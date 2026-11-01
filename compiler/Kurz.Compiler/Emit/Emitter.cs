using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The IR of a program with its runtime, the libraries its C functions come from (R16), which the link names, whether it is a release
/// build, which the backend optimises, whether it carries line tables, as a test build and a release build with <c>--lines</c> do, and
/// clang's option for the CPU the build names, null for the target's baseline (section 2).
/// </summary>
public sealed record EmittedProgram(string Ir, IReadOnlyList<string> Libraries, bool Release, bool Lines, string? CpuFlag = null);

/// <summary>
/// The code generator: LLVM IR, as text, for a program the checker read without an error and without <c>not supported</c>, from
/// the syntax tree and the checker's <see cref="SemanticModel"/>, together with the runtime of the target it is built for and the
/// standard library, which are Kurz too and are emitted into the same module from their own trees and models (design record,
/// section 2): the program's top-level
/// code is the function <c>kz.main</c>, and the module's <c>main</c>, which the C library's start calls, calls the runtime's
/// <c>Start</c>, then <c>kz.main</c>, then the runtime's <c>Finish</c>, the runtime's one exit. It emits the integers,
/// <c>bool</c>, <c>char</c> and <c>string</c>, functions and their generic instances, the statements of chapter 10, <c>throw</c>,
/// and the raw forms of chapter 11 the runtime is written in (<see cref="Lower.Supported"/>); a program that uses another type, or a
/// construct the emitter does not lay out, gets one <c>not supported</c> diagnostic and no IR. In a test build the arithmetic of T4
/// raises <c>overflow</c> (T5) and the backend does not optimise, as Rust's dev profile, so that every call is a frame; in a release build
/// it wraps and the optimiser runs. A test build, and a release build with <c>--lines</c>, carry line tables (<see cref="DebugInfo"/>). A
/// raise by code passes its file and its line to the runtime as constants (design record, section 6), and so does a <c>print</c>, for
/// <c>print-failed</c> (the owner, 2026-10-23, "Caller passes it").
/// </summary>
public sealed partial class Emitter
{
    private static readonly IReadOnlyDictionary<TypeParameterSymbol, KurzType> NoMap = new Dictionary<TypeParameterSymbol, KurzType>();

