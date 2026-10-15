using System.Numerics;

#pragma warning disable CA1720 // the members carry the names the reference gives the types (T2, L19); a C# type name is no concern of a Kurz type

namespace Kurz.Compiler.Semantics;

/// <summary>The built-in types of T2, T13, T15, T21, T24, T29 and L8, by name.</summary>
public enum Primitive
{
    SByte,
    Byte,
    Short,
    UShort,
    Int,
    UInt,
    Long,
    ULong,
    Float,
    Double,
    Decimal,
    Bool,
    String,
    Char,
    Duration,
    Timestamp,
    LongDuration,
    LongTimestamp,
}

/// <summary>
/// A type of the checker. The text is how the reference writes it. Two types are the same when
/// <see cref="Types.Same"/> says so; the records compare by structure through it.
/// </summary>
public abstract record KurzType
{
    public abstract string Text { get; }

    public sealed override string ToString() => Text;
}

/// <summary>A built-in type (T2, T13, T15, T21, T24, T29, L8).</summary>
public sealed record PrimitiveType(Primitive Kind) : KurzType
{
    public override string Text => Kind switch
    {
        Primitive.SByte => "sbyte",
        Primitive.Byte => "byte",
        Primitive.Short => "short",
        Primitive.UShort => "ushort",
        Primitive.Int => "int",
        Primitive.UInt => "uint",
        Primitive.Long => "long",
        Primitive.ULong => "ulong",
        Primitive.Float => "float",
        Primitive.Double => "double",
        Primitive.Decimal => "decimal",
        Primitive.Bool => "bool",
        Primitive.String => "string",
        Primitive.Char => "char",
        Primitive.Duration => "duration",
        Primitive.Timestamp => "timestamp",
        Primitive.LongDuration => "longduration",
        Primitive.LongTimestamp => "longtimestamp",
        _ => Kind.ToString(),
    };
}

/// <summary><c>void</c>: the result of a function without one (F4), or the success case of O10.</summary>
public sealed record VoidType : KurzType
{
    public override string Text => "void";
}

/// <summary>
/// The type of an expression the checker could not type: a name it did not find, a construct it does
/// not read, an operand that was already wrong. Nothing is reported about a value of this type, so
/// that one defect is one error (E1).
/// </summary>
public sealed record ErrorType : KurzType
{
    public override string Text => "?";
}

/// <summary><c>List&lt;T&gt;</c> (M9, T17).</summary>
public sealed record ListType(KurzType Element) : KurzType
{
    public override string Text => $"List<{Element.Text}>";

    public bool Equals(ListType? other) => other is not null && Types.Same(Element, other.Element);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>A <c>data</c> type (D1), with its type arguments where it is generic (T19).</summary>
public sealed record DataType(DataSymbol Symbol, IReadOnlyList<KurzType> TypeArguments) : KurzType
{
    public override string Text => TypeArguments.Count == 0 ? Symbol.Name : $"{Symbol.Name}<{string.Join(", ", TypeArguments.Select(t => t.Text))}>";

    public bool Equals(DataType? other) => other is not null && ReferenceEquals(Symbol, other.Symbol) && Types.AllSame(TypeArguments, other.TypeArguments);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// A union (D9). The cases are flat and distinct, in the order written; the first is the success
/// case of a result (O1). Two unions are the same type when their cases are the same in the same
/// order (the reading in <c>compiler/README.md</c>): O1 gives the order a meaning, so
/// <c>List&lt;int | string&gt;</c> and <c>List&lt;string | int&gt;</c> are two types, while a value of
/// one union stands where the other is required when every case of it is a case there (<see cref="Types.Assignable"/>).
/// </summary>
public sealed record UnionType(IReadOnlyList<KurzType> Cases) : KurzType
{
    public override string Text => string.Join(" | ", Cases.Select(c => c is FunctionType or UnionType ? "(" + c.Text + ")" : c.Text));

    public bool Equals(UnionType? other) => other is not null && Types.AllSame(Cases, other.Cases);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The type of a function value (F11): <c>(int) => bool</c>.</summary>
public sealed record FunctionType(IReadOnlyList<KurzType> Parameters, KurzType Result) : KurzType
{
    public override string Text => $"({string.Join(", ", Parameters.Select(p => p.Text))}) => {(Result is UnionType ? "(" + Result.Text + ")" : Result.Text)}";

    public bool Equals(FunctionType? other) => other is not null && Types.AllSame(Parameters, other.Parameters) && Types.Same(Result, other.Result);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>A type parameter inside the generic function or type that declares it (T19).</summary>
public sealed record TypeParameterType(TypeParameterSymbol Symbol) : KurzType
{
    public override string Text => Symbol.Name;

