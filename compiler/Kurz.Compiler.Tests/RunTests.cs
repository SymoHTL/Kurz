using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Corpus;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The clang of the pinned LLVM major, found as the command finds it, and the runtime of the machine's target, checked once, for
/// every program the suite builds. A machine without that clang fails every test that needs it, with the command's lines on the
/// search: a build that cannot run the corpus is red, never a pass by omission.
/// </summary>
public sealed class Toolchain : IDisposable
{
    public Toolchain()
    {
        var search = Compiler.Build.Clang.Find();
        Clang = search.Found ?? throw new InvalidOperationException(Command.Failure(search));
        var target = Builder.HostTarget ?? throw new InvalidOperationException("the runtime has no directory for this system");
        Runtime = Builder.CheckRuntime(target);
        x64 = new(() => Builder.CheckRuntime(target, Arch.X64));
        arm64 = new(() => Builder.CheckRuntime(target, Arch.Arm64));
        Directory = System.IO.Directory.CreateTempSubdirectory("kurz-tests-").FullName;
    }

    public Clang Clang { get; }

    /// <summary>The runtime of the target of this machine, which every program the suite builds is emitted with.</summary>
    public CheckedRuntime Runtime { get; }

    private readonly Lazy<CheckedRuntime> x64;
    private readonly Lazy<CheckedRuntime> arm64;

    /// <summary>
    /// The runtime of the target of this machine for the architecture of <paramref name="cpu"/>, which a program emitted for that CPU reads
    /// (design record, section 2, round 20, "Dir per arch"): the IR of a build for 64-bit Arm on an x86-64 machine.
    /// </summary>
    public CheckedRuntime For(Cpu cpu) => cpu.X86 ? x64.Value : arm64.Value;

    /// <summary>The directory of this run's builds, removed with the fixture.</summary>
    public string Directory { get; }

    /// <summary>Builds the program into an executable under a directory of its own; the executable's path.</summary>
    public string Build(EmittedProgram program, string name)
    {
        var directory = Path.Combine(Directory, name);
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "program.exe" : "program");
        Builder.Build(program, Clang, directory, executable);
        return executable;
    }

    /// <summary>Whether a built executable carries line tables: on Linux the DWARF section <c>.debug_line</c> in it, on Windows a PDB beside it.</summary>
    public static bool HasLineTables(string executable) =>
        OperatingSystem.IsWindows() ? File.Exists(Path.ChangeExtension(executable, ".pdb")) : Sections(executable).Contains(".debug_line");

    /// <summary>The names of the sections of an ELF file, little-endian and of 64 bits, read from its table of section headers.</summary>
    private static List<string> Sections(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var table = (int)BitConverter.ToInt64(bytes, 0x28);
        var size = BitConverter.ToUInt16(bytes, 0x3A);
        var names = (int)BitConverter.ToInt64(bytes, table + (BitConverter.ToUInt16(bytes, 0x3E) * size) + 0x18);
        return Enumerable.Range(0, BitConverter.ToUInt16(bytes, 0x3C)).Select(i =>
        {
            var start = names + BitConverter.ToInt32(bytes, table + (i * size));
            return System.Text.Encoding.ASCII.GetString(bytes, start, Array.IndexOf(bytes, (byte)0, start) - start);
        }).ToList();
    }

    /// <summary>
    /// Runs a built program with nothing on its input and the trace of the calls switched off, <c>KURZ_TRACE=0</c> (design record,
    /// section 6), so that an error stream holds the error line alone, in the directory of its build, so that a file it makes with a
    /// relative path (S2) lands there; its exit code, what it wrote on standard output as lines, and what it wrote on standard error.
    /// </summary>
    public static (int Code, IReadOnlyList<string> Output, string Error) Run(string executable) => Run(executable, "0");

    /// <summary>
    /// Runs a built program as <see cref="Run(string)"/> does, with <c>KURZ_TRACE</c> set to <paramref name="trace"/>, or taken away where
    /// it is null, and <c>KURZ_LOG</c> taken away, so that a case with a log call runs at the default level (P3) wherever the suite runs.
    /// </summary>
    public static (int Code, IReadOnlyList<string> Output, string Error) Run(string executable, string? trace)
    {
        var (code, output, error) = Processes.Capture(executable, [], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = trace, ["KURZ_LOG"] = null });
        return (code, Lines(output), error);
    }

    /// <summary>The lines of a program's output: each ends in a line feed, so the text's last piece is empty and dropped.</summary>
    public static IReadOnlyList<string> Lines(string text)
    {
        var pieces = text.Split('\n');
        return pieces[^1].Length == 0 ? pieces[..^1] : pieces;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // a directory the system still holds is left to it
        }
    }
}

