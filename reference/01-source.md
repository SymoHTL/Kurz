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

Inside a string literal, `{expression}` is replaced by the text (A2 to A8) of the expression's value.

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
Whether these five are all the escapes there are, and what a backslash before any other character
is, nobody has chosen: L16.

Case: [source/escapes.kz](../corpus/source/escapes.kz)
```kurz
print("a \{b}")
print("say \"hi\"")
```

### L16 (open) The other escapes

C# has more escapes than L7 lists (`\r`, `\0`, `\a`, `\b`, `\f`, `\v`, `\uXXXX`, `\xH`), and a
backslash before any other character (`\}`, `\q`) is a compile error there. The options:

- (a) L7's five are complete, and a backslash before any other character is the compile error
  `unknown-escape`. Cost: `\r` and `\0` have to be written as `{'\r'}`-like expressions or taken
  from the library.
- (b) The whole set of C#, with the same compile error for the rest. Cost: the escapes a C#
  developer knows, and one more thing a lexer has to carry.

The lean is (b): the owner's background is C#, and nothing in the record argues against it.

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

An integer literal without a suffix has the type `int`, or `long` when its value does not fit an
`int`, or `ulong` when it does not fit a `long`, as in C#; a literal that fits no integer type is
the compile error `constant-overflow` (T6). A suffix (L11) gives the literal the type the suffix
names. Where a type is written or expected, the literal takes that type if its value fits, as a
constant does in C#; how far an expected type reaches is L17. How a literal is written is L11.

Case: [source/literal-too-large.kz](../corpus/source/literal-too-large.kz)
```kurz
print(99999999999999999999)
```

Case: [source/integer-literal.kz](../corpus/source/integer-literal.kz)
```kurz
byte b = 200
long big = 5
huge = 4000000000
print(b)
print(big)
print(huge)
```

### L17 (proposed) How far an expected type reaches

A type that is written or expected reaches every literal of an expression that is made only of
literals and the operators of T18 and T22, so `long big = 2147483647 + 1` is a `long` and not
`constant-overflow` (T6), as C# treats a constant expression. It also reaches a literal that is
one operand of such an operator whose other operand has a type: with `uint u`, the `1` of `u + 1`
is a `uint`, which keeps T9 out of ordinary arithmetic, and the `100` of `sbyte low = -100`
takes `sbyte` with its sign. C# converts the constant `int` instead, which Kurz cannot do across
signedness (T9); this is the smallest rule that lets the cases of T3 and T9 stand.

Case: [source/literal-takes-operand-type.kz](../corpus/source/literal-takes-operand-type.kz)
```kurz
uint u = 5
print(u + 1)
long big = 2147483647 + 1
print(big)
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
it and is an ordinary name in every other file. Which words are the core words is L18: until it
is answered, a compiler can only be sure about the words this reference uses as syntax.

Case: [source/reserved-word.kz](../corpus/source/reserved-word.kz)
```kurz
data Job(int match)

print(Job(1) == Job(2))
```

### L18 (open) The list of core words

L12 reserves "the core words" and names five of them. Two compilers that each pick their own list
accept different programs. The options:

- (a) The list is the words this reference uses as syntax, closed here: `if`, `else`, `match`,
  `for`, `in`, `while`, `break`, `continue`, `return`, `throw`, `mut`, `data`, `class`,
  `interface`, `enum`, `flags`, `with`, `pub`, `prot`, `static`, `override`, `weak`, `raw`,
  `use`, `void`, `true`, `false` and `null`, plus the words the parts outside this reference
  add, each listed where that part is specified. Cost: the list has to be kept.
- (b) The keywords of C#, whether Kurz uses them or not. Cost: words Kurz never uses (`goto`,
  `unsafe`, `checked`) are taken from programs for nothing.

The lean is (a). The names of the built-in types (`int`, `string`, ...) are not reserved under
either option, as in C# they are contextual in all but name; whether they should be is part of
the same answer.

### L13 (decided, §8) A string over several lines

A string that holds line breaks is written as a raw string literal is in C# 11. It opens with
`"""` at the end of a line and closes with `"""` on a line of its own. The text starts on the
line after the opening and ends before the closing line; neither of those two line breaks
belongs to it. The indentation of the closing line is removed from every line of the text. A
line of the text that does not start with that indentation is the compile error
`block-indentation`; an empty line is exempt. An ordinary literal ends on the line it starts on.
What `{` and `\` mean inside the block is L15; the cases hold neither.

Case: [source/multi-line-string.kz](../corpus/source/multi-line-string.kz)
```kurz
text = """
    SELECT name
      FROM users
    """
print(text)
```

Case: [source/multi-line-string-indentation.kz](../corpus/source/multi-line-string-indentation.kz)
```kurz
text = """
        SELECT name
    FROM users
        """
print(text)
```

### L14 (decided, §4) Literals with a unit

A number directly followed by a unit is a duration or a size. The list of units is fixed: `ms`,
`s`, `min`, `h` and `days` make a duration (T13); `kb`, `mb` and `gb` make a number of bytes, an
integer literal (L10) in steps of 1024.

Case: [source/unit-literals.kz](../corpus/source/unit-literals.kz)
```kurz
print(2kb)
print(1mb)
```

### L15 (decided, §8) What is special inside a `"""` block

Inside a `"""` block (L13), `{expression}` is replaced (L6) and `\` starts an escape (L7), as in
every other literal. A brace that starts no interpolation is written `\{`. This is where the
block leaves the raw string of C#, which does neither unless it is marked with `$`. A `"` inside
the block is an ordinary character, as it is there, and needs no `\`.

Case: [source/multi-line-string-interpolation.kz](../corpus/source/multi-line-string-interpolation.kz)
```kurz
table = "users"
text = """
    \{ "table": "{table}" }
    a\\b
    """
print(text)
```
