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
/// Chapter 4: <c>T?</c>, <c>null</c>, <c>??</c>, <c>?.</c>, <c>?[</c>, <c>==</c> with a nullable side and the flow of the code that
/// makes a nullable variable or path of <c>data</c> fields a value of the type it makes nullable (N1 to N8, N10). What the code shows
/// is kept as the set of paths known not null where the checker reads (<see cref="Frame.Known"/>): a test adds its paths to the branch
/// it guards, an assignment of a value whose type has no <c>null</c> adds its target, any other assignment and a <c>mut</c> argument
/// take away what runs through their target, and where branches or the rounds of a loop meet only what every path that reaches the
/// point keeps is kept, the join C#'s nullable analysis makes (section 4, an assumption there). A path through a class instance or a
/// <c>weak</c> reference (N9) is not read: the checker reads neither.
/// </summary>
public sealed partial class Checker
{
    // N5: the type of each member, and of each item of `?[`, of a chain before the chain makes it nullable, which the next member of the chain reads
    private readonly Dictionary<Expression, KurzType> chainMembers = new(ReferenceEqualityComparer.Instance);

    // N3, N7: what was known after the right side of each `&&` and `||`, which a path the left side showed must still be in
    private readonly Dictionary<BinaryExpression, ImmutableHashSet<NullPath>> afterRight = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// N1: <c>T?</c>, the union <c>T | null</c>. A type parameter's <c>T?</c> is the type that fills it where that type is nullable
    /// already, <c>T??</c> being <c>T?</c> (section 4, the owner, 2026-10-30, "Flatten, as C#"), which <see cref="Types.Substitute"/>
    /// makes. A union and a function type have their nullable form, <c>(int | string)?</c> being <c>int | string | null</c> (section 4,
    /// the owner, 2026-10-30, "Nullable form, as Option"). A pointer, which <c>null</c> is a value of already (R10), and <c>void</c> are
    /// <c>not supported</c>: section 4 says nothing of them.
    /// </summary>
    private KurzType ResolveNullable(NullableType n)
    {
        var inner = Resolve(n.Inner);
        if (inner is ErrorType)
        {
            return Types.Error;
        }

        if (!CanBeNullable(inner))
        {
            NotSupported(n.Line, $"a nullable `{inner}` (N1)");
            return Types.Error;
        }

        return Types.Nullable(inner);
    }

    /// <summary>N1: whether the checker reads <paramref name="inner"/> made nullable, which <see cref="ResolveNullable"/> names; a map's read takes the same (M9).</summary>
    private static bool CanBeNullable(KurzType inner) => inner is not (PointerType or VoidType);

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

    /// <summary>N3: the paths that are not null where the value of <paramref name="e"/> is not: its own, and each value a member of it was read from or a <c>?[</c> indexed, so that a test of <c>a?.b != null</c> or of <c>xs?[0] != null</c> shows <c>a</c> or <c>xs</c> too, as C# reads them.</summary>
    private ImmutableHashSet<NullPath> NonNullPaths(Expression e)
    {
        var paths = ImmutableHashSet<NullPath>.Empty;
        for (var x = Unwrap(e); x is not null; x = x switch { MemberAccess m => Unwrap(m.Target), IndexExpression { Conditional: true } i => Unwrap(i.Target), _ => null })
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
    /// they can come out so (the reading C# takes), where what the left side showed holds past the right side only if the right side
    /// did not change it (N7).
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
                    var kept = KeptPastRight(b, lt);
                    return (kept.Union(rt), lf.Intersect(kept.Union(rf)));
                }

            case BinaryExpression { Operator: TokenKind.BarBar } b:
                {
                    var (lt, lf) = Tests(b.Left);
                    var (rt, rf) = Tests(b.Right);
                    var kept = KeptPastRight(b, lf);
                    return (lt.Intersect(kept.Union(rt)), kept.Union(rf));
                }

            case MemberAccess when ClassTested(condition) is { } tested:
                return (tested, none); // R18: a test of a class's `IsSupported` guards where it is true, as a test of `null` narrows

