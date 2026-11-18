using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The protocols by which actors can run on any number of threads, transcribed into C# and explored, as loom explores the
/// interleavings of the threads of a Rust test and CDSChecker those of a C11 program (neither checked at source): the actor's state
/// word, whose wakes, takes and ends of a hold say whether a thread holds the actor and whether an entry of it stands in the run queue,
/// the reply of a call that waits, which its callee and the crash of its waiter each take by one atomic swap, and the queue of many
/// senders an actor's messages are put on, which its holder moves into its own lists (<c>Pushed</c>, <c>Popped</c>). Each move of a model
/// is one atomic operation of the runtime, or one plain read or write that no other thread races, and names the function of
/// <c>runtime/common/runtime.kz</c> it transcribes, with the text of the atomic operation it is there and the texts of the masks of
/// the state word's bits it decides by; a test holds every name and every such text to that file, a guard against a model that says
/// what the runtime no longer does, so that a change of one of those lines, which one thread cannot show, turns it red. The explorer runs every
/// interleaving of two or three threads, each state once, and finds no lost wake (a message queued or a reply set always leads to the
/// actor or the waiter running), no two owners of a reply, no two threads holding one actor, no message while its constructor runs
/// (U17) or before its restart (U22), and no owner of a reply, and no timer, that reads or writes the waiter's frame once it is gone; the
/// timer that fires at a call's deadline wakes the waiter and owns no reply (U36, U37); no <c>Ready</c> reads the reply of a wait that
/// returned, which <c>Wait</c> takes off its activation at its read of the set word; no message of a queue lost, moved twice or moved
/// before an earlier message of its sender (U5); and it finds each of these broken in a model
/// broken so, the cases of <see cref="Break"/>. Its moves are sequentially consistent: the orders of the runtime's atomic operations,
/// acquire and release, are the IR's to show (<c>EmitterTests</c>), not the explorer's.
/// </summary>
public sealed class ModelTests
{
    /// <summary>The breaks the explorer finds, each a change of one move of a model.</summary>
    public enum Break
    {
        /// <summary>The models as the runtime is.</summary>
        None,

        /// <summary>The emitted call stores the reply's waiter after <c>Enqueue</c> queued its message.</summary>
        WaiterAfterEnqueue,

        /// <summary>The callee and the crash take the message's reply by a plain read and then a plain write of null, not by one swap.</summary>
        ReadThenWrite,

        /// <summary>A take that finds the actor held drops its entry and sets no NOTIFIED.</summary>
        NotifiedDropped,

        /// <summary>The end of a hold queues the actor again where it can go on alone, NOTIFIED left out.</summary>
        RequeueLeftOut,

        /// <summary>A take reads the actor as held by BUILDING alone, RUNNING left out.</summary>
        RunningNotHeld,

        /// <summary>A take reads the actor as held by RUNNING alone, BUILDING left out.</summary>
        BuildingNotHeld,

        /// <summary><c>ActorBuilt</c> clears NOTIFIED and queues nothing.</summary>
        BuiltQueuesNothing,

        /// <summary><c>Replied</c> reads the reply's waiter after its store of the reply's word.</summary>
        WaiterReadAfterStore,

        /// <summary>The crash gives the waiter's stack back at once where the callee took the reply first.</summary>
        CrashLeavesAtOnce,

        /// <summary>A restart holds the actor by its compare-exchange whether a take holds it or not.</summary>
        RestartHoldsNothing,

        /// <summary><c>Dropped</c> takes the reply of a message past its deadline and sets no word and wakes nothing.</summary>
        DropSignalsNothing,

        /// <summary><c>Fire</c> takes the reply off the timers at its deadline and wakes nothing.</summary>
        TimerWakesNothing,

        /// <summary>A wait that its deadline ended gives the waiter's stack back at once, with no unhook of its reply.</summary>
        TimeoutLeavesAtOnce,

        /// <summary><c>Fall</c> gives the waiter's stack back with its reply still among the timers.</summary>
        TimerKeptPastFrame,

        /// <summary>A timed wait that read a set word goes on at <c>Ready</c>, which reads the reply, not at the end of the hold.</summary>
        ReadyAfterReturn,

        /// <summary>A sender wakes the actor after its swap of the queue's end and before it links its message.</summary>
        WakeBeforeLink,

        /// <summary><c>Popped</c> returns null once it pushed the stub, with no second load of the link of the message it holds.</summary>
        StubPushedNotRead,
    }

    /// <summary>
    /// One atomic move of a strand: the runtime's function it transcribes, the text of that function it is, its atomic operation where it
    /// is one, null where the function's name alone is held, whether it can be made in a state, what it does, which sets the strand's
    /// place, and the texts of that function that decide what it does, the masks of the state word's bits among them.
    /// </summary>
    private sealed record Move(string Function, string? Text, Func<int[], bool> Can, Action<int[]> Make, string[]? Rules = null);

    /// <summary>A thread of a model: its name, the word of the state that holds its place, -1 once it ended, and its moves by place.</summary>
    private sealed record Strand(string Name, int Place, Move[] Moves);

    /// <summary>A model: its name, its first state, its strands, what every state keeps, and what a state in which no strand can move keeps.</summary>
    private sealed record Model(string Name, int[] Start, Strand[] Strands, Func<int[], string?> Always, Func<int[], string?> AtRest);

    /// <summary>A state that breaks what its model keeps: what it breaks, and the moves that reached it.</summary>
    private sealed record Broken(string What, IReadOnlyList<string> Path);

    /// <summary>The most moves of a path the explorer follows; a longer one is a break of its own.</summary>
    private const int Bound = 200;

