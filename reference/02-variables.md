# 2. Variables

### V1 (decided, §8) An immutable variable

`name = expression` declares a variable that keeps its value.

Case: [variables/declare.kz](../corpus/variables/declare.kz)
```kurz
x = 4
print(x + 5)
```

### V2 (decided, §8) A mutable variable

`mut name = expression` declares a variable that can be assigned again.

Case: [variables/mut.kz](../corpus/variables/mut.kz)
```kurz
mut x = 4
x = x + 5
print(x)
```

### V3 (decided, §8) A written type

The type may be written in front of the name: `int x = 5`.

Case: [variables/typed.kz](../corpus/variables/typed.kz)
```kurz
int x = 4
string s = "ok"
print(x)
print(s)
```

### V4 (assumed, §4) `mut` with a written type

`mut` comes first: `mut int x = 5`. A primary constructor in section 4 of the record puts it
there (`mut int Age`).

Case: [variables/mut-typed.kz](../corpus/variables/mut-typed.kz)
```kurz
mut int x = 4
x = 5
print(x)
```

### V5 (decided, §8) An unused variable is an error

A variable that is never used is the compile error `unused-variable`. This also catches a
mistyped name, which would otherwise declare a new variable.

Case: [variables/unused.kz](../corpus/variables/unused.kz)
```kurz
x = 1
y = 2
print(x)
```

### V6 (decided, §8) What "used" means

A variable is used when it is read at least once. Being assigned again does not count.

Case: [variables/unused-written.kz](../corpus/variables/unused-written.kz)
```kurz
mut x = 1
x = 2
```

### V7 (decided, §4, §8) No second assignment without `mut`

Assigning to a variable that was declared without `mut` is the compile error `assign-immutable`,
reported at the assignment.

Case: [variables/assign-immutable.kz](../corpus/variables/assign-immutable.kz)
```kurz
x = 1
print(x)
x = 2
```

### V8 (decided, §8) Declaration or assignment

`name = expression` assigns when a variable of that name is visible, and declares one when none
is. A variable therefore cannot hide another one: inside a block, the same line is an assignment
to the outer variable.

Case: [variables/no-shadowing.kz](../corpus/variables/no-shadowing.kz)
```kurz
x = 1
if x > 0 {
    x = 2
}
print(x)
```

### V9 (assumed, §8) A written type always declares

`int x = 5` is a declaration. When a variable named `x` is already visible, it is the compile
error `redeclared`.

Case: [variables/redeclared.kz](../corpus/variables/redeclared.kz)
```kurz
x = 1
int x = 2
print(x)
```

### V10 (assumed, §8) Scope

A variable is visible from its declaration to the end of the block that holds the declaration,
as in C#. A name that is not visible is the compile error `unknown-name`.

Case: [variables/block-scope.kz](../corpus/variables/block-scope.kz)
```kurz
x = 1
if x > 0 {
    y = x + 1
    print(y + 1)
}
print(y)
```

### V11 (assumed, §8) Every declaration has a value

There is no way to declare a variable without giving it a value.

No case: there is no syntax to write one with.

### V12 (decided, §8) `mut` that is never needed

A variable that is declared `mut` and that nothing changes is a warning, not an error. Changing
it is assigning it again, assigning into it through a path (M4), calling a `mut` method on it
(M5) or passing it as a `mut` argument (M7). An unneeded `mut` is noise, not a bug, and an error
would get in the way while a function is half written.

No case: a warning changes neither what a program prints nor whether it compiles, and a corpus
header cannot expect one.
