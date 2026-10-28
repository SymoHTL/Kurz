using System.Globalization;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on <c>float</c> and <c>double</c> that no corpus case pins: the text of each value, read against what .NET writes
/// (A4, A11, A14; the owner, 2026-10-28, "As C# prints a float" and "As .NET prints: 1E+17"), and where .NET's text does not read
/// back as the value, the fewest digits that do (A4); a conversion into an integer type at the edges of its range, in a test and in
/// a release build (T26); the comparisons with NaN and a negative zero (T18), the equality of a field that holds them (D3), widening
/// from an unsigned type and from a <c>float</c> (T23), the remainder's sign (T18), a list and an interpolation of them (A4, L6), and
/// a C function over them (R16).
/// </summary>
public sealed class FloatingTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name, bool testBuild = true, params string[] grants)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), grants, toolchain.Runtime, testBuild);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    /// <summary>
    /// A program that shows the value of each of <paramref name="bits"/> through a raw read of its bits, and the bits it shows, in
    /// order: 20000 of a xorshift sequence, the largest value with each exponent, the smallest and the one after it, and then
    /// <paramref name="listed"/>.
    /// </summary>
    private static string Shows(string type, string bitsType, int mantissaBits, int exponents, IReadOnlyList<ulong> listed, List<ulong> bits)
    {
        var mask = bitsType == "uint" ? 0xFFFFFFFFUL : ulong.MaxValue;
        var text = new StringBuilder($$"""
            void Show({{bitsType}} value) {
                mut {{bitsType}} held = value
                mut {{type}} shown = 0
                raw {
                    shown = *Ptr<{{type}}>(&held)
                }

                print(shown)
            }

            mut ulong state = 88172645463325252
            mut int round = 0
            while round < 20000 {
                state = state ^ (state << 13)
                state = state ^ (state >> 7)
                state = state ^ (state << 17)
                Show({{(bitsType == "uint" ? "uint(state & 4294967295)" : "state")}})
                round = round + 1
            }

            mut {{bitsType}} field = 1
            while field < {{exponents}} {
                Show((field << {{mantissaBits}}) - 1)
                Show(field << {{mantissaBits}})
                Show((field << {{mantissaBits}}) + 1)
                field = field + 1
            }

            """);
        var state = 88172645463325252UL;
        for (var round = 0; round < 20000; round++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            bits.Add(state & mask);
        }

        for (var field = 1UL; field < (ulong)exponents; field++)
        {
            bits.AddRange([(field << mantissaBits) - 1, field << mantissaBits, (field << mantissaBits) + 1]);
        }

        foreach (var value in listed)
        {
            text.Append(CultureInfo.InvariantCulture, $"Show({value})\n");
            bits.Add(value);
        }

        return text.ToString();
    }

    /// <summary>The significant digits of a text a <c>double</c> or a <c>float</c> shows: those of its digits that a zero does not lead or end.</summary>
    private static string Digits(string text)
    {
        var mantissa = text.Split('E')[0];
        return mantissa.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Trim('0');
    }

    /// <summary>The fewest significant digits that read back as the value, rounded to the nearest, from .NET's formats of a fixed count of digits.</summary>
    private static string Shortest(Func<int, string> format, Func<string, bool> readsBack)
    {
        for (var count = 1; ; count++)
        {
            var text = format(count);
            if (readsBack(text))
            {
                return Digits(text);
            }
        }
    }

    [Fact]
    public void A_double_shows_what_dotnet_writes_and_where_that_does_not_read_back_the_fewest_digits_that_do()
    {
        // the powers of ten from 1E-323 to 1E+308, each with its neighbours, and the values without digits
        var listed = new List<ulong>();
        for (var exponent = -323; exponent <= 308; exponent++)
        {
            var power = BitConverter.DoubleToUInt64Bits(double.Parse($"1E{exponent}", Invariant));
            listed.AddRange([power - 1, power, power + 1]);
        }

        listed.AddRange([0, 0x8000000000000000, 1, 0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000, 0xFFF8000000000000, 0x7FF0000000000001]);
        var bits = new List<ulong>();
        var run = BuildAndRun(Shows("double", "ulong", 52, 2047, listed, bits), "double-texts", grants: "raw");
        Assert.Equal((0, ""), (run.Code, run.Error));
        Assert.Equal(bits.Count, run.Output.Count);
        var misread = new List<ulong>();
        for (var i = 0; i < bits.Count; i++)
        {
            var value = BitConverter.UInt64BitsToDouble(bits[i]);
            var dotnet = value.ToString(Invariant);
            if (double.IsNaN(value) || BitConverter.DoubleToUInt64Bits(double.Parse(dotnet, Invariant)) == bits[i])
            {
                Assert.True(dotnet == run.Output[i], $"0x{bits[i]:X16}: .NET writes {dotnet}, the program {run.Output[i]}");
                continue;
            }

            // A4: the fewest digits that read back, where .NET's do not
            misread.Add(bits[i]);
            Assert.Equal(bits[i], BitConverter.DoubleToUInt64Bits(double.Parse(run.Output[i], Invariant)));
            Assert.Equal(Shortest(n => value.ToString("E" + (n - 1), Invariant), t => double.Parse(t, Invariant) == value), Digits(run.Output[i]));
        }

        // .NET 10 writes 2^-958 and 2^-25 with 16 digits that read back as other values
        Assert.Equal([0x0410000000000000UL, 0x3E60000000000000UL], misread.Order());
        Assert.Contains("4.1045368012983762E-289", run.Output);
        Assert.Contains("2.9802322387695312E-08", run.Output);
    }

    [Fact]
    public void A_float_shows_what_dotnet_writes()
    {
        // the powers of ten from 1E-45 to 1E+38, each with its neighbours, and the values without digits
        var listed = new List<ulong>();
        for (var exponent = -45; exponent <= 38; exponent++)
        {
            var power = BitConverter.SingleToUInt32Bits(float.Parse($"1E{exponent}", Invariant));
            listed.AddRange([power - 1UL, power, power + 1UL]);
        }

        listed.AddRange([0, 0x80000000, 1, 0x7F7FFFFF, 0x7F800000, 0xFF800000, 0x7FC00000, 0xFFC00000, 0x7F800001]);
        var bits = new List<ulong>();
        var run = BuildAndRun(Shows("float", "uint", 23, 255, listed, bits), "float-texts", grants: "raw");
        Assert.Equal((0, ""), (run.Code, run.Error));
        Assert.Equal(bits.Count, run.Output.Count);
        for (var i = 0; i < bits.Count; i++)
        {
            var value = BitConverter.UInt32BitsToSingle((uint)bits[i]);
            var dotnet = value.ToString(Invariant);
            Assert.True(float.IsNaN(value) || BitConverter.SingleToUInt32Bits(float.Parse(dotnet, Invariant)) == (uint)bits[i], $"0x{bits[i]:X8}: .NET's {dotnet} does not read back");
            Assert.True(dotnet == run.Output[i], $"0x{bits[i]:X8}: .NET writes {dotnet}, the program {run.Output[i]}");
        }
    }

    private const string Conversions = """
        int I(double x) => int(x)
        uint U(double x) => uint(x)
        long L(double x) => long(x)
        ulong Q(double x) => ulong(x)
        byte B(double x) => byte(x)
        sbyte S(float x) => sbyte(x)
        int F(float x) => int(x)
        double Zero() => 0.0

        """;

    [Fact]
    public void A_conversion_into_an_integer_type_keeps_a_value_whose_fraction_alone_lies_beyond_the_range()
    {
        // T26: the fraction is dropped toward zero, so a value just beyond either end of a range fits; the largest double below 2^63
        // and 2^64, and the smallest `int` as a `float`, which holds no value between it and the one below
        var text = Conversions + """
            print(I(2147483647.9))
            print(I(-2147483648.9))
            print(U(-0.9))
            print(U(4294967295.5))
            print(L(-9223372036854775808.0))
            print(L(9223372036854774784.0))
            print(Q(18446744073709549568.0))
            print(B(255.99))
            print(S(-128.5f))
            print(F(-2147483648f))
            print(F(2147483520f))
            """;
        Ran(BuildAndRun(text, "conversion-edges"), 0, ["2147483647", "-2147483648", "0", "4294967295", "-9223372036854775808", "9223372036854774784", "18446744073709549568", "255", "-128", "-2147483648", "2147483520"], "");
    }

    [Theory]
    [InlineData("I(2147483648.0)", 1, "2147483647")]
    [InlineData("I(-2147483649.0)", 1, "-2147483648")]
    [InlineData("I(Zero() / Zero())", 1, "0")]
    [InlineData("I(1.0 / Zero())", 1, "2147483647")]
    [InlineData("U(-1.0)", 2, "0")]
    [InlineData("L(9223372036854775808.0)", 3, "9223372036854775807")]
    [InlineData("Q(18446744073709551616.0)", 4, "18446744073709551615")]
    [InlineData("B(256.0)", 5, "255")]
    [InlineData("S(-129.0f)", 6, "-128")]
    [InlineData("F(2147483648f)", 7, "2147483647")]
    public void A_conversion_that_loses_the_value_raises_overflow_in_a_test_build_and_saturates_in_a_release_build(string call, int line, string saturated)
    {
        // T26: beyond the range, an infinity and NaN lose the value, raised at the conversion's line; the release build gives the
        // largest or the smallest value, and 0 for NaN
        var text = Conversions + $"print({call})\n";
        var name = "conversion-" + new string(call.Select(c => c == '-' ? 'M' : c).Where(char.IsLetterOrDigit).ToArray()); // `I(2147483648.0)` and `I(1.0 / Zero())` agree in all else, a sign kept as `M`
        Ran(BuildAndRun(text, name + "-test"), 1, [], $"error: overflow at program.kz:{line}\n");
        Ran(BuildAndRun(text, name + "-release", testBuild: false), 0, [saturated], "");
    }

    [Fact]
    public void Comparisons_equality_widening_the_remainder_lists_and_holes_are_those_of_csharp()
    {
        var text = """
            data Point(double V)

            double z = 0.0
            nan = z / z
            double minus = -z
            int one = 1
            float half = 0.5f
            print(nan == nan)
            print(nan != nan)
            print(nan < 1.0)
            print(nan >= 1.0)
            print(z == minus)
            print(one < 1.5)
            print(half == 0.5)
            print(Point(nan) == Point(nan))
            print(Point(z) == Point(minus))
            double a = -7.5
            print(a % 2.0)
            uint u = 4294967295
            double w = u
            print(w)
            ulong big = 18446744073709551615
            double v = big
            print(v)
            float tenth = 0.1f
            double t = tenth
            print(t)
            print(float(t))
            mut xs = List<double>()
            xs.Add(1.5)
            xs.Add(nan)
            xs.Add(minus)
            print(xs)
            print("{minus} and {half}")
            """;
        Ran(BuildAndRun(text, "floating-operations"), 0,
            ["false", "true", "false", "false", "true", "true", "true", "true", "true", "-1.5", "4294967295", "1.8446744073709552E+19", "0.10000000149011612", "0.1", "[1.5, NaN, -0]", "-0 and 0.5"], "");
    }

    [Fact]
    public void A_C_function_takes_and_gives_a_double_and_a_float()
    {
        // functions both C libraries export: Windows' C runtime on x64 has `fabsf` inline in its headers and in no library, and
        // the GNU C library has `modf` and `modff` in libc itself
        var text = """
            extern "C" {
                double fabs(double x)
                double modf(double x, double* whole)
                float modff(float x, float* whole)
            }

            raw {
                print(fabs(-2.5))
                mut double w = 0
                print(modf(2.75, &w))
                print(w)
                mut float v = 0
                print(modff(-1.5f, &v))
                print(v)
            }
            """;
        Ran(BuildAndRun(text, "c-floating", grants: ["raw", "native"]), 0, ["2.5", "0.75", "2", "-0.5", "-1"], "");
    }
}
