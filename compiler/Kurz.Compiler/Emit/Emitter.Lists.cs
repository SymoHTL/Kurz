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
        ListType or MapType or FunctionType or HandleType or HasherType or PrimitiveType { Kind: Primitive.String } => true,
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
    /// stand in its argument (M7), finds the block shared, copies it (M3) and appends the items of the block as it was. Room for all the
    /// items is made at once, as Rust's <c>Vec::extend_from_slice</c> reserves it (<c>ListReserve</c>), and a block the system refuses
    /// raises <c>allocation-failed</c> (M9).
    /// </summary>
    private string? EmitListAddRange(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var argument = c.Arguments[0].Value;
        string other = null!;
        InRegion(() => other = Owned(argument));
        var place = CollectionPlace(member.Target);
        var add = Helper($"addrange.{Lower.Mangle(list)}", "void", ["ptr", "ptr"], () =>
        {
            var size = ItemSize(list.Element);
            var count = ListCount("%a1");
            var some = fn.Label("some");
            var none = fn.Label("none");
            fn.CondBranch(fn.Value($"icmp sgt i32 {count}, 0"), some, none);
            fn.Block(none);
            fn.Return(null);
            fn.Block(some);
            var more = fn.Value($"sext i32 {count} to i64");
            var block = fn.Value("load ptr, ptr %a0");
            var shared = fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})");
            var asked = fn.Alloca("i64", "asked");
            var room = fn.Value($"call ptr {StdFunction("ListReserve")}(ptr {block}, i64 {more}, i64 {size}, ptr {asked})");
            Refused(fn.Value($"icmp eq ptr {room}, null"), asked);
            if (Counted(list.Element))
            {
                var copied = fn.Label("copied");
                var held = fn.Label("held");
                fn.CondBranch(shared, copied, held);
                fn.Block(copied);
                EachItem(room, list.Element, item => Retain(item, list.Element)); // the copy holds the items' blocks too
                fn.Branch(held);
                fn.Block(held);
                EachItem("%a1", list.Element, item => Retain(item, list.Element)); // and the list holds the added ones
            }

            fn.Emit($"store ptr {room}, ptr %a0");
            fn.Emit($"call void {StdFunction("ListAppend")}(ptr {room}, ptr {fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a1)")}, i64 {more}, i64 {size})");
            fn.Return(null);
        });
        fn.Emit($"call void {add}(ptr {place}, ptr {other})");
        Release(other, list);
        return null;
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
    /// X3: <c>s.ToLowerAscii()</c> and <c>s.ToUpperAscii()</c>, the string the standard library's <c>AsciiCase</c> makes, or the string
    /// itself where no byte changes (<see cref="Made"/>). A block the system refuses ends the process (M9).
    /// </summary>
    private string EmitStringCase(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var text = EmitExpression(member.Target);
        var (bytes, n, _) = StringParts(text);
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var upper = member.Member == "ToUpperAscii" ? "true" : "false";
        var result = fn.Value($"call ptr {StdFunction("AsciiCase")}(ptr {bytes}, i64 {n}, i1 {upper}, ptr {asked})");
        Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        return Made(result, fn.Value($"select i1 {fn.Value($"icmp eq ptr {result}, null")}, i64 -1, i64 {n}"), text);
    }

    /// <summary>
    /// X6: <c>string.Join(separator, values)</c>, the texts of the items (A4) with the separator, a string or a <c>char</c>'s UTF-8 bytes,
    /// between two of them, joined by the standard library's <c>Join</c>: a list of strings is its own texts, and the texts of the items of any other type are made into a
    /// buffer, as a list's text makes them (<see cref="ListText"/>), and let go of after the join. The separator and the list are held
    /// without a count of their own while the arguments after them are made (M7). A block the system refuses ends the process (M9).
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