    /// <summary>
    /// Every interleaving of the model's strands from its first state, depth first, a state seen before not explored again, since what
    /// follows a state depends on that state alone: the first state that breaks what the model keeps, with the moves that reached it,
    /// or null, and the count of the states seen. A path longer than <see cref="Bound"/> moves is a break, so that a model that does not
    /// end is never read as one that keeps everything.
    /// </summary>
    private static (Broken? Broken, int States) Explore(Model model)
    {
        var seen = new HashSet<string>();
        var path = new List<string>();

        Broken? Visit(int[] state)
        {
            if (!seen.Add(string.Join(",", state)))
            {
                return null;
            }

            var what = model.Always(state);
            if (what is not null)
            {
                return new Broken(what, path.ToList());
            }

            if (path.Count >= Bound)
            {
                return new Broken($"a path of more than {Bound} moves", path.ToList());
            }

            var moved = false;
            foreach (var strand in model.Strands)
            {
                var place = state[strand.Place];
                if (place < 0 || !strand.Moves[place].Can(state))
                {
                    continue;
                }

                moved = true;
                var after = (int[])state.Clone();
                strand.Moves[place].Make(after);
                path.Add($"{strand.Name}: {strand.Moves[place].Function}");
                var found = Visit(after);
                path.RemoveAt(path.Count - 1);
                if (found is not null)
                {
                    return found;
                }
            }

            var rest = moved ? null : model.AtRest(state);
            return rest is null ? null : new Broken(rest, path.ToList());
        }

        var broken = Visit((int[])model.Start.Clone());
        return (broken, seen.Count);
    }

    // The bits of the actor's state word (`StateWord`).
    private const int Running = 1, Queued = 2, Notified = 4, Building = 8;

    // The words every model's state holds first: the actor's state word, its entries in the run queue, the messages of its inbox, the
    // threads that hold it, whether its constructor ended, whether a message started while it ran; then the reply's: whether the
    // message names the reply, whether the message is queued, whether the reply names its waiter, the reply's word, the owners of the
    // reply, whether the waiter's frame is gone, whether the waiter parked, returned or crashed, and whether an owner of the reply read
    // or wrote the frame once it was gone; the actor's life, 1 while it waits for its restart; whether the reply stands among the timers,
    // whether the call's deadline passed at the scheduler's turn, and whether the waiter's `Ready` read the reply once its wait had
    // returned. Each strand's words follow: its place, the word it read, what `Ready` gave, a register.
    private const int Word = 0, Entries = 1, Mail = 2, Holders = 3, Built = 4, Early = 5;
    private const int Hook = 6, Published = 7, Waiter = 8, Answer = 9, Owners = 10, Gone = 11, Parked = 12, Returned = 13, Crashed = 14;
    private const int Touched = 15, Life = 16, Listed = 17, Due = 18, ReadLate = 19, Shared = 20, Own = 4;

    /// <summary>A first state: the shared words zero but those given, each strand at its place 0 but those given.</summary>
    private static int[] Start(int strands, params (int Word, int Value)[] set)
    {
        var state = new int[Shared + strands * Own];
        foreach (var (word, value) in set)
        {
            state[word] = value;
        }

        return state;
    }

    /// <summary>The first word of the strand numbered <paramref name="n"/>, its place.</summary>
    private static int At(int n) => Shared + n * Own;

    /// <summary>
    /// <c>Enqueue</c>'s append of a message to the inbox, one move, as a queue of many senders appends, with its read of the actor's life;
    /// then, where the actor does not wait for its restart, the wake: one atomic <c>or</c> of QUEUED, and where the mark was clear the
    /// entry put in the run queue (<c>Wake</c>, <c>Queue</c>).
    /// </summary>
    private static Strand Sender(string name, int at) => new(name, at,
    [
        new("Enqueue", "Pushed(state - 144, message)", _ => true, s => { s[Mail]++; s[at] = s[Life] != 0 ? -1 : 1; },
            ["if life != 1 && life != 5 {"]),
        .. Woken(at, 1, -1),
    ]);

    /// <summary>The wake of the actor from the place <paramref name="from"/>, the strand going on at <paramref name="then"/>.</summary>
    private static Move[] Woken(int at, int from, int then) =>
    [
        new("Wake", ".FetchOr(2, MemoryOrder.AcqRel)", _ => true, s =>
        {
            var before = s[Word];
            s[Word] |= Queued;
            s[at] = (before & Queued) == 0 ? from + 1 : then;
        }, ["if (before & 2) == 0 {"]),
        new("Queue", "runLast = state", _ => true, s => { s[Entries]++; s[at] = then; }),
    ];

    /// <summary>
    /// The take of an entry off the run queue at the place <paramref name="from"/> and the hold of the actor (<c>Claimed</c>: a load, then
    /// a compare-exchange, made again on what it found): RUNNING where neither RUNNING nor BUILDING is set, the strand going on at
    /// <paramref name="held"/>, else NOTIFIED, the strand back at <paramref name="from"/>; QUEUED cleared either way.
    /// </summary>
    private static Move[] Taken(int at, int from, int held, Break broken)
    {
        var holds = broken switch { Break.RunningNotHeld => Building, Break.BuildingNotHeld => Running, _ => Running | Building };
        return
        [
            new("RunOne", "runFirst = *Ptr<byte*>(state - 16)", s => s[Entries] > 0, s => { s[Entries]--; s[at] = from + 1; }),
            new("Claimed", "word.Load(MemoryOrder.Acquire)", _ => true, s => { s[at + 1] = s[Word]; s[at] = from + 2; }),
            new("Claimed", "Exchanged(word, seen, wanted)", _ => true, s =>
            {
                var seen = s[at + 1];
                if (s[Word] != seen)
                {
                    s[at + 1] = s[Word];
                    return;
                }

                if ((seen & holds) != 0)
                {
                    s[Word] = (seen & ~Queued) | (broken == Break.NotifiedDropped ? 0 : Notified);
                    s[at] = from;
                    return;
                }

                s[Word] = (seen & ~Queued) | Running;
                s[Holders]++;
                s[at] = held;
            }, ["held = (seen & 9) != 0", "mut int wanted = (seen & ~2) | 1", "wanted = (seen & ~2) | 4"]),
        ];
    }

