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
/// senders an actor's messages are put on, which its holder moves into its own lists (<c>Pushed</c>, <c>Popped</c>), the lock of the words
/// the threads share (<c>Lock</c>, <c>Unlock</c>), the lookup of an actor's slot, which takes no lock (<c>Lookup</c>), and the end of a
/// supervisor, which sets its child's ENDING bit and acts on the child under its hold (<c>EndChildren</c>). Each move of a model
/// is one atomic operation of the runtime, or one plain read or write that no other thread races, but for the run queue and the inbox,
/// which the model takes as queues whose append and take are one move each, while the runtime's are plain lists, which two threads
/// that append or take at once race; each move names the function of <c>runtime/common/runtime.kz</c> it transcribes, with the text of
/// the atomic operation it is there and the texts of the masks of the state word's bits it decides by, but for the crash's hold,
/// <c>Fell</c>, an assumption of the model that names no text (<see cref="Crash"/>); a test holds every name and every such text to
/// that file, each once in its function's code, a guard against a model that says what the runtime no longer does, so that a change
/// of one of those lines, which one thread cannot show, turns it red. The explorer runs every interleaving of two or three threads,
/// each state once, and finds no cycle of moves and no path past its bound, so that every path ends, no lost wake (a message queued or
/// a reply set always leads to the actor or the waiter running), no two owners of a reply, no two threads holding one actor, no
/// message while its constructor runs (U17) or before its restart (U22), and no owner of a reply, and no timer, that reads or writes the waiter's
/// frame once it is gone; the
/// timer that fires at a call's deadline wakes the waiter and owns no reply (U36, U37); no <c>Ready</c> reads the reply of a wait that
/// returned, which <c>Answer</c> takes off its activation at its read of the set word; no message of a queue lost, moved twice or moved
/// before an earlier message of its sender (U5), no two holders of a lock and no thread asleep on a free one, and no lookup that gives
/// another actor's state, and no child of an ended supervisor that does not end, ends twice or has its record written while another thread
/// holds it (U13); and it finds each of these but the bound broken in a model broken so, the cases of <see cref="Break"/>. Its
/// moves are sequentially consistent: the orders of the runtime's atomic operations, acquire and release, are the IR's to show
/// (<c>EmitterTests</c>), not the explorer's.
/// </summary>
public sealed class ModelTests
{
    /// <summary>The breaks the explorer finds, each a change of the moves of a model.</summary>
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

        /// <summary>A wake on the worker threads turns QUEUED into NOTIFIED whether a thread still holds the actor or not.</summary>
        LeftUnheld,

        /// <summary>The end of a hold queues the actor again where it can go on alone, NOTIFIED left out.</summary>
        RequeueLeftOut,

        /// <summary>The end of a hold queues the actor again whether it can go on or not, so that a thread takes it without end.</summary>
        RequeueAlways,

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

        /// <summary>A take starts the message of an actor that waits for its restart, its life left out.</summary>
        RestartRunsEarly,

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

        /// <summary><c>Unlock</c> lets go of the lock and wakes no thread that sleeps on its word.</summary>
        UnlockWakesNothing,

        /// <summary>A contended <c>Lock</c> sleeps while the word holds 1, which the end of a hold wakes no thread for.</summary>
        WaitsOnOne,

        /// <summary><c>Lookup</c> loads the slot's number once, before the state, and not again after it.</summary>
        LookupReadsOnce,

        /// <summary><c>SlotFree</c> stores the link of the free slots before it clears the slot's number.</summary>
        LinkBeforeNumber,

        /// <summary><c>Run</c> lets go of a child whose last activation ended with no read of its ENDING bit.</summary>
        RunIgnoresEnding,

        /// <summary><c>EndChildren</c> ends or clears the child once its bit is set, with no hold of it.</summary>
        EndWithoutHold,
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
    /// or null, and the count of the states seen. A state met again on the path that reached it closes a cycle, in which the strands can
    /// move without end while a message waits, and a path longer than <see cref="Bound"/> moves is a break too, so that a model that
    /// does not end is never read as one that keeps everything.
    /// </summary>
    private static (Broken? Broken, int States) Explore(Model model)
    {
        var seen = new HashSet<string>();
        var open = new HashSet<string>();
        var path = new List<string>();

        Broken? Visit(int[] state)
        {
            var key = string.Join(",", state);
            if (open.Contains(key))
            {
                return new Broken("a cycle of moves back to a state on its own path, a model that does not end", path.ToList());
            }

            if (!seen.Add(key))
            {
                return null;
            }

            var what = model.Always(state);
            if (what is not null)
            {
                return new Broken(what, path.ToList());
            }

            if (path.Count > Bound)
            {
                return new Broken($"a path of more than {Bound} moves", path.ToList());
            }

            open.Add(key);
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

            open.Remove(key);
            var rest = moved ? null : model.AtRest(state);
            return rest is null ? null : new Broken(rest, path.ToList());
        }

        var broken = Visit((int[])model.Start.Clone());
        return (broken, seen.Count);
    }

    // The bits of the actor's state word (`StateWord`).
    private const int Running = 1, Queued = 2, Notified = 4, Building = 8;

    // The words every model's state holds first: the actor's state word, its entries in the run queue, the messages of its inbox, the
    // threads that hold it, whether its constructor ended, whether a message started while its constructor ran (1) or before its
    // restart (2); then the reply's: whether the message names the reply, whether the message is queued, whether the reply names its
    // waiter, the reply's word, the owners of the reply, whether the waiter's frame is gone, whether the waiter parked, returned or
    // crashed, and whether an owner of the reply read or wrote the frame once it was gone; the actor's life, 1 while it waits for its
    // restart; whether the reply stands among the timers, whether the call's deadline passed at the scheduler's turn, and whether the
    // waiter's `Ready` read the reply once its wait had returned, and whether the restart's message has not started, 1 from the first
    // state of the restart's model to the restarter's `Start`. Each strand's words follow: its place, the word it read, what `Ready`
    // gave, a register.
    private const int Word = 0, Entries = 1, Mail = 2, Holders = 3, Built = 4, Early = 5;
    private const int Hook = 6, Published = 7, Waiter = 8, Answer = 9, Owners = 10, Gone = 11, Parked = 12, Returned = 13, Crashed = 14;
    private const int Touched = 15, Life = 16, Listed = 17, Due = 18, ReadLate = 19, Fresh = 20, Shared = 21, Own = 4;

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
    /// <c>Enqueue</c>'s append of a message to the inbox, one move, as a queue of many senders appends, then its plain read of the actor's
    /// life, by which the form of one thread passes the wake over where the actor waits for its restart (the life 1), the message left
    /// in the inbox that the end of the restart's hold reads (<c>Ready</c>), and which decides nothing on threads (<paramref name="workers"/>),
    /// where the sender wakes the actor whatever its life, since a restart's plain write of the life and its read of the queue are a store
    /// and a load that no fence orders, which a sender's read can see in either order; then the wake in the form <paramref name="workers"/>
    /// names (<see cref="Woken"/>), whose entry a take that finds the actor waiting for its restart lets go of (<c>RunTake</c>). The life
    /// 5, an actor that waits for the decision of its supervisor's block of <c>on crash</c>, over which the runtime passes the wake too, is
    /// a life no step of the model sets, so that the step reads the life 1 alone.
    /// </summary>
    private static Strand Sender(string name, int at, bool workers, Break broken) => new(name, at,
    [
        new("Enqueue", "Pushed(state - 144, message)", _ => true, s => { s[Mail]++; s[at] = 1; }),
        new("Enqueue", "int life = *Ptr<int>(state - 88)", _ => true, s => { s[at] = workers || s[Life] != 1 ? 2 : -1; }, ["if Threaded() || (life != 1 && life != 5) {"]),
        .. Woken(at, 2, -1, workers, broken),
    ]);

