using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Section 5 of the design record: what the code of a program can throw, found over the whole program. The <c>Reason</c> of an actor
/// is the union of every type its code can throw and the runtime's error type, <c>RuntimeError</c> of <c>std/</c>, which is a member of
/// every <c>Reason</c>; the set of a <c>pub</c> function is what a call of it can raise, which the compiler lists. An actor's code is its
/// construction, its constructors and every method it declares, with what they reach: a call of a function; of a method, with every
/// override of it and, through an interface, every implementer's method; a construction of a class or of a <c>data</c> value, with the
/// defaults and the constructors it runs; a read of a property; a call of a function value, which reaches every lambda of the program
/// whose type fits, and of a value of a C function type, which reaches every function of the C calling convention that a name gives as a
/// value of that type (R17); the test of <c>Where</c> and the key of <c>SortBy</c>, which they call (M9, X8), where another built-in call
/// that takes a function value keeps it; the <c>Text()</c> that <c>print</c>, <c>eprint</c>, a hole of an interpolation and
/// <c>string.Join</c> call on the types they make text of, and for a generic <c>data</c> type derived from such a type the text of each
/// instantiation of it that the program builds (section 5, the owner, 2026-11-16); the construction of an actor it spawns, whose throw is
/// its crash (U3); and the code of an actor it spawns under <c>escalate</c>, whose crash passes up whole (U25). The instantiations a
/// program builds are found as Rust's monomorphization finds its instances: from the roots, the top-level code and every <c>pub</c>
/// function, a generic one and each actor's <c>pub</c> methods among them, through the type arguments each call gives, with the fixed
/// point's edges, a lambda's in the unit that makes it, and each with no type parameter left, read again with the text of each
/// instantiation found until they grow no more. A call through an actor's
/// reference reaches none of the callee's code, whose crash its caller gets as <c>callee-crashed</c> (U20). The text of a thrown value
/// adds nothing, since a raise while it is made ends the process (section 6), and <c>==</c>, <c>equal by</c> and a hash run no code of the program, which declares no <c>Equals</c> and no
/// <c>GetHashCode</c> (K5, K8, D16). A set is in the order of the first <c>throw</c> of each type that the code reaches, the program's
/// file before <c>std/</c> and by line, with the runtime's type last (section 5, assumed there). No build reads the sets; the tests do.
/// The same walk is what layer 2 of section 6's deadlocks reads of what the code of an actor reaches (U56, <see cref="ActorsCode"/>):
/// each unit holds the calls through an actor's reference and the <c>spawn</c>s its own code makes, with where each receiver and
/// argument comes from, and every edge into a function or a method where each argument comes from, so that the code an actor's
/// message runs is one definition for the sets and for the loops.
/// </summary>
public sealed class ThrowSets
{
    private readonly Checker program;
    private readonly Checker? std;
    private readonly bool blocks;
    private readonly IReadOnlySet<CrashPolicy>? skipped;
    private readonly bool reach;
    private readonly Dictionary<object, Unit> units = new(new UnitKeys());
    private readonly List<Unit> order = [];
    private KurzType? runtimeError;
    private List<(LambdaExpression Lambda, FunctionType Type, SemanticModel Model, int File)>? lambdas;
    private Dictionary<FunctionSymbol, List<FunctionSymbol>>? overrides;
    private List<DataType>? instantiations;
    private List<(object Key, Unit Unit)>? waiting;
    private Dictionary<ActorSite, Unit>? owners;

    /// <summary>
    /// The sets of the code of <paramref name="program"/>, which sees the types of <paramref name="std"/> (W1); null where the program is
    /// <c>std/</c> itself. Where <paramref name="blocks"/> does not hold, the bodies of the blocks of <c>on crash</c> are left out, as at the
    /// checker's first reading of such a program, which reads them with no <c>Reason</c> found yet (U30), and so are those of the blocks
    /// <paramref name="skipped"/> names, which reported errors at a reading whose <c>Reason</c>s may not have settled. Where
    /// <paramref name="reach"/> holds, the walk reads what the code reaches and no throw, for <see cref="ActorsCode"/>, on a program that
    /// may have reported errors: a node the checker did not record, a call that reported its own error among them, reaches nothing.
    /// </summary>
    public ThrowSets(Checker program, Checker? std, bool blocks = true, IReadOnlySet<CrashPolicy>? skipped = null, bool reach = false)
    {
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        this.std = std;
        this.blocks = blocks;
        this.skipped = skipped;
        this.reach = reach;
    }

    // the runtime's error type, which only the sets read
    private KurzType RuntimeError
    {
        get
        {
            if (runtimeError is null)
            {
                var error = (std ?? program).Model.DataTypes.FirstOrDefault(d => d.Name == "RuntimeError")
                    ?? throw new InvalidOperationException("std/ declares no `RuntimeError`, the runtime's error type of section 5");
                runtimeError = new DataType(error, []);
            }

            return runtimeError;
        }
    }

    /// <summary>
    /// Every kind of node of the tree that the walk reads, with what it can run. A kind missing here is a defect of the walk: a test holds
    /// this table to every kind the tree declares, so that a new kind cannot be added without its edge.
    /// </summary>
    public static IReadOnlyDictionary<Type, string> Kinds { get; } = new Dictionary<Type, string>
    {
        [typeof(IntegerLiteral)] = "no code",
        [typeof(RealLiteral)] = "no code",
        [typeof(UnitLiteral)] = "no code",
        [typeof(StringLiteral)] = "its holes, and the Text() of each hole's type",
        [typeof(CharLiteral)] = "no code",
        [typeof(BoolLiteral)] = "no code",
        [typeof(NullLiteral)] = "no code",
        [typeof(NameExpression)] = "a property's read",
        [typeof(ThisExpression)] = "no code",
        [typeof(MemberAccess)] = "its target, and a property's read",
        [typeof(TypeMemberAccess)] = "a property's read",
        [typeof(CallExpression)] = "its callee, its arguments, the bound of its clause and what it calls; through an actor's reference none of the callee's code, and the call or the send it is, its word vouch running nothing (U56)",
        [typeof(ListLiteral)] = "its items",
        [typeof(IndexExpression)] = "its target and its index",
        [typeof(UnaryExpression)] = "its operand",
        [typeof(BinaryExpression)] = "its operands",
        [typeof(RangeExpression)] = "its ends",
        [typeof(LambdaExpression)] = "nothing where it is made but what its body builds: its body where a value of its type is called",
        [typeof(MatchExpression)] = "its subject and its arms",
        [typeof(WithExpression)] = "its target, its values, and the fields of the data type's body",
        [typeof(ElseExpression)] = "its call and its arms",
        [typeof(ParenthesizedExpression)] = "its inner expression",
        [typeof(SpawnExpression)] = "the actor's construction, its code under escalate or a block, and the block's body; for the loops the spawn it is, the construction being the spawned actor's code (U56)",
        [typeof(AttemptExpression)] = "no code: the checker refuses it",
        [typeof(Block)] = "its statements",
        [typeof(VariableDeclaration)] = "its value",
        [typeof(AssignOrDeclare)] = "its value",
        [typeof(Assignment)] = "its place and its value",
        [typeof(ExpressionStatement)] = "its expression",
        [typeof(IfStatement)] = "its condition and its branches",
        [typeof(WhileStatement)] = "its condition and its body",
        [typeof(ForInStatement)] = "its collection, its body or its passes, and its arms",
        [typeof(MatchStatement)] = "its subject and its arms",
        [typeof(BreakStatement)] = "no code",
        [typeof(ContinueStatement)] = "no code",
        [typeof(ReturnStatement)] = "its value",
        [typeof(ThrowStatement)] = "its value, and the throw",
        [typeof(RawBlock)] = "its body",
        [typeof(UseStatement)] = "no code",
        [typeof(SendStatement)] = "its call's arguments",
        [typeof(WithinStatement)] = "its bound and its body",
        [typeof(DecisionStatement)] = "no code: the runtime acts on the decision",
        [typeof(AsmStatement)] = "its operands, and the function a sym operand names",
        [typeof(ConstAssertStatement)] = "no code: the compiler reads it",
        [typeof(ExternBlock)] = "no code",
        [typeof(FunctionDeclaration)] = "no code where it stands: its body where it is called",
        [typeof(FieldDeclaration)] = "no code where it stands: its value where its type is made",
        [typeof(ConstructorDeclaration)] = "no code where it stands: its body where its class is made",
        [typeof(TypeDeclaration)] = "no code where it stands",
        [typeof(TypePattern)] = "no code",
        [typeof(ValuePattern)] = "its value",
        [typeof(ElsePattern)] = "no code",
    };

