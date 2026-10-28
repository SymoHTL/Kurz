using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Lambdas and function values (F7, F11, F15; design record, section 4: the owner, 2026-10-25, "Pair + heap env"). A function value
/// is the pair <c>%kz.fn</c> of the address of its lambda's code and the address of its environment; a lambda is a function of the
/// module whose first parameter is that environment. A lambda that reads no variable has no environment, null, and allocates
/// nothing. One that reads variables allocates its environment where it is made: the count of the function values that hold it,
/// 8 bytes, the address of the function that lets go of it, then the values it read, each held once more, as an assignment holds
/// them (M3, F15). The environment is counted as a list's block is: a copy raises the count, the end of a holder lowers it, and the
/// last holder's end lets go of the values and frees it.
/// </summary>
public sealed partial class Emitter
{
    private readonly Queue<PendingLambda> lambdas = new();
    private int lambdaCount;

    /// <summary>
    /// A lambda made in the function being emitted, emitted after it: the file it is in, the type arguments of that function, its IR
    /// name, the IR type of its environment, none where it reads no variable, and the variables it reads, in the order of the
    /// environment's fields.
    /// </summary>
    private sealed record PendingLambda(Unit Unit, LambdaExpression Lambda, IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map, string Name, string? Environment, IReadOnlyList<VariableSymbol> Captures);

    /// <summary>The function value of a lambda where it is made: its code, and an environment of the values it reads (F15), a temporary of the region.</summary>
    private string EmitLambda(LambdaExpression l, FunctionType type)
    {
        if (l.Parameters.Any(p => p.Mut))
        {
            throw Unsupported(l.Line, "a `mut` parameter of a lambda (M7)");
        }

        var key = $"{unit.Prefix}.lambda.{Number(l.Line)}.{Number(++lambdaCount)}";
        var name = $"@\"{key}\"";
        var captures = Model.LambdaOf(l).Captures;
        if (captures.Count == 0)
        {
            lambdas.Enqueue(new PendingLambda(unit, l, map, name, null, captures));
            return $"{{ ptr {name}, ptr null }}";
        }

        var types = captures.Select(v => Sub(v.Type)).ToList();
        var environment = $"%\"env.{key}\"";
        module.Global($"{environment} = type {{ i64, ptr{string.Concat(types.Select(t => ", " + Ir(t)))} }}");
        lambdas.Enqueue(new PendingLambda(unit, l, map, name, environment, captures));
        var drop = Helper($"drop.{key}", "void", ["ptr"], () =>
        {
            for (var i = 0; i < types.Count; i++)
            {
                if (Counted(types[i]))
                {
                    Release(fn.Value($"load {Ir(types[i])}, ptr {Capture(environment, "%a0", i)}"), types[i]);
                }
            }

            var end = fn.Value($"getelementptr {environment}, ptr null, i32 1");
            fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {fn.Value($"ptrtoint ptr {end} to i64")})");
            fn.Return(null);
        });
        var p = Allocate(environment);
        fn.Emit($"store i64 1, ptr {p}");
        fn.Emit($"store ptr {drop}, ptr {fn.Value($"getelementptr {environment}, ptr {p}, i32 0, i32 1")}");
        for (var i = 0; i < types.Count; i++)
        {
            var value = fn.Value($"load {Ir(types[i])}, ptr {Place(captures[i], l.Line)}");
            if (Counted(types[i]))
            {
                Retain(value, types[i]);
            }

            fn.Emit($"store {Ir(types[i])} {value}, ptr {Capture(environment, p, i)}");
        }

