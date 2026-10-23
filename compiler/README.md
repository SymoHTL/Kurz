# The Kurz compiler

The compiler reads the language the reference under `reference/` states, rule by rule, and the
corpus under `corpus/` is what it is measured against. This directory holds a .NET solution:

- `Kurz.Compiler/`: the library. `Text/` reads a source file (L1: UTF-8, a byte-order mark
  skipped, the two-byte line break of Windows editors read as one, in a `"""` block too), `Syntax/`
  turns it into tokens and a syntax tree (the syntax of chapters 1 to 11: every declaration,
  statement and expression form the reference shows), `Semantics/` checks a tree the parser read
  without an error against the rules of chapters 1 to 3, 5, 6, 8 to 10 and the raw forms of 11
  (the section "What the checker reads" below) and says `not supported` to a construct of the
  other chapters, and keeps what it found out, node by node, for a code generator
  (`Semantics/SemanticModel.cs`: the type and constant of every expression, the variable or
  function every name means, the variable every declaration makes, the function every call picks
  with the parameter each argument fills, what every lambda captures), `Emit/` turns a program the
  checker read in full, with the runtime of its target, into LLVM's IR (the section "What the
  emitter emits" below), `Corpus/` reads the header of a corpus case in the shape `reference/00-about.md`
  states, its grant line among it (R7), and `Build/` finds the clang of the release of LLVM the
  compiler pins, carries and checks the runtime, builds a program from its source to an executable
  and is the command (the section "The command" below).
- `Kurz/`: the command `kurz`, one line over `Build/Command.cs`.
- `Kurz.Compiler.Tests/`: xunit tests. Every program the lexer, parser and checker tests read
  stands inline in the test, and the corpus suite reads the cases where they lie, under `corpus/`:
  the tree gate admits no `.kz` file under `compiler/`. That suite runs every corpus case through
  the front end, and every case the front end reads clean through the checker, and checks that
  the errors each reports are exactly the ones the case's header expects of it, the checker taking
  the grants of the case's `// allow:` line as a project file would give them (R7); a case that
  uses a construct the checker does not read must get `not supported` and no error from it.
  A second suite walks the tree of every corpus case the checker reads without a diagnostic and
  checks that the checker's record of it holds an entry for every node a code generator reads,
  and takes, program by program, what each kind of entry holds.
  A third suite takes the clang search over a path and a Program Files directory it makes, with
  the answers to `--version` it gives, and over this machine's own environment; a fourth runs the
  command `kurz` as a process, and in the test's own process with a clang search it gives; a
  fifth checks the runtime under `runtime/` (the section "The runtime" below). A sixth builds
  every corpus case that expects a run with the pinned clang and runs it, and checks what it
  prints, its exit code and its error line against the case's header (`RunTests`); a seventh
  builds the programs that pin what no case can, a refused write and a refused allocation among
  them (`EmitterTests`), and an eighth those of data values and unions, with the width of a tag and
  the copy of a block before a write (`DataTests`).

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
no failed test, a missing `dotnet` and a run out of time; then runs `kurz --version`, the command
the test run built, prints what it wrote and refuses an exit code other than 0, which the command
gives where it refused the clang it found or found none, and a 0 with no line that names the clang's
release; and is part of every gates run.

The suites need the clang of the pinned release on the machine that runs them, as every build
does (design record, section 2): on a machine without it, the test of this machine's search, the
tests of the command's `--version` and `build` and every test that builds a program fail, the last
with the command's own lines, and the gate fails on `kurz --version`. The image the `gates` job runs in has it, and so does a local run of the gates in
that image (`knowledge/the-gates-job-runs-in-its-image.md`); the workflow `compiler-windows`
installs it on every run.

## The command

