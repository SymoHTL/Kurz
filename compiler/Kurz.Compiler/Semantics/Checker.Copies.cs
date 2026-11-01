using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// T33: reading a type in generic code. A generic function that reads its type, and one that passes its type parameter to such a
/// function, is checked only in the copies that calls make (section 4, the owner, 2026-11-01, "Only per copy, as Zig"): each copy is
/// a function of its own, with a copy of the body whose every node is new (<see cref="SyntaxCopy"/>), the names of the type parameters
/// bound to the call's type arguments, and no type parameters, so that the emitter emits it as any function of the file.
/// </summary>
public sealed partial class Checker
{
    /// <summary>The members of a type parameter that read its type (T33; section 4, the parts assumed with the owner's answer "Kinds + parts").</summary>
    private static bool IsTypeMember(string member) => member is "Kind" or "Fields" or "Make" or "Values" or "Item" or "Key" or "Value" or "Inner" or "Cases";

    /// <summary>The deepest chain of copies, each made by a call in the one before, that a build reads.</summary>
    private const int CopyDepth = 64;

    /// <summary>A copy of a generic function that reads its type, for one list of type arguments.</summary>
    private sealed class Copy(FunctionSymbol symbol, FunctionSymbol original, IReadOnlyDictionary<TypeParameterSymbol, KurzType> map, CallExpression origin, Copy? outer)
    {
        public FunctionSymbol Symbol { get; } = symbol;

        public FunctionSymbol Original { get; } = original;

        public IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map { get; } = map;

        /// <summary>The call that made the copy, and the copy whose body holds that call, null where the call stands outside every copy.</summary>
        public CallExpression Origin { get; } = origin;

        public Copy? Outer { get; } = outer;

        public int Depth => Outer is null ? 1 : Outer.Depth + 1;

        /// <summary>The copy that a call outside every copy made, at the end of the chain: where the build stops on a fault in a copy (F-R7).</summary>
        public Copy Outermost => Outer?.Outermost ?? this;

        public KurzType? Bound(string name) => Map.FirstOrDefault(p => p.Key.Name == name).Value;
    }

    /// <summary>The generic functions that read their type, and those that pass their type parameter to one: never checked on their own.</summary>
    private readonly HashSet<FunctionSymbol> readers = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<FunctionSymbol, Copy> copies = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<(FunctionSymbol Original, string Arguments), Copy> copiesByArguments = [];

    /// <summary>Each call of a generic function of the file the checker typed, with the function whose body holds it, null in the top-level code.</summary>
    private readonly List<(FunctionSymbol? Caller, CallExpression Call)> genericCalls = [];

    private readonly HashSet<CallExpression> genericCallSeen = new(ReferenceEqualityComparer.Instance);

    /// <summary>The diagnostics of each generic function's body, which leave the build where the function turns out to read its type.</summary>
    private readonly Dictionary<FunctionSymbol, List<Diagnostic>> genericDiagnostics = new(ReferenceEqualityComparer.Instance);

    /// <summary>What a copy reported, so that a fault of the function's text is reported once and not once for each copy.</summary>
    private readonly HashSet<(string? Id, int Line, string Message)> copyReported = [];

    /// <summary>The function whose body the checker reads, null in the top-level code.</summary>
    private FunctionSymbol? checking;

