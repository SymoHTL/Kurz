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

    /// <summary>T35: the type of the constant the parameter holds, written <c>const JsonKeys K</c>; null for a type parameter, the error type where it is not read.</summary>
    public KurzType? Constant { get; set; }

    /// <summary>T35: the constant a call that leaves the parameter out gives it, <c>= Mode.Plain</c>; null where none is written or it is wrong.</summary>
    public ConstantArgument? Default { get; set; }

    /// <summary>T35: a default is written, read or not, so that a call may leave the parameter out; one that was not read binds the error type.</summary>
    public bool HasDefault { get; init; }
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

    /// <summary>
    /// K7: a method written <c>static</c>, which has no instance and no <c>this</c>, and is called by its class's name, <c>T.M(args)</c>,
    /// as C#'s static method and Rust's associated function, which takes no <c>self</c>.
    /// </summary>
    public bool IsStatic => Owner is not null && Declaration is not null && (Declaration.Modifiers & Modifiers.Static) != 0;

    /// <summary>K7: a read-only property, <c>pub int Count => names.Count</c>, a method without parameters that a read calls, <c>h.Count</c>.</summary>
    public bool IsProperty => Declaration is { Property: true };

    /// <summary>K6, K7: the interface whose method this is, a signature with no body; null for any other function.</summary>
    public InterfaceSymbol? Interface { get; init; }

    /// <summary>K7: the method of a base class this <c>override</c> overrides; null for any other function.</summary>
    public FunctionSymbol? Overridden { get; set; }

    /// <summary>K7: whether a call through a reference of a base class's type runs the override of the instance's class: a method marked <c>virtual</c> or <c>override</c>.</summary>
    public bool IsVirtual => Declaration is not null && (Declaration.Modifiers & (Modifiers.Virtual | Modifiers.Override)) != 0;

    /// <summary>
    /// D16: the <c>data</c> type whose body declares this method, set on both of its symbols: the one a call picks, whose
    /// <see cref="Declaration"/> is the method as written and whose parameters are the written ones, and the function it is checked
    /// and emitted as (<see cref="Real"/>), whose first parameter is the value the method was called on, <c>this</c>, <c>mut</c> in a
    /// <c>mut</c> method (M5), as Rust's <c>self</c> and <c>&amp;mut self</c>; null for any other function.
    /// </summary>
    public DataSymbol? DataOwner { get; set; }

    /// <summary>D16: of the symbol a call picks, the function it is checked and emitted as; null for any other function.</summary>
    public FunctionSymbol? Real { get; set; }

    /// <summary>
    /// D16: the field of a <c>data</c> body whose initializer this function computes, from the value being made, <c>this</c>, whose
    /// primary fields and the fields of the body above it are set (§4, the owner, 2026-11-12); null for any other function.
    /// </summary>
    public FieldSymbol? DataField { get; set; }
}

/// <summary>
/// A field of a <c>data</c> type (D1), its own or inherited (D7), or of a class (K1); <paramref name="Body"/> is the declaration of a
/// field of a class body (K7) or of a <c>data</c> body (D16), which is no parameter of a constructor, and null for a field of a primary
/// constructor.
/// </summary>
public sealed record FieldSymbol(string Name, KurzType Type, bool HasDefault, bool Mut, int Line, FieldDeclaration? Body = null)
{
    /// <summary>F9, D14: a field of a primary constructor is visible wherever its type is, one of a class body only with <c>pub</c>.</summary>
    public bool IsPub => Body is null || (Body.Modifiers & Modifiers.Pub) != 0;

