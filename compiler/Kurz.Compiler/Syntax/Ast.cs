namespace Kurz.Compiler.Syntax;

/// <summary>
/// The syntax tree. Every node carries the 1-based line a diagnostic about it names (E1): for a
/// binary operator the line of the operator, for an argument its own line, for a statement the
/// line it starts on.
/// </summary>
public abstract record Node(int Line);

// ---- types -------------------------------------------------------------------------------------

public abstract record TypeSyntax(int Line) : Node(Line);

/// <summary>A type by name, a built-in one (L19) or a declared one, with its type arguments (T17): <c>int</c>, <c>List&lt;int&gt;</c>.</summary>
public sealed record NamedType(int Line, string Name, IReadOnlyList<TypeSyntax> TypeArguments) : TypeSyntax(Line);

/// <summary><c>T?</c> (N1).</summary>
public sealed record NullableType(int Line, TypeSyntax Inner) : TypeSyntax(Line);

/// <summary><c>A | B</c> (D9).</summary>
public sealed record UnionType(int Line, IReadOnlyList<TypeSyntax> Cases) : TypeSyntax(Line);

/// <summary><c>(int) => bool</c> (F11).</summary>
public sealed record FunctionType(int Line, IReadOnlyList<TypeSyntax> Parameters, TypeSyntax Result) : TypeSyntax(Line);

/// <summary><c>weak T?</c> (R3, R5). The inner type is what was written after <c>weak</c>, with or without its <c>?</c>.</summary>
public sealed record WeakType(int Line, TypeSyntax Inner) : TypeSyntax(Line);

/// <summary><c>void</c> (F4), as a result type.</summary>
public sealed record VoidType(int Line) : TypeSyntax(Line);

// ---- expressions -------------------------------------------------------------------------------

public abstract record Expression(int Line) : Node(Line);

public sealed record IntegerLiteral(int Line, IntegerLiteralValue Value) : Expression(Line);

public sealed record RealLiteral(int Line, RealLiteralValue Value) : Expression(Line);

public sealed record UnitLiteral(int Line, UnitLiteralValue Value) : Expression(Line);

public abstract record StringSegment;

public sealed record TextSegment(string Text) : StringSegment;

/// <summary>An interpolation (L6).</summary>
public sealed record HoleSegment(Expression Expression) : StringSegment;

public sealed record StringLiteral(int Line, IReadOnlyList<StringSegment> Segments) : Expression(Line)
{
    public bool IsPlain => Segments.All(s => s is TextSegment);
}

public sealed record CharLiteral(int Line, int CodePoint) : Expression(Line);

public sealed record BoolLiteral(int Line, bool Value) : Expression(Line);

public sealed record NullLiteral(int Line) : Expression(Line);

/// <summary>A name: a variable, a function, a type used as a value (D8) or as a conversion (T10).</summary>
public sealed record NameExpression(int Line, string Name) : Expression(Line);

public sealed record ThisExpression(int Line) : Expression(Line);

/// <summary><c>a.B</c>, or <c>a?.B</c> when <paramref name="Conditional"/> (N5). The line is the line of the dot.</summary>
public sealed record MemberAccess(int Line, Expression Target, string Member, bool Conditional) : Expression(Line);

/// <summary>An argument of a call: by position or by name (D11), with <c>mut</c> in front for a <c>mut</c> parameter (M7).</summary>
public sealed record Argument(int Line, string? Name, bool Mut, Expression Value);

/// <summary>A call, a construction (D2) or a conversion (T10); type arguments where the call writes them (T19).</summary>
public sealed record CallExpression(int Line, Expression Callee, IReadOnlyList<TypeSyntax> TypeArguments, IReadOnlyList<Argument> Arguments) : Expression(Line);

public sealed record IndexExpression(int Line, Expression Target, Expression Index) : Expression(Line);

/// <summary><c>-x</c>, <c>!x</c>, <c>~x</c>.</summary>
public sealed record UnaryExpression(int Line, TokenKind Operator, Expression Operand) : Expression(Line);

