using System.Text;
using Kurz.Compiler.Emit;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// G3: the check of a literal pattern of <c>Regex.New</c> when the program compiles, by the parse and the count of the standard
/// library's <c>RegexParser</c> (<c>std/std.kz</c>), which this class follows step for step, with the tables of
/// <see cref="CaseTables"/> that the emitter writes into the program for it (<c>Emitter.RegexTable</c>): the kind and the position
/// of the first construct the parse refuses, or <c>TooBig</c> where the size of the automaton passes the bound (G2), so that the
/// compiler refuses a literal exactly where <c>Regex.New</c> of the same string gives a <c>RegexError</c>.
/// <c>RegexTests.The_check_of_a_literal_and_Regex_New_give_the_same_error_for_every_pattern</c> holds the two to each other.
/// </summary>
internal sealed class RegexPattern
{
    /// <summary>The kinds of <c>RegexKind</c>, in its order.</summary>
    public enum Kind { Syntax, Unsupported, TooBig }

    /// <summary>The bound of the automaton, 10 MiB, the default of Rust's <c>size_limit</c> (G2).</summary>
    public const long Bound = 10485760;

    private readonly string text;
    private readonly byte[] p;
    private readonly int n;
    private int at;
    private int flags;
    private int failKind = -1;
    private int failAt;
    private string? unbuilt;
    private readonly List<int> kind = [];
    private readonly List<int> na = [];
    private readonly List<int> nb = [];
    private readonly List<int> nc = [];
    private readonly List<int> kids = [];
    private int ranges;
    private readonly List<string> names = [];
    private bool usesWord;
    private int escCp;
    private List<int> escSet = [];
    private int countMin;
    private int countMax;
    private List<int> asciiSet = [];

    private RegexPattern(string pattern)
    {
        text = pattern;
        p = Encoding.UTF8.GetBytes(pattern);
        n = p.Length;
    }

    /// <summary>
    /// The error <c>Regex.New(pattern)</c> gives, or null where it builds; with the words of the construct where the error is one of
    /// Rust's syntax that the build does not hold (<see cref="Unbuilt"/>).
    /// </summary>
    public static (Kind Kind, int At, string? Unbuilt)? Check(string pattern)
    {
        var r = new RegexPattern(pattern);
        r.Build();
        return r.failKind < 0 ? null : ((Kind)r.failKind, r.failAt, r.unbuilt);
    }

    /// <summary>The words <c>RegexError</c>'s text gives the error, <c>syntax at 1</c> (G3).</summary>
    public static string Words(Kind kind, int at) => kind switch
    {
        Kind.Syntax => $"syntax at {at}",
        Kind.Unsupported => $"unsupported at {at}",
        _ => $"too big at {at}",
    };

    private void Build()
    {
        names.Add("");
        var root = Alt(0);
        if (failKind >= 0)
        {
            return;
        }

        if (at < n)
        {
            Fail(0, at);
            return;
        }

        if (usesWord)
        {
            ranges += CaseTables.RegexWord.Length;
        }

        var size = 3 + Size(root);
        if (16 * size + 4 * (long)ranges > Bound)
        {
            Fail(2, 0);
        }
    }

    private void Fail(int k, int position)
    {
        if (failKind < 0)
        {
            failKind = k;
            failAt = position;
        }
    }

    /// <summary>
    /// A construct of Rust's syntax (G4) that the standard library's parse does not build, <c>Unsupported</c> there at the position,
    /// with the words the checker's <c>not supported</c> names it by where it is the first error of the pattern.
    /// </summary>
    private void Unbuilt(int position, string words)
    {
        if (failKind < 0)
        {
            unbuilt = words;
        }

        Fail(1, position);
    }

    private int Node(int k, int a, int b, int c)
    {
        kind.Add(k);
        na.Add(a);
        nb.Add(b);
        nc.Add(c);
        return kind.Count - 1;
    }

    private int Kids(List<int> items)
    {
        var start = kids.Count;
        kids.AddRange(items);
        return start;
    }

    private int ClassNode(List<int> set)
    {
        var start = ranges;
        ranges += set.Count;
        return Node(2, start, set.Count / 2, 0);
    }

