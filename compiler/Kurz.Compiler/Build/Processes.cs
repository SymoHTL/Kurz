using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace Kurz.Compiler.Build;

/// <summary>Runs the programs the compiler needs: clang, the program <c>kurz run</c> and <c>kurz exec</c> built, and in the tests the command itself.</summary>
public static class Processes
{
    /// <summary>The time the streams of a program get to end once it has ended, when its limit has passed.</summary>
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs a program to its end with both of its streams captured, or ends it when the time is up;
    /// the exit code with what it wrote. A program that is not there or does not start answers with
    /// the code -1 and says so on its error stream. The variables given are set for it, a null one
    /// taken away. The streams are waited for within what remains of the limit: a child the program
    /// left running holds them past the program's end, and no end of the program's tree reaches it once
    /// the first process of the system has taken it over, so on Linux each process that holds them then
    /// is found by the pipes it holds and ended, and the run answers -1 with its pid on the error stream
    /// (knowledge/an-orphan-holds-the-output-of-its-program.md).
    /// </summary>
    public static (int Code, string Output, string Error) Capture(string file, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan limit, IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, // what clang and the command write, whatever the console's page
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            info.Environment[name] = value;
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return (-1, string.Empty, $"`{file}` did not start");
            }

            var clock = Stopwatch.StartNew();
            var pipes = Pipes(process);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            var answered = process.WaitForExit((int)limit.TotalMilliseconds);
            if (!answered)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            var ending = answered ? "ended" : $"gave no answer within {limit.TotalSeconds} s and was ended";
            var rest = limit - clock.Elapsed;
            if (!Task.WaitAll([output, error], rest > Drain ? rest : Drain))
            {
                var orphans = Holders(pipes);
                End(orphans);
                var drained = Task.WaitAll([output, error], Drain);
                var held = orphans.Count == 0 ? "no process that holds it was found" : $"pid {string.Join(", ", orphans)}, ended";
                return (-1, drained ? output.Result : string.Empty, (drained ? error.Result : string.Empty) + $"\n`{file}` {ending}, and an orphan holds its output: {held}");
            }

            if (!answered)
            {
                return (-1, output.Result, error.Result + $"\n`{file}` {ending}");
            }

            return (process.ExitCode, output.Result, error.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, string.Empty, $"`{file}` did not start: {e.Message}");
        }
    }

    /// <summary>
    /// The pipes of the program's two streams as Linux names them, <c>pipe:[inode]</c>, read from this process's own ends of
    /// them, which the same name stands for; none elsewhere, where no list of a process's handles is read.
    /// </summary>
    private static HashSet<string> Pipes(Process process)
    {
        var pipes = new HashSet<string>(StringComparer.Ordinal);
        if (!OperatingSystem.IsLinux())
        {
            return pipes;
        }

        foreach (var stream in new[] { process.StandardOutput.BaseStream, process.StandardError.BaseStream })
        {
            if (stream is PipeStream pipe && new FileInfo($"/proc/self/fd/{pipe.SafePipeHandle.DangerousGetHandle()}").LinkTarget is { } name)
            {
                pipes.Add(name);
            }
        }

        return pipes;
    }

    /// <summary>Each process other than this one that holds one of the <paramref name="pipes"/>, read from its handles in <c>/proc</c>.</summary>
    private static List<int> Holders(HashSet<string> pipes)
    {
        var found = new List<int>();
        if (pipes.Count == 0)
        {
            return found;
        }

        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(Path.Combine(entry, "fd")).Any(fd => new FileInfo(fd).LinkTarget is { } name && pipes.Contains(name)))
                {
                    found.Add(pid);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a process that ended while it was read, or one whose handles this user may not read: neither holds the pipes
            }
        }

        return found;
    }

    /// <summary>Ends each of the processes, with what they started; one that ended first is passed over.</summary>
    private static void End(List<int> processes)
    {
        foreach (var pid in processes)
        {
            try
            {
                using var orphan = Process.GetProcessById(pid);
                orphan.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // it ended before the end reached it
            }
        }
    }

    /// <summary>
    /// How <c>kurz run</c> and <c>kurz exec</c> start the program: in the caller's directory, with the words after <c>--</c> as its
    /// arguments, each as it was given (the owner, 2026-10-30, "After <c>--</c>, as cargo"), and with the command's standard input,
    /// output and error as they are, nothing redirected (the owner, 2026-10-29, "Inherit all three").
    /// </summary>
    public static ProcessStartInfo Inheriting(string file, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        return info;
    }

    /// <summary>
    /// Runs the program to its end, as <see cref="Inheriting"/> starts it, and gives its exit code: on Linux 128 and the signal's
    /// number where a signal ended it, as .NET reports it. While it runs, the terminal's interrupt, and on Linux its quit, which
    /// the terminal sends to the command and the program alike, do not end this process, so that the program alone ends by them
    /// (the owner, 2026-10-29, "Command waits it out", as cargo does on Windows and C's <c>system()</c> does). A program that does
    /// not start throws <see cref="System.ComponentModel.Win32Exception"/>.
    /// </summary>
    public static int Run(string file, IReadOnlyList<string> arguments)
    {
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true);
        using var quit = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGQUIT, c => c.Cancel = true);
        using var process = Process.Start(Inheriting(file, arguments)) ?? throw new System.ComponentModel.Win32Exception($"`{file}` did not start");
        process.WaitForExit();
        return process.ExitCode;
    }
}
