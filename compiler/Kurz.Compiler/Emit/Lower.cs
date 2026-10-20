using Kurz.Compiler.Semantics;

namespace Kurz.Compiler.Emit;

/// <summary>
/// How each Kurz type is laid out in the emitted IR. Integers are the LLVM integer of their width,
/// signed and unsigned alike (the operations tell them apart); a <c>bool</c> is <c>i1</c>; a
/// <c>char</c> is its code point as <c>i32</c>; a string is <c>%kz.str</c>, a pointer to UTF-8 bytes
/// and their count; a list is <c>%kz.list</c>, a pointer to a shared buffer and the count of items
/// this value sees; a <c>data</c> value is a pointer to a record on the heap whose first field is the
/// number of its type, so that a value held as its base type (D6) can be told from the others in a
/// <c>match</c> (C4); a union is <c>%kz.union</c>, the index of its case and a pointer to a box that
/// holds the case's value; a function value is <c>%kz.fn</c>, the code and its environment (F11, F15).
/// A <c>float</c> and a <c>double</c> are LLVM's <c>float</c> and <c>double</c>; a <c>decimal</c> is
/// <c>%kz.dec</c>, the four words of a <c>System.Decimal</c> as two 64-bit words: the low 64 bits of
/// the 96-bit magnitude, then its high 32 bits with the scale in the byte above them and the sign in
/// the top bit; a <c>duration</c> is its count of nanoseconds as <c>i64</c> and a <c>longduration</c>
/// as <c>i128</c> (T24, T29). The timestamps are not emitted: <see cref="Supported"/> says so.
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
            Primitive.Float => "float",
            Primitive.Double => "double",
            Primitive.Decimal => "%kz.dec",
            Primitive.Duration => "i64",
            Primitive.LongDuration => "i128",
            _ => throw new NotSupportedException($"a `{p}` is not emitted"),
        },
        VoidType => "void",
        ListType => "%kz.list",
        DataType => "ptr",
        UnionType => "%kz.union",
        FunctionType => "%kz.fn",
        TypeParameterType t => throw new InvalidOperationException($"the type parameter `{t}` was not substituted"),
        _ => throw new InvalidOperationException($"no layout for `{type}`"),
    };

    /// <summary>The bytes a value of the type takes in a record, a box or a list buffer.</summary>
    public static int Size(KurzType type) => IrType(type) switch
    {
        "i1" or "i8" => 1,
        "i16" => 2,
        "i32" or "float" => 4,
        "i64" or "double" or "ptr" => 8,
        "i128" or "%kz.dec" or "%kz.str" or "%kz.list" or "%kz.union" or "%kz.fn" => 16,
        var other => throw new InvalidOperationException($"no size for `{other}`"),
    };

    /// <summary>The alignment of a value of the type in a record, as LLVM lays it out: an <c>i128</c> at 16, everything else at its size up to 8.</summary>
    public static int Align(KurzType type) => IrType(type) == "i128" ? 16 : Math.Min(Size(type), 8);

    /// <summary>Whether the type is one the emitter lays out: every primitive type but the timestamps, and lists, data types, unions and functions made of them.</summary>
    public static bool Supported(KurzType type) => type switch
    {
        PrimitiveType p => !Types.IsTimestamp(p),
        VoidType => true,
        ListType l => Supported(l.Element),
        DataType d => d.TypeArguments.All(Supported),
        UnionType u => u.Cases.All(Supported),
        FunctionType f => f.Parameters.All(Supported) && Supported(f.Result),
        _ => false,
    };

    /// <summary>The named IR type of a data type's record: its type number, then its fields in order, the base's first (D7).</summary>
    public static string RecordName(DataType data) => $"%\"d.{data.Text}\"";

    /// <summary>The fields of a data type with the type arguments of this instance put in (T19).</summary>
    public static IReadOnlyList<KurzType> FieldTypes(DataType data)
    {
        var map = data.Symbol.TypeParameters.Zip(data.TypeArguments).ToDictionary(p => p.First, p => p.Second);
        return data.Symbol.Fields.Select(f => Types.Substitute(f.Type, map)).ToList();
    }

    /// <summary>A text that names a type inside a quoted IR identifier.</summary>
    public static string Mangle(KurzType type) => type.Text.Replace('"', '\'');
}
