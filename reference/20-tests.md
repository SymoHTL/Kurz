# 20. Tests

A program's tests are `test` blocks at the top level of its files, which `kurz test file.kz` runs one at a
time, each in an actor of its own, and `check`, a statement that stands in any code (§2, §11; issue 586,
comments 6992687784 and 6993110710, each rule marked as it stands while the questions of the first comment
wait for the owner). The cluster test of §13, `mock` and `advance` are read and answered `not supported`
(Y8 to Y10).

### Y1 (assumed, §11) `test "name" { ... }`, and its words

A test is a block `test "name" { ... }` at the top level of a file: the word `test`, a plain string
literal, its name, and a block that opens on the line of the name, as an `if`'s does (C1). The name is
printed as written, and a hole in it is `syntax` at its line (E4) *(assumed: F19 of issue 586, comment
6993110710, a reading of the build said back there)*. A `test` block inside a declaration or a block, a
function's, a lambda's, an `if`'s, a class's or another test's, is `syntax` at the line of its `test`
(E4), and the parser reads the block and goes on with what follows it; a second test of one name in a
program is `syntax` at the line of the second, its text naming the line of the first *(assumed: D3 of
issue 586, comment 6992687784, said back there)*. The names are compared by their value, so that `"A"`
and `"\u0041"` are one name *(assumed: Q10 of issue 586, comment 6994632453, built at the lean while it
waits for the owner)*.

`test`, `check`, `mock` and `advance` are words only where their form stands (L18): `test` at the start
of a statement before a string literal, or before `cluster` and a token that can start an expression;
`check` and `advance` at the start of a statement before a token that can start an expression and is no
`[`, so that `check(x)` and `advance(d)` are those statements, as `const assert(` is the statement of T37;
and `mock` at the start of a statement where a type, `with` and a type follow it. Before `=`, `.` and
`[`, and everywhere else, each is a name, and a variable named so keeps its meaning *(assumed: D2 of
comment 6992687784, said back there, and F9 of comment 6993110710, a reading of the build said back
there)*.

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

Case: [source/test-words-as-names.kz](../corpus/source/test-words-as-names.kz)
```kurz
test = 1
check = 2
mock = 3
advance = 4
print(test + check + mock + advance)
```

### Y2 (assumed, §2, §11) `kurz test`, the tests it runs and their order

`kurz test file.kz` runs every `test` block of the program that file starts, of its files and never of
`std/`'s, one at a time, in the order of the files and of the blocks in each, and runs no top-level code
of the program *(assumed: D1 of issue 586, comment 6992687784, said back there)*. It takes no argument
after the file: `--` or another argument there is an error of its usage, since a test reads no `args`
(Y5) *(assumed: F17 of comment 6993110710, a reading of the build said back there)*. It has no filter by
name, no seed, no parallel tests and no format of its output but text *(assumed: D4 of comment
6992687784, said back there)*. A case of the corpus that runs its program's tests carries the header line
`// command: test` (chapter 0) *(assumed: the corpus line of comment 6993110710, a reading of the build
said back there)*.

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

### Y3 (assumed, §2) What `kurz test` writes, and its exit code

`kurz test` writes a line for each test on standard output, in the order the tests run: `test "name" ...
ok`, or `test "name" ... FAILED` with the words of the log line of the crash that failed it (U24), after
its `crashed: `, indented under it; then a summary of the counts, `1 test:` where the program has one
*(assumed: Q3 of issue 586, comment 6992687784, built at the default while it waits for the owner, and F1
and F21 of comment 6993110710, readings of the build said back there)*:

```text
test "rename fails for unknown user" ... ok
test "survives 60 days" ... FAILED
    check-failed "names.Count == 2 (3 == 2)" at users.kz:12
2 tests: 1 passed, 1 failed
```

A test's own `print` goes to standard output as it runs, so that its lines stand before the line of its
result *(assumed: D5 of comment 6992687784, said back there, and F4 of comment 6993110710, a reading of
the build said back there)*. The command exits with 0 where every test passed and with 1 where one failed,
the code of a refused program (§2) *(assumed: Q4 of comment 6992687784, built at the default while it
waits for the owner)*.

