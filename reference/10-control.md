# 10. Control flow

### C1 (decided, §8) `if`

`if condition { }`. The condition needs no parentheses, and `if (x > 5) {` compiles all the same,
since an expression in parentheses is an expression like any other (the owner, 2026-10-09, against
an id of its own for the parentheses, which would put a special case into the parser for one
spelling). The braces are always required, and the block opens on the line of the condition: a
statement in place of the block, and a `{` on the next line, which L2 ends the statement before and
L4 does not continue it to, are the compile error `braces-required` (the owner, 2026-10-09, against
continuing the statement onto the `{`, a fourth exception to L2).

Case: [control/if.kz](../corpus/control/if.kz)
```kurz
x = 7
if x > 5 {
    print("big")
}
```

Case: [control/braces-required.kz](../corpus/control/braces-required.kz)
```kurz
x = 7
if x > 5 print(x)
```

Case: [control/if-parenthesized.kz](../corpus/control/if-parenthesized.kz)
```kurz
x = 7
if (x > 5) {
    print("big")
}
```

Case: [control/brace-next-line.kz](../corpus/control/brace-next-line.kz)
```kurz
x = 7
if x > 5
{
    print("big")
}
```

### C2 (assumed, §8) `else`

`else` and `else if` follow the closing brace on the same line, with the meaning they have in C#.

Case: [control/else.kz](../corpus/control/else.kz)
```kurz
int Sign(int n) {
    if n > 0 {
        return 1
    } else if n < 0 {
        return -1
    } else {
        return 0
    }
}

print(Sign(5))
print(Sign(-5))
print(Sign(0))
```

### C3 (assumed, §8) A condition is a `bool`

A condition has the type `bool`. Any other type is the compile error `type-mismatch`, as in C#:
a number or a nullable value is not a condition.

Case: [control/condition-must-be-bool.kz](../corpus/control/condition-must-be-bool.kz)
```kurz
x = 1
if x {
    print("yes")
}
```

### C4 (decided, §5) `match`

`match value { Case name => ... }` runs the arm whose case the value is. An arm names a case type,
optionally followed by a name for the value as that type. O8 says which arms have to be there, and
C9 what else an arm can test. When a value fits several arms, the first arm in source order whose
type the value is runs, as C#'s `switch` does; an arm may name a class or a `data` type derived from
a case (D6, K6), `Admin` where the case is `User`; and an arm that can never run, because an earlier
arm covers its type, is the compile error `unreachable-arm` (the owner, 2026-10-09, against arms
that name cases only, under which a `match` cannot tell an `Admin` from a `User`; the cost is a row
in the table and a subtype check per arm).

Case: [data/union.kz](../corpus/data/union.kz)
```kurz
data Circle(int Radius)
data Square(int Side)

void Describe(Circle | Square shape) {
    match shape {
        Circle c => print("circle {c.Radius}")
        Square s => print("square {s.Side}")
    }
}

Describe(Circle(2))
Describe(Square(3))
```

