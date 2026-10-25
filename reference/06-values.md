# 6. Values and change

### M1 (decided, §4) What a value is

`data` values, collections, strings and numbers are values. A class instance is a reference with
identity (chapter 7).

Case: [values/copy-on-assign.kz](../corpus/values/copy-on-assign.kz)
```kurz
mut a = List<int>()
a.Add(1)
b = a
a.Add(2)
print(a.Count)
print(b.Count)
```

### M2 (decided, §4) Without `mut`, nothing changes

Without `mut`, nothing reachable through a variable changes. With `mut`, it changes in place.
This is about values. It does not bind a class instance that is reached through the variable
(K2).

Case: [values/mut-method-on-immutable.kz](../corpus/values/mut-method-on-immutable.kz)
```kurz
xs = List<int>()
xs.Add(1)
print(xs.Count)
```

### M3 (decided, §4) Assigning copies, in meaning

Assigning a value or passing it gives the other side its own value: a later change on one side
is not seen on the other. How much is really copied is the compiler's business; storage is shared
until one side writes.

Case: [values/copy-on-assign.kz](../corpus/values/copy-on-assign.kz)

### M4 (decided, §4) Assigning into a path

Assigning to a field at the end of a path that starts at a `mut` variable changes that variable's
value. It stores what nested `with` (D5) over the variable stores, with the variable read after the value: the value is evaluated first, then the path, in the
variable as the value left it: a `mut` argument in the value that changes the variable keeps its
change (§4, the owner, 2026-10-24). A `mut` method takes its receiver as `mut` (M5), and M7 holds the receiver while
the method's arguments run, so a call of one on the variable whose argument changes the variable,
`xs.Add(F(mut xs))`, is M7's `changed-while-held`. An assignment into a path of an immutable
variable is the compile error `assign-immutable` (D4). A class instance on the path starts the path anew: from a `mut` field of
the instance on, the rest of the path is as if it started at a `mut` variable, whatever holds the
instance (K2), and a field of the instance that is not `mut` ends the path as an immutable variable
would (K1). *(proposed reading of K2 for paths)*

Case: [values/path-assign-order.kz](../corpus/values/path-assign-order.kz)
```kurz
data Address(string City)
data Customer(string Name, Address Home)

string Move(mut Customer moved) {
    moved = Customer("Bea", Address("Linz"))
    return "Wien"
}

mut c = Customer("Ann", Address("Graz"))
c.Home.City = Move(mut c)
print(c.Name)
print(c.Home.City)
```

Case: [values/path-assign.kz](../corpus/values/path-assign.kz)
```kurz
data Address(string City)
data Customer(string Name, Address Home)

mut c = Customer("Ann", Address("Graz"))
before = c
c.Home.City = "Wien"
print(c.Home.City)
print(before.Home.City)
```

### M5 (decided, §4) Methods that change their own value

A method that changes the value it is called on carries a `mut` marker (M6). Calling it on a value
that is not reachable through a `mut` variable is the compile error `mut-required`. A value held
in a `mut` field of a class instance is reachable through that field from every reference to the
instance (K2, M4), so `node.Children.Add(x)` compiles for an immutable `node` whose class declares
`mut List<Node> Children`. *(proposed reading of K2 for paths)*

Case: [values/mut-method-on-immutable.kz](../corpus/values/mut-method-on-immutable.kz)

Case: [values/mut-method-through-field.kz](../corpus/values/mut-method-through-field.kz)
```kurz
class Node(string Name, mut List<string> Children)

node = Node("root", List<string>())
node.Children.Add("leaf")
print(node.Children.Count)
```

### M6 (decided, §4) Where the `mut` marker of a method is written

In front of the return type: `mut void Add(T item)`. `mut` stands in front everywhere else as
well: of a variable, a field and a parameter.

Case: [values/mut-method.kz](../corpus/values/mut-method.kz)
```kurz
data Counter(int Count) {
    pub mut void Increment() {
        Count = Count + 1
    }
}

mut c = Counter(0)
before = c
c.Increment()
c.Increment()
print(c.Count)
print(before.Count)
```

### M7 (decided, §4) `mut` parameters

