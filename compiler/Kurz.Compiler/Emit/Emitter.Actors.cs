using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 19 on the one thread the runtime runs, as tokio's <c>current_thread</c> runtime runs its tasks (Q1 (a), the answers of
/// 2026-11-14, issue 519, comment 6880404917; <c>compiler/README.md</c> lists what of section 6 this does not build), each message on a
/// stack of its own, where a call that waits parks it until its reply is set, so that no function is rewritten (section 6, "Own
/// stack"). An actor's state is an instance of its body, laid out and constructed as a class's
/// (<see cref="Construct"/>), in the first block of the actor's own heap, which holds the actor's record in front of it, as a string's
/// block stands in front of its bytes (the runtime's <c>ActorNew</c>; section 3): the reference is the address of the state, so that the
/// actor's own methods reach the record through <c>this</c> (U11). Nothing counts an actor, which lives to the end of the process, where
/// its heap is freed whole (U13). A call through a reference, or after <c>send</c>, is a message, one block of the heap of the actor it
/// goes to, as actix packs a message and the sender of its reply into one envelope: the
/// message after it in its list, which the runtime's <c>Enqueue</c> sets, the table of its call site, the function that runs it and the
/// one that copies it into the heap of an actor's new instance (U21), the reply its caller waits on, null
/// after <c>send</c>, as a message of a tokio actor carries a <c>oneshot</c> sender or none, the chain ID, 16 bytes, which
/// <c>Enqueue</c> stamps (section 6, "Deadlocks", layer 1), then the arguments, each holding a count of its own
/// (<see cref="EmitMessage"/>). The function of a method's messages calls the method on the state, as actix's
/// <c>EnvelopeProxy::handle</c> calls the handler on the actor (<see cref="MessageRun"/>).
/// </summary>
public sealed partial class Emitter
{
    // the slots of the heaps of the `spawn`s, and of the messages built in a heap of their own, begun in each region, null once the actor
    // took its heap or the message was queued, which the region's end, or a jump out of it, frees where a case propagated out of an argument
    // after it left one (EndHeaps)
    private readonly Dictionary<List<(string Value, KurzType Type)>, List<(string Slot, string Free)>> regionHeaps = new(ReferenceEqualityComparer.Instance);

    // the global of each actor type's name, for the log line of a crash (U24), declared once for its type
    private readonly Dictionary<ClassSymbol, string> actorNames = [];

