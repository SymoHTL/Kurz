namespace Kurz.Compiler.Semantics;

/// <summary>
/// A class of intrinsics (R18): an instruction set under <c>X86</c> or <c>Arm</c>, with C#'s name, <c>X86.Sse41</c>, the class C#
/// derives it from, whose intrinsics a test of it guards too (section 13, the owner, 2026-11-01, "C#'s shape"), the LLVM feature that
/// a function compiled for it carries, the macro clang defines for a CPU that has it, and its bits in the CPU's word
/// (<see cref="Intrinsics.CpuWord"/>). A class that is not <paramref name="Built"/> is one of C#'s whose name the compiler knows and
/// whose intrinsics it does not build.
/// </summary>
public sealed record IntrinsicClass(string Name, string? Base, string? Feature, string? Macro, ulong Bit, bool Built)
{
    /// <summary>Whether the class stands under <c>X86</c>.</summary>
    public bool IsX86 => Name.StartsWith("X86.", StringComparison.Ordinal);
}

/// <summary>A parameter of an intrinsic, with C#'s name; an <paramref name="Immediate"/> is a constant the instruction holds, C#'s <c>[ConstantExpected]</c>.</summary>
public sealed record IntrinsicParameter(string Name, KurzType Type, bool Immediate = false);

/// <summary>
/// An intrinsic, or a lane operation of a vector (R18): the class or the vector type it is a member of, C#'s name and signature, what
/// the emitter turns it into (<paramref name="Op"/>), and the vendor's intrinsic it is, which its documentation names (section 13,
/// the owner, 2026-11-01, "C#'s shape"): Intel's <c>_mm_extract_epi32</c> for <c>X86.Sse41.Extract</c>. A lane operation has no class,
/// and needs no test (R18).
/// </summary>
public sealed record Intrinsic(string Owner, string Name, IReadOnlyList<IntrinsicParameter> Parameters, KurzType Result, string Op, string Vendor)
{
    /// <summary>The class of an intrinsic, null for a lane operation.</summary>
    public IntrinsicClass? Class => Intrinsics.ClassOf(Owner);

    /// <summary>The form an error names: <c>X86.Sse41.Extract(Vector128&lt;int&gt; value, byte index)</c>.</summary>
    public string Form => $"{Owner}.{Name}({string.Join(", ", Parameters.Select(p => $"{p.Type} {p.Name}"))})";
}

/// <summary>
/// The intrinsics the compiler builds (R18), the one table the checker types them by and the emitter emits them from: the lane
/// operations of <c>Vector128&lt;T&gt;</c> and <c>Vector256&lt;T&gt;</c>, and the classes under <c>X86</c> and <c>Arm</c>, each function
/// with C#'s name and signature where C# has the class, and <c>X86.Sha</c> in C#'s pattern, as the closed .NET proposal of the class
/// wrote it (dotnet/runtime pull request 62999), on the lanes of Intel's <c>epu32</c>.
/// </summary>
public static class Intrinsics
{
    /// <summary>The ten lane types of C#, T2's numbers but <c>decimal</c>.</summary>
    public static readonly IReadOnlyList<PrimitiveType> Lanes =
        [Types.Byte, Types.SByte, Types.Short, Types.UShort, Types.Int, Types.UInt, Types.Long, Types.ULong, Types.Float, Types.Double];

    private static readonly PrimitiveType[] Integers = [Types.Byte, Types.SByte, Types.Short, Types.UShort, Types.Int, Types.UInt, Types.Long, Types.ULong];

    // The CPU's word: ECX of cpuid's leaf 1 in its low 32 bits, EBX of leaf 7, subleaf 0, in its high 32, as std_detect reads them
    private const ulong Sse3Bit = 1UL << 0;
    private const ulong Ssse3Bit = 1UL << 9;
    private const ulong Sse41Bit = 1UL << 19;
    private const ulong Sse42Bit = 1UL << 20;
    private const ulong AesBit = 1UL << 25;
    private const ulong PclmulBit = 1UL << 1;
    private const ulong ShaBit = 1UL << (32 + 29);

