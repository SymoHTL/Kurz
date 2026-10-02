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

### K2 (decided, §4) A `mut` field changes through any reference

A `mut` field of a class instance can be assigned through every reference to the instance,
whether the variable that holds the reference is `mut` or not. The variable holds a reference,
and the reference does not change. Requiring `mut` on the variable could not promise that the
instance stays the same either, because another reference to it can exist.

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

No case: how it is written is open (K8).

### K6 (decided, §4) Single inheritance and interfaces

A class inherits from at most one class and implements any number of interfaces, as in C#.

Case: [classes/inherit.kz](../corpus/classes/inherit.kz)
```kurz
interface Named {
    string Name()
}

class Animal {
    pub string Sound() => "quiet"
}

class Dog : Animal, Named {
    pub string Name() => "Rex"
}

void Describe(Named n) {
    print(n.Name())
}

d = Dog()
Describe(d)
print(d.Sound())
```

### K7 (decided, §4) The body of a class

Class bodies follow C#. A class names its base class and its interfaces after `:`, the base
class first. An interface is declared with `interface` and lists the signatures of its methods;
they are visible wherever the interface is, and a method that implements one is `pub`. Between
the braces of a class, a field is written `Type name`, with `mut` in front when it can be
assigned and its first value after `=`, and a method is written as a function (chapter 8).
Further constructors, `static` members and `override` are written as in C#. There is no property
syntax until something needs it.

Case: [classes/body.kz](../corpus/classes/body.kz)
```kurz
class Counter {
    mut int count = 0
    pub static int Step = 2

    pub void Add() {
        count = count + Step
    }

    pub int Value() => count
}

c = Counter()
c.Add()
c.Add()
print(c.Value())
print(Counter.Step)
```

Case: [classes/inherit.kz](../corpus/classes/inherit.kz)

### K8 (open) How equality is overridden

K5 says that equality can be overridden, as in C#. C# does it with `Equals(object)`,
`GetHashCode()` and, separately, the operator `==`; Kurz has no type that every value belongs to.
Options: (a) a method with a fixed name, `bool Equals(User other)`, and a second one for the
hash, where the compiler checks that both are there; (b) an operator declaration as in C#,
`static bool operator ==(User a, User b)`; (c) the class names the fields that count, and
equality and the hash are derived from them. Lean: (c). The usual reason is an entity that is
equal by its id; the two halves cannot disagree; and it is one line.

### K9 (proposed) A method of a class needs no `mut` marker

A method of a class can assign the `mut` fields of its instance without a marker of its own, and
can be called through every reference (K2). The marker of M5 is for values, where a change has to
reach the variable that holds the value. The record does not say this.

Case: [classes/body.kz](../corpus/classes/body.kz)

### K10 (open) A class with a primary constructor that inherits

How the fields of the base class reach its constructor. Options: (a) as a `data` type does (D7):
the constructor takes the fields of the base first, `class Admin(int Level) : User`; (b) as C#
does: the class lists every parameter and passes the base's on,
`class Admin(string Name, int Level) : User(Name)`. Lean: (a), one form for `data` and `class`.
The cases inherit from classes without a primary constructor until this is answered.