    /// <summary>The count of the units of code the walk has read: functions, constructors, lambdas and constructions.</summary>
    public int Walked => order.Count;

    /// <summary>The <c>Reason</c> of the actor (section 5): every type its code can throw, then the runtime's error type.</summary>
    public IReadOnlyList<KurzType> ReasonOf(ClassSymbol actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.IsActor ? Solved(new Root(actor)) : throw new ArgumentException($"`{actor.Name}` is no actor", nameof(actor));
    }

    /// <summary>What a call of the function can raise (section 5): every type its code can throw, then the runtime's error type.</summary>
    public IReadOnlyList<KurzType> RaisedBy(FunctionSymbol function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Solved(function);
    }

    /// <summary>Each <c>pub</c> function of the program's file that has a body, its methods and those of its data types among them, by line, with what a call of it can raise.</summary>
    public IReadOnlyDictionary<FunctionSymbol, IReadOnlyList<KurzType>> PubFunctions() => Pub().ToDictionary(f => f, RaisedBy);

    // the `pub` functions of the program's file that have a body, its methods and those of its data types among them, by line
    private IEnumerable<FunctionSymbol> Pub()
    {
        var model = program.Model;
        var copies = new HashSet<FunctionSymbol>(model.Copies, ReferenceEqualityComparer.Instance);
        return model.Functions.Where(f => f.DataOwner is null && !copies.Contains(f))
            .Concat(model.DataTypes.SelectMany(d => d.Methods.Values.SelectMany(list => list)))
            .Where(f => f.Declaration is { } d && (d.Modifiers & Modifiers.Pub) != 0 && (d.BlockBody is not null || d.ExpressionBody is not null))
            .Distinct()
            .OrderBy(f => f.Line);
    }

    /// <summary>
    /// Section 6, "Deadlocks", layer 2 (U56, U57): the code of each actor of the program, by its entries, the construction first, then
    /// each method it declares, with each call through an actor's reference and each <c>spawn</c> that entry's code reaches by the walk of
    /// the sets, and the origins the receiver of each, and each of its arguments, can have in the entry's terms: a call of a function or a method passes each origin
    /// of an argument to the parameter it fills, through a local without <c>mut</c> too, to the method it picks and to each of its
    /// overrides and of the methods that implement it, a data type's method taking the value it is called on first (D16), and any other
    /// edge, a lambda's or a construction's among them, passes none, which reads as no callback. A <c>spawn</c> reaches the
    /// construction of the actor it makes, whose code is that actor's, and the code under <c>escalate</c> is the child's: neither is the
    /// spawner's. Each entry holds too the writes of its actor's fields that its code reaches, with the origins each value can have in
    /// its terms, passed as an argument's are (T49 (c)). The walk reads each unit once over all the actors, from the roots of the sets and
    /// every actor's code, and the reach of each unit is its own sites and writes and those of the units it runs, read again as long as one
    /// grows.
    /// </summary>
    public IReadOnlyList<ActorCode> ActorsCode()
    {
        Instantiations();
        var actors = Checkers().SelectMany(c => c.Checker.Model.Classes).Where(c => c.IsActor).ToList();
        foreach (var actor in actors)
        {
            Discover(new Root(actor));
        }

        Close();
        var direct = units.Where(u => u.Key is not Root).SelectMany(u => u.Value.Edges).Where(e => e.Runs).Select(e => e.Unit).ToHashSet(new UnitKeys());
        return actors.Select(actor => new ActorCode(actor, units[new Root(actor)].Edges.Select(e => Entry(e, direct.Contains(e.Unit))).ToList())).ToList();
    }

    /// <summary>Every call through an actor's reference and every <c>spawn</c> of every unit the walk read, in the order it read them.</summary>
    public IReadOnlyList<ActorSite> Sites() => order.SelectMany(u => u.Sites).ToList();

    /// <summary>Every write of a field of an actor that a unit the walk read makes, a lambda's that no code runs among them (T49 (c)).</summary>
    public IReadOnlyList<FieldWrite> Writes() => order.SelectMany(u => u.Writes).ToList();

