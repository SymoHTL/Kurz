# The Kurz compiler

The compiler reads the language the reference under `reference/` states, rule by rule, and the
corpus under `corpus/` is what it is measured against. This directory holds a .NET solution:

- `Kurz.Compiler/`: the library. `Text/` reads a source file (L1: UTF-8, a byte-order mark
  skipped, the two-byte line break of Windows editors read as one, in a `"""` block too), `Syntax/`
  turns it into tokens and a syntax tree (the syntax of chapters 1 to 11: every declaration,
  statement and expression form the reference shows), `Semantics/` checks a tree the parser read
  without an error against the rules of chapters 1 to 3, 5, 6, 8 to 10, the raw forms of 11 and
  the calls on files of 13 (the section "What the checker reads" below) and says `not supported` to a construct of the
  other chapters, and keeps what it found out, node by node, for a code generator
  (`Semantics/SemanticModel.cs`: the type and constant of every expression, the variable or
  function every name means, the variable every declaration makes, the function every call picks
  with the parameter each argument fills, what every lambda captures), `Emit/` turns a program the
  checker read in full, with the runtime of its target, into LLVM's IR (the section "What the
  emitter emits" below), `Corpus/` reads the header of a corpus case in the shape `reference/00-about.md`
  states, its grant line among it (R7), and `Build/` finds the clang of the release of LLVM the
  compiler pins, carries and checks the runtime, builds a program from its source to an executable,
  makes the places `run` and `exec` build and run a program in, and is the command (the section
  "The command" below).
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
  fifth checks the runtime under `runtime/` and the standard library under `std/` (the section "The
  runtime" below). A sixth builds
  every corpus case that expects a run with the pinned clang and runs it, and checks what it
  prints, its exit code and its error line against the case's header, then builds and runs it again
  under an allocator that makes every block it frees inaccessible (`RunTests`); a seventh
  builds the programs that pin what no case can, a refused write and a refused allocation among
  them (`EmitterTests`), and an eighth those of data values and unions, with the width of a tag and
  the copy of a block before a write (`DataTests`); a ninth makes and removes the places of `run`
  and `exec` under a directory of its own (`PlaceTests`).

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

`kurz build [--release] [--lines] file.kz [-o output]` builds the program for the machine that
builds, the test build unless `--release` says otherwise, a release build with the line tables a
test build has where `--lines` says so (the owner, 2026-10-29, "build, run and exec"), into the
executable beside the source, named
after it, `hello.exe` on Windows, or where `-o` says (design record, section 2). It writes over an
executable that is there, writes nothing on standard output, and exits with 0. A program the
compiler refuses gets its lines on standard error, one per diagnostic in the order of their lines,
and no executable, and the command exits with 1:

    hello.kz:3: unknown-name: `y` is not declared
    hello.kz:2: not supported: `s.Chars` as a list (T15)

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

`kurz run [--release] [--lines] file.kz` builds the file as `build` does and runs it, and
`kurz exec [--release] [--lines] "program text"` builds its argument and runs it, the argument's lines named
`exec` in a refusal and in a failure the program reports (`exec:2: unknown-name: ...`,
`error: overflow at exec:2`) (design record, section 2). Each builds into a place of its own,
`place-` and 32 hexadecimal digits, under the directory `kurz` in the user's cache directory as
Rust's `dirs` crate names it: on Linux the directory `XDG_CACHE_HOME` names where it is an absolute
path, else `.cache` under the home directory, which is `HOME` or the user database's entry, and on
Windows the known folder LocalAppData, each taken as the system names it, whether or not it is
there, as `dirs` takes it (the owner, 2026-10-29, "As Rust's dirs"). On Linux the
directory `kurz` the command makes and every place are the user's alone, 0700 (the owner,
2026-10-29, "Owner only, 0700"). A place holds the lock file `lock`, which the command opens so that
no child inherits it, takes the lock of, `flock`'s on Linux and `LockFileEx`'s on Windows, waiting
for it, and writes the mark `kurz place 1` into before anything else is written to the place; the
executable is `program`, `program.exe` on Windows, beside the build's other files. The program runs
in the caller's directory with no argument and with the command's standard input, output and error
(the owner, 2026-10-29, "Inherit all three"); while it runs, the command ignores Ctrl-C, and on Linux
SIGQUIT, so that the program alone ends by them (the owner, 2026-10-29, "Command waits it out"). The
command then removes the place, its other files first and the lock file last, and ends with the
program's exit code, on Linux 128 and the signal's number where a signal ended the program, 130 for
Ctrl-C. At its start, before the build, it removes every place whose lock it can take and whose lock
file holds the mark and nothing else, a place a run cut short left, and leaves a place whose lock
file is missing or holds anything else alone. A removal that fails is passed over without a word
(the owner, 2026-10-29, "Silent"). A program refused, or a source not read, makes no place and ends
the command with 1, as do the failures of `build` above that a run shares and these, in the owner's
words (the owner, 2026-10-29, "Keep these words"), the first on Linux and the second on Windows:

    error: no directory for the command's places
      note: XDG_CACHE_HOME is not set to an absolute path, and no home directory is found in HOME or the user database
      help: set HOME, or XDG_CACHE_HOME to an absolute path

    error: no directory for the command's places
      note: Windows names no LocalAppData folder for this user
      help: run the command as a user with a profile

    error: the place for the run cannot be made
      note: <what the system said>
      help: check that <the directory of the places> can be written

    error: the built program cannot be started
      note: <what the system said>
      help: check that programs may run from <the directory of the places>

`CommandTests` and `PlaceTests` pin these. That the command outlives Ctrl-C and SIGQUIT, and that
no child inherits the lock file, they check on Linux alone, reading `/proc`: on Windows nothing
checks either.

Any other command line gets the usage on standard error and the exit code 1:

    usage: kurz --version [--verbose]
           kurz build [--release] [--lines] <file.kz> [-o <output>]
           kurz run [--release] [--lines] <file.kz>
           kurz exec [--release] [--lines] "<program text>"
           run and exec pass no arguments to the program

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
- `run` takes `--release`, `--lines` and the file, each once, in any order, and nothing else, `-o`
  among it; `exec` takes `--release` and `--lines` before its text alone, each once, and one
  text, which may be empty: an empty
  text is an empty program and ends with 0. A missing file or text gets the usage. Pinned by
  `CommandTests`.
