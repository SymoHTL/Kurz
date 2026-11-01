using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Files and sockets (chapter 13; design record, section 12). A file, a listener and a stream are the address of their handle's block:
/// the count of the values that hold it, 8 bytes, its descriptor, 4 bytes, -1 once it is closed, a listener's port, 4 bytes, and from its
/// 16th byte the path or the address it was opened with, a string of which the block holds a count; 32 bytes. A stream a listener's
/// <c>Accept</c> gives holds the listener's address (S9). The last value that lets go of it closes the descriptor, ignoring an error of the close, as Rust's
/// <c>Drop</c> of a <c>File</c> ignores it, lets go of the path and frees the block (S5; the owner, 2026-10-29, "At the last count, as Rust
/// Drop"). A call on a file calls the runtime of Linux, whose result is a number of 0 or more or a failure below 0, which the runtime's
/// <c>ErrorKind</c> and <c>ErrorCode</c> read into an <c>IoError</c> with the path the call was about (S1); the runtime of Windows gives none of
/// these functions, and a build for Windows says <c>not supported</c> at the call (S11; the owner, 2026-10-29, "Not supported on Windows").
/// </summary>
public sealed partial class Emitter
{
    /// <summary>What a build for Windows says <c>not supported</c> with at a call on a file or a socket (S11).</summary>
    internal const string NotOnWindows = "a call on a file or a socket, which the runtime of Windows does not give (S11)";

    /// <summary>The bytes of a handle's block.</summary>
    private const string HandleSize = "32";

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
        if (info.Kind is CallKind.FileRead or CallKind.FileWrite or CallKind.FileClose or CallKind.TcpAccept or CallKind.TcpSetReadTimeout or CallKind.TcpSetWriteTimeout)
        {
            var target = ((MemberAccess)Unwrap(c.Callee)).Target;
            var kinds = TypeOf(target) is HandleType { Kind: Handle.TcpStream } ? "StreamErrorKind" : "ErrorKind";
            var handle = EmitExpression(target);
            switch (info.Kind)
            {
                case CallKind.FileRead:
                    EmitFileRead(c, info, outcome, handle, kinds);
                    break;
                case CallKind.FileWrite:
                    EmitFileWrite(c, info, outcome, handle, kinds);
                    break;
                case CallKind.TcpAccept:
                    EmitAccept(outcome, handle);
                    break;
                case CallKind.TcpSetReadTimeout or CallKind.TcpSetWriteTimeout:
                    EmitSetTimeout(c, info, outcome, handle);
                    break;
                default:
                    EmitFileClose(outcome, handle);
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
    /// </summary>
    private void EmitFileRead(CallExpression c, CallInfo info, FileOutcome o, string handle, string kinds)
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
        var r = SysCall("ReadFile", $"i32 {HandleDescriptor(handle)}, ptr {fn.Value($"call ptr {StdFunction("BytesEnd")}(ptr {block})")}, i64 {more}");
        FailBelowZero(o, r, HandlePath(handle), kinds: kinds);
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {r})");
        Succeed(o, fn.Value($"trunc i64 {r} to i32"));
    }

    /// <summary>S4: <c>f.Write(bytes)</c>, every byte, a write of which the system takes none <c>WriteZero</c>; the kind of a failure as <see cref="EmitFileRead"/> gives it.</summary>
    private void EmitFileWrite(CallExpression c, CallInfo info, FileOutcome o, string handle, string kinds)
    {
        var list = Arguments(c, info, _ => new ListType(Types.Byte), _ => false, _ => null, cConvention: false, owned: false).Single()["ptr ".Length..];
        var (items, count) = BytesOf(list);
        var r = SysCall("WriteFile", $"i32 {HandleDescriptor(handle)}, ptr {items}, i64 {count}");
        var path = HandlePath(handle);
        FailBelowZero(o, r, path, kinds: kinds);
        WriteZeroBelow(o, r, count, path);
        Succeed(o, null);
    }

    /// <summary>
    /// S9: a stream's <c>SetReadTimeout(timeout)</c> or <c>SetWriteTimeout(timeout)</c>: the runtime's <c>SetTimeout</c> with
    /// <c>SO_RCVTIMEO</c> (20) or <c>SO_SNDTIMEO</c> (21), the nanoseconds of the time, and whether there is one, <c>null</c> being no bound.
    /// </summary>
    private void EmitSetTimeout(CallExpression c, CallInfo info, FileOutcome o, string handle)
    {
        var nullable = Types.Nullable(Types.Duration);
        var time = Arguments(c, info, _ => nullable, _ => false, _ => null, cConvention: false, owned: false).Single()[(Ir(nullable).Length + 1)..];
        var bounded = fn.Value($"xor i1 {IsCase(Tag(time, nullable), nullable, Types.Null)}, true");
        var nanoseconds = fn.Value($"select i1 {bounded}, i64 {Payload(time, nullable, Types.Duration)}, i64 0");
        var option = info.Kind == CallKind.TcpSetReadTimeout ? "20" : "21";
        var r = SysCall("SetTimeout", $"i32 {HandleDescriptor(handle)}, i32 {option}, i64 {nanoseconds}, i1 {bounded}");
        FailBelowZero(o, r, HandlePath(handle));
        Succeed(o, null);
    }

    /// <summary>S5: <c>f.Close()</c>: the descriptor closed and -1 in its place, so that every later call, a second <c>Close</c> among them, gets the system's error of a descriptor that is not open.</summary>
    private void EmitFileClose(FileOutcome o, string handle)
    {
        var fd = HandleDescriptor(handle);
        fn.Emit($"store i32 -1, ptr {HandleField(handle, 8)}");
        var r = SysCall("CloseFile", $"i32 {fd}");
        FailBelowZero(o, r, HandlePath(handle));
        Succeed(o, null);
    }

    /// <summary>
    /// S9: a listener's <c>Accept()</c>, a stream that holds the listener's address on a count of its own, which
    /// <see cref="NewHandle"/> takes, so that the address outlives the listener's block.
    /// </summary>
    private void EmitAccept(FileOutcome o, string handle)
    {
        var r = SysCall("Accept", $"i32 {HandleDescriptor(handle)}");
        var address = HandlePath(handle);
        FailBelowZero(o, r, address);
        Succeed(o, NewHandle(fn.Value($"trunc i64 {r} to i32"), address));
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
    /// function <paramref name="kinds"/> gives, <c>ErrorKind</c>, or <c>StreamErrorKind</c> for a stream's read and write (S9); the code
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

    // ---- the handle --------------------------------------------------------------------------------

    private string HandleField(string handle, int offset) => fn.Value($"getelementptr i8, ptr {handle}, i64 {Number(offset)}");

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
        return handle;
    }

    /// <summary>One more value holds the handle.</summary>
    private string RetainHandle() => Helper("retain.File", "void", ["ptr"], () =>
    {
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value("load i64, ptr %a0")}, 1")}, ptr %a0");
        fn.Return(null);
    });

    /// <summary>One value less holds the handle; the last closes a descriptor that is open, ignoring the close's error, lets go of the path and frees the block (S5).</summary>
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
            var fd = HandleDescriptor("%a0");
            var open = fn.Label("open");
            var closed = fn.Label("closed");
            fn.CondBranch(fn.Value($"icmp sge i32 {fd}, 0"), open, closed);
            fn.Block(open);
            SysCall("CloseFile", $"i32 {fd}");
            fn.Branch(closed);
            fn.Block(closed);
            Release(HandlePath("%a0"), Types.String);
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
