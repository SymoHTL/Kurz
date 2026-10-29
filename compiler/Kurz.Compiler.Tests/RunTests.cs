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
        Runtime = Builder.CheckRuntime(Builder.HostTarget ?? throw new InvalidOperationException("the runtime has no directory for this system"));
        Directory = System.IO.Directory.CreateTempSubdirectory("kurz-tests-").FullName;
    }

    public Clang Clang { get; }

    /// <summary>The runtime of the target of this machine, which every program the suite builds is emitted with.</summary>
    public CheckedRuntime Runtime { get; }

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

    /// <summary>Runs a built program with nothing on its input; its exit code, what it wrote on standard output as lines, and what it wrote on standard error.</summary>
    public static (int Code, IReadOnlyList<string> Output, string Error) Run(string executable)
    {
        var (code, output, error) = Processes.Capture(executable, [], null, TimeSpan.FromSeconds(30));
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
/// machine. A case the checker reads in full and the emitter lays out prints exactly the header's lines on standard output; an
/// <c>output</c> case then exits with 0 and writes nothing on standard error; a <c>throws</c> case exits with 1, and its error stream
/// is the one line <c>error: &lt;id&gt; at &lt;file&gt;:&lt;n&gt;</c> of the design record's section 6, with the header's id and line and
/// the case's path under <c>corpus/</c> as the file, or for <c>thrown</c> that line ending in a colon with the thrown text below it.
/// A case the checker says <c>not supported</c> to is held by the checker's theory in <see cref="CorpusTests"/> and is not built here; a
/// case the checker reads and the emitter does not lay out is in <see cref="NotEmitted"/>, with the construct the emitter names; and a
/// case it lays out whose expectation needs what the build does not hold is in <see cref="NotRun"/>, with why, and is built and not run.
/// Neither list holds a stale entry. A case is built with the grants of its <c>allow:</c> line, and as its <c>build:</c> line says, else
/// as the test build, the default. A floor holds the count of cases run.
/// </summary>
public sealed class RunTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    /// <summary>The cases built and run: fewer means the emitter refuses what it built before.</summary>
    private const int RunFloor = 187;

    /// <summary>Cases the checker reads in full and the emitter does not lay out, each with what the emitter says.</summary>
    private static readonly Dictionary<string, string> NotEmitted = new(StringComparer.Ordinal)
    {
        ["memory/asm-explicit-register.kz"] = "inline assembly (R11)",
        ["memory/asm-in-raw.kz"] = "inline assembly (R11)",
        ["types/string-bytes.kz"] = "`s.Bytes` as a list (T15)",
        ["types/string-chars.kz"] = "`s.Chars` as a list (T15)",
    };

    /// <summary>Cases the emitter lays out whose expectation the build does not hold, each with why.</summary>
    private static readonly Dictionary<string, string> NotRun = new(StringComparer.Ordinal)
    {
        ["outcomes/stack-overflow.kz"] = "the raise of `stack-overflow` is the crash path's (design record, section 6), which is not emitted",
    };

    public static TheoryData<string> RunnableCases()
    {
        var data = new TheoryData<string>();
        foreach (var file in CorpusTests.CaseFiles())
        {
            if (Header(file).Expectation != Expectation.Error)
            {
                data.Add(file);
            }
        }

        return data;
    }

    private static CaseHeader Header(string relativePath) => CorpusTests.ReadCase(relativePath).Header;

    /// <summary>The program of a case the checker reads in full and the emitter lays out; else null, with what the emitter said, or null where the checker said <c>not supported</c>.</summary>
    private (EmittedProgram? Program, string? NotSupported) Emit(string relativePath, CaseHeader header)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CorpusTests.Root, "corpus", relativePath));
        var source = SourceText.FromBytes(relativePath, bytes, out _)!;
        var compilation = Builder.Compile(source, header.Grants, toolchain.Runtime, testBuild: header.Build != BuildKind.Release);
        Assert.False(compilation.Diagnostics.HasErrors, $"{relativePath}: the compiler refuses a case that expects a run: {string.Join("; ", compilation.Diagnostics.Sorted)}");
        if (compilation.Program is { } program)
        {
            Assert.Empty(compilation.Diagnostics.Items);
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
        Semantics.Checker.Check(tree, diagnostics, header.Grants);
        return diagnostics.HasNotSupported;
    }

    [Theory]
    [MemberData(nameof(RunnableCases))]
    public void A_built_case_runs_as_its_header_says(string relativePath)
    {
        var header = Header(relativePath);
        var (program, notSupported) = Emit(relativePath, header);
        if (program is null)
        {
            if (notSupported is not null)
            {
                Assert.True(NotEmitted.TryGetValue(relativePath, out var listed) && listed == notSupported, $"{relativePath}: the emitter says `{notSupported}`, and the case is not listed with those words");
            }

            return;
        }

        var executable = toolchain.Build(program, relativePath.Replace('/', '_').Replace(".kz", "", StringComparison.Ordinal));
        if (NotRun.ContainsKey(relativePath))
        {
            return;
        }

        var (code, output, error) = Toolchain.Run(executable);
        Assert.Equal(header.OutputLines, output);
        if (header.Expectation == Expectation.Output)
        {
            Assert.Equal((0, ""), (code, error));
            return;
        }

        var line = $"error: {header.ErrorId} at {relativePath}:{header.ErrorLine}";
        if (header.ErrorId == ErrorIds.Thrown)
        {
            Assert.StartsWith(line + ":\n", error, StringComparison.Ordinal);
            Assert.True(Toolchain.Lines(error).Count >= 2, $"{relativePath}: no text below the error line: {error}");
        }
        else
        {
            Assert.Equal(line + "\n", error);
        }

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
            if (header.Expectation != Expectation.Error && !NotRun.ContainsKey(relativePath) && Emit(relativePath, header).Program is not null)
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

        foreach (var (relativePath, why) in NotRun)
        {
            var header = Header(relativePath);
            Assert.True(header.Expectation != Expectation.Error && Emit(relativePath, header).Program is not null, $"{relativePath}: listed as not run ({why}), and the emitter does not lay it out");
        }
    }
}
