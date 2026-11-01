using System.Numerics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public enum VariableKind
{
    /// <summary>Declared by V1 to V4, or by V8's first assignment; <c>unused-variable</c> when never read (V5).</summary>
    Local,

    /// <summary>A parameter of a function or a lambda: immutable unless <c>mut</c> (F8, M7), never <c>unused-variable</c> (V13).</summary>
    Parameter,

    /// <summary>The variable of a <c>for ... in</c> loop (C6): immutable, never <c>unused-variable</c> (V13).</summary>
    Loop,

    /// <summary>The name an arm of a <c>match</c> binds (C4): immutable, and never <c>unused-variable</c>, as a loop variable is not (V13's reading in <c>compiler/README.md</c>: the arm declares it, not the program's choice).</summary>
    Binding,

    /// <summary>State outside a function (R13), <c>static mut</c> or <c>thread static mut</c> at the top level of a file: visible in every function, read and written inside a <c>raw</c> block alone, and never <c>unused-variable</c>, being state of its package and no variable.</summary>
    Static,
}

/// <summary>A variable: its type, whether it is <c>mut</c>, and whether the program has read or changed it (V5, V6, V12).</summary>
public sealed class VariableSymbol(string name, KurzType type, bool mut, VariableKind kind, int line)
{
    public string Name { get; } = name;

    public KurzType Type { get; } = type;

    public bool Mut { get; } = mut;

    public VariableKind Kind { get; } = kind;

    public int Line { get; } = line;

    public bool Read { get; set; }

    public bool Changed { get; set; }
}

/// <summary>A type parameter (T19). A limit written on it is <c>not supported</c> and is not read; whether one is written is kept, since only a limit gives a type parameter members.</summary>
public sealed class TypeParameterSymbol(string name, int line)
{
    public string Name { get; } = name;

    public int Line { get; } = line;

    public bool HasLimit { get; init; }
}

/// <summary>A parameter of a function (F1), a lambda (F7) or a primary constructor (D1): the type, the <c>mut</c> marker (M7) and whether a default is written (D11).</summary>
public sealed record ParameterSymbol(string Name, KurzType Type, bool Mut, bool HasDefault, int Line);

/// <summary>A function declared in the file (F1 to F5), a declaration of a block of C functions (R16), or a built-in one.</summary>
public sealed class FunctionSymbol(string name, FunctionDeclaration? declaration)
{
    public string Name { get; } = name;

    public FunctionDeclaration? Declaration { get; } = declaration;

    /// <summary>The declaration in a block of C functions (R16) that this symbol is, with no body.</summary>
    public ExternFunction? CDeclaration { get; init; }

    /// <summary>Whether the function takes the C calling convention: a declaration of a block of C functions (R16), a function of the package's own written <c>extern "C"</c> (R16), or a <c>naked</c> one (R12).</summary>
    public bool TakesC => CDeclaration is not null || (Declaration is not null && (Declaration.Modifiers & (Modifiers.Extern | Modifiers.Naked)) != 0);

    public List<TypeParameterSymbol> TypeParameters { get; } = [];

    public List<ParameterSymbol> Parameters { get; } = [];

    public KurzType ReturnType { get; set; } = Types.Error;

    public int Line => Declaration?.Line ?? CDeclaration?.Line ?? 0;

    public bool IsGeneric => TypeParameters.Count > 0;

    /// <summary>The class whose method this is (K7), with its instance as <c>this</c>; null for a function of the file.</summary>
    public ClassSymbol? Owner { get; init; }
}

/// <summary>A field of a <c>data</c> type (D1), its own or inherited (D7).</summary>
public sealed record FieldSymbol(string Name, KurzType Type, bool HasDefault, bool Mut, int Line);

/// <summary>A <c>data</c> type (D1) with its fields, the base fields first (D7), its base (D6) and its type parameters (T19).</summary>
public sealed class DataSymbol(string name, TypeDeclaration declaration)
{
    public string Name { get; } = name;

    public TypeDeclaration Declaration { get; } = declaration;

    public List<TypeParameterSymbol> TypeParameters { get; } = [];

    /// <summary>The base's fields first, then the type's own (D7).</summary>
    public List<FieldSymbol> Fields { get; } = [];

    public DataSymbol? Base { get; set; }

    /// <summary>Set when the checker starts resolving the type, whatever the outcome, so that it is resolved once and a cycle of bases ends.</summary>
    public bool Resolved { get; set; }

    /// <summary>Whether the declaration writes a primary constructor: <c>data Empty</c> has none and one value (D8); an empty <c>()</c> is <c>syntax</c> and counts as written.</summary>
    public bool HasFields => Declaration.PrimaryConstructor is not null;

    public int Line => Declaration.Line;

    public bool IsGeneric => TypeParameters.Count > 0;

    public FieldSymbol? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

/// <summary>
/// A <c>class</c> (K1): the fields of its primary constructor in their order, <c>mut</c> where written (K1), and its methods by name
/// (K7), each a function whose instance is <c>this</c>.
/// </summary>
public sealed class ClassSymbol(string name, TypeDeclaration declaration)
{
    public string Name { get; } = name;

    public TypeDeclaration Declaration { get; } = declaration;

    public List<FieldSymbol> Fields { get; } = [];

    public Dictionary<string, List<FunctionSymbol>> Methods { get; } = new(StringComparer.Ordinal);

    public int Line => Declaration.Line;

    public FieldSymbol? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);

    /// <summary>A5: the <c>Text()</c> that gives an instance its text, a method of no parameters with a <c>string</c> result; null where the class has none.</summary>
    public FunctionSymbol? TextMethod =>
        Methods.TryGetValue("Text", out var list) ? list.FirstOrDefault(f => f.Parameters.Count == 0 && f.TypeParameters.Count == 0 && f.ReturnType is PrimitiveType { Kind: Primitive.String }) : null;
}

/// <summary>
/// An <c>enum</c> (D12) with its values in the order written, each name once, and their numbers where the declaration writes them
/// (D18); a declaration that breaks D20's shape keeps its values and has its uses of the numbers read without a second error (E1).
/// </summary>
public sealed class EnumSymbol(string name, TypeDeclaration declaration)
{
    public string Name { get; } = name;

    public TypeDeclaration Declaration { get; } = declaration;

    public List<string> Values { get; } = [];

    /// <summary>The number of each value, in the order of <see cref="Values"/>; empty where the declaration writes none.</summary>
    public List<BigInteger> Numbers { get; } = [];

    /// <summary>Set where the declaration breaks D20's shape, which was reported at the declaration.</summary>
    public bool Refused { get; set; }

    public bool Numbered => Numbers.Count > 0;

    public int Line => Declaration.Line;

    public int IndexOf(string value) => Values.IndexOf(value);
}