    /// <summary>
    /// The end of the hold at the place <paramref name="from"/> (<c>LetGo</c>: a load, then a compare-exchange, made again on what it
    /// found), with what <c>Ready</c> gave in the strand's third word: RUNNING and NOTIFIED cleared, and where no entry stands while
    /// NOTIFIED was set or the actor can go on, QUEUED set and the entry put in the run queue; the strand going on at the place
    /// <paramref name="then"/> gives for the state.
    /// </summary>
    private static Move[] LetGone(int at, int from, Func<int[], int> then, Break broken) =>
    [
        new("LetGo", "word.Load(MemoryOrder.Acquire)", _ => true, s => { s[at + 1] = s[Word]; s[at] = from + 1; }),
        new("LetGo", "Exchanged(word, seen, wanted)", _ => true, s =>
        {
            var seen = s[at + 1];
            if (s[Word] != seen)
            {
                s[at + 1] = s[Word];
                return;
            }

            var notified = broken != Break.RequeueLeftOut && (seen & Notified) != 0;
            var queue = (seen & Queued) == 0 && (notified || s[at + 2] != 0);
            s[Word] = (seen & ~(Running | Notified)) | (queue ? Queued : 0);
            s[Holders]--;
            s[at] = queue ? from + 2 : then(s);
        }, ["queue = (seen & 2) == 0 && ((seen & 4) != 0 || ready)", "mut int wanted = seen & ~5", "wanted = wanted | 2"]),
        new("Queue", "runLast = state", _ => true, s => { s[Entries]++; s[at] = then(s); }),
    ];

    /// <summary>
    /// A thread of the scheduler: the take of an entry and the hold (<c>RunOne</c>, <c>Claimed</c>), the first message of the inbox
    /// started where it holds the actor and the actor does not wait for its restart (<c>RunOne</c>), <c>Ready</c> read after the switch
    /// back, and the end of the hold (<c>LetGo</c>); then another entry. One that starts at the place 4 holds the actor already, its
    /// message ran.
    /// </summary>
    private static Strand Runner(string name, int at, Break broken) => new(name, at,
    [
        .. Taken(at, 0, 3, broken),
        new("RunOne", "Start(state, message, null)", _ => true, s =>
        {
            if (s[Mail] > 0 && s[Life] == 0)
            {
                s[Mail]--;
                s[Early] |= s[Built] == 0 || s[Life] != 0 ? 1 : 0;
                s[at] = 4;
                return;
            }

            s[at + 2] = 0;
            s[at] = 5;
        }, ["if life == 1 || life == 5 {"]),
        new("Ready", "return *Ptr<byte*>(state - 32) != null", _ => true, s => { s[at + 2] = s[Mail] > 0 ? 1 : 0; s[at] = 5; }),
        .. LetGone(at, 5, _ => 0, broken),
    ]);

    /// <summary>
    /// The restart of the actor, whose pause ended while it waits for its restart (<c>Restart</c>): the hold outside the run queue
    /// (<c>HeldAlone</c>: a load, then a compare-exchange that sets RUNNING from a word in which no thread holds it, made again on what it
    /// found, and waiting while a take holds it), the life set to 0, the restart's message run, <c>Ready</c> read after the switch back,
    /// and the end of the hold (<c>LetGo</c>).
    /// </summary>
    private static Strand Restarter(int at, Break broken) => new("restarter", at,
    [
        new("Restart", "HeldAlone(state)", _ => true, s => { s[at] = 1; }),
        new("HeldAlone", "word.Load(MemoryOrder.Acquire)", _ => true, s => { s[at + 1] = s[Word]; s[at] = 2; }),
        new("HeldAlone", "Exchanged(word, seen, seen | 1)",
            s => broken == Break.RestartHoldsNothing || (s[at + 1] & Running) == 0 || s[Word] != s[at + 1], s =>
            {
                var seen = s[at + 1];
                if (s[Word] != seen)
                {
                    s[at + 1] = s[Word];
                    return;
                }

                s[Word] = seen | Running;
                s[Holders]++;
                s[at] = 3;
            }, ["if (seen & 1) == 0 {"]),
        new("Restart", "*Ptr<int>(state - 88) = 0", _ => true, s => { s[Life] = 0; s[at] = 4; }),
        new("Restart", "Start(state, message, null)", _ => true, s => { s[at] = 5; }),
        new("Ready", "return *Ptr<byte*>(state - 32) != null", _ => true, s => { s[at + 2] = s[Mail] > 0 ? 1 : 0; s[at] = 6; }),
        .. LetGone(at, 6, _ => -1, broken),
    ]);

    /// <summary>
    /// The end of the constructor at its <c>spawn</c> (<c>ActorBuilt</c>: a load, then a compare-exchange, made again on what it found):
    /// BUILDING and NOTIFIED cleared, and where NOTIFIED was set and no entry stands, QUEUED set and the entry put in the run queue.
    /// </summary>
    private static Strand Builder(int at, Break broken) => new("builder", at,
    [
        new("ActorBuilt", "word.Load(MemoryOrder.Acquire)", _ => true, s => { s[at + 1] = s[Word]; s[at] = 1; }),
        new("ActorBuilt", "Exchanged(word, seen, wanted)", _ => true, s =>
        {
            var seen = s[at + 1];
            if (s[Word] != seen)
            {
                s[at + 1] = s[Word];
                return;
            }

            var queue = broken != Break.BuiltQueuesNothing && (seen & (Queued | Notified)) == Notified;
            s[Word] = (seen & ~(Building | Notified)) | (queue ? Queued : 0);
            s[Built] = 1;
            s[at] = queue ? 2 : -1;
        }, ["queue = (seen & 6) == 4", "mut int wanted = seen & ~12", "wanted = wanted | 2"]),
        new("Queue", "runLast = state", _ => true, s => { s[Entries]++; s[at] = -1; }),
    ]);

    /// <summary>What every state of a model keeps.</summary>
    private static string? Always(int[] s) =>
        s[Holders] > 1 ? "two threads hold the actor"
        : s[Early] != 0 ? "a message started while the actor's constructor ran or before its restart"
        : s[Owners] > 1 ? "two owners of the reply"
        : s[Touched] != 0 ? "an owner of the reply read or wrote the waiter's frame once it was gone"
        : s[ReadLate] != 0 ? "Ready read the reply once its wait had returned, which Wait took off the activation"
        : null;

