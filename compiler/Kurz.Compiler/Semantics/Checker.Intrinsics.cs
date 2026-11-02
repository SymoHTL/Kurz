using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// R18: the intrinsics, the classes of instruction sets under <c>X86</c> and <c>Arm</c> with their <c>IsSupported</c>, and the vector
/// types <c>Vector128&lt;T&gt;</c> and <c>Vector256&lt;T&gt;</c> with their lane operations, typed from <see cref="Intrinsics"/>, under
/// the names <c>X86</c>, <c>Arm</c>, <c>Vector128</c> and <c>Vector256</c>, which no declaration of a file takes (section 13, the owner,
/// 2026-11-01). A test of a class's <c>IsSupported</c> is kept among what N3's flow knows, as a stand-in variable of the class known "not
/// null", so that a
/// test guards an intrinsic where it would narrow a nullable variable, with every form N3 reads: the block of a test, the right side of
/// <c>&amp;&amp;</c>, <c>!</c> turning a test around, and the rest of a block after a test whose block always leaves (section 13, "Checker
/// narrows"). An intrinsic that no test of its class, or of one C# derives from it, guards, outside a <c>raw</c> block, is
/// <c>intrinsic-unguarded</c>.
/// </summary>
public sealed partial class Checker
{
    // R18: the stand-in variable of each class, which N3's paths hold where a test of the class is known true, and back
    private Dictionary<string, VariableSymbol>? testedVariables;
    private Dictionary<VariableSymbol, IntrinsicClass>? testedClasses;

