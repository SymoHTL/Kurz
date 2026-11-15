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
/// the text of <c>this</c> and <c>!=</c> of two references (U2, U10); and a state aligned to the 16 a vector field needs (R18).
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
        Assert.Equal(lines, run.Output);
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
        // section 6, "Own stack", "Keep guard page", "2 MiB + clause": a message's 20000 calls, about a megabyte, run on the actor's
        // stack on every system; on Linux its 100000, about five, pass the 2 MiB and fault in the guard page below them, the
        // error line at the call's line and the trace ending at the method, while the same 100000 calls of the top-level code run on the
        // thread's own stack of 8 MiB (the root actor's code stays there, section 2)
        var text = "long Top(long n) {\n    if n == 0 {\n        return 0\n    }\n\n    return Top(n - 1) + 1\n}\n\n"
            + Deep("Deep")
            + "print(Top(100000))\nd = spawn Deep()\nprint(d.Down(20000))\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-stack-runs"), 0, ["100000", "20000"], "");
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
    public void A_thread_holds_one_stack_and_takes_another_for_a_message_that_asks_another_size()
    {
        // U1, the clause `stack`: a thread holds the stack of the size its last message asked for, so the 8 MiB actor's 100000 calls,
        // after a message of the actor of 2 MiB, run on a stack of 8 MiB, and the next message of 2 MiB on one of that size again
        var text = Deep("Big", " stack 8mb") + Deep("Plain") + "b = spawn Big()\np = spawn Plain()\nprint(p.Down(20000))\nprint(b.Down(100000))\nprint(p.Down(20000))\n";
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-stack-sizes"), 0, ["20000", "100000", "20000"], "");
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
}