    // the count of the call sites and `spawn`s whose table or restart a function of its own serves, which names each one
    private int actorSites;

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
    /// restart with a copy of each argument the constructor is given (<see cref="KeptArguments"/>; U22), and while the constructor runs,
    /// the runtime names the actor as the one whose constructor runs, so that an actor it spawns is its child (the runtime's
    /// <c>Building</c>; U13).
    /// </summary>
    private string EmitActorSpawn(SpawnExpression s)
    {
        var info = Model.CallOf(s.Call);
        var symbol = ((ClassType)info.Target!).Symbol;
        var block = layout.ClassBlock(symbol);
        var stack = symbol.Declaration.Clauses?.FirstOrDefault(c => c.Size is not null)?.Size is { } size
            ? ((IntegerConstant)Model.TypeOf(size).Constant!).Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "2097152";
        var supervisor = fn.Value($"call ptr {StdFunction("HeapCurrent")}()");
        var heap = fn.Value($"call ptr {RuntimeFunction("ActorHeapNew")}(i64 {IrSize(block)})");
        var left = HeapBegun(heap, "HeapLeft");
        var spawner = fn.Alloca("ptr", "spawner");
        var building = fn.Alloca("ptr", "building");
        var side = "null";
        var state = Construct(symbol, s.Call, info, () =>
        {
            var state = fn.Value($"call ptr {RuntimeFunction("ActorNew")}(ptr {heap}, i64 {IrSize(block)}, i64 {stack}, ptr {side}, ptr {ActorName(symbol)})");
            fn.Emit($"store ptr null, ptr {left}");
            fn.Emit($"store i64 1, ptr {state}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {state}, i32 0, i32 1")}");
            fn.Emit($"store ptr {fn.Value($"call ptr {RuntimeFunction("Building")}(ptr {state})")}, ptr {building}");
            fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapUse")}(ptr {heap})")}, ptr {spawner}");
            return state;
        }, into: heap, kept: (arguments, types) => side = KeptArguments(s.Call, info, symbol, arguments, types, supervisor));
        fn.Emit($"call ptr {RuntimeFunction("Building")}(ptr {fn.Value($"load ptr, ptr {building}")})");
        fn.Emit($"call ptr {StdFunction("HeapUse")}(ptr {fn.Value($"load ptr, ptr {spawner}")})");
        return state;
    }

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
    /// and the next actor of the queue of the pauses, each the runtime's (<c>Pause</c>), and a copy of each argument the constructor is
    /// given, its default among them, every counted block it reaches copied into that heap, a <c>move</c> argument's too (the taken
    /// points of comment 6899225864).
    /// </summary>
    private string KeptArguments(CallExpression c, CallInfo info, ClassSymbol symbol, List<string> arguments, List<KurzType> types, string heap)
    {
        var copiesInto = CopiesInto(c, info, types);
        var side = SideType(types);
        var restart = Restarted(symbol, info, types, copiesInto);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(side)})");
        fn.Emit($"store {side} {{ ptr {restart}, ptr null, i64 0, i64 0, i64 0, ptr null, {Kept(types)} zeroinitializer }}, ptr {block}");
        var session = CallSession(types.Count(Counted), heap);
        for (var i = 0; i < types.Count; i++)
        {
            var source = fn.Alloca(Ir(types[i]), "kept");
            fn.Emit($"store {arguments[i]}, ptr {source}");
            var value = fn.Value($"load {Ir(types[i])}, ptr {source}");
            if (Counted(types[i]))
            {
                value = Crossed(value, types[i], session, heap, copiesInto[i]);
            }

            fn.Emit($"store {Ir(types[i])} {value}, ptr {fn.Value($"getelementptr {side}, ptr {block}, i32 0, i32 6, i32 {Number(i)}")}");
        }

        return block;
    }

    /// <summary>
    /// U3, U22: the function a restart of the actor of the type <paramref name="symbol"/> runs, with the state and the restart's message
    /// (the runtime's <c>Restart</c>), made for its <c>spawn</c>: a copy of each kept argument in the actor's heap, the one the allocation
    /// entry serves, the state's counts at one, and the constructor run on them as at the <c>spawn</c>, a constructor of the body
    /// borrowing them, the primary one owning them, then the defaults of the body (K7); then the message freed. A throw there is the
    /// actor's own crash (U3).
    /// </summary>
    private string Restarted(ClassSymbol symbol, CallInfo info, List<KurzType> types, List<string?> copiesInto) =>
        Helper($"actor.restart.{symbol.Name}.{Number(++actorSites)}", "void", ["ptr", "ptr"], () =>
        {
            var side = SideType(types);
            var table = fn.Value($"load ptr, ptr {fn.Value("getelementptr { ptr, ptr }, ptr %a1, i32 0, i32 1")}");
            var heap = fn.Value($"call ptr {StdFunction("HeapCurrent")}()");
            var session = CallSession(types.Count(Counted), heap);
            var values = new List<string>();
            for (var i = 0; i < types.Count; i++)
            {
                var value = fn.Value($"load {Ir(types[i])}, ptr {fn.Value($"getelementptr {side}, ptr {table}, i32 0, i32 6, i32 {Number(i)}")}");
                values.Add(Counted(types[i]) ? Crossed(value, types[i], session, heap, copiesInto[i]) : value);
            }

            var block = layout.ClassBlock(symbol);
            fn.Emit("store i64 1, ptr %a0");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1")}");
            if (info.Constructor is { } made)
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

            fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 40)");
            fn.Return(null);
        });

    /// <summary>The IR type of the block of a restart whose constructor takes values of the types <paramref name="types"/> (<see cref="KeptArguments"/>).</summary>
    private string SideType(List<KurzType> types) => $"{{ ptr, ptr, i64, i64, i64, ptr, {Kept(types)} }}";

    private string Kept(List<KurzType> types) => $"{{ {string.Join(", ", types.Select(Ir))} }}";

    /// <summary>
    /// U12, Q5: for each slot of the call whose type is in <paramref name="types"/>, the function that copies its function value, by the
    /// environment of the lambda the checker saw for the argument bound to it (<see cref="CopyEnvironmentInto"/>), and null for a slot of
    /// any other type.
    /// </summary>
    private List<string?> CopiesInto(CallExpression c, CallInfo info, List<KurzType> types) => types.Select((t, j) =>
    {
        if ((Types.NonNull(t) ?? t) is not Semantics.FunctionType)
        {
            return null;
        }

        var bound = Enumerable.Range(0, info.Binding.Count).Where(i => info.Binding[i] == j).Select(i => c.Arguments[i].Value).FirstOrDefault();
        var lambda = bound is null ? null : Model.CrossingLambda(bound);
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
    /// U4, U6, U11: a call of an actor's method as a message: the actor, the address of its state, read first, through its reference
    /// or, inside it, as <c>this</c> (U11), then the arguments, each a copy of its own in the actor's heap where it holds a block (U12,
    /// <see cref="Crossed"/>; comment 6902210646, Q5), stored into a block of that heap that the message's function frees once it ran; the block goes to the end of the actor's inbox (U5, U15), or of its side list, where it is a
    /// call of the chain the actor waits in (U17). A call that waits (U4), wherever it stands, passes a reply in its own frame, the one-shot
    /// slot of section 6, a byte the message's function sets once the result stands beside it, the activation that waits on it and the
    /// heap the caller allocates in, which the result is copied into, and
    /// the runtime's <c>Wait</c>, given the call's place for its <c>deadlock</c>, runs messages until it is set in the top-level code and
    /// parks the running message's activation on its stack inside a message, which no other code reaches while it is parked but the calls
    /// of its chain, its actor's own methods; the result is then the caller's, a temporary of its region as any call's result is. A
    /// <c>send</c> passes none (U6). The message names the table of its call site, a constant: the function that runs it and the one
    /// that copies it into the heap of the actor's new instance after a crash (<see cref="MessageCopy"/>; U21); <c>Wait</c> is given the
    /// message, so that a crash of the waiting actor unhooks the reply from it (U20). Where an argument that can wait, one that runs a call
    /// anywhere in it (<see cref="SemanticModel.CanWait"/>), follows an argument that crosses, the wait lets the actor crash, which frees
    /// the heap the arguments crossed into (U21), or end (U13): the message is then built in a heap of its own, made for it (the runtime's
    /// <c>MessageHeapNew</c>), and at its queueing copied into the heap the actor has then by the function of its call site that copies it
    /// after a crash, its own heap then freed, as Erlang builds a message in the sender's heap and copies it into the receiver's at its
    /// send; where the actor ended meanwhile, the copy goes into its first heap and the message is dropped as any message to an ended
    /// actor (the runtime's <c>Enqueue</c>). Where no argument after one that crosses can wait, the message is built in the actor's heap.
    /// </summary>
    private string EmitMessage(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var actor = c.Callee switch
        {
            MemberAccess m => EmitExpression(m.Target),
            NameExpression n => This(n, n.Line),
            _ => throw new InvalidOperationException($"a message through `{c.Callee.GetType().Name}` at line {c.Line}"),
        };
        var crossing = Enumerable.Range(0, c.Arguments.Count).FirstOrDefault(i => info.Binding[i] >= 0 && Counted(Sub(f.Parameters[info.Binding[i]].Type)), -1);
        var own = crossing >= 0 && c.Arguments.Skip(crossing + 1).Any(a => Model.CanWait(a.Value)) ? fn.Value($"call ptr {RuntimeFunction("MessageHeapNew")}()") : null;
        var left = own is null ? null : HeapBegun(own, "MessageHeapFree");
        var heap = own ?? fn.Value($"call ptr {RuntimeFunction("ActorHeap")}(ptr {actor})");
        var arguments = Arguments(c, info, j => f.Parameters[j].Type, _ => false, j => Default(f.Declaration!.Parameters[j].Default, owners[f]), cConvention: false, owned: true, into: heap);
        var message = MessageType(f);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(message)})");
        var run = MessageRun(f);
        var copy = MessageCopy(f, CopiesInto(c, info, f.Parameters.Select(p => p.Type).ToList()));
        var table = $"@\"message.table.{Number(++actorSites)}\"";
        module.Global($"{table} = private unnamed_addr constant [2 x ptr] [ptr {run}, ptr {copy}]");
        fn.Emit($"store ptr {table}, ptr {MessageField(message, block, 1)}");
        for (var i = 0; i < arguments.Count; i++)
        {
            fn.Emit($"store {arguments[i]}, ptr {MessageField(message, block, 5 + i)}");
        }

        // a message built in a heap of its own: copied into the actor's heap of the moment it is queued, its own heap freed
        string Queued()
        {
            if (own is null)
            {
                return block;
            }

            var moved = fn.Value($"call ptr {copy}(ptr {block}, ptr {fn.Value($"call ptr {RuntimeFunction("ActorHeap")}(ptr {actor})")})");
            fn.Emit($"call void {RuntimeFunction("MessageHeapFree")}(ptr {own})");
            fn.Emit($"store ptr null, ptr {left}");
            return moved;
        }

        if (info.Kind == CallKind.ActorSend)
        {
            fn.Emit($"store ptr null, ptr {MessageField(message, block, 2)}");
            fn.Emit($"call void {RuntimeFunction("Enqueue")}(ptr {actor}, ptr {Queued()})");
            return "void";
        }

        var result = f.ReturnType;
        var reply = fn.Alloca(Reply(result), "reply");
        fn.Emit($"store i8 0, ptr {reply}");
        if (!Types.IsVoid(result))
        {
            fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapCurrent")}()")}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 2")}");
        }

        fn.Emit($"store ptr {reply}, ptr {MessageField(message, block, 2)}");
        block = Queued();
        fn.Emit($"call void {RuntimeFunction("Enqueue")}(ptr {actor}, ptr {block})");
        fn.Emit($"call void {RuntimeFunction("Wait")}(ptr {reply}, ptr {block}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        return Types.IsVoid(result) ? "void" : fn.Value($"load {Ir(result)}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 3")}");
    }

    /// <summary>
    /// U4, U6: the function of the messages of the method <paramref name="f"/>, which the runtime calls with the actor's state and the
    /// message, made at its first use and named outside the program's functions, so that a trace leaves its frame out as it leaves the
    /// runtime's: the method called on the state with the arguments the message holds, which it lets go of after the call; where the
    /// message has a reply, the result moved beside it, a copy of its own in the caller's heap where it holds a block, the method's let go
    /// of (U12; comment 6902210646, Q5), and the runtime's <c>Replied</c>, which sets its byte and wakes the activation that
    /// waits on it, as tokio's <c>oneshot::Sender::send</c> stores the value and then wakes the receiver; then the message's block freed.
    /// A call whose caller crashed while it ran has no reply any more, the runtime having unhooked it, and its result is let go of (U20).
    /// </summary>
    private string MessageRun(FunctionSymbol f) => Helper($"message.{f.Owner!.Name}.{f.Name}.{Number(f.Line)}", "void", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var arguments = f.Parameters.Select((p, i) => (p.Type, Value: fn.Value($"load {Ir(p.Type)}, ptr {MessageField(message, "%a1", 5 + i)}"))).ToList();
        var result = CallOn(f, "%a0", new ClassType(f.Owner!), arguments.Select(a => $"{Ir(a.Type)} {a.Value}"));
        var reply = fn.Value($"load ptr, ptr {MessageField(message, "%a1", 2)}");
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
            if (Counted(f.ReturnType))
            {
                // U12: a result that holds a block crosses back into the caller's heap as a copy of its own, and the method's lets go
                var heap = fn.Value($"load ptr, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 2")}");
                var crossed = Crossed(result, f.ReturnType, null, heap); // in a session of its own, not the call's
                Release(result, f.ReturnType);
                result = crossed;
            }

            // a method with a result takes no `send` (U6, U7)
            fn.Emit($"store {Ir(f.ReturnType)} {result}, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 3")}");
            fn.Emit($"call void {RuntimeFunction("Replied")}(ptr {reply})");
            fn.Branch(joined);
            fn.Block(joined);
        }

        foreach (var (type, value) in arguments.Where((a, i) => Counted(a.Type) && !f.Parameters[i].Move)) // U12: the method owns a parameter written `move`
        {
            Release(value, type);
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
    /// reply and chain ID, and a copy of each argument, every counted block it reaches copied there in one session (U12, Q2), a function
    /// value by the environment of its lambda (<paramref name="copiesInto"/>); the message's own block goes with the crashed heap, or with
    /// the message's own heap.
    /// </summary>
    private string MessageCopy(FunctionSymbol f, List<string?> copiesInto) => Helper($"message.copy.{f.Owner!.Name}.{f.Name}.{Number(++actorSites)}", "ptr", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var moved = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr %a1, i64 {IrSize(message)})");
        fn.Emit($"store ptr null, ptr {MessageField(message, moved, 0)}");
        foreach (var (index, type) in new[] { (1, "ptr"), (2, "ptr"), (3, "i64"), (4, "i64") })
        {
            fn.Emit($"store {type} {fn.Value($"load {type}, ptr {MessageField(message, "%a0", index)}")}, ptr {MessageField(message, moved, index)}");
        }

        var session = CallSession(f.Parameters.Count(p => Counted(p.Type)), "%a1");
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var type = f.Parameters[i].Type;
            var value = fn.Value($"load {Ir(type)}, ptr {MessageField(message, "%a0", 5 + i)}");
            if (Counted(type))
            {
                value = Crossed(value, type, session, "%a1", copiesInto[i]);
            }

            fn.Emit($"store {Ir(type)} {value}, ptr {MessageField(message, moved, 5 + i)}");
        }

        fn.Return($"ptr {moved}");
    });

    /// <summary>
    /// The IR type of a message of the method: the message after it in its list, the function that runs it, the reply, the chain ID as two
    /// words, then the arguments.
    /// </summary>
    private string MessageType(FunctionSymbol f) => $"{{ ptr, ptr, ptr, i64, i64{string.Concat(f.Parameters.Select(p => ", " + Ir(p.Type)))} }}";

    /// <summary>
    /// The IR type of the reply of a call that waits, as tokio's <c>oneshot</c> holds its state, its receiver's task and its value: the
    /// byte set once the result stands, the activation that waits on it, which the runtime's <c>Wait</c> sets, then, for a method with a
    /// result, the heap of the caller, which the result is copied into (U12), and the result.
    /// </summary>
    private string Reply(KurzType result) => Types.IsVoid(result) ? "{ i8, ptr }" : $"{{ i8, ptr, ptr, {Ir(result)} }}";

    private string MessageField(string message, string block, int index) => fn.Value($"getelementptr {message}, ptr {block}, i32 0, i32 {Number(index)}");

    /// <summary>The size of a value of the IR type, in bytes.</summary>
    private string IrSize(string irType) => fn.Value($"ptrtoint ptr {fn.Value($"getelementptr {irType}, ptr null, i32 1")} to i64");
}
