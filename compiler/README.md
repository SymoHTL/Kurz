# The Kurz compiler

The compiler reads the language the reference under `reference/` states, rule by rule, and the
corpus under `corpus/` is what it is measured against. This directory holds a .NET solution:

- `Kurz.Compiler/`: the library. `Text/` reads a source file (L1: UTF-8, a byte-order mark
  skipped, the two-byte line break of Windows editors read as one, in a `"""` block too), `Syntax/`
  turns it into tokens and a syntax tree (the syntax of chapters 1 to 11: every declaration,
  statement and expression form the reference shows), `Corpus/` reads the header of a corpus case
  in the shape `reference/00-about.md` states.
- `Kurz.Compiler.Tests/`: xunit tests. Every Kurz program a test reads stands inline in the test,
  and the corpus is read where it lies: the tree gate admits no `.kz` file under `compiler/`. One
  suite runs every corpus case through the front end and checks that the errors it reports are
  exactly the ones the case's header expects of it.

## Building and testing

The SDK is pinned in `global.json`, every package in `Directory.Packages.props` at an exact
version, and `NuGet.config` names the one package source; a restore runs in locked mode against
the lock files beside each project.

    dotnet test compiler/Kurz.sln -c Release -p:RestoreLockedMode=true

The gate `compiler-tests` (`tools/compiler_gate.py`) runs that command, refuses a skipped test
and a run with fewer passed tests than its floor, and is part of every gates run.

## What the compiler says about a program

A diagnostic is one of two kinds. A compile error carries an id from the table of
`reference/12-errors.md` and the line the reference's E1 names; a corpus case pins both and never
the message, which is free to improve. The front end raises six ids of the table, `syntax`,
`semicolon`, `reserved-word`, `braces-required`, `unknown-escape` and `block-indentation`, and
L1's `invalid-source`, which no case can expect; every other id of the table is the checker's.
A construct the reference states and this compiler does not implement is reported as
`not supported`, with no id, so that it can never pass for an error a case expects, and a program
that gets one is not judged; nothing produces it yet. One corpus case, `data/flags-match.kz`,
expects a `syntax` that only the checker can raise, since it depends on the type of the matched
value (D13); the corpus theory names it as the checker's.

A declaration whose type is a user type is parsed speculatively, since its first token is a plain
name. Once its shape is clear (`Type name =`, `Type name(`, `Type name<`) the parse is committed:
an error after that is reported where it stands, and what was reported before it (a
`reserved-word` on the name) is kept. A speculation that turns out to be no declaration drops what
it reported and undoes the `>>` it may have split for a type-argument list, and the statement is
parsed again as an expression; so each error reaches the bag exactly once.

## Readings the reference leaves open

Each of these is the front end's reading, not a decision; a design round can ask about the ones
that sit under a rule by its id. Every one is pinned by a test.

- L7, L16: a `\u`, `\U` or `\x` escape whose digits name a surrogate (U+D800 to U+DFFF) or a value
  beyond U+10FFFF is `unknown-escape`. L16's text calls `\u` with four digits valid, so the
  surrogate half goes beyond it: a UTF-8 string cannot hold a lone surrogate (L1).
- L12: a core word followed by `=` at the start of a statement declares a variable of that name,
  with the one `reserved-word` error, whatever the word (`if = 1` as much as `equal = 1`). Where an
  expression stands, a core word that starts a statement or a declaration (`if`, `else`, `while`,
  `for`, `in`, `break`, `continue`, `return`, `throw`, `data`, `class`, `interface`, `enum`,
  `flags`, `raw`, `use`, `with`) is `syntax` at its line; `match` starts a match expression, or a
  match statement at the start of a statement, when a token that can start a value follows it (a
  name, a core word, a literal, `(`, `-`, `!`, `~`), and is a name otherwise, so `match.Add(1)` reads as a
  call and `match - 1` as a match that fails; every other core word (`equal`, `by`, the modifiers,
  `void`, `weak`, the type names) is read as a name, so `print(equal)` after `equal = 1` adds no
  error and the declaration carries the one error, as E1 asks. By L12's letter the use would be an
  error too, and an undeclared `print(equal)` gets no error from the front end: the checker's name
  lookup says what it says about any unknown name. An untyped lambda parameter that is a core word
  (`by => by.Id`) and a core word as the name of a named argument (`f(by: 1)`) are `syntax`, where
  the typed form `(int by) => ...` is `reserved-word`.