/// <summary>A binary operator, <c>??</c> included; the line is the operator's.</summary>
public sealed record BinaryExpression(int Line, TokenKind Operator, Expression Left, Expression Right) : Expression(Line);

/// <summary><c>a..b</c> or <c>a..&lt;b</c> (C8); the line is the operator's.</summary>
public sealed record RangeExpression(int Line, Expression Start, Expression End, bool Inclusive) : Expression(Line);

/// <summary>A lambda (F7): parameters with or without types, and an expression or a block as its body.</summary>
public sealed record LambdaExpression(int Line, IReadOnlyList<Parameter> Parameters, Expression? ExpressionBody, Block? BlockBody) : Expression(Line);

/// <summary><c>match</c> used as an expression (C9).</summary>
public sealed record MatchExpression(int Line, Expression Subject, IReadOnlyList<MatchArm> Arms) : Expression(Line);

public sealed record FieldAssignment(int Line, string Field, Expression Value);

/// <summary><c>value with { Field = expression }</c> (D5).</summary>
public sealed record WithExpression(int Line, Expression Target, IReadOnlyList<FieldAssignment> Assignments) : Expression(Line);

/// <summary>The postfix <c>else</c> of a call (O5).</summary>
public sealed record ElseExpression(int Line, Expression Call, IReadOnlyList<MatchArm> Arms) : Expression(Line);

public sealed record ParenthesizedExpression(int Line, Expression Inner) : Expression(Line);

// ---- match -------------------------------------------------------------------------------------

public abstract record Pattern(int Line) : Node(Line);

/// <summary>An arm that names a case type, optionally binding the value as that type (C4): <c>Circle c</c>, <c>NotFound</c>, <c>int n</c>.</summary>
public sealed record TypePattern(int Line, TypeSyntax Type, string? Binding) : Pattern(Line);

/// <summary>An arm that names a value (C9, D12): a literal, or a value such as <c>Plan.Free</c>.</summary>
public sealed record ValuePattern(int Line, Expression Value) : Pattern(Line);

public sealed record ElsePattern(int Line) : Pattern(Line);

/// <summary>One arm. Its body is a statement: an expression, a block, <c>break</c>, <c>return</c> or <c>throw</c>.</summary>
public sealed record MatchArm(int Line, Pattern Pattern, Statement Body);

// ---- statements --------------------------------------------------------------------------------

public abstract record Statement(int Line) : Node(Line);

/// <summary>Statements between braces. <paramref name="EndLine"/> is the line of the closing brace.</summary>
public sealed record Block(int Line, IReadOnlyList<Statement> Statements, int EndLine) : Statement(Line);

/// <summary>A declaration with <c>mut</c> or a written type (V2, V3, V4, V9): <c>mut x = 1</c>, <c>int x = 1</c>, <c>mut int x = 1</c>.</summary>
public sealed record VariableDeclaration(int Line, bool Mut, TypeSyntax? Type, string Name, Expression Value) : Statement(Line);

/// <summary><c>name = expression</c>: a declaration or an assignment, which V8 decides by the names in scope.</summary>
public sealed record AssignOrDeclare(int Line, string Name, Expression Value) : Statement(Line);

/// <summary>An assignment into a path or an index (M4, M9): <c>a.B = v</c>, <c>xs[i] = v</c>.</summary>
public sealed record Assignment(int Line, Expression Target, Expression Value) : Statement(Line);

public sealed record ExpressionStatement(int Line, Expression Expression) : Statement(Line);

/// <summary><c>if</c> with its block and, after the block's brace, <c>else</c> or <c>else if</c> (C1, C2). <paramref name="Else"/> is a <see cref="Block"/> or an <see cref="IfStatement"/>.</summary>
public sealed record IfStatement(int Line, Expression Condition, Block Then, Statement? Else) : Statement(Line);

public sealed record WhileStatement(int Line, Expression Condition, Block Body) : Statement(Line);

/// <summary><c>for name in collection { }</c> (C6, C8).</summary>
public sealed record ForInStatement(int Line, string Variable, int VariableLine, Expression Collection, Block Body) : Statement(Line);

