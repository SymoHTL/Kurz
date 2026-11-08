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

Case: [functions/capture-changes-instance.kz](../corpus/functions/capture-changes-instance.kz)
```kurz
class Counter(mut int Count) {
    pub void Add(int n) {
        Count = Count + n
    }
}

c = Counter(0)
bump = () => c.Add(1)
bump()
bump()
print(c.Count)
reset = () => { c.Count = 5 }
reset()
print(c.Count)
```

Case: [functions/capture-writes-field.kz](../corpus/functions/capture-writes-field.kz)
```kurz
class Box(mut List<int> Items)

b = Box([1, 0])
first = () => { b.Items[0] = 2 }
first()
print(b.Items)
```

Case: [functions/capture-calls-field-method.kz](../corpus/functions/capture-calls-field-method.kz)
```kurz
class Box(mut List<int> Items)

b = Box([1, 0])
more = () => b.Items.Add(3)
more()
print(b.Items)
```

Case: [functions/capture-passes-field-mut.kz](../corpus/functions/capture-passes-field-mut.kz)
```kurz
void Fill(mut List<int> xs) {
    xs.Add(xs.Count)
}

class Box(mut List<int> Items) {
    pub () => void Filler() => () => Fill(mut Items)
}

b = Box([1, 0])
fill = b.Filler()
fill()
print(b.Items.Count)
```

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

A class with `mut` fields compares by identity. A class with no `mut` field and no `weak` reference in
its fields' types compares by content, each field
by the equality Kurz gives its type, a `double` or `float` with NaN equal to NaN as under D3 and K5. Across
inheritance (K6, K10) K13 holds. A class with a `weak` field compares by identity too, as one with a
`mut` field does: what the field holds turns `null` when its target is freed (§4, the owner,
2026-11-01, "Class compares by identity", against leaving the field out of the comparison and the
hash, and against comparing its target's identity, under which equality and the hash change when
the target is freed). So does a class whose field's type reaches a `weak` reference, an item of a
list, a key or a value of a map, a nullable or a field of a `data` value, `class Club(List<weak Owner?> Members)`;
content comes through K5's clause with fields that hold no `weak` (§4, the owner, 2026-11-02, #110,
comment 6580085338). A class in between compares as its own class does, so a `weak` behind it does
not count *(assumed, as §4 marks it)*.

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

Case: [classes/equality-weak-field.kz](../corpus/classes/equality-weak-field.kz)
```kurz
class Owner(string Name)
class Pet(string Name, weak Owner? Keeper)

o = Owner("Ann")
a = Pet("Rex", o)
b = Pet("Rex", o)
print(a == b)
print(a == a)
```

Case: [classes/equality-content-map.kz](../corpus/classes/equality-content-map.kz)
```kurz
class Tally(string Name, Map<string, int> Counts)

t = Tally("a", Map<string, int>())
print(t == t)
```

Case: [classes/equality-weak-in-list.kz](../corpus/classes/equality-weak-in-list.kz)
```kurz
class Owner(string Name)
class Club(List<weak Owner?> Members)

o = Owner("Ann")
a = Club([o])
print(a == Club([o]))
print(a == a)
```

Case: [classes/property-equality.kz](../corpus/classes/property-equality.kz)
```kurz
class Point(int X) {
    pub int Loud => Say()

    int Say() {
        print("read")
        return X
    }
}

// a property is no field: equality compares the fields alone (K4), and runs no property, which would print
print(Point(1) == Point(1))
print(Point(1) == Point(2))
print(Point(1).Loud)
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
collection cannot find an instance by a base-typed key. `==` on a class that another class derives
from, and a key of it, read the fields that count of each class derived from it too, since the value
can be an instance of any of them, so that such a field of a type `==` does not take makes `==` on the
base `type-mismatch`, at the comparison, whatever the folder of the derived class, as D3 reads the
fields of a type derived from a `data` type *(assumed, §4: the build's reading of the answer that `==`
on a `data` value that holds a map or a function value is a compile error; C# takes `==` on any class
or record, a class's comparing references; the cost is that a class derived from the base, in any
file of the program, can make `==` on the base `type-mismatch` where no line changed, and that the
check reads every class of the program)*.

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

Case: [classes/equality-derived-map.kz](../corpus/classes/equality-derived-map.kz)
```kurz
class Shape(string Name)
class Tally(Map<string, int> Counts) : Shape

bool Same(Shape a, Shape b) => a == b

