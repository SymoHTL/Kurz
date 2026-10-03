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
Together they are the integer types of C#.

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

The same arithmetic raises an exception in a test build. Which exception, and how a case names
the one it expects, is E3: until it is answered the case below passes with any exception.

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

### T8 (assumed, §4) Implicit widening

A value of a narrower integer type becomes a wider one of the same signedness without a word.
This is less than C# does, which also widens `byte` and `ushort` into `int` and `uint` into
`long`, and every integer type into `float`, `double` and `decimal`; whether Kurz follows it
there, and which error a value gets where it does not, is T23.

Case: [types/widening.kz](../corpus/types/widening.kz)
```kurz
int small = 5
long big = small
print(big)
```

### T23 (open) Widening across signedness and into floating-point

The record marks T8 as *(assumed)* with the words "of the same signedness", and names no error for
`int x = b` with a `byte` or for `uint u = i` with an `int`: T7 covers only a wider type put into
a narrower one, T9 only an operation between the two. The options:

- (a) As T8 stands: no implicit conversion across signedness, and none from an integer type into
  `float`, `double` or `decimal`; each is written as a conversion (T10). The error for a value put
  where a type of the other signedness is required is `sign-mix`, and for an integer where a
  floating-point type is required `type-mismatch`. Cost: `int x = b` needs `int(b)`, which no C#
  developer expects.
- (b) What C# does: an unsigned type widens into a signed type that holds all its values (`byte`
  and `ushort` into `int`, `uint` into `long`), and every integer type widens into `float`,
  `double` and `decimal`; a signed type never becomes unsigned without a word, and `uint u = i`
  stays `sign-mix`. Cost: `b + i` with a `byte` and an `int` is then an `int` and not T9's
  `sign-mix`, so T9's case changes.

The lean is (b): no value is lost on any of these paths, and they are the paths a C# developer
takes without thinking.

### T9 (decided, §4) No mixing of signed and unsigned

An operation between a signed and an unsigned integer is the compile error `sign-mix`. A
conversion (T10) puts both on one side.

Case: [types/sign-mix.kz](../corpus/types/sign-mix.kz)
```kurz
byte b = 200
int i = 5
print(b + i)
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

A conversion whose value does not fit the target type behaves as overflow does (T4, T5): in a
release build the value wraps around, in a test build it raises an exception; the exception has
no id yet (E3). The cases below have an integer source. What a `double`, `float` or `decimal`
source does is T26: whether a dropped fraction counts as losing the value, and what wraps around
when the source is out of range, an infinity or NaN.

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

### T26 (open) A conversion from a floating-point or `decimal` source

`int(2.5)`, `int(1e20)`, `int(x)` with `x` NaN or infinite, and `float(d)` for a `double` beyond
a `float`'s range all lose something, and T20 says only what an integer source does. LLVM leaves
the out-of-range cases undefined (`fptosi` gives poison), so a rule has to be written down. The
options:

- (a) Dropping a fraction is not losing the value: `int(2.5)` is `2`, toward zero, as in C#. A
  value outside the target's range, an infinity and NaN lose the value: a test build throws (T5),
  a release build gives the target's largest or smallest value for an out-of-range value and an
  infinity, and `0` for NaN (saturation, as C# does since .NET Core 3.0). Cost: "wraps around"
  in the record does not describe this path; the record's sentence would say "saturates".
- (b) The same, but the release build wraps the integer part modulo the target's width, as T4
  does for integers, and NaN gives `0`. Cost: a wrapped value from a floating-point source
  means nothing to anyone; the compiler pays for the modulo on every such conversion.
- (c) Every conversion that drops a fraction loses the value too, so `int(2.5)` throws in a test
  build. Cost: rounding has to be written out on every conversion (`int(Math.Floor(x))`).

The lean is (a).

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

Durations and timestamps are 64-bit types of their own and never raw integers: one of them where
an integer is required is the compile error `type-mismatch`. A duration is written with a unit
(L14). The names of the two types, which a parameter or a field needs, and what one step of
their 64 bits is are T24.

Case: [types/duration-not-integer.kz](../corpus/types/duration-not-integer.kz)
```kurz
int seconds = 30s
print(seconds)
```

### T24 (open) The names and the resolution of durations and timestamps

No rule names the two types, so no program can write a parameter or a field of them, and
"64-bit" does not say what one step is. The step decides how many `days` fit before the type
overflows, whether a value below one millisecond can exist, and how A12 prints a value. The
options for the names: (a) `duration` and `timestamp`, in lower case like the other built-in
types; (b) `Duration` and `Timestamp`, like the types of the library. The options for the step:
(a) one nanosecond, which holds about 292 years of duration and timestamps between the years 1678
and 2262; (b) one millisecond, the smallest unit of L14, which holds about 292 million years and
nothing below a millisecond; (c) one tick of 100 nanoseconds, as .NET's `TimeSpan` and
`DateTime` use, which holds about 29,000 years.

The lean is lower-case names and nanoseconds: the ranges are wide enough for a device and a
server, and a nanosecond is what the clocks of the operating systems give.

### T14 (assumed, §4) Out of range and division by zero

An index that is out of range and a division by zero raise an exception. Neither exception has an
id yet (E3), so the cases below pass with any exception. What `%` by zero, a floating-point
division by zero and a division of literals by the literal zero do is T25.

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

`%` by zero raises the exception that `/` by zero raises: both are the same instruction of the
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

byte b = 1
int i = 2
print(Pick(b, i))
```

