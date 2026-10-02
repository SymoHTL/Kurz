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
as in C#.

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

A lambda is written as in C#: `x => x * 2`.

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
exposes it; `prot` exposes a member to inheriting types.

No case: it takes several folders, and the corpus holds single files so far.

### F10 (decided, §8) Folders and `use`

A folder is a namespace; there is no `namespace` line. Files in one folder see each other
without imports. `use` brings in another folder or a package.

No case: it takes several files, and the corpus holds single files so far.

### F11 (decided, §4) Function types and what a lambda captures

The type of a function value is written with an arrow: the parameter types between brackets,
`=>`, and the result type, as in `(int) => bool`. A lambda reads the variables around it and
cannot assign them: assigning one is the compile error `capture-assign`.

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

Functions may share a name when their parameters differ. A parameter can have a default value,
and an argument can be passed by name (D11). Which function a call picks when more than one fits
is F13.

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

### F13 (open) Which overload a call picks

A call can fit more than one function of its name: through widening (T8), when `Show(int)` and
`Show(long)` both take a `short`; through a default value (F12), when `Show(int a)` and
`Show(int a, int b = 0)` both take `Show(4)`; and through a literal, which takes the type that is
expected (L10). Options: (a) the rules of C# for the better function, all of them; (b) a function
whose parameters have exactly the types of the arguments, with no default used, wins, and any
other call that fits more than one function is a compile error; (c) no ranking: a call that fits
more than one function is always a compile error. Lean: (b). The C# rules fill pages and still
surprise their readers, and an error that names the two functions that fit is cheap to fix at
the call.
