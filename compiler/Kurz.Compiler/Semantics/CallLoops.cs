using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Section 6, "Deadlocks", layer 2: the compile error <c>call-loop</c> for loops of calls that wait between actor types (U56), with the
/// call back to the caller that makes no edge (U57), the word <c>vouch</c> that takes its call's edge out (U58) and the lint
/// <c>vouch-unneeded</c> (U59) (the owner, 2026-11-20, #519, comment 7051071630: T40 to T62 and T74, T45 and T47 naming constructs the
/// build does not read, an actor behind an interface, a generic one and a <c>per</c> one staying <c>not supported</c>). The graph's points
/// are the entries of the actor types, the construction and every method each declares; an edge goes from an entry to a method of an actor
/// for each call that waits on it that the entry's code reaches, with what it runs, as the walk of the throw sets reads it
/// (<see cref="ThrowSets.ActorsCode"/>, the one definition of what code reaches), and from an entry to the construction of an actor for each
/// <c>spawn</c> of it the code reaches. A loop is a path of one edge or more from an entry that ends in a call that waits on a method of
/// the entry's type, another instance of it where the path is one edge long (T40 (c), T48 (a)); a path that comes back to the type by a
/// <c>spawn</c> alone reaches a new actor and is none (T43 (a)). The entries on loops that reach one another's types are one group, the
/// loops of one type among them: one error for each group, at its call that stands first in source order, naming the shortest loop
/// through that call (T52 (a)). Which loops pass through each call is read by reachability alone and no loop is listed one by one: the entries' groups
/// that reach one another (Tarjan's strongly connected components, by a depth-first walk with a stack of its own) carry the types each
/// reaches the end of a loop of and the types of the entries that reach it, as sets of bits, so that the cost grows with the edges times
/// the types and never with the count of paths; the reach of the units and the callbacks are read to their fixed points, each round over
/// every unit, every method or every field, so that a reference handed on through a chain of methods takes a round for each of them (T59 (a)).
/// </summary>
public sealed class CallLoops
{
    private readonly ThrowSets sets;
    private readonly List<Point> points = [];
    private readonly int types;
    private readonly List<Hop> hops = [];
    private readonly List<ActorSite> sites;
    private readonly ILookup<FunctionSymbol?, ActorSite> calls;
    private readonly Dictionary<ActorEntry, bool[]> callbacks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FieldSymbol, bool> fields = new(ReferenceEqualityComparer.Instance);

    /// <summary>The loops of the code of <paramref name="program"/>, which sees the types of <paramref name="std"/> (W1), a program that may have reported errors.</summary>
    public CallLoops(Checker program, Checker? std)
    {
        ArgumentNullException.ThrowIfNull(program);
        sets = new ThrowSets(program, std, reach: true);
        var code = sets.ActorsCode();
        var methods = new Dictionary<FunctionSymbol, int>(ReferenceEqualityComparer.Instance);
        var constructions = new Dictionary<ClassSymbol, int>(ReferenceEqualityComparer.Instance);
        for (var type = 0; type < code.Count; type++)
        {
            foreach (var entry in code[type].Entries)
            {
                if (entry.Method is { } method)
                {
                    methods[method] = points.Count;
                }
                else
                {
                    constructions.TryAdd(code[type].Actor, points.Count);
                }

                points.Add(new Point(code[type].Actor, type, entry));
            }
        }

        types = code.Count;
        sites = sets.Sites().ToList();
        calls = sites.ToLookup(s => s.Method, ReferenceEqualityComparer.Instance as IEqualityComparer<FunctionSymbol?>);
        Callbacks(code, sets.Outside(), sets.Writes());
        for (var from = 0; from < points.Count; from++)
        {
            var entry = points[from].Entry;
            foreach (var reached in entry.Sites.Where(r => r.Site.Kind != SiteKind.Send).OrderBy(r => (r.Site.File, r.Site.Line)))
            {
                // U57: a call that waits through the actor itself, or through a parameter or a field that is a callback, reaches the caller
                // that waits, which takes it as a call of its chain (U17): no edge
                if (reached.Site.Kind == SiteKind.Wait && reached.Receivers.All(o => Callback(entry, o)))
                {
                    continue;
                }

                // a call of an actor whose code the build does not read reaches no entry: no edge
                var to = reached.Site.Kind == SiteKind.Wait ? methods.GetValueOrDefault(reached.Site.Method!, -1) : constructions.GetValueOrDefault(reached.Site.Actor, -1);
                if (to >= 0)
                {
                    hops.Add(new Hop(from, to, entry, reached));
                }
            }
        }
    }

