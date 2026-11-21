using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 19 on the one thread the runtime runs, as tokio's <c>current_thread</c> runtime runs its tasks (Q1 (a), the answers of
/// 2026-11-14, issue 519, comment 6880404917; <c>compiler/README.md</c> lists what of section 6 this does not build), each message on a
/// stack of its own, where a call that waits parks it until its reply is set, so that no function is rewritten (section 6, "Own
/// stack"). An actor's state is an instance of its body, laid out and constructed as a class's
/// (<see cref="Construct"/>), in the first block of the actor's own heap, which holds the actor's record in front of it, as a string's
/// block stands in front of its bytes (the runtime's <c>ActorNew</c>; section 3): the reference is the index of the actor's slot in the
/// runtime's table and its number, which the record holds, so that the actor's own methods make it from <c>this</c>
/// (<see cref="Reference"/>; U10, U11). Nothing counts a reference: an actor that ends frees its heap whole at once, and a reference to
/// it then finds no actor (the runtime's <c>Lookup</c>; section 6, the owner, 2026-11-16, #519, comment 6919351399), and the end of the
/// process frees every heap (U13). A call through a reference, or after <c>send</c>, is a message, one block of the heap of the actor it
/// goes to, as actix packs a message and the sender of its reply into one envelope: the
/// message after it in its list, which the runtime's <c>Enqueue</c> sets, the table of its call site, the function that runs it and the
/// one that copies it into the heap of an actor's new instance (U21) and the one that drops it unrun (U37), the reply its caller
/// waits on, null after <c>send</c>, as a message of a tokio actor carries a <c>oneshot</c> sender or none, the chain ID, 16 bytes,
/// and its deadline and that of the <c>within</c> block that runs while it runs, which <c>Enqueue</c> stamps (section 6, "Deadlocks",
/// layer 1; U37, U38), then the arguments, each holding a count of its own
/// (<see cref="EmitMessage"/>). The function of a method's messages calls the method on the state, as actix's
/// <c>EnvelopeProxy::handle</c> calls the handler on the actor (<see cref="MessageRun"/>).
/// </summary>
public sealed partial class Emitter
{
    // the slots of the heaps of the `spawn`s, and of the messages built in a heap of their own, begun in each region, null once the actor
    // took its heap or the message was queued, which the region's end, or a jump out of it, frees where a case propagated out of an argument
    // after it left one (EndHeaps)
    private readonly Dictionary<List<(string Value, KurzType Type)>, List<(string Slot, string Free)>> regionHeaps = new(ReferenceEqualityComparer.Instance);

    // the deadline that the `within` block whose statement is the region replaced, which the region's end, or a jump out of it, puts
    // back (EndWithin)
    private readonly Dictionary<List<(string Value, KurzType Type)>, string> regionBlocks = new(ReferenceEqualityComparer.Instance);

    // the global of each actor type's name, for the log line of a crash (U24), declared once for its type
    private readonly Dictionary<ClassSymbol, string> actorNames = [];

    // the count of the call sites and `spawn`s whose table or restart a function of its own serves, which names each one
    private int actorSites;

    // U51, U52: the function that finds or starts the one actor of each `per node` type, set for its type before its globals are
    // declared and before any function its start calls is built, so that a builder nested in it finds it and declares nothing again
    private readonly Dictionary<ClassSymbol, string> perStarts = [];

