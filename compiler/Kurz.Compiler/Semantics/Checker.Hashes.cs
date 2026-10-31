using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // ---- hashes (chapter 14) -----------------------------------------------------------------------

    /// <summary>The names of the hashes' types and functions, which every file sees (H1 to H4).</summary>
    private static bool IsHashName(string name) => name is "Sha256" or "Md5" or "Crc32" or "Hmac" or "Hex" or "FixedTime";

    /// <summary>The hasher a type's name names (H3), or null.</summary>
    private static Hasher? HasherOf(string name) => name switch
    {
        "Sha256" => Hasher.Sha256,
        "Md5" => Hasher.Md5,
        "Crc32" => Hasher.Crc32,
        "Hmac" => Hasher.Hmac,
        _ => null,
    };

    /// <summary>
    /// H1 to H4: a function of <c>Sha256</c>, <c>Md5</c>, <c>Crc32</c>, <c>Hmac</c>, <c>Hex</c> or <c>FixedTime</c>, a type and a verb (section
    /// 12, the owner, 2026-10-31, "`Sha256.Hash(b)`"): a digest is <c>Bytes</c> and the CRC a <c>uint</c> ("`Bytes`, CRC32 a `uint`"), a
    /// hasher's <c>New</c> ("Yes, as Rust"), <c>Hex.Encode</c> ("`Hex.Encode(b)`, lower case"), and the check of a tag and the comparison
    /// in fixed time ("Verify + fixed-time equal"). Every call of a function of <c>Md5</c> is the lint <c>md5-insecure</c> (H5).
    /// </summary>
    private Typed CheckHashFunction(string type, MemberAccess m, CallExpression c)
    {
        var bytes = new ListType(Types.Byte);
        Slot Bytes(string name) => new(name, bytes, false, false, false);
        var hasher = HasherOf(type) is { } h ? new HasherType(h) : null;
        (CallKind Kind, List<Slot> Slots, KurzType Result)? function = (type, m.Member) switch
        {
            ("Sha256" or "Md5", "Hash") => (CallKind.Hash, [Bytes("bytes")], bytes),
            ("Crc32", "Hash") => (CallKind.Hash, [Bytes("bytes")], Types.UInt),
            ("Sha256" or "Md5" or "Crc32", "New") => (CallKind.HasherNew, [], hasher!),
            ("Hmac", "Sha256") => (CallKind.HmacHash, [Bytes("key"), Bytes("message")], bytes),
            ("Hmac", "NewSha256") => (CallKind.HmacNew, [Bytes("key")], hasher!),
            ("Hmac", "VerifySha256") => (CallKind.HmacVerify, [Bytes("key"), Bytes("message"), Bytes("tag")], Types.Bool),
            ("Hex", "Encode") => (CallKind.HexEncode, [Bytes("bytes")], Types.String),
            ("FixedTime", "Equal") => (CallKind.FixedTimeEqual, [Bytes("a"), Bytes("b")], Types.Bool),
            _ => null,
        };
        if (function is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"`{type}` has no function `{m.Member}` (H1 to H4, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{type}.{m.Member}`"))
        {
            return Typed.Error;
        }

        var typed = Call([new Candidate { Slots = f.Slots, Result = f.Result }], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(f.Kind, null, null, hasher, NoMap, picked.Binding, picked.Slots.Count, f.Result));
            if (type == "Md5")
            {
                lints[c] = Diagnostic.Warning(
                    LintIds.Md5Insecure,
                    c.Line,
                    "two messages with one MD5 digest are made in seconds: a checksum may take it, a signature or a password takes `Sha256` or `Hmac` (H5)");
            }
        }

        return typed;
    }

    /// <summary>
    /// H3: a hasher's <c>Update(bytes)</c>, which adds the bytes to the message, and <c>Finish()</c>, which gives the digest of the message
    /// and starts the hasher over; each changes the hasher, which a <c>mut</c> variable holds, as a list's <c>Add</c> changes the list (M5).
    /// </summary>
    private Typed CheckHasherMethod(HasherType hasher, MemberAccess m, CallExpression c)
    {
        (CallKind Kind, List<Slot> Slots, KurzType Result)? method = m.Member switch
        {
            "Update" => (CallKind.HasherUpdate, [new Slot("bytes", new ListType(Types.Byte), false, false, false)], Types.Void),
            "Finish" => (CallKind.HasherFinish, [], hasher.Kind == Hasher.Crc32 ? Types.UInt : new ListType(Types.Byte)),
            _ => null,
        };
        if (method is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"a `{hasher}` has no member `{m.Member}` (H3, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        var typed = Call([new Candidate { Slots = f.Slots, Result = f.Result }], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(f.Kind, null, null, hasher, NoMap, picked.Binding, picked.Slots.Count, f.Result));
            MutReceiver(c, m.Target, c.Line, $"`{m.Member}` changes the hasher it is called on"); // after the call's own error nothing more (E1)
        }

        return typed;
    }
}