`kurz --version` writes one line on standard output, as `rustc -V` writes one: the compiler's
version, the major release of LLVM the compiler pins, and the clang it found with that clang's
release. It exits with 0. `kurz --version --verbose` writes the same on lines of their own, as
`rustc -vV` does (design record, section 2; the owner, 2026-10-23):

    kurz 0.1.0 (LLVM 23, clang 23.1.4 at /usr/bin/clang-23)

    kurz 0.1.0
    LLVM version: 23
    clang: /usr/bin/clang-23
    clang version: 23.1.4

The compiler accepts one major release of LLVM, `Clang.PinnedMajor` in `Build/Clang.cs`.
`tools/lint_ci.py` holds the CI image and the Windows job to it.

The search tries three places, in order (design record, section 2, round 20):

1. `clang-23` on the path;
2. `clang` on the path;
3. on Windows, `clang.exe` in `LLVM\bin` under the directory the `ProgramFiles` variable names,
   where the LLVM installer puts it.

The search resolves each name through the path's directories itself and runs the file by its full
path, so the places the operating system would search beside the path play no part. It asks the
file it found for `--version`.

The first place that holds such a file decides, and a refusal ends the search there (the
record's assumed reading of round 20). Three answers are refused:

- a clang of another major;
- Apple's clang, whose version is Apple's own and names no LLVM release (the owner, 2026-10-23);
- a file whose answer names no clang release.

A refusal, or a search that found nothing, does three things:

- it writes on standard output what the command knows before the clang: `kurz 0.1.0 (LLVM 23)`,
  or under `--verbose` the first two lines (the owner, 2026-10-23);
- it writes the failure on standard error, in the record's form of the command's own failures;
- it exits with 1.

The failures read, in the words the owner kept on 2026-10-23:

    error: clang 20.1.2 is not LLVM 23
      note: found at /usr/local/bin/clang
      help: install LLVM 23, ahead of this clang on the path

    error: the clang found is Apple clang, whose version names no LLVM release
      note: found at /usr/bin/clang
      help: install LLVM 23, ahead of this clang on the path

    error: no clang found
      note: looked for clang-23 and clang on the path
      help: install LLVM 23

On Windows the note of a search that found nothing also names the installer's directory. On the
Windows runner a child process read `ProgramFiles` as the system's folder whatever its parent set,
so a test cannot move that directory through a child's environment
([a Windows child gets its own Program Files](../knowledge/a-windows-child-gets-its-own-program-files.md)).

`--verbose` stands before or after `--version`, as rustc takes `-v` and `-V` in either order (the
owner, 2026-10-23).

`kurz build [--release] file.kz [-o output]` builds the program for the machine that builds,
the test build unless `--release` says otherwise, into the executable beside the source, named
after it, `hello.exe` on Windows, or where `-o` says (design record, section 2). It writes over an
executable that is there, writes nothing on standard output, and exits with 0. A program the
compiler refuses gets its lines on standard error, one per diagnostic in the order of their lines,
and no executable, and the command exits with 1:

    hello.kz:3: unknown-name: `y` is not declared
    hello.kz:1: not supported: a value of type `List<int>` (M9)

The file is named as the command line gave it. The command grants a program neither `raw` nor
`native` (R7, R16): a program that needs them is refused with the grant's error. It refuses to
write over its source and over a directory, as rustc does, and those refusals, an output in a
directory that is not there, a source it cannot read, a machine it has no runtime for and a clang
that did not build the program are its own failures, in the record's form, each exiting with 1:

    error: the executable would be written over its source
      note: both are hello
      help: name another output with -o

    error: the output is a directory
      note: out is a directory
      help: name a file with -o

    error: the output's directory does not exist
      note: <the directory's full path> is not there
      help: name a file in a directory that exists with -o

    error: the source cannot be read
      note: <what the system said, the file's full path in it>
      help: name a Kurz source file that can be read

    error: no runtime for this system
      note: the runtime has a directory for 64-bit Linux and one for 64-bit Windows
      help: build on 64-bit Linux or Windows

    error: clang did not build the program
      note: /usr/bin/clang-23 exited with 1 and wrote:
        <what clang wrote>
      help: check that the clang found runs; the compiler accepted the program, so a clang that runs and fails on it is the compiler's defect

The search for clang runs after the program was accepted, and its failures are those above. The
build writes the IR into a directory of its own under the system's temporary directory, which it
removes after clang's run.

Any other command line gets the usage on standard error and the exit code 1:

    usage: kurz --version [--verbose]
           kurz build [--release] <file.kz> [-o <output>]

### Readings the record leaves open to the command

Each is a reading of this directory, not a decision. A test pins each one, and a design round can
ask about it.

- The compiler's version is the `Version` of `Directory.Build.props`, 0.1.0, with no commit
  appended. Pinned by the literal version in every expectation of `CommandTests`.
- A file that does not start, exits with a code other than 0 or gives no answer in 30 s names no
  release, and is refused as such. Pinned by
  `ClangTests.A_file_whose_answer_names_no_clang_release_is_refused`.
- `build` takes its options before or after the file, each once, and nothing else; the words of
  its own failures are this directory's, in the record's form. Pinned by `CommandTests`.
- `build` builds for a 64-bit machine alone: the emitter lays a pointer out in 8 bytes, and the
  runtime declares C's `size_t` as a `long`. Pinned by
  `CommandTests.The_target_is_a_64_bit_Linux_or_Windows_machine`.

## The runtime

The runtime a built program links is written in Kurz, in `runtime/` at the top of the repository
(design record, section 2): `runtime/common/` holds the code every target shares, and
`runtime/linux/` and `runtime/windows/` the code of one target each. The build of the compiler
embeds every `.kz` file there, by its path from the top of the repository (`Build/Runtime.cs`,
`Runtime.Files`). Code in `common/` reaches the functions of the target it is built for as the
folder `sys`: it writes `use sys` and calls `sys.Write(...)`, and `sys` is that target's directory
with the directory of the build's architecture read into it (the owner, 2026-10-23, "Rust's sys"),
a directory this compiler does not read (below). The checker reads `use sys` at the top of a file of
`common/`, and a call qualified by `sys` picks among the folder's `pub` functions; a function of the
folder that is not `pub` is `not-visible` there (F9), and a name the folder does not declare is
`unknown-name` (V10).

`Runtime.CheckEveryTarget` checks the runtime for every target, each folder granted `raw` and
`native` (R7, R16): the target's directory on its own, then `common/` with it as `sys`. It is also
red where a function `common/` calls has other parameters or another result in one target's
directory than in another's; a function only its own directory calls can differ freely (record,
section 2, round 20). A line of `common/` that the check for every target reports stands once, and one
that the check for some targets reports names them, `(in the check for windows)`. The compiler
reads one file in each of the three directories and no directory inside them: a second file of a
directory, which section 8 makes one folder, and the directory of an architecture inside a
target's (`runtime/linux/x86-64/`, where round 20 places that code) are `not supported`, and a
directory of a target it does not know, a directory inside `common/` and a directory of no file are
refused, each named by its path. The checks and the layout are pinned by `RuntimeTests`, which also
holds what the compiler carries to the files of the tree.