    private int Literal(int cp)
    {
        if ((flags & 1) != 0)
        {
            var set = Fold([cp, cp]);
            if (set.Count > 2)
            {
                return ClassNode(set);
            }
        }

        return Node(1, cp, 0, 0);
    }

    private static List<int> Table(int which) => [.. which switch
    {
        0 => CaseTables.RegexDigit,
        1 => CaseTables.RegexWord,
        2 => CaseTables.RegexSpace,
        _ => CaseTables.RegexFold,
    }];

    private void Skip()
    {
        if ((flags & 8) == 0)
        {
            return;
        }

        while (at < n)
        {
            if (p[at] == 35)
            {
                while (at < n && p[at] != 10)
                {
                    at++;
                }

                continue;
            }

            var cp = Decode(p, at);
            if (cp != 32 && (cp < 9 || cp > 13) && (cp < 128 || !In(CaseTables.RegexSpace, cp)))
            {
                return;
            }

            at += Length(p[at]);
        }
    }

    private int Alt(int depth)
    {
        var items = new List<int>();
        while (true)
        {
            var item = Concat(depth);
            if (failKind >= 0)
            {
                return -1;
            }

            items.Add(item);
            if (at < n && p[at] == 124)
            {
                at++;
                continue;
            }

            break;
        }

        return items.Count == 1 ? items[0] : Node(6, Kids(items), items.Count, 0);
    }

    private int Concat(int depth)
    {
        var items = new List<int>();
        while (true)
        {
            Skip();
            if (at >= n || p[at] == 124 || p[at] == 41)
            {
                break;
            }

            var atom = Atom(depth);
            if (failKind >= 0)
            {
                return -1;
            }

            if (atom == -2)
            {
                continue;
            }

            var node = atom;
            var stacked = 0;
            while (true)
            {
                Skip();
                if (at >= n)
                {
                    break;
                }

                var op = at;
                int d = p[at];
                var min = 0;
                var max = -1;
                if (d == 42)
                {
                    at++;
                }
                else if (d == 43)
                {
                    min = 1;
                    at++;
                }
                else if (d == 63)
                {
                    max = 1;
                    at++;
                }
                else if (d == 123)
                {
                    if (!Counted())
                    {
                        return -1;
                    }

                    min = countMin;
                    max = countMax;
                }
                else
                {
                    break;
                }

                var greedy = true;
                if (at < n && p[at] == 63)
                {
                    greedy = false;
                    at++;
                }

                if ((flags & 16) != 0)
                {
                    greedy = !greedy;
                }

                stacked++;
                if (depth + stacked > 250)
                {
                    Fail(0, op);
                    return -1;
                }

                node = Node(greedy ? 7 : 8, node, min, max);
            }

            items.Add(node);
        }

        return items.Count switch
        {
            0 => Node(0, 0, 0, 0),
            1 => items[0],
            _ => Node(5, Kids(items), items.Count, 0),
        };
    }

    private bool Counted()
    {
        var open = at;
        at++;
        Skip();
        var lo = Decimal(open);
        if (lo < 0)
        {
            return false;
        }

        var hi = lo;
        Skip();
        if (at < n && p[at] == 44)
        {
            at++;
            Skip();
            if (at < n && p[at] == 125)
            {
                hi = -1;
            }
            else
            {
                hi = Decimal(open);
                if (hi < 0)
                {
                    return false;
                }

                Skip();
            }
        }

        if (at >= n || p[at] != 125)
        {
            Fail(0, open);
            return false;
        }

        at++;
        if (hi >= 0 && hi < lo)
        {
            Fail(0, open);
            return false;
        }

        countMin = lo;
        countMax = hi;
        return true;
    }

    private int Decimal(int open)
    {
        var start = at;
        long value = 0;
        while (at < n && p[at] >= 48 && p[at] <= 57)
        {
            value = value * 10 + (p[at] - 48);
            if (value > int.MaxValue)
            {
                Fail(0, open);
                return -1;
            }

            at++;
        }

        if (at == start)
        {
            Fail(0, open);
            return -1;
        }

        return (int)value;
    }

