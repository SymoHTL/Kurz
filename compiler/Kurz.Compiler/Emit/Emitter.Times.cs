using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The time types (T13, T24, T29): a <c>duration</c> and a <c>timestamp</c> are the <c>i64</c> of their nanoseconds, a
/// <c>longduration</c> and a <c>longtimestamp</c> the <c>i128</c>. A narrow value beside a wide one, and one where a wide one is
/// required, is extended by its sign (T29). <c>+</c>, <c>-</c> and the unary <c>-</c> raise <c>overflow</c> where the result leaves
/// its type's range, and so does a written conversion into the narrow pair that loses the value, in every build (T31: the owner,
/// 2026-10-28, "Raise in every build"). The runtime makes the text (<c>runtime/common/runtime.kz</c>, "the time types"), given
/// the two 64-bit words of the 128 bits. A reading of the monotonic clock, an <c>instant</c>, is the <c>i64</c> of its nanoseconds
/// too, with no wide variant, and shows the text of a duration (T36, S25).
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

    /// <summary>
    /// T38: the time of a count of <see cref="CallInfo.TimeScale"/> nanoseconds each, raising <c>overflow</c> where it leaves the range of 64
    /// bits, in every build, as T31's arithmetic does. A <c>long</c> count is multiplied. A <c>double</c> one is split at its point: its whole
    /// units are multiplied as a <c>long</c>, and its fraction's product is rounded to the nearest nanosecond, ties to even, where the error
    /// of that product (<c>fma</c>) decides a product that was rounded onto a half, so that the double's exact value is rounded once, as
    /// <c>Checker.NanosecondsOf</c> rounds it; every other unit is an even count of nanoseconds, so the whole units keep the parity the
    /// rounding of the fraction picked, and a count of nanoseconds is rounded whole. NaN and an infinity fail the range and raise.
    /// </summary>
    private string EmitTimeFrom(CallExpression c, CallInfo info)
    {
        var real = Types.IsFloating(info.Target!);
        var count = Arguments(c, info, _ => info.Target!, _ => false, _ => null, cConvention: false, owned: false).Single()[(real ? "double " : "i64 ").Length..];
        var scale = Number(info.TimeScale);
        if (!real)
        {
            return info.TimeScale == 1 ? count : Arithmetic("mul", count, scale, Types.Duration, c.Line, checkedBuild: true);
        }

        module.Declare("declare double @llvm.trunc.f64(double)");
        module.Declare("declare double @llvm.rint.f64(double)");
        module.Declare("declare double @llvm.fma.f64(double, double, double)");
        if (info.TimeScale == 1)
        {
            // a count of nanoseconds is rounded whole, ties to even: split at its point, a tie of its fraction would go to the even
            // fraction, 3.5 to 3, where the even of the whole count is 4
            var nearest = fn.Value($"call double @llvm.rint.f64(double {count})");
            var above = fn.Value($"fcmp oge double {nearest}, {RealText(-9223372036854775808.0)}");
            var below = fn.Value($"fcmp olt double {nearest}, {RealText(9223372036854775808.0)}");
            RaiseUnless(fn.Value($"and i1 {above}, {below}"), c.Line);
            return fn.Value($"fptosi double {nearest} to i64");
        }

        var whole = fn.Value($"call double @llvm.trunc.f64(double {count})");
        var low = fn.Value($"fcmp oge double {whole}, {RealText(-9223372036854775808.0)}");
        var high = fn.Value($"fcmp olt double {whole}, {RealText(9223372036854775808.0)}");
        RaiseUnless(fn.Value($"and i1 {low}, {high}"), c.Line);
        var units = Arithmetic("mul", fn.Value($"fptosi double {whole} to i64"), scale, Types.Duration, c.Line, checkedBuild: true);
        var fraction = fn.Value($"fsub double {count}, {whole}");
        var step = RealText(info.TimeScale);
        var product = fn.Value($"fmul double {fraction}, {step}");
        var error = fn.Value($"call double @llvm.fma.f64(double {fraction}, double {step}, double {fn.Value($"fneg double {product}")})");
        var near = fn.Value($"call double @llvm.rint.f64(double {product})");
        var off = fn.Value($"fsub double {product}, {near}");
        var up = fn.Value($"and i1 {fn.Value($"fcmp oeq double {off}, {RealText(0.5)}")}, {fn.Value($"fcmp ogt double {error}, {RealText(0.0)}")}");
        var down = fn.Value($"and i1 {fn.Value($"fcmp oeq double {off}, {RealText(-0.5)}")}, {fn.Value($"fcmp olt double {error}, {RealText(0.0)}")}");
        var rounded = fn.Value($"select i1 {up}, double {fn.Value($"fadd double {near}, {RealText(1.0)}")}, double {near}");
        rounded = fn.Value($"select i1 {down}, double {fn.Value($"fsub double {rounded}, {RealText(1.0)}")}, double {rounded}");
        return Arithmetic("add", units, fn.Value($"fptosi double {rounded} to i64"), Types.Duration, c.Line, checkedBuild: true);
    }

    /// <summary>S25: <c>t.Elapsed</c>, <c>instant.Now - t</c>, the reading taken first and the clock read after it.</summary>
    private string Elapsed(string reading, int line) =>
        TimeArithmetic(TokenKind.Minus, EmitMonotonicNow(line), Types.Instant, reading, Types.Instant, Types.Duration, line);

    /// <summary>The order of two time values, the narrow one widened beside a wide one (T29).</summary>
    private string TimeComparison(TokenKind op, string left, KurzType leftType, string right, KurzType rightType)
    {
        var wide = Types.IsWideTime(leftType) || Types.IsWideTime(rightType);
        return fn.Value($"icmp {Condition(op, signed: true)} {(wide ? "i128" : "i64")} {TimeBits(left, leftType, wide)}, {TimeBits(right, rightType, wide)}");
    }

    /// <summary>The text of a duration (A12) or a timestamp (A16), made by the runtime from the two words of the 128 bits; a reading of the monotonic clock shows its time since the clock's zero as a duration does (S25).</summary>
    private string TimeText(string value, KurzType type)
    {
        var bits = TimeBits(value, type, wide: true);
        var low = fn.Value($"trunc i128 {bits} to i64");
        var high = fn.Value($"trunc i128 {fn.Value($"lshr i128 {bits}, 64")} to i64");
        return fn.Value($"call %kz.str {RuntimeFunction(Types.IsTimestamp(type) ? "TextOfTimestamp" : "TextOfDuration")}(i64 {low}, i64 {high})");
    }
}