    /// <summary>What a state of the state word's models in which no thread can move keeps.</summary>
    private static string? WordAtRest(int[] s) =>
        s[Mail] > 0 ? $"{s[Mail]} message(s) wait in the inbox and nothing runs the actor: a lost wake"
        : s[Word] != 0 ? $"the state word is {s[Word]} with no thread moving, not idle"
        : null;

    /// <summary>The models of the state word, each of three threads.</summary>
    private static Model[] WordModels(Break broken) =>
    [
        // a message sent while the actor's constructor runs: it starts once `ActorBuilt` ran (U17)
        new("a wake while the constructor runs", Start(3, (Word, Building)),
            [Builder(At(0), broken), Sender("sender", At(1)), Runner("runner", At(2), broken)], Always, WordAtRest),

        // a message sent while a thread holds the actor: a second thread takes its entry, which the holder queues again
        new("a wake while a thread holds the actor", Start(3, (Word, Running), (Holders, 1), (Built, 1), (At(0), 4)),
            [Runner("holder", At(0), broken), Runner("runner", At(1), broken), Sender("sender", At(2))], Always, WordAtRest),

        // two senders, one thread
        new("two wakes", Start(3, (Built, 1)),
            [Sender("first", At(0)), Sender("second", At(1)), Runner("runner", At(2), broken)], Always, WordAtRest),

        // a restart while an entry of the actor, queued before its crash, stands in the run queue with a message in its inbox, and a
        // message sent meanwhile: a take passes the actor over while it waits for its restart, and the restart waits for that take
        new("a take while a restart starts", Start(3, (Built, 1), (Life, 1), (Word, Queued), (Entries, 1), (Mail, 1)),
            [Restarter(At(0), broken), Runner("runner", At(1), broken), Sender("sender", At(2))], Always, WordAtRest),
    ];

    /// <summary>
    /// The waiter of a call, whose thread holds its actor at the start: the reply's waiter stored (<c>ReplyWaiter</c>) and the message
    /// queued (<c>Enqueue</c>), the reply's word read (<c>Wait</c>), and where it is 0 the activation parked: <c>Ready</c> and the end of
    /// the hold; then the takes of the actor's entries, each resuming the activation where the word is set (<c>RunOne</c>), as long as it
    /// neither returned nor crashed.
    /// </summary>
    private static Strand Waiting(int at, Break broken)
    {
        var late = broken == Break.WaiterAfterEnqueue;
        Move stored = new("ReplyWaiter", "*Ptr<byte*>(reply + 8) = running", _ => true, s => { s[Waiter] = 1; s[at] = late ? 2 : 1; });
        Move queued = new("Enqueue", "Pushed(state - 144, message)", _ => true, s => { s[Published] = 1; s[at] = late ? 1 : 2; });
        int Then(int[] s) => s[Returned] != 0 || s[Crashed] != 0 ? -1 : 7;
        return new("waiter", at,
        [
            late ? queued : stored,
            late ? stored : queued,
            new("Wait", "while ReplyState(reply) == 0", _ => true, s =>
            {
                if (s[Answer] != 0)
                {
                    s[Returned] = 1;
                    s[Gone] = 1;
                    s[at + 2] = 0;
                    s[at] = 4;
                    return;
                }

                s[Parked] = 1;
                s[at] = 3;
            }),
            new("Ready", "ReplyState(reply) != 0", _ => true, s => { s[at + 2] = s[Answer] != 0 ? 1 : 0; s[at] = 4; }),
            .. LetGone(at, 4, Then, broken),
            .. Taken(at, 7, 10, broken),
            new("RunOne", "if reply != null && (ReplyState(reply) != 0 || Waited(reply))", _ => true, s =>
            {
                if (s[Crashed] == 0 && s[Answer] != 0)
                {
                    s[Returned] = 1;
                    s[Gone] = 1;
                }

                s[at + 2] = 0;
                s[at] = 4;
            }),
        ]);
    }

    /// <summary>The count of the moves of <see cref="Took"/>.</summary>
    private static int Takes(Break broken) => broken == Break.ReadThenWrite ? 2 : 1;

    /// <summary>
    /// The take of the message's reply at the place <paramref name="from"/> (<c>ReplyTaken</c>, one swap), or, broken so, a plain read of
    /// it and then a plain write of null: the strand going on at <paramref name="owned"/> where it took the reply and at
    /// <paramref name="lost"/> where another took it first.
    /// </summary>
    private static Move[] Took(int at, int from, int owned, int lost, Func<int[], bool> can, Break broken)
    {
        void Owned(int[] s, bool reply)
        {
            if (reply)
            {
                s[Owners]++;
            }

            s[at] = reply ? owned : lost;
        }

        return Takes(broken) == 2
            ?
            [
                new("ReplyTaken", null, can, s => { s[at + 3] = s[Hook]; s[at] = from + 1; }),
                new("ReplyTaken", null, _ => true, s => { s[Hook] = 0; Owned(s, s[at + 3] != 0); }),
            ]
            :
            [
                new("ReplyTaken", ".Swap(null, MemoryOrder.AcqRel)", can, s =>
                {
                    var reply = s[Hook] != 0;
                    s[Hook] = 0;
                    Owned(s, reply);
                }),
            ];
    }

    /// <summary>
    /// The callee, once its message is queued: the take of the reply (<c>ReplyTaken</c>), and where it owns it, <c>Replied</c>: the
    /// waiter read, then the release store of the reply's word, then the wake of the waiter's actor (<c>Wake</c>).
    /// </summary>
    private static Strand Callee(int at, Break broken)
    {
        var first = Takes(broken);
        var late = broken == Break.WaiterReadAfterStore;
        Move read = new("Replied", "byte* waiter = *Ptr<byte*>(reply + 8)", _ => true, s =>
        {
            s[Touched] |= s[Gone];
            s[at + 1] = s[Waiter];
            s[at] = late ? first + 2 : first + 1;
        });
        Move store = new("Replied", ".Store(1, MemoryOrder.Release)", _ => true, s =>
        {
            s[Touched] |= s[Gone];
            s[Answer] = 1;
            s[at] = late ? first + 1 : first + 2;
        });
        return new("callee", at,
        [
            .. Took(at, 0, first, -1, s => s[Published] != 0, broken),
            late ? store : read,
            late ? read : store,
            new("Replied", "Wake(state)", _ => true, s => { s[at] = s[at + 1] != 0 ? first + 3 : -1; }),
            .. Woken(at, first + 3, -1),
        ]);
    }

