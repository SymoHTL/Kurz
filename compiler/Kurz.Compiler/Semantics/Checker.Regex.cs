using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // ---- regular expressions (G1 to G5) ------------------------------------------------------------

    /// <summary>
    /// G1: whether <c>Regex</c> names the class of <c>std/</c> the prelude brings into the file (W1), which no file can declare
    /// beside it.
    /// </summary>
    private bool IsStdRegex() => preludeSeen.Contains("Regex");

    /// <summary>
    /// G3: <c>Regex.New(pattern)</c>, the one function of the name <c>Regex</c>. A pattern written as a literal without
    /// interpolation, raw or not (L20), is checked here by <see cref="RegexPattern"/>, the parse and the count of the standard
    /// library's: one that does not parse, that takes a construct the linear engine does not take, or whose automaton passes the
    /// bound (G2) is <c>regex-invalid</c> at the literal, with the words the <c>RegexError</c> of the same pattern prints and no word
    /// of the pattern, and the call gives a <c>Regex</c>; any other string gives <c>Regex | RegexError</c> when the program runs
    /// (section 12, the owner, 2026-11-01, "Compile time for literals"). A flag <c>backtracking</c>, named or the second argument,
    /// picks the engine (G2); the literal is checked for that engine where the flag is absent or written as <c>true</c> or
    /// <c>false</c>, and any other flag leaves the check to the run, as for a pattern that is no literal (G3).
    /// </summary>
    private Typed CheckRegexFunction(MemberAccess m, CallExpression c)
    {
        if (m.Member != "New")
        {
            Report(ErrorIds.UnknownName, m.Line, $"`Regex` has no function `{m.Member}`: a `Regex` is made by `Regex.New` (G4, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, "`Regex.New`"))
        {
            return Typed.Error;
        }

        var flag = c.Arguments.FirstOrDefault(a => a.Name == "backtracking")
            ?? (c.Arguments.Count > 1 && c.Arguments[1].Name is null ? c.Arguments[1] : null);
        bool? backtracking = flag is null ? false : Unwrap(flag.Value) is BoolLiteral b ? b.Value : null;
        var pattern = c.Arguments.FirstOrDefault(a => a.Name is null or "pattern")?.Value;
        var literal = backtracking is not null && pattern is not null && Unwrap(pattern) is StringLiteral { IsPlain: true } s
            ? string.Concat(s.Segments.Cast<TextSegment>().Select(t => t.Text))
            : null;
        var regex = new ClassType(classes["Regex"]);
        var candidate = new Candidate
        {
            Slots = flag is null
                ? [new Slot("pattern", Types.String, false, false, false)]
                : [new Slot("pattern", Types.String, false, false, false), new Slot("backtracking", Types.Bool, false, false, false)],
            Result = literal is null ? new UnionType([regex, new DataType(dataTypes["RegexError"], [])]) : regex,
        };
        var typed = Record(c, CallKind.RegexNew, Call([candidate], c, null, out var picked), picked);
        if (literal is not null && RegexPattern.Check(literal, backtracking == true) is { } failed)
        {
            var rules = failed.Kind == RegexPattern.Kind.Syntax ? "G3" : "G2, G3";
            Report(ErrorIds.RegexInvalid, pattern!.Line, $"`Regex.New` of this literal gives `{RegexPattern.Words(failed.Kind, failed.At)}` ({rules})");
        }

        return typed;
    }
}
