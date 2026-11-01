using System.ComponentModel;
using System.Reflection;
using Kurz.Compiler.Emit;
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
/// <c>kurz build [--release] [--lines] [--cpu name] file.kz [-o output]</c> builds the program for the machine that builds, the test build unless
/// <c>--release</c> says otherwise, a release build with the line tables a test build has where <c>--lines</c> says so (the owner,
/// 2026-10-29, "build, run and exec"), for the baseline of the machine's architecture, or for the CPU <c>--cpu</c> names as clang knows
/// it, <c>native</c> the machine's own, which clang gets as rustc passes <c>-C target-cpu</c> and whose sets make their tests constants
/// (section 2; R18; the owner, 2026-11-01, "--cpu on build, run and exec"; a CPU clang does not know is a failure of the command's own),
/// into the executable beside the source, named after it, <c>hello.exe</c> on Windows, or where
/// <c>-o</c> says, written over an executable that is there; it refuses to write over the source or a directory. A program the
/// compiler refuses gets its lines on standard error, <c>hello.kz:3: &lt;id&gt;: &lt;text&gt;</c> or
/// <c>hello.kz:3: not supported: &lt;text&gt;</c>, and no executable; that, and every failure of the command's own, exits with 1. The
/// command grants a program neither <c>raw</c> nor <c>native</c> (R7, R16). <c>kurz run [--release] [--lines] [--cpu name] file.kz</c> builds the file
/// and runs it, and <c>kurz exec [--release] [--lines] [--cpu name] "program text"</c> builds its argument, whose lines are named <c>exec</c>, and runs
/// it, each in a place of its own (<see cref="Place"/>), which it removes after the run, with the places a run cut short left removed at its
/// start; the program takes the words after <c>--</c> and the command's streams (<see cref="Processes.Run"/>), and the command ends with the
/// program's exit code. A command line it cannot read gets the usage on standard error and the exit code 1.
/// </summary>
public static class Command
{
    /// <summary>What the command prints for a command line it cannot read.</summary>
    public const string Usage = "usage: kurz --version [--verbose]\n       kurz build [--release] [--lines] [--cpu <name>] <file.kz> [-o <output>]\n"
        + "       kurz run [--release] [--lines] [--cpu <name>] <file.kz> [-- <argument>...]\n       kurz exec [--release] [--lines] [--cpu <name>] \"<program text>\" [-- <argument>...]\n"
        + "       run and exec pass the words after -- to the program\n";

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
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<ClangSearch> find, Target? host) =>
        Run(arguments, output, error, find, host, Place.Root);

    /// <summary>As above, with the directory of the places <paramref name="places"/> gives, null where there is none.</summary>
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<ClangSearch> find, Target? host, Func<string?> places)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(places);
        switch (arguments)
        {
            case ["build", ..]:
                return Build(arguments.Skip(1).ToList(), error, find, host);
            case ["run", ..]:
                return RunFile(arguments.Skip(1).ToList(), error, find, host, places);
            case ["exec", ..]:
                return Exec(arguments.Skip(1).ToList(), error, find, host, places);
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
        var lines = false;
        string? cpu = null;
        string? sourcePath = null;
        string? outputPath = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            switch (arguments[i])
            {
                case "--release" when !release:
                    release = true;
                    break;
                case "--lines" when !lines:
                    lines = true;
                    break;
                case "--cpu" when cpu is null && i + 1 < arguments.Count:
                    cpu = arguments[++i];
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
            error.Write(NoRuntime);
            return 1;
        }

        if (Read(sourcePath, error) is not { } source || Compile(source, target, release, lines, CpuOf(cpu, find, error), error) is not { } program)
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
            error.Write(ClangFailed(e));
            return 1;
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    private static int RunFile(List<string> arguments, TextWriter error, Func<ClangSearch> find, Target? host, Func<string?> places)
    {
        var release = false;
        var lines = false;
        string? cpu = null;
        string? sourcePath = null;
        var (own, program) = AtDashes(arguments);
        for (var i = 0; i < own.Count; i++)
        {
            switch (own[i])
            {
                case "--release" when !release:
                    release = true;
                    break;
                case "--lines" when !lines:
                    lines = true;
                    break;
                case "--cpu" when cpu is null && i + 1 < own.Count:
                    cpu = own[++i];
                    break;
                case var p when sourcePath is null && !p.StartsWith('-'):
                    sourcePath = p;
                    break;
                default:
                    return Usage1(error);
            }
        }

        return sourcePath is null ? Usage1(error) : BuildAndRun(() => Read(sourcePath, error), program, release, lines, cpu, error, find, host, places);
    }

    /// <summary><c>exec</c>'s options, each once, before its text, which is the last argument before <c>--</c> and no option.</summary>
    private static int Exec(List<string> arguments, TextWriter error, Func<ClangSearch> find, Target? host, Func<string?> places)
    {
        var release = false;
        var lines = false;
        string? cpu = null;
        var (own, program) = AtDashes(arguments);
        for (var i = 0; i < own.Count; i++)
        {
            switch (own[i])
            {
                case "--release" when !release:
                    release = true;
                    break;
                case "--lines" when !lines:
                    lines = true;
                    break;
                case "--cpu" when cpu is null && i + 2 < own.Count:
                    cpu = own[++i];
                    break;
                case var text when i == own.Count - 1 && text is not ("--release" or "--lines" or "--cpu"):
                    return BuildAndRun(() => SourceText.FromString("exec", text), program, release, lines, cpu, error, find, host, places);
                default:
                    return Usage1(error);
            }
        }

        return Usage1(error);
    }

    /// <summary>
    /// The command's own words of <c>run</c> or <c>exec</c>, those before the first <c>--</c>, and the program's, every word after it as
    /// it was given, a further <c>--</c> among them, as <c>cargo run</c> splits them (the owner, 2026-10-30, "After <c>--</c>, as cargo").
    /// </summary>
    private static (List<string> Own, List<string> Program) AtDashes(List<string> arguments) =>
        arguments.IndexOf("--") is var at and >= 0 ? (arguments[..at], arguments[(at + 1)..]) : (arguments, []);

    /// <summary>
    /// Builds the program <paramref name="read"/> reads, a file or <c>exec</c>'s text, into a place of its own and runs it with
    /// <paramref name="program"/> as its arguments; the program's exit code, or 1 where it was refused or the command failed. The
    /// places a run cut short left are removed at the start, before the build.
    /// </summary>
    private static int BuildAndRun(Func<SourceText?> read, IReadOnlyList<string> program, bool release, bool lines, string? cpu, TextWriter error, Func<ClangSearch> find, Target? host, Func<string?> places)
    {
        if (host is not { } target)
        {
            error.Write(NoRuntime);
            return 1;
        }

        if (places() is not { } root)
        {
            error.Write(OperatingSystem.IsWindows()
                ? "error: no directory for the command's places\n  note: Windows names no LocalAppData folder for this user\n  help: run the command as a user with a profile\n"
                : "error: no directory for the command's places\n  note: XDG_CACHE_HOME is not set to an absolute path, and no home directory is found in HOME or the user database\n  help: set HOME, or XDG_CACHE_HOME to an absolute path\n");
            return 1;
        }

        Place.RemoveLeft(root);
        if (read() is not { } source || Compile(source, target, release, lines, CpuOf(cpu, find, error), error) is not { } emitted)
        {
            return 1;
        }

        var search = find();
        if (search.Found is not { } clang)
        {
            error.Write(Failure(search));
            return 1;
        }

        Place place;
        try
        {
            place = Place.Make(root);
        }
        catch (PlaceException e)
        {
            error.Write($"error: the place for the run cannot be made\n  note: {e.Message}\n  help: check that {root} can be written\n");
            return 1;
        }

        using (place)
        {
            var executable = Path.Combine(place.Directory, OperatingSystem.IsWindows() ? "program.exe" : "program");
            try
            {
                Builder.Build(emitted, clang, place.Directory, executable);
            }
            catch (ToolchainException e)
            {
                error.Write(ClangFailed(e));
                return 1;
            }

            return RunProgram(executable, program, root, error);
        }
    }

    /// <summary>Runs a built program as <see cref="Processes.Run"/> does; its exit code, or 1 where it does not start.</summary>
    internal static int RunProgram(string executable, IReadOnlyList<string> arguments, string root, TextWriter error)
    {
        try
        {
            return Processes.Run(executable, arguments);
        }
        catch (Win32Exception e)
        {
            error.Write($"error: the built program cannot be started\n  note: {e.Message}\n  help: check that programs may run from {root}\n");
            return 1;
        }
    }

    /// <summary>The source at the path, or null where it cannot be read or is no text, with the failure written.</summary>
    private static SourceText? Read(string sourcePath, TextWriter error)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sourcePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.Write($"error: the source cannot be read\n  note: {e.Message}\n  help: name a Kurz source file that can be read\n");
            return null;
        }

        var source = SourceText.FromBytes(sourcePath, bytes, out var invalid);
        if (source is null)
        {
            error.Write(invalid!.At(sourcePath) + "\n");
        }

        return source;
    }

    /// <summary>The program the compiler emits for the source and the CPU <paramref name="cpu"/> gives, or null where it refused it, with its lines written, named by the source's name, or where the CPU failed.</summary>
    private static EmittedProgram? Compile(SourceText source, Target target, bool release, bool lines, Func<Cpu?> cpu, TextWriter error)
    {
        var compilation = Builder.Compile(source, [], Builder.CheckRuntime(target), testBuild: !release, lines, cpu);
        foreach (var d in compilation.Diagnostics.Sorted)
        {
            error.Write(d.At(source.Name) + "\n");
        }

        return compilation.Program;
    }

    /// <summary>
    /// The CPU <c>--cpu</c> names, as the clang found knows it, once the checker accepted the program, or the machine's baseline without
    /// the option; null with the failure written where no clang is found, where clang knows no CPU of that name for the machine's
    /// architecture (the owner, 2026-11-01: the failure is the command's own, naming clang's list), or where clang fails on the question
    /// (the owner, 2026-11-01: "clang did not answer which CPU", with what it wrote).
    /// </summary>
    private static Func<Cpu?> CpuOf(string? name, Func<ClangSearch> find, TextWriter error) => () =>
    {
        if (name is null)
        {
            return Builder.HostCpu;
        }

        var search = find();
        if (search.Found is not { } clang)
        {
            error.Write(Failure(search));
            return null;
        }

        var x86 = Builder.HostCpu.X86;
        try
        {
            if (clang.DescribeCpu(name, x86, out var triple) is { } cpu)
            {
                return cpu;
            }

            error.Write($"error: clang {clang.Version} knows no {(x86 ? "x86-64" : "64-bit Arm")} CPU named `{name}`\n  note: `clang --target={triple} --print-supported-cpus` lists the ones it knows\n  help: name one of those, or native\n");
            return null;
        }
        catch (ToolchainException e)
        {
            error.Write($"error: clang did not answer which CPU `{name}` is\n  note: {e.Message.Replace("\n", "\n    ", StringComparison.Ordinal)}\n  help: check that the clang found runs\n");
            return null;
        }
    };

    private static int Usage1(TextWriter error)
    {
        error.Write(Usage);
        return 1;
    }

    private const string NoRuntime = "error: no runtime for this system\n  note: the runtime has a directory for 64-bit Linux and one for 64-bit Windows\n  help: build on 64-bit Linux or Windows\n";

    private static string ClangFailed(ToolchainException e) =>
        $"error: clang did not build the program\n  note: {e.Message.Replace("\n", "\n    ", StringComparison.Ordinal)}\n  help: check that the clang found runs; the compiler accepted the program, so a clang that runs and fails on it is the compiler's defect\n";

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
