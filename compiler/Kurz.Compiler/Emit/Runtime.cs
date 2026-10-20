namespace Kurz.Compiler.Emit;

/// <summary>
/// The functions of the C runtime (<c>Runtime/kurz_rt.c</c>) as the emitted IR declares them. Every
/// parameter and result is a scalar: a string or a list crosses the boundary as its two fields, or
/// through a pointer to where the runtime writes it, because the platforms pass a 16-byte struct
/// by value differently and the IR would have to know which one it is on.
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

    /// <summary>Every declaration above, in the order the module writes them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Init, Print, Fail, Throw, Alloc, TextI64, TextU64, TextBool, TextChar, Concat, StrEq, ListPush, ListAt, StrBytes, StrChars, Memcpy,
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