    /// <summary>
    /// C#'s classes of <c>System.Runtime.Intrinsics.X86</c> and <c>.Arm</c> by their names under <c>X86</c> and <c>Arm</c>, and
    /// <c>X86.Sha</c>: those built, and those whose name the compiler knows and says <c>not supported</c> to. <c>X86.Sse2</c> is in
    /// every x86-64 CPU, the target's baseline, and has no bit; a class under <c>Arm</c> has none, since its test is <c>false</c> on
    /// x86-64 (R18).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IntrinsicClass> Classes = new IntrinsicClass[]
    {
        new("X86.X86Base", null, null, null, 0, false),
        new("X86.Sse", "X86.X86Base", "sse", "__SSE__", 0, false),
        new("X86.Sse2", "X86.Sse", "sse2", "__SSE2__", 0, true),
        new("X86.Sse3", "X86.Sse2", "sse3", "__SSE3__", Sse3Bit, false),
        new("X86.Ssse3", "X86.Sse3", "ssse3", "__SSSE3__", Ssse3Bit, true),
        new("X86.Sse41", "X86.Ssse3", "sse4.1", "__SSE4_1__", Sse41Bit, true),
        new("X86.Sse42", "X86.Sse41", "sse4.2", "__SSE4_2__", Sse42Bit, false),
        new("X86.Avx", "X86.Sse42", "avx", "__AVX__", 0, false),
        new("X86.Avx2", "X86.Avx", "avx2", "__AVX2__", 0, false),
        new("X86.Avx512F", "X86.Avx2", "avx512f", "__AVX512F__", 0, false),
        new("X86.Avx512BW", "X86.Avx512F", "avx512bw", "__AVX512BW__", 0, false),
        new("X86.Avx512CD", "X86.Avx512F", "avx512cd", "__AVX512CD__", 0, false),
        new("X86.Avx512DQ", "X86.Avx512F", "avx512dq", "__AVX512DQ__", 0, false),
        new("X86.Avx512Vbmi", "X86.Avx512BW", "avx512vbmi", "__AVX512VBMI__", 0, false),
        new("X86.AvxVnni", "X86.Avx2", "avxvnni", "__AVXVNNI__", 0, false),
        new("X86.Fma", "X86.Avx", "fma", "__FMA__", 0, false),
        new("X86.Aes", "X86.Sse2", "aes", "__AES__", AesBit, true),
        new("X86.Pclmulqdq", "X86.Sse2", "pclmul", "__PCLMUL__", PclmulBit, false),
        new("X86.Sha", "X86.Sse2", "sha", "__SHA__", ShaBit, true),
        new("X86.Gfni", "X86.Sse41", "gfni", "__GFNI__", 0, false),
        new("X86.Popcnt", "X86.Sse42", "popcnt", "__POPCNT__", 0, false),
        new("X86.Lzcnt", "X86.X86Base", "lzcnt", "__LZCNT__", 0, false),
        new("X86.Bmi1", "X86.X86Base", "bmi", "__BMI__", 0, false),
        new("X86.Bmi2", "X86.X86Base", "bmi2", "__BMI2__", 0, false),
        new("X86.X86Serialize", "X86.X86Base", "serialize", "__SERIALIZE__", 0, false),
        new("Arm.ArmBase", null, null, null, 0, true),
        new("Arm.AdvSimd", "Arm.ArmBase", null, null, 0, true),
        new("Arm.Aes", "Arm.ArmBase", null, null, 0, true),
        new("Arm.Crc32", "Arm.ArmBase", null, null, 0, true),
        new("Arm.Dp", "Arm.AdvSimd", null, null, 0, true),
        new("Arm.Rdm", "Arm.AdvSimd", null, null, 0, true),
        new("Arm.Sha1", "Arm.ArmBase", null, null, 0, true),
        new("Arm.Sha256", "Arm.ArmBase", null, null, 0, true),
        new("Arm.Sve", "Arm.AdvSimd", null, null, 0, true),
        new("Arm.Sve2", "Arm.Sve", null, null, 0, true),
    }.ToDictionary(c => c.Name, StringComparer.Ordinal);

    /// <summary>The class of the name, null for a name that is none.</summary>
    public static IntrinsicClass? ClassOf(string name) => Classes.TryGetValue(name, out var c) ? c : null;

    /// <summary>The class and every class it derives from, as C# derives them: what a test of it guards (R18).</summary>
    public static IEnumerable<IntrinsicClass> Included(IntrinsicClass c)
    {
        for (IntrinsicClass? x = c; x is not null; x = x.Base is null ? null : Classes[x.Base])
        {
            yield return x;
        }
    }

    /// <summary>Whether a test of <paramref name="tested"/> guards an intrinsic of <paramref name="needed"/>: it is that class or derives from it.</summary>
    public static bool Guards(IntrinsicClass tested, IntrinsicClass needed) => Included(tested).Any(c => c.Name == needed.Name);

