using System.Globalization;
using System.Numerics;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on an integer's <c>Parse</c> (X7), <c>Hex.Decode</c> (H7) and <c>Substring</c> (X2) that no corpus case pins: 2000
/// strings, which the program and this class generate alike, give through the <c>Parse</c> of each of the eight integer types what .NET's
/// <c>Parse</c> of the same type gives in <c>NumberStyles.Integer</c> with the invariant culture, a <c>FormatException</c> read as
/// <c>empty</c> where the string holds nothing but whitespace and else as <c>invalid digit</c>, an <c>OverflowException</c> as <c>too
/// small</c> where the number is below 0 and else as <c>too large</c>; 2000 strings of digits and other letters give through
/// <c>Hex.Decode</c> the bytes .NET's <c>Convert.FromHexString</c> gives, or the error at the length or the first byte that is no digit;
/// and 2000 cuts of strings of letters of one to four bytes give the bytes of .NET's cut of their UTF-8 bytes, where the cut falls on no
/// byte inside a code point; and the same for the first 500 of them under the allocator that makes every block it frees inaccessible. Each
/// kind of result is taken at least 100 times. The parses of a floating-point number and of a <c>decimal</c> are the second fact's.
/// </summary>
public sealed class ParseTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Count = 2000;

    /// <summary>
    /// The strings the run under the allocator that makes every freed block inaccessible takes: that allocator keeps the pages of every
    /// block it frees, so what the program holds grows with each block it ever took, as in <see cref="PieceTests"/>.
    /// </summary>
    private const int FencedCount = 500;

    private static readonly string[] Digits = ["0", "9", "1", "8", "5", "2", "7", " ", "-", "+", "x", "\t", "\u000B", "\u00A0", ","];

    private static readonly string[] Fronts = ["", " ", "-", "+", " -", "\t+", "-0", "+-", "\t", " \u000B"];

    private static readonly string[] Backs = ["", " ", "x", "\u000B", "\n", "\r"];

    private static readonly string[] Hexes = ["0", "f", "9", "a", "A", "F", "g", "x", "ä", " "];

    private static readonly string[] Letters = ["a", "ä", "€", "😀", "b", "Ä"];

    private const string Program = """
        string Make(List<string> pool, int length, ulong start, int span) {
            mut ulong state = start
            mut string text = ""
            mut int made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                text = "{text}{pool[int((state >> 33) % ulong(span))]}"
                made = made + 1
            }

            return text
        }

        bool Cuts(string s, int at) {
            return at == s.Bytes.Count || (int(s.Bytes[at]) & 192) != 128
        }

        string I8(string s) => match sbyte.Parse(s) {
            sbyte v => "{v}"
            ParseError e => "{e}"
        }

        string U8(string s) => match byte.Parse(s) {
            byte v => "{v}"
            ParseError e => "{e}"
        }

        string I16(string s) => match short.Parse(s) {
            short v => "{v}"
            ParseError e => "{e}"
        }

        string U16(string s) => match ushort.Parse(s) {
            ushort v => "{v}"
            ParseError e => "{e}"
        }

        string I32(string s) => match int.Parse(s) {
            int v => "{v}"
            ParseError e => "{e}"
        }

        string U32(string s) => match uint.Parse(s) {
            uint v => "{v}"
            ParseError e => "{e}"
        }

        string I64(string s) => match long.Parse(s) {
            long v => "{v}"
            ParseError e => "{e}"
        }

        string U64(string s) => match ulong.Parse(s) {
            ulong v => "{v}"
            ParseError e => "{e}"
        }

        string Decoded(string s) => match Hex.Decode(s) {
            Bytes b => "{b}"
            HexError e => "{e}"
        }

        digits = ["0", "9", "1", "8", "5", "2", "7", " ", "-", "+", "x", "\t", "\u000B", "\u00A0", ","]
        fronts = ["", " ", "-", "+", " -", "\t+", "-0", "+-", "\t", " \u000B"]
        backs = ["", " ", "x", "\u000B", "\n", "\r"]
        hexes = ["0", "f", "9", "a", "A", "F", "g", "x", "ä", " "]
        letters = ["a", "ä", "€", "😀", "b", "Ä"]
        mut ulong seed = 24680UL
        mut n = 0
        while n < 2000 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            body = Make(digits, int((seed >> 40) % 29UL) - 5, seed, 2 + int((seed >> 10) % 14UL))
            s = "{fronts[int((seed >> 20) % 10UL)]}{body}{backs[int((seed >> 30) % 6UL)]}"
            print("{I8(s)} {U8(s)} {I16(s)} {U16(s)} {I32(s)} {U32(s)} {I64(s)} {U64(s)}")
            hex = Make(hexes, int((seed >> 50) % 13UL), seed ^ 99UL, 2 + int((seed >> 14) % 9UL))
            print(Decoded(hex))
            whole = Make(letters, int((seed >> 44) % 20UL), seed ^ 5UL, 6)
            int count = whole.Bytes.Count
            int begin = int((seed >> 8) % ulong(count + 1))
            int taken = int((seed >> 28) % ulong(count - begin + 1))
            if Cuts(whole, begin) && Cuts(whole, begin + taken) {
                print("{whole.Substring(begin, taken).Bytes}")
            } else {
                print("-")
            }

            n = n + 1
        }

        """;

    /// <summary>The string the program's <c>Make</c> makes: <paramref name="length"/> letters of the pool, each picked by a step of the same generator; none for a length below 1.</summary>
    private static string Make(string[] pool, int length, ulong start, int span)
    {
        var state = start;
        var text = new StringBuilder();
        for (var made = 0; made < length; made++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            text.Append(pool[(int)((state >> 33) % (ulong)span)]);
        }

        return text.ToString();
    }

    /// <summary>The text of a list of bytes, as <c>print</c> writes it (A4).</summary>
    private static string Bytes(IEnumerable<byte> bytes) => "[" + string.Join(", ", bytes) + "]";

    /// <summary>The whitespace of C#'s <c>NumberStyles.Integer</c>: the tab to the carriage return, and the space (X7).</summary>
    private static bool White(char c) => c is ' ' or (>= '\t' and <= '\r');

    /// <summary>What <c>T.Parse</c> of the string prints: the number, or the words of the <c>ParseError</c> .NET's exception stands for.</summary>
    private static string Parsed<T>(string s)
        where T : IBinaryInteger<T>
    {
        try
        {
            return T.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(null, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return s.All(White) ? "empty" : "invalid digit";
        }
        catch (OverflowException)
        {
            return s.TrimStart().StartsWith('-') ? "too small" : "too large";
        }
    }

    /// <summary>What <c>Hex.Decode</c> of the string prints: the bytes, or the error at the odd length or at the first byte that is no digit (H7).</summary>
    private static string Decoded(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length % 2 != 0)
        {
            return $"odd length at {bytes.Length}";
        }

        var bad = Array.FindIndex(bytes, b => !Uri.IsHexDigit((char)b));
        return bad >= 0 ? $"invalid digit at {bad}" : Bytes(Convert.FromHexString(s));
    }

    [Fact]
    public void Every_parse_decode_and_cut_of_generated_strings_gives_what_dotnet_gives()
    {
        var seed = 24680UL;
        var expected = new List<string>();
        var parsed = new Dictionary<string, int>(StringComparer.Ordinal);
        var (decoded, odd, invalid, cut, refused, wide) = (0, 0, 0, 0, 0, 0);
        for (var n = 0; n < Count; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var body = Make(Digits, (int)((seed >> 40) % 29UL) - 5, seed, 2 + (int)((seed >> 10) % 14UL));
            var s = Fronts[(int)((seed >> 20) % 10UL)] + body + Backs[(int)((seed >> 30) % 6UL)];
            string[] results =
            [
                Parsed<sbyte>(s), Parsed<byte>(s), Parsed<short>(s), Parsed<ushort>(s), Parsed<int>(s), Parsed<uint>(s), Parsed<long>(s), Parsed<ulong>(s),
            ];
            expected.Add(string.Join(' ', results));
            foreach (var r in results)
            {
                var kind = r is "empty" or "invalid digit" or "too large" or "too small" ? r : "number";
                parsed[kind] = parsed.GetValueOrDefault(kind) + 1;
            }

            var hex = Make(Hexes, (int)((seed >> 50) % 13UL), seed ^ 99UL, 2 + (int)((seed >> 14) % 9UL));
            var text = Decoded(hex);
            expected.Add(text);
            decoded += text.StartsWith('[') ? 1 : 0;
            odd += text.StartsWith("odd", StringComparison.Ordinal) ? 1 : 0;
            invalid += text.StartsWith("invalid", StringComparison.Ordinal) ? 1 : 0;

            var whole = Encoding.UTF8.GetBytes(Make(Letters, (int)((seed >> 44) % 20UL), seed ^ 5UL, 6));
            var begin = (int)((seed >> 8) % (ulong)(whole.Length + 1));
            var taken = (int)((seed >> 28) % (ulong)(whole.Length - begin + 1));
            bool Cuts(int at) => at == whole.Length || (whole[at] & 192) != 128;
            if (Cuts(begin) && Cuts(begin + taken))
            {
                expected.Add(Bytes(whole[begin..(begin + taken)]));
                cut++;
                wide += whole[begin..(begin + taken)].Any(b => b >= 128) ? 1 : 0;
            }
            else
            {
                expected.Add("-");
                refused++;
            }
        }

        // the floors: each kind of result is taken often
        foreach (var kind in (string[])["number", "empty", "invalid digit", "too large", "too small"])
        {
            Assert.True(parsed.GetValueOrDefault(kind) >= 100, $"only {parsed.GetValueOrDefault(kind)} parses give `{kind}`");
        }

        Assert.True(decoded >= 100, $"only {decoded} strings decode");
        Assert.True(odd >= 100, $"only {odd} strings are of an odd length");
        Assert.True(invalid >= 100, $"only {invalid} strings hold a byte that is no digit");
        Assert.True(cut >= 100, $"only {cut} cuts stand");
        Assert.True(refused >= 100, $"only {refused} cuts fall inside a code point");
        Assert.True(wide >= 100, $"only {wide} cuts hold a letter of more than one byte");
        foreach (var (runtime, suffix, count) in new[] { (toolchain.Runtime, "", Count), (ListTests.Fenced, "-fenced", FencedCount) })
        {
            var program = Program.Replace($"while n < {Count} {{", $"while n < {count} {{", StringComparison.Ordinal);
            Assert.Contains($"while n < {count} {{", program, StringComparison.Ordinal);
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "parses" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Take(3 * count), output); // three lines for each string
        }
    }

    /// <summary>The program of the floating-point and <c>decimal</c> parses: a line of the strings' parses follows it for each string.</summary>
    private const string FloatingProgram = """
        string D(string s) => match double.Parse(s) {
            double v => "{v}"
            ParseError e => "{e}"
        }

        string F(string s) => match float.Parse(s) {
            float v => "{v}"
            ParseError e => "{e}"
        }

        string M(string s) => match decimal.Parse(s) {
            decimal v => "{v}"
            ParseError e => "{e}"
        }

        void All(string s) {
            print("{D(s)} {F(s)} {M(s)}")
        }

        void Two(string s) {
            print("{D(s)} {F(s)}")
        }

        """;

    private static readonly string[] Pieces = ["0", "1", "9", "5", "2", "7", "00", ".", ",", "e", "E", "+", "-", " ", "\t", "\u000B", "x", "\u00A0", "\0", "Infinity", "nAn"];

    private static readonly string[] FloatingFronts = ["", "", " ", "-", "+", "\t-", "+-", " +", ".", ","];

    private static readonly string[] FloatingBacks = ["", "", " ", "e5", "E-3", "\n", ".", ",", "\0", "x", "e"];

    private static readonly string[] Blanks = [" ", "\t", "\n", "\r", "\u000B", "\u000C"];

    private static readonly string[] Words = ["Infinity", "infinity", "INFINITY", "iNfInItY", "NaN", "nan", "NAN", "Inf", "infinit", "na"];

    private static readonly string[] Tails = ["5", "50", "500000000000000000000001", "4999999", "51", "15", "25"];

    private const string Largest = "79228162514264337593543950335";

    /// <summary>One step of the generator the strings take their letters from.</summary>
    private static ulong Step(ulong state) => unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);

    /// <summary>A string as a Kurz literal: a backslash, a quote and a brace escaped, and a control character or one past ASCII as <c>\u</c> (L7, L16).</summary>
    private static string Literal(string s) => "\"" + string.Concat(s.Select(c => c switch
    {
        '\\' => "\\\\",
        '"' => "\\\"",
        '{' => "\\{",
        < ' ' or > '~' => $"\\u{(int)c:X4}",
        _ => c.ToString(),
    })) + "\"";

    /// <summary>The text of a <c>decimal</c> as <c>print</c> writes it (A8): .NET's, with a <c>-</c> before a zero whose sign is set.</summary>
    private static string DecimalText(decimal m) =>
        (m == 0m && (decimal.GetBits(m)[3] & int.MinValue) != 0 ? "-" : "") + m.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What <c>double.Parse</c>, <c>float.Parse</c> (<paramref name="type"/> <c>d</c> or <c>f</c>) or <c>decimal.Parse</c> of the string
    /// prints: the number .NET's <c>Parse</c> of the type gives with the invariant culture, in <c>NumberStyles.Float |
    /// NumberStyles.AllowThousands</c> and for a <c>decimal</c> in <c>NumberStyles.Number</c> without its trailing sign, a
    /// <c>FormatException</c> read as <c>empty</c> where the string holds whitespace alone and else as <c>invalid digit</c>, an
    /// <c>OverflowException</c> as <c>too small</c> below 0 and else as <c>too large</c>; and <c>invalid digit</c> for a string that holds
    /// a NUL, which .NET takes after the number and X7 does not.
    /// </summary>
    private static string Floating(string s, char type)
    {
        if (s.Contains('\0'))
        {
            return "invalid digit";
        }

        try
        {
            const NumberStyles style = NumberStyles.Float | NumberStyles.AllowThousands;
            return type switch
            {
                'd' => double.Parse(s, style, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                'f' => float.Parse(s, style, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                _ => DecimalText(decimal.Parse(s, NumberStyles.Number & ~NumberStyles.AllowTrailingSign, CultureInfo.InvariantCulture)),
            };
        }
        catch (FormatException)
        {
            return s.All(White) ? "empty" : "invalid digit";
        }
        catch (OverflowException)
        {
            return s.TrimStart().StartsWith('-') ? "too small" : "too large";
        }
    }

    /// <summary>
    /// The decimal text of the number halfway between a positive <c>double</c> or <c>float</c>, <paramref name="significand"/> times
    /// 2^<paramref name="exponent"/>, and the next one up, exactly, as the integer <c>2M + 1</c> times 2^(<c>E</c> - 1) written in digits
    /// and a power of ten; and of the numbers one digit past its last above it and below it.
    /// </summary>
    private static string[] Halfway(BigInteger significand, int exponent)
    {
        var middle = (2 * significand) + 1;
        var (digits, ten) = exponent - 1 >= 0 ? (middle << (exponent - 1), 0) : (middle * BigInteger.Pow(5, 1 - exponent), exponent - 1);
        var text = digits.ToString(CultureInfo.InvariantCulture);
        return [$"{text}e{ten}", $"{text}1e{ten - 1}", $"{(digits * 10) - 1}e{ten - 1}"];
    }

    /// <summary>
    /// X7: <c>double.Parse</c>, <c>float.Parse</c> and <c>decimal.Parse</c> give what .NET's <c>Parse</c> of the type gives
    /// (<see cref="Floating"/>) for 1500 strings of the pieces of a number's forms and of other bytes, each read by all three, 120 of
    /// the words <c>Infinity</c> and <c>NaN</c> in their cases with a sign and without, 100 strings of whitespace alone, 400 random <c>double</c> values, each finite one as .NET
    /// writes it to round-trip and once more with separators of thousands or to 30 digits, 300 exact halfway points between two neighbouring
    /// <c>double</c> values and as many between two <c>float</c> values, a tenth of each below the smallest normal value and a tenth at the
    /// largest exponent, with the numbers a digit above and below each, which Eisel-Lemire and the slow path round to the even one, the slow path reached by those points, 200 strings of 19 to 800 random
    /// digits, most of which Eisel-Lemire settles from their first 19 digits, and 1200 <c>decimal</c> strings of up to 39 digits before the
    /// point, three zeros before them in a fifth of them, and up to 34 after it, at the largest <c>decimal</c> and past it, a third of them
    /// with one of the tails that round half to even, of up to 24 digits, after their places. Each kind of result is taken at least 100 times, and the first 600 lines are run again under the allocator that makes every
    /// block it frees inaccessible.
    /// </summary>
    [Fact]
    public void Every_parse_of_a_floating_point_number_and_a_decimal_gives_what_dotnet_gives()
    {
        var lines = new List<string>();
        var expected = new List<string>();
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string kind) => kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
        string Kind(string r) => r is "empty" or "invalid digit" or "too large" or "too small" ? r : "number";
        void Read(string s, string types)
        {
            var results = types.Select(type => Floating(s, type)).ToList();
            foreach (var (type, r) in types.Zip(results))
            {
                Count($"{type} {Kind(r)}");
            }

            lines.Add(types switch { "dfm" => $"All({Literal(s)})", "df" => $"Two({Literal(s)})", _ => $"print({char.ToUpperInvariant(types[0])}({Literal(s)}))" });
            expected.Add(string.Join(' ', results));
        }

        var state = 13579UL;
        ulong Next() => state = Step(state);
        string Pick(string[] pool) => pool[(int)((Next() >> 33) % (ulong)pool.Length)];
        for (var n = 0; n < 1500; n++)
        {
            var body = string.Concat(Enumerable.Range(0, (int)((Next() >> 40) % 12UL)).Select(_ => Pick(Pieces)));
            Read(Pick(FloatingFronts) + body + Pick(FloatingBacks), "dfm");
        }

        for (var n = 0; n < 120; n++)
        {
            Read(Pick(FloatingFronts) + Pick(Words) + Pick(FloatingBacks), "df");
        }

        for (var n = 0; n < 100; n++)
        {
            Read(string.Concat(Enumerable.Range(0, (int)(Next() % 4UL)).Select(_ => Pick(Blanks))), "dfm");
        }

        for (var n = 0; n < 400; n++)
        {
            var d = BitConverter.Int64BitsToDouble((long)Next());
            if (double.IsFinite(d))
            {
                Read(d.ToString("R", CultureInfo.InvariantCulture), "d");
                Read(n % 2 == 0 ? d.ToString("N" + (n % 17), CultureInfo.InvariantCulture) : d.ToString("E30", CultureInfo.InvariantCulture), "d");
            }
        }

        var (doubleHalves, floatHalves, subnormal) = (0, 0, 0);
        for (var n = 0; n < 300; n++)
        {
            // every tenth value below the smallest normal one and every tenth at the largest exponent, where the halfway point above the largest
            // finite value is infinity
            var bits = ((Next() >> 1) & (n % 10 is 0 or 1 ? (1UL << 52) - 1 : ulong.MaxValue)) | (n % 10 == 1 ? 2046UL << 52 : 0);
            var biased = (int)(bits >> 52);
            if (biased < 2047)
            {
                var fraction = new BigInteger(bits & ((1UL << 52) - 1));
                subnormal += biased == 0 ? 1 : 0;
                foreach (var s in biased == 0 ? Halfway(fraction, -1074) : Halfway(fraction + (BigInteger.One << 52), biased - 1075))
                {
                    Read(s, "d");
                    doubleHalves++;
                }
            }

            var single = ((uint)(Next() >> 33) & (n % 10 is 0 or 1 ? (1U << 23) - 1 : uint.MaxValue)) | (n % 10 == 1 ? 254U << 23 : 0);
            var exponent = (int)(single >> 23);
            if (exponent < 255)
            {
                var fraction = new BigInteger(single & ((1U << 23) - 1));
                subnormal += exponent == 0 ? 1 : 0;
                foreach (var s in exponent == 0 ? Halfway(fraction, -149) : Halfway(fraction + (BigInteger.One << 23), exponent - 150))
                {
                    Read(s, "f");
                    floatHalves++;
                }
            }
        }

        for (var n = 0; n < 200; n++)
        {
            var length = 19 + (int)(Next() % 782UL);
            var digits = new StringBuilder();
            while (digits.Length < length)
            {
                digits.Append((char)('0' + (int)((Next() >> 33) % 10UL)));
            }

            digits.Insert((int)(Next() % (ulong)length), '.');
            Read($"{digits}e{(int)(Next() % 801UL) - 400 - (length / 2)}", n % 2 == 0 ? "d" : "f");
        }

        var rounded = 0;
        for (var n = 0; n < 1200; n++)
        {
            var whole = new StringBuilder();
            var count = (int)(Next() % 40UL);
            if (Next() % 4UL == 0)
            {
                whole.Append(Largest[..Math.Min(count, Largest.Length)]);
            }

            while (whole.Length < count)
            {
                whole.Append((char)('0' + (int)((Next() >> 33) % 10UL)));
            }

            var integer = whole.ToString();
            if (Next() % 3UL == 0 && integer.Length > 3)
            {
                integer = string.Join(',', Enumerable.Range(0, (integer.Length + 2) / 3).Select(i => integer[Math.Max(0, integer.Length - (3 * (i + 1)))..(integer.Length - (3 * i))]).Reverse());
            }

            var fraction = new StringBuilder();
            for (var places = (int)(Next() % 35UL); fraction.Length < places;)
            {
                fraction.Append((char)('0' + (int)((Next() >> 33) % 10UL)));
            }

            if (Next() % 3UL == 0)
            {
                fraction.Append(Pick(Tails));
            }

            var s = (Next() % 2UL == 0 ? "-" : Pick(["", "+", " "])) + (Next() % 5UL == 0 ? "000" : "") + integer + (fraction.Length > 0 ? "." + fraction : "");
            var significant = (integer + fraction).Replace(",", "", StringComparison.Ordinal).TrimStart('0').Length;
            rounded += (significant > 29 || fraction.Length > 28) && Kind(Floating(s, 'm')) == "number" ? 1 : 0;
            Read(s, "m");
        }

        // the floors: each kind of result is taken often, the halfway points and the numbers below the smallest normal one among them
        foreach (var kind in (string[])["d number", "d empty", "d invalid digit", "f number", "f empty", "f invalid digit", "m number", "m empty", "m invalid digit", "m too large", "m too small"])
        {
            Assert.True(kinds.GetValueOrDefault(kind) >= 100, $"only {kinds.GetValueOrDefault(kind)} parses give `{kind}`");
        }

        Assert.True(doubleHalves >= 600 && floatHalves >= 600, $"only {doubleHalves} and {floatHalves} halfway points");
        Assert.True(subnormal >= 60 && rounded >= 100, $"only {subnormal} values below the smallest normal `double` or `float` and {rounded} rounded `decimal` values");
        foreach (var (runtime, suffix, count) in new[] { (toolchain.Runtime, "", lines.Count), (ListTests.Fenced, "-fenced", 600) })
        {
            var program = FloatingProgram + string.Join('\n', lines.Take(count)) + "\n";
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "floating" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Take(count), output);
        }
    }

    /// <summary>
    /// X7, at the bounds of the parse's two faster paths: <c>double.Parse</c> and <c>float.Parse</c> give what .NET's <c>Parse</c> of the
    /// type gives (<see cref="Floating"/>) for every count of digits from 1 to 24 with the point at every place and with none, and with 19
    /// zeros before them, which the standard library reads 8 at a time and past 19 digits reads the first 19 of again; for a byte that is
    /// no digit, one below <c>0</c>, one above <c>9</c>, a letter, a separator, a point, a byte past ASCII and a NUL, at every place of a run
    /// of 17 digits, before the point and after it; for the forms that hold no digit, or <c>,</c> among more than 19 digits or next to
    /// the point; and for the numbers of 19 digits a unit below, at and a unit above the halfway point between 2^63 and the next
    /// <c>double</c>, where the low 64 bits of Eisel-Lemire's product alone tell the tie from a number above it. The <c>Parse</c> of each of
    /// the eight integer types gives what .NET's gives (<see cref="Parsed{T}"/>) for the largest and the smallest value of each type and a
    /// unit past them, for the numbers of 19 and 20 digits about 2^64, each as it is and with zeros before it to 19 and to 20 digits, where
    /// the standard library adds the first 19 digits with no check and checks each one past them, with whitespace
    /// around it, a letter after it and a sign before it; and for a byte that is no digit, one below <c>0</c>, one above <c>9</c>, a letter,
    /// a separator, a point, a byte past ASCII and a space, at every place of a run of 8 digits and of 20. Each kind of result is taken at
    /// least 100 times, and the program runs again under the allocator that makes every block it frees inaccessible.
    /// </summary>
    [Fact]
    public void A_parse_at_the_bounds_of_its_faster_paths_gives_what_dotnet_gives()
    {
        var lines = new List<string>();
        var expected = new List<string>();
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        void Floats(string s)
        {
            var results = new[] { Floating(s, 'd'), Floating(s, 'f') };
            foreach (var r in results)
            {
                var kind = r is "empty" or "invalid digit" ? r : "number";
                kinds[$"floating {kind}"] = kinds.GetValueOrDefault($"floating {kind}") + 1;
            }

            lines.Add($"Two({Literal(s)})");
            expected.Add(string.Join(' ', results));
        }

        var state = 86420UL;
        string DigitsOf(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => (char)('0' + (int)(((state = Step(state)) >> 33) % 10UL))));
        for (var count = 1; count <= 24; count++)
        {
            var body = DigitsOf(count);
            Floats(body);
            Floats(new string('0', 19) + body);
            for (var at = 0; at <= count; at++)
            {
                Floats($"{body[..at]}.{body[at..]}");
            }
        }

        foreach (var other in (string[])["/", ":", "a", ",", ".", "é", " ", "e", " ", "\0"])
        {
            for (var at = 0; at < 17; at++)
            {
                var run = DigitsOf(17);
                var cut = run[..at] + other + run[(at + 1)..];
                Floats(cut);
                Floats("0." + cut);
                Floats("1" + cut + "9");
            }
        }

        foreach (var s in (string[])
                 [
                     "Infinity", "-nAn", ".", "e5", "-", "1.5.", "1,234.5", ",5", "1,,2", ".5", "5.", "1234567,12345678.5", "12345678.1234567,8", "1,.5", "1,",
                     "1,e5", "1,234,567,890,123,456,789,012.5", "0,000,000,000,000,000,000,001.5", "000.000000000000000000001234567890123456789",
                     "12345678901234567890123", "1234567890123456789012.345", "0.000000000000000000000000123456789012345678901", "9,999,999,999,999,999,999,999e-3",
                     ".Infinity", "-.NaN", "9223372036854776831", "9223372036854776832", "9223372036854776833",
                 ])
        {
            Floats(s);
        }

        void IntsOf(string s)
        {
            string[] results = [Parsed<sbyte>(s), Parsed<byte>(s), Parsed<short>(s), Parsed<ushort>(s), Parsed<int>(s), Parsed<uint>(s), Parsed<long>(s), Parsed<ulong>(s)];
            foreach (var r in results)
            {
                var kind = r is "empty" or "invalid digit" or "too large" or "too small" ? r : "number";
                kinds[$"integer {kind}"] = kinds.GetValueOrDefault($"integer {kind}") + 1;
            }

            lines.Add($"Ints({Literal(s)})");
            expected.Add(string.Join(' ', results));
        }

        string[] bounds =
        [
            "127", "128", "-128", "-129", "255", "256", "32767", "32768", "-32768", "-32769", "65535", "65536", "2147483647", "2147483648",
            "-2147483648", "-2147483649", "4294967295", "4294967296", "9223372036854775807", "9223372036854775808", "-9223372036854775808",
            "-9223372036854775809", "18446744073709551615", "18446744073709551616", "36893488147419103232", "99999999999999999999",
            "9999999999999999999", "10000000000000000000", "0", "-0", "-1",
        ];
        foreach (var bound in bounds)
        {
            var digits = bound.TrimStart('-');
            var sign = bound[..^digits.Length];
            foreach (var s in (string[])[bound, sign + digits.PadLeft(19, '0'), sign + digits.PadLeft(20, '0'), $" {bound}\t", $"{bound}x", $"+{bound}"])
            {
                IntsOf(s);
            }
        }

        foreach (var other in (string[])["/", ":", "a", ",", ".", "\u00E9", "\u00A0", "e", " "])
        {
            foreach (var length in (int[])[8, 20])
            {
                for (var at = 0; at < length; at++)
                {
                    var run = DigitsOf(length);
                    IntsOf(run[..at] + other + run[(at + 1)..]);
                }
            }
        }

        foreach (var kind in (string[])["floating number", "floating invalid digit", "integer number", "integer invalid digit", "integer too large", "integer too small"])
        {
            Assert.True(kinds.GetValueOrDefault(kind) >= 100, $"only {kinds.GetValueOrDefault(kind)} parses give `{kind}`");
        }

        var integers = Program[..Program.IndexOf("string Decoded(", StringComparison.Ordinal)];
        var program = FloatingProgram + integers + """
            void Ints(string s) {
                print("{I8(s)} {U8(s)} {I16(s)} {U16(s)} {I32(s)} {U32(s)} {I64(s)} {U64(s)}")
            }

            """ + string.Join('\n', lines) + "\n";
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "bounds" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    /// <summary>
    /// X7: the powers of five the compiler writes into a program that parses a <c>double</c> or a <c>float</c> (<see cref="Emitter.Fives"/>)
    /// are fast_float's and Rust's: two words for each power from 5^-342 to 5^308, the first, the one of 5^0 and the last as their
    /// tables hold them; the other rows are held by the parses of the fact above that reach them.
    /// </summary>
    [Fact]
    public void The_powers_of_five_of_the_parse_are_those_of_rusts_table()
    {
        var fives = Emitter.Fives.Value;
        Assert.Equal(2 * 651, fives.Length);
        Assert.Equal((0xEEF453D6923BD65AUL, 0x113FAA2906A13B3FUL), (fives[0], fives[1]));
        Assert.Equal((0x8000000000000000UL, 0UL), (fives[2 * 342], fives[(2 * 342) + 1]));
        Assert.Equal((0x8E679C2F5E44FF8FUL, 0x570F09EAA7EA7648UL), (fives[2 * 650], fives[(2 * 650) + 1]));
    }

    /// <summary>
    /// X7: a program that declares functions of the names of the parse's helpers in the standard library, <c>Product</c>, <c>Slow</c> and
    /// <c>Shift</c>, calls its own, in a test build and in a release build, while its parse of the halfway point 1 + 2^-53, which
    /// Eisel-Lemire leaves to the slow path, calls the library's and ties to the even <c>double</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_program_that_declares_the_names_of_the_parses_helpers_calls_its_own(bool testBuild)
    {
        const string program = """
            int Product(int a, int b) => a * b + 1

            int Slow(int a) => a - 1

            int Shift(int n) => n * 3

            print(Product(2, 3))
            print(Slow(5))
            print(Shift(4))
            match double.Parse("1.00000000000000011102230246251565404236316680908203125") {
                double d => print(d)
                ParseError e => print(e)
            }

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, testBuild ? "helper-names" : "helper-names-release"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(["7", "4", "12", "1"], output);
    }

    /// <summary>
    /// X2, X10: a cut inside the string whose start or end falls inside a code point of <c>"hä€"</c>, whose <c>ä</c> is bytes 1 and 2 and
    /// whose <c>€</c> bytes 3 to 5, ends the program with <c>not-char-boundary</c> and names the position on the line below: the start
    /// where it alone falls inside, the end where it alone does, and the start where both do, as Rust's <c>&amp;s[a..b]</c> reads a cut.
    /// </summary>
    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(1, 3, 4)]
    [InlineData(2, 2, 2)]
    public void A_cut_inside_a_code_point_names_its_start_before_its_end(int startIndex, int length, int named)
    {
        var program = $"s = \"h\u00e4\u20ac\"\nprint(s.Substring({startIndex}, {length}))\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, all) = Toolchain.Run(toolchain.Build(compilation.Program, $"cut-{startIndex}-{length}"), null);
        var (error, _) = TraceTests.Split(all);
        Assert.Equal((1, 0), (code, output.Count));
        Assert.Equal($"error: not-char-boundary at program.kz:2:\nbyte {named} is inside a code point\n", error);
    }

    /// <summary>
    /// X2: a cut that reaches outside <c>"hä€"</c>, of 6 bytes, ends the program with <c>index-out-of-range</c>: a negative
    /// <c>startIndex</c> or <c>length</c>, a start past the end, and a <c>startIndex + length</c> past the largest <c>int</c>, which the
    /// bounds read without adding the two; the bounds are read before the code points, so a start inside <c>ä</c> with a negative
    /// length is outside, and a start at the end with no bytes, the positive control, is the empty string.
    /// </summary>
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(7, 0)]
    [InlineData(2147483647, 1)]
    [InlineData(1, 2147483647)]
    [InlineData(2, -1)]
    [InlineData(6, 0)]
    public void A_cut_outside_the_string_is_out_of_range(int startIndex, int length)
    {
        var program = $"s = \"hä€\"\nprint(s.Substring({startIndex}, {length}) == \"\")\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, all) = Toolchain.Run(toolchain.Build(compilation.Program, $"outside-{startIndex}-{length}".Replace('-', 'm')), null);
        var (error, _) = TraceTests.Split(all);
        if (startIndex == 6)
        {
            Assert.Equal(["true"], output);
            Assert.Equal((0, ""), (code, error));
            return;
        }

        Assert.Equal((1, 0), (code, output.Count));
        Assert.Equal("error: index-out-of-range at program.kz:2\n", error);
    }
}
