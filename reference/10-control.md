# 10. Control flow

### C1 (decided, §8) `if`

`if condition { }`. There are no parentheses around the condition, and the braces are always
required. A statement in place of the block is the compile error `braces-required`.

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

`match value { Case name => ... }` runs the arm whose case the value is. An arm names a case
type, optionally followed by a name for the value as that type. O8 says which arms have to be
there, and C9 what else an arm can test.

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
range. That holds for both forms: `3..2` and `3..<2` are errors, `3..<3` is not. It is the
compile error `reversed-range` when both ends are expressions made only of literals, and an
exception where the range is evaluated otherwise, before the first round of a loop over it; the
exception has no id yet (E3). `for i in 1..n` therefore throws when `n` is 0; a loop that may run
zero times is written with `..<`. The C form with three parts is the compile error `syntax`, as is
every other text no rule gives a meaning (E4).

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
