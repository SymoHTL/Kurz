using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>What the compiler made of a program: its IR and the libraries it links when it accepted it, else the diagnostics that refused it.</summary>
public sealed record Compilation(DiagnosticBag Diagnostics, EmittedProgram? Program)
{
    /// <summary>Whether the program was accepted: the front end and the checker found no error, and nothing in it is <c>not supported</c>.</summary>
    public bool Accepted => Program is not null;
}

/// <summary>
/// A build of one program, from its source to an executable: the parser, the checker and the emitter in turn, the runtime of the
/// target emitted into the same module, then the pinned clang on the IR, which links the program with the C library as the platform
/// links it (design record, section 2). The target is the machine that builds (section 2).
/// </summary>
public static partial class Builder
{
    /// <summary>The name of the IR file in a build's working directory.</summary>
    public const string IrFile = "program.ll";

    /// <summary>
    /// The target of the machine that builds, or null on a system the runtime has no directory for, and on a machine that is not
    /// 64-bit: the emitter lays a pointer out in 8 bytes, and the runtime declares C's <c>size_t</c> as a <c>long</c>.
    /// </summary>
    public static Target? HostTarget => TargetFor(RuntimeInformation.OSArchitecture, OperatingSystem.IsWindows(), OperatingSystem.IsLinux());

    /// <summary>The baseline CPU of the machine that builds, which a build that names no CPU is for (section 2): x86-64's, or 64-bit Arm's.</summary>
    public static Cpu HostCpu => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? Cpu.ArmBaseline : Cpu.Baseline;

    /// <summary>The target of a machine of this architecture and system, as <see cref="HostTarget"/> reads it.</summary>
    internal static Target? TargetFor(Architecture architecture, bool windows, bool linux) =>
        architecture is not (Architecture.X64 or Architecture.Arm64) ? null
        : windows ? Target.Windows
        : linux ? Target.Linux
        : null;

    /// <summary>
    /// The runtime checked for <paramref name="target"/>. A runtime the checker refuses is a defect of the compiler, which the suite's
    /// check of every target holds, so it is thrown and not reported against the program.
    /// </summary>
    public static CheckedRuntime CheckRuntime(Target target)
    {
        var errors = new List<string>();
        var runtime = Runtime.Check(target, errors);
        return errors.Count == 0
            ? runtime
            : throw new InvalidOperationException($"the runtime for {Runtime.Directory(target)} does not check:\n{string.Join('\n', errors)}");
    }

    /// <summary>
    /// Reads, checks and emits the program, with the grants of <paramref name="grants"/> (R7, R16) and the runtime given, and with the
    /// line tables in a release build where <paramref name="lines"/> asks for them (<c>--lines</c>), for the CPU <paramref name="cpu"/>
    /// gives once the checker accepted the program, the machine's baseline where it is null (section 2). A program the front end or the
    /// checker refuses, and one that uses what the checker or the emitter does not read, gets no IR, and the diagnostics say why; where
    /// <paramref name="cpu"/> gives null, it wrote why itself, and the program gets no IR either.
    /// </summary>
    public static Compilation Compile(SourceText source, IReadOnlyCollection<string> grants, CheckedRuntime runtime, bool testBuild, bool lines = false, Func<Cpu?>? cpu = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        if (diagnostics.HasErrors)
        {
            return new Compilation(diagnostics, null);
        }

        var checker = Checker.Check(tree, diagnostics, grants, runtime.StdChecker); // W1: the file sees the types of std/ the prelude names
        if (diagnostics.HasErrors || diagnostics.HasNotSupported)
        {
            return new Compilation(diagnostics, null);
        }

        return (cpu ?? (() => HostCpu))() is { } named
            ? new Compilation(diagnostics, Emitter.Emit(tree, checker, runtime, source.Name, testBuild, diagnostics, lines, named))
            : new Compilation(diagnostics, null);
    }

