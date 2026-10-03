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
value. It is short for nested `with` (D5). On an immutable variable it is the compile error
`assign-immutable` (D4).

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
that is not reachable through a `mut` variable is the compile error `mut-required`.

Case: [values/mut-method-on-immutable.kz](../corpus/values/mut-method-on-immutable.kz)

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

A function changes a value of its caller only through a parameter marked `mut`, and the caller
writes `mut` in front of the argument as well. Changing a parameter that is not `mut`, or passing
an immutable variable as `mut`, is the compile error `mut-required`. Leaving `mut` out at the
call is the compile error `mut-at-call`.

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

### M8 (decided, §4) One family of collections

There is one family of collections, and every value can cross between actors.

No case: actors are outside this reference; that collections are values is shown under M3.

### M9 (assumed, §4, §5) The collections the corpus uses

`List<T>()` makes an empty list; `Add(item)` appends and is a `mut` method; `Count` is the number
of items; `list[i]` is the item at position `i`, counted from 0; `Where(test)` is the list of
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

### M10 (decided, §4) The order of a map's entries

A map has no order a program may rely on. A `for` loop over it (C6) and its text (A6) yield the
entries in the order of the map's storage, which can differ between runs and versions, as for a
`Dictionary` in C#. Two runs of one program can print the same map differently.

No case: no order can be expected, so a case can show a map of one entry only, which the case of
A6 does.
