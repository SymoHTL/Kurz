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

### C2 (proposed) `else`

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

### C3 (proposed) A condition is a `bool`

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
there.

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

### C5 (proposed) `while`

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

### C6 (proposed, §13) `for ... in`

`for name in collection { }` runs its block once for each item, in order. The record's stream
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

### C7 (proposed) `break` and `continue`

Inside a loop, `break` leaves the loop and `continue` goes on with the next round, as in C#.

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

### C8 (open) Counting loops and ranges

How a loop over numbers is written, and what `a..b` means. The record writes ranges in two places
(`where 0..150`, `supports 2..`), and the first reads as including both ends. In C# a range
excludes its end. Options: (a) `a..b` includes both ends everywhere; (b) `a..b` excludes the end,
as in C#, and a constraint is written with the last allowed value plus one; (c) two spellings:
`a..b` includes both ends and `a..<b` excludes the end, as in Kotlin and Swift. Also open:
whether the C form `for (i = 0; i < n; i++)` exists. Lean: (c), and no C form: a constraint such
as `0..150` reads as it is spoken, and a loop over positions is `for i in 0..<items.Count`.

### C9 (open) Patterns in `match`

Whether an arm can test more than the case type: a literal (`0 => ...`), a field
(`User { Age: 0 } => ...`), a condition (`User u when u.Age > 17 => ...`), or a default arm.
Whether `match` can be an expression that yields a value. Lean: literals, a default arm written
`else`, and `match` as an expression; field patterns only when something needs them.
