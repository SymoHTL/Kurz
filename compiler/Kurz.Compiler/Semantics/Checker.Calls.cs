using System.Numerics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    /// <summary>A parameter as a call binds it: of a function (F12), a primary constructor (D1) or a function value (F11).</summary>
    private sealed record Slot(string Name, KurzType Type, bool Mut, bool HasDefault, bool GenericPosition);

    /// <summary>What an argument is before a candidate is picked: its own type, or that it is a lambda to be typed by the parameter it lands on (F13, F14, T27).</summary>
    private sealed class Pre
    {
        public Argument Argument { get; init; } = null!;

        public Typed Typed { get; set; }

        public List<Diagnostic> Diagnostics { get; set; } = [];

        public bool LiteralOnly { get; init; }

        public LambdaExpression? Lambda { get; init; }
    }

    private sealed class Candidate
    {
        public FunctionSymbol? Function { get; init; }

        public List<Slot> Slots { get; init; } = [];

        public List<TypeParameterSymbol> TypeParameters { get; init; } = [];

        /// <summary>The slot each argument lands on, after binding (D11).</summary>
        public int[] Binding { get; set; } = [];

        public Dictionary<TypeParameterSymbol, KurzType> Map { get; set; } = [];

        public bool DefaultUsed { get; set; }

        public string? BindingError { get; set; }

        public bool CannotInfer { get; set; }

        /// <summary>The first argument whose own type does not fit its parameter, with the parameter's type after substitution.</summary>
        public (int Argument, KurzType Parameter)? Misfit { get; set; }

        public bool Fits => BindingError is null && !CannotInfer && Misfit is null;

        public bool Exact { get; set; }

        public KurzType Result { get; set; } = Types.Error;
    }

    private Typed CheckCall(CallExpression c, KurzType? expected, bool keepCases)
    {
        var result = CallResult(c, expected);
        if (keepCases || result.IsError || result.Type is not UnionType u)
        {
            return result;
        }

        // O2: the success case is the value; every other case leaves the calling function (O3, O9)
        Propagate(u.Cases.Skip(1), c.Line);
        return new Typed(u.Cases[0]);
    }

    private Typed CallResult(CallExpression c, KurzType? expected)
    {
        switch (c.Callee)
        {
            case NameExpression n:
                {
                    var (variable, _) = Lookup(n.Name);
                    if (variable is not null)
                    {
                        variable.Read = true;
                        return CallValue(variable.Type, c);
                    }

                    if (frame.Reserved is not null && frame.Reserved.Contains(n.Name))
                    {
                        CheckName(n);
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    if (n.Name == "print")
                    {
                        return CheckPrint(c);
                    }

                    if (Types.ByName.TryGetValue(n.Name, out var primitive))
                    {
                        return CheckConversion(primitive, c);
                    }

                    if (n.Name == "List")
                    {
                        return CheckListConstruction(c);
                    }

                    if (n.Name == "Map")
                    {
                        NotSupported(c.Line, "Map (M9)");
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    if (functions.TryGetValue(n.Name, out var candidates))
                    {
                        return ResolveCall(candidates, c, expected);
                    }

                    if (dataTypes.TryGetValue(n.Name, out var data))
                    {
                        return CheckConstruction(data, c, expected);
                    }

                    if (unsupportedTypes.Contains(n.Name))
                    {
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    Report(ErrorIds.UnknownName, n.Line, $"no `{n.Name}` is visible here (V10)");
                    CheckArgumentsLoosely(c);
                    return Typed.Error;
                }

            case MemberAccess m:
                return CheckMethodCall(m, c);
            default:
                {
                    var callee = CheckExpression(c.Callee, null);
                    return CallValue(callee.Type, c);
                }
        }
    }

    /// <summary>The arguments checked for their own defects, where the call itself is already wrong.</summary>
    private void CheckArgumentsLoosely(CallExpression c)
    {
        foreach (var a in c.Arguments)
        {
            if (a.Value is LambdaExpression l && l.Parameters.Any(p => p.Type is null))
            {
                continue; // nothing gives its parameters a type: its own error would stand beside the call's
            }

            CheckExpression(a.Value, null);
        }
    }

    /// <summary>F11: a call of a function value, by position.</summary>
    private Typed CallValue(KurzType type, CallExpression c)
    {
        if (Types.IsError(type))
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (type is not FunctionType f)
        {
            Report(ErrorIds.TypeMismatch, c.Line, $"a `{type}` is no function and cannot be called (F11)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var slots = f.Parameters.Select((p, i) => new Slot($"#{i}", p, false, false, false)).ToList();
        var candidate = new Candidate { Slots = slots, Result = f.Result };
        return Call([candidate], c, null);
    }

    private Typed CheckPrint(CallExpression c)
    {
        if (c.Arguments.Count != 1 || c.Arguments[0].Name is not null)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, "`print` takes one value (A1)");
            CheckArgumentsLoosely(c);
            return new Typed(Types.Void);
        }

        var argument = c.Arguments[0];
        if (argument.Mut)
        {
            Report(ErrorIds.MutAtCall, argument.Line, "`print` changes nothing: no `mut` before its argument (M7)");
        }

        var t = CheckExpression(argument.Value, null);
        if (Types.IsVoid(t.Type))
        {
            Report(ErrorIds.Syntax, c.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!t.IsError && !HasText(t.Type))
        {
            Report(ErrorIds.NoText, c.Line, $"a `{t.Type}` has no text to print (A4, A7, A10)");
        }

        return new Typed(Types.Void);
    }

    /// <summary>T10: a conversion written as a call of a number type or a time type; the constant folded as T6 and T26 say.</summary>
    private Typed CheckConversion(PrimitiveType target, CallExpression c)
    {
        if (!Types.IsNumber(target) && !Types.IsTime(target))
        {
            NotSupported(c.Line, $"a conversion to `{target}`");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments.Count != 1 || c.Arguments[0].Name is not null || c.Arguments[0].Mut)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, $"a conversion to `{target}` takes one value (T10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var argument = c.Arguments[0].Value;
        Typed source;
        if (IsLiteralOnly(argument))
        {
            // L17: the conversion reaches the integer literals inside it; a fraction or exponent literal keeps its own type
            realKeepsOwnType = true;
            source = CheckExpression(argument, Types.IsInteger(target) ? target : Types.IsTime(target) ? target : null);
            realKeepsOwnType = false;
        }
        else
        {
            source = CheckExpression(argument, null);
        }

        if (source.IsError)
        {
            return Typed.Error;
        }

        if (Types.IsNumber(source.Type) && Types.IsNumber(target))
        {
            return ConvertNumber(source, target, c.Line);
        }

        if (Types.IsTime(source.Type) && Types.IsTime(target))
        {
            if (Types.IsDuration(source.Type) != Types.IsDuration(target))
            {
                Report(ErrorIds.TypeMismatch, c.Line, $"a `{source.Type}` is no `{target}` (T13)");
                return Typed.Error;
            }

            if (source.Constant is IntegerConstant k && !Types.Fits(k.Value, target))
            {
                Report(ErrorIds.ConstantOverflow, c.Line, $"the constant does not fit a `{target}` (T6, T29)");
                return Typed.Error;
            }

            return new Typed(target, source.Constant);
        }

        Report(ErrorIds.TypeMismatch, c.Line, $"a `{source.Type}` is not converted to a `{target}` (T10, T13)");
        return Typed.Error;
    }

    private Typed ConvertNumber(Typed source, PrimitiveType target, int line)
    {
        switch (source.Constant)
        {
            case IntegerConstant k:
                if (Types.IsInteger(target))
                {
                    if (!Types.Fits(k.Value, target))
                    {
                        Report(ErrorIds.ConstantOverflow, line, $"{k.Value} does not fit a `{target}` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, k);
                }

                if (Types.IsDecimal(target))
                {
                    if (BigInteger.Abs(k.Value) > DecimalMax)
                    {
                        Report(ErrorIds.ConstantOverflow, line, $"{k.Value} does not fit a `decimal` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, new DecimalConstant((decimal)k.Value));
                }

                return new Typed(target, new RealConstant(target.Kind == Primitive.Float ? (float)(double)k.Value : (double)k.Value));
            case RealConstant r:
                if (Types.IsInteger(target))
                {
                    if (double.IsNaN(r.Value) || double.IsInfinity(r.Value))
                    {
                        Report(ErrorIds.ConstantOverflow, line, $"`{r.Value}` fits no integer type (T6)");
                        return Typed.Error;
                    }

                    var truncated = new BigInteger(Math.Truncate(r.Value));
                    if (!Types.Fits(truncated, target))
                    {
                        Report(ErrorIds.ConstantOverflow, line, $"{truncated} does not fit a `{target}` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, new IntegerConstant(truncated));
                }

                if (Types.IsDecimal(target))
                {
                    if (double.IsNaN(r.Value) || double.IsInfinity(r.Value) || Math.Abs(r.Value) > (double)decimal.MaxValue)
                    {
                        Report(ErrorIds.ConstantOverflow, line, "the value does not fit a `decimal` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, new DecimalConstant((decimal)r.Value));
                }

                return new Typed(target, new RealConstant(target.Kind == Primitive.Float ? (float)r.Value : r.Value));
            case DecimalConstant m:
                if (Types.IsInteger(target))
                {
                    var truncated = new BigInteger(decimal.Truncate(m.Value));
                    if (!Types.Fits(truncated, target))
                    {
                        Report(ErrorIds.ConstantOverflow, line, $"{truncated} does not fit a `{target}` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, new IntegerConstant(truncated));
                }

                return Types.IsDecimal(target) ? new Typed(target, m) : new Typed(target, new RealConstant(target.Kind == Primitive.Float ? (float)m.Value : (double)m.Value));
            default:
                return new Typed(target);
        }
    }

    /// <summary>M9: <c>List&lt;T&gt;()</c> makes an empty list.</summary>
    private Typed CheckListConstruction(CallExpression c)
    {
        if (c.TypeArguments.Count != 1)
        {
            Report(c.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, c.Line, "`List` takes one type argument (M9, T17)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments.Count != 0)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, "`List<T>()` takes no arguments (M9)");
            CheckArgumentsLoosely(c);
        }

        var element = Resolve(c.TypeArguments[0]);
        return Types.IsError(element) ? Typed.Error : new Typed(new ListType(element));
    }

    /// <summary>D2, D7, D11: a data value made by the type's name with the base's fields first.</summary>
    private Typed CheckConstruction(DataSymbol data, CallExpression c, KurzType? expected)
    {
        if (!data.HasFields)
        {
            if (c.Arguments.Count != 0)
            {
                Report(ErrorIds.ArgumentMismatch, c.Line, $"`{data.Name}` has no fields: its value is the bare name (D8)");
                CheckArgumentsLoosely(c);
            }

            return new Typed(new DataType(data, []), new UnitConstant());
        }

        var slots = data.Fields.Select(f => new Slot(f.Name, f.Type, false, f.HasDefault, data.TypeParameters.Any(p => Types.Mentions(f.Type, p)))).ToList();
        var candidate = new Candidate { Slots = slots, TypeParameters = data.TypeParameters, Result = Types.Void }; // the result is built below from the type arguments
        if (c.TypeArguments.Count > 0)
        {
            if (c.TypeArguments.Count != data.TypeParameters.Count)
            {
                Report(ErrorIds.Syntax, c.Line, $"`{data.Name}` takes {data.TypeParameters.Count} type arguments (T19)");
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            candidate.Map = data.TypeParameters.Zip(c.TypeArguments.Select(Resolve)).ToDictionary(p => p.First, p => p.Second);
        }

        var typed = Call([candidate], c, expected);
        if (typed.IsError)
        {
            return typed;
        }

        var arguments = data.TypeParameters.Select(p => candidate.Map.TryGetValue(p, out var t) ? t : Types.Error).ToList();
        return new Typed(arguments.Any(Types.IsError) ? Types.Error : new DataType(data, arguments));
    }

    /// <summary>M5, M9, T15: a call of a member: a list's <c>Add</c> and <c>Where</c>; a data value's field of function type.</summary>
    private Typed CheckMethodCall(MemberAccess m, CallExpression c)
    {
        if (m.Conditional)
        {
            NotSupported(m.Line, "`?.` (N5)");
            CheckExpression(m.Target, null);
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (m.Target is NameExpression n && Lookup(n.Name).Variable is null && !(frame.Reserved?.Contains(n.Name) ?? false))
        {
            if (unsupportedTypes.Contains(n.Name))
            {
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            if ((dataTypes.TryGetValue(n.Name, out var data) && data.HasFields) || Types.ByName.ContainsKey(n.Name) || n.Name is "List" or "Map" || functions.ContainsKey(n.Name))
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` has no member `{m.Member}`");
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }
        }

        var target = CheckExpression(m.Target, null);
        if (target.IsError)
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (target.Type is ListType list)
        {
            switch (m.Member)
            {
                case "Add":
                    {
                        var candidate = new Candidate { Slots = [new Slot("item", list.Element, false, false, false)], Result = Types.Void };
                        var result = Call([candidate], c, null);
                        MutReceiver(m.Target, c.Line, "`Add` changes the list it is called on");
                        return result;
                    }

                case "Where":
                    {
                        var test = new FunctionType([list.Element], Types.Bool);
                        var candidate = new Candidate { Slots = [new Slot("test", test, false, false, false)], Result = list };
                        return Call([candidate], c, null);
                    }

                default:
                    break;
            }
        }

        var member = MemberOf(target.Type, m.Member, m.Line);
        if (member.IsError)
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return CallValue(member.Type, c);
    }

    /// <summary>M5: the receiver of a <c>mut</c> method is a <c>mut</c> variable or a path of data fields from one; in a lambda, such a variable is not changed (F11).</summary>
    private void MutReceiver(Expression receiver, int line, string what)
    {
        var root = receiver;
        while (true)
        {
            if (root is ParenthesizedExpression p)
            {
                root = p.Inner;
            }
            else if (root is MemberAccess { Conditional: false } m)
            {
                root = m.Target;
            }
            else
            {
                break;
            }
        }

        if (root is not NameExpression name)
        {
            Report(ErrorIds.MutRequired, line, $"{what}, and only a value reachable through a `mut` variable changes (M5)");
            return;
        }

        var (variable, captured) = Lookup(name.Name);
        if (variable is null || Types.IsError(variable.Type))
        {
            return; // reported where the receiver was read
        }

        if (captured)
        {
            Report(ErrorIds.CaptureAssign, line, $"a lambda reads `{name.Name}` and cannot change it (F11)");
        }
        else if (!variable.Mut)
        {
            Report(ErrorIds.MutRequired, line, $"{what}, and `{name.Name}` was declared without `mut` (M5)");
        }

        variable.Changed = true;
    }

    // ---- overloads, binding and inference ----------------------------------------------------------

    /// <summary>F12 to F14, T27: the one function of the name a call picks.</summary>
    private Typed ResolveCall(List<FunctionSymbol> candidates, CallExpression c, KurzType? expected)
    {
        var list = new List<Candidate>();
        foreach (var f in candidates)
        {
            var slots = f.Parameters.Select(p => new Slot(p.Name, p.Type, p.Mut, p.HasDefault, f.TypeParameters.Any(t => Types.Mentions(p.Type, t)))).ToList();
            var candidate = new Candidate { Function = f, Slots = slots, TypeParameters = f.TypeParameters, Result = f.ReturnType };
            if (c.TypeArguments.Count > 0)
            {
                if (c.TypeArguments.Count != f.TypeParameters.Count)
                {
                    candidate.BindingError = $"`{f.Name}` takes {f.TypeParameters.Count} type arguments (T19)";
                }
                else
                {
                    candidate.Map = f.TypeParameters.Zip(c.TypeArguments.Select(Resolve)).ToDictionary(p => p.First, p => p.Second);
                }
            }

            list.Add(candidate);
        }

        return Call(list, c, expected);
    }

    /// <summary>Binds the arguments to each candidate, picks one (F13) and checks the arguments against it.</summary>
    private Typed Call(List<Candidate> candidates, CallExpression c, KurzType? expected)
    {
        var pres = new List<Pre>();
        foreach (var a in c.Arguments)
        {
            if (a.Value is LambdaExpression l)
            {
                pres.Add(new Pre { Argument = a, Lambda = l });
                continue;
            }

            Push();
            var typed = CheckExpression(a.Value, null);
            var captured = Pop(commit: false);
            pres.Add(new Pre { Argument = a, Typed = typed, Diagnostics = captured, LiteralOnly = IsLiteralOnly(a.Value) });
        }

        foreach (var candidate in candidates)
        {
            if (candidate.BindingError is null)
            {
                Bind(candidate, pres);
            }

            if (candidate.BindingError is null && candidate.TypeParameters.Count > 0 && candidate.Map.Count < candidate.TypeParameters.Count)
            {
                Infer(candidate, pres);
            }

            if (candidate.Fits)
            {
                Fit(candidate, pres, candidates.Count == 1);
            }
        }

        var fitting = candidates.Where(x => x.Fits).ToList();
        Candidate? picked = null;
        if (fitting.Count == 1)
        {
            picked = fitting[0];
        }
        else if (fitting.Count > 1)
        {
            var exact = fitting.Where(x => x.Exact).ToList();
            if (exact.Count == 1)
            {
                picked = exact[0];
            }
            else if (exact.Count > 1 && exact.Count(x => x.TypeParameters.Count == 0) == 1)
            {
                picked = exact.First(x => x.TypeParameters.Count == 0); // F13: the function without type parameters wins
            }
            else
            {
                Report(ErrorIds.AmbiguousCall, c.Line, "the call fits more than one function, none of them exactly (F13)");
            }
        }
        else
        {
            ReportNoFit(candidates, c, pres);
        }

        if (picked is null)
        {
            foreach (var pre in pres)
            {
                foreach (var d in pre.Diagnostics)
                {
                    Add(d);
                }

                if (pre.Lambda is not null && pre.Lambda.Parameters.All(p => p.Type is not null))
                {
                    CheckExpression(pre.Lambda, null);
                }
            }

            return Typed.Error;
        }

        Commit(picked, pres);
        var result = Types.Substitute(picked.Result, picked.Map);
        return new Typed(result);
    }

    /// <summary>D11: arguments without a name fill the parameters in order, arguments by name follow them; what does not fit is <c>argument-mismatch</c>.</summary>
    private static void Bind(Candidate candidate, List<Pre> pres)
    {
        var binding = new int[pres.Count];
        var taken = new bool[candidate.Slots.Count];
        var named = false;
        for (var i = 0; i < pres.Count; i++)
        {
            var a = pres[i].Argument;
            if (a.Name is null)
            {
                if (named)
                {
                    candidate.BindingError = "an argument without a name stands before the named ones (D11)";
                    return;
                }

                if (i >= candidate.Slots.Count)
                {
                    candidate.BindingError = $"the call passes {pres.Count} arguments to {candidate.Slots.Count} parameters (D11)";
                    return;
                }

                binding[i] = i;
                taken[i] = true;
                continue;
            }

            named = true;
            var slot = candidate.Slots.FindIndex(s => s.Name == a.Name);
            if (slot < 0)
            {
                candidate.BindingError = $"no parameter is named `{a.Name}` (D11)";
                return;
            }

            if (taken[slot])
            {
                candidate.BindingError = $"the parameter `{a.Name}` gets two arguments (D11)";
                return;
            }

            binding[i] = slot;
            taken[slot] = true;
        }

        for (var s = 0; s < candidate.Slots.Count; s++)
        {
            if (!taken[s])
            {
                if (!candidate.Slots[s].HasDefault)
                {
                    candidate.BindingError = $"the parameter `{candidate.Slots[s].Name}` gets no argument and has no default (D11)";
                    return;
                }

                candidate.DefaultUsed = true;
            }
        }

        candidate.Binding = binding;
    }

    /// <summary>T27: a type parameter takes the one type every argument in its positions has; literals take the type the others fix, or the type a constant of them has.</summary>
    private static void Infer(Candidate candidate, List<Pre> pres)
    {
        var map = new Dictionary<TypeParameterSymbol, KurzType>(candidate.Map);
        for (var i = 0; i < pres.Count; i++)
        {
            var pre = pres[i];
            if (pre.Lambda is not null || pre.LiteralOnly)
            {
                continue;
            }

            if (!Unify(candidate.Slots[candidate.Binding[i]].Type, pre.Typed.Type, map))
            {
                candidate.CannotInfer = true;
                return;
            }
        }

        foreach (var p in candidate.TypeParameters)
        {
            if (map.ContainsKey(p))
            {
                continue;
            }

            // literals alone in its positions: the type a constant expression of them has (L10, L17)
            KurzType? fixedType = null;
            for (var i = 0; i < pres.Count; i++)
            {
                var pre = pres[i];
                if (!pre.LiteralOnly || pre.Typed.IsError || candidate.Slots[candidate.Binding[i]].Type is not TypeParameterType tp || !ReferenceEquals(tp.Symbol, p))
                {
                    continue;
                }

                fixedType = fixedType is null ? pre.Typed.Type : LiteralCommon(fixedType, pre.Typed.Type);
                if (fixedType is null)
                {
                    break;
                }
            }

            if (fixedType is null)
            {
                candidate.CannotInfer = true;
                return;
            }

            map[p] = fixedType;
        }

        candidate.Map = map;
    }

    private static KurzType? LiteralCommon(KurzType a, KurzType b)
    {
        if (a is not PrimitiveType pa || b is not PrimitiveType pb)
        {
            return Types.Same(a, b) ? a : null;
        }

        if (Types.IsInteger(pa) && Types.IsInteger(pb))
        {
            return Types.CommonInteger(pa, pb);
        }

        if (Types.IsNumber(pa) && Types.IsNumber(pb))
        {
            return pa.Kind == Primitive.Double || pb.Kind == Primitive.Double ? Types.Double : Types.IsDecimal(pa) || Types.IsDecimal(pb) ? null : Types.Float;
        }

        return Types.Same(pa, pb) ? pa : null;
    }

    private static bool Unify(KurzType parameter, KurzType argument, Dictionary<TypeParameterSymbol, KurzType> map)
    {
        switch (parameter)
        {
            case TypeParameterType p:
                if (map.TryGetValue(p.Symbol, out var bound))
                {
                    if (Types.Same(bound, argument) || Types.IsError(argument) || Types.IsError(bound))
                    {
                        return true; // an argument the checker could not type fixes nothing and contradicts nothing
                    }

                    // T27: no widening between the arguments, as C# would do; where one data type is the base of the other, the base
                    if (bound is DataType && argument is DataType)
                    {
                        if (Types.Assignable(argument, bound))
                        {
                            return true;
                        }

                        if (Types.Assignable(bound, argument))
                        {
                            map[p.Symbol] = argument;
                            return true;
                        }
                    }

                    return false;
                }

                map[p.Symbol] = argument;
                return true;
            case ListType l when argument is ListType la:
                return Unify(l.Element, la.Element, map);
            case DataType d when argument is DataType da && ReferenceEquals(d.Symbol, da.Symbol):
                return d.TypeArguments.Zip(da.TypeArguments).All(pair => Unify(pair.First, pair.Second, map));
            case FunctionType f when argument is FunctionType fa && f.Parameters.Count == fa.Parameters.Count:
                return f.Parameters.Zip(fa.Parameters).All(pair => Unify(pair.First, pair.Second, map)) && Unify(f.Result, fa.Result, map);
            default:
                return true; // nothing to bind here; whether the types fit is the fit's question
        }
    }

    /// <summary>F13, F14: whether every argument fits its parameter by its own type, and whether all do exactly.</summary>
    private static void Fit(Candidate candidate, List<Pre> pres, bool only)
    {
        var exact = !candidate.DefaultUsed;
        for (var i = 0; i < pres.Count; i++)
        {
            var pre = pres[i];
            var slot = candidate.Slots[candidate.Binding[i]];
            var type = Types.Substitute(slot.Type, candidate.Map);
            if (pre.Lambda is not null)
            {
                if (type is not FunctionType f || f.Parameters.Count != pre.Lambda.Parameters.Count)
                {
                    candidate.Misfit ??= (i, type);
                }

                continue;
            }

            if (pre.Typed.IsError)
            {
                continue;
            }

            if (pre.LiteralOnly && (slot.GenericPosition || only) && Types.IsNumber(type) && Types.IsNumber(pre.Typed.Type))
            {
                // T27, L17: the literal takes the type the parameter fixes; whether it fits is checked under that type
                exact &= Types.Same(pre.Typed.Type, type);
                continue;
            }

            if (!Types.Assignable(pre.Typed.Type, type))
            {
                candidate.Misfit ??= (i, type);
            }

            exact &= Types.Same(pre.Typed.Type, type);
        }

        candidate.Exact = exact;
    }

    private void ReportNoFit(List<Candidate> candidates, CallExpression c, List<Pre> pres)
    {
        var bound = candidates.FirstOrDefault(x => x.BindingError is null);
        if (bound is null)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, candidates[0].BindingError!);
            return;
        }

        if (bound.CannotInfer)
        {
            Report(ErrorIds.CannotInfer, c.Line, "the type arguments are neither written nor inferable from the arguments (T27)");
            return;
        }

        var (index, parameter) = bound.Misfit!.Value;
        var pre = pres[index];
        var argumentType = pre.Lambda is not null ? "a lambda" : $"a `{pre.Typed.Type}`";
        var id = pre.Lambda is not null ? ErrorIds.TypeMismatch : Types.MismatchId(pre.Typed.Type, parameter);
        Report(id, pre.Argument.Line, $"{argumentType} where a `{parameter}` is required (T1)");
    }

    /// <summary>The picked candidate: literals and lambdas typed by their parameters (L17, F11), the <c>mut</c> markers checked (M7).</summary>
    private void Commit(Candidate candidate, List<Pre> pres)
    {
        var mutRoots = new List<string>();
        for (var i = 0; i < pres.Count; i++)
        {
            var pre = pres[i];
            var slot = candidate.Slots[candidate.Binding[i]];
            var type = Types.Substitute(slot.Type, candidate.Map);
            var a = pre.Argument;
            if (pre.Lambda is not null)
            {
                var typed = CheckExpression(pre.Lambda, type);
                if (!typed.IsError && !Types.IsError(type) && !Types.Assignable(typed.Type, type))
                {
                    Report(ErrorIds.TypeMismatch, a.Line, $"a `{typed.Type}` where a `{type}` is required (F11, T1)");
                }
            }
            else if (pre.LiteralOnly)
            {
                var typed = CheckExpression(a.Value, type);
                if (!typed.IsError && !Types.IsError(type) && !Types.Assignable(typed.Type, type))
                {
                    Report(Types.MismatchId(typed.Type, type), a.Line, $"a `{typed.Type}` where a `{type}` is required (T1)");
                }
            }
            else
            {
                foreach (var d in pre.Diagnostics)
                {
                    Add(d);
                }

                if (!pre.Typed.IsError && Types.IsVoid(pre.Typed.Type))
                {
                    Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
                }
            }

            if (slot.Mut && !a.Mut)
            {
                Report(ErrorIds.MutAtCall, a.Line, $"the parameter `{slot.Name}` is `mut`: the call writes `mut` before the argument (M7)");
            }
            else if (!slot.Mut && a.Mut)
            {
                Report(ErrorIds.MutAtCall, a.Line, $"the parameter `{slot.Name}` is not `mut`: no `mut` before the argument (M7)");
            }
            else if (slot.Mut)
            {
                MutArgument(a, mutRoots);
            }
        }
    }

    /// <summary>M7: what stands after <c>mut</c> at a call is a <c>mut</c> variable or a path of data fields from one, and each one once.</summary>
    private void MutArgument(Argument a, List<string> roots)
    {
        var root = a.Value;
        while (true)
        {
            if (root is ParenthesizedExpression p)
            {
                root = p.Inner;
            }
            else if (root is MemberAccess { Conditional: false } m)
            {
                root = m.Target;
            }
            else
            {
                break;
            }
        }

        if (root is not NameExpression name)
        {
            Report(ErrorIds.MutAtCall, a.Line, "after `mut` stands a `mut` variable or a path from one, not a call result or a literal (M7)");
            return;
        }

        var (variable, captured) = Lookup(name.Name);
        if (variable is null || Types.IsError(variable.Type))
        {
            return;
        }

        if (captured)
        {
            Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{name.Name}` and cannot pass it as `mut` (F11)");
        }
        else if (!variable.Mut)
        {
            Report(ErrorIds.MutRequired, a.Line, $"`{name.Name}` was declared without `mut` and cannot be passed as `mut` (M7)");
        }
        else if (roots.Contains(name.Name))
        {
            Report(ErrorIds.MutAtCall, a.Line, $"`{name.Name}` is passed as `mut` twice (M7)");
        }

        roots.Add(name.Name);
        variable.Changed = true;
    }
}
