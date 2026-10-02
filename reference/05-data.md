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

### D2 (proposed) Making a value

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

No case: how inheritance is written is open (D7).

### D7 (open) How `data` inheritance is written

Options: (a) `data Admin(int Level) : User`, the base's fields come first in the constructor;
(b) as C# records, `data Admin(int Id, string Name, int Level) : User(Id, Name)`. Also open:
whether a value of the derived type equals a value of the base type with the same fields.
Lean: (a), the shorter one, and never equal across types.

### D8 (proposed, §5) A `data` type without fields

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

### D11 (open) Fields with defaults, arguments by name

Whether a field can have a default value, and whether a constructor or a function can be called
with named arguments. The record needs a default for a field that is added to an `open` type
(section 13) and does not say how it is written. Lean: C# forms, `int Count = 1` in the
declaration and `Item(ProductId: 7)` at the call.

### D12 (open) Enums

A type with a fixed set of named values. The record lists enums as a missing detail (section 14),
and its durable-actor sample writes `Plan.Free` and `Plan.Pro` without declaring `Plan`
(section 13). Options: (a) `enum Plan { Free, Pro }` as in C#, where each value is a number
underneath; (b) no enum: a union of `data` types without fields (D8), which `match` already
covers (O8), and which needs a way to give a union a name; (c) `enum` as the short form of (b):
its values are cases, a `match` has to list every one, and a value has a number only where one is
written. Lean: (c). A number is needed at the edges only, in a database column or a wire format,
and a value that is silently also an integer is the C# trap of `(Plan)7`.