    /// <summary>The count of the units of code the walk read, each once (<see cref="ThrowSets.Walked"/>).</summary>
    public int Walked => sets.Walked;

    /// <summary>
    /// U56: one <c>call-loop</c> for each group of loops, a call after <c>vouch</c> making no edge (U58), at the call of the group that
    /// stands first in source order, the program's file first, then by line, of the calls that wait on a loop of the group, naming the
    /// shortest loop through it, each hop with its method and its line, as E1 puts <c>reference-cycle</c> at the first declaration of the
    /// cycle.
    /// </summary>
    public IReadOnlyList<Diagnostic> Errors()
    {
        var graph = hops.Where(h => !(h.Wait && h.Reached.Site.Vouch > 0)).ToList();
        var (groups, through) = Loops(graph);
        var errors = new List<(ActorSite Site, Diagnostic Error)>();
        foreach (var first in Enumerable.Range(0, graph.Count).Where(i => groups[i] >= 0 && graph[i].Wait).GroupBy(i => groups[i])
            .Select(g => g.OrderBy(i => (graph[i].Reached.Site.File, graph[i].Reached.Site.Line)).First()))
        {
            var loop = Shortest(graph, first, through[first]);
            errors.Add((graph[first].Reached.Site, Diagnostic.Error(ErrorIds.CallLoop, graph[first].Reached.Site.Line, Text(loop))));
        }

        return errors.OrderBy(e => (e.Site.File, e.Site.Line)).Select(e => e.Error).ToList();
    }

    /// <summary>
    /// U59: <c>vouch-unneeded</c> at each <c>vouch</c> of the program's file whose call is on no loop of U56's graph read with every
    /// <c>vouch</c> left out, so that two on one loop keep each other, or whose call U57 takes out: a warning, which stops no build, as
    /// rustc warns of an <c>unsafe</c> block that holds no unsafe operation; the caller gives it beside the program's other errors and
    /// none beside a <c>not supported</c>, since an unread construct can hide an edge.
    /// </summary>
    public IReadOnlyList<Diagnostic> Warnings()
    {
        var (groups, _) = Loops(hops);
        var covering = new HashSet<CallExpression>(Enumerable.Range(0, hops.Count).Where(i => groups[i] >= 0 && hops[i].Wait && hops[i].Reached.Site.Vouch > 0)
            .Select(i => hops[i].Reached.Site.Call), ReferenceEqualityComparer.Instance);
        var warned = new HashSet<CallExpression>(ReferenceEqualityComparer.Instance);
        return sites
            .Where(s => s.Kind == SiteKind.Wait && s.Vouch > 0 && s.File == 0 && !covering.Contains(s.Call) && warned.Add(s.Call))
            .OrderBy(s => s.Vouch)
            .Select(s => Diagnostic.Warning(LintIds.VouchUnneeded, s.Vouch,
                $"`vouch` before the call of `{s.Actor.Name}.{s.Method!.Name}` covers no loop of calls that wait between actor types, read with every `vouch` left out: take the word out (U59, U56, U57)"))
            .ToList();
    }

