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

Inside the block of `if name != null { }`, the variable `name` has the type `T`. No other form
narrows. After a test that leaves early, `if name == null { return }`, the variable is still
nullable, and so is a path such as `user.Email` inside a check of that path. `??` (N4), `?.` (N5)
and a variable of its own for the path cover those.

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

Case: [null/early-return-does-not-narrow.kz](../corpus/null/early-return-does-not-narrow.kz)
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
