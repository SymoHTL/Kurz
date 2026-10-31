using System.Globalization;
using System.Security.Cryptography;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the hashes (H1 to H6; design record, section 12) that no corpus case pins: a message of every count of bytes from 0
/// to 130, so that every count of bytes waiting in a block and the padding that takes a second block are read, gives the digests .NET's
/// SHA-256, MD5 and HMAC-SHA256 give and the CRC-32 this class computes bit by bit, and the same in two pieces cut at every place, with
/// a copy of the SHA-256 hasher taken between the pieces, under the allocator that makes every block it frees inaccessible; every call of a
/// function of <c>Md5</c> is the lint <c>md5-insecure</c> at its line, once, and the program is built (H5); and a build for Windows
/// builds the hashes, which make no call of the system (H1). The names and members the checker reads are pinned beside them.
/// </summary>
public sealed class HashTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>The bytes of the message of <see cref="Every_count_of_bytes_whole_and_in_two_pieces_gives_the_digests_of_the_standards"/>.</summary>
    private static byte[] Message() => Enumerable.Range(0, 130).Select(i => (byte)((i * 31 + 7) % 256)).ToArray();

    /// <summary>CRC-32/ISO-HDLC a bit at a time, as its definition reads, with no table: the check value of <c>123456789</c> is 0xCBF43926.</summary>
    private static uint Crc32(byte[] bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
        }

        return c ^ 0xFFFFFFFFu;
    }

    [Fact]
    public void Every_count_of_bytes_whole_and_in_two_pieces_gives_the_digests_of_the_standards()
    {
        // the key, of 70 bytes, is longer than a block and is hashed first (H1); `t` takes a copy of `s` between the pieces, so that the
        // first `Update` after it copies the block both hold and the second writes the block it is then alone in (H6, M3)
        const string text = """
            mut message = Bytes()
            while message.Count < 130 {
                message.Add(byte((message.Count * 31 + 7) % 256))
            }

            Bytes key = message[0..<70]
            mut differ = 0
            mut n = 0
            while n <= 130 {
                Bytes whole = message[0..<n]
                Bytes sha = Sha256.Hash(whole)
                Bytes md5 = Md5.Hash(whole)
                uint crc = Crc32.Hash(whole)
                Bytes mac = Hmac.Sha256(key, whole)
                print("{Hex.Encode(sha)} {Hex.Encode(md5)} {crc} {Hex.Encode(mac)}")
                mut k = 0
                while k <= n {
                    mut s = Sha256.New()
                    s.Update(whole[0..<k])
                    mut t = s
                    t.Update(whole[k..<n])
                    s.Update(whole[k..<n])
                    mut d = Md5.New()
                    d.Update(whole[0..<k])
                    d.Update(whole[k..<n])
                    mut c = Crc32.New()
                    c.Update(whole[0..<k])
                    c.Update(whole[k..<n])
                    mut m = Hmac.NewSha256(key)
                    m.Update(whole[0..<k])
                    m.Update(whole[k..<n])
                    if !FixedTime.Equal(s.Finish(), sha) || !FixedTime.Equal(t.Finish(), sha) || !FixedTime.Equal(d.Finish(), md5) || c.Finish() != crc || !FixedTime.Equal(m.Finish(), mac) {
                        differ = differ + 1
                    }

                    k = k + 1
                }

                n = n + 1
            }

            print(differ)

            """;
        var message = Message();
        var key = message[..70];
        var expected = Enumerable.Range(0, 131)
            .Select(n => message[..n])
            .Select(whole => string.Join(
                ' ',
                Convert.ToHexStringLower(SHA256.HashData(whole)),
#pragma warning disable CA5351 // MD5 is what the program under test computes (H1), and .NET's is the digest it is held to; nothing here relies on it for security
                Convert.ToHexStringLower(MD5.HashData(whole)),
#pragma warning restore CA5351
                Crc32(whole).ToString(CultureInfo.InvariantCulture),
                Convert.ToHexStringLower(HMACSHA256.HashData(key, whole))))
            .Append("0")
            .ToList();
        Assert.Equal(0xCBF43926u, Crc32("123456789"u8.ToArray())); // the check value: the reference computation of the test is CRC-32/ISO-HDLC
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Compile(text, runtime);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "hash-pieces" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void Every_call_of_a_function_of_md5_is_the_lint_md5_insecure_once_at_its_line_and_the_program_is_built()
    {
        // `Update` and `Finish` of an `Md5` are no call of a function of `Md5`, and `Sha256` gets none (the positive control); two calls on
        // one line are two lints, and a call the checker reads twice, in a list literal that is an argument (M11), is one
        const string text = """
            mut h = Md5.New()
            h.Update("a".Bytes)
            print(Hex.Encode(h.Finish()))
            print(Sha256.Hash(Bytes()).Count)
            print(Md5.Hash(Bytes()).Count + Md5.Hash("a".Bytes).Count)
            void Show(List<Bytes> all) {
                print(all.Count)
            }

            Show([Md5.Hash(Bytes())])

            """;
        var compilation = Compile(text, toolchain.Runtime);
        Assert.NotNull(compilation.Program); // a warning builds the program
        var lints = compilation.Diagnostics.Sorted;
        Assert.Equal(
            [(DiagnosticKind.Warning, "md5-insecure", 1), (DiagnosticKind.Warning, "md5-insecure", 5), (DiagnosticKind.Warning, "md5-insecure", 5), (DiagnosticKind.Warning, "md5-insecure", 10)],
            lints.Select(d => (d.Kind, d.Id, d.Line)));
        Assert.All(lints, d => Assert.False(d.IsError || d.IsNotSupported));
        Assert.Equal(
            "program.kz:1: warning: md5-insecure: two messages with one MD5 digest are made in seconds: a checksum may take it, a signature or a tag takes `Sha256` or `Hmac` (H5)",
            lints[0].At("program.kz")); // no password: one is stored with a slow, salted function, which SHA-256 and HMAC are not
        Assert.Matches(@"^1: warning md5-insecure: .+ \(H5\)$", lints[0].ToString());
        Assert.Empty(Compile("print(Sha256.Hash(Bytes()).Count)\n", toolchain.Runtime).Diagnostics.Items);
    }

    [Fact]
    public void A_build_for_windows_builds_the_hashes_which_make_no_call_of_the_system()
    {
        const string text = """
            mut h = Hmac.NewSha256("key".Bytes)
            h.Update(Sha256.Hash(Bytes()))
            mut c = Crc32.New()
            c.Update(Bytes())
            print("{Hex.Encode(h.Finish())} {c.Finish()} {Crc32.Hash(Bytes())} {Hmac.VerifySha256(Bytes(), Bytes(), Bytes())} {FixedTime.Equal(Bytes(), Bytes())}")
            print(Md5.Hash(Hmac.Sha256(Bytes(), Bytes())))

            """;
        var windows = Compile(text, Builder.CheckRuntime(Target.Windows));
        Assert.True(windows.Program is not null, string.Join("; ", windows.Diagnostics.Sorted));
        Assert.Equal([(DiagnosticKind.Warning, 6)], windows.Diagnostics.Items.Select(d => (d.Kind, d.Line)));
        Assert.Null(Compile("print(File.Exists(\"a.txt\"))\n", Builder.CheckRuntime(Target.Windows)).Program); // the positive control: a call on a file is refused there (S11)
    }

    [Theory]
    [InlineData("print(Sha256.Digest(Bytes()))\n", "unknown-name", 1)] // H1: a type and a verb, the verb `Hash`
    [InlineData("print(Hash.Sha256(Bytes()))\n", "unknown-name", 1)]
    [InlineData("print(Crc32.Hash(\"abc\"))\n", "type-mismatch", 1)] // H1: a string through `s.Bytes`
    [InlineData("print(Hex.Encode<int>(Bytes()))\n", "syntax", 1)]
    [InlineData("mut h = Sha256.New()\nh.Reset()\n", "unknown-name", 2)] // H3: `Update` and `Finish`
    [InlineData("mut h = Sha256.New()\nprint(h.Count)\n", "unknown-name", 2)]
    [InlineData("h = Crc32.New()\nprint(h.Finish())\n", "mut-required", 2)]
    [InlineData("mut h = Hmac.NewSha256(Bytes())\nmut g = Hmac.NewSha256(Bytes())\nprint(h != g)\n", "type-mismatch", 3)] // H6
    [InlineData("Map<Sha256, int> m = Map<Sha256, int>()\nprint(m.Count)\n", "type-mismatch", 1)] // H6, M9: `==` takes no hasher, and a key is a value of a type `==` compares
    [InlineData("data Hex(int Digits)\nprint(Hex(1))\n", "redeclared", 1)] // H1: the names every file sees, as a type, a function, a variable and a parameter
    [InlineData("int Hex(int n) => n\nprint(1)\n", "redeclared", 1)]
    [InlineData("int FixedTime = 1\nprint(FixedTime)\n", "redeclared", 1)]
    [InlineData("void Show(int Md5) {\n    print(Md5)\n}\n\nShow(1)\n", "redeclared", 1)]
    [InlineData("void Show() {\n    int Hex = 1\n    print(Hex)\n}\n\nShow()\n", "redeclared", 2)] // at any level
    [InlineData("data Holder(Sha256 Hasher)\n\na = Holder(Sha256.New())\nb = Holder(Sha256.New())\nprint(a == b)\n", "type-mismatch", 5)] // H6, D3
    [InlineData("Bytes Change(mut Bytes k) {\n    k = \"x\".Bytes\n    return k\n}\n\nmut key = \"k\".Bytes\nprint(Hmac.Sha256(key, Change(mut key)))\n", "changed-while-held", 7)] // M7: the borrowed arguments of a hash
    [InlineData("Bytes Change(mut Bytes k) {\n    k = \"x\".Bytes\n    return k\n}\n\nmut key = \"k\".Bytes\nprint(Hmac.VerifySha256(key, \"m\".Bytes, Change(mut key)))\n", "changed-while-held", 7)]
    [InlineData("Bytes Change(mut Bytes k) {\n    k.Add(1)\n    return k\n}\n\nmut a = \"ab\".Bytes\nprint(FixedTime.Equal(a, Change(mut a)))\n", "changed-while-held", 7)]
    [InlineData("data Holder(Sha256 Hasher)\n\nBytes Make(mut Holder h) {\n    h = Holder(Sha256.New())\n    return \"abc\".Bytes\n}\n\nmut holder = Holder(Sha256.New())\nholder.Hasher.Update(Make(mut holder))\n", "changed-while-held", 9)] // M7: the receiver's root
    [InlineData("mut h = Sha256.New()\nh = Md5.New()\nprint(h)\n", "type-mismatch", 2)] // H3: four types
    public void The_checker_holds_the_names_the_parameters_and_the_members_of_the_hashes(string text, string id, int line)
    {
        var compilation = Compile(text, toolchain.Runtime);
        Assert.Null(compilation.Program);
        Assert.Contains((id, line), compilation.Diagnostics.Items.Where(d => d.IsError).Select(d => (d.Id, d.Line)));
    }

    [Fact]
    public void A_field_takes_a_name_of_the_hashes_and_the_items_of_a_list_take_no_update_from_this_compiler()
    {
        // H1: a field is no declaration of a name every file sees; H3, M5: an item of a list is a receiver this compiler does not read,
        // so that the bytes `Update` borrows are read before a place that runs no code (compiler/README.md)
        var field = Compile("data Point(int Hex)\n\nprint(Point(1).Hex)\n", toolchain.Runtime);
        Assert.True(field.Program is not null, string.Join("; ", field.Diagnostics.Sorted));
        var item = Compile("mut hs = [Sha256.New()]\nhs[0].Update(\"abc\".Bytes)\nprint(hs)\n", toolchain.Runtime);
        Assert.Null(item.Program);
        var refused = Assert.Single(item.Diagnostics.Items);
        Assert.Equal((DiagnosticKind.NotSupported, 2, "a `mut` method on an element of a list (M5, M9)"), (refused.Kind, refused.Line, refused.Message));
        Assert.NotNull(Compile("mut h = Sha256.New()\nh.Update(\"abc\".Bytes)\nprint(h)\n", toolchain.Runtime).Program); // the positive control
    }

    [Fact]
    public void An_empty_key_of_hmac_is_a_key_of_no_bytes()
    {
        // FIPS 198-1: K0 is the key padded with zeros to 64 bytes (RFC 2104's step (1)), all zeros for an empty key, whose block the standard library holds as null
        const string text = """
            print(Hex.Encode(Hmac.Sha256(Bytes(), "abc".Bytes)))
            mut h = Hmac.NewSha256(Bytes())
            h.Update("abc".Bytes)
            print(Hex.Encode(h.Finish()))
            print(Hmac.VerifySha256(Bytes(), "abc".Bytes, Hmac.Sha256(Bytes(), "abc".Bytes)))
            print(Hmac.VerifySha256(Bytes(), "abd".Bytes, Hmac.Sha256(Bytes(), "abc".Bytes)))

            """;
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "hash-empty-key"));
        Assert.Equal((0, ""), (code, error));
        var tag = Convert.ToHexStringLower(HMACSHA256.HashData([], "abc"u8));
        Assert.Equal([tag, tag, "true", "false"], output);
    }

    [Fact]
    public void A_hasher_is_a_type_a_parameter_a_field_an_item_and_a_case_can_take_and_a_function_changes_through_mut()
    {
        // H3, H6: the type names `Sha256`, `Md5`, `Crc32` and `Hmac` stand where a type is written, a field's hasher takes `Update` through
        // its path (M5), and a `mut` parameter changes the caller's hasher (M7), a copy of the field's, which the field's leaves as it was
        const string text = """
            data Holder(Sha256 Hasher, string Name)

            void Feed(mut Sha256 into, Bytes bytes) {
                into.Update(bytes)
            }

            mut holder = Holder(Sha256.New(), "x")
            holder.Hasher.Update("ab".Bytes)
            mut h = holder.Hasher
            Feed(mut h, "c".Bytes)
            print(Hex.Encode(h.Finish()))
            print(Hex.Encode(holder.Hasher.Finish()))
            print(holder)
            List<Crc32> all = [Crc32.New()]
            print(all)
            Md5 | Hmac either = Hmac.NewSha256(Bytes())
            match either {
                Md5 => print("md5")
                Hmac m => print(m)
            }

            """;
        var compilation = Compile(text, toolchain.Runtime);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "hash-holder"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal([Convert.ToHexStringLower(SHA256.HashData("abc"u8)), Convert.ToHexStringLower(SHA256.HashData("ab"u8)), "Holder { Hasher = Sha256, Name = x }", "[Crc32]", "Hmac"], output);
    }
}
