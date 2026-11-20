using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// T19: the limits of type parameters, <c>T: Shape</c> and <c>T: Animal + Named</c> (the owner's answers of 2026-11-20 on issue 605,
/// comment 7051071380). A type argument, inferred or written, meets an interface limit where it is a class that implements the
/// interface, itself or through a base (K6), the interface itself, or a type parameter whose own limits name it; it meets a class limit
/// where it is the class or a class derived from it, or a type parameter whose own class limit is one; every type that has a text meets
/// an interface whose only member is <c>string Text()</c> (A10). A value of a type parameter reaches the methods of its limits, in a
/// function checked on its text and in a copy of one that reads its type (T33) alike.
/// </summary>
public sealed partial class Checker
{
    /// <summary>T19: a limit that a type argument does not meet: the parameter, the name of the interface or the class, which of the two, and the argument.</summary>
    private readonly record struct UnmetLimit(TypeParameterSymbol Parameter, string Limit, bool Class, KurzType Argument);

    /// <summary>
    /// T19, T33: in a copy of a function that reads its type, each parameter and local whose type in the function's text names a type
    /// parameter, with that type: through one whose type is a type parameter with limits the copy reaches the limits' members alone
    /// (Q13), and through each a call meets a limit by the type parameter's own limits (Q8 and Q22 of issue 605, comment 7051071380).
    /// </summary>
    private readonly Dictionary<VariableSymbol, KurzType> views = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// T19: whether a type argument meets an interface limit. A class meets it by naming the interface, itself or through a base (K6),
    /// and an interface meets its own (Q11, Q17); a type parameter meets it through its own limits alone, an interface among them or a
    /// class limit that names it (Q8); a union, a nullable type, a data type and a built-in type meet none (Q12, Q17), but an interface
    /// whose only member is <c>string Text()</c>, which every type that has a text meets (A10). The error type meets every limit, so
    /// that nothing more is reported (E1).
    /// </summary>
    private static bool Meets(KurzType type, InterfaceSymbol face) => type switch
    {
        ErrorType => true,
        _ when face.TextOnly => HasText(type),
        ClassType c => c.Symbol.Implements(face),
        InterfaceType i => ReferenceEquals(i.Symbol, face),
        TypeParameterType p => p.Symbol.LimitUnread || p.Symbol.Limits.Contains(face) || (p.Symbol.ClassLimit?.Implements(face) ?? false),
        _ => false,
    };

    /// <summary>
    /// T19: whether a type argument meets a class limit (Q5): the class itself or a class derived from it (K6), and a type parameter
    /// whose own class limit is one (Q8); no other type, an interface type among them. The error type meets it (E1).
    /// </summary>
    private static bool Meets(KurzType type, ClassSymbol limit) => type switch
    {
        ErrorType => true,
        ClassType c => c.Symbol.HasBaseOrIs(limit),
        TypeParameterType p => p.Symbol.LimitUnread || (p.Symbol.ClassLimit?.HasBaseOrIs(limit) ?? false),
        _ => false,
    };