- The name of a place, of its lock file and of the executable, and the text of the mark, are this
  directory's. On Linux the lock file is opened through `open` with `O_CLOEXEC` and locked through
  `flock`, called directly: .NET's own open of a file takes a `flock` of its own that does not
  wait, under which the maker the record lets wait would fail
  ([a .NET open takes a lock that does not wait](../knowledge/a-dotnet-open-takes-a-lock-that-does-not-wait.md)).
  Pinned by `PlaceTests`.
- The program runs in the caller's directory, and the command waits for it however long it runs.
  Pinned by `CommandTests.The_program_takes_the_commands_input_output_and_error_no_argument_and_the_callers_directory`
  and, on Linux, by the endless program of
  `CommandTests.On_linux_an_interrupt_or_a_quit_ends_the_program_alone_and_the_command_ends_with_its_code`.

## The runtime

The runtime a built program links is written in Kurz, in `runtime/` at the top of the repository
(design record, section 2): `runtime/common/` holds the code every target shares, and
`runtime/linux/` and `runtime/windows/` the code of one target each. The standard library's Kurz
code is in `std/`: the allocator of section 3 and the blocks of a list and of a string (the owner, 2026-10-24, "A std/ directory now"), at
the top of the repository beside `runtime/` (an assumption of the record, section 2). The build of the compiler embeds every `.kz` file of
both, by its path from the top of the repository (`Build/Runtime.cs`, `Runtime.Files`), and a build
compiles `std/` with every program as it compiles the runtime. Code in `common/` reaches the functions of the target it is built for as the
folder `sys`: it writes `use sys` and calls `sys.Write(...)`, and `sys` is that target's directory
with the directory of the build's architecture read into it (the owner, 2026-10-23, "Rust's sys"),
a directory this compiler does not read (below). The checker reads `use sys` at the top of a file of
`common/`, and a call qualified by `sys` picks among the folder's `pub` functions; a function of the
folder that is not `pub` is `not-visible` there (F9), and a name the folder does not declare is
`unknown-name` (V10). `std/` reaches the target's directory as `sys` in the same way, and `common/`
reaches `std/` as the folder `std`, `use std` and `std.Allocate(...)` (the owner, 2026-10-24, "As built").

`Runtime.CheckEveryTarget` checks the runtime for every target, each folder granted `raw` and
`native` (R7, R16): the target's directory on its own, then `std/` with it as `sys`, then `common/`
with it as `sys` and `std/` as `std`. It is also red where a function `common/` or `std/` calls has
other parameters or another result in one target's directory than in another's, the line naming
which of the two calls it; a function only its own directory calls can differ freely (record,
section 2, round 20). A line of `common/` or `std/` that the check for every target reports stands
once, and one that the check for some targets reports names them, `(in the check for windows)`. The
compiler reads one file in each of the four directories and no directory inside them: a second file
of a directory, which section 8 makes one folder, the directory of an architecture inside a
target's (`runtime/linux/x86-64/`, where round 20 places that code) and a directory inside `std/`
are `not supported`, and a
directory of a target it does not know, a directory inside `common/` and a directory of no file are
refused, each named by its path. The checks and the layout are pinned by `RuntimeTests`, which also
holds what the compiler carries to the files of the tree.

A build checks the runtime for its target and emits it into the program's module, its functions
under IR names of their own; nothing of it is compiled apart (record, section 2, round 17). The
emitted `main` calls the runtime's `Start`, then the program's top level, then `Finish`, which ends
the process with the exit code 0 through the target's `Exit`, the runtime's one exit (section 6).
`common/` gives what the emitted code calls: `Print`, the text of an integer, of a `char` and of a
`float` and a `double` (below), the arithmetic, the conversions and the text of a `decimal` (below), the
text of a duration and of a timestamp (below), the
joining and the comparison of two strings, the text of a list from the texts of its items,
`Raise` for an exception of the run-time table and `Throw`, `AllocateString`, the room of a new string in
a block of `std/` (R15, below), and `Allocate`, which takes its memory
from the allocator's entry, `std/`'s `Allocate` (record, section 2), and ends the process where the
entry gives null (the owner, 2026-10-24, "Null, runtime ends it"), as Rust's `GlobalAlloc::alloc`
returns null and leaves the end of the process to a caller, which its documentation encourages to
call `handle_alloc_error`. The entry gives a block of one of fifteen size classes, the powers of two
from 16 to 262144 bytes: the block of its class freed last, else one carved from the thread's
memory, which it takes from the system's `Memory` a mebibyte at a time; an allocation of more than
262144 bytes takes memory of its own from `Memory`. It gives null where the system refuses. `std/`'s
`Free` takes a block back with the size it was asked for, as Rust's `GlobalAlloc::dealloc` takes
the layout a block was allocated with: a block of a size class to the front of its class's free
blocks, where the next request of the class takes it, and a larger one to the system through the
target's `Release`; the mebibytes a thread carved from stay its own. A target's directory gives
`Start`, `Write`, `Exit`, `Memory`, `Release`, `Trace` and `Crashing`: on Linux over the C library,
`Start` setting the signal of a closed pipe to ignored and installing the handler of a fault (below),
`Write` calling again where a signal interrupted it (section 6), and `Release` calling `munmap`, on Windows over `kernel32`, `Release` calling `VirtualFree`. The
Linux file is the code of 64-bit Linux, where C's `size_t` is 64 bits as `long` is.

