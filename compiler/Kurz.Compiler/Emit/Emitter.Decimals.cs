using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// <c>decimal</c> (T2): C#'s System.Decimal, the pair <c>%kz.dec</c> of two 64-bit words, the low 64 bits of its 96-bit integer,
/// then its high 32 bits with System.Decimal's flags above them, the scale in bits 48 to 55 and the sign in bit 63. The runtime
/// computes the operations, the conversions to and from the floating-point types and the text (<c>runtime/common/runtime.kz</c>,
/// "decimal"); the emitted code turns the sign, widens an integer, and raises what the runtime reports: <c>overflow</c> in every
/// build where a result leaves the range (the owner, 2026-10-28, "Raise in every build"), and <c>divide-by-zero</c> before a
/// division or a remainder by zero, in every build (T25).
/// </summary>
public sealed partial class Emitter
{
    /// <summary>Bit 63 of the high word, the sign, as the <c>i64</c> literal LLVM reads.</summary>
    private const string DecimalSign = "-9223372036854775808";

    /// <summary>A <c>decimal</c> constant, written as an operand is, without its type: System.Decimal's bits, as the checker folded them.</summary>
    private static string DecimalText(decimal value)
    {
        var bits = decimal.GetBits(value);
        var low = (ulong)(uint)bits[0] | ((ulong)(uint)bits[1] << 32);
        var high = (ulong)(uint)bits[2] | ((ulong)(uint)bits[3] << 32);
        return $"{{ i64 {Number((long)low)}, i64 {Number((long)high)} }}";
    }

    /// <summary>The two words of a <c>decimal</c>, as the runtime takes them.</summary>
    private (string Low, string High) DecimalWords(string value) =>
        (fn.Value($"extractvalue %kz.dec {value}, 0"), fn.Value($"extractvalue %kz.dec {value}, 1"));

    /// <summary>A <c>decimal</c> of its two words.</summary>
    private string DecimalOf(string low, string high) =>
        fn.Value($"insertvalue %kz.dec {fn.Value($"insertvalue %kz.dec undef, i64 {low}, 0")}, i64 {high}, 1");

    /// <summary>Unary <c>-</c>: the sign turned, a zero's too, which only this gives a sign (the owner, 2026-10-28, "None, as rust_decimal").</summary>
    private string DecimalNegate(string value)
    {
        var (low, high) = DecimalWords(value);
        return DecimalOf(low, fn.Value($"xor i64 {high}, {DecimalSign}"));
    }

    /// <summary><c>+ - * / %</c> on two <c>decimal</c>s (T18): the runtime's operation, <c>-</c> as the sum with the second's sign turned.</summary>
    private string DecimalArithmetic(TokenKind op, string left, string right, int line)
    {
        var (aLow, aHigh) = DecimalWords(left);
        var (bLow, bHigh) = DecimalWords(right);
        if (op is TokenKind.Slash or TokenKind.Percent)
        {
            // T25: the 96 bits of the divisor all zero, whatever its sign and its scale
            var digits = fn.Value($"or i64 {bLow}, {fn.Value($"and i64 {bHigh}, 4294967295")}");
            var fail = fn.Label("divzero");
            var ok = fn.Label("divide");
            fn.CondBranch(fn.Value($"icmp eq i64 {digits}, 0"), fail, ok);
            fn.Block(fail);
            Raise(ErrorIds.DivideByZero, line);
            fn.Block(ok);
        }

        if (op == TokenKind.Minus)
        {
            bHigh = fn.Value($"xor i64 {bHigh}, {DecimalSign}");
        }

        var low = fn.Alloca("i64", "low");
        var high = fn.Alloca("i64", "high");
        var name = op switch
        {
            TokenKind.Plus or TokenKind.Minus => "DecimalAdd",
            TokenKind.Star => "DecimalMultiply",
            TokenKind.Slash => "DecimalDivide",
            _ => "DecimalRemainder",
        };
        var arguments = $"i64 {aLow}, i64 {aHigh}, i64 {bLow}, i64 {bHigh}, ptr {low}, ptr {high}";
        if (op == TokenKind.Percent)
        {
            fn.Emit($"call void {RuntimeFunction(name)}({arguments})"); // a remainder never leaves the range
        }
        else
        {
            RaiseUnless(fn.Value($"call i1 {RuntimeFunction(name)}({arguments})"), line);
        }

        return DecimalOf(fn.Value($"load i64, ptr {low}"), fn.Value($"load i64, ptr {high}"));
    }