    /// <summary>
    /// U57: which parameters of the methods of the actors are callbacks, and which fields of their bodies. A parameter is one where calls
    /// through a reference reach the method, every one of them waits, stands in code that the actors' messages alone run, which neither the
    /// top-level code nor a root no actor's code reaches runs (<see cref="ThrowSets.Outside"/>, T74 (b)), and fills the parameter, in the
    /// terms of each entry that reaches it, with the entry's own reference, as the call writes it, or with a parameter or a field of the entry
    /// that is a callback, so that a reference handed on through a chain of calls that wait reaches the caller that waits at its start (T60
    /// (b)); or none does and the actor's own code calls the method directly (U10), whose entries read its calls with the arguments they
    /// pass. A field of an actor's body is one where every write of it, and for a list every item put into it, stands in code an entry
    /// reaches and takes, in the terms of each entry that reaches it, a value that is a callback (T49 (c)), a field with no such write among
    /// them; a field of its primary constructor, which its <c>spawn</c> sets, is none (the writes, assumed: a reading of T49 (c) of comment
    /// 7051071630). It is the least fixed point, read over the parameters and the fields together: every one starts as no callback, and
    /// the rounds read each again as long as one changes, so that a cycle of parameters that no <c>this</c> fills stays none, and so does one that a <c>this</c> enters (T62 (a)). The walk keeps no context per
    /// call, so one caller that passes another reference makes the parameter's calls edges for every caller, and a method that nothing calls
    /// has no caller to call back, so that its loop is refused as a loop no code reaches is (T53).
    /// </summary>
    private void Callbacks(IReadOnlyList<ActorCode> code, IReadOnlySet<ActorSite> outside, IReadOnlyList<FieldWrite> writes)
    {
        var entries = code.SelectMany(c => c.Entries).ToList();
        var reaching = entries.SelectMany(e => e.Sites.Select(r => (Entry: e, Reached: r)))
            .ToLookup(p => p.Reached.Site, ReferenceEqualityComparer.Instance as IEqualityComparer<ActorSite>);
        var written = entries.SelectMany(e => e.Writes.Select(w => (Entry: e, Reached: w)))
            .ToLookup(p => p.Reached.Write, ReferenceEqualityComparer.Instance as IEqualityComparer<FieldWrite>);
        var byField = writes.ToLookup(w => w.Field, ReferenceEqualityComparer.Instance as IEqualityComparer<FieldSymbol>);
        var methods = entries.Where(e => e.Method is not null).ToList();
        foreach (var entry in methods)
        {
            callbacks[entry] = new bool[entry.Method!.Parameters.Count];
        }

        foreach (var field in code.SelectMany(c => c.Actor.Fields))
        {
            fields[field] = false;
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var entry in methods)
            {
                var callers = calls[entry.Method].ToList();
                var each = callbacks[entry];
                for (var i = 0; i < each.Length; i++)
                {
                    if ((callers.Count > 0 ? callers.All(s => Fills(s, i)) : entry.Direct) != each[i])
                    {
                        each[i] = !each[i];
                        changed = true;
                    }
                }
            }

            foreach (var field in fields.Keys.ToList())
            {
                // T49 (c): a field of the actor's body, every write of which stands in code an entry reaches, and each entry that reaches it gives
                // it a callback; a field of the primary constructor, which its `spawn` sets, is none
                if ((field.Body is not null && byField[field].All(w => written[w].Any() && written[w].All(r => r.Reached.Values.All(o => Callback(r.Entry, o))))) != fields[field])
                {
                    fields[field] = !fields[field];
                    changed = true;
                }
            }
        }