A build checks the runtime for its target and emits it into the program's module, its functions
under IR names of their own; nothing of it is compiled apart (record, section 2, round 17). The
emitted `main` calls the runtime's `Start`, then the program's top level, then `Finish`, which ends
the process with the exit code 0 through the target's `Exit`, the runtime's one exit (section 6).
`common/` gives what the emitted code calls: `Print`, the text of an integer and of a `char`, the
joining and the comparison of two strings, `Raise` for an exception of the run-time table and
`Throw`, and `Allocate`, which takes memory from the system's `Memory` in pieces of a mebibyte per
thread, and an allocation of more than a quarter of that in a piece of its own, and frees nothing:
the reference counting of section 3 is not emitted. A target's directory gives `Start`, `Write`,
`Exit` and `Memory`: on Linux over the C library, `Start` setting the signal of a closed pipe to
ignored and `Write` calling again where a signal interrupted it (section 6), on Windows over
`kernel32`. The Linux file is the code of 64-bit Linux, where C's `size_t` is 64 bits as `long` is.

The error lines are the record's (section 6): `error: <id> at <file>:<n>` for an exception of the
run-time table, the file named as the command line gave it, and `error: thrown at <file>:<n>:` for
a `throw`, with the thrown value's text on the line below it. A `print` passes its file and its line
to `Print`, which raises `print-failed` there where the system refuses the write or a write takes
no byte, as Rust's `write_all` fails on a write of zero bytes, in every build (the owner,
2026-10-23, "Caller passes it"). An allocation the system refuses ends the process with
`error: allocation-failed (<n> bytes)`, the size asked for, and no place (the owner, 2026-10-23, "Id +
size"). Both end with the exit code 1. `EmitterTests` pins both, the first in a test and in a release
build, the second with a runtime whose `Memory` refuses every request.

## What the emitter emits

The emitter (`Emit/`) takes a program the checker read without an error and without `not
supported`, with the runtime checked for its target, and writes one LLVM module of both, which the
pinned clang compiles at `-O2` and links with the C library as the platform links it, and with the
library a block of C functions names (R16). It emits:

- the integers, `bool`, `char` and `string`, and pointers to them and C function types over them
  (T2, T21, T15, R10, R17); a string is its bytes and their count, and the empty string's bytes
  are a constant of none, never at the address 0 (R15);
- variables, assignments, functions with their defaults and arguments by name (D11), `mut`
  parameters given a variable (M7), and a generic function once per list of type arguments it is
  called with (T19, T27);
- the arithmetic and the comparisons of chapter 3, which in a test build raise `overflow` where a
  value leaves its type (T5) and `divide-by-zero` (T14, T25) and wrap in a release build (T4), the
  operators on bits and the shifts (T22, T28), and the conversions written as calls (T10, T20);
- `if`, `while`, `for` over a range, which raises `reversed-range-at-run-time` (C8), `break`,
  `continue` and `return` (chapter 10), and `throw` of a value (O6);
- `data` types and their values, made with defaults and arguments by name (D1, D2, D8, D11), their
  fields (D14), `with` (D5), an assignment into a path (M4), a value of a derived type where its
  base is required (D6, D7), `==` field by field and never equal across types (D3, D15), and their
  text (A3);
- unions (D9), whose tag is the number of the case's type in the program, as wide as the largest
  number among the cases needs (section 5, round 19), a call whose result is one, with its other
  cases passed on to the caller (O1, O2), a success case without a value (O10), `match` with arms
  of a type, of a value and `else` (C4, C9, O8), `else` after a call (O4, O5), and `throw` of a
  data value and the bare `throw` of an arm of `else`, which throws the case that reached it (O7);
- `print` and the text of a value in a string's holes (A1, A2, A8, L6), and `==` on strings by
  their bytes (T18);
- the raw forms the runtime is written in: `raw` blocks, pointers, their arithmetic, `*p`, `&x`
  and `p[i]` (R10), `Ptr<T>(x)`, `long(p)` and `SizeOf<T>()` (R14), `s.Pointer`, `s.Bytes.Count`
  and `string.FromRaw(p, n)` (R15), state outside a function, `thread static` among it (R13),
  calls of C functions with `...` (R16) and of a C function value (R17).

In a test build every function carries LLVM's attribute that turns no call into a jump, so that a
recursion runs as deep as the program wrote it (record, section 2). A raise by code passes its file
and its line to the runtime as constants (section 6).

### What the emitter does not emit

A program that uses one of these gets one `not supported` at the construct's line, in the
checker's shape, and no executable: a list (M9), a generic `data` type with a base or a type
derived from it (D6, T19), the text of a value of a generic `data` type (A3), `==` on a `data` value
with a field of a union (D3, T18), `SizeOf<T>()` of a `data` type or a union (R14), a
lambda (F7) and a function value (F11), `float`, `double` and `decimal` (T2), the durations and the
timestamps (T13), a `mut` parameter given anything but a variable (M7), a `naked` function (R12),
inline assembly (R11), and a construct of the other chapters the checker does not read. The
reference counting of section 3 is not emitted either: memory a program takes is never given back,
so a program that allocates without end runs out of memory and ends with `allocation-failed`. The
corpus runner lists every case the checker reads in full and the emitter does not emit, 42 of the
220 cases that expect a run, with the words the emitter says, and refuses a stale entry; the 56 the
checker says `not supported` to are held by the checker's theory and not built. The crash path is
not emitted: a stack overflow
ends the program with the system's signal and not with its error line, and no exception writes a
trace (section 6), so `outcomes/stack-overflow.kz` is built and not run, the one case the runner
lists so.

### Readings the record leaves open to the emitter

Each is a reading of this directory, not a decision. A test pins each one, and a design round can
ask about it.

- `s.Bytes.Count` is the count the string holds, read as an `int`, the type M9 gives a count, its
  low 32 bits: a string of 2 GiB or more is beyond it. Pinned by
  `EmitterTests.The_count_of_a_strings_bytes_is_read_as_an_int`.
- A `data` type with neither a base nor a type derived from it is a plain value of its fields
  whatever its size, where D10 names small values; one with either is a pointer to a block that
  holds the number of the value's own type, then its fields, the base's first, so that a block of a
  derived type reads as one of its base (D6, D7). `with` and an assignment into a path through such
  a value copy the block before the write, so that no other variable sees it (D4, M4), and a block
  is never given back, as no memory is. Pinned by
  `DataTests.A_copy_by_with_of_a_derived_value_through_its_base_keeps_its_type_and_leaves_the_original`
  and `DataTests.An_assignment_into_a_path_through_a_block_copies_the_block_so_that_another_variable_keeps_its_value`.
- A `print` hands its line to one write call, called again for what a call did not take, through a
  buffer per thread that holds the line until that call returns. Pinned by the corpus runner.

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
never pass for an error a case expects. The front end produces it nowhere: it reads every form of
chapters 1 to 11, the raw forms of chapter 11 among them, where `Ptr<T>(x)` and `SizeOf<T>()` parse
as calls and `p[i]` as an index; they stand in the cases of R7 and R10 to R17, which the checker
reads as the section below says. The checker produces it for the constructs its section lists, and
says nothing else about a program that uses one. Both sides raise `syntax`, so a case that expects it
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
  when `(` follows the `>`, so `a < b, c > (d)` is the call `a<b, c>(d)` and `a < b, c > d` two
  comparisons, and after a name when a member and its `(` follow, R15's `List<T>.FromRaw(p, n)`,
  the one form the reference gives a generic name that is not called; C# also keeps the list
  before `)`, `]`, `,`, `?` and a few more, which the front end reads as comparisons:
  `a < b, c > .5` and `a < b, c > d, e` are comparisons, and `f(a < b, c >)` or `xs[a < b, c >]` is
  `syntax`, a comparison without its right operand. T17 says generic types are written as in C# and
  states no such rule.
- R10 to R17: the raw forms, each edge with the rule it rests on.
  - `*` and `&` before a value are prefix operators that bind as `-` does, since R10 writes a pointer
    as C# writes it, so `*b.Next` reads through `b.Next`. A line that starts with `*` starts a
    statement (L4), and `*` or `&` starts the value after `match` and after the `mut` of an argument.
  - `&` takes a place as the front end reads one: a name, a path with no `?.`, an element or what a
    pointer points to, in parentheses or not. Anything else after it has no meaning in any rule,
    `syntax` (E4); which of these places R10's "a variable, a field or an element" admits is the
    checker's.
  - Every type the parser reads takes the suffix `*`, since R10 lets a pointer type stand anywhere a
    type stands, a type argument among them, where C# refuses one. Its trial parses take it too, so
    a statement `a * b = c` declares `b` a pointer to an `a`, as C# reads `a * b = c;`, while
    `x = a * b` stays a product.
  - `int*?` is `syntax` (E4): a pointer type is no `T?` (R10), and the record turned `int*?` down as
    the nullable form (§3, round 17). `int?*` parses as a pointer to an `int?`, and whether that is a
    pointee is the checker's (R10).
  - `naked` and `extern "C"` stand among the other modifiers in any order, as C# orders modifiers
    (R12, R16). `thread` is one only right before `static` (L18, R13). `naked` before a method or
    before a function declared inside a block of code, and a block of C functions anywhere but at
    the top level of a file, are `syntax` (R12, R16), raised by the front end, which reads each
    all the same, so that the rest of the file is read.
  - `static mut` with a type and a name and no initial value parses as a declaration, which the
    checker judges (`static-state`, R13); a variable without one is `syntax` everywhere else.
  - `extern "C"` right before `(` starts the type of a C function, R17's form. So the result of a
    function of the C calling convention is never a type in parentheses alone: no type that crosses
    to C (§2, R16) is written in parentheses. `extern` with a string other than `"C"` has no meaning
    in any rule, `syntax` (E4).
  - A block of C functions holds a declaration with no body on each line (R16), with modifiers,
    types and defaults the checker judges. `...` stands last in such a declaration (R16) and nowhere
    else (E4). A variable there is `syntax` (E4): no rule gives it a meaning, since §14 holds open
    whether a block declares a C library's variables, and no case stands on it.
  - `asm` is a statement of its own (R11), and `syntax` where a value stands (E4). The string
    literals that open its arguments are its templates, which the lexer reads with no hole (R11,
    L6). Its operands parse in Rust's shape (R11): `name =`, a word, a list in parentheses, a value
    and `=> place`, or `label` with a block; `const` takes a value and `sym` a path, both read as a
    value with no list. A word or a
    register class that is not Rust's parses, and is the checker's (`asm-operand`, R11). Inside the
    value of an operand `=>` splits the operand and starts no lambda, while an argument of a call
    there takes one as any argument does.
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
M9 for lists), 8 (functions, without F9 and F10 across the files of a program: it reads one file, but for the runtime's `use sys`, whose `pub` functions it checks), 9 (outcomes), 10 (control
flow) and 11's raw forms (R7, R10 to R17), and the rules of `print` and of a value's text that
these chapters reach: A1 to A4, A8, A10 for a type parameter, A11, A12 and A14. A program's
grants, `raw`, `native` or both (R7, R16), are given to it: the corpus theory gives a case those
of its `// allow:` line, and a program given none has none, as one without a project file; a
grant other than these two is none R7 and R16 name, and the checker refuses it as its caller's
error. A register class is read against x86-64's table with the features every x86-64 target
has, the target a case is read for where its header names none (R11). It declares every data type, function, block of C functions and state outside
a function of the top level first, in source order (each is visible in the whole file, F6, R13,
R16), resolves the fields and bases of the data types and then judges the pointees their fields
name, then the C declarations and the signatures, checks the state's initial values and the
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
`cannot-infer`, E4's `syntax` for a count of type arguments it does not have (the reading on E4
below), or the id of the argument that does not fit, `narrowing-conversion` among them. A `match`
and a postfix `else` are checked for the cases they cover (C4, C9, O5, O8), and a case the
calling function does not list after the first case of its return type propagates as
`unlisted-case` (O1 to O3, O9); a cycle of `data` bases is `base-cycle` (D6), where the order in
which D6 reads the files of a program, the order of their paths, is not reached, since the checker
reads one file. One defect, one diagnostic (E1): a value the checker could not type has
the error type, which fits anywhere and is reported nowhere again.

