using System.Diagnostics;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The run of a program with its streams captured (<see cref="Processes.Capture"/>), as the corpus's runner and the suites run one:
/// a child that the program left running, and that holds the streams it took from it, ends the run at its limit with -1, the
/// child's pid on the error stream and, on Linux, the child ended, where the run waited on the streams without end and a run of
/// the suite hung for 80 minutes (knowledge/an-orphan-holds-the-output-of-its-program.md). Only a process that holds the write end
/// of a run's pipe is ended, and only while its id is still its own.
/// </summary>
public sealed class CaptureTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            // `start /b` leaves `ping` running with the output it took from `cmd`, which ends at once; no list of a process's handles
            // is read on Windows, so the run ends at its limit and says that it found no holder, and `ping` ends by itself
            Assert.Equal(0, Processes.Capture("cmd.exe", ["/c", "echo done"], null, Limit).Code); // a program whose streams end with it
            var started = Stopwatch.StartNew();
            var held = Task.Run(() => Processes.Capture("cmd.exe", ["/c", "start /b ping -n 30 127.0.0.1"], null, Limit));
            Assert.True(await Task.WhenAny(held, Task.Delay(TimeSpan.FromSeconds(20))) == held, "the run waited past its limit on the output a child of its program holds");
            Assert.True(started.Elapsed >= Limit, $"the run ended after {started.Elapsed}, before its limit of {Limit}");
            var (heldCode, _, heldError) = await held;
            Assert.Equal(-1, heldCode);
            Assert.EndsWith("`cmd.exe` ended, and an orphan holds its output: no process that holds it was found", heldError, StringComparison.Ordinal);
            return;
        }

        Assert.Equal((0, "done\n", ""), Processes.Capture("/bin/sh", ["-c", "echo done"], null, Limit)); // a program whose streams end with it

        var clock = Stopwatch.StartNew();
        var run = Task.Run(() => Processes.Capture("/bin/sh", ["-c", "sleep 60 & echo $!"], null, Limit));
        Assert.True(await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20))) == run, "the run waited past its limit on the output a child of its program holds");
        Assert.True(clock.Elapsed >= Limit, $"the run ended after {clock.Elapsed}, before its limit of {Limit}");
        var (code, output, error) = await run;
        var child = output.Trim();
        Assert.Equal(-1, code);
        Assert.Matches("^[0-9]+$", child);
        Assert.EndsWith($"`/bin/sh` ended, and an orphan holds its output: pid {child}, ended", error, StringComparison.Ordinal);
        Assert.False(Lives(child), $"the child {child} still runs");
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
            Directory.CreateDirectory(Path.Combine(proc, "self"));

            Assert.Equal(
                new[] { new Processes.Holder(100, 1000), new Processes.Holder(101, 1010) },
                Processes.Holders(proc, ["pipe:[7]"], 400).OrderBy(h => h.Pid).ToArray());
            Assert.Empty(Processes.Holders(proc, [], 400)); // no pipe read, no holder
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
        var pid = sleeper.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var start = ulong.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19], System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Processes.End("/proc", [new Processes.Holder(sleeper.Id, start + 1)]); // the start it was found with is not the one it has
            Assert.False(sleeper.WaitForExit(500), "a process whose start is not the holder's was ended");
            Processes.End("/proc", [new Processes.Holder(sleeper.Id, start)]); // a positive control: its own start
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
