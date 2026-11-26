using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The count of worker threads that the start of a program with a <c>spawn</c> reads from <c>KURZ_THREADS</c> (U35), which no corpus case
/// can set, its header holding no environment: a value that is no whole number above 0 written in digits alone, or above the greatest
/// <c>int</c>, or not UTF-8, ends the start with <c>threads-variable</c> and the exit code 1. Each program is built for Linux and run with
/// <c>KURZ_THREADS</c> set or taken away; a build for Windows reads no <c>KURZ_THREADS</c>, its runtime giving no variable of the
/// environment (S14), and no test runs there.
/// </summary>
public sealed class ThreadsTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const string Spawns = "actor Pinger {\n    pub int Ping() => 1\n}\n\np = spawn Pinger()\nprint(p.Ping())\n";

    /// <summary>
    /// The program built for Linux and run by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="setup"/>, with <c>KURZ_THREADS</c>
    /// taken away and <c>KURZ_TRACE=0</c>: its exit code, its lines and its error stream; null on Windows.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            return null;
        }

        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup}exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0", ["KURZ_THREADS"] = null });
        return (code, Toolchain.Lines(output), error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")] // empty, as tokio refuses an empty `TOKIO_WORKER_THREADS`
    [InlineData("0")] // no whole number above 0
    [InlineData("-1")]
    [InlineData("+4")] // digits alone, where Rust's parse of a `usize` takes the `+`
    [InlineData(" 4")]
    [InlineData("4 ")]
    [InlineData("4.0")]
    [InlineData("2147483648")] // the greatest `int` and 1
    [InlineData("99999999999999999999")] // beyond a `long` too
    public void A_KURZ_THREADS_that_is_no_whole_number_above_0_ends_the_start_with_threads_variable_naming_the_value(string value) =>
        Refused($"export KURZ_THREADS='{value}'; ", $"error: threads-variable ({value})\n");

    [Fact]
    public void A_KURZ_THREADS_that_is_not_UTF_8_ends_the_start_with_threads_variable_not_unicode() =>
        Refused("export KURZ_THREADS=\"$(printf '4\\377')\"; ", "error: threads-variable (not unicode)\n");

    [Theory]
    [InlineData("export KURZ_THREADS=1; ")]
    [InlineData("export KURZ_THREADS=4; ")]
    [InlineData("export KURZ_THREADS=007; ")] // digits alone, zeros in front among them
    [InlineData("")] // not set: the cores the process may use
    public void A_KURZ_THREADS_that_is_a_whole_number_above_0_or_none_starts_the_program(string setup)
    {
        if (Ran(Spawns, $"threads-ok-{Math.Abs(setup.GetHashCode(StringComparison.Ordinal))}", setup) is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["1"], run.Output);
        }
    }

    /// <summary>
    /// A program with a <c>spawn</c>, started after <paramref name="setup"/>, ends with the exit code 1 and <paramref name="error"/> alone,
    /// before its first statement, while a program with no <c>spawn</c> under <paramref name="setup"/>, which reads no
    /// <c>KURZ_THREADS</c>, runs.
    /// </summary>
    private void Refused(string setup, string error)
    {
        const string text = "print(\"start\")\n" + Spawns;
        if (Ran(text, $"threads-refused-{Math.Abs(setup.GetHashCode(StringComparison.Ordinal))}", setup) is { } run)
        {
            Assert.Equal((1, error), (run.Code, run.Error));
            Assert.Empty(run.Output);
        }

        if (Ran("print(\"start\")\n", "threads-refused-none", setup) is { } none)
        {
            Assert.Equal((0, ""), (none.Code, none.Error));
            Assert.Equal(["start"], none.Output);
        }
    }
}
