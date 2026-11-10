using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on standard input (S22; design record, section 12) that no corpus case pins, since a case runs with nothing on its
/// standard input (chapter 0): the lines given to a program are read as a file's are, <c>\n</c> and <c>\r\n</c> ending a line and
/// taken off it, and the next read goes on where a loop that stopped left the one buffer of the process, through
/// <c>Stdin.ReadLine()</c>, <c>Stdin.Read(mut into, max)</c> and <c>Stdin.ReadAll()</c>, <c>ReadLine</c> giving <c>null</c> at the
/// end; a line that is not UTF-8 is <c>InvalidUtf8</c> and is taken, so that the next read gives the line after it; a closed
/// descriptor 0 is a failed read, an <c>IoError</c> naming <c>&lt;stdin&gt;</c>; and a build for Windows says <c>not supported</c>
/// at each of the four. Each program runs under the runtime of this machine, through <c>/bin/sh</c>, which gives it its input; on
/// Windows the tests hold the build's refusal instead.
/// </summary>
public sealed class StdinTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and started by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="input"/>, a shell
    /// command whose output is piped into it, or with the redirections <paramref name="redirect"/> given; its exit code, its lines and
    /// its error stream. Null on Windows, where the build is held to its refusal.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string input, string redirect = "")
    {
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Compile(text, toolchain.Runtime).Program);
            return null;
        }

        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var command = input.Length == 0 ? $"exec \"$0\" {redirect}" : $"{input} | {{ exec \"$0\" {redirect}; }}";
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", command, executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" });
        return (code, Toolchain.Lines(output), error);
    }

    private const string Across = """
        void | IoError | InvalidUtf8 Run() {
            for line in Stdin.Lines() {
                print("line {line}")
                if line == "b" {
                    break
                }
            }

            match Stdin.ReadLine() {
                string l => print("next {l}")
                null => print("end")
                IoError e => print(e)
                InvalidUtf8 e => print(e)
            }

            mut into = Bytes()
            print(Stdin.Read(mut into, 2))
            print(string.FromUtf8(into))
            rest = Stdin.ReadAll()
            print(rest.Count)
            print(string.FromUtf8(rest))
            match Stdin.ReadLine() {
                string l => print("next {l}")
                null => print("end")
                IoError e => print(e)
                InvalidUtf8 e => print(e)
            }

            print(Stdin.ReadAll().Count)
        }

        Run() else {
            IoError e => print(e)
            InvalidUtf8 e => print(e)
        }
        """;

    [Fact]
    public void The_reads_after_a_loop_over_the_lines_go_on_where_it_stopped()
    {
        // S22: `\r\n` and `\n` end a line; the loop that broke left `c` in the one buffer, which `ReadLine` gives, `Read` takes two bytes
        // of what follows, `ReadAll` the rest with the last line that has no line break, and after it `ReadLine` gives `null` and
        // `ReadAll` no byte
        if (Ran(Across, "stdin-across", "printf 'a\\r\\nb\\nc\\nxyz\\nlast'") is { } run)
        {
            Assert.True((0, "") == (run.Code, run.Error), $"exit {run.Code}: {run.Error}");
            Assert.Equal(["line a", "line b", "next c", "2", "xy", "6", "z", "last", "end", "0"], run.Output);
        }
    }

    [Fact]
    public void A_line_that_is_not_utf8_is_taken_and_the_next_read_gives_the_line_after_it()
    {
        // S22 with T15: a loop ends at such a line with `InvalidUtf8`, whose `At` is the position in the line, and so does a
        // `ReadLine`; each takes the line, so that the read after it goes on with the next one
        const string text = """
            void | IoError Run() {
                for line in Stdin.Lines() {
                    print(line)
                } else {
                    InvalidUtf8 u => print("bad line at {u.At}")
                }

                mut more = true
                while more {
                    match Stdin.ReadLine() {
                        string l => print("then {l}")
                        null => {
                            print("end")
                            more = false
                        }
                        IoError e => print(e)
                        InvalidUtf8 e => print("bad at {e.At}")
                    }
                }
            }

            Run() else {
                IoError e => print(e)
            }
            """;
        if (Ran(text, "stdin-invalid", "printf 'ok\\nab\\377\\nnext\\n\\376\\nafter\\n'") is { } run)
        {
            Assert.True((0, "") == (run.Code, run.Error), $"exit {run.Code}: {run.Error}");
            Assert.Equal(["ok", "bad line at 2", "then next", "bad at 0", "then after", "end"], run.Output);
        }
    }

    [Fact]
    public void A_closed_standard_input_is_a_failed_read_that_names_stdin()
    {
        // S22: a read of descriptor 0, which the program's caller closed, is the system's error of a descriptor that is not open, 9,
        // whose `IoError` names the path `<stdin>`; the positive control reads the same program with an empty input, which is no failure
        const string text = """
            match Stdin.ReadLine() {
                string l => print(l)
                null => print("end")
                IoError e => print(e)
                InvalidUtf8 e => print(e)
            }

            void | IoError Rest() {
                print(Stdin.ReadAll().Count)
                mut into = Bytes()
                print(Stdin.Read(mut into, 4))
            }

            Rest() else {
                IoError e => print(e)
            }

            void | IoError | InvalidUtf8 Lines() {
                for line in Stdin.Lines() {
                    print(line)
                }

                print("no line")
            }

            Lines() else {
                IoError e => print(e)
                InvalidUtf8 e => print(e)
            }
            """;
        if (Ran(text, "stdin-closed", "", "<&-") is { } closed)
        {
            Assert.True((0, "") == (closed.Code, closed.Error), $"exit {closed.Code}: {closed.Error}");
            Assert.Equal(["other (os error 9): <stdin>", "other (os error 9): <stdin>", "other (os error 9): <stdin>"], closed.Output);
        }

        if (Ran(text, "stdin-closed-control", "") is { } empty)
        {
            Assert.True((0, "") == (empty.Code, empty.Error), $"exit {empty.Code}: {empty.Error}");
            Assert.Equal(["end", "0", "0", "no line"], empty.Output);
        }
    }

    [Fact]
    public void A_captured_program_runs_with_nothing_on_its_standard_input()
    {
        // chapter 0 with S22: `Processes.Capture`, which runs every case, gives the program a pipe closed at its start, not the caller's
        // own standard input, so that a read of it ends at once wherever the suite runs; Linux names the descriptor's file in `/proc`
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var own = new FileInfo("/proc/self/fd/0").LinkTarget;
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", "readlink /proc/self/fd/0; cat; echo end"], null, TimeSpan.FromSeconds(30));
        Assert.True((0, "") == (code, error), $"exit {code}: {error}");
        var lines = Toolchain.Lines(output);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("pipe:", lines[0], StringComparison.Ordinal);
        Assert.NotEqual(own, lines[0]);
        Assert.Equal("end", lines[1]);
    }

    [Theory]
    [InlineData("void | IoError | InvalidUtf8 Echo() {\n    for line in Stdin.Lines() {\n        print(line)\n    }\n}\n\nEcho() else {\n    IoError e => print(e)\n    InvalidUtf8 e => print(e)\n}\n", 2)]
    [InlineData("print(1)\nmatch Stdin.ReadLine() {\n    string l => print(l)\n    null => print(0)\n    IoError e => print(e)\n    InvalidUtf8 e => print(e)\n}\n", 2)]
    [InlineData("void | IoError Rest() {\n    print(Stdin.ReadAll().Count)\n}\n\nRest() else {\n    IoError e => print(e)\n}\n", 2)]
    [InlineData("void | IoError Some() {\n    mut into = Bytes()\n    print(Stdin.Read(mut into, 4))\n}\n\nSome() else {\n    IoError e => print(e)\n}\n", 3)]
    public void A_build_for_windows_says_not_supported_at_standard_input(string text, int line)
    {
        // S22 with S11: as `Environment.Variable` is, each call on `Stdin` and each loop over its lines
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((line, Emitter.StdinNotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }

    [Theory]
    [InlineData("x = Stdin.Lines()\nprint(1)\n", "syntax", 1, "`Stdin.Lines()` stands as the collection of a `for` loop alone (S16, S22)")]
    [InlineData("x = Stdin.Peek()\nprint(1)\n", "unknown-name", 1, "`Stdin` has no function `Peek` (S22, V10)")]
    [InlineData("x = Stdin\nprint(1)\n", "unknown-name", 1, "no `Stdin` is visible here (V10)")]
    [InlineData("void Stdin() {\n}\n\nprint(1)\n", "redeclared", 1, null)]
    public void A_use_of_stdin_outside_its_members_is_refused_where_it_stands(string text, string id, int line, string? message)
    {
        // S22, S16, V9, V10: `Lines()` stands as a loop's collection alone, `Stdin` has its four members and is no value, and a file's
        // own declaration of the name is `redeclared`
        var refused = Compile(text, toolchain.Runtime);
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((id, line), (diagnostic.Id, diagnostic.Line));
        if (message is not null)
        {
            Assert.Equal(message, diagnostic.Message);
        }
    }
}
