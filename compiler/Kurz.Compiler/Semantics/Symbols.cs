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

    /// <summary>The name an arm of a <c>match</c> binds (C4): immutable, and read as a loop variable is.</summary>
    Binding,
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

/// <summary>A type parameter (T19) with its limit, where one is written.</summary>
public sealed class TypeParameterSymbol(string name, int line)
{
    public string Name { get; } = name;

    public int Line { get; } = line;

    public KurzType? Limit { get; set; }
}

/// <summary>A parameter of a function (F1), a lambda (F7) or a primary constructor (D1): the type, the <c>mut</c> marker (M7) and whether a default is written (D11).</summary>
public sealed record ParameterSymbol(string Name, KurzType Type, bool Mut, bool HasDefault, int Line);

/// <summary>A function declared in the file (F1 to F5), or a built-in one.</summary>
public sealed class FunctionSymbol(string name, FunctionDeclaration? declaration)
{
    public string Name { get; } = name;

    public FunctionDeclaration? Declaration { get; } = declaration;

    public List<TypeParameterSymbol> TypeParameters { get; } = [];

    public List<ParameterSymbol> Parameters { get; } = [];

    public KurzType ReturnType { get; set; } = Types.Error;

    public int Line => Declaration?.Line ?? 0;

    public bool IsGeneric => TypeParameters.Count > 0;
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

    /// <summary>Whether the declaration writes a primary constructor at all: <c>data Empty</c> has none and one value (D8).</summary>
    public bool HasFields => Declaration.PrimaryConstructor is not null;

    public int Line => Declaration.Line;

    public bool IsGeneric => TypeParameters.Count > 0;

    public FieldSymbol? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
}