Case: [tests/check-fails-in-test.kz](../corpus/tests/check-fails-in-test.kz)
```kurz
test "adds" {
    check 1 + 1 == 2
}

test "counts" {
    names = ["a", "b", "c"]
    check names.Count == 2
}

test "throws" {
    throw "down"
}
```

Case: [tests/one-test.kz](../corpus/tests/one-test.kz)
```kurz
test "adds" {
    check 1 + 1 == 2
}
```

### Y4 (assumed, §2) A program with no test

`kurz test` on a program with no `test` block writes `error: no test in users.kz`, with a `help:` line that
names the form, and exits with 1, so that a run that checked nothing does not look like a pass
*(assumed: Q6 of issue 586, comment 6992687784, built at the default while it waits for the owner)*.

No case: the refusal of a program with no test is the command's own, with no id (E2), which a case cannot
expect; `CommandTests` holds its line, its `help:` line and its exit code.

### Y5 (assumed, §11) A test runs in an actor of its own

Each test runs in an actor of its own, a child of the runner. The test fails where that actor crashes, by
a `throw`, a failed `check` (Y6), an overflow or a deadline, or where the runner's wait for it meets
`deadlock` (U18), which then names the test's line, `deadlock at users.kz:4`, since a parked wait keeps no
place of its own; the run goes on with the test after it *(assumed: Q2 of issue 586, comment 6992687784,
built at the default while it waits for the owner, and F5 of comment 6993110710, a reading of the build
said back there)*. A `deadlock` that a call an actor's start keeps out closes (U22, U52) names that
call's line in place of the test's, the line the top-level code's error names for that loop (U18) (§11,
the owner, 2026-11-21, #599, comment 7063693195, Y5's line 1), in any file, the test's or a file the
test's file `use`s or the standard library (§11, the owner, 2026-11-21, #599, comment 7067131310, the
file 1); where more than one call is kept out, the words name every one, the first place in source
order as `deadlock at <file>:<n>` and each further place on a line `kept out at <file>:<n>` under it,
as the top-level code's error does (U22) (§11, the owner, 2026-11-21, #599, comment 7067131310,
several 2; comment 7067139953, the form 1), in the order, with the counts and with the cap of U22,
each line indented by four spaces as every word under a `FAILED` line (Y3) (§11, the owner,
2026-11-21, #599, comment 7067639209, the order 1, the same place 1, the cap 1, the indent 1). An error that ends the process wherever
it happens (U19), `allocation-failed` among
them, ends the run inside a test too: its error line stands on standard error, and neither that test's
line, nor the tests after it, nor the counts are written, and the exit code is 1 *(assumed: a reading of
the build, proposed with this record and not put to the owner)*. The crash of a test's own actor writes no
line on standard error, its words standing under the test's `FAILED` line (Y3), and the crash of an actor
the test spawned keeps its line (U24). At a test's end every actor it made is dropped at once, parked ones
too, as U13 drops what is queued at a program's end; the numbers of actors (U2) go on counting, so that
the actors of a test are numbered after those of the tests before it, and a socket or a file its actors
hold stays open *(assumed: F3, F6 and F22 of comment 6993110710, readings of the build said back there)*.
No line names a test's own actor, since its crash writes none: the name `test` that F2 of comment
6993110710 gives it in the log line of a crash is a reading with no case.

A test is checked as a function of its file with no parameter and a `void` result is: its body reads no
variable of the top-level code, `unknown-name` at the read and `redeclared` at a declaration of its name
(F16), and no `args` (S13), and a failure case that can leave it is `unlisted-case` (O3) *(assumed: D1 of
comment 6992687784, said back there, and F17 and F18 of comment 6993110710, readings of the build said
back there)*.

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
            return x.Peek()
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
            return x.Peek()
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
            return x.Peek()
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
            0 => x.Peek()
            1 => x.Peek()
            2 => x.Peek()
            3 => x.Peek()
            4 => x.Peek()
            5 => x.Peek()
            6 => x.Peek()
            7 => x.Peek()
            8 => x.Peek()
            9 => x.Peek()
            10 => x.Peek()
            11 => x.Peek()
            else => x.Peek()
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

### Y6 (assumed, §11) `check`

`check condition` is a statement that stands in any code, the top-level code, a function, a method, a
lambda and a test, in every build *(assumed: F8 and F9 of issue 586, comment 6993110710, readings of the
build said back there)*. Its condition is a `bool`, read as a condition is (C3): a value of another type
is `type-mismatch` at its line, a nullable one `nullable-unchecked` (N2), and a call with a `void` success
`syntax` (O10). A `check` narrows nothing: after `check x != null` the variable `x` is still nullable in
the statements after it, `nullable-unchecked` at a use that needs its value (N2), as Rust's `assert!`
leaves an `Option` an `Option` *(assumed: Q9 of issue 586, comment 6994632453, built at the lean while it
waits for the owner)*.

A false condition crashes the actor that runs it with the run-time error `check-failed`, at the file and
the line of the `check`. Its text is the condition as the source writes it, each run of spaces, line
breaks and comments between two of its tokens one space, and for one of the six comparisons, `==`, `!=`,
`<`, `<=`, `>` and `>=`, alone or in parentheses, whose two sides show their values as a hole of an
interpolation shows them (L6), each side's value after it in parentheses, `names.Count == 2 (3 == 2)`,
each side evaluated once; a side of a type whose text no `print` shows, a `Text()` without `pub` (A9),
a raw pointer or a vector among them, leaves the condition its text alone *(assumed: Q1 of issue 586,
comment 6992687784, built at the default while it waits for the owner, and F1 and F10 of comment
6993110710, readings of the build said back there)*. Under `kurz test` the words of that crash stand
under its test's `FAILED` line (Y3).

Case: [tests/check-not-bool.kz](../corpus/tests/check-not-bool.kz)
```kurz
// a test is checked by every command, so its error refuses the build of the program
test "counts" {
    check 1
}

print(1)
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

### Y7 (assumed, §11) `build`, `run` and `exec` check every test

Every command checks every `test` block, so that an error in one refuses `kurz build`, `kurz run` and
`kurz exec` of its program as an error elsewhere does, and none of them runs a test *(assumed: Q5 of
issue 586, comment 6992687784, built at the default while it waits for the owner)*. A test has no bound of
time: a test that waits on a wait the runtime does not own holds the run *(assumed: D6 of comment
6992687784, said back there)*.

Case: [tests/check-not-bool.kz](../corpus/tests/check-not-bool.kz)

### Y8 (assumed, §11, §13) The cluster test

`test cluster N seed S { ... }`, the cluster test of §13, with its `split`, `heal` and `check`, is `not
supported` at the line of its `test`, its words and its block read to the block's end and not checked, as
`per cluster` is, since it needs the directory and the network of §7 *(assumed: D7 of issue 586, comment
6992687784, said back there)*.

No case: the build answers `not supported` to the form, and a case stands on what a build does.

### Y9 (open) `mock X with Y`

`mock X with Y` is read, two types after `mock` and `with`, and answered `not supported` at its line, its
types not resolved (§11, assumed there: F20 of issue 586, comment 6993110710).
`mock` takes a class or an actor type, a `per node` one among them, as `X` and as `Y`, and a function
takes no swap (§11, D9 of comment 6992687784). What `Y` has to offer nobody chose (§14; Q7 of comment
6992687784): (a) each member of `X` that the code the test reaches calls, with the same types, checked at
each such call, `mock-mismatch` at the `mock` line for a member `Y` lacks or types otherwise and
`mock-unreached` for a `mock` whose `X` nothing the test runs reaches; (b) every `pub` member of `X`, with
the same types, checked at the `mock` line; (c) an interface that `X` implements.

No case: the rule is open, and the build answers `not supported` to the form.

### Y10 (open) `advance d`

`advance d` is read, a duration after `advance`, which is checked as any expression is, and as the bound of
`within` is (U40), a value of another type than `duration` `type-mismatch` at its line, and the statement
is answered `not supported` at its line (§11, assumed there). Where
it stands nobody chose (§14; Q8 of issue 586, comment 6992687784): (a) among a `test` block's own
statements alone, the compile error `advance-outside` elsewhere and the run-time error `advance-negative`
for a negative duration; (b) in any function the build of `kurz test` reaches, refused where a program's
own run can reach it; (c) a function of the standard library, `Clock.Advance(d)`, that moves nothing
outside the build of `kurz test`.

No case: the rule is open, and the build answers `not supported` to the form.
