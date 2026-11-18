using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// U12, section 6: a <c>move</c> that may have ended a variable: the line of its argument, the text of the value it moved, the value
/// itself, and whether the checker read it in an earlier round of a loop around the read it meets.
/// </summary>
internal sealed record MoveSite(int Line, string Text, Expression Value, bool Earlier);

/// <summary>
/// U12, section 6: the regions of the values that need a word to cross into an actor, as Swift's SE-0414 keeps them, and the
/// <c>move</c>s that may have ended each variable, carried beside <see cref="Checker"/>'s known paths and joined where they are joined.
/// A region is a set of tags: 0 is the region outside the function (a parameter not written <c>move</c>, <c>this</c> with its fields, a
/// captured variable, a static), every other tag a value made in the function or a parameter written <c>move</c>; two tags a call or a
/// field's assignment merged are linked. A variable of the function has its tags here, a value that crosses with no word none (answer 2
/// of comment 6897778812); a variable with no entry is outside the function. Where paths meet, each variable's tags, the links and the
/// moves are united: a use is <c>used-after-move</c> where any path moved the value (comment 6897444564).
/// </summary>
internal sealed class MoveState(
    ImmutableDictionary<VariableSymbol, ImmutableHashSet<int>> tags,
    ImmutableHashSet<(int A, int B)> links,
    ImmutableDictionary<VariableSymbol, ImmutableHashSet<MoveSite>> moved)
{
    public static readonly MoveState Empty = new(
        ImmutableDictionary<VariableSymbol, ImmutableHashSet<int>>.Empty,
        ImmutableHashSet<(int A, int B)>.Empty,
        ImmutableDictionary<VariableSymbol, ImmutableHashSet<MoveSite>>.Empty);

    public ImmutableDictionary<VariableSymbol, ImmutableHashSet<int>> Tags { get; } = tags;

    public ImmutableHashSet<(int A, int B)> Links { get; } = links;

    public ImmutableDictionary<VariableSymbol, ImmutableHashSet<MoveSite>> Moved { get; } = moved;

    public MoveState With(
        ImmutableDictionary<VariableSymbol, ImmutableHashSet<int>>? tags = null,
        ImmutableHashSet<(int A, int B)>? links = null,
        ImmutableDictionary<VariableSymbol, ImmutableHashSet<MoveSite>>? moved = null) => new(tags ?? Tags, links ?? Links, moved ?? Moved);

    /// <summary>The join where paths meet: what any of them holds; <paramref name="otherwise"/> where no path reaches the point.</summary>
    public static MoveState Join(IEnumerable<MoveState> reaching, MoveState otherwise)
    {
        MoveState? joined = null;
        foreach (var state in reaching)
        {
            joined = joined is null ? state : joined.Union(state);
        }

        return joined ?? otherwise;
    }

    public MoveState Union(MoveState other) => new(UnionOf(Tags, other.Tags), Links.Union(other.Links), UnionOf(Moved, other.Moved));

    public bool Same(MoveState other) => Links.SetEquals(other.Links) && SameAs(Tags, other.Tags) && SameAs(Moved, other.Moved);

    /// <summary>The state without the variables, which a block's end or a round of a loop no longer holds.</summary>
    public MoveState Without(IEnumerable<VariableSymbol> variables)
    {
        var gone = variables.ToList();
        return gone.Count == 0 ? this : new(Tags.RemoveRange(gone), Links, Moved.RemoveRange(gone));
    }

    /// <summary>Every move that <paramref name="before"/> does not hold, marked as read in an earlier round of a loop, or no longer so.</summary>
    public MoveState Rounds(MoveState before, bool earlier) => new(Tags, Links, Moved.ToImmutableDictionary(
        p => p.Key,
        p => p.Value.Select(s => before.Moved.TryGetValue(p.Key, out var held) && held.Contains(s) ? s : s with { Earlier = earlier }).ToImmutableHashSet()));

    private static ImmutableDictionary<VariableSymbol, ImmutableHashSet<T>> UnionOf<T>(ImmutableDictionary<VariableSymbol, ImmutableHashSet<T>> a, ImmutableDictionary<VariableSymbol, ImmutableHashSet<T>> b)
    {
        var result = a;
        foreach (var (key, value) in b)
        {
            result = result.SetItem(key, result.TryGetValue(key, out var mine) ? mine.Union(value) : value);
        }

        return result;
    }

    private static bool SameAs<T>(ImmutableDictionary<VariableSymbol, ImmutableHashSet<T>> a, ImmutableDictionary<VariableSymbol, ImmutableHashSet<T>> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var other) && other.SetEquals(p.Value));
}

