using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions on lists that no corpus case pins (M9; design record, section 3; the owner, 2026-10-24, "Full counting
/// now" and "Items after the Add"): a list is a value whose storage is shared until one side writes (M3), the counting frees a
/// block nothing holds, which the allocator gives out again, no block is read after it is freed, the number `allocation-failed`
/// reports past the limit and for a refused growth, and what the emitter says `not supported` to.
/// </summary>
public sealed class ListTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    // nested lists, a list in a data value and in `with`, a path assignment, a list of strings, a loop's `continue`, and the texts
    private const string Values = """
        data Cart(string Owner, List<int> Items)

        List<int> Range(int n) {
            mut xs = List<int>()
            mut i = 0
            while i < n {
                xs.Add(i)
                i = i + 1
            }
            return xs
        }

        int Sum(List<int> xs) {
            mut total = 0
            for x in xs {
                total = total + x
            }
            return total
        }

        mut grid = List<List<int>>()
        grid.Add(Range(3))
        grid.Add(Range(0))
        grid.Add(Range(2))
        print(grid)
        mut row = grid[0]
        row.Add(9)
        print(row)
        print(grid)
        c = Cart("Ann", Range(4))
        print(c)
        d = c with { Owner = "Bea" }
        print(d.Items.Count)
        print(Sum(d.Items))
        mut e = c
        e.Items = Range(1)
        print(c)
        print(e)
        mut names = List<string>()
        names.Add("a")
        names.Add("b")
        print(names)
        for r in grid {
            if r.Count == 0 {
                continue
            }
            print(Sum(r))
        }
        """;

    private static readonly string[] ValuesPrint =
    [
        "[[0, 1, 2], [], [0, 1]]",
        "[0, 1, 2, 9]", // the row's own value: the grid keeps its list (M3)
        "[[0, 1, 2], [], [0, 1]]",
        "Cart { Owner = Ann, Items = [0, 1, 2, 3] }",
        "4",
        "6",
        "Cart { Owner = Ann, Items = [0, 1, 2, 3] }",
        "Cart { Owner = Ann, Items = [0] }",
        "[a, b]",
        "3",
        "1",
    ];

    // a `mut` parameter beside a parameter given the same variable, a loop over a list that the loop adds to, an `Add` whose argument
    // changes the list, and many short lists
    private const string Sharing = """
        void Fill(mut List<int> xs, List<int> seen) {
            xs.Add(7)
            print(seen.Count)
        }

        int Grow(mut List<int> xs) {
            xs.Add(5)
            return xs.Count
        }

        mut a = List<int>()
        a.Add(1)
        Fill(mut a, a)
        print(a.Count)
        mut b = a
        for x in b {
            b.Add(x)
        }
        print(a)
        print(b)
        mut ys = List<int>()
        ys.Add(Grow(mut ys))
        print(ys)
        mut i = 0
        while i < 1000 {
            mut t = List<int>()
            t.Add(i)
            t.Add(i)
            i = i + 1
        }
        print(i)
        """;

    private static readonly string[] SharingPrint =
    [
        "1", // the parameter keeps the value it was given (M3, M7)
        "2",
        "[1, 7]",
        "[1, 7, 1, 7]", // the loop walks the list as it was when it started (C6)
        "[5, 1]", // the argument first, then the list as the argument left it (M4's order, assumed for a method's receiver)
        "1000",
    ];

    // every way out of a block a list is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, a `with` and a copy written to
    private const string Rounds = """
        data Pair(List<int> A, int B)

        List<int> Make(int n) {
            mut xs = List<int>()
            mut i = 0
            while i < n {
                xs.Add(i)
                i = i + 1
            }
            return xs
        }

        int Early(int n) {
            xs = Make(n)
            if n > 0 {
                return xs.Count
            }
            return 0
        }

        mut round = 0
        mut total = 0
        while round < 3000 {
            round = round + 1
            a = Make(1000)
            mut b = a
            b.Add(1)
            p = Pair(Make(1000), 1)
            q = p with { B = 2 }
            for x in Make(1000) {
                if x == 3 {
                    break
                }
            }
            for j in 0..<3 {
                c = Make(1000)
                if j == 1 {
                    continue
                }
                total = total + c.Count
            }
            Make(1000)
            total = total + Early(1000) + q.A.Count + b.Count - a.Count
        }
        print(round)
        print(total)
        """;

    private static Compilation Compile(string text, CheckedRuntime runtime, params string[] grants) =>
        Builder.Compile(SourceText.FromString("program.kz", text), grants, runtime, testBuild: true);

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, CheckedRuntime runtime, string name, params string[] grants)
    {
        var compilation = Compile(text, runtime, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    /// <summary>The runtime and the standard library of this machine's target with each file's text edited where <paramref name="edits"/> names its path; each text it replaces is there.</summary>
    private static CheckedRuntime Edited(params (string Path, string Old, string New)[] edits)
    {
        var target = Builder.HostTarget!.Value;
        var files = Runtime.Files.Select(f =>
        {
            var text = Encoding.UTF8.GetString(f.Bytes);
            foreach (var (path, old, @new) in edits.Where(e => e.Path == f.Path))
            {
                Assert.Contains(old, text, StringComparison.Ordinal);
                text = text.Replace(old, @new, StringComparison.Ordinal);
            }

            return new RuntimeFile(f.Path, Encoding.UTF8.GetBytes(text));
        }).ToList();
        Assert.All(edits, e => Assert.Contains(files, f => f.Path == e.Path));
        var errors = new List<string>();
        var runtime = Runtime.Check(files, target, errors);
        Assert.Empty(errors);
        return runtime;
    }

    private static string TargetFile => $"runtime/{Runtime.Directory(Builder.HostTarget!.Value)}/{Runtime.Directory(Builder.HostTarget!.Value)}.kz";

    /// <summary>The runtime of this machine's target whose system gives 16 pieces of memory and refuses every request after them.</summary>
    private static CheckedRuntime Budget() => Edited((TargetFile, "pub byte* Memory(long bytes) {", """
        thread static mut long pieces = 0

        pub byte* Memory(long bytes) {
            mut long taken = 0
            raw {
                pieces = pieces + 1
                taken = pieces
            }

            if taken > 16 {
                return null
            }

            return Granted(bytes)
        }

        byte* Granted(long bytes) {
        """));

    /// <summary>
    /// The edits that make the allocator take every block from pages of its own and make them unreachable when the block is freed, so
    /// that a read or a write of a freed block ends the process with the system's signal; nothing is used again.
    /// </summary>
    private static (string Path, string Old, string New)[] Fence => Builder.HostTarget == Target.Windows
        ?
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            (TargetFile, "VirtualFree(Ptr<void>(p), 0, 32768)", "VirtualFree(Ptr<void>(p), ulong(bytes), 16384)"), // MEM_DECOMMIT: the pages stay reserved
        ]
        :
        [
            ("std/std.kz", "if size > 262144 {", "if size >= 0 {"),
            (TargetFile, "    int munmap(void* address, long length)\n", "    int munmap(void* address, long length)\n    int mprotect(void* address, long length, int protection)\n"),
            (TargetFile, "munmap(Ptr<void>(p), bytes)", "mprotect(Ptr<void>(p), bytes, 0)"), // PROT_NONE: the pages stay mapped
        ];

    [Fact]
    public void Lists_are_values_whose_storage_is_shared_until_one_side_writes()
    {
        Ran(BuildAndRun(Values, toolchain.Runtime, "list-values"), 0, ValuesPrint, "");
        Ran(BuildAndRun(Sharing, toolchain.Runtime, "list-sharing"), 0, SharingPrint, "");
    }

    [Fact]
    public void No_block_is_read_or_written_after_it_is_freed()
    {
        var fenced = Edited(Fence);
        Ran(BuildAndRun(Values, fenced, "list-values-fenced"), 0, ValuesPrint, "");
        Ran(BuildAndRun(Sharing, fenced, "list-sharing-fenced"), 0, SharingPrint, "");

        // the positive control: a write into a freed block under the same allocator ends the process
        var writer = Edited([.. Fence, ("runtime/common/runtime.kz", "pub void Start() {\n    sys.Start()\n", "pub void Start() {\n    sys.Start()\n    byte* p = Allocate(16)\n    raw {\n        std.Free(p, 16)\n        p[0] = 1\n    }\n")]);
        var run = BuildAndRun("print(1)\n", writer, "list-fence-control");
        Assert.NotEqual(0, run.Code);
        Assert.Empty(run.Output);
    }

    [Fact]
    public void What_no_value_holds_is_freed_and_given_out_again()
    {
        Ran(BuildAndRun(Rounds, Budget(), "list-rounds"), 0, ["3000", "12003000"], "");

        // the positive control: the same lists, all of them held, outgrow the same memory
        const string held = "mut all = List<List<int>>()\nmut round = 0\nwhile round < 3000 {\n    round = round + 1\n    mut xs = List<int>()\n    mut i = 0\n    while i < 1000 {\n        xs.Add(i)\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n";
        var run = BuildAndRun(held, Budget(), "list-rounds-held");
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Add_past_the_limit_reports_the_bytes_of_the_items_after_it()
    {
        // the owner, 2026-10-24, "Items after the Add": the limit of the largest `int` lowered to 3 in the library's text, so that a
        // fourth `Add` of a `long` asks for 4 times 8 bytes
        const string text = "mut xs = List<long>()\nxs.Add(1)\nxs.Add(2)\nxs.Add(3)\nxs.Add(4)\nprint(xs.Count)\n";
        Ran(BuildAndRun(text, Edited(("std/std.kz", "2147483647", "3")), "list-limit"), 1, [], "error: allocation-failed (32 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-below-limit"), 0, ["4"], ""); // the positive control
    }

    [Fact]
    public void A_growth_the_system_refuses_reports_the_bytes_the_allocator_was_asked_for()
    {
        // the first block of a list of `long`: 16 bytes of counts and room for 4 items, as Rust's `Vec` takes 4 of up to a kibibyte
        const string text = "mut xs = List<long>()\nxs.Add(1)\nprint(xs.Count)\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "list-refused"), 1, [], "error: allocation-failed (48 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-granted"), 0, ["1"], ""); // the positive control
    }

    [Theory]
    [InlineData("mut xs = List<int>()\nraw {\n    print(xs.Pointer != null)\n}\n", 3, "`l.Pointer` (R15)", "raw")]
    [InlineData("raw {\n    xs = List<int>.FromRaw(Ptr<int>(16), 0)\n    print(xs.Count)\n}\n", 2, "`List<T>.FromRaw` (R15)", "raw")]
    [InlineData("mut xs = List<int>()\nxs.Add(1)\nxs[0] = 2\n", 3, "an assignment into an index (M9)", null)]
    [InlineData("data Box(List<int> Items)\n\nmut b = Box(List<int>())\nb.Items.Add(1)\n", 4, "an `Add` on a list in a field (M5)", null)]
    [InlineData("data Box(List<int> Items)\n\na = Box(List<int>())\nb = Box(List<int>())\nprint(a == b)\n", 5, "`==` on a `List<int>`", null)] // D3 compares the fields
    [InlineData("s = \"ab\"\nprint(s.Bytes[0])\n", 2, "`s.Bytes` as a list (T15)", null)]
    public void What_the_emitter_does_not_lay_out_of_a_list_is_not_supported_at_its_line(string text, int line, string message, string? grant)
    {
        var compilation = Compile(text, toolchain.Runtime, grant is null ? [] : [grant]);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((line, message), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
    }
}
