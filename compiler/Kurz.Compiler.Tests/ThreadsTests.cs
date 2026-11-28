using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The count of worker threads that the start of a program with a <c>spawn</c> reads from <c>KURZ_THREADS</c> (U35), which a corpus case
/// sets only to 1, by its <c>// threads: 1</c> line, or takes away, where the runner sets no count of its own (<c>RunTests</c>), so that no
/// case gives it a refused value: a value that is no whole number above 0 written in digits alone, or above the greatest
/// <c>int</c>, or not UTF-8, ends the start with <c>threads-variable</c> and the exit code 1. Each program is built for Linux and run with
/// <c>KURZ_THREADS</c> set or taken away; a build for Windows reads no <c>KURZ_THREADS</c>, its runtime giving no variable of the
/// environment (S14), so the tests that run a program are skipped on Windows, where <c>Ran</c> gives null and they check nothing, while
/// the test of the marks of the worker threads' forms, which emits a program for Linux on x86-64 and optimises its IR with clang, running
/// no program, runs on every host.
/// </summary>
public sealed class ThreadsTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const string Spawns = "actor Pinger {\n    pub int Ping() => 1\n}\n\np = spawn Pinger()\nprint(p.Ping())\n";

    /// <summary>
    /// The program built for Linux and run by <c>/bin/sh</c> as <c>exec "$0"</c> after <paramref name="setup"/>, with <c>KURZ_THREADS</c>
    /// taken away and <c>KURZ_TRACE=0</c>, with the runtime <paramref name="runtime"/> or the toolchain's: its exit code, its lines and its
    /// error stream; null on Windows.
    /// </summary>
    private (int Code, IReadOnlyList<string> Output, string Error)? Ran(string text, string name, string setup, CheckedRuntime? runtime = null)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            return null;
        }

        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], runtime ?? toolchain.Runtime, testBuild: true);
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
    [InlineData("")] // not set: the cores are read (`Cores`), and as many worker threads start where they are more than 1 (`workersAsked`)
    public void A_KURZ_THREADS_that_is_a_whole_number_above_0_or_none_starts_the_program(string setup)
    {
        if (Ran(Spawns, Named("threads-ok", setup), setup) is { } run)
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
    /// The program of <see cref="A_call_through_an_interface_on_worker_threads_frees_the_heap_of_its_message"/>: 300000 calls of one
    /// actor to another through an interface inside one message, then the line <c>VmHWM</c> of the system's <c>/proc/self/status</c>,
    /// the peak of the process's resident memory.
    /// </summary>
    private const string InterfaceCalls = """
        interface Store {
            long Get(string key)
        }

        actor Cache : Store {
            pub long Get(string key) => long(key.Bytes.Count)
        }

        actor Driver {
            pub long Run(int count) {
                Store c = spawn Cache()
                mut long total = 0
                mut int i = 0
                while i < count {
                    total = total + c.Get("item number {i}")
                    i = i + 1
                }

                return total
            }
        }

        string | IoError Peak() {
            bytes = File.ReadAll("/proc/self/status")
            text = string.FromUtf8(bytes) else {
                InvalidUtf8 => ""
            }

            for line in text.Split('\n') {
                if line.StartsWith("VmHWM:") {
                    return line
                }
            }

            return "none"
        }

        void | IoError Show() {
            print(spawn Driver().Run(300000))
            print(Peak())
        }

        Show() else {
            IoError e => print(e)
        }

        """;

    /// <summary>
    /// U1, U35, T17: a call through an interface that an actor implements builds its message in a heap of its own on worker threads, which
    /// the field of the message names (<c>Emitter.OwnHeapStored</c>), so that the receiver copies the arguments out of it and frees it, as
    /// every other message's: 300000 such calls inside one message on four worker threads keep the peak of the process's resident memory
    /// under 64 MiB. Built with the slot that left that field unwritten, the program kept every such heap: its peak was 234.8 MB on four
    /// worker threads, against 9.6 MB with the field written and 8.7 MB for the same calls made directly (2026-11-27, `--release` builds).
    /// </summary>
    [Fact]
    public void A_call_through_an_interface_on_worker_threads_frees_the_heap_of_its_message()
    {
        if (Ran(InterfaceCalls, "threads-interface-heap", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal("5288890", run.Output[0]);
            var peak = Regex.Match(run.Output[1], "^VmHWM:\\s+([0-9]+) kB$");
            Assert.True(peak.Success, run.Output[1]);
            Assert.True(long.Parse(peak.Groups[1].Value, CultureInfo.InvariantCulture) < 65536, run.Output[1]);
        }
    }

    /// <summary>
    /// U35, T16 2: the wait of a call of the top-level code reads no block of the call's message after its push, since the callee's
    /// worker can run the message and free its heap of its own while the main thread holds no epoch (`OwnLeft`, `Unpinned`), but where
    /// it finds nothing left to run, under the lock of the process (`Answer`): 2000 calls to fresh actors on four worker threads, built
    /// with the allocator that makes every block it frees inaccessible (<see cref="ListTests.Fenced"/>), so that such a read ends the run
    /// with the system's signal: while the wait read the message's chain before it, 64 of 100 runs of this program ended so beside a run
    /// of the suite, and none of 100 after the change. A run on a machine with no other load ends so less often, so that this test can
    /// pass on a build that reads the freed block.
    /// </summary>
    [Fact]
    public void The_wait_of_a_call_of_the_top_level_code_reads_no_block_of_its_message_that_a_worker_freed()
    {
        const string text = """
            actor Echo {
                pub int Get(int n) => n
            }

            mut sum = 0
            mut i = 0
            while i < 2000 {
                e = spawn Echo()
                sum = sum + e.Get(i)
                i = i + 1
            }

            print(sum)

            """;
        if (Ran(text, "threads-echo-fenced", "export KURZ_THREADS=4; ", ListTests.Fenced) is { } run)
        {
            Assert.Equal((0, ""), (run.Code, run.Error));
            Assert.Equal(["1999000"], run.Output);
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
    /// and the top-level code sleeps on the main thread; the actor's sleep lasts at least its length. The sleep of 500 ms that `Nap`
    /// prints after is the margin by which `7` comes first, which a wake late by tens of milliseconds on a loaded machine keeps.
    /// </summary>
    [Fact]
    public void An_actor_that_sleeps_on_a_worker_thread_parks_while_the_others_run()
    {
        const string text = """
            actor Napper {
                pub void Nap() {
                    Environment.Sleep(500ms)
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
    /// U13, U21, U22, U35: the crash of <c>Parent</c> on a worker thread ends its child, under a hold of the child that its crash's thread
    /// takes (the runtime's <c>HeldAlone</c>, <c>EndChildren</c>), before its restart, so that the top-level
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
    /// U19, U35: the crash of <c>Flaky</c> and its restart hold <c>Flaky</c> alone, while <c>Busy</c> runs a message of three seconds on
    /// another worker thread with no call and no wait in it (section 6, the owner, 2026-11-26, #519, comment 7194874125, "Per-actor holds
    /// (Recommended)"), so that the top-level code's call of <c>Ping</c>, which waits in the inbox for the restart, returns well within
    /// that message, where a world that waits for every other worker's turn would hold the crash to its end.
    /// </summary>
    [Fact]
    public void A_crash_on_a_worker_thread_holds_only_its_actor_while_another_worker_runs_a_long_message()
    {
        const string text = """
            void Hold(duration d) {
                instant start = instant.Now
                while start.Elapsed < d {
                }
            }

            actor Busy {
                pub void Spin() {
                    Hold(3s)
                }
            }

            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            b = spawn Busy()
            f = spawn Flaky() on crash restart after 1ms
            send b.Spin()
            instant begun = instant.Now
            send f.Fail()
            print(f.Ping())
            print(begun.Elapsed < 2s)

            """;
        if (Ran(text, "threads-hold", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["1", "true"], run.Output);
            Assert.Equal(["Flaky#2 crashed: thrown \"flaky\" at program.kz:15"], Logged(run.Error));
        }
    }

    /// <summary>
    /// U25, U35: a crash that <c>Worker</c>'s policy escalates on a worker thread reaches <c>Boss</c> as a signal, which the holder of
    /// <c>Boss</c> takes before its messages (the runtime's <c>Forwarded</c> and <c>Signaled</c>; section 6, the owner, 2026-11-26, #519,
    /// comment 7194874125), so that <c>Boss</c> falls with <c>Worker</c>'s crash and writes the one log line in its own name with the
    /// child's words, as one thread writes it, and restarts with its count at 0, which the top-level code's calls of <c>Count</c> find
    /// once the restart ran.
    /// </summary>
    [Fact]
    public void A_crash_a_child_escalates_on_a_worker_thread_reaches_its_supervisor_as_a_signal()
    {
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }
            }

            actor Boss {
                mut int n = 0

                pub void Go() {
                    n = n + 1
                    w = spawn Worker() on crash escalate
                    send w.Fail()
                }

                pub int Count() => n
            }

            b = spawn Boss()
            b.Go()
            mut seen = b.Count()
            mut tries = 0
            while seen != 0 && tries < 1000000 {
                seen = b.Count()
                tries = tries + 1
            }

            print(seen)

            """;
        if (Ran(text, "threads-escalate", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["0"], run.Output);
            Assert.Equal(["Boss#1 crashed: thrown \"worker fails\" at program.kz:3"], Logged(run.Error));
        }
    }

    /// <summary>
    /// U26, U35: the crash of <c>Flaky</c> past its <c>max 1 in 1min</c> on a worker thread sends its supervisor <c>restart-limit</c> as a
    /// signal (section 6, the owner, 2026-11-26, #519, comment 7194874125), so that <c>Boss</c> crashes with <c>restart-limit</c> at the
    /// line of the <c>spawn</c> after the two log lines of <c>Flaky</c>'s crashes, as one thread writes them, and restarts with its count at 0.
    /// </summary>
    [Fact]
    public void A_restart_limit_on_a_worker_thread_reaches_the_supervisor_as_a_signal()
    {
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }
            }

            actor Boss {
                mut int n = 0

                pub void Go() {
                    n = n + 1
                    f = spawn Flaky() on crash restart after 1ms max 1 in 1min
                    send f.Fail()
                    send f.Fail()
                }

                pub int Count() => n
            }

            b = spawn Boss()
            b.Go()
            mut seen = b.Count()
            mut tries = 0
            while seen != 0 && tries < 1000000 {
                seen = b.Count()
                tries = tries + 1
            }

            print(seen)

            """;
        if (Ran(text, "threads-restart-limit", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["0"], run.Output);
            Assert.Equal(
                ["Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Boss#1 crashed: restart-limit at program.kz:12"],
                Logged(run.Error));
        }
    }

    /// <summary>
    /// U35, U60: in each of 200 rounds <c>Left</c> and <c>Right</c> call each other at once on two worker threads, each holding its thread
    /// to the same moment, 2 ms after the round's start, and the walk of each
    /// wait, which runs with no lock, may find the loop at the same moment; the loop is confirmed under the lock of the process before a
    /// wait raises (section 6, T72 2; the runtime's <c>LoopHeld</c>), so that at most one of the two raises <c>deadlock</c> in a round,
    /// its new instance answering the other's call, as <c>actors/deadlock.kz</c> shows on one thread; a round whose call of
    /// <c>Value</c> reaches the other's inbox before the top-level code's <c>send</c> of <c>Ask</c> closes no loop.
    /// </summary>
    [Fact]
    public void A_loop_two_actors_close_at_once_on_worker_threads_raises_one_deadlock()
    {
        const string text = """
            actor Left {
                mut int got = 0

                pub void Ask(Right other, instant start) {
                    while start.Elapsed < 2ms {
                    }

                    got = other.Value()
                }

                pub int Value() => 1
            }

            actor Right {
                mut int got = 0

                pub void Ask(Left other, instant start) {
                    while start.Elapsed < 2ms {
                    }

                    got = other.Value()
                }

                pub int Value() => 2
            }

            mut i = 0
            mut sum = 0
            while i < 200 {
                l = spawn Left() on crash restart after 1ms
                r = spawn Right() on crash restart after 1ms
                begun = instant.Now
                send l.Ask(r, begun)
                send r.Ask(l, begun)
                sum = sum + l.Value() + r.Value()
                i = i + 1
            }

            print(sum)

            """;
        if (Ran(text, "threads-loop-once", "export KURZ_THREADS=4; ") is { } run)
        {
            Assert.Equal(0, run.Code);
            Assert.Equal(["600"], run.Output);
            var rounds = Logged(run.Error).Select(line => Regex.Match(line, "^(?:Left|Right)#([0-9]+) crashed: deadlock ")).ToList();
            Assert.All(rounds, match => Assert.True(match.Success, match.Value));
            Assert.NotEmpty(rounds);
            Assert.All(rounds.GroupBy(match => (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1) / 2), round => Assert.Single(round));
        }
    }

    /// <summary>
    /// The name of a build of the kind <paramref name="kind"/> after <paramref name="setup"/>, the same in every run of the tests: the
    /// first 16 hexadecimal digits of the SHA-256 of the setup's UTF-8 bytes, where <c>string.GetHashCode</c> differs in each process.
    /// </summary>
    private static string Named(string kind, string setup) =>
        $"{kind}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(setup)))[..16]}";

    /// <summary>
    /// The lines of <paramref name="error"/>, where the log line of an actor's crash has its time, its level, the span of P8 and its
    /// category taken off, and every other line stays whole.
    /// </summary>
    private static List<string> Logged(string error) =>
        [.. error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => Regex.Replace(line, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z Error [^{:\\s]+(?:\\{chain=[0-9a-f]{32}\\})?: Kurz\\.Actor: ", ""))];

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
    /// U35: the path of a message on one thread pays, at each form on it that only the worker threads go on in, the test of the worker
    /// threads, a load and a branch, and no call (the owner, 2026-11-26, #519, comment
    /// 7204486070, Q1): the emitter marks the forms only the worker threads reach <c>noinline</c> and <c>cold</c>
    /// (<c>Emitter.WorkerForms</c>), and the helpers of that path that the worker threads call too <c>alwaysinline</c>
    /// (<c>Emitter.InLine</c>). A program of sends and a call to an actor, built for Linux on x86-64 on every machine, since a build
    /// for Windows starts no worker thread (<c>compiler/README.md</c>), its IR optimised as a release build optimises it, calls
    /// none of the helpers, which stayed calls without the mark, and still calls <c>RootSlept</c>, which its one caller took in
    /// without its mark (LLVM writes a name without its quotes where it needs none).
    /// </summary>
    [Fact]
    public void A_build_of_one_thread_takes_the_helpers_of_a_message_into_their_callers_and_keeps_the_forms_of_the_worker_threads_calls()
    {
        const string text = """
            actor Counter {
                mut long n = 0

                pub void Add(long x) {
                    n = n + x
                }

                pub long Get() => n
            }

            c = spawn Counter()
            mut long i = 0
            while i < 1000 {
                send c.Add(1)
                i = i + 1
            }

            print(c.Get())

            """;
        var program = Builder.Compile(SourceText.FromString("program.kz", text), [], Builder.CheckRuntime(Target.Linux, Arch.X64), testBuild: false, cpu: () => Cpu.Baseline).Program!;
        var defines = program.Ir.Split('\n').Where(l => l.StartsWith("define internal ", StringComparison.Ordinal)).ToList();
        foreach (var form in new[] { "RootSlept", "QueueLocal", "QueueHeld", "Answered", "Deferred", "PinnedHeld", "LeftToHolder" })
        {
            var define = defines.Single(l => l.Contains($"@\"rt.common.{form}.", StringComparison.Ordinal));
            Assert.Contains(" noinline", define, StringComparison.Ordinal);
            Assert.Contains(" cold", define, StringComparison.Ordinal);
        }

        var directory = Path.Combine(toolchain.Directory, "threads-one-path");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "program.ll");
        var output = Path.Combine(directory, "optimised.ll");
        File.WriteAllText(input, program.Ir);
        List<string> arguments = ["--target=x86_64-unknown-linux-gnu", "-O2", "-S", "-emit-llvm", input, "-o", output];
        if (program.CpuFlag is { } cpu)
        {
            arguments.Add(cpu);
        }

        var (code, _, error) = Processes.Capture(toolchain.Clang.Path, arguments, null, TimeSpan.FromMinutes(2));
        Assert.True(code == 0, error);
        var optimised = File.ReadAllLines(output);
        static int Calls(IEnumerable<string> lines, string name) =>
            lines.Count(l => l.Contains("call ", StringComparison.Ordinal)
                && (l.Contains($"@\"rt.common.{name}.", StringComparison.Ordinal) || l.Contains($"@rt.common.{name}.", StringComparison.Ordinal)));
        foreach (var helper in new[] { "Fire", "RootTaken", "Give", "Replied" })
        {
            Assert.True(Calls(optimised, helper) == 0, $"`{helper}` is a call");
        }

        Assert.True(Calls(optimised, "RootSlept") >= 1, "`RootSlept` is put into its caller");
    }

    /// <summary>
    /// U35: a start of the worker threads that the system refuses a descriptor for, here the <c>eventfd</c> of the second worker under a
    /// limit of six descriptors, ends the process with <c>thread-refused</c> naming the count asked and the exit code 1, after what the
    /// program printed before its first <c>spawn</c>: a refused worker ends the start with an error line, an id of its own and the exit
    /// code 1 (section 6, the owner, 2026-11-20, #519, comment 7051492844, T15 1 of comment 6972618225), the id is <c>thread-refused</c>
    /// (the owner, 2026-11-26, #519, comment 7204486070, "Keep thread-refused (Recommended)"), and a worker whose <c>eventfd</c> the
    /// system refuses ends the start so too (assumed in section 6, a part of the worker that its start needs).
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
    /// U35: a worker thread maps the page its clock is read into at its start, and a worker whose page the system refuses there, here
    /// the second of four, ends the process with <c>thread-refused</c> naming the count asked and the exit code 1, while the top-level
    /// code sleeps after its first <c>spawn</c> (the owner, 2026-11-28, issue 673, comment 7236862307, Q2). The other three workers
    /// start, so that the refusal is that one worker's alone and no two refusals write their lines at once.
    /// </summary>
    [Fact]
    public void A_worker_whose_page_of_the_clock_the_system_refuses_at_its_start_ends_the_process_with_thread_refused()
    {
        if (Builder.HostTarget == Target.Windows)
        {
            return;
        }

        var refused = ListTests.Edited(
            ("runtime/linux/linux.kz", "        return status != null\n", "        return false\n"),
            ("runtime/common/runtime.kz", "if WakeArmed() < 0 || !sys.StatusMapped() {", "if WakeArmed() < 0 || (*Ptr<long>(record + 96) == 1 && !sys.StatusMapped()) {"));
        const string text = "actor Pinger {\n    pub int Ping() => 1\n}\n\nprint(\"start\")\np = spawn Pinger()\nEnvironment.Sleep(5s)\nprint(p.Ping())\n";
        if (Ran(text, "threads-refused-clock", "export KURZ_THREADS=4; ", refused) is { } run)
        {
            Assert.Equal((1, "error: thread-refused (4)\n"), (run.Code, run.Error));
            Assert.Equal(["start"], run.Output);
        }
    }

    /// <summary>
    /// U35, O11: the main thread maps the page its clock is read into at its first read, and where the system refuses that page there,
    /// here at the deadline of the top-level code's call (U36), the process ends with <c>allocation-failed (4096 bytes)</c> and the exit
    /// code 1, on one thread and on worker threads, whose own pages their start mapped (the owner, 2026-11-28, issue 673, comment
    /// 7236862307, Q2). The clock of 0 such a read gave before printed the answer on one thread and raised <c>call-timeout</c> on
    /// worker threads, where a worker read that deadline as passed.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("4")]
    public void A_main_thread_whose_page_of_the_clock_the_system_refuses_ends_the_process_with_allocation_failed_at_its_first_read(string threads)
    {
        if (Builder.HostTarget == Target.Windows)
        {
            return;
        }

        var refused = ListTests.Edited(
            ("runtime/linux/linux.kz", "thread static mut byte* status = null\n", "thread static mut byte* status = null\nthread static mut bool started = false\n"),
            ("runtime/linux/linux.kz", "        return status != null\n", "        started = true\n        return status != null\n"),
            ("runtime/linux/linux.kz", "        clock_gettime(1, Ptr<void>(status))", "        if !started {\n            return false\n        }\n\n        clock_gettime(1, Ptr<void>(status))"));
        if (Ran("print(\"start\")\n" + Spawns, $"threads-clock-refused-{threads}", $"export KURZ_THREADS={threads}; ", refused) is { } run)
        {
            Assert.Equal((1, "error: allocation-failed (4096 bytes)\n"), (run.Code, run.Error));
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
        if (Ran(text, Named("threads-refused", setup), setup) is { } run)
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
