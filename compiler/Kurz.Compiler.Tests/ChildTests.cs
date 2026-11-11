using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on running a program (S17, S18, S21; design record, section 12) that no corpus case pins, since a case runs with no
/// file of its own, no variable of its own and no descriptor but its three streams: a chain and <c>cmd = cmd.Arg(x)</c> build one
/// command with no copy, and a command another value holds is copied; <c>Output()</c> reads both pipes as they fill;
/// a child gets the program's environment less and with what its members took away and set, in the order of their calls; a program
/// named without a <c>/</c> is looked up in the <c>PATH</c> the child gets, a directory that refuses it passed over, and in the
/// default path where the child gets none; a start that fails names the program, or the directory <c>Dir</c> named where that is
/// what failed; a child gets no descriptor but its three streams, and its standard input where the program started with its own
/// closed; <c>Status()</c> gives it the program's streams, and a stream set
/// to <c>Stdio.Piped</c> that the call does not read is closed at once; and a build for Windows says <c>not supported</c> at each
/// call. Each program runs under the runtime of this machine, through <c>/bin/sh</c>, whose command <c>setup</c> makes the files a
/// program finds, so that no file the test host wrote is started (<c>knowledge/a-file-this-process-wrote-does-not-start-beside-a-fork.md</c>);
/// on Windows the tests hold the build's refusal instead.
/// </summary>
public sealed class ChildTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and started by <c>/bin/sh</c> as <c>exec "$0"</c> after the shell command
    /// <paramref name="setup"/>, in the directory of its build, with <c>KURZ_TRACE=0</c> and the variables of
    /// <paramref name="variables"/>; its exit code, its lines and its error stream. Null on Windows, where the build is held to its refusal.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup = "", IReadOnlyDictionary<string, string?>? variables = null)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Compile(text, toolchain.Runtime).Program);
            return null;
        }

        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var environment = new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" };
        foreach (var (key, value) in variables ?? new Dictionary<string, string?>())
        {
            environment[key] = value;
        }

        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup}exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), environment);
        return (code, Toolchain.Lines(output), error);
    }

    /// <summary>A program that runs <paramref name="body"/> in a function whose cases it prints.</summary>
    private static string Program(string body) => $$"""
        void | IoError | ExitError | InvalidUtf8 Run() {
        {{body}}
        }

        Run() else {
            IoError e => print("{e.Kind} [{e.Path}]")
            ExitError e => print(e)
            InvalidUtf8 e => print(e)
        }

        """;

    /// <summary>The runtime of this machine whose copy of a list's block for a write (M3) fails, so that a program that copies one ends with <c>allocation-failed</c>.</summary>
    private static CheckedRuntime NoCopy() => ListTests.Edited(("std/std.kz", "pub byte* ListCopy(byte* block, long item, mut long asked) {\n", "pub byte* ListCopy(byte* block, long item, mut long asked) {\n    asked = 0\n    return null\n}\n\nbyte* CopiedList(byte* block, long item, mut long asked) {\n"));

    [Fact]
    public void A_chain_and_an_assignment_of_a_member_to_its_variable_build_one_command_in_place()
    {
        // S17, section 12, the owner, 2026-11-01, P1: each step of a chain, whose command no other value holds, and `cmd = cmd.Arg(x)`,
        // which keeps no earlier command, write into the one block, so that a runtime whose every copy of a list fails runs them, and
        // an argument of such an assignment reads the command as it was before the assignment; a command another value holds is
        // copied, which that runtime ends with `allocation-failed`, the positive control
        var text = Program("""
                out = Command.New("printf").Arg("%s|").Arg("a").Args(["b", "c"]).Env("KURZ_CASE_K", "v").Stdin(Stdio.Null).Output()
                print(string.FromUtf8(out.Stdout))
                mut d = Command.New("printf").Arg("%s,")
                for w in ["p", "q"] {
                    d = d.Arg(w).Arg("{w}{w}")
                }

                print(string.FromUtf8(d.Output().Stdout))
                mut e = Command.New("printf").Arg("%s;")
                e = e.Arg("1").Arg(Shown(e))
                print(string.FromUtf8(e.Output().Stdout))
            """) + "string | IoError | ExitError | InvalidUtf8 Shown(Command c) {\n    return string.FromUtf8(c.Output().Stdout)\n}\n";
        const string shared = "base = Command.New(\"printf\").Arg(\"%s\")\nmore = base.Arg(\"x\")\nmore.Status() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n";
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Compile(text, toolchain.Runtime).Program);
            return;
        }

        var runtime = NoCopy();
        var inPlace = Compile(text, runtime);
        Assert.True(inPlace.Program is not null, string.Join("; ", inPlace.Diagnostics.Sorted));
        var ran = Toolchain.Run(toolchain.Build(inPlace.Program, "child-in-place"));
        Assert.Equal((0, ""), (ran.Code, ran.Error));
        Assert.Equal(["a|b|c|", "p,pp,q,qq,", "1;;;"], ran.Output); // `Shown(e)` read the command before the chain appended `1`
        var copied = Compile(shared, runtime);
        Assert.True(copied.Program is not null, string.Join("; ", copied.Diagnostics.Sorted));
        var refused = Toolchain.Run(toolchain.Build(copied.Program, "child-copied"));
        Assert.Equal(1, refused.Code);
        Assert.StartsWith("error: allocation-failed", refused.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_reads_both_pipes_as_they_fill_so_a_child_that_fills_one_does_not_wait()
    {
        // S18, S21: more than a pipe holds to standard error first, then to standard output; a call that read standard output to its
        // end before standard error would wait on the child, and the child on it
        var text = Program("""
                big = Command.New("sh").Args(["-c", "head -c 200000 /dev/zero >&2; head -c 300000 /dev/zero"]).Output()
                print("{big.Stdout.Count} {big.Stderr.Count}")
            """);
        if (Ran(text, "child-both-pipes") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["300000 200000"], ran.Output);
        }
    }

    [Fact]
    public void A_child_gets_the_programs_environment_less_and_with_what_its_members_took_away_and_set()
    {
        // S21: inherited, `Env` replacing a variable once, `EnvRemove` taking one away, `EnvClear` dropping the program's and what an
        // earlier `Env` set, the later member winning
        var text = Program("""
                for c in [Command.New("env"), Command.New("env").Env("KURZ_CASE_X", "8"), Command.New("env").EnvRemove("KURZ_CASE_X"), Command.New("env").Env("KURZ_CASE_A", "1").EnvClear(), Command.New("env").EnvClear().Env("A", "1").Env("B", "2").EnvRemove("A"), Command.New("env").EnvRemove("B").Env("B", "3").EnvClear().Env("C", "4")] {
                    lines = string.FromUtf8(c.Output().Stdout).Split("\n")
                    mut seen = List<string>()
                    for line in lines {
                        if line.StartsWith("KURZ_CASE_") || line.StartsWith("A=") || line.StartsWith("B=") || line.StartsWith("C=") {
                            seen.Add(line)
                        }
                    }

                    count = lines.Count - 1
                    print("{count} {seen}")
                }
            """);
        if (Ran(text, "child-environment", variables: new Dictionary<string, string?> { ["KURZ_CASE_X"] = "7" }) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(6, ran.Output.Count);
            Assert.Matches("^[0-9]+ \\[KURZ_CASE_X=7\\]$", ran.Output[0]);
            Assert.Matches("^[0-9]+ \\[KURZ_CASE_X=8\\]$", ran.Output[1]);
            Assert.Matches("^[0-9]+ \\[\\]$", ran.Output[2]);
            Assert.Equal(["0 []", "1 [B=2]", "1 [C=4]"], ran.Output.Skip(3));
        }
    }

    [Fact]
    public void A_program_named_without_a_slash_is_looked_up_in_the_path_the_child_gets()
    {
        // S21: in the `PATH` the child gets, a directory whose file is no program passed over and named where no later one has it; in
        // the default path, `/bin:/usr/bin`, where the child gets none; an empty directory of the path is the working directory; and a
        // program named with a `/` that is not from the root is found from the directory `Dir` names
        const string setup = "mkdir -p refused found && printf 'not run' > refused/kurz-case-echo && cp /bin/echo found/kurz-case-echo && ";
        var text = Program("""
                for c in [Command.New("kurz-case-echo").Arg("later").Env("PATH", "refused:found"), Command.New("kurz-case-echo").Arg("here").Env("PATH", "/kurz-case-nowhere:").Dir("found"), Command.New("./kurz-case-echo").Arg("slash").Dir("found")] {
                    print(string.FromUtf8(c.Output().Stdout).Split("\n")[0])
                }

                mut path = false
                for line in string.FromUtf8(Command.New("env").EnvRemove("PATH").Output().Stdout).Split("\n") {
                    path = path || line.StartsWith("PATH=")
                }

                print("env {path}")

                print(Command.New("kurz-case-echo").Env("PATH", "refused").Output().Stdout.Count)
            """);
        if (Ran(text, "child-path", setup) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["later", "here", "slash", "env false", "PermissionDenied [kurz-case-echo]"], ran.Output); // `env` found in the default path
        }
    }

    [Fact]
    public void A_start_that_fails_names_the_program_or_the_directory_dir_named()
    {
        // S21: the path of a failed start is the program, or the directory where that is what failed, never an argument; an empty
        // program is `NotFound` with the empty path
        const string body = """
                for c in [Command.New("kurz-case-no-such").Arg("a"), Command.New("true").Dir("kurz-case-no-dir").Arg("a"), Command.New("")] {
                    c.Status() else {
                        IoError e => print("{e.Kind} [{e.Path}]")
                        ExitError e => print(e)
                    }
                }
            """;
        if (Ran(Program(body), "child-start-failed") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["NotFound [kurz-case-no-such]", "NotFound [kurz-case-no-dir]", "NotFound []"], ran.Output);
        }
    }

    [Fact]
    public void A_child_gets_no_descriptor_but_its_three_streams()
    {
        // S21: the program gets descriptor 7 from its shell with no close-on-exec mark, and the child it runs has 0, 1 and 2 alone
        var text = Program("""
                print(string.FromUtf8(Command.New("sh").Args(["-c", "ls /proc/$$/fd"]).Output().Stdout).Split("\n"))
            """);
        if (Ran(text, "child-descriptors", "exec 7</dev/null && ") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["[0, 1, 2, ]"], ran.Output);
        }
    }

    [Fact]
    public void Status_gives_the_child_the_programs_streams_and_output_gives_empty_bytes_for_one_it_does_not_pipe()
    {
        // S18, S21: `Status()` gives the child the program's three streams; `Output()` gives empty `Bytes` for a stream set to
        // `Stdio.Inherit` or `Stdio.Null`, and closes the program's end of a piped standard input at once, so that `cat` reads its end
        var text = Program("""
                Command.New("sh").Args(["-c", "echo out; echo err >&2"]).Status()
                inherited = Command.New("echo").Arg("through").Stdout(Stdio.Inherit).Output()
                print(inherited.Stdout.Count)
                silent = Command.New("sh").Args(["-c", "echo gone; echo gone >&2"]).Stdout(Stdio.Null).Stderr(Stdio.Null).Output()
                print("{silent.Stdout.Count} {silent.Stderr.Count}")
                ended = Command.New("cat").Stdin(Stdio.Piped).Output()
                print("{ended.Status.Success} {ended.Stdout.Count}")
            """);
        if (Ran(text, "child-streams") is { } ran)
        {
            Assert.Equal((0, "err\n"), (ran.Code, ran.Error));
            Assert.Equal(["out", "through", "0", "0 0", "true 0"], ran.Output);
        }
    }

    [Fact]
    public void A_child_gets_its_streams_where_the_program_started_with_standard_input_closed()
    {
        // S21: the system gives the program descriptor 0 for `/dev/null` or a pipe it opens, which the runtime moves above 2, so that
        // the child's `dup2` onto its standard input takes away the close-on-exec mark and `cat` reads its input to the end
        var text = Program("""
                a = Command.New("cat").Output()
                b = Command.New("cat").Stdin(Stdio.Piped).Output()
                print("{a.Status.Success} {b.Status.Success}")
            """);
        if (Ran(text, "child-closed-input", "exec 0<&- && ") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["true true"], ran.Output);
        }
    }

    [Theory]
    [InlineData("print(1)\nCommand.New(\"true\").Arg(\"x\").Status() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("void | IoError | ExitError Run() {\n    Command.New(\"true\").Status()\n}\n\nRun() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("void | IoError | ExitError Run() {\n    print(1)\n    out = Command.New(\"true\").Output()\n    print(out.Stdout.Count)\n}\n\nRun() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 3)]
    public void A_build_for_windows_says_not_supported_at_the_first_call_on_a_command(string text, int line)
    {
        // S17 to S21 with S11: as `Stdin` is, each call on `Command` and on a command
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((line, Emitter.CommandNotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }

    [Theory]
    [InlineData("c = Command.New(\"a\")\nprint(c)\n", "no-text", 2)] // A4, as of a `File`
    [InlineData("c = Command.New(\"a\")\nprint(\"{c}\")\n", "no-text", 2)]
    [InlineData("c = Command.New(\"a\")\nprint(c == c)\n", "type-mismatch", 2)] // T18, as of a `File`
    [InlineData("print(1)\nc = Command.New(\"a\").Kill()\n", "unknown-name", 2)] // V10
    [InlineData("print(1)\nc = Command.Run(\"a\")\n", "unknown-name", 2)]
    [InlineData("print(1)\nCommand<int> c = Command.New(\"a\")\nprint(1)\n", "syntax", 2)] // T17
    [InlineData("print(1)\nc = Command.New(1)\n", "type-mismatch", 2)] // T1
    [InlineData("print(1)\nc = Command.New(\"a\").Stdout(1)\n", "type-mismatch", 2)]
    [InlineData("print(1)\nc = Command.New(\"a\").Spawn()\n", "", 2)] // S19, `not supported`
    [InlineData("print(1)\nc = Command.New(\"a\").Input([1])\n", "", 2)] // S19
    [InlineData("print(1)\nc = Command.New(\"a\").Timeout(1)\n", "", 2)] // S20
    [InlineData("print(1)\nChildProcess c = null\n", "", 2)] // S19
    public void A_use_of_a_command_outside_its_members_is_refused_where_it_stands(string text, string id, int line)
    {
        // S17: a command has the members of S17 and S18 and no text and no `==`; S19 and S20 stay `not supported` (an empty id)
        var refused = Compile(text, toolchain.Runtime);
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((id, line, id.Length == 0), (diagnostic.Id, diagnostic.Line, diagnostic.IsNotSupported)); // section 8: the id and the line of an error, never its message
    }
}