/// <summary>
/// U12, section 6: the check of what <c>move</c> ends. A <c>move</c> before an argument ends the variable it names and every variable
/// whose region its value's region reaches, as Swift's SE-0414 reads a <c>sending</c> argument; a use of one after it is
/// <c>used-after-move</c> (the owner, 2026-11-15, #519, comment 6896192351, Q1), and a <c>move</c> of a value the region outside the
/// function reaches is <c>move-reachable</c> (section 6). A call merges the regions of its arguments and of what it is called on, a
/// field's assignment the target's and the value's, a lambda joins the regions of what it captures, a binding of a <c>match</c> its
/// subject's, and a value that crosses with no word joins none, so that <c>move</c> of it ends the variable it names alone (comment
/// 6897778812, Q2 (a)). A loop whose body moves its variable takes its list (Q1 (b)).
/// </summary>
public sealed partial class Checker
{
    private static readonly ImmutableHashSet<int> Outside = [0];

    // the tag of a value whose region the checker does not keep: its `move` is `not supported`
    private static readonly ImmutableHashSet<int> Unkept = [-1];

    // the tag of each place that makes a value of a region: a declaration, an assignment, a loop, a binding, a `move` parameter, kept by
    // its node, so that the rounds of a loop read the same tags and meet
    private readonly Dictionary<object, int> regionTags = new(ReferenceEqualityComparer.Instance);

    // the second tag of each such place: every value an earlier round of a loop made there, which a round's new value leaves (BindRegion)
    private readonly Dictionary<int, int> earlierTags = [];

    private int lastTag;

    private readonly Dictionary<KurzType, bool> joinsRegions = [];

    // each `used-after-move` with its variable and the moves it names, so that one is kept for each (E1)
    private readonly List<(Diagnostic Diagnostic, VariableSymbol Variable, string Moves)> movedUses = [];

    // the tags each variable had where its block ended, which the last expression of a block that ends an arm gives to the arm's value;
    // each reading of a loop declares the variables of its body again, so that a later reading, and a lambda in it, holds none of the
    // variables an earlier reading ended here, and a variable a lambda captures is in scope where it is made, so that its block has not ended
    private readonly Dictionary<VariableSymbol, ImmutableHashSet<int>> endedTags = new(ReferenceEqualityComparer.Instance);

    private int RegionTag(object node)
    {
        if (!regionTags.TryGetValue(node, out var tag))
        {
            tag = ++lastTag;
            regionTags[node] = tag;
        }

        return tag;
    }

    private int EarlierTag(int tag)
    {
        if (!earlierTags.TryGetValue(tag, out var earlier))
        {
            earlier = ++lastTag;
            earlierTags[tag] = earlier;
        }

        return earlier;
    }

    /// <summary>Whether a value of the type joins a region: one that needs a word to cross into an actor; one that crosses with no word joins none (comment 6897778812, Q2 (a)).</summary>
    private bool JoinsRegion(KurzType type)
    {
        if (Types.IsError(type) || Types.IsVoid(type))
        {
            return false;
        }

        if (!joinsRegions.TryGetValue(type, out var joins))
        {
            joins = CrossingOf(type).Kind != ActorCrossing.Crosses;
            joinsRegions[type] = joins;
        }

        return joins;
    }

    /// <summary>
    /// The tags of the variable's region here, or where its block ended, as rustc reads a block's tail after the block's own variables;
    /// a variable with neither is outside the function.
    /// </summary>
    private ImmutableHashSet<int> TagsOf(VariableSymbol variable) =>
        !JoinsRegion(variable.Type) ? [] : frame.Moves.Tags.TryGetValue(variable, out var tags) ? tags : endedTags.TryGetValue(variable, out var ended) ? ended : Outside;

