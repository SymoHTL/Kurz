namespace Kurz.Compiler.Syntax;

/// <summary>The words a name cannot be: the core words of L18 and the names of the built-in types of L19.</summary>
public static class CoreWords
{
    /// <summary>L18, the closed list of the sequential core.</summary>
    public static readonly IReadOnlySet<string> Syntax = new HashSet<string>(StringComparer.Ordinal)
    {
        "if", "else", "match", "for", "in", "while", "break", "continue", "return", "throw",
        "mut", "data", "class", "interface", "enum", "flags", "with", "equal", "by",
        "pub", "prot", "static", "this", "virtual", "override", "weak", "raw", "use", "void",
        "true", "false", "null",
    };

    /// <summary>L19, the built-in types, in the order of T2, T13 and T24.</summary>
    public static readonly IReadOnlySet<string> TypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "bool", "string", "char",
        "duration", "timestamp", "longduration", "longtimestamp",
    };

    /// <summary>L14, the units a number can carry, with their meaning as nanoseconds or bytes.</summary>
    public static readonly IReadOnlyDictionary<string, long> Units = new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["ms"] = 1_000_000L,
        ["s"] = 1_000_000_000L,
        ["min"] = 60_000_000_000L,
        ["h"] = 3_600_000_000_000L,
        ["days"] = 86_400_000_000_000L,
        ["kb"] = 1024L,
        ["mb"] = 1024L * 1024L,
        ["gb"] = 1024L * 1024L * 1024L,
    };

    public static bool IsDurationUnit(string unit) => unit is "ms" or "s" or "min" or "h" or "days";

    public static bool IsReserved(string word) => Syntax.Contains(word) || TypeNames.Contains(word);
}
