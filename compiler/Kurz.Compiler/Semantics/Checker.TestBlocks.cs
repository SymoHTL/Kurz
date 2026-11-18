using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// The tests of a program (record, section 11; chapter 20 of the reference): <c>test "name" { ... }</c> at the top level of a file, each
/// checked as a function of its own (Y1, Y5); <c>check condition</c> in any code (Y6); and the forms the checker says <c>not supported</c>
/// to, the cluster test of section 13 (Y8), <c>mock X with Y</c> (Y9) and <c>advance d</c> (Y10). Every command checks every test, so that an
/// error in one refuses a build (Y7).
/// </summary>
public sealed partial class Checker
{
    private readonly List<(TestDeclaration Test, FunctionSymbol Symbol)> tests = [];

    /// <summary>
    /// Y1, Y5: a test is a function of the file with no parameter and a <c>void</c> result, which no call can name, so that its body reads
    /// no variable of the top-level code (F16) and no <c>args</c> (S13), and a failure case that can leave it is <c>unlisted-case</c> (O3).
    /// A second test of one name is <c>syntax</c> at its line, its text naming the line of the first (E4). The cluster test is no function
    /// (Y8).
    /// </summary>
    private void DeclareTest(TestDeclaration t)
    {
        if (t.Cluster)
        {
            return;
        }

        if (tests.FirstOrDefault(x => x.Test.Name == t.Name).Test is { } first)
        {
            Report(ErrorIds.Syntax, t.Line, $"a second test named {t.Written}, whose first stands at line {first.Line}: a test's name is its own (Y1)");
        }

        var declaration = new FunctionDeclaration(t.Line, Modifiers.None, new Syntax.VoidType(t.Line), $"test {t.Written}", [], [], null, t.Body);
        var symbol = new FunctionSymbol(declaration.Name, declaration) { ReturnType = Types.Void };
        tests.Add((t, symbol));
        model.RecordTest(t, symbol);
    }

    /// <summary>Y5, Y7: the body of every test, as a function's body is checked, whatever command the build is for.</summary>
    private void CheckTestBodies()
    {
        foreach (var (_, symbol) in tests)
        {
            CheckBody(symbol);
        }
    }

    /// <summary>Y1, Y8: a test at the top level is no code of the top-level code; the cluster test of section 13 is <c>not supported</c>.</summary>
    private void CheckTestDeclaration(TestDeclaration t)
    {
        if (t.Cluster)
        {
            NotSupported(t.Line, "`test cluster N seed S { ... }`, the cluster test of section 13 (Y8)");
        }
    }

    /// <summary>
    /// Y6: the condition of <c>check</c> is a <c>bool</c>, read as a condition is (C3): a value of another type is <c>type-mismatch</c> at its
    /// line, a nullable one <c>nullable-unchecked</c> and a call with a <c>void</c> success <c>syntax</c> (O10). The model keeps its text and,
    /// for one of the six comparisons, alone or in parentheses, whose two sides each show their value (<see cref="Shows"/>), each side's
    /// type, whose values its failure shows (F10).
    /// </summary>
    private void CheckAssertion(AssertionStatement a)
    {
        CheckCondition(a.Condition);
        var shown = Unwrap(a.Condition) is BinaryExpression
        {
            Operator: TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual,
        } comparison
            && model.HasType(comparison.Left) && model.HasType(comparison.Right)
            && Shows(model.TypeOf(comparison.Left).Type) && Shows(model.TypeOf(comparison.Right).Type)
            ? comparison
            : null;
        model.RecordAssertion(a, new Assertion(a.Text, shown, shown is null ? null : model.TypeOf(shown.Left).Type, shown is null ? null : model.TypeOf(shown.Right).Type));
    }

    /// <summary>
    /// Y6 with F10: whether a side of a comparison shows its value in the failure's text, as a hole of an interpolation shows it (L6): a value
    /// with a text (A2 to A15), which a vector has not (R18), that holds no raw pointer and no C function value, whose texts no rule gives,
    /// and reaches no <c>Text()</c> without <c>pub</c>, which no <c>print</c> reaches (A9, A15). A side that does not shows nothing, and its
    /// condition its text alone.
    /// </summary>
    private static bool Shows(KurzType type) => HasText(type) && !HoldsRaw(type, []) && PrivateTextClass(type, []) is null;

    /// <summary>Y9: <c>mock X with Y</c>, whose types the checker does not resolve, is <c>not supported</c> while what it accepts is open.</summary>
    private void CheckMock(MockStatement m) => NotSupported(m.Line, "`mock X with Y` (Y9)");

    /// <summary>
    /// Y10: <c>advance d</c> is <c>not supported</c> while where it stands is open; its duration is checked as any expression is, and as the
    /// bound of <c>within</c> is (U40): a value of another type than <c>duration</c> is <c>type-mismatch</c> at its line, a nullable one
    /// <c>nullable-unchecked</c>, a call with a <c>void</c> success <c>syntax</c> (O10).
    /// </summary>
    private void CheckAdvance(AdvanceStatement a)
    {
        CheckBound(a.Duration, "the duration of `advance`", "Y10");
        NotSupported(a.Line, "`advance` (Y10)");
    }
}
