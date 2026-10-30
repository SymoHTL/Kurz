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

/// <summary><c>int*</c>, a raw pointer to what <paramref name="Pointee"/> names, <c>void*</c> among them (R10, R14).</summary>
public sealed record PointerType(int Line, TypeSyntax Pointee) : TypeSyntax(Line);

/// <summary><c>extern "C" (int) => void</c>, the type of a function that takes the C calling convention (R17).</summary>
public sealed record ExternFunctionType(int Line, IReadOnlyList<TypeSyntax> Parameters, TypeSyntax Result) : TypeSyntax(Line);

// ---- expressions -------------------------------------------------------------------------------

public abstract record Expression(int Line) : Node(Line);

/// <summary>An integer literal (L10, L11).</summary>
public sealed record IntegerLiteral(int Line, IntegerLiteralValue Value) : Expression(Line);

/// <summary>A literal with a fraction, an exponent or a floating-point or decimal suffix (L11).</summary>
public sealed record RealLiteral(int Line, RealLiteralValue Value) : Expression(Line);

/// <summary>A number with a unit (L14).</summary>
public sealed record UnitLiteral(int Line, UnitLiteralValue Value) : Expression(Line);

public abstract record StringSegment;

public sealed record TextSegment(string Text) : StringSegment;

/// <summary>An interpolation (L6).</summary>
public sealed record HoleSegment(Expression Expression) : StringSegment;

/// <summary>A string literal, one line or a <c>"""</c> block, as text and holes (L6, L7, L13).</summary>
public sealed record StringLiteral(int Line, IReadOnlyList<StringSegment> Segments) : Expression(Line)
{
    public bool IsPlain => Segments.All(s => s is TextSegment);
}

/// <summary>A <c>char</c> literal (T21).</summary>
public sealed record CharLiteral(int Line, int CodePoint) : Expression(Line);

/// <summary><c>true</c> or <c>false</c> (L8).</summary>
public sealed record BoolLiteral(int Line, bool Value) : Expression(Line);

/// <summary><c>null</c> (N1).</summary>
public sealed record NullLiteral(int Line) : Expression(Line);

/// <summary>A name: a variable, a function, a type used as a value (D8) or as a conversion (T10).</summary>
public sealed record NameExpression(int Line, string Name) : Expression(Line);

/// <summary><c>this</c>: the instance inside a class body (K7, as in C#). The <c>: this(...)</c> of a further constructor (K14) is no expression; its arguments are <see cref="ConstructorDeclaration.ThisArguments"/>.</summary>
public sealed record ThisExpression(int Line) : Expression(Line);

/// <summary><c>a.B</c>, or <c>a?.B</c> when <paramref name="Conditional"/> (N5). The line is the line of the dot.</summary>
public sealed record MemberAccess(int Line, Expression Target, string Member, bool Conditional) : Expression(Line);

/// <summary><c>List&lt;T&gt;.FromRaw</c>: a member of a type with type arguments, which R15 writes before a call. The line is the line of the dot.</summary>
public sealed record TypeMemberAccess(int Line, TypeSyntax Type, string Member) : Expression(Line);

/// <summary>An argument of a call: by position or by name (D11), with <c>mut</c> in front for a <c>mut</c> parameter (M7).</summary>
public sealed record Argument(int Line, string? Name, bool Mut, Expression Value);

/// <summary>A call, a construction (D2) or a conversion (T10); type arguments where the call writes them (T19).</summary>
public sealed record CallExpression(int Line, Expression Callee, IReadOnlyList<TypeSyntax> TypeArguments, IReadOnlyList<Argument> Arguments) : Expression(Line);

/// <summary><c>list[i]</c> or <c>map[key]</c> (M9); the line is the bracket's.</summary>
public sealed record IndexExpression(int Line, Expression Target, Expression Index) : Expression(Line);

/// <summary><c>-x</c> and <c>!x</c> (T18), <c>~x</c> (T22), and of a raw pointer the read <c>*p</c> and the address <c>&amp;x</c> (R10).</summary>
public sealed record UnaryExpression(int Line, TokenKind Operator, Expression Operand) : Expression(Line);

/// <summary>A binary operator of T18, T22 or T12, or N4's <c>??</c>; the line is the operator's (E1).</summary>
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

/// <summary>An expression in parentheses, which is an expression like any other (C1).</summary>
public sealed record ParenthesizedExpression(int Line, Expression Inner) : Expression(Line);

