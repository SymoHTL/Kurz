# 1. Source text

### L1 (decided, §1, §8) Files

A source file has the extension `.kz`. It is read as UTF-8, and a string literal holds the code
points written in it, as they are written, without normalization: the case below prints such a
literal as written, and the string cases of T15 and T16 count its characters and bytes. A file that
is not valid UTF-8 is one compile error, `invalid-source`, at the line of the first bad byte; an
encoded surrogate is one, since it is not valid UTF-8. A byte-order mark at the start of the file is
skipped, because some Windows editors and tools write one (the owner, 2026-10-09, against refusing
the mark too, under which a file saved with the mark does not compile; the cost is one accepted
leading code point that is not part of the text). The error has no case: a corpus file is read as UTF-8 by the lint and by every
gate, so no case can be a file that is not, and the id stands here and not in the table of chapter
12, which lists the ids cases expect.

Case: [source/utf8-literal.kz](../corpus/source/utf8-literal.kz)
```kurz
print("aä")
```

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

Inside a string literal, `{expression}` is replaced by the text (A2 to A15) of the expression's value.

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
The other escapes of C#, and a backslash before any other character, are L16.

Case: [source/escapes.kz](../corpus/source/escapes.kz)
```kurz
print("a \{b}")
print("say \"hi\"")
```

### L16 (decided, §8) The other escapes

The other escapes of C# 12 mean what they mean there: `\'`, `\0`, `\a`, `\b`, `\f`, `\r`,
`\v`, `\u` followed by four hex digits, `\U` followed by eight that name a code point up to
U+10FFFF, and `\x` followed by one to four. A backslash before any other character (`\}`, `\q`),
or before a `u`, `U` or `x` that is not followed by the digits it takes (`\u12`, `\xg`,
`\U00110000`), is the compile error `unknown-escape`, as each is in C# 12. The owner chose this
on 2026-10-03, against L7's five alone, which would have put `\r` and `\0` into the library; the
cost is one more thing a lexer has to carry. *(assumed: the version; the `\e` that C# 13 added is
`unknown-escape` under it)*

Case: [source/escapes-of-c-sharp.kz](../corpus/source/escapes-of-c-sharp.kz)
```kurz
print("\u0041\x42|\U00000043")
```

Case: [source/escape-too-short.kz](../corpus/source/escape-too-short.kz)
```kurz
print("\u12")
```