    /// <summary>
    /// The wake of the actor from the place <paramref name="from"/>, the strand going on at <paramref name="then"/>: one atomic <c>or</c>
    /// of QUEUED, and where the mark was clear the entry put in the run queue (<c>Wake</c>, <c>Queue</c>), the form of one thread, which
    /// puts an entry of an actor that a thread holds too; on the worker threads (<paramref name="workers"/>) where the mark was clear on
    /// an actor that a thread holds or whose constructor runs, the mark turned into NOTIFIED by a compare-exchange made again on what it
    /// found while the actor is held, with no entry, and the entry put where the holder let go of it meanwhile (<c>LeftToHolder</c>,
    /// <c>QueueLocal</c>). The strand's second word holds what the compare-exchange saw. The runtime's test reads RECLAIMED (64) beside
    /// QUEUED, which no move of the models sets (U35, T16).
    /// </summary>
    private static Move[] Woken(int at, int from, int then, bool workers, Break broken) => workers
        ?
        [
            new("Wake", ".FetchOr(2, MemoryOrder.AcqRel)", _ => true, s =>
            {
                var before = s[Word];
                s[Word] |= Queued;
                s[at] = (before & Queued) != 0 ? then : (before & (Running | Building)) == 0 ? from + 1 : from + 2;
            }, ["if (before & 66) == 0 {", "} else if (before & 9) == 0 || !LeftToHolder(state) {"]),
            new("QueueLocal", "InjectPush(state)", _ => true, s => { s[Entries]++; s[at] = then; }),
            new("LeftToHolder", "word.Load(MemoryOrder.Acquire)", _ => true, s => { s[at + 1] = s[Word]; s[at] = from + 3; }),
            new("LeftToHolder", "Exchanged(word, seen, (seen & ~2) | 4)", _ => true, s =>
            {
                var seen = s[at + 1];
                if (broken != Break.LeftUnheld && (seen & (Running | Building)) == 0)
                {
                    s[at] = from + 1;
                    return;
                }

                if (s[Word] != seen)
                {
                    s[at + 1] = s[Word];
                    return;
                }

                s[Word] = (seen & ~Queued) | Notified;
                s[at] = then;
            }, ["while (seen & 9) != 0 {"]),
        ]
        :
        [
            new("Wake", ".FetchOr(2, MemoryOrder.AcqRel)", _ => true, s =>
            {
                var before = s[Word];
                s[Word] |= Queued;
                s[at] = (before & Queued) == 0 ? from + 1 : then;
            }, ["if (before & 66) == 0 {"]),
            new("Queue", "runLast = state", _ => true, s => { s[Entries]++; s[at] = then; }),
        ];

    /// <summary>The count of the moves of a wake in the form <paramref name="workers"/> names (<see cref="Woken"/>).</summary>
    private static int WokenMoves(bool workers) => Woken(0, 0, -1, workers, Break.None).Length;