    /// <summary>The type a copy binds to the name of one of its function's type parameters, in the copy's body and its lambdas.</summary>
    private KurzType? BoundType(string name)
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Copy?.Bound(name) is { } bound)
            {
                return bound;
            }
        }

        return null;
    }

    /// <summary>The copy whose body is read, from a lambda of it too.</summary>
    private Copy? CurrentCopy()
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Copy is { } copy)
            {
                return copy;
            }
        }

        return null;
    }

    /// <summary><c>T.Kind</c> in a copy: the value of <c>TypeKind</c> for the type the copy binds to <c>T</c>, a constant.</summary>
    private Typed? KindOfCopy(string name, string member)
    {
        if (member != "Kind" || TypeParameter(name) is not null || BoundType(name) is not { } bound)
        {
            return null;
        }

        var kinds = enums["TypeKind"];
        return new Typed(new EnumType(kinds), new EnumConstant(kinds.IndexOf(KindOf(bound)!)));
    }

    /// <summary>The name of the value of <c>TypeKind</c> for a type (T33; section 4, assumed with "Kinds + parts"); null for <c>void</c>, which has none.</summary>
    private static string? KindOf(KurzType type) => type switch
    {
        PrimitiveType p => p.Kind.ToString(),
        ListType => "List",
        MapType => "Map",
        UnionType u when u.Cases.Any(c => c is NullType) => "Nullable",
        UnionType => "Union",
        DataType => "Data",
        EnumType => "Enum",
        ClassType or HandleType or HasherType => "Class",
        FunctionType or CFunctionType => "Function",
        PointerType => "Pointer",
        VoidType => null,
        _ => throw new InvalidOperationException($"`{type.Text}` has no kind of type"),
    };

    /// <summary>The generic functions whose own text reads their type: a member of <see cref="IsTypeMember"/> on the name of one of their type parameters.</summary>
    private void FindReaders()
    {
        foreach (var f in functions.Values.SelectMany(list => list).Concat(duplicates).Concat(refused).Concat(unreadOverloads))
        {
            if (!f.IsGeneric || f.Declaration is not { } d)
            {
                continue;
            }

            var names = f.TypeParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var reads = false;
            SyntaxCopy.Walk(d, e => reads |= e is MemberAccess { Target: NameExpression n, Member: var m } && names.Contains(n.Name) && IsTypeMember(m));
            if (reads)
            {
                readers.Add(f);
            }
        }
    }

    /// <summary>A body as the loop over the functions checks it: a generic one's diagnostics kept apart, so that they can leave the build.</summary>
    private void CheckBody(FunctionSymbol f)
    {
        checking = f;
        if (f.IsGeneric)
        {
            Push();
            Unread(f, () => CheckFunctionBody(f));
            genericDiagnostics[f] = Pop(commit: true);
        }
        else
        {
            Unread(f, () => CheckFunctionBody(f));
        }

        checking = null;
    }

    /// <summary>A call of a generic function of the file, kept for <see cref="JoinPassingReaders"/> and <see cref="MakeCopies"/>.</summary>
    private void KeepGenericCall(CallExpression c, FunctionSymbol? callee)
    {
        if (callee is { IsGeneric: true } && genericCallSeen.Add(c))
        {
            genericCalls.Add((checking, c));
        }
    }

    /// <summary>
    /// A generic function that passes its type parameter to a function that reads its type, in a type argument it writes or one the
    /// checker inferred, reads it too, until no function joins; what the generic check of a function that joined reported leaves the build.
    /// </summary>
    private void JoinPassingReaders()
    {
        var joined = new List<FunctionSymbol>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (caller, c) in genericCalls)
            {
                if (caller is not { IsGeneric: true } || readers.Contains(caller) || CalledReader(c) is not { } info)
                {
                    continue;
                }

                if (info.Map.Values.Any(t => caller.TypeParameters.Any(p => Types.Mentions(t, p))))
                {
                    readers.Add(caller);
                    joined.Add(caller);
                    changed = true;
                }
            }
        }

        foreach (var f in joined)
        {
            foreach (var d in genericDiagnostics.GetValueOrDefault(f) ?? [])
            {
                diagnostics.Remove(d);
            }
        }

        foreach (var f in readers)
        {
            model.RecordReader(f);
        }
    }

    /// <summary>The call's record where it calls a function that reads its type.</summary>
    private CallInfo? CalledReader(CallExpression c) =>
        model.TryCallOf(c) is { Kind: CallKind.Function, Function: { } f } info && readers.Contains(f) ? info : null;

    /// <summary>
    /// Every call of a function that reads its type from code that is checked, the top-level code, a function that reads none and a copy,
    /// calls a copy for its type arguments: made at the first such call and checked once, the calls of a copy's body before the calls
    /// that follow the call that made the copy, so that the first call in the order of the source that needs a copy makes it.
    /// </summary>
    private void MakeCopies() => MakeCopies(0, genericCalls.Count);

    private void MakeCopies(int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            var (caller, c) = genericCalls[i];
            if ((caller is not null && readers.Contains(caller)) || CalledReader(c) is not { } info)
            {
                continue;
            }

            var outer = caller is null ? null : copies.GetValueOrDefault(caller);
            if (info.Map.Values.Any(t => Types.IsError(t) || t is TypeParameterType))
            {
                continue; // a type argument that was reported, or a caller that is no copy of a reader and so reads no type parameter here
            }

            if (info.Map.Values.FirstOrDefault(t => KindOf(t) is null) is { } kindless)
            {
                ReportOnce(Diagnostic.NotSupported(c.Line, $"`{kindless.Text}` as a type argument of `{info.Function!.Name}`, which reads its type, `{kindless.Text}` having no kind of `TypeKind` (T33)"));
                continue;
            }

            var copy = CopyOf(info.Function!, info.Map, c, outer);
            if (copy is not null)
            {
                model.RecordCall(c, info with { Function = copy.Symbol, Map = NoMap });
            }
        }
    }

    /// <summary>The copy of a function for a list of type arguments, made and checked at its first call; null where the chain is too deep to read.</summary>
    private Copy? CopyOf(FunctionSymbol original, IReadOnlyDictionary<TypeParameterSymbol, KurzType> map, CallExpression origin, Copy? outer)
    {
        var arguments = string.Join(", ", original.TypeParameters.Select(p => map[p].Text));
        if (copiesByArguments.TryGetValue((original, arguments), out var made))
        {
            return made;
        }

        if ((outer?.Depth ?? 0) >= CopyDepth)
        {
            var first = outer!.Outermost;
            ReportOnce(Diagnostic.NotSupported(first.Origin.Line, $"a chain of more than {CopyDepth} copies of generic functions that read their type, from `{first.Symbol.Name}` (T33)"));
            return null;
        }

        var declaration = SyntaxCopy.Copy(original.Declaration!);
        var symbol = new FunctionSymbol($"{original.Name}<{arguments}>", declaration) { ReturnType = Types.Substitute(original.ReturnType, map) };
        symbol.Parameters.AddRange(original.Parameters.Select(p => p with { Type = Types.Substitute(p.Type, map) }));
        var copy = new Copy(symbol, original, map, origin, outer);
        copiesByArguments[(original, arguments)] = copy;
        copies[symbol] = copy;
        model.RecordFunction(declaration, symbol);
        model.RecordCopy(original, symbol);
        CheckCopy(copy);
        return copy;
    }

    /// <summary>The body of a copy, what it reports kept where the same fault of the function's text was not reported by another copy.</summary>
    private void CheckCopy(Copy copy)
    {
        var (savedFrame, savedChecking) = (frame, checking);
        checking = copy.Symbol;
        var start = genericCalls.Count;
        Push();
        CheckFunctionBody(copy.Symbol);
        foreach (var d in Pop(commit: false))
        {
            ReportOnce(d);
        }

        (frame, checking) = (savedFrame, savedChecking);
        MakeCopies(start, genericCalls.Count); // the copies its body calls
    }

    private void ReportOnce(Diagnostic d)
    {
        if (copyReported.Add((d.Id, d.Line, d.Message)))
        {
            Add(d);
        }
    }

    /// <summary>
    /// F-R7: a <c>match</c> on <c>T.Kind</c> in a copy with no arm for the kind of the type the copy binds: the build stops at the call
    /// that made the outermost copy, with an error that names that copy, the type, its kind and the function.
    /// </summary>
    private void ReportKindNotRead(Copy copy, KurzType type)
    {
        var outermost = copy.Outermost;
        Report( // into the copy's own list, which CheckCopy reports once
            ErrorIds.KindNotRead,
            outermost.Origin.Line,
            $"`{outermost.Symbol.Name}`: `{type.Text}` is of the kind `TypeKind.{KindOf(type)}`, which no arm of the `match` on `T.Kind` in `{copy.Original.Name}` takes (T33)");
    }

    /// <summary>The variables a body that a copy does not read names, which count as read there, as a use in a branch that never runs counts.</summary>
    private void CountNamesRead(Statement body)
    {
        SyntaxCopy.Walk(body, e =>
        {
            if (e is NameExpression n && frame.Scope is { } scope)
            {
                for (var s = scope; s is not null; s = s.Parent)
                {
                    if (s.Variables.TryGetValue(n.Name, out var v))
                    {
                        v.Read = true;
                        break;
                    }
                }
            }
        });
    }
}
