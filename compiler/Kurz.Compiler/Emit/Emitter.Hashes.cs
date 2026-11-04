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
                // H1: the register a local of `Crc32Hash`, which asks for no block, so nothing is refused
                return fn.Value($"call i32 {StdFunction("Crc32Hash")}({listed}ptr {Crc32Table()})");

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
            var table = hasher == Hasher.Crc32 ? $", ptr {Crc32Table()}" : "";
            fn.Emit($"call void {StdFunction($"{hasher}Update")}(ptr {block}, ptr {data}{table})");
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
        var at = fn.Value($"call i64 {StdFunction("HexDecode")}(ptr {bytes}, i64 {count}, ptr {HexDigits()}, ptr {made}, ptr {asked})");
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

    /// <summary>
    /// The global that holds the value of each byte as a hexadecimal digit (H7), written once into a program that decodes hexadecimal:
    /// 256 bytes, 0 to 15 for <c>0</c> to <c>9</c>, <c>A</c> to <c>F</c> and <c>a</c> to <c>f</c>, and 255 for every other byte, as
    /// .NET's <c>HexConverter.CharToHexLookup</c> holds them; the standard library's <c>HexDecode</c> reads two of them for each byte it
    /// makes, as .NET's scalar <c>FromHexString</c> does. The <c>hex</c> crate, whose <c>decode</c> H7 cites for what is read, compares each
    /// byte with the three ranges (<c>val</c>): a text of 32 MiB decoded 5 times took 468 ms so in Kurz and 610 ms in the crate's own Rust,
    /// against 47 ms through this table (release builds, the best of seven).
    /// </summary>
    private string HexDigits()
    {
        const string name = "@\"hex.digits\"";
        if (helpers.Add(name))
        {
            var values = Enumerable.Range(0, 256).Select(b => (char)b switch
            {
                >= '0' and <= '9' => b - '0',
                >= 'A' and <= 'F' => b - 'A' + 10,
                >= 'a' and <= 'f' => b - 'a' + 10,
                _ => 255,
            });
            module.Global($"{name} = private unnamed_addr constant [256 x i8] [{string.Join(", ", values.Select(v => $"i8 {(sbyte)v}"))}]");
        }

        return name;
    }

    /// <summary>
    /// H1, H3: the table of CRC-32/ISO-HDLC that the standard library's <c>Crc32Update</c> and <c>Crc32Hash</c> take, 16 rows of 256
    /// remainders of the reflected polynomial 0xEDB88320, as crc32fast's <c>CRC32_TABLE</c> holds them: row 0 a byte's remainder, as zlib's
    /// table, and row k the remainder of a byte followed by k bytes of zero, which its slicing-by-16 reads for the byte 15 - k places
    /// from the end of 16. Each call of a CRC built row 0 into the hasher's block before, a byte at a time: 16 MiB hashed 10 times took
    /// 250.19 ms so, against 25.08 ms with this table and 24.58 ms in crc32fast's software path (release builds, the best of seven).
    /// </summary>
    private string Crc32Table()
    {
        const string name = "@\"crc32.table\"";
        if (helpers.Add(name))
        {
            var table = new uint[16 * 256];
            for (uint b = 0; b < 256; b++)
            {
                var c = b;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[b] = c;
            }

            for (var row = 1; row < 16; row++)
            {
                for (var b = 0; b < 256; b++)
                {
                    var before = table[((row - 1) * 256) + b];
                    table[(row * 256) + b] = (before >> 8) ^ table[before & 255];
                }
            }

            module.Global($"{name} = private unnamed_addr constant [4096 x i32] [{string.Join(", ", table.Select(v => $"i32 {(int)v}"))}]");
        }

        return name;
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
            var words = Joined([LiteralPiece(Words(values[i]) + " at "), NumberPiece(at, true)]);
            fn.Return($"%kz.str {words}");
        }

        fn.Block(other);
        fn.Unreachable(); // a kind is one of its values (D18)
    }
}
