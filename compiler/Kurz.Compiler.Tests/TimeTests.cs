using System.Globalization;
using System.Numerics;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on the time types (T13, T24, T29, T31) that no corpus case pins: the text of a duration (A12) of 3000 values of a
/// xorshift sequence of 0 to 127 bits and of the edges of both widths, against the split read off the nanoseconds; the text of a
/// timestamp of either width (A16: the owner, 2026-10-28, "RFC 3339 UTC, digits as needed", "Groups of three, as chrono" and
/// "Sign + 4 or more digits, as chrono") of as many values and the edges of the calendar, against .NET's calendar moved by whole
/// cycles of 400 years, which the Gregorian calendar repeats; <c>overflow</c> in every build for <c>+</c>, <c>-</c>, the unary
/// <c>-</c> and a conversion into the narrow pair (T31, "Raise in every build"); the wide pair's 16 bytes aligned to 16 ("16 bytes,
/// 16-aligned"); and the widening, the mixed operations, the comparisons, a field, a case of a union, a list and an interpolation.
/// </summary>
public sealed class TimeTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly BigInteger Wide = BigInteger.One << 127;
    private static readonly BigInteger Narrow = BigInteger.One << 63;

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name, bool testBuild = true)
    {
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), ["raw"], toolchain.Runtime, testBuild);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    private static readonly (long Size, string Name)[] Units =
        [(86_400_000_000_000, "days"), (3_600_000_000_000, "h"), (60_000_000_000, "min"), (1_000_000_000, "s"), (1_000_000, "ms"), (1_000, "us"), (1, "ns")];

    /// <summary>The text of a duration (A12): from `days` down, each unit at most once and a zero count left out, `0ms` for zero, `-` before the whole.</summary>
    private static string DurationText(BigInteger value)
    {
        if (value.IsZero)
        {
            return "0ms";
        }

        var rest = BigInteger.Abs(value);
        var parts = new List<string>();
        foreach (var (size, name) in Units)
        {
            var count = BigInteger.DivRem(rest, size, out rest);
            if (!count.IsZero)
            {
                parts.Add(count.ToString(Invariant) + name);
            }
        }

        return (value.Sign < 0 ? "-" : "") + string.Join(' ', parts);
    }

    private static (BigInteger Quotient, BigInteger Remainder) FloorDivide(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var r);
        return r.Sign < 0 ? (q - 1, r + b) : (q, r);
    }

    /// <summary>
    /// The text of a timestamp (A16), of nanoseconds from 1970-01-01: the date and the time of day from .NET's calendar, the day
    /// moved into the 400 years from 2000-01-01 by whole cycles of 146097 days and the year moved back; a year outside 0000 to 9999
    /// with its sign and at least four digits; the fraction in groups of three digits, none for a whole second.
    /// </summary>
    private static string TimestampText(BigInteger nanoseconds)
    {
        var (seconds, nanos) = FloorDivide(nanoseconds, 1_000_000_000);
        var (days, second) = FloorDivide(seconds, 86_400);
        var (cycles, day) = FloorDivide(days - 10_957, 146_097);
        var date = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays((double)day).AddSeconds((double)second);
        var year = date.Year + (400 * cycles);
        var yearText = year.Sign < 0 ? "-" + BigInteger.Abs(year).ToString("D4", Invariant) : year > 9999 ? "+" + year.ToString(Invariant) : year.ToString("D4", Invariant);
        var fraction = nanos.IsZero ? ""
            : (nanos % 1_000_000).IsZero ? "." + (nanos / 1_000_000).ToString("D3", Invariant)
            : (nanos % 1_000).IsZero ? "." + (nanos / 1_000).ToString("D6", Invariant)
            : "." + nanos.ToString("D9", Invariant);
        return yearText + date.ToString("-MM-dd'T'HH:mm:ss", Invariant) + fraction + "Z";
    }

    private static ulong Step(ulong s)
    {
        s ^= s << 13;
        s ^= s >> 7;
        return s ^ (s << 17);
    }

    /// <summary>Values of 0 to 127 bits of either sign, within the wide pair, from a xorshift sequence.</summary>
    private static List<BigInteger> Values(int count, int maxBits)
    {
        var values = new List<BigInteger>();
        var s = 0x9E3779B97F4A7C15UL;
        for (var i = 0; i < count; i++)
        {
            s = Step(s);
            var low = s;
            s = Step(s);
            var high = s;
            s = Step(s);
            var bits = (int)(s % (ulong)(maxBits + 1));
            var magnitude = ((new BigInteger(high) << 64) | low) & ((BigInteger.One << bits) - 1);
            values.Add((s >> 32 & 1) == 1 ? -magnitude : magnitude);
        }

        return values;
    }

    /// <summary>A duration literal of the value: its nanoseconds, a `-` before them.</summary>
    private static string Literal(BigInteger value) => (value.Sign < 0 ? "-" : "") + BigInteger.Abs(value).ToString(Invariant) + "ns";

    private static readonly BigInteger[] DurationEdges =
    [
        0, 1, -1, 999, 1_000, 1_001, 999_999, 1_000_000, 59_999_999_999, 60_000_000_000, 3_599_999_999_999, 3_600_000_000_000,
        86_399_999_999_999, 86_400_000_000_000, 86_400_000_000_001, -86_400_000_000_000, Narrow - 1, -Narrow, Narrow, -Narrow - 1,
        Wide - 1, -Wide, -Wide + 1, BigInteger.Pow(10, 30), -BigInteger.Pow(10, 30),
    ];

    [Fact]
    public void A_duration_shows_its_units_from_days_down()
    {
        var values = Values(3000, 127).Concat(DurationEdges).ToList();
        var text = new StringBuilder();
        foreach (var value in values)
        {
            text.Append("print(").Append(Literal(value)).Append(")\n");
        }

        var run = BuildAndRun(text.ToString(), "time-duration-text");
        Ran(run, 0, values.Select(DurationText).ToArray(), "");
    }

    /// <summary>The nanoseconds from 1970-01-01 of an instant of .NET's calendar.</summary>
    private static BigInteger At(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new BigInteger((new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc) - DateTime.UnixEpoch).Ticks) * 100;

    private static readonly BigInteger Cycle = (BigInteger)146_097 * 86_400_000_000_000;

    private static readonly BigInteger[] CalendarEdges =
    [
        0, -1, 1, 1_000, 1_000_000, 1_500_000_000, 999_999_999, -999_999_999, -1_000_000_000,
        At(2000, 2, 29), At(2000, 3, 1) - 1, At(1900, 2, 28, 23, 59, 59), At(1900, 3, 1), At(2100, 3, 1), At(2026, 10, 28, 9, 30),
        At(1677, 9, 21, 0, 12, 43), At(2262, 4, 11, 23, 47, 16), Narrow - 1, -Narrow,
        At(9999, 12, 31, 23, 59, 59) + 999_999_999, At(9999, 12, 31, 23, 59, 59) + 1_000_000_000, At(1, 1, 1), At(1, 1, 1) - 1,
        At(2000, 1, 1) - (5 * Cycle), At(2000, 3, 1) - (5 * Cycle) - 1, At(2000, 2, 29) - (5 * Cycle), At(2000, 1, 1) + (20 * Cycle),
        Wide - 1, -Wide, -Wide + 1,
    ];

    private const string Show = """
        void Show(longduration d) {
            long zero = 0
            raw {
                longtimestamp epoch = *Ptr<timestamp>(&zero)
                print(epoch + d)
            }
        }

        void ShowNarrow(duration d) {
            long zero = 0
            raw {
                timestamp epoch = *Ptr<timestamp>(&zero)
                print(epoch + d)
            }
        }


        """;

    [Fact]
    public void A_timestamp_shows_its_instant_in_UTC_as_RFC_3339_writes_it()
    {
        var wide = Values(3000, 127).Concat(CalendarEdges).ToList();
        var narrow = Values(1000, 63).Where(v => v >= -Narrow && v < Narrow).ToList();
        var text = new StringBuilder(Show);
        foreach (var value in wide)
        {
            text.Append("Show(").Append(Literal(value)).Append(")\n");
        }

        foreach (var value in narrow)
        {
            text.Append("ShowNarrow(").Append(Literal(value)).Append(")\n");
        }

        var run = BuildAndRun(text.ToString(), "time-timestamp-text");
        Ran(run, 0, wide.Concat(narrow).Select(TimestampText).ToArray(), "");
    }

    [Fact]
    public void The_text_of_a_timestamp_is_what_chrono_writes()
    {
        // chrono 0.4.38 with rustc 1.82, 2026-10-28: to_rfc3339_opts(SecondsFormat::AutoSi, true) of these instants
        var values = new[] { At(9999, 12, 31) + (BigInteger)86_400_000_000_000, At(1, 1, 1) - (BigInteger)366 * 86_400_000_000_000, At(1, 1, 1) - (BigInteger)366 * 86_400_000_000_000 - 1_000_000_000, 1_000_250_000, 1 };
        Assert.Equal(["+10000-01-01T00:00:00Z", "0000-01-01T00:00:00Z", "-0001-12-31T23:59:59Z", "1970-01-01T00:00:01.000250Z", "1970-01-01T00:00:00.000000001Z"], values.Select(TimestampText));
    }

    private const string Operations = """
        duration Add(duration a, duration b) => a + b
        duration Sub(duration a, duration b) => a - b
        duration Neg(duration a) => -a
        longduration AddWide(longduration a, longduration b) => a + b
        longduration SubWide(longduration a, longduration b) => a - b
        longduration NegWide(longduration a) => -a
        duration Narrowed(longduration a) => duration(a)
        timestamp Later(timestamp t, duration d) => t + d
        timestamp Earlier(timestamp t, duration d) => t - d
        timestamp After(duration d, timestamp t) => d + t
        duration Between(timestamp a, timestamp b) => a - b
        longtimestamp LaterWide(longtimestamp t, longduration d) => t + d
        timestamp NarrowedAt(longtimestamp t) => timestamp(t)

        timestamp At(long n) {
            long bits = n
            raw {
                return *Ptr<timestamp>(&bits)
            }
        }


        """;

    /// <summary>A file name of a call's letters and digits, a `-` as `M`.</summary>
    private static string Name(string call) => new(call.Select(c => c == '-' ? 'M' : c).Where(char.IsLetterOrDigit).ToArray());

    [Theory]
    [InlineData("Add(100000days, 10000days)", 1)]
    [InlineData("Add(-100000days, -10000days)", 1)]
    [InlineData("Sub(-100000days, 10000days)", 2)]
    [InlineData("Neg(-9223372036854775808ns)", 3)]
    [InlineData("AddWide(170141183460469231731687303715884105727ns, 1ns)", 4)]
    [InlineData("SubWide(-170141183460469231731687303715884105728ns, 1ns)", 5)]
    [InlineData("NegWide(-170141183460469231731687303715884105728ns)", 6)]
    [InlineData("Narrowed(9223372036854775808ns)", 7)]
    [InlineData("Narrowed(-9223372036854775809ns)", 7)]
    [InlineData("Later(At(9223372036854775807), 1ns)", 8)]
    [InlineData("Earlier(At(-9223372036854775808), 1ns)", 9)]
    [InlineData("After(1ns, At(9223372036854775807))", 10)]
    [InlineData("Between(At(9223372036854775807), At(-1))", 11)]
    [InlineData("LaterWide(At(1), 170141183460469231731687303715884105727ns)", 12)]
    [InlineData("NarrowedAt(LaterWide(At(9223372036854775807), 1ns))", 13)]
    public void Time_arithmetic_and_a_narrowing_beyond_the_range_raise_in_every_build(string call, int line)
    {
        // T31, the owner, 2026-10-28, "Raise in every build": the exception at the line of the operation, a release build as well
        var text = Operations + $"print({call})\n";
        var name = "time-raise-" + Name(call);
        Ran(BuildAndRun(text, name + "-test"), 1, [], $"error: overflow at program.kz:{line}\n");
        Ran(BuildAndRun(text, name + "-release", testBuild: false), 1, [], $"error: overflow at program.kz:{line}\n");
    }

    [Fact]
    public void Time_arithmetic_and_a_narrowing_at_the_edges_of_the_range_keep_the_value()
    {
        var text = Operations + """
            print(Add(9223372036854775806ns, 1ns))
            print(Sub(-9223372036854775807ns, 1ns))
            print(Neg(-9223372036854775807ns))
            print(AddWide(170141183460469231731687303715884105726ns, 1ns))
            print(NegWide(-170141183460469231731687303715884105727ns))
            print(Narrowed(9223372036854775807ns))
            print(Narrowed(-9223372036854775808ns))
            print(Later(At(9223372036854775806), 1ns))
            print(Between(At(9223372036854775807), At(0)))
            print(Between(At(-1), At(9223372036854775807)))
            print(NarrowedAt(LaterWide(At(0), -9223372036854775808ns)))
            """;
        Ran(BuildAndRun(text, "time-edges", testBuild: false), 0,
        [
            DurationText(Narrow - 1), DurationText(-Narrow), DurationText(Narrow - 1), DurationText(Wide - 1), DurationText(Wide - 1),
            DurationText(Narrow - 1), DurationText(-Narrow), TimestampText(Narrow - 1), DurationText(Narrow - 1), DurationText(-Narrow),
            TimestampText(-Narrow),
        ], "");
    }

    [Fact]
    public void The_wide_pair_takes_16_bytes_aligned_to_16()
    {
        // the owner, 2026-10-28, "16 bytes, 16-aligned": a union's room for a wide case is one i128, as LLVM aligns it, and a data value
        // with a byte before it holds it at 16
        var text = """
            data Span(byte Tag, longduration Length)

            byte | longduration u = 200000days
            print(u)
            mut spans = List<Span>()
            spans.Add(Span(7, 2s))
            spans.Add(Span(9, 200000days))
            print(spans)
            print(SizeOf<longduration>())
            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        Assert.Contains("{ i8, [1 x i128] }", compilation.Program.Ir, StringComparison.Ordinal);
        Assert.Contains("%\"d.Span\" = type { i8, i128 }", compilation.Program.Ir, StringComparison.Ordinal);
        Ran(Toolchain.Run(toolchain.Build(compilation.Program, "time-layout")), 0,
            ["200000days", "[Span { Tag = 7, Length = 2s }, Span { Tag = 9, Length = 200000days }]", "16"], "");
    }

    [Fact]
    public void Widening_comparisons_fields_unions_lists_and_interpolations_hold_a_time()
    {
        var text = Operations + """
            data Span(byte Tag, longduration Length)

            duration span = 90min
            longduration w = span
            print(w)
            print(span == w)
            print(w < 100000000days)
            print(AddWide(w, 1ns) > span)
            print(Sub(span, 1h) <= 30min)
            print(Span(1, 200000days) == Span(1, 200000days))
            print(Span(1, span) == Span(1, 90min + 1ns))
            int | duration u = 5days
            print(u)
            mut xs = List<longduration>()
            xs.Add(1s)
            xs.Add(200000days)
            xs.Add(Neg(span))
            print(xs)
            print(xs[1] - xs[0])
            print("{span} and {Neg(span)}")
            timestamp start = At(-1)
            longtimestamp lt = start
            print(lt - start)
            print(LaterWide(lt, 1s))
            print(Later(start, 1ns) == LaterWide(lt, 1ns))
            print(start < LaterWide(lt, 200000days))
            print(Between(Later(start, 1s), start))
            print(Narrowed(w))
            print(NarrowedAt(LaterWide(lt, 1ns)))
            """;
        Ran(BuildAndRun(text, "time-values"), 0,
        [
            "1h 30min", "true", "true", "true", "true", "true", "false", "5days", "[1s, 200000days, -1h 30min]", "199999days 23h 59min 59s",
            "1h 30min and -1h 30min", "0ms", "1970-01-01T00:00:00.999999999Z", "true", "true", "1s", "1h 30min", "1970-01-01T00:00:00Z",
        ], "");
    }
}
