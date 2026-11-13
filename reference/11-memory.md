# 11. Memory and references

### R1 (decided, §3) No lifetimes, no collector

There are no lifetimes, no borrow annotations and no garbage collector. The compiler inserts
reference counts and removes most of them at compile time.

An instance whose last count ends lets go of the instances its fields hold through a work list,
not by a call for each, so a chain of any length is freed without exhausting the stack: a million
links are freed as two are (§3, the owner, 2026-11-01, "Iterative, never crashes", against a
release that calls itself for each nested instance, as Rust's drop glue does, under which a long
chain ends the program with `stack-overflow`). The list needs no memory of its own: an instance on
it is linked through its strong count, which counts nothing from then on, so the release allocates
nothing and cannot run out *(assumed, as §3 marks it)*. Its cost is a pause: an assignment that lets
go of the head of a million links frees all of them before it returns, where a region is freed in
one sweep only for the memory of a message (§13, "Region per message", the research verdict on
Goose's data stacks).

The end of an instance or of a block lets go of what its fields hold in the order of the fields, as
Rust drops a value's fields (§3, §4, the owner, 2026-11-04, issue 156, comment 6643376941), a derived
block's base's fields first (§4, the owner, 2026-11-05, issue 110, comment 6657251329, recorded as a
reading of that answer from D7), an instance of a derived class its base's fields first too, and a
value whose last count a field held ends after the value that held it, from the work list.
*(assumed, §3, §4: the end after its holder, a reading of the owner's answer on classes of
2026-11-01, "Iterative, never crashes", issue 110, comment 6560107950; a derived class's base's
fields first, a reading of the answer of 2026-11-05, given for a derived block, as K6 lays the
base's fields out first)* No case shows the
order: a release runs no code of the program between two fields, and a handle that closes at its
last count (S5) shows it only outside the program.

Case: [memory/long-chain-freed.kz](../corpus/memory/long-chain-freed.kz)
```kurz
class Link(int Value, Link? Next)

mut Link? head = null
for i in 0..<1000000 {
    head = Link(i, head)
}
print(head?.Value ?? -1)
head = null
print("freed")
```

### R2 (decided, §3) A possible reference cycle is an error

Judged by types, over the whole program: a class may not reach itself through strong fields when
one field on that path is `mut`. That is the compile error `reference-cycle`. A path without a
`mut` field is allowed, because immutable data cannot form a cycle. A field of function type
(F11) is a strong field whose type names no class, yet the lambda it holds can capture any
instance: R9 says how it counts.

Case: [memory/cycle-self.kz](../corpus/memory/cycle-self.kz)
```kurz
class Node(mut List<Node> Children)

n = Node(List<Node>())
print(n.Children.Count)
```

Case: [memory/cycle-two-types.kz](../corpus/memory/cycle-two-types.kz)
```kurz
class Customer(string Name, mut List<Order> Orders)
class Order(int Id, Customer Owner)

c = Customer("Ann", List<Order>())
print(c.Name)
```

Case: [memory/immutable-chain.kz](../corpus/memory/immutable-chain.kz)
```kurz
class Link(int Value, Link? Next)

a = Link(1, null)
b = Link(2, a)
print(b.Next?.Value ?? 0)
```

### R9 (decided, §3) Fields of function type in the cycle rule

A field of function type, `mut` or not, counts as a field that can reach every class whose
instance a lambda it counts captures (below), as R8 treats a field of interface type; R2's `mut`
field can be the function field or any other field on the path. With
`class Button(string Name, mut () => void OnClick)` and a `b.OnClick = () => print(b.Name)`
anywhere in the program, `Button` reaches itself, and the class is `reference-cycle`; with
`class Holder(mut Button? B)` and an immutable `() => void` field of `Button` whose lambda reads a
`Holder`, the ring runs through the immutable field and `Holder`'s `mut` one, and the first class
on the path is the error. The owner chose this on 2026-10-03, against a weak capture inside
stored lambdas, which would need a rule for what a gone capture reads as and a check at every
use, and against forbidding such a capture. *(assumed: that a field without `mut` counts, which
the second ring needs)* The field counts the lambdas whose type it can hold, and the lambdas a
function value such a lambda captures can be, every lambda counting where a type on the way names a
type parameter; so a button's `mut () => void` field is not reached by an `(int) => void` lambda
that reads the button, unless a `() => void` lambda captures that one (§3, the owner,
2026-11-04, #110, comment 6643368937, answering comment 6624472418). *(assumed, §3: the count is transitive,
the lambdas a function value of a counted lambda can be counted in turn until no lambda is added;
the types on the way are the lambda's own type, which, where that type names a type parameter,
makes the lambda count for every field, so that a `(T) => void` lambda that reads the button
reaches its `() => void` field, and the type of a value a counted lambda captures, or of a function
value such a value holds, which, where that type names a type parameter, makes every lambda of the
program count)* The cost
is not local: once a class has a field of function type, any lambda the field counts, anywhere in
the program, that captures an instance of that class, or of a class that holds one, makes it an
error, and the lambda can sit in another file. An event handler escapes the error only in a program
in which no lambda the button's field counts captures the button's class: through an interface
(R8), whose class must not hold the button, or by receiving the button as a parameter.

Case: [memory/cycle-through-function-field.kz](../corpus/memory/cycle-through-function-field.kz)
```kurz
class Button(string Name, mut () => void OnClick)

b = Button("ok", () => print("clicked"))
b.OnClick = () => print(b.Name)
b.OnClick()
```

Case: [memory/cycle-through-immutable-function-field.kz](../corpus/memory/cycle-through-immutable-function-field.kz)
```kurz
class Holder(mut Button? B)
class Button(string Name, () => void OnClick)

h = Holder(null)
b = Button("ok", () => print(h.B?.Name ?? ""))
h.B = b
b.OnClick()
```

Case: [memory/function-field-no-capture.kz](../corpus/memory/function-field-no-capture.kz)
```kurz
class Button(string Name, mut () => void OnClick)

b = Button("ok", () => print("clicked"))
b.OnClick()
```

Case: [memory/function-field-other-type.kz](../corpus/memory/function-field-other-type.kz)
```kurz
// a lambda whose type the field cannot hold, `(int) => void` beside a `() => void` field, closes no ring through it in this program,
// though it reads the button: the field does not count it by its type, and no lambda the field counts captures a function value that
// can be it (R9, the owner, 2026-11-04, #110, comment 6643368937); its own type names no type parameter, and no lambda the field
// counts captures a value whose type names one (R9, assumed)
class Button(string Name, mut () => void OnClick)

void Each(List<int> xs, (int) => void f) {
    for x in xs {
        f(x)
    }
}

b = Button("ok", () => print("clicked"))
Each([1], (int n) => print(b.Name))
b.OnClick()
```

Case: [memory/cycle-through-captured-function.kz](../corpus/memory/cycle-through-captured-function.kz)
```kurz
// a lambda of another type that reads the button, which a `() => void` lambda that the field can hold captures, closes the ring
// through the field as the handler that reads the button itself does (R9, the owner, 2026-11-04, #110, comment 6643368937)
class Button(string Name, mut () => void OnClick)

b = Button("ok", () => print("x"))
inner = (int n) => print(b.Name)
b.OnClick = () => inner(1)
b.OnClick()
```

Case: [memory/cycle-through-open-capture.kz](../corpus/memory/cycle-through-open-capture.kz)
```kurz
// a lambda the field can hold that captures a value of a type parameter holds whatever function a use gives that parameter, so a
// lambda of another type that reads the button closes the ring through the field (R9, assumed)
class Button(string Name, mut () => void OnClick)
class Panel(Button B)

void Keep<T>(T x) {
}

void Wire<T>(Button button, T x) {
    button.OnClick = () => Keep(x)
}

b = Button("ok", () => print("x"))
p = Panel(b)
f = (int n) => print(p.B.Name)
Wire(b, f)
b.OnClick()
```

Case: [memory/cycle-through-generic-lambda.kz](../corpus/memory/cycle-through-generic-lambda.kz)
```kurz
// a lambda whose type names a type parameter is a lambda of whatever type a use gives that parameter, so one that reads the button
// through a panel closes the ring through a field of another function type (R9, assumed)
class Button(string Name, mut (int) => void OnClick)
class Panel(Button B)

(T) => void Show<T>(Panel panel) => (T t) => print(panel.B.Name)

b = Button("ok", (int n) => print("x"))
p = Panel(b)
b.OnClick = Show<int>(p)
b.OnClick(1)
```

Case: [memory/cycle-through-type-parameter-lambda.kz](../corpus/memory/cycle-through-type-parameter-lambda.kz)
```kurz
// a lambda whose own type names a type parameter counts for a field of any function type: a `(T) => void` lambda that reads the
// button through a panel closes the ring through a `() => void` field (R9, assumed)
class Button(string Name, mut () => void OnClick)
class Panel(Button B)

(T) => void Show<T>(Panel panel) => (T t) => print(panel.B.Name)

b = Button("ok", () => print("x"))
p = Panel(b)
f = Show<int>(p)
f(1)
b.OnClick()
```

Case: [http/stream-cycle.kz](../corpus/http/stream-cycle.kz)
```kurz
// a function that writes a response's body and reads a class that holds a `mut` response closes a ring through the response's
// function, which the cycle rule refuses, while a handler that reads that class closes none, since the response's function cannot
// be a handler (R9, the owner, 2026-11-04, #110, comment 6643368937)
class App(mut HttpResponse Cached)

void Keep((HttpRequest) => HttpResponse handler) {
}

app = App(HttpResponse.Text(200, "x"))
Keep((HttpRequest r) => app.Cached)
streamed = HttpResponse.Stream(200, (HttpWriter w) => w.Write("{app.Cached.Status}"))
print(streamed.Status)
```

Case: [http/stream-handler-no-cycle.kz](../corpus/http/stream-handler-no-cycle.kz)
```kurz
// a handler that reads a class that holds a `mut` response closes no ring through the response's function, since no lambda that
// field counts captures `app`: a handler is no lambda of a type the field can hold (R9, the owner, 2026-11-04, #110, comment
// 6643368937), the function that writes the body reads nothing of the class, and neither lambda captures a function value, through
// which the field would count more (R9, the owner, the same comment), or a value whose type names a type parameter (R9, assumed)
class App(mut HttpResponse Cached)

void Keep((HttpRequest) => HttpResponse handler) {
}

app = App(HttpResponse.Text(200, "x"))
Keep((HttpRequest r) => app.Cached)
app.Cached = HttpResponse.Stream(201, (HttpWriter w) => w.Write("y"))
print(app.Cached.Status)
```

What a lambda captures is read as a field of its type. A `weak` reference it captures reaches no
class, as R3 says of a `weak` field, so a handler that reads its button through a `weak` reference
closes no ring (§3, the owner, 2026-11-02, #110, comment 6573113786, R9a, against counting it as
strong, which would close to a lambda the escape `weak` gives a field). A value of a type parameter
reaches the classes of the type argument every call of its function names or infers, and through a
generic caller's type parameter those of that caller's calls, as the copy of section 4 for each list
of type arguments holds them (R9b, against every class of the program, under which one such lambda
anywhere makes every ring through a field of function type an error). A method taken as a value
captures its instance, as the lambda `() => c.Get()` does and as a C# delegate keeps its target
(R9c, against capturing nothing, under which `b.OnClick = b.Show` would hold `b` in `b` with no ring
reported); a method as a value is `not supported` (K7, F11), so R9c has no case of its own.

Case: [memory/function-field-weak-capture.kz](../corpus/memory/function-field-weak-capture.kz)
```kurz
class Button(string Name, mut () => void OnClick)

b = Button("ok", () => print("x"))
weak Button? w = b
b.OnClick = () => print(w?.Name ?? "gone")
b.OnClick()
```

Case: [memory/cycle-through-type-argument.kz](../corpus/memory/cycle-through-type-argument.kz)
```kurz
class Button(string Name, mut () => void OnClick)

T Hook<T>(T x) {
    f = () => x
    return f()
}

b = Button("ok", () => print("x"))
b.OnClick()
print(Hook(b).Name)
```

### R3 (decided, §3) `weak`

A `weak` reference points at an instance without keeping it alive. It is nullable, and it is
null once its target has been freed. A field that is `weak` is not a strong field for R2. When a
target is freed follows from what holds it: a variable holds its instance until the end of the
block that declares the variable, a field as long as its owner lives, and a temporary until the
end of its statement, or of its condition in an `if` or a `while`, as N9 ends a narrowing there (§4); an instance is freed when the last of these ends. So in the case under N9
the `o` that is declared at the top level keeps `Ann` alive while `p.Keeper` is read. *(the
record marks the lifetimes as assumed: proposed on 2026-10-03, against releasing a variable after
its last use, which would free `Ann` before the print)*

A read of a `weak T?` place, a field, a variable or an item, gives a strong `T?`, which holds the
target as a temporary does, to the end of its statement or its condition, and longer where the read is stored:
`k = p.Keeper` keeps the target alive while `k` lives, and a `T?` parameter takes the read as it
is (§3, the owner, 2026-11-01, "A strong T?, held", against a read that stays `weak T?` until
`?.`, `??`, a test or a member makes a `T?` of it, as Rust's `Weak::upgrade` gives one).

`weak` may stand before an interface as before a class: a `weak I?` is the two words of a value of
the interface and holds the weak count of the instance's block, as Rust's `Weak<dyn Trait>` (§3,
the owner, 2026-11-02, #110, comment 6573113786, against a compile error). What it holds where the
value is an actor's, or one of a type an `impl` block declares, stands open (§14).

A `weak T?` type stands wherever a type stands: a field, a list's item, a variable, a parameter, a
return type, a map's value and a union's case (§3, the owner, 2026-11-01, "Anywhere a type
stands", against a field and a list's item alone). What a nullable type written as a case of a
union is stays open (§14), a `weak T?` among them.

A `weak T?` field of a `data` value compares by its target's identity (D3): two values are equal
where it reaches the same instance, and a freed target reads as null, so two freed ones are equal
and a freed one equals `null` (§3, the owner, 2026-11-02, #110, comment 6580085338). A `weak T?` is
no key of a `Map`, nor is a type that reaches one through what `==` compares by content, a field of
a `data` value, an item or a nullable, while a `weak` behind a class does not count: such a key type
is the compile error `weak-key` at the line that writes it (§3, the owner, 2026-11-02, #110, comments
6580085338 and 6580137405; the id and the line *(assumed, as §3 marks them)*). A clause of K8 that
names a `weak` field is `equal-by-weak` (K8).

Case: [memory/weak-back-pointer.kz](../corpus/memory/weak-back-pointer.kz)
```kurz
class Customer(string Name, mut List<Order> Orders)
class Order(int Id, weak Customer? Owner)

c = Customer("Ann", List<Order>())
c.Orders.Add(Order(1, c))
print(c.Orders.Count)
print(c.Orders[0].Owner?.Name ?? "gone")
```

Case: [memory/weak-becomes-null.kz](../corpus/memory/weak-becomes-null.kz)
```kurz
class Owner(string Name)
class Pet(weak Owner? Keeper)

Pet Adopt() {
    o = Owner("Ann")
    return Pet(o)
}

p = Adopt()
print(p.Keeper?.Name ?? "gone")
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

Case: [memory/weak-read-holds.kz](../corpus/memory/weak-read-holds.kz)
```kurz
class Owner(string Name)
class Home(mut Owner Resident)
class Pet(weak Owner? Keeper)

h = Home(Owner("Ann"))
p = Pet(h.Resident)
k = p.Keeper
h.Resident = Owner("Bob")
print(k?.Name ?? "gone")
print(p.Keeper?.Name ?? "gone")
```

Case: [memory/weak-anywhere.kz](../corpus/memory/weak-anywhere.kz)
```kurz
class Owner(string Name)

weak Owner? Remember(weak Owner? r) {
    return r
}

string NameOf(weak Owner? r) {
    return r?.Name ?? "gone"
}

o = Owner("Ann")
weak Owner? a = Remember(o)
print(NameOf(a))
weak Owner? b = Remember(Owner("Bob"))
print(NameOf(b))
```

Case: [memory/weak-map-value.kz](../corpus/memory/weak-map-value.kz)
```kurz
class Owner(string Name)

o = Owner("Ann")
mut keepers = Map<string, weak Owner?>()
keepers["ann"] = o
print(keepers["ann"]?.Name ?? "gone")
```

Case: [memory/weak-data-equality.kz](../corpus/memory/weak-data-equality.kz)
```kurz
class Owner(string Name)
data Tag(string Label, weak Owner? Keeper)

o = Owner("Ann")
print(Tag("x", o) == Tag("x", o))
print(Tag("x", o) == Tag("x", Owner("Bo")))
gone = Tag("x", Owner("Cy"))
print(gone == Tag("x", null))
```

Case: [memory/weak-key.kz](../corpus/memory/weak-key.kz)
```kurz
class Owner(string Name)

seen = Map<weak Owner?, int>()
print(seen.Count)
```

Case: [classes/equal-by-weak.kz](../corpus/classes/equal-by-weak.kz)
```kurz
class Owner(string Name)
class Pet(string Name, weak Owner? Keeper) equal by Name, Keeper

print(Pet("Rex", null) == Pet("Rex", null))
```

### R4 (decided, §3) Where `weak` is enough

Between different types, `weak` on the back-pointer is enough. A class that reaches itself
through a strong `mut` field is rejected even when it also has a `weak` back-pointer, because
the strong field alone can close a ring.

Case: [memory/weak-back-pointer.kz](../corpus/memory/weak-back-pointer.kz)

Case: [memory/cycle-children-with-weak-parent.kz](../corpus/memory/cycle-children-with-weak-parent.kz)
```kurz
class Node(mut List<Node> Children, mut weak Node? Parent)

n = Node(List<Node>(), null)
print(n.Children.Count)
```

### R5 (decided, §3) The `?` of a `weak` type is always written

A weak reference is always nullable, and its type always shows it: `weak Node?`, also inside a
collection, `List<weak Node?>`. `weak Node` without the `?` is the compile error `weak-not-nullable`,
never read as if the `?` were there. `weak` in front of a `data`, a number, a string or a
collection type, which are values (M1) and have no instance to free, is the compile error
`weak-value`. *(both errors are a proposed reading of this rule)*

Case: [memory/weak-not-nullable.kz](../corpus/memory/weak-not-nullable.kz)
```kurz
class Node(string Name, weak Node Parent)

print(Node("root", null).Name)
```

Case: [memory/weak-value.kz](../corpus/memory/weak-value.kz)
```kurz
data Point(int X, int Y)
class Mark(weak Point? At)

print(Mark(null).At == null)
```

Case: [memory/weak-in-list.kz](../corpus/memory/weak-in-list.kz)
```kurz
class Person(string Name)
class Club(mut List<weak Person?> Members)

p = Person("Ann")
c = Club(List<weak Person?>())
c.Members.Add(p)
print(c.Members.Count)
print(c.Members[0]?.Name ?? "gone")
```

### R6 (decided, §3) A tree is a recursive `data` value

A tree is a `data` type that refers to itself through nullable fields. It is updated in place
while nobody else holds it; when somebody holds an older version, an update copies the path from
the root to the changed node, and the older version stays as it was.

Case: [memory/tree-value.kz](../corpus/memory/tree-value.kz)
```kurz
data Tree(int Key, Tree? Left, Tree? Right)

mut root = Tree(8, null, null)
before = root
root.Left = Tree(3, null, null)
print(root.Left?.Key ?? 0)
print(before.Left?.Key ?? 0)
```

### R7 (decided, §3) `raw` blocks

Code that touches raw memory sits in `raw` blocks. A `raw` block compiles only in a package that the
project grants `allow raw`; one in a package without the grant is the compile error
`raw-not-allowed`, and a program without a `project.kz` has no grant. A case of the corpus, a single
file, takes the grants of its header's `// allow:` line as a `project.kz` would give
them to its package (§3, the owner, 2026-10-21); in any other file the line is a comment and grants
nothing *(assumed: as §3 marks it)*. The line takes `raw`, `native` or `raw native`, in that order, the grants R7 and
R16 ask of a file's own code; each compiler's runner of the corpus reads it and gives the case's file
its grants, as Rust's test runner reads a test's directives and passes their flags to the compiler,
so that a program outside the corpus takes no grant from it and a build of a project takes none from it
*(assumed: as §3 marks it)*. A `raw` block holds the machine instructions no other form of the language expresses, as
inline assembly (§3, the owner, 2026-10-21), written as R11 says, and the reading and writing through a
raw pointer (R10).
The compiler's memory guarantees cover the Kurz code outside `raw` blocks; the runtime is Kurz (§2),
and its `raw` blocks and the inline assembly in them are unchecked as every `raw` block is, and so is
the foreign code the process runs beside them, the C library, the system's DLLs on Windows, the compiler's
support library, the unwind library on Linux and `dbghelp.dll` on Windows, Espressif's framework and its
radio's binary libraries on the ESP32 models, avr-libc on AVR, the cipher library of TLS and a library the
program takes through the C-ABI, so the guarantees hold while that code writes only the memory it
owns or was handed, inside the bounds it was given, and keeps the invariants the Kurz code relies on
*(assumed: the foreign code outside the guarantees, under the
condition on what that code writes and keeps, as §3 marks it)*. The grant is per package and written
like `allow network`. *(the error and the program without a project file are a proposed reading)* The
runtime's and the standard library's packages carry the grant themselves, so that a program without
a project file compiles with them *(assumed: as §3 marks it)*. The
case of a `raw` block that compiles takes the grant through its header, `// allow: raw`.

Case: [memory/raw-not-allowed.kz](../corpus/memory/raw-not-allowed.kz)
```kurz
raw {
    print(1)
}
```

Case: [memory/raw-allowed.kz](../corpus/memory/raw-allowed.kz)
```kurz
raw {
    print(1)
}
```

### R10 (decided, §3) Raw pointers

A raw pointer is written as C# writes it in `unsafe` code: the type `int*`, the reading and writing
through it `*p` and `p[i]`, the offset `p + n` and the address `&x`; an offset is an `int` or a
`long`, as every index is (§4). A pointer type stands anywhere, in a field or a parameter, and only
the reading or writing through a pointer needs a `raw` block (§3, the owner, 2026-10-21); one outside it
is the compile error `raw-required`.
The address `&x` and the offset `p + n` read and write nothing and need no `raw` block, and `p + n`
computes as Rust's `wrapping_add` (§3, the owner, 2026-10-21). *(assumed: the id `raw-required`, as §3
marks it)* `p + n` and `p - n` count elements of the pointee, `p - q` is a `long`, the count of
elements from `q` to `p`, and `==`, `!=`, `<`, `<=`, `>` and `>=` compare two pointers' addresses, none of
them needing a `raw` block; `&x` takes a variable, a field or an element, and the pointer holds while
that value lives *(assumed: the element count, the subtraction, the comparisons and what `&x` takes,
as §3 marks them)*. `p - q` takes two pointers of one pointee type, others being `type-mismatch` (T1), and
divides the distance in bytes by the size of the pointee; `p - n` wraps as `p + n` does; `&x` of a
variable that is not `mut` gives a pointer through which a write is a misuse of `raw` code, and so is
a read or a write through a pointer whose value a sweep or a return freed, outside the guarantees
(R7) *(assumed: as §3 marks them)*. A pointer can be `null` (`int* p = null`), and `p == null` compares outside `raw`; N2's check does
not apply to it, since a pointer type is no `T?`, and a read or a write through `null` is a misuse of
`raw` code. A pointer points to a number, a `bool`, another pointer or `extern "C"` data; a pointer
to a class, a list, a string or a `data` value that holds counted references, `&x` of a variable of
such a type among them, is the compile error `counted-pointee` (§3, the owner, 2026-10-21), and a
`data` value that holds no counted reference is a pointee too *(assumed: the id and that pointee, as
§3 marks them)*. A pointer points, beyond these, to every other type that holds no counted reference,
a `char`, an `enum` or a `flags` type, a `duration`, a `timestamp`, a `decimal`, a `T?` of such a type
and a union of such types among them, while a type that holds a counted reference stays
`counted-pointee` (§3, the owner, 2026-10-22).

Case: [memory/pointer-offset.kz](../corpus/memory/pointer-offset.kz)
```kurz
long m = 64
p = Ptr<long>(m)
q = p + 3
print(q - p)
```

Case: [memory/pointer-compare.kz](../corpus/memory/pointer-compare.kz)
```kurz
long m = 64
p = Ptr<long>(m)
q = p + 3
print(q > p)
```

Case: [memory/pointer-read-outside-raw.kz](../corpus/memory/pointer-read-outside-raw.kz)
```kurz
long m = 64
p = Ptr<long>(m)
print(p[0])
```

Case: [memory/string-pointer-read.kz](../corpus/memory/string-pointer-read.kz)
```kurz
s = "hi"
raw {
    p = s.Pointer
    print(p[1])
}
```

Case: [memory/pointer-address.kz](../corpus/memory/pointer-address.kz)
```kurz
mut long n = 5
p = &n
raw {
    *p = 7
}
print(n)
```

Case: [memory/pointer-parameter.kz](../corpus/memory/pointer-parameter.kz)
```kurz
void Set(long* p, long v) {
    raw {
        *p = v
    }
}

mut long n = 1
Set(&n, 9)
print(n)
```

Case: [memory/pointer-deref-outside-raw.kz](../corpus/memory/pointer-deref-outside-raw.kz)
```kurz
long n = 5
// the address of a variable that is not `mut` is a pointer to read through, a write through it being a misuse (R10)
p = &n
print(n)
print(*p)
```

Case: [memory/address-of-counted.kz](../corpus/memory/address-of-counted.kz)
```kurz
s = "hi"
// the address of a variable that is not `mut` is a pointer to read through (R10), so the counted pointee is the one defect here
p = &s
// a pointer compares with `null` outside `raw` (R10)
print(p == null)
print(s)
```

Case: [memory/pointer-to-counted-union-field.kz](../corpus/memory/pointer-to-counted-union-field.kz)
```kurz
data Cell(int | string Value)

void Show(Cell* p) {
}
// a `Cell` holds a union, and one of the union's cases, the `string`, is a counted reference, so no pointer points to a `Cell` (R10)
```

Case: [memory/pointer-to-char.kz](../corpus/memory/pointer-to-char.kz)
```kurz
mut char c = 'a'
p = &c
raw {
    *p = 'b'
}
print(c)
// a `char` holds no counted reference, so a pointer points to it as to a number (R10)
```

Case: [values/text-timestamp.kz](../corpus/values/text-timestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        print(*Ptr<timestamp>(&bits))
    }
}

Show(0)
Show(1793179800000000000)
Show(1793179801500000000)
Show(1793179801000250000)
Show(-1)
```

Case: [values/text-longtimestamp.kz](../corpus/values/text-longtimestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        longtimestamp epoch = *Ptr<timestamp>(&bits)
        print(epoch + 2932897days)
        print(epoch - 719528days)
        print(epoch - 719528days - 1s)
    }
}

Show(0)
```

### R11 (decided, §3) Inline assembly

Inline assembly is a statement inside a `raw` block, written as Rust writes `asm!`:
`asm("add {r}, 1", r = inout(reg) count)`. A `{name}` in its template names an operand and is no
interpolation (L6); a `"""` block (L13) holds a template of many lines; the operands take Rust's
words, `in`, `out`, `inout`, `lateout`, the register classes Rust names for each architecture and
`clobber_abi` (§3, the owner, 2026-10-21). `{{` and `}}` write a brace, an operand is taken by its name or by
its position, a modifier after a colon gives the form of its register, `{r:e}`, every operand appears in
the template but one in an explicit register, `in("rax") 60`, which the template names nowhere, the operands take the rest of Rust's words, `inlateout`, `const`, `sym`, `label` and
`options(...)`, and a variable an operand writes is `mut` *(assumed: the rest of Rust's rules of the
template and of its words, as §3 marks them)*. On WebAssembly and on Xtensa the register classes are
those of Rust's experimental support for them, `local` on wasm32 and `reg`, `freg`, `sreg` and
`breg` on Xtensa; a breach of the template's rules is the compile error `asm-operand`, and an operand
that writes a variable without `mut` is `assign-immutable`, as any assignment to it is *(assumed: as §3
marks them)*. The operands' values are read, and the places outputs write through a pointer
(`*p`, `p[i]`) found, in the order the operands are written, but an `inout`'s input, which is loaded
when the `asm` runs, and an `inout` loads from and stores into the one place it finds, so a call in `p[Index()]` runs once, as Rust's compiler
lowers the operands of `asm!` *(assumed: Rust's order and Rust's `inout`, which no answer named, as §3
marks them)*. An `inout` loads its input when the `asm` runs, after every operand ran, one into a
variable, or into a path of fields from one, from the place the variable has then, and one through a
pointer from the place found in the order the operands are written, so the `asm` reads what a later
operand's call wrote there, as Rust's compiler reads the copy of an `inout`'s place at the `asm`
*(assumed: Rust's `inout`, which no answer named, as §3 marks it)*. An
output into a variable, or into a path of fields from one, is stored after the call,
into the place the variable has then, so a variable a later operand's call changed is written as that
call left it, as Rust writes an `out` operand's place and as an assignment into a path is written (M4)
(§3, the owner, 2026-11-13). The outputs are stored in the order the operands are written, so of two
outputs into one variable the later one stays, as Rust's compiler stores them *(assumed: Rust's
order, which no answer named, as §3 marks it)*. An instruction of an `asm`
that the assembler refuses is the compile error `asm-refused` at the line of the `asm`, which the build
raises once clang refuses the program (§3, the owner, 2026-11-13), and so is one of a `naked`
function's `asm` (R12) *(assumed: the id's name and the `naked` function's, as §3 marks them)*. An
`asm` the optimiser drops before the assembler reads it raises nothing, so a `pure` `asm` whose outputs
nothing reads is `asm-refused` in a test build, which keeps its call, and builds in a release build,
which drops it, as Rust's builds differ so (§3, the owner, 2026-11-13, pull request 530, comment 6854386584). The
build assembles the program's `asm`s together in the order LLVM meets them, the order the compiler
writes them, in one file as across files: the `naked` functions' assembly first (R12), in the order the
compiler writes the functions, then the `asm` statements, in the order of the functions that hold
them, a guarded block's (R18) before the function it stands in, and of the statements in each; each
beside those before it that the assembler took, and each one whose addition
the assembler refuses is `asm-refused`, once, in that order, so that the line named is the one LLVM
refuses: one it refuses alone, and of two `naked` functions that define one label the one LLVM meets
second, whose label the assembler refuses beside the first's, the earlier line's where the two are
called out of the order of their lines, where Rust's compiler, which gives a naked function's assembly
no `!srcloc`, names neither (§3, the owner, 2026-11-13, pull request 530, comment 6855725580). The
compiler writes the top-level code's function first, then every other function in the order its first
use, a call, a `sym` operand or a function value, is reached, those the top-level code reaches before
those their own text reaches, each standing where its text ends, and a function no use reaches not at
all, so that the `asm` of a `naked` function nothing uses is not assembled *(assumed: this compiler's
order, which no answer named, as §3 marks it)*. Where clang refuses
a release build, whose optimiser can copy the call of an `asm`, as where it inlines a function into two
callers, or drop it, the build marks each `asm` call with its line and assembles one copy for each call
the optimised module still holds, in the order that module holds them, so that the line named is the
one LLVM refuses, the second copy of a call the optimiser copied among them, and an `asm` the optimiser
dropped is named by none; a test build assembles its copies of the program as the compiler wrote it
(§3, the owner, 2026-11-13, #532, comment 6860019854, "Copies from optimised IR", against R11
narrowed for a release build, so that an `asm` the optimiser copies or drops can stay clang's failure
with no line, at the cost of a decided rule narrowed and a failure of a release build alone that names
no line of the program; the cost is paid by a refused release build alone: the optimised IR is written
and read again). The mark is a `!srcloc` on the call that holds the line of the `asm`, as Rust's
compiler puts one on each call of inline assembly, and a copy LLVM inlines keeps it *(assumed: the mark,
the build's mechanism, as §3 marks it)*. Two `asm`s that define one label by a name the reading of
labels takes are those of two `naked` functions (R12), since such a label of an inline `asm` is
`named-asm-label` (§3, the owner, 2026-11-13); two inline `asm`s can still define one label the reading
takes for none, a quoted one, `"twice":`, or one after a comment, `/* c */ twice:`, and one symbol a
directive defines, `.equiv twice, 1`, as Rust's lint leaves them, and the one the assembler refuses
beside those before it is `asm-refused` in LLVM's order *(assumed: Rust's reading of a template, as §3
marks it)*. Across the files of a
program the `asm`s are named in the order LLVM meets them, the order the compiler writes the
functions, and no order of the source is stated, as Rust's compiler reports the errors of LLVM as they
come (§3, the owner, 2026-11-13, pull request 530, comment 6854386584). A label an `asm`'s template
defines by name, `again:`, is the compile error `named-asm-label` at the line of the label, in a `"""` block
the label's own line, and local labels, `2:` with `2b` or `2f`, take its place, since LLVM may copy an
`asm`, as where a function is inlined into two callers, and the copy defines the label a second time; on
x86 and x86-64 *(assumed: the answer's "On x86" read as the family, x86-64 among it, as Rust's
`binary_asm_labels` takes both, which the answer did not name)* a label of only the digits 0 and 1, `1:`
among them, is the compile error `binary-asm-label`,
since the assembler reads its use, `1b`, as a binary number; R12 states which labels the `asm` of a
`naked` function may define; each as Rust's lints `named_asm_labels` and `binary_asm_labels` refuse them (§3, the owner,
2026-11-13). A template is read for its labels as Rust's lint pass reads it: it splits into statements at
each line break and `;`, a statement's text from `//` on is set aside, and each text before a `:` of a
statement, trimmed, from the statement's start or from the label before it, is a label where it starts
with an ASCII letter, `.` or `_` and goes on with ASCII letters, digits, `_` and `$`, or where it holds a
placeholder outside `options(raw)`, `{}:` or the `{0` of `{0:e}`, both named, its characters outside the
braces those of a name and any but `{` inside them, so that `inc {0` of `inc {0:e}` defines none, or where
it holds the digits 0 and 1 alone, binary, the first such text that is no label ending the statement's
labels, so that `2bar: done:` defines none; a binary label is an error in Intel's syntax alone, so that
`options(att_syntax)` takes `1:`; and an `asm` one of whose templates cannot be read, `asm-operand`, is
read for no label, as Rust's `asm!` is then no expression *(assumed: Rust's reading of a template, its exemption of `att_syntax`
and the ids' names, which no answer named, as §3 marks them)*. A
register class is read against the table of the target the build compiles for, so
that a class that target does not have is `asm-operand`, and a case is read for x86-64 where its
header names no target (§3, the owner, 2026-10-22); how a header names the target is open (§14), and
no case names one. An `asm` outside a `raw` block and outside the body of a
`naked` function (R12) is the compile error `raw-required` (R10) *(assumed: the id, as §3 marks it)*.
`asm` is a core word (L18). The cases of an `asm` in a `raw` block take the grant through their
header (R7). Every case runs through the compiler's front end, which reads `asm("nop")` with no error
and checks only the ids it raises itself, as for every case whose id another part of the compiler
raises.

Case: [memory/asm-outside-raw.kz](../corpus/memory/asm-outside-raw.kz)
```kurz
asm("nop")
// an `asm` outside a `raw` block and outside a `naked` function is raw-required (R11); the front end parses this line as an `asm` statement with no error, and the id is raised after the parse
```

Case: [memory/asm-in-raw.kz](../corpus/memory/asm-in-raw.kz)
```kurz
raw {
    asm("nop")
}
```

Case: [memory/asm-unknown-operand.kz](../corpus/memory/asm-unknown-operand.kz)
```kurz
raw {
    asm("nop {r}")
}
```

Case: [memory/asm-unused-operand.kz](../corpus/memory/asm-unused-operand.kz)
```kurz
raw {
    asm("nop", x = in(reg) 1)
}
```

Case: [memory/asm-explicit-register.kz](../corpus/memory/asm-explicit-register.kz)
```kurz
raw {
    asm("nop", in("rax") 1)
}
```

Case: [memory/asm-operands.kz](../corpus/memory/asm-operands.kz)
```kurz
mut long count = 41
mut long seven = 0
mut long high = 0
mut long low = 0
mut long doubled = 0
mut double x = 1.25
mut long n = 1
mut long kept = 5
raw {
    long* p = &n
    asm("add {r}, 1", r = inout(reg) count)
    asm("mov {x:e}, 7", x = out(reg) seven)
    print(count)
    print(seven)
    asm("lea {r}, [{r} + {k}]", r = inout(reg) count, k = const 100)
    print(count)
    asm("mul {f}", f = in(reg) 8, inlateout("rax") 4611686018427387904 => low, lateout("rdx") high)
    print(high)
    print(low)
    asm("lea {o}, [{i} + {i}]", i = in(reg) 21, o = out(reg) doubled, options(pure, nomem, nostack))
    print(doubled)
    asm("addsd {a}, {b}", a = inout(xmm_reg) x, b = in(xmm_reg) 2.5)
    print(x)
    asm("mov {v}, 9", v = out(reg) *p)
    print(n)
    // writes rcx and r8, which no output names, and the program goes on; the registers `clobber_abi` lets go of are
    // pinned by the compiler's test of the options, since `kept` sits in memory in a build that is not optimised
    asm("mov ecx, 3", "mov r8d, 4", clobber_abi("C"))
    print(kept)
}
```

Case: [memory/asm-inout-place.kz](../corpus/memory/asm-inout-place.kz)
```kurz
long Index() {
    print("index")
    return 0
}

mut long n = 10
raw {
    long* p = &n
    asm("add {r}, 1", r = inout(reg) p[Index()])
}
print(n)
```

Case: [memory/asm-inout-after-call.kz](../corpus/memory/asm-inout-after-call.kz)
```kurz
data Point(long X, long Y)

long Bump(mut Point point) {
    point = Point(10, 20)
    return 1
}

long Ten(long* slot) {
    raw {
        *slot = 10
    }
    return 1
}

mut Point p = Point(0, 0)
mut long n = 0
raw {
    long* q = &n
    asm("add {r}, {k}", r = inout(reg) p.X, k = in(reg) Bump(mut p))
    asm("add {r}, {k}", r = inout(reg) *q, k = in(reg) Ten(q))
}
print(p)
print(n)
```

Case: [memory/asm-operand-read-order.kz](../corpus/memory/asm-operand-read-order.kz)
```kurz
long Show(long v) {
    print(v)
    return v
}

mut long m = 0
raw {
    long* p = &m
    asm("mov {a}, {x}", a = out(reg) p[Show(0)], x = in(reg) Show(1))
}
print(m)
```

Case: [memory/asm-output-store-order.kz](../corpus/memory/asm-output-store-order.kz)
```kurz
mut long n = 0
raw {
    asm("mov {a}, 1", "mov {b}, 2", a = out(reg) n, b = out(reg) n)
}
print(n)
```

Case: [memory/asm-out-after-call.kz](../corpus/memory/asm-out-after-call.kz)
```kurz
data Base(long X)
data Point(long Y) : Base

long Bump(mut Point point) {
    point = Point(10, 20)
    return 5
}

mut Point p = Point(0, 0)
mut long n = 0
raw {
    asm("mov {x}, 7", "mov {n}, {k}", x = out(reg) p.X, n = out(reg) n, k = in(reg) Bump(mut p))
}
print(n)
print(p)
```

Case: [memory/naked-function.kz](../corpus/memory/naked-function.kz)
```kurz
long Nine() => 9

naked long Seven() {
    asm("mov eax, 7", "ret")
}

naked long Answer() {
    asm("mov eax, {k}", "ret", k = const 40 + 2)
}

naked long Forward() {
    asm("jmp {f}", f = sym Nine)
}

print(Seven())
print(Answer())
print(Forward())
```

Case: [memory/asm-register-twice.kz](../corpus/memory/asm-register-twice.kz)
```kurz
raw {
    asm("nop", in("rax") 1, in("eax") 2)
}
```

Case: [memory/asm-class-of-another-target.kz](../corpus/memory/asm-class-of-another-target.kz)
```kurz
mut long n = 0
raw {
    asm("mov {x}, 1", x = out(vreg) n)
}
print(n)
// `vreg` is a register class of AArch64 and not of x86-64; the case names no target, so it is read for x86-64 (R11)
```

Case: [memory/asm-refused.kz](../corpus/memory/asm-refused.kz)
```kurz
mut long n = 1
raw {
    asm("add {r}, 1", r = inout(reg) n)
    asm("add {r}, 1", "frobnicate {r}", r = inout(reg) n)
}
print(n)
```

Case: [memory/asm-label-twice.kz](../corpus/memory/asm-label-twice.kz)
```kurz
naked long One() {
    asm("twice:", "mov eax, 1", "ret")
}

naked long Two() {
    asm("mov eax, 2", "ret")
}

naked long Three() {
    asm("twice:", "mov eax, 3", "ret")
}

print(One() + Two() + Three())
```

Case: [memory/asm-named-label.kz](../corpus/memory/asm-named-label.kz)
```kurz
mut long n = 3
raw {
    asm("again:", "dec {n}", "jnz again", n = inout(reg) n)
}
print(n)
// LLVM may copy an `asm`, as where a function is inlined into two callers, and the copy defines `again` a second time; a local label, `2:` with `2b`, takes its place (R11)
```

Case: [memory/asm-binary-label.kz](../corpus/memory/asm-binary-label.kz)
```kurz
mut long n = 3
raw {
    asm("1:", "dec {n}", "jnz 1b", n = inout(reg) n)
}
print(n)
// the case names no target, so it is read for x86-64, whose assembler reads `1b` in Intel's syntax as a binary number (R11)
```

Case: [memory/asm-local-label.kz](../corpus/memory/asm-local-label.kz)
```kurz
mut long n = 3
mut long steps = 0
raw {
    asm("2:", "inc {s}", "dec {n}", "jnz 2b", n = inout(reg) n, s = inout(reg) steps)
}
print(steps)
print(n)
// a local label, `2:` with `2b`, takes the place of a label by name, which a copy of the `asm` LLVM makes would define a second time (R11)
```

Case: [memory/asm-att-binary-label.kz](../corpus/memory/asm-att-binary-label.kz)
```kurz
mut long kept = 7
raw {
    asm("jmp 1f", "mov $9, {k}", "1:", k = inout(reg) kept, options(att_syntax))
}
print(kept)
// a label of only the digits 0 and 1 is binary-asm-label in Intel's syntax alone, so `1:` stands under `att_syntax` (R11)
```

Case: [memory/naked-named-label.kz](../corpus/memory/naked-named-label.kz)
```kurz
naked long Seven() {
    asm("jmp done", "mov eax, 1", "ret", "done:", "mov eax, 7", "ret")
}

print(Seven())
// the compiler emits a `naked` function with no type parameters once, so its `asm` may define a label by name (R12)
```

### R12 (decided, §3) A function with no frame

A function marked `naked` before its signature, `naked void Switch(...)`, gets no prologue and no
epilogue from the compiler; its body is one `asm` statement (R11), and it compiles only in a package
granted `allow raw` (§3, the owner, 2026-10-21); its body counts as a `raw` block (§3, an assumption
there). One in a package without the grant, or in a program without a project file, which has no grant (R7), is the compile
error `naked-not-allowed`, and its body, a `raw` block, raises no `raw-not-allowed` beside it *(assumed:
the id and the one error, as §3 marks them)*.
Its `asm` takes `sym` and `const` operands alone, its instructions return or never end, and the
function is called by the C calling convention of its target *(assumed: as §3 marks them)*. Its `asm`
may define labels by name, which R11 refuses in an inline `asm` (§3, the owner, 2026-11-13), and labels of
only the digits 0 and 1 *(assumed: binary labels within the exemption, as §3 marks them)*. A `naked`
function with type parameters is one no rule states, and the compiler reads none; where one is read,
its `asm`, which would be emitted once for each copy, is read for its labels as an inline `asm` is (R11),
as Rust exempts a naked function that is not generic *(assumed: a function with type parameters
outside the exemption, as §3 marks it)*. A label of its `asm` that is a symbol of the program is
`asm-refused` (R11); the compiler names the program's symbols, but `main`, which the C library's start
calls, and the C functions a program declares (R16), so which other label is one is the compiler's, as
Rust's compiler mangles the symbols of its items but those of `#[no_mangle]` ones, and no case rests on
one *(assumed: Rust's way, which no answer named, as §3 marks it)*. A body
other than one `asm`, or an operand there other than `sym` and `const`, is the compile error
`naked-body`, and instructions that fall through the function's end are a misuse of `raw` code, which
no compiler checks *(assumed: as §3 marks them)*. `naked` stands before a function at the top level
of a file alone, `naked extern "C"` among them; before a method, a lambda or a function declared
inside a block of code it is `syntax` (E4) (§3, the owner, 2026-10-22). A `naked extern "C"` function
needs `allow raw` and `allow native`, and a `naked` function without `extern "C"` needs `allow raw`
alone, R16's grant going with the word `extern "C"` written and not with the C calling convention,
which a `naked` function takes in any case, so that in a package with neither grant a
`naked extern "C"` function is `naked-not-allowed` and `native-not-allowed` at its declaration, two
errors at one line, which no case shows, a case expecting one error *(assumed: as §3 marks it)*. `naked` is a core word (L18).
An instruction of its `asm` that the assembler refuses is `asm-refused` at the line of that `asm` (R11)
*(assumed: as §3 marks it)*.

Case: [memory/naked-not-allowed.kz](../corpus/memory/naked-not-allowed.kz)
```kurz
naked void Switch() {
    asm("ret")
}
```

Case: [memory/naked-body.kz](../corpus/memory/naked-body.kz)
```kurz
naked void Switch() {
    asm("mov {x}, 1", x = out(reg) _)
}
```

Case: [memory/naked-method.kz](../corpus/memory/naked-method.kz)
```kurz
class Switcher {
    naked void Switch() {
        asm("ret")
    }
}
// the grant keeps `naked-not-allowed` out, and a `naked` body counts as a `raw` block, so that the place of `naked` is the one defect (R12)
```

Case: [memory/naked-function.kz](../corpus/memory/naked-function.kz)

Case: [memory/naked-refused.kz](../corpus/memory/naked-refused.kz)
```kurz
naked long Seven() {
    asm("mov eax, 7", "frobnicate eax", "ret")
}

print(Seven())
```

Case: [memory/asm-label-twice.kz](../corpus/memory/asm-label-twice.kz)

Case: [memory/naked-named-label.kz](../corpus/memory/naked-named-label.kz)

### R13 (decided, §3) State outside a function

A package granted `allow raw` declares, at the top level of a file, state that every function of the
package reaches, `static mut long freeList = 0`, and state of which each thread has its own, `thread
static mut long stackLimit = 0`, both read and written inside a `raw` block alone, as Rust reads and
writes a `static mut` only in an `unsafe` block (§3, the owner, 2026-10-21); this is the one
exception the record decides to the reason of K18's rule, that no state is reached by every actor, a
C library's global variables standing open (§14). `thread` is a word
only before `static` in such a declaration and a name everywhere else (L18; §3, the owner,
2026-10-21). Such state is its package's alone: no `pub` stands before it, and another package calls
a function of its package (§3, the owner, 2026-10-21). Such a declaration holds what a pointer points
to (R10) and takes an initial value, which is a constant expression as F12 counts one for a
parameter's default value; each thread's value starts at that
constant; a declaration that no function reads is no `unused-variable` (V5), being state of its
package and no variable; a `static` without `mut` at the top level of a file has no meaning there,
`syntax` (E4) as on any variable; and a `static mut` field of a class stays `static-state` in such a
package too (K18). A declaration of either kind in a package without the grant, of another type,
without an initial value or with one that is no constant, or with `pub` before it, is the compile
error `static-state`, and a read or a write of one outside a `raw` block is `raw-required` (R10)
*(assumed: as §3 marks them)*. A `thread static mut` is the target's thread-local storage, as C's
`_Thread_local` is: each thread's value sits at an offset from the pointer the target keeps for each
thread, an access taking a few instructions more than one of a `static mut`, and on a target that
runs one thread, AVR among them, it is one value *(assumed: as §3 marks it)*.

Case: [memory/static-mut-in-raw.kz](../corpus/memory/static-mut-in-raw.kz)
```kurz
static mut long total = 0

void Add() {
    raw {
        total = total + 1
    }
}

Add()
Add()
raw {
    print(total)
}
```

Case: [memory/static-mut-not-allowed.kz](../corpus/memory/static-mut-not-allowed.kz)
```kurz
// a package without `allow raw` declares no such state (R13); that no function reads it is no `unused-variable`, the state being no variable
static mut long total = 0
print(1)
```

Case: [memory/static-mut-outside-raw.kz](../corpus/memory/static-mut-outside-raw.kz)
```kurz
static mut long total = 0

print(total)
```

Case: [memory/static-mut-pub.kz](../corpus/memory/static-mut-pub.kz)
```kurz
pub static mut long total = 0
raw {
    total = 1
}
```

Case: [memory/thread-static-mut.kz](../corpus/memory/thread-static-mut.kz)
```kurz
thread static mut long depth = 0
raw {
    depth = depth + 1
    print(depth)
}
```

Case: [source/thread-as-name.kz](../corpus/source/thread-as-name.kz)
```kurz
thread = 1
print(thread)
```

### R14 (decided, §3) The conversions of a pointer and the size of a type

A conversion between pointers, and of a pointer to and from a `long`, takes the call form of T10's
conversions: `Ptr<Block>(m)` is the pointer to a `Block` at the address `m` holds, `long(b)` the
address `b` holds, a field is reached through a pointer by `(*b).Next`, and the size of a type in bytes
is `SizeOf<Block>()`, as Rust converts with `as` and reads a size with `size_of::<T>()` (§3, the
owner, 2026-10-21). `Ptr<T>` names the type `T*` in a conversion alone, `Ptr<T>(x)` takes a pointer
to any pointee or a `long`, `long(p)` gives the address as a `long`, and `SizeOf<T>()` is a `long`
and takes what a pointer points to (R10); none of these reads or writes through a pointer, so none
needs a `raw` block, nor the grant `allow raw`, a pointer standing in a package without it (R10), while
`(*b).Next` reads through one and needs it *(assumed: as §3 marks them)*. The untyped pointer is
`void*`, through which nothing is read or written and which `p + n`, `p - n` and `p - q` do not take; a conversion of
another value is `type-mismatch` (T1), and `SizeOf` of a type a pointer cannot point to is
`counted-pointee`. On a target whose pointer is narrower than a `long`, `Ptr<T>(x)` keeps the low
bits of `x` and `long(p)` gives the address as a number that is never negative, and on a target whose
pointer is as wide as a `long` both keep the bits, so that an address whose highest bit is set is a
negative `long` and `Ptr<T>(x)` of a negative `x` is that address; a read or a write
through `void*`, and `p + n`, `p - n` and `p - q` on one, are `type-mismatch`, `Ptr<void>(x)` gives a `void*` and
`SizeOf<void>()` is `type-mismatch`; and another count of type arguments or of arguments in these
forms is `syntax` (E4) *(assumed: as §3 marks them)*. `Ptr` and `SizeOf` are core words (L18; §3,
the owner, 2026-10-21).

Case: [memory/pointer-conversion.kz](../corpus/memory/pointer-conversion.kz)
```kurz
long m = 64
p = Ptr<long>(m)
print(long(p))
```

Case: [memory/size-of.kz](../corpus/memory/size-of.kz)
```kurz
print(SizeOf<long>())
```

Case: [memory/size-of-counted.kz](../corpus/memory/size-of-counted.kz)
```kurz
print(SizeOf<string>())
```

Case: [memory/pointer-offset.kz](../corpus/memory/pointer-offset.kz)

Case: [memory/pointer-compare.kz](../corpus/memory/pointer-compare.kz)

Case: [memory/pointer-read-outside-raw.kz](../corpus/memory/pointer-read-outside-raw.kz)

Case: [source/core-word-sizeof.kz](../corpus/source/core-word-sizeof.kz)
```kurz
SizeOf = 1
print(SizeOf)
```

Case: [values/text-timestamp.kz](../corpus/values/text-timestamp.kz)

Case: [values/text-longtimestamp.kz](../corpus/values/text-longtimestamp.kz)

### R15 (decided, §3, §12) The memory of a string and a list

A string and a list show their memory inside a `raw` block alone: `s.Pointer` is the pointer to the
bytes of a string, whose count `s.Bytes.Count` gives (T15), `l.Pointer` the pointer to the first
element of a list, whose count is `l.Count`, and `string.FromRaw(p, n)` and `List<T>.FromRaw(p, n)`
make a string of the `n` bytes and a list of the `n` elements at `p`, as Rust's `as_ptr`, `len` and
`String::from_raw_parts` and `Vec::from_raw_parts` do (§3, the owner, 2026-10-21). The layout is the
compiler's, defined in one place. `s.Pointer` points at the string's UTF-8 bytes in order (T15) and
`l.Pointer` at the list's elements in order, the layout around them being the compiler's.
`s.Pointer` is a `byte*` and `l.Pointer` a `T*`; `l.Pointer` and `List<T>.FromRaw` take a `T` that a
pointer can point to, another `T` being `counted-pointee` (R10); a write through `s.Pointer` is a
misuse of `raw` code, a string being immutable; the pointer of an empty string or list is not `null`,
and nothing is read or written through it; the member's pointer holds while the string or the list
lives and, for a list, while its count does not change, a use after either being a misuse of `raw`
code; and such a member outside a `raw` block is the compile
error `raw-required` (R10). `List<T>.FromRaw` takes over memory `List<T>.Allocate` gave (below), which
the list frees as its own and grows from; bytes handed to `string.FromRaw` that are not UTF-8 are a
misuse of `raw` code; and `n` of 0 makes an empty string or list; and `FromRaw` takes no capacity,
which Rust's two functions take as a third argument *(assumed: as §3 marks them)*.

`string.Allocate(n)` gives the address of room for `n` bytes in a block that holds the count of a
string in front of them (§4), which `raw` code writes, and `string.FromRaw(p, n)` takes over such a
block, `n` no more than its room, which the string frees as its own; memory from anywhere else, or a
count past the room, handed to `string.FromRaw` is a misuse of `raw` code, and a block never handed
to it is never given back, as Rust's `String::from_raw_parts` takes only memory the allocator of
Rust's standard library gave (§3, the owner, 2026-10-29). `string.Allocate` outside a `raw` block
is `raw-required` (R10), and more than the largest `int` of bytes asked of it raises
`allocation-failed` with the count it asked for (T15) *(assumed: as §3 marks them)*.

`List<T>.Allocate(n)` gives the address of room for `n` items of `T` in a list's block, a `T*`, which
`raw` code writes, and `List<T>.FromRaw(p, n)` takes over such a block, `n` no more than its room,
the list holding the room `Allocate` gave; memory from anywhere else, or a count past the room,
handed to `List<T>.FromRaw` is a misuse of `raw` code (§3, the owner, 2026-10-29). A write through
`l.Pointer` is a misuse of `raw` code, since another list value can share the block (M3);
`List<T>.Allocate` takes a `T` that a pointer can point to, another `T` being `counted-pointee`
(R10), outside a `raw` block it is `raw-required` (R10), and more than the largest `int` of items
asked of it raises `allocation-failed` with the bytes of those items, as M9 counts them
*(assumed: as §3 marks them)*.

A `raw` member copies a string into a block that ends in a NUL byte, for a C function that takes one
(§12, the owner, 2026-10-30, against a type of its own for a C string, as Rust's `CString`); its name,
what it does with a string that holds a NUL byte, and what frees the block stand open (§14).

Case: [memory/string-from-raw.kz](../corpus/memory/string-from-raw.kz)
```kurz
raw {
    p = string.Allocate(2)
    p[0] = 104
    p[1] = 105
    print(string.FromRaw(p, 2))
}
```

Case: [memory/string-pointer.kz](../corpus/memory/string-pointer.kz)
```kurz
s = "hi"
raw {
    print(s.Pointer != null)
}
```

Case: [memory/string-pointer-read.kz](../corpus/memory/string-pointer-read.kz)

Case: [memory/list-from-raw.kz](../corpus/memory/list-from-raw.kz)
```kurz
raw {
    p = List<byte>.Allocate(4)
    p[0] = 104
    p[1] = 105
    b = List<byte>.FromRaw(p, 2)
    print(b)
    print(string.FromUtf8Lossy(b))
}
```

Case: [memory/list-pointer.kz](../corpus/memory/list-pointer.kz)
```kurz
mut xs = Bytes()
xs.Add(104)
xs.Add(105)
raw {
    p = xs.Pointer
    print(p[1])
}
```

### R16 (decided, §2, §13) The functions of C

A package granted `allow native` declares the functions of a C library in a block of declarations
with no bodies, `extern "C" { long write(int fd, byte* buf, long n) }`, and a call of one stands
inside a `raw` block, as Rust declares them in an `unsafe extern "C"` block (§13, the owner,
2026-10-21). A block names the library its functions come from after `in`, `extern "C" in "dbghelp"
{ ... }`, and a block without it takes what the target links by default, the C library among it
(§13, the owner, 2026-10-21). A function of the package's own that takes the C calling convention,
`extern "C" int OnSample(int frames) { ... }`, needs the grant too (§2, §13). A declaration takes the
types that this form takes (§2), and its last parameter may be `...`, for a C function that takes a
variable count of arguments; an argument in the place of `...` takes those types but a number smaller
than an `int`, a `bool` and a `float`, which C would widen and which the developer converts. A block
in a package without the grant is the compile error `native-not-allowed`; a call of a declared
function outside a `raw` block is `raw-required` (R10), so that a package that calls one needs `allow
raw` beside `allow native` (R7); and a declaration with a type that cannot cross to C, or such an
argument in the place of `...`, is `native-type` *(assumed: as §13 marks them)*. A declaration takes
default values and its call named arguments (F12), which the call fills in, while a `mut` parameter
in a declaration is `native-type` (§13, the owner, 2026-10-22). In a declaration whose last parameter
is `...`, the arguments without a name fill the declared parameters in order, those with a default
among them, and only those beyond them stand in the place of `...`, as C# binds them before its
`params` array *(assumed: as §13 marks it)*. A function of the package's own in
the C calling convention is called by its name with no `raw` block, and in a package without the
grant it is `native-not-allowed` at its declaration (§13, the owner, 2026-10-22); a `naked` one
without `extern "C"` takes R12's grant alone *(assumed: as §3 marks it)*. A block stands at
the top level of a file alone, and anywhere else, inside a function, the body of a type or a block of
code, it is `syntax` (E4) (§13, the owner, 2026-10-22). A C declaration, a declaration in a block of C
functions, whose name another function of the package, a type or another C declaration has is
`redeclared` (V9) at the second of the two (§13, the owner, 2026-10-22), the second in one file being
the one written after the other and across files the one in the file that comes after the other in
D6's order of the paths
*(assumed: as §13 marks it)*. `extern` is
a core word (L18).

Case: [memory/extern-not-allowed.kz](../corpus/memory/extern-not-allowed.kz)
```kurz
extern "C" {
    int abs(int n)
}
```

Case: [memory/extern-call.kz](../corpus/memory/extern-call.kz)
```kurz
extern "C" {
    int abs(int n)
}

raw {
    print(abs(-3))
}
```

Case: [memory/extern-call-outside-raw.kz](../corpus/memory/extern-call-outside-raw.kz)
```kurz
extern "C" {
    int abs(int n)
}

print(abs(-3))
```

Case: [memory/extern-type.kz](../corpus/memory/extern-type.kz)
```kurz
extern "C" {
    long strlen(string s)
}
```

Case: [memory/extern-variadic-bool.kz](../corpus/memory/extern-variadic-bool.kz)
```kurz
extern "C" {
    int fcntl(int fd, int cmd, ...)
}

raw {
    fcntl(0, 1, true)
}
```

Case: [memory/extern-mut-parameter.kz](../corpus/memory/extern-mut-parameter.kz)
```kurz
extern "C" {
    long time(mut long t)
}
// C's `time` takes a pointer, which the declaration writes `long*` (R16)
// a declaration takes a default value and its call a named argument, as every function does, and no `mut` parameter (R16)
```

Case: [memory/extern-default-named.kz](../corpus/memory/extern-default-named.kz)
```kurz
extern "C" {
    int abs(int n = -5)
}

raw {
    print(abs(n: -3))
    print(abs())
}
```

Case: [memory/extern-own-call.kz](../corpus/memory/extern-own-call.kz)
```kurz
extern "C" long Twice(long x) {
    return x * 2
}

print(Twice(4))
// the header grants `allow native`, and a call of the package's own C function by its name needs no `raw` block (R16)
```

Case: [memory/extern-own-not-allowed.kz](../corpus/memory/extern-own-not-allowed.kz)
```kurz
extern "C" long Twice(long x) {
    return x * 2
}

print(Twice(4))
// the header grants nothing, and a function of the package's own in the C calling convention needs `allow native` (R16)
```

Case: [memory/extern-block-in-function.kz](../corpus/memory/extern-block-in-function.kz)
```kurz
void F() {
    extern "C" {
        int abs(int n)
    }
}

F()
// the grant keeps `native-not-allowed` out, so that the place of the block is the one defect (R16)
```

Case: [memory/extern-name-clash.kz](../corpus/memory/extern-name-clash.kz)
```kurz
long abs(long n) {
    return n
}

extern "C" {
    int abs(int n)
}
// the two clash by their name alone, whatever their parameters, since a C declaration has one name (R16)
```

Case: [memory/c-function-type.kz](../corpus/memory/c-function-type.kz)
```kurz
// a value of the type is the name of a function of the C calling convention (R17), which R16 states
extern "C" long Twice(long x) {
    return x * 2
}

extern "C" (long) => long f = Twice
raw {
    print(f(4))
}
```

### R17 (decided, §2, §13) The type of a C function

A function's address passes to C as a value of a C function type, written as a function type (F11)
after `extern "C"`, `extern "C" (int) => void`, whose value is the name of a function that takes the
C calling convention, the package's own or one that a block of R16 declares, as Rust writes the type
`extern "C" fn(i32)` (§13, the owner, 2026-10-21). Such a value is never `null`, its nullable form is
`(extern "C" (int) => void)?`, a call through it outside a `raw` block is `raw-required` (R10), as a
call of a function that a block of R16 declares is, it stands where a raw pointer stands (R10), in a field of C-layout data,
`extern "C" data Point(int X, int Y)` (§2), among them, and a lambda or a function that does not take the C calling convention where such a type is
required is `type-mismatch` (T1); a `null` that C gives where such a type is not nullable, in a field
it writes or as the result of a function R16 declares, is a misuse of `raw` code, as a `fn` pointer
value must be non-null in Rust, so that a declaration where C may give `null` takes the nullable form
*(assumed: as §13 marks them)*.

Case: [memory/c-function-type.kz](../corpus/memory/c-function-type.kz)

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

Case: [memory/c-function-type-outside-raw.kz](../corpus/memory/c-function-type-outside-raw.kz)
```kurz
extern "C" long Twice(long x) {
    return x * 2
}

extern "C" (long) => long f = Twice
// a call through a value of the type outside `raw` is `raw-required` (R17), whatever function the value holds
print(f(4))
```

Case: [memory/c-function-type-lambda.kz](../corpus/memory/c-function-type-lambda.kz)
```kurz
void Run(extern "C" (long) => long f) {
}

Run((long x) => x * 2)
```

### R18 (decided, §13) Intrinsics

An intrinsic is a function of the class of an instruction set, which the compiler turns into that
set's instructions, `X86.Sse41.Extract(v, 2)`; the classes stand under `X86` and `Arm`, the value
types `Vector128<T>` and `Vector256<T>` hold the lanes, and each class has `IsSupported`, which tests
the CPU for its set (§13, the owner, 2026-11-01, "C#'s shape"). The classes and their functions take
the names, and each class the class it derives from, of .NET 10's `System.Runtime.Intrinsics`; the
class of x86's SHA set, which .NET 10 does not have, is `X86.Sha`, derived from `X86.Sse2` as the
class of .NET's closed proposal was; and the operations of `Vector128<T>` and `Vector256<T>` on their
lanes, `Vector128.Create(1, 2, 3, 4)` and `v.GetElement(2)`, are no
intrinsics and need no test *(assumed: as §13 marks them)*. The functions of `X86.Sha` are the seven
of .NET's closed proposal, `Sha256TwoRounds` among them, on `Vector128<uint>` (§13, the owner,
2026-11-01, "Proposal names, uint lanes"). A constant given for an intrinsic's parameter that C#
types `byte`, the `2` of `X86.Sse41.Extract(v, 2)`, is an `int` for the pick of the function (F14)
and has to fit the parameter's type: `X86.Sse41.Extract(v, 256)` is `constant-overflow`; one for a
parameter that names a lane, the `index` of `X86.Sse41.Extract`, that fits and lies past the lanes
is the compile error `lane-out-of-range` at its line, whose text names the lane and the count of the
lanes, so `X86.Sse41.Extract(v, 255)` of four lanes builds nothing (§13, the owner, 2026-11-02); any
other constant is read as the instruction reads it, a count of 32 bytes or more giving 0 in
`X86.Ssse3.AlignRight`, and one of 16 to 31 the bytes of its left operand from the count less 16 on,
then zeros *(assumed: as §13 marks them)*. What such a parameter takes
from a value that is not a constant is open (§14): refused, as Rust refuses it, or chosen at run time
among the instruction's forms. `Vector128.Create` and `Vector256.Create` take one value, which every
lane takes, or one for each lane, another count being `argument-mismatch`, its lane type picked as a call's function is (F13, F14), and `GetElement` of an
index below 0 or past the lanes raises `index-out-of-range` *(assumed: as §13 marks them)*. A file's
own declaration of `X86`, `Arm`, `Vector128` or `Vector256`, at any level, is `redeclared`, as a file's
own `EnvError` is (S14), while a field takes the name (§13, the owner, 2026-11-01, "redeclared, as
File").

An intrinsic stands where a test of its class's `IsSupported` guards it, as a test narrows a
nullable variable (N3), and in a `raw` block (R7) (§13, the owner, 2026-11-01, "Checker narrows"):
wherever N3's flow proves the test, in the block of `if X86.Sse41.IsSupported { }`, on the right
of `X86.Sse41.IsSupported && ...` and of `!X86.Sse41.IsSupported || ...`, in the body of
`while X86.Sse41.IsSupported { }`, and
after an `if !X86.Sse41.IsSupported { }` whose block always leaves, for the rest of the block that
holds the `if`; `!` turns a test around, so that the `else` block of `if !X86.Sse41.IsSupported { }`
is guarded; a test of a class guards the intrinsics of the classes C# derives it from,
`X86.Sse41.IsSupported` those of `X86.Sse2`, while the `else` block of `if X86.Sse41.IsSupported { }`,
a test of a class that is neither the intrinsic's class nor a class derived from the intrinsic's
class, `X86.Aes` for `X86.Sse41`, and a test kept in a variable guard nothing *(assumed: as §13 marks them)*. Anywhere else an intrinsic is the compile error
`intrinsic-unguarded` *(proposed: the id)*. The compiler compiles a guarded block as a function of
its own for the sets its tests name, and every function the block reaches by direct calls, the
runtime's included, again for them, in which the
tests of those sets are `true`; where the build names a CPU that has a set, its test is `true`
everywhere (§2, §13, the owner, 2026-11-01, "Guarded block", "Copied per feature" and "Transitive,
all"). That function and each copy are compiled for the sets the tests of every guarded block they
stand in name, and the sets those derive from, all of whose tests are `true` in them, one copy of a
function for each such union, so that a function that blocks of two sets reach is compiled for each,
and for both where one of the blocks stands in the other; the copy stops at a C declaration (R16), at
a call through a function value, which runs the function as it was compiled where the value was made,
a lambda's, or a C function's (R17), whose address is that of the function compiled for the build's
CPU alone wherever it is taken, a function of the program being no value (F11), and at the compiler's
own helpers, the functions it writes into every program itself, the `==` of two values of a `data`
type or the sort of a list, while the functions of the runtime and the standard library, `print`'s
among them, are copied; a lambda written in a guarded block is guarded and compiled for the block's
sets *(assumed: as §13 marks them)*. In a block, the statements from the first that a test guards to
the block's end are compiled as the guarded block's function: the block of a test, the `else` block
of a test turned around, the body of a `while` and the rest of a block after a test turned around
whose block always leaves; the right side of `&&` after a test, and of `||` after a test turned around, is a function of
its own that gives the side's value; and a statement in either that a test guards for more sets is a
function again, for them all *(assumed: as §13 marks them)*. A guarded block reads and writes the
function's variables, and its `return`, `break`, `continue` and `throw` act as in any block; beside
the call, the function it is compiled into costs this: the variables it reads or writes pass to it by
their addresses and stay in memory across the call, how it left comes back as a number the caller acts
on, and the call is never inlined, since the function is compiled for sets its caller is not; where
its test is a constant `true`, the build naming a CPU that has the set or a copy being compiled for
it, the block is compiled in place, with none of these costs; the CPU is read once per process, at its
start, in a program that has a test the build does not answer as a constant, as it is in a build whose
CPU option names sets beyond the baseline (decided below); the guarded code and the
code that runs where its test is `false` are the program's own and can give different results
*(assumed: as §13 marks them)*.
`IsSupported` of a set the target's architecture does not have, a class under `Arm` on x86-64 and one
under `X86` on Arm, or of one a target cannot test, is a constant `false`, and the code it guards is still checked and not compiled, with no copy made for its
set; a case is read for x86-64 (R11), where the test of every class under `Arm`
is `false` *(assumed: as §13 marks it)*. A set every CPU of the architecture has, `X86.Sse2` on x86-64, is tested as any other: its
test is a constant `true`, and an intrinsic of it outside a test and outside `raw` is
`intrinsic-unguarded` *(assumed: as §13 marks them)*. A `raw` block that runs an intrinsic of a set
the CPU lacks, of the classes the compiler builds, whose every instruction is one a CPU without its set
refuses *(assumed: as §13 marks it)*, ends the process with an illegal instruction, and nothing beyond the platform's fault
for it is promised: that the set is there is an invariant `raw` code keeps *(assumed: as §13 marks it)*. A build whose CPU
option names sets beyond the target's baseline, in which the test of such a set is a constant `true`,
reads the CPU once at its start, every such set as LLVM reads the CPU for `native`, the state the
system saves for the sets of AVX and after among it, and where the CPU lacks one of them ends before
the program's first statement with the exit code 1 and the line
`error: cpu-lacks-set (<the CPU option>: <the sets it lacks, by clang's names>)`, as .NET's Native AOT
ends where the sets a build requires are missing; a build without the option, or whose option names
the baseline, reads nothing for it; a set the build's clang gives the CPU that the check cannot read
fails the build with a text that names it; and on a 64-bit Arm target such an option is
`not supported` (§2, §13, the owner, 2026-11-02). The run setting turns sets off for a run, as .NET's
`DOTNET_EnableHWIntrinsic=0` does: the variable `KURZ_INTRINSICS` of the environment set to `0` turns
off every set beyond the baseline, and `KURZ_INTRINSICS_<SET>`, the name of a class under `X86`
upper-cased, `KURZ_INTRINSICS_SSE41` for `X86.Sse41`, the class's set and every set whose class derives
from it, while the variable of a class of the baseline, `X86.X86Base`, `X86.Sse` or `X86.Sse2`, turns
nothing off, not even the sets whose classes derive from it, as `KURZ_INTRINSICS=0` keeps the baseline,
where .NET's turns its baseline off too (the owner, 2026-11-02); they are read once, with the CPU, in a program that reads the CPU: `KURZ_INTRINSICS` and the variables of
the seven classes a test reads the CPU for, `X86.Sse3`, `X86.Ssse3`, `X86.Sse41`, `X86.Sse42`, `X86.Aes`,
`X86.Pclmulqdq` and `X86.Sha`, where a test reads it, and for each set the start check reads,
`KURZ_INTRINSICS` and the variables of the set's class and of the classes beyond the baseline it derives
from *(assumed: as §13 marks it)*; and a value other than `0`,
and a name that names no class, turns nothing off *(assumed: the names and that only `0` acts, said
back to the owner, as §13 marks them)*. A test of a set the setting turned off is `false`, and a set of
the build's CPU option that it turned off fails the start check, whose line names the variable after
the set, `avx2 turned off by KURZ_INTRINSICS_AVX2` (§13, the owner, 2026-11-02); where it names several
sets, the line is grouped: the sets the CPU lacks first, then one group for each variable that turned
off sets the CPU has, the groups apart by
`; `, `error: cpu-lacks-set (x86-64-v4: avx512f, avx512bw; avx2 turned off by KURZ_INTRINSICS_AVX2)`
(§13, the owner, 2026-11-02). In the list of the sets the CPU lacks and in each group the sets stand in
the order of the start check's table, LLVM's for `native`; the groups stand in the order of their
variable's class's own set in that table, the group of `KURZ_INTRINSICS` last; and a set that several
variables turned off stands in the group of the nearest of them: its class's variable, else the
variable of the nearest class its class derives from, else `KURZ_INTRINSICS` (the owner, 2026-11-02).
A trace and the line tables name the function a guarded block is compiled
into after the function it stands in, `<function>.guarded`, `Pick.guarded` for a block in `Pick`, in the
pattern of a lambda's `main.lambda` (§13, the owner, 2026-11-02). The function is the one written in the
source: a block in the method `Twice` of `Doubler` is `Doubler.Twice.guarded`, one in a lambda written in
`main` is `main.lambda.guarded`, and a lambda written in a guarded block of `main` is `main.lambda`, as
outside it *(assumed: as §13 marks them)*. A guarded block written in a guarded block adds its own mark:
one in a guarded block of `Pick` is `Pick.guarded.guarded` (§13, the owner, 2026-11-03).

Case: [memory/intrinsic-guarded.kz](../corpus/memory/intrinsic-guarded.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    print(X86.Sse41.Extract(v, 2))
} else {
    print(v.GetElement(2))
}
```

Case: [memory/intrinsic-unguarded.kz](../corpus/memory/intrinsic-unguarded.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
// an intrinsic that no test of its class's `IsSupported` guards, outside a `raw` block, is intrinsic-unguarded (R18)
print(X86.Sse41.Extract(v, 2))
```

Case: [memory/intrinsic-immediate.kz](../corpus/memory/intrinsic-immediate.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    // the index of Extract is a `byte` in C#: the constant 256 does not fit it (R18)
    print(X86.Sse41.Extract(v, 256))
}
```

Case: [memory/intrinsic-lane-out-of-range.kz](../corpus/memory/intrinsic-lane-out-of-range.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    // 4 fits a `byte`, and is the first lane past the four lanes of an `int` vector (R18)
    print(X86.Sse41.Extract(v, 4))
}
```

Case: [memory/intrinsic-create-count.kz](../corpus/memory/intrinsic-create-count.kz)
```kurz
// `Create` takes one value, or one for each of the four lanes of an `int` vector (R18)
print(Vector128.Create(1, 2, 3).GetElement(0))
```

Case: [memory/intrinsic-redeclared.kz](../corpus/memory/intrinsic-redeclared.kz)
```kurz
// a file's own `X86`, `Arm`, `Vector128` or `Vector256`, a type, a function, a variable or a parameter, is redeclared, while a field
// takes the name, a parameter of a primary constructor among them (R18)
data Vector128(int X)
```

Case: [memory/intrinsic-field.kz](../corpus/memory/intrinsic-field.kz)
```kurz
// a field takes the name `X86`, `Arm`, `Vector128` or `Vector256`, a parameter of a primary constructor among them (R18)
data Lanes(int X86, int Vector128)
class Unit(int Arm, int Vector256)

lanes = Lanes(1, 2)
print(lanes.X86 + lanes.Vector128)
unit = Unit(3, 1)
print(unit.Arm + unit.Vector256)
```

Case: [memory/intrinsic-element-range.kz](../corpus/memory/intrinsic-element-range.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
// `GetElement` past the four lanes raises index-out-of-range, where C# throws ArgumentOutOfRangeException (R18)
print(v.GetElement(4))
```

Case: [memory/intrinsic-baseline.kz](../corpus/memory/intrinsic-baseline.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
// every x86-64 CPU has SSE2, and its intrinsic still stands only under a test or in `raw` (R18)
print(X86.Sse2.Add(v, v).GetElement(2))
```

Case: [memory/intrinsic-and.kz](../corpus/memory/intrinsic-and.kz)
```kurz
bool Shown(int lane) {
    print(lane)
    return true
}

v = Vector128.Create(1, 2, 3, 4)
// the right side of `&&` after a test is guarded, and prints the lane it extracted (R18)
found = X86.Sse41.IsSupported && Shown(X86.Sse41.Extract(v, 2))
if !found {
    print(v.GetElement(2))
}
```

Case: [memory/intrinsic-not-else.kz](../corpus/memory/intrinsic-not-else.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if !X86.Sse41.IsSupported {
    print(v.GetElement(2))
} else {
    // `!` turns the test around, so this `else` block is guarded (R18)
    print(X86.Sse41.Extract(v, 2))
}
```

Case: [memory/intrinsic-derived.kz](../corpus/memory/intrinsic-derived.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    // a test of `X86.Sse41` guards the intrinsics of the classes it derives from, `X86.Ssse3` among them (R18)
    print(X86.Ssse3.AlignRight(v, v, 4).GetElement(2))
} else {
    print(v.GetElement(3))
}
```

Case: [memory/intrinsic-in-else.kz](../corpus/memory/intrinsic-in-else.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    print(v.GetElement(2))
} else {
    // the `else` block of `if X86.Sse41.IsSupported` guards nothing (R18)
    print(X86.Sse41.Extract(v, 2))
}
```

Case: [memory/intrinsic-other-class.kz](../corpus/memory/intrinsic-other-class.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Aes.IsSupported {
    // a test of `X86.Aes` guards nothing of `X86.Sse41`, from which C# does not derive `Aes` (R18)
    print(X86.Sse41.Extract(v, 2))
}
```

Case: [memory/intrinsic-early-return.kz](../corpus/memory/intrinsic-early-return.kz)
```kurz
int Third(Vector128<int> lanes) {
    if !X86.Sse41.IsSupported {
        return lanes.GetElement(2)
    }
    // the rest of the block after a test turned around whose block always leaves is guarded (R18)
    return X86.Sse41.Extract(lanes, 2)
}

print(Third(Vector128.Create(1, 2, 3, 4)))
```

Case: [memory/intrinsic-other-target-checked.kz](../corpus/memory/intrinsic-other-target-checked.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
b = Vector128.Create(byte(1))
if Arm.AdvSimd.IsSupported {
    // the test of a class under `Arm` is `false` on x86-64 and its block is still checked: a call of lanes that fit none of its functions is reported (R18)
    print(Arm.AdvSimd.Add(v, b).GetElement(2))
}
```

Case: [memory/intrinsic-in-raw.kz](../corpus/memory/intrinsic-in-raw.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
raw {
    // no test guards it in a `raw` block; SSE2 is in every x86-64 CPU, and a case is read for x86-64 (R11)
    print(X86.Sse2.Add(v, v).GetElement(2))
}
```

Case: [memory/intrinsic-other-target.kz](../corpus/memory/intrinsic-other-target.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
// a case is read for x86-64 (R11), where the test of a class under `Arm` is `false`; its block is still checked (R18)
if Arm.AdvSimd.IsSupported {
    print(Arm.AdvSimd.Add(v, v).GetElement(2))
} else {
    print(v.GetElement(2))
}
```

### R8 (decided, §3) Fields of interface type

A `mut` field of interface type can hold any implementer, so R2 is judged with the whole program
in view. A field of interface type without `mut` reaches every implementer as well, so a ring
through it is refused where another field on the ring is `mut`, as R2 and R4 read a field of a
base's type *(assumed, §3: proposed on #110, comment 6567439435, and not objected to in the
answer, comment 6573113786)*.

Case: [memory/cycle-through-interface.kz](../corpus/memory/cycle-through-interface.kz)
```kurz
interface Shape {
    int Area()
}

class Group(mut List<Shape> Parts) : Shape {
    pub int Area() => Parts.Count
}

g = Group(List<Shape>())
print(g.Area())
```

Case: [memory/interface-field.kz](../corpus/memory/interface-field.kz)
```kurz
interface Shape {
    int Area()
}

class Square(int Side) : Shape {
    pub int Area() => Side * Side
}

class Canvas(mut List<Shape> Parts)

c = Canvas(List<Shape>())
c.Parts.Add(Square(3))
print(c.Parts[0].Area())
```
