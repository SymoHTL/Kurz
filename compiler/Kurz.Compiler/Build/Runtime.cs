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

/// <summary>A file of the runtime: its path from the top of the repository, <c>runtime/common/runtime.kz</c>, and its bytes.</summary>
public sealed record RuntimeFile(string Path, byte[] Bytes);

/// <summary>
/// The runtime checked for one target: <c>runtime/common/</c>, which reads the target's directory as the folder <c>sys</c>, with its checker,
/// and the target's directory with its own, each with the path of its file.
/// </summary>
public sealed record CheckedRuntime(Target Target, string CommonPath, ProgramSyntax Common, Checker CommonChecker, string SysPath, ProgramSyntax Sys, Checker SysChecker);

/// <summary>
/// The runtime a built program links, written in Kurz, one per target (design record, section 2, round 17): the files under
/// <c>runtime/</c> at the top of the repository, which the build of the compiler embeds in it (round 20). <c>runtime/common/</c> holds
/// the code every target shares, and the directory of a target the code of that target alone; code in <c>common/</c> reaches the
/// functions of the target it is built for as the folder <c>sys</c>, which is that target's directory (<c>use sys</c>,
/// <c>sys.Write(...)</c>; the owner, 2026-10-23, "Rust's sys"), and every target's directory gives the functions <c>common/</c> calls,
/// each with the same parameters and the same result (round 20, "Called from outside"). Both folders are checked granted
/// <c>raw</c> and <c>native</c> (R7, R16); <see cref="CheckEveryTarget"/> checks every target in turn, so that the code of no target is
/// checked only in a build for it (the record's reading of round 20).
/// The compiler reads one file in each of the three directories and no directory inside them: a folder of several files, which section 8
/// makes one namespace, and the directory of an architecture inside a target's, where round 20 places that code, are <c>not supported</c>
/// by <see cref="Layout"/>.
/// </summary>
public static class Runtime
{
    private const string Prefix = "runtime/";

    /// <summary>The files of the runtime as the compiler carries them, by path, in a fixed order.</summary>
    public static IReadOnlyList<RuntimeFile> Files { get; } = Embedded();

    /// <summary>The name of the target's directory under <c>runtime/</c>.</summary>
    public static string Directory(Target target) => target switch
    {
        Target.Linux => "linux",
        Target.Windows => "windows",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "no directory of the runtime"),
    };

    /// <summary>
    /// What is wrong with the files' places: a file outside <c>common/</c> and the targets' directories and a directory of no file, and what
    /// the compiler does not read there, a directory inside a target's and a directory of more than one file.
    /// </summary>
    public static IReadOnlyList<string> Layout(IReadOnlyList<RuntimeFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var errors = new List<string>();
        var folders = Enum.GetValues<Target>().Select(Directory).Prepend("common").ToList();
        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            var known = parts.Length >= 3 && parts[0] == "runtime" && folders.Contains(parts[1]) && parts[^1].EndsWith(".kz", StringComparison.Ordinal);
            if (known && parts.Length == 4 && parts[1] != "common")
            {
                errors.Add($"{file.Path}: not supported: a directory inside a target's, where round 20 places the code of an architecture (design record, section 2): the compiler reads one file in each directory of the runtime");
            }
            else if (!known || parts.Length != 3)
            {
                errors.Add($"{file.Path}: the runtime is `runtime/common/<file>.kz` and `runtime/<target>/<file>.kz`, a target being one of {string.Join(", ", folders.Skip(1))}");
            }
        }

        foreach (var folder in folders)
        {
            var count = files.Count(f => f.Path.StartsWith($"{Prefix}{folder}/", StringComparison.Ordinal) && f.Path.Count(ch => ch == '/') == 2);
            if (count == 0)
            {
                errors.Add($"{Prefix}{folder}/ holds 0 files: the compiler reads one file in each directory of the runtime");
            }
            else if (count > 1)
            {
                errors.Add($"{Prefix}{folder}/: not supported: a directory of {count} files, which section 8 makes one folder: the compiler reads one file in each directory of the runtime");
            }
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
        var (sysPath, sys) = Read(files, Directory(target), errors);
        var sysDiagnostics = new DiagnosticBag();
        var sysChecker = Checker.Check(sys, sysDiagnostics, ["raw", "native"]);
        errors.AddRange(sysDiagnostics.Sorted.Select(d => Line(sysPath, d)));

        var (commonPath, common) = Read(files, "common", errors);
        var commonDiagnostics = new DiagnosticBag();
        var commonChecker = Checker.Check(common, commonDiagnostics, ["raw", "native"], new Dictionary<string, Checker> { ["sys"] = sysChecker });
        errors.AddRange(commonDiagnostics.Sorted.Select(d => Line(commonPath, d)));
        return new CheckedRuntime(target, commonPath, common, commonChecker, sysPath, sys, sysChecker);
    }

    /// <summary>
    /// The runtime checked for every target, with the rule that every target's directory gives the functions <c>common/</c> calls with the
    /// same parameters and the same result: what is wrong, one line each, or nothing. A line of <c>common/</c> that the check for every
    /// target reports stands once; one that only the check for some targets reports names them.
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

        var called = new Dictionary<Target, SortedSet<string>>();
        var reported = new Dictionary<Target, List<string>>();
        foreach (var target in Enum.GetValues<Target>())
        {
            reported[target] = [];
            var checkedRuntime = Check(files, target, reported[target]);
            called[target] = new SortedSet<string>(checkedRuntime.CommonChecker.FolderCalls.Select(Signature), StringComparer.Ordinal);
        }

        foreach (var line in reported.Values.SelectMany(l => l).Distinct(StringComparer.Ordinal))
        {
            var targets = reported.Where(r => r.Value.Contains(line, StringComparer.Ordinal)).Select(r => Directory(r.Key)).ToList();
            errors.Add(targets.Count == reported.Count || !line.StartsWith($"{Prefix}common/", StringComparison.Ordinal)
                ? line
                : $"{line} (in the check for {string.Join(" and ", targets)})");
        }

        var first = Enum.GetValues<Target>()[0];
        foreach (var (target, signatures) in called)
        {
            if (target == first)
            {
                continue;
            }

            foreach (var missing in called[first].Except(signatures))
            {
                errors.Add($"{Prefix}{Directory(target)}/: `common/` calls `{missing}` of {Prefix}{Directory(first)}/, which this directory does not give with the same parameters and result");
            }

            foreach (var missing in signatures.Except(called[first]))
            {
                errors.Add($"{Prefix}{Directory(first)}/: `common/` calls `{missing}` of {Prefix}{Directory(target)}/, which this directory does not give with the same parameters and result");
            }
        }

        return errors;
    }

    /// <summary>A function's result, name and parameters, <c>long Write(int, byte*, long)</c>, a <c>mut</c> parameter marked.</summary>
    internal static string Signature(FunctionSymbol f) =>
        $"{f.ReturnType} {f.Name}({string.Join(", ", f.Parameters.Select(p => (p.Mut ? "mut " : "") + p.Type))})";

    private static (string Path, ProgramSyntax Tree) Read(IReadOnlyList<RuntimeFile> files, string folder, List<string> errors)
    {
        var inFolder = files.Where(f => f.Path.StartsWith($"{Prefix}{folder}/", StringComparison.Ordinal) && f.Path.Count(ch => ch == '/') == 2).ToList();
        if (inFolder.Count != 1)
        {
            return ($"{Prefix}{folder}/", new ProgramSyntax([]));
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
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            files.Add(new RuntimeFile(name.Replace('\\', '/'), bytes.ToArray())); // the directory separator of the machine that built the compiler
        }

        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }
}