    /// <summary>The bits of the CPU's word a test of the class reads: its own and those of every class it derives from.</summary>
    public static ulong CpuWord(IntrinsicClass c) => Included(c).Aggregate(0UL, (word, x) => word | x.Bit);

    /// <summary>The LLVM features of a function compiled for the classes: each class's own, which implies those it derives from.</summary>
    public static IEnumerable<string> Features(IEnumerable<IntrinsicClass> classes) =>
        classes.SelectMany(Included).Select(c => c.Feature).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static VectorType V128(KurzType lane) => new(lane, 128);

    private static IntrinsicParameter P(string name, KurzType type, bool immediate = false) => new(name, type, immediate);

    private static string Suffix(KurzType lane) => lane switch
    {
        PrimitiveType { Kind: Primitive.Byte or Primitive.SByte } => "epi8",
        PrimitiveType { Kind: Primitive.Short or Primitive.UShort } => "epi16",
        PrimitiveType { Kind: Primitive.Int or Primitive.UInt } => "epi32",
        PrimitiveType { Kind: Primitive.Long or Primitive.ULong } => "epi64",
        PrimitiveType { Kind: Primitive.Float } => "ps",
        _ => "pd",
    };

    /// <summary>The functions of a class or a vector type, by its name and the function's; overloads, as C# has them, in one list.</summary>
    public static readonly IReadOnlyDictionary<(string Owner, string Name), IReadOnlyList<Intrinsic>> Functions = Build();

    /// <summary>The lane operations of a vector of this type that its value is called on: <c>v.GetElement(2)</c>, <c>v.AsByte()</c>.</summary>
    public static IReadOnlyList<Intrinsic> Methods(VectorType vector, string name)
    {
        var owner = $"Vector{vector.Bits}<{vector.Lane}>";
        return Functions.TryGetValue((owner, name), out var list) ? list : [];
    }