    /// <summary>
    /// The take of an entry off the run queue at the place <paramref name="from"/>, by the one thread (<c>RunOne</c>) or, where
    /// <paramref name="workers"/>, by a worker thread's turn, which gives what it took to <c>RunTake</c> (<c>WorkerTurn</c>), and the hold of
    /// the actor (<c>Claimed</c>: a load, then a compare-exchange, made again on what it found): RUNNING where neither RUNNING nor BUILDING
    /// is set, the strand going on at <paramref name="held"/>, else NOTIFIED, the strand back at <paramref name="from"/>; QUEUED cleared
    /// either way.
    /// </summary>
    private static Move[] Taken(int at, int from, int held, Break broken, bool workers = false)
    {
        var holds = broken switch { Break.RunningNotHeld => Building, Break.BuildingNotHeld => Running, _ => Running | Building };
        return
        [
            workers
                ? new Move("WorkerTurn", "byte* state = NextActor(record, shared)", s => s[Entries] > 0, s => { s[Entries]--; s[at] = from + 1; })
                : new Move("RunOne", "runFirst = *Ptr<byte*>(state - 16)", s => s[Entries] > 0, s => { s[Entries]--; s[at] = from + 1; }),
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
            var queue = (seen & Queued) == 0 && (notified || s[at + 2] != 0 || broken == Break.RequeueAlways);
            s[Word] = (seen & ~(Running | Notified)) | (queue ? Queued : 0);
            s[Holders]--;
            s[at] = queue ? from + 2 : then(s);
        }, ["queue = (seen & 2) == 0 && ((seen & 4) != 0 || ready)", "mut int wanted = seen & ~5", "wanted = wanted | 2"]),
        new("Queue", "runLast = state", _ => true, s => { s[Entries]++; s[at] = then(s); }),
    ];

    /// <summary>
    /// A thread of the scheduler: the take of an entry and the hold (<c>RunOne</c>, <c>Claimed</c>), the first message of the inbox
    /// started where it holds the actor and the actor does not wait for its restart (<c>RunOne</c>), <c>Ready</c> read after the switch
    /// back, and the end of the hold (<c>LetGo</c>); then another entry. Where <paramref name="workers"/>, the take and the start are a
    /// worker thread's (<c>WorkerTurn</c>, <c>RunTake</c>), which the models on worker threads run, so that a change of those lines turns
    /// the guard red there. One that starts at the place 4 holds the actor already, its message ran.
    /// </summary>
    private static Strand Runner(string name, int at, bool workers, Break broken) => new(name, at,
    [
        .. Taken(at, 0, 3, broken, workers),
        new(workers ? "RunTake" : "RunOne", workers ? "Start(state, message, null, Adopted(state, message))" : "Start(state, message, null, null)", _ => true, s =>
        {
            if (s[Mail] > 0 && (broken == Break.RestartRunsEarly || s[Life] == 0))
            {
                s[Mail]--;
                s[Early] |= (s[Built] == 0 ? 1 : 0) | (s[Fresh] != 0 ? 2 : 0);
                s[at] = 4;
                return;
            }

            s[at + 2] = 0;
            s[at] = 5;
        }, ["if life == 1 || life == 5 {"]),
        new("Ready", "byte* first = *Ptr<byte*>(state - 32)", _ => true, s => { s[at + 2] = s[Mail] > 0 ? 1 : 0; s[at] = 5; }),
        .. LetGone(at, 5, _ => 0, broken),
    ]);

    /// <summary>
    /// The restart of the actor, whose pause ended while it waits for its restart (<c>Restart</c>): the hold outside the run queue
    /// (<c>HeldAlone</c>: a load, then a compare-exchange that sets RUNNING from a word in which no thread holds it, made again on what it
    /// found, and waiting while a take holds it, the load in that wait the move of the compare-exchange that reads the word anew), the
    /// life set to 0, the restart's message started, which clears the word that a message started before it reads, <c>Ready</c> read
    /// after the switch back, and the end of the hold (<c>LetGo</c>).
    /// </summary>
    private static Strand Restarter(int at, Break broken) => new("restarter", at,
    [
        new("Restart", "HeldAlone(state)", _ => true, s => { s[at] = 1; }),
        new("HeldAlone", "seen = word.Load(MemoryOrder.Acquire) } mut bool stored", _ => true, s => { s[at + 1] = s[Word]; s[at] = 2; }),
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
        new("Restart", "Start(state, message, null, null)", _ => true, s => { s[Fresh] = 0; s[at] = 5; }),
        new("Ready", "byte* first = *Ptr<byte*>(state - 32)", _ => true, s => { s[at + 2] = s[Mail] > 0 ? 1 : 0; s[at] = 6; }),
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
        : (s[Early] & 1) != 0 ? "a message started while the actor's constructor ran"
        : (s[Early] & 2) != 0 ? "a message started before the actor's restart"
        : s[Owners] > 1 ? "two owners of the reply"
        : s[Touched] != 0 ? "an owner of the reply read or wrote the waiter's frame once it was gone"
        : s[ReadLate] != 0 ? "Ready read the reply once its wait had returned, which Answer took off the activation"
        : null;

    /// <summary>What a state of the state word's models in which no thread can move keeps.</summary>
    private static string? WordAtRest(int[] s) =>
        s[Mail] > 0 ? $"{s[Mail]} message(s) wait in the inbox and nothing runs the actor: a lost wake"
        : s[Word] != 0 ? $"the state word is {s[Word]} with no thread moving, not idle"
        : null;

    /// <summary>
    /// The models of the state word, each of three threads, with the wake, the sender's read of the life and the runner's take in the form
    /// <paramref name="workers"/> names, whose models are named with " on worker threads" (<see cref="Woken"/>, <see cref="Sender"/>,
    /// <see cref="Runner"/>).
    /// </summary>
    private static Model[] WordModels(Break broken, bool workers)
    {
        var on = workers ? " on worker threads" : string.Empty;
        return
        [
            // a message sent while the actor's constructor runs: it starts once `ActorBuilt` ran (U17)
            new("a wake while the constructor runs" + on, Start(3, (Word, Building)),
                [Builder(At(0), broken), Sender("sender", At(1), workers, broken), Runner("runner", At(2), workers, broken)], Always, WordAtRest),

            // a message sent while a thread holds the actor: a second thread takes its entry, which the holder queues again; on worker
            // threads the holder queues it again with no entry
            new("a wake while a thread holds the actor" + on, Start(3, (Word, Running), (Holders, 1), (Built, 1), (At(0), 4)),
                [Runner("holder", At(0), workers, broken), Runner("runner", At(1), workers, broken), Sender("sender", At(2), workers, broken)], Always, WordAtRest),

            // two senders, one thread
            new("two wakes" + on, Start(3, (Built, 1)),
                [Sender("first", At(0), workers, broken), Sender("second", At(1), workers, broken), Runner("runner", At(2), workers, broken)], Always, WordAtRest),

            // a restart while an entry of the actor, queued before its crash, stands in the run queue with a message in its inbox, and a
            // message sent meanwhile: a take passes the actor over while it waits for its restart, and the restart waits for that take
            new("a take while a restart starts" + on, Start(3, (Built, 1), (Life, 1), (Fresh, 1), (Word, Queued), (Entries, 1), (Mail, 1)),
                [Restarter(At(0), broken), Runner("runner", At(1), workers, broken), Sender("sender", At(2), workers, broken)], Always, WordAtRest),
        ];
    }

    /// <summary>
    /// The waiter of a call, whose thread holds its actor at the start: the reply's waiter stored (<c>ReplyWaiter</c>) and the message
    /// queued (<c>Enqueue</c>), the reply's word read (<c>Answer</c>), and where it is 0 the activation parked: <c>Ready</c> and the end of
    /// the hold; then the takes of the actor's entries, each resuming the activation where the word is set (<c>RunTake</c>), as long as it
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
            new("Answer", "while ReplyState(reply) == 0 && !Waited(reply) { long entries = sys.Inside(0)", _ => true, s =>
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
            new("RunTake", "if reply != null && (ReplyState(reply) != 0 || Waited(reply))", _ => true, s =>
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
    /// waiter read, then the release store of the reply's word, then the wake of the waiter's actor (<c>Wake</c>), in the form
    /// <paramref name="workers"/> names.
    /// </summary>
    private static Strand Callee(int at, Break broken, bool workers)
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
            .. Woken(at, first + 3, -1, workers, broken),
        ]);
    }

    /// <summary>
    /// The crash of the waiter's actor while its activation is parked: the hold of the actor by the crash, from a state word no thread
    /// holds (<c>Fell</c>, which names no text, an assumption of the model: the runtime holds the actor so by <c>Claimed</c>, the take of
    /// an upper activation's entry, where that activation crashes, and holds it by no take where the actor is a supervisor that falls on
    /// the way up from a child's crash, which on the build's one thread no other thread can take meanwhile); the reply taken off the timers (<c>Fall</c>); where the reply's word is
    /// 0 (<c>Fall</c>), the take of the reply
    /// (<c>ReplyTaken</c>), and where the callee took it first, the wait for its word
    /// (<c>Fall</c>); then the stack given back, the
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
            new("Fall", "waited != null && ReplyState(waited) == 0", _ => true, s => { s[at] = s[Answer] != 0 ? wait + 1 : 3; }),
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
    /// deadline (<c>Answer</c>), and where neither holds the activation parked: <c>Ready</c> and the end of the hold, then the takes of the
    /// actor's entries, each resuming the activation where the word is set or the deadline passed (<c>RunTake</c>), which tests again.
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
            new("Answer", "TimerAdd(reply)", _ => true, s => { s[Listed] = 1; s[at] = 3; }),
            new("Answer", "while ReplyState(reply) == 0 && !Waited(reply)", _ => true, s =>
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
            new("RunTake", "if reply != null && (ReplyState(reply) != 0 || Waited(reply))", _ => true, s =>
            {
                if (s[Crashed] == 0 && (s[Answer] != 0 || s[Due] != 0))
                {
                    s[at] = 3;
                    return;
                }

                s[at + 2] = 0;
                s[at] = 5;
            }),
            new("Answer", "TimerRemove(reply)", _ => true, s => { s[Listed] = 0; s[at] = 13; }),
            new("Answer", "int kind = ReplyState(reply)", _ => true, s =>
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
            new("Fall", "waited != null && ReplyState(waited) == 0", _ => true, s => { s[at] = s[Answer] != 0 ? give : 15; }),
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
    /// (<c>RunTake</c>); before it, the run, the take of the reply (<c>ReplyTaken</c>) and, where it owns it, <c>Replied</c>, as
    /// <see cref="Callee"/> has them; past it, the drop (<c>Dropped</c>): the take of the reply, and where it owns it, <c>Signal</c>: the
    /// waiter read, the clock stored in the reply, then the release store of the word <c>dropped</c>, then the wake of the waiter's actor,
    /// each wake in the form <paramref name="workers"/> names.
    /// </summary>
    private static Strand TimedCallee(int at, Break broken, bool workers)
    {
        var takes = Takes(broken);
        var answer = 1 + takes;
        var drop = answer + 3 + WokenMoves(workers);
        var signal = drop + takes;
        return new("callee", at,
        [
            new("RunTake", "byte* message = Taken(state - 32) if *Ptr<long>(message + 40) > tick", s => s[Published] != 0, s => { s[at] = s[Due] != 0 ? drop : 1; }, ["Dropped(message) } } else {"]),
            .. Took(at, 1, answer, -1, _ => true, broken),
            new("Replied", "byte* waiter = *Ptr<byte*>(reply + 8)", _ => true, s => { s[Touched] |= s[Gone]; s[at + 1] = s[Waiter]; s[at] = answer + 1; }),
            new("Replied", ".Store(1, MemoryOrder.Release)", _ => true, s => { s[Touched] |= s[Gone]; s[Answer] = 1; s[at] = answer + 2; }),
            new("Replied", "Wake(state)", _ => true, s => { s[at] = s[at + 1] != 0 ? answer + 3 : -1; }),
            .. Woken(at, answer + 3, -1, workers, broken),
            .. Took(at, drop, broken == Break.DropSignalsNothing ? -1 : signal, -1, _ => true, broken),
            new("Signal", "byte* waiter = *Ptr<byte*>(reply + 8)", _ => true, s => { s[Touched] |= s[Gone]; s[at + 1] = s[Waiter]; s[at] = signal + 1; }),
            new("Signal", "*Ptr<long>(reply + 40) = PauseClock()", _ => true, s => { s[Touched] |= s[Gone]; s[at] = signal + 2; }),
            new("Signal", ".Store(kind, MemoryOrder.Release)", _ => true, s => { s[Touched] |= s[Gone]; s[Answer] = 4; s[at] = signal + 3; }),
            new("Signal", "Wake(state)", _ => true, s => { s[at] = s[at + 1] != 0 ? signal + 4 : -1; }),
            .. Woken(at, signal + 4, -1, workers, broken),
        ]);
    }

    /// <summary>
    /// The timer of the call's deadline: the clock past it at a turn of the scheduler (<c>RootLoop</c>, the root loop of <c>Answer</c>),
    /// then the timers fired (<c>Fire</c>): where the reply stands among them, it is taken off them and its waiter read, one move under
    /// the timers' owner, and the waiter's actor woken, in the form <paramref name="workers"/> names; the timer owns no reply.
    /// </summary>
    private static Strand Timer(int at, Break broken, bool workers) => new("timer", at,
    [
        new("RootLoop", "tick = now", _ => true, s => { s[Due] = 1; s[at] = 1; }),
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
        .. Woken(at, 2, -1, workers, broken),
    ]);

    /// <summary>What a state of the reply's models in which no thread can move keeps.</summary>
    private static string? ReplyAtRest(int[] s) =>
        s[Returned] == 0 && s[Crashed] == 0 ? "the waiter waits and nothing wakes it: a lost wake" : null;

    /// <summary>
    /// The models of the reply, of two threads and of three, with the wakes in the form <paramref name="workers"/> names, whose models
    /// are named with " on worker threads" (<see cref="Woken"/>).
    /// </summary>
    private static Model[] ReplyModels(Break broken, bool workers)
    {
        var on = workers ? " on worker threads" : string.Empty;
        return
        [
            // a call that waits and its answer
            new("a call and its answer" + on, Start(2, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
                [Waiting(At(0), broken), Callee(At(1), broken, workers)], Always, ReplyAtRest),

            // the waiter's actor crashes while its activation is parked, the callee answering meanwhile
            new("a call, its answer and the crash of its waiter" + on, Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
                [Waiting(At(0), broken), Callee(At(1), broken, workers), Crash(At(2), broken)], Always, ReplyAtRest),

            // a call with a deadline: its callee answers, or drops its message past the deadline, while the timer fires (U36, U37)
            new("a call, its answer or its drop, and its deadline" + on, Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
                [TimedWaiting(At(0), broken), TimedCallee(At(1), broken, workers), Timer(At(2), broken, workers)], Always, ReplyAtRest),

            // a call whose message no callee takes: the timer alone wakes its waiter (U36)
            new("a call no callee takes, and its deadline" + on, Start(2, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
                [TimedWaiting(At(0), broken), Timer(At(1), broken, workers)], Always, ReplyAtRest),

            // the waiter's actor crashes while its activation is parked with a deadline, the timer firing meanwhile (U20, U36)
            new("a deadline and the crash of the waiter's actor" + on, Start(3, (Word, Running), (Holders, 1), (Built, 1), (Hook, 1)),
                [TimedWaiting(At(0), broken), Timer(At(1), broken, workers), Crash(At(2), broken)], Always, ReplyAtRest),
        ];
    }

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
    /// The holder of the actor: at each take a wake asked for (<c>RunTake</c>), the messages of the queue moved out one by one
    /// (<c>Moved</c>, <c>Popped</c>) until <c>Popped</c> gives null: the link of the node at <c>out</c> loaded with acquire, the stub passed
    /// over, the end loaded where the node has no link yet, null where a sender's swap stands before its link, else the stub pushed behind
    /// the last message (<c>Pushed</c>) and its link loaded again; a message taken has <c>out</c> set to its link and its own link cleared.
    /// </summary>
    private static Strand QueueHolder(int at, Break broken) => new("holder", at,
    [
        new("RunTake", "Moved(state) if life == 1 || life == 5", s => s[Wakes] > 0, s => { s[Wakes]--; s[at] = 1; }),
        new("Popped", "mut byte* next = AtomicPtr<byte>.FromPtr(Ptr<byte*>(first)).Load(MemoryOrder.Acquire)", _ => true, s =>
        {
            var first = s[Out];
            var next = s[Link + first];
            s[at + 1] = first;
            s[at + 2] = next;
            s[at] = first == Stub ? (next == Null ? 0 : 2) : (next == Null ? 3 : 7);
        }, ["if first == stub {"]),
        new("Popped", "*Ptr<byte*>(queue + 8) = next first = next", _ => true, s =>
        {
            s[Out] = s[at + 2];
            // the load of the new first node's link is a move of its own, the first's, which reads `first` from `out` (`var first =
            // s[Out]`), the one consumer's own word, so that this store of `out` is the text's `first = next` too
            s[at] = 1;
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
        new("Popped", "next = AtomicPtr<byte>.FromPtr(Ptr<byte*>(first)).Load(MemoryOrder.Acquire) if next == null {", _ => true, s =>
        {
            s[at + 2] = s[Link + s[at + 1]];
            s[at] = broken == Break.StubPushedNotRead || s[at + 2] == Null ? 0 : 7;
        }),
        new("Popped", "*Ptr<byte*>(queue + 8) = next *Ptr<byte*>(first) = null", _ => true, s =>
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

    // The words of the lock's model: the lock's word, the threads that hold the lock, the threads asleep on its word, the wakes those
    // are given and not yet taken; then each strand's place and the word its last load, compare-exchange or swap found.
    private const int LockWord = 0, LockHolders = 1, Sleeping = 2, Given = 3, LockShared = 4;

    /// <summary>The first word of the strand numbered <paramref name="n"/> of the lock's model, its place.</summary>
    private static int LockAt(int n) => LockShared + n * Own;

    /// <summary>
    /// A thread that takes the lock once and lets go of it (<c>Lock</c>, <c>Unlock</c>), as Rust's futex mutex (not checked at source): the compare-exchange from
    /// 0 to 1, the spin, abstracted to one load, a second compare-exchange where the spin found 0, then the swap of 2 where the word
    /// found is not 2, the wait on the word while it holds 2 (<c>sys.LockWait</c>, asleep until a wake), the spin and the swap again;
    /// the end of the hold swaps 0 in and wakes one sleeper where it found 2 (<c>sys.LockWake</c>, lost where none sleeps).
    /// </summary>
    private static Strand Locker(string name, int at, Break broken)
    {
        void Held(int[] s)
        {
            s[LockHolders]++;
            s[at] = 7;
        }

        Move Tried(int then) => new("LockTried", "lock.CompareExchange(0, 1, MemoryOrder.Acquire, MemoryOrder.Relaxed)", _ => true, s =>
        {
            if (s[LockWord] == 0)
            {
                s[LockWord] = 1;
                Held(s);
                return;
            }

            s[at + 1] = s[LockWord];
            s[at] = then;
        });
        return new(name, at,
        [
            Tried(1),
            new("LockSpun", "mut int state = lock.Load(MemoryOrder.Relaxed)", _ => true, s =>
            {
                s[at + 1] = s[LockWord];
                s[at] = s[at + 1] == 0 ? 2 : 3;
            }, ["while state == 1 && spins > 0 {"]),
            Tried(3),
            new("Lock", "lock.Swap(2, MemoryOrder.Acquire)", _ => true, s =>
            {
                if (s[at + 1] == 2)
                {
                    s[at] = 4;
                    return;
                }

                var before = s[LockWord];
                s[LockWord] = 2;
                if (before == 0)
                {
                    Held(s);
                    return;
                }

                s[at] = 4;
            }, ["while state == 2 || lock.Swap(2, MemoryOrder.Acquire) != 0 {"]),
            new("Lock", "sys.LockWait(word, 2)", _ => true, s =>
            {
                if (s[LockWord] == (broken == Break.WaitsOnOne ? 1 : 2))
                {
                    s[Sleeping]++;
                    s[at] = 5;
                    return;
                }

                s[at] = 6;
            }),
            new("Lock", "sys.LockWait(word, 2)", s => s[Given] > 0, s => { s[Given]--; s[at] = 6; }),
            new("LockSpun", "mut int state = lock.Load(MemoryOrder.Relaxed)", _ => true, s => { s[at + 1] = s[LockWord]; s[at] = 3; }),
            new("Unlock", "Swap(0, MemoryOrder.Release) == 2 {", _ => true, s =>
            {
                var before = s[LockWord];
                s[LockWord] = 0;
                s[LockHolders]--;
                s[at] = before == 2 && broken != Break.UnlockWakesNothing ? 8 : -1;
            }),
            new("Unlock", "sys.LockWake(word)", _ => true, s =>
            {
                if (s[Sleeping] > 0)
                {
                    s[Sleeping]--;
                    s[Given]++;
                }

                s[at] = -1;
            }),
        ]);
    }

    /// <summary>What every state of the lock's model keeps: one holder at most.</summary>
    private static string? LockAlways(int[] s) => s[LockHolders] > 1 ? "two threads hold the lock" : null;

    /// <summary>What a state of the lock's model in which no thread can move keeps: every thread took the lock and let go of it.</summary>
    private static string? LockAtRest(int[] s) =>
        Enumerable.Range(0, 3).Any(n => s[LockAt(n)] != -1) ? "a thread sleeps on the lock and nothing wakes it: a lost wake"
        : s[LockWord] != 0 ? $"the lock's word is {s[LockWord]} with no thread holding it"
        : null;

    // The words of the lookup's model: the slot's word, the state of the actor 1 or 2 or the link of the free slots, -1, and its number,
    // 0 where the slot is free; whether a lookup gave a state that is not its actor's; then each strand's place and the state it loaded.
    private const int SlotWord = 0, SlotNumber = 1, Wrong = 2, SlotShared = 3;

    /// <summary>The first word of the strand numbered <paramref name="n"/> of the lookup's model, its place.</summary>
    private static int SlotAt(int n) => SlotShared + n * Own;

    /// <summary>
    /// The end of the actor 1 (<c>SlotFree</c>: the number cleared with release, then the free slots' link stored with release) and the
    /// spawn of the actor 2 into the same slot (<c>SlotTake</c>: its state, then its number, each with release), under the lock of the
    /// table, which orders them.
    /// </summary>
    private static Strand Recycler(int at, Break broken)
    {
        Move cleared = new("SlotFree", "AtomicLong.FromPtr(Ptr<long>(slot + 8)).Store(0, MemoryOrder.Release)", _ => true, s =>
        {
            s[SlotNumber] = 0;
            s[at] = broken == Break.LinkBeforeNumber ? 2 : 1;
        });
        Move linked = new("SlotFree", "AtomicLong.FromPtr(Ptr<long>(slot)).Store(slotFree, MemoryOrder.Release)", _ => true, s =>
        {
            s[SlotWord] = -1;
            s[at] = broken == Break.LinkBeforeNumber ? 1 : 2;
        });
        return new("recycler", at,
        [
            broken == Break.LinkBeforeNumber ? linked : cleared,
            broken == Break.LinkBeforeNumber ? cleared : linked,
            new("SlotTake", "AtomicPtr<byte>.FromPtr(Ptr<byte*>(slot)).Store(state, MemoryOrder.Release)", _ => true, s => { s[SlotWord] = 2; s[at] = 3; }),
            new("SlotTake", "AtomicLong.FromPtr(Ptr<long>(slot + 8)).Store(number, MemoryOrder.Release)", _ => true, s => { s[SlotNumber] = 2; s[at] = -1; }),
        ]);
    }

    /// <summary>
    /// A lookup of the actor whose number is <paramref name="number"/> (<c>Lookup</c>, no lock): the number loaded with acquire, null where
    /// it is another, the state loaded with acquire, the number loaded again, null where it changed, else the state.
    /// </summary>
    private static Strand Looker(string name, int at, int number, Break broken)
    {
        void Gave(int[] s, int state)
        {
            s[Wrong] |= state != number ? 1 : 0;
            s[at] = -1;
        }

        return new(name, at,
        [
            new("Lookup", "if held.Load(MemoryOrder.Acquire) != number { return null } byte* state", _ => true,
                s => { s[at] = s[SlotNumber] == number ? 1 : -1; }),
            new("Lookup", "byte* state = AtomicPtr<byte>.FromPtr(Ptr<byte*>(slot)).Load(MemoryOrder.Acquire)", _ => true, s =>
            {
                s[at + 1] = s[SlotWord];
                if (broken == Break.LookupReadsOnce)
                {
                    Gave(s, s[at + 1]);
                    return;
                }

                s[at] = 2;
            }),
            new("Lookup", "if held.Load(MemoryOrder.Acquire) != number { return null } return state", _ => true, s =>
            {
                if (s[SlotNumber] != number)
                {
                    s[at] = -1;
                    return;
                }

                Gave(s, s[at + 1]);
            }),
        ]);
    }

    /// <summary>The models of the lock and of the lookup of a slot.</summary>
    private static Model[] SharedModels(Break broken) =>
    [
        // three threads that each take the lock and let go of it
        new("three threads on one lock", new int[LockShared + 3 * Own],
            [Locker("first", LockAt(0), broken), Locker("second", LockAt(1), broken), Locker("third", LockAt(2), broken)], LockAlways, LockAtRest),

        // the actor 1 in its slot ends, the actor 2 takes the slot, while a lookup of each runs
        new("a lookup while its slot is freed and taken again", Slots(),
            [Recycler(SlotAt(0), broken), Looker("lookup of 1", SlotAt(1), 1, broken), Looker("lookup of 2", SlotAt(2), 2, broken)],
            s => s[Wrong] != 0 ? "a lookup gave a state that is not its actor's" : null, _ => null),
    ];

    /// <summary>The first state of the lookup's model: the actor 1 in the slot, each strand at its place 0.</summary>
    private static int[] Slots()
    {
        var state = new int[SlotShared + 3 * Own];
        state[SlotWord] = 1;
        state[SlotNumber] = 1;
        return state;
    }

    // The words of the end's model besides the state word, its entries and its holders: whether the child's activation waits in its
    // message, whether the child ended, how often, and whether the end of the supervisor wrote the child's record while another thread
    // held it.
    private const int ChildWaits = 4, ChildEnds = 5, Trespass = 6;

    /// <summary>
    /// The end of the child's supervisor (<c>EndChildren</c>): the hold of the child outside the run queue (<c>HeldAlone</c>, waiting while
    /// a take holds it), then, where the child did not end meanwhile, its ENDING bit set by one atomic <c>or</c> and the child ended where
    /// it has no activation, else its policy and supervisor cleared, and the end of the hold (<c>LetGo</c>).
    /// </summary>
    private static Strand Ender(int at, Break broken)
    {
        void Trespassed(int[] s) => s[Trespass] |= s[Holders] - s[at + 3] > 0 ? 1 : 0;
        return new("ender", at,
        [
            new("HeldAlone", "seen = word.Load(MemoryOrder.Acquire) } mut bool stored", _ => true,
                s => { s[at + 1] = s[Word]; s[at] = broken == Break.EndWithoutHold ? 2 : 1; }),
            new("HeldAlone", "Exchanged(word, seen, seen | 1)", s => (s[at + 1] & Running) == 0 || s[Word] != s[at + 1], s =>
            {
                var seen = s[at + 1];
                if (s[Word] != seen)
                {
                    s[at + 1] = s[Word];
                    return;
                }

                s[Word] = seen | Running;
                s[Holders]++;
                s[at + 3] = 1;
                s[at] = 2;
            }, ["if (seen & 1) == 0 {"]),
            new("EndChildren", "StateWord(at).FetchOr(16, MemoryOrder.AcqRel)", _ => true, s =>
            {
                if (s[ChildEnds] != 0)
                {
                    s[at] = s[at + 3] != 0 ? 4 : -1;
                    return;
                }

                s[Word] |= 16;
                s[at] = 3;
            }, ["HeldAlone(at) } if *Ptr<byte*>(at - 72) == state && *Ptr<int>(at - 88) != 3 {"]),
            new("EndChildren", "if *Ptr<byte*>(at - 8) == null {", _ => true, s =>
            {
                Trespassed(s);
                s[ChildEnds] += s[ChildWaits] == 0 ? 1 : 0;
                s[at + 2] = 0;
                s[at] = s[at + 3] != 0 ? 4 : -1;
            }),
            .. LetGone(at, 4, s => { s[at + 3] = 0; return -1; }, broken),
        ]);
    }

    /// <summary>
    /// A thread of the scheduler that takes the child's entry (<c>RunOne</c>, <c>Claimed</c>), resumes its activation, whose message then
    /// ends: the child ended where its ENDING bit is set (<c>Run</c>), and the end of the hold (<c>LetGo</c>).
    /// </summary>
    private static Strand Resumer(int at, Break broken) => new("child", at,
    [
        .. Taken(at, 0, 3, broken),
        new("Run", "if below == null && Ending(state) {", _ => true, s =>
        {
            s[ChildWaits] = 0;
            if ((s[Word] & 16) != 0 && broken != Break.RunIgnoresEnding)
            {
                s[ChildEnds]++;
            }

            s[at + 2] = 0;
            s[at] = 4;
        }),
        .. LetGone(at, 4, _ => -1, broken),
    ]);

    /// <summary>The model of the end of a supervisor while its child's activation waits in a message whose answer queued the child.</summary>
    private static Model[] EndModels(Break broken) =>
    [
        new("the end of a supervisor while its child waits in a message", Start(2, (Word, Queued), (Entries, 1), (ChildWaits, 1)),
            [Ender(At(0), broken), Resumer(At(1), broken)],
            s => s[Holders] > 1 ? "two threads hold the actor"
                : s[ChildEnds] > 1 ? "the child ended twice"
                : s[Trespass] != 0 ? "the end of the supervisor wrote the child's record while another thread held it"
                : null,
            s => s[ChildEnds] == 0 ? "a child whose supervisor ended did not end" : null),
    ];

    private static Model[] Models(Break broken) =>
    [
        .. WordModels(broken, false), .. WordModels(broken, true), .. ReplyModels(broken, false), .. ReplyModels(broken, true),
        .. QueueModels(broken), .. SharedModels(broken), .. EndModels(broken),
    ];

    [Fact]
    public void The_state_word_and_the_reply_keep_every_interleaving_of_their_threads()
    {
        // the six properties in every state of every model, as the runtime is, with the wake in its form of one thread and in its form on
        // the worker threads; each model reaches more than a handful of states, so that a model whose threads do not move is not read as
        // one that keeps everything
        foreach (var model in WordModels(Break.None, false).Concat(WordModels(Break.None, true)).Concat(ReplyModels(Break.None, false)).Concat(ReplyModels(Break.None, true)))
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

    [Fact]
    public void The_lock_and_the_lookup_of_a_slot_keep_every_interleaving_of_their_threads()
    {
        // three threads on one futex lock: one holder at a time and no thread left asleep on a free lock; a lookup with no lock while
        // its slot is freed and taken by another actor: no lookup gives the other actor's state or the link of the free slots (section 6)
        foreach (var model in SharedModels(Break.None))
        {
            var (broken, states) = Explore(model);
            Assert.True(broken is null, $"{model.Name}: {broken?.What} after {string.Join(", ", broken?.Path ?? [])}");
            Assert.True(states >= 40, $"{model.Name}: {states} states");
        }
    }

    [Fact]
    public void A_child_whose_supervisor_ended_ends_once_after_its_current_message_in_every_interleaving()
    {
        // U13: the end of the supervisor sets the child's ENDING bit and acts on the child under its hold, while another thread resumes the
        // child's activation; the child ends once, by the end of its supervisor or where its last activation ends, and nothing writes its
        // record while another thread holds it
        foreach (var model in EndModels(Break.None))
        {
            var (broken, states) = Explore(model);
            Assert.True(broken is null, $"{model.Name}: {broken?.What} after {string.Join(", ", broken?.Path ?? [])}");
            Assert.True(states >= 40, $"{model.Name}: {states} states");
        }
    }

    [Fact]
    public void Each_act_of_one_actor_on_another_is_made_under_the_other_s_hold()
    {
        // section 6, the default for the acts of one actor on another: the end of a supervisor ends its child or clears the child's policy
        // and supervisor, and a block's decision restarts or ends the child, each between the hold of the child and its end
        Held(
        [
            ("EndChildren", "HeldAlone(at)", "EndActor(at)", "LetGo(at, false)"),
            ("EndChildren", "HeldAlone(at)", "*Ptr<byte*>(at - 80) = null", "LetGo(at, false)"),
            ("EndChildren", "HeldAlone(at)", "*Ptr<byte*>(at - 72) = null", "LetGo(at, false)"),
            ("Decided", "HeldAlone(child)", "Pause(child)", "LetGo(child, false)"),
            ("Decided", "HeldAlone(child)", "EndActor(child)", "LetGo(child, false)"),
        ]);
    }

    /// <summary>
    /// Each text <c>Access</c> of the function <c>Function</c> stands between the text <c>Take</c> and the text <c>End</c>: after the
    /// first take, and before the first end after that take, so that an end between the take and the access is no hold.
    /// </summary>
    private static void Held((string Function, string Take, string Access, string End)[] held)
    {
        foreach (var (function, take, access, end) in held)
        {
            var body = Body(function);
            var taken = body.IndexOf(take, StringComparison.Ordinal);
            var at = body.IndexOf(access, StringComparison.Ordinal);
            Assert.True(taken >= 0 && at > taken && body.IndexOf(end, taken, StringComparison.Ordinal) > at,
                $"`{function}` holds no `{access}` between `{take}` and `{end}`");
        }
    }

    [Fact]
    public void Each_word_the_threads_share_is_read_and_written_under_its_lock()
    {
        // section 6: the table of slots, the queue of the pauses, the blocks the top-level code's wait runs and the streams of `print`
        // and of the log are each read or written between the take of their lock and its end, which one thread cannot show
        Held(
        [
            ("Print", "StreamLock(fd)", "sys.WriteOut(fd, buffer + done, total - done)", "StreamUnlock(fd)"),
            ("LogSpanned", "StreamLock(2)", "sys.Write(2, logBuffer + done, n - done)", "StreamUnlock(2)"),
            ("SlotsUsed", "Lock(SlotLock())", "used = slotsUsed", "Unlock(SlotLock())"),
            ("SlotTake", "Lock(SlotLock())", "mut long index = slotFree", "Unlock(SlotLock())"),
            ("SlotFree", "Lock(SlotLock())", "slotFree = index", "Unlock(SlotLock())"),
            ("Pause", "Lock(PauseLock())", "paused = state", "Unlock(PauseLock())"),
            ("Unpause", "Lock(PauseLock())", "paused = after", "Unlock(PauseLock())"),
            ("PauseDue", "Lock(PauseLock())", "due = *Ptr<long>(*Ptr<byte*>(paused - 80) + 32)", "Unlock(PauseLock())"),
            ("PauseEnded", "Lock(PauseLock())", "paused = *Ptr<byte*>(*Ptr<byte*>(paused - 80) + 40)", "Unlock(PauseLock())"),
            ("Post", "Lock(RootLock())", "Append(rootBlocks, message)", "Unlock(RootLock())"),
            ("RootTaken", "Lock(RootLock())", "message = Taken(rootBlocks)", "Unlock(RootLock())"),
        ]);
    }

    [Theory]
    [InlineData(Break.WaiterAfterEnqueue, "a call and its answer", "a lost wake")]
    [InlineData(Break.ReadThenWrite, "a call, its answer and the crash of its waiter", "two owners of the reply")]
    [InlineData(Break.NotifiedDropped, "a wake while a thread holds the actor", "a lost wake")]
    [InlineData(Break.LeftUnheld, "a wake while a thread holds the actor on worker threads", "a lost wake")]
    [InlineData(Break.RequeueLeftOut, "a wake while a thread holds the actor", "a lost wake")]
    [InlineData(Break.RequeueAlways, "two wakes", "a cycle of moves")]
    [InlineData(Break.RunningNotHeld, "a wake while a thread holds the actor", "two threads hold the actor")]
    [InlineData(Break.BuildingNotHeld, "a wake while the constructor runs", "a message started while the actor's constructor ran")]
    [InlineData(Break.BuiltQueuesNothing, "a wake while the constructor runs", "a lost wake")]
    [InlineData(Break.WaiterReadAfterStore, "a call and its answer", "once it was gone")]
    [InlineData(Break.CrashLeavesAtOnce, "a call, its answer and the crash of its waiter", "once it was gone")]
    [InlineData(Break.RestartHoldsNothing, "a take while a restart starts", "two threads hold the actor")]
    [InlineData(Break.RestartRunsEarly, "a take while a restart starts", "a message started before the actor's restart")]
    [InlineData(Break.DropSignalsNothing, "a call, its answer or its drop, and its deadline", "a lost wake")]
    [InlineData(Break.TimeoutLeavesAtOnce, "a call, its answer or its drop, and its deadline", "once it was gone")]
    [InlineData(Break.TimerWakesNothing, "a call no callee takes, and its deadline", "a lost wake")]
    [InlineData(Break.TimerKeptPastFrame, "a deadline and the crash of the waiter's actor", "once it was gone")]
    [InlineData(Break.ReadyAfterReturn, "a call, its answer or its drop, and its deadline", "once its wait had returned")]
    [InlineData(Break.WakeBeforeLink, "a queue of many senders and its holder", "a lost message")]
    [InlineData(Break.StubPushedNotRead, "a queue of many senders and its holder", "a lost message")]
    [InlineData(Break.UnlockWakesNothing, "three threads on one lock", "a lost wake")]
    [InlineData(Break.WaitsOnOne, "three threads on one lock", "a cycle of moves")]
    [InlineData(Break.LookupReadsOnce, "a lookup while its slot is freed and taken again", "not its actor's")]
    [InlineData(Break.LinkBeforeNumber, "a lookup while its slot is freed and taken again", "not its actor's")]
    [InlineData(Break.RunIgnoresEnding, "the end of a supervisor while its child waits in a message", "did not end")]
    [InlineData(Break.EndWithoutHold, "the end of a supervisor while its child waits in a message", "while another thread held it")]
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

    /// <summary>The body of the function <paramref name="function"/> of <c>runtime/common/runtime.kz</c>, which a test holds a text of to.</summary>
    private static string Body(string function)
    {
        var text = Encoding.UTF8.GetString(Runtime.Files.Single(f => f.Path == "runtime/common/runtime.kz").Bytes).ReplaceLineEndings("\n");
        var declared = Regex.Match(text, $@"^(pub )?[\w*<>]+ {function}\([^\n]*\{{\n(.*?)\n\}}\n", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(declared.Success, $"runtime/common/runtime.kz declares no function `{function}`, which a test of the models names");
        return declared.Groups[2].Value;
    }

    [Fact]
    public void Each_move_of_the_models_names_a_function_of_the_runtime_and_the_text_it_transcribes_in_its_order()
    {
        // the guard against drift: every function a move names is declared in runtime/common/runtime.kz, the text a move transcribes and
        // the texts it decides by stand in that function's code once each, its `//` comments cut off and each run of white space read as
        // one space, so that a text in a comment counts for nothing and the order of two moves is the order of the one place of each,
        // and the moves of one function in a strand stand in their order there
        string Code(string function) => Cut(Body(function));

        // the cut and the count on code made for them: a text in a comment stands nowhere, one written twice stands twice
        Assert.Equal([0, 1, 2], new[] { Places(Cut("    // a = 1\n"), "a = 1"), Places(Cut("    a = 1 // a = 2\n"), "a = 1"), Places(Cut("    a = 1\n    a = 1\n"), "a = 1") });
        Assert.Equal([false, true, false], new[] { Once(Cut("    // a = 1\n"), "a = 1"), Once(Cut("    a  =\n 1\n"), "a = 1"), Once(Cut("    a = 1\n    a = 1\n"), "a = 1") });
        var strands = Models(Break.None).SelectMany(m => m.Strands).ToList();
        var moves = strands.SelectMany(s => s.Moves).ToList();
        Assert.True(moves.Select(m => m.Function).Distinct().Count() >= 12, string.Join(", ", moves.Select(m => m.Function).Distinct()));
        Assert.True(moves.Where(m => m.Text is not null && m.Text.Contains("MemoryOrder", StringComparison.Ordinal)).Select(m => (m.Function, m.Text)).Distinct().Count() >= 6, "the atomic operations the moves are");
        foreach (var move in moves)
        {
            var code = Code(move.Function);
            if (move.Text is not null)
            {
                Assert.True(Once(code, move.Text), $"`{move.Function}` holds `{move.Text}`, which a move of the models transcribes, {Places(code, move.Text)} times, not once");
            }

            foreach (var rule in move.Rules ?? [])
            {
                Assert.True(Once(code, rule), $"`{move.Function}` holds `{rule}`, which a move of the models decides by, {Places(code, rule)} times, not once");
            }
        }

        Assert.True(moves.SelectMany(m => m.Rules ?? []).Distinct().Count() >= 12, "the texts the moves decide by");
        var ordered = 0;
        foreach (var strand in strands)
        {
            foreach (var function in strand.Moves.Where(m => m.Text is not null).GroupBy(m => m.Function))
            {
                var texts = function.Select(m => m.Text!).Distinct().ToList();
                var code = Code(function.Key);
                for (var k = 1; k < texts.Count; k++)
                {
                    Assert.True(code.IndexOf(texts[k - 1], StringComparison.Ordinal) < code.IndexOf(texts[k], StringComparison.Ordinal),
                        $"`{function.Key}` holds `{texts[k]}` before `{texts[k - 1]}`, against the order of the moves of {strand.Name}");
                    ordered++;
                }
            }
        }

        Assert.True(ordered >= 12, $"{ordered} pairs of moves in order");
    }

    /// <summary>The code of a body as the guard reads it: each <c>//</c> comment cut off, each run of white space one space.</summary>
    private static string Cut(string body) => Regex.Replace(Regex.Replace(body, "//[^\n]*", string.Empty), @"\s+", " ");

    /// <summary>The count of the places at which <paramref name="text"/> stands in <paramref name="code"/>.</summary>
    private static int Places(string code, string text)
    {
        var count = 0;
        for (var at = code.IndexOf(text, StringComparison.Ordinal); at >= 0; at = code.IndexOf(text, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>Whether <paramref name="text"/> stands in <paramref name="code"/> at one place, as the guard requires of every text.</summary>
    private static bool Once(string code, string text) => Places(code, text) == 1;
}
