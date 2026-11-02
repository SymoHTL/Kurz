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

        /// <summary>The path of fields, from the type of the outermost copy, that led to a type argument of this copy: <c>Order.Source</c>.</summary>
        public Dictionary<TypeParameterSymbol, string> Paths { get; } = [];

        public KurzType? Bound(string name) => Map.FirstOrDefault(p => p.Key.Name == name).Value;

        /// <summary>The path of fields that led to a type this copy binds, null where none did.</summary>
        public string? PathOf(KurzType type) => Map.Where(p => p.Value.Text == type.Text && Paths.ContainsKey(p.Key)).Select(p => Paths[p.Key]).FirstOrDefault();
    }

    /// <summary>T33: a pass of a loop over <c>T.Fields</c> in a copy: the name the loop gives a field, the data type it walks, the field, and the field's type there.</summary>
    private sealed record FieldPass(string Variable, string TypeName, DataType Walked, FieldSymbol Field, KurzType Type);

    /// <summary>The variable of each pass, which stands in <c>f.Name</c>, <c>f.Type</c> and <c>f.Get(value)</c> alone.</summary>
    private readonly Dictionary<VariableSymbol, FieldPass> passVariables = new(ReferenceEqualityComparer.Instance);

    /// <summary>What <c>f.Get(value)</c> became in a pass, <c>(value).Field</c>, with its pass: its value is held to the type the loop walks.</summary>
    private readonly Dictionary<MemberAccess, FieldPass> fieldGets = new(ReferenceEqualityComparer.Instance);

    /// <summary>The passes being checked, innermost last: a call of a function that reads its type keeps the one it stands in, for the path of fields.</summary>
    private readonly List<FieldPass> activePasses = [];

    /// <summary>The generic functions that read their type, and those that pass their type parameter to one: never checked on their own.</summary>
    private readonly HashSet<FunctionSymbol> readers = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<FunctionSymbol, Copy> copies = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<(FunctionSymbol Original, string Arguments), Copy> copiesByArguments = [];

    /// <summary>Each call of a generic function of the file the checker typed, with the function whose body holds it, null in the top-level code, and the pass of a loop over <c>T.Fields</c> it stands in.</summary>
    private readonly List<(FunctionSymbol? Caller, CallExpression Call, FieldPass? Pass)> genericCalls = [];

    private readonly HashSet<CallExpression> genericCallSeen = new(ReferenceEqualityComparer.Instance);

    /// <summary>The diagnostics of each generic function's body, which leave the build where the function turns out to read its type.</summary>
    private readonly Dictionary<FunctionSymbol, List<Diagnostic>> genericDiagnostics = new(ReferenceEqualityComparer.Instance);

    /// <summary>What a copy reported, so that a fault of the function's text is reported once and not once for each copy.</summary>
    private readonly HashSet<(string? Id, int Line, string Message)> copyReported = [];

    /// <summary>The function whose body the checker reads, null in the top-level code.</summary>
    private FunctionSymbol? checking;

    /// <summary>The type a copy binds to the name of one of its function's type parameters, in the copy's body and its lambdas.</summary>
    private KurzType? BoundType(string name) => Binding(name) is { } bound and not ConstantArgument ? bound : null;

    /// <summary>T34: the constant a copy binds to the name of a type parameter that holds one.</summary>
    private ConstantArgument? BoundConstant(string name) => Binding(name) as ConstantArgument;

    private KurzType? Binding(string name)
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

    /// <summary>
    /// The generic functions whose own text reads their type: a member of <see cref="IsTypeMember"/> on the name of one of their type
    /// parameters, a type parameter that holds a constant (T34), which each copy binds to its value, or a <c>const assert</c>, which
    /// each copy checks (T35).
    /// </summary>
    private void FindReaders()
    {
        foreach (var f in functions.Values.SelectMany(list => list).Concat(duplicates).Concat(refused).Concat(unreadOverloads))
        {
            if (!f.IsGeneric || f.Declaration is not { } d)
            {
                continue;
            }

            var names = f.TypeParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var reads = f.TypeParameters.Any(p => p.Constant is EnumType); // T34: its body reads the constant a copy binds
            SyntaxCopy.Walk(d, e => reads |= e is MemberAccess { Target: NameExpression n, Member: var m } && names.Contains(n.Name) && IsTypeMember(m), s => reads |= s is ConstAssertStatement);
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
            genericCalls.Add((checking, c, activePasses.Count == 0 ? null : activePasses[^1]));
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
            foreach (var (caller, c, _) in genericCalls)
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
            var (caller, c, pass) = genericCalls[i];
            if ((caller is not null && readers.Contains(caller)) || CalledReader(c) is not { } info)
            {
                continue;
            }

            var outer = caller is null ? null : copies.GetValueOrDefault(caller);
            if (info.Map.Values.Any(t => Types.IsError(t) || t is TypeParameterType))
            {
                continue; // a type argument that was reported, or a caller that is no copy of a reader and so reads no type parameter here
            }

            if (info.Map.Values.FirstOrDefault(t => t is not ConstantArgument && KindOf(t) is null) is { } kindless)
            {
                ReportOnce(Diagnostic.NotSupported(c.Line, $"`{kindless.Text}` as a type argument of `{info.Function!.Name}`, which reads its type, `{kindless.Text}` having no kind of `TypeKind` (T33)"));
                continue;
            }

            var copy = CopyOf(info.Function!, info.Map, c, outer, pass);
            if (copy is not null)
            {
                model.RecordCall(c, info with { Function = copy.Symbol, Map = NoMap });
            }
        }
    }

    /// <summary>
    /// The copy of a function for a list of type arguments, made and checked at its first call; null where the chain is too deep to read.
    /// A type argument that is the field's type of the pass the call stands in has the path to that field, and one its caller bound has the caller's path.
    /// </summary>
    private Copy? CopyOf(FunctionSymbol original, IReadOnlyDictionary<TypeParameterSymbol, KurzType> map, CallExpression origin, Copy? outer, FieldPass? pass)
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
        foreach (var (parameter, type) in map)
        {
            if (pass is not null && type.Text == pass.Type.Text)
            {
                copy.Paths[parameter] = $"{outer?.PathOf(pass.Walked) ?? pass.Walked.Text}.{pass.Field.Name}";
            }
            else if (outer?.PathOf(type) is { } path)
            {
                copy.Paths[parameter] = path;
            }
        }

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
            $"`{outermost.Symbol.Name}`: {Named(copy, type)} is of the kind `TypeKind.{KindOf(type)}`, which no arm of the `match` on `T.Kind` in `{copy.Original.Name}` takes (T33)");
    }

    /// <summary>A type a copy binds, named by the path of fields that led to it where one did: <c>`Order.Source` of the type `Source`</c>.</summary>
    private static string Named(Copy copy, KurzType type) =>
        copy.PathOf(type) is { } path ? $"`{path}` of the type `{type.Text}`" : $"`{type.Text}`";

    /// <summary>
    /// T33: <c>for f in T.Fields</c> in a copy, unrolled: its body is checked once for each field of the primary constructor of the
    /// <c>data</c> type the copy binds to <c>T</c> (D14), in the order of their declaration, each pass a copy of the body's syntax in which
    /// <c>f.Get(value)</c> reads <c>(value).Field</c> (section 4, assumed with "for f in T.Fields"); <c>f.Name</c> is a constant
    /// <c>string</c> there and <c>f.Type</c> the field's type. Where the copy binds a type of another kind, the loop is
    /// <c>kind-not-read</c> (section 4, assumed with "Fixed compile error"). A <c>break</c> and a <c>continue</c> of the loop are
    /// <c>not supported</c>. False where the loop walks no <c>T.Fields</c> of a copy.
    /// </summary>
    private bool CheckFieldsLoop(ForInStatement f)
    {
        if (Unwrap(f.Collection) is not MemberAccess { Target: NameExpression n, Member: "Fields", Conditional: false }
            || Lookup(n.Name).Variable is not null || BoundType(n.Name) is not { } bound || CurrentCopy() is not { } copy)
        {
            return false;
        }

        if (f.Second is not null)
        {
            Report(ErrorIds.TypeMismatch, f.Collection.Line, $"two names walk the entries of a map, not the fields of `{n.Name}` (C6, T33)");
            CountNamesRead(f.Body);
            return true;
        }

        if (bound is not DataType walked)
        {
            var outermost = copy.Outermost;
            Report( // into the copy's own list, as for a `match` on `T.Kind`
                ErrorIds.KindNotRead,
                outermost.Origin.Line,
                $"`{outermost.Symbol.Name}`: {Named(copy, bound)} is of the kind `TypeKind.{KindOf(bound)}`, and `{n.Name}.Fields` in `{copy.Original.Name}` walks the fields of a `data` type alone (T33)");
            CountNamesRead(f.Body);
            return true;
        }

        var passes = new List<Block>();
        foreach (var field in walked.Symbol.Fields)
        {
            var pass = new FieldPass(f.Variable, n.Name, walked, field, SubstituteData(walked, field.Type));
            var body = SyntaxCopy.Copy(f.Body, (e, copyOf) => FieldGet(e, copyOf, pass));
            passes.Add(body);
            PushScope();
            DeclareChecked(f.Variable, Types.Error, false, VariableKind.Loop, f.VariableLine, f);
            passVariables[frame.Scope.Variables[f.Variable]] = pass;
            activePasses.Add(pass);
            frame.Loops.Add(new LoopExits { Unrolled = true });
            frame.LoopDepth++;
            CheckBlock(body);
            frame.LoopDepth--;
            frame.Loops.RemoveAt(frame.Loops.Count - 1);
            activePasses.RemoveAt(activePasses.Count - 1);
            PopScope();
        }

        if (passes.Count == 0)
        {
            CountNamesRead(f.Body); // a type with no fields: the body runs in no pass, and what it names counts as read
        }

        model.RecordPasses(f, passes);
        return true;
    }

    /// <summary>A pass's copy of <c>f.Get(value)</c>, with one value and no name, as <c>(value).Field</c>; null for any other expression.</summary>
    private MemberAccess? FieldGet(Expression e, Func<Expression, Expression> copyOf, FieldPass pass)
    {
        if (e is not CallExpression { Callee: MemberAccess { Target: NameExpression target, Member: "Get", Conditional: false }, TypeArguments.Count: 0, Arguments: [{ Name: null, Mut: false } argument] } call
            || target.Name != pass.Variable)
        {
            return null;
        }

        var read = new MemberAccess(call.Line, new ParenthesizedExpression(argument.Value.Line, copyOf(argument.Value)), pass.Field.Name, false);
        fieldGets[read] = pass;
        return read;
    }

    /// <summary>The variable's pass where it is the variable of a loop over <c>T.Fields</c>: <c>f.Name</c> and <c>f.Type</c> in a lambda capture no <c>f</c>, which holds no value.</summary>
    private FieldPass? PassOf(string name) => Lookup(name, capture: false).Variable is { } v && passVariables.TryGetValue(v, out var pass) ? pass : null;

    /// <summary><c>f</c> of a loop over <c>T.Fields</c> where it stands as a value, or with a member but <c>Name</c> or a call but <c>Get</c>: <c>syntax</c> (section 4, assumed).</summary>
    private Typed FieldVariableAsValue(FieldPass pass, int line)
    {
        Report(ErrorIds.Syntax, line, $"`{pass.Variable}` walks the fields of `{pass.TypeName}`, and stands only in `{pass.Variable}.Name`, `{pass.Variable}.Type` as a type and `{pass.Variable}.Get(value)` (T33)");
        return Typed.Error;
    }

    /// <summary><c>f.Name</c>, a constant <c>string</c>; null for any other member, whose <c>f</c> the check of a name finds no value (<see cref="FieldVariableAsValue"/>).</summary>
    private Typed? PassMember(MemberAccess m) =>
        m is { Target: NameExpression n, Member: "Name", Conditional: false } && PassOf(n.Name) is { } pass ? new Typed(Types.String, new StringConstant(pass.Field.Name)) : null;

    /// <summary><c>(value).Field</c>, what <c>f.Get(value)</c> became: its value is of the type the loop walks, or one derived from it.</summary>
    private Typed CheckFieldGet(MemberAccess m, FieldPass pass)
    {
        var target = CheckExpression(m.Target, pass.Walked);
        if (target.IsError)
        {
            return Typed.Error;
        }

        if (!Types.Assignable(target.Type, pass.Walked))
        {
            ReportMismatch(target.Type, pass.Walked, m.Line, $"`{pass.Variable}.Get` reads a field of {Types.A(pass.Walked)}, not of {Types.A(target.Type)}", "T33");
            return Typed.Error;
        }

        return Narrowed(m, MemberOf(target.Type, m.Member, m.Line));
    }

    /// <summary>
    /// <c>f.Type</c> as a type in a pass, the field's type; a value of an enum where a type stands, <c>Say&lt;Mode.Loud&gt;</c> of a
    /// <c>Say&lt;T&gt;</c>, is <c>type-mismatch</c> (T34, T1); any other dotted name as a type is <c>not supported</c>.
    /// </summary>
    private KurzType ResolveDotted(NamedType n)
    {
        var dot = n.Name.IndexOf('.');
        if (n.Name[(dot + 1)..] == "Type" && n.TypeArguments.Count == 0 && PassOf(n.Name[..dot]) is { } pass)
        {
            return pass.Type;
        }

        var last = n.Name.LastIndexOf('.');
        if (n.TypeArguments.Count == 0 && enums.TryGetValue(n.Name[..last], out var named) && named.Values.Contains(n.Name[(last + 1)..]))
        {
            Report(ErrorIds.TypeMismatch, n.Line, $"`{n.Name}` is a value of `{named.Name}`, where a type stands (T34, T1)");
            return Types.Error;
        }

        NotSupported(n.Line, $"a dotted name as a type, `{n.Name}`, but `f.Type` of a loop over `T.Fields` (T33)");
        return Types.Error;
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
