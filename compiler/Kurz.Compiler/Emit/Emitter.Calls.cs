using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- calls -----------------------------------------------------------------------------------

    /// <summary>The value of a call, null for a <c>void</c> one; of a call whose result is a union and that stands for its success, the success, with every other case leaving the function (O2, O3).</summary>
    private string? EmitCall(CallExpression c)
    {
        var info = Model.CallOf(c);
        var result = Sub(info.Result);
        var value = info.Kind switch
        {
            CallKind.Print => EmitPrint(c),
            CallKind.Conversion => EmitConversion(c, info.Target!),
            CallKind.Function => EmitFunctionCall(c, info),
            CallKind.CFunction => EmitCFunctionCall(c, info),
            CallKind.Value => EmitValueCall(c),
            CallKind.PointerConversion => EmitPointerConversion(c),
            CallKind.SizeOf => SizeOf(Sub(info.Target!), c.Line),
            CallKind.FromRaw when info.Target is PrimitiveType { Kind: Primitive.String } => EmitStringFromRaw(c),
            CallKind.ListNew or CallKind.ListAdd or CallKind.ListWhere or CallKind.FromRaw => throw Unsupported(c.Line, "a list (M9)"),
            CallKind.DataNew => EmitConstruction(c, info),
            _ => throw new InvalidOperationException(info.Kind.ToString()),
        };
        if (result is UnionType union && !Types.Same(TypeOf(c), union))
        {
            return Propagate(value ?? throw new InvalidOperationException($"a call at line {c.Line} without a value"), union, c.Line); // the checker typed the call as its success (O2)
        }

        return Types.IsVoid(result) ? null : value;
    }

    /// <summary>A1: the text of the value, with the file and the line of the `print`, to the runtime's <c>Print</c>, which raises <c>print-failed</c> at that place (the owner, 2026-10-23, "Caller passes it").</summary>
    private string? EmitPrint(CallExpression c)
    {
        var argument = c.Arguments[0].Value;
        var text = TextOf(EmitExpression(argument), TypeOf(argument), argument.Line);
        fn.Emit($"call void {RuntimeFunction("Print")}(%kz.str {text}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        return null;
    }

    /// <summary>The type arguments of a call, in the terms of the function being emitted (T19, T27).</summary>
    private Dictionary<TypeParameterSymbol, KurzType> InstanceMap(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> callMap) =>
        f.TypeParameters.ToDictionary(p => p, p => Sub(callMap.TryGetValue(p, out var t) ? t : throw new InvalidOperationException($"`{p.Name}` of `{f.Name}` is unbound")));

    private string EmitFunctionCall(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var instanceMap = InstanceMap(f, info.Map);
        var name = Instance(f, instanceMap);
        var arguments = Arguments(
            c,
            info,
            j => Types.Substitute(f.Parameters[j].Type, instanceMap),
            j => f.Parameters[j].Mut,
            j => f.Declaration!.Parameters[j].Default,
            owners[f],
            f.TakesC);
        var result = Types.Substitute(f.ReturnType, instanceMap);
        var call = $"call {ParameterType(f, result)} {name}({string.Join(", ", arguments)})";
        return Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>R16: a call of a declaration of a block of C functions, by its own name, the library it comes from added to the link; an argument in the place of <c>...</c> is passed as C passes it, an integer narrower than an <c>int</c> widened to one.</summary>
    private string EmitCFunctionCall(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var declaration = f.CDeclaration!;
        var parameters = f.Parameters.Select(p => Lower.CType(p.Type)).ToList();
        var result = Lower.CType(f.ReturnType);
        module.Declare($"declare {result} @{declaration.Name}({string.Join(", ", declaration.Variadic ? parameters.Append("...") : parameters)})");
        if (cLibraries.TryGetValue(f, out var library) && library is not null)
        {
            libraries.Add(library);
        }

        var arguments = Arguments(c, info, j => f.Parameters[j].Type, _ => false, j => declaration.Parameters[j].Default, owners[f], cConvention: true);
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            if (info.Binding[i] < 0)
            {
                arguments.Add(VariadicArgument(c.Arguments[i].Value));
            }
        }

        var type = declaration.Variadic ? $"{result} ({string.Join(", ", parameters.Append("..."))})" : result;
        var call = $"call {type} @{declaration.Name}({string.Join(", ", arguments)})";
        return Types.IsVoid(f.ReturnType) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>An argument in the place of a C declaration's <c>...</c>: C's default promotion widens an integer narrower than an <c>int</c>, and a <c>bool</c>, to an <c>int</c>.</summary>
    private string VariadicArgument(Expression a)
    {
        var type = TypeOf(a);
        var value = EmitExpression(a);
        if (type is PrimitiveType { Kind: Primitive.Bool })
        {
            return $"i32 {fn.Value($"zext i1 {value} to i32")}";
        }

        if (Types.IsInteger(type) && Lower.Size(type) < 4)
        {
            return $"i32 {Resize(value, type, Types.IsSigned(type) ? Types.Int : Types.UInt)}";
        }

        return $"{Ir(type)} {value}";
    }

    private string Emitted(string instruction)
    {
        fn.Emit(instruction);
        return "void";
    }

    /// <summary>R17: a call of a value of a C function type, through its address, in the C calling convention; a call of a Kurz function value is not emitted.</summary>
    private string EmitValueCall(CallExpression c)
    {
        var type = TypeOf(c.Callee) as CFunctionType ?? throw Unsupported(c.Line, $"a call of a value of type `{TypeOf(c.Callee)}` (F11)");
        var callee = EmitExpression(c.Callee);
        var arguments = new List<string>();
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var a = c.Arguments[i].Value;
            arguments.Add($"{Lower.CType(type.Parameters[i])} {Convert(EmitExpression(a), TypeOf(a), type.Parameters[i], a.Line)}");
        }

        var call = $"call {Lower.CType(type.Result)} {callee}({string.Join(", ", arguments)})";
        return Types.IsVoid(type.Result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>
    /// The arguments of a call in the order of the parameter slots (D11): the argument bound to the slot, converted to its type, or
    /// the slot's default; a <c>mut</c> slot gets the address of the caller's variable (M7). The arguments are evaluated in the
    /// order they are written; an argument in the place of a C declaration's <c>...</c> (binding -1) is the caller's to add. A
    /// default is read in the file <paramref name="declaredIn"/> that declares it, which the call may stand outside of.
    /// </summary>
    private List<string> Arguments(CallExpression c, CallInfo info, Func<int, KurzType> slotType, Func<int, bool> slotMut, Func<int, Expression?> slotDefault, Unit? declaredIn, bool cConvention)
    {
        var values = new string?[c.Arguments.Count];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = info.Binding[i];
            if (slot >= 0 && !slotMut(slot))
            {
                var a = c.Arguments[i].Value;
                values[i] = Convert(EmitExpression(a), TypeOf(a), Sub(slotType(slot)), a.Line);
            }
        }

        var arguments = new List<string>();
        for (var j = 0; j < info.SlotCount; j++)
        {
            var argument = -1;
            for (var i = 0; i < info.Binding.Count; i++)
            {
                if (info.Binding[i] == j)
                {
                    argument = i;
                }
            }

            var type = Sub(slotType(j));
            if (slotMut(j))
            {
                if (argument < 0 || Unwrap(c.Arguments[argument].Value) is not NameExpression n)
                {
                    throw Unsupported(c.Line, "a `mut` parameter given anything but a variable (M7)");
                }

                arguments.Add($"ptr {Place(Model.VariableOf(n), n.Line)}");
                continue;
            }

            string value;
            if (argument >= 0)
            {
                value = values[argument]!;
            }
            else
            {
                var d = slotDefault(j) ?? throw new InvalidOperationException($"the slot {j} of the call at line {c.Line} has no argument and no default");
                value = EmitDefault(declaredIn!, d, type);
            }

            arguments.Add($"{(cConvention ? Lower.CType(type) : Ir(type))} {value}");
        }

        return arguments;
    }

    /// <summary>A parameter's or a field's default (D11), read in the file that declares it.</summary>
    private string EmitDefault(Unit declaredIn, Expression d, KurzType type)
    {
        var caller = unit;
        unit = declaredIn;
        try
        {
            return Convert(EmitExpression(d), TypeOf(d), type, d.Line);
        }
        finally
        {
            unit = caller;
        }
    }

    // ---- conversions written as calls ------------------------------------------------------------

    /// <summary>
    /// T10, T20: a conversion written as a call of an integer type or of <c>char</c>; a value that does not fit raises
    /// <c>overflow</c> in a test build and wraps in a release build. R14: <c>long(p)</c> is the address a pointer holds.
    /// </summary>
    private string EmitConversion(CallExpression c, KurzType target)
    {
        var source = c.Arguments[0].Value;
        var sourceType = TypeOf(source);
        var value = EmitExpression(source);
        if (sourceType is PointerType or CFunctionType && target is PrimitiveType { Kind: Primitive.Long })
        {
            return fn.Value($"ptrtoint ptr {value} to i64");
        }

        var from = sourceType is PrimitiveType { Kind: Primitive.Char } ? Types.UInt : sourceType;
        var to = target is PrimitiveType { Kind: Primitive.Char } ? Types.UInt : target;
        if (!Types.IsInteger(from) || !Types.IsInteger(to))
        {
            throw Unsupported(c.Line, $"a conversion from `{sourceType}` to `{target}`");
        }

        var fromWidth = Lower.Size(from);
        var toWidth = Lower.Size(to);
        var fromSigned = Types.IsSigned(from);
        var toSigned = Types.IsSigned(to);
        if (test)
        {
            string? misfit = null;
            if (toWidth >= fromWidth)
            {
                if (fromSigned && !toSigned)
                {
                    misfit = fn.Value($"icmp slt {Ir(from)} {value}, 0"); // a negative value fits no unsigned type
                }
                else if (!fromSigned && toSigned && toWidth == fromWidth)
                {
                    misfit = fn.Value($"icmp slt {Ir(from)} {value}, 0"); // the high bit set: above the signed maximum
                }
            }
            else
            {
                var cut = fn.Value($"trunc {Ir(from)} {value} to {Ir(to)}");
                var back = fn.Value($"{(toSigned ? "sext" : "zext")} {Ir(to)} {cut} to {Ir(from)}");
                var same = fn.Value($"icmp eq {Ir(from)} {back}, {value}");
                misfit = fn.Value($"xor i1 {same}, true");
                if (fromSigned != toSigned)
                {
                    // negative, or from an unsigned type the high bit set, which the cut and its sign extension give back unchanged
                    misfit = fn.Value($"or i1 {misfit}, {fn.Value($"icmp slt {Ir(from)} {value}, 0")}");
                }
            }

            if (misfit is not null)
            {
                var fail = fn.Label("overflow");
                var ok = fn.Label("fits");
                fn.CondBranch(misfit, fail, ok);
                fn.Block(fail);
                Raise(ErrorIds.Overflow, c.Line);
                fn.Block(ok);
            }
        }

        return Resize(value, from, to);
    }

    /// <summary>R14: the bytes a value of a number, <c>bool</c>, <c>char</c>, a string or a pointer takes; the layout of a data value and of a union is the compiler's, which a program does not read.</summary>
    private static string SizeOf(KurzType type, int line) => type is DataType or UnionType
        ? throw Unsupported(line, $"the size of a `{type}` (R14)")
        : Number(Lower.Size(type));

    /// <summary>R14: <c>Ptr&lt;T&gt;(x)</c> of a pointer is the same address, and of a <c>long</c> the address the number holds.</summary>
    private string EmitPointerConversion(CallExpression c)
    {
        var source = c.Arguments[0].Value;
        var sourceType = TypeOf(source);
        var value = EmitExpression(source);
        if (sourceType is PointerType or CFunctionType)
        {
            return value;
        }

        return fn.Value($"inttoptr i64 {Convert(value, sourceType, Types.Long, source.Line)} to ptr");
    }

    /// <summary>
    /// R15: <c>string.FromRaw(p, n)</c>, the string of the <c>n</c> bytes at <c>p</c>, which it takes over; an <c>n</c> above the largest
    /// <c>int</c> ends the process as a refused allocation does (T15; the owner, 2026-10-23 and 2026-10-24).
    /// </summary>
    private string EmitStringFromRaw(CallExpression c)
    {
        var info = Model.CallOf(c);
        var arguments = Arguments(c, info, j => j == 0 ? new PointerType(Types.Byte) : Types.Long, _ => false, _ => null, null, cConvention: false);
        var pointer = arguments[0]["ptr ".Length..];
        var count = arguments[1]["i64 ".Length..];
        var tooLong = fn.Label("too.long");
        var fits = fn.Label("fits");
        fn.CondBranch(fn.Value($"icmp sgt i64 {count}, 2147483647"), tooLong, fits);
        fn.Block(tooLong);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {count})");
        fn.Unreachable();
        fn.Block(fits);
        var withPointer = fn.Value($"insertvalue %kz.str undef, ptr {pointer}, 0");
        return fn.Value($"insertvalue %kz.str {withPointer}, i64 {count}, 1");
    }
}
