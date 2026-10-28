using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The command <c>kurz</c>, run as a process from the suite's output directory, where the command's
/// project is built beside the suite, and in this process with the clang search given: what
/// <c>--version</c> and <c>--version --verbose</c> write where and exit with (design record, section 2),
/// what <c>build</c> writes, where, and what it refuses, and the usage. Every expected text is written
/// out here, the compiler's version 0.1.0 and the pinned major 23 among it, and not taken from the code
/// under test.
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
    private static (int Code, string Output, string Error) Run(Func<ClangSearch> find, params string[] arguments) =>
        RunFor(Builder.HostTarget, find, arguments);

    /// <summary>As above, on a machine of the target given, null for one the runtime has none for.</summary>
    private static (int Code, string Output, string Error) RunFor(Target? host, Func<ClangSearch> find, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Command.Run(arguments, output, error, find, host);
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

    [Fact]
    public void Build_writes_the_executable_beside_the_source_named_after_it_and_says_nothing()
    {
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(\"hello\")\n");
        Assert.Equal((0, "", ""), Kurz("build", source));
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "hello.exe" : "hello");
        Assert.Equal((0, "hello\n", ""), Processes.Capture(executable, [], null, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Build_writes_where_o_says_over_a_file_that_is_there_the_test_build_unless_release_says_otherwise()
    {
        var source = Path.Combine(root, "add.kz");
        File.WriteAllText(source, "int Next(int n) => n + 1\n\nprint(Next(2147483647))\n");
        var output = Path.Combine(root, OperatingSystem.IsWindows() ? "out.exe" : "out"); // the Windows linker names an executable without an extension with one
        File.WriteAllText(output, "a file the build writes over");
        Assert.Equal((0, "", ""), Kurz("build", source, "-o", output));
        Assert.Equal((1, "", $"error: overflow at {source}:1\n"), Processes.Capture(output, [], null, TimeSpan.FromSeconds(30)));
        Assert.Equal((0, "", ""), Kurz("build", "--release", "-o", output, source));
        Assert.Equal((0, "-2147483648\n", ""), Processes.Capture(output, [], null, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData("print(1)\nprint(y)\n", ":2: unknown-name: ")] // a compile error, in the record's form
    [InlineData("print(1.5s)\n", ":1: not supported: a value of type `duration` (T13)\n")] // the emitter's not supported
    [InlineData("string s = \"\"\nraw {\n    print(s.Pointer != null)\n}\n", ":2: raw-not-allowed: ")] // the command grants no `raw`
    [InlineData("extern \"C\" {\n    int abs(int n)\n}\n\nprint(1)\n", ":1: native-not-allowed: ")] // nor `native`
    public void Build_of_a_program_the_compiler_refuses_writes_its_lines_and_no_executable_and_exits_1(string text, string line)
    {
        var source = Path.Combine(root, "refused.kz");
        File.WriteAllText(source, text);
        var (code, output, error) = Kurz("build", source);
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith(source + line, error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, OperatingSystem.IsWindows() ? "refused.exe" : "refused")));
    }

    [Fact]
    public void Build_refuses_to_write_over_its_source_or_a_directory()
    {
        var source = Path.Combine(root, "hello");
        File.WriteAllText(source, "print(1)\n");
        var overSource = $"error: the executable would be written over its source\n  note: both are {source}\n  help: name another output with -o\n";
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal((1, "", overSource), Kurz("build", source)); // beside a source with no extension: its own name
        }

        Assert.Equal((1, "", overSource), Kurz("build", source, "-o", source));
        Assert.Equal("print(1)\n", File.ReadAllText(source));
        var directory = Path.Combine(root, "dir");
        Directory.CreateDirectory(directory);
        Assert.Equal((1, "", $"error: the output is a directory\n  note: {directory} is a directory\n  help: name a file with -o\n"), Kurz("build", source, "-o", directory));
        var missing = Path.Combine(root, "missing");
        Assert.Equal(
            (1, "", $"error: the output's directory does not exist\n  note: {missing} is not there\n  help: name a file in a directory that exists with -o\n"),
            Kurz("build", source, "-o", Path.Combine(missing, "out")));
    }

    [Fact]
    public void Build_on_a_machine_the_runtime_has_no_directory_for_is_the_commands_own_failure()
    {
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(1)\n");
        Assert.Equal(
            (1, "", "error: no runtime for this system\n  note: the runtime has a directory for 64-bit Linux and one for 64-bit Windows\n  help: build on 64-bit Linux or Windows\n"),
            RunFor(null, () => throw new InvalidOperationException("the search ran"), "build", source));
    }

    [Theory]
    [InlineData(Architecture.X64, true, false, Target.Windows)]
    [InlineData(Architecture.Arm64, true, false, Target.Windows)]
    [InlineData(Architecture.X64, false, true, Target.Linux)]
    [InlineData(Architecture.Arm64, false, true, Target.Linux)]
    [InlineData(Architecture.X86, false, true, null)] // a 32-bit machine: a pointer is not 8 bytes, nor C's size_t a long
    [InlineData(Architecture.Arm, true, false, null)]
    [InlineData(Architecture.X64, false, false, null)] // a system the runtime has no directory for
    public void The_target_is_a_64_bit_Linux_or_Windows_machine(Architecture architecture, bool windows, bool linux, Target? target) =>
        Assert.Equal(target, Builder.TargetFor(architecture, windows, linux));

    [Fact]
    public void Build_with_a_clang_that_does_not_build_writes_what_it_wrote_and_exits_1()
    {
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(1)\n");
        var (find, path) = Found(); // an empty file named clang-23, which does not start
        var (code, output, error) = Run(find, "build", source);
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith($"error: clang did not build the program\n  note: {path} exited with -1 and wrote:\n    ", error, StringComparison.Ordinal);
        Assert.EndsWith("\n  help: check that the clang found runs; the compiler accepted the program, so a clang that runs and fails on it is the compiler's defect\n", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_of_a_source_that_is_not_there_is_the_commands_own_failure()
    {
        var (code, output, error) = Kurz("build", Path.Combine(root, "missing.kz"));
        Assert.Equal((1, ""), (code, output));
        Assert.Matches(@"^error: the source cannot be read\n  note: .+\n  help: name a Kurz source file that can be read\n$", error);
    }

    [Fact]
    public void Build_with_a_refused_clang_writes_the_search_once_the_program_was_accepted_and_exits_1()
    {
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(1)\n");
        var search = new ClangSearch(null, Path.Combine(root, "clang"), "20.1.2", ["clang-23 on the path", "clang on the path"]);
        Assert.Equal(
            (1, "", $"error: clang 20.1.2 is not LLVM 23\n  note: found at {Path.Combine(root, "clang")}\n  help: install LLVM 23, ahead of this clang on the path\n"),
            Run(() => search, "build", source));

        // a program the compiler refuses gets its lines and no search
        File.WriteAllText(source, "print(y)\n");
        var (code, output, error) = Run(() => throw new InvalidOperationException("the search ran"), "build", source);
        Assert.Equal((1, ""), (code, output));
        Assert.StartsWith($"{source}:1: unknown-name: ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("build")]
    [InlineData("build", "a.kz", "b.kz")]
    [InlineData("build", "--release", "--release", "a.kz")]
    [InlineData("build", "a.kz", "-o")]
    [InlineData("build", "--fast", "a.kz")]
    [InlineData("--version", "--release")]
    [InlineData("--Version")]
    [InlineData("--verbose")]
    [InlineData("--version", "--verbose", "--verbose")]
    public void A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1(params string[] arguments) =>
        Assert.Equal((1, "", "usage: kurz --version [--verbose]\n       kurz build [--release] <file.kz> [-o <output>]\n"), Kurz(arguments));
}
