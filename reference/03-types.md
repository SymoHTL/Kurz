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

Case: [values/list-literal-item-mismatch.kz](../corpus/values/list-literal-item-mismatch.kz)
```kurz
List<int> xs = [
    1, "a",
]
print(xs.Count)
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

Integer arithmetic that leaves the range of its type wraps around silently. A project keeps T5's
checks in its release build by a setting in its `project.kz` (§4, the owner, 2026-10-30, "Project
setting") *(assumed, §4: the name `overflow-checks`, and the whole program, its packages
among it)*; a case of the corpus has no `project.kz` and takes the default.

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

Case: [values/list-literal-item-fixes.kz](../corpus/values/list-literal-item-fixes.kz)
```kurz
List<T> Pair<T>(T a, List<T> b) => b

x = 5
r = Pair(1L, [x])
print(r)
```

Case: [classes/static-narrowing.kz](../corpus/classes/static-narrowing.kz)
```kurz
class Level {
    pub static byte Small = Wide
    pub static int Wide = 7
}

print(Level.Small)
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

A constant that a copy passes on to a type parameter holding a constant of a wider integer type widens the same way
(§4, the owner, 2026-11-03). *(assumed, §4: one passed on to a narrower type is `type-mismatch` (T1), as T35 reads a
value of a type that does not widen where a constant stands)*

Case: [types/const-parameter-widening.kz](../corpus/types/const-parameter-widening.kz)
```kurz
void Wide<const long W>() {
    print(W + 1)
}

void Pass<const int L>() {
    Wide<L>()
}

Pass<7>()
```

Case: [types/const-parameter-narrowing.kz](../corpus/types/const-parameter-narrowing.kz)
```kurz
void Narrow<const int N>() {
    print(N)
}

void Pass<const long L>() {
    Narrow<L>()
}

Pass<7>()
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
checked on 2026-10-28 with rust_decimal 1.36) *(assumed: the rounding half to even, the largest scale
whose digits fit and the zeros removed at the end, readings of the option's "rounded to 28
digits")*; that rounding is no loss. A `float` becomes the `double` that holds it exactly, and then
that *(assumed: the answer named the exact binary value, which the `double` holds; C# keeps 7
significant digits of a `float`)*. A zero that comes of the conversion has no sign, `decimal(-0.0)`
included *(assumed: the reading of the answer "None, as rust_decimal", under which only the unary
`-` gives a zero its sign)*. A `decimal` becomes a `double` as C# computes it: the low 64 bits of its
96-bit integer as a `double`, plus its high 32 bits times 2^64 as a `double`, divided by the
`double` nearest to 10 to the power of its scale, as .NET's `VarR8FromDec` computes it, so
`double(1m / 3m)` is `0.33333333333333337` (the owner, 2026-10-28, "As C# computes it", against the
nearest `double`, `0.3333333333333333`; the cost is a last bit that can differ from the nearest
`double`), and a `float` as that `double` rounded, as C# computes it *(assumed)*.

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
the scale of their result as System.Decimal does, but for the remainders below, so `1.50m * 2` is
`3.00`, `1m / 3m` is `0.3333333333333333333333333333` and `5m / 2m` is `2.5`; `%` takes the sign of
the dividend, but for a zero, which has none (below); `/` and `%` by a zero, whatever its scale and
its sign, raise `divide-by-zero` in every build (T25); `==`
and the comparisons compare the values, so `1.5m == 1.50m` *(assumed: T2 names C#'s type; Rust has
no `decimal`, and its crate rust_decimal chooses other scales, `2.50m * 0` is `0` there where
System.Decimal keeps `0.00`, so the two do not agree, and Kurz takes C#'s)*. A result beyond
±79228162514264337593543950335 raises `overflow` (E3) in every build, a release build too, as C#
throws for it (the owner, 2026-10-28, "Raise in every build", against raising in a test build and
saturating in a release build; the cost is nothing at run time beyond what the rounding checks
anyway, and a release program ends with an error line instead of a wrong amount). A zero that `+`,
the binary `-`, `*`, `/` and `%` compute has no sign: `1.50m - 1.5m` shows `0.00` and `0m * -1m`
shows `0`; only the unary `-`, which turns the sign, gives a zero its sign, as `-(0.0m)` shows
`-0.0` (A8), and the digits and the scale stay System.Decimal's,
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

print(Times(1.50m, 2m))
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

Case: [values/text-timestamp.kz](../corpus/values/text-timestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        print(*Ptr<timestamp>(&bits))
    }
}

Show(0)
Show(1793179800000000000)
Show(1793179801500000000)
Show(1793179801000250000)
Show(-1)
```

Case: [values/text-longtimestamp.kz](../corpus/values/text-longtimestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        longtimestamp epoch = *Ptr<timestamp>(&bits)
        print(epoch + 2932897days)
        print(epoch - 719528days)
        print(epoch - 719528days - 1s)
    }
}

Show(0)
```

### T29 (decided, §4) The wider pair: `longduration` and `longtimestamp`

Beside the 64-bit pair of T24 stand `longduration` and `longtimestamp`: 128 bits with the same step
of one nanosecond, for a calendar or an archive that reaches before 1677 or after 2262. A `duration`
widens into a `longduration` and a `timestamp` into a `longtimestamp` without a word (T8), a
conversion the other way is written out and raises `overflow` in every build where the value does
not fit it (T31), and the texts are A12's and A16's. What T13
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

A `longduration` and a `longtimestamp` take 16 bytes aligned to 16, as Rust's `i128` from rustc 1.77
on, C's `__int128` on x86-64 and .NET's `Int128` on Linux, which keeps the 8 of a `long` on Windows,
and `SizeOf` gives 16 for them and 8 for the narrow pair (R14) (§4, the owner, 2026-10-28, "16
bytes, 16-aligned", against the 8 of a `decimal`, under which a data value that holds one is laid
out otherwise than C lays out a structure with an `__int128`; the cost is up to 8 bytes of padding
in a data value that holds one).

Case: [types/time-size.kz](../corpus/types/time-size.kz)
```kurz
print(SizeOf<duration>())
print(SizeOf<timestamp>())
print(SizeOf<longduration>())
print(SizeOf<longtimestamp>())
```

A `longduration` is narrowed to a `duration` only by a written conversion, which raises `overflow`
where the value does not fit, in a release build as well (T31): in the cases below the sum is two
hundred thousand days, beyond the narrow range, so `duration(l)` raises it in a test build and in a
release build.

Case: [types/longduration-wide.kz](../corpus/types/longduration-wide.kz)
```kurz
longduration a = 100000days
longduration l = a + a
print(duration(l))
```

Case: [types/longduration-wide-release.kz](../corpus/types/longduration-wide-release.kz)
```kurz
longduration a = 100000days
longduration l = a + a
print(duration(l))
```

A wide value shows as A12 says, its days beyond the narrow range as well.

Case: [values/text-longduration.kz](../corpus/values/text-longduration.kz)
```kurz
longduration Plus(longduration a, longduration b) => a + b

