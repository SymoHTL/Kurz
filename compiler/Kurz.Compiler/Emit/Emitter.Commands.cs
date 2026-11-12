using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- running a program (S17 to S21) ----------------------------------------------------------------

    /// <summary>What a build for Windows says <c>not supported</c> with at a call on a command (S11, S17 to S21).</summary>
    internal const string CommandNotOnWindows = "running a program, `Command`, which the runtime of Windows does not give (S11, S17 to S21)";

    /// <summary>Whether the data type is <c>ExitError</c>, which no file can declare beside the one every file sees (S18, V9).</summary>
    private static bool IsExitError(KurzType type) => type is DataType { Symbol.Name: "ExitError" };

    /// <summary>
    /// S17: a command is the block of a list of strings: the program, then three strings for each member in the order of the calls, its
    /// tag and its two values, which <c>runtime/common/</c> appends through a <c>mut</c> list. A member is given its command as a value
    /// of its own: the result of the member before it in a chain, which no other value holds and which it takes over, so that the chain
    /// builds into one block, or a command a variable holds counted once more, so that the first <c>Add</c> copies it and the variable
    /// keeps its command (M3; section 12, the owner, 2026-11-01, P1, "Chain, each step a new value"). S18, S21: <c>Output()</c> and
    /// <c>Status()</c> are the runtime's <c>CommandOutput</c> and <c>CommandStatus</c> of the target, which start the child, wait for its
    /// end and give the call's union. Each call is <c>not supported</c> in a build for Windows (S11).
    /// </summary>
    private string EmitCommandCall(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, CommandNotOnWindows);
        }

        var m = (MemberAccess)c.Callee;
        if (info.Kind == CallKind.CommandNew)
        {
            var program = Arguments(c, info, _ => Types.String, _ => false, _ => null, cConvention: false, owned: false);
            return fn.Value($"call ptr {RuntimeFunction("CommandNew")}({program[0]})");
        }

        if (info.Kind is CallKind.CommandOutput or CallKind.CommandStatus)
        {
            var run = EmitExpression(m.Target);
            return fn.Value($"call {Ir(Sub(info.Result))} {SysFunction(info.Kind == CallKind.CommandOutput ? "CommandOutput" : "CommandStatus")}(ptr {run})");
        }

        if (info.Kind == CallKind.CommandSpawn)
        {
            return EmitSpawn(EmitExpression(m.Target), (Semantics.UnionType)Sub(info.Result));
        }

        var slot = fn.Alloca("ptr", "command");
        fn.Emit($"store ptr {Owned(m.Target)}, ptr {slot}"); // the command before its arguments, as written
        Append(c, info, slot, MemberArguments(c, info));
        return fn.Value($"load ptr, ptr {slot}");
    }

    // ---- a child beside the program (S19) ----------------------------------------------------------------

    /// <summary>S19: the offset in a child's block of the handle of its piped standard input, a <c>File</c>'s block or null.</summary>
    private const int ChildStdin = 8;

    /// <summary>S19: the offset of the handle of its piped standard output, and of its standard error 8 bytes after it.</summary>
    private const int ChildStdout = 16;

    /// <summary>S19: the offset of the program, the path of its errors and of its pipes' errors, a string of which the block holds a count.</summary>
    private const int ChildProgram = 32;

    /// <summary>
    /// S19: the offset of the state the runtime reads and writes: the child's number, 4 bytes; whether it was waited for, 0 not, 1 yes and
    /// 2 at a bound that passed, 4 bytes; the deadline of its bound on the monotonic clock, -1 where it has none, 8 bytes; and the status
    /// <c>waitpid</c> gave, 4 bytes.
    /// </summary>
    private const int ChildPid = 48;

    /// <summary>S19: the bytes of a child's block: its count, 8 bytes, its three handles, its program and its state.</summary>
    private const string ChildSize = "72";

    /// <summary>
    /// S19: <c>Spawn()</c> on the command <paramref name="run"/>: the runtime's <c>CommandSpawn</c> starts the child as <c>Output()</c>
    /// and <c>Status()</c> start theirs (S21), and gives its number, the program's end of each stream the command set to
    /// <c>Stdio.Piped</c> and the deadline of its bound; the child is a new block held by one value, each end a <c>File</c> of its own
    /// whose path is the program (S5), as a failed start names the program. A start that fails is the runtime's <c>SpawnFailure</c>.
    /// </summary>
    private string EmitSpawn(string run, Semantics.UnionType union)
    {
        var error = union.Cases.Single(IsIoError);
        var slot = fn.Alloca(Ir(union), "spawn");
        var spawned = fn.Alloca("[8 x i32]", "spawned");
        var r = fn.Value($"call i64 {SysFunction("CommandSpawn")}(ptr {run}, ptr {spawned})");
        var failed = fn.Label("spawn.failed");
        var started = fn.Label("spawn.started");
        var done = fn.Label("spawn.done");
        fn.CondBranch(fn.Value($"icmp slt i64 {r}, 0"), failed, started);

        fn.Block(failed);
        var at = fn.Value($"load i32, ptr {HandleField(spawned, 16)}");
        var e = fn.Value($"call {Ir(error)} {SysFunction("SpawnFailure")}(ptr {run}, i64 {r}, i32 {at})");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, error, e)}, ptr {slot}");
        fn.Branch(done);

        fn.Block(started);
        var program = fn.Value($"load %kz.str, ptr {fn.Value($"call ptr {StdFunction("ListItems")}(ptr {run})")}");
        var child = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {ChildSize})");
        fn.Emit($"store i64 1, ptr {child}");
        for (var stream = 0; stream < 3; stream++)
        {
            var fd = fn.Value($"load i32, ptr {HandleField(spawned, 4 + stream * 4)}");
            var place = HandleField(child, ChildStdin + stream * 8);
            var piped = fn.Label("spawn.piped");
            var next = fn.Label("spawn.next");
            fn.Emit($"store ptr null, ptr {place}");
            fn.CondBranch(fn.Value($"icmp sge i32 {fd}, 0"), piped, next);
            fn.Block(piped);
            fn.Emit($"store ptr {NewHandle(fd, program)}, ptr {place}");
            fn.Branch(next);
            fn.Block(next);
        }

        Retain(program, Types.String);
        fn.Emit($"store %kz.str {program}, ptr {HandleField(child, ChildProgram)}");
        fn.Emit($"store i32 {fn.Value($"load i32, ptr {spawned}")}, ptr {HandleField(child, ChildPid)}");
        fn.Emit($"store i32 0, ptr {HandleField(child, ChildPid + 4)}"); // not waited for
        fn.Emit($"store i64 {fn.Value($"load i64, ptr {HandleField(spawned, 24)}")}, ptr {HandleField(child, ChildPid + 8)}");
        fn.Emit($"store i32 0, ptr {HandleField(child, ChildPid + 16)}");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, Types.ChildProcess, child)}, ptr {slot}");
        fn.Branch(done);

        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {slot}");
    }

    /// <summary>S19: the child's <c>Stdin</c>, <c>Stdout</c> or <c>Stderr</c>, its <c>File</c> where the command piped the stream, else <c>null</c>; the child holds it.</summary>
    private string ChildStream(string child, string member)
    {
        var offset = member switch { "Stdin" => ChildStdin, "Stdout" => ChildStdout, _ => ChildStdout + 8 };
        var handle = fn.Value($"load ptr, ptr {HandleField(child, offset)}");
        var union = Types.Nullable(Types.File);
        var piped = MakeUnion(union, Types.File, handle);
        var none = MakeUnion(union, Types.Null, "zeroinitializer");
        return fn.Value($"select i1 {fn.Value($"icmp eq ptr {handle}, null")}, {Ir(union)} {none}, {Ir(union)} {piped}");
    }

    /// <summary>
    /// S19: <c>Wait()</c>, <c>WaitWithOutput()</c> and <c>Kill()</c>, the runtime's <c>ChildWait</c>, <c>ChildWaitWithOutput</c> and
    /// <c>ChildKill</c> on the child's state. A wait closes the program's end of a piped standard input first, as Rust's <c>wait</c> drops
    /// its <c>stdin</c>, so that a child that reads to its end ends (issue 174, Q6: the owner, 2026-11-11, "All three as built", comment
    /// 6790379522). <c>WaitWithOutput()</c> reads each pipe from where the program stopped: the bytes its <c>File</c> read ahead first,
    /// taken from it once the wait gives no <c>IoError</c>, then what the pipe still gives; a <c>File</c> that a <c>Close</c> marked gives
    /// none. A wait that gives an <c>IoError</c> gives every byte it gathered of a pipe back to that pipe's <c>File</c>, before the rest
    /// of the pipe (the owner, 2026-11-12, issue 174, comment 6819138844), those its <c>File</c> read ahead first, S19's assumed order
    /// (<see cref="GiveBack"/>). The runtime tries each pipe's reader lock at each wake of its poll and never waits on one, reading a
    /// pipe only once it took its lock (the owner, 2026-11-12, issue 174, comment 6821985043, "Try the lock inside the poll";
    /// <see cref="TryReader"/>), and each lock it took is held until the bytes are taken or given back (<see cref="LetGo"/>). Once
    /// the child ended, a lock another holder holds is tried once more at the wake after that end and then no more, and that pipe is
    /// left to its holder (the owner, 2026-11-12, issue 174, comment 6825493833, "Child's end decides"; the last try is S19's assumed
    /// reading). The call is in flight on each pipe's <c>File</c> from before the
    /// runtime's call until after its lock is let go of (<see cref="Pipe"/>, <see cref="Leave"/>). Each call is
    /// <c>not supported</c> in a build for Windows (S11), where no child exists.
    /// </summary>
    private string EmitChildCall(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, CommandNotOnWindows);
        }

        var child = EmitExpression(((MemberAccess)c.Callee).Target);
        var union = Ir(Sub(info.Result));
        var program = fn.Value($"load %kz.str, ptr {HandleField(child, ChildProgram)}");
        var state = HandleField(child, ChildPid);
        if (info.Kind == CallKind.ChildKill)
        {
            return fn.Value($"call {union} {SysFunction("ChildKill")}(ptr {state}, %kz.str {program})");
        }

        var input = fn.Value($"load ptr, ptr {HandleField(child, ChildStdin)}");
        var close = fn.Label("wait.close");
        var closed = fn.Label("wait.closed");
        fn.CondBranch(fn.Value($"icmp ne ptr {input}, null"), close, closed);
        fn.Block(close);
        fn.Emit($"call i64 {CloseHandle()}(ptr {input}, i1 false)"); // a `File` closed before gives its error, which the wait drops
        fn.Branch(closed);
        fn.Block(closed);
        if (info.Kind == CallKind.ChildWait)
        {
            return fn.Value($"call {union} {SysFunction("ChildWait")}(ptr {state}, %kz.str {program})");
        }

        var (outFd, outHandle) = Pipe(child, ChildStdout);
        var (errFd, errHandle) = Pipe(child, ChildStdout + 8);
        var outKept = fn.Alloca("ptr", "kept.out");
        var errKept = fn.Alloca("ptr", "kept.err");
        fn.Emit($"store ptr null, ptr {outKept}");
        fn.Emit($"store ptr null, ptr {errKept}");
        var held = fn.Alloca("[2 x i32]", "held");
        fn.Emit($"store [2 x i32] zeroinitializer, ptr {held}");
        var result = fn.Value($"call {union} {SysFunction("ChildWaitWithOutput")}(ptr {state}, %kz.str {program}, i32 {outFd}, ptr {Reader(outHandle)}, i32 {errFd}, ptr {Reader(errHandle)}, ptr {TryReader()}, ptr {held}, ptr {outKept}, ptr {errKept})");
        var outTook = Took(held, 0, outHandle);
        var errTook = Took(held, 1, errHandle);
        var type = (UnionType)Sub(info.Result);
        var take = fn.Label("ahead.take");
        var given = fn.Label("ahead.given");
        var done = fn.Label("ahead.done");
        fn.CondBranch(IsCase(Tag(result, type), type, type.Cases.Single(IsIoError)), given, take);
        fn.Block(take);
        Taken(outTook);
        Taken(errTook);
        fn.Branch(done);
        fn.Block(given);
        GiveBack(outTook, outKept);
        GiveBack(errTook, errKept);
        fn.Branch(done);
        fn.Block(done);
        LetGo(outTook);
        LetGo(errTook);
        Leave(outHandle);
        Leave(errHandle);
        return result;
    }

    /// <summary>
    /// S19: the descriptor of the pipe whose handle is at <paramref name="offset"/> in the child's block, and that handle, -1 and null
    /// where it is null or a <c>Close</c> marked it (S5). The <c>ChildProcess</c> holds the <c>File</c> through the call, so that nothing
    /// frees the reader the runtime tries the lock of (<see cref="TryReader"/>), and the call enters the handle as a call in flight on
    /// it (<see cref="EnterHandle"/>), so that a <c>Close</c> through another holder while the runtime polls the descriptor marks the
    /// handle and leaves the release of the descriptor to the call's end (<see cref="Leave"/>), as it leaves it to a read in flight (S5).
    /// </summary>
    private (string Fd, string Handle) Pipe(string child, int offset)
    {
        var fd = fn.Alloca("i32", "pipe");
        var open = fn.Alloca("ptr", "pipe.handle");
        fn.Emit($"store i32 -1, ptr {fd}");
        fn.Emit($"store ptr null, ptr {open}");
        var handle = fn.Value($"load ptr, ptr {HandleField(child, offset)}");
        var present = fn.Label("pipe.present");
        var unmarked = fn.Label("pipe.open");
        var done = fn.Label("pipe.done");
        fn.CondBranch(fn.Value($"icmp ne ptr {handle}, null"), present, done);
        fn.Block(present);
        fn.CondBranch(fn.Value($"call i1 {EnterHandle()}(ptr {handle})"), unmarked, done);
        fn.Block(unmarked);
        fn.Emit($"store i32 {HandleDescriptor(handle)}, ptr {fd}");
        fn.Emit($"store ptr {handle}, ptr {open}");
        fn.Branch(done);
        fn.Block(done);
        return (fn.Value($"load i32, ptr {fd}"), fn.Value($"load ptr, ptr {open}"));
    }

    /// <summary>S19: the reader's state of the handle <paramref name="handle"/>, null where it is null, which the runtime hands to <see cref="TryReader"/>.</summary>
    private string Reader(string handle) =>
        fn.Value($"select i1 {fn.Value($"icmp ne ptr {handle}, null")}, ptr {HandleField(handle, ReaderAt)}, ptr null");

    /// <summary>S19: the handle <paramref name="handle"/> where the runtime took its reader's lock, which it marks at <paramref name="index"/> of <paramref name="held"/>, and null where it did not.</summary>
    private string Took(string held, int index, string handle)
    {
        var mark = fn.Value($"load i32, ptr {fn.Value($"getelementptr [2 x i32], ptr {held}, i64 0, i64 {index}")}");
        return fn.Value($"select i1 {fn.Value($"icmp ne i32 {mark}, 0")}, ptr {handle}, ptr null");
    }

    /// <summary>
    /// S19: the helper the runtime calls at each wake of a <c>WaitWithOutput()</c>'s poll, through its address, to try the lock of the
    /// reader at <c>%a0</c> once and never wait on it, since Kurz has no atomic form of its own: -1 where another holder holds it, else
    /// the lock held, as <see cref="LockReader"/> takes a free one, and the count of the bytes the reader holds ahead of the program,
    /// whose address it writes at <c>%a1</c>, read only once the lock is held, so that no other holder takes them or replaces the
    /// buffer they sit in (S16; the owner, 2026-11-12, issue 174, comment 6821985043, "Try the lock inside the poll").
    /// </summary>
    private string TryReader() => Helper("try.reader", "i64", ["ptr", "ptr"], () =>
    {
        var tried = fn.Value($"cmpxchg ptr {ReaderField("%a0", ReaderLock)}, i32 0, i32 1 acquire monotonic");
        var locked = fn.Label("locked");
        var elsewhere = fn.Label("elsewhere");
        var buffered = fn.Label("buffered");
        var empty = fn.Label("empty");
        fn.CondBranch(fn.Value($"extractvalue {{ i32, i1 }} {tried}, 1"), locked, elsewhere);
        fn.Block(elsewhere);
        fn.Return("i64 -1");
        fn.Block(locked);
        var buffer = fn.Value($"load ptr, ptr {ReaderField("%a0", ReaderBuffer)}");
        fn.CondBranch(fn.Value($"icmp ne ptr {buffer}, null"), buffered, empty);
        fn.Block(empty);
        fn.Return("i64 0");
        fn.Block(buffered);
        var at = fn.Value($"load i32, ptr {ReaderField("%a0", ReaderPosition)}");
        var end = fn.Value($"load i32, ptr {ReaderField("%a0", ReaderCount)}");
        fn.Emit($"store ptr {fn.Value($"getelementptr i8, ptr {buffer}, i64 {fn.Value($"sext i32 {at} to i64")}")}, ptr %a1");
        fn.Return($"i64 {fn.Value($"sext i32 {fn.Value($"sub i32 {end}, {at}")} to i64")}");
    });

    /// <summary>
    /// S19: the reader's position of the handle <paramref name="handle"/>, whose lock the runtime took (<see cref="Took"/>), moved past
    /// the bytes <see cref="TryReader"/> handed to a wait that read the pipes, once the wait gave no <c>IoError</c>; a wait that gave
    /// one, a failed read's or <c>TimedOut</c>, gives them back with the rest it read instead (<see cref="GiveBack"/>). Null where the
    /// runtime took no lock of the pipe.
    /// </summary>
    private void Taken(string handle)
    {
        var move = fn.Label("ahead.move");
        var moved = fn.Label("ahead.moved");
        fn.CondBranch(fn.Value($"icmp ne ptr {handle}, null"), move, moved);
        fn.Block(move);
        var reader = HandleField(handle, ReaderAt);
        fn.Emit($"store i32 {fn.Value($"load i32, ptr {ReaderField(reader, ReaderCount)}")}, ptr {ReaderField(reader, ReaderPosition)}");
        fn.Branch(moved);
        fn.Block(moved);
    }

    /// <summary>
    /// S19: the bytes a <c>WaitWithOutput()</c> that gave an <c>IoError</c> gathered of one pipe, in the list at <paramref name="kept"/>,
    /// given back to that <c>File</c>'s reader (<see cref="KeepRest"/>), so that a later read takes them before the rest of the pipe and
    /// sees the output whole (the owner, 2026-11-12, issue 174, (a), against the bytes dropped), those its <c>File</c> read ahead first,
    /// S19's assumed order; under the reader's lock that the runtime took, which this takes no second time; <paramref name="handle"/>
    /// is the <c>File</c> whose lock the runtime took (<see cref="Took"/>), null where it took none and so gathered nothing of it. The
    /// list is let go of either way.
    /// </summary>
    private void GiveBack(string handle, string kept)
    {
        var list = fn.Value($"load ptr, ptr {kept}");
        var give = fn.Label("give.back");
        var given = fn.Label("give.done");
        fn.CondBranch(fn.Value($"and i1 {fn.Value($"icmp ne ptr {handle}, null")}, {fn.Value($"icmp ne ptr {list}, null")}"), give, given);
        fn.Block(give);
        KeepRest(handle, HandleField(handle, ReaderAt), list);
        fn.Branch(given);
        fn.Block(given);
        Release(list, new ListType(Types.Byte));
    }

    /// <summary>
    /// S19: the reader's lock of the handle <paramref name="handle"/> that the runtime took (<see cref="Took"/>) let go of, null where
    /// it took none, once the wait's bytes were taken (<see cref="Taken"/>) or given back (<see cref="GiveBack"/>).
    /// </summary>
    private void LetGo(string handle)
    {
        var held = fn.Label("ahead.held");
        var free = fn.Label("ahead.free");
        fn.CondBranch(fn.Value($"icmp ne ptr {handle}, null"), held, free);
        fn.Block(held);
        fn.Emit($"call void {UnlockReader()}(ptr {ReaderField(HandleField(handle, ReaderAt), ReaderLock)})");
        fn.Branch(free);
        fn.Block(free);
    }

    /// <summary>
    /// S19, S5: the call on the handle <paramref name="handle"/> that <see cref="Pipe"/> entered ended, null where it entered none; the
    /// last call out of a handle a <c>Close</c> marked meanwhile releases its descriptor (<see cref="LeaveHandle"/>).
    /// </summary>
    private void Leave(string handle)
    {
        var entered = fn.Label("pipe.entered");
        var left = fn.Label("pipe.left");
        fn.CondBranch(fn.Value($"icmp ne ptr {handle}, null"), entered, left);
        fn.Block(entered);
        fn.Emit($"call void {LeaveHandle()}(ptr {handle})");
        fn.Branch(left);
        fn.Block(left);
    }

    /// <summary>
    /// S19: one value less holds the child's block; the last lets go of its three handles, each of which closes its pipe at its own last
    /// holder (S5), and of its program, and frees the block, with neither a wait nor a kill, so that a child let go of runs on (S19).
    /// </summary>
    private string ReleaseChild() => Helper("release.ChildProcess", "void", ["ptr"], () =>
    {
        var count = fn.Value($"sub i64 {fn.Value("load i64, ptr %a0")}, 1");
        fn.Emit($"store i64 {count}, ptr %a0");
        var last = fn.Label("last");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {count}, 0"), last, held);
        fn.Block(last);
        for (var stream = 0; stream < 3; stream++)
        {
            var handle = fn.Value($"load ptr, ptr {HandleField("%a0", ChildStdin + stream * 8)}");
            var piped = fn.Label("piped");
            var next = fn.Label("next");
            fn.CondBranch(fn.Value($"icmp ne ptr {handle}, null"), piped, next);
            fn.Block(piped);
            fn.Emit($"call void {ReleaseHandle()}(ptr {handle})");
            fn.Branch(next);
            fn.Block(next);
        }

        Release(fn.Value($"load %kz.str, ptr {HandleField("%a0", ChildProgram)}"), Types.String);
        fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {ChildSize})");
        fn.Return(null);
        fn.Block(held);
        fn.Return(null);
    });

    /// <summary>
    /// The arguments of a member of S17, read in the order they are written; the callee borrows them. With <paramref name="held"/> each
    /// one that is no temporary of a region is counted once more, a temporary of the region being emitted, whose end, a <c>return</c> or a
    /// <c>break</c> lets go of it, so that it outlives a change of the place it was read from (<see cref="EmitCommandInPlace"/>).
    /// </summary>
    private List<string> MemberArguments(CallExpression c, CallInfo info, bool held = false)
    {
        KurzType[] slots = ((MemberAccess)c.Callee).Member switch
        {
            "Arg" or "Dir" or "EnvRemove" => [Types.String],
            "Env" => [Types.String, Types.String],
            "Args" => [new ListType(Types.String)],
            "Stdin" or "Stdout" or "Stderr" => [TypeOf(c.Arguments[0].Value)],
            "Input" => [TypeOf(c.Arguments[0].Value) is PrimitiveType { Kind: Primitive.String } ? Types.String : new ListType(Types.Byte)], // S19's two forms
            "Timeout" => [Types.Duration],
            _ => [],
        };
        var arguments = Arguments(c, info, j => slots[j], _ => false, _ => null, cConvention: false, owned: false);
        for (var j = 0; held && j < arguments.Count; j++)
        {
            var type = Sub(slots[j]);
            var value = arguments[j][(Ir(type).Length + 1)..]; // `Arguments` writes each as its type and its value
            if (Counted(type) && !regions.Any(r => r.Exists(t => t.Value == value)))
            {
                Retain(value, type);
                Temp(value, type);
            }
        }

        return arguments;
    }

    /// <summary>
    /// S17: the member of the call <paramref name="c"/>, with its <paramref name="arguments"/>, appended to the command at
    /// <paramref name="slot"/>, which holds a count of it: in place where no other value holds its block, else into a copy that the slot
    /// then holds, as a list's <c>Add</c> writes (M3).
    /// </summary>
    private void Append(CallExpression c, CallInfo info, string slot, List<string> arguments)
    {
        var member = ((MemberAccess)c.Callee).Member;
        var empty = $"%kz.str {StringValue(string.Empty)}";
        fn.Emit(info.Kind switch
        {
            CallKind.CommandAdd => member switch
            {
                "Arg" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("a")}, {arguments[0]}, {empty})",
                "Dir" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("d")}, {arguments[0]}, {empty})",
                "Env" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("e")}, {arguments[0]}, {arguments[1]})",
                "EnvRemove" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("r")}, {arguments[0]}, {empty})",
                _ => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("c")}, {empty}, {empty})",
            },
            CallKind.CommandArgs => $"call void {RuntimeFunction("CommandArgs")}(ptr {slot}, {arguments[0]})",
            CallKind.CommandInput => $"call void {RuntimeFunction(TypeOf(c.Arguments[0].Value) is PrimitiveType { Kind: Primitive.String } ? "CommandInputText" : "CommandInput")}(ptr {slot}, {arguments[0]})",
            CallKind.CommandTimeout => $"call void {RuntimeFunction("CommandTimeout")}(ptr {slot}, {arguments[0]})", // a `duration` is its nanoseconds, the `long` the runtime takes
            _ => $"call void {RuntimeFunction("CommandStream")}(ptr {slot}, i32 {(member == "Stdin" ? "0" : member == "Stdout" ? "1" : "2")}, {arguments[0]})",
        });
    }

    /// <summary>
    /// S17: <c>cmd = cmd.Arg(x)</c>, an assignment to a variable of a chain of members on that variable, written as the members appended
    /// to the variable itself, so that a command no other value holds changes in place, since the assignment keeps no earlier command,
    /// and one another value holds is copied once, by the first <c>Add</c>. The arguments of every member are read first, in the order
    /// they are written, so that an argument that reads the variable reads the command before any member is appended, and a case an
    /// argument passes on (O2) leaves the variable as it was. The checker holds a borrowed argument unchanged only across its own call
    /// (M7), while here the arguments of a member stay read until the members after it have read theirs, which can change the place it
    /// was read from, <c>cmd = cmd.Arg(s).Arg(Take(mut s))</c>: so each argument of a member before the last is counted for the
    /// statement (<see cref="MemberArguments"/>), as the line <c>command-chain-fenced</c> of <c>ListTests</c> holds. An argument that
    /// changes the variable itself, <c>cmd = cmd.Arg(Reset(mut cmd))</c>, is refused by the checker, <c>changed-while-held</c>, since a
    /// <c>Command</c> is not copied (M7), so the command the members are appended to is the one read before the arguments; a field takes
    /// the ordinary path, since this one takes a variable alone. False, with nothing emitted, where the value is no such chain.
    /// </summary>
    private bool EmitCommandInPlace(Expression assigned, VariableSymbol variable, string place)
    {
        var chain = new List<CallExpression>();
        var value = Unwrap(assigned);
        while (value is CallExpression c && Model.CallOf(c).Kind is CallKind.CommandAdd or CallKind.CommandArgs or CallKind.CommandStream or CallKind.CommandInput or CallKind.CommandTimeout)
        {
            chain.Insert(0, c);
            value = Unwrap(((MemberAccess)c.Callee).Target);
        }

        if (target != Target.Linux || chain.Count == 0 || value is not NameExpression n || !ReferenceEquals(Model.VariableOf(n), variable))
        {
            return false;
        }

        var arguments = chain.Select((c, i) => MemberArguments(c, Model.CallOf(c), held: i < chain.Count - 1)).ToList();
        for (var i = 0; i < chain.Count; i++)
        {
            Append(chain[i], Model.CallOf(chain[i]), place, arguments[i]);
        }

        return true;
    }
}
