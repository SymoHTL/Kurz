using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// D16: the body of a <c>data</c> type, in the form of a class body (K7): its methods, <c>mut</c> ones among them (M5, M6), and a
/// <c>Text()</c> that gives a value its text in place of A3's (A5). A method is checked and emitted as a function of the file whose
/// first parameter is the value it was called on, <c>this</c>, <c>mut</c> in a <c>mut</c> method, as Rust's <c>self</c> and
/// <c>&amp;mut self</c>: in its copy of the body a field named alone reads <c>this</c>'s field, an assignment of a field's name assigns
/// it, and a method named alone is called on <c>this</c>. A call picks the method by the symbol whose parameters are the written ones
/// (<see cref="FunctionSymbol.Real"/>), so that its arguments are matched as a class method's are (F13).
/// </summary>
public sealed partial class Checker
{
    // D16: the data types of the file that write a body, in the order of their declaration, each declared once every base is known
    private readonly List<DataSymbol> dataBodies = [];

    // D16: each method of a data body the checker reads: the function it is checked as, and the method as written
    private readonly List<(FunctionSymbol Real, FunctionDeclaration Written)> dataMethods = [];

    // M7, D16: the calls of a `mut` method of a data type, whose receiver changes while the arguments the call holds are in use
    private readonly HashSet<CallExpression> dataReceivers = new(ReferenceEqualityComparer.Instance);

    // E1, D16: the methods of a data body that their declaration reported as an error, a method without a body, so that a use of one
    // carries no further diagnostic
    private readonly HashSet<(DataSymbol Type, string Name)> dataMethodErrors = [];

    /// <summary>D16: the data type whose method is being checked, inside its lambdas too; null anywhere else.</summary>
    private DataSymbol? CurrentData() => checking?.DataOwner;

    /// <summary>
    /// D16, F9: a field of a data type read or assigned where the checker stands: one of the primary constructor wherever the type is,
    /// one of the body without <c>pub</c> inside the type whose body declares it alone, its methods and the initializers of its fields,
    /// as a class's (K1), so that a type derived from it does not see it (F9); else <c>not-visible</c>, reported, and false.
    /// </summary>
    private bool DataFieldVisible(FieldSymbol field, DataSymbol owner, int line)
    {
        if (field.IsPub || ReferenceEquals(CurrentData(), owner.Declaring(field)))
        {
            return true;
        }

        Report(ErrorIds.NotVisible, line, $"`{field.Name}` of `{owner.Name}` is private to its type: a field of the body is visible outside it with `pub` (F9, D16)");
        return false;
    }

    /// <summary>D16: the names a data type's body declares, a member the checker refused among them.</summary>
    private static IEnumerable<string> BodyNames(DataSymbol symbol) => (symbol.Declaration.Members ?? []).Select(m => m switch
    {
        FunctionDeclaration f => f.Name,
        FieldDeclaration f => f.Name,
        _ => string.Empty,
    }).Where(name => name.Length > 0);

    /// <summary>
    /// D16, F9: whether the body of <paramref name="b"/> or of a base of it declares a <c>pub</c> method of the name, which a derived type
    /// sees; a private one it does not see, so that a member of a derived type's body may take its name, as a class's may (K7).
    /// </summary>
    private static bool SeenBaseMethod(DataSymbol b, string name) =>
        b.Chain.Any(s => (s.Declaration.Members ?? []).OfType<FunctionDeclaration>().Any(m => m.Name == name && (m.Modifiers & Modifiers.Pub) != 0));

    /// <summary>
    /// D16, K7: the methods of a name a call on a value of the data type picks from (F13): its own, and those of each base up the chain
    /// that no method nearer the type has the parameter types of, so that an override stands for what it overrides; null where none has one.
    /// </summary>
    private static List<FunctionSymbol>? DataMethodsOf(DataSymbol symbol, string name) => NearestMethods(symbol.Chain.Select(s => s.Methods), name);

