using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurz.Compiler.Build;

/// <summary>
/// The clang of the one major release of LLVM the compiler accepts, which compiles and links the
/// emitted IR (design record, section 2). The search tries <c>clang-&lt;major&gt;</c> on the path, then
/// <c>clang</c> on the path, then on Windows <c>clang.exe</c> in <c>LLVM\bin</c> under the directory the
/// <c>ProgramFiles</c> variable names, where the LLVM installer puts it without adding it to the path.
/// The first place that holds such a file gives the clang taken, and its refusal ends the search, so
/// that of two installs the one ahead on the path wins (the record's reading of round 20). Apple's
/// clang numbers its own releases, so its answer names no LLVM release and it is refused (the owner,
/// 2026-10-23). Each name is
/// resolved through the path's directories here and the file is run by its full path, so that the
/// places the operating system would search beside the path (on Windows the program's own directory,
/// the current directory and the system directories) play no part.
/// </summary>
public sealed partial class Clang
{
    /// <summary>
    /// The pinned major, the newest stable release of LLVM when it was pinned (design record, section
    /// 2). <c>tools/lint_ci.py</c> reads this line and holds the CI image and the Windows job to it.
    /// </summary>
    public const int PinnedMajor = 23;

    private Clang(string path, string version)
    {
        Path = path;
        Version = version;
    }

    /// <summary>Where the executable is.</summary>
    public string Path { get; }

    /// <summary>The release, as <c>clang --version</c> names it: major, minor and patch.</summary>
    public string Version { get; }

    /// <summary>The versioned name the search tries first, without the extension Windows gives it.</summary>
    public static string VersionedName => $"clang-{PinnedMajor}";

    [GeneratedRegex(@"clang version (\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionText();

    [GeneratedRegex(@"^Apple clang version ", RegexOptions.Multiline)]
    private static partial Regex AppleText();

    /// <summary>The search over the variables <c>PATH</c> and <c>ProgramFiles</c> of this process, asking each candidate itself.</summary>
    public static ClangSearch Find() =>
        Find(Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("ProgramFiles"), OperatingSystem.IsWindows(), Ask);

    /// <summary>
    /// The search over the path and the Program Files directory given, as those variables would name
    /// them (null for a variable that is not set), with the names and places of Windows or of the other
    /// systems; the path is split as this system splits it. <paramref name="ask"/> runs a
    /// file with <c>--version</c> and gives what it wrote on standard output, or null where it did not
    /// start or did not exit with 0.
    /// </summary>
    public static ClangSearch Find(string? path, string? programFiles, bool windows, Func<string, string?> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        var looked = new List<string>();
        foreach (var (place, file) in Places(path, programFiles, windows))
        {
            looked.Add(place);
            if (file is null)
            {
                continue;
            }

            var answer = ask(file);
            var version = answer is null ? null : ReadVersion(answer);
            return version is not null && Major(version) == PinnedMajor
                ? new ClangSearch(new Clang(file, version), file, version, looked)
                : new ClangSearch(null, file, version, looked, answer is not null && IsApple(answer));
        }

        return new ClangSearch(null, null, null, looked);
    }

    /// <summary>
    /// The LLVM release a <c>--version</c> answer names, <c>23.1.4</c>, or null where it names none, as
    /// Apple's clang does: the number in its answer is Apple's own (Apple clang 17 is built from LLVM 19).
    /// </summary>
    public static string? ReadVersion(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var match = VersionText().Match(answer);
        return match.Success && !IsApple(answer) ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}" : null;
    }

    /// <summary>Whether the answer to <c>--version</c> is Apple's clang's, whose first line names it so.</summary>
    public static bool IsApple(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return AppleText().IsMatch(answer);
    }

    private static int Major(string version) => int.Parse(version.Split('.')[0], CultureInfo.InvariantCulture);

    /// <summary>
    /// Each place in the order of the search, with the file it holds or null: the versioned name in
    /// the first directory of the path that holds it, the bare name likewise, then on Windows the
    /// installer's directory. A place is named as the search looked at it, for the note of a search
    /// that found nothing.
    /// </summary>
    private static IEnumerable<(string Place, string? File)> Places(string? path, string? programFiles, bool windows)
    {
        var extension = windows ? ".exe" : string.Empty;
        var directories = (path ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var name in new[] { VersionedName + extension, "clang" + extension })
        {
            var file = directories.Select(d => System.IO.Path.Combine(d, name)).FirstOrDefault(File.Exists);
            yield return ($"{name} on the path", file);
        }

        if (windows && !string.IsNullOrEmpty(programFiles))
        {
            var installed = System.IO.Path.Combine(programFiles, "LLVM", "bin", "clang.exe");
            yield return (installed, File.Exists(installed) ? installed : null);
        }
    }

    /// <summary>
    /// Compiles the IR at <paramref name="irPath"/> and links it into the executable at <paramref name="outputPath"/>, with the
    /// C library as the platform links it, on Linux its math library <c>libm</c> with it, as Rust's standard library links it through
    /// its <c>libc</c> crate, which the calls of an unoptimised build reach (<c>fmod</c> for a <c>%</c> of a <c>double</c>), and the
    /// libraries the program's C functions come from (R16); on Windows with the linker of the same release where the installer put
    /// one beside this clang. A release build is optimised and a test build is not, as Rust's release and dev profiles (the owner,
    /// 2026-10-29, "As Rust's dev profile, -O0"); IR with line tables is built with <c>-g</c>, which on Windows has the linker write
    /// them into the PDB. The failure, with what clang wrote, where it
    /// does not exit with 0.
    /// </summary>
    public void Build(string irPath, string outputPath, IReadOnlyList<string> libraries, bool release, bool lines)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var arguments = new List<string> { release ? "-O2" : "-O0", irPath, "-o", outputPath };
        if (lines)
        {
            arguments.Add("-g");
        }

        if (OperatingSystem.IsWindows() && File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? string.Empty, "lld-link.exe")))
        {
            arguments.Add("-fuse-ld=lld");
        }

        arguments.AddRange(libraries.Select(l => "-l" + l));
        if (OperatingSystem.IsLinux())
        {
            arguments.Add("-lm");
        }
        var (code, _, error) = Processes.Capture(Path, arguments, null, TimeSpan.FromMinutes(2));
        if (code != 0)
        {
            throw new ToolchainException($"{Path} exited with {code.ToString(CultureInfo.InvariantCulture)} and wrote:\n{error.TrimEnd()}");
        }
    }

    /// <summary>Runs the file with <c>--version</c>; what it wrote, or null where it did not start or did not exit with 0.</summary>
    private static string? Ask(string file)
    {
        var (code, output, _) = Processes.Capture(file, ["--version"], null, TimeSpan.FromSeconds(30));
        return code == 0 ? output : null;
    }
}

/// <summary>
/// What the search found: <see cref="Found"/>, the clang of the pinned major; or at <see cref="Place"/>
/// a file it refused, with the release it named (<see cref="Version"/>), or none where it named none,
/// <see cref="Apple"/> where the file is Apple's clang; or no file at any place. <see cref="Looked"/>
/// names the places tried, in order.
/// </summary>
public sealed record ClangSearch(Clang? Found, string? Place, string? Version, IReadOnlyList<string> Looked, bool Apple = false);

/// <summary>A failure of clang on a build, saying what clang wrote: the program reached it accepted, so the failure is the toolchain's or the compiler's, never the program's.</summary>
public sealed class ToolchainException(string message) : Exception(message);
