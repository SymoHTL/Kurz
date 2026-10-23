using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;

namespace Kurz.Compiler.Emit;

/// <summary>The IR of a program with its runtime, and the libraries its C functions come from (R16), which the link names.</summary>
public sealed record EmittedProgram(string Ir, IReadOnlyList<string> Libraries);

/// <summary>
/// The code generator: LLVM IR, as text, for a program the checker read without an error and without <c>not supported</c>, from
/// the syntax tree and the checker's <see cref="SemanticModel"/>, together with the runtime of the target it is built for, which
/// is Kurz too and is emitted into the same module from its own trees and models (design record, section 2): the program's top-level
/// code is the function <c>kz.main</c>, and the module's <c>main</c>, which the C library's start calls, calls the runtime's
/// <c>Start</c>, then <c>kz.main</c>, then the runtime's <c>Finish</c>, the runtime's one exit. It emits the integers,
/// <c>bool</c>, <c>char</c> and <c>string</c>, functions and their generic instances, the statements of chapter 10, <c>throw</c>,
/// and the raw forms of chapter 11 the runtime is written in (<see cref="Lower.Supported"/>); a program that uses another type, or a
/// construct the emitter does not lay out, gets one <c>not supported</c> diagnostic and no IR. In a test build the arithmetic of T4
/// raises <c>overflow</c> (T5) and every function carries LLVM's attribute that turns no call into a jump; in a release build it
/// wraps and the optimiser may. A raise by code passes its file and its line to the runtime as constants (design record,
/// section 6), and so does a <c>print</c>, for <c>print-failed</c> (the owner, 2026-10-23, "Caller passes it").
/// </summary>
public sealed partial class Emitter
{
    private static readonly IReadOnlyDictionary<TypeParameterSymbol, KurzType> NoMap = new Dictionary<TypeParameterSymbol, KurzType>();

    private readonly bool test;
    private readonly IrModule module = new();
    private readonly Unit program;
    private readonly Unit common;
    private readonly IReadOnlyList<Unit> units;
    private readonly Dictionary<FunctionSymbol, Unit> owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionSymbol, string?> cLibraries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VariableSymbol, string> statics = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> instances = new(StringComparer.Ordinal);
    private readonly Queue<(FunctionSymbol Function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map, string Name)> pending = new();
    private readonly List<IrFunction> emitted = [];
    private readonly SortedSet<string> libraries = new(StringComparer.Ordinal);

    // the function being emitted, and the file it is in
    private Unit unit;
    private IrFunction fn = null!;
    private Dictionary<VariableSymbol, string> slots = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<TypeParameterSymbol, KurzType> map = NoMap;
    private KurzType returnType = Types.Void;
    private readonly Stack<(string Break, string Continue)> loops = new();

    /// <summary>A file the module holds code of: the program, or a folder of the runtime, with the prefix of its functions' IR names.</summary>
    private sealed record Unit(string Prefix, string File, ProgramSyntax Tree, SemanticModel Model, bool Runtime);

    private Emitter(Unit program, Unit common, Unit sys, bool test)
    {
        this.program = program;
        this.common = common;
        units = [program, common, sys];
        unit = program;
        this.test = test;
    }

    private SemanticModel Model => unit.Model;

    /// <summary>
    /// The IR of the program and of the runtime checked for its target, or null with one <c>not supported</c> diagnostic added
    /// when the program uses what the emitter does not lay out. <paramref name="checker"/> is the checker that read the program,
    /// with no error and no <c>not supported</c> among its diagnostics; <paramref name="file"/> is the name of the program's
    /// file as a compile error names it (design record, section 2), which the error line of an exception names (section 6).
    /// </summary>
    public static EmittedProgram? Emit(ProgramSyntax tree, Checker checker, CheckedRuntime runtime, string file, bool testBuild, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.HasErrors || diagnostics.HasNotSupported)
        {
            throw new InvalidOperationException("the emitter takes a program the checker read without an error and without `not supported`");
        }

