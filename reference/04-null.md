# 4. Null

### N1 (decided, §4) `T?`

`null` exists. `T?` is short for `T | null`: a value of that type is a `T` or `null`.

Case: [null/coalesce.kz](../corpus/null/coalesce.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

print(Email(1) ?? "none")
print(Email(2) ?? "none")
```

### N2 (decided, §4) No use without a check

Using a value of type `T?` where a `T` is needed, or reaching a member through it, without a
check is the compile error `nullable-unchecked`.

Case: [null/unchecked.kz](../corpus/null/unchecked.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

email = Email(1)
print(email.Bytes.Count)
```

### N3 (decided, §4) What a check is

Two forms narrow. Inside the block of `if name != null { }`, the variable `name` has the type
`T`. After `if name == null { }` whose block always leaves, `name` has the type `T` for the rest
of the block that holds the `if`, as in C#. A block always leaves when it ends in `return` or
`throw`, or in `break` or `continue` inside a loop; the record marks that list as *(assumed)*.

No other form narrows. A path such as `user.Email` stays nullable inside a check of that path,
and so does a variable in the rest of a condition (`name != null && ...`) and in an `else`
block. `??` (N4), `?.` (N5) and a variable of its own cover those. What an assignment to a
narrowed `mut` variable does is N7; the cases narrow variables that are not `mut`.

Case: [null/checked.kz](../corpus/null/checked.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

email = Email(1)
if email != null {
    print(email.Bytes.Count)
}
```

Case: [null/early-return-narrows.kz](../corpus/null/early-return-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

int Length(int id) {
    email = Email(id)
    if email == null {
        return 0
    }
    return email.Bytes.Count
}

print(Length(1))
print(Length(2))
```

Case: [null/early-continue-narrows.kz](../corpus/null/early-continue-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

for id in 0..2 {
    email = Email(id)
    if email == null {
        continue
    }
    print(email.Bytes.Count)
}
```

Case: [null/path-does-not-narrow.kz](../corpus/null/path-does-not-narrow.kz)
```kurz
data User(string Name, string? Email)

u = User("Ann", "a@example.com")
if u.Email != null {
    print(u.Email.Bytes.Count)
}
```

### N4 (decided, §4) `??`

`a ?? b` is `a` when `a` is not null, and `b` otherwise, as in C#.

Case: [null/coalesce.kz](../corpus/null/coalesce.kz)

### N5 (decided, §4) `?.`

`a?.Member` is null when `a` is null and the member otherwise, as in C#. The rest of the chain is
skipped with it.

Case: [null/conditional-access.kz](../corpus/null/conditional-access.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

print(Email(1)?.Bytes.Count ?? 0)
print(Email(2)?.Bytes.Count ?? 0)
```

### N6 (assumed, §4) `null` needs a nullable type

`null` is a value of nullable types only. Giving it to a type without `?` is the compile error
`type-mismatch`.

Case: [null/not-nullable.kz](../corpus/null/not-nullable.kz)
```kurz
string name = null
print(name)
```

### N7 (open) An assignment to a narrowed variable

N3 gives a narrowed variable the type `T`. For a `mut` variable of the type `T?` that leaves
open what `name = Find()` does after the check, when `Find` returns `T?`. Options: (a) the
variable has the type `T` wherever it is narrowed, so the assignment is the compile error
`type-mismatch` (N6), and a second lookup takes a second variable; (b) as in C#: the assignment
is allowed and ends the narrowing, so the variable is nullable again from that line on.
Lean: (b). A loop that walks a chain assigns `node = node.Next`, a nullable value, to the
variable it has just checked. The cost is a checker that follows the statements of a block in
order instead of deciding once for the block.

### N8 (proposed) A nullable result is not unwrapped

`T?` is the union `T | null` (N1), and the unwrap rule (O2) is about unions. It does not apply
here: a call of a function that returns `T?` yields a nullable value, which N2 to N5 handle, and
`null` never leaves the calling function by itself. The record does not say this. Without it N2
could never apply to the result of a call, and the cases of this chapter that call `Email` rely
on it.

Case: [null/unchecked.kz](../corpus/null/unchecked.kz)
