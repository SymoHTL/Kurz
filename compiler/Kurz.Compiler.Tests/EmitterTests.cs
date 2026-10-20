using System.Globalization;
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

    // ---- the floating-point, decimal and duration types (T24 to T26, T29, A8, A11, A12, A14) ----

    [Fact]
    public void The_text_of_a_float_takes_the_exponent_form_at_the_thresholds_of_a_double()
    {
        // A14 names the thresholds for a double, from 1E+15 and below 1E-04; this directory reads them for a float too
        var (code, output, error) = Run("float-thresholds", "float F(float x) => x\nprint(F(1e9f))\nprint(F(1e15f))\nprint(F(0.0001f))\nprint(F(0.00001f))\n", release: false);
        Assert.Equal(["1000000000", "1E+15", "0.0001", "1E-05"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void The_text_of_a_double_is_the_shortest_that_reads_back_as_the_value()
    {
        // 2^-25: the sixteen digits 2.980232238769531E-08 read back as another value, so the text has seventeen; below 1E-04 the exponent form (A14)
        var (code, output, error) = Run("double-shortest", "double D(double x) => x\nprint(D(1.0) / 33554432.0)\nprint(D(0.1) + 0.2)\nprint(D(0.0001))\nprint(D(0.00001))\nprint(D(123456.789))\n", release: false);
        Assert.Equal(["2.9802322387695312E-08", "0.30000000000000004", "0.0001", "1E-05", "123456.789"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_decimal_that_does_not_fit_is_overflow_in_both_builds(bool release)
    {
        var (code, output, error) = Run("decimal-overflow", "decimal M(decimal x) => x\nprint(M(79228162514264337593543950335m) + 1m)\n", release);
        Assert.Empty(output);
        Assert.Equal((1, "error: overflow at line 2\n"), (code, error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_floating_point_value_beyond_a_decimal_is_overflow_in_both_builds(bool release)
    {
        var (code, output, error) = Run("decimal-from-double", "double D(double x) => x\nprint(decimal(D(0.5)))\nprint(decimal(D(1e30)))\n", release);
        Assert.Equal(["0.5"], output);
        Assert.Equal((1, "error: overflow at line 3\n"), (code, error));
    }

    [Fact]
    public void A_decimal_keeps_its_scale_through_its_text_and_its_arithmetic()
    {
        // A8: the digits a decimal holds, as System.Decimal keeps them through each operation
        var program = "decimal M(decimal x) => x\nprint(M(1.50m))\nprint(M(1.50m) + 0.5m)\nprint(M(1.5m) * 2m)\nprint(M(3.0m) - 1m)\nprint(M(10m) / 4m)\nprint(M(7.5m) % 2m)\nprint(-M(1.5m))\n";
        var (code, output, error) = Run("decimal-scale", program, release: false);
        Assert.Equal(["1.50", "2.00", "3.0", "2.0", "2.5", "1.5", "-1.5"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_floating_point_field_holds_zero_equal_to_minus_zero_and_NaN_equal_to_itself()
    {
        // D3: the fields compare as C#'s record fields do, where `==` on the values follows IEEE 754 (T25)
        var program = "data Point(double X)\ndouble D(double x) => x\nzero = D(0.0)\nnan = zero / zero\nprint(D(0.0) == -D(0.0))\nprint(Point(0.0) == Point(-0.0))\nprint(nan == nan)\nprint(nan != nan)\nprint(Point(nan) == Point(nan))\n";
        var (code, output, error) = Run("double-equality", program, release: false);
        Assert.Equal(["true", "true", "false", "true", "true"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_comparison_meets_in_the_wider_of_its_operands()
    {
        var program = "double D(double x) => x\ndecimal M(decimal x) => x\nduration T(duration d) => d\nlongduration W(longduration d) => d\nprint(D(2.5) > 2)\nprint(M(2.5m) < 3)\nprint(T(1h) > 59min)\nprint(T(1h) == 3600s)\nprint(T(1h) < W(100000days) + W(100000days))\n";
        var (code, output, error) = Run("wider-comparison", program, release: false);
        Assert.Equal(["true", "true", "true", "true", "true"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_conversion_to_an_integer_type_drops_the_fraction()
    {
        // T26: toward zero, from a floating-point value and from a decimal
        var program = "double D(double x) => x\ndecimal M(decimal x) => x\nprint(int(D(2.5)))\nprint(int(D(-2.5)))\nprint(int(M(2.5m)))\nprint(int(M(-2.5m)))\nprint(byte(D(255.9)))\n";
        var (code, output, error) = Run("drop-fraction", program, release: false);
        Assert.Equal(["2", "-2", "2", "-2", "255"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Theory]
    [InlineData("double D(double x) => x\nprint(byte(D(300.0)))\n", 2)]
    [InlineData("double D(double x) => x\nprint(byte(D(-1.0)))\n", 2)]
    [InlineData("double D(double x) => x\nzero = D(0.0)\nprint(int(zero / zero))\n", 3)]
    [InlineData("decimal M(decimal x) => x\nprint(long(M(100000000000000000000m)))\n", 2)]
    [InlineData("decimal M(decimal x) => x\nprint(byte(M(300m)))\n", 2)]
    [InlineData("decimal M(decimal x) => x\nprint(ulong(M(-1m)))\n", 2)]
    public void A_value_an_integer_type_does_not_hold_is_overflow_in_the_test_build(string program, int line)
    {
        var (code, output, error) = Run("narrow-" + line.ToString(CultureInfo.InvariantCulture) + "-" + program.Length.ToString(CultureInfo.InvariantCulture), program, release: false);
        Assert.Empty(output);
        Assert.Equal((1, $"error: overflow at line {line.ToString(CultureInfo.InvariantCulture)}\n"), (code, error));
    }

    [Theory]
    [InlineData("double D(double x) => x\nprint(byte(D(300.0)))\n", "255")]
    [InlineData("double D(double x) => x\nprint(byte(D(-1.0)))\n", "0")]
    [InlineData("decimal M(decimal x) => x\nprint(long(M(100000000000000000000m)))\n", "9223372036854775807")]
    [InlineData("decimal M(decimal x) => x\nprint(long(M(-100000000000000000000m)))\n", "-9223372036854775808")]
    [InlineData("decimal M(decimal x) => x\nprint(byte(M(300m)))\n", "255")]
    [InlineData("decimal M(decimal x) => x\nprint(ulong(M(-1m)))\n", "0")]
    [InlineData("decimal M(decimal x) => x\nprint(sbyte(M(-200m)))\n", "-128")]
    public void A_value_an_integer_type_does_not_hold_is_held_to_its_end_in_the_release_build(string program, string expected)
    {
        var (code, output, error) = Run("saturate-" + expected.Replace("-", "m", StringComparison.Ordinal) + "-" + program.Length.ToString(CultureInfo.InvariantCulture), program, release: true);
        Assert.Equal([expected], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_number_converted_to_a_floating_point_type_or_decimal_is_rounded_to_what_the_type_holds()
    {
        var program = "double D(double x) => x\ndecimal M(decimal x) => x\nprint(float(D(0.1)))\nprint(decimal(D(0.1)))\nprint(double(M(0.1m)))\nprint(float(M(0.1m)))\nprint(float(D(1e300)))\n";
        var (code, output, error) = Run("rounded", program, release: false);
        Assert.Equal(["0.1", "0.1", "0.1", "0.1", "Infinity"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_duration_sum_that_does_not_fit_is_overflow_in_the_test_build()
    {
        var (code, output, error) = Run("duration-sum", "duration T(duration d) => d\nprint(T(100000days) + 100000days)\n", release: false);
        Assert.Empty(output);
        Assert.Equal((1, "error: overflow at line 2\n"), (code, error));
    }

    [Fact]
    public void A_duration_sum_that_does_not_fit_wraps_in_the_release_build()
    {
        // T29 with T5: the count wraps as an integer does
        var (code, output, error) = Run("duration-sum", "duration T(duration d) => d\nprint(T(100000days) + 100000days)\n", release: true);
        Assert.Equal(["-13503days 23h 34min 33s 709ms 551us 616ns"], output);
        Assert.Equal((0, ""), (code, error));
    }

    [Fact]
    public void A_longduration_converted_to_a_duration_wraps_in_the_release_build()
    {
        // the test build of the same program is corpus/types/longduration-wide.kz
        var (code, output, error) = Run("longduration-wide", "longduration a = 100000days\nlongduration l = a + a\nprint(l)\nprint(duration(l))\n", release: true);
        Assert.Equal(["200000days", "-13503days 23h 34min 33s 709ms 551us 616ns"], output);
        Assert.Equal((0, ""), (code, error));
    }
}
