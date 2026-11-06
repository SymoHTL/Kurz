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
/// section 4, an assumption there), the layout of a nullable value and the address of one known not null (N1, N3, R10), the reference to an instance of a class (K6), the runtime of every target emitted, and a
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
            "enum Color { Red, Green }\ndata Pair(string A, string B)\nclass Cell(mut int N)\nclass Spot(int X, int Y)\n\nlongduration far = 100000days\n" +
            "mut a = Map<int, int>()\na[1] = 1\nmut b = Map<long, int>()\nb[1] = 1\nmut c = Map<char, int>()\nc['x'] = 1\n" +
            "mut d = Map<bool, int>()\nd[true] = 1\nmut e = Map<Color, int>()\ne[Color.Green] = 1\nmut f = Map<double, int>()\nf[1.5] = 1\n" +
            "mut g = Map<string, int>()\ng[\"x\"] = 1\nmut h = Map<Pair, int>()\nh[Pair(\"a\", \"b\")] = 1\nmut i = Map<List<int>, int>()\ni[[1, 2]] = 1\n" +
            "mut j = Map<duration, int>()\nj[90min] = 1\nmut k = Map<longduration, int>()\nk[far] = 1\nmut l = Map<int?, int>()\nl[null] = 1\n" +
            "mut m = Map<float, int>()\nm[1.5f] = 1\nmut n = Map<Cell, int>()\nn[Cell(1)] = 1\nmut o = Map<Spot, int>()\no[Spot(1, 2)] = 1\n" +
            "print(a.Count + b.Count + c.Count + d.Count + e.Count + f.Count + g.Count + h.Count + i.Count + j.Count + k.Count + l.Count + m.Count + n.Count + o.Count)\n";
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
        ];
        Assert.All(layouts, layout => Assert.Contains($"\n%\"entry.{layout}", compiled.Ir, StringComparison.Ordinal));
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
        Assert.Matches(new Regex($@"= getelementptr i8, ptr {Regex.Escape(current)}, i64 {Regex.Escape(offset.Groups[1].Value)}$", RegexOptions.Multiline), copy);
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
}
