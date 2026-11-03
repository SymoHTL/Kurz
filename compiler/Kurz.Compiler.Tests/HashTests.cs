using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
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

    /// <summary>The register of CRC-32/ISO-HDLC, from 0, after the byte <paramref name="b"/> and <paramref name="zeros"/> bytes of zero, a bit at a time.</summary>
    private static uint Remainder(byte b, int zeros)
    {
        uint c = b;
        for (var k = 0; k < 8 * (zeros + 1); k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }

        return c;
    }

    [Fact]
    public void A_crc_reads_a_table_of_16_rows_which_a_program_that_hashes_one_holds_as_a_constant()
    {
        // crc32fast's `CRC32_TABLE`, which its slicing-by-16 reads: 16 rows of 256, row k the remainder of a byte followed by k bytes of
        // zero, row 0 zlib's table; a constant of the program, which `Crc32.Hash` and a hasher's `Update` pass to the standard library,
        // and which a program that hashes no CRC does not hold, its size unchanged (the positive control: the first program holds it)
        var program = Compile("mut c = Crc32.New()\nc.Update(\"a\".Bytes)\nprint(Crc32.Hash(Bytes()) + c.Finish())\n", toolchain.Runtime).Program;
        Assert.True(program is not null, "no program");
        var table = Regex.Match(program.Ir, @"\n@""crc32\.table"" = private unnamed_addr constant \[4096 x i32\] \[([^\]]*)\]");
        Assert.True(table.Success, "no table of 4096 words");
        var words = table.Groups[1].Value.Split(", ").Select(w => unchecked((uint)int.Parse(w["i32 ".Length..], CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(Enumerable.Range(0, 16).SelectMany(k => Enumerable.Range(0, 256).Select(b => Remainder((byte)b, k))), words);
        Assert.Equal(0x77073096u, words[1]);
        Assert.Equal(2, Regex.Count(program.Ir, @"call (i32|void) @""std\.Crc32(Hash|Update)[^""]*""\([^)]*ptr @""crc32\.table"""));
        var none = Compile("print(Hex.Encode(Sha256.Hash(Bytes())))\n", toolchain.Runtime).Program;
        Assert.True(none is not null, "no program");
        Assert.DoesNotContain("crc32.table", none.Ir, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crc_block_the_system_refuses_reports_the_bytes_of_a_hasher_s_block()
    {
        // `Crc32.New()` asks for a hasher's block: 16 bytes of a list's counts and 1028 of the register and the 1024 bytes after it,
        // where the table stood before it was the program's, which keep the bytes `allocation-failed` names (H1); `Crc32.Hash` asks
        // for none (the test below)
        foreach (var (text, name) in new[] { ("mut c = Crc32.New()\nprint(c.Finish())\n", "crc-new") })
        {
            var refused = Compile(text, EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value));
            Assert.True(refused.Program is not null, string.Join("; ", refused.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(refused.Program, name + "-refused"));
            Assert.Equal((1, "error: allocation-failed (1044 bytes)\n"), (code, error));
            Assert.Empty(output);
            var granted = Compile(text, toolchain.Runtime); // the positive control
            Assert.True(granted.Program is not null, string.Join("; ", granted.Diagnostics.Sorted));
            var (grantedCode, printed, grantedError) = Toolchain.Run(toolchain.Build(granted.Program, name + "-granted"));
            Assert.Equal((0, ""), (grantedCode, grantedError));
            Assert.Equal(["0"], printed);
        }
    }

    [Fact]
    public void Crc32_Hash_keeps_its_register_in_a_local_and_asks_for_no_block()
    {
        // H1: `Crc32.Hash` keeps its register in a local of `Crc32Hash`, as crc32fast's `hash` keeps its state, so it asks the system
        // for no block (the owner, 2026-11-02, issue 157, comment 6573114059: the bytes `allocation-failed` names follow the layout).
        // Where the system refuses every block, a program that hashes no bytes ends as it does with memory, where a block of the
        // hasher's ended it with `allocation-failed`; it prints only where the CRC is not 0, and a print asks for a block of its own,
        // so a wrong CRC ends it with `allocation-failed` too. The IR of `Crc32Hash` calls `Crc32Bytes`, the positive control, and
        // neither `Crc32New` nor `ByteBlock`
        var refused = Compile("if Crc32.Hash(Bytes()) != 0 {\n    print(1)\n}\n", EmitterTests.RuntimeWithoutMemory(Builder.HostTarget!.Value));
        Assert.True(refused.Program is not null, string.Join("; ", refused.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(refused.Program, "crc-hash-no-block"));
        Assert.Equal((0, ""), (code, error));
        Assert.Empty(output);
        var ir = refused.Program.Ir;
        var start = ir.IndexOf("define internal i32 @\"std.Crc32Hash.", StringComparison.Ordinal);
        Assert.True(start >= 0, "the program holds the standard library's `Crc32Hash`");
        var hash = ir[start..];
        hash = hash[..hash.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Contains("call i32 @\"std.Crc32Bytes.", hash, StringComparison.Ordinal);
        Assert.DoesNotContain("@\"std.Crc32New.", hash, StringComparison.Ordinal);
        Assert.DoesNotContain("@\"std.ByteBlock.", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crc_hasher_copied_between_two_updates_goes_on_apart_from_its_copy_and_finish_starts_each_over()
    {
        // H6: a copy of a `Crc32` hasher taken between two `Update`s is a value, the first `Update` after it writing a block of its own,
        // so each goes on with its own bytes, against the CRC this class computes bit by bit; `Finish` starts each over, so a second
        // `Finish` gives the CRC of no bytes (H3); under both allocators
        const string text = """
            mut message = Bytes()
            while message.Count < 130 {
                message.Add(byte((message.Count * 31 + 7) % 256))
            }

            mut c = Crc32.New()
            c.Update(message[0..<70])
            mut t = c
            t.Update(message[70..<73])
            c.Update(message[73..<130])
            print(c.Finish())
            print(t.Finish())
            print(c.Finish())
            print(t.Finish())

            """;
        var message = Message();
        string[] expected = [Crc32([.. message[..70], .. message[73..]]).ToString(CultureInfo.InvariantCulture), Crc32(message[..73]).ToString(CultureInfo.InvariantCulture), "0", "0"];
        Assert.NotEqual(expected[0], expected[1]); // the two diverge
        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Compile(text, runtime);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "crc-copy" + suffix));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void Sha256_gives_the_same_digests_through_the_sha_instructions_and_through_the_plain_rounds()
    {
        // R18 in std: a block, and the whole blocks a feed holds, take the SHA instructions where a test of the CPU finds them, as the
        // `shani` backend of Rust's `sha2` crate does, and the plain rounds elsewhere. Three builds of one program: as std is, its test
        // reading the CPU's word of this machine, which has the instructions; for `native`, which names them, the test a constant; and with
        // std's tests turned `false`, the plain rounds alone. Each gives the standard's vectors (FIPS 180-2's three messages and two of the
        // NIST examples) and .NET's digest of every count of bytes from 0 to 200, whole and in two pieces cut at every place.
        const string text = """
            mut message = Bytes()
            while message.Count < 200 {
                message.Add(byte((message.Count * 31 + 7) % 256))
            }

            mut differ = 0
            mut n = 0
            while n <= 200 {
                Bytes whole = message[0..<n]
                Bytes sha = Sha256.Hash(whole)
                print(Hex.Encode(sha))
                mut k = 0
                while k <= n {
                    mut s = Sha256.New()
                    s.Update(whole[0..<k])
                    s.Update(whole[k..<n])
                    if !FixedTime.Equal(s.Finish(), sha) {
                        differ = differ + 1
                    }

                    k = k + 1
                }

                n = n + 1
            }

            print(differ)
            print(Hex.Encode(Sha256.Hash("".Bytes)))
            print(Hex.Encode(Sha256.Hash("abc".Bytes)))
            print(Hex.Encode(Sha256.Hash("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq".Bytes)))
            print(Hex.Encode(Sha256.Hash("abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu".Bytes)))
            mut a = Bytes()
            while a.Count < 1000 {
                a.Add(byte(97))
            }

            mut million = Sha256.New()
            mut i = 0
            while i < 1000 {
                million.Update(a)
                i = i + 1
            }

            print(Hex.Encode(million.Finish()))

            """;
        var message = Message200();
        var expected = Enumerable.Range(0, 201).Select(n => Convert.ToHexStringLower(SHA256.HashData(message[..n])))
            .Append("0")
            .Append("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
            .Append("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
            .Append("248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")
            .Append("cf5b16a778af8380036ce59e7b0492370b249b11e8f07a51afac45037afee9d1")
            .Append("cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0")
            .ToList();
        Assert.True(System.Runtime.Intrinsics.X86.Sse41.IsSupported && (System.Runtime.Intrinsics.X86.X86Base.CpuId(7, 0).Ebx & (1 << 29)) != 0, "this machine lacks SSE4.1 or SHA, so the path through the SHA instructions is not run here");
        var plain = ListTests.Edited(("std/std.kz", "X86.Sha.IsSupported && X86.Sse41.IsSupported", "false"));
        var named = toolchain.Clang.DescribeCpu("native", x86: true, out _)!;
        Assert.Contains("X86.Sha", named.Classes);
        foreach (var (runtime, cpu, name, word) in new (CheckedRuntime, Cpu?, string, bool)[]
        {
            (toolchain.Runtime, null, "hash-sha-tested", true),
            (toolchain.Runtime, named, "hash-sha-named", false),
            (plain, null, "hash-sha-plain", false),
        })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true, cpu: cpu is null ? null : () => cpu);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var ir = compilation.Program.Ir;
            Assert.Equal((name, word), (name, ir.Contains("load i64, ptr @kz.cpu", StringComparison.Ordinal))); // which path runs is held by the mutations of each
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name));
            Assert.Equal((name, 0, ""), (name, code, error));
            Assert.Equal(expected, output);
        }
    }

    /// <summary>The bytes of the message of <see cref="Sha256_gives_the_same_digests_through_the_sha_instructions_and_through_the_plain_rounds"/>.</summary>
    private static byte[] Message200() => Enumerable.Range(0, 200).Select(i => (byte)((i * 31 + 7) % 256)).ToArray();

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
