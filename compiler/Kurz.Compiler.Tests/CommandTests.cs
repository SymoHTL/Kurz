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
        Assert.Equal((1, "", $"error: overflow at {source}:1\n"), Processes.Capture(output, [], null, TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        Assert.Equal((0, "", ""), Kurz("build", "--release", "-o", output, source));
        Assert.Equal((0, "-2147483648\n", ""), Processes.Capture(output, [], null, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Build_writes_the_line_tables_into_a_test_build_and_into_a_release_build_where_lines_says_so()
    {
        // the owner, 2026-10-29, "build, run and exec": `--lines` keeps them in a release build and changes nothing in a test build
        var source = Path.Combine(root, "lines.kz");
        File.WriteAllText(source, "print(1)\n");
        var output = Path.Combine(root, OperatingSystem.IsWindows() ? "lines.exe" : "lines");
        (string[] Options, bool Tables)[] builds = [([], true), (["--lines"], true), (["--release"], false), (["--release", "--lines"], true), (["--lines", "--release"], true)];
        foreach (var (options, tables) in builds)
        {
            File.Delete(Path.ChangeExtension(output, ".pdb")); // the PDB of the build before
            Assert.Equal((0, "", ""), Kurz(["build", .. options, source, "-o", output]));
            Assert.Equal((string.Join(' ', options), tables), (string.Join(' ', options), Toolchain.HasLineTables(output)));
            Assert.Equal((0, "1\n", ""), Processes.Capture(output, [], null, TimeSpan.FromSeconds(30)));
        }
    }

    [Theory]
    [InlineData("print(1)\nprint(y)\n", ":2: unknown-name: ")] // a compile error, in the record's form
    [InlineData("s = \"ab\"\nprint(s.Chars[0])\n", ":2: not supported: `s.Chars` as a list (T15)\n")] // the emitter's not supported
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
    public void A_nullable_unchecked_error_says_how_to_check_on_the_line_below_and_no_other_error_does()
    {
        // section 4, the owner, 2026-10-30, "Text + help line", indented as the command's own `help:` (an assumption there)
        var source = Path.Combine(root, "refused.kz");
        File.WriteAllText(source, "string? Name() => null\n\nprint(Name().Bytes.Count)\nprint(y)\n");
        var (code, output, error) = Kurz("build", source);
        Assert.Equal((1, ""), (code, output));
        var lines = error.Split('\n');
        Assert.StartsWith($"{source}:3: nullable-unchecked: ", lines[0], StringComparison.Ordinal);
        Assert.Equal("  help: check it with `!= null`, or give a value for null with `??`", lines[1]);
        Assert.StartsWith($"{source}:4: unknown-name: ", lines[2], StringComparison.Ordinal);
        Assert.Equal([""], lines[3..]);
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
    [InlineData("build", "--lines", "--lines", "a.kz")]
    [InlineData("build", "a.kz", "-o")]
    [InlineData("build", "--fast", "a.kz")]
    [InlineData("--version", "--release")]
    [InlineData("--Version")]
    [InlineData("--verbose")]
    [InlineData("--version", "--verbose", "--verbose")]
    [InlineData("run")]
    [InlineData("run", "a.kz", "b.kz")]
    [InlineData("run", "--release", "--release", "a.kz")]
    [InlineData("run", "--lines", "--lines", "a.kz")]
    [InlineData("run", "a.kz", "-o", "out")]
    [InlineData("run", "--", "a.kz")]
    [InlineData("run", "--release", "--", "a.kz")]
    [InlineData("exec")]
    [InlineData("exec", "--release")]
    [InlineData("exec", "print(1)", "print(2)")]
    [InlineData("exec", "print(1)", "--release")]
    [InlineData("exec", "--lines")]
    [InlineData("exec", "--release", "--lines")]
    [InlineData("exec", "--lines", "--lines", "print(1)")]
    [InlineData("exec", "print(1)", "--lines")]
    [InlineData("exec", "--", "print(1)")]
    [InlineData("exec", "print(1)", "a", "--", "b")]
    public void A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1(params string[] arguments) =>
        Assert.Equal(
            (1, "", "usage: kurz --version [--verbose]\n       kurz build [--release] [--lines] <file.kz> [-o <output>]\n"
                + "       kurz run [--release] [--lines] <file.kz> [-- <argument>...]\n       kurz exec [--release] [--lines] \"<program text>\" [-- <argument>...]\n"
                + "       run and exec pass the words after -- to the program\n"),
            Kurz(arguments));

    /// <summary>
    /// Runs <c>kurz</c> with the places under the test's directory on Linux; on Windows the known folder is the system's answer and
    /// no variable of the child's moves it (knowledge/a-windows-child-gets-its-own-program-files.md), so the places are the user's.
    /// </summary>
    private (int Code, string Output, string Error) KurzWithPlaces(params string[] arguments) =>
        Processes.Capture(Host, [Dll, .. arguments], null, TimeSpan.FromMinutes(2), new Dictionary<string, string?> { ["XDG_CACHE_HOME"] = Path.Combine(root, "cache"), ["KURZ_TRACE"] = "0" }); // the program run inherits the switch: these tests hold the error line, TraceTests the trace

    /// <summary>
    /// No place left once the runs ended: on Linux the directory of the places under the test's directory is the user's alone and
    /// empty, or not there where no run <paramref name="made"/> a place; on Windows the user's holds no place, which another
    /// <c>kurz run</c> of the user's at the same moment would hold.
    /// </summary>
    private void NoPlaceLeft(bool made)
    {
        var places = OperatingSystem.IsWindows() ? Place.Root()! : Path.Combine(root, "cache", "kurz");
        if (OperatingSystem.IsWindows())
        {
            Assert.Empty(Directory.Exists(places) ? Directory.EnumerateFileSystemEntries(places, Place.Prefix + "*") : []);
            return;
        }

        Assert.Equal(made, Directory.Exists(places));
        if (!made)
        {
            return;
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(places));
        Assert.Empty(Directory.EnumerateFileSystemEntries(places));
    }

    [Fact]
    public void Run_and_exec_build_the_program_run_it_with_the_commands_streams_and_end_with_its_exit_code()
    {
        var source = Path.Combine(root, "add.kz");
        File.WriteAllText(source, "int Next(int n) => n + 1\n\nprint(\"next\")\nprint(Next(2147483647))\n");
        Assert.Equal((1, "next\n", $"error: overflow at {source}:1\n"), KurzWithPlaces("run", source));
        Assert.Equal((0, "next\n-2147483648\n", ""), KurzWithPlaces("run", "--release", source));
        Assert.Equal((0, "1\n", ""), KurzWithPlaces("exec", "print(1)"));
        Assert.Equal((1, "2\n", "error: overflow at exec:2\n"), KurzWithPlaces("exec", "print(2)\nint Next(int n) => n + 1\nprint(Next(2147483647))"));
        Assert.Equal((0, "-2147483648\n", ""), KurzWithPlaces("exec", "--release", "int Next(int n) => n + 1\nprint(Next(2147483647))"));
        Assert.Equal((0, "", ""), KurzWithPlaces("exec", "")); // an empty text is an empty program, as an empty file is
        NoPlaceLeft(made: true);
    }

    [Fact]
    public void Run_and_exec_give_the_program_the_words_after_the_first_dashes_as_they_were_given()
    {
        // S13; the owner, 2026-10-30, "After `--`, as cargo": a further `--`, a flag of the command and a word with a space are the program's
        var source = Path.Combine(root, "words.kz");
        File.WriteAllText(source, "print(args)\nprint(Environment.Args().Count)\n");
        if (OperatingSystem.IsWindows())
        {
            // S11: the runtime of Windows gives no argument, and the build says so; the words reach the start of the program as
            // `The_program_takes_the_commands_input_output_and_error_the_words_given_and_the_callers_directory` holds
            var (code, output, error) = KurzWithPlaces("run", source, "--", "a");
            Assert.Equal((1, ""), (code, output));
            Assert.StartsWith($"{source}:1: not supported: ", error, StringComparison.Ordinal);
            return;
        }

        Assert.Equal((0, "[a, --, --release, b c]\n5\n", ""), KurzWithPlaces("run", source, "--", "a", "--", "--release", "b c"));
        Assert.Equal((0, "[]\n1\n", ""), KurzWithPlaces("run", "--release", source, "--"));
        Assert.Equal((0, "[]\n1\n", ""), KurzWithPlaces("run", source)); // the positive control of the split: no `--`, no word
        Assert.Equal((0, "[x]\n", ""), KurzWithPlaces("exec", "--lines", "print(args)", "--", "x"));

        // S13: the program's name is the executable in the command's place, as `cargo run` gives its program the path in `target`
        var named = Path.Combine(root, "named.kz");
        File.WriteAllText(named, "print(Environment.Args()[0])\n");
        foreach (var (code, name, error) in new[] { KurzWithPlaces("run", named), KurzWithPlaces("exec", "print(Environment.Args()[0])") })
        {
            Assert.Equal((0, ""), (code, error));
            Assert.Matches($@"^{Regex.Escape(Path.Combine(root, "cache", "kurz"))}/place-[0-9a-f]+/program\n$", name);
        }
    }

    [Fact]
    public void A_program_run_or_exec_refuses_gets_its_lines_named_by_the_file_or_exec_and_exit_1()
    {
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(y)\n");
        foreach (var (refused, line) in new[] { (KurzWithPlaces("run", source), $"{source}:1: unknown-name: "), (KurzWithPlaces("exec", "print(1)\nprint(x)"), "exec:2: unknown-name: ") })
        {
            Assert.Equal((1, "", 1), (refused.Code, refused.Output, refused.Error.Count(c => c == '\n'))); // one line, whose words are the checker's
            Assert.StartsWith(line, refused.Error, StringComparison.Ordinal);
        }
        var (code, output, error) = KurzWithPlaces("run", Path.Combine(root, "missing.kz"));
        Assert.Equal((1, ""), (code, output));
        Assert.Matches(@"^error: the source cannot be read\n  note: .+\n  help: name a Kurz source file that can be read\n$", error);
        NoPlaceLeft(made: false); // a program refused, or not read, makes no place
    }

    [Fact]
    public void A_lint_is_written_as_an_error_is_with_warning_before_its_id_and_the_program_runs()
    {
        // H5: `app.kz:3: warning: md5-insecure: <text>`, on the command's error stream, in every build; `Sha256` gets none (the positive control)
        var source = Path.Combine(root, "digest.kz");
        File.WriteAllText(source, "print(Sha256.Hash(Bytes()).Count)\nprint(Md5.Hash(Bytes()).Count)\n");
        foreach (var (run, name) in new[] { (KurzWithPlaces("run", source), source), (KurzWithPlaces("run", "--release", source), source), (KurzWithPlaces("exec", "print(Sha256.Hash(Bytes()).Count)\nprint(Md5.Hash(Bytes()).Count)"), "exec") })
        {
            Assert.Equal((0, "32\n16\n"), (run.Code, run.Output));
            Assert.Matches($@"^{Regex.Escape(name)}:2: warning: md5-insecure: [^\n]+ \(H5\)\n$", run.Error);
        }

        NoPlaceLeft(made: true);
    }

    [Fact]
    public void Run_and_exec_without_a_directory_for_the_places_fail_with_the_owners_words()
    {
        var expected = OperatingSystem.IsWindows()
            ? "error: no directory for the command's places\n  note: Windows names no LocalAppData folder for this user\n  help: run the command as a user with a profile\n"
            : "error: no directory for the command's places\n  note: XDG_CACHE_HOME is not set to an absolute path, and no home directory is found in HOME or the user database\n  help: set HOME, or XDG_CACHE_HOME to an absolute path\n";
        Assert.Equal((1, "", expected), RunWithPlaces(Builder.HostTarget, () => null, Found().Find, "exec", "print(1)"));
    }

    [Fact]
    public void On_linux_a_home_directory_that_is_not_there_holds_the_places_as_dirs_takes_it()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows' known folder is the system's answer, which no variable of a child moves
        }

        // .NET's folder of the user's profile, asked without DoNotVerify, is empty for a home that is not there; `dirs` takes HOME as it is
        var home = Path.Combine(root, "home-not-there");
        var environment = new Dictionary<string, string?> { ["XDG_CACHE_HOME"] = "", ["HOME"] = home };
        Assert.Equal((0, "1\n", ""), Processes.Capture(Host, [Dll, "exec", "print(1)"], null, TimeSpan.FromMinutes(2), environment));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(home, ".cache", "kurz"))); // the place was made there, and removed
    }

    [Fact]
    public void Run_and_exec_without_a_runtime_or_a_clang_fail_as_build_does_and_leave_no_place()
    {
        var places = Path.Combine(root, "places", "kurz");
        var source = Path.Combine(root, "hello.kz");
        File.WriteAllText(source, "print(1)\n");
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        var note = OperatingSystem.IsWindows()
            ? $"looked for clang-23.exe and clang.exe on the path and in the LLVM installer directory, {Path.Combine(empty, "LLVM", "bin")}"
            : "looked for clang-23 and clang on the path";
        var refused = new ClangSearch(null, Path.Combine(root, "clang"), "20.1.2", ["clang-23 on the path", "clang on the path"]);
        var (find, path) = Found(); // an empty file named clang-23, which does not start
        foreach (var arguments in new[] { new[] { "run", source }, ["exec", "print(1)"] })
        {
            Assert.Equal(
                (1, "", "error: no runtime for this system\n  note: the runtime has a directory for 64-bit Linux and one for 64-bit Windows\n  help: build on 64-bit Linux or Windows\n"),
                RunWithPlaces(null, () => places, () => throw new InvalidOperationException("the search ran"), arguments));
            Assert.Equal(
                (1, "", $"error: no clang found\n  note: {note}\n  help: install LLVM 23\n"),
                RunWithPlaces(Builder.HostTarget, () => places, () => Clang.Find(empty, empty, OperatingSystem.IsWindows(), file => throw new InvalidOperationException($"asked {file}")), arguments));
            Assert.Equal(
                (1, "", $"error: clang 20.1.2 is not LLVM 23\n  note: found at {Path.Combine(root, "clang")}\n  help: install LLVM 23, ahead of this clang on the path\n"),
                RunWithPlaces(Builder.HostTarget, () => places, () => refused, arguments));
            Assert.False(Directory.Exists(places), "a place was made before the clang was found");

            var (code, output, error) = RunWithPlaces(Builder.HostTarget, () => places, find, arguments); // the clang runs in the place
            Assert.Equal((1, ""), (code, output));
            Assert.StartsWith($"error: clang did not build the program\n  note: {path} exited with -1 and wrote:\n    ", error, StringComparison.Ordinal);
            Assert.EndsWith("\n  help: check that the clang found runs; the compiler accepted the program, so a clang that runs and fails on it is the compiler's defect\n", error, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(places));
            Directory.Delete(places);
        }
    }

    [Fact]
    public void A_place_that_cannot_be_made_and_a_program_that_does_not_start_fail_with_the_owners_words()
    {
        var file = Path.Combine(root, "a-file");
        File.WriteAllText(file, "");
        var places = Path.Combine(file, "kurz"); // a directory under a file, which no system makes
        var (code, output, error) = RunWithPlaces(Builder.HostTarget, () => places, Found().Find, "exec", "print(1)");
        Assert.Equal((1, ""), (code, output));
        Assert.Matches($@"^error: the place for the run cannot be made\n  note: .+\n  help: check that {Regex.Escape(places)} can be written\n$", error);

        using var writer = new StringWriter();
        Assert.Equal(1, Command.RunProgram(file, [], root, writer)); // an empty file, which no system starts
        Assert.Matches($@"^error: the built program cannot be started\n  note: .+\n  help: check that programs may run from {Regex.Escape(root)}\n$", writer.ToString());
    }

    /// <summary>Runs the command line in this process for the machine, with the places and the search given.</summary>
    private static (int Code, string Output, string Error) RunWithPlaces(Target? host, Func<string?> places, Func<ClangSearch> find, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Command.Run(arguments, output, error, find, host, places);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void The_program_takes_the_commands_input_output_and_error_the_words_given_and_the_callers_directory()
    {
        // the owner, 2026-10-29, "Inherit all three": nothing redirected; 2026-10-30, "After `--`, as cargo": the words as they were given
        var info = Processes.Inheriting("program", ["a", "b c", ""]);
        Assert.Equal(
            (false, false, false, false, ""),
            (info.UseShellExecute, info.RedirectStandardInput, info.RedirectStandardOutput, info.RedirectStandardError, info.WorkingDirectory));
        Assert.Equal(["a", "b c", ""], info.ArgumentList);
        Assert.Empty(Processes.Inheriting("program", []).ArgumentList);
    }

    [Fact]
    public void On_linux_an_interrupt_or_a_quit_ends_the_program_alone_and_the_command_ends_with_its_code()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows sends Ctrl-C to every process of a console, the test's own among them: nothing checks it there
        }

        // the owner, 2026-10-29, "Command waits it out": the command outlives an interrupt and a quit, and ends with 128 + 2
        var info = new System.Diagnostics.ProcessStartInfo(Host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { Dll, "exec", "while true {\n}" })
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["XDG_CACHE_HOME"] = Path.Combine(root, "cache");
        using var command = System.Diagnostics.Process.Start(info)!;
        try
        {
            var program = ProgramOf(command.Id, TimeSpan.FromMinutes(2));
            Assert.Equal(0, Kill(command.Id, 2));
            Assert.Equal(0, Kill(command.Id, 3));
            Assert.False(command.WaitForExit(1000), "the command ended by the interrupt or the quit sent to it");
            Assert.Equal(0, Kill(program, 2));
            Assert.True(command.WaitForExit(30000), "the command did not end when its program did");
            Assert.Equal((130, "", ""), (command.ExitCode, command.StandardOutput.ReadToEnd(), command.StandardError.ReadToEnd()));
            NoPlaceLeft(made: true);
        }
        finally
        {
            if (!command.HasExited)
            {
                command.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>The process number of the built program the command started, a child named <c>program</c> and not the clang of the build, read from <c>/proc</c>.</summary>
    private static int ProgramOf(int command, TimeSpan limit)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < limit)
        {
            foreach (var task in Directory.EnumerateDirectories($"/proc/{command}/task"))
            {
                foreach (var child in File.ReadAllText(Path.Combine(task, "children")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        if (File.ReadAllText($"/proc/{child}/cmdline").Split('\0')[0].EndsWith("/program", StringComparison.Ordinal))
                        {
                            return int.Parse(child, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    catch (IOException)
                    {
                        // a clang that ended between the two reads
                    }
                }
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"the command {command} started no program within {limit}");
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int process, int signal);
}
