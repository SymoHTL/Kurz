using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>The systems the runtime has a directory for: <c>runtime/linux/</c> and <c>runtime/windows/</c> (design record, section 2).</summary>
public enum Target
{
    Linux,
    Windows,
}

/// <summary>A file of the runtime or of the standard library: its path from the top of the repository, <c>runtime/common/runtime.kz</c>, and its bytes.</summary>
public sealed record RuntimeFile(string Path, byte[] Bytes);

/// <summary>
/// The runtime checked for one target: <c>runtime/common/</c>, which reads the target's directory as the folder <c>sys</c> and the standard
/// library as the folder <c>std</c>, with its checker, the target's directory with its own, and <c>std/</c>, which reads the target's
/// directory as the folder <c>sys</c>, with its own, each with the path of its file.
/// </summary>
public sealed record CheckedRuntime(
    Target Target,
    string CommonPath,
    ProgramSyntax Common,
    Checker CommonChecker,
    string SysPath,
    ProgramSyntax Sys,
    Checker SysChecker,
    string StdPath,
    ProgramSyntax Std,
    Checker StdChecker);

/// <summary>
/// The runtime a built program links, written in Kurz, one per target (design record, section 2, round 17): the files under
/// <c>runtime/</c> at the top of the repository, which the build of the compiler embeds in it (round 20). <c>runtime/common/</c> holds
/// the code every target shares, and the directory of a target the code of that target alone; code in <c>common/</c> reaches the
/// functions of the target it is built for as the folder <c>sys</c>, which is that target's directory (<c>use sys</c>,
/// <c>sys.Write(...)</c>; the owner, 2026-10-23, "Rust's sys"), and every target's directory gives the functions <c>common/</c> calls,
/// each with the same parameters and the same result (round 20, "Called from outside"). The standard library's Kurz code, the allocator
/// of section 3, is under <c>std/</c> at the top of the repository, which the build of the compiler embeds too and the compiler compiles
/// with every program as it compiles the runtime: it reaches the target's directory as the folder <c>sys</c>, and <c>common/</c> reaches it
/// as the folder <c>std</c> (the owner, 2026-10-24, "A std/ directory now"), so a function of a target's directory that <c>std/</c> calls is
/// one that every target's directory gives too. Every folder is checked granted <c>raw</c> and <c>native</c> (R7, R16);
/// <see cref="CheckEveryTarget"/> checks every target in turn, so that the code of no target is checked only in a build for it (the
/// record's reading of round 20).
/// The compiler reads one file in each of the four directories and no directory inside them: a folder of several files, which section 8
/// makes one namespace, the directory of an architecture inside a target's, where round 20 places that code, and a directory inside
/// <c>std/</c> are <c>not supported</c> by <see cref="Layout"/>.
/// </summary>
public static class Runtime
{
    private const string Prefix = "runtime/";
    private const string StdDirectory = "std/";

    // the folders whose code calls a target's directory: the name of each in a line of the rule of the same functions
    private static readonly string[] Callers = ["common/", StdDirectory];

    /// <summary>The files of the runtime and of the standard library as the compiler carries them, by path, in a fixed order.</summary>
    public static IReadOnlyList<RuntimeFile> Files { get; } = Embedded();

