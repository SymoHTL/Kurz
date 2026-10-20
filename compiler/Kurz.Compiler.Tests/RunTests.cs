using Kurz.Compiler.Build;
using Kurz.Compiler.Corpus;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The clang of this machine and the runtime compiled once, for every program the suite builds. A
/// machine without clang fails every test that needs it, with the places looked at: a build that
/// cannot run the corpus is red, never a pass by omission.
/// </summary>
public sealed class Toolchain : IDisposable
{
    public Toolchain()
    {
        Clang = Compiler.Build.Clang.Find(out var looked) ?? throw new InvalidOperationException($"clang is not there; looked for {string.Join(", ", looked)}");
        Directory = Path.Combine(Path.GetTempPath(), "kurz-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        RuntimeObject = Path.Combine(Directory, "kurz_rt.o");
        Clang.CompileRuntime(Builder.WriteRuntime(Directory), RuntimeObject);
    }

    public Clang Clang { get; }

    /// <summary>The directory of this run's builds, removed with the fixture.</summary>
    public string Directory { get; }

    /// <summary>The runtime as an object file, linked into every program of the run.</summary>
    public string RuntimeObject { get; }

    /// <summary>Builds the IR into an executable under its own directory; the executable's path.</summary>
    public string Build(string ir, string name)
    {
        var directory = Path.Combine(Directory, name);
        var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "program.exe" : "program");
        Builder.Build(ir, Clang, directory, RuntimeObject, executable);
        return executable;
    }

    /// <summary>Runs a built program with nothing on its input; what it wrote, as lines, and its exit code.</summary>
    public static (int Code, IReadOnlyList<string> Output, string Error) Run(string executable)
    {
        var (code, output, error) = Processes.Capture(executable, [], null, TimeSpan.FromSeconds(30));
        return (code, Lines(output), error);
    }

    /// <summary>The lines of a program's output: each ends in a newline, so the text's last piece is empty and dropped.</summary>
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
/// Every corpus case that expects a run (<c>output</c> or <c>throws</c>), built by the emitter and
/// clang and run. The emitter's theory: a case the checker reads in full and the emitter lays out
/// prints exactly the header's lines on standard output; an <c>output</c> case then exits with 0
/// and writes nothing on standard error; a <c>throws</c> case exits with 1 and its error stream is
/// the one line <c>error: &lt;id&gt; at line &lt;n&gt;</c> of the header, with the thrown text after it
/// for a <c>throw</c>. A case the checker does not read is held by the checker's theory; a case the
/// checker reads and the emitter does not lay out is in the list of cases the emitter does not
/// emit, each with the construct, and that list holds no case the emitter emits. A case with a
/// <c>build:</c> line is built as it says; the others are built as the test build, the default.
/// No case is skipped, and a floor holds the count of cases run.
/// </summary>
public sealed class RunTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    /// <summary>The suite runs more cases than this; fewer means the emitter refuses what it built before.</summary>
    private const int RunFloor = 100;

    /// <summary>Cases the checker reads in full and the emitter does not lay out, each with the construct; a stale entry, whose case the emitter emits, is refused.</summary>
    private static readonly Dictionary<string, string> NotEmitted = new(StringComparer.Ordinal)
    {
        ["data/equality-nan.kz"] = "a field of type double (T24, D3)",
        ["source/number-literal-forms.kz"] = "a double constant (T26, L2)",
        ["source/number-literals.kz"] = "a double constant (T26, L2)",
        ["source/unit-literal-fraction.kz"] = "a duration constant (T29, L5)",
        ["types/constant-double-division.kz"] = "a double constant (T26)",
        ["types/constant-mixed-fold.kz"] = "arithmetic on a double (T26, T28)",
        ["types/conversion-float-infinity.kz"] = "a float constant (T26, T20)",
        ["types/conversion-float-test.kz"] = "a double constant (T26, T20)",
        ["types/conversion-saturates.kz"] = "a double constant (T26, T20)",
        ["types/decimal-divide-by-zero.kz"] = "a decimal constant (T24, T25)",
        ["types/duration-parameter.kz"] = "a duration constant (T29)",
        ["types/float-remainder-by-zero.kz"] = "a double constant (T26, T25)",
        ["types/fraction-beside-narrow.kz"] = "arithmetic on a double (T26, T28)",
        ["types/longduration-parameter.kz"] = "a duration constant (T29)",
        ["types/longduration-wide.kz"] = "a longduration constant (T29)",
        ["types/widening-across-sign.kz"] = "a double variable (T26, T23)",
        ["values/text-double-special.kz"] = "a double constant (T26, A12)",
        ["values/text-duration-sub-millisecond.kz"] = "a duration constant (T29, A14)",
        ["values/text-duration.kz"] = "a duration constant (T29, A14)",
        ["values/text-float.kz"] = "a float constant (T26, A11)",
    };

