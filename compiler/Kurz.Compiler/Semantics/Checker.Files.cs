using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // ---- files (chapter 13) ------------------------------------------------------------------------

    /// <summary>The case every call on a file returns where it failed (S1).</summary>
    private DataType IoError => new(dataTypes["IoError"], []);

    /// <summary>
    /// S2, S6, S7, S8, S15: a function of <c>File</c> or <c>Directory</c> on a path, a <c>string</c>, and S9: <c>TcpListener.Bind</c> and
    /// <c>TcpStream.Connect</c> on an address, a <c>string</c>; each result is its success or the case <c>IoError</c> (S1; section 12, the
    /// owner, 2026-10-29, "One union, as Rust" and "Rust's types, Kurz casing").
    /// </summary>
    private Typed CheckFileFunction(string type, MemberAccess m, CallExpression c)
    {
        var bytes = new ListType(Types.Byte);
        Slot Path(string name) => new(name, Types.String, false, false, false);
        (CallKind Kind, List<Slot> Slots, KurzType Success)? function = (type, m.Member) switch
        {
            ("File", "Open") => (CallKind.FileOpen, [Path("path")], Types.File),
            ("File", "Create") => (CallKind.FileCreate, [Path("path")], Types.File),
            ("File", "ReadAll") => (CallKind.FileReadAll, [Path("path")], bytes),
            ("File", "WriteAll") => (CallKind.FileWriteAll, [Path("path"), new Slot("bytes", bytes, false, false, false)], Types.Void),
            ("File", "Delete") => (CallKind.FileDelete, [Path("path")], Types.Void),
            ("File", "Rename") => (CallKind.FileRename, [Path("from"), Path("to")], Types.Void),
            ("File", "Size") => (CallKind.FileSize, [Path("path")], Types.Long),
            ("File", "Exists") => (CallKind.FileExists, [Path("path")], Types.Bool),
            ("File", "Modified") => (CallKind.FileModified, [Path("path")], Types.Timestamp),
            ("Directory", "Create") => (CallKind.DirectoryCreate, [Path("path")], Types.Void),
            ("Directory", "List") => (CallKind.DirectoryList, [Path("path")], new ListType(Types.String)),
            ("Directory", "Delete") => (CallKind.DirectoryDelete, [Path("path")], Types.Void),
            ("TcpListener", "Bind") => (CallKind.TcpBind, [Path("address")], Types.TcpListener),
            ("TcpStream", "Connect") => (CallKind.TcpConnect, [Path("address")], Types.TcpStream),
            _ => null,
        };
        if (function is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"`{type}` has no function `{m.Member}` (S2, S6 to S9, S15, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{type}.{m.Member}`"))
        {
            return Typed.Error;
        }

        var result = Types.Union([f.Success, IoError]);
        List<Candidate> candidates = [new Candidate { Slots = f.Slots, Result = result }];
        if (f.Kind == CallKind.TcpConnect)
        {
            // S9: the second form, `TcpStream.Connect(address, timeout)`, as Rust's `TcpStream::connect_timeout` (section 12, the owner,
            // 2026-10-30, "Second form, as Rust"), its time a `duration` (assumed); the emitter tells the forms apart by their slots
            candidates = [
                new Candidate { Slots = f.Slots, Result = result, Form = $"{result} Connect(string address)" },
                new Candidate { Slots = [Path("address"), new Slot("timeout", Types.Duration, false, false, false)], Result = result, Form = $"{result} Connect(string address, duration timeout)" },
            ];
        }

        return Record(c, f.Kind, Call(candidates, c, null, out var picked), picked);
    }

    /// <summary>
    /// S3, S4, S5: a file's and a stream's <c>Read(mut into, max)</c>, <c>Write(bytes)</c> and <c>Close()</c>; S24: their
    /// <c>Fill(mut into)</c>, whose success is the count it appended, as a <c>Read</c>'s is (section 12, assumed), and <c>Consume(n)</c>,
    /// as Rust's <c>BufRead::fill_buf</c> and <c>BufRead::consume</c> (section 12, the owner, 2026-11-03); S27: their <c>Seek(offset)</c>,
    /// as Rust's <c>Seek::seek(SeekFrom::Start(n))</c> (section 12, the owner, 2026-11-08, G5), its offset a <c>long</c> (section 12,
    /// assumed); S9: a listener's
    /// <c>Accept()</c> and <c>Close()</c>, a stream's <c>SetReadTimeout(timeout)</c> and <c>SetWriteTimeout(timeout)</c>, whose time is
    /// a <c>duration?</c> (section 12, assumed), <c>null</c> for no bound, as Rust's <c>set_read_timeout(Option&lt;Duration&gt;)</c> (the
    /// owner, 2026-11-01, Q8 (a)), and a listener's <c>SetAcceptTimeout(timeout)</c>, which takes its time as the stream's setters do
    /// (section 12, assumed), and a stream's <c>ShutdownWrite()</c>, as Rust's <c>shutdown(Shutdown::Write)</c> (section 12, the owner,
    /// 2026-11-02); and, in <c>std/</c> alone, whose checker reads no other library, a stream's <c>SetNoDelay(on)</c>, the system's
    /// <c>TCP_NODELAY</c>, which the writer of a streamed response sets, as Kestrel sets it on every connection, and which a program does
    /// not see (W11, assumed); each with the case <c>IoError</c>. In <c>std/</c> alone, where no checker of <c>std/</c> stands above this one, a
    /// stream's <c>WriteWithin(bytes, bound)</c>, a write held to <c>bound</c> across the calls of the system it makes, which the server's
    /// writes of a response take (section 12, the default of the follow-up's product pass, Q10): a program sees no such member.
    /// </summary>
    private Typed CheckFileMethod(HandleType handle, MemberAccess m, CallExpression c)
    {
        if (m.Member == "Lines" && handle.Kind == Handle.File)
        {
            return CheckLines(c);
        }

        var bytes = new ListType(Types.Byte);
        var listener = handle.Kind == Handle.TcpListener;
        var stream = handle.Kind == Handle.TcpStream;
        var timeout = new Slot("timeout", Types.Nullable(Types.Duration), false, false, false);
        (CallKind Kind, List<Slot> Slots, KurzType Success)? method = m.Member switch
        {
            "Read" when !listener => (CallKind.FileRead, [new Slot("into", bytes, true, false, false), new Slot("max", Types.Int, false, false, false)], Types.Int),
            "Fill" when !listener => (CallKind.FileFill, [new Slot("into", bytes, true, false, false)], Types.Int),
            "Consume" when !listener => (CallKind.FileConsume, [new Slot("n", Types.Int, false, false, false)], Types.Void),
            "Seek" when !listener => (CallKind.FileSeek, [new Slot("offset", Types.Long, false, false, false)], Types.Void),
            "Write" when !listener => (CallKind.FileWrite, [new Slot("bytes", bytes, false, false, false)], Types.Void),
            "Accept" when listener => (CallKind.TcpAccept, [], Types.TcpStream),
            "SetReadTimeout" when stream => (CallKind.TcpSetReadTimeout, [timeout], Types.Void),
            "SetWriteTimeout" when stream => (CallKind.TcpSetWriteTimeout, [timeout], Types.Void),
            "SetAcceptTimeout" when listener => (CallKind.TcpSetAcceptTimeout, [timeout], Types.Void),
            "ShutdownWrite" when stream => (CallKind.TcpShutdownWrite, [], Types.Void),
            "SetNoDelay" when stream && std is null => (CallKind.TcpSetNoDelay, [new Slot("on", Types.Bool, false, false, false)], Types.Void),
            "WriteWithin" when stream && std is null => (CallKind.TcpWriteWithin, [new Slot("bytes", bytes, false, false, false), new Slot("bound", Types.Duration, false, false, false)], Types.Void),
            "Close" => (CallKind.FileClose, [], Types.Void),
            _ => null,
        };
        if (method is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"{Types.A(handle)} has no member `{m.Member}` (S3 to S5, S9, S24, S27, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = f.Slots, Result = Types.Union([f.Success, IoError]) };
        return Record(c, f.Kind, Call([candidate], c, null, out var picked), picked);
    }

    /// <summary>
    /// S16: <c>f.Lines()</c>, which stands as the collection of a <c>for</c> loop alone (section 12, the owner, 2026-11-01, "Loop source
    /// only"): there its result is the loop's outcome, <c>void</c> with the cases a pull fails with, <c>IoError</c> and <c>InvalidUtf8</c>,
    /// which the loop takes whole (<see cref="CheckForIn"/>); anywhere else it is <c>syntax</c>, E4's id, which S16 proposes.
    /// </summary>
    private Typed CheckLines(CallExpression c)
    {
        if (!ReferenceEquals(c, loopSource))
        {
            Report(ErrorIds.Syntax, c.Line, "`f.Lines()` stands as the collection of a `for` loop alone (S16)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, "`Lines`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = [], Result = Types.Union([Types.Void, IoError, new DataType(dataTypes["InvalidUtf8"], [])]) };
        return Record(c, CallKind.FileLines, Call([candidate], c, null, out var picked), picked);
    }

    // ---- the command line and the environment (chapter 13) -----------------------------------------------

    /// <summary>
    /// S13: <c>Environment.Args()</c>, the program's arguments with its name first, as Rust's <c>std::env::args()</c> (section 12, the
    /// owner, 2026-10-30, "Both"); S14: <c>Environment.Variable(name)</c>, a variable of the environment or the case <c>EnvError</c>, as
    /// Rust's <c>env::var</c> (section 12, the owner, 2026-10-30, "An outcome, as Rust"); S23: <c>Environment.Exit(code)</c>, the end of the
    /// process with an <c>int</c> code, as C#'s <c>Environment.Exit</c> and Rust's <c>std::process::exit</c> (section 12, the owner, 2026-11-01,
    /// "Top-level return n + Exit"), after which no path goes on (<see cref="IsExit"/>).
    /// </summary>
    private Typed CheckEnvironmentFunction(MemberAccess m, CallExpression c)
    {
        (CallKind Kind, List<Slot> Slots, KurzType Result)? function = m.Member switch
        {
            "Args" => (CallKind.EnvironmentArgs, [], new ListType(Types.String)),
            "Variable" => (CallKind.EnvironmentVariable, [new Slot("name", Types.String, false, false, false)], Types.Union([Types.String, new DataType(dataTypes["EnvError"], [])])),
            "Exit" => (CallKind.EnvironmentExit, [new Slot("code", Types.Int, false, false, false)], Types.Void),
            _ => null,
        };
        if (function is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"`Environment` has no function `{m.Member}` (S13, S14, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`Environment.{m.Member}`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = f.Slots, Result = f.Result };
        return Record(c, f.Kind, Call([candidate], c, null, out var picked), picked);
    }
}