        var emitter = new Emitter(
            new Unit("f", file, tree, checker.Model, false),
            new Unit("rt.common", runtime.CommonPath, runtime.Common, runtime.CommonChecker.Model, true),
            new Unit("rt.sys", runtime.SysPath, runtime.Sys, runtime.SysChecker.Model, true),
            testBuild);
        try
        {
            return emitter.Run();
        }
        catch (UnsupportedException e)
        {
            diagnostics.NotSupported(e.Line, e.Construct);
            return null;
        }
    }

    /// <summary>A construct the emitter does not lay out, at its line; the one diagnostic of the run.</summary>
    private sealed class UnsupportedException(int line, string construct) : Exception(construct)
    {
        public int Line { get; } = line;

        public string Construct { get; } = construct;
    }

    private static UnsupportedException Unsupported(int line, string construct) => new(line, construct);

    private EmittedProgram Run()
    {
        foreach (var u in units)
        {
            Register(u);
        }

        unit = program;
        Begin(new IrFunction("internal void @kz.main()", Attributes()), NoMap, Types.Void);
        foreach (var s in program.Tree.Statements)
        {
            EmitStatement(s);
        }

        if (!fn.Terminated)
        {
            fn.Return(null);
        }

        End();
        var start = RuntimeFunction("Start");
        var finish = RuntimeFunction("Finish");
        while (pending.Count > 0)
        {
            var (function, instanceMap, name) = pending.Dequeue();
            EmitFunction(function, instanceMap, name);
        }

        var main = new IrFunction("i32 @main()", Attributes());
        main.Emit($"call void {start}()");
        main.Emit("call void @kz.main()");
        main.Emit($"call void {finish}()");
        main.Unreachable();
        module.Add(main);
        foreach (var f in emitted)
        {
            module.Add(f);
        }

        return new EmittedProgram(module.Render(), libraries.ToList());
    }

    /// <summary>The attributes of every function the module defines: in a test build, no call turned into a jump (design record, section 2).</summary>
    private string[] Attributes() => test ? ["\"disable-tail-calls\"=\"true\""] : [];

    /// <summary>The functions, the C declarations and the state outside a function of a file (R13, R16), each by its symbol.</summary>
    private void Register(Unit u)
    {
        unit = u;
        foreach (var statement in u.Tree.Statements)
        {
            switch (statement)
            {
                case FunctionDeclaration d:
                    owners[u.Model.FunctionOf(d)] = u;
                    break;
                case ExternBlock b:
                    foreach (var f in b.Functions)
                    {
                        var symbol = u.Model.CFunctionOf(f);
                        owners[symbol] = u;
                        cLibraries[symbol] = b.Library;
                    }

                    break;
                case VariableDeclaration v when (v.Modifiers & Modifiers.Static) != 0:
                    DefineStatic(v);
                    break;
                case TypeDeclaration t:
                    throw Unsupported(t.Line, $"the declaration of a {t.Kind.ToString().ToLowerInvariant()} type (D1)");
            }
        }
    }

    /// <summary>R13: state outside a function is a global of the module, its constant initial value its initializer; <c>thread static</c> is the target's thread-local storage.</summary>
    private void DefineStatic(VariableDeclaration v)
    {
        var variable = Model.DeclarationOf(v);
        var type = variable.Type;
        RequireSupported(type, v.Line);
        var name = $"@\"s.{unit.Prefix}.{v.Name}\"";
        var initial = v.Value is null ? Zero(type) : Constant(Model.TypeOf(v.Value).Constant ?? throw new InvalidOperationException($"state without a constant at {unit.File}:{v.Line}"), type, v.Line);
        var thread = (v.Modifiers & Modifiers.Thread) != 0 ? "thread_local " : string.Empty;
        module.Global($"{name} = internal {thread}global {Ir(type)} {initial}");
        statics[variable] = name;
    }

    private static string Zero(KurzType type) => Lower.IrType(type) switch
    {
        "ptr" => "null",
        "i1" => "false",
        "%kz.str" => "zeroinitializer",
        _ => "0",
    };

    private void Begin(IrFunction function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, KurzType result)
    {
        fn = function;
        slots = new Dictionary<VariableSymbol, string>(ReferenceEqualityComparer.Instance);
        map = instanceMap;
        returnType = result;
        loops.Clear();
    }

    private void End() => emitted.Add(fn);

    // ---- types -----------------------------------------------------------------------------------

    /// <summary>The type with the type arguments of the function being emitted put in.</summary>
    private KurzType Sub(KurzType type) => map.Count == 0 ? type : Types.Substitute(type, map);

    private KurzType TypeOf(Expression e) => Sub(Model.TypeOf(e).Type);

    private static string Ir(KurzType type) => Lower.IrType(type);

    private static void RequireSupported(KurzType type, int line)
    {
        if (!Lower.Supported(type))
        {
            throw Unsupported(line, Lower.Refusal(type));
        }
    }

    // ---- functions -------------------------------------------------------------------------------

    /// <summary>The IR name of a function for these type arguments, queued for emission on first use.</summary>
    private string Instance(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap)
    {
        var owner = owners.TryGetValue(f, out var u) ? u : throw new InvalidOperationException($"`{f.Name}` belongs to no file of the module");
        var arguments = f.TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", f.TypeParameters.Select(p => Lower.Mangle(instanceMap[p]))) + ">";
        var key = $"{owner.Prefix}.{f.Name}.{f.Line.ToString(CultureInfo.InvariantCulture)}{arguments}";
        if (!instances.TryGetValue(key, out var name))
        {
            name = $"@\"{key}\"";
            instances[key] = name;
            pending.Enqueue((f, instanceMap, name));
        }

        return name;
    }

    /// <summary>The IR name of a <c>pub</c> function of the runtime's <c>common/</c>, which the emitted code calls by its name.</summary>
    private string RuntimeFunction(string name) => Instance(RuntimeSymbol(name), NoMap);

    private FunctionSymbol RuntimeSymbol(string name) =>
        common.Tree.Statements.OfType<FunctionDeclaration>()
            .Where(d => d.Name == name && (d.Modifiers & Modifiers.Pub) != 0)
            .Select(d => common.Model.FunctionOf(d))
            .SingleOrDefault() ?? throw new InvalidOperationException($"{common.File} has no `pub` function `{name}`, which the emitted code calls");

    private void EmitFunction(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, string name)
    {
        var caller = unit;
        unit = owners[f];
        var d = f.Declaration!;
        try
        {
            EmitFunctionCore(f, d, instanceMap, name);
        }
        catch (UnsupportedException e) when (unit.Runtime)
        {
            // the runtime is the compiler's own code: a construct of it the emitter does not lay out is a defect of the compiler
            throw new InvalidOperationException($"{unit.File}:{e.Line}: the emitter does not lay out {e.Construct}", e);
        }
        finally
        {
            unit = caller;
        }
    }

    private void EmitFunctionCore(FunctionSymbol f, FunctionDeclaration d, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, string name)
    {
        if ((d.Modifiers & Modifiers.Naked) != 0)
        {
            throw Unsupported(d.Line, "a `naked` function (R12)");
        }

        var result = Types.Substitute(f.ReturnType, instanceMap);
        RequireSupported(result, d.Line);
        var parameters = new List<string>();
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var type = Types.Substitute(p.Type, instanceMap);
            RequireSupported(type, p.Line);
            parameters.Add(p.Mut ? $"ptr %p{i}" : $"{ParameterType(f, type)} %p{i}");
        }

        Begin(new IrFunction($"internal {ParameterType(f, result)} {name}({string.Join(", ", parameters)})", Attributes()), instanceMap, result);
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var variable = Model.DeclarationOf(d.Parameters[i]);
            if (p.Mut)
            {
                slots[variable] = $"%p{i}"; // the caller's storage: what the function writes, the caller sees (M7)
            }
            else
            {
                var type = Sub(p.Type);
                var slot = fn.Alloca(Ir(type), p.Name);
                fn.Emit($"store {Ir(type)} %p{i}, ptr {slot}");
                slots[variable] = slot;
            }
        }

        if (d.ExpressionBody is not null)
        {
            var value = EmitExpression(d.ExpressionBody);
            EmitReturn(value, TypeOf(d.ExpressionBody), d.ExpressionBody.Line);
        }
        else
        {
            EmitBlock(d.BlockBody!);
            if (!fn.Terminated)
            {
                if (Types.IsVoid(returnType))
                {
                    fn.Return(null);
                }
                else
                {
                    fn.Unreachable(); // the checker reported missing-return for every other result (F3)
                }
            }
        }

        End();
    }

    /// <summary>A parameter's or a result's type in a function's signature: of the C calling convention for a function written <c>extern "C"</c> (R16).</summary>
    private static string ParameterType(FunctionSymbol f, KurzType type) => f.TakesC ? Lower.CType(type) : Ir(type);

    /// <summary>Returns a value of <paramref name="type"/> from the function being emitted, converted to its result type (T1).</summary>
    private void EmitReturn(string? value, KurzType type, int line)
    {
        if (Types.IsVoid(returnType))
        {
            fn.Return(null);
            return;
        }

        var converted = Convert(value ?? throw new InvalidOperationException($"a return without a value at line {line}"), type, returnType, line);
        fn.Return($"{Ir(returnType)} {converted}");
    }

    // ---- statements ------------------------------------------------------------------------------

    private void EmitBlock(Block block)
    {
        foreach (var s in block.Statements)
        {
            if (fn.Terminated)
            {
                return; // what follows a return, a break or a throw never runs
            }

            EmitStatement(s);
        }
    }

    /// <summary>A type the emitter does not lay out, met anywhere inside the statement, is the one diagnostic, at the statement's line.</summary>
    private void EmitStatement(Statement statement)
    {
        try
        {
            EmitStatementCore(statement);
        }
        catch (NotSupportedException e)
        {
            throw Unsupported(statement.Line, e.Message);
        }
    }

    private void EmitStatementCore(Statement statement)
    {
        switch (statement)
        {
            case Block b:
                EmitBlock(b);
                break;
            case RawBlock r:
                EmitBlock(r.Body); // R7: a `raw` block lifts the checker's rule and adds no code of its own
                break;
            case VariableDeclaration v when (v.Modifiers & Modifiers.Static) != 0:
                break; // R13: a global of the module, defined by Register
            case VariableDeclaration v:
                {
                    var variable = Model.DeclarationOf(v);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(v.Value ?? throw new InvalidOperationException($"a variable without a value at line {v.Line}"));
                    var slot = fn.Alloca(Ir(type), v.Name);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(v.Value), type, v.Value.Line)}, ptr {slot}");
                    slots[variable] = slot;
                    break;
                }

            case AssignOrDeclare a when Model.Declares(a):
                {
                    var variable = Model.DeclarationOf(a);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(a.Value);
                    var slot = fn.Alloca(Ir(type), a.Name);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(a.Value), type, a.Value.Line)}, ptr {slot}");
                    slots[variable] = slot;
                    break;
                }

            case AssignOrDeclare a:
                {
                    var variable = Model.AssignedVariableOf(a);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(a.Value);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(a.Value), type, a.Value.Line)}, ptr {Place(variable, a.Line)}");
                    break;
                }

            case Assignment a:
                EmitAssignment(a);
                break;
            case ExpressionStatement e:
                EmitExpressionStatement(e);
                break;
            case IfStatement i:
                EmitIf(i);
                break;
            case WhileStatement w:
                {
                    var check = fn.Label("while");
                    var body = fn.Label("do");
                    var exit = fn.Label("done");
                    fn.Block(check);
                    var condition = EmitExpression(w.Condition);
                    fn.CondBranch(condition, body, exit);
                    fn.Block(body);
                    loops.Push((exit, check));
                    EmitBlock(w.Body);
                    loops.Pop();
                    if (!fn.Terminated)
                    {
                        fn.Branch(check);
                    }

                    fn.Block(exit);
                    break;
                }

            case ForInStatement f:
                EmitForIn(f);
                break;
            case BreakStatement:
                fn.Branch(loops.Peek().Break);
                break;
            case ContinueStatement:
                fn.Branch(loops.Peek().Continue);
                break;
            case ReturnStatement r:
                if (r.Value is null)
                {
                    EmitReturn(null, Types.Void, r.Line);
                }
                else
                {
                    var value = EmitExpression(r.Value);
                    EmitReturn(value, TypeOf(r.Value), r.Value.Line);
                }

                break;
            case ThrowStatement t:
                EmitThrow(t);
                break;
            case FunctionDeclaration or ExternBlock or UseStatement:
                break; // declared at the top level and emitted when called; a `use` brings in a folder for the checker alone
            default:
                throw Unsupported(statement.Line, StatementName(statement));
        }
    }

    private static string StatementName(Statement statement) => statement switch
    {
        MatchStatement => "`match` (C4)",
        AsmStatement => "inline assembly (R11)",
        _ => $"the statement `{statement.GetType().Name}`",
    };

    private void EmitExpressionStatement(ExpressionStatement e)
    {
        switch (Unwrap(e.Expression))
        {
            case CallExpression c:
                EmitCall(c);
                break;
            default:
                throw Unsupported(e.Line, ExpressionName(e.Expression));
        }
    }

    private static Expression Unwrap(Expression e) => e is ParenthesizedExpression p ? Unwrap(p.Inner) : e;

    private void EmitIf(IfStatement i)
    {
        var then = fn.Label("then");
        var otherwise = fn.Label("else");
        var exit = fn.Label("endif");
        var condition = EmitExpression(i.Condition);
        fn.CondBranch(condition, then, i.Else is null ? exit : otherwise);
        fn.Block(then);
        EmitBlock(i.Then);
        if (!fn.Terminated)
        {
            fn.Branch(exit);
        }

        if (i.Else is not null)
        {
            fn.Block(otherwise);
            EmitStatement(i.Else);
            if (!fn.Terminated)
            {
                fn.Branch(exit);
            }
        }

        fn.Block(exit);
    }

    /// <summary>A <c>throw</c> (O6): the text of the value and the place of the throw to the runtime's <c>Throw</c>, which ends the process.</summary>
    private void EmitThrow(ThrowStatement t)
    {
        var value = t.Value ?? throw Unsupported(t.Line, "a `throw` without a value, in an arm of `else` (O7)");
        var text = TextOf(EmitExpression(value), TypeOf(value), t.Line);
        fn.Emit($"call void {RuntimeFunction("Throw")}(%kz.str {text}, %kz.str {StringValue(unit.File)}, i32 {Number(t.Line)})");
        fn.Unreachable();
    }

    /// <summary>An exception of the run-time table (chapter 12) at its line of the file being emitted: the runtime's <c>Raise</c>, which ends the process.</summary>
    private void Raise(string id, int line)
    {
        fn.Emit($"call void {RuntimeFunction("Raise")}(%kz.str {StringValue(id)}, %kz.str {StringValue(unit.File)}, i32 {Number(line)})");
        fn.Unreachable();
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>C8, C6: a range runs its numbers in order, both ends read once, an end below the start the exception before the first round; a `for` over anything else is not emitted.</summary>
    private void EmitForIn(ForInStatement f)
    {
        if (Unwrap(f.Collection) is not RangeExpression range)
        {
            throw Unsupported(f.Collection.Line, $"a `for` over a `{TypeOf(f.Collection)}` (C6)");
        }

        var variable = Model.DeclarationOf(f);
        var elementType = Sub(variable.Type);
        var slot = fn.Alloca(Ir(elementType), f.Variable);
        slots[variable] = slot;
        var body = fn.Label("for");
        var next = fn.Label("next");
        var exit = fn.Label("endfor");
        var start = Convert(EmitExpression(range.Start), TypeOf(range.Start), elementType, range.Start.Line);
        var end = Convert(EmitExpression(range.End), TypeOf(range.End), elementType, range.End.Line);
        var ir = Ir(elementType);
        var signed = Types.IsSigned(elementType);
        var reversed = fn.Value($"icmp {(signed ? "slt" : "ult")} {ir} {end}, {start}");
        var fail = fn.Label("reversed");
        var go = fn.Label("range");
        fn.CondBranch(reversed, fail, go);
        fn.Block(fail);
        Raise(ErrorIds.ReversedRangeAtRunTime, range.Line);
        fn.Block(go);
        var counter = fn.Alloca(ir, "i");
        fn.Emit($"store {ir} {start}, ptr {counter}");
        if (range.Inclusive)
        {
            fn.Block(body);
            var i = fn.Value($"load {ir}, ptr {counter}");
            fn.Emit($"store {ir} {i}, ptr {slot}");
            loops.Push((exit, next));
            EmitBlock(f.Body);
            loops.Pop();
            fn.Block(next);
            var last = fn.Value($"load {ir}, ptr {counter}");
            var done = fn.Value($"icmp eq {ir} {last}, {end}");
            var step = fn.Label("step");
            fn.CondBranch(done, exit, step);
            fn.Block(step);
            var plus = fn.Value($"add {ir} {last}, 1");
            fn.Emit($"store {ir} {plus}, ptr {counter}");
            fn.Branch(body);
        }
        else
        {
            var check = fn.Label("check");
            fn.Block(check);
            var i = fn.Value($"load {ir}, ptr {counter}");
            var more = fn.Value($"icmp {(signed ? "slt" : "ult")} {ir} {i}, {end}");
            fn.CondBranch(more, body, exit);
            fn.Block(body);
            fn.Emit($"store {ir} {i}, ptr {slot}");
            loops.Push((exit, next));
            EmitBlock(f.Body);
            loops.Pop();
            fn.Block(next);
            var again = fn.Value($"load {ir}, ptr {counter}");
            var plus = fn.Value($"add {ir} {again}, 1");
            fn.Emit($"store {ir} {plus}, ptr {counter}");
            fn.Branch(check);
        }

        fn.Block(exit);
    }

    /// <summary>`x = v`, `*p = v` and `p[i] = v` (R10); an assignment to a field is not emitted.</summary>
    private void EmitAssignment(Assignment a)
    {
        var targetType = TypeOf(a.Target);
        string place;
        switch (Unwrap(a.Target))
        {
            case NameExpression n:
                place = Place(Model.VariableOf(n), n.Line);
                break;
            case UnaryExpression { Operator: TokenKind.Star } u:
                place = EmitExpression(u.Operand);
                break;
            case IndexExpression i when TypeOf(i.Target) is PointerType p:
                place = ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index);
                break;
            default:
                throw Unsupported(a.Line, $"an assignment to {ExpressionName(a.Target)}");
        }

        var value = Convert(EmitExpression(a.Value), TypeOf(a.Value), targetType, a.Value.Line);
        fn.Emit($"store {Ir(targetType)} {value}, ptr {place}");
    }

    /// <summary>Where a variable lives: its slot in the function, or the global of state outside a function (R13).</summary>
    private string Place(VariableSymbol variable, int line) =>
        slots.TryGetValue(variable, out var slot) ? slot
        : statics.TryGetValue(variable, out var global) ? global
        : throw new InvalidOperationException($"`{variable.Name}` has no storage at {unit.File}:{line}");
}