// ---- match -------------------------------------------------------------------------------------

public abstract record Pattern(int Line) : Node(Line);

/// <summary>An arm that names a case type, optionally binding the value as that type (C4): <c>Circle c</c>, <c>NotFound</c>, <c>int n</c>.</summary>
public sealed record TypePattern(int Line, TypeSyntax Type, string? Binding) : Pattern(Line);

/// <summary>An arm that names a value (C9, D12): a literal, or a value such as <c>Plan.Free</c>.</summary>
public sealed record ValuePattern(int Line, Expression Value) : Pattern(Line);

/// <summary>The <c>else</c> arm, which matches whatever no arm before it matched (C9).</summary>
public sealed record ElsePattern(int Line) : Pattern(Line);

/// <summary>One arm. Its body is a statement: an expression, a block, <c>break</c>, <c>return</c> or <c>throw</c>.</summary>
public sealed record MatchArm(int Line, Pattern Pattern, Statement Body);

// ---- statements --------------------------------------------------------------------------------

public abstract record Statement(int Line) : Node(Line);

/// <summary>Statements between braces. <paramref name="EndLine"/> is the line of the closing brace.</summary>
public sealed record Block(int Line, IReadOnlyList<Statement> Statements, int EndLine) : Statement(Line);

/// <summary>
/// A declaration with <c>mut</c> or a written type (V2, V3, V4, V9): <c>mut x = 1</c>, <c>int x = 1</c>, <c>mut int x = 1</c>. Every
/// modifier written in front of it stays on the node: which of them a variable may carry is the checker's to say. <paramref name="Value"/>
/// is null on a <c>static mut</c> written without one, which R13 makes <c>static-state</c>.
/// </summary>
public sealed record VariableDeclaration(int Line, Modifiers Modifiers, TypeSyntax? Type, string Name, Expression? Value) : Statement(Line)
{
    public bool Mut => (Modifiers & Modifiers.Mut) != 0;
}

/// <summary><c>name = expression</c>: a declaration or an assignment, which V8 decides by the names in scope.</summary>
public sealed record AssignOrDeclare(int Line, string Name, Expression Value) : Statement(Line);

/// <summary>An assignment into a path or an index (M4, M9): <c>a.B = v</c>, <c>xs[i] = v</c>.</summary>
public sealed record Assignment(int Line, Expression Target, Expression Value) : Statement(Line);

/// <summary>An expression, a call as a rule, as a statement (F5: statements at the top level, F3: in a body).</summary>
public sealed record ExpressionStatement(int Line, Expression Expression) : Statement(Line);

/// <summary><c>if</c> with its block and, after the block's brace, <c>else</c> or <c>else if</c> (C1, C2). <paramref name="Else"/> is a <see cref="Block"/> or an <see cref="IfStatement"/>.</summary>
public sealed record IfStatement(int Line, Expression Condition, Block Then, Statement? Else) : Statement(Line);

/// <summary><c>while condition { }</c> (C5).</summary>
public sealed record WhileStatement(int Line, Expression Condition, Block Body) : Statement(Line);

/// <summary><c>for name in collection { }</c> (C6, C8), and <c>for key, value in map { }</c>, whose second name is <paramref name="Second"/> (C6).</summary>
public sealed record ForInStatement(int Line, string Variable, int VariableLine, Expression Collection, Block Body, LoopName? Second = null) : Statement(Line);

/// <summary>The second name of <c>for key, value in map { }</c>, which takes an entry's value (C6).</summary>
public sealed record LoopName(int Line, string Name) : Node(Line);

/// <summary><c>match value { }</c> as a statement (C4, C9).</summary>
public sealed record MatchStatement(int Line, Expression Subject, IReadOnlyList<MatchArm> Arms) : Statement(Line);

/// <summary><c>break</c> (C7).</summary>
public sealed record BreakStatement(int Line) : Statement(Line);

/// <summary><c>continue</c> (C7).</summary>
public sealed record ContinueStatement(int Line) : Statement(Line);

/// <summary><c>return</c> with or without a value (F3).</summary>
public sealed record ReturnStatement(int Line, Expression? Value) : Statement(Line);

/// <summary><c>throw</c> with a value, or bare in an arm of <c>else</c> (O7).</summary>
public sealed record ThrowStatement(int Line, Expression? Value) : Statement(Line);

/// <summary>A <c>raw</c> block (R7).</summary>
public sealed record RawBlock(int Line, Block Body) : Statement(Line);

