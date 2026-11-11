using System.Net;
using System.Net.Sockets;
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
/// end; a line that is not UTF-8 is <c>InvalidUtf8</c> and is taken, so that the next read gives the line after it; a read the
/// system fails with error 9 is the end of the input, as Rust's <c>handle_ebadf</c> reads it, and another failed read an
/// <c>IoError</c> naming <c>&lt;stdin&gt;</c>, after which the bytes a <c>ReadAll</c> took are the next read's; a closed descriptor 0,
/// 1 or 2 is <c>/dev/null</c> from the start, so that no file the program opens takes its number, and a <c>print</c> or an
/// <c>eprint</c> the system fails with error 9 counts as written; and a build for Windows says <c>not supported</c> at each of the
/// four. Each program runs under the runtime of this machine, through <c>/bin/sh</c>, which gives it its input; on
/// Windows the tests hold the build's refusal instead.
/// </summary>
public sealed class StdinTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and started by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="input"/>, a shell
    /// command whose output is piped into it, or with the redirections <paramref name="redirect"/> given; its exit code, its lines and
    /// its error stream. Null on Windows, where the build is held to its refusal: <c>not supported</c>, on lines that call on
    /// <c>Stdin</c> or <c>File</c> and at none other (S11, S22), by the id and the line, never the message (section 8).
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string input, string redirect = "")
    {
        if (Builder.HostTarget == Target.Windows)
        {
            var refused = Compile(text, toolchain.Runtime);
            Assert.Null(refused.Program);
            var lines = text.Split('\n').Select((line, i) => (line, i + 1)).Where(l => l.line.Contains("Stdin.", StringComparison.Ordinal) || l.line.Contains("File.", StringComparison.Ordinal)).Select(l => l.Item2).ToHashSet();
            Assert.NotEmpty(refused.Diagnostics.Items);
            Assert.All(refused.Diagnostics.Items, d => Assert.True(d.Kind == DiagnosticKind.NotSupported && lines.Contains(d.Line), $"{d.Id} at line {d.Line}"));
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
    public void A_read_of_stdin_appends_to_its_list_as_a_files_read_does()
    {
        // S22 with S3: `Read` appends to the bytes `into` holds; a `max` of 0 takes nothing, before a fill and with bytes in the buffer,
        // and keeps them for the next read; a `max` below 0 raises `index-out-of-range` at its line
        const string text = """
            void | IoError | InvalidUtf8 Run() {
                mut into = "ab".Bytes
                print(Stdin.Read(mut into, 0))
                print(Stdin.Read(mut into, 3))
                print(string.FromUtf8(into))
                print(Stdin.Read(mut into, 0))
                print(Stdin.ReadAll().Count)
                int max = -1
                Stdin.Read(mut into, max)
            }

            Run() else {
                IoError e => print(e)
                InvalidUtf8 e => print(e)
            }
            """;
        if (Ran(text, "stdin-read", "printf 'xyz\\nw'") is { } run)
        {
            Assert.Equal(["0", "3", "abxyz", "0", "2"], run.Output);
            Assert.Equal(1, run.Code);
            Assert.StartsWith("error: index-out-of-range at program.kz:9", run.Error, StringComparison.Ordinal);
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
                        IoError e => {
                            print(e)
                            more = false
                        }
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
    public void A_closed_standard_input_is_the_end_of_the_input_and_another_failed_read_names_stdin()
    {
        // S22: a read of descriptor 0 open for writing alone gives the system's error 9, which is the end of the input, as Rust's
        // `handle_ebadf` reads it (the owner, 2026-11-11, "End of input" and "End of input, as Rust"): the program prints what it
        // prints with an empty input. A descriptor 0 the caller closed is `/dev/null` from the start, which reads as empty too. The
        // control is a read that fails otherwise, of a directory, 21, whose `IoError` names the path `<stdin>` at each of the three
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
            Assert.Equal(["end", "0", "0", "no line"], closed.Output);
        }

        if (Ran(text, "stdin-write-only", "", "0>/dev/null") is { } writeOnly)
        {
            Assert.True((0, "") == (writeOnly.Code, writeOnly.Error), $"exit {writeOnly.Code}: {writeOnly.Error}");
            Assert.Equal(["end", "0", "0", "no line"], writeOnly.Output);
        }

        if (Ran(text, "stdin-directory", "", "< /") is { } directory)
        {
            Assert.True((0, "") == (directory.Code, directory.Error), $"exit {directory.Code}: {directory.Error}");
            Assert.Equal(["is a directory: <stdin>", "is a directory: <stdin>", "is a directory: <stdin>"], directory.Output);
        }

        if (Ran(text, "stdin-closed-control", "") is { } empty)
        {
            Assert.True((0, "") == (empty.Code, empty.Error), $"exit {empty.Code}: {empty.Error}");
            Assert.Equal(["end", "0", "0", "no line"], empty.Output);
        }
    }

    [Theory]
    [InlineData("<&-", "standard-0", 0)]
    [InlineData(">&-", "standard-1", 1)]
    [InlineData("2>&-", "standard-2", 2)]
    public void A_closed_standard_descriptor_is_dev_null_from_the_start_so_no_file_takes_its_number(string redirect, string name, int fd)
    {
        // S22, A1, A17: a descriptor 0, 1 or 2 the caller closed is opened again on `/dev/null` before the first statement (section 12,
        // the owner, 2026-11-11, "0, 1, 2 as Rust"), so the file the program opens next takes another number: a read of `Stdin` is the
        // end, and the line of a `print` or an `eprint` goes to `/dev/null`, not into the file, which stays empty. Without the step
        // the file took the closed number, and the read gave the file's line (issue 174, comment 6792523201) or the line went into the
        // file. The control is each program with its descriptors open
        var text = fd switch
        {
            0 => $$"""
                void | IoError | InvalidUtf8 Run() {
                    File.WriteAll("{{name}}.txt", "a line of the file".Bytes)
                    f = File.Open("{{name}}.txt")
                    match Stdin.ReadLine() {
                        string l => print("read {l}")
                        null => print("end")
                        IoError e => print(e)
                        InvalidUtf8 e => print(e)
                    }

                    f.Close()
                    File.Delete("{{name}}.txt")
                }

                Run() else {
                    IoError e => print(e)
                    InvalidUtf8 e => print(e)
                }
                """,
            _ => $$"""
                void | IoError Run() {
                    f = File.Create("{{name}}.txt")
                    {{(fd == 1 ? "print" : "eprint")}}("a line")
                    f.Close()
                    {{(fd == 1 ? "eprint" : "print")}}(File.ReadAll("{{name}}.txt").Count)
                    File.Delete("{{name}}.txt")
                }

                Run() else {
                    IoError e => {{(fd == 1 ? "eprint" : "print")}}(e)
                }
                """,
        };
        if (Ran(text, name, "", redirect) is { } closed)
        {
            switch (fd)
            {
                case 0:
                    Assert.True((0, "") == (closed.Code, closed.Error), $"exit {closed.Code}: {closed.Error}");
                    Assert.Equal(["end"], closed.Output);
                    break;
                case 1:
                    Assert.True((0, "0\n") == (closed.Code, closed.Error), $"exit {closed.Code}: {closed.Error}");
                    Assert.Empty(closed.Output);
                    break;
                default:
                    Assert.True((0, "") == (closed.Code, closed.Error), $"exit {closed.Code}: {closed.Error}");
                    Assert.Equal(["0"], closed.Output);
                    break;
            }
        }

        if (Ran(text, name + "-control", "") is { } open)
        {
            Assert.Equal(0, open.Code);
            Assert.Equal(fd switch { 0 => ["end"], 1 => ["a line"], _ => ["0"] }, open.Output);
            Assert.Equal(fd switch { 0 => "", 1 => "0\n", _ => "a line\n" }, open.Error);
        }
    }

    [Theory]
    [InlineData("1</dev/null", "print", "eprint")]
    [InlineData("2</dev/null", "eprint", "print")]
    public void A_print_or_eprint_the_system_fails_with_error_9_counts_as_written(string redirect, string refused, string after)
    {
        // A1, A17: a write to a descriptor open for reading alone gives the system's error 9, which counts as written whole, as Rust's
        // `handle_ebadf` reads it for its `Stdout` and `Stderr` (section 12, the owner, 2026-11-11, "Success, as Rust"): the program
        // goes on and ends with 0, where `print-failed` ended it with 1. A closed pipe still raises `print-failed` (EmitterTests)
        var text = $"{refused}(\"into nothing\")\n{after}(\"after\")\n";
        if (Builder.HostTarget == Target.Windows)
        {
            // the owner's answer names Linux: on Windows the program builds, and a refused write stays `print-failed`
            Assert.NotNull(Compile(text, toolchain.Runtime).Program);
            return;
        }

        if (Ran(text, refused + "-ebadf", "", redirect) is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(after == "print" ? ["after"] : [], run.Output);
            Assert.Equal(after == "eprint" ? "after\n" : "", run.Error);
        }
    }

    [Fact]
    public void A_ReadAll_that_fails_part_way_keeps_the_bytes_it_took_for_the_next_read()
    {
        // S22: standard input a stream whose other end sends a line and more, then resets it: `ReadLine` takes the line and leaves the
        // rest in the reader's buffer, `ReadAll` takes that rest, and its read fails on the reset; the bytes it took go back to the buffer,
        // so that the next `ReadLine` gives them as the last line, and the one after it the end (the owner, 2026-11-11, "Keep for next
        // read"). The control ends the stream with no reset, where `ReadAll` gives the rest and each `ReadLine` after it the end
        const string text = """
            void Line() {
                match Stdin.ReadLine() {
                    string l => print(l)
                    null => print("end")
                    IoError e => print(e)
                    InvalidUtf8 e => print(e)
                }
            }

            Line()
            match Stdin.ReadAll() {
                Bytes b => print(b.Count)
                IoError e => print(e)
            }

            Line()
            Line()
            """;
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Ran(text, "stdin-kept", ""));
            return;
        }

        var reset = OverStream(text, "stdin-kept", reset: true);
        Assert.True((0, "") == (reset.Code, reset.Error), $"exit {reset.Code}: {reset.Error}");
        Assert.Equal(["first", "connection reset: <stdin>", "rest of it", "end"], reset.Output);

        var ended = OverStream(text, "stdin-ended", reset: false);
        Assert.True((0, "") == (ended.Code, ended.Error), $"exit {ended.Code}: {ended.Error}");
        Assert.Equal(["first", "10", "end", "end"], ended.Output);
    }

    /// <summary>
    /// The program built for this machine and started with a stream as its standard input: <c>python3</c>, the one of the image the
    /// <c>gates</c> job runs in, which no file pins (HAZARD #15), connects to a listener of the test, puts the connection on descriptor 0
    /// and runs the program in its place. The listener sends <c>first\nrest of it</c>, waits
    /// half a second, so that the program's <c>ReadAll</c> waits on its read, and then resets the connection (<paramref name="reset"/>,
    /// a close that lingers 0 seconds) or closes it.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error) OverStream(string text, string name, bool reset)
    {
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var other = Task.Run(() =>
        {
            using var stream = listener.AcceptSocket();
            stream.Send("first\nrest of it"u8);
            Thread.Sleep(500);
            if (reset)
            {
                stream.LingerState = new LingerOption(true, 0);
            }

            stream.Close();
        });
        const string wrapper = "import os, socket, sys; s = socket.create_connection(('127.0.0.1', int(sys.argv[2]))); os.dup2(s.fileno(), 0); os.execv(sys.argv[1], [sys.argv[1]])";
        var (code, output, error) = Processes.Capture("python3", ["-c", wrapper, executable, port.ToString(System.Globalization.CultureInfo.InvariantCulture)], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" });
        Assert.True(other.Wait(TimeSpan.FromSeconds(30)), "the listener did not end");
        return (code, Toolchain.Lines(output), error);
    }

    [Fact]
    public void A_captured_program_runs_with_nothing_on_its_standard_input()
    {
        // chapter 0 with S22: `Processes.Capture`, which runs every case, gives the program an empty pipe whose writing end is closed, not the caller's
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
        Assert.Equal((DiagnosticKind.NotSupported, line), (diagnostic.Kind, diagnostic.Line)); // section 8: the id and the line of an error, never its message
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }

    [Theory]
    [InlineData("x = Stdin.Lines()\nprint(1)\n", "syntax", 1)]
    [InlineData("x = Stdin.Peek()\nprint(1)\n", "unknown-name", 1)]
    [InlineData("x = Stdin\nprint(1)\n", "unknown-name", 1)]
    [InlineData("void Stdin() {\n}\n\nprint(1)\n", "redeclared", 1)]
    [InlineData("void | IoError Some() {\n    mut into = Bytes()\n    print(Stdin.Read(into, 4))\n}\n\nSome() else {\n    IoError e => print(e)\n}\n", "mut-at-call", 3)] // M7, as of `f.Read`; the same program with `mut` is the Windows theory's row, built clean here
    public void A_use_of_stdin_outside_its_members_is_refused_where_it_stands(string text, string id, int line)
    {
        // S22, S16, V9, V10: `Lines()` stands as a loop's collection alone, `Stdin` has its four members and is no value, and a file's
        // own declaration of the name is `redeclared`; M7: the bytes `Read` appends to are a `mut` argument, written so at the call
        var refused = Compile(text, toolchain.Runtime);
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((id, line), (diagnostic.Id, diagnostic.Line)); // section 8: the id and the line of an error, never its message
    }
}
