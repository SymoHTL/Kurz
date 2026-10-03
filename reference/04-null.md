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

What counts as a check follows the flow of the code, as the nullable analysis of C# does: what
that analysis proves about a variable holds here, with the two differences this chapter states
(N9, and the end of a narrowing by assignment in N7). The record marks the version as
*(assumed)*: C# 12. The list that follows shows the forms the corpus covers; it is not the rule,
and a form C# narrows that it leaves out, such as the block of `if name != null && other { }` or
the code after `if name != null { } else { return }`, narrows here too. A variable of the type
`T?` has the type `T`:

- in the block of `if name != null { }`, and in the `else` block of `if name == null { }`;
- after an `if name == null { }` whose block always leaves, for the rest of the block that holds
  the `if`;
- in the body of `while name != null { }`;
- on the right of `name != null && ...` and of `name == null || ...`;
- after an assignment of a value whose type is not nullable (N7).

`!` turns a test around, and a test of `a?.b != null` shows that `a` is not null either. A block
always leaves when its end cannot be reached, as C# decides reachability: it ends in `return` or
`throw`, in `break` or `continue` inside a loop, or in an `if` with `else` or a `match` whose
every arm leaves; the record marks that definition as *(assumed)*. Where the analysis proves
nothing, the variable is nullable, and N2 applies.

A path of fields such as `u.Email` is narrowed as a variable is. An index (`map[key]`) and the
result of a call are not, as in C#: they take `??` (N4), `?.` (N5) or a variable of their own.
A path that a call or an assignment can change behind the function's back is narrowed for a
shorter stretch (N9). Inside a lambda, what was known about a variable, or about a path through
`data` values, where the lambda was made holds, because the lambda takes the value the variable
has there (F15). What was known about a path N9 covers does not hold inside a lambda: the lambda
takes the reference, not the field, and the field can be null when the lambda runs. *(the record
marks the last sentence as assumed; it was proposed on 2026-10-03)*

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
`type-mismatch`, and never N2's `nullable-unchecked`: N2 is about a value of a type `T?`, and the
literal `null` has no such type until a nullable type takes it.

Case: [null/not-nullable.kz](../corpus/null/not-nullable.kz)
```kurz
string name = null
print(name)
```

### N7 (decided, §4) An assignment to a narrowed variable

An assignment replaces what is known about a variable, as in C#. Assigning a value whose type is
not nullable narrows the variable from that line on, up to the end of the block that holds the
assignment; where the branches of an `if` or the paths around a loop meet, the variable is
nullable when any of them leaves it nullable, the join C# makes, so `if c { e = Email(2) }`
narrows `e` inside the block and not after it *(assumed: proposed on 2026-10-03)*. A `weak`
reference (R3) is never narrowed by an assignment: nothing need hold its new target, which can be
freed before the next line. Assigning a nullable value to a narrowed `mut` variable is allowed
and makes it nullable again from that line on, so a use after it needs a new check (N2). That is what lets a loop walk a chain: `node = node.Next` assigns a nullable
value to the variable the loop has just checked. The record marks as *(assumed)* what else
counts as such an assignment: handing the variable to a `mut` parameter (M7), as `ref` does in
C#, and a call of a `mut` method (M5) on a value that a narrowed path runs through. The cases
assign.

Case: [null/while-narrows.kz](../corpus/null/while-narrows.kz)

Case: [null/assignment-narrows.kz](../corpus/null/assignment-narrows.kz)

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

### N8 (decided, §4) A nullable result is not unwrapped

`T?` is the union `T | null` (N1), and the unwrap rule (O2) is about unions. It does not apply
here: a call of a function that returns `T?` yields a nullable value, which N2 to N5 handle, and
`null` never leaves the calling function by itself. Without this N2 could never apply to the
result of a call, and the cases of this chapter that call `Email` rely on it. O2 applies to a
written outcome list (O1) and to nothing else: not to `T?`, not to an `enum` (D12), which is a
union in its declaration only, and not to a union written as the type of a value. A function that
returns `Plan` yields a `Plan`. *(this reading of O2's reach is proposed)*

Case: [null/unchecked.kz](../corpus/null/unchecked.kz)

### N9 (decided, §4) A narrowed path that something else can change

N3 narrows a path of fields. A path whose every step is a field of a `data` value, or a field
that is neither `mut` nor `weak`, changes only through what the function itself does to it (N7).
Two kinds of path can change behind its back: one through a `mut` field of a class instance,
which a call can assign through another reference (K2), and one through a `weak` reference,
whose target a call or an assignment can free (R3). Such a path is narrowed by a test only (N7),
and stays narrowed only up to the next call and the next assignment, in the order the program
runs them: a test directly followed by the use is fine, and a second use after a call takes a
variable of its own. A call is every expression that runs code the function does not see: a call
of a function or a method, a constructor, an interpolation or `print` of a class instance (its
`Text()`, A5), and `==` on class instances that name their fields (K5). A declaration is not an
assignment; an assignment into any path is one. Reading a field, `.Bytes` and `.Count` on a
value, and `==` on values are none of them. *(this list is proposed)* `print` is a call like any
other. C# keeps such a path narrowed across the call, which its analysis can afford because it
only warns; here an unchecked use is an error (N2), so the narrowing ends where the promise would
end.

Case: [null/narrowed-field.kz](../corpus/null/narrowed-field.kz)
```kurz
class Account(mut string? Email)

a = Account("a@example.com")
if a.Email != null {
    print(a.Email.Bytes.Count)
}
```

Case: [null/narrowed-field-after-call.kz](../corpus/null/narrowed-field-after-call.kz)
```kurz
class Account(mut string? Email)

void Clear(Account a) {
    a.Email = null
}

a = Account("a@example.com")
if a.Email != null {
    print(a.Email.Bytes.Count)
    Clear(a)
    print(a.Email.Bytes.Count)
}
```

Case: [memory/weak-narrowed.kz](../corpus/memory/weak-narrowed.kz)
```kurz
class Owner(string Name)
class Pet(weak Owner? Keeper)

o = Owner("Ann")
p = Pet(o)
if p.Keeper != null {
    print(p.Keeper.Name)
}
```
