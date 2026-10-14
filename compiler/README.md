# The Kurz compiler

The compiler reads the language the reference under `reference/` states, rule by rule, and the
corpus under `corpus/` is what it is measured against. This directory holds a .NET solution:

- `Kurz.Compiler/`: the library. `Text/` reads a source file (L1: UTF-8, a byte-order mark
  skipped, the two-byte line break of Windows editors read as one, in a `"""` block too), `Syntax/`
  turns it into tokens and a syntax tree (the syntax of chapters 1 to 11: every declaration,
  statement and expression form the reference shows), `Semantics/` checks a tree the parser read
  without an error against the rules of chapters 1 to 3, 5, 6 and 8 to 10 (the section "What the
  checker reads" below) and says `not supported` to a construct of the other chapters, `Corpus/`
  reads the header of a corpus case in the shape `reference/00-about.md` states.
- `Kurz.Compiler.Tests/`: xunit tests. Every program the lexer, parser and checker tests read
  stands inline in the test, and the corpus suite reads the cases where they lie, under `corpus/`:
  the tree gate admits no `.kz` file under `compiler/`. That suite runs every corpus case through
  the front end, and every case the front end reads clean through the checker, and checks that
  the errors each reports are exactly the ones the case's header expects of it; a case that uses
  a construct the checker does not read must get `not supported` and no error from it.

## Building and testing

The SDK is pinned in `global.json`, every package in `Directory.Packages.props` at an exact
version in NuGet's bracket form, and `NuGet.config` names the one package source; a restore runs
in locked mode against the lock files beside each project, and audits no advisory feed
(`NuGetAudit` is off in `Directory.Build.props`: that data comes from the network, and no file
pins it).

    cd compiler && dotnet test Kurz.sln -c Release -p:RestoreLockedMode=true --disable-build-servers

From inside `compiler/`: the dotnet host reads `global.json` from the working directory and its
parents, not from the directory of the solution it is given, so the same command from the
repository root would run whatever SDK the machine has. `--disable-build-servers` leaves no
MSBuild node and no compiler server running after the command, so that the gate's timeout can end
the run.

The gate `compiler-tests` (`tools/compiler_gate.py`) reads `NuGet.config` as XML and refuses it
when it is missing, is not well-formed, does not open its package sources with `<clear />` or maps
no package pattern to a source; runs that command from inside `compiler/`; refuses a failed or a
skipped test, a run with fewer passed tests than its floor, no summary line, a non-zero exit with
no failed test, a missing `dotnet` and a run out of time; and is part of every gates run.

## What the compiler says about a program

A diagnostic is one of two kinds. A compile error carries an id from the table of
`reference/12-errors.md`, or the one id a rule names and keeps out of the table, L1's
`invalid-source` (the table lists the ids a case can expect, and no case can be a file that is not
UTF-8), and the line the reference's E1 names; a corpus case pins both and never the message,
which is free to improve. The front end raises six ids of the table, `syntax`, `semicolon`,
`reserved-word`, `braces-required`, `unknown-escape` and `block-indentation`, and `invalid-source`.
The checker raises the ids of the chapters it reads, listed in its section below, and `syntax` for
the shapes E4 leaves to it; a corpus case that expects an id of a chapter it does not read is
checked for parsing clean and for getting `not supported` from the checker, its expectation read
against the rules as `reference/00-about.md` says. The second kind, `not supported`, names a
construct the reference states and this directory does not read; it carries no id, so it can
never pass for an error a case expects. The front end produces it nowhere, since every form of
chapters 1 to 11 is read; the checker produces it for the constructs its section lists, and says
nothing else about a program that uses one. One corpus case, `data/flags-match.kz`, expects a
`syntax` that depends on the type of the matched value (D13), which the front end cannot see and
the checker does not read; the corpus theories check that it parses with no diagnostic and that
the checker says `not supported` on its `flags` type.

A declaration whose type is a user type is parsed speculatively, since its first token is a plain
name, and so is one whose type is a function type (F11), since it starts with `(` as an expression
does. Once its shape is clear (`Type name =`, `Type name(`, `Type name<`) the parse is committed:
an error after that is reported where it stands, and what was reported before it (a
`reserved-word` on the name) is kept. A speculation that turns out to be no declaration drops what
it reported and undoes the `>>` it may have split for a type-argument list, and the statement is
parsed again as an expression; so each error reaches the bag exactly once. A statement that starts
with a modifier, `void`, `weak` or a built-in type's name is tried as a declaration the same way
and otherwise read as an expression, so `int.Parse(s)` and `mut.Add(1)` are member accesses on
names (the reading on L12 below). Inside parentheses or brackets, where L4 passes a line break
over, arms or braces that open on the next line are `braces-required` all the same: C1 raises the
error wherever a block that a statement form, a type body, `with` or a function takes opens on the
next line, is missing or is replaced by a statement, and the rest of that line goes with the one
error; a lambda's block after a `=>` that ends its line is the reading on F7 below.

## Readings the reference leaves open to the front end

Each of these is the front end's reading, not a decision; a design round can ask about the ones
that sit under a rule by its id. Every one is pinned by a test.

- L12: which core words are `syntax` where an expression stands. The seventeen words that begin or
  continue a statement form (`if`, `else`, `while`, `for`, `in`, `break`, `continue`, `return`,
  `throw`, `data`, `class`, `interface`, `enum`, `flags`, `raw`, `use`, `with`) are `syntax` at their
  line there, as the record's `print(if)` is; every other core word (`equal`, `by`, the modifiers,
  `void`, `weak`, the type names) is read as a name there, and so is one at the start of a statement
  where no declaration follows it (`mut.Add(1)`, `int.Parse(s)`) and one before an argument that
  nothing follows (`f(mut)`), so `print(equal)` after `equal = 1` adds no error and the declaration
  carries the one error, as E1 asks; an untyped lambda parameter and the name of a named argument
  that are core words are `syntax`, as the record decides, whatever the word. By L12's letter the use
  would be an error too, and an undeclared `print(equal)` gets no error from the front end, which
  looks no name up. A core word after a dot is a member name, `x.match(1)` and `x.int` alike, with no
  error at the use: the same reading.
