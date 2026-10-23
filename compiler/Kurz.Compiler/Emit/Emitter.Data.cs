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
        var arguments = Arguments(c, info, j => fields[j], _ => false, j => FieldDefault(type.Symbol, j), dataOwners[type.Symbol], cConvention: false);
        return Make(type, arguments.Select((a, j) => a[(Ir(fields[j]).Length + 1)..]).ToList());
    }

    /// <summary>The default written for the field in the slot, in the declaration of the type that declares the field (D7, D11).</summary>
    private static Expression FieldDefault(DataSymbol symbol, int slot)
    {
        var own = symbol.Declaration.PrimaryConstructor ?? [];
        var inherited = symbol.Fields.Count - own.Count;
        if (slot < inherited)
        {
            return FieldDefault(symbol.Base ?? throw new InvalidOperationException($"`{symbol.Name}` has inherited fields and no base"), slot);
        }

        return own[slot - inherited].Default ?? throw new InvalidOperationException($"the field {slot} of `{symbol.Name}` has no argument and no default");
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

    /// <summary>D5: a copy of the value with the fields replaced, the values read in the order written after the value; a block is copied whole, as the type of the value it holds, so that a derived value stays one.</summary>
    private string EmitWith(WithExpression w)
    {
        var type = (DataType)TypeOf(w.Target);
        var fields = Layout.Fields(type);
        var value = EmitExpression(w.Target);
        var replaced = w.Assignments.Select(a =>
        {
            var index = Layout.FieldIndex(type, a.Field);
            return (Index: index, Value: Convert(EmitExpression(a.Value), TypeOf(a.Value), fields[index], a.Value.Line));
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
    /// M4: an assignment into a path of a <c>mut</c> variable, short for nested <c>with</c>: the value is read first, then each data
    /// value on the path changes in place, a plain value in its variable or its field, and a block after it was copied into the
    /// place that held it, since another variable may hold the same block.
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

        var root = (NameExpression)target;
        var variable = Model.VariableOf(root);
        var value = Convert(EmitExpression(a.Value), TypeOf(a.Value), TypeOf(a.Target), a.Value.Line);
        var place = Place(variable, root.Line);
        var type = Sub(variable.Type);
        foreach (var m in members)
        {
            var data = (DataType)type;
            var index = Layout.FieldIndex(data, m.Member);
            if (layout.IsBlock(data))
            {
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

        fn.Emit($"store {Ir(type)} {value}, ptr {place}");
    }

    // ---- equality (D3, D15) and text (A3, A4) ----------------------------------------------------

    /// <summary>
    /// Whether two values of one type are equal: integers, <c>char</c> and <c>bool</c> by their bits, strings by their bytes (T18),
    /// data by their type and their fields (D3, D15). <c>==</c> takes no union (T18), and a field of a union type compares by no
    /// equality the record gives, so a data value with one is not compared.
    /// </summary>
    private string Equal(string left, string right, KurzType type, int line)
    {
        switch (type)
        {
            case PrimitiveType { Kind: Primitive.String }:
                return fn.Value($"call i1 {RuntimeFunction("Same")}(%kz.str {left}, %kz.str {right})");
            case PrimitiveType p when Types.IsInteger(p) || p.Kind is Primitive.Char or Primitive.Bool:
                return fn.Value($"icmp eq {Ir(type)} {left}, {right}");
            case UnionType:
                throw Unsupported(line, $"`==` on a data value with a field of the union `{type}` (D3, T18)");
            case DataType:
                {
                    var name = Helper($"equal.{Lower.Mangle(type)}", "i1", [Ir(type), Ir(type)], () => EqualBody((DataType)type, line));
                    return fn.Value($"call i1 {name}({Ir(type)} {left}, {Ir(type)} {right})");
                }

            default:
                throw Unsupported(line, $"`==` on a `{type}`");
        }
    }

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

    /// <summary>A3, A4: the text of a data value, its type's name and its fields, or its name alone without fields; of a block, of the type the block holds; of a union, the text of its case's value.</summary>
    private string DerivedText(string value, KurzType type, int line)
    {
        if (type is DataType { TypeArguments.Count: > 0 } generic)
        {
            throw Unsupported(line, $"the text of a value of the generic data type `{generic}` (A3)");
        }

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
                case DataType d:
                    fn.Return($"%kz.str {RecordText(d, Layout.Fields(d).Select((_, i) => fn.Value($"extractvalue {Ir(d)} %a0, {Number(i)}")).ToList(), line)}");
                    break;
                case UnionType u:
                    ByCase(Tag("%a0", u), u, u.Cases.Where(c => c is not VoidType), c => fn.Return($"%kz.str {TextOf(Payload("%a0", u, c), c, line)}"));
                    break;
            }
        });
        return fn.Value($"call %kz.str {name}({Ir(type)} {value})");
    }

    /// <summary>A3: `Name { Field = text, ... }`, or the name alone for a type without fields.</summary>
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
            text = Concat(text, StringValue($"{(i == 0 ? string.Empty : ", ")}{fields[i].Name} = "));
            text = Concat(text, TextOf(values[i], types[i], line));
        }

        return Concat(text, StringValue(" }"));
    }

    private string Concat(string first, string second) => fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {first}, %kz.str {second})");

    /// <summary>
    /// A function of the module the emitted code calls for a type, emitted once: <paramref name="body"/> fills it with its arguments
    /// as <c>%a0</c>, <c>%a1</c> and so on, while the function being emitted waits.
    /// </summary>
    private string Helper(string key, string result, IReadOnlyList<string> parameters, Action body)
    {
        var name = $"@\"{key}\"";
        if (!helpers.Add(name))
        {
            return name;
        }

        var saved = fn;
        fn = new IrFunction($"internal {result} {name}({string.Join(", ", parameters.Select((p, i) => $"{p} %a{Number(i)}"))})", Attributes());
        try
        {
            body();
            emitted.Add(fn);
        }
        finally
        {
            fn = saved;
        }

        return name;
    }
}