    /// <summary>
    /// D16: the bodies of the file's data types, declared once every data type is resolved, so that a base is known, and each after its
    /// base's, so that a type derived from one whose chain declares fields in a body takes them (<see cref="TakeBaseBodyFields"/>). A body
    /// the checker does not read in full is <c>not supported</c> at the part it does not read, and the rest of the body is still declared.
    /// </summary>
    private void DeclareDataBodies()
    {
        foreach (var symbol in dataDeclared.OrderBy(s => s.Chain.Count()))
        {
            if (symbol.Base is { } b)
            {
                TakeBaseBodyFields(symbol, b);

                // a field of the type's primary constructor named like a `pub` method of a base's body is `redeclared`, as a field of its
                // body is; a private one it does not see (V9, F9, D16)
                foreach (var p in (symbol.Declaration.PrimaryConstructor ?? []).Where(p => SeenBaseMethod(b, p.Name)))
                {
                    Report(ErrorIds.Redeclared, p.Line, $"`{p.Name}` is a method of a base of `{symbol.Name}` already (V9, D16)");
                }
            }

            if (!dataBodies.Contains(symbol))
            {
                continue;
            }

            var members = symbol.Declaration.Members!;
            if (symbol.IsGeneric)
            {
                NotSupported(members[0].Line, "the body of a generic data type (D16, T19)");
                continue;
            }

            // D16: the fields first, so that a method named like a field below it is `redeclared` as one named like a field above it (V9)
            foreach (var field in members.OfType<FieldDeclaration>())
            {
                DeclareDataField(symbol, field);
            }

            foreach (var member in members.Where(m => m is not FieldDeclaration))
            {
                if (member is FunctionDeclaration f && UnreadDataMethod(f) is not { } construct)
                {
                    DeclareDataMethod(symbol, f);
                }
                else
                {
                    NotSupported(member.Line, member is FunctionDeclaration refused ? UnreadDataMethod(refused)! : member switch
                    {
                        ConstructorDeclaration => "a constructor in the body of a data type (D16, K14)",
                        _ => "a member of the body of a data type other than a method or a field (D16)",
                    });
                }
            }
        }
    }

    /// <summary>
    /// D6, D16: the fields of a type derived from a base whose chain declares fields in its body: the base's fields as the base holds
    /// them, its fields of the body among them, then the type's own, so that a block of the type starts as a block of its base does and
    /// each type's fields of the primary constructor and of the body stand together, the base's first (§4, the owner, 2026-11-12, issue
    /// 455, comment 6819137865, H2 (b)). The resolution copied the base's fields before any body was declared; a field of the type's
    /// primary constructor named like a field of a base's body is <c>redeclared</c> (V9), and one named like a <c>pub</c> method of a
    /// base's body is refused where the bodies are declared (<see cref="DeclareDataBodies"/>).
    /// </summary>
    private void TakeBaseBodyFields(DataSymbol symbol, DataSymbol b)
    {
        var copied = b.ConstructorFields.Count;
        if (b.Fields.Count == copied || symbol.Fields.Count < copied)
        {
            return;
        }

        var own = symbol.Fields.Skip(copied).ToList();
        symbol.Fields.Clear();
        symbol.Fields.AddRange(b.Fields);
        foreach (var field in own)
        {
            if (b.Field(field.Name) is { Body: not null } taken)
            {
                Report(ErrorIds.Redeclared, field.Line, $"the field `{field.Name}` is declared already, in the body of `{b.Declaring(taken).Name}` (V9, D16)");
            }

            symbol.Fields.Add(field);
        }
    }

