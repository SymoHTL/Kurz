namespace Kurz.Compiler.Syntax;

/// <summary>
/// T33: a copy of a function's syntax in which every node is new, so that the checker can type each copy of a generic function that
/// reads its type apart from the others (its model keys a node by its reference), and a walk of a function's syntax that shows each
/// expression to a visitor. The tree is the parser's and is never changed. A declaration nested in a body, a type's, a function's,
/// a block of C functions' and a <c>use</c>, is kept as it is: the checker says <c>not supported</c> to it there (F1) and keeps no
/// type of it.
/// </summary>
public static class SyntaxCopy
{
    /// <summary>A new declaration with the same text, every node of its parameters, its result and its body new.</summary>
    public static FunctionDeclaration Copy(FunctionDeclaration d) => new Copier(null).Function(d);

    /// <summary>
    /// A new block with the same text, every node of it new, but where <paramref name="rewrite"/> gives an expression its own: it is
    /// shown each expression before its copy, with the copier for the expressions inside it, and returns the expression to stand in
    /// its place or null for the copy. T33: a pass of a loop over <c>T.Fields</c>, in which <c>f.Get(value)</c> reads the field.
    /// </summary>
    public static Block Copy(Block b, Func<Expression, Func<Expression, Expression>, Expression?> rewrite) => (Block)new Copier(null, rewrite).Statement(b);

    /// <summary>As the block's copy above, of an expression: T33, an argument of the call <c>T.Make(f => ...)</c> becomes, its lambda's body for one field.</summary>
    public static Expression Copy(Expression e, Func<Expression, Func<Expression, Expression>, Expression?> rewrite) => new Copier(null, rewrite).Expression(e);

    /// <summary>Shows <paramref name="visit"/> every expression of the declaration, its parameters' default values among them.</summary>
    public static void Walk(FunctionDeclaration d, Action<Expression> visit) => new Copier(visit).Function(d);

    /// <summary>Shows <paramref name="visit"/> every expression of a statement.</summary>
    public static void Walk(Statement s, Action<Expression> visit) => new Copier(visit).Statement(s);

    private sealed class Copier(Action<Expression>? visit, Func<Expression, Func<Expression, Expression>, Expression?>? rewrite = null)
    {
        public FunctionDeclaration Function(FunctionDeclaration d) => d with
        {
            ReturnType = Type(d.ReturnType),
            TypeParameters = d.TypeParameters.Select(p => p with { Limit = OptionalType(p.Limit) }).ToList(),
            Parameters = d.Parameters.Select(Parameter).ToList(),
            ExpressionBody = Optional(d.ExpressionBody),
            BlockBody = d.BlockBody is null ? null : Block(d.BlockBody),
        };

        private Parameter Parameter(Parameter p) => p with { Type = OptionalType(p.Type), Default = Optional(p.Default) };

        private TypeSyntax? OptionalType(TypeSyntax? t) => t is null ? null : Type(t);

        private TypeSyntax Type(TypeSyntax t) => t switch
        {
            NamedType n => n with { TypeArguments = n.TypeArguments.Select(Type).ToList() },
            NullableType n => n with { Inner = Type(n.Inner) },
            UnionType u => u with { Cases = u.Cases.Select(Type).ToList() },
            FunctionType f => f with { Parameters = f.Parameters.Select(Type).ToList(), Result = Type(f.Result) },
            WeakType w => w with { Inner = Type(w.Inner) },
            VoidType v => v with { },
            PointerType p => p with { Pointee = Type(p.Pointee) },
            ExternFunctionType f => f with { Parameters = f.Parameters.Select(Type).ToList(), Result = Type(f.Result) },
            _ => throw new InvalidOperationException($"the type syntax `{t.GetType().Name}` has no copy"),
        };

        private Expression? Optional(Expression? e) => e is null ? null : Expression(e);

