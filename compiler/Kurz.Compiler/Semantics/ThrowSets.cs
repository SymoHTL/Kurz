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
/// program builds are found as Rust's monomorphization finds its instances: from the roots, the top-level code, every <c>pub</c>
/// function, a generic one and each actor's <c>pub</c> methods among them, and every test (Y5), through the type arguments each call gives, with the fixed
/// point's edges, a lambda's in the unit that makes it, and each with no type parameter left, read again with the text of each
/// instantiation found until they grow no more. A call through an actor's
/// reference reaches none of the callee's code, whose crash its caller gets as <c>callee-crashed</c> (U20). The text of a thrown value
/// adds nothing, since a raise while it is made ends the process (section 6), and <c>==</c>, <c>equal by</c> and a hash run no code of the program, which declares no <c>Equals</c> and no
/// <c>GetHashCode</c> (K5, K8, D16). A set is in the order of the first <c>throw</c> of each type that the code reaches, the program's
/// file before <c>std/</c> and by line, with the runtime's type last (section 5, assumed there). No build reads the sets; the tests do.
/// </summary>
public sealed class ThrowSets
{
    private readonly Checker program;
    private readonly Checker? std;
    private readonly bool blocks;
    private readonly IReadOnlySet<CrashPolicy>? skipped;
    private readonly IReadOnlySet<AttemptExpression>? skippedArms;
    private readonly Dictionary<object, Unit> units = new(new UnitKeys());
    private readonly List<Unit> order = [];
    private readonly KurzType runtimeError;
    private List<(LambdaExpression Lambda, FunctionType Type, SemanticModel Model, int File)>? lambdas;
    private Dictionary<FunctionSymbol, List<FunctionSymbol>>? overrides;
    private List<DataType>? instantiations;
    private List<(object Key, Unit Unit)>? waiting;

    /// <summary>
    /// The sets of the code of <paramref name="program"/>, which sees the types of <paramref name="std"/> (W1); null where the program is
    /// <c>std/</c> itself. Where <paramref name="blocks"/> does not hold, the bodies of the blocks of <c>on crash</c> are left out, as at the
    /// checker's first reading of such a program, which reads them with no <c>Reason</c> found yet (U30), and so are those of the blocks
    /// <paramref name="skipped"/> names, which reported errors at a reading whose <c>Reason</c>s may not have settled; so are the arms of
    /// the <c>else</c> of every <c>attempt</c> there, and of those <paramref name="skippedArms"/> names, for the same reasons (U46).
    /// </summary>
    public ThrowSets(Checker program, Checker? std, bool blocks = true, IReadOnlySet<CrashPolicy>? skipped = null, IReadOnlySet<AttemptExpression>? skippedArms = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        this.program = program;
        this.std = std;
        this.blocks = blocks;
        this.skipped = skipped;
        this.skippedArms = skippedArms;
        var error = (std ?? program).Model.DataTypes.FirstOrDefault(d => d.Name == "RuntimeError")
            ?? throw new InvalidOperationException("std/ declares no `RuntimeError`, the runtime's error type of section 5");
        runtimeError = new DataType(error, []);
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
        [typeof(CallExpression)] = "its callee, its arguments, the bound of its clause and what it calls",
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
        [typeof(SpawnExpression)] = "the actor's construction, its code under escalate or a block, and the block's body",
        [typeof(AttemptExpression)] = "nothing where it runs but what its block builds, whose throws its child's crash holds; `retry`: no code, the checker refuses it",
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
        [typeof(TestDeclaration)] = "no code where it stands: its body is the code of the test's own actor under kurz test",
        [typeof(AssertionStatement)] = "its condition, and the Text() of each side of the comparison its failure shows",
        [typeof(MockStatement)] = "no code: the checker refuses it",
        [typeof(AdvanceStatement)] = "no code: the checker refuses it",
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

    /// <summary>
    /// U46: the <c>Reason</c> of the <c>Crashed</c> of an <c>attempt</c> of the program's file: every type the code of its block can throw,
    /// with what that code reaches, then the runtime's error type.
    /// </summary>
    public IReadOnlyList<KurzType> ReasonOf(AttemptExpression attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return program.Model.HasAttempt(attempt) ? Solved(attempt) : throw new ArgumentException($"no `attempt` the checker read stands at line {attempt.Line}", nameof(attempt));
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

    private List<KurzType> Solved(object root)
    {
        Instantiations();
        var unit = Discover(root);
        Solve();
        var types = unit.Set.OrderBy(t => (t.File, t.Line)).Select(t => t.Type).ToList();
        types.RemoveAll(t => Types.Same(t, runtimeError));
        types.Add(runtimeError);
        return types;
    }

    // ---- the units and their fixed point ----------------------------------------------------------------------------------

    /// <summary>A thrown type, with the place of the first <c>throw</c> of it that the code reaches: the file, 0 for the program's and 1 for <c>std/</c>, and the line.</summary>
    private sealed record Thrown(KurzType Type, int File, int Line);

    /// <summary>
    /// An edge from a unit to a unit it runs, with the type arguments the call gives the type parameters of that unit (T19); or, where it
    /// does not run it, from a unit to a lambda it makes, which brings what the lambda builds and nothing it throws (section 5), as Rust
    /// collects a closure's instance in the instance of the function that makes it.
    /// </summary>
    private sealed record Edge(object Unit, IReadOnlyDictionary<TypeParameterSymbol, KurzType>? Map, bool Runs = true);

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
        public List<Thrown> Own { get; } = [];

        public List<Edge> Edges { get; } = [];

        public List<Thrown> Set { get; } = [];

        public List<DataType> Builds { get; } = [];

        public List<DataType> Built { get; } = [];
    }

    /// <summary>The keys of the units: a lambda and an <c>attempt</c> by its node, never by its text, and every other key by its own equality.</summary>
    private sealed class UnitKeys : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) =>
            x is LambdaExpression or AttemptExpression || y is LambdaExpression or AttemptExpression ? ReferenceEquals(x, y) : object.Equals(x, y);

