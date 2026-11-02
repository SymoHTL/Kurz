using System.Globalization;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the sorts of a list (X8) that no corpus case pins: 300 lists of each of five item types, which the program and this
/// class generate alike, give through <c>Sort</c>, <c>SortUnstable</c> and <c>SortBy</c> what .NET's <c>OrderBy</c>, a stable sort, gives
/// with the order of <c>Comparer&lt;T&gt;.Default</c>: <c>int</c> and <c>byte</c> items, <c>double</c> items of a pool that holds NaN,
/// <c>-0</c> and <c>0</c>, which <c>double.CompareTo</c> orders NaN first and <c>-0</c> beside <c>0</c>, strings of letters of one to four
/// bytes in the order of their UTF-8 bytes (T32), <c>int?</c> items, <c>null</c> first, and keys of <c>int</c>, <c>string</c>,
/// <c>double</c>, <c>ulong</c>, <c>byte</c>, an enum by its numbers and <c>duration</c>; an unstable sort of <c>double</c> items is held to
/// the stable one with <c>-0</c> read as <c>0</c>, the one pair of equal items whose texts differ; and the same under the allocator that
/// makes every block it frees inaccessible, for the first 50 rounds. Lists of up to 64,000 items of eight shapes, runs that rise and that
/// fall among them, and one of 1,100,000, past the scratch that holds every item, sort as .NET's <c>OrderBy</c> sorts them, and lists of
/// <c>decimal</c> items whose equal values print apart keep their order. A list whose quicksort's pivots were picked against it, as
/// McIlroy's adversary picks them, reaches the heapsort of <c>SortUnstable</c>, and one picked against the stable quicksort reaches the
/// driftsort that <c>Sort</c> falls back on; both are sorted.
/// </summary>
public sealed class SortTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Rounds = 300;

    /// <summary>The rounds the run under the allocator that makes every freed block inaccessible takes, which keeps the pages of every block it frees (<see cref="ParseTests"/>).</summary>
    private const int FencedRounds = 50;

    /// <summary>The most items for which the emitter's sorts take insertion alone, its <c>InsertionOnly</c>.</summary>
    private const int InsertionOnly = 20;

    /// <summary>The most items of the emitter's small sorts, its <c>SmallSortMost</c>; the stable sort finds its runs eagerly up to twice as many.</summary>
    private const int SmallSortMost = 32;

    private static readonly double[] Pool = [double.NaN, -0.0, 0.0, 1.5, -2.25, 3.0, 0.1];

    private static readonly string[] Letters = ["a", "B", "ä", "€", "😀"];

    private const string Program = """
        ulong Step(ulong state) => state *% 6364136223846793005UL +% 1442695040888963407UL

        List<int> Ints(int length, ulong start, int span) {
            mut ulong state = start
            mut xs = List<int>()
            while xs.Count < length {
                state = Step(state)
                xs.Add(int((state >> 33) % ulong(span)) - span / 2)
            }

            return xs
        }

        List<double> Doubles(List<double> values, int length, ulong start) {
            mut ulong state = start
            mut xs = List<double>()
            while xs.Count < length {
                state = Step(state)
                xs.Add(values[int((state >> 33) % 7UL)])
            }

            return xs
        }

        List<string> Strings(List<string> alphabet, int length, ulong start) {
            mut ulong state = start
            mut xs = List<string>()
            while xs.Count < length {
                state = Step(state)
                mut text = ""
                mut left = int((state >> 20) % 4UL)
                while left > 0 {
                    state = Step(state)
                    text = "{text}{alphabet[int((state >> 33) % 5UL)]}"
                    left = left - 1
                }

                xs.Add(text)
            }

            return xs
        }

        List<int?> Nullables(int length, ulong start) {
            mut ulong state = start
            mut List<int?> xs = []
            while xs.Count < length {
                state = Step(state)
                r = int((state >> 33) % 9UL)
                if r == 0 {
                    xs.Add(null)
                } else {
                    xs.Add(r - 4)
                }
            }

            return xs
        }

        List<byte> Octets(int length, ulong start) {
            mut ulong state = start
            mut xs = List<byte>()
            while xs.Count < length {
                state = Step(state)
                xs.Add(byte((state >> 33) % 256UL))
            }

            return xs
        }

        enum Level { High = 5, Low = -1, Mid = 0 }

        double z = 0.0
        pool = [z / z, -0.0, 0.0, 1.5, -2.25, 3.0, 0.1]
        levels = [Level.High, Level.Low, Level.Mid]
        waits = [-1s, 1s, 1min, -1h]
        letters = ["a", "B", "ä", "€", "😀"]
        mut ulong seed = 13579UL
        mut n = 0
        while n < 300 {
            seed = seed *% 2862933555777941757UL +% 3037000493UL
            xs = Ints(int((seed >> 40) % 400UL), seed, 2 + int((seed >> 10) % 80UL))
            mut a = xs
            a.Sort()
            print(a)
            mut b = xs
            b.SortUnstable()
            print(b)
            mut c = xs
            c.SortBy(x => x % 5)
            print(c)
            mut d = xs
            d.SortBy(x => "{x % 7}")
            print(d)
            mut e = xs
            e.SortBy(x => pool[(x % 7 + 7) % 7])
            print(e)
            mut u = xs
            u.SortBy(x => ulong(x + 100) *% 11400714819323198485UL)
            print(u)
            mut v = xs
            v.SortBy(x => byte(x + 100))
            print(v)
            mut w = xs
            w.SortBy(x => levels[(x % 3 + 3) % 3])
            print(w)
            mut y = xs
            y.SortBy(x => waits[(x % 4 + 4) % 4])
            print(y)
            print(xs)
            ds = Doubles(pool, int((seed >> 20) % 150UL), seed ^ 7UL)
            mut f = ds
            f.Sort()
            print(f)
            mut g = ds
            g.SortUnstable()
            print(g)
            ss = Strings(letters, int((seed >> 30) % 150UL), seed ^ 11UL)
            mut h = ss
            h.Sort()
            print(h)
            mut k = ss
            k.SortUnstable()
            print(k)
            ns = Nullables(int((seed >> 50) % 150UL), seed ^ 13UL)
            mut m = ns
            m.Sort()
            print(m)
            mut q = ns
            q.SortUnstable()
            print(q)
            bs = Octets(int((seed >> 45) % 150UL), seed ^ 17UL)
            mut r = bs
            r.Sort()
            print(r)
            mut t = bs
            t.SortUnstable()
            print(t)
            n = n + 1
        }

        """;

    /// <summary>The lines each round prints.</summary>
    private const int Lines = 18;

    /// <summary>The numbers of <c>Level.High</c>, <c>Level.Low</c> and <c>Level.Mid</c>, and the seconds of the durations, of the program's keys.</summary>
    private static readonly int[] Levels = [5, -1, 0];

    private static readonly int[] Waits = [-1, 1, 60, -3600];

    private static ulong Step(ulong state) => unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);

    /// <summary>The text of a list, as <c>print</c> writes it (A4).</summary>
    private static string Text<T>(IEnumerable<T> items, Func<T, string> text) => "[" + string.Join(", ", items.Select(text)) + "]";

    private static string Number(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>T32: two strings in the order of their UTF-8 bytes.</summary>
    private static readonly Comparer<string> Utf8 = Comparer<string>.Create((x, y) => Encoding.UTF8.GetBytes(x).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(y)));

    [Fact]
    public void Every_sort_of_generated_lists_gives_what_dotnet_gives()
    {
        var seed = 13579UL;

        // each line, and whether it is held with -0 read as 0
        var expected = new List<(string Text, bool AsZero)>();
        var (pastRun, pastEager, zeros, nulls, equalStrings) = (0, 0, 0, 0, 0);
        for (var n = 0; n < Rounds; n++)
        {
            seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
            var length = (int)((seed >> 40) % 400UL);
            var span = 2 + (int)((seed >> 10) % 80UL);
            var state = seed;
            var xs = new List<int>();
            while (xs.Count < length)
            {
                state = Step(state);
                xs.Add((int)((state >> 33) % (ulong)span) - (span / 2));
            }

            string Ints(IEnumerable<int> items) => Text(items, i => i.ToString(CultureInfo.InvariantCulture));
            var sorted = Ints(xs.OrderBy(x => x));
            expected.Add((sorted, false));
            expected.Add((sorted, false)); // equal `int`s are alike, so an unstable sort prints what a stable one does
            expected.Add((Ints(xs.OrderBy(x => x % 5)), false));
            expected.Add((Ints(xs.OrderBy(x => (x % 7).ToString(CultureInfo.InvariantCulture), Utf8)), false));
            expected.Add((Ints(xs.OrderBy(x => Pool[((x % 7) + 7) % 7])), false));
            expected.Add((Ints(xs.OrderBy(x => unchecked((ulong)(x + 100) * 11400714819323198485UL))), false));
            expected.Add((Ints(xs.OrderBy(x => (byte)(x + 100))), false));
            expected.Add((Ints(xs.OrderBy(x => Levels[((x % 3) + 3) % 3])), false));
            expected.Add((Ints(xs.OrderBy(x => Waits[((x % 4) + 4) % 4])), false));
            expected.Add((Ints(xs), false)); // the list the sorts copied keeps its order (M3)
            pastRun += xs.Count > InsertionOnly ? 1 : 0;
            pastEager += xs.Count > 2 * SmallSortMost ? 1 : 0;

            state = seed ^ 7UL;
            var ds = new List<double>();
            for (var count = (int)((seed >> 20) % 150UL); ds.Count < count;)
            {
                state = Step(state);
                ds.Add(Pool[(int)((state >> 33) % 7UL)]);
            }

            var doubles = Text(ds.OrderBy(d => d), Number);
            expected.Add((doubles, false));
            expected.Add((doubles, true));
            var zero = ds.FindIndex(d => d == 0 && !double.IsNegative(d));
            var negativeZero = ds.FindLastIndex(d => d == 0 && double.IsNegative(d));
            zeros += ds.Count > InsertionOnly && ds.Any(double.IsNaN) && zero >= 0 && zero < negativeZero ? 1 : 0;

            state = seed ^ 11UL;
            var ss = new List<string>();
            for (var count = (int)((seed >> 30) % 150UL); ss.Count < count;)
            {
                state = Step(state);
                var text = new StringBuilder();
                for (var letters = (int)((state >> 20) % 4UL); letters > 0; letters--)
                {
                    state = Step(state);
                    text.Append(Letters[(int)((state >> 33) % 5UL)]);
                }

                ss.Add(text.ToString());
            }

            var strings = Text(ss.OrderBy(s => s, Utf8), s => s);
            expected.Add((strings, false));
            expected.Add((strings, false)); // equal strings are alike
            equalStrings += ss.Count > InsertionOnly && ss.Where(s => new StringInfo(s).LengthInTextElements > 1).GroupBy(s => s, StringComparer.Ordinal).Any(g => g.Count() > 1) ? 1 : 0;

            state = seed ^ 13UL;
            var ns = new List<int?>();
            for (var count = (int)((seed >> 50) % 150UL); ns.Count < count;)
            {
                state = Step(state);
                var r = (int)((state >> 33) % 9UL);
                ns.Add(r == 0 ? null : r - 4);
            }

            var nullables = Text(ns.OrderBy(x => x), x => x?.ToString(CultureInfo.InvariantCulture) ?? "null");
            expected.Add((nullables, false));
            expected.Add((nullables, false));
            nulls += ns.Count > InsertionOnly && ns.Contains(null) && ns.IndexOf(null) > 0 ? 1 : 0;

            state = seed ^ 17UL;
            var bs = new List<byte>();
            for (var count = (int)((seed >> 45) % 150UL); bs.Count < count;)
            {
                state = Step(state);
                bs.Add((byte)((state >> 33) % 256UL));
            }

            var bytes = Text(bs.OrderBy(b => b), b => b.ToString(CultureInfo.InvariantCulture));
            expected.Add((bytes, false));
            expected.Add((bytes, false));
        }

        Assert.Equal(Rounds * Lines, expected.Count);

        // the floors: lists past the insertion alone, past the runs the stable sort finds eagerly, `double` lists whose `0` stands before
        // a `-0` beside NaN, string lists with equal items among items of more than one letter, and `int?` lists with a `null` after
        // their first item
        Assert.True(pastRun >= 200, $"only {pastRun} lists of `int` are longer than {InsertionOnly} items");
        Assert.True(pastEager >= 200, $"only {pastEager} lists of `int` are longer than {2 * SmallSortMost} items");
        Assert.True(zeros >= 100, $"only {zeros} lists of `double` hold NaN and a `0` before a `-0`");
        Assert.True(equalStrings >= 100, $"only {equalStrings} lists of strings hold equal items of more than one letter");
        Assert.True(nulls >= 100, $"only {nulls} lists of `int?` hold a `null` after their first item");
        foreach (var (runtime, suffix, rounds) in new[] { (toolchain.Runtime, "", Rounds), (ListTests.Fenced, "-fenced", FencedRounds) })
        {
            var program = Program.Replace($"while n < {Rounds} {{", $"while n < {rounds} {{", StringComparison.Ordinal);
            Assert.Contains($"while n < {rounds} {{", program, StringComparison.Ordinal);
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "sorts" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(rounds * Lines, output.Count);
            for (var i = 0; i < output.Count; i++)
            {
                var (text, asZero) = expected[i];
                Assert.Equal(asZero ? AsZero(text) : text, asZero ? AsZero(output[i]) : output[i]);
            }
        }
    }

    /// <summary>The text of a list of <c>double</c>s with each <c>-0</c> written as <c>0</c>.</summary>
    private static string AsZero(string text) => text.Replace("[-0,", "[0,", StringComparison.Ordinal).Replace(" -0,", " 0,", StringComparison.Ordinal).Replace(" -0]", " 0]", StringComparison.Ordinal).Replace("[-0]", "[0]", StringComparison.Ordinal);

    /// <summary>The program of the shapes: lists of <c>double</c> items made from integers, a <c>0</c> written as <c>-0</c> or <c>0</c> by a bit of the generator, and lists of <c>decimal</c> items whose equal values print apart; each sorted list printed as a hash of its items.</summary>
    private const string Shapes = """
        ulong Step(ulong state) => state *% 6364136223846793005UL +% 1442695040888963407UL

        ulong Fnv(ulong hash, ulong code) => (hash ^ code) *% 1099511628211UL

        ulong Hashed(List<double> xs, bool zeros) {
            mut ulong hash = 14695981039346656037UL
            for x in xs {
                mut ulong code = ulong(int(x) + 1000000)
                if zeros && x == 0.0 && 1.0 / x < 0.0 {
                    code = 1UL
                }

                hash = Fnv(hash, code)
            }

            return hash
        }

        ulong Texts(List<decimal?> xs) {
            mut ulong hash = 14695981039346656037UL
            for x in xs {
                for b in "{x}".Bytes {
                    hash = Fnv(hash, ulong(b))
                }

                hash = Fnv(hash, 44UL)
            }

            return hash
        }

        ulong Plain(List<decimal> xs) {
            mut ulong hash = 14695981039346656037UL
            for x in xs {
                for b in "{x}".Bytes {
                    hash = Fnv(hash, ulong(b))
                }

                hash = Fnv(hash, 44UL)
            }

            return hash
        }

        List<double> Shape(int shape, int length, ulong start, int span) {
            mut ulong state = start
            mut xs = List<double>()
            while xs.Count < length {
                state = Step(state)
                i = xs.Count
                r = int((state >> 33) % ulong(span))
                mut value = r - span / 2
                if shape == 1 {
                    value = (i % span) / 2 - span / 4
                } else if shape == 2 {
                    value = span / 2 - i % span
                } else if shape == 3 {
                    value = span / 4 - (i % span) / 2
                } else if shape == 4 {
                    value = i / 2 - length / 4
                } else if shape == 5 {
                    value = length / 2 - i
                } else if shape == 6 {
                    value = r % 3 - 1
                } else if shape == 7 && r % 4 != 0 {
                    value = i / 3 - length / 6
                }

                mut x = double(value)
                if value == 0 && (state >> 20) % 2UL == 1UL {
                    x = -0.0
                }

                xs.Add(x)
            }

            return xs
        }

        void Report(List<double> xs) {
            mut a = xs
            a.Sort()
            mut b = xs
            b.SortUnstable()
            mut c = xs
            c.SortBy(x => int(x) % 5)
            mut d = xs
            d.SortBy(x => "{int(x) % 7}")
            print("{xs.Count} {Hashed(a, true)} {Hashed(b, false)} {Hashed(c, true)} {Hashed(d, true)}")
        }

        void Run(int rounds, int big) {
            mut ulong seed = 24680UL
            for n in 0..<rounds {
                seed = seed *% 2862933555777941757UL +% 3037000493UL
                r = int((seed >> 33) % 60000UL)
                mut length = 21 + r % 44
                if n % 4 == 1 {
                    length = 65 + r % 448
                } else if n % 4 == 2 {
                    length = 513 + r % 3584
                } else if n % 4 == 3 {
                    length = 4097 + r
                }

                Report(Shape((n / 4) % 8, length, seed, 2 + int((seed >> 10) % 300UL)))
            }

            if big > 0 {
                Report(Shape(0, big, 97531UL, 1000))
            }

            mut List<double> falling = [-0.0, 0.0]
            for k in 1..<201 {
                falling.Add(double(-k))
            }

            Report(falling)

            List<decimal?> pool = [1.5m, 1.50m, null, -3.25m, -3.250m, 2m, 2.0m, 0.1m]
            List<decimal> plain = [1.5m, 1.50m, 0m, -3.25m, -3.250m, 2m, 2.0m, 0.1m]
            for m in 0..<rounds / 4 {
                seed = seed *% 2862933555777941757UL +% 3037000493UL
                r = int((seed >> 33) % 60000UL)
                mut length = 21 + r % 44
                if m % 3 == 1 {
                    length = 65 + r % 106
                } else if m % 3 == 2 {
                    length = 171 + r % 2830
                }

                mut List<decimal?> ds = []
                mut List<decimal> es = []
                mut ulong state = seed
                while ds.Count < length {
                    state = Step(state)
                    k = int((state >> 33) % 8UL)
                    ds.Add(pool[k])
                    es.Add(plain[k])
                }

                mut e = ds
                e.Sort()
                mut f = es
                f.Sort()
                mut g = ds
                g.SortBy(x => x)
                print("{ds.Count} {Texts(e)} {Plain(f)} {Texts(g)}")
            }
        }

        Run(64, 1100000)

        """;

    /// <summary>The texts of the program's <c>decimal?</c> pool, the order of its values with <c>null</c> first, and the texts of its pool without <c>null</c>, where <c>0</c> stands in its place.</summary>
    private static readonly string[] Amounts = ["1.5", "1.50", "null", "-3.25", "-3.250", "2", "2.0", "0.1"];

    private static readonly decimal?[] AmountOrder = [1.5m, 1.5m, null, -3.25m, -3.25m, 2m, 2m, 0.1m];

    private static readonly string[] PlainAmounts = ["1.5", "1.50", "0", "-3.25", "-3.250", "2", "2.0", "0.1"];

    /// <summary>
    /// X8: 64 lists of <c>double</c> items, of eight shapes and of four ranges of length, and one of 1,100,000 items, give through
    /// <c>Sort</c>, <c>SortUnstable</c> and <c>SortBy</c> by an <c>int</c> key and by a <c>string</c> key what .NET's <c>OrderBy</c> gives,
    /// the unstable sort with <c>-0</c> read as <c>0</c>, and 16 lists of <c>decimal?</c> and <c>decimal</c> items, whose values 1.5 and
    /// 1.50 are equal and print apart, keep their order through <c>Sort</c> and <c>SortBy</c>; each list is held by a hash of its items.
    /// The ranges reach the stable sort's eager runs (up to 64 items), its scratch on the stack (512 <c>double</c> items, 170 <c>decimal?</c>
    /// items), a scratch from the allocator, and the least length of a run found that grows with the square root of the count (past 4096
    /// items); the shapes runs that rise and that fall, each value twice or once, a list that is one run either way, three values only, and
    /// a list that rises with other items among its own; the list of 1,100,000 items is past the 1,000,000 <c>double</c> items of the
    /// scratch that holds every item, so that a merge does not fit it. A list of 202 items that falls after a <c>-0</c> and a <c>0</c>
    /// keeps the two in their order, as Rust's <c>find_existing_run</c> starts a run that falls only where the second item sorts before the
    /// first, so that the run it reverses holds no two equal items. The same, without the long list and for the first 16 lists, under
    /// the allocator that makes every block it frees inaccessible.
    /// </summary>
    [Fact]
    public void Lists_of_every_shape_and_length_sort_as_dotnet_sorts_them()
    {
        static ulong Fnv(ulong hash, ulong code) => unchecked((hash ^ code) * 1099511628211UL);
        static ulong Hashed(IEnumerable<double> xs, bool zeros) => xs.Aggregate(14695981039346656037UL, (hash, x) => Fnv(hash, zeros && x == 0 && double.IsNegative(x) ? 1UL : (ulong)((int)x + 1000000)));
        static ulong Texts(IEnumerable<string> texts) => texts.Aggregate(14695981039346656037UL, (hash, text) => Fnv(Encoding.UTF8.GetBytes(text).Aggregate(hash, (h, b) => Fnv(h, b)), 44UL));
        string Report(List<double> xs) =>
            string.Join(' ', xs.Count, Hashed(xs.OrderBy(x => x), true), Hashed(xs.OrderBy(x => x), false), Hashed(xs.OrderBy(x => (int)x % 5), true),
                Hashed(xs.OrderBy(x => ((int)x % 7).ToString(CultureInfo.InvariantCulture), Utf8), true));

        var decimals = new List<(int Count, bool Twins)>();
        List<string> Expected(int rounds, int big, Action<List<double>, int> seen)
        {
            var lines = new List<string>();
            var seed = 24680UL;
            for (var n = 0; n < rounds; n++)
            {
                seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
                var r = (int)((seed >> 33) % 60000UL);
                var length = (n % 4) switch { 1 => 65 + (r % 448), 2 => 513 + (r % 3584), 3 => 4097 + r, _ => 21 + (r % 44) };
                var span = 2 + (int)((seed >> 10) % 300UL);
                var xs = Shape(n / 4 % 8, length, seed, span);
                seen(xs, n / 4 % 8 is 1 or 2 ? span : 0);
                lines.Add(Report(xs));
            }

            if (big > 0)
            {
                var xs = Shape(0, big, 97531UL, 1000);
                seen(xs, 0);
                lines.Add(Report(xs));
            }

            var falling = new List<double> { -0.0, 0.0 };
            falling.AddRange(Enumerable.Range(1, 200).Select(k => (double)-k));
            lines.Add(Report(falling));

            for (var m = 0; m < rounds / 4; m++)
            {
                seed = unchecked((seed * 2862933555777941757UL) + 3037000493UL);
                var r = (int)((seed >> 33) % 60000UL);
                var length = (m % 3) switch { 1 => 65 + (r % 106), 2 => 171 + (r % 2830), _ => 21 + (r % 44) };
                var state = seed;
                var picks = new List<int>();
                while (picks.Count < length)
                {
                    state = Step(state);
                    picks.Add((int)((state >> 33) % 8UL));
                }

                var sorted = picks.OrderBy(k => AmountOrder[k]).ToList();
                lines.Add(string.Join(' ', picks.Count, Texts(sorted.Select(k => Amounts[k])), Texts(picks.OrderBy(k => AmountOrder[k] ?? 0m).Select(k => PlainAmounts[k])), Texts(sorted.Select(k => Amounts[k]))));
                decimals.Add((picks.Count, picks.Contains(0) && picks.Contains(1)));
            }

            return lines;
        }

        // the floors: lists past the eager runs, past the stack's scratch, past the length from which the least run grows with the root,
        // lists that hold both zeros, lists of runs that rise or fall at least as long as the least run found, the list past the scratch
        // of every item, and `decimal` lists past their stack's scratch that hold 1.5 and 1.50
        var (pastEager, pastStack, pastRoot, bothZeros, longRuns, pastWhole) = (0, 0, 0, 0, 0, 0);
        var expected = Expected(64, 1_100_000, (xs, period) =>
        {
            pastEager += xs.Count > 2 * SmallSortMost ? 1 : 0;
            pastStack += xs.Count > 4096 / 8 ? 1 : 0;
            pastRoot += xs.Count > 4096 ? 1 : 0;
            bothZeros += xs.Any(x => x == 0 && double.IsNegative(x)) && xs.Any(x => x == 0 && !double.IsNegative(x)) ? 1 : 0;
            longRuns += period >= LeastRun(xs.Count) ? 1 : 0;
            pastWhole += xs.Count > 8_000_000 / 8 ? 1 : 0;
        });
        Assert.Equal(64 + 1 + 1 + 16, expected.Count);
        Assert.True(pastEager >= 48, $"only {pastEager} lists are longer than {2 * SmallSortMost} items");
        Assert.True(pastStack >= 32, $"only {pastStack} lists are longer than 512 items");
        Assert.True(pastRoot >= 16, $"only {pastRoot} lists are longer than 4096 items");
        Assert.True(bothZeros >= 40, $"only {bothZeros} lists hold both -0 and 0");
        Assert.True(longRuns >= 8, $"only {longRuns} lists of runs hold runs as long as the least run found");
        Assert.Equal(1, pastWhole);
        Assert.True(decimals.Count(d => d.Count > 4096 / 24 && d.Twins) >= 4, "too few `decimal` lists past their stack's scratch hold 1.5 and 1.50");
        foreach (var (runtime, suffix, rounds, big) in new[] { (toolchain.Runtime, "", 64, 1_100_000), (ListTests.Fenced, "-fenced", 16, 0) })
        {
            var program = Shapes.Replace("Run(64, 1100000)", $"Run({rounds}, {big})", StringComparison.Ordinal);
            Assert.Contains($"Run({rounds}, {big})", program, StringComparison.Ordinal);
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "sort-shapes" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(rounds == 64 ? expected : Expected(rounds, big, (_, _) => { }), output);
        }
    }

    /// <summary>The program's shape <paramref name="shape"/> of <paramref name="length"/> items.</summary>
    private static List<double> Shape(int shape, int length, ulong start, int span)
    {
        var state = start;
        var xs = new List<double>();
        while (xs.Count < length)
        {
            state = Step(state);
            var i = xs.Count;
            var r = (int)((state >> 33) % (ulong)span);
            var value = shape switch
            {
                1 => (i % span / 2) - (span / 4),
                2 => (span / 2) - (i % span),
                3 => (span / 4) - (i % span / 2),
                4 => (i / 2) - (length / 4),
                5 => (length / 2) - i,
                6 => (r % 3) - 1,
                7 when r % 4 != 0 => (i / 3) - (length / 6),
                _ => r - (span / 2),
            };
            xs.Add(value == 0 && (state >> 20) % 2UL == 1UL ? -0.0 : value);
        }

        return xs;
    }

    /// <summary>The least length of a run the stable sort keeps as found, as Rust's <c>drift::sort</c> takes it: up to 4096 items half the count, rounded up, and at most 64, past them about its square root.</summary>
    private static int LeastRun(int n)
    {
        if (n <= 4096)
        {
            return Math.Min(n - (n / 2), 64);
        }

        var shift = (1 + int.Log2(n | 1)) / 2;
        return ((1 << shift) + (n >> shift)) / 2;
    }

    /// <summary>
    /// X8: a list of 1000 items built against <c>SortUnstable</c>'s quicksort by McIlroy's adversary (<i>A Killer Adversary for
    /// Quicksort</i>, 1999), which answers each comparison so that the pivot falls near an end, takes more splits than the bound the
    /// emitter gives the quicksort, so its last side is sorted by the heapsort, and the list is sorted. Its second item is made the least
    /// before the sort, so that ipnsort finds a run of two where the list begins and does not take the whole list for one run; the
    /// adversary runs against a copy of the emitter's ipnsort written here, comparison for comparison, which the list is replayed through
    /// to show that the run and the bound are as said.
    /// </summary>
    [Fact]
    public void A_list_built_against_the_quicksorts_pivots_is_sorted_by_its_heapsort()
    {
        const int count = 1000;
        var (adversary, values) = McIlroy(count, [1]);
        var heaps = 0;
        Assert.Equal(2, Unstable(Enumerable.Range(0, count).ToArray(), adversary, ref heaps));
        Assert.True(heaps > 0, "the adversary's list took no heapsort");

        // the list replayed through the copy, compared as numbers: the same comparisons, so the same heapsort
        var replay = values.ToArray();
        var replayed = 0;
        Assert.Equal(2, Unstable(replay, (x, y) => x < y, ref replayed));
        Assert.True(replayed > 0, "the list replayed took no heapsort");
        var sorted = values.Order().ToArray();
        Assert.Equal(sorted, replay);

        // and an order of random items takes none, the positive control of the count
        var random = Enumerable.Range(0, count).Select(i => (int)((uint)(i * 2654435761) % 997)).ToArray();
        var randomHeaps = 0;
        Assert.True(Unstable(random, (x, y) => x < y, ref randomHeaps) < count, "the random list is one run");
        Assert.Equal(0, randomHeaps);

        var program = $"mut xs = [{string.Join(", ", values)}]\nxs.SortUnstable()\nprint(xs)\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "sort-adversary"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal([Text(sorted, v => v.ToString(CultureInfo.InvariantCulture))], output);
    }

    /// <summary>
    /// X8: a list of 1000 items built against <c>Sort</c>'s stable quicksort by McIlroy's adversary takes more splits than the bound the
    /// emitter gives that quicksort, so its last side is sorted by driftsort's eager form, as Rust's <c>sort</c> falls back on it, and the
    /// list is sorted. Its second item of each 64 is made the least before the sort, so that driftsort finds a run of two at each and keeps
    /// the 64 items unsorted, and the stable quicksort takes the whole list; the adversary runs against a copy of those two steps written
    /// here, comparison for comparison, which the list is replayed through to show that the runs and the bound are as said.
    /// </summary>
    [Fact]
    public void A_list_built_against_the_stable_quicksorts_pivots_is_sorted_by_driftsort()
    {
        const int count = 1000;
        var starts = Enumerable.Range(0, count / 64).Select(i => i * 64).Where(s => count - s >= 64).ToArray();
        var (adversary, values) = McIlroy(count, starts.Select(s => s + 1).ToArray());
        var drifts = 0;
        var runs = Stable(Enumerable.Range(0, count).ToArray(), adversary, ref drifts);
        Assert.Equal(Enumerable.Repeat(2, starts.Length), runs);
        Assert.True(drifts > 0, "the adversary's list took no driftsort");

        var replay = values.ToArray();
        var replayed = 0;
        Assert.Equal(runs, Stable(replay, (x, y) => x < y, ref replayed));
        Assert.True(replayed > 0, "the list replayed took no driftsort");
        var sorted = values.Order().ToArray();
        Assert.Equal(sorted, replay);

        // and a list of the same runs whose other items are in a random order takes none, the positive control of the count
        var random = Enumerable.Range(0, count).Select(i => starts.Contains(i - 1) ? -1 - i : (int)((uint)(i * 2654435761) % 997)).ToArray();
        var randomDrifts = 0;
        Assert.Equal(runs, Stable(random, (x, y) => x < y, ref randomDrifts));
        Assert.Equal(0, randomDrifts);

        var program = $"mut xs = [{string.Join(", ", values)}]\nxs.Sort()\nprint(xs)\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "sort-stable-adversary"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal([Text(sorted, v => v.ToString(CultureInfo.InvariantCulture))], output);
    }

    /// <summary>
    /// McIlroy's adversary over <paramref name="count"/> items, all of them gas, above every value given, but the items at
    /// <paramref name="least"/>, which are given the least values first: two gas items compared give one of them the next value, the one
    /// that is not the candidate for the pivot, and a gas item compared with one of a value becomes the candidate.
    /// </summary>
    private static (Func<int, int, bool> Less, int[] Values) McIlroy(int count, int[] least)
    {
        var values = Enumerable.Repeat(count, count).ToArray();
        var solid = 0;
        foreach (var at in least)
        {
            values[at] = solid++;
        }

        var candidate = -1;
        return ((x, y) =>
        {
            if (values[x] == count && values[y] == count)
            {
                values[x == candidate ? x : y] = solid++;
            }

            if (values[x] == count)
            {
                candidate = x;
            }
            else if (values[y] == count)
            {
                candidate = y;
            }

            return values[x] < values[y];
        }, values);
    }

    /// <summary>The emitter's <c>SortLimit</c>: twice the base-2 logarithm of <c>n | 1</c>.</summary>
    private static int Limit(int n) => 2 * int.Log2(n | 1);

    /// <summary>The emitter's <c>SortRunOf</c>, Rust's <c>find_existing_run</c>: the length of the run the <paramref name="n"/> items at <paramref name="low"/> begin with, and whether it falls.</summary>
    private static (int Length, bool Falls) Run(int[] a, int low, int n, Func<int, int, bool> less)
    {
        if (n < 2)
        {
            return (n, false);
        }

        var run = 2;
        var falls = less(a[low + 1], a[low]);
        while (run < n && less(a[low + run], a[low + run - 1]) == falls)
        {
            run++;
        }

        return (run, falls);
    }

    /// <summary>The emitter's <c>IpnSort</c> of a list of more than 20 items, comparison for comparison: <paramref name="heaps"/> counts the sides its quicksort gives to the heapsort. Gives the length of the run the list begins with.</summary>
    private static int Unstable(int[] a, Func<int, int, bool> less, ref int heaps)
    {
        var (run, falls) = Run(a, 0, a.Length, less);
        if (run == a.Length)
        {
            if (falls)
            {
                Array.Reverse(a);
            }

            return run;
        }

        Quick(a, 0, a.Length, null, Limit(a.Length), less, ref heaps);
        return run;
    }

    /// <summary>The emitter's <c>QuickSort</c>, Rust's unstable <c>quicksort</c>, comparison for comparison but in its small sort, which sorts by insertion here.</summary>
    private static void Quick(int[] a, int low, int n, int? ancestor, int limit, Func<int, int, bool> less, ref int heaps)
    {
        while (true)
        {
            if (n <= SmallSortMost)
            {
                Insertion(a, low, n, less);
                return;
            }

            if (limit == 0)
            {
                heaps++;
                Heap(a, low, n, less);
                return;
            }

            limit--;
            var p = Pivot(a, low, n, less);
            if (ancestor is { } at && !less(a[at], a[low + p]))
            {
                var equal = Partition(a, low, n, p, (x, y) => !less(y, x));
                low += equal + 1;
                n -= equal + 1;
                ancestor = null;
                continue;
            }

            var below = Partition(a, low, n, p, less);
            Quick(a, low, below, ancestor, limit, less, ref heaps);
            ancestor = low + below;
            low += below + 1;
            n -= below + 1;
        }
    }

    /// <summary>The emitter's <c>SortPivot</c>, Rust's <c>choose_pivot</c>: the position of the pivot among the <paramref name="n"/> items at <paramref name="low"/>.</summary>
    private static int Pivot(int[] a, int low, int n, Func<int, int, bool> less)
    {
        var eighth = n / 8;
        var (x, y, z) = (low, low + (eighth * 4), low + (eighth * 7));
        return (n < 64 ? Median(a, x, y, z, less) : MedianOfMedians(a, x, y, z, eighth, less)) - low;
    }

    private static int Median(int[] a, int x, int y, int z, Func<int, int, bool> less)
    {
        var p = less(a[x], a[y]);
        var q = less(a[x], a[z]);
        return p != q ? x : less(a[y], a[z]) ^ p ? z : y;
    }

    private static int MedianOfMedians(int[] a, int x, int y, int z, int n, Func<int, int, bool> less)
    {
        if (n * 8 >= 64)
        {
            var eighth = n / 8;
            x = MedianOfMedians(a, x, x + (eighth * 4), x + (eighth * 7), eighth, less);
            y = MedianOfMedians(a, y, y + (eighth * 4), y + (eighth * 7), eighth, less);
            z = MedianOfMedians(a, z, z + (eighth * 4), z + (eighth * 7), eighth, less);
        }

        return Median(a, x, y, z, less);
    }

    /// <summary>The emitter's <c>SortPartition</c>, Rust's <c>partition</c> with <c>partition_lomuto_branchless_cyclic</c>, move for move: the count of the items before the pivot.</summary>
    private static int Partition(int[] a, int low, int n, int p, Func<int, int, bool> less)
    {
        (a[low], a[low + p]) = (a[low + p], a[low]);
        var pivot = a[low];
        var first = low + 1;
        var (gapValue, gap, below) = (a[first], first, 0);
        for (var right = first + 1; right <= low + n; right++)
        {
            var item = right == low + n ? gapValue : a[right];
            var goes = less(item, pivot);
            a[gap] = a[first + below];
            a[first + below] = item;
            gap = right;
            below += goes ? 1 : 0;
        }

        (a[low], a[low + below]) = (a[low + below], a[low]);
        return below;
    }

    /// <summary>
    /// The emitter's <c>DriftsortMain</c> of a list of 65 to 1024 items with no run of 64, comparison for comparison: the run found at
    /// each 64 items that leave 64 or more, all of them kept unsorted, and the stable quicksort of the whole list (<see cref="StableQuick"/>);
    /// <paramref name="drifts"/> counts the sides that quicksort gives to driftsort's eager form. Gives the lengths of the runs found.
    /// </summary>
    private static List<int> Stable(int[] a, Func<int, int, bool> less, ref int drifts)
    {
        Assert.InRange(a.Length, (2 * SmallSortMost) + 1, 1024);
        var runs = new List<int>();
        for (var scan = 0; a.Length - scan >= 64; scan += 64)
        {
            runs.Add(Run(a, scan, a.Length - scan, less).Length);
        }

        Assert.All(runs, run => Assert.True(run < 64, "a run of 64 is kept sorted, which this copy does not follow"));
        StableQuick(a, 0, a.Length, null, Limit(a.Length), less, ref drifts);
        return runs;
    }

    /// <summary>The emitter's <c>StableQuick</c>, Rust's stable <c>quicksort</c>, comparison for comparison but in its small sort and in the driftsort it falls back on, which sort by insertion here.</summary>
    private static void StableQuick(int[] a, int low, int n, int? ancestor, int limit, Func<int, int, bool> less, ref int drifts)
    {
        while (true)
        {
            if (n <= SmallSortMost)
            {
                Insertion(a, low, n, less);
                return;
            }

            if (limit == 0)
            {
                drifts++;
                Insertion(a, low, n, less);
                return;
            }

            limit--;
            var p = Pivot(a, low, n, less);
            var pivot = a[low + p];
            var equal = ancestor is { } before && !less(before, pivot);
            var below = 0;
            if (!equal)
            {
                below = StablePartition(a, low, n, p, false, less);
                equal = below == 0;
            }

            if (equal)
            {
                var equals = StablePartition(a, low, n, p, true, (x, y) => !less(y, x));
                low += equals;
                n -= equals;
                ancestor = null;
                continue;
            }

            StableQuick(a, low + below, n - below, pivot, limit, less, ref drifts);
            n = below;
        }
    }

    /// <summary>The emitter's <c>StablePartition</c>, Rust's <c>stable_partition</c>: the items that go before the pivot first, the pivot among them where <paramref name="goesLeft"/>, each side in its order; the count of the first side.</summary>
    private static int StablePartition(int[] a, int low, int n, int p, bool goesLeft, Func<int, int, bool> less)
    {
        var pivot = a[low + p];
        var (left, right) = (new List<int>(), new List<int>());
        for (var i = 0; i < n; i++)
        {
            ((i == p ? goesLeft : less(a[low + i], pivot)) ? left : right).Add(a[low + i]);
        }

        left.Concat(right).ToArray().CopyTo(a, low);
        return left.Count;
    }

    private static void Insertion(int[] a, int low, int n, Func<int, int, bool> less)
    {
        for (var k = 1; k < n; k++)
        {
            var item = a[low + k];
            var j = k;
            while (j > 0 && less(item, a[low + j - 1]))
            {
                a[low + j] = a[low + j - 1];
                j--;
            }

            a[low + j] = item;
        }
    }

    private static void Heap(int[] a, int low, int n, Func<int, int, bool> less)
    {
        for (var node = n >> 1; node > 0;)
        {
            node--;
            Sift(a, low, node, n, less);
        }

        for (var end = n; end > 1;)
        {
            end--;
            (a[low], a[low + end]) = (a[low + end], a[low]);
            Sift(a, low, 0, end, less);
        }
    }

    private static void Sift(int[] a, int low, int node, int end, Func<int, int, bool> less)
    {
        while (true)
        {
            var child = (2 * node) + 1;
            if (child >= end)
            {
                return;
            }

            if (child + 1 < end && less(a[low + child], a[low + child + 1]))
            {
                child++;
            }

            if (!less(a[low + node], a[low + child]))
            {
                return;
            }

            (a[low + node], a[low + child]) = (a[low + child], a[low + node]);
            node = child;
        }
    }
}