    /// <summary>
    /// U2, U3: <c>spawn Name(args)</c>, the state constructed as an instance of a class is (K1, K7, D11) in the room the runtime's
    /// <c>ActorNew</c> gives behind the actor's record, both its counts at one, as <see cref="EmitClassNew"/> makes them, which the
    /// record holds to the end of the process, so that a count a lambda takes of <c>this</c> never lets go of the state; the arguments,
    /// the constructor and the defaults of the body run here, before <c>spawn</c> gives the reference, which on one thread is the new
    /// actor running its constructor while the spawner waits (U3). The record keeps the size of the actor's stack, the one its <c>stack</c>
    /// clause writes or 2 MiB, which the runtime rounds up to its page and raises to its floor (U1; section 6, "2 MiB + clause"). The
    /// actor's heap is made first, its first page sized to the record and the state (section 3), and the arguments cross into it as a
    /// message's do (U12, <see cref="Crossed"/>); the constructor and the defaults then allocate in it, as the actor's code does, and
    /// the spawner's heap is served again once the state stands. The spawner's heap, the supervisor's, holds the block of the actor's
    /// restart with a copy of each argument the constructor is given (<see cref="KeptArguments"/>; U22) where the policy restarts, the policy
    /// written after the <c>spawn</c>, else the type's, else <c>on crash restart</c> (U25, U27); <c>escalate</c> keeps the block alone, which
    /// names no function, and <c>stop</c> keeps none (section 6); a block that decides, <c>on crash (c) { ... }</c>, keeps the block of a
    /// restart, which names the block's function value, made here in the supervisor's heap after the arguments and before the constructor
    /// runs, so that the variables it takes by value are taken after the arguments ran, as the checker reads the block after them and as
    /// it is written, and the function that runs it (<see cref="Deciding"/>; U28, U31). While the constructor runs, the runtime names the actor as the one whose
    /// constructor runs, so that an actor it spawns is its child (the runtime's <c>Building</c>; U13), and its record says that its
    /// constructor runs until <c>ActorBuilt</c>, so that an escalation that reaches it is its spawner's crash (U3, U25).
    /// </summary>
    private string EmitActorSpawn(SpawnExpression s)
    {
        var info = Model.CallOf(s.Call);
        var symbol = ((ClassType)info.Target!).Symbol;
        var block = layout.ClassBlock(symbol);
        var stack = StackOf(symbol);
        var supervisor = fn.Value($"call ptr {StdFunction("HeapCurrent")}()");
        var heap = fn.Value($"call ptr {RuntimeFunction("ActorHeapNew")}(i64 {IrSize(block)})");
        var left = HeapBegun(heap, "HeapLeft");
        var spawner = fn.Alloca("ptr", "spawner");
        var building = fn.Alloca("ptr", "building");
        var side = "null";
        var policy = s.Policy ?? symbol.Declaration.Clauses?.FirstOrDefault(c => c.Policy is not null)?.Policy ?? new CrashPolicy(s.Line, "restart");
        var state = Construct(symbol, s.Call, info, () =>
        {
            var state = fn.Value($"call ptr {RuntimeFunction("ActorNew")}(ptr {heap}, i64 {IrSize(block)}, i64 {stack}, ptr {side}, ptr {ActorName(symbol)})");
            fn.Emit($"store ptr null, ptr {left}");
            fn.Emit($"store i64 1, ptr {state}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {state}, i32 0, i32 1")}");
            fn.Emit($"store ptr {fn.Value($"call ptr {RuntimeFunction("Building")}(ptr {state})")}, ptr {building}");
            fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapUse")}(ptr {heap})")}, ptr {spawner}");
            return state;
        }, into: heap, kept: (arguments, types) => side = policy.Word switch
        {
            "restart" => KeptArguments(s.Call, info, symbol, arguments, types, supervisor, policy),
            "escalate" => Escalating(supervisor),
            "block" => Deciding(KeptArguments(s.Call, info, symbol, arguments, types, supervisor, policy), EmitLambda(policy.Block!, Model.LambdaOf(policy.Block!).Type), policy.Block!, supervisor),
            _ => "null",
        });
        fn.Emit($"call ptr {RuntimeFunction("Building")}(ptr {fn.Value($"load ptr, ptr {building}")})");
        fn.Emit($"call void {RuntimeFunction("ActorBuilt")}(ptr {state})");
        fn.Emit($"call ptr {StdFunction("HeapUse")}(ptr {fn.Value($"load ptr, ptr {spawner}")})");
        return Reference(state);
    }

    /// <summary>U1: the size of the stack each message of an actor of the type <paramref name="symbol"/> runs on, the one its <c>stack</c> clause writes or 2 MiB.</summary>
    private string StackOf(ClassSymbol symbol) => symbol.Declaration.Clauses?.FirstOrDefault(c => c.Size is not null)?.Size is { } size
        ? ((IntegerConstant)Model.TypeOf(size).Constant!).Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "2097152";

    /// <summary>
    /// U2, U10, U11: the reference of the actor whose state is <paramref name="state"/>, the two words its record holds: the index of its
    /// slot in the runtime's table and its number (the runtime's <c>ActorNew</c>; section 6).
    /// </summary>
    private string Reference(string state)
    {
        var slot = fn.Value($"load i64, ptr {fn.Value($"getelementptr i8, ptr {state}, i64 -56")}");
        var number = fn.Value($"load i64, ptr {fn.Value($"getelementptr i8, ptr {state}, i64 -40")}");
        return fn.Value($"insertvalue %kz.actor {fn.Value($"insertvalue %kz.actor undef, i64 {slot}, 0")}, i64 {number}, 1");
    }

    /// <summary>U51, U52: the reference of the one actor of the <c>per node</c> type <paramref name="symbol"/>, found or started (<see cref="PerStart"/>).</summary>
    private string PerReference(ClassSymbol symbol) => fn.Value($"call %kz.actor {PerStart(symbol)}()");

    /// <summary>
    /// U51 to U54 (assumed: T27 to T33 of issue 519, comment 6983903100, at their defaults): the function a message to the name of the
    /// <c>per node</c> type <paramref name="symbol"/> calls before its arguments run, made once for the type with three globals of its
    /// own, declared here, once, where its entry is set: its cell, the lock, the slot and the number of its one actor (the runtime's
    /// <c>PerFind</c>); its block of a restart, as <see cref="KeptArguments"/> makes a <c>spawn</c>'s with no argument kept, naming the
    /// function of its restart (<see cref="Restarted"/>), or none for <c>escalate</c>, the policy after its name or <c>restart</c> (U27),
    /// and no block for <c>stop</c>; and its knobs where <c>after</c> or <c>max N in</c> is written, marked as a <c>per node</c> type's
    /// (<see cref="KnobsValue"/>), whose place no <c>restart-limit</c> names, so that it holds none. The function finds the actor that
    /// stands in the cell; where none does, a crash or a <c>stop</c> having ended the one before or no message having named it, it makes
    /// one with that block of a restart, whose supervisor <c>ActorNew</c> takes as at a <c>spawn</c>, the code that runs (U53), stores it
    /// in the cell before its constructor runs (<c>PerStarted</c>), so that a message that reaches it meanwhile waits for it, and runs the
    /// constructor, the defaults of its body, on the stack of that code, as a <c>spawn</c> runs it (<see cref="EmitActorSpawn"/>; U3), so
    /// that a throw there is that code's crash, which ends the actor half built, its supervisor's child; then the root becomes its
    /// supervisor (<c>PerBuilt</c>). A message to it from the code its constructor runs waits for a constructor that waits for it, the
    /// top-level code's <c>deadlock</c> on one thread (U18).
    /// </summary>
    private string PerStart(ClassSymbol symbol)
    {
        if (perStarts.TryGetValue(symbol, out var start))
        {
            return start;
        }

        var n = Number(perStarts.Count);
        perStarts[symbol] = start = $"@\"actor.per.{n}\"";
        var cell = $"@\"per.{n}.cell\"";
        module.Global($"{cell} = internal global {{ i32, i64, i64 }} zeroinitializer, align 8");
        var policy = symbol.Declaration.Clauses?.FirstOrDefault(c => c.Policy is not null)?.Policy ?? new CrashPolicy(symbol.Line, "restart");
        var knobs = "null";
        if (policy.Word == "restart" && (policy.After is not null || policy.Max is not null))
        {
            knobs = $"@\"per.{n}.knobs\"";
            module.Global($"{knobs} = internal global {KnobsType} {KnobsValue(policy, "zeroinitializer", 0, perNode: true)}, align 8");
        }

        var side = "null";
        if (policy.Word is "restart" or "escalate")
        {
            side = $"@\"per.{n}.side\"";
            var restart = policy.Word == "restart" ? Restarted(symbol, null, [], []) : "null";
            module.Global($"{side} = internal global {SideType([])} {{ ptr {restart}, ptr null, i64 0, i64 0, i64 0, ptr null, ptr {knobs}, ptr null, {Kept([])} zeroinitializer }}, align 8");
        }

        return Helper($"actor.per.{n}", "%kz.actor", [], () =>
        {
            var block = layout.ClassBlock(symbol);
            var found = fn.Value($"call ptr {RuntimeFunction("PerFind")}(ptr {cell})");
            var stands = fn.Label("stands");
            var starts = fn.Label("starts");
            fn.CondBranch(fn.Value($"icmp ne ptr {found}, null"), stands, starts);
            fn.Block(stands);
            fn.Return($"%kz.actor {Reference(found)}");
            fn.Block(starts);
            var heap = fn.Value($"call ptr {RuntimeFunction("ActorHeapNew")}(i64 {IrSize(block)})");
            var state = fn.Value($"call ptr {RuntimeFunction("ActorNew")}(ptr {heap}, i64 {IrSize(block)}, i64 {StackOf(symbol)}, ptr {side}, ptr {ActorName(symbol)})");
            fn.Emit($"call void {RuntimeFunction("PerStarted")}(ptr {cell}, ptr {state})");
            fn.Emit($"store i64 1, ptr {state}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {state}, i32 0, i32 1")}");
            var building = fn.Value($"call ptr {RuntimeFunction("Building")}(ptr {state})");
            var caller = fn.Value($"call ptr {StdFunction("HeapUse")}(ptr {heap})");
            if (symbol.Fields.Any(f => f.Body?.Value is not null))
            {
                fn.Emit($"call void {ClassDefaults(symbol)}(ptr {state})"); // K7: a `per node` actor has no parameter (U51) and no constructor of its body (U2)
            }

            fn.Emit($"call ptr {RuntimeFunction("Building")}(ptr {building})");
            fn.Emit($"call void {RuntimeFunction("PerBuilt")}(ptr {state})");
            fn.Emit($"call ptr {StdFunction("HeapUse")}(ptr {caller})");
            fn.Return($"%kz.actor {Reference(state)}");
        });
    }

    /// <summary>The reference <paramref name="reference"/> as the two arguments the runtime's functions take for it: the index of the slot, then the number.</summary>
    private string ReferenceWords(string reference) =>
        $"i64 {fn.Value($"extractvalue %kz.actor {reference}, 0")}, i64 {fn.Value($"extractvalue %kz.actor {reference}, 1")}";

    /// <summary>
    /// U2, U24: the global of the name of the actor type <paramref name="symbol"/>, a text the record of each of its actors names for the
    /// log line of a crash, declared once for its type.
    /// </summary>
    private string ActorName(ClassSymbol symbol)
    {
        if (!actorNames.TryGetValue(symbol, out var name))
        {
            actorNames[symbol] = name = $"@\"actor.name.{Number(actorNames.Count)}\"";
            module.Global($"{name} = private unnamed_addr constant %kz.str {StringValue(symbol.Name)}");
        }

        return name;
    }

    /// <summary>
    /// U22, section 6 (Q13): the block of the restart of the actor a <c>spawn</c> makes, in the heap <paramref name="heap"/> of its
    /// supervisor, the spawner, as Erlang's supervisor keeps the start arguments of its child: the function that runs the constructor
    /// again (<see cref="Restarted"/>), the first word of a message's table, which the restart's message names (the runtime's
    /// <c>Restart</c>), a word that stands where a table's copy stands, the pause, the time of the last restart, the time the pause ends
    /// and the next actor of the queue of the pauses, each the runtime's (<c>Pause</c>), the block of the policy's knobs, null where it
    /// writes none (<see cref="Knobs"/>, U26), the block of a policy that decides, null for none (<see cref="Deciding"/>, U28), and a
    /// copy of each argument the constructor is given, its default among them, every counted
    /// block it reaches copied into that heap, a <c>move</c> argument's too (the taken points of comment 6899225864).
    /// </summary>
    private string KeptArguments(CallExpression c, CallInfo info, ClassSymbol symbol, List<string> arguments, List<KurzType> types, string heap, CrashPolicy policy)
    {
        var copiesInto = CopiesInto(c, info, types);
        var side = SideType(types);
        var restart = Restarted(symbol, info.Constructor, types, copiesInto);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(side)})");
        fn.Emit($"store {side} {{ ptr {restart}, ptr null, i64 0, i64 0, i64 0, ptr null, ptr null, ptr null, {Kept(types)} zeroinitializer }}, ptr {block}");
        if (Knobs(policy, heap, c.Line) is { } knobs)
        {
            fn.Emit($"store ptr {knobs}, ptr {fn.Value($"getelementptr {side}, ptr {block}, i32 0, i32 6")}");
        }
        var session = CallSession(types.Count(Counted), heap);
        for (var i = 0; i < types.Count; i++)
        {
            var source = fn.Alloca(Ir(types[i]), "kept");
            fn.Emit($"store {arguments[i]}, ptr {source}");
            var value = fn.Value($"load {Ir(types[i])}, ptr {source}");
            var place = fn.Value($"getelementptr {side}, ptr {block}, i32 0, i32 8, i32 {Number(i)}");
            if (Counted(types[i]))
            {
                CrossInto(place, value, types[i], session, heap, copiesInto[i]); // U12: into the place the restart reads, which the session's end points
                continue;
            }

            fn.Emit($"store {Ir(types[i])} {value}, ptr {place}");
        }

        return block;
    }

    /// <summary>
    /// U3, U22: the function a restart of the actor of the type <paramref name="symbol"/> runs, with the state and the restart's message
    /// (the runtime's <c>Restart</c>), made for its <c>spawn</c>, or for the type where it is <c>per node</c>, which keeps no argument
    /// (<see cref="PerStart"/>): a copy of each kept argument in the actor's heap, the one the allocation
    /// entry serves, the state's counts at one, and the constructor run on them as at the <c>spawn</c>, a constructor of the body
    /// borrowing them, since <c>move</c> before a parameter of a constructor is <c>not supported</c> (<c>Semantics/Checker.Constructors.cs</c>),
    /// so that the copies are released after it, the primary one owning them, then the defaults of the body (K7); then the message freed. A
    /// throw there is the actor's own crash (U3).
    /// </summary>
    private string Restarted(ClassSymbol symbol, ConstructorSymbol? constructor, List<KurzType> types, List<string?> copiesInto) =>
        Helper($"actor.restart.{symbol.Name}.{Number(++actorSites)}", "void", ["ptr", "ptr"], () =>
        {
            var side = SideType(types);
            // the block of the restart in the supervisor's heap, read across the copies: the actors of a program run on one thread
            // (the runtime's actors), these reads run no call that waits, and a supervisor's crash ends its children before it frees its
            // heap (`Fall`: `EndChildren` before `Emptied`), an idle or paused child at once, its block of the restart dropped (`EndActor`)
            var table = fn.Value($"load ptr, ptr {fn.Value("getelementptr { ptr, ptr }, ptr %a1, i32 0, i32 1")}");
            var heap = fn.Value($"call ptr {StdFunction("HeapCurrent")}()");
            var session = CallSession(types.Count(Counted), heap, noExit: true);
            var values = new List<string>();
            var copies = new string?[types.Count];
            for (var i = 0; i < types.Count; i++)
            {
                var value = fn.Value($"load {Ir(types[i])}, ptr {fn.Value($"getelementptr {side}, ptr {table}, i32 0, i32 8, i32 {Number(i)}")}");
                if (Counted(types[i]))
                {
                    copies[i] = fn.Alloca(Ir(types[i]), "copy");
                    CrossInto(copies[i]!, value, types[i], session, heap, copiesInto[i]);
                }

                values.Add(value);
            }

            for (var i = 0; i < types.Count; i++)
            {
                if (copies[i] is { } copy)
                {
                    values[i] = fn.Value($"load {Ir(types[i])}, ptr {copy}"); // U12: read after the last copy, which points its `weak` references
                }
            }

            var block = layout.ClassBlock(symbol);
            fn.Emit("store i64 1, ptr %a0");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1")}");
            if (constructor is { } made)
            {
                fn.Emit($"call void {Constructor(made)}({string.Join(", ", values.Select((v, i) => $"{Ir(types[i])} {v}").Prepend("ptr %a0"))})");
                for (var i = 0; i < types.Count; i++)
                {
                    if (Counted(types[i]))
                    {
                        Release(values[i], types[i]);
                    }
                }
            }
            else
            {
                for (var i = 0; i < values.Count; i++)
                {
                    fn.Emit($"store {Ir(types[i])} {values[i]}, ptr {InstanceFieldPointer(symbol, "%a0", symbol.Fields.IndexOf(symbol.Constructor[i].Field))}");
                }

                if (symbol.Fields.Any(f => f.Body?.Value is not null))
                {
                    fn.Emit($"call void {ClassDefaults(symbol)}(ptr %a0)");
                }
            }

            fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 56)"); // the restart's message (the runtime's `Restart`)
            fn.Return(null);
        });

    /// <summary>The IR type of the block of a restart whose constructor takes values of the types <paramref name="types"/> (<see cref="KeptArguments"/>).</summary>
    private string SideType(List<KurzType> types) => $"{{ ptr, ptr, i64, i64, i64, ptr, ptr, ptr, {Kept(types)} }}";

    /// <summary>
    /// U28, U31: the block of a restart <paramref name="side"/> of a policy that decides, given the block of the decision, in the heap
    /// <paramref name="heap"/> of the supervisor: the function that runs the block (<see cref="BlockEntry"/>) and the block's function value
    /// <paramref name="decides"/>, one holder more of its environment, which takes the locals the block reads by value at the <c>spawn</c>
    /// and <c>this</c> of the supervisor, as a lambda does (F15; assumed: S7 of issue 519, comment 6907441402). A block can
    /// restart its child, so the block of the restart keeps the copies of the arguments (section 6, "restart-needs-copy").
    /// </summary>
    private string Deciding(string side, string decides, LambdaExpression block, string heap)
    {
        const string type = "{ ptr, %kz.fn }";
        var record = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(type)})");
        fn.Emit($"store ptr {BlockEntry(block)}, ptr {fn.Value($"getelementptr {type}, ptr {record}, i32 0, i32 0")}");
        fn.Emit($"call void {RetainEnvironment()}(%kz.fn {decides})");
        fn.Emit($"store %kz.fn {decides}, ptr {fn.Value($"getelementptr {type}, ptr {record}, i32 0, i32 1")}");
        fn.Emit($"store ptr {record}, ptr {fn.Value($"getelementptr i8, ptr {side}, i64 56")}"); // the side's eighth field (SideType), at byte 56 on every target, each 64-bit, where the runtime's `RunBlock` reads it
        return side;
    }

    /// <summary>
    /// U30, U34: the function that runs the block <paramref name="block"/>, which the runtime calls with the block of the restart and the
    /// block's message: <c>c</c>, the <c>Crashed</c> it reads (<see cref="CrashedOf"/>), then the block's function value called with it,
    /// which sets the decision (<c>Decide</c>), and <c>c</c> let go of.
    /// </summary>
    private string BlockEntry(LambdaExpression block)
    {
        var crashed = (DataType)Model.LambdaOf(block).Type.Parameters[0];
        return Helper($"actor.block.{Number(++actorSites)}", "void", ["ptr", "ptr"], () =>
        {
            const string type = "{ ptr, %kz.fn }";
            var record = fn.Value("load ptr, ptr " + fn.Value("getelementptr i8, ptr %a0, i64 56"));
            var decides = fn.Value($"load %kz.fn, ptr {fn.Value($"getelementptr {type}, ptr {record}, i32 0, i32 1")}");
            var c = CrashedOf(crashed, "%a1");
            fn.Emit($"call void {fn.Value($"extractvalue %kz.fn {decides}, 0")}(ptr {fn.Value($"extractvalue %kz.fn {decides}, 1")}, {Ir(crashed)} {c})");
            Release(c, crashed);
            fn.Return(null);
        });
    }

    /// <summary>
    /// U30, U46: the <c>Crashed</c> <paramref name="crashed"/> that a block of <c>on crash</c> reads and an <c>attempt</c> gives, built from
    /// the copy of the crash the message <paramref name="message"/> holds in the heap of the code that takes it (the runtime's
    /// <c>Pending</c>): its <c>Reason</c> the thrown value of the case its type's number names, whose block the message holds and which
    /// is freed once read, or, for the runtime's own crash, the runtime's error of its id and its text (std's <c>RuntimeErrorOf</c>, which
    /// throws for an id no row of the run-time table names, as for a type the <c>Reason</c> lacks), its <c>Place</c> the file and the line,
    /// and its <c>Chain</c> the crashing chain's ID as text.
    /// </summary>
    private string CrashedOf(DataType crashed, string message)
    {
        var reason = crashed.TypeArguments[0];
        var place = new DataType(std.Model.DataTypes.Single(d => d.Name == "Place"), []);
        var error = std.Model.DataTypes.Single(d => d.Name == "RuntimeError");
        string Word(int offset, string ir) => fn.Value($"load {ir}, ptr {fn.Value($"getelementptr i8, ptr {message}, i64 {Number(offset)}")}");
        string Text(int at, int count) => fn.Value($"call %kz.str {RuntimeFunction("TextAt")}(ptr {Word(at, "ptr")}, i64 {Word(count, "i64")})");

        var number = Word(152, "i64");
        var held = Word(160, "ptr");
        var slot = fn.Alloca(Ir(reason), "reason");
        IReadOnlyList<KurzType> cases = reason is Semantics.UnionType u ? u.Cases : [reason];
        var labels = cases.Select(_ => fn.Label("case")).ToList();
        var runtime = fn.Label("runtime");
        var done = fn.Label("reason");
        fn.Terminate($"switch i64 {number}, label %{runtime} [ {string.Join(" ", cases.Select((c, i) => $"i64 {Number(layout.Number(c) + 1)}, label %{labels[i]}"))} ]");
        string Case(KurzType c, string? value) => reason is Semantics.UnionType union ? MakeUnion(union, c, value) : value!;
        for (var i = 0; i < cases.Count; i++)
        {
            fn.Block(labels[i]);
            string? value = cases[i] is NullType ? "zeroinitializer" : null; // a thrown `null` holds no value, which the message holds none of
            if (cases[i] is not (Semantics.VoidType or NullType))
            {
                value = fn.Value($"load {Ir(cases[i])}, ptr {held}");
                fn.Emit($"call void {StdFunction("Free")}(ptr {held}, i64 {IrSize(Ir(cases[i]))})");
            }

            fn.Emit($"store {Ir(reason)} {Case(cases[i], value)}, ptr {slot}");
            fn.Branch(done);
        }

        fn.Block(runtime);
        var id = Text(72, 80);
        var detail = Text(120, 128);
        var errorType = new DataType(error, []);
        var made = fn.Value($"call {Ir(errorType)} {StdFunction("RuntimeErrorOf")}(%kz.str {id}, %kz.str {detail})");
        Release(id, Types.String);
        Release(detail, Types.String);
        fn.Emit($"store {Ir(reason)} {Case(cases.Single(c => c is DataType d && d.Symbol == error), made)}, ptr {slot}");
        fn.Branch(done);

        fn.Block(done);
        var file = fn.Value($"call %kz.str {RuntimeFunction("TextAt")}(ptr {Word(88, "ptr")}, i64 {Word(96, "i64")})");
        var line = fn.Value($"trunc i64 {Word(104, "i64")} to i32");
        var where = Make(place, [file, line]);
        var chain = fn.Value($"call %kz.str {RuntimeFunction("ChainText")}(ptr {fn.Value($"getelementptr i8, ptr {message}, i64 136")})");
        return Make(crashed, [fn.Value($"load {Ir(reason)}, ptr {slot}"), where, chain]);
    }

    /// <summary>
    /// U30: the function that copies a thrown value of the type <paramref name="type"/> from its slot into a heap, which the runtime calls
    /// where a block takes the crash, before the child's heap is freed: a block of that heap that holds the copy, every counted block it
    /// reaches copied there in a session of its own, as a crossing copies it (U12).
    /// </summary>
    private string ThrownCopy(KurzType type) => Helper($"thrown.copy.{Lower.Mangle(type)}", "ptr", ["ptr", "ptr"], () =>
    {
        var value = fn.Value($"load {Ir(type)}, ptr %a0");
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr %a1, i64 {IrSize(Ir(type))})");
        fn.Emit($"store {Ir(type)} {(Counted(type) ? Crossed(value, type, "%a1") : value)}, ptr {block}");
        fn.Return($"ptr {block}");
    });

    /// <summary>
    /// U26: the block of the knobs of a restart's policy, in the heap <paramref name="heap"/> of the supervisor, made where <c>after</c> or
    /// <c>max N in</c> is written, else null, so that a <c>spawn</c> that writes neither keeps one word for them: the first pause, 100 ms or what
    /// <c>after</c> writes, the count of <c>max N in</c>, 0 where no limit holds, and its window, then the ring of the times of the crashes in
    /// the window, its slots, its first and its count, the runtime's (<c>Limited</c>), the place of the <c>spawn</c> at the line
    /// <paramref name="line"/>, which <c>restart-limit</c> names, and 0, a <c>spawn</c>'s mark (<see cref="KnobsValue"/>).
    /// </summary>
    private string? Knobs(CrashPolicy policy, string heap, int line)
    {
        if (policy.After is null && policy.Max is null)
        {
            return null;
        }

        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(KnobsType)})");
        fn.Emit($"store {KnobsType} {KnobsValue(policy, StringValue(unit.File), line, perNode: false)}, ptr {block}");
        return block;
    }

    /// <summary>The IR type of the block of a policy's knobs (<see cref="Knobs"/>).</summary>
    private const string KnobsType = "{ i64, i64, i64, ptr, i64, i64, i64, %kz.str, i64, i64 }";

    /// <summary>
    /// U26, U54: the constant value of the block of the knobs of <paramref name="policy"/>, the place <paramref name="file"/> and
    /// <paramref name="line"/> after them, then a word that holds 1 where the block is a <c>per node</c> type's, whose actor past its
    /// limit ends with no <c>restart-limit</c> (the runtime's <c>Fell</c>), and 0 for a <c>spawn</c>'s.
    /// </summary>
    private string KnobsValue(CrashPolicy policy, string file, int line, bool perNode)
    {
        var first = policy.After is { } after ? Knob(after) : "100000000";
        var max = policy.Max is { } count ? Knob(count) : "0";
        var window = policy.Within is { } within ? Knob(within) : "0";
        return $"{{ i64 {first}, i64 {max}, i64 {window}, ptr null, i64 0, i64 0, i64 0, %kz.str {file}, i64 {Number(line)}, i64 {(perNode ? "1" : "0")} }}";
    }

    /// <summary>U26: the constant a knob of a policy holds, read by the checker: a duration's nanoseconds or the count.</summary>
    private string Knob(Expression literal) =>
        ((IntegerConstant)Model.TypeOf(literal).Constant!).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// U25: the block of the policy <c>escalate</c> of the actor a <c>spawn</c> makes, in the heap <paramref name="heap"/> of its supervisor:
    /// a block of a restart with no function to run, and no kept arguments, since the policy never restarts the actor (section 6), which
    /// the runtime reads as the policy that crashes the supervisor too.
    /// </summary>
    private string Escalating(string heap)
    {
        var side = SideType([]);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(side)})");
        fn.Emit($"store {side} zeroinitializer, ptr {block}");
        return block;
    }

    private string Kept(List<KurzType> types) => $"{{ {string.Join(", ", types.Select(Ir))} }}";

    /// <summary>
    /// U12, Q5: for each slot of the call whose type is in <paramref name="types"/>, the function that copies its function value, by the
    /// environment of the lambda the checker saw for the argument bound to it (<see cref="CopyEnvironmentInto"/>), and null for a slot of
    /// any other type and for one its default fills, whose value is <c>null</c>, a parameter's default being a constant expression (F12),
    /// with no environment to copy, which the copy of its type keeps as it is.
    /// </summary>
    private List<string?> CopiesInto(CallExpression c, CallInfo info, List<KurzType> types) => types.Select((t, j) =>
    {
        if ((Types.NonNull(t) ?? t) is not Semantics.FunctionType)
        {
            return null;
        }

        var bound = Enumerable.Range(0, info.Binding.Count).Where(i => info.Binding[i] == j).Select(i => c.Arguments[i].Value).FirstOrDefault();
        if (bound is null)
        {
            return null;
        }

        var lambda = Model.CrossingLambda(bound);
        return CopyEnvironmentInto(Model.LambdaOf(lambda ?? throw new InvalidOperationException($"a function value is kept at line {c.Line} with no lambda the checker saw")).Captures.Select(v => Sub(v.Type)).ToList());
    }).ToList();

    /// <summary>
    /// The slot of the heap <paramref name="heap"/>, of a <c>spawn</c> or of a message built in a heap of its own, listed among the heaps
    /// begun in the region with the runtime's function <paramref name="free"/> that frees it (<see cref="EndHeaps"/>); the code stores
    /// null there once the actor took the heap or the message was queued.
    /// </summary>
    private string HeapBegun(string heap, string free)
    {
        var left = fn.Alloca("ptr", "heap", "null");
        fn.Emit($"store ptr {heap}, ptr {left}");
        if (regions.Count > 0)
        {
            if (!regionHeaps.TryGetValue(regions[^1], out var begun))
            {
                regionHeaps[regions[^1]] = begun = [];
            }

            begun.Add((left, free));
        }

        return left;
    }

    /// <summary>
    /// At a region's end, or a jump out of it, after the region let go of its copies, the heap of each <c>spawn</c> begun in it that a
    /// case propagated out of an argument after it left without its actor, and of each message built in a heap of its own that it left
    /// unqueued, freed whole (the runtime's <c>HeapLeft</c> and <c>MessageHeapFree</c>).
    /// </summary>
    private void EndHeaps(List<(string Value, KurzType Type)> temporaries)
    {
        if (regionHeaps.TryGetValue(temporaries, out var begun))
        {
            foreach (var (left, free) in begun)
            {
                fn.Emit($"call void {RuntimeFunction(free)}(ptr {fn.Value($"load ptr, ptr {left}")})");
            }
        }
    }

    /// <summary>
    /// U38: <c>within d { ... }</c>, its bound read, then its deadline set by the runtime's <c>BlockEnter</c>, which gives the deadline it
    /// replaced, then its body; the end of the statement's region, and every jump out of it, a <c>return</c>, a <c>break</c>, a
    /// <c>continue</c> or a case propagated out, puts that deadline back (<see cref="EndWithin"/>), as Kotlin's <c>withTimeout</c> ends its
    /// scope on every way out; a crash ends the activation with it, and a <c>throw</c> in the top-level code the process.
    /// </summary>
    private void EmitWithin(WithinStatement w)
    {
        var bound = EmitExpression(w.Bound);
        regionBlocks[regions[^1]] = fn.Value($"call i64 {RuntimeFunction("BlockEnter")}(i64 {bound})");
        EmitBlock(w.Body);
    }

    /// <summary>At the end of the region of a <c>within</c> block's statement, or a jump out of it, the deadline its entry replaced set again.</summary>
    private void EndWithin(List<(string Value, KurzType Type)> temporaries)
    {
        if (regionBlocks.TryGetValue(temporaries, out var before))
        {
            fn.Emit($"call void {RuntimeFunction("BlockExit")}(i64 {before})");
        }
    }

    /// <summary>
    /// U39, U19: whether the bound of a call's clause can raise with no call in it, an index out of range or an operator's
    /// <c>overflow</c> or <c>divide-by-zero</c> (chapter 12), which crashes the actor whose code runs it: anything but a duration's literal,
    /// a number with its unit (L14), its sign included, or a read of a name, either in brackets, a bound of another type being
    /// <c>type-mismatch</c> (U39, U40); a bound that runs a call, a name that runs a property's among them, can wait
    /// (<see cref="SemanticModel.CanWait"/>).
    /// </summary>
    private static bool Raises(Expression bound) => bound switch
    {
        ParenthesizedExpression p => Raises(p.Inner),
        UnitLiteral => false,
        UnaryExpression { Operand: UnitLiteral } => false,
        NameExpression => false,
        _ => true,
    };

    /// <summary>
    /// U4, U6, U11: a call of an actor's method as a message: the actor's reference, read first, or, inside it, made from <c>this</c>
    /// (U11), or the one actor of a <c>per node</c> type found or started by its name (<see cref="PerStart"/>; U52), then the arguments, each a copy of its own in the actor's heap where it holds a block (U12,
    /// <see cref="Crossed"/>; comment 6902210646, Q5), stored into a block of that heap that the message's function frees once it ran; the block goes to the end of the actor's inbox (U5, U15), or of its side list, where it is a
    /// call of the chain the actor waits in (U17). A call that waits (U4), wherever it stands, passes a reply in its own frame, the one-shot
    /// slot of section 6, a word the message's function sets once the result stands beside it, the activation that waits on it, which the
    /// runtime's <c>ReplyWaiter</c> stores before <c>Enqueue</c> queues the message, so that whoever answers it on any thread finds it there,
    /// and the heap the caller allocates in, which the result is copied into, and
    /// the runtime's <c>Wait</c>, given the call's place for its <c>deadlock</c>, runs messages until it is set in the top-level code and
    /// parks the running message's activation on its stack inside a message, which no other code reaches while it is parked but the calls
    /// of its chain, its actor's own methods; the result is then the caller's, a temporary of its region as any call's result is. A
    /// <c>send</c> passes none (U6). The message names the table of its call site, a constant: the function that runs it and the one
    /// that copies it into the heap of the actor's new instance after a crash (<see cref="MessageCopy"/>; U21); <c>Wait</c> is given the
    /// message, so that a crash of the waiting actor unhooks the reply from it (U20). Where any argument or the bound of the call's clause
    /// (U39) can wait, one that runs a call
    /// anywhere in it (<see cref="SemanticModel.CanWait"/>), the wait lets the actor crash, which frees or empties the heap taken before it,
    /// that the arguments and the message would go into (U21), or end (U13): the message is then built in a heap of its own, made for it (the runtime's
    /// <c>MessageHeapNew</c>), and at its queueing copied into the heap the actor has then by the function of its call site that copies it
    /// after a crash, its own heap then freed, as Erlang builds a message in the sender's heap and copies it into the receiver's at its
    /// send. So is a message whose clause's bound can raise with no call in it (<see cref="Raises"/>), which runs after the message is
    /// built: its raise crashes the actor whose code builds the message before its queueing, and that crash frees the heap of its own with
    /// the message and the copies of its arguments (the runtime's <c>Fall</c>), which the actor's heap would keep unqueued to the actor's
    /// end. Where no argument can wait, the message is built in the actor's heap, and nothing runs between the read of that heap and the
    /// queueing that could end the actor. Where the actor ended, the message is built or copied in the heap the caller allocates in (the
    /// runtime's <c>ActorHeap</c>), and its queueing raises <c>actor-gone</c> at the call's line, a <c>send</c>'s too (the runtime's
    /// <c>Enqueue</c>; U6, U20; section 6, the owner, 2026-11-16, #519, comments 6919351399 and 6923513534).
    /// </summary>
    private string EmitMessage(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var actor = c.Callee switch
        {
            _ when info.ByName => PerReference(((ActorType)info.Target!).Symbol), // U51, U52: `Cache.Get(1)`, its one actor found or started
            MemberAccess m when Sub(Model.TypeOf(m.Target).Type) is ActorType => EmitExpression(m.Target),
            MemberAccess m => Reference(EmitExpression(m.Target)), // U11: `send this.Add(1)`, `this` the actor's own instance
            NameExpression n => Reference(This(n, n.Line)),
            _ => throw new InvalidOperationException($"a message through `{c.Callee.GetType().Name}` at line {c.Line}"),
        };
        var own = c.Arguments.Select(a => a.Value).Concat(c.Clause()).Any(Model.CanWait) || c.Clause().Any(Raises) ? fn.Value($"call ptr {RuntimeFunction("MessageHeapNew")}()") : null;
        var left = own is null ? null : HeapBegun(own, "MessageHeapFree");
        var heap = own ?? fn.Value($"call ptr {RuntimeFunction("ActorHeap")}({ReferenceWords(actor)})");
        var arguments = Arguments(c, info, j => f.Parameters[j].Type, _ => false, j => Default(f.Declaration!.Parameters[j].Default, owners[f]), cConvention: false, owned: true, into: heap);
        var message = MessageType(f);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(message)})");
        var run = MessageRun(f);
        var copy = MessageCopy(f, CopiesInto(c, info, f.Parameters.Select(p => p.Type).ToList()));
        var table = $"@\"message.table.{Number(++actorSites)}\"";
        module.Global($"{table} = private unnamed_addr constant [3 x ptr] [ptr {run}, ptr {copy}, ptr {MessageDrop(f)}]");
        fn.Emit($"store ptr {table}, ptr {MessageField(message, block, 1)}");
        for (var i = 0; i < arguments.Count; i++)
        {
            fn.Emit($"store {arguments[i]}, ptr {MessageField(message, block, MessageArguments + i)}");
        }

        // a message built in a heap of its own: copied into the actor's heap of the moment it is queued, its own heap freed
        string Queued()
        {
            if (own is null)
            {
                return block;
            }

            var moved = fn.Value($"call ptr {copy}(ptr {block}, ptr {fn.Value($"call ptr {RuntimeFunction("ActorHeap")}({ReferenceWords(actor)})")})");
            fn.Emit($"call void {RuntimeFunction("MessageHeapFree")}(ptr {own})");
            fn.Emit($"store ptr null, ptr {left}");
            return moved;
        }

        if (info.Kind == CallKind.ActorSend)
        {
            fn.Emit($"store ptr null, ptr {MessageField(message, block, 2)}");
            var sent = Queued();
            fn.Emit($"call void {RuntimeFunction("Enqueue")}({ReferenceWords(actor)}, ptr {sent}, i1 false, i64 0, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
            return "void";
        }

        // U39: the call's bound, that of its clause, run after the arguments, at every call it stands on, under a `within` block whose
        // deadline wins over it too (assumed: a reading of the build), else the literal of the method's declaration, the call's clause
        // winning over the declaration's, which the runtime's `Enqueue` takes where no `within` block runs (assumed in section 6); the
        // callee's reference and its heap, read before the arguments, stay the callee's while the bound runs, since a bound that
        // changes the variable the callee was read from, `s.Get() within Swap(mut s)`, is `changed-while-held` (M7), as an argument is
        var bound = c.Within is { } clause ? EmitExpression(clause) : f.Declaration!.Within is { } declared ? EmitExpression(declared) : null;
        var result = f.ReturnType;
        var reply = fn.Alloca(Reply(result), "reply");
        fn.Emit($"store i32 0, ptr {reply}");
        if (!Types.IsVoid(result))
        {
            fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapCurrent")}()")}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 {Number(ReplyHeap)}")}");
        }

        fn.Emit($"call void {RuntimeFunction("ReplyWaiter")}(ptr {reply})"); // the waiter before the message is queued
        fn.Emit($"store ptr {reply}, ptr {MessageField(message, block, 2)}");
        block = Queued();
        fn.Emit($"call void {RuntimeFunction("Enqueue")}({ReferenceWords(actor)}, ptr {block}, i1 {(bound is null ? "false" : "true")}, i64 {bound ?? "0"}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        fn.Emit($"call void {RuntimeFunction("Wait")}(ptr {reply}, ptr {block}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        return Types.IsVoid(result) ? "void" : fn.Value($"load {Ir(result)}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 {Number(ReplyHeap + 1)}")}");
    }

    /// <summary>
    /// U4, U6: the function of the messages of the method <paramref name="f"/>, which the runtime calls with the actor's state and the
    /// message, made at its first use and named outside the program's functions, so that a trace leaves its frame out as it leaves the
    /// runtime's: the method called on the state with the arguments the message holds, which it lets go of after the call, before it
    /// takes the reply, so that a crash in that release is answered <c>callee crashed</c> as a crash in the method is (U20); where the
    /// message has a reply, which it takes off the message by the runtime's atomic swap, <c>ReplyTaken</c>, after the method returned, the
    /// result moved beside it, a copy of its own in the caller's heap where it holds a block, the method's let go
    /// of once the reply is set (U12; comment 6902210646, Q5), and the runtime's <c>Replied</c>, which sets its word and wakes the activation that
    /// waits on it, as tokio's <c>oneshot::Sender::send</c> stores the value and then wakes the receiver, so that between the swap and
    /// <c>Replied</c> stands only the copy, whose fault ends the process (O11), and no code whose fault is the actor's crash; then the
    /// message's block freed.
    /// A call whose caller crashed while it ran has no reply any more, the runtime having taken it by the same swap at the crash, and its
    /// result is let go of (U20).
    /// </summary>
    private string MessageRun(FunctionSymbol f) => Helper($"message.{f.Owner!.Name}.{f.Name}.{Number(f.Line)}", "void", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var arguments = f.Parameters.Select((p, i) => (p.Type, Value: fn.Value($"load {Ir(p.Type)}, ptr {MessageField(message, "%a1", MessageArguments + i)}"))).ToList();
        var result = CallOn(f, "%a0", new ClassType(f.Owner!), arguments.Select(a => $"{Ir(a.Type)} {a.Value}"));

        // U20: the arguments let go of before the reply is taken, so that a crash in their release, an overflow of the actor's stack
        // among it, finds the reply still on the message and answers it `callee crashed`
        foreach (var (type, value) in arguments.Where((a, i) => Counted(a.Type) && !f.Parameters[i].Move)) // U12: the method owns a parameter written `move`
        {
            Release(value, type);
        }

        var reply = fn.Value($"call ptr {RuntimeFunction("ReplyTaken")}(ptr %a1)"); // the swap that makes this function its one owner
        if (!Types.IsVoid(f.ReturnType))
        {
            // U20: the reply of a call whose caller crashed is unhooked, and the result goes nowhere
            var answered = fn.Label("answered");
            var dropped = fn.Label("dropped");
            var joined = fn.Label("joined");
            fn.CondBranch(fn.Value($"icmp ne ptr {reply}, null"), answered, dropped);
            fn.Block(dropped);
            if (Counted(f.ReturnType))
            {
                Release(result, f.ReturnType);
            }

            fn.Branch(joined);
            fn.Block(answered);
            var answer = result;
            if (Counted(f.ReturnType))
            {
                // U12: a result that holds a block crosses back into the caller's heap as a copy of its own, and the method's lets go;
                // no function value reaches here, since a call that waits on a method whose result does not cross with no word, a
                // function type among them, is `send-needs-word` at the call, and a `send` of a method with a result is `send-result`
                // (U6; `Checker.Actors.cs`, the stage of the result)
                var heap = fn.Value($"load ptr, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 {Number(ReplyHeap)}")}");
                answer = Crossed(result, f.ReturnType, heap); // in a session of its own, not the call's, whose fault ends the process (O11)
            }

            // a method with a result takes no `send` (U6, U7)
            fn.Emit($"store {Ir(f.ReturnType)} {answer}, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 {Number(ReplyHeap + 1)}")}");
            fn.Emit($"call void {RuntimeFunction("Replied")}(ptr {reply})");
            if (Counted(f.ReturnType))
            {
                Release(result, f.ReturnType); // the method's, let go of once the reply is set, so that nothing between the swap and `Replied` can crash the actor
            }

            fn.Branch(joined);
            fn.Block(joined);
        }

        if (Types.IsVoid(f.ReturnType))
        {
            // a void method takes a `send` and a call that waits alike, and a `send` has no reply (U6)
            var waits = fn.Label("waits");
            var sent = fn.Label("sent");
            fn.CondBranch(fn.Value($"icmp ne ptr {reply}, null"), waits, sent);
            fn.Block(waits);
            fn.Emit($"call void {RuntimeFunction("Replied")}(ptr {reply})");
            fn.Block(sent);
        }

        fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 {IrSize(message)})");
        fn.Return(null);
    });

    /// <summary>
    /// U21: the function that copies a message of the method <paramref name="f"/>, made at its call site, into the heap of the actor's new
    /// instance after a crash, which the runtime calls with the message and that heap, and into the actor's heap at the queueing of a
    /// message built in a heap of its own (<see cref="EmitMessage"/>): a block of that heap with the message's table,
    /// reply, chain ID and deadlines, and a copy of each argument, every counted block it reaches copied there in one session (U12, Q2), a function
    /// value by the environment of its lambda (<paramref name="copiesInto"/>); the message's own block goes with the crashed heap, or with
    /// the message's own heap.
    /// </summary>
    private string MessageCopy(FunctionSymbol f, List<string?> copiesInto) => Helper($"message.copy.{f.Owner!.Name}.{f.Name}.{Number(++actorSites)}", "ptr", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var moved = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr %a1, i64 {IrSize(message)})");
        fn.Emit($"store ptr null, ptr {MessageField(message, moved, 0)}");
        foreach (var (index, type) in new[] { (1, "ptr"), (2, "ptr"), (3, "i64"), (4, "i64"), (5, "i64"), (6, "i64") })
        {
            fn.Emit($"store {type} {fn.Value($"load {type}, ptr {MessageField(message, "%a0", index)}")}, ptr {MessageField(message, moved, index)}");
        }

        var session = CallSession(f.Parameters.Count(p => Counted(p.Type)), "%a1", noExit: true);
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var type = f.Parameters[i].Type;
            var value = fn.Value($"load {Ir(type)}, ptr {MessageField(message, "%a0", MessageArguments + i)}");
            if (Counted(type))
            {
                CrossInto(MessageField(message, moved, MessageArguments + i), value, type, session, "%a1", copiesInto[i]); // U12: into the place the method reads
                continue;
            }

            fn.Emit($"store {Ir(type)} {value}, ptr {MessageField(message, moved, MessageArguments + i)}");
        }

        fn.Return($"ptr {moved}");
    });

    /// <summary>
    /// The IR type of a message of the method: the message after it in its list, the table of its call site (run, copy, drop), the reply,
    /// the chain ID as two words, the deadline and the deadline of the <c>within</c> block that runs while it runs (U37, U38), then the
    /// arguments, from the field <see cref="MessageArguments"/> on.
    /// </summary>
    private string MessageType(FunctionSymbol f) => $"{{ ptr, ptr, ptr, i64, i64, i64, i64{string.Concat(f.Parameters.Select(p => ", " + Ir(p.Type)))} }}";

    /// <summary>The field of a message's first argument (<see cref="MessageType"/>), after the runtime's seven words.</summary>
    private const int MessageArguments = 7;

    /// <summary>
    /// The IR type of the reply of a call that waits, as tokio's <c>oneshot</c> holds its state, its receiver's task and its value: the
    /// word set once the result stands, an <c>i32</c> that the runtime stores with release and loads with acquire, the activation that
    /// waits on it, which the runtime's <c>ReplyWaiter</c> sets, the call's deadline and the replies before and after it among the
    /// thread's timers, which the runtime's <c>Enqueue</c> and <c>Wait</c> set (U36), the clock at which a crash or an end set the word,
    /// which the runtime's <c>Signal</c> stores and <c>Wait</c> holds to the deadline (U43), then, for a method with a result, the heap of
    /// the caller, which the result is copied into (U12), the field <see cref="ReplyHeap"/>, and the result after it.
    /// </summary>
    private string Reply(KurzType result) => Types.IsVoid(result) ? "{ i32, ptr, i64, ptr, ptr, i64 }" : $"{{ i32, ptr, i64, ptr, ptr, i64, ptr, {Ir(result)} }}";

    /// <summary>The field of the caller's heap in a reply (<see cref="Reply"/>), the result's the one after it.</summary>
    private const int ReplyHeap = 6;

    /// <summary>
    /// U37: the function that drops a message of the method <paramref name="f"/> unrun, the third of its call site's table, which the
    /// runtime calls with the message where its deadline passed at its queueing or before its callee took it (the runtime's
    /// <c>Discard</c>): every argument that holds a count let go of, a parameter written <c>move</c> among them, whose method never took
    /// it, and the message's block freed, as <see cref="MessageRun"/> frees it.
    /// </summary>
    private string MessageDrop(FunctionSymbol f) => Helper($"message.drop.{f.Owner!.Name}.{f.Name}.{Number(f.Line)}", "void", ["ptr"], () =>
    {
        var message = MessageType(f);
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var type = f.Parameters[i].Type;
            if (Counted(type))
            {
                Release(fn.Value($"load {Ir(type)}, ptr {MessageField(message, "%a0", MessageArguments + i)}"), type);
            }
        }

        fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {IrSize(message)})");
        fn.Return(null);
    });

    private string MessageField(string message, string block, int index) => fn.Value($"getelementptr {message}, ptr {block}, i32 0, i32 {Number(index)}");

    /// <summary>The size of a value of the IR type, in bytes.</summary>
    private string IrSize(string irType) => fn.Value($"ptrtoint ptr {fn.Value($"getelementptr {irType}, ptr null, i32 1")} to i64");

    // ---- the block `attempt { ... }` (U46 to U50) ------------------------------------------------------------------------------

    /// <summary>The global of the name the child of every <c>attempt</c> carries for the log line of its crash, declared once (U50).</summary>
    private string? attemptName;

    /// <summary>
    /// U46: <c>attempt { ... }</c> where it stands as a value or a statement: its whole value (<see cref="EmitAttemptValue"/>), whose
    /// success stands where it stands and whose <c>Crashed</c> leaves the function, but where a written union type keeps its cases (O2, O3).
    /// </summary>
    private string? EmitAttempt(AttemptExpression a)
    {
        var union = (Semantics.UnionType)Sub(Model.AttemptOf(a).Type);
        var value = EmitAttemptValue(a);
        return Types.Same(TypeOf(a), union) ? value : Propagate(value, union, a.Line);
    }

    /// <summary>
    /// U46 to U50: the whole value of <c>attempt { ... }</c>, a value of its own, as Erlang's <c>spawn_monitor</c> starts a process and
    /// waits for its result or its <c>'DOWN'</c> reason (not checked at source): the fields of <c>this</c> that the block names read into the variables it takes
    /// them through, at its start; the block's function value made as a lambda's is (F15); the child made (the runtime's
    /// <c>AttemptNew</c>), whose supervisor is the code that runs; one message in the child's heap that holds a copy of that function
    /// value, made as a message's argument crosses (U12), and whose function calls it (<see cref="AttemptRun"/>); a reply in the frame as
    /// a call's, the heap its value crosses back into and the slot of the crash's message beside it (<see cref="AttemptReply"/>); the
    /// message queued with the chain's deadline and no bound of its own (the runtime's <c>Enqueue</c>), and the wait for it
    /// (<c>Attempted</c>; U49): the block's value as the union's success where the reply holds it, else <c>Crashed</c>, built from the
    /// crash's message (<see cref="CrashedOf"/>), which is then freed (<c>PendingLeft</c>).
    /// </summary>
    private string EmitAttemptValue(AttemptExpression a)
    {
        var attempt = Model.AttemptOf(a);
        var union = (Semantics.UnionType)Sub(attempt.Type);
        var success = union.Cases[0];
        var crashed = (DataType)union.Cases[^1];
        if (attempt.This is { } self)
        {
            var symbol = ((ClassType)Sub(self.Type)).Symbol;
            var instance = fn.Value($"load {Ir(Sub(self.Type))}, ptr {Place(self, a.Line)}");
            foreach (var (variable, field) in attempt.Fields)
            {
                var type = Sub(variable.Type);
                var slot = fn.Alloca(Ir(type), "taken");
                fn.Emit($"store {Ir(type)} {InstanceField(instance, symbol, symbol.FieldIndex(field.Name))}, ptr {slot}"); // U47: by value, a temporary of the region
                slots[variable] = slot;
            }
        }

        var info = Model.LambdaOf(attempt.Lambda);
        var block = (Semantics.FunctionType)Sub(info.Type);
        var made = EmitLambda(attempt.Lambda, block);
        if (attemptName is null)
        {
            attemptName = "@\"attempt.name\"";
            module.Global($"{attemptName} = private unnamed_addr constant %kz.str {StringValue("attempt")}");
        }

        var child = fn.Value($"call ptr {RuntimeFunction("AttemptNew")}(ptr {attemptName})");
        var reference = Reference(child);
        var heap = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {child}, i64 -64")}"); // the child's heap, its record's word at -64
        var message = AttemptMessage();
        var queued = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(message)})");
        var copyInto = CopyEnvironmentInto(info.Captures.Select(v => Sub(v.Type)).ToList());
        var table = $"@\"attempt.table.{Number(++actorSites)}\"";
        module.Global($"{table} = private unnamed_addr constant [3 x ptr] [ptr {AttemptRun(block)}, ptr {AttemptCopy(block, copyInto)}, ptr {AttemptDrop(block)}]");
        fn.Emit($"store ptr {table}, ptr {MessageField(message, queued, 1)}");
        fn.Emit($"store %kz.fn {Crossed(made, block, heap, copyInto)}, ptr {MessageField(message, queued, MessageArguments)}");
        var replyType = AttemptReply(success);
        var reply = fn.Alloca(replyType, "reply");
        fn.Emit($"store i32 0, ptr {reply}");
        fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapCurrent")}()")}, ptr {fn.Value($"getelementptr {replyType}, ptr {reply}, i32 0, i32 {Number(ReplyHeap)}")}");
        fn.Emit($"store ptr null, ptr {fn.Value($"getelementptr {replyType}, ptr {reply}, i32 0, i32 {Number(ReplyHeap + 1)}")}");
        fn.Emit($"call void {RuntimeFunction("ReplyWaiter")}(ptr {reply})"); // the waiter before the message is queued
        fn.Emit($"store ptr {reply}, ptr {MessageField(message, queued, 2)}");
        fn.Emit($"call void {RuntimeFunction("Enqueue")}({ReferenceWords(reference)}, ptr {queued}, i1 true, i64 9223372036854775807, %kz.str {StringValue(unit.File)}, i32 {Number(a.Line)})");
        var crash = fn.Value($"call ptr {RuntimeFunction("Attempted")}(ptr {reply}, ptr {queued}, %kz.str {StringValue(unit.File)}, i32 {Number(a.Line)})");
        var result = fn.Alloca(Ir(union), "attempt");
        var held = fn.Label("attempt.value");
        var fell = fn.Label("attempt.crashed");
        var done = fn.Label("attempt.done");
        fn.CondBranch(fn.Value($"icmp eq ptr {crash}, null"), held, fell);
        fn.Block(held);
        var value = success is Semantics.VoidType ? null : fn.Value($"load {Ir(success)}, ptr {fn.Value($"getelementptr {replyType}, ptr {reply}, i32 0, i32 {Number(ReplyHeap + 2)}")}");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, success, value)}, ptr {result}");
        fn.Branch(done);
        fn.Block(fell);
        var c = CrashedOf(crashed, crash);
        fn.Emit($"call void {RuntimeFunction("PendingLeft")}(ptr {crash})");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, crashed, c)}, ptr {result}");
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {result}");
    }

    /// <summary>
    /// U46: the IR type of the message of an <c>attempt</c>, a message's seven words (<see cref="MessageType"/>) and the block's function
    /// value, the one argument, at <see cref="MessageArguments"/>.
    /// </summary>
    private static string AttemptMessage() => "{ ptr, ptr, ptr, i64, i64, i64, i64, %kz.fn }";

    /// <summary>
    /// U46, U50: the IR type of the reply of an <c>attempt</c>, a call's (<see cref="Reply"/>) with the caller's heap at
    /// <see cref="ReplyHeap"/> whatever the block gives, then the slot of the crash's message, at byte 56, where the runtime's
    /// <c>Fell</c> stores it, then the block's value, where it gives one.
    /// </summary>
    private string AttemptReply(KurzType success) => success is Semantics.VoidType
        ? "{ i32, ptr, i64, ptr, ptr, i64, ptr, ptr }"
        : $"{{ i32, ptr, i64, ptr, ptr, i64, ptr, ptr, {Ir(success)} }}";

    /// <summary>
    /// U46, U47: the function of the message of an <c>attempt</c> whose block has the function type <paramref name="block"/>, which the
    /// runtime calls in the child with its state and the message: the block's function value called, then, as a method's message
    /// (<see cref="MessageRun"/>), the reply taken off the message by the runtime's <c>ReplyTaken</c>, the value moved beside it, a copy of
    /// its own in the heap of the code that waits where it holds a block (U12), and the reply set (<c>Replied</c>); a value whose waiter
    /// crashed meanwhile is let go of; then the function value and the message's block freed.
    /// </summary>
    private string AttemptRun(Semantics.FunctionType block) => Helper($"attempt.run.{Lower.Mangle(block.Result)}", "void", ["ptr", "ptr"], () =>
    {
        var message = AttemptMessage();
        var result = block.Result;
        var made = fn.Value($"load %kz.fn, ptr {MessageField(message, "%a1", MessageArguments)}");
        var call = $"call {Ir(result)} {fn.Value($"extractvalue %kz.fn {made}, 0")}(ptr {fn.Value($"extractvalue %kz.fn {made}, 1")})";
        var value = Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
        var reply = fn.Value($"call ptr {RuntimeFunction("ReplyTaken")}(ptr %a1)");
        var answered = fn.Label("answered");
        var dropped = fn.Label("dropped");
        var joined = fn.Label("joined");
        fn.CondBranch(fn.Value($"icmp ne ptr {reply}, null"), answered, dropped);
        fn.Block(dropped);
        if (!Types.IsVoid(result) && Counted(result))
        {
            Release(value, result);
        }

        fn.Branch(joined);
        fn.Block(answered);
        if (!Types.IsVoid(result))
        {
            var replyType = AttemptReply(result);
            if (Counted(result))
            {
                var crossed = Crossed(value, result, fn.Value($"load ptr, ptr {fn.Value($"getelementptr {replyType}, ptr {reply}, i32 0, i32 {Number(ReplyHeap)}")}"));
                Release(value, result);
                value = crossed;
            }

            fn.Emit($"store {Ir(result)} {value}, ptr {fn.Value($"getelementptr {replyType}, ptr {reply}, i32 0, i32 {Number(ReplyHeap + 2)}")}");
        }

        fn.Emit($"call void {RuntimeFunction("Replied")}(ptr {reply})");
        fn.Branch(joined);
        fn.Block(joined);
        Release(made, block);
        fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 {IrSize(message)})");
        fn.Return(null);
    });

    /// <summary>
    /// U21, U46: the function that copies the message of an <c>attempt</c> into a heap, the second of its table, as a method's message is
    /// copied (<see cref="MessageCopy"/>): its words, and the block's function value by the environment of its lambda,
    /// <paramref name="copyInto"/>.
    /// </summary>
    private string AttemptCopy(Semantics.FunctionType block, string copyInto) => Helper($"attempt.copy.{Number(++actorSites)}", "ptr", ["ptr", "ptr"], () =>
    {
        var message = AttemptMessage();
        var moved = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr %a1, i64 {IrSize(message)})");
        fn.Emit($"store ptr null, ptr {MessageField(message, moved, 0)}");
        foreach (var (index, type) in new[] { (1, "ptr"), (2, "ptr"), (3, "i64"), (4, "i64"), (5, "i64"), (6, "i64") })
        {
            fn.Emit($"store {type} {fn.Value($"load {type}, ptr {MessageField(message, "%a0", index)}")}, ptr {MessageField(message, moved, index)}");
        }

        var made = fn.Value($"load %kz.fn, ptr {MessageField(message, "%a0", MessageArguments)}");
        CrossInto(MessageField(message, moved, MessageArguments), made, block, null, "%a1", copyInto);
        fn.Return($"ptr {moved}");
    });

    /// <summary>
    /// U37, U46: the function that drops the message of an <c>attempt</c> unrun, the third of its table, which the runtime calls where its
    /// deadline passed at its queueing or before the child took it: the block's function value let go of and the message's block freed.
    /// </summary>
    private string AttemptDrop(Semantics.FunctionType block) => Helper($"attempt.drop.{Lower.Mangle(block)}", "void", ["ptr"], () =>
    {
        var message = AttemptMessage();
        Release(fn.Value($"load %kz.fn, ptr {MessageField(message, "%a0", MessageArguments)}"), block);
        fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {IrSize(message)})");
        fn.Return(null);
    });
}
