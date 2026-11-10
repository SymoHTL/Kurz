using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// K14: the constructors a class declares in its body. A further constructor of a class with a primary constructor calls it first
/// with <c>: this(...)</c>, else <c>constructor-must-chain</c>; a class without one has C#'s constructors, which may call another of
/// the body with <c>: this(...)</c> or pass the arguments of its base with <c>: base(...)</c>, and a construction picks among them as a
/// call picks among overloads (F13), a private one visible inside its class alone (F9). A field of the body without <c>=</c> in a class with no primary constructor, no base and no derived
/// classes is assigned on every path of every constructor that calls no other, once where it has no <c>mut</c>, as C#'s definite
/// assignment reads a <c>readonly</c> field and Rust's an immutable binding (E0381, E0384): a second assignment is
/// <c>assign-immutable</c>, a path that leaves it unassigned and a read before its first assignment are <c>field-unassigned</c>. The
/// flow of that analysis is two marks per field in <see cref="Frame.Known"/>, assigned on every path so far and on none so far, so
/// that every join and every loop of the flow of the code reads them as it reads what is known not null (N3). The base's constructor
/// runs before the body, as C#, Java and Swift run it: the one <c>: base(...)</c> picks, else the one that takes no arguments, as C#'s
/// implied <c>base()</c>. What the checker does not read is <c>not supported</c>: a use of the instance,
/// <c>this</c>, a method of it or a lambda that reaches it, before every field is assigned (Swift's rule); <c>this</c> and a method of
/// the instance in a constructor of a class with derived classes, where an override could read a field of the derived class while
/// the constructor runs; <c>: this(...)</c> calls that come back to their constructor (C#'s CS0768); and a default that reads a field
/// a constructor's body assigns (<c>compiler/README.md</c>).
/// </summary>
public sealed partial class Checker
{
    private readonly HashSet<ConstructorSymbol> duplicateConstructors = new(ReferenceEqualityComparer.Instance);

    /// <summary>The classes without a primary constructor that write arguments after the name of their base, which is <c>not supported</c>.</summary>
    private readonly HashSet<ClassSymbol> baseArgumentsRefused = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// K14: a constructor's body while it is read: the fields it assigns, each with its two marks, assigned on every path so far and
    /// on none so far, and what held at each <c>return</c>.
    /// </summary>
    private sealed class Building(ConstructorSymbol constructor)
    {
        public ConstructorSymbol Constructor { get; } = constructor;

        public List<(FieldSymbol Field, NullPath Assigned, NullPath Unassigned)> Marks { get; } = [];

        public List<ImmutableHashSet<NullPath>> Returns { get; } = [];

