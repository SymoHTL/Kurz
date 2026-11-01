using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Semantics;

namespace Kurz.Compiler.Emit;

/// <summary>
/// How each Kurz type is laid out in the emitted IR. Integers are the LLVM integer of their width,
/// signed and unsigned alike (the operations tell them apart); a <c>bool</c> is <c>i1</c>; a
/// <c>char</c> is its code point as <c>i32</c>; a <c>float</c> and a <c>double</c> are LLVM's
/// <c>float</c> and <c>double</c>, IEEE 754's binary32 and binary64 (T2); a <c>decimal</c> is
/// <c>%kz.dec</c>, System.Decimal's 16 bytes as two 64-bit words, the low 64 bits of its integer, then its high 32 bits with its
/// flags above them (T2, <see cref="Emitter"/>); a <c>duration</c> and a
/// <c>timestamp</c> are the <c>i64</c> of their nanoseconds, a <c>longduration</c> and a <c>longtimestamp</c> the <c>i128</c>,
/// 16 bytes aligned to 16 as LLVM lays it out (T24, T29); a string is
/// <c>%kz.str</c>, a pointer to UTF-8 bytes
/// and their count, whose highest bit marks a string that holds a block of the standard library in front of the bytes, which a
/// literal does not, the pointer of an empty string pointing at a constant of no bytes and never
/// <c>null</c> (R15; design record, section 3, and section 4, the counter: the owner, 2026-10-29, "Bit in the count"); a pointer and a C function value are <c>ptr</c> (R10, R17), and so are a list, the
/// address of its block (M9), and a file, the address of its handle's block (S5); a function value is <c>%kz.fn</c>, the address of its code and of its
/// environment, null for a lambda that reads no variable (F11, F15); a value of an enum is its number, or its place among the values
/// where the declaration writes none, in the smallest of <c>i8</c>, <c>i16</c> and <c>i32</c> that holds every one, signed where a number
/// is negative, as Rust stores the value of an enum without fields (D12, D18; section 4, an assumption there). Every other type is not emitted:
/// <see cref="Supported"/> says so.
/// </summary>
internal static class Lower
{
    public static string IrType(KurzType type) => type switch
    {
        PrimitiveType p => p.Kind switch
        {
            Primitive.SByte or Primitive.Byte => "i8",
            Primitive.Short or Primitive.UShort => "i16",
            Primitive.Int or Primitive.UInt or Primitive.Char => "i32",
            Primitive.Long or Primitive.ULong or Primitive.Duration or Primitive.Timestamp => "i64",
            Primitive.LongDuration or Primitive.LongTimestamp => "i128",
            Primitive.Bool => "i1",
            Primitive.Float => "float",
            Primitive.Double => "double",
            Primitive.Decimal => "%kz.dec",
            Primitive.String => "%kz.str",
            _ => throw new NotSupportedException(Refusal(p)),
        },
        VoidType => "void",
        NullType => "{}", // N1: one value, of no size
        EnumType e => $"i{EnumBits(e.Symbol).ToString(CultureInfo.InvariantCulture)}",
        PointerType or CFunctionType or ListType or MapType or HandleType or HasherType => "ptr",
        UnionType u when Types.NonNull(u) is CFunctionType => "ptr", // R17: its `null` is the address 0 (Layout.Niche)
        UnionType u when Types.NonNull(u) is FunctionType => "%kz.fn", // N1: its `null` is the pair of no code and no environment (Layout.Niche)
        FunctionType => "%kz.fn",
        VectorType v => $"<{v.Count.ToString(CultureInfo.InvariantCulture)} x {IrType(v.Lane)}>", // R18: LLVM's vector of the lanes
        TypeParameterType t => throw new InvalidOperationException($"the type parameter `{t}` was not substituted"),
        _ => throw new NotSupportedException(Refusal(type)),
    };

