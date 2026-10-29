using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kurz.Compiler.Build;

/// <summary>Runs the programs the compiler needs: clang, the program <c>kurz run</c> and <c>kurz exec</c> built, and in the tests the command itself.</summary>
public static class Processes
{
    /// <summary>
    /// Runs a program to its end with both of its streams captured, or ends it when the time is up;
    /// the exit code with what it wrote. A program that is not there or does not start answers with
    /// the code -1 and says so on its error stream. The variables given are set for it, a null one
    /// taken away.
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

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)limit.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (-1, output.Result, error.Result + $"\n`{file}` gave no answer within {limit.TotalSeconds} s and was ended");
            }

            return (process.ExitCode, output.Result, error.Result);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return (-1, string.Empty, $"`{file}` did not start: {e.Message}");
        }
    }

    /// <summary>
    /// How <c>kurz run</c> and <c>kurz exec</c> start the program: in the caller's directory, with no argument, and with the
    /// command's standard input, output and error as they are, nothing redirected (the owner, 2026-10-29, "Inherit all three").
    /// </summary>
    public static ProcessStartInfo Inheriting(string file) => new(file) { UseShellExecute = false };

    /// <summary>
    /// Runs the program to its end, as <see cref="Inheriting"/> starts it, and gives its exit code: on Linux 128 and the signal's
    /// number where a signal ended it, as .NET reports it. While it runs, the terminal's interrupt, and on Linux its quit, which
    /// the terminal sends to the command and the program alike, do not end this process, so that the program alone ends by them
    /// (the owner, 2026-10-29, "Command waits it out", as cargo does on Windows and C's <c>system()</c> does). A program that does
    /// not start throws <see cref="System.ComponentModel.Win32Exception"/>.
    /// </summary>
    public static int Run(string file)
    {
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => c.Cancel = true);
        using var quit = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGQUIT, c => c.Cancel = true);
        using var process = Process.Start(Inheriting(file)) ?? throw new System.ComponentModel.Win32Exception($"`{file}` did not start");
        process.WaitForExit();
        return process.ExitCode;
    }
}
