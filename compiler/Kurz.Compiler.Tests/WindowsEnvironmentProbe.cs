using System.Diagnostics;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>A probe of what a child process on Windows reads of the variables its parent gives it.</summary>
public sealed class WindowsEnvironmentProbe
{
    private static string Run(string file, string empty, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["PATH"] = empty;
        info.Environment["ProgramFiles"] = empty;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    [Fact]
    public void Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var empty = Directory.CreateTempSubdirectory("kurz-probe-").FullName;
        var cmd = Run(Path.Combine(Environment.SystemDirectory, "cmd.exe"), empty, "/c", "set");
        var pwsh = Run(Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles")!, "PowerShell", "7", "pwsh.exe"), empty, "-NoProfile", "-Command",
            "'GetEnvironmentVariable ProgramFiles=' + [Environment]::GetEnvironmentVariable('ProgramFiles'); 'GetEnvironmentVariable PATH=' + [Environment]::GetEnvironmentVariable('PATH'); 'GetFolderPath=' + [Environment]::GetFolderPath('ProgramFiles'); [Environment]::Is64BitProcess");
        var lines = cmd.Split('\n').Where(l => l.StartsWith("Program", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Path", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Common", StringComparison.OrdinalIgnoreCase));
        Assert.Fail($"empty={empty}\nparent ProgramFiles={Environment.GetEnvironmentVariable("ProgramFiles")} 64bit={Environment.Is64BitProcess}\n--- cmd /c set\n{string.Join("\n", lines)}\n--- pwsh\n{pwsh}");
    }
}
