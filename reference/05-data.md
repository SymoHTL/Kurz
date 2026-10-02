# 5. Data and unions

### D1 (decided, §4, §8) `data`

`data Name(Type Field, ...)` declares an immutable record. The list in brackets is its primary
constructor and its fields. A field is read with a dot.

Case: [data/declare.kz](../corpus/data/declare.kz)
```kurz
data User(int Id, string Name)

u = User(1, "Ann")
print(u.Id)
print(u.Name)
```

### D2 (assumed, §4) Making a value

A value is made by the type's name followed by the arguments in order. There is no `new`; every
sample in the record is written this way.

Case: [data/declare.kz](../corpus/data/declare.kz)

### D3 (decided, §4) Equal by content

Two values of a `data` type are equal when their fields are equal.

Case: [data/equality.kz](../corpus/data/equality.kz)
```kurz
data Point(int X, int Y)

print(Point(1, 2) == Point(1, 2))
print(Point(1, 2) == Point(2, 1))
```

### D4 (decided, §4) Immutable

A field of a `data` value that is held in an immutable variable cannot be assigned: the compile
error `assign-immutable`. Through a `mut` variable it can (M4).

Case: [data/immutable.kz](../corpus/data/immutable.kz)
```kurz
data Point(int X, int Y)

p = Point(1, 2)
p.X = 3
print(p.X)
```

### D5 (decided, §4, §5) `with`

`value with { Field = expression }` is a copy of the value with that field replaced. The original
is unchanged.

Case: [data/with.kz](../corpus/data/with.kz)
```kurz
data User(int Id, string Name)

a = User(1, "Ann")
b = a with { Name = "Bea" }
print(a.Name)
print(b.Name)
print(b.Id)
```

### D6 (decided, §4) A `data` type may inherit from another

A value of the derived type can be used wherever the base type is required.

Case: [data/inherit.kz](../corpus/data/inherit.kz)
```kurz
data User(int Id, string Name)
data Admin(int Level) : User

string NameOf(User u) => u.Name

a = Admin(1, "Ann", 3)
print(a.Id)
print(a.Name)
print(a.Level)
print(NameOf(a))
```

### D7 (decided, §4) How `data` inheritance is written

`data Admin(int Level) : User` declares a `data` type that inherits from `User`. Its constructor
takes the fields of the base first, then its own.

Case: [data/inherit.kz](../corpus/data/inherit.kz)

### D8 (assumed, §4, §5) A `data` type without fields

`data Name` declares a type with exactly one value, written as the bare name. The record's
outcome samples use such types as cases (`NotFound`).

Case: [data/no-fields.kz](../corpus/data/no-fields.kz)
```kurz
data Empty

e = Empty
print(e == Empty)
```

### D9 (decided, §4) Unions

`A | B` is a type. Its values are the values of `A` and the values of `B`. A value of a case
type is a value of the union without a word. To use it as one of its cases, `match` it (C4).

Case: [data/union.kz](../corpus/data/union.kz)
```kurz
data Circle(int Radius)
data Square(int Side)

void Describe(Circle | Square shape) {
    match shape {
        Circle c => print("circle {c.Radius}")
        Square s => print("square {s.Side}")
    }
}

Describe(Circle(2))
Describe(Square(3))
```

### D10 (assumed, §4) Small values are plain values

A small `data` value is compiled as a plain value without a reference count, automatically.
There is no `struct` keyword.

No case: it changes speed and layout, not what a program prints.

### D11 (decided, §8) Default values and arguments by name

A field or a parameter can have a default value, written after its name: `int Count = 1`. An
argument can be passed by name: `Item(ProductId: 7)`. Both work as in C#: arguments without a
name fill the parameters in order, arguments by name follow them in any order, and a parameter
that has a default can be left out.

Case: [data/default-and-named.kz](../corpus/data/default-and-named.kz)
```kurz
data Item(int ProductId, int Count = 1)

a = Item(7)
b = Item(Count: 5, ProductId: 7)
print(a.Count)
print(b.Count)
print(b.ProductId)
```

Case: [functions/default-value.kz](../corpus/functions/default-value.kz)
```kurz
void Greet(string name, string word = "hello") {
    print("{word} {name}")
}

Greet("Ann")
Greet("Ann", "bye")
Greet("Ann", word: "bye")
```