Case: [source/unknown-escape.kz](../corpus/source/unknown-escape.kz)
```kurz
print("a\q")
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

A name starts with a letter or `_` and continues with letters, digits and `_`. A letter is one of
ASCII's, `a` to `z` and `A` to `Z`: a name that holds any other character, an umlaut included, is
the compile error `syntax` (E4), and C#'s `@` names do not exist (the owner, 2026-10-09, against any
Unicode letter as C# allows, under which a Latin and a Cyrillic `a` are two names nobody can tell
apart, and against `@keyword` names, a second spelling C# has for interop, which Kurz has no rule
for; the cost is that a C# program with an umlaut in a name is edited). Upper and lower case are
different. The owner chose the letter set; the rest of the form, the start with a letter or `_`,
the letters, digits and `_` after it and the two cases, is the record's assumption, so the rule is
assumed.

Case: [source/names.kz](../corpus/source/names.kz)
```kurz
total = 1
Total = 2
print(total + Total)
_count2 = 7
print(_count2)
```

Case: [source/name-non-ascii.kz](../corpus/source/name-non-ascii.kz)
```kurz
zähler = 1
print(1)
```

Case: [source/name-at.kz](../corpus/source/name-at.kz)
```kurz
@total = 1
print(1)
```

### L10 (assumed, §4) The type of an integer literal

An integer literal without a suffix has the type `int`, or `long` when its value does not fit an
`int`, or `ulong` when it does not fit a `long`, as in C#; a literal that fits no integer type is
the compile error `constant-overflow` (T6). A suffix (L11) gives the literal the type the suffix
names. Where a type is written or expected, the literal takes that type if its value fits, as a
constant does in C#, and is `constant-overflow` (T6) when it does not, `byte c = 300`, never an `int`
narrowed (T7); how far an expected type reaches is L17, which also carries the reading that a literal
which does not fit the type it takes is that error wherever it stands. *(assumed: proposed on 2026-10-10, after the review of round 13)*
How a literal is written is L11.

Case: [source/literal-does-not-fit.kz](../corpus/source/literal-does-not-fit.kz)
```kurz
byte c = 300
print(c)
```

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

### L17 (decided, §4) How far an expected type reaches

A type that is written or expected reaches every literal of an expression that is made only of
literals and the operators of T18 and T22, so `long big = 2147483647 + 1` is a `long` and not
`constant-overflow` (T6); C# folds `2147483647 + 1` as an `int` and refuses it whatever the written
type, so Kurz departs from C# here. It also reaches a literal that is one
operand of such an operator whose other operand has a type: with `uint u`, the `1` of `u + 1` is a
`uint`, which keeps T9 out of ordinary arithmetic, and the `100` of `sbyte low = -100` takes `sbyte`
with its sign. C# converts the constant `int` instead, which Kurz cannot do across signedness (T9);
this is the smallest rule that lets the cases of T3 and T9 stand. The owner confirmed this reading
on 2026-10-09, in round 13. The promotion of narrow operands (chapter 3) is for the operands of a
run-time operation. A constant expression is folded in the type its literals take, one operation at
a time, each operation by the rule of its own operator: under `+ - * / %` an operation whose result
leaves the type is `constant-overflow` (T6), under `+%`, `-%` and `*%` it wraps in that type (T12).
So `sbyte low = -100` and `byte c = 200 + 50` compile, `byte c = 200 + 100` is the error, and so is
`byte c = 200 + 100 - 100`, whose result would fit but whose first sum does not; `byte b = 255 +% 1`
is `0`, and `2147483647 + 1 +% 0` is the error at the `+`. A literal that does not fit the type it
takes is `constant-overflow` wherever it stands: `byte c = 300` (L10), never an `int` narrowed (T7),
and `b + 300` with a `byte` `b`, whose `300` takes `byte` by the sentence above, as much as
`sum +% 300` (T28); the cost is that `b * 1000` on a `byte` is written `int(b) * 1000`. *(assumed: proposed on 2026-10-10, after the review of round 13)*

Case: [source/constant-intermediate-overflow.kz](../corpus/source/constant-intermediate-overflow.kz)
```kurz
byte c = 200 + 100 - 100
print(c)
```

Case: [source/literal-operand-too-big.kz](../corpus/source/literal-operand-too-big.kz)
```kurz
byte b = 1
print(b + 300)
```

Case: [source/constant-does-not-fit.kz](../corpus/source/constant-does-not-fit.kz)
```kurz
byte c = 200 + 100
print(c)
```

Case: [source/literal-takes-operand-type.kz](../corpus/source/literal-takes-operand-type.kz)
```kurz
uint u = 5
print(u + 1)
long big = 2147483647 + 1
print(big)
```

### L11 (decided, §4) Number literals

Number literals take every form C# gives them: decimal digits; hexadecimal digits after `0x` and
binary digits after `0b`, where `_` may also stand directly after the prefix (`0x_FF`); `_` between
digits, which changes nothing; a fraction after a `.` and an exponent after `e` or `E` (`1e3`,
`2.5E-3`), each of which makes a literal without a suffix a `double`; and the suffixes of C# (`L`,
`U`, `UL` in either order, `f`, `d`, `m`), each letter in upper or lower case, so `7uL` is a `ulong`
and `1e3f` a `float`, with the meaning they have there, except that a suffix holding a lower-case `l`
is the compile error `syntax` (E4), because `1l` reads as `11`, where C# only warns (the owner,
2026-10-09, against the forms listed before this round alone, under which `1e-9` is written out in
full; the cost is that the lexer is C#'s). An integer suffix after a fraction or an exponent, `1.5U`,
is no form C# gives, so it is `syntax` (E4). A floating-point literal whose value lies outside its
type's range, `1e400`, is `constant-overflow`, and one that rounds to zero or a subnormal is that
value (T6). *(assumed: proposed on 2026-10-10, after the review of round 13)*

Case: [source/exponent-literal-too-large.kz](../corpus/source/exponent-literal-too-large.kz)
```kurz
x = 1e400
print(x)
```

Case: [source/number-literal-fraction-suffix.kz](../corpus/source/number-literal-fraction-suffix.kz)
```kurz
x = 1.5U
print(x)
```

Case: [source/number-literal-ul-suffix.kz](../corpus/source/number-literal-ul-suffix.kz)
```kurz
x = 1ul
print(x)
```

Case: [source/number-literal-suffix-cases.kz](../corpus/source/number-literal-suffix-cases.kz)
```kurz
print(1L)
print(1UL)
print(7uL)
```

Case: [source/number-literals.kz](../corpus/source/number-literals.kz)
```kurz
print(0xFF)
print(0b101)
print(1_000_000)
print(4_000_000_000L)
half = 0.5
print(half < 1.0)
```

Case: [source/number-literal-forms.kz](../corpus/source/number-literal-forms.kz)
```kurz
print(1e3 == 1000.0)
print(2.5E-3 == 0.0025)
print(0x_FF)
print(0b_101)
print(1.5e2 == 150.0)
print(7u)
```

Case: [source/number-literal-l-suffix.kz](../corpus/source/number-literal-l-suffix.kz)
```kurz
x = 1l
print(x)
```

### L12 (decided, §8) Reserved words

Only the core words are reserved: the words that the language itself uses as syntax, such as
`if`, `match`, `mut`, `data` and `class`. Using one as a name is the compile error
`reserved-word`. A user-defined keyword (record, section 9) is reserved in the files that import
it and is an ordinary name in every other file. The core words are listed in L18.

Case: [source/reserved-word.kz](../corpus/source/reserved-word.kz)
```kurz
data Job(int match)

