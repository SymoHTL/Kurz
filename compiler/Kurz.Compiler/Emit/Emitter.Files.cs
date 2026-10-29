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
        if (info.Kind is CallKind.FileRead or CallKind.FileWrite or CallKind.FileClose or CallKind.TcpAccept)
        {
            var handle = EmitExpression(((MemberAccess)Unwrap(c.Callee)).Target);
            switch (info.Kind)
            {
                case CallKind.FileRead:
                    EmitFileRead(c, info, outcome, handle);
                    break;
                case CallKind.FileWrite:
                    EmitFileWrite(c, info, outcome, handle);
                    break;
                case CallKind.TcpAccept:
                    EmitAccept(outcome, handle);
                    break;
                default:
                    EmitFileClose(outcome, handle);
                    break;
            }
        }
        else
        {
            var bytes = new ListType(Types.Byte);
            var arguments = Arguments(c, info, j => info.Kind == CallKind.FileWriteAll && j == 1 ? bytes : Types.String, _ => false, _ => null, cConvention: false, owned: false)
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
                    var r = SysCall("Connect", $"ptr {c}");
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
    /// is made in <c>into</c>, as <c>Add</c> grows a list, before any byte is read, and the bytes read are added at its end.
    /// </summary>
    private void EmitFileRead(CallExpression c, CallInfo info, FileOutcome o, string handle)
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
        FailBelowZero(o, r, HandlePath(handle));
        fn.Emit($"call void {StdFunction("BytesAdded")}(ptr {block}, i64 {r})");
        Succeed(o, fn.Value($"trunc i64 {r} to i32"));
    }

    /// <summary>S4: <c>f.Write(bytes)</c>, every byte, a write of which the system takes none <c>WriteZero</c>.</summary>
    private void EmitFileWrite(CallExpression c, CallInfo info, FileOutcome o, string handle)
    {
        var list = Arguments(c, info, _ => new ListType(Types.Byte), _ => false, _ => null, cConvention: false, owned: false).Single()["ptr ".Length..];
        var (items, count) = BytesOf(list);
        var r = SysCall("WriteFile", $"i32 {HandleDescriptor(handle)}, ptr {items}, i64 {count}");
        var path = HandlePath(handle);
        FailBelowZero(o, r, path);
        WriteZeroBelow(o, r, count, path);
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

    /// <summary>S9: a listener's <c>Accept()</c>, a stream that holds the listener's address.</summary>
    private void EmitAccept(FileOutcome o, string handle)
    {
        var r = SysCall("Accept", $"i32 {HandleDescriptor(handle)}");
        var address = HandlePath(handle);
        FailBelowZero(o, r, address);
        Succeed(o, NewHandle(fn.Value($"trunc i64 {r} to i32"), address));
    }

    /// <summary>M9: <c>list.Clear()</c>, through the place of the list's variable.</summary>
    private string? EmitListClear(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var list = (ListType)TypeOf(member.Target);
        if (Unwrap(member.Target) is not NameExpression n)
        {
            throw Unsupported(c.Line, "a `Clear` on a list in a field (M5)");
        }

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
        fn.Emit($"call void {clear}(ptr {Place(Model.VariableOf(n), n.Line)})");
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

    /// <summary>A failure, a result below 0, ends the call with its <c>IoError</c> after <paramref name="before"/>; the code goes on with a result of 0 or more.</summary>
    private void FailBelowZero(FileOutcome o, string result, string path, Action? before = null)
    {
        var failed = fn.Label("io.failed");
        var ok = fn.Label("io.ok");
        fn.CondBranch(fn.Value($"icmp slt i64 {result}, 0"), failed, ok);
        fn.Block(failed);
        before?.Invoke();
        Fail(o, fn.Value($"call {Ir(Layout.Fields(o.Error)[0])} {SysFunction("ErrorKind")}(i64 {result})"), fn.Value($"call i32 {SysFunction("ErrorCode")}(i64 {result})"), path);
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
    /// <c>fs::read</c> reads to the end: the piece <c>BytesPiece</c> gives, read again while a read gives bytes, the file closed after,
    /// its error ignored. A list that holds the largest <c>int</c> of items reads one byte more, as Rust's <c>read_to_end</c> probes a
    /// full <c>Vec</c> before it grows it: none is the end of the file, and one raises <c>allocation-failed</c> as an <c>Add</c> past
    /// the limit does (M9). 0, or the failure of the open or of a read.
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
        var loop = fn.Label("read");
        var end = fn.Label("end");
        fn.Branch(loop);
        fn.Block(loop);
        var more = fn.Value($"call i64 {StdFunction("BytesPiece")}(ptr {fn.Value("load ptr, ptr %a1")}, i64 {ReadAllChunk})");
        var full = fn.Label("full");
        var room = fn.Label("room");
        fn.CondBranch(fn.Value($"icmp eq i64 {more}, 0"), full, room);
        fn.Block(full);
        var probed = SysCall("ReadFile", $"i32 {fd}, ptr {probe}, i64 1");
        fn.Emit($"store i64 {probed}, ptr {result}");
        var past = fn.Label("past");
        fn.CondBranch(fn.Value($"icmp sle i64 {probed}, 0"), end, past);
        fn.Block(past);
        Reserve("%a1", "1");
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
                var code = fn.Value($"sext i32 {fn.Value($"extractvalue {Ir(error)} %a0, 1")} to i64");
                var number = fn.Value($"call %kz.str {RuntimeFunction("TextOfLong")}(i64 {code})");
                var numbered = fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {StringValue("other (os error ")}, %kz.str {number})");
                Release(number, Types.String);
                var words = Joined(numbered, StringValue(")"));
                fn.CondBranch(empty, alone, joined);
                fn.Block(alone);
                fn.Return($"%kz.str {words}");
                fn.Block(joined);
                fn.Return($"%kz.str {Joined(Joined(words, StringValue(": ")), path)}");
                continue;
            }

            fn.CondBranch(empty, alone, joined);
            fn.Block(alone);
            fn.Return($"%kz.str {StringValue(Words(values[i]))}");
            fn.Block(joined);
            fn.Return($"%kz.str {fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {StringValue(Words(values[i]) + ": ")}, %kz.str {path})")}");
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
}
