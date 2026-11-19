using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Section 6, "Deadlocks", layer 2: the compile error <c>call-loop</c> for loops of calls that wait between actor types (U56), with the
/// call back to the caller that makes no edge (U57), the word <c>vouch</c> that takes its call's edge out (U58) and the lint
/// <c>vouch-unneeded</c> (U59) (assumed: T40 to T59 of #519, comment 7007991607, and T60 and T61 of #519, comment 7009683539, built at
/// the lean while it waits for the owner). The graph's points are the actor types; an edge goes from <c>A</c> to <c>B</c> for each call
/// that waits on a method of <c>B</c> that the code of <c>A</c> reaches, its construction and every method it declares with what they
/// run, as the walk of the throw sets reads it (<see cref="ThrowSets.ActorsCode"/>, the one definition of what code reaches), and for
/// each <c>spawn</c> of <c>B</c> it reaches. A group of types that reach one another (a strongly connected component) that holds the
/// edge of a call is one error, at its call that stands first in source order, naming the shortest loop through that call; a group of
/// <c>spawn</c>s alone is none. The walk reads each unit of code once over all the actors, and the groups are found by one depth-first
/// walk of the types (Tarjan's), so that the cost grows with the code and its calls, and no loop is listed one by one.
/// </summary>
public sealed class CallLoops
{
    private readonly ThrowSets sets;
    private readonly List<ClassSymbol> actors;
    private readonly List<Hop> hops = [];
    private readonly List<ActorSite> sites;
    private readonly ILookup<FunctionSymbol?, ActorSite> calls;
    private readonly Dictionary<ActorEntry, bool[]> callbacks = new(ReferenceEqualityComparer.Instance);

    /// <summary>The loops of the code of <paramref name="program"/>, which sees the types of <paramref name="std"/> (W1), a program that may have reported errors.</summary>
    public CallLoops(Checker program, Checker? std)
    {
        ArgumentNullException.ThrowIfNull(program);
        sets = new ThrowSets(program, std, reach: true);
        var code = sets.ActorsCode();
        actors = code.Select(c => c.Actor).ToList();
        sites = sets.Sites().ToList();
        calls = sites.ToLookup(s => s.Method, ReferenceEqualityComparer.Instance as IEqualityComparer<FunctionSymbol?>);
        Callbacks(code, sets.Outside());
        foreach (var (actor, entries) in code)
        {
            foreach (var entry in entries)
            {
                foreach (var reached in entry.Sites.Where(r => r.Site.Kind != SiteKind.Send).OrderBy(r => (r.Site.File, r.Site.Line)))
                {
                    // U57: a call that waits through the actor itself, or through a parameter that is a callback, reaches the caller that
                    // waits, which takes it as a call of its chain (U17): no edge
                    if (reached.Site.Kind == SiteKind.Wait && reached.Receivers.All(o => o == Origin.Self || (o.Parameter >= 0 && Callback(entry, o.Parameter))))
                    {
                        continue;
                    }

                    hops.Add(new Hop(actor, reached.Site.Actor, entry, reached));
                }
            }
        }
    }

    /// <summary>The count of the units of code the walk read, each once (<see cref="ThrowSets.Walked"/>).</summary>
    public int Walked => sets.Walked;

    /// <summary>
    /// U56: one <c>call-loop</c> for each group of types that reach one another and hold the edge of a call, a call after <c>vouch</c>
    /// making none (U58), at the call of the group that stands first in source order, the program's file first, then by line, naming
    /// the shortest loop through it, each hop with its method and its line, as E1 puts <c>reference-cycle</c> at the first declaration of
    /// the cycle.
    /// </summary>
    public IReadOnlyList<Diagnostic> Errors()
    {
        var graph = hops.Where(h => !(h.Wait && h.Reached.Site.Vouch > 0)).ToList();
        var groups = Groups(graph);
        var errors = new List<(ActorSite Site, Diagnostic Error)>();
        foreach (var inside in graph.Where(h => groups[h.From] == groups[h.To]).GroupBy(h => groups[h.From]).Select(g => g.ToList()))
        {
            if (inside.Where(h => h.Wait).OrderBy(h => (h.Reached.Site.File, h.Reached.Site.Line)).FirstOrDefault() is not { } first)
            {
                continue; // T43: a group of spawns alone reaches new actors only
            }

            var loop = new List<Hop> { first };
            loop.AddRange(Shortest(inside, first.To, first.From));
            errors.Add((first.Reached.Site, Diagnostic.Error(ErrorIds.CallLoop, first.Reached.Site.Line, Text(loop))));
        }

        return errors.OrderBy(e => (e.Site.File, e.Site.Line)).Select(e => e.Error).ToList();
    }

