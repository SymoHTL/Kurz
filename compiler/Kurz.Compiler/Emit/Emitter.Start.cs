using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The read of the CPU that the module's <c>main</c> makes once, before the runtime starts (R18; section 2 and section 13, the owner,
/// 2026-11-02): the CPU's word that the tests of the program read, the run setting that turns sets off, <c>KURZ_INTRINSICS=0</c> every
/// set beyond the baseline and <c>KURZ_INTRINSICS_&lt;SET&gt;=0</c> one and the sets that derive from it, each read from the
/// environment once, only <c>0</c> acting, and the start check of a build for a named CPU, which ends the process with
/// <c>error: cpu-lacks-set (&lt;the CPU&gt;: &lt;its sets the CPU lacks&gt;)</c> and the exit code 1 before the first statement, where the
/// CPU lacks a set of the build's CPU, or the setting turned one off, as .NET's Native AOT ends where the instruction sets a build
/// requires are missing. Each set is read as LLVM reads it for <c>native</c> (<see cref="CpuSets"/>). The read and <c>main</c> are
/// compiled for x86-64's baseline, since they run before the check says the CPU has more.
/// </summary>
public sealed partial class Emitter
{
    // clang's own attributes for a function compiled for x86-64's baseline (`-march=x86-64`), which take the place of the module's CPU
    private static readonly string[] BaselineAttributes = ["\"target-cpu\"=\"x86-64\"", "\"target-features\"=\"+cmov,+cx8,+fxsr,+mmx,+sse,+sse2,+x87\""];

    private const string Cpuid = "call { i32, i32, i32, i32 } asm \"cpuid\", \"={ax},={bx},={cx},={dx},{ax},{cx}\"";

    private const string Setting = "KURZ_INTRINSICS";

    private const string TurnedOff = " turned off by ";

    // whether the module holds the function that reads a variable of the run setting
    private bool environmentZero;

    /// <summary>The sets the start check reads: those of the build's CPU beyond x86-64's baseline, in the table's order, each name once.</summary>
    private List<CpuSet> CheckedSets() =>
        !cpu.X86 ? [] : CpuSets.Table.Where(s => cpu.Sets.Contains(s.Macro, StringComparer.Ordinal)).DistinctBy(s => s.Name, StringComparer.Ordinal).ToList();

    /// <summary>Whether <c>main</c> runs the start check, so that it is compiled for the baseline.</summary>
    private bool StartChecks => CheckedSets().Count > 0;

    /// <summary>The attributes of <c>main</c> and of the functions it calls before the runtime starts: the baseline's, where it checks the CPU.</summary>
    private string[] StartAttributes => StartChecks ? BaselineAttributes : [];

    /// <summary>The variable of the run setting that turns the class off: <c>KURZ_INTRINSICS_SSE41</c> for <c>X86.Sse41</c>.</summary>
    private static string SettingOf(IntrinsicClass c) => $"{Setting}_{c.Name[(c.Name.IndexOf('.', StringComparison.Ordinal) + 1)..].ToUpperInvariant()}";

    /// <summary>
    /// The variables of the run setting that turn a set of the start check off, the nearest first: its class's, those of the classes it
    /// derives from beyond the baseline, then the one of every set.
    /// </summary>
    private static List<string> SettingsOf(CpuSet set)
    {
        var cls = Intrinsics.Classes.Values.FirstOrDefault(c => c.IsX86 && c.Macro == set.Macro);
        var classes = cls is null ? [] : Intrinsics.Included(cls).Where(c => !Cpu.Baseline.Classes.Contains(c.Name)).Select(SettingOf);
        return [.. classes, Setting];
    }

