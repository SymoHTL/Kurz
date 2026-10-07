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

No case shows the folder part: it takes several folders, and a case of the corpus is a single
file.

### F10 (decided, §8) Folders and `use`

A folder is a namespace; there is no `namespace` line. Files in one folder see each other
without imports. `use` brings in another folder or a package.

No case: it takes several files, and a case of the corpus is a single file.

### F11 (decided, §4) Function types and what a lambda captures

The type of a function value is written with an arrow: the parameter types between brackets,
`=>`, and the result type, as in `(int) => bool`. A lambda reads the variables around it and
cannot assign them: assigning one is the compile error `capture-assign`. So is every change of a
captured variable: an assignment into a path from it (M4), a `mut` method called on it or on a
path from it (M5), and passing it as a `mut` argument (M7); the lambda holds a copy (F15), and a
change to the copy would be lost or, through a class instance on the path, would not be a change
of the copy at all. *(proposed: the record speaks of assigning)*

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

### F12 (decided, §8) Overloads, default values, named arguments

Functions may share a name when their parameters differ: in their number, or in the type of a
parameter at the same place. Names, default values and the `mut` marker (M7) make no difference,
so `Fill(List<int>)` next to `Fill(mut List<int>)` is the compile error `duplicate-function`, and a
call of one of them without `mut` is M7's `mut-at-call`, never a quiet pick of the other. A
parameter can have a default value, and an argument can be passed by name (D11). Which function a
call picks when more than one fits is F13. *(proposed: what "differ" is, and the id)*

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

A function declared at the top level (F5) reads no top-level variable: what it needs comes
through its parameters, and a top-level variable's name read in its body is `unknown-name`
(V10), as if the variable were declared in another block. The name stays reserved inside the
function: on the left of `=`, or in any other declaration there, it is `redeclared` (V9), so a
function meant to reset the program's counter does not declare a local instead (the owner,
2026-10-04, against a silent local; the cost is that the function cannot reuse the name). The
owner chose the rule on 2026-10-03, against
reading the variable at the call as a C# local function does, which would have given two kinds of
function two views of a captured variable (F15), and against taking the value at the function's
declaration as a lambda does, which a function declared above the variable (F6) could never see;
the cost is that a counter or a table of the program is passed into every function, or made a
field of a class.

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
