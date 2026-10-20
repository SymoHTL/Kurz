using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>What the compiler made of a program: its IR when it accepted it, else the diagnostics that refused it.</summary>
public sealed record Compilation(DiagnosticBag Diagnostics, string? Ir)
{
    /// <summary>Whether the program was accepted: the front end and the checker found no error, and nothing in it is <c>not supported</c>.</summary>
    public bool Accepted => Ir is not null;
}

/// <summary>
/// A build of one program, from its source to an executable: the parser, the checker and the
/// emitter in turn, then clang on the IR and the runtime (design record, section 2). The runtime's
/// C travels inside the compiler and is written beside the IR, or compiled once to an object file
/// where many programs are built in one run.
/// </summary>
public static class Builder
{
    /// <summary>The name of the IR file in a build's working directory.</summary>
    public const string IrFile = "program.ll";

    /// <summary>The name of the runtime's source in a build's working directory.</summary>
    public const string RuntimeFile = "kurz_rt.c";

    /// <summary>
    /// Reads, checks and emits the program. A program the front end or the checker refuses, and one
    /// that uses what the checker or the emitter does not read, gets no IR, and the diagnostics say why.
    /// </summary>
    public static Compilation Compile(SourceText source, bool testBuild)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        if (diagnostics.HasErrors)
        {
            return new Compilation(diagnostics, null);
        }

        var checker = Checker.Check(tree, diagnostics);
        if (diagnostics.HasErrors || diagnostics.HasNotSupported)
        {
            return new Compilation(diagnostics, null);
        }

        var ir = Emitter.Emit(tree, checker, testBuild, diagnostics);
        return new Compilation(diagnostics, ir);
    }

    /// <summary>
    /// Writes the IR into the working directory and has clang build the executable at
    /// <paramref name="outputPath"/>, linking the runtime from <paramref name="runtimeObject"/>
    /// where one was compiled before, else from its source, written beside the IR.
    /// </summary>
    public static void Build(string ir, Clang clang, string workingDirectory, string? runtimeObject, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(clang);
        Directory.CreateDirectory(workingDirectory);
        var irPath = Path.Combine(workingDirectory, IrFile);
        File.WriteAllText(irPath, ir);
        clang.Link(irPath, runtimeObject ?? WriteRuntime(workingDirectory), outputPath);
    }

    /// <summary>Writes the runtime's source into the directory; its path.</summary>
    public static string WriteRuntime(string directory)
    {
        var path = Path.Combine(directory, RuntimeFile);
        File.WriteAllText(path, Runtime.Source());
        return path;
    }

    /// <summary>The name of the executable a build of this source writes beside it: the source's name without its extension, with <c>.exe</c> on Windows.</summary>
    public static string ExecutableBeside(string sourcePath)
    {
        var stem = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(sourcePath));
        return OperatingSystem.IsWindows() ? stem + ".exe" : stem;
    }
}
