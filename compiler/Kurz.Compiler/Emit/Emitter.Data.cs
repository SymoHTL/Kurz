using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- data values (D1 to D11, D15) -------------------------------------------------------------

    /// <summary>D2, D7, D11: a data value of the fields in the order of their slots, the base's first, each the argument bound to it or its default.</summary>
    private string EmitConstruction(CallExpression c, CallInfo info)
    {
        var type = (DataType)Sub(info.Result);
        var fields = Layout.Fields(type);
        var arguments = Arguments(c, info, j => fields[j], _ => false, j => FieldDefault(type.Symbol, j), cConvention: false, owned: true);
        return Make(type, arguments.Select((a, j) => a[(Ir(fields[j]).Length + 1)..]).ToList());
    }

    /// <summary>The default written for the field in the slot, in the declaration of the type that declares the field, and the file of that declaration (D7, D11).</summary>
    private (Expression Value, Unit File) FieldDefault(DataSymbol symbol, int slot)
    {
        var own = symbol.Declaration.PrimaryConstructor ?? [];
        var inherited = symbol.Fields.Count - own.Count;
        if (slot < inherited)
        {
            return FieldDefault(symbol.Base ?? throw new InvalidOperationException($"`{symbol.Name}` has inherited fields and no base"), slot);
        }

        return (own[slot - inherited].Default ?? throw new InvalidOperationException($"the field {slot} of `{symbol.Name}` has no argument and no default"), dataOwners[symbol]);
    }

    /// <summary>A data value of these field values: a plain value built in registers, or a block from the allocator that starts with the number of its type.</summary>
    private string Make(DataType type, List<string> values)
    {
        var fields = Layout.Fields(type);
        if (layout.IsBlock(type))
        {
            var block = layout.Block(type.Symbol);
            var p = Allocate(block);
            fn.Emit($"store i32 {Number(layout.Number(type))}, ptr {p}");
            for (var i = 0; i < fields.Count; i++)
            {
                fn.Emit($"store {Ir(fields[i])} {values[i]}, ptr {FieldPointer(block, p, i)}");
            }

            return p;
        }

        var ir = Ir(type);
        var value = "poison";
        if (fields.Count == 0)
        {
            return "zeroinitializer";
        }

        for (var i = 0; i < fields.Count; i++)
        {
            value = fn.Value($"insertvalue {ir} {value}, {Ir(fields[i])} {values[i]}, {Number(i)}");
        }

        return value;
    }

    /// <summary>Memory for a value of the IR type, from the runtime's allocator, which nothing frees (the reference counting of section 3 is not emitted).</summary>
    private string Allocate(string irType)
    {
        var end = fn.Value($"getelementptr {irType}, ptr null, i32 1");
        var size = fn.Value($"ptrtoint ptr {end} to i64");
        return fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {size})");
    }

    /// <summary>The address of field <paramref name="index"/> in a block: the number of the value's type comes first.</summary>
    private string FieldPointer(string block, string pointer, int index) =>
        fn.Value($"getelementptr {block}, ptr {pointer}, i32 0, i32 {Number(index + 1)}");

    /// <summary>D1: a field of a data value, read from the plain value or from the block.</summary>
    private string Field(string value, DataType type, int index)
    {
        var field = Layout.Fields(type)[index];
        return layout.IsBlock(type)
            ? fn.Value($"load {Ir(field)}, ptr {FieldPointer(layout.Block(type.Symbol), value, index)}")
            : fn.Value($"extractvalue {Ir(type)} {value}, {Number(index)}");
    }

    /// <summary>
    /// D5: a copy of the value with the fields replaced, the values read in the order written after the value; a block is copied
    /// whole, as the type of the value it holds, so that a derived value stays one. The copy holds its replaced fields' values and,
    /// a plain value, the lists of the fields it keeps once more.
    /// </summary>
    private string EmitWith(WithExpression w)
    {
        var type = (DataType)TypeOf(w.Target);
        var fields = Layout.Fields(type);
        var value = EmitExpression(w.Target);
        var replaced = w.Assignments.Select(a =>
        {
            var index = Layout.FieldIndex(type, a.Field);
            return (Index: index, Value: Convert(Owned(a.Value), TypeOf(a.Value), fields[index], a.Value.Line));
        }).ToList();
        if (layout.IsBlock(type))
        {
            var copy = CopyBlock(value, type.Symbol);
            foreach (var (index, v) in replaced)
            {
                fn.Emit($"store {Ir(fields[index])} {v}, ptr {FieldPointer(layout.Block(type.Symbol), copy, index)}");
            }

            return copy;
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (Counted(fields[i]) && !replaced.Exists(r => r.Index == i))
            {
                Retain(fn.Value($"extractvalue {Ir(type)} {value}, {Number(i)}"), fields[i]);
            }
        }

        foreach (var (index, v) in replaced)
        {
            value = fn.Value($"insertvalue {Ir(type)} {value}, {Ir(fields[index])} {v}, {Number(index)}");
        }

        return value;
    }

    /// <summary>A new block with the bytes of a block of the type or of a type derived from it, as long as the block of the type it holds.</summary>
    private string CopyBlock(string pointer, DataSymbol symbol)
    {
        var name = Helper($"copy.{symbol.Name}", "ptr", ["ptr"], () =>
        {
            module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
            ByNumber("%a0", symbol, derived =>
            {
                var block = layout.Block(derived);
                var copy = Allocate(block);
                var end = fn.Value($"getelementptr {block}, ptr null, i32 1");
                var size = fn.Value($"ptrtoint ptr {end} to i64");
                fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {copy}, ptr %a0, i64 {size}, i1 false)");
                fn.Return($"ptr {copy}");
            });
        });
        return fn.Value($"call ptr {name}(ptr {pointer})");
    }

    /// <summary>
    /// In a helper over a block of the type: a branch on the number the block starts with to the code for each type the block may
    /// hold, the type and those derived from it; another number is no block of the type.
    /// </summary>
    private void ByNumber(string pointer, DataSymbol symbol, Action<DataSymbol> each)
    {
        var number = fn.Value($"load i32, ptr {pointer}");
        var derived = layout.Derived(symbol);
        var labels = derived.Select(d => fn.Label("kind")).ToList();
        var other = fn.Label("other");
        fn.Terminate($"switch i32 {number}, label %{other} [ {string.Join(" ", derived.Select((d, i) => $"i32 {Number(layout.Number(new DataType(d, [])))}, label %{labels[i]}"))} ]");
        for (var i = 0; i < derived.Count; i++)
        {
            fn.Block(labels[i]);
            each(derived[i]);
        }

        fn.Block(other);
        fn.Unreachable();
    }

    /// <summary>Whether the block holds a value of the type or of a type derived from it (C4, D6).</summary>
    private string HoldsDerived(string pointer, DataSymbol symbol)
    {
        var number = fn.Value($"load i32, ptr {pointer}");
        var result = "false";
        foreach (var d in layout.Derived(symbol))
        {
            var same = fn.Value($"icmp eq i32 {number}, {Number(layout.Number(new DataType(d, [])))}");
            result = result == "false" ? same : fn.Value($"or i1 {result}, {same}");
        }

        return result;
    }

    /// <summary>
    /// M4: an assignment into a path of a <c>mut</c> variable, short for nested <c>with</c>: the value is read first, then the path in
    /// the variable as the value left it (the owner, 2026-10-24, "Value first"); each data value on the path changes in place, a plain
    /// value in its variable or its field, and a block after it was copied into the place that held it, since another variable may
    /// hold the same block. The field lets go of its old value, unless the field is in a block copied here, whose old block keeps it.
    /// </summary>
    private void EmitPathAssignment(Assignment a)
    {
        var members = new List<MemberAccess>();
        var target = Unwrap(a.Target);
        while (target is MemberAccess m)
        {
            members.Insert(0, m);
            target = Unwrap(m.Target);
        }

        var value = Convert(Owned(a.Value), TypeOf(a.Value), TypeOf(a.Target), a.Value.Line);
        var (place, type, copied) = WritablePath((NameExpression)target, members);
        if (copied)
        {
            fn.Emit($"store {Ir(type)} {value}, ptr {place}");
        }
        else
        {
            Store(value, type, place);
        }
    }

    /// <summary>
    /// M4: the place at the end of a path of fields from the <c>mut</c> variable <paramref name="root"/>, read as the code before it left
    /// the variable, and the type there: each data value on the path is changed in place, a plain value in its variable or its field,
    /// and a block after it was copied into the place that held it, since another variable may hold the same block; whether one was.
    /// </summary>
    private (string Place, KurzType Type, bool Copied) WritablePath(NameExpression root, List<MemberAccess> members)
    {
        var variable = Model.VariableOf(root);
        var place = Place(variable, root.Line);
        var type = Sub(variable.Type);
        var copied = false;
        foreach (var m in members)
        {
            var data = (DataType)type;
            var index = Layout.FieldIndex(data, m.Member);
            if (layout.IsBlock(data))
            {
                copied = true;
                var copy = CopyBlock(fn.Value($"load ptr, ptr {place}"), data.Symbol);
                fn.Emit($"store ptr {copy}, ptr {place}");
                place = FieldPointer(layout.Block(data.Symbol), copy, index);
            }
            else
            {
                place = fn.Value($"getelementptr {Ir(data)}, ptr {place}, i32 0, i32 {Number(index)}");
            }

            type = Layout.Fields(data)[index];
        }

        return (place, type, copied);
    }

    /// <summary>
    /// M5, M9: the place of the list or the map at the end of <paramref name="receiver"/>, a <c>mut</c> variable or a path of fields from
    /// one (M4), the room of its case where the checker read the variable or the last field known not null (N3), for a write that copies
    /// a block other values hold before it writes (M3). A list or a map in a block this copied is held by the block it was copied from
    /// too, which counts it once for both and never lets go of it (section 3), so it takes a count of its own here: the write then copies
    /// it, and the value the old block holds stays as it was.
    /// </summary>
    private string CollectionPlace(Expression receiver)
    {
        var members = new List<MemberAccess>();
        var target = Unwrap(receiver);
        while (target is MemberAccess m)
        {
            members.Insert(0, m);
            target = Unwrap(m.Target);
        }

        if (members.Count == 0)
        {
            return NarrowedPlace((NameExpression)target);
        }

        var (path, stored, copied) = WritablePath((NameExpression)target, members);
        var (place, type) = NarrowedPlace(path, stored, TypeOf(receiver), receiver.Line);
        if (copied)
        {
            Retain(fn.Value($"load {Ir(type)}, ptr {place}"), type);
        }

        return place;
    }

    // ---- equality (D3, D15) and text (A3, A4) ----------------------------------------------------

    /// <summary>
    /// Whether two values of one type are equal: integers, <c>char</c> and <c>bool</c> by their bits, strings by their bytes (T18),
    /// data by their type and their fields (D3, D15), and the values of a field of a union by their case and that case's values
    /// (D3, the owner, 2026-10-23); a <c>float</c> or a <c>double</c> here is a field's or a case's, which compares as C#'s
    /// <c>Equals</c> does, NaN equal to NaN and 0 to -0 (D3, the owner, 2026-10-09 and 2026-10-24); a <c>decimal</c> by its value,
    /// 1.5 equal to 1.50, as C#'s <c>Equals</c> compares it. <paramref name="ieee"/> is set for the two sides of a list's <c>==</c>,
    /// whose <c>double</c> and <c>float</c> items, the values of its <c>double?</c> and <c>float?</c> items, and those of the lists among
    /// its items, compare by IEEE's <c>==</c>, <c>null</c> equal to <c>null</c>, as Rust's <c>Vec&lt;Option&lt;f64&gt;&gt;</c> compares
    /// (section 4, the owner, 2026-10-30, "As plain double"), and for the two sides of a union's <c>==</c>, whose <c>double</c> or
    /// <c>float</c> case compares so too (section 4, the owner, 2026-10-30, "Case and value, as Rust"); a list or a union held in a
    /// <c>data</c> value or a key compares them as C#'s <c>Equals</c> does (section 4, the owner, 2026-10-30, "Equals inside data/keys",
    /// "As plain double").
    /// </summary>
    private string Equal(string left, string right, KurzType type, int line, bool ieee = false)
    {
        switch (type)
        {
            case NullType:
                return "true"; // N1: `null` is the one value of its case
            case PrimitiveType { Kind: Primitive.String }:
                return fn.Value($"call i1 {RuntimeFunction("Same")}(%kz.str {left}, %kz.str {right})");
            case PrimitiveType p when Types.IsInteger(p) || Types.IsTime(p) || p.Kind is Primitive.Char or Primitive.Bool:
                return fn.Value($"icmp eq {Ir(type)} {left}, {right}");
            case EnumType:
                return fn.Value($"icmp eq {Ir(type)} {left}, {right}"); // D12: the same value
            case PrimitiveType p when Types.IsFloating(p):
                {
                    var same = fn.Value($"fcmp oeq {Ir(type)} {left}, {right}");
                    var bothNaN = fn.Value($"and i1 {fn.Value($"fcmp uno {Ir(type)} {left}, {left}")}, {fn.Value($"fcmp uno {Ir(type)} {right}, {right}")}");
                    return fn.Value($"or i1 {same}, {bothNaN}");
                }

            case PrimitiveType { Kind: Primitive.Decimal }:
                return fn.Value($"icmp eq i32 {DecimalOrder(left, right)}, 0");

            case UnionType union:
                {
                    // a nullable item of a list under `==`: `null` equal to `null` alone (N10), a `double?` or a `float?` one's value by
                    // IEEE's `==` (section 4, the owner, 2026-10-30, "As plain double"); a union alone or a list's item under `==`: its
                    // `double` or `float` case by IEEE's `==` (section 4, the owner, 2026-10-30, "Case and value, as Rust")
                    ieee = ieee && IeeeItem(union);
                    var name = Helper($"equal.{(ieee ? "ieee." : "")}{Lower.Mangle(type)}", "i1", [Ir(type), Ir(type)], () =>
                    {
                        var tag = Tag("%a0", union);
                        var same = fn.Value($"icmp eq i{Number(layout.TagBits(union))} {tag}, {Tag("%a1", union)}");
                        var cases = fn.Label("cases");
                        var differ = fn.Label("differ");
                        fn.CondBranch(same, cases, differ);
                        fn.Block(differ);
                        fn.Return("i1 false");
                        fn.Block(cases);
                        ByCase(tag, union, union.Cases, c =>
                        {
                            var (a, b) = c is VoidType or NullType ? ("", "") : (Payload("%a0", union, c), Payload("%a1", union, c));
                            fn.Return($"i1 {(c is VoidType or NullType ? "true" : ieee && c is PrimitiveType p && Types.IsFloating(p) ? fn.Value($"fcmp oeq {Ir(p)} {a}, {b}") : Equal(a, b, c, line, ieee))}");
                        });
                    });
                    return fn.Value($"call i1 {name}({Ir(type)} {left}, {Ir(type)} {right})");
                }

            case DataType:
                {
                    var name = Helper($"equal.{Lower.Mangle(type)}", "i1", [Ir(type), Ir(type)], () => EqualBody((DataType)type, line));
                    return fn.Value($"call i1 {name}({Ir(type)} {left}, {Ir(type)} {right})");
                }

            case ListType list:
                {
                    // M9: `==` compares two lists by their counts and their items in order, each pair by `==`, as Rust's `Vec` does,
                    // a `double` item by IEEE's `==`, NaN unequal to NaN (section 4, the owner, 2026-10-30, "By items, as Rust"); a key
                    // of a map, which hashes a list over its items (section 4, "Any type with =="), and a field of a `data` value that
                    // holds one (D3) compare its `double` items as C#'s `Equals`, NaN equal to NaN (section 4, the owner, 2026-10-30,
                    // "Equals inside data/keys"), so that a value equals itself and a key is found again
                    ieee = ieee && FloatingItems(list);
                    var name = Helper($"equal.{(ieee ? "ieee." : "")}{Lower.Mangle(type)}", "i1", ["ptr", "ptr"], () =>
                    {
                        var count = ListCount("%a0");
                        var items = fn.Label("items");
                        var differ = fn.Label("differ");
                        fn.CondBranch(fn.Value($"icmp eq i32 {count}, {ListCount("%a1")}"), items, differ);
                        fn.Block(items);
                        var first = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a0)");
                        var second = fn.Value($"call ptr {StdFunction("ListItems")}(ptr %a1)");
                        var counter = fn.Alloca("i32", "item");
                        fn.Emit($"store i32 0, ptr {counter}");
                        var check = fn.Label("check");
                        var item = fn.Label("item");
                        var same = fn.Label("same");
                        fn.Block(check);
                        var i = fn.Value($"load i32, ptr {counter}");
                        fn.CondBranch(fn.Value($"icmp slt i32 {i}, {count}"), item, same);
                        fn.Block(item);
                        var wide = fn.Value($"sext i32 {i} to i64");
                        var a = fn.Value($"load {Ir(list.Element)}, ptr {fn.Value($"getelementptr {Ir(list.Element)}, ptr {first}, i64 {wide}")}");
                        var b = fn.Value($"load {Ir(list.Element)}, ptr {fn.Value($"getelementptr {Ir(list.Element)}, ptr {second}, i64 {wide}")}");
                        var more = fn.Label("more");
                        var equal = ieee && list.Element is PrimitiveType p ? fn.Value($"fcmp oeq {Ir(p)} {a}, {b}") : Equal(a, b, list.Element, line, ieee);
                        fn.CondBranch(equal, more, differ);
                        fn.Block(more);
                        fn.Emit($"store i32 {fn.Value($"add i32 {i}, 1")}, ptr {counter}");
                        fn.Branch(check);
                        fn.Block(same);
                        fn.Return("i1 true");
                        fn.Block(differ);
                        fn.Return("i1 false");
                    });
                    return fn.Value($"call i1 {name}(ptr {left}, ptr {right})");
                }

            default:
                throw Unsupported(line, $"`==` on a `{type}`");
        }
    }

    /// <summary>Whether the list's items, or the items of the lists among them, are of a <c>double</c> or a <c>float</c>, or of a union one of whose cases is one, a nullable type of one among them.</summary>
    private static bool FloatingItems(ListType list) => IeeeItem(list.Element);

    /// <summary>Whether an item of the type compares by IEEE's <c>==</c> in a list under <c>==</c>: a <c>double</c>, a <c>float</c>, a list of them and a union one of whose cases is one of them, a nullable type of each among them.</summary>
    private static bool IeeeItem(KurzType item) => item switch
    {
        PrimitiveType p => Types.IsFloating(p),
        ListType inner => FloatingItems(inner),
        UnionType u => u.Cases.Any(IeeeItem),
        _ => false,
    };

    private void EqualBody(DataType type, int line)
    {
        switch (type)
        {
            case DataType d when layout.IsBlock(d):
                {
                    var same = fn.Value($"icmp eq i32 {fn.Value("load i32, ptr %a0")}, {fn.Value("load i32, ptr %a1")}");
                    var fields = fn.Label("fields");
                    var differ = fn.Label("differ");
                    fn.CondBranch(same, fields, differ);
                    fn.Block(differ);
                    fn.Return("i1 false"); // D15: a value of one type never equals a value of another
                    fn.Block(fields);
                    ByNumber("%a0", d.Symbol, held =>
                    {
                        var heldType = new DataType(held, []);
                        var block = layout.Block(held);
                        var result = "true";
                        var types = Layout.Fields(heldType);
                        for (var i = 0; i < types.Count; i++)
                        {
                            var l = fn.Value($"load {Ir(types[i])}, ptr {FieldPointer(block, "%a0", i)}");
                            var r = fn.Value($"load {Ir(types[i])}, ptr {FieldPointer(block, "%a1", i)}");
                            result = And(result, Equal(l, r, types[i], line));
                        }

                        fn.Return($"i1 {result}");
                    });
                    break;
                }

            case DataType d:
                {
                    var result = "true";
                    var types = Layout.Fields(d);
                    for (var i = 0; i < types.Count; i++)
                    {
                        var l = fn.Value($"extractvalue {Ir(d)} %a0, {Number(i)}");
                        var r = fn.Value($"extractvalue {Ir(d)} %a1, {Number(i)}");
                        result = And(result, Equal(l, r, types[i], line));
                    }

                    fn.Return($"i1 {result}");
                    break;
                }
        }
    }

    private string And(string a, string b) => a == "true" ? b : fn.Value($"and i1 {a}, {b}");

    /// <summary>
    /// A3, A4: the text of a data value, the name its declaration gives and its fields, or that name alone without fields; of a
    /// block, of the type the block holds; of a union, the text of its case's value. The caller holds a count of the text.
    /// </summary>
    private string DerivedText(string value, KurzType type, int line)
    {
        var name = Helper($"text.{Lower.Mangle(type)}", "%kz.str", [Ir(type)], () =>
        {
            switch (type)
            {
                case DataType d when layout.IsBlock(d):
                    ByNumber("%a0", d.Symbol, held =>
                    {
                        var heldType = new DataType(held, []);
                        var block = layout.Block(held);
                        var fields = Layout.Fields(heldType).Select((f, i) => fn.Value($"load {Ir(f)}, ptr {FieldPointer(block, "%a0", i)}")).ToList();
                        fn.Return($"%kz.str {RecordText(heldType, fields, line)}");
                    });
                    break;
                case DataType d when IsIoError(d):
                    IoErrorText(d); // S1: its words and its path, in place of A3's text of a data value
                    break;
                case DataType d:
                    fn.Return($"%kz.str {RecordText(d, Layout.Fields(d).Select((_, i) => fn.Value($"extractvalue {Ir(d)} %a0, {Number(i)}")).ToList(), line)}");
                    break;
                case UnionType u when u.Cases.Any(c => c is VoidType):
                    throw new InvalidOperationException($"the text of a `{u}` at line {line}: a success without a value has none, `no-text`, which the checker reports (A7)");
                case UnionType u:
                    ByCase(Tag("%a0", u), u, u.Cases, c =>
                    {
                        var text = TextOf(Payload("%a0", u, c), c, line);
                        if (c is PrimitiveType { Kind: Primitive.String })
                        {
                            Retain(text, c); // the case's string is its own text, of which the result holds a count
                        }

                        fn.Return($"%kz.str {text}");
                    });
                    break;
            }
        });
        return fn.Value($"call %kz.str {name}({Ir(type)} {value})");
    }

    /// <summary>A4: the name of an enum's value without the name of its type, a literal, chosen by the value's number or place.</summary>
    private string EnumText(string value, EnumType type)
    {
        var name = Helper($"text.{Lower.Mangle(type)}", "%kz.str", [Ir(type)], () =>
        {
            var values = type.Symbol.Values;
            var labels = values.Select(_ => fn.Label("value")).ToList();
            var other = fn.Label("other");
            fn.Terminate($"switch {Ir(type)} %a0, label %{other} [ {string.Join(" ", values.Select((_, i) => $"{Ir(type)} {IntegerText(Lower.EnumStored(type.Symbol, i), type)}, label %{labels[i]}"))} ]");
            for (var i = 0; i < values.Count; i++)
            {
                fn.Block(labels[i]);
                fn.Return($"%kz.str {StringValue(values[i])}");
            }

            fn.Block(other);
            fn.Unreachable(); // a value of the enum is one of its values: `From` makes no other (D18)
        });
        return fn.Value($"call %kz.str {name}({Ir(type)} {value})");
    }

    /// <summary>D18: the number of an enum's value, the value itself widened to an <c>int</c>, by its sign where a number is negative.</summary>
    private string EnumNumber(string value, EnumType type) =>
        Ir(type) == "i32" ? value : fn.Value($"{(Lower.EnumSigned(type.Symbol) ? "sext" : "zext")} {Ir(type)} {value} to i32");

    /// <summary>
    /// D18: <c>Plan.From(n)</c>, the value whose number is <c>n</c>, chosen by a switch on the numbers the declaration writes, or the
    /// case <c>Invalid</c> for a number no value has.
    /// </summary>
    private string EmitEnumFrom(CallExpression c, CallInfo info)
    {
        var type = (EnumType)info.Target!;
        var number = Arguments(c, info, _ => Types.Int, _ => false, _ => null, cConvention: false, owned: false).Single()["i32 ".Length..];
        var union = (UnionType)Sub(info.Result);
        var slot = fn.Alloca(Ir(union), "from");
        var done = fn.Label("from.done");
        var values = type.Symbol.Values;
        var labels = values.Select(_ => fn.Label("value")).ToList();
        var invalid = fn.Label("invalid");
        fn.Terminate($"switch i32 {number}, label %{invalid} [ {string.Join(" ", values.Select((_, i) => $"i32 {IntegerText(type.Symbol.Numbers[i], Types.Int)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < values.Count; i++)
        {
            fn.Block(labels[i]);
            fn.Emit($"store {Ir(union)} {MakeUnion(union, type, IntegerText(Lower.EnumStored(type.Symbol, i), type))}, ptr {slot}");
            fn.Branch(done);
        }

        fn.Block(invalid);
        fn.Emit($"store {Ir(union)} {MakeUnion(union, union.Cases.Single(c => !Types.Same(c, type)), "zeroinitializer")}, ptr {slot}");
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {slot}");
    }

    /// <summary>A3: `Name { Field = text, ... }`, or the name alone for a type without fields; the texts it joined on the way are let go of.</summary>
    private string RecordText(DataType type, List<string> values, int line)
    {
        var fields = type.Symbol.Fields;
        if (!type.Symbol.HasFields)
        {
            return StringValue(type.Symbol.Name);
        }

        var text = StringValue($"{type.Symbol.Name} {{ ");
        var types = Layout.Fields(type);
        for (var i = 0; i < fields.Count; i++)
        {
            text = Joined(text, StringValue($"{(i == 0 ? string.Empty : ", ")}{fields[i].Name} = "));
            var piece = TextOf(values[i], types[i], line);
            text = Joined(text, piece);
            if (MakesText(types[i]))
            {
                Release(piece, Types.String);
            }
        }

        return Joined(text, StringValue(" }"));
    }

    /// <summary>The runtime's <c>Concat</c> of the two texts, after which the first, a text the caller held, is let go of.</summary>
    private string Joined(string first, string second)
    {
        var joined = fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {first}, %kz.str {second})");
        Release(first, Types.String);
        return joined;
    }

    /// <summary>
    /// A function of the module the emitted code calls for a type, emitted once: <paramref name="body"/> fills it with its arguments
    /// as <c>%a0</c>, <c>%a1</c> and so on, while the function being emitted waits; <paramref name="attributes"/> are its own function
    /// attributes.
    /// </summary>
    private string Helper(string key, string result, IReadOnlyList<string> parameters, Action body, params string[] attributes)
    {
        var name = $"@\"{key}\"";
        if (!helpers.Add(name))
        {
            return name;
        }

        var saved = fn;
        var (savedRegions, savedScopes) = (regions, scopes);
        fn = new IrFunction($"internal {result} {name}({string.Join(", ", parameters.Select((p, i) => $"{p} %a{Number(i)}"))})", attributes);
        (regions, scopes) = ([], []); // a helper makes no temporary and has no variable
        try
        {
            body();
            emitted.Add(fn);
        }
        finally
        {
            fn = saved;
            (regions, scopes) = (savedRegions, savedScopes);
        }

        return name;
    }
}
