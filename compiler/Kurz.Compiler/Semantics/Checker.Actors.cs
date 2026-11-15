using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Chapter 19: an <c>actor</c>, a class's body whose state nothing outside it reaches (U1, U8), made by <c>spawn</c> (U2), whose
/// reference, an <see cref="ActorType"/>, takes calls of its <c>pub</c> methods as messages: a call waits for its result (U4) and one
/// after <c>send</c> does not (U6), and a method with a result after <c>send</c> is <c>send-result</c> (U6, U7). Inside the actor its
/// own methods and fields are direct, as a class's are (U10), and <c>send</c> of its own method is a message (U11). What crosses into a
/// message is a value or an actor's reference (U12). What else an actor can write, and what the build does not make of it, is
/// <c>not supported</c>, each naming its rule (<c>compiler/README.md</c>).
/// </summary>
public sealed partial class Checker
{
    /// <summary>U4: the frame of the top-level statements, the one place a call through an actor's reference waits.</summary>
    private Frame? topLevelFrame;

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
    /// The first part of an actor's declaration that the build does not read beyond a class's, with its line and what it is; null where
    /// there is none: a clause, a modifier, type parameters, a type after its name and a <c>static</c> member (U1), and a modifier of a
    /// method but <c>pub</c> (U9). A type after the name is named here as an interface, since <see cref="RefuseUnreadActors"/>
    /// reads every such type once the types are resolved and leaves an actor to this part only where each is one; <c>equal by</c> and a
    /// constructor in the body are no part of it, being <c>syntax</c> (U2; Q26 of issue 519, comment 6882624606).
    /// </summary>
    private static (int Line, string Construct)? UnreadActorPart(TypeDeclaration d)
    {
        if (d.Clauses is [var clause, ..])
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
    /// class, is <c>static-state</c>, as K18 reads a class's field (<see cref="ResolveStaticField"/>; U1, assumed: K18's reading of
    /// <c>static</c> state); whether it has its error, reported here or, for a type that resolves to none, where the type is resolved. A
    /// field whose type is not written takes its value's (V2), which the checker does not read in an actor it does not read, so that it
    /// stays the build's limit, as an immutable one of another type does.
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

        return Types.IsError(type);
    }

    /// <summary>
    /// U12: once the classes and data types are resolved, an actor's reference as a field of a class or of a data type, and a field of an
    /// actor's primary constructor of a type that does not cross, are <c>not supported</c>, each at its field.
    /// </summary>
    private void CheckActorFields()
    {
        foreach (var symbol in classDeclared)
        {
            foreach (var field in symbol.Fields.Where(f => f.Type is ActorType && !symbol.IsActor))
            {
                NotSupported(field.Line, $"an actor's reference, `{field.Type}`, as the field `{field.Name}` of a class (U12)");
            }

            if (symbol.IsActor)
            {
                foreach (var slot in symbol.Constructor.Where(s => !CrossesActors(s.Field.Type)))
                {
                    NotSupported(slot.Field.Line, $"the argument `{slot.Field.Name}` of `{symbol.Name}`'s constructor, {Types.A(slot.Field.Type)}, which crosses into the actor and is no value or actor's reference (U12)");
                }
            }
        }

        foreach (var data in dataDeclared)
        {
            foreach (var field in data.Fields.Where(f => f.Type is ActorType))
            {
                NotSupported(field.Line, $"an actor's reference, `{field.Type}`, as the field `{field.Name}` of a data type (U12)");
            }
        }
    }

    /// <summary>
    /// U12: an actor's reference stands alone as a variable's, a parameter's, a field's or a result's type; one inside a list, a map, a
    /// union, a <c>?</c>, a function type or a type argument is <c>not supported</c>, where the type is written.
    /// </summary>
    private KurzType Resolve(TypeSyntax type)
    {
        var resolved = ResolveWritten(type);
        if (ActorInside(resolved))
        {
            NotSupported(type.Line, $"an actor's reference inside `{resolved}` (U12)");
            return Types.Error;
        }

        return resolved;
    }

    /// <summary>U12: the same where a value's type is inferred, a list literal's or a lambda's among them, at the expression.</summary>
    private Typed ActorInsideValue(Expression expression, Typed typed)
    {
        if (typed.IsError || !ActorInside(typed.Type))
        {
            return typed;
        }

        NotSupported(expression.Line, $"an actor's reference inside `{typed.Type}` (U12)");
        return Typed.Error;
    }

