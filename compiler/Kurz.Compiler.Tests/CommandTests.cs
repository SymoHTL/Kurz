using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The command <c>kurz</c>, run as a process from the suite's output directory, where the command's
/// project is built beside the suite: what each of <c>build</c>, <c>run</c> and <c>exec</c> writes
/// where and exits with (design record, section 2; the readings in <c>compiler/README.md</c>).
/// </summary>
public sealed class CommandTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "kurz.dll");

    private static readonly string Host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    /// <summary>Runs <c>kurz</c> with the arguments, in the directory; its exit code and both streams.</summary>
    private static (int Code, string Output, string Error) Kurz(string? directory, params string[] arguments)
    {
        Assert.True(File.Exists(Dll), $"the command is not built beside the suite: {Dll}");
        return Processes.Capture(Host, [Dll, .. arguments], directory, TimeSpan.FromMinutes(2));
    }

    private string Directory(string name)
    {
        var directory = Path.Combine(toolchain.Directory, "command-" + name);
        System.IO.Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Exec_builds_the_program_given_as_the_argument_and_runs_it()
    {
        var (code, output, error) = Kurz(null, "exec", "x = 4\nprint(x + 5)\nprint(\"done\")\n");
        Assert.Equal((0, "9\ndone\n", ""), (code, output, error));
    }

    [Fact]
    public void Exec_ends_with_the_exit_code_of_the_program_and_the_runtime_line_names_no_file()
    {
        var (code, output, error) = Kurz(null, "exec", "print(1)\nmut int x = 2147483647\nx = x + 1\nprint(x)\n");
        Assert.Equal((1, "1\n", "error: overflow at line 3\n"), (code, output, error));
    }

    [Fact]
    public void Release_wraps_where_the_test_build_throws()
    {
        var (code, output, error) = Kurz(null, "exec", "mut int x = 2147483647\nx = x + 1\nprint(x)\n", "--release");
        Assert.Equal((0, "-2147483648\n", ""), (code, output, error));
    }

    [Fact]
    public void A_thrown_value_follows_the_error_line_after_a_colon_and_a_space()
    {
        // the corpus case throws "negative" at its line 7 after printing "before"; its header pins the id and the line, this pins the text
        var (code, output, error) = Kurz(CorpusFiles.Root, "run", Path.Combine("corpus", "outcomes", "throw-text.kz"));
        Assert.Equal((1, "before\n", "error: thrown at line 7: negative\n"), (code, output, error));
    }

    [Fact]
    public void Two_defects_give_two_lines_in_the_order_of_their_lines()
    {
        var (code, output, error) = Kurz(null, "exec", "print(y)\nprint(z)\n");
        Assert.Equal((1, ""), (code, output));
        var lines = Toolchain.Lines(error);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("exec:1: unknown-name: ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("exec:2: unknown-name: ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_program_under_exec_gets_one_line_per_diagnostic_named_exec_and_exit_1()
    {
        var (code, output, error) = Kurz(null, "exec", "print(1)\nprint(y)\n");
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("exec:2: unknown-name: ", error, StringComparison.Ordinal);
        Assert.Single(Toolchain.Lines(error));
    }

    [Fact]
    public void A_construct_the_compiler_does_not_read_is_reported_as_not_supported_with_its_line()
    {
        var (code, output, error) = Kurz(null, "exec", "print(1)\ndata Event(timestamp at)\nprint(2)\n");
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("exec:2: not supported: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_builds_a_file_into_a_temporary_place_runs_it_and_leaves_nothing_beside_the_source()
    {
        var directory = Directory("run");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(\"hello\")\n");
        var (code, output, error) = Kurz(directory, "run", "hello.kz");
        Assert.Equal((0, "hello\n", ""), (code, output, error));
        Assert.Equal(["hello.kz"], System.IO.Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_refused_file_is_reported_by_its_name_and_line()
    {
        var directory = Directory("refused");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(1)\nprint(2 +)\n");
        var (code, output, error) = Kurz(directory, "run", "hello.kz");
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("hello.kz:2: syntax: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_writes_the_executable_beside_the_source_named_after_it()
    {
        var directory = Directory("build");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(\"built\")\n");
        var (code, output, error) = Kurz(directory, "build", "hello.kz");
        Assert.Equal((0, "", ""), (code, output, error));
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "hello.exe" : "hello");
        Assert.True(File.Exists(executable), $"no executable at {executable}");
        AssertRuns(executable, "built");
    }

    [Fact]
    public void Build_of_a_refused_program_writes_no_executable()
    {
        var directory = Directory("refused-build");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(y)\n");
        var (code, output, error) = Kurz(directory, "build", "hello.kz");
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("hello.kz:1: unknown-name: ", error, StringComparison.Ordinal);
        Assert.Equal(["hello.kz"], System.IO.Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Build_with_release_writes_the_release_build()
    {
        var directory = Directory("build-release");
        File.WriteAllText(Path.Combine(directory, "wrap.kz"), "mut int x = 2147483647\nx = x + 1\nprint(x)\n");
        var (code, _, error) = Kurz(directory, "build", "wrap.kz", "--release");
        Assert.Equal((0, ""), (code, error));
        AssertRuns(Path.Combine(directory, OperatingSystem.IsWindows() ? "wrap.exe" : "wrap"), "-2147483648");
    }

    [Fact]
    public void A_clang_that_refuses_the_build_is_reported_and_exits_1()
    {
        // an output path inside a directory that does not exist: clang cannot open it and refuses
        var directory = Directory("refusing-clang");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(1)\n");
        var (code, output, error) = Kurz(directory, "build", "hello.kz", "-o", Path.Combine(directory, "no-such-directory", "hello"));
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("kurz: clang did not build the program (exit ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_writes_the_executable_where_o_says()
    {
        var directory = Directory("build-o");
        File.WriteAllText(Path.Combine(directory, "hello.kz"), "print(\"named\")\n");
        var executable = Path.Combine(directory, "out", OperatingSystem.IsWindows() ? "named.exe" : "named");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        var (code, _, error) = Kurz(directory, "build", "hello.kz", "-o", executable);
        Assert.Equal((0, ""), (code, error));
        AssertRuns(executable, "named");
    }

    /// <summary>The built program prints the one line, exits with 0 and writes nothing on its error stream.</summary>
    private static void AssertRuns(string executable, string line)
    {
        var (code, output, error) = Toolchain.Run(executable);
        Assert.Equal([line], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_missing_clang_is_reported_with_the_names_tried_and_exits_1()
    {
        // the command searches the path it is given; an empty directory as the whole path holds no clang
        var empty = Directory("no-clang");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = empty, ["ProgramFiles"] = empty };
        Assert.True(File.Exists(Dll), $"the command is not built beside the suite: {Dll}");
        var (code, output, error) = Processes.Capture(Host, [Dll, "exec", "print(1)"], null, TimeSpan.FromMinutes(2), environment);
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("kurz: clang is not there; looked for ", error, StringComparison.Ordinal);
        Assert.Contains(OperatingSystem.IsWindows() ? "clang.exe" : "clang", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_is_reported_and_exits_1()
    {
        var (code, output, error) = Kurz(Directory("missing"), "build", "nothing.kz");
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith("kurz: nothing.kz: ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("help")]
    [InlineData("build")]
    [InlineData("exec")]
    [InlineData("run", "hello.kz", "-o", "x")]
    [InlineData("build", "hello.kz", "extra.kz")]
    [InlineData("exec", "print(1)", "--debug")]
    public void A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1(params string[] arguments)
    {
        var (code, output, error) = Kurz(null, arguments);
        Assert.Equal((1, "", Command.Usage), (code, output, error));
    }
}