- L9: where a word ends. A letter outside ASCII makes the whole word one `syntax`, since L9 names the
  letters a name has; a symbol such as an emoji is text of its own, and the name after it is a name.
  L9 says which letters a name has and not what a word with another character is.
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
  nothing of a block, and nothing in this directory says what the declaration means there.
- F7 with L4 and C1: a lambda whose `=>` ends its line takes its block or its expression on the next
  line with no error. L4 continues a statement at a line that ends in `=>`, and C1 names no line for
  a lambda's block, where it names one for every statement form, type body, `with` and function.
- K7 with V2: `mut count = 0` in a type body is a field without a written type, with the modifiers
  written before it; K7 shows a field as `Type name`, V2 speaks of variables, and the record's
  actor bodies write `mut users = Map<int, User>()`.
- T17 with T19: how a call's type arguments are told from comparisons. The front end keeps the list
  only when `(` follows the `>`, so `a < b, c > (d)` is the call `a<b, c>(d)` and `a < b, c > d` two
  comparisons; C# also keeps the list before `)`, `]`, `,`, `.`, `?` and a few more, which the front
  end reads as comparisons, since the reference has no form for a generic name that is not called:
  `a < b, c > .5` and `a < b, c > d, e` are comparisons, and `f(a < b, c >)` or `xs[a < b, c >]` is
  `syntax`, a comparison without its right operand. T17 says generic types are written as in C# and
  states no such rule.