    private readonly bool test;
    private readonly bool lines;
    private readonly IrModule module;
    private readonly Unit program;
    private readonly Unit common;
    private readonly Unit sys;
    private readonly Unit std;
    private readonly Target target;
    private readonly IReadOnlyList<Unit> units;
    private readonly Dictionary<FunctionSymbol, Unit> owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FunctionSymbol, string?> cLibraries = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VariableSymbol, string> statics = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> instances = new(StringComparer.Ordinal);
    private readonly Queue<(FunctionSymbol Function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map, string Name, System.Collections.Immutable.ImmutableSortedSet<string> Features)> pending = new();
    private readonly List<IrFunction> emitted = [];
    private readonly SortedSet<string> libraries = new(StringComparer.Ordinal);
    private readonly Dictionary<DataSymbol, Unit> dataOwners = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Unit Unit, VariableDeclaration Declaration)> state = [];
    private readonly HashSet<string> helpers = new(StringComparer.Ordinal);
    private readonly Layout layout;

    // the function being emitted, and the file it is in
    private Unit unit;
    private IrFunction fn = null!;
    private string display = "main"; // the function being emitted as a trace names it, which names a lambda in it
    private Dictionary<VariableSymbol, string> slots = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<TypeParameterSymbol, KurzType> map = NoMap;
    private KurzType returnType = Types.Void;
    private readonly Stack<(string Break, string Continue, int Regions, int Scopes)> loops = new();

    /// <summary>
    /// A file the module holds code of: the program, a folder of the runtime or the standard library, with the prefix of its functions' IR
    /// names; <c>Runtime</c> marks the compiler's own code, the runtime's and the standard library's.
    /// </summary>
    private sealed record Unit(string Prefix, string File, ProgramSyntax Tree, SemanticModel Model, bool Runtime);

    private Emitter(Unit program, Unit common, Unit sys, Unit std, bool test, bool lines, Target target, Cpu cpu)
    {
        this.cpu = cpu;
        this.program = program;
        this.common = common;
        this.sys = sys;
        this.std = std;
        this.target = target;
        units = [program, common, sys, std];
        unit = program;
        this.test = test;
        this.lines = test || lines;
        module = new IrModule
        {
            Debug = this.lines ? new DebugInfo(System.Environment.CurrentDirectory, program.File, $"kurz {Command.CompilerVersion}", !test, target == Target.Windows) : null,
            InlineProbe = target != Target.Windows,
        };
        layout = new Layout(module);
    }

    private SemanticModel Model => unit.Model;

    /// <summary>
    /// The IR of the program and of the runtime checked for its target, or null with one <c>not supported</c> diagnostic added
    /// when the program uses what the emitter does not lay out. <paramref name="checker"/> is the checker that read the program,
    /// with no error and no <c>not supported</c> among its diagnostics; <paramref name="file"/> is the name of the program's
    /// file as a compile error names it (design record, section 2), which the error line of an exception names (section 6);
    /// <paramref name="lines"/> keeps the line tables in a release build (<c>--lines</c>), which a test build carries in any case;
    /// <paramref name="cpu"/> is the CPU the build names (section 2), the target's baseline where it names none.
    /// </summary>
    public static EmittedProgram? Emit(ProgramSyntax tree, Checker checker, CheckedRuntime runtime, string file, bool testBuild, DiagnosticBag diagnostics, bool lines = false, Cpu? cpu = null)
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
            new Unit("std", runtime.StdPath, runtime.Std, runtime.StdChecker.Model, true),
            testBuild,
            lines,
            runtime.Target,
            cpu ?? Cpu.Baseline);
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
        RefuseArmTests();
        foreach (var u in units)
        {
            Register(u);
        }

        layout.Register(dataOwners.Keys);
        foreach (var (u, v) in state)
        {
            unit = u;
            DefineStatic(v); // after the layout knows which data types are blocks
        }

        unit = program;
        Begin(new IrFunction("internal void @kz.main()"), NoMap, Types.Void);
        Trace("main", "kz.main", 1);
        InScope(() =>
        {
            if (Model.Arguments is { Read: true } arguments)
            {
                // S13: `args`, made before the first statement, its refusal with no place (the owner, 2026-10-30, "Refused, as Rust")
                var slot = fn.Alloca("ptr", "args");
                fn.Emit($"store ptr {EmitArguments(1, 0, Model.ArgumentsLine)}, ptr {slot}");
                slots[arguments] = slot;
                Declare(slot, arguments.Type);
            }

            EmitStatements(program.Tree.Statements);
        });

        if (!fn.Terminated)
        {
            fn.Return(null);
        }

        End();
        var start = RuntimeFunction("Start");
        var finish = RuntimeFunction("Finish");
        while (pending.Count > 0 || lambdas.Count > 0)
        {
            if (pending.Count > 0)
            {
                var (function, instanceMap, name, compiled) = pending.Dequeue();
                features = compiled;
                EmitFunction(function, instanceMap, name);
            }
            else
            {
                EmitLambdaFunction(lambdas.Dequeue());
            }
        }

        if (released.Count > 0)
        {
            EmitDrain(); // K2 with section 3: the work lists of the instances released
        }

        var main = new IrFunction("i32 @main(i32 %argc, ptr %argv)");
        if (CpuRead() is { } read)
        {
            main.Emit($"call void {read}()"); // R18: the CPU's word, read once before the runtime starts, for every test that reads it
        }

        main.Emit($"call void {start}(i32 %argc, ptr %argv)");
        main.Emit("call void @kz.main()");
        main.Emit($"call void {finish}()");
        main.Unreachable();
        module.Add(main);
        foreach (var f in emitted)
        {
            module.Add(f);
        }

        return new EmittedProgram(module.Render(), libraries.ToList(), !test, lines, cpu.Name is null ? null : Clang.CpuFlag(cpu.Name, cpu.X86));
    }

    /// <summary>The functions, the C declarations, the data types and the state outside a function of a file (R13, R16), each by its symbol.</summary>
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
                    state.Add((u, v));
                    break;
                case TypeDeclaration { Kind: TypeDeclarationKind.Data } t:
                    dataOwners[u.Model.DataTypeOf(t)] = u; // the checker reads a data type and an enum, whose values are constants, and says `not supported` to the others
                    break;
                case TypeDeclaration { Kind: TypeDeclarationKind.Class } t:
                    {
                        var symbol = u.Model.ClassOf(t);
                        classOwners[symbol] = u;
                        foreach (var method in symbol.Methods.Values.SelectMany(m => m))
                        {
                            owners[method] = u; // K7: emitted when called, as a function is
                        }

                        break;
                    }
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
        var initial = v.Value is null ? Zero(type) : StaticConstant(Model.TypeOf(v.Value), type, v.Line);
        var thread = (v.Modifiers & Modifiers.Thread) != 0 ? "thread_local " : string.Empty;
        module.Global($"{name} = internal {thread}global {Ir(type)} {initial}");
        statics[variable] = name;
    }

    /// <summary>The constant a static starts at, as its type holds it: a data value without fields as the block of its own type, which a block of its base holds (D6, D8).</summary>
    private string StaticConstant(Typed value, KurzType type, int line)
    {
        var constant = value.Constant ?? throw new InvalidOperationException($"state without a constant at {unit.File}:{line}");
        return Constant(constant, value.Type is DataType && type is DataType ? Sub(value.Type) : type, line);
    }

    private string Zero(KurzType type) => type is UnionType u && layout.Niche(u) is { } niche ? niche : Ir(type) switch
    {
        "ptr" => "null",
        "i1" => "false",
        var ir when ir.StartsWith('i') => "0",
        _ => "zeroinitializer",
    };

    private void Begin(IrFunction function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, KurzType result)
    {
        fn = function;
        slots = new Dictionary<VariableSymbol, string>(ReferenceEqualityComparer.Instance);
        map = instanceMap;
        returnType = result;
        loops.Clear();
        regions = [];
        scopes = [];
        outline = null; // a guarded block's function says what it stands in after it began (R18)
    }

    private void End() => emitted.Add(fn);

    /// <summary>
    /// Gives the function being emitted its subprogram in the line tables, under the name a trace shows (design record, section 6:
    /// <c>main</c> for the top-level code, the owner's "Type.Method, Max&lt;int&gt;, std path" of 2026-10-29 for the rest), at its line.
    /// </summary>
    private void Trace(string name, string symbol, int line)
    {
        display = name;
        if (module.Debug is { } debug)
        {
            fn.Subprogram = debug.Subprogram(name, symbol, unit.File, line);
            At(line);
        }
    }

    /// <summary>The line of the instructions emitted from here on, in a function with a subprogram.</summary>
    private void At(int line)
    {
        if (fn.Subprogram is { } scope)
        {
            fn.Location = module.Debug!.Location(line, scope);
        }
    }

    /// <summary>The name a trace shows for a function with these type arguments: <c>Max&lt;int&gt;</c>.</summary>
    private static string TraceName(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap) =>
        (f.Owner is { } c ? c.Name + "." : string.Empty)
        + (f.TypeParameters.Count == 0 ? f.Name : $"{f.Name}<{string.Join(", ", f.TypeParameters.Select(p => instanceMap[p].Text))}>");

    /// <summary>The symbol of an IR name, without its <c>@</c> and its quotes.</summary>
    private static string Symbol(string name) => name.TrimStart('@').Trim('"');

    // ---- types -----------------------------------------------------------------------------------

    /// <summary>The type with the type arguments of the function being emitted put in.</summary>
    private KurzType Sub(KurzType type) => map.Count == 0 ? type : Types.Substitute(type, map);

    private KurzType TypeOf(Expression e) => Sub(Model.TypeOf(e).Type);

    private string Ir(KurzType type) => layout.Ir(type);

    private void RequireSupported(KurzType type, int line)
    {
        if (layout.Refusal(type) is { } refusal)
        {
            throw Unsupported(line, refusal);
        }
    }

    // ---- functions -------------------------------------------------------------------------------

    /// <summary>
    /// The IR name of a function for these type arguments, queued for emission on first use: compiled for the sets of the function
    /// being emitted, a copy where it has sets beyond the CPU's (R18), or for <paramref name="compiled"/>, none for a function value.
    /// </summary>
    private string Instance(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, System.Collections.Immutable.ImmutableSortedSet<string>? compiled = null)
    {
        var owner = owners.TryGetValue(f, out var u) ? u : throw new InvalidOperationException($"`{f.Name}` belongs to no file of the module");
        var arguments = f.TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", f.TypeParameters.Select(p => Lower.Mangle(instanceMap[p]))) + ">";
        var sets = compiled ?? features;
        var key = $"{owner.Prefix}.{(f.Owner is { } c ? c.Name + "." : string.Empty)}{f.Name}{arguments}.{f.Line.ToString(CultureInfo.InvariantCulture)}{FeatureSuffix(sets)}";
        if (!instances.TryGetValue(key, out var name))
        {
            name = $"@\"{key}\"";
            instances[key] = name;
            pending.Enqueue((f, instanceMap, name, sets));
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
            // the runtime and the standard library are the compiler's own code: a construct of them the emitter does not lay out is a defect of the compiler
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
        if (f.Owner is not null)
        {
            parameters.Add("ptr %this"); // K7: the instance, borrowed from the caller
        }

        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var type = Types.Substitute(p.Type, instanceMap);
            RequireSupported(type, p.Line);
            parameters.Add(p.Mut ? $"ptr %p{i}" : $"{ParameterType(f, type)} %p{i}");
        }

        Begin(new IrFunction($"internal {ParameterType(f, result)} {name}({string.Join(", ", parameters)})", FeatureAttributes(features)), instanceMap, result);
        Trace(unit.Prefix.StartsWith("rt.", StringComparison.Ordinal) ? Symbol(name) : TraceName(f, instanceMap), Symbol(name), d.Line);
        if (f.Owner is not null)
        {
            var self = fn.Alloca("ptr", "this");
            fn.Emit($"store ptr %this, ptr {self}");
            slots[Model.ThisOf(f)] = self;
        }

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

        EmitBody(d.ExpressionBody, d.BlockBody, d.Line);
        End();
    }

    /// <summary>The body of a function or a lambda, which returns from the function being emitted.</summary>
    private void EmitBody(Expression? expressionBody, Block? blockBody, int line)
    {
        if (expressionBody is not null && Types.IsVoid(TypeOf(expressionBody)))
        {
            InRegion(() => EmitEffect(expressionBody, expressionBody.Line)); // a call with a `void` success, as a `void` lambda's body is (F11)
            if (!fn.Terminated)
            {
                EmitReturn(null, Types.Void, line);
            }
        }
        else if (expressionBody is not null)
        {
            // the value of a `void` function's body is no result: it stays a temporary, which the return lets go of (F4)
            InRegion(() => EmitReturn(Types.IsVoid(returnType) ? EmitExpression(expressionBody) : Owned(expressionBody), TypeOf(expressionBody), expressionBody.Line));
        }
        else
        {
            EmitBlock(blockBody!);
            if (!fn.Terminated)
            {
                if (Types.IsVoid(returnType) || returnType is UnionType { Cases: [VoidType, ..] })
                {
                    EmitReturn(null, Types.Void, line); // the end of the body returns the success of a function without a value (F4, O10)
                }
                else
                {
                    fn.Unreachable(); // the checker reported missing-return for every other result (F3)
                }
            }
        }
    }

    /// <summary>A parameter's or a result's type in a function's signature: of the C calling convention for a function written <c>extern "C"</c> (R16).</summary>
    private string ParameterType(FunctionSymbol f, KurzType type) => f.TakesC ? Lower.CType(type) : Ir(type);

    /// <summary>
    /// Returns a value of <paramref name="type"/>, which the result holds, from the function being emitted, converted to its result
    /// type (T1); without a value, the success of a function whose success is <c>void</c> (O10). The function lets go of its
    /// temporaries and its variables first.
    /// </summary>
    private void EmitReturn(string? value, KurzType type, int line)
    {
        if (Types.IsVoid(returnType))
        {
            Leaving(0, 0);
            ReturnRaw(null);
            return;
        }

        if (value is null && returnType is UnionType { Cases: [VoidType, ..] } union)
        {
            Leaving(0, 0);
            ReturnRaw(MakeUnion(union, Types.Void, null));
            return;
        }

        var converted = Convert(value ?? throw new InvalidOperationException($"a return without a value at line {line}"), type, returnType, line);
        Leaving(0, 0);
        ReturnRaw(converted);
    }

    // ---- statements ------------------------------------------------------------------------------

    private void EmitBlock(Block block) => InScope(() => EmitStatements(block.Statements));

    /// <summary>
    /// A type the emitter does not lay out, met anywhere inside the statement, is the one diagnostic, at the statement's line. Its
    /// instructions carry its line in the line tables, and what the statement around it emits after it carries that one's again, as
    /// <see cref="EmitExpression"/> does.
    /// </summary>
    private void EmitStatement(Statement statement)
    {
        if (Model.GuardsAt(statement).Any(c => Known(c) == false))
        {
            fn.Unreachable(); // R18: a test showed a set the target cannot have, so the statement never runs
            return;
        }

        var f = fn;
        var around = f.Location;
        At(statement.Line);
        try
        {
            InRegion(() => EmitStatementCore(statement));
        }
        catch (NotSupportedException e)
        {
            throw Unsupported(statement.Line, e.Message);
        }
        finally
        {
            f.Location = around;
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
                    var value = Owned(v.Value ?? throw new InvalidOperationException($"a variable without a value at line {v.Line}"));
                    var slot = fn.Alloca(Ir(type), v.Name);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(v.Value), type, v.Value.Line)}, ptr {slot}");
                    slots[variable] = slot;
                    Declare(slot, type);
                    break;
                }

            case AssignOrDeclare a when Model.IsImplicitField(a, out _, out var field):
                EmitFieldAssignment(a.Value, field!.Type, () => (This(a, a.Line), ClassOfThis(a, a.Line).Symbol), field.Name); // K7
                break;
            case AssignOrDeclare a when Model.Declares(a):
                {
                    var variable = Model.DeclarationOf(a);
                    var type = Sub(variable.Type);
                    var value = Owned(a.Value);
                    var slot = fn.Alloca(Ir(type), a.Name);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(a.Value), type, a.Value.Line)}, ptr {slot}");
                    slots[variable] = slot;
                    Declare(slot, type);
                    break;
                }

            case AssignOrDeclare a:
                {
                    var variable = Model.AssignedVariableOf(a);
                    var type = Sub(variable.Type);
                    var value = Owned(a.Value);
                    Store(Convert(value, TypeOf(a.Value), type, a.Value.Line), type, Place(variable, a.Line));
                    break;
                }

            case Assignment a:
                EmitAssignment(a);
                break;
            case ExpressionStatement e:
                EmitEffect(e.Expression, e.Line);
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
                    string condition = null!;
                    InRegion(() => condition = EmitExpression(w.Condition)); // each round lets go of what its test made
                    fn.CondBranch(condition, body, exit);
                    fn.Block(body);
                    loops.Push((exit, check, regions.Count, scopes.Count));
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
                Leaving(loops.Peek().Regions, loops.Peek().Scopes);
                fn.Branch(loops.Peek().Break);
                break;
            case ContinueStatement:
                Leaving(loops.Peek().Regions, loops.Peek().Scopes);
                fn.Branch(loops.Peek().Continue);
                break;
            case ReturnStatement r:
                if (r.Value is null)
                {
                    EmitReturn(null, Types.Void, r.Line);
                }
                else
                {
                    var value = Owned(r.Value);
                    EmitReturn(value, TypeOf(r.Value), r.Value.Line);
                }

                break;
            case ThrowStatement t:
                EmitThrow(t);
                break;
            case MatchStatement m:
                EmitMatch(m.Subject, m.Arms, null);
                break;
            case FunctionDeclaration or ExternBlock or UseStatement or TypeDeclaration:
                break; // declared at the top level and emitted when called, a data type laid out where a value of it is made; a `use` brings in a folder for the checker alone
            default:
                throw Unsupported(statement.Line, StatementName(statement));
        }
    }

    private static string StatementName(Statement statement) => statement switch
    {
        AsmStatement => "inline assembly (R11)",
        _ => $"the statement `{statement.GetType().Name}`",
    };

    /// <summary>An expression that stands as a statement: a call, an <c>else</c> or a <c>match</c>, run for what it does.</summary>
    private void EmitEffect(Expression expression, int line)
    {
        switch (Unwrap(expression))
        {
            case CallExpression c:
                if (EmitCall(c) is { } value)
                {
                    Temp(value, TypeOf(c)); // a result nothing holds: the statement's end lets go of it
                }

                break;
            case ElseExpression el:
                EmitElse(el, asValue: false);
                break;
            case MatchExpression m:
                EmitMatch(m.Subject, m.Arms, null);
                break;
            default:
                throw Unsupported(line, ExpressionName(expression));
        }
    }

    private static Expression Unwrap(Expression e) => e is ParenthesizedExpression p ? Unwrap(p.Inner) : e;

    private void EmitIf(IfStatement i)
    {
        if (ConstantTest(i.Condition) is bool known)
        {
            // R18: a test the CPU or the function's sets answer; the branch it skips is not compiled
            if (known)
            {
                EmitBlock(i.Then);
            }
            else if (i.Else is not null)
            {
                EmitStatements([i.Else]);
            }

            return;
        }

        var then = fn.Label("then");
        var otherwise = fn.Label("else");
        var exit = fn.Label("endif");
        string condition = null!;
        InRegion(() => condition = EmitExpression(i.Condition));
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
            EmitStatements([i.Else]); // R18: an `else if` that a test guards runs as a guarded block
            if (!fn.Terminated)
            {
                fn.Branch(exit);
            }
        }

        fn.Block(exit);
    }

    /// <summary>A <c>throw</c> (O6): the text of the value and the place of the throw to the runtime's <c>Throw</c>, which ends the process; a bare <c>throw</c> in an arm of <c>else</c> throws the case value that reached the arm (O7).</summary>
    private void EmitThrow(ThrowStatement t)
    {
        var (value, type) = t.Value is null
            ? elseCase ?? throw new InvalidOperationException($"a bare `throw` outside an arm of `else` at line {t.Line}: the checker reports it")
            : (EmitExpression(t.Value), TypeOf(t.Value));
        var text = TextOf(value, type, t.Line);
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

    /// <summary>C8, C6: a range runs its numbers in order, both ends read once, an end below the start the exception before the first round; a list its items (<see cref="EmitForInList"/>); a `for` over anything else is not emitted.</summary>
    private void EmitForIn(ForInStatement f)
    {
        if (TypeOf(f.Collection) is ListType list)
        {
            EmitForInList(f, list);
            return;
        }

        if (TypeOf(f.Collection) is MapType map)
        {
            EmitForInMap(f, map);
            return;
        }

        if (Unwrap(f.Collection) is not RangeExpression range)
        {
            throw Unsupported(f.Collection.Line, $"a `for` over {Types.A(TypeOf(f.Collection))} (C6)");
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
            loops.Push((exit, next, regions.Count, scopes.Count));
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
            loops.Push((exit, next, regions.Count, scopes.Count));
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

    /// <summary>`x = v`, `*p = v` and `p[i] = v` (R10), and an assignment into a path of a `mut` variable (M4).</summary>
    private void EmitAssignment(Assignment a)
    {
        var targetType = TypeOf(a.Target);
        string place;
        switch (Unwrap(a.Target))
        {
            case MemberAccess m when TypeOf(m.Target) is ClassType c:
                EmitFieldAssignment(a.Value, targetType, () => (EmitExpression(m.Target), c.Symbol), m.Member); // K2, K9
                return;
            case MemberAccess:
                EmitPathAssignment(a);
                return;
            case NameExpression n:
                place = Place(Model.VariableOf(n), n.Line);
                break;
            case UnaryExpression { Operator: TokenKind.Star } u:
                place = EmitExpression(u.Operand);
                break;
            case IndexExpression i when TypeOf(i.Target) is PointerType p:
                place = ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index);
                break;
            case IndexExpression i when TypeOf(i.Target) is MapType map:
                EmitMapWrite(a, i, map);
                return;
            case IndexExpression i when TypeOf(i.Target) is ListType list:
                EmitListWrite(a, i, list);
                return;
            default:
                throw Unsupported(a.Line, $"an assignment to {ExpressionName(a.Target)}");
        }

        var value = Convert(Owned(a.Value), TypeOf(a.Value), targetType, a.Value.Line);
        Store(value, targetType, place);
    }

    /// <summary>A value stored into a place that held one of the type: the place lets go of the old value after it holds the new one.</summary>
    private void Store(string value, KurzType type, string place)
    {
        var old = Counted(type) ? fn.Value($"load {Ir(type)}, ptr {place}") : null;
        fn.Emit($"store {Ir(type)} {value}, ptr {place}");
        if (old is not null)
        {
            Release(old, type);
        }
    }

    /// <summary>Where a variable lives: its slot in the function, the address a guarded block's function takes of it (R18), or the global of state outside a function (R13).</summary>
    private string Place(VariableSymbol variable, int line) =>
        slots.TryGetValue(variable, out var slot) ? slot
        : outline is not null && Captured(outline, slots, variable) is { } captured ? captured // R18: a variable of the function a guarded block stands in
        : statics.TryGetValue(variable, out var global) ? global
        : throw new InvalidOperationException($"`{variable.Name}` has no storage at {unit.File}:{line}");
}
