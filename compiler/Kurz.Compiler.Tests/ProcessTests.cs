using System.Globalization;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the command line, the environment and the clock (S13 to S15; design record, section 12) that no corpus case
/// pins, since a case runs with no argument, no variable of its own and no file of a known time: <c>args</c> holds the words the
/// process was given and <c>Environment.Args()</c> the program's name before them (S13, "Both"); an argument that is not UTF-8
/// ends the process with <c>invalid-argument</c> where it is read, before the first statement with no place and no trace for
/// <c>args</c> and at the call with both for <c>Environment.Args()</c>, and not at all where nothing reads it (S13, "Refused, as
/// Rust"); a variable gives its value, and one that is not set, whose value is not UTF-8, or whose name is empty or holds <c>=</c>
/// or a NUL byte the <c>EnvError</c> of its kind (S14, "An outcome, as Rust"); <c>timestamp.Now</c> is the time of the wall clock
/// and <c>File.Modified</c> a file's or a directory's time to the nanosecond, with the edges of the 64 bits of a <c>timestamp</c>
/// (S15); and a build for Windows says <c>not supported</c> at each of the five. Each program runs under the runtime of this
/// machine; on Windows the tests hold the build's refusal instead.
/// </summary>
public sealed class ProcessTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and started by <c>/bin/sh</c> as <c>exec "$0" &lt;words&gt;</c> after the shell command
    /// <paramref name="setup"/>, with <c>KURZ_TRACE</c> set to <paramref name="trace"/>, or taken away where it is null; its exit code,
    /// its lines and its error stream, with the executable's path. Null on Windows, where the build is held to its refusal.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error, string Executable)? Ran(string text, string name, string words, string setup = "", string? trace = "0")
    {
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Compile(text, toolchain.Runtime).Program);
            return null;
        }

        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup}exec \"$0\" {words}", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = trace });
        return (code, Toolchain.Lines(output), error, executable);
    }

    [Fact]
    public void The_arguments_are_the_words_the_process_was_given_and_the_whole_list_names_the_program_first()
    {
        const string text = "print(args)\nall = Environment.Args()\nprint(all.Count)\nprint(all[0])\n";
        if (Ran(text, "arguments-words", "a 'b c' ''") is { } given)
        {
            Assert.Equal((0, ""), (given.Code, given.Error));
            Assert.Equal(["[a, b c, ]", "4", given.Executable], given.Output);
        }

        if (Ran(text, "arguments-none", "") is { } none)
        {
            Assert.Equal((0, ""), (none.Code, none.Error));
            Assert.Equal(["[]", "1", none.Executable], none.Output);
        }
    }

    [Fact]
    public void An_argument_that_is_not_utf8_ends_the_process_with_invalid_argument_where_it_is_read()
    {
        const string bad = "ok \"$(printf 'x\\377')\"";

        // `args`, made before the first statement: no line printed, no place and no trace, with the trace switched on
        if (Ran("print(1)\nprint(args)\n", "argument-top", bad, trace: null) is { } top)
        {
            Assert.Equal((1, "error: invalid-argument\n"), (top.Code, top.Error));
            Assert.Empty(top.Output);
        }

        if (Ran("print(1)\nprint(args)\n", "argument-top-control", "ok x", trace: null) is { } control)
        {
            Assert.Equal((0, ""), (control.Code, control.Error)); // the positive control
            Assert.Equal(["1", "[ok, x]"], control.Output);
        }

        // `Environment.Args()`, at its call with its place and its trace
        if (Ran("print(1)\nall = Environment.Args()\nprint(all.Count)\n", "argument-call", bad, trace: null) is { } call)
        {
            Assert.Equal((1, "error: invalid-argument at program.kz:2\n  at main in program.kz:2\n"), (call.Code, call.Error));
            Assert.Equal(["1"], call.Output);
        }

        // a program that reads no argument does not look at them
        if (Ran("print(1)\n", "argument-unread", bad) is { } unread)
        {
            Assert.Equal((0, ""), (unread.Code, unread.Error));
            Assert.Equal(["1"], unread.Output);
        }
    }

    [Fact]
    public void A_variable_gives_its_value_and_one_not_set_not_utf8_or_misnamed_gives_the_env_error_of_its_kind()
    {
        const string text = """
            string Read(string name) => Environment.Variable(name) else {
                EnvError e => "{e} {e.Kind}"
            }

            print(Read("KZ_SET"))
            print(Read("KZ_EMPTY") == "")
            print(Read("KZ_BAD"))
            print(Read("KZ_NOT"))
            print(Read(""))
            print(Read("KZ_EQ=x"))
            print(Read("KZ_SET\0"))

            """;

        // `KZ_EQ=x` would find `KZ_EQ`'s `y` through `getenv`, which reads the name up to its end and the `=` after it in `KZ_EQ=x=y`
        var setup = "export KZ_SET=value KZ_EMPTY= KZ_BAD=\"$(printf 'a\\377')\" KZ_EQ=x=y; unset KZ_NOT; ";
        if (Ran(text, "variables", "", setup) is { } read)
        {
            Assert.Equal((0, ""), (read.Code, read.Error));
            Assert.Equal(
                ["value", "true", "not unicode: KZ_BAD NotUnicode", "not present: KZ_NOT NotPresent", "not present:  NotPresent", "not present: KZ_EQ=x NotPresent", "not present: KZ_SET\0 NotPresent"],
                read.Output);
        }
    }

    [Fact]
    public void The_clock_is_the_systems_wall_clock()
    {
        var before = DateTime.UtcNow;
        if (Ran("print(timestamp.Now)\n", "clock", "") is { } read)
        {
            var after = DateTime.UtcNow;
            Assert.Equal((0, ""), (read.Code, read.Error));

            // .NET reads seven digits of a second: the clock's nine are cut to them, and the two ends given a second each
            var text = Regex.Replace(Assert.Single(read.Output), @"(\.\d{7})\d+Z$", "$1Z");
            var now = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
        }
    }

    [Fact]
    public void The_time_of_a_file_or_a_directory_is_its_last_write_to_the_nanosecond_and_one_a_timestamp_does_not_hold_is_invalid_data()
    {
        // the edges of the 64 bits (T24) after 1970 on the build's file system and before it in the memory of /dev/shm, a tmpfs, whose
        // times reach 1677 where ext4's stop at 1901
        var low = $"/dev/shm/kurz-{Guid.NewGuid():N}";
        const string text = """
            void Show(string path) {
                match File.Modified(path) {
                    timestamp t => print(t)
                    IoError e => print("{e} {e.Kind}")
                }
            }

            Show("top.txt")
            Show("over.txt")
            Show("before.txt")
            Show("folder")
            Show("{low}-low")
            Show("{low}-lower")

            """;
        var setup = $"touch -d @9223372036.854775807 top.txt && touch -d @9223372036.854775808 over.txt && touch -d @-1.5 before.txt && mkdir -p folder && touch -d @1700000000.25 folder && touch -d @-9223372036.854775808 {low}-low && touch -d @-9223372036.854775809 {low}-lower && ";
        try
        {
            if (Ran(text.Replace("{low}", low, StringComparison.Ordinal), "file-modified", "", setup) is { } read)
            {
                Assert.Equal((0, ""), (read.Code, read.Error));
                Assert.Equal(
                    ["2262-04-11T23:47:16.854775807Z", "invalid data: over.txt InvalidData", "1969-12-31T23:59:58.500Z", "2023-11-14T22:13:20.250Z", "1677-09-21T00:12:43.145224192Z", $"invalid data: {low}-lower InvalidData"],
                    read.Output);
            }
        }
        finally
        {
            File.Delete($"{low}-low");
            File.Delete($"{low}-lower");
        }
    }

    [Theory]
    [InlineData("print(1)\nprint(args)\n", 2)]
    [InlineData("print(Environment.Args())\n", 1)]
    [InlineData("print(Environment.Variable(\"HOME\") else {\n    EnvError e => \"{e}\"\n})\n", 1)]
    [InlineData("print(1)\nprint(timestamp.Now)\n", 2)]
    public void A_build_for_windows_says_not_supported_at_a_read_of_the_command_line_the_environment_or_the_clock(string text, int line)
    {
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((line, Emitter.ProcessNotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }
}