    public bool Equals(TypeParameterType? other) => other is not null && ReferenceEquals(Symbol, other.Symbol);

    public override int GetHashCode() => Symbol.Name.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The one instance of each built-in type, and what the chapters say about them.</summary>
public static class Types
{
    public static readonly PrimitiveType SByte = new(Primitive.SByte);
    public static readonly PrimitiveType Byte = new(Primitive.Byte);
    public static readonly PrimitiveType Short = new(Primitive.Short);
    public static readonly PrimitiveType UShort = new(Primitive.UShort);
    public static readonly PrimitiveType Int = new(Primitive.Int);
    public static readonly PrimitiveType UInt = new(Primitive.UInt);
    public static readonly PrimitiveType Long = new(Primitive.Long);
    public static readonly PrimitiveType ULong = new(Primitive.ULong);
    public static readonly PrimitiveType Float = new(Primitive.Float);
    public static readonly PrimitiveType Double = new(Primitive.Double);
    public static readonly PrimitiveType Decimal = new(Primitive.Decimal);
    public static readonly PrimitiveType Bool = new(Primitive.Bool);
    public static readonly PrimitiveType String = new(Primitive.String);
    public static readonly PrimitiveType Char = new(Primitive.Char);
    public static readonly PrimitiveType Duration = new(Primitive.Duration);
    public static readonly PrimitiveType Timestamp = new(Primitive.Timestamp);
    public static readonly PrimitiveType LongDuration = new(Primitive.LongDuration);
    public static readonly PrimitiveType LongTimestamp = new(Primitive.LongTimestamp);
    public static readonly VoidType Void = new();
    public static readonly ErrorType Error = new();

    /// <summary>The built-in types by the names L19 reserves.</summary>
    public static readonly IReadOnlyDictionary<string, PrimitiveType> ByName = new Dictionary<string, PrimitiveType>(StringComparer.Ordinal)
    {
        ["sbyte"] = SByte, ["byte"] = Byte, ["short"] = Short, ["ushort"] = UShort, ["int"] = Int, ["uint"] = UInt, ["long"] = Long, ["ulong"] = ULong,
        ["float"] = Float, ["double"] = Double, ["decimal"] = Decimal, ["bool"] = Bool, ["string"] = String, ["char"] = Char,
        ["duration"] = Duration, ["timestamp"] = Timestamp, ["longduration"] = LongDuration, ["longtimestamp"] = LongTimestamp,
    };

    public static bool IsError(KurzType type) => type is ErrorType;

    public static bool IsVoid(KurzType type) => type is VoidType;

    public static bool IsInteger(KurzType type) => type is PrimitiveType { Kind: <= Primitive.ULong };

    public static bool IsSigned(KurzType type) => type is PrimitiveType { Kind: Primitive.SByte or Primitive.Short or Primitive.Int or Primitive.Long };

    public static bool IsUnsigned(KurzType type) => type is PrimitiveType { Kind: Primitive.Byte or Primitive.UShort or Primitive.UInt or Primitive.ULong };

    /// <summary>T28: narrower than <c>int</c>, promoted before an operation.</summary>
    public static bool IsNarrow(KurzType type) => type is PrimitiveType { Kind: Primitive.SByte or Primitive.Byte or Primitive.Short or Primitive.UShort };

    public static bool IsFloating(KurzType type) => type is PrimitiveType { Kind: Primitive.Float or Primitive.Double };

    public static bool IsDecimal(KurzType type) => type is PrimitiveType { Kind: Primitive.Decimal };

    /// <summary>T2: the integer types, the floating-point types and <c>decimal</c>.</summary>
    public static bool IsNumber(KurzType type) => type is PrimitiveType { Kind: <= Primitive.Decimal };

    public static bool IsTime(KurzType type) => type is PrimitiveType { Kind: >= Primitive.Duration };

    public static bool IsDuration(KurzType type) => type is PrimitiveType { Kind: Primitive.Duration or Primitive.LongDuration };

    public static bool IsTimestamp(KurzType type) => type is PrimitiveType { Kind: Primitive.Timestamp or Primitive.LongTimestamp };

    public static bool IsWideTime(KurzType type) => type is PrimitiveType { Kind: Primitive.LongDuration or Primitive.LongTimestamp };

    /// <summary>The width of an integer type in bits; of a time type the width of its count (T24, T29).</summary>
    public static int Width(KurzType type) => type is PrimitiveType p ? p.Kind switch
    {
        Primitive.SByte or Primitive.Byte => 8,
        Primitive.Short or Primitive.UShort => 16,
        Primitive.Int or Primitive.UInt => 32,
        Primitive.Long or Primitive.ULong or Primitive.Duration or Primitive.Timestamp => 64,
        Primitive.LongDuration or Primitive.LongTimestamp => 128,
        _ => 0,
    } : 0;