    /// <summary>
    /// The crash of the waiter's actor while its activation is parked: the hold of the actor by the crash, from a state word no thread
    /// holds (<c>Fell</c>, as the record's default of a crash through the state word has it, which the build makes on its one thread);
    /// the reply taken off the timers (<c>Fall</c>); where the reply's word is 0 (<c>Fall</c>), the take of the reply
    /// (<c>ReplyTaken</c>), and where the callee took it first, the wait for its word (<c>Fall</c>); then the stack given back, the
    /// activation gone, and the end of the hold (<c>LetGo</c>).
    /// </summary>
    private static Strand Crash(int at, Break broken)
    {
        var wait = 3 + Takes(broken);
        var lost = broken == Break.CrashLeavesAtOnce ? wait + 1 : wait;
        return new("crash", at,
        [
            new("Fell", null, s => s[Parked] != 0 && s[Returned] == 0 && s[Crashed] == 0 && (s[Word] & (Running | Building)) == 0, s =>
            {
                s[Word] |= Running;
                s[Holders]++;
                s[at] = 1;
            }),
            new("Fall", "TimerRemove(waited)", _ => true, s =>
            {
                s[Listed] = broken == Break.TimerKeptPastFrame ? s[Listed] : 0;
                s[at] = 2;
            }),
            new("Fall", "ReplyState(waited) == 0", _ => true, s => { s[at] = s[Answer] != 0 ? wait + 1 : 3; }),
            .. Took(at, 3, wait + 1, lost, _ => true, broken),
            new("Fall", "while ReplyState(waited) == 0", s => s[Answer] != 0, s => { s[at] = wait + 1; }),
            new("Fall", "Give(at)", _ => true, s =>
            {
                s[Crashed] = 1;
                s[Gone] = 1;
                s[at + 2] = 0;
                s[at] = wait + 2;
            }),
            .. LetGone(at, wait + 2, _ => -1, broken),
        ]);
    }

    /// <summary>
    /// The waiter of a call with a deadline, whose thread holds its actor at the start: the reply's waiter stored (<c>ReplyWaiter</c>), the
    /// message queued (<c>Enqueue</c>), the reply among the timers (<c>TimerAdd</c>), the loop's test of the reply's word and of the
    /// deadline (<c>Wait</c>), and where neither holds the activation parked: <c>Ready</c> and the end of the hold, then the takes of the
    /// actor's entries, each resuming the activation where the word is set or the deadline passed (<c>RunOne</c>), which tests again.
    /// Out of the loop: the reply off the timers (<c>TimerRemove</c>) and its word read once; where it is set the wait returns, the
    /// activation naming the reply no more, and the strand goes on at the end of the hold, not at <c>Ready</c>, and else
    /// the deadline's <c>call-timeout</c> is the actor's crash, whose fall unhooks the reply as <see cref="Crash"/> does, the waiter's thread
    /// holding the actor; then the stack given back and the end of the hold.
    /// </summary>
    private static Strand TimedWaiting(int at, Break broken)
    {
        var wait = 15 + Takes(broken);
        var give = wait + 1;
        int Then(int[] s) => s[Returned] != 0 || s[Crashed] != 0 ? -1 : 8;
        return new("waiter", at,
        [
            new("ReplyWaiter", "*Ptr<byte*>(reply + 8) = running", _ => true, s => { s[Waiter] = 1; s[at] = 1; }),
            new("Enqueue", "Pushed(state - 144, message)", _ => true, s => { s[Published] = 1; s[at] = 2; }),
            new("Wait", "TimerAdd(reply)", _ => true, s => { s[Listed] = 1; s[at] = 3; }),
            new("Wait", "while ReplyState(reply) == 0 && !Waited(reply)", _ => true, s =>
            {
                if (s[Answer] != 0 || s[Due] != 0)
                {
                    s[at] = 12;
                    return;
                }

                s[Parked] = 1;
                s[at] = 4;
            }),
            new("Ready", "ReplyState(reply) != 0 || Waited(reply)", _ => true, s =>
            {
                s[ReadLate] |= s[Returned];
                s[at + 2] = s[Answer] != 0 || s[Due] != 0 ? 1 : 0;
                s[at] = 5;
            }),
            .. LetGone(at, 5, Then, broken),
            .. Taken(at, 8, 11, broken),
            new("RunOne", "if reply != null && (ReplyState(reply) != 0 || Waited(reply))", _ => true, s =>
            {
                if (s[Crashed] == 0 && (s[Answer] != 0 || s[Due] != 0))
                {
                    s[at] = 3;
                    return;
                }

                s[at + 2] = 0;
                s[at] = 5;
            }),
            new("Wait", "TimerRemove(reply)", _ => true, s => { s[Listed] = 0; s[at] = 13; }),
            new("Wait", "int kind = ReplyState(reply)", _ => true, s =>
            {
                if (s[Answer] != 0)
                {
                    s[Returned] = 1;
                    s[Gone] = 1;
                    s[at] = broken == Break.ReadyAfterReturn ? 4 : 5;
                    return;
                }

                s[at] = broken == Break.TimeoutLeavesAtOnce ? give : 14;
            }, ["*Ptr<byte*>(running + 360) = null"]),
            new("Fall", "ReplyState(waited) == 0", _ => true, s => { s[at] = s[Answer] != 0 ? give : 15; }),
            .. Took(at, 15, give, wait, _ => true, broken),
            new("Fall", "while ReplyState(waited) == 0", s => s[Answer] != 0, s => { s[at] = give; }),
            new("Fall", "Give(at)", _ => true, s =>
            {
                s[Crashed] = 1;
                s[Gone] = 1;
                s[at + 2] = 0;
                s[at] = 5;
            }),
        ]);
    }

