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

/// <summary>N3: what held at each <c>break</c> and <c>continue</c> of one loop, where that <c>break</c> or <c>continue</c> can be reached.</summary>
internal sealed class LoopExits
{
    public List<ImmutableHashSet<NullPath>> Breaks { get; } = [];

    public List<ImmutableHashSet<NullPath>> Continues { get; } = [];

    /// <summary>U12: the moves at each <c>break</c> and <c>continue</c> of <see cref="Breaks"/> and <see cref="Continues"/>, in their order.</summary>
    public List<MoveState> BreakMoves { get; } = [];

    public List<MoveState> ContinueMoves { get; } = [];

    /// <summary>T33: a loop over <c>T.Fields</c>, unrolled into a pass for each field, whose <c>break</c> and <c>continue</c> are <c>not supported</c>.</summary>
    public bool Unrolled { get; init; }
}

/// <summary>
/// Chapter 4: <c>T?</c>, <c>null</c>, <c>??</c>, <c>?.</c>, <c>?[</c>, <c>==</c> with a nullable side and the flow of the code that
/// makes a nullable variable or path of <c>data</c> fields a value of the type it makes nullable (N1 to N8, N10). What the code shows
/// is kept as the set of paths known not null where the checker reads (<see cref="Frame.Known"/>): a test adds its paths to the branch
/// it guards, an assignment of a value whose type has no <c>null</c> adds its target, any other assignment and a <c>mut</c> argument
/// take away what runs through their target, and where branches or the rounds of a loop meet only what every path that reaches the
/// point keeps is kept, the join C#'s nullable analysis makes (section 4, an assumption there). A path through a <c>mut</c> field of a
/// class instance is known up to the next call or assignment (N9, <see cref="EndChangeableNarrowing"/>); a <c>weak</c> reference is not read.
/// </summary>
public sealed partial class Checker
{
    // N5: the type of each member, and of each item of `?[`, of a chain before the chain makes it nullable, which the next member of the chain reads
    private readonly Dictionary<Expression, KurzType> chainMembers = new(ReferenceEqualityComparer.Instance);

