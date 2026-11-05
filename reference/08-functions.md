# 8. Functions and program structure

### F1 (decided, §8) Signatures

A function is declared with its return type first, then its name and its parameters, each with
its type first: `int Add(int a, int b)`.

Case: [functions/expression-body.kz](../corpus/functions/expression-body.kz)
```kurz
int Add(int a, int b) => a + b

print(Add(2, 3))
```

### F2 (decided, §8) Expression body

`=> expression` after the signature makes the expression the function's result.

Case: [functions/expression-body.kz](../corpus/functions/expression-body.kz)

### F3 (assumed, §8) Block body and `return`

A body between braces holds statements. `return expression` ends the function with that result,
as in C#. As there, a function with a result whose end can be reached without a `return`, and a
bare `return` in such a function, are the compile error `missing-return`; `return` with a value in
a `void` function (F4), and a value of another type than the result, are `type-mismatch`.
*(proposed: C#'s rules with Kurz's ids)*

Case: [functions/block-body.kz](../corpus/functions/block-body.kz)
```kurz
int Max(int a, int b) {
    if a > b {
        return a
    }
    return b
}

print(Max(2, 3))
```

Case: [functions/missing-return.kz](../corpus/functions/missing-return.kz)
```kurz
int Sign(int n) {
    if n < 0 {
        return -1
    }
}

print(Sign(3))
```

### F4 (assumed, §8) `void`

A function that has no result is declared with `void`, as in C# and in the record's samples.

Case: [functions/void.kz](../corpus/functions/void.kz)
```kurz
void Greet(string name) {
    print("hello {name}")
}

Greet("Ann")
```

### F5 (decided, §8) Top-level statements

There is no `Main`. Statements at the top level of a file run in order when the program starts.
One file of a program has them; a second file with top-level statements is a compile error, as in
C#, because nothing says in which order two files would run. *(proposed)* Its id is not in the
error table: a case of the corpus is a single file, so no case can expect it, and the table lists
only ids the corpus pins (E2).

Case: [functions/top-level.kz](../corpus/functions/top-level.kz)
```kurz
print("first")
print("second")
```

### F6 (assumed, §8) Use before declaration

A function or a type can be used above the line that declares it, as in C#.

Case: [functions/use-before-declaration.kz](../corpus/functions/use-before-declaration.kz)
```kurz
print(Double(4))

int Double(int n) => n * 2
```

### F7 (decided, §4, §8) Lambdas

A lambda is written as in C#: `x => x * 2`. A lambda has a type of its own, written as F11
writes function types, when its parameters are typed and its body has a type: `() => n` with an
`int` `n` is a `() => int`, so `first = () => n` declares a variable of that type (V1); where a
type is written or expected (a parameter of F11's type, a typed variable), the lambda takes it and
its parameters need no types. A lambda whose parameters have no type and no type to take, as `f =
x => x * 2`, is the compile error `cannot-infer` (T27). This is what C# 10 does with a lambda's
natural type, written down because C# before 10 had none. *(proposed)*

Case: [functions/lambda-cannot-infer.kz](../corpus/functions/lambda-cannot-infer.kz)
```kurz
f = x => x * 2
print(f(2))
```

Case: [functions/lambda.kz](../corpus/functions/lambda.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
print(xs.Where(x => x > 1).Count)
```

### F8 (assumed, §4) Parameters are immutable

Inside a function a parameter is an immutable variable, unless it is marked `mut` (M7).
Assigning to it is the compile error `assign-immutable`.

Case: [functions/parameter-immutable.kz](../corpus/functions/parameter-immutable.kz)
```kurz
int Next(int n) {
    n = n + 1
    return n
}

print(Next(1))
```

### F9 (decided, §8) Private by default

A member is private to its type. A top-level type or function is private to its folder. `pub`
exposes it; `prot` exposes a member to inheriting types. The fields of a primary constructor
are the exception (D14). Using a member that is not visible from where it is used is the compile
error `not-visible` *(proposed id)*.

A member marked `internal` is visible to the files of its folder and to nothing outside it, as
C#'s `internal` is visible in its assembly and Rust's `pub(crate)` in its crate (§8, the owner,
2026-11-02 and 2026-11-03). *(assumed, §8: `internal` is a core word (L18); `pub` and `internal`
before one declaration are `syntax`, as C# refuses two access modifiers; before a top-level type or
function `internal` says what no modifier says.)*

Case: [classes/private-member.kz](../corpus/classes/private-member.kz)
```kurz
class Counter {
    mut int count = 0

    void Add() {
        count = count + 1
    }
}

c = Counter()
c.Add()
```

Case: [classes/internal-member.kz](../corpus/classes/internal-member.kz)
```kurz
class Counter {
    internal mut int count = 0

    internal void Add() {
        count = count + 1
    }

    internal static int Twice(int n) => n * 2
}

c = Counter()
c.Add()
c.Add()
print(c.count)
print(Counter.Twice(5))
```

No case shows the folder part, a member without `pub` or one marked `internal` used from another
folder: it takes several folders, and a case of the corpus is a single file.

### F10 (decided, §8, §13) Folders and `use`

A folder is a namespace; there is no `namespace` line. Files in one folder see each other
without imports. `use` brings in another folder or a package: a folder of the project by its path
from the folder of `project.kz`, `use a/util`, and a package by the name of its line in
`project.kz`, `use json`. A type it holds is visible unqualified, `Json.Parse<T>(text)`, and is named
through the folder too, `json.Json`; a function at the top of the folder is reached through the
folder's last name, `util.Trim(s)`, `sys.Write(...)`; `use fastjson as fj` gives the folder another
name in the file, `fj.Json`, `fj.Parse(text)`. Two folders a file uses that both hold a type of one
name are the compile error `ambiguous-name` at the bare use of that name, and two whose last names
agree are `redeclared` at the second `use` line, which `as` fixes, `use b/util as butil` (§8, §13,
the owner, 2026-11-01, "By kind of name", "Both forms", "Root path, last name"). A `use` line whose name is both a folder at the top of the
project and a line of `project.kz`, `use json` beside a folder `json`, is `ambiguous-name` at that
line, in every file of the project, those of the folder `json` included, and with `as` too,
`use json as j` (§8, the owner, 2026-11-02). The reading reads a project's files: the runtime's `use sys` and `use std`
stand as §2 makes them, and in a program's file they are paths like any other, which a folder at
the top or a line of `project.kz` of that name answers, `unknown-name` where the project has neither
(§8, the owner, 2026-11-03, "the runtime's names only"). *(assumed, §8, §13: the two ids; a type of the file's own
folder is the one its bare name means; every name at the top of a folder that is no type is reached
through the folder's name; a package's qualifier is the name of its line; `as` takes the place of
the folder's name, or of a package's line name, as its qualifier in the file, the old name reaching
nothing through that line and being `unknown-name` where no other `use` line of the file gives it;
the names `redeclared` compares are the qualifiers the `use` lines give, and a qualifier that a name at the
top of the file's own folder has is `redeclared` at the `use` line; a path that names no folder of
the project and no line of `project.kz`, a path to a folder that holds a `project.kz` of its own,
and a path that holds `.` or `..` are `unknown-name` at the `use` line; a longer
path whose first name is both a folder at the top and a line of `project.kz`, `use json/schema`, is
`ambiguous-name` at its `use` line too; a folder at the top that holds a `project.kz` of its own is no
folder of the project, so that `use json` beside it names the line)*

Case: [functions/use-names-nothing.kz](../corpus/functions/use-names-nothing.kz)
```kurz
use nothing_here

print(1)
```

Case: [functions/use-sys-in-a-program.kz](../corpus/functions/use-sys-in-a-program.kz)
```kurz
use sys

print(1)
```

No case: the forms that take several files or a `project.kz`, since a case of the corpus is a single
file with none, and a path that holds `.` or `..`, which the compiler's front end refuses as `syntax`
before the checker reads it.

### F11 (decided, §4) Function types and what a lambda captures

The type of a function value is written with an arrow: the parameter types between brackets, `=>`,
and the result type, as in `(int) => bool`. A lambda reads the variables around it and cannot assign
them: assigning one is the compile error `capture-assign`. The lambda holds a copy of each (F15),
and a class instance is no part of the copy: the lambda holds a reference to the same instance. So
every change of the copy is `capture-assign` too, since it would be lost: an assignment into a path
from a captured variable that reaches no instance (M4), a `mut` method called on the variable or on
such a path (M5), and passing the variable or such a path as a `mut` argument (M7). *(proposed: the
record speaks of assigning)* A path reaches an instance only where an instance holds the field it
assigns or the item it writes: `() => { d.Item = Counter(2) }`, with a `data` value `d` whose field
`Item` holds an instance, assigns a field of the copy and is `capture-assign`. An instance changes through every reference (K2), so the lambda may
change what an instance it captured holds, a `mut` field by an assignment, `() => { c.Count = 5 }`,
or through a method, `() => c.Add(1)`, while `() => { c = Counter(1) }` and `() => { d.X = 1 }` with a `data`
value `d` stay `capture-assign` (§4, the owner, 2026-11-01, #110, comment 6562487754, "Both allowed", against a method call
alone and against neither). It may also write into the list or the map that a `mut` field of the
instance holds, `() => { b.Xs[0] = 2 }` and `() => b.Xs.Add(3)`, as M5's reading of K2 takes the same
write outside a lambda (§4, the owner, 2026-11-02, #110, comment 6573113786), and pass a `mut` field of the
instance as a `mut` argument, `() => Fill(mut Items)` inside a method (M7; §4, the owner, 2026-11-02, #110, comment
6579495475). A field without `mut` changes in a lambda no more than outside it: assigning it is
`assign-immutable` (K1), and writing into what it holds, a `mut` method on it and passing it as `mut`
by its name inside a method are `mut-required` (M5, M7, K1). An instance that a captured `data` value holds is one the lambda may
change too, `() => { d.Item.Count = 1 }` *(assumed, as §4 marks it)*. The result type of a function type may be `void`,
`(int) => void` (C#'s `Action<int>`); `(int) => int?` is a function that returns an `int?`, and a
nullable function type is written `((int) => int)?`. *(assumed: proposed on 2026-10-09 and not
objected to)* A lambda whose body is a call with a `void` success, `x => print(x)`, is a lambda with
a `void` result and no use of the call as a value (O10) (the owner, 2026-10-10). The `=>` of a function
type reaches to the end of the type, so `(int) => int | NotFound` returns the union, and a union of a
function type and another type is written `((int) => int) | NotFound`, as the nullable one is; such a
lambda under a function type whose result is not `void`, `(int) => int`, is `type-mismatch` (T1) at
the lambda, since its result is `void` where an `int` is required (the owner, 2026-10-11, confirming the reading proposed after the review of round 14).

Case: [functions/function-type.kz](../corpus/functions/function-type.kz)
```kurz
int Apply((int) => int f, int n) => f(n)

print(Apply(x => x + 1, 4))
print(Apply(x => x * x, 4))
```

Case: [functions/capture-read.kz](../corpus/functions/capture-read.kz)
```kurz
limit = 1
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
print(xs.Where(x => x > limit).Count)
```

Case: [functions/capture-assign.kz](../corpus/functions/capture-assign.kz)
```kurz
mut total = 0
mut xs = List<int>()
xs.Add(1)
big = xs.Where(x => {
    total = total + x
    return x > 0
})
print(big.Count)
print(total)
```

Case: [functions/capture-changes-instance.kz](../corpus/functions/capture-changes-instance.kz)
```kurz
class Counter(mut int Count) {
    pub void Add(int n) {
        Count = Count + n
    }
}

c = Counter(0)
bump = () => c.Add(1)
bump()
bump()
print(c.Count)
reset = () => { c.Count = 5 }
reset()
print(c.Count)
```

Case: [functions/capture-calls-field-method.kz](../corpus/functions/capture-calls-field-method.kz)
```kurz
class Box(mut List<int> Items)

b = Box([1, 0])
more = () => b.Items.Add(3)
more()
print(b.Items)
```

Case: [functions/capture-passes-field-mut.kz](../corpus/functions/capture-passes-field-mut.kz)
```kurz
void Fill(mut List<int> xs) {
    xs.Add(xs.Count)
}

class Box(mut List<int> Items) {
    pub () => void Filler() => () => Fill(mut Items)
}

b = Box([1, 0])
fill = b.Filler()
fill()
print(b.Items.Count)
```

Case: [functions/capture-writes-field.kz](../corpus/functions/capture-writes-field.kz)
```kurz
class Box(mut List<int> Items)

b = Box([1, 0])
first = () => { b.Items[0] = 2 }
first()
print(b.Items)
```

Case: [functions/function-type-void.kz](../corpus/functions/function-type-void.kz)
```kurz
void Run((int) => void f) {
    f(1)
}

Run(x => print(x))
// the lambda's body is a call with a void success: a void lambda, and no use of the call as a value (F11)
```

### F12 (decided, §8) Overloads, default values, named arguments

Functions may share a name when their parameters differ: in their number, or in the type of a
parameter at the same place. Names, default values and the `mut` marker (M7) make no difference, so
`Fill(List<int>)` next to `Fill(mut List<int>)` is the compile error `duplicate-function`, and a
call of one of them without `mut` is M7's `mut-at-call`, never a quiet pick of the other. A
parameter can have a default value, and an argument can be passed by name (D11). Which function a
call picks when more than one fits is F13. *(proposed: what "differ" is, and the id)* Arguments are
evaluated from left to right, as in C# *(assumed: proposed on 2026-10-09 and not objected to)*, in the
order written at the call, named arguments included (the owner, 2026-10-10). A parameter's default value is a constant expression, as C# requires of it: a literal, `null`, a
value of an `enum` or `flags`, a `data` value without fields, or an expression of literals that T6
folds (the owner, 2026-10-11, confirming the reading proposed after the review of round 14), and a
`data` value made by its name whose every argument is such a constant, `Rate(240, 5s)`, which goes
beyond C#, where `new S()` is a default only for a value type `S` and only without arguments
(CS1736), while `Rate(n, 1s)` with a variable `n` stays no constant (§8, the owner,
2026-11-03, against any expression evaluated at each call that leaves it out, as Kotlin evaluates
one) *(assumed, §8: such a value as an argument of another, a field it leaves out taking its own
default (D11), which this rule holds to a constant where the field is declared, an instance of a
class being no such value, and the cost, that such a value is made at each call that leaves the
argument out)* (C# also takes `default`, which the record
does not name); one that is not is the compile error `default-not-constant`, at the parameter (the owner, 2026-10-10, against
`type-mismatch` (T1) and `syntax` (E4); the spelling of the id and its line are *(assumed: proposed on
2026-10-10, after the owner's choice)*). The default of a field of a class body may be any expression (K7).

Case: [functions/named-argument-order.kz](../corpus/functions/named-argument-order.kz)
```kurz
int Log(string s, int v) {
    print(s)
    return v
}

int Add(int a, int b) => a + b

print(Add(b: Log("b", 2), a: Log("a", 1)))
```

Case: [functions/default-not-constant.kz](../corpus/functions/default-not-constant.kz)
```kurz
string Pick() => "hi"

void Greet(string name, string word = Pick()) {
    print("{word} {name}")
}

Greet("Ann")
```

Case: [functions/default-data-of-constants.kz](../corpus/functions/default-data-of-constants.kz)
```kurz
data Rate(int BytesPerSecond, int GraceSeconds)

data Limits(int MaxLine = 8192, Rate BodyRate = Rate(240, 5))

data Pair(Rate First, Rate Second)

int PerSecond(Rate r = Rate(1, 1)) => r.BytesPerSecond

int Sum(Pair p = Pair(Rate(1, 1), Rate(2, 2))) => p.First.BytesPerSecond + p.Second.BytesPerSecond

a = Limits()
print("{a.MaxLine} {a.BodyRate.BytesPerSecond} {a.BodyRate.GraceSeconds}")
b = Limits(1000)
print("{b.MaxLine} {b.BodyRate.BytesPerSecond} {b.BodyRate.GraceSeconds}")
print(PerSecond())
print(PerSecond(Rate(7, 2)))
print(Sum())
```

Case: [functions/default-data-call-argument.kz](../corpus/functions/default-data-call-argument.kz)
```kurz
data Rate(int BytesPerSecond, int GraceSeconds)

int One() => 1

int PerSecond(Rate r = Rate(One(), 1)) => r.BytesPerSecond

print(PerSecond())
```

Case: [functions/default-class-instance.kz](../corpus/functions/default-class-instance.kz)
```kurz
class Counter(int Count)

int Read(Counter c = Counter(1)) => c.Count

print(Read())
```

Case: [functions/duplicate-function.kz](../corpus/functions/duplicate-function.kz)
```kurz
void Fill(List<int> xs) {
    print(xs.Count)
}

void Fill(mut List<int> xs) {
    xs.Add(1)
}

Fill(List<int>())
```

Case: [functions/overload.kz](../corpus/functions/overload.kz)
```kurz
void Show(int n) {
    print("int {n}")
}

void Show(string s) {
    print("text {s}")
}

void Show(int a, int b) {
    print("pair {a} {b}")
}

Show(4)
Show("four")
Show(4, 5)
```

Case: [functions/default-value.kz](../corpus/functions/default-value.kz)
```kurz
void Greet(string name, string word = "hello") {
    print("{word} {name}")
}

Greet("Ann")
Greet("Ann", "bye")
Greet("Ann", word: "bye")
```

### F13 (decided, §8) Which overload a call picks

A call can fit more than one function of its name: through widening (T8), when `Show(int)` and
`Show(long)` both take a `short`, and through a default value (F12), when `Show(int a)` and
`Show(int a, int b = 0)` both take one `int`. A function whose parameters have exactly the types
of the arguments, with no default value used, wins. When a generic function (T19) and a function
without type parameters both fit exactly, the one without type parameters wins, as in C#: `Show<T>(T
x)` and `Show(int n)` are not ambiguous for an `int` *(proposed)*. Any other call that fits more than
one function is the compile error `ambiguous-call`; a conversion (T10) at the call picks one. Which
type a literal argument has here is F14; the cases pass variables.

Case: [functions/overload-exact.kz](../corpus/functions/overload-exact.kz)
```kurz
void Show(int n) {
    print("int {n}")
}

void Show(long n) {
    print("long {n}")
}

void Show(int a, int b = 0) {
    print("two {a} {b}")
}

int i = 4
int j = 5
long big = 4
Show(i)
Show(big)
Show(i, j)
```

Case: [functions/overload-ambiguous.kz](../corpus/functions/overload-ambiguous.kz)
```kurz
void Show(int n) {
    print("int {n}")
}

void Show(long n) {
    print("long {n}")
}

short s = 4
Show(s)
```

A call of a name that has more than one function and fits none of them is the compile error
`argument-mismatch`, at the call, with a message that names each function and why it does not fit.
The owner chose this on 2026-10-21, against the error of the first function, in the order of
declaration, that the arguments bind to, under which the error changes when two declarations swap
places; the cost is a longer message, and an id that no longer names the argument's own fault, such
as `narrowing-conversion` (T7). The line is the first line of the call, as E1 reports an error of
the whole construct *(assumed: the answer said at the call)*.

Case: [functions/overload-no-fit.kz](../corpus/functions/overload-no-fit.kz)
```kurz
void Show(int n) {
    print("int {n}")
}

void Show(string s) {
    print("text {s}")
}

long big = 5
Show(big)
```

A call of a name that has one function, with an argument of a type its parameter does not take, is
`type-mismatch` (T1), a value of one type where another is required, and not `argument-mismatch`,
and so is a call of such a name whose argument is a call with a case the parameter's union does not
list (O4) *(assumed, §5: as the outcomes chapter reads both)*.

Case: [outcomes/keep-argument-partial.kz](../corpus/outcomes/keep-argument-partial.kz)
```kurz
data NotFound

// `Invalid` is the case of an enum's `From` that every file sees without declaring it (D18)
int | NotFound | Invalid Find(int id) {
    if id == 1 {
        return 10
    }
    if id == 2 {
        return Invalid
    }
    return NotFound
}

void Report(int | NotFound found) {
    print(found)
}

// the parameter names `NotFound` and not `Invalid`, so it does not hold the call's cases
// `Report` is the one function of its name, so the call is `type-mismatch`, not `argument-mismatch` (F13)
Report(Find(1))
```

Case: [outcomes/keep-argument-exact-fit-first.kz](../corpus/outcomes/keep-argument-exact-fit-first.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(int n) {
    print("the success")
}

void Show(long | NotFound x) {
    print("the union")
}

// `Show(int)` fits the success exactly, and the union needs a widening: the exact fit runs, and `NotFound` leaves `Shown`
int | NotFound Shown(int id) {
    Show(Find(id))
    return 0
}

match Shown(1) {
    int n => print("done")
    NotFound => print("none")
}
match Shown(2) {
    int n => print("done")
    NotFound => print("none")
}
```

Case: [outcomes/keep-argument-ambiguous-widening.kz](../corpus/outcomes/keep-argument-ambiguous-widening.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(long n) {
    print("the success")
}

void Show(long | NotFound x) {
    print("the union")
}

// one function takes the success with a widening and one keeps the cases with a widening: neither fits exactly, in a
// function whose result lists `NotFound`, where the one that unwraps the call would pass the case on
int | NotFound Run() {
    Show(Find(1))
    return 0
}
```

Case: [outcomes/keep-items-the-union-one.kz](../corpus/outcomes/keep-items-the-union-one.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(List<int> xs) {
    print("the items")
}

void Show(List<int | NotFound> xs) {
    print("the union")
}

int | NotFound Run() {
    // a list literal of calls is read as such a call: the function whose item type keeps the cases fits it exactly, and wins
    Show([Find(1)])
    return 1
}

match Run() {
    int n => print(n)
    NotFound => print("none")
}
```

### F14 (decided, §8) A literal as an argument of an overloaded function

For the pick of an overload (F13), a literal argument has the type it has on its own (L10, L11):
`int` for `4`, `long` for `4000000000` and for `4L`. So `Show(4)` picks `Show(int)` over
`Show(long)`, as in C#. Its own type also decides which functions fit at all: `Show(4)` with
`Show(short)` and `Show(long)` fits `Show(long)` only, through widening, and picks it; the literal
does not take `short` here, although `short s = 4` lets it (L10), because the pick would otherwise
depend on the value of the literal. This differs from C#, which picks `Show(short)`. *(proposed)*

Case: [functions/overload-literal.kz](../corpus/functions/overload-literal.kz)
```kurz
void Show(int n) {
    print("int {n}")
}

void Show(long n) {
    print("long {n}")
}

Show(4)
Show(4000000000)
Show(4L)
```

### F15 (decided, §4) A `mut` variable that a lambda reads

A lambda takes the value each variable it reads (F11) has when the lambda is made, as an
assignment does (M3). What its function assigns to a `mut` variable after that is not seen
inside the lambda, and what a check had shown about the variable at that point (N3) holds inside
it. This is not what C# does: there the lambda reads the variable itself and sees every later
assignment. A function declared at the top level (F5) reads no top-level variable (F16).

Case: [functions/capture-value.kz](../corpus/functions/capture-value.kz)
```kurz
mut n = 1
first = () => n
n = 2
print(first())
print(n)
```

Case: [null/lambda-keeps-narrowing.kz](../corpus/null/lambda-keeps-narrowing.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

mut email = Email(1)
if email != null {
    length = () => email.Bytes.Count
    email = Email(2)
    print(length())
}
```

### F16 (decided, §4) A top-level function that reads a top-level variable

A function declared at the top level (F5) reads no top-level variable: what it needs comes through
its parameters, and a top-level variable's name read in its body is `unknown-name` (V10), as if the
variable were declared in another block. The name stays reserved inside the function: on the left of
`=`, or in any other declaration there, it is `redeclared` (V9), so a function meant to reset the
program's counter does not declare a local instead (the owner, 2026-10-04, against a local that
`unused-variable` (V5) reports unless the body reads it afterwards, and that compiles when it does;
the cost is that the function cannot reuse the name, and that a top-level variable added later turns
every top-level function that already declares that name into a compile error). A parameter of the
function, a parameter of a lambda inside it and a loop variable are declarations in this sense and
`redeclared` too, and a variable reserves its name whether it is declared above or below the
function (F6). *(assumed: the reference's reading, accepted by its id on 2026-10-09 without its text
shown)* Only a variable at the outermost level of the top-level code reserves its name, since it
lives for the whole program; a loop variable, a variable inside a block of the top-level code and a
parameter of a lambda there do not, since no function can mean them (the owner, 2026-10-21, against
every variable declared outside a function reserving its name at any depth of the top-level code,
the reading this rule took on 2026-10-09, and against every name of the top-level code reserving it,
the parameter of a lambda there as well; Rust reserves none, as an inner `fn` may declare any name
of the function around it, and Kurz keeps those of the outermost level; the cost is that a local of
a top-level function may share its name with a loop variable, a variable of a block or a parameter
of a lambda of the top-level code, so that a reader may take one for the other, and that the
compiler tells the outermost level of the top-level code from the blocks inside it). That the lambda
is one of the
top-level code, so that inside a top-level function a lambda's parameter stays such a declaration, is
*(assumed: the answer's text named a lambda's parameter without saying where the lambda stands)*. A name that an
arm of a `match` of the top-level code binds reserves nothing either, as a variable of a block there
*(assumed: the answer named a loop variable, a variable inside a block and a lambda's parameter)*. In `count = count + 1` inside such a function the one error is `redeclared`, for the
declaration on the left, which the compiler reads first to learn whether the line declares; the read
on the right reports nothing more (the owner, 2026-10-09, against reporting `unknown-name` for the
read first, whose message points at the read while the fix is the whole line). The owner chose the
rule on 2026-10-03, against reading the variable at the call as a C# local function does, which
would have given two kinds of function two views of a captured variable (F15), and against taking
the value at the function's declaration as a lambda does, which a function declared above the
variable (F6) could never see; the cost is that a counter or a table of the program is passed into
every function, or made a field of a class.

Case: [functions/top-level-function-reads-no-variable.kz](../corpus/functions/top-level-function-reads-no-variable.kz)
```kurz
mut count = 0

int Next() => count + 1

count = Next()
print(count)
```

Case: [functions/top-level-name-reserved.kz](../corpus/functions/top-level-name-reserved.kz)
```kurz
mut count = 0

void Reset() {
    count = 0
}

Reset()
print(count)
```

Case: [functions/top-level-name-as-parameter.kz](../corpus/functions/top-level-name-as-parameter.kz)
```kurz
mut count = 0

int Next(int count) => count + 1

count = Next(count)
print(count)
```

Case: [functions/top-level-name-assigned-and-read.kz](../corpus/functions/top-level-name-assigned-and-read.kz)
```kurz
mut count = 0

void Bump() {
    count = count + 1
}

Bump()
print(count)
```

Case: [functions/top-level-loop-reserves-nothing.kz](../corpus/functions/top-level-loop-reserves-nothing.kz)
```kurz
int Twice(int n) {
    i = n * 2
    return i
}

for i in 1..2 {
    print(Twice(i))
}
```

Case: [functions/top-level-block-reserves-nothing.kz](../corpus/functions/top-level-block-reserves-nothing.kz)
```kurz
int Twice(int n) => n * 2

if true {
    n = 3
    print(Twice(n))
}
```

Case: [functions/top-level-lambda-reserves-nothing.kz](../corpus/functions/top-level-lambda-reserves-nothing.kz)
```kurz
int Twice(int n) => n * 2

f = (int n) => Twice(n)
print(f(4))
```

### F17 (decided, §12) A top-level `return` with a value

A `return` with a value among the top-level statements, `return 2`, ends the program with that value as
its exit code, and the end of the top-level code, and a bare `return` among its statements, end it with
0, whether or not a `return` with a value stands beside them (§12, the owner, 2026-11-01, "Top-level
return n + Exit" and "0, silently"). The value is an `int`, and a value of another type is
`type-mismatch`. Linux takes the low 8 bits of the value, as of `Environment.Exit`'s (S23), and a build
for Windows says `not supported` to a `return` with a value among the top-level statements (S11), as to
`Environment.Exit`. *(assumed, §12: the type, the low 8 bits and Windows)*

No case for a code other than 0: a case that expects output ends with 0.

Case: [functions/top-level-return.kz](../corpus/functions/top-level-return.kz)
```kurz
print("a")
if args.Count == 0 {
    return 0
}
print("b")
```

Case: [functions/top-level-return-end.kz](../corpus/functions/top-level-return-end.kz)
```kurz
print("a")
if args.Count > 0 {
    return 3
}
if args.Count > 1 {
    return
}
print("b")
```

Case: [functions/top-level-return-mismatch.kz](../corpus/functions/top-level-return-mismatch.kz)
```kurz
print("a")
if args.Count == 0 {
    return "stop"
}
```

### F18 (decided, §12) `Hint.BlackBox`

`Hint.BlackBox(value)` gives back its argument, a value of any type, `T BlackBox<T>(T value)`, as
Rust's `std::hint::black_box` does (§12, the owner, 2026-11-03, "`Hint.BlackBox(x)`" and "Any value,
as Rust's"). `Hint` is a name every file sees, so a file's own declaration of it is `redeclared`.
Every program may call it, without `raw`. The optimiser knows nothing of the value the call gives back and makes the argument before
the call, as best effort, the way Rust's does, with no promise of constant time (§12, the owner,
2026-11-03, "The defaults of the round stand"). *(assumed, §12: type arguments are refused, as
`string.Join` refuses them, and a member of `Hint` other than `BlackBox` is `unknown-name` (V10), as
a function `Sha256` lacks is)*

No case for what the optimiser sees: a case sees only the value the call gives back.

Case: [functions/black-box.kz](../corpus/functions/black-box.kz)
```kurz
data Point(int X, int Y)

n = Hint.BlackBox(1000)
print(n + 1)
hay = Hint.BlackBox("a haystack of {n}")
print(hay)
xs = Hint.BlackBox([3, 1, 2])
mut ys = Hint.BlackBox(xs)    // the list the variable holds, which the change of `ys` leaves as it is (M3)
ys.Add(4)
print(xs.Count)
print(ys.Count)
p = Hint.BlackBox(Point(1, 2))
print(p.X + p.Y)
```

Case: [functions/black-box-redeclared.kz](../corpus/functions/black-box-redeclared.kz)
```kurz
// a hint of the program's own, under a name every file sees
data Hint(string Text)

print(1)
```

Case: [functions/black-box-unknown-member.kz](../corpus/functions/black-box-unknown-member.kz)
```kurz
x = Hint.Opaque(1)
print(x)
```
