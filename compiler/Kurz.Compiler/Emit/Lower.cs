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
/// and their count, the pointer of an empty string pointing at a constant of no bytes and never
/// <c>null</c> (R15); a pointer and a C function value are <c>ptr</c> (R10, R17), and so is a list, the
/// address of its block (M9); a function value is <c>%kz.fn</c>, the address of its code and of its
/// environment, null for a lambda that reads no variable (F11, F15). Every other type is not emitted:
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
        PointerType or CFunctionType or ListType => "ptr",
        FunctionType => "%kz.fn",
        TypeParameterType t => throw new InvalidOperationException($"the type parameter `{t}` was not substituted"),
        _ => throw new NotSupportedException(Refusal(type)),
    };

    /// <summary>The bytes a value of the type takes in memory, which <c>SizeOf</c> gives (R14) and a pointer's offset counts in (R10).</summary>
    public static int Size(KurzType type) => IrType(type) switch
    {
        "i1" or "i8" => 1,
        "i16" => 2,
        "i32" or "float" => 4,
        "i64" or "double" or "ptr" => 8,
        "%kz.str" or "%kz.fn" or "%kz.dec" or "i128" => 16,
        var other => throw new InvalidOperationException($"no size for `{other}`"),
    };

    /// <summary>The alignment of a value of the type in memory, as LLVM lays out its IR type: 16 for an <c>i128</c> (T29), else its size up to 8.</summary>
    public static int Align(KurzType type) => IrType(type) == "i128" ? 16 : Math.Min(Size(type), 8);

    /// <summary>Whether the type is one the emitter lays out: the integers, <c>float</c>, <c>double</c>, <c>decimal</c>, the time types, <c>bool</c>, <c>char</c>, <c>string</c>, <c>void</c>, pointers to them and C function types over them.</summary>
    public static bool Supported(KurzType type) => type switch
    {
        PrimitiveType p => p.Kind is Primitive.SByte or Primitive.Byte or Primitive.Short or Primitive.UShort or Primitive.Int or Primitive.UInt
            or Primitive.Long or Primitive.ULong or Primitive.Float or Primitive.Double or Primitive.Decimal or Primitive.Bool or Primitive.Char or Primitive.String
            or Primitive.Duration or Primitive.Timestamp or Primitive.LongDuration or Primitive.LongTimestamp,
        VoidType => true,
        PointerType p => Supported(p.Pointee),
        CFunctionType f => f.Parameters.All(Supported) && Supported(f.Result),
        _ => false,
    };

    /// <summary>A type the emitter does not lay out, as its <c>not supported</c> names it: with the rule that states the type, as the checker's name theirs.</summary>
    public static string Refusal(KurzType type)
    {
        var rule = type switch
        {
            ListType => "M9",
            DataType => "D1",
            UnionType => "D9",
            FunctionType => "F11",
            _ => null,
        };
        return $"a value of type `{type}`" + (rule is null ? string.Empty : $" ({rule})");
    }

    /// <summary>The type of a parameter or a result in a declaration of the C calling convention: a <c>bool</c> is C's <c>_Bool</c>, one byte with its high bits zero, as clang declares it.</summary>
    public static string CType(KurzType type) => type is PrimitiveType { Kind: Primitive.Bool } ? "i1 zeroext" : IrType(type);

    /// <summary>A text that names a type inside a quoted IR identifier.</summary>
    public static string Mangle(KurzType type) => type.Text.Replace('"', '\'');
}