    /// <summary>Whether the variable belongs to the region outside the function: a parameter not written <c>move</c>, <c>this</c>, a captured variable, a static.</summary>
    private bool OutsideVariable(VariableSymbol variable) => !frame.Moves.Tags.ContainsKey(variable);

    private static ImmutableHashSet<int> Union(IEnumerable<ImmutableHashSet<int>> regions) => regions.Aggregate(ImmutableHashSet<int>.Empty, (a, b) => a.Union(b));

    /// <summary>The tags of the region a checked expression's value is in: none for a value that crosses with no word, outside for what the function did not make.</summary>
    private ImmutableHashSet<int> RegionOf(Expression e)
    {
        if (!model.HasType(e) || !JoinsRegion(model.TypeOf(e).Type))
        {
            return [];
        }

        return Unwrap(e) switch
        {
            NameExpression n => model.IsVariable(n) ? TagsOf(model.VariableOf(n)) : Outside, // K7, K18: a field of `this`, a static
            ThisExpression => Outside,
            MemberAccess m => Through(m.Target), // section 6: a field read is in the region of what it is read from (SE-0414), a fresh value's a fresh one (U12, assumed)
            IndexExpression i => Through(i.Target), // section 6: a list reaches each value it holds
            CallExpression c => !model.HasCall(c) ? Outside : model.CallOf(c).Kind is CallKind.ActorCall or CallKind.ActorSend ? [] : Union(CallRegions(c)), // U12 (assumed): an actor's call gives a region of its own, what it gives having crossed
            LambdaExpression l => model.HasLambda(l) ? Union(model.LambdaOf(l).Captures.Select(TagsOf)) : Outside, // a lambda joins what it captures
            ListLiteral l => Union(l.Items.Select(RegionOf)),
            WithExpression w => Union([RegionOf(w.Target), .. w.Assignments.Select(a => RegionOf(a.Value))]), // read as a call that takes both
            BinaryExpression b => RegionOf(b.Left).Union(RegionOf(b.Right)), // U12 (assumed): an operator's value joins its operands' regions
            MatchExpression m => Union([RegionOf(m.Subject), .. m.Arms.Select(a => ArmRegion(a.Body))]),
            ElseExpression x => Union([RegionOf(x.Call), .. x.Arms.Select(a => ArmRegion(a.Body))]), // U12 (assumed): an `else` binding joins its call's region
            SpawnExpression or NullLiteral => [], // U12 (assumed): a `spawn` gives a region of its own
            UnaryExpression u => RegionOf(u.Operand),
            TypeMemberAccess => Outside, // K18: a static
            _ => Unkept,
        };

        // the empty region of a value the function made that joins none, a construction, a call that merges none, an actor's call or a
        // `spawn`, gives an empty one, and what the function did not make, a static's path, is outside
        ImmutableHashSet<int> Through(Expression target) => model.HasType(target) ? RegionOf(target) : Outside;
    }

    private ImmutableHashSet<int> ArmRegion(Statement body) => body switch
    {
        ExpressionStatement es when model.HasType(es.Expression) => RegionOf(es.Expression),
        ReturnStatement or ThrowStatement or BreakStatement or ContinueStatement => [],
        Block { Statements: [.., var last] } => ArmRegion(last), // an arm that leaves gives no value; one that ends in a value gives its region, as rustc reads a block's tail (U12, assumed)
        _ => Unkept,
    };

    /// <summary>The regions a call merges: of every argument that is not written <c>copy</c> or <c>move</c>, written <c>mut</c> or not, and of what it is called on (comment 6897444564; the receiver, U12, assumed).</summary>
    private IEnumerable<ImmutableHashSet<int>> CallRegions(CallExpression c)
    {
        foreach (var a in c.Arguments)
        {
            if (a.Word is not (ArgumentWord.Copy or ArgumentWord.Move))
            {
                yield return RegionOf(a.Value);
            }
        }

        yield return Unwrap(c.Callee) switch
        {
            MemberAccess m => model.HasType(m.Target) ? RegionOf(m.Target) : [],
            NameExpression n when model.IsVariable(n) => TagsOf(model.VariableOf(n)), // a function value
            NameExpression when model.CallOf(c) is { Kind: CallKind.Method, Function.IsStatic: false } => Outside, // K7: a method of `this`
            // a static or top-level function gives what it made or what its arguments reach: a `static` field holds no class instance,
            // so that no mutable class instance reaches a global (section 6, the rule of a parameter written `move`; section 4; K18),
            // and a top-level function reads no top-level variable (F16)
            _ => [],
        };
    }

