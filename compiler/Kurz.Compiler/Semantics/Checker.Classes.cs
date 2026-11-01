using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Chapter 7: a <c>class</c> with its primary constructor or none, its fields with and without <c>mut</c> (K1), its methods (K7, K9),
/// <c>this</c>, the visibility of its members (F9), its text (A4, A5, A9, A15), the instance as a reference with identity whose
/// <c>mut</c> fields change through every reference (K2, K3), the narrowing of a path through such a field (N9), and the cycle rule
/// judged by the types of the fields (R2, R4). What else a class can write, a base or an interface, <c>equal by</c>, a field or a
/// constructor in its body, a <c>static</c>, <c>virtual</c>, <c>override</c> or <c>prot</c> member, type parameters, and a field
/// that holds a function value, whose cycle R9 judges by the lambdas of the program, is <c>not supported</c>, and so are <c>==</c>
/// on two instances (K4) and an instance in a map's key (K12) (<c>compiler/README.md</c>).
/// </summary>
public sealed partial class Checker
{
    private readonly Dictionary<string, ClassSymbol> classes = new(StringComparer.Ordinal);
    private readonly List<ClassSymbol> classDeclared = [];

    /// <summary>
    /// K1, K7: a class the checker reads is declared with its methods; one that writes what the checker does not read is <c>not
    /// supported</c> at that line, and its name joins the types the checker does not read, so that a use of it carries nothing more (E1).
    /// </summary>
    private void DeclareClass(TypeDeclaration d)
    {
        if (UnreadClassPart(d) is { } unread)
        {
            NotSupported(unread.Line, unread.Construct);
            unsupportedTypes.Add(d.Name);
            unsupportedBases[d.Name] = "a class a data type names (chapter 7)";
            return;
        }

        var symbol = new ClassSymbol(d.Name, d);
        unsupportedBases[d.Name] = "a class a data type names (chapter 7)"; // D6: a data type's base is a data type
        model.RecordClass(d, symbol);
        classes[d.Name] = symbol;
        classDeclared.Add(symbol);
        foreach (var f in (d.Members ?? []).OfType<FunctionDeclaration>())
        {
            var method = new FunctionSymbol(f.Name, f) { Owner = symbol };
            model.RecordFunction(f, method);
            if (!symbol.Methods.TryGetValue(f.Name, out var list))
            {
                list = [];
                symbol.Methods[f.Name] = list;
            }

            list.Add(method);
        }
    }

