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

    // the flags in force: 1 `i`, 2 `m`, 4 `s`, 8 `x`, 16 `U`, 32 `R` and 64 `u`, which is in force from the start, as in Rust's crate
    private int flags = 64;

    // the position of the first construct Rust's translator refuses, which it reads after the parse and the levels: -1 for none
    private int laterAt = -1;
    private int failKind = -1;
    private int failAt;
    private readonly List<int> kind = [];
    private readonly List<int> na = [];
    private readonly List<int> nb = [];
    private readonly List<int> nc = [];
    private readonly List<bool> zero = [];
    private readonly List<int> kids = [];
    private int ranges;
    private readonly List<string> names = [];
    private bool usesWord;
    private int escCp;
    private List<int> escSet = [];
    private List<int> asciiSet = [];
    private int capture;
    private int decimalAt;

    // the backtracking engine's (G2): whether the pattern is built for it; its backreferences, each by its node, the position of
    // its `\`, and the group's number, or -1 with its name; and what an escape that is one gave
    private readonly bool backtracking;
    private readonly List<int> refNode = [];
    private readonly List<int> refAt = [];
    private readonly List<int> refNumber = [];
    private readonly List<string> refName = [];
    private int escStart;
    private int escRef;
    private string escName = "";

    // the levels of Rust's `NestLimiter`: each node of Rust's syntax tree that counts one, by its start, its height and its children
    private readonly List<int> nestStart = [];
    private readonly List<int> nestHeight = [];
    private readonly List<int> nestFirst = [];
    private readonly List<int> nestCount = [];
    private readonly List<int> nestKids = [];
    private int lastNest = -1;

    // the items of the open sequences, the innermost last, as Rust's `Concat`s on its stack: the node of each (-1 for flags), its
    // level and its start; the branches of the open alternations; and the open groups, each with what its opening put aside
    private readonly List<int> itemNode = [];
    private readonly List<int> itemNest = [];
    private readonly List<int> itemStart = [];
    private readonly List<int> branchNode = [];
    private readonly List<int> branchNest = [];
    private readonly List<int> groupOpen = [];
    private readonly List<int> groupCapture = [];
    private readonly List<int> groupFlags = [];
    private readonly List<int> groupItems = [];
    private readonly List<int> groupConcat = [];
    private readonly List<int> groupBranches = [];
    private readonly List<int> groupAlt = [];
    private int items0;
    private int concatAt;
    private int branches0;
    private int altAt = -1;

    // the open brackets of a class, the innermost last, as Rust's `ClassState`s: where each opened, whether it is negated, its union
    // of items so far with their count, the start of the first and their levels, and the operation waiting for its right side
    private readonly List<int> classOpen = [];
    private readonly List<bool> classNegated = [];
    private readonly List<List<int>> classUnion = [];
    private readonly List<int> classCount = [];
    private readonly List<int> classFirst = [];
    private readonly List<int> classItems = [];
    private readonly List<int> unionNest = [];
    private readonly List<int> classOp = [];
    private readonly List<List<int>> classLhs = [];
    private readonly List<int> classLhsNest = [];
    private readonly List<int> classLhsStart = [];
    private List<int> setValue = [];
    private int setNest;
    private int setStart;

    private RegexPattern(string pattern, bool backtracking)
    {
        this.backtracking = backtracking;
        text = pattern;
        p = Encoding.UTF8.GetBytes(pattern);
        n = p.Length;
    }

    /// <summary>
    /// The error <c>Regex.New(pattern)</c> gives, or with <paramref name="backtracking"/> <c>Regex.New(pattern, backtracking:
    /// true)</c>, or null where it builds.
    /// </summary>
    public static (Kind Kind, int At)? Check(string pattern, bool backtracking = false)
    {
        var r = new RegexPattern(pattern, backtracking);
        r.Build();
        return r.failKind < 0 ? null : ((Kind)r.failKind, r.failAt);
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
        var root = Parse();
        if (failKind >= 0)
        {
            return;
        }

        Refs();
        if (failKind >= 0)
        {
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

    /// <summary>
    /// The backreferences, resolved once every group is known, as .NET resolves <c>\2(a)(b)</c>: a number no group has and a name no
    /// group has are <c>Syntax</c> at the <c>\</c>, the first such one in the pattern.
    /// </summary>
    private void Refs()
    {
        for (var i = 0; i < refNode.Count; i++)
        {
            var group = refNumber[i] >= 0 ? refNumber[i] : names.IndexOf(refName[i], 1);
            if (group < 0 || group >= names.Count)
            {
                Fail(0, refAt[i]);
                return;
            }

            na[refNode[i]] = group;
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
    /// A node, with whether the most it matches is no character, as Rust's <c>maximum_len</c> of <c>Some(0)</c>: the empty node and an
    /// assertion; a group of such a node; a sequence or an alternation of such nodes only; and a repetition of such a node or of at
    /// most 0.
    /// </summary>
    private int Node(int k, int a, int b, int c)
    {
        var none = k switch
        {
            0 or 3 => true,
            4 => zero[b],
            5 or 6 => Enumerable.Range(a, b).All(i => zero[kids[i]]),
            7 or 8 => c == 0 || zero[a],
            10 => true,
            11 => zero[b],
            _ => false,
        };
        kind.Add(k);
        na.Add(a);
        nb.Add(b);
        nc.Add(c);
        zero.Add(none);
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
            var set = Folded([cp, cp]);
            if (set.Count > 2)
            {
                return ClassNode(set);
            }
        }

        return Node(1, cp, 0, 0);
    }

    /// <summary>
    /// Rust's <c>parse_unicode_class</c> and its translation (G4): <c>\p</c> or <c>\P</c> at <paramref name="start"/>, then one
    /// letter, or in braces a name, <c>name=value</c>, <c>name:value</c> or <c>name!=value</c>, which negates once more, the white
    /// space and the comments of <c>(?x)</c> passed over. The end of the pattern is <c>Syntax</c> at the end, and a <c>\</c> for
    /// the letter at that <c>\</c>. The class is <see cref="escSet"/>, folded under <c>(?i)</c> before it is negated; a name
    /// Rust's crate does not know, and any <c>\p</c> under <c>(?-u)</c>, is <c>Syntax</c> at <paramref name="start"/> once the
    /// parse and the levels found no error, as its translator refuses it.
    /// </summary>
    private bool Property(bool negated, int start)
    {
        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, at);
            return false;
        }

        var text = new List<byte>();
        if (p[at] == 123)
        {
            at++;
            Skip();
            while (at < n && p[at] != 125)
            {
                var next = at + Length(p[at]);
                text.AddRange(p[at..next]);
                at = next;
                Skip();
            }

            if (at >= n)
            {
                Fail(0, at);
                return false;
            }

            at++;
        }
        else
        {
            if (p[at] == 92)
            {
                Fail(0, at);
                return false;
            }

            var next = at + Length(p[at]);
            text.AddRange(p[at..next]);
            at = next;
            Skip();
        }

        var split = Find(text, 33, 61);
        var width = 2;
        if (split < 0)
        {
            width = 1;
            split = Find(text, 58, -1);
        }

        if (split < 0)
        {
            split = Find(text, 61, -1);
        }

        escSet = [];
        if ((flags & 64) == 0)
        {
            Later(start);
            return true;
        }

        var key = Key(0, text, 0, text.Count);
        if (split >= 0)
        {
            var kindOf = Lookup(Key(8, text, 0, split));
            key = kindOf is [< 0] ? Key(-1 - kindOf[0], text, split + width, text.Count) : [];
        }

        var set = key.Count > 0 ? Lookup(key) : [];
        if (set.Count == 0 || set[0] < 0)
        {
            Later(start);
            return true;
        }

        if ((flags & 1) != 0)
        {
            set = Folded(set);
        }

        escSet = negated != (split >= 0 && width == 2) ? Negate(set) : set;
        return true;
    }

    /// <summary>Where <paramref name="text"/> first holds the byte <paramref name="a"/>, and <paramref name="b"/> after it where it is not -1; -1 where nowhere.</summary>
    private static int Find(List<byte> text, int a, int b)
    {
        for (var i = 0; i < text.Count; i++)
        {
            if (text[i] == a && (b < 0 || (i + 1 < text.Count && text[i + 1] == b)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The key of a name of <c>\p</c>: <paramref name="kind"/>, then the bytes of <paramref name="text"/> from
    /// <paramref name="from"/> to <paramref name="to"/> as Rust's <c>symbolic_name_normalize</c> gives them: a first
    /// <c>is</c> of any case dropped, <c> </c>, <c>_</c>, <c>-</c> and every byte above ASCII dropped, ASCII's capitals made
    /// small, and <c>is</c> with <c>c</c> alone after it kept as <c>isc</c>.
    /// </summary>
    private static List<int> Key(int kind, List<byte> text, int from, int to)
    {
        var prefixed = to - from >= 2 && (text[from] | 32) == 105 && (text[from + 1] | 32) == 115;
        var output = new List<int> { kind };
        for (var i = prefixed ? from + 2 : from; i < to; i++)
        {
            int b = text[i];
            if (b is not (32 or 95 or 45) && b <= 127)
            {
                output.Add(b >= 65 && b <= 90 ? b + 32 : b);
            }
        }

        return prefixed && output is [_, 99] ? [kind, 105, 115, 99] : output;
    }

    /// <summary>The stream of the classes of <c>\p</c> that <see cref="CaseTables.RegexProperties"/> holds.</summary>
    internal static readonly byte[] Properties = Convert.FromBase64String(CaseTables.RegexProperties);

    /// <summary>
    /// The classes of <c>\p</c> (G4), as the standard library's <c>RegexProperty</c> reads them: the union of the sets of
    /// <paramref name="key"/> in <see cref="Properties"/>, the ranges of each set read from the varints of their gaps and
    /// lengths, or for the name of a property its values' kind as <c>-1 - kind</c>, or none where the stream has no such key.
    /// </summary>
    private static List<int> Lookup(List<int> key)
    {
        var d = Properties;
        var at = 0;
        var end = Varint(d, ref at);
        end += at;
        var sets = end;
        while (at < end)
        {
            int size = d[at];
            var same = size == key.Count;
            for (var i = 0; same && i < size; i++)
            {
                same = d[at + 1 + i] == key[i];
            }

            at += 1 + size;
            var offset = Varint(d, ref at);
            int count = d[at++];
            if (!same)
            {
                continue;
            }

            if (count == 0)
            {
                return [-1 - offset];
            }

            var output = new List<int>();
            at = sets + offset;
            for (var s = 0; s < count; s++)
            {
                var one = new List<int>();
                var last = -1;
                for (var r = Varint(d, ref at); r > 0; r--)
                {
                    var lo = last + 1 + Varint(d, ref at);
                    last = lo + Varint(d, ref at);
                    one.Add(lo);
                    one.Add(last);
                }

                output = Unite(output, one);
            }

            return output;
        }

        return [];
    }

    private static int Varint(byte[] d, ref int at)
    {
        var value = 0;
        for (var shift = 0; ; shift += 7)
        {
            int b = d[at++];
            value |= (b & 127) << shift;
            if (b < 128)
            {
                return value;
            }
        }
    }

    private static List<int> Table(int which) => [.. which switch
    {
        0 => CaseTables.RegexDigit,
        1 => CaseTables.RegexWord,
        2 => CaseTables.RegexSpace,
        _ => CaseTables.RegexFold,
    }];

    /// <summary>Rust's <c>char::is_whitespace</c>: the property White_Space.</summary>
    private static bool White(int cp) => cp == 32 || (cp >= 9 && cp <= 13) || (cp >= 128 && In(CaseTables.RegexSpace, cp));

    /// <summary>Rust's <c>bump_space</c>: under <c>(?x)</c>, the white space and the comments from a <c>#</c> to the line's end.</summary>
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

            if (!White(Decode(p, at)))
            {
                return;
            }

            at += Length(p[at]);
        }
    }

    private bool Starts(string s)
    {
        if (at + s.Length > n)
        {
            return false;
        }

        for (var i = 0; i < s.Length; i++)
        {
            if (p[at + i] != s[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rust's <c>parse_with_comments</c>: one pass over the pattern with a stack of the open groups and alternations instead of the
    /// call stack, so that no depth of nesting runs out of it; then the end of the open groups (<c>pop_group_end</c>) and the count of
    /// the levels (<c>NestLimiter</c>), each only once the whole pattern was read.
    /// </summary>
    private int Parse()
    {
        while (true)
        {
            Skip();
            if (at >= n)
            {
                break;
            }

            int c = p[at];
            var start = at;
            if (c == 40)
            {
                if (!Open())
                {
                    return -1;
                }
            }
            else if (c == 41)
            {
                if (!Close())
                {
                    return -1;
                }
            }
            else if (c == 124)
            {
                Alternate();
                at++;
                concatAt = at;
            }
            else if (c == 91)
            {
                var set = Class();
                if (failKind >= 0)
                {
                    return -1;
                }

                Push(ClassNode(set), setNest, start);
            }
            else if (c is 42 or 43 or 63)
            {
                if (!Repeat())
                {
                    return -1;
                }
            }
            else if (c == 123)
            {
                if (!Counted())
                {
                    return -1;
                }
            }
            else
            {
                var node = Primitive();
                if (failKind >= 0)
                {
                    return -1;
                }

                Push(node, -1, start);
            }
        }

        var root = Level();
        if (groupOpen.Count > 0)
        {
            Fail(0, groupOpen[^1]);
            return -1;
        }

        var deep = Deep(lastNest);
        if (deep >= 0)
        {
            Fail(0, deep);
            return -1;
        }

        if (laterAt >= 0)
        {
            Fail(0, laterAt);
            return -1;
        }

        return root;
    }

    private void Push(int node, int nest, int start)
    {
        itemNode.Add(node);
        itemNest.Add(nest);
        itemStart.Add(start);
    }

    /// <summary>The sequence being read, as Rust's <c>Concat::into_ast</c>, its level in <see cref="lastNest"/>.</summary>
    private int Sequence()
    {
        var nodes = new List<int>();
        var nests = new List<int>();
        for (var i = items0; i < itemNode.Count; i++)
        {
            if (itemNode[i] >= 0)
            {
                nodes.Add(itemNode[i]);
            }

            nests.Add(itemNest[i]);
        }

        var count = itemNode.Count - items0;
        itemNode.RemoveRange(items0, count);
        itemNest.RemoveRange(items0, count);
        itemStart.RemoveRange(items0, count);
        lastNest = count switch
        {
            0 => -1,
            1 => nests[0],
            _ => NestOf(concatAt, nests),
        };
        return nodes.Count switch
        {
            0 => Node(0, 0, 0, 0),
            1 => nodes[0],
            _ => Node(5, Kids(nodes), nodes.Count, 0),
        };
    }

    /// <summary>A <c>|</c>: the sequence before it is a branch of the alternation, which starts where its first branch does.</summary>
    private void Alternate()
    {
        var node = Sequence();
        if (altAt < 0)
        {
            altAt = concatAt;
        }

        branchNode.Add(node);
        branchNest.Add(lastNest);
    }

    /// <summary>The sequence being read, or the alternation of it and the branches before it.</summary>
    private int Level()
    {
        var node = Sequence();
        if (altAt < 0)
        {
            return node;
        }

        var nodes = branchNode.GetRange(branches0, branchNode.Count - branches0);
        var nests = branchNest.GetRange(branches0, branchNest.Count - branches0);
        nodes.Add(node);
        nests.Add(lastNest);
        branchNode.RemoveRange(branches0, branchNode.Count - branches0);
        branchNest.RemoveRange(branches0, branchNest.Count - branches0);
        lastNest = NestOf(altAt, nests);
        altAt = -1;
        return Node(6, Kids(nodes), nodes.Count, 0);
    }

    /// <summary>Rust's <c>push_group</c>: a group opens a sequence of its own, and flags alone are an item of the one being read.</summary>
    private bool Open()
    {
        var open = at;
        var saved = flags;
        var r = Group();
        if (r < 0)
        {
            return false;
        }

        if (r == 0)
        {
            Push(-1, -1, open);
            return true;
        }

        groupOpen.Add(open);
        groupCapture.Add(capture);
        groupFlags.Add(saved);
        groupItems.Add(items0);
        groupConcat.Add(concatAt);
        groupBranches.Add(branches0);
        groupAlt.Add(altAt);
        items0 = itemNode.Count;
        concatAt = at;
        branches0 = branchNode.Count;
        altAt = -1;
        return true;
    }

    /// <summary>Rust's <c>pop_group</c>: a <c>)</c> no group opened is <c>Syntax</c> at it.</summary>
    private bool Close()
    {
        if (groupOpen.Count == 0)
        {
            Fail(0, at);
            return false;
        }

        var child = Level();
        var childNest = lastNest;
        at++;
        var g = groupOpen.Count - 1;
        var open = groupOpen[g];
        var index = groupCapture[g];
        flags = groupFlags[g];
        items0 = groupItems[g];
        concatAt = groupConcat[g];
        branches0 = groupBranches[g];
        altAt = groupAlt[g];
        groupOpen.RemoveAt(g);
        groupCapture.RemoveAt(g);
        groupFlags.RemoveAt(g);
        groupItems.RemoveAt(g);
        groupConcat.RemoveAt(g);
        groupBranches.RemoveAt(g);
        groupAlt.RemoveAt(g);
        var node = index >= 0 ? Node(4, index, child, 0) : index == -6 ? Node(11, 0, child, 0) : index <= -2 ? Node(10, -2 - index, child, 0) : child;
        Push(node, NestOf(open, [childNest]), open);
        return true;
    }

    /// <summary>
    /// Rust's <c>parse_group</c>: a look-around is <c>Unsupported</c> at the <c>(</c>, as Kurz reads an atomic group <c>(?&gt;</c>
    /// (G4), but for the backtracking engine, which takes them (G2); 1 for a group, its capture in <see cref="capture"/> (-1 for
    /// none, and for the backtracking engine -2 for <c>(?=</c>, -3 <c>(?!</c>, -4 <c>(?&lt;=</c>, -5 <c>(?&lt;!</c> and -6
    /// <c>(?&gt;</c>), 0 for flags alone and -1 for an error.
    /// </summary>
    private int Group()
    {
        var open = at;
        at++;
        Skip();
        if (Starts("?=") || Starts("?!") || Starts("?<=") || Starts("?<!") || Starts("?>"))
        {
            if (!backtracking)
            {
                Fail(1, open);
                return -1;
            }

            var look = Starts("?=") ? 0 : Starts("?!") ? 1 : Starts("?<=") ? 2 : Starts("?<!") ? 3 : 4;
            at += look is 2 or 3 ? 3 : 2;
            capture = -2 - look;
            return 1;
        }

        var inner = at;
        if (Starts("?P<") || Starts("?<"))
        {
            at += p[at + 1] == 80 ? 3 : 2;
            var name = Name(true);
            if (failKind >= 0)
            {
                return -1;
            }

            capture = names.Count;
            names.Add(name);
            return 1;
        }

        if (at < n && p[at] == 63)
        {
            at++;
            if (at >= n)
            {
                Fail(0, open);
                return -1;
            }

            return Flags(inner);
        }

        capture = names.Count;
        names.Add("");
        return 1;
    }

    /// <summary>
    /// Rust's <c>parse_capture_name</c>: the name's first character is <c>_</c> or alphabetic, and a later one also <c>.</c>,
    /// <c>[</c>, <c>]</c> or numeric (<c>is_capture_char</c>); an empty name, and a group's name given twice, are <c>Syntax</c> at its
    /// start. A backreference's name, <paramref name="fresh"/> false, names a group that has it.
    /// </summary>
    private string Name(bool fresh)
    {
        if (at >= n)
        {
            Fail(0, at);
            return "";
        }

        var start = at;
        while (p[at] != 62)
        {
            var cp = Decode(p, at);
            var named = cp == 95 || In(CaseTables.RegexAlpha, cp)
                || (at > start && (cp is 46 or 91 or 93 || In(CaseTables.RegexNumber, cp)));
            if (!named)
            {
                Fail(0, at);
                return "";
            }

            at += Length(p[at]);
            if (at >= n)
            {
                Fail(0, at);
                return "";
            }
        }

        var name = Encoding.UTF8.GetString(p, start, at - start);
        at++;
        if (name.Length == 0 || (fresh && names.Contains(name)))
        {
            Fail(0, start);
            return "";
        }

        return name;
    }

    /// <summary>
    /// Rust's <c>parse_flags</c>: the flags up to <c>:</c> or <c>)</c>; a second <c>-</c> and a flag given twice are <c>Syntax</c> at
    /// it, the end of the pattern at the end, a <c>-</c> with no flag after it at the <c>-</c>, and <c>(?)</c> at its <c>?</c>.
    /// </summary>
    private int Flags(int inner)
    {
        var negation = -1;
        var dangling = -1;
        var seen = 0;
        while (p[at] != 58 && p[at] != 41)
        {
            int c = p[at];
            if (c == 45)
            {
                if (negation >= 0)
                {
                    Fail(0, at);
                    return -1;
                }

                negation = at;
                dangling = at;
            }
            else
            {
                dangling = -1;
                var bit = c switch
                {
                    105 => 1,
                    109 => 2,
                    115 => 4,
                    120 => 8,
                    85 => 16,
                    82 => 32,
                    117 => 64,
                    _ => 0,
                };
                if (bit == 0 || (seen & bit) != 0)
                {
                    Fail(0, at);
                    return -1;
                }

                seen |= bit;
                flags = negation >= 0 ? flags & (127 - bit) : flags | bit;
            }

            at++;
            if (at >= n)
            {
                Fail(0, at);
                return -1;
            }
        }

        if (dangling >= 0)
        {
            Fail(0, dangling);
            return -1;
        }

        var alone = p[at] == 41;
        at++;
        if (alone && seen == 0 && negation < 0)
        {
            Fail(0, inner);
            return -1;
        }

        capture = -1;
        return alone ? 0 : 1;
    }

    /// <summary>
    /// Rust's <c>parse_uncounted_repetition</c>: <c>*</c>, <c>+</c> or <c>?</c> of the item before it, lazy with a <c>?</c> right after
    /// it; with no item before it, or flags, <c>Syntax</c> at it.
    /// </summary>
    private bool Repeat()
    {
        var last = itemNode.Count - 1;
        if (last < items0 || itemNode[last] < 0)
        {
            Fail(0, at);
            return false;
        }

        int d = p[at];
        at++;
        var lazy = at < n && p[at] == 63;
        if (lazy)
        {
            at++;
        }

        Repeated(d == 43 ? 1 : 0, d == 63 ? 1 : -1, lazy);
        return true;
    }

    /// <summary>
    /// The last item repeated, the other way round under <c>(?U)</c>; a node that matches no character is repeated at most once, as
    /// Rust's <c>Hir::repetition</c> does, so that <c>(?:){4294967295}</c> builds.
    /// </summary>
    private void Repeated(long min, long max, bool lazy)
    {
        var last = itemNode.Count - 1;
        var child = itemNode[last];
        if (zero[child])
        {
            min = Math.Min(min, 1);
            max = max < 0 ? 1 : Math.Min(max, 1);
        }

        var greedy = lazy == ((flags & 16) != 0);
        itemNode[last] = Node(greedy ? 7 : 8, child, (int)Math.Min(min, int.MaxValue), (int)Math.Min(max, int.MaxValue));
        itemNest[last] = NestOf(itemStart[last], [itemNest[last]]);
    }

    /// <summary>
    /// Rust's <c>parse_counted_repetition</c>: <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>, each count up to 4294967295; the end of the
    /// pattern, and a brace not closed, are <c>Syntax</c> at the brace, a missing or too large count at its digits, and a most below the
    /// least at the brace.
    /// </summary>
    private bool Counted()
    {
        var open = at;
        var last = itemNode.Count - 1;
        if (last < items0 || itemNode[last] < 0)
        {
            Fail(0, at);
            return false;
        }

        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, open);
            return false;
        }

        var lo = Decimal();
        var loAt = decimalAt;
        if (at >= n)
        {
            Fail(0, open);
            return false;
        }

        long hi = lo;
        if (p[at] == 44)
        {
            at++;
            Skip();
            if (at >= n)
            {
                Fail(0, open);
                return false;
            }

            if (lo < 0)
            {
                Fail(0, loAt);
                return false;
            }

            hi = -1;
            if (p[at] != 125)
            {
                hi = Decimal();
                if (hi < 0)
                {
                    Fail(0, decimalAt);
                    return false;
                }
            }
        }
        else if (lo < 0)
        {
            Fail(0, loAt);
            return false;
        }

        if (at >= n || p[at] != 125)
        {
            Fail(0, open);
            return false;
        }

        at++;
        Skip();
        var lazy = at < n && p[at] == 63;
        if (lazy)
        {
            at++;
        }

        if (hi >= 0 && lo > hi)
        {
            Fail(0, open);
            return false;
        }

        Repeated(lo, hi, lazy);
        return true;
    }

    /// <summary>
    /// Rust's <c>parse_decimal</c>: white space before and after the digits whether or not <c>(?x)</c> holds, the digits' start in
    /// <see cref="decimalAt"/>; -1 for no digit and -2 for a count above 4294967295, Rust's <c>u32</c>.
    /// </summary>
    private long Decimal()
    {
        while (at < n && White(Decode(p, at)))
        {
            at += Length(p[at]);
        }

        decimalAt = at;
        long value = 0;
        var digits = false;
        var big = false;
        while (at < n && p[at] >= 48 && p[at] <= 57)
        {
            value = value * 10 + (p[at] - 48);
            if (value > 4294967295)
            {
                big = true;
                value = 4294967295;
            }

            digits = true;
            at++;
            Skip();
        }

        while (at < n && White(Decode(p, at)))
        {
            at += Length(p[at]);
            Skip();
        }

        return !digits ? -1 : big ? -2 : value;
    }

    /// <summary>A backreference's node, its group resolved by <see cref="Refs"/> once every group is known.</summary>
    private int Reference()
    {
        var node = Node(9, 0, (flags & 1) != 0 ? 1 : 0, 0);
        refNode.Add(node);
        refAt.Add(escStart);
        refNumber.Add(escRef);
        refName.Add(escName);
        return node;
    }

    private int Primitive()
    {
        int c = p[at];
        if (c == 46)
        {
            if ((flags & 64) == 0)
            {
                Later(at);
            }

            at++;
            return (flags & 4) != 0 ? ClassNode([0, 1114111])
                : (flags & 32) != 0 ? ClassNode([0, 9, 11, 12, 14, 1114111])
                : ClassNode([0, 9, 11, 1114111]);
        }

        if (c == 94)
        {
            at++;
            return Node(3, (flags & 2) == 0 ? 0 : (flags & 32) != 0 ? 10 : 2, 0, 0);
        }

        if (c == 36)
        {
            at++;
            return Node(3, (flags & 2) == 0 ? 1 : (flags & 32) != 0 ? 11 : 3, 0, 0);
        }

        if (c == 92)
        {
            return Escape() switch
            {
                0 => -1,
                1 => EscapedLiteral(),
                2 => ClassNode(escSet),
                4 => Reference(),
                _ => Assertion(),
            };
        }

        var cp = Decode(p, at);
        at += Length(p[at]);
        return Literal(cp);
    }

    /// <summary>
    /// The character of an escape; under <c>(?-u)</c> <c>\xNN</c> above 7F, which Rust's translator reads as a byte, is refused,
    /// since a byte above ASCII is no character. A character above ASCII written another way is taken as its UTF-8.
    /// </summary>
    private int EscapedLiteral()
    {
        if ((flags & 64) == 0 && escCp > 127 && p[escStart + 1] == 120 && p[at - 1] != 125)
        {
            Later(escStart);
        }

        return Literal(escCp);
    }

    /// <summary>
    /// The assertion of an escape; under <c>(?-u)</c> the word's assertions of ASCII, 12 to 17.
    /// </summary>
    private int Assertion()
    {
        if ((flags & 64) == 0 && escCp >= 4)
        {
            return Node(3, escCp + 8, 0, 0);
        }

        return Node(3, escCp, 0, 0);
    }

    /// <summary>A construct Rust's translator refuses, at <paramref name="start"/>: the first one is the error once the parse and the levels found none.</summary>
    private void Later(int start)
    {
        if (laterAt < 0)
        {
            laterAt = start;
        }
    }

    /// <summary><paramref name="set"/> folded as <c>(?i)</c> folds it: by the simple case folding of G4's table, or under <c>(?-u)</c> ASCII's letters alone.</summary>
    private List<int> Folded(List<int> set) => (flags & 64) != 0 ? Fold(set)
        : Unite(set, Unite(Shift(Intersect(set, [97, 122]), -32), Shift(Intersect(set, [65, 90]), 32)));

    private static List<int> Shift(List<int> set, int by) => [.. set.Select(x => x + by)];

    /// <summary>
    /// A class that is a set of bytes under <c>(?-u)</c>, at <paramref name="start"/>: one that holds a character above ASCII,
    /// after its negation, is refused there, since it would match a byte of no character, as Rust's translator refuses it.
    /// </summary>
    private void Bytewise(List<int> set, int start)
    {
        if ((flags & 64) == 0 && set.Count > 0 && set[^1] > 127)
        {
            Later(start);
        }
    }

    /// <summary>
    /// Rust's <c>parse_escape</c>: 1 for a character in <see cref="escCp"/>, 2 for a class in <see cref="escSet"/>, 3 for an assertion
    /// in <see cref="escCp"/> (0 <c>\A</c>, 1 <c>\z</c>, 4 <c>\b</c>, 5 <c>\B</c>, 6 <c>\b{start}</c> and <c>\&lt;</c>, 7
    /// <c>\b{end}</c> and <c>\&gt;</c>, 8 <c>\b{start-half}</c>, 9 <c>\b{end-half}</c>) and 0 for an error; a backreference is
    /// <c>Unsupported</c> at the <c>\</c>, the backtracking engine's (G4), and so is <c>\k</c>. For the backtracking engine (G2), 4 for a
    /// backreference: <c>\</c> and a digit from 1 on, the group of the number its digits write, or <c>\k&lt;name&gt;</c>, in
    /// <see cref="escRef"/>, -1 with the name in <see cref="escName"/>.
    /// </summary>
    private int Escape()
    {
        var start = at;
        escStart = at;
        at++;
        if (at >= n)
        {
            Fail(0, start);
            return 0;
        }

        int c = p[at];
        if (backtracking && c >= 49 && c <= 57)
        {
            var number = 0;
            while (at < n && p[at] >= 48 && p[at] <= 57)
            {
                if (number < 100000000)
                {
                    number = number * 10 + p[at] - 48;
                }

                at++;
            }

            escRef = number;
            escName = "";
            return 4;
        }

        if (backtracking && c == 107)
        {
            at++;
            if (at >= n || p[at] != 60)
            {
                Fail(0, start);
                return 0;
            }

            at++;
            var name = Name(false);
            if (failKind >= 0)
            {
                return 0;
            }

            escRef = -1;
            escName = name;
            return 4;
        }

        if ((c >= 48 && c <= 57) || c == 107)
        {
            Fail(1, start);
            return 0;
        }

        if (c is 120 or 117 or 85)
        {
            return HexEscape(c) ? 1 : 0;
        }

        if (c is 112 or 80)
        {
            return Property(c == 80, start) ? 2 : 0;
        }

        if (c is 100 or 68 or 119 or 87 or 115 or 83)
        {
            at++;
            var which = c is 119 or 87 ? 1 : c is 115 or 83 ? 2 : 0;
            List<int> set = (flags & 64) != 0 ? Table(which) : which == 0 ? [48, 57] : which == 1 ? [48, 57, 65, 90, 95, 95, 97, 122] : [9, 13, 32, 32];
            escSet = c < 97 ? Negate(set) : set;
            if (c < 97)
            {
                Bytewise(escSet, start);
            }

            return 2;
        }

        at += Length(p[at]);
        if (c < 128 && !(c >= 65 && c <= 90) && !(c >= 97 && c <= 122) && c != 60 && c != 62)
        {
            escCp = c;
            return 1;
        }

        var simple = c switch
        {
            97 => 7,
            102 => 12,
            116 => 9,
            110 => 10,
            114 => 13,
            118 => 11,
            _ => -1,
        };
        if (simple >= 0)
        {
            escCp = simple;
            return 1;
        }

        escCp = c switch
        {
            65 => 0,
            122 => 1,
            98 => 4,
            66 => 5,
            60 => 6,
            62 => 7,
            _ => -1,
        };
        if (escCp < 0)
        {
            Fail(0, start);
            return 0;
        }

        if (escCp >= 4 && (flags & 64) != 0)
        {
            usesWord = true;
        }

        if (c == 98 && at < n && p[at] == 123)
        {
            return Boundary(start) ? 3 : 0;
        }

        return 3;
    }

    /// <summary>
    /// Rust's <c>maybe_parse_special_word_boundary</c>: <c>\b{start}</c>, <c>\b{end}</c>, <c>\b{start-half}</c> or
    /// <c>\b{end-half}</c>; a brace whose next character is no letter and no <c>-</c> is a counted repetition of <c>\b</c>.
    /// </summary>
    private bool Boundary(int start)
    {
        var brace = at;
        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, start);
            return false;
        }

        var contents = at;
        static bool Letter(byte b) => (b >= 65 && b <= 90) || (b >= 97 && b <= 122) || b == 45;
        if (!Letter(p[at]))
        {
            at = brace;
            return true;
        }

        var word = new StringBuilder();
        while (at < n && Letter(p[at]))
        {
            word.Append((char)p[at]);
            at++;
            Skip();
        }

        if (at >= n || p[at] != 125)
        {
            Fail(0, brace);
            return false;
        }

        at++;
        escCp = word.ToString() switch
        {
            "start" => 6,
            "end" => 7,
            "start-half" => 8,
            "end-half" => 9,
            _ => -1,
        };
        if (escCp < 0)
        {
            Fail(0, contents);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Rust's <c>parse_hex</c>: <c>\x</c> of two digits, <c>\u</c> of four, <c>\U</c> of eight, or any of them with any number of
    /// digits in braces; a value that is no scalar value is <c>Syntax</c> at its first digit.
    /// </summary>
    private bool HexEscape(int c)
    {
        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, at);
            return false;
        }

        long value = 0;
        int first;
        if (p[at] == 123)
        {
            var brace = at;
            first = at + 1;
            var count = 0;
            while (true)
            {
                at++;
                Skip();
                if (at >= n || p[at] == 125)
                {
                    break;
                }

                var h = Hex(p[at]);
                if (h < 0)
                {
                    Fail(0, at);
                    return false;
                }

                value = Math.Min(value * 16 + h, 1114112);
                count++;
            }

            if (at >= n)
            {
                Fail(0, brace);
                return false;
            }

            at++;
            Skip();
            if (count == 0)
            {
                Fail(0, brace);
                return false;
            }
        }
        else
        {
            first = at;
            var digits = c == 120 ? 2 : c == 117 ? 4 : 8;
            for (var i = 0; i < digits; i++)
            {
                if (i > 0)
                {
                    at++;
                    Skip();
                    if (at >= n)
                    {
                        Fail(0, at);
                        return false;
                    }
                }

                var h = Hex(p[at]);
                if (h < 0)
                {
                    Fail(0, at);
                    return false;
                }

                value = value * 16 + h;
            }

            at++;
            Skip();
        }

        if (value > 1114111 || (value >= 55296 && value <= 57343))
        {
            Fail(0, first);
            return false;
        }

        escCp = (int)value;
        return true;
    }

    /// <summary>
    /// Rust's <c>parse_set_class</c>: a bracketed class and every class nested in it in one pass, with a stack of the open brackets;
    /// its level in <see cref="setNest"/>. The end of the pattern inside it is <c>Syntax</c> at the innermost open bracket.
    /// </summary>
    private List<int> Class()
    {
        OpenClass();
        while (failKind < 0)
        {
            Skip();
            if (at >= n)
            {
                Fail(0, classOpen[^1]);
                break;
            }

            int c = p[at];
            var start = at;
            if (c == 91)
            {
                if (Ascii())
                {
                    AddItem(asciiSet, -1, start);
                }
                else
                {
                    OpenClass();
                }

                continue;
            }

            if (c == 93)
            {
                if (CloseClass())
                {
                    return setValue;
                }

                continue;
            }

            if (at + 1 < n && (c is 38 or 45 or 126) && p[at + 1] == c)
            {
                at += 2;
                var k = classOpen.Count - 1;
                IntoItem(k);
                classLhs[k] = setValue;
                classLhsNest[k] = setNest;
                classLhsStart[k] = setStart;
                classOp[k] = c;
                classUnion[k] = [];
                classCount[k] = 0;
                classFirst[k] = at;
                continue;
            }

            Range();
        }

        return [];
    }

    /// <summary>
    /// Rust's <c>parse_set_class_open</c>: a <c>[</c>, its <c>^</c>, every <c>-</c> right after them as a character, then a <c>]</c>
    /// as a character where no item came before it.
    /// </summary>
    private void OpenClass()
    {
        var open = at;
        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, open);
            return;
        }

        var negated = false;
        if (p[at] == 94)
        {
            negated = true;
            at++;
            Skip();
            if (at >= n)
            {
                Fail(0, open);
                return;
            }
        }

        classOpen.Add(open);
        classNegated.Add(negated);
        classUnion.Add([]);
        classCount.Add(0);
        classFirst.Add(at);
        classItems.Add(unionNest.Count);
        classOp.Add(-1);
        classLhs.Add([]);
        classLhsNest.Add(-1);
        classLhsStart.Add(0);
        while (p[at] == 45)
        {
            AddItem([45, 45], -1, at);
            at++;
            Skip();
            if (at >= n)
            {
                Fail(0, open);
                return;
            }
        }

        if (classCount[^1] == 0 && p[at] == 93)
        {
            AddItem([93, 93], -1, at);
            at++;
            Skip();
            if (at >= n)
            {
                Fail(0, open);
            }
        }
    }

    private void AddItem(List<int> set, int nest, int start)
    {
        var k = classOpen.Count - 1;
        classUnion[k] = Unite(classUnion[k], set);
        if (classCount[k] == 0)
        {
            classFirst[k] = start;
        }

        classCount[k]++;
        unionNest.Add(nest);
    }

    /// <summary>
    /// Rust's <c>ClassSetUnion::into_item</c> and <c>pop_class_op</c>: the items of the innermost bracket since its opening or its last
    /// operation, then that operation applied, each side folded first under <c>(?i)</c> as Rust's translator folds them; the set, its
    /// level and its start in <see cref="setValue"/>, <see cref="setNest"/> and <see cref="setStart"/>.
    /// </summary>
    private void IntoItem(int k)
    {
        var count = classCount[k];
        var b = classItems[k];
        setValue = classUnion[k];
        setStart = classFirst[k];
        setNest = count switch
        {
            0 => -1,
            1 => unionNest[b],
            _ => NestOf(setStart, unionNest.GetRange(b, count)),
        };
        unionNest.RemoveRange(b, unionNest.Count - b);
        var op = classOp[k];
        if (op < 0)
        {
            return;
        }

        var lhs = classLhs[k];
        var rhs = setValue;
        if ((flags & 1) != 0)
        {
            lhs = Folded(lhs);
            rhs = Folded(rhs);
        }

        setValue = op == 38 ? Intersect(lhs, rhs)
            : op == 45 ? Intersect(lhs, Negate(rhs))
            : Unite(Intersect(lhs, Negate(rhs)), Intersect(rhs, Negate(lhs)));
        setNest = NestOf(classLhsStart[k], [classLhsNest[k], setNest]);
        setStart = classLhsStart[k];
    }

    /// <summary>Rust's <c>pop_class</c>: a <c>]</c>, true where it closes the outermost bracket.</summary>
    private bool CloseClass()
    {
        var k = classOpen.Count - 1;
        IntoItem(k);
        at++;
        var set = setValue;
        if ((flags & 1) != 0)
        {
            set = Folded(set);
        }

        if (classNegated[k])
        {
            set = Negate(set);
        }

        var open = classOpen[k];
        Bytewise(set, open);
        var nest = NestOf(open, [setNest]);
        classOpen.RemoveAt(k);
        classNegated.RemoveAt(k);
        classUnion.RemoveAt(k);
        classCount.RemoveAt(k);
        classFirst.RemoveAt(k);
        classItems.RemoveAt(k);
        classOp.RemoveAt(k);
        classLhs.RemoveAt(k);
        classLhsNest.RemoveAt(k);
        classLhsStart.RemoveAt(k);
        if (k == 0)
        {
            setValue = set;
            setNest = nest;
            return true;
        }

        AddItem(set, nest, open);
        return false;
    }

    /// <summary>
    /// Rust's <c>parse_set_class_range</c>: an item, or two joined by <c>-</c> where the next character past white space under
    /// <c>(?x)</c> is no <c>]</c> and no <c>-</c>. An assertion is <c>Syntax</c> at its start, a side that is no character at that
    /// side, and a range whose end is below its start at its start.
    /// </summary>
    private void Range()
    {
        var first = at;
        var k1 = ClassItem();
        if (k1 == 0)
        {
            return;
        }

        var lo = escCp;
        var set = escSet;
        Skip();
        if (at >= n)
        {
            Fail(0, classOpen[^1]);
            return;
        }

        if (p[at] != 45 || PeekSpace() is 93 or 45)
        {
            if (k1 == 3)
            {
                Fail(0, first);
                return;
            }

            AddItem(k1 == 2 ? set : [lo, lo], -1, first);
            return;
        }

        at++;
        Skip();
        if (at >= n)
        {
            Fail(0, classOpen[^1]);
            return;
        }

        var second = at;
        var k2 = ClassItem();
        if (k2 == 0)
        {
            return;
        }

        if (k1 != 1 || k2 != 1 || escCp < lo)
        {
            Fail(0, k1 == 1 && k2 != 1 ? second : first);
            return;
        }

        AddItem([lo, escCp], -1, first);
    }

    /// <summary>A backreference inside a class is <c>Unsupported</c> at its <c>\</c>, as Rust's parse reads one there.</summary>
    private int ClassItem()
    {
        var start = at;
        if (p[at] == 92)
        {
            var r = Escape();
            if (r == 4)
            {
                Fail(1, escStart);
                return 0;
            }

            if (r == 1 && (flags & 64) == 0 && escCp > 127)
            {
                Later(start);
            }

            return r;
        }

        escCp = Decode(p, at);
        at += Length(p[at]);
        if ((flags & 64) == 0 && escCp > 127)
        {
            Later(start);
        }

        return 1;
    }

    /// <summary>
    /// Rust's <c>peek_space</c>: the character after the one being read, past white space and one <c>#</c> under <c>(?x)</c>; -1 at
    /// the pattern's end.
    /// </summary>
    private int PeekSpace()
    {
        var k = at + Length(p[at]);
        if ((flags & 8) != 0)
        {
            var comment = false;
            while (k < n)
            {
                var cp = Decode(p, k);
                if (White(cp))
                {
                    k += Length(p[k]);
                }
                else if (!comment && cp == 35)
                {
                    comment = true;
                    k++;
                }
                else
                {
                    break;
                }
            }
        }

        return k < n ? Decode(p, k) : -1;
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

        if (k == nameAt || k + 1 >= n || p[k] != 58 || p[k + 1] != 93 || at + 1 >= n || p[at + 1] != 58)
        {
            return false;
        }

        var set = AsciiClass(Encoding.UTF8.GetString(p, nameAt, k - nameAt));
        if (set.Count == 0)
        {
            return false;
        }

        // Rust's `unicode_fold_and_negate`: under `(?i)` the class is folded before it is negated, so that `(?i)[[:^lower:]]`
        // takes no letter of either case.
        if ((flags & 1) != 0)
        {
            set = Folded(set);
        }

        var begin = at;
        at = k + 2;
        asciiSet = negated ? Negate(set) : set;
        if (negated)
        {
            Bytewise(asciiSet, begin);
        }
        return true;
    }

    /// <summary>A level of Rust's <c>NestLimiter</c> that starts at <paramref name="start"/>, over its children, -1 for none.</summary>
    private int NestOf(int start, List<int> children)
    {
        var first = nestKids.Count;
        var height = 0;
        foreach (var child in children)
        {
            if (child >= 0)
            {
                nestKids.Add(child);
                height = Math.Max(height, nestHeight[child]);
            }
        }

        nestStart.Add(start);
        nestHeight.Add(height + 1);
        nestFirst.Add(first);
        nestCount.Add(nestKids.Count - first);
        return nestStart.Count - 1;
    }

    /// <summary>
    /// Rust's <c>NestLimiter</c> over the whole tree: the start of the first node, in the order it visits them, that is more than 250
    /// levels deep, its <c>nest_limit</c>, or -1 where none is.
    /// </summary>
    private int Deep(int root)
    {
        if (root < 0 || nestHeight[root] <= 250)
        {
            return -1;
        }

        var node = root;
        for (var depth = 1; depth <= 250; depth++)
        {
            for (var i = 0; i < nestCount[node]; i++)
            {
                var child = nestKids[nestFirst[node] + i];
                if (nestHeight[child] >= 251 - depth)
                {
                    node = child;
                    break;
                }
            }
        }

        return nestStart[node];
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

        if (k <= 3 || k == 9)
        {
            return 1;
        }

        if (k is 4 or 10 or 11)
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
