using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The CPU a build is for (design record, section 2): the name the build option gives, null for the target's baseline, which is
/// clang's own default; the classes of intrinsics it has, whose tests are <c>true</c> everywhere (R18); the features clang's driver
/// passes for it, which a function compiled for more sets carries in front of theirs, since a function's features take the place
/// of the module's; and whether it is an x86-64 CPU, the one architecture whose intrinsics the compiler builds.
/// </summary>
public sealed record Cpu(string? Name, IReadOnlyList<string> Classes, IReadOnlyList<string> Features, bool X86 = true)
{
    /// <summary>x86-64's baseline, which every x86-64 CPU has: SSE and SSE2.</summary>
    public static readonly Cpu Baseline = new(null, ["X86.X86Base", "X86.Sse", "X86.Sse2"], []);

    /// <summary>The baseline of a 64-bit Arm machine, which has no class under <c>X86</c> and whose own classes the compiler does not build.</summary>
    public static readonly Cpu ArmBaseline = new(null, [], [], X86: false);
}

/// <summary>
/// R18: intrinsics and the blocks their tests guard. A test of a class's <c>IsSupported</c> is <c>true</c> where the build's CPU or the
/// function being emitted has the class's set, <c>false</c> for a class under <c>Arm</c> on x86-64, and otherwise a read of the CPU's
/// word, which the module's <c>main</c> reads once, by <c>cpuid</c>, before the runtime starts (section 13, as Rust's
/// <c>std_detect</c> reads leaf 1 and leaf 7). A run of statements that a test guards for sets the function being emitted does not
/// have, and the right side of <c>&amp;&amp;</c> and <c>||</c> that one guards, is compiled as a function of its own for those sets,
/// called where the run stands (section 13, the owner, 2026-11-01, "Guarded block"); a function such a function calls directly is
/// compiled again for the same sets, the program's, the standard library's and the runtime's alike, while a call through a function
/// value is not copied (the owner, 2026-11-01, "Copied per feature", and the answer of the same day that the copy reaches every
/// function called from it). A block whose test the CPU or the sets answer is compiled alone, the branch the test skips left out; a
/// run guarded by a class the target cannot have is never reached. An intrinsic where the function being emitted lacks its set,
/// in a <c>raw</c> block, is a call of a function compiled for the set, as Rust's intrinsics are functions with their features.
/// </summary>
public sealed partial class Emitter
{
    private static readonly ImmutableSortedSet<string> NoFeatures = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);

    private readonly Cpu cpu;

    // the classes the function being emitted is compiled for beyond the CPU's, the function a guarded block is compiled out of, and
    // whether a test reads the CPU's word
    private ImmutableSortedSet<string> features = NoFeatures;
    private Outline? outline;
    private int guardedCount;
    private bool cpuRead;

    /// <summary>
    /// The function a guarded block's own function is compiled out of: its storage of the variables, the block it was itself compiled
    /// out of, and the variables the block reads there, each passed as the address of its storage.
    /// </summary>
    private sealed class Outline(Dictionary<VariableSymbol, string> slots, Outline? parent)
    {
        public Dictionary<VariableSymbol, string> Slots { get; } = slots;

        public Outline? Parent { get; } = parent;

        public List<(string Argument, string Parameter)> Captures { get; } = [];
    }

    /// <summary>Whether the build's CPU or the function being emitted has the class's set: a class that one of theirs is or derives from.</summary>
    private bool Has(IntrinsicClass c) =>
        cpu.X86 && c.IsX86 && cpu.Classes.Concat(features).Any(k => Intrinsics.Guards(Intrinsics.Classes[k], c));

    /// <summary>
    /// The answer of a test of the class known where it is emitted: <c>true</c> where the CPU or the function has its set, <c>false</c> for a
    /// class of the other architecture, null where the CPU's word answers it. A test of a class under <c>Arm</c> on an Arm machine is
    /// <c>not supported</c> before anything is emitted (<see cref="RefuseArmTests"/>).
    /// </summary>
    private bool? Known(IntrinsicClass c) => c.IsX86 != cpu.X86 ? false : Has(c) ? true : null;

    /// <summary>On an Arm machine, the first test of a class under <c>Arm</c> in the program is <c>not supported</c>: the compiler builds the intrinsics of x86-64 alone.</summary>
    private void RefuseArmTests()
    {
        if (!cpu.X86 && program.Model.SupportTests.Where(t => !t.Class.IsX86).OrderBy(t => t.Access.Line).FirstOrDefault() is { Access: { } access } test)
        {
            throw Unsupported(access.Line, $"a test of `{test.Class.Name}`, a class whose intrinsics the compiler builds for x86-64 alone (R18)");
        }
    }

    /// <summary>The answer of a condition made of tests alone, with <c>!</c>, <c>&amp;&amp;</c> and <c>||</c>, where it is known; null where it is not.</summary>
    private bool? ConstantTest(Expression e)
    {
        switch (Unwrap(e))
        {
            case MemberAccess m when Model.SupportTestOf(m) is { } c:
                return Known(c);
            case UnaryExpression { Operator: TokenKind.Bang } u:
                return !ConstantTest(u.Operand);
            case BinaryExpression { Operator: TokenKind.AmpersandAmpersand } b:
                return ConstantTest(b.Left) switch { false => false, true => ConstantTest(b.Right), null => null }; // T18: a left side `false` leaves the right unread
            case BinaryExpression { Operator: TokenKind.BarBar } b:
                return ConstantTest(b.Left) switch { true => true, false => ConstantTest(b.Right), null => null };
            default:
                return null;
        }
    }

    /// <summary>The value of <c>IsSupported</c>: its known answer, or the bits of the class and the classes it derives from in the CPU's word.</summary>
    private string EmitSupportTest(IntrinsicClass c)
    {
        if (Known(c) is bool known)
        {
            return known ? "true" : "false";
        }

        cpuRead = true;
        var mask = Number((long)Intrinsics.CpuWord(c));
        var word = fn.Value("load i64, ptr @kz.cpu");
        return fn.Value($"icmp eq i64 {fn.Value($"and i64 {word}, {mask}")}, {mask}");
    }

    /// <summary>The classes a test guards the node with that the function being emitted does not have: the sets a function of its own is compiled for.</summary>
    private List<IntrinsicClass> NewGuards(Node node) => Model.GuardsAt(node).Where(c => Known(c) != true).ToList();

    /// <summary>The statements of a block in order; from the first that a test guards for sets the function lacks, the rest as a guarded block's function.</summary>
    private void EmitStatements(IReadOnlyList<Statement> statements)
    {
        var clean = fn.UpperClean;
        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                if (fn.Terminated)
                {
                    return; // what follows a return, a break or a throw never runs
                }

                var guards = NewGuards(statements[i]);
                if (guards.Count > 0 && guards.All(c => Known(c) is null))
                {
                    var rest = statements.Skip(i).ToList();
                    EmitGuarded(guards, rest[0].Line, [], _ => EmitStatements(rest)); // a statement in the run that a test guards for still more sets runs in a function of its own again
                    return;
                }

                CleanUpperHalves(statements[i]);
                EmitStatement(statements[i]); // a run guarded by a class the target cannot have is never reached, which EmitStatement emits
            }
        }
        finally
        {
            fn.UpperClean = clean; // the code after the run may come from a path that did not clean them
        }
    }

    /// <summary>
    /// The right side of <c>&amp;&amp;</c> or <c>||</c> into <paramref name="slot"/>: in place, or, where a test on the left guards it for
    /// sets the function lacks, as a guarded block's function.
    /// </summary>
    private void EmitLogicalRight(Expression right, string slot)
    {
        var guards = NewGuards(right);
        if (guards.Count == 0)
        {
            InRegion(() => fn.Emit($"store i1 {EmitExpression(right)}, ptr {slot}")); // what the right side makes is let go of where it ran
        }
        else if (guards.Any(c => Known(c) == false))
        {
            fn.Unreachable(); // the left side showed a set the target cannot have
        }
        else
        {
            EmitGuarded(guards, right.Line, [slot], given => InRegion(() => fn.Emit($"store i1 {EmitExpression(right)}, ptr {given[0]}")));
        }
    }

    /// <summary>
    /// A guarded block as a function of its own, compiled for the sets of the function being emitted and <paramref name="guards"/>,
    /// and its call. The function takes the address of each variable it reads, of each value in <paramref name="pointers"/> and of the
    /// result, and gives back how it ended: 0 at its end, 1 at a <c>return</c>, whose value it stored, 2 at a <c>break</c> and 3 at a
    /// <c>continue</c> of the loop around the call, each of which the caller then takes, letting go of its own temporaries and variables
    /// as the block's function let go of the block's.
    /// </summary>
    private void EmitGuarded(IReadOnlyList<IntrinsicClass> guards, int line, IReadOnlyList<string> pointers, Action<IReadOnlyList<string>> body)
    {
        var compiled = features.Union(guards.Select(c => c.Name));
        var key = $"{unit.Prefix}.{display.Replace('"', '\'')}.guarded.{Number(line)}.{Number(++guardedCount)}";
        var name = $"@\"{key}\"";
        var hasResult = !Types.IsVoid(returnType);
        var (parentFn, parentSlots, parentLoops, parentRegions, parentScopes) = (fn, slots, loops.ToArray(), regions, scopes);
        var (parentElse, parentDisplay, parentOutline, parentFeatures) = (elseCase, display, outline, features);
        var context = new Outline(parentSlots, parentOutline);
        var parameters = new List<string>();
        var fallsThrough = true;
        Begin(new IrFunction(string.Empty, FeatureAttributes(compiled)), map, returnType);
        try
        {
            features = compiled;
            outline = context;
            Trace(parentDisplay, key, line); // the trace names the block's function as the function it stands in
            if (hasResult)
            {
                parameters.Add("ptr %result");
            }

            var given = pointers.Select((_, i) => $"%given{Number(i)}").ToList();
            parameters.AddRange(given.Select(g => $"ptr {g}"));
            if (parentElse is { } bare)
            {
                parameters.Add($"{Ir(bare.Type)} %case");
                elseCase = ("%case", bare.Type); // O7: a bare `throw` in the block throws the case its arm took
            }

            string? leaveBreak = null;
            string? leaveContinue = null;
            if (parentLoops.Length > 0)
            {
                leaveBreak = fn.Label("leave.break");
                leaveContinue = fn.Label("leave.continue");
                loops.Push((leaveBreak, leaveContinue, 0, 0));
            }

            InScope(() => body(given));
            fallsThrough = !fn.Terminated;
            if (fallsThrough)
            {
                fn.Return("i32 0");
            }

            if (leaveBreak is not null)
            {
                fn.Block(leaveBreak);
                fn.Return("i32 2");
                fn.Block(leaveContinue!);
                fn.Return("i32 3");
            }

            fn.Signature = $"internal i32 {name}({string.Join(", ", parameters.Concat(context.Captures.Select(c => $"ptr {c.Parameter}")))})";
            End();
        }
        finally
        {
            (fn, slots, regions, scopes) = (parentFn, parentSlots, parentRegions, parentScopes);
            loops.Clear();
            foreach (var loop in parentLoops.Reverse())
            {
                loops.Push(loop);
            }

            (elseCase, display, outline, features) = (parentElse, parentDisplay, parentOutline, parentFeatures);
        }

        var result = hasResult ? fn.Alloca(Ir(returnType), "guarded") : null;
        var arguments = new List<string>();
        if (result is not null)
        {
            arguments.Add($"ptr {result}");
        }

        arguments.AddRange(pointers.Select(p => $"ptr {p}"));
        if (elseCase is { } taken)
        {
            arguments.Add($"{Ir(taken.Type)} {taken.Value}");
        }

        arguments.AddRange(context.Captures.Select(c => $"ptr {c.Argument}"));
        var code = fn.Value($"call i32 {name}({string.Join(", ", arguments)})");
        var done = fn.Label("guarded.done");
        var leave = fn.Label("guarded.return");
        var broke = fn.Label("guarded.break");
        var continued = fn.Label("guarded.continue");
        var cases = $"i32 1, label %{leave}" + (loops.Count > 0 ? $" i32 2, label %{broke} i32 3, label %{continued}" : string.Empty);
        fn.Terminate($"switch i32 {code}, label %{done} [ {cases} ]");
        fn.Block(leave);
        var value = result is null ? null : fn.Value($"load {Ir(returnType)}, ptr {result}");
        Leaving(0, 0);
        ReturnRaw(value);
        if (loops.Count > 0)
        {
            fn.Block(broke);
            Leaving(loops.Peek().Regions, loops.Peek().Scopes);
            fn.Branch(loops.Peek().Break);
            fn.Block(continued);
            Leaving(loops.Peek().Regions, loops.Peek().Scopes);
            fn.Branch(loops.Peek().Continue);
        }

        fn.Block(done);
        if (!fallsThrough)
        {
            fn.Unreachable(); // the block's function always left
        }
    }

    /// <summary>
    /// Returns the value, of the IR type of the function's result, null for none: from the function being emitted, or, from a guarded
    /// block's function, as the value it stores and the code 1, which its caller returns.
    /// </summary>
    private void ReturnRaw(string? value)
    {
        if (outline is null)
        {
            fn.Return(value is null ? null : $"{Ir(returnType)} {value}");
            return;
        }

        if (value is not null)
        {
            fn.Emit($"store {Ir(returnType)} {value}, ptr %result");
        }

        fn.Return("i32 1");
    }

    /// <summary>
    /// The storage of a variable a guarded block's function reads from the function it was compiled out of: a parameter that takes
    /// its address there, added on the first read, through every block's function between them. Null for a variable none of them holds.
    /// </summary>
    private static string? Captured(Outline context, Dictionary<VariableSymbol, string> own, VariableSymbol variable)
    {
        var outer = context.Slots.TryGetValue(variable, out var slot) ? slot
            : context.Parent is { } parent ? Captured(parent, context.Slots, variable)
            : null;
        if (outer is null)
        {
            return null;
        }

        var parameter = $"%c{Number(context.Captures.Count)}";
        context.Captures.Add((outer, parameter));
        own[variable] = parameter;
        return parameter;
    }

    /// <summary>The function attribute of a function compiled for the classes beyond the CPU's: the driver's features, then theirs.</summary>
    private string[] FeatureAttributes(ImmutableSortedSet<string> classes) => classes.Count == 0 ? [] :
        [$"\"target-features\"=\"{string.Join(",", cpu.Features.Concat(Intrinsics.Features(classes.Select(c => Intrinsics.Classes[c])).Select(f => "+" + f)))}\""];

    /// <summary>The suffix of the IR name of a function compiled for the classes beyond the CPU's.</summary>
    private static string FeatureSuffix(ImmutableSortedSet<string> classes) => classes.Count == 0 ? string.Empty : ".for." + string.Join("+", classes);

    /// <summary>The module's read of the CPU's word, when a test reads it: a global, and the function <c>main</c> calls before the runtime starts.</summary>
    private string? CpuRead()
    {
        if (!cpuRead)
        {
            return null;
        }

        module.Global("@kz.cpu = internal global i64 0");
        var read = new IrFunction("internal void @kz.cpu.read()");
        const string Cpuid = "call { i32, i32, i32, i32 } asm \"cpuid\", \"={ax},={bx},={cx},={dx},{ax},{cx}\"";
        var top = read.Value($"extractvalue {{ i32, i32, i32, i32 }} {read.Value($"{Cpuid}(i32 0, i32 0)")}, 0");
        var leaf1 = read.Value($"zext i32 {read.Value($"extractvalue {{ i32, i32, i32, i32 }} {read.Value($"{Cpuid}(i32 1, i32 0)")}, 2")} to i64");
        read.Emit($"store i64 {leaf1}, ptr @kz.cpu");
        read.CondBranch(read.Value($"icmp uge i32 {top}, 7"), "leaf7", "done");
        read.Block("leaf7");
        var leaf7 = read.Value($"zext i32 {read.Value($"extractvalue {{ i32, i32, i32, i32 }} {read.Value($"{Cpuid}(i32 7, i32 0)")}, 1")} to i64");
        read.Emit($"store i64 {read.Value($"or i64 {leaf1}, {read.Value($"shl i64 {leaf7}, 32")}")}, ptr @kz.cpu");
        read.Branch("done");
        read.Block("done");
        read.Return(null);
        module.Add(read);
        return "@kz.cpu.read";
    }

    // ---- intrinsics and lane operations ----------------------------------------------------------

    /// <summary>A call of an intrinsic or a lane operation: its arguments in the order they are written, then the operation, in place or, where the function lacks its class's set, through a function compiled for it.</summary>
    private string? EmitIntrinsic(CallExpression c, CallInfo info)
    {
        var intrinsic = info.Intrinsic ?? throw new InvalidOperationException($"an intrinsic call without its intrinsic at line {c.Line}");
        var arguments = new List<string>();
        if (info.Target is not null)
        {
            arguments.Add(EmitExpression(((MemberAccess)c.Callee).Target)); // the vector a lane operation is called on
        }

        var values = new string?[intrinsic.Parameters.Count];
        var immediates = new BigInteger?[intrinsic.Parameters.Count];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = info.Binding[i];
            var a = c.Arguments[i].Value;
            var parameter = intrinsic.Parameters[slot];
            if (parameter.Immediate)
            {
                immediates[slot] = Model.TypeOf(a).Constant is IntegerConstant k ? k.Value : throw new InvalidOperationException($"an immediate that is no constant at line {a.Line}: the checker says `not supported`");
            }
            else
            {
                values[slot] = Convert(EmitExpression(a), TypeOf(a), parameter.Type, a.Line);
            }
        }

        arguments.AddRange(values.OfType<string>());
        var cls = intrinsic.Class;
        if (cls is null || Has(cls))
        {
            return Operation(intrinsic, arguments, immediates, c.Line);
        }

        if (!cls.IsX86)
        {
            throw Unsupported(c.Line, $"`{cls.Name}.{intrinsic.Name}` outside a test of `{cls.Name}.IsSupported`, which the target does not have (R18)");
        }

        var types = intrinsic.Parameters.Where(p => !p.Immediate).Select(p => Ir(p.Type)).ToList();
        var result = Types.IsVoid(intrinsic.Result) ? "void" : Ir(intrinsic.Result);
        var wrapper = Helper(
            $"intrinsic.{intrinsic.Form.Replace('"', '\'')}.{string.Join(".", immediates.OfType<BigInteger>())}",
            result,
            types,
            () =>
            {
                var value = Operation(intrinsic, types.Select((_, i) => $"%a{Number(i)}").ToList(), immediates, c.Line);
                fn.Return(value is null ? null : $"{result} {value}");
            },
            FeatureAttributes(NoFeatures.Add(cls.Name)));
        var call = $"call {result} {wrapper}({string.Join(", ", types.Select((t, i) => $"{t} {arguments[i]}"))})";
        return result == "void" ? Emitted(call) : fn.Value(call);
    }

    /// <summary>The instructions of an intrinsic or a lane operation over the values, in the function being emitted.</summary>
    private string? Operation(Intrinsic intrinsic, List<string> a, IReadOnlyList<BigInteger?> immediates, int line)
    {
        var result = intrinsic.Result;
        var immediate = immediates.OfType<BigInteger>().Select(v => (int)(v & 0xFF)).FirstOrDefault();
        var first = intrinsic.Parameters.Count > 0 ? intrinsic.Parameters[0].Type : null;
        switch (intrinsic.Op)
        {
            case "create":
                {
                    var vector = (VectorType)result;
                    var value = "poison";
                    for (var i = 0; i < a.Count; i++)
                    {
                        value = fn.Value($"insertelement {Ir(vector)} {value}, {Ir(vector.Lane)} {a[i]}, i32 {Number(i)}");
                    }

                    return value;
                }

            case "broadcast":
                {
                    var vector = (VectorType)result;
                    var one = fn.Value($"insertelement {Ir(vector)} poison, {Ir(vector.Lane)} {a[0]}, i32 0");
                    return fn.Value($"shufflevector {Ir(vector)} {one}, {Ir(vector)} poison, <{Number(vector.Count)} x i32> zeroinitializer");
                }

            case "get":
                {
                    // C#'s GetElement throws for an index outside the lanes; here that is `index-out-of-range` (chapter 12)
                    var vector = VectorOf(intrinsic);
                    var inside = fn.Label("lane");
                    var outside = fn.Label("out.of.range");
                    fn.CondBranch(fn.Value($"icmp ult i32 {a[1]}, {Number(vector.Count)}"), inside, outside); // a negative index reads as a number above every count
                    fn.Block(outside);
                    Raise(ErrorIds.IndexOutOfRange, line);
                    fn.Block(inside);
                    return fn.Value($"extractelement {Ir(vector)} {a[0]}, i32 {a[1]}");
                }

            case "as":
                return fn.Value($"bitcast {Ir(VectorOf(intrinsic))} {a[0]} to {Ir(result)}");
            case "add":
                return fn.Value($"{(Types.IsFloating(((VectorType)result).Lane) ? "fadd" : "add")} {Ir(result)} {a[0]}, {a[1]}");
            case "load":
                return fn.Value($"load {Ir(result)}, ptr {a[0]}, align 1");
            case "store":
                fn.Emit($"store {Ir(intrinsic.Parameters[1].Type)} {a[1]}, ptr {a[0]}, align 1");
                return null;
            case "pshufd":
                {
                    var lanes = Enumerable.Range(0, 4).Select(i => $"i32 {Number((immediate >> (2 * i)) & 3)}");
                    return fn.Value($"shufflevector {Ir(result)} {a[0]}, {Ir(result)} poison, <4 x i32> <{string.Join(", ", lanes)}>");
                }

            case "pshufb":
                return X86Call("ssse3.pshuf.b.128", "<16 x i8>", a.Select(v => $"<16 x i8> {v}"));
            case "palignr":
                {
                    // the 32 bytes of `left` above `right`, shifted right by the immediate's bytes, the low 16 kept, zeros shifted in
                    var bytes = "<16 x i8>";
                    var left = fn.Value($"bitcast {Ir(first!)} {a[0]} to {bytes}");
                    var right = fn.Value($"bitcast {Ir(first!)} {a[1]} to {bytes}");
                    string shifted;
                    if (immediate >= 32)
                    {
                        shifted = "zeroinitializer";
                    }
                    else if (immediate > 16)
                    {
                        var lanes = Enumerable.Range(0, 16).Select(i => $"i32 {Number(i + immediate - 16)}");
                        shifted = fn.Value($"shufflevector {bytes} {left}, {bytes} zeroinitializer, <16 x i32> <{string.Join(", ", lanes)}>");
                    }
                    else
                    {
                        var lanes = Enumerable.Range(0, 16).Select(i => $"i32 {Number(i + immediate)}");
                        shifted = fn.Value($"shufflevector {bytes} {right}, {bytes} {left}, <16 x i32> <{string.Join(", ", lanes)}>");
                    }

                    return fn.Value($"bitcast {bytes} {shifted} to {Ir(result)}");
                }

            case "extract":
                {
                    var vector = (VectorType)first!;
                    return fn.Value($"extractelement {Ir(vector)} {a[0]}, i32 {Number(immediate & (vector.Count - 1))}"); // the instruction reads the immediate's low bits
                }

            case "pblendw":
                {
                    var lanes = Enumerable.Range(0, 8).Select(i => $"i32 {Number(((immediate >> i) & 1) == 1 ? 8 + i : i)}");
                    return fn.Value($"shufflevector {Ir(result)} {a[0]}, {Ir(result)} {a[1]}, <8 x i32> <{string.Join(", ", lanes)}>");
                }

            case "sha256rnds2" or "sha256msg1" or "sha256msg2" or "sha1nexte" or "sha1msg1" or "sha1msg2":
                return X86Call(intrinsic.Op, "<4 x i32>", a.Select(v => $"<4 x i32> {v}"));
            case "sha1rnds4":
                return X86Call("sha1rnds4", "<4 x i32>", a.Select(v => $"<4 x i32> {v}").Append($"i8 {Number(immediate & 3)}"));
            case "aesenc" or "aesenclast" or "aesdec" or "aesdeclast" or "aesimc" or "aeskeygenassist":
                {
                    // LLVM's AES intrinsics take two 64-bit lanes, C#'s functions sixteen bytes: the same bits
                    var words = a.Select(v => $"<2 x i64> {fn.Value($"bitcast <16 x i8> {v} to <2 x i64>")}");
                    if (intrinsic.Op == "aeskeygenassist")
                    {
                        words = words.Append($"i8 {Number(immediate)}");
                    }

                    return fn.Value($"bitcast <2 x i64> {X86Call($"aesni.{intrinsic.Op}", "<2 x i64>", words.ToList())} to <16 x i8>");
                }

            default:
                throw Unsupported(line, $"`{intrinsic.Owner}.{intrinsic.Name}` (R18)");
        }
    }

    /// <summary>The vector a lane operation is called on, its owner's type.</summary>
    private static VectorType VectorOf(Intrinsic intrinsic)
    {
        var open = intrinsic.Owner.IndexOf('<', StringComparison.Ordinal);
        var bits = int.Parse(intrinsic.Owner.AsSpan(6, open - 6), CultureInfo.InvariantCulture);
        var lane = Intrinsics.Lanes.Single(l => l.Text == intrinsic.Owner[(open + 1)..^1]);
        return new VectorType(lane, bits);
    }

    /// <summary>
    /// The SHA instructions have no VEX form: on a CPU with AVX, where LLVM writes every other vector instruction in its VEX form and
    /// cleans the upper halves of the YMM registers before a call and a return alone, the first statement of a run that a test of
    /// <c>X86.Sha</c> guards is preceded by cleaning them, since a legacy instruction run while they are dirty pays a transition of the
    /// register state on Intel's cores: SHA-256 of 48 MiB took 594 ms, not 19, built for `raptorlake`, where the hash had stored its
    /// constants through YMM registers just before its rounds. Once a run, not at each instruction: cleaned at the entry of each
    /// function that ran one, the rounds of SHA-256 written as small functions inlined into a loop took 36 ms, not 19.
    /// </summary>
    private void CleanUpperHalves(Statement statement)
    {
        if (!fn.UpperClean && cpu.Classes.Contains("X86.Avx") && Model.GuardsAt(statement).Any(c => Intrinsics.Guards(c, Intrinsics.Classes["X86.Sha"])))
        {
            module.Declare("declare void @llvm.x86.avx.vzeroupper()");
            fn.Emit("call void @llvm.x86.avx.vzeroupper()");
            fn.UpperClean = true;
        }
    }

    /// <summary>A call of LLVM's x86 intrinsic over the typed arguments, declared once in the module, whose attributes LLVM gives it by its name.</summary>
    private string X86Call(string name, string result, IEnumerable<string> arguments)
    {
        var list = arguments.ToList();
        module.Declare($"declare {result} @llvm.x86.{name}({string.Join(", ", list.Select(t => t[..t.LastIndexOf(' ')]))})");
        return fn.Value($"call {result} @llvm.x86.{name}({string.Join(", ", list)})");
    }
}
