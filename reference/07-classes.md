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

A class with `mut` fields compares by identity. A class without them compares by content. Across
inheritance (K6, K10) K13 holds.

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

### K13 (decided, §4) Equality across inheritance

What D15 says for `data` holds for classes: instances of different classes are never equal,
whatever their fields hold. The fields of the base count as fields of the class for K4, so one
`mut` field anywhere in the chain means identity. The `equal by` clause (K8) of the base is
inherited, and a clause of the derived class adds its fields to the base's: `Root` below compares
by `Id` and `Level`. The owner chose this on 2026-10-03 as the option whose contrast was a derived
clause "replacing instead of extending" the base's; against C#'s record equality, which compares
the run-time types and every field with a replacing clause, and against equality by the fields of
the static type, under which `a == b` and `b == a` can differ; the cost is that a base-typed
collection cannot find an instance by a base-typed key.

Case: [classes/equality-inherited.kz](../corpus/classes/equality-inherited.kz)
```kurz
class User(string Name)
class Admin(int Level) : User
class Box(mut int Count)
class Tagged(string Tag) : Box

bool Same(User a, User b) => a == b

print(Same(Admin("Ann", 1), User("Ann")))
print(Admin("Ann", 1) == Admin("Ann", 1))
print(Tagged(0, "a") == Tagged(0, "a"))
```

Case: [classes/equality-clause-inherited.kz](../corpus/classes/equality-clause-inherited.kz)
```kurz
class User(int Id, mut string Name) equal by Id
class Admin(int Level) : User
class Root(int Level) : User equal by Level

print(Admin(1, "Ann", 2) == Admin(1, "Bea", 3))
print(Root(1, "Ann", 2) == Root(1, "Ann", 3))
print(Root(1, "Ann", 2) == Root(2, "Bea", 2))
```

### K5 (decided, §4) Equality by named fields

A class can replace the equality of K4 by naming the fields that count. Two instances are then
equal when those fields are equal, and the hash is derived from the same fields, so the two
cannot disagree. Nothing else overrides equality: a class brings no comparison code of its own.

Case: [classes/equality-named.kz](../corpus/classes/equality-named.kz)
```kurz
class User(int Id, mut string Name) equal by Id

a = User(1, "Ann")
b = User(1, "Bea")
c = User(2, "Ann")
print(a == b)
print(a == c)
b.Name = "Ann"
print(a == b)
```

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
Further constructors, `static` members and `override` are written as in C#. What a further
constructor may do to a field without `mut` is K14; what a `static` field may hold is K18. A
method that a derived class overrides is marked `virtual` in the base, as in C#. *(proposed:
`override` as there needs it)* There is no property syntax until something needs it.

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

### K18 (decided, §4, §6) What a `static` field may hold

