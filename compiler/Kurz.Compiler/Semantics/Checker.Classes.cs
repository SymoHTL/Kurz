using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// Chapter 7: a <c>class</c> with its primary constructor or none, its fields with and without <c>mut</c>, of the primary constructor
/// (K1) and of its body with their defaults (K7), its methods (K7, K9),
/// <c>this</c>, the visibility of its members (F9), its text (A4, A5, A9, A15), the instance as a reference with identity whose
/// <c>mut</c> fields change through every reference (K2, K3), the narrowing of a path through such a field (N9), and the cycle rule
/// judged by the types of the fields (R2, R4), its <c>static</c> methods and fields (K7, K18), and its equality and hash, by identity or
/// by content (K4) or by the fields an <c>equal by</c> clause names (K5, K8, K12, K15), across inheritance (K13); its base class, in the
/// short form and the explicit one (K6, K10, K11, K16, K17), and its <c>virtual</c>, <c>override</c> and <c>prot</c> methods (K7, F9).
/// What else a class can write, an interface, a constructor in its body, a field there without a written type, a field in its body or
/// a <c>static</c> member of a class with a base or with classes derived from it (K6, K7, K18), type parameters, and a field that holds
/// a function value, whose cycle R9 judges by the lambdas of the program, is <c>not supported</c> (<c>compiler/README.md</c>).
/// </summary>
public sealed partial class Checker
{
    private readonly Dictionary<string, ClassSymbol> classes = new(StringComparer.Ordinal);
    private readonly List<ClassSymbol> classDeclared = [];
    private readonly HashSet<ClassSymbol> classesCompared = new(ReferenceEqualityComparer.Instance); // K4: the classes a walk of `==` is inside of
    private readonly HashSet<ClassSymbol> classesResolving = new(ReferenceEqualityComparer.Instance); // K6: the classes whose bases are being resolved

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

