using System.Text.RegularExpressions;
using Kurz.Compiler.Corpus;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Every file of the corpus through the lexer, the parser and the checker. The front end's theory: a
/// case that expects an error the front end reports (<c>semicolon</c>, <c>reserved-word</c>,
/// <c>braces-required</c>, <c>unknown-escape</c>, <c>block-indentation</c>, and <c>syntax</c> where the
/// front end reports an error on the case) gets exactly that error at that line; every other case gets
/// none from the front end. The checker's
/// theory, on every case the front end read clean: a case the checker reads in full gets exactly the
/// error its header expects, or none; a case on which the checker says <c>not supported</c> gets no
/// error from it, and cites a rule the checker does not read, or stands in the list of cases that
/// use such a construct under rules it does read. The excuse is taken only where the checker said
/// <c>not supported</c>: a case read in full is held to its header, whatever rules it cites. No case
/// is skipped, and floors hold the counts of cases read in full and of cases that expect an error the
/// checker reports.
/// </summary>
public sealed class CorpusTests
{
    /// <summary>The corpus has more cases than this; a scan that finds fewer looked in the wrong place.</summary>
    private const int Floor = 250;

    /// <summary>The corpus has more cases that expect an error of the front end than this; a count below it read the headers wrong.</summary>
    private const int FrontEndErrorFloor = 15;

    /// <summary>The checker reads more cases in full than this, and more cases that expect an error it reports than the second floor; fewer means it refuses what it read before.</summary>
    private const int ReadInFullFloor = 260;

    private const int CheckerErrorFloor = 97;

    private static readonly HashSet<string> FrontEndIds =
    [
        ErrorIds.Syntax, ErrorIds.Semicolon, ErrorIds.ReservedWord, ErrorIds.BracesRequired, ErrorIds.UnknownEscape, ErrorIds.BlockIndentation,
    ];

    /// <summary>
    /// The rules the checker does not read: the null chapter's paths through class instances (N9), the class chapter and the
    /// memory chapter's counted references and <c>weak</c> (R1 to R6, R8, R9), <c>flags</c>
    /// (D13, D17, D19), the body of a data type (D16),
    /// folders (F9, F10), the limit of a type parameter and a member of one (T19), <c>mut</c> methods
    /// (M6), collections between actors (M8), the texts of class instances and sets of flags (A5, A7, A9,
    /// A10, A13, A15), and the grants of files and sockets (S12). A case that cites one may get <c>not supported</c>;
    /// one the checker reads in full is held to its header.
    /// </summary>
    private static bool OutsideTheChecker(string rule) =>
        rule[0] is 'K' || rule is "N9"
        || rule is "R1" or "R2" or "R3" or "R4" or "R5" or "R6" or "R8" or "R9"
        || rule is "D13" or "D16" or "D17" or "D19" or "F9" or "F10" or "T19" or "M6" or "M8"
        || rule is "A5" or "A7" or "A9" or "A10" or "A13" or "A15"
        || rule is "S12";

    /// <summary>Cases that cite only rules the checker reads and still use a construct it does not, each with that construct; an entry whose case cites a rule outside the checker is redundant and refused.</summary>
    private static readonly Dictionary<string, string> UnsupportedByConstruct = new(StringComparer.Ordinal)
    {
        ["classes/print-without-text.kz"] = "a class (chapter 7)",
        ["types/generic-cannot-infer-classes.kz"] = "classes (chapter 7)",
        ["values/mut-method-through-field.kz"] = "a class (chapter 7)",
        ["values/text-more.kz"] = "a flags type (D13)",
    };

    private static bool IsNotSupported(Diagnostic d) => d.Kind == DiagnosticKind.NotSupported;

