using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions on lists that no corpus case pins (M9; design record, section 3; the owner, 2026-10-24, "Full counting
/// now" and "Items after the Add"): a list is a value whose storage is shared until one side writes (M3), the counting frees a
/// block nothing holds, which the allocator gives out again, and a lambda's environment as it frees a list's block (the owner,
/// 2026-10-25, "Pair + heap env"), no block is read after it is freed, the number `allocation-failed`
/// reports past the limit and for a refused growth, and what the emitter says `not supported` to; what the checker refuses of a
/// read beside a `mut` call (M7) the corpus pins.
/// </summary>
public sealed class ListTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    // nested lists, a list in a data value and in `with`, a path assignment, a list of strings, a loop's `continue`, a list in a
    // value laid out as a block, and the texts
    private const string Values = """
        data Cart(string Owner, List<int> Items)
        data Leaf
        data Tree(List<int> Items, Tree | Leaf Next)

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
        t = Tree(Range(2), Leaf)
        mut ts = t.Items
        ts.Add(9)
        print(t)
        print(ts)
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
        "Tree { Items = [0, 1], Next = Leaf }", // a list a block holds: the block keeps it (M3)
        "[0, 1, 9]",
    ];

    // a `mut` parameter beside a parameter given a copy of the same list, a loop over a list that the loop adds to, the name an arm
    // binds while the arm changes the subject's variable, many short lists, a function value and a data value that holds one read
    // beside a call that changes their variables, a lambda's list whose variable changed, and the items `Where` keeps
    private const string Sharing = """
        data Empty
        data Row(List<int> Cells)
        data Rule(() => int Step, int Limit)

        void Fill(mut List<int> xs, List<int> seen) {
            xs.Add(7)
            print(seen.Count)
        }

        int Keep(() => int f, int ignored) => f()

        int Swap(mut () => int f) {
            f = () => 0
            return 0
        }

        int SwapStep(mut (int) => int f) {
            f = delta => 0
            return 0
        }

        int KeepRule(Rule r, int ignored) => r.Step()

        int SwapRule(mut Rule r) {
            r = Rule(() => 0, 0)
            return 0
        }

        List<int> | Empty Pair() {
            mut xs = List<int>()
            xs.Add(1)
            xs.Add(2)
            return xs
        }

        mut a = List<int>()
        a.Add(1)
        before = a
        Fill(mut a, before)
        print(a.Count)
        mut b = a
        for x in b {
            b.Add(x)
        }
        print(a)
        print(b)
        mut List<int> | Empty u = Pair()
        match u {
            List<int> l => {
                u = Empty
                mut ys = List<int>()
                ys.Add(7)
                ys.Add(8)
                print(l)
            }
            Empty => print(0)
        }
        mut i = 0
        while i < 1000 {
            mut t = List<int>()
            t.Add(i)
            t.Add(i)
            i = i + 1
        }
        print(i)
        mut rows = List<List<int>>()
        mut row = List<int>()
        row.Add(3)
        rows.Add(row)
        row = List<int>()
        mut k = 0
        while k < 8 {
            rows.Add(rows[0])
            k = k + 1
        }
        print(rows[8])
        mut cells = List<Row>()
        mut kept = List<int>()
        kept.Add(4)
        cells.Add(Row(kept))
        kept = List<int>()
        mut c = 0
        while c < 8 {
            cells.Add(cells[0])
            c = c + 1
        }
        print(cells[8].Cells)
        mut held = List<int>()
        held.Add(5)
        held.Add(6)
        mut read = () => held.Count
        held = List<int>()
        print(Keep(read, Swap(mut read)))
        mut steps = List<int>()
        steps.Add(1)
        mut rule = Rule(() => steps.Count, 1)
        steps = List<int>()
        print(KeepRule(rule, SwapRule(mut rule)))
        mut nested = List<List<int>>()
        mut one = List<int>()
        one.Add(9)
        nested.Add(one)
        nested.Add(List<int>())
        one = List<int>()
        survivors = nested.Where(inner => inner.Count > 0)
        nested = List<List<int>>()
        print(survivors)
        mut counted = List<int>()
        counted.Add(3)
        counted.Add(4)
        counted.Add(5)
        mut step = (int delta) => delta + counted.Count
        counted = List<int>()
        print(step(SwapStep(mut step)))
        """;

    private static readonly string[] SharingPrint =
    [
        "1", // the parameter keeps the value it was given (M3, M7)
        "2",
        "[1, 7]",
        "[1, 7, 1, 7]", // the loop walks the list as it was when it started (C6)
        "[1, 2]", // the name keeps the value it was bound to (M3), and the freed block the next lists take is not its own
        "1000",
        "[3]", // an item read for its own list's `Add` across two growths, held by the list's slot alone (M7)
        "[4]", // the same for an item that holds a list
        "2", // the value a call is given beside a `mut` call that replaces it, a function value whose environment it alone held (M7, "Copied")
        "1", // the same for a data value that holds one ("Copied too"), and a lambda's list that only its environment holds (F15)
        "[[9]]", // an item `Where` keeps, which only the source list held before the source was let go of
        "3", // the function value a call calls, beside a `mut` call that replaces it, whose environment it alone held (M7, "Copied")
    ];

    // every way out of a block a list is held in, round after round: the end of a block, a `return`, a `break`, a `continue`, a
    // result nothing holds, the body of a `void` function, a `with`, a copy written to, and a lambda's environment in a variable and
    // in a list
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

        void Drop(int n) => Make(n)

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
            Drop(1000)
            l = Make(1000)
            h = () => l.Count
            mut hs = List<() => int>()
            hs.Add(h)
            hs.Add(() => Make(1000).Count)
            total = total + Early(1000) + q.A.Count + b.Count - a.Count + hs[0]() + hs[1]() - 2000
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

    /// <summary>The run ended with the system's signal before it printed: neither 0 nor the runtime's error exit 1, and no error line of the runtime.</summary>
    private static void Crashed((int Code, IReadOnlyList<string> Output, string Error) run)
    {
        Assert.True(run.Code is not 0 and not 1, $"exit {run.Code}");
        Assert.Empty(run.Output);
        Assert.DoesNotContain("error:", run.Error, StringComparison.Ordinal);
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
    /// The runtime of this machine's target whose system refuses a request that would take what it gave and was not given back past
    /// 64 mebibytes; its own `Memory` and `Release` run behind the count.
    /// </summary>
    private static CheckedRuntime Bytes() => Edited(
        (TargetFile, "pub byte* Memory(long bytes) {", """
            thread static mut long live = 0

            pub byte* Memory(long bytes) {
                mut long total = 0
                raw {
                    live = live + bytes
                    total = live
                }

                if total > 67108864 {
                    return null
                }

                return Granted(bytes)
            }

            byte* Granted(long bytes) {
            """),
        (TargetFile, "pub void Release(byte* p, long bytes) {", """
            pub void Release(byte* p, long bytes) {
                raw {
                    live = live - bytes
                }

                Released(p, bytes)
            }

            void Released(byte* p, long bytes) {
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
        // every way out of a block, as the budget's program takes them, in rounds few enough for a mapping per block
        Ran(BuildAndRun(Rounds.Replace("while round < 3000", "while round < 20", StringComparison.Ordinal), fenced, "list-rounds-fenced"), 0, ["20", "80020"], "");

        // the positive control: a write into a freed block under the same allocator ends the process
        var writer = Edited([.. Fence, ("runtime/common/runtime.kz", "pub void Start() {\n    sys.Start()\n", "pub void Start() {\n    sys.Start()\n    byte* p = Allocate(16)\n    raw {\n        std.Free(p, 16)\n        p[0] = 1\n    }\n")]);
        Crashed(BuildAndRun("print(1)\n", writer, "list-fence-control"));
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
    public void A_block_of_more_than_262144_bytes_goes_back_to_the_system()
    {
        // the target's own `Release`: a write into such a block after its `Free` ends the process with the system's signal
        const string start = "pub void Start() {\n    sys.Start()\n";
        const string write = "    byte* p = Allocate(300000)\n    raw {\n        p[0] = 1\n        std.Free(p, 300000)\n        p[0] = 2\n    }\n";
        Crashed(BuildAndRun("print(1)\n", Edited(("runtime/common/runtime.kz", start, start + write)), "list-large-freed"));
        var kept = Edited(("runtime/common/runtime.kz", start, start + write.Replace("        std.Free(p, 300000)\n", "", StringComparison.Ordinal)));
        Ran(BuildAndRun("print(1)\n", kept, "list-large-kept"), 0, ["1"], ""); // the positive control: the block not freed

        // the lists of 70000 items grow through blocks of 262160 and 524304 bytes, 150 mebibytes in 200 rounds were none of them given back
        const string rounds = "long Fill(int n) {\n    mut xs = List<int>()\n    mut i = 0\n    while i < n {\n        xs.Add(i)\n        i = i + 1\n    }\n    return long(xs.Count)\n}\n\nmut round = 0\nmut long total = 0\nwhile round < 200 {\n    round = round + 1\n    total = total + Fill(70000)\n}\nprint(total)\n";
        Ran(BuildAndRun(rounds, Bytes(), "list-large-rounds"), 0, ["14000000"], "");
        const string held = "mut all = List<List<int>>()\nmut round = 0\nwhile round < 200 {\n    round = round + 1\n    mut xs = List<int>()\n    mut i = 0\n    while i < 70000 {\n        xs.Add(i)\n        i = i + 1\n    }\n    all.Add(xs)\n}\nprint(all.Count)\n";
        var run = BuildAndRun(held, Bytes(), "list-large-held"); // the positive control: the same lists, all of them held
        Assert.Equal(1, run.Code);
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("long", "1", 32)]
    [InlineData("byte", "byte(1)", 4)]
    [InlineData("string", "\"a\"", 64)] // its bytes' address and their count
    [InlineData("List<int>", "List<int>()", 32)] // the address of its block
    [InlineData("Padded", "Padded(1, byte(2))", 64)] // 9 bytes of fields, padded to the `long`'s alignment
    [InlineData("Tree", "Tree(1, Leaf)", 32)] // a block: the list holds its address
    public void An_Add_past_the_limit_reports_the_bytes_of_the_items_after_it(string type, string item, int bytes)
    {
        // the owner, 2026-10-24, "Items after the Add": the limit of the largest `int` lowered to 3 in the library's text, so that a
        // fourth `Add` asks for 4 times the bytes one item takes in the list
        var add = $"xs.Add({item})\n";
        var text = $"data Padded(long A, byte B)\ndata Leaf\ndata Tree(int V, Tree | Leaf Next)\n\nmut xs = List<{type}>()\n{add}{add}{add}{add}print(xs.Count)\n";
        var name = new string(type.Where(char.IsLetter).ToArray());
        Ran(BuildAndRun(text, Edited(("std/std.kz", "2147483647", "3")), "list-limit-" + name), 1, [], $"error: allocation-failed ({bytes} bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "list-below-limit-" + name), 0, ["4"], ""); // the positive control
    }

    [Theory]
    [InlineData("-1", "minus")]
    [InlineData("2", "count")]
    [InlineData("100000", "far")]
    [InlineData("-2147483648", "smallest")]
    public void An_index_below_0_or_at_Count_or_beyond_raises_index_out_of_range_at_its_line(string index, string name)
    {
        // T14, M9: the read before it is the positive control, an index inside the list
        var text = $"mut xs = List<int>()\nxs.Add(4)\nxs.Add(5)\nprint(xs[1])\nprint(xs[{index}])\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-index-" + name), 1, ["5"], "error: index-out-of-range at program.kz:5\n");
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
    [InlineData("data Box(List<int> Items)\n\nvoid Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nmut b = Box(List<int>())\nFill(mut b.Items)\n", 8, "a `mut` parameter given anything but a variable (M7)", null)]
    public void What_the_emitter_does_not_lay_out_of_a_list_is_not_supported_at_its_line(string text, int line, string message, string? grant)
    {
        var compilation = Compile(text, toolchain.Runtime, grant is null ? [] : [grant]);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((line, message), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
    }

    [Fact]
    public void Where_calls_its_test_once_for_each_item_in_order()
    {
        // M9: `Where` calls its test once for each item, in order (section 4, the owner, 2026-10-28, "Once each, in order"), as Rust's
        // `filter` and `collect` run it
        const string text = "mut xs = List<int>()\nxs.Add(3)\nxs.Add(1)\nxs.Add(2)\nkept = xs.Where(x => {\n    print(x)\n    return x > 1\n})\nprint(kept)\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "list-where-order"), 0, ["3", "1", "2", "[3, 2]"], "");
    }

    [Fact]
    public void An_environment_the_system_refuses_reports_its_bytes()
    {
        // the count, the address of the function that lets go of it, and the `int` the lambda read, padded to the count's alignment
        const string text = "n = 1\nf = () => n\nprint(f())\n";
        Ran(BuildAndRun(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value), "lambda-refused"), 1, [], "error: allocation-failed (24 bytes)\n");
        Ran(BuildAndRun(text, toolchain.Runtime, "lambda-granted"), 0, ["1"], ""); // the positive control
    }

    [Fact]
    public void What_Add_s_argument_made_besides_the_item_is_let_go_of_before_the_list_grows()
    {
        // the lambda given to `Count` holds a count of the list (F15) until its environment is let go of: before the `Add`, so that
        // the list is not seen as shared and copied at each `Add`
        var ir = Compile("int Count(() => int f) => f()\n\nmut xs = List<int>()\nxs.Add(Count(() => xs.Count))\nprint(xs)\n", toolchain.Runtime).Program!.Ir;
        var main = ir[ir.IndexOf("define internal void @kz.main()", StringComparison.Ordinal)..];
        main = main[..main.IndexOf("\n}", StringComparison.Ordinal)];
        var release = main.IndexOf("call void @\"release.fn\"", StringComparison.Ordinal);
        Assert.True(release >= 0, "the environment is let go of in the top-level code");
        Assert.True(release < main.IndexOf("call void @\"add.List<int>\"", StringComparison.Ordinal), "the environment is let go of after the `Add`");
    }

    [Fact]
    public void A_mut_parameter_of_a_lambda_is_not_supported_at_its_line()
    {
        // a function type passes no `mut` (F11), so no caller's variable sees what such a parameter is given (M7)
        var compilation = Compile("void Run((int) => int f) {\n    print(f(1))\n}\n\nRun((mut int x) => x)\n", toolchain.Runtime);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((5, "a `mut` parameter of a lambda (M7)"), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
        Assert.NotNull(Compile("void Run((int) => int f) {\n    print(f(1))\n}\n\nRun((int x) => x)\n", toolchain.Runtime).Program); // the positive control
    }
}