/// <summary><c>use folder</c> (F10).</summary>
public sealed record UseStatement(int Line, string Target) : Statement(Line);

/// <summary>
/// An <c>asm</c> statement (R11): its templates, each a string literal whose <c>{name}</c> names an operand and is no
/// interpolation (L6), then its operands.
/// </summary>
public sealed record AsmStatement(int Line, IReadOnlyList<AsmTemplate> Templates, IReadOnlyList<AsmOperand> Operands) : Statement(Line);

/// <summary>A template of an <c>asm</c>, its text with the escapes of a string literal read and its braces as written.</summary>
public sealed record AsmTemplate(int Line, string Text);

/// <summary>
/// An operand of an <c>asm</c> as Rust writes one (R11): an optional <c>name =</c>, a word, then the list in parentheses of
/// <c>in(reg)</c>, <c>out("eax")</c>, <c>clobber_abi("C")</c> or <c>options(nomem, nostack)</c>, then the value, with the place
/// after <c>=&gt;</c> where it is split (<c>inout(reg) x =&gt; y</c>); <c>const</c> and <c>sym</c> take the value alone, and
/// <c>label</c> takes a block. A discarded output, Rust's <c>_</c>, is the name <c>_</c>. Which words, register classes and shapes
/// are Rust's is the checker's (<c>asm-operand</c>).
/// </summary>
public sealed record AsmOperand(int Line, string? Name, string Word, IReadOnlyList<AsmArgument>? Arguments, Expression? Value, Expression? Output, Block? Body);

/// <summary>A word or a string in the parentheses of an operand: a register class, a register (<paramref name="Quoted"/>), an ABI or an option.</summary>
public sealed record AsmArgument(int Line, string Text, bool Quoted);

/// <summary>
/// A block of the functions of a C library (R16): <c>extern "C" { long write(int fd, byte* buf, long n) }</c>, with the library
/// after <c>in</c> where one is named, <c>extern "C" in "dbghelp" { ... }</c>.
/// </summary>
public sealed record ExternBlock(int Line, string? Library, IReadOnlyList<ExternFunction> Functions) : Statement(Line);

/// <summary>A function a block of R16 declares, with no body; <paramref name="Variadic"/> where its last parameter is <c>...</c>.</summary>
public sealed record ExternFunction(int Line, Modifiers Modifiers, TypeSyntax ReturnType, string Name, IReadOnlyList<Parameter> Parameters, bool Variadic);

// ---- declarations ------------------------------------------------------------------------------

/// <summary>The modifiers a declaration can carry, as flags: K7's <c>pub</c>, <c>prot</c>, <c>static</c>, <c>virtual</c> and <c>override</c>, F9's <c>pub</c>, V2's and M6's <c>mut</c>, R12's <c>naked</c>, R13's <c>thread</c>, and <c>extern "C"</c>, the C calling convention of a function (R16) and the C layout of a data type (R17).</summary>
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
    Naked = 64,
    Thread = 128,
    Extern = 256,
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

/// <summary>A field written in a type body (K7): <c>mut int count = 0</c>, <c>pub static int Step = 2</c>, <c>int x</c>, or <c>mut count = 0</c> without a written type, the reading on K7 with V2 (K7 shows a field as <c>Type name</c>; V2 declares a variable without one, and the record's actor bodies write the form).</summary>
public sealed record FieldDeclaration(int Line, Modifiers Modifiers, TypeSyntax? Type, string Name, Expression? Value) : Statement(Line);

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
/// fields, methods and constructors of the body, null where no body is written and an empty list
/// for <c>{ }</c>; values are the members of an enum or flags type. The modifiers written in
/// front of the declaration stay on it for the checker to judge.
/// </summary>
public sealed record TypeDeclaration(
    int Line,
    Modifiers Modifiers,
    TypeDeclarationKind Kind,
    string Name,
    IReadOnlyList<TypeParameter> TypeParameters,
    IReadOnlyList<Parameter>? PrimaryConstructor,
    IReadOnlyList<BaseSpecifier> Bases,
    IReadOnlyList<string> EqualBy,
    IReadOnlyList<Statement>? Members,
    IReadOnlyList<EnumMember> Values) : Statement(Line);

/// <summary>One file: its statements and declarations in source order (F5).</summary>
public sealed record ProgramSyntax(IReadOnlyList<Statement> Statements);