    internal static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "corpus")) && File.Exists(Path.Combine(directory.FullName, "kurz-design.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("the repository root, which holds corpus/ and kurz-design.md, is not above the test directory");
    }

    /// <summary>Every case file, as a path relative to corpus/ with forward slashes, in a fixed order.</summary>
    internal static List<string> CaseFiles()
    {
        var corpus = Path.Combine(Root, "corpus");
        return Directory.EnumerateFiles(corpus, "*.kz", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(corpus, file).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var file in CaseFiles())
        {
            data.Add(file);
        }

        return data;
    }

    internal static (CaseHeader Header, ProgramSyntax Tree, DiagnosticBag FrontEnd) ReadCase(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, "corpus", relativePath));
        var source = SourceText.FromBytes(relativePath, bytes, out var invalid);
        Assert.True(source is not null, $"{relativePath}: {invalid}");
        var header = CaseHeader.Parse(source!.Text, out var problem);
        Assert.True(header is not null, $"{relativePath}: {problem}");
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        return (header!, tree, diagnostics);
    }

    /// <summary>
    /// Whether the front end is the side that raises the error a case expects. Five of its ids are the front end's
    /// alone; <c>syntax</c> is raised by both sides, by the front end for text it cannot read and by the checker
    /// for the shapes E4 leaves to it, which the parser reads as sound (the readings in <c>compiler/README.md</c>:
    /// a value on its own as a statement, a <c>void</c> call where a value stands, D13's <c>match</c> on a set of
    /// flags and the rest of that list). So a case that expects <c>syntax</c> is the front end's when the front end
    /// reports an error on it, and the checker's when the front end reads it clean: the checker's theory then holds
    /// it to its id and line, or, where the checker does not read the construct, to <c>not supported</c> and no
    /// error, as for <c>data/flags-match.kz</c>, whose flags type it does not read and whose expectation no test
    /// holds; no floor counts such a case.
    /// </summary>
    private static bool ExpectsFrontEndError(CaseHeader header, DiagnosticBag frontEnd) =>
        header.Expectation == Expectation.Error && FrontEndIds.Contains(header.ErrorId!) && (header.ErrorId != ErrorIds.Syntax || frontEnd.HasErrors);

    [Fact]
    public void The_corpus_holds_at_least_the_floor_of_cases()
    {
        Assert.True(CaseFiles().Count >= Floor, $"found {CaseFiles().Count} cases under {Root}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_front_end_reads_a_case_as_its_header_says(string relativePath)
    {
        var (header, tree, diagnostics) = ReadCase(relativePath);
        var found = diagnostics.Items.Select(d => (d.Id, d.Line)).ToList();
        Assert.DoesNotContain(diagnostics.Items, d => !d.IsError);
        if (ExpectsFrontEndError(header, diagnostics))
        {
            Assert.Equal([(header.ErrorId!, header.ErrorLine)], found);
        }
        else
        {
            Assert.Empty(found);
            Assert.NotEmpty(tree.Statements); // a tree dropped whole would pass the line above
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_checker_reads_a_case_as_its_header_says(string relativePath)
    {
        var (header, tree, frontEnd) = ReadCase(relativePath);
        if (frontEnd.HasErrors)
        {
            Assert.True(ExpectsFrontEndError(header, frontEnd), "the theory above holds this case"); // the checker reads a tree the parser read clean
            return;
        }

        var diagnostics = new DiagnosticBag();
        Checker.Check(tree, diagnostics, header.Grants);
        var errors = diagnostics.Sorted.Where(d => d.IsError).Select(d => (d.Id, d.Line)).ToList();
        var unsupported = diagnostics.Sorted.Where(IsNotSupported).Select(d => d.ToString()).ToList();
        var lints = diagnostics.Items.Where(d => d.Kind == DiagnosticKind.Warning).ToList(); // H5: a warning, which builds the program and which no header expects
        Assert.Equal(errors.Count + unsupported.Count + lints.Count, diagnostics.Items.Count); // the three kinds, and no fourth one that the floors would count as read in full
        var lines = File.ReadAllLines(Path.Combine(Root, "corpus", relativePath));
        Assert.All(lints, l => Assert.True(
            l.Id == "md5-insecure" && Regex.IsMatch(lines[l.Line - 1], @"(?<![\w.])Md5\.(Hash|New)\("),
            $"{relativePath}: `{l}` is no `md5-insecure` on a line that calls a function of `Md5` (H5)"));
        if (unsupported.Count > 0)
        {
            Assert.True(header.Rules.Any(OutsideTheChecker) || UnsupportedByConstruct.ContainsKey(relativePath),
                $"the checker says `{string.Join("; ", unsupported)}` on a case that cites only rules it reads ({string.Join(", ", header.Rules)})");
            Assert.True(errors.Count == 0, $"the checker reports {string.Join(", ", errors)} beside `{string.Join("; ", unsupported)}`");
            return;
        }

        if (header.Expectation == Expectation.Error)
        {
            Assert.Equal([(header.ErrorId!, header.ErrorLine)], errors);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void At_least_the_front_end_error_floor_of_cases_expect_an_error_of_the_front_end()
    {
        // the positive control of the front end's theory: its error branch runs for at least this many cases, read as the theory reads them
        var found = CaseFiles().Count(relativePath =>
        {
            var (header, _, frontEnd) = ReadCase(relativePath);
            return ExpectsFrontEndError(header, frontEnd);
        });
        Assert.True(found >= FrontEndErrorFloor, $"only {found} cases expect an error of the front end");
    }

    [Fact]
    public void At_least_the_floors_of_cases_are_read_in_full_by_the_checker()
    {
        // the positive controls of the checker's theory: its exact branch runs for at least this many cases, and its error branch for at least the second floor
        var inFull = 0;
        var withError = 0;
        var notSupported = new List<string>();
        foreach (var relativePath in CaseFiles())
        {
            var (header, tree, frontEnd) = ReadCase(relativePath);
            if (frontEnd.HasErrors)
            {
                continue;
            }

            var diagnostics = new DiagnosticBag();
            Checker.Check(tree, diagnostics, header.Grants);
            if (diagnostics.Items.Any(IsNotSupported))
            {
                notSupported.Add(relativePath);
                continue;
            }

            inFull++;
            if (header.Expectation == Expectation.Error)
            {
                withError++;
            }
        }

        Assert.True(inFull >= ReadInFullFloor, $"only {inFull} cases are read in full; not supported on {string.Join(", ", notSupported)}");
        Assert.True(withError >= CheckerErrorFloor, $"only {withError} cases that expect an error are read in full");
    }

    [Fact]
    public void Every_case_listed_as_unsupported_by_its_construct_exists_cites_no_outside_rule_and_gets_not_supported()
    {
        // a stale entry would excuse a case the checker reads in full from its expectation; a redundant one, whose case cites a rule
        // outside the checker, excuses nothing and would let the list drift from its meaning
        foreach (var (relativePath, construct) in UnsupportedByConstruct)
        {
            var (header, tree, frontEnd) = ReadCase(relativePath);
            Assert.False(frontEnd.HasErrors, relativePath);
            Assert.False(header.Rules.Any(OutsideTheChecker), $"{relativePath} cites a rule outside the checker ({string.Join(", ", header.Rules)}), so its entry ({construct}) is redundant");
            var diagnostics = new DiagnosticBag();
            Checker.Check(tree, diagnostics, header.Grants);
            Assert.True(diagnostics.Items.Any(IsNotSupported), $"{relativePath} is read in full, so its entry ({construct}) is stale");
        }
    }

    [Theory]
    [InlineData("// expect: output\n// | 9\n// rules: V1\n\nx = 4\nprint(x + 5)\n", Expectation.Output, null, 0, 1, null, 5)]
    [InlineData("// expect: error assign-immutable at 6\n// rules: V7\n\nx = 4\nprint(x)\nx = 5\n", Expectation.Error, "assign-immutable", 6, 0, null, 4)]
    [InlineData("// expect: throws overflow at 8\n// | 1\n// build: test\n// rules: T5\n\nx = 1\nprint(x)\nprint(2147483647 + x)\n", Expectation.Throws, "overflow", 8, 1, BuildKind.Test, 6)]
    public void A_header_in_its_shape_is_read(string text, Expectation expectation, string? id, int line, int outputLines, BuildKind? build, int bodyLine)
    {
        var header = CaseHeader.Parse(text, out var problem);
        Assert.Null(problem);
        Assert.Equal((expectation, id, line, outputLines, build, bodyLine), (header!.Expectation, header.ErrorId, header.ErrorLine, header.OutputLines.Count, header.Build, header.BodyLine));
        Assert.Empty(header.Grants);
    }

    [Theory]
    [InlineData("// expect: output\n// | 1\n// allow: raw\n// rules: R7\n\nraw {\n    print(1)\n}\n", "raw", 6)]
    [InlineData("// expect: error raw-required at 5\n// allow: native\n// rules: R7\n\nx\n", "native", 5)]
    [InlineData("// expect: output\n// | 1\n// build: release\n// allow: raw native\n// rules: R7\n\nx\n", "raw native", 7)]
    public void A_grant_line_gives_the_case_the_grants_it_names(string text, string grants, int bodyLine)
    {
        var header = CaseHeader.Parse(text, out var problem);
        Assert.Null(problem);
        Assert.Equal((grants, bodyLine), (string.Join(' ', header!.Grants), header.BodyLine));
    }

    [Theory]
    [InlineData("// expects output\n// rules: V1\n\nx = 4\n")]
    [InlineData("// expect: output\n\nx = 4\n")]
    [InlineData("// expect: output\n// rules: v1\n\nx = 4\n")]
    [InlineData("// expect: output\n// rules: V1\nx = 4\n")]
    [InlineData("// expect: output\n// rules: V1\n\n")]
    [InlineData("// expect: error unused-variable at 5\n// | a\n// rules: V1\n\nx\n")]
    [InlineData("// expect: throws overflow at 5\n// build: debug\n// rules: V1\n\nx\n")]
    [InlineData("// expect: output\n// allow: raw\n// build: test\n// rules: R7\n\nx\n")]
    public void A_header_out_of_its_shape_is_refused(string text)
    {
        Assert.Null(CaseHeader.Parse(text, out var problem));
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData("// expect: output\n// allow: network\n// rules: R7\n\nx\n")]
    [InlineData("// expect: output\n// allow: native raw\n// rules: R7\n\nx\n")]
    [InlineData("// expect: output\n// allow: raw raw\n// rules: R7\n\nx\n")]
    [InlineData("// expect: output\n// allow:\n// rules: R7\n\nx\n")]
    public void A_grant_line_the_header_does_not_take_is_refused_as_such(string text)
    {
        // R7: the line takes `raw`, `native` or `raw native`, in that order; another is named as the grant line it is, not as a missing rules line
        Assert.Null(CaseHeader.Parse(text, out var problem));
        Assert.Equal("the `// allow:` line is not `// allow: raw`, `// allow: native` or `// allow: raw native`", problem);
    }
}
