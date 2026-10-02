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

The same arithmetic raises an exception in a test build.

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
compile error `narrowing`.

Case: [types/narrowing.kz](../corpus/types/narrowing.kz)
```kurz
long big = 5
int small = big
print(small)
```

### T8 (assumed, §4) Implicit widening

A value of a narrower integer type becomes a wider one of the same signedness without a word, as
in C#.

Case: [types/widening.kz](../corpus/types/widening.kz)
```kurz
int small = 5
long big = small
print(big)
```

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
release build the value wraps around, in a test build it raises an exception.

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
(L14).

Case: [types/duration-not-integer.kz](../corpus/types/duration-not-integer.kz)
```kurz
int seconds = 30s
print(seconds)
```

### T14 (assumed, §4) Out of range and division by zero

An index that is out of range and a division by zero raise an exception.

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

### T16 (decided, §4) What `.Chars` yields

`.Chars` yields the Unicode code points of the string, one element for each code point. It
yields neither UTF-16 units, as a C# string does, nor what a reader calls a character: grapheme
clusters are left to a library.

Case: [types/string-chars.kz](../corpus/types/string-chars.kz)
```kurz
s = "aä😀"
print(s.Chars.Count)
print(s.Bytes.Count)
```

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
(record, section 13). A use names the type arguments, or leaves them to be inferred where C#
infers them. An argument whose type does not meet the limit is the compile error `type-mismatch`.

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

### T18 (assumed, §4) Operators

`+ - * / %` on numbers, `== != < <= > >=` for comparison, and `&& || !` on `bool`, with the
meaning and the precedence they have in C#. Integer division drops the fraction, toward zero.

Case: [types/arithmetic.kz](../corpus/types/arithmetic.kz)
```kurz
print(1 + 2 * 3)
print(7 / 2)
print(-7 / 2)
print(7 % 3)
print(1 < 2 && 2 < 3)
print(!(1 == 1) || 3 >= 3)
```

### T22 (decided, §4) Operators on bits

`&`, `|`, `^`, `~`, `<<` and `>>` work on the bits of integers, with the meaning and the
precedence they have in C#. `>>` on a signed integer keeps the sign. A shift is not arithmetic
in the sense of T4 to T6: bits that leave the type are dropped in every build, and the count of
a shift is taken modulo the width of the type, so `1 << 33` on an `int` is `1 << 1`. `|` also
separates the cases of a union type (D9). The two meanings never meet: one stands between types,
the other between values.

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
