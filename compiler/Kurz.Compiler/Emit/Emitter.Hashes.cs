using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- hashes (chapter 14) -----------------------------------------------------------------------

    /// <summary>
    /// H1 to H4: a hash, a hasher's <c>New</c>, <c>Hex.Encode</c>, the check of a tag or a comparison in fixed time, each a call of a
    /// <c>pub</c> function of the standard library, whose Kurz code they are (design record, section 12, "In Kurz, as RustCrypto"), on the
    /// blocks of the lists of bytes it takes; a block the system refuses ends the process with <c>allocation-failed</c>, as a list's does
    /// (M9). A hasher's state is the block of a list of bytes, which its counts and its copies treat as one.
    /// </summary>
    private string? EmitHashCall(CallExpression c, CallInfo info)
    {
        var hasher = (info.Target as HasherType)?.Kind;
        if (info.Kind is CallKind.HasherUpdate or CallKind.HasherFinish)
        {
            return EmitHasherMethod(c, info, hasher!.Value);
        }

        var bytes = new ListType(Types.Byte);
        var arguments = Arguments(c, info, _ => bytes, _ => false, _ => null, cConvention: false, owned: false)
            .Select(a => a["ptr ".Length..])
            .ToList();
        if (info.Kind == CallKind.FixedTimeEqual)
        {
            return fn.Value($"call i1 {StdFunction("FixedTimeEqual")}(ptr {arguments[0]}, ptr {arguments[1]})");
        }

        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var listed = string.Concat(arguments.Select(a => $"ptr {a}, "));
        switch (info.Kind)
        {
            case CallKind.Hash when hasher == Hasher.Crc32:
                {
                    var crc = fn.Value($"call i32 {StdFunction("Crc32Hash")}({listed}ptr {asked})");
                    Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
                    return crc;
                }

            case CallKind.HmacVerify:
                {
                    var equal = fn.Value($"call i1 {StdFunction("HmacVerifySha256")}({listed}ptr {asked})");
                    Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
                    return equal;
                }

            case CallKind.HexEncode:
                {
                    // the bytes of a new string's block, or null for no bytes, which is the empty string (H2)
                    var text = fn.Value($"call ptr {StdFunction("HexEncode")}({listed}ptr {asked})");
                    Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
                    var count = fn.Value($"shl i64 {fn.Value($"zext i32 {ListCount(arguments[0])} to i64")}, 1");
                    var none = fn.Value($"icmp eq ptr {text}, null");
                    var (empty, _) = module.String(string.Empty);
                    var pointer = fn.Value($"select i1 {none}, ptr {empty}, ptr {text}");
                    var marked = fn.Value($"select i1 {none}, i64 0, i64 {fn.Value($"or i64 {count}, -9223372036854775808")}");
                    var withPointer = fn.Value($"insertvalue %kz.str undef, ptr {pointer}, 0");
                    return fn.Value($"insertvalue %kz.str {withPointer}, i64 {marked}, 1");
                }

            default:
                {
                    var name = info.Kind switch
                    {
                        CallKind.Hash => $"{hasher}Hash",
                        CallKind.HasherNew => $"{hasher}New",
                        CallKind.HmacHash => "HmacSha256",
                        CallKind.HmacNew => "HmacNew",
                        _ => throw new InvalidOperationException(info.Kind.ToString()),
                    };
                    var made = fn.Value($"call ptr {StdFunction(name)}({listed}ptr {asked})");
                    Refused(fn.Value($"icmp eq ptr {made}, null"), asked);
                    return made;
                }
        }
    }

    /// <summary>
    /// H3: a hasher's <c>Update(bytes)</c> and <c>Finish()</c>, on the block in the hasher's place, a <c>mut</c> variable or a path of fields
    /// from one (M5): a block other values hold is copied first, as a list's is before a write (M3), and the block the hasher then holds is
    /// stored back in its place. <c>Finish</c> gives a new block of the digest, or the CRC, and starts the state over.
    /// </summary>
    private string? EmitHasherMethod(CallExpression c, CallInfo info, Hasher hasher)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var data = info.Kind == CallKind.HasherUpdate
            ? Arguments(c, info, _ => new ListType(Types.Byte), _ => false, _ => null, cConvention: false, owned: false).Single()["ptr ".Length..]
            : null;
        var place = CollectionPlace(member.Target);
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var block = fn.Value($"call ptr {StdFunction("HasherOwned")}(ptr {fn.Value($"load ptr, ptr {place}")}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {block}, null"), asked);
        fn.Emit($"store ptr {block}, ptr {place}");
        if (data is not null)
        {
            fn.Emit($"call void {StdFunction($"{hasher}Update")}(ptr {block}, ptr {data})");
            return null;
        }

        if (hasher == Hasher.Crc32)
        {
            return fn.Value($"call i32 {StdFunction("Crc32Finish")}(ptr {block})");
        }

        var digest = fn.Value($"call ptr {StdFunction($"{hasher}Finish")}(ptr {block}, ptr {asked})");
        Refused(fn.Value($"icmp eq ptr {digest}, null"), asked);
        return digest;
    }
}
