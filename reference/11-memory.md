# 11. Memory and references

### R1 (decided, §3) No lifetimes, no collector

There are no lifetimes, no borrow annotations and no garbage collector. The compiler inserts
reference counts and removes most of them at compile time.

No case: it changes how a program runs, not what it prints.

### R2 (decided, §3) A possible reference cycle is an error

Judged by types, over the whole program: a class may not reach itself through strong fields when
one field on that path is `mut`. That is the compile error `reference-cycle`. A path without a
`mut` field is allowed, because immutable data cannot form a cycle. A field of function type
(F11) is a strong field whose type names no class, yet the lambda it holds can capture any
instance: how such a field counts is R9.

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

### R9 (open) Fields of function type in the cycle rule

`class Button(string Name, mut () => void OnClick)` with `b.OnClick = () => print(b.Name)` closes
the ring b, OnClick, the lambda, b, and no field type on that path names `Button`, so R2 as
written accepts the program and the ring leaks, although R7 promises the memory guarantees for
everything outside `raw`. The options:

- (a) A `mut` field of function type counts as a field that can reach every class whose instance
  a lambda in the program captures, as R8 treats a field of interface type: the ring above is
  `reference-cycle`, and a `mut` function field in a class that any lambda anywhere can capture
  is one as well. Cost: such fields become rare, and an event handler is written as an interface
  (R8) or through a `weak` capture the record does not have yet.
- (b) A lambda stored in a field captures class instances weakly, so the ring never forms; a
  captured instance can be gone when the lambda runs, and the lambda sees `null` for it. Cost: a
  rule for what a captured `weak` reference reads as, and a check at every use.
- (c) A lambda may not capture a class instance at all when it is stored in a field. Cost: the
  handler above has to receive the button as a parameter.

The lean is (a): it is R8's rule applied once more, and nothing new has to be invented.

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

Code that touches raw memory sits in `raw` blocks. A `raw` block compiles only in a package that
the project grants `allow raw`; one in a package without the grant is the compile error
`raw-not-allowed`, and a program without a `project.kz`, as every case of the corpus is, has no
grant. The compiler's memory guarantees cover everything outside `raw` blocks. The grant is per
package and written like `allow network`. *(the error and the program without a project file are
a proposed reading)* No case shows a `raw` block that compiles: a grant sits in `project.kz`, and
a case of the corpus is a single file.

Case: [memory/raw-not-allowed.kz](../corpus/memory/raw-not-allowed.kz)
```kurz
raw {
    print(1)
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
