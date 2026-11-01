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
/// least 100 searches, and at least 100 stand twice in their string. A search of one byte, which reads two words at a time as Rust's
/// <c>memchr</c> does, finds it at every place of lists of 0 to 40 bytes and from each of the 8 places in a word a search can start at.
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

    private const string OneByte = """
        string Line(int length, ulong start) {
            mut ulong state = start
            mut string text = ""
            mut int made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                int pick = int((state >> 33) % 24UL)
                if pick == 0 {
                    text = "{text},"
                } else if pick < 8 {
                    text = "{text}a"
                } else if pick < 16 {
                    text = "{text}ä"
                } else {
                    text = "{text}b"
                }

                made = made + 1
            }

            return text
        }

        List<int> targets = [0, 35, 128, 255]
        for ti in 0..<targets.Count {
            byte t = byte(targets[ti])
            for n in 0..<41 {
                int places = n + 1
                for q in 0..<places {
                    mut xs = List<byte>()
                    for k in 0..<n {
                        mut v = byte((k * 37 + 11) % 256)
                        if v == t {
                            v = byte((k * 37 + 12) % 256)
                        }

                        xs.Add(v)
                    }

                    if q > 0 {
                        xs[q - 1] = t
                    }

                    print(xs.IndexOf(t) ?? -1)
                }
            }
        }

        mut ulong seed = 777UL
        for r in 0..<1500 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            mut line = ""
            mut first = true
            for piece in Line(int((seed >> 40) % 150UL), seed).Split(",") {
                if first {
                    line = "{piece.Bytes.Count}"
                    first = false
                } else {
                    line = "{line} {piece.Bytes.Count}"
                }
            }

            print(line)
        }

        """;

    /// <summary>The string the program's <c>Line</c> makes: <paramref name="length"/> letters, one in 24 a comma, each picked by a step of the same generator.</summary>
    private static string Line(int length, ulong start)
    {
        var state = start;
        var text = new StringBuilder();
        for (var made = 0; made < length; made++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            var pick = (state >> 33) % 24UL;
            text.Append(pick == 0 ? "," : pick < 8 ? "a" : pick < 16 ? "ä" : "b");
        }

        return text.ToString();
    }

    [Fact]
    public void A_search_of_one_byte_finds_it_where_dotnet_does_in_either_word_and_at_every_alignment()
    {
        // X1, X9: `Find` looks for one byte as Rust's `memchr` of a slice does, byte by byte up to an address that is a multiple of 8 and
        // then two words at a time. A list of 0 to 40 bytes, every value but the one looked for, 0, 35, 128 and 255, holds it at each
        // place or nowhere, so that it stands in the first word of a pair, in the second, before the words and after them; and `Split` of
        // 1500 strings of letters of one and two bytes at a comma looks for the comma from each place after one, at each of the 8 places
        // in a word, the string's bytes standing in a block at a multiple of 8 (`StringBlock`)
        var expected = new List<string>();
        foreach (var t in new[] { 0, 35, 128, 255 })
        {
            for (var n = 0; n <= 40; n++)
            {
                for (var q = 0; q <= n; q++)
                {
                    expected.Add((q > 0 ? q - 1 : -1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        var seed = 777UL;
        var starts = new int[8];
        for (var r = 0; r < 1500; r++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var bytes = Encoding.UTF8.GetBytes(Line((int)((seed >> 40) % 150UL), seed));
            var pieces = new List<int>();
            var at = 0;
            while (true)
            {
                var found = bytes.AsSpan(at).IndexOf((byte)',');
                if (found >= 8 && found <= bytes.Length - at - 16)
                {
                    starts[at % 8]++; // a comma past the bytes before the first word and in a pair of words the search reads
                }

                pieces.Add(found < 0 ? bytes.Length - at : found);
                if (found < 0)
                {
                    break;
                }

                at += found + 1;
            }

            expected.Add(string.Join(' ', pieces));
        }

        // the floor: each of the 8 places a search can start at in a word finds a comma through the words at least 50 times
        Assert.True(starts.All(s => s >= 50), string.Join(", ", starts));
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", OneByte), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "search-one-byte" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }
}