    /// <summary>
    /// U59: <c>vouch-unneeded</c> at each <c>vouch</c> of the program's file whose call is on no loop of U56's graph read with every
    /// <c>vouch</c> left out, so that two on one loop keep each other, or whose call U57 takes out: a warning, which builds the program, as
    /// rustc warns of an <c>unsafe</c> block that holds no unsafe operation; the caller gives none beside a <c>not supported</c>, since
    /// an unread construct can hide an edge.
    /// </summary>
    public IReadOnlyList<Diagnostic> Warnings()
    {
        var groups = Groups(hops);
        var covering = new HashSet<CallExpression>(hops.Where(h => h.Wait && h.Reached.Site.Vouch > 0 && groups[h.From] == groups[h.To]).Select(h => h.Reached.Site.Call), ReferenceEqualityComparer.Instance);
        var warned = new HashSet<CallExpression>(ReferenceEqualityComparer.Instance);
        return sites
            .Where(s => s.Kind == SiteKind.Wait && s.Vouch > 0 && s.File == 0 && !covering.Contains(s.Call) && warned.Add(s.Call))
            .OrderBy(s => s.Vouch)
            .Select(s => Diagnostic.Warning(LintIds.VouchUnneeded, s.Vouch,
                $"`vouch` before the call of `{s.Actor.Name}.{s.Method!.Name}` covers no loop of calls that wait between actor types, read with every `vouch` left out: take the word out (U59, U56, U57)"))
            .ToList();
    }

    /// <summary>
    /// U57: which parameters of the methods of the actors are callbacks: calls through a reference reach the method, every one of them
    /// waits, stands in the code of the actors' entries alone, which no top-level code or <c>pub</c> function reaches, and fills the
    /// parameter, in the terms of each entry that reaches it, with the entry's own reference, as the call writes it, or with a parameter
    /// of the entry that is a callback, so that a reference handed on through a chain of calls that wait reaches the caller that waits at
    /// its start (assumed: T60 of #519, comment 7009683539, built at the lean while it waits for the owner); or none does and the actor's
    /// own code calls the method directly (U10), whose entries read its calls with the arguments they pass. It is the least fixed point:
    /// every parameter starts as no callback, and the rounds read each again as long as one changes, so that a cycle of parameters that
    /// no <c>this</c> fills stays none. The walk keeps no context per call, so one caller that passes another reference makes the
    /// parameter's calls edges for every caller, and a method that nothing calls has no caller to call back, so that its loop is refused
    /// as a loop no code reaches is (T53).
    /// </summary>
    private void Callbacks(IReadOnlyList<ActorCode> code, IReadOnlySet<ActorSite> outside)
    {
        var reaching = code.SelectMany(c => c.Entries).SelectMany(e => e.Sites.Select(r => (Entry: e, Reached: r)))
            .ToLookup(p => p.Reached.Site, ReferenceEqualityComparer.Instance as IEqualityComparer<ActorSite>);
        var methods = code.SelectMany(c => c.Entries).Where(e => e.Method is not null).ToList();
        foreach (var entry in methods)
        {
            callbacks[entry] = new bool[entry.Method!.Parameters.Count];
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
        }

        // a caller that waits in the code of entries alone, each of which fills the parameter with its own reference or a callback of its own
        bool Fills(ActorSite caller, int parameter) =>
            caller.Kind == SiteKind.Wait && !outside.Contains(caller) && reaching[caller].Any()
            && reaching[caller].All(r => parameter < r.Reached.Arguments.Count
                && r.Reached.Arguments[parameter].All(o => o == Origin.Self || (o.Parameter >= 0 && Callback(r.Entry, o.Parameter))));
    }

    // U57: whether the parameter of the entry's method is a callback, as the fixed point of Callbacks gave it
    private bool Callback(ActorEntry entry, int parameter) =>
        callbacks.TryGetValue(entry, out var each) && parameter < each.Length && each[parameter];

