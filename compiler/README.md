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
nothing else about a program that uses one. Both sides raise `syntax`, so a case that expects it
is the front end's when the front end reports an error on it, and the checker's when the front end
reads it clean. One such case, `data/flags-match.kz`, expects a `syntax` that depends on the type
of the matched value (D13), which the front end cannot see and the checker does not read; the
corpus theories check that it parses with no diagnostic and that the checker says `not supported`
on its `flags` type, and no test holds its expected error.

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
  statement ends at it; which id it takes is a question for the same design round.
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
chapters 1 (source), 2 (variables), 3 (types and operators; of T19 the type parameters and the
type arguments a use names or leaves to be inferred, without the limit of a type parameter and a
generic data type as a base), 5 (data types: D1 to D11, D14 and D15), 6 (values: M1 to M5, M7, and
M9 for lists), 8 (functions, without F9 and F10: it reads one file), 9 (outcomes) and 10 (control
flow), and the rules of `print` and of a value's text that these chapters reach: A1 to A4, A8, A10
for a type parameter, A11, A12 and A14. It declares
every data type and function of the top level first, in source order (both are visible in the
whole file, F6), resolves the fields and bases of the data types, then the signatures, checks the
defaults, then every function body, then the top level. Every expression gets a type, and an
expression made of literals and constants gets its value too, so that `byte c = 200 + 100` is
`constant-overflow` where T6 and L17 put it and `10 / 0` is `constant-divide-by-zero`. A call is
resolved as F12 to F14 and D11 say: named arguments are bound to parameters, type arguments are
inferred from the arguments (T27: no widening between them, and a literal takes what the other
arguments fix; that a data type and one derived from it meet at the base, in whatever order the
arguments stand, is the reading on T27 with D6 below), and the one candidate whose parameters fit
is taken; more than one is `ambiguous-call` (F13), and none is, for a name with more than one
function, `argument-mismatch` at the call, with a line of the message per function and why it does
not fit (F13), and for a name with one function the error of that function: `argument-mismatch`,
`cannot-infer`, or the id of the argument that does not fit, `narrowing-conversion` among them. A
`match` and a postfix `else` are checked for the cases they cover (C4, C9, O5, O8), and a case the
calling function does not list after the first case of its return type propagates as
`unlisted-case` (O1 to O3, O9); a cycle of `data` bases is `base-cycle` (D6), where the order in
which D6 reads the files of a program, the ordinal order of their paths, is not reached, since the
checker reads one file. One defect, one diagnostic (E1): a value the checker could not type has
the error type, which fits anywhere and is reported nowhere again.

It raises these ids of the table: `type-mismatch`, `narrowing-conversion`, `sign-mix`,
`constant-overflow`, `constant-divide-by-zero`, `inexact-literal`, `redeclared`, `unknown-name`,
`unused-variable`, `assign-immutable`, `capture-assign`, `mut-required`, `mut-at-call`,
`argument-mismatch`, `ambiguous-call`, `cannot-infer`, `duplicate-function`,
`default-not-constant`, `base-cycle`, `missing-return`, `throw-needs-value`, `unlisted-case`,
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
on an element of a list and an element of a list passed as `mut` (M7, M9); the body of a `data`
type and its members, after a value of the type or of one derived from it and after the type's
name (D16 to D20), and `equal by` (K8); among the bases of a `data` type a class, an interface, an
enum or a flags type (chapter 7, K10, D12, D13), the explicit form of inheritance (K17) and a
generic data type (T19), each of which leaves the data type, and every data type derived from it,
unread; the limit of a type parameter and a member of a type parameter with one (T19); a function
or a type declared inside a block (F1, F6, below); `-` on a `uint` or a `ulong` value, which no
rule gives a meaning (below); a conversion to `string`, `bool` or `char` (A5 to A7); `this` (K7);
a modifier of a method on a top-level function, which leaves the function and every call of its
name unread (K7, M6); and a field or a constructor among statements, which the parser reads in a
type body only (K7, K14), so that only a tree built another way holds one. The corpus theory
lists the rule ids whose cases the checker may say `not supported` to, and six cases by name that
cite only rules it reads and still use such a construct, each with the construct; a case the
checker reads in full is held to its header whatever it cites.

### Readings the reference leaves open to the checker

Each is the checker's reading, not a decision, pinned by a test in `CheckerTests.cs`; a design
round can ask about it by the rule's id.