print(200000days)
print(Plus(200000days, 1ns))
print(-200000days)
```

Where a union is required, its one time case reaches a duration literal as a written type does (T8).

Case: [types/literal-one-time-case.kz](../corpus/types/literal-one-time-case.kz)

Case: [values/text-timestamp.kz](../corpus/values/text-timestamp.kz)

Case: [values/text-longtimestamp.kz](../corpus/values/text-longtimestamp.kz)

### T31 (decided, §4) Time arithmetic that leaves the range

`+` and `-` on durations and timestamps, of either pair, and the unary `-` on a duration of either
pair raise `overflow` (E3) where the result leaves the range of its type, in every build, a release
build too, as Rust's `Duration` and `SystemTime` panic and C#'s `TimeSpan` throws in a build without
checks; a written conversion from the wide pair into the narrow one raises it where the value does
not fit (T29), in every build as well (§4, the owner, 2026-10-28, "Raise in every build", asked for
the operations and for the conversion, against the rule of integer arithmetic, T4 and T5, under
which a release build wraps, and against T20's wrap in a release build for the conversion; the cost
is a check and a branch on every such operation in a release build). A constant expression that
leaves the range of the type its literals take (T29) is `constant-overflow` (T6).

Case: [types/duration-overflow-release.kz](../corpus/types/duration-overflow-release.kz)
```kurz
duration Add(duration a, duration b) => a + b

print(Add(100000days, 10000days))
```

Case: [types/duration-negate-release.kz](../corpus/types/duration-negate-release.kz)
```kurz
duration Negate(duration d) => -d

print(Negate(-9223372036854775808ns)) // one constant: the parameter's type takes the literal with its sign (L17)
```

### T36 (decided, §12) `instant`, a reading of the monotonic clock

`instant` is the type of a reading of the monotonic clock, which `instant.Now` gives (S25), and a
core word (L19) (§12, the owner, 2026-11-04, issue 110, comment 6643368937, the monotonic clock, Q1
(a)). It is a time type with rules of its own, and this rule and S25 state every form a reading
takes: a rule that names the time types reaches a reading only where one of them names it, so that
T29's widening and its conversions and T31's unary `-` take none *(assumed, §12: a reading has no
wide variant, a default of that round, below, and the round gives a negated reading no meaning)*.
`b - a` of two readings is a `duration`, negative where `a` is the later (§12, the same answer, Q2
(a)), and it raises `overflow` (E3) where the difference leaves the range of a `duration`, in every
build, as T31 says of the time types (§12, the same answer, "the check of section 4 on the
subtraction"). A reading plus or less a `duration` is an `instant`, and `+` and `-` raise `overflow`
where the result leaves the range, in every build, as T31 says of the other time types, so that a
bound is checked by what has passed, `instant.Now - start >= limit`, and not by the sum
`start + limit` (§12, the same answer, Q3 (a)). *(assumed, §12: the defaults of that round, not
objected to: a reading holds 64 bits of nanoseconds, signed, counted from the clock's zero, and has
no wide variant; `<`, `<=`, `>`, `>=`, `==` and `!=` compare two readings, inside a `data` value and
as a map's key too; and a reading converts to no `timestamp` and no integer, and from neither.)*

Of the operators, a reading takes these and no other: `-` with a reading, `+` and `-` with a
`duration` on its right, `+` with a `duration` on its left, and the six comparisons with a reading.
Every other operator and operand is `type-mismatch` (T18): two readings added, a `duration` less a
reading, `*`, `/` and `%` on a reading, a reading beside a `timestamp`, a `longduration`, a
`longtimestamp` or an integer, the unary `-` of a reading, and a reading where an integer or a
`timestamp` is required. A comparison of a reading with `null` is `type-mismatch` (N6), and a
conversion of one to `string` is `not supported` (T10), as for every time type. *(assumed, §12, each
from the forms of the other time types: a `duration` plus a reading is a reading, as `+` takes a
`timestamp` and a `duration` in either order, where C#'s `DateTime` and Rust's `Instant` take the
`duration` on the right alone; two readings added and a `duration` less a reading are refused, since
a sum of two points in time and a span less a point mean nothing, and C# and Rust take neither; a
reading beside the wide pair is refused, since a reading has no wide variant to widen into; and the
unary `-` of a reading is refused, since the round gives a negated reading no meaning.)*

A conversion written into `instant` from another type is `type-mismatch`, as one from a `duration`
into a `timestamp` is (T13) *(assumed, §12: a reading that no conversion from another type makes)*,
and `instant(t)` of a reading is that reading, as every time type converts into itself *(assumed,
§12: from the forms of the other time types)*. A `raw` block reads a reading's bits through a
pointer as it reads any value's (R10), and the bits are the nanoseconds since the clock's zero
*(assumed, §12: the 64 bits of nanoseconds of that round's default)*.

Case: [types/instant-difference.kz](../corpus/types/instant-difference.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

instant a = Reading(5000000000)
instant b = Reading(5405000000000)
print(b - a == 90min)
print(a - b == -90min)
print(a - a == 0s)
```

