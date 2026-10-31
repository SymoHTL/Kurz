using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the searches of a string and of a list (X1, X9) that no corpus case pins: 3000 strings and needles of two to four
/// letters, one of them of two bytes, which the program and this class generate alike, give the positions, the containment, the start
/// and the end that .NET's searches of their UTF-8 bytes give, through a string's members, through a list of bytes' <c>IndexOf</c> of a
/// run, which the standard library's two-way search answers as it answers a string's, and through a list of <c>int</c>'s, which is
/// compared item by item; and the same under the allocator that makes every block it frees inaccessible. Needles of up to 12 bytes
/// reach both cases of the two-way search, a needle whose part before its cut repeats at its period and one whose does not, each in at
/// least 100 searches, and at least 100 stand twice in their string.
/// </summary>
public sealed class SearchTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Count = 3000;

    private const string Program = """
        string Make(int length, ulong start, int letters) {
            mut ulong state = start
            mut string text = ""
            mut int made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                int pick = int((state >> 33) % ulong(letters))
                if pick == 0 {
                    text = "{text}a"
                } else if pick == 1 {
                    text = "{text}b"
                } else if pick == 2 {
                    text = "{text}ä"
                } else {
                    text = "{text}c"
                }

                made = made + 1
            }

            return text
        }

        List<int> Codes(string text) {
            mut codes = List<int>()
            for b in text.Bytes {
                codes.Add(int(b))
            }

            return codes
        }

        mut ulong seed = 12345UL
        mut n = 0
        while n < 3000 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            int hayLength = int((seed >> 40) % 60UL)
            int pinLength = int((seed >> 20) % 13UL)
            int letters = 2 + int((seed >> 10) % 3UL)
            hay = Make(hayLength, seed, letters)
            pin = Make(pinLength, seed ^ 99UL, letters)
            print("{hay.IndexOf(pin) ?? -1} {hay.Contains(pin)} {hay.StartsWith(pin)} {hay.EndsWith(pin)} {hay.Bytes.IndexOf(pin.Bytes) ?? -1} {Codes(hay).IndexOf(Codes(pin)) ?? -1} {hay.IndexOf('ä') ?? -1} {hay.Contains('b')}")
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
            text.Append(((state >> 33) % (ulong)letters) switch { 0 => "a", 1 => "b", 2 => "ä", _ => "c" });
        }

        return text.ToString();
    }

    private static string Text(bool value) => value ? "true" : "false";

    /// <summary>Whether the two-way search takes the needle as one whose part before its cut repeats at its period, as Rust's <c>TwoWaySearcher::new</c> and the standard library's <c>TwoWay</c> read it.</summary>
    private static bool Repeating(byte[] x)
    {
        var (critLess, periodLess) = MaximalSuffix(x, greater: false);
        var (critGreater, periodGreater) = MaximalSuffix(x, greater: true);
        var (crit, period) = critLess > critGreater ? (critLess, periodLess) : (critGreater, periodGreater);
        return period + crit <= x.Length && x.AsSpan(0, crit).SequenceEqual(x.AsSpan(period, crit));
    }

    /// <summary>The start of the needle's maximal suffix under the order of bytes, or under its reverse, and its period, as Crochemore and Perrin compute them.</summary>
    private static (int Start, int Period) MaximalSuffix(byte[] x, bool greater)
    {
        var (start, other, offset, period) = (0, 1, 0, 1);
        while (other + offset < x.Length)
        {
            var (a, b) = (x[other + offset], x[start + offset]);
            if (greater ? a > b : a < b)
            {
                other += offset + 1;
                offset = 0;
                period = other - start;
            }
            else if (a == b)
            {
                if (offset + 1 == period)
                {
                    other += offset + 1;
                    offset = 0;
                }
                else
                {
                    offset++;
                }
            }
            else
            {
                start = other;
                other++;
                offset = 0;
                period = 1;
            }
        }

        return (start, period);
    }

    [Fact]
    public void Every_search_of_generated_strings_and_their_bytes_gives_what_the_searches_of_dotnet_give()
    {
        var seed = 12345UL;
        var expected = new List<string>();
        var (found, twoWay, twice, repeating) = (0, 0, 0, 0);
        for (var n = 0; n < Count; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var letters = 2 + (int)((seed >> 10) % 3UL);
            var hay = Encoding.UTF8.GetBytes(Make((int)((seed >> 40) % 60UL), seed, letters));
            var pin = Encoding.UTF8.GetBytes(Make((int)((seed >> 20) % 13UL), seed ^ 99UL, letters));
            var at = hay.AsSpan().IndexOf(pin);
            expected.Add(string.Join(
                ' ',
                at,
                Text(at >= 0),
                Text(hay.AsSpan().StartsWith(pin)),
                Text(hay.AsSpan().EndsWith(pin)),
                at,
                at,
                hay.AsSpan().IndexOf("ä"u8),
                Text(hay.AsSpan().IndexOf((byte)'b') >= 0)));
            if (at >= 0 && pin.Length >= 2)
            {
                found++;
            }

            if (pin.Length >= 2 && at != hay.AsSpan().LastIndexOf(pin))
            {
                twice++; // a needle that stands at two places, the first of which a search must not move past
            }

            if (pin.Length >= 2 && hay.Length >= pin.Length)
            {
                twoWay++;
                repeating += Repeating(pin) ? 1 : 0;
            }
        }

        // the floors: the searches of more than one byte, which the two-way search answers, find their needle often and miss it often,
        // many needles stand twice in their string, and both cases of the two-way search are taken often
        Assert.True(found >= 300, $"only {found} needles of two bytes or more are found");
        Assert.True(twoWay - found >= 300, $"only {twoWay - found} needles of two bytes or more are missed");
        Assert.True(twice >= 100, $"only {twice} needles stand twice");
        Assert.True(repeating >= 100 && twoWay - repeating >= 100, $"{repeating} of {twoWay} needles repeat before their cut");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", Program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "searches" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }
}