        public Expression Expression(Expression e)
        {
            visit?.Invoke(e);
            if (rewrite?.Invoke(e, Expression) is { } rewritten)
            {
                return rewritten;
            }

            return e switch
            {
                IntegerLiteral or RealLiteral or UnitLiteral or CharLiteral or BoolLiteral or NullLiteral or NameExpression or ThisExpression => e with { },
                StringLiteral s => s with { Segments = s.Segments.Select(Segment).ToList() },
                MemberAccess m => m with { Target = Expression(m.Target) },
                TypeMemberAccess t => t with { Type = Type(t.Type) },
                CallExpression c => c with
                {
                    Callee = Expression(c.Callee),
                    TypeArguments = c.TypeArguments.Select(Type).ToList(),
                    Arguments = c.Arguments.Select(a => a with { Value = Expression(a.Value) }).ToList(),
                },
                ListLiteral l => l with { Items = l.Items.Select(Expression).ToList() },
                IndexExpression i => i with { Target = Expression(i.Target), Index = Expression(i.Index) },
                UnaryExpression u => u with { Operand = Expression(u.Operand) },
                BinaryExpression b => b with { Left = Expression(b.Left), Right = Expression(b.Right) },
                RangeExpression r => r with { Start = Expression(r.Start), End = Expression(r.End) },
                LambdaExpression l => l with
                {
                    Parameters = l.Parameters.Select(Parameter).ToList(),
                    ExpressionBody = Optional(l.ExpressionBody),
                    BlockBody = l.BlockBody is null ? null : Block(l.BlockBody),
                },
                MatchExpression m => m with { Subject = Expression(m.Subject), Arms = m.Arms.Select(Arm).ToList() },
                WithExpression w => w with { Target = Expression(w.Target), Assignments = w.Assignments.Select(a => a with { Value = Expression(a.Value) }).ToList() },
                ElseExpression el => el with { Call = Expression(el.Call), Arms = el.Arms.Select(Arm).ToList() },
                ParenthesizedExpression p => p with { Inner = Expression(p.Inner) },
                _ => throw new InvalidOperationException($"the expression `{e.GetType().Name}` has no copy"),
            };
        }

        private StringSegment Segment(StringSegment s) => s switch
        {
            HoleSegment h => h with { Expression = Expression(h.Expression) },
            _ => s, // text, which no model keys
        };

        private MatchArm Arm(MatchArm a) => a with { Pattern = Pattern(a.Pattern), Body = Statement(a.Body) };

        private Pattern Pattern(Pattern p) => p switch
        {
            TypePattern t => t with { Type = Type(t.Type) },
            ValuePattern v => v with { Value = Expression(v.Value) },
            ElsePattern e => e with { },
            _ => throw new InvalidOperationException($"the pattern `{p.GetType().Name}` has no copy"),
        };

        private Block Block(Block b) => b with { Statements = b.Statements.Select(Statement).ToList() };

        public Statement Statement(Statement s) => s switch
        {
            Block b => Block(b),
            VariableDeclaration v => v with { Type = OptionalType(v.Type), Value = Optional(v.Value) },
            AssignOrDeclare a => a with { Value = Expression(a.Value) },
            Assignment a => a with { Target = Expression(a.Target), Value = Expression(a.Value) },
            ExpressionStatement e => e with { Expression = Expression(e.Expression) },
            IfStatement i => i with { Condition = Expression(i.Condition), Then = Block(i.Then), Else = i.Else is null ? null : Statement(i.Else) },
            WhileStatement w => w with { Condition = Expression(w.Condition), Body = Block(w.Body) },
            ForInStatement f => f with { Collection = Expression(f.Collection), Body = Block(f.Body), Second = f.Second is null ? null : f.Second with { } },
            MatchStatement m => m with { Subject = Expression(m.Subject), Arms = m.Arms.Select(Arm).ToList() },
            BreakStatement or ContinueStatement => s with { },
            ReturnStatement r => r with { Value = Optional(r.Value) },
            ThrowStatement t => t with { Value = Optional(t.Value) },
            RawBlock r => r with { Body = Block(r.Body) },
            AsmStatement a => a with
            {
                Operands = a.Operands.Select(o => o with { Value = Optional(o.Value), Output = Optional(o.Output), Body = o.Body is null ? null : Block(o.Body) }).ToList(),
            },
            FunctionDeclaration or TypeDeclaration or ExternBlock or UseStatement or FieldDeclaration or ConstructorDeclaration => s, // not supported in a body (F1)
            _ => throw new InvalidOperationException($"the statement `{s.GetType().Name}` has no copy"),
        };
    }
}
