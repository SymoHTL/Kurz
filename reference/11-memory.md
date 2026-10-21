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
`raw-not-allowed`, and a program without a `project.kz`, as every case of the corpus is, has no
grant. A `raw` block holds the machine instructions no other form of the language expresses, as
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
a project file compiles with them *(assumed: as §3 marks it)*. No
case shows a `raw` block that compiles: a grant sits in `project.kz`, and a case of the corpus is a
single file.

Case: [memory/raw-not-allowed.kz](../corpus/memory/raw-not-allowed.kz)
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
§3 marks them; `counted-pointee` stands here and not in the table of chapter 12)*.

No case: a pointer is read or written in a `raw` block only, which compiles only under a grant in
`project.kz` (R7), and a case of the corpus is a single file; a case of `raw-required` through a pointer needs a pointer
form, and the compiler's front end, through which every case runs, reads none: a pointer type, `&x`
and `*p` all get `syntax` there, since it takes `-`, `!` and `~` alone as prefix operators.

### R11 (decided, §3) Inline assembly

Inline assembly is a statement inside a `raw` block, written as Rust writes `asm!`:
`asm("add {r}, 1", r = inout(reg) count)`. A `{name}` in its template names an operand and is no
interpolation (L6); a `"""` block (L13) holds a template of many lines; the operands take Rust's
words, `in`, `out`, `inout`, `lateout`, the register classes Rust names for each architecture and
`clobber_abi` (§3, the owner, 2026-10-21). `{{` and `}}` write a brace, an operand is taken by its name or by
its position, a modifier after a colon gives the form of its register, `{r:e}`, every operand appears in
the template, the operands take the rest of Rust's words, `inlateout`, `const`, `sym`, `label` and
`options(...)`, and a variable an operand writes is `mut` *(assumed: the rest of Rust's rules of the
template and of its words, as §3 marks them)*. On WebAssembly and on Xtensa the register classes are
those of Rust's experimental support for them, `local` on wasm32 and `reg`, `freg`, `sreg` and
`breg` on Xtensa; a breach of the template's rules is the compile error `asm-operand`, and an operand
that writes a variable without `mut` is `assign-immutable`, as any assignment to it is *(assumed: as §3
marks them; `asm-operand` stands here and not in the table of chapter 12)*. An `asm` outside a `raw` block and outside the body of a
`naked` function (R12) is the compile error `raw-required` (R10) *(assumed: the id, as §3 marks it)*.
`asm` is a core word (L18). No case shows an `asm` that compiles: it stands only in a `raw` block,
which compiles only under a grant in `project.kz` (R7), and a case of the corpus is a single file. The
case below runs through the compiler's front end, which reads `asm("nop")` with no error and checks
only the ids it raises itself, as for every case whose id another part of the compiler raises.

Case: [memory/asm-outside-raw.kz](../corpus/memory/asm-outside-raw.kz)
```kurz
asm("nop")
// an `asm` outside a `raw` block and outside a `naked` function is raw-required (R11); the front end parses this line as a call with no error, and the id is raised after the parse
```

### R12 (decided, §3) A function with no frame

A function marked `naked` before its signature, `naked void Switch(...)`, gets no prologue and no
epilogue from the compiler; its body is one `asm` statement (R11), and it compiles only in a package
granted `allow raw` (§3, the owner, 2026-10-21); its body counts as a `raw` block (§3, an assumption
there). One in a package without the grant, or in a program without a project file, which has no grant (R7), is the compile
error `naked-not-allowed`, and its body, a `raw` block, raises no `raw-not-allowed` beside it *(assumed:
the id and the one error, as §3 marks them; the id stands here and not in the table of chapter 12)*.
Its `asm` takes `sym` and `const` operands alone, its instructions return or never end, and the
function is called by the C calling convention of its target *(assumed: as §3 marks them)*. A body
other than one `asm`, or an operand there other than `sym` and `const`, is the compile error
`naked-body`, and instructions that fall through the function's end are a misuse of `raw` code, which
no compiler checks *(assumed: as §3 marks them; the id stands here and not in the table of chapter
12)*. `naked` is a core word (L18).

No case: the grant sits in `project.kz` (R7), and a case of the corpus is a single file; a case of
`naked-not-allowed` needs a `naked` function, at which the compiler's front end, through which every
case runs, reports `syntax`, so that such a case fails there.

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
