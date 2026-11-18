using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions that no corpus case pins: where a refused write and a refused allocation end the program and what they
/// say (design record, section 6; the owner, 2026-10-23, "Caller passes it" and "Id + size"), the test build left unoptimised and
/// the line tables (section 2; the owner, 2026-10-29), the texts of the integers at their edges (A2), a conversion from an unsigned type to a signed one (T20), the empty
/// string's pointer (R15), the largest string (T15; the owner, 2026-10-23, "C#'s limit"), the integer an enum's value is stored in (D12;
/// section 4, an assumption there), the layout of a nullable value and the address of one known not null (N1, N3, R10), the reference to an instance of a class (K6), the constraints and the attributes of an `asm` and the module's assembly of a `naked` function (R11, R12), the runtime of every target emitted, and a
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
        var path = $"runtime/{Runtime.Directory(target)}/{Runtime.Directory(target)}.kz"; // the target's own file, beside its architectures' directories
        var files = Runtime.Files.Select(f => f.Path == path
            ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Bytes).Replace("pub byte* Memory(long bytes) {", "byte* Granted(long bytes) {", StringComparison.Ordinal) + "\npub byte* Memory(long bytes) => null\n"))
            : f).ToList();
        Assert.NotEqual(Runtime.Files.Select(f => f.Bytes), files.Select(f => f.Bytes)); // the target's file did change
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        return runtime;
    }

    [Fact]
    public void An_eprint_writes_its_line_to_standard_error_as_a_print_writes_to_standard_output()
    {
        // A17: the text and the one line feed of `print` (A1), on standard error, and nothing of it on standard output
        var run = BuildAndRun("eprint(\"to error\")\nprint(1)\nname = \"kurz\"\neprint(\"hi {name}\")\neprint(25)\n", "eprint");
        Assert.Equal(["1"], run.Output);
        Assert.Equal(0, run.Code);
        Assert.Equal("to error\nhi kurz\n25\n", run.Error);
    }

    [Fact]
    public async Task An_eprint_the_system_refuses_ends_the_program_with_the_code_of_an_error_and_its_report_dropped()
    {
        // A17: the failed write raises `print-failed`, whose report goes to the same closed standard error and is dropped; the program
        // ends with 1, as a crash does (section 6), and no statement after the `eprint` runs
        var compilation = Compile("mut i = 0\nwhile i < 100000000 {\n    eprint(\"line\")\n    i = i + 1\n}\nprint(\"after\")\n", toolchain.Runtime, false);
        var executable = toolchain.Build(compilation.Program!, "eprint-failed");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        try
        {
            process.StandardError.Close(); // the reader is gone: the next write is refused (a closed pipe)
            var output = process.StandardOutput.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "the program did not end once its standard error was closed");
            Assert.Equal(1, process.ExitCode);
            Assert.Equal("", await output);
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
    [InlineData(true)]
    [InlineData(false)] // a release build names the line too (the owner, 2026-10-23)
    public async Task A_print_the_system_refuses_raises_print_failed_at_the_file_and_line_of_the_print(bool testBuild)
    {
        var compilation = Compile("mut i = 0\nwhile i < 100000000 {\n    print(\"line\")\n    i = i + 1\n}\n", toolchain.Runtime, testBuild);
        var executable = toolchain.Build(compilation.Program!, testBuild ? "print-failed-test" : "print-failed-release");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["KURZ_TRACE"] = "0"; // the error line alone: the trace below it is TraceTests'
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
        // the text's 5 bytes and the 16 in front of them, its counts and its room (design record, section 4, assumed)
        var compilation = Compile("print(12345)\n", RuntimeWithoutMemory(Builder.HostTarget!.Value));
        Ran(Toolchain.Run(toolchain.Build(compilation.Program!, "allocation-failed")), 1, [], "error: allocation-failed (21 bytes)\n");

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
    public void A_value_through_Hint_BlackBox_is_stored_hidden_by_an_empty_asm_and_read_back_and_no_constant_to_the_optimiser()
    {
        // F18, lowered as rustc lowers Rust's `black_box` (section 12, the owner, 2026-11-03, "The defaults of the round stand"): the value
        // goes to a slot, an empty `asm sideeffect` takes the slot's address and clobbers memory, and the value is read back. In the IR
        // clang optimises as a release build is optimised, 7340032 is stored to a slot, the asm takes that slot, a load reads it after
        // the asm and the sum adds 1 to what it read, so the sum is nowhere a constant; the same program without the hint folds the
        // sum, and its text, into the digits it writes, so that 7340032 is nowhere in its IR (the positive control); both builds print
        // the sum
        const string hinted = "n = Hint.BlackBox(7340032)\nprint(n + 1)\n";
        const string plain = "n = 7340032\nprint(n + 1)\n";
        string Optimised(string text, string name)
        {
            var program = Compile(text, toolchain.Runtime, testBuild: false).Program!;
            Ran(Toolchain.Run(toolchain.Build(program, name)), 0, ["7340033"], "");
            var directory = Path.Combine(toolchain.Directory, name);
            Directory.CreateDirectory(directory);
            var input = Path.Combine(directory, "program.ll");
            var output = Path.Combine(directory, "optimised.ll");
            File.WriteAllText(input, program.Ir);
            List<string> arguments = ["-O2", "-S", "-emit-llvm", input, "-o", output];
            if (program.CpuFlag is { } cpu)
            {
                arguments.Add(cpu);
            }

            var (code, _, error) = Processes.Capture(toolchain.Clang.Path, arguments, null, TimeSpan.FromMinutes(2));
            Assert.True(code == 0, error);
            return File.ReadAllText(output);
        }

        var ir = Optimised(hinted, "hint-black-box");
        Assert.Matches(
            new Regex("""store i32 7340032, ptr (%[\w.]+).*call void asm sideeffect "", "r,~\{memory\}"\(ptr (nonnull )?\1\).*(%[\w.]+) = load i32, ptr \1\b.*= add (nsw )?i32 \3, 1\b""", RegexOptions.Singleline),
            ir);
        Assert.DoesNotContain("7340033", ir, StringComparison.Ordinal);
        var control = Optimised(plain, "hint-black-box-control");
        Assert.DoesNotContain("7340032", control, StringComparison.Ordinal);
        Assert.DoesNotContain("asm sideeffect \"\", \"r,~{memory}\"", control, StringComparison.Ordinal);
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

        // the owner, 2026-10-29, "As Rust: the build's directory"
        Assert.Contains($"directory: {IrModule.Quote(Environment.CurrentDirectory)})", test.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("!dbg", release.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("DISPFlagOptimized", test.Ir, StringComparison.Ordinal);
        Assert.Contains("DISPFlagOptimized", lines.Ir, StringComparison.Ordinal);

        // in the executable on Linux, in the PDB beside it on Windows
        var built = new[] { (test, "lines-test"), (release, "lines-release"), (lines, "lines-lines") }.Select(b => Toolchain.HasLineTables(toolchain.Build(b.Item1, b.Item2)));
        Assert.Equal([true, false, true], built);
    }

    [Fact]
    public void An_instruction_after_a_nested_statement_carries_the_line_of_the_statement_around_it()
    {
        // compiler/README.md: the line of each statement on the instructions emitted for it, so that the back edge of a loop, emitted
        // after its body, is the `while`'s at line 2 and not the line of the body's last statement
        var ir = Emit("mut i = 0\nwhile i < 2 {\n    i = i + 1\n}\nprint(i)\n").Ir;
        var start = ir.IndexOf("define internal void @kz.main()", StringComparison.Ordinal);
        Assert.True(start >= 0, "no kz.main in the IR");
        var body = ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        var lines = Regex.Matches(body, @"br label %while\.[0-9]+, !dbg (![0-9]+)")
            .Select(m => Regex.Match(ir, $@"^{m.Groups[1].Value} = !DILocation\(line: ([0-9]+),", RegexOptions.Multiline).Groups[1].Value);
        Assert.Equal(["2"], lines);
    }

    [Fact]
    public void A_module_rendered_twice_writes_the_same_text()
    {
        // the line tables' nodes, the module's flags among them, are made once
        var module = new IrModule { Debug = new DebugInfo(".", "program.kz", "kurz", optimized: false, codeView: false) };
        Assert.Equal(module.Render(), module.Render());
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
    [InlineData("print(string.FromRaw(string.Allocate(1), 2147483647).Bytes.Count)", 0, "2147483647", "")] // the largest that fits, the positive control: a count past the block's room, which nothing reads beyond, freed as the block's room says
    [InlineData("print(string.FromRaw(Ptr<byte>(16), 2147483648).Bytes.Count)", 1, null, "error: allocation-failed (2147483648 bytes)\n")]
    [InlineData("print(\"{string.FromRaw(string.Allocate(1), 2147483647)}a\")", 1, null, "error: allocation-failed (2147483648 bytes)\n")] // a join
    [InlineData("print(string.Allocate(2147483648) != null)", 1, null, "error: allocation-failed (2147483648 bytes)\n")]
    public void A_string_holds_at_most_the_largest_int_of_bytes_and_a_longer_one_ends_as_a_refused_allocation(string statement, int code, string? line, string error)
    {
        // the owner, "C#'s limit" on 2026-10-23 and "allocation-failed" on 2026-10-24; no byte at the pointer is read
        Ran(BuildAndRun($"raw {{\n    {statement}\n}}\n", $"string-limit-{code}-{statement.Length}", "raw"), code, line is null ? [] : [line], error);
    }

    [Fact]
    public void An_asm_takes_the_constraints_and_the_attributes_Rusts_compiler_gives_it_and_a_naked_function_is_assembly_of_the_module()
    {
        // R11, R12, lowered as rustc lowers `asm!` and `#[naked]` (README, "What the emitter emits"); what each call does is run by the
        // corpus cases memory/asm-operands.kz and memory/naked-function.kz, and what a run cannot show is pinned here: the options' words,
        // clobbers and attributes, each statement a control of the others, and the registers each `clobber_abi` lets go of
        const string text = "mut long a = 1\nmut long b = 2\nmut long c = 3\nraw {\n"
            + "    asm(\"add {r}, 1\", r = inout(reg) a)\n"
            + "    asm(\"lea {o}, [{i} + 1]\", i = in(reg) a, o = out(reg) b, options(pure, nomem, nostack, preserves_flags))\n"
            + "    asm(\"mov ({p}), {o}\", p = in(reg) &c, o = out(reg) b, options(pure, readonly, att_syntax))\n"
            + "    asm(\"nop\", options(nomem, nostack))\n"
            + "    asm(\"nop\", out(\"rsi\") b, clobber_abi(\"sysv64\"))\n"
            + "    asm(\"nop\", clobber_abi(\"win64\"))\n"
            + "}\nprint(b)\n";
        var program = Compile(text, Builder.CheckRuntime(Target.Linux), testBuild: true, "raw").Program;
        Assert.True(program is not null, "no program");
        var calls = program.Ir.ReplaceLineEndings("\n").Split('\n').Where(l => l.Contains(" asm ", StringComparison.Ordinal) && l.Contains("call ", StringComparison.Ordinal) && !l.Contains("\"\", \"r,~{memory}\"", StringComparison.Ordinal))
            .Select(l => Regex.Replace(l, @", !srcloc !\{i64 \d+\}, !dbg !\d+$", string.Empty)).ToList(); // the call's mark and the line's location set aside
        var shown = string.Join("\n", calls);
        Assert.True(calls.Count == 6, shown);
        Assert.Contains("asm sideeffect alignstack inteldialect \"add ${0:q}, 1\", \"=&r,0,~{dirflag},~{fpsr},~{flags},~{memory}\"(", calls[0], StringComparison.Ordinal);
        Assert.EndsWith(") nounwind", calls[0], StringComparison.Ordinal);
        Assert.Contains("asm inteldialect \"lea ${0:q}, [${1:q} + 1]\", \"=&r,r\"(", calls[1], StringComparison.Ordinal);
        Assert.EndsWith(") nounwind willreturn memory(none)", calls[1], StringComparison.Ordinal);
        Assert.Contains("asm alignstack \"mov (${1:q}), ${0:q}\", \"=&r,r,~{dirflag},~{fpsr},~{flags},~{memory}\"(", calls[2], StringComparison.Ordinal);
        Assert.EndsWith(") nounwind willreturn memory(read)", calls[2], StringComparison.Ordinal);
        Assert.Contains("call void asm sideeffect inteldialect \"nop\", \"~{dirflag},~{fpsr},~{flags}\"()", calls[3], StringComparison.Ordinal);
        Assert.EndsWith(") nounwind memory(inaccessiblemem: readwrite)", calls[3], StringComparison.Ordinal);
        Assert.Contains("\"=&{si},={ax},={cx},={dx},={di},={r8},={r9},={r10},={r11},={xmm0},", calls[4], StringComparison.Ordinal); // rsi once, as the output names it
        Assert.Contains(",={xmm15},~{xmm16},", calls[4], StringComparison.Ordinal); // Rust 1.91's list: AVX-512's registers, which no operand names, as clobbers
        Assert.Contains(",~{xmm31},~{k0},", calls[4], StringComparison.Ordinal);
        Assert.Contains(",~{k7},~{st},~{st(1)},", calls[4], StringComparison.Ordinal);
        Assert.Contains(",~{mm7},~{tmm0},", calls[4], StringComparison.Ordinal);
        Assert.Contains(",~{tmm7},~{dirflag},", calls[4], StringComparison.Ordinal);
        Assert.Contains("\"={ax},={cx},={dx},={r8},={r9},={r10},={r11},={xmm0},", calls[5], StringComparison.Ordinal); // no rsi, no rdi
        Assert.Contains(",={xmm5},={xmm6},", calls[5], StringComparison.Ordinal); // xmm6 to xmm15 too, whose upper halves no callee keeps
        Assert.Contains(",={xmm15},~{xmm16},", calls[5], StringComparison.Ordinal);

        const string naked = "long Nine() => 9\n\nnaked long Forward() {\n    asm(\"jmp {f}\", f = sym Nine)\n}\n\nprint(Forward())\n";
        const string symbol = @"\\22[^\\""]*\bForward\b[^\\""]*\\22"; // the function's symbol, quoted as LLVM writes a quote in a string
        foreach (var (target, typed) in new[] { (Target.Linux, $@"module asm ""\.type {symbol},@function"""), (Target.Windows, $@"module asm ""\.def {symbol}""") })
        {
            var emitted = Compile(naked, Builder.CheckRuntime(target), testBuild: true, "raw").Program;
            Assert.True(emitted is not null, $"no program for {target}");
            var ir = emitted.Ir.ReplaceLineEndings("\n"); // the module's lines end as the system's do
            var module = string.Join("\n", ir.Split('\n').Where(l => l.StartsWith("module asm ", StringComparison.Ordinal) || l.StartsWith("@llvm.compiler.used", StringComparison.Ordinal)));
            Assert.True(Regex.IsMatch(module, typed), $"{target}:\n{module}");
            Assert.Matches($@"module asm ""{symbol}:""", module);
            Assert.Contains("module asm \".p2align 4\"", module, StringComparison.Ordinal);
            Assert.Contains("module asm \".popsection\"", module, StringComparison.Ordinal); // the section the module's assembly was in, given back
            Assert.Contains(target == Target.Windows ? "module asm \".pushsection .text,\\22xr\\22\"" : "module asm \".pushsection .text\"", module, StringComparison.Ordinal);
            Assert.Matches(@"module asm ""jmp \\22[^\\""]*\bNine\b[^\\""]*\\22""", module); // the `sym` as the symbol it names
            Assert.Matches(@"@llvm\.compiler\.used = appending global \[1 x ptr\] \[ptr @""[^""]*\bNine\b[^""]*""\]", module);
            Assert.Matches(@"(?m)^declare dso_local i64 @""[^""]*\bForward\b[^""]*""\(\)$", ir);
            Assert.DoesNotMatch(@"(?m)^define [^\n]*@""[^""]*\bForward\b", ir); // no body of LLVM's, so no prologue
        }
    }

    [Fact]
    public void White_space_after_the_operand_of_a_placeholder_is_set_aside_as_Rusts_parser_sets_it_aside()
    {
        // R11 (assumed: Rust's reading of a template, as section 3 marks it): `{ }` takes the operands without a name in their order, `{0 }` the
        // first and `{o }` the one named `o`, as `{}`, `{0}` and `{o}` do, as Rust's parser sets white space aside after the operand
        // (rustc_parse_format's `argument`); the program the emitter writes from them runs and prints twice its input
        const string text = "long a = 1\nmut long b = 0\nraw {\n    asm(\"lea {o }, [{ } + {0 }]\", in(reg) a, o = out(reg) b)\n}\nprint(b)\n";
        Ran(BuildAndRun(text, "asm-white-space", "raw"), 0, ["2"], "");
    }

    [Fact]
    public void White_space_at_the_end_of_a_modifier_is_set_aside_and_an_empty_modifier_is_none_as_Rusts_parser_reads_them()
    {
        // R11 (assumed: Rust's reading of a template, as section 3 marks it): `{o:e }` is `{o:e}`, the low 32 bits of `o`'s register, as
        // Rust's parser sets white space aside before the `}` (rustc_parse_format's `next`), and `{0:}` and `{0: }` are `{0}`, the 64 bits
        // of the first operand's, as an empty modifier is none (rustc_builtin_macros' `expand_preparsed_asm`); the program the emitter
        // writes from them runs and prints the low 32 bits of twice its input, 2 of 2^32 + 2, where all 64 would print 4294967298
        const string text = "long a = 2147483649\nmut long b = 0\nraw {\n    asm(\"lea {o:e }, [{0:} + {0: }]\", in(reg) a, o = out(reg) b)\n}\nprint(b)\n";
        Ran(BuildAndRun(text, "asm-modifier-white-space", "raw"), 0, ["2"], "");
    }

    [Fact]
    public void On_an_arm_machine_a_naked_function_is_assembly_of_the_module_and_an_asm_is_not_supported_at_its_line()
    {
        // R12 on 64-bit Arm (the owner, 2026-11-15, #519, comment 6892812780, Q2, "Rust: AArch64 switch now"): a `naked` function is the
        // module's assembly there as on x86-64, in the one syntax Arm's assembler reads, on Linux typed `%function`, since `@` begins a
        // comment in it, and on Windows, whose module is COFF, named by `.def` and typed `.type 32`, as on x86-64; on an Arm machine it runs. R11: an `asm` statement, whose operands the checker reads for x86-64 alone, gets one `not
        // supported` at its line in a build for Arm and no assembly of x86-64 in a module of Arm; the same program builds for x86-64
        // (the control)
        const string asm = "mut long n = 1\nraw {\n    asm(\"add {r}, 1\", r = inout(reg) n)\n}\nprint(n)\n";
        var refused = Builder.Compile(SourceText.FromString("program.kz", asm), ["raw"], toolchain.For(Cpu.ArmBaseline), testBuild: true, cpu: () => Cpu.ArmBaseline);
        Assert.Null(refused.Program);
        var d = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 3), (d.Kind, d.Line));
        Assert.NotNull(Builder.Compile(SourceText.FromString("program.kz", asm), ["raw"], toolchain.For(Cpu.Baseline), testBuild: true, cpu: () => Cpu.Baseline).Program); // x86-64's baseline, whatever machine runs the test

        const string naked = "long Two() => 2\n\nnaked long Seven() {\n    asm(\"mov x0, #7\", \"ret\")\n}\n\nprint(Seven())\n";
        var built = Builder.Compile(SourceText.FromString("program.kz", naked), ["raw"], toolchain.For(Cpu.ArmBaseline), testBuild: true, cpu: () => Cpu.ArmBaseline);
        Assert.True(built.Program is not null, string.Join("; ", built.Diagnostics.Sorted));
        var module = string.Join("\n", built.Program.Ir.ReplaceLineEndings("\n").Split('\n').Where(l => l.StartsWith("module asm ", StringComparison.Ordinal)));
        Assert.Matches(OperatingSystem.IsWindows() ? @"module asm ""\.def \\22[^\\""]*\bSeven\b[^\\""]*\\22""\nmodule asm ""\.scl 3""\nmodule asm ""\.type 32""" : @"module asm ""\.type \\22[^\\""]*\bSeven\b[^\\""]*\\22,%function""", module); // the module of the host's system on Arm
        Assert.Contains("module asm \"mov x0, #7\"", module, StringComparison.Ordinal);
        Assert.DoesNotContain("_syntax", module, StringComparison.Ordinal);
        if (Builder.HostCpu.X86)
        {
            return; // the run, on an Arm machine alone
        }

        Ran(Toolchain.Run(toolchain.Build(built.Program, "naked-arm")), 0, ["7"], "");
    }

    [Fact]
    public void The_switch_of_every_target_and_architecture_is_assembled_by_clang_for_its_machine()
    {
        // section 2, the switch of a thread to an actor's stack, a `naked` function of each architecture's directory (round 20, "Dir per
        // arch"; the owner, 2026-11-15, #519, comment 6892812780, Q1 and Q2): the program of each pair is emitted on this machine and
        // compiled into an object by clang for the pair's machine, so that the assembler of each reads its switch, whose last load from
        // the save area the module holds
        var counter = "actor Counter {\n    mut long count = 0\n\n    pub void Add(long n) {\n        count = count + n\n    }\n\n    pub long Get() => count\n}\n\nc = spawn Counter()\nsend c.Add(2)\nprint(c.Get())\n";
        foreach (var (target, arch, triple, last) in new[]
        {
            (Target.Linux, Arch.X64, "x86_64-unknown-linux-gnu", "mov r15, qword ptr [rsi + 48]"),
            (Target.Windows, Arch.X64, "x86_64-pc-windows-msvc", "mov r15, qword ptr [rdx + 64]"),
            (Target.Linux, Arch.Arm64, "aarch64-unknown-linux-gnu", "ldr x16, [x1, #104]"),
            (Target.Windows, Arch.Arm64, "aarch64-pc-windows-msvc", "ldr x16, [x1, #104]"),
        })
        {
            var cpu = arch == Arch.X64 ? Cpu.Baseline : Cpu.ArmBaseline;
            var compilation = Builder.Compile(SourceText.FromString("program.kz", counter), [], Builder.CheckRuntime(target, arch), true, cpu: () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var ir = compilation.Program.Ir;
            Assert.Contains($"module asm \"{last}\"", ir, StringComparison.Ordinal);
            var directory = Path.Combine(toolchain.Directory, $"switch-{triple}");
            Directory.CreateDirectory(directory);
            var input = Path.Combine(directory, "program.ll");
            File.WriteAllText(input, ir);
            var (code, _, error) = Processes.Capture(toolchain.Clang.Path, [$"--target={triple}", "-O0", "-c", input, "-o", Path.Combine(directory, "program.o")], null, TimeSpan.FromMinutes(2));
            Assert.True(code == 0, $"{triple}: {error}");
        }
    }

    [Fact]
    public void The_lock_word_s_wait_and_wake_call_futex_by_its_number_on_each_architecture_and_windows_waits_on_the_address()
    {
        // section 2, round 20, "Dir per arch": `futex` is 202 on x86-64, and 98 on AArch64, where 202 is `accept`, so each Linux
        // architecture's file gives the lock word's wait and wake with its own number, which every program reaches through the lock of
        // its streams (`Print`): the wait by `FUTEX_WAIT_PRIVATE` (128) while the word holds the value the lock gives it, 2, with no time
        // limit, and the wake of one by `FUTEX_WAKE_PRIVATE` (129); Windows waits by `WaitOnAddress` on the 4 bytes of the word with no
        // time limit (`INFINITE`) and wakes by `WakeByAddressSingle`, as Rust's futex does there
        const string windowsWait = @"@WaitOnAddress\(ptr %[\w.]+, ptr %[\w.]+, i64 4, i32 -1\)";
        foreach (var (target, arch, wait, wake) in new[]
        {
            (Target.Linux, Arch.X64, @"@syscall\(i64 202, ptr %[\w.]+, i64 128, i64 %[\w.]+, i64 0\)", @"@syscall\(i64 202, ptr %[\w.]+, i64 129, i64 1\)"),
            (Target.Linux, Arch.Arm64, @"@syscall\(i64 98, ptr %[\w.]+, i64 128, i64 %[\w.]+, i64 0\)", @"@syscall\(i64 98, ptr %[\w.]+, i64 129, i64 1\)"),
            (Target.Windows, Arch.X64, windowsWait, @"@WakeByAddressSingle\(ptr %"),
            (Target.Windows, Arch.Arm64, windowsWait, @"@WakeByAddressSingle\(ptr %"),
        })
        {
            var cpu = arch == Arch.X64 ? Cpu.Baseline : Cpu.ArmBaseline;
            var compilation = Builder.Compile(SourceText.FromString("program.kz", "print(1)\n"), [], Builder.CheckRuntime(target, arch), true, cpu: () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var ir = compilation.Program.Ir;
            foreach (var (function, call) in new[] { ("LockWait", wait), ("LockWake", wake) })
            {
                var body = Regex.Match(ir, $@"^define [^\n]*@""?rt\.sys\.{function}\b[^\n]*\n(.*?)\n\}}", RegexOptions.Multiline | RegexOptions.Singleline);
                Assert.True(body.Success, $"{target} {arch}: no `{function}` in the module");
                Assert.True(Regex.IsMatch(body.Groups[1].Value, call), $"{target} {arch}: no `{call}` in `{function}`:\n{body.Groups[1].Value}");
            }

            // the value the wait sleeps on, which the lock gives it: 2, taken with a thread that may sleep (`Lock`)
            var locked = Regex.Match(ir, @"^define [^\n]*@""rt\.common\.Lock\.[0-9]+""\([^\n]*\n(.*?)\n\}", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.True(locked.Success, $"{target} {arch}: no `Lock` in the module");
            Assert.True(Regex.IsMatch(locked.Groups[1].Value, @"@""?rt\.sys\.LockWait\b[^\n(]*\(ptr %[\w.]+, i32 2\)"), $"{target} {arch}: `Lock` waits on no 2:\n{locked.Groups[1].Value}");
            Assert.Equal(target == Target.Linux ? 2 : 0, Regex.Count(ir, @"@syscall\(i64 (202|98), ")); // no other number of the two
        }
    }

    [Fact]
    public void A_directory_s_creation_reads_st_mode_at_the_byte_of_its_architecture_s_struct_stat()
    {
        // S8, section 2, round 20, "Dir per arch": `Directory.Create` takes a directory that is there by the type in `st_mode`, at byte
        // 24 of x86-64's `struct stat` and at byte 16 of AArch64's, the kernel's generic one, where 24 is `st_uid`; each Linux
        // architecture's file gives the byte (`StatModeAt`), the emitted call passes it to `CreateDirectories`, and `IsDirectory` reads
        // the mode at the byte it was given
        const string text = "void | IoError Make() {\n    Directory.Create(\"made\")\n}\n\nMake() else {\n    IoError e => print(e)\n}\n";
        foreach (var (arch, at) in new[] { (Arch.X64, 24), (Arch.Arm64, 16) })
        {
            var cpu = arch == Arch.X64 ? Cpu.Baseline : Cpu.ArmBaseline;
            var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], Builder.CheckRuntime(Target.Linux, arch), true, cpu: () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var ir = compilation.Program.Ir.ReplaceLineEndings("\n");
            string Body(string function)
            {
                var defined = Regex.Match(ir, $@"^define [^\n]*@""?{function}\b[^\n]*\n(.*?)\n\}}", RegexOptions.Multiline | RegexOptions.Singleline);
                Assert.True(defined.Success, $"{arch}: no `{function}` in the module");
                return defined.Groups[1].Value;
            }

            var given = Body(@"rt\.sys\.StatModeAt");
            Assert.True(Regex.IsMatch(given, $@"\bi64 {at}\b") && !Regex.IsMatch(given, $@"\bi64 {40 - at}\b"), $"{arch}: `StatModeAt` gives no {at}:\n{given}");
            var made = Body(@"f\.Make\.[0-9]+");
            Assert.True(Regex.IsMatch(made, @"(%[\w.]+) = call i64 @""?rt\.sys\.StatModeAt\b[^\n(]*\(\)[^\n]*\n(?:[^\n]*\n)*?[^\n]*call i64 @""?rt\.sys\.CreateDirectories\b[^\n(]*\(ptr %[\w.]+, i64 %[\w.]+, i64 \1\)"),
                $"{arch}: `CreateDirectories` is not given what `StatModeAt` gave:\n{made}");
            // the mode loaded from the pointer the parameter's own load offsets, since a literal offset is a value too (`sext i32 24
            // to i64`), so that no operand of a pointer's offset is a constant
            var read = Body(@"rt\.sys\.IsDirectory");
            Assert.True(Regex.IsMatch(read, @"store i64 %p1, ptr (%[\w.]+)\b[^\n]*\n(?:[^\n]*\n)*?[^\n]*?(%[\w.]+) = load i64, ptr \1\b[^\n]*\n(?:[^\n]*\n)*?[^\n]*?(%[\w.]+) = getelementptr i8, ptr %[\w.]+, i64 \2\b[^\n]*\n(?:[^\n]*\n)*?[^\n]*load i32, ptr \3\b"),
                $"{arch}: `IsDirectory` reads `st_mode` at no byte its parameter gave:\n{read}");
        }
    }

    [Fact]
    public void A_function_of_sys_that_neither_file_or_both_give_is_named_with_both_files()
    {
        // round 20, "Dir per arch": a call on a file finds the runtime's function in the target's file or in the architecture's, which the
        // folder `sys` reads beside it (`SysFunction`); where neither gives it, the error names both files and the function, and where both
        // do, which the runtime's check refuses as `redeclared` (V9), it names each declaration by its file and line, where `.Single` named
        // neither
        const string linux = "runtime/linux/linux.kz", x64 = "runtime/linux/x86-64/x86-64.kz";
        var program = SourceText.FromString("program.kz", "print(Environment.Args().Count)\n");
        CheckedRuntime Edited(string path, Func<string, string> edit, List<string> errors)
        {
            var files = Runtime.Files.Select(f => f.Path == path ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(edit(Encoding.UTF8.GetString(f.Bytes).ReplaceLineEndings("\n")))) : f).ToList();
            Assert.NotEqual(Runtime.Files.Select(f => f.Bytes), files.Select(f => f.Bytes)); // the file did change
            return Runtime.Check(files, Target.Linux, Arch.X64, errors);
        }

        Assert.NotNull(Builder.Compile(program, [], Builder.CheckRuntime(Target.Linux, Arch.X64), true, cpu: () => Cpu.Baseline).Program); // the positive control

        var errors = new List<string>();
        var missing = Edited(linux, t => t.Replace("pub int Arguments(", "int Arguments(", StringComparison.Ordinal), errors);
        Assert.Empty(errors); // only a program's call reaches `Arguments`, so that the check finds nothing missing
        var none = Assert.Throws<InvalidOperationException>(() => Builder.Compile(program, [], missing, true, cpu: () => Cpu.Baseline));
        Assert.True(none.Message.Contains(linux, StringComparison.Ordinal) && none.Message.Contains(x64, StringComparison.Ordinal) && none.Message.Contains("`Arguments`", StringComparison.Ordinal), none.Message);

        var declared = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == linux).Bytes).ReplaceLineEndings("\n").Split('\n').ToList().FindIndex(l => l.StartsWith("pub int Arguments(", StringComparison.Ordinal)) + 1;
        var twice = Edited(x64, t => t + "\npub int Arguments(int skip) => skip\n", errors);
        var again = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == x64).Bytes).ReplaceLineEndings("\n").Split('\n').Length + 1;
        Assert.Contains(errors, e => e.StartsWith($"{x64}:{again}: redeclared: ", StringComparison.Ordinal)); // the check refuses it
        var both = Assert.Throws<InvalidOperationException>(() => Builder.Compile(program, [], twice, true, cpu: () => Cpu.Baseline));
        Assert.True(declared > 0 && both.Message.Contains($"{linux}:{declared}", StringComparison.Ordinal) && both.Message.Contains($"{x64}:{again}", StringComparison.Ordinal) && both.Message.Contains("`Arguments`", StringComparison.Ordinal), both.Message);
    }

    [Fact]
    public void Random_s_words_on_linux_are_each_thread_s_own()
    {
        // section 6 (assumed: each thread's own, as the `rand` crate's `ThreadRng` is one per thread, not checked at source): the two flags and the descriptor `Random` keeps
        // on Linux are the thread's local storage, since an atomic view takes the address of its word (R19) and no `static` gives one
        foreach (var arch in new[] { Arch.X64, Arch.Arm64 })
        {
            var cpu = arch == Arch.X64 ? Cpu.Baseline : Cpu.ArmBaseline;
            var compilation = Builder.Compile(SourceText.FromString("program.kz", "print(1)\n"), [], Builder.CheckRuntime(Target.Linux, arch), true, cpu: () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            foreach (var name in new[] { "insecureTaken", "getrandomRefused", "urandom" })
            {
                Assert.Matches($@"@""s\.[\w.]+\.{name}"" = internal thread_local global ", compilation.Program.Ir);
            }
        }
    }

    [Fact]
    public void A_build_refuses_a_runtime_of_another_architecture_than_its_cpus()
    {
        // round 20, "Dir per arch": a program for one architecture calls that architecture's switch, so a runtime checked for the other
        // is refused and not emitted, by the emitter, which `Compile` calls; the command reads `--cpu` for the machine's own
        // architecture, and a caller of the API names both
        var source = SourceText.FromString("program.kz", "print(1)\n");
        foreach (var (arch, own, other) in new[] { (Arch.X64, Cpu.Baseline, Cpu.ArmBaseline), (Arch.Arm64, Cpu.ArmBaseline, Cpu.Baseline) })
        {
            var runtime = Builder.CheckRuntime(Target.Linux, arch);
            Assert.NotNull(Builder.Compile(source, [], runtime, testBuild: true, cpu: () => own).Program); // the positive control
            Assert.Throws<InvalidOperationException>(() => Builder.Compile(source, [], runtime, testBuild: true, cpu: () => other));
        }
    }

    [Fact]
    public void An_instruction_the_assembler_refuses_is_asm_refused_at_the_line_of_its_asm()
    {
        // R11, R12 (the owner, 2026-11-13, "Rust's srcloc", for the error at the line of the `asm`, and pull request 530, comment
        // 6855725580, "LLVM's order everywhere", for the order): clang's text for IR names no line of the program, so where it refuses
        // inline assembly the build assembles the program's `asm`s together in the order LLVM meets them, a `naked` function's
        // module-level assembly before every function, each with those before it that clang took, and each one whose addition clang
        // refuses is `asm-refused` at its line, an inline one and a `naked` function's, in that order, each under its rule; one that
        // assembles is named by none, and the program without the refused instructions builds (the controls). Of two `naked` functions
        // that define one label and are called in the order of their lines, the second is named, whose label clang refuses beside the
        // first's, and an `asm` between them that assembles is named by none; an inline `asm` defines no named label (R11, the owner,
        // 2026-11-13, "Refuse as Rust")
        const string program = """
            mut long n = 1
            raw {
                asm("add {r}, 1", r = inout(reg) n)
                asm("frobnicate {r}", r = inout(reg) n)
            }

            naked long Seven() {
                asm("mov eax, 7", "frobnicate eax", "ret")
            }

            print(n)
            print(Seven())
            """;
        var compiled = Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!;
        Assert.Equal([(8, "R12"), (3, "R11"), (4, "R11")], compiled.Asms.Select(a => (a.Line, a.Rule)).ToList());
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(compiled, "asm-refused"));
        Assert.Equal([(ErrorIds.AsmRefused, 8), (ErrorIds.AsmRefused, 4)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program.Replace("frobnicate", "inc", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw").Program!, "asm-assembled")));
        const string twice = "naked long One() {\n    asm(\"twice:\", \"mov eax, 1\", \"ret\")\n}\n\nnaked long Two() {\n    asm(\"mov eax, 2\", \"ret\")\n}\n\n"
            + "naked long Three() {\n    asm(\"twice:\", \"mov eax, 3\", \"ret\")\n}\n\nprint(One() + Two() + Three())\n";
        refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(twice, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-twice"));
        Assert.Equal([(ErrorIds.AsmRefused, 10)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void Of_two_naked_functions_that_define_one_label_the_build_names_the_one_LLVM_refuses_in_the_order_of_their_calls()
    {
        // R12 (the owner, 2026-11-13, pull request 530, comment 6855725580, "LLVM's order everywhere"): the compiler writes the functions
        // in the order their first uses are reached (R11), and LLVM writes the `naked` functions' module-level assembly in that order, so of two that define one label and
        // are called out of the order of their lines, LLVM refuses the label of the earlier line, the one it meets second, and the build
        // names that line
        const string program = """
            naked long One() {
                asm("twice:", "mov eax, 1", "ret")
            }

            naked long Two() {
                asm("mov eax, 2", "ret")
            }

            naked long Three() {
                asm("twice:", "mov eax, 3", "ret")
            }

            print(Three() + Two() + One())
            """;
        var compiled = Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!;
        Assert.Equal([10, 6, 2], compiled.Asms.Select(a => a.Line).ToList());
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(compiled, "asm-twice-out-of-order"));
        Assert.Equal([(ErrorIds.AsmRefused, 2)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void Of_two_asm_statements_that_define_one_symbol_the_build_names_the_one_LLVM_refuses_in_the_order_of_their_functions()
    {
        // R11, R18 (the owner, 2026-11-13, pull request 530, comment 6855725580, "LLVM's order everywhere"): the compiler writes a guarded
        // block's function before the function the block stands in, so LLVM meets the `asm` of the block before the function's own `asm`
        // of an earlier line and refuses the symbol of the earlier line, and the build names that line; `.equiv` defines a symbol that
        // is no label (named-asm-label), and the program with one of the two builds (the control)
        const string program = """
            mut long n = 1
            raw {
                asm(".equiv twice, 1", "add {r}, 1", r = inout(reg) n)
            }
            if X86.Sse41.IsSupported {
                raw {
                    asm(".equiv twice, 2", "add {r}, 2", r = inout(reg) n)
                }
            }
            print(n)
            """;
        var compiled = Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!;
        Assert.Equal([7, 3], compiled.Asms.Select(a => a.Line).ToList());
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(compiled, "asm-symbol-twice"));
        Assert.Equal([(ErrorIds.AsmRefused, 3)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program.Replace(".equiv twice, 2", ".equiv once, 2", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw").Program!, "asm-symbol-once")));
    }

    [Fact]
    public void The_copies_the_build_assembles_define_no_symbol_the_program_does_not()
    {
        // R11, R12: the build writes the calls of the `asm` statements in the copies' `main`, which every program defines, in place of its
        // label, so no symbol of the program, a `naked` function's label or a symbol an `asm` statement's directive defines, meets a
        // function only the copies define: the build names the one `asm` the assembler refuses and no other, the copy it took before it,
        // the second, assembles whole, as no symbol is defined twice, and the program without the refused one builds (the control)
        const string program = """
            naked long Seven() {
                asm("kz.asm1:", "mov eax, 7", "ret")
            }

            mut long n = 1
            raw {
                asm(".equiv kz.asm0, 1", "add {r}, 1", r = inout(reg) n)
                asm("frobnicate {r}", r = inout(reg) n)
            }
            print(n)
            print(Seven())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-copies-names"));
        Assert.Equal([(ErrorIds.AsmRefused, 8)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(Path.Combine(toolchain.Directory, "asm-copies-names", "asm1.o")));
        Assert.True(File.Exists(toolchain.Build(Compile(program.Replace("frobnicate", "inc", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw").Program!, "asm-copies-names-built")));
    }

    [Fact]
    public void A_label_of_a_naked_function_that_is_a_symbol_of_the_program_is_asm_refused_at_the_line_of_its_asm()
    {
        // R12, R11: LLVM writes a `naked` function's module-level assembly before the program's own definitions, and refuses a label of it
        // that is a symbol the program defines where it writes that definition, with no line of the template, so the build assembles the
        // copies beside a label for each symbol the program's IR defines: `main`, which the C library's start calls, `kz.main`, the
        // program's top-level code, and the string constant `.str.0`, private, `.L.str.0` in the assembly; each is refused in its
        // function's text and named at its line, and a label that is no symbol of the program is named by none (the control)
        const string program = """
            naked long One() {
                asm("main:", "mov eax, 1", "ret")
            }

            naked long Two() {
                asm("kz.main:", "mov eax, 2", "ret")
            }

            naked long Three() {
                asm(".L.str.0:", "mov eax, 3", "ret")
            }

            naked long Four() {
                asm("done:", "mov eax, 4", "ret")
            }

            print(One() + Two() + Three() + Four())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-symbol"));
        Assert.Equal([(ErrorIds.AsmRefused, 2), (ErrorIds.AsmRefused, 6), (ErrorIds.AsmRefused, 10)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void A_pure_asm_whose_output_nothing_reads_is_asm_refused_in_a_test_build_and_builds_in_a_release_build()
    {
        // R11 (the owner, 2026-11-13, "As Rust, builds differ"): an `asm` the optimiser drops before the assembler reads it raises
        // nothing, so a `pure` one whose output nothing reads, in a function, is `asm-refused` at its line in a test build, whose store into
        // the variable keeps the call, and builds in a release build, whose optimiser drops the store and the call, as Rust's dev and
        // release profiles hand LLVM the same call; the variable is read before the `asm` alone, as V5 refuses one never read
        const string program = """
            long Zero() {
                mut long n = 0
                long kept = n
                raw {
                    asm("frobnicate {r}", r = out(reg) n, options(pure, nomem))
                }
                return kept
            }

            print(Zero())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-dropped-test"));
        Assert.Equal([(ErrorIds.AsmRefused, 5)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-dropped-release")));
    }

    [Fact]
    public void A_pure_asm_the_assembler_refuses_is_asm_refused_at_its_line_in_a_test_build_and_a_release_build()
    {
        // R11: the call of a `pure` `asm` has no effect, and LLVM drops a call of no effect whose value nothing uses, at -O0 too, so the
        // copy the build assembles to find a refused `asm` has an effect whatever its options; the program's own call has its value used
        const string program = """
            mut long n = 1
            mut long m = 2
            raw {
                asm("frobnicate {r}", r = inout(reg) n, options(pure, nomem))
                asm("frobnicate {r}", r = inout(reg) m, options(pure, readonly))
            }
            print(n)
            print(m)
            """;
        foreach (var testBuild in new[] { true, false })
        {
            var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild, "raw").Program!, testBuild ? "asm-pure-test" : "asm-pure-release"));
            Assert.Equal([(ErrorIds.AsmRefused, 4), (ErrorIds.AsmRefused, 5)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        }
    }

    [Fact]
    public void An_asm_the_optimiser_of_a_release_build_copies_is_asm_refused_at_its_line_where_the_assembler_refuses_a_copy()
    {
        // R11 (the owner, 2026-11-13, pull request 532, comment 6860019854, "Copies from optimised IR"): the optimiser of a release build
        // inlines `Bump` into its two callers, so the module defines the symbol of `.equiv k, 1` twice and clang refuses it; the build reads
        // the calls its code generator holds by the mark on each, its line, and assembles a copy of each, so the second is refused and
        // named at the line of the `asm`, once; a test build, which keeps one call, builds the program (the control)
        const string program = """
            long Bump(long x) {
                mut long n = x
                raw {
                    asm(".equiv k, 1", "add {r}, 1", r = inout(reg) n)
                }
                return n
            }

            long One() => Bump(1)

            long Two() => Bump(2)

            print(One() + Two())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-copied-release"));
        Assert.Equal([(ErrorIds.AsmRefused, 4)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-copied-test")));
    }

    [Fact]
    public void An_asm_the_code_generator_of_a_release_build_copies_is_asm_refused_at_its_line_where_the_assembler_refuses_a_copy()
    {
        // R11 (the owner, 2026-11-13, pull request 532, comment 6860019854, "Copies from optimised IR", for the line named in a release
        // build, the one LLVM refuses): the optimised module of a release build holds one call of `.equiv k, 1`, and LLVM's code generator
        // duplicates the block that holds it, after the inner `if`, into the two blocks before it, so clang refuses the second copy; the
        // build reads the calls the code generator holds once it placed the blocks, by the mark each copy keeps (assumed: the build's
        // mechanism, as section 3 marks it), so the second is refused and named at the line of the `asm`, once; a test build, which
        // copies no block, builds the program (the control)
        const string program = """
            void Pick(bool a, bool b) {
                if a {
                    if b { print(1) } else { print("two") }
                    raw { asm(".equiv k, 1") }
                } else {
                    print(3.5)
                }
                print(4)
            }

            Pick(true, false)
            Pick(true, true)
            Pick(false, true)
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-generated-release"));
        Assert.Equal([(ErrorIds.AsmRefused, 4)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-generated-test")));
    }

    [Fact]
    public void A_release_build_names_no_asm_its_optimiser_dropped_beside_one_the_assembler_refuses()
    {
        // R11 (the owner, 2026-11-13, pull request 530, comment 6854386584, "As Rust, builds differ", and pull request 532, comment
        // 6860019854, "Copies from optimised IR"): where clang refuses a release build, the build assembles copies of the calls its code
        // generator holds alone, so the `pure` `asm` whose output nothing reads, which the optimiser dropped, raises nothing beside
        // the `asm` of the top-level code the assembler refuses; a test build's copies, taken as the compiler wrote the program, hold both
        // calls and name both, in the order LLVM meets them, the top-level code's function first (the control)
        const string program = """
            long Zero() {
                mut long n = 0
                long kept = n
                raw {
                    asm("frobnicate {r}", r = out(reg) n, options(pure, nomem))
                }
                return kept
            }

            raw {
                asm("frobnicate")
            }
            print(Zero())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-held-release"));
        Assert.Equal([(ErrorIds.AsmRefused, 11)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, "asm-held-test"));
        Assert.Equal([(ErrorIds.AsmRefused, 11), (ErrorIds.AsmRefused, 5)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void Of_two_asm_statements_at_one_line_a_release_build_copies_each_call_as_its_own_asm()
    {
        // R11 (the owner, 2026-11-13, pull request 532, comment 6860019854, "Copies from optimised IR"): the mark of a call is its line,
        // and two `asm`s can stand at one line, so the build copies each call the code generator holds as the `asm` of that line whose
        // template the call has: the one the assembler refuses is named, and the one beside it that it takes is no copy
        // of it (the control: the program without the refused instruction builds)
        const string program = """
            long Pick(bool c) {
                if c { raw { asm("nop") } } else { raw { asm("frobnicate") } }
                return 1
            }

            print(Pick(true) + Pick(false))
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-one-line-release"));
        Assert.Equal([(ErrorIds.AsmRefused, 2)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Assert.True(File.Exists(toolchain.Build(Compile(program.Replace("frobnicate", "pause", StringComparison.Ordinal), toolchain.Runtime, testBuild: false, "raw").Program!, "asm-one-line-built")));
    }

    [Fact]
    public void Copies_of_one_asm_the_assembler_refuses_with_two_texts_are_two_errors_at_its_line()
    {
        // R11 (assumed: Rust's, whose compiler reports identical diagnostics once, as section 3 marks it): the optimiser of a release
        // build inlines `Bump` into its two callers, and the build assembles the copies in the order LLVM meets them, the first, then
        // the `asm` of `Two`, which defines `j`, then the second; the assembler refuses the first for `frobnicate` and the second for
        // the `.err` the `.ifdef j` before it reaches, beside `frobnicate`, whose message comes after the one of `.err`, the first the
        // build compares, two messages, so the line of the `asm` is named twice; with `nop` in place of `.equiv j, 1` it refuses both
        // copies with one message, which are one error at that line (the control)
        const string program = """
            long Bump(long x) {
                mut long n = x
                raw {
                    asm(".ifdef j", ".err", ".endif", "frobnicate", "add {r}, 1", r = inout(reg) n)
                }
                return n
            }

            long One() => Bump(1)

            long Two() {
                raw { asm(".equiv j, 1") }
                return Bump(2)
            }

            print(One() + Two())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: false, "raw").Program!, "asm-two-texts"));
        Assert.Equal([(ErrorIds.AsmRefused, 4), (ErrorIds.AsmRefused, 4)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program.Replace(".equiv j, 1", "nop", StringComparison.Ordinal), toolchain.Runtime, testBuild: false, "raw").Program!, "asm-one-text"));
        Assert.Equal([(ErrorIds.AsmRefused, 4)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void A_global_the_IR_declares_and_does_not_define_takes_no_label_beside_the_copies()
    {
        // R12: the copies hold a label for each symbol the program's IR defines, and a global the IR declares, `external` or `extern_weak`,
        // defines none, so where clang refuses another `asm` the label of a `naked` function of that name is named by none; the program
        // whose `asm` the assembler takes builds beside the same declarations (the control: clang takes both labels)
        const string program = """
            naked long Seven() {
                asm("declared:", "mov eax, 7", "ret")
            }

            naked long Eight() {
                asm("weakly:", "mov eax, 8", "ret")
            }

            raw {
                asm("frobnicate")
            }
            print(Seven() + Eight())
            """;
        const string declarations = "@declared = external global i64\n@weakly = extern_weak global i64\n";
        var compiled = Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(compiled with { Ir = compiled.Ir + declarations }, "asm-declared"));
        Assert.Equal([(ErrorIds.AsmRefused, 10)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        var built = Compile(program.Replace("frobnicate", "nop", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw").Program!;
        Assert.True(File.Exists(toolchain.Build(built with { Ir = built.Ir + declarations }, "asm-declared-built")));
    }

    [Fact]
    public void A_C_function_the_program_declares_takes_no_label_beside_the_copies()
    {
        // R12 (assumed: Rust's way, as section 3 marks it): a label of a `naked` function's `asm` that is a symbol the program defines is
        // `asm-refused`, and a C function the program declares (R16) is a `declare` line of the IR, no definition, beside which the
        // assembler takes a label of its name, so where clang refuses another `asm` the label `abs:` is named by none; the program whose
        // `asm` the assembler takes builds beside the same declaration (the control: clang takes the label), and its call of `abs`, which
        // the assembler resolves in the one object a test build writes, runs the label's code and prints 7, where the C library's
        // `abs(-3)` is 3: a misuse of `raw` code, which no compiler checks (assumed: Rust's way, as section 3 marks it)
        const string program = """
            extern "C" {
                int abs(int n)
            }

            naked long Seven() {
                asm("abs:", "mov eax, 7", "ret")
            }

            raw {
                asm("frobnicate")
                print(abs(-3))
            }
            print(Seven())
            """;
        var refused = Assert.Throws<AsmRefusedException>(() => toolchain.Build(Compile(program, toolchain.Runtime, testBuild: true, "raw", "native").Program!, "asm-c-declared"));
        Assert.Equal([(ErrorIds.AsmRefused, 10)], refused.Refused.Select(d => (d.Id, d.Line)).ToList());
        Ran(Toolchain.Run(toolchain.Build(Compile(program.Replace("frobnicate", "nop", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw", "native").Program!, "asm-c-declared-built")), 0, ["7", "7"], "");
    }

    [Fact]
    public void The_command_writes_each_asm_the_assembler_refuses_as_a_compile_error_at_its_line()
    {
        // R11, R12: a build the assembler refuses reaches the command's error stream as its other compile errors do,
        // `<file>:<line>: <id>: <text>`, one line for each refused `asm` in the order LLVM meets them, and not as clang's failure; a program
        // clang builds writes nothing (the control)
        const string program = """
            mut long n = 1
            raw {
                asm("add {r}, 1", r = inout(reg) n)
                asm("frobnicate {r}", r = inout(reg) n)
            }

            naked long Seven() {
                asm("mov eax, 7", "frobnicate eax", "ret")
            }

            print(n)
            print(Seven())
            """;
        var directory = Path.Combine(toolchain.Directory, "asm-command");
        using var error = new StringWriter();
        Assert.False(Command.Built(Compile(program, toolchain.Runtime, testBuild: true, "raw").Program!, toolchain.Clang, directory, Path.Combine(directory, OperatingSystem.IsWindows() ? "refused.exe" : "refused"), "program.kz", error));
        Assert.Matches(@"^program\.kz:8: asm-refused: [^\n]*\nprogram\.kz:4: asm-refused: [^\n]*\n$", error.ToString());
        using var none = new StringWriter();
        Assert.True(Command.Built(Compile(program.Replace("frobnicate", "inc", StringComparison.Ordinal), toolchain.Runtime, testBuild: true, "raw").Program!, toolchain.Clang, directory, Path.Combine(directory, OperatingSystem.IsWindows() ? "built.exe" : "built"), "program.kz", none));
        Assert.Equal("", none.ToString());
    }

    [Fact]
    public void The_runtime_of_every_target_is_emitted_with_its_own_calls_of_the_system()
    {
        foreach (var (target, call) in new[] { (Target.Linux, "@write("), (Target.Windows, "@WriteFile(") })
        {
            var program = Compile("print(1)\n", Builder.CheckRuntime(target)).Program;
            Assert.True(program is not null, $"no program for {target}");
            Assert.Contains("define i32 @main(i32 %argc, ptr %argv)", program.Ir, StringComparison.Ordinal);
            Assert.Contains(call, program.Ir, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_function_carries_its_unwind_tables_and_on_linux_the_inline_probe_of_a_frame_larger_than_a_page()
    {
        // design record, sections 2 and 6: the unwinder walks from a fault in a prologue, and no frame steps past the guard page; on
        // Windows the backend's `__chkstk` probes, and the attribute there names the function the backend calls
        foreach (var target in new[] { Target.Linux, Target.Windows })
        {
            var program = Compile("int Twice(int n) => n * 2\n\nprint(Twice(2))\n", Builder.CheckRuntime(target)).Program;
            Assert.True(program is not null, $"no program for {target}");
            var defines = program.Ir.Split('\n').Where(l => l.StartsWith("define ", StringComparison.Ordinal)).ToList();
            Assert.True(defines.Count >= 10, $"{target}: {defines.Count} functions, and the program's, the runtime's and the standard library's are more"); // a floor
            Assert.All(defines, d => Assert.Contains(" uwtable", d, StringComparison.Ordinal));
            Assert.All(defines, d => Assert.Equal(target == Target.Linux, d.Contains(" \"probe-stack\"=\"inline-asm\"", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void The_lookup_of_a_map_reads_the_control_bytes_of_a_group_as_one_word()
    {
        // the control bytes of Rust's `HashMap` (hashbrown's SwissTable, its generic groups of 8): the helper loads the group's word,
        // marks the bytes equal to the hash's control byte and takes the first mark by counting trailing zeros
        var program = Compile("mut m = Map<long, long>()\nm[1] = 2\nprint(m[1] ?? 0)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(program is not null, "no program");
        var start = program.Ir.IndexOf("define internal ptr @\"find.", StringComparison.Ordinal);
        Assert.True(start >= 0, "no find helper in the IR");
        var body = program.Ir[start..program.Ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        Assert.Contains(", 72340172838076673", body, StringComparison.Ordinal); // the hash's control byte in each of the 8 bytes
        Assert.Contains(", -9187201950435737472", body, StringComparison.Ordinal); // the high bit of each byte
        Assert.Contains("call i64 @llvm.cttz.i64(", body, StringComparison.Ordinal);
        Assert.Contains("@\"std.MapControl.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_walk_of_a_map_reads_the_control_bytes_of_a_group_as_one_word()
    {
        // hashbrown's `RawIterRange`: the loop over a map, in `kz.main`, and the walk of its text, in the text helper, load a group's
        // word, mark the slots with an entry, whose bytes have the high bit clear, and take the marks lowest first by counting trailing
        // zeros, with no load of a single control byte
        var program = Compile("mut m = Map<int, int>()\nm[1] = 2\nfor k, v in m {\n    print(k + v)\n}\nprint(m)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(program is not null, "no program");
        foreach (var name in new[] { "define internal void @kz.main(", "define internal %kz.str @\"text." })
        {
            var start = program.Ir.IndexOf(name, StringComparison.Ordinal);
            Assert.True(start >= 0, $"no {name} in the IR");
            var body = program.Ir[start..program.Ir.IndexOf("\n}", start, StringComparison.Ordinal)];
            Assert.Matches(@"= xor i64 %[^,]+, -1[,\r\n]", body); // the slots with an entry, the high bit of each byte turned around
            Assert.Contains(", -9187201950435737472", body, StringComparison.Ordinal); // the high bit of each byte
            Assert.Contains("call i64 @llvm.cttz.i64(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("load i8, ptr", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_map_keeps_the_hash_of_a_key_whose_hash_reads_more_than_one_word()
    {
        // `KeepsHash`: a slot of a key of one word holds the key and the value, as hashbrown's bucket, and its growth hashes the key
        // again; a slot of a key of a kind whose hash can read more than one word holds its hash first, as .NET keeps an entry's hash
        // code (issue 157, item 14, under the owner's "Allow, faster wins"): each key type the checker takes, with its layout
        const string program =
            "enum Color { Red, Green }\ndata Pair(string A, string B)\nclass Cell(mut int N)\nclass Spot(int X, int Y)\ninterface Named {\n    string Name()\n}\n\n" +
            "class Tag(string N) : Named {\n    pub string Name() => N\n}\n\nlongduration far = 100000days\n" +
            "mut a = Map<int, int>()\na[1] = 1\nmut b = Map<long, int>()\nb[1] = 1\nmut c = Map<char, int>()\nc['x'] = 1\n" +
            "mut d = Map<bool, int>()\nd[true] = 1\nmut e = Map<Color, int>()\ne[Color.Green] = 1\nmut f = Map<double, int>()\nf[1.5] = 1\n" +
            "mut g = Map<string, int>()\ng[\"x\"] = 1\nmut h = Map<Pair, int>()\nh[Pair(\"a\", \"b\")] = 1\nmut i = Map<List<int>, int>()\ni[[1, 2]] = 1\n" +
            "mut j = Map<duration, int>()\nj[90min] = 1\nmut k = Map<longduration, int>()\nk[far] = 1\nmut l = Map<int?, int>()\nl[null] = 1\n" +
            "mut m = Map<float, int>()\nm[1.5f] = 1\nmut n = Map<Cell, int>()\nn[Cell(1)] = 1\nmut o = Map<Spot, int>()\no[Spot(1, 2)] = 1\n" +
            "mut p = Map<Named, int>()\np[Tag(\"a\")] = 1\n" +
            "print(a.Count + b.Count + c.Count + d.Count + e.Count + f.Count + g.Count + h.Count + i.Count + j.Count + k.Count + l.Count + m.Count + n.Count + o.Count + p.Count)\n";
        var compiled = Compile(program, Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(compiled is not null, "no program");
        string[] layouts =
        [
            "Map<int, int>\" = type { i32, i32 }",
            "Map<long, int>\" = type { i64, i32 }",
            "Map<char, int>\" = type { i32, i32 }",
            "Map<bool, int>\" = type { i1, i32 }",
            "Map<Color, int>\" = type { i8, i32 }",
            "Map<double, int>\" = type { double, i32 }",
            "Map<duration, int>\" = type { i64, i32 }",
            "Map<float, int>\" = type { float, i32 }",
            "Map<Cell, int>\" = type { ptr, i32 }",
            "Map<string, int>\" = type { i64, %kz.str, i32 }",
            "Map<Pair, int>\" = type { i64, %\"d.Pair\", i32 }",
            "Map<List<int>, int>\" = type { i64, ptr, i32 }",
            "Map<longduration, int>\" = type { i64, i128, i32 }",
            "Map<int?, int>\" = type { i64, { i8, [1 x i32] }, i32 }",
            "Map<Spot, int>\" = type { i64, ptr, i32 }",
            "Map<Named, int>\" = type { i64, %kz.obj, i32 }",
        ];
        Assert.All(layouts, layout => Assert.Contains($"\n%\"entry.{layout}", compiled.Ir, StringComparison.Ordinal));
    }

    [Fact]
    public void A_map_hashes_an_actors_reference_by_its_number_and_keeps_no_hash_of_it()
    {
        // U2, U12: a reference is the index of its actor's slot in the runtime's table and its number, two words (section 6, the owner,
        // 2026-11-16, #519, comment 6919351399); its hash reads the number alone, as `==` does, which no other actor of the process has,
        // so that the hashes are those of the number the record held before the table, and a key of one word keeps no hash in its slot
        // (`KeepsHash`; section 6, assumed there). A run cannot tell the index's hash from the number's, which `==` tells apart
        // (ActorTests), so the IR is read
        const string program = "actor Counter {\n    pub int Get() => 1\n}\n\nc = spawn Counter()\nmut m = Map<Counter, int>()\nm[c] = 1\nprint(m.Count)\n";
        var compiled = Compile(program, Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(compiled is not null, "no program");
        Assert.Contains("\n%\"entry.Map<Counter, int>\" = type { %kz.actor, i32 }", compiled.Ir, StringComparison.Ordinal);
        Assert.NotEmpty(Regex.Matches(compiled.Ir, @"(%[\w.]+) = extractvalue %kz\.actor %[\w.]+, 1(, !dbg ![0-9]+)?\r?\n\s+store i64 \1, ptr %v\.key\."));
        Assert.DoesNotMatch(@"(%[\w.]+) = extractvalue %kz\.actor %[\w.]+, 0(, !dbg ![0-9]+)?\r?\n\s+store i64 \1, ptr %v\.key\.", compiled.Ir);
    }

    [Fact]
    public void Remove_finds_the_slot_of_its_key_once_and_takes_the_slot_of_a_copy_at_its_offset()
    {
        // hashbrown's `remove_entry` finds the key's slot once, and so does the helper of `Remove`: where the block it found the slot in
        // was shared and was copied (M3), it takes the copy's slot at the offset the slot has in that block, since the copy holds the
        // block's bytes (`MapCopy`)
        var program = Compile("mut m = Map<long, long>()\nm[1] = 2\nprint(m.Remove(1) ?? 0)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(program is not null, "no program");
        var lines = program.Ir.Split('\n').Select(l => l.TrimEnd('\r')).ToArray(); // the IR ends its lines in CR LF on Windows
        var first = Array.FindIndex(lines, l => l.StartsWith("define ", StringComparison.Ordinal) && l.Contains("@\"remove.", StringComparison.Ordinal));
        Assert.True(first >= 0, "no remove helper in the IR");
        var body = lines[first..Array.IndexOf(lines, "}", first)];
        var find = Assert.Single(body, l => l.Contains("call ptr @\"find.", StringComparison.Ordinal)); // the lookup alone
        var found = Regex.Match(find, @"^\s*(%\S+) = call ptr @""find\.[^""]+""\(ptr (%[^,\s]+),");
        Assert.True(found.Success, find);
        var (slot, block) = (found.Groups[1].Value, found.Groups[2].Value);

        // the branch into the copy's slot is taken where the block the map holds after `OwnBlock` is not the block the slot was found
        // in, and the slot found is taken where it is
        var branch = Assert.Single(body, l => l.TrimStart().StartsWith("br i1 ", StringComparison.Ordinal) && l.Contains("label %in.copy.", StringComparison.Ordinal));
        Assert.Matches(@"label %take\.\d+, label %in\.copy\.", branch);
        var condition = branch.TrimStart()["br i1 ".Length..].Split(',')[0];
        var test = Assert.Single(body, l => l.TrimStart().StartsWith(condition + " = ", StringComparison.Ordinal));
        var compared = Regex.Match(test, @"= icmp eq ptr (%[^,\s]+), (%[^,\s]+)$");
        Assert.True(compared.Success, test);
        Assert.Equal(block, compared.Groups[2].Value);
        var current = compared.Groups[1].Value;

        // in the copy: the slot's address less the address of the block it was found in, added to the address of the copy
        var start = Array.FindIndex(body, l => l.StartsWith("in.copy.", StringComparison.Ordinal));
        var end = Array.FindIndex(body, start + 1, l => l.EndsWith(':') && !l.StartsWith(' '));
        var copy = string.Join("\n", body[(start + 1)..end]);
        var from = Regex.Match(copy, $@"(%\S+) = ptrtoint ptr {Regex.Escape(slot)} to i64$", RegexOptions.Multiline);
        var of = Regex.Match(copy, $@"(%\S+) = ptrtoint ptr {Regex.Escape(block)} to i64$", RegexOptions.Multiline);
        Assert.True(from.Success && of.Success, copy);
        var offset = Regex.Match(copy, $@"(%\S+) = sub i64 {Regex.Escape(from.Groups[1].Value)}, {Regex.Escape(of.Groups[1].Value)}$", RegexOptions.Multiline);
        Assert.True(offset.Success, copy);
        var stepped = Regex.Match(copy, $@"^\s*(%\S+) = getelementptr i8, ptr {Regex.Escape(current)}, i64 {Regex.Escape(offset.Groups[1].Value)}$", RegexOptions.Multiline);
        Assert.True(stepped.Success, copy);

        // the slot `take` erases: the found slot is kept in one place, the copy's slot is the one store there in the copy, and `take`
        // loads that place and gives what it loads to `MapErase` with the block the map holds
        var kept = Regex.Match(string.Join("\n", body), $@"^\s*store ptr {Regex.Escape(slot)}, ptr (%\S+)$", RegexOptions.Multiline);
        Assert.True(kept.Success, "the found slot is kept nowhere");
        var place = kept.Groups[1].Value;
        var stored = $"store ptr {stepped.Groups[1].Value}, ptr {place}";
        bool TakesTheCopy(string[] lines)
        {
            var inCopy = lines[(start + 1)..end].Select(l => l.Trim()).Where(l => l.Contains($"ptr {place}", StringComparison.Ordinal)).ToArray();
            var takeStart = Array.FindIndex(lines, l => l.StartsWith("take.", StringComparison.Ordinal));
            if (inCopy is not [var only] || only != stored || takeStart < 0)
            {
                return false;
            }

            var takeEnd = Array.FindIndex(lines, takeStart + 1, l => l.EndsWith(':') && !l.StartsWith(' '));
            var taken = lines[(takeStart + 1)..(takeEnd < 0 ? lines.Length : takeEnd)];
            var loaded = Regex.Match(string.Join("\n", taken), $@"^\s*(%\S+) = load ptr, ptr {Regex.Escape(place)}$", RegexOptions.Multiline);
            return loaded.Success && taken.Any(l => l.Contains("MapErase", StringComparison.Ordinal) && l.Contains($"(ptr {current}, ptr {loaded.Groups[1].Value}, i64 ", StringComparison.Ordinal));
        }

        Assert.True(TakesTheCopy(body), string.Join("\n", body));

        // the positive controls: the found slot stored again in the copy, which leaves the copy's slot unused, and a `take` that gives
        // `MapErase` the found slot, not what it loaded, each fail the check
        var unused = body.Select(l => l.Trim() == stored ? l.Replace($"store ptr {stepped.Groups[1].Value},", $"store ptr {slot},", StringComparison.Ordinal) : l).ToArray();
        Assert.NotEqual(body, unused);
        Assert.False(TakesTheCopy(unused));
        var erasedFound = body.Select(l => l.Contains("MapErase", StringComparison.Ordinal) ? Regex.Replace(l, $@"\(ptr {Regex.Escape(current)}, ptr %[^,\s]+, i64 ", m => $"(ptr {current}, ptr {slot}, i64 ") : l).ToArray();
        Assert.NotEqual(body, erasedFound);
        Assert.False(TakesTheCopy(erasedFound));
    }

    [Fact]
    public void The_push_of_a_list_widens_its_count_without_a_sign()
    {
        // std's ListPush reads the count as a `uint`, so that the item's offset is no sign extension and a loop of `Add`s reads the count
        // the `Add` before stored by a plain load
        var program = Compile("mut b = Bytes()\nb.Add(1)\nprint(b.Count)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program;
        Assert.True(program is not null, "no program");
        var start = program.Ir.IndexOf("define internal ptr @\"std.ListPush.", StringComparison.Ordinal);
        Assert.True(start >= 0, "no ListPush in the IR");
        var body = program.Ir[start..program.Ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        Assert.Contains("zext i32 %", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sext i32 %", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_helper_of_Add_holds_the_push_and_calls_the_making_of_room_out_of_line()
    {
        // the push is small enough for the optimiser to inline at every `Add`, as Rust's `Vec::push`; the copy, the growth and the
        // refused block stay in a helper it never inlines
        var ir = Compile("mut xs = List<int>()\nxs.Add(1)\nxs.Add(2)\nprint(xs)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program!.Ir;
        string Body(string name)
        {
            var start = ir.IndexOf($"define internal void @\"{name}\"(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"no {name} in the IR");
            return ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        }

        var add = Body("add.List<int>");
        Assert.Contains("call void @\"reserve.List<int>\"(ptr %a0)", add, StringComparison.Ordinal);
        Assert.DoesNotContain("ListGrow", add, StringComparison.Ordinal);
        Assert.DoesNotContain("ListCopy", add, StringComparison.Ordinal);
        var reserve = Body("reserve.List<int>");
        Assert.Contains(" noinline cold ", reserve.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("ListGrow", reserve, StringComparison.Ordinal);
        Assert.Contains("ListCopy", reserve, StringComparison.Ordinal);
    }

    [Fact]
    public void The_end_of_a_block_and_the_end_of_an_instance_let_go_of_their_fields_in_their_order()
    {
        // R1, the owner, 2026-11-04: the end of a value lets go of what its fields hold in the order of the fields, as Rust drops a
        // value's fields, a block by the release of its family and an instance by its class's; `Pair` and `Two` let go of their
        // string before their list, `Rev` and `Owt` of their list before their string, so that an order by the kind of a field
        // fails one of them; a derived block lets go of its base's fields first (R1, the owner, 2026-11-05), so `Tagged`
        // lets go of the string of `Named` before its own list, and so does an instance of a derived class (R1, assumed: a
        // reading of that answer), so `Hand` lets go of the string of `Who` before its own list
        const string text = "data Pair(string A, List<int> B, Pair? Next)\nclass Two(string A, List<int> B)\ndata Rev(List<int> B, string A, Rev? Next)\nclass Owt(List<int> B, string A)\ndata Named(string A)\ndata Tagged(List<int> B) : Named\nclass Who(string A)\nclass Hand(List<int> B) : Who\n\np = Pair(\"{1}\", [2], null)\nt = Two(\"{3}\", [4])\nr = Rev([5], \"{6}\", null)\no = Owt([7], \"{8}\")\ng = Tagged(\"{9}\", [10])\nh = Hand(\"{11}\", [12])\nprint(p.B)\nprint(t.B)\nprint(r.B)\nprint(o.B)\nprint(g.B)\nprint(h.B)\n";
        const string release = "call void @\"release.string\"(";
        const string releaseList = "call void @\"release.List<int>\"(";
        var ir = Compile(text, Builder.CheckRuntime(Builder.HostTarget!.Value)).Program!.Ir;
        string Body(string name)
        {
            var start = ir.IndexOf($"define internal void @\"{name}\"(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"no {name} in the IR");
            return ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        }

        foreach (var (name, earlier, later) in new[]
        {
            ("drop.block.Pair", release, releaseList), ("drop.class.Two", release, releaseList),
            ("drop.block.Rev", releaseList, release), ("drop.class.Owt", releaseList, release),
            ("drop.class.Hand", release, releaseList),
        })
        {
            var body = Body(name);
            var first = body.IndexOf(earlier, StringComparison.Ordinal);
            var second = body.IndexOf(later, StringComparison.Ordinal);
            Assert.True(first >= 0 && second >= 0, $"{name} lets go of no string or of no list");
            Assert.True(first < second, $"{name} lets go of its fields out of their order");
        }

        // the release of the family `Named` branches on the number the block holds: one arm for `Named`, one for `Tagged`
        var arms = Body("drop.block.Named").Split("\nkind.").Skip(1).ToList();
        Assert.Equal(2, arms.Count);
        Assert.Contains(arms, arm => arm.Contains(release, StringComparison.Ordinal) && !arm.Contains(releaseList, StringComparison.Ordinal));
        var tagged = Assert.Single(arms, arm => arm.Contains(releaseList, StringComparison.Ordinal));
        Assert.True(tagged.Contains(release, StringComparison.Ordinal), "a `Tagged` block lets go of no string of its base");
        Assert.True(tagged.IndexOf(release, StringComparison.Ordinal) < tagged.IndexOf(releaseList, StringComparison.Ordinal), "a `Tagged` block lets go of its own list before its base's string");
    }

    [Fact]
    public void The_lookup_of_a_map_hands_its_probe_past_the_bound_to_a_helper_out_of_line()
    {
        // the lookup is small enough for the optimiser to inline at every read, write and `Remove`, as before the bound of a lookup:
        // the test of the bound's rehash, the rehash, the second hash and the probe again with no bound stay in a helper it never
        // inlines, which it hands the block, the key and its hash alone and whose result it returns, and the lookup does not call itself
        var ir = Compile("mut m = Map<int, int>()\nm[1] = 2\nprint(m[1] ?? 0)\n", Builder.CheckRuntime(Builder.HostTarget!.Value)).Program!.Ir;
        string Body(string name)
        {
            var start = ir.IndexOf($"define internal ptr @\"{name}\"(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"no {name} in the IR");
            return ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
        }

        var find = Body("find.Map<int, int>");
        var call = find.IndexOf("call ptr @\"far.Map<int, int>\"(ptr %a0, i32 %a1, i64 %a2)", StringComparison.Ordinal);
        Assert.True(call >= 0, "the lookup hands no probe past the bound on");
        Assert.StartsWith("ret ptr %", find[(find.IndexOf('\n', call) + 1)..].TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("call ptr @\"find.Map<int, int>\"", find, StringComparison.Ordinal);
        Assert.DoesNotContain("MapLongLookup", find, StringComparison.Ordinal);
        Assert.DoesNotContain("MapHash", find, StringComparison.Ordinal);
        var far = Body("far.Map<int, int>");
        Assert.StartsWith("define internal ptr @\"far.Map<int, int>\"(ptr %a0, i32 %a1, i64 %a2)", far, StringComparison.Ordinal);
        Assert.Contains(" noinline cold ", far.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("MapLongLookup", far, StringComparison.Ordinal);
        Assert.Contains("call void @\"rehash.Map<int, int>\"(ptr %a0)", far, StringComparison.Ordinal);
        Assert.Contains("call ptr @\"find.Map<int, int>\"(ptr %a0, i32 %a1, i64 ", far, StringComparison.Ordinal);
        Assert.DoesNotContain("call ptr @\"far.Map<int, int>\"", far, StringComparison.Ordinal);
    }

    [Fact]
    public void A_construct_of_the_runtime_the_emitter_does_not_lay_out_is_the_compilers_defect_and_names_its_file()
    {
        var target = Builder.HostTarget!.Value;
        var path = $"runtime/{Runtime.Directory(target)}/";
        var files = Runtime.Files.Select(f => f.Path.StartsWith(path, StringComparison.Ordinal)
            ? new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(f.Bytes).Replace("pub void Start(int count, byte* values) {", "pub void Start(int count, byte* values) {\n    print(\"ab\".Chars[0])", StringComparison.Ordinal)))
            : f).ToList();
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        var e = Assert.Throws<InvalidOperationException>(() => Compile("print(1)\n", runtime));
        Assert.StartsWith($"{path}{Runtime.Directory(target)}.kz:", e.Message, StringComparison.Ordinal);
        Assert.Contains("(T15)", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enum_value_is_stored_in_the_smallest_integer_that_holds_it_and_read_by_its_sign()
    {
        // section 4, an assumption there: as Rust stores an enum without fields, signed where a number is negative
        const string Program = "enum Few { A, B }\nenum Signed { Low = -1, High = 5 }\nenum Unsigned { High = 200, Low = 1 }\nenum Middle { A = 300 }\nenum Wide { A = 70000 }\n\n"
            + "bool Above(Unsigned x, Unsigned y) {\n    return x > y\n}\n\nbool Below(Signed x, Signed y) {\n    return x < y\n}\n\n"
            + "print(Few.B)\nprint(Signed.High)\nprint(Signed.Low.Number)\nprint(Unsigned.Low)\nprint(Unsigned.High.Number)\nprint(Above(Unsigned.High, Unsigned.Low))\n"
            + "print(Below(Signed.Low, Signed.High))\nprint(Middle.A)\nprint(Wide.A)\nprint(Wide.A.Number)";
        var program = Emit(Program);
        foreach (var (name, ir) in (List<(string, string)>)[("Few", "i8"), ("Signed", "i8"), ("Unsigned", "i8"), ("Middle", "i16"), ("Wide", "i32")])
        {
            Assert.Contains($"@\"text.{name}\"({ir} %a0)", program.Ir, StringComparison.Ordinal);
        }

        Ran(Toolchain.Run(toolchain.Build(program, "enum-layout")), 0, ["B", "High", "-1", "Low", "200", "true", "true", "A", "A", "70000"], string.Empty);
    }

    [Fact]
    public void A_set_of_flags_is_an_int_of_its_bits_with_its_names_as_its_text()
    {
        // D19: a set is stored in an `int`, the bits of its names, whose `Number` it is; `From` gives the set of a number with no other
        // bit and `Invalid` otherwise, a negative number among them; D17: `~` of a set that is not a constant keeps the type's bits alone;
        // A13: the names in the order of their declaration, whatever their bits, and `None` for the empty set, in a hole too
        const string Program = "flags Access { Read, Write, Run }\nflags Mode { Read = 4, Write = 2, Exec = 1 }\n\n"
            + "Access Flip(Access a) => ~a\n\nstring Show(Access a) => \"[{a}]\"\n\n"
            + "void Look(int n) {\n    match Access.From(n) {\n        Access a => print(a)\n        Invalid => print(\"invalid\")\n    }\n}\n\n"
            + "Look(0)\nLook(6)\nLook(8)\nLook(-1)\nprint(Flip(Access.Read))\nprint(Flip(Access.Read).Number)\nprint(Flip(Access.None))\n"
            + "print(Mode.Exec | Mode.Read)\nprint((Mode.Exec | Mode.Read).Number)\nprint(Show(Access.Run | Access.Read))\nprint(Access.Read.Has(Access.None))\n"
            + "mut q = Access.None\nq = q | Access.Write\nprint(q)\nprint(q.Has(Access.Write | Access.Run))\nprint(~Access.Write)";
        var program = Emit(Program);
        Assert.Contains("@\"text.Access\"(i32 %a0)", program.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(program, "flags-layout")), 0,
            ["None", "Write | Run", "invalid", "invalid", "Write | Run", "6", "Read | Write | Run", "Read | Exec", "5", "[Read | Run]", "true", "Write", "false", "Read | Run"], string.Empty);
    }

    [Fact]
    public void Two_sets_of_flags_are_equal_by_their_names_and_a_set_is_a_key_by_them()
    {
        // D13 (section 4, the owner, 2026-11-11, issue 452): `==` compares the bits of two sets, written in any order; a list finds a set
        // by them, a map hashes a set by them, and a `data` value compares and hashes a field of a set as the set compares
        const string Program = "flags Access { Read, Write, Run }\ndata Grant(string Who, Access Can)\n\n"
            + "a = Access.Read | Access.Write\nb = Access.Write | Access.Read\nprint(a == b)\nprint(a != b)\nprint(a == Access.Read)\n"
            + "print([Access.Run, b].IndexOf(a))\nmut names = Map<Access, string>()\nnames[a] = \"rw\"\nprint(names[b] ?? \"none\")\nprint(names[Access.Run] ?? \"none\")\n"
            + "print(Grant(\"x\", a) == Grant(\"x\", b))\nprint(Grant(\"x\", a) == Grant(\"x\", Access.Run))\n"
            + "mut grants = Map<Grant, int>()\ngrants[Grant(\"x\", a)] = 1\nprint(grants[Grant(\"x\", b)] ?? 0)\nprint(grants[Grant(\"x\", Access.Run)] ?? 0)";
        Ran(Toolchain.Run(toolchain.Build(Emit(Program), "flags-equal")), 0, ["true", "false", "false", "1", "rw", "none", "true", "false", "1", "0"], string.Empty);
    }

    [Fact]
    public void A_nullable_value_is_a_union_and_the_address_of_one_known_not_null_is_the_address_of_its_value()
    {
        // N1 with section 5, a reading of this directory: the tag beside the room of a `long`; N3, R10: where the checker reads a nullable
        // variable as the type it makes nullable, `&` gives the place of that value in the variable's room, so that a write through it
        // writes the variable's value
        var program = Emit("mut long? n = 5\nraw {\n    long* p = &n\n    *p = 7\n}\nprint(n)\n", "raw");
        Assert.Contains("alloca { i8, [1 x i64] }", program.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(program, "null-address")), 0, ["7"], string.Empty);
    }

    [Fact]
    public void A_nullable_list_or_block_is_its_address_alone_with_a_null_no_value_of_it_has()
    {
        // section 4, the owner, 2026-10-30, "Rust's niche": a `List<int>?` and a node's `Next` take 8 bytes, a block's `null` is the
        // address 0 and a list's the address 1, since a list without items is the address 0; the `long?` beside them keeps its tag
        const string Program = "data Node(int Value, Node? Next)\n\nList<int>? Items(int n) {\n    if n < 0 {\n        return null\n    }\n    mut xs = List<int>()\n"
            + "    for i in 0..<n {\n        xs.Add(i)\n    }\n    return xs\n}\n\nint Length(Node? node) {\n    mut Node? at = node\n    mut count = 0\n"
            + "    while at != null {\n        count = count + 1\n        at = at.Next\n    }\n    return count\n}\n\n"
            + "print(Items(0))\nprint(Items(-1))\nprint(Items(2))\nprint(Items(0) == null)\nprint(Items(-1)?[0])\nprint(Length(Node(1, Node(2, null))))\nprint(Length(null))\n"
            + "mut long? wide = 5\nprint(wide)\n";
        var program = Emit(Program);
        Assert.Contains("icmp eq ptr", program.Ir, StringComparison.Ordinal);
        Assert.Contains("inttoptr (i64 1 to ptr)", program.Ir, StringComparison.Ordinal);
        Assert.Contains("type { i64, i32, i32, ptr }", program.Ir, StringComparison.Ordinal); // the node's block: its count, its type's number, `Value` and `Next`
        Assert.Contains("alloca { i8, [1 x i64] }", program.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(program, "null-niche")), 0, ["[]", "null", "[0, 1]", "false", "null", "2", "0", "5"], string.Empty);
    }

    [Fact]
    public void A_class_nothing_derives_from_is_the_one_word_of_its_address_and_a_base_class_adds_the_table_of_the_instance()
    {
        // K6, laid out as "faster wins" picks it: a reference whose class has derived classes is the address of the instance's block and
        // the table of its class, as Rust lays out `Rc<dyn Trait>`, its `null` the pair of the address 0 (N1, as Rust's
        // `Option<Rc<dyn Trait>>`) and its `weak` the same two words (R3); a class nothing derives from, a derived one among them, stays
        // the one word of its address, so that a class with no derived class never pays for the table, and no block holds a table
        const string Program = """
            class Base(int N) {
                pub virtual int Get() => N
            }

            class Mid(int M) : Base {
                pub override int Get() => M
            }

            class Leaf(int K)
            class Holder(weak Leaf? A, weak Base? B, Leaf? C, Base? D)

            int Leaves(Leaf x, Leaf? y) {
                if y == null {
                    return x.K
                }

                return x.K + y.K
            }

            int Bases(Base x, Base? y) {
                if y == null {
                    return x.Get()
                }

                return x.Get() + y.Get()
            }

            int Mids(Mid x) => x.Get()

            l = Leaf(1)
            m = Mid(2, 3)
            h = Holder(l, m, null, null)
            print(Leaves(l, null))
            print(Leaves(l, Leaf(4)))
            print(Bases(m, null))
            print(Bases(Base(5), m))
            print(Mids(m))
            print(h.D == null)
            """;
        var program = Emit(Program);
        string Parameters(string name) =>
            Regex.Match(program.Ir, $@"^define [^@\n]*@""[^""]*\.{name}\.[0-9]+""\(([^)]*)\)", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal("ptr %p0, ptr %p1", Parameters("Leaves"));
        Assert.Equal("%kz.obj %p0, %kz.obj %p1", Parameters("Bases"));
        Assert.Equal("ptr %p0", Parameters("Mids"));
        Assert.Contains("%kz.obj = type { ptr, ptr }", program.Ir, StringComparison.Ordinal);
        Assert.Contains("%\"c.Holder\" = type { i64, i64, ptr, %kz.obj, ptr, %kz.obj }", program.Ir, StringComparison.Ordinal);
        Assert.Contains("%\"c.Mid\" = type { i64, i64, i32, i32 }", program.Ir, StringComparison.Ordinal); // the counts, the base's `N`, then `M`
        Ran(Toolchain.Run(toolchain.Build(program, "class-layout")), 0, ["1", "5", "3", "8", "3", "true"], string.Empty);
    }

    [Fact]
    public void A_value_of_an_interface_is_the_address_and_the_table_its_class_has_for_the_interface()
    {
        // K6, as Rust lays out `Rc<dyn Trait>`: a value of an interface is the address of the instance's block and its class's table for
        // the interface, whose words are the class's release, its weak release, its equality and its hash (null where no `==` and no key
        // reads them), the class's own table (null for a class with no base and no derived class), and a slot for each method of the
        // interface in its order, so `Dot`'s table for `Shape` is 7 words; its `null` is the pair of the address 0 (N1); a class with no
        // base and no derived class, as `Dot`, has that table alone, and its own references stay the one word of the address
        const string Program = """
            interface Shape {
                int Area()
                int Twice(int k)
            }

            class Dot(int R) : Shape {
                pub int Area() => R
                pub int Twice(int k) => R * k
            }

            class Holder(Shape S, Shape? T)

            int Both(Shape x, Shape? y) {
                if y == null {
                    return x.Area()
                }

                return x.Area() + y.Twice(2)
            }

            int Own(Dot d) => d.R

            h = Holder(Dot(1), null)
            print(Both(h.S, h.T))
            print(Both(Dot(2), Dot(3)))
            print(Own(Dot(4)))
            """;
        var program = Emit(Program);
        string Parameters(string name) =>
            Regex.Match(program.Ir, $@"^define [^@\n]*@""[^""]*\.{name}\.[0-9]+""\(([^)]*)\)", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal("%kz.obj %p0, %kz.obj %p1", Parameters("Both"));
        Assert.Equal("ptr %p0", Parameters("Own"));
        Assert.Contains("%\"c.Holder\" = type { i64, i64, %kz.obj, %kz.obj }", program.Ir, StringComparison.Ordinal);
        Assert.Contains("@\"table.Dot.Shape\" = internal constant [8 x ptr] [", program.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("@\"table.Dot\" =", program.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(program, "interface-layout")), 0, ["1", "8", "4"], string.Empty);
    }

    [Theory]
    [InlineData("(move Cart) => void g = (Cart c) => print(c.Items)\ng(move Cart(4))\n")]
    [InlineData("((move Cart) => void)? g = (Cart c) => print(c.Items)\nif g != null {\n    g(move Cart(4))\n}\n")]
    public void A_function_value_whose_parameter_is_not_written_move_given_for_a_type_that_writes_it_is_not_supported_through_a_nullable_too(string text)
    {
        // U12: a lambda whose parameter's type is written without `move` fits a type that writes it (2026-11-05, Q4), which the build does
        // not wrap, so that the callee would borrow what its caller hands over; a nullable function type reaches the refusal through its
        // case, as the plain one does
        var refused = Compile("class Cart(mut int Items)\n\n" + text, toolchain.Runtime);
        Assert.Null(refused.Program);
        var d = Assert.Single(refused.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 3), (d.Kind, d.Line));
    }

    /// <summary>The lines of a program's IR that hold an atomic instruction of the program's own, which the values given mark.</summary>
    private static List<string> AtomicLines(EmittedProgram program) =>
        program.Ir.ReplaceLineEndings("\n").Split('\n').Where(l => Regex.IsMatch(l, @"\b(load atomic|store atomic|atomicrmw|cmpxchg|fence) .*\b7000\d\b|^\s*fence ")).ToList();

    [Fact]
    public void An_atomic_member_with_its_order_written_is_one_llvm_instruction_of_that_order_aligned_to_its_word()
    {
        // R19 (section 6, 2026-11-17, comment 6946657505, Q1): Rust's lowering, `Relaxed` LLVM's `monotonic`; the values 70001 to
        // 70009 mark the program's own instructions among the runtime's
        const string text = "mut long w = 5\nmut int i = 1\nmut bool b = false\nmut long* slot = &w\na = AtomicLong.FromPtr(&w)\nn = AtomicInt.FromPtr(&i)\n"
            + "c = AtomicBool.FromPtr(&b)\np = AtomicPtr<long>.FromPtr(&slot)\nraw {\n"
            + "    a.Store(70001, MemoryOrder.Release)\n    print(a.Swap(70002, MemoryOrder.AcqRel))\n    print(a.FetchAdd(70003, MemoryOrder.Acquire))\n"
            + "    print(a.FetchSub(70004, MemoryOrder.SeqCst))\n    print(n.FetchAnd(70005, MemoryOrder.Relaxed))\n    print(long(p.FetchOr(70006, MemoryOrder.Relaxed)) - long(&w))\n"
            + "    print(a.FetchXor(70007, MemoryOrder.Relaxed))\n"
            + "    match a.CompareExchangeWeak(70008, 70009, MemoryOrder.AcqRel, MemoryOrder.Acquire) {\n        long found => print(found)\n        NotExchanged<long> missed => print(missed.Value)\n    }\n"
            + "    print(c.Swap(true, MemoryOrder.SeqCst))\n    print(a.Load(MemoryOrder.Relaxed))\n    Fence(MemoryOrder.Acquire)\n}\n";
        var program = Emit(text, "raw");
        var lines = AtomicLines(program);
        var shown = string.Join("\n", lines);
        string[] expected =
        [
            @"store atomic i64 70001, ptr %[\w.]+ release, align 8",
            @"atomicrmw xchg ptr %[\w.]+, i64 70002 acq_rel, align 8",
            @"atomicrmw add ptr %[\w.]+, i64 70003 acquire, align 8",
            @"atomicrmw sub ptr %[\w.]+, i64 70004 seq_cst, align 8",
            @"atomicrmw and ptr %[\w.]+, i32 70005 monotonic, align 4",
            @"atomicrmw or ptr %[\w.]+, i64 70006 monotonic, align 8", // an `AtomicPtr`'s bits on the address as an `i64`
            @"atomicrmw xor ptr %[\w.]+, i64 70007 monotonic, align 8",
            @"cmpxchg weak ptr %[\w.]+, i64 70008, i64 70009 acq_rel acquire, align 8",
            @"^\s*fence acquire",
        ];
        Assert.True(lines.Count == expected.Length, shown);
        for (var k = 0; k < expected.Length; k++)
        {
            Assert.Matches(expected[k], lines[k]);
        }

        Assert.Matches(@"atomicrmw xchg ptr %[\w.]+, i8 %[\w.]+ seq_cst, align 1", program.Ir); // a `bool`'s word is a byte
        Assert.Matches(@"load atomic i64, ptr %[\w.]+ monotonic, align 8", program.Ir);
        Assert.Matches(@"inttoptr i64 %[\w.]+ to ptr", program.Ir);
        Ran(Toolchain.Run(toolchain.Build(program, "atomic-orders")), 0, ["70001", "70002", "140005", "1", "0", "70001", "6", "false", "6"], string.Empty);
    }

    [Fact]
    public void An_atomic_order_held_in_a_value_is_chosen_when_the_call_runs_and_one_rust_refuses_raises_at_the_call()
    {
        // R19: a `switch` over the five values of `MemoryOrder`, each to the instruction of its order, as Rust's `match` on the order;
        // `Release` and `AcqRel`, which a load does not take, raise `invalid-ordering-at-run-time` at the call's line
        const string text = "long Read(AtomicLong a, MemoryOrder order) {\n    mut long v = 0\n    raw {\n        v = a.Load(order)\n    }\n\n    return v\n}\n\n"
            + "mut long w = 70001\nview = AtomicLong.FromPtr(&w)\nprint(Read(view, MemoryOrder.Relaxed))\nprint(Read(view, MemoryOrder.Acquire))\nprint(Read(view, MemoryOrder.SeqCst))\nprint(Read(view, MemoryOrder.AcqRel))\n";
        var program = Emit(text, "raw");
        foreach (var order in new[] { "monotonic", "acquire", "seq_cst" })
        {
            Assert.Matches($@"load atomic i64, ptr %[\w.]+ {order}, align 8", program.Ir);
        }

        Assert.Matches(@"switch i8 %[\w.]+, label %[\w.]+ \[ i8 0, label %[\w.]+ i8 1, label %[\w.]+ i8 2, label %[\w.]+ i8 3, label %[\w.]+ i8 4, label %[\w.]+ \]", program.Ir);
        Ran(Toolchain.Run(toolchain.Build(program, "atomic-order-chosen")), 1, ["70001", "70001", "70001"], "error: invalid-ordering-at-run-time at program.kz:4\n");
    }

    [Fact]
    public void The_reply_and_the_state_word_are_taken_and_set_by_atomic_operations_of_their_protocols_orders()
    {
        // U12, U17, R19: the call stores its reply's waiter (`ReplyWaiter`) before `Enqueue` queues its message; the message's function
        // takes the reply off the message after the method returned by one swap (`ReplyTaken`), as the crash's unhook does, so that one
        // of them owns it, as tokio's `oneshot` decides by one word (not checked at source); the owner reads the waiter, then stores the
        // reply's word with release, then wakes (`Replied`, `Signal`), the waiter loads it with acquire
        // (`ReplyState`); a wake sets QUEUED by one `or` (`Wake`), a take, a hold's end and a constructor's end change the state word by
        // one compare-exchange (`Exchanged`); the orders are Rust's for each, which a thread of their own needs and one thread does not
        // show
        const string text = "actor Counter {\n    mut long count = 0\n\n    pub void Add(long n) {\n        count = count + n\n    }\n\n    pub long Get() => count\n}\n\n"
            + "c = spawn Counter()\nsend c.Add(2)\nprint(c.Get())\n";
        var program = Emit(text);
        var ir = program.Ir.ReplaceLineEndings("\n");
        string Body(string name)
        {
            var defined = Regex.Match(ir, $@"^define [^\n]*@{name}\([^\n]*\n(.*?)\n\}}\n", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.True(defined.Success, $"no function {name}");
            return defined.Groups[1].Value;
        }

        string Runtime(string name) => Body($@"""rt\.common\.{name}\.[0-9]+""");
        void InOrder(string body, params string[] patterns)
        {
            var at = 0;
            foreach (var pattern in patterns)
            {
                var found = new Regex(pattern).Match(body, at);
                Assert.True(found.Success, $"no `{pattern}` after the place {at} of:\n{body}");
                at = found.Index + found.Length;
            }
        }

        InOrder(Body(@"kz\.main"), @"store i32 0, ptr %v\.reply\.\d+", @"call void @""rt\.common\.ReplyWaiter\.\d+""\(ptr %v\.reply\.\d+\)",
            @"store ptr %v\.reply\.\d+, ptr %t\d+", @"call void @""rt\.common\.Enqueue\.\d+""", @"call void @""rt\.common\.Wait\.\d+""");
        Assert.Contains("getelementptr { i32, ptr, i64, ptr, ptr, i64, ptr, i64 }, ptr %v.reply.", ir, StringComparison.Ordinal); // U36, U43: the deadline, the timers' links and the time of a crash or an end before the heap
        InOrder(Body(@"""message\.Counter\.Get\.\d+"""), @"call i64 @""f\.Counter\.Get\.\d+""", @"call ptr @""rt\.common\.ReplyTaken\.\d+""\(ptr %a1\)",
            @"call void @""rt\.common\.Replied\.\d+""");
        InOrder(Runtime("ReplyTaken"), @"atomicrmw xchg ptr %t\d+, ptr null acq_rel, align 8");
        InOrder(Runtime("ReplyState"), @"load atomic i32, ptr %t\d+ acquire, align 4");
        InOrder(Runtime("Replied"), @"store ptr %t\d+, ptr %v\.waiter\.\d+", @"store atomic i32 1, ptr %t\d+ release, align 4", @"call void @""rt\.common\.Wake\.\d+""");
        InOrder(Runtime("Signal"), @"store ptr %t\d+, ptr %v\.waiter\.\d+", @"store atomic i32 %t\d+, ptr %t\d+ release, align 4", @"call void @""rt\.common\.Wake\.\d+""");
        InOrder(Runtime("Wake"), @"atomicrmw or ptr %t\d+, i32 2 acq_rel, align 4", @"call void @""rt\.common\.Queue\.\d+""");
        InOrder(Runtime("Exchanged"), @"cmpxchg ptr %t\d+, i32 %t\d+, i32 %t\d+ acq_rel acquire, align 4");
        foreach (var name in new[] { "Claimed", "LetGo", "ActorBuilt" })
        {
            InOrder(Runtime(name), @"load atomic i32, ptr %t\d+ acquire, align 4", @"call i32 @""rt\.common\.Exchanged\.\d+""");
        }

        Ran(Toolchain.Run(toolchain.Build(program, "reply-orders")), 0, ["2"], string.Empty);
    }

    [Fact]
    public void The_queues_the_locks_and_the_slots_the_threads_share_are_taken_and_set_by_atomic_operations_of_their_orders()
    {
        // section 6, R19: a sender puts its message on the actor's queue by one swap of its end with acquire and release, after a
        // relaxed store of the message's link, and stores the link of the end it found with release, which the holder loads with
        // acquire (`Pushed`, `Popped`, an intrusive queue of many producers and one consumer, not checked at source); the lock of the shared words is of the shape of Rust's futex mutex (not checked at source), a compare-exchange from 0
        // to 1 with acquire, relaxed where it fails, relaxed loads in its spin, a swap of 2 with acquire, and its end a swap of 0 with
        // release before the wake (`LockTried`, `LockSpun`, `Lock`, `Unlock`); the count of the actors is one relaxed add (`ActorNew`); a
        // slot is set with release, its state before its number, cleared with release, its number before its link, and looked up with
        // no lock by three loads with acquire, the number, the state and the number again (`SlotTake`, `SlotFree`, `Lookup`); the orders
        // are what a thread of its own needs and one thread does not show; the end of a supervisor sets its child's ENDING bit by one `or`
        // with acquire and release before it holds the child, and the child's holder loads it with acquire (`EndChildren`, `Ending`)
        const string text = "actor Counter {\n    mut long count = 0\n\n    pub void Add(long n) {\n        count = count + n\n    }\n\n    pub long Get() => count\n}\n\n"
            + "c = spawn Counter()\nsend c.Add(2)\nprint(c.Get())\n";
        var program = Emit(text);
        var ir = program.Ir.ReplaceLineEndings("\n");
        string Runtime(string name)
        {
            var defined = Regex.Match(ir, $@"^define [^\n]*@""rt\.common\.{name}\.[0-9]+""\([^\n]*\n(.*?)\n\}}\n", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.True(defined.Success, $"no function {name}");
            return defined.Groups[1].Value;
        }

        void InOrder(string body, params string[] patterns)
        {
            var at = 0;
            foreach (var pattern in patterns)
            {
                var found = new Regex(pattern).Match(body, at);
                Assert.True(found.Success, $"no `{pattern}` after the place {at} of:\n{body}");
                at = found.Index + found.Length;
            }
        }

        InOrder(Runtime("Pushed"), @"store atomic ptr null, ptr %[\w.]+ monotonic, align 8", @"atomicrmw xchg ptr %[\w.]+, ptr %[\w.]+ acq_rel, align 8",
            @"store atomic ptr %[\w.]+, ptr %[\w.]+ release, align 8");
        InOrder(Runtime("Popped"), @"load atomic ptr, ptr %[\w.]+ acquire, align 8", @"load atomic ptr, ptr %[\w.]+ acquire, align 8",
            @"load atomic ptr, ptr %[\w.]+ acquire, align 8", @"call void @""rt\.common\.Pushed\.\d+""", @"load atomic ptr, ptr %[\w.]+ acquire, align 8");
        InOrder(Runtime("LockTried"), @"cmpxchg ptr %[\w.]+, i32 0, i32 1 acquire monotonic, align 4");
        InOrder(Runtime("LockSpun"), @"load atomic i32, ptr %[\w.]+ monotonic, align 4", @"load atomic i32, ptr %[\w.]+ monotonic, align 4");
        InOrder(Runtime("Lock"), @"call i32 @""rt\.common\.LockTried\.\d+""", @"call i32 @""rt\.common\.LockSpun\.\d+""",
            @"atomicrmw xchg ptr %[\w.]+, i32 2 acquire, align 4", @"call i64 @""?rt\.sys\.LockWait");
        InOrder(Runtime("Unlock"), @"atomicrmw xchg ptr %[\w.]+, i32 0 release, align 4", @"call i64 @""?rt\.sys\.LockWake");
        InOrder(Runtime("ActorNew"), @"atomicrmw add ptr %[\w.]+, i64 1 monotonic, align 8", @"call i64 @""rt\.common\.SlotTake\.\d+""");
        InOrder(Runtime("SlotTake"), @"call void @""rt\.common\.Lock\.\d+""", @"store atomic ptr %[\w.]+, ptr %[\w.]+ release, align 8",
            @"store atomic i64 %[\w.]+, ptr %[\w.]+ release, align 8", @"call void @""rt\.common\.Unlock\.\d+""");
        InOrder(Runtime("SlotFree"), @"call void @""rt\.common\.Lock\.\d+""", @"store atomic i64 0, ptr %[\w.]+ release, align 8",
            @"store atomic i64 %[\w.]+, ptr %[\w.]+ release, align 8", @"call void @""rt\.common\.Unlock\.\d+""");
        InOrder(Runtime("Lookup"), @"load atomic i64, ptr %[\w.]+ acquire, align 8", @"load atomic ptr, ptr %[\w.]+ acquire, align 8",
            @"load atomic i64, ptr %[\w.]+ acquire, align 8");
        InOrder(Runtime("EndChildren"), @"atomicrmw or ptr %[\w.]+, i32 16 acq_rel, align 4", @"call void @""rt\.common\.HeldAlone\.\d+""",
            @"call void @""rt\.common\.LetGo\.\d+""");
        InOrder(Runtime("Ending"), @"load atomic i32, ptr %[\w.]+ acquire, align 4");
        Ran(Toolchain.Run(toolchain.Build(program, "shared-orders")), 0, ["2"], string.Empty);
    }

    [Fact]
    public void An_atomic_order_a_copy_holds_as_its_constant_that_rust_refuses_raises_at_the_call()
    {
        // R19: an order no call writes, held as a constant by a copy of a function (T35), is no compile error; the copy that holds
        // one Rust refuses for a load raises `invalid-ordering-at-run-time` at the call's line, as Rust's `match` on the order panics
        const string text = "long Read<const MemoryOrder O>(AtomicLong a) {\n    mut long v = 0\n    raw {\n        v = a.Load(O)\n    }\n\n    return v\n}\n\n"
            + "mut long w = 70001\nview = AtomicLong.FromPtr(&w)\nprint(Read<MemoryOrder.Acquire>(view))\nprint(Read<MemoryOrder.Release>(view))\n";
        Ran(BuildAndRun(text, "atomic-order-copy", "raw"), 1, ["70001"], "error: invalid-ordering-at-run-time at program.kz:4\n");
    }
}