- L5: a `;` that ends a statement, starts one or follows one is `semicolon`, one error for a run of
  them, and the statement after it parses on its own. Any other `;` is `syntax`: inside
  parentheses, brackets or an interpolation, and where an operand, a name, a member, a parameter or
  a match arm is expected, as the case `control/c-style-for.kz` expects (E4), though L5's letter
  calls every `;` `semicolon`.
- L4: a line that starts with `?.` continues the statement, as one that starts with `.` does; a
  line break inside the braces of an `enum`, `flags` or `with` body, and inside the `< >` of type
  arguments or type parameters, does not end the statement, as inside parentheses, though L4 names
  parentheses and brackets alone; a line break after the `:` of a base list continues the
  declaration; and empty lines and comment lines between a statement and the line that starts with
  `.` or `?.` do not end it, though L4 says the next line; and a line that ends in the `|` of a
  type or in `..` or `..<`, which the reference does not call binary operators, continues on the
  next as after one. A trailing comma is accepted in an `enum` or `flags` body and refused in
  `with { }`.
- C1: `else`, `while`, `for`, `raw` and a constructor followed by a statement without a block, or
  by nothing, are `braces-required` at the line where the header ends (the word, the name, or the
  last line of a condition over several lines), as `if` is, where `match`, `with` and an `enum`
  or `flags` body without their `{` are `syntax`; `else` on a line of its own after the closing
  brace is `syntax` at its line, and the branch still parses. A `{` on the line after the condition reports the line where the condition ends, which
  for a condition over several lines is its last line, the line where the block should have opened.
  `braces-required` is also raised where a block opens on the line after the construct that takes
  it: the body of a function outside a type body or of a constructor (at the first line of its
  signature, where an `if` reports the last line of its condition), the body of a `while` or a
  `for` (at the last line of its header), the arms of a `match` or of the `else` after a call
  (at its line), an `else` and a `raw` block (at the line of the word), and the body of a `class`, `data`, `interface`, `enum` or
  `flags` and the `{ }` of `with` (at the line of the name or the word), after which the body still
  parses; chapter 12 lists the id for C1 alone, and E4 would call these `syntax`. A method
  signature inside a type body that ends its line is the bodiless form of an interface member, so
  a `{` on the next line is `syntax` at its own line.
- L13, E1: text after the closing `"""` of a block, and `"""` inside a content line of the block,
  is `syntax` at that line, and spaces and tabs after either `"""` are not text; a block that is never closed is `syntax` at its opening line, since the
  whole block is what is wrong; an interpolation that `}` does not close on its line is `syntax`
  at the line of its `{`. A lone carriage return, one no line feed follows, is an ordinary character
  in a literal, except at the end of a block line, where every trailing one is dropped with the
  line's end; outside a literal it is whitespace, so a file whose lines end in a carriage return
  alone is one line. A `)`, `]` or `}` missing at the end of the file is `syntax` at the last line
  that holds code, not at an empty line after it, where E1 can be read either way.
- Recovery (no rule id): after an error the parser skips the rest of the statement, which is the
  rest of its line once every parenthesis, bracket and brace the statement opened is closed again,
  and a block the statement opened, with an `else` clause after it, so that the next statement
  parses on its own; a line the lexer already flagged gets no second `syntax` from the parser,
  inside an interpolation too. After a stray `"""` in a block the lexer passes over the rest of
  the block, up to the line that starts with `"""`, so that the block's text is not read as code.

Six shapes still get two errors, or an uneven one, from the front end, each low: `if x print(1)
else print(2)` is `braces-required` and a `syntax` on one line, because the postfix `else` binds to
the call; `x.match(1)` reports `reserved-word` at the use while `x.match` does not; the interpolation
scan inside a `"""` block counts braces only and does not pass over a string literal nested in a
hole, as the one-line scan does; a `\` as the last character of an unterminated one-line literal is
`unknown-escape` beside the literal's `syntax`; a `"""` block that is never closed takes the
rest of the file with it, under its one `syntax`; and a `class`, `data` or `interface` body whose
`}` is missing reads the top-level statements after it as members, one `syntax` per line of them
before the `syntax` of the missing brace. Each is pinned as it is, so that a fix shows.
