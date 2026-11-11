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

    /// <summary>D16: the data type whose method is being checked, inside its lambdas too; null anywhere else.</summary>
    private DataSymbol? CurrentData() => checking?.DataOwner;

    /// <summary>D16: the names a data type's body declares, a member the checker refused among them.</summary>
    private static IEnumerable<string> BodyNames(DataSymbol symbol) => (symbol.Declaration.Members ?? []).Select(m => m switch
    {
        FunctionDeclaration f => f.Name,
        FieldDeclaration f => f.Name,
        _ => string.Empty,
    }).Where(name => name.Length > 0);

    /// <summary>
    /// D16: the bodies of the file's data types, declared once every data type is resolved, so that a base is known. A body the checker
    /// does not read in full is <c>not supported</c> at the part it does not read, and the rest of the body is still declared.
    /// </summary>
    private void DeclareDataBodies()
    {
        foreach (var symbol in dataBodies)
        {
            var members = symbol.Declaration.Members!;
            if (symbol.IsGeneric)
            {
                NotSupported(members[0].Line, "the body of a generic data type (D16, T19)");
                continue;
            }

            if (symbol.Base is not null || dataDeclared.Any(other => ReferenceEquals(other.Base, symbol)))
            {
                NotSupported(members[0].Line, "the body of a data type that derives from another or that another derives from (D16, D6)");
                continue;
            }

            foreach (var member in members)
            {
                if (member is FunctionDeclaration f && UnreadDataMethod(f) is not { } construct)
                {
                    DeclareDataMethod(symbol, f);
                }
                else
                {
                    NotSupported(member.Line, member is FunctionDeclaration refused ? UnreadDataMethod(refused)! : member switch
                    {
                        FieldDeclaration => "a field declared in the body of a data type (D16, F9)",
                        ConstructorDeclaration => "a constructor in the body of a data type (D16, K14)",
                        _ => "a member of the body of a data type other than a method (D16)",
                    });
                }
            }
        }
    }

    /// <summary>The part of a method of a data body that the checker does not read, or null where it reads all of it.</summary>
    private static string? UnreadDataMethod(FunctionDeclaration f) => f switch
    {
        { Property: true } => "a property of a data type, which section 14 leaves open (D16, K7)",
        { Name: "Equals" or "GetHashCode" } => $"a method named `{f.Name}` of a data type, which section 14 leaves open (D16, K5)",
        _ when (f.Modifiers & Modifiers.Static) != 0 => "a `static` method of a data type (D16, K19)",
        _ when (f.Modifiers & (Modifiers.Virtual | Modifiers.Override)) != 0 => "a `virtual` or `override` method of a data type (D16, K7)",
        _ when (f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut)) != 0 => $"the modifier `{(f.Modifiers & ~(Modifiers.Pub | Modifiers.Mut)).ToString().ToLowerInvariant()}` of a method of a data type (D16, F9)",
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
            Report(ErrorIds.Syntax, f.Line, "a method of a data type has a body (D16, K7, F2)");
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

    /// <summary>D16: in a method's copy of its body, <c>this</c> and a member named alone, read on the parameter <c>this</c>.</summary>
    private static Expression? OnThis(DataSymbol symbol, Expression e) => e switch
    {
        ThisExpression t => new NameExpression(t.Line, "this"),
        NameExpression n when symbol.Field(n.Name) is not null || BodyNames(symbol).Contains(n.Name) => new MemberAccess(n.Line, new NameExpression(n.Line, "this"), n.Name, false),
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
    }

    /// <summary>
    /// D16, F13: a call of a method of a data type on a value of it, the one of its name whose parameters fit; a method without
    /// <c>pub</c> is visible inside its type alone (F9), and a <c>mut</c> one takes a value reachable through a <c>mut</c> variable (M5).
    /// </summary>
    private Typed CallDataMethod(DataType type, MemberAccess m, CallExpression c, KurzType? expected)
    {
        if (!type.Symbol.Methods.TryGetValue(m.Member, out var methods))
        {
            NotSupported(m.Line, "a member of the body of a data type that the checker does not read (D16)"); // refused where it is declared
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var seen = methods.Where(f => (f.Declaration!.Modifiers & Modifiers.Pub) != 0 || ReferenceEquals(CurrentData(), type.Symbol)).ToList();
        if (seen.Count == 0)
        {
            Report(ErrorIds.NotVisible, m.Line, $"`{m.Member}` of `{type.Symbol.Name}` is private to its type: a method is visible outside it with `pub` (F9, D16)");
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