A `static` field exists once for the program, so it holds an immutable value only: `static
mut`, and a static field whose type is or holds a class, are the compile error `static-state`. A
`mut` field would be state that every actor reaches, against the record's section 6; a class
instance, mutable or not, lives in one actor's heap, which no other actor reaches into, and so
does one inside a `List<Button>`. The owner chose this on 2026-10-03, against one copy per actor.
*(assumed: that the heap is the reason for an immutable class and for a value that holds one, and
that a static field of a generic class exists once per type argument, as in C#)* K7's case shows
a static field that is allowed.

Case: [classes/body.kz](../corpus/classes/body.kz)

Case: [classes/static-mut.kz](../corpus/classes/static-mut.kz)
```kurz
class Counter {
    pub static mut int Total = 0

    pub void Add() {
        Total = Total + 1
    }
}

Counter().Add()
print(Counter.Total)
```

Case: [classes/static-instance.kz](../corpus/classes/static-instance.kz)
```kurz
class Counter(int Count)

class Registry {
    pub static Counter Zero = Counter(0)
}

print(Registry.Zero.Count)
```

### K14 (decided, §4) Further constructors and fields without `mut`

A constructor, and only a constructor, may assign each field without `mut` once, before its body
ends, as C# lets a constructor set a `readonly` field; a second assignment there is
`assign-immutable` like one anywhere else. A further constructor of a class with a primary
constructor calls the primary one first, with `: this(...)` as C# 12 requires, and a class
without a primary constructor has C#'s constructors. The owner chose this on 2026-10-03, against
a class with a primary constructor having no further constructor, which would have made a second
way to build an instance a static method or a second class; the cost is the flow analysis that
proves "once, before the end". What the answer did not reach, *(proposed)* as a whole: the once is
per instance, stricter than C#, which allows any number of assignments. A field that the primary
constructor or an `=` in the body (K7) sets is assigned by no constructor; a field without `mut`
and without `=` is assigned exactly once on every path of every constructor that does not chain,
and a constructor that leaves it unassigned on a path, or reads it first, is the compile error
`field-unassigned`, as is such a field in a class with a primary constructor, which a call of
that constructor would leave unset. The call of the primary one is direct; a further constructor
without it is `constructor-must-chain`. A constructor written in the body is private without
`pub`, as every member is (F9); the primary constructor, and the empty constructor of a class
without one, are visible wherever the class is.

Case: [classes/further-constructor.kz](../corpus/classes/further-constructor.kz)
```kurz
class Counter(int Start) {
    pub Counter() : this(0) { }

    pub int Value() => Start
}

print(Counter().Value())
print(Counter(5).Value())
```

Case: [classes/constructor-assigns-twice.kz](../corpus/classes/constructor-assigns-twice.kz)
```kurz
class Point {
    int x

    pub Point(int value) {
        x = value
        x = value + 1
    }

    pub int X() => x
}

print(Point(1).X())
```

Case: [classes/constructor-must-chain.kz](../corpus/classes/constructor-must-chain.kz)
```kurz
class Counter(int Start) {
    pub Counter() { }

    pub int Value() => Start
}

print(Counter().Value())
```

Case: [classes/field-unassigned.kz](../corpus/classes/field-unassigned.kz)
```kurz
class Point {
    int x

    pub Point(bool set) {
        if set {
            x = 1
        }
    }

    pub int X() => x
}

print(Point(true).X())
```

### K8 (decided, §4) How a class names the fields that count

The fields that count (K5) are named in a clause after the head of the class:
`class User(int Id, mut string Name) equal by Id`. `equal` and `by` are core words (L12). A class
without the clause compares as K4 says. Whether a `mut` field can be named is K12; the form of the
clause with several fields, and its errors, is K15.

Case: [classes/equality-named.kz](../corpus/classes/equality-named.kz)

### K12 (assumed, §4) A `mut` field in the clause

A `mut` field can be named in the clause of K8. The record states the cost: when such a field is
assigned, the hash of the instance changes, so an instance that is a key of a map at that moment
is no longer found under it, and nothing reports that. The record marks as *(assumed)* this
reading of the owner's answer on `mut` fields.

Case: [classes/equality-named-mut.kz](../corpus/classes/equality-named-mut.kz)
```kurz
class Tag(mut string Name) equal by Name

x = Tag("a")
y = Tag("a")
print(x == y)
y.Name = "b"
print(x == y)
```

### K15 (proposed) The form of the `equal by` clause

The clause names one or more fields of the class, with `, ` between them: `equal by Id, Kind`. It
stands after the primary constructor and after the `: Base` part (K6, K10), before a body in
braces. It can name a field of the primary constructor, a field declared in the body and a field
of the base (K13 says what the base contributes). A name that is not a field of the class is the
compile error `equal-by-unknown`.

Case: [classes/equality-two-fields.kz](../corpus/classes/equality-two-fields.kz)
```kurz
class Key(int Id, string Kind, mut int Hits) equal by Id, Kind

print(Key(1, "a", 0) == Key(1, "a", 5))
print(Key(1, "a", 0) == Key(1, "b", 0))
```

Case: [classes/equality-unknown-field.kz](../corpus/classes/equality-unknown-field.kz)
```kurz
class Key(int Id) equal by Code

print(Key(1) == Key(1))
```

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
from a field. What the short form follows when the base has further constructors or none, and
what a listed parameter with a base field's name is in that form, K16 says.

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

### K16 (decided, §4) The short form against a base with other constructors

The short form of K10 follows the primary constructor of the base, and is the compile error
`no-primary-constructor` when the base has none (K6's `Animal`), whatever further constructors
it has. A listed parameter with a base field's name and type is that field, as K11 says for the
explicit form, and with another type it is K17's error. The defaults of the base keep their
place, so the class's own parameters come after them, and a call that leaves one out names the
rest (D11). The owner chose this on 2026-10-03, against taking the empty constructor when the
base has no primary one, which would have given one form two readings; the cost is that such a
base forces the explicit form.

Case: [classes/inherit-no-primary-constructor.kz](../corpus/classes/inherit-no-primary-constructor.kz)
```kurz
class Animal {
    pub string Sound() => "quiet"
}

class Dog(string Name) : Animal

d = Dog("Rex")
print(d.Sound())
```

Case: [classes/inherit-default-keeps-place.kz](../corpus/classes/inherit-default-keeps-place.kz)
```kurz
class User(string Name = "guest")

class Admin(int Level) : User

a = Admin(Level: 3)
print(a.Name)
print(a.Level)
```

### K11 (decided, §4) A parameter that is passed on to the base

In the explicit form of K10, a parameter that has the name and the type of a field of the base
is that field and no new one, as in a C# record:
`class Admin(string Name, int Level) : User(Name)` holds `Name` once. Every other parameter is a
field of the class (K1). The cost: a name decides whether a parameter is a field. A parameter
with a base field's name and another type, and one with its name and type that the base does not
receive, are K17.

Case: [classes/inherit-pass-on.kz](../corpus/classes/inherit-pass-on.kz)
```kurz
class User(string Name)

class Admin(string Name, int Level) : User(Name)

a = Admin("Ann", 3)
print(a.Name)
print(a.Level)
```

### K17 (proposed) A parameter that clashes with a field of the base

A parameter that has the name of a field of the base and another type (`class Admin(int Name) :
User("x")`) would give the class two fields of one name; a parameter that has the name and the
type of a base field but is not what the base receives (`class Admin(string Name, int Level) :
User("guest")`) would be a field the base sets to something else. Both are the compile error
`base-field-clash`, as a C# record rejects a positional parameter that does not match the
inherited member it names.

Case: [classes/inherit-field-clash.kz](../corpus/classes/inherit-field-clash.kz)
```kurz
class User(string Name)

class Admin(int Name) : User("x")

print(Admin(1).Name)
```
