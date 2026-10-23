using System.Reflection;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>
/// The command <c>kurz</c> (design record, section 2). <c>kurz --version</c> writes on standard output
/// one line, the compiler's version, the major release of LLVM it pins and the clang it found with
/// that clang's release, as <c>rustc -V</c> writes one; <c>kurz --version --verbose</c> writes them on
/// lines of their own, as <c>rustc -vV</c> does (the owner, 2026-10-23). Both exit with 0; where the
/// search refused the clang it found, or found none, they write what they know before the clang, the
/// failure on standard error in the form of the record's failures of the command (<c>error:</c>, then
/// indented <c>note:</c> and <c>help:</c> lines), and exit with 1.
/// <c>kurz build [--release] file.kz [-o output]</c> builds the program for the machine that builds, the test build unless
/// <c>--release</c> says otherwise, into the executable beside the source, named after it, <c>hello.exe</c> on Windows, or where
/// <c>-o</c> says, written over an executable that is there; it refuses to write over the source or a directory. A program the
/// compiler refuses gets its lines on standard error, <c>hello.kz:3: &lt;id&gt;: &lt;text&gt;</c> or
/// <c>hello.kz:3: not supported: &lt;text&gt;</c>, and no executable; that, and every failure of the command's own, exits with 1. The
/// command grants a program neither <c>raw</c> nor <c>native</c> (R7, R16). A command line it cannot read gets the usage on standard
/// error and the exit code 1.
/// </summary>
public static class Command
{
    /// <summary>What the command prints for a command line it cannot read.</summary>
    public const string Usage = "usage: kurz --version [--verbose]\n       kurz build [--release] <file.kz> [-o <output>]\n";

    private const string OnThePath = " on the path";

    /// <summary>The compiler's version, from the assembly: the <c>Version</c> of <c>compiler/Directory.Build.props</c>.</summary>
    public static string CompilerVersion =>
        typeof(Command).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Runs the command line over the clang search of this process; the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error) =>
        Run(arguments, output, error, Clang.Find);

    /// <summary>Runs the command line with the clang search given; the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<ClangSearch> find) =>
        Run(arguments, output, error, find, Builder.HostTarget);

    /// <summary>As above, building for <paramref name="host"/>, the target of the machine, null where the runtime has none for it.</summary>
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<ClangSearch> find, Target? host)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(find);
        if (arguments is ["build", ..])
        {
            return Build(arguments.Skip(1).ToList(), error, find, host);
        }

        bool verbose;
        switch (arguments)
        {
            case ["--version"]:
                verbose = false;
                break;
            case ["--version", "--verbose"] or ["--verbose", "--version"]:
                verbose = true;
                break;
            default:
                error.Write(Usage);
                return 1;
        }

        var search = find();
        var clang = search.Found;
        output.Write(verbose
            ? $"kurz {CompilerVersion}\nLLVM version: {Clang.PinnedMajor}\n" + (clang is null ? string.Empty : $"clang: {clang.Path}\nclang version: {clang.Version}\n")
            : $"kurz {CompilerVersion} (LLVM {Clang.PinnedMajor}" + (clang is null ? string.Empty : $", clang {clang.Version} at {clang.Path}") + ")\n");
        if (clang is not null)
        {
            return 0;
        }

        error.Write(Failure(search));
        return 1;
    }

    private static int Build(List<string> arguments, TextWriter error, Func<ClangSearch> find, Target? host)
    {
        var release = false;
        string? sourcePath = null;
        string? outputPath = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--release" when !release:
                    release = true;
                    break;
                case "-o" when outputPath is null && i + 1 < arguments.Count:
                    outputPath = arguments[++i];
                    break;
                case var a when sourcePath is null && !a.StartsWith('-'):
                    sourcePath = a;
                    break;
                default:
                    error.Write(Usage);
                    return 1;
            }
        }

        if (sourcePath is null)
        {
            error.Write(Usage);
            return 1;
        }

        outputPath ??= Builder.ExecutableBeside(sourcePath);
        if (SamePath(sourcePath, outputPath))
        {
            error.Write($"error: the executable would be written over its source\n  note: both are {outputPath}\n  help: name another output with -o\n");
            return 1;
        }

        if (Directory.Exists(outputPath))
        {
            error.Write($"error: the output is a directory\n  note: {outputPath} is a directory\n  help: name a file with -o\n");
            return 1;
        }

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (outputDirectory is not null && !Directory.Exists(outputDirectory))
        {
            error.Write($"error: the output's directory does not exist\n  note: {outputDirectory} is not there\n  help: name a file in a directory that exists with -o\n");
            return 1;
        }

        if (host is not { } target)
        {
            error.Write("error: no runtime for this system\n  note: the runtime has a directory for 64-bit Linux and one for 64-bit Windows\n  help: build on 64-bit Linux or Windows\n");
            return 1;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sourcePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.Write($"error: the source cannot be read\n  note: {e.Message}\n  help: name a Kurz source file that can be read\n");
            return 1;
        }

        var source = SourceText.FromBytes(sourcePath, bytes, out var invalid);
        if (source is null)
        {
            error.Write(invalid!.At(sourcePath) + "\n");
            return 1;
        }

        var compilation = Builder.Compile(source, [], Builder.CheckRuntime(target), testBuild: !release);
        foreach (var d in compilation.Diagnostics.Sorted)
        {
            error.Write(d.At(sourcePath) + "\n");
        }

        if (compilation.Program is not { } program)
        {
            return 1;
        }

        var search = find();
        if (search.Found is not { } clang)
        {
            error.Write(Failure(search));
            return 1;
        }

        var work = Directory.CreateTempSubdirectory("kurz-build-");
        try
        {
            Builder.Build(program, clang, work.FullName, outputPath);
            return 0;
        }
        catch (ToolchainException e)
        {
            error.Write($"error: clang did not build the program\n  note: {e.Message.Replace("\n", "\n    ", StringComparison.Ordinal)}\n  help: check that the clang found runs; the compiler accepted the program, so a clang that runs and fails on it is the compiler's defect\n");
            return 1;
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    /// <summary>Whether the two paths name one file, as the file system compares names: without regard to case on Windows.</summary>
    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>The lines of a search that refused the clang it found, or found none.</summary>
    public static string Failure(ClangSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        var help = $"  help: install LLVM {Clang.PinnedMajor}";
        if (search.Place is null)
        {
            var names = search.Looked.Where(l => l.EndsWith(OnThePath, StringComparison.Ordinal)).Select(l => l[..^OnThePath.Length]);
            var installer = search.Looked.Where(l => !l.EndsWith(OnThePath, StringComparison.Ordinal)).ToList();
            var note = $"looked for {string.Join(" and ", names)}{OnThePath}"
                + (installer.Count > 0 ? $" and in the LLVM installer directory, {string.Join(", ", installer.Select(Path.GetDirectoryName))}" : string.Empty);
            return $"error: no clang found\n  note: {note}\n{help}\n";
        }

        var onPath = search.Looked[^1].EndsWith(OnThePath, StringComparison.Ordinal);
        var what = search.Apple
            ? "the clang found is Apple clang, whose version names no LLVM release"
            : search.Version is null
                ? "the clang found names no release in its answer to `--version`"
                : $"clang {search.Version} is not LLVM {Clang.PinnedMajor}";
        return $"error: {what}\n  note: found at {search.Place}\n{help}{(onPath ? ", ahead of this clang on the path" : string.Empty)}\n";
    }
}