    private int Atom(int depth)
    {
        int c = p[at];
        if (c == 40)
        {
            return Group(depth);
        }

        if (c == 91)
        {
            var set = Bracket(depth);
            return failKind >= 0 ? -1 : ClassNode(set);
        }

        if (c == 46)
        {
            at++;
            return (flags & 4) != 0 ? ClassNode([0, 1114111]) : ClassNode([0, 9, 11, 1114111]);
        }

        if (c == 94)
        {
            at++;
            return Node(3, (flags & 2) != 0 ? 2 : 0, 0, 0);
        }

        if (c == 36)
        {
            at++;
            return Node(3, (flags & 2) != 0 ? 3 : 1, 0, 0);
        }

        if (c == 92)
        {
            return Escape(false) switch
            {
                0 => -1,
                1 => Literal(escCp),
                2 => ClassNode(escSet),
                _ => Node(3, escCp, 0, 0),
            };
        }

        if (c is 42 or 43 or 63 or 123)
        {
            Fail(0, at);
            return -1;
        }

        var cp = Decode(p, at);
        at += Length(p[at]);
        return Literal(cp);
    }

    private int Group(int depth)
    {
        var open = at;
        if (depth >= 250)
        {
            Fail(0, open);
            return -1;
        }

        at++;
        var saved = flags;
        var capture = -1;
        if (at < n && p[at] == 63)
        {
            at++;
            if (at >= n)
            {
                Fail(0, open);
                return -1;
            }

            int c = p[at];
            if (c == 61 || c == 33 || c == 62 || (c == 60 && at + 1 < n && (p[at + 1] == 61 || p[at + 1] == 33)))
            {
                Fail(1, open);
                return -1;
            }

            if (c == 60 || (c == 80 && at + 1 < n && p[at + 1] == 60))
            {
                if (c == 80)
                {
                    at++;
                }

                at++;
                var name = Name(open);
                if (failKind >= 0)
                {
                    return -1;
                }

                capture = names.Count;
                names.Add(name);
            }
            else
            {
                var r = Flags(open);
                if (r < 0)
                {
                    return -1;
                }

                if (r == 0)
                {
                    return -2;
                }
            }
        }
        else
        {
            capture = names.Count;
            names.Add("");
        }

        var child = Alt(depth + 1);
        if (failKind >= 0)
        {
            return -1;
        }

        if (at >= n || p[at] != 41)
        {
            Fail(0, open);
            return -1;
        }

        at++;
        flags = saved;
        return capture < 0 ? child : Node(4, capture, child, 0);
    }

    private string Name(int open)
    {
        var start = at;
        while (at < n && p[at] != 62)
        {
            at++;
        }

        if (at >= n)
        {
            Fail(0, open);
            return "";
        }

        if (at == start)
        {
            Fail(0, start);
            return "";
        }

        var k = start;
        while (k < at)
        {
            var cp = Decode(p, k);
            var letter = cp == 95 || (cp >= 65 && cp <= 90) || (cp >= 97 && cp <= 122) || (cp >= 128 && In(CaseTables.RegexWord, cp));
            var later = k > start && ((cp >= 48 && cp <= 57) || cp == 46 || cp == 91 || cp == 93);
            if (!letter && !later)
            {
                Fail(0, k);
                return "";
            }

            k += Length(p[k]);
        }

        var name = Encoding.UTF8.GetString(p, start, at - start);
        if (names.Contains(name))
        {
            Fail(0, start);
            return "";
        }

        at++;
        return name;
    }

    private int Flags(int open)
    {
        var negate = false;
        var seen = 0;
        var dash = false;
        var any = false;
        while (true)
        {
            if (at >= n)
            {
                Fail(0, open);
                return -1;
            }

            int c = p[at];
            if (c == 41 || c == 58)
            {
                if (dash)
                {
                    Fail(0, at - 1);
                    return -1;
                }

                if (c == 41 && !any)
                {
                    Fail(0, open);
                    return -1;
                }

                at++;
                return c == 41 ? 0 : 1;
            }

            if (c == 45)
            {
                if (negate)
                {
                    Fail(0, at);
                    return -1;
                }

                negate = true;
                dash = true;
                at++;
                continue;
            }

            var bit = c switch
            {
                105 => 1,
                109 => 2,
                115 => 4,
                120 => 8,
                85 => 16,
                _ => 0,
            };
            if (c is 82 or 117)
            {
                Unbuilt(at, c == 82 ? "the flag `R`" : "the flag `u`");
                return -1;
            }

            if (bit == 0)
            {
                Fail(0, at);
                return -1;
            }

            if ((seen & bit) != 0)
            {
                Fail(0, at);
                return -1;
            }

            seen |= bit;
            flags = negate ? flags & (31 - bit) : flags | bit;
            dash = false;
            any = true;
            at++;
        }
    }

