using System.Globalization;
using System.Numerics;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on <c>decimal</c> that no corpus case pins, read against C#'s System.Decimal: the sum, the difference, the product,
/// the quotient and the remainder of 10000 pairs of a xorshift sequence and of the pairs of 34 values at the edges of the range, the
/// scales and the 32-bit and 64-bit words, digit for digit and scale for scale (T18, section 4), with a zero they compute unsigned
/// (the owner, 2026-10-28, "None, as rust_decimal") and the exact remainder where System.Decimal raises for one that fits ("Exact
/// remainder"); the comparisons by value (T18); the text with the sign of a negative zero (A8;
/// "Sign, as Rust"); a <c>decimal</c> as a <c>double</c> and a <c>float</c> as C# computes it ("As C# computes it") and as a
/// <c>long</c>; a <c>double</c> and a <c>float</c> as a <c>decimal</c>, their exact binary value rounded ("Exact binary value");
/// <c>overflow</c> and <c>divide-by-zero</c> in every build ("Raise in every build", T25); the conversions into an integer type
/// (T26); and a field, a case of a union, a list and an interpolation of them.
/// </summary>
public sealed class DecimalTests(Toolchain toolchain) : IClassFixture<Toolchain>
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

    /// <summary>The text a <c>decimal</c> shows: .NET's digits, and <c>-</c> before a zero whose sign is set (A8, "Sign, as Rust").</summary>
    private static string Text(decimal d) => (d == 0m && decimal.IsNegative(d) ? "-" : "") + d.ToString(Invariant);

    /// <summary>A result of <c>+ - * / %</c>: System.Decimal's, a zero without its sign ("None, as rust_decimal").</summary>
    private static decimal Computed(decimal d) => d == 0m && decimal.IsNegative(d) ? decimal.Negate(d) : d;

    private static decimal Make(ulong low, ulong high) =>
        new((int)(uint)low, (int)(uint)(low >> 32), (int)(uint)high, high >> 63 == 1, (byte)((high >> 48) & 255));

    private static (ulong Low, ulong High) Words(decimal d)
    {
        var bits = decimal.GetBits(d);
        return ((uint)bits[0] | ((ulong)(uint)bits[1] << 32), (uint)bits[2] | ((ulong)(uint)bits[3] << 32));
    }

    private static ulong Step(ulong s)
    {
        s ^= s << 13;
        s ^= s >> 7;
        return s ^ (s << 17);
    }

    /// <summary>The operand the program makes of three words of the sequence: a 96-bit integer of 0 to 96 bits, a scale and a sign.</summary>
    private static decimal Operand(ulong low, ulong high, ulong control)
    {
        var bits = (int)(control % 97);
        var h = high & 0xFFFFFFFF;
        var l = low;
        if (bits < 64)
        {
            l &= (1UL << bits) - 1;
            h = 0;
        }
        else
        {
            h &= (1UL << (bits - 64)) - 1;
        }

        var flags = ((control >> 8) % 29) << 48;
        if (((control >> 16) & 1) == 1)
        {
            flags |= 1UL << 63;
        }

        return Make(l, h | flags);
    }

    /// <summary>Which operations the program shows for a pair of the sequence, from bounds of their magnitudes that keep each one inside the range.</summary>
    private static int Flags(ulong ca, ulong cb)
    {
        int na = (int)(ca % 97), nb = (int)(cb % 97), sa = (int)((ca >> 8) % 29), sb = (int)((cb >> 8) % 29);
        var flags = 0;
        if (na <= 95 && nb <= 95)
        {
            flags |= 1;
        }

        if (na + nb < 95 + 3 * (sa + sb))
        {
            flags |= 2;
        }

        if (sb >= sa ? na + 4 * (sb - sa) < 95 : na - 3 * (sa - sb) < 95)
        {
            flags |= 4;
        }

        if (na < 63 + 3 * sa)
        {
            flags |= 8;
        }

        return flags;
    }

    private const string Program = """
        decimal Make(ulong low, ulong high) {
            mut decimal d = 0m
            raw {
                ulong* p = Ptr<ulong>(&d)
                p[0] = low
                p[1] = high
            }

            return d
        }

        ulong Step(ulong s) {
            ulong a = s ^ (s << 13)
            ulong b = a ^ (a >> 7)
            return b ^ (b << 17)
        }

        decimal Operand(ulong low, ulong high, ulong control) {
            int bits = int(control % 97)
            mut ulong l = low
            mut ulong h = high & 4294967295
            if bits < 64 {
                l = l & ((ulong(1) << bits) - 1)
                h = 0
            } else {
                h = h & ((ulong(1) << (bits - 64)) - 1)
            }

            mut ulong shown = ((control >> 8) % 29) << 48
            if ((control >> 16) & 1) == 1 {
                shown = shown | 9223372036854775808
            }

            return Make(l, h | shown)
        }

        int Flags(ulong ca, ulong cb) {
            int na = int(ca % 97)
            int nb = int(cb % 97)
            int sa = int((ca >> 8) % 29)
            int sb = int((cb >> 8) % 29)
            mut int shown = 0
            if na <= 95 && nb <= 95 {
                shown = shown | 1
            }

            if na + nb < 95 + 3 * (sa + sb) {
                shown = shown | 2
            }

            if (sb >= sa && na + 4 * (sb - sa) < 95) || (sb < sa && na - 3 * (sa - sb) < 95) {
                shown = shown | 4
            }

            if na < 63 + 3 * sa {
                shown = shown | 8
            }

            return shown
        }

        void Pair(decimal a, decimal b, int shown) {
            print(a)
            if (shown & 1) != 0 {
                print(a + b)
                print(a - b)
            }

            if (shown & 2) != 0 {
                print(a * b)
            }

            if (shown & 4) != 0 && b != 0m {
                print(a / b)
            }

            if b != 0m {
                print(a % b)
            }

            print(a < b)
            print(a <= b)
            print(a == b)
            print(double(a))
            print(float(a))
            if (shown & 8) != 0 {
                print(long(a))
            }
        }

        mut ulong state = 88172645463325252
        mut int round = 0
        while round < 10000 {
            state = Step(state)
            ulong al = state
            state = Step(state)
            ulong ah = state
            state = Step(state)
            ulong ac = state
            state = Step(state)
            ulong bl = state
            state = Step(state)
            ulong bh = state
            state = Step(state)
            ulong bc = state
            Pair(Operand(al, ah, ac), Operand(bl, bh, bc), Flags(ac, bc))
            round = round + 1
        }

        """;

    /// <summary>
    /// The remainder at the larger scale with the dividend's sign: System.Decimal's, and the exact one where System.Decimal raises
    /// <c>overflow</c> for a remainder that fits (the owner, 2026-10-28, "Exact remainder"), which <paramref name="exact"/> counts.
    /// </summary>
    private static decimal Remainder(decimal a, decimal b, ref int exact)
    {
        try
        {
            return a % b;
        }
        catch (OverflowException)
        {
            exact++;
            var scale = Math.Max(a.Scale, b.Scale);
            var r = BigInteger.Remainder(Magnitude(a) * BigInteger.Pow(10, scale - a.Scale), Magnitude(b) * BigInteger.Pow(10, scale - b.Scale));
            var mask = new BigInteger(uint.MaxValue);
            return new decimal((int)(uint)(r & mask), (int)(uint)((r >> 32) & mask), (int)(uint)(r >> 64), decimal.IsNegative(a), (byte)scale);
        }
    }

    private static BigInteger Magnitude(decimal d)
    {
        var (low, high) = Words(d);
        return (new BigInteger(high & 0xFFFFFFFF) << 64) | low;
    }

    /// <summary>What <c>Pair</c> shows for two operands and the operations its flags name.</summary>
    private static void Expect(decimal a, decimal b, int flags, List<string> lines, ref int exact)
    {
        lines.Add(Text(a));
        if ((flags & 1) != 0)
        {
            lines.Add(Text(Computed(a + b)));
            lines.Add(Text(Computed(a - b)));
        }

        if ((flags & 2) != 0)
        {
            lines.Add(Text(Computed(a * b)));
        }

        if ((flags & 4) != 0 && b != 0m)
        {
            lines.Add(Text(Computed(a / b)));
        }

        if (b != 0m)
        {
            lines.Add(Text(Computed(Remainder(a, b, ref exact))));
        }

        lines.Add(a < b ? "true" : "false");
        lines.Add(a <= b ? "true" : "false");
        lines.Add(a == b ? "true" : "false");
        lines.Add(((double)a).ToString(Invariant));
        lines.Add(((float)a).ToString(Invariant));
        if ((flags & 8) != 0)
        {
            lines.Add(((long)a).ToString(Invariant));
        }
    }

    /// <summary>The flags of a listed pair: each operation that System.Decimal computes inside the range.</summary>
    private static int Fits(decimal a, decimal b)
    {
        static bool Inside(Func<decimal> f)
        {
            try
            {
                f();
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return (Inside(() => a + b) && Inside(() => a - b) ? 1 : 0) | (Inside(() => a * b) ? 2 : 0) | (b == 0m || Inside(() => a / b) ? 4 : 0)
            | (Inside(() => (long)a) ? 8 : 0);
    }

    /// <summary>
    /// The edges: the largest and the smallest value, 2^96 - 1 at the scale 28, ones and zeros at several scales, a negative zero,
    /// halves, the smallest step, the 32-bit and 64-bit boundaries of the integer, numbers of 28 digits, and the pairs that take
    /// System.Decimal's own paths: two numbers of 32 bits whose scales add up to more than 47, and a zero beside a number of more than
    /// 32 bits.
    /// </summary>
    private static readonly decimal[] Edges =
    [
        decimal.MaxValue, decimal.MinValue, new(-1, -1, -1, false, 28), new(-1, -1, -1, true, 1), 1m, -1m, 0m, 0.0m, 0.00m,
        decimal.Negate(0.000m), 0.5m, 1.5m, 2.5m, -2.5m, 0.0000000000000000000000000001m, 0.0000000000000000000000000005m,
        new(1, 0, 0, false, 25), new(3, 0, 0, false, 23), 4294967295m, 4294967296m, 18446744073709551615m, 18446744073709551616m,
        12345678901.5m, 12345678901234567890.123456789m, 0.3333333333333333333333333333m, 0.9999999999999999999999999999m,
        1.0000000000000000000000000001m, 3m, 7m, 10m, 1000000000m, 0.1m, -0.01m, 7922816251426433759354395033.5m,
    ];

    [Fact]
    public void Arithmetic_comparisons_and_conversions_are_those_of_System_Decimal()
    {
        var text = new StringBuilder(Program);
        var lines = new List<string>();
        var exact = 0;
        var state = 88172645463325252UL;
        for (var round = 0; round < 10000; round++)
        {
            var words = new ulong[6];
            for (var i = 0; i < 6; i++)
            {
                state = Step(state);
                words[i] = state;
            }

            Expect(Operand(words[0], words[1], words[2]), Operand(words[3], words[4], words[5]), Flags(words[2], words[5]), lines, ref exact);
        }

        foreach (var a in Edges)
        {
            foreach (var b in Edges)
            {
                var (al, ah) = Words(a);
                var (bl, bh) = Words(b);
                var flags = Fits(a, b);
                text.Append(Invariant, $"Pair(Make({al}, {ah}), Make({bl}, {bh}), {flags})\n");
                Expect(a, b, flags, lines, ref exact);
            }
        }

        Assert.True(exact > 0, "no pair took the remainder System.Decimal raises for");

        var run = BuildAndRun(text.ToString(), "decimal-arithmetic", grants: "raw");
        Assert.Equal((0, ""), (run.Code, run.Error));
        Assert.Equal(lines.Count, run.Output.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.True(lines[i] == run.Output[i], $"line {i + 1}: System.Decimal gives {lines[i]}, the program {run.Output[i]}");
        }
    }

    /// <summary>
    /// The decimal of a <c>double</c>'s exact binary value: its digits rounded half to even at the largest scale up to 28 at which they
    /// fit 96 bits, its zeros at the end removed down to the scale 0, and a zero without a sign ("Exact binary value").
    /// </summary>
    private static string ExactDecimal(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        var power = Math.Max(exponent, 1) - 1075;
        var numerator = power >= 0 ? mantissa << power : mantissa;
        var denominator = power >= 0 ? BigInteger.One : BigInteger.One << -power;
        for (var scale = 28; ; scale--)
        {
            var digits = BigInteger.DivRem(numerator * BigInteger.Pow(10, scale), denominator, out var remainder);
            if (remainder * 2 > denominator || (remainder * 2 == denominator && !digits.IsEven))
            {
                digits += 1;
            }

            if (digits >= BigInteger.One << 96)
            {
                continue;
            }

            while (scale > 0 && digits % 10 == 0)
            {
                digits /= 10;
                scale--;
            }

            var text = digits.ToString(Invariant).PadLeft(scale + 1, '0');
            if (scale > 0)
            {
                text = text[..^scale] + "." + text[^scale..];
            }

            return (bits < 0 && !digits.IsZero ? "-" : "") + text;
        }
    }

    [Fact]
    public void A_double_and_a_float_become_their_exact_binary_value_rounded()
    {
        // 20000 doubles from 2^-123, where the value rounds to zero, to below 2^96, and as many floats from 2^-67; the smallest
        // subnormals, a negative zero, and values around the ties and the bounds
        var text = new StringBuilder("""
            double Real(ulong bits) {
                mut ulong held = bits
                mut double value = 0
                raw {
                    value = *Ptr<double>(&held)
                }

                return value
            }

            float Single(uint bits) {
                mut uint held = bits
                mut float value = 0
                raw {
                    value = *Ptr<float>(&held)
                }

                return value
            }

            ulong Step(ulong s) {
                ulong a = s ^ (s << 13)
                ulong b = a ^ (a >> 7)
                return b ^ (b << 17)
            }

            mut ulong state = 88172645463325252
            mut int round = 0
            while round < 20000 {
                state = Step(state)
                ulong exponent = 900 + (state >> 52) % 219
                print(decimal(Real((state & 0x800FFFFFFFFFFFFF) | (exponent << 52))))
                uint single = uint((state >> 7) & 0x807FFFFF) | (uint(60 + state % 163) << 23)
                print(decimal(Single(single)))
                round = round + 1
            }

            """);
        var expected = new List<string>();
        var state = 88172645463325252UL;
        for (var round = 0; round < 20000; round++)
        {
            state = Step(state);
            var exponent = 900 + (state >> 52) % 219;
            expected.Add(ExactDecimal(BitConverter.UInt64BitsToDouble((state & 0x800FFFFFFFFFFFFF) | (exponent << 52))));
            var single = (uint)((state >> 7) & 0x807FFFFF) | ((uint)(60 + state % 163) << 23);
            expected.Add(ExactDecimal(BitConverter.UInt32BitsToSingle(single)));
        }

        double[] listed = [0.1, 1.0 / 3, 0.5, 2.0, -0.0, 0.0, double.Epsilon, -double.Epsilon, 1e-28, 5e-29, 4.9e-29, 1.5e-28, 2.5e-28,
            123.456, 79228162514264328797450928128.0, -79228162514264328797450928128.0, 9007199254740993.0, 1e20, 0.000123];
        foreach (var value in listed)
        {
            text.Append(Invariant, $"print(decimal(Real({BitConverter.DoubleToUInt64Bits(value)})))\n");
            expected.Add(ExactDecimal(value));
        }

        var run = BuildAndRun(text.ToString(), "decimal-from-floating", grants: "raw");
        Assert.Equal((0, ""), (run.Code, run.Error));
        Assert.Equal(expected.Count, run.Output.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == run.Output[i], $"line {i + 1}: the exact value rounded is {expected[i]}, the program shows {run.Output[i]}");
        }

        Assert.Equal("0.1000000000000000055511151231", run.Output[40000]);
        Assert.Equal("123.45600000000000306954461848", run.Output[40013]);
    }

    /// <summary>A file name of a call's letters and digits, a `-` as `M`.</summary>
    private static string Name(string call) => new(call.Select(c => c == '-' ? 'M' : c).Where(char.IsLetterOrDigit).ToArray());

    private const string Operations = """
        decimal Add(decimal a, decimal b) => a + b
        decimal Sub(decimal a, decimal b) => a - b
        decimal Mul(decimal a, decimal b) => a * b
        decimal Div(decimal a, decimal b) => a / b
        decimal Rem(decimal a, decimal b) => a % b
        decimal Neg(decimal a) => -a
        decimal FromDouble(double x) => decimal(x)
        decimal FromFloat(float x) => decimal(x)
        double Zero() => 0.0
        int I(decimal d) => int(d)
        uint U(decimal d) => uint(d)
        long L(decimal d) => long(d)
        ulong Q(decimal d) => ulong(d)
        byte B(decimal d) => byte(d)

        """;

    [Theory]
    [InlineData("Add(79228162514264337593543950335m, 1m)", "overflow", 1)]
    [InlineData("Add(79228162514264337593543950335m, 0.5m)", "overflow", 1)]
    [InlineData("Sub(-79228162514264337593543950335m, 1m)", "overflow", 2)]
    [InlineData("Mul(79228162514264337593543950335m, 2m)", "overflow", 3)]
    [InlineData("Div(79228162514264337593543950335m, 0.5m)", "overflow", 4)]
    [InlineData("FromDouble(1e30)", "overflow", 7)]
    [InlineData("FromDouble(79228162514264337593543950336.0)", "overflow", 7)]
    [InlineData("FromDouble(Zero() / Zero())", "overflow", 7)]
    [InlineData("FromFloat(float(1.0 / Zero()))", "overflow", 8)]
    [InlineData("Div(1m, 0m)", "divide-by-zero", 4)]
    [InlineData("Rem(1m, Neg(0.00m))", "divide-by-zero", 5)]
    public void A_result_beyond_the_range_and_a_division_by_zero_raise_in_every_build(string call, string id, int line)
    {
        // the owner, 2026-10-28, "Raise in every build"; T25: the exception at the line of the operation, a release build as well
        var text = Operations + $"print({call})\n";
        var name = "decimal-raise-" + Name(call);
        Ran(BuildAndRun(text, name + "-test"), 1, [], $"error: {id} at program.kz:{line}\n");
        Ran(BuildAndRun(text, name + "-release", testBuild: false), 1, [], $"error: {id} at program.kz:{line}\n");
    }

    [Theory]
    [InlineData("I(100000000000000000000m)", 10, "2147483647")]
    [InlineData("I(-2147483649m)", 10, "-2147483648")]
    [InlineData("U(-1.5m)", 11, "0")]
    [InlineData("L(9223372036854775808m)", 12, "9223372036854775807")]
    [InlineData("Q(18446744073709551616m)", 13, "18446744073709551615")]
    [InlineData("B(256m)", 14, "255")]
    public void A_conversion_into_an_integer_type_that_loses_the_value_raises_in_a_test_build_and_saturates_in_a_release_build(string call, int line, string saturated)
    {
        // T26: the fraction dropped toward zero is no loss; beyond the range the test build raises and the release build saturates
        var text = Operations + $"print({call})\n";
        var name = "decimal-integer-" + Name(call);
        Ran(BuildAndRun(text, name + "-test"), 1, [], $"error: overflow at program.kz:{line}\n");
        Ran(BuildAndRun(text, name + "-release", testBuild: false), 0, [saturated], "");
    }

    [Fact]
    public void A_conversion_into_an_integer_type_keeps_a_value_whose_fraction_alone_lies_beyond_the_range()
    {
        var text = Operations + """
            print(I(2147483647.99m))
            print(I(-2147483648.99m))
            print(U(-0.9m))
            print(L(9223372036854775807.9m))
            print(L(-9223372036854775808.9m))
            print(Q(18446744073709551615.5m))
            print(B(255.99m))
            print(I(Neg(0.0m)))
            """;
        Ran(BuildAndRun(text, "decimal-integer-edges"), 0, ["2147483647", "-2147483648", "0", "9223372036854775807", "-9223372036854775808", "18446744073709551615", "255", "0"], "");
    }

    [Fact]
    public void Only_a_negated_zero_has_a_sign_and_folded_constants_agree_with_the_run_time()
    {
        // the owner, 2026-10-28, "None, as rust_decimal" and "Sign, as Rust": .NET sets the sign of the first four zeros computed here,
        // and of the folded ones beside them
        var text = Operations + """
            print(Sub(1.50m, 1.5m))
            print(Add(-1m, 1m))
            print(Mul(0m, -1m))
            print(Rem(-3m, 1m))
            print(Div(Neg(0.00m), 1m))
            print(Rem(Neg(0.00m), 1m))
            print(Mul(Neg(0.0000000000000000000000000001m), 0.1m))
            print(1.50m - 1.5m)
            print(0m * -1m)
            print(Neg(0.0m))
            print(-(0.0m))
            print(Neg(Sub(1.5m, 1.5m)))
            print(Neg(0.0m) == 0m)
            print(double(Neg(0.0m)))
            print(FromDouble(-0.0))
            print(FromDouble(-1e-30))
            print(decimal(-0.0))
            print(decimal(0.0))
            print(decimal(0.1))
            print(FromDouble(0.1))
            """;
        Ran(BuildAndRun(text, "decimal-zero-sign"), 0,
            ["0.00", "0", "0", "0", "0.00", "0.00", "0.0000000000000000000000000000", "0.00", "0", "-0.0", "-0.0", "-0.0", "true", "-0", "0", "0", "0", "0",
                "0.1000000000000000055511151231", "0.1000000000000000055511151231"], "");
    }

    [Fact]
    public void Widening_fields_unions_lists_and_interpolations_hold_a_decimal()
    {
        var text = Operations + """
            data Price(decimal Amount)

            int low = -2147483648
            long least = -9223372036854775808
            ulong most = 18446744073709551615
            byte small = 255
            decimal w = low
            print(w)
            decimal wide = least
            print(wide)
            decimal y = most
            print(y)
            print(Add(small, 1))
            print(Add(1.5m, 2))
            print(Price(Mul(1.5m, 1m)) == Price(1.50m))
            print(Price(1.5m) == Price(1.6m))
            print(Price(2.50m))
            int | decimal u = Mul(2m, 1.25m)
            print(u)
            mut xs = List<decimal>()
            xs.Add(1.50m)
            xs.Add(Neg(0.0m))
            xs.Add(low)
            print(xs)
            decimal half = Div(1m, 2m)
            print("{half} and {Neg(half)}")
            print(Mul(2m, 3m) > 5.99m)
            print(5 < Add(2m, 3m))
            print(1.5m == Mul(1.50m, 1m))
            """;
        Ran(BuildAndRun(text, "decimal-values"), 0,
            ["-2147483648", "-9223372036854775808", "18446744073709551615", "256", "3.5", "true", "false", "Price { Amount = 2.50 }", "2.50",
                "[1.50, -0.0, -2147483648]", "0.5 and -0.5", "true", "false", "true"], "");
    }
}
