using System.Numerics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    private static readonly IReadOnlyDictionary<TypeParameterSymbol, KurzType> NoMap = new Dictionary<TypeParameterSymbol, KurzType>();

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

        /// <summary>A construct the checker does not read stands as the argument: it was <c>not supported</c> before the pick, and the pick checks nothing of it (E1).</summary>
        public bool Unread { get; init; }

        /// <summary>The argument has no type of its own and is typed by the parameter it lands on: <c>null</c>, which a pointer takes (R10), or the name of a function of Kurz, which a C function type refuses (R17).</summary>
        public bool BySlot { get; init; }
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
        if (keepCases || result.IsError || result.Type is not UnionType u || Types.NonNull(u) is not null)
        {
            return result; // N8: a `T?` is no outcome list, and a call that returns one yields it whole
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

                    if (n.Name == "Bytes")
                    {
                        return CheckBytesConstruction(c);
                    }

                    if (n.Name == "Map")
                    {
                        return CheckMapConstruction(c);
                    }

                    if (cFunctions.TryGetValue(n.Name, out var cFunction))
                    {
                        return CallC(cFunction, c, expected); // R16
                    }

                    if (functions.TryGetValue(n.Name, out var candidates))
                    {
                        return ResolveCall(candidates, c, expected);
                    }

                    if (dataTypes.TryGetValue(n.Name, out var data))
                    {
                        return CheckConstruction(data, c, expected);
                    }

                    if (enums.ContainsKey(n.Name))
                    {
                        // D18: the value of a number is `Plan.From(n)`, against the C# form `Plan(2)` (section 4, the owner, 2026-10-02)
                        Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is an enum, and the value of a number is `{n.Name}.From(n)` (D18)");
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    if (IsUnreadName(n.Name))
                    {
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    if (n.Name == "asm")
                    {
                        Report(ErrorIds.Syntax, n.Line, "`asm` is a statement of its own and no value (R11, E4)");
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    if (n.Name is "Ptr" or "SizeOf")
                    {
                        // R14 states `Ptr<T>(x)` and `SizeOf<T>()`; another count of type arguments or arguments is text no rule gives a meaning (E4)
                        var shaped = c.TypeArguments.Count == 1 && c.Arguments.Count == (n.Name == "Ptr" ? 1 : 0);
                        if (shaped)
                        {
                            return n.Name == "Ptr" ? CheckPtr(c) : CheckSizeOf(c);
                        }

                        Report(ErrorIds.Syntax, n.Line, $"R14 writes `{n.Name}` as `{(n.Name == "Ptr" ? "Ptr<T>(x)" : "SizeOf<T>()")}` (E4)");
                        CheckArgumentsLoosely(c);
                        return Typed.Error;
                    }

                    Report(ErrorIds.UnknownName, n.Line, $"no `{n.Name}` is visible here (V10)");
                    CheckArgumentsLoosely(c);
                    return Typed.Error;
                }

            case TypeMemberAccess t:
                return CheckTypeMemberCall(t, c); // R15
            case MemberAccess { Target: NameExpression folderName } m when used.TryGetValue(folderName.Name, out var folder) && Lookup(folderName.Name).Variable is null:
                return CheckFolderCall(folder, folderName.Name, m, c, expected);
            case MemberAccess m:
                return CheckMethodCall(m, c);
            default:
                {
                    var callee = CheckExpression(c.Callee, null);
                    return CallValue(callee.Type, c);
                }
        }
    }

    /// <summary>A call of a function of a folder a `use` brought in (F10), qualified by the folder's name, `sys.Write(...)`, as the design record's section 2 states it (the owner, 2026-10-23, "Rust's sys"); a function of the folder that is not `pub` is not visible here (F9).</summary>
    private Typed CheckFolderCall(Checker folder, string name, MemberAccess m, CallExpression c, KurzType? expected)
    {
        var all = folder.functions.TryGetValue(m.Member, out var list) ? list : [];
        var visible = all.Where(f => f.Declaration is { } d && d.Modifiers.HasFlag(Modifiers.Pub)).ToList();
        if (visible.Count == 0)
        {
            if (all.Count > 0)
            {
                Report(ErrorIds.NotVisible, m.Line, $"`{m.Member}` of the folder `{name}` is not `pub` (F9)");
            }
            else
            {
                Report(ErrorIds.UnknownName, m.Line, $"the folder `{name}` has no function `{m.Member}` (V10)");
            }

            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var result = ResolveCall(visible, c, expected, out var picked);
        if (picked is not null)
        {
            folderCalls.Add(picked);
        }

        return result;
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

        CheckExpression(value, Untyped(value));
    }

    /// <summary>The type a value is read with where its site's type is unknown, the error: none, but for a <c>null</c>, which takes its type from the site (N6) and so stands for the error (E1).</summary>
    private static ErrorType? Untyped(Expression value) => Unwrap(value) is NullLiteral ? Types.Error : null;

    /// <summary>T19: a callee without type parameters, <c>print</c>, a conversion, a function value or a list's method, takes no type argument; one written is a count no rule gives a meaning (E4).</summary>
    private bool RefusesTypeArguments(CallExpression c, string callee)
    {
        if (c.TypeArguments.Count == 0)
        {
            return false;
        }

        Report(ErrorIds.Syntax, c.Line, $"{callee} takes no type arguments (T19; E4 for another count)");
        CheckArgumentsLoosely(c);
        return true;
    }

    /// <summary>F11: a call of a function value, by position.</summary>
    private Typed CallValue(KurzType type, CallExpression c)
    {
        if (Types.IsError(type))
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (type is CFunctionType cf)
        {
            RequireRaw(c.Line, "a call through a value of a C function type", "R17");
            if (RefusesTypeArguments(c, $"a `{type}`"))
            {
                return Typed.Error;
            }

            var cSlots = cf.Parameters.Select((p, i) => new Slot($"#{i}", p, false, false, false)).ToList();
            return Record(c, CallKind.Value, Call([new Candidate { Slots = cSlots, Result = cf.Result }], c, null, out var cPicked), cPicked);
        }

        if (type is not FunctionType f)
        {
            Report(ErrorIds.TypeMismatch, c.Line, $"a `{type}` is no function and cannot be called (F11)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"a `{type}`"))
        {
            return Typed.Error;
        }

        var slots = f.Parameters.Select((p, i) => new Slot($"#{i}", p, false, false, false)).ToList();
        var candidate = new Candidate { Slots = slots, Result = f.Result };
        return Record(c, CallKind.Value, Call([candidate], c, null, out var picked), picked);
    }

    private Typed CheckPrint(CallExpression c)
    {
        if (RefusesTypeArguments(c, "`print`"))
        {
            return new Typed(Types.Void);
        }

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
            CheckArgumentsLoosely(c); // the call's own error: nothing more about it (E1)
            return new Typed(Types.Void);
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
        else if (HoldsRaw(t.Type, []))
        {
            NotSupported(c.Line, "the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)");
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

        if (RefusesTypeArguments(c, $"a conversion to `{target}`"))
        {
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
            CheckArgumentsLoosely(c); // the call's own error: nothing more about it (E1)
            return Typed.Error;
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

        var converted = ConvertKind(target, c, source);
        if (!converted.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.Conversion, null, null, target, NoMap, [0], 1, target));
        }

        return converted;
    }

    /// <summary>T10, T13, R14: a value that typed, converted to a number type, a time type or, from a pointer, a <c>long</c>.</summary>
    private Typed ConvertKind(PrimitiveType target, CallExpression c, Typed source)
    {
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

        if (source.Type is PointerType && target.Kind == Primitive.Long)
        {
            return new Typed(Types.Long); // R14: the address a pointer holds
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

                    return new Typed(target, new DecimalConstant(ToDecimal(r.Value)));
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

    /// <summary>
    /// T6, T26: a <c>double</c> inside decimal's range rounded to decimal's precision: its exact binary value at
    /// the largest scale up to 28 whose digits fit decimal's 96 bits, rounded half to even, and its zeros at the
    /// end removed down to the scale 0, a zero's among them, which has no sign (the owner, 2026-10-28, "Exact
    /// binary value"). The conversion of the runtime library keeps 15 significant digits, so that
    /// <c>decimal(0.1) - 0.1m</c> would be 0.
    /// </summary>
    private static decimal ToDecimal(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        var power = Math.Max(exponent, 1) - 1075; // the value is mantissa * 2^power
        var numerator = power >= 0 ? mantissa << power : mantissa;
        var denominator = power >= 0 ? BigInteger.One : BigInteger.One << -power;
        var limit = BigInteger.One << 96;
        for (var scale = 28; scale >= 0; scale--)
        {
            var digits = BigInteger.DivRem(numerator * BigInteger.Pow(10, scale), denominator, out var remainder);
            var twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !digits.IsEven))
            {
                digits += 1;
            }

            if (digits >= limit)
            {
                continue;
            }

            while (scale > 0 && digits % 10 == 0)
            {
                digits /= 10;
                scale--;
            }

            var mask = new BigInteger(uint.MaxValue);
            return new decimal((int)(uint)(digits & mask), (int)(uint)((digits >> 32) & mask), (int)(uint)(digits >> 64), bits < 0 && !digits.IsZero, (byte)scale);
        }

        throw new InvalidOperationException("the value is outside decimal's range"); // the caller reported it: `constant-overflow` (T6)
    }

    /// <summary>M9: <c>List&lt;T&gt;()</c> makes an empty list.</summary>
    /// <summary>M9: <c>Bytes()</c>, an empty list of bytes, through the second name of <c>List&lt;byte&gt;</c> (section 4, the owner, 2026-10-29).</summary>
    private Typed CheckBytesConstruction(CallExpression c)
    {
        if (RefusesTypeArguments(c, "`Bytes()`"))
        {
            return Typed.Error;
        }

        if (c.Arguments.Count != 0)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, "`Bytes()` takes no arguments (M9)");
            CheckArgumentsLoosely(c);
        }

        var list = new ListType(Types.Byte);
        model.RecordCall(c, new CallInfo(CallKind.ListNew, null, null, null, NoMap, [], 0, list));
        return new Typed(list);
    }

    /// <summary>
    /// T15: <c>string.FromUtf8(b)</c>, a string of the bytes of <c>b</c> or the case <c>InvalidUtf8</c>, and <c>string.FromUtf8Lossy(b)</c>,
    /// a string with each sequence that is not UTF-8 read as U+FFFD (section 4, the owner, 2026-10-29, "Union, as Rust").
    /// </summary>
    private Typed CheckFromUtf8(CallExpression c, bool lossy)
    {
        var form = lossy ? "`string.FromUtf8Lossy`" : "`string.FromUtf8`";
        if (RefusesTypeArguments(c, form))
        {
            return Typed.Error;
        }

        KurzType result = lossy ? Types.String : new UnionType([Types.String, new DataType(dataTypes["InvalidUtf8"], [])]);
        var candidate = new Candidate { Slots = [new Slot("b", new ListType(Types.Byte), false, false, false)], Result = result };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(lossy ? CallKind.FromUtf8Lossy : CallKind.FromUtf8, null, null, result, NoMap, picked.Binding, 1, result));
        }

        return typed;
    }

    /// <summary>
    /// D18: <c>Plan.From(n)</c>, the value whose number is <c>n</c>, an <c>int</c>, or the case <c>Invalid</c>, an outcome (O1); on an enum that
    /// numbers none it is <c>enum-number</c> (D20).
    /// </summary>
    private Typed CheckEnumFrom(EnumSymbol symbol, CallExpression c)
    {
        var form = $"`{symbol.Name}.From`";
        if (RefusesTypeArguments(c, form))
        {
            return Typed.Error;
        }

        if (!symbol.Numbered && !symbol.Refused)
        {
            Report(ErrorIds.EnumNumber, c.Line, $"`{symbol.Name}` numbers none of its values, so no number gives one (D20)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var type = new EnumType(symbol);
        var result = new UnionType([type, new DataType(dataTypes["Invalid"], [])]);
        var candidate = new Candidate { Slots = [new Slot("number", Types.Int, false, false, false)], Result = result };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.EnumFrom, null, null, type, NoMap, picked.Binding, 1, result));
        }

        return symbol.Refused ? Typed.Error : typed;
    }

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

    /// <summary>M9: <c>Map&lt;K, V&gt;()</c>, a map without entries.</summary>
    private Typed CheckMapConstruction(CallExpression c)
    {
        if (c.TypeArguments.Count != 2)
        {
            Report(c.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, c.Line, "`Map` takes two type arguments (M9, T17)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (c.Arguments.Count != 0)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, "`Map<K, V>()` takes no arguments (M9)");
            CheckArgumentsLoosely(c);
        }

        var key = Resolve(c.TypeArguments[0]);
        var value = Resolve(c.TypeArguments[1]);
        var map = Types.IsError(key) || Types.IsError(value) ? Types.Error : MapOf(key, value, c.Line);
        if (Types.IsError(map))
        {
            return Typed.Error;
        }

        model.RecordCall(c, new CallInfo(CallKind.MapNew, null, null, null, NoMap, [], 0, map));
        return new Typed(map);
    }

    /// <summary>
    /// M9: the type of a map the checker reads. A key is a value of a type that compares by content (section 4, the owner, 2026-10-30,
    /// "Any type with =="): a string, an integer, a <c>char</c>, a <c>bool</c>, a time, an enum, a <c>data</c> value of such fields, a
    /// field of a union of them among them (D3), and a list of such items. A map, a function value and a file or a socket take no
    /// <c>==</c>, so they are no key (<c>type-mismatch</c>, T18); the rest are <c>not supported</c>: a nullable key, which compares by
    /// the lifted <c>==</c> of N1, a <c>double</c> or a <c>float</c>, whether a key at all is a fork no answer took, and a
    /// <c>decimal</c>, a pointer, a type parameter, a value that holds a map or a function value. A value is any type whose nullable
    /// type the checker reads (N1), and a nullable one, which <c>map[key]</c> reads as it is (the owner, 2026-10-30, "Flatten, as C#").
    /// </summary>
    private KurzType MapOf(KurzType key, KurzType value, int line)
    {
        switch (KeyOf(key, []))
        {
            case (true, var why):
                Report(ErrorIds.TypeMismatch, line, $"a map's key is compared with `==`, which `{key}` does not take: {why} (M9, T18)");
                return Types.Error;
            case (false, var why):
                NotSupported(line, $"a map with keys of type `{key}`: {why} (M9)");
                return Types.Error;
        }

        if (!CanBeNullable(value) && !(Types.NonNull(value) is { } inner && CanBeNullable(inner)))
        {
            NotSupported(line, $"a map with values of type `{value}` (M9, N1)");
            return Types.Error;
        }

        return new MapType(key, value);
    }

    /// <summary>
    /// M9: why a value of the type is no key of a map, null where it is one: <c>Mismatch</c> for a type that takes no <c>==</c>, else a
    /// key the checker does not read. <paramref name="seen"/> holds the data types on the way, whose fields are read at the first meeting.
    /// </summary>
    private (bool Mismatch, string Why)? KeyOf(KurzType type, HashSet<DataSymbol> seen)
    {
        switch (type)
        {
            case ErrorType or EnumType:
                return null;
            case PrimitiveType p when Types.IsInteger(p) || Types.IsTime(p) || p.Kind is Primitive.String or Primitive.Char or Primitive.Bool:
                return null;
            case PrimitiveType p when Types.IsFloating(p):
                return (false, $"whether a `{p}` is a key is not decided"); // Rust's `HashMap` takes none, as `f64` has no `Hash`; C#'s `Dictionary` takes one
            case PrimitiveType p:
                return (false, $"the hash of a `{p}`, which reads 1.5 and 1.50 as one value, is not built");
            case MapType m:
                return (true, $"`==` between two `{m}` is `type-mismatch`");
            case FunctionType or HandleType or CFunctionType or VoidType:
                return (true, $"`==` takes no `{type}`");
            case UnionType u when Types.NonNull(u) is not null:
                return (false, "a nullable key compares by the lifted `==` (N1)");
            case ListType l:
                return KeyOf(l.Element, seen) is { } item ? item with { Why = $"its items: {item.Why}" } : null;
            case DataType d:
                {
                    if (!seen.Add(d.Symbol))
                    {
                        return null; // its fields are read where the data type was met first
                    }

                    foreach (var field in d.Symbol.Fields)
                    {
                        if (FieldKeyOf(SubstituteData(d, field.Type), seen) is { } why)
                        {
                            return (false, $"its field `{field.Name}`: {why}");
                        }
                    }

                    foreach (var derived in dataTypes.Values.Where(x => x.Base == d.Symbol))
                    {
                        if (KeyOf(new DataType(derived, derived.TypeParameters.Select(t => (KurzType)new TypeParameterType(t)).ToList()), seen) is { } inherited)
                        {
                            return inherited;
                        }
                    }

                    return null;
                }

            default:
                return (false, $"a `{type}` is no key the checker reads");
        }
    }

    /// <summary>M9 with D3: why a field of a <c>data</c> key is not read, null where it is: a key's type, or a union of them, <c>null</c> among its cases, which compares by its case and its value.</summary>
    private string? FieldKeyOf(KurzType type, HashSet<DataSymbol> seen)
    {
        if (type is UnionType u)
        {
            return u.Cases.Where(c => c is not NullType).Select(c => FieldKeyOf(c, seen)).FirstOrDefault(w => w is not null);
        }

        return KeyOf(type, seen) is { } why ? why.Why : null;
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

    /// <summary>M5, M9, T15, S2 to S8: a call of a member: a list's <c>Add</c>, <c>Where</c> and <c>Clear</c>; a function of <c>File</c> and <c>Directory</c>, and a file's methods; a data value's field of function type.</summary>
    private Typed CheckMethodCall(MemberAccess m, CallExpression c)
    {
        if (m.Conditional || InChain(m.Target))
        {
            // N5: C# reads a call after `?.` inside the chain, which section 4 does not say
            NotSupported(m.Line, "`?.` (N5)");
            CheckExpression(m.Target, null);
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (m.Target is NameExpression n && Lookup(n.Name).Variable is null && !(frame.Reserved?.Contains(n.Name) ?? false))
        {
            if (IsUnreadName(n.Name))
            {
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            if (dataTypes.TryGetValue(n.Name, out var data) && data.HasFields && HasBody(data))
            {
                NotSupported(m.Line, "a method of a data type (D16)"); // a member its body declares, which the checker does not read
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            if (n.Name == "string" && m.Member == "FromRaw")
            {
                return CheckFromRaw(c, Types.String, Types.Byte, "string.FromRaw"); // R15
            }

            if (n.Name == "string" && m.Member == "Allocate")
            {
                return CheckStringAllocate(c); // R15
            }

            if (n.Name == "string" && m.Member is "FromUtf8" or "FromUtf8Lossy")
            {
                return CheckFromUtf8(c, m.Member == "FromUtf8Lossy"); // T15
            }

            if (n.Name == "Bytes" && m.Member == "FromRaw")
            {
                return CheckFromRaw(c, new ListType(Types.Byte), Types.Byte, "Bytes.FromRaw"); // R15, through the second name of `List<byte>` (M9)
            }

            if (n.Name == "Bytes" && m.Member == "Allocate")
            {
                return CheckListAllocate(c, new ListType(Types.Byte), "Bytes.Allocate"); // R15, as above
            }

            if (n.Name is "File" or "Directory" or "TcpListener" or "TcpStream")
            {
                return CheckFileFunction(n.Name, m, c); // S2, S6 to S9
            }

            if (enums.TryGetValue(n.Name, out var enumSymbol))
            {
                if (m.Member == "From")
                {
                    return CheckEnumFrom(enumSymbol, c); // D18
                }

                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` has no function `{m.Member}`: an enum's one is `From` (D18, V10)");
                CheckArgumentsLoosely(c);
                return Typed.Error;
            }

            if ((data is not null && data.HasFields) || IsBuiltInName(n.Name) || functions.ContainsKey(n.Name))
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
                        if (RefusesTypeArguments(c, "`Add`"))
                        {
                            return Typed.Error;
                        }

                        var candidate = new Candidate { Slots = [new Slot("item", list.Element, false, false, false)], Result = Types.Void };
                        var result = Record(c, CallKind.ListAdd, Call([candidate], c, null, out var picked), picked);
                        if (!result.IsError)
                        {
                            MutReceiver(c, m.Target, c.Line, "`Add` changes the list it is called on"); // after the call's own error nothing more (E1)
                        }

                        return result;
                    }

                case "Where":
                    {
                        if (RefusesTypeArguments(c, "`Where`"))
                        {
                            return Typed.Error;
                        }

                        var test = new FunctionType([list.Element], Types.Bool);
                        var candidate = new Candidate { Slots = [new Slot("test", test, false, false, false)], Result = list };
                        return Record(c, CallKind.ListWhere, Call([candidate], c, null, out var picked), picked);
                    }

                case "Clear":
                    {
                        if (RefusesTypeArguments(c, "`Clear`"))
                        {
                            return Typed.Error;
                        }

                        var candidate = new Candidate { Slots = [], Result = Types.Void };
                        var result = Record(c, CallKind.ListClear, Call([candidate], c, null, out var picked), picked);
                        if (!result.IsError)
                        {
                            MutReceiver(c, m.Target, c.Line, "`Clear` changes the list it is called on"); // after the call's own error nothing more (E1)
                        }

                        return result;
                    }

                default:
                    break;
            }
        }

        if (target.Type is MapType map && m.Member is "ContainsKey" or "Remove" or "Clear")
        {
            return CheckMapMethod(c, m, map);
        }

        if (target.Type is HandleType handle && !(handle.Kind == Handle.TcpListener && m.Member == "Port"))
        {
            return CheckFileMethod(handle, m, c); // S3 to S5, S9
        }

        var member = MemberOf(target.Type, m.Member, m.Line);
        if (member.IsError)
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        model.RecordType(m, member); // the callee is the field's value (F11)
        return CallValue(member.Type, c);
    }

    /// <summary>
    /// M9: a map's members beside <c>Count</c>, with C#'s names and Rust's <c>Remove</c> (section 4, the owner, 2026-10-30, "C# names,
    /// Rust's Remove"): <c>ContainsKey(key)</c>, a <c>bool</c>; <c>mut Remove(key)</c>, the value of the key's entry, moved out of the
    /// map, or <c>null</c>, of the type <c>map[key]</c> has; and <c>mut Clear()</c>.
    /// </summary>
    private Typed CheckMapMethod(CallExpression c, MemberAccess m, MapType map)
    {
        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        var (kind, slots, result) = m.Member switch
        {
            "ContainsKey" => (CallKind.MapContainsKey, (List<Slot>)[new Slot("key", map.Key, false, false, false)], (KurzType)Types.Bool),
            "Remove" => (CallKind.MapRemove, [new Slot("key", map.Key, false, false, false)], Types.Flattened(map.Value)),
            _ => (CallKind.MapClear, [], Types.Void),
        };
        var candidate = new Candidate { Slots = slots, Result = result };
        var typed = Record(c, kind, Call([candidate], c, null, out var picked), picked);
        if (!typed.IsError && kind != CallKind.MapContainsKey)
        {
            MutReceiver(c, m.Target, c.Line, $"`{m.Member}` changes the map it is called on"); // after the call's own error nothing more (E1)
        }

        return typed;
    }

    /// <summary>
    /// M5: the receiver of a <c>mut</c> method is a <c>mut</c> variable or a path of data fields from one; in a lambda, such a variable
    /// is not changed (F11). A path through an index is not read (M9). The call changes what it takes as <c>mut</c> (M7).
    /// </summary>
    private void MutReceiver(CallExpression call, Expression receiver, int line, string what)
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
        else
        {
            var path = string.Join(".", members.Select(m => m.Member).Prepend(name.Name));
            changes[call] = new Change(variable, path, line, $"`{((MemberAccess)call.Callee).Member}` changes `{path}`");
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
    private Typed ResolveCall(List<FunctionSymbol> candidates, CallExpression c, KurzType? expected) => ResolveCall(candidates, c, expected, out _);

    /// <summary>As above, with the function picked, or null where the call is wrong.</summary>
    private Typed ResolveCall(List<FunctionSymbol> candidates, CallExpression c, KurzType? expected, out FunctionSymbol? function)
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

        var typed = Record(c, CallKind.Function, Call(list, c, expected, out var picked), picked);
        function = typed.IsError ? null : picked?.Function;
        return typed;
    }

    /// <summary>Binds the arguments to each candidate, picks one (F13) and checks the arguments against it.</summary>
    private Typed Call(List<Candidate> candidates, CallExpression c, KurzType? expected) => Call(candidates, c, expected, out _);

    /// <summary>As above, with the candidate picked, or null where none was.</summary>
    private Typed Call(List<Candidate> candidates, CallExpression c, KurzType? expected, out Candidate? picked)
    {
        var pres = new List<Pre>();
        foreach (var a in c.Arguments)
        {
            if (a.Value is LambdaExpression l)
            {
                pres.Add(new Pre { Argument = a, Lambda = l });
                continue;
            }

            if (Unwrap(a.Value) is NullLiteral || (Unwrap(a.Value) is NameExpression kurz && Lookup(kurz.Name).Variable is null && functions.TryGetValue(kurz.Name, out var named) && named is not [{ TakesC: true }]))
            {
                pres.Add(new Pre { Argument = a, Typed = Typed.Error, BySlot = true }); // fits every parameter, which Commit types it by
                continue;
            }

            if (a.Mut && IsElementPath(a.Value))
            {
                NotSupported(a.Line, "an element of a list passed as `mut` (M7, M9)"); // before the pick, so that no misfit of its type is reported beside it (E1)
                Push();
                CheckExpression(a.Value, null);
                Pop(commit: false);
                pres.Add(new Pre { Argument = a, Typed = Typed.Error, Unread = true });
                continue;
            }

            MutArgumentRead(a); // N7
            Push();
            var typed = CheckExpression(a.Value, null);
            var captured = Pop(commit: false);
            if (!typed.IsError && Types.IsVoid(typed.Type))
            {
                // O10: a `void` call where a value stands is `syntax`, whatever the parameter (the reading in compiler/README.md), and it is
                // the argument's own error, so that it fits no parameter and contradicts none (E1)
                captured.Add(Diagnostic.Error(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (E4, O10)"));
                typed = Typed.Error;
            }

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

        // E1: an argument with an error of its own may have been meant for any of the functions of the name, so neither
        // `ambiguous-call` nor a misfit is reported beside its error, which the arguments' diagnostics below carry
        var open = candidates.Count > 1 && pres.Any(p => p.Lambda is null && p.Typed.IsError);
        var fitting = candidates.Where(x => x.Fits).ToList();
        picked = null;
        if (fitting.Count == 1)
        {
            picked = fitting[0];
        }
        else if (fitting.Count > 1 && !open)
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
        else if (fitting.Count == 0 && !open)
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

                if (pre.BySlot)
                {
                    CheckExpression(pre.Argument.Value, null); // `null` or a function's name with no parameter to type it
                }
            }

            return Typed.Error;
        }

        Commit(picked, pres);
        var result = Types.Substitute(picked.Result, picked.Map);
        return new Typed(result);
    }

    /// <summary>Records what a call that typed calls: the candidate picked, its type arguments, its binding and its whole result.</summary>
    private Typed Record(CallExpression c, CallKind kind, Typed typed, Candidate? picked, IReadOnlyList<int>? binding = null)
    {
        if (picked is not null && !typed.IsError)
        {
            var map = picked.Map.Count == 0 ? NoMap : picked.Map;
            model.RecordCall(c, new CallInfo(kind, picked.Function, null, null, map, binding ?? picked.Binding, picked.Slots.Count, Types.Substitute(picked.Result, picked.Map)));
        }

        return typed;
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

            // a type a nested position bound already, `List<T>` of a `List<User>`, is the one the direct arguments meet: each is that
            // type or a data type derived from it (D6), whatever order the arguments stand in
            var nested = map.TryGetValue(p, out var bound) && !Types.IsError(bound);
            var common = nested ? bound : known.FirstOrDefault(t => known.All(u => Types.Same(u, t) || (u is DataType && t is DataType && Types.Assignable(u, t))));
            if (common is null || !known.All(u => Types.Same(u, common) || (u is DataType && common is DataType && Types.Assignable(u, common))))
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
            case MapType m when argument is MapType ma:
                return Unify(m.Key, ma.Key, map) && Unify(m.Value, ma.Value, map);
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
                if (!Types.IsError(type) && (type is not FunctionType f || f.Parameters.Count != pre.Lambda.Parameters.Count))
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

    /// <summary>
    /// F13 with F14: a call of a name with more than one function that fits none of them is <c>argument-mismatch</c> at the
    /// call's first line, the line E1 gives an error of the whole construct (the record's reading), with a
    /// line of the message per function and why it does not fit. A call of one function, a constructor or a function value
    /// reports its own error: its binding error, its <c>cannot-infer</c>, or the misfit of its first argument that does not
    /// fit, at that argument's line.
    /// </summary>
    private void ReportNoFit(List<Candidate> candidates, CallExpression c, List<Pre> pres)
    {
        if (candidates.Count > 1)
        {
            var reasons = candidates.Select(x => $"  `{Signature(x.Function!)}` at line {x.Function!.Line}: {WhyNot(x, pres)}");
            Report(ErrorIds.ArgumentMismatch, c.Line, $"the call fits none of the functions named `{candidates[0].Function!.Name}` (F13):\n{string.Join("\n", reasons)}");
            return;
        }

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
        if (pre.Lambda is null && bound.Slots[bound.Binding[index]].Mut && Types.Assignable(Types.NonNull(pre.Typed.Type) ?? pre.Typed.Type, parameter))
        {
            // a nullable variable among them, which no check makes a variable of the parameter's type (N7)
            Report(ErrorIds.TypeMismatch, pre.Argument.Line, $"a `{pre.Typed.Type}` passed as `mut` where a `mut {parameter}` is required: a `mut` parameter takes a variable of its own type, since the callee writes a `{parameter}` back into it (M7, T1)");
            return;
        }

        var argumentType = pre.Lambda is not null ? "a lambda" : $"a `{pre.Typed.Type}`";
        var id = pre.Lambda is not null ? ErrorIds.TypeMismatch : Types.MismatchId(pre.Typed.Type, parameter);
        Report(id, pre.Argument.Line, $"{argumentType} where a `{parameter}` is required (T1)");
    }

    /// <summary>A function as its declaration writes it, for a message: <c>void Show(int n)</c>, each default after its parameter (<c>int b = 0</c>).</summary>
    private static string Signature(FunctionSymbol f)
    {
        var typeParameters = f.TypeParameters.Count == 0 ? "" : $"<{string.Join(", ", f.TypeParameters.Select(p => p.Name))}>";
        var defaults = f.Declaration?.Parameters.Select(p => p.Default).ToList() ?? [];
        return $"{f.ReturnType} {f.Name}{typeParameters}({string.Join(", ", f.Parameters.Select((p, i) => $"{(p.Mut ? "mut " : "")}{p.Type} {p.Name}{(defaults.ElementAtOrDefault(i) is { } d ? " = " + DefaultText(d) : "")}"))})";
    }

    /// <summary>A default value for a message, from its tree: a literal, a name and the operators of a constant expression over them (F12); <c>...</c> for another form.</summary>
    private static string DefaultText(Expression e) => e switch
    {
        IntegerLiteral i => (i.Value.Radix == 16 ? "0x" : i.Value.Radix == 2 ? "0b" : "") + i.Value.Digits + i.Value.Suffix,
        RealLiteral r => r.Value.Text + r.Value.Suffix,
        UnitLiteral u => u.Value.Number + u.Value.Unit,
        BoolLiteral b => b.Value ? "true" : "false",
        CharLiteral c => System.Text.Rune.IsValid(c.CodePoint) ? Quoted(new System.Text.Rune(c.CodePoint).ToString(), '\'') : "...",
        StringLiteral { IsPlain: true } s => Quoted(string.Concat(s.Segments.Cast<TextSegment>().Select(t => t.Text)), '"'),
        NameExpression n => n.Name,
        ParenthesizedExpression p => $"({DefaultText(p.Inner)})",
        UnaryExpression u => Operator(u.Operator) + DefaultText(u.Operand),
        BinaryExpression b => $"{DefaultText(b.Left)} {Operator(b.Operator)} {DefaultText(b.Right)}",
        _ => "...",
    };

    /// <summary>A char or string default in its quotes, or <c>...</c> where the source writes it with an escape (L7, L16), so that the message keeps one line per function.</summary>
    private static string Quoted(string text, char quote) =>
        text.Any(ch => char.IsControl(ch) || ch == '\\' || ch == quote || (quote == '"' && ch == '{')) ? "..." : $"{quote}{text}{quote}";

    /// <summary>F13: why a function does not fit a call, as far as the checker knows it: its binding error (D11, T19), type arguments it cannot infer (T27), or its first argument that does not fit.</summary>
    private static string WhyNot(Candidate candidate, List<Pre> pres)
    {
        if (candidate.BindingError is not null)
        {
            return candidate.BindingError;
        }

        if (candidate.CannotInfer)
        {
            return "its type arguments are neither written nor inferable from the arguments (T27)";
        }

        var (index, parameter) = candidate.Misfit!.Value;
        var pre = pres[index];
        if (pre.Lambda is not null)
        {
            return $"argument {index + 1}, a lambda, does not fit a `{parameter}` (F11)";
        }

        if (candidate.Slots[candidate.Binding[index]].Mut && Types.Assignable(pre.Typed.Type, parameter))
        {
            return $"argument {index + 1}, a `{pre.Typed.Type}`, is no variable of the type `{parameter}` that a `mut` parameter writes back into (M7)";
        }

        var why = Types.MismatchId(pre.Typed.Type, parameter) switch
        {
            ErrorIds.NarrowingConversion => "a narrowing conversion (T7)",
            ErrorIds.SignMix => "a type of the other signedness (T23)",
            _ => "a value of another type (T1)",
        };
        return $"argument {index + 1} is a `{pre.Typed.Type}`{(pre.LiteralOnly ? ", the literal's own type for the pick (F14)," : "")} where a `{parameter}` is required: {why}";
    }

    /// <summary>The picked candidate: literals and lambdas typed by their parameters (L17, F11), the <c>mut</c> markers checked (M7).</summary>
    private void Commit(Candidate candidate, List<Pre> pres)
    {
        var mutPaths = new List<string>();
        for (var i = 0; i < pres.Count; i++)
        {
            var pre = pres[i];
            if (pre.Unread)
            {
                continue;
            }

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
            else if (pre.BySlot)
            {
                // a `null` on a type parameter that nothing inferred is chapter 4's `null`, which the checker does not read (N1); one on a
                // parameter whose type was an error carries none of its own (E1)
                var typed = CheckExpression(a.Value, Unwrap(a.Value) is NullLiteral && Types.IsError(type) && !Types.IsError(slot.Type) ? null : type);
                if (!typed.IsError && !Types.IsError(type) && !Types.Assignable(typed.Type, type))
                {
                    Report(Types.MismatchId(typed.Type, type), a.Line, $"a `{typed.Type}` where a `{type}` is required (T1)");
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

    /// <summary>M7, M9: whether the path after <c>mut</c> starts at an element of a list, which the checker does not read.</summary>
    private static bool IsElementPath(Expression value)
    {
        var root = Unwrap(value);
        while (root is MemberAccess { Conditional: false } m)
        {
            root = Unwrap(m.Target);
        }

        return root is IndexExpression;
    }

    /// <summary>
    /// M7: what stands after <c>mut</c> at a call is a <c>mut</c> variable or a path of data fields from
    /// one, and each path once: the same path twice, or a path and one it is a prefix of, would reach one
    /// value through two parameters; two fields of one variable are two values. A path through an index
    /// is not read (M9), and was refused before the pick.
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
        else if (paths.Any(other => Overlaps(other, path)))
        {
            Report(ErrorIds.MutAtCall, a.Line, $"`{path}` is passed as `mut` twice (M7)");
        }
        else
        {
            changes[a] = new Change(variable, path, a.Line, $"the call that takes `mut {path}` runs");
        }

        paths.Add(path);
        variable.Changed = true;
    }
}
