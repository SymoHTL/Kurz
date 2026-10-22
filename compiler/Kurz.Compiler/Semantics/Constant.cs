using System.Numerics;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// The value of a constant expression, which F12 names as what a default may be: a literal of any
/// kind (an integer, a floating-point number, a <c>decimal</c>, a <c>bool</c>, a string without
/// holes, a <c>char</c>, a unit literal); the one value of a <c>data</c> type without fields (D8),
/// which F12 counts among them; literals under the operators of T18 (with T9's sign rule), T22 and
/// T12, folded one operation at a time in the type the literals take (L17, T6); and a written
/// conversion of one of these (T10). The type stands beside it, in the <see cref="Typed"/> that
/// carries it.
/// </summary>
public abstract record Constant;

/// <summary>The value of an integer type, or the count of nanoseconds of a time type (T24).</summary>
public sealed record IntegerConstant(BigInteger Value) : Constant;

/// <summary>The value of a <c>float</c> (rounded to one) or a <c>double</c>, IEEE 754 as C# folds it (T6).</summary>
public sealed record RealConstant(double Value) : Constant;

public sealed record DecimalConstant(decimal Value) : Constant;

public sealed record BoolConstant(bool Value) : Constant;

/// <summary>The text of a string literal without holes.</summary>
public sealed record StringConstant(string Value) : Constant;

/// <summary>A code point (T21).</summary>
public sealed record CharConstant(int Value) : Constant;

/// <summary>The one value of a <c>data</c> type without fields (D8), which F12 counts as a constant.</summary>
public sealed record UnitConstant : Constant;

/// <summary><c>null</c>, which F12 counts as a constant; the checker types it as a pointer alone (R10).</summary>
public sealed record NullConstant : Constant;

/// <summary>What the checker knows about an expression: its type, and its value where it is a constant.</summary>
public readonly record struct Typed(KurzType Type, Constant? Constant = null)
{
    public static Typed Error => new(Types.Error);

    public bool IsError => Type is ErrorType;
}