    /// <summary>The first part of a class's declaration that the checker does not read, with its line; null where it reads all of it.</summary>
    private static (int Line, string Construct)? UnreadClassPart(TypeDeclaration d)
    {
        if (d.TypeParameters.Count > 0)
        {
            return (d.Line, "a generic class (T19, chapter 7)");
        }

        if (d.Bases.Count > 0)
        {
            return (d.Bases[0].Line, "a base class or an interface after `:` (K6, K10)");
        }

        if (d.EqualBy.Count > 0)
        {
            return (d.Line, "`equal by` (K8)");
        }

        if ((d.PrimaryConstructor ?? []).Select(p => WeakIn(p.Type)).FirstOrDefault(w => w is not null) is { } weak)
        {
            return (weak.Line, "weak (R3, R5)"); // R2: a `weak` field is no strong field, so the cycle rule cannot judge a class until `weak` is read
        }

        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case FieldDeclaration { Modifiers: var modifiers } f when (modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` field (K18)");
                case FieldDeclaration f:
                    return (f.Line, "a field declared in the body of a class (K7, K14)");
                case ConstructorDeclaration c:
                    return (c.Line, "a constructor declared in the body of a class (K14)");
                case FunctionDeclaration f when (f.Modifiers & (Modifiers.Static | Modifiers.Virtual | Modifiers.Override | Modifiers.Prot | Modifiers.Extern)) != 0:
                    return (f.Line, $"the modifier `{(f.Modifiers & (Modifiers.Static | Modifiers.Virtual | Modifiers.Override | Modifiers.Prot | Modifiers.Extern)).ToString().ToLowerInvariant()}` of a method (K7, K18, R16)");
                case FunctionDeclaration f when f.TypeParameters.Count > 0:
                    return (f.Line, "a method with type parameters of its own (section 4, T19)");
                default:
                    break;
            }
        }

        return null;
    }

    /// <summary>R3: the first <c>weak</c> a written type holds, at any depth.</summary>
    private static WeakType? WeakIn(TypeSyntax? type) => type switch
    {
        WeakType w => w,
        NamedType n => n.TypeArguments.Select(WeakIn).FirstOrDefault(w => w is not null),
        NullableType n => WeakIn(n.Inner),
        Syntax.UnionType u => u.Cases.Select(WeakIn).FirstOrDefault(w => w is not null),
        Syntax.FunctionType f => f.Parameters.Append(f.Result).Select(WeakIn).FirstOrDefault(w => w is not null),
        _ => null,
    };

    /// <summary>
    /// K1: the fields of a class, those of its primary constructor in their order, <c>mut</c> where written; a field and a method of
    /// one name, and a name two fields have, are <c>redeclared</c> (V9).
    /// </summary>
    private void ResolveClass(ClassSymbol symbol)
    {
        var d = symbol.Declaration;
        foreach (var p in d.PrimaryConstructor ?? [])
        {
            var type = p.Type is null ? Types.Error : Resolve(p.Type);
            if (p.Type is null)
            {
                Report(ErrorIds.Syntax, p.Line, $"the field `{p.Name}` of a class names its type (K1)");
            }
            else if (Types.IsVoid(type))
            {
                Report(ErrorIds.Syntax, p.Line, "`void` is no type of a field (K1, F4)");
                type = Types.Error;
            }

            if (symbol.Field(p.Name) is not null)
            {
                Report(ErrorIds.Redeclared, p.Line, $"the field `{p.Name}` is declared already (V9)");
            }

            symbol.Fields.Add(new FieldSymbol(p.Name, type, p.Default is not null, p.Mut, p.Line));
        }

        foreach (var (name, methods) in symbol.Methods)
        {
            if (symbol.Field(name) is not null)
            {
                Report(ErrorIds.Redeclared, methods[0].Line, $"`{name}` is a field of `{symbol.Name}` already (V9)");
            }
        }
    }

    /// <summary>
    /// K7, F1: the parameter and result types of each method. A method takes no <c>mut</c> marker, which is for values (K9, M5): one
    /// written is text no rule gives a meaning (E4), and so is <c>naked</c> before a method (R12). Two methods of one name with the same
    /// parameters are <c>duplicate-function</c>, as two functions are (F13).
    /// </summary>
    private void ResolveMethods()
    {
        foreach (var symbol in classDeclared)
        {
            foreach (var method in symbol.Methods.Values.SelectMany(list => list))
            {
                var d = method.Declaration!;
                if ((d.Modifiers & Modifiers.Mut) != 0)
                {
                    Report(ErrorIds.Syntax, d.Line, $"a method of a class takes no `mut` marker: it assigns the `mut` fields of its instance without one (K9, M5; E4)");
                }

                if ((d.Modifiers & Modifiers.Naked) != 0)
                {
                    Report(ErrorIds.Syntax, d.Line, "`naked` stands before a function at the top level of a file alone (R12; E4)");
                }

                ResolveTypes(method, d);
            }

            foreach (var list in symbol.Methods.Values)
            {
                for (var i = 1; i < list.Count; i++)
                {
                    if (list.Take(i).Any(earlier => SameParameters(earlier, list[i])))
                    {
                        Report(ErrorIds.DuplicateFunction, list[i].Line, $"`{symbol.Name}` has a method `{list[i].Name}` with these parameter types already (F13)");
                    }
                }
            }
        }
    }

    /// <summary>F12, D11: the default of a field of a primary constructor is a constant expression, as a parameter's is.</summary>
    private void CheckClassDefaults()
    {
        foreach (var symbol in classDeclared)
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = symbol.Line };
            foreach (var (p, field) in (symbol.Declaration.PrimaryConstructor ?? []).Zip(symbol.Fields))
            {
                if (p.Default is not null)
                {
                    CheckDefault(p.Default, field.Type, p.Line);
                }
            }
        }
    }

    /// <summary>K7: the body of each method, with its instance as the variable <c>this</c>, through which its fields and methods are read by their names.</summary>
    private void CheckMethodBodies()
    {
        foreach (var symbol in classDeclared)
        {
            foreach (var method in symbol.Methods.Values.SelectMany(list => list))
            {
                var d = method.Declaration!;
                frame = new Frame { ReturnType = method.ReturnType, Line = d.Line, Reserved = reservedTopLevel, Class = symbol };
                var receiver = new VariableSymbol("this", new ClassType(symbol), false, VariableKind.Parameter, d.Line) { Read = true };
                frame.Scope.Variables[receiver.Name] = receiver;
                model.RecordThis(method, receiver);
                foreach (var (p, node) in method.Parameters.Zip(d.Parameters))
                {
                    DeclareParameter(p.Name, p.Type, p.Mut, p.Line, node);
                }

                if (d.ExpressionBody is not null)
                {
                    CheckExpressionBody(d.ExpressionBody, method.ReturnType, d.Line);
                }
                else if (d.BlockBody is not null)
                {
                    var flow = CheckBlock(d.BlockBody);
                    if (flow == Flow.Completes && !SuccessIsVoid(method.ReturnType) && !Types.IsError(method.ReturnType))
                    {
                        Report(ErrorIds.MissingReturn, d.Line, $"the end of `{d.Name}` can be reached without a `return` (F3)");
                    }
                }
                else
                {
                    Report(ErrorIds.Syntax, d.Line, "a method of a class has a body (K7, F2)");
                }

                ReportUnused(frame.Scope);
            }
        }
    }

    // ---- names inside a class ----------------------------------------------------------------------

    /// <summary>The class whose method is being checked, through the lambdas around the place; null outside a method.</summary>
    private ClassSymbol? CurrentClass()
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Class is not null)
            {
                return f.Class;
            }
        }

        return null;
    }

    /// <summary>K7: inside a method, a name that is a field of its class, read through <c>this</c>; a lambda that reads it captures <c>this</c> (F15).</summary>
    private FieldSymbol? ImplicitField(string name) => CurrentClass()?.Field(name);

    /// <summary>K7: inside a method, the methods of its class of a name a call names alone.</summary>
    private List<FunctionSymbol>? ImplicitMethods(string name) => CurrentClass() is { } c && c.Methods.TryGetValue(name, out var list) ? list : null;

    /// <summary>The variable <c>this</c> of the method around the place, read and, from a lambda, captured (F15).</summary>
    private VariableSymbol ThisVariable()
    {
        var (receiver, _) = Lookup("this");
        return receiver ?? throw new InvalidOperationException("no `this` inside a method");
    }

    /// <summary>K7, V9: inside a method, whether a name is a member of its class, which a declaration of the name would hide.</summary>
    private bool IsMemberName(string name) => CurrentClass() is { } c && (c.Field(name) is not null || c.Methods.ContainsKey(name));

    /// <summary>K7: <c>this</c>, the instance a method was called on; outside a method no <c>this</c> is visible (V10).</summary>
    private Typed CheckThis(ThisExpression t)
    {
        if (CurrentClass() is null)
        {
            Report(ErrorIds.UnknownName, t.Line, "`this` is the instance inside a method of a class, and no method is around it (K7, V10)");
            return Typed.Error;
        }

        model.RecordImplicitField(t, ThisVariable(), null);
        return new Typed(new ClassType(CurrentClass()!));
    }

    /// <summary>K7: a read of a field by its name inside a method, the field of <c>this</c>.</summary>
    private Typed ReadImplicitField(NameExpression n, FieldSymbol field)
    {
        model.RecordImplicitField(n, ThisVariable(), field);
        return Narrowed(n, new Typed(field.Type)); // N3, N9
    }

    /// <summary>
    /// K1, K2, K7: an assignment to a field by its name inside a method: the field of <c>this</c>, which takes a value only where it is
    /// <c>mut</c>; inside a lambda it is the change of a captured value, <c>capture-assign</c> (F11).
    /// </summary>
    private void AssignImplicitField(AssignOrDeclare a, FieldSymbol field)
    {
        var (receiver, captured) = Lookup("this");
        model.RecordImplicitField(a, receiver!, field);
        if (captured)
        {
            Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `this` and cannot change its field `{a.Name}` (F11)");
        }
        else if (!field.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{a.Name}` is a field without `mut`, which nothing assigns (K1)");
        }

        EndChangeableNarrowing(); // N9: an assignment ends what was known of a path through a `mut` field
        var value = CheckExpression(a.Value, field.Type, keepCases: Types.KeepsCases(field.Type));
        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, field.Type))
        {
            ReportMismatch(value.Type, field.Type, a.Value.Line, $"{Types.A(value.Type)} where {Types.A(field.Type)} is required");
        }
        else if (!captured && field.Mut)
        {
            Assigned(new NullPath(receiver!, field.Name), field.Type, value); // N9: a path with no `weak` reference is narrowed by its assignment
        }
    }

    // ---- members of an instance ----------------------------------------------------------------------

    /// <summary>
    /// K1, F9, D14: a field of an instance, which every code reads, the fields of a primary constructor being visible wherever the
    /// class is; a method named without a call is no value (F11).
    /// </summary>
    private Typed ClassMember(ClassType type, string member, int line)
    {
        if (type.Symbol.Field(member) is { } field)
        {
            return new Typed(field.Type);
        }

        if (type.Symbol.Methods.ContainsKey(member))
        {
            NotSupported(line, "a method as a value, named without a call (K7, F11)");
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, line, $"`{type.Symbol.Name}` has no member `{member}` (V10)");
        return Typed.Error;
    }

    /// <summary>F9: a method without <c>pub</c> is private to its class; a call of it from outside the class is <c>not-visible</c>.</summary>
    private bool Visible(FunctionSymbol method) =>
        (method.Declaration!.Modifiers & Modifiers.Pub) != 0 || ReferenceEquals(CurrentClass(), method.Owner);

    /// <summary>K7, F13: a call of a method on an instance, <c>c.Add(1)</c>, the one of its name whose parameters fit; a private one from outside its class is <c>not-visible</c> (F9).</summary>
    private Typed CallMethod(ClassType type, MemberAccess m, CallExpression c, KurzType? expected)
    {
        if (!type.Symbol.Methods.TryGetValue(m.Member, out var methods))
        {
            if (type.Symbol.Field(m.Member) is { } field)
            {
                var value = new Typed(field.Type);
                model.RecordType(m, value);
                return CallValue(field.Type, c); // F11: a field of function type is called as any function value
            }

            Report(ErrorIds.UnknownName, m.Line, $"`{type.Symbol.Name}` has no method `{m.Member}` (V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return PickMethod(methods, c, expected, m.Line);
    }

    /// <summary>The method of the candidates a call picks (F13), recorded as a call of a method; <c>not-visible</c> where it is private and the call stands outside its class (F9).</summary>
    private Typed PickMethod(List<FunctionSymbol> methods, CallExpression c, KurzType? expected, int line)
    {
        var typed = ResolveCall(methods, c, expected, out var picked, CallKind.Method);
        if (picked is not null && !Visible(picked))
        {
            Report(ErrorIds.NotVisible, line, $"`{picked.Name}` of `{picked.Owner!.Name}` is private to its class: a method is visible outside it with `pub` (F9)");
            return Typed.Error;
        }

        return typed;
    }

    /// <summary>K7: a call that names a method of the class alone, inside one of its methods: the method of <c>this</c>.</summary>
    private Typed CallImplicitMethod(List<FunctionSymbol> methods, NameExpression n, CallExpression c, KurzType? expected)
    {
        model.RecordImplicitField(n, ThisVariable(), null);
        return PickMethod(methods, c, expected, n.Line);
    }

    /// <summary>K1: an instance made by the class's name, with the arguments of its primary constructor, bound as D11 binds them; a class without one takes none.</summary>
    private Typed CheckClassConstruction(ClassSymbol symbol, CallExpression c, KurzType? expected)
    {
        var slots = symbol.Fields.Select(f => new Slot(f.Name, f.Type, false, f.HasDefault, false)).ToList();
        var candidate = new Candidate { Slots = slots, Result = Types.Void };
        if (c.TypeArguments.Count > 0)
        {
            Report(ErrorIds.Syntax, c.Line, $"`{symbol.Name}` takes no type arguments (T19; E4)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var typed = Call([candidate], c, expected);
        if (typed.IsError)
        {
            return typed;
        }

        var made = new ClassType(symbol);
        model.RecordCall(c, new CallInfo(CallKind.ClassNew, null, null, made, NoMap, candidate.Binding, candidate.Slots.Count, made));
        return new Typed(made);
    }

    // ---- writes through an instance ----------------------------------------------------------------

    /// <summary>
    /// The type a path of a name and fields has without reading it, where every step is a variable, <c>this</c>, a field by its name
    /// inside a method, or a field of a data value or an instance; null for any other expression.
    /// </summary>
    private KurzType? PathType(Expression e)
    {
        switch (Unwrap(e))
        {
            case NameExpression n when Lookup(n.Name).Variable is { } v:
                return v.Type;
            case NameExpression n when ImplicitField(n.Name) is { } field:
                return field.Type;
            case ThisExpression when CurrentClass() is { } c:
                return new ClassType(c);
            case MemberAccess { Conditional: false } m when PathType(m.Target) is { } target:
                return target switch
                {
                    DataType d when d.Symbol.Field(m.Member) is { } field => SubstituteData(d, field.Type),
                    ClassType c when c.Symbol.Field(m.Member) is { } field => field.Type,
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>Whether the path, read as <see cref="PathType"/> reads it, runs through an instance of a class before its last member.</summary>
    private bool RunsThroughInstance(IReadOnlyList<MemberAccess> members) =>
        members.Any(m => PathType(m.Target) is ClassType);

    /// <summary>
    /// K1, K2: an assignment of a field of an instance, <c>c.Count = 1</c>: the field is <c>mut</c>, the variable that holds the
    /// reference need not be, since the reference does not change; through a lambda's captured value it is <c>capture-assign</c> (F11).
    /// A path that goes on into a data value after the instance's field is <c>not supported</c>. The value is read before the path, as
    /// M4 reads an assignment into a path (the emitter's order).
    /// </summary>
    private void AssignThroughInstance(Assignment a, List<MemberAccess> members)
    {
        var lastInstance = members.FindLastIndex(m => PathType(m.Target) is ClassType);
        if (lastInstance < members.Count - 1)
        {
            NotSupported(members[lastInstance + 1].Line, "an assignment into a value held by a field of an instance (K2, M4)");
            CheckLoosely(a.Target, a.Value);
            return;
        }

        var last = members[^1];
        var target = CheckExpression(last.Target, null);
        if (target.IsError || target.Type is not ClassType owner)
        {
            CheckExpression(a.Value, Untyped(a.Value));
            return;
        }

        if (owner.Symbol.Field(last.Member) is not { } field)
        {
            ClassMember(owner, last.Member, last.Line);
            CheckExpression(a.Value, Untyped(a.Value));
            return;
        }

        model.RecordType(last, new Typed(field.Type));
        if (CapturedRoot(last.Target) is { } root)
        {
            Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{root}` and cannot change what is reached through it (F11)");
        }
        else if (!field.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{last.Member}` is a field without `mut`, which nothing assigns (K1)");
        }

        EndChangeableNarrowing(); // N9: an assignment ends what was known of a path through a `mut` field
        var value = CheckExpression(a.Value, field.Type, keepCases: Types.KeepsCases(field.Type));
        if (value.IsError)
        {
            return;
        }

        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, field.Type))
        {
            ReportMismatch(value.Type, field.Type, a.Value.Line, $"{Types.A(value.Type)} where {Types.A(field.Type)} is required");
        }
        else
        {
            Assigned(PathOf(a.Target), field.Type, value); // N9: a path with no `weak` reference is narrowed by its assignment
        }
    }

    /// <summary>F11: the name of the variable a path starts at where a lambda captured it, <c>this</c> among them; null where it is the function's own.</summary>
    private string? CapturedRoot(Expression e)
    {
        switch (Unwrap(e))
        {
            case NameExpression n when Lookup(n.Name) is (not null, var captured):
                return captured ? n.Name : null;
            case NameExpression n when ImplicitField(n.Name) is not null:
                return Lookup("this").Captured ? "this" : null;
            case ThisExpression:
                return Lookup("this").Captured ? "this" : null;
            case MemberAccess m:
                return CapturedRoot(m.Target);
            default:
                return null;
        }
    }

    /// <summary>
    /// M5 with K2: the receiver of a <c>mut</c> method that is a <c>mut</c> field of an instance, <c>node.Children.Add(x)</c>, which every
    /// reference reaches (M5's reading of K2 for paths); a field without <c>mut</c> is <c>mut-required</c>, a path that goes on into a
    /// data value after the field is <c>not supported</c>, and inside a lambda the change of a captured value is <c>capture-assign</c> (F11).
    /// Returns false where the receiver is no such field.
    /// </summary>
    private bool MutReceiverThroughInstance(Expression receiver, List<MemberAccess> members, int line, string what)
    {
        if (members.Count == 0 || !RunsThroughInstance(members))
        {
            if (Unwrap(receiver) is NameExpression n && Lookup(n.Name).Variable is null && ImplicitField(n.Name) is { } implicitField)
            {
                return MutField(receiver, implicitField, line, what);
            }

            return false;
        }

        var lastInstance = members.FindLastIndex(m => PathType(m.Target) is ClassType);
        if (lastInstance < members.Count - 1)
        {
            NotSupported(line, "a `mut` method on a value held by a field of an instance (K2, M5)");
            return true;
        }

        var owner = (ClassType)PathType(members[^1].Target)!;
        if (owner.Symbol.Field(members[^1].Member) is not { } field)
        {
            return true; // reported where the receiver was read
        }

        return MutField(receiver, field, line, what);
    }

    private bool MutField(Expression receiver, FieldSymbol field, int line, string what)
    {
        if (CapturedRoot(receiver) is { } root)
        {
            Report(ErrorIds.CaptureAssign, line, $"a lambda reads `{root}` and cannot change what is reached through it (F11)");
        }
        else if (!field.Mut)
        {
            Report(ErrorIds.MutRequired, line, $"{what}, and `{field.Name}` is a field without `mut` (M5, K1)");
        }

        return true;
    }

    // ---- N9: paths that a call can change ---------------------------------------------------------

    /// <summary>
    /// N9: whether a path known not null runs through a <c>mut</c> field of an instance, which a call can assign through another
    /// reference (K2), so that what is known of it lasts up to the next call and the next assignment.
    /// </summary>
    private static bool Changeable(NullPath path)
    {
        var current = path.Root.Type;
        foreach (var name in path.Fields.Length == 0 ? [] : path.Fields.Split('.'))
        {
            switch (Types.NonNull(current) ?? current)
            {
                case ClassType c when c.Symbol.Field(name) is { } field:
                    if (field.Mut)
                    {
                        return true;
                    }

                    current = field.Type;
                    break;
                case DataType d when d.Symbol.Field(name) is { } field:
                    current = SubstituteData(d, field.Type);
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>N9: a call or an assignment ends what was known of every path through a <c>mut</c> field of an instance.</summary>
    private void EndChangeableNarrowing() => frame.Known = Unchangeable(frame.Known);

    /// <summary>N9: what is known of paths that no call can change.</summary>
    private static ImmutableHashSet<NullPath> Unchangeable(ImmutableHashSet<NullPath> known) =>
        known.Any(Changeable) ? known.Where(p => !Changeable(p)).ToImmutableHashSet() : known;

    // ---- the cycle rule ----------------------------------------------------------------------------

    /// <summary>
    /// R2, R4: judged by types, over the file: a class that reaches itself through strong fields, one of them <c>mut</c>, is
    /// <c>reference-cycle</c>. A field reaches every class its type holds, through lists, maps, unions, nullable types and the fields of
    /// data values; each ring is reported once, at the class of the ring declared first, the first class on the path (R9's case). A
    /// class with a field whose type holds a function value is <c>not supported</c>: R9 counts such a field as reaching every class a
    /// lambda of the program captures, which the checker does not judge.
    /// </summary>
    private void CheckReferenceCycles()
    {
        var edges = new Dictionary<ClassSymbol, List<(ClassSymbol To, FieldSymbol Field)>>();
        foreach (var c in classDeclared)
        {
            var list = new List<(ClassSymbol, FieldSymbol)>();
            foreach (var field in c.Fields)
            {
                var reached = new HashSet<ClassSymbol>();
                if (HoldsFunctionValue(field.Type, []))
                {
                    NotSupported(field.Line, "a field that holds a function value, which the cycle rule judges by the lambdas of the program (R9)");
                    continue;
                }

                ClassesHeld(field.Type, reached, []);
                list.AddRange(reached.Select(r => (r, field)));
            }

            edges[c] = list;
        }

        var reported = new HashSet<ClassSymbol>();
        foreach (var start in classDeclared)
        {
            if (reported.Contains(start))
            {
                continue;
            }

            if (RingThroughMut(start, edges) is { } ring)
            {
                var members = ring.Select(step => step.From).Distinct().ToList();
                reported.UnionWith(members);
                var first = members.MinBy(m => classDeclared.IndexOf(m))!;
                var path = string.Join(", ", ring.Select(step => $"`{step.From.Name}.{step.Field.Name}`{(step.Field.Mut ? " (mut)" : "")}"));
                Report(ErrorIds.ReferenceCycle, first.Line, $"`{first.Name}` can reach itself through strong fields, {path}, one of them `mut`, and nothing would free such a ring (R2)");
            }
        }
    }

    /// <summary>A ring of fields from <paramref name="start"/> back to it with a <c>mut</c> field on it; null where there is none.</summary>
    private static List<(ClassSymbol From, FieldSymbol Field)>? RingThroughMut(ClassSymbol start, Dictionary<ClassSymbol, List<(ClassSymbol To, FieldSymbol Field)>> edges)
    {
        // a search over (class, a `mut` field met yet) from the start, back to the start with one met
        var queue = new Queue<(ClassSymbol At, bool Mut)>();
        var cameFrom = new Dictionary<(ClassSymbol, bool), ((ClassSymbol, bool) From, FieldSymbol Field)>();
        queue.Enqueue((start, false));
        var seen = new HashSet<(ClassSymbol, bool)> { (start, false) };
        while (queue.Count > 0)
        {
            var (at, mut) = queue.Dequeue();
            foreach (var (to, field) in edges[at])
            {
                var next = (to, mut || field.Mut);
                if (ReferenceEquals(to, start) && next.Item2)
                {
                    var ring = new List<(ClassSymbol, FieldSymbol)> { (at, field) };
                    for (var step = (at, mut); cameFrom.TryGetValue(step, out var back); step = back.From)
                    {
                        ring.Insert(0, (back.From.Item1, back.Field));
                    }

                    return ring;
                }

                if (seen.Add(next))
                {
                    cameFrom[next] = ((at, mut), field);
                    queue.Enqueue(next);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The type arguments of a data type a field reaches, as <see cref="HasText"/> judges them: a data type met again in a walk is read
    /// through these alone, so that the walk of a type that holds itself with other arguments, <c>data Node&lt;T&gt;(int V,
    /// Node&lt;List&lt;T&gt;&gt;? Next)</c>, ends, and what it holds through its arguments is still found.
    /// </summary>
    private static IEnumerable<KurzType> HeldArguments(DataType d) =>
        d.Symbol.TypeParameters.Zip(d.TypeArguments).Where(p => d.Symbol.Fields.Any(f => Types.Mentions(f.Type, p.First))).Select(p => p.Second);

    /// <summary>The classes a value of the type holds strongly: itself, the items of a list, the keys and values of a map, the cases of a union and the fields of a data value.</summary>
    private static void ClassesHeld(KurzType type, HashSet<ClassSymbol> reached, HashSet<DataSymbol> path)
    {
        switch (type)
        {
            case ClassType c:
                reached.Add(c.Symbol);
                break;
            case ListType l:
                ClassesHeld(l.Element, reached, path);
                break;
            case MapType m:
                ClassesHeld(m.Key, reached, path);
                ClassesHeld(m.Value, reached, path);
                break;
            case UnionType u:
                foreach (var c in u.Cases)
                {
                    ClassesHeld(c, reached, path);
                }

                break;
            case DataType d when path.Add(d.Symbol):
                foreach (var f in d.Symbol.Fields)
                {
                    ClassesHeld(SubstituteData(d, f.Type), reached, path);
                }

                break;
            case DataType d:
                foreach (var a in HeldArguments(d))
                {
                    ClassesHeld(a, reached, path);
                }

                break;
            default:
                break;
        }
    }

    /// <summary>Whether a value of the type holds a function value, outside the instances of classes, whose own fields are judged as theirs.</summary>
    private static bool HoldsFunctionValue(KurzType type, HashSet<DataSymbol> path) => type switch
    {
        FunctionType => true,
        ListType l => HoldsFunctionValue(l.Element, path),
        MapType m => HoldsFunctionValue(m.Key, path) || HoldsFunctionValue(m.Value, path),
        UnionType u => u.Cases.Any(c => HoldsFunctionValue(c, path)),
        DataType d when path.Add(d.Symbol) => d.Symbol.Fields.Any(f => HoldsFunctionValue(SubstituteData(d, f.Type), path)),
        DataType d => HeldArguments(d).Any(a => HoldsFunctionValue(a, path)),
        _ => false,
    };

    // ---- what the checker does not read of instances ------------------------------------------------

    /// <summary>Whether a value of the type holds an instance of a class at any depth, outside the instances themselves.</summary>
    private static bool HoldsInstance(KurzType type, HashSet<DataSymbol> path) => type switch
    {
        ClassType => true,
        ListType l => HoldsInstance(l.Element, path),
        MapType m => HoldsInstance(m.Key, path) || HoldsInstance(m.Value, path),
        UnionType u => u.Cases.Any(c => HoldsInstance(c, path)),
        DataType d when path.Add(d.Symbol) => d.Symbol.Fields.Any(f => HoldsInstance(SubstituteData(d, f.Type), path)),
        DataType d => HeldArguments(d).Any(a => HoldsInstance(a, path)),
        _ => false,
    };

    /// <summary>A5, A9, A15: a class instance's text is its <c>pub Text()</c>; one without <c>pub</c> is <c>not-visible</c> wherever <c>print</c> or an interpolation uses it, inside the class too.</summary>
    private bool PrivateText(KurzType type, int line)
    {
        if (PrivateTextClass(type, []) is not { } c)
        {
            return false;
        }

        Report(ErrorIds.NotVisible, line, $"the `Text()` of `{c.Name}` has no `pub`, and `print` and an interpolation reach it from outside the class, inside it too (A9, A15)");
        return true;
    }

    private static ClassSymbol? PrivateTextClass(KurzType type, HashSet<DataSymbol> path) => type switch
    {
        ClassType c when c.Symbol.TextMethod is { } text && (text.Declaration!.Modifiers & Modifiers.Pub) == 0 => c.Symbol,
        ListType l => PrivateTextClass(l.Element, path),
        MapType m => PrivateTextClass(m.Key, path) ?? PrivateTextClass(m.Value, path),
        UnionType u => u.Cases.Select(c => PrivateTextClass(c, path)).FirstOrDefault(c => c is not null),
        DataType d when path.Add(d.Symbol) => d.Symbol.Fields.Select(f => PrivateTextClass(SubstituteData(d, f.Type), path)).FirstOrDefault(c => c is not null),
        DataType d => HeldArguments(d).Select(a => PrivateTextClass(a, path)).FirstOrDefault(c => c is not null),
        _ => null,
    };
}
