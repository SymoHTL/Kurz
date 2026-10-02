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

What counts as a check follows the flow of the code, as the nullable analysis of C# does. A
variable of the type `T?` has the type `T`:

- in the block of `if name != null { }`, and in the `else` block of `if name == null { }`;
- after an `if name == null { }` whose block always leaves, for the rest of the block that holds
  the `if`;
- in the body of `while name != null { }`;
- on the right of `name != null && ...` and of `name == null || ...`;
- after an assignment of a value whose type is not nullable (N7).

`!` turns a test around, and a test of `a?.b != null` shows that `a` is not null either. A block
always leaves when it ends in `return` or `throw`, or in `break` or `continue` inside a loop; the
record marks that list as *(assumed)*. Everywhere else the variable is nullable, and N2 applies.

A path of fields such as `u.Email` is narrowed as a variable is. An index (`map[key]`) and the
result of a call are not, as in C#: they take `??` (N4), `?.` (N5) or a variable of their own.
What happens to a narrowed path that a call or an assignment can change is N9, and what a lambda
knows about a variable around it is F15. The cases stay clear of both: the path they narrow runs
through a field of a `data` value, and no lambda reads a variable they narrow.

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

Case: [null/else-narrows.kz](../corpus/null/else-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

void Show(int id) {
    email = Email(id)
    if email == null {
        print("none")
    } else {
        print(email.Bytes.Count)
    }
}

Show(1)
Show(2)
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

Case: [null/while-narrows.kz](../corpus/null/while-narrows.kz)
```kurz
data Node(int Value, Node? Next)

mut Node? node = Node(1, Node(2, null))
while node != null {
    print(node.Value)
    node = node.Next
}
print("done")
```

Case: [null/condition-narrows.kz](../corpus/null/condition-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

email = Email(1)
if email != null && email.Bytes.Count > 5 {
    print("long")
}
other = Email(2)
if other == null || other.Bytes.Count == 0 {
    print("empty")
}
```

Case: [null/test-forms.kz](../corpus/null/test-forms.kz)
```kurz
data User(string Name, string? Email)

User? Find(int id) {
    if id == 1 {
        return User("Ann", "a@example.com")
    }
    return null
}

u = Find(1)
if u?.Email != null {
    print(u.Name)
}
if !(u == null) {
    print(u.Name)
}
```

Case: [null/path-narrows.kz](../corpus/null/path-narrows.kz)
```kurz
data User(string Name, string? Email)

u = User("Ann", "a@example.com")
if u.Email != null {
    print(u.Email.Bytes.Count)
}
```

Case: [null/narrowing-ends-with-the-block.kz](../corpus/null/narrowing-ends-with-the-block.kz)
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
print(email.Bytes.Count)
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

### N7 (decided, §4) An assignment to a narrowed variable

An assignment replaces what is known about a variable, as in C#. Assigning a value whose type is
not nullable narrows the variable from that line on. Assigning a nullable value to a narrowed
`mut` variable is allowed and makes it nullable again from that line on, so a use after it needs
a new check (N2). That is what lets a loop walk a chain: `node = node.Next` assigns a nullable
value to the variable the loop has just checked. The record marks as *(assumed)* what else
counts as such an assignment: handing the variable to a `mut` parameter (M7), as `ref` does in
C#, and a call of a `mut` method (M5) on a value that a narrowed path runs through. The cases
assign.

Case: [null/while-narrows.kz](../corpus/null/while-narrows.kz)

Case: [null/assignment-narrows.kz](../corpus/null/assignment-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

mut email = Email(2)
email = "b@example.com"
print(email.Bytes.Count)
```

Case: [null/assignment-ends-narrowing.kz](../corpus/null/assignment-ends-narrowing.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

mut email = Email(1)
if email != null {
    print(email.Bytes.Count)
    email = Email(2)
    print(email.Bytes.Count)
}
```

### N8 (assumed, §4) A nullable result is not unwrapped

`T?` is the union `T | null` (N1), and the unwrap rule (O2) is about unions. It does not apply
here: a call of a function that returns `T?` yields a nullable value, which N2 to N5 handle, and
`null` never leaves the calling function by itself. Without this N2 could never apply to the
result of a call, and the cases of this chapter that call `Email` rely on it.

Case: [null/unchecked.kz](../corpus/null/unchecked.kz)

### N9 (open) A narrowed path that something else can change

N3 narrows a path of fields. A path whose every step is a field of a `data` value, or a field
that is not `mut`, changes only through what the function itself does to it (N7). Two kinds of
path can change behind its back: one through a `mut` field of a class instance, which a call can
assign through another reference (K2), and one through a `weak` reference, whose target a call
or an assignment can free (R3). C# keeps such a path narrowed across the call, which its
analysis can afford because it only warns. Options: (a) the narrowing of such a path ends at the
next call and at the next assignment, so a second use after a call takes a variable of its own;
(b) such a path is never narrowed, and every use takes a variable of its own, `?.` or `??`;
(c) as in C#: the path stays narrowed, and a use where the value has become null in between
raises an exception when the program runs. Lean: (a). It keeps N2 without a check at run time
and accepts the common shape, a test directly followed by the use. The cost: an error that
depends on a call between the test and the use.
