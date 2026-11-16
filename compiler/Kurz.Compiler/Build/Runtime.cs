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

/// <summary>
/// The architectures the directory of a target has a directory for, <c>runtime/linux/x86-64/</c> and <c>runtime/linux/arm64/</c> (design
/// record, section 2, round 20, "Dir per arch"): x86-64 and 64-bit Arm, the machines a build is for (<see cref="Builder.HostArch"/>).
/// </summary>
public enum Arch
{
    X64,
    Arm64,
}

/// <summary>A file of the runtime or of the standard library: its path from the top of the repository, <c>runtime/common/runtime.kz</c>, and its bytes.</summary>
public sealed record RuntimeFile(string Path, byte[] Bytes);

/// <summary>
/// The runtime checked for one target and architecture: <c>runtime/common/</c>, which reads the target's directory as the folder <c>sys</c>
/// and the standard library as the folder <c>std</c>, with its checker, the target's directory with its own, and <c>std/</c>, which reads
/// the target's directory as the folder <c>sys</c>, with its own, each with the path of its file; and the directory of the architecture
/// inside the target's, which <c>sys</c> reads beside the target's own file (round 20, "Dir per arch"), with its checker.
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
    Checker StdChecker,
    Arch Arch,
    string ArchPath,
    ProgramSyntax ArchTree,
    Checker ArchChecker);