            case BinaryExpression { Operator: TokenKind.EqualEqual or TokenKind.NotEqual } b when Unwrap(b.Left) is NullLiteral != Unwrap(b.Right) is NullLiteral:
                {
                    var paths = NonNullPaths(Unwrap(b.Left) is NullLiteral ? b.Right : b.Left);
                    return b.Operator == TokenKind.NotEqual ? (paths, none) : (none, paths);
                }

            case BinaryExpression { Operator: TokenKind.EqualEqual or TokenKind.NotEqual } b:
                {
                    // the lifted `==` (N10): equal to a value that is not nullable, a nullable side is not null, as C#'s analysis reads it (section 4)
                    var paths = NonNullBeside(b.Left, b.Right).Union(NonNullBeside(b.Right, b.Left));
                    return b.Operator == TokenKind.EqualEqual ? (paths, none) : (none, paths);
                }

            default:
                return (none, none);
        }
    }

    /// <summary>N3, N10: the paths <paramref name="e"/> shows not null where it equals <paramref name="other"/>: those of a nullable <paramref name="e"/> beside a value whose type has no <c>null</c>.</summary>
    private ImmutableHashSet<NullPath> NonNullBeside(Expression e, Expression other) =>
        model.HasType(e) && model.HasType(other) && Types.NonNull(model.TypeOf(e).Type) is not null && model.TypeOf(other) is { IsError: false } o && Types.NonNull(o.Type) is null && o.Type is not NullType
            ? NonNullPaths(e)
            : ImmutableHashSet<NullPath>.Empty;

    /// <summary>N7: of what the left side of <paramref name="b"/> showed, what the right side left as it was; nothing where the right side was not read.</summary>
    private ImmutableHashSet<NullPath> KeptPastRight(BinaryExpression b, ImmutableHashSet<NullPath> shown) =>
        afterRight.TryGetValue(b, out var after) ? shown.Intersect(after) : ImmutableHashSet<NullPath>.Empty;

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
        RecordGuards(b.Right); // R18: the right side runs where the left showed what it shows
        var right = CheckExpression(b.Right, Types.Bool);
        afterRight[b] = frame.Known;
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

        Report(ErrorIds.NullableUnchecked, line, $"{Types.A(value.Type)} {where} without a check (N2)");
        return true;
    }

    /// <summary>
    /// N3: a value a test, a <c>match</c> with a <c>null</c> arm, <c>??</c> or <c>?.</c> reads the case of: a variable or a path of fields known not null is read as the nullable
    /// type it has, so that a second test, or a <c>??</c> or <c>?.</c> after a test, reads as the first did.
    /// </summary>
    private Typed CheckNullable(Expression e, bool keepCases = false)
    {
        var known = frame.Known;
        if (PathOf(e) is { } path && Unwrap(e) is NameExpression or MemberAccess { Conditional: false })
        {
            frame.Known = frame.Known.Remove(path); // a variable or a path of fields, which runs nothing that could change what is known
            var read = CheckExpression(e, null, keepCases);
            frame.Known = known;
            return read;
        }

        return CheckExpression(e, null, keepCases); // anything else keeps what it changed: a `mut` argument in it ends a narrowing (N7)
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
            : $"`null` compares with a nullable value or a pointer, not with {Types.A(typed.Type)} (N6, N1)");
        return new Typed(Types.Bool);
    }

    /// <summary>N5: <c>?[</c> on a value that is not nullable.</summary>
    private void NotNullable(IndexExpression i, Typed target) =>
        Report(ErrorIds.TypeMismatch, i.Line, $"`?[` needs a nullable left side; `{target.Type}` is not one (N5)");

    /// <summary>N3: a read of a variable or a field of a nullable type, as the type it makes nullable where its path is known not null.</summary>
    private Typed Narrowed(Expression read, Typed typed) =>
        Types.NonNull(typed.Type) is { } inner && PathOf(read) is { } path && frame.Known.Contains(path) ? new Typed(inner) : typed;

    /// <summary>
    /// A loop (C6, C8): its body, and its condition before each round, are read with what holds at its head: what held before it, less
    /// what a round leaves nullable at the end of its body or at a <c>continue</c>, the join C# makes where the paths around a loop meet
    /// (section 4, an assumption there). Where a reading ends with less known than it began with, it is read again from that, its
    /// diagnostics dropped, until a reading keeps what it began with; that reading's diagnostics are the loop's. After the loop holds
    /// what the condition leaves where it is false, joined with what held at each <c>break</c>, and a <c>while true</c> what held at its
    /// <c>break</c>s alone; a <c>for</c> (C8), which has no condition and can run no round, leaves with what the head holds as well.
    /// </summary>
    private void CheckLoop(Expression? condition, Block body)
    {
        var head = frame.Known;
        while (true)
        {
            var variables = declared.Count;
            frame.Known = head;
            Push();
            var tested = head;
            var (whenTrue, whenFalse) = (ImmutableHashSet<NullPath>.Empty, ImmutableHashSet<NullPath>.Empty);
            if (condition is not null)
            {
                CheckCondition(condition);
                tested = frame.Known; // what the condition's `mut` arguments changed, while the head stays what a round comes back to
                (whenTrue, whenFalse) = Tests(condition);
            }

            var exits = new LoopExits();
            frame.Loops.Add(exits);
            frame.Known = tested.Union(whenTrue);
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
                var leaves = condition is not null && Unwrap(condition) is BoolLiteral { Value: true } ? exits.Breaks : exits.Breaks.Prepend(tested.Union(whenFalse));
                frame.Known = Join(leaves, tested);
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
    /// before it is known after. A left side that is not nullable is <c>type-mismatch</c> (section 4, the owner, 2026-10-30, "Error, as Rust").
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
            Report(ErrorIds.TypeMismatch, b.Line, $"`??` needs a nullable left side; `{left.Type}` is not one (N4)");
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

        if (Types.IsVoid(right.Type))
        {
            Report(ErrorIds.Syntax, b.Right.Line, "a call with a `void` success used as a value (O10)");
        }
        else
        {
            ReportMismatch(right.Type, inner, b.Right.Line, $"{Types.A(right.Type)} on the right of `??` where {Types.A(inner)} is required", "N4");
        }

        return Typed.Error;
    }

    /// <summary>N5: whether the member is read in a chain of <c>?.</c> or <c>?[</c>: the chain goes on through members until a parenthesis ends it.</summary>
    private static bool InChain(Expression e) => e is IndexExpression { Conditional: true } || (e is MemberAccess m && (m.Conditional || InChain(m.Target)));

    /// <summary>
    /// N5: <c>a?.M</c> is null where <c>a</c> is and <c>a.M</c> otherwise, and each member after it in the chain is read only where the
    /// chain did not stop, as in C#: the chain's value is the nullable type of the member read, or the member's own type where that is
    /// nullable. A member after it in the chain is read from the member before as that member's own type, so a nullable one there is
    /// N2's, unless the member's path is known not null (N3), which it is then read as. <c>?.</c> on a value that is not nullable is <c>type-mismatch</c> (section 4, the owner, 2026-10-30, "Error, as Rust"), and
    /// to a member whose type no <c>?</c> makes nullable (a pointer, a C function type, <c>void</c>) <c>not supported</c> (section 4 says nothing
    /// of it); a union's and a function type's nullable form is N1's (section 4, the owner, 2026-10-30, "Nullable form, as Option").
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
                Report(ErrorIds.TypeMismatch, m.Line, $"`?.` needs a nullable left side; `{target.Type}` is not one (N5)");
                return Typed.Error;
            }

            from = inner;
        }
        else
        {
            from = chainMembers[m.Target];
            if (Types.NonNull(from) is { } known && PathOf(m.Target) is { } path && frame.Known.Contains(path))
            {
                from = known; // N3: a test showed the member's path not null, as C# narrows `u?.Email` after `u?.Email != null`
            }
            else if (Types.NonNull(from) is not null)
            {
                Report(ErrorIds.NullableUnchecked, m.Line, $"`{m.Member}` is read from {Types.A(from)} without a check (N2)");
                return Typed.Error;
            }
        }

        var member = MemberOf(from, m.Member, m.Line);
        if (member.IsError)
        {
            return Typed.Error;
        }

        if (Types.NonNull(member.Type) is null && member.Type is PointerType or CFunctionType or VoidType)
        {
            NotSupported(m.Line, $"`?.` to {Types.A(member.Type)}, which no `?` makes nullable (N1, N5)");
            return Typed.Error;
        }

        chainMembers[m] = member.Type;
        model.RecordChainMember(m, member.Type);
        return new Typed(Types.NonNull(member.Type) is null ? Types.Nullable(member.Type) : member.Type);
    }

    /// <summary>
    /// N5: <c>xs?[i]</c> is null where <c>xs</c> is and its item at <c>i</c> otherwise, and the rest of the chain is skipped with it, as
    /// C#'s <c>?[]</c> (section 4, the owner, 2026-10-30, "Add, as C#"): the type of the item made nullable, or the item's own where it is
    /// nullable. The index runs only where the list is not null, so only what held before it is known after. On a value that is not
    /// nullable it is <c>type-mismatch</c>, as <c>?.</c> is (section 4, an assumption there), with a part in it as well; a part,
    /// <c>xs?[a..b]</c>, on a nullable list is <c>not supported</c> (section 4 names the item), and so is <c>m?[key]</c> on a nullable map.
    /// </summary>
    private Typed CheckConditionalIndex(IndexExpression i)
    {
        var target = CheckNullable(i.Target);
        if (i.Index is RangeExpression)
        {
            if (target.IsError)
            {
                // the target's own error, and nothing more (E1)
            }
            else if (Types.NonNull(target.Type) is null)
            {
                NotNullable(i, target);
            }
            else
            {
                NotSupported(i.Line, "a part after `?[` (N5, M9)");
            }

            CheckLoosely(i.Index);
            return Typed.Error;
        }

        if (Types.NonNull(target.Type) is MapType)
        {
            NotSupported(i.Line, "`?[` on a map (N5, M9)"); // a map's entry by `?[`, which the emitter does not lay out
            CheckLoosely(i.Index);
            return Typed.Error;
        }

        var before = frame.Known;
        var index = CheckExpression(i.Index, Types.Int);
        frame.Known = before.Intersect(frame.Known);
        if (target.IsError)
        {
            return Typed.Error;
        }

        if (Types.NonNull(target.Type) is not { } inner)
        {
            NotNullable(i, target);
            return Typed.Error;
        }

        if (inner is not ListType list)
        {
            Report(inner is PrimitiveType { Kind: Primitive.String } ? ErrorIds.StringIndex : ErrorIds.TypeMismatch, i.Line, inner is PrimitiveType { Kind: Primitive.String }
                ? "a string is indexed through `.Bytes` or `.Chars` (T15)"
                : $"{Types.A(inner)} is not indexed (M9)");
            return Typed.Error;
        }

        if (Unchecked(index, i.Line, "used as an index"))
        {
            return Typed.Error;
        }

        if (!index.IsError && !Types.IsInteger(index.Type))
        {
            Report(ErrorIds.TypeMismatch, i.Line, $"an index is an integer, not {Types.A(index.Type)} (M9, T2)");
            return Typed.Error;
        }

        if (Types.NonNull(list.Element) is null && list.Element is PointerType or CFunctionType or VoidType)
        {
            NotSupported(i.Line, $"`?[` to {Types.A(list.Element)}, which no `?` makes nullable (N1, N5)");
            return Typed.Error;
        }

        chainMembers[i] = list.Element;
        return new Typed(Types.Nullable(list.Element));
    }
}
