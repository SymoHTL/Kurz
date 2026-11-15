using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// U12: the copy of a value that crosses into an actor, at each argument of a message and of <c>spawn</c>, and back to the caller as
/// a message's result, and of a value after <c>copy</c> at any call (the owner, 2026-11-15, #519, comment 6896192351, Q6, "Rust: deep
/// copy anywhere"). On the one thread the runtime runs, every class instance the value reaches is copied and every other block is shared
/// by its count, the sender making the copy (section 6): a value that reaches no instance crosses as any argument does, and one that
/// does arrives as a copy of its own, with a count of one, made before the message is queued. The copy is made by the type's helpers: a
/// new block for each class instance, list, map and <c>data</c> block on the way to an instance, the instance's strong and weak counts
/// at one; a class with derived classes copies through the copy slot of the instance's table (K6). A block held more
/// than once is copied once, by the runtime's memo, so that two paths that reach one block reach one copy (Q2, "Sharing kept"); a
/// <c>weak</c> reference points at the copy of its target where the copy holds the target, else null, set at the copy's end (Q3,
/// "Points at the copy"); and the blocks whose items or fields wait to be copied go on the runtime's work list, so that a chain of any
/// length crosses with no deeper stack than the nesting of its types (<c>runtime/common/runtime.kz</c>, "the copy of a value that
/// crosses"). A copy runs no program code.
/// </summary>
public sealed partial class Emitter
{
    // K6 with U12: the classes whose values a copy reads through their tables, whose tables then hold each class's copy
    private readonly HashSet<ClassSymbol> copiedThroughTable = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// U12: whether a value of the type reaches a class instance through a block a copy makes anew: an instance, an interface's value and a
    /// <c>weak</c> reference, and a list, a map, a union and a <c>data</c> value that hold one, but no function value, whose environment
    /// crosses by its count (Q5, "By written type"), and no actor's reference.
    /// </summary>
    private bool ReachesInstance(KurzType type) => ReachesInstance(type, new HashSet<string>(StringComparer.Ordinal));

    private bool ReachesInstance(KurzType type, HashSet<string> seen) => type switch
    {
        ClassType or InterfaceType or WeakRefType => true,
        ListType l => ReachesInstance(l.Element, seen),
        MapType m => ReachesInstance(m.Key, seen) || ReachesInstance(m.Value, seen),
        UnionType u => u.Cases.Any(c => ReachesInstance(c, seen)),
        DataType d when layout.IsBlock(d) => layout.Derived(d.Symbol).Any(x => seen.Add(x.Name) && Layout.Fields(new DataType(x, [])).Any(f => ReachesInstance(f, seen))),
        DataType d => seen.Add(d.Text) && Layout.Fields(d).Any(f => ReachesInstance(f, seen)),
        _ => false,
    };

    /// <summary>
    /// U12: the copy of a value that reaches a class instance (<see cref="ReachesInstance"/>), a value of its own with one count of each
    /// block it holds: the type's copy run from a slot that holds the value into one that takes the copy (<see cref="CopyRun"/>).
    /// </summary>
    private string Crossed(string value, KurzType type)
    {
        var source = fn.Alloca(Ir(type), "crossing");
        var copy = fn.Alloca(Ir(type), "copy");
        fn.Emit($"store {Ir(type)} {value}, ptr {source}");
        fn.Emit($"call void {CopyRun()}(ptr {copy}, ptr {source}, ptr {CopyInto(type)})");
        return fn.Value($"load {Ir(type)}, ptr {copy}");
    }

    /// <summary>
    /// U12: a value after <c>copy</c> at a call that is no message, the copy of a value that reaches a class instance, a temporary of the
    /// region, and any other value as it is, which no copy could tell apart from its source (Q6, "Rust: deep copy anywhere").
    /// </summary>
    private string Copied(string value, KurzType type) => ReachesInstance(type) ? Temp(Crossed(value, type), type) : value;

