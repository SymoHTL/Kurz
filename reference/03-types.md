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

`byte`, `short`, `int` (32 bits), `long` (64 bits), `float`, `double` and `decimal`, with the
sizes these names have in C#.

Case: [types/long.kz](../corpus/types/long.kz)
```kurz
long big = 2147483647
print(big + 1)
```

### T3 (open) The other number types of C#

The record lists seven names. C# also has `sbyte`, `ushort`, `uint`, `ulong` and `char`. The
record forbids mixing signed with unsigned, and of the seven only `byte` is unsigned. Options:
(a) the seven names are the whole set; (b) the C# set. Lean: (b), because hashes, sizes and wire
formats need unsigned 32 and 64 bits.

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

Putting a value of a wider integer type into a narrower one without saying so is the compile
error `narrowing`.

Case: [types/narrowing.kz](../corpus/types/narrowing.kz)
```kurz
long big = 5
int small = big
print(small)
```

### T8 (proposed) Implicit widening

A value of a narrower integer type becomes a wider one of the same signedness without a word, as
in C#.

Case: [types/widening.kz](../corpus/types/widening.kz)
```kurz
int small = 5
long big = small
print(big)
```

### T9 (decided, §4) No mixing of signed and unsigned

An operation between a signed and an unsigned integer is the compile error `sign-mix`.

Case: [types/sign-mix.kz](../corpus/types/sign-mix.kz)
```kurz
byte b = 200
int i = 5
print(b + i)
```

### T10 (open) How a conversion is written

T7 and T9 need a way to convert on purpose. Options: (a) a cast, `(int)value`, as in C#; (b) the
type name as a function, `int(value)`; (c) a method, `value.ToInt()`. Also open: whether a
conversion that loses the value throws, wraps or follows the build as overflow does (T4, T5).
Lean: (b), shortest and no new bracket form; and it follows the build, like any overflow.

### T11 (decided, §4) Wrapping on purpose

An explicit wrapping operator exists for intended cases such as hashes. It wraps in every build.

No case: the operator has no spelling yet (T12).

### T12 (open) The spelling of the wrapping operator

Options: (a) operators of their own, `+%`, `-%`, `*%`, as Zig has; (b) a block, `wrapping { }`,
inside which arithmetic wraps; (c) methods, `a.WrappingAdd(b)`. Lean: (a): a hash function is a
line of operators, and a block or a method call doubles its length.

### T13 (decided, §4) Durations and timestamps

Durations and timestamps are 64-bit types of their own and never raw integers.

No case: their literals have no spelling yet (L11).

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

### T16 (open) What `.Chars` yields

Options: (a) Unicode scalar values, one per code point; (b) grapheme clusters, what a reader
calls a character; (c) both, under two names. Also open: the name of the element type. Lean:
(a) for `.Chars`, with graphemes as a library function, because a grapheme table changes with
every Unicode version and does not belong on a microcontroller.

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

### T19 (open) Declaring generic types and functions

T17 covers using a generic type. How one is declared is a missing detail of the record
(section 14): the list of type parameters, and how a parameter is limited to types that can do
something. C# writes the limit as `where T : IComparable<T>`. In Kurz `where` already starts a
constraint on a value (`int Age where 0..150`, record section 13), so the word would mean two
things. Options: (a) the C# form, with the two uses of `where` told apart by what follows;
(b) the limit inside the brackets, `T Max<T: Comparable>(T a, T b)`; (c) no limits: the body of a
generic function may only do what every type can. Lean: (b): one meaning per word, and the limit
stands where the parameter is declared.

### T18 (proposed) Operators

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
