using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on HTTP (chapter 16; design record, section 12) that no corpus case pins: an index of <c>HttpHeaders</c> outside its
/// headers stops the program at the line of <c>std/</c> that raised (W4, section 6), where a case's header names a line of the case's
/// own file alone.
/// </summary>
public sealed class HttpTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    [Theory]
    [InlineData("NameAt", "2", "Accept")]
    [InlineData("NameAt", "-1", "Accept")]
    [InlineData("ValueAt", "2", "b")]
    [InlineData("ValueAt", "-1", "b")]
    public void An_index_of_HttpHeaders_outside_its_headers_is_index_out_of_range(string member, string index, string last)
    {
        // W4 (section 12: the owner, 2026-11-03, #110, comment 6615254370): with `i` outside 0 to `Count - 1`, `NameAt(i)` and
        // `ValueAt(i)` stop the program with index-out-of-range, as a list's index does (T14); the error line names the line of std/ that
        // raised, as section 6 says of every exception *(assumed: #110, comment 6616414692, parked with this default and not put to the
        // owner, against the program's line and against both)*; the read of the last header before it is the positive control
        var program = "void | HttpError Fill(HttpHeaders headers) {\n    headers.Add(\"Host\", \"a\")\n    headers.Add(\"Accept\", \"b\")\n}\n\n"
            + $"h = HttpHeaders()\nFill(h) else {{\n    HttpError e => print(e.Detail)\n}}\nprint(h.{member}(1))\nprint(h.{member}({index}))\n";
        var line = Array.FindIndex(File.ReadAllLines(Path.Combine(CorpusTests.Root, "std", "std.kz")), l => l.Contains($"pub string {member}(int i) =>", StringComparison.Ordinal)) + 1;
        Assert.True(line > 0, $"std/std.kz holds no `{member}`");
        var run = BuildAndRun(program, $"headers-{member}-{index.Replace("-", "minus", StringComparison.Ordinal)}");
        Assert.Equal([last], run.Output);
        Assert.Equal((1, $"error: index-out-of-range at std/std.kz:{line}\n"), (run.Code, run.Error));
    }
}
