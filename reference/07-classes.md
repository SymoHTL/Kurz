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

A class with `mut` fields compares by identity. A class without them compares by content. How
this works across inheritance (K6, K10) is K13.

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

### K13 (open) Equality across inheritance

K4 and K5 speak of one class. With inheritance three things are unsaid: whether the `mut` fields
of the base count when K4 decides between identity and content; whether an instance of a derived
class can equal an instance of its base or of a sibling (`Dog() == Animal()` with K6's classes,
`Admin("Ann", 3) == User("Ann")` with K10's); and whether a derived class inherits the `equal by`
clause of its base. The options:

- (a) What D15 says for `data`: instances of different classes are never equal, whatever their
  fields hold; the fields of the base count as fields of the class for K4, so one `mut` field
  anywhere in the chain means identity; the clause of the base is inherited until the derived
  class writes its own. Cost: a base-typed collection cannot find an instance by a base-typed key.
- (b) What C# records do: equality compares the run-time types and every field, which is (a)
  with the clause of the derived class replacing instead of extending the base's. Cost: the same,
  with one more rule for the clause.
- (c) Equality by the fields the static type has, so an `Admin` held as a `User` equals a `User`
  with the same `Name`. Cost: `a == b` and `b == a` can differ when the static types differ.

The lean is (a), which the `data` case data/inherit-equality.kz already pins for values.

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
constructor may do to a field without `mut`, and whether it has to call the primary one, is K14.
What a `static` field may hold is open in the record (section 14): a `static mut` field, or a
static field that holds a class instance, would be state that every actor shares, against section
6. There is no property syntax until something needs it.

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

### K14 (open) Further constructors and fields without `mut`

K1 makes assigning a field without `mut` the compile error `assign-immutable`, with no exception
for a constructor, and K7 takes further constructors from C#, where a constructor is exactly the
place that sets a read-only field. Which C# is meant is unsaid as well: before C# 12 a class had
no primary constructor, and since C# 12 every further constructor of a class that has one must
chain to it with `this(...)`. The options:

- (a) A constructor, and only a constructor, may assign each field without `mut` once, before its
  body ends, as C# does for `readonly`; a further constructor of a class with a primary
  constructor calls the primary one first, as C# 12 requires, and a class without one has C#'s
  constructors. Cost: the flow analysis that proves "once, before the end".
- (b) A class with a primary constructor has no further constructor; a field without `mut` gets
  its value from the primary constructor or from its `=` in the body (K7) and nowhere else. Cost:
  a second way to build an instance is a static method or a second class.

The lean is (a).

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
what a listed parameter with a base field's name is in that form, is K16.

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

### K16 (open) The short form against a base with other constructors

The short form of K10 takes "what the constructor of the base takes", which assumes a base with
exactly one constructor. K7 allows further constructors, and a base without a primary constructor
(K6's `Animal`) has only those, or the empty one. Unsaid as well: a listed parameter that repeats
the name of a base field (`class Admin(string Name) : User`), and a base parameter with a default
value followed by the class's own parameters. The options:

- (a) The short form follows the primary constructor of the base and is the compile error
  `no-primary-constructor` when the base has none; a listed parameter with a base field's name
  and type is that field, as K11 says for the explicit form, and with another type it is K17's
  error; the defaults of the base keep their place, so the class's own parameters come after
  them and a call that leaves one out names the rest (D11). Cost: a base without a primary
  constructor forces the explicit form.
- (b) The short form takes the empty constructor when the base has no primary one, and a repeated
  name is `redeclared`. Cost: two readings of one form.

The lean is (a).

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
