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

### F3 (proposed) Block body and `return`

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

### F4 (proposed) `void`

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

### F6 (proposed) Use before declaration

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

### F8 (proposed, §4) Parameters are immutable

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

### F11 (open) Function types and closures

How the type of a function value is written, and what a lambda may do with the variables around
it. The record lists closures as a missing detail (section 14). Options for the type:
(a) `Func<int, bool>` as in C#; (b) an arrow, `(int) => bool`. For captures: (c) a lambda reads
the variables around it and cannot assign them; (d) it can assign a `mut` variable, which then
has to outlive it. Lean: (b) and (c). (d) makes a `mut` variable shared between the lambda and
its function, and section 4 of the record gives every other change a visible `mut` at the call.

### F12 (open) Overloads, default values, named arguments

Whether two functions may share a name when their parameters differ, and whether a parameter can
have a default value (see D11 for named arguments). Lean: defaults yes, overloads no: a default
covers most overloads with less code, and without overloads a name means one function for the
reader and for `mock`.
