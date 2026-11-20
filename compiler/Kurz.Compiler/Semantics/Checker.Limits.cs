using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// T19: the limits of type parameters, <c>T: Shape</c>. A type argument, inferred or written, meets a limit where it is a class that
/// implements the interface, itself or through a base (K6), the interface itself, or a type parameter whose own limits name it; every
/// type that has a text meets an interface whose only member is <c>string Text()</c> (A10). A value of a type parameter reaches the
/// methods of its limits, and no other member, in a function checked on its text and in a copy of one that reads its type (T33) alike.
/// </summary>
public sealed partial class Checker
{
    /// <summary>
    /// T19, T33: in a copy of a function that reads its type, each parameter and local whose written type is a type parameter with
    /// limits, with that parameter: through it the copy reaches the limits' members alone *(assumed: Q13 of issue 605, built at the
    /// lean while it waits for the owner)*.
    /// </summary>
    private readonly Dictionary<VariableSymbol, TypeParameterSymbol> limitedViews = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// T19: whether a type argument meets a limit. A class meets it by naming the interface, itself or through a base (K6), and an
    /// interface meets its own *(assumed: Q11 of issue 605, built at the lean while it waits for the owner; Q17 of the product pass on
    /// issue 605, comment 7014593721, built at its default while it waits for the owner)*; a type parameter meets it through its own
    /// limits alone *(assumed: Q8 of issue 605, built at the lean while it waits for the owner)*; a union, a nullable type, a data type
    /// and a built-in type meet none *(assumed: Q12 of issue 605, built at the lean while it waits for the owner; Q17 of the product
    /// pass on issue 605, comment 7014593721, built at its default while it waits for the owner)*, but an interface whose only member is
    /// <c>string Text()</c>, which every type that has a text meets (A10). The error type meets every limit, so that nothing more is
    /// reported (E1).
    /// </summary>
    private static bool Meets(KurzType type, InterfaceSymbol face) => type switch
    {
        ErrorType => true,
        _ when face.TextOnly => HasText(type),
        ClassType c => c.Symbol.Implements(face),
        InterfaceType i => ReferenceEquals(i.Symbol, face),
        TypeParameterType p => p.Symbol.LimitUnread || p.Symbol.Limits.Contains(face),
        _ => false,
    };

