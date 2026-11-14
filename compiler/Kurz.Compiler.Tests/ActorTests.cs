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
/// message's function and the runtime out (the lean of the round of 2026-11-13, issue 519, comment 6866132292, Q1); the order of one
/// sender's messages and of a message sent inside a message (U5, U15), in a test build and a release build; the run queue, one message
/// of each actor in turn in the order the actors were queued, as tokio's <c>current_thread</c> runs its tasks, an actor that sends itself
/// a message while it runs standing in that queue once (U11); what is still queued
/// when the top-level code ends, which does not run (U13); and a <c>mut</c> parameter in a <c>send</c> of an actor's own method,
/// which the emitter does not emit.
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
        // inbox has no bound of its own (the lean of the round of 2026-11-13, issue 519, comment 6866132292, Q1)
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
        var error = "error: divide-by-zero at program.kz:2\n" + (OperatingSystem.IsLinux() ? "  at Calc.Div in program.kz:2\n  at main in program.kz:7\n" : string.Empty);
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
    public void A_mut_parameter_in_a_send_of_the_actors_own_method_is_not_supported()
    {
        const string bump = """
            actor Worker {
                pub void Start() {
                    mut int x = 1
                    send Bump(mut x)
                    print(x)
                }

                pub void Bump(mut int k) {
                    k = k + 1
                }
            }

            w = spawn Worker()
            w.Start()

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", bump), [], toolchain.Runtime, testBuild: true);
        Assert.Null(compilation.Program);
        var only = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 4), (only.Kind, only.Line));
        Assert.EndsWith("(U9, U11)", only.Message, StringComparison.Ordinal);
    }
}
