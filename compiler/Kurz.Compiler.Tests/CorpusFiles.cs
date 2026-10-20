using Kurz.Compiler.Corpus;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>The corpus under <c>corpus/</c>, read where it lies: the one place the suites find the repository and read a case.</summary>
internal static class CorpusFiles
{
    /// <summary>The repository's root: the directory above the suite that holds <c>corpus/</c> and the design record.</summary>
    public static readonly string Root = FindRoot();

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
    public static List<string> CaseFiles()
    {
        var corpus = Path.Combine(Root, "corpus");
        return Directory.EnumerateFiles(corpus, "*.kz", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(corpus, file).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The case's text, as the front end reads it; the test fails on a file that is not valid UTF-8.</summary>
    public static SourceText Source(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, "corpus", relativePath));
        var source = SourceText.FromBytes(relativePath, bytes, out var invalid);
        Assert.True(source is not null, $"{relativePath}: {invalid}");
        return source!;
    }

    /// <summary>The case's header and its tree, with what the front end said about it.</summary>
    public static (CaseHeader Header, ProgramSyntax Tree, DiagnosticBag FrontEnd) ReadCase(string relativePath)
    {
        var source = Source(relativePath);
        var header = CaseHeader.Parse(source.Text, out var problem);
        Assert.True(header is not null, $"{relativePath}: {problem}");
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        return (header!, tree, diagnostics);
    }
}
