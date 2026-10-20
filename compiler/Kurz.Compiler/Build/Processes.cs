using System.Diagnostics;
using System.Text;

namespace Kurz.Compiler.Build;

/// <summary>Runs the programs the compiler needs: clang, and a built program under <c>run</c> and <c>exec</c>.</summary>
public static class Processes
{
    /// <summary>
    /// Runs a program to its end with both of its streams captured, or ends it when the time is
    /// up; the exit code with what it wrote. A program that is not there answers with the code -1
    /// and says so on its error stream. <paramref name="environment"/> sets or replaces variables
    /// of the program's environment.
    /// </summary>
    public static (int Code, string Output, string Error) Capture(string file, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan limit, IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = Start(file, arguments, workingDirectory);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8; // what a Kurz program and clang write, whatever the console's page
        info.StandardErrorEncoding = Encoding.UTF8;
        try
        {
            using var process = Process.Start(info) ?? throw new ToolchainException($"`{file}` did not start");
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
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, string.Empty, $"`{file}` is not there");
        }
    }

    /// <summary>Runs a program on the caller's own streams, as <c>run</c> and <c>exec</c> run the program they built; its exit code.</summary>
    public static int Inherit(string file, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var process = Process.Start(Start(file, arguments, workingDirectory)) ?? throw new ToolchainException($"`{file}` did not start");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static ProcessStartInfo Start(string file, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        return info;
    }
}

/// <summary>The toolchain did not do what the compiler asked: clang is missing, or refused the IR or the runtime.</summary>
public sealed class ToolchainException(string message) : Exception(message);
