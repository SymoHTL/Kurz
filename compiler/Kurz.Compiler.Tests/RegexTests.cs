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
/// error and its byte, or a pattern that builds, on both sides of the bound of the automaton (G2). The linear engine's matches are held
/// to .NET's backtracking engine, whose leftmost-first order is Rust's, over generated patterns of ASCII letters, classes, anchors,
/// groups, alternatives and repetitions greedy and lazy, on generated texts without a line end, where the two read every construct
/// alike; .NET's matches are walked as Rust's <c>find_iter</c> walks them, an empty match that ends where the last one ended passed
/// over. Both programs run a second time under the allocator that makes every block it frees inaccessible.
/// </summary>
public sealed class RegexTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Seed = 1101;

    /// <summary>Patterns written by hand: each kind of error at its byte, constructs the linear engine does not take, and the bound.</summary>
    private static readonly string[] Written =
    [
        "", "a", "a)", "(a", "((a)", "[a", "[]", "[]a]", "[^]a]", "a{", "a{1", "a{1,", "a{2,1}", "a{,2}", "{1}", "*a", "a**", "a|*",
        "(?", "(?:", "(?i", "(?i)", "(?i-i)", "(?ii)", "(?-)", "(?z)", "(?R)", "(?u)", "(?U)a*", "(?x) a b # c", "(?s).", "(?m)^a$",
        @"\", @"\q", @"\d\D\w\W\s\S", @"\x41", @"\x{1F600}", @"\x{110000}", @"\x4", @"\u0041", @"\p{L}", @"\P{L}", @"\pL", @"\k<a>",
        @"\1", @"(a)\1", @"\<", @"\>", @"\b{start}", @"\A\z", @"\Z", @"\G", "(?=a)", "(?!a)", "(?<=a)", "(?<!a)", "(?>a)", "(?<n>a)",
        "(?P<n>a)", "(?<n>a)(?<n>b)", "(?<1n>a)", "(?<>a)", "(?P=n)", "[[:alpha:]]", "[[:word:]]", "[[:foo:]]", "[a-]", "[-a]", "[z-a]",
        "[a&&b]", "[a--b]", "[a~~b]", "[\\d-z]", "é(", "é{", "[é-a]", "a{1001}", "(?:a{1000}){1000}", @"(?:\w{1000}){1000}",
        "a{655357}", "a{655358}", "(a){327677}", "x{4294967296}",
        string.Concat(Enumerable.Repeat("(", 250)) + "a" + string.Concat(Enumerable.Repeat(")", 250)),
        string.Concat(Enumerable.Repeat("(", 251)) + "a" + string.Concat(Enumerable.Repeat(")", 251)),
    ];

    /// <summary>The characters of the syntax, one of which is put into a generated pattern.</summary>
    private const string Syntax = "()[]{}*+?\\|^$.-,:<>";

    /// <summary>The largest count of one character under the bound of the automaton (G2), and the next.</summary>
    private static readonly string[] Bound = ["a{655357}", "a{655358}"];

    /// <summary>
    /// G3: the checker's copy of the parse and the count gives every pattern what the standard library's <c>Regex.New</c> gives it
    /// when the program runs: the error's kind and byte, or a pattern that builds. The patterns are the written ones, generated ones,
    /// which all build, and each generated one with a character of the syntax put in at a place, most of which do not.
    /// </summary>
    [Fact]
    public void The_checkers_reading_of_a_pattern_is_what_regex_new_gives_when_the_program_runs()
    {
        var generator = new Generator(Seed);
        var random = new Random(Seed + 1);
        var patterns = new List<string>(Written);
        for (var i = 0; i < 150; i++)
        {
            var p = generator.Pattern();
            patterns.Add(p);
            var at = random.Next(p.Length + 1);
            patterns.Add(p[..at] + Syntax[random.Next(Syntax.Length)] + p[at..]);
        }

        patterns.RemoveAll(p => p.Contains('"', StringComparison.Ordinal));
        var expected = patterns.Select(p => RegexPattern.Check(p) is { } failed ? RegexPattern.Words(failed.Kind, failed.At) : "ok").ToList();
        Assert.Contains(expected, e => e.StartsWith("syntax at ", StringComparison.Ordinal));
        Assert.Contains(expected, e => e.StartsWith("unsupported at ", StringComparison.Ordinal));
        Assert.Contains(expected, e => e.StartsWith("too big at ", StringComparison.Ordinal));
        Assert.True(expected.Count(e => e == "ok") > 150, "the generated patterns build");

        // the bound of the automaton (G2): the largest count of one character under it builds, the next is too big
        Assert.Equal(["ok", "too big at 0"], Bound.Select(p => RegexPattern.Check(p) is { } f ? RegexPattern.Words(f.Kind, f.At) : "ok"));

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
    /// G2, G4: the matches of the linear engine, <c>Matches</c>, <c>Match</c> and <c>IsMatch</c>, each match's position, its length and
    /// every group's, are .NET's for the same pattern and text, walked as Rust's <c>find_iter</c> walks them, over 300 generated
    /// patterns and 12 texts each. The generator writes no named group, which .NET numbers after the others, no repetition of what
    /// can match nothing, where backtracking engines stop a loop early, and no line end in a text, where .NET's <c>$</c> matches before
    /// one.
    /// </summary>
    [Fact]
    public void The_linear_engine_finds_the_matches_and_groups_a_leftmost_first_engine_finds()
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
            expected.AddRange(texts.Select(t => Walked(dotnet, groups, t)));
        }

        Assert.True(expected.Count(e => e.EndsWith("@-", StringComparison.Ordinal)) > 100, "texts no pattern matches");
        Assert.True(expected.Count(e => e.Contains(";-", StringComparison.Ordinal)) > 20, "groups that take no part");
        Assert.True(expected.Count(e => e.Contains(",0", StringComparison.Ordinal)) > 20, "empty matches");

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

        program.Append("""
            ]
            for k in 0..<patterns.Count {
                match Regex.New(patterns[k]) {
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

            """);
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program.ToString()), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "regex-matches" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected.Count, output.Count);
            for (var i = 0; i < expected.Count; i++)
            {
                var (p, _) = patterns[i / texts.Count];
                Assert.True(expected[i] == output[i], $"`{p}` on `{texts[i % texts.Count]}`: .NET finds `{expected[i]}`, the program `{output[i]}`");
            }
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

    /// <summary>A pattern or a text written into a Kurz string literal: its backslash, its quote and its opening braces escaped (L20).</summary>
    private static string Escaped(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("{", "\\{", StringComparison.Ordinal);

    /// <summary>
    /// G1 to G5: a use of a name of regular expressions that the build refuses, where it stands: a pattern written as a literal that
    /// does not build (G3), at the literal, whatever line the call starts on, but for a construct of Rust's syntax that the build does not
    /// hold (G4), which is <c>not supported</c> where it is the pattern's first error, as the backtracking engine (G2), and <c>Replace</c>
    /// and <c>Split</c> (G5), are; a function of <c>Regex</c> other than <c>New</c>; a pattern that is no literal, whose
    /// call gives <c>Regex | RegexError</c>; and a file's own type, function, variable or parameter of one of the four names (V9),
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
    [InlineData("print(Regex.New(r\"\\<a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(r\"\\b{start}a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(\"(?u)a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(\"(?R)a\").IsMatch(\"a\"))\n", "", 1)]
    [InlineData("print(Regex.New(r\"a)\\p{L}\").IsMatch(\"a\"))\n", "regex-invalid", 1)] // the first error of the pattern decides
    [InlineData("print(Regex.New(\"a\", backtracking: true).IsMatch(\"a\"))\n", "", 1)] // G2: the backtracking engine
    [InlineData("b = false\nprint(Regex.New(\"a\", backtracking: b).IsMatch(\"a\"))\n", "", 2)] // not the literal `false`
    [InlineData("print(Regex.New(\"a\").Replace(\"a\", \"b\"))\n", "", 1)] // G5
    [InlineData("print(Regex.New(\"a\").Split(\"a\"))\n", "", 1)]
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
