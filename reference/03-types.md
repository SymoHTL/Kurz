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

The integer types are `sbyte`, `byte`, `short`, `ushort`, `int` (32 bits), `uint`, `long`
(64 bits) and `ulong`. The others are `float`, `double` and `decimal`. Each has the size its name
has in C#.

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

An expression made only of literals whose result leaves the range of its type is the compile
error `constant-overflow`.

Case: [types/constant-overflow.kz](../corpus/types/constant-overflow.kz)
```kurz
x = 2147483647 + 1
print(x)
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
are; nothing of that is a loss. Into `decimal`, which has neither, an infinity, NaN and a value
beyond its range lose the value as an integer target does, and rounding is no loss. *(assumed: the
targets other than integers, which the question did not name)*

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
result leaves the range of its type.

Case: [types/wrapping.kz](../corpus/types/wrapping.kz)

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

Beside the 64-bit pair of T24 stand `longduration` and `longtimestamp`: 128 bits with the same
step of one nanosecond, for a calendar or an archive that reaches before 1677 or after 2262. A
`duration` widens into a `longduration` and a `timestamp` into a `longtimestamp` without a word
(T8), a conversion the other way is written out and checked as T20 says, and the texts are A12's.
What T13 says of the narrow pair holds for the wide one, and an operation between the two pairs
widens the narrow operand and has the wide type, as `i + l` has `long` (T8), so that
`longtimestamp - timestamp` is a `longduration`. *(assumed: the mixed operations; proposed on
2026-10-07, when the review found the hole)* The owner chose this on 2026-10-04, against a 64-bit
pair with a millisecond step, in which nothing below a millisecond exists, and against a type of
the library, which has no literal (L14); the cost is 128-bit arithmetic and a second name for
every operation on time in the library. *(proposed: the names, which stand to the narrow pair as
`long` stands to `int`; and the type of a literal, which follows the integer literal of L10: a
duration literal is a `duration`, or a `longduration` when its value does not fit one, and where
a type is written or expected it takes that type if the value fits, so that a literal beyond both
ranges is `constant-overflow` (T6))*

Case: [types/longduration-parameter.kz](../corpus/types/longduration-parameter.kz)
```kurz
void Show(longduration d) {
    print(d)
}

duration span = 90min
Show(span)
```

Case: [types/longduration-wide.kz](../corpus/types/longduration-wide.kz)
```kurz
longduration a = 100000days
longduration l = a + a
print(duration(l))
```

### T14 (assumed, §4) Out of range and division by zero

An index that is out of range raises the exception `index-out-of-range`, and a division by zero
`divide-by-zero` (E3). What `%` by zero, a floating-point division by zero and a division of
literals by the literal zero do is T25.

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

### T25 (proposed) Division by zero, in detail

`%` by zero raises `divide-by-zero`, as `/` by zero does: both are the same instruction of the
machine, and C# throws the same exception for both. A floating-point division by zero does not
raise: it yields an infinity, or NaN for `0.0 / 0.0`, as IEEE 754 and C# have it. A division or
a remainder of literals by the literal `0`, which T6 would otherwise fold at compile time, is the
compile error `constant-divide-by-zero`, as C# reports it, because such a program can never run
to the line after it.

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
the edges.

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

A `char` has no literal in this reference. The case walks the `.Chars` of a string and prints
each one (A8).

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

### T27 (proposed) How type arguments are inferred

A type argument that a call does not write is inferred from the arguments: a type parameter takes
the one type that every argument in its positions has, after the conversions of this chapter (a
literal takes the type an argument of its position would take, L10). When the arguments in its
positions have different types, or no argument stands in one of its positions, the call is the
compile error `cannot-infer`, and the type arguments are written. This is C#'s inference in Kurz's
terms: C# would widen a `byte` to an `int` to make `Max(b, i)` an `int`, which T8 and T9 do not
allow here, and C#'s inference has changed between its versions, so no version of it is named.

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
(T9). The owner chose this on 2026-10-03, against computing
in the operands' type, under which a program ported from C# computes other values without a word;
the cost is that `byte c = a + b` needs `byte(a + b)`, and that the wrap of T4 never happens at 8
or 16 bits, while `ushort * ushort` is computed in a signed `int` and can overflow it. The
wrapping operators `+%`, `-%` and `*%` (T12) do not promote: they compute in the wider of their
operand types and wrap there, so that an 8-bit checksum is `a +% b` on two `byte`s, where a
promoted `+%` would need a mask on every narrow checksum (the owner, 2026-10-04, against promoting
them; C# has no such operators). The cost: `a + b` and `a +% b` differ in type.
*(proposed: operands of different signedness are `sign-mix` (T9) whatever their widths, because no wider type holds both, and a literal operand takes
the other operand's type, so that `sum +% 1` on a `byte` computes in `byte`)*

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