    /// <summary>F9: a field of a class body marked <c>internal</c>, visible to the files of the folder that declares its class.</summary>
    public bool IsInternal => Body is not null && (Body.Modifiers & Modifiers.Internal) != 0;
}

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

    /// <summary>
    /// Whether the type has a field, of its own or of its base (D7, D21), or writes a primary constructor: <c>data Empty</c> has none
    /// and one value (D8), as has <c>data Leaf : Empty</c>; an empty <c>()</c> is <c>syntax</c> and counts as written. The fields are
    /// the resolved ones, so it is read after the type is resolved.
    /// </summary>
    public bool HasFields => Declaration.PrimaryConstructor is not null || Fields.Count > 0;

    public int Line => Declaration.Line;

    public bool IsGeneric => TypeParameters.Count > 0;

    public FieldSymbol? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    /// <summary>
    /// D2, D16: the fields a construction gives, those of the primary constructors, the base's first (D7); a field declared in the body
    /// follows them and is given by its initializer alone (§4, the owner, 2026-11-12).
    /// </summary>
    public List<FieldSymbol> ConstructorFields => Fields.Where(f => f.Body is null).ToList();

    /// <summary>D16: the function that computes each field of the body from the value being made, by the field's name (<see cref="FunctionSymbol.DataField"/>).</summary>
    public Dictionary<string, FunctionSymbol> Initializers { get; } = [];

    /// <summary>D16: the methods its body declares, by name, each the symbol a call picks (<see cref="FunctionSymbol.Real"/>).</summary>
    public Dictionary<string, List<FunctionSymbol>> Methods { get; } = [];

    /// <summary>A5, D16: the <c>Text()</c> its body declares, a method of no parameters with a <c>string</c> result, which gives a value its text in place of A3's; null where it declares none.</summary>
    public FunctionSymbol? TextMethod =>
        Methods.TryGetValue("Text", out var list) ? list.FirstOrDefault(f => f.Parameters.Count == 0 && f.TypeParameters.Count == 0 && f.ReturnType is PrimitiveType { Kind: Primitive.String } && (f.Declaration!.Modifiers & Modifiers.Mut) == 0) : null;
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

    /// <summary>K7, K18: the <c>static</c> fields of the class, in the order written.</summary>
    public List<StaticFieldSymbol> StaticFields { get; } = [];

    public int Line => Declaration.Line;

    /// <summary>K6, K10: the class this one inherits from, whose fields come first in <see cref="Fields"/>; null for a class without one.</summary>
    public ClassSymbol? Base { get; set; }

    /// <summary>K6: whether a class of the program names this one as its base, so that a value of its type can be an instance of another class.</summary>
    public bool Derived { get; set; }

    /// <summary>Set once the base and the fields are resolved, and while they are, so that a class named as a base of itself is met once.</summary>
    public bool Resolved { get; set; }

    /// <summary>K10, K11, K16: the parameters of the primary constructor in the order a construction binds them, each with the field it gives.</summary>
    public List<ConstructorSlot> Constructor { get; } = [];

    /// <summary>
    /// K10: the explicit form's arguments of the base, read as a construction of the base; K14: for a class with a base, no primary
    /// constructor and no constructor in its body, the construction of the base with no arguments, which its empty constructor runs, as
    /// C#'s implied <c>base()</c>, where the base has a constructor to run (a constructor of a body holds its own,
    /// <see cref="ConstructorSymbol.BaseCall"/>); null for the short form and for a class without a base.
    /// </summary>
    public CallExpression? BaseCall { get; set; }

    /// <summary>K14: the constructors declared in the body of the class, in their order.</summary>
    public List<ConstructorSymbol> Constructors { get; } = [];

    /// <summary>The class and its bases, the class first.</summary>
    public IEnumerable<ClassSymbol> Chain
    {
        get
        {
            for (var c = this; c is not null; c = c.Base)
            {
                yield return c;
            }
        }
    }

    /// <summary>K6: whether the class is <paramref name="other"/> or derives from it.</summary>
    public bool HasBaseOrIs(ClassSymbol other) => Chain.Any(c => ReferenceEquals(c, other));

    /// <summary>K6: the interfaces the class names after <c>:</c>, in their order.</summary>
    public List<InterfaceSymbol> Interfaces { get; } = [];

    /// <summary>K6: the interfaces the class implements: those it names and those its bases name, the base's first, each once.</summary>
    public IEnumerable<InterfaceSymbol> AllInterfaces => Chain.Reverse().SelectMany(c => c.Interfaces).Distinct();

    /// <summary>K6: whether the class implements the interface, naming it or through a base that does.</summary>
    public bool Implements(InterfaceSymbol face) => Chain.Any(c => c.Interfaces.Contains(face));

    public FieldSymbol? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);

    public StaticFieldSymbol? StaticField(string name) => StaticFields.FirstOrDefault(f => f.Name == name);

    public int FieldIndex(string name) => Fields.FindIndex(f => f.Name == name);

    /// <summary>K5, K8, K15: the fields an <c>equal by</c> clause names, each once, in its order; empty where the class has no clause.</summary>
    public List<FieldSymbol> EqualBy { get; } = [];

    /// <summary>K4: whether two instances compare by identity: a class without a clause that has a <c>mut</c> field or a field whose type
    /// holds a <c>weak</c> reference, <c>List&lt;weak Owner?&gt;</c> as well as <c>weak Owner?</c> (the owner, 2026-11-02, #110, comment 6580085338).</summary>
    public bool ComparesByIdentity => EqualBy.Count == 0 && Fields.Any(f => f.Mut || Types.ReachesWeak(f.Type));

    /// <summary>K4, K5: the fields <c>==</c> and the hash read: those the clause names, or every field of a class that compares by content.</summary>
    public IReadOnlyList<FieldSymbol> FieldsThatCount => EqualBy.Count > 0 ? EqualBy : ComparesByIdentity ? [] : Fields;

    /// <summary>A5, A9: the <c>Text()</c> that gives an instance its text, a method of the instance of no parameters with a <c>string</c> result, its own or the nearest base's; null where the chain has none.</summary>
    public FunctionSymbol? TextMethod =>
        Chain.Select(c => c.Methods.TryGetValue("Text", out var list) ? list.FirstOrDefault(f => !f.IsStatic && !f.IsProperty && f.Parameters.Count == 0 && f.TypeParameters.Count == 0 && f.ReturnType is PrimitiveType { Kind: Primitive.String }) : null)
            .FirstOrDefault(f => f is not null);
}