    private int Escape(bool inClass)
    {
        var start = at;
        at++;
        if (at >= n)
        {
            Fail(0, start);
            return 0;
        }

        int c = p[at];
        if (c is 100 or 68 or 119 or 87 or 115 or 83)
        {
            at++;
            var which = c is 119 or 87 ? 1 : c is 115 or 83 ? 2 : 0;
            escSet = c < 97 ? Negate(Table(which)) : Table(which);
            return 2;
        }

        if (c is 98 or 66 or 65 or 122)
        {
            if (inClass)
            {
                Fail(0, start);
                return 0;
            }

            if (c == 98 && at + 1 < n && p[at + 1] == 123)
            {
                Unbuilt(start, "a word boundary of one side, `\\b{start}` and its kind");
                return 0;
            }

            at++;
            if (c == 65)
            {
                escCp = 0;
            }
            else if (c == 122)
            {
                escCp = 1;
            }
            else
            {
                usesWord = true;
                escCp = c == 98 ? 4 : 5;
            }

            return 3;
        }

        var simple = c switch
        {
            110 => 10,
            116 => 9,
            114 => 13,
            102 => 12,
            118 => 11,
            97 => 7,
            _ => -1,
        };
        if (simple >= 0)
        {
            at++;
            escCp = simple;
            return 1;
        }

        if (c is 120 or 117 or 85)
        {
            at++;
            var digits = c == 117 ? 4 : c == 85 ? 8 : 2;
            long value = 0;
            if (at < n && p[at] == 123)
            {
                at++;
                var count = 0;
                while (at < n && p[at] != 125)
                {
                    var h = Hex(p[at]);
                    if (h < 0 || count >= 8)
                    {
                        Fail(0, start);
                        return 0;
                    }

                    value = value * 16 + h;
                    count++;
                    at++;
                }

                if (at >= n || count == 0)
                {
                    Fail(0, start);
                    return 0;
                }

                at++;
            }
            else
            {
                for (var k = 0; k < digits; k++)
                {
                    if (at >= n || Hex(p[at]) < 0)
                    {
                        Fail(0, start);
                        return 0;
                    }

                    value = value * 16 + Hex(p[at]);
                    at++;
                }
            }

            if (value > 1114111 || (value >= 55296 && value <= 57343))
            {
                Fail(0, start);
                return 0;
            }

            escCp = (int)value;
            return 1;
        }

        if (c is 112 or 80)
        {
            Unbuilt(start, "a class of Unicode by its name, `\\p` and `\\P`");
            return 0;
        }

        if (c is 60 or 62)
        {
            Unbuilt(start, "the start or the end of a word, `\\<` and `\\>`");
            return 0;
        }

        if ((c >= 48 && c <= 57) || c == 107)
        {
            Fail(1, start);
            return 0;
        }

        if (c < 128 && !((c >= 65 && c <= 90) || (c >= 97 && c <= 122)))
        {
            at++;
            escCp = c;
            return 1;
        }

        Fail(0, start);
        return 0;
    }

    private List<int> Bracket(int depth)
    {
        var open = at;
        if (depth >= 250)
        {
            Fail(0, open);
            return [];
        }

        at++;
        var negated = false;
        if (at < n && p[at] == 94)
        {
            negated = true;
            at++;
        }

        var set = Union(open, depth, true);
        while (failKind < 0)
        {
            Skip();
            if (at >= n)
            {
                Fail(0, open);
                break;
            }

            int c = p[at];
            if (c == 93)
            {
                at++;
                break;
            }

            at += 2;
            var right = Union(open, depth, false);
            if (failKind >= 0)
            {
                break;
            }

            set = c == 38 ? Intersect(set, right)
                : c == 45 ? Intersect(set, Negate(right))
                : Unite(Intersect(set, Negate(right)), Intersect(right, Negate(set)));
        }

        if (failKind >= 0)
        {
            return [];
        }

        if ((flags & 1) != 0)
        {
            set = Fold(set);
        }

        return negated ? Negate(set) : set;
    }