    /// <summary>T19: the first limit a type argument does not meet, in the order of the type parameters and of their limits; null where each meets all.</summary>
    private static (TypeParameterSymbol Parameter, InterfaceSymbol Limit, KurzType Argument)? Unmet(IEnumerable<(TypeParameterSymbol Parameter, KurzType Argument)> pairs)
    {
        foreach (var (parameter, argument) in pairs)
        {
            foreach (var face in parameter.Limits)
            {
                if (!Meets(argument, face))
                {
                    return (parameter, face, argument);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// T19: the message of an unmet limit, naming the type, the interface, the parameter and the function or data type that declares it
    /// *(assumed: Q9 of issue 605, built at the lean while it waits for the owner; Q16 of the product pass on issue 605, comment
    /// 7014593721, built at its default while it waits for the owner)*; for a type parameter of the caller, the limit it lacks.
    /// </summary>
    private static string UnmetText((TypeParameterSymbol Parameter, InterfaceSymbol Limit, KurzType Argument) unmet, string of) =>
        unmet.Argument is TypeParameterType own
            ? $"`{own.Symbol.Name}` has no limit `{unmet.Limit.Name}`, which `{unmet.Parameter.Name}` of `{of}` requires (T19)"
            : $"`{unmet.Argument.Text}` does not implement `{unmet.Limit.Name}`, the limit of `{unmet.Parameter.Name}` of `{of}` (T19)";

    /// <summary>
    /// T19: a call of a method on a value of a type parameter: the methods of the name its limits declare, picked as a call of a name
    /// picks them (F13), so that one that two limits declare with the same parameters is <c>ambiguous-call</c>, with a text that names
    /// the method and both limits *(assumed: a default of issue 605, built while it waits for the owner; the text is this change's
    /// wording)*; a name none of them
    /// declares, every name where it has none, is <c>unknown-name</c> *(assumed: Q1 and Q2 of issue 605, built at the lean while it waits for the owner)*. A call
    /// of the method of a limit that a type meets by its text alone (A10) is <c>not supported</c>, since a type without the interface
    /// meets it; a limit that is not read reports nothing more (E1).
    /// </summary>
    private Typed CallThroughLimits(TypeParameterSymbol parameter, MemberAccess m, CallExpression c)
    {
        if (parameter.LimitUnread)
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var methods = parameter.Limits.SelectMany(face => face.Methods.Where(f => f.Name == m.Member)).ToList();
        if (methods.Count == 0)
        {
            Report(ErrorIds.UnknownName, m.Line, $"{Types.A(new TypeParameterType(parameter))} has no member `{m.Member}` (V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (methods.Any(f => f.Interface!.TextOnly))
        {
            NotSupported(m.Line, $"a call of `{m.Member}` through a limit that a type meets by its text alone (A10, T19)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        // F13, F12: a method that two limits declare with the same parameters fits every call that fits either alike, so the pick reads
        // the first of them, and a call it picks is `ambiguous-call`
        var distinct = methods.Where((f, i) => !methods.Take(i).Any(g => SameParameters(f, g))).ToList();
        var typed = ResolveCall(distinct, c, null, out var picked, CallKind.Method);
        if (picked is not null && methods.FirstOrDefault(g => !ReferenceEquals(g, picked) && SameParameters(g, picked)) is { } twin)
        {
            Report(ErrorIds.AmbiguousCall, c.Line, $"`{m.Member}` is declared with the same parameters by the limits `{picked.Interface!.Name}` and `{twin.Interface!.Name}` of `{parameter.Name}`, so the call fits both (F13, T19)");
            return Typed.Error;
        }

        return typed;
    }

    /// <summary>
    /// T19, V10: a member named through a type parameter itself, <c>T.Create()</c> or <c>T.Size</c>, where T33 reads none: a limit
    /// gives a value of the parameter its methods, and the parameter itself has none, so it is <c>unknown-name</c>, with a text that
    /// names the parameter and the member *(assumed: Q2 of issue 605, built at the lean while it waits for the owner; the text is this
    /// change's wording)*, on the function's text and in a copy alike. A parameter that holds a constant (T35) is no such name.
    /// </summary>
    private bool TypeParameterMember(string name, string member, int line)
    {
        if (TypeParameter(name) is not { Constant: null } && BoundType(name) is null)
        {
            return false;
        }

        Report(ErrorIds.UnknownName, line, $"`{name}.{member}` names no member: a limit gives a value of `{name}` its methods alone (V10, T19)");
        return true;
    }

    /// <summary>T19, T33: the type parameter with limits through whose parameter or local a copy reads a member; null for every other target.</summary>
    private TypeParameterSymbol? ViewedLimit(Expression target) =>
        limitedViews.Count > 0 && Unwrap(target) is NameExpression n && Lookup(n.Name, capture: false).Variable is { } variable
            && limitedViews.TryGetValue(variable, out var parameter) ? parameter : null;

    /// <summary>T19, T33: in a copy, each parameter whose type in the function's text is a type parameter with limits.</summary>
    private void ViewParameters(FunctionSymbol f)
    {
        if (frame.Copy is not { } copy)
        {
            return;
        }

        foreach (var (p, original) in f.Parameters.Zip(copy.Original.Parameters))
        {
            if (original.Type is TypeParameterType { Symbol.Limits.Count: > 0 } limited && Lookup(p.Name, capture: false).Variable is { } variable)
            {
                limitedViews[variable] = limited.Symbol;
            }
        }
    }

    /// <summary>
    /// T19, T33: in a copy, a local whose written type is the name of a type parameter with limits, or one without a written type
    /// whose value is read through such a parameter or local.
    /// </summary>
    private void ViewLocal(string name, TypeSyntax? written, Expression? value)
    {
        if (CurrentCopy() is not { } copy || Lookup(name, capture: false).Variable is not { } variable)
        {
            return;
        }

        if (written is NamedType { TypeArguments.Count: 0 } n && copy.Original.TypeParameters.FirstOrDefault(p => p.Name == n.Name) is { Limits.Count: > 0 } limited)
        {
            limitedViews[variable] = limited;
        }
        else if (written is null && value is not null && ViewedLimit(value) is { } viewed)
        {
            limitedViews[variable] = viewed;
        }
    }
}
