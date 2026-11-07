using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions of the server's reader of requests (W5, W6; design record, section 12) that no corpus case pins: the status the
/// server answers each refusal with, which a program does not see, read through a function an edited std/ adds, with every limit of
/// <c>HttpLimits()</c> at its edge as Kestrel counts it; and a build for Windows, whose runtime gives no socket, says
/// <c>not supported</c> at the line of the program whose call reaches the socket through std/, where its author can act; and no
/// form of <c>Read</c> that takes limits, which a case that expects a run cannot show beside its run.
/// </summary>
public sealed class HttpRequestTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// std/ with <c>HttpRequest.StatusOf</c>, the status the server answers the stream's next request with: 200 for a request it
    /// reads, the status of a refusal, 0 where it answers none, -1 for a failure of the network.
    /// </summary>
    private static readonly Lazy<CheckedRuntime> WithStatus = new(() => ListTests.Edited(("std/std.kz", "    pub static HttpRequest | IoError | HttpError Read(TcpStream stream) {", """
            pub static int StatusOf(TcpStream stream) {
                match HttpReadRequest(stream, HttpLimits()) {
                    HttpRequest r => return 200
                    IoError e => return -1
                    HttpRefused no => return no.Status
                }
            }

            pub static HttpRequest | IoError | HttpError Read(TcpStream stream) {
        """)));

    /// <summary>A string literal of Kurz that holds <paramref name="text"/>, its line ends written as escapes.</summary>
    private static string Literal(string text) => "\"" + text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    [Fact]
    public void Each_refusal_is_answered_with_its_status_and_each_limit_holds_at_its_edge()
    {
        // a request line of 8192 bytes with its CR LF; with Host, 100 header lines; header lines of 32768 bytes with their CR LFs,
        // and the empty line's 2 (Kestrel's `MaxRequestHeadersTotalSize + 2`); a declared body of 30000000 bytes
        var line = new string('a', 8176);
        var lines = string.Concat(Enumerable.Range(0, 99).Select(i => $"X-{i}: v\r\n"));
        var pad = new string('v', 32754);
        (string Request, int Status)[] requests =
        [
            ("GET / HTTP/1.1\r\nHost: h\r\n\r\n", 200),
            ("", 0),
            ("GET / HTTP/1.1\r\nHost: h\r\n", 400),
            ("GET / HTTP/1.1\nHost: h\n\n", 400),
            ("GET / HTTP/1.1\r\nHost : h\r\n\r\n", 400),
            ($"GET /{line} HTTP/1.1\r\nHost: h\r\n\r\n", 200),
            ($"GET /{line}a HTTP/1.1\r\nHost: h\r\n\r\n", 414),
            ($"GET / HTTP/1.1\r\nHost: h\r\n{lines}\r\n", 200),
            ($"GET / HTTP/1.1\r\nHost: h\r\n{lines}X-99: v\r\n\r\n", 431),
            ($"GET / HTTP/1.1\r\nHost: h\r\nX: {pad}\r\n\r\n", 200),
            ($"GET / HTTP/1.1\r\nHost: h\r\nX: {pad}v\r\n\r\n", 431),
            ("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 30000000\r\n\r\n", 200),
            ("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 30000001\r\n\r\n", 413),
            ("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip\r\n\r\n", 400),
            ("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip, chunked\r\n\r\n", 501),
            ("GET / HTTP/2.0\r\nHost: h\r\n\r\n", 505),
            ("POST / HTTP/1.1\r\nHost: h\r\nExpect: 200-ok\r\n\r\n", 417),
        ];
        var text = $$"""
            void | IoError Show(string request) {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                client.Write(request.Bytes)
                client.ShutdownWrite()
                print(HttpRequest.StatusOf(server))
            }

            void | IoError All() {
            {{string.Concat(requests.Select(r => $"    Show({Literal(r.Request)})\n"))}}}

            All() else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text, WithStatus.Value);
        if (Builder.HostTarget == Target.Windows)
        {
            // the program's own call on a socket, line 2
            Assert.Null(compilation.Program);
            Assert.Equal((2, Emitter.NotOnWindows), (Assert.Single(compilation.Diagnostics.Items).Line, Assert.Single(compilation.Diagnostics.Items).Message));
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-request-status"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(requests.Select(r => r.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)), output);
    }

    [Fact]
    public void A_build_for_windows_says_not_supported_at_the_line_of_the_program_whose_call_reaches_a_socket_through_std()
    {
        // S11 with W5: `HttpRequest.Read` is std/'s, and its read of the socket lies calls deep in std/; the refusal names the line of
        // the program that called it, not a line of std/, and not none
        const string text = """
            print(1)
            void | IoError | HttpError Show(TcpStream s) {
                r = HttpRequest.Read(s)
                print(r.Method)
            }

            TcpStream? none = null
            if none != null {
                Show(none) else {
                    IoError e => print(e)
                    HttpError e => print(e.Detail)
                }
            }
            """;
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((3, Emitter.NotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.StartsWith("program.kz:3: not supported: a call on a file or a socket", diagnostic.At("program.kz"), StringComparison.Ordinal);
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }

    [Fact]
    public void A_program_reads_a_request_under_the_default_limits_alone()
    {
        // W7 (section 12, the owner, 2026-11-02, Q3): `HttpRequest.Read(stream)` reads under `HttpLimits()`, and the limits a server
        // is given reach the parser through an entry of std/ that no program calls, with no second public form; a call of `Read` with
        // limits and a call of that entry are refused, beside the form with the stream alone, the positive control, which checks
        const string text = """
            void | IoError | HttpError Show(TcpStream s) {
                r = HttpRequest.Read(s)
                print(r.Method)
                other = HttpRequest.Read(s, HttpLimits())
                inner = HttpReadRequest(s, HttpLimits())
            }
            """;
        var compilation = Compile(text, Builder.CheckRuntime(Target.Linux));
        var errors = compilation.Diagnostics.Sorted.Select(d => (d.Id, d.Line, d.Message)).ToList();
        Assert.Equal([("argument-mismatch", 4, "the call passes 2 arguments to 1 parameter (D11)"), ("unknown-name", 5, "no `HttpReadRequest` is visible here (V10)")], errors);
    }
}
