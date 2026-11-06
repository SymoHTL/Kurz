using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Files and sockets (chapter 13; design record, section 12). A file, a listener and a stream are the address of their handle's block:
/// the count of the values that hold it, 8 bytes, its descriptor, 4 bytes, a listener's port, 4 bytes, from its
/// 16th byte the path or the address it was opened with, a string of which the block holds a count, from its 32nd byte the address of the
/// buffer a file's lines are read through, null until the first pull, the position of its first byte not taken yet and the count of the
/// bytes read into it, 4 bytes each, and the word of the lock a pull takes, 4 bytes (S16), and from its 52nd byte the word of the calls in
/// flight on it and of the mark a <c>Close</c> leaves, 4 bytes (S5, <see cref="InFlightAt"/>); 56 bytes. A stream a listener's
/// <c>Accept</c> gives holds the listener's address (S9). The last value that lets go of it closes the descriptor no <c>Close</c> marked, ignoring an error of the close, as Rust's
/// <c>Drop</c> of a <c>File</c> ignores it, lets go of the path, frees the buffer and the block (S5; the owner, 2026-10-29, "At the last count, as Rust
/// Drop"). A call on a file calls the runtime of Linux, whose result is a number of 0 or more or a failure below 0, which the runtime's
/// <c>ErrorKind</c> and <c>ErrorCode</c> read into an <c>IoError</c> with the path the call was about (S1); the runtime of Windows gives none of
/// these functions, and a build for Windows says <c>not supported</c> at the call (S11; the owner, 2026-10-29, "Not supported on Windows").
/// </summary>
public sealed partial class Emitter
{
    /// <summary>What a build for Windows says <c>not supported</c> with at a call on a file or a socket (S11).</summary>
    internal const string NotOnWindows = "a call on a file or a socket, which the runtime of Windows does not give (S11)";

    /// <summary>The bytes of a handle's block.</summary>
    private const string HandleSize = "56";

    /// <summary>The bytes of the buffer a file's lines are read through, as Rust's <c>BufReader</c> reads 8 KiB at a time (S16).</summary>
    private const string LineBuffer = "8192";

    /// <summary>
    /// S3, S16: where a handle's block holds the state of its reader, which its reads and a file's lines go through, 20 bytes: the address
    /// of the buffer, null until the first fill or pull, 8 bytes, then the position of the buffer's first byte not taken yet, the count of
    /// the bytes read into it and the word of the lock a read or a pull takes, 4 bytes each. The pull reads nothing else of the handle but
    /// its descriptor, so that whatever holds a descriptor can hold a reader's state.
    /// </summary>
    private const int ReaderAt = 32;

    /// <summary>
    /// S5: where a handle's block holds the word of its calls in flight, 4 bytes: 2 for each call on the handle that runs, between
    /// <see cref="EnterHandle"/> and <see cref="LeaveHandle"/>, and 1 once a <c>Close</c> through any holder marked the handle closed, so that
    /// the call that leaves the word at 1, the last call in flight out or a <c>Close</c> that finds none in flight, releases the descriptor,
    /// as Rust's <c>Arc&lt;File&gt;</c> closes the file with its last clone (the owner, 2026-11-02, against a lock around each call and against
    /// keeping the hazard of a call that reaches a number the system has given another file since).
    /// </summary>
    private const int InFlightAt = 52;

    /// <summary>S5: the result a call on a handle a <c>Close</c> marked gives, the system's error of a descriptor that is not open, <c>EBADF</c>.</summary>
    private const string ClosedResult = "-9";

    /// <summary>S9: the runtime's function of the kinds of a failure of a stream's read and write and of a listener's <c>Accept</c>, the calls on a socket that block.</summary>
    private const string SocketKinds = "SocketErrorKind";

    // the fields of a reader's state, from its start
    private const int ReaderBuffer = 0;
    private const int ReaderPosition = 8;
    private const int ReaderCount = 12;
    private const int ReaderLock = 16;

    /// <summary>The bytes a read of a whole file asks room for where its list has none left (S6).</summary>
    private const string ReadAllChunk = "8192";

    /// <summary>The result of a call on a file being emitted: its union, the case <c>IoError</c>, the slot that takes the value and the block after it.</summary>
    private sealed record FileOutcome(UnionType Union, DataType Error, string Slot, string Done);

    /// <summary>The IR name of a <c>pub</c> function of the runtime of the target, which only a program's calls on files and sockets call.</summary>
    private string SysFunction(string name) => Instance(
        sys.Tree.Statements.OfType<FunctionDeclaration>()
            .Where(d => d.Name == name && (d.Modifiers & Modifiers.Pub) != 0)
            .Select(d => sys.Model.FunctionOf(d))
            .SingleOrDefault() ?? throw new InvalidOperationException($"{sys.File} has no `pub` function `{name}`, which a call on a file or a socket calls"),
        NoMap);

    /// <summary>Whether the data type is <c>IoError</c>, which no file can declare beside the one every file sees (S1, V9).</summary>
    private static bool IsIoError(KurzType type) => type is DataType { Symbol.Name: "IoError" };

