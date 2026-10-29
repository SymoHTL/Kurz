using System.Diagnostics;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions that no corpus case pins: where a refused write and a refused allocation end the program and what they
/// say (design record, section 6; the owner, 2026-10-23, "Caller passes it" and "Id + size"), the test build left unoptimised and
/// the line tables (section 2; the owner, 2026-10-29), the texts of the integers at their edges (A2), a conversion from an unsigned type to a signed one (T20), the empty
/// string's pointer (R15), the largest string (T15; the owner, 2026-10-23, "C#'s limit"), the runtime of every target emitted, and a
/// construct of the runtime the emitter does not lay out, which is the compiler's defect.
/// </summary>
public sealed class EmitterTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime, bool testBuild = true, params string[] grants) =>
        Builder.Compile(SourceText.FromString("program.kz", text), grants, runtime, testBuild);

    private EmittedProgram Emit(string text, params string[] grants)
    {
        var compilation = Compile(text, toolchain.Runtime, testBuild: true, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return compilation.Program;
    }

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name, params string[] grants) =>
        Toolchain.Run(toolchain.Build(Emit(text, grants), name));

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    /// <summary>The runtime of this machine's target with the target's <c>Memory</c> refusing every request, as a system out of memory does.</summary>
    internal static CheckedRuntime RuntimeWithoutMemory(Target target)
    {
        var path = $"runtime/{Runtime.Directory(target)}/";
        var files = Runtime.Files.Select(f => f.Path.StartsWith(path, StringComparison.Ordinal)
            ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Bytes).Replace("pub byte* Memory(long bytes) {", "byte* Granted(long bytes) {", StringComparison.Ordinal) + "\npub byte* Memory(long bytes) => null\n"))
            : f).ToList();
        Assert.NotEqual(Runtime.Files.Select(f => f.Bytes), files.Select(f => f.Bytes)); // the target's file did change
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        return runtime;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // a release build names the line too (the owner, 2026-10-23)
    public async Task A_print_the_system_refuses_raises_print_failed_at_the_file_and_line_of_the_print(bool testBuild)
    {
        var compilation = Compile("mut i = 0\nwhile i < 100000000 {\n    print(\"line\")\n    i = i + 1\n}\n", toolchain.Runtime, testBuild);
        var executable = toolchain.Build(compilation.Program!, testBuild ? "print-failed-test" : "print-failed-release");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        try
        {
            process.StandardOutput.Close(); // the reader is gone: the next write is refused (a closed pipe)
            var error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "the program did not end once its output was closed");
            Assert.Equal((1, "error: print-failed at program.kz:3\n"), (process.ExitCode, await error));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    [Fact]
    public void An_allocation_the_system_refuses_ends_the_program_with_the_id_and_the_size_and_no_line()
    {
        var compilation = Compile("print(12345)\n", RuntimeWithoutMemory(Builder.HostTarget!.Value));
        Ran(Toolchain.Run(toolchain.Build(compilation.Program!, "allocation-failed")), 1, [], "error: allocation-failed (5 bytes)\n");

        // the positive control: the same program with the runtime as it is
        Ran(BuildAndRun("print(12345)\n", "allocation-granted"), 0, ["12345"], "");
    }

    [Fact]
    public void The_test_build_keeps_every_call_and_the_release_build_turns_a_tail_call_into_a_loop()
    {
        // the owner, 2026-10-29, "As Rust's dev profile, -O0": a recursion of a test build runs as deep as the program wrote it
        const string text = "long Count(long n) {\n    if n == 0 {\n        return 0\n    }\n\n    return Count(n - 1)\n}\n\nprint(Count(10000000))\n";
        Ran(Toolchain.Run(toolchain.Build(Compile(text, toolchain.Runtime, testBuild: false).Program!, "tail-release")), 0, ["0"], "");
        Assert.NotEqual(0, BuildAndRun(text, "tail-test").Code); // ten million frames do not fit the stack
    }

    [Fact]
    public void A_test_build_and_a_release_build_with_lines_carry_line_tables_under_the_names_a_trace_shows_and_a_release_build_does_not()
    {
        const string text = "T Max<T>(T a, T b) => a\n\nint Apply((int) => int f, int n) => f(n)\n\nint Twice(int n) => Apply(x => x * 2, n)\n\n"
            + "print(Max(1, 2))\nprint(Apply(x => x + 1, Twice(3)))\n";
        var test = Emit(text);
        var release = Compile(text, toolchain.Runtime, testBuild: false).Program!;
        var lines = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: false, lines: true).Program!;
        Assert.Equal((false, true, true, false, true, true), (test.Release, test.Lines, release.Release, release.Lines, lines.Release, lines.Lines));

        // design record, section 6, and the owner's "Type.Method, Max<int>, std path" of 2026-10-29
        foreach (var name in new[] { "main", "Max<int>", "Apply", "Twice", "Twice.lambda", "main.lambda" })
        {
            Assert.Contains($"!DISubprogram(name: \"{name}\",", test.Ir, StringComparison.Ordinal);
            Assert.Contains($"!DISubprogram(name: \"{name}\",", lines.Ir, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("!dbg", release.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("DISPFlagOptimized", test.Ir, StringComparison.Ordinal);
        Assert.Contains("DISPFlagOptimized", lines.Ir, StringComparison.Ordinal);

        // in the executable on Linux, in the PDB beside it on Windows
        var built = new[] { (test, "lines-test"), (release, "lines-release"), (lines, "lines-lines") }.Select(b => Toolchain.HasLineTables(toolchain.Build(b.Item1, b.Item2)));
        Assert.Equal([true, false, true], built);
    }

    [Fact]
    public void The_integers_at_their_edges_have_their_texts()
    {
        const string text = "long Least() => -9223372036854775807 - 1\nulong Most() => 18446744073709551615\n\nprint(Least())\nprint(Most())\nprint(sbyte(-128))\nprint(0)\n";
        Ran(BuildAndRun(text, "edges"), 0, ["-9223372036854775808", "18446744073709551615", "-128", "0"], "");
    }

    [Fact]
    public void A_function_whose_first_instruction_starts_a_block_enters_it_from_the_entry_block()
    {
        // a `mut` parameter is the caller's storage, so no store comes before the loop's first block
        const string text = "void Down(mut int n) {\n    while n > 0 {\n        n = n - 1\n    }\n}\n\nmut int k = 3\nDown(mut k)\nprint(k)\n";
        Ran(BuildAndRun(text, "loop-first"), 0, ["0"], "");
    }

    [Theory]
    [InlineData("int(Most())", "overflow")] // the high bit set: the cut to 32 bits and its sign extension give the value back
    [InlineData("sbyte(Most())", "overflow")]
    [InlineData("long(Most())", "overflow")] // of the same width
    [InlineData("int(Most() / 8589934592)", "2147483647")] // the largest that fits, the positive control
    public void A_conversion_from_an_unsigned_type_to_a_signed_one_raises_overflow_above_the_signed_maximum(string conversion, string result)
    {
        var run = BuildAndRun($"ulong Most() => 18446744073709551615\n\nprint({conversion})\n", $"narrow-{result}-{conversion.Length}");
        if (result == "overflow")
        {
            Ran(run, 1, [], "error: overflow at program.kz:3\n");
        }
        else
        {
            Ran(run, 0, [result], "");
        }
    }

    [Fact]
    public void The_pointer_of_the_empty_string_is_not_null()
    {
        const string text = "string empty = \"\"\nraw {\n    print(empty.Pointer != null)\n    print(empty.Bytes.Count)\n}\n";
        Ran(BuildAndRun(text, "empty-pointer", "raw"), 0, ["true", "0"], "");
    }

    [Theory]
    [InlineData("print(string.FromRaw(Ptr<byte>(16), 2147483647).Bytes.Count)", 0, "2147483647", "")] // the largest that fits, the positive control
    [InlineData("print(string.FromRaw(Ptr<byte>(16), 2147483648).Bytes.Count)", 1, null, "error: allocation-failed (2147483648 bytes)\n")]
    [InlineData("print(\"{string.FromRaw(Ptr<byte>(16), 2147483647)}a\")", 1, null, "error: allocation-failed (2147483648 bytes)\n")] // a join
    public void A_string_holds_at_most_the_largest_int_of_bytes_and_a_longer_one_ends_as_a_refused_allocation(string statement, int code, string? line, string error)
    {
        // the owner, "C#'s limit" on 2026-10-23 and "allocation-failed" on 2026-10-24; no byte at the pointer is read
        Ran(BuildAndRun($"raw {{\n    {statement}\n}}\n", $"string-limit-{code}-{statement.Length}", "raw"), code, line is null ? [] : [line], error);
    }

    [Fact]
    public void The_runtime_of_every_target_is_emitted_with_its_own_calls_of_the_system()
    {
        foreach (var (target, call) in new[] { (Target.Linux, "@write("), (Target.Windows, "@WriteFile(") })
        {
            var program = Compile("print(1)\n", Builder.CheckRuntime(target)).Program;
            Assert.True(program is not null, $"no program for {target}");
            Assert.Contains("define i32 @main()", program.Ir, StringComparison.Ordinal);
            Assert.Contains(call, program.Ir, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_construct_of_the_runtime_the_emitter_does_not_lay_out_is_the_compilers_defect_and_names_its_file()
    {
        var target = Builder.HostTarget!.Value;
        var path = $"runtime/{Runtime.Directory(target)}/";
        var files = Runtime.Files.Select(f => f.Path.StartsWith(path, StringComparison.Ordinal)
            ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Bytes).Replace("pub void Start() {", "pub void Start() {\n    print(\"ab\".Bytes[0])", StringComparison.Ordinal)))
            : f).ToList();
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        var e = Assert.Throws<InvalidOperationException>(() => Compile("print(1)\n", runtime));
        Assert.StartsWith($"{path}{Runtime.Directory(target)}.kz:", e.Message, StringComparison.Ordinal);
        Assert.Contains("(T15)", e.Message, StringComparison.Ordinal);
    }
}