    public static TheoryData<string> RunnableCases()
    {
        var data = new TheoryData<string>();
        foreach (var file in CorpusFiles.CaseFiles())
        {
            var header = CorpusFiles.ReadCase(file).Header;
            if (header.Expectation != Expectation.Error)
            {
                data.Add(file);
            }
        }

        return data;
    }

    /// <summary>The IR of a case the checker reads in full and the emitter lays out, else null with why: the checker's or the emitter's diagnostic.</summary>
    private static (string? Ir, string? NotSupported) Emit(string relativePath, CaseHeader header)
    {
        var (_, tree, frontEnd) = CorpusFiles.ReadCase(relativePath);
        Assert.False(frontEnd.HasErrors, $"{relativePath}: the front end refuses a case that expects a run");
        var diagnostics = new DiagnosticBag();
        var checker = Checker.Check(tree, diagnostics);
        Assert.False(diagnostics.HasErrors, $"{relativePath}: the checker refuses a case that expects a run: {string.Join("; ", diagnostics.Sorted)}");
        if (diagnostics.HasNotSupported)
        {
            return (null, null); // the checker's theory holds this case
        }

        var ir = Emitter.Emit(tree, checker, testBuild: header.Build != BuildKind.Release, diagnostics);
        if (ir is null)
        {
            Assert.True(diagnostics.HasNotSupported, $"{relativePath}: no IR and no diagnostic");
            return (null, string.Join("; ", diagnostics.Sorted));
        }

        Assert.Empty(diagnostics.Items);
        return (ir, null);
    }

    [Theory]
    [MemberData(nameof(RunnableCases))]
    public void A_built_case_runs_as_its_header_says(string relativePath)
    {
        var header = CorpusFiles.ReadCase(relativePath).Header;
        var (ir, notSupported) = Emit(relativePath, header);
        if (ir is null)
        {
            if (notSupported is not null)
            {
                Assert.True(NotEmitted.ContainsKey(relativePath), $"{relativePath}: the emitter says `{notSupported}`, and the case is not listed as one it does not emit");
            }

            return;
        }

        var executable = toolchain.Build(ir, relativePath.Replace('/', '_').Replace(".kz", "", StringComparison.Ordinal));
        var (code, output, error) = Toolchain.Run(executable);
        Assert.Equal(header.OutputLines, output);
        if (header.Expectation == Expectation.Output)
        {
            Assert.Equal((0, ""), (code, error));
        }
        else
        {
            var line = Toolchain.Lines(error);
            Assert.True(line.Count == 1, $"{relativePath}: the error stream holds {line.Count} lines: {error}");
            var expected = $"error: {header.ErrorId} at line {header.ErrorLine}";
            Assert.True(line[0] == expected || line[0].StartsWith(expected + ": ", StringComparison.Ordinal), $"{relativePath}: expected `{expected}`, got `{line[0]}`");
            Assert.Equal(1, code);
        }
    }

    [Fact]
    public void At_least_the_floor_of_cases_are_built_and_run()
    {
        // the positive control of the theory: its run branch runs for at least this many cases
        var run = 0;
        var notRun = new List<string>();
        foreach (var relativePath in CorpusFiles.CaseFiles())
        {
            var header = CorpusFiles.ReadCase(relativePath).Header;
            if (header.Expectation == Expectation.Error)
            {
                continue;
            }

            if (Emit(relativePath, header).Ir is null)
            {
                notRun.Add(relativePath);
            }
            else
            {
                run++;
            }
        }

        Assert.True(run >= RunFloor, $"only {run} cases are built and run; not run: {string.Join(", ", notRun)}");
    }

    [Fact]
    public void Every_case_listed_as_not_emitted_exists_is_read_in_full_by_the_checker_and_gets_not_supported_from_the_emitter()
    {
        // a stale entry would excuse a case the emitter emits from its run; an entry for a case the checker does not read excuses nothing
        foreach (var (relativePath, construct) in NotEmitted)
        {
            var header = CorpusFiles.ReadCase(relativePath).Header;
            Assert.NotEqual(Expectation.Error, header.Expectation);
            var (ir, notSupported) = Emit(relativePath, header);
            Assert.True(ir is null && notSupported is not null, $"{relativePath}: the emitter {(ir is null ? "is never asked: the checker does not read the case" : "emits the case")}, so its entry ({construct}) excuses nothing");
        }
    }
}