    /// <summary>
    /// The module's read of the CPU, where a test reads the CPU's word or the build's CPU has sets beyond the baseline: the function
    /// <c>main</c> calls before the runtime starts, null where there is nothing to read.
    /// </summary>
    private string? CpuRead()
    {
        var check = CheckedSets();
        if (!cpuRead && check.Count == 0)
        {
            return null;
        }

        var read = new IrFunction("internal void @kz.cpu.read()", StartAttributes);

        // every leaf the read needs, in the entry block, each register taken as 0 where the CPU has no such leaf, as LLVM takes it
        var leaves = new Dictionary<(uint Leaf, uint Subleaf), string>();
        string Leaf(uint leaf, uint subleaf)
        {
            if (!leaves.TryGetValue((leaf, subleaf), out var value))
            {
                value = read.Value($"{Cpuid}(i32 {I32(leaf)}, i32 {I32(subleaf)})");
                leaves[(leaf, subleaf)] = value;
            }

            return value;
        }

        string Field(uint leaf, uint subleaf, int register) => read.Value($"extractvalue {{ i32, i32, i32, i32 }} {Leaf(leaf, subleaf)}, {register}");
        var top = Field(0, 0, 0);
        string? extended = null;
        string Has(uint leaf, uint subleaf)
        {
            if (leaf >= 0x80000000)
            {
                extended ??= Field(0x80000000, 0, 0);
                return read.Value($"icmp uge i32 {extended}, {I32(leaf)}");
            }

            var has = read.Value($"icmp uge i32 {top}, {I32(leaf)}");
            return leaf == 7 && subleaf > 0 ? read.Value($"and i1 {has}, {read.Value($"icmp uge i32 {Register(7, 0, 0)}, {I32(subleaf)}")}") : has;
        }

        string Register(uint leaf, uint subleaf, int register) => read.Value($"select i1 {Has(leaf, subleaf)}, i32 {Field(leaf, subleaf, register)}, i32 0");
        string Bit(CpuBit b) => read.Value($"icmp ne i32 {read.Value($"and i32 {Register(b.Leaf, b.Subleaf, b.Register)}, {I32(1u << b.Bit)}")}, 0");

        // the state the system saves, from XCR0, which `xgetbv` reads only where the CPU says the system turned it on (OSXSAVE)
        string? xcr0 = null;
        if (check.Any(s => s.State != CpuState.None))
        {
            var slot = read.Alloca("i32", "xcr0");
            read.Emit($"store i32 0, ptr {slot}");
            var get = read.Label("xgetbv");
            var got = read.Label("xcr0");
            read.CondBranch(Bit(new CpuBit(1, 0, 2, 27)), get, got);
            read.Block(get);
            read.Emit($"store i32 {read.Value($"extractvalue {{ i32, i32 }} {read.Value("call { i32, i32 } asm \"xgetbv\", \"={ax},={dx},{cx}\"(i32 0)")}, 0")}, ptr {slot}");
            read.Branch(got);
            read.Block(got);
            xcr0 = read.Value($"load i32, ptr {slot}");
        }

        string Saved(int mask) => read.Value($"icmp eq i32 {read.Value($"and i32 {xcr0}, {I32((uint)mask)}")}, {I32((uint)mask)}");
        var states = new Dictionary<CpuState, string>();
        string State(CpuState state)
        {
            if (!states.TryGetValue(state, out var value))
            {
                value = state switch
                {
                    CpuState.Avx => read.Value($"and i1 {Saved(0x6)}, {Bit(new CpuBit(1, 0, 2, 28))}"), // LLVM: the AVX bit with the state
                    CpuState.Avx512 => read.Value($"and i1 {State(CpuState.Avx)}, {Saved(0xe0)}"),
                    CpuState.Amx => Saved(0x60000),
                    _ => Saved(0x80000), // APX's extended registers
                };
                states[state] = value;
            }

            return value;
        }

        // the run setting, each variable read once
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        string Off(string variable)
        {
            if (!settings.TryGetValue(variable, out var value))
            {
                value = read.Value($"call i1 {EnvironmentZero()}(ptr {module.String(variable + "\0").Pointer})");
                settings[variable] = value;
            }

            return value;
        }

        if (cpuRead)
        {
            // the word of the tests: ECX of leaf 1 in its low half, EBX of leaf 7 in its high half, as std_detect reads them, without the
            // sets the setting turns off; a class has a bit in it only for a set of the XMM registers, SSE3 to SSE4.2, AES, PCLMULQDQ and
            // SHA, which every x86-64 system saves, so the word needs no XCR0, which the sets of AVX and after need (the check below)
            module.Global("@kz.cpu = internal global i64 0");
            var word = read.Value($"or i64 {read.Value($"zext i32 {Register(1, 0, 2)} to i64")}, {read.Value($"shl i64 {read.Value($"zext i32 {Register(7, 0, 1)} to i64")}, 32")}");
            word = read.Value($"select i1 {Off(Setting)}, i64 0, i64 {word}");
            foreach (var c in Intrinsics.Classes.Values.Where(c => c.IsX86 && c.Bit != 0))
            {
                word = read.Value($"select i1 {Off(SettingOf(c))}, i64 {read.Value($"and i64 {word}, {Number(~(long)c.Bit)}")}, i64 {word}");
            }

            read.Emit($"store i64 {word}, ptr @kz.cpu");
        }

        if (check.Count > 0)
        {
            var sets = check.Select(s =>
            {
                var has = s.Bits.Select(Bit).Aggregate((a, b) => read.Value($"and i1 {a}, {b}"));
                if (s.Else is { } other)
                {
                    has = read.Value($"or i1 {has}, {Bit(other)}");
                }

                if (s.State != CpuState.None)
                {
                    has = read.Value($"and i1 {has}, {State(s.State)}");
                }

                if (s.Version > 0)
                {
                    has = read.Value($"and i1 {has}, {read.Value($"icmp uge i32 {read.Value($"and i32 {Register(0x24, 0, 1)}, 255")}, {I32((uint)s.Version)}")}");
                }

                var offs = SettingsOf(s).Select(v => (Variable: v, Off: Off(v))).ToList();
                var lacks = read.Value($"xor i1 {has}, true");
                var missing = offs.Aggregate(lacks, (m, o) => read.Value($"or i1 {m}, {o.Off}"));
                return (Set: s, Lacks: lacks, Offs: offs, Missing: missing);
            }).ToList();

            // the line (section 13, the owner, 2026-11-02, #39): the sets the CPU lacks, then one group for each variable of the setting
            // that turned off sets the CPU has, each such set in the group of the nearest variable that is on, the groups apart by `; `
            var groups = sets.SelectMany(s => s.Offs.Select(o => o.Variable)).Distinct(StringComparer.Ordinal).OrderBy(v => v == Setting).ToList();
            var by = sets.Select(s =>
            {
                var has = read.Value($"xor i1 {s.Lacks}, true");
                var earlier = "false";
                var nearest = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (variable, off) in s.Offs)
                {
                    nearest[variable] = read.Value($"and i1 {read.Value($"and i1 {off}, {has}")}, {read.Value($"xor i1 {earlier}, true")}");
                    earlier = read.Value($"or i1 {earlier}, {off}");
                }

                return nearest;
            }).ToList();

            var report = read.Label("cpu.lacks");
            var ok = read.Label("cpu.has");
            read.CondBranch(sets.Select(s => s.Missing).Aggregate((a, b) => read.Value($"or i1 {a}, {b}")), report, ok);
            read.Block(report);
            var prefix = $"error: cpu-lacks-set ({cpu.Name}: ";
            var size = prefix.Length + sets.Sum(s => 2 + s.Set.Name.Length) + groups.Sum(v => 2 + TurnedOff.Length + v.Length) + 2;
            var buffer = read.Alloca($"[{Number(size)} x i8]", "line");
            var at = read.Alloca("i64", "at");
            var first = read.Alloca("i1", "first");
            read.Emit($"store i64 0, ptr {at}");
            read.Emit($"store i1 true, ptr {first}");
            Append(read, buffer, at, prefix);

            // a name after the first of its list, the list of the sets the CPU lacks or a group, follows `, `
            void Name(string condition, string name) => When(read, condition, () =>
            {
                When(read, read.Value($"xor i1 {read.Value($"load i1, ptr {first}")}, true"), () => Append(read, buffer, at, ", "));
                read.Emit($"store i1 false, ptr {first}");
                Append(read, buffer, at, name);
            });

            foreach (var s in sets)
            {
                Name(s.Lacks, s.Set.Name);
            }

            // whether the line names a set yet, which a group follows with `; `
            var wrote = read.Alloca("i1", "wrote");
            read.Emit($"store i1 {read.Value($"xor i1 {read.Value($"load i1, ptr {first}")}, true")}, ptr {wrote}");
            foreach (var variable in groups)
            {
                var members = sets.Select((s, i) => (s.Set.Name, In: by[i].GetValueOrDefault(variable))).Where(m => m.In is not null).ToList();
                When(read, members.Select(m => m.In!).Aggregate((a, b) => read.Value($"or i1 {a}, {b}")), () =>
                {
                    When(read, read.Value($"load i1, ptr {wrote}"), () => Append(read, buffer, at, "; "));
                    read.Emit($"store i1 true, ptr {wrote}");
                    read.Emit($"store i1 true, ptr {first}");
                    foreach (var (name, condition) in members)
                    {
                        Name(condition!, name);
                    }

                    Append(read, buffer, at, TurnedOff + variable);
                });
            }

            Append(read, buffer, at, ")\n");
            WriteAndEnd(read, buffer, read.Value($"load i64, ptr {at}"));
            read.Block(ok);
        }