        public (NullPath Assigned, NullPath Unassigned)? MarksOf(FieldSymbol field)
        {
            foreach (var (f, assigned, unassigned) in Marks)
            {
                if (ReferenceEquals(f, field))
                {
                    return (assigned, unassigned);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// K14, F12: the parameters of each constructor of the class's body, typed as a function's are; one with the parameter types of an
    /// earlier one, or of the primary constructor, is <c>duplicate-function</c> and is taken out of what a construction picks (E1).
    /// </summary>
    private void ResolveConstructors(ClassSymbol symbol)
    {
        foreach (var k in symbol.Constructors)
        {
            foreach (var p in k.Declaration.Parameters)
            {
                var type = p.Type is null ? Types.Error : Resolve(p.Type);
                if (p.Type is null)
                {
                    Report(ErrorIds.Syntax, p.Line, $"the parameter `{p.Name}` of a constructor needs a type (F1)");
                }
                else if (Types.IsVoid(type))
                {
                    Report(ErrorIds.Syntax, p.Line, "`void` is no type of a parameter (F4)");
                    type = Types.Error;
                }

                if (k.Parameters.Any(q => q.Name == p.Name))
                {
                    Report(ErrorIds.Redeclared, p.Line, $"the parameter `{p.Name}` is declared already (V9)");
                }

                k.Parameters.Add(new ParameterSymbol(p.Name, type, p.Mut, p.Default is not null, p.Line));
            }
        }

        var primary = symbol.Declaration.PrimaryConstructor is null ? null : symbol.Constructor.Select(s => s.Field.Type).ToList();
        for (var i = 0; i < symbol.Constructors.Count; i++)
        {
            var k = symbol.Constructors[i];
            var types = k.Parameters.Select(p => p.Type).ToList();
            if ((primary is not null && SameTypes(primary, types)) || symbol.Constructors.Take(i).Any(e => !duplicateConstructors.Contains(e) && SameTypes(e.Parameters.Select(p => p.Type).ToList(), types)))
            {
                Report(ErrorIds.DuplicateFunction, k.Line, $"`{symbol.Name}` has a constructor with these parameter types already (F12)");
                duplicateConstructors.Add(k);
            }
        }
    }

    private static bool SameTypes(List<KurzType> a, List<KurzType> b) => a.Count == b.Count && a.Zip(b).All(x => Types.Same(x.First, x.Second));

    /// <summary>K14: whether the constructors of the class's body assign its fields without <c>=</c>: it has no primary constructor, which would leave them unset, and is in no chain, whose classes have no field in their bodies (K6, K7, K18).</summary>
    private static bool AssignsInBody(ClassSymbol symbol) =>
        symbol.Constructors.Count > 0 && symbol.Declaration.PrimaryConstructor is null && symbol.Base is null && !symbol.Derived;

    /// <summary>
    /// K14: whether building <paramref name="b"/> as the base of a class without a primary constructor runs a constructor: one of its
    /// body, a primary one that takes parameters, or the construction of its own base.
    /// </summary>
    private static bool RunsAConstructor(ClassSymbol b) => b.Constructors.Count > 0 || b.Constructor.Count > 0 || b.BaseCall is not null;

    /// <summary>
    /// K14: whether <paramref name="b"/> has a constructor a construction with no arguments can pick, every parameter of it with a
    /// default: its primary one, or the empty one of a class without a constructor in its body, or one of its body.
    /// </summary>
    private static bool TakesNoArguments(ClassSymbol b) =>
        ((b.Declaration.PrimaryConstructor is not null || b.Constructors.Count == 0) && b.Constructor.All(s => s.Declaration.Default is not null))
        || b.Constructors.Any(k => k.Declaration.Parameters.All(p => p.Default is not null));

    /// <summary>
    /// K14: what runs before each constructor's body, its parameters' defaults and its <c>: this(...)</c> or the construction of its
    /// base, then its body, class by class; the arguments of <c>: this(...)</c> and <c>: base(...)</c> read the parameters and no
    /// instance, as C# reads them, and a member of the instance named there is <c>static-mismatch</c> (§4, the owner, 2026-11-09).
    /// </summary>
    private void CheckConstructorBodies()
    {
        foreach (var symbol in classDeclared)
        {
            foreach (var k in symbol.Constructors)
            {
                CheckConstructorInitializer(symbol, k);
            }

            RefuseConstructorCycles(symbol);
            foreach (var k in symbol.Constructors)
            {
                CheckConstructorBody(symbol, k);
            }
        }
    }

    private void CheckConstructorInitializer(ClassSymbol symbol, ConstructorSymbol k)
    {
        var d = k.Declaration;
        frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = d.Line };
        foreach (var (p, node) in k.Parameters.Zip(d.Parameters))
        {
            if (node.Default is not null)
            {
                CheckDefault(node.Default, p.Type, node.Line); // F12, D11: a constant, as a function's parameter's default is
            }
        }

        if (d.ThisArguments is null && symbol.Declaration.PrimaryConstructor is not null)
        {
            Report(ErrorIds.ConstructorMustChain, d.Line, $"a further constructor of `{symbol.Name}`, which has a primary constructor, calls it first, with `: this(...)` (K14)");
            return;
        }

        if (d.BaseArguments is not null && symbol.Base is null)
        {
            Report(ErrorIds.Syntax, d.Line, $"`: base(...)` in a constructor of `{symbol.Name}`, which has no base class (K14)");
            return;
        }

        if (d.ThisArguments is null && (symbol.Base is not { } b || baseArgumentsRefused.Contains(symbol) || (d.BaseArguments is null && !RunsAConstructor(b))))
        {
            return;
        }

        // the arguments read the parameters while no instance exists yet, a place of `static-mismatch` (§4, the owner, 2026-11-09, #429,
        // comment 6752598091), where the class's `static` members are read as in its other places *(assumed)*
        frame = new Frame
        {
            ReturnType = Types.Void, Line = d.Line, Reserved = reservedTopLevel, Class = symbol, Static = true,
            Chain = d.ThisArguments is not null ? ": this(...)" : ": base(...)",
        };
        foreach (var (p, node) in k.Parameters.Zip(d.Parameters))
        {
            k.InitializerParameters.Add(DeclareParameter(p.Name, p.Type, p.Mut, p.Line, node));
        }

        if (d.ThisArguments is not null)
        {
            // the call of the primary constructor is direct (K14); in a class without one, the call of another constructor of the body
            k.ThisCall = new CallExpression(d.Line, new NameExpression(d.Line, symbol.Name), [], d.ThisArguments);
            CheckClassConstruction(symbol, k.ThisCall, null, insideOf: symbol, primaryOnly: symbol.Declaration.PrimaryConstructor is not null);
            return;
        }

        // `: base(...)`, as C# writes it (the owner, 2026-11-03), or C#'s implied `base()`: a construction of the base, whose
        // constructors without `pub` this class does not see (F9)
        var named = symbol.Base!;
        if (d.BaseArguments is null && !TakesNoArguments(named))
        {
            Report(ErrorIds.ArgumentMismatch, d.Line, $"this constructor of `{symbol.Name}` runs the constructor of its base `{named.Name}` that takes no arguments, as C#'s implied `base()`, and `{named.Name}` has none: pass the arguments with `: base(...)` (K14)");
            return;
        }

        k.BaseCall = new CallExpression(d.Line, new NameExpression(d.Line, named.Name), [], d.BaseArguments ?? []);
        CheckClassConstruction(named, k.BaseCall, null);
    }

    /// <summary>K14: <c>: this(...)</c> calls that come back to the constructor they start from, reported once, at the first of the ring.</summary>
    private void RefuseConstructorCycles(ClassSymbol symbol)
    {
        foreach (var k in symbol.Constructors)
        {
            var ring = new List<ConstructorSymbol>();
            for (var next = Chained(k); next is not null && !ring.Contains(next); next = Chained(next))
            {
                ring.Add(next);
            }

            if (ring.Contains(k) && ring.All(other => other.Line >= k.Line))
            {
                NotSupported(k.Line, $"`: this(...)` calls that come back to this constructor of `{symbol.Name}` through {ring.Count} {(ring.Count == 1 ? "call" : "calls")}, which would never end (K14)");
            }
        }
    }

    /// <summary>The constructor of the body a constructor's <c>: this(...)</c> calls; null for the primary one and where the call was not read.</summary>
    private ConstructorSymbol? Chained(ConstructorSymbol k) => k.ThisCall is { } call && model.HasCall(call) ? model.CallOf(call).Constructor : null;

    private void CheckConstructorBody(ClassSymbol symbol, ConstructorSymbol k)
    {
        var d = k.Declaration;
        var building = new Building(k);
        if (d.ThisArguments is null && AssignsInBody(symbol))
        {
            // a constructor that calls another leaves the fields to it
            foreach (var field in symbol.Fields.Where(f => f.Body is { Value: null }))
            {
                building.Marks.Add((field, Mark($"{field.Name} assigned"), Mark($"{field.Name} unassigned")));
            }
        }

        frame = new Frame { ReturnType = Types.Void, Line = d.Line, Reserved = reservedTopLevel, Class = symbol, Building = building };
        frame.Known = building.Marks.Select(m => m.Unassigned).ToImmutableHashSet();
        var receiver = new VariableSymbol("this", new ClassType(symbol), false, VariableKind.Parameter, d.Line) { Read = true };
        frame.Scope.Variables[receiver.Name] = receiver;
        model.RecordThis(k, receiver);
        foreach (var (p, node) in k.Parameters.Zip(d.Parameters))
        {
            DeclareParameter(p.Name, p.Type, p.Mut, p.Line, node);
        }

        var ends = building.Returns;
        if (CheckBlock(d.Body) == Flow.Completes)
        {
            ends.Add(frame.Known);
        }

        foreach (var (field, assigned, _) in building.Marks)
        {
            if (ends.Any(known => !known.Contains(assigned)))
            {
                Report(ErrorIds.FieldUnassigned, d.Line, $"a path through this constructor of `{symbol.Name}` ends with `{field.Name}` unassigned: a field without `=` takes its value from every constructor that calls no other (K14)");
            }
        }

        ReportUnused(frame.Scope);
    }

    /// <summary>A mark of the flow of a constructor's body, a path of no field from a variable of its own, which no change of a place ends (N9).</summary>
    private static NullPath Mark(string name) => new(new VariableSymbol(name, Types.Bool, false, VariableKind.Local, 0), string.Empty);

    /// <summary>The frame of the constructor whose body holds the code being read, through the lambdas around it; null outside one.</summary>
    private Frame? Constructing()
    {
        for (var f = frame; f is not null; f = f.Outer)
        {
            if (f.Class is not null)
            {
                return f.Building is null ? null : f;
            }
        }

        return null;
    }

    /// <summary>
    /// K14: a read of a field before its first assignment: in a constructor's body, where a path to the read leaves it unassigned,
    /// <c>field-unassigned</c>; in a lambda there, which can run while it is, <c>not supported</c>; in a field's default, which runs
    /// before the body of every constructor (K7), of a field a constructor's body assigns, <c>not supported</c>, since no rule says what
    /// that read is.
    /// </summary>
    private void ReadInConstruction(FieldSymbol field, int line)
    {
        if (Initializing() is { } being && field.Body is { Value: null } && CurrentClass() is { } c && AssignsInBody(c))
        {
            NotSupported(line, $"a default of `{being.Name}` that reads `{field.Name}`, a field a constructor's body assigns after every default has run (K7, K14)");
            return;
        }

        if (Constructing() is not { } around || around.Building!.MarksOf(field) is not { } marks || frame.Known.Contains(marks.Assigned))
        {
            return;
        }

        if (ReferenceEquals(around, frame))
        {
            Report(ErrorIds.FieldUnassigned, line, $"`{field.Name}` is read before this constructor assigns it on every path (K14)");
        }
        else
        {
            NotSupported(line, $"a lambda that reads `{field.Name}` before the constructor assigns it on every path (K14, F15)");
        }
    }

    /// <summary>The marks of a field a constructor's body assigns, where the code being read stands in that body or in a lambda there; null elsewhere.</summary>
    private (NullPath Assigned, NullPath Unassigned)? MarksOf(FieldSymbol field) => Constructing()?.Building!.MarksOf(field);

    /// <summary>
    /// K14: an assignment of a field by its name in a constructor's body, after its value was read: the first one on every path takes
    /// the mark of the field, one that a path to it follows an earlier one is <c>assign-immutable</c> without <c>mut</c>, and with
    /// <c>mut</c> <c>not supported</c> where another path has none. A lambda there assigns as anywhere else, a field without
    /// <c>mut</c> never, and a <c>mut</c> one only where the constructor assigned it on every path to the lambda, since it can run at
    /// any time after.
    /// </summary>
    private void AssignInConstruction(AssignOrDeclare a, FieldSymbol field, (NullPath Assigned, NullPath Unassigned) marks)
    {
        if (!ReferenceEquals(Constructing(), frame))
        {
            if (field.Mut && !frame.Known.Contains(marks.Assigned))
            {
                NotSupported(a.Line, $"a lambda that assigns `{field.Name}` before the constructor assigns it on every path (K14, F15)");
            }

            return;
        }

        if (frame.Known.Contains(marks.Unassigned))
        {
            model.RecordFieldInitialization(a, true);
            frame.Known = frame.Known.Remove(marks.Unassigned).Add(marks.Assigned);
            return;
        }

        model.RecordFieldInitialization(a, false);
        if (!field.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{field.Name}` is a field without `mut`, which a constructor assigns once, and a path to this assignment assigns it already (K14)");
        }
        else if (!frame.Known.Contains(marks.Assigned))
        {
            NotSupported(a.Line, $"an assignment of the `mut` field `{field.Name}` that one path to it reaches with the field assigned and another without (K14)");
        }

        frame.Known = frame.Known.Add(marks.Assigned); // assigned from here on, so that its paths carry no second error (E1)
    }

    /// <summary>
    /// K14: whether the instance a constructor builds can be reached where the code stands. Before the constructor assigns every
    /// field, a use of the instance would reach a field with no value (Swift's rule); in a class with derived classes, <c>this</c>
    /// and a method of the instance could reach an override that reads a field of the derived class while the constructor runs.
    /// Both are <c>not supported</c>.
    /// </summary>
    private bool InstanceUsable(string what, int line)
    {
        if (Constructing() is not { } around)
        {
            return true;
        }

        var building = around.Building!;
        if (building.Constructor.Owner.Derived)
        {
            NotSupported(line, $"{what} in a constructor of `{building.Constructor.Owner.Name}`, a class another class derives from, where an override could read a field of the derived class while the constructor runs (K6, K14)");
            return false;
        }

        foreach (var (field, assigned, _) in building.Marks)
        {
            if (!frame.Known.Contains(assigned))
            {
                NotSupported(line, $"{what} before the constructor assigns `{field.Name}` on every path, which would reach a field with no value (K14)");
                return false;
            }
        }

        return true;
    }
}
