using Kurz.Compiler.Corpus;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Every file of the corpus through the lexer and the parser. A case that expects an error the front
/// end reports (<c>syntax</c>, <c>semicolon</c>, <c>reserved-word</c>, <c>braces-required</c>,
/// <c>unknown-escape</c>, <c>block-indentation</c>) gets exactly that error at that line; every
/// other case gets none from the front end. No case is skipped: a case the front end cannot judge
/// fails here.
/// </summary>
public sealed class CorpusFrontEndTests
{
    /// <summary>The corpus has more cases than this; a scan that finds fewer looked in the wrong place.</summary>
    private const int Floor = 250;

    private static readonly HashSet<string> FrontEndIds =
    [
        ErrorIds.Syntax, ErrorIds.Semicolon, ErrorIds.ReservedWord, ErrorIds.BracesRequired, ErrorIds.UnknownEscape, ErrorIds.BlockIndentation,
    ];

    /// <summary>E4 names shapes that only the type checker can tell: a <c>match</c> on a set of flags (D13). The parser reads them as sound.</summary>
    private static readonly HashSet<string> CheckerSyntax = ["data/flags-match.kz"];

    private static readonly string Root = FindRoot();

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
    private static List<string> CaseFiles()
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

    [Fact]
    public void The_corpus_holds_at_least_the_floor_of_cases()
    {
        Assert.True(CaseFiles().Count >= Floor, $"found {CaseFiles().Count} cases under {Root}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_front_end_reads_a_case_as_its_header_says(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, "corpus", relativePath));
        var source = SourceText.FromBytes(relativePath, bytes, out var invalid);
        Assert.Null(invalid);
        var header = CaseHeader.Parse(source!.Text, out var problem);
        Assert.True(header is not null, problem);
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        var found = diagnostics.Items.Select(d => (d.Id, d.Line)).ToList();
        Assert.DoesNotContain(diagnostics.Items, d => !d.IsError);
        if (header!.Expectation == Expectation.Error && FrontEndIds.Contains(header.ErrorId!) && !CheckerSyntax.Contains(relativePath))
        {
            Assert.Equal([(header.ErrorId!, header.ErrorLine)], found);
        }
        else
        {
            Assert.Empty(found);
            Assert.NotEmpty(tree.Statements); // a tree dropped whole would pass the line above
        }
    }

    [Fact]
    public void The_front_end_finds_the_errors_of_at_least_the_floor_of_error_cases()
    {
        var found = 0;
        foreach (var relativePath in CaseFiles())
        {
            var text = File.ReadAllText(Path.Combine(Root, "corpus", relativePath));
            var header = CaseHeader.Parse(text, out _)!;
            if (header.Expectation == Expectation.Error && FrontEndIds.Contains(header.ErrorId!) && !CheckerSyntax.Contains(relativePath))
            {
                found++;
            }
        }

        Assert.True(found >= 15, $"only {found} cases expect an error of the front end");
    }

    [Theory]
    [InlineData("// expect: output\n// | 9\n// rules: V1\n\nx = 4\nprint(x + 5)\n", Expectation.Output, null, 0, 1, null, 5)]
    [InlineData("// expect: error assign-immutable at 6\n// rules: V7\n\nx = 4\nprint(x)\nx = 5\n", Expectation.Error, "assign-immutable", 6, 0, null, 4)]
    [InlineData("// expect: throws overflow at 6\n// | 1\n// build: test\n// rules: T5\n\nprint(1)\nprint(2147483647 + x)\n", Expectation.Throws, "overflow", 6, 1, BuildKind.Test, 6)]
    public void A_header_in_its_shape_is_read(string text, Expectation expectation, string? id, int line, int outputLines, BuildKind? build, int bodyLine)
    {
        var header = CaseHeader.Parse(text, out var problem);
        Assert.Null(problem);
        Assert.Equal((expectation, id, line, outputLines, build, bodyLine), (header!.Expectation, header.ErrorId, header.ErrorLine, header.OutputLines.Count, header.Build, header.BodyLine));
    }

    [Theory]
    [InlineData("// expects output\n// rules: V1\n\nx = 4\n")]
    [InlineData("// expect: output\n\nx = 4\n")]
    [InlineData("// expect: output\n// rules: v1\n\nx = 4\n")]
    [InlineData("// expect: output\n// rules: V1\nx = 4\n")]
    [InlineData("// expect: output\n// rules: V1\n\n")]
    [InlineData("// expect: error unused-variable at 5\n// | a\n// rules: V1\n\nx\n")]
    [InlineData("// expect: throws overflow at 4\n// build: debug\n// rules: V1\n\nx\n")]
    public void A_header_out_of_its_shape_is_refused(string text)
    {
        Assert.Null(CaseHeader.Parse(text, out var problem));
        Assert.NotNull(problem);
    }
}
