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
    /// <summary>M9: a list of bytes is named <c>Bytes</c>, its second name (section 4, the owner, 2026-10-29, "Bytes = List&lt;byte&gt;").</summary>
    public override string Text => Element is PrimitiveType { Kind: Primitive.Byte } ? "Bytes" : $"List<{Element.Text}>";

    public bool Equals(ListType? other) => other is not null && Types.Same(Element, other.Element);

    public override int GetHashCode() => HashCode.Combine(nameof(ListType), Element);
}

/// <summary>A <c>data</c> type (D1), with its type arguments where it is generic (T19).</summary>
public sealed record DataType(DataSymbol Symbol, IReadOnlyList<KurzType> TypeArguments) : KurzType
{
    public override string Text => TypeArguments.Count == 0 ? Symbol.Name : $"{Symbol.Name}<{string.Join(", ", TypeArguments.Select(t => t.Text))}>";

    public bool Equals(DataType? other) => other is not null && ReferenceEquals(Symbol, other.Symbol) && Types.AllSame(TypeArguments, other.TypeArguments);

    public override int GetHashCode() => HashCode.Combine(Symbol.Name, TypeArguments.Count);
}

/// <summary>An <c>enum</c> (D12), one type per declaration.</summary>
public sealed record EnumType(EnumSymbol Symbol) : KurzType
{
    public override string Text => Symbol.Name;

    public bool Equals(EnumType? other) => other is not null && ReferenceEquals(Symbol, other.Symbol);

    public override int GetHashCode() => Symbol.Name.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The handles of chapter 13, each a name every file sees.</summary>
public enum Handle
{
    File,
    TcpListener,
    TcpStream,
}

/// <summary>
/// A handle (S5, S9): a file or a socket of the system, held by every copy of the value and closed when the last lets go of it, as a
/// list's block is freed (section 12, the owner, 2026-10-29, "At the last count, as Rust Drop").
/// </summary>
public sealed record HandleType(Handle Kind) : KurzType
{
    public override string Text => Kind.ToString();
}

/// <summary>
/// A union (D9). The cases are flat and distinct, in the order written; the first is the success
/// case of a result (O1). Two unions are one type when they have the same first case and the same
/// other cases, in whatever order (D9, the owner, 2026-10-21): <c>List&lt;A | B | C&gt;</c> and
/// <c>List&lt;A | C | B&gt;</c> are one type, and <c>List&lt;int | string&gt;</c> and
/// <c>List&lt;string | int&gt;</c> two. A value of one union stands where another is required when
/// its first case goes to the first case and each other case to one case after the first (D9 with O1,
/// <see cref="Types.Assignable"/>).
/// </summary>
public sealed record UnionType(IReadOnlyList<KurzType> Cases) : KurzType
{
    public override string Text => string.Join(" | ", Cases.Select(c => c is FunctionType or UnionType or CFunctionType ? "(" + c.Text + ")" : c.Text));

    public bool Equals(UnionType? other) => other is not null && Types.Same(this, other);

    public override int GetHashCode() => HashCode.Combine(Cases[0], Cases.Count);
}

/// <summary>The type of a function value (F11): <c>(int) => bool</c>.</summary>
public sealed record FunctionType(IReadOnlyList<KurzType> Parameters, KurzType Result) : KurzType
{
    public override string Text => $"({string.Join(", ", Parameters.Select(p => p.Text))}) => {(Result is UnionType ? "(" + Result.Text + ")" : Result.Text)}";

    public bool Equals(FunctionType? other) => other is not null && Types.AllSame(Parameters, other.Parameters) && Types.Same(Result, other.Result);

    public override int GetHashCode() => HashCode.Combine(Parameters.Count, Result);
}

/// <summary>
/// A raw pointer (R10): <c>int*</c>, written as C# writes it in <c>unsafe</c> code, and <c>void*</c>, the untyped pointer, whose
/// pointee is <c>void</c> (R14). Its pointee holds no counted reference: the checker builds one only for such a pointee (R10).
/// </summary>
public sealed record PointerType(KurzType Pointee) : KurzType
{
    public override string Text => Pointee is FunctionType or UnionType or CFunctionType ? $"({Pointee.Text})*" : Pointee.Text + "*";

