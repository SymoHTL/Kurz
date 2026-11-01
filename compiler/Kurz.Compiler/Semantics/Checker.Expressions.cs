using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    private static readonly BigInteger DecimalMax = new(decimal.MaxValue);

    /// <summary>
    /// The type of an expression, and its value where it is a constant. <paramref name="expected"/>
    /// is the type written or expected around it, which reaches its literals (L10, L17); with
    /// <paramref name="keepCases"/> a call keeps every case of its result instead of being
    /// unwrapped (O4); <paramref name="given"/> are the types a call passes the parameters of a lambda it takes, whose result the
    /// lambda's body gives (X8's <c>SortBy</c>).
    /// </summary>
    private Typed CheckExpression(Expression expression, KurzType? expected, bool keepCases = false, IReadOnlyList<KurzType>? given = null)
    {
        if (expressionDepth > 0)
        {
            inline.Add(expression);
        }

        expressionDepth++;
        var typed = CheckExpressionCore(expression, expected, keepCases, given);
        expressionDepth--;
        model.RecordType(expression, typed); // the last reading stands: a call's literal argument is read a second time, against the parameter of the function picked
        if (expressionDepth == 0)
        {
            CheckReads(expression); // M7, over the whole expression once its parts are read
        }

        return typed;
    }

    private Typed CheckExpressionCore(Expression expression, KurzType? expected, bool keepCases, IReadOnlyList<KurzType>? given)
    {
        switch (expression)
        {
            case IntegerLiteral i:
                return CheckInteger(i.Value, i.Line, expected, negative: false);
            case RealLiteral r:
                return CheckReal(r.Value, r.Line, expected, negative: false);
            case UnitLiteral u:
                return CheckUnit(u.Value, u.Line, expected, negative: false);
            case StringLiteral s:
                return CheckString(s);
            case CharLiteral c:
                return new Typed(Types.Char, new CharConstant(c.CodePoint));
            case BoolLiteral b:
                return new Typed(Types.Bool, new BoolConstant(b.Value));
            case NullLiteral when expected is PointerType p:
                return new Typed(p, new NullConstant()); // R10: a pointer can be `null`
            case NullLiteral when expected is not null && Types.IsError(expected):
                return Typed.Error; // the type it stands for was the error, which was reported (E1)
            case NullLiteral when expected is WeakRefType:
                return new Typed(Types.Null, new NullConstant()); // R3: a `weak T?` is nullable (R5)
            case NullLiteral when expected is not null && Types.NonNull(expected) is not null:
                return new Typed(Types.Null, new NullConstant()); // N1: the value of a nullable type's case `null`, which the site makes a value of that type
            case NullLiteral n:
                // N6: `null` is a value of nullable types and of pointers only, and has no type of its own that a value without one could take
                Report(ErrorIds.TypeMismatch, n.Line, expected is null
                    ? "`null` has no type of its own: a nullable type takes it, `string? name = null` (N6)"
                    : $"`null` where {Types.A(expected)} is required: `null` is a value of nullable types and pointers only (N6, N1)");
                return Typed.Error;
            case NameExpression n:
                return CheckName(n, expected);
            case ThisExpression t:
                return CheckThis(t); // K7
            case MemberAccess m:
                return CheckMember(m);
            case CallExpression c:
                {
                    var result = CheckCall(c, expected, keepCases);
                    return result.Type is WeakRefType ? new Typed(Types.Read(result.Type)) : result; // R3: a `weak T?` result reads as a strong `T?`
                }

            case IndexExpression i:
                return CheckIndex(i);
            case ListLiteral l when expected is not null && Types.IsError(expected):
                foreach (var item in l.Items)
                {
                    CheckArgumentLoosely(item); // the type it stands for was the error, which was reported (E1)
                }

                return Typed.Error;
            case ListLiteral l:
                return CheckListLiteral(l, expected);
            case UnaryExpression u:
                return CheckUnary(u, expected);
            case BinaryExpression b:
                return CheckBinary(b, expected);
            case RangeExpression r:
                CheckRange(r);
                Report(ErrorIds.Syntax, r.Line, "a range stands in a `for` loop (C8)");
                return Typed.Error;
            case LambdaExpression l when expected is not null && Types.IsError(expected):
                // a lambda where a type stands that was reported or is not read, the type of a C function (R17) among them: its misfit and
                // the missing types of its parameters come from that type and are not reported beside its line (E1); a parameter of no type
                // is error-typed, and the body is checked as any lambda's
                CheckLambda(l, null, atUnreadType: true);
                return Typed.Error;
            case LambdaExpression l when expected is CFunctionType:
                // R17: a lambda takes Kurz's calling convention and captures, so it is no value of a C function type; its body is checked as any lambda's
                Report(ErrorIds.TypeMismatch, l.Line, $"a lambda where {Types.A(expected)} is required: a value of a C function type is the name of a function of the C calling convention (R17, T1)");
                CheckLambda(l, null, atUnreadType: true);
                return Typed.Error;
            case LambdaExpression l:
                // a lambda where a nullable function type is required takes the types of that function type (N1)
                return CheckLambda(l, (expected is null ? null : Types.NonNull(expected) ?? expected) as FunctionType, given: given);
            case MatchExpression m:
                return new Typed(CheckMatch(m.Subject, m.Arms, isExpression: true, expected, m.Line).Type);
            case WithExpression w:
                return CheckWith(w);
            case ElseExpression e:
                return CheckElse(e, isStatement: false, expected);
            case ParenthesizedExpression p:
                return CheckExpression(p.Inner, expected, keepCases, given);
            default:
                throw new InvalidOperationException(expression.GetType().Name);
        }
    }

    /// <summary>Whether an expression is made only of literals and the operators of T18, T22 and T12, which a written or expected type reaches whole (L17).</summary>
    private static bool IsLiteralOnly(Expression e) => e switch
    {
        IntegerLiteral or RealLiteral or UnitLiteral or CharLiteral or BoolLiteral => true,
        StringLiteral s => s.IsPlain,
        UnaryExpression u => IsLiteralOnly(u.Operand),
        BinaryExpression b => b.Operator != TokenKind.QuestionQuestion && IsLiteralOnly(b.Left) && IsLiteralOnly(b.Right),
        ParenthesizedExpression p => IsLiteralOnly(p.Inner),
        _ => false,
    };

    /// <summary>
    /// The number or time type an expected type reaches a literal with: the type itself (L10, L17, T29), or, by the literal's kind, the one
    /// case of a union that is a number type for a number literal and the one that is a time type for a <paramref name="duration"/> literal,
    /// whatever cases of the other kind the union has (T8 with D9 and L17, the owner, 2026-10-21).
    /// </summary>
    private static PrimitiveType? Reach(KurzType? expected, bool duration = false) => expected switch
    {
        PrimitiveType p when Types.IsNumber(p) || Types.IsTime(p) => p,
        UnionType u => u.Cases.OfType<PrimitiveType>().Where(c => duration ? Types.IsTime(c) : Types.IsNumber(c)).ToList() is [var one] ? one : null,
        _ => null,
    };

    // ---- literals --------------------------------------------------------------------------------

    private Typed CheckInteger(IntegerLiteralValue literal, int line, KurzType? expected, bool negative)
    {
        var value = ParseInteger(literal.Digits, literal.Radix);
        if (negative)
        {
            value = -value;
        }

        return IntegerValue(value, literal.Suffix, line, expected);
    }

    private static BigInteger ParseInteger(string digits, int radix)
    {
        if (radix == 10)
        {
            return BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        var value = BigInteger.Zero;
        foreach (var c in digits)
        {
            var digit = c is >= '0' and <= '9' ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
            value = value * radix + digit;
        }

        return value;
    }

    /// <summary>L10: the type of an integer value by its suffix and its size, or the type the context reaches it with; <c>constant-overflow</c> where it fits neither.</summary>
    private Typed IntegerValue(BigInteger value, string suffix, int line, KurzType? expected)
    {
        var target = Reach(expected);
        if (suffix.Length == 0 && target is not null)
        {
            if (Types.IsInteger(target))
            {
                if (!Types.Fits(value, target))
                {
                    Report(ErrorIds.ConstantOverflow, line, $"{value} does not fit {Types.A(target)} (L10, T6)");
                    return Typed.Error;
                }

                return new Typed(target, new IntegerConstant(value));
            }

            if (Types.IsFloating(target))
            {
                var d = (double)value;
                if (double.IsInfinity(d))
                {
                    Report(ErrorIds.ConstantOverflow, line, "the literal lies outside the range of `double` (T6, L11)");
                    return Typed.Error;
                }

                return RealValue(d, target, line);
            }

            if (Types.IsDecimal(target))
            {
                if (BigInteger.Abs(value) > DecimalMax)
                {
                    Report(ErrorIds.ConstantOverflow, line, $"{value} does not fit a `decimal` (T6)");
                    return Typed.Error;
                }

                return new Typed(Types.Decimal, new DecimalConstant((decimal)value));
            }
        }

        PrimitiveType[] ladder = suffix switch
        {
            "U" => [Types.UInt, Types.ULong],
            "L" => [Types.Long, Types.ULong],
            "UL" => [Types.ULong],
            _ => [Types.Int, Types.Long, Types.ULong],
        };
        foreach (var type in ladder)
        {
            if (Types.Fits(value, type))
            {
                return new Typed(type, new IntegerConstant(value));
            }
        }

        Report(ErrorIds.ConstantOverflow, line, suffix.Length == 0 ? $"{value} fits no integer type (L10, T6)" : $"{value} fits none of the types the suffix `{suffix}` names (L10, T6)");
        return Typed.Error;
    }

    private Typed CheckReal(RealLiteralValue literal, int line, KurzType? expected, bool negative)
    {
        var target = realKeepsOwnType ? null : Reach(expected);
        if (literal.Suffix == "m" || (literal.Suffix.Length == 0 && target is not null && Types.IsDecimal(target)))
        {
            if (!decimal.TryParse(literal.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
            {
                Report(ErrorIds.ConstantOverflow, line, "the literal lies outside the range of `decimal` (T6, L11)");
                return Typed.Error;
            }

            return new Typed(Types.Decimal, new DecimalConstant(negative ? -m : m));
        }

        if (!double.TryParse(literal.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsInfinity(d))
        {
            Report(ErrorIds.ConstantOverflow, line, "the literal lies outside the range of `double` (T6, L11)");
            return Typed.Error;
        }

        var type = literal.Suffix == "f" ? Types.Float : literal.Suffix.Length == 0 && target is not null && Types.IsFloating(target) ? target : Types.Double;
        return RealValue(negative ? -d : d, type, line);
    }

    /// <summary>A floating-point value in its type, rounded to <c>float</c> where that is the type; <c>constant-overflow</c> for a literal beyond the range (L11).</summary>
    private Typed RealValue(double value, PrimitiveType type, int line)
    {
        if (type.Kind == Primitive.Float)
        {
            var f = (float)value;
            if (float.IsInfinity(f) && !double.IsInfinity(value))
            {
                Report(ErrorIds.ConstantOverflow, line, "the literal lies outside the range of `float` (T6, L11)");
                return Typed.Error;
            }

            return new Typed(Types.Float, new RealConstant(f));
        }

        return new Typed(type, new RealConstant(value));
    }

    private Typed CheckUnit(UnitLiteralValue literal, int line, KurzType? expected, bool negative)
    {
        var (factor, isTime) = literal.Unit switch
        {
            "ns" => (BigInteger.One, true),
            "us" => (new BigInteger(1_000), true),
            "ms" => (new BigInteger(1_000_000), true),
            "s" => (new BigInteger(1_000_000_000), true),
            "min" => (new BigInteger(60_000_000_000), true),
            "h" => (new BigInteger(3_600_000_000_000), true),
            "days" => (new BigInteger(86_400_000_000_000), true),
            "kb" => (new BigInteger(1024), false),
            "mb" => (new BigInteger(1024 * 1024), false),
            "gb" => (new BigInteger(1024L * 1024 * 1024), false),
            _ => throw new InvalidOperationException(literal.Unit),
        };
        var dot = literal.Number.IndexOf('.', StringComparison.Ordinal);
        var digits = literal.Number.Replace(".", "", StringComparison.Ordinal);
        var fraction = dot < 0 ? 0 : literal.Number.Length - dot - 1;
        var scaled = BigInteger.Parse(digits.Length == 0 ? "0" : digits, NumberStyles.None, CultureInfo.InvariantCulture) * factor;
        var divisor = BigInteger.Pow(10, fraction);
        if (!scaled.IsZero && scaled % divisor != 0)
        {
            Report(ErrorIds.InexactLiteral, line, isTime ? "the fraction does not fall on a whole nanosecond (L14)" : "the fraction does not fall on a whole byte (L14)");
            return Typed.Error;
        }

        var value = scaled / divisor;
        if (negative)
        {
            value = -value;
        }

        if (!isTime)
        {
            return IntegerValue(value, "", line, expected);
        }

        var target = Reach(expected, duration: true);
        if (target is not null && Types.IsDuration(target))
        {
            if (!Types.Fits(value, target))
            {
                Report(ErrorIds.ConstantOverflow, line, $"the duration does not fit {Types.A(target)} (T6, T29)");
                return Typed.Error;
            }

            return new Typed(target, new IntegerConstant(value));
        }

        if (Types.Fits(value, Types.Duration))
        {
            return new Typed(Types.Duration, new IntegerConstant(value));
        }

        if (Types.Fits(value, Types.LongDuration))
        {
            return new Typed(Types.LongDuration, new IntegerConstant(value));
        }

        Report(ErrorIds.ConstantOverflow, line, "the duration fits neither `duration` nor `longduration` (T6, T29)");
        return Typed.Error;
    }

    /// <summary>L6: every hole holds a value with a text (A2 to A15).</summary>
    private Typed CheckString(StringLiteral s)
    {
        var text = s.IsPlain ? new System.Text.StringBuilder() : null;
        foreach (var segment in s.Segments)
        {
            if (segment is HoleSegment hole)
            {
                var t = CheckExpression(hole.Expression, null);
                if (Types.IsVoid(t.Type))
                {
                    Report(ErrorIds.Syntax, hole.Expression.Line, "a call with a `void` success used as a value (O10)");
                }
                else if (!HasText(t.Type))
                {
                    Report(ErrorIds.NoText, hole.Expression.Line, $"{Types.A(t.Type)} has no text to put into a string (A4, A7, A10)");
                }
                else if (PrivateText(t.Type, hole.Expression.Line))
                {
                    // A9, A15: reported
                }
                else if (HoldsInstance(t.Type, []))
                {
                    CallRan(); // N9: the text of an instance is its `Text()`, a call (A5)
                }
                else if (HoldsRaw(t.Type, []))
                {
                    NotSupported(hole.Expression.Line, "the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)");
                }
            }
            else
            {
                text?.Append(((TextSegment)segment).Text);
            }
        }

        return new Typed(Types.String, text is null ? null : new StringConstant(text.ToString()));
    }

    /// <summary>A2 to A15: whether a value of the type has a text for <c>print</c> and an interpolation. The error type has one, so that nothing more is reported.</summary>
    private static bool HasText(KurzType type) => HasText(type, []);

    /// <summary>
    /// A data type has a text when each type argument that a field reaches has one, and its fields have one with its own
    /// type parameters standing for those arguments. The fields of a type are read on the way down and not again inside
    /// it, so that a type that holds itself ends, and each use of it is still judged on its own type arguments.
    /// </summary>
    private static bool HasText(KurzType type, List<DataSymbol> seen) => type switch
    {
        PrimitiveType or EnumType or NullType => true, // A4: null shows `null`
        HasherType => true, // H6: the name of its type
        ErrorType => true,
        ListType l => HasText(l.Element, seen),
        MapType m => HasText(m.Value, seen), // A6, M9: every type a map takes as its key has a text; a function value and a handle, which have none, are no key
        UnionType u => u.Cases.All(c => HasText(c, seen)),
        DataType d => ArgumentsHaveText(d, seen) && (seen.Contains(d.Symbol) || Fields(d.Symbol, seen)),
        ClassType c => c.Symbol.TextMethod is not null, // A4, A5: an instance has the text its class declares, and none else
        WeakRefType w => HasText(w.Target, seen), // A4 with R3: the text of what its read gives
        PointerType or CFunctionType => true, // its text is not read (HoldsRaw), and decides nothing here
        TypeParameterType p => seen.Any(s => s.TypeParameters.Contains(p.Symbol)), // a parameter of a data type whose fields are read: its argument is judged where the type is used
        _ => false, // a function value; a type parameter of a function, which has no limit the checker reads (A10)
    };

    private static bool ArgumentsHaveText(DataType d, List<DataSymbol> seen) =>
        d.Symbol.TypeParameters.Zip(d.TypeArguments).All(pair => !d.Symbol.Fields.Any(f => Types.Mentions(f.Type, pair.First)) || HasText(pair.Second, seen));

    private static bool Fields(DataSymbol symbol, List<DataSymbol> seen)
    {
        seen.Add(symbol);
        var result = symbol.Fields.All(f => HasText(f.Type, seen));
        seen.RemoveAt(seen.Count - 1);
        return result;
    }

    private static KurzType SubstituteData(DataType d, KurzType type)
    {
        if (d.TypeArguments.Count == 0)
        {
            return type;
        }

        var map = d.Symbol.TypeParameters.Zip(d.TypeArguments).ToDictionary(p => p.First, p => p.Second);
        return Types.Substitute(type, map);
    }

    // ---- names and members -----------------------------------------------------------------------

    /// <summary>A name as a value: a variable, the one value of a data type without fields (D8), or the name of a function of the C calling convention as a value of its C function type (R17).</summary>
    private Typed CheckName(NameExpression n, KurzType? expected = null)
    {
        var (variable, _) = Lookup(n.Name);
        if (variable is not null)
        {
            variable.Read = true;
            model.RecordName(n, variable);
            if (ReferenceEquals(variable, model.Arguments))
            {
                model.RecordArgumentsRead(n.Line); // S13
            }

            stateRead |= readingConstant && variable.Kind == VariableKind.Static;
            RequireRawForState(variable, n.Line);
            return Narrowed(n, new Typed(Types.Read(variable.Type))); // N3; R3: a `weak` place reads as a strong `T?`
        }

        if (ImplicitField(n.Name) is { } implicitField)
        {
            return ReadImplicitField(n, implicitField); // K7: the field of `this`
        }

        if (ImplicitMethods(n.Name) is not null)
        {
            NotSupported(n.Line, "a method as a value, named without a call (K7, F11)");
            return Typed.Error;
        }

        if (frame.Reserved is not null && frame.Reserved.Contains(n.Name))
        {
            Report(ErrorIds.UnknownName, n.Line, $"`{n.Name}` is a top-level variable, which a top-level function does not read (F16, V10)");
            return Typed.Error;
        }

        if (classes.ContainsKey(n.Name))
        {
            Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is a class, and no value stands here: an instance is made by `{n.Name}(...)` (K1; E4)");
            return Typed.Error;
        }

        if (dataTypes.TryGetValue(n.Name, out var data))
        {
            if (!data.HasFields)
            {
                return new Typed(new DataType(data, []), new UnitConstant()); // D8: the bare name is the one value
            }

            Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is a type, and no value stands here (D2; E4)");
            return Typed.Error;
        }

        if (enums.ContainsKey(n.Name))
        {
            var example = enums[n.Name].Values is [var first, ..] ? $"`{n.Name}.{first}`" : "as its name, a dot and a value's name";
            Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is a type, and no value stands here: a value of it is written {example} (D12; E4)");
            return Typed.Error;
        }

        if (IsUnreadName(n.Name))
        {
            return Typed.Error;
        }

        if (cFunctions.TryGetValue(n.Name, out var cFunction))
        {
            model.RecordFunctionName(n, cFunction);
            return CFunctionValue(cFunction, n.Line);
        }

        if (functions.TryGetValue(n.Name, out var named) && named is [{ TakesC: true } own])
        {
            model.RecordFunctionName(n, own);
            return CFunctionValue(own, n.Line); // a function of the package's own in the C calling convention (R12, R16)
        }

        if (expected is CFunctionType && functions.ContainsKey(n.Name))
        {
            Report(ErrorIds.TypeMismatch, n.Line, $"`{n.Name}` takes Kurz's calling convention, and {Types.A(expected)} is required: a value of a C function type is the name of a function of the C calling convention (R17, T1)");
            return Typed.Error;
        }

        if (functions.ContainsKey(n.Name) || n.Name == "print")
        {
            Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is a function, and no value stands here (F11)");
            return Typed.Error;
        }

        if (Types.ByName.ContainsKey(n.Name) || n.Name is "List" or "Map" or "Bytes")
        {
            Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` is a type, and no value stands here (T10; E4)");
            return Typed.Error;
        }

        if (n.Name == "args")
        {
            Report(ErrorIds.UnknownName, n.Line, "`args` is seen by the top-level code alone: a function takes it as a parameter, or reads `Environment.Args()`, the list with the program's name first (S13, V10)");
            return Typed.Error;
        }

        if (UnreadIntrinsic(n.Name, n.Line))
        {
            return Typed.Error;
        }

        Report(ErrorIds.UnknownName, n.Line, $"no `{n.Name}` is visible here (V10)");
        return Typed.Error;
    }

    private Typed CheckMember(MemberAccess m)
    {
        if (m.Conditional || InChain(m.Target))
        {
            return CheckChainMember(m); // N5
        }

        if (m.Target is NameExpression n && Lookup(n.Name).Variable is null && !(frame.Reserved?.Contains(n.Name) ?? false))
        {
            if (IsUnreadName(n.Name))
            {
                return Typed.Error;
            }

            if (enums.TryGetValue(n.Name, out var enumSymbol))
            {
                return EnumValue(enumSymbol, m.Member, m.Line);
            }

            if (classes.ContainsKey(n.Name) && ImplicitField(n.Name) is null)
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` is a class and has no member `{m.Member}` of its own: a field is read from an instance (K1, V10)");
                return Typed.Error;
            }

            if (dataTypes.TryGetValue(n.Name, out var data) && data.HasFields)
            {
                if (HasBody(data))
                {
                    NotSupported(m.Line, "a method of a data type (D16)"); // a member its body declares, which the checker does not read
                    return Typed.Error;
                }

                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` is a type and has no member `{m.Member}` (D1, V10)");
                return Typed.Error;
            }

            if (n.Name == "timestamp" && m.Member == "Now")
            {
                model.RecordClockRead(m); // S15: the wall clock (section 12, the owner, 2026-10-30, "`timestamp.Now`")
                return new Typed(Types.Timestamp);
            }

            if (IsBuiltInName(n.Name) || functions.ContainsKey(n.Name))
            {
                Report(ErrorIds.UnknownName, m.Line, $"`{n.Name}` has no member `{m.Member}` (V10)");
                return Typed.Error;
            }
        }

        var target = CheckExpression(m.Target, null);
        return Narrowed(m, MemberOf(target.Type, m.Member, m.Line)); // N3
    }

    /// <summary>D12: <c>Plan.Free</c>, a value of the enum, a constant (section 4); <c>From</c> is called (D18), and any other name is no value of it (V10).</summary>
    private Typed EnumValue(EnumSymbol symbol, string member, int line)
    {
        var index = symbol.IndexOf(member);
        if (index >= 0)
        {
            return new Typed(new EnumType(symbol), new EnumConstant(index));
        }

        if (member == "From")
        {
            Report(ErrorIds.Syntax, line, $"`{symbol.Name}.From` is a function, and no value stands here: it is called, `{symbol.Name}.From(n)` (D18, F11)");
            return Typed.Error;
        }

        if (symbol.Refused && symbol.Declaration.Values.Any(v => v.Name == member))
        {
            return Typed.Error; // a value its declaration wrote and that was reported there (E1)
        }

        Report(ErrorIds.UnknownName, line, $"`{symbol.Name}` has no value `{member}` (D12, V10)");
        return Typed.Error;
    }

    /// <summary>
    /// The members the reference gives a type: a string's <c>Bytes</c> and <c>Chars</c> (T15), a list's
    /// <c>Count</c> (M9), a data value's fields (D1), a listener's <c>Port</c> (S9). A member no rule gives the type is a name that is not
    /// visible here, V10's <c>unknown-name</c> (the reading in <c>compiler/README.md</c>); a member of a
    /// union is <c>type-mismatch</c>, since a union is one of its cases only after a <c>match</c> (D9). A
    /// type parameter has members only through a limit (T19), so a member of one without a limit is
    /// <c>unknown-name</c>, and of one with a limit, which the checker does not read, <c>not supported</c>.
    /// </summary>
    private Typed MemberOf(KurzType type, string member, int line)
    {
        switch (type)
        {
            case ErrorType:
                return Typed.Error;
            case PrimitiveType { Kind: Primitive.String }:
                if (member == "Bytes")
                {
                    return new Typed(new ListType(Types.Byte));
                }

                if (member == "Chars")
                {
                    return new Typed(new ListType(Types.Char));
                }

                if (member == "Pointer")
                {
                    RequireRaw(line, "`s.Pointer`", "R15");
                    return new Typed(new PointerType(Types.Byte)); // R15: the string's UTF-8 bytes
                }

                break;
            case ListType list:
                if (member == "Count")
                {
                    return new Typed(Types.Int);
                }

                if (member == "Pointer")
                {
                    var pointer = PointerTo(list.Element, line, "`l.Pointer`"); // R15: a list of a type a pointer points to
                    if (!Types.IsError(pointer))
                    {
                        RequireRaw(line, "`l.Pointer`", "R15");
                    }

                    return new Typed(pointer);
                }

                break;
            case MapType when member == "Count":
                return new Typed(Types.Int); // M9: the count of its entries, as C#'s `Dictionary.Count` (section 4, the owner, 2026-10-30)
            case DataType d:
                {
                    var field = d.Symbol.Field(member);
                    if (field is not null)
                    {
                        return new Typed(Types.Read(SubstituteData(d, field.Type))); // R3
                    }

                    if (HasBody(d.Symbol))
                    {
                        NotSupported(line, "a method of a data type (D16)");
                        return Typed.Error;
                    }

                    break;
                }

            case ClassType c:
                return ClassMember(c, member, line); // K1, F9
            case EnumType e when member == "Number":
                if (e.Symbol.Refused)
                {
                    return new Typed(Types.Int); // the declaration's error was reported there (E1)
                }

                if (!e.Symbol.Numbered)
                {
                    Report(ErrorIds.EnumNumber, line, $"`{e}` numbers none of its values, so a value of it has no `Number` (D20)");
                    return Typed.Error;
                }

                return new Typed(Types.Int); // D18
            case HandleType { Kind: Handle.TcpListener } when member == "Port":
                return new Typed(Types.Int); // S9
            case UnionType when Types.NonNull(type) is not null:
                Report(ErrorIds.NullableUnchecked, line, $"`{member}` is read from {Types.A(type)} without a check (N2)");
                return Typed.Error;
            case UnionType:
                Report(ErrorIds.TypeMismatch, line, $"{Types.A(type)} is one of its cases only after a `match` (D9)");
                return Typed.Error;
            case TypeParameterType { Symbol.HasLimit: true }:
                NotSupported(line, "a member of a type parameter with a limit (T19)");
                return Typed.Error;
            default:
                break;
        }

        Report(ErrorIds.UnknownName, line, $"{Types.A(type)} has no member `{member}` (V10)");
        return Typed.Error;
    }

    /// <summary>D16: whether the type or a base of it writes a body, whose members the checker does not read.</summary>
    private static bool HasBody(DataSymbol symbol)
    {
        for (DataSymbol? s = symbol; s is not null; s = s.Base)
        {
            if (s.Declaration.Members is { Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    private Typed CheckIndex(IndexExpression i)
    {
        if (i.Conditional)
        {
            return CheckConditionalIndex(i); // N5
        }

        if (InChain(i.Target))
        {
            // N5: C# reads an index after `?.` inside the chain, which section 4 does not say
            NotSupported(i.Line, "an index after `?.` or `?[` (N5)");
            CheckLoosely(i.Target, i.Index);
            return Typed.Error;
        }

        var target = CheckExpression(i.Target, null);
        if (i.Index is RangeExpression range)
        {
            if (Unchecked(target, i.Line, "sliced"))
            {
                CheckLoosely(range);
                return Typed.Error;
            }

            return CheckPart(i, target, range);
        }

        if (target.Type is MapType map)
        {
            return CheckMapRead(i, map);
        }

        var index = CheckExpression(i.Index, target.Type is PointerType ? null : Types.Int); // R10: an offset is an `int` or a `long`, so a literal takes the type L10 gives it
        if (target.Type is PointerType)
        {
            return CheckPointerIndex(i, target, index); // R10
        }

        if (target.Type is ListType && Unchecked(index, i.Line, "used as an index"))
        {
            return Typed.Error;
        }

        if (target.Type is ListType && !index.IsError && !Types.IsInteger(index.Type))
        {
            Report(ErrorIds.TypeMismatch, i.Line, $"an index is an integer, not {Types.A(index.Type)} (M9, T2)");
        }

        if (Unchecked(target, i.Line, "indexed"))
        {
            return Typed.Error;
        }

        switch (target.Type)
        {
            case ErrorType:
                return Typed.Error;
            case PrimitiveType { Kind: Primitive.String }:
                Report(ErrorIds.StringIndex, i.Line, "a string is indexed through `.Bytes` or `.Chars` (T15)");
                return Typed.Error;
            case ListType l:
                return new Typed(Types.Read(l.Element)); // R3: a `weak` item reads as a strong `T?`
            default:
                Report(ErrorIds.TypeMismatch, i.Line, $"{Types.A(target.Type)} is not indexed (M9)");
                return Typed.Error;
        }
    }

    /// <summary>
    /// M9: <c>map[key]</c>, the value of the key's entry or <c>null</c>, of the nullable type of the map's values (N1), which is the
    /// values' own type where they are nullable, so that a missing entry and a <c>null</c> one read alike (section 4, the owner,
    /// 2026-10-30, "Flatten, as C#").
    /// </summary>
    private Typed CheckMapRead(IndexExpression i, MapType map)
    {
        CheckKey(i.Index, map);
        return new Typed(Types.Nullable(map.Value));
    }

    /// <summary>M9: the key of an index into the map, read or written.</summary>
    private void CheckKey(Expression index, MapType map)
    {
        var key = CheckExpression(index, map.Key);
        if (!key.IsError && !Types.Assignable(key.Type, map.Key))
        {
            ReportMismatch(key.Type, map.Key, index.Line, $"{Types.A(key.Type)} where a key of `{map}`, {Types.A(map.Key)}, is required", "M9");
        }
    }

    /// <summary>
    /// M9: <c>xs[a..&lt;b]</c> and <c>xs[a..b]</c>, a new list of the items from position <c>a</c> to <c>b</c>, whose ends are read as
    /// C8 reads a range's, <c>reversed-range</c> among them (section 4, the owner, 2026-10-29, "A List&lt;T&gt;, a value"); a part of a
    /// string is <c>string-index</c>, as its index is (T15).
    /// </summary>
    private Typed CheckPart(IndexExpression i, Typed target, RangeExpression range)
    {
        RecordThrough(i.Index, new Typed(CheckRange(range))); // a range has the type of its numbers (C8)
        switch (target.Type)
        {
            case ErrorType:
                return Typed.Error;
            case PrimitiveType { Kind: Primitive.String }:
                Report(ErrorIds.StringIndex, i.Line, "a string is indexed through `.Bytes` or `.Chars` (T15)");
                return Typed.Error;
            case ListType l:
                return new Typed(l); // an end that is no integer was reported by the range, and the part is still a list (E1)
            default:
                Report(ErrorIds.TypeMismatch, i.Line, $"a part is taken of a list, not of {Types.A(target.Type)} (M9)");
                return Typed.Error;
        }
    }

    // ---- operators -------------------------------------------------------------------------------

    private Typed CheckUnary(UnaryExpression u, KurzType? expected)
    {
        if (u.Operator == TokenKind.Star)
        {
            return CheckDeref(u); // R10
        }

        if (u.Operator == TokenKind.Ampersand)
        {
            return CheckAddress(u); // R10
        }

        if (u.Operator == TokenKind.Minus)
        {
            // L17: a `-` written directly before a literal belongs to the literal
            switch (u.Operand)
            {
                case IntegerLiteral i:
                    return CheckInteger(i.Value, i.Line, expected, negative: true);
                case RealLiteral r:
                    return CheckReal(r.Value, r.Line, expected, negative: true);
                case UnitLiteral l:
                    return CheckUnit(l.Value, l.Line, expected, negative: true);
                default:
                    break;
            }
        }

        // L17: an operand made of literals takes the type the context reaches it with and folds in it, without T28's promotion, which is
        // for the operand of a run-time operation; so `short s = -(5)` is a `short`, as `short s = -5` is
        var literal = IsLiteralOnly(u.Operand);
        var operand = CheckExpression(u.Operand, u.Operator != TokenKind.Bang && literal ? expected : null);
        if (operand.IsError || Unchecked(operand, u.Line, $"under `{Operator(u.Operator)}`"))
        {
            return Typed.Error;
        }

        switch (u.Operator)
        {
            case TokenKind.Minus:
                if (Types.IsUnsigned(operand.Type) && !Types.IsNarrow(operand.Type))
                {
                    // T9 and T23 judge two operands; whether `-` on one `uint` or `ulong` value is an error or wraps no rule says. `-5U` and
                    // `uint u = -1` are no such value: the `-` belongs to the literal (L17)
                    NotSupported(u.Line, $"`-` on {Types.A(operand.Type)}, which no rule gives a meaning");
                    return Typed.Error;
                }

                if (Types.IsNumber(operand.Type) || Types.IsDuration(operand.Type))
                {
                    var type = Types.IsNarrow(operand.Type) && !literal ? Types.Int : (PrimitiveType)operand.Type; // T28: a narrow operand is promoted under every operator
                    return Fold(type, u.Line, operand.Constant switch
                    {
                        IntegerConstant c => new IntegerConstant(-c.Value),
                        RealConstant c => new RealConstant(-c.Value),
                        DecimalConstant c => new DecimalConstant(-c.Value),
                        _ => null,
                    });
                }

                break;
            case TokenKind.Bang:
                if (operand.Type is PrimitiveType { Kind: Primitive.Bool })
                {
                    return new Typed(Types.Bool, operand.Constant is BoolConstant b ? new BoolConstant(!b.Value) : null);
                }

                break;
            case TokenKind.Tilde:
                if (Types.IsInteger(operand.Type))
                {
                    var type = Types.IsNarrow(operand.Type) && !literal ? Types.Int : (PrimitiveType)operand.Type;
                    return new Typed(type, operand.Constant is IntegerConstant c ? new IntegerConstant(Types.IsUnsigned(type) ? Types.Range(type).Max - c.Value : -c.Value - 1) : null);
                }

                break;
            default:
                throw new InvalidOperationException(u.Operator.ToString());
        }

        Report(ErrorIds.TypeMismatch, u.Line, $"`{Operator(u.Operator)}` takes no `{operand.Type}` (T18, T22)");
        return Typed.Error;
    }

    private static string Operator(TokenKind kind) => kind switch
    {
        TokenKind.Plus => "+",
        TokenKind.Minus => "-",
        TokenKind.Star => "*",
        TokenKind.Slash => "/",
        TokenKind.Percent => "%",
        TokenKind.PlusPercent => "+%",
        TokenKind.MinusPercent => "-%",
        TokenKind.StarPercent => "*%",
        TokenKind.EqualEqual => "==",
        TokenKind.NotEqual => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        TokenKind.AmpersandAmpersand => "&&",
        TokenKind.BarBar => "||",
        TokenKind.Bang => "!",
        TokenKind.Ampersand => "&",
        TokenKind.Bar => "|",
        TokenKind.Caret => "^",
        TokenKind.Tilde => "~",
        TokenKind.LessLess => "<<",
        TokenKind.GreaterGreater => ">>",
        TokenKind.QuestionQuestion => "??",
        _ => kind.ToString(),
    };

    private enum OperatorClass
    {
        Arithmetic,
        Wrapping,
        Comparison,
        Logical,
        Bits,
        Shift,
    }

    private static OperatorClass ClassOf(TokenKind kind) => kind switch
    {
        TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent => OperatorClass.Arithmetic,
        TokenKind.PlusPercent or TokenKind.MinusPercent or TokenKind.StarPercent => OperatorClass.Wrapping,
        TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => OperatorClass.Comparison,
        TokenKind.AmpersandAmpersand or TokenKind.BarBar => OperatorClass.Logical,
        TokenKind.Ampersand or TokenKind.Bar or TokenKind.Caret => OperatorClass.Bits,
        TokenKind.LessLess or TokenKind.GreaterGreater => OperatorClass.Shift,
        _ => throw new InvalidOperationException(kind.ToString()),
    };

    /// <summary>
    /// The two operands, with L17's reach: a written or expected type reaches both when both are
    /// made of literals; a literal operand takes the type of the other operand when that one has
    /// one; the count of a shift is an <c>int</c>.
    /// </summary>
    private (Typed Left, Typed Right) Operands(Expression left, Expression right, KurzType? expected, OperatorClass cls)
    {
        var leftLiteral = IsLiteralOnly(left);
        var rightLiteral = IsLiteralOnly(right);
        var reach = cls is OperatorClass.Comparison or OperatorClass.Logical ? null : expected;
        if (cls == OperatorClass.Shift)
        {
            var l = CheckExpression(left, leftLiteral ? reach : null);
            var r = CheckExpression(right, Types.Int);
            return (l, r);
        }

        if (cls == OperatorClass.Logical)
        {
            return (CheckExpression(left, Types.Bool), CheckExpression(right, Types.Bool));
        }

        if (leftLiteral && !rightLiteral)
        {
            var r = CheckExpression(right, null);
            var l = CheckExpression(left, Reach(r.Type));
            return (l, r);
        }

        if (rightLiteral && !leftLiteral)
        {
            var l = CheckExpression(left, null);
            var r = CheckExpression(right, Reach(l.Type));
            return (l, r);
        }

        return (CheckExpression(left, leftLiteral ? reach : null), CheckExpression(right, rightLiteral ? reach : null));
    }

    private Typed CheckBinary(BinaryExpression b, KurzType? expected)
    {
        if (b.Operator == TokenKind.QuestionQuestion)
        {
            return CheckCoalesce(b); // N4
        }

        var cls = ClassOf(b.Operator);
        if (cls == OperatorClass.Comparison && CompareWithNull(b) is { } withNull)
        {
            return withNull; // R10: a pointer compares with `null`; N3: a nullable value does
        }

        var (left, right) = cls == OperatorClass.Logical ? Logical(b) : Operands(b.Left, b.Right, expected, cls);
        if (b.Operator is TokenKind.EqualEqual or TokenKind.NotEqual && !left.IsError && !right.IsError && (Types.NonNull(left.Type) is not null || Types.NonNull(right.Type) is not null))
        {
            // section 4, the owner, 2026-10-30, "Lift == and !=": `null` equals `null` alone, and two values compare as their types do (N10)
            return Comparison(b, left with { Type = Types.NonNull(left.Type) ?? left.Type }, right with { Type = Types.NonNull(right.Type) ?? right.Type });
        }

        if (left.IsError || right.IsError || Unchecked(left, b.Line, $"under `{Operator(b.Operator)}`") || Unchecked(right, b.Line, $"under `{Operator(b.Operator)}`"))
        {
            return cls is OperatorClass.Comparison or OperatorClass.Logical ? new Typed(Types.Bool) : Typed.Error;
        }

        if ((left.Type is PointerType || right.Type is PointerType) && PointerOperation(b, left, right) is { } pointer)
        {
            return pointer; // R10, R14
        }

        var op = Operator(b.Operator);
        switch (cls)
        {
            case OperatorClass.Arithmetic:
                return Arithmetic(b, left, right);
            case OperatorClass.Wrapping:
                {
                    if (!Types.IsInteger(left.Type) || !Types.IsInteger(right.Type))
                    {
                        Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes integer operands, not `{left.Type}` and `{right.Type}` (T12)");
                        return Typed.Error;
                    }

                    var common = Types.CommonWrapping((PrimitiveType)left.Type, (PrimitiveType)right.Type);
                    if (common is null)
                    {
                        Report(ErrorIds.SignMix, b.Line, $"`{op}` between {Types.A(left.Type)} and {Types.A(right.Type)} mixes the signedness (T9, T28)");
                        return Typed.Error;
                    }

                    common = ConstantType(common, b, left, right); // L17: two literals of one type wrap in that type, `byte b = 200 +% 100` is 44
                    if (left.Constant is IntegerConstant l && right.Constant is IntegerConstant r)
                    {
                        var raw = b.Operator switch
                        {
                            TokenKind.PlusPercent => l.Value + r.Value,
                            TokenKind.MinusPercent => l.Value - r.Value,
                            _ => l.Value * r.Value,
                        };
                        return new Typed(common, new IntegerConstant(Wrap(raw, common)));
                    }

                    return new Typed(common);
                }

            case OperatorClass.Comparison:
                return Comparison(b, left, right);
            case OperatorClass.Logical:
                {
                    if (left.Type is not PrimitiveType { Kind: Primitive.Bool } || right.Type is not PrimitiveType { Kind: Primitive.Bool })
                    {
                        Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes two `bool`s, not `{left.Type}` and `{right.Type}` (T18)");
                        return new Typed(Types.Bool);
                    }

                    if (left.Constant is BoolConstant l && right.Constant is BoolConstant r)
                    {
                        return new Typed(Types.Bool, new BoolConstant(b.Operator == TokenKind.AmpersandAmpersand ? l.Value && r.Value : l.Value || r.Value));
                    }

                    return new Typed(Types.Bool);
                }

            case OperatorClass.Bits:
                {
                    if (!Types.IsInteger(left.Type) || !Types.IsInteger(right.Type))
                    {
                        Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` works on the bits of integers, not on `{left.Type}` and `{right.Type}` (T22)");
                        return Typed.Error;
                    }

                    var common = Types.CommonInteger((PrimitiveType)left.Type, (PrimitiveType)right.Type);
                    if (common is null)
                    {
                        Report(ErrorIds.SignMix, b.Line, $"`{op}` between {Types.A(left.Type)} and {Types.A(right.Type)} mixes the signedness (T9)");
                        return Typed.Error;
                    }

                    common = ConstantType(common, b, left, right);
                    if (left.Constant is IntegerConstant l && right.Constant is IntegerConstant r)
                    {
                        var value = b.Operator switch
                        {
                            TokenKind.Ampersand => l.Value & r.Value,
                            TokenKind.Bar => l.Value | r.Value,
                            _ => l.Value ^ r.Value,
                        };
                        return new Typed(common, new IntegerConstant(value));
                    }

                    return new Typed(common);
                }

            case OperatorClass.Shift:
                {
                    if (!Types.IsInteger(left.Type) || !Types.IsInteger(right.Type))
                    {
                        Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` shifts the bits of an integer by an integer count, not `{left.Type}` by `{right.Type}` (T22)");
                        return Typed.Error;
                    }

                    // T22, T28: a narrow left operand is promoted to `int`, so its width is 32 and the count is taken modulo 32; two operands
                    // made of literals fold in the type they take (L17). A shift is not arithmetic in the sense of T4 to T6: the bits that
                    // leave the type are dropped at every width, and no fold of one is `constant-overflow`
                    var type = IsLiteralOnly(b.Left) && IsLiteralOnly(b.Right) ? (PrimitiveType)left.Type : Types.IsNarrow(left.Type) ? Types.Int : (PrimitiveType)left.Type;
                    if (left.Constant is IntegerConstant l && right.Constant is IntegerConstant r)
                    {
                        var width = Types.IsNarrow(type) ? 32 : Types.Width(type);
                        var count = (int)(((r.Value % width) + width) % width);
                        var value = b.Operator == TokenKind.LessLess ? Wrap(l.Value << count, type) : l.Value >> count;
                        return new Typed(type, new IntegerConstant(value));
                    }

                    return new Typed(type);
                }

            default:
                throw new InvalidOperationException(cls.ToString());
        }
    }

    /// <summary>T18, T28, T9, T23, T13, T29: the type of <c>+ - * / %</c> on two operands, and the fold of two constants (T6, T25).</summary>
    private Typed Arithmetic(BinaryExpression b, Typed left, Typed right)
    {
        var op = Operator(b.Operator);
        PrimitiveType? type = null;
        if (Types.IsNumber(left.Type) && Types.IsNumber(right.Type))
        {
            var l = (PrimitiveType)left.Type;
            var r = (PrimitiveType)right.Type;
            if (Types.IsInteger(l) && Types.IsInteger(r))
            {
                type = Types.CommonInteger(l, r);
                if (type is null)
                {
                    Report(ErrorIds.SignMix, b.Line, $"`{op}` between {Types.A(l)} and {Types.A(r)} mixes the signedness (T9)");
                    return Typed.Error;
                }
            }
            else if (Types.IsDecimal(l) || Types.IsDecimal(r))
            {
                if (Types.IsFloating(l) || Types.IsFloating(r))
                {
                    Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes no `decimal` beside {Types.A((Types.IsFloating(l) ? l : r))} (T18, T23)");
                    return Typed.Error;
                }

                type = Types.Decimal;
            }
            else
            {
                type = l.Kind == Primitive.Double || r.Kind == Primitive.Double ? Types.Double : Types.Float;
            }
        }
        else if (Types.IsTime(left.Type) && Types.IsTime(right.Type))
        {
            type = TimeArithmetic(b.Operator, (PrimitiveType)left.Type, (PrimitiveType)right.Type);
        }

        if (type is null)
        {
            Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes no `{left.Type}` and `{right.Type}` (T18, T13)");
            return Typed.Error;
        }

        type = ConstantType(type, b, left, right);
        if (right.Constant is IntegerConstant { Value.IsZero: true } or DecimalConstant { Value: 0m } && b.Operator is TokenKind.Slash or TokenKind.Percent && (Types.IsInteger(type) || Types.IsDecimal(type)))
        {
            Report(ErrorIds.ConstantDivideByZero, b.Line, $"`{op}` by a constant zero can never succeed (T25)");
            return Typed.Error;
        }

        if (left.Constant is null || right.Constant is null)
        {
            return new Typed(type);
        }

        switch (left.Constant, right.Constant)
        {
            case (IntegerConstant l, IntegerConstant r):
                {
                    var value = b.Operator switch
                    {
                        TokenKind.Plus => l.Value + r.Value,
                        TokenKind.Minus => l.Value - r.Value,
                        TokenKind.Star => l.Value * r.Value,
                        TokenKind.Slash => BigInteger.Divide(l.Value, r.Value),
                        _ => BigInteger.Remainder(l.Value, r.Value),
                    };
                    return Fold(type, b.Line, new IntegerConstant(value));
                }

            case (RealConstant l, RealConstant r):
                {
                    var value = b.Operator switch
                    {
                        TokenKind.Plus => l.Value + r.Value,
                        TokenKind.Minus => l.Value - r.Value,
                        TokenKind.Star => l.Value * r.Value,
                        TokenKind.Slash => l.Value / r.Value,
                        _ => Math.IEEERemainder(l.Value, r.Value) is var rem && double.IsNaN(rem) ? rem : l.Value % r.Value,
                    };
                    return new Typed(type, new RealConstant(type.Kind == Primitive.Float ? (float)value : value));
                }

            case (DecimalConstant l, DecimalConstant r):
                try
                {
                    var value = b.Operator switch
                    {
                        TokenKind.Plus => l.Value + r.Value,
                        TokenKind.Minus => l.Value - r.Value,
                        TokenKind.Star => l.Value * r.Value,
                        TokenKind.Slash => l.Value / r.Value,
                        _ => Remainder(l.Value, r.Value),
                    };

                    // a zero these compute has no sign, where System.Decimal can set it (the owner, 2026-10-28, "None, as rust_decimal")
                    return new Typed(type, new DecimalConstant(value == 0m && decimal.IsNegative(value) ? decimal.Negate(value) : value));
                }
                catch (OverflowException)
                {
                    Report(ErrorIds.ConstantOverflow, b.Line, $"the result of `{op}` leaves the range of `decimal` (T6)");
                    return Typed.Error;
                }

            default:
                return new Typed(type); // a mixed pair after a reach that left one side its own type: typed, not folded
        }
    }

    /// <summary>
    /// <c>%</c> on two <c>decimal</c>s (T30): the exact remainder at the larger scale, with the dividend's sign, which always fits;
    /// System.Decimal raises <c>OverflowException</c> for some that fit, <c>4294967295m % 0.9999999999999999999999999999m</c> among
    /// them, and those are computed here (the owner, 2026-10-28, "Exact remainder").
    /// </summary>
    private static decimal Remainder(decimal a, decimal b)
    {
        try
        {
            return a % b;
        }
        catch (OverflowException)
        {
            static BigInteger Magnitude(decimal d)
            {
                var bits = decimal.GetBits(d);
                return (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | (uint)bits[0];
            }

            var scale = Math.Max(a.Scale, b.Scale);
            var r = BigInteger.Remainder(Magnitude(a) * BigInteger.Pow(10, scale - a.Scale), Magnitude(b) * BigInteger.Pow(10, scale - b.Scale));
            var mask = new BigInteger(uint.MaxValue);
            return new decimal((int)(uint)(r & mask), (int)(uint)((r >> 32) & mask), (int)(uint)(r >> 64), decimal.IsNegative(a), (byte)scale);
        }
    }

    /// <summary>L17: an expression made of literals is folded in the type its literals take, without T28's promotion, which is for the operands of a run-time operation; a conversion or another constant that is no literal is promoted (T28).</summary>
    private static PrimitiveType ConstantType(PrimitiveType promoted, BinaryExpression b, Typed left, Typed right) =>
        IsLiteralOnly(b.Left) && IsLiteralOnly(b.Right) && left.Constant is IntegerConstant && right.Constant is IntegerConstant && Types.IsInteger(left.Type) && Types.Same(left.Type, right.Type) ? (PrimitiveType)left.Type : promoted;

    /// <summary>C8 judges <c>reversed-range</c> on ends that are expressions made only of literals; a named constant or a call is left to the run time (<c>reversed-range-at-run-time</c>).</summary>
    private static bool IsLiteralEnd(Expression e) => IsLiteralOnly(e);

    private static PrimitiveType? TimeArithmetic(TokenKind op, PrimitiveType l, PrimitiveType r)
    {
        var wide = Types.IsWideTime(l) || Types.IsWideTime(r);
        var (ld, rd) = (Types.IsDuration(l), Types.IsDuration(r));
        return (op, ld, rd) switch
        {
            (TokenKind.Plus, true, true) or (TokenKind.Minus, true, true) => wide ? Types.LongDuration : Types.Duration,
            (TokenKind.Plus, false, true) or (TokenKind.Plus, true, false) or (TokenKind.Minus, false, true) => wide ? Types.LongTimestamp : Types.Timestamp,
            (TokenKind.Minus, false, false) => wide ? Types.LongDuration : Types.Duration,
            _ => null,
        };
    }

    /// <summary>An integer or time constant in its type: <c>constant-overflow</c> when it leaves the range (T6).</summary>
    private Typed Fold(PrimitiveType type, int line, Constant? constant)
    {
        if (constant is IntegerConstant c && (Types.IsInteger(type) || Types.IsTime(type)) && !Types.Fits(c.Value, type))
        {
            Report(ErrorIds.ConstantOverflow, line, $"the result leaves the range of `{type}` (T6)");
            return Typed.Error;
        }

        return new Typed(type, constant);
    }

    private static BigInteger Wrap(BigInteger value, PrimitiveType type)
    {
        var width = Types.Width(type);
        var modulus = BigInteger.One << width;
        var wrapped = ((value % modulus) + modulus) % modulus;
        if (Types.IsSigned(type) && wrapped >= (modulus >> 1))
        {
            wrapped -= modulus;
        }

        return wrapped;
    }

    private Typed Comparison(BinaryExpression b, Typed left, Typed right)
    {
        var op = Operator(b.Operator);
        var equality = b.Operator is TokenKind.EqualEqual or TokenKind.NotEqual;
        var ok = false;
        if (Types.IsNumber(left.Type) && Types.IsNumber(right.Type))
        {
            var l = (PrimitiveType)left.Type;
            var r = (PrimitiveType)right.Type;
            if (Types.IsInteger(l) && Types.IsInteger(r))
            {
                if (Types.CommonInteger(l, r) is null)
                {
                    Report(ErrorIds.SignMix, b.Line, $"`{op}` between {Types.A(l)} and {Types.A(r)} mixes the signedness (T9)");
                    return new Typed(Types.Bool);
                }

                ok = true;
            }
            else
            {
                ok = !((Types.IsDecimal(l) && Types.IsFloating(r)) || (Types.IsFloating(l) && Types.IsDecimal(r)));
            }
        }
        else if (Types.IsTime(left.Type) && Types.IsTime(right.Type))
        {
            ok = Types.IsDuration(left.Type) == Types.IsDuration(right.Type);
        }
        else if (left.Type is PrimitiveType { Kind: Primitive.Char } && right.Type is PrimitiveType { Kind: Primitive.Char })
        {
            ok = true;
        }
        else if (left.Type is EnumType e && Types.Same(left.Type, right.Type))
        {
            // D12: equal when the same value; D18: in order by their numbers, as C# orders them (section 4, the owner, 2026-10-29), and an
            // enum that numbers none has no order (D20, section 4, an assumption there)
            if (!equality && !e.Symbol.Numbered && !e.Symbol.Refused)
            {
                Report(ErrorIds.EnumNumber, b.Line, $"`{op}` orders values by their numbers, and `{e}` numbers none of its values (D18, D20)");
                return new Typed(Types.Bool);
            }

            ok = true;
        }
        else if (!equality && left.Type is PrimitiveType { Kind: Primitive.String } && right.Type is PrimitiveType { Kind: Primitive.String })
        {
            ok = true; // T32: two strings in the order of their code points
        }
        else if (equality)
        {
            ok = (left.Type is PrimitiveType { Kind: Primitive.String or Primitive.Bool } && Types.Same(left.Type, right.Type))
                || (left.Type is DataType && right.Type is DataType && (Types.Assignable(left.Type, right.Type) || Types.Assignable(right.Type, left.Type)))
                || (left.Type is ListType && Types.Same(left.Type, right.Type))
                || (left.Type is ClassType && Types.Same(left.Type, right.Type))
                || (left.Type is UnionType && right.Type is UnionType && (Types.Assignable(left.Type, right.Type) || Types.Assignable(right.Type, left.Type)));
            var refused = !ok ? null : left.Type switch
            {
                // M9: two lists compare by their counts and their items (section 4, the owner, 2026-10-30, "By items, as Rust"); D3: a data
                // value that holds a map or a function value, at any depth, compares by no field (section 4, the owner, 2026-10-30,
                // "type-mismatch")
                ListType list => ItemEquality(list.Element) is { } item ? item with { Why = $"its items: {item.Why}" } : null,
                ClassType => (false, $"the equality of a class instance (K4) is not read"),
                DataType => HeldEquality(left.Type, [], [], []) ?? HeldEquality(right.Type, [], [], []),
                // two values of a union, as the union the other side goes to: by the case, then by the value (section 4, the owner,
                // 2026-10-30, "Case and value, as Rust"); a union beside a value of one of its cases is no such pair, as Rust refuses
                // `Some(5) == 5` (section 4, the owner, 2026-10-30, "Refuse, as Rust")
                _ => ItemEquality(Types.Assignable(left.Type, right.Type) ? right.Type : left.Type),
            };
            switch (refused)
            {
                case (true, var why):
                    Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes no `{left.Type}` and `{right.Type}`: {why} (T18, D3, M9)");
                    return new Typed(Types.Bool);
                case (false, var why):
                    NotSupported(b.Line, $"`{op}` on {Types.A(left.Type)}: {why} (D3, M9)");
                    return new Typed(Types.Bool);
            }
        }

        if (!ok)
        {
            Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes no `{left.Type}` and `{right.Type}` (T18, D3)");
            return new Typed(Types.Bool);
        }

        var result = (left.Constant, right.Constant) switch
        {
            (IntegerConstant l, IntegerConstant r) => Compare(b.Operator, l.Value.CompareTo(r.Value), true),
            (RealConstant l, RealConstant r) => double.IsNaN(l.Value) || double.IsNaN(r.Value) ? b.Operator == TokenKind.NotEqual : Compare(b.Operator, l.Value.CompareTo(r.Value), true),
            (DecimalConstant l, DecimalConstant r) => Compare(b.Operator, l.Value.CompareTo(r.Value), true),
            (CharConstant l, CharConstant r) => Compare(b.Operator, l.Value.CompareTo(r.Value), true),
            // T32: by their code points, the order of their UTF-8 bytes, where C#'s ordinal order of UTF-16 units puts U+FFFF after U+1F600
            (StringConstant l, StringConstant r) => Compare(b.Operator, System.Text.Encoding.UTF8.GetBytes(l.Value).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(r.Value)), true),
            (BoolConstant l, BoolConstant r) => Compare(b.Operator, l.Value.CompareTo(r.Value), true),
            (EnumConstant l, EnumConstant r) when left.Type is EnumType { Symbol: { Numbered: true } numbered } =>
                Compare(b.Operator, numbered.Numbers[l.Index].CompareTo(numbered.Numbers[r.Index]), true),
            (EnumConstant l, EnumConstant r) when equality => Compare(b.Operator, l.Index.CompareTo(r.Index), true),
            _ => (bool?)null,
        };
        return new Typed(Types.Bool, result is null ? null : new BoolConstant(result.Value));
    }

    private static bool? Compare(TokenKind op, int comparison, bool known) => !known ? null : op switch
    {
        TokenKind.EqualEqual => comparison == 0,
        TokenKind.NotEqual => comparison != 0,
        TokenKind.Less => comparison < 0,
        TokenKind.LessEqual => comparison <= 0,
        TokenKind.Greater => comparison > 0,
        _ => comparison >= 0,
    };

    /// <summary>C8: the type of a range's numbers, and <c>reversed-range</c> for ends in the wrong order where both are expressions made only of literals, as the rule says.</summary>
    private KurzType CheckRange(RangeExpression r)
    {
        var (start, end) = Operands(r.Start, r.End, null, OperatorClass.Arithmetic);
        if (start.IsError || end.IsError)
        {
            return Types.Error;
        }

        if (!Types.IsInteger(start.Type) || !Types.IsInteger(end.Type))
        {
            Report(ErrorIds.TypeMismatch, r.Line, $"the ends of a range are integers, not `{start.Type}` and `{end.Type}` (C8)");
            return Types.Error;
        }

        var type = Types.CommonInteger((PrimitiveType)start.Type, (PrimitiveType)end.Type);
        if (type is null)
        {
            Report(ErrorIds.SignMix, r.Line, $"the ends of a range, {Types.A(start.Type)} and {Types.A(end.Type)}, mix the signedness (C8, T9)");
            return Types.Error;
        }

        if (IsLiteralEnd(r.Start) && IsLiteralEnd(r.End) && start.Constant is IntegerConstant s && end.Constant is IntegerConstant e && e.Value < s.Value)
        {
            Report(ErrorIds.ReversedRange, r.Line, $"the range ends at {e.Value}, below its start {s.Value} (C8)");
        }

        return type;
    }

    // ---- lambdas and with --------------------------------------------------------------------------

    /// <summary>
    /// F7, F11, F15: a lambda under the function type expected of it, or with its own type where its parameters are typed, or are
    /// <paramref name="given"/>, the types a call passes them, whose result the lambda's body gives (X8's <c>SortBy</c>).
    /// </summary>
    private Typed CheckLambda(LambdaExpression l, FunctionType? expected, bool atUnreadType = false, IReadOnlyList<KurzType>? given = null)
    {
        // after an error on the parameters the body is still read, with them error-typed, so that its own defects and the variables it reads count (E1)
        var parameterTypes = new List<KurzType>();
        var failed = false;
        if (expected is not null && expected.Parameters.Count != l.Parameters.Count)
        {
            Report(ErrorIds.TypeMismatch, l.Line, $"the lambda takes {l.Parameters.Count} parameters where `{expected}` takes {expected.Parameters.Count} (F11)");
            failed = true;
            expected = null;
        }

        if (expected is not null || given is not null)
        {
            foreach (var (p, type) in l.Parameters.Zip(expected?.Parameters ?? given!))
            {
                if (p.Type is null)
                {
                    parameterTypes.Add(type);
                    continue;
                }

                var written = Resolve(p.Type);
                if (!Types.IsError(written) && !Types.Same(written, type))
                {
                    Report(ErrorIds.TypeMismatch, p.Line, $"the parameter `{p.Name}` is {Types.A(written)} where {(expected is null ? "the call" : $"`{expected}`")} passes {Types.A(type)} (F11)");
                    written = Types.Error; // and the lambda with it, so that the call reports nothing more about it (E1)
                }

                parameterTypes.Add(written);
            }
        }
        else
        {
            if (!failed && !atUnreadType && l.Parameters.Any(p => p.Type is null))
            {
                Report(ErrorIds.CannotInfer, l.Line, "the parameters of the lambda have no type, and nothing around it gives one (F7, T27)");
                failed = true;
            }

            parameterTypes.AddRange(l.Parameters.Select(p => failed || p.Type is null ? Types.Error : Resolve(p.Type)));
        }

        var outer = frame;
        frame = new Frame
        {
            IsLambda = true,
            Outer = outer,
            CapturedScope = outer.Scope,
            ReturnType = expected?.Result,
            Line = l.Line,
            TypeParameters = outer.TypeParameters,
            Reserved = outer.Reserved,
            Known = Unchangeable(outer.Known), // N3 with F15: the lambda takes the values its variables have where it is made, and what was known of them; N9: not of a path a call can change before the lambda runs
        };
        var parameters = new List<VariableSymbol>();
        foreach (var (p, type) in l.Parameters.Zip(parameterTypes))
        {
            parameters.Add(DeclareParameter(p.Name, type, p.Mut, p.Line, p));
        }

        KurzType result;
        if (l.ExpressionBody is not null)
        {
            if (expected is null)
            {
                var body = CheckExpression(l.ExpressionBody, null);
                result = body.Type;
            }
            else
            {
                result = expected.Result;
                if (Types.IsVoid(result))
                {
                    // F11 types the lambda by its parameter, whose result is `void`: a value standing as its body is text no rule gives a
                    // meaning, E4's `syntax`, as for the expression body of a `void` function (F2, F4; the reading in compiler/README.md)
                    var body = CheckExpression(l.ExpressionBody, null);
                    if (!Types.IsVoid(body.Type) && !body.IsError && Unwrap(l.ExpressionBody) is not CallExpression and not ElseExpression)
                    {
                        Report(ErrorIds.Syntax, l.Line, "the body of a lambda with a `void` result is a call or a call with `else` (E4, F11, F4)");
                    }
                }
                else
                {
                    var body = CheckExpression(l.ExpressionBody, result, keepCases: Types.KeepsCases(result));
                    if (Types.IsVoid(body.Type))
                    {
                        Report(ErrorIds.TypeMismatch, l.Line, $"the lambda's result is `void` where `{expected}` requires {Types.A(result)} (F11, T1)");
                    }
                    else if (!body.IsError && !Types.IsError(result) && !Types.Assignable(body.Type, result))
                    {
                        ReportMismatch(body.Type, result, l.Line, $"the lambda's result is {Types.A(body.Type)} where `{expected}` requires {Types.A(result)}", "F11");
                    }
                }
            }
        }
        else
        {
            var flow = CheckBlock(l.BlockBody!);
            result = frame.ReturnType ?? Types.Void;
            if (flow == Flow.Completes && !SuccessIsVoid(result) && !Types.IsError(result))
            {
                Report(ErrorIds.MissingReturn, l.Line, "the end of the lambda can be reached without a `return` (F3, F7)");
            }
        }

        ReportUnused(frame.Scope);
        var lambda = frame;
        frame = outer;
        if (failed || parameterTypes.Any(Types.IsError) || Types.IsError(result))
        {
            return Typed.Error;
        }

        var lambdaType = new FunctionType(parameterTypes, result);
        model.RecordLambda(l, new LambdaInfo(parameters, lambda.Captures.ToList(), lambdaType)); // the symbols declared here: a reader of the model never runs while the checker does
        return new Typed(lambdaType);
    }

    /// <summary>D5: a copy of a data value with fields replaced.</summary>
    private Typed CheckWith(WithExpression w)
    {
        var target = CheckExpression(w.Target, null);
        if (target.IsError)
        {
            foreach (var a in w.Assignments)
            {
                CheckExpression(a.Value, Untyped(a.Value));
            }

            return Typed.Error;
        }

        if (target.Type is not DataType d)
        {
            if (!Unchecked(target, w.Line, "copied by `with`"))
            {
                Report(ErrorIds.TypeMismatch, w.Line, $"`with` copies a data value, not {Types.A(target.Type)} (D5)");
            }

            foreach (var a in w.Assignments)
            {
                CheckExpression(a.Value, Untyped(a.Value));
            }

            return Typed.Error;
        }

        foreach (var a in w.Assignments)
        {
            var field = d.Symbol.Field(a.Field);
            if (field is null)
            {
                Report(ErrorIds.UnknownName, a.Line, $"`{d.Symbol.Name}` has no field `{a.Field}` (D5)");
                CheckExpression(a.Value, Untyped(a.Value));
                continue;
            }

            var type = SubstituteData(d, field.Type);
            var value = CheckExpression(a.Value, type);
            if (!value.IsError && !Types.IsError(type) && !Types.Assignable(value.Type, type))
            {
                ReportMismatch(value.Type, type, a.Line, $"{Types.A(value.Type)} where the field `{a.Field}` is {Types.A(type)}", "D5");
            }
        }

        return new Typed(d);
    }
}