print(Same(Shape("a"), Shape("a")))
```

### K5 (decided, §4) Equality by named fields

A class can replace the equality of K4 by naming the fields that count. Two instances are then equal
when those fields are equal, and the hash is derived from the same fields, so the two cannot
disagree. Nothing else overrides equality: a class brings no comparison code of its own. There are
no operator declarations: `+` on two instances is a method, `a.Add(b)`, and `operator` is no core
word (L18), so a declaration of one is `syntax` (E4) (the owner, 2026-10-09, against C#'s operator
declarations with `==`, `!=` and the comparisons excluded; the cost is that a vector type reads as
`a.Add(b).Scale(2)`). A method named `Equals` or `GetHashCode` on a class is the compile error
`equals-method`, whose message points to the `equal by` clause (K8), and `==` stays K4's and this
rule's (§4, the owner, 2026-10-30, against `==` only where content equality is declared and identity
in a form of its own, as Rust has them, and against such a method as an ordinary member that changes
nothing about `==`; the cost is two names no method of a class can take). The id and its line, the
method's, are the owner's too (§4, 2026-10-30, "Accept both"). `a + b` on two instances is
`type-mismatch` (T1), the error of an
arithmetic operator on operands it has no meaning for (the owner, 2026-10-10, confirming the reading
proposed after the review of round 13). A `double` or `float` among the named fields compares as a field of a
`data` value does (D3), NaN equal to NaN (the owner, 2026-10-10); under K4's content equality it
compares the same way. A named field whose type `==` does not take, a map among them, makes `==` on
the instances and a key of the class `type-mismatch`, at that `==` and at the line that writes the
key type, as such a field does in a `data` value (D3, M9): no named field is skipped (§4, the owner, 2026-11-02, #110, comment 6590315607).

Case: [classes/equal-by-nan.kz](../corpus/classes/equal-by-nan.kz)
```kurz
class Point(double X, double Y, mut int Hits) equal by X, Y

double z = 0.0
nan = z / z
print(Point(nan, 0.0, 0) == Point(nan, 0.0, 1))
```

Case: [classes/operator-on-instances.kz](../corpus/classes/operator-on-instances.kz)
```kurz
class Vec(int X)

print(Vec(1) + Vec(2))
```

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

Case: [classes/operator-declaration.kz](../corpus/classes/operator-declaration.kz)
```kurz
class Vec(int X) {
    pub Vec operator +(Vec a, Vec b) => Vec(a.X + b.X)
}

print(Vec(1).X)
```

Case: [classes/equals-method.kz](../corpus/classes/equals-method.kz)
```kurz
class User(int Id) {
    pub bool Equals(User other) => Id == other.Id
}

print(User(1) == User(1))
```

Case: [null/narrowed-field-after-clause-key.kz](../corpus/null/narrowed-field-after-clause-key.kz)
```kurz
class Node(int V)
class Holder(mut Node? Head)
class K(int Id) equal by Id

h = Holder(Node(1))
k = K(1)
mut seen = Map<K, int>()
if h.Head != null {
    seen[k] = 2
    print(h.Head.V)
}
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

