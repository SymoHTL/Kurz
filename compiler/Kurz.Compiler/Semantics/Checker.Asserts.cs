using System.Globalization;
using System.Text;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// T36: <c>const assert(condition, text)</c>, with which generic code stops the build on a fault it finds in a type it reads (section 4,
/// the owner, 2026-11-01, "const assert, as Rust"). It is checked while the program compiles and emits nothing. A generic function that
/// holds one is checked in each copy a call makes (T33), where the copy's reading of its type and its constant type arguments (T35) are
/// known; where the condition is false, the build stops at the call that made the outermost copy, with the text.
/// </summary>
public sealed partial class Checker
{
    private void CheckConstAssert(ConstAssertStatement a)
    {
        CallExpression? call = null;
        SyntaxCopy.Walk(a, e => call ??= e as CallExpression);
        if (call is not null)
        {
            NotSupported(call.Line, "a call in a `const assert`, which the build does not evaluate while the program compiles (T36)");
            CountNamesRead(a); // what it names counts as read, as in a branch that never runs (E1)
            return;
        }

        Push();
        CheckCondition(a.Condition);
        var reported = Pop(commit: true).Count > 0; // a fault in a part is that part's one error (E1)
        var condition = model.TypeOf(a.Condition);
        var holds = condition.Constant as BoolConstant;
        if (!reported && !condition.IsError && holds is null)
        {
            NotKnown(a.Condition, "a condition");
        }

        var text = ConstAssertText(a.Text);
        if (holds is { Value: false } && text is not null)
        {
            var copy = CurrentCopy();
            if (copy is null)
            {
                Report(ErrorIds.ConstAssertFailed, a.Line, $"{text} (T36)");
            }
            else
            {
                var outermost = copy.Outermost;
                Report( // into the copy's own list, which CheckCopy reports once
                    ErrorIds.ConstAssertFailed,
                    outermost.Origin.Line,
                    $"`{outermost.Symbol.Name}`: {text} (the `const assert` in `{copy.Original.Name}`, T36)");
            }
        }
    }

    /// <summary>
    /// The text of a <c>const assert</c>, each hole filled while the program compiles: a type parameter with the name of the type the copy
    /// binds, any other hole with the text of its constant (A2, A4); null where a hole was reported.
    /// </summary>
    private string? ConstAssertText(StringLiteral s)
    {
        var text = new StringBuilder();
        var whole = true;
        foreach (var segment in s.Segments)
        {
            if (segment is TextSegment t)
            {
                text.Append(t.Text);
                continue;
            }

            var hole = ((HoleSegment)segment).Expression;
            if (hole is NameExpression n && Lookup(n.Name, capture: false).Variable is null && BoundType(n.Name) is { } type)
            {
                text.Append(type.Text);
                continue;
            }

            Push();
            var value = CheckExpression(hole, null);
            if (Pop(commit: true).Count > 0 || value.IsError)
            {
                whole = false;
            }
            else if (ConstantText(value) is { } part)
            {
                text.Append(part);
            }
            else
            {
                NotKnown(hole, "a hole of the text");
                whole = false;
            }
        }

        return whole ? text.ToString() : null;
    }

    /// <summary>The text of a constant as A2 and A4 give it; null for one whose text the build does not give while the program compiles.</summary>
    private static string? ConstantText(Typed t) => t.Constant switch
    {
        IntegerConstant i => i.Value.ToString(CultureInfo.InvariantCulture),
        BoolConstant b => b.Value ? "true" : "false",
        StringConstant s => s.Value,
        CharConstant c => char.ConvertFromUtf32(c.Value),
        EnumConstant e when t.Type is EnumType type => type.Symbol.Values[e.Index],
        _ => null,
    };

    /// <summary>
    /// A part of a <c>const assert</c> that is not known while the program compiles: one that reads a variable or a parameter is
    /// <c>const-assert-failed</c> at its name, as Rust's E0435 refuses one in a constant; any other is <c>not supported</c>.
    /// </summary>
    private void NotKnown(Expression part, string what)
    {
        NameExpression? variable = null;
        SyntaxCopy.Walk(new ExpressionStatement(part.Line, part), e =>
            variable ??= e is NameExpression n && Lookup(n.Name, capture: false).Variable is not null ? n : null);
        if (variable is not null)
        {
            Report(ErrorIds.ConstAssertFailed, variable.Line, $"the value of `{variable.Name}` is not known while the program compiles, in {what} of a `const assert` (T36)");
        }
        else
        {
            NotSupported(part.Line, $"{what} of a `const assert` beyond what the build evaluates while the program compiles: literals, constants, operators on them, `T.Kind`, `f.Name` and constant type arguments (T36)");
        }
    }
}