    // R18: the tests of a class's `IsSupported` read so far, which `Tests` turns into what a branch knows
    private readonly Dictionary<MemberAccess, IntrinsicClass> supportTests = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The names under which the intrinsics and the vector types stand, which a declaration of the file cannot take (<c>redeclared</c>,
    /// <see cref="IsBuiltInName"/>), where no variable refused so gives them, whose reads report nothing more (E1).
    /// </summary>
    private bool IsIntrinsicName(string name) =>
        name is "X86" or "Arm" or "Vector128" or "Vector256" && Lookup(name).Variable is null && !(frame.Reserved?.Contains(name) ?? false);

    /// <summary>The path of N3 a test of the class adds where it is true.</summary>
    private NullPath TestedPath(IntrinsicClass c)
    {
        if (testedVariables is null)
        {
            testedVariables = Intrinsics.Classes.Values.ToDictionary(x => x.Name, x => new VariableSymbol(x.Name, Types.Bool, false, VariableKind.Local, 0), StringComparer.Ordinal);
            testedClasses = new Dictionary<VariableSymbol, IntrinsicClass>(testedVariables.Select(p => KeyValuePair.Create(p.Value, Intrinsics.Classes[p.Key])), ReferenceEqualityComparer.Instance);
        }

        return new NullPath(testedVariables[c.Name], string.Empty);
    }

    /// <summary>The classes whose tests the flow of the code knows true where the checker reads.</summary>
    private List<IntrinsicClass> KnownClasses(ImmutableHashSet<NullPath> known) =>
        testedClasses is null ? [] : known.Where(p => p.Fields.Length == 0 && testedClasses.ContainsKey(p.Root)).Select(p => testedClasses[p.Root]).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

    /// <summary>R18, for the emitter: the classes known tested where the statement or the right side of <c>&amp;&amp;</c> or <c>||</c> starts; the last reading stands.</summary>
    private void RecordGuards(Node node) => model.RecordGuards(node, KnownClasses(frame.Known));

    /// <summary>N3 with R18: what a test of a class's <c>IsSupported</c> shows where it is true, null for any other condition.</summary>
    private ImmutableHashSet<NullPath>? ClassTested(Expression condition) =>
        Unwrap(condition) is MemberAccess m && supportTests.TryGetValue(m, out var c) ? [TestedPath(c)] : null;

    /// <summary>
    /// <c>X86.Sse41</c>: whether the expression names a class, with the class, or null for a name under <c>X86</c> or <c>Arm</c> that is
    /// no class of C#'s; false for any other expression.
    /// </summary>
    private bool NamesClass(Expression e, out string path, out IntrinsicClass? c)
    {
        if (Unwrap(e) is MemberAccess { Conditional: false, Target: NameExpression n } m && n.Name is "X86" or "Arm" && IsIntrinsicName(n.Name))
        {
            path = $"{n.Name}.{m.Member}";
            c = Intrinsics.ClassOf(path);
            return true;
        }

        path = string.Empty;
        c = null;
        return false;
    }

    /// <summary>The class a path names, or the error: a name that is no class of C#'s, or one whose intrinsics the compiler does not build.</summary>
    private IntrinsicClass? BuiltClass(string path, IntrinsicClass? c, int line)
    {
        if (c is null)
        {
            Report(ErrorIds.UnknownName, line, $"`{path[..path.IndexOf('.', StringComparison.Ordinal)]}` has no class `{path[(path.IndexOf('.', StringComparison.Ordinal) + 1)..]}` (R18, V10)");
            return null;
        }

        if (!c.Built)
        {
            NotSupported(line, $"`{c.Name}`, a class of C#'s whose intrinsics the compiler does not build (R18)");
            return null;
        }

        return c;
    }

    /// <summary>R18: <c>Vector128&lt;T&gt;</c> or <c>Vector256&lt;T&gt;</c> as a type, its lane one of C#'s ten, or null where the name is no vector's.</summary>
    private KurzType? ResolveVector(NamedType n)
    {
        if (n.Name is not ("Vector128" or "Vector256"))
        {
            return null;
        }

        if (n.TypeArguments.Count != 1)
        {
            Report(n.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, n.Line, $"`{n.Name}` takes one type argument, the type of its lanes (R18, T17)");
            return Types.Error;
        }

        var lane = Resolve(n.TypeArguments[0]);
        if (lane is ErrorType)
        {
            return Types.Error;
        }

        if (lane is TypeParameterType)
        {
            NotSupported(n.Line, $"a vector whose lanes are a type parameter, `{n.Name}<{lane}>` (R18, T19)");
            return Types.Error;
        }

        if (!Types.IsLane(lane))
        {
            Report(ErrorIds.TypeMismatch, n.Line, $"the lanes of `{n.Name}` are a number type but `decimal`, as C#'s are, not `{lane}` (R18, T19)");
            return Types.Error;
        }

        return new VectorType(lane, n.Name == "Vector128" ? 128 : 256);
    }

    /// <summary>R18: <c>X86</c>, <c>Arm</c>, <c>Vector128</c> or <c>Vector256</c> read as a value, which none of them is.</summary>
    private bool IntrinsicNameAsValue(NameExpression n)
    {
        if (!IsIntrinsicName(n.Name))
        {
            return false;
        }

        Report(ErrorIds.UnknownName, n.Line, n.Name is "X86" or "Arm"
            ? $"`{n.Name}` holds classes of intrinsics and is no value: a class's test is `{n.Name}.<class>.IsSupported` (R18, V10)"
            : $"`{n.Name}` is a type and no value: a vector is made by `{n.Name}.Create` (R18, V10)");
        return true;
    }

    /// <summary>R18: a member read as a value from a class, <c>X86.Sse41.IsSupported</c>, a <c>bool</c>; null where the target names no class.</summary>
    private Typed? CheckIntrinsicMember(MemberAccess m)
    {
        if (NamesClass(m.Target, out var path, out var named))
        {
            if (BuiltClass(path, named, m.Target.Line) is not { } c)
            {
                return Typed.Error;
            }

            if (m.Member != "IsSupported")
            {
                Report(ErrorIds.UnknownName, m.Line, Intrinsics.Functions.ContainsKey((c.Name, m.Member))
                    ? $"`{c.Name}.{m.Member}` is a function, which is called (R18, V10)"
                    : $"`{c.Name}` has no value `{m.Member}`: its test is `IsSupported` (R18, V10)");
                return Typed.Error;
            }

            supportTests[m] = c;
            model.RecordSupportTest(m, c);
            return new Typed(Types.Bool);
        }

        if (m.Target is NameExpression n && IsIntrinsicName(n.Name))
        {
            Report(ErrorIds.UnknownName, m.Line, n.Name is "X86" or "Arm"
                ? $"`{n.Name}.{m.Member}` is a class of intrinsics and no value (R18, V10)"
                : $"`{n.Name}.{m.Member}` is a function, which is called (R18, V10)");
            return Typed.Error;
        }

        return null;
    }

    /// <summary>R18: a call of a class's intrinsic, <c>X86.Sse41.Extract(v, 2)</c>, or of <c>Vector128.Create</c>; null where the callee is neither.</summary>
    private Typed? CheckIntrinsicCall(MemberAccess m, CallExpression c)
    {
        string owner;
        IntrinsicClass? cls = null;
        if (m.Target is NameExpression n && n.Name is "Vector128" or "Vector256" && IsIntrinsicName(n.Name))
        {
            owner = n.Name;
        }
        else if (NamesClass(m.Target, out var path, out var named))
        {
            cls = BuiltClass(path, named, m.Target.Line);
            if (cls is null)
            {
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            owner = cls.Name;
        }
        else
        {
            return null;
        }

        if (!Intrinsics.Functions.TryGetValue((owner, m.Member), out var list))
        {
            if (m.Member == "IsSupported" && cls is not null)
            {
                Report(ErrorIds.TypeMismatch, m.Line, $"`{owner}.IsSupported` is a `bool` and no function (R18, F11)");
            }
            else
            {
                NotSupported(m.Line, $"`{owner}.{m.Member}`, which the compiler does not build (R18)");
            }

            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return RefusesTypeArguments(c, $"`{owner}.{m.Member}`") ? Typed.Error : CallIntrinsic(list, c, cls, null);
    }

    /// <summary>The <c>not supported</c> of a vector's text: C# writes its lanes, <c>&lt;1, 2, 3, 4&gt;</c>, and no rule gives Kurz's (A4, R18).</summary>
    private const string VectorText = "the text of a vector, which C# writes as its lanes and no rule gives (A4, R18)";

    /// <summary>Whether a value of the type is a vector or holds one where a text would show it: an item, a value, a case or a field of its own.</summary>
    private static bool HoldsVector(KurzType type) => type switch
    {
        VectorType => true,
        ListType l => HoldsVector(l.Element),
        MapType m => HoldsVector(m.Value),
        UnionType u => u.Cases.Any(HoldsVector),
        DataType d => d.Symbol.Fields.Any(f => f.Type is VectorType),
        _ => false,
    };

    /// <summary>R18: a lane operation called on a vector, <c>v.GetElement(2)</c>, <c>v.AsByte()</c>.</summary>
    private Typed CheckVectorMethod(VectorType vector, MemberAccess m, CallExpression c)
    {
        var list = Intrinsics.Methods(vector, m.Member);
        if (list.Count == 0)
        {
            NotSupported(m.Line, $"`{m.Member}` of {Types.A(vector)}, which the compiler does not build (R18)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return RefusesTypeArguments(c, $"`{m.Member}`") ? Typed.Error : CallIntrinsic(list, c, null, vector);
    }

    /// <summary>
    /// Picks the intrinsic of the overloads that the arguments fit (F13), holds each immediate to a constant, which C# compiles with a
    /// slower path the compiler does not build where it is none, and an intrinsic of a class to a test that guards it outside <c>raw</c>.
    /// An immediate takes an integer constant that fits its type, as C#'s <c>[ConstantExpected] byte</c> takes the <c>2</c> of
    /// <c>X86.Sse41.Extract(v, 2)</c>, the form R18's cases write: for the pick it is an <c>int</c>, so that the literal's own type fits
    /// it (F14), and a value outside its type is <c>constant-overflow</c> (L10).
    /// </summary>
    private Typed CallIntrinsic(IReadOnlyList<Intrinsic> list, CallExpression c, IntrinsicClass? cls, VectorType? receiver)
    {
        var candidates = list.Select(i => new Candidate
        {
            Slots = i.Parameters.Select(p => new Slot(p.Name, p.Immediate ? Types.Int : p.Type, false, false, false)).ToList(),
            Result = i.Result,
            Form = i.Form,
        }).ToList();
        var typed = Call(candidates, c, null, out var picked);
        if (picked is null || typed.IsError)
        {
            return typed;
        }

        var intrinsic = list[candidates.IndexOf(picked)];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var parameter = intrinsic.Parameters[picked.Binding[i]];
            if (!parameter.Immediate)
            {
                continue;
            }

            if (model.TypeOf(c.Arguments[i].Value).Constant is not IntegerConstant immediate)
            {
                NotSupported(c.Arguments[i].Line, $"`{parameter.Name}` of `{intrinsic.Owner}.{intrinsic.Name}` that is no constant: C# compiles it with a slower path, which the compiler does not build (R18)");
                return Typed.Error;
            }

            if (!Types.Fits(immediate.Value, parameter.Type))
            {
                Report(ErrorIds.ConstantOverflow, c.Arguments[i].Line, $"{immediate.Value} does not fit {Types.A(parameter.Type)}, the type of `{parameter.Name}` of `{intrinsic.Owner}.{intrinsic.Name}` (L10, R18)");
                return Typed.Error;
            }
        }

        if (cls is not null && rawDepth == 0 && !KnownClasses(frame.Known).Any(k => Intrinsics.Guards(k, cls)))
        {
            Report(ErrorIds.IntrinsicUnguarded, c.Line, $"`{cls.Name}.{intrinsic.Name}` stands where no test of `{cls.Name}.IsSupported` guards it (R18)");
        }

        model.RecordCall(c, new CallInfo(CallKind.Intrinsic, null, null, receiver, NoMap, picked.Binding, picked.Slots.Count, intrinsic.Result) { Intrinsic = intrinsic });
        return typed;
    }
}
