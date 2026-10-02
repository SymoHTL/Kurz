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

### K5 (decided, §4) Equality by named fields

A class can replace the equality of K4 by naming the fields that count. Two instances are then
equal when those fields are equal, and the hash is derived from the same fields, so the two
cannot disagree. Nothing else overrides equality: a class brings no comparison code of its own.

No case: how the fields are named is open (K8).

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

### K8 (open) How a class names the fields that count

K5 lets a class name the fields that count for its equality. How it names them is not chosen,
and neither is whether a `mut` field can be one. Options: (a) a word in front of each such
field, `class User(key int Id, mut string Name)`; (b) a clause after the head of the class,
`class User(int Id, mut string Name) equal by Id`. Either adds core words (L12), and `key` is a
common name for a variable. Lean: (b), which shows the whole key in one place and leaves the
fields as they are. On `mut` fields the lean is no: an instance that sits in a map as a key
would change its hash while it is there.

### K9 (decided, §4) A method of a class needs no `mut` marker

A method of a class can assign the `mut` fields of its instance without a marker of its own, and
can be called through every reference (K2). The marker of M5 is for values, where a change has to
reach the variable that holds the value.

Case: [classes/body.kz](../corpus/classes/body.kz)

### K10 (decided, §4) A class with a primary constructor that inherits

There are two forms. The short one is the form of a `data` type (D7):
`class Admin(int Level) : User`, whose constructor takes what the constructor of the base takes,
in the same order, and then the parameters the class lists. The explicit one writes the
arguments of the base after its name, so that they can be computed:
`class Guest(int Number) : User("guest {Number}")`. Its constructor takes only the parameters
the class lists, and the arguments of the base are expressions over them. Whether a parameter
can be passed on without becoming a field of the class is K11; the case computes the argument
from a field.

Case: [classes/inherit-constructor.kz](../corpus/classes/inherit-constructor.kz)
```kurz
class User(string Name)

class Admin(int Level) : User

class Guest(int Number) : User("guest {Number}")

a = Admin("Ann", 3)
print(a.Name)
print(a.Level)
g = Guest(7)
print(g.Name)
print(g.Number)
```

### K11 (open) A parameter that is only passed to the base

In the explicit form of K10 every parameter of the class is a field (K1). A class that takes a
value only to hand it to its base, `class Admin(string Name, int Level) : User(Name)`, would
hold `Name` twice. Options: (a) a parameter with the name and the type of a field of the base is
that field and no new one, as in a C# record; (b) every parameter is a field, a name the base
already has is the compile error `redeclared`, and such a class uses the short form; (c) a word
marks a parameter that is no field. Lean: (a). It is what the explicit form is for when only
some of the arguments are computed, and it needs no new word. The cost: a name decides whether
a parameter is a field.