    /// <summary>
    /// U57, T74 (b): every site that code outside the messages of the program's actor types reaches after <see cref="ActorsCode"/>: the
    /// top-level code, and each root of the sets that no code of an actor reaches, a <c>pub</c> function or a <c>pub</c> method of a class or
    /// a data type that only a caller outside the program could call (assumed: a reading of T74 (b) of comment 7051071630), through every
    /// function, method and lambda they run; a method of a class, an interface or a data type that the actors' code alone calls runs in
    /// their messages, as their own methods do.
    /// </summary>
    public IReadOnlySet<ActorSite> Outside()
    {
        var inside = new HashSet<object>(new UnitKeys());
        var pending = new Queue<object>(units.Where(u => u.Key is Root).SelectMany(u => u.Value.Edges).Select(e => e.Unit).Where(k => k is not FunctionSymbol { IsStatic: true }));
        while (pending.TryDequeue(out var key))
        {
            if (inside.Add(key))
            {
                foreach (var edge in units[key].Edges.Where(e => e.Runs && !e.Crosses))
                {
                    pending.Enqueue(edge.Unit);
                }
            }
        }

        return Roots().Where(r => r is not FunctionSymbol { Owner.IsActor: true, IsStatic: false } && !inside.Contains(r))
            .SelectMany(r => units[r].Reach.Keys).ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<ActorSite>);
    }

    // an entry of an actor's code: its construction, or a method; a site that stands in another unit names it
    private ActorEntry Entry(Edge edge, bool direct)
    {
        var unit = units[edge.Unit];
        var sites = unit.Reach.Select(p => new ReachedSite(p.Key, ReferenceEquals(owners![p.Key], unit) ? null : owners[p.Key].Name, p.Value[0], p.Value[1..])).ToList();
        var writes = unit.Written.Select(p => new ReachedWrite(p.Key, p.Value)).ToList();
        return new ActorEntry(unit.Name, edge.Unit as FunctionSymbol, sites, writes, direct);
    }

    // each unit's reach: its own sites, then those of every unit it runs through an edge that does not cross into another actor's code (U56), each origin of the receiver and
    // of each argument passed through the edge, as long as one grows; the units were found from the callers to the callees, so the
    // reverse order reads most callees first
    private void Close()
    {
        if (owners is not null)
        {
            return;
        }

        owners = new Dictionary<ActorSite, Unit>(ReferenceEqualityComparer.Instance);
        foreach (var unit in order)
        {
            foreach (var site in unit.Sites)
            {
                unit.Reach[site] = [[site.Receiver], .. site.Arguments.Select(a => new HashSet<Origin> { a })];
                owners[site] = unit;
            }

            foreach (var write in unit.Writes)
            {
                unit.Written[write] = [write.Value];
            }
        }

        for (var grew = true; grew;)
        {
            grew = false;
            for (var i = order.Count - 1; i >= 0; i--)
            {
                var unit = order[i];
                foreach (var edge in unit.Edges.Where(e => e.Runs && !e.Crosses))
                {
                    foreach (var (site, origins) in units[edge.Unit].Reach.ToList())
                    {
                        if (!unit.Reach.TryGetValue(site, out var into))
                        {
                            unit.Reach[site] = into = origins.Select(_ => new HashSet<Origin>()).ToArray();
                        }

                        for (var k = 0; k < origins.Length; k++)
                        {
                            foreach (var origin in origins[k].ToList())
                            {
                                grew |= into[k].Add(Passed(edge, origin));
                            }
                        }
                    }

                    foreach (var (write, values) in units[edge.Unit].Written.ToList())
                    {
                        if (!unit.Written.TryGetValue(write, out var into))
                        {
                            unit.Written[write] = into = [];
                        }

                        foreach (var value in values.ToList())
                        {
                            grew |= into.Add(Passed(edge, value));
                        }
                    }
                }
            }
        }
    }

    // U57: an origin of the unit an edge reaches in the terms of the unit it leaves: a parameter takes its argument's, and the actor
    // itself, a field of it, an item of a field's list and anything else stay
    private static Origin Passed(Edge edge, Origin origin) =>
        origin.Parameter < 0 ? origin
        : edge.Arguments is { } arguments && origin.Parameter < arguments.Count ? arguments[origin.Parameter]
        : Origin.Other;

    private List<KurzType> Solved(object root)
    {
        Instantiations();
        var unit = Discover(root);
        Solve();
        var types = unit.Set.OrderBy(t => (t.File, t.Line)).Select(t => t.Type).ToList();
        types.RemoveAll(t => Types.Same(t, RuntimeError));
        types.Add(RuntimeError);
        return types;
    }

    // ---- the units and their fixed point ----------------------------------------------------------------------------------

    /// <summary>A thrown type, with the place of the first <c>throw</c> of it that the code reaches: the file, 0 for the program's and 1 for <c>std/</c>, and the line.</summary>
    private sealed record Thrown(KurzType Type, int File, int Line);

    /// <summary>
    /// An edge from a unit to a unit it runs, with the type arguments the call gives the type parameters of that unit (T19); or, where it
    /// does not run it, from a unit to a lambda it makes, which brings what the lambda builds and nothing it throws (section 5), as Rust
    /// collects a closure's instance in the instance of the function that makes it.
    /// <c>Arguments</c> says where each argument of a call of a function or a method comes from, by the callee's parameter, null where the
    /// edge passes none that the loops follow (U57); an edge that <c>Crosses</c> runs the code of another actor, the construction of one
    /// that a <c>spawn</c> makes or its code under <c>escalate</c>, which its crash brings and no loop of the spawner holds (U56).
    /// </summary>
    private sealed record Edge(object Unit, IReadOnlyDictionary<TypeParameterSymbol, KurzType>? Map, bool Runs = true, IReadOnlyList<Origin>? Arguments = null, bool Crosses = false);

    /// <summary>The code of an actor (section 5): its construction, its constructors and every method it declares.</summary>
    private sealed record Root(ClassSymbol Actor);

    /// <summary>What a construction of a class runs besides a constructor of its body: the defaults of its primary constructor and of its body's fields, and its base's construction (K7, K10).</summary>
    private sealed record Construction(ClassSymbol Class);

    /// <summary>What a construction of a <c>data</c> value runs: the defaults of its primary constructor, and the fields of its body computed (D11, D16), its base's among them.</summary>
    private sealed record Made(DataSymbol Data);

    /// <summary>A call of a function value of this type, which runs a lambda of the program whose type fits (F11).</summary>
    private sealed record Valued(FunctionType Type);

    /// <summary>A call of a value of this C function type, which runs a function of the C calling convention that a name gives as a value of that type (R17).</summary>
    private sealed record Native(CFunctionType Type);

    /// <summary>The top-level code of a file, the root actor's (O6): a root of what the program builds, as Rust's <c>main</c> is.</summary>
    private sealed record TopLevel(SemanticModel Model);

    /// <summary>The text of every instantiation of a generic <c>data</c> type that the program builds (section 5, the owner, 2026-11-16).</summary>
    private sealed record Each(DataSymbol Data);

    /// <summary>
    /// A unit of code: what it throws itself, what it runs, and the set the fixed point gives it; and the generic <c>data</c> types it
    /// builds itself, in the terms of its own type parameters, with those the fixed point gives it.
    /// </summary>
    private sealed class Unit
    {
        /// <summary>What the unit is, as a text of <c>call-loop</c> names the code a call stands in: a function's name, <c>a lambda</c>.</summary>
        public string Name { get; set; } = "";

        public List<Thrown> Own { get; } = [];

        /// <summary>The calls through an actor's reference and the <c>spawn</c>s the unit's own code makes (U56).</summary>
        public List<ActorSite> Sites { get; } = [];

        /// <summary>Each site the unit's code reaches, its own and those of the units it runs, with the origins its receiver, then each of its arguments by the parameter it fills, can have in the unit's terms (U57).</summary>
        public Dictionary<ActorSite, HashSet<Origin>[]> Reach { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The writes of an actor's fields the unit's own code makes (U57, T49 (c)).</summary>
        public List<FieldWrite> Writes { get; } = [];

        /// <summary>Each write the unit's code reaches, its own and those of the units it runs, with the origins its value can have in the unit's terms (U57, T49 (c)).</summary>
        public Dictionary<FieldWrite, HashSet<Origin>> Written { get; } = new(ReferenceEqualityComparer.Instance);

        public List<Edge> Edges { get; } = [];

        public List<Thrown> Set { get; } = [];

        public List<DataType> Builds { get; } = [];

        public List<DataType> Built { get; } = [];
    }

    /// <summary>The keys of the units: a lambda by its node, never by its text, and every other key by its own equality.</summary>
    private sealed class UnitKeys : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) =>
            x is LambdaExpression || y is LambdaExpression ? ReferenceEquals(x, y) : object.Equals(x, y);

        public int GetHashCode(object obj) =>
            obj is LambdaExpression ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) : obj.GetHashCode();
    }

    private Unit Discover(object key)
    {
        if (units.TryGetValue(key, out var found))
        {
            return found;
        }

        var first = Added(key);
        Read(key, first);
        return first;
    }

    // the unit's code, and the code of every unit it reaches that no read took before
    private void Read(object key, Unit unit)
    {
        var pending = new Queue<(object Key, Unit Unit)>();
        pending.Enqueue((key, unit));
        while (pending.TryDequeue(out var next))
        {
            Scan(next.Key, next.Unit);
            foreach (var edge in next.Unit.Edges.Where(edge => !units.ContainsKey(edge.Unit)))
            {
                pending.Enqueue((edge.Unit, Added(edge.Unit)));
            }
        }
    }

    private Unit Added(object key)
    {
        var unit = new Unit();
        units[key] = unit;
        order.Add(unit);
        return unit;
    }

    /// <summary>
    /// Each unit's set made of its own throws and of the sets of the units it runs, and the generic data types it builds made of its own and
    /// of those the units it runs build, each put through each edge's type arguments, as long as a set grows. No program the checker reads
    /// reaches the bound: a thrown or a built type grows only through an edge's type arguments, and a cycle of generic calls whose type
    /// argument grows, <c>void Deep&lt;T&gt;(T x, int n)</c> calling itself with <c>[x]</c>, is the compile error
    /// <c>growing-type-argument</c> whether or not a call reaches it (T34).
    /// </summary>
    private void Solve()
    {
        for (var round = 0; ; round++)
        {
            if (round > 10_000)
            {
                throw new InvalidOperationException("the throw sets did not settle: a thrown or a built type grows at each call");
            }

            var grew = false;
            foreach (var unit in order)
            {
                foreach (var thrown in unit.Own)
                {
                    grew |= Add(unit.Set, thrown);
                }

                foreach (var made in unit.Builds)
                {
                    grew |= Add(unit.Built, made);
                }

                foreach (var edge in unit.Edges)
                {
                    List<Thrown> reached = edge.Runs ? units[edge.Unit].Set.ToList() : []; // the making of a lambda throws nothing
                    foreach (var thrown in reached)
                    {
                        foreach (var one in Cases(Through(edge, thrown.Type)))
                        {
                            grew |= Add(unit.Set, thrown with { Type = one });
                        }
                    }

                    foreach (var made in units[edge.Unit].Built.ToList())
                    {
                        if (Through(edge, made) is DataType d)
                        {
                            grew |= Add(unit.Built, d);
                        }
                    }
                }
            }

            if (!grew)
            {
                return;
            }
        }
    }

    // a type of the unit an edge reaches, in the terms of the unit it leaves: the type arguments of the call put in (T19)
    private static KurzType Through(Edge edge, KurzType type) => edge.Map is null ? type : Types.Substitute(type, edge.Map);

    private static IReadOnlyList<KurzType> Cases(KurzType type) => type is UnionType u ? u.Cases : new[] { type };

    // a built type once
    private static bool Add(List<DataType> built, DataType made)
    {
        if (built.Any(d => Types.Same(d, made)))
        {
            return false;
        }

        built.Add(made);
        return true;
    }

    /// <summary>
    /// Section 5 (the owner, 2026-11-16, "Each instantiation"): every instantiation of a generic data type that the program builds, as
    /// Rust's monomorphization collects its instances, from the roots through the type arguments each call gives, each with no type
    /// parameter left; read before the code of any set, and read again with each text: the text of an instantiation runs code that can
    /// build more, a <c>Text()</c> of a type argument among it, so each <see cref="Each"/> unit reads the text of every instantiation as
    /// it is found, and the roots are solved again, until the list grows no more. The order of the reads does not change what the list
    /// and the sets hold, since both only grow and each <see cref="Each"/> unit has read every instantiation of its type before the
    /// method returns, so that a set does not depend on the order of the questions.
    /// </summary>
    private List<DataType> Instantiations()
    {
        if (instantiations is null)
        {
            instantiations = [];
            waiting = [];
            var roots = Roots().Select(Discover).ToList();
            var eaches = new List<(Each Key, Unit Unit, Walk Walk)>();
            var read = new List<int>(); // for each of them, how many of the instantiations it read

            // the loop ends: a text that builds a larger instantiation of the type it is read for, as a `Text()` of `Wrap<T>` that builds a
            // `Wrap<List<T>>` would, is the body of a generic `data` type or a generic class, which the checker says `not supported` to
            // (D16, T19; the rows `data W<T>` and `class Box<T>` of `CheckerTests.A_part_of_a_data_body_the_checker_does_not_read_is_not_supported_at_its_line`
            // and `A_part_of_a_class_the_checker_does_not_read_is_not_supported`), so a text read here is that of a type without type
            // parameters, or reaches through an instantiation's type arguments the texts of its fields' types, which build one fixed set
            // whichever instantiation is read, and the type arguments of a call grow no more than `Solve` lets them (T34)
            for (var more = true; more;)
            {
                Solve();
                foreach (var made in roots.SelectMany(root => root.Built).Where(d => !Open(d)).ToList())
                {
                    Add(instantiations, made);
                }

                foreach (var (key, unit) in waiting)
                {
                    var (model, file) = Site(((Each)key).Data);
                    eaches.Add(((Each)key, unit, new Walk(this, model, file, unit)));
                    read.Add(0);
                }

                more = false;
                waiting.Clear();
                for (var i = 0; i < eaches.Count; i++)
                {
                    for (; read[i] < instantiations.Count; read[i]++)
                    {
                        if (ReferenceEquals(instantiations[read[i]].Symbol, eaches[i].Key.Data))
                        {
                            eaches[i].Walk.TextOf(instantiations[read[i]]);
                            more = true;
                        }
                    }

                    foreach (var edge in eaches[i].Unit.Edges.ToList())
                    {
                        Discover(edge.Unit); // a unit the text reaches that no read took before, which can build more
                    }
                }

                more |= waiting.Count > 0;
            }

            waiting = null;
        }

        return instantiations;
    }

    // the roots of what the program builds, as Rust's are `main` and its `pub` items: the top-level code and every `pub` function the
    // list holds, a generic one among them, whose instantiations with every type argument named are built whatever type argument a
    // call gives it (section 5, the owner, 2026-11-17, #560, comment 6952257456, "(b) As built (Recommended)"); the list holds each
    // actor's `pub` methods, which alone take messages (U9), so that what an actor builds is built: its other methods run from its own
    // code alone, and its construction from a `spawn` that a root reaches
    private IEnumerable<object> Roots() => Pub().Prepend<object>(new TopLevel(program.Model));

    // a type once, at the earliest place that throws it
    private static bool Add(List<Thrown> set, Thrown thrown)
    {
        var at = set.FindIndex(t => Types.Same(t.Type, thrown.Type));
        if (at < 0)
        {
            set.Add(thrown);
            return true;
        }

        if ((thrown.File, thrown.Line).CompareTo((set[at].File, set[at].Line)) < 0)
        {
            set[at] = thrown;
            return true;
        }

        return false;
    }

    // ---- what each unit runs ----------------------------------------------------------------------------------------------

    private void Scan(object key, Unit unit)
    {
        unit.Name = NameOf(key);
        switch (key)
        {
            case Root r:
                unit.Edges.Add(new Edge(new Construction(r.Actor), null));
                unit.Edges.AddRange(r.Actor.Constructors.Select(k => new Edge(k, null)));
                unit.Edges.AddRange(r.Actor.Methods.Values.SelectMany(list => list).Select(m => new Edge(m, null)));
                break;
            case Construction c:
                {
                    var walk = WalkOf(c.Class, unit);
                    foreach (var slot in c.Class.Constructor.Where(slot => ReferenceEquals(slot.Owner, c.Class)))
                    {
                        walk.Optional(slot.Declaration.Default);
                    }

                    foreach (var field in c.Class.Fields.Where(f => f.Body is { } body && (c.Class.Declaration.Members ?? []).Any(m => ReferenceEquals(m, body))))
                    {
                        walk.Optional(field.Body!.Value);
                        if (field.Body.Value is { } value)
                        {
                            walk.Default(c.Class, field, value);
                        }
                    }

                    if (c.Class.BaseCall is { } call)
                    {
                        walk.Expression(call);
                    }
                    else if (c.Class.Base is { } b)
                    {
                        unit.Edges.Add(new Edge(new Construction(b), null));
                    }

                    break;
                }

            case ConstructorSymbol k:
                {
                    var walk = WalkOf(k.Owner, unit);
                    foreach (var parameter in k.Declaration.Parameters)
                    {
                        walk.Optional(parameter.Default);
                    }

                    if (k.ThisCall is { } self)
                    {
                        walk.Expression(self);
                    }
                    else
                    {
                        if (k.BaseCall is { } baseCall)
                        {
                            walk.Expression(baseCall);
                        }

                        unit.Edges.Add(new Edge(new Construction(k.Owner), null));
                    }

                    walk.Statement(k.Declaration.Body);
                    break;
                }

            case Made m:
                {
                    var (model, file) = Site(m.Data);
                    var walk = new Walk(this, model, file, unit);
                    foreach (var parameter in m.Data.Declaration.PrimaryConstructor ?? [])
                    {
                        walk.Optional(parameter.Default);
                    }

                    unit.Edges.AddRange(m.Data.Initializers.Values.Select(f => new Edge(f, null)));
                    if (m.Data.Base is { } b)
                    {
                        unit.Edges.Add(new Edge(new Made(b), null));
                    }

                    break;
                }

            case FunctionSymbol f:
                Function(f, unit);
                break;
            case LambdaExpression l:
                {
                    var (_, _, model, file) = Lambdas().First(x => ReferenceEquals(x.Lambda, l));
                    var walk = new Walk(this, model, file, unit, lambda: true);
                    walk.Optional(l.ExpressionBody);
                    if (l.BlockBody is { } block)
                    {
                        walk.Statement(block);
                    }

                    break;
                }

            case Valued v:
                unit.Edges.AddRange(Lambdas().Where(x => Fits(x.Type, v.Type)).Select(x => new Edge(x.Lambda, null)));
                break;
            case Native n:
                unit.Edges.AddRange(Checkers()
                    .SelectMany(c => c.Checker.Model.FunctionNames.Where(x => c.Checker.Model.HasType(x.Name) && Types.Same(c.Checker.Model.TypeOf(x.Name).Type, n.Type)))
                    .Select(x => x.Function)
                    .Distinct()
                    .Select(f => new Edge(f, null)));
                break;
            case TopLevel t:
                {
                    var walk = new Walk(this, t.Model, Checkers().First(c => ReferenceEquals(c.Checker.Model, t.Model)).File, unit);
                    foreach (var statement in t.Model.TopLevel)
                    {
                        walk.Statement(statement);
                    }

                    break;
                }

            case Each e:
                if (waiting is not null)
                {
                    waiting.Add((e, unit)); // read once every instantiation is known
                    break;
                }

                {
                    var (model, file) = Site(e.Data);
                    var walk = new Walk(this, model, file, unit);
                    foreach (var made in Instantiations().Where(d => ReferenceEquals(d.Symbol, e.Data)))
                    {
                        walk.TextOf(made);
                    }
                }

                break;
            default:
                throw new InvalidOperationException($"no unit of code is a `{key.GetType().Name}`");
        }
    }

    private void Function(FunctionSymbol f, Unit unit)
    {
        var declaration = (f.Real ?? f).Declaration;
        if (declaration is null || Site(f) is not { } site)
        {
            return; // a function of C, or of a folder of the runtime, whose code throws nothing: a test holds that the runtime has no `throw`
        }

        var copies = Checkers().SelectMany(c => c.Checker.Model.CopiesOf(f)).ToList();
        if (copies.Count > 0 || site.Model.ReadsItsType(f))
        {
            unit.Edges.AddRange(copies.Select(copy => new Edge(copy, null))); // T33: the copies, each read for its type arguments, run in its place
            return;
        }

        // U57: the parameters the arguments of a call of the function fill, each by its place, where the checker read it
        var parameters = declaration.Parameters.Select(p => site.Model.Declares(p) ? site.Model.DeclarationOf(p) : null).ToList();
        var walk = new Walk(this, site.Model, site.File, unit, parameters);
        foreach (var parameter in declaration.Parameters)
        {
            walk.Optional(parameter.Default);
        }

        walk.Optional(declaration.ExpressionBody);
        if (declaration.BlockBody is { } block)
        {
            walk.Statement(block);
        }
    }

    // what a unit of code is, as a text of `call-loop` names where a call stands (U56)
    private static string NameOf(object key) => key switch
    {
        FunctionSymbol { Owner: { } owner } f => $"`{owner.Name}.{f.Name}`",
        FunctionSymbol f => $"`{f.Name}`",
        LambdaExpression => "a lambda",
        Construction c => $"the construction of `{c.Class.Name}`",
        ConstructorSymbol k => $"a constructor of `{k.Owner.Name}`",
        Made m => $"the construction of `{m.Data.Name}`",
        Each e => $"the text of `{e.Data.Name}`",
        TopLevel => "the top-level code",
        _ => "",
    };

    // the model that read the function's body, and the file's place in the order of a set; null for a function of neither file
    private (SemanticModel Model, int File)? Site(FunctionSymbol f)
    {
        foreach (var (checker, file) in Checkers())
        {
            if ((f.Declaration is { } d && checker.Model.HasFunction(d)) || (f.Real?.Declaration is { } r && checker.Model.HasFunction(r)))
            {
                return (checker.Model, file);
            }
        }

        return null;
    }

    private Walk WalkOf(ClassSymbol c, Unit unit)
    {
        foreach (var (checker, file) in Checkers())
        {
            if (checker.Model.Classes.Contains(c))
            {
                return new Walk(this, checker.Model, file, unit);
            }
        }

        throw new InvalidOperationException($"the class `{c.Name}` is of no file the sets read");
    }

    private (SemanticModel Model, int File) Site(DataSymbol d)
    {
        foreach (var (checker, file) in Checkers())
        {
            if (checker.Model.DataTypes.Contains(d))
            {
                return (checker.Model, file);
            }
        }

        throw new InvalidOperationException($"the data type `{d.Name}` is of no file the sets read");
    }

    private IEnumerable<(Checker Checker, int File)> Checkers()
    {
        yield return (program, 0);
        if (std is not null)
        {
            yield return (std, 1);
        }
    }

    private List<(LambdaExpression Lambda, FunctionType Type, SemanticModel Model, int File)> Lambdas() =>
        lambdas ??= Checkers().SelectMany(c => c.Checker.Model.Lambdas.Select(l => (l.Lambda, l.Info.Type, c.Checker.Model, c.File))).ToList();

    private IEnumerable<ClassSymbol> AllClasses() => Checkers().SelectMany(c => c.Checker.Model.Classes);

    private IEnumerable<DataSymbol> AllData() => Checkers().SelectMany(c => c.Checker.Model.DataTypes);

    // F11: a lambda fits a call of a function value that has its parameters and its result; where either type holds a type parameter,
    // whose argument the call does not show, every lambda of as many parameters fits
    private static bool Fits(FunctionType lambda, FunctionType call) =>
        lambda.Parameters.Count == call.Parameters.Count && (Open(lambda) || Open(call) || Types.Assignable(lambda, call));

    private static bool Open(KurzType type) => type switch
    {
        TypeParameterType => true,
        ListType l => Open(l.Element),
        MapType m => Open(m.Key) || Open(m.Value),
        DataType d => d.TypeArguments.Any(Open),
        UnionType u => u.Cases.Any(Open),
        FunctionType f => f.Parameters.Any(Open) || Open(f.Result),
        PointerType p => Open(p.Pointee),
        _ => false,
    };

    // K7, D16: every override of the method, the overrides of its overrides among them
    private List<FunctionSymbol> Overrides(FunctionSymbol method)
    {
        if (overrides is null)
        {
            overrides = new Dictionary<FunctionSymbol, List<FunctionSymbol>>(ReferenceEqualityComparer.Instance);
            var all = AllClasses().SelectMany(c => c.Methods.Values).Concat(AllData().SelectMany(d => d.Methods.Values)).SelectMany(list => list);
            foreach (var g in all)
            {
                for (var up = g.Overridden; up is not null; up = up.Overridden)
                {
                    if (!overrides.TryGetValue(up, out var list))
                    {
                        overrides[up] = list = [];
                    }

                    list.Add(g);
                }
            }
        }

        return overrides.TryGetValue(method, out var found) ? found : [];
    }

    // K7: the method of an interface runs the method of that name and arity of each class that implements the interface
    private IEnumerable<FunctionSymbol> Implementers(FunctionSymbol method) =>
        method.Interface is not { } face
            ? []
            : AllClasses().Where(c => c.Implements(face))
                .SelectMany(c => c.Chain)
                .SelectMany(c => c.Methods.TryGetValue(method.Name, out var list) ? list : [])
                .Where(g => !g.IsStatic && g.Parameters.Count == method.Parameters.Count);

    private static Dictionary<TypeParameterSymbol, KurzType> Merged(IReadOnlyDictionary<TypeParameterSymbol, KurzType>? map, DataType d)
    {
        var merged = map is null ? [] : new Dictionary<TypeParameterSymbol, KurzType>(map);
        foreach (var (parameter, argument) in d.Symbol.TypeParameters.Zip(d.TypeArguments))
        {
            merged.TryAdd(parameter, argument);
        }

        return merged;
    }

    private static Dictionary<TypeParameterSymbol, KurzType>? MapOf(DataType d) => d.TypeArguments.Count > 0 ? Merged(null, d) : null;

    // ---- the walk of a unit's code ------------------------------------------------------------------------------------------

    /// <summary>
    /// The walk of one unit's code: <paramref name="parameters"/> are those of the function it is, by their place, null for any other
    /// unit, and <paramref name="lambda"/> holds for a lambda's body, whose <c>this</c> and parameters are no origin the loops follow
    /// (U57).
    /// </summary>
    private sealed class Walk(ThrowSets sets, SemanticModel model, int file, Unit unit, List<VariableSymbol?>? parameters = null, bool lambda = false)
    {
        // O7: the cases a bare `throw` throws in the arm of `else` being walked, innermost on top
        private readonly Stack<IReadOnlyList<KurzType>> bare = new();

        // U57: each local without `mut` the walk declared so far, with where its value comes from
        private readonly Dictionary<VariableSymbol, Origin> locals = new(ReferenceEqualityComparer.Instance);

        public void Optional(Expression? e)
        {
            if (e is not null)
            {
                Expression(e);
            }
        }

        public void Expression(Expression e)
        {
            switch (e)
            {
                case IntegerLiteral or RealLiteral or UnitLiteral or CharLiteral or BoolLiteral or NullLiteral or ThisExpression:
                    break;
                case LambdaExpression l:
                    if (model.HasLambda(l))
                    {
                        unit.Edges.Add(new Edge(l, null, Runs: false)); // section 5: what its body builds, in the terms of this unit's type parameters
                    }

                    break;
                case StringLiteral s:
                    foreach (var hole in s.Segments.OfType<HoleSegment>())
                    {
                        Expression(hole.Expression);
                        if (Typed(hole.Expression) is { } type)
                        {
                            Text(type); // L6, A1: the text of a hole
                        }
                    }

                    break;
                case NameExpression or TypeMemberAccess:
                    Property(e);
                    break;
                case MemberAccess m:
                    Expression(m.Target);
                    Property(e);
                    break;
                case CallExpression c:
                    Call(c);
                    break;
                case ListLiteral l:
                    foreach (var item in l.Items)
                    {
                        Expression(item);
                    }

                    break;
                case IndexExpression i:
                    Expression(i.Target);
                    Expression(i.Index);
                    break;
                case UnaryExpression u:
                    Expression(u.Operand);
                    break;
                case BinaryExpression b:
                    Expression(b.Left);
                    Expression(b.Right);
                    break;
                case RangeExpression r:
                    Expression(r.Start);
                    Expression(r.End);
                    break;
                case MatchExpression m:
                    Match(m.Subject, m.Arms);
                    break;
                case WithExpression w:
                    Expression(w.Target);
                    foreach (var assignment in w.Assignments)
                    {
                        Expression(assignment.Value);
                    }

                    if (Typed(w) is DataType d)
                    {
                        // D16: the fields of the body, computed again for the new value, of its type or of a type derived from it
                        foreach (var x in sets.AllData().Where(x => x.Chain.Contains(d.Symbol)).SelectMany(x => x.Chain).Distinct())
                        {
                            unit.Edges.AddRange(x.Initializers.Values.Select(f => new Edge(f, ReferenceEquals(x, d.Symbol) ? MapOf(d) : null)));
                        }
                    }

                    break;
                case ElseExpression el:
                    Expression(el.Call);
                    Arms(el.Arms, Result(el.Call));
                    break;
                case ParenthesizedExpression p:
                    Expression(p.Inner);
                    break;
                case SpawnExpression s:
                    Call(s.Call);
                    if ((sets.reach && !model.HasCall(s.Call)) || model.CallOf(s.Call).Target is not ClassType { Symbol: var actor })
                    {
                        break; // in the reach mode, a spawn that reported its own error
                    }

                    unit.Sites.Add(new ActorSite(s.Call, actor, null, SiteKind.Spawn, Origin.Other, [], file, s.Line)); // U56: the spawn's edge
                    if (Policy(s, actor) is { Word: "escalate" or "block" } policy)
                    {
                        unit.Edges.Add(new Edge(new Root(actor), null, Crosses: true)); // U25, U28: the child's crash passes up whole, as a block can pass it up
                        if (policy.Block is { } block && sets.blocks && sets.skipped?.Contains(policy) != true)
                        {
                            Statement(block.BlockBody!); // U32: the block runs as a message of the actor whose code spawns, and its throw is that actor's crash
                        }
                    }

                    break;
                case AttemptExpression when sets.reach:
                    break; // U56: the block the checker said `not supported` to (section 6, U28), which runs no code a program builds
                default:
                    throw new InvalidOperationException($"the throw sets read no expression `{e.GetType().Name}` (line {e.Line})");
            }
        }

        public void Statement(Statement s)
        {
            switch (s)
            {
                case Block b:
                    foreach (var statement in b.Statements)
                    {
                        Statement(statement);
                    }

                    break;
                case VariableDeclaration v:
                    Optional(v.Value);
                    if (v.Value is { } value)
                    {
                        Local(v, value);
                    }

                    break;
                case AssignOrDeclare a:
                    Expression(a.Value);
                    Local(a, a.Value);
                    if (model.IsImplicitField(a, out var self, out var assigned) && assigned is not null && ActorOf(self.Type) is not null)
                    {
                        Write(assigned, a.Value); // T49 (c)
                    }

                    break;
                case Assignment a:
                    Expression(a.Target);
                    Expression(a.Value);
                    if (a.Target is IndexExpression { Target: var list } && FieldOf(list) is { Type: ListType } held)
                    {
                        Record(held, item: true, a.Value); // T49 (c): an item of the list a field holds
                    }
                    else
                    {
                        Write(FieldOf(a.Target), a.Value); // T49 (c): a field through `this`
                    }

                    break;
                case ExpressionStatement x:
                    Expression(x.Expression);
                    break;
                case IfStatement i:
                    Expression(i.Condition);
                    Statement(i.Then);
                    if (i.Else is { } otherwise)
                    {
                        Statement(otherwise);
                    }

                    break;
                case WhileStatement w:
                    Expression(w.Condition);
                    Statement(w.Body);
                    break;
                case ForInStatement f:
                    Expression(f.Collection);
                    if (sets.reach && model.Declares(f))
                    {
                        locals[model.DeclarationOf(f)] = ItemOf(OriginOf(f.Collection)); // T49 (c): the variable of a loop over a field's list holds its items
                    }

                    foreach (var body in model.PassesOf(f) ?? new[] { f.Body })
                    {
                        Statement(body); // T33: in a copy, a pass of the body for each field of the type
                    }

                    Arms(f.Arms ?? [], Result(f.Collection));
                    break;
                case MatchStatement m:
                    Match(m.Subject, m.Arms);
                    break;
                case BreakStatement or ContinueStatement or UseStatement or ConstAssertStatement or ExternBlock or FunctionDeclaration or FieldDeclaration or ConstructorDeclaration or TypeDeclaration or DecisionStatement:
                    break;
                case ReturnStatement r:
                    Optional(r.Value);
                    break;
                case ThrowStatement t:
                    Throw(t);
                    break;
                case RawBlock r:
                    Statement(r.Body);
                    break;
                case SendStatement send:
                    Call(send.Call);
                    break;
                case WithinStatement w:
                    Expression(w.Bound);
                    Statement(w.Body);
                    break;
                case AsmStatement a:
                    foreach (var operand in a.Operands)
                    {
                        Optional(operand.Value);
                        Optional(operand.Output);
                        if (operand.Body is { } body)
                        {
                            Statement(body);
                        }

                        if (operand.Word == "sym" && operand.Value is NameExpression n && model.IsFunctionName(n))
                        {
                            unit.Edges.Add(new Edge(model.FunctionNameOf(n), null)); // R11: the instructions may call the function a `sym` operand names
                        }
                    }

                    break;
                default:
                    throw new InvalidOperationException($"the throw sets read no statement `{s.GetType().Name}` (line {s.Line})");
            }
        }

        // O5, S16: the whole result of the call before `else` or of a loop's collection, whose cases after the success the arms take
        private UnionType? Result(Expression e) =>
            (e is ParenthesizedExpression p ? p.Inner : e) is CallExpression c && model.HasCall(c) ? model.CallOf(c).Result as UnionType : null;

        // C4, T33: the arms of a `match`; in a copy, the one arm of a `match` on `T.Kind` that the checker read
        private void Match(Expression subject, IReadOnlyList<MatchArm> arms)
        {
            Expression(subject);
            Arms(model.KindArmOf(subject) is { } taken ? [taken] : arms, null);
        }

        // the arms of a `match`, or of an `else` over the cases after the success of `result`, whose bare `throw` throws the case its arm took (O7)
        private void Arms(IReadOnlyList<MatchArm> arms, UnionType? result)
        {
            var taken = new List<KurzType>();
            foreach (var arm in arms)
            {
                IReadOnlyList<KurzType>? cases = null;
                switch (arm.Pattern)
                {
                    case ValuePattern v:
                        Expression(v.Value);
                        break;
                    case TypePattern tp:
                        if (result is not null && !sets.reach)
                        {
                            cases = [model.PatternTypeOf(tp)];
                            taken.Add(model.PatternTypeOf(tp));
                        }

                        break;
                    case ElsePattern:
                        if (result is not null && !sets.reach)
                        {
                            cases = result.Cases.Skip(1).Where(c => !taken.Any(t => Types.Same(t, c))).ToList();
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"the throw sets read no pattern `{arm.Pattern.GetType().Name}` (line {arm.Line})");
                }

                if (cases is not null)
                {
                    bare.Push(cases);
                }

                Statement(arm.Body);
                if (cases is not null)
                {
                    bare.Pop();
                }
            }
        }

        // O6, O7: the type a `throw` throws, or for a bare one the case its arm took; the text of the value adds nothing (section 6)
        private void Throw(ThrowStatement t)
        {
            if (sets.reach)
            {
                Optional(t.Value); // what the value's code reaches, and no throw
                return;
            }

            if (t.Value is { } value)
            {
                Expression(value);
                Thrown(model.TypeOf(value).Type, t.Line);
                return;
            }

            if (!bare.TryPeek(out var cases))
            {
                throw new InvalidOperationException($"a bare `throw` outside an arm of `else` at line {t.Line}: the checker reports it (O7)");
            }

            foreach (var type in cases)
            {
                Thrown(type, t.Line);
            }
        }

        private void Thrown(KurzType type, int line)
        {
            foreach (var one in Cases(type))
            {
                if (!unit.Own.Any(x => Types.Same(x.Type, one)))
                {
                    unit.Own.Add(new Thrown(one, file, line));
                }
            }
        }

        // K7: a read of a property is a call of its method, whose callee is the read itself, its target walked already
        private void Property(Expression read)
        {
            if (model.PropertyCallOf(read) is { } call)
            {
                Calls(call);
            }
        }

        private void Call(CallExpression c)
        {
            Expression(c.Callee);
            foreach (var argument in c.Arguments)
            {
                Expression(argument.Value);
                if (sets.reach && argument.Word == ArgumentWord.Mut && FieldOf(argument.Value) is { } field)
                {
                    unit.Writes.Add(new FieldWrite(field, false, Origin.Other, argument.Value.Line)); // T49 (c): a field the callee can write
                }
            }

            foreach (var bound in c.Clause())
            {
                Expression(bound); // U39: the clause's bound, after the arguments
            }

            Calls(c);
        }

        // what the call runs, its callee and its arguments aside
        private void Calls(CallExpression c)
        {
            if (model.IsMade(c, out var construction))
            {
                if (construction is not null)
                {
                    Call(construction); // T33: `T.Make(f => ...)` in a copy is the construction it became, its arguments the body for each field
                }

                return;
            }

            if (sets.reach && !model.HasCall(c))
            {
                return; // a call that reported its own error
            }

            var info = model.CallOf(c);
            var map = info.Map.Count > 0 ? info.Map : null;
            switch (info.Kind)
            {
                case CallKind.Function:
                    unit.Edges.Add(new Edge(info.Function!, map, Arguments: Slots(c, info)));
                    break;
                case CallKind.Method or CallKind.DataMethod:
                    {
                        if (sets.reach && info.Kind == CallKind.Method && c.Callee is MemberAccess { Target: var through } && Typed(through) is ActorType)
                        {
                            break; // a call through an actor's reference that reported its own error, recorded as the method it picked
                        }

                        if (info.Target is DataType { TypeArguments.Count: > 0 } d)
                        {
                            map = Merged(map, d);
                        }

                        var method = info.Function!;
                        var targets = sets.Overrides(method).Prepend(method)
                            .Concat(sets.Implementers(method).SelectMany(g => sets.Overrides(g).Prepend(g)))
                            .Distinct();
                        // D16: a data method's function takes the value first, a value of a data type, which is no actor's reference
                        Origin[] arguments = info.Kind == CallKind.Method ? Slots(c, info) : [Origin.Other, .. Slots(c, info)];
                        unit.Edges.AddRange(targets.Select(target => new Edge(target, map, Arguments: arguments)));
                        break;
                    }

                case CallKind.ClassNew:
                    {
                        var made = ((ClassType)info.Target!).Symbol;
                        unit.Edges.Add(new Edge(new Construction(made), null, Crosses: made.IsActor)); // U56: an actor's construction is the spawned actor's code
                        if (info.Constructor is { } constructor)
                        {
                            unit.Edges.Add(new Edge(constructor, null, Crosses: made.IsActor));
                        }

                        break;
                    }

                case CallKind.DataNew:
                    {
                        var made = Typed(c) as DataType ?? new DataType(info.Data!, []);
                        unit.Edges.Add(new Edge(new Made(made.Symbol), MapOf(made)));
                        if (made.Symbol.IsGeneric)
                        {
                            unit.Builds.Add(made); // section 5: an instantiation the code builds, in the terms of its own type parameters
                        }

                        break;
                    }

                case CallKind.Value:
                    if (Typed(c.Callee) is { } callee)
                    {
                        unit.Edges.AddRange(Functions(callee).Select(type => new Edge(new Valued(type), null)));
                        unit.Edges.AddRange(CFunctions(callee).Select(type => new Edge(new Native(type), null))); // R17
                    }

                    break;
                case CallKind.Print or CallKind.ErrorPrint:
                    foreach (var argument in c.Arguments)
                    {
                        if (Typed(argument.Value) is { } type)
                        {
                            Text(type); // A1, A17
                        }
                    }

                    break;
                case CallKind.StringJoin:
                    foreach (var argument in c.Arguments)
                    {
                        if (Typed(argument.Value) is ListType list)
                        {
                            Text(list.Element); // X6: the text of each item
                        }
                    }

                    break;
                case CallKind.ActorCall or CallKind.ActorSend:
                    // U20: the callee's code runs in the callee, whose crash its caller gets as `callee-crashed`; U56: the call's edge, with where
                    // its receiver comes from, the actor's own for a `send` of its own method (U11), and each argument by its parameter
                    unit.Sites.Add(new ActorSite(c, ((ActorType)info.Target!).Symbol, info.Function, info.Kind == CallKind.ActorCall ? SiteKind.Wait : SiteKind.Send,
                        c.Callee is MemberAccess m ? OriginOf(m.Target) : Origin.Self, Slots(c, info), file, c.Line));
                    break;
                case CallKind.ListAdd or CallKind.ListInsert:
                    // M9, X11: `Add` and `Insert` keep the function value they take and run none of it, so a lambda there brings what its
                    // walk gave it, what it builds and nothing it throws, and a call of the value it became reaches the lambda; T49 (c): the
                    // item they put into the list a field holds, the last argument
                    if (sets.reach && c.Callee is MemberAccess { Target: var addedTo } && FieldOf(addedTo) is { } added && c.Arguments.Count > 0)
                    {
                        Record(added, item: true, c.Arguments[^1].Value);
                    }

                    break;
                case CallKind.ListAddRange when sets.reach && c.Callee is MemberAccess { Target: var rangedTo } && FieldOf(rangedTo) is { } ranged:
                    unit.Writes.Add(new FieldWrite(ranged, true, Origin.Other, c.Line)); // T49 (c): the items of another list
                    goto default;
                default:
                    // M9, X8: a built-in call that takes a function value calls it, as `Where` calls its test and `SortBy` its key for each
                    // item, but `Add` and `Insert` above, so that a kind no arm names counts what the function throws, too much rather than
                    // too little
                    foreach (var argument in c.Arguments)
                    {
                        if (argument.Value is LambdaExpression lambda && model.HasLambda(lambda))
                        {
                            unit.Edges.Add(new Edge(lambda, null));
                        }
                        else if (model.HasType(argument.Value))
                        {
                            unit.Edges.AddRange(Functions(model.TypeOf(argument.Value).Type).Select(type => new Edge(new Valued(type), null)));
                        }
                    }

                    break;
            }
        }

        // the type the checker gave an expression; in the reach mode null where it gave none, a node of a reported error
        private KurzType? Typed(Expression e) => !sets.reach || model.HasType(e) ? model.TypeOf(e).Type : null;

        // U57: a local that a declaration makes holds its value's origin, which a read of it takes where it has no `mut` (OriginOf)
        private void Local(object declaration, Expression value)
        {
            if (model.Declares(declaration) && model.DeclarationOf(declaration) is { Kind: VariableKind.Local } variable)
            {
                locals[variable] = OriginOf(value);
            }
        }

        /// <summary>
        /// U57: where a reference comes from: <c>this</c> as a value inside an actor, its own reference (U10), or a local without
        /// <c>mut</c> that holds it, is the actor itself; a parameter of the function, which no <c>mut</c> lets change, is that
        /// parameter, by its place; a field of the actor, read by its name or through <c>this</c>, is that field, and an item of the
        /// list it holds, read by an index or as the variable of a <c>for</c> over it, an item of it (T49 (c)); anything else, a field of
        /// a class, a capture in a lambda, a call's result, is other. In a lambda nothing is the actor's, a parameter, a field or a local of the code around it, since its body
        /// is a unit read by a walk of its own, whose locals hold none of that code's: <c>this</c> inside a lambda and a capture are none
        /// (the owner, 2026-11-20, #519, comment 7051071630: T61 (a) and T49 (c)), and so are a parameter and a field read in a lambda
        /// (assumed: a reading of T61 (a) of comment 7051071630).
        /// </summary>
        private Origin OriginOf(Expression e)
        {
            switch (e)
            {
                case ParenthesizedExpression p:
                    return OriginOf(p.Inner);
                case ThisExpression t when !lambda && Typed(t) is ActorType:
                    return Origin.Self;
                case NameExpression n when model.IsVariable(n) && model.VariableOf(n) is { Mut: false } v:
                    var at = parameters?.IndexOf(v) ?? -1;
                    return at >= 0 && !lambda ? new Origin(at) : locals.TryGetValue(v, out var held) ? held : Origin.Other;
                case NameExpression or MemberAccess when !lambda && FieldOf(e) is { } field:
                    return Origin.Of(field);
                case IndexExpression i:
                    return ItemOf(OriginOf(i.Target));
                default:
                    return Origin.Other;
            }
        }

        // T49 (c): an item of the list a field of the actor's body holds, other for anything else
        private static Origin ItemOf(Origin list) => list is { Field.Type: ListType, Item: false } ? Origin.Of(list.Field!, item: true) : Origin.Other;

        // T49 (c): the field of the actor an expression names, by its name inside a method or through `this`, in a lambda too; null for any
        // other expression, a field of a class among them
        private FieldSymbol? FieldOf(Expression e)
        {
            switch (e)
            {
                case ParenthesizedExpression p:
                    return FieldOf(p.Inner);
                case NameExpression n when model.IsImplicitField(n, out var self, out var field) && field is not null && ActorOf(self.Type) is not null:
                    return field;
                case MemberAccess { Target: ThisExpression t } m when Typed(t) is { } type && ActorOf(type)?.Field(m.Member) is { } field:
                    return field;
                default:
                    return null;
            }
        }

        private static ClassSymbol? ActorOf(KurzType type) => type switch
        {
            ActorType a => a.Symbol,
            ClassType { Symbol.IsActor: true } c => c.Symbol,
            _ => null,
        };

        /// <summary>
        /// T49 (c): the default of a field of an actor's body, a write of it in the construction, which reads no <c>this</c> (V10).
        /// </summary>
        public void Default(ClassSymbol owner, FieldSymbol field, Expression value)
        {
            if (owner.IsActor && field.Body is not null)
            {
                Write(field, value);
            }
        }

        // T49 (c): a write of a field of the actor with the value: a list literal puts each of its items into the list, an empty list
        // and `null` hold no reference, and a list of another origin is other; in a lambda every value is other
        private void Write(FieldSymbol? field, Expression value)
        {
            if (!sets.reach || field is null)
            {
                return;
            }

            if (field.Type is ListType)
            {
                switch (value)
                {
                    case ListLiteral l:
                        foreach (var item in l.Items)
                        {
                            Record(field, item: true, item);
                        }

                        return;
                    case CallExpression c when model.HasCall(c) && model.CallOf(c).Kind == CallKind.ListNew && c.Arguments.Count == 0:
                        return;
                    default:
                        unit.Writes.Add(new FieldWrite(field, false, Origin.Other, value.Line));
                        return;
                }
            }

            if (value is not NullLiteral)
            {
                Record(field, item: false, value);
            }
        }

        // T49 (c): a write of the field or of an item of its list, with where the value comes from; in a lambda, other
        private void Record(FieldSymbol field, bool item, Expression value) =>
            unit.Writes.Add(new FieldWrite(field, item, lambda ? Origin.Other : OriginOf(value), value.Line));

        // U57: where each argument of the call comes from, by the parameter it fills (D11), other for a parameter it leaves to its default
        private Origin[] Slots(CallExpression c, CallInfo info)
        {
            var slots = Enumerable.Repeat(Origin.Other, info.SlotCount).ToArray();
            for (var i = 0; i < c.Arguments.Count && i < info.Binding.Count; i++)
            {
                if (info.Binding[i] is var slot && slot >= 0 && slot < slots.Length)
                {
                    slots[slot] = OriginOf(c.Arguments[i].Value);
                }
            }

            return slots;
        }

        private static IEnumerable<FunctionType> Functions(KurzType type) => type switch
        {
            FunctionType f => [f],
            UnionType u => u.Cases.OfType<FunctionType>(),
            _ => [],
        };

        private static IEnumerable<CFunctionType> CFunctions(KurzType type) => type switch
        {
            CFunctionType f => [f],
            UnionType u => u.Cases.OfType<CFunctionType>(),
            _ => [],
        };

        // A3, A4, A5, A9, A10: the Text() that the text of a value of the type calls, of the type, of every type derived from it, and of
        // every type its text reads
        private void Text(KurzType type) => Text(type, []);

        /// <summary>The Text() that the text of a value of an instantiation the program builds calls (section 5).</summary>
        public void TextOf(DataType made) => Text(made);

        // `seen` holds each data type the text read, an instantiation with its type arguments, so that a second instantiation of one
        // generic type in one text, `Box<int>` and `Box<Quiet>`, is read too (section 5)
        private void Text(KurzType type, List<DataType> seen)
        {
            switch (type)
            {
                case ClassType c:
                    TextMethods(sets.AllClasses().Where(x => x.HasBaseOrIs(c.Symbol)).Select(x => x.TextMethod));
                    break;
                case InterfaceType face:
                    TextMethods(sets.AllClasses().Where(x => x.Implements(face.Symbol)).Select(x => x.TextMethod));
                    break;
                case WeakRefType w:
                    Text(w.Target, seen);
                    break;
                case DataType d:
                    foreach (var x in sets.AllData().Where(x => x.Chain.Contains(d.Symbol)))
                    {
                        var read = ReferenceEquals(x, d.Symbol) ? d : new DataType(x, []);
                        if (seen.Any(s => Types.Same(s, read)))
                        {
                            continue;
                        }

                        seen.Add(read);
                        if (!ReferenceEquals(x, d.Symbol) && x.IsGeneric)
                        {
                            unit.Edges.Add(new Edge(new Each(x), null)); // section 5: the text of each instantiation of it that the program builds
                            continue;
                        }

                        var map = ReferenceEquals(x, d.Symbol) ? MapOf(d) : null;
                        if (x.TextMethod is { } own)
                        {
                            unit.Edges.Add(new Edge(own, map));
                        }
                        else
                        {
                            foreach (var field in x.Fields)
                            {
                                Text(map is null ? field.Type : Types.Substitute(field.Type, map), seen);
                            }
                        }
                    }

                    break;
                case UnionType u:
                    foreach (var one in u.Cases)
                    {
                        Text(one, seen);
                    }

                    break;
                case ListType l:
                    Text(l.Element, seen);
                    break;
                case MapType m:
                    Text(m.Key, seen);
                    Text(m.Value, seen);
                    break;
                case TypeParameterType when sets.reach:
                    break; // A10: `no-text`, which the checker reported
                case TypeParameterType p:
                    throw new InvalidOperationException($"the text of the type parameter `{p.Symbol.Name}`, whose argument the walk does not see: no program the checker reads reaches it, since the checker reports the text of a type parameter as `no-text` (A10), and the walk reads a generic data type derived from the type it makes text of only for each instantiation the program builds, which names every type argument (section 5)");
                default:
                    break; // a text the runtime makes, which runs no code of the program
            }
        }

        private void TextMethods(IEnumerable<FunctionSymbol?> methods) =>
            unit.Edges.AddRange(methods.OfType<FunctionSymbol>().Distinct().Select(m => new Edge(m, null)));

        // U25, U27: the policy a spawn gives its actor: its own, else the type's default, null where none is written, the default `restart`
        private static CrashPolicy? Policy(SpawnExpression s, ClassSymbol actor) =>
            s.Policy ?? actor.Declaration.Clauses?.FirstOrDefault(c => c.Policy is not null)?.Policy;
    }
}

