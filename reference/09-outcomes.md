# 9. Outcomes and errors

Every function declares every outcome that can happen under normal use.

### O1 (decided, §5) The return type is a union, and its first case is success

A function that can end in more than one way returns a union. The first case is the success
case. A function returns a case by returning a value of it.

Case: [outcomes/unwrap.kz](../corpus/outcomes/unwrap.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

int | NotFound Twice(int id) {
    value = Find(id)
    return value * 2
}

match Twice(1) {
    int n => print(n)
    NotFound => print("none")
}
match Twice(2) {
    int n => print(n)
    NotFound => print("none")
}
```

### O2 (decided, §5) The unwrap rule

Calling such a function yields the success value directly. Every other case leaves the calling
function at that point and becomes its result.

Case: [outcomes/unwrap.kz](../corpus/outcomes/unwrap.kz)

### O3 (decided, §5) Honest signatures

A case that can leave a function this way has to be listed in that function's own return type.
Otherwise the call is the compile error `unlisted-case`.

Case: [outcomes/unlisted-case.kz](../corpus/outcomes/unlisted-case.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

int Twice(int id) {
    value = Find(id)
    return value * 2
}

print(Twice(1))
```

### O4 (decided, §5) Keeping all cases

A call is not unwrapped when it is the subject of a `match`, or when its result goes into a
variable with a written union type.

Case: [outcomes/match-call.kz](../corpus/outcomes/match-call.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

match Find(2) {
    int n => print(n)
    NotFound => print("none")
}
```

Case: [outcomes/keep-explicit-type.kz](../corpus/outcomes/keep-explicit-type.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

int | NotFound result = Find(1)
match result {
    int n => print(n)
    NotFound => print("none")
}
```

### O5 (decided, §5) Postfix `else`

`call else { Case => ... }` handles the listed cases at the call. A case that is not listed
still propagates (O2, O3). An arm either recovers with a value, which takes the place of the
success value; or returns another case from the calling function; or throws. When the success
case is `void` (O10) there is no value to recover with, and an arm is a statement, or a block of
statements between braces, after which the program goes on after the call; `print("none")` in
O10's case is such an arm. A block of statements is allowed in every arm; its last statement is
what the arm does. *(proposed: the record shows one statement per arm)*

Case: [outcomes/else-recover.kz](../corpus/outcomes/else-recover.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

int OrZero(int id) {
    value = Find(id) else {
        NotFound => 0
    }
    return value
}

print(OrZero(1))
print(OrZero(2))
```

Case: [outcomes/else-transform.kz](../corpus/outcomes/else-transform.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

data Missing(int Id)

int | Missing Need(int id) {
    value = Find(id) else {
        NotFound => return Missing(id)
    }
    return value
}

match Need(2) {
    int n => print(n)
    Missing m => print("missing {m.Id}")
}
```

Case: [outcomes/else-partial.kz](../corpus/outcomes/else-partial.kz)
```kurz
data NotFound
data Invalid

int | NotFound | Invalid Find(int id) {
    if id < 0 {
        return Invalid
    }
    if id == 1 {
        return 10
    }
    return NotFound
}

int | Invalid OrZero(int id) {
    value = Find(id) else {
        NotFound => 0
    }
    return value
}

match OrZero(2) {
    int n => print(n)
    Invalid => print("invalid")
}
match OrZero(-1) {
    int n => print(n)
    Invalid => print("invalid")
}
```

Case: [outcomes/else-throw.kz](../corpus/outcomes/else-throw.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

print("before")
value = Find(2) else {
    NotFound => throw
}
print(value)
```

### O6 (decided, §5, §6) Exceptions

An exception is for a situation the function cannot recover from. There is no `catch`. An
exception ends the actor it happens in; top-level code is the root actor, so an exception there
ends the program with the exit code 1 (the owner, 2026-10-04, against a distinct code such as 70).
What was printed before stays printed. A case names the exception it expects (E3); a `throw` of
the program is the run-time error `thrown`.

Case: [outcomes/else-throw.kz](../corpus/outcomes/else-throw.kz)

### O7 (decided, §5) What `throw` takes

`throw` takes a value or a text: `throw ConfigMissing(path)`, or `throw "no config"` as the short
form. It is a statement and can stand wherever one can. As an arm of `else` it can also stand
alone (O5): a bare `throw` there throws the case value that reached the arm, so the arm
`NotFound => throw` throws the `NotFound`. A bare `throw` anywhere else is the compile error
`throw-needs-value`: there is no value it could mean. *(proposed)* When a supervisor reads what
was thrown as the `Reason` of a crashed child, its type is the union of everything the
child's code can throw, found over the whole program (the owner, 2026-10-03, against a fixed type
that carries the text and the place, and against limiting `throw` to `data` values and text). The
run-time errors of chapter 12 are values of one `data` type of the runtime, a member of every
such union in every build; the place and the chain id are fields of the `Crashed` case beside
`Reason` (the record marks as *(assumed)* that the runtime adds them to what was thrown); a
thrown class instance is moved out of the dying child's heap into the supervisor's
with everything it reaches, which is the cost, paid once per such crash (the owner, 2026-10-04,
against a compile error for a thrown class instance). The supervisors
themselves are outside this reference. The bare `throw` of an `else` arm is the `throw` that
raised (E3): it throws the case value that reached the arm, and nothing is raised a second time,
because nothing catches.

Case: [outcomes/bare-throw.kz](../corpus/outcomes/bare-throw.kz)
```kurz
void Fail() {
    throw
}

Fail()
```

Case: [outcomes/throw-text.kz](../corpus/outcomes/throw-text.kz)
```kurz
void Check(int n) {
    if n < 0 {
        throw "negative"
    }
}

print("before")
Check(-1)
print("after")
```

Case: [outcomes/throw-value.kz](../corpus/outcomes/throw-value.kz)
```kurz
data ConfigMissing(string Path)

print("before")
throw ConfigMissing("app.json")
```

### O8 (decided, §5) `match` covers every case

A `match` on a union lists every case of the union, or ends in an `else` arm (C9). A missing case
is the compile error `match-not-exhaustive`, reported at the `match`. Without this, a new case in
a signature would pass silently through every `match` that was written before.

Case: [outcomes/match-not-exhaustive.kz](../corpus/outcomes/match-not-exhaustive.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

match Find(2) {
    int n => print(n)
}
```

### O9 (decided, §5) Top-level code has no caller

Top-level code has no signature to list a case in, so a call there has to handle every case that
is not success. Letting one propagate is the compile error `unlisted-case`.

Case: [outcomes/top-level-unlisted.kz](../corpus/outcomes/top-level-unlisted.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

value = Find(2)
print(value)
```

### O10 (decided, §5) A success case without a value

`void` can be the success case: `void | NotFound Remove(int id)`. The call is then a statement,
and its other cases propagate or are handled as those of any call (O2, O5).

Case: [outcomes/void-success.kz](../corpus/outcomes/void-success.kz)
```kurz
data NotFound

void | NotFound Remove(int id) {
    if id != 1 {
        return NotFound
    }
    print("removed")
}

Remove(1) else {
    NotFound => print("none")
}
Remove(2) else {
    NotFound => print("none")
}
```