    public bool Equals(PointerType? other) => other is not null && Types.Same(Pointee, other.Pointee);

    public override int GetHashCode() => HashCode.Combine(nameof(PointerType), Pointee);
}

/// <summary>The type of a C function (R17): <c>extern "C" (int) =&gt; void</c>, whose value is the name of a function of the C calling convention; it captures nothing, and the checker keeps it apart from a function type of F11.</summary>
public sealed record CFunctionType(IReadOnlyList<KurzType> Parameters, KurzType Result) : KurzType
{
    public override string Text => $"extern \"C\" ({string.Join(", ", Parameters.Select(p => p.Text))}) => {(Result is UnionType ? "(" + Result.Text + ")" : Result.Text)}";

    public bool Equals(CFunctionType? other) => other is not null && Types.AllSame(Parameters, other.Parameters) && Types.Same(Result, other.Result);

    public override int GetHashCode() => HashCode.Combine(nameof(CFunctionType), Parameters.Count, Result);
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
    public static readonly HandleType File = new(Handle.File);

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

    /// <summary>
    /// The cases among <paramref name="cases"/> a value of <paramref name="from"/> goes to: the case
    /// that is its type, and no other, for a value, a <c>data</c> value, a case of a union after its
    /// first and a propagated case alike (T8 with D9, D9, O3, the owner, 2026-10-21); else every case
    /// it widens into (T8) or derives from (D6). One is a fit; none is no fit, and two or more are the
    /// error a conversion or a <c>match</c> resolves, as a call that fits two functions is (F13), for a
    /// value and a propagated case (T8 with D9, D9 with D6 and O3, the owner, 2026-10-21) and for a
    /// case of a union after its first (the record's reading).
    /// </summary>
    public static IReadOnlyList<KurzType> CasesFor(KurzType from, IReadOnlyList<KurzType> cases)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(cases);
        var same = cases.FirstOrDefault(c => Same(from, c));
        return same is not null ? [same] : cases.Where(c => Assignable(from, c)).ToList();
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
            (EnumType x, EnumType y) => ReferenceEquals(x.Symbol, y.Symbol),
            (HandleType x, HandleType y) => x.Kind == y.Kind,
            (UnionType x, UnionType y) => x.Cases.Count == y.Cases.Count && Same(x.Cases[0], y.Cases[0]) && x.Cases.Skip(1).All(c => y.Cases.Skip(1).Any(d => Same(c, d))),
            (FunctionType x, FunctionType y) => AllSame(x.Parameters, y.Parameters) && Same(x.Result, y.Result),
            (PointerType x, PointerType y) => Same(x.Pointee, y.Pointee),
            (CFunctionType x, CFunctionType y) => AllSame(x.Parameters, y.Parameters) && Same(x.Result, y.Result),
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
    /// A union with the error type among its cases is the error type (E1).
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

        return flat.Any(f => f is ErrorType) ? Error : flat.Count == 1 ? flat[0] : new UnionType(flat);
    }