    /// <summary>The name of the target's directory under <c>runtime/</c>.</summary>
    public static string Directory(Target target) => target switch
    {
        Target.Linux => "linux",
        Target.Windows => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "no directory of the runtime"),
    };

    /// <summary>
    /// What is wrong with the files' places: a file outside <c>common/</c>, the targets' directories and <c>std/</c> and a directory of no
    /// file, and what the compiler does not read there, a directory inside a target's or inside <c>std/</c> and a directory of more than one
    /// file.
    /// </summary>
    public static IReadOnlyList<string> Layout(IReadOnlyList<RuntimeFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var errors = new List<string>();
        var folders = Enum.GetValues<Target>().Select(Directory).Prepend("common").ToList();
        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            if (parts.Length > 2 && parts[0] == "std" && parts[^1].EndsWith(".kz", StringComparison.Ordinal))
            {
                errors.Add($"{file.Path}: not supported: a directory inside {StdDirectory}, which section 8 makes a folder of its own: the compiler reads one file of the standard library");
                continue;
            }

            if (parts.Length == 2 && parts[0] == "std" && parts[1].EndsWith(".kz", StringComparison.Ordinal))
            {
                continue;
            }

            var known = parts.Length >= 3 && parts[0] == "runtime" && folders.Contains(parts[1]) && parts[^1].EndsWith(".kz", StringComparison.Ordinal);
            if (known && parts.Length == 4 && parts[1] != "common")
            {
                errors.Add($"{file.Path}: not supported: a directory inside a target's, where round 20 places the code of an architecture (design record, section 2): the compiler reads one file in each directory of the runtime");
            }
            else if (!known || parts.Length != 3)
            {
                errors.Add($"{file.Path}: the runtime is `runtime/common/<file>.kz` and `runtime/<target>/<file>.kz`, a target being one of {string.Join(", ", folders.Skip(1))}, and the standard library `std/<file>.kz`");
            }
        }

        foreach (var folder in folders)
        {
            var count = InDirectory(files, $"{Prefix}{folder}/").Count;
            if (count == 0)
            {
                errors.Add($"{Prefix}{folder}/ holds 0 files: the compiler reads one file in each directory of the runtime");
            }
            else if (count > 1)
            {
                errors.Add($"{Prefix}{folder}/: not supported: a directory of {count} files, which section 8 makes one folder: the compiler reads one file in each directory of the runtime");
            }
        }

        var std = InDirectory(files, StdDirectory).Count;
        if (std == 0)
        {
            errors.Add($"{StdDirectory} holds 0 files: the compiler reads one file of the standard library");
        }
        else if (std > 1)
        {
            errors.Add($"{StdDirectory}: not supported: a directory of {std} files, which section 8 makes one folder: the compiler reads one file of the standard library");
        }

        return errors;
    }

    /// <summary>The runtime checked for <paramref name="target"/>; every diagnostic is added to <paramref name="errors"/> as a line that names its file.</summary>
    public static CheckedRuntime Check(Target target, List<string> errors) => Check(Files, target, errors);

    /// <summary>As above, over the files given; a directory that does not hold one file is read as empty, and <see cref="Layout"/> names it.</summary>
    internal static CheckedRuntime Check(IReadOnlyList<RuntimeFile> files, Target target, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(errors);
        errors.AddRange(Layout(files));
        var (sysPath, sys, sysChecker) = CheckFolder(files, $"{Prefix}{Directory(target)}/", new Dictionary<string, Checker>(), errors);
        var (stdPath, std, stdChecker) = CheckFolder(files, StdDirectory, new Dictionary<string, Checker> { ["sys"] = sysChecker }, errors);
        var (commonPath, common, commonChecker) = CheckFolder(files, $"{Prefix}common/", new Dictionary<string, Checker> { ["sys"] = sysChecker, ["std"] = stdChecker }, errors);
        return new CheckedRuntime(target, commonPath, common, commonChecker, sysPath, sys, sysChecker, stdPath, std, stdChecker);
    }

    /// <summary>
    /// The runtime checked for every target, with the rule that every target's directory gives the functions <c>common/</c> and <c>std/</c>
    /// call with the same parameters and the same result: what is wrong, one line each, or nothing. A line of <c>common/</c> or <c>std/</c>
    /// that the check for every target reports stands once; one that only the check for some targets reports names them.
    /// </summary>
    public static IReadOnlyList<string> CheckEveryTarget() => CheckEveryTarget(Files);

    /// <summary>As above, over the files given.</summary>
    internal static IReadOnlyList<string> CheckEveryTarget(IReadOnlyList<RuntimeFile> files)
    {
        var errors = new List<string>();
        var layout = Layout(files);
        if (layout.Count > 0)
        {
            return layout;
        }

        var called = new Dictionary<Target, Dictionary<string, SortedSet<string>>>();
        var reported = new Dictionary<Target, List<string>>();
        foreach (var target in Enum.GetValues<Target>())
        {
            reported[target] = [];
            var checkedRuntime = Check(files, target, reported[target]);
            // the calls of the target's directory, which common/'s calls of std/ are not
            var given = new HashSet<FunctionDeclaration>(checkedRuntime.Sys.Statements.OfType<FunctionDeclaration>(), ReferenceEqualityComparer.Instance);
            bool Given(FunctionSymbol f) => f.Declaration is { } d && given.Contains(d);
            called[target] = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal)
            {
                ["common/"] = new(checkedRuntime.CommonChecker.FolderCalls.Where(Given).Select(Signature), StringComparer.Ordinal),
                [StdDirectory] = new(checkedRuntime.StdChecker.FolderCalls.Where(Given).Select(Signature), StringComparer.Ordinal),
            };
        }

        foreach (var line in reported.Values.SelectMany(l => l).Distinct(StringComparer.Ordinal))
        {
            var targets = reported.Where(r => r.Value.Contains(line, StringComparer.Ordinal)).Select(r => Directory(r.Key)).ToList();
            var shared = line.StartsWith($"{Prefix}common/", StringComparison.Ordinal) || line.StartsWith(StdDirectory, StringComparison.Ordinal);
            errors.Add(targets.Count == reported.Count || !shared
                ? line
                : $"{line} (in the check for {string.Join(" and ", targets)})");
        }

        var first = Enum.GetValues<Target>()[0];
        foreach (var (target, callers) in called)
        {
            if (target == first)
            {
                continue;
            }

            foreach (var caller in Callers)
            {
                foreach (var missing in called[first][caller].Except(callers[caller]))
                {
                    errors.Add($"{Prefix}{Directory(target)}/: `{caller}` calls `{missing}` of {Prefix}{Directory(first)}/, which this directory does not give with the same parameters and result");
                }

                foreach (var missing in callers[caller].Except(called[first][caller]))
                {
                    errors.Add($"{Prefix}{Directory(first)}/: `{caller}` calls `{missing}` of {Prefix}{Directory(target)}/, which this directory does not give with the same parameters and result");
                }
            }
        }

        return errors;
    }

    /// <summary>A function's result, name and parameters, <c>long Write(int, byte*, long)</c>, a <c>mut</c> parameter marked.</summary>
    internal static string Signature(FunctionSymbol f) =>
        $"{f.ReturnType} {f.Name}({string.Join(", ", f.Parameters.Select(p => (p.Mut ? "mut " : "") + p.Type))})";

    // the folder's file read and checked with the folders it reaches, its diagnostics added as lines that name the file
    private static (string Path, ProgramSyntax Tree, Checker Checker) CheckFolder(IReadOnlyList<RuntimeFile> files, string directory, Dictionary<string, Checker> reaches, List<string> errors)
    {
        var (path, tree) = Read(files, directory, errors);
        var diagnostics = new DiagnosticBag();
        var checker = Checker.Check(tree, diagnostics, ["raw", "native"], reaches);
        errors.AddRange(diagnostics.Sorted.Select(d => Line(path, d)));
        return (path, tree, checker);
    }

    // the files directly in the directory, not in a directory inside it
    private static List<RuntimeFile> InDirectory(IReadOnlyList<RuntimeFile> files, string directory) =>
        files.Where(f => f.Path.StartsWith(directory, StringComparison.Ordinal) && !f.Path.AsSpan(directory.Length).Contains('/')).ToList();

    private static (string Path, ProgramSyntax Tree) Read(IReadOnlyList<RuntimeFile> files, string directory, List<string> errors)
    {
        var inFolder = InDirectory(files, directory);
        if (inFolder.Count != 1)
        {
            return (directory, new ProgramSyntax([]));
        }

        var file = inFolder[0];
        var diagnostics = new DiagnosticBag();
        var source = SourceText.FromBytes(file.Path, file.Bytes, out var invalid);
        if (source is null)
        {
            errors.Add(Line(file.Path, invalid!));
            return (file.Path, new ProgramSyntax([]));
        }

        var tree = Parser.Parse(source, diagnostics);
        errors.AddRange(diagnostics.Sorted.Select(d => Line(file.Path, d)));
        return (file.Path, diagnostics.HasErrors ? new ProgramSyntax([]) : tree);
    }

    private static string Line(string path, Diagnostic d) => d.At(path);

    private static List<RuntimeFile> Embedded()
    {
        var assembly = typeof(Runtime).Assembly;
        var files = new List<RuntimeFile>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) || n.StartsWith(StdDirectory, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            files.Add(new RuntimeFile(name.Replace('\\', '/'), bytes.ToArray())); // the directory separator of the machine that built the compiler
        }

        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }
}
