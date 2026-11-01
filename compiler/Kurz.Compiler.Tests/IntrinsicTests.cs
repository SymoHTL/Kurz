using System.Globalization;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// R18's decisions of the emitter that no corpus case pins (section 13; the owner, 2026-11-01, "Guarded block" and "Copied per
/// feature"): a guarded block compiled as a function of its own for its sets, called after a test of the CPU's word, which the
/// module reads once; a test the build's CPU answers, constant, with the branch it skips left out; a function such a block calls
/// compiled again for its sets, and every function that copy calls, the runtime's among them, while a function value is not; a run
/// guarded by a class under <c>Arm</c> never reached on x86-64, and on an Arm machine a test of one <c>not supported</c>; an
/// intrinsic in <c>raw</c> without a test, a call of a function compiled for its set; the ways out of a guarded block, under the
/// allocator that makes every freed block inaccessible; every intrinsic giving what .NET's gives; and an element outside the lanes.
/// </summary>
public sealed partial class IntrinsicTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime, Cpu? cpu = null, params string[] grants) =>
        Builder.Compile(SourceText.FromString("program.kz", text), grants, runtime, testBuild: true, cpu: cpu is null ? null : () => cpu);

    private string Ir(string text, Cpu? cpu = null, params string[] grants)
    {
        var compilation = Compile(text, toolchain.Runtime, cpu, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return compilation.Program.Ir;
    }

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, CheckedRuntime runtime, string name, params string[] grants)
    {
        var compilation = Compile(text, runtime, null, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    [GeneratedRegex(@"^define [^@]*@(""[^""]*""|[\w.]+)\(.*?$", RegexOptions.Multiline)]
    private static partial Regex Definition();

    /// <summary>Each function the IR defines by its name, unquoted, with its line of <c>define</c> and its body up to its closing brace.</summary>
    private static Dictionary<string, (string Header, string Body)> Functions(string ir)
    {
        var functions = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (Match m in Definition().Matches(ir))
        {
            var end = Regex.Match(ir[m.Index..], @"\r?\n}").Index; // the IR ends its lines in CR LF on Windows
            functions[m.Groups[1].Value.Trim('"')] = (m.Value, ir.Substring(m.Index + m.Length, end - m.Length));
        }

        Assert.NotEmpty(functions);
        return functions;
    }

    /// <summary>The CPU of a build that names one with x86-64's baseline and the classes given.</summary>
    private static Cpu CpuWith(params string[] classes) => new("test", [.. Cpu.Baseline.Classes, .. classes], []);

    /// <summary>Whether this machine has the sets the runs below take the guarded paths of: SSE4.1, AES and, by cpuid's leaf 7, SHA.</summary>
    private static void HasTheSets() =>
        Assert.True(Sse41.IsSupported && Aes.IsSupported && (X86Base.CpuId(7, 0).Ebx & (1 << 29)) != 0, "this machine lacks SSE4.1, AES or SHA, so the guarded paths are not run here");

    private const string Guarded = """
        v = Vector128.Create(1, 2, 3, 4)
        if X86.Sse41.IsSupported {
            print(X86.Sse41.Extract(v, 2))
        } else {
            print("plain")
        }

        """;

    [Fact]
    public void A_guarded_block_is_a_function_of_its_own_for_its_sets_called_after_a_test_of_the_word_the_module_reads_once()
    {
        var ir = Ir(Guarded);
        var functions = Functions(ir);
        var (header, body) = Assert.Single(functions, f => f.Key.StartsWith("f.main.guarded.", StringComparison.Ordinal)).Value;
        Assert.Contains("\"target-features\"=\"+sse,+sse2,+sse3,+sse4.1,+ssse3\"", header, StringComparison.Ordinal);
        Assert.Contains("extractelement <4 x i32>", body, StringComparison.Ordinal);
        var main = functions["kz.main"].Body;
        Assert.Contains("load i64, ptr @kz.cpu", main, StringComparison.Ordinal);
        Assert.Contains("and i64 ", main, StringComparison.Ordinal);
        Assert.Contains(", 524801", main, StringComparison.Ordinal); // SSE4.1's bit 19 with SSSE3's 9 and SSE3's 0 of ECX, leaf 1
        Assert.Contains("call i32 @\"f.main.guarded.", main, StringComparison.Ordinal);
        Assert.DoesNotContain("extractelement", main, StringComparison.Ordinal);
        Assert.DoesNotContain("target-features", functions["kz.main"].Header, StringComparison.Ordinal);
        var start = functions["main"].Body;
        Assert.True(start.IndexOf("call void @kz.cpu.read()", StringComparison.Ordinal) is >= 0 and var read && read < start.IndexOf(".Start", StringComparison.Ordinal), start);
        Assert.Contains("asm \"cpuid\"", functions["kz.cpu.read"].Body, StringComparison.Ordinal);

        // the right side of `&&` after a test is guarded too, as a function of its own: no intrinsic is called through a wrapper
        var right = Functions(Ir("v = Vector128.Create(1, 2, 3, 4)\nif X86.Sse41.IsSupported && X86.Sse41.Extract(v, 0) == 1 {\n    print(1)\n}\n"));
        Assert.Contains(right, f => f.Key.StartsWith("f.main.guarded.", StringComparison.Ordinal) && f.Value.Body.Contains("extractelement <4 x i32>", StringComparison.Ordinal));
        Assert.DoesNotContain(right.Keys, k => k.StartsWith("intrinsic.", StringComparison.Ordinal));
        Assert.DoesNotContain("extractelement", right["kz.main"].Body, StringComparison.Ordinal);

        // a program whose tests the baseline answers reads no word: SSE2 is in every x86-64 CPU
        Assert.DoesNotContain("@kz.cpu", Ir("print(1)\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("@kz.cpu", Ir("print(X86.Sse2.IsSupported)\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Where_the_build_names_a_cpu_that_has_the_set_the_test_is_true_and_the_else_is_dropped()
    {
        var named = Ir(Guarded, CpuWith("X86.Sse3", "X86.Ssse3", "X86.Sse41"));
        Assert.DoesNotContain("@kz.cpu", named, StringComparison.Ordinal);
        Assert.DoesNotContain("guarded", named, StringComparison.Ordinal);
        Assert.DoesNotContain("plain", named, StringComparison.Ordinal);
        Assert.Contains("extractelement <4 x i32>", Functions(named)["kz.main"].Body, StringComparison.Ordinal);

        // the positive control: the baseline keeps the else, and a CPU without SSE4.1 does too
        Assert.Contains("plain", Ir(Guarded), StringComparison.Ordinal);
        Assert.Contains("plain", Ir(Guarded, CpuWith("X86.Sse3", "X86.Ssse3")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_cpu_reaches_clang_and_a_guarded_function_carries_its_features_in_front_of_the_blocks()
    {
        // a function's features replace those the driver passes for the CPU clang was given, so a block's function names the driver's
        // first and its sets' after them
        const string text = "v = Vector128.Create(uint(1))\nif X86.Sha.IsSupported {\n    print(X86.Sha.Sha256MessageSchedule1(v, v).GetElement(0))\n}\n";
        var made = Compile(text, toolchain.Runtime, new Cpu("made", Cpu.Baseline.Classes, ["+cx16", "-avx512f"])).Program!;
        Assert.Equal("-march=made", made.CpuFlag);
        var header = Assert.Single(Functions(made.Ir), f => f.Key.StartsWith("f.main.guarded.", StringComparison.Ordinal)).Value.Header;
        Assert.Contains("\"target-features\"=\"+cx16,-avx512f,+sha,+sse,+sse2\"", header, StringComparison.Ordinal);
        Assert.Null(Compile(text, toolchain.Runtime).Program!.CpuFlag);

        // `native` as clang describes this machine, which has SHA: the test is `true`, clang gets the CPU, and the program runs
        HasTheSets();
        var native = Compile(text, toolchain.Runtime, toolchain.Clang.DescribeCpu("native", x86: true, out _)).Program!;
        Assert.Equal("-march=native", native.CpuFlag);
        Assert.DoesNotContain("@kz.cpu", native.Ir, StringComparison.Ordinal);

        // a SHA instruction has no VEX form, so on a CPU with AVX a run of statements a test of `X86.Sha` guards starts by cleaning the
        // upper halves of the YMM registers, once; on a CPU without AVX nothing does
        Assert.True(System.Runtime.Intrinsics.X86.Avx.IsSupported, "this machine lacks AVX, so `native` has no VEX form to clean up after");
        var main = Functions(native.Ir)["kz.main"].Body;
        Assert.True(main.IndexOf("call void @llvm.x86.avx.vzeroupper()", StringComparison.Ordinal) is >= 0 and var clean && clean < main.IndexOf("@llvm.x86.sha256msg1", StringComparison.Ordinal), main);
        Assert.Equal(1, main.Split("vzeroupper").Length - 1);
        Assert.DoesNotContain("vzeroupper", made.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(native, "intrinsic-native")), 0, [(1u + (uint.RotateRight(1u, 7) ^ uint.RotateRight(1u, 18) ^ (1u >> 3))).ToString(CultureInfo.InvariantCulture)], "");
    }

    [Fact]
    public void On_a_cpu_with_avx_a_run_a_test_of_sha_guards_cleans_the_upper_halves_once_and_a_function_it_calls_does_not()
    {
        // under `native`, on this machine with AVX, the first statement of a run that a test of `X86.Sha` guards is preceded by
        // `vzeroupper`: once a run, not before each statement, not in a function the run calls, which a loop may run, and not for a run
        // another class guards; the code after a run that cleaned cleans again, since another path may reach it
        const string text = """
            Vector128<uint> Next(Vector128<uint> w) {
                mut Vector128<uint> next = w
                raw {
                    next = X86.Sha.Sha256MessageSchedule1(w, w)
                }

                return next
            }

            void Twice(Vector128<uint> u, bool first) {
                if first {
                    if X86.Sha.IsSupported {
                        print(X86.Sha.Sha256MessageSchedule1(u, u).GetElement(0))
                    }
                }

                if !X86.Sha.IsSupported {
                    return
                }

                print(X86.Sha.Sha256MessageSchedule1(u, u).GetElement(1))
            }

            n = Vector128.Create(1, 2, 3, 4)
            if X86.Sse41.IsSupported {
                print(X86.Sse41.Extract(n, 1))
            }

            mut v = Vector128.Create(uint(1))
            if X86.Sha.IsSupported {
                mut i = 0
                while i < 3 {
                    v = Next(v)
                    i = i + 1
                }

                print(v.GetElement(0))
            }

            Twice(v, true)

            """;
        HasTheSets();
        Assert.True(System.Runtime.Intrinsics.X86.Avx.IsSupported, "this machine lacks AVX, so `native` has no VEX form to clean up after");
        var native = Compile(text, toolchain.Runtime, toolchain.Clang.DescribeCpu("native", x86: true, out _), "raw").Program!;
        var functions = Functions(native.Ir);
        static int Cleans(string body) => body.Split("call void @llvm.x86.avx.vzeroupper()").Length - 1;
        Assert.Equal(1, Cleans(functions["kz.main"].Body));
        Assert.Equal(0, Cleans(Assert.Single(functions, f => f.Key.StartsWith("f.Next.", StringComparison.Ordinal)).Value.Body));
        Assert.Equal(2, Cleans(Assert.Single(functions, f => f.Key.StartsWith("f.Twice.", StringComparison.Ordinal)).Value.Body));
        static uint Sigma0(uint x) => uint.RotateRight(x, 7) ^ uint.RotateRight(x, 18) ^ (x >> 3);
        var x = 1u;
        for (var i = 0; i < 3; i++)
        {
            x = unchecked(x + Sigma0(x));
        }

        var y = unchecked(x + Sigma0(x)).ToString(CultureInfo.InvariantCulture);
        Ran(Toolchain.Run(toolchain.Build(native, "intrinsic-clean")), 0, ["2", x.ToString(CultureInfo.InvariantCulture), y, y], "");
    }

    [Fact]
    public void A_function_a_guarded_block_calls_is_compiled_again_for_its_sets_with_every_function_it_calls_but_a_function_value()
    {
        const string text = """
            int Inner(Vector128<int> lanes) {
                if X86.Sse41.IsSupported {
                    return X86.Sse41.Extract(lanes, 1)
                }

                return lanes.GetElement(1)
            }

            int Outer(Vector128<int> lanes) => Inner(lanes)

            int Counted() {
                mut m = Map<int, int>()
                m[7] = 1
                return m.Count
            }

            v = Vector128.Create(1, 2, 3, 4)
            seven = () => 7
            print(Outer(v))
            if X86.Sse41.IsSupported {
                print(Outer(v) + seven() + Counted())
                made = () => X86.Sse41.Extract(Vector128.Create(5, 6, 7, 8), 0)
                print(made())
            }

            """;
        var functions = Functions(Ir(text));
        const string Features = "\"target-features\"=\"+sse,+sse2,+sse3,+sse4.1,+ssse3\"";
        foreach (var name in new[] { "f.Inner.1", "f.Outer.9" })
        {
            Assert.DoesNotContain("target-features", functions[name].Header, StringComparison.Ordinal);
            Assert.Contains(Features, functions[name + ".for.X86.Sse41"].Header, StringComparison.Ordinal);
        }

        // the copy's own test is `true`: no read of the word, no block of its own; the plain function tests and calls its block
        Assert.DoesNotContain("@kz.cpu", functions["f.Inner.1.for.X86.Sse41"].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("guarded", functions["f.Inner.1.for.X86.Sse41"].Body, StringComparison.Ordinal);
        Assert.Contains("load i64, ptr @kz.cpu", functions["f.Inner.1"].Body, StringComparison.Ordinal);
        Assert.Contains("call i32 @\"f.Inner.guarded.", functions["f.Inner.1"].Body, StringComparison.Ordinal);

        // the runtime's functions the block reaches are copied too (the owner, 2026-11-01: the copy is transitive)
        Assert.Contains(functions, f => f.Key.StartsWith("rt.common.Print.", StringComparison.Ordinal) && f.Key.EndsWith(".for.X86.Sse41", StringComparison.Ordinal));

        // a lambda the block calls through its value is compiled once, for what it was made in: the one made outside for the CPU
        // alone, the one made in the block for the block's sets, with its intrinsic in place
        var lambdas = functions.Where(f => f.Key.Contains(".lambda.", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lambdas.Count);
        var made = Assert.Single(lambdas, f => f.Value.Header.Contains(Features, StringComparison.Ordinal));
        Assert.Contains("extractelement <4 x i32>", made.Value.Body, StringComparison.Ordinal);
        Assert.Single(lambdas, f => !f.Value.Header.Contains("target-features", StringComparison.Ordinal));

        // nothing else carries features: not the helpers a copy calls (retain, release), nor any function a copy is not of
        var featured = functions.Where(f => f.Value.Header.Contains("target-features", StringComparison.Ordinal)).Select(f => f.Key).ToList();
        Assert.All(featured, name => Assert.True(name.EndsWith(".for.X86.Sse41", StringComparison.Ordinal) || name.Contains(".guarded.", StringComparison.Ordinal) || name == made.Key, name));
        Assert.Contains(featured, name => name.Contains(".guarded.", StringComparison.Ordinal));

        // and none of the others calls a copy: a helper serves every caller, so what it calls is compiled for the CPU alone, the map's
        // helpers among them, which the copy of `Counted` is the first to call
        Assert.All(functions.Where(f => !featured.Contains(f.Key)), f => Assert.False(f.Value.Body.Contains(".for.", StringComparison.Ordinal), f.Key));
        Assert.Contains(featured, name => functions[name].Body.Contains(".for.", StringComparison.Ordinal));
        Assert.Contains("f.Counted.11.for.X86.Sse41", featured);
        Assert.Contains(functions.Keys, k => k.StartsWith("set.", StringComparison.Ordinal));
        Assert.Contains(functions.Keys, k => k.StartsWith("retain.", StringComparison.Ordinal) || k.StartsWith("release.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_test_of_a_class_under_arm_is_false_on_x86_64_and_what_it_guards_is_not_compiled()
    {
        const string text = """
            v = Vector128.Create(1, 2, 3, 4)
            if Arm.AdvSimd.IsSupported {
                print(Arm.AdvSimd.Add(v, v).GetElement(0))
            } else {
                print("x86")
            }

            while Arm.AdvSimd.IsSupported && Arm.AdvSimd.Add(v, v).GetElement(1) == 4 {
                print(Arm.AdvSimd.Add(v, v).GetElement(2))
            }

            if !Arm.AdvSimd.IsSupported {
                print("done")
                return
            }

            print(Arm.AdvSimd.Add(v, v).GetElement(3))

            """;
        var ir = Ir(text);
        Assert.DoesNotContain("@kz.cpu", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("guarded", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("add <4 x i32>", ir, StringComparison.Ordinal);
        var compilation = Compile(text, toolchain.Runtime);
        Ran(Toolchain.Run(toolchain.Build(compilation.Program!, "intrinsic-arm")), 0, ["x86", "done"], "");
    }

    [Fact]
    public void On_an_arm_machine_a_test_of_an_x86_class_is_false_and_one_of_an_arm_class_is_not_supported()
    {
        var ir = Ir(Guarded, Cpu.ArmBaseline);
        Assert.DoesNotContain("@kz.cpu", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("guarded", ir, StringComparison.Ordinal);
        Assert.Contains("plain", ir, StringComparison.Ordinal);
        var compilation = Compile("v = Vector128.Create(1, 2, 3, 4)\nprint(1)\nif Arm.AdvSimd.IsSupported {\n    print(Arm.AdvSimd.Add(v, v).GetElement(0))\n}\n", toolchain.Runtime, Cpu.ArmBaseline);
        Assert.Null(compilation.Program);
        var d = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 3, "a test of `Arm.AdvSimd`, a class whose intrinsics the compiler builds for x86-64 alone (R18)"), (d.Kind, d.Line, d.Message));
    }

    [Fact]
    public void An_intrinsic_in_raw_without_a_test_is_a_call_of_a_function_compiled_for_its_set_and_one_of_the_baseline_stands_in_place()
    {
        const string text = "v = Vector128.Create(1, 2, 3, 4)\nraw {\n    print(X86.Sse41.Extract(v, 2))\n    print(X86.Sse2.Add(v, v).GetElement(3))\n}\n";
        var functions = Functions(Ir(text, null, "raw"));
        var wrapper = Assert.Single(functions, f => f.Key.StartsWith("intrinsic.", StringComparison.Ordinal));
        Assert.Equal("intrinsic.X86.Sse41.Extract(Vector128<int> value, byte index).2", wrapper.Key);
        Assert.Contains("\"target-features\"=\"+sse,+sse2,+sse3,+sse4.1,+ssse3\"", wrapper.Value.Header, StringComparison.Ordinal);
        Assert.Contains("call i32 @\"intrinsic.X86.Sse41.Extract", functions["kz.main"].Body, StringComparison.Ordinal);
        Assert.Contains("add <4 x i32>", functions["kz.main"].Body, StringComparison.Ordinal);
        HasTheSets();
        Ran(BuildAndRun(text, toolchain.Runtime, "intrinsic-raw", "raw"), 0, ["3", "8"], "");
    }

    [Fact]
    public void A_guarded_block_leaves_by_return_break_and_continue_and_holds_what_it_reads_under_the_fenced_allocator()
    {
        // `Pick` breaks and continues its loop from the block; `Name` returns a string it made after a test that leaves; the right
        // sides of `&&` and `||`, an `else if` and a lambda are guarded; a block inside a block adds SHA to SSE4.1
        const string text = """
            int Pick(List<int> xs) {
                mut total = 0
                for x in xs {
                    if X86.Sse41.IsSupported {
                        if x == 3 {
                            continue
                        }

                        if x == 6 {
                            break
                        }

                        Vector128<int> v = Vector128.Create(x, x + 1, x + 2, x + 3)
                        total = total + X86.Sse41.Extract(v, 3)
                    }
                }

                return total
            }

            string Name(int n) {
                if !X86.Ssse3.IsSupported {
                    return "none"
                }

                mut s = "lanes"
                Vector128<byte> v = X86.Ssse3.Shuffle(Vector128.Create(byte(n)), Vector128.Create(byte(0)))
                s = "{s} {v.GetElement(15)}"
                return s
            }

            print(Pick([1, 2, 3, 4, 5, 6, 7]))
            print(Name(7))
            u = Vector128.Create(1, 2, 3, 4)
            if X86.Sse41.IsSupported && X86.Sse41.Extract(u, 0) == 1 {
                print("and")
            }

            if !X86.Sse41.IsSupported || X86.Sse41.Extract(u, 1) == 2 {
                print("or")
            }

            if X86.Sse41.IsSupported {
                print([1, 2, 3].Where(x => X86.Sse41.Extract(Vector128.Create(x, x, x, x), 0) > 1).Count)
            }

            if !X86.Sse41.IsSupported {
                print("old")
            } else if X86.Aes.IsSupported {
                print(X86.Aes.Encrypt(u.AsByte(), u.AsByte()).GetElement(0))
            }

            if X86.Sse41.IsSupported {
                if X86.Sha.IsSupported {
                    print(X86.Sse41.Extract(X86.Sha.Sha256MessageSchedule1(u.AsUInt32(), u.AsUInt32()), 0))
                }
            }

            """;
        HasTheSets();
        var aes = Aes.Encrypt(Vector128.Create(1, 2, 3, 4).AsByte(), Vector128.Create(1, 2, 3, 4).AsByte()).GetElement(0);

        // SHA256MSG1's first word: W0 + σ0(W1), with W0 = 1 and W1 = 2 (Intel's guide)
        var schedule = 1u + (uint.RotateRight(2u, 7) ^ uint.RotateRight(2u, 18) ^ (2u >> 3));
        Ran(BuildAndRun(text, ListTests.Fenced, "intrinsic-flow"), 0, ["24", "lanes 7", "and", "or", "2", aes.ToString(CultureInfo.InvariantCulture), schedule.ToString(CultureInfo.InvariantCulture)], "");
    }

    [Fact]
    public void Every_intrinsic_gives_what_dotnets_gives_on_the_same_lanes()
    {
        var a = Vector128.Create(Enumerable.Range(0, 16).Select(i => (byte)((i * 37 + 11) % 256)).ToArray());
        var b = Vector128.Create(Enumerable.Range(0, 16).Select(i => (byte)((i * 53 + 200) % 256)).ToArray());
        static string Bytes(Vector128<byte> v) => string.Concat(Enumerable.Range(0, 16).Select(i => " " + v.GetElement(i).ToString(CultureInfo.InvariantCulture)));
        static string Create(Vector128<byte> v) => "Vector128.Create(" + string.Join(", ", Enumerable.Range(0, 16).Select(i => $"byte({v.GetElement(i).ToString(CultureInfo.InvariantCulture)})")) + ")";
        var text = $$"""
            string Lanes(Vector128<byte> v) {
                mut s = ""
                for i in 0..<16 {
                    s = "{s} {v.GetElement(i)}"
                }

                return s
            }

            a = {{Create(a)}}
            b = {{Create(b)}}
            print(Lanes(a))
            print(Lanes(Vector128.Create(byte(9))))
            if X86.Sse2.IsSupported {
                print(Lanes(X86.Sse2.Add(a, b)))
                print(Lanes(X86.Sse2.Add(a.AsInt64(), b.AsInt64()).AsByte()))
                print(Lanes(X86.Sse2.Shuffle(a.AsUInt32(), 0x1B).AsByte()))
                print(Lanes(X86.Sse2.Shuffle(a.AsInt32(), 0xB1).AsByte()))
                print(X86.Sse2.Add(Vector128.Create(2147483647, 0, 0, 0), Vector128.Create(1, 0, 0, 0)).GetElement(0))
                print(X86.Sse2.Add(Vector128.Create(1.5, 2.25), Vector128.Create(0.25, 0.5)).GetElement(1) == 2.75)
            }

            print(Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8).GetElement(7))
            print(Vector256.Create(byte(1)).AsUInt64().GetElement(3))
            mut Vector128<uint> words = Vector128.Create(uint(1), uint(2), uint(3), uint(4))
            raw {
                uint* p = Ptr<uint>(&words)
                X86.Sse2.Store(p, X86.Sse2.Add(X86.Sse2.LoadVector128(p), X86.Sse2.LoadVector128(p)))
            }

            print(words.GetElement(3))
            if X86.Sse41.IsSupported && X86.Aes.IsSupported {
                print(Lanes(X86.Ssse3.Shuffle(a, b)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 0)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 4)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 16)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 20)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 31)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 32)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 40)))
                print(Lanes(X86.Ssse3.AlignRight(a, b, 255)))
                print(Lanes(X86.Ssse3.AlignRight(a.AsUInt32(), b.AsUInt32(), 12).AsByte()))
                print(X86.Sse41.Extract(a, 13))
                print(X86.Sse41.Extract(a.AsInt32(), 6))
                print(X86.Sse41.Extract(a.AsUInt32(), 3))
                print(X86.Sse41.Extract(a.AsSingle(), 1) == a.AsSingle().GetElement(1))
                print(Lanes(X86.Sse41.Blend(a.AsUInt16(), b.AsUInt16(), 0xA5).AsByte()))
                print(Lanes(X86.Aes.Encrypt(a, b)))
                print(Lanes(X86.Aes.EncryptLast(a, b)))
                print(Lanes(X86.Aes.Decrypt(a, b)))
                print(Lanes(X86.Aes.DecryptLast(a, b)))
                print(Lanes(X86.Aes.InverseMixColumns(a)))
                print(Lanes(X86.Aes.KeygenAssist(a, 0x1B)))
            }

            """;
        HasTheSets();
        string[] expected =
        [
            Bytes(a),
            Bytes(Vector128.Create((byte)9)),
            Bytes(Sse2.Add(a, b)),
            Bytes(Sse2.Add(a.AsInt64(), b.AsInt64()).AsByte()),
            Bytes(Sse2.Shuffle(a.AsUInt32(), 0x1B).AsByte()),
            Bytes(Sse2.Shuffle(a.AsInt32(), 0xB1).AsByte()),
            Sse2.Add(Vector128.Create(int.MaxValue, 0, 0, 0), Vector128.Create(1, 0, 0, 0)).GetElement(0).ToString(CultureInfo.InvariantCulture), // the lanes wrap, as C#'s
            "true",
            "8",
            "72340172838076673",
            "8",
            Bytes(Ssse3.Shuffle(a, b)),
            Bytes(Ssse3.AlignRight(a, b, 0)),
            Bytes(Ssse3.AlignRight(a, b, 4)),
            Bytes(Ssse3.AlignRight(a, b, 16)),
            Bytes(Ssse3.AlignRight(a, b, 20)),
            Bytes(Ssse3.AlignRight(a, b, 31)),
            Bytes(Ssse3.AlignRight(a, b, 32)),
            Bytes(Ssse3.AlignRight(a, b, 40)),
            Bytes(Ssse3.AlignRight(a, b, 255)),
            Bytes(Ssse3.AlignRight(a.AsUInt32(), b.AsUInt32(), 12).AsByte()),
            Sse41.Extract(a, 13).ToString(CultureInfo.InvariantCulture),
            Sse41.Extract(a.AsInt32(), 6).ToString(CultureInfo.InvariantCulture), // the instruction reads the immediate's low bits: lane 2
            Sse41.Extract(a.AsUInt32(), 3).ToString(CultureInfo.InvariantCulture),
            "true",
            Bytes(Sse41.Blend(a.AsUInt16(), b.AsUInt16(), 0xA5).AsByte()),
            Bytes(Aes.Encrypt(a, b)),
            Bytes(Aes.EncryptLast(a, b)),
            Bytes(Aes.Decrypt(a, b)),
            Bytes(Aes.DecryptLast(a, b)),
            Bytes(Aes.InverseMixColumns(a)),
            Bytes(Aes.KeygenAssist(a, 0x1B)),
        ];
        Ran(BuildAndRun(text, toolchain.Runtime, "intrinsic-ops", "raw"), 0, expected, "");
    }

    [Fact]
    public void The_sha1_instructions_compress_a_block_as_sha1_does()
    {
        // .NET has no `X86.Sha`, so its SHA-1 is the oracle of the four SHA-1 functions: the block of `abc`, compressed as Intel's sample
        // compresses one (each message schedule, each of the four round functions, and the next `e` of every group of four rounds)
        const string text = """
            Vector128<uint> Xor(Vector128<uint> a, Vector128<uint> b) =>
                Vector128.Create(a.GetElement(0) ^ b.GetElement(0), a.GetElement(1) ^ b.GetElement(1), a.GetElement(2) ^ b.GetElement(2), a.GetElement(3) ^ b.GetElement(3))

            Vector128<uint> Words(Bytes message, int at) {
                Vector128<byte> bytes = Vector128.Create(message[at], message[at + 1], message[at + 2], message[at + 3], message[at + 4], message[at + 5], message[at + 6], message[at + 7], message[at + 8], message[at + 9], message[at + 10], message[at + 11], message[at + 12], message[at + 13], message[at + 14], message[at + 15])
                if !X86.Ssse3.IsSupported {
                    return bytes.AsUInt32()
                }

                return X86.Ssse3.Shuffle(bytes, Vector128.Create(byte(15), byte(14), byte(13), byte(12), byte(11), byte(10), byte(9), byte(8), byte(7), byte(6), byte(5), byte(4), byte(3), byte(2), byte(1), byte(0))).AsUInt32()
            }

            mut block = Bytes()
            block.Add(byte(97))
            block.Add(byte(98))
            block.Add(byte(99))
            block.Add(byte(128))
            while block.Count < 63 {
                block.Add(byte(0))
            }

            block.Add(byte(24))
            if X86.Sha.IsSupported {
                mut w0 = Words(block, 0)
                mut w1 = Words(block, 16)
                mut w2 = Words(block, 32)
                mut w3 = Words(block, 48)
                Vector128<uint> start = X86.Sse2.Shuffle(Vector128.Create(uint(0x67452301), uint(0xEFCDAB89), uint(0x98BADCFE), uint(0x10325476)), 0x1B)
                Vector128<uint> e = Vector128.Create(uint(0), uint(0), uint(0), uint(0xC3D2E1F0))
                mut abcd = start
                mut before = abcd
                mut g = 0
                while g < 20 {
                    mut Vector128<uint> w = w0
                    if g >= 4 {
                        w = X86.Sha.Sha1MessageSchedule2(Xor(X86.Sha.Sha1MessageSchedule1(w0, w1), w2), w3)
                    }

                    w0 = w1
                    w1 = w2
                    w2 = w3
                    w3 = w
                    mut Vector128<uint> next = X86.Sse2.Add(e, w)
                    if g > 0 {
                        next = X86.Sha.Sha1NextE(before, w)
                    }

                    before = abcd
                    if g < 5 {
                        abcd = X86.Sha.Sha1FourRounds(abcd, next, 0)
                    } else if g < 10 {
                        abcd = X86.Sha.Sha1FourRounds(abcd, next, 1)
                    } else if g < 15 {
                        abcd = X86.Sha.Sha1FourRounds(abcd, next, 2)
                    } else {
                        abcd = X86.Sha.Sha1FourRounds(abcd, next, 3)
                    }

                    g = g + 1
                }

                Vector128<uint> last = X86.Sha.Sha1NextE(before, e)
                Vector128<uint> sum = X86.Sse2.Shuffle(X86.Sse2.Add(abcd, start), 0x1B)
                print("{sum.GetElement(0)} {sum.GetElement(1)} {sum.GetElement(2)} {sum.GetElement(3)} {last.GetElement(3)}")
            }

            """;
        HasTheSets();
#pragma warning disable CA5350 // SHA-1 is what the instructions under test compute, and .NET's is the digest they are held to
        var digest = System.Security.Cryptography.SHA1.HashData("abc"u8);
#pragma warning restore CA5350
        var words = Enumerable.Range(0, 5).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(4 * i)).ToString(CultureInfo.InvariantCulture));
        Ran(BuildAndRun(text, toolchain.Runtime, "intrinsic-sha1"), 0, [string.Join(' ', words)], "");
    }

    [Fact]
    public void Every_class_and_the_class_it_derives_from_are_those_of_dotnet_but_sha_which_dotnet_has_not()
    {
        // R18 names C#'s classes; the table holds .NET's of the runtime the suite runs on, and `X86.Sha` as .NET's proposal of it, of
        // `X86.Sse2`, the one class .NET does not have
        var assembly = typeof(System.Runtime.Intrinsics.X86.Sse2).Assembly;
        var read = 0;
        foreach (var c in Kurz.Compiler.Semantics.Intrinsics.Classes.Values.Where(c => c.Name != "X86.Sha"))
        {
            var type = assembly.GetType($"System.Runtime.Intrinsics.{c.Name}");
            Assert.True(type is not null, $"{c.Name} is no class of .NET's");
            var dotnetBase = type.BaseType == typeof(object) ? null : $"{type.BaseType!.Namespace![(type.BaseType.Namespace!.LastIndexOf('.') + 1)..]}.{type.BaseType.Name}";
            Assert.Equal((c.Name, dotnetBase), (c.Name, c.Base));
            read++;
        }

        Assert.Equal(Kurz.Compiler.Semantics.Intrinsics.Classes.Count - 1, read);
        Assert.Null(assembly.GetType("System.Runtime.Intrinsics.X86.Sha"));
        Assert.Equal("X86.Sse2", Kurz.Compiler.Semantics.Intrinsics.Classes["X86.Sha"].Base);
    }

    [Fact]
    public void An_element_outside_the_lanes_is_index_out_of_range_at_its_line()
    {
        const string text = "v = Vector128.Create(1, 2, 3, 4)\nmut i = 3\nprint(v.GetElement(i))\ni = i + 1\nprint(v.GetElement(i))\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "intrinsic-lane"), 1, ["4"], "error: index-out-of-range at program.kz:5\n");
        Ran(BuildAndRun("v = Vector128.Create(1, 2, 3, 4)\nmut i = -1\nprint(v.GetElement(i))\n", toolchain.Runtime, "intrinsic-lane-negative"), 1, [], "error: index-out-of-range at program.kz:3\n");
    }

    [Theory]
    [InlineData("xs = [Vector128.Create(1, 2, 3, 4)]\nprint(xs.Count)\n", 1, "a value of type `List<Vector128<int>>` (M9)")]
    [InlineData("data P(Vector128<int> V)\n\np = P(Vector128.Create(1, 2, 3, 4))\nprint(p.V.GetElement(0))\n", 3, "a value of type `P` (D1)")]
    [InlineData("class P(Vector128<int> V)\n\np = P(Vector128.Create(1, 2, 3, 4))\nprint(p.V.GetElement(0))\n", 3, "a value of type `P` (K1)")]
    [InlineData("v = Vector128.Create(1, 2, 3, 4)\nf = () => v.GetElement(1)\nprint(f())\n", 2, "a lambda that reads a variable of type `Vector128<int>`, whose environment is not aligned to it (R18)")]
    public void A_vector_held_in_a_value_of_another_type_is_not_supported_at_its_line(string text, int line, string message)
    {
        var compilation = Compile(text, toolchain.Runtime);
        Assert.Null(compilation.Program);
        var d = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, line, message), (d.Kind, d.Line, d.Message));
    }

    [Fact]
    public void A_guarded_block_in_a_method_reads_and_changes_the_fields_of_its_instance()
    {
        // K7 with R18: the block's function takes the address of the method's `this` as it takes a variable's, by its name and through `this`
        const string text = "class Acc(mut int Total) {\n    pub int Add(Vector128<int> v) {\n        if X86.Sse41.IsSupported {\n            Total = Total + X86.Sse41.Extract(v, 2)\n            this.Total = this.Total + 1\n            return Total\n        }\n\n        return -1\n    }\n}\n\na = Acc(10)\nprint(a.Add(Vector128.Create(1, 2, 3, 4)))\nprint(a.Total)\n";
        Assert.Contains(Functions(Ir(text)), f => f.Key.Contains(".guarded.", StringComparison.Ordinal) && f.Value.Body.Contains("extractelement <4 x i32>", StringComparison.Ordinal));
        Ran(BuildAndRun(text, ListTests.Fenced, "intrinsic-method"), 0, ["14", "14"], "");
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }
}
