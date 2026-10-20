# The Kurz compiler

The compiler reads the language the reference under `reference/` states, rule by rule, and the
corpus under `corpus/` is what it is measured against. This directory holds a .NET solution:

- `Kurz.Compiler/`: the library. `Text/` reads a source file (L1: UTF-8, a byte-order mark
  skipped, the two-byte line break of Windows editors read as one, in a `"""` block too), `Syntax/`
  turns it into tokens and a syntax tree (the syntax of chapters 1 to 11: every declaration,
  statement and expression form the reference shows), `Semantics/` checks a tree the parser read
  without an error against the rules of chapters 1 to 3, 5, 6 and 8 to 10 (the section "What the
  checker reads" below) and says `not supported` to a construct of the other chapters, `Corpus/`
  reads the header of a corpus case in the shape `reference/00-about.md` states, `Emit/` turns a
  tree the checker read without an error into LLVM IR (the section "What the emitter emits"
  below), `Runtime/` is the C runtime a built program links, carried inside the library, and
  `Build/` finds clang, builds the IR with the runtime into an executable, and is the command.
- `Kurz/`: the command `kurz` (`build`, `run` and `exec`, the section "The command" below), one
  line over `Build/Command.cs`.
- `Kurz.Compiler.Tests/`: xunit tests. Every program the lexer, parser, checker and command tests
  read stands inline in the test, and the corpus suites read the cases where they lie, under
  `corpus/`: the tree gate admits no `.kz` file under `compiler/`. One suite runs every corpus
  case through the front end, and every case the front end reads clean through the checker, and
  checks that the errors each reports are exactly the ones the case's header expects of it; a
  case that uses a construct the checker does not read must get `not supported` and no error from
  it. A second suite builds every case that expects a run, with the emitter and clang, and runs
  it: its output, its exit code and the line an exception writes are held to the header; the
  cases of the types the emitter does not emit are listed by name with the construct, and a stale
  entry is refused. A third suite runs the command `kurz` as a process. A fourth takes the pieces
  of `Build/` on their own: the clang search over the path and the Program Files directory it is
  given, and the environment a captured process gets.

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

The suites that build a program need a clang (design record, section 2). The compiler tries, in
this order, every `clang-<major>` on the path, newest major first, then `clang`, then on Windows
`LLVM\bin\clang.exe` under the directory the `ProgramFiles` variable names, where the LLVM installer puts it without adding it
to the path; the first that answers `--version` as clang does is taken. The compiler resolves each
name through the path's directories itself and runs the file by its full path, so that the places
the operating system would search beside the path (on Windows the program's own directory, the
current directory and the system directories) play no part: a name that no directory of the path
holds is named among the places looked at and not run. Without a clang, every test that builds a
program fails and names the places looked at; none is skipped. The runtime's C is
compiled once per run, and every program is linked against that object.

The gate `compiler-tests` (`tools/compiler_gate.py`) reads `NuGet.config` as XML and refuses it
when it is missing, is not well-formed, does not open its package sources with `<clear />` or maps
no package pattern to a source; asks the clang the compiler would use for its version, prints the
line, and refuses a missing clang or one whose major release is outside the set the gate names
(18 to 23: the Linux image's 18, the Windows image's 20, and the releases up to the newest a build
was seen on; the IR uses no form that differs between them); runs that command from inside
`compiler/`; refuses a failed or a skipped test, a run with fewer passed tests than its floor, no
summary line, a non-zero exit with no failed test, a missing `dotnet` and a run out of time; and
is part of every gates run.

## The command

`kurz build file.kz [-o <executable>] [--release]` writes the executable beside the source, named
after it, `hello` for `hello.kz` and `hello.exe` on Windows, or where `-o` says. `kurz run
file.kz [--release]` and `kurz exec "<program>" [--release]` build into a temporary directory, run
the program, remove the directory and end with the program's exit code. The default is the test
build; `--release` builds the release one. A refused program gets its diagnostics, one line each,
`hello.kz:3: <id>: <text>`, with `exec` in place of the file's name for a program given as the
argument of `exec`, no executable, and the exit code 1 (design record, section 2). From inside
`compiler/`, `dotnet run --project Kurz -- exec "print(1)"` runs it without a build of its own.

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
  the type checker could be meant, and E4 names the parser's id for none of them, nor for the next
  one. A modifier other than `mut` before a variable's name (`pub x = 1`) is `syntax` the same
  way: V2 and V4 give a variable `mut` and nothing else, so the word begins no declaration and the
  statement ends at it.
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
chapters 1 (source), 2 (variables), 3 (types and operators, without the limit of a type parameter,
T19), 5 (data types: D1 to D11, D14 and D15), 6 (values: M1 to M5, M7, and M9 for lists), 8
(functions, without F9 and F10: it reads one file), 9 (outcomes) and 10 (control flow). It declares
every data type and function of the top level first, in source order (both are visible in the
whole file, F6), resolves the fields and bases of the data types, then the signatures, checks the
defaults, then every function body, then the top level. Every expression gets a type, and an
expression made of literals and constants gets its value too, so that `byte c = 200 + 100` is
`constant-overflow` where T6 and L17 put it and `10 / 0` is `constant-divide-by-zero`. A call is
resolved as F12 to F14 and D11 say: named arguments are bound to parameters, type arguments are
inferred from the arguments (T27: no widening between them, a literal takes what the other
arguments fix, a data type and one derived from it meet at the base, in whatever order the
arguments stand), and the one candidate whose parameters fit is taken, else `argument-mismatch`,
`ambiguous-call` or `cannot-infer`. A `match` and a postfix
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

A program that uses one of these gets `not supported` at the construct's line and nothing else;
what stands inside the construct is still read, with its diagnostics dropped, so that the variables
it uses count as used. The list, which is the complete one: a `class`, an `interface`, an `enum`
or a `flags` type and a value of one (chapter 7, D12, D13, and the A-rules of their text); `null`,
a nullable type, `?.` and `??` (chapter 4); `weak` (R3, R5), a `raw` block (R7) and `use` (F10);
a second file of a program and what F9 makes visible across files, since the checker reads one
file; `Map` (M8, M9, M10), an assignment into an index or into a path through one, a `mut` method
on an element of a list and an element passed as `mut` (M9); the body of a `data` type and its
methods (D16 to D20), `equal by` (K8), an interface a `data` type names (K10), the explicit form
of inheritance (K17) and a generic data type as a base (D6, T19); the limit of a type parameter
and a member of one (T19); a function or a type declared inside a block (F1, F6, below); `-` on a
`uint` or a `ulong` (T9, below); a conversion to `string`, `bool` or `char` (A5 to A7); `this`
(K7) and a modifier of a method on a top-level function (K7, M6). The corpus theory lists the rule
ids whose cases the checker may say `not supported` to, and six cases by name that cite only rules
it reads and still use such a construct, each with the construct; a case the checker reads in full
is held to its header whatever it cites.

### Readings the reference leaves open to the checker

Each is the checker's reading, not a decision, pinned by a test in `CheckerTests.cs`; a design
round can ask about it by the rule's id.

- E4: the rule gives one id, `syntax`, to text no rule gives a meaning, and names shapes only the
  checker can tell among them (D13's `match` on flags, O10's call). The checker reads these shapes
  under it: a function's name where a value stands (`f = Double`), since F11 gives a function value
  through a lambda only, and a type's name there (`print(int)`), since T10 gives it a meaning as a
  conversion only; a value on its own as a statement (`x + 1`), since F5 lists what a statement is
  and a value is none; a range outside `for`, since C8 shows it there only; a value as the
  expression body of a `void` function or a `void` lambda (F2, F4, F11); a call with a `void`
  success where a value stands (O10); `Name()` on a data type without fields, whose value D8 writes
  as the bare name; a second data type among the bases of a data type, which D6 gives one base; a
  count of type arguments the type or function does not have (T17, T19), in a type as in a call;
  a path as an arm (`p.X`, which the parser reads as a pattern for a value of an enum, D12), where
  C9 has a literal; a `match` used as a value whose every arm throws, or whose arm is a statement,
  where C9 has an arm with a value; an arm of a postfix `else` whose last statement neither
  recovers, returns nor throws, the three things O5 lets it do, or that names a literal where O5
  has a case type, or an `else` that follows no call; `data E()`, where D8 writes the type as the
  bare name; `mut` on a field (D4); a function of the file without a body (F2, F3); `void` as the
  type of a variable, a parameter or a field (F4); a modifier on a variable (V2, V4); the left
  side of an assignment that is neither a name nor a path (V8, M4); a field or a constructor
  outside a type body (K7, K14). An id of the type checker could be meant for any of them; a design
  round can ask by the rule.
- T9 with T23 and T28: `-` on a `uint` or a `ulong` is `not supported`: whether it is an error or
  wraps is written nowhere. On a `byte` or a `ushort` the operand is promoted to `int` first, as
  T28 promotes a narrow operand under every operator, so `-b` on a `byte` is an `int` and no error.
- V10 with D9: a member a type does not have (`s.Count` on a string, `xs.Size` on a list) is
  `unknown-name` at the dot: V10 gives that id to a name that is not visible at the place it
  stands, and a member no rule gives the type is such a name; a member of a union is
  `type-mismatch`, since a union is one of its cases only after a `match` (D9).
- V7 with C8 and M9: an assignment to a loop variable is `assign-immutable`, since C8 names it a
  variable and V7 makes a second assignment to a variable without `mut` that error; so is an
  assignment to a field through a value no `mut` variable reaches (`Point(1).X = 2`), and to
  `Count`, which M9 gives a list to read and no field to assign.
- T27 with D6: the arguments of a type parameter's positions are gathered first and then judged,
  so that their order makes no difference; where one argument's data type is the base of the
  others' (D6) the parameter takes the base, and two derived types whose common base no argument
  has are `cannot-infer`. T27 names the base for classes; the checker reads it for data types.
- C9 with O8: the arms `true` and `false` together cover a `bool`, bare or as a case of a union,
  so a `match` with both is exhaustive and an `else` after them is `unreachable-arm` (C4).
- V9 with M9 and T17: a data type named as a built-in (`data List`, `data int`, `data print`) is
  `redeclared`, since no type could name it.
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
  `null/early-continue-narrows.kz` named its loop variable `id` beside a parameter `id`; the
  checker says `not supported` to that case (chapter 4) and reports nothing on it, and the variable
  was renamed on the rule's letter alone, so that the case holds under it whichever way a reader
  takes the rule; whether a loop variable, which lives only in its loop, should reserve its name is
  a question for a design round.
- F1 with F6: a function or a type declared inside a block is `not supported`: F6 makes a
  declaration of the file visible in the whole file and says nothing of one inside a block. The
  name of such a declaration is one the checker does not read, so a use of it is no error.
- V13 with C4: the name an arm of a `match` binds is never `unused-variable`, as a loop variable
  is not (V13): the arm declares it, not the program's choice, and V13 names parameters and loop
  variables only.
- T27 with F11: a lambda argument fixes no type parameter. It is typed by the parameter it lands
  on, after the other arguments fixed the type arguments, so a type parameter that only the
  lambda's result could fix is `cannot-infer`.
- F14 with L17: for a function that is not overloaded the parameter's type reaches a literal
  argument (L17), so `H(1)` with one `H(byte b)` compiles; with a second `H` declared, F14 gives
  the literal its own type for the pick, and the same call is `narrowing-conversion`.
- M7 with M4: what stands after `mut` is a path of data fields from a `mut` variable; two fields
  of one variable are two values and may both be passed; the same path twice, or a path and one it
  is a prefix of, is `mut-at-call`.
- D9 with O1: two unions are one type when their cases are the same in the same order, since O1
  gives the order a meaning (the first case is the success), so `List<int | string>` and
  `List<string | int>` are two types; a value of one union stands where another is required when
  every case of it is a case there.

## What the emitter emits

The emitter (`Emit/`) takes a tree the checker read without an error and writes LLVM IR as text
for what the checker reads, over every primitive type but the timestamps, and the lists, data
types, unions and function values made of them; the build (`Build/`) hands the IR and the
runtime's C (`Runtime/kurz_rt.c`) to clang, which compiles both and links them into the one
executable, the runtime's code a part of the program and the C library linked as the platform
links it (design record, section 2). The runtime calls the C library for what the record's
boundary lists and moves its own bytes with loops; the copy of a record in the emitted code is
LLVM's copy intrinsic, which clang lowers to the C library's `memcpy` where it does not inline it. An integer is the LLVM integer of its width, signed and unsigned alike, and
the operations tell them apart; a `float` and a `double` are LLVM's, and the fewest digits that
read back as the value are their text (A11, A14); a `decimal` is the four words of a
`System.Decimal` as two 64-bit words, and its arithmetic, comparisons, conversions and text run
in the runtime as `System.Decimal` computes them (T24, A8); a `duration` is its count of
nanoseconds as `i64` and a `longduration` as `i128` (T24, T29); a `bool` is `i1`; a `char` is its code point as `i32`; a string is
a pointer to UTF-8 bytes and their count; a list is a pointer to a shared buffer and the count of
items the value sees; a `data` value is a pointer to a record on the heap whose first field is
the number of its type, with the base's fields first (D7), so that a value held as its base (D6)
is told apart in a `match` (C4) and a value compares by its fields (D3); a union is the index of
its case and a pointer to a box with the case's value, the success being case 0 (O1) and a
`void` success having no box (O10); a function value is the code and its environment, which holds
the captured variables by value (F15). A generic function or data type is emitted once per set of
type arguments. The runtime's functions take and return scalars only: a string or a list crosses
the boundary as its fields, or through a pointer to where the runtime writes it, because the
platforms pass a 16-byte struct by value differently.

The test build, the default, checks every `+`, `-` and `*` of T4 with LLVM's overflow intrinsics
and raises `overflow` (T5); the release build wraps. A division or a remainder by zero raises
`divide-by-zero` in both (T25); the signed minimum divided by -1 is `overflow` in the test build
and wraps in the release build (a reading, below); a shift count is taken modulo the width (T22); a conversion that
does not fit is `overflow` in the test build and wraps in the release build (T20). A
floating-point division or remainder by zero gives an infinity or NaN, as IEEE 754 has it (T25); a
`decimal` result that does not fit raises `overflow`, and a `decimal` division or remainder by
zero `divide-by-zero`, in both builds (a reading, below); a conversion of a floating-point value
or a `decimal` into an integer type drops the fraction, and a value that does not fit, an
infinity and NaN raise `overflow` in the test build and saturate in the release build, NaN to 0
(T26); a `duration` sum that does not fit, and a `longduration` converted into a `duration` it
does not fit, are `overflow` in the test build and wrap in the release build (T29). An exception
writes `error: <id> at line <n>` on standard error and ends the process with the exit code 1
through an exit that runs no handler of the C library (section 6); what `print` wrote before
stays. Every call stores its line in the runtime before it runs, so that the line of a stack
overflow is the line of the call that did not fit (O11): the runtime takes the fault on a signal
stack, under a vectored handler on Windows, and writes the line as for any exception. No call of
a Kurz function is a tail call (`notail`), so that a recursion that does not end overflows the
stack as O11 says instead of running as a loop.

Nothing is freed: the reference counting of section 3 is not in this compiler, and every
allocation lives until the process ends. A list buffer is shared between the values that hold it
and grown in place while the appending value holds the buffer's last item, the watermark in the
buffer's header; an append behind the watermark copies the buffer, so that a list value never
changes under a variable that holds an older one (M1, M3). A write into a field through a path
replaces the records along the path by copies (M4), so that a variable that holds the old value
keeps it.

### What the emitter does not emit

A program that uses a timestamp (T24, and the A-rules of its text) gets `not supported` at the
line of the first field, variable, parameter or operation of that type the emitter reaches, and
no IR; no corpus case that expects a run uses one, and the runner's theory fails on a
`not supported` of any case the checker reads in full. What the checker does not read (its list
above) never reaches the emitter.

### Readings the reference leaves open to the emitter and the command

Each is the reading of this directory, not a decision, pinned by a test in `CommandTests.cs` or
`EmitterTests.cs`, named with it; a design round can ask about it.

- Section 6 with E3: the text of a thrown value follows the line with `: ` between them,
  `error: <id> at line <n>: <text>`, so that the fixed part of the line reads the same for every
  exception. A corpus header names the id and the line, not the text, so the runner holds the
  line to its fixed part alone; the whole line is pinned by `CommandTests.cs`
  (`A_thrown_value_follows_the_error_line_after_a_colon_and_a_space`), which runs
  `corpus/outcomes/throw-text.kz` through the command.
- Section 2: a command line the command cannot read (no verb, a verb it does not know, no file or
  program, a second file, an option it does not know, `-o` under `run` or `exec`) gets the usage on
  standard error and the exit code 1, as a refused program does; a file that cannot be read, and a
  clang that is missing or refuses the build, get one line that starts with `kurz: ` and the same
  code. The record gives a refused program the code 1 and names no other failure of the command.
  Pinned in `CommandTests.cs`: the usage by `A_command_line_the_command_cannot_read_gets_the_usage_and_exit_1`,
  the file by `A_missing_file_is_reported_and_exits_1`, the clang by
  `A_missing_clang_is_reported_with_the_names_tried_and_exits_1` and
  `A_clang_that_refuses_the_build_is_reported_and_exits_1`.
- Section 2 with E1: a construct the compiler does not read is reported as
  `<file>:<line>: not supported: <construct>`, the form of a compile error with `not supported`
  where the id stands, since the record gives a refused program one line per diagnostic and
  `not supported` carries no id. Pinned in `CommandTests.cs` by
  `A_construct_the_compiler_does_not_read_is_reported_as_not_supported_with_its_line`.
- A14 with A11: the text of a `float` takes the exponent form at the thresholds A14 names for a
  `double`, from 1E+15 and below 1E-04, and the digits of either type are the fewest that read
  back as the same value, which can be seventeen for a `double` where sixteen read back as a
  neighbour (2^-25 is `2.9802322387695312E-08`). Pinned in `EmitterTests.cs` by
  `The_text_of_a_float_takes_the_exponent_form_at_the_thresholds_of_a_double` and
  `The_text_of_a_double_is_the_shortest_that_reads_back_as_the_value`.
- T25 with T5 and T26: a `decimal` result that does not fit, and a floating-point value converted
  into `decimal` that does not fit, an infinity and NaN, raise `overflow` in both builds: the
  record wraps an integer in the release build, and a `decimal` has no width to wrap at. Pinned
  in `EmitterTests.cs` by `A_decimal_that_does_not_fit_is_overflow_in_both_builds` and
  `A_floating_point_value_beyond_a_decimal_is_overflow_in_both_builds`.
- Section 2: on a platform other than Windows the executable `kurz build` writes beside the
  source has no extension, `hello` for `hello.kz`, as a program built there is named; the record
  names `hello.exe` for Windows alone. Pinned in `CommandTests.cs` by
  `Build_writes_the_executable_beside_the_source_named_after_it`, on each platform in its form.
- T5 with T25: the signed minimum of a type divided by -1 does not fit the type, so `/` raises
  `overflow` in the test build and gives the minimum in the release build, as `+`, `-` and `*` do
  on a value that does not fit; `%` of the same pair is 0 in both builds, since nothing in it
  overflows. T25 names the zero divisor alone. Pinned in `EmitterTests.cs`, since no corpus case
  can stand on it, by `The_smallest_value_divided_by_minus_one_is_overflow_in_the_test_build`,
  `The_smallest_value_divided_by_minus_one_is_the_smallest_value_in_the_release_build` and
  `The_remainder_of_the_smallest_value_by_minus_one_is_zero_in_both_builds`.
