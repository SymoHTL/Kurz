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
and a `mut` method its receiver (M5). `Add` changes its receiver after its argument ran and counts
the item it stores before the list grows, so a read of the receiver in its argument holds nothing
while `Add` changes it: `rows.Add(rows[0])` for a list of lists `rows` passes, and `xs.Add(F(mut xs))`
is the error (§4, the owner, 2026-10-24 and 2026-10-25). A lambda that reads the receiver and is that
argument's value, bare, in brackets or as the value of an arm, holds it while `Add` changes it, so
`fs.Add(() => fs.Count)` for a list of function values is the error, and so is a lambda given to a
call or a `with` there that the argument's value can keep, a value of a type that can hold a function
value: `fs.Add(Wrap(() => fs.Count))` is the error (§4, the owner, 2026-10-25). A value inside the
argument of a type that can hold none ends what it keeps, so `xs.Add(Count(() => xs.Count))` and
`xs.Add(Count(Wrap(() => xs.Count)))`, whose `Count` gives an `int`, pass (§4, the owner, 2026-10-25
and 2026-10-28). `Add` lets go of a lambda's environment before the list grows, so that no count
the environment holds makes `Add` copy the items (§4, the owner, 2026-10-28, "Pass, as Rust"), and
of everything else its argument made besides its item *(assumed, as §4 marks it)*. A type that can hold a function value is a function type, a
type parameter, a list or a union that holds one, and a `data` type when one of its type arguments
holds one, whether a field stores it or not, or when a field of it or of a type derived from it
holds one (§4, the owner, 2026-10-28, "confirmed" and "Counts, as Rust"): `Q<() => int>` holds one
for any `data Q<T>`, so `qs.Add(Keep(() => qs.Count))`, with a `Keep` that gives a list of them, is
the error. A field's type is read with the type's arguments in place of its parameters, so a field
of type `T` holds what the argument for `T` holds, and a map holds one where its key or its value
type can, a nullable type where the type it makes nullable can, and a class where one of its type
arguments or a field of it or of a class derived from it can *(assumed, as §4 marks them)*. Every `mut`
method that stores what its argument gave, counted, before it changes its receiver takes `Add`'s
exception, with the conditions above, so that `xs.AddRange(xs)` appends a copy of the list (X9) (§4,
the owner, 2026-10-31, "Store-first methods"). The
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
used as a value is its value (§5, C9). *(proposed: a block as such an arm is `syntax`, E4, which §5
does not name)* Three reads hold no such
value. A read that makes a new value first: the result of an operator other than `??`, whose value
is one of its sides, and of a call or a method (`numbers.Count`, `numbers[0] + 1`, `Same(numbers)` for
a function that returns its list), where an index is no such operator and its item is held (above)
*(proposed: the result of a call is a value of its own, M3; the value of `??` is one of its sides and
the item of an index is held, which §4 does not name)*. A read that runs after the call has ended. A read of
a copied type, which the expression holds as a copy: a number, a `bool`, a `char`, a pointer, a time,
the type of a C function, and a `data` value or a union made only of such types that holds no value of
its own type, so that a lambda that reads an `int` holds the value from before the call (§4, the
owner, 2026-10-24 and 2026-10-25). A list, a `string`, a type parameter and a `data` type that holds a
value of its own type are not copied; a function value is, and so is a `data` value or a union made only
of function values and copied types (§4, the owner, 2026-10-25). Two fields of one
variable stay two values. The error stands at the line of what changes the value, an argument after
`mut`, the call of a `mut` method or an assignment, once for each of them however many reads it meets.
*(assumed, as §4 marks it: one error per change, E1; a `data` type is copied where every type derived
from it is; a hole of a type other than `string` is made text where it stands, a new value)* A hole of a `string` holds
the string it read until the interpolation is made, so a hole after it that changes what it read is the error, as
Rust's `format!` borrows its arguments; an interpolation is a new value, one of a single hole of a `string` among them,
as Rust's `format!` makes a new `String`, so a call around it holds nothing of what the hole read (§4, the owner,
2026-10-29, "Refuse, as Rust" and "New value, as format!"). The reads of an expression run left to
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

