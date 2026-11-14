using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 19 on the one thread the runtime runs, each message run to its end, as tokio's <c>current_thread</c> runtime runs its tasks
/// (the lean of the round of 2026-11-13, issue 519, comment 6866132292, Q1, which no answer named; <c>compiler/README.md</c> lists
/// what of section 6 this does not build). An actor's state is an instance of its body, laid out and constructed as a class's
/// (<see cref="Construct"/>), in a block of the runtime's <c>ActorNew</c> that holds the actor's record in front of it, as a string's
/// block stands in front of its bytes: the reference is the address of the state, so that the actor's own methods reach the record
/// through <c>this</c> (U11). Nothing counts an actor, which lives to the end of the process. A call through a reference, or after
/// <c>send</c>, is a message, one block of the allocator, as actix packs a message and the sender of its reply into one envelope: the
/// message after it in the inbox, which the runtime's <c>Enqueue</c> sets, the function that runs it, the reply its caller waits on, null
/// after <c>send</c>, as a message of a tokio actor carries a <c>oneshot</c> sender or none, then the arguments, each holding a count
/// of its own (<see cref="EmitMessage"/>). The function of a method's messages calls the method on the state, as actix's
/// <c>EnvelopeProxy::handle</c> calls the handler on the actor (<see cref="MessageRun"/>).
/// </summary>
public sealed partial class Emitter
{
    /// <summary>
    /// U2, U3: <c>spawn Name(args)</c>, the state constructed as an instance of a class is (K1, K7, D11) in the room the runtime's
    /// <c>ActorNew</c> gives behind the actor's record, both its counts at one, as <see cref="EmitClassNew"/> makes them, which the
    /// record holds to the end of the process, so that a count a lambda takes of <c>this</c> never lets go of the state; the arguments,
    /// the constructor and the defaults of the body run here, before <c>spawn</c> gives the reference, which on one thread is the new
    /// actor running its constructor while the spawner waits (U3).
    /// </summary>
    private string EmitActorSpawn(SpawnExpression s)
    {
        var info = Model.CallOf(s.Call);
        var symbol = ((ClassType)info.Target!).Symbol;
        var block = layout.ClassBlock(symbol);
        return Construct(symbol, s.Call, info, () =>
        {
            var state = fn.Value($"call ptr {RuntimeFunction("ActorNew")}(i64 {IrSize(block)})");
            fn.Emit($"store i64 1, ptr {state}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {state}, i32 0, i32 1")}");
            return state;
        });
    }

    /// <summary>
    /// U4, U6, U11: a call of an actor's method as a message: the actor, the address of its state, read first, through its reference
    /// or, inside it, as <c>this</c> (U11), then the arguments, each taking a count of its own (D11, F13), stored into a block that the
    /// message's function frees once it ran; the block goes to the end of the actor's inbox (U5, U15). A call that waits (U4) passes a
    /// reply in its own frame, a byte the message's function sets once the result stands beside it, and the runtime's <c>Wait</c> runs
    /// messages until it is set; the result is then the caller's, a temporary of its region as any call's result is. A <c>send</c>
    /// passes none (U6).
    /// </summary>
    private string EmitMessage(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        if (f.Parameters.FirstOrDefault(p => p.Mut) is { } mut)
        {
            // the checker reads a `send` of the actor's own method with a `mut` parameter (U11), whose place no message can hold
            throw Unsupported(c.Line, $"the `mut` parameter `{mut.Name}` of `{f.Owner!.Name}.{f.Name}` in a message, which issue 519 holds open, comment 6866132292, Q6 (U9, U11)");
        }

        var actor = c.Callee switch
        {
            MemberAccess m => EmitExpression(m.Target),
            NameExpression n => This(n, n.Line),
            _ => throw new InvalidOperationException($"a message through `{c.Callee.GetType().Name}` at line {c.Line}"),
        };
        var arguments = Arguments(c, info, j => f.Parameters[j].Type, _ => false, j => Default(f.Declaration!.Parameters[j].Default, owners[f]), cConvention: false, owned: true);
        var message = MessageType(f);
        var block = Allocate(message);
        fn.Emit($"store ptr {MessageRun(f)}, ptr {MessageField(message, block, 1)}");
        for (var i = 0; i < arguments.Count; i++)
        {
            fn.Emit($"store {arguments[i]}, ptr {MessageField(message, block, 3 + i)}");
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
        fn.Emit($"store ptr {reply}, ptr {MessageField(message, block, 2)}");
        fn.Emit($"call void {RuntimeFunction("Enqueue")}(ptr {actor}, ptr {block})");
        fn.Emit($"call void {RuntimeFunction("Wait")}(ptr {reply})");
        return Types.IsVoid(result) ? "void" : fn.Value($"load {Ir(result)}, ptr {fn.Value($"getelementptr {Reply(result)}, ptr {reply}, i32 0, i32 1")}");
    }

    /// <summary>
    /// U4, U6: the function of the messages of the method <paramref name="f"/>, which the runtime calls with the actor's state and the
    /// message, made at its first use and named outside the program's functions, so that a trace leaves its frame out as it leaves the
    /// runtime's: the method called on the state with the arguments the message holds, which it lets go of after the call; where the
    /// message has a reply, the result moved beside it and its byte set, as tokio's <c>oneshot::Sender::send</c> stores the value and
    /// then marks it sent; then the message's block freed.
    /// </summary>
    private string MessageRun(FunctionSymbol f) => Helper($"message.{f.Owner!.Name}.{f.Name}.{Number(f.Line)}", "void", ["ptr", "ptr"], () =>
    {
        var message = MessageType(f);
        var arguments = f.Parameters.Select((p, i) => (p.Type, Value: fn.Value($"load {Ir(p.Type)}, ptr {MessageField(message, "%a1", 3 + i)}"))).ToList();
        var result = CallOn(f, "%a0", new ClassType(f.Owner!), arguments.Select(a => $"{Ir(a.Type)} {a.Value}"));
        foreach (var (type, value) in arguments.Where(a => Counted(a.Type)))
        {
            Release(value, type);
        }

        var reply = fn.Value($"load ptr, ptr {MessageField(message, "%a1", 2)}");
        if (Types.IsVoid(f.ReturnType))
        {
            // a void method takes a `send` and a call that waits alike, and a `send` has no reply (U6)
            var waits = fn.Label("waits");
            var sent = fn.Label("sent");
            fn.CondBranch(fn.Value($"icmp ne ptr {reply}, null"), waits, sent);
            fn.Block(waits);
            fn.Emit($"store i8 1, ptr {reply}");
            fn.Block(sent);
        }
        else
        {
            // a method with a result takes no `send` (U6, U7)
            fn.Emit($"store {Ir(f.ReturnType)} {result}, ptr {fn.Value($"getelementptr {Reply(f.ReturnType)}, ptr {reply}, i32 0, i32 1")}");
            fn.Emit($"store i8 1, ptr {reply}");
        }

        fn.Emit($"call void {StdFunction("Free")}(ptr %a1, i64 {IrSize(message)})");
        fn.Return(null);
    });

    /// <summary>The IR type of a message of the method: the message after it in the inbox, the function that runs it, the reply, then the arguments.</summary>
    private string MessageType(FunctionSymbol f) => $"{{ ptr, ptr, ptr{string.Concat(f.Parameters.Select(p => ", " + Ir(p.Type)))} }}";

    /// <summary>The IR type of the reply of a call that waits: the byte set once the result stands, then the result.</summary>
    private string Reply(KurzType result) => Types.IsVoid(result) ? "{ i8 }" : $"{{ i8, {Ir(result)} }}";

    private string MessageField(string message, string block, int index) => fn.Value($"getelementptr {message}, ptr {block}, i32 0, i32 {Number(index)}");

    /// <summary>The size of a value of the IR type, in bytes.</summary>
    private string IrSize(string irType) => fn.Value($"ptrtoint ptr {fn.Value($"getelementptr {irType}, ptr null, i32 1")} to i64");
}
