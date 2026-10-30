using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>A variable, or a path of fields from one, <c>u.Email</c>, as the flow of the code knows it not null (N3): the fields joined by dots, empty for the variable itself.</summary>
internal readonly record struct NullPath(VariableSymbol Root, string Fields)
{
    /// <summary>Whether this path is <paramref name="other"/> or runs through it, so that a change of <paramref name="other"/> changes it.</summary>
    public bool Within(NullPath other) =>
        ReferenceEquals(Root, other.Root) && (other.Fields.Length == 0 || Fields == other.Fields || Fields.StartsWith(other.Fields + ".", StringComparison.Ordinal));
}

/// <summary>N3: what held at each <c>break</c> and <c>continue</c> of one loop.</summary>
internal sealed class LoopExits
{
    public List<ImmutableHashSet<NullPath>> Breaks { get; } = [];

    public List<ImmutableHashSet<NullPath>> Continues { get; } = [];
}

/// <summary>
/// Chapter 4: <c>T?</c>, <c>null</c>, <c>??</c>, <c>?.</c> and the flow of the code that makes a nullable variable or path of
/// <c>data</c> fields a value of the type it makes nullable (N1 to N8). What the code shows is kept as the set of paths known not null
/// where the checker reads (<see cref="Frame.Known"/>): a test adds its paths to the branch it guards, an assignment of a value whose
/// type has no <c>null</c> adds its target, any other assignment and a <c>mut</c> argument take away what runs through their target,
/// and where branches or the rounds of a loop meet only what every path that reaches the point keeps is kept, the join C#'s nullable
/// analysis makes (section 4, an assumption there). A path through a class instance or a <c>weak</c> reference (N9) is not read: the
/// checker reads neither.
/// </summary>
public sealed partial class Checker
{
    // N5: the type of each member of a chain of `?.` before the chain makes it nullable, which the next member of the chain reads
    private readonly Dictionary<MemberAccess, KurzType> chainMembers = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// N1: <c>T?</c>, the union <c>T | null</c>, laid out as every union is (section 5). What a <c>?</c> makes nullable that is a union,
    /// a nullable type among them (<c>T??</c>), a function type, a pointer, which <c>null</c> is a value of already (R10), a C function
    /// type, a type parameter, which a nullable type could stand for, and <c>void</c> are <c>not supported</c>: section 4 says nothing of
    /// them.
    /// </summary>
    private KurzType ResolveNullable(NullableType n)
    {
        var inner = Resolve(n.Inner);
        switch (inner)
        {
            case ErrorType:
                return Types.Error;
            case UnionType or FunctionType or PointerType or CFunctionType or TypeParameterType or VoidType:
                NotSupported(n.Line, $"a nullable `{inner}` (N1)");
                return Types.Error;
            default:
                return Types.Nullable(inner);
        }
    }

    /// <summary>
    /// The path an expression reads, where it is a variable that is no state outside a function (R13, which another function changes),
    /// or a path of members from one; null for any other expression. A member that is no field makes a path of which nothing is ever
    /// known, since only a nullable field's read looks a path up.
    /// </summary>
    private NullPath? PathOf(Expression e)
    {
        switch (Unwrap(e))
        {
            case NameExpression n when Lookup(n.Name).Variable is { Kind: not VariableKind.Static } v:
                return new NullPath(v, string.Empty);
            case MemberAccess m when PathOf(m.Target) is { } target:
                return target with { Fields = target.Fields.Length == 0 ? m.Member : target.Fields + "." + m.Member };
            default:
                return null;
        }
    }