    /// <summary>The bytes a value of the type takes in memory, which <c>SizeOf</c> gives (R14) and a pointer's offset counts in (R10).</summary>
    public static int Size(KurzType type) => type is VectorType v ? v.Bits / 8 : IrType(type) switch
    {
        "{}" => 0,
        "i1" or "i8" => 1,
        "i16" => 2,
        "i32" or "float" => 4,
        "i64" or "double" or "ptr" => 8,
        "%kz.str" or "%kz.fn" or "%kz.dec" or "i128" => 16,
        var other => throw new InvalidOperationException($"no size for `{other}`"),
    };

    /// <summary>The alignment of a value of the type in memory, as LLVM lays out its IR type: 16 for an <c>i128</c> (T29), a vector's size (R18), 1 for a type of no size, <c>null</c>'s <c>{}</c> (N1), else its size up to 8.</summary>
    public static int Align(KurzType type) => type is VectorType v ? v.Bits / 8 : IrType(type) == "i128" ? 16 : Math.Clamp(Size(type), 1, 8);

    /// <summary>Whether the type is one the emitter lays out: the integers, <c>float</c>, <c>double</c>, <c>decimal</c>, the time types, <c>bool</c>, <c>char</c>, <c>string</c>, <c>void</c>, <c>null</c>, of no size (N1), an enum's value (D12, D18), a file or a socket, a hasher, a vector (R18), pointers to them and C function types over them.</summary>
    public static bool Supported(KurzType type) => type switch
    {
        PrimitiveType p => p.Kind is Primitive.SByte or Primitive.Byte or Primitive.Short or Primitive.UShort or Primitive.Int or Primitive.UInt
            or Primitive.Long or Primitive.ULong or Primitive.Float or Primitive.Double or Primitive.Decimal or Primitive.Bool or Primitive.Char or Primitive.String
            or Primitive.Duration or Primitive.Timestamp or Primitive.LongDuration or Primitive.LongTimestamp,
        VoidType or NullType or EnumType or HandleType or HasherType or VectorType => true,
        PointerType p => Supported(p.Pointee),
        CFunctionType f => f.Parameters.All(Supported) && Supported(f.Result),
        _ => false,
    };

    /// <summary>A type the emitter does not lay out, as its <c>not supported</c> names it: with the rule that states the type, as the checker's name theirs.</summary>
    public static string Refusal(KurzType type)
    {
        var rule = type switch
        {
            ListType or MapType => "M9",
            DataType => "D1",
            UnionType => "D9",
            FunctionType => "F11",
            _ => null,
        };
        return $"a value of type `{type}`" + (rule is null ? string.Empty : $" ({rule})");
    }

    /// <summary>The value an enum's value is stored as: its number, or its place where the declaration writes no number.</summary>
    public static BigInteger EnumStored(EnumSymbol symbol, int index) => symbol.Numbered ? symbol.Numbers[index] : index;

    /// <summary>Whether the stored values of an enum are read as signed: one of its numbers is negative.</summary>
    public static bool EnumSigned(EnumSymbol symbol) => symbol.Numbers.Any(n => n.Sign < 0);

    /// <summary>The width of the smallest integer that holds every stored value of the enum, eight bits for one without values.</summary>
    private static int EnumBits(EnumSymbol symbol)
    {
        var stored = Enumerable.Range(0, symbol.Values.Count).Select(i => EnumStored(symbol, i)).ToList();
        if (stored.Count == 0)
        {
            return 8;
        }

        var signed = EnumSigned(symbol);
        foreach (var bits in (int[])[8, 16])
        {
            var low = signed ? -(BigInteger.One << (bits - 1)) : BigInteger.Zero;
            var high = (BigInteger.One << (signed ? bits - 1 : bits)) - 1;
            if (stored.All(v => v >= low && v <= high))
            {
                return bits;
            }
        }

        return 32;
    }

    /// <summary>The type of a parameter or a result in a declaration of the C calling convention: a <c>bool</c> is C's <c>_Bool</c>, one byte with its high bits zero, as clang declares it.</summary>
    public static string CType(KurzType type) => type is PrimitiveType { Kind: Primitive.Bool } ? "i1 zeroext" : IrType(type);

    /// <summary>A text that names a type inside a quoted IR identifier.</summary>
    public static string Mangle(KurzType type) => type.Text.Replace('"', '\'');
}
