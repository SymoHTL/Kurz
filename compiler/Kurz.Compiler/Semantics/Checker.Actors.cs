using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Chapter 19: an <c>actor</c>, a class's body whose state nothing outside it reaches (U1, U8), made by <c>spawn</c> (U2), whose
/// reference, an <see cref="ActorType"/>, takes calls of its <c>pub</c> methods as messages: a call waits for its result (U4) and one
/// after <c>send</c> does not (U6), and a method with a result after <c>send</c> is <c>send-result</c> (U6, U7). Inside the actor its
/// own methods and fields are direct, as a class's are (U10), and <c>send</c> of its own method is a message (U11). What crosses into a
/// message and back out of it is read by its type, <c>send-needs-word</c> where it does not cross, <c>pointer-crosses</c> where it reaches
/// a pointer or a view of an atomic type (U12, <see cref="CrossingOf"/>). What
/// else an actor can write, and what the build does not make of it, is <c>not supported</c>, each naming its rule
/// (<c>compiler/README.md</c>).
/// </summary>
public sealed partial class Checker
{
    /// <summary>U6: the call after the <c>send</c> being checked, which no one waits for.</summary>
    private CallExpression? sending;

    /// <summary>U2: the call after the <c>spawn</c> being checked, the one place an actor's name makes its state.</summary>
    private CallExpression? spawning;

    /// <summary>U10: each <c>this</c> in an actor's methods that stands before a member, the one place it is read.</summary>
    private readonly HashSet<ThisExpression> actorMemberThis = new(ReferenceEqualityComparer.Instance);

    /// <summary>U1: each actor the checker does not read, whose one diagnostic <see cref="RefuseUnreadActors"/> gives once every type is resolved.</summary>
    private readonly List<TypeDeclaration> unreadActors = [];

    /// <summary>U1: the name of each actor the file declares, read or not, which no type after an actor's name may name.</summary>
    private readonly HashSet<string> actorNames = new(StringComparer.Ordinal);

    /// <summary>U28, U30: each block of <c>on crash</c> the checker read, with the actor whose crash it decides and the <c>Reason</c> it read of it.</summary>
    private readonly List<(CrashPolicy Policy, ClassSymbol Child, IReadOnlyList<KurzType> Read)> crashBlocks = [];

