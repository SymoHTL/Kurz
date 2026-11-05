using System.Diagnostics;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The run of a program with its streams captured (<see cref="Processes.Capture"/>), as the corpus's runner and the suites run one:
/// a child that the program left running, and that holds the streams it took from it, ends the run at its limit with -1, the
/// child's pid on the error stream and the child ended, where the run waited on the streams without end and a run of the suite
/// hung for 80 minutes (knowledge/an-orphan-holds-the-output-of-its-program.md).
/// </summary>
public sealed class CaptureTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/bin/sh` to fork a child there, and the handles a process holds are read from /proc
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
