using System.Diagnostics;
using System.Globalization;
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
/// each write of a streamed response, a write over 64 KiB given its own length at the rate, and the time its end takes (W11), which need a client beside the server's loop, a second thread
/// no corpus case has; and the end of a chunked file's body, which goes out in the write of its last piece, where the bytes are the
/// same either way and only the time of a client that sends each request after the whole response tells them apart; and the reads of
/// a request that wait more than 5 seconds where its bounds allow it, which a case could only show by a run of that length; and what needs a client beside the loop for its time or its
/// socket: the bound of a write held across the calls of the system it makes, the close in stages after the server's answer, the
/// time of a body's read set before each refill, and a file whose write fails closed, with the refusal of the bounded write to a
/// program, which only std/ is given; and how long a bound of time waits at most, a bound of the wall's time that a corpus case,
/// which shows each bound's outcome and that it waited at least its length, does not hold.
/// </summary>
public sealed class HttpServerTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime, IReadOnlyCollection<string>? grants = null) =>
        Builder.Compile(SourceText.FromString("program.kz", text), grants ?? [], runtime, testBuild: true);

    /// <summary>std/ with <c>HttpServer.DateOf</c>, the <c>Date</c> the loop writes in the given second since 1970-01-01.</summary>
    private static readonly Lazy<CheckedRuntime> WithDate = new(() => ListTests.Edited(("std/std.kz", "pub class HttpServer {\n", """
        pub class HttpServer {
            pub static string DateOf(long second) => HttpDateOf(second)

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
                File.WriteAll("port", "{listener.Port}\n".Bytes)
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
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        var paused = false;
        using (var client = Connect(executable, run))
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
        Ran((0, "", "served\n"), (code, error, output));
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
                File.WriteAll("port", "{listener.Port}\n".Bytes)
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
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        bool ended;
        using (var client = Connect(executable, run))
        {
            client.GetStream().Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n"u8);
            ended = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))) == run;
        }

        var (code, output, error) = await run;
        Assert.Equal((true, 0, "", "TimedOut\nserved\n"), (ended, code, error, output));
    }

    [Fact]
    public async Task A_write_that_failed_leaves_the_writer_failed_so_that_the_body_ends_cut()
    {
        // W11 (assumed, section 12): a client that reads nothing makes a write of 512 KiB time out, part of its chunk written; the
        // function catches the `IoError`, waits for the byte of the request's body, which the client sends before it reads again, and
        // writes again: that write gives the `IoError` again and writes nothing, and the end of the body gives it to the loop, which
        // closes the connection, so that the client gets the body cut and never a chunk of `x` after the part of the failed one; the
        // writer, kept past the function, then gives `writer used after its response ended`, not the failed write's `IoError`
        const string text = """
            class Keeper {
                pub mut List<HttpWriter> Kept = List<HttpWriter>()

                pub void Keep(HttpWriter w) {
                    Kept.Add(w)
                }
            }

            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                mut piece = Bytes()
                while piece.Count < 524288 {
                    piece.Add(122)
                }

                keeper = Keeper()
                handler = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
                    keeper.Keep(w)
                    mut failed = false
                    while !failed {
                        w.Write(piece) else {
                            IoError e => {
                                print("first {e.Kind}")
                                failed = true
                            }
                        }
                    }

                    mut one = Bytes()
                    n = r.Body.Read(mut one, 1) else {
                        IoError e => return e
                        HttpError e => return IoError(IoKind.InvalidData, 0, e.Detail)
                    }

                    print("read {n}")
                    w.Write("x") else {
                        IoError e => print("again {e.Kind}")
                    }
                })
                HttpServer.ServeConnection(server, handler, HttpLimits(MinResponseDataRate: HttpRate(10_000_000, 100ms))) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print("served")
                keeper.Kept[0].Write("late") else {
                    IoError e => print("late {e}")
                }
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
        var executable = toolchain.Build(compilation.Program, "http-server-stream-failed-write");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        using (var client = Connect(executable, run, receiveBuffer: 65536))
        {
            var stream = client.GetStream();
            stream.Write("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 1\r\n\r\n"u8);

            // the write times out about 100 ms after the buffers of the connection are full
            Thread.Sleep(TimeSpan.FromSeconds(2));
            stream.Write("b"u8);
            client.Client.Shutdown(SocketShutdown.Send);
            var buffer = new byte[65536];
            while (true)
            {
                int n;
                try
                {
                    n = stream.Read(buffer);
                }
                catch (IOException)
                {
                    break;
                }

                if (n == 0)
                {
                    break;
                }

                got.Write(buffer, 0, n);
            }
        }

        var (code, output, error) = await run;
        var bytes = got.ToArray();
        Assert.Equal((0, "", "first TimedOut\nread 1\nagain TimedOut\nTimedOut\nserved\nlate invalid input: writer used after its response ended\n", false, false),
            (code, error, output, bytes.AsSpan().EndsWith("0\r\n\r\n"u8), bytes.AsSpan().IndexOf("1\r\nx"u8) >= 0));
    }

    [Fact]
    public async Task A_write_of_a_streamed_response_over_64_KiB_is_given_its_length_at_the_rate()
    {
        // W11 (assumed, section 12): a write over 64 KiB goes out on its own after its chunk's size line, and it is given the longer of
        // the grace and its own length at the `MinResponseDataRate` (W10), not the time of the size line before it, which is the grace
        // alone; a client that reads at 4 MB a second, above the rate of 500 KB a second, holds each write of 512 KiB about 130 ms
        // once the buffers of the connection are full, within the 1 second its length gives and past the 20 ms of the grace
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                mut piece = Bytes()
                while piece.Count < 524288 {
                    piece.Add(122)
                }

                streamed = HttpResponse.Stream(200, (HttpWriter w) => {
                    for i in 0..<32 {
                        w.Write(piece)
                    }
                })
                HttpServer.ServeConnection(server, (HttpRequest r) => streamed, HttpLimits(MinResponseDataRate: HttpRate(500_000, 20ms))) else {
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
        var executable = toolchain.Build(compilation.Program, "http-server-stream-big-write");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        long read = 0;
        using (var client = Connect(executable, run, receiveBuffer: 65536))
        {
            var stream = client.GetStream();
            stream.Write("GET /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n"u8);
            var buffer = new byte[65536];
            var clock = Stopwatch.StartNew();
            while (true)
            {
                int n;
                try
                {
                    n = stream.Read(buffer);
                }
                catch (IOException)
                {
                    break;
                }

                if (n == 0)
                {
                    break;
                }

                read += n;
                var due = TimeSpan.FromSeconds(read / 4_000_000.0) - clock.Elapsed;
                if (due > TimeSpan.Zero)
                {
                    Thread.Sleep(due);
                }
            }
        }

        var (code, output, error) = await run;
        Assert.Equal((0, "", "served\n", true), (code, error, output, read > 32 * 524288));
    }

    [Fact]
    public async Task A_streamed_response_ends_without_waiting_for_the_acknowledgement_of_its_last_chunk()
    {
        // W11 (assumed, section 12): the writer sets `TCP_NODELAY` on the stream at each streamed response, as Kestrel sets it on every
        // connection, so that the chunk that ends a body goes out at once after the chunk before it; under Nagle's algorithm it waits for the client's
        // acknowledgement, which a client that sends its next request only after the whole response gives at its delayed-ACK timer,
        // about 40 ms on Linux, so that 100 requests one after the other take 4 seconds and not a few milliseconds
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
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
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var whole = 0;
        var failed = "";
        var watch = new System.Diagnostics.Stopwatch();
        using (var client = Connect(executable, run))
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

    [Fact]
    public async Task A_chunked_file_ends_its_body_in_the_write_of_its_last_piece()
    {
        // W8: the chunk of no bytes that ends a chunked file goes out in the one write of the file's last piece, as hyper flushes the
        // end of a body with its last data; written on its own after that piece, it waits under Nagle's algorithm for the client's
        // acknowledgement, which a client that sends its next request only after the whole response gives at its delayed-ACK timer,
        // about 40 ms on Linux, so that 100 requests one after the other take 4 seconds and not a few milliseconds
        const string text = """
            void | IoError Main(string address) {
                File.WriteAll("http-server-coded-file.txt", "hello".Bytes)
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                coded = HttpResponse.File(200, "http-server-coded-file.txt")
                coded.Headers.Set("Transfer-Encoding", "gzip") else {
                    HttpError e => print(e.Detail)
                }

                HttpServer.ServeConnection(server, (HttpRequest r) => coded) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                File.Delete("http-server-coded-file.txt")
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
        var executable = toolchain.Build(compilation.Program, "http-server-coded-file");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var whole = 0;
        var failed = "";
        var watch = new System.Diagnostics.Stopwatch();
        using (var client = Connect(executable, run))
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
                failed = $"after {whole} responses, at {clock.Elapsed}: {e.Message}";
            }

            watch.Stop();
        }

        var (code, output, error) = await run;
        var seen = $"code {code} output [{output}] error [{error}] client [{failed}] after {clock.Elapsed}"; // the whole of what the run gave, which a tuple's display cuts
        Assert.True((code, error, output, whole, failed) == (0, "", "served\n", 100, ""), $"the program did not serve 100 chunked files: {whole} responses, {seen}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"100 chunked files took {watch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task A_read_of_a_request_waits_past_five_seconds_where_its_bounds_allow_it()
    {
        // W10 (section 12, Q10 (b) and Q11 (a)): a read of a head waits what is left of `RequestHeadersTimeout`, 30 seconds by
        // default, and a read of a body the longer of its grace and its bytes at the rate, less what its reads waited, with no
        // 5 seconds of a read on a socket inside the server; a pause of 5.5 seconds in the head and another in the body, with a grace
        // of 10 seconds, are both waited out. Held to 5 seconds, either read ends in its pause and the request is answered 408
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                echo = (HttpRequest r) => {
                    body = r.Body.ReadAllText(100) else {
                        IoError e => "{e.Kind}"
                        HttpError e => e.Detail
                        else => "?"
                    }

                    return HttpResponse.Text(200, body)
                }

                HttpServer.ServeConnection(server, echo, HttpLimits(MinRequestBodyDataRate: HttpRate(240, 10s))) else {
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
        var executable = toolchain.Build(compilation.Program, "http-server-long-reads");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        var failed = "";
        using (var client = Connect(executable, run))
        {
            var stream = client.GetStream();
            try
            {
                stream.Write("POST /a HTTP/1.1\r\nHost: h\r\n"u8);
                await Task.Delay(5500);
                stream.Write("Content-Length: 4\r\nConnection: close\r\n\r\nab"u8);
                await Task.Delay(5500);
                stream.Write("cd"u8);
                stream.CopyTo(got);
            }
            catch (IOException e)
            {
                // the server's exit code and what it printed say why the connection broke, so the client's error does not end the test
                failed = $"after {got.Length} bytes of the response, at {clock.Elapsed}: {e.Message}";
            }
        }

        var (code, output, error) = await run;
        var seen = $"code {code} output [{output}] error [{error}] client [{failed}] after {clock.Elapsed}"; // the whole of what the run gave, which a tuple's display cuts
        Assert.True((code, error, output, failed) == (0, "", "served\n", ""), $"the program did not serve the request: {seen}");
        var response = System.Text.Encoding.ASCII.GetString(got.ToArray());
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response);
        Assert.EndsWith("\r\n\r\nabcd", response);
    }

    [Theory]
    [InlineData("head", "RequestHeadersTimeout: 300ms, KeepAliveTimeout: 300ms", "GET /a HTTP/1.1\r\nHost: h\r\n", "request timed out\nserved\n")]
    [InlineData("first", "KeepAliveTimeout: 300ms", "", "served\n")]
    [InlineData("idle", "KeepAliveTimeout: 300ms", "GET /a HTTP/1.1\r\nHost: h\r\n\r\n", "served\n")]
    [InlineData("response", "MinResponseDataRate: HttpRate(1_000_000_000, 300ms)", "GET /large HTTP/1.1\r\nHost: h\r\n\r\n", "TimedOut\nserved\n")]
    public async Task A_bound_of_time_ends_its_connection_within_its_length(string name, string limits, string sent, string printed)
    {
        // W10 (section 12, the owner, 2026-11-01, "Kestrel's bounds" and "Add Kestrel's response rate", and 2026-10-31, "Persistent,
        // 100 req, 5 s"): a head that stops coming is answered `408` at `RequestHeadersTimeout`, its close in stages waiting
        // `KeepAliveTimeout` more for a client that sends nothing; a connection that sends no first byte, or none after a request, closes
        // at `KeepAliveTimeout`; and a response the client does not take ends at the grace of `MinResponseDataRate`. Each bound is
        // 300 ms against defaults of 5 or 30 seconds, and the connection ends, the program with it, within 2.5 seconds of the client's
        // last byte; the cases `serve-timeouts` and `serve-keep-alive` show each outcome and that it waited at least its bound. The clock
        // holds that span alone. The program writes its port once its body of 32 MiB is built, so that the client, which connects to
        // that port and whose connection the system completes before any accept, starts the clock where nothing but the accept is left
        // before the loop; and the end is the close of the program's listener, which resets a second connection the program never
        // accepts, read on the test's own thread. The end of the run's task is no such end: it waits for its reads of the program's
        // output on threads of the pool, and with every thread of the pool held by other work it came 13.6 seconds after a connection
        // that ended at 0.33 seconds (2026-11-09)
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                mut piece = Bytes()
                while piece.Count < 65536 {
                    piece.Add(122)
                }

                mut body = Bytes()
                while body.Count < 33554432 {
                    body.AddRange(piece)
                }

                large = HttpResponse.Bytes(200, body)
                handler = (HttpRequest r) => {
                    if r.Path == "/large" {
                        return large
                    }

                    return HttpResponse.Text(200, "hi")
                }

                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                HttpServer.ServeConnection(server, handler, HttpLimits(LIMITS)) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print("served")
            }

            Main(args[0]) else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text.Replace("LIMITS", limits, StringComparison.Ordinal), toolchain.Runtime);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, $"http-server-bound-{name}");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        TimeSpan ended;
        (int Code, string Output, string Error) result;
        using (var client = Connect(executable, run))
        using (var witness = Connect(executable, run))
        {
            witness.ReceiveTimeout = 60_000;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            client.GetStream().Write(System.Text.Encoding.ASCII.GetBytes(sent));
            try
            {
                _ = witness.GetStream().Read(new byte[1]);
            }
            catch (IOException)
            {
                // the reset of the listener's close, or the end of the 60 seconds, which the range below refuses
            }

            ended = watch.Elapsed;
            result = await run;
        }

        Ran((0, "", printed), (result.Code, result.Error, result.Output));
        Assert.InRange(ended, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(2500));
    }

    [Fact]
    public async Task A_write_of_a_response_is_held_to_its_bound_across_the_calls_of_the_system_it_makes()
    {
        // W10 (section 12, the default of the follow-up's product pass, Q10): a response of 32 MiB at 16 MB a second has 2.1 seconds
        // for its whole write, and the runtime sets what remains of them before each call of the system. A client that reads 64 KiB
        // every 50 ms keeps each call short, so a bound set whole for each call would not run out, and the client, reading at full
        // speed after 3 seconds, would get every byte; held across the calls, the write ends at its bound, `TimedOut`, the body cut. The
        // 3 seconds count from the first byte of the response, its head, which the server writes just before the body whose bound they
        // must outlast: counted from before the program started, they held its start and its build of the body, and where those took
        // over 0.9 seconds the bound ran out after the client read at full speed, and the body went out whole (2026-11-09). The client
        // waits by blocking its own thread, since after an `await` it goes on only once a thread of the pool takes it: a slow phase that
        // ends late leaves the defect's second call too little of its 2.1 seconds to take the rest
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
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
                HttpServer.ServeConnection(server, (HttpRequest r) => large, HttpLimits(MinResponseDataRate: HttpRate(16_000_000, 100ms))) else {
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
        var executable = toolchain.Build(compilation.Program, "http-server-write-bound");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(60),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        using (var client = Connect(executable, run))
        {
            var stream = client.GetStream();
            stream.Write("GET /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\n\r\n"u8);
            var buffer = new byte[65536];
            System.Diagnostics.Stopwatch? watch = null;
            int n;
            while ((n = stream.Read(buffer)) > 0)
            {
                watch ??= System.Diagnostics.Stopwatch.StartNew();
                got.Write(buffer, 0, n);
                if (watch.Elapsed < TimeSpan.FromSeconds(3))
                {
                    Thread.Sleep(50);
                }
            }
        }

        var (code, output, error) = await run;
        Ran((0, "", "TimedOut\nserved\n"), (code, error, output));
        var bytes = got.ToArray();
        var body = bytes.Length - (bytes.AsSpan().IndexOf("\r\n\r\n"u8) + 4);
        Assert.InRange(body, 1, 33554431);
    }

    [Theory]
    [InlineData("POST /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\nContent-Length: 40000\r\n\r\n", 40000, "HTTP/1.1 200 OK\r\n", "served\n")]
    [InlineData("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 60000\r\n\r\n", 10000, "HTTP/1.1 413 Content Too Large\r\n", "request body over limit\nserved\n")]
    public async Task A_connection_the_server_closes_after_its_answer_is_closed_in_stages(string head, int sent, string status, string printed)
    {
        // section 12 (the owner, 2026-11-02, "a half-close on `TcpStream`"), W9: once the server has written a response it closes the
        // connection, here for the request's `Connection: close`, or a refusal, here `413` for a body over its limit, it ends its
        // writes, reads and drops what the client still sends, and only then closes, as RFC 9112, section 9.6, has it. The body is
        // left unread either way; closed at once with bytes unread, the socket sends a reset in place of the end of the stream, and a
        // client that reads its answer after that sees the reset
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                HttpServer.ServeConnection(server, (HttpRequest r) => HttpResponse.Text(200, "ok"), HttpLimits(MaxRequestBodySize: 50000)) else {
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
        var executable = toolchain.Build(compilation.Program, $"http-server-staged-close-{sent}");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        string? reset = null;
        using (var client = Connect(executable, run))
        {
            var stream = client.GetStream();
            var request = new MemoryStream();
            request.Write(System.Text.Encoding.ASCII.GetBytes(head));
            request.Write(new byte[sent]);
            stream.Write(request.ToArray());
            await Task.Delay(500);
            try
            {
                stream.CopyTo(got);
            }
            catch (IOException e)
            {
                reset = e.Message;
            }
        }

        var (code, output, error) = await run;
        var response = System.Text.Encoding.ASCII.GetString(got.ToArray());
        Ran((0, "", printed), (code, error, output));
        Assert.Null(reset);
        Assert.StartsWith(status, response);
        Assert.Contains("\r\nConnection: close\r\n", response);
    }

    [Fact]
    public async Task A_read_of_a_body_sets_what_remains_of_its_time_before_each_refill()
    {
        // W10 (section 12, the owner, 2026-11-04, Q9 (a)): a read of a body may wait its grace, here 1 second, and each refill it makes
        // inside, the size line of a chunk among them, waits what remains of that. A size line of which two bytes come, 800 ms apart,
        // runs out at its second wait, and the request is answered `408` about 1 second after its head; a refill given the whole grace
        // again would answer 1 second after the second byte, 1.8 seconds after the head at the earliest, past the window, and one that
        // waited for the rest of the line without a bound would answer only once the client sent it, which this client does after the
        // answer. The client waits and reads by blocking its own thread: after an `await` it goes on only once a thread of the pool takes
        // it, and with every thread of the pool held by other work its wait of 800 ms ended at 1043 ms, after the answer (2026-11-09),
        // so that the time it read was the pool's, and a second byte sent after the first refill's whole second leaves a refill given
        // the whole grace again answering at 1 second too
        const string text = """
            void | IoError Main(string address) {
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                echo = (HttpRequest r) => {
                    body = r.Body.ReadAllText(100) else {
                        IoError e => "{e.Kind}"
                        HttpError e => e.Detail
                        else => "?"
                    }

                    return HttpResponse.Text(200, body)
                }

                HttpServer.ServeConnection(server, echo, HttpLimits(MinRequestBodyDataRate: HttpRate(1, 1s))) else {
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
        var executable = toolchain.Build(compilation.Program, "http-server-refill-time");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        var got = new MemoryStream();
        TimeSpan answered;
        using (var client = Connect(executable, run))
        {
            var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            stream.Write("POST /a HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n1"u8);
            Thread.Sleep(800);
            stream.Write("0"u8);
            var buffer = new byte[65536];
            var n = stream.Read(buffer);
            answered = watch.Elapsed;
            got.Write(buffer, 0, n);
            stream.Write("\r\n0123456789abcdef\r\n0\r\n\r\n"u8);
            client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);
            stream.CopyTo(got);
        }

        var (code, output, error) = await run;
        Ran((0, "", "request timed out\nserved\n"), (code, error, output));
        Assert.StartsWith("HTTP/1.1 408 Request Timeout\r\n", System.Text.Encoding.ASCII.GetString(got.ToArray()));
        Assert.InRange(answered, TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(1700));
    }

    [Fact]
    public async Task A_file_whose_write_fails_is_closed()
    {
        // W8 (section 12, the default of the follow-up's product pass, Q12): the file `HttpSendFile` opened is closed on every path, a
        // failed write among them, since a file closes at its last count (S5) when the send returns. A client that
        // reads nothing holds a file of 8 MiB past the bound of its first write, 100 ms at 1 MB a second, and the write gives
        // `TimedOut`; the file, the program's descriptor 5 after the listener's 3 and the connection's 4, is then closed
        const string text = """
            extern "C" {
                int fcntl(int fd, int command, ...)
            }

            // whether the descriptor is open, by `F_GETFD` (1)
            bool Open(int fd) {
                mut open = false
                raw {
                    open = fcntl(fd, 1) >= 0
                }

                return open
            }

            void | IoError Main(string address) {
                mut piece = Bytes()
                while piece.Count < 65536 {
                    piece.Add(122)
                }

                mut bytes = Bytes()
                while bytes.Count < 8388608 {
                    bytes.AddRange(piece)
                }

                File.WriteAll("served.bin", bytes)
                listener = TcpListener.Bind(address)
                File.WriteAll("port", "{listener.Port}\n".Bytes)
                server = listener.Accept()
                HttpServer.ServeConnection(server, (HttpRequest r) => HttpResponse.File(200, "served.bin"), HttpLimits(MinResponseDataRate: HttpRate(1_000_000, 100ms))) else {
                    IoError e => print(e.Kind)
                    HttpError e => print(e.Detail)
                }

                print(Open(3))
                print(Open(5))
            }

            Main(args[0]) else {
                IoError e => print(e)
            }
            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), ["raw", "native"], toolchain.Runtime, testBuild: true);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "http-server-file-closed");
        var run = Task.Run(() => Processes.Capture(executable, ["127.0.0.1:0"], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30),
            new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" }));
        using (var client = Connect(executable, run))
        {
            client.GetStream().Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n"u8);
            await run;
        }

        var (code, output, error) = await run;
        Ran((0, "", "TimedOut\ntrue\nfalse\n"), (code, error, output));
    }

    [Fact]
    public void A_program_sees_no_write_held_to_a_bound()
    {
        // section 12 (the default of the follow-up's product pass, Q10): `WriteWithin`, the write the server's responses take, is a
        // member of a stream that the checker admits in std/ alone; a program's call of it names no member, and the server that
        // calls it builds (the positive control); the refusal is pinned by its id and its line (E1, E2)
        const string text = """
            void | IoError Main(TcpStream s) {
                s.WriteWithin("a".Bytes, 1s)
            }
            """;
        var refused = Compile(text, Builder.CheckRuntime(Target.Linux));
        Assert.Null(refused.Program);
        Assert.Contains(refused.Diagnostics.Sorted,
            d => d.Id == ErrorIds.UnknownName && d.Line == 2 && d.Message.Contains("`WriteWithin`", StringComparison.Ordinal));
        Assert.NotNull(Compile("void | IoError | HttpError Main(TcpStream s) {\n    HttpServer.ServeConnection(s, (HttpRequest r) => HttpResponse.Text(200, \"\"))\n}\n", Builder.CheckRuntime(Target.Linux)).Program);
    }

    [Fact]
    public void A_response_held_to_its_rate_leaves_the_stream_its_default_bound()
    {
        // W10 (section 12, the default of the follow-up's product pass, Q10): `WriteWithin` sets `SO_SNDTIMEO` (21) to what remains of
        // a response's bound before each call of the system, and the stream's default bound of 5 seconds (S9) again before it returns,
        // so that a later write that sets no time of its own, the `100 Continue` of the next request among them, is not held to a
        // remainder; the handler reads the bound of the stream `Accept` gave, descriptor 5, at each request (as FileTests' `Bounds`),
        // under the grants `raw` and `native`; the first request has a body, so that its response is written before the second is
        // read and not held for it (W9)
        const string text = """
            extern "C" {
                int getsockopt(int fd, int level, int name, void* value, int* length)
                void* malloc(long size)
                void free(void* p)
            }

            string Bound(int fd, int option) {
                mut long seconds = -1
                mut long micros = -1
                raw {
                    void* time = malloc(16)
                    mut int length = 16
                    if getsockopt(fd, 1, option, time, &length) == 0 {
                        seconds = *Ptr<long>(time)
                        micros = *Ptr<long>(Ptr<byte>(time) + 8)
                    }

                    free(time)
                }

                return "{seconds} {micros}"
            }

            void | IoError | HttpError All() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                client.Write("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 1\r\n\r\nxGET /b HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
                client.ShutdownWrite()
                HttpServer.ServeConnection(server, (HttpRequest r) => {
                    print("{r.Path} {Bound(5, 21)}")
                    return HttpResponse.Empty(204)
                }, HttpLimits(MinResponseDataRate: HttpRate(10_000_000, 100ms)))
            }

            All() else {
                IoError e => print(e)
                HttpError e => print(e.Detail)
            }
            """;
        var compilation = Compile(text, toolchain.Runtime, ["raw", "native"]);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-server-default-bound"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(["/a 5 0", "/b 5 0"], output);
    }

    /// <summary>
    /// The exit code, the error and the output of a server program's run held to what is expected, each text whole in the message: a
    /// comparison of tuples cuts a long text, and what an error says last is cut first.
    /// </summary>
    private static void Ran((int Code, string Error, string Output) expected, (int Code, string Error, string Output) actual) =>
        Assert.True(expected == actual, $"exit {actual.Code}, not {expected.Code}; error [{actual.Error}]; output [{actual.Output}]; expected output [{expected.Output}]");

    /// <summary>A client of the program's listener, on the port the program wrote into the file <c>port</c> beside
    /// <paramref name="executable"/> once it listens, waited for until the file holds it or the program has ended.</summary>
    // the program binds port 0 and writes the port the system gave it, so that from the moment the client learns the port no socket
    // but the program's listener holds it: a port the test chose and freed for the program could be held by another socket before
    // the program bound it, and a client that reached that socket was reset while the program waited in its accept until the
    // test's time ended (`A_chunked_file_ends_its_body_in_the_write_of_its_last_piece`, twice on 2026-11-06, with the port
    // chosen below the system's range for port 0); the newline that ends the number says that the write is whole. No file of an
    // earlier run is read: each run of the class builds in a new temporary directory (`Toolchain`), which its end removes, and each
    // test, and each row of a theory, builds under a name of its own, so the only `port` beside a program is the one it wrote
    private static TcpClient Connect(string executable, Task program, int receiveBuffer = 0)
    {
        var file = Path.Combine(Path.GetDirectoryName(executable)!, "port");
        while (true)
        {
            var text = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            if (text.EndsWith('\n') && int.TryParse(text.AsSpan(0, text.Length - 1), out var port))
            {
                var client = new TcpClient();
                if (receiveBuffer > 0)
                {
                    // set before the connect, so that the window the client offers is no larger from its first segment on
                    client.ReceiveBufferSize = receiveBuffer;
                }

                try
                {
                    client.Connect("127.0.0.1", port);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }

                return client;
            }

            if (program.IsCompleted)
            {
                throw new InvalidOperationException($"the program ended before it wrote its port into {file}");
            }

            Thread.Sleep(20);
        }
    }
}