public sealed record MatchStatement(int Line, Expression Subject, IReadOnlyList<MatchArm> Arms) : Statement(Line);

public sealed record BreakStatement(int Line) : Statement(Line);

public sealed record ContinueStatement(int Line) : Statement(Line);

public sealed record ReturnStatement(int Line, Expression? Value) : Statement(Line);

/// <summary><c>throw</c> with a value, or bare in an arm of <c>else</c> (O7).</summary>
public sealed record ThrowStatement(int Line, Expression? Value) : Statement(Line);

/// <summary>A <c>raw</c> block (R7).</summary>
public sealed record RawBlock(int Line, Block Body) : Statement(Line);

/// <summary><c>use folder</c> (F10).</summary>
public sealed record UseStatement(int Line, string Target) : Statement(Line);

// ---- declarations ------------------------------------------------------------------------------

[Flags]
public enum Modifiers
{
    None = 0,
    Pub = 1,
    Prot = 2,
    Static = 4,
    Mut = 8,
    Virtual = 16,
    Override = 32,
}

/// <summary>A parameter of a function, a lambda or a primary constructor: <c>mut</c> (M7), a type (absent on an untyped lambda parameter), a name and a default (D11).</summary>
public sealed record Parameter(int Line, bool Mut, TypeSyntax? Type, string Name, Expression? Default);

/// <summary>A type parameter with its limit (T19): <c>T</c>, <c>T: Comparable</c>.</summary>
public sealed record TypeParameter(int Line, string Name, TypeSyntax? Limit);

/// <summary>A function or a method (F1 to F4, K7). Both bodies are null for the signature of an interface method.</summary>
public sealed record FunctionDeclaration(
    int Line,
    Modifiers Modifiers,
    TypeSyntax ReturnType,
    string Name,
    IReadOnlyList<TypeParameter> TypeParameters,
    IReadOnlyList<Parameter> Parameters,
    Expression? ExpressionBody,
    Block? BlockBody) : Statement(Line);

/// <summary>A field written in a class body (K7): <c>mut int count = 0</c>, <c>pub static int Step = 2</c>, <c>int x</c>.</summary>
public sealed record FieldDeclaration(int Line, Modifiers Modifiers, TypeSyntax Type, string Name, Expression? Value) : Statement(Line);

/// <summary>A further constructor (K14): <c>pub Counter() : this(0) { }</c>.</summary>
public sealed record ConstructorDeclaration(int Line, Modifiers Modifiers, string Name, IReadOnlyList<Parameter> Parameters, IReadOnlyList<Argument>? ThisArguments, Block Body) : Statement(Line);

public enum TypeDeclarationKind
{
    Data,
    Class,
    Interface,
    Enum,
    Flags,
}

/// <summary>A base class or an interface after the colon (K6, K10), with the arguments of the explicit form.</summary>
public sealed record BaseSpecifier(int Line, TypeSyntax Type, IReadOnlyList<Argument>? Arguments);

/// <summary>A value of an <c>enum</c> or a name of a <c>flags</c> type, with its number where written (D18, D19).</summary>
public sealed record EnumMember(int Line, string Name, Expression? Number);

/// <summary>
/// <c>data</c>, <c>class</c>, <c>interface</c>, <c>enum</c> or <c>flags</c>. The primary constructor is
/// null when none is written (a class without one, D8's bare <c>data</c> name). Members are the
/// fields, methods and constructors of the body; values are the members of an enum or flags type.
/// </summary>
public sealed record TypeDeclaration(
    int Line,
    TypeDeclarationKind Kind,
    string Name,
    IReadOnlyList<TypeParameter> TypeParameters,
    IReadOnlyList<Parameter>? PrimaryConstructor,
    IReadOnlyList<BaseSpecifier> Bases,
    IReadOnlyList<string> EqualBy,
    IReadOnlyList<Statement> Members,
    IReadOnlyList<EnumMember> Values) : Statement(Line);

/// <summary>One file: its statements and declarations in source order (F5).</summary>
public sealed record ProgramSyntax(IReadOnlyList<Statement> Statements);
