using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// What the parse of a head gives (W5, W14; design record, section 12), read through functions an edited std/ adds, which hand
/// the parse a head held whole: every byte in a header's name and in its value, at each place a value's bytes are read eight at a
/// time and one at a time, for the server's head and the client's; and which error wins where a head has two faults, with the
/// headers the server judges after the header lines (<c>Host</c>, <c>Content-Length</c>, <c>Transfer-Encoding</c>,
/// <c>Expect</c>) spelled in any case and given more than once.
/// </summary>
public sealed class HttpParseTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// std/ with <c>HttpRequest.ParseOf</c> and <c>HttpRequest.ResponseOf</c>: the server's and the client's parse of a head held
    /// whole, its lines found as the reader finds them, written as one line: <c>ok</c> and what the head says, or the status and the
    /// text of the refusal (the server's), or the text of the error (the client's).
    /// </summary>
    private static readonly Lazy<CheckedRuntime> WithParse = new(() => ListTests.Edited(("std/std.kz", "    pub static HttpRequest | IoError | HttpError Read(TcpStream stream) {", """
            static int LineEnd(Bytes head) {
                for i in 0..<head.Count {
                    if head[i] == 10 {
                        return i - 1
                    }
                }

                return -1
            }

            static int HeadEnd(Bytes head) {
                mut lineStart = 0
                mut first = true
                for i in 0..<head.Count {
                    if head[i] == 10 {
                        if !first && i - 1 == lineStart {
                            return i - 1
                        }

                        first = false
                        lineStart = i + 1
                    }
                }

                return -1
            }

            static string Listed(HttpHeaders headers) {
                mut text = ""
                for i in 0..<headers.Count {
                    text = "{text}|{headers.NameAt(i)}={headers.ValueAt(i)}"
                }

                return text
            }

            pub static string ParseOf(Bytes head) {
                match HttpParseHead(head, LineEnd(head), HeadEnd(head), HttpLimits()) {
                    HttpHead h => return "ok {h.Method}|{h.Path}|{h.Query}|{h.Minor}|{h.Length}|{h.Chunked}|{h.Continue}{Listed(h.Headers)}"
                    HttpRefused no => return "{no.Status} {no.Error.Detail}"
                }
            }

            pub static string ResponseOf(Bytes head) {
                match HttpParseResponseHead(head, LineEnd(head), HeadEnd(head)) {
                    HttpResponseHead h => return "ok {h.Status}|{h.Length}|{h.Chunked}|{h.ToEnd}|{h.Keep}{Listed(h.Headers)}"
                    HttpError e => return e.Detail
                }
            }

            pub static HttpRequest | IoError | HttpError Read(TcpStream stream) {
        """)));

    /// <summary>A string literal of Kurz that holds <paramref name="text"/>, its line ends and tabs written as escapes.</summary>
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("{", "\\{", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    /// <summary>The bytes of a token (RFC 9110, section 5.6.2): a letter, a digit, or one of <c>!#$%&amp;'*+-.^_`|~</c>.</summary>
    private static bool Token(int b) => b is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "!#$%&'*+-.^_`|~".Contains((char)b, StringComparison.Ordinal);

    private const string Value = "abcdefghijklmnopqrst";

    /// <summary>The places of a value of 20 bytes whose byte is changed: its first and last, those of its first and second word of eight, and its tail after them.</summary>
    private static readonly int[] Places = [0, 3, 7, 8, 12, 15, 16, 19];

    /// <summary>The places of the last byte of a value of 1, 4 and 7 bytes on a line before another, whose CR stands in the value's one word of eight.</summary>
    private static readonly int[] ShortPlaces = [0, 3, 6];

    /// <summary>What the value <paramref name="of"/> with the byte at <paramref name="place"/> made <paramref name="b"/> gives: the value, or the words of what is wrong.</summary>
    private static (bool Ok, string Text) ValueWith(string of, int place, int b)
    {
        if (b == 9 || b >= 32 && b < 127)
        {
            var value = of[..place] + (char)b + of[(place + 1)..];
            return (true, b is 9 or 32 ? value.Trim(' ', '\t') : value);
        }

        return (false, b < 128 ? "control byte in a header" : "header not UTF-8");
    }

    private (int Code, IReadOnlyList<string> Output, string Error) Run(string program, string name)
    {
        var compilation = Compile(program, WithParse.Value);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name));
        return (code, output.ToList(), error);
    }

    [Fact]
    public void Every_byte_of_a_name_and_of_a_value_is_read_as_before_by_the_server_and_by_the_client()
    {
        // the bytes 10 and 13 end a line, which the reader finds before the parse; every other byte stands in the name, and at each
        // place of a value of 20 bytes, and last in a short value on a line before another, of the server's head and of the client's
        var bytes = Enumerable.Range(0, 256).Where(b => b is not 10 and not 13).ToList();
        var program = $$"""
            Bytes With(string before, int value, string after) {
                mut b = before.Bytes
                b.Add(byte(value))
                b.AddRange(after.Bytes)
                return b
            }

            void Sweep(string before, string after, bool response) {
                for v in 0..<256 {
                    if v != 10 && v != 13 {
                        if response {
                            print(HttpRequest.ResponseOf(With(before, v, after)))
                        } else {
                            print(HttpRequest.ParseOf(With(before, v, after)))
                        }
                    }
                }
            }

            Sweep({{Literal("GET / HTTP/1.1\r\nHost: h\r\nX")}}, {{Literal("Y: v\r\n\r\n")}}, false)
            {{string.Concat(Places.Select(p => $"Sweep({Literal("GET / HTTP/1.1\r\nHost: h\r\nX: " + Value[..p])}, {Literal(Value[(p + 1)..] + "\r\n\r\n")}, false)\n"))}}
            {{string.Concat(Places.Select(p => $"Sweep({Literal("HTTP/1.1 200 OK\r\nX: " + Value[..p])}, {Literal(Value[(p + 1)..] + "\r\n\r\n")}, true)\n"))}}
            {{string.Concat(ShortPlaces.Select(p => $"Sweep({Literal("GET / HTTP/1.1\r\nHost: h\r\nX: " + Value[..p])}, {Literal("\r\nY: " + Value + "\r\n\r\n")}, false)\n"))}}
            {{string.Concat(ShortPlaces.Select(p => $"Sweep({Literal("HTTP/1.1 200 OK\r\nX: " + Value[..p])}, {Literal("\r\nY: " + Value + "\r\n\r\n")}, true)\n"))}}
            """;
        var expected = new List<string>();
        foreach (var b in bytes)
        {
            expected.Add(Token(b) ? $"ok GET|/||1|0|false|false|Host=h|X{(char)b}Y=v"
                : b == ':' ? "ok GET|/||1|0|false|false|Host=h|X=Y: v"
                : b is 32 or 9 ? "400 malformed request: space before a header's colon"
                : "400 malformed request: bad header name");
        }

        foreach (var place in Places)
        {
            expected.AddRange(bytes.Select(b => ValueWith(Value, place, b)).Select(r => r.Ok ? $"ok GET|/||1|0|false|false|Host=h|X={r.Text}" : $"400 malformed request: {r.Text}"));
        }

        foreach (var place in Places)
        {
            expected.AddRange(bytes.Select(b => ValueWith(Value, place, b)).Select(r => r.Ok ? $"ok 200|0|false|true|false|X={r.Text}" : $"malformed response: {r.Text}"));
        }

        foreach (var place in ShortPlaces)
        {
            expected.AddRange(bytes.Select(b => ValueWith(Value[..(place + 1)], place, b)).Select(r => r.Ok ? $"ok GET|/||1|0|false|false|Host=h|X={r.Text}|Y={Value}" : $"400 malformed request: {r.Text}"));
        }

        foreach (var place in ShortPlaces)
        {
            expected.AddRange(bytes.Select(b => ValueWith(Value[..(place + 1)], place, b)).Select(r => r.Ok ? $"ok 200|0|false|true|false|X={r.Text}|Y={Value}" : $"malformed response: {r.Text}"));
        }

        var (code, output, error) = Run(program, "http-parse-bytes");
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(expected.Count, output.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == output[i], $"line {i + 1}: expected `{expected[i]}`, got `{output[i]}`");
        }
    }

    [Fact]
    public void Where_a_head_has_two_faults_the_same_one_wins_and_the_headers_judged_after_the_lines_are_read_in_any_case()
    {
        const string R = "GET / HTTP/1.1\r\n";
        const string P = "POST / HTTP/1.1\r\nHost: h\r\n";
        (string Head, string Says)[] heads =
        [
            // a fault of a header line, the first line's first
            (R + "Host: h\r\nX\u0001: v\r\nY: \u0001\r\n\r\n", "400 malformed request: bad header name"),
            (R + "Host: h\r\nY: \u0001\r\nX\u0001: v\r\n\r\n", "400 malformed request: control byte in a header"),
            (R + "Host: h\r\nX Y\u0001: v\r\n\r\n", "400 malformed request: space before a header's colon"),
            (R + "Host: h\r\nX\u0001 Y: v\r\n\r\n", "400 malformed request: bad header name"),
            (R + "Host: h\r\nX\u0001Y\r\n\r\n", "400 malformed request: bad header name"),
            (R + "Host: h\r\nXY\r\n\r\n", "400 malformed request: header without a colon"),
            (R + "Host: h\r\n: v\r\n\r\n", "400 malformed request: bad header name"),
            (R + "Host: h\r\n X: v\r\n\r\n", "400 malformed request: folded header"),
            (R + "Host: h\r\n\tX: v\r\n\r\n", "400 malformed request: folded header"),
            (R + "Host: h\r\nX: \t a b \t\r\n\r\n", "ok GET|/||1|0|false|false|Host=h|X=a b"),
            (R + "Host: h\r\nX:\r\n\r\n", "ok GET|/||1|0|false|false|Host=h|X="),
            (R + "Host: h\r\nX: \t \r\n\r\n", "ok GET|/||1|0|false|false|Host=h|X="),
            (R + "Host: h\r\nX: é\r\n\r\n", "ok GET|/||1|0|false|false|Host=h|X=é"),
            (R + "Host: h\r\nX: é\r\nY: abcdefgh\r\n\r\n", "ok GET|/||1|0|false|false|Host=h|X=é|Y=abcdefgh"),
            (R + "Host: h\r\nX: abcdefgé\u007f\r\n\r\n", "400 malformed request: control byte in a header"),
            // a fault of a header line wins over every fault of the headers judged after them
            (R + "X\u0001: v\r\n\r\n", "400 malformed request: bad header name"),
            (R + "Host: h\r\nHost: h\r\nX\u0001: v\r\n\r\n", "400 malformed request: bad header name"),
            (P + "Content-Length: x\r\nY: \u0001\r\n\r\n", "400 malformed request: control byte in a header"),
            // Host
            (R + "\r\n", "400 malformed request: no Host"),
            ("GET / HTTP/1.0\r\n\r\n", "ok GET|/||0|0|false|false"),
            (R + "Host: h\r\nhOsT: h\r\n\r\n", "400 malformed request: two Host headers"),
            (R + "HOST: h:80\r\n\r\n", "ok GET|/||1|0|false|false|HOST=h:80"),
            (R + "Host: h h\r\n\r\n", "400 malformed request: bad Host"),
            (R + "Hosts: h\r\n\r\n", "400 malformed request: no Host"),
            (R + "Hos: h\r\n\r\n", "400 malformed request: no Host"),
            (R + "Host: h\r\nHost: h\r\nContent-Length: x\r\n\r\n", "400 malformed request: two Host headers"),
            (R + "Content-Length: x\r\n\r\n", "400 malformed request: no Host"),
            (R + "Host: h h\r\nContent-Length: x\r\n\r\n", "400 malformed request: bad Host"),
            // Content-Length
            (P + "Content-Length: 5\r\n\r\n", "ok POST|/||1|5|false|false|Host=h|Content-Length=5"),
            (P + "content-LENGTH: 5\r\nContent-Length: 5\r\n\r\n", "ok POST|/||1|5|false|false|Host=h|content-LENGTH=5|Content-Length=5"),
            (P + "Content-Length: 5\r\nContent-Length: 6\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Length:\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Length: +5\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Length: 5 5\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Lengths: 5\r\n\r\n", "ok POST|/||1|0|false|false|Host=h|Content-Lengths=5"),
            (P + "Content_Length: 5\r\n\r\n", "ok POST|/||1|0|false|false|Host=h|Content_Length=5"),
            (P + "Transfer_Encoding: gzip\r\n\r\n", "ok POST|/||1|0|false|false|Host=h|Transfer_Encoding=gzip"),
            (P + "Content-Length: 99999999999999999999\r\n\r\n", "413 request body over limit"),
            (P + "Content-Length: 99999999999999999999\r\nContent-Length: x\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Length: 99999999999999999999\r\nTransfer-Encoding: chunked\r\n\r\n", "400 malformed request: Content-Length beside Transfer-Encoding"),
            (P + "Content-Length: x\r\nTransfer-Encoding: chunked\r\n\r\n", "400 malformed request: bad Content-Length"),
            (P + "Content-Length: 99999999999999999999\r\nTransfer-Encoding: gzip\r\n\r\n", "400 malformed request: Content-Length beside Transfer-Encoding"),
            (P + "Content-Length: 99999999999999999999\r\nExpect: 200-ok\r\n\r\n", "413 request body over limit"),
            // Transfer-Encoding
            (P + "Transfer-Encoding: chunked\r\n\r\n", "ok POST|/||1|0|true|false|Host=h|Transfer-Encoding=chunked"),
            (P + "transfer-encoding: CHUNKED\r\n\r\n", "ok POST|/||1|0|true|false|Host=h|transfer-encoding=CHUNKED"),
            (P + "Transfer-Encoding: gzip\r\n\r\n", "501 unsupported transfer encoding"),
            (P + "Transfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n\r\n", "501 unsupported transfer encoding"),
            (P + "Transfer-Encoding: chunked\r\nExpect: 200-ok\r\n\r\n", "417 unsupported expectation"),
            // Expect
            (P + "Expect: 100-continue\r\n\r\n", "ok POST|/||1|0|false|true|Host=h|Expect=100-continue"),
            (P + "eXpEcT: 100-Continue\r\n\r\n", "ok POST|/||1|0|false|true|Host=h|eXpEcT=100-Continue"),
            (P + "Expect: 100-continue\r\nExpect: x\r\n\r\n", "ok POST|/||1|0|false|true|Host=h|Expect=100-continue|Expect=x"),
            (P + "Expect: x\r\nExpect: 100-continue\r\n\r\n", "417 unsupported expectation"),
            ("POST / HTTP/1.0\r\nExpect: 100-continue\r\n\r\n", "ok POST|/||0|0|false|false|Expect=100-continue"),
            // the request line
            ("GET /a?b?c HTTP/1.1\r\nHost: h\r\n\r\n", "ok GET|/a|b?c|1|0|false|false|Host=h"),
            ("GET /é?é HTTP/1.1\r\nHost: h\r\n\r\n", "ok GET|/é|é|1|0|false|false|Host=h"),
            ("GET /? HTTP/1.1\r\nHost: h\r\n\r\n", "ok GET|/||1|0|false|false|Host=h"),
            ("GET ?q HTTP/1.1\r\nHost: h\r\n\r\n", "ok GET||q|1|0|false|false|Host=h"),
            ("get / HTTP/1.1\r\nHost: h\r\n\r\n", "ok get|/||1|0|false|false|Host=h"),
            ("M-SEARCH * HTTP/1.1\r\nHost: h\r\n\r\n", "ok M-SEARCH|*||1|0|false|false|Host=h"),
            ("CONNECT h:443 HTTP/1.1\r\nHost: h\r\n\r\n", "400 malformed request: CONNECT"),
            ("G\u0001T / HTTP/1.1\r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            (" GET / HTTP/1.1\r\nHost: h\r\n\r\n", "400 malformed request: empty method"),
            ("GET  / HTTP/1.1\r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            ("GET /\u007f HTTP/1.1\r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            ("GET / HTTP/1.1 \r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            ("GET / HTTP/2.0\r\nHost: h\r\n\r\n", "505 unsupported HTTP version"),
            ("GET / HTTP/1.2\r\nHost: h\r\n\r\n", "505 unsupported HTTP version"),
            ("GET / HTTP/1.x\r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            ("CONNECT / HTTP/1.x\r\nHost: h\r\n\r\n", "400 malformed request: bad request line"),
            ("CONNECT / HTTP/2.0\r\nHost: h\r\n\r\n", "505 unsupported HTTP version"),
            ("GET / HTTP/1.1\r\nX\u0001: v\r\n\r\n", "400 malformed request: bad header name"),
            ("GET / HTTP/1.x\r\nX\u0001: v\r\n\r\n", "400 malformed request: bad request line"),
        ];
        // a target that is not UTF-8 wins over a bad version, and a value that is not UTF-8 over every fault judged after the lines
        byte[][] raw =
        [
            [.. "GET /"u8, 0xFF, .. " HTTP/1.x\r\nHost: h\r\n\r\n"u8],
            [.. "GET /a?"u8, 0xC3, .. " HTTP/1.1\r\nHost: h\r\n\r\n"u8],
            [.. "GET / HTTP/1.1\r\nX: "u8, 0xFF, .. "\r\n\r\n"u8],
            [.. "GET / HTTP/1.1\r\nHost: h\r\nX: abcdefgh"u8, 0xC3, .. "\r\n\r\n"u8],
            [.. "GET / HTTP/1.1\r\nHost: h\r\nX: "u8, 0xFF, 0x01, .. "\r\n\r\n"u8],
        ];
        string[] rawSays =
        [
            "400 malformed request: target not UTF-8",
            "400 malformed request: target not UTF-8",
            "400 malformed request: header not UTF-8",
            "400 malformed request: header not UTF-8",
            "400 malformed request: control byte in a header",
        ];
        (string Head, string Says)[] responses =
        [
            ("HTTP/1.1 200 OK\r\nX\u0001: v\r\nContent-Length: x\r\n\r\n", "malformed response: bad header name"),
            ("HTTP/1.1 200 OK\r\ncontent-length: 5\r\n\r\n", "ok 200|5|false|false|true|content-length=5"),
            ("HTTP/1.1 200 OK\r\nX: é\r\nContent-Length: 0\r\n\r\n", "ok 200|0|false|false|true|X=é|Content-Length=0"),
            ("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nX: \u0001\r\n\r\n", "malformed response: control byte in a header"),
            ("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n", "ok 204|0|false|false|false|Connection=close"),
        ];
        var program = $$"""
            Bytes Of(List<int> values) {
                mut b = Bytes()
                for v in values {
                    b.Add(byte(v))
                }

                return b
            }

            {{string.Concat(heads.Select(h => $"print(HttpRequest.ParseOf({Literal(h.Head)}.Bytes))\n"))}}
            {{string.Concat(raw.Select(r => $"print(HttpRequest.ParseOf(Of([{string.Join(", ", r.Select(b => b.ToString(CultureInfo.InvariantCulture)))}])))\n"))}}
            {{string.Concat(responses.Select(h => $"print(HttpRequest.ResponseOf({Literal(h.Head)}.Bytes))\n"))}}
            """;
        var expected = heads.Select(h => h.Says).Concat(rawSays).Concat(responses.Select(h => h.Says)).ToList();
        var (code, output, error) = Run(program, "http-parse-faults");
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(expected.Count, output.Count);
        var all = heads.Select(h => h.Head).Concat(raw.Select(r => Convert.ToHexString(r))).Concat(responses.Select(h => h.Head)).ToList();
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == output[i], $"head {i + 1} ({all[i].Replace("\r\n", "\\r\\n", StringComparison.Ordinal)}): expected `{expected[i]}`, got `{output[i]}`");
        }
    }

    [Fact]
    public void The_parse_s_own_way_to_add_a_header_is_no_member_a_program_sees()
    {
        // W4 with F9: `Append`, which adds a name and a value the parse proved free of CR and LF, is `internal` to std/, so a program
        // adds a header by `Add` alone, which refuses a CR or a LF
        var hidden = Compile("h = HttpHeaders()\nh.Append(\"a\", \"b\")\n", WithParse.Value);
        var error = Assert.Single(hidden.Diagnostics.Items);
        Assert.Equal((ErrorIds.NotVisible, 2), (error.Id, error.Line));
        Assert.Contains("`Append` of `HttpHeaders` is `internal`", error.Message, StringComparison.Ordinal);

        // the positive control: the same call of `Add`, its refusal handled
        Assert.Empty(Compile("h = HttpHeaders()\nh.Add(\"a\", \"b\") else {\n    HttpError e => print(e.Detail)\n}\n", WithParse.Value).Diagnostics.Items);
    }
}
