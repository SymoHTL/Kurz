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

        var candidate = new Candidate { Slots = f.Slots, Result = Types.Union([f.Success, IoError]) };
        return Record(c, f.Kind, Call([candidate], c, null, out var picked), picked);
    }

    /// <summary>
    /// S3, S4, S5: a file's and a stream's <c>Read(mut into, max)</c>, <c>Write(bytes)</c> and <c>Close()</c>, and S9: a listener's
    /// <c>Accept()</c> and <c>Close()</c>, each with the case <c>IoError</c>.
    /// </summary>
    private Typed CheckFileMethod(HandleType handle, MemberAccess m, CallExpression c)
    {
        var bytes = new ListType(Types.Byte);
        var listener = handle.Kind == Handle.TcpListener;
        (CallKind Kind, List<Slot> Slots, KurzType Success)? method = m.Member switch
        {
            "Read" when !listener => (CallKind.FileRead, [new Slot("into", bytes, true, false, false), new Slot("max", Types.Int, false, false, false)], Types.Int),
            "Write" when !listener => (CallKind.FileWrite, [new Slot("bytes", bytes, false, false, false)], Types.Void),
            "Accept" when listener => (CallKind.TcpAccept, [], Types.TcpStream),
            "Close" => (CallKind.FileClose, [], Types.Void),
            _ => null,
        };
        if (method is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"{Types.A(handle)} has no member `{m.Member}` (S3 to S5, S9, V10)");
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

    // ---- the command line and the environment (chapter 13) -----------------------------------------------

    /// <summary>
    /// S13: <c>Environment.Args()</c>, the program's arguments with its name first, as Rust's <c>std::env::args()</c> (section 12, the
    /// owner, 2026-10-30, "Both"); S14: <c>Environment.Variable(name)</c>, a variable of the environment or the case <c>EnvError</c>, as
    /// Rust's <c>env::var</c> (section 12, the owner, 2026-10-30, "An outcome, as Rust").
    /// </summary>
    private Typed CheckEnvironmentFunction(MemberAccess m, CallExpression c)
    {
        if (m.Member == "Exit")
        {
            NotSupported(m.Line, "`Environment.Exit`, the exit code (S23)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        (CallKind Kind, List<Slot> Slots, KurzType Result)? function = m.Member switch
        {
            "Args" => (CallKind.EnvironmentArgs, [], new ListType(Types.String)),
            "Variable" => (CallKind.EnvironmentVariable, [new Slot("name", Types.String, false, false, false)], Types.Union([Types.String, new DataType(dataTypes["EnvError"], [])])),
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