        foreach (var f in (d.Members ?? []).OfType<FieldDeclaration>().Where(f => (f.Modifiers & Modifiers.Static) != 0))
        {
            symbol.StaticFields.Add(new StaticFieldSymbol(f, symbol)); // K7, K18: the one field of a class body the checker reads
        }
    }

    /// <summary>The first part of a class's declaration that the checker does not read, with its line; null where it reads all of it.</summary>
    private static (int Line, string Construct)? UnreadClassPart(TypeDeclaration d)
    {
        if (d.TypeParameters.Count > 0)
        {
            return (d.Line, "a generic class (T19, chapter 7)");
        }

        foreach (var member in d.Members ?? [])
        {
            switch (member)
            {
                case FieldDeclaration { Modifiers: var modifiers } f when (modifiers & Modifiers.Static) != 0 && (modifiers & ~(Modifiers.Pub | Modifiers.Internal | Modifiers.Static | Modifiers.Mut)) != 0:
                    return (f.Line, $"the modifier `{(modifiers & ~(Modifiers.Pub | Modifiers.Internal | Modifiers.Static | Modifiers.Mut)).ToString().ToLowerInvariant()}` of a `static` field (K7, K18, R13)");
                case FieldDeclaration { Modifiers: var modifiers } when (modifiers & Modifiers.Static) != 0:
                    break; // K7, K18: read with the class (ResolveStaticField)
                case FieldDeclaration { Type: null } f:
                    return (f.Line, "a field of a class body without a written type (K7, V2)");
                case FieldDeclaration { Modifiers: var modifiers } f when (modifiers & ~(Modifiers.Pub | Modifiers.Internal | Modifiers.Mut)) != 0:
                    return (f.Line, $"the modifier `{(modifiers & ~(Modifiers.Pub | Modifiers.Internal | Modifiers.Mut)).ToString().ToLowerInvariant()}` of a field (K7, F9)");
                case FieldDeclaration:
                    break; // K7: read with the class (ResolveClass)
                case ConstructorDeclaration c:
                    return (c.Line, "a constructor declared in the body of a class (K14)");
                case FunctionDeclaration { Property: true } f when (f.Modifiers & Modifiers.Static) != 0:
                    return (f.Line, "a `static` property (K7, K19)"); // section 14 of the record: open
                case FunctionDeclaration { Property: true } f when (f.Modifiers & (Modifiers.Virtual | Modifiers.Override | Modifiers.Prot)) != 0:
                    return (f.Line, $"the modifier `{(f.Modifiers & (Modifiers.Virtual | Modifiers.Override | Modifiers.Prot)).ToString().ToLowerInvariant()}` of a property, which section 14 leaves open (K7)");
                case FunctionDeclaration f when (f.Modifiers & Modifiers.Extern) != 0:
                    return (f.Line, $"the modifier `extern` of a {(f.Property ? "property" : "method")} (K7, R16)");
                case FunctionDeclaration f when f.TypeParameters.Count > 0:
                    return (f.Line, "a method with type parameters of its own (section 4, T19)");
                case FunctionDeclaration:
                    break;
                default:
                    return (member.Line, "a type declared in the body of a class (K7)");
            }
        }

        return null;
    }

    // ---- weak (R3, R5) -----------------------------------------------------------------------------------

    /// <summary>
    /// R3, R5: <c>weak T?</c> of a class <c>T</c>. Before a type whose values are no instances of a class it is <c>weak-value</c>, and
    /// without its <c>?</c> <c>weak-not-nullable</c>; before a type parameter or a union of classes the checker does not read it (T19, D9).
    /// </summary>
    private KurzType ResolveWeak(WeakType w)
    {
        var target = Resolve(w.Inner is NullableType nullable ? nullable.Inner : w.Inner);
        if (target is ErrorType)
        {
            return Types.Error;
        }

        if (target is TypeParameterType || (target is UnionType u && u.Cases.Any(c => c is ClassType)))
        {
            NotSupported(w.Line, $"`weak` before `{target}`, which the checker does not read as a class (R5, T19, D9)");
            return Types.Error;
        }

        if (target is not ClassType)
        {
            Report(ErrorIds.WeakValue, w.Line, $"`weak` stands before `{target}`, a value and no instance of a class, which has nothing to free (R5, M1)");
            return Types.Error;
        }

        if (w.Inner is not NullableType)
        {
            Report(ErrorIds.WeakNotNullable, w.Line, $"`weak {target}` is written without its `?`: a weak reference is null once its target is freed, `weak {target}?` (R5)");
            return Types.Error;
        }

        return new WeakRefType((ClassType)target);
    }

    /// <summary>Whether one of the types is a <c>weak</c> reference, where it stands in a place the checker does not read one: said <c>not supported</c> there (R3, E1).</summary>
    private bool WeakUnread(int line, string where, params KurzType[] types)
    {
        if (!types.Any(t => t is WeakRefType))
        {
            return false;
        }

        NotSupported(line, $"a `weak` reference as {where} (R3)");
        return true;
    }

    /// <summary>
    /// K1, K6, K10, K11, K16, K17: the base of a class and its fields, the base's first, as a data type's (D7), then those of its primary
    /// constructor in their order, <c>mut</c> where written, and the parameters its constructor binds: in the short form the base's and
    /// then the class's own, in the explicit form those the class lists. A parameter with a base field's name is that field (K11, K16);
    /// with another type it is <c>base-field-clash</c> (K17), and so it is in the short form where it does not stand at the base's place
    /// for it (K16). A field and a method of one name, and a name two fields have, are <c>redeclared</c> (V9). The clause of the base is
    /// inherited and the class's own adds its fields to it (K13). Each class once, its base before it.
    /// </summary>
    private void ResolveClass(ClassSymbol symbol)
    {
        if (symbol.Resolved)
        {
            return;
        }

        symbol.Resolved = true;
        var d = symbol.Declaration;
        classesResolving.Add(symbol);
        var baseSpecifier = ResolveClassBase(symbol);
        classesResolving.Remove(symbol);
        var b = symbol.Base;
        var shortForm = b is not null && baseSpecifier!.Arguments is null && d.PrimaryConstructor is not null;
        if (b is not null && baseSpecifier is not null)
        {
            symbol.Fields.AddRange(b.Fields);
            if (d.PrimaryConstructor is null && (baseSpecifier.Arguments is not null || b.Constructor.Count > 0))
            {
                NotSupported(baseSpecifier.Line, $"arguments of the base `{b.Name}`, given or taken, for a class without a primary constructor, which a constructor in its body passes (K10, K14)");
            }
            else if (shortForm && b.Declaration.PrimaryConstructor is null)
            {
                Report(ErrorIds.NoPrimaryConstructor, baseSpecifier.Line, $"`{b.Name}` has no primary constructor for the short form to follow: write its arguments after its name, `{b.Name}(...)` (K16)");
            }
        }

        var own = new List<ConstructorSlot>();
        var listed = d.PrimaryConstructor ?? [];
        for (var i = 0; i < listed.Count; i++)
        {
            var p = listed[i];
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

            if (b?.Field(p.Name) is { } inherited)
            {
                // K11, K16, K17: the parameter is the base's field, of its type, and in the short form at the base's place for it; one
                // of another type stays a parameter of its own type and no field, so that its calls carry no second error (E1)
                var place = b.Constructor.FindIndex(s => ReferenceEquals(s.Field, inherited));
                if (!Types.IsError(type) && !Types.IsError(inherited.Type) && !Types.Same(type, inherited.Type))
                {
                    Report(ErrorIds.BaseFieldClash, p.Line, $"`{p.Name}` is a field of the base `{b.Name}` already, {Types.A(inherited.Type)}, so the class would hold two fields of one name (K17)");
                    own.Add(new ConstructorSlot(new FieldSymbol(p.Name, type, p.Default is not null, p.Mut, p.Line), symbol, p));
                    continue;
                }

                if (shortForm && place != i)
                {
                    Report(ErrorIds.BaseFieldClash, p.Line, place < 0
                        ? $"`{p.Name}` is a field of the base `{b.Name}` that its constructor does not take, so the short form does not pass it on (K16, K17)"
                        : $"`{p.Name}` is a field of the base `{b.Name}`, whose constructor takes it as its parameter {place + 1}, and the short form keeps that place (K16, K17)");
                    if (place < 0)
                    {
                        own.Add(new ConstructorSlot(new FieldSymbol(p.Name, type, p.Default is not null, p.Mut, p.Line), symbol, p)); // no slot of the base stands for it
                    }

                    continue;
                }

                if (p.Mut != inherited.Mut)
                {
                    NotSupported(p.Line, $"`{p.Name}` {(p.Mut ? "with" : "without")} `mut`, unlike the field of the base `{b.Name}` it is (K11, K16)");
                }
                else if (shortForm && p.Default is not null)
                {
                    NotSupported(p.Line, $"a default on `{p.Name}`, which in the short form is the field of the base `{b.Name}` with the base's default (K16)");
                }

                if (!shortForm)
                {
                    own.Add(new ConstructorSlot(inherited, symbol, p));
                }

                continue;
            }

            if (symbol.Field(p.Name) is not null)
            {
                Report(ErrorIds.Redeclared, p.Line, $"the field `{p.Name}` is declared already (V9)");
            }

            var field = new FieldSymbol(p.Name, type, p.Default is not null, p.Mut, p.Line);
            symbol.Fields.Add(field);
            own.Add(new ConstructorSlot(field, symbol, p));
        }

        if (shortForm)
        {
            symbol.Constructor.AddRange(b!.Constructor);
        }

        symbol.Constructor.AddRange(own);
        if (b is not null && baseSpecifier!.Arguments is not null && d.PrimaryConstructor is not null)
        {
            symbol.BaseCall = new CallExpression(baseSpecifier.Line, new NameExpression(baseSpecifier.Line, b.Name), [], baseSpecifier.Arguments);
        }

        foreach (var f in (d.Members ?? []).OfType<FieldDeclaration>().Where(f => (f.Modifiers & Modifiers.Static) == 0))
        {
            var type = Resolve(f.Type!);
            if (Types.IsVoid(type))
            {
                Report(ErrorIds.Syntax, f.Line, "`void` is no type of a field (K1, F4)");
                type = Types.Error;
            }

            if (symbol.Field(f.Name) is not null)
            {
                Report(ErrorIds.Redeclared, f.Line, $"the field `{f.Name}` is declared already (V9)");
            }
            else if (f.Value is null)
            {
                // K14: no constructor of the class assigns a field of its body, the primary one and the empty one of a class without one
                Report(ErrorIds.FieldUnassigned, f.Line, $"the field `{f.Name}` takes no value: it has no `=`, and the constructor of `{symbol.Name}` assigns no field of the body (K14)");
            }

            symbol.Fields.Add(new FieldSymbol(f.Name, type, f.Value is not null, (f.Modifiers & Modifiers.Mut) != 0, f.Line, f)); // K7: after the primary constructor's, in their order
        }

        foreach (var s in symbol.StaticFields)
        {
            ResolveStaticField(symbol, s);
        }

        foreach (var (name, methods) in symbol.Methods)
        {
            if (symbol.Field(name) is not null || symbol.StaticField(name) is not null)
            {
                Report(ErrorIds.Redeclared, methods[0].Line, $"`{name}` is a field of `{symbol.Name}` already (V9)");
            }

            for (var i = 1; i < methods.Count; i++)
            {
                if (methods[i].IsProperty || methods.Take(i).Any(f => f.IsProperty))
                {
                    // K7: a property is one member of its name, as a field is; a method of that name is no overload of it (V9)
                    var earlier = methods.Take(i).Any(f => f.IsProperty) ? "a property" : "a method";
                    Report(ErrorIds.Redeclared, methods[i].Line, $"`{name}` is {earlier} of `{symbol.Name}` already (V9)");
                }
            }

            if (name is "Equals" or "GetHashCode")
            {
                Report(ErrorIds.EqualsMethod, methods[0].Line, $"a method of a class is named `{name}`: `==` and the hash of an instance are K4's, or come from the fields an `equal by` clause names, `equal by Id` (K5, K8)");
            }
        }

        if (b is not null && b.EqualBy.Count > 0)
        {
            symbol.EqualBy.AddRange(b.EqualBy); // K13: the base's clause is inherited, and the class's own adds its fields to it
        }
        else if (b is not null && d.EqualBy.Count > 0 && b.Fields.Count > 0)
        {
            NotSupported(d.Line, $"`equal by` on a class whose base `{b.Name}` has fields and no clause, where K13 does not say whether the base's fields count");
        }

        foreach (var name in d.EqualBy)
        {
            if (symbol.Field(name) is not { } field)
            {
                Report(ErrorIds.EqualByUnknown, d.Line, $"`{name}` is no field of `{symbol.Name}`: `equal by` names fields of the class (K15)");
            }
            else if (Types.ReachesWeak(field.Type))
            {
                NotSupported(d.Line, $"`equal by` that names `{name}`, a field that holds a `weak` reference, which the record makes the error `equal-by-weak` (K8, R3)");
            }
            else if (!symbol.EqualBy.Contains(field))
            {
                symbol.EqualBy.Add(field);
            }
        }
    }

    /// <summary>
    /// K7, K18: a <c>static</c> field, which exists once for the program and so holds an immutable value: <c>static mut</c>, and a type
    /// that is or holds a class, are <c>static-state</c>; a field of the name of another field is <c>redeclared</c> (V9). A field
    /// without a written type takes the type of its initial value (V2), judged when that value is read (<see cref="CheckStaticFields"/>).
    /// </summary>
    private void ResolveStaticField(ClassSymbol symbol, StaticFieldSymbol s)
    {
        var d = s.Declaration;
        if (symbol.Field(s.Name) is not null || symbol.StaticFields.TakeWhile(other => !ReferenceEquals(other, s)).Any(other => other.Name == s.Name))
        {
            Report(ErrorIds.Redeclared, d.Line, $"the field `{s.Name}` is declared already (V9)");
        }

        if ((d.Modifiers & Modifiers.Mut) != 0)
        {
            Report(ErrorIds.StaticState, d.Line, $"`{s.Name}` is a `static mut` field, state that every actor would reach: a `static` field holds an immutable value (K18)");
            s.Refused = true;
            return;
        }

        if (d.Type is not null)
        {
            s.Type = Resolve(d.Type);
            JudgeStaticFieldType(s);
        }
    }

    /// <summary>
    /// K18: the type of a <c>static</c> field: one that is or holds a class is <c>static-state</c>, since an instance lives in one actor's
    /// heap; the checker reads a field of a number, a <c>bool</c>, a <c>char</c>, a <c>string</c>, a time or a value of an enum, whose
    /// constants F12 gives, and a type of another kind is <c>not supported</c>, as an initial value that is no constant is.
    /// </summary>
    private void JudgeStaticFieldType(StaticFieldSymbol s)
    {
        var held = new HashSet<ClassSymbol>();
        ClassesHeld(s.Type, held, []);
        if (Types.IsError(s.Type))
        {
            s.Refused = true;
        }
        else if (Types.IsVoid(s.Type))
        {
            Report(ErrorIds.Syntax, s.Line, "`void` is no type of a field (K1, F4)");
            s.Refused = true;
        }
        else if (held.Count > 0)
        {
            Report(ErrorIds.StaticState, s.Line, $"the `static` field `{s.Name}` would hold an instance of `{held.First().Name}`, which lives in one actor's heap: a `static` field holds an immutable value no actor owns (K18)");
            s.Refused = true;
        }
        else if (s.Type is not (PrimitiveType or EnumType))
        {
            NotSupported(s.Line, $"a `static` field of type `{s.Type}`: the checker reads one of a number, a `bool`, a `char`, a `string`, a time or a value of an enum, which a constant gives (K18)");
            s.Refused = true;
        }
    }

    /// <summary>
    /// K18, K20, F12: the initial value of each <c>static</c> field, a constant as F12 counts one, in which a <c>static</c> field of any
    /// class, declared above or below, counts as the constant it holds, its own initial value read first, as Rust evaluates a static's
    /// (<see cref="ReadStaticField"/>): the values are read in the order their reads need, and a cycle among them is
    /// <c>static-cycle</c>. An initial value that is no constant is computed at the field's first read (section 4), whose open items of
    /// section 14 leave it <c>not supported</c>; a field without one is <c>field-unassigned</c>, since no type has a default value (K14).
    /// </summary>
    private void CheckStaticFields()
    {
        foreach (var symbol in classDeclared)
        {
            foreach (var s in symbol.StaticFields)
            {
                CheckStaticField(s);
            }
        }
    }

    /// <summary>
    /// K18, K20: the initial value of one <c>static</c> field, read once, at the top level of the file or from the initial value of
    /// another field that reads it; the place the reading of that other value stands at is kept and given back.
    /// </summary>
    private void CheckStaticField(StaticFieldSymbol s)
    {
        if (s.Refused || s.Value is not null || readingStatics.Contains(s))
        {
            return;
        }

        if (s.Declaration.Value is not { } value)
        {
            Report(ErrorIds.FieldUnassigned, s.Line, $"the `static` field `{s.Name}` takes no value: it has no `=`, and no constructor assigns a `static` field (K14, K18)");
            s.Refused = true;
            return;
        }

        var outer = (frame, typeContext, inElseArm, loopSource, realKeepsOwnType, blockDepth, rawDepth, readingConstant, stateRead, initializing);
        frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = s.Line, Class = s.Owner, Static = true };
        (typeContext, inElseArm, loopSource, realKeepsOwnType, blockDepth, rawDepth) = (null, false, null, false, 0, 0);
        (readingConstant, stateRead, initializing) = (true, false, s);
        readingStatics.Add(s);
        var t = CheckExpression(value, s.Declaration.Type is null ? null : s.Type);
        readingStatics.RemoveAt(readingStatics.Count - 1);
        (frame, typeContext, inElseArm, loopSource, realKeepsOwnType, blockDepth, rawDepth, readingConstant, stateRead, initializing) = outer;
        if (t.IsError)
        {
            s.Refused = true; // E1: refused inside its value, a cycle it stands in among it (K20)
            return;
        }

        if (s.Declaration.Type is null)
        {
            s.Type = t.Type;
            JudgeStaticFieldType(s);
            if (s.Refused)
            {
                return;
            }
        }

        if (t.Constant is null)
        {
            NotSupported(value.Line, $"the initial value of the `static` field `{s.Name}`, which is no constant: section 4 computes it at the field's first read, and section 14 holds that read's open items (K18)");
            s.Refused = true;
        }
        else if (!Types.Assignable(t.Type, s.Type))
        {
            ReportMismatch(t.Type, s.Type, value.Line, $"{Types.A(t.Type)} where {Types.A(s.Type)} is required");
            s.Refused = true;
        }
        else
        {
            s.Value = t.Constant;
        }
    }

    /// <summary>
    /// K20: a cycle of initial values, each reading the next and the last reading the first, reported once, at the field of the cycle
    /// that stands first in the file, its message naming the fields from there in the order they read each other; every field of the
    /// cycle is refused, so that it and a field that reads it carry nothing more (E1).
    /// </summary>
    private void ReportStaticCycle(List<StaticFieldSymbol> cycle)
    {
        var first = cycle.IndexOf(cycle.MinBy(f => f.Line)!);
        var names = cycle.Skip(first).Concat(cycle.Take(first)).Select(f => $"`{f.Owner.Name}.{f.Name}`").ToList();
        var path = string.Concat(names.Skip(1).Select((name, i) => i == 0 ? $" reads {name}" : $", which reads {name}"));
        var text = names.Count == 1 ? $"{names[0]} reads {names[0]}" : $"{names[0]}{path}, which reads {names[0]}";
        Report(ErrorIds.StaticCycle, cycle[first].Line, $"{text}: the initial values of `static` fields read each other in a cycle (K20)");
        foreach (var field in cycle)
        {
            field.Refused = true;
        }
    }

    /// <summary>
    /// K6, K7, K18: a field in the body or a <c>static</c> member of a class in a chain, one with a base or with classes derived from
    /// it, is <c>not supported</c>, at the first such member of the class: how a derived class sees a field of the base without
    /// <c>pub</c>, where its construction gives the base's fields of the body their defaults, and how it names the base's
    /// <c>static</c> members, the meaning section 4 assumes from C#, the checker and the emitter do not read.
    /// </summary>
    private void RefuseChainMembers()
    {
        foreach (var symbol in classDeclared.Where(c => c.Base is not null || c.Derived))
        {
            var member = (symbol.Declaration.Members ?? []).FirstOrDefault(m => m is FieldDeclaration || (m is FunctionDeclaration f && (f.Modifiers & Modifiers.Static) != 0));
            if (member is not null)
            {
                NotSupported(member.Line, $"a field in the body or a `static` member of `{symbol.Name}`, a class with a base or with classes derived from it (K6, K7, K18)");
            }
        }
    }

    /// <summary>
    /// K6, K10: the base class a class names after <c>:</c>, resolved before the class, with the part of the declaration that names
    /// it; null where it names none. What the checker does not read there is <c>not supported</c>: an interface, whose declaration
    /// says so (K6), a type that is no class, a second class and a class that is its own base.
    /// </summary>
    private BaseSpecifier? ResolveClassBase(ClassSymbol symbol)
    {
        BaseSpecifier? taken = null;
        foreach (var b in symbol.Declaration.Bases)
        {
            if (b.Type is not NamedType { TypeArguments.Count: 0 } named || !classes.TryGetValue(named.Name, out var baseSymbol))
            {
                var type = Resolve(b.Type); // a name that is no type is `unknown-name` there (V10), and an interface was said `not supported` at its declaration
                if (!Types.IsError(type))
                {
                    NotSupported(b.Line, $"`{type}` after `:`, where a class names its base class and its interfaces (K6, K7)");
                }

                continue;
            }

            if (!InFolder(baseSymbol))
            {
                // W1 with K6: `std/`'s checker judged the class with no class derived from it, so the refusals of a class in a chain did not
                // run on it, and its symbol is `std/`'s own, which the file's reading leaves as it is
                NotSupported(b.Line, $"`{named.Name}` after `:`, a class of `std/` the prelude brings into the file (K6, W1)");
                continue;
            }

            if (taken is not null)
            {
                NotSupported(b.Line, $"a second base class, `{named.Name}`, where a class inherits from one class (K6)");
                continue;
            }

            taken = b;
            if (classesResolving.Contains(baseSymbol))
            {
                NotSupported(b.Line, $"the base `{named.Name}`, which closes a cycle of bases (K6)");
                return null;
            }

            ResolveClass(baseSymbol);
            symbol.Base = baseSymbol;
            baseSymbol.Derived = true;
        }

        return taken;
    }

    /// <summary>
    /// K7, F1: the parameter and result types of each method. A method takes no <c>mut</c> marker, which is for values (K9, M5): one
    /// written is text no rule gives a meaning (E4), and so is <c>naked</c> before a method (R12). Two methods of one name with the same
    /// parameters are <c>duplicate-function</c>, as two functions are (F12).
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
                    Report(ErrorIds.Syntax, d.Line, d.Property
                        ? "a property takes no `mut` marker: it has no setter, and its body assigns the `mut` fields of its instance without one (K7, K9; E4)"
                        : $"a method of a class takes no `mut` marker: it assigns the `mut` fields of its instance without one (K9, M5; E4)");
                }

                if ((d.Modifiers & Modifiers.Naked) != 0)
                {
                    Report(ErrorIds.Syntax, d.Line, "`naked` stands before a function at the top level of a file alone (R12; E4)");
                }

                ResolveTypes(method, d);
                if (method.IsProperty && (Types.IsVoid(method.ReturnType) || method.ReturnType is UnionType u && Types.NonNull(u) is null))
                {
                    // K7: a read gives a value; a `void` success (O10) and cases beside the success (O2) are section 14's, open
                    NotSupported(d.Line, "a property of `void` or of a type that lists cases beside its success (K7, O2, O10)");
                }
            }

            foreach (var list in symbol.Methods.Values.Where(list => !list.Any(f => f.IsProperty))) // a property's name: `redeclared` above
            {
                for (var i = 1; i < list.Count; i++)
                {
                    if (list.Take(i).Any(earlier => SameParameters(earlier, list[i])))
                    {
                        Report(ErrorIds.DuplicateFunction, list[i].Line, $"`{symbol.Name}` has a method `{list[i].Name}` with these parameter types already (F12)");
                    }
                }
            }
        }

        foreach (var symbol in classDeclared)
        {
            foreach (var method in symbol.Methods.Values.SelectMany(list => list))
            {
                CheckOverride(symbol, method);
            }

            foreach (var field in symbol.Fields.Skip(symbol.Base?.Fields.Count ?? 0))
            {
                HidesProperty(symbol, field.Name, false, field.Line); // K7: a field of the class's own, its bases' fields coming first (K6)
            }
        }
    }

    /// <summary>
    /// K7 <i>(assumed)</i>: a property has no override (section 14: open), so a member of a derived class with the name of a property of a
    /// base that the class sees (F9), a field or a method whatever its parameters, is <c>hides-member</c>, as a method of a property's name
    /// is no overload of it in one class (V9); so is a property with the name of a method of a base that the class sees.
    /// </summary>
    private bool HidesProperty(ClassSymbol symbol, string name, bool isProperty, int line)
    {
        if (symbol.Base?.Chain.FirstOrDefault(c => c.Methods.TryGetValue(name, out var list) && list.Any(SeenByDerived)) is not { } b)
        {
            return false;
        }

        var ofProperty = b.Methods[name].Any(f => f.IsProperty);
        if (!ofProperty && !isProperty)
        {
            return false;
        }

        Report(ErrorIds.HidesMember, line, ofProperty
            ? $"`{name}` has the name of a property of `{b.Name}`: a derived class gives no member of its own the name of a property it inherits (K7)"
            : $"`{name}` is a property, and `{b.Name}` has a method of this name: a derived class gives no property the name of a method it inherits (K7)");
        return true;
    }

    /// <summary>
    /// K7: a method with the name and the parameter types, in order, of a method of a base class that the class can see, the nearest
    /// such, is marked <c>override</c>, and that method <c>virtual</c> or <c>override</c>; without the mark it is <c>hides-member</c>.
    /// <c>override</c> where no such method is, or where it carries neither mark, is <c>override-without-virtual</c>, and an override
    /// whose return type differs is <c>override-return-type</c>. The class sees a base method with <c>pub</c> or <c>prot</c>, and one
    /// marked <c>internal</c> where its folder declared the base (F9); a base method the class cannot see, a private one, is hidden by
    /// nothing (F9). An override is as visible as it writes, whatever the method it overrides writes: one without <c>pub</c> is private
    /// to its class, which a call through the base reaches and a call on the derived type outside it is refused (F9; the owner,
    /// 2026-11-02, #110, "allowed as written"). The checker does not read <c>virtual</c> on a private method, which no derived class sees,
    /// or <c>virtual</c> and <c>override</c> on one method: <c>not supported</c>.
    /// </summary>
    private void CheckOverride(ClassSymbol symbol, FunctionSymbol method)
    {
        var d = method.Declaration!;
        var marks = d.Modifiers & (Modifiers.Virtual | Modifiers.Override);
        var visibility = d.Modifiers & (Modifiers.Pub | Modifiers.Prot);
        if (marks == (Modifiers.Virtual | Modifiers.Override) || (marks == Modifiers.Virtual && visibility == 0))
        {
            NotSupported(d.Line, marks == (Modifiers.Virtual | Modifiers.Override) ? "`virtual` and `override` on one method (K7)" : "`virtual` on a private method, which no derived class sees (K7, F9)");
            return;
        }

        if (HidesProperty(symbol, method.Name, method.IsProperty, d.Line))
        {
            return;
        }

        var overridden = symbol.Base?.Chain
            .Select(c => c.Methods.TryGetValue(method.Name, out var list) ? list.FirstOrDefault(m => SameParameters(m, method) && SeenByDerived(m)) : null)
            .FirstOrDefault(m => m is not null);
        if ((d.Modifiers & Modifiers.Override) == 0)
        {
            if (overridden is not null)
            {
                Report(ErrorIds.HidesMember, d.Line, $"`{method.Name}` has the name and the parameter types of a method of `{overridden.Owner!.Name}`: an override is marked `override`, and the method it overrides `virtual` (K7)");
            }

            return;
        }

        if (overridden is null || !overridden.IsVirtual)
        {
            Report(ErrorIds.OverrideWithoutVirtual, d.Line, overridden is null
                ? $"`{method.Name}` overrides nothing: no base class of `{symbol.Name}` has a method of this name and these parameter types that it sees (K7)"
                : $"`{method.Name}` of `{overridden.Owner!.Name}` is marked neither `virtual` nor `override`, so nothing overrides it (K7)");
            return;
        }

        if (!Types.IsError(method.ReturnType) && !Types.IsError(overridden.ReturnType) && !Types.Same(method.ReturnType, overridden.ReturnType))
        {
            Report(ErrorIds.OverrideReturnType, d.Line, $"`{method.Name}` returns `{method.ReturnType}`, and the method of `{overridden.Owner!.Name}` it overrides `{overridden.ReturnType}`: an override has the return type of the method it overrides (K7)");
            return;
        }

        method.Overridden = overridden;
    }

    /// <summary>F9: whether a class derived from the class of <paramref name="method"/> sees it: with <c>pub</c> or <c>prot</c>, or <c>internal</c> in its folder.</summary>
    private bool SeenByDerived(FunctionSymbol method) =>
        (method.Declaration!.Modifiers & (Modifiers.Pub | Modifiers.Prot)) != 0
        || ((method.Declaration.Modifiers & Modifiers.Internal) != 0 && InFolder(method.Owner!));

    /// <summary>
    /// F12, D11: the default of a parameter of a primary constructor is a constant expression, as a parameter's is. K10, K11, K17: the
    /// explicit form's arguments of the base are a construction of the base over the class's parameters, and a parameter with a base
    /// field's name and type is what the base receives for that field, else <c>base-field-clash</c>.
    /// </summary>
    private void CheckClassDefaults()
    {
        foreach (var symbol in classDeclared)
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = symbol.Line };
            foreach (var slot in symbol.Constructor.Where(s => ReferenceEquals(s.Owner, symbol)))
            {
                if (slot.Declaration.Default is not null)
                {
                    CheckDefault(slot.Declaration.Default, slot.Field.Type, slot.Declaration.Line);
                }
            }

            if (symbol.BaseCall is { } call && symbol.Base is { } b)
            {
                frame = new Frame { ReturnType = Types.Void, Line = symbol.Line, Reserved = reservedTopLevel };
                foreach (var slot in symbol.Constructor)
                {
                    DeclareParameter(slot.Declaration.Name, slot.Field.Type, false, slot.Declaration.Line, slot.Declaration);
                }

                if (!CheckClassConstruction(b, call, null).IsError)
                {
                    var binding = model.CallOf(call).Binding;
                    foreach (var slot in symbol.Constructor.Where(s => b.Fields.Contains(s.Field)))
                    {
                        // K11, K17: the parameter is the base's field, so the base receives it for that field
                        var place = b.Constructor.FindIndex(s => ReferenceEquals(s.Field, slot.Field));
                        var argument = binding.Select((j, i) => (j, i)).Where(x => x.j == place && place >= 0).Select(x => call.Arguments[x.i].Value).FirstOrDefault();
                        if (argument is null || Unwrap(argument) is not NameExpression n || n.Name != slot.Declaration.Name)
                        {
                            Report(ErrorIds.BaseFieldClash, slot.Declaration.Line, $"`{slot.Declaration.Name}` has the name and the type of the field of the base `{b.Name}`, and the base receives {(argument is null ? "nothing" : "something else")} for it, so the field would hold another value than the parameter (K11, K17)");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// K7: the default of each field of a class body, in declaration order, which may be any expression: it reads the fields of the
    /// primary constructor and the fields declared above it through the instance being built, and functions outside the class, as C#
    /// reads a field's initializer before the constructor's body; a field below it, a method of the class and <c>this</c> are no names
    /// there (V10).
    /// </summary>
    private void CheckFieldDefaults()
    {
        foreach (var symbol in classDeclared)
        {
            VariableSymbol? receiver = null;
            foreach (var field in symbol.Fields.Where(f => f.Body?.Value is not null))
            {
                receiver ??= new VariableSymbol("this", new ClassType(symbol), false, VariableKind.Parameter, symbol.Line) { Read = true };
                frame = new Frame { ReturnType = Types.Void, Line = field.Line, Reserved = reservedTopLevel, Class = symbol, Initializing = field };
                frame.Scope.Variables[receiver.Name] = receiver;
                var value = field.Body!.Value!;
                var t = CheckExpression(value, field.Type, keepCases: Types.KeepsCases(field.Type));
                if (t.IsError || Types.IsError(field.Type))
                {
                    continue;
                }

                if (Types.IsVoid(t.Type))
                {
                    Report(ErrorIds.Syntax, value.Line, "a call with a `void` success used as a value (O10)");
                }
                else if (!Types.Assignable(t.Type, field.Type))
                {
                    ReportMismatch(t.Type, field.Type, value.Line, $"{Types.A(t.Type)} where {Types.A(field.Type)} is required");
                }
            }

            if (receiver is not null)
            {
                model.RecordFieldsThis(symbol, receiver);
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
                frame = new Frame { ReturnType = method.ReturnType, Line = d.Line, Reserved = reservedTopLevel, Class = symbol, Static = method.IsStatic };
                if (!method.IsStatic)
                {
                    var receiver = new VariableSymbol("this", new ClassType(symbol), false, VariableKind.Parameter, d.Line) { Read = true };
                    frame.Scope.Variables[receiver.Name] = receiver;
                    model.RecordThis(method, receiver);
                }
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

    /// <summary>
    /// K7: whether the code being checked stands in a <c>static</c> method of its class, or in the initial value of a <c>static</c>
    /// field, where no instance is.
    /// </summary>
    private bool InStaticMember()
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Class is not null)
            {
                return f.Static;
            }
        }

        return false;
    }

    /// <summary>
    /// K7: inside a method of the instance, a name that is a field of its class, read through <c>this</c>; a lambda that reads it captures
    /// <c>this</c> (F15). A <c>static</c> member has no instance, so none is found there (<see cref="InstanceFieldInStatic"/>). In a
    /// field's default only the fields declared above it are names (V10).
    /// </summary>
    private FieldSymbol? ImplicitField(string name) =>
        !InStaticMember() && CurrentClass() is { } c && c.Field(name) is { } field && (Initializing() is not { } being || c.Fields.IndexOf(field) < c.Fields.IndexOf(being)) ? field : null;

    /// <summary>K7, K19: inside a <c>static</c> member, a name that is a field of an instance of its class, which is <c>static-mismatch</c>.</summary>
    private bool InstanceFieldInStatic(string name, int line)
    {
        if (!InStaticMember() || CurrentClass()?.Field(name) is null)
        {
            return false;
        }

        Report(ErrorIds.StaticMismatch, line, $"a `static` member has no instance: `{name}` is a field of an instance of `{CurrentClass()!.Name}` (K7, K19)");
        return true;
    }

    /// <summary>K19: a method of an instance named through its class, <c>Counter.Next()</c> or <c>Counter.Next</c>.</summary>
    private void InstanceMethodThroughClass(ClassSymbol symbol, string name, int line) =>
        Report(ErrorIds.StaticMismatch, line, Property(symbol, name) is not null
            ? $"`{name}` is a property of an instance of `{symbol.Name}`, not of the class: read it from an instance (K7, K19)"
            : $"`{name}` is a method of an instance of `{symbol.Name}`, not of the class: call it on an instance (K7, K19)");

    /// <summary>K19: a method of an instance named alone, with a call or without one, in a <c>static</c> member, which has no instance.</summary>
    private void InstanceMethodInStatic(string name, int line) =>
        Report(ErrorIds.StaticMismatch, line, $"a `static` member has no instance: `{name}` is a {(Property(CurrentClass()!, name) is not null ? "property" : "method")} of an instance of `{CurrentClass()!.Name}` (K7, K19)");

    /// <summary>K7, K18: inside the class, a name that is one of its <c>static</c> fields.</summary>
    private StaticFieldSymbol? ImplicitStaticField(string name) => CurrentClass()?.StaticField(name);

    /// <summary>
    /// K7: inside a method, the methods of its class of a name a call names alone, its bases' among them (<see cref="MethodsOf"/>), of
    /// the instance and <c>static</c>; a field's default names the <c>static</c> ones alone, as C# refuses an instance member in an
    /// initializer and no <c>static</c> one (V10).
    /// </summary>
    private List<FunctionSymbol>? ImplicitMethods(string name) =>
        CurrentClass() is { } c && MethodsOf(c, name) is { } list
        && (Initializing() is null ? list : list.Where(f => f.IsStatic).ToList()) is { Count: > 0 } reached ? reached : null;

    /// <summary>
    /// K6, K7: the methods of a name a call on an instance of the class picks from (F13): its own, and those of each base up the chain
    /// that no method nearer the class has the parameter types of, so that an override stands for what it overrides; null where the
    /// chain has none.
    /// </summary>
    private static List<FunctionSymbol>? MethodsOf(ClassSymbol symbol, string name)
    {
        List<FunctionSymbol>? found = null;
        foreach (var c in symbol.Chain)
        {
            if (!c.Methods.TryGetValue(name, out var list))
            {
                continue;
            }

            foreach (var m in list)
            {
                if (found is null || !found.Any(f => SameParameters(f, m)))
                {
                    (found ??= []).Add(m);
                }
            }
        }

        return found;
    }

    /// <summary>K7: the field of a class body whose default is being read, through the lambdas around the place; null elsewhere.</summary>
    private FieldSymbol? Initializing()
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Class is not null)
            {
                return f.Initializing;
            }
        }

        return null;
    }

    /// <summary>
    /// K7, V10: in a field's default, a field of the class not declared above it, the field itself among them, or a method of the class
    /// is no name, which the report says; false where the name is neither, and for every name outside a default.
    /// </summary>
    private bool OutOfReachInDefault(string name, int line)
    {
        if (Initializing() is not { } being || CurrentClass() is not { } c)
        {
            return false;
        }

        if (c.Field(name) is not null)
        {
            Report(ErrorIds.UnknownName, line, $"`{name}` is not declared above `{being.Name}`: a field's default reads the fields declared above it (K7, V10)");
            return true;
        }

        if (c.Methods.ContainsKey(name))
        {
            Report(ErrorIds.UnknownName, line, $"`{name}` is a method of `{c.Name}`: a field's default reads no method of its instance (K7, V10)");
            return true;
        }

        return false;
    }

    /// <summary>The variable <c>this</c> of the method around the place, read and, from a lambda, captured (F15).</summary>
    private VariableSymbol ThisVariable()
    {
        var (receiver, _) = Lookup("this");
        return receiver ?? throw new InvalidOperationException("no `this` inside a method");
    }

    /// <summary>K7, V9: inside a method, whether a name is a member of its class, which a declaration of the name would hide.</summary>
    private bool IsMemberName(string name) => CurrentClass() is { } c && (c.Field(name) is not null || c.StaticField(name) is not null || MethodsOf(c, name) is not null);

    /// <summary>K7: <c>this</c>, the instance a method was called on; outside a method no <c>this</c> is visible (V10), and a <c>static</c> member has none (K19).</summary>
    private Typed CheckThis(ThisExpression t)
    {
        if (CurrentClass() is null)
        {
            Report(ErrorIds.UnknownName, t.Line, "`this` is the instance inside a method of a class, and no method is around it (K7, V10)");
            return Typed.Error;
        }

        if (InStaticMember())
        {
            Report(ErrorIds.StaticMismatch, t.Line, "a `static` member has no instance, and so no `this` (K7, K19)");
            return Typed.Error;
        }

        if (Initializing() is not null)
        {
            Report(ErrorIds.UnknownName, t.Line, "a field's default reads no `this`: its instance is being built (K7, V10)");
            return Typed.Error;
        }

        model.RecordImplicitField(t, ThisVariable(), null);
        return new Typed(new ClassType(CurrentClass()!));
    }

    /// <summary>
    /// K7, K6: the read-only property of that name of the class or, as C# reads an inherited property, of the nearest base up the chain
    /// that declares a method or a property of the name; null where that class's members of the name are methods, where a field has the
    /// name, or where the chain has none.
    /// </summary>
    private static FunctionSymbol? Property(ClassSymbol symbol, string name) =>
        symbol.Field(name) is not null ? null
        : symbol.Chain.FirstOrDefault(c => c.Methods.ContainsKey(name)) is { } declaring ? declaring.Methods[name].Find(f => f.IsProperty) : null;

    /// <summary>
    /// K7: <c>h.Count</c>, a read of a property, the call of its method that takes no arguments (section 4: the owner, 2026-11-03), recorded
    /// as that call with the read as its callee; its body runs at every read, so no test of an earlier read narrows it (N9). A property
    /// without <c>pub</c> read from outside its class is <c>not-visible</c> (F9).
    /// </summary>
    private Typed ReadProperty(MemberAccess m, ClassType owner, FunctionSymbol property)
    {
        if (!Visible(property))
        {
            Report(ErrorIds.NotVisible, m.Line, Hidden(property.Name, owner.Symbol, (property.Declaration!.Modifiers & Modifiers.Internal) != 0, "a property"));
            return Typed.Error;
        }

        var call = new CallExpression(m.Line, m, [], []);
        var typed = ResolveCall([property], call, null, out _, CallKind.Method);
        CallRan(); // N9: the body can assign a `mut` field through another reference (K2) and free the target of a `weak` one (R3)
        model.RecordPropertyRead(m, call);
        return typed;
    }

    /// <summary>K7: <c>Count</c>, a property of the class read by its name inside a method, the property of <c>this</c>, read as <see cref="ReadProperty"/> reads it.</summary>
    private Typed ReadImplicitProperty(NameExpression n, FunctionSymbol property)
    {
        var call = new CallExpression(n.Line, n, [], []);
        var typed = CallImplicitMethod([property], n, call, null);
        CallRan(); // N9, as ReadProperty
        model.RecordPropertyRead(n, call);
        return typed;
    }

    /// <summary>K7: an assignment of a property, which has no setter.</summary>
    private void AssignProperty(ClassSymbol owner, string name, int line) =>
        Report(ErrorIds.AssignImmutable, line, $"`{name}` is a property of `{owner.Name}`, which has no setter (K7)");

    /// <summary>K7: a read of a field by its name inside a method, the field of <c>this</c>.</summary>
    private Typed ReadImplicitField(NameExpression n, FieldSymbol field)
    {
        model.RecordImplicitField(n, ThisVariable(), field);
        return Narrowed(n, new Typed(Types.Read(field.Type))); // N3, N9; R3
    }

    /// <summary>
    /// K1, K2, K7: an assignment to a field by its name inside a method: the field of <c>this</c>, which takes a value only where it is
    /// <c>mut</c>; inside a lambda too, which holds a reference to the same instance (F11, K2).
    /// </summary>
    private void AssignImplicitField(AssignOrDeclare a, FieldSymbol field)
    {
        var receiver = ThisVariable();
        model.RecordImplicitField(a, receiver, field);
        if (!field.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{a.Name}` is a field without `mut`, which nothing assigns (K1)");
        }

        var value = CheckExpression(a.Value, field.Type, keepCases: Types.KeepsCases(field.Type));
        EndChangeableNarrowing(); // N9: the assignment, which runs after its value, ends what was known of a path through a `mut` field
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
        else if (field.Mut)
        {
            Assigned(new NullPath(receiver, field.Name), field.Type, value); // N9: a path with no `weak` reference is narrowed by its assignment
        }
    }

    // ---- static members ------------------------------------------------------------------------------

    /// <summary>
    /// K18, K20: a read of a <c>static</c> field, by its name inside the class or as <c>T.F</c>. In the initial value of a <c>static</c>
    /// field it is the constant the field holds, of any class and declared above or below, whose own initial value is read first; a
    /// field whose initial value is being read closes a cycle, <c>static-cycle</c>. Elsewhere it is a value of its type, which the
    /// emitter gives as the constant the field holds, and no constant itself, as a C# <c>static readonly</c> field is none.
    /// </summary>
    private Typed ReadStaticField(Expression node, StaticFieldSymbol field)
    {
        if (initializing is not null && !field.Refused)
        {
            var at = readingStatics.IndexOf(field);
            if (at >= 0)
            {
                var cycle = readingStatics.GetRange(at, readingStatics.Count - at);
                if (!cycle.Any(f => f.Refused))
                {
                    ReportStaticCycle(cycle); // a cycle through a field a reported cycle refused is that error's, in any order of the fields (E1)
                }

                return Typed.Error;
            }

            CheckStaticField(field); // K20: its own initial value first
        }

        if (field.Refused || Types.IsError(field.Type))
        {
            return Typed.Error; // E1: refused at its declaration, or in a cycle
        }

        model.RecordStaticField(node, field);
        return initializing is null ? new Typed(field.Type) : new Typed(field.Type, field.Value);
    }

    /// <summary>
    /// K7, K18, K19: <c>T.F</c>, a member of a class reached by its name: a <c>static</c> field, <c>not-visible</c> where it is private and
    /// the read stands outside the class (F9); a member of an instance is <c>static-mismatch</c>.
    /// </summary>
    private Typed ClassStaticMember(ClassSymbol symbol, MemberAccess m)
    {
        if (symbol.StaticField(m.Member) is { } field)
        {
            if (!field.IsPub && !ReferenceEquals(CurrentClass(), symbol) && !(field.IsInternal && InFolder(symbol)))
            {
                Report(ErrorIds.NotVisible, m.Line, Hidden(field.Name, symbol, field.IsInternal, "a field"));
                return Typed.Error;
            }

            return ReadStaticField(m, field);
        }

        if (symbol.Field(m.Member) is not null)
        {
            Report(ErrorIds.StaticMismatch, m.Line, $"`{m.Member}` is a field of an instance of `{symbol.Name}`, not of the class: read it from an instance (K7, K19)");
            return Typed.Error;
        }

        if (symbol.Methods.TryGetValue(m.Member, out var methods))
        {
            if (!methods.Any(f => f.IsStatic))
            {
                InstanceMethodThroughClass(symbol, m.Member, m.Line); // K19, with a call or without one
                return Typed.Error;
            }

            NotSupported(m.Line, "a method as a value, named without a call (K7, F11)");
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, m.Line, $"`{symbol.Name}` has no member `{m.Member}` (V10)");
        return Typed.Error;
    }

    /// <summary>
    /// K7, K19: <c>T.M(args)</c>, a call of a <c>static</c> method by its class's name, the one of its name whose parameters fit (F13);
    /// a method of an instance called so is <c>static-mismatch</c>, as C#'s CS0120 refuses it.
    /// </summary>
    private Typed CallStatic(ClassSymbol symbol, MemberAccess m, CallExpression c, KurzType? expected)
    {
        if (symbol.Methods.TryGetValue(m.Member, out var methods))
        {
            var statics = methods.Where(f => f.IsStatic).ToList();
            if (statics.Count > 0)
            {
                return PickMethod(statics, c, expected, m.Line, out _);
            }

            InstanceMethodThroughClass(symbol, m.Member, m.Line);
        }
        else if (symbol.StaticField(m.Member) is not null || symbol.Field(m.Member) is not null)
        {
            var value = ClassStaticMember(symbol, m);
            if (!value.IsError)
            {
                model.RecordType(m, value);
                return CallValue(value.Type, c); // F11: what a call of a value of another type says
            }
        }
        else
        {
            Report(ErrorIds.UnknownName, m.Line, $"`{symbol.Name}` has no member `{m.Member}` (V10)");
        }

        CheckArgumentsLoosely(c);
        return Typed.Error;
    }

    /// <summary>K18: an assignment of a <c>static</c> field, or of a path that starts at one, which holds one value for the program.</summary>
    private void AssignStaticField(StaticFieldSymbol field, int line)
    {
        if (!field.Refused)
        {
            Report(ErrorIds.AssignImmutable, line, $"`{field.Name}` is a `static` field, which holds one immutable value for the program: nothing assigns it (K18)");
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
            return FieldVisible(field, type.Symbol, line) ? new Typed(Types.Read(field.Type)) : Typed.Error; // R3: a `weak` field reads as a strong `T?`
        }

        if (Property(type.Symbol, member) is not null)
        {
            NotSupported(line, "a property read where the checker reads a field alone (K7)");
            return Typed.Error;
        }

        if (MethodsOf(type.Symbol, member) is not null)
        {
            NotSupported(line, "a method as a value, named without a call (K7, F11)");
            return Typed.Error;
        }

        if (type.Symbol.StaticField(member) is not null)
        {
            Report(ErrorIds.StaticMismatch, line, $"`{member}` is a `static` field of `{type.Symbol.Name}`: read it as `{type.Symbol.Name}.{member}` (K7, K19)");
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, line, $"`{type.Symbol.Name}` has no member `{member}` (V10)");
        return Typed.Error;
    }

    /// <summary>
    /// F9: a field of a class body without <c>pub</c> is private to its class, and one marked <c>internal</c> to the folder that declares
    /// its class; a use of it from outside is <c>not-visible</c>.
    /// </summary>
    private bool FieldVisible(FieldSymbol field, ClassSymbol owner, int line)
    {
        if (field.IsPub || ReferenceEquals(CurrentClass(), owner) || (field.IsInternal && InFolder(owner)))
        {
            return true;
        }

        Report(ErrorIds.NotVisible, line, Hidden(field.Name, owner, field.IsInternal, "a field"));
        return false;
    }

    /// <summary>F9: a method without <c>pub</c> is private to its class, one marked <c>internal</c> to the folder that declares its class, and one with <c>prot</c> visible in the classes derived from it too; a call of it from elsewhere is <c>not-visible</c>.</summary>
    private bool Visible(FunctionSymbol method) =>
        (method.Declaration!.Modifiers & Modifiers.Pub) != 0 || ReferenceEquals(CurrentClass(), method.Owner)
        || ((method.Declaration.Modifiers & Modifiers.Prot) != 0 && CurrentClass() is { } current && current.HasBaseOrIs(method.Owner!))
        || ((method.Declaration.Modifiers & Modifiers.Internal) != 0 && InFolder(method.Owner!));

    /// <summary>
    /// F9: whether the folder this checker reads declared <paramref name="owner"/>, so that its <c>internal</c> members are visible here,
    /// as C#'s <c>internal</c> is in its assembly (the owner, 2026-11-02 and 2026-11-03). A class of <c>std/</c> that the prelude brings into
    /// a file (W1) is the symbol <c>std/</c>'s checker made, and the one class a file reaches from another folder.
    /// </summary>
    private bool InFolder(ClassSymbol owner) => std is null || !ReferenceEquals(std.classes.GetValueOrDefault(owner.Name), owner);

    /// <summary>F9: the text of <c>not-visible</c> for a member of <paramref name="owner"/> used where it is not visible.</summary>
    private static string Hidden(string member, ClassSymbol owner, bool isInternal, string kind) => isInternal
        ? $"`{member}` of `{owner.Name}` is `internal` to the folder that declares its class: {kind} is visible outside that folder with `pub` (F9)"
        : $"`{member}` of `{owner.Name}` is private to its class: {kind} is visible outside it with `pub` (F9)";

    /// <summary>K7, F13: a call of a method on an instance, <c>c.Add(1)</c>, the one of its name whose parameters fit; a private one from outside its class is <c>not-visible</c> (F9).</summary>
    private Typed CallMethod(ClassType type, MemberAccess m, CallExpression c, KurzType? expected)
    {
        if (Property(type.Symbol, m.Member) is { } property)
        {
            var read = ReadProperty(m, type, property);
            if (read.IsError)
            {
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            model.RecordType(m, read);
            return CallValue(read.Type, c); // K7, F11: the property's value is called, as a field's value is
        }

        if (BesideField(MethodsOf(type.Symbol, m.Member), type.Symbol.Field(m.Member) is not null) is not { } methods)
        {
            if (type.Symbol.Field(m.Member) is { } field)
            {
                if (!FieldVisible(field, type.Symbol, m.Line))
                {
                    CheckArgumentsLoosely(c);
                    return Typed.Error;
                }

                var value = new Typed(field.Type);
                model.RecordType(m, value);
                return CallValue(field.Type, c); // F11: a field of function type is called as any function value
            }

            if (type.Symbol.StaticField(m.Member) is not null)
            {
                ClassMember(type, m.Member, m.Line); // K19: a `static` field reached through an instance
            }
            else
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{type.Symbol.Name}` has no method `{m.Member}` (V10)");
            }

            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var instance = methods.Where(f => !f.IsStatic).ToList();
        if (instance.Count == 0)
        {
            Report(ErrorIds.StaticMismatch, m.Line, $"`{m.Member}` is a `static` method of `{type.Symbol.Name}`: call it as `{type.Symbol.Name}.{m.Member}(...)` (K7, K19)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return PickMethod(instance, c, expected, m.Line, out _, type);
    }

    /// <summary>
    /// The method of the candidates a call picks (F13), recorded as a call of a method, or of a function where it is <c>static</c>;
    /// <c>not-visible</c> where it is private and the call stands outside its class, or <c>prot</c> and outside the classes derived from
    /// its class (F9). A <c>prot</c> method called in a derived class through a reference whose type is not that class or one derived
    /// from it, which C# refuses (CS1540), is <c>not supported</c>.
    /// </summary>
    private Typed PickMethod(List<FunctionSymbol> methods, CallExpression c, KurzType? expected, int line, out FunctionSymbol? picked, ClassType? receiver = null)
    {
        var typed = ResolveCall(methods, c, expected, out picked, CallKind.Method);
        if (picked is not null && !Visible(picked))
        {
            Report(ErrorIds.NotVisible, line, (picked.Declaration!.Modifiers & Modifiers.Prot) != 0
                ? $"`{picked.Name}` of `{picked.Owner!.Name}` is `prot`, visible in its class and the classes derived from it (F9)"
                : Hidden(picked.Name, picked.Owner!, (picked.Declaration!.Modifiers & Modifiers.Internal) != 0, picked.IsProperty ? "a property" : "a method"));
            return Typed.Error;
        }

        if (picked is not null && receiver is not null && (picked.Declaration!.Modifiers & (Modifiers.Pub | Modifiers.Prot)) == Modifiers.Prot
            && !ReferenceEquals(CurrentClass(), picked.Owner) && !receiver.Symbol.HasBaseOrIs(CurrentClass()!))
        {
            NotSupported(line, $"the `prot` method `{picked.Name}` called through {Types.A(receiver)}, which is not the calling class or derived from it (F9)");
            return Typed.Error;
        }

        return typed;
    }

    /// <summary>
    /// K7: a call that names a method of the class alone, inside one of its methods: a method of <c>this</c>, or a <c>static</c> one; inside
    /// a <c>static</c> member, which has no instance, a <c>static</c> one alone, and a method of an instance is <c>static-mismatch</c> (K19).
    /// </summary>
    private Typed CallImplicitMethod(List<FunctionSymbol> methods, NameExpression n, CallExpression c, KurzType? expected)
    {
        var candidates = InStaticMember() ? methods.Where(f => f.IsStatic).ToList() : methods;
        if (candidates.Count == 0)
        {
            InstanceMethodInStatic(n.Name, n.Line);
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var typed = PickMethod(candidates, c, expected, n.Line, out var picked);
        if (picked is { IsStatic: false })
        {
            model.RecordImplicitField(n, ThisVariable(), null);
        }

        return typed;
    }

    /// <summary>K1, K10: an instance made by the class's name, with the arguments of its primary constructor, bound as D11 binds them; a class without one takes none.</summary>
    private Typed CheckClassConstruction(ClassSymbol symbol, CallExpression c, KurzType? expected)
    {
        var slots = symbol.Constructor.Select(s => new Slot(s.Declaration.Name, s.Field.Type, false, s.Declaration.Default is not null, false)).ToList();
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
    /// reference need not be, since the reference does not change; a lambda that captured the instance, or a value that holds it, assigns
    /// it too (F11). A path that goes on into a data value after the instance's field is <c>not supported</c>. The value is read before
    /// the path, as M4 reads an assignment into a path (the emitter's order).
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

        var field = owner.Symbol.Field(last.Member);
        if (field is null && Property(owner.Symbol, last.Member) is not null)
        {
            AssignProperty(owner.Symbol, last.Member, a.Line);
            CheckExpression(a.Value, Untyped(a.Value));
            return;
        }

        if (field is null)
        {
            ClassMember(owner, last.Member, last.Line);
            CheckExpression(a.Value, Untyped(a.Value));
            return;
        }

        if (!FieldVisible(field, owner.Symbol, last.Line))
        {
            CheckExpression(a.Value, Untyped(a.Value));
            return;
        }

        model.RecordType(last, new Typed(field.Type));
        if (!field.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{last.Member}` is a field without `mut`, which nothing assigns (K1)");
        }

        var value = CheckExpression(a.Value, field.Type, keepCases: Types.KeepsCases(field.Type));
        EndChangeableNarrowing(); // N9: the assignment, which runs after its value, ends what was known of a path through a `mut` field
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
        else if (field.Mut)
        {
            Assigned(PathOf(a.Target), field.Type, value); // N9: a path with no `weak` reference is narrowed by its assignment
        }
    }

    /// <summary>
    /// M5 with K2: the receiver of a <c>mut</c> method that is a <c>mut</c> field of an instance, <c>node.Children.Add(x)</c>, which every
    /// reference reaches (M5's reading of K2 for paths); a field without <c>mut</c> is <c>mut-required</c>, a path that goes on into a
    /// data value after the field is <c>not supported</c>, and a lambda that captured the instance changes the field too (F11).
    /// Returns false where the receiver is no such field.
    /// </summary>
    private bool MutReceiverThroughInstance(Expression receiver, List<MemberAccess> members, int line, string what)
    {
        if (members.Count == 0 || !RunsThroughInstance(members))
        {
            if (Unwrap(receiver) is NameExpression n && Lookup(n.Name).Variable is null && ImplicitField(n.Name) is { } implicitField)
            {
                return MutField(implicitField, line, what);
            }

            if (members.Count > 0 && Unwrap(members[0].Target) is NameExpression root && Lookup(root.Name).Variable is null && ImplicitField(root.Name) is not null)
            {
                NotSupported(line, "a `mut` method on a value held by a field of an instance (K2, M5)"); // K7: a field named alone is a step through `this`
                return true;
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
            if (Property(owner.Symbol, members[^1].Member) is not null)
            {
                PropertyReceiver(members[^1].Member, line, what); // K7: read without an error, so reported here
            }

            return true; // reported where the receiver was read
        }

        return MutField(field, line, what);
    }

    /// <summary>
    /// K2 with M4 and M5: an item or an entry written into a collection a <c>mut</c> field of an instance holds, <c>note.Counts[k] = v</c>,
    /// read as a <c>mut</c> method on that field is (<see cref="MutReceiverThroughInstance"/>); a path that goes on into a data value
    /// after the field is <c>not supported</c>, as an assignment into one is. Returns whether the checker takes the write.
    /// </summary>
    private bool WrittenThroughInstance(Assignment a, IndexExpression index, List<MemberAccess> fields)
    {
        if (fields.Count > 0 && fields.FindLastIndex(m => PathType(m.Target) is ClassType) < fields.Count - 1)
        {
            NotSupported(a.Line, "an assignment into a value held by a field of an instance (K2, M4)");
            CheckLoosely(index.Index, a.Value);
            return false;
        }

        var what = PathType(index.Target) is MapType ? "an entry is written into the map" : "an item is written into the list";
        MutReceiverThroughInstance(index.Target, fields, a.Line, what);
        return true;
    }

    /// <summary>K7: inside a method, whether a name without a variable and without a field is a property of the class.</summary>
    private bool ImplicitProperty(string name) => ImplicitPropertyOf(name) is not null;

    /// <summary>
    /// K7: the property a name alone stands for inside a method of the class, which every reading of such a name takes from here: the
    /// one a read through an instance reads (<see cref="Property"/>), of the nearest class in the chain that declares the name and none
    /// beside a field of it, where the method reaches it (no property is static, so none in a field's default). A base's property the
    /// class does not see stands behind a field or a method of the class's own of its name, which a name alone reads or calls (F9).
    /// </summary>
    private FunctionSymbol? ImplicitPropertyOf(string name) =>
        CurrentClass() is { } c && Property(c, name) is { } property && ImplicitMethods(name) is { } reached && reached.Contains(property) ? property : null;

    /// <summary>
    /// K7: the methods a call of a name picks from where a field of the class has the name too. A property of a base stands beside such
    /// a field only where the class does not see it (F9), since a member with the name of a property it sees is <c>hides-member</c>, and
    /// the field stands before it, as a read takes the field and <see cref="Property"/> finds no property beside one. So the property is
    /// no candidate of the call, and null is left where it was the only one.
    /// </summary>
    private static List<FunctionSymbol>? BesideField(List<FunctionSymbol>? methods, bool field) =>
        field && methods is not null ? (methods.Where(f => !f.IsProperty).ToList() is { Count: > 0 } rest ? rest : null) : methods;

    /// <summary>K7, M5: a change of the value a property gives, which is a call's value: no place holds it, so no change reaches it.</summary>
    private void PropertyReceiver(string name, int line, string what) =>
        Report(ErrorIds.MutRequired, line, $"{what}, and `{name}` is a property, whose value no change reaches: only a value reachable through a `mut` variable changes (M5, K7)");

    private bool MutField(FieldSymbol field, int line, string what)
    {
        if (!field.Mut)
        {
            Report(ErrorIds.MutRequired, line, $"{what}, and `{field.Name}` is a field without `mut` (M5, K1)");
        }

        return true;
    }

    // ---- N9: paths that a call can change ---------------------------------------------------------

    /// <summary>
    /// N9: whether a path known not null runs through a <c>mut</c> field of an instance, which a call can assign through another
    /// reference (K2), or through a <c>weak</c> reference, whose target a call can free (R3), so that what is known of it lasts up to the
    /// next call and the next assignment.
    /// </summary>
    private static bool Changeable(NullPath path) => Steps(path).Any(s => s.Mut || s.Type is WeakRefType);

    /// <summary>N9: whether a path runs through a <c>weak</c> reference, the variable itself or a field on the way.</summary>
    private static bool ThroughWeak(NullPath path) => Steps(path).Any(s => s.Type is WeakRefType);

    /// <summary>N9: the root of a path and each field it reads, with its type and whether it is a <c>mut</c> field of an instance.</summary>
    private static IEnumerable<(KurzType Type, bool Mut)> Steps(NullPath path)
    {
        var current = path.Root.Type;
        yield return (current, false);
        foreach (var name in path.Fields.Length == 0 ? [] : path.Fields.Split('.'))
        {
            switch (Types.NonNull(current) ?? current)
            {
                case ClassType c when c.Symbol.Field(name) is { } field:
                    current = field.Type;
                    yield return (current, field.Mut);
                    break;
                case DataType d when d.Symbol.Field(name) is { } field:
                    current = SubstituteData(d, field.Type);
                    yield return (current, false);
                    break;
                default:
                    yield break;
            }
        }
    }

    // N9: the calls read so far, so that a condition knows whether one ran in it
    private int callsRun;

    /// <summary>N9: a call ran, which ends what was known of every path a call can change, and counts for the end of a condition.</summary>
    private void CallRan()
    {
        callsRun++;
        EndChangeableNarrowing();
    }

    /// <summary>
    /// N9 (the owner, 2026-11-01, "Those ends stop narrowing"): what a condition shows where it is true and where it is false, less every
    /// path through a <c>weak</c> reference where a call ran in it since <paramref name="callsBefore"/>: a count let go of at the end of
    /// the condition can free the target before the block runs.
    /// </summary>
    private (ImmutableHashSet<NullPath> True, ImmutableHashSet<NullPath> False) ConditionTests(Expression condition, int callsBefore)
    {
        var (whenTrue, whenFalse) = Tests(condition);
        return callsRun == callsBefore ? (whenTrue, whenFalse) : (Unweak(whenTrue), Unweak(whenFalse));
    }

    /// <summary>N9: what is known of paths with no <c>weak</c> reference in them, which the end of a block that declares a variable keeps.</summary>
    private static ImmutableHashSet<NullPath> Unweak(ImmutableHashSet<NullPath> known) =>
        known.Any(ThroughWeak) ? known.Where(p => !ThroughWeak(p)).ToImmutableHashSet() : known;

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
            if (!edges.TryGetValue(at, out var fields))
            {
                // W1: a class of std/ that the prelude brings into the file, whose fields std/'s checker judged; it names no class of
                // the file, so no ring of the file runs through it
                continue;
            }

            foreach (var (to, field) in fields)
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

    /// <summary>The classes a value of the type holds strongly: itself and every class derived from it, the items of a list, the keys and values of a map, the cases of a union and the fields of a data value.</summary>
    private void ClassesHeld(KurzType type, HashSet<ClassSymbol> reached, HashSet<DataSymbol> path)
    {
        switch (type)
        {
            case ClassType c:
                reached.UnionWith(classDeclared.Where(x => x.HasBaseOrIs(c.Symbol))); // K6: an instance of a derived class stands where its base does
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
        ClassType or WeakRefType => true,
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
        WeakRefType w => PrivateTextClass(w.Target, path), // A4 with R3: the text of what its read gives
        ListType l => PrivateTextClass(l.Element, path),
        MapType m => PrivateTextClass(m.Key, path) ?? PrivateTextClass(m.Value, path),
        UnionType u => u.Cases.Select(c => PrivateTextClass(c, path)).FirstOrDefault(c => c is not null),
        DataType d when path.Add(d.Symbol) => d.Symbol.Fields.Select(f => PrivateTextClass(SubstituteData(d, f.Type), path)).FirstOrDefault(c => c is not null),
        DataType d => HeldArguments(d).Select(a => PrivateTextClass(a, path)).FirstOrDefault(c => c is not null),
        _ => null,
    };
}
