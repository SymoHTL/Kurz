using System.Globalization;
using System.Text;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Writes a syntax tree as one line of nested brackets, so that a test states the whole shape it
/// expects: <c>(declare x (int 1))</c>. Lines are not printed; tests that are about a line read it
/// from the node.
/// </summary>
internal static class SyntaxPrinter
{
    public static string Print(ProgramSyntax program) => string.Join(" ", program.Statements.Select(Print));

    public static string Print(Statement statement) => statement switch
    {
        Block b => $"{{{string.Join(" ", b.Statements.Select(Print))}}}",
        VariableDeclaration v => $"({(v.Mut ? "mut-" : "")}declare {Type(v.Type)}{v.Name} {Print(v.Value)})",
        AssignOrDeclare a => $"(assign-or-declare {a.Name} {Print(a.Value)})",
        Assignment a => $"(assign {Print(a.Target)} {Print(a.Value)})",
        ExpressionStatement e => Print(e.Expression),
        IfStatement i => $"(if {Print(i.Condition)} {Print(i.Then)}{(i.Else is null ? "" : " else " + Print(i.Else))})",
        WhileStatement w => $"(while {Print(w.Condition)} {Print(w.Body)})",
        ForInStatement f => $"(for {f.Variable} in {Print(f.Collection)} {Print(f.Body)})",
        MatchStatement m => $"(match {Print(m.Subject)} {Arms(m.Arms)})",
        BreakStatement => "(break)",
        ContinueStatement => "(continue)",
        ReturnStatement r => r.Value is null ? "(return)" : $"(return {Print(r.Value)})",
        ThrowStatement t => t.Value is null ? "(throw)" : $"(throw {Print(t.Value)})",
        RawBlock r => $"(raw {Print(r.Body)})",
        UseStatement u => $"(use {u.Target})",
        FunctionDeclaration f => Function(f),
        FieldDeclaration f => $"(field {Mods(f.Modifiers)}{Type(f.Type)}{f.Name}{(f.Value is null ? "" : " " + Print(f.Value))})",
        ConstructorDeclaration c => $"(constructor {Mods(c.Modifiers)}{c.Name}({Parameters(c.Parameters)}){(c.ThisArguments is null ? "" : " this(" + Arguments(c.ThisArguments) + ")")} {Print(c.Body)})",
        TypeDeclaration t => TypeDeclaration(t),
        _ => throw new InvalidOperationException(statement.GetType().Name),
    };

    private static string Function(FunctionDeclaration f)
    {
        var typeParameters = f.TypeParameters.Count == 0 ? "" : "<" + string.Join(", ", f.TypeParameters.Select(p => p.Limit is null ? p.Name : $"{p.Name}: {Type(p.Limit)}".TrimEnd())) + ">";
        var body = f.ExpressionBody is not null ? " => " + Print(f.ExpressionBody) : f.BlockBody is not null ? " " + Print(f.BlockBody) : "";
        return $"(function {Mods(f.Modifiers)}{Type(f.ReturnType)}{f.Name}{typeParameters}({Parameters(f.Parameters)}){body})";
    }

    private static string TypeDeclaration(TypeDeclaration t)
    {
        var sb = new StringBuilder("(").Append(t.Kind.ToString().ToLowerInvariant()).Append(' ').Append(t.Name);
        if (t.TypeParameters.Count > 0)
        {
            sb.Append('<').Append(string.Join(", ", t.TypeParameters.Select(p => p.Name))).Append('>');
        }

        if (t.PrimaryConstructor is not null)
        {
            sb.Append('(').Append(Parameters(t.PrimaryConstructor)).Append(')');
        }

        foreach (var b in t.Bases)
        {
            sb.Append(" : ").Append(Type(b.Type).TrimEnd());
            if (b.Arguments is not null)
            {
                sb.Append('(').Append(Arguments(b.Arguments)).Append(')');
            }
        }

        if (t.EqualBy.Count > 0)
        {
            sb.Append(" equal by ").Append(string.Join(", ", t.EqualBy));
        }

        if (t.Values.Count > 0)
        {
            sb.Append(" {").Append(string.Join(", ", t.Values.Select(v => v.Number is null ? v.Name : $"{v.Name} = {Print(v.Number)}"))).Append('}');
        }

        if (t.Members.Count > 0)
        {
            sb.Append(" {").Append(string.Join(" ", t.Members.Select(Print))).Append('}');
        }

        return sb.Append(')').ToString();
    }

    private static string Mods(Modifiers modifiers) => modifiers == Modifiers.None ? "" : modifiers.ToString().ToLowerInvariant().Replace(", ", " ", StringComparison.Ordinal) + " ";

    private static string Parameters(IReadOnlyList<Parameter> parameters) =>
        string.Join(", ", parameters.Select(p => $"{(p.Mut ? "mut " : "")}{Type(p.Type)}{p.Name}{(p.Default is null ? "" : " = " + Print(p.Default))}"));