    /// <summary>The range of an integer type, or of the count of a time type.</summary>
    public static (BigInteger Min, BigInteger Max) Range(KurzType type)
    {
        var width = Width(type);
        if (width == 0)
        {
            throw new ArgumentException($"{type} has no integer range", nameof(type));
        }

        if (IsUnsigned(type))
        {
            return (BigInteger.Zero, (BigInteger.One << width) - 1);
        }

        var half = BigInteger.One << (width - 1);
        return (-half, half - 1);
    }

    public static bool Fits(BigInteger value, KurzType type)
    {
        var (min, max) = Range(type);
        return value >= min && value <= max;
    }

    public static bool Same(KurzType a, KurzType b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return (a, b) switch
        {
            (PrimitiveType x, PrimitiveType y) => x.Kind == y.Kind,
            (VoidType, VoidType) => true,
            (ErrorType, ErrorType) => true,
            (ListType x, ListType y) => Same(x.Element, y.Element),
            (DataType x, DataType y) => ReferenceEquals(x.Symbol, y.Symbol) && AllSame(x.TypeArguments, y.TypeArguments),
            (UnionType x, UnionType y) => AllSame(x.Cases, y.Cases),
            (FunctionType x, FunctionType y) => AllSame(x.Parameters, y.Parameters) && Same(x.Result, y.Result),
            (TypeParameterType x, TypeParameterType y) => ReferenceEquals(x.Symbol, y.Symbol),
            _ => false,
        };
    }

    public static bool AllSame(IReadOnlyList<KurzType> a, IReadOnlyList<KurzType> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second));
    }

    /// <summary>
    /// A union of these cases, flat and without a case listed twice (D9); the one case alone when
    /// only one is left. The order is the order given, so that the first case stays the success case (O1).
    /// </summary>
    public static KurzType Union(IEnumerable<KurzType> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var flat = new List<KurzType>();
        foreach (var c in cases)
        {
            foreach (var one in c is UnionType u ? u.Cases : [c])
            {
                if (!flat.Any(f => Same(f, one)))
                {
                    flat.Add(one);
                }
            }
        }

        return flat.Count == 1 ? flat[0] : new UnionType(flat);
    }

    /// <summary>
    /// Whether a value of <paramref name="from"/> stands where <paramref name="to"/> is required
    /// without a word: the same type; an integer widened within its signedness or across it where C#
    /// widens (T8, T23); an integer into a floating-point type or <c>decimal</c> (T23); <c>float</c>
    /// into <c>double</c>; the narrow time type into its wide one (T29); a derived <c>data</c> type
    /// where its base is required (D6); a case where its union is required (D9); a union whose every
    /// case the required union holds. A value of the error type stands anywhere, and anything stands
    /// where the error type is required (E1: one defect, one error). No type has the error type inside
    /// it: a list, data or function type with an error in a part is the error type as a whole, as
    /// <see cref="Checker"/> resolves and infers them, so the test on the whole is the whole test.
    /// </summary>
    public static bool Assignable(KurzType from, KurzType to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from is ErrorType || to is ErrorType || Same(from, to))
        {
            return true;
        }

        if (from is UnionType fu)
        {
            return fu.Cases.All(c => Assignable(c, to));
        }

        if (to is UnionType tu)
        {
            return tu.Cases.Any(c => Assignable(from, c));
        }

        if (from is PrimitiveType f && to is PrimitiveType t)
        {
            return Widens(f.Kind, t.Kind);
        }

        if (from is DataType fd && to is DataType td)
        {
            for (var b = fd.Symbol.Base; b is not null; b = b.Base)
            {
                if (ReferenceEquals(b, td.Symbol) && td.TypeArguments.Count == 0)
                {
                    return true;
                }
            }

            return false;
        }

        if (from is ListType fl && to is ListType tl)
        {
            return Same(fl.Element, tl.Element);
        }

        return false;
    }

    /// <summary>T8, T23, T29: the widening conversions between built-in types that need no word.</summary>
    public static bool Widens(Primitive from, Primitive to)
    {
        if (from == to)
        {
            return true;
        }

        if (to is Primitive.Float or Primitive.Double or Primitive.Decimal)
        {
            return from <= Primitive.ULong || (from == Primitive.Float && to == Primitive.Double);
        }

        return (from, to) switch
        {
            (Primitive.SByte, Primitive.Short or Primitive.Int or Primitive.Long) => true,
            (Primitive.Byte, Primitive.Short or Primitive.UShort or Primitive.Int or Primitive.UInt or Primitive.Long or Primitive.ULong) => true,
            (Primitive.Short, Primitive.Int or Primitive.Long) => true,
            (Primitive.UShort, Primitive.Int or Primitive.UInt or Primitive.Long or Primitive.ULong) => true,
            (Primitive.Int, Primitive.Long) => true,
            (Primitive.UInt, Primitive.Long or Primitive.ULong) => true,
            (Primitive.Duration, Primitive.LongDuration) => true,
            (Primitive.Timestamp, Primitive.LongTimestamp) => true,
            _ => false,
        };
    }

