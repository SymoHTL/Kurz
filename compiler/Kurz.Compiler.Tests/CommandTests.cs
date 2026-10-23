using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The command <c>kurz</c>, run as a process from the suite's output directory, where the command's
/// project is built beside the suite, and in this process with the clang search given: what
/// <c>--version</c> and <c>--version --verbose</c> write where and exit with (design record, section 2),
/// and the usage. Every expected text is written out here, the compiler's version 0.1.0 and the
/// pinned major 23 among it, and not taken from the code under test.
/// </summary>
public sealed class CommandTests : IDisposable
{
    private static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "kurz.dll");

    private static readonly string Host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private readonly string root = Directory.CreateTempSubdirectory("kurz-command-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>Runs <c>kurz</c> with the arguments given; its exit code and both streams.</summary>
    private static (int Code, string Output, string Error) Kurz(params string[] arguments)
    {
        Assert.True(File.Exists(Dll), $"the command is not built beside the suite: {Dll}");
        return Processes.Capture(Host, [Dll, .. arguments], null, TimeSpan.FromMinutes(2));
    }

    /// <summary>Runs the command line in this process with the search given; its exit code and both streams.</summary>
    private static (int Code, string Output, string Error) Run(Func<ClangSearch> find, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Command.Run(arguments, output, error, find);
        return (code, output.ToString(), error.ToString());
    }

    /// <summary>A search that finds a clang 23.1.4 the test made, named <c>clang-23</c> on a path of its own.</summary>
    private (Func<ClangSearch> Find, string Path) Found()
    {
        var directory = Path.Combine(root, "bin");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "clang-23");
        File.WriteAllBytes(file, []);
        return (() => Clang.Find(directory, null, windows: false, _ => "clang version 23.1.4\n"), file);
    }

    [Fact]
    public void Version_writes_one_line_with_the_compiler_the_pinned_major_and_the_clang_found()
    {
        var (code, output, error) = Kurz("--version");
        Assert.Equal((0, ""), (code, error));
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var match = Regex.Match(line, @"^kurz 0\.1\.0 \(LLVM 23, clang 23\.\d+\.\d+ at (?<path>.+)\)$");
        Assert.True(match.Success, line);
        Assert.True(File.Exists(match.Groups["path"].Value), line);
        Assert.EndsWith("\n", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_verbose_writes_the_same_on_lines_of_their_own()
    {
        var (code, output, error) = Kurz("--version", "--verbose");
        Assert.Equal((0, ""), (code, error));
        var match = Regex.Match(output, @"^kurz 0\.1\.0\nLLVM version: 23\nclang: (?<path>.+)\nclang version: 23\.\d+\.\d+\n$");
        Assert.True(match.Success, output);
        Assert.True(File.Exists(match.Groups["path"].Value), output);
    }

    [Fact]
    public void Both_forms_name_the_clang_found_in_the_words_the_owner_chose()
    {
        var (find, path) = Found();
        Assert.Equal((0, $"kurz 0.1.0 (LLVM 23, clang 23.1.4 at {path})\n", ""), Run(find, "--version"));
        var verbose = (0, $"kurz 0.1.0\nLLVM version: 23\nclang: {path}\nclang version: 23.1.4\n", "");
        Assert.Equal(verbose, Run(find, "--version", "--verbose"));
        Assert.Equal(verbose, Run(find, "--verbose", "--version"));
    }

    /// <summary>
    /// In this process, with the search over an empty directory given: on the Windows runner a child
    /// process read <c>ProgramFiles</c> as the system's folder whatever its parent set, so on Windows
    /// no environment of a child empties the search
    /// (knowledge/a-windows-child-gets-its-own-program-files.md).
    /// </summary>
    [Fact]
    public void Version_with_no_clang_found_writes_what_it_knows_the_places_looked_at_and_exits_1()
    {
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        var note = OperatingSystem.IsWindows()
            ? $"looked for clang-23.exe and clang.exe on the path and in the LLVM installer directory, {Path.Combine(empty, "LLVM", "bin")}"
            : "looked for clang-23 and clang on the path";
        var failure = $"error: no clang found\n  note: {note}\n  help: install LLVM 23\n";
        ClangSearch Find() => Clang.Find(empty, empty, OperatingSystem.IsWindows(), file => throw new InvalidOperationException($"asked {file}"));
        Assert.Equal((1, "kurz 0.1.0 (LLVM 23)\n", failure), Run(Find, "--version"));
        Assert.Equal((1, "kurz 0.1.0\nLLVM version: 23\n", failure), Run(Find, "--version", "--verbose"));
    }

    [Fact]
    public void A_refused_clang_ends_the_command_with_1_and_its_three_lines()
    {
        var search = new ClangSearch(null, Path.Combine(root, "clang"), "20.1.2", ["clang-23 on the path", "clang on the path"]);
        Assert.Equal(
            (1, "kurz 0.1.0 (LLVM 23)\n", $"error: clang 20.1.2 is not LLVM 23\n  note: found at {Path.Combine(root, "clang")}\n  help: install LLVM 23, ahead of this clang on the path\n"),
            Run(() => search, "--version"));
    }

    [Theory]
    [InlineData]
    [InlineData("build", "hello.kz")]
    [InlineData("--version", "--release")]
    [InlineData("--Version")]
    [InlineData("--verbose")]
    [InlineData("--version", "--verbose", "--verbose")]
    public void A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1(params string[] arguments) =>
        Assert.Equal((1, "", "usage: kurz --version [--verbose]\n"), Kurz(arguments));
}
