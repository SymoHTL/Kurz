using System.Diagnostics;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The run of a program with its streams captured (<see cref="Processes.Capture"/>), as the corpus's runner and the suites run one:
/// a child that the program left running, and that holds the streams it took from it, ends the run at its limit with -1 and, on
/// Linux, the child's pid on the error stream and the child ended, where the run waited on the streams without end and a run of
/// the suite hung for 80 minutes (knowledge/an-orphan-holds-the-output-of-its-program.md). Only a process that holds the write end
/// of a run's pipe is ended, not the processes it started, and only while its id is still its own.
/// </summary>
public sealed class CaptureTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    // the limit of a control, a program whose streams end with it: a cold start of `cmd.exe` on a CI runner took all of `Limit`
    private static readonly TimeSpan ControlLimit = TimeSpan.FromSeconds(60);

    // the time a process the run ended has to end in: the run sends the kill and does not wait, and in a run of the `gates` job the
    // child of `sh` still ran when the test read its state at once
    private static readonly TimeSpan EndBound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            // `start /b` leaves `ping` running with the output it took from `cmd`, which ends at once; no list of a process's handles
            // is read on Windows, so the run ends at its limit and says that it found no holder, and `ping` ends by itself
            Assert.Equal(0, Processes.Capture("cmd.exe", ["/c", "echo done"], null, ControlLimit).Code); // a program whose streams end with it
            var started = Stopwatch.StartNew();
            var held = Task.Run(() => Processes.Capture("cmd.exe", ["/c", "start /b ping -n 30 127.0.0.1"], null, Limit));
            Assert.True(await Task.WhenAny(held, Task.Delay(TimeSpan.FromSeconds(20))) == held, "the run waited past its limit on the output a child of its program holds");
            Assert.True(started.Elapsed >= Limit, $"the run ended after {started.Elapsed}, before its limit of {Limit}");
            var (heldCode, heldOutput, heldError) = await held;
            var heldSeen = $"output [{heldOutput}] error [{heldError}] after {started.Elapsed}"; // each assert below names what the run gave
            Assert.True(heldCode == -1, $"the run ended with {heldCode}, not -1: {heldSeen}");
            Assert.True(heldError.EndsWith("`cmd.exe` ended, and an orphan holds its output: no process that holds it was found", StringComparison.Ordinal), $"the error does not say that no holder was found: {heldSeen}");
            return;
        }

        Assert.Equal((0, "done\n", ""), Processes.Capture("/bin/sh", ["-c", "echo done"], null, ControlLimit)); // a program whose streams end with it

        var clock = Stopwatch.StartNew();
        var run = Task.Run(() => Processes.Capture("/bin/sh", ["-c", "sleep 60 & echo $!"], null, Limit));
        Assert.True(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20))) == run, "the run waited past its limit on the output a child of its program holds");
        Assert.True(clock.Elapsed >= Limit, $"the run ended after {clock.Elapsed}, before its limit of {Limit}");
        var (code, output, error) = await run;
        var child = output.Trim();
        var seen = $"output [{output}] error [{error}] after {clock.Elapsed}"; // each assert below names what the run gave
        Assert.True(code == -1, $"the run ended with {code}, not -1: {seen}");
        Assert.True(child.Length > 0 && child.All(char.IsAsciiDigit), $"the output holds no pid: {seen}");
        Assert.True(error.EndsWith($"`/bin/sh` ended, and an orphan holds its output: pid {child}, ended", StringComparison.Ordinal), $"the error does not say that the child {child} was ended: {seen}");
        // the child must have ended within `EndBound` of the run's end
        var since = Stopwatch.StartNew();
        while (Lives(child) && since.Elapsed < EndBound)
        {
            await Task.Delay(50);
        }

        Assert.False(Lives(child), $"the child {child} still runs {since.Elapsed} after the run ended it: {seen}");
    }

    [Fact]
    public void Only_a_process_that_holds_the_write_end_of_a_pipe_of_the_run_is_its_holder()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the handles a process holds are read from /proc on Linux alone: nothing checks the scan elsewhere
        }

        // a tree laid out as /proc lays it out: `fd/<n>` a link to the pipe's name, which both ends share, and `fdinfo/<n>` its flags
        var proc = Directory.CreateTempSubdirectory("kurz-proc-").FullName;
        try
        {
            void Lay(int pid, int fd, string pipe, string flags)
            {
                var entry = Path.Combine(proc, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(Path.Combine(entry, "fd"));
                Directory.CreateDirectory(Path.Combine(entry, "fdinfo"));
                File.CreateSymbolicLink(Path.Combine(entry, "fd", $"{fd}"), pipe);
                File.WriteAllText(Path.Combine(entry, "fdinfo", $"{fd}"), $"pos:\t0\nflags:\t{flags}\nmnt_id:\t15\n");
                File.WriteAllText(Path.Combine(entry, "stat"), $"{pid} (a (named) one) S 1 {pid} {pid} 0 -1 4194560 0 0 0 0 0 0 0 0 20 0 1 0 {pid * 10} 2048 1 0\n");
            }

            Lay(100, 1, "pipe:[7]", "01"); // the write end, as an orphan holds the output: a holder
            Lay(101, 2, "pipe:[7]", "02100002"); // a write end opened for reading too: a holder
            Lay(200, 9, "pipe:[7]", "02000000"); // a read end, as a child that another thread forked holds it before its exec
            Lay(300, 1, "pipe:[8]", "01"); // the write end of a pipe that is not the run's
            Lay(400, 1, "pipe:[7]", "01"); // this process
            Lay(500, 4, "pipe:[7]", "01"); // the write end beside a handle that closed while it was read: a holder
            var closed = Path.Combine(proc, "500", "fd", "3");
            File.CreateSymbolicLink(closed, "pipe:[7]"); // its fdinfo is gone
            Lay(600, 1, "pipe:[7]", "zz"); // flags that are no octal number
            Lay(700, 1, "pipe:[7]", "01");
            File.WriteAllText(Path.Combine(proc, "700", "stat"), "700 (x)"); // a stat that ends after its name: no start
            Directory.CreateDirectory(Path.Combine(proc, "self"));

            Assert.Equal(
                new[] { new Processes.Holder(100, 1000), new Processes.Holder(101, 1010), new Processes.Holder(500, 5000) },
                Processes.Holders(proc, ["pipe:[7]"], 400).OrderBy(h => h.Pid).ToArray());
            Assert.Empty(Processes.Holders(proc, [], 400)); // no pipe read, no holder
            Assert.False(Processes.HoldsWriteEnd(closed, Path.Combine(proc, "500", "fdinfo", "3"), ["pipe:[7]"])); // read in any order
            Assert.False(Processes.HoldsWriteEnd(Path.Combine(proc, "500", "fd", "9"), Path.Combine(proc, "500", "fdinfo", "9"), ["pipe:[7]"]));
            Assert.True(Processes.HoldsWriteEnd(Path.Combine(proc, "500", "fd", "4"), Path.Combine(proc, "500", "fdinfo", "4"), ["pipe:[7]"]));
        }
        finally
        {
            Directory.Delete(proc, recursive: true);
        }
    }

    [Fact]
    public void A_holder_whose_id_another_process_took_since_is_not_ended()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the start of a process is read from /proc on Linux alone: nothing checks it elsewhere
        }

        using var sleeper = Process.Start("/bin/sleep", "30") ?? throw new InvalidOperationException("`/bin/sleep` did not start");
        var start = StartOf(sleeper.Id);
        try
        {
            Assert.Empty(Processes.End("/proc", [new Processes.Holder(sleeper.Id, start + 1)])); // the start it was found with is not the one it has
            Assert.False(sleeper.WaitForExit(500), "a process whose start is not the holder's was ended");
            var holder = new Processes.Holder(sleeper.Id, start);
            Assert.Equal(new[] { holder }, Processes.End("/proc", [holder])); // a positive control: its own start
            Assert.True(sleeper.WaitForExit(5000), "the holder was not ended");
        }
        finally
        {
            if (!sleeper.HasExited)
            {
                sleeper.Kill();
            }
        }
    }

    [Fact]
    public void A_holder_is_ended_alone_and_not_the_processes_it_started()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the start of a process is read from /proc on Linux alone: nothing ends a holder elsewhere
        }

        // the shell starts a `sleep` that holds none of its streams, says its pid and waits for it
        var info = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("sleep 30 </dev/null >/dev/null 2>&1 & echo $!; wait");
        using var shell = Process.Start(info) ?? throw new InvalidOperationException("`/bin/sh` did not start");
        var child = shell.StandardOutput.ReadLine() ?? string.Empty;
        try
        {
            Assert.Matches("^[0-9]+$", child);
            var holder = new Processes.Holder(shell.Id, StartOf(shell.Id));
            Assert.Equal(new[] { holder }, Processes.End("/proc", [holder]));
            Assert.True(shell.WaitForExit(5000), "the holder was not ended");
            Thread.Sleep(500);
            Assert.True(Lives(child), $"the child {child} of the holder, which holds nothing of the run, was ended");
        }
        finally
        {
            if (!shell.HasExited)
            {
                shell.Kill();
            }

            if (int.TryParse(child, out var pid) && Lives(child))
            {
                using var sleeping = Process.GetProcessById(pid);
                sleeping.Kill();
            }
        }
    }

    [Fact]
    public void The_error_names_each_holder_and_whether_it_was_ended()
    {
        var ended = new Processes.Holder(100, 1000);
        var passed = new Processes.Holder(101, 1010);
        Assert.Equal("pid 100, ended; pid 101, not ended", Processes.Held([ended, passed], [ended]));
        Assert.Equal("pid 100, ended", Processes.Held([ended], [ended]));
        Assert.Equal("no process that holds it was found", Processes.Held([], []));
    }

    [Fact]
    public void A_program_ended_at_its_limit_names_where_it_stood()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // no /proc is read elsewhere, and the error ends as before
        }

        // a positive control first: a program that ends inside its limit says nothing of where it stood
        Assert.Equal((0, "done\n", ""), Processes.Capture("/bin/sh", ["-c", "echo done"], null, TimeSpan.FromSeconds(60)));

        // `sleep` waits in a call of the system the whole time, so its state is S and its syscall a number and six arguments, none
        // printed as a value of 0x10000 or more; the call takes the address of its time, so one of them at least is `addr`
        var (code, output, error) = Processes.Capture("/bin/sleep", ["30"], null, Limit);
        Assert.Equal((-1, ""), (code, output));
        Assert.Matches("^\n`/bin/sleep` gave no answer within 3 s and was ended; where it stood, as /proc gave it: state S, wchan \\S+, syscall [0-9]+( (0x[0-9a-f]{1,4}|addr)){6}$", error);
        Assert.Contains(" addr", error);
    }

    [Fact]
    public void Where_a_program_stood_is_read_from_its_entry_without_the_pointers()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // /proc is read on Linux alone
        }

        var proc = Directory.CreateTempSubdirectory("kurz-proc-").FullName;
        try
        {
            void Lay(int pid, string stat, string wchan, string syscall)
            {
                var entry = Path.Combine(proc, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(entry);
                File.WriteAllText(Path.Combine(entry, "stat"), stat);
                File.WriteAllText(Path.Combine(entry, "wchan"), wchan);
                File.WriteAllText(Path.Combine(entry, "syscall"), syscall);
            }

            // a name with a space and a bracket, a call with its six arguments and the two pointers after them, which are left out,
            // and the address of the call's buffer, which is given as addr
            Lay(200, "200 (a b) c) S 1 200 200 0", "wait_woken", "0 0x4 0x55d0c2a01000 0x2000 0x0 0x8 0x0 0x7ffe0000 0x7f000001\n");
            Assert.Equal("; where it stood, as /proc gave it: state S, wchan wait_woken, syscall 0 0x4 addr 0x2000 0x0 0x8 0x0", Processes.Stood(proc, 200));

            // the edge: 0xffff is a value, 0x10000 can be an address
            Lay(205, "205 (p) S 1 205 205 0", "pipe_read", "0 0xffff 0x10000 0x0 0x0 0x0 0x0 0x7ffe0000 0x7f000001\n");
            Assert.Equal("; where it stood, as /proc gave it: state S, wchan pipe_read, syscall 0 0xffff addr 0x0 0x0 0x0 0x0", Processes.Stood(proc, 205));

            // a process that waits outside a call: -1 alone, its pointers left out; and one that runs, which waits in nothing
            Lay(201, "201 (p) D 1 201 201 0", "folio_wait_bit", "-1 0x7ffe0000 0x7f000001\n");
            Assert.Equal("; where it stood, as /proc gave it: state D, wchan folio_wait_bit, syscall -1", Processes.Stood(proc, 201));
            Lay(202, "202 (p) R 1 202 202 0", "0", "running\n");
            Assert.Equal("; where it stood, as /proc gave it: state R, wchan 0, syscall running", Processes.Stood(proc, 202));

            // an entry that is gone, as the program's is once it ended, and a stat that ends at its name
            Assert.Equal("; where it stood could not be read", Processes.Stood(proc, 203));
            Lay(204, "204 (p)", "0", "running\n");
            Assert.Equal("; where it stood could not be read", Processes.Stood(proc, 204));
        }
        finally
        {
            Directory.Delete(proc, recursive: true);
        }
    }

    /// <summary>The start of the running process <paramref name="pid"/>: the 22nd field of its stat, counted from the end of its name in brackets.</summary>
    private static ulong StartOf(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid.ToString(System.Globalization.CultureInfo.InvariantCulture)}/stat");
        return ulong.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the process runs: it has an entry in /proc, and the entry is no zombie, which a first process of the system that reaps nothing keeps.</summary>
    private static bool Lives(string pid)
    {
        var stat = $"/proc/{pid}/stat";
        try
        {
            var text = File.ReadAllText(stat);
            return !text[(text.LastIndexOf(')') + 2)..].StartsWith('Z');
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
    }
}
