namespace Kurz.Compiler.Syntax;

/// <summary>
/// J6, J7: a walk of a keyword's template, or of a block a use gives, that shows <paramref name="statement"/> each statement with the
/// loops around it inside the walk and whether a lambda stands between it and the walk's start (a loop around the lambda is none for
/// it), <paramref name="expression"/> each expression, and <paramref name="pattern"/> the pattern of each arm.
/// </summary>
internal sealed class KeywordWalk(Action<Statement, int, bool> statement, Action<Expression>? expression = null, Action<Pattern>? pattern = null)
{
    public void Statements(IEnumerable<Statement> statements)
    {
        foreach (var s in statements)
        {
            Statement(s, 0, false);
        }
    }

    private void Statement(Statement s, int loops, bool lambda)
    {
        statement(s, loops, lambda);
        switch (s)
        {
            case Block b:
                Block(b, loops, lambda);
                break;
            case VariableDeclaration v:
                Optional(v.Value, loops, lambda);
                break;
            case AssignOrDeclare a:
                Expression(a.Value, loops, lambda);
                break;
            case Assignment a:
                Expression(a.Target, loops, lambda);
                Expression(a.Value, loops, lambda);
                break;
            case ExpressionStatement e:
                Expression(e.Expression, loops, lambda);
                break;
            case SendStatement e:
                Expression(e.Call, loops, lambda);
                break;
            case WithinStatement w:
                Expression(w.Bound, loops, lambda);
                Block(w.Body, loops, lambda);
                break;
            case IfStatement i:
                Expression(i.Condition, loops, lambda);
                Block(i.Then, loops, lambda);
                if (i.Else is { } other)
                {
                    Statement(other, loops, lambda);
                }

                break;
            case WhileStatement w:
                Expression(w.Condition, loops, lambda);
                Block(w.Body, loops + 1, lambda);
                break;
            case ForInStatement f:
                Expression(f.Collection, loops, lambda);
                Block(f.Body, loops + 1, lambda);
                Arms(f.Arms ?? [], loops, lambda);
                break;
            case MatchStatement m:
                Expression(m.Subject, loops, lambda);
                Arms(m.Arms, loops, lambda);
                break;
            case ReturnStatement r:
                Optional(r.Value, loops, lambda);
                break;
            case ThrowStatement t:
                Optional(t.Value, loops, lambda);
                break;
            case RawBlock r:
                Block(r.Body, loops, lambda);
                break;
            case ConstAssertStatement c:
                Expression(c.Condition, loops, lambda);
                Expression(c.Text, loops, lambda);
                break;
            case AssertionStatement a:
                Expression(a.Condition, loops, lambda);
                break;
            case AdvanceStatement a:
                Expression(a.Duration, loops, lambda);
                break;
            case AsmStatement a:
                foreach (var operand in a.Operands)
                {
                    Optional(operand.Value, loops, lambda);
                    Optional(operand.Output, loops, lambda);
                    if (operand.Body is { } body)
                    {
                        Block(body, loops, lambda);
                    }
                }

                break;

            // a break, a continue, a decision, a mock and a declaration hold nothing the walk reads
        }
    }

    private void Block(Block b, int loops, bool lambda)
    {
        foreach (var s in b.Statements)
        {
            Statement(s, loops, lambda);
        }
    }

    private void Arms(IReadOnlyList<MatchArm> arms, int loops, bool lambda)
    {
        foreach (var arm in arms)
        {
            pattern?.Invoke(arm.Pattern);
            if (arm.Pattern is ValuePattern value)
            {
                Expression(value.Value, loops, lambda);
            }

            Statement(arm.Body, loops, lambda);
        }
    }

    private void Optional(Expression? e, int loops, bool lambda)
    {
        if (e is not null)
        {
            Expression(e, loops, lambda);
        }
    }

    private void Expression(Expression e, int loops, bool lambda)
    {
        expression?.Invoke(e);
        switch (e)
        {
            case StringLiteral s:
                foreach (var hole in s.Segments.OfType<HoleSegment>())
                {
                    Expression(hole.Expression, loops, lambda);
                }

                break;
            case MemberAccess m:
                Expression(m.Target, loops, lambda);
                break;
            case CallExpression c:
                Expression(c.Callee, loops, lambda);
                foreach (var argument in c.Arguments)
                {
                    Expression(argument.Value, loops, lambda);
                }

                Optional(c.Within, loops, lambda);
                break;
            case ListLiteral l:
                foreach (var item in l.Items)
                {
                    Expression(item, loops, lambda);
                }

                break;
            case IndexExpression i:
                Expression(i.Target, loops, lambda);
                Expression(i.Index, loops, lambda);
                break;
            case UnaryExpression u:
                Expression(u.Operand, loops, lambda);
                break;
            case BinaryExpression b:
                Expression(b.Left, loops, lambda);
                Expression(b.Right, loops, lambda);
                break;
            case RangeExpression r:
                Expression(r.Start, loops, lambda);
                Expression(r.End, loops, lambda);
                break;
            case LambdaExpression l:
                Optional(l.ExpressionBody, 0, true); // a jump in a lambda's body is the lambda's, and no loop around it reaches in
                if (l.BlockBody is { } body)
                {
                    Block(body, 0, true);
                }

                break;
            case MatchExpression m:
                Expression(m.Subject, loops, lambda);
                Arms(m.Arms, loops, lambda);
                break;
            case WithExpression w:
                Expression(w.Target, loops, lambda);
                foreach (var assignment in w.Assignments)
                {
                    Expression(assignment.Value, loops, lambda);
                }

                break;
            case ElseExpression el:
                Expression(el.Call, loops, lambda);
                Arms(el.Arms, loops, lambda);
                break;
            case ParenthesizedExpression p:
                Expression(p.Inner, loops, lambda);
                break;
            case SpawnExpression s:
                Expression(s.Call, loops, lambda);
                if (s.Policy is { } policy)
                {
                    Optional(policy.After, loops, lambda);
                    Optional(policy.Max, loops, lambda);
                    Optional(policy.Within, loops, lambda);
                    Optional(policy.Block, loops, lambda);
                }

                break;
            case AttemptExpression a:
                Block(a.Body, loops, lambda);
                break;

            // a literal, a name, `this` and a member of a type hold no expression the walk reads
        }
    }
}