    private List<int> Union(int open, int depth, bool first)
    {
        var set = new List<int>();
        var firstItem = first;
        while (true)
        {
            Skip();
            if (at >= n)
            {
                Fail(0, open);
                return set;
            }

            int c = p[at];
            if (c == 93 && !firstItem)
            {
                return set;
            }

            if (!firstItem && at + 1 < n && (c is 38 or 45 or 126) && p[at + 1] == c)
            {
                return set;
            }

            firstItem = false;
            if (c == 91)
            {
                if (at + 1 < n && p[at + 1] == 58 && Ascii())
                {
                    set = Unite(set, asciiSet);
                    continue;
                }

                var inner = Bracket(depth + 1);
                if (failKind >= 0)
                {
                    return set;
                }

                set = Unite(set, inner);
                continue;
            }

            var itemAt = at;
            var lo = ClassChar();
            if (failKind >= 0)
            {
                return set;
            }

            if (lo == -2)
            {
                set = Unite(set, escSet);
                continue;
            }

            var hi = lo;
            if (at + 1 < n && p[at] == 45 && p[at + 1] != 93 && p[at + 1] != 45)
            {
                at++;
                hi = ClassChar();
                if (failKind >= 0)
                {
                    return set;
                }

                if (hi == -2 || hi < lo)
                {
                    Fail(0, itemAt);
                    return set;
                }
            }

            set = Unite(set, [lo, hi]);
        }
    }

    private int ClassChar()
    {
        if (p[at] == 92)
        {
            return Escape(true) switch
            {
                0 => -1,
                2 => -2,
                _ => escCp,
            };
        }

        var cp = Decode(p, at);
        at += Length(p[at]);
        return cp;
    }

    private bool Ascii()
    {
        var k = at + 2;
        var negated = false;
        if (k < n && p[k] == 94)
        {
            negated = true;
            k++;
        }

        var nameAt = k;
        while (k < n && p[k] >= 97 && p[k] <= 122)
        {
            k++;
        }

        if (k == nameAt || k + 1 >= n || p[k] != 58 || p[k + 1] != 93)
        {
            return false;
        }

        var set = AsciiClass(Encoding.UTF8.GetString(p, nameAt, k - nameAt));
        if (set.Count == 0)
        {
            return false;
        }

        at = k + 2;
        asciiSet = negated ? Negate(set) : set;
        return true;
    }

    private static List<int> Fold(List<int> set)
    {
        var f = CaseTables.RegexFold;
        var count = f.Length / 2;
        var extra = new List<int>();
        for (var r = 0; r < set.Count; r += 2)
        {
            int lo = set[r], hi = set[r + 1];
            int a = 0, b = count;
            while (a < b)
            {
                var m = (a + b) / 2;
                if (f[2 * m] < lo)
                {
                    a = m + 1;
                }
                else
                {
                    b = m;
                }
            }

            for (; a < count && f[2 * a] <= hi; a++)
            {
                var c = f[2 * a + 1];
                for (var steps = 0; c != f[2 * a] && steps < 8; steps++)
                {
                    extra.Add(c);
                    c = NextFold(f, count, c);
                }
            }
        }

        if (extra.Count == 0)
        {
            return set;
        }

        extra.Sort();
        var add = new List<int>();
        foreach (var c in extra)
        {
            var k = add.Count;
            if (k > 0 && c <= add[k - 1] + 1)
            {
                add[k - 1] = Math.Max(add[k - 1], c);
            }
            else
            {
                add.Add(c);
                add.Add(c);
            }
        }

        return Unite(set, add);
    }

