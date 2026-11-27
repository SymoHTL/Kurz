using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The actors the emitter builds on the one thread the runtime runs (chapter 19, <c>compiler/README.md</c>), in what no corpus case
/// pins: a call that waits behind a million messages on the stack of one, since the runtime's <c>Wait</c> runs them in a loop; the
/// block of every message and the counts of its arguments let go of once it ran, under a system that gives 16 pieces of memory, and
/// never before, under the allocator that makes every freed block inaccessible, and an inbox no call that waits runs, which grows
/// until that system's memory is gone; the state an actor's record holds, which a lambda's
/// count of <c>this</c> does not let go of; a crash at the root, the top-level code, which ends the process with its error line and its
/// trace (U19); the order of one
/// sender's messages and of a message sent inside a message (U5, U15), in a test build and a release build; the run queue, one message
/// of each actor in turn in the order the actors were queued, as tokio's <c>current_thread</c> runs its tasks, an actor that sends itself
/// a message while it runs standing in that queue once (U11); what is still queued
/// when the top-level code ends, which does not run (U13); and the number in an actor's text, one count over the actors of every type,
/// the text of <c>this</c> and <c>!=</c> of two references (U2, U10); and a state aligned to the 16 a vector field needs (R18). Then the
/// stacks of section 6, and the waits that park a message on its stack: a call of the chain that reaches the actor waiting in it (U17),
/// the pool and its trim, the registers a parked message keeps, the top-level code's <c>deadlock</c>, a parked message at the end (U13),
/// the chains the top-level code starts, and an overflow in a call of the chain. Then crashes (U19 to U24): the state after a restart,
/// the inbox kept, memory over 10,000 crashes, an overflow restarted, a reply into a caller that crashed, children ending, the pause,
/// the top-level code's sleep through it, <c>callee-crashed</c>, and the log line; the words of a policy (U25 to U27): a stopped actor, a
/// crash escalated to the root, into an actor and into one whose constructor runs, the first pause of <c>after</c>, the window of
/// <c>max N in</c> and <c>restart-limit</c>, the actor past its limit ended in its supervisor's fall, and the pause of <c>max</c> alone; a
/// function parameter that takes its default at a <c>spawn</c> and in a message; a stack fault inside the allocator, the scheduler, the
/// copy of a crossing and the list of the heaps of messages, and a check of the runtime's own code inside the scheduler, which end the
/// process, and the count of their entries back at 0; the size a refused heap at a crash names;
/// and <c>print-failed</c> and <c>not-char-boundary</c> in a message. Last, the block of <c>on crash</c> (U28 to U34): at the root and in
/// an actor, its order among the supervisor's messages, the state it reads, its three decisions, a throw in it, the type's block, the
/// thrown value's copy, the variables it takes after the spawn's arguments, a thrown <c>null</c>, the runtime's error, an overflow, the
/// blocks dropped, and memory over 10,000 crashes it restarts.
/// </summary>
public sealed class ActorTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, CheckedRuntime runtime, string name, bool testBuild = true, string? trace = "0", bool tests = false)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild, tests: tests);
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
    public void A_chain_of_a_million_links_crosses_by_copy_on_a_messages_stack()
    {
        // U12: the copy walks the runtime's work list and never recurses (comment 6895076937, "Taken as they follow from the record"), so a
        // chain of 1,000,000 instances crosses from inside a message, on its stack of 2 MiB, and arrives whole
        const string chain = """
            class Link(int Value, Link? Next)

            actor Sink {
                pub long Count(Link first) {
                    mut long n = 0
                    mut Link? at = first
                    while at != null {
                        n = n + 1
                        at = at.Next
                    }

                    return n
                }
            }

            actor Maker(Sink Target) {
                pub long Make() {
                    mut Link head = Link(0, null)
                    for i in 1..<1000000 {
                        head = Link(i, head)
                    }

                    return Target.Count(copy head)
                }
            }

            m = spawn Maker(spawn Sink())
            print(m.Make())

            """;
        Ran(BuildAndRun(chain, toolchain.Runtime, "actor-copy-chain"), 0, ["1000000"], "");
    }

    [Fact]
    public void References_to_one_instance_arrive_as_references_to_one_copy()
    {
        // U12 (the owner, 2026-11-15, #519, comment 6896192351, Q2, "Sharing kept"): 10,000 items of one mutable instance arrive as 10,000
        // items of one copy, which a write through the first shows at the last, and the sender's instance stays as it was
        const string shared = """
            class Item(mut int V)

            actor Keeper {
                pub string Take(List<Item> got) {
                    first = got[0]
                    first.V = 42
                    return "{got[9999].V} {got.Count}"
                }
            }

            one = Item(1)
            mut items = List<Item>()
            for i in 0..<10000 {
                items.Add(one)
            }

            k = spawn Keeper()
            print(k.Take(copy items))
            print(one.V)

            """;
        Ran(BuildAndRun(shared, toolchain.Runtime, "actor-copy-shared"), 0, ["42 10000", "1"], "");
    }

    [Fact]
    public void A_weak_parent_link_points_at_the_copy_of_its_target()
    {
        // U12 (the owner, 2026-11-15, #519, comment 6896192351, Q3, "Points at the copy"): a tree's weak parent link arrives pointing at the
        // copy of the root that the copy holds, the one instance the receiver got, and a child sent alone arrives with a null parent
        const string tree = """
            class Tree(string Name, List<Tree> Children, mut weak Tree? Parent)

            actor Keeper {
                pub string Take(Tree top) {
                    p = top.Children[0].Parent
                    if p != null {
                        return "{p.Name} {p == top}"
                    }

                    return "none"
                }

                pub bool Orphan(Tree t) => t.Parent == null
            }

            kid = Tree("kid", List<Tree>(), null)
            root = Tree("root", [kid], null)
            kid.Parent = root
            k = spawn Keeper()
            print(k.Take(copy root))
            print(k.Orphan(copy kid))
            print(kid.Parent == root)

            """;
        Ran(BuildAndRun(tree, toolchain.Runtime, "actor-copy-weak"), 0, ["root true", "true", "true"], "");
    }

    [Fact]
    public void A_weak_reference_an_argument_holds_itself_points_at_its_copy_at_a_restart_and_in_a_message_a_crash_copies()
    {
        // U12 with U21 and U22 (Q3, "Points at the copy"): the arguments of a `spawn`, kept in its supervisor's heap and copied into the
        // heap of each restart, and of a message queued behind a crash, which the crash copies into that heap, are read after the last
        // copy of their session or copied into the place the actor reads, so that a `weak Box?` and a `data` value's `weak` field, which
        // the argument holds in its own bytes, point at the copy of the instance another argument holds, under the fenced allocator
        const string text = """
            class Box(mut int N)
            data Link(weak Box? To)

            actor Shelf(weak Box? W, Link L, Box B) {
                pub void Fail() {
                    throw "shelf fails"
                }

                pub string Own() => Show(W, L, B)

                pub string Look(weak Box? w, Link l, Box b) => Show(w, l, b)

                string Show(weak Box? w, Link l, Box b) {
                    b.N = b.N + 1
                    v = w
                    t = l.To
                    if v != null && t != null {
                        return "{v.N} {t.N} {v == b} {t == b}"
                    }

                    return "gone"
                }
            }

            box = Box(1)
            s = spawn Shelf(copy box, copy Link(box), copy box) on crash restart after 1ms
            print(s.Own())
            send s.Fail()
            print(s.Look(copy box, copy Link(box), copy box))
            print(s.Own())
            print(box.N)

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-weak-arguments");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Shelf#1 crashed: thrown \"shelf fails\" at program.kz:6"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["2 2 true true", "2 2 true true", "2 2 true true", "1"], "");
    }

    [Fact]
    public void What_crosses_is_let_go_of_once_its_message_ran()
    {
        // U12: 5,000 copies of a list of 100 instances, each with a string and a weak link to the first, and the immutable instance each
        // message gives back, under a system that gives 16 pieces of memory: 28 MB of copies kept is more than it gives; every weak link
        // reaches the copy of the first instance (Q3)
        const string rounds = """
            class Node(string Name, mut int Seen, mut weak Node? Parent)
            class Label(string Text)

            actor Keeper {
                mut long total = 0

                pub Label Take(List<Node> nodes) {
                    for i in 0..<nodes.Count {
                        n = nodes[i]
                        n.Seen = n.Seen + 1
                        p = n.Parent
                        if p != null {
                            total = total + p.Seen
                        }
                    }

                    return Label("{total}")
                }
            }

            k = spawn Keeper()
            mut last = ""
            for r in 0..<5000 {
                root = Node("root", 0, null)
                mut nodes = [root]
                for i in 0..<99 {
                    nodes.Add(Node("node {i}", 0, root))
                }

                last = k.Take(copy nodes).Text
            }

            print(last)

            """;
        Ran(BuildAndRun(rounds, ListTests.Budget(), "actor-copies-freed"), 0, ["495000"], "");
    }

    /// <summary>
    /// std/ whose allocator counts the blocks it gives and takes back, read by the program through <c>HttpServer.Allocated()</c> and
    /// <c>HttpServer.Live()</c>, a class of std/ the program sees (W1).
    /// </summary>
    internal static readonly Lazy<CheckedRuntime> Blocks = new(() => ListTests.Edited(
        ("std/std.kz", "pub byte* Allocate(long size) {\n    mut byte* p = null\n", """
            thread static mut long allocated = 0
            thread static mut long freed = 0

            pub byte* Allocate(long size) {
                raw {
                    allocated = allocated + 1
                }

                mut byte* p = null

            """),
        ("std/std.kz", "pub void Free(byte* p, long size) {\n", """
            pub void Free(byte* p, long size) {
                raw {
                    freed = freed + 1
                }


            """),
        ("std/std.kz", "pub class HttpServer {\n", """
            pub class HttpServer {
                pub static long Allocated() {
                    mut long n = 0
                    raw {
                        n = allocated
                    }

                    return n
                }

                pub static long Live() {
                    mut long n = 0
                    raw {
                        n = allocated - freed
                    }

                    return n
                }

            """)));

    [Fact]
    public void A_moved_instance_is_copied_into_the_receivers_heap_which_keeps_and_changes_it()
    {
        // U12: `move` hands the instance to the actor, as its copy in the actor's heap, which the actor keeps in its state past the message
        // and changes there, under the allocator that makes every freed block inaccessible, after the sender's place of it was let go of
        // (section 6, the copy and the release at a message assumed on comment 6902210646, Q5) through the test for the empty address (the
        // point of comment 6897444564 that the answers left standing)
        const string program = """
            class Cart(mut int Items) {
                pub void Add(int n) {
                    Items = Items + n
                }
            }

            actor Shop {
                mut Cart kept = Cart(0)

                pub void Keep(Cart c) {
                    c.Add(1)
                    kept = c
                }

                pub int Bump() {
                    kept.Add(10)
                    return kept.Items
                }
            }

            void Give(Shop s) {
                cart = Cart(1)
                s.Keep(move cart)
            }

            shop = spawn Shop()
            Give(shop)
            print(shop.Bump())
            print(shop.Bump())

            """;
        Ran(BuildAndRun(program, ListTests.Fenced, "actor-move-kept"), 0, ["12", "22"], "");
    }

    [Fact]
    public void A_move_into_an_actor_copies_into_its_heap_and_frees_the_original()
    {
        // U12: a message with `move` takes the blocks of its copy in the actor's heap beside its own and gives back the moved instance
        // and its list, and one with `copy` takes the copy's blocks and gives back none, read through the counts of the allocator
        // (section 6: a `move` into a parameter copies nothing, comment 6895076937, and one into a message copies and frees the original,
        // assumed on comment 6902210646, Q5); a copy sent first makes the runtime's work list of copies, which the thread keeps
        const string program = """
            class Cart(mut int Items, List<int> Marks)

            actor Shop {
                pub void Add(Cart c) {
                    c.Items = c.Items + 1
                }

                pub void Number(int n) {
                    print(n)
                }

                pub int Ping() => 1
            }

            shop = spawn Shop()
            send shop.Add(copy Cart(0, [0]))
            shop.Ping()
            cart = Cart(1, [1, 2, 3])
            other = Cart(2, [4])
            start = HttpServer.Allocated()
            live = HttpServer.Live()
            send shop.Number(1)
            numbered = HttpServer.Allocated()
            numberedLive = HttpServer.Live()
            send shop.Add(move cart)
            moved = HttpServer.Allocated()
            movedLive = HttpServer.Live()
            send shop.Add(copy other)
            copied = HttpServer.Allocated()
            copiedLive = HttpServer.Live()
            shop.Ping()
            print("number {numbered - start} {numberedLive - live}, move {moved - numbered} {movedLive - numberedLive}, copy {copied - moved} {copiedLive - movedLive}")

            """;
        Ran(BuildAndRun(program, Blocks.Value, "actor-move-allocates"), 0, ["1", "number 1 1, move 3 1, copy 3 3"], "");
    }

    [Fact]
    public void Every_block_a_move_hands_on_is_let_go_of_once()
    {
        // U12: as many blocks taken back as given, across a second run of each, after a move on one branch, in a loop that takes its list
        // (comment 6897778812, Q1 (b)), in a loop that assigns the moved variable again, and through a parameter written `move`, kept or
        // moved on by the function, an actor's method among them, whose message lets go of no such argument; the places a `move` may have
        // emptied are let go of through the test for the empty address
        const string program = """
            class Cart(mut int Items, List<int> Marks)

            actor Shop {
                pub void Add(Cart c) {
                    c.Items = c.Items + 1
                }

                pub void Own(move Cart c) {
                    c.Items = c.Items + 1
                }

                pub int Ping() => 1
            }

            void Branch(Shop s, bool give) {
                cart = Cart(1, [1])
                if give {
                    send s.Add(move cart)
                }
            }

            void Loop(Shop s) {
                carts = [Cart(1, [1]), Cart(2, [2]), Cart(3, [3])]
                for c in carts {
                    if c.Items != 2 {
                        send s.Add(move c)
                    }
                }
            }

            void Again(Shop s) {
                mut cart = Cart(0, [0])
                for i in 0..<3 {
                    send s.Add(move cart)
                    cart = Cart(i, [i])
                }
            }

            void Keep(move Cart c) {
                c.Items = c.Items + 1
            }

            void Pass(Shop s, move Cart c) {
                send s.Add(move c)
            }

            void Through(Shop s) {
                one = Cart(1, [1])
                Keep(move one)
                two = Cart(2, [2])
                Pass(s, move two)
                Keep(move Cart(3, [3]))
                three = Cart(4, [4])
                send s.Own(move three)
            }

            void All(Shop s) {
                Branch(s, true)
                Branch(s, false)
                Loop(s)
                Again(s)
                Through(s)
                s.Ping()
            }

            shop = spawn Shop()
            All(shop)
            first = HttpServer.Live()
            All(shop)
            print(HttpServer.Live() - first)

            """;
        Ran(BuildAndRun(program, Blocks.Value, "actor-move-freed"), 0, ["0"], "");
    }

    [Fact]
    public void A_copy_a_call_left_before_its_last_copy_had_not_read_is_let_go_of()
    {
        // U12: a call of two copies, by its name and through a function value, left by the case a later argument propagates, after the
        // first copy was made in its slot and before the call read it, lets go of that copy as of the temporary of the region it would
        // have been; as many blocks taken back as given across a second run (copy-left-early.kz)
        const string program = """
            data Missing

            class Box(mut int N)
            class Holder(mut Box B)

            Box | Missing Lose(Holder held) {
                held.B = Box(2)
                return Missing
            }

            string Pair(Box a, Box b) => "{a.N} {b.N}"

            string | Missing Plain(Holder held) {
                return Pair(copy held.B, copy Lose(held))
            }

            string | Missing Valued(Holder held) {
                f = (Box a, Box b) => Pair(a, b)
                return f(copy held.B, copy Lose(held))
            }

            int Missed(string | Missing r) {
                match r {
                    string s => return 0
                    Missing => return 1
                }
            }

            int All() {
                h = Holder(Box(1))
                return Missed(Plain(h)) + Missed(Valued(h))
            }

            mut missed = All()
            first = HttpServer.Live()
            missed = missed + All()
            grown = HttpServer.Live() - first
            print(missed)
            print(grown)

            """;
        Ran(BuildAndRun(program, Blocks.Value, "actor-copy-left-early"), 0, ["4", "0"], "");
    }

    [Fact]
    public void A_construction_an_argument_leaves_lets_go_of_what_the_slots_before_it_took()
    {
        // a construction, of a data value and of an instance, and a call of a parameter written `move`, left by the case an argument
        // propagates out of the function, let go of every value a slot took from an argument before it, a copy, a count of a shared block
        // and a new value, as Rust drops the operands it had evaluated (issue 569); a plain call and a function value's, whose
        // parameters borrow, and slots of an `int` and of a list without items, which hold no block, the controls; the blocks live
        // across a second call of each, after one that warmed it up
        const string program = """
            data Missing

            class Box(mut int N)
            class Holder(mut Box B)
            data Pair(Box A, Box B)
            data Single(Box A)
            data Triple(Box A, Box B, Box C)
            class Kept(Box A, Box B)
            data Mixed(int N, List<int> Xs, Box B)

            Box | Missing Lose(Holder held) {
                held.B = Box(2)
                return Missing
            }

            Pair | Missing Copied(Holder held) {
                return Pair(copy held.B, copy Lose(held))
            }

            Pair | Missing Shared(Holder held) {
                return Pair(held.B, copy Lose(held))
            }

            Single | Missing Alone(Holder held) {
                return Single(copy Lose(held))
            }

            Triple | Missing Two(Holder held) {
                return Triple(Box(5), Box(6), copy Lose(held))
            }

            Kept | Missing Made(Holder held) {
                return Kept(Box(5), copy Lose(held))
            }

            string Keep(move Box a, Box b) => "{a.N} {b.N}"

            string | Missing MovedCopy(Holder held) {
                return Keep(copy held.B, copy Lose(held))
            }

            string | Missing MovedNew(Holder held) {
                return Keep(move Box(5), copy Lose(held))
            }

            string | Missing MovedOne(Holder held) {
                return Keep(copy held.B, Lose(held))
            }

            string Both(Box a, Box b) => "{a.N} {b.N}"

            string | Missing Called(Holder held) {
                return Both(Box(5), copy Lose(held))
            }

            string | Missing CalledShared(Holder held) {
                return Both(held.B, copy Lose(held))
            }

            string | Missing Valued(Holder held) {
                f = (Box a, Box b) => "{a.N} {b.N}"
                return f(copy held.B, copy Lose(held))
            }

            Mixed | Missing Uncounted(Holder held) {
                return Mixed(5, List<int>(), copy Lose(held))
            }

            Box | Missing Found(Holder held) {
                return Box(3)
            }

            Pair | Missing Built(Holder held) {
                return Pair(copy held.B, copy Found(held))
            }

            Triple | Missing BuiltNew(Holder held) {
                return Triple(Box(5), held.B, copy Found(held))
            }

            int MissedPair(Pair | Missing r) {
                match r {
                    Pair p => return p.A.N + p.B.N - p.A.N - p.B.N
                    Missing => return 1
                }
            }

            int MissedSingle(Single | Missing r) {
                match r {
                    Single s => return 0
                    Missing => return 1
                }
            }

            int MissedTriple(Triple | Missing r) {
                match r {
                    Triple t => return t.A.N + t.B.N + t.C.N - t.A.N - t.B.N - t.C.N
                    Missing => return 1
                }
            }

            int MissedKept(Kept | Missing r) {
                match r {
                    Kept k => return 0
                    Missing => return 1
                }
            }

            int MissedText(string | Missing r) {
                match r {
                    string s => return 0
                    Missing => return 1
                }
            }

            int MissedMixed(Mixed | Missing r) {
                match r {
                    Mixed m => return 0
                    Missing => return 1
                }
            }

            int Run(int row, Holder held) {
                if row == 0 {
                    return MissedPair(Copied(held))
                }

                if row == 1 {
                    return MissedPair(Shared(held))
                }

                if row == 2 {
                    return MissedSingle(Alone(held))
                }

                if row == 3 {
                    return MissedTriple(Two(held))
                }

                if row == 4 {
                    return MissedKept(Made(held))
                }

                if row == 5 {
                    return MissedText(MovedCopy(held))
                }

                if row == 6 {
                    return MissedText(MovedNew(held))
                }

                if row == 7 {
                    return MissedText(MovedOne(held))
                }

                if row == 8 {
                    return MissedText(Called(held))
                }

                if row == 9 {
                    return MissedText(CalledShared(held))
                }

                if row == 10 {
                    return MissedText(Valued(held))
                }

                if row == 11 {
                    return MissedMixed(Uncounted(held))
                }

                if row == 12 {
                    return MissedPair(Built(held))
                }

                return MissedTriple(BuiltNew(held))
            }

            names = ["Copied", "Shared", "Alone", "Two", "Made", "MovedCopy", "MovedNew", "MovedOne", "Called", "CalledShared", "Valued", "Uncounted", "Built", "BuiltNew"]
            h = Holder(Box(1))
            mut missed = 0
            for row in 0..<14 {
                missed = missed + Run(row, h)
                before = HttpServer.Live()
                missed = missed + Run(row, h)
                n = HttpServer.Live() - before
                print("{names[row]} {n}")
            }

            print(missed)

            """;
        Ran(
            BuildAndRun(program, Blocks.Value, "construction-left-early"),
            0,
            ["Copied 0", "Shared 0", "Alone 0", "Two 0", "Made 0", "MovedCopy 0", "MovedNew 0", "MovedOne 0", "Called 0", "CalledShared 0", "Valued 0", "Uncounted 0", "Built 0", "BuiltNew 0", "24"],
            "");
    }

    [Fact]
    public void A_function_value_whose_type_writes_move_takes_the_argument_there_and_every_block_is_let_go_of_once()
    {
        // U12, F11 (issue 587): each row of `ListTests.ValueMoves`, a call of a function value whose type writes `move` at a parameter,
        // hands that argument over to the lambda, which lets go of it at its end, as a direct call does (`Arguments`), and a call left by
        // the case an argument after it propagates lets go of it there; the blocks live across a second call of each row, after one that
        // warmed it up, and the sum of every row's value twice
        const string main = """
            names = ["Through", "Variable", "Item", "Inline", "Copied", "Copies", "Forwarded", "Left", "LeftVariable", "LeftCopy", "Borrowed"]
            h = Holder(Box(5))
            o = Box(7)
            mut sum = 0
            for row in 0..<11 {
                sum = sum + Run(row, h, o)
                before = HttpServer.Live()
                sum = sum + Run(row, h, o)
                n = HttpServer.Live() - before
                print("{names[row]} {n}")
            }

            print(sum)

            """;
        Ran(
            BuildAndRun(ListTests.ValueMoves + main, Blocks.Value, "value-move-freed"),
            0,
            ["Through 0", "Variable 0", "Item 0", "Inline 0", "Copied 0", "Copies 0", "Forwarded 0", "Left 0", "LeftVariable 0", "LeftCopy 0", "Borrowed 0", "816"],
            "");
    }

    /// <summary>
    /// std/ whose <c>Free</c> notes the sizes of the first and the last two blocks it was given and the number of blocks while the
    /// program records, which <c>HttpServer.Record()</c> starts and <c>HttpServer.Stop()</c> ends, read through
    /// <c>HttpServer.LastFreedSmaller()</c>, whether the last block was smaller than the one before it, <c>HttpServer.FirstFreed()</c>,
    /// the size of the first, and <c>HttpServer.FreedCount()</c>, a class of std/ the program sees (W1).
    /// </summary>
    private static readonly Lazy<CheckedRuntime> FreeOrder = new(() => ListTests.Edited(
        ("std/std.kz", "pub void Free(byte* p, long size) {\n", """
            thread static mut long recording = 0
            thread static mut long freedBefore = 0
            thread static mut long freedLast = 0
            thread static mut long freedCount = 0
            thread static mut long freedFirst = 0

            pub void Free(byte* p, long size) {
                raw {
                    if recording == 1 {
                        if freedCount == 0 {
                            freedFirst = size
                        }

                        freedBefore = freedLast
                        freedLast = size
                        freedCount = freedCount + 1
                    }
                }


            """),
        ("std/std.kz", "pub class HttpServer {\n", """
            pub class HttpServer {
                pub static void Record() {
                    raw {
                        recording = 1
                        freedCount = 0
                        freedFirst = 0
                    }
                }

                pub static void Stop() {
                    raw {
                        recording = 0
                    }
                }

                pub static bool LastFreedSmaller() {
                    mut bool smaller = false
                    raw {
                        smaller = freedLast < freedBefore
                    }

                    return smaller
                }

                pub static long FirstFreed() {
                    mut long n = 0
                    raw {
                        n = freedFirst
                    }

                    return n
                }

                pub static long FreedCount() {
                    mut long n = 0
                    raw {
                        n = freedCount
                    }

                    return n
                }

            """)));

    [Fact]
    public void A_construction_an_argument_leaves_lets_go_of_what_the_slots_before_it_took_the_last_first()
    {
        // what the slots of a construction took before the argument that leaves it is let go of in the reverse of the order the
        // arguments were evaluated, the copy of a call of two copies among them, and so are the arguments of a plain call and of a
        // function value's, as R1 states for a jump out of a construction or a call (assumed: a reading of the build), as Rust drops
        // the operands of an expression that one of its operands leaves (the Rust Reference, "Destructors", drop scopes, operands;
        // issue 569): the instance of the larger class, made second, is freed before the smaller one, made first, a copy where the
        // call makes two, and in the rows of a call of two copies no other block is freed while the program records but the tables
        // of the session of those two copies,
        // which its end frees first (`CopyAbort`), before the instance made after the first copy, the tables larger than the instances
        // of `Small` (24 bytes) and of `Large` (32); a call's one copy is let go of among the values its arguments made, the last made
        // first, so that the copy, made second, goes before the instance made first, and the instance, made second, before the copy
        // made first, after the tables of the copy's own session, which ends when the copy is made (R1)
        const string program = """
            data Missing

            class Small(int N)
            class Large(int A, int B, int C, int D)
            class Holder(Small S)
            data Two(Small A, Large B, Small C)

            Small | Missing Lose() {
                return Missing
            }

            Two | Missing Made() {
                return Two(Small(1), Large(1, 2, 3, 4), Lose())
            }

            string One(Large b, Small s, Small c) => "{b.A} {s.N} {c.N}"

            string | Missing Single(Holder h) {
                return One(Large(1, 2, 3, 4), copy h.S, Lose())
            }

            string OneFirst(Small s, Large b, Small c) => "{s.N} {b.A} {c.N}"

            string | Missing SingleFirst(Holder h) {
                return OneFirst(copy h.S, Large(1, 2, 3, 4), Lose())
            }

            Two | Missing Copied(Holder h) {
                return Two(copy h.S, Large(1, 2, 3, 4), copy Lose())
            }

            string Both(Small a, Large b, Small c) => "{a.N} {b.A} {c.N}"

            string | Missing Called(Holder h) {
                return Both(copy h.S, Large(1, 2, 3, 4), copy Lose())
            }

            string | Missing Valued(Holder h) {
                g = (Small a, Large b, Small c) => "{a.N} {b.A} {c.N}"
                return g(copy h.S, Large(1, 2, 3, 4), copy Lose())
            }

            int MissedTwo(Two | Missing r) {
                match r {
                    Two t => return 0
                    Missing => return 1
                }
            }

            int MissedText(string | Missing r) {
                match r {
                    string s => return 0
                    Missing => return 1
                }
            }

            int Order(int row, Holder h) {
                mut missed = 0
                HttpServer.Record()
                if row == 0 {
                    missed = MissedTwo(Made())
                } else if row == 1 {
                    missed = MissedTwo(Copied(h))
                } else if row == 2 {
                    missed = MissedText(Called(h))
                } else if row == 3 {
                    missed = MissedText(Valued(h))
                } else if row == 4 {
                    missed = MissedText(Single(h))
                } else {
                    missed = MissedText(SingleFirst(h))
                }

                HttpServer.Stop()
                return missed
            }

            names = ["Made", "Copied", "Called", "Valued", "Single", "SingleFirst"]
            holder = Holder(Small(7))
            for row in 0..<6 {
                missed = Order(row, holder)
                print("{names[row]} {HttpServer.LastFreedSmaller()} {HttpServer.FreedCount()} {missed} {HttpServer.FirstFreed() > 32}")
            }

            """;
        Ran(BuildAndRun(program, FreeOrder.Value, "construction-left-early-order"), 0, ["Made true 2 1 false", "Copied true 3 1 true", "Called true 3 1 true", "Valued true 3 1 true", "Single false 3 1 true", "SingleFirst true 3 1 true"], "");
    }

    /// <summary>
    /// std/ whose allocator counts the blocks each heap holds, in the word of the heap's record no field takes, one more at an allocation
    /// in the heap the entry serves and one less at the free of a block of the heap, read by the program through
    /// <c>HttpServer.HeapLive()</c>, the count of the heap its code allocates in (design record, section 3).
    /// </summary>
    private static readonly Lazy<CheckedRuntime> HeapBlocks = new(() => HeapBlocksFor(Builder.HostTarget!.Value));

    /// <summary>As <see cref="HeapBlocks"/>, the runtime and the standard library of <paramref name="target"/>.</summary>
    private static CheckedRuntime HeapBlocksFor(Target target) => ListTests.EditedFor(target, [
        ("std/std.kz", "pub byte* Allocate(long size) {\n    mut byte* p = null\n", """
            pub byte* Allocate(long size) {
                mut byte* p = null
                raw {
                    byte* counted = HeapCurrent()
                    if counted != null {
                        *Ptr<long>(counted + 40) = *Ptr<long>(counted + 40) + 1
                    }
                }

            """),
        ("std/std.kz", "pub void Free(byte* p, long size) {\n", """
            pub void Free(byte* p, long size) {
                raw {
                    byte* counted = Owner(p, size)
                    *Ptr<long>(counted + 40) = *Ptr<long>(counted + 40) - 1
                }


            """),
        ("std/std.kz", "pub class HttpServer {\n", """
            pub class HttpServer {
                pub static long HeapLive() {
                    mut long n = 0
                    raw {
                        n = *Ptr<long>(HeapCurrent() + 40)
                    }

                    return n
                }

            """)]);

    [Fact]
    public void Each_heap_gives_back_every_block_of_its_crossings_results_and_moves()
    {
        // section 3, U12 (the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big unbuilt"): a round of messages whose
        // arguments are an instance with a list, strings, a list and a map, whose results are a string, a data value with a list of
        // strings and an instance, a `move` of an instance with its list (section 6, assumed on that Q5) and a lambda that captures a string,
        // each a copy in the heap it crosses into; across a third round the top-level code's heap and the actor's hold as many blocks as
        // before it
        const string program = """
            class Item(string Name, List<int> Xs)
            data Pair(string A, List<string> B)

            actor Shop {
                mut Item kept = Item("none", [])
                mut List<string> names = []

                pub string Take(Item it, string s, List<int> xs, Map<string, int> m) {
                    kept = it
                    names = [s, it.Name]
                    return "{it.Name} {s} {xs.Count} {m.Count}"
                }

                pub Pair Give(int n) {
                    mut bs = List<string>()
                    for i in 0..<n {
                        bs.Add("b{i}")
                    }

                    return Pair("a{n}", bs)
                }

                pub Item Kept() => kept

                pub void Own(move Item it) {
                    kept = it
                }

                pub void Run(() => int f) {
                    names = ["{f()}"]
                }

                pub long Live() => HttpServer.HeapLive()
            }

            long Round(Shop s, int r) {
                word = "w{r}"
                mut table = Map<string, int>()
                table[word] = r
                t = s.Take(Item("it {word}", [r, 2]), "s {word}", [1, 2, 3], table)
                p = s.Give(3)
                k = s.Kept()
                it = Item("moved {word}", [r])
                send s.Own(move it)
                send s.Run(() => word.Bytes.Count + r)
                return long(t.Bytes.Count + p.B.Count + k.Xs.Count) + s.Live()
            }

            shop = spawn Shop()
            Round(shop, 0)
            Round(shop, 1)
            own = HttpServer.HeapLive()
            held = shop.Live()
            Round(shop, 2)
            ownLeft = HttpServer.HeapLive() - own
            heldLeft = shop.Live() - held
            print("{ownLeft} {heldLeft}")

            """;
        Ran(BuildAndRun(program, HeapBlocks.Value, "actor-heaps-freed"), 0, ["0 0"], "");
    }

    [Fact]
    public void A_million_idle_actors_take_at_most_512_bytes_each_and_5_ticks_of_the_cpu_in_a_second()
    {
        // section 6, "An idle actor costs no CPU and a few hundred bytes. Millions run on a handful of OS threads", and section 13, "an
        // idle actor at most 512 bytes" (the owner, 2026-11-15, #519, comment 6902210646, Q6, "Small growing pages"): 1,000,000 actors
        // spawned and never sent a message, each a heap whose first page of 256 bytes holds its record and its state, measured by the
        // growth of the resident memory, the line `VmRSS` of `/proc/self/status`, over their spawns; then the time on the CPU of every
        // thread of the process, user and system, fields 14 and 15 of `/proc/self/stat` in ticks of 10 ms, over a sleep of one second
        // of the top-level code that starts after the spawns, at most 5 ticks, where a worker thread that spins while nothing is left
        // to run takes about 100; the last actor answers its call after the sleep
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows, nor `/proc/self/stat` the time on the CPU
        }

        const string program = """
            long | IoError Resident() {
                bytes = File.ReadAll("/proc/self/status")
                text = string.FromUtf8(bytes) else {
                    InvalidUtf8 => ""
                }

                for line in text.Split('\n') {
                    if line.StartsWith("VmRSS:") {
                        return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                            ParseError => -1
                        }
                    }
                }

                return -1
            }

            long | IoError Ticks() {
                bytes = File.ReadAll("/proc/self/stat")
                text = string.FromUtf8(bytes) else {
                    InvalidUtf8 => ""
                }

                fields = text.Split(' ')
                user = long.Parse(fields[13]) else {
                    ParseError => -1
                }
                system = long.Parse(fields[14]) else {
                    ParseError => -1
                }
                return user + system
            }

            actor Idle(int N) {
                pub int Get() => N
            }

            mut last = spawn Idle(0)
            Resident() else {
                IoError e => -1
            }

            before = Resident() else {
                IoError e => -1
            }

            for i in 0..<1000000 {
                last = spawn Idle(i)
            }

            after = Resident() else {
                IoError e => -1
            }

            print(last)
            print((after - before) * 1024 / 1000000)

            idle = Ticks() else {
                IoError e => -1
            }
            Environment.Sleep(1s)
            woke = Ticks() else {
                IoError e => -1
            }

            print(woke - idle)
            print(idle >= 0)
            print(last.Get())

            """;
        var run = BuildAndRun(program, toolchain.Runtime, "actor-idle-million");
        Assert.Equal((0, "", "Idle#1000001"), (run.Code, run.Error, run.Output[0]));
        var each = long.Parse(run.Output[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(each is > 0 and <= 512, $"an idle actor takes {each} bytes");
        var ticks = long.Parse(run.Output[2], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(ticks is >= 0 and <= 5, $"a million idle actors took {ticks} ticks of 10 ms on the CPU in a second");
        Assert.Equal(["true", "999999"], run.Output.Skip(3));
    }

    [Fact]
    public void A_runtime_buffer_made_inside_an_actor_outlives_it()
    {
        // section 3: the first line printed, by an actor's message, makes the buffer `Print` keeps for the thread, in the thread's own heap,
        // so that it stays under the allocator that makes every freed block inaccessible once every actor's heap was freed whole
        // (ListTests.EndingActors), where the top-level code prints into it again
        const string program = """
            actor Talker {
                pub void Say(string s) {
                    print(s)
                }
            }

            talker = spawn Talker()
            talker.Say("first")
            eprint("end actors")
            print("after")

            """;
        Ran(BuildAndRun(program, ListTests.Edited([.. ListTests.Fence, ListTests.EndingActors]), "actor-buffer-outlives"), 0, ["first", "after"], "");
    }

    /// <summary>std/ whose <c>HttpServer.Kept()</c> reads the bytes of the blocks of more than 262144 bytes the thread keeps.</summary>
    private static (string Path, string Old, string New) KeptBytes => ("std/std.kz", "pub class HttpServer {\n", """
        pub class HttpServer {
            pub static long Kept() {
                mut long n = 0
                raw {
                    n = keptBytes
                }

                return n
            }

        """);

    [Fact]
    public void A_block_of_more_than_262144_bytes_of_an_actor_joins_the_kept_blocks_at_its_end()
    {
        // section 3 (2026-11-13): the list of 70000 items an actor keeps, in a block of 524304 bytes, 524352 with its header, in its
        // heap's list of such blocks, joins the blocks the thread keeps when the actor's heap is freed whole (ListTests.EndingActors)
        const string program = """
            actor Holder {
                mut List<int> xs = []

                pub int Fill(int n) {
                    for i in 0..<n {
                        xs.Add(i)
                    }

                    return xs.Count
                }
            }

            holder = spawn Holder()
            print(holder.Fill(70000))
            before = HttpServer.Kept()
            eprint("end actors")
            print(HttpServer.Kept() - before)

            """;
        Ran(BuildAndRun(program, ListTests.Edited(KeptBytes, ListTests.EndingActors), "actor-heap-kept"), 0, ["70000", "524352"], "");
    }

    [Fact]
    public void A_lambda_that_crosses_into_an_actor_takes_a_copy_of_its_environment_in_the_actors_heap()
    {
        // U12 (the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big unbuilt"): a lambda that captures a string crosses
        // as a new environment in the actor's heap, the string copied into it, which the actor keeps in its state, so that the heap of
        // the top-level code holds as many blocks as before the send once its lambda was let go of, and the actor's two more
        const string program = """
            actor Keeper {
                mut () => int kept = () => 0

                pub void Keep(() => int f) {
                    kept = f
                }

                pub int Run() {
                    f = kept
                    return f()
                }

                pub long Live() => HttpServer.HeapLive()
            }

            void Give(Keeper keeper, string text) {
                send keeper.Keep(() => text.Bytes.Count)
            }

            k = spawn Keeper()
            word = "w {k.Run()}"
            own = HttpServer.HeapLive()
            held = k.Live()
            Give(k, word)
            ran = k.Run()
            ownLeft = HttpServer.HeapLive() - own
            heldLeft = k.Live() - held
            print("{ran} {ownLeft} {heldLeft}")

            """;
        Ran(BuildAndRun(program, HeapBlocks.Value, "actor-lambda-heap"), 0, ["3 0 2"], "");
    }

    [Fact]
    public void A_lambda_moved_into_an_actor_takes_a_copy_of_its_environment_in_the_actors_heap()
    {
        // U12: a `move` into a message copies the value into the receiver's heap as an argument with no word crosses (section 6, assumed on
        // comment 6902210646, Q5), a lambda the sending function shows with its environment, so that the heap of the top-level code holds as
        // many blocks as before the send once its lambda was let go of, and the actor's two more, as for a lambda sent with no word
        const string program = """
            actor Keeper {
                mut () => int kept = () => 0

                pub void Keep(() => int f) {
                    kept = f
                }

                pub int Run() {
                    f = kept
                    return f()
                }

                pub long Live() => HttpServer.HeapLive()
            }

            void Give(Keeper keeper, string text) {
                f = () => text.Bytes.Count
                send keeper.Keep(move f)
            }

            k = spawn Keeper()
            word = "w {k.Run()}"
            own = HttpServer.HeapLive()
            held = k.Live()
            Give(k, word)
            ran = k.Run()
            ownLeft = HttpServer.HeapLive() - own
            heldLeft = k.Live() - held
            print("{ran} {ownLeft} {heldLeft}")

            """;
        Ran(BuildAndRun(program, HeapBlocks.Value, "actor-lambda-moved-heap"), 0, ["3 0 2"], "");
    }

    [Fact]
    public void A_loop_that_moves_a_number_or_a_string_into_an_actor_takes_its_list_where_the_items_place_empties()
    {
        // U12: a loop whose body moves its variable takes its list, an item that crosses with no word among them (the owner, 2026-11-17,
        // #561, comment 6945857315, Q1); a string's place empties at its `move`, and a number's, which has no empty value, stays, with
        // nothing counted
        const string program = """
            actor Sum {
                mut int total = 0
                mut string text = ""

                pub void Add(int n) {
                    total = total + n
                }

                pub void Join(string t) {
                    text = "{text}{t}"
                }

                pub string Show() => "{total} {text}"
            }

            s = spawn Sum()
            nums = [1, 2, 3]
            for n in nums {
                send s.Add(move n)
            }

            names = ["a", "b"]
            for t in names {
                send s.Join(move t)
            }

            print(s.Show())

            """;
        Ran(BuildAndRun(program, toolchain.Runtime, "actor-loop-moves-number"), 0, ["6 ab"], "");
    }

    /// <summary>
    /// std/ that counts the heaps of the thread, one more at each heap made and one less at each heap freed whole, read by the program
    /// through <c>HttpServer.Heaps()</c> (design record, section 3).
    /// </summary>
    private static (string Path, string Old, string New)[] HeapCount =>
    [
        ("std/std.kz", "thread static mut long keptBytes = 0\n", "thread static mut long keptBytes = 0\nthread static mut long heapsLive = 0\n"),
        ("std/std.kz", "        *Ptr<long>(heap + 40) = 0\n", "        *Ptr<long>(heap + 40) = 0\n        heapsLive = heapsLive + 1\n"),
        ("std/std.kz", "pub void HeapFree(byte* heap) {\n    sys.Entered()\n    raw {\n", "pub void HeapFree(byte* heap) {\n    sys.Entered()\n    raw {\n        heapsLive = heapsLive - 1\n"),
        ("std/std.kz", "pub class HttpServer {\n", """
            pub class HttpServer {
                pub static long Heaps() {
                    mut long n = 0
                    raw {
                        n = heapsLive
                    }

                    return n
                }

            """),
    ];

    [Fact]
    public void A_spawn_left_by_a_case_of_a_later_argument_frees_the_heap_it_made()
    {
        // section 3, U12: the heap a `spawn` makes before its arguments, freed whole where a case propagates out of a later argument before
        // the actor is made (the runtime's `HeapLeft`), once the copy of the earlier argument in it was let go of; the heap of an actor that
        // was made stays
        const string program = """
            actor Holder(string Name, int N) {
                pub string Text() => "{Name} {N}"
            }

            int | ParseError Made(string s) {
                h = spawn Holder("holder {s}", int.Parse(s))
                return h.Text().Bytes.Count
            }

            print("spawns")
            start = HttpServer.Heaps()
            made = Made("7") else {
                ParseError => -1
            }

            kept = HttpServer.Heaps() - start
            for i in 0..<3 {
                n = Made("x{i}") else {
                    ParseError => -1
                }

                print(n)
            }

            print("{made} {kept} {HttpServer.Heaps() - start}")

            """;
        Ran(BuildAndRun(program, ListTests.Edited(HeapCount), "actor-heap-left"), 0, ["spawns", "-1", "-1", "-1", "10 1 1"], "");
    }

    [Fact]
    public void A_constructor_that_waits_inside_a_message_allocates_in_the_new_actors_heap_again()
    {
        // section 3, U3, U4: a default of an actor's body whose call waits, inside the message of another actor, parks that message, which
        // its actor's heap serves when it resumes; the runtime's `Wait` then serves the heap of the actor `spawn` makes again, so that the
        // string the default makes after the call is a block of that heap, as it is for the same actor spawned by the top-level code
        const string program = """
            actor Config {
                pub int Size() => 3
            }

            actor Pool(Config Settings) {
                string name = "pool {Settings.Size()}"

                pub string Name() => name

                pub long Live() => HttpServer.HeapLive()
            }

            actor Owner(Config Settings) {
                pub long Spawned() {
                    p = spawn Pool(Settings)
                    return p.Live()
                }
            }

            s = spawn Config()
            top = spawn Pool(s)
            print(top.Live())
            print(spawn Owner(s).Spawned())

            """;
        Ran(BuildAndRun(program, HeapBlocks.Value, "actor-wait-heap"), 0, ["2", "2"], "");
    }

    /// <summary>
    /// std/ whose <c>HttpServer.Fill(n, size)</c> takes <c>n</c> blocks of <c>size</c> bytes from the allocation entry and keeps them, and
    /// whose <c>HttpServer.PageCount()</c> and <c>HttpServer.PageSize(i)</c> read the pages of the heap the entry serves, its first page at 0.
    /// </summary>
    private static (string Path, string Old, string New) Pages => ("std/std.kz", "pub class HttpServer {\n", """
        pub class HttpServer {
            pub static void Fill(int n, long size) {
                mut int i = 0
                while i < n {
                    Allocate(size)
                    i = i + 1
                }
            }

            pub static int PageCount() {
                mut int n = 0
                raw {
                    mut byte* page = *Ptr<byte*>(HeapCurrent() + 16)
                    while page != null {
                        n = n + 1
                        page = *Ptr<byte*>(page)
                    }
                }

                return n
            }

            pub static long PageSize(int i) {
                mut long size = 0
                raw {
                    mut int skip = PageCount() - 1 - i
                    mut byte* page = *Ptr<byte*>(HeapCurrent() + 16)
                    while skip > 0 {
                        page = *Ptr<byte*>(page)
                        skip = skip - 1
                    }

                    size = *Ptr<long>(page + 8)
                }

                return size
            }

        """);

    [Fact]
    public void A_heaps_pages_double_from_its_first_up_to_65536_bytes()
    {
        // section 3 (the owner, 2026-11-15, #519, comment 6902210646, Q6, "Small growing pages"): the heap of an actor with no field, its
        // first page of 256 bytes, takes 10000 blocks of 16 bytes in pages each twice the size of the one before it, up to 65536 bytes,
        // and a page of 65536 bytes again after one of 65536
        const string program = """
            actor Grower {
                pub string Grow(int n) {
                    HttpServer.Fill(n, 16)
                    count = HttpServer.PageCount()
                    mut sizes = List<long>()
                    for i in 0..<count {
                        sizes.Add(HttpServer.PageSize(i))
                    }

                    return "{sizes}"
                }
            }

            g = spawn Grower()
            print(g.Grow(10000))

            """;
        Ran(BuildAndRun(program, ListTests.Edited(Pages), "actor-heap-pages"), 0, ["[256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 65536]"], "");
    }

    [Fact]
    public void Every_actors_heap_is_freed_whole_at_the_end_of_the_program()
    {
        // section 3, U13: the end of the top-level code ends every actor it spawned, each heap freed whole (ListTests.EndingActors, the
        // runtime's `EndActors`, which `Finish` calls)
        const string program = """
            actor Idle(int N) {
                pub int Get() => N
            }

            a = spawn Idle(1)
            b = spawn Idle(2)
            c = spawn Idle(3)
            print(a.Get() + b.Get() + c.Get())
            before = HttpServer.Heaps()
            eprint("end actors")
            print(before - HttpServer.Heaps())

            """;
        Ran(BuildAndRun(program, ListTests.Edited([.. HeapCount, ListTests.EndingActors]), "actor-heaps-ended"), 0, ["6", "3"], "");
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
    public void A_crash_at_the_root_ends_the_process_with_its_error_line_and_its_trace()
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
        // U19, U20: the method's crash is the actor's, which its log line names and which restarts it, and the wait of the top-level
        // code, the root actor, raises `callee-crashed` at its call, which ends the process with the error line and the trace of the
        // thread's own stack, where `main` waits, and none of the actor's
        var run = BuildAndRun(calc, toolchain.Runtime, "actor-crash", trace: null);
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Calc#1 crashed: divide-by-zero at program.kz:2"], crashes);
        var error = "error: callee-crashed at program.kz:7\n" + (OperatingSystem.IsLinux() ? "  at main in program.kz:7\n" : string.Empty);
        Ran((run.Code, run.Output, rest), 1, ["5"], error);
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

        // U19: the overflow is the actor's crash, its log line at the call's line, and the top-level code's wait raises `callee-crashed`
        var run = BuildAndRun(text + "print(d.Down(100000))\n", toolchain.Runtime, "actor-stack-overflow", trace: null);
        Assert.Equal(["100000", "20000"], run.Output);
        Assert.Equal(1, run.Code);
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Deep#1 crashed: stack-overflow at program.kz:15"], crashes);
        Assert.StartsWith("error: callee-crashed at program.kz:21\n", rest, StringComparison.Ordinal);
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
        // closed pages of its stack, which the stack's return to the pool gives back and closes again, so that on Linux the stack is two
        // mappings again, a closed range of 2 MiB less 64 KiB joined to its guard page, one mapping with no access, under 64 KiB open for
        // reading and writing; the same calls then run again on that stack, which on Windows grows through the guard page the trim set
        // again; the counts are Linux's halved on Windows, whose frames are twice as large (Deep)
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
        var page = Environment.SystemPageSize;
        Assert.Contains(maps, m => m.Access == "---p" && m.High - m.Low >= closed + page && m.High - m.Low <= closed + 65536 && maps.Any(o => o.Low == m.High && o.Access == "rw-p"));
        Assert.DoesNotContain(maps, m => m.Access == "---p" && m.High - m.Low == closed); // the closed range is no mapping of its own
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
        // section 6, "Deadlocks" (the owner, 2026-11-15, #519, comment 6892812780, Q4, "deadlock at top-level wait"), on one thread for
        // what layer 3 does not see (U64; the owner, 2026-11-20, #519, comment 7051071630, T70 1): `Boss`'s `Go` waits on `Get`, queued
        // behind `Fail` in `Worker`'s inbox, and `Fail`'s crash queues the block of `Worker`'s policy behind `Go`, so that a crash closes
        // the loop and no wait does; the top-level code's wait, in a function it called, finds nothing left to run: the process ends at
        // that call's line, not at a line of the loop, with the exit code 1, and the loop's waits below the error line, which one walk
        // from the call finds (U64; the owner, 2026-11-21, #519, comment 7060155980, T85 2)
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Get() => 5
            }

            actor Boss {
                pub void Go() {
                    print("boss goes")
                    w = spawn Worker() on crash (x) {
                        print("decided")
                        restart
                    }

                    send w.Fail()
                    print("got {w.Get()}")
                }
            }

            int Start(Boss b) {
                b.Go()
                return 1
            }

            boss = spawn Boss()
            print(Start(boss))

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadlock");
        Ran((run.Code, run.Output, RunTests.Chains(run.Error)), 1, ["boss goes"], "error: deadlock at program.kz:23:\n" +
            "Boss#1 waits at program.kz:18 on Worker#2 in chain <chain>\nWorker#2 waits for the decision of Boss#1 on its crash in chain <chain>\n");
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
        // guard page, after the stacks of the first `Save` went back to the pool, and the walk finds the line in `Load`'s frames, as it
        // does for `Load` reached as a message of its own; the store's crash answers the top-level code's wait, which raises
        // `callee-crashed` before the checker runs again (U19, U20)
        var callback = BuildAndRun(Overflowing("print(s.Save(100000))"), toolchain.Runtime, "actor-callback-overflow", trace: null);
        var message = BuildAndRun(Overflowing("print(s.Load(100000))"), toolchain.Runtime, "actor-message-overflow", trace: null);
        foreach (var run in new[] { callback, message })
        {
            Assert.Equal((1, "1000"), (run.Code, string.Join('\n', run.Output)));
            Assert.Equal(["Store#2 crashed: stack-overflow at program.kz:11"], Logged(run.Error).Crashes);
            Assert.StartsWith("error: callee-crashed at program.kz:22\n", Logged(run.Error).Others, StringComparison.Ordinal);
        }
    }

    // ---- crashes (U19 to U24; the answers of 2026-11-15, issue 519, comment 6902210646) -------------------------------------------

    /// <summary>
    /// U24: the text after the category of each log line of an actor's crash on standard error, <c>&lt;Type&gt;#&lt;n&gt; crashed: ...</c>,
    /// each line in P7's layout, and the rest of standard error.
    /// </summary>
    internal static (List<string> Crashes, string Others) Logged(string error)
    {
        const string category = "Error Kurz.Actor: ";
        var crashes = new List<string>();
        var rest = new StringBuilder();
        foreach (var line in Toolchain.Lines(error))
        {
            if (line.Length > 25 && line[4] == '-' && line[10] == 'T' && line[23] == 'Z' && line[24] == ' ' && line[25..].StartsWith(category, StringComparison.Ordinal))
            {
                crashes.Add(line[(25 + category.Length)..]);
            }
            else
            {
                rest.Append(line).Append('\n');
            }
        }

        return (crashes, rest.ToString());
    }

    /// <summary>
    /// U23: the runtime whose clock of the pauses is a count of its own, 0 at the start, which its sleep moves on by the time asked, with
    /// that time written on standard output as <c>pause &lt;ms&gt;</c> where <paramref name="print"/> holds, and which
    /// <c>eprint("advance")</c> moves on by 31 s, a full cap and a second, so that no pause takes time. Its calls have no default bound,
    /// since the deadlines read that clock too (U36), which the pauses move on by minutes in no time.
    /// </summary>
    private static CheckedRuntime StoodIn(bool print) => ListTests.Edited(
        ("runtime/common/runtime.kz", "own = Due(now, sys.DefaultBound())", "own = Never()"),
        ("runtime/common/runtime.kz", "    mut long now = 0\n    if !sys.MonotonicNow(mut now) {\n        return 0\n    }\n\n    raw {\n        return now + ahead\n    }\n",
            "    mut long now = 0\n    raw {\n        now = standIn\n    }\n\n    return now\n"),
        ("runtime/common/runtime.kz", "void PauseSleep(long nanoseconds) {\n    sys.Sleep(nanoseconds)\n}",
            "static mut long standIn = 0\n\nvoid PauseSleep(long nanoseconds) {\n    raw {\n        standIn = standIn + nanoseconds\n    }\n\n" +
            (print ? "    Print(1, \"pause {nanoseconds / 1000000}\", \"\", 0)\n" : string.Empty) + "}"),
        ("runtime/common/runtime.kz", "pub void Print(int fd, string text, string file, int line) {\n", """
            pub void Print(int fd, string text, string file, int line) {
                if fd == 2 && text.Bytes.Count == 7 {
                    raw {
                        if text.Pointer[0] == 97 && text.Pointer[6] == 101 {
                            standIn = standIn + 31000000000
                            return
                        }
                    }
                }

            """));

    [Fact]
    public void After_a_restart_the_state_is_fresh_the_inbox_survives_and_the_crashing_message_is_dropped()
    {
        // U21, U22: the crash of `Add(-1)` drops it, the constructor runs again on a copy of the kept `move` argument and the defaults of
        // the body, and `Add(2)` and `Get`, queued behind it, run on the new instance; under the allocator that makes every freed block
        // inaccessible, so that a block of the crashed heap that a copy still named ends the run
        const string text = """
            actor Counter(List<int> Seed) {
                mut int n = 0

                pub void Add(int k) {
                    if k < 0 {
                        throw "negative {k}"
                    }

                    n = n + k
                    print("added {k}, now {n}")
                }

                pub int Get() => Seed.Count * 100 + n
            }

            seed = [1, 2]
            c = spawn Counter(move seed)
            send c.Add(5)
            send c.Add(-1)
            send c.Add(2)
            print(c.Get())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-restart-fresh");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Counter#1 crashed: thrown \"negative -1\" at program.kz:6"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["added 5, now 5", "added 2, now 2", "202"], "");
    }

    /// <summary>
    /// The growth of the resident memory, in KiB, between the readings <paramref name="before"/> and <paramref name="after"/> a program
    /// printed, each held above 0 first, so that a read or a parse of <c>/proc/self/status</c> that failed, whose reading is -1, fails the
    /// test where a growth of 0 would pass it.
    /// </summary>
    private static long Grown(string before, string after)
    {
        var (first, last) = (long.Parse(before, System.Globalization.CultureInfo.InvariantCulture), long.Parse(after, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(first > 0 && last > 0, $"the resident memory read {first} KiB before and {last} KiB after");
        return last - first;
    }

    [Fact]
    public void Memory_stays_flat_over_ten_thousand_crashes()
    {
        // U21: each crash frees the actor's heap whole, its stacks go back to the pool and its sessions end, so that 10,000 crashes after
        // 1,000 that warm the pools grow the resident memory by less than 256 KiB, a tenth of what a page of 256 bytes kept per crash
        // would add; the pauses stood in for, so that no crash waits
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        const string program = """
            long | IoError Resident() {
                bytes = File.ReadAll("/proc/self/status")
                text = string.FromUtf8(bytes) else {
                    InvalidUtf8 => ""
                }

                for line in text.Split('\n') {
                    if line.StartsWith("VmRSS:") {
                        return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                            ParseError => -1
                        }
                    }
                }

                return -1
            }

            actor Flaky(string Name) {
                mut List<string> seen = []

                pub void Fail(string s, string t) {
                    seen.Add(s)
                    throw "{Name} fails at {s} {t}"
                }

                pub int Ping() => seen.Count + 1
            }

            f = spawn Flaky("flaky")
            mut total = 0
            for i in 0..<1000 {
                send f.Fail("warm {i}", "t")
                total = total + f.Ping()
            }

            before = Resident() else {
                IoError e => -1
            }

            for i in 0..<10000 {
                send f.Fail("run {i}", "t")
                total = total + f.Ping()
            }

            after = Resident() else {
                IoError e => -1
            }

            print(total)
            print(before)
            print(after)

            """;
        var run = BuildAndRun(program, StoodIn(print: false), "actor-crash-memory");
        Assert.Equal((0, 11000), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("11000", run.Output[0]);
        var grown = Grown(run.Output[1], run.Output[2]);
        Assert.True(grown < 256, $"10,000 crashes grow the resident memory by {grown} KiB");
    }

    [Fact]
    public void A_crash_between_two_copied_arguments_ends_their_session()
    {
        // U19, a crash between two copied arguments: `Boom` crashes the sender after the first argument of `Take`
        // crossed, while the session of the call's two copies is open; the box went into the session's memo, of the thread's own heap,
        // as every block of a session of two copies goes, and the crash ends the session with it, so that 10,000 such crashes grow the
        // resident memory by less than 2 MiB, where a memo of 1,536 bytes kept per crash adds 15; the copy of the box and its text is made
        // in the message's own heap, since `Boom(s)` is a call after an argument that crosses, which the sender's crash frees, where a page
        // of 256 bytes kept per crash would add about 2.4 MiB (compiler/README.md)
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        const string program = """
            long | IoError Resident() {
                bytes = File.ReadAll("/proc/self/status")
                text = string.FromUtf8(bytes) else {
                    InvalidUtf8 => ""
                }

                for line in text.Split('\n') {
                    if line.StartsWith("VmRSS:") {
                        return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                            ParseError => -1
                        }
                    }
                }

                return -1
            }

            class Box(string Text)

            actor Sink {
                pub void Take(Box a, string b) {
                    print(a.Text)
                }
            }

            string Boom(string s) {
                throw "boom {s}"
            }

            actor Sender(Sink Into) {
                pub void Send(string s) {
                    send Into.Take(Box("x{s}"), Boom(s))
                }

                pub int Ping() => 1
            }

            sender = spawn Sender(spawn Sink())
            mut total = 0
            for i in 0..<1000 {
                send sender.Send("{i}")
                total = total + sender.Ping()
            }

            before = Resident() else {
                IoError e => -1
            }

            for i in 0..<10000 {
                send sender.Send("{i}")
                total = total + sender.Ping()
            }

            after = Resident() else {
                IoError e => -1
            }

            print(total)
            print(before)
            print(after)

            """;
        var run = BuildAndRun(program, StoodIn(print: false), "actor-crash-session");
        Assert.Equal((0, 11000), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("11000", run.Output[0]);
        var grown = Grown(run.Output[1], run.Output[2]);
        Assert.True(grown < 2048, $"10,000 crashes between two copied arguments grow the resident memory by {grown} KiB");
    }

    [Fact]
    public void An_overflow_inside_an_actor_restarts_it_on_Linux()
    {
        // U19: the handler of the fault takes an overflow in the guard page of an actor's stack as the actor's crash, twice, and the
        // actor runs `Count` on a fresh state after each restart
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Deep {
                mut int calls = 0

                int Down(int n) => Down(n) + 1

                pub void Go() {
                    print(Down(0))
                }

                pub int Count() {
                    calls = calls + 1
                    return calls
                }
            }

            d = spawn Deep()
            print(d.Count())
            send d.Go()
            print(d.Count())
            send d.Go()
            print(d.Count())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-overflow-restarts");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Deep#1 crashed: stack-overflow at program.kz:4", "Deep#1 crashed: stack-overflow at program.kz:4"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["1", "1", "1"], "");
    }

    [Fact]
    public void A_callee_whose_caller_crashed_writes_no_reply_into_the_callers_stack()
    {
        // U20: the caller's `Ask` waits on `Work`, whose call back of the chain crashes the caller; its stacks go back to the system at
        // once under this runtime, and `Work` then crashes with `callee-crashed`, whose crash writes nothing into the stack of `Ask`, its
        // reply unhooked, else that write would fault
        const string text = """
            actor Callee {
                pub int Work(Caller back) => back.Fail() + 1
            }

            actor Caller(Callee Other) {
                pub void Ask() {
                    print(Other.Work(this))
                }

                pub int Fail() {
                    throw "caller fails"
                }

                pub int Ping() => 7
            }

            c = spawn Callee()
            a = spawn Caller(c)
            send a.Ask()
            print(a.Ping())

            """;
        var unpooled = ListTests.Edited(("runtime/common/runtime.kz", "        if pooledCount >= 64 {", "        if pooledCount >= 0 {"));
        var run = BuildAndRun(text, unpooled, "actor-reply-unhooked");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Caller#2 crashed: thrown \"caller fails\" at program.kz:11", "Callee#1 crashed: callee-crashed at program.kz:2"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["7"], "");
    }

    [Fact]
    public void Children_end_after_their_current_message()
    {
        // U13: the parent's crash ends its child, idle with `Tick(2)` queued, which is dropped; the parent's new instance has no child
        const string text = """
            actor Child(int N) {
                pub void Tick(int t) {
                    print("child {N} tick {t}")
                }
            }

            actor Parent {
                mut Child? kid = null

                pub void Make() {
                    kid = spawn Child(1)
                }

                pub void Boom() {
                    throw "parent fails"
                }

                pub Child? Kid() => kid
            }

            p = spawn Parent()
            p.Make()
            kid = p.Kid()
            if kid != null {
                send kid.Tick(1)
                send p.Boom()
                send kid.Tick(2)
            }

            print(p.Kid() == null)

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-children-end");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent fails\" at program.kz:15"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["child 1 tick 1", "true"], "");
    }

    [Fact]
    public void The_pause_doubles_up_to_30_s_and_starts_again_after_a_full_cap()
    {
        // U23: 100 ms at the first crash, twice the one before at each crash up to 30 s, each within 20% either way, and after a run of
        // 31 s with no crash 100 ms again, read through the clock of the pauses, which this runtime stands in for
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            f = spawn Flaky()
            for i in 0..<12 {
                send f.Fail()
                print(f.Ping())
            }

            eprint("advance")
            send f.Fail()
            print(f.Ping())

            """;
        var run = BuildAndRun(text, StoodIn(print: true), "actor-pause-doubles");
        Assert.Equal((0, 13), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal(26, run.Output.Count);
        long[] pauses = [100, 200, 400, 800, 1600, 3200, 6400, 12800, 25600, 30000, 30000, 30000, 100];
        for (var i = 0; i < pauses.Length; i++)
        {
            Assert.Equal("1", run.Output[(2 * i) + 1]);
            var slept = long.Parse(run.Output[2 * i]["pause ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(slept >= pauses[i] * 8 / 10 && slept <= pauses[i] * 12 / 10, $"pause {i}: {slept} ms where {pauses[i]} ms within 20% was expected");
        }
    }

    [Fact]
    public void A_top_level_wait_sleeps_through_a_restarts_pause_instead_of_raising_deadlock()
    {
        // U23: the run queue is empty while the pause runs, and the wait of `Ping` sleeps on the system's clock until it ends, about
        // 100 ms, where U18 would raise `deadlock`; the watch times the run alone, the build before it taking more than that
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            f = spawn Flaky()
            send f.Fail()
            print(f.Ping())

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "actor-pause-sleeps");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var run = Toolchain.Run(executable, "0");
        watch.Stop();
        Ran((run.Code, run.Output, Logged(run.Error).Others), 0, ["1"], "");
        Assert.True(watch.ElapsedMilliseconds >= 80, $"the run took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_waiting_caller_gets_callee_crashed_at_its_line_and_crashes_in_turn()
    {
        // U20: `B`'s crash answers `A`'s wait with `callee-crashed` at the line of that call, `A`'s own crash, whose wait of the top-level
        // code raises `callee-crashed` again, at the root, which ends the process
        const string text = """
            actor B {
                pub int Get(int n) {
                    if n == 0 {
                        throw "zero"
                    }

                    return n
                }
            }

            actor A(B Other) {
                pub int Ask(int n) => Other.Get(n) + 1
            }

            b = spawn B()
            a = spawn A(b)
            print(a.Ask(1))
            print(a.Ask(0))
            print("after")

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-callee-crashed");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["B#1 crashed: thrown \"zero\" at program.kz:4", "A#2 crashed: callee-crashed at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["2"], "error: callee-crashed at program.kz:18\n");
    }

    [Fact]
    public void A_message_whose_callee_ends_while_one_of_its_arguments_waits_holds_no_block_of_the_heap_the_end_freed()
    {
        // U12, U13, U21, U6: `Box` ends as the child of a supervisor that crashed during the wait of `s.Get()`, which frees its heap whole,
        // its first page with it, and its slot of the table; the third argument reaches the list of the first, whose copy the call's memo
        // gives it again, so a copy made in the callee's heap would be read after its free. The message is built in a heap of its own and
        // copied at its queueing into the heap the top-level code allocates in, since the reference finds no actor (the runtime's
        // `ActorHeap`), and the `send` raises `actor-gone` at its line, "sent" never printed (U6; section 6, the owner, 2026-11-16, #519,
        // comment 6923513534, "Raise actor-gone"), as a call does (`ListTests`, `actor-end-wait-fenced`); under the allocator that makes
        // every freed block inaccessible
        const string text = """
            actor Slow {
                pub int Get() => 7
            }

            actor Box {
                pub void Add(List<int> xs, int k, List<int> ys) {
                    print(xs[0] + k + ys[2])
                }
            }

            actor Parent {
                mut Box? kid = null

                pub void Make() {
                    kid = spawn Box()
                }

                pub Box? Kid() => kid

                pub void Boom() {
                    throw "parent"
                }
            }

            p = spawn Parent()
            p.Make()
            kid = p.Kid()
            s = spawn Slow()
            items = [10, 20, 30]
            if kid != null {
                send p.Boom()
                send kid.Add(items, s.Get(), items)
                print("sent")
            }

            print("end")

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-ended-while-waiting");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:21"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: actor-gone at program.kz:32\n");
    }

    [Fact]
    public void A_function_parameter_that_takes_its_default_crosses_at_a_spawn_at_its_restart_and_in_a_message_a_crash_copies()
    {
        // U12, F12: a nullable function parameter that takes its default, `null`, a constant expression with no environment to copy, in the
        // constructor a `spawn` keeps and its restart runs again, and in a message, written with a lambda and without, the last one queued
        // behind the crash, which copies it into the new instance's heap; under the allocator that makes every freed block inaccessible
        const string text = """
            actor Apply(((int) => int)? F = null) {
                pub int Run(int x, ((int) => int)? g = null) {
                    if g != null {
                        return g(x)
                    }

                    if F != null {
                        return F(x)
                    }

                    return x + 1
                }

                pub void Fail() {
                    throw "fail"
                }
            }

            a = spawn Apply()
            print(a.Run(1))
            k = 10
            print(a.Run(1, x => x + k))
            send a.Fail()
            print(a.Run(2))

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-function-default");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Apply#1 crashed: thrown \"fail\" at program.kz:15"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["2", "11", "3"], "");
    }

    [Fact]
    public void The_log_line_carries_the_actor_the_id_the_place_and_the_thrown_text()
    {
        // U24: one line at `Error` per crash in P7's layout, the thrown text in double quotes as a `string` field is written, its `"`
        // after a `\`; `KURZ_LOG` turns it off as any line of the category `Kurz.Actor` (P4)
        const string text = """
            actor Shop {
                pub void Buy(string item) {
                    throw "no \"{item}\""
                }

                pub int Ping() => 1
            }

            s = spawn Shop()
            send s.Buy("stock")
            print(s.Ping())

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "actor-log-line");
        var on = Toolchain.Run(executable, "0");
        Assert.Equal(0, on.Code);
        Assert.Equal(["1"], on.Output);
        var line = Assert.Single(Toolchain.Lines(on.Error));
        Assert.Matches("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z Error Kurz\\.Actor: Shop#1 crashed: thrown \"no \\\\\"stock\\\\\"\" at program\\.kz:3$", line);
        if (!OperatingSystem.IsLinux())
        {
            return; // the filter is read on Linux alone (P4)
        }

        var off = Processes.Capture(executable, [], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0", ["KURZ_LOG"] = "Kurz.Actor=None" });
        Assert.Equal((0, "1\n", ""), (off.Code, off.Output, off.Error));
    }

    // ---- the words of a policy (U25 to U27; section 6, "Policy at `spawn`"; comment 6902210646, Q2 to Q4) --------------------------

    [Fact]
    public void A_stopped_actor_writes_its_log_line_its_waiting_caller_gets_callee_crashed_and_a_call_after_the_stop_actor_gone()
    {
        // U25: `Calc`'s crash under `on crash stop` writes its line (U24) and ends it; `Asker`, which waited on it, crashes with
        // `callee-crashed` at its call (U20) and restarts after its 1 ms, serving its queued `Ping`; the call of the top-level code that
        // comes after the stop raises `actor-gone`, as a call to an actor that ended does (U20)
        const string text = """
            actor Calc {
                pub int Div(int n) => 10 / n
            }

            actor Asker(Calc C) {
                pub void Ask(int n) {
                    print("asked {C.Div(n)}")
                }

                pub int Ping() => 1
            }

            c = spawn Calc() on crash stop
            a = spawn Asker(c) on crash restart after 1ms
            send a.Ask(5)
            send a.Ask(0)
            print(a.Ping())
            print(c.Div(2))
            print("after")

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-policy-stop");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Calc#1 crashed: divide-by-zero at program.kz:2", "Asker#2 crashed: callee-crashed at program.kz:7"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["asked 2", "1"], "error: actor-gone at program.kz:18\n");
    }

    [Fact]
    public void A_call_queued_in_the_inbox_of_a_stopped_actor_raises_actor_gone()
    {
        // U25 (section 6, the owner, 2026-11-16, #519, comment 6919351399, "actor-gone"): the stopped actor's inbox goes as an ended actor's
        // does (U21), so that `Get`, queued behind the crashing `Fail`, raises `actor-gone` in its caller, where a restart would have answered it
        const string text = """
            actor Slow {
                pub void Fail() {
                    throw "slow"
                }

                pub int Get() => 7
            }

            s = spawn Slow() on crash stop
            send s.Fail()
            print(s.Get())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-policy-stop-queued");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Slow#1 crashed: thrown \"slow\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: actor-gone at program.kz:11\n");
    }

    [Fact]
    public void A_reference_whose_actor_ended_compares_hashes_and_prints_by_its_number_and_reaches_no_later_actor_of_its_slot()
    {
        // section 6 (the owner, 2026-11-16, #519, comment 6919351399, "Erlang: id + generation"), U2, U12, U20: `old` ends as the child of
        // `Parent`, whose crash frees its heap whole and its slot of the table, which `fresh`, the next actor spawned, takes again; `old`
        // compares, hashes as a key and prints by its number, which reads no record (section 6, assumed there), and a call through it finds
        // no actor in the slot and raises `actor-gone` at its line, where a reference without its number would reach `fresh` and print 2;
        // under the allocator that makes every freed block inaccessible
        const string text = """
            actor Child(int N) {
                pub int Get() => N
            }

            actor Parent {
                pub Child Make(int n) => spawn Child(n)

                pub void Boom() {
                    throw "parent"
                }
            }

            p = spawn Parent()
            old = p.Make(1)
            mut seen = Map<Child, string>()
            seen[old] = "old"
            send p.Boom()
            fresh = p.Make(2)
            seen[fresh] = "fresh"
            print(old == fresh)
            print("{old} {fresh}")
            print(seen[old] ?? "none")
            print(seen[fresh] ?? "none")
            print(fresh.Get())
            print(old.Get())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-stale-reference");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:9"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["false", "Child#2 Child#3", "old", "fresh", "2"], "error: actor-gone at program.kz:25\n");
    }

    [Fact]
    public void The_slot_of_an_ended_actor_is_taken_again_so_that_a_crash_looping_supervisors_children_take_one()
    {
        // section 6 (comment 6919351399): each crash of `Parent` ends the child its `Make` spawned, which frees its slot, and the next `Make`
        // takes it again, the slot freed last first, as slotmap's free list takes them (section 6, assumed there), so that after five
        // children the table has taken two slots, where one per actor ever spawned would be six; the count of the slots taken written at
        // the end of the program
        const string text = """
            actor Child {
                pub int Get() => 1
            }

            actor Parent {
                mut Child? kid = null

                pub int Make() {
                    kid = spawn Child()
                    return 1
                }

                pub void Boom() {
                    throw "parent"
                }
            }

            p = spawn Parent() on crash restart after 1ms
            mut total = 0
            for i in 0..<5 {
                total = total + p.Make()
                send p.Boom()
            }

            print(total)

            """;
        var counted = ListTests.Edited(("runtime/common/runtime.kz", "pub void Finish() {\n    EndActors()\n",
            "pub void Finish() {\n    mut long used = 0\n    raw {\n        used = slotsUsed\n    }\n\n    Print(2, \"slots {used}\", \"\", 0)\n    EndActors()\n"));
        var run = BuildAndRun(text, counted, "actor-slots-reused");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(Enumerable.Repeat("Parent#1 crashed: thrown \"parent\" at program.kz:14", 4), crashes);
        Ran((run.Code, run.Output, rest), 0, ["5"], "slots 2\n");
    }

    [Fact]
    public void References_in_the_pages_of_the_table_past_its_first_reach_their_actors()
    {
        // section 6 (comment 6919351399): the table's pages double from 64 slots and never move (section 6, assumed there), so that 300
        // actors take slots of its first three pages, 64, 128 and 256 slots, and a call through each reference reaches its own actor
        const string text = """
            actor Idle(int N) {
                pub int Get() => N
            }

            mut List<Idle> all = []
            for i in 0..<300 {
                all.Add(spawn Idle(i))
            }

            mut total = 0
            for a in all {
                total = total + a.Get()
            }

            print(total)
            print(all[299])

            """;
        Ran(BuildAndRun(text, toolchain.Runtime, "actor-table-pages"), 0, ["44850", "Idle#300"], "");
    }

    [Fact]
    public void An_ended_actor_frees_its_heap_at_once_or_where_it_stands_in_the_run_queue_when_the_scheduler_takes_it_off()
    {
        // section 6 (comment 6919351399; U13, U25): `Parent`'s crash under `stop` ends it and its children; `Child(1)`, idle, and `Parent`
        // free their heaps at once, the new heap `Parent`'s fall made among them, and `Child(2)`, which stands in the run queue with
        // `Tick(2)`, dropped, frees its first page when the scheduler takes it off at the next wait (section 6, assumed there), so that the
        // heaps drop by 2 and then by 1, where a record freed while the run queue holds it would be freed again when the scheduler takes it
        // off; under the allocator that makes every freed block inaccessible
        const string text = """
            actor Child(int N) {
                pub void Tick(int t) {
                    print("tick {N} {t}")
                }
            }

            actor Parent {
                mut Child? one = null
                mut Child? two = null

                pub void Make() {
                    one = spawn Child(1)
                    two = spawn Child(2)
                }

                pub Child? Two() => two

                pub void Boom() {
                    throw "parent"
                }
            }

            actor Quick {
                pub int Get() => 5
            }

            p = spawn Parent() on crash stop
            q = spawn Quick()
            p.Make()
            two = p.Two()
            if two != null {
                send two.Tick(1)
                send two.Tick(2)
            }

            before = HttpServer.Heaps()
            send p.Boom()
            print(q.Get())
            middle = HttpServer.Heaps()

            """;
        var run = BuildAndRun(text + "print(q.Get())\nprint(\"{before - middle} {middle - HttpServer.Heaps()}\")\n", ListTests.Edited([.. ListTests.Fence, .. HeapCount]), "actor-ended-heaps");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:19"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["tick 2 1", "5", "5", "2 1"], "");

        // U13: an actor that ended while it stood in the run queue, still there when the program's actors end, has its first page freed
        // with them (the runtime's `EndActors`), with `Quick`'s heap, the one actor of the table
        var ended = BuildAndRun(text + "eprint(\"end actors\")\nprint(\"{before - middle} {middle - HttpServer.Heaps()}\")\n", ListTests.Edited([.. ListTests.Fence, .. HeapCount, ListTests.EndingActors]), "actor-ended-heaps-end");
        var (endedCrashes, endedRest) = Logged(ended.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:19"], endedCrashes);
        Ran((ended.Code, ended.Output, endedRest), 0, ["tick 2 1", "5", "2 2"], "");
    }

    /// <summary>
    /// The runtime under the allocator that makes every freed block inaccessible, whose <c>RecordFreed</c> writes 16 into the word of the heap
    /// of a record it frees, before the free, since the pages of a freed heap stay mapped in the allocator's pool, which the fence does not
    /// reach, so that a read of a freed record's heap ends the run.
    /// </summary>
    private static CheckedRuntime PoisonedEnds => ListTests.Edited([.. ListTests.Fence, ("runtime/common/runtime.kz", "    raw {\n        std.HeapFree(state - 192)\n",
            "    raw {\n        *Ptr<long>(state - 64) = 16\n        std.HeapFree(state - 192)\n")]);

    [Fact]
    public void A_child_whose_supervisor_ended_while_it_waited_takes_its_own_crash_and_writes_its_log_line()
    {
        // U13, U19, U24, section 6 (comment 6919351399): `Parent`'s crash under `stop` ends it, which frees its record, while `Child` waits
        // in `Work` for `S.Get()`, so that `Child` ends after its current message with no supervisor (the runtime's `EndChildren`, section 6,
        // assumed there); its crash after the wait is taken by its own end, and its log line written, reading nothing of the freed
        // supervisor; under the allocator that makes every freed block inaccessible, and with the word of the heap of a record its end
        // frees at once, or the end of its hold frees where a thread held it (section 6, assumed there), written 16 before the free, since
        // the pages of a freed heap stay mapped in the allocator's pool, which the fence does not reach, so that a read of the freed
        // supervisor's heap ends the run
        const string text = """
            actor Slow {
                pub int Get() => 7
            }

            actor Other {
                pub int Ping() => 0
            }

            actor Child(Slow S) {
                pub void Work() {
                    n = S.Get()
                    throw "child {n}"
                }
            }

            actor Parent(Slow S) {
                mut Child? kid = null

                pub void Make() {
                    kid = spawn Child(S)
                }

                pub Child? Kid() => kid

                pub void Boom() {
                    throw "parent"
                }
            }

            s = spawn Slow()
            o = spawn Other()
            p = spawn Parent(s) on crash stop
            p.Make()
            kid = p.Kid()
            if kid != null {
                send kid.Work()
            }

            send p.Boom()
            print(o.Ping())
            print(s.Get())

            """;
        var run = BuildAndRun(text, PoisonedEnds, "actor-orphan-crash");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#3 crashed: thrown \"parent\" at program.kz:26", "Child#4 crashed: thrown \"child 7\" at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["0", "7"], "");
    }

    [Fact]
    public void A_supervisors_crash_ends_its_children_from_the_last_slot_of_the_table_down()
    {
        // U13, U20, section 6 (comment 6919351399): `Parent`'s crash ends `second` before `first`, from the highest index of the table down
        // (section 6, assumed there), each setting the call queued to it `gone`, so that the caller that waits on `second` wakes first and
        // crashes with `actor-gone` first
        const string text = """
            actor Child {
                pub int Get() => 1
            }

            actor Parent {
                mut Child? first = null
                mut Child? second = null

                pub void Make() {
                    first = spawn Child()
                    second = spawn Child()
                }

                pub Child? First() => first

                pub Child? Second() => second

                pub void Boom() {
                    throw "parent"
                }

                pub int Ping() => 0
            }

            actor Asker {
                pub void Ask(Child c) {
                    print(c.Get())
                }
            }

            p = spawn Parent()
            p.Make()
            a = p.First()
            b = p.Second()
            one = spawn Asker()
            two = spawn Asker()
            if a != null {
                send one.Ask(a)
            }

            if b != null {
                send two.Ask(b)
            }

            send p.Boom()
            print(p.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-children-order");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:19", "Asker#5 crashed: actor-gone at program.kz:27", "Asker#4 crashed: actor-gone at program.kz:27"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["0"], "");
    }

    [Fact]
    public void A_spawn_past_the_largest_number_ends_the_process_as_a_refused_allocation()
    {
        // U2, section 6 (comment 6919351399): the actor's number is the generation of its slot, a `long`, and the spawn past the largest
        // ends the process as a refused allocation does, with the bytes of its record and its state, as a string past the largest `int`
        // does (T15), so that no two actors have one number (section 6, assumed there); the count of the spawns begins one below it here
        const string text = """
            actor Idle {
                pub int Get() => 1
            }

            a = spawn Idle()
            print(a)
            b = spawn Idle()
            print(b)

            """;
        var late = ListTests.Edited(("runtime/common/runtime.kz", "            sharedWords = SharedCleared(words)\n", "            sharedWords = SharedCleared(words)\n            *Ptr<long>(words + 24) = 9223372036854775806\n"));
        Ran(BuildAndRun(text, late, "actor-number-last"), 1, ["Idle#9223372036854775807"], "error: allocation-failed (160 bytes)\n");
    }

    [Fact]
    public void A_process_whose_start_got_no_block_for_the_shared_words_makes_it_at_its_first_spawn()
    {
        // section 6 (assumed): the start makes the block of the words the threads share, and where the system refuses it there, the first
        // `spawn` makes it (`Shared`), the one thread taking no lock before it; here the start's allocation gives none
        const string text = """
            actor Counter {
                mut int n = 0

                pub int Add() {
                    n = n + 1
                    return n
                }
            }

            print(0)
            c = spawn Counter()
            print(c.Add())
            print(c.Add())

            """;
        var refused = ListTests.Edited(("runtime/common/runtime.kz", "        byte* words = std.AllocateIn(null, 48)\n", "        byte* words = null\n"));
        Ran(BuildAndRun(text, refused, "actor-shared-at-spawn"), 0, ["0", "1", "2"], "");
    }

    [Fact]
    public async Task A_print_after_a_print_the_system_refused_takes_its_stream_s_lock_and_raises_at_its_own_line()
    {
        // section 6: a `print` lets go of its stream's lock before it raises `print-failed`, so that the top-level code's `print` after
        // the crash of `Printer`, whose `print` the system refused, takes the lock and raises `print-failed` at its own line, where a
        // lock left taken waits on its word for ever
        const string text = """
            actor Printer {
                pub void Spam() {
                    mut i = 0
                    while i < 100000000 {
                        print("line")
                        i = i + 1
                    }
                }

                pub int Count() => 3
            }

            p = spawn Printer()
            send p.Spam()
            eprint(p.Count())
            print("again")

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], StoodIn(print: false), true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var info = new System.Diagnostics.ProcessStartInfo(toolchain.Build(compilation.Program, "actor-print-again"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.Environment["KURZ_TRACE"] = "0";
        using var process = System.Diagnostics.Process.Start(info)!;
        try
        {
            process.StandardOutput.Close(); // the reader is gone: a write is refused (a closed pipe)
            var error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "the program did not end once its output was closed");
            var (crashes, rest) = Logged(await error);
            Assert.Equal(["Printer#1 crashed: print-failed at program.kz:5"], crashes);
            Assert.Equal((1, "3\nerror: print-failed at program.kz:16\n"), (process.ExitCode, rest));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    [Fact]
    public void An_escalated_crash_at_the_root_ends_the_process_with_the_childs_error_line_and_trace_and_no_log_line()
    {
        // U25: the root is the supervisor of `Worker`, so that its escalated crash ends the process with the child's error line, the
        // thrown text below it, and the trace of the child's stack, from the raise to its message's method (assumed), and no log line
        // (comment 6902210646, Q4)
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 1
            }

            w = spawn Worker() on crash escalate
            print(w.Ping())
            send w.Fail()
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-policy-escalate-root", trace: null);
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        var error = "error: thrown at program.kz:3:\nworker fails\n" + (OperatingSystem.IsLinux() ? "  at Worker.Fail in program.kz:3\n" : string.Empty);
        Ran((run.Code, run.Output, rest), 1, ["1"], error);
    }

    [Fact]
    public void An_escalated_overflow_at_the_root_ends_the_process_with_its_stack_overflow_line_and_the_childs_trace()
    {
        // U25, U19: an overflow in the guard page of an escalating actor's stack, on Linux, ends the process as an overflow of the
        // top-level code does, with the trace of the child's stack, cut at its hundredth frame
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Deep {
                int Down(int n) => Down(n) + 1

                pub void Go() {
                    print(Down(0))
                }
            }

            d = spawn Deep() on crash escalate
            d.Go()

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-policy-escalate-overflow", trace: null);
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        var (error, trace) = TraceTests.Split(rest);
        Assert.Equal((1, "error: stack-overflow at program.kz:2\n"), (run.Code, error));
        TraceTests.AssertTrace(trace, "program.kz:2", true);
    }

    [Fact]
    public void An_escalated_crash_crashes_the_supervisor_whose_restart_ends_its_other_children()
    {
        // U25: `Kid(1)`'s crash under `on crash escalate` is `Boss`'s, passed up whole: the root restarts `Boss`, whose log line carries
        // the child's id, place and text (assumed: the line names the supervisor that crashed), and none is written for the child; the
        // crash ends `Boss`'s other child, `Kid(2)`, whose queued `Tick(2)` is dropped (U21); `Boss`'s new instance has run no `Make`
        const string text = """
            actor Kid(int N) {
                pub void Fail() {
                    throw "kid {N} fails"
                }

                pub void Tick(int t) {
                    print("kid {N} tick {t}")
                }
            }

            actor Boss {
                mut Kid? first = null
                mut Kid? second = null
                mut int starts = 0

                pub void Make() {
                    starts = starts + 1
                    first = spawn Kid(1) on crash escalate
                    second = spawn Kid(2)
                }

                pub void Break() {
                    k = first
                    if k != null {
                        send k.Fail()
                    }
                }

                pub Kid? Second() => second

                pub int Starts() => starts
            }

            b = spawn Boss() on crash restart after 1ms
            b.Make()
            s = b.Second()
            if s != null {
                send s.Tick(1)
                b.Break()
                send s.Tick(2)
            }

            print(b.Starts())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-policy-escalate");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Boss#1 crashed: thrown \"kid 1 fails\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["kid 2 tick 1", "0"], "");
    }

    [Theory]
    [InlineData("on crash escalate", 1, new[] { "Top#1 crashed: thrown \"kid fails\" at program.kz:3" })]
    [InlineData("on crash restart after 1ms max 1 in 1min", 2, new[] { "Kid#3 crashed: thrown \"kid fails\" at program.kz:3", "Kid#3 crashed: thrown \"kid fails\" at program.kz:3", "Top#1 crashed: restart-limit at program.kz:10" })]
    public void A_crash_passed_into_an_actor_whose_constructor_runs_at_its_spawn_is_its_spawners(string policy, int fails, string[] lines)
    {
        // U25, U26, U3 (assumed): `Boss` has no instance while its constructor runs at its `spawn`, whose throw is its spawner's crash
        // (U3), so that the crash `Kid` escalates into it, and the `restart-limit` its second crash gives it, crash `Top`, which writes
        // the line and restarts, and the top-level code's wait on `Top` raises `callee-crashed` (U20)
        var text = $$"""
            actor Kid {
                pub void Fail() {
                    throw "kid fails"
                }

                pub int Ping() => 2
            }

            int Start() {
                k = spawn Kid() {{policy}}
                for i in 0..<{{fails}} {
                    send k.Fail()
                }

                return k.Ping()
            }

            actor Boss {
                mut int x = Start()

                pub int X() => x
            }

            actor Top {
                pub int Make() {
                    b = spawn Boss()
                    return b.X()
                }
            }

            t = spawn Top() on crash restart after 1ms
            print(t.Make())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, $"actor-policy-into-building-{fails}");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(lines, crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:32\n");
    }

    [Fact]
    public void A_spawn_makes_the_block_of_its_knobs_only_where_it_writes_after_or_max()
    {
        // U26: the first pause, the limit's count and window, the ring of the crash times, the place of the `spawn` and the word that
        // marks a `per node` type's block (U54) stand in a block of their own in the supervisor's heap, made where `after` or `max N in`
        // is written, so that the block of a restart that writes neither grows by one word alone and an idle actor stays within its 512
        // bytes (section 13)
        const string knobs = "{ i64, i64, i64, ptr, i64, i64, i64, %kz.str, i64, i64 }";
        string Ir(string spawn)
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", $"actor A {{\n    pub int Get() => 1\n}}\n\na = {spawn}\nprint(a.Get())\n"), [], toolchain.Runtime, true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            return compilation.Program.Ir;
        }

        Assert.DoesNotContain(knobs, Ir("spawn A()"), StringComparison.Ordinal);
        Assert.DoesNotContain(knobs, Ir("spawn A() on crash restart"), StringComparison.Ordinal);
        Assert.Contains(knobs, Ir("spawn A() on crash restart after 1ms"), StringComparison.Ordinal);
        Assert.Contains(knobs, Ir("spawn A() on crash restart max 2 in 1min"), StringComparison.Ordinal);
    }

    [Fact]
    public void After_sets_the_first_pause_which_doubles_to_the_cap_and_comes_back_after_a_full_cap()
    {
        // U26: `after 5ms` is the first pause, doubled at each crash, 5, 10 and 20 ms, and after a run of 31 s with no crash 5 ms again;
        // `after 20s` doubles to the cap of 30 s (U23), each within 20% either way, read through the clock of the pauses, which this
        // runtime stands in for
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            f = spawn Flaky() on crash restart after 5ms
            for i in 0..<3 {
                send f.Fail()
                print(f.Ping())
            }

            eprint("advance")
            send f.Fail()
            print(f.Ping())
            g = spawn Flaky() on crash restart after 20s
            for i in 0..<2 {
                send g.Fail()
                print(g.Ping())
            }

            """;
        var run = BuildAndRun(text, StoodIn(print: true), "actor-policy-after");
        Assert.Equal((0, 6), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal(12, run.Output.Count);
        long[] pauses = [5, 10, 20, 5, 20000, 30000];
        for (var i = 0; i < pauses.Length; i++)
        {
            Assert.Equal("1", run.Output[(2 * i) + 1]);
            var slept = long.Parse(run.Output[2 * i]["pause ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(slept >= pauses[i] * 8 / 10 && slept <= pauses[i] * 12 / 10, $"pause {i}: {slept} ms where {pauses[i]} ms within 20% was expected");
        }
    }

    [Fact]
    public void Max_counts_the_crashes_in_a_sliding_window_and_the_one_past_it_is_the_supervisors_restart_limit_at_the_spawns_line()
    {
        // U26: under `max 2 in 1min`, crashes at 0 s, 31 s and 62 s each restart, since the first has left the window by the third, as
        // Erlang's `supervisor` drops the restarts older than its period, where a count with no window, or a window from the first
        // crash, would differ; a fourth crash at once is the third in the window, which passes the limit: its line is written (assumed)
        // and the top-level code, the supervisor, crashes with `restart-limit` at the line of the `spawn`, which ends the process
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            f = spawn Flaky() on crash restart after 1ms max 2 in 1min
            send f.Fail()
            print(f.Ping())
            eprint("advance")
            send f.Fail()
            print(f.Ping())
            eprint("advance")
            send f.Fail()
            print(f.Ping())
            send f.Fail()
            print(f.Ping())

            """;
        var run = BuildAndRun(text, StoodIn(print: false), "actor-policy-max");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(Enumerable.Repeat("Flaky#1 crashed: thrown \"flaky\" at program.kz:3", 4), crashes);
        Ran((run.Code, run.Output, rest), 1, ["1", "1", "1"], "error: restart-limit at program.kz:9\n");
    }

    [Fact]
    public void A_restart_limit_in_an_actor_is_its_crash_which_its_own_supervisor_handles()
    {
        // U26: `Flaky`'s second crash passes its `max 1 in 1min`, and `Boss` crashes with `restart-limit` at the line of the `spawn`,
        // after `Flaky`'s two lines; the root restarts `Boss`, and the crash ends `Flaky` with it (U21)
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            actor Boss {
                mut int starts = 0

                pub void Break() {
                    starts = starts + 1
                    k = spawn Flaky() on crash restart after 1ms max 1 in 1min
                    send k.Fail()
                    send k.Fail()
                    print(k.Ping())
                }

                pub int Starts() => starts
            }

            b = spawn Boss() on crash restart after 1ms
            send b.Break()
            print(b.Starts())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-policy-limit-in-actor");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Boss#1 crashed: restart-limit at program.kz:14"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["0"], "");
    }

    [Fact]
    public void The_actor_past_its_restart_limit_ends_in_its_supervisors_fall_and_a_call_queued_to_it_raises_actor_gone()
    {
        // U26, U21, U20: `Flaky`'s second crash passes its `max 1 in 1min`, and `Boss` crashes with `restart-limit` at the line of the
        // `spawn`; `Flaky` has no activation, its own fall having cleared it, so that `Boss`'s fall ends it at once (`EndChildren`), and
        // `Ping`, queued to it by the top-level code, another caller than its supervisor, raises `actor-gone` at its line, which ends the
        // process; under the allocator that makes every freed block inaccessible
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            actor Boss {
                pub Flaky Make() => spawn Flaky() on crash restart after 1ms max 1 in 1min
            }

            b = spawn Boss()
            k = b.Make()
            send k.Fail()
            send k.Fail()
            print(k.Ping())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-policy-limit-ends");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Flaky#2 crashed: thrown \"flaky\" at program.kz:3", "Boss#1 crashed: restart-limit at program.kz:10"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: actor-gone at program.kz:17\n");
    }

    [Fact]
    public void A_spawn_that_writes_max_alone_pauses_100_ms_at_the_first_crash()
    {
        // U23, U26: the block of the knobs a `spawn` with `max N in` alone makes holds the first pause of 100 ms, as one with no knob, read
        // through the clock of the pauses, which this runtime stands in for, within 20% either way
        const string text = """
            actor Flaky {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            f = spawn Flaky() on crash restart max 3 in 1min
            send f.Fail()
            print(f.Ping())

            """;
        var run = BuildAndRun(text, StoodIn(print: true), "actor-policy-max-alone");
        Assert.Equal((0, 1), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal(2, run.Output.Count);
        Assert.Equal("1", run.Output[1]);
        var slept = long.Parse(run.Output[0]["pause ".Length..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(slept is >= 80 and <= 120, $"the first pause: {slept} ms where 100 ms within 20% was expected");
    }

    /// <summary>The Kurz of <see cref="Burning"/>: the recursion with no end, armed by the program's <c>eprint("arm")</c> for one call.</summary>
    private const string Burn = """
        thread static mut bool burnNext = false

        pub void BurnArm() {
            raw {
                burnNext = true
            }
        }

        long Burn(long n) {
            if n == 0 {
                return 0
            }

            return Burn(n - 1) * 3 + n
        }

        void BurnArmed() {
            mut bool armed = false
            raw {
                armed = burnNext
                burnNext = false
            }

            if armed && Burn(1000000000) == 7 {
                raw {
                    burnNext = true
                }
            }
        }


        """;

    /// <summary>The edit of the runtime's <c>Print</c> that runs <paramref name="call"/> where the program writes <c>arm</c> on standard error.</summary>
    private static (string Path, string Old, string New) Armed(string call) => ("runtime/common/runtime.kz",
        "pub void Print(int fd, string text, string file, int line) {\n", $$"""
            pub void Print(int fd, string text, string file, int line) {
                if fd == 2 && text.Bytes.Count == 3 {
                    raw {
                        if text.Pointer[0] == 97 && text.Pointer[1] == 114 && text.Pointer[2] == 109 {
                            {{call}}
                            return
                        }
                    }
                }


            """);

    /// <summary>
    /// The runtime with a stack fault made at the place <paramref name="at"/> of the file <paramref name="path"/>, the start of an entry into
    /// the allocator or the scheduler, or of a function the copy of a crossing calls: after the program writes <c>arm</c> on standard error,
    /// the first call that reaches that place recurses there without end, on whichever stack the thread then runs.
    /// </summary>
    private static CheckedRuntime Burning(string path, string at) => ListTests.Edited(BurnEdits(path, at));

    /// <summary>The edits of <see cref="Burning"/>: <c>Burn</c> put in before <paramref name="at"/>, and <c>Print</c> armed.</summary>
    private static (string Path, string Old, string New)[] BurnEdits(string path, string at) =>
        [(path, at, Burn + at + "    BurnArmed()\n"), Armed((path == "std/std.kz" ? "std." : string.Empty) + "BurnArm()")];

    /// <summary>The line of the call of <c>Burn</c> that does not fit, in the file <paramref name="path"/> as <see cref="Burning"/> edits it.</summary>
    private static int BurnLine(string path, string at) => LineOf(path, BurnEdits(path, at), "    return Burn(n - 1) * 3 + n");

    /// <summary>The line of the file <paramref name="path"/> that starts with <paramref name="line"/>, in that file as <paramref name="edits"/> edit it.</summary>
    private static int LineOf(string path, (string Path, string Old, string New)[] edits, string line)
    {
        var text = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == path).Bytes);
        foreach (var (_, old, @new) in edits.Where(e => e.Path == path))
        {
            text = text.Replace(old, @new, StringComparison.Ordinal);
        }

        return text[..text.IndexOf(line, StringComparison.Ordinal)].Count(c => c == '\n') + 1;
    }

    [Fact]
    public void A_stack_fault_inside_the_allocator_on_an_actor_s_stack_ends_the_process()
    {
        // O11 and the design record's section 6, "End the process": the allocation of `"x{k}"` recurses without end inside the allocator's
        // entry on the stack of `Make`, where the count of the entries under way is 1, so that the process ends with the error line of a
        // stack overflow and the exit code 1, and the actor neither restarts nor logs a crash
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Maker {
                pub string Make(int k) {
                    eprint("arm")
                    return "x{k}"
                }
            }

            m = spawn Maker()
            print(m.Make(1))

            """;
        // the error line names the allocator's own line where the fault is, the call of `Burn` that did not fit, in the edited library
        const string at = "pub byte* Allocate(long size) {\n    mut byte* p = null\n    sys.Entered()\n";
        var run = BuildAndRun(text, Burning("std/std.kz", at), "actor-fault-allocator");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], $"error: stack-overflow at std.kz:{BurnLine("std/std.kz", at)}\n");
    }

    [Fact]
    public void A_stack_fault_inside_the_scheduler_after_a_wait_on_an_actor_s_stack_ends_the_process()
    {
        // O11 and the design record's section 6, "End the process": `Go` parks in its wait on `Get`, whose side of the switch puts its count
        // back when it resumes, then its `send` recurses without end inside the scheduler's `Enqueue`, where the count is 1, so that the
        // process ends with the error line of a stack overflow and the exit code 1, and no actor restarts
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Sink {
                mut int total = 0

                pub void Take(int k) {
                    total = total + k
                }

                pub int Get() => 1
            }

            actor Sender(Sink To) {
                pub int Go() {
                    n = To.Get()
                    eprint("arm")
                    send To.Take(n)
                    return n
                }
            }

            s = spawn Sender(spawn Sink())
            print(s.Go())

            """;
        // the error line names the scheduler's own line where the fault is, the call of `Burn` that did not fit, in the edited runtime's file
        const string at = "pub void Enqueue(long index, long number, byte* message, bool bounded, long bound, string file, int line) {\n    sys.Entered()\n";
        var run = BuildAndRun(text, Burning("runtime/common/runtime.kz", at), "actor-fault-scheduler");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], $"error: stack-overflow at runtime.kz:{BurnLine("runtime/common/runtime.kz", at)}\n");
    }

    [Fact]
    public void A_stack_fault_inside_the_copy_of_a_crossing_on_an_actor_s_stack_ends_the_process()
    {
        // O11: the copy of a crossing counts as inside the scheduler from `CopyUse` to `CopyLeft`, whose work list is the thread's, so that
        // the box's copy, which recurses without end where the copy takes its next block, ends the process with the error line of a stack
        // overflow and the exit code 1, and no actor restarts with that work list half changed
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            class Box(string Text)

            actor Sink {
                mut int seen = 0

                pub void Take(Box b) {
                    seen = seen + b.Text.Bytes.Count
                }
            }

            actor Sender(Sink To) {
                pub int Go() {
                    eprint("arm")
                    send To.Take(Box("x"))
                    return 1
                }
            }

            s = spawn Sender(spawn Sink())
            print(s.Go())

            """;
        // the error line names the line of the copy's own frame in the runtime's file, as of the scheduler, the call of `Burn` that did not fit
        const string at = "pub byte* CopyNext() {\n";
        var run = BuildAndRun(text, Burning("runtime/common/runtime.kz", at), "actor-fault-copy");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], $"error: stack-overflow at runtime.kz:{BurnLine("runtime/common/runtime.kz", at)}\n");
    }

    /// <summary>
    /// The edits of the runtime that make a stack fault in the list of the heaps that messages are built in, the place <paramref name="at"/>
    /// of <c>Listed</c> or <c>Unlisted</c>, which <c>MessageHeapNew</c> and <c>MessageHeapFree</c> call, between two of its writes:
    /// <c>Burn</c> put in before <c>MessageHeapNew</c>, its armed call before that place, and <c>Print</c> armed.
    /// </summary>
    private static (string Path, string Old, string New)[] HeapListBurn(string at) =>
    [
        ("runtime/common/runtime.kz", "pub byte* MessageHeapNew() {\n", Burn + "pub byte* MessageHeapNew() {\n"),
        ("runtime/common/runtime.kz", at, "            BurnArmed()\n" + at),
        Armed("BurnArm()"),
    ];

    [Theory]
    [InlineData("            *Ptr<byte*>(first + 16) = *Ptr<byte*>(running + 320)\n")]
    [InlineData("            if after != null {\n                *Ptr<byte*>(std.HeapFirst(after) + 8) = before\n            }\n        }\n    }\n\n    sys.Left()\n}\n")]
    public void A_stack_fault_inside_the_list_of_the_heaps_of_messages_on_an_actor_s_stack_ends_the_process(string at)
    {
        // O11: `MessageHeapNew` and `MessageHeapFree` link and unlink the heap of a message whose argument waits among the heaps being
        // built, with calls between their writes, and count as inside the scheduler while they do, as `Enqueue` does, so that a stack fault
        // there, on the stack of `Go`, ends the process with the error line of a stack overflow and the exit code 1, and no actor restarts
        // with the list that its crash walks half changed
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Slow {
                pub int Get() => 7
            }

            actor Sink {
                mut int total = 0

                pub void Take(int k) {
                    total = total + k
                }
            }

            actor Sender(Sink To, Slow From) {
                pub int Go() {
                    eprint("arm")
                    send To.Take(From.Get())
                    return 1
                }
            }

            s = spawn Sender(spawn Sink(), spawn Slow())
            print(s.Go())

            """;
        // the error line names the scheduler's own line where the fault is, the call of `Burn` that did not fit, in the edited runtime's file
        var edits = HeapListBurn(at);
        var run = BuildAndRun(text, ListTests.Edited(edits), "actor-fault-heap-list");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], $"error: stack-overflow at runtime.kz:{LineOf("runtime/common/runtime.kz", edits, "    return Burn(n - 1) * 3 + n")}\n");
    }

    /// <summary>
    /// The Kurz of a check of the runtime's own code in a test build that fires: a product past the range of a <c>long</c>, armed by the
    /// program's <c>eprint("arm")</c> for one call.
    /// </summary>
    private const string Check = """
        thread static mut bool checkNext = false

        pub void CheckArm() {
            raw {
                checkNext = true
            }
        }

        void CheckArmed() {
            mut bool armed = false
            mut long n = 2
            raw {
                armed = checkNext
                checkNext = false
            }

            if armed && n * 4611686018427387904 == 7 {
                raw {
                    checkNext = true
                }
            }
        }


        """;

    /// <summary>The line of <see cref="Check"/> whose product passes the range of a <c>long</c>, the place of its check.</summary>
    private const string CheckLine = "    if armed && n * 4611686018427387904 == 7 {";

    [Fact]
    public void A_check_of_the_runtime_s_own_code_inside_the_scheduler_on_an_actor_s_stack_ends_the_process()
    {
        // chapter 12 and O11: in a test build a check of the runtime's own code that fires inside `Enqueue`, where the count of the entries
        // under way is 1, on the stack of `Go`, ends the process with its error line and the exit code 1, as a stack fault there does,
        // and is no crash of `Sender` that restarts it with the scheduler's lists half changed; the error line names the scheduler's own
        // line where the check is, the product in `CheckArmed`, in the edited runtime's common file, which a check names by its path in the
        // repository, since O11 reads the runtime of the rule of the innermost frame outside it as its primitives, and not the allocator
        // or the scheduler, whose own line it gives for a fault there, as the tests of a stack fault inside the scheduler hold it
        const string text = """
            actor Sink {
                mut int total = 0

                pub void Take(int k) {
                    total = total + k
                }
            }

            actor Sender(Sink To) {
                pub int Go() {
                    eprint("arm")
                    send To.Take(1)
                    return 1
                }
            }

            s = spawn Sender(spawn Sink())
            print(s.Go())

            """;
        const string at = "pub void Enqueue(long index, long number, byte* message, bool bounded, long bound, string file, int line) {\n    sys.Entered()\n";
        (string Path, string Old, string New)[] edits = [("runtime/common/runtime.kz", at, Check + at + "    CheckArmed()\n"), Armed("CheckArm()")];
        var run = BuildAndRun(text, ListTests.Edited(edits), "actor-check-scheduler");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], $"error: overflow at runtime/common/runtime.kz:{LineOf("runtime/common/runtime.kz", edits, CheckLine)}\n");
    }

    [Fact]
    public void A_check_of_the_runtime_s_own_code_where_no_entry_is_under_way_on_an_actor_s_stack_ends_the_process()
    {
        // chapter 12, U19: in a test build a check of the runtime's own code that fires in a primitive, `Print`, where the count of the
        // entries under way is 0, on the stack of `Go`, ends the process with its error line and the exit code 1, as one inside the
        // scheduler does, and is no crash of `Sender` that restarts it (design record, section 6, "End the process", against the crash of
        // the actor alone; the site of such a check asking for that end with no count, assumed there); the place is not held: chapter 12
        // gives a primitive's check the line of the innermost frame outside the runtime, where `Raise` writes the check's own line for
        // every check of the runtime's code, the one inside the scheduler among them, where that line is the place O11 gives
        const string text = """
            actor Sender {
                pub int Go() {
                    eprint("arm")
                    print("x")
                    return 1
                }
            }

            s = spawn Sender()
            print(s.Go())

            """;
        const string at = "pub void Print(int fd, string text, string file, int line) {\n";
        (string Path, string Old, string New)[] edits = [("runtime/common/runtime.kz", at, Check + at + "    CheckArmed()\n"), Armed("CheckArm()")];
        var run = BuildAndRun(text, ListTests.Edited(edits), "actor-check-primitive");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Assert.True(run.Code == 1 && run.Output.Count == 0 && rest.StartsWith("error: overflow at ", StringComparison.Ordinal) && rest.Count(c => c == '\n') == 1, $"exit code {run.Code}, printed [{string.Join(", ", run.Output)}], standard error: {rest}");
    }

    [Fact]
    public void A_check_of_the_standard_library_s_code_where_no_entry_is_under_way_is_its_actor_s_crash()
    {
        // O11 reads the runtime as its primitives and not a collection, the standard library's Kurz code above them, so a check of that
        // code in a test build, in `Find`, where the count of the entries under way is 0, is no check of the runtime's own code: it is
        // the crash of `Sender`, at the check's own line, as a check of the program's code is, whose wait of the top-level code gets
        // `callee-crashed` (U20), as Rust's standard library panics in the thread that called it (design record, section 6, assumed there)
        const string text = """
            actor Sender {
                pub int Go() {
                    eprint("arm")
                    print("abc".Contains("b"))
                    return 1
                }
            }

            s = spawn Sender()
            print(s.Go())

            """;
        const string at = "pub long Find(byte* hay, long n, byte* needle, long m) {\n";
        (string Path, string Old, string New)[] edits = [("std/std.kz", at, Check + at + "    CheckArmed()\n"), Armed("std.CheckArm()")];
        var run = BuildAndRun(text, ListTests.Edited(edits), "actor-check-std");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal([$"Sender#1 crashed: overflow at std/std.kz:{LineOf("std/std.kz", edits, CheckLine)}"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:10\n");
    }

    [Fact]
    public void The_count_of_the_entries_into_the_allocator_and_the_scheduler_is_0_again_at_the_end()
    {
        // O11: every entry lowers the count it raised and each side of a switch puts its own back, through spawns, sends, calls of a chain,
        // a session of two copies, a list grown past the allocator's classes, two crashes and their restarts, so that the program's end,
        // which writes the count here, finds it 0
        const string text = """
            class Box(string Text)

            actor Store {
                mut List<string> items = []

                pub void Put(Box b, string s) {
                    items.Add(b.Text)
                    items.Add(s)
                }

                pub int Fail(int k) {
                    if k > 0 {
                        throw "no {k}"
                    }

                    return k
                }

                pub int Count() => items.Count
            }

            actor Front(Store To) {
                pub int Pass(int k) => To.Fail(k) + 1

                pub void Try(int k) {
                    print(Pass(k))
                }
            }

            store = spawn Store()
            front = spawn Front(store)
            for i in 0..<100 {
                send store.Put(Box("b{i}"), "s{i}")
            }

            mut List<long> xs = []
            for i in 0..<40000 {
                xs.Add(long(i))
            }

            print(xs.Count)
            send front.Try(1)
            print(front.Pass(0))
            print(store.Count())

            """;
        var counted = ListTests.Edited(("runtime/common/runtime.kz", "pub void Finish() {\n    EndActors()\n",
            "pub void Finish() {\n    EndActors()\n    Print(2, \"entries {sys.Inside(0)}\", \"\", 0)\n"));
        var run = BuildAndRun(text, counted, "actor-entries-counted");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Store#1 crashed: thrown \"no 1\" at program.kz:13", "Front#2 crashed: callee-crashed at program.kz:23"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["40000", "1", "0"], "entries 0\n");
    }

    [Fact]
    public void A_heap_the_system_refuses_at_a_crash_ends_the_process_with_allocation_failed_of_the_0_bytes_asked()
    {
        // O11, U21: the new heap the crash of `Fail` asks for its kept inbox has a first block of no bytes, which the error line of
        // `allocation-failed` names as the size asked for, as a refused actor's heap names the bytes of its record and state, where the
        // system refuses that heap
        const string text = """
            actor Thrower {
                pub int Fail(int k) {
                    eprint("arm")
                    if k > 0 {
                        throw "no {k}"
                    }

                    return k
                }
            }

            t = spawn Thrower()
            print(t.Fail(1))

            """;
        var refusing = ListTests.Edited(("std/std.kz", "pub byte* HeapNew(long first) {\n    sys.Entered()\n", """
            thread static mut bool refuseNext = false

            pub void RefuseArm() {
                raw {
                    refuseNext = true
                }
            }

            pub byte* HeapNew(long first) {
                mut bool refused = false
                raw {
                    refused = refuseNext && first == 0
                }

                if refused {
                    return null
                }

                sys.Entered()

            """), Armed("std.RefuseArm()"));
        var run = BuildAndRun(text, refusing, "actor-crash-heap-refused");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: allocation-failed (0 bytes)\n");
    }

    [Fact]
    public async Task A_print_the_system_refuses_inside_a_message_restarts_the_actor_and_its_waiting_caller_gets_callee_crashed()
    {
        // U19, U20, U24: `print-failed` raised in `Spam`, once the reader of standard output is gone, is the crash of `Printer`, whose log
        // line names it at its line; `Ask`, which waits on it, gets `callee-crashed` at its call's line, its own crash; both restart, `Ping`
        // runs from the kept inbox, and `Count` finds the fresh state
        const string text = """
            actor Printer {
                mut int calls = 0

                pub int Spam() {
                    calls = calls + 1
                    mut i = 0
                    while i < 100000000 {
                        print("line")
                        i = i + 1
                    }

                    return i
                }

                pub int Count() {
                    calls = calls + 1
                    return calls
                }
            }

            actor Caller(Printer To) {
                pub void Ask() {
                    eprint(To.Spam())
                }

                pub int Ping() => 7
            }

            p = spawn Printer()
            c = spawn Caller(p)
            send c.Ask()
            eprint(c.Ping())
            eprint(p.Count())

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], StoodIn(print: false), true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var info = new System.Diagnostics.ProcessStartInfo(toolchain.Build(compilation.Program, "actor-print-failed"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        info.Environment["KURZ_TRACE"] = "0";
        using var process = System.Diagnostics.Process.Start(info)!;
        try
        {
            process.StandardOutput.Close(); // the reader is gone: a write is refused (a closed pipe)
            var error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "the program did not end once its output was closed");
            var (crashes, rest) = Logged(await error);
            Assert.Equal(["Printer#1 crashed: print-failed at program.kz:8", "Caller#2 crashed: callee-crashed at program.kz:23"], crashes);
            Assert.Equal((0, "7\n1\n"), (process.ExitCode, rest));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    [Fact]
    public void A_cut_inside_a_character_in_a_message_restarts_the_actor_and_its_waiting_caller_gets_callee_crashed()
    {
        // U19, U20, U24: `not-char-boundary` raised in `Cut` is the crash of `Cutter`, whose log line names it with the text below its error
        // line at its line; `Ask`, which waits on it, gets `callee-crashed` at its call's line, its own crash; both restart, `Ping` runs from
        // the kept inbox, and `Count` finds the fresh state
        const string text = """
            actor Cutter {
                mut int calls = 0

                pub string Cut(string s) {
                    calls = calls + 1
                    return s.Substring(0, 2)
                }

                pub int Count() {
                    calls = calls + 1
                    return calls
                }
            }

            actor Caller(Cutter To) {
                pub void Ask() {
                    print(To.Cut("hä"))
                }

                pub int Ping() => 7
            }

            t = spawn Cutter()
            c = spawn Caller(t)
            send c.Ask()
            print(c.Ping())
            print(t.Count())

            """;
        var run = BuildAndRun(text, StoodIn(print: false), "actor-not-char-boundary");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Cutter#1 crashed: not-char-boundary \"byte 2 is inside a code point\" at program.kz:6", "Caller#2 crashed: callee-crashed at program.kz:17"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["7", "1"], "");
    }

    // ---- the block of `on crash` (U28 to U34) ------------------------------------------------------------------------------------

    [Fact]
    public void A_block_at_the_root_reads_the_crash_and_restarts_its_child()
    {
        // U28, U30, U33: the top-level code's wait runs the block, which reads the thrown value as its case of `Reason`, the place,
        // the chain's 32 hexadecimal digits (S4 of comment 6907441402) and a local of the top-level code, taken by value (S7 of that
        // comment); `restart` restarts the child on the copy of its argument that its supervisor keeps where the policy can restart, a
        // block among them (section 6, 2026-11-13, comment 6851105334), which serves the call that waits, and the log line is written at
        // the decision (S10 of comment 6907441402)
        const string text = """
            data Lost(int Code)

            actor Worker(string Name) {
                pub void Fail(int code) {
                    throw Lost(code)
                }

                pub string Ping() => Name
            }

            label = "worker"
            w = spawn Worker("kept") on crash (c) {
                match c.Reason {
                    Lost l => print("{label} lost {l.Code} at {c.Place.File}:{c.Place.Line}")
                    RuntimeError e => print("runtime {e.Detail}")
                }

                print(c.Chain)
                restart
            }
            send w.Fail(7)
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-root-restart");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: thrown \"Lost { Code = 7 }\" at program.kz:5"], crashes);
        Ran((run.Code, run.Output.Where((_, i) => i != 1).ToList(), rest), 0, ["worker lost 7 at program.kz:5", "kept"], "");
        Assert.Matches("^[0-9a-f]{32}$", run.Output[1]);
    }

    [Fact]
    public void A_block_reads_the_case_a_bare_throw_of_the_arm_else_over_a_void_success_threw()
    {
        // O7 with U28, U30: the arm `else` over a union whose success is `void` throws the case that reached it (issue 612), which the
        // block reads as its case of `Reason`, each of two cases by its tag, as it reads a case the arm of its type threw; the child
        // restarts after each
        const string text = """
            data Lost(int Code)
            data Gone(string Why)

            void | Lost | Gone Find(int id) {
                if id == 1 {
                    return Lost(7)
                }
                if id == 2 {
                    return Gone("away")
                }
            }

            actor Worker(string Name) {
                pub void Fail(int id) {
                    Find(id) else {
                        else => throw
                    }
                }

                pub string Ping() => Name
            }

            w = spawn Worker("kept") on crash (c) {
                match c.Reason {
                    Lost l => print("lost {l.Code} at {c.Place.Line}")
                    Gone g => print("gone {g.Why} at {c.Place.Line}")
                    RuntimeError e => print("runtime {e.Detail}")
                }

                restart
            }
            send w.Fail(1)
            print(w.Ping())
            send w.Fail(2)
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-else-void-reason");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: thrown \"Lost { Code = 7 }\" at program.kz:16", "Worker#1 crashed: thrown \"Gone { Why = away }\" at program.kz:16"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["lost 7 at 16", "kept", "gone away at 16", "kept"], "");
    }

    [Fact]
    public void A_block_runs_after_the_supervisors_current_message_and_before_its_queued_ones_in_crash_order()
    {
        // U28, U31, U34: each block is a message of `Boss`, which runs once `Go`, parked in its call to `Other`, ended, before the
        // `Queued` that `Go` sent itself, the first crash's first; each reads and assigns `Boss`'s field, `stop` ends its child, whose
        // later call gets `actor-gone`, and `restart` restarts the other; each log line at the decision (S7, S10, S11 of comment
        // 6907441402)
        const string text = """
            actor Other {
                pub int Ping() => 1
            }

            actor Worker(string Name) {
                pub void Fail() {
                    throw "{Name} fails"
                }

                pub int Ping() => 2
            }

            actor Boss {
                mut int crashes = 0

                pub Worker Go(Other other) {
                    first = spawn Worker("first") on crash (c) {
                        crashes = crashes + 1
                        match c.Reason {
                            string s => print("block {s} {crashes}")
                            RuntimeError e => print(e.Detail)
                        }

                        stop
                    }

                    second = spawn Worker("second") on crash (c) {
                        crashes = crashes + 1
                        match c.Reason {
                            string s => print("block {s} {crashes}")
                            RuntimeError e => print(e.Detail)
                        }

                        restart
                    }

                    send this.Queued()
                    send first.Fail()
                    send second.Fail()
                    print(other.Ping())
                    print("go ends {crashes}")
                    return first
                }

                pub void Queued() {
                    print("queued {crashes}")
                }

                pub int Crashes() => crashes
            }

            o = spawn Other()
            b = spawn Boss()
            w = b.Go(o)
            print(b.Crashes())
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-order");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#3 crashed: thrown \"first fails\" at program.kz:7", "Worker#4 crashed: thrown \"second fails\" at program.kz:7"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["1", "go ends 0", "block first fails 1", "block second fails 2", "queued 2", "2"], "error: actor-gone at program.kz:56\n");
    }

    [Fact]
    public void A_block_posted_to_a_supervisor_whose_constructor_waits_runs_once_its_spawn_returned()
    {
        // U17, U28: `Boss`'s constructor spawns `Worker`, whose crash posts its block to `Boss` while that constructor waits on `Other`;
        // a thread that takes `Boss`'s entry then finds it BUILDING in its state word and leaves it NOTIFIED, and `ActorBuilt` queues it
        // again at the constructor's end, so that the block runs after `spawn Boss(o)` returned, in the top-level code's wait after it, as
        // no message starts before its actor's `spawn` returns (U17, assumed in section 6), and its log line is written at its decision
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "fails"
                }
            }

            actor Other {
                pub int Ping() => 1
            }

            Worker Started() {
                w = spawn Worker() on crash (c) {
                    print("block")
                    stop
                }

                send w.Fail()
                return w
            }

            int Shown(int n) {
                print("built {n}")
                return n
            }

            actor Boss(Other O) {
                Worker w = Started()
                int first = O.Ping()
                int second = Shown(O.Ping())

                pub int Sum() => first + second
            }

            o = spawn Other()
            b = spawn Boss(o)
            print("spawned")
            print(o.Ping())
            print(b.Sum())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-building");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#3 crashed: thrown \"fails\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["built 1", "spawned", "block", "1", "2"], "");
    }

    [Fact]
    public void A_building_actor_woken_and_then_ended_by_its_spawners_crash_frees_its_heap_when_the_scheduler_takes_it_off()
    {
        // U13, U17, section 6 (comment 6919351399): `Boss`'s constructor, which runs in `Spawner`'s message, spawns `Worker`, whose crash
        // posts its block to `Boss` and so puts an entry of `Boss` in the run queue while its constructor waits; `Esc`'s crash escalates to
        // `Spawner`, whose fall ends `Boss` with that entry still queued and BUILDING still in its state word, since its constructor never
        // ends; the scheduler's take of that entry then finds the actor ended and frees its first page, as it frees one that no constructor
        // holds, so that the heaps drop by one, `Spawner`'s, as they do where `Boss` was never woken; under the allocator that makes every
        // freed block inaccessible
        const string worker = """
            actor Worker {
                pub void Fail() {
                    throw "fails"
                }
            }

            Worker Started() {
                w = spawn Worker() on crash (c) {
                    print("block")
                    stop
                }

                send w.Fail()
                return w
            }


            """;
        const string text = """
            actor Esc {
                pub void Boom() {
                    throw "boom"
                }
            }

            actor Other {
                pub int Ping() => 1
            }

            int Armed(Esc e) {
                send e.Boom()
                return 0
            }

            actor Boss(Other O, Esc E) {
            WOKEN    int armed = Armed(E)
                int first = O.Ping()

                pub int Sum() => armed + first
            }

            actor Spawner(Other O) {
                mut Boss? boss = null

                pub void Make() {
                    e = spawn Esc() on crash escalate
                    boss = spawn Boss(O, e)
                }
            }

            o = spawn Other()
            t = spawn Other()
            s = spawn Spawner(o) on crash stop
            before = HttpServer.Heaps()
            send s.Make()
            print(t.Ping())
            print(t.Ping())
            print(t.Ping())
            print(before - HttpServer.Heaps())

            """;
        var woken = BuildAndRun(worker + text.Replace("WOKEN", "    Worker w = Started()\n"), ListTests.Edited([.. ListTests.Fence, .. HeapCount]), "actor-building-ended");
        var never = BuildAndRun(worker + text.Replace("WOKEN", "    Worker? w = null\n"), ListTests.Edited([.. ListTests.Fence, .. HeapCount]), "actor-building-ended-control");
        var (crashes, rest) = Logged(woken.Error);
        Assert.Equal(["Spawner#3 crashed: thrown \"boom\" at program.kz:19"], crashes);
        Assert.Equal(crashes, Logged(never.Error).Crashes);
        Ran((woken.Code, woken.Output, rest), 0, ["1", "1", "1", "1"], "");
        Ran((never.Code, never.Output, Logged(never.Error).Others), 0, ["1", "1", "1", "1"], "");
    }

    [Fact]
    public void A_crash_in_the_release_of_a_messages_arguments_answers_the_waiting_call_callee_crashed()
    {
        // U12, U19, U20: the message's function lets go of the method's arguments before it takes the reply off the message by its swap,
        // so that a crash in that release is the callee's crash before it answered, which takes the reply and answers the waiting call
        // `callee-crashed` at its line, for a method with no result as for one with a result; here the release of a value of 200 nested
        // data types, each beside a string, whose emitted helpers take each level by value, about 320 KB of frames, overflows the 64 KiB
        // stack of `Sink`, and fits its default 2 MiB; a release after the swap left the waiting call of the method with no result
        // unanswered, the top-level code's wait ending in `deadlock`; under the allocator that makes every freed block inaccessible
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        var types = "data D0(string S)\n" + string.Concat(Enumerable.Range(1, 200).Select(k => $"data D{k}(D{k - 1} A, string B)\n"))
            + "\nD200 Make(int seed) {\n    D0 v0 = D0(\"x{seed}\")\n"
            + string.Concat(Enumerable.Range(1, 200).Select(k => $"    D{k} v{k} = D{k}(v{k - 1}, \"y{{seed}}\")\n")) + "    return v200\n}\n\n";
        const string none = "    pub void Take(D200 d) {\n        n = n + 1\n    }\n";
        const string some = "    pub int Take(D200 d) {\n        n = n + 1\n        return n\n    }\n";
        string Program(string clause, string take, string call) => types + $"actor Sink{clause} {{\n    mut int n = 0\n\n{take}\n    pub int Get() => n\n}}\n\n"
            + $"s = spawn Sink()\nvalue = Make(7)\nprint(\"before\")\n{call}\nprint(\"answered\")\nprint(s.Get())\n";

        Ran(BuildAndRun(Program(string.Empty, none, "s.Take(value)"), ListTests.Fenced, "actor-release-fits"), 0, ["before", "answered", "1"], "");
        foreach (var (take, call, name) in new[] { (none, "s.Take(value)", "actor-release-crash"), (some, "print(s.Take(value))", "actor-release-crash-result") })
        {
            var text = Program(" stack 64kb", take, call);
            var run = BuildAndRun(text, ListTests.Fenced, name, trace: null);
            var (crashes, rest) = Logged(run.Error);
            Assert.True(crashes is [var crash] && crash.StartsWith("Sink#1 crashed: stack-overflow", StringComparison.Ordinal), string.Join("; ", crashes) + rest);
            Assert.Equal((1, "before"), (run.Code, string.Join(", ", run.Output)));
            Assert.StartsWith($"error: callee-crashed at program.kz:{text[..text.IndexOf("s.Take(value)", StringComparison.Ordinal)].Count(c => c == '\n') + 1}\n", rest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_throw_in_the_block_crashes_the_supervisor_and_the_child_stays_stopped()
    {
        // U32: the block's throw is `Boss`'s crash, after the child's log line as for a `stop` (S10 of comment 6907441402); the crash ends
        // the child, whose call waiting in its inbox gets `actor-gone`, and the root restarts `Boss`
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 2
            }

            actor Boss {
                pub Worker Go() {
                    kid = spawn Worker() on crash (c) {
                        throw "block fails"
                    }

                    send kid.Fail()
                    return kid
                }
            }

            b = spawn Boss()
            w = b.Go()
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-throw");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#2 crashed: thrown \"worker fails\" at program.kz:3", "Boss#1 crashed: thrown \"block fails\" at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: actor-gone at program.kz:22\n");
    }

    [Fact]
    public void A_block_that_escalates_to_the_root_ends_the_process_with_the_childs_error_line_and_trace()
    {
        // U34, U25: `escalate` in the top-level code's block passes the child's crash up whole: the process ends with the child's error
        // line, its thrown text, and the trace of the stack the child raised on, kept while the block decided, and no log line
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 1
            }

            w = spawn Worker() on crash (c) {
                print("deciding")
                escalate
            }
            print(w.Ping())
            send w.Fail()
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-escalate-root", trace: null);
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        var error = "error: thrown at program.kz:3:\nworker fails\n" + (OperatingSystem.IsLinux() ? "  at Worker.Fail in program.kz:3\n" : string.Empty);
        Ran((run.Code, run.Output, rest), 1, ["1", "deciding"], error);
    }

    [Fact]
    public void A_block_that_escalates_crashes_the_supervisor_with_the_childs_crash()
    {
        // U34, U25: `escalate` in `Boss`'s block is `Boss`'s crash with the child's id, place and text, whose log line names `Boss`, and
        // none the child; the root restarts `Boss`, fresh, and the child ended with it
        const string text = """
            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 2
            }

            actor Boss {
                mut int made = 0

                pub Worker Go() {
                    made = made + 1
                    kid = spawn Worker() on crash (c) {
                        escalate
                    }

                    send kid.Fail()
                    return kid
                }

                pub int Made() => made
            }

            b = spawn Boss()
            w = b.Go()
            print(b.Made())
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-escalate");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Boss#1 crashed: thrown \"worker fails\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["0"], "error: actor-gone at program.kz:28\n");
    }

    [Fact]
    public void The_block_after_an_actors_name_decides_for_each_of_its_spawns()
    {
        // U27, U31: the type's block, which reads `c` and what a top-level function reads (S6 of comment 6907441402), runs as the
        // block at the spawn does, here in the top-level code's wait; a block at a `spawn` of the type wins over it (2026-11-15, #519,
        // comment 6902210646, Q3)
        const string text = """
            actor Worker on crash (c) {
                print("type's block at {c.Place.Line}")
                restart
            } {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 1
            }

            w = spawn Worker()
            send w.Fail()
            print(w.Ping())
            v = spawn Worker() on crash (c) {
                print("spawn's block at {c.Place.Line}")
                restart
            }
            send v.Fail()
            print(v.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-type");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: thrown \"worker fails\" at program.kz:6", "Worker#2 crashed: thrown \"worker fails\" at program.kz:6"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["type's block at 6", "1", "spawn's block at 6", "1"], "");
    }

    [Fact]
    public void A_block_pending_where_the_top_level_code_ends_is_dropped_with_no_log_line()
    {
        // U33: the top-level code waits on the call that crashes the child, so that its wait ends with `callee-crashed` before it runs
        // the block the crash left pending; the end drops the block, which decided nothing and wrote no line (U13; issue 519, comment
        // 6919351399, "Root's wait runs it")
        const string text = """
            actor Worker {
                pub int Fail() {
                    throw "worker fails"
                }
            }

            w = spawn Worker() on crash (c) {
                print("never")
                restart
            }
            print(w.Fail())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-dropped");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:11\n");
    }

    [Fact]
    public void Memory_stays_flat_over_ten_thousand_crashes_a_block_restarts()
    {
        // U30, U34: each crash's copy into the top-level code's heap, its message, its `c` and the stack the child raised on are let go
        // of at the decision, so that 10,000 crashes after 1,000 that warm the pools grow the resident memory by less than 256 KiB; the
        // pauses stood in for
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        const string program = """
            long | IoError Resident() {
                bytes = File.ReadAll("/proc/self/status")
                text = string.FromUtf8(bytes) else {
                    InvalidUtf8 => ""
                }

                for line in text.Split('\n') {
                    if line.StartsWith("VmRSS:") {
                        return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                            ParseError => -1
                        }
                    }
                }

                return -1
            }

            data Lost(string Why)

            actor Flaky {
                pub void Fail(string s) {
                    throw Lost("fails at {s}")
                }

                pub int Ping() => 1
            }

            f = spawn Flaky() on crash (c) {
                match c.Reason {
                    Lost l => {
                        if l.Why.Bytes.Count == 0 {
                            stop
                        }
                    }
                    RuntimeError e => stop
                }

                restart
            }

            mut total = 0
            for i in 0..<1000 {
                send f.Fail("warm {i}")
                total = total + f.Ping()
            }

            before = Resident() else {
                IoError e => -1
            }

            for i in 0..<10000 {
                send f.Fail("run {i}")
                total = total + f.Ping()
            }

            after = Resident() else {
                IoError e => -1
            }

            print(total)
            print(after - before)
            print(before)

            """;
        var run = BuildAndRun(program, StoodIn(print: false), "actor-block-memory");
        Assert.Equal((0, 11000), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("11000", run.Output[0]);
        var grown = long.Parse(run.Output[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(grown < 256, $"10,000 crashes a block restarts grow the resident memory by {grown} KiB");

        // the bound is read against a reading: `Resident` gives -1 where it reads none, which a growth of 0 would hide
        Assert.True(long.Parse(run.Output[2], System.Globalization.CultureInfo.InvariantCulture) > 0, $"the resident memory before the run read {run.Output[2]}");
    }

    /// <summary>
    /// The function a program of a test of the memory of `per node` actors reads its resident memory with, in KiB, -1 where it reads
    /// none, and its probe, an actor whose answer runs after every message queued before it on the one thread (U35).
    /// </summary>
    private const string PerNodeMemory = """
        long | IoError Resident() {
            bytes = File.ReadAll("/proc/self/status")
            text = string.FromUtf8(bytes) else {
                InvalidUtf8 => ""
            }

            for line in text.Split('\n') {
                if line.StartsWith("VmRSS:") {
                    return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                        ParseError => -1
                    }
                }
            }

            return -1
        }

        actor Probe {
            pub int Ping() => 0
        }

        p = spawn Probe()

        """;

    /// <summary>
    /// The program of a test of the memory of `per node` actors: <paramref name="actors"/>, then <paramref name="round"/> run 1,000 times to
    /// warm the pools and 10,000 times between two readings of the resident memory, with <c>i</c> its count, adding to <c>total</c>; it
    /// prints the total, then the two readings.
    /// </summary>
    private static string PerNodeRounds(string actors, string round) => actors + PerNodeMemory + $$"""
        mut total = 0
        for i in 0..<1000 {
        {{round}}
        }

        before = Resident() else {
            IoError e => -1
        }

        for i in 0..<10000 {
        {{round}}
        }

        after = Resident() else {
            IoError e => -1
        }

        print(total)
        print(before)
        print(after)

        """;

    [Theory]
    [InlineData("restart", "", "    send Flaky.Fail(\"r {i}\")\n    total = total + Flaky.Ping() + p.Ping()", 1)]
    [InlineData("stop", " on crash stop", "    total = total + Flaky.Ping()\n    send Flaky.Fail(\"s {i}\")\n    total = total + p.Ping()", 1)]
    [InlineData("limit", " on crash restart max 1 in 1min", "    send Flaky.Fail(\"a {i}\")\n    total = total + Flaky.Ping()\n    send Flaky.Fail(\"b {i}\")\n    total = total + p.Ping()", 2)]
    public void Memory_stays_flat_over_ten_thousand_crashes_of_a_per_node_actor(string name, string policy, string round, int crashes)
    {
        // U54 (assumed: T30 of issue 519, comment 6983903100): a restart in place frees the crashed instance's heap whole (U21), and a
        // crash under `stop` or past `max N in` ends the actor and frees its heap, its record and its slot, so that the message after it
        // starts a fresh one in a heap of its own; 10,000 rounds after 1,000 that warm the pools grow the resident memory by less than
        // 256 KiB, a tenth of what a page of 256 bytes kept per round would add; the pauses stood in for, and `Probe`'s answer runs after
        // the crash that ends the actor, on the one thread (U35)
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        var actors = $$"""
            actor Flaky per node{{policy}} {
                mut List<string> seen = []

                pub void Fail(string s) {
                    seen.Add(s)
                    throw "flaky fails at {s}"
                }

                pub int Ping() => seen.Count + 1
            }


            """;
        var run = BuildAndRun(PerNodeRounds(actors, round), StoodIn(print: false), $"actor-per-memory-{name}");
        Assert.Equal((0, 11000 * crashes), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("11000", run.Output[0]);
        var grown = Grown(run.Output[1], run.Output[2]);
        Assert.True(grown < 256, $"10,000 rounds of a `per node` actor under {name} grow the resident memory by {grown} KiB");
    }

    [Fact]
    public void Memory_stays_flat_over_ten_thousand_starts_of_a_per_node_actor_that_throw()
    {
        // U52 (assumed: T29 of issue 519, comment 6983903100): the defaults of `Cache` throw on `User`'s stack, which is `User`'s crash,
        // and its stop ends `Cache`, its child while it is built, freeing the heap its defaults allocated in, its record and its slot, so
        // that 10,000 such starts after 1,000 that warm the pools grow the resident memory by less than 256 KiB
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        const string actors = """
            List<string> Words(string s) {
                words = ["start", s]
                throw "start fails at {words[1]}"
            }

            actor Cache per node {
                List<string> words = Words("cache")

                pub int Get() => words.Count
            }

            actor User {
                pub void Use() {
                    print(Cache.Get())
                }
            }


            """;
        var run = BuildAndRun(PerNodeRounds(actors, "    u = spawn User() on crash stop\n    send u.Use()\n    total = total + p.Ping() + 1"), StoodIn(print: false), "actor-per-memory-start");
        Assert.Equal((0, 11000), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("11000", run.Output[0]);
        var grown = Grown(run.Output[1], run.Output[2]);
        Assert.True(grown < 256, $"10,000 starts of a `per node` actor that throw grow the resident memory by {grown} KiB");
    }

    [Fact]
    public void A_fresh_per_node_actor_past_its_limit_pauses_first_and_counts_its_window_from_its_start()
    {
        // U54 (assumed: a reading of the build, proposed with this record and not put to the owner): the second crash of each round
        // passes `max 1 in 1min` and ends `Flaky`, and the fresh one the round after starts takes its first crash with a first pause, 100 ms
        // within 20% either way, and restarts in place, the crash of the actor before it no longer in its window, where the pause the
        // ended actor left would double and its count would end the fresh one at its first crash; read through the clock of the pauses,
        // which this runtime stands in for
        const string text = """
            actor Flaky per node on crash restart max 1 in 1min {
                pub void Fail() {
                    throw "flaky"
                }

                pub int Ping() => 1
            }

            actor Probe {
                pub int Ping() => 9
            }

            p = spawn Probe()
            for i in 0..<3 {
                send Flaky.Fail()
                print(Flaky.Ping())
                send Flaky.Fail()
                print(p.Ping())
            }

            """;
        var run = BuildAndRun(text, StoodIn(print: true), "actor-per-fresh-pause");
        Assert.Equal((0, 6), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal(9, run.Output.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(("1", "9"), (run.Output[(3 * i) + 1], run.Output[(3 * i) + 2]));
            var slept = long.Parse(run.Output[3 * i]["pause ".Length..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(slept >= 80 && slept <= 120, $"round {i}: a pause of {slept} ms where 100 ms within 20% was expected");
        }
    }

    [Fact]
    public void A_per_node_actors_defaults_allocate_in_its_heap_after_a_wait_inside_a_message()
    {
        // section 3, U52 (assumed: T29 of issue 519, comment 6983903100): the defaults of a `per node` actor run on its first caller's
        // stack in its own heap, as a `spawn`'s run (U3); a default whose call waits, inside `Owner`'s message, parks that message, and the
        // runtime's `Wait` serves the heap of the actor being built again, so that the string the default makes after the call is a block of
        // that heap, as it is for `Top`, which the top-level code starts
        const string program = """
            actor Sizes per node {
                pub int Size() => 3
            }

            actor Top per node {
                string name = "top {Sizes.Size()}"

                pub string Name() => name

                pub long Live() => HttpServer.HeapLive()
            }

            actor Pool per node {
                string name = "pool {Sizes.Size()}"

                pub string Name() => name

                pub long Live() => HttpServer.HeapLive()
            }

            actor Owner {
                pub long Started() => Pool.Live()
            }

            print(Top.Live())
            print(spawn Owner().Started())

            """;
        Ran(BuildAndRun(program, HeapBlocks.Value, "actor-per-wait-heap"), 0, ["2", "2"], "");
    }

    [Fact]
    public void Every_per_node_actors_heap_is_freed_whole_at_the_end_of_the_program()
    {
        // U53 (assumed: said back with T27 to T33 of issue 519, comment 6983903100): a `per node` actor ends with the process, its heap
        // freed with every actor's (U13; ListTests.EndingActors, the runtime's `EndActors`), the one `stop` would end among them
        const string program = """
            actor Counter per node {
                pub int Get() => 1
            }

            actor Stopping per node on crash stop {
                pub int Get() => 2
            }

            print(Counter.Get() + Stopping.Get())
            before = HttpServer.Heaps()
            eprint("end actors")
            print(before - HttpServer.Heaps())

            """;
        Ran(BuildAndRun(program, ListTests.Edited([.. HeapCount, ListTests.EndingActors]), "actor-per-heaps-ended"), 0, ["3", "2"], "");
    }

    [Fact]
    public void A_per_node_actor_ended_by_a_start_that_threw_a_stop_or_its_limit_is_read_no_more_and_a_fresh_one_starts()
    {
        // U52, U54 (assumed: T29 and T30 of issue 519, comment 6983903100): `Failing`'s defaults throw on `User`'s stack, whose stop ends
        // it while it is built, `Counter`'s crash under `stop` ends it, and `Limited`'s second crash passes its limit and ends it; each
        // message after that finds no actor by the cell's slot and number and starts a fresh one, reading nothing of the record its end
        // freed; under the allocator that makes every freed block inaccessible, with the word of the heap of a record its end frees
        // written 16 before the free (PoisonedEnds); each start's crash names the line of the throw, where T29's words name the
        // call's (U52; assumed: T37 of issue 519, comment 6991862214, put to the owner)
        const string text = """
            List<string> Words(string s) {
                words = ["start", s]
                throw "start fails at {words[1]}"
            }

            actor Failing per node {
                List<string> words = Words("failing")

                pub int Get() => words.Count
            }

            actor Counter per node on crash stop {
                mut List<string> seen = []

                pub void Add(string s) {
                    seen.Add(s)
                }

                pub void Fail() {
                    throw "counter fails at {seen.Count}"
                }

                pub int Get() => seen.Count
            }

            actor Limited per node on crash restart max 1 in 1min {
                mut List<string> seen = []

                pub void Add(string s) {
                    seen.Add(s)
                }

                pub void Fail() {
                    throw "limited fails at {seen.Count}"
                }

                pub int Get() => seen.Count
            }

            actor User {
                pub void Use() {
                    print(Failing.Get())
                }
            }

            actor Probe {
                pub int Ping() => 9
            }

            p = spawn Probe()
            print(Counter.Get())
            send Counter.Fail()
            print(p.Ping())
            send Counter.Add("b")
            print(Counter.Get())
            send Limited.Add("a")
            send Limited.Fail()
            print(Limited.Get())
            send Limited.Fail()
            print(p.Ping())
            print(Limited.Get())
            for i in 0..<2 {
                u = spawn User() on crash stop
                send u.Use()
                print(p.Ping())
            }

            """;
        var run = BuildAndRun(text, PoisonedEnds, "actor-per-ends-poisoned");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal([
            "Counter#2 crashed: thrown \"counter fails at 0\" at program.kz:20",
            "Limited#4 crashed: thrown \"limited fails at 1\" at program.kz:34",
            "Limited#4 crashed: thrown \"limited fails at 0\" at program.kz:34",
            "User#6 crashed: thrown \"start fails at failing\" at program.kz:3",
            "User#8 crashed: thrown \"start fails at failing\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["0", "9", "1", "0", "9", "0", "9", "9"], "");
    }

    [Fact]
    public void The_thrown_value_reaches_the_block_as_a_copy_in_the_supervisors_heap()
    {
        // U30 with U12: the thrown instance, its `mut` field and its list among it, is copied into `Boss`'s heap before the child's heap
        // is freed, under the allocator that makes every freed block inaccessible, so that the block reads no block of the freed heap
        const string text = """
            class Note(mut string Why, List<int> Codes) {
                pub string Text() => Why
            }

            actor Worker {
                pub void Fail() {
                    throw Note("lost", [1, 2, 3])
                }
            }

            actor Boss {
                mut int total = 0

                pub void Go() {
                    kid = spawn Worker() on crash (c) {
                        match c.Reason {
                            Note n => {
                                total = total + n.Codes.Count
                                print("{n.Why} {n.Codes}")
                            }
                            RuntimeError e => print(e.Detail)
                        }

                        stop
                    }

                    send kid.Fail()
                }

                pub int Total() => total
            }

            b = spawn Boss()
            b.Go()
            print(b.Total())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-block-copy");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#2 crashed: thrown \"lost\" at program.kz:7"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["lost [1, 2, 3]", "3"], "");
    }

    [Fact]
    public void The_block_takes_the_variables_it_reads_after_the_spawns_arguments_ran()
    {
        // U28, U31: the block takes the variables it reads after the spawn's arguments ran, F12's left to right with the block written last
        // (section 6, the owner, 2026-11-17, #570, comment 6961064167, Q2, "After the arguments"), as the checker reads it after them,
        // so the variables it takes by value (F15) are taken after the arguments ran: `n`, which the argument `Bumped(mut n)` changes,
        // reaches the block as 1; the string it takes too, under the allocator that makes every freed block inaccessible
        const string text = """
            int Bumped(mut int n) {
                n = n + 1
                return n
            }

            actor Worker(int Start) {
                pub void Fail() {
                    throw "worker fails"
                }
            }

            actor Boss {
                mut string seen = ""

                pub void Go() {
                    mut n = 0
                    who = "kid"
                    w = spawn Worker(Bumped(mut n)) on crash (c) {
                        seen = "{who} {n}"
                        stop
                    }

                    send w.Fail()
                }

                pub string Seen() => seen
            }

            b = spawn Boss()
            b.Go()
            print(b.Seen())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-block-after-arguments");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#2 crashed: thrown \"worker fails\" at program.kz:8"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["kid 1"], "");
    }

    [Fact]
    public void A_thrown_null_reaches_the_block_as_the_null_case_of_its_reason()
    {
        // U30: a `throw` of a nullable value whose value is `null` puts the `null` case in the child's `Reason`, which holds no value and
        // has none to copy into the supervisor's heap; the block reads it, and the restarted child takes the call that waited
        const string text = """
            data Lost(int Code)

            actor Worker {
                pub void Fail(Lost? lost) {
                    throw lost
                }

                pub int Ping() => 1
            }

            w = spawn Worker() on crash (c) {
                match c.Reason {
                    Lost l => print("lost {l.Code}")
                    null => print("null")
                    RuntimeError e => print(e.Kind)
                }

                restart
            }
            send w.Fail(null)
            send w.Fail(Lost(7))
            print(w.Ping())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-block-null");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: thrown \"null\" at program.kz:5", "Worker#1 crashed: thrown \"Lost { Code = 7 }\" at program.kz:5"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["null", "lost 7", "1"], "");
    }

    [Fact]
    public void A_block_pending_in_a_supervisor_that_crashes_is_dropped_and_its_child_ended()
    {
        // U34: the block waits in `Boss`'s inbox while `Go` runs on, and `Go`'s throw drops it with no line and ends its child, the
        // inbox's call kept for `Boss`'s restart (S11 of comment 6907441402); `Other` keeps the child's reference, and a call to it after
        // the drop is `actor-gone` at its line (U13)
        const string text = """
            actor Other {
                mut List<Worker> kept = []

                pub int Keep(Worker w) {
                    kept.Add(w)
                    return 1
                }

                pub Worker Kept() => kept[0]
            }

            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 3
            }

            actor Boss {
                pub void Go(Other other) {
                    kid = spawn Worker() on crash (c) {
                        print("never")
                        restart
                    }

                    send kid.Fail()
                    print(other.Keep(kid))
                    throw "boss fails"
                }

                pub int Ping() => 2
            }

            o = spawn Other()
            b = spawn Boss()
            send b.Go(o)
            print(b.Ping())
            print(o.Kept().Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-supervisor-crash");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Boss#2 crashed: thrown \"boss fails\" at program.kz:29"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["1", "2"], "error: actor-gone at program.kz:39\n");
    }

    [Fact]
    public void A_call_sent_to_a_child_whose_block_is_pending_waits_in_its_inbox_for_the_restart()
    {
        // U34: while its block waits behind `Go`, the child takes no message, so that the `Note` that `Go` sends it waits in its kept
        // inbox and runs on the instance the block's restart constructs, whose `n` is 0 again (S11 of comment 6907441402)
        const string text = """
            actor Other {
                pub int Ping() => 1
            }

            actor Worker {
                mut int n = 0

                pub void Fail() {
                    n = 5
                    throw "worker fails"
                }

                pub void Note() {
                    print("note {n}")
                }

                pub int Get() => n
            }

            actor Boss {
                pub Worker Go(Other other) {
                    kid = spawn Worker() on crash (c) {
                        print("block")
                        restart
                    }

                    send kid.Fail()
                    print(other.Ping())
                    send kid.Note()
                    return kid
                }
            }

            o = spawn Other()
            b = spawn Boss()
            w = b.Go(o)
            print(w.Get())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-pending-call");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#3 crashed: thrown \"worker fails\" at program.kz:10"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["1", "block", "note 0", "0"], "");
    }

    [Fact]
    public void A_message_of_the_supervisor_that_calls_its_child_whose_block_waits_behind_it_gets_deadlock_at_that_call()
    {
        // U34 (the owner, 2026-11-16, #519, comment 6926540546, "Keep: it waits"), U60, U62 (the owner, 2026-11-20, #519, comment
        // 7051071630, T63 2): `Go` calls its child while the child's block waits in `Boss`'s inbox behind `Go`, so that the call's wait
        // closes a loop and gets `deadlock` at its line, `Boss`'s crash, whose log line names the loop; the top-level code, which waits on
        // `Go`, gets `callee-crashed`, and the block decided nothing
        const string text = """
            actor Other {
                pub int Ping() => 1
            }

            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Ping() => 2
            }

            actor Boss {
                pub int Go(Other other) {
                    kid = spawn Worker() on crash (c) {
                        print("never")
                        restart
                    }

                    send kid.Fail()
                    print(other.Ping())
                    return kid.Ping()
                }
            }

            o = spawn Other()
            b = spawn Boss()
            print(b.Go(o))

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-wait-deadlock");
        var (crashes, rest) = Logged(run.Error);
        Assert.Matches(@"^Boss#2 crashed: deadlock ""Boss#2 waits at program\.kz:22 on Worker#3 in chain [0-9a-f]{32}\\nWorker#3 waits for the decision of Boss#2 on its crash in chain [0-9a-f]{32}"" at program\.kz:22$", Assert.Single(crashes));
        Ran((run.Code, run.Output, rest), 1, ["1"], "error: callee-crashed at program.kz:28\n");
    }

    [Fact]
    public void The_log_line_of_a_deadlock_names_its_loop_with_the_chain_of_each_wait()
    {
        // U63 (the owner, 2026-11-20, #519, comment 7051071630, T68 3; 2026-11-21, comment 7060155980, T84 1): `Boss`'s `Go`,
        // sent in a chain of its own, calls `Get` of its child once the child's block waits behind `Go`, the child having crashed in
        // `Fail`, sent in another chain; `Boss`'s wait closes the loop and gets `deadlock`, and its log line holds the loop's text in
        // double quotes, a line for each wait, each ending with a chain's ID: the call's line that of `Go`, which waits, the child's that
        // of `Fail`, whose crash its block decides on, which differ
        const string text = """
            actor Clock {
                pub int Now() => 7
            }

            actor Worker {
                pub void Fail() {
                    throw "worker fails"
                }

                pub int Get() => 5
            }

            actor Boss(Clock C) {
                pub Worker Make() {
                    made = spawn Worker() on crash (x) {
                        restart
                    }

                    return made
                }

                pub void Go(Worker kid) {
                    t = C.Now()
                    print("now {t}")
                    print("got {kid.Get()}")
                }

                pub int Last() => 1
            }

            c = spawn Clock()
            b = spawn Boss(c)
            w = b.Make()
            send b.Go(w)
            send w.Fail()
            print(b.Last())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadlock-log");
        var (crashes, rest) = Logged(run.Error);
        var loop = Regex.Match(Assert.Single(crashes, l => l.StartsWith("Boss#2 ", StringComparison.Ordinal)),
            @"^Boss#2 crashed: deadlock ""Boss#2 waits at program\.kz:25 on Worker#3 in chain (?<call>[0-9a-f]{32})\\nWorker#3 waits for the decision of Boss#2 on its crash in chain (?<crash>[0-9a-f]{32})"" at program\.kz:25$");
        Assert.True(loop.Success, string.Join("\n", crashes));
        Assert.NotEqual(loop.Groups["call"].Value, loop.Groups["crash"].Value);
        Ran((run.Code, run.Output, rest), 0, ["now 7", "1"], "");
    }

    [Fact]
    public void The_wait_line_goes_on_through_the_activation_that_runs_a_call_that_got_in_by_its_chain()
    {
        // U60 (the owner, 2026-11-21, #519, comment 7060155980, T86 2): `C`'s `H` waits on `B`'s `K`, which got in by the chain of
        // `B`'s `G` and runs above it, newer in the chain than `H`; `K`'s wait on `D`, busy in `Y`, which waits on `C`, busy in `H`,
        // closes the loop through `K`, so `B` gets `deadlock` at `K`'s call, its text one line for each wait, `K`'s and `H`'s in
        // one chain and `Y`'s in another (U63); under the default policy `C`'s `H`, `A`'s `F` and the top-level code's call get
        // `callee-crashed` (U20)
        const string text = """
            actor E {
                pub int Delay() => 1
            }

            actor C {
                pub int H(B b) => b.K()

                pub int Z() => 3
            }

            actor B(D Last, C Mid) {
                pub int G() => Mid.H(this)

                pub int K() => Last.X()
            }

            actor D(E Slow) {
                pub void Y(C c) {
                    t = Slow.Delay()
                    print("y got {c.Z() + t}")
                }

                pub int X() => 4
            }

            actor A {
                pub int F(B b) => b.G()
            }

            ae = spawn E()
            ad = spawn D(ae)
            ac = spawn C()
            ab = spawn B(ad, ac)
            aa = spawn A()
            send ad.Y(ac)
            print(aa.F(ab))

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadlock-got-in");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(3, crashes.Count);
        var loop = Regex.Match(crashes[0],
            @"^B#4 crashed: deadlock ""B#4 waits at program\.kz:14 on D#2 in chain (?<k>[0-9a-f]{32})\\nD#2 waits at program\.kz:20 on C#3 in chain (?<y>[0-9a-f]{32})\\nC#3 waits at program\.kz:6 on B#4 in chain (?<h>[0-9a-f]{32})"" at program\.kz:14$");
        Assert.True(loop.Success, crashes[0]);
        Assert.Equal(loop.Groups["k"].Value, loop.Groups["h"].Value);
        Assert.NotEqual(loop.Groups["k"].Value, loop.Groups["y"].Value);
        Assert.Equal(["C#3 crashed: callee-crashed at program.kz:6", "A#5 crashed: callee-crashed at program.kz:27"], crashes.Skip(1).ToList());
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:36\n");
    }

    [Fact]
    public void An_overflow_a_block_escalates_to_the_root_reads_as_the_runtimes_stack_overflow()
    {
        // U30, U34: the overflow's `Reason` is the runtime's error of the kind `StackOverflow`, and its escalation ends the process with
        // its error line; the stack it overflowed on left from the handler's stack, so that the trace is the top-level code's (assumed)
        if (!OperatingSystem.IsLinux())
        {
            return; // an actor's overflow on Windows ends the process as the system ends it (compiler/README.md)
        }

        const string text = """
            actor Deep {
                int Down(int n) => Down(n) + 1

                pub void Go() {
                    print(Down(0))
                }

                pub int Ping() => 1
            }

            d = spawn Deep() on crash (c) {
                match c.Reason {
                    RuntimeError e => print("{e.Kind} {c.Place.Line}")
                }

                escalate
            }
            send d.Go()
            print(d.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-overflow", trace: null);
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 1, ["StackOverflow 0"], "error: stack-overflow at program.kz:2\n  at main in program.kz:19\n");
    }

    [Fact]
    public void A_raise_of_the_runtime_reaches_the_block_as_the_runtimes_error_of_its_kind()
    {
        // U30: a child whose code throws nothing has the runtime's error type alone as its `Reason`, its kind the id of the raise and its
        // place the raise's line; `stop` ends the child, whose call waiting in its inbox meanwhile gets `actor-gone` (S11 of comment
        // 6907441402)
        const string text = """
            actor Worker {
                pub void Fail(int at) {
                    xs = [1, 2]
                    print(xs[at])
                }

                pub int Ping() => 1
            }

            w = spawn Worker() on crash (c) {
                print("{c.Reason.Kind} at {c.Place.Line}")
                stop
            }
            send w.Fail(5)
            print(w.Ping())

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-block-runtime-error");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: index-out-of-range at program.kz:4"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["IndexOutOfRange at 4"], "error: actor-gone at program.kz:15\n");
    }

    // ---- the deadlines of the calls (U36 to U45; the owner, 2026-10-30, "Deadline on chain ID"; #519, comments 6967147952, 6967625156)

    /// <summary>
    /// S11: the build for Windows of <paramref name="text"/>, a program that reads the clock to hold its thread or to time a wait, is
    /// refused with one diagnostic, whose text is the emitter's refusal of such a read (<c>Emitter.ProcessNotOnWindows</c>), which this
    /// checks on every system, on <paramref name="windows"/> where it is given, else on the runtime of Windows, and not that diagnostic's
    /// line; whether this machine is Windows, where such a test holds that refusal alone, its run not built.
    /// </summary>
    private static bool ClockRefusedOnWindows(string text, CheckedRuntime? windows = null)
    {
        var refused = Builder.Compile(SourceText.FromString("program.kz", text), [], windows ?? Builder.CheckRuntime(Target.Windows), testBuild: true);
        Assert.Null(refused.Program);
        Assert.Equal(Emitter.ProcessNotOnWindows, Assert.Single(refused.Diagnostics.Items).Message);
        return OperatingSystem.IsWindows();
    }

    /// <summary>A function that holds the thread for <c>d</c> with no call and no wait, as the cases of timeouts hold it.</summary>
    private const string Hold = """
        void Hold(duration d) {
            instant start = instant.Now
            while start.Elapsed < d {
            }
        }

        """;

    [Fact]
    public void Every_target_gives_a_call_the_same_default_bound_of_5_seconds()
    {
        // U36: the default bound is the runtime's `DefaultBound` of the target, the one Linux's sockets take (S9), and Windows's holds
        // the same 5 seconds, so that the two never part
        string Bound(string path)
        {
            var text = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == path).Bytes);
            var found = Regex.Matches(text, @"^pub long DefaultBound\(\) => (\d+)$", RegexOptions.Multiline);
            Assert.True(found.Count == 1, $"{path} declares `pub long DefaultBound()` {found.Count} times");
            return found[0].Groups[1].Value;
        }

        Assert.Equal(["5000000000", "5000000000"], new[] { Bound("runtime/linux/linux.kz"), Bound("runtime/windows/windows.kz") });
        var common = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == "runtime/common/runtime.kz").Bytes);
        Assert.Contains("own = Due(now, sys.DefaultBound())", common, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deadline_is_a_bound_from_now_in_whole_milliseconds_rounded_up_and_none_past_the_clock()
    {
        // U36, U40: a part of a millisecond rounds up, a bound of zero or less is a deadline that has passed, `now` itself, which no bound
        // of a `within` gives it, since the emitted code refuses one where it is read, and a deadline past the largest time is none, the
        // largest `long`
        var printed = ListTests.Edited(("runtime/common/runtime.kz", "pub void Finish() {\n    EndActors()\n",
            "pub void Finish() {\n    Print(1, \"{Due(1000, 1)} {Due(1000, 1000000)} {Due(1000, 1500000)} {Due(1000, 0)} {Due(1000, -1)}\", \"\", 0)\n" +
            "    Print(1, \"{Due(1000, 9223372036854775807)} {Due(9223372036854775000, 5000000000)} {Due(9223372030000000000, 5000000000)}\", \"\", 0)\n    EndActors()\n"));
        Ran(BuildAndRun("print(0)\n", printed, "actor-deadline-due"), 0,
            ["0", "1001000 1001000 2001000 1000 1000", "9223372036854775807 9223372036854775807 9223372035000000000"], "");
    }

    [Fact]
    public void The_timers_keep_their_deadlines_in_order_and_none_outlives_its_wait()
    {
        // U36, U37: 300 activations park at once, each with a deadline of its own `within` block in an order no insertion at either end
        // keeps, and the runtime checks the order of its timers at each insertion; every wait takes its reply off them, and so does the
        // fall of an actor whose parked activation waits below a call of its chain that throws (U17, U20), so that none is left at the end
        var timers = ListTests.Edited(
            ("runtime/common/runtime.kz", "            timersLast = reply\n        }\n    }\n}\n",
                "            timersLast = reply\n        }\n\n        mut byte* at = timers\n        while at != null && *Ptr<byte*>(at + 32) != null {\n" +
                "            if *Ptr<long>(at + 16) > *Ptr<long>(*Ptr<byte*>(at + 32) + 16) || *Ptr<byte*>(*Ptr<byte*>(at + 32) + 24) != at {\n" +
                "                Print(2, \"timers out of order\", \"\", 0)\n            }\n\n            at = *Ptr<byte*>(at + 32)\n        }\n    }\n}\n"),
            ("runtime/common/runtime.kz", "pub void Finish() {\n    EndActors()\n",
                "pub void Finish() {\n    mut long left = 0\n    raw {\n        mut byte* at = timers\n        while at != null {\n            left = left + 1\n" +
                "            at = *Ptr<byte*>(at + 32)\n        }\n    }\n\n    Print(2, \"timers {left}\", \"\", 0)\n    EndActors()\n"));
        const string text = """
            actor Sink {
                pub int Get() => 1
            }

            actor Waiter {
                mut int got = 0

                pub void Go(Sink s, int n) {
                    mut d = 1s
                    for k in 0..<n {
                        d = d + 1ms
                    }

                    within d {
                        got = s.Get()
                    }
                }
            }

            actor Bouncer {
                pub int Ask(Crasher c) => c.Boom()
            }

            actor Crasher {
                pub void Go(Bouncer b) {
                    me = this
                    within 2s {
                        print(b.Ask(me))
                    }
                }

                pub int Boom() {
                    throw "boom"
                }
            }

            sink = spawn Sink()
            bouncer = spawn Bouncer() on crash restart after 1ms
            crasher = spawn Crasher() on crash restart after 1ms
            send crasher.Go(bouncer)
            mut waiters = [spawn Waiter()]
            for i in 1..<300 {
                waiters.Add(spawn Waiter())
            }

            for i in 0..<299 {
                send waiters[i].Go(sink, i * 7919 % 300)
            }

            waiters[299].Go(sink, 299 * 7919 % 300)
            print("done")

            """;
        var run = BuildAndRun(text, timers, "actor-timers-order");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Crasher#3 crashed: thrown \"boom\" at program.kz:33", "Bouncer#2 crashed: callee-crashed at program.kz:21"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["done"], "timers 0\n");
    }

    [Fact]
    public void A_send_past_its_chains_deadline_is_dropped_with_no_raise_and_so_is_one_whose_deadline_passes_in_the_inbox()
    {
        // U37: a `send` carries its chain's deadline (comment 6880404917, Q13); one queued before it passed, behind a message that holds
        // the callee past it, is dropped at its take with no line (T3 of comment 6967147952); one made after it passed raises nothing and
        // is dropped (U45, T10 of comment 6967625156), so that the message that sent it goes on
        const string text = Hold + """
            actor Store {
                mut int notes = 0

                pub void Busy() {
                    Hold(300ms)
                }

                pub void Note() {
                    notes = notes + 1
                }

                pub int Notes() => notes
            }

            actor Front(Store S) {
                pub void Early() {
                    send S.Busy()
                    send S.Note()
                }

                pub void Late() {
                    Hold(150ms)
                    send S.Note()
                    print("sent")
                }
            }

            s = spawn Store()
            f = spawn Front(s)
            within 100ms {
                f.Early()
            }

            print(s.Notes())
            within 100ms {
                f.Late()
            }

            print(s.Notes())

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        Ran(BuildAndRun(text, toolchain.Runtime, "actor-deadline-sends"), 0, ["0", "sent", "0"], "");
    }

    [Fact]
    public void A_message_dropped_past_its_deadline_gives_back_the_blocks_of_its_arguments()
    {
        // U37, section 3: the inbox drops a message past its deadline through the third function of its call site's table, which lets go
        // of its arguments, a string and a list, and frees it, so that across a round of such drops the heaps of both actors and the
        // top-level code's hold as many blocks as before it, and no dropped message ran
        const string text = Hold + """
            actor Store {
                mut int notes = 0

                pub void Busy() {
                    Hold(300ms)
                }

                pub void Note(string s, List<int> xs) {
                    notes = notes + s.Bytes.Count + xs.Count
                }

                pub int Notes() => notes

                pub long Live() => HttpServer.HeapLive()
            }

            actor Front(Store S) {
                pub void Early(int r) {
                    send S.Busy()
                    send S.Note("note {r}", [r, 2])
                }

                pub long Live() => HttpServer.HeapLive()
            }

            long Round(Store s, Front f, int r) {
                within 100ms {
                    f.Early(r)
                }

                return s.Live() + f.Live()
            }

            store = spawn Store()
            front = spawn Front(store)
            Round(store, front, 0)
            Round(store, front, 1)
            own = HttpServer.HeapLive()
            held = Round(store, front, 2)
            ownHeld = HttpServer.HeapLive() - own
            left = Round(store, front, 3) - held
            ownLeft = HttpServer.HeapLive() - own - ownHeld
            print("{left} {ownLeft} {store.Notes()}")

            """;
        if (ClockRefusedOnWindows(text, HeapBlocksFor(Target.Windows)))
        {
            return;
        }

        Ran(BuildAndRun(text, HeapBlocks.Value, "actor-deadline-drop-blocks"), 0, ["0 0 0"], "");
    }

    [Fact]
    public void A_call_past_its_deadline_raises_where_it_is_queued_before_another_message_runs()
    {
        // U37, U38: a call whose deadline passed before it is queued raises there, so that the crash of its caller, whose block of the
        // top-level code prints first (U33), comes before `C`'s message that the caller sent, which stands ahead in the run queue
        const string text = Hold + """
            actor B {
                pub int Get() => 1
            }

            actor C {
                pub void Say() {
                    print("c")
                }

                pub int Ping() => 1
            }

            actor T(C Other) {
                pub int Slow() => Other.Ping()
            }

            actor A {
                pub void Go(B b, C c) {
                    within 100ms {
                        send c.Say()
                        Hold(150ms)
                        print("a {b.Get()}")
                    }
                }
            }

            getter = spawn B()
            sayer = spawn C()
            slow = spawn T(sayer)
            caller = spawn A() on crash (e) {
                print("crash")
                stop
            }

            send caller.Go(getter, sayer)
            print(slow.Slow())

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-queued");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["A#4 crashed: call-timeout at program.kz:27"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["crash", "c", "1"], "");
    }

    [Fact]
    public void A_call_of_the_waiting_chain_past_its_deadline_is_dropped_from_the_side_of_the_waiting_actor()
    {
        // U17, U37: `Y`'s call back into `X`, which waits on `Y` in the same chain, stands on `X`'s side while `W` holds the thread past
        // its deadline, so that `X` drops it unrun: `Y` raises `call-timeout`, and `X`, whose deadline stands, `callee-crashed`
        const string text = Hold + """
            actor W {
                pub void Busy() {
                    Hold(300ms)
                }
            }

            actor X {
                pub int Go(Y y, W w) {
                    me = this
                    return y.Work(me, w)
                }

                pub int Back() => 1
            }

            actor Y {
                pub int Work(X x, W w) {
                    send w.Busy()
                    mut r = 0
                    within 50ms {
                        r = x.Back()
                    }

                    return r
                }
            }

            xs = spawn X()
            ys = spawn Y()
            ws = spawn W()
            print(xs.Go(ys, ws))

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-side");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Y#2 crashed: call-timeout at program.kz:26", "X#1 crashed: callee-crashed at program.kz:15"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:36\n");
    }

    [Fact]
    public void A_chain_that_no_call_starts_has_no_deadline()
    {
        // U45, T9 of comment 6967625156: the chain of a `send` of the top-level code and the restart's chain carry no deadline, each call
        // they make bounded on its own; under a default bound of 100 ms, a call made after 300 ms of such a chain's work answers
        var short_ = ListTests.Edited(("runtime/linux/linux.kz", "pub long DefaultBound() => 5000000000", "pub long DefaultBound() => 100000000"));
        const string text = Hold + """
            actor Ticker {
                pub int Ping() => 1
            }

            actor Slow(Ticker T) {
                pub void Work() {
                    Hold(300ms)
                    print("work {T.Ping()}")
                }
            }

            int Warm(Ticker t) {
                Hold(300ms)
                return t.Ping()
            }

            actor Phoenix(Ticker T) {
                mut int pings = Warm(T)

                pub void Fail() {
                    throw "fail"
                }

                pub int Pings() => pings
            }

            ticker = spawn Ticker()
            phoenix = spawn Phoenix(ticker) on crash restart after 1ms
            slow = spawn Slow(ticker)
            send slow.Work()
            send phoenix.Fail()
            within 10s {
                print(phoenix.Pings())
            }

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, short_, "actor-deadline-no-chain");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Phoenix#2 crashed: thrown \"fail\" at program.kz:26"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["work 1", "1"], "");
    }

    [Fact]
    public void A_constructor_that_a_spawn_runs_calls_under_the_chain_and_the_within_of_its_spawner()
    {
        // S4 (section 6, assumed, proposed with the record and not put to the owner): a constructor runs at its `spawn` in its spawner's
        // code (U3), so that its call after 300 ms raises `call-timeout` at its line under the top-level code's `within`, under the
        // `within` of a message and under the deadline of a message's chain, as a call of the spawner's own would; the raise of a
        // constructor that a message runs is the crash of that message's actor, and the top-level code whose callee crashed once its own
        // deadline had passed raises `call-timeout` (U43); under the 5 seconds of the top-level code's call alone, which its 300 ms fit,
        // the constructor's call answers
        const string slow = Hold + """
            actor Ticker {
                pub int Ping() => 1
            }

            int Warm(Ticker ticker) {
                Hold(300ms)
                return ticker.Ping()
            }

            actor Slow(Ticker T) {
                mut int pings = Warm(T)

                pub int Pings() => pings
            }

            """;
        const string atTop = slow + """
            ticks = spawn Ticker()
            within 100ms {
                s = spawn Slow(ticks)
                print(s.Pings())
            }

            """;
        const string inMessage = slow + """
            actor Maker(Ticker T) {
                pub void Make() {
                    within 100ms {
                        s = spawn Slow(T)
                        print("made {s.Pings()}")
                    }
                }

                pub int Pings() {
                    s = spawn Slow(T)
                    return s.Pings()
                }
            }

            ticks = spawn Ticker()
            m = spawn Maker(ticks)
            send m.Make()
            print(m.Pings())
            within 100ms {
                print(m.Pings())
            }

            """;
        var windows = ClockRefusedOnWindows(atTop);
        ClockRefusedOnWindows(inMessage);
        if (windows)
        {
            return;
        }

        Ran(BuildAndRun(atTop, toolchain.Runtime, "actor-deadline-constructor-top"), 1, [], "error: call-timeout at program.kz:12\n");
        var run = BuildAndRun(inMessage, toolchain.Runtime, "actor-deadline-constructor");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Maker#2 crashed: call-timeout at program.kz:12", "Maker#2 crashed: call-timeout at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["1"], "error: call-timeout at program.kz:39\n");
    }

    [Fact]
    public void A_block_that_runs_in_the_wait_of_the_top_level_code_calls_under_no_deadline_of_its_within()
    {
        // U33, U45, T9 of comment 6967625156: the block's call after 300 ms answers though the `within` of the top-level call it runs in
        // has passed; the top-level call, whose message the inbox dropped meanwhile, raises `call-timeout` at its line
        const string text = Hold + """
            actor Ticker {
                pub int Ping() => 1
            }

            actor Kid {
                pub void Fail() {
                    throw "kid"
                }
            }

            t = spawn Ticker()
            k = spawn Kid() on crash (c) {
                Hold(300ms)
                print("block {t.Ping()}")
                restart
            }
            send k.Fail()
            within 200ms {
                print(t.Ping())
            }

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-root-block");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Kid#2 crashed: thrown \"kid\" at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 1, ["block 1"], "error: call-timeout at program.kz:24\n");
    }

    [Fact]
    public void A_wait_inside_a_message_ends_at_its_deadline_while_a_pause_runs()
    {
        // U36, U37: `A` waits on `B`, whose restart's pause of 1 s runs; the top-level code, with nothing else to run, sleeps to the
        // first of that pause, its own deadline and the first timer, so that `A`'s wait ends at 200 ms with `call-timeout` at its line,
        // the crash of `A`, and the call queued behind it answers well before the pause ends
        const string text = """
            actor B {
                pub void Fail() {
                    throw "b"
                }

                pub int Get() => 1
            }

            actor A {
                pub void Go(B b) {
                    within 200ms {
                        print("got {b.Get()}")
                    }
                }

                pub int After() => 2
            }

            paused = spawn B() on crash restart after 1s
            waiting = spawn A() on crash restart after 1ms
            send paused.Fail()
            start = instant.Now
            send waiting.Go(paused)
            print(waiting.After())
            print(start.Elapsed < 700ms)

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-timer");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["B#1 crashed: thrown \"b\" at program.kz:3", "A#2 crashed: call-timeout at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["2", "true"], "");
    }

    [Fact]
    public void A_timer_that_fired_leaves_the_timers_of_the_other_waits()
    {
        // U36, U37: two activations wait on `B`, whose restart's pause of 1 s runs, one to 100 ms and one to 200 ms; the first timer
        // fires and its wait ends, which leaves the second timer in place, so that the second wait ends at 200 ms too
        const string text = """
            actor B {
                pub void Fail() {
                    throw "b"
                }

                pub int Get() => 1
            }

            actor A {
                pub void Go(B b, duration d) {
                    within d {
                        print("got {b.Get()}")
                    }
                }

                pub int After() => 2
            }

            paused = spawn B() on crash restart after 1s
            first = spawn A() on crash restart after 1ms
            second = spawn A() on crash restart after 1ms
            send paused.Fail()
            start = instant.Now
            send first.Go(paused, 100ms)
            send second.Go(paused, 200ms)
            print(second.After())
            print(start.Elapsed < 700ms)

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-timers-two");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["B#1 crashed: thrown \"b\" at program.kz:3", "A#2 crashed: call-timeout at program.kz:12",
            "A#3 crashed: call-timeout at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["2", "true"], "");
    }

    [Fact]
    public void A_caller_whose_callee_crashed_after_the_callers_deadline_raises_call_timeout()
    {
        // U43, T7 of comment 6967625156: `C` throws after the deadline of `A`'s call passed, and `A` raises `call-timeout` at its call,
        // not `callee-crashed`, so that the top-level code, whose deadline stands, raises `callee-crashed`; the top-level code whose
        // callee throws after its deadline raises `call-timeout`
        const string callee = Hold + """
            actor C {
                pub int Work() {
                    Hold(300ms)
                    throw "c"
                }
            }

            """;
        const string waiting = callee + """
            actor A {
                pub int Go(C c) {
                    mut r = 0
                    within 100ms {
                        r = c.Work()
                    }

                    return r
                }
            }

            worker = spawn C()
            caller = spawn A()
            print(caller.Go(worker))

            """;
        if (ClockRefusedOnWindows(waiting))
        {
            return;
        }

        var run = BuildAndRun(waiting, toolchain.Runtime, "actor-deadline-crashed");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["C#1 crashed: thrown \"c\" at program.kz:9", "A#2 crashed: call-timeout at program.kz:16"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: callee-crashed at program.kz:25\n");
        run = BuildAndRun(callee + """
            worker = spawn C()
            within 100ms {
                print(worker.Work())
            }

            """, toolchain.Runtime, "actor-deadline-crashed-top");
        (crashes, rest) = Logged(run.Error);
        Assert.Equal(["C#1 crashed: thrown \"c\" at program.kz:9"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: call-timeout at program.kz:14\n");
    }

    [Fact]
    public void A_within_puts_its_deadline_back_at_every_way_out_and_an_inner_one_keeps_the_smaller()
    {
        // U38: the deadline a `within` replaced stands again after a `continue`, a `break` and a `return` out of it, so that a call 100 ms
        // later answers; a block inside one whose deadline is earlier keeps that deadline (U38, assumed)
        const string text = Hold + """
            actor Store {
                pub int Get() => 1
            }

            int Inside(Store s) {
                within 50ms {
                    return s.Get()
                }
            }

            store = spawn Store()
            for i in 0..<2 {
                within 50ms {
                    if i == 0 {
                        continue
                    }

                    break
                }
            }

            print(Inside(store))
            Hold(100ms)
            print(store.Get())
            within 100ms {
                within 10s {
                    Hold(150ms)
                    print(store.Get())
                }
            }

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        Ran(BuildAndRun(text, toolchain.Runtime, "actor-deadline-within"), 1, ["1", "1"], "error: call-timeout at program.kz:33\n");
    }

    [Fact]
    public void A_calls_bound_is_its_blocks_over_its_clauses_over_its_declarations_each_capped_by_its_chain()
    {
        // U39: where more than one form names a bound for one call, the block's wins over the call's clause and the call's clause over
        // the declaration's, and every bound stays capped by the time the chain has left (assumed in section 6): `Busy` holds `Store` for
        // 150 ms, so a call queued behind it under a bound of 100 ms or less is dropped and raises `call-timeout` in `Probe`, its crash,
        // and one under 1 s answers; `Relay` runs under the 100 ms of its call, so its clause of 1 s is capped to that (U44)
        const string text = Hold + """
            actor Store {
                pub void Busy() {
                    Hold(150ms)
                }

                pub int Get() within 100ms => 1

                pub int Put() => 2
            }

            actor Front(Store S) {
                pub int Relay() {
                    send S.Busy()
                    return S.Put() within 1s
                }
            }

            actor Probe(Store S, Front F) {
                pub void ClauseOverDeclaration() {
                    send S.Busy()
                    print(S.Get() within 1s)
                }

                pub void BlockOverClause() {
                    send S.Busy()
                    within 1s {
                        print(S.Get() within 50ms)
                    }
                }

                pub void Declaration() {
                    send S.Busy()
                    print(S.Get())
                }

                pub void Clause() {
                    send S.Busy()
                    print(S.Put() within 100ms)
                }

                pub void Chain() {
                    print(F.Relay() within 100ms)
                }

                pub string Done() => "done"
            }

            s = spawn Store()
            f = spawn Front(s)
            p = spawn Probe(s, f)
            send p.ClauseOverDeclaration()
            send p.BlockOverClause()
            send p.Declaration()
            send p.Clause()
            send p.Chain()
            print(p.Done())

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        var run = BuildAndRun(text, toolchain.Runtime, "actor-deadline-clauses");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Probe#3 crashed: call-timeout at program.kz:38", "Probe#3 crashed: call-timeout at program.kz:43"], crashes.Take(2));

        // the wait of `Chain` and that of `Relay` end at one deadline, in either order
        Assert.Equal(["Front#2 crashed: call-timeout at program.kz:19", "Probe#3 crashed: call-timeout at program.kz:47"], crashes.Skip(2).Order(StringComparer.Ordinal));
        Ran((run.Code, run.Output, rest), 0, ["1", "1", "done"], "");
    }

    [Fact]
    public void A_clauses_bound_runs_after_the_arguments_and_a_declarations_literal_bounds_each_call()
    {
        // U39, F12: the bound of a call's clause runs after the call's arguments, as it is written after them, and the literal of a
        // declaration's clause bounds every call of the method that names none: `Add`'s answers within its 2 seconds, and `Late`'s, whose
        // literal of 50 ms passes while `Busy` holds the thread for 150 ms before it (U41), raises `call-timeout` at its line, which only
        // that literal bounds; a literal of zero or less there is `bound-not-positive` (CheckerTests)
        const string text = Hold + """
            actor Store {
                pub int Add(int x) within 2s => x + 1

                pub void Busy() {
                    Hold(150ms)
                }

                pub int Late() within 50ms => 0
            }

            int Note(string text, int x) {
                print(text)
                return x
            }

            duration Bound(string text) {
                print(text)
                return 1s
            }

            s = spawn Store()
            print(s.Add(Note("argument", 1)) within Bound("bound"))
            print(s.Add(2))
            send s.Busy()
            print(s.Late())

            """;
        if (ClockRefusedOnWindows(text))
        {
            return;
        }

        Ran(BuildAndRun(text, toolchain.Runtime, "actor-clause-order"), 1, ["argument", "bound", "2", "3"], "error: call-timeout at program.kz:30\n");
    }

    /// <summary>
    /// An actor, a function that prints a note and gives its number, one that prints a note and gives a bound, a call of the actor, and
    /// a block bounded by <c>2ns - 1ns</c>, the smallest positive bound, which runs: the line after it is the first of a row of
    /// <see cref="A_bound_of_zero_or_less_read_at_run_time_is_invalid_input_at_its_line_where_it_is_read"/>.
    /// </summary>
    private const string Bounds = """
        actor Store {
            pub int Get(int x) => x
        }

        int Note(string text, int x) {
            print(text)
            return x
        }

        duration Bound(string text, duration d) {
            print(text)
            return d
        }

        s = spawn Store()
        print(s.Get(0))
        tiny = 2ns - 1ns
        within tiny {
            print("tiny")
        }

        """;

    [Theory]
    [InlineData("within Bound(\"block\", BOUND) {\n    print(s.Get(1))\n}\n", "1s - 1s", 0, "block", "0ms", "1")]
    [InlineData("within Bound(\"block\", BOUND) {\n    print(\"inside\")\n}\n", "1s - 2s", 0, "block", "-1s", "inside")]
    [InlineData("print(s.Get(Note(\"argument\", 1)) within Bound(\"bound\", BOUND))\n", "1s - 1s", 0, "argument bound", "0ms", "1")]
    [InlineData("within 1s {\n    print(s.Get(1) within Bound(\"bound\", BOUND))\n}\n", "0s - 1ms", 1, "bound", "-1ms", "1")]
    public void A_bound_of_zero_or_less_read_at_run_time_is_invalid_input_at_its_line_where_it_is_read(string tail, string zero, int below, string notes, string bound, string last)
    {
        // U40, U41: a bound that is no constant (T25) is read where it is read, and one of zero or less is `invalid-input` at the bound's
        // line, its value on the line below the error line, written as a duration's text is (section 6, the owner, 2026-11-20, #519, comment
        // 7051492844, "T4 (2)"; assumed: T78 and T81 of comment 7055305138, and T79 of it as corrected on 2026-11-21): at a block's entry,
        // before its first statement, so that
        // no call in it runs and a block with no call in it is refused too, and at a call's clause after the call's arguments, before
        // the call is queued, under a block whose deadline wins over the clause too (U39); `2ns - 1ns`, the smallest positive bound, is
        // a bound (the product pass's default, comment 7055304984), and so is a computed bound of 1 s in the same place, the control
        var line = Bounds.Split('\n').Length + below;
        Ran(BuildAndRun(Bounds + tail.Replace("BOUND", zero, StringComparison.Ordinal), toolchain.Runtime, "actor-bound-zero"), 1, ["0", "tiny", .. notes.Split(' ')],
            $"error: invalid-input at program.kz:{line}:\nthe bound of `within` is {bound}; a bound is positive\n");
        Ran(BuildAndRun(Bounds + tail.Replace("BOUND", "2s - 1s", StringComparison.Ordinal), toolchain.Runtime, "actor-bound-positive"), 0,
            ["0", "tiny", .. notes.Split(' '), last], "");
    }

    [Fact]
    public void A_calls_bound_of_zero_or_less_over_an_actor_that_ended_is_invalid_input_and_a_positive_one_actor_gone()
    {
        // U41, U43, U25: the bound of a call's clause is read after the call's arguments and before the call is queued, where the actor
        // is looked up, so that a bound of zero or less over an actor that ended is `invalid-input`, not `actor-gone` (assumed: T81 of
        // comment 7055305138), where a positive bound over it is `actor-gone`, the control
        const string text = """
            actor Store {
                pub void Fail() {
                    throw "gone"
                }

                pub int Get() => 1
            }

            actor Other {
                pub int Ping() => 2
            }

            gone = spawn Store() on crash stop
            o = spawn Other()
            send gone.Fail()
            print(o.Ping())
            d = 2s - BOUND
            print(gone.Get() within d)

            """;
        var zero = BuildAndRun(text.Replace("BOUND", "2s", StringComparison.Ordinal), toolchain.Runtime, "actor-bound-zero-gone");
        var (crashes, rest) = Logged(zero.Error);
        Assert.Equal(["Store#1 crashed: thrown \"gone\" at program.kz:3"], crashes);
        Ran((zero.Code, zero.Output, rest), 1, ["2"], "error: invalid-input at program.kz:18:\nthe bound of `within` is 0ms; a bound is positive\n");
        var positive = BuildAndRun(text.Replace("BOUND", "1s", StringComparison.Ordinal), toolchain.Runtime, "actor-bound-positive-gone");
        (crashes, rest) = Logged(positive.Error);
        Assert.Equal(["Store#1 crashed: thrown \"gone\" at program.kz:3"], crashes);
        Ran((positive.Code, positive.Output, rest), 1, ["2"], "error: actor-gone at program.kz:18\n");
    }

    [Fact]
    public void A_bound_of_zero_or_less_in_a_message_crashes_its_actor_whose_block_reads_invalid_input()
    {
        // U40, U19, U30: `invalid-input` is raised as `call-timeout` is, the crash of the actor whose code reads the bound (assumed: T78 of
        // comment 7055305138), whose log line carries the bound, and whose block reads the runtime's error of the kind `InvalidInput`,
        // its detail the text below the error line, at the bound's line; the restarted actor answers
        const string text = """
            actor Worker {
                pub void Go(duration d) {
                    within d {
                        print("inside")
                    }
                }

                pub int Ping() => 2
            }

            w = spawn Worker() on crash (c) {
                print("{c.Reason.Kind} {c.Place.Line} {c.Reason.Detail}")
                restart
            }
            send w.Go(1s - 1s)
            print(w.Ping())

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-bound-zero-crash");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Worker#1 crashed: invalid-input \"the bound of `within` is 0ms; a bound is positive\" at program.kz:3"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["InvalidInput 3 the bound of `within` is 0ms; a bound is positive", "2"], "");
    }

    [Fact]
    public void A_message_whose_clauses_bound_is_refused_at_run_time_leaves_no_block_in_its_callees_heap()
    {
        // U41, U12, U19: the bound `d`, a name, runs after the message and the copy of its list were built, and its `invalid-input` for a
        // bound of zero crashes `Front` before their queueing; the message is built in a heap of its own, which that crash frees, so that
        // `Store`'s heap holds no more blocks than it held before, where a message built in `Store`'s heap would leave the list's copy
        // there, unqueued; the call of 1 s is the control that the count sees that copy in `Store`'s heap while its message runs; under
        // the allocator that makes every freed block inaccessible
        const string text = """
            actor Store {
                pub long Held(List<int> xs) => HttpServer.Big()

                pub long Big() => HttpServer.Big()
            }

            actor Front {
                pub void Ask(Store s, duration d, long start) {
                    mut xs = List<int>()
                    for n in 0..<40000 {
                        xs.Add(n)
                    }

                    held = s.Held(xs) within d
                    print(held - start)
                }

                pub int Ping() => 2
            }

            store = spawn Store()
            f = spawn Front()
            before = store.Big()
            send f.Ask(store, 1s, before)
            send f.Ask(store, 1s - 1s, before)
            print(f.Ping())
            print(store.Big() - before)

            """;
        var run = BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. BigBlocks]), "actor-clause-bound-refused");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Front#2 crashed: invalid-input \"the bound of `within` is 0ms; a bound is positive\" at program.kz:14"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["1", "2", "0"], "");
    }

    [Fact]
    public void A_bound_that_gives_the_field_its_callee_was_read_from_another_actor_through_its_instance_leaves_the_call_with_the_actor_read_before_it()
    {
        // U39, F12, U2: the callee's reference, two words, is read before the arguments and the bound, as a receiver is, so that a bound
        // that gives the field it was read from another actor, through its instance passed as `mut`, leaves the call with the actor read
        // before it, which answers `1`, and the next read of the field the other actor, which answers `2`; M7 refuses no such bound, as
        // it refuses one that changes the callee's variable or its list
        // (`CheckerTests.The_bound_of_a_calls_clause_changes_nothing_its_callee_was_read_from`); under the allocator that makes every
        // freed block inaccessible
        const string swap = """
            actor Store(int N) {
                pub int Get() => N
            }

            class Box {
                pub mut Store s = spawn Store(1)
            }

            duration Swap(mut Box held) {
                held.s = spawn Store(2)
                return 1s
            }

            mut b = Box()
            print(b.s.Get() within Swap(mut b))
            print(b.s.Get())

            """;
        Ran(BuildAndRun(swap, ListTests.Fenced, "actor-callee-field-swap"), 0, ["1", "2"], "");
    }

    [Fact]
    public void A_bound_that_gives_the_actors_field_its_callee_was_read_from_another_actor_in_its_method_leaves_the_call_with_the_actor_read_before_it()
    {
        // U39, F12, U2: as through an instance passed as `mut`
        // (`A_bound_that_gives_the_field_its_callee_was_read_from_another_actor_through_its_instance_leaves_the_call_with_the_actor_read_before_it`),
        // a bound that calls a method of the actor that holds the field, which gives the field another actor, leaves the call with the
        // actor read before it, which answers `1`, and the next read of the field the other actor, which answers `2`; under the
        // allocator that makes every freed block inaccessible
        const string reset = """
            actor Store(int N) {
                pub int Get() => N
            }

            actor Front {
                mut Store helper = spawn Store(1)

                duration Reset() {
                    helper = spawn Store(2)
                    return 1s
                }

                pub int Ask() => helper.Get() within Reset()

                pub int Now() => helper.Get()
            }

            f = spawn Front()
            print(f.Ask())
            print(f.Now())

            """;
        Ran(BuildAndRun(reset, ListTests.Fenced, "actor-callee-field-reset"), 0, ["1", "2"], "");
    }

    [Fact]
    public void A_message_whose_callee_ends_while_its_clauses_bound_waits_holds_no_block_of_the_heap_the_end_freed()
    {
        // U39, U12, U21: the bound of the clause waits after the arguments ran, as an argument that waits does (`A_message_whose_callee_
        // ends_while_one_of_its_arguments_waits_holds_no_block_of_the_heap_the_end_freed`): `Box` ends as the child of a supervisor that
        // crashed during the wait of `s.Bound()`, which frees its heap whole, so the message is built in a heap of its own and copied at
        // its queueing into the heap the top-level code allocates in, the call raising `actor-gone` at its line; under the allocator that
        // makes every freed block inaccessible
        const string text = """
            actor Slow {
                pub duration Bound() => 2s
            }

            actor Box {
                pub int Add(List<int> xs, int k, List<int> ys) => xs[0] + k + ys[2]
            }

            actor Parent {
                mut Box? kid = null

                pub void Make() {
                    kid = spawn Box()
                }

                pub Box? Kid() => kid

                pub void Boom() {
                    throw "parent"
                }
            }

            p = spawn Parent()
            p.Make()
            kid = p.Kid()
            s = spawn Slow()
            items = [10, 20, 30]
            if kid != null {
                send p.Boom()
                print(kid.Add(items, 1, items) within s.Bound())
            }

            print("end")

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-ended-while-bound-waits");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Parent#1 crashed: thrown \"parent\" at program.kz:19"], crashes);
        Ran((run.Code, run.Output, rest), 1, [], "error: actor-gone at program.kz:30\n");
    }

    /// <summary>
    /// The count of the blocks of more than 262144 bytes of the heap the allocation entry serves, an actor's own inside its message, each
    /// on its heap's list of such blocks (<c>Enlist</c> in <c>std/std.kz</c>), read as <c>HttpServer.Big()</c>.
    /// </summary>
    private static (string Path, string Old, string New)[] BigBlocks =>
    [
        ("std/std.kz", "pub class HttpServer {\n", """
            pub class HttpServer {
                pub static long Big() {
                    mut long n = 0
                    byte* heap = HeapCurrent()
                    raw {
                        mut byte* at = *Ptr<byte*>(heap + 24)
                        while at != null {
                            n = n + 1
                            at = *Ptr<byte*>(at + 8)
                        }
                    }

                    return n
                }

            """),
    ];

    [Fact]
    public void A_message_whose_clauses_bound_crashes_its_builder_leaves_no_block_in_its_callees_heap()
    {
        // U39, U12, U19: the bound `ds[i]` runs after the message and the copy of its list were built, and its `index-out-of-range` crashes
        // `Front`, the actor whose code builds them, before their queueing; the message is built in a heap of its own, which that crash
        // frees, so that `Store`'s heap holds no more blocks than it held before, where a message built in `Store`'s heap would leave the
        // list's copy there, unqueued; `Held` is the control that the count sees that copy in `Store`'s heap while its message runs; under
        // the allocator that makes every freed block inaccessible
        const string text = """
            actor Store {
                pub long Held(List<int> xs) => HttpServer.Big()

                pub long Big() => HttpServer.Big()
            }

            actor Front {
                pub void Ask(Store s, List<duration> ds, int i, long start) {
                    mut xs = List<int>()
                    for n in 0..<40000 {
                        xs.Add(n)
                    }

                    held = s.Held(xs) within ds[i]
                    print(held - start)
                }

                pub int Ping() => 2
            }

            store = spawn Store()
            f = spawn Front()
            before = store.Big()
            send f.Ask(store, [1s], 0, before)
            send f.Ask(store, [1s], 1, before)
            print(f.Ping())
            print(store.Big() - before)

            """;
        var run = BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. BigBlocks]), "actor-clause-bound-crashes");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Front#2 crashed: index-out-of-range at program.kz:14"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["1", "2", "0"], "");
    }

    [Fact]
    public void A_message_whose_argument_after_a_crossing_one_crashes_its_builder_leaves_no_block_in_its_callees_heap()
    {
        // section 6 (the owner, 2026-11-20, #519, comment 7051492844, "T24 (1)", and 2026-11-21, comment 7060155980, "T83 1"): the index
        // `ks[i]` stands after the list `xs`, which crosses into the callee's heap, and its `index-out-of-range` crashes `Front`, the
        // actor whose code builds the message, before the message's queueing; the list is copied after the last argument, so that the
        // crash comes before its copy and `Store`'s heap holds no more blocks than it held before, where a copy made where the list is
        // evaluated would stay there, unqueued, to `Store`'s end; `Held` is the control that the count sees that copy in `Store`'s heap
        // while its message runs; under the allocator that makes every freed block inaccessible
        const string text = """
            actor Store {
                pub long Held(List<int> xs, int k) => HttpServer.Big()

                pub long Big() => HttpServer.Big()
            }

            actor Front {
                pub void Ask(Store s, List<int> ks, int i, long start) {
                    mut xs = List<int>()
                    for n in 0..<40000 {
                        xs.Add(n)
                    }

                    held = s.Held(xs, ks[i])
                    print(held - start)
                }

                pub int Ping() => 2
            }

            store = spawn Store()
            f = spawn Front()
            before = store.Big()
            send f.Ask(store, [1], 0, before)
            send f.Ask(store, [1], 1, before)
            print(f.Ping())
            print(store.Big() - before)

            """;
        var run = BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. BigBlocks]), "actor-argument-raises-after-crossing");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Front#2 crashed: index-out-of-range at program.kz:14"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["1", "2", "0"], "");
    }

    [Fact]
    public void A_spawn_whose_argument_after_a_crossing_one_crashes_its_spawner_leaves_no_heap_of_the_actor_it_was_making()
    {
        // section 6 (the owner, 2026-11-21, #519, comment 7060155980, "T82 1"): the index `ks[i]` stands after the list `xs`, which
        // crosses into the heap the `spawn` made before its arguments, and its `index-out-of-range` crashes `Front`, the actor whose code
        // spawns; that heap is listed with `Front`, whose crash frees it with the list's copy, so that the count of the heaps of the
        // thread moves at that crash as it moves at a crash in `Before`, which raises before its `spawn` makes a heap, where a heap
        // listed with nothing stays, one heap more; both after a first crash, whose restart moved the count by one where the later ones
        // move it by none; `Make` with `ks[0]` is the control that the count sees the heap of the actor a `spawn` made; under the
        // allocator that makes every freed block inaccessible
        const string text = """
            actor Holder(List<int> Items, int N) {
                pub int Count() => Items.Count + N
            }

            actor Front {
                pub void Make(List<int> ks, int i) {
                    mut xs = List<int>()
                    for n in 0..<1000 {
                        xs.Add(n)
                    }

                    h = spawn Holder(xs, ks[i]) on crash stop
                    print(h.Count())
                }

                pub void Before(List<int> ks, int i) {
                    k = ks[i]
                    mut xs = List<int>()
                    for n in 0..<1000 {
                        xs.Add(n)
                    }

                    h = spawn Holder(xs, k) on crash stop
                    print(h.Count())
                }

                pub int Ping() => 2
            }

            f = spawn Front()
            send f.Before([1], 1)
            print(f.Ping())
            start = HttpServer.Heaps()
            send f.Before([1], 1)
            print(f.Ping())
            middle = HttpServer.Heaps()
            send f.Make([1], 1)
            print(f.Ping())
            end = HttpServer.Heaps()
            print("{middle - start} {end - middle}")
            f.Make([1], 0)
            print(HttpServer.Heaps() - end)

            """;
        var run = BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. HeapCount]), "actor-spawn-raises-after-crossing");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Front#1 crashed: index-out-of-range at program.kz:17", "Front#1 crashed: index-out-of-range at program.kz:17", "Front#1 crashed: index-out-of-range at program.kz:12"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["2", "2", "2", "0 0", "1001", "1"], "");
    }

    /// <summary>
    /// The runtime with a check that the heap the runtime's <c>ActorNew</c> gives an actor, and the one <c>HeapLeft</c> frees, stands on no
    /// list of the heaps being built (<c>Listed</c>), which ends the process with the line <c>listed</c> and the exit code 3 where it does,
    /// and with the links of a heap of that list written 16 before <c>MessageHeapFree</c> frees it, so that a walk of the list that reached
    /// it after its free would end the run on the system's signal, where the pool keeps a freed heap's page readable under the fence
    /// (<c>knowledge/a-freed-heap-page-stays-readable-under-the-fence.md</c>).
    /// </summary>
    private static (string Path, string Old, string New)[] UnlistedHeaps =>
    [
        ("runtime/common/runtime.kz", "pub byte* ActorNew(byte* heap, long size, long stack, byte* side, byte* name) {\n",
            "pub byte* ActorNew(byte* heap, long size, long stack, byte* side, byte* name) {\n    Unbuilt(heap)\n"),
        ("runtime/common/runtime.kz", "pub void HeapLeft(byte* heap) {\n", """
            void Unbuilt(byte* heap) {
                raw {
                    mut byte* at = messageHeaps
                    while at != null {
                        if at == heap {
                            WriteError("listed\n")
                            sys.Exit(3)
                        }

                        at = *Ptr<byte*>(std.HeapFirst(at))
                    }
                }
            }

            pub void HeapLeft(byte* heap) {
                Unbuilt(heap)

            """),
        ("runtime/common/runtime.kz", "    Unlisted(heap)\n    std.HeapFree(heap)\n", """
                Unlisted(heap)
                raw {
                    *Ptr<long>(std.HeapFirst(heap)) = 16
                    *Ptr<long>(std.HeapFirst(heap) + 8) = 16
                }

                std.HeapFree(heap)

            """),
    ];

    [Fact]
    public void A_spawns_heap_leaves_the_list_of_heaps_being_built_before_its_actor_takes_it_or_a_case_frees_it()
    {
        // section 6 (the owner, 2026-11-21, #519, comment 7060155980, "T82 1"): inside `Front`'s message, the heap of each `spawn` with
        // an index after the crossing list, or with an argument that can wait, is listed with `Front`; it leaves that list before
        // `ActorNew` writes the actor's record over the words that list it, and before the end of the region of `Made` frees it, where
        // the case `int.Parse` propagates out of the argument after the list leaves the `spawn` without its actor; a heap left on the
        // list would name an actor's record, or a freed heap, to the next walk of the list; under the allocator that makes every freed
        // block inaccessible
        const string text = """
            actor Holder(List<int> Items, int N) {
                pub int Count() => Items.Count + N
            }

            int | ParseError Made(List<int> xs, string s) {
                h = spawn Holder(xs, int.Parse(s)) on crash stop
                return h.Count()
            }

            actor Front {
                pub int Run(List<int> ks) {
                    xs = [1, 2, 3]
                    h = spawn Holder(xs, ks[0]) on crash stop
                    w = spawn Holder(xs, h.Count()) on crash stop
                    n = Made(xs, "x") else {
                        ParseError => -1
                    }

                    m = Made(xs, "4") else {
                        ParseError => -1
                    }

                    return h.Count() + w.Count() + n + m
                }
            }

            f = spawn Front()
            print(f.Run([1]))

            """;
        Ran(BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. UnlistedHeaps]), "actor-spawn-heap-unlisted"), 0, ["17"], "");
    }

    [Fact]
    public void A_spawn_whose_argument_raises_before_its_heap_is_made_leaves_no_heap()
    {
        // section 6 (the owner, 2026-11-23, #519, comment 7095825398, "T87 3"): a `spawn` makes the new actor's heap just before its first
        // argument that crosses into it, or after its last where none crosses, so that the index in `Ahead`, before the crossing list, and
        // the index in `Plain`, whose `spawn` has no crossing argument, crash `Front` with no heap made; the heap made before the first
        // crossing argument is listed with `Front` where that argument can raise, the index in `Inside`, or one after it can wait, the
        // property `Size`, whose index raises, so that the crash frees it; the count of the heaps of the thread moves by none at each of
        // those crashes, as at a crash in `Before`, which raises before its `spawn`, where a heap made before the arguments, or listed with
        // nothing, would stay, one heap more; all after a first crash, whose restart moved the count by one where the later ones move it by
        // none; the calls with an index in range are the control that the count sees the heap of each actor a `spawn` made; under the
        // allocator that makes every freed block inaccessible, with a heap that an actor takes or `HeapLeft` frees checked to stand on no
        // list, and a listed heap's links written before `MessageHeapFree` frees it (`UnlistedHeaps`)
        const string text = """
            actor Holder(List<int> Items, int N) {
                pub int Count() => Items.Count + N
            }

            actor Lead(int N, List<int> Items) {
                pub int Count() => Items.Count + N
            }

            actor Pair(int A, int N) {
                pub int Count() => A + N
            }

            actor Front {
                mut int at = 0
                List<int> sizes = [1, 2]

                int Size => sizes[at]

                pub void Before(List<int> ks, int i) {
                    k = ks[i]
                    h = spawn Holder([1, 2, 3], k) on crash stop
                    print(h.Count())
                }

                pub void Ahead(List<int> ks, int i) {
                    h = spawn Lead(ks[i], [1, 2, 3]) on crash stop
                    print(h.Count())
                }

                pub void Plain(List<int> ks, int i) {
                    h = spawn Pair(i, ks[i]) on crash stop
                    print(h.Count())
                }

                pub void Inside(List<List<int>> rows, int i) {
                    h = spawn Holder(rows[i], 1) on crash stop
                    print(h.Count())
                }

                pub void Sized(int i) {
                    at = i
                    xs = [1, 2, 3]
                    h = spawn Holder(xs, Size) on crash stop
                    print(h.Count())
                }

                pub int Ping() => 2
            }

            f = spawn Front()
            send f.Before([1], 1)
            print(f.Ping())
            start = HttpServer.Heaps()
            send f.Ahead([1], 1)
            print(f.Ping())
            ahead = HttpServer.Heaps()
            send f.Plain([1], 1)
            print(f.Ping())
            plain = HttpServer.Heaps()
            send f.Inside([[1]], 1)
            print(f.Ping())
            inside = HttpServer.Heaps()
            send f.Sized(5)
            print(f.Ping())
            sized = HttpServer.Heaps()
            send f.Before([1], 1)
            print(f.Ping())
            before = HttpServer.Heaps()
            print("{ahead - start} {plain - ahead} {inside - plain} {sized - inside} {before - sized}")
            f.Ahead([1], 0)
            f.Plain([1], 0)
            f.Inside([[1]], 0)
            f.Sized(1)
            print(HttpServer.Heaps() - before)

            """;
        var run = BuildAndRun(text, ListTests.Edited([.. ListTests.Fence, .. HeapCount, .. UnlistedHeaps]), "actor-spawn-raises-before-heap");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(
            [
                "Front#1 crashed: index-out-of-range at program.kz:20", "Front#1 crashed: index-out-of-range at program.kz:26",
                "Front#1 crashed: index-out-of-range at program.kz:31", "Front#1 crashed: index-out-of-range at program.kz:36",
                "Front#1 crashed: index-out-of-range at program.kz:17", "Front#1 crashed: index-out-of-range at program.kz:20",
            ],
            crashes);
        Ran((run.Code, run.Output, rest), 0, ["2", "2", "2", "2", "2", "2", "0 0 0 0 0", "4", "1", "2", "5", "4"], "");
    }

    [Theory]
    [InlineData("h = spawn Lead(Noted(\"before\", 1), Listed(\"crossing\"))", "before")]
    [InlineData("h = spawn Pair(Noted(\"first\", 1), Noted(\"last\", 2))", "first last")]
    [InlineData("h = spawn Holder(Listed(\"crossing\"), Noted(\"after\", 1))", "")]
    public void A_spawn_whose_heap_cannot_be_made_has_run_the_arguments_before_the_point_it_is_made_at_and_none_after(string statement, string printed)
    {
        // section 6 (the owner, 2026-11-23, #519, comment 7095825398, "T87 3"): where the new actor's heap cannot be made
        // (`allocation-failed`), the arguments before the point where it is made, just before the first argument that crosses into it or
        // after the last where none crosses, have run, and none from that point on; the runtime's `ActorHeapNew` edited to end the process
        // as a heap the system refuses does, with each argument printing as it runs
        var text = "actor Holder(List<int> Items, int N) {\n    pub int Count() => Items.Count + N\n}\n\n"
            + "actor Lead(int N, List<int> Items) {\n    pub int Count() => Items.Count + N\n}\n\n"
            + "actor Pair(int A, int N) {\n    pub int Count() => A + N\n}\n\n"
            + "int Noted(string s, int k) {\n    print(s)\n    return k\n}\n\n"
            + "List<int> Listed(string s) {\n    print(s)\n    return [1]\n}\n\n"
            + $"{statement}\nprint(h.Count())\n";
        var refused = ("runtime/common/runtime.kz", "    if heap == null {\n        AllocationFailed(144 + size)\n", "    if heap != null {\n        AllocationFailed(144 + size)\n");
        var run = BuildAndRun(text, ListTests.Edited([refused]), "actor-spawn-heap-refused");
        Ran((run.Code, run.Output, Regex.Replace(run.Error, @"\([0-9]+ bytes\)", "(N bytes)")), 1, printed.Split(' ', StringSplitOptions.RemoveEmptyEntries), "error: allocation-failed (N bytes)\n");
    }

    // ---- the block `attempt { ... }` (U46 to U50; assumed: T18 to T23 of #519, comment 6980152255) --------------------------------

    /// <summary>
    /// A round of <c>attempt</c>s by <c>i</c>: a <c>Note</c> thrown where <c>i % 3</c> is 0, the runtime's <c>index-out-of-range</c> where it
    /// is 2, else a string built from a copy of the instance <c>kept</c> the block takes; each <c>Crashed</c> read into a string by its
    /// <c>Reason</c>; and <c>Resident</c>, the resident memory in KiB.
    /// </summary>
    private const string AttemptRounds = """
        class Note(mut string Why, List<int> Codes) {
            pub string Text() => Why
        }

        int Use(Note n) => n.Codes.Count

        string Read(Note | RuntimeError r) {
            match r {
                Note n => return "{n.Why} {n.Codes}"
                RuntimeError e => return "{e.Kind}"
            }
        }

        string Round(Note kept, int i) {
            v = attempt {
                if i % 3 == 0 {
                    throw Note("lost {i}", [i, 2])
                }

                xs = [1]
                return "{Use(copy kept)} {xs[i % 3 - 1]}"
            } else {
                Crashed c => Read(c.Reason)
            }

            return v
        }

        long | IoError Resident() {
            bytes = File.ReadAll("/proc/self/status")
            text = string.FromUtf8(bytes) else {
                InvalidUtf8 => ""
            }

            for line in text.Split('\n') {
                if line.StartsWith("VmRSS:") {
                    return long.Parse(line.Replace("VmRSS:", "").Replace("kB", "").Trim()) else {
                        ParseError => -1
                    }
                }
            }

            return -1
        }

        held = Note("kept", [1, 2, 3])

        """;

    [Fact]
    public void An_attempts_child_heap_is_freed_at_its_end_and_at_its_crash()
    {
        // U50 (assumed: the small defaults of comment 6980152255): the child of each `attempt` has a heap of its own, freed whole where its
        // block gives its value, where it throws and where the runtime's error ends it, so that the thread holds as many heaps after three
        // `attempt`s as before them
        const string program = AttemptRounds + """
            before = HttpServer.Heaps()
            print(Round(held, 1))
            print(Round(held, 0))
            print(Round(held, 2))
            print(HttpServer.Heaps() - before)

            """;
        var run = BuildAndRun(program, ListTests.Edited(HeapCount), "actor-attempt-heaps");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(2, crashes.Count);
        Ran((run.Code, run.Output, rest), 0, ["3 1", "lost 0 [0, 2]", "IndexOutOfRange", "0"], "");
    }

    [Fact]
    public void Memory_stays_flat_over_ten_thousand_attempts()
    {
        // U47, U50: each child's heap and stack, the copy of what its block takes, each crash's message, its thrown copy and its `Crashed`,
        // and the value that crosses back are let go of, so that 10,000 `attempt`s after 1,000 that warm the pools, two thirds of them
        // crashes, grow the resident memory by less than 256 KiB
        if (!OperatingSystem.IsLinux())
        {
            return; // no `/proc/self/status` reads the resident memory on Windows
        }

        const string program = AttemptRounds + """
            mut total = 0
            for i in 0..<1000 {
                total = total + Round(held, i).Bytes.Count
            }

            before = Resident() else {
                IoError e => -1
            }

            for i in 0..<10000 {
                total = total + Round(held, i).Bytes.Count
            }

            after = Resident() else {
                IoError e => -1
            }

            print(total > 0)
            print(after - before)
            print(before)

            """;
        var run = BuildAndRun(program, toolchain.Runtime, "actor-attempt-memory");
        Assert.Equal((0, 334 + 333 + 3334 + 3333), (run.Code, Logged(run.Error).Crashes.Count));
        Assert.Equal("true", run.Output[0]);
        var grown = long.Parse(run.Output[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(grown < 256, $"10,000 attempts grow the resident memory by {grown} KiB");

        // the bound is read against a reading: `Resident` gives -1 where it reads none, which a growth of 0 would hide
        Assert.True(long.Parse(run.Output[2], System.Globalization.CultureInfo.InvariantCulture) > 0, $"the resident memory before the run read {run.Output[2]}");
    }

    [Fact]
    public void What_crosses_into_and_out_of_the_child_of_an_attempt_is_a_copy_in_the_heap_it_reaches()
    {
        // U47 with U12 (assumed: T19 of comment 6980152255): the block's copy of a mutable instance, its list among it, in the child's heap;
        // the block's value, a list of strings, copied into the heap of the code that waits; and the thrown instance moved there before the
        // child's heap is freed (U50, the small defaults of that comment), each read after that free, under the allocator that makes every
        // freed block inaccessible; the crash writes its one log line, named `attempt` (U50; assumed: T23 of that comment)
        const string text = """
            class Note(mut string Why, List<int> Codes) {
                pub string Text() => Why
            }

            List<string> Words(Note n) => [n.Why, "{n.Codes}"]

            List<string> Fail(int n) {
                if n > 0 {
                    throw Note("lost", [n, 4])
                }

                return []
            }

            List<string> Read(Note | RuntimeError r) {
                match r {
                    Note n => return Words(n)
                    RuntimeError e => return [e.Detail]
                }
            }

            kept = Note("kept", [1, 2])
            xs = attempt { Words(copy kept) } else {
                Crashed c => ["crashed"]
            }

            print(xs)
            ys = attempt { Fail(3) } else {
                Crashed c => Read(c.Reason)
            }

            print(ys)
            print(kept.Why)

            """;
        var run = BuildAndRun(text, ListTests.Fenced, "actor-attempt-copies");
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["attempt#2 crashed: thrown \"lost\" at program.kz:9"], crashes);
        Ran((run.Code, run.Output, rest), 0, ["[kept, [1, 2]]", "[lost, [3, 4]]", "kept"], "");
    }

    [Fact]
    public void An_attempt_of_the_top_level_code_outside_a_within_block_has_no_deadline()
    {
        // U49 (assumed: T26 of comment 6982985447): the block's message roots a chain with no deadline, so that a call of the block made past
        // the default bound of a call, 5 seconds (U36), is bounded on its own and runs
        if (OperatingSystem.IsWindows())
        {
            return; // a build for Windows says `not supported` to the reads of the clock (S11)
        }

        const string text = """
            // the thread held for `d`, with no call and no wait
            void Hold(duration d) {
                instant start = instant.Now
                while start.Elapsed < d {
                }
            }

            actor Store {
                pub int Get() => 1
            }

            s = spawn Store()
            v = attempt {
                Hold(5200ms)
                return s.Get()
            } else {
                Crashed c => {
                    print(c.Reason.Kind)
                    -1
                }
            }

            print(v)

            """;
        var run = BuildAndRun(text, toolchain.Runtime, "actor-attempt-unbounded");
        var (crashes, rest) = Logged(run.Error);
        Assert.Empty(crashes);
        Ran((run.Code, run.Output, rest), 0, ["1"], "");
    }

    // ---- the tests of `kurz test` (chapter 20, Y5; issue 586) ---------------------------------------------------------------------

    [Fact]
    public void The_end_of_a_test_ends_every_actor_it_made_with_their_timers_pauses_blocks_and_stacks()
    {
        // Y5, F6, Q2: the first test leaves a child paused by its restart, a child whose crash's block waits in the test's actor with the
        // stack it raised on, a child parked on the paused one with its timer, and a child with a `send` queued; the second meets
        // `deadlock` while it builds a message in a heap of its own, one of whose arguments waits on a child whose block waits in the
        // test's actor; the third runs alone, and after each test no timer, no pause, no queued actor and no heap of messages is left,
        // and every stack is back in the pool, under the allocator that makes every freed block inaccessible
        var counted = ListTests.Edited([.. ListTests.Fence, ("runtime/common/runtime.kz", "        testWords = null\n        testWordsCount = 0\n        testWordsRoom = 0\n    }\n}\n",
            "        testWords = null\n        testWordsCount = 0\n        testWordsRoom = 0\n    }\n\n    mut long left = 0\n    mut bool unpaused = false\n    mut bool unqueued = false\n" +
            "    mut bool unheaped = false\n    mut long stacks = 0\n    raw {\n        mut byte* at = timers\n        while at != null {\n            left = left + 1\n            at = *Ptr<byte*>(at + 32)\n        }\n\n" +
            "        unpaused = paused == null\n        unqueued = runFirst == null\n        unheaped = messageHeaps == null\n        stacks = pooledCount\n    }\n\n" +
            "    Print(1, \"timers {left} paused {!unpaused} queued {!unqueued} heaps {!unheaped} pooled {stacks}\", \"\", 0)\n}\n")]);
        const string text = """
            actor Printer {
                pub void Say(string s) {
                    print(s)
                }
            }

            actor Crasher {
                pub void Fail() {
                    throw "down"
                }

                pub int Get() => 1
            }

            actor Asker {
                pub void Ask(Crasher c) {
                    print("asked {c.Get()}")
                }
            }

            actor Pinger {
                pub int Ping() => 7
            }

            test "leaves actors behind" {
                c = spawn Crasher() on crash restart after 30s
                send c.Fail()
                d = spawn Crasher() on crash (k) {
                    stop
                }

                send d.Fail()
                a = spawn Asker()
                send a.Ask(c)
                q = spawn Pinger()
                print(q.Ping())
                p = spawn Printer()
                send p.Say("never")
                print("first")
            }

            test "deadlocks while it builds a message" {
                w = spawn Crasher() on crash (k) {
                    restart
                }

                send w.Fail()
                p = spawn Printer()
                send p.Say("{w.Get()}")
            }

            test "starts with none" {
                print("second")
            }

            """;
        var run = BuildAndRun(text, counted, "test-actors-ended", tests: true);
        var (crashes, rest) = Logged(run.Error);
        Assert.Equal(["Crasher#2 crashed: thrown \"down\" at program.kz:9"], crashes); // the test's actor is #1, and the block of `d` never ran
        // the loop of the second test's wait under its `FAILED` line (Y5, U64; the owner, 2026-11-23, #622, comment 7095825069, 1), each chain's ID as `<chain>`
        Ran((run.Code, run.Output.Select(l => RunTests.Chains(l)).ToList(), rest), 1, ["7", "first", "test \"leaves actors behind\" ... ok", "timers 0 paused false queued false heaps false pooled 4",
            "test \"deadlocks while it builds a message\" ... FAILED (seed 0)", "    deadlock at program.kz:42", "    test#7 waits at program.kz:49 on Crasher#8 in chain <chain>",
            "    Crasher#8 waits for the decision of test#7 on its crash in chain <chain>", "timers 0 paused false queued false heaps false pooled 4", "second",
            "test \"starts with none\" ... ok", "timers 0 paused false queued false heaps false pooled 4", "3 tests: 2 passed, 1 failed"], "");
    }

    [Fact]
    public void A_test_has_no_bound_of_time_so_that_it_calls_on_past_the_default_bound_of_a_call()
    {
        // D6, Y5: the test's message carries no deadline, so a test that holds its thread past the default bound of a call, here 10 ms
        // for the run, calls on and its call is answered; a read of the clock is `not supported` in a build for Windows (S11)
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var bounded = ListTests.Edited(("runtime/linux/linux.kz", "pub long DefaultBound() => 5000000000", "pub long DefaultBound() => 10000000"));
        const string text = """
            actor Pinger {
                pub int Ping() => 1
            }

            void Hold(duration d) {
                instant start = instant.Now
                while start.Elapsed < d {
                }
            }

            test "outlasts the bound" {
                p = spawn Pinger()
                Hold(100ms)
                print(p.Ping())
            }

            """;
        Ran(BuildAndRun(text, bounded, "test-no-bound", tests: true), 0, ["1", "test \"outlasts the bound\" ... ok", "1 test: 1 passed, 0 failed"], "");
    }

    [Fact]
    public void The_words_of_a_tests_crash_stand_under_its_line_whatever_the_filter_of_the_log()
    {
        // Q3, F1, F3, P4: the crash of a test's own actor writes no log line, and its words stand under its `FAILED` line with the log of
        // `Kurz.Actor` off too; the other test spawns, so that the program reads `KURZ_LOG` (P5), which it reads on Linux alone
        const string text = """
            actor Pinger {
                pub int Ping() => 1
            }

            test "spawns" {
                p = spawn Pinger()
                print(p.Ping())
            }

            test "throws" {
                throw "down"
            }

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true, tests: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var executable = toolchain.Build(compilation.Program, "test-words-log-off");
        string[] lines = ["1", "test \"spawns\" ... ok", "test \"throws\" ... FAILED (seed 0)", "    thrown \"down\" at program.kz:11", "2 tests: 1 passed, 1 failed"];
        Ran(Toolchain.Run(executable, "0"), 1, lines, "");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var off = Processes.Capture(executable, [], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0", ["KURZ_LOG"] = "Kurz.Actor=None" });
        Assert.Equal((1, string.Join("\n", lines) + "\n", ""), (off.Code, off.Output, off.Error));
    }
}