Case: [values/mut-read-lambda-add.kz](../corpus/values/mut-read-lambda-add.kz)
```kurz
mut counts = List<() => int>()
counts.Add(() => counts.Count)
print(counts.Count)
```

Case: [values/mut-read-lambda-kept.kz](../corpus/values/mut-read-lambda-kept.kz)
```kurz
() => int Wrap(() => int f) => f

mut counts = List<() => int>()
counts.Add(Wrap(() => counts.Count))
print(counts.Count)
```

Case: [values/mut-read-lambda-ends.kz](../corpus/values/mut-read-lambda-ends.kz)
```kurz
int Count(() => int f) => f()

mut xs = List<int>()
xs.Add(Count(() => xs.Count))
xs.Add(Count(() => xs.Count))
print(xs)
```

Case: [values/mut-read-lambda-nested.kz](../corpus/values/mut-read-lambda-nested.kz)
```kurz
() => int Wrap(() => int f) => f

int Count(() => int f) => f()

mut xs = List<int>()
xs.Add(Count(Wrap(() => xs.Count)))
xs.Add(Count(Wrap(() => xs.Count)))
print(xs)
```

Case: [values/mut-read-lambda-generic-depth.kz](../corpus/values/mut-read-lambda-generic-depth.kz)
```kurz
data Box<T>(T V)

mut bs = List<Box<Box<() => int>>>()
bs.Add(Box<Box<() => int>>(Box<() => int>(() => bs.Count)))
print(bs.Count)
```

Case: [values/mut-read-lambda-unused-argument.kz](../corpus/values/mut-read-lambda-unused-argument.kz)
```kurz
data Tag<T>(int Id)

Tag<() => int> Mark(() => int f) => Tag<() => int>(f())

mut ts = List<Tag<() => int>>()
ts.Add(Mark(() => ts.Count))
print(ts.Count)
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

Case: [values/mut-read-hole.kz](../corpus/values/mut-read-hole.kz)
```kurz
string Rename(mut string t) {
    t = "{t}!"
    return "b"
}

mut name = "a{1}"
print("{name}{Rename(mut name)}")
```

Case: [values/mut-read-hole-new-value.kz](../corpus/values/mut-read-hole-new-value.kz)
```kurz
string Rename(mut string t) {
    t = "{t}!"
    return "b"
}

string First(string a, string b) => a

mut name = "a{1}"
print(First("{name}", Rename(mut name)))
print(name)
print("{Rename(mut name)}{name}")
```

Case: [values/list-literal-held.kz](../corpus/values/list-literal-held.kz)
```kurz
List<int> Grow(mut List<int> list) {
    list.Add(2)
    return list
}