    /// <summary>
    /// Writes the IR into the working directory and has clang build the executable at <paramref name="outputPath"/>. Where clang refuses
    /// inline assembly, its text names no line of the program, not even the <c>!srcloc</c> Rust's compiler puts on the call (clang 23 on
    /// IR), so the program's <c>asm</c>s are assembled together without the rest of the program, in the order of their lines, each with
    /// those before it that clang took, and each one whose addition clang refuses is the compile error <c>asm-refused</c> at its line, an
    /// <see cref="AsmRefusedException"/> that names them in the order of the lines (R11, R12; the owner, 2026-11-13, "Rust's srcloc"): one
    /// clang refuses alone, and the second of two that define one label, which LLVM refuses in the second's text, as Rust's compiler names
    /// it through the second's <c>!srcloc</c>. Where none is refused so, the failure stays clang's.
    /// </summary>
    public static void Build(EmittedProgram program, Clang clang, string workingDirectory, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(clang);
        Directory.CreateDirectory(workingDirectory);
        var irPath = Path.Combine(workingDirectory, IrFile);
        File.WriteAllText(irPath, program.Ir);
        try
        {
            clang.Build(irPath, outputPath, program.Libraries, program.Release, program.Lines, program.CpuFlag);
        }
        catch (ToolchainException e) when (program.Asms.Count > 0 && e.Message.Contains(InlineAsm, StringComparison.Ordinal))
        {
            var taken = new List<AsmAlone>();
            var refused = new List<Diagnostic>();
            foreach (var (asm, i) in program.Asms.OrderBy(a => a.Line).Select((asm, i) => (asm, i))) // stable: the `asm`s of one line in the order emitted
            {
                var path = Path.Combine(workingDirectory, $"asm{i.ToString(CultureInfo.InvariantCulture)}.ll");
                File.WriteAllText(path, Together([.. taken, asm]));
                if (clang.Assemble(path, Path.ChangeExtension(path, ".o"), program.CpuFlag) is { } refusal && refusal.Contains(InlineAsm, StringComparison.Ordinal))
                {
                    var said = AssemblerError().Match(refusal) is { Success: true } m ? m.Groups[1].Value : "the instruction is not one it reads";
                    refused.Add(Diagnostic.Error(ErrorIds.AsmRefused, asm.Line, $"the assembler refuses an instruction of the `asm`: {said} ({asm.Rule})"));
                }
                else
                {
                    taken.Add(asm);
                }
            }

            if (refused.Count == 0)
            {
                throw;
            }

            throw new AsmRefusedException([.. refused.DistinctBy(d => d.Line)]); // an `asm` of a function emitted twice, once (E1)
        }
    }

    /// <summary>R11, R12: one module of IR that holds the <c>asm</c>s given, each symbol their <c>sym</c>s name declared once.</summary>
    private static string Together(IReadOnlyList<AsmAlone> asms) =>
        string.Concat(asms.SelectMany(a => a.Declarations).Distinct(StringComparer.Ordinal).Select(d => d + "\n")) + string.Concat(asms.Select(a => a.Ir));

    /// <summary>The file clang names an error of inline assembly in, LLVM's buffer of the <c>asm</c>'s text.</summary>
    private const string InlineAsm = "<inline asm>";

    [GeneratedRegex(@"<inline asm>:\d+:\d+: error: ([^\r\n]*)")]
    private static partial Regex AssemblerError();

    /// <summary>The name of the executable a build of this source writes beside it: the source's name without its extension, with <c>.exe</c> on Windows.</summary>
    public static string ExecutableBeside(string sourcePath)
    {
        var stem = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(sourcePath));
        return OperatingSystem.IsWindows() ? stem + ".exe" : stem;
    }
}

/// <summary>R11, R12: the <c>asm</c>s of a program the assembler refuses, each the compile error <c>asm-refused</c> at its line, in the order of the lines (<see cref="Builder.Build"/>).</summary>
public sealed class AsmRefusedException(IReadOnlyList<Diagnostic> refused) : Exception(string.Join("\n", refused))
{
    public IReadOnlyList<Diagnostic> Refused { get; } = refused;
}