        return Temp(fn.Value($"insertvalue %kz.fn {{ ptr {name}, ptr null }}, ptr {p}, 1"), type);
    }

    /// <summary>The address of the value of the variable at <paramref name="index"/> that the environment holds.</summary>
    private string Capture(string environment, string pointer, int index) =>
        fn.Value($"getelementptr {environment}, ptr {pointer}, i32 0, i32 {Number(index + 2)}");

    /// <summary>
    /// A lambda's function: its environment in <c>%env</c>, then its parameters, which it borrows from the caller as a function does;
    /// a variable it reads is the value its environment holds.
    /// </summary>
    private void EmitLambdaFunction(PendingLambda pending)
    {
        var caller = unit;
        unit = pending.Unit;
        try
        {
            var l = pending.Lambda;
            var info = Model.LambdaOf(l);
            var type = (FunctionType)Types.Substitute(info.Type, pending.Map);
            var parameters = type.Parameters.Select((t, i) => $"{Ir(t)} %p{Number(i)}").Prepend("ptr %env");
            Begin(new IrFunction($"internal {Ir(type.Result)} {pending.Name}({string.Join(", ", parameters)})", Attributes()), pending.Map, type.Result);
            for (var i = 0; i < pending.Captures.Count; i++)
            {
                slots[pending.Captures[i]] = Capture(pending.Environment!, "%env", i);
            }

            for (var i = 0; i < info.Parameters.Count; i++)
            {
                var ir = Ir(type.Parameters[i]);
                var slot = fn.Alloca(ir, info.Parameters[i].Name);
                fn.Emit($"store {ir} %p{Number(i)}, ptr {slot}");
                slots[info.Parameters[i]] = slot;
            }

            EmitBody(l.ExpressionBody, l.BlockBody, l.Line);
            End();
        }
        finally
        {
            unit = caller;
        }
    }

    /// <summary>
    /// F11: a call of a function value, its code given its environment and the arguments, which it borrows for the call. The value
    /// it calls and each argument are borrowed across the arguments after them: the checker holds them there (M7), refuses a change
    /// of a list among them, <c>f(xs, Push(mut xs))</c>, and marks a read of a copied type that a change meets, <c>f</c> in
    /// <c>f(Swap(mut f))</c>, which <see cref="HeldCopy"/> counts.
    /// </summary>
    private string EmitFunctionValueCall(CallExpression c, FunctionType type)
    {
        var callee = EmitExpression(c.Callee);
        var arguments = new List<string> { $"ptr {fn.Value($"extractvalue %kz.fn {callee}, 1")}" };
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var a = c.Arguments[i].Value;
            arguments.Add($"{Ir(type.Parameters[i])} {Convert(EmitExpression(a), TypeOf(a), type.Parameters[i], a.Line)}");
        }

        var call = $"call {Ir(type.Result)} {fn.Value($"extractvalue %kz.fn {callee}, 0")}({string.Join(", ", arguments)})";
        return Types.IsVoid(type.Result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>One more holder of the environment of a function value; none for a lambda that reads no variable.</summary>
    private string RetainEnvironment() => Helper("retain.fn", "void", ["%kz.fn"], () =>
    {
        var environment = Environment("%a0");
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value($"load i64, ptr {environment}")}, 1")}, ptr {environment}");
        fn.Return(null);
    });

    /// <summary>One holder less of the environment of a function value; the last one calls the function that lets go of it.</summary>
    private string ReleaseEnvironment() => Helper("release.fn", "void", ["%kz.fn"], () =>
    {
        var environment = Environment("%a0");
        var count = fn.Value($"sub i64 {fn.Value($"load i64, ptr {environment}")}, 1");
        fn.Emit($"store i64 {count}, ptr {environment}");
        var last = fn.Label("last");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {count}, 0"), last, held);
        fn.Block(last);
        var drop = fn.Value($"load ptr, ptr {fn.Value($"getelementptr {{ i64, ptr }}, ptr {environment}, i32 0, i32 1")}");
        fn.Emit($"call void {drop}(ptr {environment})");
        fn.Return(null);
        fn.Block(held);
        fn.Return(null);
    });

    /// <summary>In a helper over a function value: its environment, after a branch that returns where it has none.</summary>
    private string Environment(string value)
    {
        var environment = fn.Value($"extractvalue %kz.fn {value}, 1");
        var none = fn.Label("none");
        var some = fn.Label("environment");
        fn.CondBranch(fn.Value($"icmp eq ptr {environment}, null"), none, some);
        fn.Block(none);
        fn.Return(null);
        fn.Block(some);
        return environment;
    }
}
