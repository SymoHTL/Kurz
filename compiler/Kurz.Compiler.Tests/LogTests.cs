using System.Globalization;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The lines of a log call on standard error (chapter 17), which no corpus case can hold, its header holding standard output alone: the
/// layout of a line, its category, the levels and the default, a call site that is off, <c>KURZ_LOG</c>'s entries and its refusals, and
/// a write the system refuses, and the error line of a crash after a line. Each program is built for Linux and started by <c>/bin/sh</c>
/// after a shell command that sets its environment or its standard error; on Windows each test holds the build's refusal of a log call.
/// </summary>
public sealed partial class LogTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    [GeneratedRegex(@"^(?<time>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z) (?<rest>.*)$")]
    private static partial Regex Stamped();

    /// <summary>
    /// The program built and run by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="setup"/>, with <c>KURZ_LOG</c> taken away
    /// and <c>KURZ_TRACE=0</c>: its exit code, its lines and its error stream; null on Windows, where the build is held to its refusal
    /// of a log call, and a program with none builds and is not run.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup = "")
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Equal(text.Contains("Log.", StringComparison.Ordinal), compilation.Program is null);
            return null;
        }

        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup}exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0", ["KURZ_LOG"] = null });
        return (code, Toolchain.Lines(output), error);
    }

    /// <summary>The lines of an error stream with the time of each cut off, each time read as RFC 3339 with milliseconds in UTC and held to the run's.</summary>
    private static List<string> Unstamped(string error, DateTime before, DateTime after)
    {
        var lines = new List<string>();
        foreach (var line in Toolchain.Lines(error))
        {
            var match = Stamped().Match(line);
            Assert.True(match.Success, $"a line without a time: {line}");
            var time = DateTime.ParseExact(match.Groups["time"].Value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            Assert.InRange(time, before.AddSeconds(-1), after.AddSeconds(1));
            lines.Add(match.Groups["rest"].Value);
        }

        return lines;
    }

    /// <summary>The lines of a run's error stream, as <see cref="Unstamped"/> cuts them, with its exit code and its output.</summary>
    private (int Code, IReadOnlyList<string> Output, List<string> Lines)? Logged(string text, string name, string setup = "")
    {
        var before = DateTime.UtcNow;
        if (Ran(text, name, setup) is not { } run)
        {
            return null;
        }

        return (run.Code, run.Output, Unstamped(run.Error, before, DateTime.UtcNow));
    }

    [Fact]
    public void A_line_holds_the_time_the_level_the_category_the_message_and_each_field_a_string_in_quotes()
    {
        const string text = "id = 17\nwho = \"a \\\"b\\\" \\\\ c\"\nLog.Information(\"Paid {id} by { who }\")\nprint(\"done\")\n";
        if (Logged(text, "log-line") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["done"], run.Output);
            Assert.Equal(["Information program: Paid 17 by a \"b\" \\\\ c id=17 who=\"a \\\"b\\\" \\\\ c\""], run.Lines); // a `\` in the message as `\\`
        }
    }

    [Fact]
    public void A_line_feed_and_a_carriage_return_in_the_message_or_a_field_are_written_as_escapes_on_one_line_a_backslash_in_the_message_as_two()
    {
        // the message ends with a line feed, then `\` and `n`: written `\n` and `\\n`, so that the escape reads back
        const string text = "note = \"one\\ntwo\\rthree\"\nLog.Warning(\"note {note} {7}\\n\\\\n\")\n";
        if (Logged(text, "log-breaks") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["Warning program: note one\\ntwo\\rthree 7\\n\\\\n note=\"one\\ntwo\\rthree\" 7=7"], run.Lines);
        }
    }

    [Fact]
    public void The_category_is_the_class_whose_method_holds_the_call_and_outside_a_class_the_files_name()
    {
        const string text = "class Orders {\n    pub void Pay() {\n        Log.Information(\"in a method\")\n    }\n}\n\nvoid Top() {\n    Log.Information(\"in a function\")\n}\n\no = Orders()\no.Pay()\nTop()\nLog.Information(\"at the top\")\n";
        if (Logged(text, "log-category") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["Information Orders: in a method", "Information program: in a function", "Information program: at the top"], run.Lines);
        }
    }

    [Fact]
    public void A_call_in_a_lambda_takes_the_category_of_the_code_that_holds_it_and_a_member_named_Log_leaves_the_log_call()
    {
        // P6 for a lambda in a method and at the top, and P1 for a method named `Log` of the type whose method logs
        const string text = "class Orders {\n    pub void Log() {\n        print(\"own\")\n    }\n\n    pub void Pay() {\n        f = () => Log.Information(\"in a lambda\")\n        f()\n        Log.Information(\"in a method\")\n    }\n}\n\no = Orders()\no.Log()\no.Pay()\ng = () => Log.Information(\"at the top\")\ng()\n";
        if (Logged(text, "log-lambda") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["own"], run.Output);
            Assert.Equal(["Information Orders: in a lambda", "Information Orders: in a method", "Information program: at the top"], run.Lines);
        }
    }

    [Fact]
    public void A_call_in_a_fields_default_or_a_property_takes_the_name_of_its_type()
    {
        // P6, the owner's answer M11: a log call gives nothing, so it stands in a field's default or a property inside a lambda
        const string text = "class Orders {\n    () => void report = () => Log.Information(\"in a field's default\")\n\n    pub () => void Notice => () => Log.Information(\"in a property\")\n\n    pub void Pay() {\n        report()\n        n = Notice\n        n()\n    }\n}\n\no = Orders()\no.Pay()\n";
        if (Logged(text, "log-member-category") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["Information Orders: in a field's default", "Information Orders: in a property"], run.Lines);
        }
    }

    [Fact]
    public void Log_alone_reaches_a_member_named_Log_and_a_call_of_a_level_of_Log_stays_a_log_call()
    {
        // P1: a parameter of a primary constructor named `Log`, whose type has an `Information` of its own
        const string text = "class Sink {\n    pub void Information(string s) {\n        print(s)\n    }\n}\n\nclass Orders(Sink Log) {\n    pub void Pay() {\n        Log.Information(\"a log call\")\n        this.Log.Information(\"through this\")\n        s = Log\n        s.Information(\"by its name alone\")\n    }\n}\n\no = Orders(Sink())\no.Pay()\n";
        if (Logged(text, "log-member-named-log") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["through this", "by its name alone"], run.Output);
            Assert.Equal(["Information Orders: a log call"], run.Lines);
        }
    }

    [Fact]
    public void In_a_property_or_a_fields_default_of_a_type_with_a_member_named_Log_a_level_of_Log_is_a_log_call_and_Log_alone_the_member()
    {
        // P1 outside the type's methods: a parameter of a primary constructor named `Log`, whose type has an `Information` of its own,
        // beside a log call in a field's default and in a property, each inside a lambda, and a property that gives the member
        const string text = "class Sink {\n    pub void Information(string s) {\n        print(s)\n    }\n}\n\nclass Orders(Sink Log) {\n    () => void report = () => Log.Information(\"in a field's default\")\n\n    pub () => void Notice => () => Log.Information(\"in a property\")\n\n    pub Sink Kept => Log\n\n    pub void Pay() {\n        report()\n        n = Notice\n        n()\n        k = Kept\n        k.Information(\"the member through a property\")\n    }\n}\n\no = Orders(Sink())\no.Pay()\n";
        if (Logged(text, "log-member-outside-a-method") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["the member through a property"], run.Output);
            Assert.Equal(["Information Orders: in a field's default", "Information Orders: in a property"], run.Lines);
        }
    }

    [Fact]
    public void With_nothing_set_a_call_site_is_on_from_information_and_each_level_writes_its_name()
    {
        const string text = "Log.Trace(\"t\")\nLog.Debug(\"d\")\nLog.Information(\"i\")\nLog.Warning(\"w\")\nLog.Error(\"e\")\nLog.Critical(\"c\")\n";
        if (Logged(text, "log-levels") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["Information program: i", "Warning program: w", "Error program: e", "Critical program: c"], run.Lines);
        }

        if (Logged(text, "log-levels-trace", "export KURZ_LOG=Trace; ") is { } all)
        {
            Assert.Equal(["Trace program: t", "Debug program: d", "Information program: i", "Warning program: w", "Error program: e", "Critical program: c"], all.Lines);
        }
    }

    [Fact]
    public void A_call_site_that_is_off_runs_no_hole_and_one_that_is_on_runs_each_hole_once_each_time_it_is_reached()
    {
        const string text = "int Counted() {\n    print(\"counted\")\n    return 1\n}\n\nmut i = 0\nwhile i < 2 {\n    Log.Debug(\"debug {Counted()}\")\n    Log.Information(\"information {Counted()} {i}\")\n    i = i + 1\n}\n";
        if (Logged(text, "log-off") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["counted", "counted"], run.Output);
            Assert.Equal(["Information program: information 1 0 Counted()=1 i=0", "Information program: information 1 1 Counted()=1 i=1"], run.Lines);
        }

        if (Logged(text, "log-off-warning", "export KURZ_LOG=Warning; ") is { } off)
        {
            Assert.Equal(0, off.Code);
            Assert.Empty(off.Output);
            Assert.Empty(off.Lines);
        }

        if (Logged(text, "log-off-debug", "export KURZ_LOG=Debug; ") is { } on)
        {
            Assert.Equal(4, on.Output.Count);
            Assert.Equal(["Debug program: debug 1 Counted()=1", "Information program: information 1 0 Counted()=1 i=0", "Debug program: debug 1 Counted()=1", "Information program: information 1 1 Counted()=1 i=1"], on.Lines);
        }
    }

    [Fact]
    public void A_log_call_in_a_generic_function_is_one_call_site_for_every_type_it_is_emitted_for()
    {
        // the body is emitted once for each type argument, the call's node the same in each: one global for the site
        const string text = "void Show<T>(T value, int n) {\n    Log.Information(\"shown {n}\")\n}\n\nShow(1, 1)\nShow(\"one\", 2)\nShow(true, 3)\n";
        if (Logged(text, "log-generic") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["Information program: shown 1 n=1", "Information program: shown 2 n=2", "Information program: shown 3 n=3"], run.Lines);
        }
    }

    private const string Shop = "class ShopOrders {\n    pub void Run() {\n        Log.Debug(\"orders debug\")\n        Log.Information(\"orders information\")\n    }\n}\n\nclass ShopItems {\n    pub void Run() {\n        Log.Warning(\"items warning\")\n        Log.Error(\"items error\")\n    }\n}\n\norders = ShopOrders()\norders.Run()\nitems = ShopItems()\nitems.Run()\nLog.Information(\"top information\")\nLog.Warning(\"top warning\")\n";

    [Theory]
    [InlineData("", "orders information|items warning|items error|top information|top warning")] // as none
    [InlineData(" wArNiNg , shop=Error,SHOPORDERS=Debug,,", "orders debug|orders information|items error|top warning")] // the longest prefix, no regard to case, spaces and empty entries
    [InlineData("ShopOrders=Debug,ShopOrders=Critical", "items warning|items error|top information|top warning")] // the last of two alike
    [InlineData("Shop=Debug,ShopOrders=Warning", "items warning|items error|top information|top warning")] // the longer prefix, written after the shorter
    [InlineData("ShopOrders=Warning,Shop=Debug", "items warning|items error|top information|top warning")] // the longer prefix, written before the shorter
    [InlineData("None,ShopItems=Error", "items error")] // `None` above every level
    [InlineData("Debug,Error", "items error")] // the last bare level
    public void KURZ_LOG_sets_the_level_of_a_category_by_its_longest_prefix_else_by_its_bare_level(string filter, string expected)
    {
        if (Logged(Shop, $"log-filter-{Math.Abs(filter.GetHashCode(StringComparison.Ordinal))}", $"export KURZ_LOG='{filter}'; ") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(expected.Split('|'), run.Lines.Select(l => l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..]));
        }
    }

    [Theory]
    [InlineData("export KURZ_LOG=Verbose; ", "error: log-filter (Verbose)\n")]
    [InlineData("export KURZ_LOG='Warning, Shop=Loud ,Debug'; ", "error: log-filter (Shop=Loud)\n")]
    [InlineData("export KURZ_LOG='=Debug'; ", "error: log-filter (=Debug)\n")]
    [InlineData("export KURZ_LOG='Shop = Debug'; ", "error: log-filter (Shop = Debug)\n")]
    public void A_KURZ_LOG_with_an_entry_that_is_no_level_ends_the_start_with_log_filter_naming_the_entry(string setup, string error) =>
        Refused(setup, error);

    [Fact]
    public void A_KURZ_LOG_that_is_not_UTF_8_ends_the_start_with_log_filter_not_unicode() =>
        Refused("export KURZ_LOG=\"$(printf 'Debug\\377')\"; ", "error: log-filter (not unicode)\n");

    /// <summary>
    /// A program with a log call, started after <paramref name="setup"/>, ends with the exit code 1 and <paramref name="error"/> alone, while
    /// the same program under a filter of levels and a program with no log call under <paramref name="setup"/> run.
    /// </summary>
    private void Refused(string setup, string error)
    {
        const string text = "print(\"start\")\nLog.Information(\"x\")\n";
        if (Ran(text, $"log-refused-{Math.Abs(setup.GetHashCode(StringComparison.Ordinal))}", setup) is { } run)
        {
            Assert.Equal((1, error), (run.Code, run.Error));
            Assert.Empty(run.Output);
        }

        // the positive controls: the same program with a filter of levels, and a program with no log call, which reads no `KURZ_LOG`
        if (Ran(text, "log-refused-control", "export KURZ_LOG=' Warning ,Shop=Debug'; ") is { } control)
        {
            Assert.Equal((0, ""), (control.Code, control.Error));
            Assert.Equal(["start"], control.Output);
        }

        if (Ran("print(\"start\")\n", "log-refused-none", setup) is { } none)
        {
            Assert.Equal((0, ""), (none.Code, none.Error));
            Assert.Equal(["start"], none.Output);
        }
    }

    [Fact]
    public void A_crash_after_a_line_writes_its_error_line_below_the_line_as_it_stands()
    {
        // the line is written at its call, so that the crash line of section 6 follows it, unchanged
        const string text = "n = 0\nLog.Information(\"before\")\nprint(10 / n)\n";
        var before = DateTime.UtcNow;
        if (Ran(text, "log-crash") is { } run)
        {
            Assert.Equal(1, run.Code);
            var lines = Toolchain.Lines(run.Error);
            Assert.Equal(2, lines.Count);
            Assert.Equal(["Information program: before"], Unstamped(lines[0] + "\n", before, DateTime.UtcNow));
            Assert.Equal("error: divide-by-zero at program.kz:3", lines[1]);
        }
    }

    [Fact]
    public void A_build_for_windows_says_not_supported_at_a_log_call()
    {
        const string text = "print(1)\nLog.Information(\"x\")\n";
        var refused = Builder.Compile(SourceText.FromString("program.kz", text), [], Builder.CheckRuntime(Target.Windows), testBuild: true);
        Assert.Null(refused.Program);
        var diagnostic = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 2), (diagnostic.Kind, diagnostic.Line)); // E2: the id and the line, not the wording
        Assert.NotNull(Builder.Compile(SourceText.FromString("program.kz", text), [], Builder.CheckRuntime(Target.Linux), testBuild: true).Program); // the positive control
    }

    [Theory]
    [InlineData("Log.Information(\"a\")\n", "", "Warning Kurz.Log: dropped 1 line")]
    [InlineData("Log.Information(\"a\")\nLog.Error(\"b\")\n", "", "Warning Kurz.Log: dropped 2 lines")]
    [InlineData("Log.Information(\"a\")\n", "Kurz.Log=None", "Warning Kurz.Log: dropped 1 line")] // whatever level the filter gives `Kurz.Log`
    public void A_write_the_system_refuses_raises_nothing_and_the_next_line_written_says_how_many_were_dropped(string dropped, string filter, string line)
    {
        // standard error appends to a file already past the limit of a file's size, so that a write fails with EFBIG and no signal;
        // the program empties the file, and the next line is written
        var text = "void | IoError Empty() {\n    File.WriteAll(\"log.txt\", \"\".Bytes)\n}\n\n" + dropped + "Empty() else {\n    IoError e => print(e)\n}\n\nLog.Information(\"c\")\nLog.Information(\"d\")\nprint(\"done\")\n";
        var name = $"log-dropped-{dropped.Length}-{filter.Length}";
        var before = DateTime.UtcNow;
        if (Ran(text, name, $"export KURZ_LOG='{filter}'; printf '%0600d' 0 > log.txt; trap '' XFSZ; ulimit -f 1; exec 2>>log.txt; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["done"], run.Output);
            var log = File.ReadAllText(Path.Combine(toolchain.Directory, name, "log.txt"));
            Assert.Equal([line, "Information program: c", "Information program: d"], Unstamped(log, before, DateTime.UtcNow));
        }
    }

    [Fact]
    public void Lines_dropped_after_the_last_line_written_are_named_by_no_line()
    {
        // as in the theory above, `a` is refused and the program empties the file, where a line after it would be written; the program
        // ends with no line after it, and nothing is written at its exit
        const string text = "void | IoError Empty() {\n    File.WriteAll(\"log.txt\", \"\".Bytes)\n}\n\nLog.Information(\"a\")\nEmpty() else {\n    IoError e => print(e)\n}\n\nprint(\"done\")\n";
        if (Ran(text, "log-dropped-last", "printf '%0600d' 0 > log.txt; trap '' XFSZ; ulimit -f 1; exec 2>>log.txt; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["done"], run.Output);
            Assert.Equal("", File.ReadAllText(Path.Combine(toolchain.Directory, "log-dropped-last", "log.txt")));
        }
    }

    [Fact]
    public void A_line_the_system_takes_in_part_is_dropped_once_and_a_line_feed_ends_it_before_the_next_line()
    {
        // standard error appends to a file under a limit of 512 bytes: `a` is refused at 600 bytes; at 430, the 58 bytes of the line of
        // the dropped lines fit and `b`, 48 bytes, is cut after 24; the program empties the file, and `c` follows a line feed and the
        // line of the one dropped line, `b`, since the line before it was taken whole
        var text = "void | IoError Put(string text) {\n    File.WriteAll(\"log.txt\", text.Bytes)\n}\n\n" +
            "Log.Information(\"a\")\nPut(\"" + new string('0', 430) + "\") else {\n    IoError e => print(e)\n}\n\n" +
            "Log.Information(\"b\")\nPut(\"\") else {\n    IoError e => print(e)\n}\n\nLog.Information(\"c\")\nprint(\"done\")\n";
        var before = DateTime.UtcNow;
        if (Ran(text, "log-cut", "printf '%0600d' 0 > log.txt; trap '' XFSZ; ulimit -f 1; exec 2>>log.txt; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["done"], run.Output);
            var log = File.ReadAllText(Path.Combine(toolchain.Directory, "log-cut", "log.txt"));
            Assert.StartsWith("\n", log, StringComparison.Ordinal);
            Assert.Equal(["Warning Kurz.Log: dropped 1 line", "Information program: c"], Unstamped(log[1..], before, DateTime.UtcNow));
        }
    }

    [Fact]
    public void A_case_runs_with_no_KURZ_LOG_of_the_environment_the_suite_runs_in()
    {
        // the corpus's runner takes the variable away (`Toolchain.Run`), so that a case with a log call runs at P3's default level; a
        // program another suite starts meanwhile inherits the variable, which only a program with a log call reads, and this suite's take
        // it away; on Windows no program with a log call builds
        if (Builder.HostTarget == Target.Windows)
        {
            Assert.Null(Builder.Compile(SourceText.FromString("program.kz", "Log.Information(\"x\")\n"), [], toolchain.Runtime, testBuild: true).Program);
            return;
        }

        var was = Environment.GetEnvironmentVariable("KURZ_LOG");
        Environment.SetEnvironmentVariable("KURZ_LOG", "Trace");
        try
        {
            Assert.Contains("KURZ_LOG=Trace", Processes.Capture("/usr/bin/env", [], null, TimeSpan.FromSeconds(30)).Output.Split('\n')); // the positive control
            Assert.DoesNotContain(Toolchain.Run("/usr/bin/env", null).Output, line => line.StartsWith("KURZ_LOG=", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KURZ_LOG", was);
        }
    }
}
