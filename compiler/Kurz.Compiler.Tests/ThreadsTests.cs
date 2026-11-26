using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The count of worker threads that the start of a program with a <c>spawn</c> reads from <c>KURZ_THREADS</c> (U35), which no corpus case
/// can set, its header holding no environment: a value that is no whole number above 0 written in digits alone, or above the greatest
/// <c>int</c>, or not UTF-8, ends the start with <c>threads-variable</c> and the exit code 1. Each program is built for Linux and run with
/// <c>KURZ_THREADS</c> set or taken away; a build for Windows reads no <c>KURZ_THREADS</c>, its runtime giving no variable of the
/// environment (S14), and no test runs there.
/// </summary>
public sealed class ThreadsTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const string Spawns = "actor Pinger {\n    pub int Ping() => 1\n}\n\np = spawn Pinger()\nprint(p.Ping())\n";

    /// <summary>
    /// The program built for Linux and run by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="setup"/>, with <c>KURZ_THREADS</c>
    /// taken away and <c>KURZ_TRACE=0</c>: its exit code, its lines and its error stream; null on Windows.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            return null;
        }

        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, name);
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup}exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0", ["KURZ_THREADS"] = null });
        return (code, Toolchain.Lines(output), error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")] // empty, as tokio refuses an empty `TOKIO_WORKER_THREADS`
    [InlineData("0")] // no whole number above 0
    [InlineData("-1")]
    [InlineData("+4")] // digits alone, where Rust's parse of a `usize` takes the `+`
    [InlineData(" 4")]
    [InlineData("4 ")]
    [InlineData("4.0")]
    [InlineData("2147483648")] // the greatest `int` and 1
    [InlineData("99999999999999999999")] // beyond a `long` too
    public void A_KURZ_THREADS_that_is_no_whole_number_above_0_ends_the_start_with_threads_variable_naming_the_value(string value) =>
        Refused($"export KURZ_THREADS='{value}'; ", $"error: threads-variable ({value})\n");

    [Fact]
    public void A_KURZ_THREADS_that_is_not_UTF_8_ends_the_start_with_threads_variable_not_unicode() =>
        Refused("export KURZ_THREADS=\"$(printf '4\\377')\"; ", "error: threads-variable (not unicode)\n");

    [Theory]
    [InlineData("export KURZ_THREADS=1; ")]
    [InlineData("export KURZ_THREADS=4; ")]
    [InlineData("export KURZ_THREADS=007; ")] // digits alone, zeros in front among them
    [InlineData("")] // not set: the cores the process may use
    public void A_KURZ_THREADS_that_is_a_whole_number_above_0_or_none_starts_the_program(string setup)
    {
        if (Ran(Spawns, $"threads-ok-{Math.Abs(setup.GetHashCode(StringComparison.Ordinal))}", setup) is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["1"], run.Output);
        }
    }

    private const string Crossing = """
        actor Counter {
            mut int count = 0
            mut string last = ""

            pub void Add(int n, string tag) {
                count = count + n
                last = tag
            }

            pub int Get() => count

            pub string Last() => "last {last}"
        }

        actor Hub {
            pub void Fire(List<Counter> all, int rounds) {
                for i in 0..<rounds {
                    for c in all {
                        send c.Add(1, "r{i}")
                    }
                }
            }

            pub int Total(List<Counter> all) {
                mut total = 0
                for c in all {
                    total = total + c.Get()
                }

                return total
            }
        }

        a = spawn Counter()
        b = spawn Counter()
        k = spawn Counter()
        h = spawn Hub()
        g = spawn Hub()
        send h.Fire([a, b, k], 300)
        send g.Fire([k, b, a], 300)
        print(h.Total([a, b, k]) >= 900)
        print(g.Total([a, b, k]))
        print(a.Last())

        """;

    /// <summary>
    /// U35, T17: on four worker threads two actors send 1800 messages, each with a <c>string</c> built in a heap of its own, to three
    /// others, which copy it into their own heaps at their turn, and the top-level code's calls read the counts and a result that holds
    /// a block, which the caller copies out of the callee's heap of its own: each hub's <c>Total</c> stands behind its <c>Fire</c> in its
    /// inbox, and each of its calls of <c>Get</c> behind its own sends (U5, U15), so that the second total counts every message.
    /// </summary>
    [Fact]
    public void Actors_on_worker_threads_take_messages_whose_arguments_and_results_cross_between_their_heaps()
    {
        if (Ran(Crossing, "threads-crossing", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["true", "1800", "last r299"], run.Output);
        }
    }

    /// <summary>
    /// U5, U35: the messages one sender sends to one actor run in the order it sent them on worker threads, a hub's sends and the
    /// top-level code's alike, each receiver counting a message that does not follow the one before it.
    /// </summary>
    [Fact]
    public void The_messages_of_one_sender_run_in_its_order_on_worker_threads()
    {
        const string text = """
            actor Checker {
                mut int expected = 0
                mut int wrong = 0

                pub void Take(int n) {
                    if n != expected {
                        wrong = wrong + 1
                    }

                    expected = n + 1
                }

                pub string Report() => "{expected} {wrong}"
            }

            actor Sender {
                pub void Run(Checker target, int count) {
                    for i in 0..<count {
                        send target.Take(i)
                    }
                }

                pub int Done() => 1
            }

            x = spawn Checker()
            y = spawn Checker()
            s = spawn Sender()
            send s.Run(x, 3000)
            for i in 0..<3000 {
                send y.Take(i)
            }

            print(s.Done())
            print(x.Report())
            print(y.Report())

            """;
        if (Ran(text, "threads-order", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["1", "3000 0", "3000 0"], run.Output);
        }
    }

    /// <summary>
    /// S28, U35: an actor that sleeps on a worker thread parks among that thread's timers, which wake it there, while the others run,
    /// and the top-level code sleeps on the main thread; the actor's sleep lasts at least its length.
    /// </summary>
    [Fact]
    public void An_actor_that_sleeps_on_a_worker_thread_parks_while_the_others_run()
    {
        const string text = """
            actor Napper {
                pub void Nap() {
                    Environment.Sleep(50ms)
                    print("napped")
                }

                pub bool Measured() {
                    start = instant.Now
                    Environment.Sleep(20ms)
                    return start.Elapsed >= 20ms
                }
            }

            actor Quick {
                pub int Get() => 7
            }

            a = spawn Napper()
            q = spawn Quick()
            send a.Nap()
            print(q.Get())
            print(a.Measured())
            Environment.Sleep(10ms)
            print("slept")

            """;
        if (Ran(text, "threads-sleep", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["7", "napped", "true", "slept"], run.Output);
        }
    }

    /// <summary>
    /// U18, U22, U35: where the top-level code waits and every worker thread parked with no actor to run, no activation asleep, no
    /// registration of its poller and no restart's pause, the last worker to park wakes the main thread, whose wait raises <c>deadlock</c>,
    /// the timers of the calls that wait counting for nothing, as on one thread (section 6, Go's <c>checkdead</c>): the top-level code's call
    /// of <c>Peek</c> waits in <c>A</c>'s inbox behind <c>Fail</c>, and at <c>A</c>'s restart its constructor waits on <c>B.Ask</c>, whose
    /// call of <c>Peek</c> waits in that inbox for the constructor, a loop through a start that layer 3 does not see (U60), so that the error
    /// names that call, as <c>actors/restart-reentrant.kz</c> shows on one thread, well within the five seconds of the calls' default bound.
    /// </summary>
    [Fact]
    public void A_wait_of_the_top_level_code_that_nothing_on_the_worker_threads_can_answer_raises_deadlock()
    {
        const string text = """
            actor B {
                mut A? a = null

                pub void Set(A x) {
                    a = x
                }

                pub int Ask() {
                    x = a
                    if x != null {
                        return vouch x.Peek()
                    }

                    return 0
                }
            }

            actor A(B Other) {
                int first = Other.Ask()

                pub int Peek() => first

                pub void Fail() {
                    throw "a fails"
                }
            }

            b = spawn B()
            a = spawn A(b)
            b.Set(a)
            send a.Fail()
            print(a.Peek())

            """;
        if (Ran(text, "threads-deadlock", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(1, run.Code);
            Assert.EndsWith("error: deadlock at program.kz:11\n", run.Error, StringComparison.Ordinal);
            Assert.Empty(run.Output);
        }
    }

    /// <summary>
    /// U13, U21, U22, U35: the crash of <c>Parent</c> on a worker thread ends its child, under the world that its crash's thread holds
    /// while every other thread waits (the runtime's <c>WorldTried</c>, <c>EndChildren</c>), before its restart, so that the top-level
    /// code, whose call of <c>Kid</c> waits in the inbox behind <c>Boom</c> for that restart, finds the child ended: its call of
    /// <c>Get</c> raises <c>actor-gone</c>, as the case <c>actors/crash-children-end.kz</c> shows on one thread, whose <c>send</c> to the
    /// child after <c>Boom</c> has no order on more than one.
    /// </summary>
    [Fact]
    public void The_children_of_an_actor_that_crashes_on_a_worker_thread_end_with_its_crash()
    {
        const string text = "actor Child {\n    pub int Get() => 1\n}\n\nactor Parent {\n    mut Child? kid = null\n\n    pub void Make() {\n"
            + "        kid = spawn Child()\n    }\n\n    pub Child? Kid() => kid\n\n    pub void Boom() {\n        throw \"parent fails\"\n    }\n}\n\n"
            + "p = spawn Parent()\np.Make()\nkid = p.Kid()\nsend p.Boom()\nprint(p.Kid() == null)\nif kid != null {\n    print(kid.Get())\n}\n";
        if (Ran(text, "threads-children", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(1, run.Code);
            Assert.Equal(["true"], run.Output);
            Assert.EndsWith("error: actor-gone at program.kz:25\n", run.Error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// U35: with <c>KURZ_THREADS</c> set to 1 every actor runs on the main thread in the top-level code's waits, first in, first out, as a
    /// case marked <c>// threads: 1</c> runs (assumed in section 6), so that the two actors <c>Go</c> wakes say their lines in the order it
    /// woke them, where one worker thread would run the actor woken last first, from its waker's slot, as Go's <c>runnext</c> runs it
    /// (section 6, Q5).
    /// </summary>
    [Fact]
    public void KURZ_THREADS_set_to_1_runs_every_actor_on_the_main_thread_first_in_first_out()
    {
        const string text = """
            actor Speaker {
                pub void Say(string s) {
                    print(s)
                }

                pub int Ping() => 1
            }

            actor Waker {
                pub void Go(Speaker first, Speaker second) {
                    send first.Say("a")
                    send second.Say("b")
                }
            }

            a = spawn Speaker()
            b = spawn Speaker()
            w = spawn Waker()
            w.Go(a, b)
            print(a.Ping() + b.Ping())

            """;
        if (Ran(text, "threads-one", "export KURZ_THREADS=1; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["a", "b", "2"], run.Output);
        }
    }

    /// <summary>
    /// U35: a start of the worker threads that the system refuses a descriptor for, here the <c>eventfd</c> of the second worker under a
    /// limit of six descriptors, ends the process with <c>thread-refused</c> naming the count asked and the exit code 1, after what the
    /// program printed before its first <c>spawn</c> (section 6, the owner, 2026-11-17, #572, comment 6961064484, Q2).
    /// </summary>
    [Fact]
    public void A_start_of_the_worker_threads_the_system_refuses_ends_the_process_with_thread_refused()
    {
        if (Ran("print(\"start\")\n" + Spawns, "threads-refused-start", "export KURZ_THREADS=4; ulimit -n 6; ") is { } run)
        {
            Assert.Equal((1, "error: thread-refused (4)\n"), (run.Code, run.Error));
            Assert.Equal(["start"], run.Output);
        }
    }

    /// <summary>
    /// A program with a <c>spawn</c>, started after <paramref name="setup"/>, ends with the exit code 1 and <paramref name="error"/> alone,
    /// before its first statement, while a program with no <c>spawn</c> under <paramref name="setup"/>, which reads no
    /// <c>KURZ_THREADS</c>, runs.
    /// </summary>
    private void Refused(string setup, string error)
    {
        const string text = "print(\"start\")\n" + Spawns;
        if (Ran(text, $"threads-refused-{Math.Abs(setup.GetHashCode(StringComparison.Ordinal))}", setup) is { } run)
        {
            Assert.Equal((1, error), (run.Code, run.Error));
            Assert.Empty(run.Output);
        }

        if (Ran("print(\"start\")\n", "threads-refused-none", setup) is { } none)
        {
            Assert.Equal((0, ""), (none.Code, none.Error));
            Assert.Equal(["start"], none.Output);
        }
    }
}