The Linux file also gives the calls on files and sockets of chapter 13 (section 12), which the emitted
code calls and nothing of `common/` or `std/` does, so that the Windows file gives none of them (S11). Each takes
a path as the address of its bytes ended by a NUL byte, which `common/`'s `CPath` copies from a string,
null where the string holds a NUL, and `FreePath` gives back; each returns a number of 0 or more, or a
failure below 0, the system's error number negated, or -4096 for the name of a directory's entry that
is not UTF-8. `ErrorKind` gives a failure's `IoKind`, the kind Rust 1.82's `decode_error_kind` gives the
number on Linux, `Other` where Rust's is `Uncategorized`, and `ErrorCode` its number, 0 for -4096 and
below (S1). `OpenFile` opens close-on-exec, a file it
creates with the permissions 0666 less the `umask` (S2); `OpenFile` and `ReadFile` call again where a
signal interrupted them; `WriteFile` calls `Write` again with the rest and returns the bytes written
before a call that took none (S4); `CloseFile` takes an interrupt for no failure; `FileSize` and
`FileExists` read `stat` into a page each thread maps at its first use, at the offsets of x86-64's
`struct stat`; `CreateDirectories` creates the directories above that are missing, as Rust's
`fs::create_dir_all` does, and `ListDirectory` reads the names `readdir` gives at byte 19 of the GNU C
library's `struct dirent` (S8). `Listen` and `Connect` take an address as `CPath` copies it, read
its port after its last `:` and its host by `inet_pton` where it is a literal of IPv4, or of IPv6 inside
`[` and `]`, and else by `getaddrinfo`, and try each address until one binds or connects, as Rust's
`each_addr` does, with the addresses in a page each thread maps at its first use; they fail with -4097,
`InvalidInput`, for an address that is no host and port or a name that gives no address, and -4098,
`NotFound`, for a lookup that fails with no error of the system. `Listen` sets `SO_REUSEADDR`, listens
with room for 128 and reads the port by `getsockname`; `Connect` calls again where a signal interrupted
it and takes `EISCONN`; `Accept` calls `accept4` close-on-exec, again where a signal interrupted it; each
socket is opened close-on-exec (S9). `std/` gives the room a read makes in a list of bytes before it,
`BytesReserve`, `BytesPiece`, `BytesEnd` and `BytesAdded`, and `ListClear` (S3, S6, M9).

The text of a `float` and of a `double` is computed in `common/` from the value's bits, which the
emitted code passes as a `uint` and a `ulong`: the fewest digits that read back as the same value,
of those the nearest, by Ryu's algorithm as Rust's `ryu` crate computes it with its small table (each
128-bit power of five, and of its inverse, from one of thirteen rows and the power of five below 26
between, with a correction of two bits per power, the inverse powers' a table of this runtime's own,
since it multiplies the row's inverse power itself where `ryu` multiplies it less one, and a `float`
through the high 64 bits of the same powers), laid out as .NET Core 3.0 and later write a `double` and a `float` with the invariant culture
(A4, A11, A14): the exponent form from `1E+17` for a `double` and from `1E+09` for a `float` (the
owner, 2026-10-28, "As .NET prints: 1E+17" and "As C# prints a float"), and below `1E-04`, with `E`,
a sign and two digits or more. Where .NET 10 writes a text that does not read back as the value, as
for 2^-25 and 2^-958, the text is A4's, the fewest digits that do. `FloatingTests` holds the text of
20000 values of a xorshift sequence, of the largest value with each exponent, the smallest and the one
after it, and of the powers of ten and their neighbours, of a `double` and of a `float`, to what .NET
writes.

A `decimal` (T2, T30) is computed in `common/` from its two words, which the emitted code passes as
two `ulong`s: the low 64 bits of its 96-bit integer, then its high 32 bits with System.Decimal's
flags above them, the scale in bits 48 to 55 and the sign in bit 63, as System.Decimal holds them.
`DecimalAdd`, `DecimalMultiply` and `DecimalDivide` compute the exact result in up to 192 bits and
round it half to even at the largest scale up to 28 at which it fits 96 bits, as .NET's `DecCalc`
does, its choices of the scale of a product and of a quotient included, and report a result beyond
the range, which the emitted code raises as `overflow`; `DecimalRemainder` computes the exact
remainder (the owner, 2026-10-28, "Exact remainder"), and none of them gives a zero it computes a
sign. `DecimalCompare` compares the values, `DecimalFromDouble` rounds a `double`'s exact binary
value, `DecimalToDouble` divides as C# does, `DecimalTruncate` drops the fraction for a conversion
into an integer type, and `TextOfDecimal` writes the digits with the point at the scale and a `-`
before a value whose sign is set (A8). `DecimalTests` holds the sum, the difference, the product,
the quotient, the remainder, the comparisons and the conversions into `double`, `float` and `long`
of 10000 pairs of a xorshift sequence and of the pairs of 34 values at the edges of the range to
what System.Decimal computes, a zero they compute without its sign, and the remainders it raises
for to the exact ones; and 20000 `double`s and as many `float`s of a xorshift sequence converted
into `decimal` to their exact binary value rounded, which `BigInteger` computes.

A duration and a timestamp of either width (T13, T29) reach `common/` as the two words of their
128 bits, the low one first, a narrow one extended by its sign. `TextOfDuration` splits the
nanoseconds into the units of A12 and `TextOfTimestamp` writes the instant as A16 says, the date
by Howard Hinnant's `civil_from_days`, both dividing the 128 bits by numbers below 2^32 in 64-bit
words, so that no division of 128 bits needs a function of the C library. `TimeTests` holds the
text of 3000 durations of a xorshift sequence of 0 to 127 bits and of the edges of both widths to
the units read off with `BigInteger`, and that of 4000 timestamps and the edges of the calendar to
.NET's calendar moved by whole cycles of 400 years.