    /// <summary>N3: the paths that are not null where the value of <paramref name="e"/> is not: its own, and each value a member of it was read from, so that a test of <c>a?.b != null</c> shows <c>a</c> too.</summary>
    private ImmutableHashSet<NullPath> NonNullPaths(Expression e)
    {
        var paths = ImmutableHashSet<NullPath>.Empty;
        for (var x = Unwrap(e); x is not null; x = x is MemberAccess m ? Unwrap(m.Target) : null)
        {
            if (PathOf(x) is { } path)
            {
                paths = paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// N3: what a condition shows where it is true and where it is false: <c>x != null</c> and <c>x == null</c> the paths of
    /// <see cref="NonNullPaths"/>, <c>!</c> the two turned around, and <c>&amp;&amp;</c> and <c>||</c> what holds on every way
    /// they can come out so (the reading C# takes).
    /// </summary>
    private (ImmutableHashSet<NullPath> True, ImmutableHashSet<NullPath> False) Tests(Expression condition)
    {
        var none = ImmutableHashSet<NullPath>.Empty;
        switch (Unwrap(condition))
        {
            case UnaryExpression { Operator: TokenKind.Bang } u:
                {
                    var (t, f) = Tests(u.Operand);
                    return (f, t);
                }

            case BinaryExpression { Operator: TokenKind.AmpersandAmpersand } b:
                {
                    var (lt, lf) = Tests(b.Left);
                    var (rt, rf) = Tests(b.Right);
                    return (lt.Union(rt), lf.Intersect(lt.Union(rf)));
                }

            case BinaryExpression { Operator: TokenKind.BarBar } b:
                {
                    var (lt, lf) = Tests(b.Left);
                    var (rt, rf) = Tests(b.Right);
                    return (lt.Intersect(lf.Union(rt)), lf.Union(rf));
                }

            case BinaryExpression { Operator: TokenKind.EqualEqual or TokenKind.NotEqual } b when Unwrap(b.Left) is NullLiteral != Unwrap(b.Right) is NullLiteral:
                {
                    var paths = NonNullPaths(Unwrap(b.Left) is NullLiteral ? b.Right : b.Left);
                    return b.Operator == TokenKind.NotEqual ? (paths, none) : (none, paths);
                }

            default:
                return (none, none);
        }
    }

    /// <summary>The join where paths meet: what each of them keeps; <paramref name="otherwise"/> where no path reaches the point.</summary>
    private static ImmutableHashSet<NullPath> Join(IEnumerable<ImmutableHashSet<NullPath>> reaching, ImmutableHashSet<NullPath> otherwise)
    {
        ImmutableHashSet<NullPath>? joined = null;
        foreach (var state in reaching)
        {
            joined = joined is null ? state : joined.Intersect(state);
        }

        return joined ?? otherwise;
    }

    /// <summary>N7: a change of <paramref name="target"/>, an assignment or a <c>mut</c> argument, ends what was known about it and every path through it.</summary>
    private void EndNarrowing(NullPath target) => frame.Known = frame.Known.Where(p => !p.Within(target)).ToImmutableHashSet();

    /// <summary>N7: after an assignment to <paramref name="target"/> of a nullable type, a value whose type has no <c>null</c> makes it known not null.</summary>
    private void Assigned(NullPath? target, KurzType declared, Typed value)
    {
        if (target is not { } path)
        {
            return;
        }

        EndNarrowing(path);
        if (Types.NonNull(declared) is not null && !value.IsError && Types.NonNull(value.Type) is null && value.Type is not NullType)
        {
            frame.Known = frame.Known.Add(path);
        }
    }

    /// <summary>N7, M7: what stands after <c>mut</c> at a call is changed by it, so that nothing known about it holds from the argument on, and it is read as its own type.</summary>
    private void MutArgumentRead(Argument a)
    {
        if (a.Mut && PathOf(a.Value) is { } path)
        {
            EndNarrowing(path);
        }
    }

    /// <summary>
    /// The operands of <c>&amp;&amp;</c> and <c>||</c> (T18), each a <c>bool</c>; N3: the right side runs where the left is true, of
    /// <c>||</c> where it is false, with what that shows, and after both holds what held before the right side and was not changed in it.
    /// </summary>
    private (Typed Left, Typed Right) Logical(BinaryExpression b)
    {
        var left = CheckExpression(b.Left, Types.Bool);
        var before = frame.Known;
        var (whenTrue, whenFalse) = Tests(b.Left);
        frame.Known = before.Union(b.Operator == TokenKind.AmpersandAmpersand ? whenTrue : whenFalse);
        var right = CheckExpression(b.Right, Types.Bool);
        frame.Known = before.Intersect(frame.Known);
        return (left, right);
    }

    /// <summary>N2: a nullable value where an operator, an index, a loop or a condition needs a value of the type it makes nullable is <c>nullable-unchecked</c>, reported here.</summary>
    private bool Unchecked(Typed value, int line, string where)
    {
        if (value.IsError || Types.NonNull(value.Type) is null)
        {
            return false;
        }

        Report(ErrorIds.NullableUnchecked, line, $"a `{value.Type}` {where} without a check (N2)");
        return true;
    }

    /// <summary>
    /// N3: a value a test, <c>??</c> or <c>?.</c> reads the case of: a variable or a path of fields known not null is read as the nullable
    /// type it has, so that a second test, or a <c>??</c> or <c>?.</c> after a test, reads as the first did.
    /// </summary>
    private Typed CheckNullable(Expression e)
    {
        var known = frame.Known;
        if (PathOf(e) is { } path && Unwrap(e) is NameExpression or MemberAccess { Conditional: false })
        {
            frame.Known = frame.Known.Remove(path); // a variable or a path of fields, which runs nothing that could change what is known
        }

        var typed = CheckExpression(e, null);
        frame.Known = known;
        return typed;
    }

    /// <summary>N3: <c>x == null</c> and <c>x != null</c> on a nullable value test its case (<see cref="CheckNullable"/>). On any other type <c>null</c> is <c>type-mismatch</c> (N6).</summary>
    private Typed CompareNullable(BinaryExpression b, Expression other, Expression literal)
    {
        var typed = CheckNullable(other);
        if (typed.IsError)
        {
            return new Typed(Types.Bool);
        }

        if (typed.Type is PointerType)
        {
            RecordThrough(literal, new Typed(typed.Type, new NullConstant())); // R10: the `null` of the other side's pointer type
            return new Typed(Types.Bool);
        }

        if (Types.NonNull(typed.Type) is not null && b.Operator is TokenKind.EqualEqual or TokenKind.NotEqual)
        {
            RecordThrough(literal, new Typed(Types.Null, new NullConstant()));
            return new Typed(Types.Bool);
        }

        Report(ErrorIds.TypeMismatch, b.Line, Types.NonNull(typed.Type) is not null
            ? $"`{Operator(b.Operator)}` orders no `null`: a nullable value is tested with `==` and `!=` (N3, T18)"
            : $"`null` compares with a nullable value or a pointer, not with a `{typed.Type}` (N6, N1)");
        return new Typed(Types.Bool);
    }

    /// <summary>N3: a read of a variable or a field of a nullable type, as the type it makes nullable where its path is known not null.</summary>
    private Typed Narrowed(Expression read, Typed typed) =>
        Types.NonNull(typed.Type) is { } inner && PathOf(read) is { } path && frame.Known.Contains(path) ? new Typed(inner) : typed;

    /// <summary>
    /// A loop (C6, C8): its body, and its condition before each round, are read with what holds at its head: what held before it, less
    /// what a round leaves nullable at the end of its body or at a <c>continue</c>, the join C# makes where the paths around a loop meet
    /// (section 4, an assumption there). Where a reading ends with less known than it began with, it is read again from that, its
    /// diagnostics dropped, until a reading keeps what it began with; that reading's diagnostics are the loop's. After the loop holds
    /// what the head and a false condition show, joined with what held at each <c>break</c>.
    /// </summary>
    private void CheckLoop(Expression? condition, Block body)
    {
        var head = frame.Known;
        while (true)
        {
            var variables = declared.Count;
            frame.Known = head;
            Push();
            var (whenTrue, whenFalse) = (ImmutableHashSet<NullPath>.Empty, ImmutableHashSet<NullPath>.Empty);
            if (condition is not null)
            {
                CheckCondition(condition);
                head = frame.Known; // what the condition's `mut` arguments changed is changed at every round
                (whenTrue, whenFalse) = Tests(condition);
            }

            var exits = new LoopExits();
            frame.Loops.Add(exits);
            frame.Known = head.Union(whenTrue);
            frame.LoopDepth++;
            var flow = CheckBlock(body);
            frame.LoopDepth--;
            frame.Loops.RemoveAt(frame.Loops.Count - 1);
            var rounds = exits.Continues.ToList();
            if (flow == Flow.Completes)
            {
                rounds.Add(frame.Known);
            }

            var next = head.Intersect(Join(rounds, head));
            if (next.Count == head.Count)
            {
                Pop(commit: true);
                var leaves = condition is null || Unwrap(condition) is BoolLiteral { Value: true } ? exits.Breaks : exits.Breaks.Prepend(head.Union(whenFalse));
                frame.Known = Join(leaves, head);
                return;
            }

            Pop(commit: false);
            declared.RemoveRange(variables, declared.Count - variables); // the variables of a reading that does not stand
            head = next;
        }
    }

    /// <summary>
    /// N4: <c>a ?? b</c> is <c>a</c> where it is not null and <c>b</c> otherwise, as in C#: the type <c>a</c> makes nullable where
    /// <c>b</c> fits it, else <c>a</c>'s nullable type where <c>b</c> fits that. <c>b</c> runs only where <c>a</c> is null, so only what held
    /// before it is known after. A left side that is not nullable is <c>not supported</c> (section 4 says nothing of it).
    /// </summary>
    private Typed CheckCoalesce(BinaryExpression b)
    {
        var left = CheckNullable(b.Left);
        if (left.IsError)
        {
            CheckLoosely(b.Right);
            return Typed.Error;
        }

        if (Types.NonNull(left.Type) is not { } inner)
        {
            NotSupported(b.Line, $"`??` on a `{left.Type}`, which is not nullable (N4)");
            CheckLoosely(b.Right);
            return Typed.Error;
        }

        var before = frame.Known;
        var right = CheckExpression(b.Right, left.Type);
        frame.Known = before.Intersect(frame.Known);
        if (right.IsError)
        {
            return Typed.Error;
        }

        if (Types.Assignable(right.Type, inner))
        {
            return new Typed(inner);
        }

        if (Types.Assignable(right.Type, left.Type))
        {
            return new Typed(left.Type);
        }

        Report(Types.IsVoid(right.Type) ? ErrorIds.Syntax : Types.MismatchId(right.Type, inner), b.Right.Line, Types.IsVoid(right.Type)
            ? "a call with a `void` success used as a value (O10)"
            : $"a `{right.Type}` on the right of `??` where a `{inner}` is required (N4, T1)");
        return Typed.Error;
    }

    /// <summary>N5: whether the member is read in a chain of <c>?.</c>: the chain goes on through members until a parenthesis ends it.</summary>
    private static bool InChain(Expression e) => e is MemberAccess m && (m.Conditional || InChain(m.Target));

    /// <summary>
    /// N5: <c>a?.M</c> is null where <c>a</c> is and <c>a.M</c> otherwise, and each member after it in the chain is read only where the
    /// chain did not stop, as in C#: the chain's value is the nullable type of the member read, or the member's own type where that is
    /// nullable. A member after it in the chain is read from the member before as that member's own type, so a nullable one there is
    /// N2's. <c>?.</c> on a value that is not nullable, and to a member whose type no <c>?</c> makes nullable (a union, a function, a
    /// pointer), is <c>not supported</c> (section 4 says nothing of them).
    /// </summary>
    private Typed CheckChainMember(MemberAccess m)
    {
        var target = m.Conditional ? CheckNullable(m.Target) : CheckExpression(m.Target, null);
        if (target.IsError)
        {
            return Typed.Error;
        }

        KurzType from;
        if (m.Conditional)
        {
            if (Types.NonNull(target.Type) is not { } inner)
            {
                NotSupported(m.Line, $"`?.` on a `{target.Type}`, which is not nullable (N5)");
                return Typed.Error;
            }

            from = inner;
        }
        else
        {
            from = chainMembers[(MemberAccess)m.Target];
            if (Types.NonNull(from) is not null)
            {
                Report(ErrorIds.NullableUnchecked, m.Line, $"`{m.Member}` is read from a `{from}` without a check (N2)");
                return Typed.Error;
            }
        }

        var member = MemberOf(from, m.Member, m.Line);
        if (member.IsError)
        {
            return Typed.Error;
        }

        if (Types.NonNull(member.Type) is null && member.Type is UnionType or FunctionType or PointerType or CFunctionType or VoidType)
        {
            NotSupported(m.Line, $"`?.` to a `{member.Type}`, which no `?` makes nullable (N1, N5)");
            return Typed.Error;
        }

        chainMembers[m] = member.Type;
        model.RecordChainMember(m, member.Type);
        return new Typed(Types.NonNull(member.Type) is null ? Types.Nullable(member.Type) : member.Type);
    }
}