A function changes a value of its caller through a parameter marked `mut`, and the caller
writes `mut` in front of the argument as well; the other way is a `mut` method (M5), whose receiver
is the value it changes. Calling a `mut` method (M5) on a parameter that is
not `mut`, or passing an immutable variable as `mut`, is the compile error `mut-required`;
assigning to such a parameter is F8's `assign-immutable`, as assigning to any immutable variable
is. Leaving `mut` out at the call is the compile error `mut-at-call`. What may stand after `mut` at
a call is a `mut` variable or a path from one, through fields of `data` values, to the value that
is passed (`Fill(mut c.Items)`), or a `mut` field of a class instance (M4); a call result or a
literal cannot stand there, and `mut` in front of an argument whose parameter is not `mut` is
`mut-at-call` as well. Passing one variable twice as `mut` (`Swap(mut a, mut a)`), or a variable
and a path into it, is `mut-at-call` too: each `mut` parameter is the only way its value changes
while the function runs (M3). *(proposed: the forms after `mut`, and the two further uses of
`mut-at-call`)*

Within one expression, a read of what a call takes as `mut`, of a value that holds it or of one it
holds (the variable itself, a path of fields, an item of a list), is the compile error
`changed-while-held` where the expression still holds the read's value when the call runs (§4, the
owner, 2026-10-24 and 2026-10-25). A call takes as `mut` what stands after `mut` among its arguments,
and a `mut` method its receiver (M5), which the method changes after its own arguments ran, so a read
of the receiver in them holds nothing while it changes: `rows.Add(rows[0])` for a list of lists `rows`
passes, and `xs.Add(F(mut xs))` is the error (§4, the owner, 2026-10-24 and 2026-10-25). The
expression holds the value of a read made as any other argument of that call without `mut` in front
of it, before or after the argument after `mut` (a second argument after `mut` that reaches the same
value is `mut-at-call`, above); as an argument of a call around it given before
the argument the call stands in; as the receiver of a method or the target of `with` or of an index
around the call; as an operand of an operator around it written before the operand the call stands
in; and in a lambda made before the call that reads the variable, which holds the variables it reads from
the moment it is made (F11, F15), whatever its body makes of them, so `() => numbers.Count` holds
`numbers` and the reads in its body are not reads after the call (§4, the owner, 2026-10-24). An assignment of the variable or of a path into it, and a `mut` method
called on either, in a statement of an `else` arm's block inside the expression, nested statements
among them, change it as such a call does while the expression holds the read's value (§4,
the owner, 2026-10-25). That block is the one an arm has inside an expression: an arm of a `match`
used as a value is its value (C9), and a block there is `syntax`, as `compiler/README.md` reads C9. Three reads hold no such
value. A read that makes a new value first: the result of an operator other than `??`, whose value
is one of its sides, and of a call or a method (`numbers.Count`, `numbers[0] + 1`, `Same(numbers)` for
a function that returns its list), where an index is no such operator and its item is held (above)
*(proposed: the result of a call is a value of its own, M3)*. A read that runs after the call has ended. A read of
a copied type, which the expression holds as a copy: a number, a `bool`, a `char`, a pointer, a time,
the type of a C function, and a `data` value or a union made only of such types that holds no value of
its own type, so that a lambda that reads an `int` holds the value from before the call (§4, the
owner, 2026-10-24 and 2026-10-25). A list, a `string`, a type parameter and a `data` type that holds a
value of its own type are not copied; a function value is, and so is a `data` value or a union made only
of function values and copied types (§4, the owner, 2026-10-25). Two fields of one
variable stay two values. The error stands at the line of what changes the value, an argument after
`mut`, the call of a `mut` method or an assignment, once for each of them however many reads it meets.
*(assumed, as §4 marks it: one error per change, E1; a `data` type is copied where every type derived
from it is; a hole of an interpolation makes a new value)* The reads of an expression run left to
right as written: a call's receiver before its arguments, an operand before the one after it, and the
target of `with` or of an index before what follows it, as F12 has a call's arguments and as Rust
evaluates operands, so a copied read before a `mut` call holds the value from before the call. The
subject of a `match` and the call of an `else` hold nothing for the arms but the names the arms bind,
which are variables of their own (M3), so an arm may pass the subject's variable as `mut`. *(proposed:
a call's receiver before its arguments, an operand before the one after it and the target of `with`
and of an index before what follows it, where F12 names arguments alone; the arms, which §4 does not
name)*

Case: [values/mut-parameter.kz](../corpus/values/mut-parameter.kz)
```kurz
void Fill(mut List<int> xs) {
    xs.Add(1)
}

mut numbers = List<int>()
Fill(mut numbers)
print(numbers.Count)
```

