# 20. Tests

A program's tests are `test` blocks at the top level of its files, which `kurz test file.kz` runs one at a
time, each in an actor of its own, and `check`, a statement that stands in any code (§2, §11; issue 586,
comments 6992687784 and 6993110710, and the owner's answers of 2026-11-20, comment 7051499099, each rule
marked as it stands). The cluster test of §13, `mock` and `advance` are read and answered `not supported`
(Y8 to Y10).

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
back there, stands), and no argument after the file: `--` or another argument there is an error of its
usage, since a test reads no `args` (Y5) *(assumed: F17 of comment 6993110710, a reading of the build said
back there)*. It has no filter by name, no seed, no parallel tests and no format of its output but text
*(assumed: D4 of comment 6992687784, said back there)*. A case of the corpus that runs its program's tests
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
ends the process (Y5): `test "name" ... ok`, or `test "name" ... FAILED` with the words that failed it
indented under it, the words of the log line of the crash that failed it (U24), after its `crashed: `, or
those of Y5's `deadlock`, which is no crash; then a summary, `<n> tests: <p> passed, <f> failed`, each
count written, a zero among them, and `test` in place of `tests` where the program has one,
`1 test: 1 passed, 0 failed` (§2, the owner, 2026-11-20, issue 586, comment 7051499099, Q3 of comment
6992687784 at its lean, and reading n of comment 6998786049, a reading of the build said back there,
which stands) *(assumed: F1 and F21 of comment 6993110710, readings of the build said back there)*:

```text
test "rename fails for unknown user" ... ok
test "survives 60 days" ... FAILED
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
2026-11-21, #599, comment 7067639209, the order 1, the same place 1, the cap 1, the indent 1). An error that
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
*(assumed: F3, F6 and F22 of comment 6993110710, readings of the build said back there)*. No line names a
test's own actor, since its crash writes none: the name `test` that F2 of comment 6993110710 gives it in
the log line of a crash is a reading with no case. A test has no bound of time: a test that waits on a
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
// the block of the child's policy runs in the test's actor once its message ends, and that message waits on the child
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
its lean). The build reads `advance d`, a duration after `advance`, which it checks as any expression is,
and as the bound of `within` is (U40), a value of another type than `duration` `type-mismatch` at its
line, and answers the statement `not supported` at its line (§11, the owner, 2026-11-20, comment
7051499099: the readings of comment 6994632453 stand).

No case: the build answers `not supported` to the form, and a case stands on what a build does.
