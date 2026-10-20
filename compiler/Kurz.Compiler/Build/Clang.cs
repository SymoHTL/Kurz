using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurz.Compiler.Build;

/// <summary>
/// The clang of an LLVM release that the machine has, which compiles the emitted IR and the runtime's
/// C and links them (design record, section 2). The compiler calls it by its versioned name,
/// <c>clang-18</c>, where the distribution installs one, taking the newest such name on the path;
/// else as <c>clang</c>; else, on Windows, where the LLVM installer puts it when it is not on the
/// path, <c>LLVM\bin\clang.exe</c> under the directory the <c>ProgramFiles</c> variable names. Each
/// name is resolved through the path's directories by the compiler itself and run by its full path,
/// so that the places the operating system would search beside the path (on Windows the program's
/// own directory, the current directory and the system directories) play no part. Which releases
/// are accepted is the compiler gate's to say (<c>tools/compiler_gate.py</c>); the compiler asks the
/// clang it found for its version and uses it.
/// </summary>
public sealed partial class Clang
{
    private Clang(string path, Version version, string versionLine)
    {
        Path = path;
        Version = version;
        VersionLine = versionLine;
    }

    /// <summary>Where the executable is.</summary>
    public string Path { get; }

    /// <summary>The release, as <c>clang --version</c> names it.</summary>
    public Version Version { get; }

    /// <summary>The first line <c>clang --version</c> printed.</summary>
    public string VersionLine { get; }

    [GeneratedRegex(@"^clang-(\d+)(?:\.exe)?$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedName();

    [GeneratedRegex(@"clang version (\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionText();

    /// <summary>The clang the machine has, or null; <paramref name="looked"/> lists the names and places tried, in order.</summary>
    public static Clang? Find(out IReadOnlyList<string> looked) =>
        Find(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("ProgramFiles"), out looked);

    /// <summary>The same search over the path and the Program Files directory given, as the variables <c>PATH</c> and <c>ProgramFiles</c> would name them; null stands for a variable that is not set.</summary>
    public static Clang? Find(string? path, string? programFiles, out IReadOnlyList<string> looked)
    {
        var places = new List<string>();
        foreach (var (place, candidate) in Candidates(path, programFiles))
        {
            places.Add(place);
            if (candidate is null)
            {
                continue;
            }

            var found = Probe(candidate);
            if (found is not null)
            {
                looked = places;
                return found;
            }
        }

        looked = places;
        return null;
    }

    /// <summary>
    /// The versioned names on the path, newest major first, then the bare name where a directory of
    /// the path holds it, then the Windows installer's directory: each as the place to name when
    /// nothing answers, with the file to run, or null where the place holds no file. The bare name is
    /// resolved through the path's directories here and never handed to the operating system bare,
    /// which on Windows would search the program's own directory, the current directory and the system
    /// directories before the path.
    /// </summary>
    private static IEnumerable<(string Place, string? Path)> Candidates(string? path, string? programFiles)
    {
        var windows = OperatingSystem.IsWindows();
        var bare = windows ? "clang.exe" : "clang";
        var directories = (path ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Directory.Exists)
            .ToList();
        var versioned = new List<(int Major, string Path)>();
        foreach (var directory in directories)
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var match = VersionedName().Match(System.IO.Path.GetFileName(file));
                if (match.Success)
                {
                    versioned.Add((int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), file));
                }
            }
        }

        foreach (var (_, versionedPath) in versioned.OrderByDescending(v => v.Major).ThenBy(v => v.Path, StringComparer.Ordinal))
        {
            yield return (versionedPath, versionedPath);
        }

        var onPath = directories.Select(d => System.IO.Path.Combine(d, bare)).FirstOrDefault(File.Exists);
        yield return (onPath ?? $"{bare} on the path", onPath);
        if (windows)
        {
            if (!string.IsNullOrEmpty(programFiles)) // the variable, as the gate reads it; the shell's known folder ignores the environment
            {
                var installed = System.IO.Path.Combine(programFiles, "LLVM", "bin", "clang.exe");
                yield return (installed, File.Exists(installed) ? installed : null);
            }
        }
    }

    /// <summary>Runs <c>--version</c>; null when the candidate does not start or does not answer as clang does.</summary>
    private static Clang? Probe(string candidate)
    {
        var (code, output, _) = Processes.Capture(candidate, ["--version"], null, TimeSpan.FromSeconds(30));
        if (code != 0)
        {
            return null;
        }

        var firstLine = output.Split('\n')[0].TrimEnd('\r');
        var match = VersionText().Match(firstLine);
        if (!match.Success)
        {
            return null;
        }

        var version = new Version(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        return new Clang(candidate, version, firstLine);
    }

    /// <summary>Compiles the runtime's C to an object file, so that a run of many programs compiles it once. The runtime moves its bytes with its own loops, and <c>-fno-builtin</c> keeps the optimizer from turning a loop into a call of the C library.</summary>
    public void CompileRuntime(string sourcePath, string objectPath)
    {
        var (code, _, error) = Processes.Capture(Path, ["-std=c11", "-fno-builtin", "-O2", "-c", sourcePath, "-o", objectPath], null, TimeSpan.FromMinutes(2));
        if (code != 0)
        {
            throw new ToolchainException($"clang did not compile the runtime (exit {code}):\n{error}");
        }
    }

    /// <summary>Compiles the IR and links it with the runtime, given as its C source or as an object file, into the executable.</summary>
    public void Link(string irPath, string runtimePath, string outputPath)
    {
        var arguments = new List<string> { "-O2", irPath, runtimePath, "-o", outputPath };
        if (runtimePath.EndsWith(".c", StringComparison.Ordinal))
        {
            arguments.InsertRange(0, ["-std=c11", "-fno-builtin"]);
        }

        if (OperatingSystem.IsWindows() && File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? string.Empty, "lld-link.exe")))
        {
            arguments.Add("-fuse-ld=lld"); // the linker of the same release, where the installer put one beside clang
        }

        var (code, _, error) = Processes.Capture(Path, arguments, null, TimeSpan.FromMinutes(2));
        if (code != 0)
        {
            throw new ToolchainException($"clang did not build the program (exit {code}):\n{error}");
        }
    }
}