    /// <summary>Links the regions, so that a move of one ends the others.</summary>
    private void Merge(IEnumerable<ImmutableHashSet<int>> regions)
    {
        var tags = regions.SelectMany(r => r).Distinct().ToList();
        if (tags.Count < 2)
        {
            return;
        }

        var links = frame.Moves.Links;
        for (var i = 1; i < tags.Count; i++)
        {
            links = links.Add((tags[0], tags[i]));
        }

        frame.Moves = frame.Moves.With(links: links);
    }

    /// <summary>Every tag the links reach from <paramref name="tags"/>.</summary>
    private ImmutableHashSet<int> Component(ImmutableHashSet<int> tags)
    {
        var reached = tags.ToHashSet();
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (a, b) in frame.Moves.Links)
            {
                if (reached.Contains(a) != reached.Contains(b))
                {
                    reached.Add(a);
                    reached.Add(b);
                    grew = true;
                }
            }
        }

        return [.. reached];
    }

    /// <summary>
    /// The variable holds the value from here on: a region of its own, which the value's joins, and no move ends it (an assignment makes a
    /// variable live again). The node's tag names this value alone: every value an earlier round of a loop made at the node keeps its
    /// links and its holders under the node's second tag, so that a round's value is new, as a variable declared in the body is (U12,
    /// section 6, assumed), and what holds an earlier one stays linked.
    /// </summary>
    private void BindRegion(VariableSymbol variable, Expression? value, object node)
    {
        if (!JoinsRegion(variable.Type))
        {
            frame.Moves = frame.Moves.With(tags: frame.Moves.Tags.SetItem(variable, []), moved: frame.Moves.Moved.Remove(variable));
            return;
        }

        var made = value is null ? [] : RegionOf(value);
        var tag = RegionTag(node);
        var state = frame.Moves;
        if (state.Links.Any(l => l.A == tag || l.B == tag) || state.Tags.Values.Any(t => t.Contains(tag)) || made.Contains(tag))
        {
            var earlier = EarlierTag(tag);
            ImmutableHashSet<int> Renamed(ImmutableHashSet<int> tags) => tags.Remove(tag) is var rest && rest.Count != tags.Count ? rest.Add(earlier) : tags;
            int Moved(int t) => t == tag ? earlier : t;
            state = state.With(
                tags: state.Tags.ToImmutableDictionary(p => p.Key, p => Renamed(p.Value), state.Tags.KeyComparer),
                links: state.Links.Select(l => (Moved(l.A), Moved(l.B))).Where(l => l.Item1 != l.Item2).ToImmutableHashSet());
            made = Renamed(made);
        }

        frame.Moves = state.With(tags: state.Tags.SetItem(variable, made.Add(tag)), moved: state.Moved.Remove(variable));
    }

    /// <summary>An assignment to a field or an item of <paramref name="target"/>: its region and the value's merge.</summary>
    private void MergeAssigned(Expression target, Expression value) => Merge([RegionOf(target), RegionOf(value)]);

    /// <summary>A variable whose block ends: nothing holds it any more, but the block's last expression, which keeps its tags.</summary>
    private void EndRegions(Scope scope)
    {
        foreach (var variable in scope.Variables.Values)
        {
            if (frame.Moves.Tags.TryGetValue(variable, out var tags))
            {
                endedTags[variable] = endedTags.TryGetValue(variable, out var before) ? before.Union(tags) : tags; // each round of a loop
            }
        }

        frame.Moves = frame.Moves.Without(scope.Variables.Values);
    }

    /// <summary>
    /// Q1 of comment 6896192351: a read of a variable a <c>move</c> may have ended, on any path, is <c>used-after-move</c> at the read,
    /// naming the line of each such <c>move</c>; a read inside the moved value itself is that <c>move</c>'s own, unless the checker read
    /// the <c>move</c> in an earlier round of a loop, which is a use after it.
    /// </summary>
    private void UseOfMoved(Expression read, VariableSymbol variable, int line)
    {
        if (!frame.Moves.Moved.TryGetValue(variable, out var sites))
        {
            return;
        }

        var reaching = sites.Where(s => s.Earlier || !Within(read, s.Value)).OrderBy(s => s.Line).ThenBy(s => s.Earlier).ToList();
        if (reaching.Count == 0)
        {
            return;
        }

        var moves = string.Join(" or ", reaching.Select(s => $"`move {s.Text}` at line {s.Line}{(s.Earlier ? " in an earlier round of the loop" : "")}"));
        var loops = reaching.Select(s => TakingLoop(s.Value, variable)).ToList();
        var taking = loops.Distinct().ToList(); // two loops over one list on two branches both reach a read after them
        var through = reaching.All(s => s.Text == variable.Name) ? "" : loops.All(l => l is not null)
            ? taking.Count > 1
                ? $", which ended the list the loops at line {string.Join(" and line ", taking)} take"
                : $", which ended the list the loop at line {taking[0]} takes"
            : $", which ended every path into the value `{variable.Name}` reaches";
        var diagnostic = Diagnostic.Error(ErrorIds.UsedAfterMove, line, $"`{variable.Name}` is used after {moves}{through}; `copy` in place of `move` keeps `{variable.Name}`, or `{variable.Name}` is assigned again before this use (U12)");
        Add(diagnostic);
        movedUses.Add((diagnostic, variable, string.Join(",", reaching.Select(s => s.Line).Distinct())));
    }

    /// <summary>Whether <paramref name="read"/> is <paramref name="value"/> or one of its parts.</summary>
    private bool Within(Expression read, Expression value) =>
        ReferenceEquals(read, value) || (value is CallExpression c ? c.Arguments.Select(a => a.Value).Prepend(c.Callee) : Parts(value)).Any(p => Within(read, p));

    /// <summary>E1: one <c>used-after-move</c> for each variable and the moves it names, the first in the file.</summary>
    private void KeepOneUseAfterMove()
    {
        foreach (var group in movedUses.Where(u => diagnostics.Items.Any(d => ReferenceEquals(d, u.Diagnostic))).GroupBy(u => (u.Variable, u.Moves)))
        {
            foreach (var later in group.OrderBy(u => u.Diagnostic.Line).Skip(1))
            {
                diagnostics.Remove(later.Diagnostic);
            }
        }
    }

    /// <summary>The text of a moved value in an error: its name, a path of fields from it, else <c>...</c>.</summary>
    private static string MovedText(Expression e) => Unwrap(e) switch
    {
        NameExpression n => n.Name,
        ThisExpression => "this",
        MemberAccess m when MovedText(m.Target) is var t && t != "..." => $"{t}.{m.Member}",
        IndexExpression i when MovedText(i.Target) is var t && t != "..." => $"{t}[...]",
        _ => "...",
    };

    /// <summary>
    /// The paths a <c>move</c> before the argument ends: the variable it names, and the tags of every region its value reaches, none for
    /// a value that crosses with no word (Q2 (a) of comment 6897778812); null where the region outside the function reaches it, after
    /// <paramref name="reachable"/> reported it.
    /// </summary>
    private (VariableSymbol? Variable, ImmutableHashSet<int> Ends)? MoveEnds(Argument a, Action<string> reachable)
    {
        var value = Unwrap(a.Value);
        var variable = value is NameExpression n && model.IsVariable(n) ? model.VariableOf(n) : null;
        if (!model.HasType(value) || !JoinsRegion(model.TypeOf(value).Type))
        {
            // Q2 (a): the variable it names alone ends, and a value the function did not make stays `move-reachable`
            var (root, outside) = RootOf(value);
            if (outside)
            {
                reachable(root is null ? $"`{MovedText(value)}` is a field of `this` or a static" : $"`{root.Name}` is {OutsideText(root)}");
                return null;
            }

            // Q1 (b): the variable of a loop ends with the list the loop takes, which a list held outside the function keeps
            // `move-reachable`, as for an item that joins a region (the owner, 2026-11-17, #561, comment 6945857315, Q1)
            if (variable is not null && model.LoopOf(variable) is { } loop)
            {
                if (model.HasType(loop.Collection) && model.TypeOf(loop.Collection).Type is ListType && RootOf(loop.Collection) is { Outside: true } held)
                {
                    reachable($"the list the loop takes, {(held.Root is null ? $"`{MovedText(loop.Collection)}`, is a field of `this` or a static" : $"`{held.Root.Name}`, is {OutsideText(held.Root)}")}");
                    return null;
                }

                if (!TakesItsList(loop, a.Line))
                {
                    return null;
                }
            }

            return (variable, []);
        }

        var ends = Component(RegionOf(value));
        if (ends.Contains(0))
        {
            var held = variable is not null && OutsideVariable(variable)
                ? $"`{variable.Name}` is {OutsideText(variable)}"
                : $"`{MovedText(value)}` reaches into a value held outside the function, through a parameter not written `move`, `this`, a captured variable or a static";
            reachable($"{held}, which the function's caller still holds");
            return null;
        }

        if (ends.Contains(Unkept.Single()))
        {
            NotSupported(a.Line, $"`move {MovedText(value)}` of a value whose region the checker does not keep (U12)");
            return null;
        }

        if (variable is not null && model.LoopOf(variable) is { } f && !TakesItsList(f, a.Line))
        {
            return null;
        }

        return (variable, ends);
    }

    /// <summary>
    /// Answer 1 of comment 6897778812: a loop whose body moves its variable takes its list; a loop over anything else, which holds no item it
    /// could give up, either of a loop's two names among them, is <c>not supported</c>, and so is a loop over a list whose item type holds a
    /// type parameter, whose list the build takes by the item type it is emitted for.
    /// </summary>
    private bool TakesItsList(ForInStatement f, int line)
    {
        if (f.Second is not null || !model.HasType(f.Collection) || model.TypeOf(f.Collection).Type is not ListType list)
        {
            NotSupported(line, "`move` of the variable of a loop over anything but a list, which the loop would take (U12)");
            return false;
        }

        if (frame.TypeParameters.Any(p => Types.Mentions(list.Element, p)))
        {
            NotSupported(line, $"`move` of the variable of a loop over a list of `{list.Element}`, a type parameter's, which the loop would take (U12)");
            return false;
        }

        return true;
    }

    /// <summary>The variable the list starts from that the loop of <paramref name="variable"/> takes, where the item joins no region that reaches it (the owner, 2026-11-17, #561, comment 6945857315, Q1).</summary>
    private VariableSymbol? TakenList(VariableSymbol? variable) =>
        variable is not null && !JoinsRegion(variable.Type) && model.LoopOf(variable) is { } loop ? RootOf(loop.Collection).Root : null;

    /// <summary>
    /// The line of the loop whose variable a <c>move</c> of <paramref name="moved"/> names, where that loop takes <paramref name="list"/>
    /// for an item that crosses with no word (<see cref="TakenList"/>), so that an error names the loop that ended the list; else null.
    /// </summary>
    private int? TakingLoop(Expression moved, VariableSymbol list) =>
        Unwrap(moved) is NameExpression n && model.IsVariable(n) && model.VariableOf(n) is var v && ReferenceEquals(TakenList(v), list) && model.LoopOf(v) is { } loop
            ? loop.Line
            : null;

    /// <summary>The variable a path of fields or items starts from, and whether it starts outside the function: at a parameter not written <c>move</c>, <c>this</c>, a field of it, a static or a captured variable.</summary>
    private (VariableSymbol? Root, bool Outside) RootOf(Expression e) => Unwrap(e) switch
    {
        NameExpression n when model.IsVariable(n) => (model.VariableOf(n), OutsideVariable(model.VariableOf(n))),
        MemberAccess m when model.HasType(m.Target) => RootOf(m.Target),
        IndexExpression i => RootOf(i.Target),
        NameExpression or ThisExpression or MemberAccess => (null, true),
        _ => (null, false),
    };

    private static string OutsideText(VariableSymbol variable) => variable.Kind switch
    {
        VariableKind.Static => "a static",
        VariableKind.Parameter when variable.Name == "this" => "`this`",
        VariableKind.Parameter => "a parameter not written `move`",
        _ => "a variable of the code around the lambda",
    };

    /// <summary>
    /// A use, in a later argument of the same call, of what a <c>move</c> before an earlier one ends, <c>s.Take(move c, c.N)</c>, is
    /// <c>used-after-move</c> at the use, since the arguments run in the order they are written (D11) and the error stands at the use
    /// (comment 6896192351, Q1); whether it reported one.
    /// </summary>
    private bool UsedInLaterArgument(Argument a, List<(Argument Argument, VariableSymbol? Variable, ImmutableHashSet<int> Ends)> moves)
    {
        foreach (var (moved, variable, ends) in moves)
        {
            var list = TakenList(variable);
            var read = ReadsOf(a.Value).FirstOrDefault(r => ReferenceEquals(r.Variable, variable) || ReferenceEquals(r.Variable, list) || TagsOf(r.Variable).Overlaps(ends));
            if (read.Variable is not null)
            {
                var through = ReferenceEquals(read.Variable, variable) ? "" : TakingLoop(moved.Value, read.Variable) is { } loop
                    ? $", which ended the list the loop at line {loop} takes"
                    : $", which ended every path into the value `{read.Variable.Name}` reaches";
                var diagnostic = Diagnostic.Error(ErrorIds.UsedAfterMove, read.Read.Line, $"`{read.Variable.Name}` is used after `move {MovedText(moved.Value)}` at line {moved.Line}, an argument before it in the same call{through}; `copy` in place of `move` keeps `{read.Variable.Name}`, or this argument is read into a variable before the call (U12, D11)");
                Add(diagnostic);
                movedUses.Add((diagnostic, read.Variable, moved.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return true;
            }
        }

        return false;
    }

    /// <summary>The variables an argument reads, through its parts and the lambdas it makes.</summary>
    private IEnumerable<(VariableSymbol Variable, Expression Read)> ReadsOf(Expression e)
    {
        if (e is NameExpression n && model.IsVariable(n))
        {
            yield return (model.VariableOf(n), n);
        }

        if (e is LambdaExpression l && model.HasLambda(l))
        {
            foreach (var v in model.LambdaOf(l).Captures)
            {
                yield return (v, l);
            }
        }

        var parts = e is CallExpression c ? c.Arguments.Select(x => x.Value).Prepend(c.Callee).Concat(c.Clause()) : Parts(e);
        foreach (var part in parts)
        {
            foreach (var read in ReadsOf(part))
            {
                yield return read;
            }
        }
    }

    /// <summary>The moves of a call whose words passed: each ends its variable and every variable its region reaches, recorded for the code generator.</summary>
    private void EndMoved(List<(Argument Argument, VariableSymbol? Variable, ImmutableHashSet<int> Ends)> moves)
    {
        foreach (var (a, variable, ends) in moves)
        {
            var site = new MoveSite(a.Line, MovedText(a.Value), a.Value, false);
            var moved = frame.Moves.Moved;
            var list = TakenList(variable);
            foreach (var (v, tags) in frame.Moves.Tags)
            {
                if (ReferenceEquals(v, variable) || ReferenceEquals(v, list) || tags.Overlaps(ends))
                {
                    moved = moved.SetItem(v, moved.TryGetValue(v, out var sites) ? sites.Add(site) : [site]);
                }
            }

            frame.Moves = frame.Moves.With(moved: moved);
            model.RecordMove(a.Value, variable);
        }
    }

    /// <summary>Whether the parameter that argument <paramref name="i"/> of the call is given to is written <c>move</c>.</summary>
    private bool MoveSlot(CallExpression c, CallInfo info, int i) => i < info.Binding.Count && info.Binding[i] is var slot && slot >= 0 && info.Kind switch
    {
        CallKind.Value => model.HasType(c.Callee) && model.TypeOf(c.Callee).Type is FunctionType f && f.MovesAt(slot),
        _ => info.Function is { } function && slot < function.Parameters.Count && function.Parameters[slot].Move,
    };

    /// <summary>
    /// U12, section 6: the words of a plain call's arguments, by argument order: <c>move</c> before an argument whose parameter is not
    /// written <c>move</c>, and no word before one whose parameter is, are <c>move-at-call</c> (2026-11-05, Q3); <c>copy</c> fits a
    /// parameter written <c>move</c>, since its copy is new (comment 6897778812, Q3 (a)); a <c>move</c> of what the region outside the
    /// function reaches is <c>move-reachable</c>; a later argument that reads what a <c>move</c> ended is <c>used-after-move</c>. Then
    /// the call merges the regions of the rest; whether it reported one.
    /// </summary>
    private bool PlainCallMoves(CallExpression c, CallInfo info, string callee)
    {
        var moves = new List<(Argument Argument, VariableSymbol? Variable, ImmutableHashSet<int> Ends)>();
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var a = c.Arguments[i];
            if (UsedInLaterArgument(a, moves))
            {
                return true;
            }

            var moveParameter = MoveSlot(c, info, i);
            if (a.Word == ArgumentWord.Move && !moveParameter)
            {
                Report(ErrorIds.MoveAtCall, a.Line, $"{ArgumentText(a, i)} of {callee} is written `move`, whose parameter is not; at a call that is no message, `move` stands before an argument of a parameter written `move` alone (U12)");
                return true;
            }

            if (a.Word == ArgumentWord.None && moveParameter)
            {
                Report(ErrorIds.MoveAtCall, a.Line, $"{ArgumentText(a, i)} of {callee} is given to a parameter written `move`, and is written `move` too, or `copy` for a copy (U12)");
                return true;
            }

            if (a.Word == ArgumentWord.Move)
            {
                if (MoveEnds(a, why => Report(ErrorIds.MoveReachable, a.Line, $"`move {MovedText(a.Value)}` cannot end what it sends: {why}; `copy` in place of `move` gives the parameter a copy (U12)")) is not { } ends)
                {
                    return true;
                }

                moves.Add((a, ends.Variable, ends.Ends));
            }
        }

        Merge(CallRegions(c));
        EndMoved(moves);
        return false;
    }

    /// <summary>
    /// U12, U16: the words of a message's or a <c>spawn</c>'s argument after its <c>send-needs-word</c>, in its place among the arguments:
    /// no word before a parameter written <c>move</c> is <c>move-at-call</c>; <c>move</c> is the send's word however the parameter is
    /// written (section 6, the reading of comment 6668638888), which ends what it sends, <c>move-reachable</c> where the region outside
    /// the function reaches it (the owner, 2026-11-03, issue 156, comment 6603205575; comment 6897778812, Q1 and Q2, for a list a loop takes
    /// and a value that crosses with no word, and 2026-11-17, #561, comment 6945857315, Q1, for the list a loop takes of such an item); a
    /// later argument that reads what it ended is <c>used-after-move</c>.
    /// Whether it reported one.
    /// </summary>
    private bool MessageMove(CallExpression c, int i, bool moveParameter, List<(Argument Argument, VariableSymbol? Variable, ImmutableHashSet<int> Ends)> moves)
    {
        var a = c.Arguments[i];
        if (UsedInLaterArgument(a, moves))
        {
            return true;
        }

        if (a.Word == ArgumentWord.None && moveParameter)
        {
            Report(ErrorIds.MoveAtCall, a.Line, $"{ArgumentText(a, i)} is given to a parameter written `move`, and is written `move` too, or `copy` for a copy (U12)");
            return true;
        }

        if (a.Word != ArgumentWord.Move)
        {
            return false;
        }

        if (MoveEnds(a, why => Report(ErrorIds.MoveReachable, a.Line, $"`move {MovedText(a.Value)}` cannot end what it sends: {why}; `copy` sends a copy (U12)")) is not { } ends)
        {
            return true;
        }

        moves.Add((a, ends.Variable, ends.Ends));
        return false;
    }
}