        read.Return(null);
        module.Add(read);
        return "@kz.cpu.read";
    }

    /// <summary>An <c>i32</c> constant of the bits of <paramref name="value"/>, as IR writes it: signed.</summary>
    private static string I32(uint value) => unchecked((int)value).ToString(CultureInfo.InvariantCulture);

    /// <summary>The code <paramref name="then"/> emits, run where <paramref name="condition"/> holds.</summary>
    private static void When(IrFunction f, string condition, Action then)
    {
        var yes = f.Label("when");
        var after = f.Label("when.after");
        f.CondBranch(condition, yes, after);
        f.Block(yes);
        then();
        f.Branch(after);
        f.Block(after);
    }

    /// <summary>The text's bytes copied into the buffer at the place <paramref name="at"/> holds, which moves past them.</summary>
    private void Append(IrFunction f, string buffer, string at, string text)
    {
        var (pointer, length) = module.String(text);
        var place = f.Value($"load i64, ptr {at}");
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        f.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {f.Value($"getelementptr i8, ptr {buffer}, i64 {place}")}, ptr {pointer}, i64 {Number(length)}, i1 false)");
        f.Emit($"store i64 {f.Value($"add i64 {place}, {Number(length)}")}, ptr {at}");
    }

    /// <summary>The line written to standard error and the process ended with 1, through the system's own calls, as the runtime's are declared.</summary>
    private void WriteAndEnd(IrFunction f, string buffer, string count)
    {
        if (target == Target.Windows)
        {
            module.Declare("declare ptr @GetStdHandle(i32)");
            module.Declare("declare i32 @WriteFile(ptr, ptr, i32, ptr, ptr)");
            module.Declare("declare void @ExitProcess(i32)");
            var written = f.Alloca("i32", "written");
            var handle = f.Value("call ptr @GetStdHandle(i32 -12)"); // STD_ERROR_HANDLE
            f.Emit($"call i32 @WriteFile(ptr {handle}, ptr {buffer}, i32 {f.Value($"trunc i64 {count} to i32")}, ptr {written}, ptr null)");
            f.Emit("call void @ExitProcess(i32 1)");
        }
        else
        {
            module.Declare("declare i64 @write(i32, ptr, i64)");
            module.Declare("declare void @_exit(i32)");
            f.Emit($"call i64 @write(i32 2, ptr {buffer}, i64 {count})");
            f.Emit("call void @_exit(i32 1)");
        }

        f.Unreachable();
    }

    /// <summary>
    /// The function that says whether the environment's variable, a NUL-ended name, is <c>0</c>, compiled for the baseline: on Linux
    /// through <c>getenv</c>, as the runtime reads <c>KURZ_TRACE</c>, on Windows through <c>GetEnvironmentVariableA</c>.
    /// </summary>
    private string EnvironmentZero()
    {
        const string Name = "@kz.cpu.setting";
        if (!environmentZero)
        {
            environmentZero = true;
            var f = new IrFunction($"internal i1 {Name}(ptr %name)", StartAttributes);
            var no = f.Label("unset");
            if (target == Target.Windows)
            {
                module.Declare("declare i32 @GetEnvironmentVariableA(ptr, ptr, i32)");
                var value = f.Alloca("[2 x i8]", "value");
                var one = f.Value($"icmp eq i32 {f.Value($"call i32 @GetEnvironmentVariableA(ptr %name, ptr {value}, i32 2)")}, 1"); // its length: one character
                var set = f.Label("set");
                f.CondBranch(one, set, no);
                f.Block(set);
                f.Return($"i1 {f.Value($"icmp eq i8 {f.Value($"load i8, ptr {value}")}, 48")}");
            }
            else
            {
                module.Declare("declare ptr @getenv(ptr)");
                var value = f.Value("call ptr @getenv(ptr %name)");
                var set = f.Label("set");
                var zero = f.Label("zero");
                f.CondBranch(f.Value($"icmp eq ptr {value}, null"), no, set);
                f.Block(set);
                f.CondBranch(f.Value($"icmp eq i8 {f.Value($"load i8, ptr {value}")}, 48"), zero, no);
                f.Block(zero);
                f.Return($"i1 {f.Value($"icmp eq i8 {f.Value($"load i8, ptr {f.Value($"getelementptr i8, ptr {value}, i64 1")}")}, 0")}");
            }

            f.Block(no);
            f.Return("i1 false");
            module.Add(f);
        }

        return Name;
    }
}