It raises these ids of the table: `type-mismatch`, `narrowing-conversion`, `sign-mix`,
`constant-overflow`, `constant-divide-by-zero`, `inexact-literal`, `redeclared`, `unknown-name`,
`unused-variable`, `assign-immutable`, `capture-assign`, `mut-required`, `mut-at-call`,
`argument-mismatch`, `ambiguous-call`, `cannot-infer`, `duplicate-function`,
`default-not-constant`, `base-cycle`, `missing-return`, `throw-needs-value`, `unlisted-case`,
`match-not-exhaustive`, `unreachable-arm`, `reversed-range`, `break-outside-loop`,
`string-index`, `no-text`, `raw-not-allowed`, `raw-required`, `counted-pointee`, `asm-operand`,
`naked-not-allowed`, `naked-body`, `static-state`, `native-not-allowed`, `native-type`, and
`syntax` for the shapes E4 leaves to it (the readings below).

### What the checker does not read

A program that uses one of these gets `not supported` at the construct's line and no error that
comes from the construct (E1), but where the construct holds an error of its own, a grant's or
`raw-required` (the reading on R7, R12 and R16 below), which is then its one diagnostic. What stands inside the construct is still read, with its diagnostics
dropped, so that the variables it uses count as used, but for the operand of `*` and `&` and the
body of a lambda where a type stands that the checker does not read: these are checked, and an
error of their own, a name no declaration has among them, is reported beside the line. The list,
which is the complete one: a `class`, an `interface`, an `enum`
or a `flags` type and a value of one (chapter 7, D12, D13, and the A-rules of their text); `null`,
a nullable type, `?.` and `??` (chapter 4), `null` where no pointer stands among them; `weak` (R3,
R5); a pointer to a type that holds a type parameter, which R10 judges by the type (T19), and the
address of state outside a function, which R13 says is no variable, where R10 names a variable
(R10, R13); the address of a place through a pointer, `&*p`, `&p[i]` and `&(*b).Next`, where
R10 says `&x` needs no `raw` block and Rust takes such a place in `unsafe` code alone (R10); an
operand of `asm` of a class Rust takes only as a clobber, `kreg0`, `x87_reg`, `mmx_reg` and
`tmm_reg`, by the class or by one of its registers, which Rust takes with `out` and `_` alone, an
operand of a class Rust gives a target with a feature beyond those every x86-64 target has,
`ymm_reg`, `zmm_reg` and `kreg`, or in one of their registers, `xmm16` to `xmm31` among them,
since no rule names the features of a build, a name on an operand in an explicit register, which
Rust's Reference allows and Rust's compiler refuses, the template naming it nowhere, and
`sym` of a function with more than one overload or with type parameters, a symbol being one
function (R11, R12); `thread
static` before a function (R13); a `decimal` in a C declaration or in the place of `...`, a
number of Kurz that no rule says crosses to C, which leaves the declaration and its calls unread
(R16), a function of the package's own in the C calling convention with type parameters, beside
another function of its name, with a `mut` parameter or with a type that cannot cross to C, for
which no rule names an id, which leaves its body read with its diagnostics dropped and its calls
unread (R12, R16; K7, M6), the value of a C function that takes `...`, which the type of a C
function does not write, the type of a C function with a type that cannot cross to C, and a data
type in C's layout (R16, R17); the text of a raw pointer, of a C function value and of a value
that holds one, which no rule gives, where A4 gives every other value a text derived from its
type (A4, R10, R17); and `use` (F10) of any folder but the runtime's `sys`;
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
- R7 with E1: every `raw` block in a package without the grant is `raw-not-allowed`, one inside
  another among them, since each is a block the grant would allow; its body is checked as one with
  the grant is, so a read through a pointer there raises nothing more.
