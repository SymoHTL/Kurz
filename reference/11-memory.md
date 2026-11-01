# 11. Memory and references

### R1 (decided, §3) No lifetimes, no collector

There are no lifetimes, no borrow annotations and no garbage collector. The compiler inserts
reference counts and removes most of them at compile time.

No case: what a program prints of it, when a `weak` reference finds its instance freed, the
cases of R3 show.

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
instance a lambda in the program captures, as R8 treats a field of interface type; R2's `mut`
field can be the function field or any other field on the path. With
`class Button(string Name, mut () => void OnClick)` and a `b.OnClick = () => print(b.Name)`
anywhere in the program, `Button` reaches itself, and the class is `reference-cycle`; with
`class Holder(mut Button? B)` and an immutable `() => void` field of `Button` whose lambda reads a
`Holder`, the ring runs through the immutable field and `Holder`'s `mut` one, and the first class
on the path is the error. The owner chose this on 2026-10-03, against a weak capture inside
stored lambdas, which would need a rule for what a gone capture reads as and a check at every
use, and against forbidding such a capture. *(assumed: that a field without `mut` counts, which
the second ring needs)* The cost is not local: once a class has a field of function type, any
lambda anywhere in the program that captures an instance of that class, or of a class that holds
one, makes it an error, and the lambda can sit in another file. An event handler escapes the
error only in a program in which no lambda captures the button's class: through an interface
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

### R3 (decided, §3) `weak`

A `weak` reference points at an instance without keeping it alive. It is nullable, and it is
null once its target has been freed. A field that is `weak` is not a strong field for R2. When a
target is freed follows from what holds it: a variable holds its instance until the end of the
block that declares the variable, a field as long as its owner lives, and a temporary until the
end of its statement; an instance is freed when the last of these ends. So in the case under N9
the `o` that is declared at the top level keeps `Ann` alive while `p.Keeper` is read. *(the
record marks the lifetimes as assumed: proposed on 2026-10-03, against releasing a variable after
its last use, which would free `Ann` before the print)*

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
marks them)*. A register class is read against the table of the target the build compiles for, so
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

### R12 (decided, §3) A function with no frame

A function marked `naked` before its signature, `naked void Switch(...)`, gets no prologue and no
epilogue from the compiler; its body is one `asm` statement (R11), and it compiles only in a package
granted `allow raw` (§3, the owner, 2026-10-21); its body counts as a `raw` block (§3, an assumption
there). One in a package without the grant, or in a program without a project file, which has no grant (R7), is the compile
error `naked-not-allowed`, and its body, a `raw` block, raises no `raw-not-allowed` beside it *(assumed:
the id and the one error, as §3 marks them)*.
Its `asm` takes `sym` and `const` operands alone, its instructions return or never end, and the
function is called by the C calling convention of its target *(assumed: as §3 marks them)*. A body
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
C#'s names, the class of x86's SHA set is `X86.Sha`, and the operations of `Vector128<T>` and
`Vector256<T>` on their lanes, `Vector128.Create(1, 2, 3, 4)` and `v.GetElement(2)`, are no
intrinsics and need no test *(assumed: as §13 marks them)*. The functions of `X86.Sha` are the seven
of .NET's closed proposal, `Sha256TwoRounds` among them, on `Vector128<uint>` (§13, the owner,
2026-11-01, "Proposal names, uint lanes").

An intrinsic stands where a test of its class's `IsSupported` guards it, as a test narrows a
nullable variable (N3), and in a `raw` block (R7) (§13, the owner, 2026-11-01, "Checker narrows"):
in the block of `if X86.Sse41.IsSupported { }`, on the right of `X86.Sse41.IsSupported && ...`, and
after an `if !X86.Sse41.IsSupported { }` whose block always leaves, for the rest of the block that
holds the `if`; `!` turns a test around, so that the `else` block of `if !X86.Sse41.IsSupported { }`
is guarded; a test of a class guards the intrinsics of the classes C# derives it from,
`X86.Avx2.IsSupported` those of `X86.Sse41`, while the `else` block of `if X86.Sse41.IsSupported { }`
and a test of another class guard nothing *(assumed: as §13 marks them)*. Anywhere else an intrinsic is the compile error
`intrinsic-unguarded` *(proposed: the id)*. The compiler compiles a guarded block as a function of
its own for the sets its tests name, and every function the block reaches by direct calls, the
runtime's included, again for them, in which the
tests of those sets are `true`; where the build names a CPU that has a set, its test is `true`
everywhere (§2, §13, the owner, 2026-11-01, "Guarded block", "Copied per feature" and "Transitive,
all").
`IsSupported` of a set the build's target does not have is `false`, and its block is still
checked; a case is read for x86-64 (R11), where the test of every class under `Arm` is `false`
*(assumed: as §13 marks it)*.

The checker does not read the names `X86`, `Arm`, `Vector128` and `Vector256`, and says
`not supported` to them, so a case of this rule runs through the front end alone; what it prints
and the error it expects are read against this rule.

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

Case: [memory/intrinsic-in-else.kz](../corpus/memory/intrinsic-in-else.kz)
```kurz
v = Vector128.Create(1, 2, 3, 4)
if X86.Sse41.IsSupported {
    print(v.GetElement(2))
} else {
    // the `else` block of a test guards nothing (R18)
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
    return X86.Sse41.Extract(lanes, 2)
}

v = Vector128.Create(1, 2, 3, 4)
print(Third(v))
if X86.Sse41.IsSupported {
    // the copy of `Third` compiled for SSE4.1, in which its test is `true` (R18)
    print(Third(v))
} else {
    print(Third(v))
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
in view.

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