    /// <summary>Whether an actor's reference stands inside the type, not as the type itself.</summary>
    private static bool ActorInside(KurzType type) => type switch
    {
        ListType l => IsOrHoldsActor(l.Element),
        MapType m => IsOrHoldsActor(m.Key) || IsOrHoldsActor(m.Value),
        UnionType u => u.Cases.Any(IsOrHoldsActor),
        FunctionType f => f.Parameters.Any(IsOrHoldsActor) || IsOrHoldsActor(f.Result),
        DataType d => d.TypeArguments.Any(IsOrHoldsActor),
        PointerType p => IsOrHoldsActor(p.Pointee),
        _ => false,
    };

    private static bool IsOrHoldsActor(KurzType type) => type is ActorType || ActorInside(type);

    /// <summary>U12: a type of the build's messages: a value, a number, a <c>bool</c>, a <c>char</c>, a string, a time, an enum or a flags set, and a list, map, union or data value of them, or an actor's reference; a class instance and a function value do not cross (Q1 of comment 6866132292).</summary>
    private static bool CrossesActors(KurzType type) =>
        !Holds(type, t => t is not (PrimitiveType or EnumType or FlagsType or NullType or VoidType or ErrorType or ListType or MapType or UnionType or DataType or ActorType), []);

    /// <summary>
    /// U12, U16: whether the language may say <c>send-needs-word</c> of a message's argument of the type written with no word, so that
    /// the build's <c>not supported</c> stands first in the stage of the arguments, before <c>mut-in-message</c>: a type that reaches a
    /// mutable class instance, an instance of a class through which something reachable has a <c>mut</c> field, a class derived from it
    /// among them (K6), a weak reference's target too; or one that reaches a function value, whose captures decide it (U12), or an
    /// interface's type, whose classes no rule of U12 reads (as no rule reads them for a <c>static</c> field, K18), neither of which the
    /// build reads. A type is read through the items of a list, the keys and values of a map, the cases of a union and the fields of a
    /// data value and of a class.
    /// </summary>
    private bool MayNeedWord(KurzType type) => MayNeedWord(type, [], []);

    private bool MayNeedWord(KurzType type, HashSet<ClassSymbol> classes, HashSet<DataSymbol> data) => type switch
    {
        FunctionType or InterfaceType => true,
        WeakRefType w => MayNeedWord(w.Target, classes, data),
        ClassType c => classDeclared.Where(x => x.HasBaseOrIs(c.Symbol)).Prepend(c.Symbol).Where(classes.Add)
            .Any(k => k.Fields.Any(f => f.Mut || MayNeedWord(f.Type, classes, data))),
        ListType l => MayNeedWord(l.Element, classes, data),
        MapType m => MayNeedWord(m.Key, classes, data) || MayNeedWord(m.Value, classes, data),
        UnionType u => u.Cases.Any(x => MayNeedWord(x, classes, data)),
        DataType d when data.Add(d.Symbol) => d.Symbol.Fields.Any(f => MayNeedWord(SubstituteData(d, f.Type), classes, data)),
        DataType d => HeldArguments(d).Any(a => MayNeedWord(a, classes, data)),
        _ => false,
    };

    /// <summary>
    /// U4, U6: a call through an actor's reference may wait in the top-level statements alone, and <c>send</c> and <c>spawn</c> stand there
    /// and in an actor's methods, never in a lambda, a function, a class's method or a default.
    /// </summary>
    private bool InTopLevel() => ReferenceEquals(frame, topLevelFrame);

    private bool InActorMethod() => frame is { Class.IsActor: true, Initializing: null }; // a lambda's frame has no class, and a static member of an actor is refused (U1)

