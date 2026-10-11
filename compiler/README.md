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
version in NuGet's bracket form, and `NuGet.config` names the one package source; a restore runs
in locked mode against the lock files beside each project, and audits no advisory feed
(`NuGetAudit` is off in `Directory.Build.props`: that data comes from the network, and no file
pins it).

    dotnet test compiler/Kurz.sln -c Release -p:RestoreLockedMode=true

The gate `compiler-tests` (`tools/compiler_gate.py`) checks that `NuGet.config` clears the
inherited sources and maps the packages, runs that command, refuses a skipped test and a run
with fewer passed tests than its floor, and is part of every gates run.

## What the compiler says about a program

A diagnostic is one of two kinds. A compile error carries an id from the table of
`reference/12-errors.md`, or the one id a rule names and keeps out of the table, L1's
`invalid-source` (the table lists the ids a case can expect, and no case can be a file that is not
UTF-8), and the line the reference's E1 names; a corpus case pins both and never the message,
which is free to improve. The front end raises six ids of the table, `syntax`, `semicolon`,
`reserved-word`, `braces-required`, `unknown-escape` and `block-indentation`, and `invalid-source`;
every other id of the table is the checker's. A construct the reference states and this compiler
does not implement is reported as `not supported`, with no id, so that it can never pass for an
error a case expects; a corpus case that gets one fails its theory, and nothing produces it. One
corpus case, `data/flags-match.kz`, expects a `syntax` that only the checker can raise, since it
depends on the type of the matched value (D13); the corpus theory checks that it parses with no
diagnostic and leaves its expectation to the checker.

A declaration whose type is a user type is parsed speculatively, since its first token is a plain
name, and so is one whose type is a function type (F11), since it starts with `(` as an expression
does. Once its shape is clear (`Type name =`, `Type name(`, `Type name<`) the parse is committed:
an error after that is reported where it stands, and what was reported before it (a
`reserved-word` on the name) is kept. A speculation that turns out to be no declaration drops what
it reported and undoes the `>>` it may have split for a type-argument list, and the statement is
parsed again as an expression; so each error reaches the bag exactly once. Inside parentheses or
brackets, where L4 passes a line break over, arms or braces that open on the next line are
`braces-required` all the same: C1 raises the error wherever a block opens on the next line.

## Readings the reference leaves open

Each of these is the front end's reading, not a decision; a design round can ask about the ones
that sit under a rule by its id. Every one is pinned by a test.

- L12: which core words are `syntax` where an expression stands. The seventeen words that begin a
  statement form (`if`, `else`, `while`, `for`, `in`, `break`, `continue`, `return`, `throw`,
  `data`, `class`, `interface`, `enum`, `flags`, `raw`, `use`, `with`) are `syntax` at their line
  there, as the record's `print(if)` is; every other core word (`equal`, `by`, the modifiers,
  `void`, `weak`, the type names) is read as a name there, so `print(equal)` after `equal = 1` adds
  no error and the declaration carries the one error, as E1 asks; an untyped lambda parameter and
  the name of a named argument that are core words are `syntax`, as the record decides, whatever
  the word. By L12's letter the use would be an error too, and an undeclared `print(equal)` gets no
  error from the front end: the checker's name lookup says what it says about any unknown name. A
  core word after a dot is a member name, `x.match(1)` and `x.int` alike, with no error at the use.
- L13: a closing line of four quotes closes the block at its three and opens a one-line literal
  that nothing closes, so it is `syntax` at that line, where the record speaks of four quotes at the
  end of a line only.
- E1: bad tokens with no readable token between them are one `syntax` on their line, so
  `c = 'ab' 'cd'` is one error and `c = 'ab' + 'cd'` two; E1 says one error for text no rule gives a
  meaning and does not say where one run ends.
- C8: where `..` and `..<` bind. The front end binds them below `+`, `-`, `<<` and `>>` and above
  the comparisons, so `1..n + 1` runs to `n + 1` and `a < 1..n` compares `a` with the range, and a
  range has two ends and chains with nothing, so `1..2..3` is `syntax`; C8 states the range and not
  its place among the operators, and C# binds its own `..` above `*`.
- F1: a function declared inside a block, `int G() => 1` in the body of `F`, parses as one at the
  top level does; the reference places functions in a file (F5) and in a type body (K7) and says
  nothing of a block, and the checker says what the declaration means there.
- K7 with V2: `mut count = 0` in a type body is a field without a written type, with the modifiers
  written before it; K7 shows a field as `Type name`, V2 speaks of variables, and the record's
  actor bodies write `mut users = Map<int, User>()`.
- T17 with T19: how a call's type arguments are told from comparisons. The front end reads
  `a < b, c > (d)` as the call `a<b, c>(d)` and `a < b, c > d` as two comparisons, as C# reads
  them by what follows the `>`; T17 says generic types are written as in C# and states no such
  rule.
- E4's general clause, applied to shapes no rule names: a modifier written twice (K7: class bodies
  follow C#, which refuses it), type parameters on an `enum` or `flags` type (D12 and D13 give them
  a name and a body), a second `?` on a type (N1 gives one), a form feed or a vertical tab outside a
  literal (L13 names the carriage return alone) and a `//` inside an interpolation (L3 runs a comment
  to the end of its line, which L6's hole does not reach) are each `syntax`, as text no rule gives
  a meaning; an error of the type checker could be meant for the first three, and E4 names the
  parser's id for none of them.
- Recovery (no rule id): after an error the parser skips the rest of the statement, which is the
  rest of its line once every parenthesis, bracket and brace the statement opened is closed again,
  and a block the statement opened, with an `else` clause after it, so that the next statement
  parses on its own; a statement in place of a block (C1) is read as the block's one statement
  after the error; a line the lexer already flagged gets no second `syntax` from the parser,
  inside an interpolation too. After a stray `"""` in a block, or text beside an opening `"""`
  that no `"""` on the same line closes, the lexer passes over the rest of the block, up to the
  closing `"""`, so that the block's text is not read as code, and reads what follows the
  closing quotes on their line as code.

Five shapes still get two errors, or an uneven one, from the front end, each low: `if x print(1)
else print(2)` is `braces-required` twice on one line, because the postfix `else` binds to the call
and takes arms of its own; the interpolation scan inside a `"""` block counts braces only and does
not pass over a string literal nested in a hole, as the one-line scan does; a `\` as the last
character of an unterminated one-line literal is `unknown-escape` beside the literal's `syntax`;
a `"""` block that is never closed takes the rest of the file with it, under its one `syntax`, and
so does a block whose content line holds `"""` after text, or whose opening line holds text or a
fourth quote, when no line that starts with `"""` follows; and a `class`, `data` or `interface` body whose `}` is missing reads the top-level
statements after it as members, one `syntax` per line of them before the `syntax` of the missing
brace. Each is pinned as it is, so that a fix shows.
