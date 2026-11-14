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

    /// <summary>
    /// U1: an actor the checker reads is declared as a class is, its body read by the class's code; what an actor writes that the
    /// checker does not read is <c>not supported</c> at that line, and its name joins the types the checker does not read (E1).
    /// </summary>
    private void DeclareActor(TypeDeclaration d)
    {
        if (UnreadActorPart(d) is { } unread)
        {
            NotSupported(unread.Line, unread.Construct);
            unsupportedTypes.Add(d.Name);
            unsupportedBases[d.Name] = "an actor a data type names (U1)";
            return;
        }

        DeclareClass(d);
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

    /// <summary>The first part of an actor's declaration that the checker does not read beyond a class's, with its line; null where it reads all of it.</summary>
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
            return (d.Bases[0].Line, "a base or an interface after an actor's name (U1)");
        }

        if (d.EqualBy.Count > 0)
        {
            return (d.Line, "`equal by` on an actor, whose `==` issue 519 holds open, comment 6866132292, Q10 (U2)");
        }

        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case ConstructorDeclaration c:
                    return (c.Line, "a constructor in the body of an actor, whose constructor is its primary one (U2)");
                case FieldDeclaration f when (f.Modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` field of an actor (U1)");
                case FunctionDeclaration f when (f.Modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` method of an actor (U1)");
                case FunctionDeclaration { Property: true } f:
                    return (f.Line, "a property of an actor, which issue 519 holds open, comment 6866132292, Q4 (U8)");
                case FieldDeclaration f when (f.Modifiers & Modifiers.Pub) != 0:
                    return (f.Line, "a `pub` field of an actor, which issue 519 holds open, comment 6866132292, Q4 (U8)");
                case FunctionDeclaration f when (f.Modifiers & ~Modifiers.Pub) != 0:
                    return (f.Line, $"the modifier `{(f.Modifiers & ~Modifiers.Pub).ToString().ToLowerInvariant()}` of an actor's method (U9)");
                default:
                    break;
            }
        }

        return null;
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
    /// reference takes: its arguments cross (U12), and a <c>mut</c> parameter is open (Q6 of comment 6866132292).
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
            if (call.Function.Parameters.FirstOrDefault(p => p.Mut) is { } mut)
            {
                NotSupported(s.Line, $"the `mut` parameter `{mut.Name}` of `{own.Name}.{call.Function.Name}` in a `send` of the actor's own method, which issue 519 holds open, comment 6866132292, Q6 (U11)");
                return;
            }

            if (call.Function.Parameters.Select(p => p.Type).FirstOrDefault(t => !CrossesActors(t)) is { } stays)
            {
                NotSupported(s.Line, $"{Types.A(stays)} in the message `{own.Name}.{call.Function.Name}`, which is no value or actor's reference (U12)");
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
    /// after <c>send</c> (U6). Its arguments and its result cross (U12), and a <c>mut</c> parameter is open (Q6 of comment 6866132292).
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

        if (picked.Parameters.FirstOrDefault(p => p.Mut) is { } mut)
        {
            NotSupported(m.Line, $"the `mut` parameter `{mut.Name}` of `{symbol.Name}.{picked.Name}` called through a reference, which issue 519 holds open, comment 6866132292, Q6 (U9)");
            return Typed.Error;
        }

        if (picked.Parameters.Select(p => p.Type).Append(picked.ReturnType).FirstOrDefault(t => !CrossesActors(t)) is { } stays)
        {
            NotSupported(m.Line, $"{Types.A(stays)} in the message `{symbol.Name}.{picked.Name}`, which is no value or actor's reference (U12)");
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

    /// <summary>U10: <c>this</c> inside an actor reads its members alone; as a value it is open (Q7 of comment 6866132292).</summary>
    private bool ActorThisAsValue(ThisExpression t)
    {
        if (CurrentClass() is not { IsActor: true } || actorMemberThis.Contains(t))
        {
            return false;
        }

        NotSupported(t.Line, "`this` as a value inside an actor, which issue 519 holds open, comment 6866132292, Q7 (U10)");
        return true;
    }

    /// <summary>U2: the text of an actor's reference, for <c>print</c> and an interpolation, is open (Q10 of comment 6866132292).</summary>
    private const string ActorText = "the text of an actor's reference, which issue 519 holds open, comment 6866132292, Q10 (U2)";
}
