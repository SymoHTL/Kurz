using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- hashes (chapter 14) -----------------------------------------------------------------------

    /// <summary>
    /// H1 to H4: a hash, a hasher's <c>New</c>, <c>Hex.Encode</c>, the check of a tag or a comparison in fixed time, each a call of a
    /// <c>pub</c> function of the standard library, whose Kurz code they are (design record, section 12, "In Kurz, as RustCrypto"), on the
    /// blocks of the lists of bytes it takes; a block the system refuses ends the process with <c>allocation-failed</c>, as a list's does
    /// (M9). A hasher's state is the block of a list of bytes, which its counts and its copies treat as one. The arguments are borrowed,
    /// with no count of their own: an argument whose place a later argument takes as <c>mut</c> is <c>changed-while-held</c> (M7), so that
    /// none changes while the call reads it.
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
    /// stored back in its place. <c>Finish</c> gives a new block of the digest, or the CRC, and starts the state over. The bytes of
    /// <c>Update</c> are borrowed before the place is read, which runs no code: an item of a list as the place is <c>not supported</c>
    /// (M5, M9), and an argument that takes the place's root as <c>mut</c> is <c>changed-while-held</c> (M7).
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

    /// <summary>
    /// H7: <c>Hex.Decode(s)</c>, the bytes of the string's hexadecimal digits, a new list the standard library's <c>HexDecode</c> makes, or
    /// the case <c>HexError</c>: <c>OddLength</c> at the string's length where that is odd, read first, else <c>InvalidDigit</c> at the
    /// first byte that is no digit (section 12, the owner, 2026-10-31, "Without input"). A block the system refuses ends the process with
    /// <c>allocation-failed</c> (M9). The string is borrowed for the call.
    /// </summary>
    private string EmitHexDecode(CallExpression c, CallInfo info)
    {
        var text = Arguments(c, info, _ => Types.String, _ => false, _ => null, cConvention: false, owned: false).Single()["%kz.str ".Length..];
        var union = (UnionType)Sub(info.Result);
        var error = (DataType)union.Cases[1];
        var (bytes, count, _) = StringParts(text);
        var made = fn.Alloca("ptr", "made");
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var at = fn.Value($"call i64 {StdFunction("HexDecode")}(ptr {bytes}, i64 {count}, ptr {made}, ptr {asked})");
        Refused(fn.Value($"icmp ne i64 {fn.Value($"load i64, ptr {asked}")}, 0"), asked);
        var result = fn.Alloca(Ir(union), "decoded");
        var done = fn.Label("hex.done");
        var decoded = fn.Label("hex.bytes");
        var failed = fn.Label("hex.failed");
        fn.CondBranch(fn.Value($"icmp slt i64 {at}, 0"), decoded, failed);
        fn.Block(decoded);
        fn.Emit($"store {Ir(union)} {MakeUnion(union, union.Cases[0], fn.Value($"load ptr, ptr {made}"))}, ptr {result}");
        fn.Branch(done);
        fn.Block(failed);
        var kind = (EnumType)Layout.Fields(error)[0];
        var odd = fn.Value($"icmp eq i64 {at}, {count}");
        var kindValue = fn.Value($"select i1 {odd}, {Ir(kind)} {KindValue(error, "OddLength")}, {Ir(kind)} {KindValue(error, "InvalidDigit")}");
        var withKind = fn.Value($"insertvalue {Ir(error)} undef, {Ir(kind)} {kindValue}, 0");
        var failure = fn.Value($"insertvalue {Ir(error)} {withKind}, i32 {fn.Value($"trunc i64 {at} to i32")}, 1"); // T15: a position fits an `int`
        fn.Emit($"store {Ir(union)} {MakeUnion(union, error, failure)}, ptr {result}");
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(union)}, ptr {result}");
    }

    /// <summary>Whether the data type is <c>HexError</c>, which no file can declare beside the one every file sees (H7, V9).</summary>
    private static bool IsHexError(KurzType type) => type is DataType { Symbol.Name: "HexError" };

    /// <summary>H7: in the helper of the text of a <c>HexError</c> in <c>%a0</c>: the words of its kind in lower case and its position, <c>invalid digit at 3</c>.</summary>
    private void HexErrorText(DataType error)
    {
        var kind = (EnumType)Layout.Fields(error)[0];
        var value = fn.Value($"extractvalue {Ir(error)} %a0, 0");
        var at = fn.Value($"sext i32 {fn.Value($"extractvalue {Ir(error)} %a0, 1")} to i64");
        var values = kind.Symbol.Values;
        var labels = values.Select(_ => fn.Label("kind")).ToList();
        var other = fn.Label("other");
        fn.Terminate($"switch {Ir(kind)} {value}, label %{other} [ {string.Join(" ", values.Select((_, i) => $"{Ir(kind)} {IntegerText(Lower.EnumStored(kind.Symbol, i), kind)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < values.Count; i++)
        {
            fn.Block(labels[i]);
            var number = fn.Value($"call %kz.str {RuntimeFunction("TextOfLong")}(i64 {at})");
            var words = fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {StringValue(Words(values[i]) + " at ")}, %kz.str {number})");
            Release(number, Types.String);
            fn.Return($"%kz.str {words}");
        }

        fn.Block(other);
        fn.Unreachable(); // a kind is one of its values (D18)
    }
}
