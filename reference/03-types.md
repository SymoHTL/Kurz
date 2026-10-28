# 3. Types and numbers

### T1 (decided, §4) Static types, inferred

Every variable has one type, fixed when it is declared. Without a written type it is the type of
the first value. Using a value where another type is required is the compile error
`type-mismatch`.

Case: [types/inferred.kz](../corpus/types/inferred.kz)
```kurz
mut x = 1
x = "one"
print(x)
```

### T2 (decided, §4) Number types

The integer types are `sbyte`, `byte`, `short`, `ushort`, `int` (32 bits), `uint`, `long` (64 bits)
and `ulong`. The others are `float`, `double` and `decimal`. Each has the size its name has in C#.
C#'s `nint` and `nuint` are not among them: a size or an index is an `int` or a `long` (the owner,
2026-10-09, against pointer-sized types whose width follows the target, under which
`constant-overflow` (T6) would depend on the target and a value of that type could not be sent
between machines without naming a width; the cost is that ported interop code writes `long`).

Case: [types/long.kz](../corpus/types/long.kz)
```kurz
long big = 2147483647
print(big + 1)
```

### T3 (decided, §4) Signed and unsigned

`sbyte`, `short`, `int` and `long` are signed. `byte`, `ushort`, `uint` and `ulong` are unsigned.

Case: [types/unsigned.kz](../corpus/types/unsigned.kz)
```kurz
uint big = 4000000000
ulong most = 18446744073709551615
sbyte low = -100
print(big)
print(most)
print(low)
```

### T4 (decided, §4) Overflow wraps in a release build

Integer arithmetic that leaves the range of its type wraps around silently.

Case: [types/overflow-release.kz](../corpus/types/overflow-release.kz)
```kurz
mut int x = 2147483647
x = x + 1
print(x)
```

### T5 (decided, §4) Overflow throws in a test build

The same arithmetic raises the exception `overflow` in a test build (E3).

Case: [types/overflow-test.kz](../corpus/types/overflow-test.kz)
```kurz
mut int x = 2147483647
x = x + 1
print(x)
```

### T6 (decided, §4) A constant that overflows is an error

An expression made only of literals whose result leaves the range of its type is the compile error
`constant-overflow`. Under `+%`, `-%` and `*%` (T12) such an expression folds to the wrapped value
instead, as the operator promises for every value: `2147483647 +% 1` is `-2147483648`. A conversion
(T10) to an integer type written on a constant that does not fit its target, `byte(300)`, is
`constant-overflow`, as C# refuses a constant cast that does not fit (the owner, 2026-10-09, against both being
`constant-overflow`, under which a checksum written on literals does not compile, and against both
computing, under which `byte(300)` is `44` and a value is lost without a word, which T20 refuses
when the program runs; the cost is a constant folder that knows two kinds of operator). Each
operator of a constant expression folds by its own rule, and the written or expected type reaches
the fold (L17): `2147483647 + 1 +% 0` is `constant-overflow` at the `+`, and `byte b = 255 +% 1` is
`0`. An overflowing constant expression is the error for the integer types and `decimal` alone: one of
`float` or `double` folds to the value IEEE 754 gives it, an infinity or NaN included, as C# folds it
(T25). A literal whose value lies outside the range of the type it takes is the error whatever that
type, a duration (T29) included: `1e400` or `3.5e38f` is `constant-overflow`, as C# refuses such a
literal, and one that rounds to zero or to a subnormal is that value, as there (L11). A constant fits
the target of a written integer conversion when its value, the fraction dropped, lies in the target's
range: `int(2.5)` is `2`, and `int(1e10)`, `int(1.0 / 0.0)` and a NaN are `constant-overflow` (the
owner, 2026-10-10, confirming the readings proposed after the review of round 13). A written
conversion to a floating-point type folds as IEEE 754 has it, to an infinity beyond the range and with
rounding, never the error: `float(1e300)` is an infinity (the owner, 2026-10-10). A constant expression of
a time type (T13, T29), which counts nanoseconds in an integer, is the error as an integer one is; a
written conversion to `decimal` fits when the value, rounded to `decimal`'s precision,
lies in its range, and `decimal(1e30)` is `constant-overflow` (the owner, 2026-10-11, confirming the reading proposed after the review of round 14).

Case: [source/exponent-literal-too-large.kz](../corpus/source/exponent-literal-too-large.kz)
```kurz
x = 1e400
print(x)
// a literal outside the range of the type it takes is the error whatever that type (T6, L11); a floating-point expression that overflows would fold to an infinity instead
```
Case: [types/conversion-float-infinity.kz](../corpus/types/conversion-float-infinity.kz)
```kurz
f = float(1e300)
print(f == f * 2)
```

Case: [types/constant-wrap-in-written-type.kz](../corpus/types/constant-wrap-in-written-type.kz)
```kurz
byte b = 255 +% 1
print(b)
```

Case: [types/constant-overflow.kz](../corpus/types/constant-overflow.kz)
```kurz
x = 2147483647 + 1
print(x)
```

Case: [types/constant-wrap.kz](../corpus/types/constant-wrap.kz)
```kurz
print(2147483647 +% 1)
```

Case: [types/constant-conversion-overflow.kz](../corpus/types/constant-conversion-overflow.kz)
```kurz
print(byte(300))
// 300 lies outside the range of byte, so the written conversion of the constant is the error (T6)
```

### T7 (decided, §4) No implicit narrowing