    /// <summary>U30: the errors the blocks of <c>on crash</c> reported at a reading after the first, and the blocks that reported them.</summary>
    private readonly HashSet<Diagnostic> blockErrors = new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<CrashPolicy> erringBlocks = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// U1: an actor the checker reads is declared as a class is, its body read by the class's code; an actor with a part the build does
    /// not read, or with <c>equal by</c> or a constructor in its body, which no rule gives a meaning (U2), joins the types the checker does
    /// not read (E1), its one diagnostic given by <see cref="RefuseUnreadActors"/>.
    /// </summary>
    private void DeclareActor(TypeDeclaration d)
    {
        actorNames.Add(d.Name);
        if (UnreadActorPart(d) is not null || d.EqualBy.Count > 0 || (d.Members ?? []).OfType<ConstructorDeclaration>().Any())
        {
            unreadActors.Add(d);
            unsupportedTypes.Add(d.Name);
            unsupportedBases[d.Name] = "an actor a data type names (U1)";
            return;
        }

        CheckStackClause(d);
        DeclareClass(d);
        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case FunctionDeclaration { Property: true } p when (p.Modifiers & Modifiers.Pub) != 0:
                    Report(ErrorIds.ActorFieldPublic, p.Line, $"`{p.Name}` is a `pub` property of the actor `{d.Name}`, whose state nothing outside it reaches: a `pub` method gives it (U8)");
                    break;
                case FieldDeclaration f when (f.Modifiers & Modifiers.Pub) != 0:
                    Report(ErrorIds.ActorFieldPublic, f.Line, $"`{f.Name}` is a `pub` field of the actor `{d.Name}`, whose state nothing outside it reaches: a `pub` method gives it (U8)");
                    break;
            }
        }

        foreach (var f in (d.Members ?? []).OfType<FunctionDeclaration>())
        {
            SyntaxCopy.Walk(f, e =>
            {
                if (e is MemberAccess { Target: ThisExpression t })
                {
                    actorMemberThis.Add(t);
                }
            });
        }
    }

    /// <summary>
    /// U1: the literal of bytes of the <c>stack</c> clause, read as a <c>long</c> (L14), whose error, <c>inexact-literal</c> or a size
    /// past a <c>long</c>, it reports; whether it reported one. The size is a lower bound the runtime rounds up to its page and raises to
    /// its floor with no message (section 6, the owner, 2026-11-15, #519, comment 6892835500, Q7, "Rust/C#: round up silently"), so no
    /// size the literal can hold is an error of its own; and the values of the type's policy (U27, <see cref="CheckPolicy"/>), each clause in
    /// the order it is written.
    /// </summary>
    private bool CheckStackClause(TypeDeclaration d)
    {
        foreach (var clause in d.Clauses ?? [])
        {
            if (clause.Size is { } size && Types.IsError(CheckExpression(size, Types.Long).Type))
            {
                return true;
            }

            if (clause.Policy is { } policy && CheckPolicy(policy))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first part of an actor's declaration that the build does not read beyond a class's, with its line and what it is; null where
    /// there is none: a clause, a modifier, type parameters, a type after its name and a <c>static</c> member (U1), and a modifier of a
    /// method but <c>pub</c> (U9). A type after the name is named here as an interface, since <see cref="RefuseUnreadActors"/>
    /// reads every such type once the types are resolved and leaves an actor to this part only where each is one; <c>equal by</c> and a
    /// constructor in the body are no part of it, being <c>syntax</c> (U2; Q26 of issue 519, comment 6882624606).
    /// </summary>
    private static (int Line, string Construct)? UnreadActorPart(TypeDeclaration d)
    {
        if ((d.Clauses ?? []).Any(c => c.Word == "per") && (d.Clauses ?? []).FirstOrDefault(c => c.Policy is { Block: not null }) is { } block)
        {
            return (block.Line, "the block `on crash (c) { ... }` after the name of a `per` actor (U27, U28)");
        }

        if ((d.Clauses ?? []).FirstOrDefault(c => c.Word != "stack" && c.Policy is null) is { } clause)
        {
            return (clause.Line, $"the clause `{clause.Word}` after an actor's name (U1)");
        }

        if ((d.Modifiers & ~Modifiers.Pub) != 0)
        {
            return (d.Line, $"the modifier `{(d.Modifiers & ~Modifiers.Pub).ToString().ToLowerInvariant()}` of an actor (U1)");
        }

        if (d.TypeParameters.Count > 0)
        {
            return (d.Line, "a generic actor (U1, T19)");
        }

        if (d.Bases.Count > 0)
        {
            return (d.Bases[0].Line, "an interface after an actor's name (U1)");
        }

        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case FieldDeclaration f when (f.Modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` field of an actor (U1)");
                case FunctionDeclaration f when (f.Modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` method of an actor (U1)");
                case FunctionDeclaration f when (f.Modifiers & ~Modifiers.Pub) != 0:
                    return (f.Line, $"the modifier `{(f.Modifiers & ~Modifiers.Pub).ToString().ToLowerInvariant()}` of an actor's method (U9)");
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// U1, U16: once every type is resolved, the one diagnostic of each actor the checker does not read: the language's error of its
    /// declaration where it has one (<see cref="ActorDeclarationError"/>), else <c>not supported</c> at the first part the build does not
    /// read (<see cref="UnreadActorPart"/>), so that the build's limit hides no error of the language (E1). Its types are read with its
    /// type parameters in scope, as a data type's are (<see cref="ResolveData"/>), since T19 declares them for its whole declaration, so
    /// that one named in a type after its name or in a <c>static</c> field's type is no <c>unknown-name</c>; one that holds a constant
    /// names no type (T35).
    /// </summary>
    private void RefuseUnreadActors()
    {
        foreach (var d in unreadActors)
        {
            typeContext = [.. d.TypeParameters.Where(p => p.Constant is null).Select(p => new TypeParameterSymbol(p.Name, p.Line) { HasLimit = p.Limit is not null })];
            var error = ActorDeclarationError(d);
            typeContext = null;
            if (!error && UnreadActorPart(d) is { } part)
            {
                NotSupported(part.Line, part.Construct);
            }
        }
    }

    /// <summary>
    /// The language's error of an actor's declaration, the first in the order of its words (U16; the owner, 2026-11-14, #519, comment
    /// 6884160133, item 7), reported; whether it has one. Each type after its name in turn: an actor the file declares, read or not, is
    /// <c>syntax</c> before the type is resolved, so that an actor that is its own base, and each of two actors that are each the other's,
    /// gets it, and so is any other type that is no interface, a base, which an actor has none of (U1; Q26 of issue 519, comment
    /// 6882624606, assumed: the id, as reading 1 of comment 6882879015 gives it); then <c>equal by</c> and, in the body's order, a
    /// constructor (U2), a <c>pub</c> field's or property's <c>actor-field-public</c> (U8) and a <c>static</c> field's error
    /// (<see cref="StaticActorFieldError"/>).
    /// </summary>
    private bool ActorDeclarationError(TypeDeclaration d)
    {
        foreach (var b in d.Bases)
        {
            if (b.Type is NamedType n && actorNames.Contains(n.Name))
            {
                Report(ErrorIds.Syntax, b.Line, $"the actor `{n.Name}` after the name of the actor `{d.Name}`, which names interfaces alone and has no base, is text no rule gives a meaning (U1, E4)");
                return true;
            }

            var type = Resolve(b.Type);
            if (Types.IsError(type))
            {
                return true; // `unknown-name` (V10), where it is resolved, or a type of another kind the checker does not read, said `not supported` at its declaration; the actor's own base is not reported then
            }

            if (type is not InterfaceType)
            {
                Report(ErrorIds.Syntax, b.Line, $"`{type}` after the name of the actor `{d.Name}`, which names interfaces alone and has no base, is text no rule gives a meaning (U1, E4)");
                return true;
            }
        }

        if (CheckStackClause(d))
        {
            return true;
        }

        if (d.EqualBy.Count > 0)
        {
            Report(ErrorIds.Syntax, d.Line, "`equal by` on an actor, whose `==` is that of the actor its reference names, is text no rule gives a meaning (U2, E4)");
            return true;
        }

        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case ConstructorDeclaration c:
                    Report(ErrorIds.Syntax, c.Line, "a constructor in the body of an actor, whose constructor is its primary one, is text no rule gives a meaning (U2, E4)");
                    return true;
                case FunctionDeclaration { Property: true } p when (p.Modifiers & Modifiers.Pub) != 0:
                    Report(ErrorIds.ActorFieldPublic, p.Line, $"`{p.Name}` is a `pub` property of the actor `{d.Name}`, whose state nothing outside it reaches: a `pub` method gives it (U8)");
                    return true;
                case FieldDeclaration f when (f.Modifiers & (Modifiers.Pub | Modifiers.Static)) == Modifiers.Pub:
                    Report(ErrorIds.ActorFieldPublic, f.Line, $"`{f.Name}` is a `pub` field of the actor `{d.Name}`, whose state nothing outside it reaches: a `pub` method gives it (U8)");
                    return true;
                case FieldDeclaration f when (f.Modifiers & Modifiers.Static) != 0 && StaticActorFieldError(d, f):
                    return true;
                default:
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// A <c>static</c> field of an actor the checker does not read: <c>pub</c> before it is <c>actor-field-public</c>, its word read before
    /// <c>static</c> (U8, U16; the owner, 2026-11-14, #519, comment 6884160133, item 7), and <c>static mut</c>, or a type that is or holds a
    /// class, or an interface's type, a function type or a type parameter, read by its written type (<see cref="MayHoldClass"/>), is
    /// <c>static-state</c>, as K18 reads a class's field (<see cref="ResolveStaticField"/>; U1, assumed: K18's reading of <c>static</c>
    /// state); whether it has its error, reported here or, for a type that resolves to none, where the type is resolved. A
    /// field whose type is not written is a <c>static mut</c> one, the one field the front end reads without a type (K7 with V2), and is
    /// <c>static-state</c> by its <c>mut</c>, so that the type read here is always written, as K18 reads a class's.
    /// </summary>
    private bool StaticActorFieldError(TypeDeclaration d, FieldDeclaration f)
    {
        if ((f.Modifiers & Modifiers.Pub) != 0)
        {
            Report(ErrorIds.ActorFieldPublic, f.Line, $"`{f.Name}` is a `pub` field of the actor `{d.Name}`, whose state nothing outside it reaches: a `pub` method gives it (U8)");
            return true;
        }

        if ((f.Modifiers & Modifiers.Mut) != 0)
        {
            Report(ErrorIds.StaticState, f.Line, $"`{f.Name}` is a `static mut` field of the actor `{d.Name}`, which holds no `static` state (U1, K18)");
            return true;
        }

        if (f.Type is null)
        {
            return false;
        }

        var type = Resolve(f.Type);
        var held = new HashSet<ClassSymbol>();
        ClassesHeld(type, held, []);
        if (held.Count > 0)
        {
            Report(ErrorIds.StaticState, f.Line, $"the `static` field `{f.Name}` of the actor `{d.Name}` would hold an instance of `{held.First().Name}`, which lives in one actor's heap (U1, K18)");
            return true;
        }

        if (MayHoldClass(type))
        {
            Report(ErrorIds.StaticState, f.Line, $"the `static` field `{f.Name}` of the actor `{d.Name}` is of type `{type}`, which may hold a class instance whatever the program puts there: a `static` field is read by its written type (U1, K18)");
            return true;
        }

        return Types.IsError(type);
    }

    /// <summary>U12: what a value of a type does where it crosses into an actor or back out of it as a result.</summary>
    private enum ActorCrossing
    {
        /// <summary>It crosses with no word.</summary>
        Crosses,

        /// <summary>It reaches a mutable class instance, and crosses with <c>copy</c> or <c>move</c> alone (Q8 of comment 6847932993).</summary>
        NeedsWord,

        /// <summary>It reaches a value of an interface's type or a function value the checker does not see, which no word makes cross.</summary>
        Never,
    }

    /// <summary>
    /// U12: how a value of the type crosses, with why where it does not cross with no word, the build's limit it reaches, null where
    /// it reaches none, and the first pointer or view of an atomic type it reaches, null where it reaches none, which no word makes cross,
    /// <c>pointer-crosses</c> (the owner, 2026-11-17, #572, comment 6961064484, Q1, "Refuse with an id (Recommended)"). A type is read
    /// through the items of a list, the keys and values of a map, the cases of a union, the fields of a data value and of a class, a class derived from it among them (K6), a class's fields as declared, since no class takes type arguments
    /// (a generic class is <c>not supported</c>, T19), and the target of a <c>weak</c> reference, which crosses where
    /// its target does (the owner, 2026-11-15, #519, comment 6896192351, Q3, "Points at the copy"), as Rust's <c>Send</c> is structural:
    /// a <c>mut</c> field needs a word, and an interface's type and a function type are read by their written type and never cross
    /// (comment 6892275110, item 2; comment 6896192351, Q5, "By written type"). A value of a type parameter, a handle of section 12, a
    /// pointer, a view and a class of <c>std/</c>, which is no class of the file (W1), <c>HttpRequest</c> among them, are the build's limits,
    /// a pointer's and a view's standing where a call that is no message copies them or a block of <c>on crash</c> reads them; a map
    /// whose keys reach a class instance is none, since its copy hashes each key again (the owner, 2026-11-16, #519, comment 6923513534,
    /// "Rehash each key (Recommended)").
    /// </summary>
    private (ActorCrossing Kind, string? Why, string? Limit, string? Raw) CrossingOf(KurzType type)
    {
        var (kind, why, limit, raw) = (ActorCrossing.Crosses, (string?)null, (string?)null, (string?)null);
        var classes = new HashSet<ClassSymbol>();
        var data = new HashSet<DataSymbol>();
        void Meet(ActorCrossing k, string reason)
        {
            if (k > kind)
            {
                (kind, why) = (k, reason);
            }
        }

        void Walk(KurzType t)
        {
            switch (t)
            {
                case FunctionType:
                    Meet(ActorCrossing.Never, $"a function value, `{t}`, which crosses only as a lambda the sending function shows, the argument itself or bound to an immutable local variable");
                    break;
                case InterfaceType:
                    Meet(ActorCrossing.Never, $"a value of the interface `{t}`, which may hold a mutable class instance whatever the program puts there");
                    break;
                case WeakRefType w:
                    Walk(w.Target);
                    break;
                case ClassType c when !classDeclared.Contains(c.Symbol):
                    limit ??= $"an instance of `{c.Symbol.Name}`, a class of `std/`";
                    break;
                case ClassType c:
                    foreach (var k in classDeclared.Where(x => x.HasBaseOrIs(c.Symbol)).Prepend(c.Symbol).Where(classes.Add))
                    {
                        foreach (var f in k.Fields)
                        {
                            if (f.Mut)
                            {
                                Meet(ActorCrossing.NeedsWord, $"an instance of `{k.Name}`, whose field `{f.Name}` is `mut`");
                            }

                            Walk(f.Type);
                        }
                    }

                    break;
                case ListType l:
                    Walk(l.Element);
                    break;
                case MapType m:
                    Walk(m.Key);
                    Walk(m.Value);
                    break;
                case UnionType u:
                    foreach (var c in u.Cases)
                    {
                        Walk(c);
                    }

                    break;
                case DataType d when data.Add(d.Symbol):
                    foreach (var f in d.Symbol.Fields)
                    {
                        Walk(SubstituteData(d, f.Type));
                    }

                    break;
                case DataType d:
                    foreach (var a in HeldArguments(d))
                    {
                        Walk(a);
                    }

                    break;
                case TypeParameterType:
                    limit ??= $"a value of the type parameter `{t}`";
                    break;
                case PointerType or AtomicType: // R19: a view, as the pointer it holds
                    raw ??= $"{Types.A(t)}";
                    limit ??= $"{Types.A(t)}";
                    break;
                case HandleType or HasherType or CommandType or ChildProcessType or CFunctionType:
                    limit ??= $"{Types.A(t)}";
                    break;
                default:
                    break; // a number, a `bool`, a `char`, a string, a time, an enum, a flags set, `null` and an actor's reference
            }
        }

        Walk(type);
        return (kind, why, limit, raw);
    }

    /// <summary>
    /// U12: how the argument crosses into the slot of the type <paramref name="slot"/>: a function value by what it captures where the
    /// checker sees its lambda (<see cref="SeenLambda"/>), and by its written type otherwise, so that any other value of a function type
    /// never crosses (a named function is no value, F11) (the owner, 2026-11-15, #519, comment 6896192351, Q5, "By written type"); every other
    /// argument by the slot's type, which a message holds (<see cref="CrossingOf"/>). What a lambda captures crosses with no word or it
    /// does not cross, since no word at the argument makes a function value cross (§6, Q24). Beside the build's limit, the limit that
    /// stands after <c>move</c> too (<see cref="SharedByCount"/>), and the pointer or view it reaches, a capture's among them.
    /// </summary>
    private (ActorCrossing Kind, string? Why, string? Limit, string? Shared, string? Raw) ArgumentCrossing(Argument a, KurzType slot)
    {
        if ((Types.NonNull(slot) ?? slot) is FunctionType)
        {
            if (SeenLambda(a.Value) is { } l)
            {
                model.RecordCrossingLambda(a.Value, l); // whose environment the crossing copies (comment 6902210646, Q5)
                var (kind, why, limit, shared, raw) = (ActorCrossing.Crosses, (string?)null, (string?)null, (string?)null, (string?)null);
                foreach (var captured in model.LambdaOf(l).Captures)
                {
                    var (k, w, s, r) = CrossingOf(captured.Type);
                    if (k != ActorCrossing.Crosses && kind == ActorCrossing.Crosses)
                    {
                        (kind, why) = (ActorCrossing.Never, $"a lambda that captures `{captured.Name}`, {w}");
                    }

                    limit ??= s;
                    shared ??= SharedByCount(captured.Type);
                    raw ??= r is null ? null : $"a lambda that captures `{captured.Name}`, which reaches {r}";
                }

                return (kind, why, limit, shared, raw);
            }
        }

        var (crossing, reason, limited, pointer) = CrossingOf(slot);
        return (crossing, reason, limited, SharedByCount(slot), pointer);
    }

    /// <summary>
    /// U12: the first handle of section 12, hasher, command or child process that a value of the type holds, or the first value of a type
    /// parameter, read on the function's text, which the copy of its function for such a type argument makes one of them, read as
    /// <see cref="CrossingOf"/> reads a type, through a class of <c>std/</c>'s fields too, <c>HttpRequest</c>'s body among them, a class's
    /// fields as declared, since no class takes type arguments (a generic class is <c>not supported</c>, T19); null where it holds none.
    /// The build copies none of them: its copy keeps the sender's block and counts it once more (<c>Emit/Emitter.Copies.cs</c>),
    /// so that the receiver would hold a count of a block of the sender's heap, which the sender's restart or end frees, and the build's
    /// <c>not supported</c> stands for such a value after <c>move</c> as with no word (<see cref="CrossingErrors"/>).
    /// </summary>
    private string? SharedByCount(KurzType type)
    {
        var classes = new HashSet<ClassSymbol>();
        var data = new HashSet<DataSymbol>();
        string? First(IEnumerable<KurzType> types) => types.Select(Find).FirstOrDefault(s => s is not null);
        string? Find(KurzType t) => t switch
        {
            HandleType or HasherType or CommandType or ChildProcessType => Types.A(t),
            WeakRefType w => Find(w.Target),
            ListType l => Find(l.Element),
            MapType m => First([m.Key, m.Value]),
            UnionType u => First(u.Cases),
            DataType d when data.Add(d.Symbol) => First(d.Symbol.Fields.Select(f => SubstituteData(d, f.Type))),
            DataType d => First(HeldArguments(d)),
            ClassType c when classes.Add(c.Symbol) => First(classDeclared.Where(x => x.HasBaseOrIs(c.Symbol)).Prepend(c.Symbol).SelectMany(k => k.Fields).Select(f => f.Type)),
            TypeParameterType => $"a value of the type parameter `{t}`",
            _ => null,
        };
        return Find(type);
    }

    /// <summary>
    /// U12: the lambda the checker sees for a function value in the function that gives it (Q5, "By written type"): one written there, or
    /// the one an immutable local variable was declared with.
    /// </summary>
    private LambdaExpression? SeenLambda(Expression value) => Unwrap(value) switch
    {
        LambdaExpression l => l,
        NameExpression n when model.IsVariable(n) && model.VariableOf(n) is { Kind: VariableKind.Local, Mut: false } v => model.DeclaringNode(v) switch
        {
            AssignOrDeclare d => Unwrap(d.Value) as LambdaExpression,
            VariableDeclaration { Value: { } bound } => Unwrap(bound) as LambdaExpression,
            _ => null,
        },
        _ => null,
    };

    /// <summary>
    /// U12, U16: the stage of the arguments of a message or of an actor's constructor, after the own error of each argument (E1), in the
    /// order of U16: of the first argument that does not cross, at its argument, <c>pointer-crosses</c> where it reaches a pointer or a view,
    /// with a word or with none, before its <c>send-needs-word</c>, since no word makes it cross (U12, assumed), else <c>send-needs-word</c>,
    /// then in the order the arguments are
    /// written, the words of <c>move</c> (<see cref="MessageMove"/>: <c>move-at-call</c>, <c>move-reachable</c> and a later argument's
    /// <c>used-after-move</c>) and <c>mut-in-message</c> of a <c>mut</c> parameter, which <paramref name="mutInMessage"/> reports (the owner,
    /// 2026-11-15, issue 519, comment 6891937666, item 4), and after every argument the build's <c>not supported</c> of the first value it
    /// does not make cross (<see cref="CrossingOf"/>; after <c>move</c>, <see cref="SharedByCount"/>), so that the build's limit hides no error
    /// of the language (E1); then each <c>move</c> ends what it sends. Whether it reported one.
    /// </summary>
    private bool CrossingErrors(CallExpression c, CallInfo info, Func<int, KurzType> slotType, Func<int, bool> slotMut, Func<int, bool> slotMove, string callee, Action<int> mutInMessage)
    {
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            if (info.Binding[i] is var bound && bound >= 0 && slotMut(bound) != c.Arguments[i].Mut)
            {
                return true; // E1, U16: the argument's own `mut-at-call` (M7), which the call's check reported, stands alone
            }
        }

        var limits = new string?[c.Arguments.Count];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = info.Binding[i];
            if (slot < 0 || slotMut(slot))
            {
                continue;
            }

            var a = c.Arguments[i];
            var (kind, why, limit, shared, raw) = ArgumentCrossing(a, slotType(slot));
            limits[i] = a.Word == ArgumentWord.Move ? shared : limit;
            if (raw is not null)
            {
                Report(ErrorIds.PointerCrosses, a.Line, $"{ArgumentText(a, i)} of `{callee}` reaches {raw}, which crosses into no actor, with a word or with none: a pointer and a view stand on memory the language does not follow (U12, R19)");
                return true;
            }

            if (kind == ActorCrossing.Never || (kind == ActorCrossing.NeedsWord && a.Word is not (ArgumentWord.Copy or ArgumentWord.Move)))
            {
                Report(ErrorIds.SendNeedsWord, a.Line, kind == ActorCrossing.NeedsWord
                    ? $"{ArgumentText(a, i)} of `{callee}` reaches {why}, and crosses into an actor only with `copy` before it, for a deep copy, or `move` (U12)"
                    : $"{ArgumentText(a, i)} of `{callee}` reaches {why}, and no word makes it cross into an actor: pass a value, a `data` value or an immutable class instance in its place, or a lambda that captures only such values (U12)");
                return true;
            }
        }

        var moves = new List<(Argument Argument, VariableSymbol? Variable, System.Collections.Immutable.ImmutableHashSet<int> Ends)>();
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = info.Binding[i];
            if (slot < 0)
            {
                continue;
            }

            if (slotMut(slot))
            {
                mutInMessage(slot);
                return true;
            }

            if (MessageMove(c, i, slotMove(slot), moves))
            {
                return true;
            }
        }

        for (var i = 0; i < c.Arguments.Count; i++)
        {
            // the build's limit; after `move` it stands only for a handle, a hasher, a command or a child process, which the copy would
            // share by its count, a block of the sender's heap that the sender's restart or end frees (`SharedByCount`), and for a value
            // of a type parameter, which the copy of its function for such a type argument makes one of them, and is set aside for the
            // rest, which the move's copy brings whole into the receiver's heap or which holds no counted block
            if (limits[i] is { } limit)
            {
                NotSupported(c.Arguments[i].Line, c.Arguments[i].Word == ArgumentWord.Move
                    ? $"{limit} after `move`, crossing into an actor, which this build would share by its count with the sender's heap, as with `copy` or no word (U12)"
                    : $"{limit}, crossing into an actor (U12)");
                return true;
            }
        }

        EndMoved(moves);
        return false;
    }

    /// <summary>The text of an argument in an error: its name or <c>this</c>, else its place among the arguments.</summary>
    private static string ArgumentText(Argument a, int i) => Text(a.Value) is var text && text != "..."
        ? $"the argument `{text}`"
        : $"the argument {(i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// U2: <c>spawn Counter(1)</c> makes the actor's state with the arguments of its primary constructor, the call recorded as the
    /// class's construction (K1), which cross into the actor as a message's arguments do (U12, <see cref="CrossingErrors"/>), and gives
    /// its reference, wherever an expression stands; <c>spawn</c> of anything else is <c>type-mismatch</c> (U14).
    /// </summary>
    private Typed CheckSpawn(SpawnExpression s)
    {
        var name = ((NameExpression)s.Call.Callee).Name;
        if (!classes.TryGetValue(name, out var symbol) || !symbol.IsActor)
        {
            if (IsUnreadName(name))
            {
                CheckArgumentsLoosely(s.Call); // its declaration was said `not supported` (E1)
            }
            else if (symbol is not null || dataTypes.ContainsKey(name) || interfaces.ContainsKey(name) || enums.ContainsKey(name) || flagsTypes.ContainsKey(name) || functions.ContainsKey(name) || Lookup(name).Variable is not null)
            {
                Report(ErrorIds.TypeMismatch, s.Line, $"`{name}` is no actor, and `spawn` makes an actor (U2, U14)");
                CheckArgumentsLoosely(s.Call);
            }
            else
            {
                Report(ErrorIds.UnknownName, s.Line, $"`{name}` is not declared (V10)");
                CheckArgumentsLoosely(s.Call);
            }

            return Typed.Error;
        }

        var outer = spawning;
        spawning = s.Call;
        Push();
        var state = CheckExpression(s.Call, null);
        var reported = Pop(commit: true).Count > 0;
        spawning = outer;
        if (state.IsError || CrossingErrors(s.Call, model.CallOf(s.Call), j => symbol.Constructor[j].Field.Type, _ => false, _ => false, name, _ => { }))
        {
            return Typed.Error; // U12: the constructor's arguments cross as a call's do (Q13 of comment 6847932993)
        }

        if (s.Policy is { } policy && !reported && CheckPolicy(policy))
        {
            return Typed.Error; // U25, U16: the policy is read after the arguments, as it is written after them
        }

        if (s.Policy is { Block: not null } block && CheckCrashBlock(block, symbol))
        {
            return Typed.Error; // U28: the block is read after the arguments, as it is written after them
        }

        return new Typed(new ActorType(symbol));
    }

    /// <summary>
    /// U28 to U31: the block of a policy, <c>on crash (c) { ... }</c>, of the actor <paramref name="child"/>, read as the body of a lambda of
    /// its one parameter, the crash, a <c>Crashed</c> of <c>std/</c> whose <c>Reason</c> is the child's (section 5): where it stands at
    /// <c>spawn</c>, it takes the variables around it by value and reads the fields of the actor whose code spawns, as a lambda there does
    /// (F15, K7; assumed: S7 of issue 519, comment 6907441402), and after a type's name it reads what a top-level function reads (F16;
    /// assumed: S6 of that comment); each path of it ends with a decision or a <c>throw</c>, and one that ends with neither is <c>missing-return</c>,
    /// as for a lambda whose path ends with no value (F3; the owner, 2026-11-16, comment 6919351399, "missing-return error"). Whether it
    /// reported an error. The <c>Reason</c> read is the one the reading before found (<see cref="Check(ProgramSyntax, DiagnosticBag)"/>):
    /// at the first reading it is the runtime's error type alone and what the block reports is dropped, as it is at a reading made only for
    /// the sets of one whose errors stand in blocks alone. A <c>Reason</c> with a type the
    /// crossing copy does not copy (U12) is <c>not supported</c>, as the copy that brings it into the supervisor's heap is not built.
    /// </summary>
    private bool CheckCrashBlock(CrashPolicy policy, ClassSymbol child)
    {
        var l = policy.Block!;
        var crashed = std?.Model.DataTypes.FirstOrDefault(d => d.Name == "Crashed");
        var error = std?.Model.DataTypes.FirstOrDefault(d => d.Name == "RuntimeError");
        if (crashed is null || error is null)
        {
            NotSupported(policy.Line, "the block `on crash (c) { ... }` in a file read without `std/`, which declares `Crashed` (U30)");
            return true;
        }

        IReadOnlyList<KurzType> reason = reasons is null ? [new DataType(error, [])] : Translated(reasons[child.Declaration]);
        if (reasons is not null && reason.FirstOrDefault(t => t is TypeParameterType || CrossingOf(t) is { Kind: ActorCrossing.Never } or { Limit: not null }) is { } uncopied)
        {
            NotSupported(policy.Line, $"a block of `on crash` whose child can throw {Types.A(uncopied)}, which the crossing copy does not bring into the supervisor's heap (U30, U12)");
            return true;
        }

        crashBlocks.Add((policy, child, reason));
        var type = new DataType(crashed, [Types.Union(reason)]);
        var outer = frame;
        Push();
        frame = new Frame
        {
            IsLambda = true,
            Deciding = true,
            Outer = outer,
            CapturedScope = outer.Scope,
            ReturnType = Types.Void,
            Line = l.Line,
            TypeParameters = outer.TypeParameters,
            Reserved = outer.Reserved,
            Known = Unchangeable(outer.Known), // N3 with F15, as a lambda's
            Moves = MoveState.Empty.With(moved: outer.Moves.Moved), // U12, as a lambda's
        };
        var parameter = DeclareParameter(l.Parameters[0].Name, type, false, l.Parameters[0].Line, l.Parameters[0]);
        if (CheckBlock(l.BlockBody!) == Flow.Completes)
        {
            Report(ErrorIds.MissingReturn, l.Line, "a path of the block of `on crash` ends with no decision, `restart`, `stop` or `escalate` (U29, F3)");
        }

        ReportUnused(frame.Scope);
        var block = frame;
        frame = outer;
        var popped = Pop(commit: reasons is not null && !quietBlocks);
        if (reasons is not null && !quietBlocks && popped.Any(d => d.IsError))
        {
            blockErrors.UnionWith(popped.Where(d => d.IsError));
            erringBlocks.Add(policy);
        }

        model.RecordLambda(l, new LambdaInfo([parameter], block.Captures.ToList(), new FunctionType([type], Types.Void)));
        model.RecordCrashReason(type.TypeArguments[0]);
        return popped.Count > 0 && reasons is not null && !quietBlocks;
    }

    /// <summary>
    /// U27, U28: the block of a policy after the name of each actor the checker reads, the type's default, read as the body of a top-level
    /// function reads (F16; assumed: S6 of comment 6907441402), with no supervisor in scope.
    /// </summary>
    private void CheckTypeBlocks()
    {
        foreach (var symbol in classDeclared.Where(c => c.IsActor))
        {
            if (symbol.Declaration.Clauses?.FirstOrDefault(c => c.Policy is { Block: not null })?.Policy is { } policy)
            {
                frame = new Frame { ReturnType = Types.Void, Line = policy.Line, Reserved = reservedTopLevel };
                CheckCrashBlock(policy, symbol);
            }
        }
    }

    /// <summary>
    /// U30: the types of a <c>Reason</c> the reading before found, with the symbols of this reading: a type of the file, of the built-in
    /// types among them, an actor's reference by its actor's class, which the file's classes hold, by the symbol this reading made of its
    /// declaration, and a type of <c>std/</c>, whose checker both readings share, as it is.
    /// </summary>
    private List<KurzType> Translated(IReadOnlyList<KurzType> types) => types.Select(Translated).ToList();

    private KurzType Translated(KurzType t) => t switch
    {
        DataType d => new DataType(dataTypes.TryGetValue(d.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, d.Symbol.Declaration) ? s : d.Symbol, d.TypeArguments.Select(Translated).ToList()),
        ClassType c => new ClassType(classes.TryGetValue(c.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, c.Symbol.Declaration) ? s : c.Symbol),
        ActorType a => new ActorType(classes.TryGetValue(a.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, a.Symbol.Declaration) ? s : a.Symbol),
        EnumType e => new EnumType(enums.TryGetValue(e.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, e.Symbol.Declaration) ? s : e.Symbol),
        FlagsType f => new FlagsType(flagsTypes.TryGetValue(f.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, f.Symbol.Declaration) ? s : f.Symbol),
        InterfaceType i => new InterfaceType(interfaces.TryGetValue(i.Symbol.Name, out var s) && ReferenceEquals(s.Declaration, i.Symbol.Declaration) ? s : i.Symbol),
        ListType l => new ListType(Translated(l.Element)),
        MapType m => new MapType(Translated(m.Key), Translated(m.Value)),
        UnionType u => Types.Union(u.Cases.Select(Translated)),
        WeakRefType w => new WeakRefType((ClassType)Translated(w.Target)),
        _ => t, // a type that holds no symbol of the file; a function type, a pointer or a type parameter, which no copy brings over, is refused above
    };

    /// <summary>
    /// U26: the values of a policy's knobs, each a constant the emitter reads, reported; whether one has an error. <c>after</c> takes a duration
    /// above 0 and at most the cap of 30 s, <c>max</c> an <c>int</c> of at least 1 and its <c>in</c> a duration above 0, each written as a
    /// literal, with its sign where it has one (the parser's); a value past its bound and any other literal there are <c>type-mismatch</c>, as
    /// a value of one type where another is required (T1), and a literal's own error, <c>inexact-literal</c> or <c>constant-overflow</c> (L10,
    /// L14), stands as it does elsewhere (section 6, "Policy at <c>spawn</c>"; the bounds assumed, as Akka's backoff supervisor refuses a
    /// minimum of 0 or past its maximum and Erlang's a period below 1).
    /// </summary>
    private bool CheckPolicy(CrashPolicy p)
    {
        if (p.After is { } after && !Duration(after, "the first pause after `after`", 30_000_000_000))
        {
            return true;
        }

        if (p.Max is { } max)
        {
            if (max is not (IntegerLiteral or UnaryExpression { Operand: IntegerLiteral }))
            {
                Report(ErrorIds.TypeMismatch, max.Line, "the count after `max` is an `int`, `max 10 in 1min` (U26)");
                return true;
            }

            var count = CheckExpression(max, Types.Int);
            if (count.IsError)
            {
                return true;
            }

            if (max is UnaryExpression || count.Constant is IntegerConstant { Value: var n } && n < 1)
            {
                Report(ErrorIds.TypeMismatch, max.Line, "the count after `max` is at least 1: `on crash stop` ends the actor at its first crash (U26)");
                return true;
            }

            if (!Duration(p.Within!, "the window after `in`", long.MaxValue))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>U26: a knob's duration, a literal of a time unit (L14) above 0 and at most <paramref name="most"/> nanoseconds; whether it is one.</summary>
    private bool Duration(Expression literal, string what, long most)
    {
        var unit = literal switch
        {
            UnitLiteral u => u.Value.Unit,
            UnaryExpression { Operand: UnitLiteral u } => u.Value.Unit,
            _ => null,
        };
        if (unit is null || !CoreWords.IsDurationUnit(unit))
        {
            Report(ErrorIds.TypeMismatch, literal.Line, $"{what} is a duration, a number with a unit of time, `1s` (U26, L14)");
            return false;
        }

        var typed = CheckExpression(literal, Types.Duration);
        if (typed.IsError)
        {
            return false;
        }

        if (literal is UnaryExpression || typed.Constant is IntegerConstant { Value: var ns } && (ns <= 0 || ns > most))
        {
            Report(ErrorIds.TypeMismatch, literal.Line, most == long.MaxValue
                ? $"{what} is a duration above 0 (U26)"
                : $"{what} is a duration above 0 and at most 30 s, the cap of the growing pause (U26)");
            return false;
        }

        return true;
    }

    /// <summary>U2: an actor's name called without <c>spawn</c> is <c>spawn-required</c>.</summary>
    private bool SpawnRequired(ClassSymbol symbol, CallExpression c)
    {
        if (!symbol.IsActor || ReferenceEquals(c, spawning))
        {
            return false;
        }

        Report(ErrorIds.SpawnRequired, c.Line, $"`{symbol.Name}` is an actor; make it with `spawn {symbol.Name}(...)` (U2)");
        CheckArgumentsLoosely(c);
        return true;
    }

    /// <summary>
    /// U6: <c>send c.Add(1)</c>, wherever a statement stands, a call of an actor's <c>pub</c> method through its reference, or of its own
    /// method inside it, a lambda in its methods among it, through the lambda's <c>this</c> (U11), recorded as
    /// <see cref="CallKind.ActorSend"/>; a method with a result is <c>send-result</c>, a <c>void</c> with cases beside it among
    /// them (U7), and anything else after <c>send</c> is <c>syntax</c> (U14). A message to its own method takes what one through a
    /// reference takes: its arguments cross (U12), and then a <c>mut</c> parameter is <c>mut-in-message</c> (U11, assumed: U11's reading of
    /// Q6 of comment 6880404917, which answers it of a call through a reference, U9), in U16's order (<see cref="CallActor"/>).
    /// </summary>
    private void CheckSend(SendStatement s)
    {
        var outer = sending;
        sending = s.Call;
        var typed = CheckWhole(s.Call, () => CallResult(s.Call, null)); // U6: every case of the result, which nobody receives
        sending = outer;
        CallRan();
        RecordThrough(s.Call, typed);
        if (typed.IsError || model.TryCallOf(s.Call) is not { } call)
        {
            return;
        }

        if (call.Kind == CallKind.Method && call.Function?.Owner is { IsActor: true } own)
        {
            var f = call.Function;
            if (CrossingErrors(s.Call, call, j => f.Parameters[j].Type, j => f.Parameters[j].Mut, j => f.Parameters[j].Move, $"{own.Name}.{f.Name}", j =>
                Report(ErrorIds.MutInMessage, s.Line, $"`{f.Parameters[j].Name}` of `{own.Name}.{f.Name}` is a `mut` parameter, and a message, as this `send` makes, holds no place of the code that sent it (U9, U11)")))
            {
                return;
            }

            call = call with { Kind = CallKind.ActorSend, Target = new ActorType(own) }; // U11: the actor's own method, which `this` alone calls
            RecordWaits(s.Call);
            model.RecordCall(s.Call, call);
        }

        if (call.Kind != CallKind.ActorSend)
        {
            Report(ErrorIds.Syntax, s.Line, "`send` stands before a call of an actor's method, `send c.Add(1)` (U6, U14)");
            return;
        }

        if (!Types.IsVoid(call.Function!.ReturnType))
        {
            var callee = s.Call.Callee is MemberAccess m ? $"{Text(m.Target)}.{m.Member}" : call.Function.Name;
            Report(ErrorIds.SendResult, s.Line, $"`{callee}` gives a result that a `send` would drop; call it, or have `{call.Function.Name}` give nothing ({(call.Function.ReturnType is UnionType ? "U6, U7" : "U6")})");
        }
    }

    /// <summary>The text of a receiver in a message, its name or <c>this</c>; <c>...</c> for any other expression.</summary>
    private static string Text(Expression e) => e switch
    {
        NameExpression n => n.Name,
        ThisExpression => "this",
        ParenthesizedExpression p => Text(p.Inner),
        _ => "...",
    };

    /// <summary>
    /// U4, U9: a call of an actor's method through its reference: a <c>pub</c> one alone is seen (U9), else <c>not-visible</c>; it
    /// waits for its result, wherever a call stands, and is <see cref="CallKind.ActorCall"/>, or <see cref="CallKind.ActorSend"/>
    /// after <c>send</c> (U6). Its arguments cross (U12), then a <c>mut</c> parameter is <c>mut-in-message</c> (U9; Q6 of comment
    /// 6880404917), in U16's order (<see cref="CrossingErrors"/>), as rustc reads <c>Send</c> in its type check before its borrow check
    /// (the owner, 2026-11-14, #519, comment 6884160133, item 6); then its result crosses where it waits, a <c>send</c> dropping it (U6):
    /// a result that does not cross with no word is <c>send-needs-word</c> at the call, since no word can stand at a result, as rustc
    /// asks <c>Send</c> of a task's output as of its input (the owner, 2026-11-15, #519, comment 6896192351, Q4, "Rust: send-needs-word").
    /// </summary>
    private Typed CallActor(ActorType type, MemberAccess m, CallExpression c, KurzType? expected)
    {
        var symbol = type.Symbol;
        var send = ReferenceEquals(c, sending);
        if (MethodsOf(symbol, m.Member) is not { } methods)
        {
            if (symbol.Field(m.Member) is not null)
            {
                Report(ErrorIds.NotVisible, m.Line, $"`{m.Member}` is a field of the actor `{symbol.Name}`, which nothing outside it reaches (U8)");
            }
            else
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{symbol.Name}` has no method `{m.Member}` (V10)");
            }

            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var seen = methods.Where(f => (f.Declaration!.Modifiers & Modifiers.Pub) != 0).ToList();
        if (seen.Count == 0)
        {
            Report(ErrorIds.NotVisible, m.Line, $"`{m.Member}` of the actor `{symbol.Name}` is private to it: a method takes messages with `pub` (U9, F9)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var typed = ResolveCall(seen, c, expected, out var picked, CallKind.Method);
        if (typed.IsError || picked is null)
        {
            return typed;
        }

        if (CrossingErrors(c, model.CallOf(c), j => picked.Parameters[j].Type, j => picked.Parameters[j].Mut, j => picked.Parameters[j].Move, $"{symbol.Name}.{picked.Name}", j =>
            Report(ErrorIds.MutInMessage, m.Line, $"`{picked.Parameters[j].Name}` of `{symbol.Name}.{picked.Name}` is a `mut` parameter, and a message through a reference holds no place of its caller's (U9)")))
        {
            return Typed.Error;
        }

        if (!send) // U6: a `send` drops the result, so nothing of it crosses, and its result is `send-result`
        {
            var (kind, why, limit, raw) = CrossingOf(picked.ReturnType);
            if (raw is not null)
            {
                Report(ErrorIds.PointerCrosses, m.Line, $"`{symbol.Name}.{picked.Name}` gives {Types.A(picked.ReturnType)}, which reaches {raw}, and no pointer or view crosses back to the caller (U12, R19)");
                return Typed.Error;
            }

            if (kind != ActorCrossing.Crosses)
            {
                Report(ErrorIds.SendNeedsWord, m.Line, $"`{symbol.Name}.{picked.Name}` gives {Types.A(picked.ReturnType)}, which reaches {why}, and no word can stand at a result to make it cross back to the caller: give a value, a `data` value or an immutable class instance (U12)");
                return Typed.Error;
            }

            if (limit is not null)
            {
                NotSupported(m.Line, $"{limit}, crossing out of an actor as the result of `{symbol.Name}.{picked.Name}` (U12)");
                return Typed.Error;
            }
        }

        RecordWaits(c);
        model.RecordCall(c, model.CallOf(c) with { Kind = send ? CallKind.ActorSend : CallKind.ActorCall, Target = type });
        return typed;
    }

    /// <summary>
    /// U4, U17: each argument of the message <paramref name="c"/> that runs a call as it is evaluated (<see cref="Calls"/>), which can wait,
    /// recorded for the emitter, which builds the message in a heap of its own where any argument is such (U12, U21). The arguments written
    /// at the call are all: a parameter's default is a constant expression (F12), which runs no call and cannot wait.
    /// </summary>
    private void RecordWaits(CallExpression c)
    {
        foreach (var a in c.Arguments.Where(a => !a.Mut && Calls(a.Value)))
        {
            model.RecordWaitingArgument(a.Value);
        }
    }

    /// <summary>U8: a member of an actor's reference read without a call: its fields are no one's outside it, and a method named without a call is no value (F11).</summary>
    private Typed ActorMember(ActorType type, string member, int line)
    {
        if (type.Symbol.Field(member) is not null)
        {
            Report(ErrorIds.NotVisible, line, $"`{member}` is a field of the actor `{type.Symbol.Name}`, which nothing outside it reaches (U8)");
            return Typed.Error;
        }

        if (MethodsOf(type.Symbol, member) is not null)
        {
            NotSupported(line, "a method of an actor as a value, named without a call (U4, F11)");
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, line, $"`{type.Symbol.Name}` has no member `{member}` (V10)");
        return Typed.Error;
    }

    /// <summary>U8: an assignment into a path through an actor's reference, <c>c.count = 1</c>, is <c>not-visible</c> at its field.</summary>
    private bool AssignsThroughActor(Assignment a, List<MemberAccess> members)
    {
        if (members.FirstOrDefault(m => PathType(m.Target) is ActorType) is not { } through)
        {
            return false;
        }

        var owner = (ActorType)PathType(through.Target)!;
        Report(ErrorIds.NotVisible, through.Line, $"`{through.Member}` of the actor `{owner.Symbol.Name}` is no one's outside it (U8)");
        CheckLoosely(a.Target, a.Value);
        return true;
    }

    /// <summary>U10: <c>this</c> as a value inside an actor, not before a member, is the actor's own reference (Q7 of comment 6866132292).</summary>
    private bool ActorThisAsValue(ThisExpression t) => CurrentClass() is { IsActor: true } && !actorMemberThis.Contains(t);
}
