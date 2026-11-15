using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The actors the emitter builds on the one thread the runtime runs (chapter 19, <c>compiler/README.md</c>), in what no corpus case
/// pins: a call that waits behind a million messages on the stack of one, since the runtime's <c>Wait</c> runs them in a loop; the
/// block of every message and the counts of its arguments let go of once it ran, under a system that gives 16 pieces of memory, and
/// never before, under the allocator that makes every freed block inaccessible, and an inbox no call that waits runs, which grows
/// until that system's memory is gone; the state an actor's record holds, which a lambda's
/// count of <c>this</c> does not let go of; a crash in a method, which ends the process with its error line and a trace that leaves the
/// message's function and the runtime out (Q1 (a), the answers of 2026-11-14, issue 519, comment 6880404917); the order of one
/// sender's messages and of a message sent inside a message (U5, U15), in a test build and a release build; the run queue, one message
/// of each actor in turn in the order the actors were queued, as tokio's <c>current_thread</c> runs its tasks, an actor that sends itself
/// a message while it runs standing in that queue once (U11); what is still queued
/// when the top-level code ends, which does not run (U13); and the number in an actor's text, one count over the actors of every type,
/// the text of <c>this</c> and <c>!=</c> of two references (U2, U10); and a state aligned to the 16 a vector field needs (R18). Then the
/// stacks of section 6, and the waits that park a message on its stack: a call of the chain that reaches the actor waiting in it (U17),
/// the pool and its trim, the registers a parked message keeps, the top-level code's <c>deadlock</c>, a parked message at the end (U13),
/// the chains the top-level code starts, and an overflow in a call of the chain.
/// </summary>
public sealed class ActorTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, CheckedRuntime runtime, string name, bool testBuild = true, string? trace = "0")
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name), trace);
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, IReadOnlyList<string> lines, string error)
    {
        // the exit code and the error with the lines, so that a run that ended early says how it ended
        Assert.True(lines.SequenceEqual(run.Output), $"printed [{string.Join(", ", run.Output)}] where [{string.Join(", ", lines)}] was expected; exit code {run.Code}, error: {run.Error}");
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    private const string Counter = """
        actor Counter {
            mut long count = 0

            pub void Add(long n) {
                count = count + n
            }

            pub long Get() => count
        }

        c = spawn Counter()
        for i in 0..<1000000 {
            send c.Add(1)
        }

        print(c.Get())

        """;

    [Fact]
    public void A_call_behind_a_million_messages_waits_on_the_stack_of_one() =>
        Ran(BuildAndRun(Counter, toolchain.Runtime, "actor-million"), 0, ["1000000"], "");

    /// <summary>A message with a string and a list made for it alone, which only the message holds once its statement ended.</summary>
    private static string Keeper(int rounds) => $$"""
        actor Keeper {
            mut long total = 0

            pub void Take(string s, List<int> xs) {
                total = total + s.Bytes.Count + xs[7] - xs[0]
            }

            pub long Total() => total
        }

        k = spawn Keeper()
        mut round = 0
        while round < {{rounds}} {
            for i in 0..<100 {
                send k.Take("item number {i} of round {round}", [i, i + 1, i + 2, i + 3, i + 4, i + 5, i + 6, i + 7])
            }

            if k.Total() < 0 {
                print("never")
            }

            round = round + 1
        }

        print(k.Total())

        """;

    /// <summary>What <see cref="Keeper"/> prints: the bytes of each message's text and 7 for its list.</summary>
    private static string KeeperTotal(int rounds) =>
        Enumerable.Range(0, rounds).Sum(r => Enumerable.Range(0, 100).Sum(i => (long)Encoding.UTF8.GetByteCount($"item number {i} of round {r}") + 7)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_message_and_its_arguments_are_let_go_of_once_it_ran()
    {
        // 500000 messages, each a block of 48 bytes with a string of up to 28 bytes and a list of 8 items, each of the three a block of 48
        // bytes or more with its count: 24 MB of any of them kept is more than the 16 MiB the system gives
        Ran(BuildAndRun(Keeper(5000), ListTests.Budget(), "actor-messages-freed"), 0, [KeeperTotal(5000)], "");
    }

    [Fact]
    public void An_inbox_nobody_runs_grows_until_the_allocator_ends_the_process()
    {
        // the positive control of the test of the freed messages: with no call that waits, every message stays in the inbox, and the
        // inbox has no bound of its own (Q1 (a), the answers of 2026-11-14, issue 519, comment 6880404917)
        const string sink = """
            actor Sink {
                mut long total = 0

                pub void Take(long n) {
                    total = total + n
                }
            }

            s = spawn Sink()
            print("start")
            while true {
                send s.Take(1)
            }

            """;
        var run = BuildAndRun(sink, ListTests.Budget(), "actor-inbox-grows");
        Assert.Equal((1, "start"), (run.Code, string.Join('\n', run.Output)));
        Assert.StartsWith("error: allocation-failed (", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_holds_its_arguments_until_it_ran()
    {
        // the string and the list of each message are temporaries of the statement of its `send`, freed there unless the message holds
        // a count of its own, and a read of a freed block ends this run with the system's signal
        Ran(BuildAndRun(Keeper(20), ListTests.Fenced, "actor-messages-fenced"), 0, [KeeperTotal(20)], "");
    }

    [Fact]
    public void A_lambda_in_a_method_gives_back_the_count_it_took_of_the_state()
    {
        const string box = """
            actor Box {
                mut int n = 5

                pub int Plus(int k) {
                    f = (int x) => x + n
                    return f(k)
                }

                pub void Grow() {
                    n = n + 1
                }
            }

            b = spawn Box()
            print(b.Plus(1))
            send b.Grow()
            print(b.Plus(2))

            """;
        Ran(BuildAndRun(box, ListTests.Fenced, "actor-lambda-state"), 0, ["6", "8"], "");
    }

    [Fact]
    public void A_crash_in_a_method_ends_the_process_and_its_trace_leaves_the_message_out()
    {
        const string calc = """
            actor Calc {
                pub int Div(int n) => 10 / n
            }

            c = spawn Calc()
            print(c.Div(2))
            print(c.Div(0))
            print("after")

            """;
        // the trace ends at the message's method, the first frame of the actor's stack, which the switch enters as if a call from the
        // address 0 had made it (design record, section 2), so `main`, which waits on the thread's own stack, is no frame of it
        var error = "error: divide-by-zero at program.kz:2\n" + (OperatingSystem.IsLinux() ? "  at Calc.Div in program.kz:2\n" : string.Empty);
        Ran(BuildAndRun(calc, toolchain.Runtime, "actor-crash", trace: null), 1, ["5"], error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void One_senders_messages_and_a_message_sent_inside_a_message_keep_their_order(bool testBuild)
    {
        // U5: each sender's messages in the order sent; U15: what the relay sends while it runs `Pass(i)` stands before what the
        // top-level code sends once that call returned
        const string relay = """
            actor Journal {
                mut List<int> items = List<int>()

                pub void Add(int n) {
                    items.Add(n)
                }

                pub List<int> Items() => items
            }

            actor Relay(Journal Target) {
                pub void Pass(int n) {
                    send Target.Add(n)
                }
            }

            l = spawn Journal()
            r = spawn Relay(l)
            for i in 0..<50 {
                r.Pass(i)
                send l.Add(100 + i)
            }

            print(l.Items())

            """;
        var expected = "[" + string.Join(", ", Enumerable.Range(0, 50).SelectMany(i => new[] { i, 100 + i })) + "]";
        Ran(BuildAndRun(relay, toolchain.Runtime, testBuild ? "actor-order" : "actor-order-release", testBuild), 0, [expected], "");
    }

    [Fact]
    public void A_call_that_waits_runs_one_message_of_each_queued_actor_in_turn()
    {
        const string turns = """
            actor Printer(string Name) {
                pub void Show(int n) {
                    print("{Name} {n}")
                }
            }

            a = spawn Printer("a")
            b = spawn Printer("b")
            send a.Show(1)
            send a.Show(2)
            send b.Show(1)
            b.Show(2)
            a.Show(3)

            """;
        Ran(BuildAndRun(turns, toolchain.Runtime, "actor-turns"), 0, ["a 1", "b 1", "a 2", "b 2", "a 3"], "");
    }

    [Fact]
    public void An_actor_that_sends_itself_a_message_while_it_runs_stands_in_the_run_queue_once()
    {
        // U11: the `send` of its own method queues the worker while its message runs, and the worker still holds that message once the
        // message ends; queued a second time there, it would cut the other actor the same message queued out of the run queue
        const string own = """
            actor Other {
                pub void Show(int n) {
                    print("other {n}")
                }
            }

            actor Worker(Other O) {
                pub void Start() {
                    send Second()
                    send O.Show(1)
                    print("first")
                }

                pub void Second() {
                    print("second")
                }
            }

            o = spawn Other()
            w = spawn Worker(o)
            w.Start()
            o.Show(2)

            """;
        Ran(BuildAndRun(own, toolchain.Runtime, "actor-own-send"), 0, ["first", "second", "other 1", "other 2"], "");
    }

    [Fact]
    public void What_is_still_queued_when_the_top_level_code_ends_does_not_run()
    {
        const string end = """
            actor Printer {
                pub void Show(int n) {
                    print(n)
                }
            }

            p = spawn Printer()
            send p.Show(1)
            p.Show(2)
            send p.Show(3)

            """;
        Ran(BuildAndRun(end, toolchain.Runtime, "actor-end"), 0, ["1", "2"], "");
    }

    [Fact]
    public void An_actors_number_counts_the_actors_of_every_type_in_the_order_they_were_spawned()
    {
        // U2: one count over the actors of every type, from 1, so that the second actor, the first of its type, takes the number 2;
        // `this` as a value is the actor's own reference, whose text is its own (U10); `!=` is true of two actors alone
        const string numbered = """
            actor Counter {
                pub string Name() => "{this}"
            }

            actor Logger {
                mut int lines = 0

                pub void Add() {
                    lines = lines + 1
                }
            }

            a = spawn Counter()
            l = spawn Logger()
            b = spawn Counter()
            c = b
            print(a)
            print("{l} {b.Name()}")
            print(a != b)
            print(b != c)

            """;
        Ran(BuildAndRun(numbered, toolchain.Runtime, "actor-numbered"), 0, ["Counter#1", "Logger#2 Counter#3", "true", "false"], "");
    }

    [Fact]
    public void An_actors_state_is_aligned_to_16_as_a_vector_field_needs()
    {
        // The record in front of the state is a multiple of 16 bytes, as Rust's `ArcInner` pads its counts to what it holds, so that the
        // state keeps the alignment of the allocator's block: a `Vector128<int>` field is loaded and stored at its alignment of 16 (R18,
        // `Lower.Align`), which x86-64 faults on where the address is not a multiple of it
        const string vector = """
            actor Lanes {
                mut Vector128<int> lanes = Vector128.Create(1, 2, 3, 4)

                pub int Third() => lanes.GetElement(2)

                pub void Set(int n) {
                    lanes = Vector128.Create(n, n * 2, n * 3, n * 4)
                }
            }

            a = spawn Lanes()
            print(a.Third())
            send a.Set(2)
            print(a.Third())

            """;
        Ran(BuildAndRun(vector, toolchain.Runtime, "actor-aligned"), 0, ["3", "6"], "");
        Ran(BuildAndRun(vector, toolchain.Runtime, "actor-aligned-release", testBuild: false), 0, ["3", "6"], "");
    }

    // ---- the stacks of actors (design record, section 6, "Own stack"; the owner, 2026-11-15, #519) -------------------------------

    // an actor whose `Down` recurses n deep; a frame of `Down` in a test build (-O0) is 48 bytes on Linux and 96 on Windows, whose calls
    // keep 32 bytes of home space, and one of the top-level `Top` below 32 and 80 (measured on 2026-11-15 from clang's assembly of each)
    private static string Deep(string name, string clause = "") => $$"""
        actor {{name}}{{clause}} {
            pub long Down(long n) {
                if n == 0 {
                    return 0
                }

                return Down(n - 1) + 1
            }
        }

        """;

    [Fact]
    public void A_message_runs_on_a_stack_of_2_mib_with_a_guard_page_and_the_top_level_code_on_the_threads_own()
    {
        // section 6, "Own stack", "Keep guard page", "2 MiB + clause": on Linux a message's 20000 calls, about a megabyte, run on the
        // actor's stack, and its 100000, about five, pass the 2 MiB and fault in the guard page below them, the error line at the call's
        // line and the trace ending at the method, while 100000 calls of the top-level code, about three, run on the thread's own stack of
        // 8 MiB (the root actor's code stays there, section 2); on Windows, whose frames are twice as large (Deep) and whose main
        // thread has 1 MiB, a message's 15000 calls, about 1.4 MB, run only on the actor's 2 MiB, and the top-level code's 10000 fit
        // the main thread's
        var (top, down) = OperatingSystem.IsWindows() ? (10000, 15000) : (100000, 20000);
        var text = "long Top(long n) {\n    if n == 0 {\n        return 0\n    }\n\n    return Top(n - 1) + 1\n}\n\n"
            + Deep("Deep")
            + $"print(Top({top}))\nd = spawn Deep()\nprint(d.Down({down}))\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-stack-runs"), 0, [$"{top}", $"{down}"], "");
        if (!OperatingSystem.IsLinux())
        {
            return; // an overflow on Windows ends the process as the system ends it (compiler/README.md, "What the emitter does not emit")
        }

        var run = BuildAndRun(text + "print(d.Down(100000))\n", toolchain.Runtime, "actor-stack-overflow", trace: null);
        Assert.Equal(["100000", "20000"], run.Output);
        Assert.Equal(1, run.Code);
        var lines = run.Error.Split('\n');
        Assert.Equal(["error: stack-overflow at program.kz:15", "  at Deep.Down in program.kz:15"], lines[..2]);
        Assert.DoesNotContain(lines, l => l.StartsWith("  at main ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fault_in_a_message_outside_its_guard_page_ends_the_process_as_the_system_ends_it()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the handler of a fault on Linux: Windows installs none
        }

        // the guard of the actor's stack is its one page below it: a read of the address 8 inside a message is no overflow, and the fault
        // ends the process with `SIGSEGV` (11), which .NET reports as 128 plus the signal, as one of the top-level code does
        const string text = "actor Reader {\n    pub int Read() {\n        raw {\n            int* p = Ptr<int>(8)\n            return *p\n        }\n    }\n}\n\nr = spawn Reader()\nprint(r.Read())\n";
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), ["raw"], toolchain.Runtime, true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var run = Toolchain.Run(toolchain.Build(compilation.Program, "actor-fault-elsewhere"), null);
        Assert.Equal((139, ""), (run.Code, run.Error));
    }

    [Fact]
    public void A_message_takes_a_stack_of_the_size_its_actor_asks_for_from_the_pool()
    {
        // U1, the clause `stack`, and section 6's pool: a thread keeps the stacks its messages gave back, each with its size, and a message
        // takes one of the size its actor asks for, so the 8 MiB actor's calls, about 4.8 MB, after a message of the actor of 2 MiB, run on a
        // stack of 8 MiB, and the next message of 2 MiB, about a megabyte, on one of that size again; the counts are Linux's halved on
        // Windows, whose frames are twice as large (Deep)
        var (big, plain) = OperatingSystem.IsWindows() ? (50000, 10000) : (100000, 20000);
        var text = Deep("Big", " stack 8mb") + Deep("Plain") + $"b = spawn Big()\np = spawn Plain()\nprint(p.Down({plain}))\nprint(b.Down({big}))\nprint(p.Down({plain}))\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-stack-sizes"), 0, [$"{plain}", $"{big}", $"{plain}"], "");
    }

    [Fact]
    public void A_stack_the_system_refuses_ends_the_process_with_allocation_failed_and_its_size_rounded_up_to_the_page()
    {
        // U1 (the owner, 2026-11-15, #519, comment 6892835500, Q7, "Rust/C#: round up silently") and section 6's `allocation-failed`:
        // 1000000000000.5 KiB, 1024000000000512 bytes, rounded up to the page of 4 KiB, is more than any system maps
        const string text = "actor Huge stack 1000000000000.5kb {\n    pub int Get() => 1\n}\n\nprint(\"before\")\nh = spawn Huge()\nprint(h.Get())\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-stack-refused"), 1, ["before"], "error: allocation-failed (1024000000004096 bytes)\n");
    }

    [Fact]
    public void The_registers_a_call_keeps_are_the_callers_again_after_the_messages_it_waited_on()
    {
        // the switch keeps the registers the convention keeps across a call in the save area of the side it leaves (the owner, 2026-11-15,
        // #519, comment 6892812780, Q1, "Save area at stack base"): in a release build the runtime's wait holds the actor it ran in them
        // across the switch, and puts it back in the run queue for the call queued behind each `send`, while the top-level code holds its
        // longs and doubles in them across the calls that wait, each its own after the messages ran on the actor's stack
        const string text = """
            actor Mix {
                mut long n = 0

                pub void Add(long k) {
                    n = n + k
                }

                pub long Get(long k) => k * 1000 + n
            }

            m = spawn Mix()
            send m.Add(1)
            long a = m.Get(1)
            send m.Add(1)
            long b = m.Get(2)
            send m.Add(1)
            long c = m.Get(3)
            send m.Add(1)
            long d = m.Get(4)
            send m.Add(1)
            long e = m.Get(5)
            send m.Add(1)
            long f = m.Get(6)
            send m.Add(1)
            long g = m.Get(7)
            send m.Add(1)
            double x = double(m.Get(8)) / 2.0
            send m.Add(1)
            double y = double(m.Get(9)) / 4.0
            send m.Add(1)
            double z = double(m.Get(10)) / 8.0
            send m.Add(1)
            double w = double(m.Get(11)) / 16.0
            send m.Add(1)
            double v = double(m.Get(12)) / 32.0
            send m.Add(1)
            double u = double(m.Get(13)) / 64.0
            send m.Add(1)
            long t = m.Get(14)
            print("{a} {b} {c} {d} {e} {f} {g} {t}")
            print("{x} {y} {z} {w} {v} {u}")

            """;
        string[] expected = ["1001 2002 3003 4004 5005 6006 7007 14014", "4004 2252.25 1251.25 688.1875 375.375 203.328125"];
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-registers"), 0, expected, "");
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-registers-release", testBuild: false), 0, expected, "");
    }

    [Fact]
    public void On_windows_a_message_runs_with_the_guarantee_of_0x5000_bytes_on_its_stack_and_the_switch_keeps_the_stacks_words()
    {
        // round 17, "Fiber layout + guarantee": an actor's stack takes the guarantee the main thread's takes, set at its first run, and
        // the switch keeps the four words of the thread environment block that belong to the stack; on another system the IR of Windows
        // for x86-64 shows both, and on Windows the guarantee in force inside a message is 0x5000 bytes
        const string text = "extern \"C\" in \"kernel32\" {\n    int SetThreadStackGuarantee(uint* size)\n}\n\nactor Probe {\n    pub long Guarantee() {\n        mut uint size = 0\n        raw {\n            SetThreadStackGuarantee(&size)\n        }\n\n        return long(size)\n    }\n}\n\np = spawn Probe()\nprint(p.Guarantee())\n";
        var windows = Builder.Compile(SourceText.FromString("program.kz", text), ["raw", "native"], Builder.CheckRuntime(Target.Windows, Arch.X64), true, cpu: () => Compiler.Emit.Cpu.Baseline);
        Assert.True(windows.Program is not null, string.Join("; ", windows.Diagnostics.Sorted));
        var ir = windows.Program.Ir.ReplaceLineEndings("\n");
        var first = System.Text.RegularExpressions.Regex.Match(ir, @"(?ms)^define [^\n]*@""?rt\.sys\.StackFirst\b[^\n]*\n(.*?)^}");
        Assert.True(first.Success, "no StackFirst in the IR");
        Assert.Contains("SetThreadStackGuarantee", first.Groups[1].Value, StringComparison.Ordinal);
        foreach (var word in new[] { "gs:[0x08]", "gs:[0x10]", "gs:[0x1478]", "gs:[0x1748]" })
        {
            Assert.Equal(2, ir.Split('\n').Count(l => l.StartsWith("module asm ", StringComparison.Ordinal) && l.Contains(word, StringComparison.Ordinal))); // read from the side it leaves, written from the other's
        }

        if (!OperatingSystem.IsWindows())
        {
            return; // `kernel32` is a library of Windows alone
        }

        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), ["raw", "native"], toolchain.Runtime, true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        Ran(Toolchain.Run(toolchain.Build(compilation.Program, "actor-guarantee"), null), 0, ["20480"], "");
    }

    // ---- waiting anywhere (design record, section 6, "Deadlocks", layer 1; the owner, 2026-11-15, #519) ------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_call_of_the_chain_reaches_the_actor_that_waits_in_it_as_a_nested_call(bool testBuild)
    {
        // U17 (section 6, "Deadlocks", layer 1): `Save` waits on `Check`, which waits on `Verify`, which calls `Load` on the store that
        // waits in their chain; `Load` runs on a stack of its own above `Save` (Q6 of comment 6892835500, "Own pooled stack"), and each
        // waiting message resumes once the calls above it ended, in a test build and a release build
        const string chain = """
            actor Store(Checker Check) {
                mut int saved = 0

                pub int Save(int n) {
                    saved = n
                    print("save waits")
                    valid = Check.Valid(this)
                    print("save resumes")
                    return valid + 1
                }

                pub int Load() {
                    print("load")
                    return saved
                }
            }

            actor Verifier {
                pub int Verify(Store store) {
                    print("verify waits")
                    loaded = store.Load()
                    print("verify resumes")
                    return loaded * 10
                }
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store) {
                    print("check waits")
                    verified = V.Verify(store)
                    print("check resumes")
                    return verified * 10
                }
            }

            v = spawn Verifier()
            c = spawn Checker(v)
            s = spawn Store(c)
            print(s.Save(4))
            print(s.Load())

            """;
        string[] expected = ["save waits", "check waits", "verify waits", "load", "verify resumes", "check resumes", "save resumes", "401", "load", "4"];
        Ran(BuildAndRun(chain, toolchain.Runtime, testBuild ? "actor-chain" : "actor-chain-release", testBuild), 0, expected, "");
    }

    /// <summary>
    /// Two actors that call each other <paramref name="depth"/> deep in one chain, each call reaching an actor that waits in it, so that
    /// every call runs on a stack of its own (Q6 of comment 6892835500); on Linux the deepest call gives the count of the process's mappings
    /// and the top-level code prints that count and the count after the chain ended, each less the count before the first message.
    /// </summary>
    private static string PingPong(int depth, bool maps) => $$"""
        long Mappings() {
            {{(maps ? "bytes = File.ReadAll(\"/proc/self/maps\") else {\n        IoError => List<byte>()\n    }\n\n    text = string.FromUtf8(bytes) else {\n        InvalidUtf8 => \"\"\n    }\n\n    return long(text.Split('\\n').Count)" : "return 0")}}
        }

        actor Ping {
            pub long Down(Pong other, int n) {
                if n == 0 {
                    return Mappings()
                }

                return other.Down(this, n - 1)
            }
        }

        actor Pong {
            pub long Down(Ping other, int n) {
                if n == 0 {
                    return Mappings()
                }

                return other.Down(this, n - 1)
            }
        }

        before = Mappings()
        a = spawn Ping()
        b = spawn Pong()
        deep = a.Down(b, {{depth}})
        after = Mappings()
        print("{deep - before} {after - before}")

        """;

    [Fact]
    public void A_chain_of_calls_past_the_pool_takes_a_stack_for_each_and_the_pool_keeps_64_of_them()
    {
        // section 6, "Bound pool + trim deep" and Q12 of comment 6880404917: 200 calls of one chain hold 200 stacks at once, two mappings
        // each on Linux, and once the chain ended the thread's pool keeps 64 of them and gives the rest back to the system
        var linux = OperatingSystem.IsLinux();
        var run = BuildAndRun(PingPong(200, linux), toolchain.Runtime, "actor-pool");
        Assert.True((run.Code, run.Error) == (0, ""), $"exit code {run.Code}, error: {run.Error}");
        if (!linux)
        {
            Assert.Equal(["0 0"], run.Output);
            return; // the mappings are read from Linux's `/proc/self/maps`
        }

        var counts = run.Output.Single().Split(' ').Select(long.Parse).ToList();
        Assert.True(counts[0] >= 400, $"{counts[0]} mappings more while 200 stacks are held"); // the stacks are mappings of their own
        Assert.InRange(counts[1], 128, 140); // 64 stacks of two mappings, and room for the allocator's own
    }

    /// <summary>
    /// An actor whose message recurses <paramref name="depth"/> deep, then takes 100 messages, then recurses again on the stack it held;
    /// on Linux the top-level code prints the process's mappings between the two, once its call that waits ran the 100 messages.
    /// </summary>
    private static string Trimmed(int depth, bool maps) => $$"""
        actor Deep {
            mut long count = 0

            pub long Down(long n) {
                if n == 0 {
                    return 0
                }

                return Down(n - 1) + 1
            }

            pub void Add() {
                count = count + 1
            }

            pub long Count() => count
        }

        d = spawn Deep()
        print(d.Down({{depth}}))
        for i in 0..<100 {
            send d.Add()
        }

        print(d.Count())
        {{(maps ? "bytes = File.ReadAll(\"/proc/self/maps\") else {\n    IoError => List<byte>()\n}\n\nprint(string.FromUtf8(bytes) else {\n    InvalidUtf8 => \"\"\n})" : "print(\"\")")}}
        print(d.Down({{depth}}))

        """;

    [Fact]
    public void A_stack_a_message_took_past_the_depth_is_trimmed_in_the_pool_and_grows_again()
    {
        // section 6, "Bound pool + trim deep", comments 6853074601 and 6855469829: a message's calls past 64 KiB, about a megabyte, open the
        // closed pages of its stack, which the stack's return to the pool gives back and closes again, so that on Linux the stack is a
        // closed range of 2 MiB less 64 KiB, joined to its guard page or not, under 64 KiB open for reading and writing; the same calls
        // then run again on that stack, which on Windows grows through the guard page the trim set again; the counts are Linux's halved
        // on Windows, whose frames are twice as large (Deep)
        var linux = OperatingSystem.IsLinux();
        var depth = OperatingSystem.IsWindows() ? 10000 : 20000;
        var run = BuildAndRun(Trimmed(depth, linux), toolchain.Runtime, "actor-trim");
        Assert.True((run.Code, run.Error) == (0, ""), $"exit code {run.Code}, error: {run.Error}");
        Assert.Equal([$"{depth}", "100"], run.Output.Take(2));
        Assert.Equal($"{depth}", run.Output[^1]);
        if (!linux)
        {
            return; // the mappings are read from Linux's `/proc/self/maps`
        }

        var maps = run.Output.Skip(2).SkipLast(1).Where(l => l.Length > 0).Select(l =>
        {
            var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var range = parts[0].Split('-').Select(x => Convert.ToInt64(x, 16)).ToArray();
            return (Low: range[0], High: range[1], Access: parts[1]);
        }).ToList();
        var closed = 2097152 - 65536;
        Assert.Contains(maps, m => m.Access == "---p" && m.High - m.Low >= closed && m.High - m.Low <= closed + 65536 && maps.Any(o => o.Low == m.High && o.Access == "rw-p"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_registers_a_parked_message_keeps_are_its_own_again_when_it_resumes(bool testBuild)
    {
        // the switch keeps the registers the convention keeps across a call in the save area at the base of each stack (the owner,
        // 2026-11-15, #519, comment 6892812780, Q1, "Save area at stack base"): 8 longs and 12 doubles, each the result of a call that
        // parked the message, live across every later call that parks it, in a release build in the registers it keeps, xmm6 to xmm15
        // among them on Windows, while the source's messages run on a stack of their own
        const string text = """
            actor Source {
                mut long n = 0

                pub long Get(long k) {
                    n = n + 1
                    return k * 1000 + n
                }
            }

            actor Mixer(Source S) {
                pub string Mix() {
                    long a = S.Get(1)
                    long b = S.Get(2)
                    long c = S.Get(3)
                    long d = S.Get(4)
                    long e = S.Get(5)
                    long f = S.Get(6)
                    long g = S.Get(7)
                    long h = S.Get(8)
                    double p = double(S.Get(9)) / 2.0
                    double q = double(S.Get(10)) / 4.0
                    double r = double(S.Get(11)) / 8.0
                    double s = double(S.Get(12)) / 16.0
                    double t = double(S.Get(13)) / 32.0
                    double u = double(S.Get(14)) / 64.0
                    double v = double(S.Get(15)) / 2.0
                    double w = double(S.Get(16)) / 4.0
                    double x = double(S.Get(17)) / 8.0
                    double y = double(S.Get(18)) / 16.0
                    double z = double(S.Get(19)) / 32.0
                    double o = double(S.Get(20)) / 64.0
                    return "{a} {b} {c} {d} {e} {f} {g} {h} {p} {q} {r} {s} {t} {u} {v} {w} {x} {y} {z} {o}"
                }
            }

            source = spawn Source()
            m = spawn Mixer(source)
            print(m.Mix())

            """;
        string[] expected = ["1001 2002 3003 4004 5005 6006 7007 8008 4504.5 2502.5 1376.375 750.75 406.65625 218.96875 7507.5 4004 2127.125 1126.125 594.34375 312.8125"];
        Ran(BuildAndRun(text, toolchain.Runtime, testBuild ? "actor-park-registers" : "actor-park-registers-release", testBuild), 0, expected, "");
    }

    [Fact]
    public void A_wait_of_the_top_level_code_that_nothing_left_to_run_can_end_is_a_deadlock_at_its_line()
    {
        // section 6, "Deadlocks" (the owner, 2026-11-15, #519, comment 6892812780, Q4, "deadlock at top-level wait"): each `Ask` waits in
        // a chain of its own on the other actor, which waits in the other chain, so neither takes the other's call (layer 1), and the
        // top-level code's wait, in a function it called, finds nothing left to run: the process ends at that call's line, not at a
        // line of the loop, with the exit code 1
        const string text = """
            actor Left {
                pub void Ask(Right other) {
                    print("left asks")
                    print("left got {other.Value()}")
                }

                pub int Value() => 1
            }

            actor Right {
                pub void Ask(Left other) {
                    print("right asks")
                    print("right got {other.Value()}")
                }

                pub int Value() => 2
            }

            int Start(Left l, Right r) {
                send l.Ask(r)
                send r.Ask(l)
                return l.Value()
            }

            left = spawn Left()
            right = spawn Right()
            print(Start(left, right))

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-deadlock"), 1, ["left asks", "right asks"], "error: deadlock at program.kz:22\n");
    }

    [Fact]
    public void A_message_parked_when_the_top_level_code_ends_is_dropped_with_the_process()
    {
        // U13 (the owner, 2026-11-15, #519, comment 6892835500, Q5, "Rust: dropped"): `Run` waits on `Open`, queued behind `Count`, whose
        // reply ends the top-level code's last wait; the program then ends, and `Run`'s line after its wait never runs
        const string text = """
            actor Gate {
                mut int opened = 0

                pub void Open() {
                    opened = opened + 1
                }

                pub int Count() => opened
            }

            actor Slow(Gate G) {
                pub void Run() {
                    print("before")
                    G.Open()
                    print("after")
                }
            }

            g = spawn Gate()
            s = spawn Slow(g)
            send s.Run()
            print(g.Count())

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-parked-end"), 0, ["before", "0"], "");
    }

    [Fact]
    public void Each_call_that_waits_and_each_send_of_the_top_level_code_starts_a_chain_of_its_own()
    {
        // section 6, "Deadlocks", layer 1 (assumed there, as comment 6892786121 took it): `Slow`, sent by the top-level code, waits on
        // the gate, and the top-level code's later call of `Peek` is of another chain, so it waits in the inbox until `Slow` ended,
        // where one chain of the top-level code would let it in above `Slow`
        const string text = """
            actor Gate {
                pub int Pass() => 1
            }

            actor Other {
                pub int Ping() => 0
            }

            actor Worker(Gate G) {
                pub void Slow() {
                    print("slow starts")
                    t = G.Pass()
                    print("slow ends {t}")
                }

                pub int Peek() {
                    print("peek")
                    return 2
                }
            }

            g = spawn Gate()
            o = spawn Other()
            w = spawn Worker(g)
            send w.Slow()
            print(o.Ping())
            print(w.Peek())

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-chains"), 0, ["slow starts", "0", "slow ends 1", "peek", "2"], "");
    }

    /// <summary>A store whose <c>Load</c> recurses <c>n</c> deep, called through a checker in the chain of <c>Save</c>, or as a message of its own.</summary>
    private static string Overflowing(string last) => $$"""
        actor Store(Checker Check) {
            pub long Save(long n) => Check.Valid(this, n)

            pub long Load(long n) => Down(n)

            long Down(long n) {
                if n == 0 {
                    return 0
                }

                return Down(n - 1) + 1
            }
        }

        actor Checker {
            pub long Valid(Store store, long n) => store.Load(n)
        }

        c = spawn Checker()
        s = spawn Store(c)
        print(s.Save(1000))
        {{last}}

        """;

    [Fact]
    public void An_overflow_in_a_call_of_the_chain_is_a_stack_overflow_whose_trace_ends_at_that_calls_method()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // an overflow on Windows ends the process as the system ends it (compiler/README.md, "What the emitter does not emit")
        }

        // the bounds of the guard page the handler reads are set at each switch, since a thread holds several stacks (design record,
        // section 2): `Load`'s calls, reached as a call of `Save`'s chain on a stack of their own, past its 2 MiB, fault in that stack's
        // guard page, after the stacks of the first `Save` went back to the pool; the walk of the trace ends at `Load`'s frame, the first of
        // its stack, so the frames it counts are those of `Load` reached as a message of its own
        var callback = BuildAndRun(Overflowing("print(s.Save(100000))"), toolchain.Runtime, "actor-callback-overflow", trace: null);
        var message = BuildAndRun(Overflowing("print(s.Load(100000))"), toolchain.Runtime, "actor-message-overflow", trace: null);
        foreach (var run in new[] { callback, message })
        {
            Assert.Equal((1, "1000"), (run.Code, string.Join('\n', run.Output)));
            Assert.Equal("error: stack-overflow at program.kz:11", run.Error.Split('\n')[0]);
        }

        static string Left(string error) => error.Split('\n').Single(l => l.StartsWith("  ... ", StringComparison.Ordinal));
        Assert.Equal(Left(message.Error), Left(callback.Error));
    }
}
