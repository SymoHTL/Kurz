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
    /// its new parent has taken it over, the nearest ancestor marked as a subreaper or else the first
    /// process of the system, so on Linux each process that holds the write end of one of them then is
    /// found by the pipes it holds and ended, and the run answers -1 with each one's pid on the error
    /// stream, and whether it was ended (knowledge/an-orphan-holds-the-output-of-its-program.md).
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
            var stood = string.Empty;
            if (!answered)
            {
                // read before the end, which takes the program's entry away
                stood = OperatingSystem.IsLinux() ? Stood("/proc", process.Id) : string.Empty;
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            var ending = answered ? "ended" : $"gave no answer within {limit.TotalSeconds} s and was ended{stood}";
            var rest = limit - clock.Elapsed;
            if (!Task.WaitAll([output, error], rest > Drain ? rest : Drain))
            {
                var orphans = Holders("/proc", pipes, Environment.ProcessId);
                var ended = End("/proc", orphans);
                var drained = Task.WaitAll([output, error], Drain);
                var held = Held(orphans, ended);
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

    /// <summary>
    /// A process that holds the write end of one of a run's pipes, with its start as <c>/proc/&lt;pid&gt;/stat</c> gives it (its 22nd
    /// field, in clock ticks after the boot), by which its end tells it from a process that took its id since.
    /// </summary>
    internal readonly record struct Holder(int Pid, ulong Start);

    /// <summary>
    /// Each process under <paramref name="proc"/> other than <paramref name="self"/> that holds the write end of one of the
    /// <paramref name="pipes"/>, read from its handles: <c>fd/&lt;n&gt;</c> names the pipe, which both of its ends share, and
    /// <c>fdinfo/&lt;n&gt;</c> says which end it is, by the access mode of its flags. A child that another thread of this process
    /// forked holds this process's read ends until its exec, and is no holder.
    /// </summary>
    internal static List<Holder> Holders(string proc, HashSet<string> pipes, int self)
    {
        var found = new List<Holder>();
        if (pipes.Count == 0)
        {
            return found;
        }

        foreach (var entry in Directory.EnumerateDirectories(proc))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid == self)
            {
                continue;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(Path.Combine(entry, "fd")).Any(fd => HoldsWriteEnd(fd, Path.Combine(entry, "fdinfo", Path.GetFileName(fd)), pipes))
                    && Start(entry) is { } start)
                {
                    found.Add(new Holder(pid, start));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // a process that ended while it was read, or one whose handles this user may not read: neither holds the pipes
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the handle <paramref name="fd"/> is one of the <paramref name="pipes"/>, open for writing as its <c>fdinfo</c> file at
    /// <paramref name="info"/> says. A handle that closed while it was read is no writer, and the process's other handles are read.
    /// </summary>
    internal static bool HoldsWriteEnd(string fd, string info, HashSet<string> pipes)
    {
        try
        {
            return new FileInfo(fd).LinkTarget is { } name && pipes.Contains(name) && Writes(info);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the handle that the <c>fdinfo</c> file at <paramref name="info"/> describes is open for writing: the access mode of its
    /// flags, written in octal, is 1 or 2, which their last digit holds. Flags that are no octal number are no writer's.
    /// </summary>
    private static bool Writes(string info)
    {
        foreach (var line in File.ReadLines(info))
        {
            if (line.StartsWith("flags:", StringComparison.Ordinal))
            {
                var flags = line["flags:".Length..].Trim();
                return flags.Length > 0 && flags.All(c => c is >= '0' and <= '7') && ((flags[^1] - '0') & 3) is 1 or 2;
            }
        }

        return false;
    }

    /// <summary>
    /// The start of the process whose <c>/proc</c> entry is <paramref name="entry"/>: the 22nd field of its <c>stat</c>, counted from
    /// the end of its name in brackets, which may hold a space; none where the field is not a number, or the text ends first.
    /// </summary>
    private static ulong? Start(string entry)
    {
        var stat = File.ReadAllText(Path.Combine(entry, "stat"));
        var close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 > stat.Length)
        {
            return null;
        }

        var fields = stat[(close + 2)..].Split(' ');
        return fields.Length > 19 && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ? start : null;
    }

    /// <summary>
    /// Ends each of the <paramref name="holders"/>, where its entry under <paramref name="proc"/> still gives the start it was found
    /// with, and gives back the ones it ended; one that ended first, or whose id another process took since, is passed over. It ends
    /// the holder alone and not its tree: a process of the tree that holds a write end is a holder of its own, and one that holds
    /// none holds nothing the run waits on.
    /// </summary>
    internal static List<Holder> End(string proc, List<Holder> holders)
    {
        var ended = new List<Holder>();
        foreach (var holder in holders)
        {
            try
            {
                if (Start(Path.Combine(proc, holder.Pid.ToString(CultureInfo.InvariantCulture))) != holder.Start)
                {
                    continue;
                }

                using var orphan = Process.GetProcessById(holder.Pid);
                orphan.Kill();
                ended.Add(holder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // IOException and UnauthorizedAccessException: its entry was gone or could not be read; ArgumentException and
                // InvalidOperationException: it ended; Win32Exception: it could not be ended. The run answers with what it has.
            }
        }

        return ended;
    }

    /// <summary>
    /// Where the process <paramref name="pid"/> stood when its run's limit passed, as Linux shows it under <paramref name="proc"/>, for the
    /// error of a run that ends a program at its limit: its state, the first field after the name in its <c>stat</c>; the kernel function
    /// it sleeps in, its <c>wchan</c>; and its <c>syscall</c>, the number of the call it is in with the call's six arguments, without the
    /// stack pointer and the program counter that follow them, or <c>-1</c> alone for a process that waits outside a call, or
    /// <c>running</c> (proc(5)). An argument that can be an address is given as <c>addr</c> (<see cref="Address"/>), so that the error
    /// names the call and its descriptors, counts and flags, and not where the program's memory lies. Where an entry cannot be read, it
    /// says so.
    /// </summary>
    internal static string Stood(string proc, int pid)
    {
        var entry = Path.Combine(proc, pid.ToString(CultureInfo.InvariantCulture));
        try
        {
            var stat = File.ReadAllText(Path.Combine(entry, "stat"));
            var close = stat.LastIndexOf(')');
            var state = close < 0 || close + 2 >= stat.Length ? string.Empty : stat[(close + 2)..].Split(' ')[0];
            var wchan = File.ReadAllText(Path.Combine(entry, "wchan")).Trim();
            var words = File.ReadAllText(Path.Combine(entry, "syscall")).Trim().Split(' ');
            var call = words[0] == "-1" ? "-1" : string.Join(' ', words.Take(7).Select((word, i) => i > 0 && Address(word) ? "addr" : word));
            return state.Length == 0 || call.Length == 0
                ? "; where it stood could not be read"
                : $"; where it stood, as /proc gave it: state {state}, wchan {wchan}, syscall {call}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // an entry gone, the process having ended at the limit, or one this user may not read
            return "; where it stood could not be read";
        }
    }

    /// <summary>
    /// Whether an argument of <c>syscall</c> can be an address: 0x10000 or more. Below that Linux maps no program's memory (the default
    /// of <c>vm.mmap_min_addr</c>), so a smaller value is a descriptor, a count or a flag; a count of 0x10000 or more is given as
    /// <c>addr</c> too.
    /// </summary>
    private static bool Address(string word) =>
        word.StartsWith("0x", StringComparison.Ordinal)
        && ulong.TryParse(word.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
        && value >= 0x10000;

    /// <summary>
    /// What the error stream of a run says of the <paramref name="found"/> holders: each one's pid, and whether it is among the
    /// <paramref name="ended"/>, as <c>pid 100, ended; pid 101, not ended</c>.
    /// </summary>
    internal static string Held(List<Holder> found, List<Holder> ended) =>
        found.Count == 0 ? "no process that holds it was found" : string.Join("; ", found.Select(h => $"pid {h.Pid}, {(ended.Contains(h) ? "ended" : "not ended")}"));

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