    /// <summary>
    /// The callee of a call with a deadline, once its message is queued: the scheduler's test of the message's deadline at its take
    /// (<c>RunOne</c>); before it, the run, the take of the reply (<c>ReplyTaken</c>) and, where it owns it, <c>Replied</c>, as
    /// <see cref="Callee"/> has them; past it, the drop (<c>Dropped</c>): the take of the reply, and where it owns it, <c>Signal</c>: the
    /// waiter read, the clock stored in the reply, then the release store of the word <c>dropped</c>, then the wake of the waiter's actor.
    /// </summary>
    private static Strand TimedCallee(int at, Break broken)
    {
        var takes = Takes(broken);
        var answer = 1 + takes;
        var drop = answer + 5;
        var signal = drop + takes;
        return new("callee", at,
        [
            new("RunOne", "if *Ptr<long>(message + 40) > tick", s => s[Published] != 0, s => { s[at] = s[Due] != 0 ? drop : 1; }, ["Dropped(message)"]),
            .. Took(at, 1, answer, -1, _ => true, broken),
            new("Replied", "byte* waiter = *Ptr<byte*>(reply + 8)", _ => true, s => { s[Touched] |= s[Gone]; s[at + 1] = s[Waiter]; s[at] = answer + 1; }),
            new("Replied", ".Store(1, MemoryOrder.Release)", _ => true, s => { s[Touched] |= s[Gone]; s[Answer] = 1; s[at] = answer + 2; }),
            new("Replied", "Wake(state)", _ => true, s => { s[at] = s[at + 1] != 0 ? answer + 3 : -1; }),
            .. Woken(at, answer + 3, -1),
            .. Took(at, drop, broken == Break.DropSignalsNothing ? -1 : signal, -1, _ => true, broken),
            new("Signal", "byte* waiter = *Ptr<byte*>(reply + 8)", _ => true, s => { s[Touched] |= s[Gone]; s[at + 1] = s[Waiter]; s[at] = signal + 1; }),
            new("Signal", "*Ptr<long>(reply + 40) = PauseClock()", _ => true, s => { s[Touched] |= s[Gone]; s[at] = signal + 2; }),
            new("Signal", ".Store(kind, MemoryOrder.Release)", _ => true, s => { s[Touched] |= s[Gone]; s[Answer] = 4; s[at] = signal + 3; }),
            new("Signal", "Wake(state)", _ => true, s => { s[at] = s[at + 1] != 0 ? signal + 4 : -1; }),
            .. Woken(at, signal + 4, -1),
        ]);
    }

    /// <summary>
    /// The timer of the call's deadline: the clock past it at a turn of the scheduler (<c>Wait</c>), then the timers fired
    /// (<c>Fire</c>): where the reply stands among them, it is taken off them and its waiter read, one move under the timers' owner, and
    /// the waiter's actor woken; the timer owns no reply.
    /// </summary>
    private static Strand Timer(int at, Break broken) => new("timer", at,
    [
        new("Wait", "tick = now", _ => true, s => { s[Due] = 1; s[at] = 1; }),
        new("Fire", "TimerRemove(reply)", _ => true, s =>
        {
            if (s[Listed] == 0)
            {
                s[at] = -1;
                return;
            }

            s[Listed] = 0;
            s[Touched] |= s[Gone];
            s[at] = broken == Break.TimerWakesNothing ? -1 : 2;
        }, ["while timers != null && *Ptr<long>(timers + 16) <= now"]),
        .. Woken(at, 2, -1),
    ]);

    /// <summary>What a state of the reply's models in which no thread can move keeps.</summary>
    private static string? ReplyAtRest(int[] s) =>
        s[Returned] == 0 && s[Crashed] == 0 ? "the waiter waits and nothing wakes it: a lost wake" : null;

