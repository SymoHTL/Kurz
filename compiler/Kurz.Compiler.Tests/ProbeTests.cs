using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>The session's probe (<see cref="Probe"/>), run in this process on programs written to a directory of the test's own.</summary>
public sealed class ProbeTests : IDisposable
{
    // a read through the pointer of a list's items after the list let go of its block
    private const string ReadAfterFree = "mut xs = List<long>()\nxs.Add(41)\nmut long* p = null\nraw {\n    p = xs.Pointer\n}\nxs = List<long>()\nraw {\n    print(p[0])\n}\n";

    private readonly string directory = Directory.CreateTempSubdirectory("kurz-probe-tests-").FullName;

    private (int Code, string[] Lines) Probed(string text, params string[] args)
    {
        var path = Path.Combine(directory, "probe.kz");
        File.WriteAllText(path, text);
        var output = new StringWriter();
        var code = Probe.Run([path, .. args], output);
        return (code, output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[..^1]);
    }

    private static (int Code, string Lines) Joined((int Code, string[] Lines) probed) => (probed.Code, string.Join('\n', probed.Lines));

    [Fact]
    public void The_probe_builds_and_runs_a_program_and_prints_its_lines_and_its_exit_code()
    {
        Assert.Equal((0, "3\nexit 0"), Joined(Probed("print(1 + 2)\n")));
        Assert.Equal((0, "3\nexit 0"), Joined(Probed("print(1 + 2)\n", "--fenced")));
    }

    [Fact]
    public void Under_the_fence_a_read_of_a_freed_block_ends_the_program_and_without_it_does_not()
    {
        var (code, lines) = Probed(ReadAfterFree, "--fenced", "--grant", "raw");
        Assert.Equal(0, code);
        Assert.DoesNotContain("41", lines);
        Assert.StartsWith("exit ", lines[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(lines[^1], (string[])["exit 0", "exit 1"]); // the system's signal, not the program's end

        // the positive control: the same program under the allocator of the target reads what the freed block still holds
        Assert.Equal((0, "41\nexit 0"), Joined(Probed(ReadAfterFree, "--grant", "raw")));
    }

    [Fact]
    public void The_probe_prints_a_programs_errors_and_refuses_a_command_line_it_does_not_read()
    {
        var (code, lines) = Probed("print(missing)\n");
        Assert.Equal(1, code);
        Assert.Single(lines);
        Assert.Contains("missing", lines[0], StringComparison.Ordinal);

        var output = new StringWriter();
        Assert.Equal(2, Probe.Run(["--budget"], output));
        Assert.Contains("`--budget`", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, Probe.Run([], new StringWriter()));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // a directory the system still holds is left to it
        }
    }
}