/// <summary>
/// The runtime a built program links, written in Kurz, one per target (design record, section 2, round 17): the files under
/// <c>runtime/</c> at the top of the repository, which the build of the compiler embeds in it (round 20). <c>runtime/common/</c> holds
/// the code every target shares, and the directory of a target the code of that target alone; code in <c>common/</c> reaches the
/// functions of the target it is built for as the folder <c>sys</c>, which is that target's directory (<c>use sys</c>,
/// <c>sys.Write(...)</c>; the owner, 2026-10-23, "Rust's sys"), and every target's directory gives the functions <c>common/</c> calls,
/// each with the same parameters and the same result (round 20, "Called from outside"). The standard library's Kurz code, the allocator
/// of section 3, is under <c>std/</c> at the top of the repository, which the build of the compiler embeds too and the compiler compiles
/// with every program as it compiles the runtime (the owner, 2026-10-24, "A std/ directory now"; its place and its one file <c>std/std.kz</c>
/// are the record's assumptions): it reaches the target's directory as the folder <c>sys</c>, <c>common/</c> reaches it as the folder
/// <c>std</c>, and a function of a target's directory that <c>std/</c> calls is one that every target's directory gives too (the owner,
/// 2026-10-24, "As built"). Every folder is checked granted <c>raw</c> and <c>native</c> (R7, R16);
/// <see cref="CheckEveryTarget"/> checks every target in turn, so that the code of no target is checked only in a build for it (the
/// record's reading of round 20).
/// The directory of each architecture inside a target's, <c>runtime/linux/x86-64/</c>, holds the code of that target on that architecture,
/// which a build compiles with <c>common/</c> and the target's own file, read into the folder <c>sys</c> beside it, the build's architecture
/// alone (round 20, "Dir per arch"); <see cref="CheckEveryTarget"/> checks every target on every architecture, and a function that
/// <c>common/</c> or <c>std/</c> calls is one that the target's file and the architecture's give together, on every pair alike (the record's
/// assumption there).
/// The compiler reads one file in each directory and no directory inside the directory of an architecture or of <c>std/</c>: a folder of
/// several files, which section 8 makes one namespace, and a directory inside <c>std/</c> are <c>not supported</c> by <see cref="Layout"/>.
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

    /// <summary>The name of the architecture's directory inside a target's.</summary>
    public static string ArchDirectory(Arch arch) => arch switch
    {
        Arch.X64 => "x86-64",
        Arch.Arm64 => "arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(arch), arch, "no directory of the runtime"),
    };

    /// <summary>
    /// What is wrong with the files' places: a file outside <c>common/</c>, the targets' directories, the directories of their
    /// architectures and <c>std/</c>, and a directory of no file, and what the compiler does not read there, a directory inside
    /// <c>std/</c> and a directory of more than one file.
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
            var archFile = known && parts.Length == 4 && parts[1] != "common" && Enum.GetValues<Arch>().Select(ArchDirectory).Contains(parts[2]);
            if (!archFile && (!known || parts.Length != 3))
            {
                errors.Add(parts[0] == "std"
                    ? $"{file.Path}: the standard library is `std/<file>.kz`"
                    : $"{file.Path}: the runtime is `runtime/common/<file>.kz`, `runtime/<target>/<file>.kz` and `runtime/<target>/<architecture>/<file>.kz`, a target being one of {string.Join(", ", folders.Skip(1))} and an architecture one of {string.Join(", ", Enum.GetValues<Arch>().Select(ArchDirectory))}, and the standard library `std/<file>.kz`");
            }
        }

        foreach (var folder in folders.Concat(Enum.GetValues<Target>().SelectMany(t => Enum.GetValues<Arch>().Select(a => $"{Directory(t)}/{ArchDirectory(a)}"))))
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

    /// <summary>
    /// The runtime checked for <paramref name="target"/> on the architecture of the machine that builds; every diagnostic is added to
    /// <paramref name="errors"/> as a line that names its file.
    /// </summary>
    public static CheckedRuntime Check(Target target, List<string> errors) => Check(Files, target, Builder.HostArch, errors);

    /// <summary>The runtime checked for <paramref name="target"/> on <paramref name="arch"/>, as above.</summary>
    public static CheckedRuntime Check(Target target, Arch arch, List<string> errors) => Check(Files, target, arch, errors);

    /// <summary>As above, over the files given, on the architecture of the machine that builds.</summary>
    internal static CheckedRuntime Check(IReadOnlyList<RuntimeFile> files, Target target, List<string> errors) => Check(files, target, Builder.HostArch, errors);

    /// <summary>
    /// As above, over the files given; a directory that does not hold one file is read as empty, and <see cref="Layout"/> names it. The
    /// architecture's file is read into the folder <c>sys</c> beside the target's (round 20, "Dir per arch"): a qualified call of <c>sys</c>
    /// picks among the functions of both, and a name both declare is <c>redeclared</c> at its line in the architecture's file (V9), as two
    /// files of one folder are one namespace (section 8).
    /// </summary>
    internal static CheckedRuntime Check(IReadOnlyList<RuntimeFile> files, Target target, Arch arch, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(errors);
        errors.AddRange(Layout(files));
        var (archPath, archTree, archChecker) = CheckFolder(files, $"{Prefix}{Directory(target)}/{ArchDirectory(arch)}/", new Dictionary<string, Checker>(), errors);
        var (sysPath, sys, sysChecker) = CheckFolder(files, $"{Prefix}{Directory(target)}/", new Dictionary<string, Checker>(), errors);
        var targetNames = sys.Statements.OfType<FunctionDeclaration>().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var twice in archTree.Statements.OfType<FunctionDeclaration>().Where(f => targetNames.Contains(f.Name)))
        {
            errors.Add(Line(archPath, Diagnostic.Error(ErrorIds.Redeclared, twice.Line, $"the name `{twice.Name}` is declared already in {sysPath}, which the folder `sys` reads beside this file (V9)")));
        }

        sysChecker.Beside = archChecker;
        var (stdPath, std, stdChecker) = CheckFolder(files, StdDirectory, new Dictionary<string, Checker> { ["sys"] = sysChecker }, errors);
        var (commonPath, common, commonChecker) = CheckFolder(files, $"{Prefix}common/", new Dictionary<string, Checker> { ["sys"] = sysChecker, ["std"] = stdChecker }, errors);
        return new CheckedRuntime(target, commonPath, common, commonChecker, sysPath, sys, sysChecker, stdPath, std, stdChecker, arch, archPath, archTree, archChecker);
    }

    /// <summary>
    /// The runtime checked for every target on every architecture, with the rule that every target's directory, its file and the
    /// architecture's read as one set, gives the functions <c>common/</c> and <c>std/</c> call with the same parameters and the same result:
    /// what is wrong, one line each, or nothing. A line of <c>common/</c> or <c>std/</c> that the check for every pair reports stands once;
    /// one that only the check for some pairs reports names them, a target by its name alone where the check for each of its
    /// architectures reports it.
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

        var pairs = Enum.GetValues<Target>().SelectMany(t => Enum.GetValues<Arch>().Select(a => (Target: t, Arch: a))).ToList();
        var called = new Dictionary<(Target, Arch), Dictionary<string, SortedSet<string>>>();
        var reported = new Dictionary<(Target, Arch), List<string>>();
        foreach (var pair in pairs)
        {
            reported[pair] = [];
            called[pair] = TargetCalls(Check(files, pair.Target, pair.Arch, reported[pair]));
        }

        foreach (var line in reported.Values.SelectMany(l => l).Distinct(StringComparer.Ordinal))
        {
            var pairsOf = reported.Where(r => r.Value.Contains(line, StringComparer.Ordinal)).Select(r => r.Key).ToList();
            var where = pairsOf.GroupBy(p => p.Item1).SelectMany(g => g.Count() == Enum.GetValues<Arch>().Length ? new[] { Directory(g.Key) } : g.Select(PairDirectory).ToArray()).ToList(); // a target's name where each of its architectures reports the line
            var shared = line.StartsWith($"{Prefix}common/", StringComparison.Ordinal) || line.StartsWith(StdDirectory, StringComparison.Ordinal);
            errors.Add(pairsOf.Count == reported.Count || !shared
                ? line
                : $"{line} (in the check for {string.Join(" and ", where)})");
        }

        var first = pairs[0];
        foreach (var (pair, callers) in called)
        {
            if (pair == first)
            {
                continue;
            }

            foreach (var caller in Callers)
            {
                foreach (var missing in called[first][caller].Except(callers[caller]))
                {
                    errors.Add($"{Prefix}{PairDirectory(pair)}/: `{caller}` calls `{missing}` of {Prefix}{PairDirectory(first)}/, which this directory does not give with the same parameters and result");
                }

                foreach (var missing in callers[caller].Except(called[first][caller]))
                {
                    errors.Add($"{Prefix}{PairDirectory(first)}/: `{caller}` calls `{missing}` of {Prefix}{PairDirectory(pair)}/, which this directory does not give with the same parameters and result");
                }
            }
        }

        return errors;
    }

    // a target's directory with the architecture's, `linux/x86-64`, as a line of the rule of the same functions names the two files' set
    private static string PairDirectory((Target Target, Arch Arch) pair) => $"{Directory(pair.Target)}/{ArchDirectory(pair.Arch)}";

    /// <summary>
    /// The functions of the target's directory, its file and the architecture's, that <c>common/</c> and <c>std/</c> call, by their
    /// signatures, keyed by the caller's directory: the calls of <c>common/</c> into <c>std/</c> are no calls of the target's.
    /// </summary>
    internal static Dictionary<string, SortedSet<string>> TargetCalls(CheckedRuntime checkedRuntime)
    {
        var given = new HashSet<FunctionDeclaration>(checkedRuntime.Sys.Statements.Concat(checkedRuntime.ArchTree.Statements).OfType<FunctionDeclaration>(), ReferenceEqualityComparer.Instance);
        bool Given(FunctionSymbol f) => f.Declaration is { } d && given.Contains(d);
        return new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal)
        {
            ["common/"] = new(checkedRuntime.CommonChecker.FolderCalls.Where(Given).Select(Signature), StringComparer.Ordinal),
            [StdDirectory] = new(checkedRuntime.StdChecker.FolderCalls.Where(Given).Select(Signature), StringComparer.Ordinal),
        };
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
