using System.Globalization;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- the tests (chapter 20) --------------------------------------------------------------------

    /// <summary>
    /// Y2, Y5: the top-level code of the build of <c>kurz test</c>, which runs no statement of the program's (D1): the table of its tests,
    /// in the order of the file, each entry the test's function, its name as written and its line, and the runtime's <c>RunTests</c>,
    /// which runs each in an actor of its own and ends the process with the exit code of the run (Q4). A test's function is queued as a
    /// function the code calls is, with its own line as the statement that queued it, and named by that line, since its name, a string
    /// literal, holds what no IR name may (<see cref="Instance"/>); a trace names it <c>test "name"</c>. The runner takes the build's seed,
    /// its bits as an <c>i64</c>, and starts its generators with it before the first test, and whether the build is ordered (Y11).
    /// </summary>
    private void EmitTestRun()
    {
        var entries = new List<string>();
        foreach (var t in Model.Tests)
        {
            var f = Model.TestOf(t);
            var key = $"{unit.Prefix}.test.{t.Line.ToString(CultureInfo.InvariantCulture)}";
            var name = $"@\"{key}\"";
            instances[key] = name;
            pending.Enqueue((f, NoMap, name, features, t.Line));
            entries.Add($"{{ ptr, %kz.str, i64 }} {{ ptr {name}, %kz.str {StringValue(t.Written ?? string.Empty)}, i64 {Number(t.Line)} }}");
        }

        module.Global($"@kz.tests = internal constant [{Number(entries.Count)} x {{ ptr, %kz.str, i64 }}] [{string.Join(", ", entries)}]");
        fn.Emit($"call void {RuntimeFunction("RunTests")}(ptr @kz.tests, i64 {Number(entries.Count)}, %kz.str {StringValue(unit.File)}, i64 {Number(unchecked((long)seed))}, i1 {(ordered ? "true" : "false")})");
    }

    /// <summary>
    /// Y6: <c>check condition</c>, which goes on where the condition holds and otherwise calls the runtime's <c>CheckFailed</c> with the
    /// condition's text, the crash of the actor that runs it, at the file and the line of the <c>check</c>. For a comparison whose two sides
    /// show their values (<see cref="Assertion"/>), each side is evaluated once, compared as the comparison compares them, and on the path
    /// of the failure shown after the text, <c>names.Count == 2 (3 == 2)</c>, each as a hole of an interpolation shows it (L6, F1). A side
    /// read through a <c>mut</c> parameter, the place of its caller's, takes no count of its own where nothing after it in the statement
    /// calls (<c>compiler/README.md</c>), and the text of the other side can run a <c>Text()</c> of the program that assigns that place; so
    /// the path of the failure takes a count of each counted side before it makes a text, and lets go of them once the text is joined, as
    /// the line <c>check-operands-fenced</c> of <c>ListTests</c> holds.
    /// </summary>
    private void EmitAssertion(AssertionStatement a)
    {
        var assertion = Model.AssertionOf(a);
        var failed = fn.Label("checkfailed");
        var passed = fn.Label("checked");
        if (assertion.Comparison is not { } b)
        {
            fn.CondBranch(EmitExpression(a.Condition), passed, failed);
            fn.Block(failed);
            Failed(StringValue(assertion.Text));
            fn.Block(passed);
            return;
        }

        var leftType = TypeOf(b.Left);
        var rightType = TypeOf(b.Right);
        var left = EmitExpression(b.Left);
        var right = EmitExpression(b.Right);
        fn.CondBranch(Compared(b, left, leftType, right, rightType), passed, failed);
        fn.Block(failed);
        var sides = new[] { (left, leftType), (right, rightType) }.Where(side => Counted(side.Item2) && side.Item1 != "null").ToList();
        foreach (var (value, type) in sides)
        {
            Retain(value, type);
        }

        var text = Joined([LiteralPiece(assertion.Text + " ("), PieceOf(left, leftType, b.Left.Line), LiteralPiece($" {Spelled(b.Operator)} "), PieceOf(right, rightType, b.Right.Line), LiteralPiece(")")]);
        foreach (var (value, type) in sides)
        {
            Release(value, type);
        }

        Failed(text);
        fn.Block(passed);

        void Failed(string words)
        {
            fn.Emit($"call void {RuntimeFunction("CheckFailed")}(%kz.str {words}, %kz.str {StringValue(unit.File)}, i32 {Number(a.Line)})");
            fn.Unreachable();
        }
    }

    /// <summary>
    /// Y10: <c>advance d</c>, which stands in a test alone, so that only the build of <c>kurz test</c> emits it: its duration, of less than
    /// zero the run-time error <c>advance-negative</c> at the statement, the crash of the test's actor; then the runtime's <c>Advance</c>,
    /// which moves the clock's offset by it, and where a clock it moves would pass the range of a <c>long</c>, <c>overflow</c> at the
    /// statement (section 4). On Linux alone, where the clock's reads are built (S25).
    /// </summary>
    private void EmitAdvance(AdvanceStatement a)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(a.Line, ProcessNotOnWindows);
        }

        var duration = EmitExpression(a.Duration);
        var negative = fn.Label("advance.negative");
        var counted = fn.Label("advance.counted");
        fn.CondBranch(fn.Value($"icmp sge i64 {duration}, 0"), counted, negative);
        fn.Block(negative);
        Raise(ErrorIds.AdvanceNegative, a.Line);
        fn.Block(counted);
        RaiseUnless(fn.Value($"call i1 {RuntimeFunction("Advance")}(i64 {duration})"), a.Line);
    }

    /// <summary>A comparison's operator as the source writes it, for the failure of a <c>check</c> (F1).</summary>
    private static string Spelled(TokenKind op) => op switch
    {
        TokenKind.EqualEqual => "==",
        TokenKind.NotEqual => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        _ => throw new InvalidOperationException($"`{op}` is none of the six comparisons a `check` shows (Y6)"),
    };
}
