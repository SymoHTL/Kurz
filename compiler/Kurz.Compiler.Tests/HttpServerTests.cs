using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions of the server's loop (W9; design record, section 12) that no corpus case pins: the text of the <c>Date</c> it writes,
/// which a case cannot hold since it is the time of the run, read through a function an edited std/ adds and held to .NET's
/// <c>"r"</c> form of the same second; a handler that throws, which ends the program at the throw, where a corpus case would name
/// the rules of the throw and not the server's; and a build for Windows, whose runtime gives no socket, says <c>not supported</c> at
/// the line of the program that calls <c>Serve</c> or <c>ServeConnection</c>; and the time a write of a response is given (W10), and
/// each write of a streamed response and the time its end takes (W11), which need a client beside the server's loop, a second thread
/// no corpus case has.
/// </summary>
public sealed class HttpServerTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>std/ with <c>HttpServer.DateOf</c>, the <c>Date</c> the loop writes in the given second since 1970-01-01.</summary>
    private static readonly Lazy<CheckedRuntime> WithDate = new(() => ListTests.Edited(("std/std.kz", "pub class HttpServer {\n", """
        pub class HttpServer {
            pub static string DateOf(long second) => HttpDate(second)

        """)));

    [Fact]
    public void The_date_is_the_imf_fixdate_of_its_second_as_dotnet_writes_it()
    {
        // the example of RFC 9110, section 5.6.7, the epoch, a second before it, a leap day, the days around the ends of years and
        // of centuries, and a second of every 997th day from 1970 to 2100, each against .NET's `r`, RFC 1123's form
        long[] edges = [784111777, 0, -1, 951782400, 951868799, 4102444799, 4107542400, 946684799, 946684800, 1793179800, 253402300799];
        var seconds = edges.Concat(Enumerable.Range(0, 48).Select(i => (long)i * 997 * 86400 + i * 3607)).ToArray();
        var text = $$"""
            {{string.Concat(seconds.Select(s => $"print(HttpServer.DateOf({s}))\n"))}}
            """;
        var compilation = Compile(text, WithDate.Value);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-server-date"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal("Sun, 06 Nov 1994 08:49:37 GMT", output[0]);
        Assert.Equal(seconds.Select(s => DateTimeOffset.FromUnixTimeSeconds(s).ToString("r", CultureInfo.InvariantCulture)), output);
    }

    [Fact]
    public void A_handler_that_throws_ends_the_program_at_its_throw()
    {
        // the first request is served, the second throws in the handler: nothing after the call runs, and the error names the line
        // of the throw in the program
        const string text = """
            void | IoError | HttpError All() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                client.Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
                client.ShutdownWrite()
                HttpServer.ServeConnection(server, (HttpRequest r) => {
                    if r.Path == "/b" {
                        throw "no handler for {r.Path}"
                    }

                    print("served")
                    return HttpResponse.Empty(204)
                })
                print("after")
            }

            All() else {
                IoError e => print(e)
                HttpError e => print(e.Detail)
            }
            """;
        var compilation = Compile(text, toolchain.Runtime);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-server-throws"));
        Assert.Equal(1, code);
        Assert.Equal(["served"], output);
        Assert.Equal("error: thrown at program.kz:9:\nno handler for /b\n", error);
    }

    [Theory]
    [InlineData("HttpServer.ServeConnection(s, (HttpRequest r) => HttpResponse.Empty(204))")]
    [InlineData("HttpServer.Serve(l, (HttpRequest r) => HttpResponse.Empty(204))")]
    public void A_build_for_windows_says_not_supported_at_the_line_of_the_program_that_serves(string call)
    {
        // S11 with W9: the loop is std/'s, and its calls on the socket lie calls deep in std/; the refusal names the line of the
        // program that called it
        var text = $$"""
            print(1)
            void | IoError | HttpError Run(TcpListener l, TcpStream s) {
                {{call}}
            }

            TcpListener? none = null
            TcpStream? stream = null
            if none != null && stream != null {
                Run(none, stream) else {
                    IoError e => print(e)
                    HttpError e => print(e.Detail)
                }
            }
            """;
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((3, Emitter.NotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }

    [Fact]
    public async Task A_write_of_a_response_is_given_the_longer_of_the_grace_and_its_length_at_the_rate()
    {
        // W10 (assumed, section 12, as Kestrel gives it): a response of 32 MiB at 10 MB a second has 3.3 seconds for its write, so a
        // client that stops reading for 500 ms, five times the grace of 100 ms, still gets every byte; a write held to the grace alone
        // ends in the pause, the connection closed with the body cut. 32 MiB is more than the buffers of the connection hold, so the
        // server's write waits in the pause. The client is this test's socket, beside the program's loop
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                server = listener.Accept()
                mut piece = Bytes()
                while piece.Count < 65536 {
                    piece.Add(122)
                }

                mut body = Bytes()
                while body.Count < 33554432 {
                    body.AddRange(piece)
                }

                large = HttpResponse.Bytes(200, body)
                HttpServer.ServeConnection(server, (HttpRequest r) => large, HttpLimits(MinResponseDataRate: HttpRate(10_000_000, 100ms))) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print("served")
            }

            Main(args[0]) else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text, toolchain.Runtime);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "http-server-pace");
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        var run = Task.Run(() => Processes.Capture(executable, [$"127.0.0.1:{port}"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        var paused = false;
        using (var client = Connect(port, run))
        {
            var stream = client.GetStream();
            stream.Write("GET /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n"u8);
            var buffer = new byte[65536];
            int n;
            while ((n = stream.Read(buffer)) > 0)
            {
                got.Write(buffer, 0, n);
                if (!paused && got.Length >= 1 << 20)
                {
                    paused = true;
                    await Task.Delay(500);
                }
            }
        }

        var (code, output, error) = await run;
        Assert.Equal((0, "", "served\n"), (code, error, output));
        var bytes = got.ToArray();
        var body = bytes.Length - (bytes.AsSpan().IndexOf("\r\n\r\n"u8) + 4);
        Assert.Equal((true, 33554432), (paused, body));
    }

    [Fact]
    public async Task Each_write_of_a_streamed_response_is_held_to_the_rate()
    {
        // W11 (assumed, section 12): each `w.Write` is a write of the response, given the longer of the grace and its length at the
        // `MinResponseDataRate` (W10); a client that reads nothing fills the buffers of the connection, the next write of 512 KiB at
        // 10 MB a second times out after 100 ms, and the function passes the `IoError` on to the loop, which gives it; a write held
        // to no time waits until the client closes, 10 seconds later, and gives the error of that close
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                server = listener.Accept()
                mut piece = Bytes()
                while piece.Count < 524288 {
                    piece.Add(122)
                }

                streamed = HttpResponse.Stream(200, (HttpWriter w) => {
                    for i in 0..<64 {
                        w.Write(piece)
                    }
                })
                HttpServer.ServeConnection(server, (HttpRequest r) => streamed, HttpLimits(MinResponseDataRate: HttpRate(10_000_000, 100ms))) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print("served")
            }

            Main(args[0]) else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text, toolchain.Runtime);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "http-server-stream-pace");
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        var run = Task.Run(() => Processes.Capture(executable, [$"127.0.0.1:{port}"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        bool ended;
        using (var client = Connect(port, run))
        {
            client.GetStream().Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n"u8);
            ended = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))) == run;
        }

        var (code, output, error) = await run;
        Assert.Equal((true, 0, "", "TimedOut\nserved\n"), (ended, code, error, output));
    }

    [Fact]
    public async Task A_streamed_response_ends_without_waiting_for_the_acknowledgement_of_its_last_chunk()
    {
        // W11 (assumed, section 12): the writer sets `TCP_NODELAY` on the stream, as Kestrel sets it on every connection, so that the
        // chunk that ends a body goes out at once after the chunk before it; under Nagle's algorithm it waits for the client's
        // acknowledgement, which a client that sends its next request only after the whole response gives at its delayed-ACK timer,
        // about 40 ms on Linux, so that 100 requests one after the other take 4 seconds and not a few milliseconds
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                server = listener.Accept()
                streamed = HttpResponse.Stream(200, (HttpWriter w) => w.Write("hello"))
                HttpServer.ServeConnection(server, (HttpRequest r) => streamed) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print("served")
            }

            Main(args[0]) else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text, toolchain.Runtime);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "http-server-stream-nodelay");
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        var run = Task.Run(() => Processes.Capture(executable, [$"127.0.0.1:{port}"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var whole = 0;
        var failed = "";
        var watch = new System.Diagnostics.Stopwatch();
        using (var client = Connect(port, run))
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var buffer = new byte[65536];
            watch.Start();
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    stream.Write(i == 99 ? "GET / HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n"u8 : "GET / HTTP/1.1\r\nHost: h\r\n\r\n"u8);
                    var got = new MemoryStream();
                    while (!got.ToArray().AsSpan().EndsWith("\r\n0\r\n\r\n"u8))
                    {
                        var n = stream.Read(buffer);
                        if (n == 0)
                        {
                            break;
                        }

                        got.Write(buffer, 0, n);
                    }

                    whole += got.ToArray().AsSpan().EndsWith("5\r\nhello\r\n0\r\n\r\n"u8) ? 1 : 0;
                }
            }
            catch (IOException e)
            {
                // the server's exit code and what it printed say why the connection broke, so the client's error does not end the test
                failed = $"after {whole} responses: {e.Message}";
            }

            watch.Stop();
        }

        var (code, output, error) = await run;
        Assert.Equal((0, "", "served\n", 100, ""), (code, error, output, whole, failed));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"100 streamed responses took {watch.Elapsed.TotalMilliseconds:F0} ms");
    }

    /// <summary>A client of the program's listener on <paramref name="port"/>, tried until it listens or the program has ended.</summary>
    private static TcpClient Connect(int port, Task program)
    {
        while (true)
        {
            try
            {
                return new TcpClient("127.0.0.1", port);
            }
            catch (SocketException) when (!program.IsCompleted)
            {
                Thread.Sleep(20);
            }
        }
    }
}