- E4's general clause, applied to shapes no rule names: a modifier written twice (K7: class bodies
  follow C#, which refuses it), type parameters on an `enum` or `flags` type (D12 and D13 give them
  a name and a body), a second `?` on a type (N1 gives one), a second `weak` (R3 and R5 write one),
  a form feed or a vertical tab outside a literal (L13 names the carriage return alone), a `//`
  inside an interpolation (L3 runs a comment to the end of its line, which L6's hole does not reach)
  and a lone surrogate in the literal of a program text that was never a file (L1: no code point, and
  a file cannot hold one) are each `syntax`, as text no rule gives a meaning. Which id the doubled
  markers, the type parameters and the surrogate take is a question for a design round: an error of
  the type checker could be meant, and E4 names the parser's id for none of them. A modifier other
  than `mut` before a variable's name (`pub x = 1`) is `syntax` the same way: V2 and V4 give a
  variable `mut` and nothing else, so the word begins no declaration and the statement ends at it.
- Recovery (no rule id): after an error the parser skips the rest of the statement, which is the
  rest of its line once every parenthesis, bracket and brace the statement opened is closed again,
  and a block the statement opened, with an `else` clause after it, so that the next statement
  parses on its own; a statement in place of a block (C1) is read as the block's one statement
  after the error; a line the lexer already flagged gets no second `syntax` from the parser,
  inside an interpolation too. After a stray `"""` in a block, or text beside an opening `"""`
  that no `"""` on the same line closes, the lexer passes over the rest of the block, up to the
  closing `"""`, so that the block's text is not read as code, and reads what follows the
  closing quotes on their line as code.

Six shapes still get two errors, or an uneven one, from the front end, each low: `if x print(1)
else print(2)` is `braces-required` twice on one line, because the postfix `else` binds to the call
and takes arms of its own; `if x` with its `{` on the next line, then `else if y` with its own, is
one `braces-required` per `if`, so a chain in the C# brace style is one error per link; the
interpolation scan inside a `"""` block counts braces only and does not pass over a string literal
nested in a hole, as the one-line scan does; `equal = 2` after `equal = 1` is `reserved-word` a
second time, because the parser cannot tell the assignment from the declaration (V8) and reports
the core word at every line that writes `name =`; a `"""` block that is never closed takes the rest
of the file with it, under its one `syntax`, and so does a block whose content line holds `"""`
after text, or whose opening line holds text or a fourth quote, when no line that starts with `"""`
follows; and a `class`, `data` or `interface` body whose `}` is missing reads the top-level
statements after it as members, one `syntax` per line of them before the `syntax` of the missing
brace. Each is pinned as it is, so that a fix shows.

## What the checker reads

The checker (`Semantics/`) takes a tree the parser read without an error and checks it against
chapters 1 (source), 2 (variables), 3 (types and operators), 5 (data types: D1 to D11, D14 and
D15), 6 (values: M1 to M5, M7, and M9 for lists), 8 (functions, without F9 and F10), 9 (outcomes)
and 10 (control flow). It declares every data type and function of the file first (both are
visible in the whole file, F6), resolves the fields and bases of the data types, checks the
defaults, then every function body, then the top level. Every expression gets a type, and an
expression made of literals and constants gets its value too, so that `byte c = 200 + 100` is
`constant-overflow` where T6 and L17 put it and `10 / 0` is `constant-divide-by-zero`. A call is
resolved as F12 to F14 and D11 say: named arguments are bound to parameters, type arguments are
inferred from the arguments (T27: no widening between them, a literal takes what the other
arguments fix, two data types meet at their common base), and the one candidate whose parameters
fit is taken, else `argument-mismatch`, `ambiguous-call` or `cannot-infer`. A `match` and a postfix
`else` are checked for the cases they cover (C4, C9, O5, O8), and a case the calling function does
not list propagates as `unlisted-case` (O2, O3, O9). One defect, one diagnostic (E1): a value the
checker could not type has the error type, which fits anywhere and is reported nowhere again.

It raises these ids of the table: `type-mismatch`, `narrowing-conversion`, `sign-mix`,
`constant-overflow`, `constant-divide-by-zero`, `inexact-literal`, `redeclared`, `unknown-name`,
`unused-variable`, `assign-immutable`, `capture-assign`, `mut-required`, `mut-at-call`,
`argument-mismatch`, `ambiguous-call`, `cannot-infer`, `duplicate-function`,
`default-not-constant`, `missing-return`, `throw-needs-value`, `unlisted-case`,
`match-not-exhaustive`, `unreachable-arm`, `reversed-range`, `break-outside-loop`,
`string-index`, `no-text`, and `syntax` for the shapes E4 leaves to it (the readings below).

### What the checker does not read

A program that uses one of these gets `not supported` at the construct's line and nothing else:
a `class`, an `interface`, an `enum` or a `flags` type and a value of one (chapter 7, D12, D13,
and the A-rules of their text); `null`, a nullable type, `?.` and `??` (chapter 4); `weak` (R3,
R5), a `raw` block (R7) and `use` (F10); `Map` (M8, M9, M10) and an assignment into an index (M9);
the body of a `data` type and its methods (D16 to D20), `equal by` (K8), an interface a `data`
type names (K10) and the explicit form of inheritance (K17); the limit of a type parameter and a
member of one (T19); a conversion to `string`, `bool` or `char` (A5 to A7); `this` (K7) and a
modifier of a method on a top-level function (K7, M6). The corpus theory lists the rule ids whose
cases the checker must say `not supported` to, and eight cases by name that cite only rules it
reads and still use such a construct, each with the construct.

### Readings the reference leaves open to the checker

Each is the checker's reading, not a decision, pinned by a test in `CheckerTests.cs`; a design
round can ask about it by the rule's id.

- E4 with F11, T10, D2, F5 and C8: a function's name where a value stands (`f = Double`) is
  `syntax`, since F11 gives a function value through a lambda only, and so is a type's name there
  (`print(int)`); a value on its own as a statement (`x + 1`) is `syntax`, since F5 lists what a
  statement is and a value is none; a range outside `for` is `syntax`, since C8 shows it there only.
- T9 with T23 and T28: `-` on a `uint` or a `ulong` is `sign-mix`, since no signed type takes
  every value of it; on a `byte` or a `ushort` the operand is promoted to `int` first, as T28
  promotes it under every operator, so `-b` on a `byte` is an `int` and no error.
- V10 with D9: a member a type does not have (`s.Count` on a string, `xs.Size` on a list) is
  `unknown-name` at the dot, V10's id for a name no rule gives a type; a member of a union is
  `type-mismatch`, since a union is one of its cases only after a `match` (D9).
- V3 with C8 and M9: an assignment to a loop variable is `assign-immutable`, since C8 names it a
  variable and V3 makes a variable without `mut` immutable; so is an assignment to a field through
  a value no `mut` variable reaches (`Point(1).X = 2`), and to `Count`, which M9 gives no setter.
- V9 with A1 and F6: a variable named `print`, or named as a function or a type of the file, is
  `redeclared`, since a function's and a type's name is visible in the whole file and so is
  `print`.
- T27 with M9: `List()` with no type argument and no argument to infer it from is `cannot-infer`,
  the id T27 gives a type argument nothing fixes.
- F12: of two functions that do not differ in their parameters the second is `duplicate-function`,
  and it is left out of overload resolution, so a call of the name gets no second error (E1).
- O5: an arm of a postfix `else` that names a type the call cannot return beside its success, and
  an arm that recovers with a value of another type than the success, are `type-mismatch` at the
  arm; a case the arms leave out propagates as O2 says, or is `unlisted-case` where the calling
  function does not list it.
- T2 with T29 and L6: `decimal` beside a floating-point operand is `type-mismatch`, since neither
  widens into the other; a duration under `*`, `/` or `%` is `type-mismatch`, since T29 gives the
  time types `+`, `-` and the comparisons; `"a" + "b"` is `type-mismatch`, since L6 joins text by
  interpolation and T2 gives `+` to numbers.
- F16: a loop variable of the top-level code reserves its name in every top-level function, so a
  parameter or a local of that name there is `redeclared`; F16's letter counts "every variable
  declared outside a function ... at any depth of the top-level code". The corpus case
  `null/early-continue-narrows.kz` named its loop variable `id` beside a parameter `id`, and was
  renamed to hold under the rule as written; whether a loop variable, which lives only in its loop,
  should reserve its name is a question for a design round.
- F1: a function declared inside a block is a function of the file, as the front end reads it.