mut xs = [1]
print([xs, Grow(mut xs)])
```

### M8 (decided, §4) One family of collections

There is one family of collections, and every value can cross between actors.

No case: actors are outside this reference; that collections are values is shown under M3.

### M9 (decided, §4, §5, §12) The collections the corpus uses

`List<T>()` makes an empty list; `Add(item)` appends and is a `mut` method *(assumed, §4: `List<T>()`,
`Add`, `Count` and an index counted from 0, the members the record's samples use)*; `Clear()` empties a list and
keeps its room, a `mut` method too, and a list that shares its block with another value lets go of it
(M3; §12, the owner, 2026-10-29); `Count` is the number
of items, an `int`, and a list holds at most as many items as the largest `int`, an `Add` that
would make it longer raising `allocation-failed` with the bytes of its items, a 64-bit number: the count
after the `Add` times the item's size in its layout, padding included, the size of a pointer for a
type laid out as a block, 8 bytes on a 64-bit target (§4, the owner, 2026-10-23 and 2026-10-24); a
growth the system refuses raises it with the bytes the allocator was asked for, the whole request with
the list's own counts, before the allocator rounds it up (§4); `list[i]` is the item at position `i`, counted from 0, and an `i` below 0 or at `Count`
or beyond raises `index-out-of-range` (T14); `Where(test)` is the list of
items that pass the test, in their order, and calls the test once for each item, in order, so that
a test that ends the program ends it at that item and no item after it is tested (§4, the owner,
2026-10-28). `Bytes` is a second name of `List<byte>`, and a message names that type `Bytes`; `xs[a..<b]`
and `xs[a..b]` are a new list of the items from position `a` to `b`, the second with `b` included, their
items copied (M3); a start below 0 or an end past the count raises `index-out-of-range`, and a range
whose end lies below its start is `reversed-range` or `reversed-range-at-run-time`, as C8 says of a
range (§4, the owner, 2026-10-29). `Map<K, V>()` makes an empty map; `map[key] = value` sets an entry, as
an assignment into a path (M4); `map[key]` is the value or `null` *(assumed, §4: the three the samples
use)*. A map's key is a value of any type
`==` compares, a list by its items and a `data` value by its fields, hashed over the same items and
fields; a map as a key is `type-mismatch`, since `==` takes no map, and so is a `data` value that holds
a map, a function value, a file or a socket (D3), and a `double` or a `float` key, alone, as a field of a
`data` key or as an item of a list in a key, at any depth, compares as C#'s `double.Equals` in a
`Dictionary`: NaN finds NaN, and 0.0 and -0.0 are one key, hashed alike, as .NET 10's
`double.GetHashCode` hashes them (§4). A union is a key by its case and its value (D3; §4, the owner,
2026-10-30, "Case and value, as Rust"), its `double` or `float` case compared as a `double` key is, as
C#'s `Equals` (§4). A class instance is a key as K12 says: `mutable-key` where its
`equal by` names a `mut` field (§4). A write under a key equal to an entry's key that prints otherwise,
0.0 where the entry's key is -0.0, takes the new value and keeps the entry's own key, as C#'s
`Dictionary` and Rust's `HashMap::insert` keep it *(assumed, §4)*. Every `V` has its nullable form, a
union's and a function type's among them (N1), so `map[key]` and `Remove(key)` give `V?` for every `V`
(§4, the owner, 2026-10-30, "Nullable form, as Option"), a `V` that is nullable already giving that type,
in which a missing entry and an entry that holds `null` read alike and `ContainsKey` alone tells them
apart, called while the entry stands, before a `Remove` takes it out (N11; §4, the owner, 2026-10-30, "Flatten, as C#"). A key of a nullable type, with
`null` among its keys, compares as `==` compares two nullable values (N10), `null` equal to `null` alone
and two values as their type compares as a key *(assumed, §4: for the nullable types beside `double?` and
`float?`)*, and a `double?` or a `float?` key compares its value as a `double` key does. `xs == ys` on two
lists of one type is `true` where their counts are equal and their items are equal in order, each pair by
`==`, so that NaN is unequal to NaN there, also in the lists among the items, and `!=` is its negation;
lists of items `==` does not take are `type-mismatch`. Items of a nullable type compare as `==` compares
two nullable values (N10) *(assumed, §4: for the nullable types beside `double?` and `float?`)*, and the
values of `double?` and `float?` items by IEEE's `==`, as Rust's `Vec<Option<f64>>` compares. A list held
in a `data` value under `==` compares its `double` and `float` items, and the values of its `double?` and
`float?` items, as C#'s `Equals` does (D3), and so does a list in a key. A key's hash is SipHash-1-3 under keys drawn from the system's randomness (M10), taken when a thread's first map takes its first block, at that map's first write *(assumed, §4: the
moment)*, from the sources Rust's standard library takes them from: on Linux from `getrandom` with `GRND_INSECURE`, or `GRND_NONBLOCK` where the kernel refuses that flag, and from `/dev/urandom` where the system refuses `getrandom` or its pool is not ready, and on Windows from `ProcessPrng`, as Rust 1.82 takes them there, which does not fail (§4, the owner, 2026-10-30, "ProcessPrng, as Rust 1.82"; `random-failed` where its library or the call is missing, on a system older than Windows 8 or Windows Server 2008 R2, the floor Microsoft's page of the call names, assumed there); on Linux, where none of them gives the bytes, where Rust panics, the program ends there with `random-failed` (§4, the owner, 2026-10-30, "End with an error, as Rust" and "Rust's full path"), whose line is `error: random-failed`, with the exit code 1 and no place and no trace, as the line of `allocation-failed` (O11) *(assumed, §4: the id, the name the question gave as an example, and the shape of its line)*. `Count` is
the number of a map's entries, an `int`; `ContainsKey(key)` is a `bool`; `Remove(key)`, a `mut` method,
takes the key's entry out of the map and gives its value, or `null` where there is none, of the type
`map[key]` has, and copies a map another value holds first (M3); `Clear()`, a `mut` method, removes every
entry, and a map that shares its block with another value lets go of it (M3). A map's value may be of a
nullable type: `map[key]` and `Remove(key)` on a `Map<K, V?>` are a `V?`, a missing entry and an entry
that holds `null` alike. `list[i] = value` writes the item at position `i` of a list reached through a
`mut` variable or a `mut` parameter, as an assignment into a path (M4): the value first, then the
position; an `i` below 0 or at `Count` or beyond raises `index-out-of-range` (T14), and a list that
shares its block with another value is copied first (M3). (§4, the owner, 2026-10-30: the keys, the
hash, the members, the nullable value, the write of an item, `==` on two lists and the keys and items of
`double?` and `float?`.) The names are the ones the record's
samples use, `Clear` is the one section 12 gives, and a map's members are the ones section 4 gives. The
naming of the standard library is open beyond the calls on files and sockets and a map's members (record,
sections 12 and 14).

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

Case: [values/list-where-order.kz](../corpus/values/list-where-order.kz)
```kurz
mut xs = List<int>()
xs.Add(3)
xs.Add(1)
xs.Add(2)
big = xs.Where(x => {
    print(x)
    return x > 1
})
print(big)
```

Case: [values/list-index-out-of-range.kz](../corpus/values/list-index-out-of-range.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
print(xs[1])
```