    private static Dictionary<(string Owner, string Name), IReadOnlyList<Intrinsic>> Build()
    {
        var all = new List<Intrinsic>();
        foreach (var bits in (int[])[128, 256])
        {
            var owner = $"Vector{bits}";
            foreach (var lane in Lanes)
            {
                var vector = new VectorType(lane, bits);
                var count = vector.Count;
                all.Add(new(owner, "Create", [.. Enumerable.Range(0, count).Select(i => P($"e{i}", lane))], vector, "create", $"_mm{(bits == 256 ? "256" : "")}_setr_{Suffix(lane)}"));
                all.Add(new(owner, "Create", [P("value", lane)], vector, "broadcast", $"_mm{(bits == 256 ? "256" : "")}_set1_{Suffix(lane)}"));
                var self = vector.Text;
                all.Add(new(self, "GetElement", [P("index", Types.Int)], lane, "get", "(an element of the vector)"));
                foreach (var (name, to) in new (string, KurzType)[]
                {
                    ("AsByte", Types.Byte), ("AsSByte", Types.SByte), ("AsInt16", Types.Short), ("AsUInt16", Types.UShort), ("AsInt32", Types.Int),
                    ("AsUInt32", Types.UInt), ("AsInt64", Types.Long), ("AsUInt64", Types.ULong), ("AsSingle", Types.Float), ("AsDouble", Types.Double),
                })
                {
                    all.Add(new(self, name, [], new VectorType(to, bits), "as", "(the same bits, other lanes)"));
                }
            }
        }

        foreach (var lane in Lanes.Where(l => l is not PrimitiveType { Kind: Primitive.Float }))
        {
            all.Add(new("X86.Sse2", "Add", [P("left", V128(lane)), P("right", V128(lane))], V128(lane), "add", $"_mm_add_{Suffix(lane)}"));
        }

        foreach (var lane in Lanes.Where(l => l is not PrimitiveType { Kind: Primitive.Float }))
        {
            var vendor = lane is PrimitiveType { Kind: Primitive.Double } ? "_mm_loadu_pd" : "_mm_loadu_si128";
            all.Add(new("X86.Sse2", "LoadVector128", [P("address", new PointerType(lane))], V128(lane), "load", vendor));
            all.Add(new("X86.Sse2", "Store", [P("address", new PointerType(lane)), P("source", V128(lane))], Types.Void, "store", vendor.Replace("load", "store", StringComparison.Ordinal)));
        }

        foreach (var lane in new[] { Types.Int, Types.UInt })
        {
            all.Add(new("X86.Sse2", "Shuffle", [P("value", V128(lane)), P("control", Types.Byte, immediate: true)], V128(lane), "pshufd", "_mm_shuffle_epi32"));
        }

        foreach (var lane in new[] { Types.Byte, Types.SByte })
        {
            all.Add(new("X86.Ssse3", "Shuffle", [P("value", V128(lane)), P("mask", V128(lane))], V128(lane), "pshufb", "_mm_shuffle_epi8"));
        }

        foreach (var lane in Integers)
        {
            all.Add(new("X86.Ssse3", "AlignRight", [P("left", V128(lane)), P("right", V128(lane)), P("mask", Types.Byte, immediate: true)], V128(lane), "palignr", "_mm_alignr_epi8"));
        }

        foreach (var lane in new[] { Types.Byte, Types.Int, Types.UInt, Types.Float })
        {
            var vendor = lane is PrimitiveType { Kind: Primitive.Float } ? "_mm_extract_ps" : lane is PrimitiveType { Kind: Primitive.Byte } ? "_mm_extract_epi8" : "_mm_extract_epi32";
            all.Add(new("X86.Sse41", "Extract", [P("value", V128(lane)), P("index", Types.Byte, immediate: true)], lane, "extract", vendor));
        }

        foreach (var lane in new[] { Types.Short, Types.UShort })
        {
            all.Add(new("X86.Sse41", "Blend", [P("left", V128(lane)), P("right", V128(lane)), P("control", Types.Byte, immediate: true)], V128(lane), "pblendw", "_mm_blend_epi16"));
        }

        var words = V128(Types.UInt);
        all.Add(new("X86.Sha", "Sha256TwoRounds", [P("state1", words), P("state2", words), P("message", words)], words, "sha256rnds2", "_mm_sha256rnds2_epu32"));
        all.Add(new("X86.Sha", "Sha256MessageSchedule1", [P("a", words), P("b", words)], words, "sha256msg1", "_mm_sha256msg1_epu32"));
        all.Add(new("X86.Sha", "Sha256MessageSchedule2", [P("a", words), P("b", words)], words, "sha256msg2", "_mm_sha256msg2_epu32"));
        all.Add(new("X86.Sha", "Sha1FourRounds", [P("state1", words), P("state2", words), P("func", Types.Byte, immediate: true)], words, "sha1rnds4", "_mm_sha1rnds4_epu32"));
        all.Add(new("X86.Sha", "Sha1NextE", [P("a", words), P("b", words)], words, "sha1nexte", "_mm_sha1nexte_epu32"));
        all.Add(new("X86.Sha", "Sha1MessageSchedule1", [P("a", words), P("b", words)], words, "sha1msg1", "_mm_sha1msg1_epu32"));
        all.Add(new("X86.Sha", "Sha1MessageSchedule2", [P("a", words), P("b", words)], words, "sha1msg2", "_mm_sha1msg2_epu32"));

        var bytes = V128(Types.Byte);
        all.Add(new("X86.Aes", "Encrypt", [P("value", bytes), P("roundKey", bytes)], bytes, "aesenc", "_mm_aesenc_si128"));
        all.Add(new("X86.Aes", "EncryptLast", [P("value", bytes), P("roundKey", bytes)], bytes, "aesenclast", "_mm_aesenclast_si128"));
        all.Add(new("X86.Aes", "Decrypt", [P("value", bytes), P("roundKey", bytes)], bytes, "aesdec", "_mm_aesdec_si128"));
        all.Add(new("X86.Aes", "DecryptLast", [P("value", bytes), P("roundKey", bytes)], bytes, "aesdeclast", "_mm_aesdeclast_si128"));
        all.Add(new("X86.Aes", "InverseMixColumns", [P("value", bytes)], bytes, "aesimc", "_mm_aesimc_si128"));
        all.Add(new("X86.Aes", "KeygenAssist", [P("value", bytes), P("control", Types.Byte, immediate: true)], bytes, "aeskeygenassist", "_mm_aeskeygenassist_si128"));

        foreach (var lane in Lanes)
        {
            var kind = lane is PrimitiveType { Kind: Primitive.Float or Primitive.Double } ? "f" : Types.IsSigned(lane) ? "s" : "u";
            all.Add(new("Arm.AdvSimd", "Add", [P("left", V128(lane)), P("right", V128(lane))], V128(lane), "add", $"vaddq_{kind}{Types.LaneBits(lane)}"));
        }

        return all.GroupBy(i => (i.Owner, i.Name)).ToDictionary(g => g.Key, g => (IReadOnlyList<Intrinsic>)g.ToList());
    }
}