    private static string Arguments(IReadOnlyList<Argument> arguments) =>
        string.Join(", ", arguments.Select(a => $"{(a.Name is null ? "" : a.Name + ": ")}{(a.Mut ? "mut " : "")}{Print(a.Value)}"));

    private static string Arms(IReadOnlyList<MatchArm> arms) => "[" + string.Join(" ", arms.Select(a => $"({Pattern(a.Pattern)} => {Print(a.Body)})")) + "]";

    private static string Pattern(Pattern pattern) => pattern switch
    {
        ElsePattern => "else",
        ValuePattern v => Print(v.Value),
        TypePattern t => Type(t.Type).TrimEnd() + (t.Binding is null ? "" : " " + t.Binding),
        _ => throw new InvalidOperationException(pattern.GetType().Name),
    };

    /// <summary>A type followed by a space, or nothing for an absent type.</summary>
    public static string Type(TypeSyntax? type) => type switch
    {
        null => "",
        NamedType n => n.Name + (n.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", n.TypeArguments.Select(a => Type(a).TrimEnd())) + ">") + " ",
        NullableType { Inner: FunctionType } n => "(" + Type(n.Inner).TrimEnd() + ")? ",
        NullableType n => Type(n.Inner).TrimEnd() + "? ",
        UnionType u => string.Join(" | ", u.Cases.Select(c => Type(c).TrimEnd())) + " ",
        FunctionType f => "(" + string.Join(", ", f.Parameters.Select(p => Type(p).TrimEnd())) + ") => " + Type(f.Result),
        WeakType w => "weak " + Type(w.Inner),
        VoidType => "void ",
        _ => throw new InvalidOperationException(type.GetType().Name),
    };

    public static string Print(Expression expression) => expression switch
    {
        IntegerLiteral i => $"(int {i.Value.Digits}{(i.Value.Radix == 10 ? "" : "r" + i.Value.Radix.ToString(CultureInfo.InvariantCulture))}{i.Value.Suffix})",
        RealLiteral r => $"(real {r.Value.Text}{r.Value.Suffix})",
        UnitLiteral u => $"(unit {u.Value.Number}{u.Value.Unit})",
        StringLiteral s => "(string " + string.Join(" ", s.Segments.Select(seg => seg is TextSegment t ? Quote(t.Text) : Print(((HoleSegment)seg).Expression))) + ")",
        CharLiteral c => $"(char {c.CodePoint})",
        BoolLiteral b => b.Value ? "true" : "false",
        NullLiteral => "null",
        NameExpression n => n.Name,
        ThisExpression => "this",
        MemberAccess m => $"({(m.Conditional ? "?." : ".")} {Print(m.Target)} {m.Member})",
        CallExpression c => $"(call {Print(c.Callee)}{(c.TypeArguments.Count == 0 ? "" : "<" + string.Join(", ", c.TypeArguments.Select(t => Type(t).TrimEnd())) + ">")}{(c.Arguments.Count == 0 ? "" : " " + Arguments(c.Arguments))})",
        IndexExpression i => $"(index {Print(i.Target)} {Print(i.Index)})",
        UnaryExpression u => $"({Op(u.Operator)} {Print(u.Operand)})",
        BinaryExpression b => $"({Op(b.Operator)} {Print(b.Left)} {Print(b.Right)})",
        RangeExpression r => $"({(r.Inclusive ? ".." : "..<")} {Print(r.Start)} {Print(r.End)})",
        LambdaExpression l => $"(lambda ({Parameters(l.Parameters)}) {(l.ExpressionBody is not null ? Print(l.ExpressionBody) : Print(l.BlockBody!))})",
        MatchExpression m => $"(match-expression {Print(m.Subject)} {Arms(m.Arms)})",
        WithExpression w => $"(with {Print(w.Target)} {string.Join(", ", w.Assignments.Select(a => a.Field + " = " + Print(a.Value)))})",
        ElseExpression e => $"(else {Print(e.Call)} {Arms(e.Arms)})",
        ParenthesizedExpression p => $"(paren {Print(p.Inner)})",
        _ => throw new InvalidOperationException(expression.GetType().Name),
    };

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Op(TokenKind kind) => kind switch
    {
        TokenKind.Plus => "+",
        TokenKind.Minus => "-",
        TokenKind.Star => "*",
        TokenKind.Slash => "/",
        TokenKind.Percent => "%",
        TokenKind.PlusPercent => "+%",
        TokenKind.MinusPercent => "-%",
        TokenKind.StarPercent => "*%",
        TokenKind.EqualEqual => "==",
        TokenKind.NotEqual => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        TokenKind.AmpersandAmpersand => "&&",
        TokenKind.BarBar => "||",
        TokenKind.Bang => "!",
        TokenKind.Ampersand => "&",
        TokenKind.Bar => "|",
        TokenKind.Caret => "^",
        TokenKind.Tilde => "~",
        TokenKind.LessLess => "<<",
        TokenKind.GreaterGreater => ">>",
        TokenKind.QuestionQuestion => "??",
        _ => kind.ToString(),
    };
}