Case: [types/bytes-name.kz](../corpus/types/bytes-name.kz)
```kurz
Bytes b = "hi".Bytes
print(b)
List<byte> same = b
print(same[1])
mut c = Bytes()
c.Add(7)
c.Add(8)
print(c.Count)
```

Case: [values/list-part.kz](../corpus/values/list-part.kz)
```kurz
mut xs = List<int>()
for i in 1..5 {
    xs.Add(i)
}
print(xs[1..<3])
print(xs[1..3])
print(xs[2..<2])
print(xs)
```

Case: [values/list-part-out-of-range.kz](../corpus/values/list-part-out-of-range.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
n = 3
print(xs[1..n])
```

Case: [values/list-part-reversed.kz](../corpus/values/list-part-reversed.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
print(xs[2..<1])
```

Case: [values/list-part-reversed-at-run-time.kz](../corpus/values/list-part-reversed-at-run-time.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
n = 0
print(xs[1..<n])
```

Case: [values/list-clear.kz](../corpus/values/list-clear.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
ys = xs
xs.Clear()
print(xs.Count)
print(ys.Count)
```

Case: [values/map-members.kz](../corpus/values/map-members.kz)
```kurz
mut ages = Map<string, int>()
ages["Ann"] = 31
ages["Bea"] = 27
print(ages.Count)
print(ages.ContainsKey("Ann"))
print(ages.Remove("Ann") ?? 0)
print(ages.Remove("Ann") ?? 0)
print(ages.Count)
print(ages.ContainsKey("Ann"))
copy = ages
ages.Clear()
print(ages.Count)
print(copy.Count + 1)
```

Case: [values/map-keys.kz](../corpus/values/map-keys.kz)
```kurz
data Point(int X, int Y)

mut names = Map<Point, string>()
names[Point(1, 0)] = "east"
print(names[Point(1, 0)] ?? "none")
mut sums = Map<List<int>, int>()
mut key = List<int>()
key.Add(1)
key.Add(2)
sums[key] = 3
mut same = List<int>()
same.Add(1)
same.Add(2)
print(sums[same] ?? 0)
print(names[Point(0, 1)] ?? "none")
```

Case: [values/map-key-map.kz](../corpus/values/map-key-map.kz)
```kurz
seen = Map<Map<string, int>, bool>()
print(seen.Count)
```

Case: [values/map-nullable-value.kz](../corpus/values/map-nullable-value.kz)
```kurz
mut scores = Map<string, int?>()
scores["Ann"] = null
scores["Bea"] = 2
print(scores["Ann"] ?? -1)
print(scores["Cid"] ?? -1)
print(scores.ContainsKey("Ann"))
print(scores.Remove("Bea") ?? 0)
```

Case: [values/map-key-double.kz](../corpus/values/map-key-double.kz)
```kurz
double z = 0.0
nan = z / z
mut names = Map<double, string>()
names[nan] = "nan"
names[z] = "zero"
names[-z] = "minus zero"
print(names.Count)
print(names[nan] ?? "none")
print(names[z] ?? "none")
```

Case: [values/map-key-list-double.kz](../corpus/values/map-key-list-double.kz)
```kurz
double z = 0.0
nan = z / z
mut xs = List<double>()
xs.Add(nan)
xs.Add(z)
mut ys = List<double>()
ys.Add(nan)
ys.Add(-z)
mut seen = Map<List<double>, string>()
seen[xs] = "found"
print(seen[ys] ?? "none")
seen[ys] = "again"
print(seen.Count)
```

Case: [values/map-key-nullable-double.kz](../corpus/values/map-key-nullable-double.kz)
```kurz
double z = 0.0
nan = z / z
mut counts = Map<double?, int>()
counts[nan] = 1
counts[null] = 2
counts[-z] = 3
print(counts[nan] ?? 0)
print(counts[null] ?? 0)
print(counts[z] ?? 0)
print(counts.Count)
```

Case: [values/list-equality.kz](../corpus/values/list-equality.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
mut ys = List<int>()
ys.Add(1)
print(xs == ys)
ys.Add(2)
print(xs == ys)
double z = 0.0
mut ds = List<double>()
ds.Add(z / z)
print(ds == ds)
```

Case: [values/list-equality-nullable-double.kz](../corpus/values/list-equality-nullable-double.kz)
```kurz
data Samples(List<double?> Xs)

double z = 0.0
mut xs = List<double?>()
xs.Add(z / z)
xs.Add(null)
print(xs == xs)
print(Samples(xs) == Samples(xs))
mut ys = List<double?>()
ys.Add(null)
ys.Add(-z)
mut zs = List<double?>()
zs.Add(null)
zs.Add(z)
print(ys == zs)
```

Case: [values/list-write.kz](../corpus/values/list-write.kz)
```kurz
mut xs = List<int>()
xs.Add(4)
xs.Add(5)
ys = xs
xs[1] = 9
print(xs)
print(ys)
```

Case: [values/list-write-out-of-range.kz](../corpus/values/list-write-out-of-range.kz)
```kurz
mut xs = List<int>()
xs.Add(1)
xs[1] = 2
print(xs)
```

Case: [values/list-union-equality.kz](../corpus/values/list-union-equality.kz)
```kurz
// two lists of union items compare by their counts and their items, each pair by its case, then its value (M9, D3)
mut xs = List<int | string>()
xs.Add(1)
xs.Add("a")
mut ys = List<int | string>()
ys.Add(1)
ys.Add("a")
mut zs = List<int | string>()
zs.Add(1)
zs.Add(1)
print(xs == ys)
print(xs == zs)
```

Case: [values/map-union-key-nan.kz](../corpus/values/map-union-key-nan.kz)
```kurz
// a `double` case of a union that is a map's key compares as a `double` key does, as C#'s `Equals`: NaN finds its entry, and 0.0
// and -0.0 are one key (M9)
double z = 0.0
mut m = Map<int | double, int>()
int | double k = z / z
m[k] = 1
print(m[k] ?? 0)
int | double p = 0.0
int | double q = -0.0
m[p] = 2
print(m[q] ?? 0)
print(m.Count)
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

Case: [values/map-union-key.kz](../corpus/values/map-union-key.kz)
```kurz
// a union is a key, which compares by its case, then its value (D3): `1` and `"1"` are two keys
mut m = Map<int | string, string>()
m[1] = "one"
m["1"] = "text"
print(m[1] ?? "none")
print(m["1"] ?? "none")
print(m.Count)
```

Case: [values/map-key-kept.kz](../corpus/values/map-key-kept.kz)
```kurz
// 0.0 and -0.0 are one key (M9): the write under 0.0 finds the entry of -0.0, takes the new value and
// keeps the entry's own key
double z = 0.0
mut names = Map<double, string>()
names[-z] = "minus zero"
names[z] = "zero"
print(names.Count)
print(names)
```

Case: [memory/c-function-type-map-value.kz](../corpus/memory/c-function-type-map-value.kz)
```kurz
// a map's value of a C function type is read as that type's nullable form (M9): the function where the key has an entry, `null`
// where it has none
extern "C" long Twice(long x) {
    return x * 2
}

mut table = Map<string, extern "C" (long) => long>()
table["twice"] = Twice
print(table["twice"] == null)
print(table["thrice"] == null)
```

Case: [data/equality-list-nan.kz](../corpus/data/equality-list-nan.kz)
```kurz
data Samples(List<double> Xs)

double z = 0.0
nan = z / z
mut xs = List<double>()
xs.Add(nan)
mut ys = List<double>()
ys.Add(nan)
print(Samples(xs) == Samples(ys))
print(xs == ys)
```

Case: [values/map-key-nested-generic.kz](../corpus/values/map-key-nested-generic.kz)
```kurz
data Wrap<T>(T Value)

mut m = Map<Wrap<Wrap<Map<int, int>>>, int>()
print(m.Count)
```

Case: [data/nested-generic-equal.kz](../corpus/data/nested-generic-equal.kz)
```kurz
data Wrap<T>(T Value)

mut m = Map<Wrap<Wrap<int>>, int>()
k = Wrap<Wrap<int>>(Wrap<int>(4))
m[k] = 1
print(m[k] ?? 0)
print(k == Wrap<Wrap<int>>(Wrap<int>(4)))
print(k == Wrap<Wrap<int>>(Wrap<int>(5)))
```

### M10 (decided, §4) The order of a map's entries

A map has no order a program may rely on. A `for` loop over it (C6) and its text (A6) yield the
entries in the order of the map's storage, which can differ between runs and versions, as the
documentation of C#'s `Dictionary` leaves its order undefined. Two runs of one program can print
the same map differently, and do as a rule, since the hash of a map's keys takes keys drawn from the
system's randomness, as Rust's `HashMap` takes them (§4, the owner, 2026-10-30).

No case: no order can be expected, so a case can show a map of one entry only, which the case of
A6 does.

### M11 (decided, §4, §8) A list written by its items

`[1, 2, 3]` is a list of the items between the brackets, and `[]` a list without items, as C# 12's
collection expression writes one (§4, the owner, 2026-10-30, "[1, 2, 3], as C#"). The literal takes
the list type where one stands, a written type, `List<int> xs = [1, 2, 3]`, or a parameter's,
`Take([1, 2])`, and each item is read under its item type as an argument is under its parameter's: a
literal takes it (L17), so `Bytes b = [1, 300]` is `constant-overflow`, a lambda takes its parameters
from it (F11), and an item of another type is `type-mismatch` (T1). The list type stands wherever a
type is expected, as L17 reads it for a number literal, but for the other operand of `==` (below):
among them the result of a `return` in a function that
returns a list, an assignment to a variable of a list type, the right side of `??`, an arm of a `match`
and the result of a lambda whose type is given *(assumed, §4: the places of L17's expected type)*;
whether the other operand of `==` gives the literal its type, as it gives one to a number literal (L17),
stands open (§14). Where a union stands whose cases hold one list type, a nullable list among them, the
literal takes that one *(assumed, §4: as a number literal takes the one number case of a union, L17)*.
Where no list type stands, the items give it, as T27 infers a type parameter from a call's arguments
(§4, the owner, 2026-10-30, "From its items"): `x = [1, 2]` is a `List<int>` and `[1, 2L]` a
`List<long>`, while `x = []` and `[1, "a"]` are `cannot-infer`, and `[1, null]` is the `type-mismatch`
of a `null` that no nullable type takes (N6) *(assumed, §4: as T27 and N6 read `Pick(1, null)`)*. A
literal of one or more literal items given to a parameter `List<T>` of a generic call gives its items to the
literal arguments of `T` (T27), so that with `Pair<T>(T a, List<T> b)` the call `Pair(1L, [1])` makes
`T` a `long`, and with two parameters `List<T>` the call `Pick([1], [2L])` does, as `Pick(1, 2L)` does
and as C# 12 infers for a collection expression and Rust for `vec![1]`; a literal with an item that is
not a literal, `[x]`, gives `T` the type of that item, as any other argument does, and `[]` gives it
nothing and takes the list type the other arguments give, `Pair(1L, [])` a `List<long>`, so that where no
other argument gives `T`, `Take([])` with `Take<T>(List<T> xs)` and `Pick([], [])` are `cannot-infer`, as
`x = []` is *(assumed, §4: the items read as arguments of the parameter, of which `[]` has none, "From its
items")*.

A comma after the last item is accepted, `[1, 2,]` (§4, the owner, 2026-10-30, "Accept, as C# 12
and Rust"); a line break inside the brackets continues the statement (L4), and where no `(` or `[` is
open a line that starts with `[` continues none, so `[` after an expression on its line is that
expression's index (M9) and `[` where an expression starts is a list. Inside an open `(` or `[` a line
break ends nothing (L4), so a `[` at the start of a line there indexes the expression before it, and
`[`, `a`, `[0]` and `]` on four lines are `[a[0]]`, while a block between braces there holds statements
of its own (L4). `..` before an item, C# 12's spread, `[..xs, 3]`, is
`syntax` (E4); whether a list takes the items of another so stands open (§14).

The items run from left to right, each counted as it is made, and the list takes the room for all of
them in one block after the last, as Rust's `vec![1, 2, 3]` makes one `Vec`; a block the system refuses
raises `allocation-failed` with the bytes asked for (M9), and an item that leaves the function, a
case of its call's result (O3), leaves no list behind *(assumed, §4: the order of a call's
arguments, and the one block for all items that the answer names)*. An item read from a variable
that an item after it changes keeps the value it was read with, as `Add` counts its item before the list
grows (M7), so `[xs, Grow(mut xs)]` holds `xs` as it was *(assumed, §4: the reading of M7's count
for an item the list counts as it is made)*.

Case: [values/list-literal.kz](../corpus/values/list-literal.kz)
```kurz
int Sum(List<int> numbers) {
    mut total = 0
    for x in numbers {
        total = total + x
    }

    return total
}

List<int> xs = [1, 2, 3]
print(xs)
ys = [4, 5]
print(ys.Count)
print(Sum([1, 2]))
nested = [[1], [2, 3]]
print(nested)
List<string> none = []
print(none.Count)
lines = [
    1,
    2,
]
print(lines)
List<int | string> mixed = [1, "a"]
print(mixed.Count)
```
Case: [values/list-literal-cannot-infer.kz](../corpus/values/list-literal-cannot-infer.kz)
```kurz
x = []
print(x.Count)
```
Case: [values/list-literal-mixed-items.kz](../corpus/values/list-literal-mixed-items.kz)
```kurz
x = [1, "a"]
print(x.Count)
```
Case: [values/list-literal-null-item.kz](../corpus/values/list-literal-null-item.kz)
```kurz
x = [1, null]
print(x.Count)
```
Case: [values/list-literal-byte-overflow.kz](../corpus/values/list-literal-byte-overflow.kz)
```kurz
Bytes b = [1, 300]
print(b.Count)
```
Case: [values/list-literal-item-mismatch.kz](../corpus/values/list-literal-item-mismatch.kz)
```kurz
List<int> xs = [
    1, "a",
]
print(xs.Count)
```
Case: [values/list-literal-spread.kz](../corpus/values/list-literal-spread.kz)
```kurz
xs = [1, 2]
ys = [..xs, 3]
print(ys.Count)
```
Case: [values/list-literal-held.kz](../corpus/values/list-literal-held.kz)

Case: [values/list-literal-inference.kz](../corpus/values/list-literal-inference.kz)
```kurz
List<T> Pair<T>(T a, List<T> b) => b
List<T> Pick<T>(List<T> a, List<T> b) => b

r1 = Pair(1L, [1])
List<long> y1 = r1
print(y1)
r2 = Pair(1, [2L])
List<long> y2 = r2
print(y2)
r3 = Pick([1], [2L])
List<long> y3 = r3
print(y3)
r4 = Pair(1, [2, 3])
List<int> y4 = r4
print(y4)
List<long> xs = [1]
r5 = Pick(xs, [7])
List<long> y5 = r5
print(y5)
byte small = 1
r6 = Pair(small, [2])
List<byte> y6 = r6
print(y6)
r7 = Pair(1L, [])
List<long> y7 = r7
print(y7)
```

Case: [values/list-literal-empty-argument.kz](../corpus/values/list-literal-empty-argument.kz)
```kurz
int Take<T>(List<T> xs) => xs.Count
print(Take([]))
```

Case: [values/list-literal-item-fixes.kz](../corpus/values/list-literal-item-fixes.kz)
```kurz
List<T> Pair<T>(T a, List<T> b) => b

x = 5
r = Pair(1L, [x])
print(r)
```

Case: [values/list-literal-expected.kz](../corpus/values/list-literal-expected.kz)
```kurz
List<long> Made() {
    return [1, 2]
}

List<long>? Missing(bool b) => null

List<long> Arm(int x) {
    return match x {
        1 => [8]
        else => [5, 6]
    }
}

print(Made())
mut List<long> xs = [0]
xs = [2, 3]
print(xs)
ys = Missing(true) ?? [4]
List<long> zs = ys
print(zs)
print(Arm(2))
(int) => List<long> f = n => [7]
print(f(1))
```

Case: [values/list-literal-lines.kz](../corpus/values/list-literal-lines.kz)
```kurz
int Count(int x) {
    return x
}

a = [5, 6]
xs = [
    a
    [0]
]
print(xs)
print(Count(a
[1]))
```

Case: [values/list-literal-bytes.kz](../corpus/values/list-literal-bytes.kz)
```kurz
Bytes b = [104, 105]
print(string.FromUtf8Lossy(b))
List<long> big = [2147483647 + 1]
print(big)
```

Case: [values/list-literal-nullable-items.kz](../corpus/values/list-literal-nullable-items.kz)
```kurz
List<int?> maybe = [1, null]
print(maybe.Count)
print(maybe)
```