- R7, R12 and R16 with E1: a `naked` function in a package without `allow raw`, and a function of
  the C calling convention in one without `allow native`, gets the grant's error alone, and an
  `asm` outside a `raw` block gets `raw-required` alone, where the list above names a part of them
  that the checker does not read: that part is read with its diagnostics dropped.
- R10 with E4 and T1: `&` before the name of a function, a C function or a type is `syntax`, since
  R10 gives `&` a variable, a field or an element; an offset that is no `int` or `long` and no
  integer that widens into one (T8, T23), a `ulong` among them, a pointer where a `long` or a
  pointer to another type stands, and `p - q` or a comparison of pointers to two types are
  `type-mismatch`, since R14's forms alone convert a pointer and R10 counts elements of one
  pointee. No type is expected of a literal offset, so it takes the one L10 gives it, a `long` for
  `p[3000000000]`. `n + p` is the offset `p + n`, as C# writes both.
- R10 with F11: a function value holds what it captures (F11), counted references among them, so
  a pointer to a type that holds one is `counted-pointee`.
- R10 with E1: a read and a write through a pointer outside a `raw` block are a `raw-required`
  each, so `*p = *q` is two, as Rust reports each operation that needs `unsafe`.
- R11: each breach of the template's rules is an `asm-operand` of its own, at the line of the
  template, of the operand or of the word the rule names, so a name the template uses that no
  operand has and an operand the template does not use are two; a template whose braces or
  placeholders cannot be read is one, and its operands then count as used (E1).
