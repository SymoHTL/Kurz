# 11. Memory and references

### R1 (decided, §3) No lifetimes, no collector

There are no lifetimes, no borrow annotations and no garbage collector. The compiler inserts
reference counts and removes most of them at compile time.

No case: it changes how a program runs, not what it prints.

### R2 (decided, §3) A possible reference cycle is an error

Judged by types, over the whole program: a class may not reach itself through strong fields when
one field on that path is `mut`. That is the compile error `reference-cycle`. A path without a
`mut` field is allowed, because immutable data cannot form a cycle.

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

### R3 (decided, §3) `weak`

A `weak` reference points at an instance without keeping it alive. It is nullable, and it is
null once its target has been freed. A field that is `weak` is not a strong field for R2.

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

### R5 (open) Is the `?` written on a `weak` type

A weak reference is always nullable. The record's one sample writes it both ways:
`List<weak Node>` and `weak Node? parent`. Options: (a) `weak T` is nullable by itself and `?`
is not written; (b) `?` is always written, `weak T?`. Lean: (b), so that every nullable type in
a program shows its `?`. The corpus writes `weak T?` and uses no `weak` inside a collection until
this is answered.

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
the project grants `allow raw`. The compiler's memory guarantees cover everything outside `raw`
blocks. That the grant is per package and written like `allow network` is *(assumed)* in the
record.

No case: a grant sits in `project.kz`, and the corpus holds single files so far.

### R8 (decided, §3) Fields of interface type

A `mut` field of interface type can hold any implementer, so R2 is judged with the whole program
in view.

No case: how an interface is written is open (K7).
