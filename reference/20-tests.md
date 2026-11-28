# 20. Tests

A program's tests are `test` blocks at the top level of its files, which `kurz test file.kz` runs one at a
time, each in an actor of its own, and `check`, a statement that stands in any code (§2, §11; issue 586,
comments 6992687784 and 6993110710, and the owner's answers of 2026-11-20, comment 7051499099, each rule
marked as it stands), and `advance`, which moves the runtime's clock in a test (Y10). The cluster test of
§13 and `mock` are read and answered `not supported` (Y8, Y9).

### Y1 (decided, §11) `test "name" { ... }`, and its words

A test is a block `test "name" { ... }` at the top level of a file: the word `test`, a string literal of
one line, plain or raw (L20), with at least one character, its name, and a block that opens on the line of
the name, as an `if`'s does (C1). The name is printed as written, so that it fits its result line (Y3); the
empty name, `""` or `r""`, and a `"""` block (L13), raw or not, are `syntax` at the line of the `test` (E4)
(§11, the owner, 2026-11-20, issue 586, comment 7051499099, Q12 (a) of comment 6998786049, which replaces
the reading of the build said back there, every literal with no hole a name). A hole in the name is `syntax`
at its line (E4) *(assumed: F19 of issue 586, comment 6993110710, a reading of the build said back there)*.
A `test` block inside a declaration or a block, a function's, a lambda's, an `if`'s, a class's or another
test's, is `syntax` at the line of its `test` (E4), and the parser reads the block and goes on with what
follows it; a second test of one name in a program is `syntax` at the line of the second, its text naming
the line of the first *(assumed: D3 of issue 586, comment 6992687784, said back there)*. The names are
compared by their value, so that `"A"` and `"\u0041"` are one name (§11, the owner, 2026-11-20, issue
586, comment 7051499099, Q10 (a) of comment 6994632453 at its lean).

`test`, `check`, `mock` and `advance` are words only where their form stands (L18): `test` at the start
of a statement before a string literal, or before `cluster` and a token that can start an expression and
is no `[`; `check` and `advance` at the start of a statement before a token that can start an expression
and is no `[`, so that `check(x)` and `advance(d)` are those statements, as `const assert(` is the
statement of T37; and `mock` at the start of a statement where a type, `with` and a type follow it.
Before `=`, `.` and `[`, and everywhere else, each is a name, and a variable named so keeps its meaning
there. A call of a variable named `check` or `advance` at the start of a statement is the statement, as
every other `const assert(` at the start of a statement is (T37): a function value so named is not called
there, and a declaration of a variable whose type is so named, `check x = 1`, is a `check` of `x` with
`= 1` after it, `syntax` at its line (E4) *(assumed: D2 of comment 6992687784, said back there, and F9 of
comment 6993110710, a reading of the build said back there, for `advance(d)` and for the declaration)*.
That `check(x)` at the start of a statement is the statement, so that a program that calls a function value
named `check` there reads as a `check`, is F9 as comment 6994632453 said it back (§11, the owner,
2026-11-20, issue 586, comment 7051499099: that reading stands).

Case: [tests/test-in-function.kz](../corpus/tests/test-in-function.kz)
```kurz
void Check() {
    test "inside a function" {
        print(1)
    }
}

Check()
```

Case: [tests/test-name-twice.kz](../corpus/tests/test-name-twice.kz)
```kurz
test "adds" {
    check 1 + 1 == 2
}

test "adds" {
    check 2 + 2 == 4
}
```

Case: [tests/test-name-empty.kz](../corpus/tests/test-name-empty.kz)
```kurz
// a test's name has at least one character, so that no result line reads `test "" ... ok`
test "" {
    check 1 + 1 == 2
}
```

