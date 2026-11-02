using System.Numerics;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Lists (M9) and the counting of their blocks and of the blocks of strings (design record, section 3: compiler-inserted reference
/// counting; section 4, the counter: the owner, 2026-10-24, "Full counting now", and 2026-10-29, "Bit in the count"). A list value is the address of its
/// block in the standard library, null for a list without items; the block counts the list values that hold it, and
/// <c>std/std.kz</c> alone reads its layout. A string made at run time holds a block of the standard library in front of its bytes,
/// which counts the string values that hold it, and the highest bit of the string's count of bytes marks it; a literal holds none.
/// A value of a type that holds a list or a string, a list or a string itself, a plain data value with such a field or a union with
/// such a case, is counted: every place that holds one,
/// a variable, a field, an item, a result, holds one count of each block in it. A value an expression makes, a call's result, a
/// list, a <c>with</c>, a <c>match</c> or an <c>else</c> as a value, is a temporary of the statement, or of the arm or the operand
/// it is made in, and the end of that region lets go of what no place took over. A value read from a variable, a field, an item
/// or a parameter is borrowed: the place it goes to counts it once more. A variable lets go of its value at the end of its block,
/// and at a <c>return</c>, a <c>break</c> and a <c>continue</c> that leave the block; a <c>throw</c> ends the process. A parameter
/// is borrowed from the caller for the call. The blocks of data types in a relation of inheritance or that hold themselves are
/// never freed, so what they hold keeps the count it was stored with. A function value is counted as a list is, by the
/// environment of its lambda (<see cref="EmitLambda"/>), a map and a hasher by their blocks, and a file or a socket by the block of its
/// handle, which closes it at its last count (S5); values of every other type are not counted.
/// </summary>
public sealed partial class Emitter
{
    // the temporaries of each region being emitted, the innermost last, and the counted variables of each block, the innermost last
    private List<List<(string Value, KurzType Type)>> regions = [];
    private List<List<(string Slot, KurzType Type)>> scopes = [];

    /// <summary>Whether a value of the type holds the block of a list, of a map, of a string or of a file's handle or the environment of a lambda: a list, a map, a string, a file or a socket, a hasher, a function value, a plain data value with a field that does, a union with a case that does.</summary>
    private bool Counted(KurzType type) => type switch
    {
        ListType or MapType or FunctionType or HandleType or HasherType or ClassType or PrimitiveType { Kind: Primitive.String } => true,
        DataType d when !layout.IsBlock(d) => Layout.Fields(d).Any(Counted),
        UnionType u => u.Cases.Any(c => c is not VoidType && Counted(c)),
        _ => false,
    };

    /// <summary>The IR name of a <c>pub</c> function of the standard library, which the emitted code calls by its name.</summary>
    private string StdFunction(string name) => Instance(
        std.Tree.Statements.OfType<FunctionDeclaration>()
            .Where(d => d.Name == name && (d.Modifiers & Modifiers.Pub) != 0)
            .Select(d => std.Model.FunctionOf(d))
            .SingleOrDefault() ?? throw new InvalidOperationException($"{std.File} has no `pub` function `{name}`, which the emitted code calls"),
        NoMap);

    // ---- regions and scopes ----------------------------------------------------------------------

    /// <summary>The value as a temporary of the region being emitted, when its type is counted: the region's end lets go of it unless a place took it over.</summary>
    private string Temp(string value, KurzType type)
    {
        if (Counted(type) && value != "null")
        {
            if (regions.Count == 0)
            {
                throw new InvalidOperationException($"a counted value `{value}` made outside a region");
            }

            regions[^1].Add((value, type));
        }

        return value;
    }

    /// <summary>
    /// The value of the expression, for a place that holds it: a temporary taken over from its region, any other value counted once
    /// more.
    /// </summary>
    private string Owned(Expression e)
    {
        var value = EmitExpression(e);
        var type = TypeOf(e);
        if (Counted(type) && !Take(value))
        {
            Retain(value, type);
        }

        return value;
    }

    private bool Take(string value)
    {
        for (var r = regions.Count - 1; r >= 0; r--)
        {
            var i = regions[r].FindLastIndex(t => t.Value == value);
            if (i >= 0)
            {
                regions[r].RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Emits <paramref name="body"/> as a region of its own, whose end lets go of its temporaries where the code goes on.</summary>
    private void InRegion(Action body)
    {
        regions.Add([]);
        try
        {
            body();
            if (!fn.Terminated)
            {
                LetGo(regions[^1]);
            }
        }
        finally
        {
            regions.RemoveAt(regions.Count - 1);
        }
    }

    /// <summary>Emits <paramref name="body"/> as a block of variables, whose end lets go of their values where the code goes on.</summary>
    private void InScope(Action body)
    {
        scopes.Add([]);
        try
        {
            body();
            if (!fn.Terminated)
            {
                LetGoOf(scopes[^1]);
            }
        }
        finally
        {
            scopes.RemoveAt(scopes.Count - 1);
        }
    }

    /// <summary>A variable of the block being emitted, which holds its value until the block's end.</summary>
    private void Declare(string slot, KurzType type)
    {
        if (Counted(type))
        {
            scopes[^1].Add((slot, type));
        }
    }

    /// <summary>A jump out of the regions and the blocks from these depths on, a <c>return</c> from 0: it lets go of their temporaries and variables, the innermost first.</summary>
    private void Leaving(int fromRegion, int fromScope)
    {
        for (var r = regions.Count - 1; r >= fromRegion; r--)
        {
            LetGo(regions[r]);
        }

        for (var s = scopes.Count - 1; s >= fromScope; s--)
        {
            LetGoOf(scopes[s]);
        }
    }

    private void LetGo(List<(string Value, KurzType Type)> temporaries)
    {
        for (var i = temporaries.Count - 1; i >= 0; i--)
        {
            Release(temporaries[i].Value, temporaries[i].Type);
        }
    }

    private void LetGoOf(List<(string Slot, KurzType Type)> variables)
    {
        for (var i = variables.Count - 1; i >= 0; i--)
        {
            var (slot, type) = variables[i];
            Release(fn.Value($"load {Ir(type)}, ptr {slot}"), type);
        }
    }

    // ---- the counts --------------------------------------------------------------------------------

    /// <summary>One more holder of every block the value holds.</summary>
    private void Retain(string value, KurzType type)
    {
        if (type is ListType or HasherType) // H3: a hasher's state is the block of a list of bytes
        {
            fn.Emit($"call void {StdFunction("ListRetain")}(ptr {value})");
            return;
        }

        if (type is MapType)
        {
            fn.Emit($"call void {StdFunction("MapRetain")}(ptr {value})");
            return;
        }

        if (type is FunctionType)
        {
            fn.Emit($"call void {RetainEnvironment()}(%kz.fn {value})");
            return;
        }

        if (type is HandleType)
        {
            fn.Emit($"call void {RetainHandle()}(ptr {value})");
            return;
        }

        if (type is ClassType)
        {
            fn.Emit($"call void {RetainInstance()}(ptr {value})");
            return;
        }

        if (type is PrimitiveType { Kind: Primitive.String })
        {
            if (!IsLiteral(value))
            {
                fn.Emit($"call void {StringCount("retain", "StringRetain")}(%kz.str {value})");
            }

            return;
        }

        var name = Helper($"retain.{Lower.Mangle(type)}", "void", [Ir(type)], () => EachHeld("%a0", type, Retain));
        fn.Emit($"call void {name}({Ir(type)} {value})");
    }

    /// <summary>One holder less of every block the value holds; a list's block that loses its last holder lets go of its items and is freed.</summary>
    private void Release(string value, KurzType type)
    {
        if (type is HasherType)
        {
            Release(value, new ListType(Types.Byte)); // H3: a hasher's state is the block of a list of bytes
            return;
        }

        if (type is FunctionType)
        {
            fn.Emit($"call void {ReleaseEnvironment()}(%kz.fn {value})");
            return;
        }

        if (type is HandleType)
        {
            fn.Emit($"call void {ReleaseHandle()}(ptr {value})");
            return;
        }

        if (type is ClassType c)
        {
            fn.Emit($"call void {ReleaseInstance(c.Symbol)}(ptr {value})");
            return;
        }

        if (type is PrimitiveType { Kind: Primitive.String })
        {
            if (!IsLiteral(value))
            {
                fn.Emit($"call void {StringCount("release", "StringRelease")}(%kz.str {value})");
            }

            return;
        }

        var name = Helper($"release.{Lower.Mangle(type)}", "void", [Ir(type)], () =>
        {
            if (type is MapType map)
            {
                MapReleaseBody(map);
                return;
            }

            if (type is ListType list)
            {
                var last = fn.Value($"call i1 {StdFunction("ListRelease")}(ptr %a0)");
                var free = fn.Label("free");
                var held = fn.Label("held");
                fn.CondBranch(last, free, held);
                fn.Block(free);
                if (Counted(list.Element))
                {
                    EachItem("%a0", list.Element, item => Release(item, list.Element));
                }

                fn.Emit($"call void {StdFunction("ListFree")}(ptr %a0, i64 {ItemSize(list.Element)})");
                fn.Return(null);
                fn.Block(held);
                fn.Return(null);
                return;
            }

            EachHeld("%a0", type, Release);
        });
        fn.Emit($"call void {name}({Ir(type)} {value})");
    }

    /// <summary>Whether the value is a string the module holds as a constant, a literal, which holds no block (<see cref="StringValue"/>).</summary>
    private static bool IsLiteral(string value) => value.StartsWith("{ ptr ", StringComparison.Ordinal) || value == "zeroinitializer";

    /// <summary>
    /// The helper that raises or lowers the count of a string's block, <paramref name="name"/> of the standard library: of a string
    /// whose count of bytes carries the mark in its highest bit, and nothing for a literal; a block that loses its last holder is
    /// freed.
    /// </summary>
    private string StringCount(string kind, string name) => Helper($"{kind}.string", "void", ["%kz.str"], () =>
    {
        var counted = fn.Label("counted");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value($"icmp slt i64 {fn.Value("extractvalue %kz.str %a0, 1")}, 0"), counted, done); // the mark: a block
        fn.Block(counted);
        var bytes = fn.Value("extractvalue %kz.str %a0, 0");
        if (kind == "retain")
        {
            fn.Emit($"call void {StdFunction(name)}(ptr {bytes})");
        }
        else
        {
            var free = fn.Label("free");
            fn.CondBranch(fn.Value($"call i1 {StdFunction(name)}(ptr {bytes})"), free, done);
            fn.Block(free);
            fn.Emit($"call void {StdFunction("StringFree")}(ptr {bytes})");
        }

        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>In a helper over a plain data value or a union: <paramref name="each"/> for each counted field, or for the case value when its case is counted; then the return.</summary>
    private void EachHeld(string value, KurzType type, Action<string, KurzType> each)
    {
        switch (type)
        {
            case DataType d:
                {
                    var fields = Layout.Fields(d);
                    for (var i = 0; i < fields.Count; i++)
                    {
                        if (Counted(fields[i]))
                        {
                            each(fn.Value($"extractvalue {Ir(d)} {value}, {Number(i)}"), fields[i]);
                        }
                    }

                    fn.Return(null);
                    break;
                }

            case UnionType u:
                ByCase(Tag(value, u), u, u.Cases, c =>
                {
                    if (c is not VoidType && Counted(c))
                    {
                        each(Payload(value, u, c), c);
                    }

                    fn.Return(null);
                });
                break;
            default:
                throw new InvalidOperationException($"{Types.A(type)} holds no list");
        }
    }

    // ---- the members of a list (M9, T14) -------------------------------------------------------------

    /// <summary>
    /// M11: <c>[a, b, c]</c>, its items run in order, each counted and a temporary of the region before the list holds it, so that an
    /// item that leaves the function lets go of the ones before it (O3); then one block with room for all of them, as Rust's
    /// <c>vec![a, b, c]</c> makes one, and a block the system refuses raises <c>allocation-failed</c> with the bytes asked for (M9).
    /// <c>[]</c> takes no block, as <c>List&lt;T&gt;()</c> takes none.
    /// </summary>
    private string EmitListLiteral(ListLiteral l, ListType list)
    {
        if (l.Items.Count == 0)
        {
            return "null";
        }

        var items = l.Items.Select(e => Temp(Convert(Owned(e), TypeOf(e), list.Element, e.Line), list.Element)).ToList();
        var asked = fn.Alloca("i64", "asked");
        var room = fn.Value($"call ptr {StdFunction("ListAllocate")}(i64 {items.Count}, i64 {ItemSize(list.Element)}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {room}, null"), asked);
        for (var i = 0; i < items.Count; i++)
        {
            Take(items[i]); // the list holds it from here
            fn.Emit($"store {Ir(list.Element)} {items[i]}, ptr {fn.Value($"getelementptr {Ir(list.Element)}, ptr {room}, i64 {i}")}");
        }

        return fn.Value($"call ptr {StdFunction("ListTakeOver")}(ptr {room}, i64 {items.Count})");
    }

    /// <summary>The bytes an item of the type takes in a list's block, as LLVM lays out its IR type.</summary>
    private string ItemSize(KurzType type)
    {
        var end = fn.Value($"getelementptr {Ir(type)}, ptr null, i32 1");
        return fn.Value($"ptrtoint ptr {end} to i64");
    }

    /// <summary>The count of a list's items, an <c>int</c>.</summary>
    private string ListCount(string list) => fn.Value($"call i32 {StdFunction("ListCount")}(ptr {list})");

    /// <summary><paramref name="each"/> for every item of the list, in order, read as the type of its items.</summary>
    private void EachItem(string list, KurzType element, Action<string> each)
    {
        var count = ListCount(list);
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})");
        var counter = fn.Alloca("i32", "item");
        fn.Emit($"store i32 0, ptr {counter}");
        var check = fn.Label("items");
        var body = fn.Label("item");
        var done = fn.Label("items.done");
        fn.Block(check);
        var i = fn.Value($"load i32, ptr {counter}");
        fn.CondBranch(fn.Value($"icmp slt i32 {i}, {count}"), body, done);
        fn.Block(body);
        var wide = fn.Value($"sext i32 {i} to i64");
        each(fn.Value($"load {Ir(element)}, ptr {fn.Value($"getelementptr {Ir(element)}, ptr {items}, i64 {wide}")}"));
        fn.Emit($"store i32 {fn.Value($"add i32 {i}, 1")}, ptr {counter}");
        fn.Branch(check);
        fn.Block(done);
    }

    /// <summary>
    /// T14: <c>list[i]</c>, the item at the position counted from 0; a position below 0 or at the count or beyond raises
    /// <c>index-out-of-range</c> at the line. <c>s.Bytes[i]</c> reads the string's byte in place (T15), and <c>list[a..&lt;b]</c> and
    /// <c>list[a..b]</c> are a part of the list (M9).
    /// </summary>
    private string EmitListIndex(IndexExpression index, ListType type)
    {
        if (index.Index is RangeExpression range)
        {
            return EmitListPart(index, type, range);
        }

        if (Unwrap(index.Target) is MemberAccess { Member: "Bytes" } bytes && TypeOf(bytes.Target) is PrimitiveType { Kind: Primitive.String })
        {
            return StringByte(index, bytes.Target);
        }

        return ListItem(EmitExpression(index.Target), index, type);
    }

    /// <summary>M9: the item of the list at the index <paramref name="index"/> names, read in place; a position outside its items raises <c>index-out-of-range</c> at the line.</summary>
    private string ListItem(string list, IndexExpression index, ListType type)
    {
        var indexType = TypeOf(index.Index);
        var position = Resize(EmitExpression(index.Index), indexType, Types.IsSigned(indexType) ? Types.Long : Types.ULong);
        var count = fn.Value($"zext i32 {ListCount(list)} to i64");
        var inside = fn.Label("index");
        var outside = fn.Label("out.of.range");
        fn.CondBranch(fn.Value($"icmp ult i64 {position}, {count}"), inside, outside); // a negative position reads as a number above every count
        fn.Block(outside);
        Raise(ErrorIds.IndexOutOfRange, index.Line);
        fn.Block(inside);
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})");
        return fn.Value($"load {Ir(type.Element)}, ptr {fn.Value($"getelementptr {Ir(type.Element)}, ptr {items}, i64 {position}")}");
    }

    /// <summary>
    /// M9, M4, T14: <c>xs[i] = value</c> on a <c>mut</c> variable or parameter, or on a path of fields from one, as C# and Rust write an item (section 4, the owner,
    /// 2026-10-30, "As C# and Rust"): the value first, then the position, then the list the variable holds as they left it, as a path
    /// assignment reads its value first (M4); a position below 0 or at the count or beyond raises <c>index-out-of-range</c> at the line
    /// (T14), and a block other values hold is copied before the write (M3), as <c>Add</c> copies it. The item takes the value over and
    /// lets go of the one it held after it holds the new one, so that <c>xs[i] = xs[j]</c> writes an item the list still counts.
    /// </summary>
    private void EmitListWrite(Assignment a, IndexExpression index, ListType type)
    {
        string value = null!;
        string position = null!;
        InRegion(() =>
        {
            value = Convert(Owned(a.Value), TypeOf(a.Value), type.Element, a.Value.Line);
            var indexType = TypeOf(index.Index);
            position = Resize(EmitExpression(index.Index), indexType, Types.IsSigned(indexType) ? Types.Long : Types.ULong);
        });
        var place = CollectionPlace(index.Target);
        var count = fn.Value($"zext i32 {ListCount(fn.Value($"load ptr, ptr {place}"))} to i64");
        var inside = fn.Label("index");
        var outside = fn.Label("out.of.range");
        fn.CondBranch(fn.Value($"icmp ult i64 {position}, {count}"), inside, outside); // a negative position reads as a number above every count
        fn.Block(outside);
        Raise(ErrorIds.IndexOutOfRange, index.Line);
        fn.Block(inside);
        var write = Helper($"write.{Lower.Mangle(type)}", "void", ["ptr", "i64", Ir(type.Element)], () =>
        {
            var size = ItemSize(type.Element);
            var asked = fn.Alloca("i64", "asked");
            var block = fn.Value("load ptr, ptr %a0");
            var copy = fn.Label("copy");
            var own = fn.Label("own");
            var failed = fn.Label("failed");
            fn.CondBranch(fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})"), copy, own);
            fn.Block(copy);
            var copied = fn.Value($"call ptr {StdFunction("ListCopy")}(ptr {block}, i64 {size}, ptr {asked})"); // which lowers the old block's count
            var keep = fn.Label("copied");
            fn.CondBranch(fn.Value($"icmp eq ptr {copied}, null"), failed, keep);
            fn.Block(failed);
            fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
            fn.Unreachable();
            fn.Block(keep);
            if (Counted(type.Element))
            {
                EachItem(copied, type.Element, held => Retain(held, type.Element)); // the copy holds the items' blocks too
            }

            fn.Emit($"store ptr {copied}, ptr %a0");
            fn.Branch(own);
            fn.Block(own);
            var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {fn.Value("load ptr, ptr %a0")})");
            Store("%a2", type.Element, fn.Value($"getelementptr {Ir(type.Element)}, ptr {items}, i64 %a1"));
            fn.Return(null);
        });
        fn.Emit($"call void {write}(ptr {place}, i64 {position}, {Ir(type.Element)} {value})");
    }

    /// <summary>T15: <c>s.Bytes[i]</c>, the byte at the position of the string's bytes, read in place without a list of them; a position outside them raises <c>index-out-of-range</c> at the line.</summary>
    private string StringByte(IndexExpression index, Expression target)
    {
        var text = EmitExpression(target);
        var indexType = TypeOf(index.Index);
        var position = Resize(EmitExpression(index.Index), indexType, Types.IsSigned(indexType) ? Types.Long : Types.ULong);
        var count = fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {text}, 1")}, 9223372036854775807"); // without the mark of a block
        var inside = fn.Label("byte");
        var outside = fn.Label("out.of.range");
        fn.CondBranch(fn.Value($"icmp ult i64 {position}, {count}"), inside, outside); // a negative position reads as a number above every count
        fn.Block(outside);
        Raise(ErrorIds.IndexOutOfRange, index.Line);
        fn.Block(inside);
        var bytes = fn.Value($"extractvalue %kz.str {text}, 0");
        return fn.Value($"load i8, ptr {fn.Value($"getelementptr i8, ptr {bytes}, i64 {position}")}");
    }

    /// <summary>
    /// M9: <c>list[a..&lt;b]</c> and <c>list[a..b]</c>, a new list of the items from position <c>a</c> to <c>b</c>, the second with
    /// <c>b</c>, copied into a block of its own, which holds each item's own blocks once more (section 4, the owner, 2026-10-29, "A
    /// List&lt;T&gt;, a value"). A range whose end lies below its start raises <c>reversed-range-at-run-time</c>, as C8's range does, and
    /// then a start below 0 or an end past the count <c>index-out-of-range</c>, at the line.
    /// </summary>
    private string EmitListPart(IndexExpression index, ListType type, RangeExpression range)
    {
        var list = EmitExpression(index.Target);
        var numbers = TypeOf(range); // the type of the range's numbers (C8)
        var signed = Types.IsSigned(numbers);
        var wide = signed ? Types.Long : Types.ULong;
        var start = Resize(Convert(EmitExpression(range.Start), TypeOf(range.Start), numbers, range.Line), numbers, wide);
        var end = Resize(Convert(EmitExpression(range.End), TypeOf(range.End), numbers, range.Line), numbers, wide);
        var reversed = fn.Label("reversed");
        var ordered = fn.Label("ordered");
        fn.CondBranch(fn.Value($"icmp {(signed ? "slt" : "ult")} i64 {end}, {start}"), reversed, ordered);
        fn.Block(reversed);
        Raise(ErrorIds.ReversedRangeAtRunTime, index.Line);
        fn.Block(ordered);
        var count = fn.Value($"zext i32 {ListCount(list)} to i64");

        // a negative end reads as a number above every count, and the end of `a..b` is checked before one is added to it
        var endInside = fn.Value($"icmp {(range.Inclusive ? "ult" : "ule")} i64 {end}, {count}");
        var startInside = fn.Value($"icmp ule i64 {start}, {count}");
        var inside = fn.Label("part");
        var outside = fn.Label("out.of.range");
        fn.CondBranch(fn.Value($"and i1 {startInside}, {endInside}"), inside, outside);
        fn.Block(outside);
        Raise(ErrorIds.IndexOutOfRange, index.Line);
        fn.Block(inside);
        var last = range.Inclusive ? fn.Value($"add i64 {end}, 1") : end;
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})");
        var from = fn.Value($"getelementptr {Ir(type.Element)}, ptr {items}, i64 {start}");
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var part = fn.Value($"call ptr {StdFunction("ListOf")}(ptr {from}, i64 {fn.Value($"sub i64 {last}, {start}")}, i64 {ItemSize(type.Element)}, ptr {asked})");
        Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        if (Counted(type.Element))
        {
            EachItem(part, type.Element, held => Retain(held, type.Element)); // the part holds the items' blocks too
        }

        return Temp(part, type);
    }

    /// <summary>The end of the process at an allocation the system refused, where <paramref name="refused"/> holds, with the bytes <paramref name="asked"/> holds (O11, M9).</summary>
    private void Refused(string refused, string asked)
    {
        var failed = fn.Label("failed");
        var done = fn.Label("allocated");
        fn.CondBranch(refused, failed, done);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
        fn.Block(done);
    }

    /// <summary>
    /// T15: <c>s.Bytes</c> as a value, a list of bytes held once more: the block of a string made while the program runs, which it shares,
    /// or a new block with the bytes of a literal, which holds none (section 4, assumed there).
    /// </summary>
    private string StringBytes(string text)
    {
        var name = Helper("bytes.string", "ptr", ["%kz.str"], () =>
        {
            var bytes = fn.Value("extractvalue %kz.str %a0, 0");
            var marked = fn.Value("extractvalue %kz.str %a0, 1");
            var made = fn.Label("made");
            var literal = fn.Label("literal");
            fn.CondBranch(fn.Value($"icmp slt i64 {marked}, 0"), made, literal); // the mark: a block
            fn.Block(made);
            fn.Return($"ptr {fn.Value($"call ptr {StdFunction("StringList")}(ptr {bytes})")}");
            fn.Block(literal);
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            var copy = fn.Value($"call ptr {StdFunction("ListOf")}(ptr {bytes}, i64 {marked}, i64 1, ptr {asked})");
            Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
            fn.Return($"ptr {copy}");
        });
        return fn.Value($"call ptr {name}(%kz.str {text})");
    }

    /// <summary>T15: the string of a list of bytes that shares its block, held once more, or the empty string for a list without a block.</summary>
    private string SharedText(string list, string count)
    {
        var bytes = fn.Value($"call ptr {StdFunction("ListString")}(ptr {list})");
        var none = fn.Value($"icmp eq ptr {list}, null");
        var (empty, _) = module.String(string.Empty);
        var pointer = fn.Value($"select i1 {none}, ptr {empty}, ptr {bytes}");
        var marked = fn.Value($"select i1 {none}, i64 0, i64 {fn.Value($"or i64 {count}, -9223372036854775808")}");
        var withPointer = fn.Value($"insertvalue %kz.str undef, ptr {pointer}, 0");
        return fn.Value($"insertvalue %kz.str {withPointer}, i64 {marked}, 1");
    }

    /// <summary>R15: <c>l.Pointer</c>, the address of the list's first item, and for a list without a block an address that is not null, aligned for its items, through which nothing is read or written, as Rust's <c>NonNull::dangling</c>.</summary>
    private string ListPointer(string list, KurzType element)
    {
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})");
        var dangling = fn.Value($"inttoptr i64 {Number(layout.Measure(element).Align)} to ptr");
        return fn.Value($"select i1 {fn.Value($"icmp eq ptr {list}, null")}, ptr {dangling}, ptr {items}");
    }

    /// <summary>
    /// M9, M5: <c>list.Add(item)</c> on a <c>mut</c> variable or parameter, or on a path of fields from one. The item is read first,
    /// then the list the variable holds as the item left it, as a path assignment reads its value first (M4); a block other values
    /// hold is copied before the write (M3), and a full one grows. What the argument made besides the item is let go of before the list is read, so that the environment
    /// of a lambda it gave to a call, which counts the values it read (F15), holds no count of the list when the list grows, as
    /// M7 states (section 4, the owner, 2026-10-28, "Pass, as Rust").
    /// </summary>
    private void EmitListAdd(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var argument = c.Arguments[0].Value;
        string item = null!;
        InRegion(() => item = Convert(Owned(argument), TypeOf(argument), list.Element, argument.Line));
        var place = CollectionPlace(member.Target);
        var add = Helper($"add.{Lower.Mangle(list)}", "void", ["ptr", Ir(list.Element)], () => AddBody(list));
        fn.Emit($"call void {add}(ptr {place}, {Ir(list.Element)} {item})");
    }

    /// <summary>
    /// The helper of <c>Add</c>: the place of the list in <c>%a0</c>, the item, which the list takes over, in <c>%a1</c>. It holds the
    /// push alone and calls the helper that makes room (<see cref="ReserveBody"/>) where the block has none, so that the optimiser
    /// inlines the push where an <c>Add</c> stands and keeps the making of room out of line, as Rust's <c>Vec::push</c> calls a grow
    /// it does not inline.
    /// </summary>
    private void AddBody(ListType list)
    {
        var size = ItemSize(list.Element);
        var block = fn.Value("load ptr, ptr %a0");
        var push = fn.Label("push");
        var reserve = fn.Label("reserve");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("ListHasRoom")}(ptr {block})"), push, reserve); // room in a block no other value holds
        fn.Block(reserve);
        var room = Helper($"reserve.{Lower.Mangle(list)}", "void", ["ptr"], () => ReserveBody(list.Element), "noinline", "cold");
        fn.Emit($"call void {room}(ptr %a0)");
        fn.Branch(push);
        fn.Block(push);
        var slot = fn.Value($"call ptr {StdFunction("ListPush")}(ptr {fn.Value("load ptr, ptr %a0")}, i64 {size})");
        fn.Emit($"store {Ir(list.Element)} %a1, ptr {slot}");
        fn.Return(null);
    }

    /// <summary>
    /// The helper that makes room for one item more in the list at the place in <c>%a0</c>: a copy of its block where another value
    /// holds it (M3), else a larger block; a block the system refuses ends the program with <c>allocation-failed</c>.
    /// </summary>
    private void ReserveBody(KurzType element)
    {
        var size = ItemSize(element);
        var asked = fn.Alloca("i64", "asked");
        var block = fn.Value("load ptr, ptr %a0");
        var copy = fn.Label("copy");
        var grow = fn.Label("grow");
        var failed = fn.Label("failed");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})"), copy, grow);
        fn.Block(copy);
        var copied = fn.Value($"call ptr {StdFunction("ListCopy")}(ptr {block}, i64 {size}, ptr {asked})"); // which lowers the old block's count
        var keep = fn.Label("copied");
        fn.CondBranch(fn.Value($"icmp eq ptr {copied}, null"), failed, keep);
        fn.Block(keep);
        if (Counted(element))
        {
            EachItem(copied, element, held => Retain(held, element)); // the copy holds the items' blocks too
        }

        fn.Emit($"store ptr {copied}, ptr %a0");
        fn.Return(null);
        fn.Block(grow);
        var grown = fn.Value($"call ptr {StdFunction("ListGrow")}(ptr {block}, i64 {size}, ptr {asked})");
        var store = fn.Label("grown");
        fn.CondBranch(fn.Value($"icmp eq ptr {grown}, null"), failed, store);
        fn.Block(store);
        fn.Emit($"store ptr {grown}, ptr %a0");
        fn.Return(null);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
    }

    /// <summary>
    /// M9: <c>list.Where(test)</c>, a new list of the items the test gives <c>true</c> for, in their order, each held once more by
    /// the new list; the test is called once for each item, in order (section 4, the owner, 2026-10-28, "Once each, in order"), as
    /// Rust's <c>filter</c> and <c>collect</c> call it. The list is borrowed across the test's argument: the checker refuses an
    /// argument that changes it, as one that takes it after <c>mut</c> (M7, <c>changed-while-held</c>).
    /// </summary>
    private string EmitListWhere(CallExpression c, CallInfo info)
    {
        var list = (ListType)Sub(info.Result);
        var source = EmitExpression(((MemberAccess)Unwrap(c.Callee)).Target);
        var test = Arguments(c, info, _ => new FunctionType([list.Element], Types.Bool), _ => false, _ => null, cConvention: false, owned: false).Single();
        var element = Ir(list.Element);
        var name = Helper($"where.{Lower.Mangle(list)}", "ptr", ["ptr", "%kz.fn"], () =>
        {
            var kept = fn.Alloca("ptr", "kept");
            fn.Emit($"store ptr null, ptr {kept}");
            var code = fn.Value("extractvalue %kz.fn %a1, 0");
            var environment = fn.Value("extractvalue %kz.fn %a1, 1");
            EachItem("%a0", list.Element, item =>
            {
                var keep = fn.Label("keep");
                var next = fn.Label("next");
                fn.CondBranch(fn.Value($"call i1 {code}(ptr {environment}, {element} {item})"), keep, next);
                fn.Block(keep);
                if (Counted(list.Element))
                {
                    Retain(item, list.Element);
                }

                var add = Helper($"add.{Lower.Mangle(list)}", "void", ["ptr", element], () => AddBody(list));
                fn.Emit($"call void {add}(ptr {kept}, {element} {item})");
                fn.Block(next);
            });
            fn.Return($"ptr {fn.Value($"load ptr, ptr {kept}")}");
        });
        return fn.Value($"call ptr {name}(ptr {source}, {test})");
    }

    /// <summary>
    /// X9, M5: <c>list.AddRange(collection)</c> on a <c>mut</c> variable or parameter, or on a path of fields from one: the items of
    /// <c>collection</c> after the list's own, each held once more by the list, as C#'s <c>List.AddRange</c> appends them. The argument is
    /// read first and held with a count of its own while the list grows, then the list the variable holds as the argument left it, as
    /// <c>Add</c> reads its item first (M4): so <c>xs.AddRange(xs)</c>, which the checker lets through as it lets <c>Add</c>'s receiver
    /// stand in its argument (M7), appends the items the block held before the call: from the front of the block, grown, where only the
    /// list and the argument hold it, as Rust's <c>Vec::extend_from_within</c> copies them, else from the block as it was, beside its
    /// copy (M3) (<see cref="AddRoomBody"/>). Room for all the items is made at once, as Rust's <c>Vec::extend_from_slice</c> reserves
    /// it: in line where the block has the room and no other value holds it (<c>ListHasRoomFor</c>), else by a helper the optimiser
    /// keeps out of line, as <c>Add</c> makes room (<see cref="AddBody"/>); the items are copied once (<see cref="SmallCopy"/>), and a
    /// block the system refuses raises <c>allocation-failed</c> (M9).
    /// </summary>
    private string? EmitListAddRange(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var argument = c.Arguments[0].Value;
        string other = null!;
        InRegion(() => other = Owned(argument));
        var place = CollectionPlace(member.Target);
        var add = Helper($"addrange.{Lower.Mangle(list)}", "ptr", ["ptr", "ptr"], () =>
        {
            var size = ItemSize(list.Element);
            var count = ListCount("%a1");
            var some = fn.Label("some");
            var none = fn.Label("none");
            fn.CondBranch(fn.Value($"icmp sgt i32 {count}, 0"), some, none);
            fn.Block(none);
            fn.Return("ptr %a1");
            fn.Block(some);
            var more = fn.Value($"sext i32 {count} to i64");
            var source = fn.Alloca("ptr", "source");
            fn.Emit($"store ptr %a1, ptr {source}");
            var append = fn.Label("append");
            var reserve = fn.Label("reserve");
            fn.CondBranch(fn.Value($"call i1 {StdFunction("ListHasRoomFor")}(ptr {fn.Value("load ptr, ptr %a0")}, i64 {more})"), append, reserve); // room in a block no other value holds
            fn.Block(reserve);
            var grow = Helper($"addroom.{Lower.Mangle(list)}", "ptr", ["ptr", "i64", "ptr"], () => AddRoomBody(list.Element), "noinline", "cold");
            fn.Emit($"store ptr {fn.Value($"call ptr {grow}(ptr %a0, i64 {more}, ptr %a1)")}, ptr {source}");
            fn.Branch(append);
            fn.Block(append);
            var room = fn.Value("load ptr, ptr %a0");
            var from = fn.Value($"load ptr, ptr {source}");
            if (Counted(list.Element))
            {
                EachItem(from, list.Element, item => Retain(item, list.Element)); // the list holds the added items
            }

            var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {from})");
            var bytes = fn.Value($"mul i64 {more}, {size}");
            var small = fn.Label("small");
            var large = fn.Label("large");
            var done = fn.Label("appended");
            fn.CondBranch(fn.Value($"icmp ule i64 {bytes}, {SmallCopy}"), small, large);
            fn.Block(small);
            module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
            var end = fn.Value($"getelementptr i8, ptr {fn.Value($"call ptr {StdFunction("ListItems")}(ptr {room})")}, i64 {fn.Value($"mul i64 {fn.Value($"sext i32 {ListCount(room)} to i64")}, {size}")}");
            fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {end}, ptr {items}, i64 {bytes}, i1 false)"); // one copy, as Rust's `copy_nonoverlapping`
            fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {room}, i64 {more})");
            fn.Branch(done);
            fn.Block(large);
            fn.Emit($"call void {StdFunction("ListAppend")}(ptr {room}, ptr {items}, i64 {more}, i64 {size})");
            fn.Branch(done);
            fn.Block(done);
            fn.Return($"ptr {from}");
        });
        Release(fn.Value($"call ptr {add}(ptr {place}, ptr {other})"), list); // the argument, or the block it shares with the list (AddRoomBody)
        return null;
    }

    /// <summary>
    /// The most bytes an <c>AddRange</c> copies with the C library's <c>memcpy</c>, as Rust's <c>extend_from_slice</c> copies them;
    /// more are copied by the library's <c>ListAppend</c>, whose loop the optimiser widens. glibc's <c>memcpy</c> turns to
    /// <c>rep movsb</c> from 2112 bytes on a processor with fast short <c>rep movsb</c>, which measured slower than that loop for
    /// appends of 3072 and 4000 bytes to a list that kept its room, 5.49 against 4.83 ms and 5.87 against 5.03 (#157).
    /// </summary>
    private const int SmallCopy = 2048;

    /// <summary>
    /// The helper that makes room for <c>%a1</c> items more in the list at the place in <c>%a0</c>, for an <c>AddRange</c> of the list
    /// <c>%a2</c> whose block has not the room or is held by other values, and gives the block whose items it appends. Where
    /// <c>%a2</c> is the list's own block and no value but the list and the argument holds it, <c>xs.AddRange(xs)</c>, that block grows
    /// and its front is appended, as Rust's <c>Vec::extend_from_within</c> reserves and copies from the vector itself
    /// (<c>ListReserveWithin</c>), and the argument's hold moves to the grown block, which the caller lets go of. Else the block is
    /// <c>ListReserve</c>'s copy (M3), whose items' own blocks it counts, or a larger block, and the items are the argument's. A block
    /// the system refuses ends the program with <c>allocation-failed</c>.
    /// </summary>
    private void AddRoomBody(KurzType element)
    {
        var block = fn.Value("load ptr, ptr %a0");
        var asked = fn.Alloca("i64", "asked");
        var within = fn.Label("within");
        var apart = fn.Label("apart");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("ListWithin")}(ptr {block}, ptr %a2)"), within, apart);
        fn.Block(within);
        var grown = fn.Value($"call ptr {StdFunction("ListReserveWithin")}(ptr {block}, i64 %a1, i64 {ItemSize(element)}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {grown}, null"), asked);
        fn.Emit($"store ptr {grown}, ptr %a0");
        fn.Return($"ptr {grown}");
        fn.Block(apart);
        var shared = fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})");
        var room = fn.Value($"call ptr {StdFunction("ListReserve")}(ptr {block}, i64 %a1, i64 {ItemSize(element)}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {room}, null"), asked);
        if (Counted(element))
        {
            var copied = fn.Label("copied");
            var held = fn.Label("held");
            fn.CondBranch(shared, copied, held);
            fn.Block(copied);
            EachItem(room, element, item => Retain(item, element)); // the copy holds the items' blocks too
            fn.Branch(held);
            fn.Block(held);
        }

        fn.Emit($"store ptr {room}, ptr %a0");
        fn.Return("ptr %a2");
    }

    /// <summary>
    /// X9, M5: <c>list.Reverse()</c>, the items in the reverse order, swapped in place from both ends as Rust's <c>reverse</c> swaps them,
    /// through the place of the list's variable or of the field a path from one ends at. A list of two items or more in a block other
    /// values hold is copied first (M3), as <c>Add</c> copies it; a swap moves the items, so no count changes.
    /// </summary>
    private string? EmitListReverse(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var reverse = Helper($"reverse.{Lower.Mangle(list)}", "void", ["ptr"], () =>
        {
            var element = Ir(list.Element);
            var block = fn.Value("load ptr, ptr %a0");
            var count = ListCount(block);
            var some = fn.Label("some");
            var done = fn.Label("done");
            fn.CondBranch(fn.Value($"icmp sge i32 {count}, 2"), some, done);
            fn.Block(some);
            var copy = fn.Label("copy");
            var own = fn.Label("own");
            fn.CondBranch(fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})"), copy, own);
            fn.Block(copy);
            var asked = fn.Alloca("i64", "asked");
            var copied = fn.Value($"call ptr {StdFunction("ListCopy")}(ptr {block}, i64 {ItemSize(list.Element)}, ptr {asked})"); // which lowers the old block's count
            Refused(fn.Value($"icmp eq ptr {copied}, null"), asked);
            if (Counted(list.Element))
            {
                EachItem(copied, list.Element, held => Retain(held, list.Element)); // the copy holds the items' blocks too
            }

            fn.Emit($"store ptr {copied}, ptr %a0");
            fn.Branch(own);
            fn.Block(own);
            var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {fn.Value("load ptr, ptr %a0")})");
            var low = fn.Alloca("i32", "low");
            var high = fn.Alloca("i32", "high");
            fn.Emit($"store i32 0, ptr {low}");
            fn.Emit($"store i32 {fn.Value($"sub i32 {count}, 1")}, ptr {high}");
            var check = fn.Label("check");
            var swap = fn.Label("swap");
            fn.Branch(check);
            fn.Block(check);
            var i = fn.Value($"load i32, ptr {low}");
            var j = fn.Value($"load i32, ptr {high}");
            fn.CondBranch(fn.Value($"icmp slt i32 {i}, {j}"), swap, done);
            fn.Block(swap);
            var first = fn.Value($"getelementptr {element}, ptr {items}, i64 {fn.Value($"sext i32 {i} to i64")}");
            var last = fn.Value($"getelementptr {element}, ptr {items}, i64 {fn.Value($"sext i32 {j} to i64")}");
            var a = fn.Value($"load {element}, ptr {first}");
            var b = fn.Value($"load {element}, ptr {last}");
            fn.Emit($"store {element} {b}, ptr {first}");
            fn.Emit($"store {element} {a}, ptr {last}");
            fn.Emit($"store i32 {fn.Value($"add i32 {i}, 1")}, ptr {low}");
            fn.Emit($"store i32 {fn.Value($"sub i32 {j}, 1")}, ptr {high}");
            fn.Branch(check);
            fn.Block(done);
            fn.Return(null);
        });
        fn.Emit($"call void {reverse}(ptr {CollectionPlace(member.Target)})");
        return null;
    }

    // ---- the sorts of a list (X8) ----------------------------------------------------------------

    /// <summary>
    /// The most items for which a sort takes insertion alone, as Rust's sorts sort a list of 20 items or fewer by insertion, and the length of
    /// the runs the stable sort sorts so before it merges them, fixed runs of this compiler's own, where Rust's merge sort finds the runs the
    /// list holds and makes a short one longer.
    /// </summary>
    private const int ShortRun = 20;

    /// <summary>What the sort helpers sort: the name of the items in the helpers' keys, their IR type, and the helper that gives whether one sorts before another.</summary>
    private readonly record struct SortOf(string Key, string Item, string Less);

    /// <summary>
    /// X8: <c>list.Sort()</c> and <c>list.SortUnstable()</c> through the place of the list's variable or of the field a path from one ends
    /// at, as <c>Reverse</c> takes it: a list of two items or more in a block other values hold is copied first (M3), and the items are
    /// moved in the block, so no count changes. <c>Sort</c> keeps equal items in their order (<see cref="StableSort"/>), and
    /// <c>SortUnstable</c> takes no buffer (<see cref="UnstableSort"/>), as Rust's <c>sort</c> and <c>sort_unstable</c>; both order the
    /// items as <see cref="SortLess"/> does.
    /// </summary>
    private string? EmitListSort(CallExpression c, bool stable)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var sorted = new SortOf(Lower.Mangle(list.Element), Ir(list.Element), SortLess(list.Element, c.Line));
        var sort = Helper($"{(stable ? "sort" : "sort.unstable")}.{Lower.Mangle(list)}", "void", ["ptr"], () =>
        {
            var count = ListCount(fn.Value("load ptr, ptr %a0"));
            var some = fn.Label("some");
            var done = fn.Label("done");
            fn.CondBranch(fn.Value($"icmp sge i32 {count}, 2"), some, done);
            fn.Block(some);
            var items = OwnItems(list);
            var n = fn.Value($"sext i32 {count} to i64");
            fn.Emit(stable
                ? $"call void {StableSort(sorted)}(ptr {items}, i64 {n})"
                : $"call void {UnstableSort(sorted)}(ptr {items}, i64 {n}, i64 {SortLimit(n)})");
            fn.Branch(done);
            fn.Block(done);
            fn.Return(null);
        });
        fn.Emit($"call void {sort}(ptr {CollectionPlace(member.Target)})");
        return null;
    }

    /// <summary>
    /// X8: <c>list.SortBy(keySelector)</c>, as Rust's <c>sort_by_cached_key</c> sorts: the key of each item made once, in the order of the
    /// list, and kept with the item's position as a pair (section 12, the owner, 2026-10-31, "Once per item"); the pairs sorted by their
    /// keys, and by their positions where two keys are equal, so that items of equal keys keep their order, without a buffer
    /// (<see cref="UnstableSort"/>); then the items moved where the pairs say, through the place of the list as <c>Sort</c> takes it, and
    /// the keys let go of. An empty list makes no key and a list of one item one; a block for the pairs the system refuses raises
    /// <c>allocation-failed</c> (M9). The list is read without a count of its own while the keys are made: a key that reads it is the
    /// checker's <c>changed-while-held</c> (M7), a lambda changes nothing it captures (F11), and a copy of the list a lambda holds keeps
    /// the block shared, so that the sort copies it (M3).
    /// </summary>
    private string? EmitListSortBy(CallExpression c, CallInfo info)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var key = ((FunctionType)TypeOf(c.Arguments[0].Value)).Result;
        var keySelector = Arguments(c, info, _ => new FunctionType([list.Element], key), _ => false, _ => null, cConvention: false, owned: false).Single();
        var pair = $"{{ {Ir(key)}, i64 }}";
        var sorted = new SortOf($"pair.{Lower.Mangle(key)}", pair, PairLess(key, pair, c.Line));
        var sort = Helper($"sort.by.{Lower.Mangle(list)}.{Lower.Mangle(key)}", "void", ["ptr", "%kz.fn"], () =>
        {
            var block = fn.Value("load ptr, ptr %a0");
            var count = ListCount(block);
            var some = fn.Label("some");
            var done = fn.Label("done");
            fn.CondBranch(fn.Value($"icmp sgt i32 {count}, 0"), some, done);
            fn.Block(some);
            var n = fn.Value($"sext i32 {count} to i64");
            var bytes = fn.Value($"mul i64 {n}, {fn.Value($"ptrtoint ptr {fn.Value($"getelementptr {pair}, ptr null, i32 1")} to i64")}");
            var pairs = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {bytes})");
            var code = fn.Value("extractvalue %kz.fn %a1, 0");
            var environment = fn.Value("extractvalue %kz.fn %a1, 1");
            var at = fn.Alloca("i64", "at");
            fn.Emit($"store i64 0, ptr {at}");
            EachItem(block, list.Element, item =>
            {
                var i = fn.Value($"load i64, ptr {at}");
                var made = fn.Value($"call {Ir(key)} {code}(ptr {environment}, {Ir(list.Element)} {item})");
                var keyed = fn.Value($"insertvalue {pair} {fn.Value($"insertvalue {pair} undef, {Ir(key)} {made}, 0")}, i64 {i}, 1");
                fn.Emit($"store {pair} {keyed}, ptr {SortAt(pair, pairs, i)}");
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
            });
            var move = fn.Label("move");
            var release = fn.Label("release");
            fn.CondBranch(fn.Value($"icmp sge i32 {count}, 2"), move, release);
            fn.Block(move);
            fn.Emit($"call void {UnstableSort(sorted)}(ptr {pairs}, i64 {n}, i64 {SortLimit(n)})");
            var items = OwnItems(list);

            // Rust's permutation of `sort_by_cached_key`: the item for position i is at the position its pair names, followed through
            // the positions the swaps before i moved it on from
            fn.Emit($"store i64 0, ptr {at}");
            var chased = fn.Alloca("i64", "chased");
            var each = fn.Label("each");
            var start = fn.Label("start");
            var follow = fn.Label("follow");
            var moved = fn.Label("moved");
            var swap = fn.Label("swap");
            fn.Block(each);
            var position = fn.Value($"load i64, ptr {at}");
            fn.CondBranch(fn.Value($"icmp slt i64 {position}, {n}"), start, release);
            fn.Block(start);
            fn.Emit($"store i64 {fn.Value($"load i64, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {position}, i32 1")}")}, ptr {chased}");
            fn.Block(follow);
            var index = fn.Value($"load i64, ptr {chased}");
            fn.CondBranch(fn.Value($"icmp slt i64 {index}, {position}"), moved, swap);
            fn.Block(moved);
            fn.Emit($"store i64 {fn.Value($"load i64, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {index}, i32 1")}")}, ptr {chased}");
            fn.Branch(follow);
            fn.Block(swap);
            fn.Emit($"store i64 {index}, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {position}, i32 1")}");
            SortSwap(Ir(list.Element), items, position, index);
            fn.Emit($"store i64 {fn.Value($"add i64 {position}, 1")}, ptr {at}");
            fn.Branch(each);
            fn.Block(release);
            if (Counted(key))
            {
                fn.Emit($"store i64 0, ptr {at}");
                var check = fn.Label("keys");
                var next = fn.Label("key");
                var freed = fn.Label("keys.done");
                fn.Block(check);
                var i = fn.Value($"load i64, ptr {at}");
                fn.CondBranch(fn.Value($"icmp slt i64 {i}, {n}"), next, freed);
                fn.Block(next);
                Release(fn.Value($"load {Ir(key)}, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {i}, i32 0")}"), key);
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
                fn.Branch(check);
                fn.Block(freed);
            }

            fn.Emit($"call void {StdFunction("Free")}(ptr {pairs}, i64 {bytes})");
            fn.Branch(done);
            fn.Block(done);
            fn.Return(null);
        });
        fn.Emit($"call void {sort}(ptr {CollectionPlace(member.Target)}, {keySelector})");
        return null;
    }

    /// <summary>
    /// The items of the list at the place in <c>%a0</c>, in a block no other value holds: a block other values hold is copied first (M3),
    /// as <c>Add</c> copies it, and the copy holds the items' blocks too; a copy the system refuses raises <c>allocation-failed</c> (M9).
    /// </summary>
    private string OwnItems(ListType list)
    {
        var block = fn.Value("load ptr, ptr %a0");
        var copy = fn.Label("copy");
        var own = fn.Label("own");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})"), copy, own);
        fn.Block(copy);
        var asked = fn.Alloca("i64", "asked");
        var copied = fn.Value($"call ptr {StdFunction("ListCopy")}(ptr {block}, i64 {ItemSize(list.Element)}, ptr {asked})"); // which lowers the old block's count
        Refused(fn.Value($"icmp eq ptr {copied}, null"), asked);
        if (Counted(list.Element))
        {
            EachItem(copied, list.Element, held => Retain(held, list.Element)); // the copy holds the items' blocks too
        }

        fn.Emit($"store ptr {copied}, ptr %a0");
        fn.Branch(own);
        fn.Block(own);
        return fn.Value($"call ptr {StdFunction("ListItems")}(ptr {fn.Value("load ptr, ptr %a0")})");
    }

    /// <summary>
    /// X8: the helper that gives whether the value in <c>%a0</c> sorts before the one in <c>%a1</c>: as <c>&lt;</c> orders two values of
    /// the type (T18, T32, D18); a <c>double</c> or a <c>float</c> as C#'s <c>CompareTo</c> orders it, NaN before every other value and
    /// equal to NaN, <c>-0</c> equal to <c>0</c> (section 12, the owner, 2026-10-31, "C# CompareTo"); and <c>null</c> before every value of
    /// a nullable type (section 12, the owner, 2026-10-31, "Null first, as C#/Rust"). No value sorts before itself, and of two values at
    /// most one sorts before the other, which keeps the sorts' scans inside the list.
    /// </summary>
    private string SortLess(KurzType type, int line) => Helper($"sort.less.{Lower.Mangle(type)}", "i1", [Ir(type), Ir(type)], () =>
    {
        if (type is UnionType nullable && Types.NonNull(nullable) is { } inner)
        {
            var leftNull = IsCase(Tag("%a0", nullable), nullable, Types.Null);
            var rightNull = IsCase(Tag("%a1", nullable), nullable, Types.Null);
            var nulls = fn.Label("null");
            var values = fn.Label("values");
            fn.CondBranch(fn.Value($"or i1 {leftNull}, {rightNull}"), nulls, values);
            fn.Block(nulls);
            fn.Return($"i1 {fn.Value($"and i1 {leftNull}, {fn.Value($"xor i1 {rightNull}, true")}")}");
            fn.Block(values);
            var less = SortLess(inner, line);
            fn.Return($"i1 {fn.Value($"call i1 {less}({Ir(inner)} {WithoutNull("%a0", nullable)}, {Ir(inner)} {WithoutNull("%a1", nullable)})")}");
            return;
        }

        if (Types.IsFloating(type))
        {
            var below = fn.Value($"fcmp olt {Ir(type)} %a0, %a1");
            var nan = fn.Value($"and i1 {fn.Value($"fcmp uno {Ir(type)} %a0, %a0")}, {fn.Value($"fcmp ord {Ir(type)} %a1, %a1")}");
            fn.Return($"i1 {fn.Value($"or i1 {below}, {nan}")}");
            return;
        }

        fn.Return($"i1 {Compare(TokenKind.Less, "%a0", type, "%a1", type, line)}");
    });

    /// <summary>X8: the helper that gives whether the pair of a key and a position in <c>%a0</c> sorts before the one in <c>%a1</c>: by the keys (<see cref="SortLess"/>), and by the positions where neither key sorts before the other, as Rust compares the tuples of <c>sort_by_cached_key</c>.</summary>
    private string PairLess(KurzType key, string pair, int line) => Helper($"sort.less.pair.{Lower.Mangle(key)}", "i1", [pair, pair], () =>
    {
        var less = SortLess(key, line);
        var a = fn.Value($"extractvalue {pair} %a0, 0");
        var b = fn.Value($"extractvalue {pair} %a1, 0");
        var before = fn.Label("before");
        var other = fn.Label("other");
        var after = fn.Label("after");
        var equal = fn.Label("equal");
        fn.CondBranch(fn.Value($"call i1 {less}({Ir(key)} {a}, {Ir(key)} {b})"), before, other);
        fn.Block(before);
        fn.Return("i1 true");
        fn.Block(other);
        fn.CondBranch(fn.Value($"call i1 {less}({Ir(key)} {b}, {Ir(key)} {a})"), after, equal);
        fn.Block(after);
        fn.Return("i1 false");
        fn.Block(equal);
        fn.Return($"i1 {fn.Value($"icmp slt i64 {fn.Value($"extractvalue {pair} %a0, 1")}, {fn.Value($"extractvalue {pair} %a1, 1")}")}");
    });

    /// <summary>The address of the item at the position, an <c>i64</c>, among the items of the IR type <paramref name="item"/> at <paramref name="items"/>.</summary>
    private string SortAt(string item, string items, string position) => fn.Value($"getelementptr {item}, ptr {items}, i64 {position}");

    /// <summary>The two items at the positions <paramref name="a"/> and <paramref name="b"/> swapped.</summary>
    private void SortSwap(string item, string items, string a, string b)
    {
        var first = SortAt(item, items, a);
        var second = SortAt(item, items, b);
        var x = fn.Value($"load {item}, ptr {first}");
        var y = fn.Value($"load {item}, ptr {second}");
        fn.Emit($"store {item} {y}, ptr {first}");
        fn.Emit($"store {item} {x}, ptr {second}");
    }

    /// <summary>The bound on the partitions of <see cref="UnstableSort"/> for <paramref name="n"/> items, twice the base-2 logarithm of <c>n | 1</c>, as Rust's <c>sort_unstable</c> takes it.</summary>
    private string SortLimit(string n)
    {
        module.Declare("declare i64 @llvm.ctlz.i64(i64, i1)");
        var zeros = fn.Value($"call i64 @llvm.ctlz.i64(i64 {fn.Value($"or i64 {n}, 1")}, i1 true)");
        return fn.Value($"shl i64 {fn.Value($"sub i64 63, {zeros}")}, 1");
    }

    /// <summary>
    /// X8: the helper that sorts the <c>%a1</c> items at <c>%a0</c> by insertion, each item moved left past the items it sorts before, so
    /// that equal items keep their order: the sort of a short run, as Rust's sorts take it.
    /// </summary>
    private string InsertionSort(SortOf s) => Helper($"sort.insertion.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var next = fn.Alloca("i64", "next");
        var at = fn.Alloca("i64", "at");
        fn.Emit($"store i64 1, ptr {next}");
        var outer = fn.Label("outer");
        var take = fn.Label("take");
        var inner = fn.Label("inner");
        var test = fn.Label("test");
        var shift = fn.Label("shift");
        var put = fn.Label("put");
        var done = fn.Label("done");
        fn.Block(outer);
        var k = fn.Value($"load i64, ptr {next}");
        fn.CondBranch(fn.Value($"icmp slt i64 {k}, %a1"), take, done);
        fn.Block(take);
        var item = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", k)}");
        fn.Emit($"store i64 {k}, ptr {at}");
        fn.Block(inner);
        var j = fn.Value($"load i64, ptr {at}");
        fn.CondBranch(fn.Value($"icmp sgt i64 {j}, 0"), test, put);
        fn.Block(test);
        var before = fn.Value($"sub i64 {j}, 1");
        var left = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", before)}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {item}, {s.Item} {left})"), shift, put);
        fn.Block(shift);
        fn.Emit($"store {s.Item} {left}, ptr {SortAt(s.Item, "%a0", j)}");
        fn.Emit($"store i64 {before}, ptr {at}");
        fn.Branch(inner);
        fn.Block(put);
        fn.Emit($"store {s.Item} {item}, ptr {SortAt(s.Item, "%a0", fn.Value($"load i64, ptr {at}"))}");
        fn.Emit($"store i64 {fn.Value($"add i64 {k}, 1")}, ptr {next}");
        fn.Branch(outer);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper of <c>Sort</c>, which sorts the <c>%a1</c> items at <c>%a0</c> and keeps equal items in their order: a merge sort, as
    /// Rust's stable sort is one. A short list is sorted by insertion alone; a longer one in runs of <see cref="ShortRun"/> items, sorted by
    /// insertion, then merged in pairs of runs twice as long each time (<see cref="Merge"/>), with a buffer of half the items, rounded up,
    /// as Rust's stable sort asks for at the least (<c>driftsort_main</c>), which holds the shorter run of a pair. A buffer the system
    /// refuses raises <c>allocation-failed</c> (M9).
    /// </summary>
    private string StableSort(SortOf s) => Helper($"sort.stable.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var insertion = InsertionSort(s);
        var shortList = fn.Label("short");
        var runs = fn.Label("runs");
        fn.CondBranch(fn.Value($"icmp sle i64 %a1, {ShortRun}"), shortList, runs);
        fn.Block(shortList);
        fn.Emit($"call void {insertion}(ptr %a0, i64 %a1)");
        fn.Return(null);
        fn.Block(runs);
        var half = fn.Value($"sub i64 %a1, {fn.Value("lshr i64 %a1, 1")}");
        var bytes = fn.Value($"mul i64 {half}, {fn.Value($"ptrtoint ptr {SortAt(s.Item, "null", "1")} to i64")}");
        var buffer = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {bytes})");

        // each run sorted by insertion
        var low = fn.Alloca("i64", "low");
        fn.Emit($"store i64 0, ptr {low}");
        var run = fn.Label("run");
        var sortRun = fn.Label("run.sort");
        var merged = fn.Label("merged");
        fn.Block(run);
        var lo = fn.Value($"load i64, ptr {low}");
        fn.CondBranch(fn.Value($"icmp slt i64 {lo}, %a1"), sortRun, merged);
        fn.Block(sortRun);
        var rest = fn.Value($"sub i64 %a1, {lo}");
        var length = fn.Value($"select i1 {fn.Value($"icmp slt i64 {rest}, {ShortRun}")}, i64 {rest}, i64 {ShortRun}");
        fn.Emit($"call void {insertion}(ptr {SortAt(s.Item, "%a0", lo)}, i64 {length})");
        fn.Emit($"store i64 {fn.Value($"add i64 {lo}, {ShortRun}")}, ptr {low}");
        fn.Branch(run);

        // then each pair of runs merged, the runs twice as long each time, until one run holds every item
        fn.Block(merged);
        var width = fn.Alloca("i64", "width");
        fn.Emit($"store i64 {ShortRun}, ptr {width}");
        var pass = fn.Label("pass");
        var start = fn.Label("pass.start");
        var pairOf = fn.Label("pair");
        var mergePair = fn.Label("pair.merge");
        var widen = fn.Label("widen");
        var done = fn.Label("done");
        fn.Block(pass);
        var w = fn.Value($"load i64, ptr {width}");
        fn.CondBranch(fn.Value($"icmp slt i64 {w}, %a1"), start, done);
        fn.Block(start);
        fn.Emit($"store i64 0, ptr {low}");
        fn.Block(pairOf);
        var first = fn.Value($"load i64, ptr {low}");
        fn.CondBranch(fn.Value($"icmp slt i64 {first}, {fn.Value($"sub i64 %a1, {w}")}"), mergePair, widen);
        fn.Block(mergePair);
        var end = fn.Value($"add i64 {first}, {fn.Value($"shl i64 {w}, 1")}");
        var high = fn.Value($"select i1 {fn.Value($"icmp slt i64 {end}, %a1")}, i64 {end}, i64 %a1");
        fn.Emit($"call void {Merge(s)}(ptr {SortAt(s.Item, "%a0", first)}, i64 {w}, i64 {fn.Value($"sub i64 {high}, {first}")}, ptr {buffer})");
        fn.Emit($"store i64 {end}, ptr {low}");
        fn.Branch(pairOf);
        fn.Block(widen);
        fn.Emit($"store i64 {fn.Value($"shl i64 {w}, 1")}, ptr {width}");
        fn.Branch(pass);
        fn.Block(done);
        fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {bytes})");
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper that merges the two sorted runs of the <c>%a2</c> items at <c>%a0</c>, the first <c>%a1</c> and the rest, as Rust's
    /// merge sort merges them: nothing where the first item of the second run does not sort before the last of the first; else the shorter
    /// run is copied into the buffer at <c>%a3</c> and merged with the other, from the front where it is the first run and from the back
    /// where it is the second, an item of the second run going before one of the first only where it sorts before it, so that equal
    /// items keep their order.
    /// </summary>
    private string Merge(SortOf s) => Helper($"sort.merge.{s.Key}", "void", ["ptr", "i64", "i64", "ptr"], () =>
    {
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        var size = fn.Value($"ptrtoint ptr {SortAt(s.Item, "null", "1")} to i64");
        var last = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", fn.Value("sub i64 %a1, 1"))}");
        var next = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", "%a1")}");
        var merge = fn.Label("merge");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {next}, {s.Item} {last})"), merge, done);
        fn.Block(merge);
        var second = fn.Value("sub i64 %a2, %a1");
        var forward = fn.Label("forward");
        var backward = fn.Label("backward");
        fn.CondBranch(fn.Value($"icmp sle i64 %a1, {second}"), forward, backward);

        // the first run in the buffer, merged from the front; what is left of it after the second run ran out goes after the merged items
        fn.Block(forward);
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr %a3, ptr %a0, i64 {fn.Value($"mul i64 %a1, {size}")}, i1 false)");
        var i = fn.Alloca("i64", "taken");
        var j = fn.Alloca("i64", "other");
        var o = fn.Alloca("i64", "out");
        fn.Emit($"store i64 0, ptr {i}");
        fn.Emit($"store i64 %a1, ptr {j}");
        fn.Emit($"store i64 0, ptr {o}");
        var front = fn.Label("front");
        var step = fn.Label("front.step");
        var right = fn.Label("front.second");
        var left = fn.Label("front.first");
        var rest = fn.Label("front.rest");
        fn.Block(front);
        var iv = fn.Value($"load i64, ptr {i}");
        var jv = fn.Value($"load i64, ptr {j}");
        fn.CondBranch(fn.Value($"and i1 {fn.Value($"icmp slt i64 {iv}, %a1")}, {fn.Value($"icmp slt i64 {jv}, %a2")}"), step, rest);
        fn.Block(step);
        var a = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a3", iv)}");
        var b = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", jv)}");
        var ov = fn.Value($"load i64, ptr {o}");
        fn.Emit($"store i64 {fn.Value($"add i64 {ov}, 1")}, ptr {o}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {b}, {s.Item} {a})"), right, left);
        fn.Block(right);
        fn.Emit($"store {s.Item} {b}, ptr {SortAt(s.Item, "%a0", ov)}");
        fn.Emit($"store i64 {fn.Value($"add i64 {jv}, 1")}, ptr {j}");
        fn.Branch(front);
        fn.Block(left);
        fn.Emit($"store {s.Item} {a}, ptr {SortAt(s.Item, "%a0", ov)}");
        fn.Emit($"store i64 {fn.Value($"add i64 {iv}, 1")}, ptr {i}");
        fn.Branch(front);
        fn.Block(rest);
        var taken = fn.Value($"load i64, ptr {i}");
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {SortAt(s.Item, "%a0", fn.Value($"load i64, ptr {o}"))}, ptr {SortAt(s.Item, "%a3", taken)}, i64 {fn.Value($"mul i64 {fn.Value($"sub i64 %a1, {taken}")}, {size}")}, i1 false)");
        fn.Return(null);

        // the second run in the buffer, merged from the back; what is left of it after the first run ran out goes to the front
        fn.Block(backward);
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr %a3, ptr {SortAt(s.Item, "%a0", "%a1")}, i64 {fn.Value($"mul i64 {second}, {size}")}, i1 false)");
        var bi = fn.Alloca("i64", "first");
        var bj = fn.Alloca("i64", "kept");
        var bo = fn.Alloca("i64", "end");
        fn.Emit($"store i64 %a1, ptr {bi}");
        fn.Emit($"store i64 {second}, ptr {bj}");
        fn.Emit($"store i64 %a2, ptr {bo}");
        var back = fn.Label("back");
        var backStep = fn.Label("back.step");
        var backFirst = fn.Label("back.first");
        var backSecond = fn.Label("back.second");
        var backRest = fn.Label("back.rest");
        fn.Block(back);
        var x = fn.Value($"load i64, ptr {bi}");
        var y = fn.Value($"load i64, ptr {bj}");
        fn.CondBranch(fn.Value($"and i1 {fn.Value($"icmp sgt i64 {x}, 0")}, {fn.Value($"icmp sgt i64 {y}, 0")}"), backStep, backRest);
        fn.Block(backStep);
        var xp = fn.Value($"sub i64 {x}, 1");
        var yp = fn.Value($"sub i64 {y}, 1");
        var p = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", xp)}");
        var q = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a3", yp)}");
        var put = fn.Value($"sub i64 {fn.Value($"load i64, ptr {bo}")}, 1");
        fn.Emit($"store i64 {put}, ptr {bo}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {q}, {s.Item} {p})"), backFirst, backSecond);
        fn.Block(backFirst);
        fn.Emit($"store {s.Item} {p}, ptr {SortAt(s.Item, "%a0", put)}");
        fn.Emit($"store i64 {xp}, ptr {bi}");
        fn.Branch(back);
        fn.Block(backSecond);
        fn.Emit($"store {s.Item} {q}, ptr {SortAt(s.Item, "%a0", put)}");
        fn.Emit($"store i64 {yp}, ptr {bj}");
        fn.Branch(back);
        fn.Block(backRest);
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr %a0, ptr %a3, i64 {fn.Value($"mul i64 {fn.Value($"load i64, ptr {bj}")}, {size}")}, i1 false)");
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper of <c>SortUnstable</c> and of <c>SortBy</c>'s pairs, which sorts the <c>%a1</c> items at <c>%a0</c> in place: a quicksort,
    /// as Rust's <c>sort_unstable</c> is one. The pivot is the median of the first, the middle and the last item; the items are split
    /// around it from both ends, so that items equal to it fall on both sides; the shorter side is sorted by a call and the longer in the
    /// same loop, so that the calls nest no deeper than the base-2 logarithm of the count. A side of <see cref="ShortRun"/> items or fewer
    /// is sorted by insertion, and a side reached after <c>%a2</c> splits, the bound <see cref="SortLimit"/> gives, by a heapsort
    /// (<see cref="HeapSort"/>), so that the comparisons of no order of the items grow faster than a constant times n log n.
    /// </summary>
    private string UnstableSort(SortOf s) => Helper($"sort.unstable.{s.Key}", "void", ["ptr", "i64", "i64"], () =>
    {
        var self = UnstableSort(s);
        var items = fn.Alloca("ptr", "items");
        var count = fn.Alloca("i64", "count");
        var limit = fn.Alloca("i64", "limit");
        fn.Emit("store ptr %a0, ptr " + items);
        fn.Emit("store i64 %a1, ptr " + count);
        fn.Emit("store i64 %a2, ptr " + limit);
        var top = fn.Label("top");
        var shortSide = fn.Label("short");
        var longSide = fn.Label("long");
        var heap = fn.Label("heap");
        var pivot = fn.Label("pivot");
        fn.Block(top);
        var v = fn.Value($"load ptr, ptr {items}");
        var m = fn.Value($"load i64, ptr {count}");
        var l = fn.Value($"load i64, ptr {limit}");
        fn.CondBranch(fn.Value($"icmp sle i64 {m}, {ShortRun}"), shortSide, longSide);
        fn.Block(shortSide);
        fn.Emit($"call void {InsertionSort(s)}(ptr {v}, i64 {m})");
        fn.Return(null);
        fn.Block(longSide);
        fn.CondBranch(fn.Value($"icmp eq i64 {l}, 0"), heap, pivot);
        fn.Block(heap);
        fn.Emit($"call void {HeapSort(s)}(ptr {v}, i64 {m})");
        fn.Return(null);
        fn.Block(pivot);
        var lower = fn.Value($"sub i64 {l}, 1");
        fn.Emit($"store i64 {lower}, ptr {limit}");
        var last = fn.Value($"sub i64 {m}, 1");
        var middle = fn.Value($"lshr i64 {last}, 1");
        OrderTwo(s, v, "0", middle);
        OrderTwo(s, v, middle, last);
        OrderTwo(s, v, "0", middle);
        // the pivot is read from its slot without a count of its own and kept while the split swaps that slot: the swaps move items
        // inside the block and let go of none, and nothing frees the block while the split runs, so the item it names stays alive
        var p = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, v, middle)}");

        // Hoare's split: the first item from the front that the pivot does not follow, and the first from the back it does not precede,
        // swapped until the two meet; every item up to the meeting point then sorts no later than the pivot and every item after it no earlier
        var i = fn.Alloca("i64", "front");
        var j = fn.Alloca("i64", "back");
        fn.Emit($"store i64 -1, ptr {i}");
        fn.Emit($"store i64 {m}, ptr {j}");
        var scanFront = fn.Label("scan.front");
        var scanBack = fn.Label("scan.back");
        var meet = fn.Label("meet");
        var swap = fn.Label("swap");
        var split = fn.Label("split");
        fn.Block(scanFront);
        var iv = fn.Value($"add i64 {fn.Value($"load i64, ptr {i}")}, 1");
        fn.Emit($"store i64 {iv}, ptr {i}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {fn.Value($"load {s.Item}, ptr {SortAt(s.Item, v, iv)}")}, {s.Item} {p})"), scanFront, scanBack);
        fn.Block(scanBack);
        var jv = fn.Value($"sub i64 {fn.Value($"load i64, ptr {j}")}, 1");
        fn.Emit($"store i64 {jv}, ptr {j}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {p}, {s.Item} {fn.Value($"load {s.Item}, ptr {SortAt(s.Item, v, jv)}")})"), scanBack, meet);
        fn.Block(meet);
        var front = fn.Value($"load i64, ptr {i}");
        fn.CondBranch(fn.Value($"icmp sge i64 {front}, {jv}"), split, swap);
        fn.Block(swap);
        SortSwap(s.Item, v, front, jv);
        fn.Branch(scanFront);
        fn.Block(split);
        var leftCount = fn.Value($"add i64 {jv}, 1");
        var rightCount = fn.Value($"sub i64 {m}, {leftCount}");
        var rightItems = SortAt(s.Item, v, leftCount);
        var leftShorter = fn.Label("left.shorter");
        var rightShorter = fn.Label("right.shorter");
        fn.CondBranch(fn.Value($"icmp slt i64 {leftCount}, {rightCount}"), leftShorter, rightShorter);
        fn.Block(leftShorter);
        fn.Emit($"call void {self}(ptr {v}, i64 {leftCount}, i64 {lower})");
        fn.Emit($"store ptr {rightItems}, ptr {items}");
        fn.Emit($"store i64 {rightCount}, ptr {count}");
        fn.Branch(top);
        fn.Block(rightShorter);
        fn.Emit($"call void {self}(ptr {rightItems}, i64 {rightCount}, i64 {lower})");
        fn.Emit($"store i64 {leftCount}, ptr {count}");
        fn.Branch(top);
    });

    /// <summary>The items at the positions <paramref name="a"/> and <paramref name="b"/> swapped where the second sorts before the first.</summary>
    private void OrderTwo(SortOf s, string items, string a, string b)
    {
        var swap = fn.Label("order.swap");
        var done = fn.Label("ordered");
        var x = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, items, a)}");
        var y = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, items, b)}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {y}, {s.Item} {x})"), swap, done);
        fn.Block(swap);
        SortSwap(s.Item, items, a, b);
        fn.Block(done);
    }

    /// <summary>
    /// X8: the helper that sorts the <c>%a1</c> items at <c>%a0</c> as a heap, as Rust's <c>heapsort</c> does: the items made a heap whose
    /// first item no other sorts after, then that item swapped to the end of the heap, and the heap one item shorter, until one is left.
    /// </summary>
    private string HeapSort(SortOf s) => Helper($"sort.heap.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var sift = Sift(s);
        var node = fn.Alloca("i64", "node");
        fn.Emit($"store i64 {fn.Value("lshr i64 %a1, 1")}, ptr {node}");
        var build = fn.Label("build");
        var siftDown = fn.Label("build.sift");
        var built = fn.Label("built");
        fn.Block(build);
        var at = fn.Value($"load i64, ptr {node}");
        fn.CondBranch(fn.Value($"icmp sgt i64 {at}, 0"), siftDown, built);
        fn.Block(siftDown);
        var below = fn.Value($"sub i64 {at}, 1");
        fn.Emit($"store i64 {below}, ptr {node}");
        fn.Emit($"call void {sift}(ptr %a0, i64 {below}, i64 %a1)");
        fn.Branch(build);
        fn.Block(built);
        var end = fn.Alloca("i64", "end");
        fn.Emit($"store i64 %a1, ptr {end}");
        var take = fn.Label("take");
        var swap = fn.Label("take.swap");
        var done = fn.Label("done");
        fn.Block(take);
        var e = fn.Value($"load i64, ptr {end}");
        fn.CondBranch(fn.Value($"icmp sgt i64 {e}, 1"), swap, done);
        fn.Block(swap);
        var shorter = fn.Value($"sub i64 {e}, 1");
        fn.Emit($"store i64 {shorter}, ptr {end}");
        SortSwap(s.Item, "%a0", "0", shorter);
        fn.Emit($"call void {sift}(ptr %a0, i64 0, i64 {shorter})");
        fn.Branch(take);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>X8: the helper that moves the item at the position <c>%a1</c> of the heap of the first <c>%a2</c> items at <c>%a0</c> down, swapped with the child it sorts before that no other child sorts after, until no child sorts after it.</summary>
    private string Sift(SortOf s) => Helper($"sort.sift.{s.Key}", "void", ["ptr", "i64", "i64"], () =>
    {
        var node = fn.Alloca("i64", "node");
        var pick = fn.Alloca("i64", "child");
        fn.Emit($"store i64 %a1, ptr {node}");
        var down = fn.Label("down");
        var children = fn.Label("children");
        var compare = fn.Label("compare");
        var chosen = fn.Label("chosen");
        var swap = fn.Label("swap");
        var done = fn.Label("done");
        fn.Block(down);
        var at = fn.Value($"load i64, ptr {node}");
        var child = fn.Value($"add i64 {fn.Value($"shl i64 {at}, 1")}, 1");
        fn.CondBranch(fn.Value($"icmp slt i64 {child}, %a2"), children, done);
        fn.Block(children);
        fn.Emit($"store i64 {child}, ptr {pick}");
        var other = fn.Value($"add i64 {child}, 1");
        fn.CondBranch(fn.Value($"icmp slt i64 {other}, %a2"), compare, chosen);
        fn.Block(compare);
        var a = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", child)}");
        var b = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", other)}");
        fn.Emit($"store i64 {fn.Value($"select i1 {fn.Value($"call i1 {s.Less}({s.Item} {a}, {s.Item} {b})")}, i64 {other}, i64 {child}")}, ptr {pick}");
        fn.Block(chosen);
        var c = fn.Value($"load i64, ptr {pick}");
        var parent = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", at)}");
        var larger = fn.Value($"load {s.Item}, ptr {SortAt(s.Item, "%a0", c)}");
        fn.CondBranch(fn.Value($"call i1 {s.Less}({s.Item} {parent}, {s.Item} {larger})"), swap, done);
        fn.Block(swap);
        SortSwap(s.Item, "%a0", at, c);
        fn.Emit($"store i64 {c}, ptr {node}");
        fn.Branch(down);
        fn.Block(done);
        fn.Return(null);
    });

    // ---- the searches of a list and of a string (X1, X9) ---------------------------------------------

    /// <summary>
    /// X9: <c>list.IndexOf(item)</c>, the position of the first item equal to <c>item</c> as <c>==</c> finds two items of a list equal
    /// (<see cref="ItemEqual"/>), and <c>list.IndexOf(value)</c> of a run of items, the position of the first run equal to the items of
    /// <c>value</c>, an empty one found at 0: an <c>int?</c>, <c>null</c> where there is none (<see cref="Position"/>). The list is read
    /// before the argument and held without a count of its own while the argument is made and while the search runs: an argument that
    /// changes it is the checker's <c>changed-while-held</c> (M7). A list of one-byte items, whose equality is that of their bits, is
    /// searched by the standard library's <c>Find</c>, as a string is (<see cref="EmitStringSearch"/>), so that a run is found in linear
    /// time; a run of other items is compared at each position in turn, as Rust compares a slice's <c>windows</c>.
    /// </summary>
    private string EmitListIndexOf(CallExpression c, bool run)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var source = EmitExpression(member.Target);
        var argument = c.Arguments[0].Value;
        var value = Convert(EmitExpression(argument), TypeOf(argument), run ? list : list.Element, argument.Line);
        if (Ir(list.Element) == "i8")
        {
            var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {source})");
            var count = fn.Value($"sext i32 {ListCount(source)} to i64");
            string needle;
            string length;
            if (run)
            {
                needle = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {value})");
                length = fn.Value($"sext i32 {ListCount(value)} to i64");
            }
            else
            {
                needle = fn.Alloca("i8", "item");
                fn.Emit($"store i8 {value}, ptr {needle}");
                length = "1";
            }

            var found = fn.Value($"call i64 {StdFunction("Find")}(ptr {items}, i64 {count}, ptr {needle}, i64 {length})");
            return Position(fn.Value($"trunc i64 {found} to i32"));
        }

        var parameter = run ? "ptr" : Ir(list.Element);
        var name = Helper($"indexof.{(run ? "run." : "")}{Lower.Mangle(list)}", "i32", ["ptr", parameter], () =>
        {
            if (run)
            {
                IndexOfRunBody(list, c.Line);
            }
            else
            {
                IndexOfBody(list, c.Line);
            }
        });
        return Position(fn.Value($"call i32 {name}(ptr {source}, {parameter} {value})"));
    }

    /// <summary>The helper of <c>IndexOf(item)</c>: the list in <c>%a0</c>, the item in <c>%a1</c>; the position of the first equal item, or -1.</summary>
    private void IndexOfBody(ListType list, int line)
    {
        var element = Ir(list.Element);
        var count = ListCount("%a0");
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a0)");
        var counter = fn.Alloca("i32", "item");
        fn.Emit($"store i32 0, ptr {counter}");
        var check = fn.Label("check");
        var item = fn.Label("item");
        var next = fn.Label("next");
        var found = fn.Label("found");
        var none = fn.Label("none");
        fn.Branch(check);
        fn.Block(check);
        var i = fn.Value($"load i32, ptr {counter}");
        fn.CondBranch(fn.Value($"icmp slt i32 {i}, {count}"), item, none);
        fn.Block(item);
        var a = fn.Value($"load {element}, ptr {fn.Value($"getelementptr {element}, ptr {items}, i64 {fn.Value($"sext i32 {i} to i64")}")}");
        fn.CondBranch(ItemEqual(a, "%a1", list.Element, line), found, next);
        fn.Block(found);
        fn.Return($"i32 {i}");
        fn.Block(next);
        fn.Emit($"store i32 {fn.Value($"add i32 {i}, 1")}, ptr {counter}");
        fn.Branch(check);
        fn.Block(none);
        fn.Return("i32 -1");
    }

    /// <summary>The helper of <c>IndexOf(value)</c> of a run: the list in <c>%a0</c>, the run in <c>%a1</c>; the first position at which every item of the run equals the list's, or -1.</summary>
    private void IndexOfRunBody(ListType list, int line)
    {
        var element = Ir(list.Element);
        var n = ListCount("%a0");
        var m = ListCount("%a1");
        var first = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a0)");
        var second = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a1)");
        var fits = fn.Label("fits");
        var none = fn.Label("none");
        fn.CondBranch(fn.Value($"icmp sle i32 {m}, {n}"), fits, none);
        fn.Block(fits);
        var last = fn.Value($"sub i32 {n}, {m}");
        var at = fn.Alloca("i32", "at");
        var offset = fn.Alloca("i32", "offset");
        fn.Emit($"store i32 0, ptr {at}");
        var window = fn.Label("window");
        var start = fn.Label("start");
        var compare = fn.Label("compare");
        var item = fn.Label("item");
        var step = fn.Label("step");
        var advance = fn.Label("advance");
        var found = fn.Label("found");
        fn.Branch(window);
        fn.Block(window);
        var position = fn.Value($"load i32, ptr {at}");
        fn.CondBranch(fn.Value($"icmp sle i32 {position}, {last}"), start, none);
        fn.Block(start);
        fn.Emit($"store i32 0, ptr {offset}");
        fn.Branch(compare);
        fn.Block(compare);
        var j = fn.Value($"load i32, ptr {offset}");
        fn.CondBranch(fn.Value($"icmp slt i32 {j}, {m}"), item, found);
        fn.Block(item);
        var a = fn.Value($"load {element}, ptr {fn.Value($"getelementptr {element}, ptr {first}, i64 {fn.Value($"sext i32 {fn.Value($"add i32 {position}, {j}")} to i64")}")}");
        var b = fn.Value($"load {element}, ptr {fn.Value($"getelementptr {element}, ptr {second}, i64 {fn.Value($"sext i32 {j} to i64")}")}");
        fn.CondBranch(ItemEqual(a, b, list.Element, line), step, advance);
        fn.Block(step);
        fn.Emit($"store i32 {fn.Value($"add i32 {j}, 1")}, ptr {offset}");
        fn.Branch(compare);
        fn.Block(advance);
        fn.Emit($"store i32 {fn.Value($"add i32 {position}, 1")}, ptr {at}");
        fn.Branch(window);
        fn.Block(found);
        fn.Return($"i32 {position}");
        fn.Block(none);
        fn.Return("i32 -1");
    }

    /// <summary>
    /// X9: whether two items of a list are equal as <c>==</c> finds two items of a list equal (M9): a <c>double</c> or a <c>float</c> by
    /// IEEE's <c>==</c>, NaN unequal to NaN, and so the items of the lists and the cases of the unions among them, and a <c>data</c> value
    /// as D3 compares it (<see cref="Equal"/>).
    /// </summary>
    private string ItemEqual(string a, string b, KurzType element, int line) =>
        element is PrimitiveType p && Types.IsFloating(p) ? fn.Value($"fcmp oeq {Ir(p)} {a}, {b}") : Equal(a, b, element, line, ieee: true);

    /// <summary>X1, X9: a position found, an <c>i32</c> that is -1 where none was, as an <c>int?</c>, <c>null</c> for -1, as Rust's <c>find</c> gives <c>None</c> where C#'s gives -1.</summary>
    private string Position(string found)
    {
        var result = Types.Nullable(Types.Int);
        var none = MakeUnion(result, Types.Null, "zeroinitializer");
        var some = MakeUnion(result, Types.Int, found);
        return fn.Value($"select i1 {fn.Value($"icmp slt i32 {found}, 0")}, {Ir(result)} {none}, {Ir(result)} {some}");
    }

    /// <summary>
    /// X1: <c>s.Contains(x)</c>, <c>s.StartsWith(x)</c>, <c>s.EndsWith(x)</c> and <c>s.IndexOf(x)</c> of a <c>string</c> or of a <c>char</c>,
    /// over the string's bytes, ordinal (T32): a <c>char</c> is searched for as its UTF-8 bytes, so that a position found is at the start
    /// of a code point (X2), and an empty <c>x</c> is found at 0. The string is read before the argument and held without a count of its
    /// own while the argument is made and while the search runs: an argument that changes it is the checker's <c>changed-while-held</c>
    /// (M7). The standard library's <c>Find</c> searches, in linear time, and its <c>BytesAt</c> compares the start and the end.
    /// </summary>
    private string EmitStringSearch(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var argument = c.Arguments[0].Value;
        var (needle, m) = Needle(EmitExpression(argument), TypeOf(argument));
        var (hay, n, _) = StringParts(text);
        if (member.Member is "StartsWith" or "EndsWith")
        {
            var at = member.Member == "StartsWith" ? "0" : fn.Value($"sub i64 {n}, {m}");
            return fn.Value($"call i1 {StdFunction("BytesAt")}(ptr {hay}, i64 {n}, ptr {needle}, i64 {m}, i64 {at})");
        }

        var found = fn.Value($"call i64 {StdFunction("Find")}(ptr {hay}, i64 {n}, ptr {needle}, i64 {m})");
        return member.Member == "Contains" ? fn.Value($"icmp sge i64 {found}, 0") : Position(fn.Value($"trunc i64 {found} to i32"));
    }

    /// <summary>A string's bytes, their count without the mark of a block, and whether it holds a block, the mark (T15).</summary>
    private (string Bytes, string Count, string Block) StringParts(string text)
    {
        var marked = fn.Value($"extractvalue %kz.str {text}, 1");
        return (fn.Value($"extractvalue %kz.str {text}, 0"), fn.Value($"and i64 {marked}, 9223372036854775807"), fn.Value($"icmp slt i64 {marked}, 0"));
    }

    /// <summary>The bytes a member of a string looks for and their count: a <c>string</c>'s own, or a <c>char</c>'s UTF-8 bytes, written to a buffer of the function (X1, X4, X5).</summary>
    private (string Bytes, string Count) Needle(string value, KurzType type)
    {
        if (type is PrimitiveType { Kind: Primitive.Char })
        {
            var buffer = fn.Alloca("[4 x i8]", "char");
            return (buffer, fn.Value($"call i64 {StdFunction("Utf8")}(i32 {value}, ptr {buffer})"));
        }

        var (bytes, count, _) = StringParts(value);
        return (bytes, count);
    }

    /// <summary>A string value of the bytes at <paramref name="bytes"/>, with <paramref name="marked"/> their count, marked where they are a block's (T15).</summary>
    private string MakeString(string bytes, string marked) =>
        fn.Value($"insertvalue %kz.str {fn.Value($"insertvalue %kz.str undef, ptr {bytes}, 0")}, i64 {marked}, 1");

    /// <summary>
    /// X4: the part of a string from the position <paramref name="start"/> to <paramref name="end"/>, a string with a count of its own:
    /// the string itself, held once more, where the part is all of it; the empty string where it is nothing; its bytes in place where the
    /// string holds no block, a literal, whose bytes are never freed; else a new block with a copy, the standard library's
    /// <c>StringOf</c>, as Rust's <c>trim</c> gives a slice and Kurz has no view into a block (section 4). A block the system refuses
    /// ends the process (M9).
    /// </summary>
    private string Piece(string text, string start, string end)
    {
        var name = Helper("piece.string", "%kz.str", ["%kz.str", "i64", "i64"], () =>
        {
            var (bytes, count, block) = StringParts("%a0");
            var whole = fn.Label("whole");
            var part = fn.Label("part");
            fn.CondBranch(fn.Value($"and i1 {fn.Value("icmp eq i64 %a1, 0")}, {fn.Value($"icmp eq i64 %a2, {count}")}"), whole, part);
            fn.Block(whole);
            Retain("%a0", Types.String);
            fn.Return("%kz.str %a0");
            fn.Block(part);
            var length = fn.Value("sub i64 %a2, %a1");
            var from = fn.Value($"getelementptr i8, ptr {bytes}, i64 %a1");
            var empty = fn.Label("empty");
            var some = fn.Label("some");
            fn.CondBranch(fn.Value($"icmp eq i64 {length}, 0"), empty, some);
            fn.Block(empty);
            fn.Return($"%kz.str {StringValue(string.Empty)}");
            fn.Block(some);
            var copy = fn.Label("copy");
            var literal = fn.Label("literal");
            fn.CondBranch(block, copy, literal);
            fn.Block(literal);
            fn.Return($"%kz.str {MakeString(from, length)}");
            fn.Block(copy);
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            var made = fn.Value($"call ptr {StdFunction("StringOf")}(ptr {from}, i64 {length}, ptr {asked})");
            Refused(fn.Value($"icmp eq ptr {made}, null"), asked);
            fn.Return($"%kz.str {MakeString(made, fn.Value($"or i64 {length}, -9223372036854775808"))}");
        });
        return fn.Value($"call %kz.str {name}(%kz.str {text}, i64 {start}, i64 {end})");
    }

    /// <summary>
    /// X3, X5, X6: the string a member of a string made through the standard library, with a count of its own: <paramref name="same"/>,
    /// held once more, where <paramref name="made"/> is -1, the string as it was; the empty string where it is 0; else the new block's
    /// bytes at <paramref name="bytes"/>, <paramref name="made"/> of them.
    /// </summary>
    private string Made(string bytes, string made, string? same)
    {
        var result = fn.Alloca("%kz.str", "made");
        var done = fn.Label("made.done");
        if (same is not null)
        {
            var kept = fn.Label("made.same");
            var other = fn.Label("made.other");
            fn.CondBranch(fn.Value($"icmp eq i64 {made}, -1"), kept, other);
            fn.Block(kept);
            Retain(same, Types.String);
            fn.Emit($"store %kz.str {same}, ptr {result}");
            fn.Branch(done);
            fn.Block(other);
        }

        var empty = fn.Label("made.empty");
        var some = fn.Label("made.some");
        fn.CondBranch(fn.Value($"icmp eq i64 {made}, 0"), empty, some);
        fn.Block(empty);
        fn.Emit($"store %kz.str {StringValue(string.Empty)}, ptr {result}");
        fn.Branch(done);
        fn.Block(some);
        fn.Emit($"store %kz.str {MakeString(bytes, fn.Value($"or i64 {made}, -9223372036854775808"))}, ptr {result}");
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load %kz.str, ptr {result}");
    }

    /// <summary>The arguments of a call in the order they are written, each made once (D11), and the one each slot took.</summary>
    private Func<int, (string Value, Expression Argument)> WrittenArguments(CallExpression c, CallInfo info)
    {
        var values = new List<string>();
        foreach (var argument in c.Arguments)
        {
            values.Add(EmitExpression(argument.Value));
        }

        return slot =>
        {
            var i = info.Binding.ToList().IndexOf(slot);
            return (values[i], c.Arguments[i].Value);
        };
    }

    /// <summary>
    /// X4: a string's trims, each the part of the string between the positions the standard library gives (<see cref="Piece"/>):
    /// <c>TrimStart</c> and <c>TrimEnd</c> over Unicode's White_Space, as Rust's <c>trim_start</c> and <c>trim_end</c>, their ASCII
    /// forms over ASCII's whitespace, as <c>trim_ascii_start</c>, <c>Trim</c> and <c>TrimAscii</c> both ends, and <c>Trim(c)</c>,
    /// <c>TrimStart(c)</c> and <c>TrimEnd(c)</c> the repeats of a character's UTF-8 bytes at both ends, the start or the end, as
    /// <c>trim_matches(c)</c>, <c>trim_start_matches(c)</c> and <c>trim_end_matches(c)</c>. The string is held without a count of its
    /// own while the argument is made: an argument that changes it is the checker's <c>changed-while-held</c> (M7).
    /// </summary>
    private string EmitStringTrim(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var start = "0";
        string end;
        if (c.Arguments.Count == 1)
        {
            var (run, m) = Needle(EmitExpression(c.Arguments[0].Value), Types.Char);
            var (bytes, n, _) = StringParts(text);
            if (member.Member != "TrimEnd")
            {
                start = fn.Value($"call i64 {StdFunction("RunStart")}(ptr {bytes}, i64 {n}, ptr {run}, i64 {m})");
            }

            end = member.Member == "TrimStart" ? n : fn.Value($"call i64 {StdFunction("RunEnd")}(ptr {bytes}, i64 {start}, i64 {n}, ptr {run}, i64 {m})");
        }
        else
        {
            var (bytes, n, _) = StringParts(text);
            var ascii = member.Member.EndsWith("Ascii", StringComparison.Ordinal) ? "true" : "false";
            if (member.Member is not ("TrimEnd" or "TrimEndAscii"))
            {
                start = fn.Value($"call i64 {StdFunction("TrimStart")}(ptr {bytes}, i64 {n}, i1 {ascii})");
            }

            end = member.Member is "TrimStart" or "TrimStartAscii" ? n : fn.Value($"call i64 {StdFunction("TrimEnd")}(ptr {bytes}, i64 {start}, i64 {n}, i1 {ascii})");
        }

        return Piece(text, start, end);
    }

    /// <summary>
    /// X5: <c>s.Split(separator)</c>, of a <c>string</c> or of a <c>char</c>'s UTF-8 bytes, the list of strings the standard library's
    /// <c>Split</c> makes, with a count of its own. The string is held without a count of its own while the argument is made (M7). A block
    /// the system refuses ends the process (M9).
    /// </summary>
    private string EmitStringSplit(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var argument = c.Arguments[0].Value;
        var (separator, m) = Needle(EmitExpression(argument), TypeOf(argument));
        var (bytes, n, block) = StringParts(text);
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var list = fn.Value($"call ptr {StdFunction("Split")}(ptr {bytes}, i64 {n}, i1 {block}, ptr {separator}, i64 {m}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {list}, null"), asked);
        return list;
    }

    /// <summary>
    /// X5: <c>s.Replace(old, new)</c>, each of a <c>string</c> or of a <c>char</c>'s UTF-8 bytes, the string the standard library's
    /// <c>Replace</c> makes (<see cref="Made"/>). The string, and <c>old</c>, are held without a count of their own while the arguments
    /// after them are made (M7). A block the system refuses ends the process (M9).
    /// </summary>
    private string EmitStringReplace(CallExpression c, CallInfo info)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var slot = WrittenArguments(c, info);
        var (oldValue, oldArgument) = slot(0);
        var (old, m) = Needle(oldValue, TypeOf(oldArgument));
        var (newValue, newArgument) = slot(1);
        var (replacement, k) = Needle(newValue, TypeOf(newArgument));
        var (bytes, n, _) = StringParts(text);
        var made = fn.Alloca("i64", "made");
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var result = fn.Value($"call ptr {StdFunction("Replace")}(ptr {bytes}, i64 {n}, ptr {old}, i64 {m}, ptr {replacement}, i64 {k}, ptr {made}, ptr {asked})");
        Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        return Made(result, fn.Value($"load i64, ptr {made}"), text);
    }

    /// <summary>
    /// X3: <c>s.ToLower()</c> and <c>s.ToUpper()</c>, the string the standard library's <c>UnicodeCase</c> makes by Unicode's full
    /// mapping, with the table of the direction (<see cref="CaseTable"/>), and <c>s.ToLowerAscii()</c> and <c>s.ToUpperAscii()</c>, the
    /// string its <c>AsciiCase</c> makes; each the string itself where nothing changes (<see cref="Made"/>). A block the system refuses
    /// ends the process (M9).
    /// </summary>
    private string EmitStringCase(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var (bytes, n, _) = StringParts(text);
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        if (member.Member is "ToLower" or "ToUpper")
        {
            var made = fn.Alloca("i64", "case.made");
            fn.Emit($"store i64 0, ptr {made}");
            var toUpper = member.Member == "ToUpper";
            var changed = fn.Value($"call ptr {StdFunction("UnicodeCase")}(ptr {bytes}, i64 {n}, i1 {(toUpper ? "true" : "false")}, ptr {CaseTable(toUpper)}, ptr {made}, ptr {asked})");
            Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
            var length = fn.Value($"load i64, ptr {made}");
            return Made(changed, fn.Value($"select i1 {fn.Value($"icmp eq ptr {changed}, null")}, i64 -1, i64 {length}"), text);
        }

        var upper = member.Member == "ToUpperAscii" ? "true" : "false";
        var result = fn.Value($"call ptr {StdFunction("AsciiCase")}(ptr {bytes}, i64 {n}, i1 {upper}, ptr {asked})");
        Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        return Made(result, fn.Value($"select i1 {fn.Value($"icmp eq ptr {result}, null")}, i64 -1, i64 {n}"), text);
    }

    /// <summary>
    /// The global that holds the case table of one direction (X3), written once into a program that changes case by Unicode's mapping in
    /// that direction: four counts, of the runs, the mappings to several code points and the ranges of Cased and Case_Ignorable, then
    /// those numbers of <see cref="CaseTables"/> in that order, as the standard library's <c>CaseMapped</c> reads them. Only lowering reads
    /// the ranges, for the final sigma, so the table of uppercasing holds none.
    /// </summary>
    private string CaseTable(bool upper)
    {
        var name = upper ? "@\"case.upper\"" : "@\"case.lower\"";
        if (helpers.Add(name))
        {
            var runs = upper ? CaseTables.UpperRuns : CaseTables.LowerRuns;
            var multis = upper ? CaseTables.UpperMultis : CaseTables.LowerMultis;
            var cased = upper ? [] : CaseTables.Cased;
            var ignorable = upper ? [] : CaseTables.CaseIgnorable;
            int[] words = [runs.Length / 4, multis.Length / 4, cased.Length / 2, ignorable.Length / 2, .. runs, .. multis, .. cased, .. ignorable];
            module.Global($"{name} = private unnamed_addr constant [{words.Length} x i32] [{string.Join(", ", words.Select(w => $"i32 {w}"))}]");
        }

        return name;
    }

    /// <summary>
    /// X2: <c>s.Substring(startIndex, length)</c>, the part of the string from the byte at <c>startIndex</c>, <c>length</c> bytes of it
    /// (<see cref="Piece"/>). A part that reaches outside the string raises <c>index-out-of-range</c>, and one whose start or end falls
    /// inside a code point <c>not-char-boundary</c>, which the runtime's <c>NotCharBoundary</c> writes with the position on the line below
    /// (X10), as Rust's <c>&amp;s[a..b]</c> panics; the standard library's <c>SubstringCut</c> reads which, the bounds first. The string is
    /// held without a count of its own while the arguments are made: an argument that changes it is <c>changed-while-held</c> (M7).
    /// </summary>
    private string EmitStringSubstring(CallExpression c, CallInfo info)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var arguments = Arguments(c, info, _ => Types.Int, _ => false, _ => null, cConvention: false, owned: false);
        var start = fn.Value($"sext i32 {arguments[0]["i32 ".Length..]} to i64");
        var length = fn.Value($"sext i32 {arguments[1]["i32 ".Length..]} to i64");
        var (bytes, n, _) = StringParts(text);
        var cut = fn.Value($"call i64 {StdFunction("SubstringCut")}(ptr {bytes}, i64 {n}, i64 {start}, i64 {length})");
        var stands = fn.Label("cut");
        var refused = fn.Label("no.cut");
        fn.CondBranch(fn.Value($"icmp eq i64 {cut}, -1"), stands, refused);
        fn.Block(refused);
        var outside = fn.Label("outside");
        var inside = fn.Label("inside");
        fn.CondBranch(fn.Value($"icmp eq i64 {cut}, -2"), outside, inside);
        fn.Block(outside);
        Raise(ErrorIds.IndexOutOfRange, c.Line);
        fn.Block(inside);
        fn.Emit($"call void {RuntimeFunction("NotCharBoundary")}(i64 {cut}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        fn.Unreachable();
        fn.Block(stands);
        return Piece(text, start, fn.Value($"add i64 {start}, {length}"));
    }

    /// <summary>
    /// The 128-bit powers of five of Eisel-Lemire (X7), 5^q for q from -342 to 308, two words each, the high one first, as fast_float's
    /// and Rust's <c>POWER_OF_FIVE_128</c> hold them: for q from 0, 5^q shifted until its first bit is bit 127 and cut to 128 bits; for
    /// a negative q, 2^b / 5^-q rounded down and then up by one, cut to 128 bits, where b is 127 more than the bits of 5^-q from 5^-27
    /// up and twice those bits and 128 more below it.
    /// </summary>
    internal static readonly Lazy<ulong[]> Fives = new(() =>
    {
        var words = new ulong[2 * 651];
        var top = BigInteger.One << 128;
        for (var q = -342; q <= 308; q++)
        {
            BigInteger power;
            if (q >= 0)
            {
                power = BigInteger.Pow(5, q);
                while (power < (BigInteger.One << 127))
                {
                    power <<= 1;
                }
            }
            else
            {
                var five = BigInteger.Pow(5, -q);
                var bits = 0;
                while ((BigInteger.One << bits) < five)
                {
                    bits++;
                }

                power = ((BigInteger.One << (q >= -27 ? bits + 127 : (2 * bits) + 128)) / five) + 1;
            }

            while (power >= top)
            {
                power >>= 1;
            }

            words[2 * (q + 342)] = (ulong)(power >> 64);
            words[(2 * (q + 342)) + 1] = (ulong)(power & ulong.MaxValue);
        }

        return words;
    });

    /// <summary>The global that holds <see cref="Fives"/>, written once into a program that parses a <c>double</c> or a <c>float</c> (X7).</summary>
    private string FivesTable()
    {
        const string name = "@\"parse.fives\"";
        if (helpers.Add(name))
        {
            var words = string.Join(", ", Fives.Value.Select(w => $"i64 {IntegerText(w, Types.ULong)}"));
            module.Global($"{name} = private unnamed_addr constant [{Fives.Value.Length} x i64] [{words}]");
        }

        return name;
    }

    /// <summary>
    /// X7: <c>long.Parse(s)</c>, and the same call on each number type, the number the string's bytes give, or the case <c>ParseError</c> of
    /// the kind the standard library gives (section 12, the owner, 2026-10-31, "Outcome long | ParseError"): an integer from
    /// <c>ParseInteger</c>, which reads it against the type's range; the bits of a <c>double</c> and a <c>float</c> from <c>ParseDouble</c>
    /// and <c>ParseFloat</c>, through a helper that keeps the 768 bytes of their slow path on its stack, as Rust keeps its <c>Decimal</c>,
    /// so that a parse asks the allocator for nothing; and the two words of a <c>decimal</c> from <c>ParseDecimal</c>. The string is
    /// borrowed for the call.
    /// </summary>
    private string EmitParse(CallExpression c, CallInfo info)
    {
        var text = Arguments(c, info, _ => Types.String, _ => false, _ => null, cConvention: false, owned: false).Single()["%kz.str ".Length..];
        var union = (UnionType)Sub(info.Result);
        var number = union.Cases[0];
        var error = (DataType)union.Cases[1];
        var (bytes, count, _) = StringParts(text);
        string kind;
        Func<string> value;
        if (Types.IsFloating(number))
        {
            var single = number is PrimitiveType { Kind: Primitive.Float };
            var bitsType = single ? "i32" : "i64";
            var parse = StdFunction(single ? "ParseFloat" : "ParseDouble");
            var helper = Helper(single ? "parse.float" : "parse.double", "i32", ["ptr", "i64", "ptr"], () =>
            {
                var digits = fn.Alloca("[768 x i8]", "digits");
                fn.Return($"i32 {fn.Value($"call i32 {parse}(ptr %a0, i64 %a1, ptr {digits}, ptr {FivesTable()}, ptr %a2)")}");
            });
            var bits = fn.Alloca(bitsType, "bits");
            kind = fn.Value($"call i32 {helper}(ptr {bytes}, i64 {count}, ptr {bits})");
            value = () => fn.Value($"bitcast {bitsType} {fn.Value($"load {bitsType}, ptr {bits}")} to {Ir(number)}");
        }
        else if (number is PrimitiveType { Kind: Primitive.Decimal })
        {
            var low = fn.Alloca("i64", "low");
            var high = fn.Alloca("i64", "high");
            kind = fn.Value($"call i32 {StdFunction("ParseDecimal")}(ptr {bytes}, i64 {count}, ptr {low}, ptr {high})");
            value = () => DecimalOf(fn.Value($"load i64, ptr {low}"), fn.Value($"load i64, ptr {high}"));
        }
        else
        {
            var width = Lower.Size(number) * 8;
            var signed = Types.IsSigned(number);
            var most = signed ? (BigInteger.One << (width - 1)) - 1 : (BigInteger.One << width) - 1;
            var least = signed ? BigInteger.One << (width - 1) : BigInteger.Zero;
            var magnitude = fn.Alloca("i64", "magnitude");
            var integer = fn.Value($"call i32 {StdFunction("ParseInteger")}(ptr {bytes}, i64 {count}, i64 {IntegerText(most, Types.ULong)}, i64 {IntegerText(least, Types.ULong)}, ptr {magnitude})");
            kind = integer;
            value = () =>
            {
                var positive = fn.Value($"load i64, ptr {magnitude}");
                var whole = fn.Value($"select i1 {fn.Value($"icmp eq i32 {integer}, -2")}, i64 {fn.Value($"sub i64 0, {positive}")}, i64 {positive}");
                return width == 64 ? whole : fn.Value($"trunc i64 {whole} to {Ir(number)}");
            };
        }

        var result = fn.Alloca(Ir(union), "parsed");
        var done = fn.Label("parse.done");
        var read = fn.Label("parse.number");
        var failed = fn.Label("parse.failed");
        fn.CondBranch(fn.Value($"icmp slt i32 {kind}, 0"), read, failed);
        fn.Block(read);
        fn.Emit($"store {Ir(union)} {MakeUnion(union, number, value())}, ptr {result}");
        fn.Branch(done);
        fn.Block(failed);
        var kindType = (EnumType)Layout.Fields(error)[0];
        // `ParseKind` numbers none of its values, so that each is stored as its index, the one the standard library gives (D18)
        var stored = Ir(kindType) == "i32" ? kind : fn.Value($"trunc i32 {kind} to {Ir(kindType)}");
        var failure = fn.Value($"insertvalue {Ir(error)} undef, {Ir(kindType)} {stored}, 0");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, error, failure)}, ptr {result}");
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {result}");
    }

    /// <summary>Whether the data type is <c>ParseError</c>, which no file can declare beside the one every file sees (X7, V9).</summary>
    private static bool IsParseError(KurzType type) => type is DataType { Symbol.Name: "ParseError" };

    /// <summary>X7: in the helper of the text of a <c>ParseError</c> in <c>%a0</c>: the words of its kind in lower case, <c>too large</c>.</summary>
    private void ParseErrorText(DataType error)
    {
        var kind = (EnumType)Layout.Fields(error)[0];
        var value = fn.Value($"extractvalue {Ir(error)} %a0, 0");
        var values = kind.Symbol.Values;
        var labels = values.Select(_ => fn.Label("kind")).ToList();
        var other = fn.Label("other");
        fn.Terminate($"switch {Ir(kind)} {value}, label %{other} [ {string.Join(" ", values.Select((_, i) => $"{Ir(kind)} {IntegerText(Lower.EnumStored(kind.Symbol, i), kind)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < values.Count; i++)
        {
            fn.Block(labels[i]);
            fn.Return($"%kz.str {StringValue(Words(values[i]))}");
        }

        fn.Block(other);
        fn.Unreachable(); // a kind is one of its values (D18)
    }

    /// <summary>
    /// X6: <c>string.Join(separator, values)</c>, the texts of the items (A4) with the separator, a string or a <c>char</c>'s UTF-8 bytes,
    /// between two of them, joined by the standard library's <c>Join</c>: a list of strings is its own texts, and the texts of the items of any other type are made into a
    /// buffer, as a list's text makes them (<see cref="ListText"/>), and let go of after the join. The separator and the list are held
    /// without a count of their own while the arguments after them are made, so that an argument that takes one of them as
    /// <c>mut</c> is the checker's <c>changed-while-held</c> (M7). The buffer comes from the runtime's <c>Allocate</c>,
    /// which ends the process itself with <c>allocation-failed (&lt;n&gt; bytes)</c> where the system refuses it (O11), so the helper reads
    /// no null there; a block of the joined text the system refuses, or one past the largest <c>int</c>, is the library's
    /// <c>asked</c>, which the helper raises (M9, T15).
    /// </summary>
    private string EmitStringJoin(CallExpression c, CallInfo info)
    {
        var slot = WrittenArguments(c, info);
        var (separatorValue, separatorArgument) = slot(0);
        var (list, listArgument) = slot(1);
        var (separator, length) = Needle(separatorValue, TypeOf(separatorArgument));
        var type = (ListType)TypeOf(listArgument);
        var name = Helper($"join.{Lower.Mangle(type)}", "%kz.str", ["ptr", "i64", "ptr"], () =>
        {
            var count = fn.Value($"sext i32 {ListCount("%a2")} to i64");
            var (bytes, m) = ("%a0", "%a1");
            var made = fn.Alloca("i64", "made");
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            var texts = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a2)");
            string? buffer = null;
            string? size = null;
            var makes = type.Element is not PrimitiveType { Kind: Primitive.String };
            if (makes)
            {
                // the items' texts, into a buffer laid out as a list of strings is
                size = fn.Value($"mul i64 {count}, 16");
                buffer = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {size})");
                var at = fn.Alloca("i64", "at");
                fn.Emit($"store i64 0, ptr {at}");
                // the line is the first call's of this list type: `not supported` of an item type without a text is the one diagnostic of
                // the run, which ends at the call whose emission makes this helper, so no later call of the type is reached
                EachItem("%a2", type.Element, item =>
                {
                    var i = fn.Value($"load i64, ptr {at}");
                    fn.Emit($"store %kz.str {TextOf(item, type.Element, c.Line)}, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {i}")}");
                    fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
                });
                texts = buffer;
            }

            var joined = fn.Value($"call ptr {StdFunction("Join")}(ptr {texts}, i64 {count}, ptr {bytes}, i64 {m}, ptr {made}, ptr {asked})");
            if (makes)
            {
                if (MakesText(type.Element))
                {
                    // the items' texts, which the joined text holds no count of
                    EachText(buffer!, count, text => Release(text, Types.String));
                }

                fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {size})");
            }

            Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
            fn.Return($"%kz.str {Made(joined, fn.Value($"load i64, ptr {made}"), null)}");
        });
        return fn.Value($"call %kz.str {name}(ptr {separator}, i64 {length}, ptr {list})");
    }

    /// <summary>For each of the <paramref name="count"/> strings at <paramref name="buffer"/>, laid out as a list of strings is, <paramref name="each"/>.</summary>
    private void EachText(string buffer, string count, Action<string> each)
    {
        var at = fn.Alloca("i64", "text");
        fn.Emit($"store i64 0, ptr {at}");
        var check = fn.Label("text.check");
        var body = fn.Label("text.each");
        var done = fn.Label("text.done");
        fn.Branch(check);
        fn.Block(check);
        var i = fn.Value($"load i64, ptr {at}");
        fn.CondBranch(fn.Value($"icmp slt i64 {i}, {count}"), body, done);
        fn.Block(body);
        each(fn.Value($"load %kz.str, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {i}")}"));
        fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
        fn.Branch(check);
        fn.Block(done);
    }

    /// <summary>
    /// C6: <c>for x in list</c> runs its block for each item, in order, over the list as it was when the loop started: the loop holds
    /// the list's block, so a write to the variable inside the block copies it first (M3). The name is the item, borrowed from that
    /// block.
    /// </summary>
    private void EmitForInList(ForInStatement f, ListType type)
    {
        var list = Owned(f.Collection);
        regions[^1].Add((list, type)); // the statement's end, a `return` or a `break` lets go of it
        var variable = Model.DeclarationOf(f);
        var slot = fn.Alloca(Ir(type.Element), f.Variable);
        slots[variable] = slot;
        var count = ListCount(list);
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})");
        var counter = fn.Alloca("i32", "i");
        fn.Emit($"store i32 0, ptr {counter}");
        var check = fn.Label("check");
        var body = fn.Label("for");
        var next = fn.Label("next");
        var exit = fn.Label("endfor");
        fn.Block(check);
        var i = fn.Value($"load i32, ptr {counter}");
        fn.CondBranch(fn.Value($"icmp slt i32 {i}, {count}"), body, exit);
        fn.Block(body);
        var wide = fn.Value($"sext i32 {i} to i64");
        fn.Emit($"store {Ir(type.Element)} {fn.Value($"load {Ir(type.Element)}, ptr {fn.Value($"getelementptr {Ir(type.Element)}, ptr {items}, i64 {wide}")}")}, ptr {slot}");
        loops.Push((exit, next, regions.Count, scopes.Count));
        EmitBlock(f.Body);
        loops.Pop();
        fn.Block(next);
        var again = fn.Value($"load i32, ptr {counter}");
        fn.Emit($"store i32 {fn.Value($"add i32 {again}, 1")}, ptr {counter}");
        fn.Branch(check);
        fn.Block(exit);
    }

    /// <summary>
    /// A4: the text of a list, its items' texts between <c>[</c> and <c>]</c> with <c>, </c> between them, which the runtime's
    /// <c>ListText</c> joins from a buffer of the items' texts.
    /// </summary>
    private string ListText(string value, ListType type, int line)
    {
        var name = Helper($"text.{Lower.Mangle(type)}", "%kz.str", ["ptr"], () =>
        {
            var count = ListCount("%a0");
            var empty = fn.Label("empty");
            var items = fn.Label("texts");
            fn.CondBranch(fn.Value($"icmp eq i32 {count}, 0"), empty, items);
            fn.Block(empty);
            fn.Return($"%kz.str {StringValue("[]")}");
            fn.Block(items);
            var wide = fn.Value($"sext i32 {count} to i64");
            var bytes = fn.Value($"mul i64 {wide}, 16");
            var buffer = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {bytes})");
            var at = fn.Alloca("i64", "at");
            fn.Emit($"store i64 0, ptr {at}");
            EachItem("%a0", type.Element, item =>
            {
                var i = fn.Value($"load i64, ptr {at}");
                fn.Emit($"store %kz.str {TextOf(item, type.Element, line)}, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {i}")}");
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
            });
            var text = fn.Value($"call %kz.str {RuntimeFunction("ListText")}(ptr {buffer}, i64 {wide})");
            if (MakesText(type.Element))
            {
                // the items' texts, which the joined text holds no count of
                fn.Emit($"store i64 0, ptr {at}");
                var check = fn.Label("made");
                var each = fn.Label("made.text");
                var done = fn.Label("made.done");
                fn.Block(check);
                var i = fn.Value($"load i64, ptr {at}");
                fn.CondBranch(fn.Value($"icmp slt i64 {i}, {wide}"), each, done);
                fn.Block(each);
                Release(fn.Value($"load %kz.str, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {i}")}"), Types.String);
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
                fn.Branch(check);
                fn.Block(done);
            }

            fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {bytes})");
            fn.Return($"%kz.str {text}");
        });
        return fn.Value($"call %kz.str {name}(ptr {value})");
    }
}