/// <summary>
/// K7, K18: a <c>static</c> field of a class, which exists once for the program and holds an immutable value: the constant its
/// initial value is, which the checker sets once it read that value, and which every read of the field gives.
/// </summary>
public sealed class StaticFieldSymbol(FieldDeclaration declaration, ClassSymbol owner)
{
    public string Name => Declaration.Name;

    public FieldDeclaration Declaration { get; } = declaration;

    public ClassSymbol Owner { get; } = owner;

    /// <summary>The written type, or the type of the initial value where none is written (V2).</summary>
    public KurzType Type { get; set; } = Types.Error;

    /// <summary>The constant the field holds; null until the checker read its initial value, and where it reads none.</summary>
    public Constant? Value { get; set; }

    /// <summary>Set where the field was refused, with its one diagnostic, so that its reads and assignments carry nothing more (E1).</summary>
    public bool Refused { get; set; }

    public int Line => Declaration.Line;

    /// <summary>F9: a field without <c>pub</c> is private to its class.</summary>
    public bool IsPub => (Declaration.Modifiers & Modifiers.Pub) != 0;

    /// <summary>F9: a field marked <c>internal</c>, visible to the files of the folder that declares its class.</summary>
    public bool IsInternal => (Declaration.Modifiers & Modifiers.Internal) != 0;
}

/// <summary>
/// K6, K7: an <c>interface</c>, the signatures of its methods in the order written, which a class that names it implements with
/// <c>pub</c> methods of the same names, parameter types and return types.
/// </summary>
public sealed class InterfaceSymbol(string name, TypeDeclaration declaration)
{
    public string Name { get; } = name;