Case: [tests/test-name-block.kz](../corpus/tests/test-name-block.kz)
```kurz
// a test's name is a string literal of one line, so that it fits its result line, and a `"""` block is none
test """
    adds
    """ {
    check 1 + 1 == 2
}
```

Case: [source/test-words-as-names.kz](../corpus/source/test-words-as-names.kz)
```kurz
test = 1
check = 2
mock = 3
advance = 4
print(test + check + mock + advance)
```

Case: [source/check-call-is-the-statement.kz](../corpus/source/check-call-is-the-statement.kz)
```kurz
// `check(x)` at the start of a statement is the statement, a `check` of `(x)`, so that the function value the variable
// `check` holds is not called there, and is called by another name
check = (bool b) => print("called {b}")
check(true)
f = check
f(false)
```

Case: [source/check-before-bracket-and-dot.kz](../corpus/source/check-before-bracket-and-dot.kz)
```kurz
// before `[` and `.` at the start of a statement the word `check` is a name, so that a variable named so keeps its meaning there
mut check = [1, 2, 3]
check[0] = 7
check.Add(4)
print(check[0])
print(check.Count)
```

Case: [source/test-and-mock-as-type-names.kz](../corpus/source/test-and-mock-as-type-names.kz)
```kurz
// `test` before `cluster` and `=`, and `mock` before a name and `=`, start declarations of variables whose types are named so
data test(int N)
data mock(int N)

test cluster = test(1)
mock x = mock(2)
print(cluster.N + x.N)
```

Case: [source/check-declaration-is-a-check.kz](../corpus/source/check-declaration-is-a-check.kz)
```kurz
// `check` before a name at the start of a statement is the statement, so that a declaration of a variable whose type is
// named `check` is a `check` of `x` with `= check(1)` after it
data check(int N)

check x = check(1)
print(x.N)
```

### Y2 (assumed, §2, §11) `kurz test`, the tests it runs and their order

`kurz test file.kz` runs every `test` block of the program that file starts, of its files and never of
`std/`'s, one at a time, in the order of the blocks in each file, and runs no top-level code of the
program *(assumed: D1 of issue 586, comment 6992687784, said back there)*. The files, which D1 names and
does not order, run in §4's order of a program's files, their paths relative to the program's root
compared code point by code point (§2, the owner, 2026-11-20, issue 586, comment 7051499099, Q11 (a) of
comment 6998786049 at its lean), an order the build, which reads one file and follows none, does not
reach. It takes `--release`, `--lines` and `--cpu <name>` before the file, as `kurz run` takes them (§2,
the owner, 2026-11-20, comment 7051499099: reading m of comment 6998786049, a reading of the build said
back there, stands), `--seed <N>` there too (Y11), and no argument after the file: `--` or another argument there is an error of its
usage, since a test reads no `args` (Y5) *(assumed: F17 of comment 6993110710, a reading of the build said
back there)*. It has no filter by name, no parallel tests and no format of its output but text
*(assumed: D4 of comment 6992687784, said back there)*, and takes a seed (Y11). A case of the corpus that runs its program's tests
carries the header line `// command: test` (chapter 0) *(assumed: the corpus line of comment 6993110710, a
reading of the build said back there)*.

Case: [tests/two-tests-in-order.kz](../corpus/tests/two-tests-in-order.kz)
```kurz
// the top-level code does not run under `kurz test`, and the tests run in the order of the file
print("top")

test "first" {
    print("in first")
}

print("between")

test "second" {
    print("in second")
}
```

### Y3 (decided, §2) What `kurz test` writes, and its exit code

`kurz test` writes a line for each test on standard output, in the order the tests run, unless an error
ends the process (Y5): `test "name" ... ok`, or `test "name" ... FAILED (seed <N>)`, the seed of the run
(Y11; the parentheses assumed in §2), with the words that failed it
indented under it, the words of the log line of the crash that failed it (U24), after its `crashed: `, or
those of Y5's `deadlock`, which is no crash; then a summary, `<n> tests: <p> passed, <f> failed`, each
count written, a zero among them, and `test` in place of `tests` where the program has one,
`1 test: 1 passed, 0 failed` (§2, the owner, 2026-11-20, issue 586, comment 7051499099, Q3 of comment
6992687784 at its lean, and reading n of comment 6998786049, a reading of the build said back there,
which stands) *(assumed: F1 and F21 of comment 6993110710, readings of the build said back there)*:

```text
test "rename fails for unknown user" ... ok
test "survives 60 days" ... FAILED (seed 1234)
    check-failed "names.Count == 2 (3 == 2)" at users.kz:12
2 tests: 1 passed, 1 failed
```

A test's own `print` goes to standard output as it runs, so that its lines stand before the line of its
result *(assumed: D5 of comment 6992687784, said back there, and F4 of comment 6993110710, a reading of
the build said back there)*. The command exits with 0 where every test passed and with 1 where one failed,
the code of a refused program (§2, the owner, 2026-11-20, comment 7051499099, Q4 of comment 6992687784
at its lean).

Case: [tests/check-fails-in-test.kz](../corpus/tests/check-fails-in-test.kz)
```kurz
test "adds" {
    check 1 + 1 == 2
}

test "counts" {
    names = ["a", "b", "c"]
    check names.Count == 2
}
```

Case: [tests/one-test.kz](../corpus/tests/one-test.kz)
```kurz
test "adds" {
    check 1 + 1 == 2
}
```

### Y4 (decided, §2) A program with no test

`kurz test` on a program with no `test` block writes `error: no test in users.kz` on standard error, and
after it, on standard error too, a `help:` line that names the form, ``help: write a test as
`test "name" { ... }` at the top level of the file``, indented by two spaces (§2, the owner, 2026-11-20,
issue 586, comment 7051499099: reading k of comment 6997332522, a reading of the build said back there,
stands), and exits with 1, so that a run that checked nothing does not look like a pass (§2, the owner,
2026-11-20, comment 7051499099, Q6 of comment 6992687784 at its lean).

No case: the refusal of a program with no test is the command's own, with no id (E2), which a case cannot
expect; `CommandTests` holds its line, its `help:` line and its exit code.

### Y5 (decided, §11) A test runs in an actor of its own

Each test runs in an actor of its own, a child of the runner. The test fails where that actor crashes, by
a `throw`, a failed `check` (Y6), `overflow` in a test build (T5), an overflow of the test's own stack,
the crash of its actor on Linux (U19), or a deadline, or where the runner's wait for it meets `deadlock`
(U18), which then names the test's line, `deadlock at users.kz:4`, since a parked wait keeps no place of
its own; the run goes on with the test after it (§11, the owner, 2026-11-20, issue 586, comment
7051499099, Q2 of comment 6992687784 at its lean) *(assumed: F5 of comment 6993110710, a reading of the
build said back there, for the line `deadlock` names)*. A test's code runs as its actor's, its calls of
one chain (U17), and the runner's wait meets `deadlock` where the test's actor waits on a message only it
can run, a block of its child's policy among them, as `tests/test-deadlock.kz` shows. *(assumed: a reading of U17 and U18, proposed with the reading of comment 6998754650 and not put to the owner)* The runner is the
root actor, which runs the tests in place of the program's statements, and each test's actor is its child
with the policy `stop` (U25), so that a crash of the test's actor fails that test and nothing restarts
(§11, the owner, 2026-11-20, comment 7051499099, Q13 (a) of comment 6998786049 at its lean). A `deadlock` that a call an actor's start keeps out closes (U22) names that
call's line in place of the test's, the line the top-level code's error names for that loop (U18) (§11,
the owner, 2026-11-21, #599, comment 7063693195, Y5's line 1), in any file, the test's or a file the
test's file `use`s or the standard library (§11, the owner, 2026-11-21, #599, comment 7067131310, the
file 1); where more than one call is kept out, the words name every one, the first place in source
order as `deadlock at <file>:<n>` and each further place on a line `kept out at <file>:<n>` under it,
as the top-level code's error does (U22) (§11, the owner, 2026-11-21, #599, comment 7067131310,
several 2; comment 7067139953, the form 1), in the order, with the counts and with the cap of U22,
each line indented by four spaces as every word under a `FAILED` line (Y3) (§11, the owner,
2026-11-21, #599, comment 7067639209, the order 1, the same place 1, the cap 1, the indent 1). Where no
call that an actor's start keeps out closes the loop and one walk from the test's call runs into a loop
that a crash closes (U64), the words are the test's line and, under it, the loop's waits, each with its
chain's ID, in the form the top-level code's error writes them below its error line (U63), the test's line ending with
no colon, as it ends where no loop stands under it, each line
indented by four spaces as the calls kept out are, as `tests/test-deadlock.kz` shows (§11, the owner,
2026-11-23, #622, comment 7095825069, 1, "Yes, list the waits") *(assumed: the indent of the calls kept
out, comment 7067639209, for these lines, which the answer did not name)*. An error that
ends the process wherever it happens (U19), `allocation-failed` among them, and on Windows an overflow of
an actor's stack, at which the build ends the process as the system ends it (U19), ends the run inside a
test too: its error line stands on standard error, and neither that test's line, nor the tests after it,
nor the counts are written, and the exit code is 1 (§11, the owner, 2026-11-20, comment 7051499099:
reading l of comment 6997332522, a reading of the build said back there, stands). The crash of a test's
own actor writes no line on standard error, its words standing under the test's `FAILED` line (Y3), and
the crash of an actor the test spawned keeps its line (U24). At a test's end every actor it made is
dropped at once, parked ones too, as U13 drops what is queued at a program's end; the numbers of actors
(U2) go on counting, each test's own actor taking one as a spawned actor does, so that the actors of a
test are numbered after those of the tests before it, so that where each of two tests spawns one child,
the first test's child is `#2` and the second's `#4`, and a socket or a file its actors hold stays open
*(assumed: F3, F6 and F22 of comment 6993110710, readings of the build said back there)*. No log line names a
test's own actor, since its crash writes none, and the line of its wait in a loop under a `FAILED` line
names it by the name `test` that F2 of comment 6993110710 gives it in the log line of a crash, `test#1` in
`tests/test-deadlock.kz` *(assumed: F2, a reading of the build said back there)*. A test has no bound of time: a test that waits on a
wait the runtime does not own, a call on a file, whose wait runs on the pool of threads for blocking
calls, or a `raw` call of a C function that blocks (§11), holds the run *(assumed: D6 of comment
6992687784, said back there)*.

A test is checked as a function of its file with no parameter and a `void` result is: its body reads no
variable of the top-level code, `unknown-name` at the read, and a declaration in the test of a name the
top-level code declares is `redeclared` (F16), and no `args` (S13), and a failure case that can leave it
is `unlisted-case` (O3) *(assumed: D1 of comment 6992687784, said back there, and F17 and F18 of comment
6993110710, readings of the build said back there)*.

Case: [tests/test-reads-top-level.kz](../corpus/tests/test-reads-top-level.kz)
```kurz
total = 3

test "reads the total" {
    check total == 3
}

print(total)
```

Case: [tests/test-deadlock.kz](../corpus/tests/test-deadlock.kz)
```kurz
// the block of the child's policy runs in the test's actor once its message ends, and that message waits on the child: a loop
// that the crash closes, whose waits stand under the test's `FAILED` line, each with its chain's ID, written `<chain>`
actor Worker {
    pub void Fail() {
        throw "down"
    }

    pub int Get() => 1
}

test "waits on its own block" {
    w = spawn Worker() on crash (c) {
        restart
    }

    send w.Fail()
    print(w.Get())
}

test "after" {
    print("after")
}
```

Case: [tests/test-restart-reentrant.kz](../corpus/tests/test-restart-reentrant.kz)
```kurz
// at its restart `A`'s constructor waits on `B.Ask`, whose call of `Peek` waits in `A`'s inbox for that constructor, so that the
// runner's wait for the test finds nothing left to run, and the words under its `FAILED` line name that call of `Peek`, not the test

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()
    List<int> items = [1, 2, 3]

    pub int Peek() => items.Count

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

test "restarts into a call it keeps out" {
    b = spawn B()
    a = spawn A(b)
    print(a.Get())
    b.Set(a)
    send a.Fail()
    print(a.Get())
}
```

Case: [tests/test-restart-reentrant-two.kz](../corpus/tests/test-restart-reentrant-two.kz)
```kurz
// at their restarts `A`'s and `C`'s constructors each wait on a call whose call back waits in the inbox for that constructor, so
// that the runner's wait for the test finds nothing left to run, and the words under its `FAILED` line name both calls, `A`'s first

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask() {
        x = a
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor D {
    mut C? c = null

    pub void Set(C x) {
        c = x
    }

    pub int Ask() {
        x = c
        if x != null {
            return vouch x.Peek()
        }

        return 0
    }
}

actor A(B Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

actor C(D Other) {
    int first = Other.Ask()

    pub int Peek() => first

    pub void Fail() {
        throw "c fails"
    }

    pub int Get() => first
}

test "restarts two actors into calls they keep out" {
    b = spawn B()
    a = spawn A(b)
    d = spawn D()
    c = spawn C(d)
    print(a.Get())
    print(c.Get())
    b.Set(a)
    d.Set(c)
    send a.Fail()
    send c.Fail()
    print(a.Get())
}
```

Case: [tests/test-restart-reentrant-places.kz](../corpus/tests/test-restart-reentrant-places.kz)
```kurz
// each test restarts `A`s whose constructors wait on `B.Ask`, whose call back waits in the inbox for that constructor, from places
// of `Ask` in the order the list gives, so that the runner's wait for the test finds nothing left to run; the words under its
// `FAILED` line name the places in the file's order, each place that keeps out two calls with their count, at most ten lines
// `kept out at` and then one line for the places past those

actor B {
    mut A? a = null

    pub void Set(A x) {
        a = x
    }

    pub int Ask(int k) {
        x = a
        if x == null {
            return 0
        }

        return match k {
            0 => vouch x.Peek()
            1 => vouch x.Peek()
            2 => vouch x.Peek()
            3 => vouch x.Peek()
            4 => vouch x.Peek()
            5 => vouch x.Peek()
            6 => vouch x.Peek()
            7 => vouch x.Peek()
            8 => vouch x.Peek()
            9 => vouch x.Peek()
            10 => vouch x.Peek()
            11 => vouch x.Peek()
            else => vouch x.Peek()
        }
    }
}

actor A(B Other, int K) {
    int first = Other.Ask(K)

    pub int Peek() => first

    pub void Fail() {
        throw "a fails"
    }

    pub int Get() => first
}

int KeepOut(List<int> places) {
    mut sum = 0
    mut all = List<A>()
    for k in places {
        b = spawn B()
        a = spawn A(b, k)
        sum = sum + a.Get()
        b.Set(a)
        all.Add(a)
    }

    print(sum)
    for target in all {
        send target.Fail()
    }

    return all[0].Get()
}

test "keeps out calls of thirteen places" {
    print(KeepOut([12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 12]))
}

test "keeps out calls of three places, two of them twice" {
    print(KeepOut([12, 1, 0, 1, 0]))
}

test "keeps out two calls of one place" {
    print(KeepOut([0, 0]))
}
```

Case: [tests/test-child-escalates.kz](../corpus/tests/test-child-escalates.kz)
```kurz
actor Worker {
    pub void Fail() {
        throw "down"
    }

    pub int Get() => 1
}

test "child escalates" {
    w = spawn Worker() on crash escalate
    send w.Fail()
    print(w.Get())
}
```

Case: [tests/two-tests-fail.kz](../corpus/tests/two-tests-fail.kz)
```kurz
// both tests fail, the run going on after the first, and `throws` names the words under the first `FAILED` line (E3)
test "throws" {
    throw "down"
}

test "throws again" {
    print("ran")
    throw "again"
}
```

### Y6 (decided, §11) `check`

`check condition` is a statement that stands in any code, the top-level code, a function, a method, a
lambda and a test, in every build *(assumed: F8 of issue 586, comment 6993110710, a reading of the build
said back there)*, `check(x)` among its forms (Y1). Its condition is a `bool`, read as a condition is
(C3): a value of another type is `type-mismatch` at its line, a nullable one `nullable-unchecked` (N2),
and a call with a `void` success `syntax` (O10). A `check` narrows nothing: after `check x != null` the
variable `x` is still nullable in the statements after it, `nullable-unchecked` at a use that needs its
value (N2), as Rust's `assert!` leaves an `Option` an `Option` (§11, the owner, 2026-11-20, issue 586,
comment 7051499099, Q9 (a) of comment 6994632453 at its lean).

A false condition crashes the actor that runs it with the run-time error `check-failed`, at the file and
the line of the `check`. Its text is the condition as the source writes it, each run of spaces, line
breaks and comments between two of its tokens one space, and for one of the six comparisons, `==`, `!=`,
`<`, `<=`, `>` and `>=`, alone or in parentheses, whose two sides show their values as a hole of an
interpolation shows them (L6), each side's value after it in parentheses, `names.Count == 2 (3 == 2)`,
each side evaluated once; a side of a type whose text no `print` shows, a `Text()` without `pub` (A9), a
raw pointer or a vector among them, leaves the condition its text alone (§11, the owner, 2026-11-20,
issue 586, comment 7051499099, Q1 of comment 6992687784 at its lean, and F10 of comment 6993110710 as
comment 6994632453 said it back, which stands) *(assumed: F1 of comment 6993110710, a reading of the build
said back there)*. At the top level, where the crash ends the process
(U19), its error line ends with a colon and the text starts on the line after it, before the trace, as a
thrown value's text does (O6). Under `kurz test` the words of such a crash that the test's own actor
takes, by a `check` of its own code or of a child that escalates into it (U25), stand under its test's
`FAILED` line (Y3), and a `check` that crashes another actor writes its log line (U24).

Case: [tests/check-not-bool.kz](../corpus/tests/check-not-bool.kz)
```kurz
check 1
```

Case: [tests/check-top-level.kz](../corpus/tests/check-top-level.kz)
```kurz
names = ["a", "b", "c"]
print("before")
check names.Count == 2
print("after")
```

Case: [tests/check-in-function.kz](../corpus/tests/check-in-function.kz)
```kurz
void Positive(int n) {
    check n > 0
}

Positive(1)
print("ok")
Positive(-1)
```

Case: [tests/check-fails-in-test.kz](../corpus/tests/check-fails-in-test.kz)

### Y7 (decided, §11) `build`, `run` and `exec` check every test

Every command checks every `test "name"` block (Y1), the cluster test of Y8 apart, so that an error in
one refuses `kurz build`, `kurz run` and `kurz exec` of its program as an error elsewhere does, and none
of them runs a test (§11, the owner, 2026-11-20, issue 586, comment 7051499099, Q5 of comment 6992687784
at its lean).

Case: [tests/test-error-refuses-build.kz](../corpus/tests/test-error-refuses-build.kz)
```kurz
// a test is checked by every command, so that its error refuses the build of the program, which runs no test
test "counts" {
    n = 1
    n = 2
    print(n)
}

print(1)
```

### Y8 (assumed, §11, §13) The cluster test

`test cluster N seed S { ... }`, the cluster test of §13, with its `split`, `heal` and `check`, is `not
supported` at the line of its `test`, its words and its block read to the block's end and not checked, as
`per cluster` is, since it needs the directory and the network of §7 *(assumed: D7 of issue 586, comment
6992687784, said back there)*.

No case: the build answers `not supported` to the form, and a case stands on what a build does.

### Y9 (decided, §11) `mock X with Y`

`mock X with Y` accepts a `Y` that has each member of `X` that the code the test reaches calls, with the
same types, checked at each such call in the build of that test's swaps: a member `Y` lacks or types
otherwise is the compile error `mock-mismatch` at the `mock` line, naming the call, and a `mock` whose `X`
nothing the test runs reaches is `mock-unreached` (§11, the owner, 2026-11-20, issue 586, comment
7051499099, Q7 of comment 6992687784 at its lean). `mock` takes a class or an actor type, a `per node` one
among them, as `X` and as `Y`, and a function takes no swap (§11, assumed there: D9 of comment 6992687784,
said back there). The build reads `mock X with Y`, two types after `mock` and `with`, and answers it `not
supported` at its line, its types not resolved (§11, assumed there: F20 of issue 586, comment 6993110710).

No case: the build answers `not supported` to the form, and a case stands on what a build does.

### Y10 (decided, §11) `advance d`

`advance d` stands among a `test` block's own statements alone, the compile error `advance-outside`
elsewhere, and a negative duration crashes the test at the statement with the run-time error
`advance-negative` (§11, the owner, 2026-11-20, issue 586, comment 7051499099, Q8 of comment 6992687784 at
its lean). A test's own statements are its body and the blocks nested in it, a block of `within` among
them, and not a lambda, a block of `on crash` or the block of an `attempt` inside it (§11, assumed there).
`advance` takes a duration, which the build checks as any expression is, and as the bound of `within` is
(U40), a value of another type than `duration` `type-mismatch` at its line (§11, the owner, 2026-11-20,
comment 7051499099: the readings of comment 6994632453 stand), wherever the statement stands, so that an
`advance` outside a test's own statements whose duration is no `duration` gets both errors at its line (E1;
§11, assumed there).

In every build `kurz test` makes, `--release` among them, `instant.Now`, `timestamp.Now`, the time of a
log line and every deadline the runtime owns, but for the wait of a child process, which waits in the
kernel, read the runtime's clock: the system's, which keeps running,
and an offset, which each `advance` adds to and which is 0 at each test's start; `build`, `run` and
`exec` read the system's clock directly (§11, assumed there). The timers and the pauses the moved clock
passed fire at the scheduler's turn after the `advance`, in the order of their deadlines, and the test's
actor waits at the statement for that turn, so that the actors they woke run before it goes on, each to
the wait that parks it again, and timers of one deadline fire in the order they were set; an `advance`
that would move the wall clock or the monotonic clock past the greatest `long` less a year of 365 days,
31536000000000000 nanoseconds, landing on that point allowed, is `overflow` at the statement, the offset
left as it was and nothing parked, while a negative `advance` is `advance-negative` on its own, so that
no lower end is kept (§11, assumed there). The build of `advance` is Linux's, `not supported` elsewhere,
as `instant.Now` is (§11, assumed there).

Case: [tests/advance-sleep.kz](../corpus/tests/advance-sleep.kz)
```kurz
// the reminder's sleep of sixty days ends at the `advance` that passes its deadline, and the reminder runs before the test goes on;
// the first call waits behind the reminder's start, since one thread runs the actors that are ready first in, first out (U35), so
// that its sleep stands among the timers before the first `advance`
actor Counter {
    mut int count = 0

    pub void Add() {
        count = count + 1
    }

    pub int Count() => count
}

actor Reminder(Counter Target) {
    pub void Wait(duration d) {
        Environment.Sleep(d)
        send Target.Add()
    }
}

test "sixty days pass at once" {
    c = spawn Counter()
    r = spawn Reminder(c)
    send r.Wait(60days)
    check c.Count() == 0
    advance 59days
    check c.Count() == 0
    advance 24h
    check c.Count() == 1
}
```

Case: [tests/advance-call-timeout.kz](../corpus/tests/advance-call-timeout.kz)
```kurz
// the bound of the watcher's call passes at the `advance`, while its callee still sleeps, and the watcher's `call-timeout`
// escalates into the test; the call of `Nap(0s)` waits behind the watcher's start, since one thread runs the actors that are ready
// first in, first out (U35), so that the watcher's call stands among the timers before the `advance`
actor Napper {
    pub int Nap(duration d) {
        Environment.Sleep(d)
        return 1
    }
}

actor Watcher(Napper Target) {
    pub void Watch() {
        print(Target.Nap(2h) within 1h)
    }
}

test "a bound of an hour passes at once" {
    n = spawn Napper()
    w = spawn Watcher(n) on crash escalate
    send w.Watch()
    check n.Nap(0s) == 1
    advance 1h
    print("after")
}
```

Case: [tests/advance-clock.kz](../corpus/tests/advance-clock.kz)
```kurz
// `instant.Now` and `timestamp.Now` read the clock that `advance` moved, in a block nested in the test too, and the real clock keeps
// running beneath it
test "an hour passes on the clock" {
    instant start = instant.Now
    timestamp wall = timestamp.Now
    mut int i = 0
    while i < 3 {
        advance 20min
        i = i + 1
    }

    check start.Elapsed >= 1h
    check start.Elapsed < 2h
    check timestamp.Now - wall >= 1h
}
```

Case: [tests/advance-negative.kz](../corpus/tests/advance-negative.kz)
```kurz
// a negative duration crashes the test at the statement, and the clock does not move back
test "the clock goes back" {
    advance -1s
}
```

Case: [tests/advance-overflow.kz](../corpus/tests/advance-overflow.kz)
```kurz
// the clock's offset goes back to 0 for each test, so that the second test's `advance` fits as the first's did, while two in one
// test move the wall clock past the range of a `timestamp`, at the second
test "a long wait" {
    advance 50000days
}

test "a long wait again" {
    advance 50000days
}

test "two long waits" {
    advance 50000days
    advance 50000days
}
```

Case: [tests/advance-outside.kz](../corpus/tests/advance-outside.kz)
```kurz
// a helper moves no clock, since a program's own run never moves its clock: it returns, and its test advances
void Later() {
    advance 1h
}

test "waits an hour" {
    Later()
}
```

Case: [tests/advance-in-lambda.kz](../corpus/tests/advance-in-lambda.kz)
```kurz
// a lambda's body is no statement of its test's own, though the lambda stands inside the test
test "a lambda waits an hour" {
    later = () => {
        advance 1h
    }

    later()
}
```

Case: [tests/advance-within.kz](../corpus/tests/advance-within.kz)
```kurz
// a block of `within` is a block nested in the test, so that `advance` stands among the test's own statements there, and the clock
// an actor reads after it is an hour on; the block's bound of two hours is longer than the `advance`, which passes no deadline of it
actor Clock {
    pub instant Now() => instant.Now
}

test "an advance inside within" {
    c = spawn Clock()
    before = c.Now()
    within 2h {
        advance 1h
    }

    check c.Now() - before >= 1h
}
```

Case: [tests/advance-read-bound.kz](../corpus/tests/advance-read-bound.kz)
```kurz
// the client's read of a connection that sends nothing waits in the poller with its bound of an hour among the runtime's timers,
// so that the `advance` of an hour passes it and the read ends with `TimedOut` and the number 11 at the scheduler's turn after it,
// in milliseconds (Y10, S10, S9); a read that blocked in the kernel would wait the hour out. The test's calls are of its chain,
// which the client's waiting activation lets in (U17), so that the loop reads "reading" once the read waits, and the `advance`
// comes after the read's bound stands among the timers
actor Client {
    mut string got = "none"

    pub void Run(int port) {
        stream = TcpStream.Connect("127.0.0.1:{port}") else {
            IoError e => {
                got = "connect {e.Kind}"
                return
            }
        }

        stream.SetReadTimeout(1h) else {
            IoError e => {
                got = "bound {e.Kind}"
                return
            }
        }

        got = "reading"
        mut buffer = Bytes()
        match stream.Read(mut buffer, 1) {
            int n => got = "read {n}"
            IoError e => got = "{e.Kind} {e.Code}"
        }
    }

    pub string Got() => got
}

test "a read's bound of an hour passes at the advance" {
    match TcpListener.Bind("127.0.0.1:0") {
        TcpListener listener => {
            c = spawn Client()
            send c.Run(listener.Port)
            match listener.Accept() {
                TcpStream server => {
                    while c.Got() != "reading" {
                        Environment.Sleep(1ms)
                    }

                    advance 1h
                    check c.Got() == "TimedOut 11"
                    server.Close() else {
                        IoError e => print(e)
                    }
                }
                IoError e => print(e)
            }
        }
        IoError e => print(e)
    }
}
```

### Y11 (decided, §2) The seed of a run

A run of `kurz test` takes a seed, a `ulong`: the keys a map's hash switches to (M9) and the chain IDs
(U17) are drawn from a generator the seed starts, and the actors ready at a turn of the one thread that
runs a test's actors (U35) run in an order that a second generator the seed starts draws, so that a run
that failed replays its draws and its order from its seed (§2, the owner, 2026-11-26, issue 657, comment
7189038775, Q1 b); the variation of a restart's pause (U23) is drawn from the first generator too (§2,
assumed there). A run where no seed is given takes a new one, 8 bytes of the system's randomness, which
the command draws before the build with .NET's generator of random bytes, so that a refusal of that
generator ends the command as .NET ends a program on an exception no code catches, with its text on
standard error and an exit code that is not 0, and never ends the built program with `random-failed`
(§2, assumed there), and the line of each failed test names it after `FAILED`, in parentheses, so that
`kurz test --seed 1234 users.kz` runs with it (§2, the owner, 2026-11-26, comment 7189038775, Q2 a and
Q3 a; the parentheses assumed there):

```text
test "adds" ... ok
test "counts" ... FAILED (seed 1234)
    check-failed "names.Count == 2 (3 == 2)" at users.kz:7
2 tests: 1 passed, 1 failed
```

The seed is written in decimal digits alone, from 0 to 18446744073709551615, the greatest `ulong`; any
other value, an empty one or a greater one among them, ends the command with `error: seed (<the
value>)` on standard error and the exit code 1, and a run whose tests all passed names no seed (§2, the
owner, 2026-11-27, issue 657, comment 7215317771, Q1, Q2 and Q3 of comment 7205593463), a value with
leading zeros naming its number, `007` the seed 7 (§2, assumed there). `--seed` stands before the file,
once, as the other options do (Y2), and the word after it is its value, whatever it is, so that `kurz
test --seed --release a.kz` ends with `error: seed (--release)` and `kurz test --seed a.kz` with `error:
seed (a.kz)`; a value it refuses ends the command before its build, and so before the start of the built
program that reads `KURZ_THREADS` (U35); and a second `--seed`, a `--seed` that is the last word of the
command line, and an option after the file are errors of its usage, as a `--seed` given to `kurz run`,
`kurz build` or `kurz exec`, which take none, is an error of theirs (§2, assumed there). The draws and the order come from two generators apart, so that a draw a test
adds moves no order (§2, the owner, 2026-11-27, comment 7215317771, Q8). Both are xoshiro256++, started
by SplitMix64 from the seed, the draws by its first four words and the order by the four after them;
the actors ready at a turn are those queued when the scheduler takes
the first of them, the actors they wake queued behind them, and each take is one of those left, at the
index the next word gives modulo their count, those left keeping their order in the queue, a word drawn
at every take, the last one of a turn among them, and a turn ending before the test's actor that an `advance`
parked where an actor stands before it, so that the actors the moved clock woke run before the test goes
on (Y10) (§2, assumed there). A build of `kurz test` asks the system for no
randomness, so that it never ends with `random-failed` (M9), and its keys and chain IDs follow from the
seed its failed lines show (§2, assumed there). A seed replays the draws and the order alone, on the
build that printed it, with no promise across versions of the compiler (§2, the owner, 2026-11-27,
comment 7215317771, Q8): a test that reads the system's clock without `advance`, or waits on a file, a
socket or a child process, can run otherwise under the same seed (§2, assumed there). A run names its
seed on the line of a failed test alone, so that a run that an error ends before such a line, the
checker's refusal or an error outside every test among them, names none (§2, the cost the same answer
names for Q1 to Q3). A case marked
`// threads: 1` runs its ready actors first in, first out under any seed (U35; §2, the owner,
2026-11-27, comment 7215317771, Q4): the compiler reads no `// threads: 1`, so the corpus runner builds
such a case with that order, which a build for Windows, reading no `KURZ_THREADS`, gets from the build
alone, and the same file run with `kurz test` by hand takes the order its seed draws (chapter 0; §2,
assumed there). A build for Linux run with `KURZ_THREADS` set to 1 takes its ready actors first in,
first out under any seed too, and a build for Windows reads no `KURZ_THREADS`, so that a seed replays
the order of a run on Linux only with `KURZ_THREADS` set to 1, or not, as it was in the run that printed
the seed (U35; §2, assumed there). A case of the corpus that runs its tests runs with the seed 0
(chapter 0; §2, assumed there).

Case: [tests/seed-orders-ready-actors.kz](../corpus/tests/seed-orders-ready-actors.kz)
```kurz
// the four speakers are ready at one turn, and run in the order that the seed 0, which the runner gives, draws for them by Y11's take,
// each take a word of the order modulo the count of those left, which keep their order in the queue, and not in the order of their
// sends, first in, first out
actor Speaker(string Name) {
    pub void Say() {
        print(Name)
    }

    pub int Said() => 1
}

test "speaks" {
    a = spawn Speaker("a")
    b = spawn Speaker("b")
    c = spawn Speaker("c")
    d = spawn Speaker("d")
    send a.Say()
    send b.Say()
    send c.Say()
    send d.Say()
    check a.Said() + b.Said() + c.Said() + d.Said() == 4
}
```
