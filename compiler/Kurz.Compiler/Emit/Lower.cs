using Kurz.Compiler.Semantics;

namespace Kurz.Compiler.Emit;

/// <summary>
/// How each Kurz type is laid out in the emitted IR. Integers are the LLVM integer of their width,
/// signed and unsigned alike (the operations tell them apart); a <c>bool</c> is <c>i1</c>; a
/// <c>char</c> is its code point as <c>i32</c>; a string is <c>%kz.str</c>, a pointer to UTF-8 bytes
/// and their count, the pointer of an empty string pointing at a constant of no bytes and never
/// <c>null</c> (R15); a pointer and a C function value are <c>ptr</c> (R10, R17). Every other type is
/// not emitted: <see cref="Supported"/> says so.
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
            Primitive.Long or Primitive.ULong => "i64",
            Primitive.Bool => "i1",
            Primitive.String => "%kz.str",
            _ => throw new NotSupportedException(Refusal(p)),
        },
        VoidType => "void",
        PointerType or CFunctionType => "ptr",
        TypeParameterType t => throw new InvalidOperationException($"the type parameter `{t}` was not substituted"),
        _ => throw new NotSupportedException(Refusal(type)),
    };

    /// <summary>The bytes a value of the type takes in memory, which <c>SizeOf</c> gives (R14) and a pointer's offset counts in (R10).</summary>
    public static int Size(KurzType type) => IrType(type) switch
    {
        "i1" or "i8" => 1,
        "i16" => 2,
        "i32" => 4,
        "i64" or "ptr" => 8,
        "%kz.str" => 16,
        var other => throw new InvalidOperationException($"no size for `{other}`"),
    };

    /// <summary>Whether the type is one the emitter lays out: the integers, <c>bool</c>, <c>char</c>, <c>string</c>, <c>void</c>, pointers to them and C function types over them.</summary>
    public static bool Supported(KurzType type) => type switch
    {
        PrimitiveType p => p.Kind is Primitive.SByte or Primitive.Byte or Primitive.Short or Primitive.UShort or Primitive.Int or Primitive.UInt
            or Primitive.Long or Primitive.ULong or Primitive.Bool or Primitive.Char or Primitive.String,
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
            PrimitiveType { Kind: Primitive.Float or Primitive.Double or Primitive.Decimal } => "T2",
            PrimitiveType { Kind: Primitive.Duration or Primitive.Timestamp or Primitive.LongDuration or Primitive.LongTimestamp } => "T13",
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
