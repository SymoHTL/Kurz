using System.Reflection;

namespace Kurz.Compiler.Build;

/// <summary>
/// The command <c>kurz</c> (design record, section 2). <c>kurz --version</c> writes on standard output
/// one line, the compiler's version, the major release of LLVM it pins and the clang it found with
/// that clang's release, as <c>rustc -V</c> writes one; <c>kurz --version --verbose</c> writes them on
/// lines of their own, as <c>rustc -vV</c> does (the owner, 2026-10-23). Both exit with 0; where the
/// search refused the clang it found, or found none, they write what they know before the clang, the
/// failure on standard error in the form of the record's failures of the command (<c>error:</c>, then
/// indented <c>note:</c> and <c>help:</c> lines), and exit with 1. A command line it cannot read gets
/// the usage on standard error and the exit code 1.
/// </summary>
public static class Command
{
    /// <summary>What the command prints for a command line it cannot read.</summary>
    public const string Usage = "usage: kurz --version [--verbose]\n";

    private const string OnThePath = " on the path";

    /// <summary>The compiler's version, from the assembly: the <c>Version</c> of <c>compiler/Directory.Build.props</c>.</summary>
    public static string CompilerVersion =>
        typeof(Command).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Runs the command line over the clang search of this process; the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error) =>
        Run(arguments, output, error, Clang.Find);

    /// <summary>Runs the command line with the clang search given; the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<ClangSearch> find)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(find);
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
