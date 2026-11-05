using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the trims, the splits, the replaces, the joins and the ASCII changes of case of a string (X3 to X6) that no corpus case
/// pins: 2000 strings of up to 40 letters out of twenty-six, among them every code point of Unicode's White_Space that is not ASCII, of two
/// and three bytes, but those inside U+2000 to U+200A, a range the alphabet holds at both ends and in the middle, the form feed and the
/// carriage return, which the ASCII forms take, and the vertical tab, which they keep, three code points that are no whitespace beside them, U+001F, U+200B
/// and U+180E, and letters of two bytes, which the program and this class generate alike, give the bytes .NET's members of the same strings
/// give, through each member and each form; and the same under the allocator that makes every block it frees inaccessible, for the first
/// 500 of the strings. Each kind of result is taken at least 100 times: a trim that takes from both ends, a Unicode trim that takes more
/// than the ASCII one, a trim of a `char` from the start and from the end, a split into three pieces or more, a replace that changes the
/// string, a replace of a `char` by a `char` that changes it, one of a `char` of one byte by another that changes it, and a change of
/// case that changes it.
/// </summary>
public sealed class PieceTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Count = 2000;

    /// <summary>
    /// The strings the run under the allocator that makes every freed block inaccessible takes: that allocator keeps the pages of every
    /// block it frees, so what the program holds grows with each block it ever took, and with all 2000 strings it ran past the memory of
    /// the machine that runs the suite and was killed.
    /// </summary>
    private const int FencedCount = 500;

    private static readonly string[] Alphabet = ["a", "b", "ä", " ", "\t", "x", "\u00A0", "\u3000", "\n", "\u000B", "B", "\u2028", "\u0085", "Ä", "\u1680", "\u2000", "\u2005", "\u200A", "\u2029", "\u202F", "\u205F", "\u000C", "\u000D", "\u001F", "\u200B", "\u180E"];

    private const string Program = """
        string Make(List<string> pool, int length, ulong start, int letters) {
            mut ulong state = start
            mut string text = ""
            mut int made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                text = "{text}{pool[int((state >> 33) % ulong(letters))]}"
                made = made + 1
            }

            return text
        }

        alphabet = ["a", "b", "ä", " ", "\t", "x", "\u00A0", "\u3000", "\n", "\u000B", "B", "\u2028", "\u0085", "Ä", "\u1680", "\u2000", "\u2005", "\u200A", "\u2029", "\u202F", "\u205F", "\u000C", "\u000D", "\u001F", "\u200B", "\u180E"]
        bar = "|"
        swap = "<>"
        none = ""
        mut ulong seed = 54321UL
        mut n = 0
        while n < 2000 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            int letters = 2 + int((seed >> 10) % 25UL)
            hay = Make(alphabet, int((seed >> 40) % 41UL), seed, letters)
            pin = Make(alphabet, int((seed >> 20) % 4UL), seed ^ 77UL, letters)
            print("{hay.Trim().Bytes} {hay.TrimStart().Bytes} {hay.TrimEnd().Bytes} {hay.TrimAscii().Bytes} {hay.TrimStartAscii().Bytes} {hay.TrimEndAscii().Bytes} {hay.Trim('x').Bytes} {hay.Trim('ä').Bytes} {hay.TrimStart('x').Bytes} {hay.TrimEnd('ä').Bytes}")
            print("{string.Join(bar, hay.Split(pin)).Bytes} {hay.Split(pin).Count} {string.Join(bar, hay.Split('a')).Bytes} {hay.Replace(pin, swap).Bytes} {hay.Replace('ä', none).Bytes} {hay.ToUpperAscii().Bytes} {hay.ToLowerAscii().Bytes} {hay.Replace('ä', 'x').Bytes} {hay.Replace('x', '€').Bytes} {string.Join('€', hay.Split(pin)).Bytes} {hay.Replace('a', 'b').Bytes}")
            n = n + 1
        }

        """;

    /// <summary>The string the program's <c>Make</c> makes: <paramref name="length"/> letters, each picked by a step of the same generator.</summary>
    private static string Make(int length, ulong start, int letters)
    {
        var state = start;
        var text = new StringBuilder();
        for (var made = 0; made < length; made++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            text.Append(Alphabet[(int)((state >> 33) % (ulong)letters)]);
        }

        return text.ToString();
    }

    /// <summary>The text of a list of bytes, the string's UTF-8 bytes, as <c>print</c> writes it (A4).</summary>
    private static string Bytes(string text) => "[" + string.Join(", ", Encoding.UTF8.GetBytes(text)) + "]";

    /// <summary>ASCII's whitespace, the five of Rust's <c>u8::is_ascii_whitespace</c> (X4).</summary>
    private static bool AsciiWhite(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private static string AsciiCase(string text, bool upper) =>
        string.Concat(text.Select(c => upper ? (c is >= 'a' and <= 'z' ? (char)(c - 32) : c) : (c is >= 'A' and <= 'Z' ? (char)(c + 32) : c)));

    [Fact]
    public void Every_trim_split_replace_join_and_change_of_case_of_generated_strings_gives_what_the_members_of_dotnet_give()
    {
        var seed = 54321UL;
        var expected = new List<string>();
        var (both, unicode, pieces, replaced, cased) = (0, 0, 0, 0, 0);
        var (start, end, swapped, bytes) = (0, 0, 0, 0);
        for (var n = 0; n < Count; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var letters = 2 + (int)((seed >> 10) % 25UL);
            var hay = Make((int)((seed >> 40) % 41UL), seed, letters);
            var pin = Make((int)((seed >> 20) % 4UL), seed ^ 77UL, letters);

            // .NET's `Trim` takes `char.IsWhiteSpace`, which is Unicode's White_Space; a separator or a pattern that is empty leaves the
            // string as it is (X5); .NET's members of a `string` compare ordinal
            var startAscii = new string(hay.SkipWhile(AsciiWhite).ToArray());
            var endAscii = new string(hay.Reverse().SkipWhile(AsciiWhite).Reverse().ToArray());
            var trimAscii = new string(startAscii.Reverse().SkipWhile(AsciiWhite).Reverse().ToArray());
            var split = pin.Length == 0 ? [hay] : hay.Split(pin);
            expected.Add(string.Join(
                ' ',
                Bytes(hay.Trim()),
                Bytes(hay.TrimStart()),
                Bytes(hay.TrimEnd()),
                Bytes(trimAscii),
                Bytes(startAscii),
                Bytes(endAscii),
                Bytes(hay.Trim('x')),
                Bytes(hay.Trim('ä')),
                Bytes(hay.TrimStart('x')),
                Bytes(hay.TrimEnd('ä'))));
            expected.Add(string.Join(
                ' ',
                Bytes(string.Join("|", split)),
                split.Length,
                Bytes(string.Join("|", hay.Split('a'))),
                Bytes(pin.Length == 0 ? hay : hay.Replace(pin, "<>", StringComparison.Ordinal)),
                Bytes(hay.Replace("ä", string.Empty, StringComparison.Ordinal)),
                Bytes(AsciiCase(hay, upper: true)),
                Bytes(AsciiCase(hay, upper: false)),
                Bytes(hay.Replace('ä', 'x')),
                Bytes(hay.Replace('x', '€')),
                Bytes(string.Join('€', split)),
                Bytes(hay.Replace('a', 'b'))));
            both += hay.TrimStart().Length < hay.Length && hay.TrimEnd().Length < hay.Length ? 1 : 0;
            unicode += hay.Trim().Length < trimAscii.Length ? 1 : 0;
            pieces += split.Length >= 3 ? 1 : 0;
            replaced += pin.Length > 0 && hay.Contains(pin, StringComparison.Ordinal) ? 1 : 0;
            cased += AsciiCase(hay, upper: true) != hay ? 1 : 0;
            start += hay.TrimStart('x').Length < hay.Length ? 1 : 0;
            end += hay.TrimEnd('ä').Length < hay.Length ? 1 : 0;
            swapped += hay.Contains('ä') ? 1 : 0;
            bytes += hay.Contains('a') ? 1 : 0;
        }

        // the floors: each kind of result is taken often
        Assert.True(both >= 100, $"only {both} strings are trimmed at both ends");
        Assert.True(unicode >= 100, $"only {unicode} strings lose more to the Unicode trim than to the ASCII one");
        Assert.True(pieces >= 100, $"only {pieces} splits give three pieces or more");
        Assert.True(replaced >= 100, $"only {replaced} replaces change their string");
        Assert.True(cased >= 100, $"only {cased} changes of case change their string");
        Assert.True(start >= 100, $"only {start} strings lose a `char` from the start");
        Assert.True(end >= 100, $"only {end} strings lose a `char` from the end");
        Assert.True(swapped >= 100, $"only {swapped} replaces of a `char` change their string");
        Assert.True(bytes >= 100, $"only {bytes} replaces of a `char` of one byte by another change their string");
        foreach (var (runtime, suffix, count) in new[] { (toolchain.Runtime, "", Count), (ListTests.Fenced, "-fenced", FencedCount) })
        {
            var program = Program.Replace($"while n < {Count} {{", $"while n < {count} {{", StringComparison.Ordinal);
            Assert.Contains($"while n < {count} {{", program, StringComparison.Ordinal);
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "pieces" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Take(2 * count), output); // two lines for each string
        }
    }

    /// <summary>
    /// The 16 bytes the long text of <see cref="OneByteProgram"/> repeats: six commas, two of them next to each other, one at the end, and a
    /// letter of two bytes. The first comma stands at 1, so that the count's vectors of 16 bytes from it end 15 bytes before the end of the
    /// text: one more vector would read a byte past it.
    /// </summary>
    private const string Unit = "a,,bc,dé,fg,ij,";

    /// <summary>
    /// The times <see cref="Unit"/> stands in the long text: a multiple of 16, so that every replace of its commas by 0, 2, 3, 4 or 8 bytes
    /// gives a count of bytes that is a multiple of 16, whose block the allocator ends where its pages end; and enough that the replace by
    /// none still asks for more than 262144 bytes, a block the allocator maps on its own.
    /// </summary>
    private const int Units = 26224;

    /// <summary>
    /// The replaces of one byte by a replacement of another count, the standard library's <c>ReplaceOne</c> up to 8 bytes: 400 strings of up
    /// to 300 letters out of one to eight, the comma first among them, so that some strings are commas alone and many hold commas next to
    /// each other, at the start and at the end, beside letters of two bytes; each comma replaced by 0, 2, 3, 4 and 8 bytes, given as a
    /// <c>char</c> and as a string, and by 9 bytes, which the general path takes, and the <c>x</c> by 2 bytes. Then the long text, whose
    /// every replace is a block the allocator maps on its own, under the allocator that puts an inaccessible page after every such block:
    /// a byte written past the end of the string ends the process.
    /// </summary>
    private static readonly string OneByteProgram = $$"""
        string Make(List<string> pool, int length, ulong start, int letters) {
            mut ulong state = start
            mut string text = ""
            mut int made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                text = "{text}{pool[int((state >> 33) % ulong(letters))]}"
                made = made + 1
            }

            return text
        }

        ulong Check(string s) {
            mut ulong h = 1469598103934665603UL
            for b in s.Bytes {
                h = (h ^ ulong(b)) *% 1099511628211UL
            }

            return h
        }

        mixed = [",", "a", "é", "b", ",", "€", "x", "ÿ"]
        mut ulong seed = 13579UL
        mut n = 0
        while n < 400 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            int letters = 1 + int((seed >> 10) % 8UL)
            hay = Make(mixed, int((seed >> 40) % 301UL), seed, letters)
            print("{hay.Replace(',', 'é')}|{hay.Replace(',', '€')}|{hay.Replace(",", "")}|{hay.Replace(',', "😀")}|{hay.Replace(",", "12345678")}|{hay.Replace(',', "123456789")}|{hay.Replace("x", "<>")}")
            n = n + 1
        }

        plain = ["a", "é", "b", "€", "x", "ÿ"]
        while n < 500 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            front = Make(plain, int((seed >> 40) % 300UL), seed, 6)
            back = Make(plain, int((seed >> 20) % 300UL), seed ^ 31UL, 6)
            sparse = "{front},{back}"
            ended = "{sparse},"
            print("{sparse.Replace(',', 'é')}|{sparse.Replace(",", "")}|{sparse.Replace(',', "12345678")}|{ended.Replace(',', '€')}")
            n = n + 1
        }

        mut units = List<string>()
        for i in 0..<{{Units}} {
            units.Add("{{Unit}}")
        }

        big = string.Join("", units)
        print("{Check(big.Replace(',', 'é'))} {Check(big.Replace(',', '€'))} {Check(big.Replace(",", ""))} {Check(big.Replace(',', "😀"))} {Check(big.Replace(",", "12345678"))}")

        """;

    /// <summary>The string the program's <c>Make</c> makes of <paramref name="pool"/>: <paramref name="length"/> of its first <paramref name="letters"/> letters, each picked by a step of the same generator.</summary>
    private static string Made(string[] pool, int length, ulong start, int letters)
    {
        var state = start;
        var text = new StringBuilder();
        for (var made = 0; made < length; made++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            text.Append(pool[(int)((state >> 33) % (ulong)letters)]);
        }

        return text.ToString();
    }

    /// <summary>The check the program's <c>Check</c> takes: FNV-1a of 64 bits over the string's UTF-8 bytes.</summary>
    private static ulong Check(string text)
    {
        var h = 1469598103934665603UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            h = unchecked((h ^ b) * 1099511628211UL);
        }

        return h;
    }

    [Fact]
    public void A_replace_of_one_byte_by_none_or_by_2_to_9_bytes_gives_what_dotnet_gives_and_writes_nothing_past_the_string()
    {
        string[] pool = [",", "a", "é", "b", ",", "€", "x", "ÿ"];
        var seed = 13579UL;
        var expected = new List<string>();
        var (only, next, last, words) = (0, 0, 0, 0);
        for (var n = 0; n < 400; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var letters = 1 + (int)((seed >> 10) % 8UL);
            var text = Made(pool, (int)((seed >> 40) % 301UL), seed, letters);
            expected.Add(string.Join(
                '|',
                text.Replace(",", "é", StringComparison.Ordinal),
                text.Replace(",", "€", StringComparison.Ordinal),
                text.Replace(",", string.Empty, StringComparison.Ordinal),
                text.Replace(",", "😀", StringComparison.Ordinal),
                text.Replace(",", "12345678", StringComparison.Ordinal),
                text.Replace(",", "123456789", StringComparison.Ordinal),
                text.Replace("x", "<>", StringComparison.Ordinal)));
            only += text.Length > 0 && text.All(c => c == ',') ? 1 : 0;
            next += text.Contains(",,", StringComparison.Ordinal) ? 1 : 0;
            last += text.EndsWith(',') ? 1 : 0;
            words += Encoding.UTF8.GetByteCount(text) >= 64 && text.Contains(',') && text.Any(c => c != ',') ? 1 : 0;
        }

        // strings of one comma, and of two with one at the end, between letters, of which most have 256 bytes or more for each comma
        string[] plain = ["a", "é", "b", "€", "x", "ÿ"];
        var far = 0;
        for (var n = 400; n < 500; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var sparse = $"{Made(plain, (int)((seed >> 40) % 300UL), seed, 6)},{Made(plain, (int)((seed >> 20) % 300UL), seed ^ 31UL, 6)}";
            expected.Add(string.Join(
                '|',
                sparse.Replace(",", "é", StringComparison.Ordinal),
                sparse.Replace(",", string.Empty, StringComparison.Ordinal),
                sparse.Replace(",", "12345678", StringComparison.Ordinal),
                (sparse + ",").Replace(",", "€", StringComparison.Ordinal)));
            far += Encoding.UTF8.GetByteCount(sparse) >= 256 && Encoding.UTF8.GetByteCount(sparse + ",") >= 512 ? 1 : 0;
        }

        // the floors: each kind of string is taken often
        Assert.True(only >= 20, $"only {only} strings are commas alone");
        Assert.True(next >= 100, $"only {next} strings hold two commas next to each other");
        Assert.True(last >= 50, $"only {last} strings end in a comma");
        Assert.True(words >= 100, $"only {words} strings of 64 bytes or more hold a comma and a letter");
        Assert.True(far >= 30, $"only {far} strings hold 256 bytes or more for each comma");
        var big = string.Concat(Enumerable.Repeat(Unit, Units));
        Assert.True(Encoding.UTF8.GetByteCount(big.Replace(",", string.Empty, StringComparison.Ordinal)) is > 262144 and var none && none % 16 == 0);
        expected.Add(string.Join(' ', new[] { "é", "€", string.Empty, "😀", "12345678" }.Select(r => Check(big.Replace(",", r, StringComparison.Ordinal)))));
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", OneByteProgram), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "replace-one" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }
}
