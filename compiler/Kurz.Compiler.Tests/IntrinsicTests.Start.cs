using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The start check and the run setting of R18 (section 2 and section 13, the owner, 2026-11-02): a build for a named CPU reads each of
/// its sets beyond the baseline once, before the first statement, as LLVM reads the machine for <c>native</c>, and ends with
/// <c>cpu-lacks-set</c> where one is missing or the run setting turned it off; the run setting turns a set off for the tests of the
/// CPU's word too, with every set that derives from it; a build that names no CPU, or the baseline, reads nothing for it.
/// </summary>
public sealed partial class IntrinsicTests
{
    // the sets of x86-64-v3 beyond the baseline by clang's names, in the order the start check reads them
    private static readonly string[] V3Names = ["sse3", "ssse3", "cx16", "sse4.1", "sse4.2", "crc32", "movbe", "popcnt", "avx", "fma", "xsave", "f16c", "sahf", "lzcnt", "bmi", "avx2", "bmi2"];

    /// <summary>
    /// Runs a built program with the trace off, the variables of the run setting given and every other variable of it taken away, so
    /// that a setting of the machine's own reaches no case; its exit code, its lines and what it wrote on standard error.
    /// </summary>
    private static (int Code, IReadOnlyList<string> Output, string Error) RunWith(string executable, params (string Name, string Value)[] settings)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["KURZ_TRACE"] = "0" };
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(n => n.StartsWith("KURZ_INTRINSICS", StringComparison.OrdinalIgnoreCase)))
        {
            environment[name] = null;
        }

        foreach (var (name, value) in settings)
        {
            environment[name] = value;
        }

        var (code, output, error) = Processes.Capture(executable, [], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), environment);
        return (code, Toolchain.Lines(output), error);
    }

    [Fact]
    public void A_build_for_a_named_cpu_reads_each_of_its_sets_at_its_start_as_llvm_reads_this_machine_and_names_those_it_lacks()
    {
        // section 2, the owner, 2026-11-02 (#39, "every set, as LLVM reads them"): a CPU named with every set of the table, so that the
        // check reads each row once; the answer of each is LLVM's own reading of this machine, the sets clang's `native` has
        var native = toolchain.Clang.DescribeCpu("native", x86: true, out _)!;
        var every = new Cpu("x86-64", Cpu.Baseline.Classes, []) { Sets = [.. CpuSets.Table.Select(s => s.Macro)] };
        var rows = CpuSets.Table.DistinctBy(s => s.Name, StringComparer.Ordinal).ToList();
        var lacks = rows.Where(s => !native.Sets.Contains(s.Macro, StringComparer.Ordinal)).Select(s => s.Name).ToList();
        Assert.True(rows.Count - lacks.Count >= 20, $"this machine has {rows.Count - lacks.Count} of the table's sets by clang's reading, so few rows are read as present");
        Assert.NotEmpty(lacks); // no machine has both vendors' sets: some row is read as missing

        var program = Compile("print(\"first\")\n", toolchain.Runtime, every).Program!;
        var executable = toolchain.Build(program, "start-every-set");
        Ran(RunWith(executable), 1, [], $"error: cpu-lacks-set (x86-64: {string.Join(", ", lacks)})\n");

        // with every set turned off, the line is grouped (the owner, 2026-11-02, #39 c6577407245): the sets the CPU lacks, alone, then
        // those it has, in the group of the variable that turned them off
        var has = rows.Select(s => s.Name).Except(lacks, StringComparer.Ordinal);
        Ran(RunWith(executable, ("KURZ_INTRINSICS", "0")), 1, [], $"error: cpu-lacks-set (x86-64: {string.Join(", ", lacks)}; {string.Join(", ", has)} turned off by KURZ_INTRINSICS)\n");
    }

    [Fact]
    public void The_start_check_reads_a_leaf_past_the_cpus_top_a_subleaf_past_leaf_7s_and_the_saved_state_as_llvm_reads_them_on_a_cpu_not_this_one()
    {
        // what this machine cannot show: a CPU whose `cpuid` and `xgetbv` are functions of the test's, standing in for the instructions;
        // a leaf or a subleaf the CPU does not have answers all ones, as an Intel CPU answers the data of its top leaf there, so that a
        // read that takes it reads sets the CPU does not have
        var every = new Cpu("x86-64", Cpu.Baseline.Classes, []) { Sets = [.. CpuSets.Table.Select(s => s.Macro)] };
        var program = Compile("print(\"first\")\n", toolchain.Runtime, every).Program!;
        Dictionary<(uint Leaf, uint Subleaf), uint[]> Base() => new()
        {
            [(0, 0)] = [0x24, 0, 0, 0],
            [(1, 0)] = [0, 0, Bits(0, 1, 9, 12, 13, 19, 20, 22, 23, 25, 26, 27, 28, 29), 0], // SSE3 to SSE4.2, AES, XSAVE, OSXSAVE, AVX, F16C
            [(7, 0)] = [1, Bits(3, 5, 8), 0, 0], // BMI, AVX2, BMI2; subleaf 1 is the last
            [(7, 1)] = [0, 0, 0, Bits(14, 19)], // Intel's PREFETCHI, AVX10
            [(0x24, 0)] = [0, 1, 0, 0], // AVX10.1
            [(0x80000000, 0)] = [0x80000001, 0, 0, 0],
            [(0x80000001, 0)] = [0, 0, Bits(0, 5), 0], // LAHF, LZCNT
        };

        string Line(string name, Dictionary<(uint Leaf, uint Subleaf), uint[]> leaves, uint? xcr0, params (string Name, string Value)[] settings)
        {
            var (code, output, error) = RunWith(toolchain.Build(program with { Ir = Faked(program.Ir, leaves, xcr0) }, name), settings);
            Assert.Equal((1, 0), (code, output.Count));
            var named = System.Text.RegularExpressions.Regex.Match(error, @"^error: cpu-lacks-set \(x86-64: (.*)\)\n$");
            Assert.True(named.Success, error);
            return named.Groups[1].Value;
        }

        IReadOnlyList<string> Lacks(string name, Dictionary<(uint Leaf, uint Subleaf), uint[]> leaves, uint? xcr0) => Line(name, leaves, xcr0).Split(", ");

        // the CPU as it is: what it has is not named, AVX10.2 is, since leaf 0x24 gives the version 1
        var lacks = Lacks("start-fake", Base(), 0x7);
        Assert.Empty(FakeHas.Intersect(lacks));
        Assert.Contains("avx10.2", lacks);
        Assert.Contains("avx512f", lacks);

        // the mixed form (the owner, 2026-11-02, #39 c6577407245): the sets the CPU lacks, then a group for each variable that turned
        // off sets it has, the nearest variable's for a set two turned off, the groups apart by `; `
        var mixed = Line("start-fake-mixed", Base(), 0x7, ("KURZ_INTRINSICS_SSE42", "0"), ("KURZ_INTRINSICS_AVX2", "0")).Split("; ");
        Assert.Equal(lacks, mixed[0].Split(", "));
        Assert.Equal(["sse4.2, popcnt, avx, fma turned off by KURZ_INTRINSICS_SSE42", "avx2 turned off by KURZ_INTRINSICS_AVX2"], mixed[1..]);

        // its top leaf 1: leaf 7 answers all ones, and the sets of leaf 7 are missing
        var top = Base();
        top[(0, 0)] = [1, 0, 0, 0];
        top.Remove((7, 0));
        Assert.Superset(new HashSet<string> { "bmi", "avx2", "bmi2", "prefetchi" }, Lacks("start-fake-top", top, 0x7).ToHashSet());

        // its last subleaf of leaf 7 the first: subleaf 1 answers all ones, and its sets are missing
        var subleaf = Base();
        subleaf[(7, 0)] = [0, Bits(3, 5, 8), 0, 0];
        subleaf.Remove((7, 1));
        var lastSubleaf = Lacks("start-fake-subleaf", subleaf, 0x7);
        Assert.Superset(new HashSet<string> { "prefetchi", "avx10.1" }, lastSubleaf.ToHashSet());
        Assert.DoesNotContain("avx2", lastSubleaf);

        // the system saves no YMM state: AVX and the sets on it are missing; and with OSXSAVE off, `xgetbv` is not run (it ends the
        // process here) and no state is saved
        Assert.Superset(new HashSet<string> { "avx", "avx2" }, Lacks("start-fake-xmm", Base(), 0x3).ToHashSet());
        var off = Base();
        off[(1, 0)] = [0, 0, Bits(0, 1, 9, 12, 13, 19, 20, 22, 23, 25, 26, 28, 29), 0];
        var noState = Lacks("start-fake-osxsave", off, null);
        Assert.Superset(new HashSet<string> { "avx", "avx2" }, noState.ToHashSet());
        Assert.DoesNotContain("sse4.2", noState);
    }

    // sets the fake CPU of the start check's test has, one of each kind of row: a bit of leaf 1, the state of AVX, leaf 7, the extended
    // leaf, the alternative bit and AVX10's version
    private static readonly string[] FakeHas = ["sse4.2", "avx", "avx2", "bmi2", "lzcnt", "prefetchi", "avx10.1"];

    private static uint Bits(params int[] bits) => bits.Aggregate(0u, (v, b) => v | (1u << b));

    /// <summary>
    /// The program's IR with <c>cpuid</c> and <c>xgetbv</c> calls of functions that answer as <paramref name="leaves"/> say, all ones for
    /// a leaf and subleaf they do not hold, and XCR0 as <paramref name="xcr0"/> says, where null a trap.
    /// </summary>
    private static string Faked(string ir, Dictionary<(uint Leaf, uint Subleaf), uint[]> leaves, uint? xcr0)
    {
        const string Cpuid = "call { i32, i32, i32, i32 } asm \"cpuid\", \"={ax},={bx},={cx},={dx},{ax},{cx}\"";
        const string Xgetbv = "call { i32, i32 } asm \"xgetbv\", \"={ax},={dx},{cx}\"";
        Assert.Contains(Cpuid, ir, StringComparison.Ordinal);
        Assert.Contains(Xgetbv, ir, StringComparison.Ordinal);
        var fake = new System.Text.StringBuilder("\ndefine internal { i32, i32, i32, i32 } @fake.cpuid(i32 %leaf, i32 %sub) {\n");
        string[] registers = ["-1", "-1", "-1", "-1"];
        var n = 0;
        foreach (var ((leaf, sub), values) in leaves)
        {
            fake.Append(CultureInfo.InvariantCulture, $"  %l{n} = icmp eq i32 %leaf, {(int)leaf}\n  %s{n} = icmp eq i32 %sub, {(int)sub}\n  %h{n} = and i1 %l{n}, %s{n}\n");
            for (var r = 0; r < 4; r++)
            {
                fake.Append(CultureInfo.InvariantCulture, $"  %r{n}.{r} = select i1 %h{n}, i32 {(int)values[r]}, i32 {registers[r]}\n");
                registers[r] = $"%r{n}.{r}";
            }

            n++;
        }

        var value = "poison";
        for (var r = 0; r < 4; r++)
        {
            fake.Append(CultureInfo.InvariantCulture, $"  %v{r} = insertvalue {{ i32, i32, i32, i32 }} {value}, i32 {registers[r]}, {r}\n");
            value = $"%v{r}";
        }

        fake.Append(CultureInfo.InvariantCulture, $"  ret {{ i32, i32, i32, i32 }} {value}\n}}\n\ndefine internal {{ i32, i32 }} @fake.xgetbv(i32 %c) {{\n");
        fake.Append(xcr0 is { } saved
            ? string.Create(CultureInfo.InvariantCulture, $"  %x = insertvalue {{ i32, i32 }} {{ i32 0, i32 0 }}, i32 {(int)saved}, 0\n  ret {{ i32, i32 }} %x\n}}\n")
            : "  call void @llvm.trap()\n  unreachable\n}\n" + (ir.Contains("declare void @llvm.trap()", StringComparison.Ordinal) ? string.Empty : "\ndeclare void @llvm.trap()\n"));
        return ir.Replace(Cpuid, "call { i32, i32, i32, i32 } @fake.cpuid", StringComparison.Ordinal)
            .Replace(Xgetbv, "call { i32, i32 } @fake.xgetbv", StringComparison.Ordinal) + fake;
    }

    [Fact]
    public void A_build_for_a_named_cpu_this_machine_has_runs_and_ends_before_the_first_statement_where_the_run_setting_turns_one_of_its_sets_off()
    {
        // section 2, the owner, 2026-11-02 (#39): the line names the CPU option and the sets by clang's names, with the variable that
        // turned a set off, and the program ends with 1 before its first statement; only `0` turns a set off
        var v3 = toolchain.Clang.DescribeCpu("x86-64-v3", x86: true, out _)!;
        Assert.Equal(
            ["__AVX2__", "__AVX__", "__BMI2__", "__BMI__", "__CRC32__", "__F16C__", "__FMA__", "__GCC_HAVE_SYNC_COMPARE_AND_SWAP_16", "__LAHF_SAHF__", "__LZCNT__", "__MOVBE__", "__POPCNT__", "__SSE3__", "__SSE4_1__", "__SSE4_2__", "__SSSE3__", "__XSAVE__"],
            v3.Sets);
        var native = toolchain.Clang.DescribeCpu("native", x86: true, out _)!;
        Assert.True(v3.Sets.All(native.Sets.Contains), "this machine lacks a set of x86-64-v3, so a build for it does not run here");

        var program = Compile("print(\"first\")\n", toolchain.Runtime, v3).Program!;
        var functions = Functions(program.Ir);
        Assert.Contains("\"target-cpu\"=\"x86-64\"", functions["main"].Header, StringComparison.Ordinal); // nothing built for v3 runs before the check
        Assert.Contains("\"target-cpu\"=\"x86-64\"", functions["kz.cpu.read"].Header, StringComparison.Ordinal);
        Assert.Contains("\"target-cpu\"=\"x86-64\"", functions["kz.cpu.setting"].Header, StringComparison.Ordinal);
        Assert.DoesNotContain("@kz.cpu =", program.Ir, StringComparison.Ordinal); // no test reads the word: v3 answers them all
        var executable = toolchain.Build(program, "start-v3");
        Ran(RunWith(executable), 0, ["first"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_AVX2", "1")), 0, ["first"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_AVX2", "00")), 0, ["first"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE2", "0")), 0, ["first"], ""); // the baseline is not turned off
        Ran(RunWith(executable, ("KURZ_INTRINSICS_AVX2", "0")), 1, [], "error: cpu-lacks-set (x86-64-v3: avx2 turned off by KURZ_INTRINSICS_AVX2)\n");

        // a set turned off turns off the sets that derive from it, in the group of its variable, a set turned off by two in the group of
        // the nearest; one with no class of its own only by the variable of every set
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE42", "0")), 1, [], "error: cpu-lacks-set (x86-64-v3: sse4.2, popcnt, avx, fma, avx2 turned off by KURZ_INTRINSICS_SSE42)\n");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_AVX2", "0"), ("KURZ_INTRINSICS_AVX", "0")), 1, [], "error: cpu-lacks-set (x86-64-v3: avx, fma turned off by KURZ_INTRINSICS_AVX; avx2 turned off by KURZ_INTRINSICS_AVX2)\n");
        Ran(
            RunWith(executable, ("KURZ_INTRINSICS", "0")),
            1,
            [],
            "error: cpu-lacks-set (x86-64-v3: " + string.Join(", ", V3Names) + " turned off by KURZ_INTRINSICS)\n");

        // a class's variable is nearer than the one of every set, whose group comes last
        Ran(
            RunWith(executable, ("KURZ_INTRINSICS", "0"), ("KURZ_INTRINSICS_AVX2", "0")),
            1,
            [],
            "error: cpu-lacks-set (x86-64-v3: avx2 turned off by KURZ_INTRINSICS_AVX2; " + string.Join(", ", V3Names.Where(s => s != "avx2")) + " turned off by KURZ_INTRINSICS)\n");

        // no CPU named, or the baseline: no check and no read
        Assert.DoesNotContain("@kz.cpu", Ir("print(\"first\")\n"), StringComparison.Ordinal);
        var baseline = toolchain.Clang.DescribeCpu("x86-64", x86: true, out _)!;
        Assert.Empty(baseline.Sets);
        var plain = Compile("print(\"first\")\n", toolchain.Runtime, baseline).Program!;
        Assert.DoesNotContain("@kz.cpu", plain.Ir, StringComparison.Ordinal);
        Assert.DoesNotContain("target-cpu", Functions(plain.Ir)["main"].Header, StringComparison.Ordinal);
    }

    [Fact]
    public void The_run_setting_turns_a_set_off_for_the_tests_of_the_word_with_every_set_that_derives_from_it()
    {
        // section 13, the owner, 2026-11-02 (#39), as .NET's `DOTNET_EnableHWIntrinsic`: read once, with the CPU's word
        HasTheSets();
        const string text = "print(\"first\")\nif X86.Sse41.IsSupported {\n    print(\"sse41\")\n} else {\n    print(\"plain\")\n}\nif X86.Sha.IsSupported {\n    print(\"sha\")\n}\n";
        var unnamed = Compile(text, toolchain.Runtime).Program!;
        Assert.DoesNotContain("cpu-lacks-set", unnamed.Ir, StringComparison.Ordinal); // no CPU named: no check
        var executable = toolchain.Build(unnamed, "start-word");
        Ran(RunWith(executable), 0, ["first", "sse41", "sha"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE41", "0")), 0, ["first", "plain", "sha"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE3", "0")), 0, ["first", "plain", "sha"], ""); // SSE4.1 derives from SSE3
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE42", "0")), 0, ["first", "sse41", "sha"], ""); // and not from SSE4.2
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SHA", "0")), 0, ["first", "sse41"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS_SSE41", "1")), 0, ["first", "sse41", "sha"], "");
        Ran(RunWith(executable, ("KURZ_INTRINSICS", "0")), 0, ["first", "plain"], "");

        // a build for x86-64-v2 answers SSE4.1 and reads SHA from the word: the setting turns SHA off there, and turning SSE4.1 off
        // fails the start check before the first statement
        var v2 = Compile(text, toolchain.Runtime, toolchain.Clang.DescribeCpu("x86-64-v2", x86: true, out _)!).Program!;
        Assert.Contains("load i64, ptr @kz.cpu", Functions(v2.Ir)["kz.main"].Body, StringComparison.Ordinal);
        var built = toolchain.Build(v2, "start-word-v2");
        Ran(RunWith(built), 0, ["first", "sse41", "sha"], "");
        Ran(RunWith(built, ("KURZ_INTRINSICS_SHA", "0")), 0, ["first", "sse41"], "");
        Ran(RunWith(built, ("KURZ_INTRINSICS_SSE41", "0")), 1, [], "error: cpu-lacks-set (x86-64-v2: sse4.1, sse4.2, popcnt turned off by KURZ_INTRINSICS_SSE41)\n");
    }

    [Fact]
    public void Every_x86_64_cpu_clang_knows_has_only_sets_the_start_check_reads()
    {
        // the owner, 2026-11-02 (#39): a set the table lacks fails the build, so every CPU of the pinned clang must be read in full
        var clang = toolchain.Clang;
        var (code, listed, list) = Processes.Capture(clang.Path, ["--print-supported-cpus"], null, TimeSpan.FromSeconds(30));
        Assert.Equal(0, code);
        var names = System.Text.RegularExpressions.Regex.Matches((listed + list).ReplaceLineEndings("\n"), @"^\t(\S+)$", System.Text.RegularExpressions.RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        var described = names.AsParallel().Select(name => (Name: name, Cpu: clang.DescribeCpu(name, x86: true, out _))).Where(d => d.Cpu is not null).ToList();
        Assert.True(described.Count >= 70, $"clang listed {names.Count} CPUs and described {described.Count} for x86-64"); // the list holds 32-bit CPUs too, which a 64-bit build does not know
        Assert.Contains(described, d => d.Name == "x86-64-v4");
        var unread = described.SelectMany(d => d.Cpu!.Sets.Where(m => !CpuSets.All.ContainsKey(m)).Select(m => $"{d.Name}: {m}")).Order(StringComparer.Ordinal).ToList();
        Assert.Empty(unread);
        Assert.Contains("__AVX512F__", clang.DescribeCpu("x86-64-v4", x86: true, out _)!.Sets); // the sets are read, not empty
    }
}