/// <summary>
/// U57: where the reference a call goes through, or an argument it passes, comes from in the unit of code that holds it: the actor
/// itself (<see cref="Self"/>), a parameter of the unit's function by its place, a field of the actor or an item of the list
/// such a field holds (<see cref="Of"/>), or anything else (<see cref="Other"/>).
/// </summary>
public readonly record struct Origin(int Parameter, FieldSymbol? Field = null, bool Item = false)
{
    /// <summary><c>this</c> as a value inside an actor, its own reference (U10), or a local without <c>mut</c> that holds it.</summary>
    public static readonly Origin Self = new(-1);

    /// <summary>A field of a class or a data type, an item of a collection a field of an actor does not hold, a capture in a lambda, a call's result, or a local with <c>mut</c>.</summary>
    public static readonly Origin Other = new(-2);

    /// <summary>A field of the actor (T49 (c)), or with <paramref name="item"/> an item of the list it holds.</summary>
    public static Origin Of(FieldSymbol field, bool item = false) => new(-3, field, item);
}

/// <summary>
/// U57, T49 (c): a write of a field of an actor that the actor's code makes: an assignment of the field, or with
/// <see cref="Item"/> an item put into the list it holds, by <c>Add</c>, <c>Insert</c>, an assignment of an item or a list literal
/// assigned to it, and where the value comes from in the unit's terms; a write the loops cannot follow, a field passed to a <c>mut</c>
/// parameter or <c>AddRange</c> among them, takes <see cref="Origin.Other"/>.
/// </summary>
public sealed record FieldWrite(FieldSymbol Field, bool Item, Origin Value, int Line);