        public int GetHashCode(object obj) =>
            obj is LambdaExpression or AttemptExpression ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj) : obj.GetHashCode();
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
    // code alone, and its construction from a `spawn` that a root reaches; and each test of the file, which the build of `kurz test`
    // runs as a function of its own (Y5), as Rust's `cargo test` builds each `#[test]` function as a root of its harness, which Rust
    // does in the test build alone, where Kurz takes the tests as roots in every reading, since the checker reads every test whatever
    // command the build is for (Y7), so that a `Reason` is one set under every command
    private IEnumerable<object> Roots() => Pub().Concat(program.Model.Tests.Select(program.Model.TestOf)).Prepend<object>(new TopLevel(program.Model));

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
                    var walk = new Walk(this, model, file, unit);
                    walk.Optional(l.ExpressionBody);
                    if (l.BlockBody is { } block)
                    {
                        walk.Statement(block);
                    }

                    break;
                }

            case AttemptExpression a:
                {
                    // U46: the block's code, which its child runs; an `attempt` stands in the program's file alone, `std/` holding none
                    var block = program.Model.AttemptOf(a).Lambda;
                    var walk = new Walk(this, program.Model, 0, unit);
                    walk.Optional(block.ExpressionBody);
                    if (block.BlockBody is { } body)
                    {
                        walk.Statement(body);
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

        var walk = new Walk(this, site.Model, site.File, unit);
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

    private sealed class Walk(ThrowSets sets, SemanticModel model, int file, Unit unit)
    {
        // O7: the cases a bare `throw` throws in the arm of `else` being walked, innermost on top
        private readonly Stack<IReadOnlyList<KurzType>> bare = new();

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
                        Text(model.TypeOf(hole.Expression).Type); // L6, A1: the text of a hole
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

                    if (model.TypeOf(w).Type is DataType d)
                    {
                        // D16: the fields of the body, computed again for the new value, of its type or of a type derived from it
                        foreach (var x in sets.AllData().Where(x => x.Chain.Contains(d.Symbol)).SelectMany(x => x.Chain).Distinct())
                        {
                            unit.Edges.AddRange(x.Initializers.Values.Select(f => new Edge(f, ReferenceEquals(x, d.Symbol) ? MapOf(d) : null)));
                        }
                    }

                    break;
                case ElseExpression el when (el.Call is ParenthesizedExpression p ? p.Inner : el.Call) is AttemptExpression a:
                    Expression(a);
                    if (sets.blocks && sets.skippedArms?.Contains(a) != true && model.HasAttempt(a))
                    {
                        Arms(el.Arms, model.AttemptOf(a).Type); // U46: the arms are the code of the actor that waits, at a reading whose `Reason`s may settle
                    }

                    break;
                case ElseExpression el:
                    Expression(el.Call);
                    Arms(el.Arms, Result(el.Call));
                    break;
                case ParenthesizedExpression p:
                    Expression(p.Inner);
                    break;
                case AttemptExpression a:
                    if (model.HasAttempt(a))
                    {
                        unit.Edges.Add(new Edge(a, null, Runs: false)); // U46: what its block builds, and nothing it throws, which `Crashed` holds
                    }

                    break;
                case SpawnExpression s:
                    Call(s.Call);
                    if (model.CallOf(s.Call).Target is ClassType { Symbol: var actor } && Policy(s, actor) is { Word: "escalate" or "block" } policy)
                    {
                        unit.Edges.Add(new Edge(new Root(actor), null)); // U25, U28: the child's crash passes up whole, as a block can pass it up
                        if (policy.Block is { } block && sets.blocks && sets.skipped?.Contains(policy) != true)
                        {
                            Statement(block.BlockBody!); // U32: the block runs as a message of the actor whose code spawns, and its throw is that actor's crash
                        }
                    }

                    break;
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
                    break;
                case AssignOrDeclare a:
                    Expression(a.Value);
                    break;
                case Assignment a:
                    Expression(a.Target);
                    Expression(a.Value);
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
                case TestDeclaration or MockStatement or AdvanceStatement:
                    break;
                case AssertionStatement a:
                    Expression(a.Condition);
                    if (model.AssertionOf(a) is { Left: { } left, Right: { } right })
                    {
                        Text(left); // Y6: the text of each side its failure shows, made by the actor that runs it
                        Text(right);
                    }

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
                        if (result is not null)
                        {
                            cases = [model.PatternTypeOf(tp)];
                            taken.Add(model.PatternTypeOf(tp));
                        }

                        break;
                    case ElsePattern:
                        if (result is not null)
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

            var info = model.CallOf(c);
            var map = info.Map.Count > 0 ? info.Map : null;
            switch (info.Kind)
            {
                case CallKind.Function:
                    unit.Edges.Add(new Edge(info.Function!, map));
                    break;
                case CallKind.Method or CallKind.DataMethod:
                    {
                        if (info.Target is DataType { TypeArguments.Count: > 0 } d)
                        {
                            map = Merged(map, d);
                        }

                        var method = info.Function!;
                        var targets = sets.Overrides(method).Prepend(method)
                            .Concat(sets.Implementers(method).SelectMany(g => sets.Overrides(g).Prepend(g)))
                            .Distinct();
                        unit.Edges.AddRange(targets.Select(target => new Edge(target, map)));
                        break;
                    }

                case CallKind.ClassNew:
                    unit.Edges.Add(new Edge(new Construction(((ClassType)info.Target!).Symbol), null));
                    if (info.Constructor is { } constructor)
                    {
                        unit.Edges.Add(new Edge(constructor, null));
                    }

                    break;
                case CallKind.DataNew:
                    {
                        var made = model.TypeOf(c).Type as DataType ?? new DataType(info.Data!, []);
                        unit.Edges.Add(new Edge(new Made(made.Symbol), MapOf(made)));
                        if (made.Symbol.IsGeneric)
                        {
                            unit.Builds.Add(made); // section 5: an instantiation the code builds, in the terms of its own type parameters
                        }

                        break;
                    }

                case CallKind.Value:
                    unit.Edges.AddRange(Functions(model.TypeOf(c.Callee).Type).Select(type => new Edge(new Valued(type), null)));
                    unit.Edges.AddRange(CFunctions(model.TypeOf(c.Callee).Type).Select(type => new Edge(new Native(type), null))); // R17
                    break;
                case CallKind.Print or CallKind.ErrorPrint:
                    foreach (var argument in c.Arguments)
                    {
                        Text(model.TypeOf(argument.Value).Type); // A1, A17
                    }

                    break;
                case CallKind.StringJoin:
                    foreach (var argument in c.Arguments)
                    {
                        if (model.TypeOf(argument.Value).Type is ListType list)
                        {
                            Text(list.Element); // X6: the text of each item
                        }
                    }

                    break;
                case CallKind.ActorCall or CallKind.ActorSend:
                    if (info.ByName)
                    {
                        // U52: a message to the name of a `per node` actor starts it where none stands, its constructor run on the caller's
                        // stack, whose throw is the caller's crash, as at a `spawn`
                        unit.Edges.Add(new Edge(new Construction(((ActorType)info.Target!).Symbol), null));
                    }

                    break; // U20: the callee's code runs in the callee, whose crash its caller gets as `callee-crashed`
                case CallKind.ListAdd or CallKind.ListInsert:
                    // M9, X11: `Add` and `Insert` keep the function value they take and run none of it, so a lambda there brings what its
                    // walk gave it, what it builds and nothing it throws, and a call of the value it became reaches the lambda
                    break;
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
