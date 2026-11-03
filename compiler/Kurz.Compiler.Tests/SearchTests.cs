using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the searches of a string and of a list (X1, X9) that no corpus case pins: 3000 strings and needles of two to four
/// letters, one of them of two bytes, which the program and this class generate alike, give the positions, the containment, the start
/// and the end that .NET's searches of their UTF-8 bytes give, through a string's members, through a list of bytes' <c>IndexOf</c> of a
/// run, which the standard library's <c>Find</c> answers as it answers a string's, and through a list of <c>int</c>'s, which is
/// compared item by item; and the same under the allocator that makes every block it frees inaccessible, and through the standard
/// library whose test of SSE2 is <c>false</c>, which takes the two-way search for every needle of more than one byte (R18). Needles of
/// up to 12 bytes reach both cases of the two-way search, a needle whose part before its cut repeats at its period and one whose does
/// not, each in at least 100 searches, and at least 100 stand twice in their string. Needles of 2 to 35 bytes in strings of up to 220
/// bytes take each part of the search of SSE2, <c>PairFind</c>, and the two-way search, and give .NET's first position, with the bytes
/// of a match standing past the end of a list of bytes whose search must not read them; and a list of items of 2, 4 and 8 bytes gives
/// .NET's <c>IndexOf</c>, through the search of SSE2 and through the loop after it. A search of one byte, which reads two words at a time as Rust's
/// <c>memchr</c> does, finds it at every place of lists of 0 to 40 bytes and from each of the 8 places in a word a search can start at,
/// takes the first of two places within one word, and reads no byte past the count of a list or a string. Built for a 64-bit Arm
/// machine, whose test of SSE2 is <c>false</c>, <c>Find</c> calls no <c>PairFind</c> and the searches of a list hold no vector.
/// </summary>
public sealed class SearchTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Count = 3000;

    /// <summary>The standard library whose test of SSE2 is <c>false</c>, so that every search takes the path a target without SSE2 takes (R18).</summary>
    internal static Kurz.Compiler.Build.CheckedRuntime Plain() => ListTests.Edited(("std/std.kz", "X86.Sse2.IsSupported", "false"));

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
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced"), (Plain(), "-plain") })
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

    private const string FirstAndCount = """
        List<int> targets = [0, 35, 128, 255]
        for ti in 0..<targets.Count {
            byte t = byte(targets[ti])
            byte flip = byte(targets[ti] ^ 1)
            for n in 0..<41 {
                mut xs = List<byte>()
                for k in 0..<64 {
                    xs.Add(t)
                }

                xs.Clear()
                for k in 0..<n {
                    mut v = byte((k * 37 + 11) % 256)
                    if v == t {
                        v = byte((k * 37 + 12) % 256)
                    }

                    xs.Add(v)
                }

                print(xs.IndexOf(t) ?? -1)
                for q in 0..<n {
                    List<int> laters = [q + 1 + (q * 7 + n) % 16, n - 1]
                    for r in laters {
                        if r > q && r < n {
                            mut ys = xs[0..<n]
                            ys[q] = t
                            ys[r] = t
                            if q + 1 < r {
                                ys[q + 1] = flip
                            }

                            print(ys.IndexOf(t) ?? -1)
                        }
                    }
                }
            }
        }

        for n in 0..<41 {
            mut cs = Bytes()
            for k in 0..<64 {
                cs.Add(byte(44))
            }

            cs.Clear()
            for k in 0..<n {
                if (k * 37 + 5) % 13 < 2 {
                    cs.Add(byte(44))
                } else {
                    cs.Add(byte(97))
                }
            }

            s = string.FromUtf8(cs) else {
                InvalidUtf8 => "?"
            }

            at = s.IndexOf(',') ?? -1
            mut line = "{at}:"
            for piece in s.Split(',') {
                line = "{line} {piece.Bytes.Count}"
            }

            print(line)
        }

        """;

    [Fact]
    public void A_search_of_one_byte_takes_the_first_of_two_and_reads_no_byte_past_the_count()
    {
        // X1, X9: `Find` takes the place of the byte from the mask of a word's test, whose lowest flag alone is sure, and reads the
        // first 16 bytes, the words after them and the last 16 bytes in words. A list of 0 to 40 bytes holds the byte at two places, the
        // second within 16 bytes of the first or at the end, and the byte after the first, where it stands before the second, is the
        // byte with its lowest bit turned, which the test flags as well when a borrow reaches it: the first place is the answer. Each
        // list, and a string that shares its block (T15), holds the byte only past its count, in the room 64 of them left behind
        // (`Clear` keeps the room, M9): a read past the count finds it there, so the answer is -1, and the string's `Split` at a comma
        // looks from each comma onward up to its end
        var expected = new List<string>();
        var near = 0;
        var far = 0;
        foreach (var t in new[] { 0, 35, 128, 255 })
        {
            for (var n = 0; n <= 40; n++)
            {
                expected.Add("-1");
                for (var q = 0; q < n; q++)
                {
                    foreach (var r in new[] { q + 1 + (((q * 7) + n) % 16), n - 1 })
                    {
                        if (r > q && r < n)
                        {
                            expected.Add(q.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            near += r - q < 8 ? 1 : 0;
                            far += q >= 16 ? 1 : 0;
                        }
                    }
                }
            }
        }

        for (var n = 0; n <= 40; n++)
        {
            var bytes = Enumerable.Range(0, n).Select(k => (byte)(((k * 37) + 5) % 13 < 2 ? ',' : 'a')).ToArray();
            var pieces = Encoding.ASCII.GetString(bytes).Split(',').Select(p => p.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            expected.Add($"{bytes.AsSpan().IndexOf((byte)',')}: {string.Join(' ', pieces)}");
        }

        // the floors: two places within one word of each other, and a first place past the first 16 bytes, each at least 500 times
        Assert.True(near >= 500 && far >= 500, $"{near}, {far}");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", FirstAndCount), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "search-first-and-count" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    private const string Pairs = """
        string Make(int length, ulong start, int letters) {
            mut ulong state = start
            mut b = Bytes()
            mut made = 0
            while made < length {
                state = state *% 6364136223846793005UL +% 1442695040888963407UL
                b.Add(byte(97UL + (state >> 33) % ulong(letters)))
                made = made + 1
            }

            return string.FromUtf8(b) else {
                InvalidUtf8 => ""
            }
        }

        mut ulong seed = 777UL
        mut n = 0
        while n < 6000 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            int letters = 2 + int((seed >> 10) % 3UL)
            int m = 2 + int((seed >> 20) % 34UL)
            int kind = int((seed >> 50) % 4UL)
            mut hay = Make(int((seed >> 40) % 220UL), seed, letters)
            mut pin = Make(m, seed ^ 99UL, letters)
            mut after = ""
            if kind == 1 && m <= hay.Bytes.Count {
                at = int((seed >> 30) % ulong(hay.Bytes.Count - m + 1))
                pin = hay.Substring(at, m)
            } else if kind == 2 {
                pin = "a{Make(m - 5, seed ^ 7UL, letters)}aaaa"
            } else if kind == 3 {
                cut = 1 + int((seed >> 30) % ulong(m - 1))
                hay = "{hay}{pin.Substring(0, cut)}"
                after = pin.Substring(cut, m - cut)
            }

            mut room = Bytes()
            room.AddRange(hay.Bytes)
            room.AddRange(after.Bytes)
            room.AddRange(pin.Bytes)
            room.Clear()
            room.AddRange(hay.Bytes)
            print("{hay.IndexOf(pin) ?? -1} {hay.Contains(pin)} {room.IndexOf(pin.Bytes) ?? -1}")
            n = n + 1
        }

        mut size = 2
        while size <= 35 {
            pin = Make(size, ulong(size), 4)
            head = pin.Substring(0, size - 1)
            mut zs = ""
            mut k = 0
            while k <= 200 {
                hay = "{zs}{head}"
                mut room = Bytes()
                room.AddRange(hay.Bytes)
                room.AddRange(pin.Substring(size - 1, 1).Bytes)
                room.Clear()
                room.AddRange(hay.Bytes)
                print("{room.IndexOf(pin.Bytes) ?? -1} {room.IndexOf(head.Bytes) ?? -1}")
                zs = "{zs}z"
                k = k + 1
            }

            size = size + 1
        }

        size = 2
        while size <= 16 {
            mut sep = "a"
            while sep.Bytes.Count < size - 1 {
                sep = "{sep}b"
            }

            sep = "{sep}a"
            mut cs = ""
            while cs.Bytes.Count <= 20 {
                mut j = 0
                while j <= 2 {
                    mut tail = ""
                    while tail.Bytes.Count < j {
                        tail = "{tail}c"
                    }

                    text = "x{sep}{sep.Substring(1, size - 1)}{cs}{sep}{tail}"
                    print(text.Split(sep).Count)
                    j = j + 1
                }

                cs = "{cs}c"
            }

            size = size + 1
        }

        """;

    /// <summary>The string <see cref="Pairs"/>'s <c>Make</c> makes: <paramref name="length"/> of the first <paramref name="letters"/> small letters, each picked by a step of the same generator, and none for a length below 0.</summary>
    private static string MakeLetters(int length, ulong start, int letters)
    {
        var state = start;
        var text = new StringBuilder();
        for (var k = 0; k < length; k++)
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            text.Append((char)('a' + (int)((state >> 33) % (ulong)letters)));
        }

        return text.ToString();
    }

    /// <summary>
    /// Where <c>PairFind</c> finds the first match of the needle, as its loops step: in a step of four vectors, in a step of one, in the
    /// vector that ends at the last byte, or in the walk of a string too short for a vector; or the two-way search, for a needle of more
    /// than 32 bytes and for one whose last four bytes are all its first; with the count of positions before the match, or before the end,
    /// whose two compared bytes are the needle's and whose other bytes are not, which the search checks and passes.
    /// </summary>
    private static (string Part, int Passed) PairPart(byte[] hay, byte[] pin)
    {
        var (n, m) = (hay.Length, pin.Length);
        var second = 1;
        if (m > 2)
        {
            var low = Math.Max(m - 4, 0);
            second = m - 1;
            while (second >= low && pin[second] == pin[0])
            {
                second--;
            }

            if (second < low)
            {
                return ("two-way", 0);
            }
        }

        if (m > 32)
        {
            return ("two-way", 0);
        }

        var at = hay.AsSpan().IndexOf(pin);
        var end = at < 0 ? n - m + 1 : at;
        var passed = 0;
        for (var i = 0; i < end; i++)
        {
            passed += hay[i] == pin[0] && hay[i + second] == pin[second] ? 1 : 0;
        }

        if (n < 16 + m - 1)
        {
            return ("short", passed);
        }

        var (four, one) = (0, 0);
        while (four + m - 1 + 64 < n)
        {
            four += 64;
        }

        one = four;
        while (one + m - 1 + 16 < n)
        {
            one += 16;
        }

        return (at < 0 ? "none" : at < four ? "four" : at < one ? "one" : "last", passed);
    }

    [Fact]
    public void A_needle_of_2_to_35_bytes_is_found_where_dotnet_finds_it_through_every_part_of_the_search_of_sse2_and_the_two_way_search()
    {
        var seed = 777UL;
        var expected = new List<string>();
        var parts = new Dictionary<string, int>();
        var (passed, twice, past) = (0, 0, 0);
        for (var n = 0; n < 6000; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var letters = 2 + (int)((seed >> 10) % 3UL);
            var m = 2 + (int)((seed >> 20) % 34UL);
            var kind = (int)((seed >> 50) % 4UL);
            var hay = MakeLetters((int)((seed >> 40) % 220UL), seed, letters);
            var pin = MakeLetters(m, seed ^ 99UL, letters);
            var after = "";
            if (kind == 1 && m <= hay.Length)
            {
                pin = hay.Substring((int)((seed >> 30) % (ulong)(hay.Length - m + 1)), m);
            }
            else if (kind == 2)
            {
                pin = $"a{MakeLetters(m - 5, seed ^ 7UL, letters)}aaaa";
            }
            else if (kind == 3)
            {
                var cut = 1 + (int)((seed >> 30) % (ulong)(m - 1));
                hay += pin[..cut];
                after = pin[cut..];
            }

            var (h, p) = (Encoding.ASCII.GetBytes(hay), Encoding.ASCII.GetBytes(pin));
            var at = h.AsSpan().IndexOf(p);
            expected.Add($"{at} {Text(at >= 0)} {at}");
            if (h.Length >= p.Length)
            {
                var (part, count) = PairPart(h, p);
                parts[part] = parts.GetValueOrDefault(part) + 1;
                passed += count > 0 ? 1 : 0;
                twice += at >= 0 && at != h.AsSpan().LastIndexOf(p) ? 1 : 0;
                past += kind == 3 && at < 0 && (hay + after).Contains(pin, StringComparison.Ordinal) ? 1 : 0;
            }
        }

        // every count of bytes before a needle's first bytes, its last past the end of the list, behind blocks that hold no position the
        // vectors would check, for every step's end; then a split, whose search after a separator starts where the byte before it is the
        // separator's last, and a needle that began there would be found before the start
        for (var size = 2; size <= 35; size++)
        {
            for (var k = 0; k <= 200; k++)
            {
                expected.Add($"-1 {k}");
            }
        }

        for (var size = 2; size <= 16; size++)
        {
            var sep = $"a{new string('b', size - 2)}a";
            for (var k = 0; k <= 20; k++)
            {
                for (var j = 0; j <= 2; j++)
                {
                    var text = $"x{sep}{sep[1..]}{new string('c', k)}{sep}{new string('c', j)}";
                    expected.Add($"{text.Split(sep).Length}");
                }
            }
        }

        // the floors: every part of the search finds a first match often, the short walk and the step of four miss often, the two-way
        // search is taken often, positions whose two compared bytes are the needle's are passed in many searches, many needles stand
        // twice, and many stand where the bytes after the end of the list would complete them
        Assert.True(
            parts.GetValueOrDefault("four") >= 300 && parts.GetValueOrDefault("one") >= 200 && parts.GetValueOrDefault("last") >= 100
                && parts.GetValueOrDefault("short") >= 200 && parts.GetValueOrDefault("none") >= 300 && parts.GetValueOrDefault("two-way") >= 300,
            string.Join(", ", parts.Select(p => $"{p.Key} {p.Value}")));
        Assert.True(passed >= 300, $"only {passed} searches pass a position whose two compared bytes are the needle's");
        Assert.True(twice >= 100, $"only {twice} needles stand twice");
        Assert.True(past >= 300, $"only {past} needles stand past the end of the list");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced"), (Plain(), "-plain") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", Pairs), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "pairs" + suffix));
            Assert.Equal((suffix, 0, ""), (suffix, code, error));
            Assert.Equal(expected, output);
        }
    }

    /// <summary>The item types whose <c>IndexOf</c> takes a search of their width, each with its values as Kurz and as C# write them; a list holds all but the last, which only the item looked for can be.</summary>
    private static readonly (string Type, int Width, string[] Kurz, object[] Values)[] Widths =
    [
        ("short", 2, ["short(0)", "short(1)", "short(-1)", "short(256)", "short(32767)", "short(-32768)", "short(255)"], [(short)0, (short)1, (short)-1, (short)256, short.MaxValue, short.MinValue, (short)255]),
        ("ushort", 2, ["ushort(0)", "ushort(1)", "ushort(65535)", "ushort(256)", "ushort(65280)"], [(ushort)0, (ushort)1, ushort.MaxValue, (ushort)256, (ushort)65280]),
        ("int", 4, ["0", "1", "-1", "65536", "2147483647", "-2147483647 - 1", "65537"], [0, 1, -1, 65536, int.MaxValue, int.MinValue, 65537]),
        ("uint", 4, ["uint(0)", "uint(1)", "uint(4294967295)", "uint(65536)", "uint(65537)"], [0u, 1u, uint.MaxValue, 65536u, 65537u]),
        ("char", 4, ["'a'", "'b'", "'ä'", "'€'", "'😀'"], [(int)'a', (int)'b', (int)'ä', (int)'€', char.ConvertToUtf32("😀", 0)]),
        ("long", 8, ["0L", "1L", "-1L", "4294967296L", "-4294967296L", "4294967297L", "30064771073L"], [0L, 1L, -1L, 4294967296L, -4294967296L, 4294967297L, 30064771073L]),
        ("ulong", 8, ["0UL", "1UL", "18446744073709551615UL", "4294967296UL", "4294967297UL"], [0UL, 1UL, ulong.MaxValue, 4294967296UL, 4294967297UL]),
    ];

    [Fact]
    public void A_list_of_items_of_2_4_and_8_bytes_gives_the_first_position_dotnet_gives_through_its_vectors_and_the_loop_after_them()
    {
        // 1200 lists of each type, of 0 to 79 items, each holding the item looked for in place of nothing past its count; the item
        // stands at a picked place, in a third of them among the last three, or in a fifth of the rest nowhere, and sometimes again after it; the other items are the type's other values, so that the
        // last `long`, with the low half of 1, and 4294967297, with that of 1, stand in lists that 1 is looked for in, and an 8-byte item
        // equal in one half alone is found nowhere
        var program = new StringBuilder();
        foreach (var (type, _, kurz, _) in Widths)
        {
            program.Append($$"""
                int In{{type}}(int count, ulong start, int wanted, int at, int again) {
                    values = [{{string.Join(", ", kurz)}}]
                    mut xs = List<{{type}}>()
                    for k in 0..<count + 40 {
                        xs.Add(values[wanted])
                    }

                    xs.Clear()
                    mut ulong state = start
                    for k in 0..<count {
                        state = state *% 6364136223846793005UL +% 1442695040888963407UL
                        mut int pick = int((state >> 33) % ulong(values.Count - 1))
                        if pick >= wanted {
                            pick = pick + 1
                        }

                        if k == at || k == again {
                            pick = wanted
                        }

                        xs.Add(values[pick])
                    }

                    return xs.IndexOf(values[wanted]) ?? -1
                }


                """);
        }

        program.Append("""
            mut ulong seed = 4242UL
            for n in 0..<1200 {
                seed = seed *% 2862933555777941757UL +% 3037000493UL
                count = int((seed >> 40) % 80UL)
                mut at = int((seed >> 30) % ulong(count + 1 + count / 4))
                near = int((seed >> 35) % 3UL)
                if (seed >> 55) % 3UL == 0UL && near < count {
                    at = count - 1 - near
                }

                again = at + 1 + int((seed >> 45) % 7UL)

            """);
        foreach (var (type, _, kurz, _) in Widths)
        {
            program.Append($"    print(In{type}(count, seed, int((seed >> 20) % {kurz.Length}UL), at, again))\n");
        }

        program.Append("}\n");
        var expected = new List<string>();
        var parts = new Dictionary<(int Width, string Part), int>();
        var halves = 0;
        var seed = 4242UL;
        for (var n = 0; n < 1200; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var count = (int)((seed >> 40) % 80UL);
            var at = (int)((seed >> 30) % (ulong)(count + 1 + (count / 4)));
            var near = (int)((seed >> 35) % 3UL);
            if ((seed >> 55) % 3UL == 0UL && near < count)
            {
                at = count - 1 - near;
            }

            var again = at + 1 + (int)((seed >> 45) % 7UL);
            foreach (var (_, width, kurz, values) in Widths)
            {
                var wanted = (int)((seed >> 20) % (ulong)kurz.Length);
                var state = seed;
                var items = new List<object>();
                for (var k = 0; k < count; k++)
                {
                    state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
                    var pick = (int)((state >> 33) % (ulong)(values.Length - 1));
                    pick += pick >= wanted ? 1 : 0;
                    items.Add(values[k == at || k == again ? wanted : pick]);
                }

                var found = items.IndexOf(values[wanted]);
                expected.Add(found.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var four = count / (64 / width) * (64 / width);
                var one = four + ((count - four) / (16 / width) * (16 / width));
                var part = count < 64 / width ? "few" : found < 0 ? "none" : found < four ? "four" : found < one ? "one" : "loop";
                parts[(width, part)] = parts.GetValueOrDefault((width, part)) + 1;
                if (width == 8)
                {
                    var low = Convert.ToUInt64(values[wanted] is long l ? unchecked((ulong)l) : values[wanted]) & 0xFFFFFFFF;
                    halves += items.Take(found < 0 ? count : found).Any(i => (Convert.ToUInt64(i is long x ? unchecked((ulong)x) : i) & 0xFFFFFFFF) == low) ? 1 : 0;
                }
            }
        }

        // the floors: for each width, lists shorter than a step of four vectors, which the emitted loop searches, and in longer ones
        // items found in a step of four vectors, of one vector and in the loop after them, and items in no list; and lists of 8-byte
        // items holding, before the item looked for or where it is in none, one equal to it in its low half
        foreach (var width in new[] { 2, 4, 8 })
        {
            foreach (var part in new[] { "few", "four", "one", "loop", "none" })
            {
                Assert.True(parts.GetValueOrDefault((width, part)) >= 50, $"only {parts.GetValueOrDefault((width, part))} items of {width} bytes in the part {part}");
            }
        }

        Assert.True(halves >= 100, $"only {halves} lists of 8-byte items hold an item equal to the one looked for in its low half");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced"), (Plain(), "-plain") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));

            // each type's `IndexOf` calls the search of its width, for a list of at least the items of a step of four vectors
            var ir = compilation.Program.Ir;
            Assert.Equal(2, Regex.Count(ir, @"call i64 @""?std\.FindShort\."));
            Assert.Equal(3, Regex.Count(ir, @"call i64 @""?std\.FindInt\."));
            Assert.Equal(2, Regex.Count(ir, @"call i64 @""?std\.FindLong\."));
            Assert.Equal(
                [8, 8, 16, 16, 16, 32, 32],
                Regex.Matches(ir, @"icmp slt i32 %[\w.]+, (\d+)[^\n]*\n\s*br i1 %[\w.]+, label %few\.").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Order());
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "items" + suffix));
            Assert.Equal((suffix, 0, ""), (suffix, code, error));
            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void On_a_target_without_sse2_find_takes_the_two_way_search_and_a_list_search_holds_no_vector()
    {
        // a search of a string and a list of each type whose `IndexOf` takes a search of its width, built for x86-64, where each
        // guard's test of SSE2 is true, and for a 64-bit Arm machine, where it is false and the code it guards is left out (R18)
        const string text = """
            s = "a needle in a haystack of bytes, a needle"
            print(s.Contains("haystack"))
            shorts = [short(1), short(2)]
            print(shorts.IndexOf(short(2)) ?? -1)
            ushorts = [ushort(1), ushort(2)]
            print(ushorts.IndexOf(ushort(2)) ?? -1)
            ints = [1, 2]
            print(ints.IndexOf(2) ?? -1)
            uints = [uint(1), uint(2)]
            print(uints.IndexOf(uint(2)) ?? -1)
            chars = ['a', 'b']
            print(chars.IndexOf('b') ?? -1)
            longs = [1L, 2L]
            print(longs.IndexOf(2L) ?? -1)
            ulongs = [1UL, 2UL]
            print(ulongs.IndexOf(2UL) ?? -1)

            """;
        foreach (var (cpu, sse2) in new[] { ((Kurz.Compiler.Emit.Cpu?)null, true), (Kurz.Compiler.Emit.Cpu.ArmBaseline, false) })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true, cpu: cpu is null ? null : () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var ir = compilation.Program.Ir;
            Assert.Matches(@"call i64 @""?std\.TwoWay\.", ir);
            Assert.Equal(sse2, Regex.IsMatch(ir, @"call i64 @""?std\.PairFind\."));
            foreach (var search in new[] { "FindShort", "FindInt", "FindLong" })
            {
                var body = Regex.Match(ir, $@"^define [^\n]*@""?std\.{search}\.[^\n]*\n(.*?)\n}}", RegexOptions.Multiline | RegexOptions.Singleline);
                Assert.True(body.Success, $"no {search} in the IR");
                Assert.Equal((search, sse2), (search, Regex.IsMatch(body.Groups[1].Value, @"<\d+ x i\d+>")));
            }
        }
    }
}