    /// <summary>The models of the reply, of two threads and of three.</summary>
    private static Model[] ReplyModels(Break broken) =>
    [
        // a call that waits and its answer
        new("a call and its answer", Start(2, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
            [Waiting(At(0), broken), Callee(At(1), broken)], Always, ReplyAtRest),

        // the waiter's actor crashes while its activation is parked, the callee answering meanwhile
        new("a call, its answer and the crash of its waiter", Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
            [Waiting(At(0), broken), Callee(At(1), broken), Crash(At(2), broken)], Always, ReplyAtRest),

        // a call with a deadline: its callee answers, or drops its message past the deadline, while the timer fires (U36, U37)
        new("a call, its answer or its drop, and its deadline", Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
            [TimedWaiting(At(0), broken), TimedCallee(At(1), broken), Timer(At(2), broken)], Always, ReplyAtRest),

        // a call whose message no callee takes: the timer alone wakes its waiter (U36)
        new("a call no callee takes, and its deadline", Start(2, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
            [TimedWaiting(At(0), broken), Timer(At(1), broken)], Always, ReplyAtRest),

        // the waiter's actor crashes while its activation is parked with a deadline, the timer firing meanwhile (U20, U36)
        new("a deadline and the crash of the waiter's actor", Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
            [TimedWaiting(At(0), broken), Timer(At(1), broken), Crash(At(2), broken)], Always, ReplyAtRest),
    ];

    // The words of the queue's model: the end the senders swap (`in`, at the queue's 0), the next message its holder takes (`out`, at 8),
    // the link of each node, the stub's and each message's, the count of the moves of each node out of the queue, the wakes no take
    // answered; then each strand's place and its registers. A node is named by its number, the stub 0, null -1.
    private const int In = 0, Out = 1, Link = 2, Moves = 6, Wakes = 10, QueueShared = 11, Stub = 0, Null = -1;

    /// <summary>The first state of the queue's model: the stub at both ends, every link null, no move, no wake, each strand at its place 0.</summary>
    private static int[] QueueStart(int strands)
    {
        var state = new int[QueueShared + strands * Own];
        for (var node = Stub; node < Moves - Link; node++)
        {
            state[Link + node] = Null;
        }

        return state;
    }

    /// <summary>The first word of the strand numbered <paramref name="n"/> of the queue's model, its place.</summary>
    private static int QueueAt(int n) => QueueShared + n * Own;

    /// <summary>
    /// A sender of the messages <paramref name="messages"/>, each by <c>Enqueue</c>: its link cleared and the queue's end swapped for it in
    /// one atomic swap, the end it found kept, then the link of that end set to it with release (<c>Pushed</c>), then the wake of the
    /// actor (<c>Wake</c>), abstracted to one count of the takes it asks for.
    /// </summary>
    private static Strand QueueSender(string name, int at, Break broken, params int[] messages)
    {
        var moves = new List<Move>();
        foreach (var message in messages)
        {
            var place = moves.Count;
            var last = place + 3 == messages.Length * 3;
            Move swapped = new("Pushed", "Swap(message, MemoryOrder.AcqRel)", _ => true, s =>
            {
                s[Link + message] = Null;
                s[at + 1] = s[In];
                s[In] = message;
                s[at] = place + 1;
            });
            Move linked = new("Pushed", ".Store(message, MemoryOrder.Release)", _ => true, s =>
            {
                s[Link + s[at + 1]] = message;
                s[at] = broken == Break.WakeBeforeLink ? (last ? -1 : place + 3) : place + 2;
            });
            Move woken = new("Enqueue", "Wake(state)", _ => true, s =>
            {
                s[Wakes]++;
                s[at] = broken == Break.WakeBeforeLink ? place + 2 : (last ? -1 : place + 3);
            });
            moves.AddRange(broken == Break.WakeBeforeLink ? new[] { swapped, woken, linked } : new[] { swapped, linked, woken });
        }

        return new(name, at, [.. moves]);
    }

    /// <summary>
    /// The holder of the actor: at each take a wake asked for (<c>RunOne</c>), the messages of the queue moved out one by one
    /// (<c>Moved</c>, <c>Popped</c>) until <c>Popped</c> gives null: the link of the node at <c>out</c> loaded with acquire, the stub passed
    /// over, the end loaded where the node has no link yet, null where a sender's swap stands before its link, else the stub pushed behind
    /// the last message (<c>Pushed</c>) and its link loaded again; a message taken has <c>out</c> set to its link and its own link cleared.
    /// </summary>
    private static Strand QueueHolder(int at, Break broken) => new("holder", at,
    [
        new("RunOne", "Moved(state)", s => s[Wakes] > 0, s => { s[Wakes]--; s[at] = 1; }),
        new("Popped", "mut byte* next = AtomicPtr<byte>.FromPtr(Ptr<byte*>(first)).Load(MemoryOrder.Acquire)", _ => true, s =>
        {
            var first = s[Out];
            var next = s[Link + first];
            s[at + 1] = first;
            s[at + 2] = next;
            s[at] = first == Stub ? (next == Null ? 0 : 2) : (next == Null ? 3 : 7);
        }, ["if first == stub {"]),
        new("Popped", "*Ptr<byte*>(queue + 8) = next\n            first = next", _ => true, s =>
        {
            s[Out] = s[at + 2];
            s[at + 1] = s[at + 2];
            s[at + 2] = s[Link + s[at + 1]];
            s[at] = s[at + 2] == Null ? 3 : 7;
        }),
        new("Popped", "if first != AtomicPtr<byte>.FromPtr(Ptr<byte*>(queue)).Load(MemoryOrder.Acquire) {", _ => true,
            s => { s[at] = s[at + 1] != s[In] ? 0 : 4; }, ["Pushed(queue, stub)"]),
        new("Pushed", "Swap(message, MemoryOrder.AcqRel)", _ => true, s =>
        {
            s[Link + Stub] = Null;
            s[at + 3] = s[In];
            s[In] = Stub;
            s[at] = 5;
        }),
        new("Pushed", ".Store(message, MemoryOrder.Release)", _ => true, s => { s[Link + s[at + 3]] = Stub; s[at] = 6; }),
        new("Popped", "next = AtomicPtr<byte>.FromPtr(Ptr<byte*>(first)).Load(MemoryOrder.Acquire)\n            if next == null {", _ => true, s =>
        {
            s[at + 2] = s[Link + s[at + 1]];
            s[at] = broken == Break.StubPushedNotRead || s[at + 2] == Null ? 0 : 7;
        }),
        new("Popped", "*Ptr<byte*>(queue + 8) = next\n        *Ptr<byte*>(first) = null", _ => true, s =>
        {
            s[Out] = s[at + 2];
            s[Link + s[at + 1]] = Null;
            s[Moves + s[at + 1]]++;
            s[at] = 1;
        }),
    ]);

    /// <summary>What every state of the queue's model keeps: no node moved as a message twice, the stub never, and a sender's messages in its order.</summary>
    private static string? QueueAlways(int[] s) =>
        s[Moves + Stub] != 0 ? "the stub moved as a message"
        : Enumerable.Range(1, 3).Any(m => s[Moves + m] > 1) ? "a message moved twice"
        : s[Moves + 2] != 0 && s[Moves + 1] == 0 ? "a sender's second message moved before its first"
        : null;

    /// <summary>What a state of the queue's model in which no thread can move keeps: every message pushed was moved.</summary>
    private static string? QueueAtRest(int[] s) =>
        Enumerable.Range(1, 3).Any(m => s[Moves + m] == 0) ? "a message pushed and never moved: a lost message" : null;

    /// <summary>The model of the queue of many senders: two senders, one of two messages, and the holder.</summary>
    private static Model[] QueueModels(Break broken) =>
    [
        // the messages 1 and 2 of one sender and the message 3 of another, put on the queue while its holder moves them out (U5)
        new("a queue of many senders and its holder", QueueStart(3),
            [QueueSender("first", QueueAt(0), broken, 1, 2), QueueSender("second", QueueAt(1), broken, 3), QueueHolder(QueueAt(2), broken)],
            QueueAlways, QueueAtRest),
    ];

    private static Model[] Models(Break broken) => [.. WordModels(broken), .. ReplyModels(broken), .. QueueModels(broken)];

    [Fact]
    public void The_state_word_and_the_reply_keep_every_interleaving_of_their_threads()
    {
        // the six properties in every state of every model, as the runtime is; each model reaches more than a handful of states, so
        // that a model whose threads do not move is not read as one that keeps everything
        foreach (var model in WordModels(Break.None).Concat(ReplyModels(Break.None)))
        {
            var (broken, states) = Explore(model);
            Assert.True(broken is null, $"{model.Name}: {broken?.What} after {string.Join(", ", broken?.Path ?? [])}");
            Assert.True(states >= 40, $"{model.Name}: {states} states");
        }
    }

    [Fact]
    public void The_queue_of_many_senders_moves_every_message_once_and_in_its_sender_s_order_in_every_interleaving()
    {
        // two senders and the holder of the actor, every interleaving of their swaps, links, wakes and moves: each message moved once,
        // none lost while a sender's swap stands before its link, the stub never moved, and one sender's messages in their order (U5)
        foreach (var model in QueueModels(Break.None))
        {
            var (broken, states) = Explore(model);
            Assert.True(broken is null, $"{model.Name}: {broken?.What} after {string.Join(", ", broken?.Path ?? [])}");
            Assert.True(states >= 40, $"{model.Name}: {states} states");
        }
    }

    [Theory]
    [InlineData(Break.WaiterAfterEnqueue, "a call and its answer", "a lost wake")]
    [InlineData(Break.ReadThenWrite, "a call, its answer and the crash of its waiter", "two owners of the reply")]
    [InlineData(Break.NotifiedDropped, "a wake while a thread holds the actor", "a lost wake")]
    [InlineData(Break.RequeueLeftOut, "a wake while a thread holds the actor", "a lost wake")]
    [InlineData(Break.RunningNotHeld, "a wake while a thread holds the actor", "two threads hold the actor")]
    [InlineData(Break.BuildingNotHeld, "a wake while the constructor runs", "a message started while the actor's constructor ran")]
    [InlineData(Break.BuiltQueuesNothing, "a wake while the constructor runs", "a lost wake")]
    [InlineData(Break.WaiterReadAfterStore, "a call and its answer", "once it was gone")]
    [InlineData(Break.CrashLeavesAtOnce, "a call, its answer and the crash of its waiter", "once it was gone")]
    [InlineData(Break.RestartHoldsNothing, "a take while a restart starts", "two threads hold the actor")]
    [InlineData(Break.DropSignalsNothing, "a call, its answer or its drop, and its deadline", "a lost wake")]
    [InlineData(Break.TimeoutLeavesAtOnce, "a call, its answer or its drop, and its deadline", "once it was gone")]
    [InlineData(Break.TimerWakesNothing, "a call no callee takes, and its deadline", "a lost wake")]
    [InlineData(Break.TimerKeptPastFrame, "a deadline and the crash of the waiter's actor", "once it was gone")]
    [InlineData(Break.ReadyAfterReturn, "a call, its answer or its drop, and its deadline", "once its wait had returned")]
    [InlineData(Break.WakeBeforeLink, "a queue of many senders and its holder", "a lost message")]
    [InlineData(Break.StubPushedNotRead, "a queue of many senders and its holder", "a lost message")]
    public void The_explorer_finds_each_break_of_a_model_in_the_model_it_breaks(Break broken, string name, string what)
    {
        // each break found in the model it breaks, with the moves that reach it: the explorer's own red proof
        var models = Models(broken);
        var model = models.Single(m => m.Name == name);
        var (found, _) = Explore(model);
        Assert.True(found is not null, $"{name}: no break found for {broken}");
        Assert.Contains(what, found.What, StringComparison.Ordinal);
        Assert.NotEmpty(found.Path);
    }

    [Fact]
    public void Each_move_of_the_models_names_a_function_of_the_runtime_and_the_text_it_transcribes_in_its_order()
    {
        // the guard against drift: every function a move names is declared in runtime/common/runtime.kz, the text a move transcribes and
        // the texts it decides by stand in that function's body, and the moves of one function in a strand stand in their order there
        var text = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == "runtime/common/runtime.kz").Bytes).ReplaceLineEndings("\n");
        string Body(string function)
        {
            var declared = Regex.Match(text, $@"^(pub )?[\w*<>]+ {function}\([^\n]*\{{\n(.*?)\n\}}\n", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.True(declared.Success, $"runtime/common/runtime.kz declares no function `{function}`, which a move of the models names");
            return declared.Groups[2].Value;
        }

        var strands = Models(Break.None).SelectMany(m => m.Strands).ToList();
        var moves = strands.SelectMany(s => s.Moves).ToList();
        Assert.True(moves.Select(m => m.Function).Distinct().Count() >= 12, string.Join(", ", moves.Select(m => m.Function).Distinct()));
        Assert.True(moves.Where(m => m.Text is not null && m.Text.Contains("MemoryOrder", StringComparison.Ordinal)).Select(m => (m.Function, m.Text)).Distinct().Count() >= 6, "the atomic operations the moves are");
        foreach (var move in moves)
        {
            var body = Body(move.Function);
            Assert.True(move.Text is null || body.Contains(move.Text, StringComparison.Ordinal), $"`{move.Function}` holds no `{move.Text}`, which a move of the models transcribes");
            foreach (var rule in move.Rules ?? [])
            {
                Assert.True(body.Contains(rule, StringComparison.Ordinal), $"`{move.Function}` holds no `{rule}`, which a move of the models decides by");
            }
        }

        Assert.True(moves.SelectMany(m => m.Rules ?? []).Distinct().Count() >= 12, "the texts the moves decide by");
        var ordered = 0;
        foreach (var strand in strands)
        {
            foreach (var function in strand.Moves.Where(m => m.Text is not null).GroupBy(m => m.Function))
            {
                var texts = function.Select(m => m.Text!).Distinct().ToList();
                var body = Body(function.Key);
                for (var k = 1; k < texts.Count; k++)
                {
                    Assert.True(body.IndexOf(texts[k - 1], StringComparison.Ordinal) < body.IndexOf(texts[k], StringComparison.Ordinal),
                        $"`{function.Key}` holds `{texts[k]}` before `{texts[k - 1]}`, against the order of the moves of {strand.Name}");
                    ordered++;
                }
            }
        }

        Assert.True(ordered >= 12, $"{ordered} pairs of moves in order");
    }
}