    /// <summary>S2 to S9: a call on a path, an address, a file or a socket, whose result is its success or the case <c>IoError</c>.</summary>
    private string EmitFileCall(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, NotOnWindows);
        }

        var union = (UnionType)Sub(info.Result);
        var outcome = new FileOutcome(union, (DataType)union.Cases.Single(IsIoError), fn.Alloca(Ir(union), "io"), fn.Label("io.done"));
        if (info.Kind is CallKind.FileRead or CallKind.FileFill or CallKind.FileConsume or CallKind.FileWrite or CallKind.FileClose or CallKind.TcpAccept or CallKind.TcpSetReadTimeout or CallKind.TcpSetWriteTimeout or CallKind.TcpSetAcceptTimeout
            or CallKind.TcpShutdownWrite)
        {
            var target = ((MemberAccess)Unwrap(c.Callee)).Target;
            var socket = TypeOf(target) is HandleType { Kind: Handle.TcpStream or Handle.TcpListener };
            var kinds = socket ? SocketKinds : "ErrorKind";
            var handle = EmitExpression(target);
            switch (info.Kind)
            {
                case CallKind.FileRead:
                    EmitFileRead(c, info, outcome, handle, kinds);
                    break;
                case CallKind.FileFill:
                    EmitFileFill(c, info, outcome, handle, kinds);
                    break;
                case CallKind.FileConsume:
                    EmitFileConsume(c, info, outcome, handle, kinds);
                    break;
                case CallKind.FileWrite:
                    EmitFileWrite(c, info, outcome, handle, kinds);
                    break;
                case CallKind.TcpAccept:
                    EmitAccept(outcome, handle, kinds);
                    break;
                case CallKind.TcpSetReadTimeout or CallKind.TcpSetWriteTimeout or CallKind.TcpSetAcceptTimeout:
                    EmitSetTimeout(c, info, outcome, handle);
                    break;
                case CallKind.TcpShutdownWrite:
                    {
                        // S9: the writes ended by the system's `shutdown`, the descriptor kept, so that the stream still reads; a call in
                        // flight on the handle (S5), so that through a holder of a closed stream it gets the closed handle's error
                        var o = Enter(outcome, handle, "ErrorKind");
                        FailBelowZero(o, SysCall("ShutdownWrite", $"i32 {HandleDescriptor(handle)}"), HandlePath(handle));
                        Succeed(o, null);
                        Leave(o, outcome, handle);
                        break;
                    }
                default:
                    EmitFileClose(outcome, handle, socket);
                    break;
            }
        }
        else
        {
            var bytes = new ListType(Types.Byte);
            KurzType Parameter(int j) => (info.Kind, j) switch
            {
                (CallKind.FileWriteAll, 1) => bytes,
                (CallKind.TcpConnect, 1) => Types.Duration,
                _ => Types.String,
            };
            var arguments = Arguments(c, info, Parameter, _ => false, _ => null, cConvention: false, owned: false)
                .Select(a => a[(a.IndexOf(' ', StringComparison.Ordinal) + 1)..])
                .ToList();
            EmitPathCall(info.Kind, outcome, arguments);
        }

        fn.Block(outcome.Done);
        return fn.Value($"load {Ir(union)}, ptr {outcome.Slot}");
    }

    /// <summary>S2, S6, S7, S8: a function of <c>File</c> or <c>Directory</c> on a path, the error naming the first path; S9: a listener or a stream on an address, the error naming it.</summary>
    private void EmitPathCall(CallKind kind, FileOutcome o, List<string> arguments)
    {
        var path = arguments[0];
        var c = CPath(o, path, path, null);
        switch (kind)
        {
            case CallKind.FileOpen or CallKind.FileCreate:
                {
                    var r = SysCall("OpenFile", $"ptr {c}, i1 {(kind == CallKind.FileCreate ? "true" : "false")}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    Succeed(o, NewHandle(fn.Value($"trunc i64 {r} to i32"), path));
                    break;
                }

            case CallKind.TcpBind:
                {
                    var port = fn.Alloca("i32", "port");
                    var r = SysCall("Listen", $"ptr {c}, ptr {port}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    var listener = NewHandle(fn.Value($"trunc i64 {r} to i32"), path);
                    fn.Emit($"store i32 {fn.Value($"load i32, ptr {port}")}, ptr {HandleField(listener, 12)}");
                    Succeed(o, listener);
                    break;
                }

            case CallKind.TcpConnect:
                {
                    // the second form takes its time (S9), the first the runtime's default bound
                    var r = arguments.Count == 2 ? SysCall("ConnectWithin", $"ptr {c}, i64 {arguments[1]}") : SysCall("Connect", $"ptr {c}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    Succeed(o, NewHandle(fn.Value($"trunc i64 {r} to i32"), path));
                    break;
                }

            case CallKind.FileReadAll:
                {
                    var list = fn.Alloca("ptr", "bytes");
                    fn.Emit($"store ptr null, ptr {list}");
                    var r = fn.Value($"call i64 {ReadAll()}(ptr {c}, ptr {list})");
                    FreePath(c, path);
                    FailBelowZero(o, r, path, () => Release(fn.Value($"load ptr, ptr {list}"), new ListType(Types.Byte)));
                    Succeed(o, fn.Value($"load ptr, ptr {list}"));
                    break;
                }

            case CallKind.FileWriteAll:
                {
                    // as Rust's `fs::write`: the file created or emptied, every byte written, and the close's error ignored
                    var opened = SysCall("OpenFile", $"ptr {c}, i1 true");
                    FreePath(c, path);
                    FailBelowZero(o, opened, path);
                    var fd = fn.Value($"trunc i64 {opened} to i32");
                    var (items, count) = BytesOf(arguments[1]);
                    var written = SysCall("WriteFile", $"i32 {fd}, ptr {items}, i64 {count}");
                    SysCall("CloseFile", $"i32 {fd}");
                    FailBelowZero(o, written, path);
                    WriteZeroBelow(o, written, count, path);
                    Succeed(o, null);
                    break;
                }

            case CallKind.FileRename:
                {
                    var to = arguments[1];
                    var c2 = CPath(o, to, path, () => FreePath(c, path));
                    var r = SysCall("RenameFile", $"ptr {c}, ptr {c2}");
                    FreePath(c, path);
                    FreePath(c2, to);
                    FailBelowZero(o, r, path);
                    Succeed(o, null);
                    break;
                }

            case CallKind.DirectoryCreate:
                {
                    var count = fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {path}, 1")}, 9223372036854775807"); // without the mark of a block
                    var r = SysCall("CreateDirectories", $"ptr {c}, i64 {count}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    Succeed(o, null);
                    break;
                }

            case CallKind.FileModified:
                {
                    var nanoseconds = fn.Alloca("i64", "modified");
                    var r = SysCall("FileModified", $"ptr {c}, ptr {nanoseconds}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    Succeed(o, fn.Value($"load i64, ptr {nanoseconds}"));
                    break;
                }

            case CallKind.DirectoryList:
                {
                    var names = new ListType(Types.String);
                    var list = fn.Alloca("ptr", "names");
                    fn.Emit($"store ptr null, ptr {list}");
                    var r = SysCall("ListDirectory", $"ptr {c}, ptr {list}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path, () => Release(fn.Value($"load ptr, ptr {list}"), names));
                    Succeed(o, fn.Value($"load ptr, ptr {list}"));
                    break;
                }

            default:
                {
                    var (function, success) = kind switch
                    {
                        CallKind.FileDelete => ("DeleteFile", (Func<string, string?>)(_ => null)),
                        CallKind.FileSize => ("FileSize", r => r),
                        CallKind.FileExists => ("FileExists", r => fn.Value($"icmp ne i64 {r}, 0")),
                        CallKind.DirectoryDelete => ("DeleteDirectory", _ => null),
                        _ => throw new InvalidOperationException(kind.ToString()),
                    };
                    var r = SysCall(function, $"ptr {c}");
                    FreePath(c, path);
                    FailBelowZero(o, r, path);
                    Succeed(o, success(r));
                    break;
                }
        }
    }

    /// <summary>
    /// S3: <c>f.Read(mut into, max)</c>: a <c>max</c> below 0 raises <c>index-out-of-range</c>; then the room for <c>max</c> more bytes
    /// is made in <c>into</c>, as <c>Add</c> grows a list, before any byte is read, and the bytes read are added at its end; the kind of a
    /// failure is the one the runtime's function <paramref name="kinds"/> gives (<see cref="FailBelowZero"/>).
    /// The read goes through the handle's buffer of <see cref="LineBuffer"/> bytes (§12, the owner, 2026-11-02), which a loop over a file's
    /// lines fills too (S16), under the lock a pull takes, as Rust's <c>BufReader::read</c> reads (§12, the owner, 2026-11-03, issue 39,
    /// comment 6605104107): the bytes the buffer holds go first, at most <c>max</c> of them, with no system call; where it holds none, a
    /// <c>max</c> of 8 KiB or more, and one of 0 (assumed), lets the lock go and
    /// is read straight into the list, and a smaller one fills the buffer with one read, the buffer made at the first fill, holding the lock
    /// while that read waits, and takes at most <c>max</c> of what it read. A failed read stores nothing.
    /// S5: the read is a call in flight on the handle (<see cref="Enter"/>), after the room is made, and a socket's read that a <c>Close</c>
    /// woke gets the closed handle's error (<see cref="Woken"/>), the fill's as the straight one's; a fill that finds the handle marked when
    /// its read returns keeps none of the rest in the buffer, so that a <c>Close</c> made while the read waited drops those bytes too.
    /// </summary>
    private void EmitFileRead(CallExpression c, CallInfo info, FileOutcome outer, string handle, string kinds)
    {
        var arguments = Arguments(c, info, j => j == 0 ? new ListType(Types.Byte) : Types.Int, j => j == 0, _ => null, cConvention: false, owned: false);
        var place = arguments[0]["ptr ".Length..];
        var max = arguments[1]["i32 ".Length..];
        var negative = fn.Label("negative");
        var counted = fn.Label("counted");
        fn.CondBranch(fn.Value($"icmp slt i32 {max}, 0"), negative, counted);
        fn.Block(negative);
        Raise(ErrorIds.IndexOutOfRange, c.Line);
        fn.Block(counted);
        var more = fn.Value($"sext i32 {max} to i64");
        var block = Reserve(place, more);
        var o = Enter(outer, handle, kinds);
        var reader = HandleField(handle, ReaderAt);
        var word = ReaderField(reader, ReaderLock);
        void Unlock() => fn.Emit($"call void {UnlockReader()}(ptr {word})");
        void Append(string from, string count) => AppendBytes(block, from, count);

        fn.Emit($"call void {LockReader()}(ptr {word})");
        var at = fn.Value($"load i32, ptr {ReaderField(reader, ReaderPosition)}");
        var held = fn.Value($"sub i32 {fn.Value($"load i32, ptr {ReaderField(reader, ReaderCount)}")}, {at}");
        var take = fn.Label("take");
        var empty = fn.Label("empty");
        fn.CondBranch(fn.Value($"icmp sgt i32 {held}, 0"), take, empty);

        // the bytes the buffer holds, at most `max` of them, with no system call
        fn.Block(take);
        var buffer = fn.Value($"load ptr, ptr {ReaderField(reader, ReaderBuffer)}");
        var taken = fn.Value($"select i1 {fn.Value($"icmp slt i32 {held}, {max}")}, i32 {held}, i32 {max}");
        Append(fn.Value($"getelementptr i8, ptr {buffer}, i64 {fn.Value($"sext i32 {at} to i64")}"), fn.Value($"sext i32 {taken} to i64"));
        fn.Emit($"store i32 {fn.Value($"add i32 {at}, {taken}")}, ptr {ReaderField(reader, ReaderPosition)}");
        Unlock();
        Succeed(o, taken);

        // none held: a `max` of 8 KiB or more, as Rust's `BufReader` passes such a read by, and one of 0, which waits for no byte, read
        // straight into the list with the lock let go
        fn.Block(empty);
        var straight = fn.Label("straight");
        var fill = fn.Label("fill");
        var large = fn.Value($"icmp sge i32 {max}, {LineBuffer}");
        fn.CondBranch(fn.Value($"or i1 {large}, {fn.Value($"icmp eq i32 {max}, 0")}"), straight, fill);
        fn.Block(straight);
        Unlock();
        var r = Woken(handle, SysCall("ReadFile", $"i32 {HandleDescriptor(handle)}, ptr {fn.Value($"call ptr {StdFunction("BytesEnd")}(ptr {block})")}, i64 {more}"), kinds, read: true);
        FailBelowZero(o, r, HandlePath(handle), kinds: kinds);
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {r})");
        Succeed(o, fn.Value($"trunc i64 {r} to i32"));

        // a smaller one: one read of up to 8 KiB into the buffer, made at the first, under the lock, and at most `max` of it taken; the
        // rest kept for the next read, unless a `Close` marked the handle while the read waited (S5)
        fn.Block(fill);
        var (filled, read) = FillBuffer(o, handle, reader, kinds, Unlock);
        var given = fn.Value($"select i1 {fn.Value($"icmp slt i64 {read}, {more}")}, i64 {read}, i64 {more}");
        Append(filled, given);
        var gave = fn.Value($"trunc i64 {given} to i32");
        KeepUnlessMarked(handle, reader, gave, fn.Value($"trunc i64 {read} to i32"));
        Unlock();
        Succeed(o, gave);
        Leave(o, outer, handle);
    }

    /// <summary>
    /// S24: <c>s.Fill(mut into)</c>, as Rust's <c>BufRead::fill_buf</c> (§12, the owner, 2026-11-03): the bytes the handle's buffer
    /// holds are appended to <c>into</c>, all of them, with no system call, and none is consumed; where it holds none, the buffer is filled
    /// first, as a <c>Read</c> below 8 KiB fills it (<see cref="FillBuffer"/>), and all that read gave is appended and kept from the
    /// buffer's start. The room in the list is made for the bytes appended, as <c>Add</c> grows a list. The count appended is the result,
    /// 0 at the end of the file or the stream (assumed, as a <c>Read</c>'s). A call in flight on the handle (S5), whose fill, where a
    /// <c>Close</c> marked the handle while its read waited, gives all it read and keeps none of it, having no <c>max</c>, where a
    /// <c>Read</c>'s fill gives at most <c>max</c> of it (assumed).
    /// </summary>
    private void EmitFileFill(CallExpression c, CallInfo info, FileOutcome outer, string handle, string kinds)
    {
        var arguments = Arguments(c, info, _ => new ListType(Types.Byte), _ => true, _ => null, cConvention: false, owned: false);
        var place = arguments[0]["ptr ".Length..];
        var o = Enter(outer, handle, kinds);
        var reader = HandleField(handle, ReaderAt);
        var word = ReaderField(reader, ReaderLock);
        void Unlock() => fn.Emit($"call void {UnlockReader()}(ptr {word})");
        fn.Emit($"call void {LockReader()}(ptr {word})");
        var at = fn.Value($"load i32, ptr {ReaderField(reader, ReaderPosition)}");
        var held = fn.Value($"sub i32 {fn.Value($"load i32, ptr {ReaderField(reader, ReaderCount)}")}, {at}");
        var take = fn.Label("fill.held");
        var empty = fn.Label("fill.empty");
        fn.CondBranch(fn.Value($"icmp sgt i32 {held}, 0"), take, empty);

        // the bytes the buffer holds, all of them, with no system call, its position left where it is
        fn.Block(take);
        var count = fn.Value($"sext i32 {held} to i64");
        var from = fn.Value($"getelementptr i8, ptr {fn.Value($"load ptr, ptr {ReaderField(reader, ReaderBuffer)}")}, i64 {fn.Value($"sext i32 {at} to i64")}");
        AppendBytes(Reserve(place, count), from, count);
        Unlock();
        Succeed(o, held);

        // none held: one read of up to 8 KiB into the buffer, all of it appended and kept from the buffer's start
        fn.Block(empty);
        var (filled, read) = FillBuffer(o, handle, reader, kinds, Unlock);
        AppendBytes(Reserve(place, read), filled, read);
        var gave = fn.Value($"trunc i64 {read} to i32");
        KeepUnlessMarked(handle, reader, "0", gave);
        Unlock();
        Succeed(o, gave);
        Leave(o, outer, handle);
    }

    /// <summary>
    /// S24: <c>s.Consume(n)</c>, as Rust's <c>BufRead::consume</c> (§12, the owner, 2026-11-03): the first <c>n</c> bytes the handle's
    /// buffer holds are dropped, under the lock, with no system call. An <c>n</c> below 0 raises <c>index-out-of-range</c>, as a
    /// <c>Read</c>'s <c>max</c> below 0 does, and so does one past the bytes the buffer holds, the bound Rust's <c>consume</c> states held
    /// as C#'s <c>PipeReader.AdvanceTo</c> holds it (§12, the owner, 2026-11-03, issue 110, comment 6615355264, both bounds). A call in
    /// flight on the handle (S5), so that a closed handle gives its error.
    /// </summary>
    private void EmitFileConsume(CallExpression c, CallInfo info, FileOutcome outer, string handle, string kinds)
    {
        var arguments = Arguments(c, info, _ => Types.Int, _ => false, _ => null, cConvention: false, owned: false);
        var n = arguments[0]["i32 ".Length..];
        var negative = fn.Label("negative");
        var counted = fn.Label("counted");
        fn.CondBranch(fn.Value($"icmp slt i32 {n}, 0"), negative, counted);
        fn.Block(negative);
        Raise(ErrorIds.IndexOutOfRange, c.Line);
        fn.Block(counted);
        var o = Enter(outer, handle, kinds);
        var reader = HandleField(handle, ReaderAt);
        var word = ReaderField(reader, ReaderLock);
        fn.Emit($"call void {LockReader()}(ptr {word})");
        var position = ReaderField(reader, ReaderPosition);
        var at = fn.Value($"load i32, ptr {position}");
        var held = fn.Value($"sub i32 {fn.Value($"load i32, ptr {ReaderField(reader, ReaderCount)}")}, {at}");
        var past = fn.Label("consume.past");
        var within = fn.Label("consume.within");
        fn.CondBranch(fn.Value($"icmp sgt i32 {n}, {held}"), past, within);
        fn.Block(past);
        fn.Emit($"call void {UnlockReader()}(ptr {word})");
        Raise(ErrorIds.IndexOutOfRange, c.Line);
        fn.Block(within);
        fn.Emit($"store i32 {fn.Value($"add i32 {at}, {n}")}, ptr {position}");
        fn.Emit($"call void {UnlockReader()}(ptr {word})");
        Succeed(o, null);
        Leave(o, outer, handle);
    }

    /// <summary>
    /// S3, S24: the fill of the handle's empty buffer that a <c>Read</c> below 8 KiB and a <c>Fill</c> make, under the lock the caller
    /// holds: the buffer made at the first fill, then one read of up to <see cref="LineBuffer"/> bytes into it, a socket's woken by a
    /// <c>Close</c> (<see cref="Woken"/>), whose failure lets the lock go (<paramref name="unlock"/>) and stores nothing; the buffer and
    /// the count read, an <c>i64</c>.
    /// </summary>
    private (string Buffer, string Read) FillBuffer(FileOutcome o, string handle, string reader, string kinds, Action unlock)
    {
        var slot = ReaderField(reader, ReaderBuffer);
        var none = fn.Label("unbuffered");
        var made = fn.Label("buffered");
        fn.CondBranch(fn.Value($"icmp eq ptr {fn.Value($"load ptr, ptr {slot}")}, null"), none, made);
        fn.Block(none);
        fn.Emit($"store ptr {fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {LineBuffer})")}, ptr {slot}");
        fn.Branch(made);
        fn.Block(made);
        var filled = fn.Value($"load ptr, ptr {slot}");
        var read = Woken(handle, SysCall("ReadFile", $"i32 {HandleDescriptor(handle)}, ptr {filled}, i64 {LineBuffer}"), kinds, read: true);
        FailBelowZero(o, read, HandlePath(handle), unlock, kinds);
        return (filled, read);
    }

    /// <summary>
    /// S5: the <paramref name="position"/> and the <paramref name="count"/>, <c>i32</c>s, that a fill leaves in the handle's buffer for
    /// the calls after it, unless a <c>Close</c> marked the handle while the fill's read waited, so that the bytes it read go with the
    /// close as the bytes the buffer held do.
    /// </summary>
    private void KeepUnlessMarked(string handle, string reader, string position, string count)
    {
        var kept = fn.Label("kept");
        var done = fn.Label("filled");
        var mark = fn.Value($"and i32 {fn.Value($"load atomic i32, ptr {HandleField(handle, InFlightAt)} monotonic, align 4")}, 1");
        fn.CondBranch(fn.Value($"icmp eq i32 {mark}, 0"), kept, done);
        fn.Block(kept);
        fn.Emit($"store i32 {position}, ptr {ReaderField(reader, ReaderPosition)}");
        fn.Emit($"store i32 {count}, ptr {ReaderField(reader, ReaderCount)}");
        fn.Branch(done);
        fn.Block(done);
    }

    /// <summary><paramref name="count"/> bytes, an <c>i64</c>, at <paramref name="from"/> added at the end of the list of bytes <paramref name="block"/>, for which <see cref="Reserve"/> made room.</summary>
    private void AppendBytes(string block, string from, string count)
    {
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {fn.Value($"call ptr {StdFunction("BytesEnd")}(ptr {block})")}, ptr {from}, i64 {count}, i1 false)");
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {count})");
    }

    /// <summary>S4: <c>f.Write(bytes)</c>, every byte, a write of which the system takes none <c>WriteZero</c>; the kind of a failure as <see cref="EmitFileRead"/> gives it; a call in flight on the handle (S5).</summary>
    private void EmitFileWrite(CallExpression c, CallInfo info, FileOutcome outer, string handle, string kinds)
    {
        var list = Arguments(c, info, _ => new ListType(Types.Byte), _ => false, _ => null, cConvention: false, owned: false).Single()["ptr ".Length..];
        var (items, count) = BytesOf(list);
        var o = Enter(outer, handle, kinds);
        var r = Woken(handle, SysCall("WriteFile", $"i32 {HandleDescriptor(handle)}, ptr {items}, i64 {count}"), kinds, read: false);
        var path = HandlePath(handle);
        FailBelowZero(o, r, path, kinds: kinds);
        WriteZeroBelow(o, r, count, path);
        Succeed(o, null);
        Leave(o, outer, handle);
    }

    /// <summary>
    /// S9: a stream's <c>SetReadTimeout(timeout)</c> or <c>SetWriteTimeout(timeout)</c>, or a listener's <c>SetAcceptTimeout(timeout)</c>:
    /// the runtime's <c>SetTimeout</c> with <c>SO_RCVTIMEO</c> (20), which bounds a read and an <c>accept</c> (socket(7)), or
    /// <c>SO_SNDTIMEO</c> (21), the nanoseconds of the time, and whether there is one, <c>null</c> being no bound; a call in flight on the handle (S5).
    /// </summary>
    private void EmitSetTimeout(CallExpression c, CallInfo info, FileOutcome outer, string handle)
    {
        var nullable = Types.Nullable(Types.Duration);
        var time = Arguments(c, info, _ => nullable, _ => false, _ => null, cConvention: false, owned: false).Single()[(Ir(nullable).Length + 1)..];
        var bounded = fn.Value($"xor i1 {IsCase(Tag(time, nullable), nullable, Types.Null)}, true");
        var nanoseconds = fn.Value($"select i1 {bounded}, i64 {Payload(time, nullable, Types.Duration)}, i64 0");
        var option = info.Kind == CallKind.TcpSetWriteTimeout ? "21" : "20";
        var o = Enter(outer, handle, "ErrorKind");
        var r = SysCall("SetTimeout", $"i32 {HandleDescriptor(handle)}, i32 {option}, i64 {nanoseconds}, i1 {bounded}");
        FailBelowZero(o, r, HandlePath(handle));
        Succeed(o, null);
        Leave(o, outer, handle);
    }

    /// <summary>
    /// S5: <c>f.Close()</c>, the handle marked closed for every holder (<see cref="CloseHandle"/>), so that every later call, a second
    /// <c>Close</c> and a pull among them, gets the system's error of a descriptor that is not open; the descriptor released at once, the
    /// error of that close given, where no call is in flight on the handle, and else by the last call in flight out, a
    /// <paramref name="socket"/> shut down so that a call that blocks on it ends at once.
    /// </summary>
    private void EmitFileClose(FileOutcome o, string handle, bool socket)
    {
        var r = fn.Value($"call i64 {CloseHandle()}(ptr {handle}, i1 {(socket ? "true" : "false")})");
        FailBelowZero(o, r, HandlePath(handle));
        Succeed(o, null);
    }

    /// <summary>
    /// S9: a listener's <c>Accept()</c>, a stream that holds the listener's address on a count of its own, which
    /// <see cref="NewHandle"/> takes, so that the address outlives the listener's block; the kind of a failure is the one the
    /// runtime's function <paramref name="kinds"/> gives (<see cref="FailBelowZero"/>); a call in flight on the listener (S5).
    /// </summary>
    private void EmitAccept(FileOutcome outer, string handle, string kinds)
    {
        var o = Enter(outer, handle, kinds);
        var r = Woken(handle, SysCall("Accept", $"i32 {HandleDescriptor(handle)}"), kinds, read: false);
        var address = HandlePath(handle);
        FailBelowZero(o, r, address, kinds: kinds);
        Succeed(o, NewHandle(fn.Value($"trunc i64 {r} to i32"), address));
        Leave(o, outer, handle);
    }

    /// <summary>M9, M5: <c>list.Clear()</c>, through the place of the list's variable or of the field a path from one ends at.</summary>
    private string? EmitListClear(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        var clear = Helper($"clear.{Lower.Mangle(list)}", "void", ["ptr"], () =>
        {
            // a block other values hold is let go of, and the list holds none (M3); one it alone holds lets go of its items and keeps its room
            var block = fn.Value("load ptr, ptr %a0");
            var shared = fn.Label("shared");
            var own = fn.Label("own");
            fn.CondBranch(fn.Value($"call i1 {StdFunction("ListShared")}(ptr {block})"), shared, own);
            fn.Block(shared);
            Release(block, list);
            fn.Emit("store ptr null, ptr %a0");
            fn.Return(null);
            fn.Block(own);
            if (Counted(list.Element))
            {
                EachItem(block, list.Element, item => Release(item, list.Element));
            }

            fn.Emit($"call void {StdFunction("ListClear")}(ptr {block})");
            fn.Return(null);
        });
        fn.Emit($"call void {clear}(ptr {CollectionPlace(member.Target)})");
        return null;
    }

    // ---- the pieces of a call ------------------------------------------------------------------------

    /// <summary>A call of the runtime of Linux that gives an <c>i64</c>.</summary>
    private string SysCall(string function, string arguments) => fn.Value($"call i64 {SysFunction(function)}({arguments})");

    /// <summary>
    /// The path as C reads it, which the caller gives back with <see cref="FreePath"/>; a path that holds a NUL byte ends the call with
    /// <c>InvalidInput</c> and the number 0 with <paramref name="named"/>, the call's first path, after <paramref name="before"/>.
    /// </summary>
    private string CPath(FileOutcome o, string path, string named, Action? before)
    {
        var copy = fn.Value($"call ptr {RuntimeFunction("CPath")}(%kz.str {path})");
        var nul = fn.Label("path.nul");
        var ok = fn.Label("path");
        fn.CondBranch(fn.Value($"icmp eq ptr {copy}, null"), nul, ok);
        fn.Block(nul);
        before?.Invoke();
        Fail(o, KindValue(o.Error, "InvalidInput"), "0", named);
        fn.Block(ok);
        return copy;
    }

    private void FreePath(string copy, string path) => fn.Emit($"call void {RuntimeFunction("FreePath")}(ptr {copy}, %kz.str {path})");

    /// <summary>
    /// A failure, a result below 0, ends the call with its <c>IoError</c> after <paramref name="before"/>, its kind the one the runtime's
    /// function <paramref name="kinds"/> gives, <c>ErrorKind</c>, or <c>SocketErrorKind</c> for a stream's read and write and a listener's <c>Accept</c> (S9); the code
    /// goes on with a result of 0 or more.
    /// </summary>
    private void FailBelowZero(FileOutcome o, string result, string path, Action? before = null, string kinds = "ErrorKind")
    {
        var failed = fn.Label("io.failed");
        var ok = fn.Label("io.ok");
        fn.CondBranch(fn.Value($"icmp slt i64 {result}, 0"), failed, ok);
        fn.Block(failed);
        before?.Invoke();
        Fail(o, fn.Value($"call {Ir(Layout.Fields(o.Error)[0])} {SysFunction(kinds)}(i64 {result})"), fn.Value($"call i32 {SysFunction("ErrorCode")}(i64 {result})"), path);
        fn.Block(ok);
    }

    /// <summary>S4: fewer bytes written than <paramref name="count"/>, which a write that took none leaves, ends the call with <c>WriteZero</c> and the number 0.</summary>
    private void WriteZeroBelow(FileOutcome o, string written, string count, string path)
    {
        var failed = fn.Label("write.zero");
        var ok = fn.Label("written");
        fn.CondBranch(fn.Value($"icmp slt i64 {written}, {count}"), failed, ok);
        fn.Block(failed);
        Fail(o, KindValue(o.Error, "WriteZero"), "0", path);
        fn.Block(ok);
    }

    /// <summary>The success of the call, its value null for <c>void</c>.</summary>
    private void Succeed(FileOutcome o, string? value)
    {
        fn.Emit($"store {Ir(o.Union)} {MakeUnion(o.Union, o.Union.Cases[0], value)}, ptr {o.Slot}");
        fn.Branch(o.Done);
    }

    /// <summary>The case <c>IoError</c> of the kind, the number and the path, of which it holds a count (S1).</summary>
    private void Fail(FileOutcome o, string kind, string code, string path)
    {
        Retain(path, Types.String);
        var error = Ir(o.Error);
        var withKind = fn.Value($"insertvalue {error} undef, {Ir(Layout.Fields(o.Error)[0])} {kind}, 0");
        var withCode = fn.Value($"insertvalue {error} {withKind}, i32 {code}, 1");
        var value = fn.Value($"insertvalue {error} {withCode}, %kz.str {path}, 2");
        fn.Emit($"store {Ir(o.Union)} {MakeUnion(o.Union, o.Error, value)}, ptr {o.Slot}");
        fn.Branch(o.Done);
    }

    /// <summary>The stored value of the kind of that name, for an error that is not the system's (S1).</summary>
    private static string KindValue(DataType error, string name)
    {
        var kind = (EnumType)Layout.Fields(error)[0];
        return IntegerText(Lower.EnumStored(kind.Symbol, kind.Symbol.IndexOf(name)), kind);
    }

    /// <summary>The address of the first byte of a list of bytes and their count, an <c>i64</c>.</summary>
    private (string Items, string Count) BytesOf(string list) =>
        (fn.Value($"call ptr {StdFunction("ListItems")}(ptr {list})"), fn.Value($"zext i32 {ListCount(list)} to i64"));

    /// <summary>
    /// The room for <paramref name="more"/> bytes after the items of the list of bytes at <paramref name="place"/>, in a block no other
    /// value holds, stored back there: the block. A count past the largest <c>int</c> and a block the system refuses raise
    /// <c>allocation-failed</c>, as <c>Add</c> does (M9).
    /// </summary>
    private string Reserve(string place, string more)
    {
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var block = fn.Value($"call ptr {StdFunction("BytesReserve")}(ptr {fn.Value($"load ptr, ptr {place}")}, i64 {more}, ptr {asked})");
        Refused(fn.Value($"icmp sgt i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        fn.Emit($"store ptr {block}, ptr {place}");
        return block;
    }

    /// <summary>
    /// S6: the helper that reads the whole file at the C path in <c>%a0</c> into the list of bytes at <c>%a1</c>, as Rust's
    /// <c>fs::read</c> reads it: the room for the size the system names for the open file (<c>DescriptorSize</c>, <c>BytesHint</c>),
    /// made at once, as <c>fs::read</c> reserves the size the file's metadata gives, then the piece <c>BytesPiece</c> gives, read again
    /// while a read gives bytes, the file closed after, its error ignored. A list full at that size, or at the largest <c>int</c> of
    /// items, reads one byte more, as Rust's <c>read_to_end</c> reads a probe where its <c>Vec</c> is full at the room it started with:
    /// none is the end of the file, and one is added after room for it is made, which grows the list past that size as
    /// <c>read_to_end</c> grows its <c>Vec</c> and, past the limit, raises <c>allocation-failed</c> as an <c>Add</c> past it does (M9).
    /// 0, or the failure of the open or of a read.
    /// </summary>
    private string ReadAll() => Helper("file.read.all", "i64", ["ptr", "ptr"], () =>
    {
        var opened = SysCall("OpenFile", "ptr %a0, i1 false");
        var failed = fn.Label("failed");
        var open = fn.Label("open");
        fn.CondBranch(fn.Value($"icmp slt i64 {opened}, 0"), failed, open);
        fn.Block(failed);
        fn.Return($"i64 {opened}");
        fn.Block(open);
        var fd = fn.Value($"trunc i64 {opened} to i32");
        var result = fn.Alloca("i64", "result");
        var probe = fn.Alloca("i8", "probe");
        var hint = fn.Value($"call i64 {StdFunction("BytesHint")}(i64 {SysCall("DescriptorSize", $"i32 {fd}")})");
        var sized = fn.Label("sized");
        var loop = fn.Label("read");
        var end = fn.Label("end");
        fn.CondBranch(fn.Value($"icmp sgt i64 {hint}, 0"), sized, loop);
        fn.Block(sized);
        Reserve("%a1", hint);
        fn.Branch(loop);
        fn.Block(loop);
        var more = fn.Value($"call i64 {StdFunction("BytesPiece")}(ptr {fn.Value("load ptr, ptr %a1")}, i64 {ReadAllChunk}, i64 {hint})");
        var full = fn.Label("full");
        var room = fn.Label("room");
        fn.CondBranch(fn.Value($"icmp eq i64 {more}, 0"), full, room);
        fn.Block(full);
        var probed = SysCall("ReadFile", $"i32 {fd}, ptr {probe}, i64 1");
        fn.Emit($"store i64 {probed}, ptr {result}");
        var past = fn.Label("past");
        fn.CondBranch(fn.Value($"icmp sle i64 {probed}, 0"), end, past);
        fn.Block(past);
        fn.Emit($"call void {StdFunction("ListAppend")}(ptr {Reserve("%a1", "1")}, ptr {probe}, i64 1, i64 1)");
        fn.Branch(loop);
        fn.Block(room);
        var block = Reserve("%a1", more);
        var r = SysCall("ReadFile", $"i32 {fd}, ptr {fn.Value($"call ptr {StdFunction("BytesEnd")}(ptr {block})")}, i64 {more}");
        fn.Emit($"store i64 {r}, ptr {result}");
        var read = fn.Label("added");
        fn.CondBranch(fn.Value($"icmp sle i64 {r}, 0"), end, read);
        fn.Block(read);
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {r})");
        fn.Branch(loop);
        fn.Block(end);
        SysCall("CloseFile", $"i32 {fd}");
        fn.Return($"i64 {fn.Value($"load i64, ptr {result}")}");
    });

    // ---- the lines of a file -------------------------------------------------------------------------

    /// <summary>
    /// S16: <c>for line in f.Lines() { } else { ... }</c>: the loop over the reader the file's handle holds, which holds the file while it
    /// runs, so that it pulls from the file it started with whatever its block assigns; the case <c>IoError</c> names the file's path.
    /// </summary>
    private void EmitForInLines(ForInStatement f, CallExpression call)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(call.Line, NotOnWindows);
        }

        var source = ((MemberAccess)Unwrap(call.Callee)).Target;
        var handle = Owned(source);
        regions[^1].Add((handle, TypeOf(source))); // the statement's end, a `return` or a `break` lets go of it
        EmitLinesLoop(f, (UnionType)Sub(Model.CallOf(call).Result), handle, HandleField(handle, 8), HandleField(handle, ReaderAt), HandlePath(handle));
    }

    /// <summary>
    /// S16: a loop over the lines of the descriptor at <paramref name="descriptor"/> through the reader's state at <paramref name="reader"/>
    /// (<see cref="ReaderAt"/>), whose outcome is <paramref name="union"/>, <c>void</c> with the cases <c>IoError</c> and <c>InvalidUtf8</c>.
    /// Each round pulls a line (<see cref="PullLine"/>), which the block's end, a <c>break</c>, a <c>continue</c> and a <c>return</c> let go
    /// of. A pull that fails makes the case of the outcome, an <c>IoError</c> naming <paramref name="path"/>, which the arms of the loop's
    /// <c>else</c> take as a call's (O5), and a case no arm takes leaves the function at the loop's line (O2, O3); after an arm the code goes
    /// on after the loop. Each pull is a call in flight on <paramref name="handle"/> (S5), and one on a handle a <c>Close</c> marked fails as
    /// a read of a descriptor that is not open does.
    /// </summary>
    private void EmitLinesLoop(ForInStatement f, UnionType union, string handle, string descriptor, string reader, string path)
    {
        var outcome = new FileOutcome(union, (DataType)union.Cases.Single(IsIoError), fn.Alloca(Ir(union), "lines"), fn.Label("lines.failed"));
        var slot = fn.Alloca("%kz.str", f.Variable);
        slots[Model.DeclarationOf(f)] = slot;
        var at = fn.Alloca("i32", "at");
        var pulled = fn.Alloca("i64", "pulled");
        var pull = fn.Label("pull");
        var open = fn.Label("pull.open");
        var closed = fn.Label("pull.closed");
        var done = fn.Label("pulled");
        var body = fn.Label("for");
        var invalid = fn.Label("not.utf8");
        var exit = fn.Label("endfor");
        fn.Branch(pull);
        fn.Block(pull);
        fn.Emit($"store %kz.str {StringValue(string.Empty)}, ptr {slot}");
        fn.CondBranch(fn.Value($"call i1 {EnterHandle()}(ptr {handle})"), open, closed);
        fn.Block(open);
        fn.Emit($"store i64 {fn.Value($"call i64 {PullLine()}(ptr {descriptor}, ptr {reader}, ptr {slot}, ptr {at})")}, ptr {pulled}");
        fn.Emit($"call void {LeaveHandle()}(ptr {handle})");
        fn.Branch(done);
        fn.Block(closed);
        fn.Emit($"store i64 {ClosedResult}, ptr {pulled}");
        fn.Branch(done);
        fn.Block(done);
        var r = fn.Value($"load i64, ptr {pulled}");
        FailBelowZero(outcome, r, path);
        fn.Terminate($"switch i64 {r}, label %{invalid} [ i64 1, label %{body} i64 0, label %{exit} ]");
        fn.Block(body);
        scopes.Add([(slot, Types.String)]); // the round's line, which a `break` and a `continue` let go of with the block's variables
        try
        {
            loops.Push((exit, pull, regions.Count, scopes.Count - 1));
            EmitBlock(f.Body);
            loops.Pop();
            if (!fn.Terminated)
            {
                LetGoOf(scopes[^1]);
                fn.Branch(pull);
            }
        }
        finally
        {
            scopes.RemoveAt(scopes.Count - 1);
        }

        fn.Block(invalid);
        var data = union.Cases.Single(c => c is DataType { Symbol.Name: "InvalidUtf8" });
        var position = fn.Value($"insertvalue {Ir(data)} undef, i32 {fn.Value($"load i32, ptr {at}")}, 0");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, data, position)}, ptr {outcome.Slot}");
        fn.Branch(outcome.Done);
        fn.Block(outcome.Done);
        ElseArms(f.Arms ?? [], fn.Value($"load {Ir(union)}, ptr {outcome.Slot}"), union, null, exit, f.Line);
        fn.Block(exit);
    }

    /// <summary>
    /// S16: the helper that pulls the next line of the descriptor at <c>%a0</c>, through
    /// the reader's state at <c>%a1</c> (<see cref="ReaderAt"/>) into the string at <c>%a2</c>, under the reader's lock, as Rust's
    /// <c>BufRead::read_line</c> reads one through a <c>BufReader</c>. The buffer, made at the first pull, is read again
    /// where every byte of it was taken, <see cref="LineBuffer"/> bytes at a time; the next <c>\n</c> is looked for with the standard
    /// library's <c>Find</c>, as Rust's <c>read_until</c> looks for it with <c>memchr</c>, and the bytes up to it are taken, gathered in a
    /// list of bytes, which a line that runs past the buffer grows as <c>read_until</c> grows its <c>Vec</c>. The <c>\n</c> and a
    /// <c>\r</c> in front of it are not part of the line, and the bytes after the last <c>\n</c> are the last line. The line's string
    /// takes over the list's block, as <c>string.FromUtf8</c> shares it (T15). 1 for a line; 2 for bytes that are not UTF-8, which are
    /// taken, with the position of their first sequence that is not put at <c>%a3</c>; 0 at the end of the file where no byte is left;
    /// the failure of a read, which gives up the bytes gathered, as Rust's <c>lines()</c> gives no part of such a line.
    /// </summary>
    private string PullLine() => Helper("lines.pull", "i64", ["ptr", "ptr", "ptr", "ptr"], () =>
    {
        var bytes = new ListType(Types.Byte);
        var word = ReaderField("%a1", ReaderLock);
        fn.Emit($"call void {LockReader()}(ptr {word})");
        var place = ReaderField("%a1", ReaderBuffer);
        var none = fn.Label("unbuffered");
        var made = fn.Label("buffered");
        fn.CondBranch(fn.Value($"icmp eq ptr {fn.Value($"load ptr, ptr {place}")}, null"), none, made);
        fn.Block(none);
        fn.Emit($"store ptr {fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {LineBuffer})")}, ptr {place}");
        fn.Branch(made);
        fn.Block(made);
        var buffer = fn.Value($"load ptr, ptr {place}");
        var gathered = fn.Alloca("ptr", "gathered");
        fn.Emit($"store ptr null, ptr {gathered}");
        var result = fn.Alloca("i64", "result");
        var ended = fn.Alloca("i1", "ended"); // whether a `\n` ended the line, so that a `\r` in front of it is not part of it
        var positionField = ReaderField("%a1", ReaderPosition);
        var countField = ReaderField("%a1", ReaderCount);
        var look = fn.Label("look");
        var search = fn.Label("search");
        var refill = fn.Label("refill");
        var line = fn.Label("line");
        var unlock = fn.Label("unlock");
        fn.Branch(look);
        fn.Block(look);
        var position = fn.Value($"load i32, ptr {positionField}");
        var count = fn.Value($"load i32, ptr {countField}");
        fn.CondBranch(fn.Value($"icmp slt i32 {position}, {count}"), search, refill);

        // every byte of the buffer taken: it is read again
        fn.Block(refill);
        var r = SysCall("ReadFile", $"i32 {fn.Value("load i32, ptr %a0")}, ptr {buffer}, i64 {LineBuffer}");
        var failed = fn.Label("read.failed");
        var read = fn.Label("read");
        fn.CondBranch(fn.Value($"icmp slt i64 {r}, 0"), failed, read);
        fn.Block(failed);
        Release(fn.Value($"load ptr, ptr {gathered}"), bytes);
        fn.Emit($"store i64 {r}, ptr {result}");
        fn.Branch(unlock);
        fn.Block(read);
        fn.Emit($"store i32 0, ptr {positionField}");
        fn.Emit($"store i32 {fn.Value($"trunc i64 {r} to i32")}, ptr {countField}");
        var end = fn.Label("end");
        fn.CondBranch(fn.Value($"icmp eq i64 {r}, 0"), end, look);

        // the end of the file: the bytes gathered are the last line, and where none were, no line is left
        fn.Block(end);
        var last = fn.Label("last");
        var nothing = fn.Label("nothing");
        fn.CondBranch(fn.Value($"icmp eq ptr {fn.Value($"load ptr, ptr {gathered}")}, null"), nothing, last);
        fn.Block(nothing);
        fn.Emit($"store i64 0, ptr {result}");
        fn.Branch(unlock);
        fn.Block(last);
        fn.Emit($"store i1 false, ptr {ended}");
        fn.Branch(line);

        // the bytes up to the next `\n`, or every byte of the buffer where it holds none
        fn.Block(search);
        var from = fn.Value($"getelementptr i8, ptr {buffer}, i64 {fn.Value($"sext i32 {position} to i64")}");
        var held = fn.Value($"sext i32 {fn.Value($"sub i32 {count}, {position}")} to i64");
        var (newline, _) = module.String("\n");
        var found = fn.Value($"call i64 {StdFunction("Find")}(ptr {from}, i64 {held}, ptr {newline}, i64 1)");
        var whole = fn.Label("whole");
        var part = fn.Label("part");
        fn.CondBranch(fn.Value($"icmp slt i64 {found}, 0"), part, whole);
        fn.Block(part);
        Gather(gathered, from, held);
        fn.Emit($"store i32 {count}, ptr {positionField}");
        fn.Branch(look);
        fn.Block(whole);
        Gather(gathered, from, found);
        fn.Emit($"store i32 {fn.Value($"add i32 {position}, {fn.Value($"add i32 {fn.Value($"trunc i64 {found} to i32")}, 1")}")}, ptr {positionField}");
        fn.Emit($"store i1 true, ptr {ended}");
        fn.Branch(line);

        // the line: its bytes without a `\r` in front of the `\n` that ended it, as a string, which an empty line needs no block for: a
        // gathered list is null until a piece adds a byte, since `BytesReserve` gives null for a list without a block and 0 more bytes
        fn.Block(line);
        var block = fn.Value($"load ptr, ptr {gathered}");
        var some = fn.Label("some");
        var empty = fn.Label("empty");
        fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), empty, some);
        fn.Block(empty);
        fn.Emit($"store i64 1, ptr {result}");
        fn.Branch(unlock);
        fn.Block(some);
        var items = fn.Value($"call ptr {StdFunction("ListItems")}(ptr {block})");
        var taken = fn.Value($"zext i32 {ListCount(block)} to i64");
        var lastByte = fn.Value($"load i8, ptr {fn.Value($"getelementptr i8, ptr {items}, i64 {fn.Value($"sub i64 {taken}, 1")}")}");
        var carriage = fn.Value($"and i1 {fn.Value($"load i1, ptr {ended}")}, {fn.Value($"icmp eq i8 {lastByte}, 13")}");
        var length = fn.Value($"select i1 {carriage}, i64 {fn.Value($"sub i64 {taken}, 1")}, i64 {taken}");
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {fn.Value($"sub i64 {length}, {taken}")})");
        var invalidAt = fn.Value($"call i64 {RuntimeFunction("Utf8Invalid")}(ptr {items}, i64 {length})");
        var valid = fn.Label("utf8");
        var bad = fn.Label("not.utf8");
        fn.CondBranch(fn.Value($"icmp slt i64 {invalidAt}, 0"), valid, bad);
        fn.Block(valid);
        var withPointer = fn.Value($"insertvalue %kz.str undef, ptr {items}, 0");
        fn.Emit($"store %kz.str {fn.Value($"insertvalue %kz.str {withPointer}, i64 {fn.Value($"or i64 {length}, -9223372036854775808")}, 1")}, ptr %a2");
        fn.Emit($"store i64 1, ptr {result}");
        fn.Branch(unlock);
        fn.Block(bad);
        fn.Emit($"store i32 {fn.Value($"trunc i64 {invalidAt} to i32")}, ptr %a3");
        Release(block, bytes);
        fn.Emit($"store i64 2, ptr {result}");
        fn.Branch(unlock);

        fn.Block(unlock);
        fn.Emit($"call void {UnlockReader()}(ptr {word})");
        fn.Return($"i64 {fn.Value($"load i64, ptr {result}")}");
    });

    /// <summary>The <paramref name="count"/> bytes at <paramref name="from"/> added to the list of bytes at <paramref name="gathered"/>, which has room made for them as <c>Add</c> makes it (M9).</summary>
    private void Gather(string gathered, string from, string count)
    {
        var block = Reserve(gathered, count);
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {fn.Value($"call ptr {StdFunction("BytesEnd")}(ptr {block})")}, ptr {from}, i64 {count}, i1 false)");
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {count})");
    }

    /// <summary>
    /// S16: the helper that takes the lock whose word is at <c>%a0</c>, as Rust's futex mutex takes it: 0 to 1 where it is free, and where it
    /// is not, the word set to 2, that a thread waits, and the thread put to sleep on it until a swap finds it free (the owner, 2026-11-01,
    /// "Lock per pull"). Rust's spin of up to 100 reads before the first sleep is left out.
    /// </summary>
    private string LockReader() => Helper("lock.reader", "void", ["ptr"], () =>
    {
        var free = fn.Value("cmpxchg ptr %a0, i32 0, i32 1 acquire monotonic");
        var locked = fn.Label("locked");
        var contended = fn.Label("contended");
        var wait = fn.Label("wait");
        fn.CondBranch(fn.Value($"extractvalue {{ i32, i1 }} {free}, 1"), locked, contended);
        fn.Block(contended);
        var before = fn.Value("atomicrmw xchg ptr %a0, i32 2 acquire");
        fn.CondBranch(fn.Value($"icmp eq i32 {before}, 0"), locked, wait);
        fn.Block(wait);
        SysCall("LockWait", "ptr %a0, i32 2");
        fn.Branch(contended);
        fn.Block(locked);
        fn.Return(null);
    });

    /// <summary>S16: the helper that lets go of the lock whose word is at <c>%a0</c>, as Rust's futex mutex lets go of it: the word set to 0, and where it was 2, one thread that waits woken.</summary>
    private string UnlockReader() => Helper("unlock.reader", "void", ["ptr"], () =>
    {
        var before = fn.Value("atomicrmw xchg ptr %a0, i32 0 release");
        var wake = fn.Label("wake");
        var done = fn.Label("unlocked");
        fn.CondBranch(fn.Value($"icmp eq i32 {before}, 2"), wake, done);
        fn.Block(wake);
        SysCall("LockWake", "ptr %a0");
        fn.Branch(done);
        fn.Block(done);
        fn.Return(null);
    });

    // ---- the handle --------------------------------------------------------------------------------

    private string HandleField(string handle, int offset) => fn.Value($"getelementptr i8, ptr {handle}, i64 {Number(offset)}");

    /// <summary>S16: the address of a field of the reader's state at <paramref name="reader"/> (<see cref="ReaderAt"/>).</summary>
    private string ReaderField(string reader, int offset) => offset == 0 ? reader : fn.Value($"getelementptr i8, ptr {reader}, i64 {Number(offset)}");

    private string HandleDescriptor(string handle) => fn.Value($"load i32, ptr {HandleField(handle, 8)}");

    private string HandlePath(string handle) => fn.Value($"load %kz.str, ptr {HandleField(handle, 16)}");

    /// <summary>A new handle's block, held by one value, for the descriptor and the path, of which it holds a count (S5).</summary>
    private string NewHandle(string fd, string path)
    {
        var handle = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {HandleSize})");
        fn.Emit($"store i64 1, ptr {handle}");
        fn.Emit($"store i32 {fd}, ptr {HandleField(handle, 8)}");
        Retain(path, Types.String);
        fn.Emit($"store %kz.str {path}, ptr {HandleField(handle, 16)}");
        var reader = HandleField(handle, ReaderAt);
        fn.Emit($"store ptr null, ptr {ReaderField(reader, ReaderBuffer)}");
        fn.Emit($"store i64 0, ptr {ReaderField(reader, ReaderPosition)}"); // the position and the count of the buffer's bytes
        fn.Emit($"store i32 0, ptr {ReaderField(reader, ReaderLock)}"); // the lock, free
        fn.Emit($"store i32 0, ptr {HandleField(handle, InFlightAt)}"); // no call in flight, and no mark
        return handle;
    }

    /// <summary>
    /// S5: the call on <paramref name="handle"/> that follows taken as a call in flight (<see cref="EnterHandle"/>); on a handle a
    /// <c>Close</c> marked, the call ends with the error <see cref="ClosedResult"/> gives, its kind the one the runtime's function
    /// <paramref name="kinds"/> gives, as a failure of the system does (<see cref="FailBelowZero"/>). The outcome the call ends through,
    /// whose end <see cref="Leave"/> emits.
    /// </summary>
    private FileOutcome Enter(FileOutcome o, string handle, string kinds)
    {
        var open = fn.Label("handle.open");
        var closed = fn.Label("handle.closed");
        fn.CondBranch(fn.Value($"call i1 {EnterHandle()}(ptr {handle})"), open, closed);
        fn.Block(closed);
        Fail(o, fn.Value($"call {Ir(Layout.Fields(o.Error)[0])} {SysFunction(kinds)}(i64 {ClosedResult})"), fn.Value($"call i32 {SysFunction("ErrorCode")}(i64 {ClosedResult})"), HandlePath(handle));
        fn.Block(open);
        return o with { Done = fn.Label("handle.leave") };
    }

    /// <summary>S5: the end of a call <see cref="Enter"/> took, where every way out of <paramref name="inner"/> leaves the handle (<see cref="LeaveHandle"/>) before <paramref name="outer"/>'s end.</summary>
    private void Leave(FileOutcome inner, FileOutcome outer, string handle)
    {
        fn.Block(inner.Done);
        fn.Emit($"call void {LeaveHandle()}(ptr {handle})");
        fn.Branch(outer.Done);
    }

    /// <summary>
    /// S5: the helper that takes a call in flight on the handle at <c>%a0</c>, 2 added to its word (<see cref="InFlightAt"/>) by a
    /// compare-and-swap, which fails and is tried again where another call changed the word, as Rust's <c>Weak::upgrade</c> takes a count;
    /// false, and no count, where a <c>Close</c> marked it, so that the word, once marked, only falls, and reaches the mark alone once.
    /// </summary>
    private string EnterHandle() => Helper("enter.handle", "i1", ["ptr"], () =>
    {
        var word = HandleField("%a0", InFlightAt);
        var seen = fn.Alloca("i32", "seen");
        fn.Emit($"store i32 {fn.Value($"load atomic i32, ptr {word} monotonic, align 4")}, ptr {seen}");
        var look = fn.Label("look");
        var swap = fn.Label("swap");
        var marked = fn.Label("marked");
        var entered = fn.Label("entered");
        fn.Branch(look);
        fn.Block(look);
        var before = fn.Value($"load i32, ptr {seen}");
        fn.CondBranch(fn.Value($"icmp ne i32 {fn.Value($"and i32 {before}, 1")}, 0"), marked, swap);
        fn.Block(swap);
        var swapped = fn.Value($"cmpxchg ptr {word}, i32 {before}, i32 {fn.Value($"add i32 {before}, 2")} acquire monotonic");
        fn.Emit($"store i32 {fn.Value($"extractvalue {{ i32, i1 }} {swapped}, 0")}, ptr {seen}");
        fn.CondBranch(fn.Value($"extractvalue {{ i32, i1 }} {swapped}, 1"), entered, look);
        fn.Block(entered);
        fn.Return("i1 true");
        fn.Block(marked);
        fn.Return("i1 false");
    });

    /// <summary>
    /// S5: the helper that ends a call in flight on the handle at <c>%a0</c>, 2 taken from its word; the last call out of a handle a
    /// <c>Close</c> marked, which leaves the mark alone, releases the descriptor, ignoring an error of that close, as the last clone of
    /// Rust's <c>Arc&lt;File&gt;</c> drops the file and ignores it.
    /// </summary>
    private string LeaveHandle() => Helper("leave.handle", "void", ["ptr"], () =>
    {
        var before = fn.Value($"atomicrmw sub ptr {HandleField("%a0", InFlightAt)}, i32 2 acq_rel");
        var last = fn.Label("last");
        var done = fn.Label("left");
        fn.CondBranch(fn.Value($"icmp eq i32 {before}, 3"), last, done);
        fn.Block(last);
        SysCall("CloseFile", $"i32 {HandleDescriptor("%a0")}");
        fn.Branch(done);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// S5: the helper of <c>Close</c> on the handle at <c>%a0</c>: the mark set in its word for every holder by a compare-and-swap, which
    /// is tried again where a call changed the word; <see cref="ClosedResult"/> where a <c>Close</c> marked it before, so that of two that
    /// race one wins; the close's result where no call is in flight, the descriptor released at once; and 0 where calls are in flight, the
    /// last of which releases it (<see cref="LeaveHandle"/>), a socket, where <c>%a1</c> is true, shut down first, so that a call that
    /// blocks on it ends at once and gets the closed handle's error (<see cref="Woken"/>), as Go's close wakes the calls that wait (the
    /// owner, 2026-11-02, against a call that waits for its bound), while the <c>Close</c> holds a count of its own, taken with the mark
    /// and given back through <see cref="LeaveHandle"/> once the shutdown is made, so that no call out releases the descriptor, and the
    /// system gives its number to another file, before the shutdown reaches it (the owner, 2026-11-02, on the review's finding, #39); the
    /// error of the close the last call makes is dropped (the owner, 2026-11-02, as Rust's last <c>Arc&lt;File&gt;</c> drops it).
    /// </summary>
    private string CloseHandle() => Helper("close.handle", "i64", ["ptr", "i1"], () =>
    {
        var word = HandleField("%a0", InFlightAt);
        var seen = fn.Alloca("i32", "seen");
        fn.Emit($"store i32 {fn.Value($"load atomic i32, ptr {word} monotonic, align 4")}, ptr {seen}");
        var look = fn.Label("look");
        var marked = fn.Label("marked");
        var swap = fn.Label("swap");
        var swapped = fn.Label("swapped");
        var idle = fn.Label("idle");
        var busy = fn.Label("busy");
        fn.Branch(look);
        fn.Block(look);
        var before = fn.Value($"load i32, ptr {seen}");
        fn.CondBranch(fn.Value($"icmp ne i32 {fn.Value($"and i32 {before}, 1")}, 0"), marked, swap);
        fn.Block(swap);
        // the mark, and on a socket with calls in flight a count of the `Close`'s own, held across its shutdown
        var shuts = fn.Value($"and i1 %a1, {fn.Value($"icmp ne i32 {before}, 0")}");
        var own = fn.Value($"select i1 {shuts}, i32 3, i32 1");
        var result = fn.Value($"cmpxchg ptr {word}, i32 {before}, i32 {fn.Value($"add i32 {before}, {own}")} acq_rel monotonic");
        fn.Emit($"store i32 {fn.Value($"extractvalue {{ i32, i1 }} {result}, 0")}, ptr {seen}");
        fn.CondBranch(fn.Value($"extractvalue {{ i32, i1 }} {result}, 1"), swapped, look);
        fn.Block(swapped);
        fn.CondBranch(fn.Value($"icmp eq i32 {before}, 0"), idle, busy);
        fn.Block(idle);
        fn.Return($"i64 {SysCall("CloseFile", $"i32 {HandleDescriptor("%a0")}")}");
        fn.Block(busy);
        var shut = fn.Label("shut");
        var deferred = fn.Label("deferred");
        fn.CondBranch("%a1", shut, deferred);
        fn.Block(shut);
        SysCall("ShutdownSocket", $"i32 {HandleDescriptor("%a0")}");
        fn.Emit($"call void {LeaveHandle()}(ptr %a0)");
        fn.Branch(deferred);
        fn.Block(deferred);
        fn.Return("i64 0");
        fn.Block(marked);
        fn.Return($"i64 {ClosedResult}");
    });

    /// <summary>
    /// S5, S9: the result <paramref name="r"/> of a call on a socket that blocks, a read where <paramref name="read"/> holds, a write or an
    /// <c>Accept</c>, which the runtime's function <see cref="SocketKinds"/> reads; a failure, or a read of no byte, on a handle a
    /// <c>Close</c> marked while the call was in flight is the shutdown of that <c>Close</c>, and gives the closed handle's error,
    /// <see cref="ClosedResult"/> (the owner, 2026-11-02, "a blocked `Accept` or `Read` returns with the closed-handle error"); a call
    /// on a file is given back as it is.
    /// </summary>
    private string Woken(string handle, string r, string kinds, bool read) =>
        kinds != SocketKinds ? r : fn.Value($"call i64 {WokenSocket()}(ptr {handle}, i64 {r}, i1 {(read ? "true" : "false")})");

    /// <summary>S5, S9: the helper of <see cref="Woken"/>: the result in <c>%a1</c> of a call on the socket at <c>%a0</c>, a read where <c>%a2</c> holds.</summary>
    private string WokenSocket() => Helper("woken.socket", "i64", ["ptr", "i64", "i1"], () =>
    {
        var none = fn.Value($"and i1 %a2, {fn.Value("icmp eq i64 %a1, 0")}");
        var ended = fn.Value($"or i1 {fn.Value("icmp slt i64 %a1, 0")}, {none}");
        var check = fn.Label("check");
        var kept = fn.Label("kept");
        fn.CondBranch(ended, check, kept);
        fn.Block(check);
        var word = fn.Value($"load atomic i32, ptr {HandleField("%a0", InFlightAt)} monotonic, align 4");
        var marked = fn.Value($"icmp ne i32 {fn.Value($"and i32 {word}, 1")}, 0");
        fn.Return($"i64 {fn.Value($"select i1 {marked}, i64 {ClosedResult}, i64 %a1")}");
        fn.Block(kept);
        fn.Return("i64 %a1");
    });

    /// <summary>One more value holds the handle.</summary>
    private string RetainHandle() => Helper("retain.File", "void", ["ptr"], () =>
    {
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value("load i64, ptr %a0")}, 1")}, ptr %a0");
        fn.Return(null);
    });

    /// <summary>
    /// One value less holds the handle; the last closes a descriptor no <c>Close</c> marked, ignoring the close's error, lets go of the
    /// path and frees the block (S5). No call is in flight on the handle then: a call holds its handle through a value, and no handle
    /// reaches another thread, a pointer to it being <c>counted-pointee</c> (R10).
    /// </summary>
    private string ReleaseHandle() => Helper("release.File", "void", ["ptr"], () =>
    {
        var count = fn.Value($"sub i64 {fn.Value("load i64, ptr %a0")}, 1");
        fn.Emit($"store i64 {count}, ptr %a0");
        var last = fn.Label("last");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {count}, 0"), last, held);
        fn.Block(last);
        if (target != Target.Linux)
        {
            fn.Unreachable(); // no file exists in a build for Windows: every call that makes one is `not supported` there (S11)
        }
        else
        {
            var word = fn.Value($"load i32, ptr {HandleField("%a0", InFlightAt)}");
            var open = fn.Label("open");
            var closed = fn.Label("closed");
            fn.CondBranch(fn.Value($"icmp eq i32 {fn.Value($"and i32 {word}, 1")}, 0"), open, closed);
            fn.Block(open);
            SysCall("CloseFile", $"i32 {HandleDescriptor("%a0")}");
            fn.Branch(closed);
            fn.Block(closed);
            Release(HandlePath("%a0"), Types.String);
            var buffer = fn.Value($"load ptr, ptr {ReaderField(HandleField("%a0", ReaderAt), ReaderBuffer)}");
            var buffered = fn.Label("buffered");
            var unbuffered = fn.Label("unbuffered");
            fn.CondBranch(fn.Value($"icmp ne ptr {buffer}, null"), buffered, unbuffered);
            fn.Block(buffered);
            fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {LineBuffer})");
            fn.Branch(unbuffered);
            fn.Block(unbuffered);
            fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {HandleSize})");
            fn.Return(null);
        }

        fn.Block(held);
        fn.Return(null);
    });

    // ---- the text of an error ------------------------------------------------------------------------

    /// <summary>
    /// S1: in the helper of the text of an <c>IoError</c> in <c>%a0</c>: the words of its kind, its name's words in lower case, a colon and
    /// its path, <c>not found: data/x.bin</c>, or the words alone where the path is empty; the words of <c>Other</c> with the system's
    /// number after them, <c>other (os error 24)</c>.
    /// </summary>
    private void IoErrorText(DataType error)
    {
        var kind = (EnumType)Layout.Fields(error)[0];
        var value = fn.Value($"extractvalue {Ir(error)} %a0, 0");
        var path = fn.Value($"extractvalue {Ir(error)} %a0, 2");
        var empty = fn.Value($"icmp eq i64 {fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {path}, 1")}, 9223372036854775807")}, 0");
        var values = kind.Symbol.Values;
        var labels = values.Select(_ => fn.Label("kind")).ToList();
        var other = fn.Label("other");
        fn.Terminate($"switch {Ir(kind)} {value}, label %{other} [ {string.Join(" ", values.Select((_, i) => $"{Ir(kind)} {IntegerText(Lower.EnumStored(kind.Symbol, i), kind)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < values.Count; i++)
        {
            fn.Block(labels[i]);
            var alone = fn.Label("alone");
            var joined = fn.Label("joined");
            if (values[i] == "Other")
            {
                // the system's number after the words, as Rust's text writes it (the owner, 2026-10-29, "Number for Other")
                var code = NumberPiece(fn.Value($"sext i32 {fn.Value($"extractvalue {Ir(error)} %a0, 1")} to i64"), true);
                fn.CondBranch(empty, alone, joined);
                fn.Block(alone);
                var words = Joined([LiteralPiece("other (os error "), code, LiteralPiece(")")]);
                fn.Return($"%kz.str {words}");
                fn.Block(joined);
                var named = Joined([LiteralPiece("other (os error "), code, LiteralPiece("): "), TextPiece(path)]);
                fn.Return($"%kz.str {named}");
                continue;
            }

            fn.CondBranch(empty, alone, joined);
            fn.Block(alone);
            fn.Return($"%kz.str {StringValue(Words(values[i]))}");
            fn.Block(joined);
            var text = Joined([LiteralPiece(Words(values[i]) + ": "), TextPiece(path)]);
            fn.Return($"%kz.str {text}");
        }

        fn.Block(other);
        fn.Unreachable(); // a kind is one of its values (D18)
    }

    /// <summary>The words of a name in lower case, split before each capital letter: <c>NotADirectory</c> is <c>not a directory</c>.</summary>
    private static string Words(string name)
    {
        var words = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsUpper(ch) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(ch));
        }

        return words.ToString();
    }

    // ---- the command line, the environment and the clock --------------------------------------------------

    /// <summary>What a build for Windows says <c>not supported</c> with at a read of the command line, the environment or the clock (S11).</summary>
    internal const string ProcessNotOnWindows = "a read of the command line, the environment or the clock, which the runtime of Windows does not give (S11)";

    /// <summary>Whether the data type is <c>EnvError</c>, which no file can declare beside the one every file sees (S14, V9).</summary>
    private static bool IsEnvError(KurzType type) => type is DataType { Symbol.Name: "EnvError" };

    /// <summary>
    /// S13: the program's arguments from the one at <paramref name="skip"/> on, a list the caller holds a count of. One that is not UTF-8
    /// raises <c>invalid-argument</c> at <paramref name="line"/>, or, where the line is 0, before the first statement, ends the process
    /// with no place; a build for Windows says <c>not supported</c> at <paramref name="readAt"/>, or at the line where it is 0.
    /// </summary>
    private string EmitArguments(int skip, int line, int readAt = 0)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(readAt == 0 ? line : readAt, ProcessNotOnWindows);
        }

        var list = fn.Alloca("ptr", "args");
        fn.Emit($"store ptr null, ptr {list}");
        var refused = fn.Value($"call i32 {SysFunction("Arguments")}(i32 {Number(skip)}, ptr {list})");
        var bad = fn.Label("args.refused");
        var ok = fn.Label("args");
        fn.CondBranch(fn.Value($"icmp ne i32 {refused}, 0"), bad, ok);
        fn.Block(bad);
        if (line > 0)
        {
            Raise("invalid-argument", line);
        }
        else
        {
            fn.Emit($"call void {RuntimeFunction("InvalidArgument")}()");
            fn.Unreachable();
        }

        fn.Block(ok);
        return fn.Value($"load ptr, ptr {list}");
    }

    /// <summary>
    /// S14: <c>Environment.Variable(name)</c>, the value of the variable or the case <c>EnvError</c> with its kind and the name, of which
    /// it holds a count: <c>NotPresent</c> where the variable is not set or the name is empty or holds <c>=</c> or a NUL byte, as Rust's
    /// <c>var</c> reads them, and <c>NotUnicode</c> where its value is not UTF-8.
    /// </summary>
    private string EmitVariable(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, ProcessNotOnWindows);
        }

        var union = (UnionType)Sub(info.Result);
        var error = (DataType)union.Cases.Single(IsEnvError);
        var name = Arguments(c, info, _ => Types.String, _ => false, _ => null, cConvention: false, owned: false).Single()["%kz.str ".Length..];
        var slot = fn.Alloca(Ir(union), "env");
        var done = fn.Label("env.done");
        var copy = fn.Value($"call ptr {RuntimeFunction("CPath")}(%kz.str {name})");
        var nul = fn.Label("env.nul");
        var read = fn.Label("env.read");
        fn.CondBranch(fn.Value($"icmp eq ptr {copy}, null"), nul, read);
        fn.Block(nul);
        StoreEnvError(slot, union, error, "NotPresent", name, done);
        fn.Block(read);
        var value = fn.Alloca("%kz.str", "value");
        fn.Emit($"store %kz.str {StringValue(string.Empty)}, ptr {value}");
        var r = fn.Value($"call i32 {SysFunction("Variable")}(ptr {copy}, ptr {value})");
        FreePath(copy, name);
        var found = fn.Label("env.found");
        var missing = fn.Label("env.missing");
        var invalid = fn.Label("env.invalid");
        fn.Terminate($"switch i32 {r}, label %{invalid} [ i32 0, label %{found} i32 1, label %{missing} ]");
        fn.Block(found);
        fn.Emit($"store {Ir(union)} {MakeUnion(union, Types.String, fn.Value($"load %kz.str, ptr {value}"))}, ptr {slot}");
        fn.Branch(done);
        fn.Block(missing);
        StoreEnvError(slot, union, error, "NotPresent", name, done);
        fn.Block(invalid);
        StoreEnvError(slot, union, error, "NotUnicode", name, done);
        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {slot}");
    }

    /// <summary>The case <c>EnvError</c> of the kind and the name, of which it holds a count (S14).</summary>
    private void StoreEnvError(string slot, UnionType union, DataType error, string kind, string name, string done)
    {
        Retain(name, Types.String);
        var withKind = fn.Value($"insertvalue {Ir(error)} undef, {Ir(Layout.Fields(error)[0])} {KindValue(error, kind)}, 0");
        var value = fn.Value($"insertvalue {Ir(error)} {withKind}, %kz.str {name}, 1");
        fn.Emit($"store {Ir(union)} {MakeUnion(union, error, value)}, ptr {slot}");
        fn.Branch(done);
    }

    /// <summary>
    /// S15: <c>timestamp.Now</c>, the wall clock in nanoseconds from 1970-01-01; where the runtime cannot map the page the system writes
    /// the time into, the process ends as an allocation the system refused does, with the bytes of the page (O11).
    /// </summary>
    private string EmitNow(int line)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(line, ProcessNotOnWindows);
        }

        var slot = fn.Alloca("i64", "now");
        var read = fn.Label("now");
        var refused = fn.Label("now.refused");
        fn.CondBranch(fn.Value($"call i1 {SysFunction("Now")}(ptr {slot})"), read, refused);
        fn.Block(refused);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 4096)");
        fn.Unreachable();
        fn.Block(read);
        return fn.Value($"load i64, ptr {slot}");
    }

    /// <summary>
    /// S25: <c>instant.Now</c>, the monotonic clock in nanoseconds; where the runtime cannot map the page the system writes the time
    /// into, the process ends as an allocation the system refused does, with the bytes of the page (O11), as <see cref="EmitNow"/>.
    /// </summary>
    private string EmitMonotonicNow(int line)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(line, ProcessNotOnWindows);
        }

        var slot = fn.Alloca("i64", "now");
        var read = fn.Label("now");
        var refused = fn.Label("now.refused");
        fn.CondBranch(fn.Value($"call i1 {SysFunction("MonotonicNow")}(ptr {slot})"), read, refused);
        fn.Block(refused);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 4096)");
        fn.Unreachable();
        fn.Block(read);
        return fn.Value($"load i64, ptr {slot}");
    }

    /// <summary>S14: in the helper of the text of an <c>EnvError</c> in <c>%a0</c>: the words of its kind in lower case, a colon and the name, <c>not present: HOME</c>.</summary>
    private void EnvErrorText(DataType error)
    {
        var kind = (EnumType)Layout.Fields(error)[0];
        var value = fn.Value($"extractvalue {Ir(error)} %a0, 0");
        var name = fn.Value($"extractvalue {Ir(error)} %a0, 1");
        var values = kind.Symbol.Values;
        var labels = values.Select(_ => fn.Label("kind")).ToList();
        var other = fn.Label("other");
        fn.Terminate($"switch {Ir(kind)} {value}, label %{other} [ {string.Join(" ", values.Select((_, i) => $"{Ir(kind)} {IntegerText(Lower.EnumStored(kind.Symbol, i), kind)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < values.Count; i++)
        {
            fn.Block(labels[i]);
            var text = Joined([LiteralPiece(Words(values[i]) + ": "), TextPiece(name)]);
            fn.Return($"%kz.str {text}");
        }

        fn.Block(other);
        fn.Unreachable(); // a kind is one of its values (D18)
    }
}
