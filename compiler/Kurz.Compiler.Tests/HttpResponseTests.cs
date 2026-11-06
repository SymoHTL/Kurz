using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions of the server's writer of responses (W8; design record, section 12) that no corpus case pins: a status outside
/// 200 to 599 stops the program, a `1xx` among them, and so does a `205` given a body by <c>Text</c> or <c>Bytes</c>, which a case cannot
/// hold since the error line names the line of std/ that throws; a file whose
/// size changes between the head and the body, which a program cannot time, read through a function an edited std/ adds; and a
/// build for Windows, whose runtime gives no socket, says <c>not supported</c> at the line of the program whose write reaches the
/// socket through std/.
/// </summary>
public sealed class HttpResponseTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// std/ with <c>HttpResponse.SendFileOf</c>, which sends the file at a path as the body of a head that gave it
    /// <c>length</c> bytes, as a file whose size was read before it changed.
    /// </summary>
    private static readonly Lazy<CheckedRuntime> WithSendFile = new(() => ListTests.Edited(("std/std.kz", "    pub void | IoError Write(TcpStream stream) {\n", """
            pub static void | IoError SendFileOf(TcpStream stream, string path, long length) => HttpSendFile(stream, HttpNoBytes(), path, length, false)

            pub void | IoError Write(TcpStream stream) {

        """)));

    [Theory]
    [InlineData("Text(600, \"x\")", 600)]
    [InlineData("Bytes(99, \"x\".Bytes)", 99)]
    [InlineData("Empty(100)", 100)]
    [InlineData("Text(199, \"x\")", 199)]
    [InlineData("Empty(-1)", -1)]
    [InlineData("File(1000, \"x.txt\")", 1000)]
    public void A_status_outside_200_to_599_stops_the_program_with_its_text(string make, int status)
    {
        // the edges 200 and 599 make a response, the positive control; the status past them is thrown from std/, and nothing after it
        // runs
        var text = $"""
            print(HttpResponse.Empty(200).Status)
            print(HttpResponse.Empty(599).Status)
            HttpResponse.{make}
            print("after")
            """;
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-response-status"));
        Assert.Equal(1, code);
        Assert.Equal(["200", "599"], output);
        var lines = error.Split('\n');
        Assert.StartsWith("error: thrown at std/std.kz:", lines[0], StringComparison.Ordinal);
        Assert.Equal([$"status {status} is outside 200 to 599", ""], lines[1..]);
    }

    [Theory]
    [InlineData("Text(205, \"x\")")]
    [InlineData("Bytes(205, \"x\".Bytes)")]
    public void A_205_given_a_body_by_Text_or_Bytes_stops_the_program_with_its_text(string make)
    {
        // an empty 205 from each of the four functions, a file's among them, whose length is taken when it is written, makes a
        // response, the positive control; a body given with it is thrown from std/ (design record, section 12: the owner,
        // 2026-11-02, Q14, and the reading said back with it, assumed), and nothing after it runs
        var text = $"""
            print(HttpResponse.Text(205, "").Status)
            print(HttpResponse.Bytes(205, Bytes()).Status)
            print(HttpResponse.Empty(205).Status)
            print(HttpResponse.File(205, "none.txt").Status)
            HttpResponse.{make}
            print("after")
            """;
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-response-205"));
        Assert.Equal(1, code);
        Assert.Equal(["205", "205", "205", "205"], output);
        var lines = error.Split('\n');
        Assert.StartsWith("error: thrown at std/std.kz:", lines[0], StringComparison.Ordinal);
        Assert.Equal(["status 205 takes no body", ""], lines[1..]);
    }

    [Fact]
    public void A_file_whose_size_changed_since_its_head_is_invalid_data_and_no_byte_past_that_size_is_sent()
    {
        // the file holds 9 bytes: as long as its head said, all of it, the positive control; shorter than its head said, the 9 bytes
        // and then the error, as the peer can tell from the length; longer, the error before a piece past the length is sent
        const string text = """
            void | IoError Send(long length) {
                File.WriteAll("changed.txt", "file body".Bytes)
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                HttpResponse.SendFileOf(server, "changed.txt", length) else {
                    IoError e => print(e)
                }

                server.ShutdownWrite()
                mut got = Bytes()
                while client.Read(mut got, 65536) > 0 {
                }

                print(got.Count)
                File.Delete("changed.txt")
            }

            void | IoError All() {
                Send(9)
                Send(20)
                Send(3)
            }

            All() else {
                IoError e => print(e)
            }
            """;
        var compilation = Compile(text, WithSendFile.Value);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(compilation.Program);
            return;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "http-response-changed-file"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(["9", "invalid data: changed.txt", "9", "invalid data: changed.txt", "0"], output);
    }

    [Fact]
    public void A_build_for_windows_says_not_supported_at_the_line_of_the_program_whose_write_reaches_a_socket_through_std()
    {
        // S11 with W8: `Write` and `WriteHead` are std/'s, and their writes to the socket lie calls deep in std/; the refusal names
        // the line of the program that called the first of them
        const string text = """
            print(1)
            void | IoError Send(TcpStream s) {
                r = HttpResponse.Text(200, "hi")
                r.Write(s)
                r.WriteHead(s)
            }

            TcpStream? none = null
            if none != null {
                Send(none) else {
                    IoError e => print(e)
                }
            }
            """;
        var refused = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((4, Emitter.NotOnWindows), (diagnostic.Line, diagnostic.Message));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }
}
