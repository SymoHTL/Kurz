using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The time types (T13, T24, T29): a <c>duration</c> and a <c>timestamp</c> are the <c>i64</c> of their nanoseconds, a
/// <c>longduration</c> and a <c>longtimestamp</c> the <c>i128</c>. A narrow value beside a wide one, and one where a wide one is
/// required, is extended by its sign (T29). <c>+</c>, <c>-</c> and the unary <c>-</c> raise <c>overflow</c> where the result leaves
/// its type's range, and so does a written conversion into the narrow pair that loses the value, in every build (T31: the owner,
/// 2026-10-28, "Raise in every build"). The runtime makes the text (<c>runtime/common/runtime.kz</c>, "the time types"), given
/// the two 64-bit words of the 128 bits.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>A time value as the bits of the wide pair where <paramref name="wide"/> says so, a narrow one extended by its sign.</summary>
    private string TimeBits(string value, KurzType from, bool wide) =>
        wide && !Types.IsWideTime(from) ? fn.Value($"sext i64 {value} to i128") : value;

    /// <summary><c>+</c> or <c>-</c> of two time values into <paramref name="type"/>, as the checker typed it, wide where an operand is (T29).</summary>
    private string TimeArithmetic(TokenKind op, string left, KurzType leftType, string right, KurzType rightType, KurzType type, int line)
    {
        var wide = Types.IsWideTime(type);
        var instruction = op == TokenKind.Plus ? "add" : "sub";
        return Arithmetic(instruction, TimeBits(left, leftType, wide), TimeBits(right, rightType, wide), type, line, checkedBuild: true);
    }

    /// <summary>A written conversion between the two widths of durations or of timestamps: the narrow value widened, the wide one narrowed where it fits.</summary>
    private string TimeConversion(string value, KurzType from, KurzType to, int line)
    {
        if (Lower.Size(from) <= Lower.Size(to))
        {
            return TimeBits(value, from, Types.IsWideTime(to));
        }

        var cut = fn.Value($"trunc i128 {value} to i64");
        RaiseUnless(fn.Value($"icmp eq i128 {fn.Value($"sext i64 {cut} to i128")}, {value}"), line);
        return cut;
    }

    /// <summary>The order of two time values, the narrow one widened beside a wide one (T29).</summary>
    private string TimeComparison(TokenKind op, string left, KurzType leftType, string right, KurzType rightType)
    {
        var wide = Types.IsWideTime(leftType) || Types.IsWideTime(rightType);
        return fn.Value($"icmp {Condition(op, signed: true)} {(wide ? "i128" : "i64")} {TimeBits(left, leftType, wide)}, {TimeBits(right, rightType, wide)}");
    }

    /// <summary>The text of a duration (A12) or a timestamp (A16), made by the runtime from the two words of the 128 bits.</summary>
    private string TimeText(string value, KurzType type)
    {
        var bits = TimeBits(value, type, wide: true);
        var low = fn.Value($"trunc i128 {bits} to i64");
        var high = fn.Value($"trunc i128 {fn.Value($"lshr i128 {bits}, 64")} to i64");
        return fn.Value($"call %kz.str {RuntimeFunction(Types.IsDuration(type) ? "TextOfDuration" : "TextOfTimestamp")}(i64 {low}, i64 {high})");
    }
}
