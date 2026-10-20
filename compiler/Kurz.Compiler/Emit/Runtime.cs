namespace Kurz.Compiler.Emit;

/// <summary>
/// The functions of the C runtime (<c>Runtime/kurz_rt.c</c>) as the emitted IR declares them. Every
/// parameter and result is a scalar: a string, a list or a <c>decimal</c> crosses the boundary as its
/// two words, or through a pointer to where the runtime writes it, because the platforms pass a
/// 16-byte struct by value differently and the IR would have to know which one it is on; a
/// <c>longduration</c> crosses as its two halves the same way.
/// </summary>
internal static class Runtime
{
    public const string Init = "declare void @kz_init()";
    public const string Print = "declare void @kz_print(ptr, i64)";
    public const string Fail = "declare void @kz_fail(ptr, i32) noreturn";
    public const string Throw = "declare void @kz_throw(ptr, i64, i32) noreturn";
    public const string Alloc = "declare ptr @kz_alloc(i64)";
    public const string TextI64 = "declare void @kz_text_i64(i64, ptr)";
    public const string TextU64 = "declare void @kz_text_u64(i64, ptr)";
    public const string TextBool = "declare void @kz_text_bool(i8, ptr)";
    public const string TextChar = "declare void @kz_text_char(i32, ptr)";
    public const string Concat = "declare void @kz_concat(ptr, i64, ptr, i64, ptr)";
    public const string StrEq = "declare i8 @kz_str_eq(ptr, i64, ptr, i64)";
    public const string ListPush = "declare void @kz_list_push(ptr, ptr, i64)";
    public const string ListAt = "declare ptr @kz_list_at(ptr, i64, i64, i64, i32)";
    public const string StrBytes = "declare void @kz_str_bytes(ptr, i64, ptr)";
    public const string StrChars = "declare void @kz_str_chars(ptr, i64, ptr)";
    public const string Memcpy = "declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)";
    public const string TextF64 = "declare void @kz_text_f64(double, ptr)";
    public const string TextF32 = "declare void @kz_text_f32(float, ptr)";
    public const string TextDec = "declare void @kz_text_dec(i64, i64, ptr)";
    public const string TextDuration = "declare void @kz_text_duration(i64, ptr)";
    public const string TextLongDuration = "declare void @kz_text_longduration(i64, i64, ptr)";
    public const string F64Rem = "declare double @kz_f64_rem(double, double)";
    public const string F32Rem = "declare float @kz_f32_rem(float, float)";
    public const string DecAdd = "declare void @kz_dec_add(i64, i64, i64, i64, i32, ptr)";
    public const string DecSub = "declare void @kz_dec_sub(i64, i64, i64, i64, i32, ptr)";
    public const string DecMul = "declare void @kz_dec_mul(i64, i64, i64, i64, i32, ptr)";
    public const string DecDiv = "declare void @kz_dec_div(i64, i64, i64, i64, i32, ptr)";
    public const string DecRem = "declare void @kz_dec_rem(i64, i64, i64, i64, i32, ptr)";
    public const string DecCmp = "declare i32 @kz_dec_cmp(i64, i64, i64, i64)";
    public const string DecFromI64 = "declare void @kz_dec_from_i64(i64, ptr)";
    public const string DecFromU64 = "declare void @kz_dec_from_u64(i64, ptr)";
    public const string DecToI64 = "declare i64 @kz_dec_to_i64(i64, i64, ptr)";
    public const string DecToU64 = "declare i64 @kz_dec_to_u64(i64, i64, ptr)";
    public const string DecToF64 = "declare double @kz_dec_to_f64(i64, i64)";
    public const string DecFromF64 = "declare void @kz_dec_from_f64(double, ptr, ptr)";
    public const string DecFromF32 = "declare void @kz_dec_from_f32(float, ptr, ptr)";

    /// <summary>Every declaration above, in the order the module writes them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Init, Print, Fail, Throw, Alloc, TextI64, TextU64, TextBool, TextChar, Concat, StrEq, ListPush, ListAt, StrBytes, StrChars, Memcpy,
        TextF64, TextF32, TextDec, TextDuration, TextLongDuration, F64Rem, F32Rem,
        DecAdd, DecSub, DecMul, DecDiv, DecRem, DecCmp, DecFromI64, DecFromU64, DecToI64, DecToU64, DecToF64, DecFromF64, DecFromF32,
    ];

    /// <summary>The source of the runtime, carried in the compiler so that a build needs nothing beside the compiler and clang.</summary>
    public static string Source()
    {
        using var stream = typeof(Runtime).Assembly.GetManifestResourceStream("Kurz.Compiler.Runtime.kurz_rt.c")
            ?? throw new InvalidOperationException("the runtime's source is not embedded in the compiler");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
