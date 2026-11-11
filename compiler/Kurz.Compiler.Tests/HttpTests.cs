using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
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

    [Fact]
    public void An_HttpError_prints_its_detail_alone_and_a_value_that_holds_one_prints_it_as_its_text()
    {
        // W2 (the owner, 2026-11-01, Q4 (a), and 2026-11-02, Q15 (a)): the `Text()` the body in std/ declares gives the detail alone, with
        // no words of the kind, wherever a text is taken (A5, D16), and a list, a union, a nullable form, a map or a data value that holds
        // one gives its text as A4 and A3 give theirs; equality, a key of a map and the fields stay a data type's (D3, D14)
        const string program = """
            data Held(HttpError Error)

            e = HttpError(HttpKind.Protocol, "no request line")
            print(e)
            print("error: {e}")
            print(string.Join(", ", [e, HttpError(HttpKind.TooLarge, "too large")]))
            print([e])
            string | IoError | HttpError o = e
            print(o)
            mut m = Map<string, HttpError>()
            m["a"] = e
            print(m)
            mut k = Map<HttpError, int>()
            k[e] = 1
            print(k)
            HttpError? n = null
            print(n)
            HttpError? s = e
            print(s)
            print(List<HttpError>())
            print(Held(e))
            print(e.Text())
            print(e == HttpError(HttpKind.Protocol, "no request line"))
            print(e.Kind)

            """;
        var run = BuildAndRun(program, "http-error-text");
        Assert.Equal((0, ""), (run.Code, run.Error));
        Assert.Equal(
            [
                "no request line", "error: no request line", "no request line, too large", "[no request line]", "no request line",
                "[a: no request line]", "[no request line: 1]", "null", "no request line", "[]", "Held { Error = no request line }",
                "no request line", "true", "Protocol",
            ],
            run.Output);
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

    [Theory]
    [InlineData("NameAt", "2", "b")]
    [InlineData("NameAt", "-1", "b")]
    [InlineData("ValueAt", "2", "2")]
    [InlineData("ValueAt", "-1", "2")]
    public void An_index_of_HttpQuery_outside_its_pairs_is_index_out_of_range(string member, string index, string last)
    {
        // W19 (the owner, 2026-11-08, #110, comment 6721051459, as W4's): with `i` outside 0 to `Count - 1`,
        // `NameAt(i)` and `ValueAt(i)` of an `HttpQuery` stop the program with index-out-of-range, the error line naming the line of
        // std/ that raised, as W4's do; the read of the last pair before it is the positive control
        var program = $$"""
            HttpQuery | IoError Query() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                client.Write("GET /p?a=1&b=2 HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
                match HttpRequest.Read(server) {
                    HttpRequest r => return r.QueryValues()
                    IoError e => return e
                    HttpError e => return HttpQuery()
                }
            }

            match Query() {
                HttpQuery q => {
                    print(q.{{member}}(1))
                    print(q.{{member}}({{index}}))
                }
                IoError e => print(e)
            }

            """;
        if (Builder.HostTarget == Target.Windows)
        {
            // S11: the program reads its request from a socket, which the runtime of Windows does not give; the refusal names the
            // program's first call on a socket, line 2, so that no other failure of the build passes for it
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
            Assert.Null(compilation.Program);
            Assert.Equal((2, Emitter.NotOnWindows), (Assert.Single(compilation.Diagnostics.Items).Line, Assert.Single(compilation.Diagnostics.Items).Message));
            return;
        }

        var lines = File.ReadAllLines(Path.Combine(CorpusTests.Root, "std", "std.kz"));
        var start = Array.FindIndex(lines, l => l.StartsWith("pub class HttpQuery {", StringComparison.Ordinal));
        Assert.True(start >= 0, "std/std.kz holds no `HttpQuery`");
        var line = Array.FindIndex(lines, start, l => l.Contains($"pub string {member}(int i) =>", StringComparison.Ordinal)) + 1;
        Assert.True(line > start, $"`HttpQuery` holds no `{member}`");
        var run = BuildAndRun(program, $"query-{member}-{index.Replace("-", "minus", StringComparison.Ordinal)}");
        Assert.Equal([last], run.Output);
        Assert.Equal((1, $"error: index-out-of-range at std/std.kz:{line}\n"), (run.Code, run.Error));
    }
}
