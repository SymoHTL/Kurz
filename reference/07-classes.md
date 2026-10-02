# 7. Classes

### K1 (decided, §4, §8) `class`

`class Name(Type Field, mut Type Field, ...)` declares a class with a primary constructor. A
field marked `mut` can be assigned; assigning any other field is the compile error
`assign-immutable`.

Case: [classes/mut-field.kz](../corpus/classes/mut-field.kz)
```kurz
class Counter(mut int Count)

c = Counter(0)
c.Count = c.Count + 1
print(c.Count)
```

Case: [classes/immutable-field.kz](../corpus/classes/immutable-field.kz)
```kurz
class User(string Name, mut int Age)

u = User("Ann", 31)
u.Age = 32
u.Name = "Bea"
print(u.Name)
```

### K2 (proposed, §4) A `mut` field changes through any reference

A `mut` field of a class instance can be assigned through every reference to the instance,
whether the variable that holds the reference is `mut` or not. The variable holds a reference,
and the reference does not change. The record says classes "change only through their own `mut`
fields"; it does not say whether the variable has to be `mut` too. The other reading would
require `mut` on the variable, and could still not promise that the instance stays the same,
because another reference to it can exist.

Case: [classes/mut-field.kz](../corpus/classes/mut-field.kz)

### K3 (decided, §4) References with identity

Assigning a class instance does not copy it. Both variables refer to the same instance.

Case: [classes/reference.kz](../corpus/classes/reference.kz)
```kurz
class Counter(mut int Count)

a = Counter(0)
b = a
a.Count = 5
print(b.Count)
```

### K4 (decided, §4) Equality

A class with `mut` fields compares by identity. A class without them compares by content.

Case: [classes/equality-identity.kz](../corpus/classes/equality-identity.kz)
```kurz
class Counter(mut int Count)

a = Counter(1)
b = Counter(1)
print(a == b)
print(a == a)
```

Case: [classes/equality-content.kz](../corpus/classes/equality-content.kz)
```kurz
class Label(string Text)

print(Label("x") == Label("x"))
```

### K5 (decided, §4) Equality can be overridden

As in C#.

No case: how members are written in a class body is open (K7).

### K6 (decided, §4) Single inheritance and interfaces

A class inherits from at most one class and implements any number of interfaces, as in C#.

No case: how either is written is open (K7).

### K7 (open) The body of a class, inheritance and interfaces

The record's samples show fields and methods between braces and nothing else. Open: how a class
names its base class and its interfaces; how an interface is declared; constructors besides the
primary one; properties; members that belong to the type (`User.Guest` in section 5 of the
record); how equality is overridden. The record lists interfaces and properties as missing
details (section 14). Lean: C# forms throughout, with fields written `Type name` and methods as
functions (chapter 8), and no property syntax until something needs it.