Case: [types/instant-sum.kz](../corpus/types/instant-sum.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

instant a = Reading(5000000000)
print(a + 90min == Reading(5405000000000))
print(a - 5s == Reading(0))
print(90min + a == Reading(5405000000000))
```

Case: [types/instant-comparison.kz](../corpus/types/instant-comparison.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

instant a = Reading(1)
instant b = Reading(2)
instant c = Reading(2)
print(a < b)
print(b <= c)
print(b > a)
print(a >= b)
print(b == c)
print(a != c)
```

Case: [types/instant-in-data.kz](../corpus/types/instant-in-data.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

data Mark(instant At)

print(Mark(Reading(7)) == Mark(Reading(7)))
print(Mark(Reading(7)) == Mark(Reading(8)))
```

Case: [types/instant-map-key.kz](../corpus/types/instant-map-key.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

mut seen = Map<instant, string>()
seen[Reading(7)] = "first"
seen[Reading(7)] = "again"
seen[Reading(8)] = "other"
print(seen.Count)
```

Case: [types/instant-identity.kz](../corpus/types/instant-identity.kz)
```kurz
instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

instant a = Reading(42)
print(instant(a) == a)
```

Case: [types/instant-difference-overflow.kz](../corpus/types/instant-difference-overflow.kz)
```kurz
duration Between(instant a, instant b) => b - a

long low = -9223372036854775807 - 1
long high = 9223372036854775807
raw {
    instant first = *Ptr<instant>(&low)
    instant last = *Ptr<instant>(&high)
    print(Between(first, last))
}
```

Case: [types/instant-overflow-release.kz](../corpus/types/instant-overflow-release.kz)
```kurz
instant Later(instant t, duration d) => t + d

long bits = 9223372036854775807
raw {
    instant last = *Ptr<instant>(&bits)
    print(Later(last, 1ns))
}
```

Case: [types/instant-overflow-lower.kz](../corpus/types/instant-overflow-lower.kz)
```kurz
instant Earlier(instant t, duration d) => t - d

long bits = -9223372036854775807 - 1
raw {
    instant first = *Ptr<instant>(&bits)
    print(Earlier(first, 1ns))
}
```

Case: [types/instant-not-integer.kz](../corpus/types/instant-not-integer.kz)
```kurz
int Count(instant t) => t
```

Case: [types/instant-not-timestamp.kz](../corpus/types/instant-not-timestamp.kz)
```kurz
timestamp At(instant t) => t
```

Case: [types/instant-plus-instant.kz](../corpus/types/instant-plus-instant.kz)
```kurz
instant Twice(instant t) => t + t
```

Case: [types/instant-duration-less-reading.kz](../corpus/types/instant-duration-less-reading.kz)
```kurz
duration Back(instant t) => 1s - t
```

Case: [types/instant-times-integer.kz](../corpus/types/instant-times-integer.kz)
```kurz
instant Twice(instant t) => t * 2
```

Case: [types/instant-conversion.kz](../corpus/types/instant-conversion.kz)
```kurz
print(instant(5s))
```

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

`s.Bytes` is a `Bytes` (M9), a value of its own (M3): the bytes of a string made while the program
runs are shared with it, and those of a literal copied *(assumed: as §4 marks it)*.
`string.FromUtf8(b)` makes a string of the bytes of `b`, and its return type is
`string | InvalidUtf8` (O1), where `data InvalidUtf8(int At)` holds in `At` the count of the bytes in
front of the first sequence that is not UTF-8, as Rust's `valid_up_to` counts them;
`string.FromUtf8Lossy(b)` makes a string in which each such sequence reads U+FFFD; and where the
bytes have a block, the string shares it (§4, the owner, 2026-10-29). The lossy form writes one
U+FFFD for each maximal part of a sequence, as Rust counts them, and `InvalidUtf8` is a `data` type
every file sees, declared before the file's own types, so that a file's own `data InvalidUtf8` is
`redeclared` (V9), as a file's own `data Invalid` is (D18) *(assumed: as §4 marks them)*.

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

Case: [types/string-bytes-value.kz](../corpus/types/string-bytes-value.kz)
```kurz
s = "hé"
print(s.Bytes)
t = "{s}!"
mut b = t.Bytes
print(b)
b.Add(33)
print(b.Count)
print(t)
```

Case: [types/string-from-utf8.kz](../corpus/types/string-from-utf8.kz)
```kurz
mut b = Bytes()
b.Add(104)
b.Add(105)
match string.FromUtf8(b) {
    string s => print(s)
    InvalidUtf8 e => print(e.At)
}
b.Add(195)
b.Add(40)
b.Add(33)
match string.FromUtf8(b) {
    string s => print(s)
    InvalidUtf8 e => print(e.At)
}
print(string.FromUtf8Lossy(b))
```

Case: [types/invalid-utf8-redeclared.kz](../corpus/types/invalid-utf8-redeclared.kz)
```kurz
data InvalidUtf8(int At)

print(InvalidUtf8(1))
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

### T32 (decided, §4) Comparing strings

`==` and `<` on two strings compare them by their code points, ordinal, as Rust compares two `str`
values; in UTF-8 that is the order of their bytes. A change of case and the reading of a number from
a string take no culture, and a sort for a reader's culture is a library's, which takes the culture
as an argument (§4, the owner, 2026-10-30, against the culture of the machine, as C#'s
`string.Compare` takes it, and against no `<` on strings). *(assumed, §4: that `!=`, `<=`, `>` and
`>=` follow `==` and `<`)*

Case: [types/string-order.kz](../corpus/types/string-order.kz)
```kurz
void Show(string a, string b) {
    print("{a < b} {a <= b} {a > b} {a >= b}")
}

Show("", "a")
Show("a", "ab")
Show("ab", "b")
Show("b", "b")
Show("é", "z")
Show("😀", "\uFFFF")
Show("a\0", "a")
```

Case: [types/string-order-literals.kz](../corpus/types/string-order-literals.kz)
```kurz
print("\uFFFF" < "😀")
print("z" < "é")
print("b" > "b")
```

### T17 (decided, §4) Generics

Generic types are written as in C#: `List<int>`, `Map<int, User>`. `void` is no type argument:
`List<void>`, `Map<string, void>` and `Other<void>()` are the compile error `void-type-argument`
where `void` is written, as C# refuses it (CS1547), while the result of a function type inside a
type argument, `List<(int) => void>`, stands (§4, the owner, 2026-11-02, "refused, as CS1547").
*(assumed, §4: the id, named for what it refuses)*

Case: [values/list.kz](../corpus/values/list.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
print(xs.Count)
print(xs[1])
```

Case: [types/void-type-argument.kz](../corpus/types/void-type-argument.kz)
```kurz
fs = List<(int) => void>()
names = Map<string, void>()
print(fs.Count + names.Count)
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
that does not fit the type it takes is wherever it stands (L17); the items of a list literal of literals
given to a parameter `List<T>` count as literal arguments of `T`, so `Pair(1L, [1])` with
`Pair<T>(T a, List<T> b)` infers `long` (M11), while `[]` gives `T` nothing, so `Take([])` with
`Take<T>(List<T> xs)`, where no other argument gives `T`, is `cannot-infer` *(assumed, §4: the items
read as arguments of the parameter, of which `[]` has none)*; and where one argument's class is the
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

Case: [values/list-literal-cannot-infer.kz](../corpus/values/list-literal-cannot-infer.kz)
```kurz
x = []
print(x.Count)
```
Case: [values/list-literal-mixed-items.kz](../corpus/values/list-literal-mixed-items.kz)
```kurz
x = [1, "a"]
print(x.Count)
```

Case: [values/list-literal-inference.kz](../corpus/values/list-literal-inference.kz)
```kurz
List<T> Pair<T>(T a, List<T> b) => b
List<T> Both<T>(List<T> a, List<T> b) => b

r1 = Pair(1L, [1])
List<long> y1 = r1
print(y1)
r2 = Pair(1, [2L])
List<long> y2 = r2
print(y2)
r3 = Both([1], [2L])
List<long> y3 = r3
print(y3)
r4 = Pair(1, [2, 3])
List<int> y4 = r4
print(y4)
List<long> xs = [1]
r5 = Both(xs, [7])
List<long> y5 = r5
print(y5)
byte small = 1
r6 = Pair(small, [2])
List<byte> y6 = r6
print(y6)
r7 = Pair(1L, [])
List<long> y7 = r7
print(y7)
```

Case: [values/list-literal-empty-argument.kz](../corpus/values/list-literal-empty-argument.kz)
```kurz
int Take<T>(List<T> xs) => xs.Count
print(Take([]))
```

Case: [values/list-literal-item-fixes.kz](../corpus/values/list-literal-item-fixes.kz)

Case: [outcomes/keep-argument-generic-union.kz](../corpus/outcomes/keep-argument-generic-union.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report<T>(T | NotFound found) {
    match found {
        NotFound => print("none")
        else => print("found")
    }
}

// `T | NotFound` names a case of the call beyond its success, so it keeps the cases, and `T` is `int`
Report(Find(1))
Report(Find(2))
```

### T33 (decided, §4) Reading a type in generic code

A generic function (T19) reads the type each of its copies is made for, while the program compiles,
through members of a type parameter, as Zig's `@typeInfo` reads a type. `T.Kind` is the kind of the
type: a number type, `string`, `bool`, a list, a map, a nullable form, a `data` type, an `enum` or a
union, and a `match` on it tells the kinds apart. A loop over `T.Fields` walks a `data` type's
fields in the order of their declaration, unrolled in each copy: `f.Name` is a constant `string`,
`f.Type` stands as a type argument, and `f.Get(value)` is the field's value, typed as the field.
`T.Make(f => ...)` makes a value of a `data` type: it is unrolled into one call of the type's
primary constructor, with one argument for each field in order, so that its constraints are checked
as at any construction. Passing a type argument grants its reading, whatever package declared the
type. `T.Fields` holds the primary constructor's fields (D14), then the `pub` fields the body
declares (F9, D16), in the order of their declaration, and never a field the body keeps private;
`T.Make` sets the primary constructor's fields alone (§4, the owner, 2026-11-01, "Kinds + parts",
"for f in T.Fields", "T.Make(f => ...)", "Type arg grants read" and "Readable ones"). In a copy whose
type has constraints (record, section 13) `T.Make` gives `T | Invalid`, and `T` in any other copy;
a generic function lists every outcome that the copies its calls make can give, and a copy that gives one its
function does not list is `unlisted-case` at the call that made the copy (§4, the owner,
2026-11-02, "a generic function lists every outcome any of its copies can give", an option that named the id and its
place, which the answer of 2026-11-03 narrows to the copies calls make); each copy is checked at the call that made it,
as each copy is checked alone, so that
a function whose calls make no copy that gives `Invalid` need not list it, `Remake` below among them, while that
call can be a caller's in another package than the function (§4, the owner, 2026-11-03, "checked at each copy a call
makes"). A variable, a parameter, a loop's variable, a binding of a `match` arm or a lambda's parameter named like a
type parameter of the function around it, one that holds a constant among them (T35), is `redeclared` (V9), as C#'s
CS0412 refuses a parameter, a local variable and a `foreach` variable named like a method's type parameter, and as C#
refused, up to its language version 7.3, the three forms its later versions let hide the type parameter, a lambda's
parameter, a variable inside a lambda and a local function's parameter (§4, the owner, 2026-11-03, "redeclared, as C#'s
CS0412", and 2026-11-04 for a constant, "the same reading for both"): a parameter of the function, part of its
signature, from its text in every case, where no call reaches the function too (§4, the owner, 2026-11-07, "Refused
always"), and each other such declaration where the body is checked (below): once from its text, or in each copy a call
makes for a function checked in its copies alone, so not at all where no call reaches such a function. *(assumed, §4,
with the build: each is reported once, at the declaration, however many copies give it (below))* *(assumed, §4: `T.Kind` is a value of `enum TypeKind`, a name every file sees, so that a
file's own `TypeKind` is `redeclared` (V9); it has a value for each built-in type, named as the
type, `TypeKind.Int` and `TypeKind.Char` among them, and one for each other kind, `TypeKind.Data`
and `TypeKind.List` among them, a file, a socket and a hasher being of the kind `TypeKind.Class`; an arm of a `match`
on `T.Kind` is checked only in the copies whose kind it names; `Kind`, `Fields` and `Make` are
members of a type parameter alone; `f.Get(value)` reads as `value.Field`; `f` stands in `f.Name`, `f.Type` and
`f.Get(value)` alone, and anywhere else is `syntax`; `f.Get` takes a value of `T` or of a type derived from it, and one of
another type is `type-mismatch`)* A `match` on `T.Kind`
is not held to name every kind or end in an `else` arm, unlike one on a union or an `enum` (O8,
D12): it is checked in each copy, and where it has no arm for the kind of a copy's type argument,
the call that made the copy is the compile error `kind-not-read`, which names the generic function,
the type, its kind and the path of fields that led to it (§4, the owner, 2026-11-01, "Fixed compile
error"). Only a `match` with `T.Kind` as its subject narrows: an `if` on `T.Kind` narrows nothing, so
that `T.Fields` in its branch, in a copy whose type is not a `data` type, is that error too (§4,
the owner, 2026-11-02, "only a `match` on `T.Kind` narrows"). *(assumed, §4: the id; that `T.Fields` and `T.Make` in a copy whose type is not a `data` type are the
same error)* *(assumed, §4: `T.Make` takes one lambda of one parameter with no type, and any other
argument is `syntax`; in the lambda's body `f` stands as in a loop over `T.Fields`; a type with no
fields gives its value, and a variable the body names counts as read in a copy of such a type, as in an arm of a
`match` on `T.Kind` that a copy does not read; a case that a call in a body gives beside the field's type leaves the function that holds `T.Make`, as one of any argument of a call does (§5),
the bodies running in the order of the fields, as F12 evaluates arguments)* Where that function does not list such a case, it is `unlisted-case` at that call's line in
the body, with O3's text; the call that made the copy, where a copy that gives an
outcome its function does not list is `unlisted-case` above, is the place of an outcome the type gives, `Invalid` (§4,
the owner, 2026-11-06, against moving the report to the call that made the copy). *(assumed, §4, with the build: it is
reported once for each case the copies give, the same case of several copies once)* A generic function that reads its type, and one that passes its type parameter to such
a function, is checked only in the copies that calls make, in its body: an error in its body shows at its first copy,
and the body of one that no call reaches is never checked (§4, the owner, 2026-11-01, "Only per copy, as Zig"). Its
signature, its type parameters among them, is checked from the text in every case, so that F16 refuses a type parameter
of a function no call reaches too (§4, the owner, 2026-11-06, "Signature always, body per copy"; the cost is that a
misnamed type parameter in a function nobody calls is an error, where Zig analyses no part of it). *(§4, the author's
note, 2026-11-07, read from Zig's source and not checked with a Zig compiler: Zig's AstGen refuses a parameter that
shadows a declaration in a function nobody references too, a `comptime` type parameter among them, and only its
analysis of the body waits for a reference)*
A copy is made at the first call that names its type arguments in the order the checker reads a file: the bodies of its
functions, in the order of their declarations, the overloads of one name at the first of them, then the methods of its
classes, class by class and method by method in the order of their declarations, then the top-level code, the calls of
a copy's body before the calls that follow the call that made it. A later call with the same type arguments reads that
copy, so that an error of the copy is reported once, and one reported at the call, `kind-not-read` or the
`unlisted-case` of an outcome the type gives, shows at that first call, the call that made it (§4, the owner,
2026-11-06, against the first call in the order of the text and against a report at every call that names the copy;
the cost is that the line named is not always the first in the file). The order is stated for one file, and the order
across the files of a program is open (§14). *(assumed, §4, with the build: a call that infers
its type arguments makes a copy as one that writes them; a fault of the function's text that several copies give,
`redeclared` (V9) among them, is reported once, at its line, and one that differs between copies, `kind-not-read` for
each kind, once for each copy)*
*(assumed, §4: a variable that an arm of a `match` on `T.Kind` names counts as read in a copy that
does not read that arm; the copies are made depth first: the copies that the calls of a copy ask
for are made after its body is checked, in the order of those calls in its text, each with its own
copies before the copy of the call after it, and the copies that calls outside every copy ask for
are made after the code outside the copies is checked; the outer call of a chain of copies is the
call outside every copy that made its first copy; a build stopped at the bound of 64 copies on a
chain of copies (the record, section 14) reports the errors outside the copies, the `not supported`
of that bound and the errors of the copies made before it, while a copy of the same outer call that a chain asks
for after the bound is not made, so that its errors show only in a build where the chain is ended)*

Case: [types/type-fields.kz](../corpus/types/type-fields.kz)
```kurz
data Order(string Id, int Count)

void ShowFields<T>(T value) {
    for f in T.Fields {
        print("{f.Name} = {f.Get(value)}")
    }
}

ShowFields(Order("a1", 3))
```

Case: [types/type-fields-type.kz](../corpus/types/type-fields-type.kz)
```kurz
data Point(int X, int Y)
data Shape(string Name, Point Corner)

string Show<T>(T value) => match T.Kind {
    TypeKind.Data => Fields(value)
    else => "{value}"
}

string Fields<T>(T value) {
    mut text = ""
    for f in T.Fields {
        f.Type part = f.Get(value)
        shown = Show<f.Type>(part)
        text = "{text}{f.Name}=({shown});"
    }
    return text
}

print(Show(Shape("box", Point(1, 2))))
```

Case: [types/type-kind.kz](../corpus/types/type-kind.kz)
```kurz
data Order(string Id, int Count)

void ShowKind<T>() {
    match T.Kind {
        TypeKind.Data => print("data")
        TypeKind.String => print("string")
        else => print("other")
    }
}

ShowKind<Order>()
ShowKind<string>()
ShowKind<bool>()
```

Case: [types/type-make.kz](../corpus/types/type-make.kz)
```kurz
data Point(int X, int Y)

T Zero<T>() => T.Make(f => 0)

print(Zero<Point>())
```

Case: [types/type-make-get.kz](../corpus/types/type-make-get.kz)
```kurz
data Order(string Id, int Count)
data Label(string Name, string Tag)
data Empty

T Remake<T>(T value) => T.Make(f => f.Get(value))

T Names<T>() => T.Make(f => "{f.Name}!")

T Lazy<T>() => T.Make(f => (() => f.Name)())

void Twin<T>(T value) {
    for g in T.Fields {
        print("{g.Name}: {T.Make(f => f.Get(value))}")
    }
}

print(Remake(Order("a1", 3)))
print(Remake(Empty))
print(Names<Label>())
print(Lazy<Label>())
Twin(Order("b2", 4))
```

Case: [types/type-make-kind-not-read.kz](../corpus/types/type-make-kind-not-read.kz)
```kurz
T Zero<T>() => T.Make(f => 0)

print(Zero<int>())
```

Case: [types/type-make-form.kz](../corpus/types/type-make-form.kz)
```kurz
data Point(int X, int Y)

T Zero<T>() => T.Make((int f) => 0)

print(Zero<Point>())
```

Case: [types/type-kind-redeclared.kz](../corpus/types/type-kind-redeclared.kz)
```kurz
enum TypeKind { Data }

print(1)
```

Case: [types/type-kind-passed.kz](../corpus/types/type-kind-passed.kz)
```kurz
data Order(string Id)

string Describe<T>() => match T.Kind {
    TypeKind.Data => "Data"
    else => "other: {T.Kind}"
}

void Show<U>(U value) {
    print(Describe<U>())
}

Show(Order("a1"))
Show(3)
Show([1, 2])
```

Case: [types/type-kind-not-read-passed.kz](../corpus/types/type-kind-not-read-passed.kz)
```kurz
data Order(string Id)

void ShowKind<T>() {
    match T.Kind {
        TypeKind.Data => print("data")
    }
}

void Outer<U>() {
    ShowKind<U>()
}

Outer<Order>()
Outer<bool>()
```

Case: [types/type-kind-not-read.kz](../corpus/types/type-kind-not-read.kz)
```kurz
data Order(string Id)

void ShowKind<T>() {
    match T.Kind {
        TypeKind.Data => print("data")
    }
}

ShowKind<Order>()
ShowKind<bool>()
```

Case: [types/type-kind-if-not-read.kz](../corpus/types/type-kind-if-not-read.kz)
```kurz
data Point(int X, int Y)

void Show<T>(T value) {
    if T.Kind == TypeKind.Data {
        for f in T.Fields {
            print(f.Name)
        }
    }
}

Show(Point(1, 2))
Show(true)
```

Case: [types/type-fields-kind-not-read.kz](../corpus/types/type-fields-kind-not-read.kz)
```kurz
void ShowFields<T>(T value) {
    for f in T.Fields {
        print(f.Name)
    }
}

ShowFields(3)
```

Case: [types/type-fields-value.kz](../corpus/types/type-fields-value.kz)
```kurz
data Order(string Id)

void Show<T>(T value) {
    for f in T.Fields {
        print(f)
    }
}

Show(Order("a1"))
```

Case: [types/type-fields-get-mismatch.kz](../corpus/types/type-fields-get-mismatch.kz)
```kurz
data Order(string Id)
data Invoice(string Id)

void Show<T>(T value, Invoice other) {
    for f in T.Fields {
        print(f.Get(other))
    }
}

Show(Order("a1"), Invoice("b2"))
```

### T34 (decided, §4) A cycle of generic calls whose type argument grows

A generic function becomes one copy of its code for each list of type arguments it is called with
(§4). A call in a generic function's body whose type argument holds a type parameter of that
function inside a list, as `[x]`, a `List<T>` for `T`, makes a copy for a larger type than the copy
it stands in. Where such a call lies on a cycle of calls among generic
functions, directly or through others, so that the type argument comes back to the type parameter
it grew from, each copy makes a larger one without end, and the call is the compile error
`growing-type-argument`, whatever values end the cycle while the program runs. A call that passes a
type parameter unchanged, swaps two of them, or gives one to a function that takes it back out of
a list, `Up<U>(List<U> ys)` called with `[x]` for `U = T`, makes no larger copy. The cycle is
refused where it is written, whether or not a call reaches it. A generic function that reads its
type (T33) keeps its check in the copies, but for the calls this paragraph reads on its text: an
arm of a `match` on `T.Kind` can end its chain, and a chain of more than 64 copies stays
`not supported`. Of its calls, those in a loop over `T.Fields` are read for such a cycle on its
text, and a cycle that grows there is refused at that call. The chain of copies stops at that
call, so that it does not reach that bound, and the copies a call made before it report every
other error of the function's body in the same build; a function that no call reaches has no copy,
so the refusals of its cycle are the only errors it reports, as T33 reads such a function. A call
in an arm of a `match` on `T.Kind` takes no part where no loop over `T.Fields` is opened between the
call and the nearest such arm around it, in an arm inside such a loop as in one outside it, since
each copy takes the one arm of its kind; a call in a loop opened inside the nearest such arm is the
case of the assumed reading below. A type argument of a call in a loop over `T.Fields` that is the
field's type takes no part either, since on the text that type is a type of its own, no type
parameter of the function, and each copy reads the field's real type. One that holds the
field's type beside a type parameter of the calling function inside a form of a type, as
`Map<f.Type, T>` for `T`, grows by that parameter, and the call is refused at its line: the walk
types `f.Type` on the text as a type of its own, which is no type parameter of the function. A
cycle that grows in such an arm, at a call with no such loop between it and the nearest such arm
around it, or that leaves the loop through a call outside it, shows only at that bound. *(assumed,
§4: the id; a type argument grows where it holds a type parameter of the calling function inside
any other form of a type, a map, a `data` type and a function type among them, and not where it is
a case of a union, which holds each case once, so that a `T | string` for `T` makes no larger copy;
a type argument that holds the field's type inside a form of a type with no type parameter of the
calling function, `List<f.Type>` and `Pair<f.Type, int>`, takes no part, as the field's type
itself; one that holds the field's type and a type parameter of the calling function grows by that
parameter wherever the form holds it, `List<Pair<f.Type, T>>` and `Pair<f.Type, List<T>>` among
them, and the error names it as the call gives it, written or inferred, with the field's type as
the loop's variable writes it, `List<Pair<f.Type, T>>` for `Deep([Pair(f.Get(x), x)], n - 1)`; a
type argument inferred from `f.Get(value)`, a value of the field's type, holds that type as a
written one does, `Pair(f.Get(x), x)` as `Pair<f.Type, T>`; the field's type stands on the text
where a type must meet a condition, as a map's key, and each copy checks the field's real type
with its own error (T33); a call in a loop over `T.Fields` opened inside the
nearest arm of a `match` on `T.Kind` around it is read as a call of that loop;
every call of the cycle where a type argument grows is
refused, each one time, a written type argument as an inferred one, a call in a lambda of the body
as one of the body)*

Case: [types/generic-growing-cycle.kz](../corpus/types/generic-growing-cycle.kz)
```kurz
void Deep<T>(T x, int n) {
    if n > 0 {
        Deep([x], n - 1)
    }
}

Deep(1, 3)
print("done")
```

Case: [types/generic-growing-cycle-through.kz](../corpus/types/generic-growing-cycle-through.kz)
```kurz
void Ping<T>(T x, int n) {
    if n > 0 {
        Pong([x], n - 1)
    }
}

void Pong<U>(U y, int n) {
    Ping(y, n)
}

Ping(1, 3)
print("done")
```

Case: [types/generic-growing-no-cycle.kz](../corpus/types/generic-growing-no-cycle.kz)
```kurz
void Same<T>(T x, int n) {
    if n > 0 {
        Same(x, n - 1)
    }
}

void Swap<A, B>(A a, B b, int n) {
    if n > 0 {
        Swap(b, a, n - 1)
    }
}

void Widen<T>(T x, int n) {
    if n > 0 {
        T | string y = x
        Widen(y, n - 1)
    }
}

void Down<T>(T x, int n) {
    if n > 0 {
        Up([x], n - 1)
    }
}

void Up<U>(List<U> ys, int n) {
    Down(ys[0], n)
}

List<T> Wrap<T>(T x) => [x]

Same(1, 3)
Swap(1, "a", 3)
Widen(1, 3)
Down(1, 3)
print(Wrap(Wrap(1)).Count)
print("done")
```

Case: [types/generic-growing-cycle-fields.kz](../corpus/types/generic-growing-cycle-fields.kz)
```kurz
data Box<T>(T A)

// `Deep` reads its type, and the call in its loop over `T.Fields` is read on its text: the chain of copies stops at it, after `Deep<Box<int>>`
void Deep<T>(T x, int n) {
    for f in T.Fields {
        if n > 0 {
            Deep(Box(x), n - 1)
        }
    }
}

Deep(Box(1), 3)
print("done")
```

Case: [types/generic-growing-cycle-fields-arm.kz](../corpus/types/generic-growing-cycle-fields-arm.kz)
```kurz
data Box(int A)

// a call in an arm of a `match` on `T.Kind` takes no part in the walk inside a loop over `T.Fields` too: no copy takes the `TypeKind.List` arm
void Deep<T>(T x, int n) {
    for f in T.Fields {
        match T.Kind {
            TypeKind.List => Deep([x], n - 1)
            else => print(n)
        }
    }
}

Deep(Box(1), 3)
print("done")
```

Case: [types/generic-growing-cycle-fields-type.kz](../corpus/types/generic-growing-cycle-fields-type.kz)
```kurz
data Point(int X, int Y)
data Shape(string Name, Point Corner)

// `Show` calls itself in its loop over `T.Fields` with the field's type, which takes no part in a cycle that grows
string Show<T>(T value) {
    match T.Kind {
        TypeKind.Data => {
            mut text = ""
            for f in T.Fields {
                f.Type part = f.Get(value)
                shown = Show<f.Type>(part)
                text = "{text}{f.Name}=({shown});"
            }
            return text
        }
        else => return "{value}"
    }
}

print(Show(Shape("box", Point(1, 2))))
```

Case: [types/generic-growing-cycle-fields-beside.kz](../corpus/types/generic-growing-cycle-fields-beside.kz)
```kurz
data Point(int X, int Y)
data Pair<A, B>(A First, B Second)

// `Deep` gives `Pair<f.Type, T>` for `T` in its loop over `T.Fields`: the field's type is a type of its own on the text, and the `T` beside it grows
void Deep<T>(T x, int n) {
    for f in T.Fields {
        if n > 0 {
            Deep(Pair<f.Type, T>(f.Get(x), x), n - 1)
        }
    }
}

Deep(Point(1, 2), 3)
print("done")
```

Case: [types/generic-growing-cycle-fields-inferred.kz](../corpus/types/generic-growing-cycle-fields-inferred.kz)
```kurz
data Point(int X, int Y)
data Pair<A, B>(A First, B Second)

// `Deep` gives `Pair(f.Get(x), x)` for `T` in its loop over `T.Fields`: the type arguments of `Pair` are inferred, `f.Get(x)` is of the field's type, and the `T` beside it grows
void Deep<T>(T x, int n) {
    for f in T.Fields {
        if n > 0 {
            Deep(Pair(f.Get(x), x), n - 1)
        }
    }
}

Deep(Point(1, 2), 3)
print("done")
```

### T35 (decided, §4) A type parameter that holds a constant

A type parameter can hold a constant, as Rust's `const` parameter does: `const`, the constant's
type and the parameter's name, `T | JsonError | Invalid Parse<T, const JsonKeys K = JsonKeys.AsWritten>(string text)`
(§12 gives `K` its default). A call
writes the constant among its type arguments, `Json.Parse<Order, JsonKeys.SnakeCase>(text)`; each
constant makes a copy of the function of its own (T19), and the function's code reads the
parameter as a value known while the program compiles (§4, the owner, 2026-11-01, "Const generic,
as Rust"). *(assumed, §4: such a parameter may have a default, `const Mode M = Mode.Plain`, which a
call that leaves it out takes; the types it holds are the integer types, `bool`, `char` and an
`enum`)* A default is a constant expression, as a parameter's default value is (F12), of one
operand: a literal with its `-`, an `enum`'s value, or an expression in brackets,
`const int N = (1 + 2)`, as Rust takes `{ 1 + 2 }` there, and `const int N = 1 + 2` is `syntax`;
it is checked against the parameter's type as a parameter's default value is (F12), so `const byte N = 300` is
`constant-overflow` (T6), `const Mode M = Color.Red` is `type-mismatch` (T1) and `const int N = (F())` is
`default-not-constant` (F12); the name of an earlier parameter there is `unknown-name` (V10); a type outside the list,
a type parameter that holds a type among them, `<T, const T N>`, is `syntax` *(assumed, §4, with the build: the name of an earlier
constant parameter as the type, `const Mode M, const M N`, is `unknown-name`, as `M value` is (below), and no type
outside the list)*; a call that writes at least one type argument
but fewer than the parameters without a default, or more than all of them, is T19's count, `syntax`, which names each
count the call may write, while a call that writes none is read in the assumed clause below. The parameter's name as the type of a declaration, `M value`, is `unknown-name` (V10), and
among type arguments where a type stands it is a constant, `type-mismatch` (T1) as a literal and an
`enum`'s value are there, inside a declaration's type too, `List<M> values`, where it stands among type arguments; a constant passed on to a parameter of a wider integer type widens as a
value of its type does (T8) (§4, the owner, 2026-11-03, "every default", with the widening "as C#
reads it"). *(assumed, §4: `const` is that word before the type of a type parameter, as it is before
`assert(` (T37) and among the words of an `asm`, and a name elsewhere (L18); a
type parameter with a default comes after every one without, and one without after one with is
`syntax`; the parameter's name stands as a value of its type; a value of a type that does not widen to the
constant's (T8), or a type, where the constant stands is `type-mismatch` (T1); a call that writes no type arguments takes each
constant's default, and a parameter with none is `cannot-infer` (T27); a function with such a
parameter is checked in each copy a call makes, as one that reads its type is (T33))* A variable, a parameter, a
loop's variable, a binding of a `match` arm or a lambda's parameter named like that type parameter in the function is
`redeclared`, as for any type parameter: a parameter of the function from its text in every case (§4, the owner,
2026-11-07, "Refused always"), and each other in each copy a call makes, so not at all where no call reaches the function
(V9, T33; §4, the owner, 2026-11-04, "the same reading for both"); `M = Mode.Loud` with that name is such a
declaration (V8, V9). The type parameter itself takes no name of a variable at the outermost level of the top-level
code, whether or not a call makes a copy (F16).

A constant of an integer type, `bool` or `char` is a bare literal among a call's type arguments
where `(` follows the list, `Buffer<64>()`, as Rust reads `f::<64>()`; `f(a < 64, c > (d))` is then
the call `a<64, c>(d)`, and two comparisons take parentheses, `f((a < 64), c > (d))` (§4, the
owner, 2026-11-02, "a bare literal before `(`"). *(assumed, §4: an integer takes its `-`,
`Count<-1>()`, as Rust takes `f::<-1>()`; the literal is checked against the parameter's type as
an argument is, so one that does not fit an integer type is `constant-overflow` (T6) and one of
another type `type-mismatch` (T1); a list that holds a literal is kept before `(` alone, so
`List<64>.FromRaw(p, 0)` is `syntax`)* An integer literal of every form, with its suffix and its
base, starts the list, `f(a < 64L, c > (d))`, and so do `true`, `false` and a `char` literal, while a `double`, a string, a literal with a unit and
`null` keep the comparisons, since no such parameter holds one (§4, the owner, 2026-11-03, "every
default").

Case: [types/const-parameter.kz](../corpus/types/const-parameter.kz)
```kurz
enum Mode { Plain, Loud }

void Say<const Mode M>(string text) {
    match M {
        Mode.Plain => print(text)
        Mode.Loud => print("{text}!")
    }
}

void Twice<T, const Mode M = Mode.Plain>(T value) {
    Say<M>("{value}")
    Say<M>("{value}")
}

void Shout<const Mode M>(string text) {
    Say<M>(text)
}

void Report<const Mode M>() {
    f = () => M
    print("{M} {f()}")
    Shout<M>("c")
}

Say<Mode.Loud>("a")
Twice(1)
Twice<string, Mode.Loud>("b")
Report<Mode.Loud>()
```

Case: [types/const-parameter-mismatch.kz](../corpus/types/const-parameter-mismatch.kz)
```kurz
enum Mode { Plain, Loud }
enum Color { Red }

void Say<const Mode M>(string text) {
    print("{M} {text}")
}

Say<Color.Red>("a")
```

Case: [types/const-parameter-default-order.kz](../corpus/types/const-parameter-default-order.kz)
```kurz
enum Mode { Plain, Loud }

void Late<const Mode M = Mode.Plain,
    T>(T value) {
    print("{M} {value}")
}

Late<Mode.Loud, int>(1)
```

Case: [types/const-parameter-name-as-type-argument.kz](../corpus/types/const-parameter-name-as-type-argument.kz)
```kurz
enum Mode { Plain, Loud }

void Other<T>() {
}

void Outer<const Mode M>() {
    Other<M>()
}

Outer<Mode.Plain>()
```

Case: [types/const-parameter-other-type.kz](../corpus/types/const-parameter-other-type.kz)
```kurz
void Name<const string S>() {
    print(S)
}

print(1)
```

Case: [types/const-parameter-as-type.kz](../corpus/types/const-parameter-as-type.kz)
```kurz
enum Mode { Plain, Loud }

void Keep<const Mode M>(M value) {
    print(value)
}

Keep<Mode.Plain>(Mode.Plain)
```

Case: [types/const-parameter-redeclared.kz](../corpus/types/const-parameter-redeclared.kz)
```kurz
enum Mode { Plain, Loud }

void Say<const Mode M>(string text) {
    M = Mode.Loud
    print("{text} {M}")
}

Say<Mode.Plain>("a")
```

Case: [types/const-parameter-not-inferred.kz](../corpus/types/const-parameter-not-inferred.kz)
```kurz
enum Mode { Plain, Loud }

void Say<const Mode M>(string text) {
    print("{M} {text}")
}

Say("a")
```

Case: [types/const-parameter-literal.kz](../corpus/types/const-parameter-literal.kz)
```kurz
int Scaled<const int N>(int x) => x * N
bool Flag<const bool B>() => B
char Mark<const char C>() => C

void Show<const int N = 4>() {
    print("{N}")
}

void Pass<const int L>() {
    Show<L>()
}

print(Scaled<64>(2))
print(Flag<true>())
print(Mark<'x'>())
Show()
Show<-3>()
Pass<7>()
```

Case: [types/const-parameter-widening.kz](../corpus/types/const-parameter-widening.kz)

Case: [types/const-parameter-narrowing.kz](../corpus/types/const-parameter-narrowing.kz)

Case: [types/const-parameter-in-declaration-type.kz](../corpus/types/const-parameter-in-declaration-type.kz)
```kurz
enum Mode { Plain, Loud }

void Keep<const Mode M>(List<M> values) {
    print(values)
}

Keep<Mode.Plain>([Mode.Plain])
```

Case: [types/const-parameter-literal-mismatch.kz](../corpus/types/const-parameter-literal-mismatch.kz)
```kurz
int Size<const int N>() => N
print(Size<true>())
```

Case: [types/const-parameter-comparisons.kz](../corpus/types/const-parameter-comparisons.kz)
```kurz
bool Both(bool x, bool y) => x && y

a = 1
c = 5
d = 3
print(Both((a < 64), c > (d)))
```

### T37 (decided, §4) `const assert`

Generic code stops the build on a fault it finds in a type it reads with `const assert(condition,
text)`: a condition over values known while the program compiles, a copy's reading of its type
(T33) and its constant type arguments (T35) among them, checked in each copy. Where it is false,
the build stops at the call that made the copy, with the text of the code that holds it:
`const assert(T.Kind == TypeKind.Data, "{T} is no data type")` stops `Need(1)` with `int is no
data type` at that call, as Rust's `const { assert!(condition, "text") }` stops a build in a copy of
generic code. Any generic code can use it (§4, the owner, 2026-11-01, "const assert, as Rust").
A variable or a parameter in its condition or in a hole of its text, whose value is not known while
the program compiles, is `const-assert-failed`, as a false condition is (§4, the owner, 2026-11-02,
"`const-assert-failed`"). *(assumed, §4: the answer's "the one id of everything a const assert
refuses" read as what its evaluation refuses, a condition that is not a `bool` and a text that is
not a string literal keeping their own ids (below); the spelling `const assert(condition, text)`, a statement; the text interpolates
values known while the program compiles as a string literal interpolates values, a type giving its
name and an `enum` value its name; the compiler evaluates literals, constants, operators, a copy's
reading of its type, its constant type arguments, the standard library's functions on strings, and
a function of the program or of a package whose body uses nothing else besides its own variables,
loops and branches, while a call of anything else in a `const assert` is `const-assert-failed`,
naming that call; an evaluation that has not ended after a fixed count of steps is the same error)* *(assumed, §4: `const` followed by the name `assert` and `(` at the
start of a statement starts it, and both are names everywhere else (L18); a line break may stand
inside its parentheses, as inside a call's; the statement stands where any statement stands, and
outside a generic function it is checked once where it stands, the build stopping at the statement
where it is false; a generic function that holds one is checked in each copy a call makes, as one
that reads its type is (T33); its condition is a `bool` (C3); the text is a string literal, and a
text of another kind is `syntax`; a hole that names a type parameter gives the name of the type the
copy binds, and any other hole the text of its value (A2, A4); a variable or a parameter in the
condition or in a hole is that error at its name; what a `const assert` names counts as
read, so that a variable read there alone is no `unused-variable` (V5); the error names the copy the call made and the
function that holds the `const assert`, as `kind-not-read` does)*

Case: [types/const-assert.kz](../corpus/types/const-assert.kz)
```kurz
enum Mode { Plain, Loud }
data Point(int X, int Y)

void Names<T>(T value) {
    const assert(T.Kind == TypeKind.Data, "{T} is no data type")
    for f in T.Fields {
        const assert(f.Name != "Id", "{T} has a field {f.Name}")
        print(f.Name)
    }
}

void Say<const Mode M>(string text) {
    const assert(M == Mode.Plain, "{M} is not plain")
    print(text)
}

const assert(1 + 1 == 2, "one and one")
Names(Point(1, 2))
Say<Mode.Plain>("a")
```

Case: [types/const-assert-failed.kz](../corpus/types/const-assert-failed.kz)
```kurz
data Point(int X, int Y)

void Need<T>(T value) {
    const assert(T.Kind == TypeKind.Data, "{T} is no data type")
}

Need(Point(1, 2))
Need(1)
```

Case: [types/const-assert-variable.kz](../corpus/types/const-assert-variable.kz)
```kurz
n = 2
const assert(n > 1, "n is small")
```

Case: [types/const-assert-constant.kz](../corpus/types/const-assert-constant.kz)
```kurz
void Buffer<const int N>() {
    const assert(N > 0, "a buffer of {N} items")
    print(N)
}

Buffer<64>()
Buffer<0>()
```

### T18 (assumed, §4) Operators

`+ - * / %` on numbers, `== != < <= > >=` for comparison, on two strings as T32 says, and `&& || !` on `bool`, with the
meaning and the precedence they have in C#. Integer division drops the fraction, toward zero. An
operand narrower than `int` is promoted to `int` first, as C# does (T28). An operator on two values
it does not take, `<` on two `bool`s among them, as C#'s `<` takes none (CS0019), is `type-mismatch`
*(assumed, §4: the meaning the operators have in C#)*.

Case: [types/arithmetic.kz](../corpus/types/arithmetic.kz)
```kurz
print(1 + 2 * 3)
print(7 / 2)
print(-7 / 2)
print(7 % 3)
print(1 < 2 && 2 < 3)
print(!(1 == 1) || 3 >= 3)
```

Case: [types/compare-bools.kz](../corpus/types/compare-bools.kz)
```kurz
// `<` orders no `bool`, as in C#
print(true < false)
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
