using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Programs built by the emitter and clang and run, for the readings the corpus cannot pin: a
/// corpus case stands on the rules it cites, and these readings stand on none (the section
/// "Readings the reference leaves open to the emitter and the command" of <c>compiler/README.md</c>).
/// </summary>
public sealed class EmitterTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const string MinimumQuotient = "int Quotient(int a, int b) { return a / b }\nprint(Quotient(-2147483647 - 1, -1))\n";

    private const string MinimumRemainder = "int Remainder(int a, int b) { return a % b }\nprint(Remainder(-2147483647 - 1, -1))\n";

    private (int Code, IReadOnlyList<string> Output, string Error) Run(string name, string program, bool release)
    {
        var compilation = Builder.Compile(SourceText.FromString(name, program), testBuild: !release);
        Assert.True(compilation.Accepted, $"{name}: {string.Join("; ", compilation.Diagnostics.Sorted)}");
        return Toolchain.Run(toolchain.Build(compilation.Ir!, name + (release ? "-release" : "-test")));
    }

    [Fact]
    public void The_smallest_value_divided_by_minus_one_is_overflow_in_the_test_build()
    {
        var (code, output, error) = Run("min-quotient", MinimumQuotient, release: false);
        Assert.Empty(output);
        Assert.Equal((1, "error: overflow at line 1\n"), (code, error));
    }

    [Fact]
    public void The_smallest_value_divided_by_minus_one_is_the_smallest_value_in_the_release_build()
    {
        var (code, output, error) = Run("min-quotient", MinimumQuotient, release: true);
        Assert.Equal(["-2147483648"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_remainder_of_the_smallest_value_by_minus_one_is_zero_in_both_builds(bool release)
    {
        var (code, output, error) = Run("min-remainder", MinimumRemainder, release);
        Assert.Equal(["0"], output);
        Assert.Equal((0, ""), (code, error));
    }
}
