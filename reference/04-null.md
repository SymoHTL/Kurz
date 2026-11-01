# 4. Null

### N1 (decided, §4) `T?`

`null` exists. `T?` is short for `T | null`: a value of that type is a `T` or `null`. Every type has
its nullable form, a union's and a function type's among them: `(int | string)?` is
`int | string | null`, and `((int) => int)?` holds a function value or `null`, as Rust's `Option` takes
every type and a C# delegate is nullable (§4, the owner, 2026-10-30, "Nullable form, as Option").
`void` and a pointer, which takes `null` itself (R10), have no nullable form, and a build says
`not supported` to both *(assumed, §4: the two, readings of that answer)*; what a nullable type written as a
case of a union is, `int? | string`, stands open (§14): nested, as Rust's `Result<Option<i32>, String>`,
flattened to `int | null | string`, or an error; the build says `not supported` to it.

An arm of a `match` may name `null`, which matches the `null` of a `T?` value, as C#'s `case null`
and Rust's `None =>` do (C9; §4, the owner, 2026-10-30, "Allow, as C#"), and a `match` over a `T?`
covers `null` by that arm or by `else` (O8). An arm that no `null` reaches, one of a type, one
of a literal, or an `else` after the `null` arm, knows the variable or the path of fields the
`match` tests not null, as C#'s analysis reads a pattern (N3). *(assumed, §4: the coverage of
`null` by the arm or by `else`, and the arm no `null` reaches knowing the subject not null, readings
of the answer, which named the arm itself)* A `null` arm over a subject whose type has no `null` and is
no number type, a `string` or a union without `null`, builds as an arm no value reaches, with no diagnostic, and covers
nothing, as C# compiles `case null` on a `string` (§4, the owner, 2026-10-30, "Allow, as C#", a
second answer, against `type-mismatch`, as Rust refuses `None =>` on an `i32`, and against
`unreachable-arm`), and so after an arm that covers the subject's type, `string t` over a `string`, where
C4's `unreachable-arm` for an arm after the arm of its type does not reach it, since `null` is no `string`
*(assumed, §4, §5: C4 read beside the answer, which named the arm itself)*. Over a subject of a type parameter, which a
nullable type can fill (N11), N6 makes it `type-mismatch`, as `null` where a `T` is required is, while §14
holds whether it builds: as C# builds `case null` on a `T` without a constraint, or `type-mismatch`.
Whether it builds over a number type, an `int`, whose `case null` C# refuses (CS0037), stands open (§14),
since the answer's "as C#" and the `i32` of the option it set aside point two ways there: it builds as
over a `string`, an arm no value reaches, or it is refused, as C# refuses it.
An arm after `else`, a `null` arm among them, is `syntax` (C9). A second `null` arm is
`unreachable-arm`, as a second arm of one literal is (C4), over a `T?` and over a subject with no `null`
alike. *(assumed, §4: the second `null` arm, which the answers named not)*

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