Putting a value of a wider integer type into a narrower one without a conversion (T10) is the
compile error `narrowing-conversion`. The id says "conversion" because the null chapter calls a
checked nullable value "narrowed" (N3), and the two have nothing to do with each other.

Case: [types/narrowing.kz](../corpus/types/narrowing.kz)
```kurz
long big = 5
int small = big
print(small)
```

### T8 (decided, §4) Implicit widening

A value of a narrower integer type becomes a wider one without a word when the wider type holds
every value of the narrower: within one signedness, and across it where C# widens (T23).

Case: [types/widening.kz](../corpus/types/widening.kz)
```kurz
int small = 5
long big = small
print(big)
```

Where a union is required (D9), a value that is no case of it stands there when it widens into
exactly one of its cases: a `byte` stands where `int | string` is required. One that would widen
into two of them, a `byte` where `short | int` is required, is the compile error `type-mismatch`
(T1), and a conversion (T10), `short(b)`, picks the case, as one picks the function of a call that
fits two through widening (F13). The owner chose this on 2026-10-21, against the narrowest case,
under which the `byte` becomes a `short` by C#'s better-conversion rule, and against no widening
into unions, under which the value must be one of the cases exactly, as Rust widens nothing
implicitly; the cost is a conversion at such places. A value that is one of the cases is that case,
and no other fit by widening or derivation is looked for, so a `short` where `short | int` is
required is a `short`, and so is a `data` value (D9). The owner chose this on 2026-10-21, against
two fits being `type-mismatch` even beside an exact case, under which a `User` where `User | Person`
is required, with `User : Person`, is `type-mismatch` too; C#'s better-conversion rule prefers the
identity conversion, and Rust has only the exact fit; the cost is that adding a case to a union can
change without a word which case an existing value goes to, as a `byte` goes to `int` where
`int | string` is required and to `byte` where `int | string | byte` is, while under the option
turned down that addition is a compile error. The owner kept this choice with that cost shown on
2026-10-21. A value that would go to two cases by deriving from them is `type-mismatch` too (D9).

Case: [types/widening-into-union.kz](../corpus/types/widening-into-union.kz)
```kurz
byte b = 1
int | string v = b
match v {
    int i => print(i)
    string s => print(s)
}
```

Case: [types/widening-into-two-cases.kz](../corpus/types/widening-into-two-cases.kz)
```kurz
byte b = 1
short | int v = b
match v {
    short s => print("short {s}")
    int i => print("int {i}")
}
```

Case: [types/conversion-picks-the-case.kz](../corpus/types/conversion-picks-the-case.kz)
```kurz
byte b = 1
short | int w = short(b)
match w {
    short x => print("short {x}")
    int y => print("int {y}")
}
```

A literal where a union is required whose cases it would widen into twice has its own type (L10),
`int` for `5`, so `long | double x = 5` is `type-mismatch`, and `5L` or `5.0` picks the case, as a
literal argument has its own type for the choice between functions (F14), so that the case does not
depend on the order the cases are written in. The owner chose this on 2026-10-21, against the first
case in the written order that can hold it, as a written or expected type reaches a literal (L17),
under which the case depends on the order and on the value; the cost is a suffix at such places.

Case: [types/literal-into-two-cases.kz](../corpus/types/literal-into-two-cases.kz)
```kurz
long | double x = 5
match x {
    long n => print("long {n}")
    double d => print("double {d}")
}
```

A union with exactly one number case, a case of a number type (T2: an integer type, `float`,
`double` or `decimal`), reaches a number literal, an integer literal or one with a fraction or an
exponent (L10, L11), as a written type does (L17), so `byte | string x = 5` makes the `5` a `byte`;
where it has two or more number cases the literal has its own type, which goes to a case as a value
does, so `short | long x = 5`, whose `int` widens into the `long` alone, is a `long`, while
`byte | short x = 5`, whose `int` widens into neither case, is `type-mismatch` too, and `byte(5)`
picks the case. The owner chose this on 2026-10-21, against every number case the literal fits
reaching it, with two fits the error, under which `byte | short x = 300` is a `short` and the case
depends on the literal's value, and against the literal's own type even beside one number case,
under which `byte | string x = 5` needs a conversion; the cost is a conversion at such places, as
for a literal whose own type would widen into two cases.

Case: [types/literal-one-number-case.kz](../corpus/types/literal-one-number-case.kz)
```kurz
byte | string x = 5
match x {
    byte n => print("byte {n}")
    string s => print(s)
}
```

Case: [types/literal-two-number-cases.kz](../corpus/types/literal-two-number-cases.kz)
```kurz
byte | short x = 5
match x {
    byte b => print("byte {b}")
    short s => print("short {s}")
}
```

Case: [types/literal-widens-into-one-case.kz](../corpus/types/literal-widens-into-one-case.kz)
```kurz
short | long x = 5
match x {
    short s => print("short {s}")
    long n => print("long {n}")
}
```

The union's one time case, a case of a time type (`duration`, `timestamp`, `longduration` or
`longtimestamp`: T13, T29), reaches a duration literal (L14) as a written type does, whatever number
cases the union has, so `int | duration d = 5days` makes the literal a `duration` and
`int | duration d = 106752days` is `constant-overflow` (T29); with two or more time cases the
literal keeps its own type, a `duration`, or a `longduration` when its value does not fit one (T29).
The owner chose this on 2026-10-21, against the literal's own type always, under which
`duration | string d = 106752days` is `type-mismatch`; the cost is the same conversion at such
places as for a number literal.