        // a caller that waits in the code of entries alone, each of which fills the parameter with its own reference or a callback of its own
        bool Fills(ActorSite caller, int parameter) =>
            caller.Kind == SiteKind.Wait && !outside.Contains(caller) && reaching[caller].Any()
            && reaching[caller].All(r => parameter < r.Reached.Arguments.Count && r.Reached.Arguments[parameter].All(o => Callback(r.Entry, o)));
    }

    // U57: whether a reference of that origin in the entry's terms reaches the caller that waits: the actor itself, or a parameter of the
    // entry's method or a field of its actor, or an item of the list such a field holds, that the fixed point of Callbacks made one
    private bool Callback(ActorEntry entry, Origin origin) =>
        origin == Origin.Self
        || (origin.Parameter >= 0 && callbacks.TryGetValue(entry, out var each) && origin.Parameter < each.Length && each[origin.Parameter])
        || (origin.Field is { } field && fields.GetValueOrDefault(field));

    /// <summary>
    /// T40 (c), T52 (a): for each hop of the graph, the group of the loops it is on, -1 for none, and the types of the loops it is on, as
    /// bits: a hop from <c>X</c> to <c>Y</c> is on a loop that comes back to the type <c>A</c> where an entry of <c>A</c> reaches <c>X</c>
    /// by none or more hops, and <c>Y</c> reaches by none or more hops a call that waits on a method of <c>A</c>, or the hop is that call.
    /// Each group of entries that reach one another (Tarjan's) carries, as bits, the types of the calls that wait that it reaches, read from
    /// the groups it reaches, and the types of the entries that reach it, read from the groups that reach it. An entry is on a loop where it
    /// reaches a call that waits on a method of its own type, and the entries on loops that reach one another's types are one group: two
    /// types are of one group where an entry on a loop of each reaches a call that waits on a method of the other, and so are the types
    /// that a chain of such pairs joins, so that every loop of a type is of its type's group and a hop on loops of two types joins them.
    /// </summary>
    private (int[] Groups, ulong[][] Through) Loops(IReadOnlyList<Hop> graph)
    {
        var words = (types + 63) / 64;
        var next = Next(graph);
        var (component, order) = Components(next, graph);
        var ends = new ulong[order.Count][];
        var starts = new ulong[order.Count][];
        for (var c = 0; c < order.Count; c++)
        {
            ends[c] = new ulong[words];
            starts[c] = new ulong[words];
        }

        // the groups come out of the walk with every group they reach before them: the ends a group reaches, its own calls that wait and those
        // of the groups after it
        for (var c = 0; c < order.Count; c++)
        {
            foreach (var hop in order[c].SelectMany(p => next[p]).Select(i => graph[i]))
            {
                if (hop.Wait)
                {
                    Set(ends[c], points[hop.To].Type);
                }

                if (component[hop.To] != c)
                {
                    Join(ends[c], ends[component[hop.To]]);
                }
            }
        }

        // the types of the entries that reach a group, its own and those of the groups before it, read from the last group out to the first
        for (var c = order.Count - 1; c >= 0; c--)
        {
            foreach (var p in order[c])
            {
                Set(starts[c], points[p].Type);
            }

            foreach (var hop in order[c].SelectMany(p => next[p]).Select(i => graph[i]).Where(h => component[h.To] != c))
            {
                Join(starts[component[hop.To]], starts[c]);
            }
        }

        var through = new ulong[graph.Count][];
        for (var i = 0; i < graph.Count; i++)
        {
            var hop = graph[i];
            var back = (ulong[])ends[component[hop.To]].Clone();
            if (hop.Wait)
            {
                Set(back, points[hop.To].Type);
            }

            through[i] = starts[component[hop.From]].Zip(back, (a, b) => a & b).ToArray();
        }

        // the types the entries on a loop of each type reach the end of a loop of; two types whose entries on loops each reach the other's
        // are one group
        var reach = Enumerable.Range(0, types).Select(_ => new ulong[words]).ToArray();
        for (var p = 0; p < points.Count; p++)
        {
            if (Has(ends[component[p]], points[p].Type))
            {
                Join(reach[points[p].Type], ends[component[p]]);
            }
        }

        var parent = Enumerable.Range(0, types).ToArray();
        for (var type = 0; type < types; type++)
        {
            foreach (var other in Bits(reach[type]).Where(other => Has(reach[other], type)))
            {
                parent[Find(parent, other)] = Find(parent, type);
            }
        }

        var groups = through.Select(on => Bits(on).Select(bit => Find(parent, bit)).DefaultIfEmpty(-1).First()).ToArray();
        return (groups, through);

        static int Find(int[] parent, int at)
        {
            while (parent[at] != at)
            {
                at = parent[at] = parent[parent[at]];
            }

            return at;
        }
    }

    // the hops out of each entry, by their place in the graph, in the order of the graph: an entry's construction first, then its methods,
    // each by the order of its sites
    private List<int>[] Next(IReadOnlyList<Hop> graph)
    {
        var next = Enumerable.Range(0, points.Count).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < graph.Count; i++)
        {
            next[graph[i].From].Add(i);
        }

        return next;
    }

    // Tarjan's strongly connected components of the entries under the hops: each entry's group, and the groups in the order the walk
    // closes them, each after every group it reaches
    private (int[] Component, List<List<int>> Order) Components(List<int>[] next, IReadOnlyList<Hop> graph)
    {
        var index = Enumerable.Repeat(-1, points.Count).ToArray();
        var low = new int[points.Count];
        var component = Enumerable.Repeat(-1, points.Count).ToArray();
        var order = new List<List<int>>();
        var stack = new Stack<int>();
        var counter = 0;
        for (var root = 0; root < points.Count; root++)
        {
            if (index[root] >= 0)
            {
                continue;
            }

            // the walk, with an explicit stack of each entry and the place in its list of hops, so that 2,000 types in a chain take no frame
            // of the process each
            var walk = new Stack<(int Point, int Next)>();
            Enter(root);
            while (walk.TryPop(out var top))
            {
                if (top.Next < next[top.Point].Count)
                {
                    walk.Push((top.Point, top.Next + 1));
                    var to = graph[next[top.Point][top.Next]].To;
                    if (index[to] < 0)
                    {
                        Enter(to);
                    }
                    else if (component[to] < 0)
                    {
                        low[top.Point] = Math.Min(low[top.Point], index[to]); // an entry on the stack, of the group being walked
                    }

                    continue;
                }

                if (low[top.Point] == index[top.Point])
                {
                    var members = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        component[member] = order.Count;
                        members.Add(member);
                    }
                    while (member != top.Point);
                    order.Add(members);
                }

                if (walk.TryPeek(out var parent))
                {
                    low[parent.Point] = Math.Min(low[parent.Point], low[top.Point]);
                }
            }

            void Enter(int point)
            {
                index[point] = low[point] = counter++;
                stack.Push(point);
                walk.Push((point, 0));
            }
        }

        return (component, order);
    }

    /// <summary>
    /// T52 (a): the shortest loop through the hop <paramref name="at"/>: of the types its loops come back to, <paramref name="through"/>,
    /// the loop of the fewest hops from an entry of the type, through the hop, to a call that waits on a method of it, the first type in the
    /// order the actors are declared where two loops have as many hops; from the entry of that type that reaches the hop in the fewest hops,
    /// the first declared, along the walk that takes at each entry its hops in the order its code reaches them, and from the hop to the first
    /// such call that walk meets.
    /// </summary>
    private List<Hop> Shortest(IReadOnlyList<Hop> graph, int at, ulong[] through)
    {
        var next = Next(graph);
        var hop = graph[at];
        var before = Distances(graph, hop.From);
        var (after, came, met) = Walk(next, graph, hop.To);

        // of each type, the entry that reaches the hop in the fewest hops, the first declared, and the first call that waits on a method of
        // it that the walk from the hop meets
        var start = Enumerable.Repeat(-1, types).ToArray();
        for (var p = 0; p < points.Count; p++)
        {
            if (before[p] >= 0 && (start[points[p].Type] < 0 || before[p] < before[start[points[p].Type]]))
            {
                start[points[p].Type] = p;
            }
        }

        var close = Enumerable.Repeat(-1, types).ToArray();
        foreach (var i in met.SelectMany(p => next[p]).Where(i => graph[i].Wait))
        {
            if (close[points[graph[i].To].Type] < 0)
            {
                close[points[graph[i].To].Type] = i;
            }
        }

        (int Length, int Start, int Close)? best = null;
        foreach (var type in Bits(through))
        {
            // the call that closes the loop: the hop itself, or the first call that waits on a method of the type that the walk meets
            var closing = hop.Wait && points[hop.To].Type == type ? -2 : close[type];
            if (closing == -1 || start[type] < 0)
            {
                continue;
            }

            var length = before[start[type]] + 1 + (closing == -2 ? 0 : after[graph[closing].From] + 1);
            if (best is null || length < best.Value.Length)
            {
                best = (length, start[type], closing);
            }
        }

        var (_, from, end) = best ?? throw new InvalidOperationException($"the hop at line {hop.Reached.Site.Line} is on no loop");
        var path = Path(Walk(next, graph, from).Came, graph, from, hop.From);
        path.Add(hop);
        if (end >= 0)
        {
            path.AddRange(Path(came, graph, hop.To, graph[end].From));
            path.Add(graph[end]);
        }

        return path;
    }

    // the fewest hops from each entry to the entry `to`, -1 where it reaches none, by a walk of the hops backwards
    private int[] Distances(IReadOnlyList<Hop> graph, int to)
    {
        var into = Enumerable.Range(0, points.Count).Select(_ => new List<int>()).ToArray();
        foreach (var hop in graph)
        {
            into[hop.To].Add(hop.From);
        }

        var distance = Enumerable.Repeat(-1, points.Count).ToArray();
        distance[to] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(to);
        while (queue.TryDequeue(out var at))
        {
            foreach (var from in into[at].Where(p => distance[p] < 0))
            {
                distance[from] = distance[at] + 1;
                queue.Enqueue(from);
            }
        }

        return distance;
    }

    // the breadth-first walk from an entry, each entry's hops in their order: the fewest hops to each entry, -1 where it reaches none, the hop
    // the walk first reached each entry by, and the entries in the order the walk met them
    private (int[] Distance, int[] Came, List<int> Met) Walk(List<int>[] next, IReadOnlyList<Hop> graph, int from)
    {
        var distance = Enumerable.Repeat(-1, points.Count).ToArray();
        var came = Enumerable.Repeat(-1, points.Count).ToArray();
        var met = new List<int> { from };
        distance[from] = 0;
        for (var k = 0; k < met.Count; k++)
        {
            foreach (var i in next[met[k]].Where(i => distance[graph[i].To] < 0))
            {
                distance[graph[i].To] = distance[met[k]] + 1;
                came[graph[i].To] = i;
                met.Add(graph[i].To);
            }
        }

        return (distance, came, met);
    }

    // the hops of the walk from `from` to `to`, by the hop it first reached each entry by
    private static List<Hop> Path(int[] came, IReadOnlyList<Hop> graph, int from, int to)
    {
        var path = new List<Hop>();
        for (var at = to; at != from; at = graph[came[at]].From)
        {
            path.Insert(0, graph[came[at]]);
        }

        return path;
    }

    private static void Set(ulong[] bits, int bit) => bits[bit / 64] |= 1UL << (bit % 64);

    private static bool Has(ulong[] bits, int bit) => (bits[bit / 64] & (1UL << (bit % 64))) != 0;

    private static void Join(ulong[] into, ulong[] from)
    {
        for (var w = 0; w < into.Length; w++)
        {
            into[w] |= from[w];
        }
    }

    private static IEnumerable<int> Bits(ulong[] bits)
    {
        for (var w = 0; w < bits.Length; w++)
        {
            for (var word = bits[w]; word != 0; word &= word - 1)
            {
                yield return (w * 64) + System.Numerics.BitOperations.TrailingZeroCount(word);
            }
        }
    }

    // T52, T55, T56: the hops of the loop, then what two chains in it do, then what to change, `send` or the code first and `vouch` last
    private string Text(IReadOnlyList<Hop> loop)
    {
        var said = string.Join(", and ", loop.Select(h => h.Said(points[h.To].Actor)));
        if (loop is [var only] && points[only.From].Type == points[only.To].Type)
        {
            var one = points[only.To].Actor.Name;
            return $"{said}, a call of another `{one}`: two chains that enter two `{one}`s at once wait on each other; make the call a `send` where no result is needed, or change the code so that its calls do not come back, and put `vouch` before it only where it reaches another `{one}` than the one that waits (U56, U58)";
        }

        return $"{said}: two chains that enter this loop at once wait on each other; make a call of it a `send` where no result is needed, or change the code so that its calls do not come back, and put `vouch` before a call only where it reaches another actor than the one that waits (U56, U58)";
    }

    /// <summary>A point of the graph: an entry of an actor's code, with its actor and the actor's place among the types.</summary>
    private sealed record Point(ClassSymbol Actor, int Type, ActorEntry Entry);

    /// <summary>An edge of the graph: from the entry whose code reaches the site, to the method the site calls or the construction it spawns.</summary>
    private sealed record Hop(int From, int To, ActorEntry Entry, ReachedSite Reached)
    {
        public bool Wait => Reached.Site.Kind == SiteKind.Wait;

        public string Said(ClassSymbol to) => (Wait ? $"{Entry.Name} waits on `{to.Name}.{Reached.Site.Method!.Name}`" : $"{Entry.Name} spawns `{to.Name}`")
            + $" (line {Reached.Site.Line}{(Reached.Through is { } through ? $", in {through}" : "")})";
    }
}