    /// <summary>T19: the first limit a type argument does not meet, in the order of the type parameters, the class limit before the interfaces; null where each meets all.</summary>
    private static UnmetLimit? Unmet(IEnumerable<(TypeParameterSymbol Parameter, KurzType Argument)> pairs)
    {
        foreach (var (parameter, argument) in pairs)
        {
            if (parameter.ClassLimit is { } limit && !Meets(argument, limit))
            {
                return new UnmetLimit(parameter, limit.Name, true, argument);
            }

            foreach (var face in parameter.Limits)
            {
                if (!Meets(argument, face))
                {
                    return new UnmetLimit(parameter, face.Name, false, argument);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// T19: the message of an unmet limit, naming the type, the limit, the parameter and the function or data type that declares it (Q9,
    /// Q16); for a type parameter of the caller, the limit it lacks; for a class limit, that the type does not derive from the class, a
    /// text of this change's wording.
    /// </summary>
    private static string UnmetText(UnmetLimit unmet, string of) =>
        unmet.Argument is TypeParameterType own
            ? $"`{own.Symbol.Name}` has no limit `{unmet.Limit}`, which `{unmet.Parameter.Name}` of `{of}` requires (T19)"
            : unmet.Class
                ? $"`{unmet.Argument.Text}` does not derive from `{unmet.Limit}`, the limit of `{unmet.Parameter.Name}` of `{of}` (T19)"
                : $"`{unmet.Argument.Text}` does not implement `{unmet.Limit}`, the limit of `{unmet.Parameter.Name}` of `{of}` (T19)";

    /// <summary>
    /// T19: a call of a method on a value of a type parameter: the methods of the name its limits declare, picked as a call of a name
    /// picks them (F13), so that one that two limits declare with the same parameters is <c>ambiguous-call</c>, with a text that names
    /// the method and both limits (a default of issue 605; the text is this change's wording); where a class limit alone declares the
    /// name, the call is read as a call on a value of the class (K7, F9, Q5); a name none of them declares, every name where it has none,
    /// is <c>unknown-name</c> (Q1, Q2). A field and a property of a class limit are <c>not supported</c> (<see cref="LimitField"/>). The
    /// method of a limit that a type meets by its text alone gives, in each copy, the text of the type the copy binds (Q21). A limit
    /// that is not read reports nothing more (E1).
    /// </summary>
    private Typed CallThroughLimits(TypeParameterSymbol parameter, MemberAccess m, CallExpression c)
    {
        if (parameter.LimitUnread || (parameter.ClassLimit is { } fielded && LimitField(parameter, fielded, m.Member, m.Line)))
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var methods = parameter.Limits.SelectMany(face => face.Methods.Where(f => f.Name == m.Member)).ToList();
        var own = parameter.ClassLimit is { } limit ? MethodsOf(limit, m.Member) : null;
        if (methods.Count == 0 && own is not null)
        {
            return CallMethod(new ClassType(parameter.ClassLimit!), m, c, null); // Q5: as a call on a value of the class, K7, F9
        }

        methods.InsertRange(0, own?.Where(f => !f.IsStatic && Visible(f)) ?? []);
        if (methods.Count == 0)
        {
            Report(ErrorIds.UnknownName, m.Line, $"{Types.A(new TypeParameterType(parameter))} has no member `{m.Member}` (V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        // F13, F12: a method that two limits declare with the same parameters fits every call that fits either alike, so the pick reads
        // the first of them, and a call it picks is `ambiguous-call`
        var distinct = methods.Where((f, i) => !methods.Take(i).Any(g => SameParameters(f, g))).ToList();
        var typed = ResolveCall(distinct, c, null, out var picked, CallKind.Method);
        if (picked is not null && methods.FirstOrDefault(g => !ReferenceEquals(g, picked) && SameParameters(g, picked)) is { } twin)
        {
            Report(ErrorIds.AmbiguousCall, c.Line, $"`{m.Member}` is declared with the same parameters by the limits `{LimitOf(picked, parameter)}` and `{LimitOf(twin, parameter)}` of `{parameter.Name}`, so the call fits both (F13, T19)");
            return Typed.Error;
        }

        return typed;
    }

    /// <summary>T19: the name of the limit that gives a method: its interface, or the class limit for a method of the class's chain.</summary>
    private static string LimitOf(FunctionSymbol method, TypeParameterSymbol parameter) => method.Interface?.Name ?? parameter.ClassLimit!.Name;

    /// <summary>
    /// T19: a field or a property of a class limit read or called through a value of the type parameter, <c>item.Side</c>. A property is
    /// what Q2 (c) of issue 605 gives a body through its limit (comment 7051071380), which the build does not read; a field is a member
    /// Q2 does not name, refused as a reading of the build; both are <c>not supported</c>, with a text that names the member and the limit.
    /// </summary>
    private bool LimitField(TypeParameterSymbol parameter, ClassSymbol limit, string member, int line)
    {
        if (limit.Chain.Any(k => k.Field(member) is not null))
        {
            NotSupported(line, $"the field `{member}` of the limit `{limit.Name}` of `{parameter.Name}`, read through a value of it (T19)");
            return true;
        }

        if (Property(limit, member) is not null)
        {
            NotSupported(line, $"the property `{member}` of the limit `{limit.Name}` of `{parameter.Name}`, read through a value of it (Q2 of issue 605, T19)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// T19: a member of a value of a type parameter read without a call: a field or a property of its class limit is <c>not
    /// supported</c> (<see cref="LimitField"/>), a method of a limit is a method named without a call, <c>not supported</c> as a class's is
    /// (K7, F11), and any other name is <c>unknown-name</c> (V10); a limit that is not read reports nothing more (E1).
    /// </summary>
    private Typed MemberOfParameter(TypeParameterSymbol parameter, string member, int line)
    {
        if (parameter.LimitUnread || (parameter.ClassLimit is { } limit && LimitField(parameter, limit, member, line)))
        {
            return Typed.Error;
        }

        if (parameter.Limits.Any(face => face.Methods.Any(f => f.Name == member)) || (parameter.ClassLimit is { } own && MethodsOf(own, member) is not null))
        {
            NotSupported(line, "a method as a value, named without a call (K7, F11)"); // a limit's method, as a class's
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, line, $"{Types.A(new TypeParameterType(parameter))} has no member `{member}` (V10)");
        return Typed.Error;
    }

    /// <summary>
    /// T19, V10: a member named through a type parameter itself, <c>T.Make()</c> or <c>T.Size</c>, where T33 reads none, on the
    /// function's text and in a copy alike. A <c>static</c> member of its class limit is what Q2 (c) of issue 605 gives a body (comment
    /// 7051071380), which the build does not read: <c>not supported</c>, with a text that names it; any other name is
    /// <c>unknown-name</c>, with a text that names the parameter and the member (this change's wording). A parameter that holds a
    /// constant (T35) is no such name; a limit that is not read reports nothing more (E1).
    /// </summary>
    private bool TypeParameterMember(string name, string member, int line)
    {
        var parameter = TypeParameter(name) is { Constant: null } own ? own
            : BoundType(name) is not null ? CurrentCopy()?.Original.TypeParameters.FirstOrDefault(p => p.Name == name)
            : null;
        if (parameter is null && BoundType(name) is null)
        {
            return false;
        }

        if (parameter is { LimitUnread: true })
        {
            return true;
        }

        if (parameter?.ClassLimit is { } limit && (limit.Chain.Any(k => k.StaticField(member) is not null) || (MethodsOf(limit, member)?.Any(f => f.IsStatic) ?? false)))
        {
            NotSupported(line, $"the `static` member `{member}` of the limit `{limit.Name}`, reached through `{name}` (Q2 of issue 605, T19)");
            return true;
        }

        Report(ErrorIds.UnknownName, line, $"`{name}.{member}` names no `static` member of a limit of `{name}` (V10, T19)");
        return true;
    }

    /// <summary>
    /// T19, T33: the type in the function's text of the parameter or local through which a copy reads a value, and of an item read from
    /// one that holds a list; null for every other target, the result of a call among them, which is read on the type the copy binds.
    /// </summary>
    private KurzType? View(Expression target) => views.Count == 0 ? null : Unwrap(target) switch
    {
        NameExpression n when Lookup(n.Name, capture: false).Variable is { } variable && views.TryGetValue(variable, out var written) => written,
        IndexExpression { Conditional: false } x when View(x.Target) is ListType list => list.Element,
        _ => null,
    };

    /// <summary>T19, T33: the type parameter with limits through whose parameter or local a copy reads a member; null for every other target (Q13).</summary>
    private TypeParameterSymbol? ViewedLimit(Expression target) => View(target) is TypeParameterType { Symbol.HasLimits: true } p ? p.Symbol : null;

    /// <summary>
    /// T19, T33 with Q8 and Q22 of issue 605 (comment 7051071380): in a copy, the type arguments a call's arguments give its type
    /// parameters from the types the function's text writes for them, where an argument is read through a parameter or a local of the
    /// copy's type parameter, so that the call meets a limit by that type parameter's own limits, whatever the copy binds; null outside a
    /// copy and where no argument is read so.
    /// </summary>
    private Dictionary<TypeParameterSymbol, KurzType>? WrittenArguments(Candidate candidate, List<Pre> pres)
    {
        if (views.Count == 0 || CurrentCopy() is null)
        {
            return null;
        }

        Dictionary<TypeParameterSymbol, KurzType>? written = null;
        for (var i = 0; i < pres.Count; i++)
        {
            if (View(pres[i].Argument.Value) is { } type)
            {
                written ??= [];
                Unify(candidate.Slots[candidate.Binding[i]].Type, type, written);
            }
        }

        return written;
    }

    /// <summary>T19, T33: in a copy, each parameter whose type in the function's text names one of the function's type parameters.</summary>
    private void ViewParameters(FunctionSymbol f)
    {
        if (frame.Copy is not { } copy)
        {
            return;
        }

        foreach (var (p, original) in f.Parameters.Zip(copy.Original.Parameters))
        {
            if (copy.Original.TypeParameters.Any(t => t.Constant is null && Types.Mentions(original.Type, t)) && Lookup(p.Name, capture: false).Variable is { } variable)
            {
                views[variable] = original.Type;
            }
        }
    }

    /// <summary>
    /// T19, T33: in a copy, a local whose written type is the name of one of the function's type parameters, or one without a written
    /// type whose value is read through such a parameter or local.
    /// </summary>
    private void ViewLocal(string name, TypeSyntax? written, Expression? value)
    {
        if (CurrentCopy() is not { } copy || Lookup(name, capture: false).Variable is not { } variable)
        {
            return;
        }

        if (written is NamedType { TypeArguments.Count: 0 } n && copy.Original.TypeParameters.FirstOrDefault(p => p.Name == n.Name) is { Constant: null } parameter)
        {
            views[variable] = new TypeParameterType(parameter);
        }
        else if (written is null && value is not null && View(value) is { } viewed)
        {
            views[variable] = viewed;
        }
    }
}
