using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions of regular expressions (G1 to G5) that the corpus cases do not pin. The checker's reading of a literal pattern
/// (<see cref="RegexPattern"/>) is a copy of the standard library's parse and count, so a program built here gives every pattern of a
/// list, written by hand and generated, to <c>Regex.New</c> at run time and prints what it makes, which the copy must give alike: the
/// error and its byte, or a pattern that builds, on both sides of the bound of the automaton (G2). The matches of both engines are held
/// to .NET's backtracking engine, whose leftmost-first order is Rust's, over generated patterns of ASCII letters, classes, anchors,
/// groups, alternatives and repetitions greedy and lazy, on generated texts without a line end, where the two read every construct
/// alike; .NET's matches are walked as Rust's <c>find_iter</c> walks them, an empty match that ends where the last one ended passed
/// over; so are the backtracking engine's backreferences, look-arounds and atomic groups. Both programs run a second time under the
/// allocator that makes every block it frees inaccessible.
/// </summary>
public sealed class RegexTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Seed = 1101;

    /// <summary>
    /// Patterns written by hand, each with the reading Rust's <c>regex-syntax</c> 0.8 gives it in the words of <c>RegexError</c>, but
    /// where G2 or G4 states Kurz's own, which its comment names: each kind of error at its byte, constructs the linear engine does
    /// not take, the levels of nesting, and the bound.
    /// </summary>
    private static readonly (string Pattern, string Reading)[] Written =
    [
        ("", "ok"), ("a", "ok"), ("a)", "syntax at 1"), ("(a", "syntax at 0"), ("((a)", "syntax at 0"), ("[a", "syntax at 0"),
        ("[]", "syntax at 0"), ("[]a]", "ok"), ("[^]a]", "ok"), ("a{", "syntax at 1"), ("a{1", "syntax at 1"), ("a{1,", "syntax at 1"),
        ("a{2,1}", "syntax at 1"), ("a{,2}", "syntax at 2"), ("{1}", "syntax at 0"), ("*a", "syntax at 0"), ("a**", "ok"),
        ("a|*", "syntax at 2"), ("(?", "syntax at 0"), ("(?:", "syntax at 0"), ("(?i", "syntax at 3"), ("(?i)", "ok"),
        ("(?i-i)", "syntax at 4"), ("(?ii)", "syntax at 3"), ("(?-)", "syntax at 2"), ("(?z)", "syntax at 2"),
        ("(?R)", "unsupported at 2"), // G4: a flag Rust's syntax has that the build does not hold
        ("(?u)", "unsupported at 2"),
        ("(?U)a*", "ok"), ("(?x) a b # c", "ok"), ("(?s).", "ok"), ("(?m)^a$", "ok"), ("\\", "syntax at 0"), ("\\q", "syntax at 0"),
        ("\\d\\D\\w\\W\\s\\S", "ok"), ("\\x41", "ok"), ("\\x{1F600}", "ok"), ("\\x{110000}", "syntax at 3"), ("\\x4", "syntax at 3"),
        ("\\u0041", "ok"),
        ("\\p{L}", "unsupported at 0"), // G4: a class of Unicode by its name, Rust's syntax the build does not hold
        ("\\P{L}", "unsupported at 0"),
        ("\\pL", "unsupported at 0"),
        ("\\k<a>", "unsupported at 0"), // the backtracking engine's (G4), where Rust's syntax has no such escape
        ("\\1", "unsupported at 0"), ("(a)\\1", "unsupported at 3"), ("\\<", "ok"), ("\\>", "ok"), ("\\b{start}", "ok"),
        ("\\A\\z", "ok"), ("\\Z", "syntax at 0"), ("\\G", "syntax at 0"), ("(?=a)", "unsupported at 0"), ("(?!a)", "unsupported at 0"),
        ("(?<=a)", "unsupported at 0"), ("(?<!a)", "unsupported at 0"),
        ("(?>a)", "unsupported at 0"), // the backtracking engine's (G4), where Rust's syntax has no such group
        ("(?<n>a)", "ok"), ("(?P<n>a)", "ok"), ("(?<n>a)(?<n>b)", "syntax at 10"), ("(?<1n>a)", "syntax at 3"),
        ("(?<>a)", "syntax at 3"), ("(?P=n)", "syntax at 2"), ("[[:alpha:]]", "ok"), ("[[:word:]]", "ok"), ("[[:foo:]]", "ok"),
        ("[a-]", "ok"), ("[-a]", "ok"), ("[z-a]", "syntax at 1"), ("[a&&b]", "ok"), ("[a--b]", "ok"), ("[a~~b]", "ok"),
        ("[\\d-z]", "syntax at 1"), ("é(", "syntax at 2"), ("é{", "syntax at 2"), ("[é-a]", "syntax at 1"), ("a{1001}", "ok"),
        ("(?:a{1000}){1000}", "too big at 0"), ("(?:\\w{1000}){1000}", "too big at 0"),
        ("a{655357}", "ok"), // the bound's own line (G2), where Rust's automaton counts its states otherwise
        ("a{655358}", "too big at 0"), ("(a){327677}", "too big at 0"), ("x{4294967296}", "syntax at 2"), ("a{ 2 }", "ok"),
        ("a{2 ,3}", "ok"), ("a{2\u00a0}", "ok"), ("a{}", "syntax at 2"), ("a{x}", "syntax at 2"), ("a{,}", "syntax at 2"),
        ("a{99999999999}", "syntax at 2"), ("a{4294967295}", "too big at 0"), ("(?:){4294967295}", "ok"), ("\\b{4294967295}", "ok"),
        ("a{2}?", "ok"), ("(?x)a{2} ?", "ok"), ("a{2", "syntax at 1"), ("a{2,", "syntax at 1"), ("a{2x}", "syntax at 1"),
        ("(?)", "syntax at 1"), ("( ?)", "ok"), ("(?x)( ?)", "syntax at 6"), ("(?i-)", "syntax at 3"), ("(?--i)", "syntax at 3"),
        ("(?i:", "syntax at 0"), ("(?x)( ?:a)", "ok"), ("(?x)( a)", "ok"), ("(?<a", "syntax at 4"), ("(?<", "syntax at 3"),
        ("(?P<", "syntax at 4"), ("(?<a-", "syntax at 4"), ("(?<a²>x)", "ok"), ("(?<\u0663x>x)", "syntax at 3"),
        ("(?<a\u0301>x)", "syntax at 4"), ("(?<a.b[c]>x)", "ok"), ("(?<_1>x)", "ok"), ("\\x", "syntax at 2"), ("\\x{}", "syntax at 2"),
        ("\\x{D800}", "syntax at 3"), ("\\x{ 41 }", "syntax at 3"), ("\\x{000000041}", "ok"), ("\\x{FFFFFFFFF}", "syntax at 3"),
        ("(?x)\\x4 1", "ok"), ("\\é", "syntax at 0"), ("\\ ", "ok"), ("\\-", "ok"), ("\\b{end}", "ok"), ("\\b{start-half}", "ok"),
        ("\\b{end-half}", "ok"), ("\\b{foo}", "syntax at 3"), ("\\b{", "syntax at 0"), ("\\b{start", "syntax at 2"),
        ("\\B{start}", "syntax at 3"), ("\\b{2}", "ok"), ("(?x)\\b{ start }", "ok"), ("[\\<]", "syntax at 1"), ("[\\b]", "syntax at 1"),
        ("[\\b{start}]", "syntax at 1"), ("[a-\\d]", "syntax at 3"), ("[\\d-\\w]", "syntax at 1"), ("[--a]", "ok"), ("[]-a]", "ok"),
        ("[&&a]", "ok"), ("[a&&]", "ok"), ("[^-]", "ok"), ("[a-[]", "syntax at 1"), ("[[a]", "syntax at 0"), ("[a[]]", "syntax at 0"),
        ("[a-]b]", "ok"), ("(?x)[a - z]", "ok"), ("(?x)[z - a]", "syntax at 5"), ("(?x)[\\d - z]", "syntax at 5"),
        ("(?x)[a-#c\nz]", "ok"), ("(?x)[ ^a]", "ok"), ("[[:^digit:]]", "ok"), ("[:alpha:]", "ok"), ("((((a", "syntax at 3"),
        ("a))", "syntax at 1"), ("(a|b", "syntax at 0"), ("a|b)", "syntax at 3"), ("(?i)a(", "syntax at 5"), ("[a&&[b", "syntax at 4"),
        (")", "syntax at 0"),
        ("\\pL)", "unsupported at 0"), // the first error of the pattern decides, as the parse reads it
        ("a)\\pL", "syntax at 1"),
        ("(?R)a)", "unsupported at 2"),
        ("a)(?R)", "syntax at 1"),
        // the levels of Rust's `NestLimiter`, at most 250, each counted after the whole pattern was read, at the start of the node past it
        (Repeat("(", 250) + "a" + Repeat(")", 250), "ok"), (Repeat("(", 251) + "a" + Repeat(")", 251), "syntax at 250"),
        ("a" + Repeat("(", 249) + "a" + Repeat(")", 249), "ok"), ("a" + Repeat("(", 250) + "a" + Repeat(")", 250), "syntax at 250"),
        ("x" + Repeat("(x", 124) + "|y" + Repeat(")", 124), "ok"), ("x" + Repeat("(x", 125) + Repeat(")", 125) + "|y", "syntax at 249"),
        ("a" + Repeat("*", 250), "ok"), ("a" + Repeat("*", 251), "syntax at 0"),
        (Repeat("(?:", 250) + "a" + Repeat(")", 250), "ok"), (Repeat("(?:", 251) + "a" + Repeat(")", 251), "syntax at 750"),
        (Repeat("[", 250) + "a" + Repeat("]", 250), "ok"), (Repeat("[", 251) + "a" + Repeat("]", 251), "syntax at 250"),
        ("[" + Repeat("a&&", 249) + "b]", "ok"), ("[" + Repeat("a&&", 250) + "b]", "syntax at 1"),
        // an error the parse finds after the levels is the pattern's error: no level is counted before the whole pattern was read
        (Repeat("(", 251) + "a" + Repeat(")", 250), "syntax at 0"), (Repeat("(", 251) + "a" + Repeat(")", 251) + "\\", "syntax at 503"),
    ];

    private static string Repeat(string s, int count) => string.Concat(Enumerable.Repeat(s, count));

    /// <summary>The characters of the syntax, one of which is put into a generated pattern.</summary>
    private const string Syntax = "()[]{}*+?\\|^$.-,:<>";

    /// <summary>The largest count of one character under the bound of the automaton (G2), and the next.</summary>
    private static readonly string[] Bound = ["a{655357}", "a{655358}"];

    /// <summary>
    /// G3: the checker's copy of the parse and the count, and the standard library's <c>Regex.New</c> when the program runs, give each
    /// written pattern the reading written beside it, and every generated pattern the same reading as each other: the error's kind and
    /// byte, or a pattern that builds. The generated patterns all build, and each of them with a character of the syntax put in at a
    /// place mostly does not.
    /// </summary>
    [Fact]
    public void The_checkers_reading_of_a_pattern_is_what_regex_new_gives_when_the_program_runs()
    {
        var generator = new Generator(Seed);
        var random = new Random(Seed + 1);
        foreach (var (p, reading) in Written)
        {
            Assert.True(Read(p) == reading, $"`{p}`: written `{reading}`, the checker reads `{Read(p)}`");
        }

        var patterns = Written.Select(w => w.Pattern).ToList();
        for (var i = 0; i < 150; i++)
        {
            var p = generator.Pattern();
            patterns.Add(p);
            var at = random.Next(p.Length + 1);
            patterns.Add(p[..at] + Syntax[random.Next(Syntax.Length)] + p[at..]);
        }

        Assert.DoesNotContain(patterns, p => p.Contains('"', StringComparison.Ordinal));
        var expected = Written.Select(w => w.Reading).Concat(patterns.Skip(Written.Length).Select(Read)).ToList();
        Assert.Contains(expected, e => e.StartsWith("syntax at ", StringComparison.Ordinal));
        Assert.Contains(expected, e => e.StartsWith("unsupported at ", StringComparison.Ordinal));
        Assert.Contains(expected, e => e.StartsWith("too big at ", StringComparison.Ordinal));
        Assert.True(expected.Count(e => e == "ok") > 150, "the generated patterns build");

        // the bound of the automaton (G2): the largest count of one character under it builds, the next is too big
        Assert.Equal(["ok", "too big at 0"], Bound.Select(Read));

        var program = new StringBuilder("patterns = [\n");
        foreach (var p in patterns)
        {
            program.Append("    \"").Append(Escaped(p)).Append("\",\n");
        }

        program.Append("]\nfor p in patterns {\n    match Regex.New(p) {\n        Regex re => print(\"ok\")\n        RegexError e => print(e)\n    }\n}\n");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "regex-parse" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Count, output.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i] == output[i], $"`{patterns[i]}`: the checker reads `{expected[i]}`, the program gives `{output[i]}`");
            }
        }
    }

    /// <summary>
    /// G2, G4: the matches of each engine, <c>Matches</c>, <c>Match</c> and <c>IsMatch</c>, each match's position, its length and
    /// every group's, are .NET's for the same pattern and text, walked as Rust's <c>find_iter</c> walks them, over 300 generated
    /// patterns and 12 texts each. The generator writes no named group, which .NET numbers after the others, no repetition of what
    /// can match nothing, where backtracking engines stop a loop early, and no line end in a text, where .NET's <c>$</c> matches before
    /// one.
    /// </summary>
    [Fact]
    public void Both_engines_find_the_matches_and_groups_a_leftmost_first_engine_finds()
    {
        var generator = new Generator(Seed + 2);
        var random = new Random(Seed + 3);
        var patterns = new List<(string Pattern, int Groups)>();
        for (var i = 0; i < 300; i++)
        {
            var p = generator.Pattern();
            Assert.Null(RegexPattern.Check(p));
            patterns.Add((p, generator.Groups));
        }

        string[] fixedTexts = ["", "a", "ab", "abcabc", "AB ab", "1a 2b"];
        var texts = fixedTexts.Concat(Enumerable.Range(0, 6).Select(_ => new string(Enumerable.Range(0, random.Next(9)).Select(_ => "abcAB1 "[random.Next(7)]).ToArray()))).ToList();
        var expected = new List<string>();
        foreach (var (p, groups) in patterns)
        {
            var dotnet = new Regex(p, RegexOptions.CultureInvariant);
            var lines = texts.Select(t => Walked(dotnet, groups, t)).ToList();
            expected.AddRange(lines); // the linear engine
            expected.AddRange(lines); // the backtracking engine
        }

        Assert.True(expected.Count(e => e.EndsWith("@-", StringComparison.Ordinal)) > 100, "texts no pattern matches");
        Assert.True(expected.Count(e => e.Contains(";-", StringComparison.Ordinal)) > 20, "groups that take no part");
        Assert.True(expected.Count(e => e.Contains(",0", StringComparison.Ordinal)) > 20, "empty matches");
        HoldToNet(patterns, texts, expected, "[false, true]", "regex-matches", 2);
    }

    /// <summary>
    /// G2: the backtracking engine's own constructs, a backreference by number and by name, the four look-arounds, a look-behind of
    /// any length among them, and the atomic group, match as .NET's do, held to .NET over written rows and 300 generated patterns of
    /// them, 12 texts each: a backreference of a group that took no part fails, one inside its own group reads the group's last text
    /// (<c>(a|b\1)+</c> matches <c>aba</c>), a positive look-around keeps its groups, and nothing backtracks into an atomic group.
    /// The generator puts no group inside a look-behind, whose groups .NET takes from right to left, and no repetition of what can
    /// match nothing. Under <c>(?i)</c> a backreference compares by the simple case folding, so <c>k</c> and the Kelvin sign are
    /// one, a row with its own expectation, since .NET counts positions in UTF-16 units.
    /// </summary>
    [Fact]
    public void The_backtracking_engine_reads_backreferences_look_arounds_and_atomic_groups_as_net_does()
    {
        string[] written =
        [
            @"(\w+) \1", @"\d+(?= kg)", @"(a|b\1)+", @"(?<=a+)b", @"(?<!a)b", @"(?>a+)b", @"(?>a|ab)c", @"(?=(a+))a*b\1",
            @"(?i)(a)\1", @"\2(a)(b)", @"(a)|\1b", @"(?!a)\w", @"(?<n>a)\k<n>", @"(?<=\b)a(?!a)", @"(?<=ab|b)a", @"a(?=b(?=c))",
        ];
        string[] atoms = ["a", "b", "[ab]", "(a|b)", "(a+)", "(b*)", @"\1", @"\2", "(?=a)", "(?!b)", "(?<=a)", "(?<!b)", "(?<=a+b?)",
            "(?>a+)", "(?>a|ab)", "a*", "b+?", "(?:ab|a)+", "(a|b)+", "(?=(ab))", "(?!(a))"];
        var random = new Random(Seed + 7);
        var patterns = written.Select(p => (p, Groups(p))).ToList();
        while (patterns.Count < written.Length + 300)
        {
            var p = string.Concat(Enumerable.Range(0, random.Next(1, 5)).Select(_ => atoms[random.Next(atoms.Length)]));
            var groups = Groups(p);
            if ((p.Contains(@"\1", StringComparison.Ordinal) && groups < 1) || (p.Contains(@"\2", StringComparison.Ordinal) && groups < 2))
            {
                continue; // a backreference to a group the pattern does not have is `Syntax` here and refused by .NET
            }

            patterns.Add((p, groups));
        }

        Assert.All(patterns, p => Assert.Null(RegexPattern.Check(p.Item1, backtracking: true)));
        Assert.All(patterns.Take(written.Length), p => Assert.NotNull(RegexPattern.Check(p.Item1))); // the linear engine takes none of the written ones
        string[] fixedTexts = ["", "a", "ab", "aba", "aab b", "say hello hello there", "5 m, 12 kg", "aA", "abab", "abc"];
        var texts = fixedTexts.Concat(Enumerable.Range(0, 4).Select(_ => new string(Enumerable.Range(0, random.Next(8)).Select(_ => "aab "[random.Next(4)]).ToArray()))).ToList();
        var expected = new List<string>();
        foreach (var (p, groups) in patterns)
        {
            var dotnet = new Regex(p, RegexOptions.CultureInvariant);
            expected.AddRange(texts.Select(t => Walked(dotnet, groups, t)));
        }

        Assert.True(expected.Count(e => !e.EndsWith("@-", StringComparison.Ordinal)) > 500, "texts a pattern matches");
        Assert.True(expected.Count(e => e.Contains(";-", StringComparison.Ordinal)) > 50, "groups that take no part");
        HoldToNet(patterns, texts, expected, "[true]", "regex-backtracking", 1);

        var folded = Builder.Compile(SourceText.FromString("program.kz", """
            re = Regex.New(r"(?i)(k)\1", backtracking: true) // a literal pattern: a `Regex`, checked by the compiler
            for t in ["akKELVIN", "KKELVINk", "kx"] {
                found = re.Match(t)
                if found != null {
                    print("{found.Index},{found.Length}")
                } else {
                    print("-")
                }
            }

            """.Replace("KELVIN", "\u212A", StringComparison.Ordinal)), [], toolchain.Runtime, testBuild: true); // U+212A
        Assert.True(folded.Program is not null, string.Join("; ", folded.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(folded.Program, "regex-backtracking-fold"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(["1,4", "0,4", "-"], output); // the Kelvin sign is three bytes, `K` and `k` one each
    }

    /// <summary>The groups of a pattern the tests generate: its <c>(</c>s that open no <c>(?</c>.</summary>
    private static int Groups(string pattern) => Enumerable.Range(0, pattern.Length)
        .Count(i => pattern[i] == '(' && (i == 0 || pattern[i - 1] != '\\') && (i + 1 >= pattern.Length || pattern[i + 1] != '?'))
        + pattern.Split("(?<n>").Length - 1;

    /// <summary>
    /// A program that runs every pattern on each engine <paramref name="engines"/> names, a Kurz list of the flag's values, and
    /// prints for each text the line <see cref="Walked"/> writes of .NET's matches, held to <paramref name="expected"/> under both
    /// runtimes, the fenced one among them.
    /// </summary>
    private void HoldToNet(List<(string Pattern, int Groups)> patterns, List<string> texts, List<string> expected, string engines, string name, int perPattern)
    {
        var program = new StringBuilder("patterns = [\n");
        foreach (var (p, _) in patterns)
        {
            program.Append("    \"").Append(Escaped(p)).Append("\",\n");
        }

        program.Append("]\ngroups = [").Append(string.Join(", ", patterns.Select(p => p.Groups))).Append("]\ntexts = [\n");
        foreach (var t in texts)
        {
            program.Append("    \"").Append(Escaped(t)).Append("\",\n");
        }

        program.Append("]\nengines = ").Append(engines).Append("""

            for k in 0..<patterns.Count {
                for engine in engines {
                match Regex.New(patterns[k], backtracking: engine) {
                    Regex re => {
                        for t in texts {
                            mut line = ""
                            for m in re.Matches(t) {
                                line = "{line}[{m.Index},{m.Length}"
                                for g in 0..<groups[k] {
                                    found = m.Group(g + 1)
                                    if found != null {
                                        line = "{line};{found.Index},{found.Length}"
                                    } else {
                                        line = "{line};-"
                                    }
                                }

                                line = "{line}]"
                            }

                            first = re.Match(t)
                            if first != null {
                                print("{line}{re.IsMatch(t)}@{first.Index}")
                            } else {
                                print("{line}{re.IsMatch(t)}@-")
                            }
                        }
                    }
                    RegexError e => print(e)
                }
                }
            }

            """);
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Count, output.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                var (p, _) = patterns[i / (texts.Count * perPattern)];
                Assert.True(expected[i] == output[i], $"`{p}` on `{texts[i % texts.Count]}`: .NET finds `{expected[i]}`, the program `{output[i]}`");
            }
        }
    }

    /// <summary>
    /// G5: <c>Replace</c> reads its replacement as Rust's <c>replace_all</c> does, each row with the text Rust's reading gives: a number
    /// and a name after <c>$</c>, the longest run of letters, digits and <c>_</c>, a number written with a <c>0</c> or a <c>+</c> before
    /// it, as Rust's <c>usize</c> parse reads one, <c>${name}</c> ended at its brace, <c>$$</c>, and a <c>$</c> that names no group (at
    /// the end, before a byte of no name, before a <c>{</c> without its <c>}</c>), which is a <c>$</c>; a group that does not exist, of
    /// a number too large for any, of the empty name or of a name no group has, and one that did not take part, give no text. A
    /// replacement that empties the text, a text with no match and a text of two-byte characters are rows too. <c>Split</c> adds the
    /// groups that took part, in the order of their numbers, and splits at an empty match as <c>Matches</c> walks one (G4). The program
    /// runs a second time under the allocator that makes every block it frees inaccessible.
    /// </summary>
    [Fact]
    public void Replace_reads_the_replacement_as_rust_does_and_split_adds_the_groups_that_took_part()
    {
        const string Dates = @"(?<y>\d+)-(\d+)";
        (string Pattern, string Text, string Replacement, string Expected)[] replaced =
        [
            (Dates, "a 12-34 b", "$1", "a 12 b"),
            (Dates, "a 12-34 b", "$y", "a 12 b"),
            (Dates, "a 12-34 b", "${y}x", "a 12x b"),
            (Dates, "a 12-34 b", "$yx", "a  b"), // the longest run names `yx`, which no group has
            (Dates, "a 12-34 b", "$2$1", "a 3412 b"),
            (Dates, "a 12-34 b", "$0", "a 12-34 b"),
            (Dates, "a 12-34 b", "$01", "a 12 b"),
            (Dates, "a 12-34 b", "${01}", "a 12 b"),
            (Dates, "a 12-34 b", "${+1}", "a 12 b"),
            (Dates, "a 12-34 b", "${+}", "a  b"),
            (Dates, "a 12-34 b", "$3", "a  b"),
            (Dates, "a 12-34 b", "$99999999999999999999999", "a  b"),
            (Dates, "a 12-34 b", "${}", "a  b"),
            (Dates, "a 12-34 b", "${é}", "a  b"),
            (Dates, "a 12-34 b", "$$1", "a $1 b"),
            (Dates, "a 12-34 b", "$", "a $ b"),
            (Dates, "a 12-34 b", "x$", "a x$ b"),
            (Dates, "a 12-34 b", "$-", "a $- b"),
            (Dates, "a 12-34 b", "$+1", "a $+1 b"), // a `+` is no byte of a name after a bare `$`
            (Dates, "a 12-34 b", "$é", "a $é b"),
            (Dates, "a 12-34 b", "${y", "a ${y b"),
            (Dates, "a 12-34 b", "$y${", "a 12${ b"),
            ("(a)|(b)", "ab", "[$1|$2]", "[a|][|b]"),
            ("a", "banana", "", "bnn"),
            ("a+", "aaa", "", ""),
            ("z", "abc", "$1", "abc"),
            ("é", "aéb", "ü", "aüb"),
            ("x*", "axb", "-", "-a-b-"),
        ];
        (string Pattern, string Text, string Expected)[] split =
        [
            ("(a)|(b)", "xaybz", "[x, a, y, b, z]"),
            ("(?<s>[a-z])(=)", "1a=2b=3", "[1, a, =, 2, b, =, 3]"),
            ("", "ab", "[, a, b, ]"),
            ("é", "aéb", "[a, b]"),
            ("x", "abc", "[abc]"),
        ];
        var program = new StringBuilder();
        var expected = new List<string>();
        for (var i = 0; i < replaced.Length; i++)
        {
            var (pattern, text, replacement, result) = replaced[i];
            program.Append($"v{i} = Regex.New(r\"{pattern}\").Replace(r\"{text}\", r\"{replacement}\")\nprint(\"<{{v{i}}}>\")\n");
            expected.Add($"<{result}>");
        }

        for (var i = 0; i < split.Length; i++)
        {
            var (pattern, text, result) = split[i];
            program.Append($"s{i} = Regex.New(r\"{pattern}\").Split(r\"{text}\")\nprint(s{i})\n");
            expected.Add(result);
        }

        program.Append("print(Regex.New(\"x\").Split(\"\").Count)\n");
        expected.Add("1");
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "regex-replace" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    /// <summary>
    /// G4: the constructs .NET does not read as Rust does, each row with the matches Rust's <c>regex</c> 1.13 gives, written by hand as
    /// it writes them, each match's start and end: the start and the end of a word, <c>\b{start}</c> and <c>\&lt;</c>, <c>\b{end}</c>
    /// and <c>\&gt;</c>, and the halves <c>\b{start-half}</c> and <c>\b{end-half}</c>, of Unicode's word characters; a class's
    /// <c>-</c> right after its <c>[</c> and its <c>]</c> first, each a character and no range; each side of <c>&amp;&amp;</c>,
    /// <c>--</c> and <c>~~</c> folded under <c>(?i)</c> before the operation, as Rust's translator folds them; an ASCII class folded
    /// under <c>(?i)</c> before it is negated, so that <c>[:^lower:]</c> takes no letter of either case; a range with white space
    /// around its <c>-</c> under <c>(?x)</c>; a counted repetition with white space inside its braces; and a repetition of what matches
    /// no character, taken at most once.
    /// </summary>
    [Fact]
    public void The_constructs_net_reads_otherwise_match_as_rust_matches_them()
    {
        (string Pattern, string Text, string Matches)[] rows =
        [
            (@"\b{start}\w", "ab cd", "0-1 3-4"),
            (@"\w\b{end}", "ab cd", "1-2 4-5"),
            (@"\<\w+", "ab cd", "0-2 3-5"),
            (@"\w+\>", "ab cd", "0-2 3-5"),
            (@"\b{start-half}x", "x ax xx", "0-1 5-6"),
            (@"x\b{end-half}", "x xa xx", "0-1 6-7"),
            (@"\b{start-half}", "a b", "0-0 2-2"),
            (@"\b{end-half}", "a b", "1-1 3-3"),
            ("é\\b{end}", "é é1", "0-2"),
            (@"(?x)\b{ end }a", "a", ""),
            ("[--a]+", "-a--b", "0-4"),
            ("[]-a]+", "]-a^b", "0-3"),
            ("[-a-c]+", "-b-d", "0-3"),
            ("(?i)[a&&A]", "aA", "0-1 1-2"),
            ("(?i)[k--K]", "kK\u212a", ""),
            ("(?i)[a~~A]+", "aAb", ""),
            ("(?i)[^a&&A]", "aAb", "2-3"),
            ("(?i)[[a]&&[A]]", "aA", "0-1 1-2"),
            ("(?i)[[:^lower:]]", "A]&K&", "1-2 2-3 4-5"),
            ("(?i)[^[:^lower:]]+", "aB1\u212a-", "0-2 3-6"),
            ("(?x)[a - c]+", "a-b c d", "0-1 2-3 4-5"),
            ("a{ 2 }", "aaa", "0-2"),
            ("(?:){5}", "ab", "0-0 1-1 2-2"),
            (@"\b{3}a", "a ba", "0-1"),
        ];
        var program = new StringBuilder();
        for (var i = 0; i < rows.Length; i++)
        {
            var (pattern, text, _) = rows[i];
            program.Append($"mut line{i} = \"\"\nfor m in Regex.New(\"{Escaped(pattern)}\").Matches(\"{Escaped(text)}\") {{\n")
                .Append($"    if line{i} != \"\" {{\n        line{i} = \"{{line{i}}} \"\n    }}\n\n")
                .Append($"    line{i} = \"{{line{i}}}{{m.Index}}-{{m.Index + m.Length}}\"\n}}\n\nprint(\"<{{line{i}}}>\")\n");
        }

        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "regex-rust" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(rows.Select(r => $"<{r.Matches}>"), output);
        }
    }

    /// <summary>The line the program prints for a pattern and a text: .NET's matches walked as Rust's <c>find_iter</c>, then <c>IsMatch</c> and the first match's position.</summary>
    private static string Walked(Regex regex, int groups, string text)
    {
        var line = new StringBuilder();
        var at = 0;
        var last = -1;
        while (at <= text.Length)
        {
            var m = regex.Match(text, at);
            if (!m.Success)
            {
                break;
            }

            if (m.Length == 0 && m.Index == last)
            {
                at++; // Rust passes over an empty match that ends where the last match ended, one character on
                continue;
            }

            line.Append('[').Append(m.Index).Append(',').Append(m.Length);
            for (var g = 1; g <= groups; g++)
            {
                var group = m.Groups[g];
                line.Append(group.Success ? $";{group.Index},{group.Length}" : ";-");
            }

            line.Append(']');
            last = m.Index + m.Length;
            at = last;
        }

        var first = regex.Match(text);
        return $"{line}{(first.Success ? "true" : "false")}@{(first.Success ? first.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}";
    }

    /// <summary>The checker's reading of a pattern in the words of <c>RegexError</c>, or <c>ok</c>.</summary>
    private static string Read(string pattern) => RegexPattern.Check(pattern) is { } f ? RegexPattern.Words(f.Kind, f.At) : "ok";

    /// <summary>A pattern or a text written into a Kurz string literal: its backslash, its quote, its opening braces and its line ends escaped (L20).</summary>
    private static string Escaped(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("{", "\\{", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>
    /// G1 to G4: a use of a name of regular expressions that the build refuses, where it stands: a pattern written as a literal that
    /// does not build (G3), at the literal, whatever line the call starts on, but for a construct of Rust's syntax that the build does not
    /// hold (G4), which is <c>not supported</c> where it is the pattern's first error; a literal checked for the engine its flag
    /// names (G2), and not checked where the flag is no literal; a function of <c>Regex</c> other than <c>New</c>; a pattern that is no
    /// literal, whose call gives <c>Regex | RegexError</c>; and a file's own type, function, variable or parameter of one of the four names (V9),
    /// while a field takes the name.
    /// </summary>
    [Theory]
    [InlineData("print(Regex.New(\"a)\").IsMatch(\"a\"))\n", "regex-invalid", 1)] // G3: a syntax error
    [InlineData("print(1)\nprint(Regex.New(\n    \"(a\").IsMatch(\"a\"))\n", "regex-invalid", 3)] // at the literal's line
    [InlineData("print(Regex.New(\"(?=a)\").IsMatch(\"a\"))\n", "regex-invalid", 1)] // a construct the linear engine does not take (G2)
    [InlineData("print(Regex.New(r\"(a)\\1\").IsMatch(\"a\"))\n", "regex-invalid", 1)]
    [InlineData("print(Regex.New(r\"a{655358}\").IsMatch(\"a\"))\n", "regex-invalid", 1)] // past the bound of the automaton (G2)
    [InlineData("print(Regex.New(r\"(?:a{1000}){1000}\").IsMatch(\"a\"))\n", "regex-invalid", 1)]
    [InlineData("print(Regex.New(r\"\\p{L}\").IsMatch(\"a\"))\n", "", 1)] // G4: Rust's syntax that the build does not hold
    [InlineData("print(Regex.New(r\"a\\P{L}\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(\"(?u)a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(\"(?R)a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(r\"a)\\p{L}\").IsMatch(\"a\"))\n", "regex-invalid", 1)] // the first error of the pattern decides
    [InlineData("print(Regex.New(r\"(a)\\1\", backtracking: true).IsMatch(\"aa\"))\n", "", 0)] // G2: the backtracking engine takes it
    [InlineData("print(Regex.New(r\"(a)\\1\", true).IsMatch(\"aa\"))\n", "", 0)] // the flag by its place, the second argument
    [InlineData("print(Regex.New(r\"(?<=a)b(?!c)(?>d)\", true).IsMatch(\"abd\"))\n", "", 0)]
    [InlineData("print(Regex.New(r\"(a)\\1\", false).IsMatch(\"aa\"))\n", "regex-invalid", 1)] // the linear engine does not
    [InlineData("print(Regex.New(r\"\\2(a)\", true).IsMatch(\"aa\"))\n", "regex-invalid", 1)] // no group 2
    [InlineData("print(Regex.New(r\"\\k<b>(?<a>x)\", true).IsMatch(\"aa\"))\n", "regex-invalid", 1)] // no group `b`
    [InlineData("b = true\nRegex re = Regex.New(r\"(a)\\1\", backtracking: b)\nprint(re)\n", "unlisted-case", 2)] // a flag that is no literal: checked when it runs (G3)
    [InlineData("b = false\nmatch Regex.New(r\"(a)\\1\", backtracking: b) {\n    Regex re => print(re)\n    RegexError e => print(e)\n}\n", "", 0)]
    [InlineData("print(Regex.New(\"a\", false).IsMatch(\"a\"))\n", "", 0)]
    [InlineData("print(Regex.New(\"a\", backtracking: false).IsMatch(\"a\"))\n", "", 0)]
    [InlineData("print(Regex.Make(\"a\"))\n", "unknown-name", 1)] // V10: `New` is the one function
    [InlineData("print(Regex.New<int>(\"a\"))\n", "syntax", 1)] // T17
    [InlineData("p = \"a\"\nRegex re = Regex.New(p)\nprint(re)\n", "unlisted-case", 2)] // G3: `Regex | RegexError`, whose error top-level code passes to no caller (O9)
    [InlineData("Regex re = Regex.New(\"a\")\nprint(re)\n", "", 0)] // a literal the checker read gives a `Regex`
    [InlineData("p = r\"a{655358}\"\nmatch Regex.New(\"{p}\") {\n    Regex re => print(re)\n    RegexError e => print(e)\n}\n", "", 0)] // an interpolation is read when the program runs
    [InlineData("data Regex(int A)\n", "redeclared", 1)] // V9: a name every file sees (G1)
    [InlineData("data Match(int A)\n", "redeclared", 1)]
    [InlineData("data RegexError(int A)\n", "redeclared", 1)]
    [InlineData("enum RegexKind { A }\n", "redeclared", 1)]
    [InlineData("int Regex() => 1\nprint(1)\n", "redeclared", 1)] // a function, a variable and a parameter of the name
    [InlineData("string RegexError = \"a\"\nprint(RegexError)\n", "redeclared", 1)]
    [InlineData("void Show(int Match) {\n    print(Match)\n}\n\nShow(1)\n", "redeclared", 1)]
    [InlineData("data Found(int Match)\n\nprint(Found(1).Match)\n", "", 0)] // while a field takes it
    public void A_use_of_a_regular_expression_the_build_refuses_is_refused_where_it_stands(string text, string id, int line)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        if (line == 0)
        {
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted)); // the positive control
            return;
        }

        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((id, line, id.Length == 0), (diagnostic.Id, diagnostic.Line, diagnostic.IsNotSupported)); // section 8: the id and the line of an error, never its message
    }

    /// <summary>
    /// Patterns of the letters <c>a</c> to <c>c</c>, <c>A</c> and <c>1</c>, <c>.</c>, classes, anchors, groups numbered and not,
    /// alternatives and repetitions greedy and lazy, a fifth of them under <c>(?i)</c>; a repetition is put only after what cannot
    /// match nothing.
    /// </summary>
    private sealed class Generator(int seed)
    {
        private static readonly string[] Classes = ["[ab]", "[^a]", "[a-c]", "[A-Z]", "[^ ]", @"\d", @"\w", @"\s", @"\D", @"\W", "[a1]"];
        private static readonly string[] Anchors = ["^", "$", @"\b", @"\B", @"\A", @"\z"];
        private static readonly string[] Repetitions = ["*", "+", "?", "{2}", "{1,2}", "{0,2}", "{2,}", "*?", "+?", "??", "{1,2}?"];
        private readonly Random random = new(seed);

        /// <summary>The numbered groups of the last pattern.</summary>
        public int Groups { get; private set; }

        public string Pattern()
        {
            Groups = 0;
            var p = Alternatives(0, out _);
            return random.Next(5) == 0 ? "(?i)" + p : p;
        }

        private string Alternatives(int depth, out bool empty)
        {
            var count = random.Next(4) == 0 ? 2 : 1;
            var parts = new List<string>();
            empty = false;
            for (var i = 0; i < count; i++)
            {
                parts.Add(Sequence(depth, out var part));
                empty |= part;
            }

            return string.Join("|", parts);
        }

        private string Sequence(int depth, out bool empty)
        {
            var count = 1 + random.Next(3);
            var text = new StringBuilder();
            empty = true;
            for (var i = 0; i < count; i++)
            {
                text.Append(Piece(depth, out var piece));
                empty &= piece;
            }

            return text.ToString();
        }

        private string Piece(int depth, out bool empty)
        {
            var atom = Atom(depth, out empty, out var repeatable);
            if (!repeatable || empty || random.Next(3) != 0)
            {
                return atom;
            }

            var repetition = Repetitions[random.Next(Repetitions.Length)];
            empty = repetition[0] is '*' or '?' || repetition.StartsWith("{0", StringComparison.Ordinal);
            return atom + repetition;
        }

        private string Atom(int depth, out bool empty, out bool repeatable)
        {
            empty = false;
            repeatable = true;
            var pick = random.Next(depth < 2 ? 12 : 9);
            switch (pick)
            {
                case <= 3:
                    return "abcA1"[random.Next(5)].ToString();
                case 4:
                    return ".";
                case 5 or 6:
                    return Classes[random.Next(Classes.Length)];
                case 7:
                    empty = true;
                    repeatable = false;
                    return Anchors[random.Next(Anchors.Length)];
                case 8:
                    return "ab";
                case 9 or 10:
                    Groups++;
                    return "(" + Alternatives(depth + 1, out empty) + ")";
                default:
                    return "(?:" + Alternatives(depth + 1, out empty) + ")";
            }
        }
    }
}