    public TypeDeclaration Declaration { get; } = declaration;

    public int Line => Declaration.Line;

    /// <summary>The methods in the order written, each a <see cref="FunctionSymbol"/> with no body.</summary>
    public List<FunctionSymbol> Methods { get; } = [];

    /// <summary>A10: the <c>string Text()</c> the interface declares, which gives a value of it its text; null where it declares none.</summary>
    public FunctionSymbol? TextMethod => Methods.FirstOrDefault(f => f.Name == "Text" && f.Parameters.Count == 0 && f.ReturnType is PrimitiveType { Kind: Primitive.String });
}

/// <summary>K10, K11, K16: a parameter of a class's primary constructor: the field it gives, own or the base's, the class whose declaration writes it, and that parameter.</summary>
public sealed record ConstructorSlot(FieldSymbol Field, ClassSymbol Owner, Parameter Declaration);

/// <summary>
/// K14: a constructor declared in the body of a class, private to it without <c>pub</c> (F9): its parameters and the call its
/// <c>: this(...)</c> makes, of the primary constructor or of another constructor of the body, read as a construction of the class.
/// </summary>
public sealed class ConstructorSymbol(ConstructorDeclaration declaration, ClassSymbol owner)
{
    public ConstructorDeclaration Declaration { get; } = declaration;

    public ClassSymbol Owner { get; } = owner;

    public List<ParameterSymbol> Parameters { get; } = [];

    public int Line => Declaration.Line;

    public bool IsPub => (Declaration.Modifiers & Modifiers.Pub) != 0;

    /// <summary>The call of <c>: this(...)</c>; null for a constructor without one.</summary>
    public CallExpression? ThisCall { get; set; }

    /// <summary>
    /// The construction of the base this constructor runs before its body: <c>: base(...)</c>, or C#'s implied <c>base()</c> where it
    /// calls no other constructor and the base has one to run; null where it calls another with <c>: this(...)</c>, and in a class
    /// without a base.
    /// </summary>
    public CallExpression? BaseCall { get; set; }

    /// <summary>The parameters as the variables the arguments of <c>: this(...)</c> or <c>: base(...)</c> read, which no instance is visible to.</summary>
    public List<VariableSymbol> InitializerParameters { get; } = [];
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

/// <summary>
/// A <c>flags</c> type (D13) with its names in the order written, each once, and the bit of each, the number its declaration writes
/// or 1, 2, 4 and so on in their order (D19); a declaration that breaks D19's shape keeps its names and has its uses read without a
/// second error (E1).
/// </summary>
public sealed class FlagsSymbol(string name, TypeDeclaration declaration)
{
    public string Name { get; } = name;

    public TypeDeclaration Declaration { get; } = declaration;

    public List<string> Names { get; } = [];

    /// <summary>The bit of each name, in the order of <see cref="Names"/>; empty until the numbers are read, and where they were refused.</summary>
    public List<int> Bits { get; } = [];

    /// <summary>Set where the declaration breaks D19's shape, which was reported at the declaration.</summary>
    public bool Refused { get; set; }

    public int Line => Declaration.Line;

    public int IndexOf(string flag) => Names.IndexOf(flag);

    /// <summary>The set of every name, whose bits <c>~</c> keeps (D17).</summary>
    public int All => Bits.Aggregate(0, (all, bit) => all | bit);

    /// <summary>
    /// A13: the text of a set, the names it holds in the order of the declaration with <c>" | "</c> between them, or <c>None</c>. No set
    /// of a refused declaration reaches it: its names and <c>None</c> are the error type there (<c>Checker.FlagsValue</c>), so no
    /// constant of it is folded, and <see cref="Bits"/> holds a bit for each name wherever it is read.
    /// </summary>
    public string Text(int set) => set == 0 ? "None" : string.Join(" | ", Names.Where((_, i) => (set & Bits[i]) != 0));
}