print(Job(1) == Job(2))
```

### L18 (decided, §8) The list of core words

The core words are the words this reference uses as syntax, and the list is closed here: `if`,
`else`, `match`, `for`, `in`, `while`, `break`, `continue`, `return`, `throw`, `mut`, `data`,
`class`, `interface`, `enum`, `flags`, `with`, `equal`, `by`, `pub`, `prot`, `static`, `this`,
`virtual`, `override`, `weak`, `raw`, `use`, `void`, `true`, `false` and `null`, plus the words
the parts outside this reference add, each listed where that part is specified. The owner chose
this on 2026-10-03, against the keywords of C#, which would take `goto`, `unsafe` and `checked`
from programs for nothing; the cost is that the list has to be kept. Four words were not in the
list the owner saw: `equal` and `by` are core words by K8 (the owner, 2026-10-02), `this` is the
call of the primary constructor (K14) and `virtual` the marker of K7 (both the owner, 2026-10-04).
The names of the built-in types are core words as well (L19).

Case: [source/core-word-as-name.kz](../corpus/source/core-word-as-name.kz)
```kurz
equal = 1
print(equal)
```

### L19 (decided, §8) The names of the built-in types are core words

The names of the built-in types are core words beside L18's list: `sbyte`, `byte`, `short`,
`ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `bool`, `string`, `char`,
`duration`, `timestamp`, `longduration` and `longtimestamp`; the last two are T29's names, which T29 marks
*(assumed)*, accepted by the rule's id on 2026-10-09. Using one as a name is `reserved-word` (L12), as it is in C# for the
first fourteen, which are keywords there; the four time types C# does not reserve. The owner chose
this on 2026-10-04, against ordinary names that a declaration hides in its block, under which
`long(x)` (T10) would have two readings in one program; the cost is that nothing can be called
`string`, and that a ported program with a local called `duration` or `timestamp` has to rename it.

Case: [source/type-name-as-name.kz](../corpus/source/type-name-as-name.kz)
```kurz
int = 1
print(1)
```

### L13 (decided, §8) A string over several lines

