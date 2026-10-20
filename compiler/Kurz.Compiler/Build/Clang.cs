using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurz.Compiler.Build;

/// <summary>
/// The clang of an LLVM release that the machine has, which compiles the emitted IR and the runtime's
/// C and links them (design record, section 2). The compiler calls it by its versioned name,
/// <c>clang-18</c>, where the distribution installs one, taking the newest such name on the path;
/// else as <c>clang</c>; else, on Windows, where the LLVM installer puts it when it is not on the
/// path, <c>LLVM\bin\clang.exe</c> under the program files. Which releases are accepted is the
/// compiler gate's to say (<c>tools/compiler_gate.py</c>); the compiler asks the clang it found for
/// its version and uses it.
/// </summary>
public sealed partial class Clang
{
    private Clang(string path, Version version, string versionLine)
    {
        Path = path;
        Version = version;
        VersionLine = versionLine;
    }

    /// <summary>Where the executable is, or the bare name the path resolves.</summary>
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
    public static Clang? Find(out IReadOnlyList<string> looked)
    {
        var places = new List<string>();
        foreach (var candidate in Candidates())
        {
            places.Add(candidate);
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

    /// <summary>The versioned names on the path, newest major first, then the bare name, then the Windows installer's directory.</summary>
    private static IEnumerable<string> Candidates()
    {
        var windows = OperatingSystem.IsWindows();
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var versioned = new List<(int Major, string Path)>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var match = VersionedName().Match(System.IO.Path.GetFileName(file));
                if (match.Success)
                {
                    versioned.Add((int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), file));
                }
            }
        }

        foreach (var (_, path) in versioned.OrderByDescending(v => v.Major).ThenBy(v => v.Path, StringComparer.Ordinal))
        {
            yield return path;
        }

        yield return windows ? "clang.exe" : "clang";
        if (windows)
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles") ?? string.Empty; // the variable, as the gate reads it; the shell's known folder ignores the environment
            if (programFiles.Length > 0)
            {
                yield return System.IO.Path.Combine(programFiles, "LLVM", "bin", "clang.exe");
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

    /// <summary>Compiles the runtime's C to an object file, so that a run of many programs compiles it once.</summary>
    public void CompileRuntime(string sourcePath, string objectPath)
    {
        var (code, _, error) = Processes.Capture(Path, ["-std=c11", "-O2", "-c", sourcePath, "-o", objectPath], null, TimeSpan.FromMinutes(2));
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
            arguments.Insert(0, "-std=c11");
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
