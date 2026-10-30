using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The trace of the calls below the error line of an exception that ends the process (design record, section 6), which the runtime
/// writes on Linux by the platform's unwinder and its own reader of the executable's file: the frames from the one that raised to
/// <c>main</c>, innermost first, under the names a trace shows and with the lines of the line tables, the frames of the runtime and of
/// the emitted code's helpers left out, a call inlined in a release build with the tables as a frame of its own (the owner,
/// 2026-10-29, "As Rust: a frame each"), the cut after a hundred frames (the owner, 2026-10-29), <c>KURZ_TRACE</c>, and the forms of a
/// frame without a line and without a name; and the handler of a stack overflow, which writes the error line and the trace from the fault
/// on Linux. Windows writes no trace and has no such handler: there each test holds the error line alone, or the system's end.
/// </summary>
public sealed partial class TraceTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    [GeneratedRegex(@"^  at (?<name>.+?)(?: in (?<place>[^ ]+:[0-9]+))?$")]
    private static partial Regex Frame();

    [GeneratedRegex(@"^  \.\.\. [0-9]+ more frames?$")]
    private static partial Regex Cut();

    [GeneratedRegex(@"^  at <unknown> \(0x[0-9a-f]+\)$")]
    private static partial Regex Unknown();

    /// <summary>An error stream cut where its trace starts: its lines up to the last that is no frame and no cut, and the lines after it.</summary>
    internal static (string Error, IReadOnlyList<string> Trace) Split(string all)
    {
        var lines = Toolchain.Lines(all);
        var first = lines.Count;
        while (first > 0 && (Frame().IsMatch(lines[first - 1]) || Cut().IsMatch(lines[first - 1])))
        {
            first--;
        }

        return (string.Concat(lines.Take(first).Select(l => l + "\n")), lines.Skip(first).ToList());
    }

    /// <summary>
    /// The trace of a raise of the program's code: none on Windows; on Linux at least one frame, every frame with a line where the
    /// build has the line tables and none without, the first at <paramref name="place"/>, the error line's, where it has them, and the
    /// last <c>main</c>, the actor's entry, or, where the trace is cut, a hundred frames and the cut line below them.
    /// </summary>
    internal static void AssertTrace(IReadOnlyList<string> trace, string place, bool lines)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Empty(trace);
            return;
        }

        Assert.NotEmpty(trace);
        var cut = Cut().IsMatch(trace[^1]);
        var frames = trace.Take(cut ? trace.Count - 1 : trace.Count).Select(l => Frame().Match(l)).ToList();
        Assert.All(frames, m => Assert.True(m.Success && m.Groups["place"].Success == lines, $"not a frame {(lines ? "with" : "without")} a line: {m.Value}"));
        if (cut)
        {
            Assert.Equal(100, frames.Count);
        }
        else
        {
            Assert.Equal("main", frames[^1].Groups["name"].Value);
        }

        if (lines)
        {
            Assert.Equal(place, frames[0].Groups["place"].Value);
        }
    }

    /// <summary>The error stream of a crash: its error lines and, on Linux, the trace below them; Windows writes no trace.</summary>
    private static string Expected(string error, params string[] trace) =>
        error + (OperatingSystem.IsLinux() ? string.Concat(trace.Select(l => l + "\n")) : string.Empty);

    private string Build(string text, string name, bool testBuild = true, bool lines = false, params string[] grants)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), grants, toolchain.Runtime, testBuild, lines);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return toolchain.Build(compilation.Program, name);
    }

    private const string Down = "int Down(int n) {\n    if n == 0 {\n        return 1 / n\n    }\n\n    return Down(n - 1) + 1\n}\n\nprint(Down(3))\n";

    [Fact]
    public void A_raise_writes_the_frames_from_the_one_that_raised_to_main_innermost_first_with_their_lines()
    {
        var run = Toolchain.Run(Build(Down, "trace-down"), null);
        Assert.Equal(
            (1, Expected("error: divide-by-zero at program.kz:3\n", "  at Down in program.kz:3", "  at Down in program.kz:6", "  at Down in program.kz:6", "  at Down in program.kz:6", "  at main in program.kz:9")),
            (run.Code, run.Error));
    }

    [Fact]
    public void A_frame_is_named_as_the_trace_shows_it_with_the_type_arguments_of_an_instance_and_the_function_around_a_lambda()
    {
        // design record, section 6, and the owner's "Type.Method, Max<int>, std path" of 2026-10-29
        const string text = "T Pick<T>(T a, int d) {\n    print(10 / d)\n    return a\n}\n\nint Apply((int) => int f, int x) {\n    return f(x)\n}\n\n"
            + "int Twice(int x) {\n    return Apply(y => Pick(y, 0), x)\n}\n\nprint(Twice(5))\n";
        var run = Toolchain.Run(Build(text, "trace-names"), null);
        Assert.Equal(
            (1, Expected("error: divide-by-zero at program.kz:2\n", "  at Pick<int> in program.kz:2", "  at Twice.lambda in program.kz:11", "  at Apply in program.kz:7", "  at Twice in program.kz:11", "  at main in program.kz:14")),
            (run.Code, run.Error));
    }

    [Fact]
    public void A_throw_writes_its_text_and_then_the_trace()
    {
        const string text = "void Fail(int n) {\n    if n == 0 {\n        throw \"no more\"\n    }\n\n    Fail(n - 1)\n}\n\nFail(1)\n";
        var run = Toolchain.Run(Build(text, "trace-throw"), null);
        Assert.Equal(
            (1, Expected("error: thrown at program.kz:3:\nno more\n", "  at Fail in program.kz:3", "  at Fail in program.kz:6", "  at main in program.kz:9")),
            (run.Code, run.Error));
    }

    [Fact]
    public void The_frames_of_the_emitted_codes_helpers_are_left_out_as_the_runtimes_are()
    {
        // the lambda runs inside the helper that is the list's `Where`; a raise inside the runtime is shown from its caller, below
        const string text = "mut xs = List<int>()\nxs.Add(1)\nxs.Add(0)\nprint(xs.Where(x => 10 / x > 1).Count)\n";
        var run = Toolchain.Run(Build(text, "trace-helper"), null);
        Assert.Equal((1, Expected("error: divide-by-zero at program.kz:4\n", "  at main.lambda in program.kz:4", "  at main in program.kz:4")), (run.Code, run.Error));
    }

    [Fact]
    public async Task A_print_the_system_refuses_writes_the_trace_from_the_frame_of_the_print()
    {
        // `print-failed` is the runtime's raise: its frames are left out, and the trace starts at the caller of `print`
        const string text = "void Say(int n) {\n    print(n)\n}\n\nmut i = 0\nwhile i < 100000000 {\n    Say(i)\n    i = i + 1\n}\n";
        var info = new ProcessStartInfo(Build(text, "trace-print")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment.Remove("KURZ_TRACE");
        using var process = Process.Start(info)!;
        try
        {
            process.StandardOutput.Close(); // the reader is gone: the next write is refused (a closed pipe)
            var error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "the program did not end once its output was closed");
            Assert.Equal((1, Expected("error: print-failed at program.kz:2\n", "  at Say in program.kz:2", "  at main in program.kz:7")), (process.ExitCode, await error));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    [Theory]
    [InlineData(149, 100, "  ... 51 more frames")]
    [InlineData(99, 100, "  ... 1 more frame")] // the owner, 2026-10-29, "  ... 1 more frame"
    [InlineData(98, 100, null)] // a hundred frames, the whole trace: no cut
    public void A_trace_is_cut_after_a_hundred_frames_with_a_line_that_counts_the_rest(int depth, int written, string? cut)
    {
        var text = $"int Deep(int n) {{\n    if n == 0 {{\n        return 1 / n\n    }}\n\n    return Deep(n - 1) + 1\n}}\n\nprint(Deep({depth}))\n";
        var run = Toolchain.Run(Build(text, $"trace-cut-{depth}"), null);
        var (error, trace) = Split(run.Error);
        Assert.Equal((1, "error: divide-by-zero at program.kz:3\n"), (run.Code, error));
        if (!OperatingSystem.IsLinux())
        {
            Assert.Empty(trace);
            return;
        }

        // depth + 1 calls of Deep and main's frame, of which a hundred are written
        Assert.Equal(written + (cut is null ? 0 : 1), trace.Count);
        Assert.All(trace.Take(written), l => Assert.Matches(Frame(), l));
        Assert.Equal(cut ?? (depth == 98 ? "  at main in program.kz:9" : null), trace[^1]);
    }

    [Fact]
    public void A_trace_is_cut_after_a_hundred_frames_where_the_reader_could_not_map_its_pages()
    {
        // the reader maps three pages, the third the table that counts the frames past the cut; where the system refuses them, every
        // frame is `<unknown>` and each frame past the hundredth counts one line, with no table: fifty calls of Deep more count fifty
        // lines more, and the frames a walk with no names cannot leave out count too, the raise's two below Deep's and the start's
        // four above main's, so that the depth 149 has 157 frames and 57 past the hundredth where the C library's start
        // has those four, which no rule fixes: the count holds at least the calls of Deep and main past the hundredth
        var counts = new List<int>();
        foreach (var depth in new[] { 149, 199 })
        {
            var text = $"int Deep(int n) {{\n    if n == 0 {{\n        return 1 / n\n    }}\n\n    return Deep(n - 1) + 1\n}}\n\nprint(Deep({depth}))\n";
            var refused = ListTests.Edited(("runtime/linux/linux.kz", "void* got = mmap(null, 12288, 3, 34, -1, 0)", "void* got = mmap(null, 12288, 3, 0, -1, 0)"));
            var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], refused, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var run = Toolchain.Run(toolchain.Build(compilation.Program, $"trace-cut-without-pages-{depth}"), null);
            var (error, trace) = Split(run.Error);
            Assert.Equal((1, "error: divide-by-zero at program.kz:3\n"), (run.Code, error));
            if (!OperatingSystem.IsLinux())
            {
                Assert.Empty(trace);
                return;
            }

            Assert.Equal(101, trace.Count);
            Assert.All(trace.Take(100), l => Assert.Matches(Unknown(), l));
            var cut = Regex.Match(trace[^1], @"^  \.\.\. ([0-9]+) more frames$");
            Assert.True(cut.Success, trace[^1]);
            counts.Add(int.Parse(cut.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.True(counts[0] >= 51, $"{counts[0]} frames past the cut");
        Assert.Equal(50, counts[1] - counts[0]);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData(null, true)] // absent
    [InlineData("", true)]
    [InlineData("1", true)]
    [InlineData("00", true)]
    public void Kurz_trace_set_to_0_switches_the_trace_off_and_every_other_value_leaves_it_on(string? value, bool on)
    {
        var run = Toolchain.Run(Build(Down, $"trace-variable-{(value is null ? "absent" : value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + value)}"), value);
        var (error, trace) = Split(run.Error);
        Assert.Equal((1, "error: divide-by-zero at program.kz:3\n"), (run.Code, error));
        Assert.Equal(on && OperatingSystem.IsLinux() ? 5 : 0, trace.Count);
    }

    [Fact]
    public void A_release_build_names_its_frames_and_with_the_tables_writes_each_inlined_call_as_a_frame_of_its_own()
    {
        // both calls inlined into main at -O2: a release build has one frame, named by its symbol; with the tables the debug information
        // names the inlined calls, each with the line of its call (the owner, 2026-10-29, "As Rust: a frame each")
        const string text = "mut int z = 0\nint Half(int n) {\n    return 10 / n\n}\n\nint Twice(int n) {\n    return Half(n) * 2\n}\n\nprint(Twice(z))\n";
        var release = Toolchain.Run(Build(text, "trace-release", testBuild: false), null);
        Assert.Equal((1, Expected("error: divide-by-zero at program.kz:3\n", "  at main")), (release.Code, release.Error));

        var lines = Toolchain.Run(Build(text, "trace-release-lines", testBuild: false, lines: true), null);
        Assert.Equal(
            (1, Expected("error: divide-by-zero at program.kz:3\n", "  at Half in program.kz:3", "  at Twice in program.kz:7", "  at main in program.kz:10")),
            (lines.Code, lines.Error));
    }

    /// <summary>
    /// The trace of a stack overflow on Linux: a hundred frames named <paramref name="frame"/>, the frame of the call that did not fit
    /// first, and the cut line, whose count is past a thousand (the recursion fills the stack of the system's limit, megabytes).
    /// </summary>
    private static void AssertOverflowTrace(IReadOnlyList<string> trace, string frame)
    {
        Assert.Equal(101, trace.Count);
        Assert.All(trace.Take(100), l => Assert.Equal(frame, l));
        var cut = Regex.Match(trace[100], @"^  \.\.\. ([0-9]+) more frames$");
        Assert.True(cut.Success && long.Parse(cut.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 1000, trace[100]);
    }

    /// <summary>The exit code of a process Windows ends for an overflow of its stack, <c>STATUS_STACK_OVERFLOW</c>.</summary>
    private const int StackOverflowOnWindows = unchecked((int)0xC00000FD);

    [Fact]
    public void A_stack_overflow_writes_the_line_of_the_call_that_did_not_fit_and_the_trace_from_its_frame()
    {
        // design record, section 6: the root actor's overflow ends the process with the exit code 1, its error line names the call that did
        // not fit, and the trace starts at the frame that made it; Windows installs no handler, and the system ends the process there
        const string text = "int Down(int n) {\n    return Down(n + 1) + 1\n}\n\nprint(Down(0))\n";
        var built = Build(text, "overflow");
        var run = Toolchain.Run(built, null);
        if (!OperatingSystem.IsLinux())
        {
            Assert.Equal((StackOverflowOnWindows, ""), (run.Code, run.Error));
            return;
        }

        var (error, trace) = Split(run.Error);
        Assert.Equal((1, "error: stack-overflow at program.kz:2\n"), (run.Code, error));
        AssertOverflowTrace(trace, "  at Down in program.kz:2");

        // KURZ_TRACE=0: the error line alone
        var off = Toolchain.Run(built, "0");
        Assert.Equal((1, "error: stack-overflow at program.kz:2\n"), (off.Code, off.Error));
    }

    [Fact]
    public void A_stack_overflow_in_a_release_build_has_the_note_in_place_of_its_line_and_with_the_tables_its_line()
    {
        // the call's result is printed after it, so that the optimizer keeps the recursion a call and does not turn it into a loop; the
        // run with the tables is the bound of the count past the cut: where every frame reads the file, its half a million frames take
        // minutes, past the 30 seconds of a run (seen 2026-10-29: 288 seconds with the table of counted addresses bypassed)
        const string text = "int Down(int n) {\n    int r = Down(n + 1)\n    print(r)\n    return r\n}\n\nprint(Down(0))\n";
        var release = Toolchain.Run(Build(text, "overflow-release", testBuild: false), null);
        var lines = Toolchain.Run(Build(text, "overflow-release-lines", testBuild: false, lines: true), null);
        if (!OperatingSystem.IsLinux())
        {
            Assert.Equal((StackOverflowOnWindows, "", StackOverflowOnWindows, ""), (release.Code, release.Error, lines.Code, lines.Error));
            return;
        }

        var (error, trace) = Split(release.Error);
        Assert.Equal((1, "error: stack-overflow (no line tables: build with --lines)\n"), (release.Code, error));
        AssertOverflowTrace(trace, "  at Down");

        (error, trace) = Split(lines.Error);
        Assert.Equal((1, "error: stack-overflow at program.kz:2\n"), (lines.Code, error));
        AssertOverflowTrace(trace, "  at Down in program.kz:2");
    }

    [Fact]
    public void A_stack_overflow_inside_the_runtime_takes_the_line_of_the_innermost_frame_outside_it()
    {
        // `print` of an `int` runs the runtime's text of the value and the standard library's allocator below the program's frame, deeper
        // than a frame of `Down`: the fault comes inside them, the runtime's frames are passed and the standard library's are not (design
        // record, section 6, "Caller outside runtime")
        const string text = "void Down(int n) {\n    print(n)\n    Down(n + 1)\n}\n\nDown(0)\n";
        var run = Toolchain.Run(Build(text, "overflow-runtime"), null);
        if (!OperatingSystem.IsLinux())
        {
            Assert.Equal((StackOverflowOnWindows, ""), (run.Code, run.Error));
            return;
        }

        var (error, trace) = Split(run.Error);
        Assert.Equal(1, run.Code);
        var down = trace.ToList().IndexOf("  at Down in program.kz:2");
        Assert.True(down >= 0 && trace[down + 1] == "  at Down in program.kz:3", string.Join("\n", trace.Take(5)));
        var first = Frame().Match(trace[0]);
        Assert.Equal($"error: stack-overflow at {first.Groups["place"].Value}\n", error);
        Assert.All(trace.Take(down), l => Assert.Matches(@"^  at [A-Za-z.<>]+ in std[^ ]*\.kz:[0-9]+$", l));
    }

    [Fact]
    public void A_fault_outside_the_guard_page_ends_the_process_as_the_system_ends_it()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the handler of a fault on Linux: Windows installs none
        }

        // a read of the address 8: no stack overflow, so the handler sets the default back, and the fault ends the process with `SIGSEGV`
        // (11), which .NET reports as 128 plus the signal; the positive control is the overflow above, which the same handler writes
        var run = Toolchain.Run(Build("raw {\n    int* p = Ptr<int>(8)\n    print(*p)\n}\n", "fault-elsewhere", grants: "raw"), null);
        Assert.Equal((139, ""), (run.Code, run.Error));
    }

    [Fact]
    public void A_frame_whose_function_the_file_does_not_name_is_written_with_its_address_and_one_without_line_tables_with_its_name()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the forms of the file's reader on Linux: not run on Windows, which writes no trace
        }

        // the positive control: the build as it is, with its names and lines
        var built = Build(Down, "trace-file");
        Assert.Equal("  at Down in program.kz:3", Split(Toolchain.Run(built, null).Error).Trace[0]);

        // the symbol table taken out of the file (its type set to SHT_NULL): no frame has a name, and each is its address
        var nameless = Path.Combine(toolchain.Directory, "trace-nameless");
        File.Copy(built, nameless, overwrite: true);
        Patch(nameless, ".symtab", (bytes, header) => BitConverter.TryWriteBytes(bytes.AsSpan(header + 4, 4), 0));
        var unknown = Split(Toolchain.Run(nameless, null).Error).Trace;
        Assert.True(unknown.Count >= 6, string.Join("\n", unknown)); // the runtime's frames, Down's four and main's, and the C library's start
        Assert.All(unknown, l => Assert.Matches(Unknown(), l));

        // the line tables taken out (their section renamed): every frame is named and has no line
        var lineless = Path.Combine(toolchain.Directory, "trace-lineless");
        File.Copy(built, lineless, overwrite: true);
        Patch(lineless, ".debug_line", (bytes, header) => bytes[NameAt(bytes, header) + 1] = (byte)'_');
        Assert.Equal(["  at Down", "  at Down", "  at Down", "  at Down", "  at main"], Split(Toolchain.Run(lineless, null).Error).Trace);
    }

    /// <summary>Changes the header of the section named <paramref name="section"/> of an ELF file, or its name, as <paramref name="change"/> does.</summary>
    private static void Patch(string path, string section, Action<byte[], int> change)
    {
        var bytes = File.ReadAllBytes(path);
        var table = (int)BitConverter.ToInt64(bytes, 0x28);
        var size = BitConverter.ToUInt16(bytes, 0x3A);
        var count = BitConverter.ToUInt16(bytes, 0x3C);
        var found = Enumerable.Range(0, count).Select(i => table + (i * size)).Where(h => Name(bytes, NameAt(bytes, h)) == section).ToList();
        Assert.Single(found);
        change(bytes, found[0]);
        File.WriteAllBytes(path, bytes);
    }

    private static int NameAt(byte[] bytes, int header)
    {
        var table = (int)BitConverter.ToInt64(bytes, 0x28);
        var size = BitConverter.ToUInt16(bytes, 0x3A);
        var names = (int)BitConverter.ToInt64(bytes, table + (BitConverter.ToUInt16(bytes, 0x3E) * size) + 0x18);
        return names + BitConverter.ToInt32(bytes, header);
    }

    private static string Name(byte[] bytes, int at) => Encoding.ASCII.GetString(bytes, at, Array.IndexOf(bytes, (byte)0, at) - at);
}
