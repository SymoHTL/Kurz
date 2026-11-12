using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on running a program (S17 to S21; design record, section 12) that no corpus case pins, since a case runs with no
/// file of its own, no variable of its own and no descriptor but its three streams: a chain and <c>cmd = cmd.Arg(x)</c> build one
/// command with no copy, and a command another value holds is copied; <c>Output()</c> reads both pipes as they fill;
/// a child gets the program's environment less and with what its members took away and set, in the order of their calls; a program
/// named without a <c>/</c> is looked up in the <c>PATH</c> the child gets, a directory that refuses it passed over, also where
/// the program's native code cleared its own environment, and in the default path where the child gets none; a start that fails
/// names the program, or the directory <c>Dir</c> named where that is
/// what failed, a second <c>Dir</c> replacing the first; a NUL byte in the program, an argument, any directory <c>Dir</c> was given
/// or the environment the child gets is <c>InvalidInput</c>; a read of the output that fails is the call's <c>IoError</c>, named by
/// the program, once both pipes are closed and the child waited for; a child gets no descriptor but its three streams, where the system refuses <c>close_range</c> too, and its standard
/// input where the program's own native code closed its own; <c>Status()</c> gives it the program's streams, and a stream set
/// to <c>Stdio.Piped</c> that the call does not read is closed at once; a bound that passes kills a child still running and gives
/// <c>TimedOut</c>, at the start where it is 0 or below, and a child that ends before its bound gives its own status, at the bound
/// where a process it left behind holds a pipe; <c>Input(bytes)</c> gives the child every byte and then the end of its input, a child
/// that ends before it read it leaves the rest unwritten, one that does not read it holds the call to its bound, and <c>Input</c>
/// beside a member <c>Stdin</c> is <c>InvalidInput</c>; and a build for Windows says <c>not supported</c> at the first call on a
/// command. Each program that <see cref="Ran"/> starts runs under the runtime it is given, this machine's unless a
/// test edits it, through <c>/bin/sh</c>, whose command <c>setup</c> makes the files a
/// program finds, so that no file the test host wrote is started (<c>knowledge/a-file-this-process-wrote-does-not-start-beside-a-fork.md</c>);
/// on Windows the tests hold the build's refusal instead.
/// </summary>
public sealed class ChildTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime, params string[] grants) => Builder.Compile(SourceText.FromString("program.kz", text), grants, runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and started by <c>/bin/sh</c> as <c>exec "$0"</c> after the shell command
    /// <paramref name="setup"/>, in the directory of its build, with <c>KURZ_TRACE=0</c> and the variables of
    /// <paramref name="variables"/>, built with the <paramref name="grants"/> under <paramref name="runtime"/>, or this machine's
    /// runtime; its exit code, its lines and its error stream. Null on Windows, where the build is held to its refusal.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup = "", IReadOnlyDictionary<string, string?>? variables = null, CheckedRuntime? runtime = null, string[]? grants = null)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Compile(text, toolchain.Runtime, grants ?? []).Program);
            return null;
        }

        var compilation = Compile(text, runtime ?? toolchain.Runtime, grants ?? []);
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
        // which keeps no earlier command, `Input(bytes)` and `Timeout(d)` among its members (S19, S20), write into the one block, so that a runtime whose every
        // copy of a list fails runs them, and
        // an argument of such an assignment reads the command as it was before the assignment; a command another value holds is
        // copied, which that runtime ends with `allocation-failed`, the positive control
        var text = Program("""
                out = Command.New("printf").Arg("%s|").Arg("a").Args(["b", "c"]).Env("KURZ_CASE_K", "v").Stdin(Stdio.Null).Output()
                print(string.FromUtf8(out.Stdout))
                mut d = Command.New("printf").Arg("%s,")
                for w in ["p", "q"] {
                    d = d.Arg(w).Timeout(10s).Input("{w}".Bytes).Arg("{w}{w}")
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
    public void A_program_is_looked_up_in_the_path_the_child_gets_where_the_programs_native_code_cleared_its_environment()
    {
        // S21: the path the child gets is the `PATH` of the environment the program started with, of which the child's environment
        // is made, so that native code that clears the program's environment (`clearenv`) changes neither: `kurz-case-echo`, found
        // only in the directory the program's `PATH` starts with, is found; a lookup in the environment the native code changed
        // finds no `PATH` there, looks in the default path and gives `NotFound`
        const string setup = "mkdir -p first && cp /bin/echo first/kurz-case-echo && PATH=\"$PWD/first:$PATH\" && export PATH && ";
        var text = "extern \"C\" {\n    int clearenv()\n}\n\nraw {\n    clearenv()\n}\n\n" + Program("""
                print(string.FromUtf8(Command.New("kurz-case-echo").Arg("found").Output().Stdout).Split("\n")[0])
            """);
        if (Ran(text, "child-path-cleared", setup, grants: ["raw", "native"]) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["found"], ran.Output);
        }
    }

    [Fact]
    public void A_start_that_fails_names_the_program_or_the_directory_dir_named()
    {
        // S21: the path of a failed start is the program, or the directory where that is what failed, never an argument; an empty
        // program is `NotFound` with the empty path; a second `Dir` replaces the first, each way round
        const string body = """
                for c in [Command.New("kurz-case-no-such").Arg("a"), Command.New("true").Dir("kurz-case-no-dir").Arg("a"), Command.New(""), Command.New("true").Dir("kurz-case-no-dir").Dir("."), Command.New("true").Dir(".").Dir("kurz-case-no-dir")] {
                    c.Status() else {
                        IoError e => print("{e.Kind} [{e.Path}]")
                        ExitError e => print(e)
                    }

                    print("called")
                }
            """;
        if (Ran(Program(body), "child-start-failed") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["NotFound [kurz-case-no-such]", "called", "NotFound [kurz-case-no-dir]", "called", "NotFound []", "called", "called", "NotFound [kurz-case-no-dir]", "called"], ran.Output);
        }
    }

    [Fact]
    public void A_nul_byte_in_a_string_the_child_gets_or_in_any_directory_is_invalid_input_with_the_number_0()
    {
        // S21 (the owner, 2026-11-11, "Rust's split", issue 174, comment 6802725183): in the program, an argument, a directory `Dir`
        // was given, a replaced one among them, and a name or a value of the environment the child gets, each refused before any call
        // of the system, so that no directory is entered and no program is looked up; a variable taken away or cleared is none the
        // child gets, so its NUL starts the child, as Rust checks the environment it captured; the positive control runs the same
        // command clean
        const string body = """
                for c in [Command.New("tr\0ue"), Command.New("true").Arg("a\0"), Command.New("true").Dir("kurz-case-no-dir\0"), Command.New("true").Dir("kurz-case-no-dir\0").Dir("."), Command.New("true").Env("KURZ\0CASE", "1"), Command.New("true").Env("KURZ_CASE", "1\0"), Command.New("true").EnvRemove("KURZ\0CASE"), Command.New("true").Env("KURZ_CASE", "1\0").EnvRemove("KURZ_CASE"), Command.New("true").Env("KURZ_CASE", "1\0").EnvClear(), Command.New("true").Env("KURZ_CASE", "1")] {
                    c.Status() else {
                        IoError e => print("{e.Kind} {e.Code}")
                        ExitError e => print(e)
                    }

                    print("called")
                }
            """;
        if (Ran(Program(body), "child-nul") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["InvalidInput 0", "called", "InvalidInput 0", "called", "InvalidInput 0", "called", "InvalidInput 0", "called", "InvalidInput 0", "called", "InvalidInput 0", "called", "called", "called", "called", "called"], ran.Output);
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
    public void A_child_gets_no_descriptor_but_its_three_streams_where_the_system_refuses_close_range()
    {
        // S21: a kernel before 5.11, or a filter, refuses `close_range`, here a runtime whose call is one the system does not have
        // (`ENOSYS`); the child marks each descriptor up to the soft limit of open files instead; the positive control, a runtime that
        // marks none, gives the child the program's descriptor 7
        const string call = "if syscall(436, long(3), long(4294967295), long(4)) < 0 {";
        var text = Program("""
                print(string.FromUtf8(Command.New("sh").Args(["-c", "ls /proc/$$/fd"]).Output().Stdout).Split("\n"))
            """);
        if (Ran(text, "child-descriptors-refused", "exec 7</dev/null && ", runtime: ListTests.Edited(("runtime/linux/linux.kz", call, "if syscall(long(-1)) < 0 {"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["[0, 1, 2, ]"], ran.Output);
            var leaked = Ran(text, "child-descriptors-leaked", "exec 7</dev/null && ", runtime: ListTests.Edited(("runtime/linux/linux.kz", call, "if syscall(long(-1)) < -1 {")))!.Value;
            Assert.Equal((0, ""), (leaked.Code, leaked.Error));
            // the control holds descriptor 7 alone: under a runtime that marks none, the child also holds any other descriptor
            // the program had open without its close-on-exec mark, one more in three runs of 2026-11-12 (183, 196 and 191),
            // whose source was not found
            Assert.Contains("7", Assert.Single(leaked.Output).Trim('[', ']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
    }

    [Fact]
    public void A_read_of_the_output_that_fails_is_the_calls_io_error_named_by_the_program()
    {
        // S18 (the owner, 2026-11-11, "The program" and "Close pipes, wait", issue 174, comment 6802725183): a runtime whose read of
        // a pipe fails (`EBADF`) gives the `IoError` of the call, with the program as its path, where a read that failed was the
        // pipe's end before; the call closes both pipes and waits for the child, so that the next call runs; the positive control
        // runs the same program under this machine's runtime
        var text = Program("""
                match Command.New("printf").Arg("x").Output() {
                    ProcessOutput o => print(o.Stdout.Count)
                    IoError e => print("{e.Kind} [{e.Path}]")
                    ExitError e => print(e)
                }

                print(string.FromUtf8(Command.New("printf").Arg("y").Output().Stdout))
            """);
        if (Ran(text, "child-read-fails", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [printf]", "Other [printf]"], ran.Output);
            var control = Ran(text, "child-read-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["1", "y"], control.Output);
        }
    }

    [Fact]
    public void A_read_of_the_output_that_fails_closes_both_pipes_and_waits_for_the_childs_end()
    {
        // S18 (the owner, 2026-11-11, "Close pipes, wait", issue 174, comment 6802725183): after a read that fails the call closes
        // both pipes and waits for the child's end before it gives the `IoError`; the child marks `kurz-reached` a second after the
        // byte the call failed to read and ends at its next write (`SIGPIPE`), before it marks `kurz-wrote`, so that a call that did
        // not wait finds no `kurz-reached` and one that left the pipes open finds `kurz-wrote`; the positive control, under this
        // machine's runtime, reads both bytes and finds both marks
        var text = Program("""
                match Command.New("sh").Args(["-c", "rm -f kurz-reached kurz-wrote; printf a; sleep 1; : > kurz-reached; printf b; : > kurz-wrote"]).Output() {
                    ProcessOutput o => print(o.Stdout.Count)
                    IoError e => print("{e.Kind} [{e.Path}]")
                    ExitError e => print(e)
                }

                Command.New("sh").Args(["-c", "test -e kurz-reached && ! test -e kurz-wrote"]).Status() else {
                    IoError e => print(e)
                    ExitError e => print(e)
                }

                print("checked")
            """);
        if (Ran(text, "child-read-fails-waits", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sh]", "checked"], ran.Output); // `Status()` reads no pipe, so the edited read does not reach it
            var control = Ran(text, "child-read-waits-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["2", "exit status 1: sh", "checked"], control.Output);
        }
    }

    [Fact]
    public void A_read_that_fails_under_a_bound_stays_the_calls_error_and_the_wait_after_it_ends_at_the_bound()
    {
        // S18, S20 (assumed, §12, with the build of the bound): the wait after a read that fails is held to the bound, so that a
        // child that neither writes nor ends is killed when it passes, and the read's `IoError` stays the call's error then, as it
        // does where the child ends with 3 before its bound; a wait that is not held to the bound waits on `sleep` past the run's
        // limit, and a kill that takes the call's error prints `TimedOut [sh]`; the positive control, under this machine's runtime,
        // reads the byte and gives `TimedOut` at the bound, and the child's own status where it ends before it
        var text = Program("""
                for c in [Command.New("sh").Args(["-c", "printf a; exec sleep 60"]).Timeout(500ms), Command.New("sh").Args(["-c", "printf a; sleep 0.2; exit 3"]).Timeout(10s)] {
                    match c.Output() {
                        ProcessOutput o => print(o.Stdout.Count)
                        IoError e => print("{e.Kind} [{e.Path}]")
                        ExitError e => print(e)
                    }
                }
            """);
        if (Ran(text, "child-read-fails-bound", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sh]", "Other [sh]"], ran.Output);
            var control = Ran(text, "child-read-bound-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["TimedOut [sh]", "exit status 3: sh"], control.Output);
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
    public void A_child_gets_its_streams_where_the_programs_native_code_closed_its_standard_input()
    {
        // S21: the program's own native code closes descriptor 0, since the start opens `/dev/null` in place of one closed before it,
        // and the system gives descriptor 0 to the `/dev/null` or the pipe the call opens, which the runtime moves above 2, so that
        // the child's `dup2` onto its standard input takes away the close-on-exec mark and `cat` reads its input to the end; without
        // the move `cat` finds its standard input closed and ends with 1
        var text = "extern \"C\" {\n    int close(int fd)\n}\n\nraw {\n    close(0)\n}\n\n" + Program("""
                a = Command.New("cat").Output()
                b = Command.New("cat").Stdin(Stdio.Piped).Output()
                print("{a.Status.Success} {b.Status.Success}")
            """);
        if (Ran(text, "child-closed-input", grants: ["raw", "native"]) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["true true"], ran.Output);
        }
    }

    /// <summary>
    /// Two functions a program of the bound calls: the case of <c>Status()</c> and the case or the output's count of <c>Output()</c>, as a
    /// line, so that a program goes on after a failed call.
    /// </summary>
    private const string Cases = """
        string Waited(Command c) {
            c.Status() else {
                IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
                ExitError e => return "{e}"
            }

            return "ended"
        }

        string Tried(Command c) {
            match c.Output() {
                ProcessOutput o => return "{o.Stdout.Count}"
                IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
                ExitError e => return "{e}"
            }
        }

        """;

    [Fact]
    public void A_bound_that_passes_kills_the_child_and_gives_timed_out_with_the_program_as_its_path()
    {
        // S20: `Status()` and `Output()` kill a child still running at its bound, `Output()` after part of its output, and a bound of 0
        // or below has passed at the start, so that the child is killed once it started; a program that does not start is `NotFound`,
        // as without a bound (S21), and of two bounds the later holds (S21's order). Each child would run ten seconds, so the program's
        // own clock, read from before the first call on, shows that each was killed (knowledge/a-test-clock-holds-only-its-bound.md).
        var text = Program("""
                start = instant.Now
                for c in [Command.New("sleep").Arg("10").Timeout(100ms), Command.New("sleep").Arg("10").Timeout(0s), Command.New("sleep").Arg("10").Timeout(-1ms), Command.New("kurz-case-no-such").Timeout(0s), Command.New("sleep").Arg("10").Timeout(10s).Timeout(100ms)] {
                    print(Waited(c))
                }

                print(Tried(Command.New("sh").Args(["-c", "echo part; exec sleep 10"]).Timeout(300ms)))
                print(start.Elapsed < 8s)
            """) + Cases;
        if (Ran(text, "child-bound-passed") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["TimedOut [sleep] 0", "TimedOut [sleep] 0", "TimedOut [sleep] 0", "NotFound [kurz-case-no-such] 2", "TimedOut [sleep] 0", "TimedOut [sh] 0", "true"], ran.Output);
        }
    }

    [Fact]
    public void A_child_that_ends_before_its_bound_gives_its_own_status_at_its_end()
    {
        // S20: a child that ends before its bound gives its output and its status as without a bound, at its end and not at the bound;
        // the largest `duration` as a bound is held at the largest time of the clock, not wrapped past it into the past; of two bounds
        // the later holds (S21's order), so that a child of one second under `Timeout(100ms).Timeout(10s)` ends with its own status
        var text = Program("""
                start = instant.Now
                quick = Command.New("sh").Args(["-c", "echo ok"]).Timeout(duration.FromNanoseconds(9223372036854775807)).Output()
                print(string.FromUtf8(quick.Stdout).Split("\n"))
                print(Waited(Command.New("sh").Args(["-c", "exit 3"]).Timeout(10s)))
                print(Waited(Command.New("sh").Args(["-c", "sleep 1; exit 4"]).Timeout(100ms).Timeout(10s)))
                print(start.Elapsed < 8s)
            """) + Cases;
        if (Ran(text, "child-bound-held") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["[ok, ]", "exit status 3: sh", "exit status 4: sh", "true"], ran.Output); // bounds of ten seconds that did not hold the call
        }
    }

    [Fact]
    public void A_child_that_ended_while_another_process_holds_its_pipe_gives_its_own_status_at_the_bound()
    {
        // S20, the child's end deciding: a child that ended while a process it started still holds its standard output ends `Output()`
        // at the bound with its own status and what the call read, the pipes closed unread, where without a bound the call reads to
        // their end; the `sleep` holds the pipe six seconds, and the call ends after its bound of half a second and before five
        var text = Program("""
                start = instant.Now
                print(Tried(Command.New("sh").Args(["-c", "sleep 6 & echo started"]).Timeout(500ms)))
                taken = start.Elapsed
                print("{taken >= 500ms} {taken < 5s}")
            """) + Cases;
        if (Ran(text, "child-bound-pipe-held") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["8", "true true"], ran.Output); // `started` and its line's end
        }
    }

    [Fact]
    public void A_kill_at_the_bound_reaches_the_child_alone_and_a_process_it_started_lives_on()
    {
        // S20 (the owner, 2026-11-11, "All four as built", Q5): at the bound the call kills the child it started and no process the
        // child started, as Rust's `kill()` sends it; the `sleep` the child left behind, its streams sent away so that it holds none
        // of the program's, is still sleeping after the call, which its state in `/proc` reads, and is then ended by hand. A kill of
        // the child's process group would end it too, and the state would be gone or a zombie's, since a container's first process
        // may reap no orphan, so that `kill -0` would find the zombie and pass
        var text = Program("""
                print(Waited(Command.New("sh").Args(["-c", "sleep 5 > /dev/null 2>&1 & echo $! > kurz-left; exec sleep 10"]).Timeout(300ms)))
                print(Waited(Command.New("sh").Args(["-c", "grep -q sleeping /proc/$(cat kurz-left)/status"])))
                print(Waited(Command.New("sh").Args(["-c", "kill $(cat kurz-left)"])))
            """) + Cases;
        if (Ran(text, "child-bound-alone") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["TimedOut [sh] 0", "ended", "ended"], ran.Output);
        }
    }

    [Fact]
    public void A_bound_where_the_system_refuses_pidfd_open_kills_the_child_and_gives_the_refusal()
    {
        // S20 (assumed, §12, with the build of the bound): the call waits for the child's end through the descriptor `pidfd_open`
        // gives; where the system refuses it (a kernel before 5.3, or a filter), the call kills the child it started, waits for its end
        // and gives the system's error, here `ENOSYS` (38), `Unsupported`, at once and not at the bound; a call that went on without
        // the descriptor would wait to the bound and give `TimedOut`, which the positive control, under this machine's runtime, gives
        var text = Program("""
                start = instant.Now
                print(Waited(Command.New("sleep").Arg("10").Timeout(2s)))
                print(start.Elapsed < 1500ms)
            """) + Cases;
        if (Ran(text, "child-bound-no-pidfd", runtime: ListTests.Edited(("runtime/linux/linux.kz", "process = int(syscall(434, long(pid), long(0)))", "process = int(syscall(-1, long(pid), long(0)))"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Unsupported [sleep] 38", "true"], ran.Output);
            var control = Ran(text, "child-bound-pidfd-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["TimedOut [sleep] 0", "false"], control.Output);
        }
    }

    /// <summary>A line of a program that fills <c>big</c>, a <c>mut</c> variable of <c>Bytes</c>, with a MiB of <c>x</c>, more than a pipe holds.</summary>
    private const string Big = """
                mut big = Bytes()
                while big.Count < 1048576 {
                    big.Add(120)
                }

            """;

    [Fact]
    public void Input_gives_the_child_every_byte_and_then_the_end_of_its_input()
    {
        // S19: the input reaches the child byte for byte, a NUL byte and every byte above 127 among them, which the command holds in a
        // string (`CommandInput`), and its end after the last byte, under `Output()` and under `Status()`, whose child reads a MiB to its
        // end and then writes; a string gives its UTF-8 bytes (the owner, 2026-11-11, "Bytes and string"); a second `Input` replaces the
        // first (S21), of either form, and an empty one is an input closed at once
        var text = Program(Big + """
                mut all = Bytes()
                mut int v = 0
                while v < 256 {
                    all.Add(byte(v))
                    v = v + 1
                }

                o = Command.New("cat").Input(all).Output()
                print("{o.Stdout.Count} {o.Stdout == all}")
                print(string.FromUtf8(Command.New("cat").Input("a".Bytes).Input("bc".Bytes).Output().Stdout))
                t = Command.New("cat").Input("héllo").Output()
                print("{t.Stdout.Count} {t.Stdout == "héllo".Bytes}")
                print(string.FromUtf8(Command.New("cat").Input("a").Input("bc".Bytes).Output().Stdout))
                print(Command.New("cat").Input(Bytes()).Output().Stdout.Count)
                Command.New("sh").Args(["-c", "cat > /dev/null; echo read"]).Input(big).Status()
            """);
        if (Ran(text, "child-input") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["256 true", "bc", "6 true", "bc", "0", "read"], ran.Output);
        }
    }

    [Fact]
    public void A_read_of_the_output_that_fails_closes_the_input_too_so_that_the_child_reads_its_end()
    {
        // S18 with S19 (the owner, 2026-11-11, "Close pipes, wait", issue 174, comment 6802725183): a read that fails closes every
        // pipe the call holds to the child, the input's among them, so that a child that reads its input to the end gets the end at
        // once and the call ends; `wc` counts what reached it, and the call that writes on after the failure would give it the
        // whole MiB. The positive control, under this machine's runtime, reads the byte, and `wc` counts the whole MiB
        var text = Program(Big + """
                match Command.New("sh").Args(["-c", "printf a; wc -c > kurz-count"]).Input(big).Output() {
                    ProcessOutput o => print(o.Stdout.Count)
                    IoError e => print("{e.Kind} [{e.Path}]")
                    ExitError e => print(e)
                }

                Command.New("sh").Args(["-c", "test $(cat kurz-count) -lt 1048576"]).Status() else {
                    IoError e => print(e)
                    ExitError e => print(e)
                }

                print("checked")
            """);
        if (Ran(text, "child-read-fails-input", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sh]", "checked"], ran.Output);
            var control = Ran(text, "child-read-input-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["1", "exit status 1: sh", "checked"], control.Output);
        }
    }

    [Fact]
    public void A_child_that_ends_before_it_read_its_input_leaves_the_rest_unwritten()
    {
        // S19, the record's assumed clause: a child that ends before it read the whole input, or reads a part of it, leaves the rest
        // unwritten, and the call gives the child's status, under `Output()` and under `Status()`
        var text = Program(Big + """
                print(Tried(Command.New("true").Input(big)))
                print(Tried(Command.New("head").Args(["-c", "1"]).Input(big)))
                print(Waited(Command.New("true").Input(big)))
            """) + Cases;
        if (Ran(text, "child-input-unread") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["0", "1", "ended"], ran.Output);
        }
    }

    [Fact]
    public void A_child_that_does_not_read_its_input_holds_the_call_to_its_bound()
    {
        // S19, S20: an input the child does not read fills the pipe, and the write that waits for room is held to the bound
        var text = Program(Big + """
                start = instant.Now
                print(Tried(Command.New("sleep").Arg("10").Input(big).Timeout(300ms)))
                print(Waited(Command.New("sleep").Arg("10").Input(big).Timeout(300ms)))
                print(start.Elapsed < 8s)
            """) + Cases;
        if (Ran(text, "child-input-bound") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["TimedOut [sleep] 0", "TimedOut [sleep] 0", "true"], ran.Output);
        }
    }

    [Fact]
    public void A_write_of_the_input_that_fails_closes_every_pipe_and_waits_for_the_childs_end_under_the_bound()
    {
        // S19 (assumed, §12): a write of the input that fails, but for a child that closed its input (`EPIPE`), is handled as a
        // read that fails is (S18): every pipe closed, the child's end waited for under the bound (S20), and the write's `IoError`
        // the call's whatever status the child ends with; here under a runtime whose every write of the input fails (`EBADF`).
        // `sleep 10` under `Timeout(300ms)` is killed at the bound, so that a wait held by no bound shows on the program's clock,
        // and `exit 3` gives the write's error and not its status; the positive control, under this machine's runtime, writes
        // the byte and gives `TimedOut` and the status
        var text = Program("""
                start = instant.Now
                match Command.New("sleep").Arg("10").Input("x").Timeout(300ms).Output() {
                    ProcessOutput o => print(o.Stdout.Count)
                    IoError e => print("{e.Kind} [{e.Path}] {e.Code}")
                    ExitError e => print(e)
                }

                Command.New("sh").Args(["-c", "exit 3"]).Input("x").Status() else {
                    IoError e => print("{e.Kind} [{e.Path}] {e.Code}")
                    ExitError e => print(e)
                }

                print(start.Elapsed < 4s)
            """);
        if (Ran(text, "child-write-fails", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long wrote = write(fds[1], staging + sent, staged - sent)", "long wrote = write(-1, staging + sent, staged - sent)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sleep] 9", "Other [sh] 9", "true"], ran.Output);
            var control = Ran(text, "child-write-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["TimedOut [sleep] 0", "exit status 3: sh", "true"], control.Output);
        }
    }

    [Fact]
    public void Input_beside_a_stdin_member_is_invalid_input_at_the_start()
    {
        // S19, the owner, 2026-11-02: `Input` beside a member `Stdin`, of any `Stdio` and in either order, is `InvalidInput` with the
        // number 0 and the program as its path, under `Output()` and under `Status()`, and no child starts; the same command without its
        // `Stdin` member is the positive control
        var text = Program("""
                for c in [Command.New("cat").Input("hi".Bytes).Stdin(Stdio.Inherit), Command.New("cat").Stdin(Stdio.Null).Input("hi".Bytes), Command.New("cat").Input("hi".Bytes).Stdin(Stdio.Piped)] {
                    print(Tried(c))
                    print(Waited(c))
                }

                print(Tried(Command.New("cat").Input("hi".Bytes)))
            """) + Cases;
        if (Ran(text, "child-input-refused") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal([.. Enumerable.Repeat("InvalidInput [cat] 0", 6), "2"], ran.Output);
        }
    }

    /// <summary>
    /// The functions a program of a spawned child calls: the case of <c>Wait()</c>, the case or the counts of <c>WaitWithOutput()</c>, and
    /// the case of a <c>Spawn()</c> waited for, as a line, so that a program goes on after a failed call.
    /// </summary>
    private const string Children = """
        string Joined(ChildProcess c) {
            c.Wait() else {
                IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
                ExitError e => return "{e}"
            }

            return "ended"
        }

        string Gathered(ChildProcess c) {
            match c.WaitWithOutput() {
                ProcessOutput o => return "{o.Stdout.Count} {o.Stderr.Count}"
                IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
                ExitError e => return "{e}"
            }
        }

        string Started(Command c) {
            match c.Spawn() {
                ChildProcess child => return Joined(child)
                IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
            }
        }

        """;

    [Fact]
    public void Spawn_starts_the_child_and_every_wait_gives_the_status_of_its_end()
    {
        // S19: `Spawn()` gives the child, whose `Id` is its process's number; `Wait()` gives its end as `Status()` does, and a second wait
        // of either kind gives the status the first gave; a start that fails is the `IoError` of S21, the program or the directory its path
        var text = Program("""
                child = Command.New("sh").Args(["-c", "exit 3"]).Spawn()
                print(child.Id > 0)
                print(Joined(child))
                print(Joined(child))
                print(Gathered(child))
                ok = Command.New("true").Spawn()
                print(Joined(ok))
                print(Gathered(ok))
                print(Started(Command.New("kurz-case-no-such")))
                print(Started(Command.New("true").Dir("/kurz-case-no-such")))
            """) + Children;
        if (Ran(text, "child-spawn") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["true", "exit status 3: sh", "exit status 3: sh", "exit status 3: sh", "ended", "0 0", "NotFound [kurz-case-no-such] 2", "NotFound [/kurz-case-no-such] 2"], ran.Output);
        }
    }

    [Fact]
    public void The_piped_streams_of_a_spawned_child_are_files_and_its_wait_reads_each_from_where_the_program_stopped()
    {
        // S19: a stream set to `Stdio.Piped` is a `File`, the others `null`; the program writes the input and closes it, reads part of the
        // output through the buffer of S24, and `WaitWithOutput()` gives the rest, the bytes the `File` read ahead first; the pipe read
        // to its end stays open, so that a read of it gives no byte and a second `WaitWithOutput()` gives empty output
        var text = Program("""
                child = Command.New("cat").Stdin(Stdio.Piped).Stdout(Stdio.Piped).Spawn()
                match child.Stderr {
                    File => print("stderr")
                    null => print("no stderr")
                }

                match child.Stdin {
                    File input => {
                        input.Write("ab;cd;".Bytes)
                        input.Close()
                    }
                    null => print("no input")
                }

                match child.Stdout {
                    File out => {
                        mut first = Bytes()
                        n = out.Fill(mut first)
                        print(n)
                        out.Consume(3)
                    }
                    null => print("no output")
                }

                got = child.WaitWithOutput()
                print(string.FromUtf8(got.Stdout))
                match child.Stdout {
                    File out => {
                        mut rest = Bytes()
                        print(out.Read(mut rest, 10))
                    }
                    null => print("no output")
                }

                print(Gathered(child))
            """) + Children;
        if (Ran(text, "child-spawn-pipes") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["no stderr", "6", "cd;", "0", "0 0"], ran.Output);
        }
    }

    [Fact]
    public void A_pipe_the_program_closed_gives_the_wait_no_byte()
    {
        // S19, S5: `WaitWithOutput()` reads no pipe whose `File` a `Close` marked, so that the number the system gives the next pipe it
        // opens, the second child's output here, is not read through the first child
        var text = Program("""
                first = Command.New("true").Stdout(Stdio.Piped).Spawn()
                match first.Stdout {
                    File out => out.Close()
                    null => print("no output")
                }

                second = Command.New("sh").Args(["-c", "printf second"]).Stdout(Stdio.Piped).Spawn()
                print(Gathered(first))
                print(Gathered(second))
            """) + Children;
        if (Ran(text, "child-spawn-closed") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["0 0", "6 0"], ran.Output);
        }
    }

    [Fact]
    public void A_copy_of_a_child_holds_the_one_child_and_a_command_spawned_again_starts_another()
    {
        // S19, issue 174, Q5 (the owner, 2026-11-11, "All three as built", comment 6790379522): a copy of a `ChildProcess` is one more holder of the one child, as a copy of a `File` is, so that
        // a wait through the copy gives the status the first wait through the other holder gave; S17: `Spawn()` changes no member of its
        // command, so that the command spawned again starts a second child of the same program and arguments
        var text = Program("""
                cmd = Command.New("sh").Args(["-c", "exit 5"])
                child = cmd.Spawn()
                copy = child
                print(Joined(copy))
                print(Joined(child))
                print(copy.Id == child.Id)
                again = cmd.Spawn()
                print(again.Id == child.Id)
                print(Joined(again))
            """) + Children;
        if (Ran(text, "child-spawn-copy") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["exit status 5: sh", "exit status 5: sh", "true", "false", "exit status 5: sh"], ran.Output);
        }
    }

    [Fact]
    public void A_write_through_a_pipe_whose_reader_ended_or_that_the_program_closed_is_an_error()
    {
        // S19, S5, issue 174, Q9 (the owner, 2026-11-11, "All three as built", comment 6790379522): a write to the input of a child that ended is the system's `EPIPE`, `BrokenPipe`, since the
        // runtime sets `SIGPIPE` aside, with the program as the path of the pipe's `File`; the child closed its input when it ended, which
        // the end of its output shows; a write through a `File` the program closed is the closed file's error
        var text = Program("""
                child = Command.New("sh").Args(["-c", "printf done"]).Stdin(Stdio.Piped).Stdout(Stdio.Piped).Spawn()
                match child.Stdout {
                    File out => {
                        mut all = Bytes()
                        while out.Read(mut all, 64) > 0 {
                        }

                        print(string.FromUtf8(all))
                    }
                    null => print("no output")
                }

                match child.Stdin {
                    File input => {
                        input.Write("late".Bytes) else {
                            IoError e => print("{e.Kind} [{e.Path}] {e.Code}")
                        }
                    }
                    null => print("no input")
                }

                print(Joined(child))
                other = Command.New("cat").Stdin(Stdio.Piped).Spawn()
                match other.Stdin {
                    File input => {
                        input.Close()
                        input.Write("late".Bytes) else {
                            IoError e => print("{e.Kind} [{e.Path}] {e.Code}")
                        }
                    }
                    null => print("no input")
                }

                print(Joined(other))
            """) + Children;
        if (Ran(text, "child-spawn-broken") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["done", "BrokenPipe [sh] 32", "ended", "Other [cat] 9", "ended"], ran.Output);
        }
    }

    [Fact]
    public void A_wait_closes_the_piped_input_first_so_that_a_child_that_reads_to_its_end_ends()
    {
        // S19, issue 174, Q6 (the owner, 2026-11-11, "All three as built", comment 6790379522): `Wait()` closes the program's end of a piped standard input before it waits, as Rust's `wait`
        // drops `stdin`, so that `cat` reads its end; a write through the `File` after it is the closed file's error (S5). The bound of
        // five seconds turns a wait that holds into `TimedOut`, and the program's own clock shows that none held
        var text = Program("""
                start = instant.Now
                child = Command.New("cat").Stdin(Stdio.Piped).Timeout(5s).Spawn()
                print(Joined(child))
                match child.Stdin {
                    File input => {
                        input.Write("late".Bytes) else {
                            IoError e => print("{e.Kind} [{e.Path}] {e.Code}")
                        }
                    }
                    null => print("no input")
                }

                print(start.Elapsed < 4s)
            """) + Children;
        if (Ran(text, "child-spawn-close") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["ended", "Other [cat] 9", "true"], ran.Output);
        }
    }

    [Fact]
    public void A_wait_with_output_whose_read_fails_gives_the_error_at_once_and_leaves_the_child_to_the_program()
    {
        // S19 (the owner, 2026-11-11, "Error, no wait", issue 174, comment 6806240267): `WaitWithOutput()` whose read of a pipe fails
        // gives that `IoError`, with the program as its path, at once and waits for no child, under a bound as without one, so that the
        // `Kill()` after it reaches the child, whose wait then gives `signal 9`; a call that waited, with the pipes open or closed, or
        // to the child's end under its bound, would hold for the three seconds of `sleep` and find the child ended by itself, which the
        // status and the program's own clock show. The positive control, under this machine's runtime, reads the byte, waits for the
        // end, and its `Kill()` sends nothing
        var text = Program("""
                start = instant.Now
                child = Command.New("sh").Args(["-c", "printf a; exec sleep 3"]).Stdout(Stdio.Piped).Spawn()
                print(Gathered(child))
                child.Kill()
                print(Joined(child))
                bounded = Command.New("sh").Args(["-c", "printf a; exec sleep 3"]).Stdout(Stdio.Piped).Timeout(10s).Spawn()
                print(Gathered(bounded))
                bounded.Kill()
                print(Joined(bounded))
                print(start.Elapsed < 2s)
            """) + Children;
        if (Ran(text, "child-spawn-read-fails", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sh] 9", "signal 9: sh", "Other [sh] 9", "signal 9: sh", "true"], ran.Output);
            var control = Ran(text, "child-spawn-read-control")!.Value;
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["1 0", "ended", "1 0", "ended", "false"], control.Output);
        }
    }

    [Fact]
    public void A_wait_with_output_whose_read_fails_leaves_the_bytes_the_file_read_ahead_to_the_file()
    {
        // S19, its assumed reading of the owner's "Error, no wait" (2026-11-11, issue 174, comment 6806240267, the error at once and no
        // wait): a `WaitWithOutput()` that gives an `IoError` takes
        // none of the bytes the pipe's `File` read ahead, so that the program reads them through the `File` after the error. The child
        // writes `abc`, of which the program takes one byte through the buffer of S24, then reads its input to the end, which the wait
        // closes first (Q6), and writes `d`, whose read fails; a wait that took the bytes ahead would leave the `File` to read `d`
        var text = Program("""
                child = Command.New("sh").Args(["-c", "printf abc; read x; printf d; exec sleep 3"]).Stdin(Stdio.Piped).Stdout(Stdio.Piped).Spawn()
                match child.Stdout {
                    File out => {
                        mut first = Bytes()
                        n = out.Fill(mut first)
                        print(n)
                        out.Consume(1)
                    }
                    null => print("no output")
                }

                print(Gathered(child))
                child.Kill()
                print(Joined(child))
                match child.Stdout {
                    File out => {
                        mut rest = Bytes()
                        print(out.Read(mut rest, 10))
                        print(string.FromUtf8(rest))
                    }
                    null => print("no output")
                }
            """) + Children;
        if (Ran(text, "child-spawn-read-fails-ahead", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"))) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["3", "Other [sh] 9", "signal 9: sh", "2", "bc"], ran.Output);
        }
    }

    [Fact]
    public void A_wait_with_output_whose_read_fails_on_the_look_at_its_bound_kills_no_child()
    {
        // S19 (the owner, 2026-11-11, "Error, no wait", issue 174, comment 6806240267, for the error and no wait) and S20's assumed reading
        // of it (no kill at the bound): a read that fails on the look a wait takes at
        // its bound, which the runtime here takes at the first look, gives its `IoError`, waits for no child and kills none, as a read
        // that fails before the bound does, so that the child still sleeps, as `/proc` shows. A wait that killed it and waited for none
        // would leave a process that no longer sleeps, as `/proc` shows of the probe `Kill()` ended, the positive control
        var text = Program("""
                child = Command.New("sh").Args(["-c", "printf a; exec sleep 3"]).Stdout(Stdio.Piped).Timeout(10s).Spawn()
                probe = Command.New("sleep").Arg("10").Spawn()
                probe.Kill()
                Command.New("sleep").Arg("0.3").Status()
                print(Gathered(child))
                Command.New("sleep").Arg("0.3").Status()
                print(Sleeping(child.Id))
                print(Sleeping(probe.Id))
                child.Kill()
                print(Joined(probe))
            """) + Children + """
            string Sleeping(int id) {
                Command.New("grep").Args(["-q", "sleeping", "/proc/{id}/status"]).Status() else {
                    IoError e => return "{e.Kind} [{e.Path}] {e.Code}"
                    ExitError e => return "{e}"
                }

                return "sleeping"
            }

            """;
        var last = ("runtime/linux/linux.kz", "                if now >= deadline {\n                    late = true\n", "                if now >= 0 {\n                    late = true\n");
        if (Ran(text, "child-spawn-read-fails-last", runtime: ListTests.Edited(("runtime/linux/linux.kz", "long n = read(fd, buffer, 65536)", "long n = read(-1, buffer, 65536)"), last)) is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["Other [sh] 9", "sleeping", "exit status 1: grep", "signal 9: sleep"], ran.Output);
        }
    }

    [Fact]
    public void Kill_ends_the_child_with_sigkill_and_sends_nothing_to_one_that_was_waited_for()
    {
        // S19: `Kill()` sends `SIGKILL`, and the wait gives `signal 9`; a `Kill()` after the wait gives `void` and sends no signal, whose
        // number the system may have given to another process; `Id` stays the number after the wait
        var text = Program("""
                start = instant.Now
                child = Command.New("sleep").Arg("10").Spawn()
                id = child.Id
                child.Kill()
                print(Joined(child))
                child.Kill()
                print(child.Id == id)
                done = Command.New("true").Spawn()
                print(Joined(done))
                done.Kill()
                print("killed after the end")
                print(start.Elapsed < 8s)
            """) + Children;
        if (Ran(text, "child-spawn-kill") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["signal 9: sleep", "true", "ended", "killed after the end", "true"], ran.Output);
        }
    }

    [Fact]
    public void A_bound_under_spawn_is_held_by_the_wait_and_a_child_that_ended_before_it_gives_its_own_status()
    {
        // S20: under `Spawn()` the bound runs from the start and is held by `Wait()` and `WaitWithOutput()`, which kill a child still
        // running and give `TimedOut`, as a second wait does again (assumed: the result the first gave); a child that ended before its
        // bound gives its own status, also when the wait comes after the bound passed, the child's end deciding, and so does one that
        // `Kill()` ended before its bound, whose wait after the bound gives `signal 9`
        var text = Program("""
                start = instant.Now
                slow = Command.New("sleep").Arg("10").Timeout(300ms).Spawn()
                print(Joined(slow))
                print(Joined(slow))
                print(Gathered(slow))
                quick = Command.New("sh").Args(["-c", "exit 3"]).Timeout(10s).Spawn()
                print(Joined(quick))
                early = Command.New("sh").Args(["-c", "exit 4"]).Timeout(100ms).Spawn()
                killed = Command.New("sleep").Arg("10").Timeout(300ms).Spawn()
                killed.Kill()
                Command.New("sleep").Arg("0.5").Status()
                print(Joined(early))
                print(Joined(killed))
                print(start.Elapsed < 8s)
            """) + Children;
        if (Ran(text, "child-spawn-bound") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["TimedOut [sleep] 0", "TimedOut [sleep] 0", "TimedOut [sleep] 0", "exit status 3: sh", "exit status 4: sh", "signal 9: sleep", "true"], ran.Output);
        }
    }

    [Fact]
    public void A_child_let_go_of_runs_on_and_a_pipe_the_program_holds_outlives_it()
    {
        // S19: a child let go of without a wait is neither waited for nor killed, and a pipe's `File` is a holder of its own (S5; issue
        // 174, Q1), so that the program reads what the child writes after its `ChildProcess` is gone
        var text = """
            File? Pipe() {
                match Command.New("sh").Args(["-c", "sleep 0.3; printf late"]).Stdout(Stdio.Piped).Spawn() {
                    ChildProcess child => return child.Stdout
                    IoError => return null
                }
            }

            void | IoError | InvalidUtf8 Run() {
                match Pipe() {
                    File out => {
                        mut got = Bytes()
                        mut n = 1
                        while n > 0 {
                            n = out.Read(mut got, 100)
                        }

                        print(string.FromUtf8(got))
                    }
                    null => print("no output")
                }
            }

            Run() else {
                IoError e => print("{e.Kind} [{e.Path}]")
                InvalidUtf8 e => print(e)
            }

            """;
        if (Ran(text, "child-spawn-let-go") is { } ran)
        {
            Assert.Equal((0, ""), (ran.Code, ran.Error));
            Assert.Equal(["late"], ran.Output);
        }
    }

    [Theory]
    [InlineData("print(1)\nCommand.New(\"true\").Arg(\"x\").Status() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("void | IoError | ExitError Run() {\n    Command.New(\"true\").Status()\n}\n\nRun() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("void | IoError | ExitError Run() {\n    print(1)\n    out = Command.New(\"true\").Output()\n    print(out.Stdout.Count)\n}\n\nRun() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 3)]
    [InlineData("print(1)\nCommand.New(\"sleep\").Arg(\"1\").Timeout(1s).Status() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("print(1)\nCommand.New(\"cat\").Input(\"hi\".Bytes).Status() else {\n    IoError e => print(e)\n    ExitError e => print(e)\n}\n", 2)]
    [InlineData("print(1)\nmatch Command.New(\"cat\").Spawn() {\n    ChildProcess c => print(c.Id)\n    IoError e => print(e)\n}\n", 2)]
    public void A_build_for_windows_says_not_supported_at_the_first_call_on_a_command(string text, int line)
    {
        // S17 to S21 with S11: as `Stdin` is, the first call on `Command` or on a command, where the build stops
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((line, true), (diagnostic.Line, diagnostic.IsNotSupported)); // section 8: the line of an error, never its message
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
    [InlineData("print(1)\nc = Command.New(\"a\").Input(1)\n", "argument-mismatch", 2)] // S19, an `int` is neither `Bytes` nor a `string`, F13 for its two forms
    [InlineData("print(1)\nc = Command.New(\"a\").Timeout(1)\n", "type-mismatch", 2)] // S20, a number is no `duration`
    [InlineData("print(1)\nChildProcess c = null\nprint(c.Id)\n", "type-mismatch", 2)] // S19, a child is no `null`
    [InlineData("void M(ChildProcess c) {\n    print(c)\n}\n", "no-text", 2)] // S19, as of a `File`
    [InlineData("void M(ChildProcess c) {\n    print(c == c)\n}\n", "type-mismatch", 2)]
    [InlineData("void M(ChildProcess c) {\n    x = c.Run()\n}\n", "unknown-name", 2)] // V10
    [InlineData("void M(ChildProcess c) {\n    x = c.Id()\n}\n", "unknown-name", 2)]
    [InlineData("void M(ChildProcess c) {\n    print(c.Name)\n}\n", "unknown-name", 2)]
    [InlineData("void M(ChildProcess c) {\n    x = c.Wait(1)\n}\n", "argument-mismatch", 2)]
    [InlineData("void M(ChildProcess c) {\n    x = c.Stdout.Close()\n}\n", "nullable-unchecked", 2)] // N2: a `File?`
    [InlineData("print(1)\nx = ChildProcess.New()\n", "unknown-name", 2)]
    [InlineData("print(1)\nChildProcess<int> c = null\n", "syntax", 2)] // T17
    [InlineData("void Command() {\n}\n", "redeclared", 1)] // V9: a name every file sees
    [InlineData("data Stdio(int A)\n", "redeclared", 1)]
    [InlineData("data ExitStatus(int A)\n", "redeclared", 1)]
    [InlineData("data ProcessOutput(int A)\n", "redeclared", 1)]
    [InlineData("data ExitError(int A)\n", "redeclared", 1)]
    [InlineData("data ChildProcess(int A)\n", "redeclared", 1)]
    public void A_use_of_a_command_outside_its_members_is_refused_where_it_stands(string text, string id, int line)
    {
        // S17: a command has the members of S17 to S20 and no text and no `==`; S19: a child has the members of S19 and no text and no
        // `==`, and `ChildProcess` names no function; and a file declares none of the six names of S17 to S19 (V9)
        var refused = Compile(text, toolchain.Runtime);
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((id, line, id.Length == 0), (diagnostic.Id, diagnostic.Line, diagnostic.IsNotSupported)); // section 8: the id and the line of an error, never its message
    }
}
