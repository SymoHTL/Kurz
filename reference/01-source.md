# 1. Source text

### L1 (decided, §1) Files

A source file has the extension `.kz`.

No case: it is a property of the file name, and every file in the corpus has it.

### L2 (decided, §8) A newline ends a statement

There are no semicolons. A statement ends where its line ends.

Case: [source/newline-ends-statement.kz](../corpus/source/newline-ends-statement.kz)
```kurz
a = 1
print(a)
print(a + 1)
```

### L3 (assumed, §8) Comments

`//` starts a comment that runs to the end of the line. Every sample in the record uses it.

Case: [source/comment.kz](../corpus/source/comment.kz)
```kurz
// a comment takes the rest of its line
a = 1    // also after code
print(a)
```

### L4 (decided, §8) A statement that continues on the next line

L2 has three exceptions. A statement continues on the next line while a `(` or a `[` is open;
when its line ends in a binary operator, a comma or `=>`; and when the next line starts with `.`,
so that a chain of calls can be broken before the dot. Nothing else continues a statement. A
block between braces holds statements of its own, each on its line, also when the block stands
inside an open bracket, as the body of a lambda does. The record marks as *(assumed)* that the
`=` of an assignment counts as a binary operator here.

Case: [source/continuation.kz](../corpus/source/continuation.kz)
```kurz
int Add(int a, int b) =>
    a + b

total = 1 +
    2 +
    3
print(total)

mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
count = xs
    .Where(x => x > 1)
    .Count
print(count)

print(Add(
    total,
    total
))
```

### L5 (decided, §8) No `;`

There is no `;`: not at the end of a line and not between two statements. A `;` outside a string
and a comment is the compile error `semicolon`. A line holds one statement, and a block that is
written on one line holds at most one.

Case: [source/semicolon.kz](../corpus/source/semicolon.kz)
```kurz
a = 1
print(a); print(a + 1)
```

### L6 (decided, §8) Interpolation is always on

Inside a string literal, `{expression}` is replaced by the text (A2, A3) of the expression's value.

Case: [source/interpolation.kz](../corpus/source/interpolation.kz)
```kurz
name = "Kurz"
count = 3
print("hello {name}")
print("{count} items")
print("next {count + 1}")
```

### L7 (assumed, §8) Escapes

Inside a string literal, `\n`, `\t`, `\"` and `\\` mean what they mean in C#, and `\{` is a brace
that does not start an interpolation. A `}` outside an interpolation is an ordinary character.

Case: [source/escapes.kz](../corpus/source/escapes.kz)
```kurz
print("a \{b}")
print("say \"hi\"")
```

### L8 (assumed, §8) `true` and `false`

`true` and `false` are the two values of the type `bool`. `null` is covered by N1.

Case: [source/bool-literals.kz](../corpus/source/bool-literals.kz)
```kurz
yes = true
no = false
print(yes)
print(no)
```

### L9 (assumed, §8) Names

A name starts with a letter or `_` and continues with letters, digits and `_`, as in C#. Upper
and lower case are different.

Case: [source/names.kz](../corpus/source/names.kz)
```kurz
total = 1
Total = 2
print(total + Total)
_count2 = 7
print(_count2)
```

### L10 (assumed, §4) The type of an integer literal

An integer literal has the type `int`, or `long` when its value does not fit an `int`. Where a type is written or expected, the literal takes that type if its
value fits, as a constant does in C#. How a literal is written is L11.

Case: [source/integer-literal.kz](../corpus/source/integer-literal.kz)
```kurz
byte b = 200
long big = 5
huge = 4000000000
print(b)
print(big)
print(huge)
```

### L11 (decided, §4) Number literals

Number literals are written as in C#: decimal digits; hexadecimal digits after `0x` and binary
digits after `0b`; `_` between digits, which changes nothing; a fraction after a `.`, which makes
the literal a `double`; and the suffixes of C# (`L`, `U`, `UL`, `f`, `d`, `m`) with the meaning
they have there.

Case: [source/number-literals.kz](../corpus/source/number-literals.kz)
```kurz
print(0xFF)
print(0b101)
print(1_000_000)
print(4_000_000_000L)
half = 0.5
print(half < 1.0)
```

### L12 (decided, §8) Reserved words

Only the core words are reserved: the words that the language itself uses as syntax, such as
`if`, `match`, `mut`, `data` and `class`. Using one as a name is the compile error
`reserved-word`. A user-defined keyword (record, section 9) is reserved in the files that import
it and is an ordinary name in every other file. The list of core words closes when the chapters
that this reference does not cover yet are written.

Case: [source/reserved-word.kz](../corpus/source/reserved-word.kz)
```kurz
data = 5
print(data)
```

### L13 (open) A string over several lines

How a string literal that holds line breaks is written. Options: (a) not at all: a line break in
a string is written `\n` (L7); (b) a literal between two lines of `"""`, as C# has, from which
the indentation of the closing line is removed; (c) an ordinary literal may run over several
lines. Lean: (b). SQL and HTML inside a program are text over several lines, and under (c) one
forgotten quote swallows the rest of the file.

### L14 (assumed, §4) Literals with a unit

A number directly followed by a unit is a duration or a size. The list of units is fixed: `ms`,
`s`, `min`, `h` and `days` make a duration (T13); `kb`, `mb` and `gb` make a number of bytes, an
integer literal (L10) in steps of 1024. That the list is fixed is decided. Which units are on it
is what the record marks as *(assumed)*.

Case: [source/unit-literals.kz](../corpus/source/unit-literals.kz)
```kurz
print(2kb)
print(1mb)
```
