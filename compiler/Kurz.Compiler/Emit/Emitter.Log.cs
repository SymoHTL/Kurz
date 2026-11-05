using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- logging (chapter 17) ----------------------------------------------------------------------

    /// <summary>The global of the lowest level a call site may be on at, which the start sets from <c>KURZ_LOG</c>, <c>None</c> until then (P3, P4).</summary>
    private const string LogLowest = "@kz.log.lowest";

    /// <summary>The global of each log call site, by its node: 0 where its level was not read against the filter yet, 1 where it is off, 2 where it is on (P3).</summary>
    private readonly Dictionary<CallExpression, string> logStates = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// P4, P5: in a program built for Linux with a log call in one of its files, <c>KURZ_LOG</c> read before the first statement, which the
    /// runtime's <c>LogFilter</c> keeps or refuses, and the lowest level a call site may be on at; a program with no log call reads none.
    /// </summary>
    private void EmitLogStart()
    {
        if (target != Target.Linux || !units.Any(u => u.Model.HasLogSites))
        {
            return;
        }

        module.Global($"{LogLowest} = internal global i32 6");
        var name = StringValue("KURZ_LOG");
        var copy = fn.Value($"call ptr {RuntimeFunction("CPath")}(%kz.str {name})");
        var value = fn.Alloca("%kz.str", "filter");
        fn.Emit($"store %kz.str {StringValue(string.Empty)}, ptr {value}");
        var found = fn.Value($"call i32 {SysFunction("Variable")}(ptr {copy}, ptr {value})");
        FreePath(copy, name);
        var lowest = fn.Value($"call i32 {RuntimeFunction("LogFilter")}(i32 {found}, %kz.str {fn.Value($"load %kz.str, ptr {value}")})");
        fn.Emit($"store i32 {lowest}, ptr {LogLowest}");
    }

    /// <summary>
    /// P3: a log call, which is off where its level is below the lowest the filter turns on, a load of a global and a branch, or where its
    /// site's global says it is off, a load and a branch, as Rust's <c>tracing</c> reads its level and its call site's interest; the first
    /// time a site is reached past the first test, the runtime's <c>LogEnabled</c> reads its category and level against the filter and the
    /// site keeps what it said. Only a site that is on runs the holes of its template and writes its line (<see cref="EmitLogLine"/>), in a
    /// region of its own; a build for Windows says <c>not supported</c>, its runtime giving no clock for the line's time (S11).
    /// </summary>
    private string? EmitLog(CallExpression c)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, ProcessNotOnWindows);
        }

        var site = Model.LogSiteOf(c);
        if (!logStates.TryGetValue(c, out var state))
        {
            // declared once for the node, before anything of the call is emitted, so that a copy of its function reads the same global
            state = $"@kz.log.{logStates.Count}";
            logStates[c] = state;
            module.Global($"{state} = internal global i8 0");
        }

        var level = Number(site.Level);
        var check = fn.Label("log.site");
        var resolve = fn.Label("log.read");
        var on = fn.Label("log.on");
        var done = fn.Label("log.done");
        fn.CondBranch(fn.Value($"icmp sge i32 {level}, {fn.Value($"load i32, ptr {LogLowest}")}"), check, done);
        fn.Block(check);
        fn.Terminate($"switch i8 {fn.Value($"load i8, ptr {state}")}, label %{resolve} [ i8 1, label %{done} i8 2, label %{on} ]");
        fn.Block(resolve);
        var category = StringValue(site.Category ?? Path.GetFileNameWithoutExtension(unit.File));
        var enabled = fn.Value($"call i1 {RuntimeFunction("LogEnabled")}(%kz.str {category}, i32 {level})");
        fn.Emit($"store i8 {fn.Value($"select i1 {enabled}, i8 2, i8 1")}, ptr {state}");
        fn.CondBranch(enabled, on, done);
        fn.Block(on);
        InRegion(() => EmitLogLine(c, site, category));
        if (!fn.Terminated)
        {
            fn.Branch(done);
        }

        fn.Block(done);
        return null;
    }

    /// <summary>
    /// P2, P7: the line of a call site that is on: each hole of the template run once, in its order, its value both a piece of the message,
    /// as an interpolation makes it (L6), and a field, <c> name=value</c>, a <c>string</c> in quotes (the runtime's <c>LogQuoted</c>), then
    /// the time of the wall clock, and the runtime's <c>LogLine</c>, which writes the line.
    /// </summary>
    private void EmitLogLine(CallExpression c, LogSite site, string category)
    {
        var template = (StringLiteral)c.Arguments[0].Value;
        var message = new List<InterpolationPiece>();
        var fields = new List<InterpolationPiece>();
        var field = 0;
        foreach (var segment in template.Segments)
        {
            if (segment is TextSegment text)
            {
                message.Add(LiteralPiece(text.Text));
                continue;
            }

            var hole = ((HoleSegment)segment).Expression;
            var piece = Hole(hole);
            message.Add(piece);
            fields.Add(LiteralPiece($" {site.Fields[field++]}="));
            fields.Add(TypeOf(hole) is PrimitiveType { Kind: Primitive.String }
                ? new InterpolationPiece(Temp(fn.Value($"call %kz.str {RuntimeFunction("LogQuoted")}(%kz.str {piece.Value})"), Types.String), null, true)
                : piece);
        }

        var now = EmitNow(c.Line);
        fn.Emit($"call void {RuntimeFunction("LogLine")}(i32 {Number(site.Level)}, %kz.str {category}, %kz.str {LogText(message)}, %kz.str {LogText(fields)}, i64 {now})");
    }

    /// <summary>The pieces as one text: none the empty string, one text itself, and more in one block of the region (<see cref="InOneBlock"/>).</summary>
    private string LogText(List<InterpolationPiece> pieces) => pieces switch
    {
        [] => StringValue(string.Empty),
        [{ Signed: null } text] => text.Value,
        _ => Temp(InOneBlock(pieces), Types.String),
    };
}