Case: [values/parameter-not-mut.kz](../corpus/values/parameter-not-mut.kz)
```kurz
void Fill(List<int> xs) {
    xs.Add(1)
}

numbers = List<int>()
Fill(numbers)
print(numbers.Count)
```

Case: [values/call-without-mut.kz](../corpus/values/call-without-mut.kz)
```kurz
void Fill(mut List<int> xs) {
    xs.Add(1)
}

mut numbers = List<int>()
Fill(numbers)
print(numbers.Count)
```

Case: [values/mut-argument-immutable.kz](../corpus/values/mut-argument-immutable.kz)
```kurz
void Fill(mut List<int> xs) {
    xs.Add(1)
}

numbers = List<int>()
Fill(mut numbers)
print(numbers.Count)
```

Case: [values/mut-read-argument.kz](../corpus/values/mut-read-argument.kz)
```kurz
int Grow(mut List<int> xs) {
    xs.Add(5)
    return xs.Count
}

int Sum(List<int> xs, int n) => xs.Count + n

mut numbers = List<int>()
print(Sum(numbers, Grow(mut numbers)))
```

Case: [values/mut-read-same-call.kz](../corpus/values/mut-read-same-call.kz)
```kurz
void Fill(mut List<int> xs, List<int> seen) {
    xs.Add(seen.Count)
}

mut numbers = List<int>()
Fill(mut numbers, numbers)
print(numbers.Count)
```

Case: [values/mut-read-receiver.kz](../corpus/values/mut-read-receiver.kz)
```kurz
int Grow(mut List<int> xs) {
    xs.Add(5)
    return xs.Count
}

mut numbers = List<int>()
numbers.Add(Grow(mut numbers))
print(numbers.Count)
```

Case: [values/mut-read-with.kz](../corpus/values/mut-read-with.kz)
```kurz
data Cart(List<int> Items, int Total)

int Clear(mut Cart c) {
    c = Cart(List<int>(), 0)
    return 1
}

mut cart = Cart(List<int>(), 0)
next = cart with { Total = Clear(mut cart) }
print(next.Total)
```

Case: [values/mut-read-index.kz](../corpus/values/mut-read-index.kz)
```kurz
int Last(mut List<int> xs) {
    xs.Add(5)
    return xs.Count - 1
}

mut numbers = List<int>()
numbers.Add(4)
print(numbers[Last(mut numbers)])
```

Case: [values/mut-read-operand.kz](../corpus/values/mut-read-operand.kz)
```kurz
data Cart(List<int> Items)

Cart Cleared(mut Cart c) {
    c = Cart(List<int>())
    return c
}

mut cart = Cart(List<int>())
print(cart == Cleared(mut cart))
```

Case: [values/mut-read-item.kz](../corpus/values/mut-read-item.kz)
```kurz
int Clear(mut List<List<int>> all) {
    all = List<List<int>>()
    return 0
}

int Sum(List<int> row, int n) => row.Count + n

mut rows = List<List<int>>()
rows.Add(List<int>())
print(Sum(rows[0], Clear(mut rows)))
```

Case: [values/mut-read-lambda.kz](../corpus/values/mut-read-lambda.kz)
```kurz
int Grow(mut List<int> xs) {
    xs.Add(5)
    return xs.Count
}

int Apply(() => int f, int n) => f() + n

mut numbers = List<int>()
print(Apply(() => numbers.Count, Grow(mut numbers)))
```

Case: [values/mut-read-add-item.kz](../corpus/values/mut-read-add-item.kz)
```kurz
mut rows = List<List<int>>()
mut row = List<int>()
row.Add(3)
rows.Add(row)
row = List<int>()
rows.Add(rows[0])
print(rows[1])
print(rows.Count)
```