Case: [null/match-null-arm.kz](../corpus/null/match-null-arm.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

void Show(int id) {
    match Email(id) {
        null => print("none")
        string s => print(s.Bytes.Count)
    }
}

Show(1)
Show(2)
```

Case: [null/match-null-arm-not-nullable.kz](../corpus/null/match-null-arm-not-nullable.kz)
```kurz
void Show(string s) {
    match s {
        null => print("none")
        string t => print(t.Bytes.Count)
    }
}

Show("ab")
Show("abc")
```

Case: [null/match-null-arm-twice.kz](../corpus/null/match-null-arm-twice.kz)
```kurz
void Show(string s) {
    match s {
        null => print("none")
        null => print("again")
        string t => print(t.Bytes.Count)
    }
}

Show("ab")
```

Case: [null/nullable-union.kz](../corpus/null/nullable-union.kz)
```kurz
// `(int | string)?` is `int | string | null` (N1): a `match` takes its `null` in an arm, and a test leaves the union, whose `match`
// needs no `null` arm (N3)
(int | string)? Find(int id) {
    if id == 1 {
        return 7
    }
    if id == 2 {
        return "text"
    }
    return null
}

void Show(int id) {
    match Find(id) {
        null => print("none")
        int n => print(n)
        string s => print(s)
    }
}

Show(1)
Show(2)
Show(3)
x = Find(1)
if x != null {
    match x {
        int n => print(n + 1)
        string s => print(s)
    }
}
print(Find(3) == null)
print(Find(2) == null)
```

Case: [null/nullable-function.kz](../corpus/null/nullable-function.kz)
```kurz
// `((int) => int)?` holds a function value or `null` (N1): a call after a test (N3), and `??` with a lambda for its `null` (N4)
((int) => int)? Pick(bool twice) {
    if twice {
        return x => x * 2
    }
    return null
}

f = Pick(true)
if f != null {
    print(f(4))
}
print(Pick(false) == null)
g = Pick(false) ?? (x => x + 1)
print(g(4))
```

Case: [values/map-union-value.kz](../corpus/values/map-union-value.kz)
```kurz
// a map's value of a union is read as the union's nullable form (M9, N1): the value where the key has an entry, `null` where it has none
mut m = Map<string, int | string>()
m["a"] = 1
match m["a"] {
    null => print("none")
    int n => print(n)
    string s => print(s)
}
match m["b"] {
    null => print("none")
    int n => print(n)
    string s => print(s)
}
```

Case: [values/map-function-value.kz](../corpus/values/map-function-value.kz)
```kurz
// a map's value of a function type is read as that type's nullable form (M9, N1): the function where the key has an entry, `null`
// where it has none, and the call after the test reads `f` as the function (N3)
mut ops = Map<string, (int) => int>()
ops["double"] = x => x * 2
f = ops["double"]
if f != null {
    print(f(5))
}
print(ops["none"] == null)
```

### N2 (decided, §4) No use without a check

Using a value of type `T?` where a `T` is needed, or reaching a member through it, without a
check is the compile error `nullable-unchecked`. The error writes a second line below it, indented
by two spaces, ``help: check it with `!= null`, or give a value for null with `??` `` (§4, the
owner, 2026-10-30); the record marks the two spaces as *(assumed)*. A case pins an error's id and
line (E2), so the second line has no case here.

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

Case: [null/nullable-function-unchecked.kz](../corpus/null/nullable-function-unchecked.kz)
```kurz
// a call of a nullable function value without a check uses it where a function value is needed (N2)
((int) => int)? Pick(bool twice) {
    if twice {
        return x => x * 2
    }
    return null
}

f = Pick(true)
print(f(4))
```

### N3 (decided, §4) What a check is

What counts as a check follows the flow of the code, as the nullable analysis of C# does: what
that analysis proves about a variable holds here, with the one difference this chapter states
(N9). The record marks the version as *(assumed)*: C# 12. The list that follows shows the forms
the corpus covers; it is not the rule, and a form C# narrows that it leaves out, such as the block
of `if name != null && other { }` or the code after `if name != null { } else { return }`,
narrows here too. A variable of the type
`T?` is known not null, and stands where a `T` is needed (N4 and N5 say how `??`, `?.` and `?[`
read it):

- in the block of `if name != null { }`, and in the `else` block of `if name == null { }`;
- after an `if name == null { }` whose block always leaves, for the rest of the block that holds
  the `if`;
- in the body of `while name != null { }`;
- on the right of `name != null && ...` and of `name == null || ...`;
- in the block of `if name == value { }` and of `if value == name { }`, where the type of `value`
  is not nullable or `value` is a variable or a path known not null at that point, and in the
  `else` block of `if name != value { }` and of `if value != name { }` (N10);
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

for n in 0..2 {
    email = Email(n)
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
    print(u?.Email.Bytes.Count ?? 0)
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

Case: [null/equality-narrows.kz](../corpus/null/equality-narrows.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

email = Email(1)
if email == "a@example.com" {
    print(email.Bytes.Count)
}
if "a@example.com" == email {
    print(email.Bytes.Count)
}
other = Email(1)
if other != null {
    if email == other {
        print(email.Bytes.Count)
    }
}
```

Case: [memory/c-function-type-nullable.kz](../corpus/memory/c-function-type-nullable.kz)
```kurz
// the nullable form of a C function type holds `null` where C may give none (R17), and a call through
// it is read where a test knows it not null, as any nullable value is (N3)
extern "C" long Twice(long x) {
    return x * 2
}

(extern "C" (long) => long)? none = null
print(none == null)
(extern "C" (long) => long)? f = Twice
raw {
    if f != null {
        print(f(4))
    }
}
```

Case: [null/nullable-union.kz](../corpus/null/nullable-union.kz)

Case: [null/nullable-function.kz](../corpus/null/nullable-function.kz)

Case: [values/map-function-value.kz](../corpus/values/map-function-value.kz)

### N4 (decided, §4) `??`

`a ?? b` is `a` when `a` is not null, and `b` otherwise, as in C#. `??` on a value whose type is
not nullable is `type-mismatch`, as Rust has no `unwrap_or` on an `i32` (§4, the owner,
2026-10-30). A variable or a path of fields that N3 or N7 shows not null is no such value: `??`
reads it as the nullable type it is declared with, as C# reads `??` on a variable its analysis
knows not null, so `??` on it is no `type-mismatch`, and N5's `?.` and `?[` read it the same way;
`memory/tree-value.kz` rests on that, with `root.Left?.Key ?? 0` after an assignment narrowed
`root.Left`. *(this reading is proposed: §4 makes the error of a value whose type is not nullable,
and a check makes a value known not null without changing the type it is declared with)*

Case: [null/coalesce.kz](../corpus/null/coalesce.kz)

Case: [null/coalesce-not-nullable.kz](../corpus/null/coalesce-not-nullable.kz)
```kurz
n = 5
print(n ?? 0)
```

Case: [null/nullable-function.kz](../corpus/null/nullable-function.kz)

### N5 (decided, §4) `?.`

`a?.Member` is null when `a` is null and the member otherwise, as in C#. `a?[i]` is null when
the list `a` is null, without running the index, and its item at `i` otherwise, as C#'s `?[]`
(§4, the owner, 2026-10-30). The rest of the chain is skipped with either. `?.` on a value whose
type is not nullable is `type-mismatch` (§4, the owner, 2026-10-30), and so is `?[` *(the record
marks this as assumed: the answer on `?[` did not name it)*. A variable or a path that N3 or N7
shows not null is read as the nullable type it is declared with, so `?.` and `?[` on it are no
`type-mismatch`, as C# reads them, by the reading N4 proposes; `memory/tree-value.kz` reads
`root.Left?.Key` after an assignment narrowed `root.Left`.

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

Case: [null/conditional-index.kz](../corpus/null/conditional-index.kz)
```kurz
List<int>? Scores(bool some) {
    if some {
        mut xs = List<int>()
        xs.Add(1)
        xs.Add(2)
        return xs
    }
    return null
}

int At(int i) {
    print("index {i}")
    return i
}

print(Scores(true)?[At(1)])
print(Scores(false)?[At(1)])
```

Case: [null/conditional-access-not-nullable.kz](../corpus/null/conditional-access-not-nullable.kz)
```kurz
data User(string Name)

u = User("Ann")
print(u?.Name)
```

### N6 (assumed, §4) `null` needs a nullable type

`null` is a value of nullable types only, and of pointer types (R10, §3, the owner, 2026-10-21).
Giving it to another type without `?` is the compile error
`type-mismatch`, and never N2's `nullable-unchecked`: N2 is about a value of a type `T?`, and the
literal `null` has no such type until a nullable type takes it.

Case: [null/not-nullable.kz](../corpus/null/not-nullable.kz)
```kurz
string name = null
print(name)
```

Case: [values/list-literal-null-item.kz](../corpus/values/list-literal-null-item.kz)
```kurz
x = [1, null]
print(x.Count)
```

Case: [values/list-literal-nullable-items.kz](../corpus/values/list-literal-nullable-items.kz)
```kurz
List<int?> maybe = [1, null]
print(maybe.Count)
print(maybe)
```

### N7 (decided, §4) An assignment to a narrowed variable

An assignment replaces what is known about a variable, as in C#. Assigning a value whose type is
not nullable narrows the variable from that line on, and past the end of the block that holds the
assignment where every way that reaches the code after that block keeps it: where the branches of
an `if` or the paths around a loop meet, the variable is nullable when any of them leaves it
nullable, the join C# makes. So after `if f == null { f = "x" }` the variable `f` is not null,
since the way through the block assigned it and the way around the block found it not null, while
`if c { e = "x" }` narrows an `e` that may be null inside the block and not after it, since the
way around the block leaves it as it was (§4, the owner, 2026-10-30, "Yes, as C#"). A `weak`
reference (R3) is never narrowed by an assignment: nothing need hold its new target, which can be
freed before the next line. Assigning a nullable value to a narrowed `mut` variable is allowed
and makes it nullable again from that line on, so a use after it needs a new check (N2). That is what lets a loop walk a chain: `node = node.Next` assigns a nullable
value to the variable the loop has just checked. The record marks as *(assumed)* what else
counts as such an assignment: handing the variable to a `mut` parameter (M7), as `ref` does in
C#, and a call of a `mut` method (M5) on a value that a narrowed path runs through. The cases
assign.

Case: [null/while-narrows.kz](../corpus/null/while-narrows.kz)

Case: [null/assignment-narrows.kz](../corpus/null/assignment-narrows.kz)

Case: [null/assignment-join.kz](../corpus/null/assignment-join.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

void Show(int id) {
    mut f = Email(id)
    if f == null {
        f = "x"
    }
    print(f.Bytes.Count)
}

Show(2)
Show(1)
```

Case: [null/assignment-join-around.kz](../corpus/null/assignment-join-around.kz)
```kurz
string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

void Show(int id, bool c) {
    mut f = Email(id)
    if c {
        f = "x"
    }
    print(f.Bytes.Count)
}

Show(2, false)
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

N3 narrows a path of fields. A path whose every step is a field of a `data` value, or a field that
is neither `mut` nor `weak`, changes only through what the function itself does to it (N7). Two
kinds of path can change behind its back: one through a `mut` field of a class instance, which a
call can assign through another reference (K2), and one through a `weak` reference, whose target a
call or an assignment can free (R3). Such a path is narrowed by a test (N7); a path with no `weak` reference in it, and only such a path,
also by an assignment of a value whose type has no `null`, `acc.Email = "x"`, as C#'s flow analysis
reads it (the owner, 2026-10-09, against narrowing by a test only, under which a line that just set
the field cannot use it); a path with a `weak` reference anywhere in it, the reference itself or a
`weak mut` field assigned as `node.Parent = Node()`, is narrowed by a test only (R3) (the owner,
2026-10-10, against narrowing such a field by the assignment as a `mut` field is, after which
nothing strong need hold the target). The narrowing lasts only up to the
next call and the next assignment, in the order the program runs them: a test directly followed by
the use is fine, and a second use after a call takes a variable of its own. A path with a `weak`
reference in it stops being narrowed at two more ends, since a count let go of there runs code the
function does not see and can free the target: the end of a statement or of a condition in which a
call ran, and the end of a block that declares a variable; a use after such an end takes a variable
of its own too, so a test after a call in the same condition does not narrow the block (§4, the
owner, 2026-11-01, "Those ends stop narrowing", against a narrowed read that checks the target and
ends the program with an error at run time where it was freed since the test). A call is every
expression that runs code the function does not see: a call of a function or a method, a
constructor, an interpolation or `print` of a class instance (its `Text()`, A5), and `==` on class
instances that name their fields (K5). A declaration is not an assignment; an assignment into any
path is one, and one that assigns a value whose type has no `null` narrows a path with no `weak`
reference in it again, as this rule says above with the owner's choice of 2026-10-09, while the case
`null/narrowed-field-after-assignment.kz` assigns a value that may be null. Reading a field,
`.Bytes` and `.Count` on a value, and `==` on values are none of them. *(this list is proposed)*
`print` is a call like any other. C# keeps such a path narrowed across the call (the .NET SDK
10.0.204, 2026-10-08: no warning for a use after a call that sets the field to null, CS8602 for an
unchecked use), which its analysis can afford because it only warns; here an unchecked use is an
error (N2), so the narrowing ends where the promise would end.

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

acc = Account("a@example.com")
if acc.Email != null {
    print(acc.Email.Bytes.Count)
    Clear(acc)
    print(acc.Email.Bytes.Count)
}
```

Case: [null/narrowed-field-after-unrelated-call.kz](../corpus/null/narrowed-field-after-unrelated-call.kz)
```kurz
class Account(mut string? Email)

void Log() {
    print("log")
}

acc = Account("a")
if acc.Email != null {
    n = acc.Email.Bytes.Count
    Log()
    m = acc.Email.Bytes.Count
    print(n + m)
}
```

Case: [null/narrowed-field-after-assignment.kz](../corpus/null/narrowed-field-after-assignment.kz)
```kurz
class Account(mut string? Email)

string? other = null
mut acc = Account("a")
if acc.Email != null {
    n = acc.Email.Bytes.Count
    acc.Email = other
    m = acc.Email.Bytes.Count
    print(n + m)
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

Case: [memory/weak-narrowed-after-call-in-condition.kz](../corpus/memory/weak-narrowed-after-call-in-condition.kz)
```kurz
class Owner(string Name)
class Pet(mut weak Owner? Keeper)

bool Keep(Pet pet, Owner o) {
    pet.Keeper = o
    return true
}

p = Pet(null)
if Keep(p, Owner("Ann")) && p.Keeper != null {
    print(p.Keeper.Name)
}
```

Case: [null/narrowed-field-by-assignment.kz](../corpus/null/narrowed-field-by-assignment.kz)
```kurz
class Account(mut string? Email)

mut acc = Account(null)
acc.Email = "x"
print(acc.Email.Bytes.Count)
```

### N10 (decided, §4) `==` and `!=` with a nullable value

`==` and `!=` with a nullable value on either side are lifted, as C#'s are: `null` equals `null`
alone, two values compare as their types do, the `null` taken off each side first (D3, T18), so
`(int | string)? a == 1` reads an `int | string` beside an `int`, which D3 refuses, and `b == 1` is
`false` for an `int? b` that is null. `<`, `<=`, `>` and `>=` with a nullable value stay `nullable-unchecked` (N2). A test
`a == b` or `b == a`, with a nullable `a` and a `b` whose type is not nullable or that is a
variable or a path known not null at that point (N3), shows that `a` is not null where it is true,
and `a != b` or `b != a` where it is false, so `"x" == email` narrows `email` as `email == "x"`
does. That is C#'s analysis, which reads what is known of `b` at that point and not its declared
type; beside a `b` that may be null, the test shows nothing of `a`, since both can be null.

Case: [null/lifted-equality.kz](../corpus/null/lifted-equality.kz)
```kurz
int? Find(string text, string part) {
    if text == part {
        return 0
    }
    return null
}

a = Find("b", "b")
b = Find("b", "c")
print(a == 0)
print(b == 0)
print(b == null)
print(a == b)
print(b != 0)
print(b == Find("x", "y"))
```

Case: [null/order-not-lifted.kz](../corpus/null/order-not-lifted.kz)
```kurz
int? Find(string text, string part) {
    if text == part {
        return 0
    }
    return null
}

a = Find("b", "b")
print(a < 1)
```

Case: [null/equality-narrows.kz](../corpus/null/equality-narrows.kz)

### N11 (decided, §4) A nullable type parameter

`T?` of a type parameter `T` is the type that fills the parameter where that type is nullable
already, since `T??` is `T?`, as C# reads `T?` of a type parameter without a constraint that is
filled with a nullable type, `int?`: a missing value and a value that is `null` read alike. Where
the type that fills the parameter is not nullable, `T?` is that type's nullable type, so `First`
on a `List<int>` gives an `int?` and its `return null` gives `null`. C# from version 9 on differs
there: it reads `T?` of such a parameter as `T` where a value type fills it, so that `First<int>`
gives an `int`, and it refuses the `return null` of the generic function itself, whatever fills `T`
(CS0403, "Cannot convert null to type parameter 'T' because it could be a non-nullable value type";
checked 2026-10-30 by a build of `static T? First<T>(List<T> xs)` with the .NET SDK 10.0.402).

Case: [null/nullable-type-parameter.kz](../corpus/null/nullable-type-parameter.kz)
```kurz
T? First<T>(List<T> xs) {
    if xs.Count == 0 {
        return null
    }
    return xs[0]
}

mut some = List<int?>()
some.Add(7)
none = List<int?>()
mut nulls = List<int?>()
nulls.Add(null)
print(First(some))
print(First(none))
print(First(nulls))
ints = List<int>()
print(First(ints))
```