    /// <summary>The order of two <c>decimal</c>s by their values, as an <c>i32</c> of -1, 0 or 1: 1.5 equals 1.50, and 0 equals -0 (T18, D3).</summary>
    private string DecimalOrder(string left, string right)
    {
        var (aLow, aHigh) = DecimalWords(left);
        var (bLow, bHigh) = DecimalWords(right);
        return fn.Value($"call i32 {RuntimeFunction("DecimalCompare")}(i64 {aLow}, i64 {aHigh}, i64 {bLow}, i64 {bHigh})");
    }

    /// <summary>An integer as a <c>decimal</c> (T23): its magnitude as the 96-bit integer at the scale 0, the sign set for a negative one.</summary>
    private string DecimalFromInteger(string value, KurzType from)
    {
        var signed = Types.IsSigned(from);
        var wide = Resize(value, from, signed ? Types.Long : Types.ULong);
        if (!signed)
        {
            return DecimalOf(wide, "0");
        }

        var negative = fn.Value($"icmp slt i64 {wide}, 0");
        var magnitude = fn.Value($"select i1 {negative}, i64 {fn.Value($"sub i64 0, {wide}")}, i64 {wide}"); // the smallest `long`'s bits are its magnitude
        return DecimalOf(magnitude, fn.Value($"select i1 {negative}, i64 {DecimalSign}, i64 0"));
    }

    /// <summary>
    /// A <c>float</c> or a <c>double</c> as a <c>decimal</c> (T26): the runtime's conversion of its exact binary value (the owner,
    /// 2026-10-28, "Exact binary value"); NaN, an infinity and a value beyond the range raise <c>overflow</c> in every build (the owner,
    /// 2026-10-28, "Raise in every build").
    /// </summary>
    private string DecimalFromFloating(string value, KurzType from, int line)
    {
        var wide = from is PrimitiveType { Kind: Primitive.Float } ? fn.Value($"fpext float {value} to double") : value;
        var low = fn.Alloca("i64", "low");
        var high = fn.Alloca("i64", "high");
        RaiseUnless(fn.Value($"call i1 {RuntimeFunction("DecimalFromDouble")}(i64 {fn.Value($"bitcast double {wide} to i64")}, ptr {low}, ptr {high})"), line);
        return DecimalOf(fn.Value($"load i64, ptr {low}"), fn.Value($"load i64, ptr {high}"));
    }

    /// <summary>A <c>decimal</c> as a <c>double</c>, as C# computes it (the owner, 2026-10-28, "As C# computes it"), and as a <c>float</c> that <c>double</c> rounded, as C# rounds it.</summary>
    private string DecimalToFloating(string value, KurzType to)
    {
        var (low, high) = DecimalWords(value);
        var d = fn.Value($"call double {RuntimeFunction("DecimalToDouble")}(i64 {low}, i64 {high})");
        return to is PrimitiveType { Kind: Primitive.Float } ? fn.Value($"fptrunc double {d} to float") : d;
    }

    /// <summary>
    /// A <c>decimal</c> into an integer type (T26): its fraction dropped toward zero. A value beyond the type's range raises
    /// <c>overflow</c> in a test build and gives the type's largest or smallest value in a release build.
    /// </summary>
    private string DecimalToInteger(string value, KurzType to, int line)
    {
        var (low, high) = DecimalWords(value);
        var slot = fn.Alloca("i64", "magnitude");
        var narrow = fn.Value($"call i1 {RuntimeFunction("DecimalTruncate")}(i64 {low}, i64 {high}, ptr {slot})");
        var magnitude = fn.Value($"load i64, ptr {slot}");
        var negative = fn.Value($"icmp slt i64 {high}, 0");
        var (min, max) = Types.Range(to);
        var limit = fn.Value($"select i1 {negative}, i64 {IntegerText(-min, Types.ULong)}, i64 {IntegerText(max, Types.ULong)}");
        var within = fn.Value($"and i1 {narrow}, {fn.Value($"icmp ule i64 {magnitude}, {limit}")}");
        var bits = fn.Value($"select i1 {negative}, i64 {fn.Value($"sub i64 0, {magnitude}")}, i64 {magnitude}");
        var result = Resize(bits, Types.Long, to);
        if (test)
        {
            RaiseUnless(within, line);
            return result;
        }

        var bound = fn.Value($"select i1 {negative}, {Ir(to)} {IntegerText(min, to)}, {Ir(to)} {IntegerText(max, to)}");
        return fn.Value($"select i1 {within}, {Ir(to)} {result}, {Ir(to)} {bound}");
    }

    /// <summary><c>overflow</c> at the line unless the condition holds.</summary>
    private void RaiseUnless(string condition, int line)
    {
        var fail = fn.Label("overflow");
        var ok = fn.Label("fits");
        fn.CondBranch(condition, ok, fail);
        fn.Block(fail);
        Raise(ErrorIds.Overflow, line);
        fn.Block(ok);
    }
}