- R11: an operand in an explicit register is read against Rust's tables of x86-64's registers: a
  smaller view or an alias is the register itself (`eax`, `rax` and `al` are `ax`), and the type a
  value in it takes is that of the register's class, `reg`, `reg_byte` or `xmm_reg`; `sp`, `bp`,
  `bx` and `ip` and their views are `asm-operand`, as Rust takes none of them as an operand; a
  register that two inputs, or two outputs, name is `asm-operand` at the second, an `out`, an
  `inout` and an `inlateout` being an input and an output each, as Rust's compiler counts them,
  since an `out` may write its register before every input is read and a `lateout` is, in the
  Reference's words, "identical to `out` except that the register allocator can reuse a register
  allocated to an `in`", so an input and a `lateout` of one register are not; such an operand
  stands after the operands without a name, as Rust's rule says, and the template names it
  nowhere. `clobber_abi` takes outputs in explicit registers.
- R11: a pointer and a C function go into `xmm_reg` as into `reg`, since Rust's table counts a
  pointer and a function pointer as the 64-bit integer, which `xmm_reg` takes; `const` or `sym`
  with a block after the word, or with `=>`, is `asm-operand`.
- R12 with R11: the body of a `naked` function is `naked-body` at its first statement that is not
  its one `asm`, at the second `asm` where it holds two, and at the declaration where it holds
  none; an operand there other than `sym` and `const` is `naked-body`, `clobber_abi` and `options`
  among them, since R11 counts both among the words of the operands, and the template's rules are
  checked beside it, so that operand unused by the template is `asm-operand` too, while Rust's rules
  of the outputs and of `label` are not, every output and `label` there being `naked-body` already.
- R13 with F12 and V10: a declaration of state gets one `static-state`, judged in the order R13
  lists its causes: the grant, the type, the initial value, `pub`. The initial value is read where
  a function's default is, with the functions, types and state of the file visible, the state
  declared after it among them (F6), and no variable of the top-level code, so such a variable
  there is `unknown-name`. A read of state in an initial value or a default is no constant, and
  that error is the value's one, with no `raw-required` beside it (E1). State whose name is
  `redeclared` is read with its diagnostics dropped, and its name then stands for state of the
  error type, where no other state has it (E1).
- R14 with R15 and T1: `void` has no size, so `SizeOf<void>()` and `List<void>.FromRaw(p, n)`,
  which counts elements of that size, are `type-mismatch`, as R14 says of `p + n`, `p - n` and
  `p - q` of a `void*`. `Ptr<T>(x)` takes a pointer, a `long` or an integer that widens into one (T8, T23),
  as a `long` parameter takes it, and a `ulong` is `type-mismatch`.
- R16: each argument in the place of `...` that C would widen is a `native-type` of its own. A
  `mut` parameter is `native-type` at the declaration, and a call of the declaration is read with
  `mut` before that argument or without it (E1).
