using System.Diagnostics;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions that no corpus case pins: where a refused write and a refused allocation end the program and what they
/// say (design record, section 6; the owner, 2026-10-23, "Caller passes it" and "Id + size"), the test build's attribute
/// (section 2), the texts of the integers at their edges (A2), the empty string's pointer (R15), the runtime of every target
/// emitted, and a construct of the runtime the emitter does not lay out, which is the compiler's defect.
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
    private static CheckedRuntime RuntimeWithoutMemory(Target target)
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
    public void The_test_build_asks_the_backend_to_keep_every_call_and_the_release_build_does_not()
    {
        const string text = "int Next(int n) => n + 1\n\nprint(Next(1))\n";
        Assert.Contains("\"disable-tail-calls\"=\"true\"", Compile(text, toolchain.Runtime, testBuild: true).Program!.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("disable-tail-calls", Compile(text, toolchain.Runtime, testBuild: false).Program!.Ir, StringComparison.Ordinal);
    }

    [Fact]
    public void The_integers_at_their_edges_have_their_texts()
    {
        const string text = "long Least() => -9223372036854775807 - 1\nulong Most() => 18446744073709551615\n\nprint(Least())\nprint(Most())\nprint(sbyte(-128))\nprint(0)\n";
        Ran(BuildAndRun(text, "edges"), 0, ["-9223372036854775808", "18446744073709551615", "-128", "0"], "");
    }

    [Fact]
    public void The_pointer_of_the_empty_string_is_not_null()
    {
        const string text = "string empty = \"\"\nraw {\n    print(empty.Pointer != null)\n    print(empty.Bytes.Count)\n}\n";
        Ran(BuildAndRun(text, "empty-pointer", "raw"), 0, ["true", "0"], "");
    }

    [Fact]
    public void The_count_of_a_strings_bytes_is_read_as_an_int()
    {
        // 4294967297 bytes, 2 to the 32 and one, of which an int holds the low 32 bits; no byte is read
        const string text = "raw {\n    string huge = string.FromRaw(Ptr<byte>(16), 4294967297)\n    print(huge.Bytes.Count)\n}\n";
        Ran(BuildAndRun(text, "count-as-int", "raw"), 0, ["1"], "");
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
            ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Bytes).Replace("pub void Start() {", "pub void Start() {\n    print(List<int>().Count)", StringComparison.Ordinal)))
            : f).ToList();
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        var e = Assert.Throws<InvalidOperationException>(() => Compile("print(1)\n", runtime));
        Assert.StartsWith($"{path}{Runtime.Directory(target)}.kz:", e.Message, StringComparison.Ordinal);
        Assert.Contains("(M9)", e.Message, StringComparison.Ordinal);
    }
}
