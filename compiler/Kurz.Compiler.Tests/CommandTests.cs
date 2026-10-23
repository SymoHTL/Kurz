using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The command <c>kurz</c>, run as a process from the suite's output directory, where the command's
/// project is built beside the suite, and in this process with the clang search given: what
/// <c>--version</c> writes where and exits with (design record, section 2), and the usage.
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

    private static string Head => $"kurz {Command.CompilerVersion}\nLLVM version: 23\n";

    [Fact]
    public void Version_names_the_compiler_the_pinned_major_and_the_clang_found()
    {
        var clang = Clang.Find().Found;
        Assert.True(clang is not null, "no clang of the pinned major on this machine: ClangTests says why");
        Assert.Matches(@"^\d+\.\d+\.\d+$", Command.CompilerVersion); // the release alone, no commit appended
        Assert.Equal((0, $"{Head}clang: {clang.Path}\nclang version: {clang.Version}\n", ""), Kurz("--version"));
    }

    /// <summary>
    /// In this process, with the search over an empty directory given: as a process on the Windows
    /// runner the command found the clang under the system's Program Files although its environment
    /// named an empty directory for <c>PATH</c> and for <c>ProgramFiles</c> (the compiler-windows run
    /// of 2026-10-23), so no environment the test sets empties the search there.
    /// </summary>
    [Fact]
    public void Version_with_no_clang_on_the_path_writes_its_first_lines_the_places_looked_at_and_exits_1()
    {
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        var note = OperatingSystem.IsWindows()
            ? $"looked for clang-23.exe and clang.exe on the path and in the LLVM installer directory, {Path.Combine(empty, "LLVM", "bin")}"
            : "looked for clang-23 and clang on the path";
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Command.Run(["--version"], output, error, () => Clang.Find(empty, empty, OperatingSystem.IsWindows(), file => throw new InvalidOperationException($"asked {file}")));
        Assert.Equal((1, Head, $"error: no clang found\n  note: {note}\n  help: install LLVM 23\n"), (code, output.ToString(), error.ToString()));
    }

    [Fact]
    public void A_refused_clang_ends_the_command_with_1_after_the_first_lines()
    {
        var search = new ClangSearch(null, Path.Combine(root, "clang"), "20.1.2", ["clang-23 on the path", "clang on the path"]);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Command.Run(["--version"], output, error, () => search);
        Assert.Equal((1, Head, Command.Failure(search)), (code, output.ToString(), error.ToString()));
        Assert.StartsWith("error: clang 20.1.2 is not LLVM 23\n", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("build", "hello.kz")]
    [InlineData("--version", "--release")]
    [InlineData("--Version")]
    public void A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1(params string[] arguments) =>
        Assert.Equal((1, "", "usage: kurz --version\n"), Kurz(arguments));
}