    /// <summary>The part of a field of a data body that the checker does not read, or null where it reads all of it.</summary>
    private static string? UnreadDataField(DataSymbol symbol, FieldDeclaration f) => f switch
    {
        _ when symbol.Declaration.PrimaryConstructor is null => "a field in the body of a data type without a primary constructor (D8, D16)",
        _ when (f.Modifiers & Modifiers.Static) != 0 => "a `static` field of a data type (D16, K18)",
        _ when (f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut)) != 0 => $"the modifier `{(f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut)).ToString().ToLowerInvariant()}` of a field of a data type (D16, F9)",
        { Type: null } => "a field of a data body without a written type (D16, V2)",
        _ => null,
    };

    /// <summary>
    /// D16: a field declared in a data body, after the fields of the primary constructor in the order of the declarations, given by its
    /// initializer alone, which no construction and no <c>with</c> gives (§4, the owner, 2026-11-12, and comment 6821275386): one without
    /// <c>=</c> is <c>field-unassigned</c>, as a field of a class body that no constructor assigns is (K7, K14), and one named like a field
    /// above it is <c>redeclared</c> (V9). Its initializer is checked as a function of the file, named <c>Type.Field</c>, which no name a
    /// program writes can call, whose one parameter is the value being made, <c>this</c>: in its copy of the initializer a field of the
    /// primary constructor or of the body above it, named alone, reads <c>this</c>'s field, as a default of a class reads its primary
    /// fields and the fields above it (K7); a field below it, a method of the type and <c>this</c> are no names there (V10, as K7).
    /// </summary>
    private void DeclareDataField(DataSymbol symbol, FieldDeclaration f)
    {
        if (UnreadDataField(symbol, f) is { } construct)
        {
            NotSupported(f.Line, construct);
            return;
        }

        var type = Resolve(f.Type!);
        if (Types.IsVoid(type))
        {
            Report(ErrorIds.Syntax, f.Line, "`void` is no type of a field (F4)");
            type = Types.Error;
        }

        if (symbol.Field(f.Name) is not null)
        {
            Report(ErrorIds.Redeclared, f.Line, $"the field `{f.Name}` is declared already (V9)");
            return;
        }

        if (symbol.Base is { } b && SeenBaseMethod(b, f.Name))
        {
            Report(ErrorIds.Redeclared, f.Line, $"`{f.Name}` is a method of a base of `{symbol.Name}` already (V9, D16)");
            return;
        }

        // the fields of every primary constructor of the chain, which a construction gives, and those of the type's own body above it: the
        // initializers of a derived type's body run before its base's, in a class's order (§4, issue 455, comment 6808456513, H4)
        var above = symbol.Fields.Where(field => field.Body is null || ReferenceEquals(symbol.Declaring(field), symbol)).Select(field => field.Name).ToHashSet();
        var field = new FieldSymbol(f.Name, type, f.Value is not null, (f.Modifiers & Modifiers.Mut) != 0, f.Line, f);
        symbol.Fields.Add(field);
        if (f.Value is null)
        {
            Report(ErrorIds.FieldUnassigned, f.Line, $"the field `{f.Name}` takes no value: it has no `=`, and no construction of `{symbol.Name}` gives a field of its body (D16)");
            return;
        }

        var self = new Parameter(f.Line, false, new NamedType(f.Line, symbol.Name, []), "this", null);
        // `Total = value` of such a field, in a lambda's block, assigns the field of `this`, as in a lambda of a method (K7, F11)
        var value = SyntaxCopy.Copy(
            f.Value,
            (e, _) => e is NameExpression n && above.Contains(n.Name) ? new MemberAccess(n.Line, new NameExpression(n.Line, "this"), n.Name, false) : null,
            (s, copy) => s is AssignOrDeclare a && above.Contains(a.Name) ? new Assignment(a.Line, new MemberAccess(a.Line, new NameExpression(a.Line, "this"), a.Name, false), copy(a.Value)) : null);
        var real = new FunctionDeclaration(f.Line, Modifiers.None, f.Type!, $"{symbol.Name}.{f.Name}", [], [self], value, null);
        DeclareFunction(real);
        var function = model.FunctionOf(real);
        function.DataOwner = symbol;
        function.DataField = field;
        symbol.Initializers[f.Name] = function;
    }

    /// <summary>The part of a method of a data body that the checker does not read, or null where it reads all of it.</summary>
    private static string? UnreadDataMethod(FunctionDeclaration f) => f switch
    {
        { Property: true } => "a property of a data type, which section 14 leaves open (D16, K7)",
        { Name: "Equals" or "GetHashCode" } => $"a method named `{f.Name}` of a data type, which section 14 leaves open (D16, K5)",
        _ when (f.Modifiers & Modifiers.Static) != 0 => "a `static` method of a data type (D16, K19)",
        _ when (f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut | Modifiers.Virtual | Modifiers.Override)) != 0 => $"the modifier `{(f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut | Modifiers.Virtual | Modifiers.Override)).ToString().ToLowerInvariant()}` of a method of a data type (D16, F9)",
        { Name: "Text" } when (f.Modifiers & Modifiers.Mut) != 0 => "a `mut` method named `Text` of a data type (D16, A5)",
        _ when f.TypeParameters.Count > 0 => "a method with type parameters of its own (section 4, T19)",
        _ => null,
    };

    /// <summary>
    /// D16: a method of a data body, declared as a function of the file named <c>Type.Method</c>, which no name a program writes can
    /// call, with the value first; a method named like a field is <c>redeclared</c> (V9, as C# refuses a member named like another).
    /// </summary>
    private void DeclareDataMethod(DataSymbol symbol, FunctionDeclaration f)
    {
        if (symbol.Field(f.Name) is not null)
        {
            Report(ErrorIds.Redeclared, f.Line, $"`{f.Name}` is a field of `{symbol.Name}` already (V9, D16)");
            return;
        }

        if (f.ExpressionBody is null && f.BlockBody is null)
        {
            Report(ErrorIds.Syntax, f.Line, "a method of a data type has a body, as one of a class body has (K7, F2)");
            dataMethodErrors.Add((symbol, f.Name));
            return;
        }

        var mut = (f.Modifiers & Modifiers.Mut) != 0;
        var self = new Parameter(f.Line, mut, new NamedType(f.Line, symbol.Name, []), "this", null);
        var real = f with
        {
            Modifiers = Modifiers.None,
            Name = $"{symbol.Name}.{f.Name}",
            Parameters = [self, .. f.Parameters],
            ExpressionBody = f.ExpressionBody is null ? null : SyntaxCopy.Copy(f.ExpressionBody, (e, _) => OnThis(symbol, e), (s, copy) => AssignedOnThis(symbol, s, copy)),
            BlockBody = f.BlockBody is null ? null : SyntaxCopy.Copy(f.BlockBody, (e, _) => OnThis(symbol, e), (s, copy) => AssignedOnThis(symbol, s, copy)),
        };

        DeclareFunction(real);
        var function = model.FunctionOf(real);
        function.DataOwner = symbol;
        dataMethods.Add((function, f));
    }

    /// <summary>D16: in a method's copy of its body, <c>this</c> and a member of the type or of a base named alone, read on the parameter <c>this</c> (K7).</summary>
    private static Expression? OnThis(DataSymbol symbol, Expression e) => e switch
    {
        ThisExpression t => new NameExpression(t.Line, "this"),
        NameExpression n when symbol.Field(n.Name) is not null || BodyDeclares(symbol, n.Name) => new MemberAccess(n.Line, new NameExpression(n.Line, "this"), n.Name, false),
        _ => null,
    };

    /// <summary>D16: in a method's copy of its body, <c>Count = value</c> of a field, which assigns the field of <c>this</c> (K7).</summary>
    private static Assignment? AssignedOnThis(DataSymbol symbol, Statement s, Func<Expression, Expression> copy) => s switch
    {
        AssignOrDeclare a when symbol.Field(a.Name) is not null => new Assignment(a.Line, new MemberAccess(a.Line, new NameExpression(a.Line, "this"), a.Name, false), copy(a.Value)),
        _ => null,
    };

    /// <summary>
    /// D16: once the signatures are resolved and a duplicate is taken out (F12), each method a call can pick: the method as written,
    /// its parameters the function's after <c>this</c>, its result the function's.
    /// </summary>
    private void MakeDataMethodViews()
    {
        foreach (var (real, written) in dataMethods.Where(pair => functions.TryGetValue(pair.Real.Name, out var list) && list.Contains(pair.Real)))
        {
            var symbol = real.DataOwner!;
            var view = new FunctionSymbol(written.Name, written) { DataOwner = symbol, Real = real, ReturnType = real.ReturnType };
            view.Parameters.AddRange(real.Parameters.Skip(1));
            if (!symbol.Methods.TryGetValue(written.Name, out var methods))
            {
                methods = [];
                symbol.Methods[written.Name] = methods;
            }

            methods.Add(view);
        }

        foreach (var symbol in dataDeclared)
        {
            foreach (var view in symbol.Methods.Values.SelectMany(list => list))
            {
                CheckDataOverride(symbol, view);
            }
        }
    }

    /// <summary>
    /// D16, K7: a method of a data type, judged as a class's against the nearest method of a base with its name and its parameter types,
    /// one with <c>pub</c>, which a derived type sees (F9); a private one is hidden by nothing (F9), and in a type without a base an
    /// <c>override</c> overrides nothing, <c>override-without-virtual</c>, as in a class without one. An override is
    /// <c>mut</c> exactly when the method it overrides is, else <c>override-mut</c> (§4, the owner, 2026-11-12, issue 455, comment
    /// 6819137865, H1 (a)). <c>virtual</c> and <c>override</c> on one method, and <c>virtual</c> on a private method, are <c>not
    /// supported</c>, as a class's (K7).
    /// </summary>
    private void CheckDataOverride(DataSymbol symbol, FunctionSymbol method)
    {
        var d = method.Declaration!;
        var marks = d.Modifiers & (Modifiers.Virtual | Modifiers.Override);
        if (marks == (Modifiers.Virtual | Modifiers.Override) || (marks == Modifiers.Virtual && (d.Modifiers & Modifiers.Pub) == 0))
        {
            NotSupported(d.Line, marks == (Modifiers.Virtual | Modifiers.Override) ? "`virtual` and `override` on one method (D16, K7)" : "`virtual` on a private method, which no derived type sees (D16, K7, F9)");
            return;
        }

        var overridden = symbol.Base?.Chain
            .Select(s => s.Methods.TryGetValue(method.Name, out var list) ? list.FirstOrDefault(m => SameParameters(m, method) && (m.Declaration!.Modifiers & Modifiers.Pub) != 0) : null)
            .FirstOrDefault(m => m is not null);
        if (JudgeOverride(method, overridden, $"type of `{symbol.Name}`", "D16, K7") is not { } taken)
        {
            return;
        }

        if (((d.Modifiers ^ taken.Declaration!.Modifiers) & Modifiers.Mut) != 0)
        {
            Report(ErrorIds.OverrideMut, d.Line, (d.Modifiers & Modifiers.Mut) != 0
                ? $"`{method.Name}` is `mut`, and the method of `{OwnerName(taken)}` it overrides is not: an override is `mut` exactly when the method it overrides is (D16, K7)"
                : $"`{method.Name}` of `{OwnerName(taken)}` is `mut`, and its override here is not: an override is `mut` exactly when the method it overrides is (D16, K7)");
            return;
        }

        method.Overridden = taken;
    }

    /// <summary>
    /// D16, F13: a call of a method of a data type on a value of it, the one of its name whose parameters fit; a method without
    /// <c>pub</c> is visible inside its type alone (F9), and a <c>mut</c> one takes a value reachable through a <c>mut</c> variable (M5).
    /// </summary>
    private Typed CallDataMethod(DataType type, MemberAccess m, CallExpression c, KurzType? expected)
    {
        if (DataMethodsOf(type.Symbol, m.Member) is not { } methods)
        {
            if (!type.Symbol.Chain.Any(s => dataMethodErrors.Contains((s, m.Member))))
            {
                NotSupported(m.Line, "a member of the body of a data type that the checker does not read (D16)"); // refused where it is declared
            }

            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var seen = methods.Where(f => (f.Declaration!.Modifiers & Modifiers.Pub) != 0 || ReferenceEquals(CurrentData(), f.DataOwner)).ToList();
        if (seen.Count == 0)
        {
            Report(ErrorIds.NotVisible, m.Line, $"`{m.Member}` of `{OwnerName(methods[0])}` is private to its type: a method is visible outside it with `pub` (F9, D16)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var typed = ResolveCall(seen, c, expected, out var picked, CallKind.DataMethod);
        if (picked is not null && (picked.Declaration!.Modifiers & Modifiers.Mut) != 0)
        {
            MutReceiver(c, m.Target, c.Line, $"`{m.Member}` changes the value it is called on");
            dataReceivers.Add(c);
        }

        return typed;
    }

    /// <summary>D16: whether a variable is the value a method of a data type was called on, which a method without <c>mut</c> does not change (M5).</summary>
    private bool IsDataThis(VariableSymbol variable) => variable.Name == "this" && variable.Kind == VariableKind.Parameter && CurrentData() is not null;
}