Case: [types/literal-one-time-case.kz](../corpus/types/literal-one-time-case.kz)
```kurz
int | duration d = 106752days
match d {
    int n => print("int {n}")
    duration t => print("duration {t}")
}
```

### T23 (decided, §4) Widening across signedness and into floating-point

As in C#: an unsigned type widens into every signed type that holds all its values, so `byte`
becomes `short`, `int` and `long`, `ushort` becomes `int` and `long`, and `uint` becomes `long`,
each without a word, and every integer type becomes `float`, `double` or `decimal` without a
word. Where `float` or `double` does not hold every value of the source (`float` above 2^24,
`double` above 2^53) the value is rounded to the nearest one the type holds, as in C#, and that
rounding is no loss in the sense of T20: `float f = n` rounds silently where `float(n)` is the
same conversion written out, and neither throws. A signed type never becomes unsigned without a
word. A value put where a type of the other signedness is required, into which it does not widen,
is the compile error `sign-mix` when the required type is at least as wide (`uint u = i`,
`int i = u`); when it is narrower the error is `narrowing-conversion` (T7), whatever the
signedness, so `byte c = a + b` with the `int` of T28 is T7's error. The owner chose this on
2026-10-03, against widening within one signedness only, under which `int x = b` needs `int(b)`;
the cost is that `b + i` with a `byte` and an `int` is an `int` and not T9's error, so T9's case
changed with it. *(assumed: the rounding, which "as in C#" implies, and which of the two ids a
value gets when both would fit)*

Case: [types/widening-across-sign.kz](../corpus/types/widening-across-sign.kz)
```kurz
byte b = 200
int i = 5
print(b + i)
double d = i
print(d / 2)
```

Case: [types/unsigned-takes-no-signed.kz](../corpus/types/unsigned-takes-no-signed.kz)
```kurz
int i = 5
uint u = i
print(u)
```

### T9 (decided, §4) No mixing of signed and unsigned