- E4: the rule gives one id, `syntax`, to text no rule gives a meaning, and names shapes only the
  checker can tell among them (D13's `match` on flags, O10's call). The checker reads these shapes
  under it: a function's name where a value stands (`f = Double`), since F11 gives a function value
  through a lambda only, and a type's name there (`print(int)`), since T10 gives it a meaning as a
  conversion only, where a member after such a name (`int.Parse(s)`, `print.Count`) is a member
  the type or the function does not have, `unknown-name` (the reading on V10 below); a value on its
  own as a statement (`x + 1`), since F5 lists what a statement is and a value is none; a range
  outside `for`, since C8 shows it there only; a value as the expression body of a `void` function
  or a `void` lambda (F2, F4, F11), which no rule gives an id, where F3 makes `return` with a value
  in a `void` function's block `type-mismatch`; a call with a `void` success where a value stands
  (O10), an argument at any parameter included, except as the body of a lambda under a function
  type whose result is not `void`, which F11 makes `type-mismatch` at the lambda; `Name()` on a
  data type without fields, whose value D8 writes as the bare name; a second data type among the
  bases of a data type, which D6 gives one base; a count of type arguments the type or function
  does not have (T17, T19), in a type as in a call, one on a callee without type parameters
  included (`print<int>(1)`, a conversion, a function value, a list's `Add` and `Where`), where a
  generic type written with none is `cannot-infer` (the reading on T27 with M9 and T19 below); a
  path as an arm (`p.X`, which the parser reads as a pattern for a value of an enum, D12), where C9
  has a literal; a `match` used as a value whose every arm throws, or whose arm is a statement or a
  block, where C9 has an arm with a value; an arm of a postfix `else` whose last statement neither
  recovers, returns nor throws, the three things O5 lets it do, or that names a literal where O5
  has a case type; an `else` that follows no call, which the parser reports, since it reads the
  postfix `else` after a call only (O5); `data E()`, a declaration with an empty field list, which
  neither D1 nor D8 writes, D8 writing a type without fields as the bare name `data E`; `mut` on a
  field (D4); a function of the file without a body (F2, F3); `void` as the type of a variable, a
  parameter or a field (F4); a modifier on a variable (V2, V4); the left side of an assignment that
  is neither a name nor a path (V8, M4). An id of the type checker could be meant for any of them;
  a design round can ask by the rule.
- T9 with T23 and T28: `-` on a `uint` or a `ulong` value is `not supported`: T9 and T23 judge two
  operands, and whether `-` on one such value is an error or wraps no rule says. `-5U` and
  `uint u = -1` are no such value: a `-` written directly before a literal belongs to it (L17), so
  each is the literal -5 or -1, which fits no type the suffix or the declaration names,
  `constant-overflow` (T6). On a `byte` or a `ushort` the operand is promoted to `int` first, as
  T28 promotes a narrow operand under every operator, so `-b` on a `byte` is an `int` and no error.
- L17 with T28: an expression made of literals folds in the type its literals take, the type the
  context reaches them with, without T28's promotion, which is for the operands of a run-time
  operation; under `-` and `~` too, so `short s = -(5)` is a `short` as `short s = -5` is,
  `byte m = ~0x0F` is the `byte` 240 and `byte b = -(5)` is `constant-overflow`. A conversion is no
  literal, and neither is a named constant: `byte(200) + byte(100)` is the `int` 300 (T28).
- L17 with D9 and T29: where a union with no number case and exactly one time case is required,
  that case reaches a duration literal, as a written time type does, so
  `duration | string d = 106752days` is `constant-overflow`, where the literal's own type, a
  `longduration`, would be `type-mismatch`. T8 and L17 decide the number cases of a union (the
  owner, 2026-10-21), and a time case is none of them, so `byte | duration x = 5` makes the `5` a
  `byte`.
- T22 with L17 and T28: a shift of two operands made of literals folds in the type they take, the
  count taken modulo 32 for a narrow type, which T28 promotes to `int` first, and modulo the width
  otherwise; the bits that leave the type are dropped, at every width, since T22 makes a shift no
  arithmetic in the sense of T4 to T6, so no folded shift is `constant-overflow`: `1 << 31` is the
  `int` -2147483648, `0x40000000 << 2` is 0, and `byte b = 1 << 9` is 0.
- V10 with D9: a member a type does not have (`s.Count` on a string, `xs.Size` on a list) is
  `unknown-name` at the dot: V10 gives that id to a name that is not visible at the place it
  stands, and a member no rule gives the type is such a name; so is a member after the name of a
  type or a function (`int.Parse(s)`, `print.Count`), which stands before a dot, where the parser
  reads a member access, and not where a value stands, and a member of a type parameter without a
  limit, since only a limit gives a type parameter members (T19); a member of a union is
  `type-mismatch`, since a union is one of its cases only after a `match` (D9).
- V7 with C8 and M9: an assignment to a loop variable is `assign-immutable`, since C8 names it a
  variable and V7 makes a second assignment to a variable without `mut` that error; so is an
  assignment to a field through a value no `mut` variable reaches (`Point(1).X = 2`), and to
  `Count`, which M9 gives a list to read and no field to assign.
- T27 with D6: the arguments of a type parameter's positions are gathered first and then judged,
  so that their order makes no difference; where one argument's data type is the base of the
  others' (D6) the parameter takes the base, and two derived types whose common base no argument
  has are `cannot-infer`; a type a nested position fixes (`List<T>` of a `List<User>`) is the one
  the direct arguments meet, each of them that type or one derived from it. T27 names the base for
  classes; the checker reads it for data types.
- C9 with O8: the arms `true` and `false` together cover a `bool`, bare or as a case of a union,
  so a `match` with both is exhaustive and an `else` after them is `unreachable-arm` (C4).
- V9 with M9 and T17: a data type named as a built-in (`data List`, `data int`, `data print`) is
  `redeclared`, since no type could name it.
- V9 with A1 and F6: a variable named `print`, or named as a function or a type of the file, is
  `redeclared`, since a function's and a type's name is visible in the whole file and so is
  `print`.
- T27 with M9 and T19: `List()` with no type argument and no argument to infer it from is
  `cannot-infer`, the id T27 gives a type argument nothing fixes, and so is a generic type written
  without its type arguments (`Pair p`): T19 lets a use name them or leave them to be inferred, and
  a written type has nothing to infer them from. A count that is neither none nor the type's is
  E4's `syntax` (above).
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
- F1 with F6: a function or a type declared inside a block is `not supported`: F6 makes a
  declaration of the file visible in the whole file and says nothing of one inside a block. Inside
  its block the name of such a declaration is one the checker does not read, so a use of it there
  is no error; outside its block the name is not visible, `unknown-name` (V10), and a function's
  name is no type's there either. Its body is read with its diagnostics dropped, so that a
  variable around it that it reads counts as used.
- V13 with C4: the name an arm of a `match` binds is never `unused-variable`, as a loop variable
  is not (V13): the arm declares it, not the program's choice, and V13 names parameters and loop
  variables only.
- T27 with F11: a lambda argument fixes no type parameter. It is typed by the parameter it lands
  on, after the other arguments fixed the type arguments, so a type parameter that only the
  lambda's result could fix is `cannot-infer`.
- F14 with L17: for a function that is not overloaded the parameter's type reaches a literal
  argument (L17), so `H(1)` with one `H(byte b)` compiles; with a second `H` declared, F14 gives
  the literal its own type for the pick, and the same call fits no function.
- F13 with F14: a call that fits none of the functions of its name is `argument-mismatch` at the
  call where the name has more than one function in the pick (the owner, 2026-10-21); a name whose
  second function is `duplicate-function` has one there (the reading on F12 above), so its call keeps
  the error of its argument. A count of type arguments that no function of such a name has is that
  `argument-mismatch` too, where for a name with one function it is E4's `syntax` (the reading on E4
  above): it follows from the letter of the answer, under which a call that fits none of the
  functions of its name is that error, whatever kept each function from fitting.
- E1 with F13: an argument with an error of its own, or a `void` call standing as one, fits every
  parameter and contradicts none, so a call of an overloaded name that it leaves without one pick
  reports nothing more: the argument may have been meant for any of the functions.
- M7 with M4: what stands after `mut` is a path of data fields from a `mut` variable; two fields
  of one variable are two values and may both be passed; the same path twice, or a path and one it
  is a prefix of, is `mut-at-call`.
- D9 with C4: a value of a union stands where a union is required only, since a union is one of its
  cases only after a `match`: a `byte | short` is no `int`, and a union of two data types derived
  from one base no value of the base; each is `type-mismatch`.
- O2 to O4 with F7: O4 keeps a call's cases only as the subject of a `match` and in a written union
  type, so a call that a lambda with an inferred result returns unwraps (O2), and the result F7
  infers from the value lists no case: a case the calling function does not list is
  `unlisted-case` (O3).
