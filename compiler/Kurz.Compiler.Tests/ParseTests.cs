using System.Globalization;
using System.Numerics;
using System.Text;
using Kurz.Compiler.Build;
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
/// kind of result is taken at least 100 times.
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