    /// <summary>
    /// The run of a copy: the function of the value's type copies the value at <c>%a1</c> into <c>%a0</c>, then each block the work list
    /// holds is filled by its function, the last put on it first, until the list is empty, and the runtime's <c>CopyEnd</c> points each
    /// <c>weak</c> reference of the copy and empties the memo.
    /// </summary>
    private string CopyRun() => Helper("copy.run", "void", ["ptr", "ptr", "ptr"], () =>
    {
        fn.Emit("call void %a2(ptr %a0, ptr %a1)");
        var next = fn.Label("next");
        var fill = fn.Label("fill");
        var done = fn.Label("done");
        fn.Block(next);
        var entry = fn.Value($"call ptr {RuntimeFunction("CopyNext")}()");
        fn.CondBranch(fn.Value($"icmp eq ptr {entry}, null"), done, fill);
        fn.Block(fill);
        var source = fn.Value($"load ptr, ptr {entry}");
        var copy = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {entry}, i64 8")}");
        var filler = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {entry}, i64 16")}");
        fn.Emit($"call void {filler}(ptr {copy}, ptr {source})");
        fn.Branch(next);
        fn.Block(done);
        fn.Emit($"call void {RuntimeFunction("CopyEnd")}()");
        fn.Return(null);
    });

    /// <summary>The function that copies a value of the type from the slot <c>%a1</c> into the slot <c>%a0</c> (<see cref="CopyAt"/>).</summary>
    private string CopyInto(KurzType type) => Helper($"copy.into.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        CopyAt("%a0", "%a1", type);
        fn.Return(null);
    });

    /// <summary>
    /// The value of the type in the slot <paramref name="from"/> copied into the slot <paramref name="into"/>, which may be the same
    /// slot: a value that reaches no instance with one more count, an instance as its copy, a <c>weak</c> reference kept for the copy's
    /// end, a list, a map or a block as its copy, and a plain <c>data</c> value and a union part by part.
    /// </summary>
    private void CopyAt(string into, string from, KurzType type)
    {
        if (!ReachesInstance(type))
        {
            var value = fn.Value($"load {Ir(type)}, ptr {from}");
            if (Counted(type))
            {
                Retain(value, type);
            }

            fn.Emit($"store {Ir(type)} {value}, ptr {into}");
            return;
        }

        switch (type)
        {
            case ClassType c when !Layout.Fat(c.Symbol):
                fn.Emit($"store ptr {fn.Value($"call ptr {CopyClass(c.Symbol)}(ptr {fn.Value($"load ptr, ptr {from}")})")}, ptr {into}");
                return;
            case ClassType c:
                copiedThroughTable.Add(c.Symbol);
                CopyThroughTable(into, from);
                return;
            case WeakRefType weak:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    var address = Address(value, weak.Target.Symbol);
                    var kept = fn.Label("weak");
                    var done = fn.Label("weak.done");
                    fn.CondBranch(fn.Value($"icmp eq ptr {address}, null"), done, kept);
                    fn.Block(kept);
                    fn.Emit($"call void {RuntimeFunction("CopyWeak")}(ptr {into}, ptr {address}, i1 {(Layout.Fat(weak.Target.Symbol) ? "true" : "false")})");
                    fn.Block(done);
                    return;
                }

            case ListType list:
                CopyBlockAt(into, from, CopyList(list));
                return;
            case MapType map:
                CopyBlockAt(into, from, CopyMap(map));
                return;
            case DataType d when layout.IsBlock(d):
                fn.Emit($"store ptr {fn.Value($"call ptr {CopyDataBlock(Family(d.Symbol))}(ptr {fn.Value($"load ptr, ptr {from}")})")}, ptr {into}");
                return;
            case DataType d:
                {
                    var fields = Layout.Fields(d);
                    for (var i = 0; i < fields.Count; i++)
                    {
                        CopyAt(PartAt(d, into, i), PartAt(d, from, i), fields[i]);
                    }

                    return;
                }

            case UnionType u when layout.Niche(u) is not null:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    var done = fn.Label("case.done");
                    ByCase(Tag(value, u), u, u.Cases, c =>
                    {
                        if (c is NullType)
                        {
                            fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                        }
                        else
                        {
                            CopyAt(into, from, c); // a union with a niche is laid out as its one case that holds a value
                        }

                        fn.Branch(done);
                    });
                    fn.Block(done);
                    return;
                }

            case UnionType u:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    var done = fn.Label("case.done");
                    ByCase(Tag(value, u), u, u.Cases, c =>
                    {
                        if (c is not (VoidType or NullType) && Counted(c))
                        {
                            if (ReachesInstance(c))
                            {
                                CopyAt(PartAt(u, into, 1), PartAt(u, from, 1), c);
                            }
                            else
                            {
                                Retain(Payload(value, u, c), c);
                            }
                        }

                        fn.Branch(done);
                    });
                    fn.Block(done);
                    return;
                }

            default:
                throw new InvalidOperationException($"a copy of {Types.A(type)}, which no word makes cross and the checker refuses (U12)");
        }
    }

    /// <summary>The address of part <paramref name="index"/> of a value of the type in the slot: a plain <c>data</c> value's field, a union's room at 1.</summary>
    private string PartAt(KurzType type, string slot, int index) =>
        fn.Value($"getelementptr {Ir(type)}, ptr {slot}, i32 0, i32 {Number(index)}");

    /// <summary>A list or a map in the slot <paramref name="from"/> copied by <paramref name="copy"/> into <paramref name="into"/>; the null of an empty one stays null.</summary>
    private void CopyBlockAt(string into, string from, string copy)
    {
        var block = fn.Value($"load ptr, ptr {from}");
        var made = fn.Alloca("ptr", "copied");
        fn.Emit($"store ptr null, ptr {made}");
        var some = fn.Label("block");
        var done = fn.Label("block.done");
        fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), done, some);
        fn.Block(some);
        fn.Emit($"store ptr {fn.Value($"call ptr {copy}(ptr {block})")}, ptr {made}");
        fn.Block(done);
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {made}")}, ptr {into}");
    }

    /// <summary>
    /// K6 with U12: the instance of a pair of an address and a table, of a class with derived classes, copied by the copy slot of its
    /// table, the pair of the copy and the same table stored into the slot <paramref name="into"/>.
    /// </summary>
    private void CopyThroughTable(string into, string from)
    {
        layout.Pair();
        var pair = fn.Value($"load %kz.obj, ptr {from}");
        var table = fn.Value($"extractvalue %kz.obj {pair}, 1");
        var copy = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(CopySlot)}")}");
        var made = fn.Value($"call ptr {copy}(ptr {fn.Value($"extractvalue %kz.obj {pair}, 0")})");
        fn.Emit($"store %kz.obj {Pair(made, table)}, ptr {into}");
    }

    /// <summary>
    /// In a copy helper over the block <c>%a0</c>, held more than once where <paramref name="shared"/> holds: such a block that the copy
    /// under way already copied gives that copy, one count more by <paramref name="retain"/>; else <paramref name="make"/> makes the new
    /// block, which a block held more than once leaves in the memo, and which goes on the work list with <paramref name="fill"/>.
    /// </summary>
    private void Memo(string shared, Action<string> retain, Func<string> make, string fill)
    {
        var look = fn.Label("memo");
        var fresh = fn.Label("fresh");
        fn.CondBranch(shared, look, fresh);
        fn.Block(look);
        var hit = fn.Value($"call ptr {RuntimeFunction("CopyFind")}(ptr %a0)");
        var found = fn.Label("found");
        fn.CondBranch(fn.Value($"icmp eq ptr {hit}, null"), fresh, found);
        fn.Block(found);
        retain(hit);
        fn.Return($"ptr {hit}");
        fn.Block(fresh);
        var copy = make();
        var keep = fn.Label("keep");
        var push = fn.Label("push");
        fn.CondBranch(shared, keep, push);
        fn.Block(keep);
        fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy})");
        fn.Block(push);
        fn.Emit($"call void {RuntimeFunction("CopyPush")}(ptr %a0, ptr {copy}, ptr {fill})");
        fn.Return($"ptr {copy}");
    }

    /// <summary>
    /// K1 with U12: the copy of an instance of the class, both counts at one, its fields filled from the work list; an instance held more
    /// than once, by its strong or its weak count, through the memo (<see cref="Memo"/>). A class with derived classes reaches it through
    /// the copy slot of its table (<see cref="CopyThroughTable"/>).
    /// </summary>
    private string CopyClass(ClassSymbol symbol) => Helper($"copy.class.{symbol.Name}", "ptr", ["ptr"], () =>
    {
        var block = layout.ClassBlock(symbol);
        var strong = fn.Value("load i64, ptr %a0");
        var weak = fn.Value($"load i64, ptr {fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1")}");
        var shared = fn.Value($"or i1 {fn.Value($"icmp sgt i64 {strong}, 1")}, {fn.Value($"icmp sgt i64 {weak}, 1")}");
        Memo(shared, hit => fn.Emit($"call void {RetainInstance()}(ptr {hit})"), () =>
        {
            var copy = Allocate(block);
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {copy}, i32 0, i32 1")}");
            return copy;
        }, FillClass(symbol));
    });

    /// <summary>The fields of the copy <c>%a0</c> of an instance of the class filled from the instance <c>%a1</c>, in their order.</summary>
    private string FillClass(ClassSymbol symbol) => Helper($"fill.class.{symbol.Name}", "void", ["ptr", "ptr"], () =>
    {
        for (var i = 0; i < symbol.Fields.Count; i++)
        {
            CopyAt(InstanceFieldPointer(symbol, "%a0", i), InstanceFieldPointer(symbol, "%a1", i), symbol.Fields[i].Type);
        }

        fn.Return(null);
    });

    /// <summary>M3 with U12: the copy of a list's block, held by one list, with room for its items alone, which the work list fills.</summary>
    private string CopyList(ListType type) => Helper($"copy.list.{Lower.Mangle(type)}", "ptr", ["ptr"], () =>
    {
        var shared = fn.Value($"icmp sgt i64 {fn.Value("load i64, ptr %a0")}, 1");
        Memo(shared, hit => fn.Emit($"call void {StdFunction("ListRetain")}(ptr {hit})"), () =>
        {
            var count = fn.Value($"load i32, ptr {fn.Value("getelementptr i8, ptr %a0, i64 8")}");
            var size = fn.Value($"add i64 16, {fn.Value($"mul i64 {fn.Value($"sext i32 {count} to i64")}, {ItemSize(type.Element)}")}");
            var copy = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {size})");
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i32 {count}, ptr {fn.Value($"getelementptr i8, ptr {copy}, i64 8")}");
            fn.Emit($"store i32 {count}, ptr {fn.Value($"getelementptr i8, ptr {copy}, i64 12")}");
            return copy;
        }, FillList(type));
    });

    /// <summary>The items of the copy <c>%a0</c> of a list's block filled from the block <c>%a1</c>, in their order.</summary>
    private string FillList(ListType type) => Helper($"fill.list.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        var count = fn.Value($"sext i32 {fn.Value($"load i32, ptr {fn.Value("getelementptr i8, ptr %a1, i64 8")}")} to i64");
        var into = fn.Value("getelementptr i8, ptr %a0, i64 16");
        var from = fn.Value("getelementptr i8, ptr %a1, i64 16");
        var index = fn.Alloca("i64", "item");
        fn.Emit($"store i64 0, ptr {index}");
        var check = fn.Label("item.check");
        var body = fn.Label("item");
        var done = fn.Label("items.done");
        fn.Block(check);
        var i = fn.Value($"load i64, ptr {index}");
        fn.CondBranch(fn.Value($"icmp slt i64 {i}, {count}"), body, done);
        fn.Block(body);
        var element = Ir(type.Element);
        CopyAt(fn.Value($"getelementptr {element}, ptr {into}, i64 {i}"), fn.Value($"getelementptr {element}, ptr {from}, i64 {i}"), type.Element);
        fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {index}");
        fn.Branch(check);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// M9 with U12: the copy of a map's block, its slots' bytes as they stand, since the keys' hashes are the thread's (<c>MapCopy</c>),
    /// which the work list fills: each key one count more, each value copied in its place.
    /// </summary>
    private string CopyMap(MapType type) => Helper($"copy.map.{Lower.Mangle(type)}", "ptr", ["ptr"], () =>
    {
        var shared = fn.Value($"icmp sgt i64 {fn.Value("load i64, ptr %a0")}, 1");
        Memo(shared, hit => fn.Emit($"call void {StdFunction("MapRetain")}(ptr {hit})"), () =>
        {
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            fn.Emit($"call void {StdFunction("MapRetain")}(ptr %a0)");
            var copy = fn.Value($"call ptr {StdFunction("MapCopy")}(ptr %a0, i64 {EntrySize(type)}, ptr {asked})"); // which lowers the block's count again
            var failed = fn.Label("failed");
            var made = fn.Label("made");
            fn.CondBranch(fn.Value($"icmp eq ptr {copy}, null"), failed, made);
            fn.Block(failed);
            fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
            fn.Unreachable();
            fn.Block(made);
            return copy;
        }, FillMap(type));
    });

    /// <summary>The entries of the copy <c>%a0</c> of a map's block, which holds the block's bytes: each key one count more, each value copied in its slot.</summary>
    private string FillMap(MapType type) => Helper($"fill.map.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        var next = fn.Label("slot.next");
        var done = fn.Label("slots.done");
        EachSlot("%a0", EntryType(type), next, done, slot =>
        {
            if (Counted(type.Key))
            {
                Retain(fn.Value($"load {Ir(type.Key)}, ptr {KeyAt(type, slot)}"), type.Key);
            }

            var value = ValueAt(type, slot);
            CopyAt(value, value, type.Value);
        });
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// D7 with U12: the copy of a block of the family, by the number of the type it holds, with one count, which the work list fills;
    /// the constant of a type without fields stays as it is (<see cref="HeldForEver"/>).
    /// </summary>
    private string CopyDataBlock(DataSymbol family) => Helper($"copy.block.{Lower.Mangle(new DataType(family, []))}", "ptr", ["ptr"], () =>
    {
        var count = fn.Value("load i64, ptr %a0");
        if (layout.Derived(family).Any(d => Layout.Fields(new DataType(d, [])).Count == 0))
        {
            var constant = fn.Label("constant");
            var counted = fn.Label("counted");
            fn.CondBranch(fn.Value($"icmp slt i64 {count}, 0"), constant, counted);
            fn.Block(constant);
            fn.Return("ptr %a0");
            fn.Block(counted);
        }

        var shared = fn.Value($"icmp sgt i64 {count}, 1");
        var look = fn.Label("memo");
        var fresh = fn.Label("fresh");
        fn.CondBranch(shared, look, fresh);
        fn.Block(look);
        var hit = fn.Value($"call ptr {RuntimeFunction("CopyFind")}(ptr %a0)");
        var found = fn.Label("found");
        fn.CondBranch(fn.Value($"icmp eq ptr {hit}, null"), fresh, found);
        fn.Block(found);
        fn.Emit($"call void {RetainBlock(family)}(ptr {hit})");
        fn.Return($"ptr {hit}");
        fn.Block(fresh);
        ByNumber("%a0", family, held =>
        {
            var block = layout.Block(held);
            var copy = Allocate(block);
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i32 {Number(layout.Number(new DataType(held, [])))}, ptr {fn.Value($"getelementptr {block}, ptr {copy}, i32 0, i32 1")}");
            var keep = fn.Label("keep");
            var push = fn.Label("push");
            fn.CondBranch(shared, keep, push);
            fn.Block(keep);
            fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy})");
            fn.Block(push);
            fn.Emit($"call void {RuntimeFunction("CopyPush")}(ptr %a0, ptr {copy}, ptr {FillDataBlock(held)})");
            fn.Return($"ptr {copy}");
        });
    });

    /// <summary>The fields of the copy <c>%a0</c> of a block of the type filled from the block <c>%a1</c>, in their order.</summary>
    private string FillDataBlock(DataSymbol held) => Helper($"fill.block.{Lower.Mangle(new DataType(held, []))}", "void", ["ptr", "ptr"], () =>
    {
        var block = layout.Block(held);
        var types = Layout.Fields(new DataType(held, []));
        for (var i = 0; i < types.Count; i++)
        {
            CopyAt(FieldPointer(block, "%a0", i), FieldPointer(block, "%a1", i), types[i]);
        }

        fn.Return(null);
    });
}