The error lines are the record's (section 6): `error: <id> at <file>:<n>` for an exception of the
run-time table, the file named as the command line gave it, and `error: thrown at <file>:<n>:` for
a `throw`, with the thrown value's text on the line below it. A `print` passes its file and its line
to `Print`, which raises `print-failed` there where the system refuses the write or a write takes
no byte, as Rust's `write_all` fails on a write of zero bytes, in every build (the owner,
2026-10-23, "Caller passes it"). An allocation the system refuses ends the process with
`error: allocation-failed (<n> bytes)`, the size asked for, and no place (the owner, 2026-10-23, "Id +
size"), for a string's block the 16 bytes of its counts among them (section 4, as for any allocation). Both
end with the exit code 1. `EmitterTests` pins both, the first in a test and in a release
build, the second with a runtime whose `Memory` refuses every request.

## What the emitter emits

The emitter (`Emit/`) takes a program the checker read without an error and without `not
supported`, with the runtime checked for its target and the standard library, and writes one LLVM
module of the three, which the
pinned clang compiles unoptimised, at `-O0`, in a test build, as Cargo's `dev` profile builds, and at
`-O2` in a release build (the owner, 2026-10-29, "As Rust's dev profile, -O0"), and links with the C
library as the platform links it, on Linux its math library `libm` with it, as Rust's standard
library links it through its `libc` crate (checked 2026-10-29: the `libc` crate's `src/unix/mod.rs`,
which links `m` beside `c` on Linux), whose `fmod` an unoptimised `%` of a `double` calls, and with the
library a block of C functions names (R16), the Linux runtime's own block among them, which names
`gcc_s` for the unwinder of the trace (below). It emits:

- the integers, `float` and `double`, `bool`, `char` and `string`, and pointers to them and C
  function types over them (T2, T21, T15, R10, R17); a string is the address of its bytes and their count, at most the
  largest `int` of them, a longer one, by a join, by `string.Allocate` or by `string.FromRaw`, ending the program with
  `allocation-failed` (T15), the count's highest bit marking a string that holds a block in front of its bytes, which
  a literal does not (below), and the empty string's bytes are a constant of none, never at the address 0 (R15);
  `s.Bytes` as a `Bytes`, which takes a count of the block of a string made while the program runs and copies the
  bytes of a literal, `s.Bytes[i]`, read from the string in place, and `string.FromUtf8(b)` and
  `string.FromUtf8Lossy(b)`, whose string takes a count of the block of the bytes where every sequence is UTF-8,
  their case `InvalidUtf8` a data type every file sees, declared before the file's own, and the lossy text, where a
  sequence is not UTF-8, made by the runtime with one U+FFFD for each maximal part of one (T15; section 4, the
  owner, 2026-10-29, "Union, as Rust");
- `enum` (D12, D18, D20): a value is its number, or its place among the values where the declaration
  writes none, in the smallest of `i8`, `i16` and `i32` that holds every one, signed where a number is
  negative, as Rust stores the value of an enum without fields (section 4, an assumption there); `==`
  and `!=` compare the values, `<`, `<=`, `>` and `>=` their numbers, `.Number` widens the value to
  an `int`, `Plan.From(n)` picks the value by a switch on the numbers or makes the case `Invalid`, a
  data type every file sees, and the text of a value is a literal of its name (A4);
- variables, assignments, functions with their defaults and arguments by name (D11), `mut`
  parameters given a variable (M7), and a generic function once per list of type arguments it is
  called with (T19, T27);
- the arithmetic and the comparisons of chapter 3, which in a test build raise `overflow` where a
  value leaves its type (T5) and `divide-by-zero` (T14, T25) and wrap in a release build (T4), the
  operators on bits and the shifts (T22, T28), and the conversions written as calls (T10, T20); on a
  `float` and a `double` IEEE 754's operations, as C# has them, by zero `/` giving an infinity or NaN
  and `%` NaN, NaN equal to nothing (T18, T25), an integer widened into them (T23), and a conversion
  into an integer type that drops the fraction and, where it loses the value, raises `overflow` in a
  test build and saturates in a release build, NaN to 0 (T26);
- `decimal` (T2) as two 64-bit words, the runtime's (above), 16 bytes: its literals as
  System.Decimal's bits, `+ - * / %` and the comparisons through the runtime, which raise
  `overflow` where a result leaves the range and `divide-by-zero` in every build (T30, T25), unary
  `-` turning the sign, a zero's too (T30), `==` by the value, also on a field (D3), an integer
  widened into it (T23), a conversion into an integer type as from a `double`, and the conversions
  between it and `float` and `double`, NaN, an infinity and a value beyond its range raising
  `overflow` in every build (T26);
- the time types (T13, T24, T29): a `duration` and a `timestamp` as the `i64` of their nanoseconds,
  a `longduration` and a `longtimestamp` as the `i128`, 16 bytes aligned to 16; their literals, `+`
  and `-` as the checker types them and the unary `-` of a duration, which raise `overflow` where a
  result leaves its type's range in every build (T31), the comparisons, `==` also on a field (D3), a
  narrow value widened beside or into the wide pair (T29), a conversion between the two widths, which
  raises `overflow` in every build where the value does not fit (T31), and their text (A12, A16);
- `if`, `while`, `for` over a range, which raises `reversed-range-at-run-time` (C8), `break`,
  `continue` and `return` (chapter 10), and `throw` of a value (O6);
- `data` types and their values, made with defaults and arguments by name (D1, D2, D8, D11), their
  fields (D14), `with` (D5), an assignment into a path, its value first (M4), a value of a derived type where its
  base is required (D6, D7), `==` field by field, a `float` or a `double` field as C#'s `Equals`
  compares it, NaN equal to NaN (D3), and never equal across types (D3, D15), and their
  text, a generic type's under the name its declaration gives (A3);
- unions (D9), whose tag is the number of the case's type in the program, as wide as the largest
  number among the cases needs (section 5, round 19), their text, the text of the case's value
  (A4), `==` on a field of one, by the case and its value (D3), a call whose result is one, kept
  whole (O4) or used where its success is required, with its other cases passed on to the caller
  (O1, O2), a success case without a value (O10), `match` with arms of a type, of a value and
  `else` (C4, C9, O8), postfix `else` after a call (O5), and `throw` of a data value and the bare
  `throw` of an arm of `else`, which throws the case that reached it (O7);
- lists (M9): `List<T>()`, `Bytes()` and the type `Bytes`, the second name of `List<byte>`, which a
  message names so (section 4, the owner, 2026-10-29, "Bytes = List<byte>"), `Add`, `Count`, `list[i]`,
  which raises `index-out-of-range` for an index below 0 or at `Count` or beyond (T14), `list[a..<b]` and
  `list[a..b]`, a new list of a copy of those items, which raises `reversed-range-at-run-time` for ends in the
  wrong order and then `index-out-of-range` for a start below 0 or an end past the count (section 4, the owner,
  2026-10-29, "A List<T>, a value"), `Where`, which calls its test once for each item, in
  order (section 4, the owner, 2026-10-28, "Once each, in order"), and gives a new list of the items
  it kept, `for` over a list, which walks the list as it was when
  the loop started (C6), their text (A4), and lists of every type the emitter lays out, in
  variables, parameters, fields, unions and lists; an `Add` past the largest `int` of items ends the
  program with `allocation-failed` and the bytes of its items after the `Add` (M9); `Clear`, which
  lets go of the items and keeps the room, or of the block where another value holds it (M9, M3);
- files and sockets (chapter 13), in a build for Linux: each call of `File`, `Directory`, `TcpListener`
  and `TcpStream`, each of a file's and a stream's `Read`, `Write` and `Close` and each of a listener's
  `Accept` and `Close` calls the Linux runtime's function (above), and a result below 0 makes the
  case `IoError`, its kind and number from `ErrorKind` and `ErrorCode` and its path the call's first,
  whose text is the words of its kind and its path, the system's number after the words of `Other` (S1); a `File`, a `TcpListener` and a `TcpStream` are the address of a block of 32 bytes,
  the count of the values that hold it, the descriptor, a listener's port, which `l.Port` reads, and the path or the address, which a stream from `Accept` takes from its listener (S9), counted as a list's block is and
  closed at zero, the error of that close ignored, and `Close` writes -1 as the descriptor before it
  closes, so that every call after it gets the system's error of a descriptor that is not open (S5);
  `Read` makes the room for `max` more bytes as an `Add` makes it, copying a block another value
  holds, before it reads (S3), and `File.ReadAll` reads into the room the list has, else 8192
  bytes more, no more than the largest `int` of items leaves, while a read gives bytes, and a list that
  holds that many reads one byte more, which raises `allocation-failed` where the file has it (S6). `FileTests` pins what no case can: 200 opens of a
  file, and the files and directories the calls on a path open for themselves, under a limit of 32
  descriptors, and the same files all held, which reach the limit; a close through a copy; a read
  into a list another value holds; a count and a `max` past the largest `int`; a whole file of the
  largest `int` of bytes, lowered to 20 in the library's text, and one byte more; a read and a write in
  the wrong direction; the modes under two masks; the close-on-exec flag the system shows; a directory
  created again and one under a file; the text of an error without a path; and the refusal for Windows;
- lambdas (F7) and function values (F11): a function value is the address of its lambda's code and
  of its environment, which holds the values the lambda read when it was made (F15); a lambda that
  reads no variable has none and allocates nothing, and a call of a function value calls its code
  with its environment (section 4, the owner, 2026-10-25, "Pair + heap env");
- `print` and the text of a value in a string's holes (A1, A2, A8, L6), and `==` on strings by
  their bytes (T18);
- the raw forms the runtime is written in: `raw` blocks, pointers, their arithmetic, `*p`, `&x`
  and `p[i]` (R10), `Ptr<T>(x)`, `long(p)` and `SizeOf<T>()` (R14), `s.Pointer`, `s.Bytes.Count`,
  `string.Allocate(n)` and `string.FromRaw(p, n)`, `l.Pointer`, which for a list without a block is the alignment of
  its items as an address, not null, as Rust's `NonNull::dangling`, and `List<T>.Allocate(n)` and
  `List<T>.FromRaw(p, n)`, also through `Bytes` (R15; section 3, the owner, 2026-10-29, "List.Allocate, take
  over"), state outside a function, `thread static` among it, laid out
  after the layout knows the program's data types, a `data` value without fields as its initial
  value (R13),
  calls of C functions with `...` (R16) and of a C function value (R17).

A test build, unoptimised, keeps every call a call, so that a recursion runs as deep as the program
wrote it (record, section 2), which
`EmitterTests.The_test_build_keeps_every_call_and_the_release_build_turns_a_tail_call_into_a_loop`
pins. A test build, and a release build built with `--lines`, carry the line tables, LLVM's debug
information of the lines alone, as clang's `-gline-tables-only` writes it: one compile unit, each file
named as a compile error names it, in the directory the build ran in (the owner, 2026-10-29, "As
Rust: the build's directory"), the runtime's and the standard library's by their path in the
repository, `std/list.kz`, a subprogram per function of the program, the runtime and the
standard library define, under the name a trace shows (section 6; the owner, 2026-10-29, "Type.Method,
Max<int>, std path"): `main` for the top-level code, `Max<int>` for an instance, `Twice.lambda` for a
lambda, and the line of each statement and each expression on the instructions emitted for it. On
Linux they are DWARF in the executable, on Windows CodeView in the PDB the linker writes beside it,
for which clang gets `-g`. On Linux LLVM writes the subprogram of a line-only unit into DWARF only
where a call was inlined into the function, which an unoptimised build never does
([a line-only unit writes no subprogram on Linux](../knowledge/a-line-only-unit-writes-no-subprogram-on-linux.md)),
so there the executable's symbols name a function, each carrying the name a trace shows between
the prefix of its code and the numbers that keep it unique (`f.Down.1`, `f.Max<int>.8`,
`f.Twice.lambda.3.1`, `kz.main`; section 6, an assumption there); CodeView keeps the subprograms'
names.
`EmitterTests.A_test_build_and_a_release_build_with_lines_carry_line_tables_under_the_names_a_trace_shows_and_a_release_build_does_not`
and `CommandTests.Build_writes_the_line_tables_into_a_test_build_and_into_a_release_build_where_lines_says_so`
pin them. A raise by code passes its file and its line to the runtime as constants (section 6).

The layout of a data value and of a union is the compiler's own, which no form of the language
outside `raw` shows (section 5, round 19; D10), and the owner kept it as described here on
2026-10-23 ("As built"). A `data` type with neither a base nor a type derived from it, and that
holds no value of its own type through its fields, is a plain value of its fields whatever its
size, where D10 names small values. One with a base, a type derived from it or a value of its own
type in a field is a pointer to a block that holds the number of the value's own type, then its
fields, the base's first, so that a block of a derived type reads as one of its base (D6, D7).
`with` and an assignment into a path through such a value copy the block before the write, so that
no other variable sees it (D4, M4). Pinned by
`DataTests.A_copy_by_with_of_a_derived_value_through_its_base_keeps_its_type_and_leaves_the_original`,
`DataTests.An_assignment_into_a_path_through_a_block_copies_the_block_so_that_another_variable_keeps_its_value`
and `DataTests.A_data_type_that_holds_a_value_of_its_own_type_is_a_block_read_and_written_as_any_value`.

A list is the address of its block, null for a list without items, which takes no block, as Rust's
`Vec::new` allocates nothing. The block holds the count of the list values that hold it, 8 bytes,
the count of its items and the count it has room for, 4 bytes each, and from its 16th byte the
items, each laid out as its type is (`std/std.kz`). A copy of a list copies the address and raises
the count. An `Add` writes in place where one value holds the block and it has room, grows the
block where it has none, as Rust's `Vec` grows, to twice its room, at least 8 items of a byte, 4 of
up to a kibibyte and 1 of more, and where other values hold the block it copies the items into a
block of its own first (M3; section 3, the owner, 2026-10-24, "Full counting now"). An argument that
changes the list through a `mut` parameter is `changed-while-held` (M7), so the order in which an `Add`
reads its argument and its list is not seen. What `Add`'s argument made besides its item, the
environment of a lambda it gave to a call among them, is let go of before the list is read, so that
no count it held makes the list copy its items: the environment counts what its lambda read (F15),
as M7 states for the environment (section 4, the owner, 2026-10-28, "Pass, as Rust") and assumes for
the rest. The count
is lowered where a holder ends: at the end of the block that declared a variable, and at a
`return`, a `break` or a `continue` for the blocks it leaves; at an assignment over the value; at
the end of its statement for a value that a call, a `with`, a `match` or an `else` made and that
nothing keeps; and where a value that holds the list ends. At zero the lists the items hold are let
go of in turn, and the block goes back to `Free`. A parameter borrows its argument and changes no
count: nothing changes the value it borrows while the call runs, since the checker refuses a read of
what a call takes as `mut` that the expression still holds when the call runs, or when an assignment
or a `mut` method in an arm's statement inside the expression changes it (M7), and such a read of a
type that is copied, a function value among them, is counted once more, as a value the expression
made (the owner, 2026-10-25, "Copied"), and no state
outside a function holds a list or a string, which R13 refuses, so a callee reaches a list or a
string its caller lent it through its parameters alone. The name an arm of a `match` binds holds a count of its value, as a
variable does, so that the arm can change the subject's variable and the name keeps its value (M3).
Counted are a list, a string, a function value, a plain `data` value that holds one of them in a
field and a union with a case that holds one. A string made while the program runs holds a block
of `std/` in front of its bytes, laid out as the block of a list of bytes: the count of the values that
hold it, 8 bytes, then the count of its bytes, which `string.FromRaw` writes, and the room
`string.Allocate` gave, 4 bytes each, so that `s.Bytes` and a string of a list of bytes share the
block, and a list that holds it copies it before it writes, as for any shared block (M3); the
highest bit of the string's count of bytes marks such a string,
and a literal, which holds no block, is neither counted nor freed, a copy and the end of a holder
testing that bit and touching no memory for a literal (section 4: the bit the owner's, 2026-10-29, "Bit
in the count", the block and its room assumed). A string is counted where a list is, with no copy
at a write, since a string never changes, and at zero its block goes back to `Free` with the room
it was given. The runtime makes every string it makes in such a block, through `string.Allocate` and `string.FromRaw`, which takes
the block over (R15, the owner, 2026-10-29, "string.Allocate, take over"); the text the emitted code
makes of a value, for a `print` or a hole of a string, is a temporary of its statement, an
interpolation of one hole of a string takes a count of the string, a new value as Rust's `format!`
makes one (the owner, 2026-10-29, "New value, as format!"), and the helper that makes the text of a
list or of a `data` value lets go of each text it joined. The environment of a lambda that reads variables starts with the
count of the function values that hold it, 8 bytes, and the address of the function that lets go
of it, then holds each value the lambda read, counted once more where its type is counted; it is
counted as a list's block is, and at zero the values it holds are let go of and it goes back to
`Free` (section 4, "Pair + heap env"). A `throw`, which no `else` catches, since an `else` takes the cases a call returns (O5),
and a raise end the process with their error line, and no count is lowered there.
A value of a block type is never freed, and the lists and strings it holds are never let go of: the
memory of such a value, which the answer of 2026-10-23 leaves ungiven back (below), is read to hold
the lists and strings in its fields, a reading of this directory, not a decision. Pinned by
`ListTests`: the values the counting keeps apart, the name an arm binds among them, strings in every
place the lists' programs hold a list, one made in a `raw` block among them, a function value
and a `data` value that holds one read beside a call that replaces them, the function value a call
calls beside such a call, a lambda's list that only its environment holds and the items `Where` keeps; a system that
refuses every request for memory after sixteen, within which a program that makes and drops lists
through every way out of a block, the body of a `void` function and a lambda's environment among
them, for 3000 rounds ends, and the same lists all kept do not; an allocator that makes every freed block
inaccessible, under which the values' programs and twenty rounds of the same program print the
same lines, and so do the strings' program and twenty rounds of strings through every way out of a block;
the target's own `Release`, after
which a write into a freed block of more than 262144 bytes ends the process, and under which lists
of 70000 items made and dropped for 200 rounds stay within 64 mebibytes of the system's memory, and
the same lists all kept do not, and so for strings of 524288 bytes; strings of 1024 bytes made and dropped
for 3000 rounds within the sixteen pieces, and five of them kept each round not; the count
`string.Allocate` asks past its limit, lowered in the runtime's text, and the bytes of a string's
block the system refuses; the number of `allocation-failed` past the limit, with the limit
lowered in the library's text, for items of 1, 8 and 16 bytes, a padded `data` value, a list and a
value of a block type, and for a growth the system refuses, and for an environment; the order of
the calls of `Where`'s test; the release of such an environment before the `Add` in the IR;
`index-out-of-range` for an index below 0, at `Count` and beyond; a part of a list outside it or with
its ends reversed, and parts at both ends with ends of `uint` and of `long`; `string.FromUtf8` and
`string.FromUtf8Lossy` over 23 sequences, which give what Rust 1.82's `str::from_utf8` and
`String::from_utf8_lossy` gave for them on 2026-10-29, under both allocators; a list that takes over
the room `List<T>.Allocate` gave and grows from it, and the pointer of a list without a block; and the
bytes of `allocation-failed` for `List<T>.Allocate` past the limit and refused, for `List<T>.FromRaw`
past the limit and for the bytes of a literal refused. The strings' program and its rounds hold the
bytes of a string, a string of bytes, a part of a list of strings and a list made in a `raw` block.

### What the emitter does not emit

A program that uses one of these gets one `not supported` at the construct's line, in the
checker's shape, and no executable: a generic `data` type with a base or a type
derived from it (D6, T19) or that holds a value of its own type (D1, T19), `SizeOf<T>()` of a
`data` type or a union (R14), a `mut` parameter given anything but a variable and a `mut` parameter of a lambda
(M7), a `naked` function (R12), inline assembly (R11), an assignment into an index (M9), an `Add` or a `Clear` on a list in
a field (M5), `==` on a value that holds a list (D3), `s.Chars` as a list (T15), a call on a file or a socket in a build for
Windows (S11), and a construct of the other chapters the checker does not read. The line
is the one where code first needs the construct or the type, in the words of the part the emitter
does not lay out, so a declaration no code reaches builds (the owner, 2026-10-23, "At first use").
The reference counting of section 3 is emitted for lists, strings, the environments of lambdas and
the values that hold them (above) and for nothing else: memory a value of a block type takes is never
given back, the lists and strings it holds among it (above), so a program that makes such values
without end runs out of memory and ends with `allocation-failed`; a plain value and a union take
none of their own (the owner, 2026-10-23, "Accept, documented", which named strings beside them,
before the answers of 2026-10-29 above). The corpus runner lists every case the checker reads in
full and the emitter does not emit, 3 of the 273 cases that expect a run, and on Windows the 9 of files besides, with the words the emitter says, and refuses a stale entry; the 55 the
checker says `not supported` to are held by the checker's theory and not built. The handler of a
stack overflow is built on Linux and not on Windows, where a stack overflow
ends the program as the system ends it and not with its error line (section 6), so there
`outcomes/stack-overflow.kz` is built and not run, the one case the runner lists so.

On Linux an exception that ends the process writes the trace of the calls below its error line
(section 6), but `allocation-failed`, which writes no place and no trace (O11): the runtime's `Trace` (`runtime/linux/linux.kz`) walks the stack it raised on with
`_Unwind_Backtrace` of `libgcc_s`, the shared library a program links for it, reads the executable's
own file, `/proc/self/exe`, through three pages it maps from the system, and takes a frame's name from
`.symtab`, its line from `.debug_line` and, in a release build built with `--lines`, the calls
inlined at it from `.debug_info`; it writes the frames whose symbols start with `f.` or `std.`, and
`kz.main`, `main`, where it ends, and leaves out every other, the runtime's and the compiler's own
functions. A hundred frames are written, then `  ... N more frames`, `  ... 1 more frame` for one,
the frames past the hundredth only counted, with what the reads found for a return address kept in
the third page, in a table of 251 slots chosen by the address, and `KURZ_TRACE=0` switches it off. Windows writes no trace: the reader of the PDB through `dbghelp.dll` that section 6
gives is not built, its `Trace` writes nothing, and the error line stands alone. `TraceTests` pins the forms, the names, the cut, the variable, the release build with and
without `--lines` and an executable stripped of its symbols or of its line tables, on Linux; the
corpus runner holds the trace of every crash it runs to its error line's place and to `main`, or, where it
is cut, to a hundred frames above the cut line (E3), and every other test that compares a crash's standard
error sets `KURZ_TRACE=0` for the program it runs.

On Linux the runtime's `Start` installs the handler of a fault (section 6) for the process's one
thread, as Rust's standard library installs its own: `Faults` maps a stack of 64 KiB with a page
below it that faults, takes the page below the lowest address `pthread_getattr_np` gives for the
thread's stack as its guard page, and installs `OnFault` for `SIGSEGV` and `SIGBUS` on that stack
with `SA_SIGINFO`, `SA_ONSTACK` and `SA_NODEFER`. A fault outside the guard page sets the signal's
default back and returns, so that the system ends the process, the exit code 139 in a shell for
`SIGSEGV`. A fault in it while a crash path runs, which `Raise`, `Throw` and the end of a refused
allocation mark first through `Crashing`, ends the process with 1 and nothing more written; any other
writes `error: stack-overflow at <file>:<n>` with the line of the call that did not fit: the frame
the fault interrupted holds it where the instruction there is a call, and otherwise the frame below
it, at its return address less one; where that frame is the runtime's or the C library's, the
innermost frame outside them. The note of section 6 stands in place of the place where the line is
not known, then the trace follows from that frame, and the process ends with 1. The emitter gives
every function `uwtable`, so that the unwinder walks from a fault in a prologue, and on Linux
`"probe-stack"="inline-asm"`, so that a frame larger than a page probes each page; on Windows the
backend calls `__chkstk` for such a frame, and the attribute would name the function it calls. A
stack overflow while a thrown value's `Text()` runs comes before `Throw` marks the crash path and is
written as a stack overflow, where section 6 ends the process with the throw's error line alone: that
window is not built. `TraceTests` pins the error line and the cut trace in a test build and in a
release build with and without `--lines`, `KURZ_TRACE=0`, an overflow inside the runtime, and a fault
outside the guard page, where the overflow of a release build with `--lines` runs past the 30 seconds
of a test's run when every frame past the cut reads the file; `EmitterTests` pins the attributes of
every function for both targets.

### Readings the record leaves open to the emitter

Each is a reading of this directory, not a decision. A test pins each one, and a design round can
ask about it.

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
  comparisons, and after a name when a member and its `(` follow, R15's `List<T>.FromRaw(p, n)`
  and `List<T>.Allocate(n)`, the forms the reference gives a generic name that is not called; C# also keeps the list
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
generic data type as a base), 5 (data types: D1 to D12, D14, D15, D18 and D20), 6 (values: M1 to M5, M7, and
M9 for lists), 8 (functions, without F9 and F10 across the files of a program: it reads one file, but for the runtime's `use sys`, whose `pub` functions it checks), 9 (outcomes), 10 (control
flow), 11's raw forms (R7, R10 to R17) and 13 (files and sockets: S1 to S11; S12's grants are a
package's, and the checker reads a program), and the rules of `print` and of a value's text that
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
`unused-variable`, `assign-immutable`, `capture-assign`, `mut-required`, `mut-at-call`, `changed-while-held`,
`argument-mismatch`, `ambiguous-call`, `cannot-infer`, `duplicate-function`,
`default-not-constant`, `base-cycle`, `enum-number`, `missing-return`, `throw-needs-value`, `unlisted-case`,
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
which is the complete one: a `class`, an `interface` or a `flags` type and a value of one
(chapter 7, D13, and the A-rules of their text); `null`,
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
name (D16), and `equal by` (K8); among the bases of a `data` type a class, an interface or a
flags type (chapter 7, K10, D13), the explicit form of inheritance (K17) and a
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
  path as an arm that names no value of an enum (`p.X`, which the parser reads as a pattern for one,
  D12), where C9 has a literal; an enum's name where a value stands (`print(Plan)`), a call of it
  (`Plan(2)`, the C# form section 4 rejects for `Plan.From(2)`), `Plan.From` without its call, and
  `extern "C"` before an `enum`, which no rule gives a layout of C; a `match` used as a value whose every arm throws, or whose arm is a statement or a
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
- E1 with D20: an enum whose declaration was reported, a name written twice, a member's name or a
  number that breaks D20, gives `.Number`, `From` and an order of its values without a second
  error, and keeps every value but the one the defect named.
- M7 with D12: a value of an enum is copied, as a number is, being a number in memory (above), as
  Rust copies an enum without fields that derives `Copy`.
- R16 with D12: an enum cannot cross to C, `native-type`, since its layout is the compiler's
  (above) and no rule gives it C's, as Rust asks `#[repr(C)]` of an enum it passes to C.
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
- M7's reads, a reading of this directory of the rule the owner chose on 2026-10-24 and 2026-10-25:
  the checker reads each expression of a statement whole, in the order it runs: a call's receiver or
  the value it calls, then its arguments as written (D11), an operator's left operand, an index's
  target and a `with`'s target before what follows them, and an interpolation's holes as written, a
  hole of a `string` holding what it read until the interpolation is made and a hole of another type
  made text where it stands (the owner, 2026-10-29, "Refuse, as Rust"); a hole closes on its line, so
  what changes a place inside one is a `mut` argument. Either side of `??` is its value, a reading
  no program shows, since the checker says `not supported` to `??` (chapter 4). A statement inside an arm is read on its own, while
  what it changes, a `mut` argument, an assignment or a `mut` method, changes it within the
  expression around the arm; and one `changed-while-held` is reported for each change, at its line,
  however many reads it meets and statements it stands in (E1). `Add`, the one `mut` method the
  checker reads (a method of a data type is not, D16), changes its receiver after its argument ran,
  so a read of the receiver in its argument meets no change (the owner, 2026-10-25, "Allow, as Rust's
  clone"), while a call in its argument that changes the receiver is the error; the emitter's `Add`
  counts its item before the list grows. Having a `void` success (O10), it stands only as a
  statement, an arm's among them. A lambda in its argument that reads the receiver, as the
  argument's value or an arm's, holds the receiver while `Add` changes it (the owner, 2026-10-25,
  "Refuse, as Rust's borrow"), and so does one given to a call or a `with` there whose value is of a
  type that can hold a function value: the checker follows the value through every part whose type
  can hold one, so `fs.Add(Wrap(() => fs.Count))` is the error, and a value of a type that can hold
  none ends it, so `xs.Add(Count(() => xs.Count))` and `xs.Add(Count(Wrap(() => xs.Count)))`, whose
  `Count` gives an `int`, are not (the owner, 2026-10-25, "Refuse if it can keep it", and 2026-10-28,
  "Pass, as Rust"). As M7 says, a type parameter can hold one, and so can a
  `data` type when one of its type arguments can, whether a field stores it or not (section 4, the
  owner, 2026-10-28, "Counts, as Rust"), or when a field of it, its type read with the type's
  arguments in place of its parameters as section 4 assumes, or of a type derived from it can
  ("confirmed"), at any depth: a type met again on the way, `Q<() => int>` through the field `N` of a
  `data Q<T>(int X, List<Q<() => int>> N)` read at `Q<int>`, answers by its type arguments
  alone, since its fields were read at the first meeting, which ends the walk for a type that holds
  itself with other arguments, `P<List<T>>` in a `P<T>`. A map, a nullable type and a class, which
  section 4 assumes can hold one, the checker says `not supported` to (M9, N1, chapter 7) before the
  walk asks. A function value is copied (the owner, 2026-10-25, "Copied"), and
  so is a `data` value or a union made of function values and copied types ("Copied too"): the
  checker marks a read of one that a change meets while the expression holds it, and the emitter
  counts it once more for the expression (above), the cost the design record names.
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
- R14 with R15 and T1: `void` has no size, so `SizeOf<void>()`, `List<void>.FromRaw(p, n)` and
  `List<void>.Allocate(n)`, which count elements of that size, are `type-mismatch`, as R14 says of `p + n`, `p - n` and
  `p - q` of a `void*`. `Ptr<T>(x)` takes a pointer, a `long` or an integer that widens into one (T8, T23),
  as a `long` parameter takes it, and a `ulong` is `type-mismatch`.
- R16: each argument in the place of `...` that C would widen is a `native-type` of its own. A
  `mut` parameter is `native-type` at the declaration, and a call of the declaration is read with
  `mut` before that argument or without it (E1).