    /// <summary>
    /// The id a value of <paramref name="from"/> gets where <paramref name="to"/> is required and
    /// <see cref="Assignable"/> says no: <c>narrowing-conversion</c> for an integer or time type put
    /// into a narrower one, whatever the signedness (T7, T23, T29); <c>sign-mix</c> for an integer
    /// put where a type of the other signedness and at least its width is required (T23);
    /// <c>type-mismatch</c> for everything else (T1).
    /// </summary>
    public static string MismatchId(KurzType from, KurzType to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (IsInteger(from) && IsInteger(to))
        {
            return Width(to) < Width(from) ? ErrorIds.NarrowingConversion : ErrorIds.SignMix;
        }

        if ((IsDuration(from) && IsDuration(to)) || (IsTimestamp(from) && IsTimestamp(to)))
        {
            return ErrorIds.NarrowingConversion;
        }

        return ErrorIds.TypeMismatch;
    }

    /// <summary>
    /// The common type of two integer operands under <c>+ - * / %</c>, the comparisons and
    /// <c>&amp; | ^</c> (T9, T18, T28): null for <c>sign-mix</c>. T28: two operands narrower than
    /// <c>int</c> are promoted to <c>int</c>, whatever their signedness, and a narrow operand beside a
    /// <c>uint</c>, a <c>long</c> or a <c>ulong</c> takes that type, so <c>byte + uint</c> is a
    /// <c>uint</c>; otherwise the type the other operand widens into (T23: <c>uint</c> widens into
    /// <c>long</c>), and where neither widens into the other (<c>uint</c> beside <c>int</c>,
    /// <c>ulong</c> beside any signed type) the error (T9).
    /// </summary>
    public static PrimitiveType? CommonInteger(PrimitiveType a, PrimitiveType b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (IsNarrow(a) && IsNarrow(b))
        {
            return Int;
        }

        if (Widens(a.Kind, b.Kind))
        {
            return b;
        }

        if (Widens(b.Kind, a.Kind))
        {
            return a;
        }

        return null;
    }

    /// <summary>
    /// The common type of two integer operands under the wrapping operators (T12, T28), which do not
    /// promote: the wider of the two within one signedness, null for <c>sign-mix</c> across it.
    /// </summary>
    public static PrimitiveType? CommonWrapping(PrimitiveType a, PrimitiveType b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (IsSigned(a) != IsSigned(b))
        {
            return null;
        }

        return Width(a) >= Width(b) ? a : b;
    }

    public static bool HasBaseOrIs(DataSymbol derived, DataSymbol maybeBase)
    {
        ArgumentNullException.ThrowIfNull(derived);
        for (DataSymbol? d = derived; d is not null; d = d.Base)
        {
            if (ReferenceEquals(d, maybeBase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every type parameter of <paramref name="type"/> replaced through <paramref name="map"/>; a parameter the map lacks stays.</summary>
    public static KurzType Substitute(KurzType type, IReadOnlyDictionary<TypeParameterSymbol, KurzType> map)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(map);
        return type switch
        {
            TypeParameterType p => map.TryGetValue(p.Symbol, out var t) ? t : type,
            ListType l => new ListType(Substitute(l.Element, map)),
            DataType d => new DataType(d.Symbol, d.TypeArguments.Select(a => Substitute(a, map)).ToList()),
            UnionType u => Union(u.Cases.Select(c => Substitute(c, map))),
            FunctionType f => new FunctionType(f.Parameters.Select(p => Substitute(p, map)).ToList(), Substitute(f.Result, map)),
            _ => type,
        };
    }

    public static bool Mentions(KurzType type, TypeParameterSymbol parameter)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            TypeParameterType p => ReferenceEquals(p.Symbol, parameter),
            ListType l => Mentions(l.Element, parameter),
            DataType d => d.TypeArguments.Any(a => Mentions(a, parameter)),
            UnionType u => u.Cases.Any(c => Mentions(c, parameter)),
            FunctionType f => f.Parameters.Any(p => Mentions(p, parameter)) || Mentions(f.Result, parameter),
            _ => false,
        };
    }
}