### D12 (decided, §4) `enum`

`enum Plan { Free, Pro }` declares a type with a fixed set of named values, written `Plan.Free`.
It is the short form of a union of cases without fields (D8, D9): a `match` on it has to list
every value (O8), and two values are equal when they are the same value. A value is not an
integer. It has a number only where the declaration writes one; how that is written and read is
D18.

Case: [data/enum.kz](../corpus/data/enum.kz)
```kurz
enum Plan { Free, Pro }

void Show(Plan p) {
    match p {
        Plan.Free => print("free")
        Plan.Pro => print("pro")
    }
}

Show(Plan.Free)
Show(Plan.Pro)
print(Plan.Free == Plan.Pro)
```

Case: [data/enum-match-not-exhaustive.kz](../corpus/data/enum-match-not-exhaustive.kz)
```kurz
enum Plan { Free, Pro }

void Show(Plan p) {
    match p {
        Plan.Free => print("free")
    }
}

Show(Plan.Pro)
```

### D13 (decided, §4) Flags

`flags Access { Read, Write, Run }` declares a type whose values are sets of the listed names.
It gives what `[Flags]` and `HasFlag` give in C#. Each name is one bit, and the compiler numbers
the bits in order unless the declaration writes the number (D18). A name alone, `Access.Read`,
is the set that holds that name. `set.Has(Access.Read)` is `true` when the set holds the name. A
set is not one case, so a `match` cannot list it case by case. How sets are combined is D17; the
case tests a set of one name.

Case: [data/flags.kz](../corpus/data/flags.kz)
```kurz
flags Access { Read, Write, Run }

p = Access.Read
print(p.Has(Access.Read))
print(p.Has(Access.Write))
```

### D14 (decided, §8) The fields of a primary constructor can be read from outside

A field that is declared in a primary constructor can be read wherever its type is visible, as
the positional members of a C# record can. It is the exception to F9, which makes a member
private to its type unless it says `pub`: without it every such field would carry `pub`.

Case: [data/declare.kz](../corpus/data/declare.kz)

### D15 (assumed, §4) Never equal across types

A value of a derived `data` type never equals a value of its base type, whatever their fields
hold. Two values of the derived type are equal when all their fields are, those of the base
included (D3).

Case: [data/inherit-equality.kz](../corpus/data/inherit-equality.kz)
```kurz
data User(int Id, string Name)
data Admin(int Level) : User

bool Same(User a, User b) => a == b

print(Same(Admin(1, "Ann", 3), Admin(1, "Ann", 3)))
print(Same(Admin(1, "Ann", 3), User(1, "Ann")))
```

### D16 (assumed, §4) The body of a `data` type

A `data` type takes its methods between braces after its constructor, in the form of a class body
(K7).

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

### D17 (open) How sets of flags are combined

D13 gives a set of flags and the test `Has`. Not chosen: how two sets become one, how a name is
taken out of a set, how the set that holds nothing is written, and what `Has` answers for a set
of several names. Options: (a) the bit operators of C# (T22): `Access.Read | Access.Write`,
`set & ~Access.Write`, and `Access.None`, a name that every `flags` type has; (b) methods:
`set.With(Access.Write)`, `set.Without(Access.Write)` and `Access()`. Under both, `Has` is
`true` when the set holds every name of its argument, as `HasFlag` is in C#. Lean: (a). It is
what C# does with `[Flags]`, and T22 has the operators. The cost: taking a name out reads as
arithmetic on bits.

### D18 (open) The number of an `enum` value and of a flag

D12 and D13 give a value a number where the declaration writes one. Not chosen: how the
declaration writes it, which bit the first name of a `flags` type gets, how a program gets the
number of a value, and how a number becomes a value. Options: (a) as in C#: `enum Plan { Free = 1, Pro = 2 }`, `int(Plan.Pro)` (T10) and
`Plan(2)`, where a number that no value has raises an exception; (b) written the same in the
declaration, and the two directions are members of the type: `Plan.Pro.Number`, and
`Plan.From(2)` with the result `Plan | Invalid`, so that a number from outside is an outcome
(O1). Lean: (b). A number arrives from a file, a database or the wire, where a wrong one is
normal use, and the record keeps exceptions for what a function cannot recover from.
