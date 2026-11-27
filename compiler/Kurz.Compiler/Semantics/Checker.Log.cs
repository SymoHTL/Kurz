using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // ---- logging (chapter 17) ----------------------------------------------------------------------

    /// <summary>The levels of a log call from the lowest, each the name of a function of <c>Log</c>, as C#'s <c>LogLevel</c> names them (P1, P3).</summary>
    internal static readonly string[] LogLevels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    /// <summary>
    /// P1, P2: <c>Log.Information("Paid {order.Id}")</c>, a call of the function of a level with a template, a string literal whose holes are
    /// checked as every hole is (L6) and name its fields by their text; the call site's record gives its level, the class whose method
    /// holds it, the category, or none for the code outside a class, whose category is its file's, and the names of the fields.
    /// </summary>
    private Typed CheckLogFunction(MemberAccess m, CallExpression c)
    {
        var level = Array.IndexOf(LogLevels, m.Member);
        if (level < 0)
        {
            Report(ErrorIds.UnknownName, m.Line, $"`Log` has no function `{m.Member}`: its functions are named after the levels, `Trace` to `Critical` (P1, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`Log.{m.Member}`"))
        {
            return Typed.Error;
        }

        if (c.Arguments is [{ Value: not StringLiteral } argument])
        {
            Report(ErrorIds.LogTemplate, argument.Value.Line, "the template of a log call is a string literal, whose holes name the fields of the line (P2)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = [new Slot("template", Types.String, false, false, false)], Result = Types.Void };
        var typed = Record(c, CallKind.Log, Call([candidate], c, null, out var picked), picked);
        if (!typed.IsError && c.Arguments is [{ Value: StringLiteral template }])
        {
            var fields = template.Segments.OfType<HoleSegment>().Select(h => h.Source.Trim()).ToList();
            model.RecordLogSite(c, new LogSite(level, CurrentClass()?.Name, fields));
        }

        return typed;
    }
}