A string that holds line breaks is written as a raw string literal is in C# 11. It opens with `"""`
at the end of a line and closes with `"""` on a line of its own. The text starts on the line after
the opening and ends before the closing line; neither of those two line breaks belongs to it. The
indentation of the closing line is removed from every line of the text. A line of the text that does
not start with that indentation is the compile error `block-indentation`; a line that holds only
whitespace is exempt and is an empty line of the text, as in C# 11. An ordinary literal ends on the
line it starts on. `"""` opens a block only at the end of a line: `"""abc"""` on one line, text
after an opening `"""` and text before a closing one are the compile error `syntax` (E4) (the owner,
2026-10-09, against a one-line form holding `abc`, in which a `"` is an ordinary character, as C# 11
has it, and against an id of its own for the malformed shapes; what the one-line form would buy, a
`"` without a backslash, the escape of L7 gives). What `{` and `\` mean inside the block is L15; the
cases hold neither. Only characters other than whitespace count as text beside a `"""`, as in C# 11:
trailing spaces after the opening and the indentation before the closing are no error. Code may
follow the closing `"""` on its line, `""")` closing a call, as C# 11 allows: "on a line of its own"
is about what stands before it. *(assumed: proposed on 2026-10-10, after the review of round 13)*

Case: [source/multi-line-string-in-call.kz](../corpus/source/multi-line-string-in-call.kz)
```kurz
print("""
    hello
    """)
```

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

Case: [source/multi-line-string-one-line.kz](../corpus/source/multi-line-string-one-line.kz)
```kurz
text = """abc"""
print(text)
```

Case: [source/multi-line-string-blank-line.kz](../corpus/source/multi-line-string-blank-line.kz)
```kurz
text = """
    a
  
    b
    """
print(text)
```

### L14 (decided, §4) Literals with a unit

A number directly followed by a unit is a duration or a size. The list of units is fixed: `ms`, `s`,
`min`, `h` and `days` make a duration (T13); `kb`, `mb` and `gb` make a number of bytes, an integer
literal (L10) in steps of 1024. The number is a decimal integer or a decimal fraction, with `_`
between its digits: `1.5s` is 1500 ms, `0.5h` is 30 min and `1.5kb` is 1536. A fraction that does
not fall on a whole nanosecond, or on a whole byte before `kb`, `mb` and `gb`, is the compile error
`inexact-literal`; a hexadecimal, a binary or a suffixed literal before a unit is `syntax` (E4) (the
owner, 2026-10-09, against a decimal integer alone, under which one and a half seconds is written
`1500ms`; the cost is a row in the error table and a decimal-to-nanosecond conversion in the
compiler). The type of a duration literal is T29's. A number with an exponent (L11) before a unit is
`syntax` as well, since the number is a decimal integer or a decimal fraction and nothing else; and
the unit is read before any suffix of L11, the longest unit that matches, so `1ms` is a millisecond
and not `1m` followed by `s`, and only a suffix the writer spells before a unit, `1Ums`, is `syntax`.
*(assumed: proposed on 2026-10-10, after the review of round 13)*

Case: [source/unit-literal-inexact-bytes.kz](../corpus/source/unit-literal-inexact-bytes.kz)
```kurz
print(1.3kb)
```

Case: [source/unit-literal-exponent.kz](../corpus/source/unit-literal-exponent.kz)
```kurz
print(1e3ms)
```

Case: [source/unit-literals.kz](../corpus/source/unit-literals.kz)
```kurz
print(2kb)
print(1mb)
```

Case: [source/unit-literal-fraction.kz](../corpus/source/unit-literal-fraction.kz)
```kurz
print(1.5s)
print(0.5h)
print(1.5kb)
```

Case: [source/unit-literal-inexact.kz](../corpus/source/unit-literal-inexact.kz)
```kurz
print(0.0000000001s)
```

Case: [source/unit-literal-hex.kz](../corpus/source/unit-literal-hex.kz)
```kurz
print(0x10ms)
```

### L15 (decided, §8) What is special inside a `"""` block

Inside a `"""` block (L13), `{expression}` is replaced (L6) and `\` starts an escape (L7), as in
every other literal. A brace that starts no interpolation is written `\{`. This is where the
block leaves the raw string of C#, which never treats `\` as an escape and replaces
`{expression}` only when it is marked with `$`. A `"` inside
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