    /// <summary>
    /// Whether a value of <paramref name="from"/> stands where <paramref name="to"/> is required
    /// without a word: the same type; an integer into a wider one of its signedness (T8); an unsigned
    /// integer into a signed one that holds all its values, <c>byte</c> into <c>short</c>, <c>int</c>
    /// and <c>long</c>, <c>ushort</c> into <c>int</c> and <c>long</c>, <c>uint</c> into <c>long</c>
    /// (T23); every integer into <c>float</c>, <c>double</c> or <c>decimal</c>, rounded where the type
    /// does not hold the value (T23); <c>float</c> into <c>double</c>; the narrow time type into its
    /// wide one (T29); a derived <c>data</c> type where its base is required (D6); a value where a union
    /// is required that goes to exactly one of its cases (<see cref="CasesFor"/>: D9, T8 with D9); a
    /// union where a union is required whose first case its first case goes to, and each of whose other
    /// cases goes to exactly one of its cases after the first (D9 with O1). A union stands only where a union is
    /// required, since it is one of its cases only after a <c>match</c> (D9). A value of the error
    /// type stands anywhere, and anything stands where the error type is
    /// required (E1: one defect, one error). No type has the error type inside it: a list, data,
    /// union or function type with an error in a part is the error type as a whole, as
    /// <see cref="Checker"/> resolves them and <see cref="Substitute"/> and <see cref="Union"/> build
    /// them, so the test on the whole is the whole test.
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
            // D9 with O1 (the owner, 2026-10-21): the first case goes to the first case, the success, and each other case to a case after
            // the first, so that no case becomes a success it was not; one that is a case there goes to it (the owner, 2026-10-21), and that
            // one that would otherwise go to two is no fit is the record's reading
            return to is UnionType tu && Assignable(fu.Cases[0], tu.Cases[0]) && fu.Cases.Skip(1).All(c => CasesFor(c, tu.Cases.Skip(1).ToList()).Count == 1);
        }

        if (to is UnionType union)
        {
            return CasesFor(from, union.Cases).Count == 1;
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
    /// <c>int</c> are promoted to <c>int</c>, whatever their signedness; a narrow operand beside a
    /// <c>long</c> takes that type, and an unsigned narrow one beside a <c>uint</c> or a <c>ulong</c>
    /// too, so <c>byte + uint</c> is a <c>uint</c>; otherwise the type the other operand widens into
    /// (T23: <c>uint</c> widens into <c>long</c>), and where neither widens into the other the error
    /// (T9): <c>uint</c> beside <c>int</c>, <c>ulong</c> beside any signed type, and a signed narrow
    /// operand beside a <c>uint</c> or a <c>ulong</c>, since T9 judges the operands as written and a
    /// signed type never becomes unsigned without a word (T23): <c>sbyte + uint</c> is the error.
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

    /// <summary><paramref name="type"/> with each type parameter <paramref name="map"/> holds put in its place; a parameter the map lacks stays. A type a part of which becomes the error type is the error type (E1).</summary>
    public static KurzType Substitute(KurzType type, IReadOnlyDictionary<TypeParameterSymbol, KurzType> map)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(map);
        switch (type)
        {
            case TypeParameterType p:
                return map.TryGetValue(p.Symbol, out var t) ? t : type;
            case ListType l:
                {
                    var element = Substitute(l.Element, map);
                    return element is ErrorType ? Error : new ListType(element);
                }

            case DataType d:
                {
                    var arguments = d.TypeArguments.Select(a => Substitute(a, map)).ToList();
                    return arguments.Any(a => a is ErrorType) ? Error : new DataType(d.Symbol, arguments);
                }

            case UnionType u:
                return Union(u.Cases.Select(c => Substitute(c, map)));
            case FunctionType f:
                {
                    var parameters = f.Parameters.Select(p => Substitute(p, map)).ToList();
                    var result = Substitute(f.Result, map);
                    return parameters.Any(p => p is ErrorType) || result is ErrorType ? Error : new FunctionType(parameters, result);
                }

            case PointerType p:
                {
                    var pointee = Substitute(p.Pointee, map);
                    return pointee is ErrorType ? Error : new PointerType(pointee);
                }

            case CFunctionType f:
                {
                    var parameters = f.Parameters.Select(p => Substitute(p, map)).ToList();
                    var result = Substitute(f.Result, map);
                    return parameters.Any(p => p is ErrorType) || result is ErrorType ? Error : new CFunctionType(parameters, result);
                }

            default:
                return type;
        }
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
            PointerType p => Mentions(p.Pointee, parameter),
            CFunctionType f => f.Parameters.Any(p => Mentions(p, parameter)) || Mentions(f.Result, parameter),
            _ => false,
        };
    }
}