Case: [control/match-derived-arm.kz](../corpus/control/match-derived-arm.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

void Show(User | NotFound u) {
    match u {
        Admin a => print("admin {a.Level}")
        User x => print("user {x.Name}")
        NotFound => print("none")
    }
}

Show(Admin("Ann", 2))
Show(User("Bea"))
Show(NotFound)
```

Case: [control/match-unreachable-arm.kz](../corpus/control/match-unreachable-arm.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

void Show(User | NotFound u) {
    match u {
        User x => print("user {x.Name}")
        Admin a => print("admin {a.Level}")
        NotFound => print("none")
    }
}

Show(Admin("Ann", 2))
```

### C5 (assumed, §8) `while`

`while condition { }` repeats its block as long as the condition holds, as in C# without the
parentheses.

Case: [control/while.kz](../corpus/control/while.kz)
```kurz
mut i = 1
mut sum = 0
while i <= 3 {
    sum = sum + i
    i = i + 1
}
print(sum)
```

### C6 (assumed, §8, §13) `for ... in`

`for name in collection { }` runs its block once for each item: in order for a list and a range
(C8), and in the order of the map's storage for a map (M10), where an item is an entry with a
`Key` and a `Value` *(proposed: the names)*. The loop runs over the collection as it was when the
loop started: the collection is a value (M1), and a change to the variable inside the block is a
change to the variable, not to what the loop walks *(proposed reading of M3)*. The record's stream
sample uses this form.

Case: [control/for-in.kz](../corpus/control/for-in.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
for x in xs {
    print(x)
}
```

### C7 (assumed, §8) `break` and `continue`

Inside a loop, `break` leaves the loop and `continue` goes on with the next round. Inside a
`match` arm (C4) they still mean the loop around the `match`, unlike C#, where `break` leaves a
`switch`: an arm ends where its statement ends and needs no `break`. Inside a lambda (F7) there is
no loop around them: `break` or `continue` there, or outside every loop, is the compile error
`break-outside-loop`. *(proposed)*

Case: [control/break-in-match.kz](../corpus/control/break-in-match.kz)
```kurz
for i in 1..5 {
    match i {
        3 => break
        else => print(i)
    }
}
```

Case: [control/break-outside-loop.kz](../corpus/control/break-outside-loop.kz)
```kurz
print(1)
break
```

Case: [control/break-continue.kz](../corpus/control/break-continue.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
xs.Add(4)
for x in xs {
    if x == 2 {
        continue
    }
    if x == 4 {
        break
    }
    print(x)
}
```

### C8 (decided, §8) Ranges and counting loops

`a..b` is the range of the integers from `a` to `b` with both ends included. `a..<b` leaves `b`
out. `for i in a..<b { }` runs its block once for each number of the range, in order. `a..<a`
holds no number and runs the block zero times. The C form with three parts,
`for (i = 0; i < n; i++)`, does not exist.

A range never counts down, and a range whose end lies below its start is an error, not an empty
range. That holds for both forms: `3..2` and `3..<2` are errors, `3..<3` is not. It is the compile
error `reversed-range` when both ends are expressions made only of literals, and an exception
`reversed-range-at-run-time` where the range is evaluated otherwise, before the first round of a
loop over it (E3). `for i in 1..n` therefore throws when `n` is 0; a loop that may run zero times is
written with `..<`. The C form with three parts is the compile error `syntax`, as is every other
text no rule gives a meaning (E4). The range and its loop variable have the common type of the two
ends, found as `+` finds it (T8, T9, T28): `0..<n` with a `long` `n` is a range of `long`, and ends
with no common type are `sign-mix` (T9) (the owner, 2026-10-09, against a range that is always
`int`, under which a loop over a `long` range is written by hand; the cost is that the loop
variable's type follows the end, which a reader finds at the declaration of `n`).

Case: [control/range-empty.kz](../corpus/control/range-empty.kz)
```kurz
for i in 3..<3 {
    print(i)
}
print("done")
```

Case: [control/range-reversed.kz](../corpus/control/range-reversed.kz)
```kurz
for i in 3..2 {
    print(i)
}
```

Case: [control/range-reversed-at-run-time.kz](../corpus/control/range-reversed-at-run-time.kz)
```kurz
void Count(int n) {
    for i in 1..n {
        print(i)
    }
}

Count(2)
Count(0)
```

Case: [control/range-inclusive.kz](../corpus/control/range-inclusive.kz)
```kurz
for i in 1..3 {
    print(i)
}
```

Case: [control/range-exclusive.kz](../corpus/control/range-exclusive.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
for i in 0..<xs.Count {
    print(xs[i])
}
```

Case: [control/range-long.kz](../corpus/control/range-long.kz)
```kurz
long n = 3
for i in 0..<n {
    print(i)
}
```

Case: [control/range-long-narrows.kz](../corpus/control/range-long-narrows.kz)
```kurz
long n = 3
for i in 0..<n {
    int x = i
    print(x)
}
```

Case: [control/range-sign-mix.kz](../corpus/control/range-sign-mix.kz)
```kurz
int a = 0
uint n = 3
for i in a..<n {
    print(i)
}
```

### C9 (decided, §5) What else an arm can test, and `match` as an expression

An arm can name a literal, which matches a value equal to it, and the last arm can be `else`,
which matches whatever no arm before it matched. A `match` over literals that does not list
every value of its type needs the `else` arm (O8). `match` can be used as an expression: its
value is the value of the arm that ran, and its type is the type the arms share; where a type is
written or expected, each arm's value has to fit it (T1), and where none is, the arms have to
have one type, or the `match` is the compile error `type-mismatch`, as a C# switch expression
without a natural type is. *(proposed: against a union of the arms' types, which no other
expression forms on its own)* An arm has no patterns over fields and no conditions.

Case: [control/match-literal.kz](../corpus/control/match-literal.kz)
```kurz
string Name(int n) => match n {
    0 => "zero"
    1 => "one"
    else => "many"
}

print(Name(0))
print(Name(1))
print(Name(7))
```

Case: [control/match-literal-no-else.kz](../corpus/control/match-literal-no-else.kz)
```kurz
n = 3
match n {
    0 => print("zero")
}
```
