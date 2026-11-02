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

    // ---- the sorts of a list (X8) ----------------------------------------------------------------

    /// <summary>The most items for which a sort takes insertion alone, as Rust's <c>sort</c> and <c>sort_unstable</c> take it (<c>MAX_LEN_ALWAYS_INSERTION_SORT</c>).</summary>
    private const int InsertionOnly = 20;

    /// <summary>
    /// The most items of a small sort, Rust's <c>SMALL_SORT_GENERAL_THRESHOLD</c> and <c>SMALL_SORT_NETWORK_THRESHOLD</c>, and the items of
    /// the general small sort's scratch, <c>SMALL_SORT_GENERAL_SCRATCH_LEN</c>, as Rust 1.82's <c>smallsort.rs</c> takes them for an item that
    /// fits 4096 bytes 48 times. An item a sort orders here takes at most 48 bytes, a nullable number of 16 bytes with its tag in a pair of
    /// <c>SortBy</c> with an 8-byte position, so Rust's forms for wider items, insertion up to 16 items and Hoare's partition past 96 bytes,
    /// have no item here and are not emitted.
    /// </summary>
    private const int SmallSortMost = 32;

    private const int SmallScratch = 48;

    /// <summary>The bytes of the stable sort's scratch on the stack, and the bytes up to which its scratch holds every item, as Rust's <c>driftsort_main</c> takes them.</summary>
    private const int StackScratchBytes = 4096;

    private const int FullScratchBytes = 8_000_000;

    /// <summary>What the sort helpers sort: the name of the items in the helpers' keys, their IR type, the helper that gives whether one sorts before another, and the bytes and the alignment of an item.</summary>
    private readonly record struct SortOf(string Key, string Item, string Less, int Size, int Align)
    {
        /// <summary>
        /// The IR type an item is moved as: an aggregate of 8 or 16 bytes as one integer of its bits, as Rust moves an item by copying
        /// its bytes, and every other item as its own type. An aggregate moved as itself is written in its parts and read back whole,
        /// which the processor does not forward from the writes (sortby-int 17.6 ms against 41.6, best of 7).
        /// </summary>
        public string Move => Item[0] is '{' or '%' && Size is 8 or 16 ? $"i{Size * 8}" : Item;
    }

    /// <summary>
    /// X8: <c>list.Sort()</c> and <c>list.SortUnstable()</c> through the place of the list's variable or of the field a path from one ends
    /// at, as <c>Reverse</c> takes it: a list of two items or more in a block other values hold is copied first (M3), and the items are
    /// moved in the block, so no count changes. <c>Sort</c> keeps equal items in their order (<see cref="StableSort"/>, Rust's driftsort),
    /// and <c>SortUnstable</c> takes no buffer (<see cref="UnstableSort"/>, Rust's ipnsort), as Rust's <c>sort</c> and <c>sort_unstable</c>;
    /// both order the items as <see cref="SortLess"/> does.
    /// </summary>
    private string? EmitListSort(CallExpression c, bool stable)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var (size, align) = layout.Measure(list.Element);
        var sorted = new SortOf(Lower.Mangle(list.Element), Ir(list.Element), SortLess(list.Element, c.Line), size, align);
        var sort = Helper($"{(stable ? "sort" : "sort.unstable")}.{Lower.Mangle(list)}", "void", ["ptr"], () =>
        {
            var count = ListCount(fn.Value("load ptr, ptr %a0"));
            var some = fn.Label("some");
            var done = fn.Label("done");
            fn.CondBranch(fn.Value($"icmp sge i32 {count}, 2"), some, done);
            fn.Block(some);
            var items = OwnItems(list);
            var n = fn.Value($"sext i32 {count} to i64");
            fn.Emit($"call void {(stable ? StableSort(sorted) : UnstableSort(sorted))}(ptr {items}, i64 {n})");
            fn.Branch(done);
            fn.Block(done);
            fn.Return(null);
        });
        fn.Emit($"call void {sort}(ptr {CollectionPlace(member.Target)})");
        return null;
    }

    /// <summary>
    /// X8: <c>list.SortBy(keySelector)</c>, as Rust's <c>sort_by_cached_key</c> sorts: the key of each item made once, in the order of the
    /// list, and kept with the item's position as a pair (section 12, the owner, 2026-10-31, "Once per item"), a position of 4 bytes where
    /// that makes the pair smaller (<see cref="PairLayout"/>); the pairs sorted by their keys, and by their positions where two keys are equal,
    /// so that items of equal keys keep their order, without a buffer
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
        var (place, pairSize, pairAlign) = PairLayout(key);
        var pair = $"{{ {Ir(key)}, {place} }}";
        var sorted = new SortOf($"pair.{Lower.Mangle(key)}", pair, PairLess(key, pair, place, c.Line), pairSize, pairAlign);
        string Narrow(string position) => place == "i64" ? position : fn.Value($"trunc i64 {position} to {place}");
        string Wide(string position) => place == "i64" ? position : fn.Value($"zext {place} {position} to i64");
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
                var keyed = fn.Value($"insertvalue {pair} {fn.Value($"insertvalue {pair} undef, {Ir(key)} {made}, 0")}, {place} {Narrow(i)}, 1");
                fn.Emit($"store {pair} {keyed}, ptr {SortAt(pair, pairs, i)}");
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
            });
            var move = fn.Label("move");
            var release = fn.Label("release");
            fn.CondBranch(fn.Value($"icmp sge i32 {count}, 2"), move, release);
            fn.Block(move);
            fn.Emit($"call void {UnstableSort(sorted)}(ptr {pairs}, i64 {n})");
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
            fn.Emit($"store i64 {Wide(fn.Value($"load {place}, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {position}, i32 1")}"))}, ptr {chased}");
            fn.Block(follow);
            var index = fn.Value($"load i64, ptr {chased}");
            fn.CondBranch(fn.Value($"icmp slt i64 {index}, {position}"), moved, swap);
            fn.Block(moved);
            fn.Emit($"store i64 {Wide(fn.Value($"load {place}, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {index}, i32 1")}"))}, ptr {chased}");
            fn.Branch(follow);
            fn.Block(swap);
            fn.Emit($"store {place} {Narrow(index)}, ptr {fn.Value($"getelementptr {pair}, ptr {pairs}, i64 {position}, i32 1")}");
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

    /// <summary>Whether <c>&lt;</c> orders the key as an integer of 64 bits or fewer, signed or not, null where it does not: the integers, <c>char</c>, an enum by its numbers (D12, D18) and a time type of 64 bits.</summary>
    private static bool? PackedKey(KurzType key) => key switch
    {
        _ when Types.IsInteger(key) => Types.IsSigned(key),
        PrimitiveType { Kind: Primitive.Char } => false,
        EnumType e => Lower.EnumSigned(e.Symbol),
        _ when Types.IsTime(key) && !Types.IsWideTime(key) => true,
        _ => null,
    };

    /// <summary>X8: the helper that gives whether the pair of a key and a position in <c>%a0</c> sorts before the one in <c>%a1</c>: by the keys (<see cref="SortLess"/>), and by the positions where neither key sorts before the other, as Rust compares the tuples of <c>sort_by_cached_key</c>.</summary>
    private string PairLess(KurzType key, string pair, string place, int line) => Helper($"sort.less.pair.{Lower.Mangle(key)}", "i1", [pair, pair], () =>
    {
        if (PackedKey(key) is { } signed)
        {
            // a key that `<` orders as an integer of 64 bits or fewer: the key above the position in one integer, compared once,
            // which gives the order of Rust's tuple with no branch on the keys (sortby-int 17.6 ms against 28.9 for Rust's form, the
            // keys compared and then the positions, best of 7)
            var low = place == "i32" ? 32 : 64;
            var bits = (Lower.Size(key) * 8) + low;
            string Packed(string value)
            {
                var high = fn.Value($"{(signed ? "sext" : "zext")} {Ir(key)} {fn.Value($"extractvalue {pair} {value}, 0")} to i{Number(bits)}");
                var position = fn.Value($"zext {place} {fn.Value($"extractvalue {pair} {value}, 1")} to i{Number(bits)}");
                return fn.Value($"or i{Number(bits)} {fn.Value($"shl i{Number(bits)} {high}, {Number(low)}")}, {position}");
            }

            fn.Return($"i1 {fn.Value($"icmp {(signed ? "slt" : "ult")} i{Number(bits)} {Packed("%a0")}, {Packed("%a1")}")}");
            return;
        }

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
        fn.Return($"i1 {fn.Value($"icmp slt {place} {fn.Value($"extractvalue {pair} %a0, 1")}, {fn.Value($"extractvalue {pair} %a1, 1")}")}");
    });

    /// <summary>
    /// The IR type of the position in a pair of <c>SortBy</c>, and the pair's bytes and alignment: 4 bytes where that makes the pair smaller
    /// than 8 bytes do, as Rust's <c>sort_by_cached_key</c> takes a <c>u32</c> where it does, else 8. A list's count is an <c>int</c>, so
    /// every position fits 4 bytes.
    /// </summary>
    private (string Place, int Size, int Align) PairLayout(KurzType key)
    {
        var (size, align) = layout.Measure(key);
        static int Up(int value, int to) => (value + to - 1) / to * to;
        var narrow = (Size: Up(Up(size, 4) + 4, Math.Max(align, 4)), Align: Math.Max(align, 4));
        var wide = (Size: Up(Up(size, 8) + 8, Math.Max(align, 8)), Align: Math.Max(align, 8));
        return narrow.Size < wide.Size ? ("i32", narrow.Size, narrow.Align) : ("i64", wide.Size, wide.Align);
    }

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

    /// <summary>An item of the sort read from its address.</summary>
    private string SortLoad(SortOf s, string at) => fn.Value($"load {s.Move}, ptr {at}, align {Number(s.Align)}");

    /// <summary>An item of the sort written to its address.</summary>
    private void SortStore(SortOf s, string value, string at) => fn.Emit($"store {s.Move} {value}, ptr {at}, align {Number(s.Align)}");

    /// <summary>Whether the item <paramref name="a"/> sorts before <paramref name="b"/>, an <c>i1</c>.</summary>
    private string SortBefore(SortOf s, string a, string b) => fn.Value($"call i1 {s.Less}({s.Item} {SortItem(s, a)}, {s.Item} {SortItem(s, b)})");

    /// <summary>An item moved as <see cref="SortOf.Move"/> read as its own type, through a local the optimiser removes.</summary>
    private string SortItem(SortOf s, string value)
    {
        if (s.Move == s.Item)
        {
            return value;
        }

        var local = fn.Alloca(s.Item, "item");
        SortStore(s, value, local);
        return fn.Value($"load {s.Item}, ptr {local}");
    }

    /// <summary>A local of the IR type, holding <paramref name="first"/>.</summary>
    private string SortLocal(string type, string hint, string first)
    {
        var local = fn.Alloca(type, hint);
        fn.Emit($"store {type} {first}, ptr {local}");
        return local;
    }

    private string SortRead(string type, string local) => fn.Value($"load {type}, ptr {local}");

    private void SortWrite(string type, string local, string value) => fn.Emit($"store {type} {value}, ptr {local}");

    /// <summary>The <paramref name="count"/> items at <paramref name="source"/> copied to <paramref name="target"/>, which they do not overlap.</summary>
    private void SortCopy(SortOf s, string target, string source, string count) => SortCopyBytes(target, source, fn.Value($"mul i64 {count}, {Number(s.Size)}"));

    private void SortCopyBytes(string target, string source, string bytes)
    {
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {target}, ptr {source}, i64 {bytes}, i1 false)");
    }

    /// <summary>The address <paramref name="by"/> items, an <c>i1</c>'s 0 or 1, past <paramref name="at"/>, or before it where <paramref name="back"/>.</summary>
    private string SortStep(SortOf s, string at, string by, bool back = false)
    {
        var step = fn.Value($"zext i1 {by} to i64");
        return SortAt(s.Item, at, back ? fn.Value($"sub i64 0, {step}") : step);
    }

    /// <summary>The smaller of two <c>i64</c>s, read without a sign.</summary>
    private string SortMin(string a, string b) => fn.Value($"select i1 {fn.Value($"icmp ult i64 {a}, {b}")}, i64 {a}, i64 {b}");

    /// <summary>The larger of two <c>i64</c>s, read without a sign.</summary>
    private string SortMax(string a, string b) => fn.Value($"select i1 {fn.Value($"icmp ugt i64 {a}, {b}")}, i64 {a}, i64 {b}");

    /// <summary>
    /// X8: the helper that moves the item at <c>%a1</c> left past the items from <c>%a0</c> on that it sorts before, the items before it
    /// sorted, as Rust's <c>insert_tail</c> does: it reads the item only where it sorts before its left neighbour, and moves each item it
    /// passes one place right, so that equal items keep their order.
    /// </summary>
    private string SortTail(SortOf s) => Helper($"sort.tail.{s.Key}", "void", ["ptr", "ptr"], () =>
    {
        var shift = fn.Label("shift");
        var step = fn.Label("step");
        var next = fn.Label("next");
        var put = fn.Label("put");
        var done = fn.Label("done");
        var before = SortAt(s.Item, "%a1", "-1");
        var item = SortLoad(s, "%a1");
        fn.CondBranch(SortBefore(s, item, SortLoad(s, before)), shift, done);
        fn.Block(shift);
        var gap = SortLocal("ptr", "gap", "%a1");
        var sift = SortLocal("ptr", "sift", before);
        fn.Block(step);
        var at = SortRead("ptr", sift);
        SortStore(s, SortLoad(s, at), SortRead("ptr", gap));
        SortWrite("ptr", gap, at);
        fn.CondBranch(fn.Value($"icmp eq ptr {at}, %a0"), put, next);
        fn.Block(next);
        var lower = SortAt(s.Item, at, "-1");
        SortWrite("ptr", sift, lower);
        fn.CondBranch(SortBefore(s, item, SortLoad(s, lower)), step, put);
        fn.Block(put);
        SortStore(s, item, SortRead("ptr", gap));
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>X8: the helper that sorts the <c>%a1</c> items at <c>%a0</c> by insertion, the first <c>%a2</c> of them sorted, as Rust's <c>insertion_sort_shift_left</c> does (<see cref="SortTail"/>).</summary>
    private string SortInsertion(SortOf s) => Helper($"sort.insertion.{s.Key}", "void", ["ptr", "i64", "i64"], () =>
    {
        var end = SortAt(s.Item, "%a0", "%a1");
        var tail = SortLocal("ptr", "tail", SortAt(s.Item, "%a0", "%a2"));
        var loop = fn.Label("loop");
        var insert = fn.Label("insert");
        var done = fn.Label("done");
        fn.Block(loop);
        var at = SortRead("ptr", tail);
        fn.CondBranch(fn.Value($"icmp ne ptr {at}, {end}"), insert, done);
        fn.Block(insert);
        fn.Emit($"call void {SortTail(s)}(ptr %a0, ptr {at})");
        SortWrite("ptr", tail, SortAt(s.Item, at, "1"));
        fn.Branch(loop);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper that gives the length of the run the <c>%a1</c> items at <c>%a0</c> begin with, and whether it falls, as Rust's
    /// <c>find_existing_run</c> does: a run that falls takes each next item that sorts before the one before it, so that it holds no two
    /// equal items and its reversal keeps the order of equal items, and a run that rises each next item that does not.
    /// </summary>
    private string SortRunOf(SortOf s) => Helper($"sort.run.{s.Key}", "{ i64, i1 }", ["ptr", "i64"], () =>
    {
        var few = fn.Label("few");
        var scan = fn.Label("scan");
        var falling = fn.Label("falling");
        var rising = fn.Label("rising");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value("icmp ult i64 %a1, 2"), few, scan);
        fn.Block(few);
        fn.Return($"{{ i64, i1 }} {fn.Value("insertvalue { i64, i1 } { i64 0, i1 false }, i64 %a1, 0")}");
        fn.Block(scan);
        var falls = SortBefore(s, SortLoad(s, SortAt(s.Item, "%a0", "1")), SortLoad(s, "%a0"));
        var run = SortLocal("i64", "run", "2");
        fn.CondBranch(falls, falling, rising);

        // one loop for each direction, as Rust's `find_existing_run` has: a run that falls takes each next item that sorts before
        // the one before it, a run that rises each next item that does not
        foreach (var (loop, down) in new[] { (falling, true), (rising, false) })
        {
            var test = fn.Label("test");
            var longer = fn.Label("longer");
            fn.Block(loop);
            var r = SortRead("i64", run);
            fn.CondBranch(fn.Value($"icmp ult i64 {r}, %a1"), test, done);
            fn.Block(test);
            var lower = SortBefore(s, SortLoad(s, SortAt(s.Item, "%a0", r)), SortLoad(s, SortAt(s.Item, "%a0", fn.Value($"sub i64 {r}, 1"))));
            fn.CondBranch(lower, down ? longer : done, down ? done : longer);
            fn.Block(longer);
            SortWrite("i64", run, fn.Value($"add i64 {r}, 1"));
            fn.Branch(loop);
        }

        fn.Block(done);
        var length = fn.Value($"insertvalue {{ i64, i1 }} undef, i64 {SortRead("i64", run)}, 0");
        fn.Return($"{{ i64, i1 }} {fn.Value($"insertvalue {{ i64, i1 }} {length}, i1 {falls}, 1")}");
    });

    /// <summary>X8: the helper that reverses the <c>%a1</c> items at <c>%a0</c>, a run that falls, as Rust's <c>reverse</c> does: each item of the first half swapped with its mirror.</summary>
    private string SortReverse(SortOf s) => Helper($"sort.reverse.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var half = fn.Value("lshr i64 %a1, 1");
        var last = fn.Value("sub i64 %a1, 1");
        var low = SortLocal("i64", "low", "0");
        var loop = fn.Label("loop");
        var swap = fn.Label("swap");
        var done = fn.Label("done");
        fn.Block(loop);
        var i = SortRead("i64", low);
        fn.CondBranch(fn.Value($"icmp ult i64 {i}, {half}"), swap, done);
        fn.Block(swap);
        SortSwap(s.Item, "%a0", i, fn.Value($"sub i64 {last}, {i}"));
        SortWrite("i64", low, fn.Value($"add i64 {i}, 1"));
        fn.Branch(loop);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper that gives the address of the median of the items at <c>%a0</c>, <c>%a1</c> and <c>%a2</c>, as Rust's <c>median3</c>
    /// does: the first where it lies between the other two, else the second or the third, by a third comparison.
    /// </summary>
    private string SortMedian(SortOf s) => Helper($"sort.median.{s.Key}", "ptr", ["ptr", "ptr", "ptr"], () =>
    {
        var a = SortLoad(s, "%a0");
        var b = SortLoad(s, "%a1");
        var c = SortLoad(s, "%a2");
        var x = SortBefore(s, a, b);
        var y = SortBefore(s, a, c);
        var outside = fn.Label("outside");
        var between = fn.Label("between");
        fn.CondBranch(fn.Value($"icmp eq i1 {x}, {y}"), outside, between);
        fn.Block(outside);
        var z = SortBefore(s, b, c);
        fn.Return($"ptr {fn.Value($"select i1 {fn.Value($"xor i1 {z}, {x}")}, ptr %a2, ptr %a1")}");
        fn.Block(between);
        fn.Return("ptr %a0");
    });

    /// <summary>
    /// X8: the helper that gives the address of a pseudo-median of the three sections of <c>%a3</c> items at <c>%a0</c>, <c>%a1</c> and
    /// <c>%a2</c>, as Rust's <c>median3_rec</c> does: the median of the three, each section of 8 items or more taken first as the
    /// pseudo-median of its own eighths at 0, 4 and 7.
    /// </summary>
    private string SortMedianOfMedians(SortOf s) => Helper($"sort.median.rec.{s.Key}", "ptr", ["ptr", "ptr", "ptr", "i64"], () =>
    {
        var self = SortMedianOfMedians(s);
        var deeper = fn.Label("deeper");
        var three = fn.Label("three");
        fn.CondBranch(fn.Value($"icmp uge i64 {fn.Value("mul i64 %a3, 8")}, 64"), deeper, three);
        fn.Block(deeper);
        var eighth = fn.Value("udiv i64 %a3, 8");
        var four = fn.Value($"mul i64 {eighth}, 4");
        var seven = fn.Value($"mul i64 {eighth}, 7");
        string Of(string at) => fn.Value($"call ptr {self}(ptr {at}, ptr {SortAt(s.Item, at, four)}, ptr {SortAt(s.Item, at, seven)}, i64 {eighth})");
        var a = Of("%a0");
        var b = Of("%a1");
        var c = Of("%a2");
        fn.Return($"ptr {fn.Value($"call ptr {SortMedian(s)}(ptr {a}, ptr {b}, ptr {c})")}");
        fn.Block(three);
        fn.Return($"ptr {fn.Value($"call ptr {SortMedian(s)}(ptr %a0, ptr %a1, ptr %a2)")}");
    });

    /// <summary>
    /// X8: the helper that gives the position of the pivot of the <c>%a1</c> items at <c>%a0</c>, 8 or more, as Rust's <c>choose_pivot</c>
    /// does: the median of the first item and the items at four and seven eighths of the list, and from 64 items on the pseudo-median of the
    /// three eighths that begin there (<see cref="SortMedianOfMedians"/>).
    /// </summary>
    private string SortPivot(SortOf s) => Helper($"sort.pivot.{s.Key}", "i64", ["ptr", "i64"], () =>
    {
        var eighth = fn.Value("udiv i64 %a1, 8");
        var b = SortAt(s.Item, "%a0", fn.Value($"mul i64 {eighth}, 4"));
        var c = SortAt(s.Item, "%a0", fn.Value($"mul i64 {eighth}, 7"));
        var chosen = fn.Alloca("ptr", "chosen");
        var few = fn.Label("few");
        var many = fn.Label("many");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value("icmp ult i64 %a1, 64"), few, many);
        fn.Block(few);
        SortWrite("ptr", chosen, fn.Value($"call ptr {SortMedian(s)}(ptr %a0, ptr {b}, ptr {c})"));
        fn.Branch(done);
        fn.Block(many);
        SortWrite("ptr", chosen, fn.Value($"call ptr {SortMedianOfMedians(s)}(ptr %a0, ptr {b}, ptr {c}, i64 {eighth})"));
        fn.Branch(done);
        fn.Block(done);
        var bytes = fn.Value($"sub i64 {fn.Value($"ptrtoint ptr {SortRead("ptr", chosen)} to i64")}, {fn.Value("ptrtoint ptr %a0 to i64")}");
        fn.Return($"i64 {fn.Value($"udiv exact i64 {bytes}, {Number(s.Size)}")}");
    });

    /// <summary>
    /// X8: the helper that splits the <c>%a1</c> items at <c>%a0</c> around the pivot at the position <c>%a2</c> and gives the count of the
    /// items before it, as Rust's <c>partition</c> does with <c>partition_lomuto_branchless_cyclic</c>: the pivot swapped to the front, each
    /// item after it read once and counted where it goes before the pivot (where <paramref name="equal"/>, where the pivot does not sort
    /// before it), with no branch on that, the items moved in one cycle through a gap that the first of them leaves, two items to a step
    /// for items of up to 16 bytes, and the pivot swapped to the end of the items before it.
    /// </summary>
    private string SortPartition(SortOf s, bool equal) => Helper($"sort.partition.{(equal ? "equal" : "less")}.{s.Key}", "i64", ["ptr", "i64", "i64"], () =>
    {
        SortSwap(s.Item, "%a0", "0", "%a2");
        var pivot = SortLoad(s, "%a0");
        var items = SortAt(s.Item, "%a0", "1");
        var count = fn.Value("sub i64 %a1, 1");
        string Goes(string item) => equal ? fn.Value($"xor i1 {SortBefore(s, pivot, item)}, true") : SortBefore(s, item, pivot);
        var gapValue = fn.Alloca(s.Item, "gap.value");
        SortStore(s, SortLoad(s, items), gapValue);
        var below = SortLocal("i64", "below", "0");
        var right = SortLocal("ptr", "right", SortAt(s.Item, items, "1"));
        var gap = SortLocal("ptr", "gap", items);
        void Step(string at)
        {
            var item = SortLoad(s, at);
            var goes = Goes(item);
            var taken = SortRead("i64", below);
            var left = SortAt(s.Item, items, taken);
            SortStore(s, SortLoad(s, left), SortRead("ptr", gap));
            SortStore(s, item, left);
            SortWrite("ptr", gap, at);
            SortWrite("i64", below, fn.Value($"add i64 {taken}, {fn.Value($"zext i1 {goes} to i64")}"));
            SortWrite("ptr", right, SortAt(s.Item, at, "1"));
        }

        var unroll = s.Size <= 16 ? 2 : 1;
        var unrollEnd = SortAt(s.Item, items, fn.Value($"sub i64 {count}, {Number(unroll - 1)}"));
        var unrolled = fn.Label("unrolled");
        var steps = fn.Label("steps");
        var rest = fn.Label("rest");
        var done = fn.Label("done");
        fn.Block(unrolled);
        fn.CondBranch(fn.Value($"icmp ult ptr {SortRead("ptr", right)}, {unrollEnd}"), steps, rest);
        fn.Block(steps);
        for (var k = 0; k < unroll; k++)
        {
            Step(SortRead("ptr", right));
        }

        fn.Branch(unrolled);

        // the item left after the steps, if any, and then the first item, read from where it was kept, into the gap
        fn.Block(rest);
        var at = SortRead("ptr", right);
        var last = fn.Value($"icmp eq ptr {at}, {SortAt(s.Item, items, count)}");
        Step(fn.Value($"select i1 {last}, ptr {gapValue}, ptr {at}"));
        fn.CondBranch(last, done, rest);
        fn.Block(done);
        var before = SortRead("i64", below);
        SortSwap(s.Item, "%a0", "0", before);
        fn.Return($"i64 {before}");
    });

    /// <summary>
    /// X8: the helper of <c>SortUnstable</c> and of <c>SortBy</c>'s pairs, which sorts the <c>%a1</c> items at <c>%a0</c> in place, as Rust's
    /// <c>sort_unstable</c> does: 20 items or fewer by insertion (<see cref="SortInsertion"/>), more by ipnsort (<see cref="IpnSort"/>).
    /// </summary>
    private string UnstableSort(SortOf s) => Helper($"sort.unstable.items.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var few = fn.Label("few");
        var many = fn.Label("many");
        fn.CondBranch(fn.Value($"icmp ule i64 %a1, {Number(InsertionOnly)}"), few, many);
        fn.Block(few);
        fn.Emit($"call void {SortInsertion(s)}(ptr %a0, i64 %a1, i64 1)");
        fn.Return(null);
        fn.Block(many);
        fn.Emit($"call void {IpnSort(s)}(ptr %a0, i64 %a1)");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>ipnsort</c> of the <c>%a1</c> items at <c>%a0</c>: a list that is one run is left as it is, or reversed where the run
    /// falls (<see cref="SortRunOf"/>); any other is sorted by the quicksort (<see cref="QuickSort"/>), with twice the base-2 logarithm of
    /// the count as the bound on its splits (<see cref="SortLimit"/>).
    /// </summary>
    private string IpnSort(SortOf s) => Helper($"sort.ipn.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var run = fn.Value($"call {{ i64, i1 }} {SortRunOf(s)}(ptr %a0, i64 %a1)");
        var whole = fn.Label("whole");
        var reverse = fn.Label("reverse");
        var done = fn.Label("done");
        var quick = fn.Label("quick");
        fn.CondBranch(fn.Value($"icmp eq i64 {fn.Value("extractvalue { i64, i1 } " + run + ", 0")}, %a1"), whole, quick);
        fn.Block(whole);
        fn.CondBranch(fn.Value($"extractvalue {{ i64, i1 }} {run}, 1"), reverse, done);
        fn.Block(reverse);
        fn.Emit($"call void {SortReverse(s)}(ptr %a0, i64 %a1)");
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
        fn.Block(quick);
        fn.Emit($"call void {QuickSort(s)}(ptr %a0, i64 %a1, ptr null, i64 {SortLimit("%a1")})");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's unstable <c>quicksort</c> of the <c>%a1</c> items at <c>%a0</c>: up to 32 items by the small sort
    /// (<see cref="SmallSort"/>), and past <c>%a3</c> splits by the heapsort (<see cref="HeapSort"/>), so that the comparisons of no order
    /// of the items grow faster than a constant times n log n; else split around the pivot (<see cref="SortPivot"/>,
    /// <see cref="SortPartition"/>), the items before it sorted by a call and the items after it in the same loop. Where the pivot the split
    /// before left at <c>%a2</c>, before these items, does not sort before the new pivot, the new one is the least of them, and the items
    /// equal to it are split off and left where they are, as Rust's quicksort sorts many equal items.
    /// </summary>
    private string QuickSort(SortOf s) => Helper($"sort.quick.{s.Key}", "void", ["ptr", "i64", "ptr", "i64"], () =>
    {
        var self = QuickSort(s);
        var items = SortLocal("ptr", "items", "%a0");
        var count = SortLocal("i64", "count", "%a1");
        var ancestor = SortLocal("ptr", "ancestor", "%a2");
        var limit = SortLocal("i64", "limit", "%a3");
        var top = fn.Label("top");
        var small = fn.Label("small");
        var large = fn.Label("large");
        var heap = fn.Label("heap");
        var split = fn.Label("split");
        var check = fn.Label("check");
        var equal = fn.Label("equal");
        var less = fn.Label("less");
        fn.Block(top);
        var v = SortRead("ptr", items);
        var n = SortRead("i64", count);
        fn.CondBranch(fn.Value($"icmp ule i64 {n}, {Number(SmallSortMost)}"), small, large);
        fn.Block(small);
        fn.Emit($"call void {SmallSort(s)}(ptr {v}, i64 {n})");
        fn.Return(null);
        fn.Block(large);
        var bound = SortRead("i64", limit);
        fn.CondBranch(fn.Value($"icmp eq i64 {bound}, 0"), heap, split);
        fn.Block(heap);
        fn.Emit($"call void {HeapSort(s)}(ptr {v}, i64 {n})");
        fn.Return(null);
        fn.Block(split);
        var lower = fn.Value($"sub i64 {bound}, 1");
        SortWrite("i64", limit, lower);
        var p = fn.Value($"call i64 {SortPivot(s)}(ptr {v}, i64 {n})");
        var before = SortRead("ptr", ancestor);
        fn.CondBranch(fn.Value($"icmp ne ptr {before}, null"), check, less);
        fn.Block(check);
        fn.CondBranch(SortBefore(s, SortLoad(s, before), SortLoad(s, SortAt(s.Item, v, p))), less, equal);
        fn.Block(equal);
        var equals = fn.Value($"call i64 {SortPartition(s, true)}(ptr {v}, i64 {n}, i64 {p})");
        var past = fn.Value($"add i64 {equals}, 1");
        SortWrite("ptr", items, SortAt(s.Item, v, past));
        SortWrite("i64", count, fn.Value($"sub i64 {n}, {past}"));
        SortWrite("ptr", ancestor, "null");
        fn.Branch(top);
        fn.Block(less);
        var below = fn.Value($"call i64 {SortPartition(s, false)}(ptr {v}, i64 {n}, i64 {p})");
        fn.Emit($"call void {self}(ptr {v}, i64 {below}, ptr {before}, i64 {lower})");
        var next = fn.Value($"add i64 {below}, 1");
        SortWrite("ptr", ancestor, SortAt(s.Item, v, below));
        SortWrite("ptr", items, SortAt(s.Item, v, next));
        SortWrite("i64", count, fn.Value($"sub i64 {n}, {next}"));
        fn.Branch(top);
    });

    /// <summary>
    /// X8: the unstable small sort of the <c>%a1</c> items at <c>%a0</c>, 32 or fewer, as Rust's <c>UnstableSmallSortTypeImpl</c> picks it:
    /// a sorting network for items of up to 8 bytes (<see cref="NetworkSort"/>), and for wider items the general small sort
    /// (<see cref="GeneralSmallSort"/>) through a scratch of 48 items on the stack.
    /// </summary>
    private string SmallSort(SortOf s) => s.Size <= 8 ? NetworkSort(s) : Helper($"sort.small.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var scratch = fn.Alloca($"[{Number(SmallScratch)} x {s.Item}]", "scratch");
        fn.Emit($"call void {GeneralSmallSort(s)}(ptr %a0, i64 %a1, ptr {scratch})");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>small_sort_network</c> of the <c>%a1</c> items at <c>%a0</c>, 32 or fewer: fewer than 18 items as one region, more as
    /// two halves, each region sorted by a network of 13 or 9 items where it holds as many and by insertion after them
    /// (<see cref="NetworkRegion"/>), and the halves merged from both ends into a scratch of 32 items on the stack
    /// (<see cref="BidirectionalMerge"/>) and copied back.
    /// </summary>
    private string NetworkSort(SortOf s) => Helper($"sort.network.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var some = fn.Label("some");
        var merge = fn.Label("merge");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value("icmp ult i64 %a1, 2"), done, some);
        fn.Block(some);
        var half = fn.Value("lshr i64 %a1, 1");
        var alone = fn.Value("icmp ult i64 %a1, 18");
        fn.Emit($"call void {NetworkRegion(s)}(ptr %a0, i64 {fn.Value($"select i1 {alone}, i64 %a1, i64 {half}")})");
        fn.CondBranch(alone, done, merge);
        fn.Block(merge);
        fn.Emit($"call void {NetworkRegion(s)}(ptr {SortAt(s.Item, "%a0", half)}, i64 {fn.Value($"sub i64 %a1, {half}")})");
        var scratch = fn.Alloca($"[{Number(SmallSortMost)} x {s.Item}]", "scratch");
        fn.Emit($"call void {BidirectionalMerge(s)}(ptr %a0, i64 %a1, ptr {scratch})");
        SortCopy(s, "%a0", scratch, "%a1");
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>X8: one region of <see cref="NetworkSort"/>, the <c>%a1</c> items at <c>%a0</c>: its first 13 items sorted by the network of 13 where it holds as many, else its first 9 by the network of 9, and the rest by insertion.</summary>
    private string NetworkRegion(SortOf s) => Helper($"sort.network.region.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var sorted = SortLocal("i64", "sorted", "1");
        var thirteen = fn.Label("thirteen");
        var notThirteen = fn.Label("not.thirteen");
        var nine = fn.Label("nine");
        var insert = fn.Label("insert");
        fn.CondBranch(fn.Value("icmp uge i64 %a1, 13"), thirteen, notThirteen);
        fn.Block(thirteen);
        fn.Emit($"call void {Network(s, 13)}(ptr %a0)");
        SortWrite("i64", sorted, "13");
        fn.Branch(insert);
        fn.Block(notThirteen);
        fn.CondBranch(fn.Value("icmp uge i64 %a1, 9"), nine, insert);
        fn.Block(nine);
        fn.Emit($"call void {Network(s, 9)}(ptr %a0)");
        SortWrite("i64", sorted, "9");
        fn.Branch(insert);
        fn.Block(insert);
        fn.Emit($"call void {SortInsertion(s)}(ptr %a0, i64 %a1, i64 {SortRead("i64", sorted)})");
        fn.Return(null);
    });

    /// <summary>The comparators of Rust's <c>sort9_optimal</c> and <c>sort13_optimal</c>, optimal sorting networks of 9 and 13 items, in their order.</summary>
    private static readonly Dictionary<int, (int A, int B)[]> Networks = new()
    {
        [9] =
        [
            (0, 3), (1, 7), (2, 5), (4, 8), (0, 7), (2, 4), (3, 8), (5, 6), (0, 2), (1, 3), (4, 5), (7, 8), (1, 4), (3, 6), (5, 7), (0, 1),
            (2, 4), (3, 5), (6, 8), (2, 3), (4, 5), (6, 7), (1, 2), (3, 4), (5, 6),
        ],
        [13] =
        [
            (0, 12), (1, 10), (2, 9), (3, 7), (5, 11), (6, 8), (1, 6), (2, 3), (4, 11), (7, 9), (8, 10), (0, 4), (1, 2), (3, 6), (7, 8),
            (9, 10), (11, 12), (4, 6), (5, 9), (8, 11), (10, 12), (0, 5), (3, 8), (4, 7), (6, 11), (9, 10), (0, 1), (2, 5), (6, 9), (7, 8),
            (10, 11), (1, 3), (2, 4), (5, 6), (9, 10), (1, 2), (3, 4), (5, 7), (6, 8), (2, 3), (4, 5), (6, 7), (8, 9), (3, 4), (5, 6),
        ],
    };

    /// <summary>X8: the helper that sorts the first <paramref name="items"/> items at <c>%a0</c> by the network of that many (<see cref="Networks"/>), each comparator Rust's <c>swap_if_less</c>: the two items swapped where the second sorts before the first, with no branch.</summary>
    private string Network(SortOf s, int items) => Helper($"sort.network.{Number(items)}.{s.Key}", "void", ["ptr"], () =>
    {
        foreach (var (a, b) in Networks[items])
        {
            var first = SortAt(s.Item, "%a0", Number(a));
            var second = SortAt(s.Item, "%a0", Number(b));
            var x = SortLoad(s, first);
            var y = SortLoad(s, second);
            var swap = SortBefore(s, y, x);
            SortStore(s, fn.Value($"select i1 {swap}, {s.Move} {y}, {s.Move} {x}"), first);
            SortStore(s, fn.Value($"select i1 {swap}, {s.Move} {x}, {s.Move} {y}"), second);
        }

        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>bidirectional_merge</c>: the two sorted halves of the <c>%a1</c> items at <c>%a0</c>, the first <c>%a1 / 2</c> and the
    /// rest, merged into <c>%a2</c> from both ends at once, each step taking at the front the first of the two front items, the first
    /// half's where neither sorts before the other, and at the back the last of the two back items, the second half's where neither sorts
    /// after the other, with no branch; of an odd count the one item left goes between.
    /// </summary>
    private string BidirectionalMerge(SortOf s) => Helper($"sort.bimerge.{s.Key}", "void", ["ptr", "i64", "ptr"], () =>
    {
        var half = fn.Value("lshr i64 %a1, 1");
        var left = SortLocal("ptr", "left", "%a0");
        var right = SortLocal("ptr", "right", SortAt(s.Item, "%a0", half));
        var output = SortLocal("ptr", "out", "%a2");
        var leftBack = SortLocal("ptr", "left.back", SortAt(s.Item, "%a0", fn.Value($"sub i64 {half}, 1")));
        var rightBack = SortLocal("ptr", "right.back", SortAt(s.Item, "%a0", fn.Value("sub i64 %a1, 1")));
        var outputBack = SortLocal("ptr", "out.back", SortAt(s.Item, "%a2", fn.Value("sub i64 %a1, 1")));
        var step = SortLocal("i64", "step", "0");
        var loop = fn.Label("loop");
        var body = fn.Label("body");
        var odd = fn.Label("odd");
        var last = fn.Label("last");
        var done = fn.Label("done");
        fn.Block(loop);
        var i = SortRead("i64", step);
        fn.CondBranch(fn.Value($"icmp ult i64 {i}, {half}"), body, odd);
        fn.Block(body);
        var l = SortRead("ptr", left);
        var r = SortRead("ptr", right);
        var lv = SortLoad(s, l);
        var rv = SortLoad(s, r);
        var takeLeft = fn.Value($"xor i1 {SortBefore(s, rv, lv)}, true");
        var o = SortRead("ptr", output);
        SortStore(s, fn.Value($"select i1 {takeLeft}, {s.Move} {lv}, {s.Move} {rv}"), o);
        SortWrite("ptr", right, SortStep(s, r, fn.Value($"xor i1 {takeLeft}, true")));
        SortWrite("ptr", left, SortStep(s, l, takeLeft));
        SortWrite("ptr", output, SortAt(s.Item, o, "1"));
        var lb = SortRead("ptr", leftBack);
        var rb = SortRead("ptr", rightBack);
        var lbv = SortLoad(s, lb);
        var rbv = SortLoad(s, rb);
        var takeRight = fn.Value($"xor i1 {SortBefore(s, rbv, lbv)}, true");
        var ob = SortRead("ptr", outputBack);
        SortStore(s, fn.Value($"select i1 {takeRight}, {s.Move} {rbv}, {s.Move} {lbv}"), ob);
        SortWrite("ptr", rightBack, SortStep(s, rb, takeRight, back: true));
        SortWrite("ptr", leftBack, SortStep(s, lb, fn.Value($"xor i1 {takeRight}, true"), back: true));
        SortWrite("ptr", outputBack, SortAt(s.Item, ob, "-1"));
        SortWrite("i64", step, fn.Value($"add i64 {i}, 1"));
        fn.Branch(loop);
        fn.Block(odd);
        fn.CondBranch(fn.Value($"icmp ne i64 {fn.Value("and i64 %a1, 1")}, 0"), last, done);
        fn.Block(last);
        var front = SortRead("ptr", left);
        var firstLeft = fn.Value($"icmp ult ptr {front}, {SortAt(s.Item, SortRead("ptr", leftBack), "1")}");
        var from = fn.Value($"select i1 {firstLeft}, ptr {front}, ptr {SortRead("ptr", right)}");
        SortStore(s, SortLoad(s, from), SortRead("ptr", output));
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>sort4_stable</c>: the 4 items at <c>%a0</c> written sorted to <c>%a1</c> after 5 comparisons, each item copied once,
    /// equal items in their order: two pairs ordered, their least and greatest picked, and the two left between them ordered.
    /// </summary>
    private string SortFour(SortOf s) => Helper($"sort.four.{s.Key}", "void", ["ptr", "ptr"], () =>
    {
        string At(int k) => SortAt(s.Item, "%a0", Number(k));
        string Pick(string choose, string yes, string no) => fn.Value($"select i1 {choose}, ptr {yes}, ptr {no}");
        string Not(string bit) => fn.Value($"xor i1 {bit}, true");
        var c1 = SortBefore(s, SortLoad(s, At(1)), SortLoad(s, At(0)));
        var c2 = SortBefore(s, SortLoad(s, At(3)), SortLoad(s, At(2)));
        var a = SortStep(s, "%a0", c1);
        var b = SortStep(s, "%a0", Not(c1));
        var c = SortStep(s, At(2), c2);
        var d = SortStep(s, At(2), Not(c2));
        var c3 = SortBefore(s, SortLoad(s, c), SortLoad(s, a));
        var c4 = SortBefore(s, SortLoad(s, d), SortLoad(s, b));
        var least = Pick(c3, c, a);
        var greatest = Pick(c4, b, d);
        var unknownLeft = Pick(c3, a, Pick(c4, c, b));
        var unknownRight = Pick(c4, d, Pick(c3, b, c));
        var c5 = SortBefore(s, SortLoad(s, unknownRight), SortLoad(s, unknownLeft));
        var low = Pick(c5, unknownRight, unknownLeft);
        var high = Pick(c5, unknownLeft, unknownRight);
        string[] order = [least, low, high, greatest];
        for (var k = 0; k < 4; k++)
        {
            SortStore(s, SortLoad(s, order[k]), SortAt(s.Item, "%a1", Number(k)));
        }

        fn.Return(null);
    });

    /// <summary>X8: Rust's <c>sort8_stable</c>: the 8 items at <c>%a0</c> sorted as two fours into the scratch at <c>%a2</c> (<see cref="SortFour"/>) and merged into <c>%a1</c> (<see cref="BidirectionalMerge"/>).</summary>
    private string SortEight(SortOf s) => Helper($"sort.eight.{s.Key}", "void", ["ptr", "ptr", "ptr"], () =>
    {
        fn.Emit($"call void {SortFour(s)}(ptr %a0, ptr %a2)");
        fn.Emit($"call void {SortFour(s)}(ptr {SortAt(s.Item, "%a0", "4")}, ptr {SortAt(s.Item, "%a2", "4")})");
        fn.Emit($"call void {BidirectionalMerge(s)}(ptr %a2, i64 8, ptr %a1)");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>small_sort_general_with_scratch</c>, the stable small sort of the <c>%a1</c> items at <c>%a0</c>, 32 or fewer, through
    /// the scratch at <c>%a2</c> of 16 items more: each half sorted in the scratch, its first 8 items by <see cref="SortEight"/> where the
    /// list holds 16 or more items of up to 16 bytes, else its first 4 by <see cref="SortFour"/> where it holds 8 or more, else its first
    /// item copied, and the rest of the half inserted (<see cref="SortTail"/>); then the halves merged back (<see cref="BidirectionalMerge"/>).
    /// </summary>
    private string GeneralSmallSort(SortOf s) => Helper($"sort.general.{s.Key}", "void", ["ptr", "i64", "ptr"], () =>
    {
        var some = fn.Label("some");
        var done = fn.Label("done");
        fn.CondBranch(fn.Value("icmp ult i64 %a1, 2"), done, some);
        fn.Block(some);
        var half = fn.Value("lshr i64 %a1, 1");
        var presorted = SortLocal("i64", "presorted", "1");
        var four = fn.Label("four");
        var fours = fn.Label("fours");
        var one = fn.Label("one");
        var fill = fn.Label("fill");
        if (s.Size <= 16)
        {
            var eights = fn.Label("eights");
            fn.CondBranch(fn.Value("icmp uge i64 %a1, 16"), eights, four);
            fn.Block(eights);
            fn.Emit($"call void {SortEight(s)}(ptr %a0, ptr %a2, ptr {SortAt(s.Item, "%a2", "%a1")})");
            fn.Emit($"call void {SortEight(s)}(ptr {SortAt(s.Item, "%a0", half)}, ptr {SortAt(s.Item, "%a2", half)}, ptr {SortAt(s.Item, "%a2", fn.Value("add i64 %a1, 8"))})");
            SortWrite("i64", presorted, "8");
            fn.Branch(fill);
        }

        fn.Block(four);
        fn.CondBranch(fn.Value("icmp uge i64 %a1, 8"), fours, one);
        fn.Block(fours);
        fn.Emit($"call void {SortFour(s)}(ptr %a0, ptr %a2)");
        fn.Emit($"call void {SortFour(s)}(ptr {SortAt(s.Item, "%a0", half)}, ptr {SortAt(s.Item, "%a2", half)})");
        SortWrite("i64", presorted, "4");
        fn.Branch(fill);
        fn.Block(one);
        SortStore(s, SortLoad(s, "%a0"), "%a2");
        SortStore(s, SortLoad(s, SortAt(s.Item, "%a0", half)), SortAt(s.Item, "%a2", half));
        fn.Branch(fill);
        fn.Block(fill);
        var start = SortRead("i64", presorted);
        foreach (var (offset, desired) in new[] { ("0", half), (half, fn.Value($"sub i64 %a1, {half}")) })
        {
            var source = SortAt(s.Item, "%a0", offset);
            var target = SortAt(s.Item, "%a2", offset);
            var at = SortLocal("i64", "at", start);
            var loop = fn.Label("loop");
            var insert = fn.Label("insert");
            var next = fn.Label("next");
            fn.Block(loop);
            var i = SortRead("i64", at);
            fn.CondBranch(fn.Value($"icmp ult i64 {i}, {desired}"), insert, next);
            fn.Block(insert);
            var tail = SortAt(s.Item, target, i);
            SortStore(s, SortLoad(s, SortAt(s.Item, source, i)), tail);
            fn.Emit($"call void {SortTail(s)}(ptr {target}, ptr {tail})");
            SortWrite("i64", at, fn.Value($"add i64 {i}, 1"));
            fn.Branch(loop);
            fn.Block(next);
        }

        fn.Emit($"call void {BidirectionalMerge(s)}(ptr %a2, i64 %a1, ptr %a0)");
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: the helper of <c>Sort</c>, which sorts the <c>%a1</c> items at <c>%a0</c> and keeps equal items in their order, as Rust's stable
    /// <c>sort</c> does: 20 items or fewer by insertion (<see cref="SortInsertion"/>), more by driftsort (<see cref="DriftsortMain"/>).
    /// </summary>
    private string StableSort(SortOf s) => Helper($"sort.stable.items.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var few = fn.Label("few");
        var many = fn.Label("many");
        fn.CondBranch(fn.Value($"icmp ule i64 %a1, {Number(InsertionOnly)}"), few, many);
        fn.Block(few);
        fn.Emit($"call void {SortInsertion(s)}(ptr %a0, i64 %a1, i64 1)");
        fn.Return(null);
        fn.Block(many);
        fn.Emit($"call void {DriftsortMain(s)}(ptr %a0, i64 %a1)");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>driftsort_main</c> for the <c>%a1</c> items at <c>%a0</c>: a scratch of every item up to 8,000,000 bytes of them and of
    /// half the items past that, and of 48 items at the least, taken from 4096 bytes on the stack where it fits them and from the allocator
    /// else, whose refusal raises <c>allocation-failed</c> (M9); and the merges found eagerly, by the small sort alone, up to 64 items
    /// (<see cref="Drift"/>).
    /// </summary>
    private string DriftsortMain(SortOf s) => Helper($"sort.drift.main.{s.Key}", "void", ["ptr", "i64"], () =>
    {
        var whole = SortMin("%a1", Number(FullScratchBytes / s.Size));
        var asked = SortMax(SortMax(fn.Value("lshr i64 %a1, 1"), whole), Number(SmallScratch));
        var onStack = Number(StackScratchBytes / s.Size);
        var eager = fn.Value($"icmp ule i64 %a1, {Number(2 * SmallSortMost)}");
        var area = fn.Alloca($"[{Number(StackScratchBytes)} x i8], align {Number(s.Align)}", "stack.scratch");
        var stack = fn.Label("stack");
        var heap = fn.Label("heap");
        fn.CondBranch(fn.Value($"icmp ule i64 {asked}, {onStack}"), stack, heap);
        fn.Block(stack);
        fn.Emit($"call void {Drift(s)}(ptr %a0, i64 %a1, ptr {area}, i64 {onStack}, i1 {eager})");
        fn.Return(null);
        fn.Block(heap);
        var bytes = fn.Value($"mul i64 {asked}, {Number(s.Size)}");
        var buffer = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {bytes})");
        fn.Emit($"call void {Drift(s)}(ptr %a0, i64 %a1, ptr {buffer}, i64 {asked}, i1 {eager})");
        fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {bytes})");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>drift::sort</c> of the <c>%a1</c> items at <c>%a0</c> through the scratch at <c>%a2</c> of <c>%a3</c> items, eagerly
    /// where <c>%a4</c>: the list read as runs (<see cref="CreateRun"/>), and each run pushed on a stack of 66 runs after the runs on it are
    /// merged that powersort puts deeper in the merge tree than the boundary of the new run (<see cref="LogicalMerge"/>); a run is a length
    /// shifted left by one, with its lowest bit set where it is sorted. A list that is still unsorted at the end is sorted by the stable
    /// quicksort (<see cref="StableQuicksort"/>).
    /// </summary>
    private string Drift(SortOf s) => Helper($"sort.drift.{s.Key}", "void", ["ptr", "i64", "ptr", "i64", "i1"], () =>
    {
        module.Declare("declare i64 @llvm.ctlz.i64(i64, i1)");
        var few = fn.Label("few");
        var go = fn.Label("go");
        fn.CondBranch(fn.Value("icmp ult i64 %a1, 2"), few, go);
        fn.Block(few);
        fn.Return(null);
        fn.Block(go);

        // `merge_tree_scale_factor`, and the least length of a run found: `sqrt_approx` of the count past 4096 items, else up to 64
        var scale = fn.Value($"udiv i64 {fn.Value("add i64 4611686018427387903, %a1")}, %a1");
        var log = fn.Value($"sub i64 63, {fn.Value("call i64 @llvm.ctlz.i64(i64 " + fn.Value("or i64 %a1, 1") + ", i1 true)")}");
        var shift = fn.Value($"lshr i64 {fn.Value($"add i64 {log}, 1")}, 1");
        var root = fn.Value($"lshr i64 {fn.Value($"add i64 {fn.Value($"shl i64 1, {shift}")}, {fn.Value($"lshr i64 %a1, {shift}")}")}, 1");
        var least = SortMin(fn.Value($"sub i64 %a1, {fn.Value("lshr i64 %a1, 1")}"), "64");
        var good = fn.Value($"select i1 {fn.Value("icmp ule i64 %a1, 4096")}, i64 {least}, i64 {root}");
        var runs = fn.Alloca("[66 x i64]", "runs");
        var depths = fn.Alloca("[66 x i8]", "depths");
        var stacked = SortLocal("i64", "stacked", "0");
        var scan = SortLocal("i64", "scan", "0");
        var previous = SortLocal("i64", "previous", "1");
        var nextRun = fn.Alloca("i64", "next.run");
        var depth = fn.Alloca("i8", "depth");
        var loop = fn.Label("loop");
        var make = fn.Label("make");
        var final = fn.Label("final");
        var collapse = fn.Label("collapse");
        var check = fn.Label("check");
        var merge = fn.Label("merge");
        var push = fn.Label("push");
        var advance = fn.Label("advance");
        var end = fn.Label("end");
        var quick = fn.Label("quick");
        var done = fn.Label("done");
        fn.Block(loop);
        var at = SortRead("i64", scan);
        var prior = SortRead("i64", previous);
        fn.CondBranch(fn.Value($"icmp ult i64 {at}, %a1"), make, final);
        fn.Block(make);
        var made = fn.Value($"call i64 {CreateRun(s)}(ptr {SortAt(s.Item, "%a0", at)}, i64 {fn.Value($"sub i64 %a1, {at}")}, ptr %a2, i64 {good}, i1 %a4)");
        SortWrite("i64", nextRun, made);

        // `merge_tree_depth`: the leading zeros of the scaled midpoints of the two runs, told apart
        var x = fn.Value($"mul i64 {scale}, {fn.Value($"add i64 {fn.Value($"sub i64 {at}, {fn.Value($"lshr i64 {prior}, 1")}")}, {at}")}");
        var y = fn.Value($"mul i64 {scale}, {fn.Value($"add i64 {at}, {fn.Value($"add i64 {at}, {fn.Value($"lshr i64 {made}, 1")}")}")}");
        var zeros = fn.Value($"call i64 @llvm.ctlz.i64(i64 {fn.Value($"xor i64 {x}, {y}")}, i1 false)");
        SortWrite("i8", depth, fn.Value($"trunc i64 {zeros} to i8"));
        fn.Branch(collapse);
        fn.Block(final);
        SortWrite("i64", nextRun, "1");
        SortWrite("i8", depth, "0");
        fn.Branch(collapse);
        fn.Block(collapse);
        var count = SortRead("i64", stacked);
        fn.CondBranch(fn.Value($"icmp ugt i64 {count}, 1"), check, push);
        fn.Block(check);
        var top = fn.Value($"sub i64 {count}, 1");
        var deeper = fn.Value($"icmp uge i8 {fn.Value($"load i8, ptr {SortAt("i8", depths, top)}")}, {SortRead("i8", depth)}");
        fn.CondBranch(deeper, merge, push);
        fn.Block(merge);
        var left = fn.Value($"load i64, ptr {SortAt("i64", runs, top)}");
        var right = SortRead("i64", previous);
        var length = fn.Value($"add i64 {fn.Value($"lshr i64 {left}, 1")}, {fn.Value($"lshr i64 {right}, 1")}");
        var start = SortAt(s.Item, "%a0", fn.Value($"sub i64 {at}, {length}"));
        SortWrite("i64", previous, fn.Value($"call i64 {LogicalMerge(s)}(ptr {start}, i64 {length}, ptr %a2, i64 %a3, i64 {left}, i64 {right})"));
        SortWrite("i64", stacked, top);
        fn.Branch(collapse);
        fn.Block(push);
        var place = SortRead("i64", stacked);
        fn.Emit($"store i64 {SortRead("i64", previous)}, ptr {SortAt("i64", runs, place)}");
        fn.Emit($"store i8 {SortRead("i8", depth)}, ptr {SortAt("i8", depths, place)}");
        SortWrite("i64", stacked, fn.Value($"add i64 {place}, 1"));
        fn.CondBranch(fn.Value($"icmp uge i64 {at}, %a1"), end, advance);
        fn.Block(advance);
        var following = SortRead("i64", nextRun);
        SortWrite("i64", scan, fn.Value($"add i64 {at}, {fn.Value($"lshr i64 {following}, 1")}"));
        SortWrite("i64", previous, following);
        fn.Branch(loop);
        fn.Block(end);
        fn.CondBranch(fn.Value($"trunc i64 {SortRead("i64", previous)} to i1"), done, quick);
        fn.Block(quick);
        fn.Emit($"call void {StableQuicksort(s)}(ptr %a0, i64 %a1, ptr %a2, i64 %a3)");
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's <c>create_run</c>: the run the <c>%a1</c> items at <c>%a0</c> begin with, sorted, where it holds <c>%a3</c> items or more
    /// (<see cref="SortRunOf"/>), reversed where it falls; else, eagerly where <c>%a4</c>, the first 32 items or fewer sorted by the stable
    /// small sort through the scratch at <c>%a2</c> (<see cref="GeneralSmallSort"/>), which Rust reaches through its quicksort, and lazily
    /// the first <c>%a3</c> items or fewer as a run not sorted yet.
    /// </summary>
    private string CreateRun(SortOf s) => Helper($"sort.drift.run.{s.Key}", "i64", ["ptr", "i64", "ptr", "i64", "i1"], () =>
    {
        var find = fn.Label("find");
        var found = fn.Label("found");
        var reverse = fn.Label("reverse");
        var sorted = fn.Label("sorted");
        var none = fn.Label("none");
        var eager = fn.Label("eager");
        var lazy = fn.Label("lazy");
        fn.CondBranch(fn.Value("icmp uge i64 %a1, %a3"), find, none);
        fn.Block(find);
        var run = fn.Value($"call {{ i64, i1 }} {SortRunOf(s)}(ptr %a0, i64 %a1)");
        var length = fn.Value($"extractvalue {{ i64, i1 }} {run}, 0");
        fn.CondBranch(fn.Value($"icmp uge i64 {length}, %a3"), found, none);
        fn.Block(found);
        fn.CondBranch(fn.Value($"extractvalue {{ i64, i1 }} {run}, 1"), reverse, sorted);
        fn.Block(reverse);
        fn.Emit($"call void {SortReverse(s)}(ptr %a0, i64 {length})");
        fn.Branch(sorted);
        fn.Block(sorted);
        fn.Return($"i64 {fn.Value($"or i64 {fn.Value($"shl i64 {length}, 1")}, 1")}");
        fn.Block(none);
        fn.CondBranch("%a4", eager, lazy);
        fn.Block(eager);
        var first = SortMin("%a1", Number(SmallSortMost));
        fn.Emit($"call void {GeneralSmallSort(s)}(ptr %a0, i64 {first}, ptr %a2)");
        fn.Return($"i64 {fn.Value($"or i64 {fn.Value($"shl i64 {first}, 1")}, 1")}");
        fn.Block(lazy);
        fn.Return($"i64 {fn.Value($"shl i64 {SortMin("%a3", "%a1")}, 1")}");
    });

    /// <summary>
    /// X8: Rust's <c>logical_merge</c> of the runs <c>%a4</c> and <c>%a5</c>, the <c>%a1</c> items at <c>%a0</c>: where either is sorted, or
    /// the two do not fit the scratch at <c>%a2</c> of <c>%a3</c> items, each unsorted one sorted by the stable quicksort
    /// (<see cref="StableQuicksort"/>) and the two merged (<see cref="StableMerge"/>) into one sorted run; else one run not sorted yet.
    /// </summary>
    private string LogicalMerge(SortOf s) => Helper($"sort.drift.merge.{s.Key}", "i64", ["ptr", "i64", "ptr", "i64", "i64", "i64"], () =>
    {
        var leftSorted = fn.Value("trunc i64 %a4 to i1");
        var rightSorted = fn.Value("trunc i64 %a5 to i1");
        var tooLong = fn.Value("icmp ugt i64 %a1, %a3");
        var physical = fn.Label("physical");
        var sortLeft = fn.Label("sort.left");
        var checkRight = fn.Label("check.right");
        var sortRight = fn.Label("sort.right");
        var merge = fn.Label("merge");
        var lazy = fn.Label("lazy");
        fn.CondBranch(fn.Value($"or i1 {tooLong}, {fn.Value($"or i1 {leftSorted}, {rightSorted}")}"), physical, lazy);
        fn.Block(physical);
        var leftLength = fn.Value("lshr i64 %a4, 1");
        fn.CondBranch(leftSorted, checkRight, sortLeft);
        fn.Block(sortLeft);
        fn.Emit($"call void {StableQuicksort(s)}(ptr %a0, i64 {leftLength}, ptr %a2, i64 %a3)");
        fn.Branch(checkRight);
        fn.Block(checkRight);
        fn.CondBranch(rightSorted, merge, sortRight);
        fn.Block(sortRight);
        fn.Emit($"call void {StableQuicksort(s)}(ptr {SortAt(s.Item, "%a0", leftLength)}, i64 {fn.Value($"sub i64 %a1, {leftLength}")}, ptr %a2, i64 %a3)");
        fn.Branch(merge);
        fn.Block(merge);
        fn.Emit($"call void {StableMerge(s)}(ptr %a0, i64 %a1, ptr %a2, i64 %a3, i64 {leftLength})");
        fn.Return($"i64 {fn.Value("or i64 " + fn.Value("shl i64 %a1, 1") + ", 1")}");
        fn.Block(lazy);
        fn.Return($"i64 {fn.Value("shl i64 %a1, 1")}");
    });

    /// <summary>X8: Rust's <c>stable_quicksort</c>: the stable quicksort of the <c>%a1</c> items at <c>%a0</c> through the scratch at <c>%a2</c> of <c>%a3</c> items, with twice the base-2 logarithm of the count as the bound on its splits (<see cref="StableQuick"/>).</summary>
    private string StableQuicksort(SortOf s) => Helper($"sort.stable.quicksort.{s.Key}", "void", ["ptr", "i64", "ptr", "i64"], () =>
    {
        fn.Emit($"call void {StableQuick(s)}(ptr %a0, i64 %a1, ptr %a2, i64 %a3, i64 {SortLimit("%a1")}, ptr null)");
        fn.Return(null);
    });

    /// <summary>
    /// X8: Rust's stable <c>quicksort</c> of the <c>%a1</c> items at <c>%a0</c> through the scratch at <c>%a2</c> of <c>%a3</c> items: up to
    /// 32 items by the stable small sort (<see cref="GeneralSmallSort"/>), and past <c>%a4</c> splits by driftsort's eager form
    /// (<see cref="Drift"/>), so that no order of the items takes more than a constant times n log n comparisons; else split around a copy
    /// of the pivot (<see cref="SortPivot"/>, <see cref="StablePartition"/>), keeping the order of the items on each side, the items after
    /// it sorted by a call that takes the copy as the pivot before them and the items before it in the same loop. Where the pivot before
    /// these items, at <c>%a5</c>, does not sort before the new pivot, or no item sorts before the new pivot, the items equal to it are split
    /// off to the front and left where they are, as Rust's quicksort sorts many equal items.
    /// </summary>
    private string StableQuick(SortOf s) => Helper($"sort.stable.quick.{s.Key}", "void", ["ptr", "i64", "ptr", "i64", "i64", "ptr"], () =>
    {
        var self = StableQuick(s);
        var items = SortLocal("ptr", "items", "%a0");
        var count = SortLocal("i64", "count", "%a1");
        var limit = SortLocal("i64", "limit", "%a4");
        var ancestor = SortLocal("ptr", "ancestor", "%a5");
        var pivotCopy = fn.Alloca(s.Item, "pivot");
        var top = fn.Label("top");
        var small = fn.Label("small");
        var large = fn.Label("large");
        var drift = fn.Label("drift");
        var split = fn.Label("split");
        var check = fn.Label("check");
        var less = fn.Label("less");
        var equal = fn.Label("equal");
        var recurse = fn.Label("recurse");
        fn.Block(top);
        var v = SortRead("ptr", items);
        var n = SortRead("i64", count);
        fn.CondBranch(fn.Value($"icmp ule i64 {n}, {Number(SmallSortMost)}"), small, large);
        fn.Block(small);
        fn.Emit($"call void {GeneralSmallSort(s)}(ptr {v}, i64 {n}, ptr %a2)");
        fn.Return(null);
        fn.Block(large);
        var bound = SortRead("i64", limit);
        fn.CondBranch(fn.Value($"icmp eq i64 {bound}, 0"), drift, split);
        fn.Block(drift);
        fn.Emit($"call void {Drift(s)}(ptr {v}, i64 {n}, ptr %a2, i64 %a3, i1 true)");
        fn.Return(null);
        fn.Block(split);
        var lower = fn.Value($"sub i64 {bound}, 1");
        SortWrite("i64", limit, lower);
        var p = fn.Value($"call i64 {SortPivot(s)}(ptr {v}, i64 {n})");
        var pivot = SortLoad(s, SortAt(s.Item, v, p));
        SortStore(s, pivot, pivotCopy);
        var before = SortRead("ptr", ancestor);
        fn.CondBranch(fn.Value($"icmp ne ptr {before}, null"), check, less);
        fn.Block(check);
        fn.CondBranch(SortBefore(s, SortLoad(s, before), pivot), less, equal);
        fn.Block(less);
        var below = fn.Value($"call i64 {StablePartition(s, false)}(ptr {v}, i64 {n}, ptr %a2, i64 {p}, i1 false)");
        fn.CondBranch(fn.Value($"icmp eq i64 {below}, 0"), equal, recurse);
        fn.Block(equal);
        var equals = fn.Value($"call i64 {StablePartition(s, true)}(ptr {v}, i64 {n}, ptr %a2, i64 {p}, i1 true)");
        SortWrite("ptr", items, SortAt(s.Item, v, equals));
        SortWrite("i64", count, fn.Value($"sub i64 {n}, {equals}"));
        SortWrite("ptr", ancestor, "null");
        fn.Branch(top);
        fn.Block(recurse);
        fn.Emit($"call void {self}(ptr {SortAt(s.Item, v, below)}, i64 {fn.Value($"sub i64 {n}, {below}")}, ptr %a2, i64 %a3, i64 {lower}, ptr {pivotCopy})");
        SortWrite("i64", count, below);
        fn.Branch(top);
    });

    /// <summary>
    /// X8: Rust's <c>stable_partition</c> of the <c>%a1</c> items at <c>%a0</c> around the pivot at the position <c>%a3</c>, through the
    /// scratch at <c>%a2</c>: each item read in order and written to the front of the scratch where it goes before the pivot (where it sorts
    /// before it, or, where <paramref name="equal"/>, where the pivot does not sort before it), else to the back of the scratch from the end
    /// down, four items to a step for items of up to 16 bytes, with no branch on where an item goes; the pivot itself goes to the front where
    /// <c>%a4</c>. Then the front copied back, and after it the back in reverse, so that each side keeps its order. Gives the count of the
    /// front.
    /// </summary>
    private string StablePartition(SortOf s, bool equal) => Helper($"sort.stable.partition.{(equal ? "equal" : "less")}.{s.Key}", "i64", ["ptr", "i64", "ptr", "i64", "i1"], () =>
    {
        var pivot = SortLoad(s, SortAt(s.Item, "%a0", "%a3"));
        var scan = SortLocal("ptr", "scan", "%a0");
        var front = SortLocal("i64", "front", "0");
        var back = SortLocal("ptr", "back", SortAt(s.Item, "%a2", "%a1"));
        var endAt = SortLocal("i64", "end.at", "%a3");
        void One(Func<string, string> goesFront)
        {
            var from = SortRead("ptr", scan);
            var item = SortLoad(s, from);
            var toFront = goesFront(item);
            var lowered = SortAt(s.Item, SortRead("ptr", back), "-1");
            SortWrite("ptr", back, lowered);
            var taken = SortRead("i64", front);
            var target = SortAt(s.Item, fn.Value($"select i1 {toFront}, ptr %a2, ptr {lowered}"), taken);
            SortStore(s, item, target);
            SortWrite("i64", front, fn.Value($"add i64 {taken}, {fn.Value($"zext i1 {toFront} to i64")}"));
            SortWrite("ptr", scan, SortAt(s.Item, from, "1"));
        }

        string Goes(string item) => equal ? fn.Value($"xor i1 {SortBefore(s, pivot, item)}, true") : SortBefore(s, item, pivot);
        var loop = fn.Label("loop");
        var plain = fn.Label("plain");
        var plainBody = fn.Label("plain.body");
        var after = fn.Label("after");
        var pivotStep = fn.Label("pivot");
        var output = fn.Label("out");
        fn.Block(loop);
        var endPosition = SortRead("i64", endAt);
        if (s.Size <= 16)
        {
            var unrolledEnd = SortAt(s.Item, "%a0", fn.Value($"select i1 {fn.Value($"icmp ugt i64 {endPosition}, 3")}, i64 {fn.Value($"sub i64 {endPosition}, 3")}, i64 0"));
            var unrolled = fn.Label("unrolled");
            var unrolledBody = fn.Label("unrolled.body");
            fn.Block(unrolled);
            fn.CondBranch(fn.Value($"icmp ult ptr {SortRead("ptr", scan)}, {unrolledEnd}"), unrolledBody, plain);
            fn.Block(unrolledBody);
            for (var k = 0; k < 4; k++)
            {
                One(Goes);
            }

            fn.Branch(unrolled);
        }

        fn.Block(plain);
        fn.CondBranch(fn.Value($"icmp ult ptr {SortRead("ptr", scan)}, {SortAt(s.Item, "%a0", endPosition)}"), plainBody, after);
        fn.Block(plainBody);
        One(Goes);
        fn.Branch(plain);
        fn.Block(after);
        fn.CondBranch(fn.Value($"icmp eq i64 {endPosition}, %a1"), output, pivotStep);
        fn.Block(pivotStep);
        One(_ => "%a4");
        SortWrite("i64", endAt, "%a1");
        fn.Branch(loop);
        fn.Block(output);
        var count = SortRead("i64", front);
        SortCopy(s, "%a0", "%a2", count);
        var at = SortLocal("i64", "at", "0");
        var rest = fn.Value($"sub i64 %a1, {count}");
        var copy = fn.Label("copy");
        var each = fn.Label("each");
        var done = fn.Label("done");
        fn.Block(copy);
        var i = SortRead("i64", at);
        fn.CondBranch(fn.Value($"icmp ult i64 {i}, {rest}"), each, done);
        fn.Block(each);
        var source = SortAt(s.Item, "%a2", fn.Value($"sub i64 {fn.Value("sub i64 %a1, 1")}, {i}"));
        SortStore(s, SortLoad(s, source), SortAt(s.Item, "%a0", fn.Value($"add i64 {count}, {i}")));
        SortWrite("i64", at, fn.Value($"add i64 {i}, 1"));
        fn.Branch(copy);
        fn.Block(done);
        fn.Return($"i64 {count}");
    });

    /// <summary>
    /// X8: Rust's stable <c>merge</c> of the two sorted runs of the <c>%a1</c> items at <c>%a0</c>, the first <c>%a4</c> and the rest, through
    /// the scratch at <c>%a2</c> of <c>%a3</c> items: the shorter run copied into the scratch and merged with the other, from the front where
    /// it is the first run and from the back where it is the second, an item of the second run going before one of the first only where it
    /// sorts before it, with no branch on which goes; what is left in the scratch goes where the merge stopped. Nothing where a run is empty
    /// or the shorter one does not fit the scratch.
    /// </summary>
    private string StableMerge(SortOf s) => Helper($"sort.merge.{s.Key}", "void", ["ptr", "i64", "ptr", "i64", "i64"], () =>
    {
        var rightLength = fn.Value("sub i64 %a1, %a4");
        var skip = fn.Value($"or i1 {fn.Value("icmp eq i64 %a4, 0")}, {fn.Value($"or i1 {fn.Value("icmp uge i64 %a4, %a1")}, {fn.Value($"icmp ult i64 %a3, {SortMin("%a4", rightLength)}")}")}");
        var go = fn.Label("go");
        var up = fn.Label("up");
        var upLoop = fn.Label("up.loop");
        var upBody = fn.Label("up.body");
        var down = fn.Label("down");
        var downLoop = fn.Label("down.loop");
        var finish = fn.Label("finish");
        var done = fn.Label("done");
        fn.CondBranch(skip, done, go);
        fn.Block(go);
        var leftShorter = fn.Value($"icmp ule i64 %a4, {rightLength}");
        var middle = SortAt(s.Item, "%a0", "%a4");
        var save = fn.Value($"select i1 {leftShorter}, ptr %a0, ptr {middle}");
        var saved = fn.Value($"select i1 {leftShorter}, i64 %a4, i64 {rightLength}");
        SortCopy(s, "%a2", save, saved);
        var start = SortLocal("ptr", "start", "%a2");
        var end = SortLocal("ptr", "end", SortAt(s.Item, "%a2", saved));
        var target = SortLocal("ptr", "target", save);
        fn.CondBranch(leftShorter, up, down);

        // the first run from the scratch and the second in place, merged from the front
        fn.Block(up);
        var right = SortLocal("ptr", "right", middle);
        var rightEnd = SortAt(s.Item, "%a0", "%a1");
        fn.Block(upLoop);
        var st = SortRead("ptr", start);
        var r = SortRead("ptr", right);
        var more = fn.Value($"and i1 {fn.Value($"icmp ne ptr {st}, {SortRead("ptr", end)}")}, {fn.Value($"icmp ne ptr {r}, {rightEnd}")}");
        fn.CondBranch(more, upBody, finish);
        fn.Block(upBody);
        var rv = SortLoad(s, r);
        var sv = SortLoad(s, st);
        var takeLeft = fn.Value($"xor i1 {SortBefore(s, rv, sv)}, true");
        var t = SortRead("ptr", target);
        SortStore(s, fn.Value($"select i1 {takeLeft}, {s.Move} {sv}, {s.Move} {rv}"), t);
        SortWrite("ptr", start, SortStep(s, st, takeLeft));
        SortWrite("ptr", right, SortStep(s, r, fn.Value($"xor i1 {takeLeft}, true")));
        SortWrite("ptr", target, SortAt(s.Item, t, "1"));
        fn.Branch(upLoop);

        // the second run from the scratch and the first in place, merged from the back
        fn.Block(down);
        var output = SortLocal("ptr", "out", SortAt(s.Item, "%a0", "%a1"));
        fn.Block(downLoop);
        var leftAt = SortAt(s.Item, SortRead("ptr", target), "-1");
        var rightAt = SortAt(s.Item, SortRead("ptr", end), "-1");
        var o = SortAt(s.Item, SortRead("ptr", output), "-1");
        SortWrite("ptr", output, o);
        var lv = SortLoad(s, leftAt);
        var bv = SortLoad(s, rightAt);
        var takeFirst = SortBefore(s, bv, lv);
        SortStore(s, fn.Value($"select i1 {takeFirst}, {s.Move} {lv}, {s.Move} {bv}"), o);
        var newTarget = SortStep(s, leftAt, fn.Value($"xor i1 {takeFirst}, true"));
        var newEnd = SortStep(s, rightAt, takeFirst);
        SortWrite("ptr", target, newTarget);
        SortWrite("ptr", end, newEnd);
        var stop = fn.Value($"or i1 {fn.Value($"icmp eq ptr {newTarget}, %a0")}, {fn.Value($"icmp eq ptr {newEnd}, %a2")}");
        fn.CondBranch(stop, finish, downLoop);

        // what is left in the scratch, where the merge stopped
        fn.Block(finish);
        var from = SortRead("ptr", start);
        var bytes = fn.Value($"sub i64 {fn.Value($"ptrtoint ptr {SortRead("ptr", end)} to i64")}, {fn.Value($"ptrtoint ptr {from} to i64")}");
        SortCopyBytes(SortRead("ptr", target), from, bytes);
        fn.Return(null);
        fn.Block(done);
        fn.Return(null);
    });

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
