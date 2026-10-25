using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // how deep the expression being checked stands in the expression around it; a statement starts again at 0 (M7)
    private int expressionDepth;

    // the expressions the checker read as parts of the expression around them, the value of an arm among them (M7)
    private readonly HashSet<Expression> inline = new(ReferenceEqualityComparer.Instance);

    // what changes a variable within an expression, by its node: an argument after `mut` the checker took, the call of a `mut`
    // method on a variable and an assignment, with the variable and the path of fields each one changes (M7)
    private readonly Dictionary<object, Change> changes = new(ReferenceEqualityComparer.Instance);

    // the changes reported, so that each one is reported once, however many reads and statements meet it (E1)
    private readonly HashSet<object> reported = new(ReferenceEqualityComparer.Instance);

    /// <summary>A change of a variable or of a path of fields from it, at its line, and how the error names it.</summary>
    private readonly record struct Change(VariableSymbol Variable, string Path, int Line, string Text);

    /// <summary>
    /// A value of a variable that an expression holds: the variable, the path of fields it was read through, which stops at an item of
    /// a list, the type of the value, how the read is named, and the read itself, none for a lambda's.
    /// </summary>
    private readonly record struct Held(VariableSymbol Variable, string Path, KurzType Type, string Text, Expression? Read, bool Item = false);

    /// <summary>M7: two paths reach one value when they are the same or one is a prefix of the other; two fields of one variable are two values.</summary>
    private static bool Overlaps(string path, string other) =>
        other == path || other.StartsWith(path + ".", StringComparison.Ordinal) || path.StartsWith(other + ".", StringComparison.Ordinal);

    /// <summary>
    /// M7 (the owner, 2026-10-24 and 2026-10-25): within one expression, a read of what a call takes as <c>mut</c>, an argument
    /// after <c>mut</c> or the receiver of a <c>mut</c> method, of a value that holds it or of one it holds, whose value the
    /// expression still holds when that call runs, or when an assignment or a <c>mut</c> method in a statement of an arm changes
    /// it, is <c>changed-while-held</c>, once for each change (E1). A read that makes a new value first, a read after the call,
    /// a read of a type that is copied and a read of <c>Add</c>'s receiver in its own argument, against <c>Add</c>'s change of it,
    /// hold no such value, unless a lambda in the argument makes the read. The lambdas of the expression are expressions of their
    /// own, read when they are called, and change nothing they read (F11).
    /// </summary>
    private void CheckReads(Expression expression) => Reads(expression);

    /// <summary>A statement's expression read by the check of its own form, with its parts read as parts of it, and then its reads (M7).</summary>
    private Typed CheckWhole(Expression e, Func<Typed> check)
    {
        expressionDepth++;
        var typed = check();
        expressionDepth--;
        CheckReads(e);
        return typed;
    }

    private void Reads(Expression e)
    {
        foreach (var part in Parts(e))
        {
            Reads(part);
        }

        if (e is LambdaExpression { ExpressionBody: { } body })
        {
            Reads(body);
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

        // a call's own changes: an argument after `mut` against all it holds; `Add` changes its receiver after its argument ran,
        // so against nothing of its own (the owner, 2026-10-25, "Allow, as Rust's clone") but a lambda there, which holds what it
        // reads past the call (the owner, 2026-10-25, "Refuse, as Rust's borrow")
        if (e is CallExpression call)
        {
            foreach (var a in call.Arguments.Where(a => a.Mut))
            {
                if (changes.TryGetValue(a, out var change))
                {
                    foreach (var (held, _) in ordered)
                    {
                        Conflict(held, a, change);
                    }
                }
            }

            if (changes.TryGetValue(call, out var receiver))
            {
                foreach (var (held, _) in ordered.Skip(1))
                {
                    Conflict(held.Where(h => h.Read is null).ToList(), call, receiver);
                }
            }
        }

        for (var j = 1; j < ordered.Count; j++)
        {
            foreach (var (node, change) in Muts(ordered[j].Part))
            {
                for (var i = 0; i < j; i++)
                {
                    Conflict(ordered[i].Held, node, change);
                }
            }
        }
    }

    private void Conflict(IReadOnlyList<Held> held, object node, Change change)
    {
        foreach (var h in held)
        {
            if (!ReferenceEquals(h.Variable, change.Variable) || !Overlaps(h.Path, change.Path))
            {
                continue;
            }

            if (Copied(h.Type))
            {
                if (h.Read is { } read)
                {
                    model.RecordHeldCopy(read); // the code generator copies it (the owner, 2026-10-25, "Copied")
                }
            }
            else if (reported.Add(node))
            {
                Report(ErrorIds.ChangedWhileHeld, change.Line, $"{h.Text} is still in use while {change.Text}, and a `{h.Type}` is not copied: copy it into a variable of its own on a line before (M7)");
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

    /// <summary>
    /// The changes made while the expression runs: the arguments after <c>mut</c> and the <c>mut</c> methods of its calls, and the
    /// assignments and the calls in the statements of its arms.
    /// </summary>
    private IEnumerable<(object Node, Change Change)> Muts(Expression e)
    {
        var own = e is CallExpression c
            ? c.Arguments.Where(a => a.Mut).Prepend<object>(c).SelectMany(ChangeOf)
            : [];
        return e switch
        {
            MatchExpression m => Muts(m.Subject).Concat(m.Arms.SelectMany(a => Muts(a.Body))),
            ElseExpression x => Muts(x.Call).Concat(x.Arms.SelectMany(a => Muts(a.Body))),
            _ => own.Concat(Parts(e).SelectMany(Muts)),
        };
    }

    private IEnumerable<(object Node, Change Change)> Muts(Statement s) => s switch
    {
        ExpressionStatement es => Muts(es.Expression),
        Block b => b.Statements.SelectMany(Muts),
        VariableDeclaration { Value: { } v } => Muts(v),
        AssignOrDeclare a => ChangeOf(a).Concat(Muts(a.Value)),
        Assignment a => ChangeOf(a).Concat(Muts(a.Value)),
        IfStatement i => Muts(i.Condition).Concat(Muts(i.Then)).Concat(i.Else is null ? [] : Muts(i.Else)),
        WhileStatement w => Muts(w.Condition).Concat(Muts(w.Body)),
        ForInStatement f => Muts(f.Collection).Concat(Muts(f.Body)),
        MatchStatement m => Muts(m.Subject).Concat(m.Arms.SelectMany(a => Muts(a.Body))),
        ReturnStatement { Value: { } v } => Muts(v),
        ThrowStatement { Value: { } v } => Muts(v),
        RawBlock r => Muts(r.Body),
        _ => [],
    };

    private IEnumerable<(object Node, Change Change)> ChangeOf(object node) =>
        changes.TryGetValue(node, out var change) ? [(node, change)] : [];

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
                return [new Held(model.VariableOf(n), n.Name, model.TypeOf(n).Type, $"`{n.Name}`", n)];
            case MemberAccess m when FieldOf(m) is not null && model.HasType(m):
                return HeldBy(m.Target).Select(h => h.Item
                    ? h with { Type = model.TypeOf(m).Type, Read = m }
                    : h with { Path = h.Path + "." + m.Member, Type = model.TypeOf(m).Type, Text = $"`{h.Path}.{m.Member}`", Read = m }).ToList();
            case IndexExpression i when model.HasType(i.Target) && model.TypeOf(i.Target).Type is ListType && model.HasType(i):
                return HeldBy(i.Target).Select(h => h with { Type = model.TypeOf(i).Type, Text = $"an item of `{h.Path}`", Read = i, Item = true }).ToList();
            case BinaryExpression { Operator: TokenKind.QuestionQuestion } b:
                return [.. HeldBy(b.Left), .. HeldBy(b.Right)];
            case MatchExpression m:
                return ArmValues(m.Arms).SelectMany(HeldBy).ToList();
            case ElseExpression x:
                return ArmValues(x.Arms).SelectMany(HeldBy).ToList();
            case LambdaExpression l when model.HasLambda(l):
                return model.LambdaOf(l).Captures.Select(v => new Held(v, v.Name, v.Type, $"a lambda that reads `{v.Name}`", null)).ToList();
            default:
                return [];
        }
    }

    /// <summary>
    /// M7, the owner's "Rust's Copy line" (2026-10-24), "Rust's Copy set", "Copied" and "Copied too" (2026-10-25): whether the
    /// expression holds a copy of a value of the type: a number, a <c>bool</c>, a <c>char</c>, a pointer, a time, the type of a C
    /// function, a function value, and a <c>data</c> value or a union made only of such types that holds no value of its own type,
    /// as Rust's <c>Copy</c> types; a <c>data</c> type only where every type derived from it is too (the record's assumption,
    /// section 4). A type the checker could not tell is copied, so that its error stays the one reported (E1).
    /// </summary>
    private bool Copied(KurzType type) => Copied(type, []);

    private bool Copied(KurzType type, HashSet<DataSymbol> path) => type switch
    {
        PrimitiveType p => p.Kind != Primitive.String,
        PointerType or CFunctionType or FunctionType or VoidType or ErrorType => true,
        UnionType u => u.Cases.All(c => Copied(c, path)),
        DataType d => CopiedData(d, path),
        _ => false, // a list, a string, a type parameter
    };

    // the data types on the way to this one: a type met again holds a value of its own type, as a Rust type does through a `Box`,
    // which is not `Copy`
    private bool CopiedData(DataType d, HashSet<DataSymbol> path)
    {
        if (!path.Add(d.Symbol))
        {
            return false;
        }

        var copied = d.Symbol.Fields.All(f => Copied(SubstituteData(d, f.Type), path))
            && dataTypes.Values.Where(x => x.Base == d.Symbol).All(x => Copied(new DataType(x, x.TypeParameters.Select(t => (KurzType)new TypeParameterType(t)).ToList()), path));
        path.Remove(d.Symbol);
        return copied;
    }
}
