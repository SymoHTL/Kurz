using System.Numerics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    /// <summary>A parameter as a call binds it: of a function (F12), a primary constructor (D1) or a function value (F11).</summary>
    private sealed record Slot(string Name, KurzType Type, bool Mut, bool HasDefault, bool GenericPosition);

    private static readonly IReadOnlyDictionary<TypeParameterSymbol, KurzType> NoMap = new Dictionary<TypeParameterSymbol, KurzType>();

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

        /// <summary>The id the binding error carries: <c>argument-mismatch</c> (D11), or <c>syntax</c> for a count of type arguments no rule gives a meaning (T19, E4).</summary>
        public string BindingErrorId { get; set; } = ErrorIds.ArgumentMismatch;

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
                        model.RecordName(n, variable);
                        model.RecordType(n, new Typed(variable.Type));
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
            CheckArgumentLoosely(a.Value);
        }
    }

    /// <summary>A lambda whose parameters nothing types is read with its diagnostics dropped: its own error would stand beside the call's (E1), and the variables it reads still count.</summary>
    private void CheckArgumentLoosely(Expression value)
    {
        if (value is LambdaExpression l && l.Parameters.Any(p => p.Type is null))
        {
            CheckLoosely(value);
            return;
        }

        CheckExpression(value, null);
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

        model.RecordCall(c, new CallInfo(CallKind.Print, null, null, null, NoMap, [0], 1, Types.Void));
        return new Typed(Types.Void);
    }

    /// <summary>T10: a conversion written as a call of a number type or a time type; the constant folded as T6 and T26 say.</summary>
    private Typed CheckConversion(PrimitiveType target, CallExpression c)
    {
        if (!Types.IsNumber(target) && !Types.IsTime(target))
        {
            NotSupported(c.Line, $"a conversion to `{target}` (A5 to A7)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments.Count != 1 || c.Arguments[0].Name is not null)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, $"a conversion to `{target}` takes one value (T10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments[0].Mut)
        {
            Report(ErrorIds.MutAtCall, c.Arguments[0].Line, "a conversion changes nothing: no `mut` before its argument (M7)");
        }

        var argument = c.Arguments[0].Value;
        Typed source;
        if (IsLiteralOnly(argument))
        {
            // L17: the conversion reaches the integer literals inside it, whatever number or time type it names; a fraction or
            // exponent literal keeps its own type, so that `float(1e300)` converts a double (T26) instead of reading the literal as a float
            var kept = realKeepsOwnType;
            realKeepsOwnType = true;
            source = CheckExpression(argument, target);
            realKeepsOwnType = kept;
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
            var converted = ConvertNumber(source, target, c.Line);
            if (!converted.IsError)
            {
                model.RecordCall(c, new CallInfo(CallKind.Conversion, null, null, target, NoMap, [0], 1, target));
            }

            return converted;
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

            model.RecordCall(c, new CallInfo(CallKind.Conversion, null, null, target, NoMap, [0], 1, target));
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

                return new Typed(target, new RealConstant(target.Kind == Primitive.Float ? (float)(double)k.Value : (double)k.Value)); // T26: beyond the range it is an infinity, no error
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
                    // (double)decimal.MaxValue rounds up to 2^96, one above the range, so a value equal to it is outside too
                    if (double.IsNaN(r.Value) || double.IsInfinity(r.Value) || Math.Abs(r.Value) >= (double)decimal.MaxValue)
                    {
                        Report(ErrorIds.ConstantOverflow, line, "the value does not fit a `decimal` (T6)");
                        return Typed.Error;
                    }

                    return new Typed(target, new DecimalConstant((decimal)r.Value));
                }

                // T26: a `double` into a `float` beyond its range becomes an infinity and loses nothing the rule names, so no error
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
            Report(c.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, c.Line, "`List` takes one type argument (M9, T17; E4 for a second one)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments.Count != 0)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, "`List<T>()` takes no arguments (M9)");
            CheckArgumentsLoosely(c);
        }

        var element = Resolve(c.TypeArguments[0]);
        if (Types.IsError(element))
        {
            return Typed.Error;
        }

        var list = new ListType(element);
        model.RecordCall(c, new CallInfo(CallKind.ListNew, null, null, null, NoMap, [], 0, list));
        return new Typed(list);
    }

    /// <summary>D2, D7, D11: a data value made by the type's name with the base's fields first.</summary>
    private Typed CheckConstruction(DataSymbol data, CallExpression c, KurzType? expected)
    {
        if (!data.HasFields)
        {
            // D8 writes the value as the bare name: `Name()` is text no rule gives a meaning (E4); an argument is D11's error
            if (c.Arguments.Count != 0)
            {
                Report(ErrorIds.ArgumentMismatch, c.Line, $"`{data.Name}` has no fields and takes no argument: its value is the bare name (D8, D11)");
                CheckArgumentsLoosely(c);
            }
            else
            {
                Report(ErrorIds.Syntax, c.Line, $"the value of `{data.Name}` is the bare name, with no `()` (D8)");
            }

            return Typed.Error;
        }

        var slots = data.Fields.Select(f => new Slot(f.Name, f.Type, false, f.HasDefault, data.TypeParameters.Any(p => Types.Mentions(f.Type, p)))).ToList();
        var candidate = new Candidate { Slots = slots, TypeParameters = data.TypeParameters, Result = Types.Void }; // the result is built below from the type arguments
        if (c.TypeArguments.Count > 0)
        {
            if (c.TypeArguments.Count != data.TypeParameters.Count)
            {
                Report(ErrorIds.Syntax, c.Line, $"`{data.Name}` takes {data.TypeParameters.Count} type arguments (T19; E4 for another count)");
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
        if (arguments.Any(Types.IsError))
        {
            return Typed.Error;
        }

        var made = new DataType(data, arguments);
        model.RecordCall(c, new CallInfo(CallKind.DataNew, null, data, null, candidate.Map, candidate.Binding, candidate.Slots.Count, made));
        return new Typed(made);
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

            if ((dataTypes.TryGetValue(n.Name, out var data) && data.HasFields) || IsBuiltInName(n.Name) || functions.ContainsKey(n.Name))
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` has no member `{m.Member}` (V10)");
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
                        if (!result.IsError)
                        {
                            model.RecordCall(c, new CallInfo(CallKind.ListAdd, null, null, null, NoMap, candidate.Binding, 1, Types.Void));
                            MutReceiver(m.Target, c.Line, "`Add` changes the list it is called on"); // after the call's own error nothing more (E1)
                        }

                        return result;
                    }

                case "Where":
                    {
                        var test = new FunctionType([list.Element], Types.Bool);
                        var candidate = new Candidate { Slots = [new Slot("test", test, false, false, false)], Result = list };
                        var result = Call([candidate], c, null);
                        if (!result.IsError)
                        {
                            model.RecordCall(c, new CallInfo(CallKind.ListWhere, null, null, null, NoMap, candidate.Binding, 1, list));
                        }

                        return result;
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

    /// <summary>M5: the receiver of a <c>mut</c> method is a <c>mut</c> variable or a path of data fields from one; in a lambda, such a variable is not changed (F11). A path through an index is not read (M9).</summary>
    private void MutReceiver(Expression receiver, int line, string what)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(receiver);
        while (root is MemberAccess { Conditional: false } m)
        {
            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        if (root is IndexExpression)
        {
            NotSupported(line, "a `mut` method on an element of a list (M5, M9)");
            return;
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

        model.RecordName(name, variable);

        if (!IsFieldPath(variable.Type, members, out var notField))
        {
            Report(ErrorIds.MutRequired, line, $"{what}, and `{notField}` is no field of a data value: only a path of data fields from a `mut` variable reaches a value that changes (M5, M4)");
            return;
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

    /// <summary>M4: whether every member of the path from a value of <paramref name="type"/> is a field of a data type; a type the checker could not tell passes.</summary>
    private static bool IsFieldPath(KurzType type, List<MemberAccess> members, out string notField)
    {
        notField = "";
        var current = type;
        foreach (var m in members)
        {
            if (Types.IsError(current))
            {
                return true;
            }

            if (current is DataType d && d.Symbol.Field(m.Member) is { } field)
            {
                current = SubstituteData(d, field.Type);
                continue;
            }

            notField = m.Member;
            return false;
        }

        return true;
    }

    // ---- overloads, binding and inference ----------------------------------------------------------

    /// <summary>F12 to F14, T27: the one function of the name a call picks.</summary>
    private Typed ResolveCall(List<FunctionSymbol> candidates, CallExpression c, KurzType? expected)
    {
        var typeArguments = c.TypeArguments.Select(Resolve).ToList(); // once: an error in one is one diagnostic, whatever the number of overloads (E1)
        var list = new List<Candidate>();
        foreach (var f in candidates)
        {
            var slots = f.Parameters.Select(p => new Slot(p.Name, p.Type, p.Mut, p.HasDefault, f.TypeParameters.Any(t => Types.Mentions(p.Type, t)))).ToList();
            var candidate = new Candidate { Function = f, Slots = slots, TypeParameters = f.TypeParameters, Result = f.ReturnType };
            if (typeArguments.Count > 0)
            {
                if (typeArguments.Count != f.TypeParameters.Count)
                {
                    candidate.BindingError = $"`{f.Name}` takes {f.TypeParameters.Count} type arguments (T19; E4 for another count)";
                    candidate.BindingErrorId = ErrorIds.Syntax;
                }
                else
                {
                    candidate.Map = f.TypeParameters.Zip(typeArguments).ToDictionary(p => p.First, p => p.Second);
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
                Report(ErrorIds.AmbiguousCall, c.Line, exact.Count > 1 ? "the call fits more than one function exactly (F13)" : "the call fits more than one function, none of them exactly (F13)");
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

                if (pre.Lambda is not null)
                {
                    CheckArgumentLoosely(pre.Lambda);
                }
            }

            return Typed.Error;
        }

        Commit(picked, pres);
        var result = Types.Substitute(picked.Result, picked.Map);
        model.RecordCall(c, new CallInfo(picked.Function is null ? CallKind.Value : CallKind.Function, picked.Function, null, null, picked.Map, picked.Binding, picked.Slots.Count, result));
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
                    candidate.BindingError = "an argument without a name stands after a named one: it comes first, or gets a name (D11)";
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

    /// <summary>
    /// T27: a type parameter takes the one type every argument in its positions has, with no widening
    /// between them; where one argument's data type is the base of the others', the base; literals take
    /// the type the others fix, and literals alone take the type a constant of them has. The arguments
    /// of a position are gathered first, so that their order makes no difference; an argument the checker
    /// could not type fixes nothing and contradicts nothing (E1).
    /// </summary>
    private static void Infer(Candidate candidate, List<Pre> pres)
    {
        var map = new Dictionary<TypeParameterSymbol, KurzType>(candidate.Map);
        var direct = candidate.TypeParameters.ToDictionary(p => p, _ => new List<KurzType>());
        for (var i = 0; i < pres.Count; i++)
        {
            var pre = pres[i];
            if (pre.Lambda is not null || pre.LiteralOnly)
            {
                continue;
            }

            var slot = candidate.Slots[candidate.Binding[i]].Type;
            if (slot is TypeParameterType tp && direct.TryGetValue(tp.Symbol, out var types))
            {
                types.Add(pre.Typed.Type);
            }
            else if (!Unify(slot, pre.Typed.Type, map))
            {
                candidate.CannotInfer = true;
                return;
            }
        }

        foreach (var (p, types) in direct)
        {
            var known = types.Where(t => !Types.IsError(t)).ToList();
            if (known.Count == 0)
            {
                if (types.Count > 0)
                {
                    map[p] = Types.Error; // every argument of the position carries its own error: nothing is inferred and nothing more is reported (E1)
                }

                continue;
            }

            var common = known.FirstOrDefault(t => known.All(u => Types.Same(u, t) || (u is DataType && t is DataType && Types.Assignable(u, t))));
            if (common is null || (map.TryGetValue(p, out var bound) && !Types.Same(bound, common) && !Types.IsError(bound)))
            {
                candidate.CannotInfer = true;
                return;
            }

            map[p] = common;
        }

        foreach (var p in candidate.TypeParameters)
        {
            if (map.ContainsKey(p))
            {
                continue;
            }

            // literals alone in its positions: the type a constant expression of them has (L10, L17, T27)
            KurzType? fixedType = null;
            var untypable = false;
            for (var i = 0; i < pres.Count; i++)
            {
                var pre = pres[i];
                if (!pre.LiteralOnly || candidate.Slots[candidate.Binding[i]].Type is not TypeParameterType tp || !ReferenceEquals(tp.Symbol, p))
                {
                    continue;
                }

                if (pre.Typed.IsError)
                {
                    untypable = true; // its own error stands; no second one for the inference (E1)
                    continue;
                }

                fixedType = fixedType is null ? pre.Typed.Type : LiteralCommon(fixedType, pre.Typed.Type);
                if (fixedType is null)
                {
                    break;
                }
            }

            if (fixedType is null && untypable)
            {
                map[p] = Types.Error;
                continue;
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

    /// <summary>The type two literal arguments of one type parameter fix together: the type the constant expression of them would have (L10, L17, T18, T23).</summary>
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

        if (Types.IsDecimal(pa) || Types.IsDecimal(pb))
        {
            // T23: an integer literal beside a decimal takes decimal; a floating-point literal beside one meets no type (T18)
            return (Types.IsInteger(pa) || Types.IsDecimal(pa)) && (Types.IsInteger(pb) || Types.IsDecimal(pb)) ? Types.Decimal : null;
        }

        if (Types.IsNumber(pa) && Types.IsNumber(pb))
        {
            return pa.Kind == Primitive.Double || pb.Kind == Primitive.Double ? Types.Double : Types.Float;
        }

        return Types.Same(pa, pb) ? pa : null;
    }

    /// <summary>Binds the type parameters inside a list, data or function type to the parts of the argument's type; a part bound already has to be the same (T27).</summary>
    private static bool Unify(KurzType parameter, KurzType argument, Dictionary<TypeParameterSymbol, KurzType> map)
    {
        switch (parameter)
        {
            case TypeParameterType p:
                if (Types.IsError(argument))
                {
                    return true; // an argument the checker could not type fixes nothing and contradicts nothing (E1)
                }

                if (map.TryGetValue(p.Symbol, out var bound))
                {
                    return Types.Same(bound, argument) || Types.IsError(bound);
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

    /// <summary>
    /// F13, F14: whether every argument fits its parameter by its own type, and whether all do exactly.
    /// A literal argument of an overloaded function has its own type for the pick (F14: `Show(4)` fits
    /// `Show(long)` and not `Show(short)`); where the function is not overloaded, or the position is
    /// generic, the parameter's type reaches the literal (L17, T27) and the literal fits when it is read
    /// under that type without an error. A <c>mut</c> parameter takes an argument of its own type only:
    /// the callee writes a value of the parameter's type back into the variable (M7).
    /// </summary>
    private void Fit(Candidate candidate, List<Pre> pres, bool only)
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
                // the literal takes the parameter's type: it fits, and a value it cannot hold there (`300` as a `byte`, `1.5` as an `int`) is
                // the error of the literal under that type, reported by Commit at the argument (L17, T6); only a literal that keeps its own type
                // there and does not widen into the parameter's is a misfit
                Push();
                var under = CheckExpression(pre.Argument.Value, type);
                var errors = Pop(commit: false);
                if (errors.Count == 0 && !under.IsError && !Types.Assignable(under.Type, type))
                {
                    candidate.Misfit ??= (i, type);
                }

                exact &= Types.Same(pre.Typed.Type, type);
                continue;
            }

            if (!(slot.Mut ? Types.Same(pre.Typed.Type, type) : Types.Assignable(pre.Typed.Type, type)))
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
            Report(candidates[0].BindingErrorId, c.Line, candidates[0].BindingError!);
            return;
        }

        if (bound.CannotInfer)
        {
            Report(ErrorIds.CannotInfer, c.Line, "the type arguments are neither written nor inferable from the arguments (T27)");
            return;
        }

        var (index, parameter) = bound.Misfit!.Value;
        var pre = pres[index];
        if (pre.Lambda is null && bound.Slots[bound.Binding[index]].Mut && Types.Assignable(pre.Typed.Type, parameter))
        {
            Report(ErrorIds.TypeMismatch, pre.Argument.Line, $"a `{pre.Typed.Type}` passed as `mut` where a `mut {parameter}` is required: a `mut` parameter takes a variable of its own type, since the callee writes a `{parameter}` back into it (M7, T1)");
            return;
        }

        var argumentType = pre.Lambda is not null ? "a lambda" : $"a `{pre.Typed.Type}`";
        var id = pre.Lambda is not null ? ErrorIds.TypeMismatch : Types.MismatchId(pre.Typed.Type, parameter);
        Report(id, pre.Argument.Line, $"{argumentType} where a `{parameter}` is required (T1)");
    }

    /// <summary>The picked candidate: literals and lambdas typed by their parameters (L17, F11), the <c>mut</c> markers checked (M7).</summary>
    private void Commit(Candidate candidate, List<Pre> pres)
    {
        var mutPaths = new List<string>();
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
                    Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (E4, O10)");
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
                MutArgument(a, mutPaths);
            }
        }
    }

    /// <summary>
    /// M7: what stands after <c>mut</c> at a call is a <c>mut</c> variable or a path of data fields from
    /// one, and each path once: the same path twice, or a path and one it is a prefix of, would reach one
    /// value through two parameters; two fields of one variable are two values. A path through an index
    /// is not read (M9).
    /// </summary>
    private void MutArgument(Argument a, List<string> paths)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(a.Value);
        while (root is MemberAccess { Conditional: false } m)
        {
            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        if (root is IndexExpression)
        {
            NotSupported(a.Line, "an element of a list passed as `mut` (M7, M9)");
            return;
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

        model.RecordName(name, variable);
        if (!IsFieldPath(variable.Type, members, out var notField))
        {
            Report(ErrorIds.MutAtCall, a.Line, $"`{notField}` is no field of a data value: after `mut` stands a `mut` variable or a path of data fields from one (M7)");
            return;
        }

        var path = string.Join(".", members.Select(m => m.Member).Prepend(name.Name));
        if (captured)
        {
            Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{name.Name}` and cannot pass it as `mut` (F11)");
        }
        else if (!variable.Mut)
        {
            Report(ErrorIds.MutRequired, a.Line, $"`{name.Name}` was declared without `mut` and cannot be passed as `mut` (M7)");
        }
        else if (paths.Any(other => other == path || other.StartsWith(path + ".", StringComparison.Ordinal) || path.StartsWith(other + ".", StringComparison.Ordinal)))
        {
            Report(ErrorIds.MutAtCall, a.Line, $"`{path}` is passed as `mut` twice (M7)");
        }

        paths.Add(path);
        variable.Changed = true;
    }
}
