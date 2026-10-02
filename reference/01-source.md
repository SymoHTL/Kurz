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

### L3 (proposed) Comments

`//` starts a comment that runs to the end of the line. Every sample in the record uses it.

Case: [source/comment.kz](../corpus/source/comment.kz)
```kurz
// a comment takes the rest of its line
a = 1    // also after code
print(a)
```

### L4 (open) A statement that continues on the next line

L2 needs an exception for long statements. Options: (a) a statement continues while a bracket is
open, and when the line ends in a binary operator, a comma or `=>`; (b) as (a), and a line that
starts with `.` continues the line before, so that a chain of calls can be broken before the dot;
(c) no continuation outside brackets. Lean: (b), because chains of calls are the common long
statement and C# breaks them before the dot.

The record's samples only break lines inside brackets and after `{`.

### L5 (open) Several statements on one line

Section 8 of the record says there are no semicolons. Two of its samples use `;` to put several
statements, or several fields, on one line (section 3: the `Node` class; section 13: the
`Trial` method). Options: (a) no separator exists and those samples are rewritten; (b) `;` is
allowed between statements on one line and nowhere else. Lean: (a), because one way to end a
statement is less to explain, and a formatter would split the line anyway.

Until this is answered the corpus writes one statement per line and puts every block on lines of
its own.

### L6 (decided, §8) Interpolation is always on

Inside a string literal, `{expression}` is replaced by the text (A2) of the expression's value.

Case: [source/interpolation.kz](../corpus/source/interpolation.kz)
```kurz
name = "Kurz"
count = 3
print("hello {name}")
print("{count} items")
print("next {count + 1}")
```

### L7 (proposed) Escapes

Inside a string literal, `\n`, `\t`, `\"` and `\\` mean what they mean in C#, and `\{` is a brace
that does not start an interpolation. A `}` outside an interpolation is an ordinary character.

Case: [source/escapes.kz](../corpus/source/escapes.kz)
```kurz
print("a \{b}")
print("say \"hi\"")
```

### L8 (proposed) `true` and `false`

`true` and `false` are the two values of the type `bool`. `null` is covered by N1.

Case: [source/bool-literals.kz](../corpus/source/bool-literals.kz)
```kurz
yes = true
no = false
print(yes)
print(no)
```

### L9 (proposed) Names

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

### L10 (proposed) Integer literals

An integer literal is written in decimal digits. It has the type `int`, or `long` when its value
does not fit an `int`. Where a type is written or expected, the literal takes that type if its
value fits, as a constant does in C#.

Case: [source/integer-literal.kz](../corpus/source/integer-literal.kz)
```kurz
byte b = 200
long big = 5
huge = 4000000000
print(b)
print(big)
print(huge)
```

### L11 (open) Other literals

How these are written: numbers with a fraction and their type; hexadecimal and binary digits;
digit separators; durations and sizes, which the record's samples write as `5min`, `30s`,
`60days` and `256kb` without listing the units; strings over several lines. Lean: C# forms for
the numbers, and a fixed list of unit suffixes that the record names once.

### L12 (open) Reserved words

Which words cannot be used as names. Section 9 of the record lets a user-defined keyword start
with its own word, so the set is not fixed by the compiler alone. Options: (a) only the core
words are reserved, and a user keyword is reserved in the files that import it; (b) a keyword
is recognised by its position at the start of a statement, and stays usable as a name elsewhere.
Lean: (a), because the editor rule of section 9 ("a file names the keywords it uses") already
gives the list per file.
