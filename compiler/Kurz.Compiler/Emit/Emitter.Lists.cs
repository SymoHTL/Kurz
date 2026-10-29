using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Lists (M9) and the counting of their blocks and of the blocks of strings (design record, section 3: compiler-inserted reference
/// counting; the owner, 2026-10-24, "Full counting now", and 2026-10-29, "Bit in the count"). A list value is the address of its
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
/// environment of its lambda (<see cref="EmitLambda"/>), and so is a file, by the block of its handle, which closes it at its last count
/// (S5), and values of every other type are not counted.
/// </summary>
public sealed partial class Emitter
{
    // the temporaries of each region being emitted, the innermost last, and the counted variables of each block, the innermost last
    private List<List<(string Value, KurzType Type)>> regions = [];
    private List<List<(string Slot, KurzType Type)>> scopes = [];

    /// <summary>Whether a value of the type holds the block of a list, of a string or of a file's handle or the environment of a lambda: a list, a string, a file, a function value, a plain data value with a field that does, a union with a case that does.</summary>
    private bool Counted(KurzType type) => type switch
    {
        ListType or FunctionType or HandleType or PrimitiveType { Kind: Primitive.String } => true,
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
        if (type is ListType)
        {
            fn.Emit($"call void {StdFunction("ListRetain")}(ptr {value})");
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
                throw new InvalidOperationException($"a `{type}` holds no list");
        }
    }

    // ---- the members of a list (M9, T14) -------------------------------------------------------------

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

        var list = EmitExpression(index.Target);
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
    /// M9: <c>list.Add(item)</c> on a <c>mut</c> variable or parameter. The item is read first, then the list the variable holds as
    /// the item left it, as a path assignment reads its value first (M4); a block other values hold is copied before the write (M3),
    /// and a full one grows. What the argument made besides the item is let go of before the list is read, so that the environment
    /// of a lambda it gave to a call, which counts the values it read (F15), holds no count of the list when the list grows, as
    /// M7 states (section 4, the owner, 2026-10-28, "Pass, as Rust").
    /// </summary>
    private void EmitListAdd(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        if (Unwrap(member.Target) is not NameExpression n)
        {
            throw Unsupported(c.Line, "an `Add` on a list in a field (M5)");
        }

        var argument = c.Arguments[0].Value;
        string item = null!;
        InRegion(() => item = Convert(Owned(argument), TypeOf(argument), list.Element, argument.Line));
        var place = Place(Model.VariableOf(n), n.Line);
        var add = Helper($"add.{Lower.Mangle(list)}", "void", ["ptr", Ir(list.Element)], () => AddBody(list.Element));
        fn.Emit($"call void {add}(ptr {place}, {Ir(list.Element)} {item})");
    }

    /// <summary>The helper of <c>Add</c>: the place of the list in <c>%a0</c>, the item, which the list takes over, in <c>%a1</c>.</summary>
    private void AddBody(KurzType element)
    {
        var size = ItemSize(element);
        var asked = fn.Alloca("i64", "asked");
        var block = fn.Value("load ptr, ptr %a0");
        var push = fn.Label("push");
        var reserve = fn.Label("reserve");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("ListHasRoom")}(ptr {block})"), push, reserve); // room in a block no other value holds
        fn.Block(reserve);
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
        fn.Branch(push);
        fn.Block(grow);
        var grown = fn.Value($"call ptr {StdFunction("ListGrow")}(ptr {block}, i64 {size}, ptr {asked})");
        var store = fn.Label("grown");
        fn.CondBranch(fn.Value($"icmp eq ptr {grown}, null"), failed, store);
        fn.Block(store);
        fn.Emit($"store ptr {grown}, ptr %a0");
        fn.Branch(push);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
        fn.Block(push);
        var slot = fn.Value($"call ptr {StdFunction("ListPush")}(ptr {fn.Value("load ptr, ptr %a0")}, i64 {size})");
        fn.Emit($"store {Ir(element)} %a1, ptr {slot}");
        fn.Return(null);
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

                var add = Helper($"add.{Lower.Mangle(list)}", "void", ["ptr", element], () => AddBody(list.Element));
                fn.Emit($"call void {add}(ptr {kept}, {element} {item})");
                fn.Block(next);
            });
            fn.Return($"ptr {fn.Value($"load ptr, ptr {kept}")}");
        });
        return fn.Value($"call ptr {name}(ptr {source}, {test})");
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
