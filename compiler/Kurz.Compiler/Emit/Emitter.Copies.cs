using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// U12: the copy of a value that crosses into an actor, at each argument of a message and of <c>spawn</c>, and back to the caller as
/// a message's result, and of a value after <c>copy</c> at any call (the owner, 2026-11-15, #519, comment 6896192351, Q6, "Rust: deep
/// copy anywhere"). A crossing copies every counted block the value reaches into the heap of the actor it crosses into, or of the caller
/// its result goes to, the sender making the copy (section 6; the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big
/// unbuilt"), so that the heap of an actor holds every block its code reaches and is freed whole at the actor's end (section 3): a value
/// arrives as a copy of its own, with a count of one, made before the message is queued, a string's block, a list's, a map's, a
/// <c>data</c> block, an instance and the environment of a lambda among them; a value after <c>copy</c> at a call that is no crossing
/// has every class instance it reaches copied and shares every other block by its count. The copy is made by the type's helpers: a new
/// block for each block on the way, an instance's strong and weak counts at one; a class with derived classes copies through the copy
/// slot of the instance's table (K6); whether a block that reaches no instance is copied is the session's, which the runtime's
/// <c>CopyWhole</c> reads. The copies one call makes, of its
/// arguments, share one memo, the call's session in the caller's frame, so that two paths that reach one block, in one argument or
/// in two, reach one copy (Q2, "Sharing kept"), and a result has a session of its own; each argument is copied where it is evaluated,
/// left to right, and the memo holds a count of each block it maps until the call's last copy (section 6). A <c>weak</c> reference
/// points at the copy of its target where the call's copies hold the target, else null, set at the call's last copy (Q3, "Points at
/// the copy"); and the blocks whose items or fields wait to be copied go on the runtime's work list, so that a chain of any
/// length crosses with no deeper stack than the nesting of its types (<c>runtime/common/runtime.kz</c>, "the copy of a value that
/// crosses"). A copy runs no program code.
/// </summary>
public sealed partial class Emitter
{
    // K6 with U12: the classes whose values a copy reads through their tables, whose tables then hold each class's copy
    private readonly HashSet<ClassSymbol> copiedThroughTable = new(ReferenceEqualityComparer.Instance);

    // U12: the session of the call each argument after `copy` stands in, where the call makes more than one copy (CallSession)
    private readonly Dictionary<Expression, string> copySessions = new(ReferenceEqualityComparer.Instance);

    // U12: the sessions of more than one copy begun in each region, which its end, or a jump out of it, ends (LetGo)
    private readonly Dictionary<List<(string Value, KurzType Type)>, List<string>> regionSessions = new(ReferenceEqualityComparer.Instance);

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
    /// U12: the copy of a value, a value of its own with one count of each block it holds: the type's copy run from a slot that holds the
    /// value into one that takes the copy, in the call's session, else in a session of its own (<see cref="CopyRun"/>), whose copies go
    /// into the heap <paramref name="heap"/> of a crossing, every counted block among them, or, for null, after <c>copy</c> at a call
    /// that is no crossing, into the heap the allocation entry serves, the class instances among them (Q5). A function value crosses by
    /// <paramref name="copyInto"/>, the copy of the environment of its lambda (<see cref="CopyEnvironmentInto"/>).
    /// </summary>
    private string Crossed(string value, KurzType type, string? session, string? heap, string? copyInto = null)
    {
        var source = fn.Alloca(Ir(type), "crossing");
        var copy = fn.Alloca(Ir(type), "copy");
        fn.Emit($"store {Ir(type)} {value}, ptr {source}");
        fn.Emit($"call void {CopyRun()}(ptr {copy}, ptr {source}, ptr {copyInto ?? CopyInto(type)}, ptr {session ?? CopySession(1, heap)})");
        return fn.Value($"load {Ir(type)}, ptr {copy}");
    }

    /// <summary>
    /// U12: the value of the argument <paramref name="e"/> after <c>copy</c> at a call that is no message, the copy of a value that
    /// reaches a class instance, made in the call's session, a temporary of the region, and any other value as it is, which no copy
    /// could tell apart from its source (Q6, "Rust: deep copy anywhere").
    /// </summary>
    private string Copied(Expression e, string value, KurzType type) =>
        ReachesInstance(type) ? Temp(Crossed(value, type, copySessions.Remove(e, out var session) ? session : null, null), type) : value;