### T18 (assumed, §4) Operators

`+ - * / %` on numbers, `== != < <= > >=` for comparison, and `&& || !` on `bool`, with the
meaning and the precedence they have in C#. Integer division drops the fraction, toward zero. What
the type of the result is when an operand is narrower than `int`, which C# promotes, is T28.

Case: [types/arithmetic.kz](../corpus/types/arithmetic.kz)
```kurz
print(1 + 2 * 3)
print(7 / 2)
print(-7 / 2)
print(7 % 3)
print(1 < 2 && 2 < 3)
print(!(1 == 1) || 3 >= 3)
```

### T28 (open) Arithmetic and shifts on types narrower than `int`

C# promotes `sbyte`, `byte`, `short` and `ushort` operands to `int` before `+ - * / %`, `~`, `<<`
and `>>`: `a + b` on two `byte`s is an `int`, `print(a + b)` prints `300` for 200 and 100, and
`byte c = a + b` is `narrowing-conversion` (T7); a shift count is masked by 31 and a negative count is
masked too (`1 << -1` is `1 << 31`). T4 and T5 speak of "the range of its type", which reads as
the operands' type. The options:

- (a) C#'s promotion: the result of these operators on operands narrower than `int` is an `int`
  (for `ushort` too, as in C#), the shift width is 32, a negative count is masked. Cost: `byte c = a + b` needs `byte(a + b)`, and the wrap of T4 never happens at 8 or
  16 bits.
- (b) The operands' type: `a + b` on two `byte`s is a `byte` that wraps or throws (T4, T5) at
  256, the shift width is the type's width (8 for a `byte`, so `b << 9` is `b << 1`), and a
  negative count is reduced modulo that width toward a value in range. Cost: a program ported
  from C# computes different values without a word.

The lean is (a): the owner took the operators from C# with their meaning, and (b) is a silent
difference.

### T22 (decided, §4) Operators on bits

`&`, `|`, `^`, `~`, `<<` and `>>` work on the bits of integers, with the meaning and the
precedence they have in C#. `>>` on a signed integer keeps the sign. A shift is not arithmetic
in the sense of T4 to T6: bits that leave the type are dropped in every build, and the count of
a shift is taken modulo the width of the type, so `1 << 33` on an `int` is `1 << 1`. `|` also
separates the cases of a union type (D9). The two meanings never meet: one stands between types,
the other between values. What the width is for an operand narrower than `int`, and what a
negative count does, is T28 as well.

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