Case: [values/mut-read-arm-change.kz](../corpus/values/mut-read-arm-change.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

int Sum(List<int> xs, int n) => xs.Count + n

mut numbers = List<int>()
print(Sum(numbers, Find(2) else {
    NotFound => {
        numbers = List<int>()
        5
    }
}))
```

Case: [values/mut-read-copied.kz](../corpus/values/mut-read-copied.kz)
```kurz
data Point(int X, int Y)

int Bump(mut int count) {
    count = count + 10
    return count
}

int Shift(mut Point point) {
    point = Point(7, 8)
    return 5
}

int Pair(int a, int b) => a * 100 + b

mut n = 1
print(Pair(n, Bump(mut n)))
print(n + Bump(mut n))
mut p = Point(1, 2)
q = p with { X = Shift(mut p) }
print(q.Y)
print(p.Y)
mut xs = List<int>()
xs.Add(3)
xs.Add(4)
xs.Add(xs[0])
print(xs)
```

Case: [values/mut-read-new-value.kz](../corpus/values/mut-read-new-value.kz)
```kurz
int Grow(mut List<int> xs) {
    xs.Add(5)
    return xs.Count
}

int Pair(int a, int b) => a * 10 + b

List<int> Same(List<int> all) => all

int Total(List<int> all, int n) => all.Count * 10 + n

mut numbers = List<int>()
numbers.Add(4)
print(Pair(numbers.Count, Grow(mut numbers)))
print(Pair(numbers[0] + 1, Grow(mut numbers)))
numbers.Add(numbers.Count)
print(numbers)
print(Total(Same(numbers), Grow(mut numbers)))
```

Case: [values/mut-read-after.kz](../corpus/values/mut-read-after.kz)
```kurz
int Grow(mut List<int> xs) {
    xs.Add(5)
    return xs.Count
}

int Sum(int n, List<int> xs) => n * 10 + xs.Count

mut numbers = List<int>()
numbers.Add(4)
print(Sum(Grow(mut numbers), numbers))
print(Grow(mut numbers) + numbers.Count)
```

Case: [values/mut-read-arms.kz](../corpus/values/mut-read-arms.kz)
```kurz
data Empty
data NotFound

int Swap(mut List<int> | Empty v) {
    v = Empty
    return 1
}

List<int> | Empty Make() {
    mut xs = List<int>()
    xs.Add(1)
    xs.Add(2)
    xs.Add(3)
    return xs
}

int | NotFound Look(List<int> all) {
    if all.Count > 3 {
        return all.Count
    }
    return NotFound
}

int Grow(mut List<int> all) {
    all.Add(5)
    return all.Count
}

mut List<int> | Empty u = Make()
print(match u {
    List<int> l => Swap(mut u) * 10 + l.Count
    Empty => 0
})
mut numbers = List<int>()
numbers.Add(1)
print(Look(numbers) else {
    NotFound => Grow(mut numbers) + 4
})
```

### M8 (decided, §4) One family of collections

There is one family of collections, and every value can cross between actors.

No case: actors are outside this reference; that collections are values is shown under M3.

### M9 (assumed, §4, §5) The collections the corpus uses

`List<T>()` makes an empty list; `Add(item)` appends and is a `mut` method; `Count` is the number
of items, an `int`, and a list holds at most as many items as the largest `int`, an `Add` that
would make it longer raising `allocation-failed` with the bytes of its items, a 64-bit number: the count
after the `Add` times the item's size in its layout, padding included, the size of a pointer for a
type laid out as a block, 8 bytes on a 64-bit target (§4, the owner, 2026-10-23 and 2026-10-24); a
growth the system refuses raises it with the bytes the allocator was asked for, the whole request with
the list's own counts, before the allocator rounds it up (§4); `list[i]` is the item at position `i`, counted from 0, and an `i` below 0 or at `Count`
or beyond raises `index-out-of-range` (T14); `Where(test)` is the list of
items that pass the test. `Map<K, V>()` makes an empty map; `map[key] = value` sets an entry, as
an assignment into a path (M4); `map[key]` is the value or `null`. The names are the ones the
record's samples use. The naming of the standard library is open (record, section 14).

Case: [values/list.kz](../corpus/values/list.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
print(xs.Count)
print(xs[1])
```

Case: [values/map.kz](../corpus/values/map.kz)
```kurz
mut ages = Map<string, int>()
ages["Ann"] = 31
print(ages["Ann"] ?? 0)
print(ages["Bea"] ?? 0)
```

Case: [values/list-index-out-of-range.kz](../corpus/values/list-index-out-of-range.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
print(xs[1])
```

### M10 (decided, §4) The order of a map's entries

A map has no order a program may rely on. A `for` loop over it (C6) and its text (A6) yield the
entries in the order of the map's storage, which can differ between runs and versions, as the
documentation of C#'s `Dictionary` leaves its order undefined. Two runs of one program can print
the same map differently.

No case: no order can be expected, so a case can show a map of one entry only, which the case of
A6 does.