    // N5: the value each call after `?.` or `?[` is called on, of the type the chain makes nullable; each such call by the call it is
    // read as, with its member and the calls read before its arguments; and those of them that write the place their chain names
    private readonly Dictionary<ChainValue, Typed> chainValues = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallExpression, (CallExpression Chain, MemberAccess Member, int Calls)> chainCalls = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CallExpression> placeCalls = new(ReferenceEqualityComparer.Instance);

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
            case NameExpression n when ImplicitField(n.Name) is not null:
                return new NullPath(ThisVariable(), n.Name); // K7: a field of `this` by its name
            case ThisExpression when CurrentClass() is not null:
                return new NullPath(ThisVariable(), string.Empty);
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
    /// what the condition leaves where it is false, joined with what held at each <c>break</c> that can be reached, and a <c>while true</c>
    /// what held at those <c>break</c>s alone; a <c>break</c> or a <c>continue</c> that is never reached adds nothing, one after a statement
    /// whose end cannot be reached, in the branch a constant condition never takes or in the body of <c>while false</c> (the frame's
    /// <c>Unreached</c>); a <c>for</c> (C8), which has no condition and can run no round, leaves with what the head holds as well.
    /// Returns whether the loop can end: a <c>while</c> whose condition is the constant <c>true</c> ends only through a <c>break</c> of its
    /// own that can be reached, as the end point of C#'s <c>while (true)</c> is reached only through one (F3), and the same constant decides
    /// both; the body of one whose condition is the constant <c>false</c> is never reached, as in C#.
    /// </summary>
    private bool CheckLoop(Expression? condition, Block body, Action? round = null)
    {
        var head = frame.Known;
        var before = frame.Moves; // U12: a round starts with what the rounds before it may have moved, which a read meets as moved in an earlier round
        var headMoves = before;
        while (true)
        {
            var variables = declared.Count;
            frame.Known = head;
            frame.Moves = headMoves;
            round?.Invoke();
            Push();
            var tested = head;
            var testedMoves = frame.Moves;
            var (whenTrue, whenFalse) = (ImmutableHashSet<NullPath>.Empty, ImmutableHashSet<NullPath>.Empty);
            bool? constant = null;
            if (condition is not null)
            {
                var calls = callsRun;
                CheckCondition(condition);
                constant = ConstantCondition(condition);
                tested = frame.Known; // what the condition's `mut` arguments changed, while the head stays what a round comes back to
                testedMoves = frame.Moves;
                (whenTrue, whenFalse) = ConditionTests(condition, calls);
            }

            var exits = new LoopExits();
            frame.Loops.Add(exits);
            frame.Known = tested.Union(whenTrue);
            frame.LoopDepth++;
            var unreached = frame.Unreached;
            frame.Unreached = unreached || constant == false;
            var flow = CheckBlock(body);
            frame.Unreached = unreached;
            frame.LoopDepth--;
            frame.Loops.RemoveAt(frame.Loops.Count - 1);
            var rounds = exits.Continues.ToList();
            var movesRounds = exits.ContinueMoves.ToList();
            if (flow == Flow.Completes && constant != false)
            {
                rounds.Add(frame.Known);
                movesRounds.Add(frame.Moves);
            }

            var next = head.Intersect(Join(rounds, head));
            var fresh = declared.Skip(variables).ToList(); // a variable declared in the body is new each round
            var nextMoves = headMoves.Union(MoveState.Join(movesRounds, headMoves).Without(fresh).Rounds(before, earlier: true));
            if (next.Count == head.Count && nextMoves.Same(headMoves))
            {
                Pop(commit: true);
                var forever = constant == true;
                var leaves = forever ? exits.Breaks : exits.Breaks.Prepend(tested.Union(whenFalse));
                frame.Known = Join(leaves, tested);
                var movesLeaving = forever ? exits.BreakMoves : exits.BreakMoves.Prepend(testedMoves);
                frame.Moves = MoveState.Join(movesLeaving, testedMoves).Without(fresh).Rounds(before, earlier: false);
                return !forever || exits.Breaks.Count > 0;
            }

            Pop(commit: false);
            declared.RemoveRange(variables, declared.Count - variables); // the variables of a reading that does not stand
            head = next;
            headMoves = nextMoves;
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

    /// <summary>N5: whether the member is read in a chain of <c>?.</c> or <c>?[</c>: the chain goes on through members and the calls of members until a parenthesis ends it.</summary>
    private static bool InChain(Expression e) =>
        e is IndexExpression { Conditional: true }
        || (e is MemberAccess m && (m.Conditional || InChain(m.Target)))
        || (e is CallExpression { Callee: MemberAccess c } && (c.Conditional || InChain(c.Target)));

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

        if (from is ClassType owner && Property(owner.Symbol, m.Member) is not null)
        {
            NotSupported(m.Line, "a property read after `?.` (K7, N5)");
            return Typed.Error;
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
    /// N5: <c>a?.M(x)</c> is null where <c>a</c> is, its arguments not run, and otherwise the call <c>M(x)</c> on <c>a</c>'s case other
    /// than <c>null</c>, of its result made nullable, a <c>T?</c> as it is and a union with <c>null</c> among its cases, and the rest of the
    /// chain is skipped at null, as C# reads a call after <c>?.</c> (section 4, the owner, 2026-11-24, #624, comment 7130490403, "yes support
    /// calls after ?. like c#"); a call in the chain, after a member or <c>?[</c>, is read from the value before it as a member is
    /// (<see cref="CheckChainMember"/>). The call is read as the same call on a <see cref="ChainValue"/>, so it is every call's reading
    /// (<paramref name="read"/>), its errors among it: a <c>void</c> call gives <c>void</c>, which as a value is O10's, and a <c>mut</c>
    /// one writes the place the chain names (<see cref="ChainReceiver"/>).
    /// </summary>
    private Typed CheckChainCall(MemberAccess m, CallExpression c, Func<CallExpression, Typed> read)
    {
        // N9: a call in the chain's index or key runs before the place is taken, as one in the arguments does, so it is counted with
        // them (the owner, 2026-11-24, pull request 631, comment 7143447670, "Refuse it in the push (Recommended)")
        var callsBefore = callsRun;
        var target = m.Conditional ? CheckNullable(m.Target) : CheckExpression(m.Target, null);
        if (target.IsError)
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        KurzType from;
        if (m.Conditional)
        {
            if (Types.NonNull(target.Type) is not { } inner)
            {
                Report(ErrorIds.TypeMismatch, m.Line, $"`?.` needs a nullable left side; `{target.Type}` is not one (N5)");
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            from = inner;
        }
        else
        {
            from = chainMembers[m.Target];
            if (Types.NonNull(from) is { } known && PathOf(m.Target) is { } path && frame.Known.Contains(path))
            {
                from = known; // N3, as a member after it reads
            }
            else if (Types.NonNull(from) is not null)
            {
                Report(ErrorIds.NullableUnchecked, m.Line, $"`{m.Member}` is read from {Types.A(from)} without a check (N2)");
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }
        }

        var receiver = new ChainValue(m.Target.Line, m.Target);
        chainValues[receiver] = new Typed(from);
        var call = c with { Callee = new MemberAccess(m.Line, receiver, m.Member, false) };
        chainCalls[call] = (c, m, callsBefore);
        var outer = sending;
        if (ReferenceEquals(c, sending))
        {
            sending = call; // U6: `send a?.M()`, a message where the chain did not stop
        }

        var result = read(call);
        sending = outer;
        if (result.IsError)
        {
            return Typed.Error;
        }

        if (Types.NonNull(result.Type) is null && result.Type is PointerType or CFunctionType)
        {
            NotSupported(m.Line, $"`?.` to {Types.A(result.Type)}, which no `?` makes nullable (N1, N5)");
            return Typed.Error;
        }

        chainMembers[c] = result.Type;
        model.RecordChainCall(c, new ChainCall(call, receiver, result.Type, placeCalls.Contains(c)));
        return new Typed(Types.IsVoid(result.Type) || Types.NonNull(result.Type) is not null ? result.Type : Types.Nullable(result.Type));
    }

    /// <summary>
    /// N5, M5: the receiver of a <c>mut</c> method after <c>?.</c> (<see cref="CheckChainCall"/>). Where the chain names a place, a
    /// variable, a path of fields from one, or an item of a list or an entry of a map such a path holds, the call changes that place, so
    /// that <c>groups[key]?.Add(n)</c> adds to the map's list, as Rust's <c>get_mut</c> and <c>push</c> do (section 4, the owner,
    /// 2026-11-24, #624, comment 7131301981, "Write the place (Recommended)"); the place is read as M5 reads the receiver of the same call
    /// without <c>?.</c>, so that a value that is no place is the error that call gets. A place a call can change (N9) whose call's
    /// arguments run a call is N2's <c>nullable-unchecked</c>, the error the call gets in a test's branch with that call on the line before
    /// it (the owner, 2026-11-24, #624, comment 7131654435, "Refuse it (Recommended)"). After a member of the chain on a path of fields
    /// from a variable, <c>cfg?.Items.Add(3)</c>, the call changes the field's place in the value the chain read, as the same call in a
    /// test's branch does (<see cref="MutPlace"/>; #624, comment 7135538740, issue 630), and a call in its arguments that can set a nullable
    /// variable or field on the way to <c>null</c> is <c>nullable-unchecked</c>, as in that branch (<see cref="NullableStepOnTheWay"/>).
    /// After an item or a call of the chain, the receiver is what the same call in a test's branch reaches: an item of a list, a value.
    /// </summary>
    private void ChainReceiver(CallExpression call, ChainValue value, int line, string what)
    {
        var (chain, member, callsBefore) = chainCalls[call];
        var of = Unwrap(value.Of);
        if (!member.Conditional && of is MemberAccess && PathRoot(of) is NameExpression or ThisExpression)
        {
            Push();
            MutReceiver(chain, of, line, what);
            if (Pop(commit: true).Count > 0)
            {
                return;
            }

            if (callsRun > callsBefore && NullableStepOnTheWay(of) is var (step, declared))
            {
                Add(Diagnostic.Error(ErrorIds.NullableUnchecked, step.Line, $"`{step.Member}` is read from {Types.A(declared)} without a check (N2)") with { OwnHelp = EndedHelp(step.Target) });
                return;
            }

            placeCalls.Add(chain);
            return;
        }

        if (!member.Conditional)
        {
            if (of is IndexExpression)
            {
                NotSupported(line, "a `mut` method on an element of a list (M5, M9)");
            }
            else if (of is MemberAccess)
            {
                NotSupported(line, "a `mut` method on a path through a nullable variable or field (N3, M5)");
            }
            else
            {
                Report(ErrorIds.MutRequired, line, $"{what}, and only a value reachable through a `mut` variable changes (M5)");
            }

            return;
        }

        var place = of is IndexExpression { Conditional: false } i && model.HasType(i.Target) && model.TypeOf(i.Target).Type is ListType or MapType ? Unwrap(i.Target) : of;
        Push();
        MutReceiver(chain, place, line, what);
        if (Pop(commit: true).Count > 0)
        {
            return;
        }

        if (PathOf(place) is { } path && Changeable(path) && callsRun > callsBefore)
        {
            Add(Diagnostic.Error(ErrorIds.NullableUnchecked, member.Line, $"`{member.Member}` is read from {Types.A(model.TypeOf(of).Type)} without a check (N2)") with { OwnHelp = EndedHelp(of) });
            return;
        }

        placeCalls.Add(chain);
    }

    /// <summary>The root of a path of members, conditional or not: the expression the first member is read from.</summary>
    private static Expression PathRoot(Expression e)
    {
        var root = Unwrap(e);
        while (root is MemberAccess m)
        {
            root = Unwrap(m.Target);
        }

        return root;
    }

    /// <summary>
    /// N9: the first member, from the root, of the path to a <c>mut</c> method's receiver after <c>?.</c> that is read from a nullable
    /// variable or field a call can change, with that step's declared type: a call in the arguments can set it to <c>null</c> before the
    /// call takes the place (issue 630); null where no step is so.
    /// </summary>
    private (MemberAccess Step, KurzType Declared)? NullableStepOnTheWay(Expression place)
    {
        var steps = new List<MemberAccess>();
        for (var e = Unwrap(place); e is MemberAccess m; e = Unwrap(m.Target))
        {
            steps.Insert(0, m);
        }

        foreach (var m in steps)
        {
            if (PathOf(m.Target) is { } path && Changeable(path) && NarrowedPathType(m.Target) is { } declared && Types.NonNull(declared) is not null)
            {
                return (m, declared);
            }
        }

        return null;
    }

    /// <summary>
    /// N9: a write into a path a call can change, a <c>mut</c> method on it or an item or an entry written into it, read where the path
    /// was known not null, after a call in the write's arguments or value ended that (<see cref="EndChangeableNarrowing"/>): the write
    /// meets what it meets with that call on the line before it, N2's <c>nullable-unchecked</c>, <paramref name="written"/> naming the
    /// write (the owner, 2026-11-24, #628, comment 7131654741, "Refuse it (Recommended)", and comment 7131713661). A longer path through
    /// such a field is refused before (K2, M4, M5). <paramref name="throughNullable"/> reads the declared type of a path that runs through
    /// a nullable step before it (<see cref="NarrowedPathType"/>), for a step on the way to a <c>mut</c> method's receiver (issue 630).
    /// Returns whether it reported.
    /// </summary>
    private bool NarrowingEnded(Expression receiver, Func<KurzType, string> written, int line, bool throughNullable = false)
    {
        var e = Unwrap(receiver);
        if (PathOf(e) is { } path && Changeable(path) && !frame.Known.Contains(path) && model.HasType(e) && Types.NonNull(model.TypeOf(e).Type) is null
            && (throughNullable ? NarrowedPathType(e) : PathType(e)) is { } declared && Types.NonNull(declared) is not null)
        {
            Add(Diagnostic.Error(ErrorIds.NullableUnchecked, line, $"{written(declared)} without a check (N2)") with { OwnHelp = EndedHelp(e) });
            return true;
        }

        return false;
    }

    /// <summary>
    /// N9: the help line of a write that a call in its arguments or its value refused (<see cref="ChainReceiver"/>,
    /// <see cref="NarrowingEnded"/>): that call can set the path to <c>null</c>, and it runs into a variable first (section 4, the owner,
    /// 2026-11-24, #624, comment 7135538740, "A help line of its own (Recommended)"), the call and the path as the source writes them, or
    /// "a call in this line" where the last call to end the check is no call of a function or a method (section 4, an assumption there).
    /// </summary>
    private string EndedHelp(Expression path) =>
        $"{(endedBy is { } call && SourceOf(call) is { } text ? $"`{text}`" : "a call in this line")} can set {(SourceOf(path) is { } p ? $"`{p}`" : "the path")} to `null`: run it into a variable before this line";

    /// <summary>N9: an expression as its source writes it, for a help line: a name, a member, an item, a call and the forms of <see cref="DefaultText"/>; null for another form.</summary>
    private static string? SourceOf(Expression e) => e switch
    {
        NameExpression n => n.Name,
        ThisExpression => "this",
        ParenthesizedExpression p => SourceOf(p.Inner) is { } inner ? $"({inner})" : null,
        MemberAccess m => SourceOf(m.Target) is { } target ? $"{target}{(m.Conditional ? "?." : ".")}{m.Member}" : null,
        IndexExpression i => SourceOf(i.Target) is { } target && SourceOf(i.Index) is { } index ? $"{target}{(i.Conditional ? "?[" : "[")}{index}]" : null,
        CallExpression { TypeArguments.Count: 0, Within: null } c => SourceOf(c.Callee) is { } callee && c.Arguments.Select(ArgumentSource).ToList() is var arguments && arguments.All(a => a is not null)
            ? $"{callee}({string.Join(", ", arguments)})" : null,
        _ => DefaultText(e) is var text && !text.Contains("...", StringComparison.Ordinal) ? text : null,
    };

    /// <summary>An argument as its source writes it, its name and its word in front (D11, M7, U12), or null (<see cref="SourceOf"/>).</summary>
    private static string? ArgumentSource(Argument a) =>
        SourceOf(a.Value) is { } value
            ? (a.Name is null ? "" : $"{a.Name}: ") + a.Word switch { ArgumentWord.Mut => "mut ", ArgumentWord.Copy => "copy ", ArgumentWord.Move => "move ", _ => "" } + value
            : null;

    /// <summary>
    /// N5: <c>xs?[i]</c> is null where <c>xs</c> is and its item at <c>i</c> otherwise, and the rest of the chain is skipped with it, as
    /// C#'s <c>?[]</c> (section 4, the owner, 2026-10-30, "Add, as C#"): the type of the item made nullable, or the item's own where it is
    /// nullable, and of a <c>weak</c> item its read, a strong <c>T?</c> (R3). The index runs only where the list is not null, so only what held before it is known after. On a value that is not
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

        var item = Types.Read(list.Element); // R3: a `weak` item reads as a strong `T?`, as `xs[i]` reads it
        chainMembers[i] = item;
        return new Typed(Types.Nullable(item));
    }
}
