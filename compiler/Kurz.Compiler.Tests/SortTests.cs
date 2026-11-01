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
/// bytes in the order of their UTF-8 bytes (T32), <c>int?</c> items, <c>null</c> first, and keys of <c>int</c>, <c>string</c> and
/// <c>double</c>; an unstable sort of <c>double</c> items is held to the stable one with <c>-0</c> read as <c>0</c>, the one pair of equal
/// items whose texts differ; and the same under the allocator that makes every block it frees inaccessible, for the first 50 rounds. A
/// list whose quicksort's pivots were picked against it, as McIlroy's adversary picks them, reaches the heapsort and is sorted by it.
/// </summary>
public sealed class SortTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Rounds = 300;

    /// <summary>The rounds the run under the allocator that makes every freed block inaccessible takes, which keeps the pages of every block it frees (<see cref="ParseTests"/>).</summary>
    private const int FencedRounds = 50;

    /// <summary>The most items for which the emitter's sorts take insertion alone, its <c>ShortRun</c>.</summary>
    private const int ShortRun = 20;

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

        double z = 0.0
        pool = [z / z, -0.0, 0.0, 1.5, -2.25, 3.0, 0.1]
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
    private const int Lines = 14;

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
        var (pastRun, pastPasses, zeros, nulls, equalStrings) = (0, 0, 0, 0, 0);
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
            expected.Add((Ints(xs), false)); // the list the sorts copied keeps its order (M3)
            pastRun += xs.Count > ShortRun ? 1 : 0;
            pastPasses += xs.Count > 8 * ShortRun ? 1 : 0;

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
            zeros += ds.Count > ShortRun && ds.Any(double.IsNaN) && zero >= 0 && zero < negativeZero ? 1 : 0;

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
            equalStrings += ss.Count > ShortRun && ss.Where(s => new StringInfo(s).LengthInTextElements > 1).GroupBy(s => s, StringComparer.Ordinal).Any(g => g.Count() > 1) ? 1 : 0;

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
            nulls += ns.Count > ShortRun && ns.Contains(null) && ns.IndexOf(null) > 0 ? 1 : 0;

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

        // the floors: lists past one run, past three passes of the merges, `double` lists whose `0` stands before a `-0` beside NaN,
        // string lists with equal items among items of more than one letter, and `int?` lists with a `null` after their first item
        Assert.True(pastRun >= 200, $"only {pastRun} lists of `int` are longer than {ShortRun} items");
        Assert.True(pastPasses >= 100, $"only {pastPasses} lists of `int` are longer than {8 * ShortRun} items");
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

    /// <summary>
    /// X8: a list of 1000 items built against <c>SortUnstable</c>'s quicksort by McIlroy's adversary (<i>A Killer Adversary for
    /// Quicksort</i>, 1999), which answers each comparison so that the pivot falls near an end, takes more splits than the bound the
    /// emitter gives the quicksort, so its last side is sorted by the heapsort, and the list is sorted. The adversary runs against a copy of
    /// the emitter's quicksort written here, comparison for comparison, which the list is replayed through to show that the bound is
    /// reached.
    /// </summary>
    [Fact]
    public void A_list_built_against_the_quicksorts_pivots_is_sorted_by_its_heapsort()
    {
        const int count = 1000;
        var values = Enumerable.Repeat(count, count).ToArray(); // every item gas, above every value given
        var (solid, candidate) = (0, -1);
        bool Adversary(int x, int y)
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
        }

        var heaps = 0;
        Unstable(Enumerable.Range(0, count).ToArray(), 0, count, Limit(count), Adversary, ref heaps);
        Assert.True(heaps > 0, "the adversary's list took no heapsort");

        // the list replayed through the copy, compared as numbers: the same comparisons, so the same heapsort
        var replay = values.ToArray();
        var replayed = 0;
        Unstable(replay, 0, count, Limit(count), (x, y) => x < y, ref replayed);
        Assert.True(replayed > 0, "the list replayed took no heapsort");
        var sorted = values.Order().ToArray();
        Assert.Equal(sorted, replay);

        // and an order of random items takes none, the positive control of the count
        var random = Enumerable.Range(0, count).Select(i => (int)((uint)(i * 2654435761) % 997)).ToArray();
        var randomHeaps = 0;
        Unstable(random, 0, count, Limit(count), (x, y) => x < y, ref randomHeaps);
        Assert.Equal(0, randomHeaps);

        var program = $"mut xs = [{string.Join(", ", values)}]\nxs.SortUnstable()\nprint(xs)\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "sort-adversary"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal([Text(sorted, v => v.ToString(CultureInfo.InvariantCulture))], output);
    }

    /// <summary>The emitter's <c>SortLimit</c>: twice the base-2 logarithm of <c>n | 1</c>.</summary>
    private static int Limit(int n) => 2 * int.Log2(n | 1);

    /// <summary>The emitter's <c>UnstableSort</c>, comparison for comparison: <paramref name="heaps"/> counts the sides it gives to the heapsort.</summary>
    private static void Unstable(int[] a, int low, int n, int limit, Func<int, int, bool> less, ref int heaps)
    {
        while (true)
        {
            if (n <= ShortRun)
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
            var last = n - 1;
            var middle = last >> 1;
            Order(a, low, 0, middle, less);
            Order(a, low, middle, last, less);
            Order(a, low, 0, middle, less);
            var p = a[low + middle];
            var (i, j) = (-1, n);
            while (true)
            {
                do
                {
                    i++;
                }
                while (less(a[low + i], p));

                do
                {
                    j--;
                }
                while (less(p, a[low + j]));

                if (i >= j)
                {
                    break;
                }

                (a[low + i], a[low + j]) = (a[low + j], a[low + i]);
            }

            var left = j + 1;
            var right = n - left;
            if (left < right)
            {
                Unstable(a, low, left, limit, less, ref heaps);
                low += left;
                n = right;
            }
            else
            {
                Unstable(a, low + left, right, limit, less, ref heaps);
                n = left;
            }
        }
    }

    private static void Order(int[] a, int low, int x, int y, Func<int, int, bool> less)
    {
        if (less(a[low + y], a[low + x]))
        {
            (a[low + x], a[low + y]) = (a[low + y], a[low + x]);
        }
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