    // Tarjan's strongly connected components of the types under the hops, each type with the index of its group
    private Dictionary<ClassSymbol, int> Groups(IReadOnlyList<Hop> graph)
    {
        var next = graph.GroupBy(h => h.From).ToDictionary(g => g.Key, g => g.Select(h => h.To).Distinct().ToList());
        var index = new Dictionary<ClassSymbol, int>(ReferenceEqualityComparer.Instance);
        var low = new Dictionary<ClassSymbol, int>(ReferenceEqualityComparer.Instance);
        var group = new Dictionary<ClassSymbol, int>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<ClassSymbol>();
        var counter = 0;
        foreach (var root in actors.Where(a => !index.ContainsKey(a)))
        {
            // the walk, with an explicit stack of each type and the place in its list of successors, so that 2,000 types in a chain
            // take no frame of the process each
            var walk = new Stack<(ClassSymbol Type, int Next)>();
            Enter(root);
            while (walk.TryPop(out var top))
            {
                var successors = next.GetValueOrDefault(top.Type) ?? [];
                if (top.Next < successors.Count)
                {
                    walk.Push((top.Type, top.Next + 1));
                    var to = successors[top.Next];
                    if (!index.TryGetValue(to, out var seen))
                    {
                        Enter(to);
                    }
                    else if (!group.ContainsKey(to))
                    {
                        low[top.Type] = Math.Min(low[top.Type], seen); // a type on the stack, of the group being walked
                    }

                    continue;
                }

                if (low[top.Type] == index[top.Type])
                {
                    ClassSymbol member;
                    do
                    {
                        member = stack.Pop();
                        group[member] = index[top.Type];
                    }
                    while (!ReferenceEquals(member, top.Type));
                }

                if (walk.TryPeek(out var parent))
                {
                    low[parent.Type] = Math.Min(low[parent.Type], low[top.Type]);
                }
            }

            void Enter(ClassSymbol type)
            {
                index[type] = low[type] = counter++;
                stack.Push(type);
                walk.Push((type, 0));
            }
        }

        return group;
    }

    // the fewest hops from one type of a group to another, by a breadth-first walk inside the group, each step the hop of the pair of
    // types that stands first in source order
    private static List<Hop> Shortest(IReadOnlyList<Hop> inside, ClassSymbol from, ClassSymbol to)
    {
        var first = inside.GroupBy(h => (h.From, h.To)).Select(g => g.OrderBy(h => (h.Reached.Site.File, h.Reached.Site.Line)).First())
            .ToLookup(h => h.From, ReferenceEqualityComparer.Instance as IEqualityComparer<ClassSymbol>);
        var came = new Dictionary<ClassSymbol, Hop?>(ReferenceEqualityComparer.Instance) { [from] = null };
        var queue = new Queue<ClassSymbol>();
        queue.Enqueue(from);
        while (queue.TryDequeue(out var at) && !came.ContainsKey(to))
        {
            foreach (var hop in first[at].Where(h => !came.ContainsKey(h.To)))
            {
                came[hop.To] = hop;
                queue.Enqueue(hop.To);
            }
        }

        var path = new List<Hop>();
        for (var at = to; came[at] is { } hop; at = hop.From)
        {
            path.Insert(0, hop);
        }

        return path;
    }

    // T52, T55, T56: the hops of the loop, then what two chains in it do, then what to change, `send` or the code first and `vouch` last
    private static string Text(IReadOnlyList<Hop> loop)
    {
        var said = string.Join(", and ", loop.Select(h => h.Said));
        if (loop is [{ From: var one } only] && ReferenceEquals(one, only.To))
        {
            return $"{said}, a call of another `{one.Name}`: two chains that enter two `{one.Name}`s at once wait on each other; make the call a `send` where no result is needed, or change the code so that its calls do not come back, and put `vouch` before it only where it reaches another `{one.Name}` than the one that waits (U56, U58)";
        }

        return $"{said}: two chains that enter this loop at once wait on each other; make a call of it a `send` where no result is needed, or change the code so that its calls do not come back, and put `vouch` before a call only where it reaches another actor than the one that waits (U56, U58)";
    }

    /// <summary>An edge of the graph: from the actor whose entry reaches the site, to the actor the site calls or spawns.</summary>
    private sealed record Hop(ClassSymbol From, ClassSymbol To, ActorEntry Entry, ReachedSite Reached)
    {
        public bool Wait => Reached.Site.Kind == SiteKind.Wait;

        public string Said => (Wait ? $"{Entry.Name} waits on `{To.Name}.{Reached.Site.Method!.Name}`" : $"{Entry.Name} spawns `{To.Name}`")
            + $" (line {Reached.Site.Line}{(Reached.Through is { } through ? $", in {through}" : "")})";
    }
}
