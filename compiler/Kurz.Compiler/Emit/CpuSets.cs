namespace Kurz.Compiler.Emit;

/// <summary>A bit of a register that <c>cpuid</c> writes for a leaf and a subleaf: 0 is EAX, 1 EBX, 2 ECX and 3 EDX.</summary>
public sealed record CpuBit(uint Leaf, uint Subleaf, int Register, int Bit);

/// <summary>The registers whose state the system saves, read from XCR0 by <c>xgetbv</c>, that a set's instructions need.</summary>
public enum CpuState
{
    None,
    Avx,
    Avx512,
    Amx,
    Apx,
}

/// <summary>
/// An instruction set the start check reads (R18; section 2, the owner, 2026-11-02): the macro clang defines for a CPU that has it,
/// clang's name for it, which the check's line gives, the bits of <c>cpuid</c> that say a CPU has it, all of them, or the one in
/// <paramref name="Else"/>, the state the system has to save for it, and for AVX10 the version leaf 0x24 has to give at least.
/// </summary>
public sealed record CpuSet(string Macro, string Name, IReadOnlyList<CpuBit> Bits, CpuState State = CpuState.None, CpuBit? Else = null, int Version = 0);

/// <summary>
/// The sets the start check of a build for a named CPU reads: every set of the CPU beyond the target's baseline, each as LLVM's own
/// reading of the machine's CPU for <c>native</c> reads it (<c>sys::getHostCPUFeatures</c> in LLVM's <c>Host.cpp</c>, the release the
/// compiler pins), by the macro clang defines for it (section 2, the owner, 2026-11-02: "every set, as LLVM reads them"). A set this
/// table does not hold fails the build with a text that names it, so that no set goes unchecked.
/// </summary>
public static class CpuSets
{
    private const int Eax = 0;
    private const int Ebx = 1;
    private const int Ecx = 2;
    private const int Edx = 3;

    private static CpuSet Leaf1(string macro, string name, int register, int bit, CpuState state = CpuState.None) => new(macro, name, [new(1, 0, register, bit)], state);

    private static CpuSet Leaf7(string macro, string name, int register, int bit, CpuState state = CpuState.None) => new(macro, name, [new(7, 0, register, bit)], state);

    private static CpuSet Leaf71(string macro, string name, int register, int bit, CpuState state = CpuState.None) => new(macro, name, [new(7, 1, register, bit)], state);

    private static CpuSet Ext1(string macro, string name, int bit, CpuState state = CpuState.None) => new(macro, name, [new(0x80000001, 0, Ecx, bit)], state);

    private static readonly CpuBit Apx = new(7, 1, Edx, 21);