/// <summary>What a site of an actor's code is (U56): a call that waits (U4), a <c>send</c> (U6) or a <c>spawn</c> (U2).</summary>
public enum SiteKind
{
    Wait,
    Send,
    Spawn,
}

/// <summary>
/// A call through an actor's reference, or a <c>spawn</c>, as the walk met it in a unit of code (U56): the call, the actor it reaches or
/// makes, the method it calls (null for a <c>spawn</c>), where its receiver and each of its arguments, by the parameter it fills, come
/// from in that unit (U57), the file it stands in, 0 for the program's and 1 for <c>std/</c>, and its line.
/// </summary>
public sealed record ActorSite(CallExpression Call, ClassSymbol Actor, FunctionSymbol? Method, SiteKind Kind, Origin Receiver, IReadOnlyList<Origin> Arguments, int File, int Line)
{
    /// <summary>U58: the line of the word <c>vouch</c> before the call, 0 where none stands.</summary>
    public int Vouch => Call.Vouch;
}

/// <summary>A site an entry of an actor's code reaches: <see cref="Through"/> names the unit it stands in where that is not the entry itself, <see cref="Receivers"/> holds the origins its receiver can have in the entry's terms, and <see cref="Arguments"/> those of each of its arguments, by the parameter it fills (U57).</summary>
public sealed record ReachedSite(ActorSite Site, string? Through, IReadOnlyCollection<Origin> Receivers, IReadOnlyList<IReadOnlyCollection<Origin>> Arguments);

/// <summary>A write of a field an entry reaches, with the origins its value can have in the entry's terms (U57, T49 (c)).</summary>
public sealed record ReachedWrite(FieldWrite Write, IReadOnlyCollection<Origin> Values);

/// <summary>
/// An entry of an actor's code (U56): its construction, whose <see cref="Method"/> is null, or a method it declares, with the sites its code
/// reaches, the writes of its actor's fields it reaches, and whether code of the program calls it directly, as its own actor's code calls
/// its method (U10).
/// </summary>
public sealed record ActorEntry(string Name, FunctionSymbol? Method, IReadOnlyList<ReachedSite> Sites, IReadOnlyList<ReachedWrite> Writes, bool Direct);

/// <summary>The code of an actor by its entries (U56).</summary>
public sealed record ActorCode(ClassSymbol Actor, IReadOnlyList<ActorEntry> Entries);
