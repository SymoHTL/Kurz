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
/// message after it in its list, which the runtime's <c>Enqueue</c> sets, the function that runs it, the reply its caller waits on, null
/// after <c>send</c>, as a message of a tokio actor carries a <c>oneshot</c> sender or none, the chain ID, 16 bytes, which
/// <c>Enqueue</c> stamps (section 6, "Deadlocks", layer 1), then the arguments, each holding a count of its own
/// (<see cref="EmitMessage"/>). The function of a method's messages calls the method on the state, as actix's
/// <c>EnvelopeProxy::handle</c> calls the handler on the actor (<see cref="MessageRun"/>).
/// </summary>
public sealed partial class Emitter
{
    // the slots of the heaps of the `spawn`s begun in each region, null once the actor took its heap, which the region's end, or a jump
    // out of it, frees where a case propagated out of a later argument left one (EndHeaps)
    private readonly Dictionary<List<(string Value, KurzType Type)>, List<string>> regionHeaps = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// U2, U3: <c>spawn Name(args)</c>, the state constructed as an instance of a class is (K1, K7, D11) in the room the runtime's
    /// <c>ActorNew</c> gives behind the actor's record, both its counts at one, as <see cref="EmitClassNew"/> makes them, which the
    /// record holds to the end of the process, so that a count a lambda takes of <c>this</c> never lets go of the state; the arguments,
    /// the constructor and the defaults of the body run here, before <c>spawn</c> gives the reference, which on one thread is the new
    /// actor running its constructor while the spawner waits (U3). The record keeps the size of the actor's stack, the one its <c>stack</c>
    /// clause writes or 2 MiB, which the runtime rounds up to its page and raises to its floor (U1; section 6, "2 MiB + clause"). The
    /// actor's heap is made first, its first page sized to the record and the state (section 3), and the arguments cross into it as a
    /// message's do (U12, <see cref="Crossed"/>); the constructor and the defaults then allocate in it, as the actor's code does, and
    /// the spawner's heap is served again once the state stands.
    /// </summary>
    private string EmitActorSpawn(SpawnExpression s)
    {
        var info = Model.CallOf(s.Call);
        var symbol = ((ClassType)info.Target!).Symbol;
        var block = layout.ClassBlock(symbol);
        var stack = symbol.Declaration.Clauses?.FirstOrDefault(c => c.Size is not null)?.Size is { } size
            ? ((IntegerConstant)Model.TypeOf(size).Constant!).Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "2097152";
        var heap = fn.Value($"call ptr {RuntimeFunction("ActorHeapNew")}(i64 {IrSize(block)})");
        var left = fn.Alloca("ptr", "heap", "null");
        fn.Emit($"store ptr {heap}, ptr {left}");
        if (regions.Count > 0)
        {
            if (!regionHeaps.TryGetValue(regions[^1], out var begun))
            {
                regionHeaps[regions[^1]] = begun = [];
            }

            begun.Add(left);
        }

        var spawner = fn.Alloca("ptr", "spawner");
        var state = Construct(symbol, s.Call, info, () =>
        {
            var state = fn.Value($"call ptr {RuntimeFunction("ActorNew")}(ptr {heap}, i64 {IrSize(block)}, i64 {stack})");
            fn.Emit($"store ptr null, ptr {left}");
            fn.Emit($"store i64 1, ptr {state}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {state}, i32 0, i32 1")}");
            fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("HeapUse")}(ptr {heap})")}, ptr {spawner}");
            return state;
        }, into: heap);
        fn.Emit($"call ptr {StdFunction("HeapUse")}(ptr {fn.Value($"load ptr, ptr {spawner}")})");
        return state;
    }

    /// <summary>
    /// At a region's end, or a jump out of it, after the region let go of its copies, the heap of each <c>spawn</c> begun in it that a
    /// case propagated out of a later argument left without its actor freed whole (the runtime's <c>HeapLeft</c>).
    /// </summary>
    private void EndHeaps(List<(string Value, KurzType Type)> temporaries)
    {
        if (regionHeaps.TryGetValue(temporaries, out var begun))
        {
            foreach (var left in begun)
            {
                fn.Emit($"call void {RuntimeFunction("HeapLeft")}(ptr {fn.Value($"load ptr, ptr {left}")})");
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
    /// <c>send</c> passes none (U6).
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
        var heap = fn.Value($"call ptr {RuntimeFunction("ActorHeap")}(ptr {actor})");
        var arguments = Arguments(c, info, j => f.Parameters[j].Type, _ => false, j => Default(f.Declaration!.Parameters[j].Default, owners[f]), cConvention: false, owned: true, into: heap);
        var message = MessageType(f);
        var block = fn.Value($"call ptr {RuntimeFunction("AllocateIn")}(ptr {heap}, i64 {IrSize(message)})");
        fn.Emit($"store ptr {MessageRun(f)}, ptr {MessageField(message, block, 1)}");
        for (var i = 0; i < arguments.Count; i++)
        {
            fn.Emit($"store {arguments[i]}, ptr {MessageField(message, block, 5 + i)}");
        }

        if (info.Kind == CallKind.ActorSend)
        {
            fn.Emit($"store ptr null, ptr {MessageField(message, block, 2)}");
            fn.Emit($"call void {RuntimeFunction("Enqueue")}(ptr {actor}, ptr {block})");
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
        fn.Emit($"call void {RuntimeFunction("Enqueue")}(ptr {actor}, ptr {block})");
        fn.Emit($"call void {RuntimeFunction("Wait")}(ptr {reply}, %kz.str {StringValue(unit.File)}, i32 {Number(c.Line)})");
        return Types.IsVoid(result) ? "void" : fn.Value($"load {Ir(result)}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 3")}");
    }

    /// <summary>
    /// U4, U6: the function of the messages of the method <paramref name="f"/>, which the runtime calls with the actor's state and the
    /// message, made at its first use and named outside the program's functions, so that a trace leaves its frame out as it leaves the
    /// runtime's: the method called on the state with the arguments the message holds, which it lets go of after the call; where the
    /// message has a reply, the result moved beside it, a copy of its own in the caller's heap where it holds a block, the method's let go
    /// of (U12; comment 6902210646, Q5), and the runtime's <c>Replied</c>, which sets its byte and wakes the activation that
    /// waits on it, as tokio's <c>oneshot::Sender::send</c> stores the value and then wakes the receiver; then the message's block freed.
    /// </summary>
    private string MessageRun(FunctionSymbol f) => Helper($"message.{f.Owner!.Name}.{f.Name}.{Number(f.Line)}", "void", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var arguments = f.Parameters.Select((p, i) => (p.Type, Value: fn.Value($"load {Ir(p.Type)}, ptr {MessageField(message, "%a1", 5 + i)}"))).ToList();
        var result = CallOn(f, "%a0", new ClassType(f.Owner!), arguments.Select(a => $"{Ir(a.Type)} {a.Value}"));
        var reply = fn.Value($"load ptr, ptr {MessageField(message, "%a1", 2)}");
        if (Counted(f.ReturnType))
        {
            // U12: a result that holds a block crosses back into the caller's heap as a copy of its own, and the method's lets go
            var heap = fn.Value($"load ptr, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 2")}");
            var crossed = Crossed(result, f.ReturnType, null, heap); // in a session of its own, not the call's
            Release(result, f.ReturnType);
            result = crossed;
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
        else
        {
            // a method with a result takes no `send` (U6, U7)
            fn.Emit($"store {Ir(f.ReturnType)} {result}, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 3")}");
            fn.Emit($"call void {RuntimeFunction("Replied")}(ptr {reply})");
        }

        fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 {IrSize(message)})");
        fn.Return(null);
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