Class bodies follow C#. A class names its base class and its interfaces after `:`, the base class
first. An interface is declared with `interface` and lists the signatures of its methods; they are
visible wherever the interface is, and a method that implements one is written `pub`, as C# demands
`public` of an implicit implementation: a class that names an interface and lacks one of its methods is the compile error
`missing-member`, at the class, with the message naming the method, and a method that is there
without `pub` is `interface-method-private`, at the method (the owner, 2026-10-09, against `pub`
implied by the interface, under which a method that looks private is public, and against one id for
both mistakes). A method implements an interface method when its name, its parameter types in their
order and its return type are the interface's; a `pub` method inherited from the base class counts,
as in C# (the owner, 2026-10-10, confirming the reading proposed after the review of round 13); one
inherited without `pub` does not count, and the class is `missing-member` (the owner, 2026-10-10); it
implements the interface with a `pub` method of its own, which hides nothing, since a base method the
class cannot see is hidden by nothing (below).
Between the braces of a class, a field is written `Type name`, with `mut` in front when it can be
assigned and its first value after `=`, which may be any expression (the owner, 2026-10-10, against a constant, which F12 keeps for a
parameter's default): it may read the parameters its primary constructor lists, whose fields are set
before the defaults of the body (§4, the owner, 2026-11-06, #39, comment 6682994925; a parameter that
the short form takes for the base is none of them, its field being the base's, K10, *(assumed, §4)*), the fields
declared above it and functions outside the class, and is evaluated in declaration order before the body of a
constructor runs, as C# evaluates an initializer, which refuses an instance member there; a field
declared below it, a method of its instance or `this` is `unknown-name` (V10), while a `static`
method of the class is a name there, as C# refuses no `static` member in an initializer (§4, the
owner, 2026-11-02); the cost is that such a
default runs at every construction, which a constant one does not (the owner, 2026-10-11, confirming the reading proposed after the review of round 14). A default that builds its own class at every construction it runs in, itself or through the default of a class it builds, runs again at each construction it makes, so that the run recurses until it ends in `stack-overflow` (O11), as C# recurses on an initializer that constructs its own class, while a guarded one, which builds it on some paths alone, runs the constructions it makes as written and ends where they end (§8, the owner, 2026-11-02, against a compile error with an id of its own for such a default, which would refuse a guarded one too); the error line names the frame in which the stack ran out, in the allocator of `std/` or in a default, as O11 reads it, so no case pins it. A method is written as a function (chapter 8). Further constructors, `static` members and `override` are written as in C#. What a
further constructor may do to a field without `mut` is K14; what a `static` field may hold is K18. A
method that a derived class overrides is marked `virtual` in the base and `override` in the derived
class, as in C#: `override` on a method that the nearest base class declaring it marks neither
`virtual` nor `override` is the compile error `override-without-virtual`, and a method of a derived
class with the name and the parameters of a base method, without `override`, is `hides-member`,
where C# hides it with a warning and offers `new`, which has no counterpart here (the owner,
2026-10-04, against every method being overridable without a marker, which would differ from C# and
make the compiler prove where a call is not virtual). As in C#, an `override` can be overridden
again further down, the whole chain of bases is searched for the method, the parameters are compared
by type and order and not by name (D11), a base method the derived class cannot see (F9) is hidden
by nothing, and a method of an interface the class implements (K6) is no base method and takes no
`override`. *(assumed: the last three readings, and that `override` with no base method at all is
`override-without-virtual` as well; proposed on 2026-10-07, when the review found the holes)* The
cost of an error where C# warns: adding a method to a base class breaks every derived class, in
every package, that already has a method of that name and those parameters. There is no `sealed`, so
every override can be overridden again, and an override has the return type of the method it
overrides, where C# 9 allows a more derived class type (the owner, 2026-10-09, confirming the
reference's reading, and on 2026-10-10 the exact return type against the covariant return of C# 9;
`sealed override` was not offered as an option): an override whose return type differs is the compile error
`override-return-type`, at the method (the owner, 2026-10-09, against `type-mismatch` at the
method's line, whose message would name no override). An override is as visible as it writes,
whatever the method it overrides writes: one without `pub` is private to its class, so a call
through the base reaches it while a call on the derived type outside the class is `not-visible`,
and one with `prot` is `prot` (§4, the owner, 2026-11-02, #110, comment 6573113786, "allowed as
written", against C#'s compile error CS0507 for an override that changes the visibility and against
the override taking the base method's visibility; the cost is a method that a call through the base
reaches and a call on the derived type is refused). A class derived from the one with such an
override takes it as the method of that name, so a call of it on that class is `not-visible` too, inside
that class as outside it, since both are outside the class that wrote the override, and its own override
is as visible as it writes, `pub` where it writes `pub` *(assumed: the nearest override stands for the method it overrides, as F13's candidates are read)*.
That `override` finds the nearest method of its name and parameters that its class sees, so a
private override between hides nothing, and the method it overrides is the base's `virtual` one
*(assumed: a private method is private to its type, F9, so no class derived from it sees it to
override or to hide)*.
A read-only property is written as C#'s expression-bodied property, `pub int Count => names.Count`: a type,
a name, `=>` and one expression. It is read as `h.Count`, as a list's `xs.Count` is (M9), without a call,
and has no setter (§4, the owner, 2026-11-03, #110, comment 6615254370, against a method `h.Count()`,
which departs from `h.Count` and from `xs.Count`, and against a `pub mut int Count` field kept in step,
which any program could write). *(assumed, §4: readings of the product pass, #110, comment 6615484562,
not put to the owner: the expression runs at every read, as C#'s does, so a read that looks like a
field's runs code; an assignment of a property, through an instance or by its name inside its class, is
`assign-immutable`; a call `h.Count()` calls the value the property gives, as a field's value is called
(F11), which is `type-mismatch` on an `int`; another form, an accessor `{ get; }`, a setter or parameters,
is `syntax` (E4); inside its class a method or another property reads it by its name alone, as a field,
and a field's default does not, where it is `unknown-name` as a method of the instance is (V10); its body
may change the instance, as a method's does (K9); a property without `pub` is private to its class, and
one marked `internal` is visible to the files of its folder, as every member marked so is (F9);
a property and a field, a method or a parameter of the primary constructor of one name are `redeclared`
(V9); a property is no field, so it stands in no list of the fields (T33), in no text `print` gives (A5)
and in no equality (K4, K5), and one named `Text` gives an instance no text (A5); a read is a call, so a
test of one read narrows no other and the read ends what a test showed of a path through a `mut` field
(N9), and its value is no place that `mut` passes (M7) or that a `mut` method changes (M5); a read
through the class's name, or by the property's name alone in a `static` member, is `static-mismatch`, as
a method of the instance is (K19); a read after `?.` (N5) or after `&` (R10), where the checker reads a
field alone, and a property marked `extern`, as a method so marked (R16), are `not supported`; a read in
a constructor written in the body is a call of a method of the instance there, before its fields are
assigned as after, and the build says `not supported` to such a constructor (K14))* *(assumed, §4: the
readings of the owner's choice of 2026-11-05, #39, comment 6657252447, marked assumed there: a property
of a base class is read through an instance of a derived class and by its name alone in the derived
class's methods, as C# reads an inherited property (K6), the nearest class up the chain that declares a
member of the name giving it; a property of a base with none of `pub`, `prot` and `internal`, or one marked `internal` read in a
derived class outside the base's folder, is `not-visible` in the derived class, as such a method of it is (F9); and `virtual`, `override` and `prot` on a property are `not supported`, as
§14 leaves them open)* A member of a derived class with the name of a property of a base, a property, a
field or a method whatever its parameters, and a property with the name of a method of a base, are
`hides-member`, since a property has no override and a method of a property's name is no overload of it
(§4, the owner, 2026-11-05, #110, comment 6659026217, against an overload of a base property by a method
with parameters and against C#'s hiding, CS0108, which `new` silences and Kurz has not; the cost is that
a derived class reuses no such name while §14 leaves a `virtual` or `override` property open). A
property or a method of a base that the class cannot see, one with none of `pub`, `prot` and `internal`, or one marked `internal`
where the class stands outside the base's folder, is hidden by nothing, as a method the class cannot see is (F9) *(assumed, §4: a reading of the answer, which names no visibility)*.
A property with the name of a field of a base is `redeclared` (V9), since the base's fields are the
class's *(assumed, §4: the id of that case)*. A `static`, `virtual`, `override` or `prot` property, one
in an interface or in the body of a `data` type, and one of `void` or of a type that lists cases beside
its success are open (§14): C# takes a `static`, `virtual`, `override` or `protected` property, one in
an interface and one in a struct or a record, refuses one of `void` (CS0547) and has no type that lists
cases; Kurz can take each form as C# does, or refuse it, as it refuses a setter. The build says
`not supported` to each.

Case: [classes/override-private.kz](../corpus/classes/override-private.kz)
```kurz
class Animal {
    pub virtual string Sound() => "quiet"
}

class Dog : Animal {
    override string Sound() => "woof"
}

Animal a = Dog()
print(a.Sound())
```

Case: [classes/override-private-not-visible.kz](../corpus/classes/override-private-not-visible.kz)
```kurz
class Animal {
    pub virtual string Sound() => "quiet"
}

class Dog : Animal {
    override string Sound() => "woof"
}

print(Dog().Sound())
```

Case: [classes/field-default-expression.kz](../corpus/classes/field-default-expression.kz)
```kurz
int Three() => 3

class Box {
    int size = Three()

    pub int Size() => size
}

print(Box().Size())
```

Case: [classes/field-default-order.kz](../corpus/classes/field-default-order.kz)
```kurz
int Note(string s, int v) {
    print(s)
    return v
}

class Counter(int Start) {
    int next = Note("b", Start + 1)
    int last = Note("c", next + 1)

    pub int Last() => last
}

print(Counter(Note("a", 1)).Last())
```

Case: [classes/field-counted.kz](../corpus/classes/field-counted.kz)
```kurz
class Bag(string Name) {
    mut List<string> items = [Name]
    string title = "bag of {Name}"
    string first = Name

    pub void Add(string x) {
        items.Add(x)
    }

    pub string Text() => "{title}: {items.Count}, first {first}"
}

b = Bag("ann")
b.Add("pen")
print(b)
```

Case: [classes/field-default-below.kz](../corpus/classes/field-default-below.kz)
```kurz
class Box(int Side) {
    int area = Side * depth
    int depth = 2

    pub int Area() => area
}

print(Box(3).Area())
```

Case: [classes/interface-method-inherited-private.kz](../corpus/classes/interface-method-inherited-private.kz)
```kurz
interface Shape {
    int Area()
}

class Base {
    int Area() => 1
}

class Dot : Base, Shape {
    pub int Size() => 2
}

print(Dot().Size())
```

Case: [classes/field-default-static-method.kz](../corpus/classes/field-default-static-method.kz)
```kurz
class Grid(int Side) {
    int cells = Area(Side) + Border()

    pub static int Area(int side) => side * side
    static int Border() => 3

    pub int Cells() => cells
}

print(Grid(2).Cells())
print(Grid(3).Cells())
```

Case: [classes/field-default-method-hides-function.kz](../corpus/classes/field-default-method-hides-function.kz)
```kurz
int Get() => 9

class C(int V) {
    int a = Get()

    pub int Get() => V

    pub int A() => a
}

print(C(1).A())
```

Case: [classes/field-default-static-hides-function.kz](../corpus/classes/field-default-static-hides-function.kz)
```kurz
int Get() => 9

class C(int V) {
    int a = Get()

    static int Get() => 2

    pub int A() => a
}

print(C(1).A())
```

Case: [classes/method-default.kz](../corpus/classes/method-default.kz)
```kurz
data Rate(int PerSecond = 1)

class Meter(int Scale) {
    pub static int Unit(Rate r = Rate()) => r.PerSecond

    pub int Read(int n, Rate r = Rate(PerSecond: 2)) => Scale * n * r.PerSecond
}

print(Meter.Unit())
print(Meter.Unit(Rate(5)))
m = Meter(10)
print(m.Read(1))
print(m.Read(1, r: Rate(3)))
```

Case: [classes/method-default-not-constant.kz](../corpus/classes/method-default-not-constant.kz)
```kurz
string Pick() => "hi"

class Greeter {
    pub static void Greet(string name, string word = Pick()) {
        print("{word} {name}")
    }
}

Greeter.Greet("Ann")
```

Case: [classes/method-default-field.kz](../corpus/classes/method-default-field.kz)
```kurz
class Counter(int Start) {
    pub int Next(int from = Start) => from + 1
}

print(Counter(1).Next(2))
```

Case: [classes/field-weak.kz](../corpus/classes/field-weak.kz)
```kurz
class Owner(string Name)

class Pet {
    pub mut weak Owner? keeper = null

    pub string Who() => keeper?.Name ?? "none"
}

o = Owner("ann")
p = Pet()
print(p.Who())
p.keeper = o
print(p.Who())
```

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

Case: [classes/override-without-virtual.kz](../corpus/classes/override-without-virtual.kz)
```kurz
class Animal {
    pub string Sound() => "quiet"
}

class Dog : Animal {
    pub override string Sound() => "woof"
}

print(Dog().Sound())
```

Case: [classes/hides-member.kz](../corpus/classes/hides-member.kz)
```kurz
class Animal {
    pub virtual string Sound() => "quiet"
}

class Dog : Animal {
    pub string Sound() => "woof"
}

print(Dog().Sound())
```

Case: [classes/interface-missing-member.kz](../corpus/classes/interface-missing-member.kz)
```kurz
interface Shape {
    int Area()
}

class Dot : Shape {
    pub string Text() => "dot"
}

print(Dot().Text())
```

Case: [classes/interface-method-private.kz](../corpus/classes/interface-method-private.kz)
```kurz
interface Shape {
    int Area()
}

class Dot : Shape {
    int Area() => 1
}

print("dot")
```

Case: [classes/override-return-type.kz](../corpus/classes/override-return-type.kz)
```kurz
class Animal {
    pub virtual string Sound() => "quiet"
}

class Dog : Animal {
    pub override int Sound() => 1
}

print(Dog().Sound())
```

Case: [classes/static-members.kz](../corpus/classes/static-members.kz)
```kurz
class Money(int Cents) {
    pub static Money Of(int units) => Money(units * 100)

    pub int Units() => Cents / 100
}

print(Money.Of(3).Units())
```

Case: [classes/property.kz](../corpus/classes/property.kz)
```kurz
class Bag(string Name) {
    mut List<int> items = [1]

    pub int Count => items.Count
    pub string Label => "{Name}: {Count}"

    pub void Add(int x) {
        items.Add(x)
    }

    pub int Twice() => Count * 2
}

b = Bag("ann")
print(b.Count)
b.Add(5)
print(b.Count)
print(b.Twice())
print(b.Label)
```

Case: [classes/property-assign.kz](../corpus/classes/property-assign.kz)
```kurz
class Bag {
    mut List<int> items = []

    pub int Count => items.Count
}

b = Bag()
b.Count = 3
print(b.Count)
```

Case: [classes/property-call.kz](../corpus/classes/property-call.kz)
```kurz
class Bag {
    mut List<int> items = []

    pub int Count => items.Count
}

b = Bag()
print(b.Count())
```

Case: [classes/property-redeclared.kz](../corpus/classes/property-redeclared.kz)
```kurz
class Bag {
    mut List<int> items = []

    pub int Count => items.Count
    pub int Count(int x) => items.Count + x
}

print(Bag().Count)
```

Case: [classes/property-mut.kz](../corpus/classes/property-mut.kz)
```kurz
void Bump(mut int n) {
    n = n + 1
}

class Counter {
    mut int count = 0

    pub int Count => count

    pub void Run() {
        Bump(mut Count)
    }
}

Counter().Run()
```

Case: [classes/property-change.kz](../corpus/classes/property-change.kz)
```kurz
class Bag {
    mut List<int> items = []

    pub List<int> Items => items
}

b = Bag()
b.Items.Add(1)
print(b.Items.Count)
```

Case: [classes/property-equality.kz](../corpus/classes/property-equality.kz)

Case: [classes/property-inherited.kz](../corpus/classes/property-inherited.kz)
```kurz
class Bag(List<int> Items) {
    pub int Count => Items.Count
}

class Named(string Name) : Bag {
    pub int Twice() => Count * 2
    pub string Label => "{Name} ({Count})"
}

n = Named([1, 2], "Ann")
print(n.Count)
Bag b = n
print(b.Count)
print(n.Twice())
print(n.Label)
```

Case: [classes/property-hides.kz](../corpus/classes/property-hides.kz)
```kurz
class Bag(List<int> Items) {
    pub int Count => Items.Count
}

class Big(int Size) : Bag {
    pub int Count(int x) => x
}

print(Big([1], 3).Count(2))
```

Case: [classes/property-static-mismatch.kz](../corpus/classes/property-static-mismatch.kz)
```kurz
class C {
    pub int Count => 1
}

print(C.Count)
```

### K18 (decided, §3, §4, §6) What a `static` field may hold

A `static` field exists once for the program, so it holds an immutable value only: `static
mut`, and a static field whose type is or holds a class, are the compile error `static-state`. A
`mut` field would be state that every actor reaches, against the record's section 6; a class
instance, mutable or not, lives in one actor's heap, which no other actor reaches into, and so
does one inside a `List<Button>`. The owner chose this on 2026-10-03, against one copy per actor.
*(assumed: that the heap is the reason for an immutable class and for a value that holds one, and
that a static field of a generic class exists once per type argument, as in C#)* The case
`static-field` shows a static field that is allowed. R13's state at the top level of a file, in a package granted `allow
raw`, is the one exception the record decides to the reason of this rule, a C library's global
variables standing open (§14) (§3, the owner, 2026-10-21), and a `static mut`
field stays `static-state` there too *(assumed: as §3 marks it)*. An assignment of a `static` field
without `mut`, which holds one immutable value for the program, is the compile error `assign-immutable`, as one of
a field without `mut` is *(proposed: the error, which the record does not name)*; one of a `static
mut` field adds nothing to the field's `static-state` (E1). A `static` field
without `=` is the compile error `field-unassigned`, at the field: no constructor assigns a `static`
field, and Kurz has no zero value it could hold (K14). The owner chose this on 2026-11-02.

Case: [classes/body.kz](../corpus/classes/body.kz)

Case: [classes/static-field.kz](../corpus/classes/static-field.kz)
```kurz
class Money(int Cents) {
    pub static int CentsPerUnit = 100
}

print(Money.CentsPerUnit)
```

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

Case: [classes/static-assign.kz](../corpus/classes/static-assign.kz)
```kurz
class Limits(int Size) {
    pub static int Max = 10
}

Limits.Max = 20
print(Limits.Max)
```

Case: [classes/static-unassigned.kz](../corpus/classes/static-unassigned.kz)
```kurz
class Limits(int Size) {
    pub static int Max
}

print(Limits(1).Size)
```

### K19 (decided, §4) A member on the other side of `static`

A member of an instance named where there is no instance, and a `static` member named through an
instance, are the compile error `static-mismatch`, its message saying on which side the member is:
a field or a method of the instance, or `this`, in a `static` method or in the initial value of a
`static` field, and one named through its class, `Counter.Next()`, a method so named with its call
or without it, `f = Counter.Next` and `f = Next` in a `static` method; a `static` field or method named
through an instance, `c.Of(3)`. C# refuses the same with CS0120, CS0026 and CS0176; the owner chose
one id whose text names the side on 2026-11-02, against reusing `unknown-name` and against an id
for each of C#'s three.

Case: [classes/static-mismatch.kz](../corpus/classes/static-mismatch.kz)
```kurz
class Counter(int Count) {
    pub int Next() => Count + 1
}

print(Counter.Next())
```

### K20 (decided, §4) What the initial value of a `static` field reads

The initial value of a `static` field reads constants and the `static` field of any class, its own
or another, in any order, by their names inside the class or as `Units.Count`, as a `static` of
Rust reads another, and the compiler computes the initial values that are constants; a member of an
instance is K19's.
Initial values that read each other in a cycle, a field that reads itself among them (`Start =
Start + 1`), are the compile error `static-cycle`, at the field of the cycle that comes first in the
file, with a message that names the fields of the cycle in order, as Rust's E0391 names a cycle of
statics. The owner chose any order on 2026-11-02, against a class declared above, against the
class's own fields alone and against a cycle decided at run time, as C#'s type initializers, which
give default values in a cycle without a word; on the same day this replaced the owner's earlier
answer, the fields of its class declared above it. *(assumed: the id, the message and the place,
which the build named and said back to the owner; the message's list starting at the field the error
stands at; in a cycle across the files of a program, the place at the field that comes first where
the files are read in the order of their paths, as for `base-cycle` (D6); every field of the cycle
refused, so that a cycle through a field one reported refused, and a field that reads one, add no
error (E1), as §4 marks it)* Inside an initial value, a read of a `static` field whose initial value
is a constant is the constant the field holds, of the field's type, so a constant built from such
reads is checked as any constant: `1 / Zero` is `constant-divide-by-zero` (T25), a product past the
field's type `constant-overflow` (T6), and an `int` field read into a `byte` field
`narrowing-conversion` (T7), as a typed `int` is *(assumed: as §4 marks it)*. An initial value that
reads a field computed at its first read (§4) is no constant either, and its own field is computed
at its own first read, which computes the field it reads first where that one is not computed yet:
each field alone, and no initializer runs for the whole class, as one `LazyLock` of Rust reads
another (§4, the owner, 2026-11-02, against the whole class at its first touch, as C#'s type
initializer, and against constants only, with such a read an error of its own id). Such a field meets
an exception in its initial value as one a function computes does (§4): the read raises it, and every
read after it the same exception *(assumed: as §4 marks it)*. Outside an initial value, a read of a
`static` field is a value of its type and no constant, as C# reads a `static readonly` field: an
`int` field does not go into a `byte` as an integer literal would (T7) *(assumed: as §4 marks it)*.
The cycle this rule names is one between the names initial values read; what a field that a
function computes at its first read (§4) does where the function reads, on a path of its calls, a
field whose initializer reads the first stands open (§14).

Case: [classes/static-initializer.kz](../corpus/classes/static-initializer.kz)
```kurz
class Money(int Cents) {
    pub static int Max = CentsPerUnit * Units.Count
    pub static int CentsPerUnit = 100
}

class Units {
    pub static int Count = 1000
}

print(Money.Max)
```

Case: [classes/static-cycle.kz](../corpus/classes/static-cycle.kz)
```kurz
class Low {
    pub static int Min = High.Max / 2
}

class High {
    pub static int Max = Low.Min * 2
}

print(Low.Min)
```

Case: [classes/static-reads-itself.kz](../corpus/classes/static-reads-itself.kz)
```kurz
class Counter {
    pub static int Start = Start + 1
}

print(Counter.Start)
```

Case: [classes/static-constant-limits.kz](../corpus/classes/static-constant-limits.kz)
```kurz
class Limits {
    pub static int Q = 10 / Two
    pub static int Two = 2
    pub static int Big = Half * 4
    pub static int Half = 536870911
    pub static long Wide = Small
    pub static int Small = 7
}

print(Limits.Q)
print(Limits.Big)
print(Limits.Wide)
```

Case: [classes/static-divide-by-zero.kz](../corpus/classes/static-divide-by-zero.kz)
```kurz
class Ratio {
    pub static int Q = 1 / Zero
    pub static int Zero = 0
}

print(Ratio.Q)
```

Case: [classes/static-overflow.kz](../corpus/classes/static-overflow.kz)
```kurz
class Size {
    pub static int Big = Half * 4
    pub static int Half = 1073741824
}

print(Size.Big)
```

Case: [classes/static-narrowing.kz](../corpus/classes/static-narrowing.kz)
```kurz
class Level {
    pub static byte Small = Wide
    pub static int Wide = 7
}

print(Level.Small)
```

Case: [classes/static-reads-first-read.kz](../corpus/classes/static-reads-first-read.kz)
```kurz
int Compute() {
    print("computing A")
    return 3
}

int Never() {
    print("computing C")
    return 0
}

class Table {
    pub static int A = Compute()
    pub static int B = A + 1
    pub static int C = Never()
}

print(Table.B)
print(Table.A)
```

No case: the place of a cycle across the files of a program, since the compiler reads one file of a
program and a case is one file.

### K14 (decided, §4) Further constructors and fields without `mut`

A constructor, and only a constructor, may assign each field without `mut` once, before its body
ends, as C# lets a constructor set a `readonly` field; a second assignment there is
`assign-immutable` like one anywhere else. A further constructor of a class with a primary
constructor calls the primary one first, with `: this(...)` as C# 12 requires, and a class without a
primary constructor has C#'s constructors. The owner chose this on 2026-10-03, against a class with
a primary constructor having no further constructor, which would have made a second way to build an
instance a static method or a second class; the cost is the flow analysis that proves "once, before
the end". The rest the owner decided on 2026-10-04, against C#'s rules, under which a constructor
assigns any number of times and an unassigned field gets the type's zero value, `default(T)`, which
Kurz has nowhere else: the once is per instance. A field that the primary constructor or an `=` in
the body (K7) sets is assigned by no constructor; a field without `mut` and without `=` is assigned
exactly once on every path of every constructor that does not chain, and a constructor that leaves
it unassigned on a path, or reads it first, is the compile error `field-unassigned`, as is such a
field in a class with a primary constructor, which a call of that constructor would leave unset. A
`mut` field without `=` is assigned on every path of such a constructor as well, any number of
times, and left unassigned on a path it is `field-unassigned` too, because Kurz has no zero value it
could hold (the owner, 2026-10-09, confirming the reference's reading; what it rules out is a `mut` field
that starts at a zero value as a C# field does, which Kurz has not). In a class with a primary constructor, which assigns no such
field, a `mut` field written between the braces without `=` is `field-unassigned` as a field without
`mut` is (the owner,
2026-10-10, confirming the reading proposed after the review of round 13). Reading such a field
before its first assignment is that error too, as the read of a field without `mut` is (the owner,
2026-10-10). In a class with no constructor at all, a field of the body without `=`, `mut` or not,
is `field-unassigned` too, at the field's line: the empty constructor of such a class assigns it no
more than a primary constructor does (the owner, 2026-11-02, against `not supported`, and the
field's line against the class's). The call of the primary one is direct; a further constructor
without it is `constructor-must-chain`. A constructor written in the body is private without `pub`,
as every member is (F9); the primary constructor, and the empty constructor of a class without one,
are visible wherever the class is.

Case: [classes/field-unassigned-none.kz](../corpus/classes/field-unassigned-none.kz)
```kurz
class Tally {
    mut int count

    pub int Count() => count
}

print(Tally().Count())
```

Case: [classes/field-read-before-assignment.kz](../corpus/classes/field-read-before-assignment.kz)
```kurz
class Counter {
    mut int hits

    pub Counter() {
        print(hits)
        hits = 0
    }

    pub int Hits() => hits
}

print(Counter().Hits())
```

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

Case: [classes/field-unassigned-primary.kz](../corpus/classes/field-unassigned-primary.kz)
```kurz
class Point(int X) {
    int y

    pub int Y() => y
}

print(Point(1).Y())
```

### K8 (decided, §4) How a class names the fields that count

The fields that count (K5) are named in a clause after the head of the class:
`class User(int Id, mut string Name) equal by Id`. `equal` and `by` are core words (L12). A class
without the clause compares as K4 says. Whether a `mut` field can be named is K12; the form of the
clause with several fields, and its errors, is K15. A clause that names a `weak` field is the
compile error `equal-by-weak`, at the clause, naming the field, since a freed target would change
`==` (§4, the owner, 2026-11-02, #110, comment 6580137405); a field whose type reaches a `weak`
reference as K4 reads it counts as one, and the id is the record's proposal *(assumed, as §4 marks
them)*.

Case: [classes/equality-named.kz](../corpus/classes/equality-named.kz)

Case: [classes/equal-by-weak.kz](../corpus/classes/equal-by-weak.kz)
```kurz
class Owner(string Name)
class Pet(string Name, weak Owner? Keeper) equal by Name, Keeper

print(Pet("Rex", null) == Pet("Rex", null))
```

### K12 (decided, §4) A `mut` field in the clause

A `mut` field can be named in the clause of K8. When such a field is assigned, the hash of the
instance changes, so an instance that is a key of a map at that moment would no longer be found
under it; such a class as the key type of a `Map` is the compile error `mutable-key`, and a program
keys such a map by a copy of the field (§4, the owner, 2026-10-30, against no `mut` field in the
clause, against a check of each key's hash in test builds, and against the clause as it stood, under
which the entry is lost with no report). That answer, which set aside refusing a `mut` field in the
clause, confirms the reading of the owner's answer on `mut` fields that the record marked as
assumed. The id and its line, the one that writes the key type, are the owner's too (§4, 2026-10-30,
"Accept both"). Whether the error
reaches a key type that holds such a class, or one that a type parameter stands for, stands open
(§14).

Case: [classes/equality-named-mut.kz](../corpus/classes/equality-named-mut.kz)
```kurz
class Tag(mut string Name) equal by Name

x = Tag("a")
y = Tag("a")
print(x == y)
y.Name = "b"
print(x == y)
```

Case: [classes/mutable-key.kz](../corpus/classes/mutable-key.kz)
```kurz
class Tag(mut string Name) equal by Name

mut counts = Map<Tag, int>()
counts[Tag("a")] = 1
print(counts[Tag("a")] ?? 0)
```

### K15 (assumed, §4) The form of the `equal by` clause

The clause names one or more fields of the class, with `, ` between them: `equal by Id, Kind`. It
stands after the primary constructor and after the `: Base` part (K6, K10), before a body in braces.
It can name a field of the primary constructor, a field declared in the body and a field of the base
(K13 says what the base contributes). A name that is not a field of the class is the compile error
`equal-by-unknown`. The owner accepted this reading by its id on 2026-10-09, in round 13, without
its text shown, so it is assumed and not decided.

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
the class lists, and the arguments of the base are expressions over them. An instance of the class
is built in C#'s order: the fields of the parameters it lists, then the defaults of its fields of the
body, then the arguments of the base, then the base's part in the same order, the fields of the
parameters the base lists, the defaults of the base's fields of the body and the base's constructor,
then its own constructor (§4, the owner, 2026-11-03, #39, comment 6600033456, and 2026-11-06, comment
6682994925); the build says `not supported` to a field in the body of a class with a base or with
classes derived from it (K7). Whether a parameter
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
explicit form, and with another type it is K17's error (the owner, 2026-10-10, confirming the
reference's reading, which the record had not carried). Such a parameter stands where the base's
constructor has it, among the base's fields in their order and before the class's own parameters; one
listed out of that order is `base-field-clash` (K17) too (the owner, 2026-10-11, confirming the reading proposed after the review of round 14). The defaults of the base keep their
place, so the class's own parameters come after them, and a call that leaves one out names the
rest (D11). The owner chose this on 2026-10-03, against taking the empty constructor when the
base has no primary one, which would have given one form two readings; the cost is that such a
base forces the explicit form.

Case: [classes/inherit-short-field-clash.kz](../corpus/classes/inherit-short-field-clash.kz)
```kurz
class User(string Name)

class Admin(int Name) : User

print(Admin("Ann", 1).Name)
```
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
b = Admin("Bea", 2)
print(b.Name)
print(b.Level)
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

### K17 (assumed, §4) A parameter that clashes with a field of the base

A parameter that has the name of a field of the base and another type (`class Admin(int Name) :
User("x")`) would give the class two fields of one name; a parameter that has the name and the type
of a base field but is not what the base receives (`class Admin(string Name, int Level) :
User("guest")`) would be a field the base sets to something else. Both are the compile error
`base-field-clash`; C# rejects the first, a positional parameter whose type differs from the inherited
member it names, and only warns about the second, so Kurz is stricter there. The owner accepted this
reading by its id on 2026-10-09, in round 13, without its text shown, so it is assumed and not decided.
The second clash exists in the explicit form alone, where the base receives what the class passes; in
the short form (K16) a parameter with a base field's name and another type is the same error, for the
same two fields of one name.

Case: [classes/inherit-field-clash.kz](../corpus/classes/inherit-field-clash.kz)
```kurz
class User(string Name)

class Admin(int Name) : User("x")

print(Admin(1).Name)
```
