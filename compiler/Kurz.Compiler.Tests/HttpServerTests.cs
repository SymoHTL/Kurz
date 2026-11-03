using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions of the server's loop (W9; design record, section 12) that no corpus case pins: the text of the <c>Date</c> it writes,
/// which a case cannot hold since it is the time of the run, read through a function an edited std/ adds and held to .NET's
/// <c>"r"</c> form of the same second; and a build for Windows, whose runtime gives no socket, says <c>not supported</c> at the line
/// of the program that calls <c>Serve</c> or <c>ServeConnection</c>.
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
}