    /// <summary>
    /// U12: the session the copies of a call share where it makes <paramref name="copies"/> of them, more than one, into the heap
    /// <paramref name="heap"/> of a crossing, else null, the one copy then working in a session of its own (<see cref="Crossed"/>): the
    /// memo of a call is the call's, not the argument's (Q2, "Sharing kept").
    /// </summary>
    private string? CallSession(int copies, string? heap) => copies > 1 ? CopySession(copies, heap) : null;

    /// <summary>
    /// U12: the session of <paramref name="copies"/> copies begun, the runtime's eleven words in the caller's frame (<c>runtime/common/
    /// runtime.kz</c>, "the copy of a value that crosses"), zero from the function's start, so that the end of a region whose code did
    /// not reach the session's start finds none under way: an empty memo, no <c>weak</c> reference, the copies left to make, whether
    /// every block goes into the memo, as it does in a session of more than one copy, where a later argument may reach a block by a
    /// path that holds no count of it, the heap <paramref name="heap"/> of a crossing, null for a <c>copy</c> at a call that is no
    /// crossing, and the two words of the list of the open sessions of the activation that runs it. Such a session ends at its last
    /// copy, or, where the code leaves the region before it, at the region's end (<see cref="LetGo"/>), and a session of more than one
    /// copy is listed among the running activation's open ones, which a crash between two of its copies ends (the runtime's
    /// <c>CopyOpen</c>; U19).
    /// </summary>
    private string CopySession(int copies, string? heap)
    {
        var session = fn.Alloca("[11 x i64]", "session", "zeroinitializer");
        fn.Emit($"store [11 x i64] [i64 0, i64 0, i64 0, i64 0, i64 0, i64 0, i64 {Number(copies)}, i64 {(copies > 1 ? 1 : 0)}, i64 0, i64 0, i64 0], ptr {session}");
        if (heap is not null)
        {
            fn.Emit($"store ptr {heap}, ptr {fn.Value($"getelementptr [11 x i64], ptr {session}, i32 0, i32 8")}");
        }

        if (copies > 1)
        {
            fn.Emit($"call void {RuntimeFunction("CopyOpen")}(ptr {session})");
        }

        if (copies > 1 && regions.Count > 0)
        {
            if (!regionSessions.TryGetValue(regions[^1], out var begun))
            {
                regionSessions[regions[^1]] = begun = [];
            }

            begun.Add(session);
        }

        return session;
    }

    /// <summary>
    /// U12: at a region's end, or a jump out of it, the sessions begun in it ended where their calls' code did not reach their last
    /// copy, a case propagated out of a later argument among them, before the region lets go of the copies, so that their <c>weak</c>
    /// references are pointed as a whole call's are and the memo's counts let go of (the runtime's <c>CopyAbort</c>).
    /// </summary>
    private void EndSessions(List<(string Value, KurzType Type)> temporaries)
    {
        if (regionSessions.TryGetValue(temporaries, out var begun))
        {
            foreach (var session in begun)
            {
                fn.Emit($"call void {RuntimeFunction("CopyAbort")}(ptr {session})");
            }
        }
    }

    /// <summary>
    /// The run of a copy in the session <c>%a3</c>, which it names to the runtime first (<c>CopyUse</c>), since another call's session
    /// may have run since its last: the function of the value's type copies the value at <c>%a1</c> into <c>%a0</c>, then each block the
    /// work list holds is filled by its function, the last put on it first, until the list is empty, and the runtime's <c>CopyLeft</c>
    /// counts the copy made, the session's last pointing each <c>weak</c> reference of its copies and emptying the memo.
    /// </summary>
    private string CopyRun() => Helper("copy.run", "void", ["ptr", "ptr", "ptr", "ptr"], () =>
    {
        fn.Emit($"call void {RuntimeFunction("CopyUse")}(ptr %a3)");
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
        fn.Emit($"call void {RuntimeFunction("CopyLeft")}(ptr %a3)");
        fn.Return(null);
    });