    private static int NextFold(int[] f, int count, int c)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var m = (lo + hi) / 2;
            if (f[2 * m] < c)
            {
                lo = m + 1;
            }
            else if (f[2 * m] > c)
            {
                hi = m;
            }
            else
            {
                return f[2 * m + 1];
            }
        }

        return c;
    }

    private long Size(int node)
    {
        var k = kind[node];
        if (k == 0)
        {
            return 0;
        }

        if (k <= 3)
        {
            return 1;
        }

        if (k == 4)
        {
            return Cap(2 + Size(nb[node]));
        }

        if (k is 5 or 6)
        {
            long total = 0;
            for (var i = 0; i < nb[node]; i++)
            {
                total = Cap(total + Size(kids[na[node] + i]));
            }

            return k == 6 ? Cap(total + 2 * (long)(nb[node] - 1)) : total;
        }

        var child = Size(na[node]);
        long min = nb[node];
        if (nc[node] < 0)
        {
            return min == 0 ? Cap(child + 2) : Cap(Times(min, child) + 1);
        }

        return Cap(Times(min, child) + Times(nc[node] - min, child + 1));
    }

    private const long Ceiling = 1099511627776;

    private static long Cap(long x) => Math.Min(x, Ceiling);

    private static long Times(long a, long b) => b != 0 && a > Ceiling / b ? Ceiling : a * b;

    private static int Decode(byte[] b, int at)
    {
        int c = b[at];
        return c switch
        {
            < 128 => c,
            < 224 => ((c & 31) << 6) | (b[at + 1] & 63),
            < 240 => ((c & 15) << 12) | ((b[at + 1] & 63) << 6) | (b[at + 2] & 63),
            _ => ((c & 7) << 18) | ((b[at + 1] & 63) << 12) | ((b[at + 2] & 63) << 6) | (b[at + 3] & 63),
        };
    }

    private static int Length(byte c) => c switch
    {
        < 128 => 1,
        < 224 => 2,
        < 240 => 3,
        _ => 4,
    };

    private static int Hex(byte c) => c switch
    {
        >= 48 and <= 57 => c - 48,
        >= 65 and <= 70 => c - 55,
        >= 97 and <= 102 => c - 87,
        _ => -1,
    };

    private static bool In(int[] ranges, int c)
    {
        int lo = 0, hi = ranges.Length / 2;
        while (lo < hi)
        {
            var m = (lo + hi) / 2;
            if (c < ranges[2 * m])
            {
                hi = m;
            }
            else if (c > ranges[2 * m + 1])
            {
                lo = m + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> Unite(List<int> a, List<int> b)
    {
        var output = new List<int>();
        int i = 0, j = 0;
        while (i < a.Count || j < b.Count)
        {
            int lo, hi;
            if (j >= b.Count || (i < a.Count && a[i] <= b[j]))
            {
                lo = a[i];
                hi = a[i + 1];
                i += 2;
            }
            else
            {
                lo = b[j];
                hi = b[j + 1];
                j += 2;
            }

            var k = output.Count;
            if (k > 0 && lo <= output[k - 1] + 1)
            {
                output[k - 1] = Math.Max(output[k - 1], hi);
            }
            else
            {
                output.Add(lo);
                output.Add(hi);
            }
        }

        return output;
    }

    private static List<int> Intersect(List<int> a, List<int> b)
    {
        var output = new List<int>();
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            var lo = Math.Max(a[i], b[j]);
            var hi = Math.Min(a[i + 1], b[j + 1]);
            if (lo <= hi)
            {
                output.Add(lo);
                output.Add(hi);
            }

            if (a[i + 1] < b[j + 1])
            {
                i += 2;
            }
            else
            {
                j += 2;
            }
        }

        return output;
    }

    private static List<int> Negate(List<int> a)
    {
        var output = new List<int>();
        var next = 0;
        for (var i = 0; i < a.Count; i += 2)
        {
            if (a[i] > next)
            {
                output.Add(next);
                output.Add(a[i] - 1);
            }

            next = a[i + 1] + 1;
        }

        if (next <= 1114111)
        {
            output.Add(next);
            output.Add(1114111);
        }

        return output;
    }

    private static List<int> AsciiClass(string name) => name switch
    {
        "alnum" => [48, 57, 65, 90, 97, 122],
        "alpha" => [65, 90, 97, 122],
        "ascii" => [0, 127],
        "blank" => [9, 9, 32, 32],
        "cntrl" => [0, 31, 127, 127],
        "digit" => [48, 57],
        "graph" => [33, 126],
        "lower" => [97, 122],
        "print" => [32, 126],
        "punct" => [33, 47, 58, 64, 91, 96, 123, 126],
        "space" => [9, 13, 32, 32],
        "upper" => [65, 90],
        "word" => [48, 57, 65, 90, 95, 95, 97, 122],
        "xdigit" => [48, 57, 65, 70, 97, 102],
        _ => [],
    };
}
