using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // how deep the expression being checked stands in the expression around it; a statement starts again at 0 (M7)
    private int expressionDepth;

    // the expressions the checker read as parts of the expression around them, the value of an arm among them (M7)
    private readonly HashSet<Expression> inline = new(ReferenceEqualityComparer.Instance);

    // the arguments after `mut` the checker took, with the variable and the path of fields each one passes (M7)
    private readonly Dictionary<Argument, (VariableSymbol Variable, string Path)> mutArguments = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// A value of a variable that an expression holds: the variable, the path of fields it was read through, which stops at an item of
    /// a list, the type of the value, and how the read is named.
    /// </summary>
    private readonly record struct Held(VariableSymbol Variable, string Path, KurzType Type, string Text, bool Item = false);

    /// <summary>M7: two paths reach one value when they are the same or one is a prefix of the other; two fields of one variable are two values.</summary>
    private static bool Overlaps(string path, string other) =>
        other == path || other.StartsWith(path + ".", StringComparison.Ordinal) || path.StartsWith(other + ".", StringComparison.Ordinal);

    /// <summary>
    /// M7 (the owner, 2026-10-24): within one expression, a read of what a call takes as <c>mut</c>, of a value that holds it or of
    /// one it holds, whose value the expression still holds when that call runs, is <c>mut-at-call</c>, once for each argument
    /// after <c>mut</c> (E1). A read that makes a new value first, a read after the call, and a read of a type that is copied hold
    /// no such value. The lambdas of the expression are expressions of their own, read when they are called.
    /// </summary>
    private void CheckReads(Expression expression) => Reads(expression, new HashSet<Argument>(ReferenceEqualityComparer.Instance));

    /// <summary>A statement's expression read by the check of its own form, with its parts read as parts of it, and then its reads (M7).</summary>
    private Typed CheckWhole(Expression e, Func<Typed> check)
    {
        expressionDepth++;
        var typed = check();
        expressionDepth--;
        CheckReads(e);
        return typed;
    }

    private void Reads(Expression e, HashSet<Argument> reported)
    {
        foreach (var part in Parts(e))
        {
            Reads(part, reported);
        }

        if (e is LambdaExpression { ExpressionBody: { } body })
        {
            Reads(body, reported);
        }

        // the parts the expression holds while the next ones run, in the order they run: a call's receiver or the value it calls
        // first, then its arguments as written (D11); a string's holes are made text where they stand, and a `match` or an `else`
        // gives its arms nothing of its subject or its call but the names they bind, which are variables of their own (M3)
        List<(IReadOnlyList<Held> Held, Expression Part)> ordered = e switch
        {
            CallExpression c => [(Callee(c), c.Callee), .. c.Arguments.Where(a => !a.Mut).Select(a => (HeldBy(a.Value), a.Value))],
            IndexExpression or RangeExpression or WithExpression or BinaryExpression { Operator: not TokenKind.QuestionQuestion } =>
                Parts(e).Select(p => (HeldBy(p), p)).ToList(),
            _ => [],
        };

        if (e is CallExpression call)
        {
            foreach (var a in call.Arguments.Where(a => a.Mut))
            {
                if (mutArguments.TryGetValue(a, out var passed))
                {
                    foreach (var (held, _) in ordered)
                    {
                        Conflict(held, a, passed, reported);
                    }
                }
            }
        }

        for (var j = 1; j < ordered.Count; j++)
        {
            foreach (var (a, passed) in Muts(ordered[j].Part))
            {
                for (var i = 0; i < j; i++)
                {
                    Conflict(ordered[i].Held, a, passed, reported);
                }
            }
        }
    }

    private void Conflict(IReadOnlyList<Held> held, Argument a, (VariableSymbol Variable, string Path) passed, HashSet<Argument> reported)
    {
        foreach (var h in held)
        {
            if (!reported.Contains(a) && ReferenceEquals(h.Variable, passed.Variable) && Overlaps(h.Path, passed.Path) && !Copied(h.Type))
            {
                reported.Add(a);
                Report(ErrorIds.MutAtCall, a.Line, $"{h.Text} is still in use while the call that takes `mut {passed.Path}` runs, and a `{h.Type}` is not copied: copy it into a variable of its own on a line before (M7)");
            }
        }
    }

    /// <summary>
    /// The expressions directly inside one that the checker read as its parts, as they run with it; a lambda's body runs when the
    /// lambda is called, what stands after <c>mut</c> is passed, not read, and a statement of an arm is read on its own.
    /// </summary>
    private IEnumerable<Expression> Parts(Expression e) => e switch
    {
        ParenthesizedExpression p => [p.Inner],
        MemberAccess m => [m.Target],
        CallExpression c => [c.Callee, .. c.Arguments.Where(a => !a.Mut).Select(a => a.Value)],
        IndexExpression i => [i.Target, i.Index],
        UnaryExpression u => [u.Operand],
        BinaryExpression b => [b.Left, b.Right],
        RangeExpression r => [r.Start, r.End],
        WithExpression w => [w.Target, .. w.Assignments.Select(a => a.Value)],
        StringLiteral s => s.Segments.OfType<HoleSegment>().Select(h => h.Expression),
        MatchExpression m => [m.Subject, .. ArmValues(m.Arms)],
        ElseExpression x => [x.Call, .. ArmValues(x.Arms)],
        _ => [],
    };

    /// <summary>The values of arms that the checker read as parts of the <c>match</c> or the <c>else</c>: an expression, or the last statement of a block (O5).</summary>
    private IEnumerable<Expression> ArmValues(IReadOnlyList<MatchArm> arms) => arms.SelectMany(a => ValueOf(a.Body));

    private IEnumerable<Expression> ValueOf(Statement body) => body switch
    {
        ExpressionStatement es when inline.Contains(es.Expression) => [es.Expression],
        Block { Statements: [.., var last] } => ValueOf(last),
        _ => [],
    };

    /// <summary>The arguments after <c>mut</c> that the checker took in the calls that run while the expression runs, the statements of its arms included.</summary>
    private IEnumerable<(Argument Argument, (VariableSymbol Variable, string Path) Passed)> Muts(Expression e)
    {
        var own = e is CallExpression c
            ? c.Arguments.Where(a => a.Mut && mutArguments.ContainsKey(a)).Select(a => (a, mutArguments[a]))
            : [];
        return e switch
        {
            MatchExpression m => Muts(m.Subject).Concat(m.Arms.SelectMany(a => Muts(a.Body))),
            ElseExpression x => Muts(x.Call).Concat(x.Arms.SelectMany(a => Muts(a.Body))),
            _ => own.Concat(Parts(e).SelectMany(Muts)),
        };
    }

    private IEnumerable<(Argument Argument, (VariableSymbol Variable, string Path) Passed)> Muts(Statement s) => s switch
    {
        ExpressionStatement es => Muts(es.Expression),
        Block b => b.Statements.SelectMany(Muts),
        VariableDeclaration { Value: { } v } => Muts(v),
        AssignOrDeclare a => Muts(a.Value),
        Assignment a => Muts(a.Value),
        IfStatement i => Muts(i.Condition).Concat(Muts(i.Then)).Concat(i.Else is null ? [] : Muts(i.Else)),
        WhileStatement w => Muts(w.Condition).Concat(Muts(w.Body)),
        ForInStatement f => Muts(f.Collection).Concat(Muts(f.Body)),
        MatchStatement m => Muts(m.Subject).Concat(m.Arms.SelectMany(a => Muts(a.Body))),
        ReturnStatement { Value: { } v } => Muts(v),
        ThrowStatement { Value: { } v } => Muts(v),
        RawBlock r => Muts(r.Body),
        _ => [],
    };

    /// <summary>What a call holds while its arguments run: the receiver of a method, or the value of a variable or a field it calls (F11).</summary>
    private IReadOnlyList<Held> Callee(CallExpression c) =>
        c.Callee is MemberAccess { Conditional: false } m && FieldOf(m) is null ? HeldBy(m.Target) : HeldBy(c.Callee);

    /// <summary>The field of a data value a member names, or null for any other member.</summary>
    private FieldSymbol? FieldOf(MemberAccess m) =>
        !m.Conditional && model.HasType(m.Target) && model.TypeOf(m.Target).Type is DataType d ? d.Symbol.Field(m.Member) : null;

    /// <summary>
    /// The values of variables the expression's value is: a variable, a path of fields from one, an item of a list it holds, either
    /// side of <c>??</c>, the value of an arm, and the variables a lambda reads (F15); any other expression makes a new value.
    /// </summary>
    private IReadOnlyList<Held> HeldBy(Expression e)
    {
        switch (e)
        {
            case ParenthesizedExpression p:
                return HeldBy(p.Inner);
            case NameExpression n when model.IsVariable(n) && model.HasType(n):
                return [new Held(model.VariableOf(n), n.Name, model.TypeOf(n).Type, $"`{n.Name}`")];
            case MemberAccess m when FieldOf(m) is not null && model.HasType(m):
                return HeldBy(m.Target).Select(h => h.Item
                    ? h with { Type = model.TypeOf(m).Type }
                    : h with { Path = h.Path + "." + m.Member, Type = model.TypeOf(m).Type, Text = $"`{h.Path}.{m.Member}`" }).ToList();
            case IndexExpression i when model.HasType(i.Target) && model.TypeOf(i.Target).Type is ListType && model.HasType(i):
                return HeldBy(i.Target).Select(h => h with { Type = model.TypeOf(i).Type, Text = $"an item of `{h.Path}`", Item = true }).ToList();
            case BinaryExpression { Operator: TokenKind.QuestionQuestion } b:
                return [.. HeldBy(b.Left), .. HeldBy(b.Right)];
            case MatchExpression m:
                return ArmValues(m.Arms).SelectMany(HeldBy).ToList();
            case ElseExpression x:
                return ArmValues(x.Arms).SelectMany(HeldBy).ToList();
            case LambdaExpression l when model.HasLambda(l):
                return model.LambdaOf(l).Captures.Select(v => new Held(v, v.Name, v.Type, $"a lambda that reads `{v.Name}`")).ToList();
            default:
                return [];
        }
    }

    /// <summary>
    /// M7, the owner's "Rust's Copy line" (2026-10-24): whether a value of the type holds no list, so that the expression holds a
    /// copy of it: a number, a <c>bool</c>, a <c>char</c>, a pointer, and a <c>data</c> value or a union of such types; a time and
    /// the type of a C function too, and a <c>data</c> type only where every type derived from it is, while a <c>string</c>, a
    /// function value and a type parameter are not (the record's assumption, section 4). A type the checker could not tell is
    /// copied, so that its error stays the one reported (E1).
    /// </summary>
    private bool Copied(KurzType type) => Copied(type, []);

    private bool Copied(KurzType type, HashSet<DataSymbol> seen) => type switch
    {
        PrimitiveType p => p.Kind != Primitive.String,
        PointerType or CFunctionType or VoidType or ErrorType => true,
        UnionType u => u.Cases.All(c => Copied(c, seen)),
        DataType d => !seen.Add(d.Symbol) || (d.Symbol.Fields.All(f => Copied(SubstituteData(d, f.Type), seen))
            && dataTypes.Values.Where(x => x.Base == d.Symbol).All(x => Copied(new DataType(x, x.TypeParameters.Select(t => (KurzType)new TypeParameterType(t)).ToList()), seen))),
        _ => false, // a list, a string, a function value, a type parameter
    };
}