/// <summary>
/// Every corpus case that expects a run (<c>output</c> or <c>throws</c>), built by the emitter and the pinned clang and run on this
/// machine, an x86-64 one, as the gates image and the Windows job are: on a 64-bit Arm machine the emitter says <c>not supported</c> to
/// an <c>asm</c> and a <c>naked</c> function (R11, R12), whose cases this runs. A case the checker reads in full and the emitter lays out prints exactly the header's lines on standard output; an
/// <c>output</c> case then exits with 0 and writes nothing on standard error but the log line of an actor's crash, a line in P7's layout
/// at <c>Error</c> with the category <c>Kurz.Actor</c>, which the runner leaves out of standard error in a case whose <c>rules:</c> line
/// names a rule of a crash, U19 to U34, and in no other, so that such a case can show an actor's restart, and every other line, the log
/// line of a crash in another case among them, turns a case red (E3, U24; the taken points of comment 6899225864, assumed there for a case
/// that expects an exception, and the case of a crash alone assumed there too, the fix of T3 of the reading of issue 39, comment
/// 6926929175, which the answer of 2026-11-16 on pull request 554, comment 6926986795, gave to the change that follows it); a
/// <c>throws</c> case exits with 1, and its error stream
/// is the one line <c>error: &lt;id&gt; at &lt;file&gt;:&lt;n&gt;</c> of the design record's section 6, with the header's id and line and
/// the case's path under <c>corpus/</c> as the file, or for <c>thrown</c> that line ending in a colon with the thrown text below it, and
/// for <c>not-char-boundary</c> that line ending in a colon with the position of the cut below it (X10). A case that expects an error the
/// build raises once clang refuses the program (<see cref="CorpusTests.BuildIds"/>) is built, and its build ends with that error alone, at the
/// header's line; it is no case run.
/// A case the checker says <c>not supported</c> to is held by the checker's theory in <see cref="CorpusTests"/> and is not built here; a
/// case the checker reads and the emitter does not lay out is in <see cref="NotEmitted"/>, with the construct the emitter names; and a
/// case it lays out whose <c>system:</c> line names another system than this machine's is built and not run (<see cref="NotRun"/>,
/// chapter 0). <see cref="NotEmitted"/> holds no stale entry, and a <c>system:</c> line stands only on a case that expects a run and
/// that the emitter lays out. A case is built with the grants of its <c>allow:</c> line, and as its <c>build:</c> line says, else
/// as the test build, the default. A floor holds the count of cases run. Every case that is run runs a second time under an allocator that makes
/// every block it frees inaccessible (<see cref="ListTests.Fenced"/>), so that a block the counting frees while a value still holds
/// it ends that run with the system's signal; a case that allocator cannot hold is in <see cref="NotFenced"/>, with why, and is
/// built under it and not run.
/// </summary>
public sealed partial class RunTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    /// <summary>E3, U24: the log line of an actor's crash on standard error, a line in P7's layout at <c>Error</c> with the category <c>Kurz.Actor</c>.</summary>
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z Error Kurz\.Actor: [^\n]*\n", RegexOptions.Multiline)]
    private static partial Regex CrashLine();

    /// <summary>
    /// E3: what a case wrote on standard error, <paramref name="written"/>, with the log line of each crash left out where its <c>rules:</c>
    /// line, <paramref name="rules"/>, names a rule of a crash, U19 to U34 or the rules of <c>attempt</c>, U46 to U50, and whole in every
    /// other case, where such a line is a crash the case does not show (E3, assumed there: the fix of T3 of the reading of issue 39, comment
    /// 6926929175, which named U19 to U24, the rules of a crash at that head, the policy and its block, U25 to U34, and <c>attempt</c>, U46
    /// to U50, having come since).
    /// </summary>
    internal static string LeftOut(string written, IReadOnlyList<string> rules) =>
        rules.Any(r => r[0] == 'U' && int.Parse(r[1..], System.Globalization.CultureInfo.InvariantCulture) is (>= 19 and <= 34) or (>= 46 and <= 50))
            ? CrashLine().Replace(written, string.Empty) : written;

    /// <summary>The cases built and run: fewer means the emitter refuses what it built before; Windows does not run the cases whose <c>system:</c> line names Linux, the stack overflows', nor build the calls on files and sockets, the reads of the command line, the environment and the clock, the calls on processes, the log calls, the reads of standard input and running a program. The number for Linux is the count measured with no margin in an image built locally from <c>ci/Dockerfile</c>, whose clang is the point release of the day it was built and not always the one of the image the gates job pulls by its digest (knowledge/the-gates-job-runs-in-its-image.md), so that one case that stops being built there turns it red, and a change that adds a case to the run raises it (#156); a green gates job shows only that its own image counts at least this number, and a count in that image is one run of the job on a pushed head with the number at 999. The number for Windows is measured by one run of the Windows job with the number set to 999, which fails and prints the count; between such runs it is a bound, the last count plus the cases a change since adds that run there (knowledge/the-run-floor-is-measured-not-merged.md), so a case that stops being built there may leave it green until the next count: it is a weaker gate than the Linux number.</summary>
    private static readonly int RunFloor = OperatingSystem.IsWindows() ? 667 : 810;

    /// <summary>The cases of the calls on files and sockets that expect a run, which a build for Windows says <c>not supported</c> to (S11).</summary>
    private static readonly string[] FileCases =
    [
        "system/bad-address.kz", "system/bind-name.kz", "system/close-twice.kz", "system/connect-refused.kz", "system/delete-rename-size.kz",
        "system/directories.kz", "system/open-missing.kz", "system/path-with-nul.kz", "system/read-appends.kz", "system/read-negative-max.kz",
        "system/tcp-echo.kz", "system/whole-file.kz", "system/write-read.kz", "system/file-modified.kz", "system/read-timeout.kz",
        "system/write-timeout.kz", "system/connect-timeout.kz", "system/timeout-refused.kz", "system/accept-timeout.kz",
        "system/lines-break-read.kz", "system/lines-empty.kz", "system/lines-invalid-utf8.kz", "system/lines-not-open-to-read.kz",
        "system/lines-propagate.kz", "system/lines-read.kz", "system/setter-after-close.kz", "system/lines-keeps-its-file.kz", "system/shutdown-write.kz",
        "system/read-buffer-fill.kz", "system/read-buffer-straight.kz", "system/read-buffer-zero.kz", "system/read-buffer-failed.kz",
        "system/read-buffer-close.kz", "system/fill-consume.kz", "system/fill-held.kz", "system/fill-head.kz", "system/fill-timeout.kz",
        "system/consume-past.kz", "system/consume-negative.kz", "system/fill-closed.kz", "system/read-buffer-zero-held.kz",
        "system/read-buffer-limit.kz", "system/read-buffer-second.kz", "system/read-buffer-close-stream.kz", "system/consume-closed.kz",
        "system/consume-closed-negative.kz", "system/read-buffer-zero-closed.kz",
        "system/seek-read.kz", "system/seek-write.kz", "system/seek-fill.kz", "system/seek-negative.kz", "system/seek-stream.kz",
        "system/seek-closed.kz",
        "http/request-read.kz", "http/request-refused.kz", "http/request-refused-unsupported.kz", "http/request-body.kz",
        "http/request-body-errors.kz", "http/request-continue.kz", "http/request-limits.kz", "http/request-trailer.kz",
        "http/response-write.kz", "http/response-head.kz", "http/response-bodiless.kz", "http/response-unwritable.kz",
        "http/response-transfer-encoding.kz", "http/response-large.kz",
        "http/serve-keep-alive.kz", "http/serve-close.kz", "http/serve-refused.kz", "http/serve-head.kz", "http/serve-drain.kz",
        "http/serve-limits.kz", "http/serve-accept.kz", "http/serve-timeouts.kz", "http/serve-close-option.kz",
        "http/response-stream.kz", "http/serve-stream.kz", "http/serve-stream-timeouts.kz", "http/stream-handler-no-cycle.kz",
        "http/serve-stream-length.kz",
        "http/client-get.kz", "http/client-pool.kz", "http/client-errors.kz", "http/client-response.kz", "http/client-malformed.kz",
        "http/client-limits.kz", "http/client-redirects.kz", "http/client-timeout.kz", "http/client-timeout-body.kz",
        "http/http-query.kz",
        "system/command-env-dir.kz",
    ];

    /// <summary>The cases of the reads of the command line, the environment and the clock that expect a run, which a build for Windows says <c>not supported</c> to (S11), the cases of the deadlines that hold their thread by the clock among them (U36 to U45, U49); a case of a deadline that reads no clock, a <c>within</c> that bounds a call with no such hold, is none of them, and a build for Windows runs it.</summary>
    private static readonly string[] ProcessCases =
    [
        "system/arguments.kz", "system/args-parameter.kz", "system/args-in-lambda.kz", "system/args-new-list.kz", "system/clock.kz", "system/environment-missing.kz",
        "system/monotonic-clock.kz", "system/elapsed-order.kz", "functions/top-level-return.kz", "functions/top-level-return-end.kz",
        "actors/timeout-block.kz", "actors/timeout-callee-crash.kz", "actors/timeout-capped.kz", "actors/timeout-carried.kz", "actors/timeout-crashed.kz",
        "actors/timeout-crashed-early.kz", "actors/timeout-default.kz", "actors/timeout-send.kz", "actors/within-clauses.kz",
        "actors/attempt-deadline.kz", "actors/attempt-dropped.kz", "actors/attempt-dropped-queued.kz",
        "actors/timeout-chain-nested.kz", "actors/timeout-chain-left.kz", "actors/timeout-gone-queued.kz",
    ];

    /// <summary>The cases of log calls that expect a run, which a build for Windows says <c>not supported</c> to (S11, P6).</summary>
    private static readonly string[] LogCases = ["logging/off-runs-no-hole.kz"];

    /// <summary>The cases of standard input that expect a run, which a build for Windows says <c>not supported</c> to (S11, S22).</summary>
    private static readonly string[] StdinCases = ["system/stdin-empty.kz", "system/stdin-lines.kz"];

    /// <summary>
    /// The cases of running a program that expect a run, which a build for Windows says <c>not supported</c> to (S11, S17 to S21); a case
    /// whose first such call is on a file, <c>system/command-env-dir.kz</c>, is among <see cref="FileCases"/>, since the build stops there.
    /// </summary>
    private static readonly string[] CommandCases =
    [
        "system/command-exit-code.kz", "system/command-input.kz", "system/command-input-stdin.kz", "system/command-not-found.kz", "system/command-nul.kz", "system/command-nul-replaced.kz",
        "system/command-output.kz", "system/command-reuse.kz", "system/command-signal.kz", "system/command-status.kz", "system/command-timeout.kz",
        "system/command-words.kz", "system/spawn-input.kz", "system/spawn-kill.kz", "system/spawn-pipes.kz", "system/status-input.kz", "system/status-piped.kz",
    ];

    /// <summary>Cases the checker reads in full and the emitter does not lay out, each with what the emitter says.</summary>
    private static readonly Dictionary<string, string> NotEmitted = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["types/string-chars.kz"] = "`s.Chars` as a list (T15)",
        ["values/text-more.kz"] = "`s.Chars` as a list (T15)",
        ["outcomes/throw-in-text-of-instantiation.kz"] = "a generic data type in a relation of inheritance, `Sub<Loud>` (D6, T19)",
    }.Concat(OperatingSystem.IsWindows() ? FileCases.Select(c => KeyValuePair.Create(c, Emitter.NotOnWindows)) : [])
        .Concat(OperatingSystem.IsWindows() ? ProcessCases.Select(c => KeyValuePair.Create(c, Emitter.ProcessNotOnWindows)) : [])
        .Concat(OperatingSystem.IsWindows() ? LogCases.Select(c => KeyValuePair.Create(c, Emitter.LogNotOnWindows)) : [])
        .Concat(OperatingSystem.IsWindows() ? StdinCases.Select(c => KeyValuePair.Create(c, Emitter.StdinNotOnWindows)) : [])
        .Concat(OperatingSystem.IsWindows() ? CommandCases.Select(c => KeyValuePair.Create(c, Emitter.CommandNotOnWindows)) : []).ToDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Whether the case whose header is <paramref name="header"/> is built and not run on this machine: its <c>system:</c> line names a
    /// system other than this machine's (chapter 0), as the cases of a stack overflow name Linux, whose raise is the handler's of a fault
    /// (design record, section 6), which the runtime installs on Linux alone, an actor's overflow ending the process on Windows (U19,
    /// compiler/README.md).
    /// </summary>
    private static bool NotRun(CaseHeader header) => NotRun(header, Host);

    /// <summary>Whether the case is built and not run on a machine whose system the <c>system:</c> line calls <paramref name="host"/>: on every system but the one it names, one the line has no name for, null, among them (chapter 0).</summary>
    private static bool NotRun(CaseHeader header, string? host) => header.RunsOn is { } system && system != host;

    /// <summary>This machine's system as a <c>system:</c> line names it, null where the line has no name for it (chapter 0).</summary>
    private static string? Host => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : null;

    /// <summary>Cases run under the plain allocator alone, each with why the fenced one cannot hold them.</summary>
    private static readonly Dictionary<string, string> NotFenced = new(StringComparer.Ordinal)
    {
        ["memory/long-chain-freed.kz"] = "a million instances alive at once, each in pages of its own with an inaccessible page after them: " +
            "two million mappings, past the limit of a process on Linux (`vm.max_map_count`, 1048576 where the run was seen " +
            "ending with `allocation-failed`), and a page of memory each, about four gibibytes",
    };

    /// <summary>
    /// U25, U26, U29: the <c>throws</c> cases whose trace is no raise of the top-level code at the error line, each with the line of its
    /// first frame and the name of its last: a crash escalated to the root, by its policy or by its block, whose trace is the child's stack,
    /// from its raise to its message's method, but a <c>stack-overflow</c> that a block escalates, whose trace is the top-level code's
    /// (section 6, the owner, 2026-11-17, #570, comment 6961064167, Q4; no case pins that trace's line, where it is taken being Q13 of
    /// comment 6964678868 on #573),
    /// and the root's <c>restart-limit</c>, whose trace is the top-level code's where it waits, and
    /// not at the line of the <c>spawn</c> that its error line names (assumed readings of section 6, "Policy at <c>spawn</c>").
    /// </summary>
    private static readonly Dictionary<string, (int First, string Last)> OwnTraces = new(StringComparer.Ordinal)
    {
        ["actors/crash-escalate-root.kz"] = (7, "Worker.Fail"),
        ["actors/block-escalate-root.kz"] = (6, "Worker.Fail"),
        ["actors/per-node-escalate.kz"] = (7, "Cache.Fail"), // U53: a `per node` actor's, whose supervisor is the root
        ["actors/crash-restart-limit.kz"] = (18, "main"),
        ["actors/timeout-chain-left.kz"] = (24, "Front.Ask"),
    };

    public static TheoryData<string> RunnableCases()
    {
        var data = new TheoryData<string>();
        foreach (var file in CorpusTests.CaseFiles())
        {
            if (Header(file) is var header && (header.Expectation != Expectation.Error || CorpusTests.BuildIds.Contains(header.ErrorId!)))
            {
                data.Add(file);
            }
        }

        return data;
    }

    private static CaseHeader Header(string relativePath) => CorpusTests.ReadCase(relativePath).Header;

    /// <summary>The program of a case the checker reads in full and the emitter lays out; else null, with what the emitter said, or null where the checker said <c>not supported</c>.</summary>
    private (EmittedProgram? Program, string? NotSupported) Emit(string relativePath, CaseHeader header, CheckedRuntime? runtime = null)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CorpusTests.Root, "corpus", relativePath));
        var source = SourceText.FromBytes(relativePath, bytes, out _)!;
        var compilation = Builder.Compile(source, header.Grants, runtime ?? toolchain.Runtime, testBuild: header.Build != BuildKind.Release, tests: header.Tests);
        Assert.False(compilation.Diagnostics.HasErrors, $"{relativePath}: the compiler refuses a case that expects a run: {string.Join("; ", compilation.Diagnostics.Sorted)}");
        if (compilation.Program is { } program)
        {
            Assert.DoesNotContain(compilation.Diagnostics.Items, d => d.Kind != DiagnosticKind.Warning); // H5: a lint builds the program
            return (program, null);
        }

        if (CheckerSaysNotSupported(source, header))
        {
            return (null, null);
        }

        return (null, Assert.Single(compilation.Diagnostics.Items).Message); // the emitter says one
    }

    private static bool CheckerSaysNotSupported(SourceText source, CaseHeader header)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Syntax.Parser.Parse(source, diagnostics);
        Semantics.Checker.Check(tree, diagnostics, header.Grants, CorpusTests.StdChecker.Value); // W1: as a build reads it
        return diagnostics.HasNotSupported;
    }

    [Theory]
    [MemberData(nameof(RunnableCases))]
    public void A_built_case_runs_as_its_header_says(string relativePath) => RunCase(relativePath, toolchain.Runtime, string.Empty);

    [Theory]
    [MemberData(nameof(RunnableCases))]
    public void A_built_case_runs_as_its_header_says_where_no_freed_block_can_be_read(string relativePath) =>
        RunCase(relativePath, ListTests.Fenced, "-fenced", NotFenced.ContainsKey(relativePath));

    private void RunCase(string relativePath, CheckedRuntime runtime, string suffix, bool buildOnly = false)
    {
        var header = Header(relativePath);
        var (program, notSupported) = Emit(relativePath, header, runtime);
        if (program is null)
        {
            if (notSupported is not null)
            {
                Assert.True(NotEmitted.TryGetValue(relativePath, out var listed) && listed == notSupported, $"{relativePath}: the emitter says `{notSupported}`, and the case is not listed with those words");
            }

            return;
        }

        var name = relativePath.Replace('/', '_').Replace(".kz", "", StringComparison.Ordinal) + suffix;
        if (header.Expectation == Expectation.Error)
        {
            var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(program, name)); // the build's error (CorpusTests.BuildIds)
            Assert.Equal([(header.ErrorId!, header.ErrorLine)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
            return;
        }

        var executable = toolchain.Build(program, name);
        if (buildOnly || NotRun(header))
        {
            return;
        }

        // the trace on, as a program runs where nobody set KURZ_TRACE; the log line of a crash left out in a case of a crash (E3); a case's
        // `threads: 1` line (chapter 0) asks for the one thread the build runs every program on, first in, first out (U35), so the run sets
        // nothing for it
        var (code, output, written) = Toolchain.Run(executable, null);
        var all = LeftOut(written, header.Rules);
        Assert.Equal(header.OutputLines, output);
        if (header.Tests)
        {
            // Y3, E3: what `kurz test` writes is its standard output, which the header's lines hold, and nothing on standard error but the
            // log lines of crashes it leaves out; a case that throws names the id and the place of the words under the first `FAILED` line
            Assert.Equal("", all);
            if (header.Expectation == Expectation.Throws)
            {
                var failed = output.ToList().FindIndex(l => l.EndsWith(" ... FAILED", StringComparison.Ordinal));
                Assert.True(failed >= 0 && failed + 1 < output.Count, $"{relativePath}: no `FAILED` line with its words under it");
                Assert.StartsWith($"    {header.ErrorId} ", output[failed + 1], StringComparison.Ordinal);
                Assert.EndsWith($" at {relativePath}:{header.ErrorLine}", output[failed + 1], StringComparison.Ordinal);
            }

            Assert.Equal(header.Expectation == Expectation.Throws ? 1 : 0, code);
            return;
        }

        if (header.Expectation == Expectation.Output)
        {
            Assert.Equal((0, ""), (code, all));
            return;
        }

        var (error, trace) = TraceTests.Split(all);
        var line = $"error: {header.ErrorId} at {relativePath}:{header.ErrorLine}";
        if (header.ErrorId is ErrorIds.Thrown or ErrorIds.CheckFailed)
        {
            Assert.StartsWith(line + ":\n", error, StringComparison.Ordinal);
            Assert.True(Toolchain.Lines(error).Count >= 2, $"{relativePath}: no text below the error line: {error}");
        }
        else if (header.ErrorId == ErrorIds.NotCharBoundary)
        {
            // the error lines alone: the trace was split off above and is held below
            Assert.Matches($"^{Regex.Escape(line)}:\nbyte [0-9]+ is inside a code point\n$", error);
        }
        else if (header.ErrorId == ErrorIds.InvalidInput)
        {
            // U40, U41: the bound of zero or less on the line below, written as a duration's text is (assumed: T78 of comment 7055305138)
            Assert.Matches($"^{Regex.Escape(line)}:\nthe bound of `within` is -?[0-9a-z]+; a bound is positive\n$", error);
        }
        else
        {
            Assert.Equal(line + "\n", error);
        }

        var (first, last) = OwnTraces.TryGetValue(relativePath, out var own) ? own : (header.ErrorLine, "main");
        TraceTests.AssertTrace(trace, $"{relativePath}:{first}", header.Build != BuildKind.Release, last);
        Assert.Equal(1, code);
    }

    [Fact]
    public void At_least_the_floor_of_cases_are_built_and_run()
    {
        // the positive control of the theory: its run branch runs for at least this many cases
        var run = 0;
        foreach (var relativePath in CorpusTests.CaseFiles())
        {
            var header = Header(relativePath);
            if (header.Expectation != Expectation.Error && !NotRun(header) && Emit(relativePath, header).Program is not null)
            {
                run++;
            }
        }

        Assert.True(run >= RunFloor, $"only {run} cases are built and run, the floor is {RunFloor}");
    }

    [Fact]
    public void Every_listed_case_exists_expects_a_run_and_is_where_its_list_says()
    {
        // a stale entry would excuse a case from its run: one the emitter lays out, or one the checker never hands it
        Assert.NotEmpty(NotEmitted);
        foreach (var (relativePath, construct) in NotEmitted)
        {
            var header = Header(relativePath);
            Assert.NotEqual(Expectation.Error, header.Expectation);
            var (program, notSupported) = Emit(relativePath, header);
            Assert.True(program is null && notSupported == construct, $"{relativePath}: listed as `{construct}`, and the emitter {(program is null ? notSupported is null ? "is never asked: the checker does not read the case" : $"says `{notSupported}`" : "lays it out")}");
        }

        // a case listed with a trace of its own is a `throws` case the run runs, but on a system whose build says
        // `not supported` to it, as the list of cases not emitted holds it (the loop of that list checks the refusal)
        Assert.NotEmpty(OwnTraces);
        foreach (var (relativePath, _) in OwnTraces)
        {
            var header = Header(relativePath);
            Assert.True(header.Expectation == Expectation.Throws, $"{relativePath}: listed with a trace of its own, and it is no `throws` case");
            if (NotEmitted.ContainsKey(relativePath))
            {
                continue;
            }

            Assert.True(!NotRun(header) && Emit(relativePath, header).Program is not null, $"{relativePath}: listed with a trace of its own, and the run does not run it as a `throws` case");
        }

        // a case listed as run under the plain allocator alone is one the plain run runs
        Assert.NotEmpty(NotFenced);
        foreach (var (relativePath, why) in NotFenced)
        {
            var header = Header(relativePath);
            Assert.True(header.Expectation != Expectation.Error && !NotRun(header) && Emit(relativePath, header).Program is not null, $"{relativePath}: listed as run under the plain allocator alone ({why}), and the plain run does not run it");
        }

        // a `system:` line stands on a case that expects a run and that the emitter lays out, where it keeps a run from the other system
        var named = 0;
        foreach (var relativePath in CorpusTests.CaseFiles())
        {
            var header = Header(relativePath);
            if (header.RunsOn is { } system)
            {
                named++;
                Assert.True(header.Expectation != Expectation.Error && Emit(relativePath, header).Program is not null, $"{relativePath}: its `system:` line names {system}, and the case is no run the emitter lays out");
            }
        }

        Assert.True(named >= 2, $"only {named} cases name a system");
    }

    [Fact]
    public void The_runner_leaves_out_the_log_line_of_a_crash_alone_and_in_a_case_of_a_crash_alone()
    {
        // E3: a case that names a rule of a crash, U19 to U34 or U46 to U50, shows its output without the line of each crash; a line of another category
        // or level stays, and so does the line of a crash in a case that names none of those rules, so that each turns its case red
        const string crash = "2026-11-16T10:00:00.000Z Error Kurz.Actor: Shop#1 crashed: thrown \"no stock\" at shop.kz:3\n";
        const string other = "2026-11-16T10:00:00.000Z Error shop: paid order.Id=17\n";
        const string warning = "2026-11-16T10:00:00.000Z Warning Kurz.Actor: Shop#1 crashed: thrown \"no stock\" at shop.kz:3\n";
        Assert.Equal(other + warning, LeftOut(crash + other + crash + warning, ["U24"]));
        Assert.Equal(string.Empty, LeftOut(crash, ["V1", "U19"]));
        Assert.Equal(string.Empty, LeftOut(crash, ["U34"]));
        Assert.Equal(crash, LeftOut(crash, ["U18"]));
        Assert.Equal(crash, LeftOut(crash, ["U35"]));
        Assert.Equal(string.Empty, LeftOut(crash, ["U46"]));
        Assert.Equal(string.Empty, LeftOut(crash, ["U50"]));
        Assert.Equal(crash, LeftOut(crash, ["U45"]));
        Assert.Equal(crash, LeftOut(crash, ["U51"]));
        Assert.Equal(crash, LeftOut(crash, ["P3"]));
    }

    [Fact]
    public void A_case_whose_system_line_names_another_system_is_built_and_not_run()
    {
        // chapter 0: the `system:` line names the one system a case runs on, which the runner follows in place of a list of its own
        string[] linux = ["outcomes/stack-overflow.kz", "actors/crash-overflow-restarts.kz"];
        var here = OperatingSystem.IsWindows() ? "windows" : "linux";
        var there = OperatingSystem.IsWindows() ? "linux" : "windows";
        foreach (var relativePath in linux)
        {
            var header = Header(relativePath);
            Assert.Equal("linux", header.RunsOn);
            Assert.Equal(OperatingSystem.IsWindows(), NotRun(header));
            Assert.False(NotRun(header with { RunsOn = here }));
            Assert.True(NotRun(header with { RunsOn = there }));
            Assert.False(NotRun(header with { RunsOn = null }));
            Assert.True(NotRun(header, null)); // a machine of a system the line has no name for runs no case that names one
        }
    }
}
