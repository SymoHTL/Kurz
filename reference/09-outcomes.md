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

The first case is the success case by its place, so no case becomes a success it was not: D9 says
when a union takes the value of another, and O3 where a case that leaves a function through a call
is listed; each records the owner's choice of 2026-10-21, with what it was chosen against and its
cost.

Case: [data/union-order.kz](../corpus/data/union-order.kz)
```kurz
data A(int X)
data B(int Y)

A | B x = B(2)
B | A y = x
match y {
    B b => print("b {b.Y}")
    A a => print("a {a.X}")
}
```

Case: [outcomes/case-as-success.kz](../corpus/outcomes/case-as-success.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

NotFound Lost(int id) {
    value = Find(id)
    print(value)
    return NotFound
}

print(Lost(1))
```

Case: [data/union-case-widens.kz](../corpus/data/union-case-widens.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

long | NotFound r = Find(1)
match r {
    long n => print(n)
    NotFound => print("none")
}
```

Case: [data/union-case-derives.kz](../corpus/data/union-case-derives.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

Admin | NotFound a = Admin("a", 2)
User | NotFound u = a
match u {
    User x => print("user {x.Name}")
    NotFound => print("none")
}
```

Case: [data/union-case-after-the-first.kz](../corpus/data/union-case-after-the-first.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

// the Admin is the case Admin, and no other fit is looked for (D9)
User | Admin a = Admin("a", 1)
User | NotFound u = a
match u {
    User x => print(x.Name)
    NotFound => print("none")
}
```

Case: [outcomes/success-case-takes-no-part.kz](../corpus/outcomes/success-case-takes-no-part.kz)
```kurz
bool | short Check(int id) {
    if id == 1 {
        return true
    }
    return short(id)
}

// the short that leaves Check goes to Load's int case, which it widens into, and not to Load's success (O3)
short | int Load(int id) {
    ok = Check(id)
    print(ok)
    return int(0)
}

match Load(2) {
    short s => print("short {s}")
    int i => print("int {i}")
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

It is listed as a case after the first one, which is the success case (O1): a return type that is no
union lists none, so `NotFound Lost()` whose body calls an `int | NotFound Find()` and lets the
`NotFound` leave is `unlisted-case`, as Rust's `?` fills only the `Err` side. The owner chose this
on 2026-10-21, against a case leaving as the success of the calling function, which a return type
that is the case itself, or that names it first, would let it do; the cost is that such a function
needs a `match` on the call to return the case. A case after the first lists a case that is its type
and one that widens into it (T8, T23). The owner chose this on 2026-10-21 with the answer on the
success type below, whose propagated `short` widens into `int`, against the options and at the cost
that answer names. It also lists a case that derives from it (D6) *(assumed: the derivation, which
the answers did not name)*.

Case: [outcomes/case-as-success.kz](../corpus/outcomes/case-as-success.kz)

A propagated case that would go to two of the cases after the first, a `byte` under the return type
`string | short | int`, is `unlisted-case`, and a `match` on the call picks the case. The owner
chose this on 2026-10-21, against the nearest case, the most derived base or the narrowest type, as
C#'s better-conversion rule picks it, under which propagation would answer differently from widening
(T8, D9); the cost is that such code needs that `match`. A propagated case that is one of the cases
after the first goes there, and no other fit is looked for, so a propagated `short` under
`string | short | int` is listed by its `short`. The owner chose this on 2026-10-21, as for a value
(T8), against two fits being `unlisted-case` even beside an exact case, under which such a function
needs a `match` on the call; the cost is that adding a case to a return type can change without a
word which case an existing propagated case leaves as, as a propagated `short` leaves as the `int`
under `string | int` and as the `short` under `string | int | short`, while under the option turned
down that addition is a compile error. The owner kept this choice with that cost shown on
2026-10-21.

Case: [outcomes/case-fits-two-cases.kz](../corpus/outcomes/case-fits-two-cases.kz)
```kurz
string | byte Get(int id) {
    if id == 1 {
        return "one"
    }
    return byte(id)
}

string | short | int Load(int id) {
    got = Get(id)
    return got
}

match Load(1) {
    string s => print(s)
    short n => print(n)
    int i => print(i)
}
```

Case: [outcomes/case-is-its-case.kz](../corpus/outcomes/case-is-its-case.kz)
```kurz
string | short Get(int id) {
    if id == 1 {
        return "one"
    }
    return short(id)
}

string | short | int Load(int id) {
    got = Get(id)
    return got
}

match Load(2) {
    string s => print(s)
    short n => print("short {n}")
    int i => print("int {i}")
}
```

A propagated case is looked for among the cases after the first only, so in `short | int F()`
calling `bool | short G()` the `short` widens into `int` and leaves as `F`'s `int` case. The owner
chose this on 2026-10-21, against `unlisted-case`, with a `match` on the call picking the case;
Rust's `?` converts the error into the error type of the calling function and never looks at its
success type; the cost is that a propagated case that is exactly the success type leaves as another
case, by widening, without a word.

Case: [outcomes/success-case-takes-no-part.kz](../corpus/outcomes/success-case-takes-no-part.kz)

### O4 (decided, §5) Keeping all cases

A call is not unwrapped when it is the subject of a `match`, or when its result goes into a variable
with a written union type. A written union type holds every case the call can return: `int | NotFound
result = Find(id)` with a `Find` that can also return `Invalid` is `type-mismatch` (T1), as an
assignment in C# must fit its type; the other cases are kept by naming them, or handled by a `match`
(the owner, 2026-10-09, against a written type as a filter that lets the missing cases leave the
function as O2 says, under which a declaration ends the function on some paths without a word; the
cost is that a function with five cases forces a five-case type on every variable that keeps its
result). A written type that is the success case itself, `User?` for a call that returns
`User? | DbError`, names no other case and is unwrapped as O2 says; one that names any case beyond the
success case names them all (the owner, 2026-10-10, confirming the reading proposed after the review of round 13).
A written type the success case converts to, `long` for an `int` success or `User?` for a `User`
success, counts as the success case itself: the call unwraps and the value widens as in an assignment
(the owner, 2026-10-11, confirming the reading proposed after the review of round 14).
A call passed as an argument to a parameter whose written type is a union keeps its cases as such a
variable keeps them, so that `Show(long.Parse(s))` with `void Show(long | ParseError x)` passes the
whole union (§5, the owner, 2026-10-31, "Keep, like a variable"); so does a call passed to a
parameter of a constructor, which a field of a written union holds (§5, the owner, 2026-10-31, "Keep,
as any parameter"), and an item of a list literal whose written item type is a union (§5, the owner,
2026-10-31, "Keep, as the item type"). Of two functions of one name, one that takes the union and one
that takes the success case, such a call runs the one that takes the union, its exact fit (F13) (§5,
the owner, 2026-10-31, "The union one"); that pick is made among the functions that fit exactly, so
of `Show(int n)` and `Show(long | NotFound x)`, `Show(Find(1))` runs `Show(int n)` and the other cases
leave the caller (§5, the owner, 2026-10-31, "Exact fit first"). A parameter whose type is a type
parameter, `Same(Find(1))`, is no written union: the call unwraps, and the type
parameter is the success case (§5, the owner, 2026-10-31, "Unwrap: T = int"). A parameter whose
written union holds a type parameter, `void F<T>(T | NotFound x)`, keeps the cases where it names one
beyond the success, as a variable of that type, and `T` is inferred from the success (§5, the owner,
2026-10-31, "Keep, infer T = int"). A parameter whose union names some of the call's cases beyond the
success and not all is `type-mismatch`, as for a variable; a parameter of the success case or of a type it converts to
unwraps the call, a union that names no case of the call beyond the success, `int | string` for an
`int | NotFound` call, among them, at a parameter as at a variable; a lambda's and a function value's parameter and a named argument follow the same
rule; and a call inside an expression, `Show(Find(1) + 1)`, unwraps; a list literal takes its written item
type from the type that stands where it does, a declaration's, a parameter's or a result's (M11); an
exact fit of such a call (F13) is its whole union at a parameter that keeps the cases and its success
case at one that unwraps it, so that the two functions of "The union one" both fit exactly; two
functions that fit with a widening, neither exactly, are `ambiguous-call` (F13), both of them keeping
the cases or one keeping them and one unwrapping the call, `Show(long n)` beside
`Show(long | NotFound x)`; and a call that fits none of two or more functions of its name is
`argument-mismatch` (F13), and one that does not fit the one function of its name `type-mismatch`
(T1), each alone, with no `unlisted-case` for a case of a call among its arguments beside it (E1) *(assumed, §5: the
defaults of the change's product pass and the readings its reviews left)*. A parameter whose type is a union by a type argument written at the call,
`Same<int | NotFound>(Find(1))`, or by the type of the list a method is called on, `xs.Add(Find(1))`
on a `List<int | NotFound>`, is a written union and keeps the cases, so that a call unwraps at a type
parameter where the arguments infer it *(assumed, §5: a reading of the change)*.

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

Case: [outcomes/keep-partial-type.kz](../corpus/outcomes/keep-partial-type.kz)
```kurz
data NotFound

int | NotFound | Invalid Find(int id) {
    if id == 1 {
        return 10
    }
    if id == 2 {
        return Invalid
    }
    return NotFound
}

int | NotFound result = Find(1)
match result {
    int n => print(n)
    NotFound => print("none")
}
```

Case: [outcomes/keep-argument.kz](../corpus/outcomes/keep-argument.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(long | ParseError x) {
    match x {
        long n => print(n)
        ParseError e => print(e)
    }
}

void Report(int | NotFound found) {
    match found {
        int n => print(n)
        NotFound => print("none")
    }
}

// a parameter whose written type is a union takes the call's cases, as a variable of that type does
Show(long.Parse("42"))
Show(long.Parse("4x"))
Report(found: Find(1))
Report(Find(2))
```

Case: [outcomes/keep-argument-overload.kz](../corpus/outcomes/keep-argument-overload.kz)
```kurz
void Show(long n) {
    print("a long: {n}")
}

void Show(long | ParseError x) {
    print("the union")
}

// the union is the call's whole type, so the function that takes it is the exact fit
Show(long.Parse("1"))
Show(5L)
```

Case: [outcomes/keep-field.kz](../corpus/outcomes/keep-field.kz)
```kurz
data NotFound

data Box(int | NotFound Value)

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report(int | NotFound found) {
    match found {
        int n => print(n)
        NotFound => print("none")
    }
}

// a constructor's parameter is a field of a written union, which holds the call's cases
Report(Box(Find(1)).Value)
Report(Box(Find(2)).Value)
```

Case: [outcomes/keep-items.kz](../corpus/outcomes/keep-items.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report(int | NotFound found) {
    match found {
        int n => print(n)
        NotFound => print("none")
    }
}

// the list's written item type is a union, which each item of the literal holds
List<int | NotFound> items = [Find(1), Find(2)]
for f in items {
    Report(f)
}
```

Case: [outcomes/keep-argument-partial.kz](../corpus/outcomes/keep-argument-partial.kz)
```kurz
data NotFound

// `Invalid` is the case of an enum's `From` that every file sees without declaring it (D18)
int | NotFound | Invalid Find(int id) {
    if id == 1 {
        return 10
    }
    if id == 2 {
        return Invalid
    }
    return NotFound
}

void Report(int | NotFound found) {
    print(found)
}

// the parameter names `NotFound` and not `Invalid`, so it does not hold the call's cases
Report(Find(1))
```

Case: [outcomes/generic-argument-unwraps.kz](../corpus/outcomes/generic-argument-unwraps.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

T Same<T>(T x) => x

// a type parameter the call infers is no written union, so the call unwraps: `T` is `int`, and the match lists no `NotFound`
int | NotFound Doubled(int id) {
    mut doubled = 0
    match Same(Find(id)) {
        int n => doubled = n * 2
    }
    return doubled
}

match Doubled(1) {
    int n => print(n)
    NotFound => print("none")
}
match Doubled(2) {
    int n => print(n)
    NotFound => print("none")
}
```

Case: [outcomes/union-without-the-cases-unwraps.kz](../corpus/outcomes/union-without-the-cases-unwraps.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report(int | string value) {
    match value {
        int n => print(n)
        string s => print(s)
    }
}

// `int | string` names no case of the call beyond its success, so the call unwraps and `NotFound` leaves `Shown`
int | NotFound Shown(int id) {
    Report(Find(id))
    int | string kept = Find(id)
    Report(kept)
    return 0
}

match Shown(1) {
    int n => print("done")
    NotFound => print("none")
}
match Shown(2) {
    int n => print("done")
    NotFound => print("none")
}
```

Case: [outcomes/keep-argument-exact-fit-first.kz](../corpus/outcomes/keep-argument-exact-fit-first.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(int n) {
    print("the success")
}

void Show(long | NotFound x) {
    print("the union")
}

// `Show(int)` fits the success exactly, and the union needs a widening: the exact fit runs, and `NotFound` leaves `Shown`
int | NotFound Shown(int id) {
    Show(Find(id))
    return 0
}

match Shown(1) {
    int n => print("done")
    NotFound => print("none")
}
match Shown(2) {
    int n => print("done")
    NotFound => print("none")
}
```

Case: [outcomes/keep-argument-ambiguous.kz](../corpus/outcomes/keep-argument-ambiguous.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(long | NotFound x) {
    print("long")
}

void Show(double | NotFound x) {
    print("double")
}

// both functions keep the cases with a widening, and neither fits exactly
Show(Find(1))
```

Case: [outcomes/keep-argument-ambiguous-widening.kz](../corpus/outcomes/keep-argument-ambiguous-widening.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Show(long n) {
    print("the success")
}

void Show(long | NotFound x) {
    print("the union")
}

// one function takes the success with a widening and one keeps the cases with a widening: neither fits exactly
Show(Find(1))
```

Case: [outcomes/keep-argument-generic-union.kz](../corpus/outcomes/keep-argument-generic-union.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report<T>(T | NotFound found) {
    match found {
        NotFound => print("none")
        else => print("found")
    }
}

// `T | NotFound` names a case of the call beyond its success, so it keeps the cases, and `T` is `int`
Report(Find(1))
Report(Find(2))
```

Case: [outcomes/keep-items-of-an-argument.kz](../corpus/outcomes/keep-items-of-an-argument.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

void Report(List<int | NotFound> items) {
    for f in items {
        match f {
            int n => print(n)
            NotFound => print("none")
        }
    }
}

// the literal takes the parameter's item type, a union, which each item holds
Report([Find(1), Find(2)])
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
On AVR an exception in any actor ends the program, since nothing there walks the actor's frames
(§6, the owner, 2026-10-21).
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
`Reason`, which is this reference's reading of the record's *(assumed)* sentence that the runtime adds
them to what was thrown: they travel with the crash, not inside the thrown value; a
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

### O11 (decided, §2, §6) A child's stack and the process's heap

An actor that exhausts its stack on Linux or on Windows dies as it does for any exception (O6), with a
`Reason` of the
runtime's `data` type (O7); its parent lives under `on crash restart` and `on crash stop`, and crashes
too under `on crash escalate`, as for any crash (O6), while at the top level, the root actor, the
process exits with the exit code 1 (O6); on WebAssembly an exhausted stack ends the program, as the
engine's trap does (§2, the owner, 2026-10-21), a trap that the stack's place before the static data
makes certain (§2, an assumption there), with no error line of the program's, since no code of it runs
after a trap (§2, from the WebAssembly specification it checks), and with the exit code each engine
gives of its own, which that specification does not define; on the ESP32 models a watchpoint the runtime sets at each switch catches an overflow at
the first write past the stack's end, and on AVR nothing checks the stack, so that an overflow there
writes over memory unnoticed (§6, the owner, 2026-10-21), and a caught overflow on the ESP32 models
ends the program as ESP-IDF's panic handler ends it *(assumed, as §6 marks it)*. On Linux and on Windows the exception is
`stack-overflow`, at the line of the call that did not fit (the owner, 2026-10-10, confirming the
reference's reading, on which the case stands), read from the line tables for the call that did
not fit (§6), the innermost frame outside the runtime and its call into the runtime where that one was
the runtime's (§6, the owner, 2026-10-21), the runtime being its primitives and not the allocator, the
scheduler or a collection, which are the standard library's Kurz code above them (§6, §3), so that the owner's words hold with that reading, in whichever source that frame's
function sits, a line of the standard library's source where that function was the library's, whose
file the error line names (§6, decided there; the owner, 2026-10-21) and whose function the trace's
first line names (§6), so a case pins a line of the
program only where that frame was the program's, and every case of the corpus that pins a stack
overflow pins a call of the program's own, with a recursion that calls nothing of the standard
library and nothing that reaches the runtime's primitives or the allocator's entry, over values that
allocate nothing, as the case below does over an `int`, which the test build keeps as calls (§2, the
owner, 2026-10-29), so that the frame that does not fit is the program's in that build, its number is
the program's line and the runner compares it as E3 says; the corpus holds no case whose overflow
lands in the standard library's source *(assumed: the name of a library file, which the naming of the
standard library gives (§14), and the condition on the cases, as §6 marks it; the line's reading
from the tables is decided, as below)*; in a release build built without its line tables
(§2, the answer decided there, its reading an assumption) the error
line names no line and is `error: stack-overflow (no line tables: build with --lines)`, unless a note below comes first, and the
`Crashed` value's place holds the line 0 (§6, the note's words decided there, the owner, 2026-10-29, the line 0 an assumption there), since the line of a
stack overflow comes from those tables (§6, decided there,
with the assumptions its markers name; the owner, 2026-10-20), and no case pins that, since a case's
header names a line (E3). In either build, where the stack could not be walked, the file that
holds the tables could not be read or the raising frame has no entry in them, the error line
carries in place of the line the note that the run-time paragraph of chapter 12 gives for that
case, the first the crash path meets where more than one holds, and the `Crashed` value's place
holds the line 0 (§6, an assumption there as to the notes, their order and the line 0).
An allocation that fails ends the whole process with the exit code 1 and the line
`error: allocation-failed (<n> bytes)` on standard error, with the size asked for, and no place and no trace (§6, the owner, 2026-10-23,
as Rust's hook for an allocation error names the size), because the memory the actors' heaps are carved from is the process's, and no actor can run on (the owner, 2026-10-09, against
both ending the process, under which one runaway recursion in a worker takes the server down, and
against an allocation failure as a `Reason`, which would need a reserve to build the `Crashed` value
when nothing can be allocated; the cost is a guard page per actor stack and a program that cannot recover from memory pressure; the
stack probe in every frame larger than that page, so that no frame steps past it, and the signal stack
per thread for the handler that turns the fault into the exception the owner added to the cost on
2026-10-10). On Linux and on Windows, a stack exhausted inside the allocator or the scheduler ends the
process the same way, with the exit code 1 and the error line, as an overflow with no actor does, which the handler
tells by a count per thread that the package's entry raises and its exit lowers, and that the switch
saves and clears on its way to an actor and puts back on its way back, so that a fault in an actor's code
counts as none inside the package (§6, whose answer named the saving at each switch), and the error line
names the line of the innermost frame outside the runtime, as above, the allocator's or the
scheduler's own where the fault is there (§6, the owner, 2026-10-21), and no case pins that, since a case's recursion reaches neither (above). The case runs at the
top level, which is the root actor, so the
exception ends the program (O6); the allocation failure has no case, since no case can ask for
memory that does not exist.

Case: [outcomes/stack-overflow.kz](../corpus/outcomes/stack-overflow.kz)
```kurz
int Down(int n) => Down(n) + 1

print(Down(0))
```