    /// <summary>
    /// U2: <c>spawn Counter(1)</c> makes the actor's state with the arguments of its primary constructor, the call recorded as the
    /// class's construction (K1), and gives its reference; <c>spawn</c> of anything else is <c>type-mismatch</c> (U14).
    /// </summary>
    private Typed CheckSpawn(SpawnExpression s)
    {
        if (s.OnCrashLine != 0)
        {
            NotSupported(s.OnCrashLine, "`on crash` after `spawn` (U2)");
            CheckArgumentsLoosely(s.Call);
            return Typed.Error;
        }

        if (!InTopLevel() && !InActorMethod())
        {
            NotSupported(s.Line, "`spawn` outside the top-level statements and an actor's methods (U2)");
            CheckArgumentsLoosely(s.Call);
            return Typed.Error;
        }

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
        var state = CheckExpression(s.Call, null);
        spawning = outer;
        return state.IsError ? Typed.Error : new Typed(new ActorType(symbol));
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
    /// U6: <c>send c.Add(1)</c>, a call of an actor's <c>pub</c> method through its reference, or of its own method inside it (U11),
    /// recorded as <see cref="CallKind.ActorSend"/>; a method with a result is <c>send-result</c>, a <c>void</c> with cases beside it among
    /// them (U7), and anything else after <c>send</c> is <c>syntax</c> (U14). A message to its own method takes what one through a
    /// reference takes: its arguments cross (U12), and then a <c>mut</c> parameter is <c>mut-in-message</c> (U11, assumed: U11's reading of
    /// Q6 of comment 6880404917, which answers it of a call through a reference, U9), in U16's order (<see cref="CallActor"/>).
    /// </summary>
    private void CheckSend(SendStatement s)
    {
        if (!InTopLevel() && !InActorMethod())
        {
            NotSupported(s.Line, "`send` outside the top-level statements and an actor's methods (U6)");
            CheckLoosely(s.Call);
            return;
        }

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
            var mut = call.Function.Parameters.FirstOrDefault(p => p.Mut);
            if (call.Function.Parameters.Select(p => p.Type).FirstOrDefault(t => !CrossesActors(t) && (mut is null || MayNeedWord(t))) is { } stays)
            {
                NotSupported(s.Line, $"{Types.A(stays)} in the message `{own.Name}.{call.Function.Name}`, which is no value or actor's reference (U12)");
                return;
            }

            if (mut is not null)
            {
                Report(ErrorIds.MutInMessage, s.Line, $"`{mut.Name}` of `{own.Name}.{call.Function.Name}` is a `mut` parameter, and a message, as this `send` makes, holds no place of the code that sent it (U9, U11)");
                return;
            }

            call = call with { Kind = CallKind.ActorSend, Target = new ActorType(own) }; // U11: the actor's own method, which `this` alone calls
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
    /// waits for its result, in the top-level statements alone, and is <see cref="CallKind.ActorCall"/>, or <see cref="CallKind.ActorSend"/>
    /// after <c>send</c> (U6). Its arguments cross (U12), then a <c>mut</c> parameter is <c>mut-in-message</c> (U9; Q6 of comment
    /// 6880404917), and then its result crosses where it waits, a <c>send</c> dropping it (U6): U16's stage of the arguments reads a type
    /// on which the language may say <c>send-needs-word</c> (<see cref="MayNeedWord"/>: one that reaches a mutable class instance, a
    /// function value or an interface's type), which this build says <c>not supported</c> to, before a <c>mut</c> parameter, as rustc reads <c>Send</c> in its type check before its borrow check (the owner, 2026-11-14,
    /// #519, comment 6884160133, item 6), and a <c>mut</c> parameter before any other type that crosses no actor in this build, an
    /// immutable class instance among them, which the language lets cross with no word (U12), so that the build's limit hides no error
    /// of the language (E1); its stage of the result follows.
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

        if (!send && !InTopLevel())
        {
            NotSupported(m.Line, "a call through an actor's reference, which waits for its result, outside the top-level statements (U4)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var typed = ResolveCall(seen, c, expected, out var picked, CallKind.Method);
        if (typed.IsError || picked is null)
        {
            return typed;
        }

        var mut = picked.Parameters.FirstOrDefault(p => p.Mut);
        if (picked.Parameters.Select(p => p.Type).FirstOrDefault(t => !CrossesActors(t) && (mut is null || MayNeedWord(t))) is { } stays)
        {
            NotSupported(m.Line, $"{Types.A(stays)} in the message `{symbol.Name}.{picked.Name}`, which is no value or actor's reference (U12)");
            return Typed.Error;
        }

        if (mut is not null)
        {
            Report(ErrorIds.MutInMessage, m.Line, $"`{mut.Name}` of `{symbol.Name}.{picked.Name}` is a `mut` parameter, and a message through a reference holds no place of its caller's (U9)");
            return Typed.Error;
        }

        if (!send && !CrossesActors(picked.ReturnType)) // U6: a `send` drops the result, so nothing of it crosses, and its result is `send-result`
        {
            NotSupported(m.Line, $"{Types.A(picked.ReturnType)} in the message `{symbol.Name}.{picked.Name}`, which is no value or actor's reference (U12)");
            return Typed.Error;
        }

        model.RecordCall(c, model.CallOf(c) with { Kind = send ? CallKind.ActorSend : CallKind.ActorCall, Target = type });
        return typed;
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
