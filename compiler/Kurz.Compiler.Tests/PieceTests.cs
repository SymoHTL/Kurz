using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the trims, the splits, the replaces, the joins and the ASCII changes of case of a string (X3 to X6) that no corpus case
/// pins: 2000 strings of up to 40 letters out of fourteen, among them whitespace of one, two and three bytes, the
/// vertical tab, which the ASCII forms keep, and letters of two bytes, which the program and this class generate alike, give the bytes
/// .NET's members of the same strings give, through each member and each form; and the same under the allocator that makes every block
/// it frees inaccessible, for the first 500 of the strings. Each kind of result is taken at least 100 times: a trim that takes from both ends, a Unicode trim that takes
/// more than the ASCII one, a trim of a `char` from the start and from the end, a split into three pieces or more, a replace that
/// changes the string, a replace of a `char` by a `char` that changes it and a change of case that changes it.
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

    private static readonly string[] Alphabet = ["a", "b", "ä", " ", "\t", "x", "\u00A0", "\u3000", "\n", "\u000B", "B", "\u2028", "\u0085", "Ä"];

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

        alphabet = ["a", "b", "ä", " ", "\t", "x", "\u00A0", "\u3000", "\n", "\u000B", "B", "\u2028", "\u0085", "Ä"]
        bar = "|"
        swap = "<>"
        none = ""
        mut ulong seed = 54321UL
        mut n = 0
        while n < 2000 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            int letters = 2 + int((seed >> 10) % 13UL)
            hay = Make(alphabet, int((seed >> 40) % 41UL), seed, letters)
            pin = Make(alphabet, int((seed >> 20) % 4UL), seed ^ 77UL, letters)
            print("{hay.Trim().Bytes} {hay.TrimStart().Bytes} {hay.TrimEnd().Bytes} {hay.TrimAscii().Bytes} {hay.TrimStartAscii().Bytes} {hay.TrimEndAscii().Bytes} {hay.Trim('x').Bytes} {hay.Trim('ä').Bytes} {hay.TrimStart('x').Bytes} {hay.TrimEnd('ä').Bytes}")
            print("{string.Join(bar, hay.Split(pin)).Bytes} {hay.Split(pin).Count} {string.Join(bar, hay.Split('a')).Bytes} {hay.Replace(pin, swap).Bytes} {hay.Replace('ä', none).Bytes} {hay.ToUpperAscii().Bytes} {hay.ToLowerAscii().Bytes} {hay.Replace('ä', 'x').Bytes} {hay.Replace('x', '€').Bytes} {string.Join('€', hay.Split(pin)).Bytes}")
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
        var (start, end, swapped) = (0, 0, 0);
        for (var n = 0; n < Count; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var letters = 2 + (int)((seed >> 10) % 13UL);
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
                Bytes(string.Join('€', split))));
            both += hay.TrimStart().Length < hay.Length && hay.TrimEnd().Length < hay.Length ? 1 : 0;
            unicode += hay.Trim().Length < trimAscii.Length ? 1 : 0;
            pieces += split.Length >= 3 ? 1 : 0;
            replaced += pin.Length > 0 && hay.Contains(pin, StringComparison.Ordinal) ? 1 : 0;
            cased += AsciiCase(hay, upper: true) != hay ? 1 : 0;
            start += hay.TrimStart('x').Length < hay.Length ? 1 : 0;
            end += hay.TrimEnd('ä').Length < hay.Length ? 1 : 0;
            swapped += hay.Contains('ä') ? 1 : 0;
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
}
