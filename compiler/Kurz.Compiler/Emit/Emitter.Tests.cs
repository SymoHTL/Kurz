using System.Globalization;
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
    /// literal, holds what no IR name may (<see cref="Instance"/>); a trace names it <c>test "name"</c>.
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
        fn.Emit($"call void {RuntimeFunction("RunTests")}(ptr @kz.tests, i64 {Number(entries.Count)}, %kz.str {StringValue(unit.File)})");
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
