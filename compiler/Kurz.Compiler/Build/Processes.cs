using System.Diagnostics;
using System.Text;

namespace Kurz.Compiler.Build;

/// <summary>Runs the programs the compiler needs: clang, and in the tests the command itself.</summary>
public static class Processes
{
    /// <summary>
    /// Runs a program to its end with both of its streams captured, or ends it when the time is up;
    /// the exit code with what it wrote. A program that is not there or does not start answers with
    /// the code -1 and says so on its error stream. <paramref name="environment"/> sets or replaces
    /// variables of the program's environment.
    /// </summary>
    public static (int Code, string Output, string Error) Capture(string file, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan limit, IReadOnlyDictionary<string, string>? environment = null)
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

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
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
}