An operation between a signed and an unsigned integer finds its common type as C# does (T18,
T28): two operands narrower than `int` are promoted to `int`, so `sb + b` with an `sbyte` and a
`byte` is an `int`, and a narrow operand beside a `uint` or a `ulong` takes that type, so `u + b`
with a `uint` and a `byte` is a `uint`, as there. Where C# joins the two through `long`, or
refuses them, Kurz reports the compile error `sign-mix`: that is `uint` with a signed type
(`u + i`, a `long` in C#) and `ulong` with a signed type (an error in C# too). The error is judged
on the two operand types as written, before the promotion of T28.
The owner chose this on 2026-10-04, against C#'s `long` for `uint` with `int`; the cost is that a
ported program writes `long(u) + i`. A conversion (T10) puts both on one side.

Case: [types/sign-mix.kz](../corpus/types/sign-mix.kz)
```kurz
// C# computes a long here; T9 keeps the error
uint u = 200
int i = 5
print(u + i)
```

### T10 (decided, §4) A conversion is written as a call of the type

`int(value)` converts a number to an `int`. The name of every number type can be used this way.
It is how a value is put into a narrower type (T7) and how a signed and an unsigned value meet
(T9).

Case: [types/conversion-reaches-literals.kz](../corpus/types/conversion-reaches-literals.kz)
```kurz
print(long(2147483647 + 1))
// the target of the conversion reaches the integer literals inside it (L17), so the sum is folded as a long
```

Case: [types/conversion-float-infinity.kz](../corpus/types/conversion-float-infinity.kz)

Case: [types/conversion.kz](../corpus/types/conversion.kz)
```kurz
long big = 5
int small = int(big)
print(small)

byte b = 200
int i = 5
print(int(b) + i)
```

### T20 (decided, §4) A conversion that loses the value

A conversion from an integer source whose value does not fit the target type behaves as overflow
does (T4, T5): in a release build the value wraps around, in a test build it raises the exception
`overflow` (E3). The cases below have an integer source. A `double`, `float` or `decimal` source
is T26: a dropped fraction is no loss, and a value that does not fit saturates instead of
wrapping. An integer into `float` or `double` is rounded and is no loss (T23).

Case: [types/conversion-release.kz](../corpus/types/conversion-release.kz)
```kurz
byte Low(int n) => byte(n)

print(Low(300))
```

Case: [types/conversion-test.kz](../corpus/types/conversion-test.kz)
```kurz
byte Low(int n) => byte(n)

print(Low(300))
```

### T26 (decided, §4) A conversion from a floating-point or `decimal` source

Into an integer type, dropping a fraction is not losing the value: `int(2.5)` is `2`, toward
zero, as in C#. A value outside the target's range, an infinity and NaN lose the value: a test
build throws `overflow` (T5, E3); a release build gives the target's largest or smallest value for
an out-of-range value and an infinity, and `0` for NaN. The saturation is Kurz's own definition:
C# leaves the result unspecified, .NET saturates on every platform since .NET 9, and for a
`decimal` source C# throws in every build. So an integer source wraps (T20) and a floating-point
or `decimal` source saturates. The owner chose this on 2026-10-03, against wrapping the integer
part modulo the target's width, which means nothing to anyone and costs a modulo on every such
conversion, and against counting a dropped fraction as a loss, which would have put a rounding
call on every conversion. Into `float` or `double` the IEEE 754 rules of C# hold: `float(d)`
rounds a `double`, becomes an infinity beyond the range, and keeps NaN and an infinity as they
are; nothing of that is a loss *(assumed: the `float` and `double` targets, which the question of
2026-10-03 did not name)*. Into `decimal`, which has neither, NaN, an infinity and a value beyond
its range raise `overflow` (E3) in every build, a release build too, as C# throws for them (the
owner, 2026-10-28, "Raise in every build", against saturating in a release build and throwing in a
test build as an integer target does, which this rule assumed before; the cost is a release program
that ends with an error line instead of a clamped amount). A `double` becomes its exact binary value
rounded half to even at the largest scale up to 28 whose digits fit `decimal`'s 96 bits, its zeros
at the end removed down to the scale 0: `decimal(0.1)` is `0.1000000000000000055511151231` (the
owner, 2026-10-28, "Exact binary value", against the shortest digits that read back as the same
`double`, `0.1`, as the Rust crate rust_decimal's `from_f64` gives them, and against the 15
significant digits C# keeps; the cost is surprising digits for almost every `double`; rust_decimal's
`from_f64_retain` comes near it and differs in the last digits, `123.456` giving
`123.45600000000000306954461843` where the exact value rounds to `123.45600000000000306954461848`,
checked on 2026-10-28 with rust_decimal 1.36); that rounding is no loss. A `float` becomes the
`double` that holds it exactly, and then that *(assumed: the answer named the exact binary value,
which the `double` holds; C# keeps 7 significant digits of a `float`)*. A zero that comes of the
conversion has no sign, `decimal(-0.0)` included *(assumed: the reading of the answer "None, as
rust_decimal", under which only `-` gives a zero its sign)*. A `decimal` becomes a `double` as C#
computes it: its 96-bit integer as a `double`, divided by the `double` nearest to 10 to the power of
its scale, so `double(1m / 3m)` is `0.33333333333333337` (the owner, 2026-10-28, "As C# computes
it", against the nearest `double`, `0.3333333333333333`; the cost is a last bit that can differ from
the nearest `double`), and a `float` as that `double` rounded, as C# computes it *(assumed)*.

Case: [types/conversion-into-decimal.kz](../corpus/types/conversion-into-decimal.kz)
```kurz
double Tenth() => 0.1
decimal Third() => 1m / 3m

print(decimal(Tenth()))
print(decimal(0.1))
print(double(Third()))
print(decimal(-Tenth() * 1e-30))
```

Case: [types/conversion-into-decimal-raises.kz](../corpus/types/conversion-into-decimal-raises.kz)
```kurz
decimal Of(double x) => decimal(x)

print(Of(1e30))
```

Case: [types/conversion-drops-fraction.kz](../corpus/types/conversion-drops-fraction.kz)
```kurz
print(int(2.5))
print(int(-2.5))
```

Case: [types/conversion-saturates.kz](../corpus/types/conversion-saturates.kz)
```kurz
double big = 100000000000000000000.0
double zero = 0.0
print(int(big))
print(int(-big))
print(int(zero / zero))
```

Case: [types/conversion-float-test.kz](../corpus/types/conversion-float-test.kz)
```kurz
double big = 100000000000000000000.0
print(int(big))
```

### T30 (decided, §4) `decimal` arithmetic

A `decimal` is C#'s System.Decimal: an integer of 96 bits, a scale from 0 to 28 that counts its
digits after the point, and a sign, in 16 bytes. `+ - * / %` compute, round half to even and choose
the scale of their result as System.Decimal does, so `1.50m * 2` is `3.00`, `1m / 3m` is
`0.3333333333333333333333333333` and `5m / 2m` is `2.5`; `%` takes the sign of the dividend; `==`
and the comparisons compare the values, so `1.5m == 1.50m` *(assumed: T2 names C#'s type; Rust has
no `decimal`, and its crate rust_decimal chooses other scales, `2.50m * 0` is `0` there where
System.Decimal keeps `0.00`, so the two do not agree, and Kurz takes C#'s)*. A result beyond
±79228162514264337593543950335 raises `overflow` (E3) in every build, a release build too, as C#
throws for it (the owner, 2026-10-28, "Raise in every build", against raising in a test build and
saturating in a release build; the cost is nothing at run time beyond what the rounding checks
anyway, and a release program ends with an error line instead of a wrong amount). A zero that `+ - *
/ %` compute has no sign: `1.50m - 1.5m` shows `0.00` and `0m * -1m` shows `0`; only `-` gives a
zero its sign, as `-(0.0m)` shows `-0.0` (A8), and the digits and the scale stay System.Decimal's,
`2.50m * 0` showing `0.00` (the owner, 2026-10-28, "None, as rust_decimal", against System.Decimal's
sign bit, which it sets on `1.50m - 1.5m`, so that the text of A8 would show `-0.00`, and against no
sign on any zero, as C# shows none; the cost is a test for zero after each operation, and a sign bit
that differs from C#'s where no `==` and no text of C# shows it). `%` gives the exact remainder at
the larger of the two scales, also where System.Decimal throws `OverflowException` for a remainder
that fits: `4294967295m % 0.9999999999999999999999999999m` is `0.0000000000000000004294967295` (the
owner, 2026-10-28, "Exact remainder", against raising `overflow` where C# throws, under which the
runtime would mirror the widths System.Decimal computes in and a remainder that fits would raise;
the cost is nothing at run time, and the result differs from C#'s only where C# throws; rust_decimal
1.36 gives `0.9232812099469197934124335104` there).

Case: [types/decimal-arithmetic.kz](../corpus/types/decimal-arithmetic.kz)
```kurz
decimal Times(decimal a, decimal b) => a * b
decimal Over(decimal a, decimal b) => a / b
decimal Remainder(decimal a, decimal b) => a % b

print(Times(1.50m, 2))
print(Over(1m, 3m))
print(Over(2m, 3m))
print(Over(5m, 2m))
print(Times(1.5m, 1m) == 1.50m)
print(Remainder(-7.5m, 2m))
```

Case: [types/decimal-overflow-release.kz](../corpus/types/decimal-overflow-release.kz)
```kurz
decimal Add(decimal a, decimal b) => a + b

print(Add(79228162514264337593543950335m, 1m))
```

Case: [types/decimal-zero-sign.kz](../corpus/types/decimal-zero-sign.kz)
```kurz
decimal Minus(decimal a, decimal b) => a - b
decimal Times(decimal a, decimal b) => a * b
decimal Negate(decimal a) => -a

print(Minus(1.50m, 1.5m))
print(Times(0m, -1m))
print(Minus(-1m, -1m))
print(Negate(0.0m))
```

Case: [types/decimal-remainder-exact.kz](../corpus/types/decimal-remainder-exact.kz)
```kurz
decimal Remainder(decimal a, decimal b) => a % b

print(Remainder(4294967295m, 0.9999999999999999999999999999m))
print(4294967295m % 0.9999999999999999999999999999m)
```

### T11 (decided, §4) Wrapping on purpose

Explicit wrapping operators (T12) exist for intended cases such as hashes. They wrap in every
build.

Case: [types/wrapping.kz](../corpus/types/wrapping.kz)
```kurz
int Next(int n) => n +% 1
int Twice(int n) => n *% 2
int Lower(int n) => n -% 2147483647

print(Next(2147483647))
print(Twice(2147483647))
print(Lower(-2))
```

### T12 (decided, §4) The wrapping operators

`+%`, `-%` and `*%` add, subtract and multiply as `+`, `-` and `*` do, and wrap around when the
result leaves the range of its type. `+%` and `-%` have the precedence of `+` and `-`, and `*%` that of `*` *(assumed: proposed on
2026-10-09 and not objected to)*, and the associativity of those operators as well (the owner,
2026-10-10). They take integer operands only: a floating-point or `decimal` operand, `d +% 1.0` or
`sum +% 1.5` on a `byte`, whose `1.5` makes the sum a `double` (L17), is `type-mismatch` (T1) (the owner, 2026-10-11, confirming the reading proposed after the review of round 14). What they do to a constant expression is T6.

Case: [types/wrapping.kz](../corpus/types/wrapping.kz)

Case: [types/wrapping-precedence.kz](../corpus/types/wrapping-precedence.kz)
```kurz
print(1 +% 2 * 3)
// +% has the precedence of + (T12), so the product is formed first; with one operator of each level the associativity plays no part
```

### T13 (decided, §4) Durations and timestamps

Durations and timestamps are types of their own and never raw integers, `duration` and
`timestamp` with 64 bits (T24) and `longduration` and `longtimestamp` with 128 (T29): one of them
where an integer is required is the compile error `type-mismatch`. A duration is written with a
unit (L14). What one step of their bits is, and what the wide pair is for, are T24 and T29.

Case: [types/duration-not-integer.kz](../corpus/types/duration-not-integer.kz)
```kurz
int seconds = 30s
print(seconds)
```

### T24 (decided, §4) The names and the resolution of durations and timestamps

The two types are `duration` and `timestamp`, in lower case like the other built-in types, and
one step of their 64 bits is one nanosecond: a `duration` holds about 292 years, a `timestamp`,
counted from 1970-01-01 *(assumed: the zero point)*, the years 1677 to 2262. The owner chose this on 2026-10-03, against `Duration` and `Timestamp`
like the types of the library, against a step of one millisecond, which holds 292 million years
and nothing below a millisecond, and against .NET's tick of 100 nanoseconds. The ranges are wide
enough for a device and a server, and a nanosecond is what the clocks of the operating systems
give. The owner asked with it for a wider variant of the two types: T29.

Case: [types/duration-parameter.kz](../corpus/types/duration-parameter.kz)
```kurz
void Show(duration d) {
    print(d)
}

Show(90min)
```

### T29 (decided, §4) The wider pair: `longduration` and `longtimestamp`

Beside the 64-bit pair of T24 stand `longduration` and `longtimestamp`: 128 bits with the same step
of one nanosecond, for a calendar or an archive that reaches before 1677 or after 2262. A `duration`
widens into a `longduration` and a `timestamp` into a `longtimestamp` without a word (T8), a
conversion the other way is written out and checked as T20 says, and the texts are A12's. What T13
says of the narrow pair holds for the wide one, and an operation between the two pairs widens the
narrow operand and has the wide type, as `i + l` has `long` (T8), so that `longtimestamp -
timestamp` is a `longduration`. *(assumed: the mixed operations, proposed on 2026-10-07, when the review found the hole, and
accepted with T29 by its id on 2026-10-09 without the text shown)* The owner chose the wide pair with
the nanosecond step on 2026-10-04, against
a 64-bit pair with a millisecond step, in which nothing below a millisecond exists, and against a
type of the library, which has no literal (L14); the cost is 128-bit arithmetic and a second name
for every operation on time in the library. The names stand to the narrow pair as `long` stands to
`int`, and the type of a literal follows the integer literal of L10: a duration literal is a
`duration`, or a `longduration` when its value does not fit one, and where a type is written or
expected it takes that type, and is `constant-overflow` (T6) when its value does not fit it, as an
integer literal is (L10), so that a literal beyond both ranges is that error too. *(assumed: the names and the type of a literal, the reference's readings under
T29, accepted by its id on 2026-10-09 without their text shown)*

Case: [types/longduration-parameter.kz](../corpus/types/longduration-parameter.kz)
```kurz
void Show(longduration d) {
    print(d)
}

duration span = 90min
Show(span)
```

A `longduration` is narrowed to a `duration` only by a written conversion, which T20 checks like any
other: in the case below the sum is two hundred thousand days, beyond the narrow range, so `duration(l)`
raises `overflow` in a test build.

Case: [types/longduration-wide.kz](../corpus/types/longduration-wide.kz)
```kurz
longduration a = 100000days
longduration l = a + a
print(duration(l))
```

Where a union is required, its one time case reaches a duration literal as a written type does (T8).

Case: [types/literal-one-time-case.kz](../corpus/types/literal-one-time-case.kz)

### T14 (assumed, §4) Out of range and division by zero

An index that is out of range raises the exception `index-out-of-range`, and a division by zero, of
integers or of `decimal`, `divide-by-zero` (E3). What `%` by zero, a floating-point division by zero and a division of
literals by the literal zero do is T25.

Case: [types/float-remainder-by-zero.kz](../corpus/types/float-remainder-by-zero.kz)
```kurz
double Rest(double a, double b) => a % b

r = Rest(1.0, 0.0)
print(r != r)
```
Case: [types/index-out-of-range.kz](../corpus/types/index-out-of-range.kz)
```kurz
xs = List<int>()
print(xs[0])
```

Case: [types/divide-by-zero.kz](../corpus/types/divide-by-zero.kz)
```kurz
int Divide(int a, int b) => a / b

print(Divide(10, 2))
print(Divide(10, 0))
```

### T25 (decided, §4) Division by zero, in detail

An integer `%` by zero raises `divide-by-zero`, as `/` by zero does: both are the same instruction of
the machine, and C# throws the same exception for both; `decimal` and the floating-point types follow
below. A floating-point division by zero does not
raise: it yields an infinity, or NaN for `0.0 / 0.0`, as IEEE 754 and C# have it, and a
floating-point `%` by zero yields NaN, as there; `decimal` raises as the integers do, as C# throws
for it (the owner, 2026-10-10). A division or a remainder
of literals by the literal `0`, `10 / 0`, which T6 would otherwise fold at compile time, is the compile
error `constant-divide-by-zero`, because that division can never succeed. The owner
confirmed this reading on 2026-10-10, in round 14; round 13 had not asked it. On the same day the owner confirmed the reach of the error, which reads
that sentence for the integer types and `decimal`: it is for a divisor that is a constant zero whatever the dividend, `x / 0` with a run-time
`x` and `1 / (1 - 1)` with a folded divisor included, as C# reports a constant divisor of zero; it is
for the integer types and `decimal`, as there, so `1m / 0m` is the error too; and a floating-point
division by a constant zero, `1.0 / 0`, is no error and yields the infinity or NaN above, as there
(the owner, 2026-10-10, confirming the reading proposed after the review of round 13).

Case: [types/float-remainder-by-zero.kz](../corpus/types/float-remainder-by-zero.kz)

Case: [types/decimal-divide-by-zero.kz](../corpus/types/decimal-divide-by-zero.kz)
```kurz
decimal Divide(decimal a, decimal b) => a / b

print(Divide(1m, 0m))
```

Case: [types/constant-zero-divisor.kz](../corpus/types/constant-zero-divisor.kz)
```kurz
int x = 5
print(x / 0)
// a constant zero divisor is the error whatever the dividend (T25)
```

Case: [types/constant-divide-by-zero.kz](../corpus/types/constant-divide-by-zero.kz)
```kurz
print(10 / 0)
```

Case: [types/remainder-by-zero.kz](../corpus/types/remainder-by-zero.kz)
```kurz
int Rest(int a, int b) => a % b

print(Rest(10, 3))
print(Rest(10, 0))
```

### T15 (decided, §4) Strings

A string is UTF-8 and immutable. It is indexed through `.Bytes` or `.Chars`; indexing the string
itself is the compile error `string-index`. No flag changes the encoding: text is converted at
the edges. A string holds at most as many bytes as the largest `int`, so that `s.Bytes.Count` is an
`int` that holds every count; an operation that would make a longer string, a `+`, an
interpolation or `string.FromRaw(p, n)` (R15), raises `allocation-failed` with the count it asked
for, as C#'s `new string('a', int.MaxValue)` throws `OutOfMemoryException` (§4, the owner,
2026-10-23 and 2026-10-24). No case shows that sentence: a case would build a string of 2 GiB, and the error line
of `allocation-failed` names no line a header could expect.

Case: [types/string-bytes.kz](../corpus/types/string-bytes.kz)
```kurz
s = "aä"
print(s.Bytes.Count)
print(s.Bytes[0])
```

Case: [types/string-index.kz](../corpus/types/string-index.kz)
```kurz
s = "abc"
print(s[0])
```

Case: [types/string-chars.kz](../corpus/types/string-chars.kz)
```kurz
s = "aä😀"
print(s.Chars.Count)
print(s.Bytes.Count)
```

### T16 (decided, §4) What `.Chars` yields

`.Chars` yields the Unicode code points of the string, one element for each code point. It
yields neither UTF-16 units, as a C# string does, nor what a reader calls a character: grapheme
clusters are left to a library.

Case: [types/string-chars.kz](../corpus/types/string-chars.kz)

### T21 (decided, §4) `char`

The element type of `.Chars` is `char`: one Unicode code point, in 32 bits. It is not the `char`
of C#, which is one UTF-16 unit and cannot hold every code point. The string stays UTF-8
whatever its `.Chars` are used for: they are decoded one at a time while the string is walked,
and no second copy of the text is built. Four bytes are used only where a `char` is stored.

A `char` literal is one code point between single quotes, `'a'`, with the escapes of L7 and L16;
more than one code point between the quotes is the compile error `syntax` (E4) (the owner,
2026-10-09, against no literal, under which every comparison of a character goes through `.Chars` or
`char(97)`; the cost is a second quote in the lexer). Anything but exactly one code point between the quotes is
`syntax`, the empty `''` included, as C# refuses it; the quote itself is written `\'`, as in C#, and a
bare `'` between the quotes ends the literal, so `'''` is an empty literal followed by a quote that
opens one that never closes, and `syntax` (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A `{` between the quotes is the character and needs no `\{` (the owner, 2026-10-10). A line break
between the quotes is `syntax` too, since a literal of one line cannot hold one, as in C#; a tab and a
bare `"` between them are one code point and need no escape (the owner, 2026-10-11, confirming the reading proposed after the review of round 14). The case of
A8 walks the `.Chars` of a string and prints each one.

Case: [types/char-brace.kz](../corpus/types/char-brace.kz)
```kurz
c = '{'
print(c)
// a `{` between the quotes is the character and needs no escape (T21)
```

Case: [types/char-literal-triple.kz](../corpus/types/char-literal-triple.kz)
```kurz
c = '''
print(c)
```

Case: [types/char-literal-empty.kz](../corpus/types/char-literal-empty.kz)
```kurz
c = ''
print(c)
```

Case: [types/char-quote-escape.kz](../corpus/types/char-quote-escape.kz)
```kurz
c = '\''
print(c)
```

Case: [values/text-more.kz](../corpus/values/text-more.kz)
```kurz
flags Access { Read, Write, Run }

print(1.0)
print(0.5f)
print(1.50m)
print(90min)
print(Access.Read | Access.Write)
for c in "ab".Chars {
    print(c)
}
```

Case: [types/char-literal.kz](../corpus/types/char-literal.kz)
```kurz
c = 'a'
print(c)
print(c == 'a')
d = 'b'
print(d == c)
print(d == 'b')
```

Case: [types/char-literal-two.kz](../corpus/types/char-literal-two.kz)
```kurz
c = 'ab'
print(c)
```

### T17 (decided, §4) Generics

Generic types are written as in C#: `List<int>`, `Map<int, User>`.

Case: [values/list.kz](../corpus/values/list.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
print(xs.Count)
print(xs[1])
```

### T19 (decided, §4) Declaring generic functions and types

A generic function or type lists its type parameters between `<` and `>` after its name. A type
parameter is limited to the types that implement an interface by naming the interface after a
colon: `T Max<T: Comparable>(T a, T b)`. `where` keeps its one meaning, the constraint on a value
(record, section 13). A use names the type arguments, or leaves them to be inferred (T27). An
argument whose type does not meet the limit is the compile error `type-mismatch`.

Case: [types/generic-function.kz](../corpus/types/generic-function.kz)
```kurz
T First<T>(List<T> items) => items[0]

mut xs = List<int>()
xs.Add(4)
mut names = List<string>()
names.Add("ok")
print(First(xs))
print(First(names))
```

Case: [types/generic-type.kz](../corpus/types/generic-type.kz)
```kurz
data Pair<A, B>(A First, B Second)

p = Pair<int, string>(1, "one")
print(p.First)
print(p.Second)
```

Case: [types/generic-limit.kz](../corpus/types/generic-limit.kz)
```kurz
interface Shape {
    int Area()
}

class Square(int Side) : Shape {
    pub int Area() => Side * Side
}

int AreaOf<T: Shape>(T item) => item.Area()

print(AreaOf(Square(3)))
```

Case: [types/generic-limit-unmet.kz](../corpus/types/generic-limit-unmet.kz)
```kurz
interface Shape {
    int Area()
}

int AreaOf<T: Shape>(T item) => item.Area()

print(AreaOf(5))
```

### T27 (decided, §4) How type arguments are inferred

A type argument that a call does not write is inferred from the arguments: a type parameter takes
the one type that every argument in its positions has, after the conversions of this chapter (a
literal takes the type the other arguments in its positions fix, below). When the arguments in its
positions have different types, or no argument stands in one of its positions, the call is the
compile error `cannot-infer`, and the type arguments are written. This is C#'s inference in Kurz's
terms: C# would widen a `byte` to an `int` to make `Max(b, i)` an `int`, which T8 and T9 do not
allow here, and C#'s inference has changed between its versions, so no version of it is named. The
owner confirmed this reading on 2026-10-10, in round 14; round 13 had not asked it. A literal argument takes the type the other arguments in the
parameter's positions fix, so `Pick(small, 1)` with a `byte` infers `byte`; two instances of
different classes with one base are `cannot-infer`, since no common base is searched for (the owner,
2026-10-10). When literals alone stand in a parameter's positions they take the type a constant
expression of them has (L10, L17), so `Pick(1, 2)` infers `int` and `Pick(1, 2.5)` `double`; a literal
that does not fit the fixed type, `Pick(small, 300)` with a `byte`, is `constant-overflow`, as a literal
that does not fit the type it takes is wherever it stands (L17); and where one argument's class is the
base of the other's, `Pick(Dog(), Animal())`, the parameter takes the base, which every argument has
(K6) (the owner, 2026-10-11, confirming the reading proposed after the review of round 14).

Case: [types/generic-cannot-infer-classes.kz](../corpus/types/generic-cannot-infer-classes.kz)
```kurz
class Animal {
    pub string Text() => "animal"
}

class Dog : Animal {
    pub int Legs() => 4
}

class Cat : Animal {
    pub int Lives() => 9
}

T Pick<T>(T a, T b) => a

print(Pick(Dog(), Cat()))
```

Case: [types/generic-literal-argument.kz](../corpus/types/generic-literal-argument.kz)
```kurz
T Pick<T>(T a, T b) => a

byte small = 7
byte r = Pick(small, 1)
print(r)
```

Case: [types/generic-cannot-infer.kz](../corpus/types/generic-cannot-infer.kz)
```kurz
T Pick<T>(T a, T b) => a

byte small = 1
int i = 2
print(Pick(small, i))
```

### T18 (assumed, §4) Operators

`+ - * / %` on numbers, `== != < <= > >=` for comparison, and `&& || !` on `bool`, with the
meaning and the precedence they have in C#. Integer division drops the fraction, toward zero. An
operand narrower than `int` is promoted to `int` first, as C# does (T28).

Case: [types/arithmetic.kz](../corpus/types/arithmetic.kz)
```kurz
print(1 + 2 * 3)
print(7 / 2)
print(-7 / 2)
print(7 % 3)
print(1 < 2 && 2 < 3)
print(!(1 == 1) || 3 >= 3)
```

### T28 (decided, §4) Arithmetic and shifts on types narrower than `int`

An operand of type `sbyte`, `byte`, `short` or `ushort` is promoted to `int` before `+ - * / %`,
unary `-`, `~`, `& | ^`, `<<`, `>>` and the comparisons, as in C#: `a + b` on two `byte`s is an
`int`, `print(a + b)` prints `300` for 200 and 100, `a & b` is an `int`, and `byte c = a + b` is
`narrowing-conversion` (T7). A shift on such an operand has the width 32, and a negative count is
masked as in C#, so `1 << -1` is `1 << 31`. Where the other operand is a `uint`, a `long` or a
`ulong`, the narrow operand takes that type instead of `int`, as C#'s binary numeric promotion does
(T9). The owner chose this on 2026-10-03, against computing in the operands' type, under which a
program ported from C# computes other values without a word; the cost is that `byte c = a + b` needs
`byte(a + b)`, and that the wrap of T4 never happens at 8 or 16 bits, while `ushort * ushort` is
computed in a signed `int` and can overflow it. The wrapping operators `+%`, `-%` and `*%` (T12) do
not promote: they compute in the wider of their operand types and wrap there, so that an 8-bit
checksum is `a +% b` on two `byte`s, where a promoted `+%` would need a mask on every narrow
checksum (the owner, 2026-10-04, against promoting them; C# has no such operators). The cost: `a +
b` and `a +% b` differ in type. Under these operators, which do not promote, operands of different
signedness are `sign-mix` (T9) whatever their widths, and a literal operand takes the other
operand's type, so that `sum +% 1` on a `byte` computes in `byte` (the owner, 2026-10-09). A literal
that does not fit that type, `sum +% 300` on a `byte`, is `constant-overflow` (T6), as a literal that
does not fit the type it takes is wherever it stands (L17) (the owner, 2026-10-10, confirming the reading proposed after the review of round 13).

Case: [types/wrap-literal-too-big.kz](../corpus/types/wrap-literal-too-big.kz)
```kurz
byte sum = 1
print(sum +% 300)
```

Case: [types/wrapping-narrow.kz](../corpus/types/wrapping-narrow.kz)
```kurz
byte a = 200
byte b = 100
ushort c = 65500
print(a +% b)
print(c +% a)
```

Case: [types/promotion.kz](../corpus/types/promotion.kz)
```kurz
byte a = 200
byte b = 100
print(a + b)
print(a << 1)
print(1 << 32)
print(1 << -1)
```

Case: [types/promotion-narrows.kz](../corpus/types/promotion-narrows.kz)
```kurz
byte a = 200
byte b = 100
byte c = a + b
print(c)
```

### T22 (decided, §4) Operators on bits

`&`, `|`, `^`, `~`, `<<` and `>>` work on the bits of integers, with the meaning and the
precedence they have in C#. `>>` on a signed integer keeps the sign. A shift is not arithmetic
in the sense of T4 to T6: bits that leave the type are dropped in every build, and the count of
a shift is taken modulo the width of the type, so `1 << 33` on an `int` is `1 << 1`. `|` also
separates the cases of a union type (D9). The two meanings never meet: one stands between types,
the other between values. An operand narrower than `int` is promoted to `int` first, so its
width is 32, and a negative count is masked (T28).

Case: [types/bit-operators.kz](../corpus/types/bit-operators.kz)
```kurz
print(6 & 3)
print(6 | 3)
print(6 ^ 3)
print(~6)
print(1 << 4)
print(-16 >> 2)
print(1 << 31)
print(1 << 33)
```

Case: [types/promotion.kz](../corpus/types/promotion.kz)