    /// <summary>
    /// U12: the function the runtime calls at a session's end with a block's address and, for a <c>weak</c> reference of a class with
    /// derived classes, its table: the count the memo held of a block of the type let go of, an instance's by its own class's release,
    /// or the weak count the session held of a <c>weak</c> reference's target.
    /// </summary>
    private string CopyLetGo(KurzType type) => Helper($"copy.let.go.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        switch (type)
        {
            case ClassType c:
                fn.Emit($"call void {ReleaseInstance(c.Symbol)}(ptr %a0)");
                break;
            case WeakRefType w when Layout.Fat(w.Target.Symbol):
                fn.Emit($"call void {ReleaseWeakThroughTable()}(%kz.obj {Pair("%a0", "%a1")})");
                break;
            case WeakRefType w:
                fn.Emit($"call void {ReleaseWeak(w.Target.Symbol)}(ptr %a0)");
                break;
            default:
                Release("%a0", type);
                break;
        }

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
    /// slot: a value that holds no block as it is; one that holds a block but reaches no instance copied in a crossing's session (Q5),
    /// else with one more count; an instance as its copy, a <c>weak</c> reference kept for the copy's end, a string, a list, a map or a
    /// block as its copy, and a plain <c>data</c> value and a union part by part.
    /// </summary>
    private void CopyAt(string into, string from, KurzType type)
    {
        if (!Counted(type))
        {
            fn.Emit($"store {Ir(type)} {fn.Value($"load {Ir(type)}, ptr {from}")}, ptr {into}");
            return;
        }

        if (ReachesInstance(type))
        {
            CopyParts(into, from, type);
            return;
        }

        var whole = fn.Label("whole");
        var shared = fn.Label("shared");
        var done = fn.Label("copied");
        fn.CondBranch(fn.Value($"call i1 {RuntimeFunction("CopyWhole")}()"), whole, shared);
        fn.Block(whole);
        CopyParts(into, from, type);
        fn.Branch(done);
        fn.Block(shared);
        var value = fn.Value($"load {Ir(type)}, ptr {from}");
        Retain(value, type);
        fn.Emit($"store {Ir(type)} {value}, ptr {into}");
        fn.Branch(done);
        fn.Block(done);
    }

    /// <summary>The value of a counted type in the slot <paramref name="from"/> copied into the slot <paramref name="into"/> as <see cref="CopyAt"/> copies it, block by block.</summary>
    private void CopyParts(string into, string from, KurzType type)
    {
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
                    fn.Emit($"call void {RuntimeFunction("CopyWeak")}(ptr {into}, ptr {address}, i1 {(Layout.Fat(weak.Target.Symbol) ? "true" : "false")}, ptr {CopyLetGo(weak)})");
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
                            CopyAt(PartAt(u, into, 1), PartAt(u, from, 1), c);
                        }

                        fn.Branch(done);
                    });
                    fn.Block(done);
                    return;
                }

            case PrimitiveType { Kind: Primitive.String }:
                fn.Emit($"store %kz.str {fn.Value($"call %kz.str {CopyString()}(%kz.str {fn.Value($"load %kz.str, ptr {from}")})")}, ptr {into}");
                return;
            case FunctionType or HandleType or HasherType or CommandType or ChildProcessType:
                {
                    // a value no crossing reaches, as the checker refuses it inside what crosses (U12), and which a `copy` shares
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    Retain(value, type);
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    return;
                }

            default:
                throw new InvalidOperationException($"a copy of {Types.A(type)}, which no word makes cross and the checker refuses (U12)");
        }
    }

    /// <summary>
    /// Q5: the copy of a string in a block of its own of the heap the allocation entry serves, by the runtime's <c>CopyBytes</c>; a literal,
    /// which holds no block, as it is.
    /// </summary>
    private string CopyString() => Helper("copy.string", "%kz.str", ["%kz.str"], () =>
    {
        var count = fn.Value("extractvalue %kz.str %a0, 1");
        var held = fn.Label("held");
        var literal = fn.Label("literal");
        fn.CondBranch(fn.Value($"icmp slt i64 {count}, 0"), held, literal); // the mark: a block
        fn.Block(literal);
        fn.Return("%kz.str %a0");
        fn.Block(held);
        var bytes = fn.Value($"call ptr {RuntimeFunction("CopyBytes")}(ptr {fn.Value("extractvalue %kz.str %a0, 0")}, i64 {fn.Value($"and i64 {count}, 9223372036854775807")})");
        fn.Return($"%kz.str {fn.Value($"insertvalue %kz.str %a0, ptr {bytes}, 0")}");
    });

    /// <summary>
    /// U12 with Q5: the function that copies a function value whose lambda captures values of the types <paramref name="captures"/> from the
    /// slot <c>%a1</c> into the slot <c>%a0</c>, a value of its own: a new environment, held by one value, with the function that lets go
    /// of it, each value it captured copied as <see cref="CopyAt"/> copies it; a lambda that captures nothing has no environment to copy.
    /// </summary>
    private string CopyEnvironmentInto(List<KurzType> captures) => Helper($"copy.into.env{string.Concat(captures.Select(t => "." + Lower.Mangle(t)))}", "void", ["ptr", "ptr"], () =>
    {
        var value = fn.Value("load %kz.fn, ptr %a1");
        var environment = fn.Value($"extractvalue %kz.fn {value}, 1");
        var none = fn.Label("none");
        var some = fn.Label("environment");
        fn.CondBranch(fn.Value($"icmp eq ptr {environment}, null"), none, some);
        fn.Block(none);
        fn.Emit($"store %kz.fn {value}, ptr %a0");
        fn.Return(null);
        fn.Block(some);
        var shape = $"{{ i64, ptr{string.Concat(captures.Select(t => ", " + Ir(t)))} }}";
        var copy = Allocate(shape);
        fn.Emit($"store i64 1, ptr {copy}");
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {fn.Value($"getelementptr {shape}, ptr {environment}, i32 0, i32 1")}")}, ptr {fn.Value($"getelementptr {shape}, ptr {copy}, i32 0, i32 1")}");
        for (var i = 0; i < captures.Count; i++)
        {
            CopyAt(Capture(shape, copy, i), Capture(shape, environment, i), captures[i]);
        }

        fn.Emit($"store %kz.fn {fn.Value($"insertvalue %kz.fn {value}, ptr {copy}, 1")}, ptr %a0");
        fn.Return(null);
    });

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
    /// In a copy helper over the block <c>%a0</c> of the type, held more than once where <paramref name="shared"/> holds, and in a session
    /// where every block goes into the memo: such a block that the session's copies already copied gives that copy, one count more by
    /// <paramref name="retain"/>; else <paramref name="make"/> makes the new block, which such a block leaves in the memo, with the
    /// function that lets go of the memo's count of the block (<see cref="CopyLetGo"/>), and which goes on the work list with
    /// <paramref name="fill"/>.
    /// </summary>
    private void Memo(KurzType type, string shared, Action<string> retain, Func<string> make, string fill)
    {
        shared = fn.Value($"or i1 {shared}, {fn.Value($"call i1 {RuntimeFunction("CopyEvery")}()")}");
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
        fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy}, ptr {CopyLetGo(type)})");
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
        Memo(new ClassType(symbol), shared, hit => fn.Emit($"call void {RetainInstance()}(ptr {hit})"), () =>
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
        Memo(type, shared, hit => fn.Emit($"call void {StdFunction("ListRetain")}(ptr {hit})"), () =>
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
        Memo(type, shared, hit => fn.Emit($"call void {StdFunction("MapRetain")}(ptr {hit})"), () =>
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

    /// <summary>
    /// The entries of the copy <c>%a0</c> of a map's block, which holds the block's bytes: each key and each value copied in its slot, a
    /// key with the hash of its bytes, which a copy keeps.
    /// </summary>
    private string FillMap(MapType type) => Helper($"fill.map.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        var next = fn.Label("slot.next");
        var done = fn.Label("slots.done");
        EachSlot("%a0", EntryType(type), next, done, slot =>
        {
            var key = KeyAt(type, slot);
            CopyAt(key, key, type.Key);
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

        var shared = fn.Value($"or i1 {fn.Value($"icmp sgt i64 {count}, 1")}, {fn.Value($"call i1 {RuntimeFunction("CopyEvery")}()")}");
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
            fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy}, ptr {CopyLetGo(new DataType(family, []))})");
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
