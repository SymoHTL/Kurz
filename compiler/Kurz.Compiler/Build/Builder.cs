using System.Runtime.InteropServices;
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
public static class Builder
{
    /// <summary>The name of the IR file in a build's working directory.</summary>
    public const string IrFile = "program.ll";

    /// <summary>
    /// The target of the machine that builds, or null on a system the runtime has no directory for, and on a machine that is not
    /// 64-bit: the emitter lays a pointer out in 8 bytes, and the runtime declares C's <c>size_t</c> as a <c>long</c>.
    /// </summary>
    public static Target? HostTarget => TargetFor(RuntimeInformation.OSArchitecture, OperatingSystem.IsWindows(), OperatingSystem.IsLinux());

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
    /// line tables in a release build where <paramref name="lines"/> asks for them (<c>--lines</c>). A program the front end or the
    /// checker refuses, and one that uses what the checker or the emitter does not read, gets no IR, and the diagnostics say why.
    /// </summary>
    public static Compilation Compile(SourceText source, IReadOnlyCollection<string> grants, CheckedRuntime runtime, bool testBuild, bool lines = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        if (diagnostics.HasErrors)
        {
            return new Compilation(diagnostics, null);
        }

        var checker = Checker.Check(tree, diagnostics, grants);
        if (diagnostics.HasErrors || diagnostics.HasNotSupported)
        {
            return new Compilation(diagnostics, null);
        }

        return new Compilation(diagnostics, Emitter.Emit(tree, checker, runtime, source.Name, testBuild, diagnostics, lines));
    }

    /// <summary>Writes the IR into the working directory and has clang build the executable at <paramref name="outputPath"/>.</summary>
    public static void Build(EmittedProgram program, Clang clang, string workingDirectory, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(clang);
        Directory.CreateDirectory(workingDirectory);
        var irPath = Path.Combine(workingDirectory, IrFile);
        File.WriteAllText(irPath, program.Ir);
        clang.Build(irPath, outputPath, program.Libraries, program.Release, program.Lines);
    }

    /// <summary>The name of the executable a build of this source writes beside it: the source's name without its extension, with <c>.exe</c> on Windows.</summary>
    public static string ExecutableBeside(string sourcePath)
    {
        var stem = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(sourcePath));
        return OperatingSystem.IsWindows() ? stem + ".exe" : stem;
    }
}