    /// <summary>Every set the table holds, in the order LLVM reads them, which the start check's line keeps.</summary>
    public static readonly IReadOnlyList<CpuSet> Table =
    [
        Leaf1("__SSE3__", "sse3", Ecx, 0),
        Leaf1("__PCLMUL__", "pclmul", Ecx, 1),
        Leaf1("__SSSE3__", "ssse3", Ecx, 9),
        Leaf1("__GCC_HAVE_SYNC_COMPARE_AND_SWAP_16", "cx16", Ecx, 13),
        Leaf1("__SSE4_1__", "sse4.1", Ecx, 19),
        Leaf1("__SSE4_2__", "sse4.2", Ecx, 20),
        Leaf1("__CRC32__", "crc32", Ecx, 20), // LLVM: crc32 is sse4.2's
        Leaf1("__MOVBE__", "movbe", Ecx, 22),
        Leaf1("__POPCNT__", "popcnt", Ecx, 23),
        Leaf1("__AES__", "aes", Ecx, 25),
        Leaf1("__RDRND__", "rdrnd", Ecx, 30),
        Leaf1("__AVX__", "avx", Ecx, 28, CpuState.Avx),
        Leaf1("__FMA__", "fma", Ecx, 12, CpuState.Avx),
        Leaf1("__XSAVE__", "xsave", Ecx, 26, CpuState.Avx),
        Leaf1("__F16C__", "f16c", Ecx, 29, CpuState.Avx),
        Ext1("__LAHF_SAHF__", "sahf", 0),
        Ext1("__LZCNT__", "lzcnt", 5),
        Ext1("__SSE4A__", "sse4a", 6),
        Ext1("__PRFCHW__", "prfchw", 8),
        Ext1("__XOP__", "xop", 11, CpuState.Avx),
        Ext1("__LWP__", "lwp", 15),
        Ext1("__FMA4__", "fma4", 16, CpuState.Avx),
        Ext1("__TBM__", "tbm", 21),
        Ext1("__MWAITX__", "mwaitx", 29),
        new("__CLZERO__", "clzero", [new(0x80000008, 0, Ebx, 0)]),
        new("__RDPRU__", "rdpru", [new(0x80000008, 0, Ebx, 4)]),
        new("__WBNOINVD__", "wbnoinvd", [new(0x80000008, 0, Ebx, 9)]),
        new("__PREFETCHI__", "prefetchi", [new(0x80000021, 0, Eax, 20)], Else: new(7, 1, Edx, 14)), // AMD's bit, or Intel's
        new("__AVX512BMM__", "avx512bmm", [new(0x80000021, 0, Eax, 23)], CpuState.Avx512),
        Leaf7("__FSGSBASE__", "fsgsbase", Ebx, 0),
        Leaf7("__SGX__", "sgx", Ebx, 2),
        Leaf7("__BMI__", "bmi", Ebx, 3),
        Leaf7("__AVX2__", "avx2", Ebx, 5, CpuState.Avx),
        Leaf7("__BMI2__", "bmi2", Ebx, 8),
        Leaf7("__INVPCID__", "invpcid", Ebx, 10),
        Leaf7("__RTM__", "rtm", Ebx, 11),
        Leaf7("__AVX512F__", "avx512f", Ebx, 16, CpuState.Avx512),
        Leaf7("__AVX512DQ__", "avx512dq", Ebx, 17, CpuState.Avx512),
        Leaf7("__RDSEED__", "rdseed", Ebx, 18),
        Leaf7("__ADX__", "adx", Ebx, 19),
        Leaf7("__AVX512IFMA__", "avx512ifma", Ebx, 21, CpuState.Avx512),
        Leaf7("__CLFLUSHOPT__", "clflushopt", Ebx, 23),
        Leaf7("__CLWB__", "clwb", Ebx, 24),
        Leaf7("__AVX512CD__", "avx512cd", Ebx, 28, CpuState.Avx512),
        Leaf7("__SHA__", "sha", Ebx, 29),
        Leaf7("__AVX512BW__", "avx512bw", Ebx, 30, CpuState.Avx512),
        Leaf7("__AVX512VL__", "avx512vl", Ebx, 31, CpuState.Avx512),
        Leaf7("__AVX512VBMI__", "avx512vbmi", Ecx, 1, CpuState.Avx512),
        Leaf7("__PKU__", "pku", Ecx, 4),
        Leaf7("__WAITPKG__", "waitpkg", Ecx, 5),
        Leaf7("__AVX512VBMI2__", "avx512vbmi2", Ecx, 6, CpuState.Avx512),
        Leaf7("__SHSTK__", "shstk", Ecx, 7),
        Leaf7("__GFNI__", "gfni", Ecx, 8),
        Leaf7("__VAES__", "vaes", Ecx, 9, CpuState.Avx),
        Leaf7("__VPCLMULQDQ__", "vpclmulqdq", Ecx, 10, CpuState.Avx),
        Leaf7("__AVX512VNNI__", "avx512vnni", Ecx, 11, CpuState.Avx512),
        Leaf7("__AVX512BITALG__", "avx512bitalg", Ecx, 12, CpuState.Avx512),
        Leaf7("__AVX512VPOPCNTDQ__", "avx512vpopcntdq", Ecx, 14, CpuState.Avx512),
        Leaf7("__RDPID__", "rdpid", Ecx, 22),
        Leaf7("__KL__", "kl", Ecx, 23),
        Leaf7("__CLDEMOTE__", "cldemote", Ecx, 25),
        Leaf7("__MOVDIRI__", "movdiri", Ecx, 27),
        Leaf7("__MOVDIR64B__", "movdir64b", Ecx, 28),
        Leaf7("__ENQCMD__", "enqcmd", Ecx, 29),
        Leaf7("__UINTR__", "uintr", Edx, 5),
        Leaf7("__AVX512VP2INTERSECT__", "avx512vp2intersect", Edx, 8, CpuState.Avx512),
        Leaf7("__SERIALIZE__", "serialize", Edx, 14),
        Leaf7("__TSXLDTRK__", "tsxldtrk", Edx, 16),
        Leaf7("__PCONFIG__", "pconfig", Edx, 18),
        Leaf7("__AMX_BF16__", "amx-bf16", Edx, 22, CpuState.Amx),
        Leaf7("__AVX512FP16__", "avx512fp16", Edx, 23, CpuState.Avx512),
        Leaf7("__AMX_TILE__", "amx-tile", Edx, 24, CpuState.Amx),
        Leaf7("__AMX_INT8__", "amx-int8", Edx, 25, CpuState.Amx),
        Leaf71("__SHA512__", "sha512", Eax, 0),
        Leaf71("__SM3__", "sm3", Eax, 1),
        Leaf71("__SM4__", "sm4", Eax, 2),
        Leaf71("__RAOINT__", "raoint", Eax, 3),
        Leaf71("__AVXVNNI__", "avxvnni", Eax, 4, CpuState.Avx),
        Leaf71("__AVX512BF16__", "avx512bf16", Eax, 5, CpuState.Avx512),
        Leaf71("__AMX_FP16__", "amx-fp16", Eax, 21, CpuState.Amx),
        Leaf71("__CMPCCXADD__", "cmpccxadd", Eax, 7),
        Leaf71("__HRESET__", "hreset", Eax, 22),
        Leaf71("__AVXIFMA__", "avxifma", Eax, 23, CpuState.Avx),
        Leaf71("__MOVRS__", "movrs", Eax, 31),
        Leaf71("__AVXVNNIINT8__", "avxvnniint8", Edx, 4, CpuState.Avx),
        Leaf71("__AVXNECONVERT__", "avxneconvert", Edx, 5, CpuState.Avx),
        Leaf71("__AMX_COMPLEX__", "amx-complex", Edx, 8, CpuState.Amx),
        Leaf71("__AVXVNNIINT16__", "avxvnniint16", Edx, 10, CpuState.Avx),
        Leaf71("__USERMSR__", "usermsr", Edx, 15),
        new("__EGPR__", "egpr", [Apx], CpuState.Apx),
        new("__PUSH2POP2__", "push2pop2", [Apx], CpuState.Apx),
        new("__PPX__", "ppx", [Apx], CpuState.Apx),
        new("__NDD__", "ndd", [Apx], CpuState.Apx),
        new("__CCMP__", "ccmp", [Apx], CpuState.Apx),
        new("__NF__", "nf", [Apx], CpuState.Apx),
        new("__CF__", "cf", [Apx], CpuState.Apx),
        new("__ZU__", "zu", [Apx], CpuState.Apx),
        new("__JMPABS__", "jmpabs", [Apx], CpuState.Apx),
        new("__APX_F__", "apxf", [Apx], CpuState.Apx), // clang's macro for all of APX's sets
        new("__XSAVEOPT__", "xsaveopt", [new(0xd, 1, Eax, 0)], CpuState.Avx),
        new("__XSAVEC__", "xsavec", [new(0xd, 1, Eax, 1)], CpuState.Avx),
        new("__XSAVES__", "xsaves", [new(0xd, 1, Eax, 3)], CpuState.Avx),
        new("__PTWRITE__", "ptwrite", [new(0x14, 0, Ebx, 4)]),
        new("__WIDEKL__", "widekl", [new(0x19, 0, Ebx, 2)]),
        new("__AMX_FP8__", "amx-fp8", [new(0x1e, 1, Eax, 4)], CpuState.Amx),
        new("__AMX_AVX512__", "amx-avx512", [new(0x1e, 1, Eax, 7)], CpuState.Amx),
        new("__AMX_MOVRS__", "amx-movrs", [new(0x1e, 1, Eax, 8)], CpuState.Amx),
        new("__AVX10_1__", "avx10.1", [new(7, 1, Edx, 19)], Version: 1),
        new("__AVX10_1_512__", "avx10.1", [new(7, 1, Edx, 19)], Version: 1), // clang defines it with __AVX10_1__
        new("__AVX10_2__", "avx10.2", [new(7, 1, Edx, 19)], Version: 2),
        new("__AVX10_2_512__", "avx10.2", [new(7, 1, Edx, 19)], Version: 2), // clang defines it with __AVX10_2__
    ];

    /// <summary>Every set the table holds, by its macro.</summary>
    public static readonly IReadOnlyDictionary<string, CpuSet> All = Table.ToDictionary(s => s.Macro, StringComparer.Ordinal);

    /// <summary>
    /// Whether a macro clang defines for a CPU names an instruction set: x86's form, <c>__AVX2__</c>, and the one macro of CMPXCHG16B,
    /// <c>__GCC_HAVE_SYNC_COMPARE_AND_SWAP_16</c>; a CPU's own name, <c>__znver4__</c>, is written in small letters.
    /// </summary>
    public static bool NamesASet(string macro) =>
        macro == "__GCC_HAVE_SYNC_COMPARE_AND_SWAP_16"
        || (macro.Length > 4 && macro.StartsWith("__", StringComparison.Ordinal) && macro.EndsWith("__", StringComparison.Ordinal)
            && macro.All(c => c is '_' or (>= 'A' and <= 'Z') or (>= '0' and <= '9')));
}
