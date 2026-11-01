# 5. Data and unions

### D1 (decided, §4, §8) `data`

`data Name(Type Field, ...)` declares a record that is a value (M1): it changes only through a
`mut` variable that holds it (D4, M4). The list in brackets is its primary constructor and its
fields. A field is read with a dot.

Case: [data/declare.kz](../corpus/data/declare.kz)
```kurz
data User(int Id, string Name)

u = User(1, "Ann")
print(u.Id)
print(u.Name)
```

### D2 (assumed, §4) Making a value

A value is made by the type's name followed by the arguments, in order or by name and with
defaults (D11); a type without fields is its bare name (D8). There is no `new`; every sample in
the record is written this way.

Case: [data/declare.kz](../corpus/data/declare.kz)

### D3 (decided, §4) Equal by content

Two values of a `data` type are equal when their fields are equal. The fields compare as the fields of
a C# record do: `Point(nan, 0.0) == Point(nan, 0.0)` is `true`, every `data` value equals itself, and a key
of a `Map` is found again, while `==` on a `double` field alone says `false` for NaN, as in C# (the
owner, 2026-10-09, against the field's `==`, under which such a value is unequal to itself and cannot
be found in a map). Each field compares by the equality Kurz gives its type, a class field by K4 or
K5, and NaN equal to NaN is the one thing taken from C#'s `Equals`, which holds for a `double` or `float` among
the named fields of K5 as well (the owner, 2026-10-10, confirming the reading proposed after the
review of round 13, and the NaN field under K5); a class declares no method named `Equals` or
`GetHashCode`, which is K5's `equals-method` (§4, the owner, 2026-10-30), and whether a `data` type may declare
a method named `Equals` or `GetHashCode` stands open (§14). A field of a union (D9) compares equal when the two values hold the same case and the two
values of that case are equal, as Rust's derived `PartialEq` compares a field of an enum (§4, the
owner, 2026-10-23). The two values of that case compare as a field of the case's type does, so a
`double` or `float` case that holds NaN equals itself, as a C# record calls `Equals` on a field of
type `object` (§4, the owner, 2026-10-24); there Kurz departs from Rust's `PartialEq`, which
compares the two values with their `==`, under which NaN is unequal to itself (§4). `==` takes two
values of one union type outside a field as well, the same first case and the same set of the others
(D9, O1) *(assumed, §4: one type, as Rust compares two values of one enum, and `!=` as the negation of `==`
here, in the refusal below too)*, alone, as a list's items, in a list a field holds and as a
map's keys, and compares the case and then the value as a field of a union compares, a `double` or
`float` case by IEEE's `==` where the union stands alone or in a list that stands alone, and as C#'s
`Equals` in a key, a union that is a key itself among them (M9) (§4, the owner, 2026-10-30, "Case and
value, as Rust") *(assumed, §4: a union key's case read as a key under "Equals inside data/keys")*. A
union with no `null` case beside a value of one of its cases takes no `==` and no `!=`, on either side:
`type-mismatch`, as Rust refuses an enum beside the payload of one of its variants, `Ok(5) == 5` on a
`Result<i32, String>` (§4, the owner, 2026-10-30, "Refuse, as Rust"); a side whose type is nullable is
lifted first (N10), so `(int | string)? a == 1` is `type-mismatch`, the union the lift leaves standing
beside a value of one of its cases, while `int? b == 5`, the shape of Rust's `Some(5) == 5`, which Rust
refuses, compares as C# lifts it, Kurz following C# there and not Rust. A field that is a
list compares by its items (M9), and its `double` or `float` items, and the values of its `double?` or
`float?` items, at any depth, compare as C#'s `Equals` compares them, NaN equal to NaN and 0.0 equal to
-0.0, `null` equal to `null`, so that such a value equals itself, where `==` on two lists outside a
`data` value compares those items by IEEE's `==` (M9) (§4, the owner, 2026-10-30). `==` on a `data` value that holds a map, a function value, a file or a socket, in a field
or at any depth below one, through a field's fields, a list's items, a union's cases and the fields of a
type derived from it, is `type-mismatch` at the `==` (§4, the owner, 2026-10-30).

Case: [classes/equal-by-nan.kz](../corpus/classes/equal-by-nan.kz)
```kurz
class Point(double X, double Y, mut int Hits) equal by X, Y

double z = 0.0
nan = z / z
print(Point(nan, 0.0, 0) == Point(nan, 0.0, 1))
```
Case: [data/equality.kz](../corpus/data/equality.kz)
```kurz
data Point(int X, int Y)

print(Point(1, 2) == Point(1, 2))
print(Point(1, 2) == Point(2, 1))
```

Case: [data/equality-nan.kz](../corpus/data/equality-nan.kz)
```kurz
data Point(double X, double Y)

double z = 0.0
nan = z / z
print(Point(nan, 0.0) == Point(nan, 0.0))
```

Case: [data/equality-union-nan.kz](../corpus/data/equality-union-nan.kz)
```kurz
data Box(double | string V)

double z = 0.0
nan = z / z
print(Box(nan) == Box(nan))
print(Box(nan) == Box("NaN"))
```

Case: [data/equality-holds-map.kz](../corpus/data/equality-holds-map.kz)
```kurz
data Box(Map<string, int> Counts)

b = Box(Map<string, int>())
print(b == b)
```

Case: [data/equality-holds-file.kz](../corpus/data/equality-holds-file.kz)
```kurz
data Log(File Out)

void Same(Log a, Log b) {
    print(a == b)
}

print(1)
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

Case: [data/equality-union-field.kz](../corpus/data/equality-union-field.kz)
```kurz
data Box(int | string V)

print(Box(1) == Box(1))
print(Box(1) == Box(2))
print(Box(1) == Box("1"))
```

Case: [data/union-equality.kz](../corpus/data/union-equality.kz)
```kurz
// `==` on two values of a union compares their case, then the value of that case (D3)
int | string a = 1
int | string b = "1"
int | string c = 1
print(a == b)
print(a == c)
print(a != b)
```

Case: [data/union-nan.kz](../corpus/data/union-nan.kz)
```kurz
// a `double` case of a union that stands alone compares by IEEE's `==`, NaN unequal to itself (D3)
double z = 0.0
int | double n = z / z
print(n == n)
```

Case: [data/union-equality-case-value.kz](../corpus/data/union-equality-case-value.kz)
```kurz
// a union beside a value of one of its cases takes no `==`, a nullable one after its lift too (N10), as Rust
// refuses `Ok(5) == 5` on a `Result<i32, String>` (D3)
int | string a = 1
print(a == 1)
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

Case: [data/entry-equality.kz](../corpus/data/entry-equality.kz)
```kurz
// two entries compare by their fields, as every `data` value does (D3)
print(Entry("Ann", 32) == Entry("Ann", 32))
print(Entry("Ann", 31) == Entry("Ann", 32))
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

Case: [data/equality-nested-generic.kz](../corpus/data/equality-nested-generic.kz)
```kurz
data Wrap<T>(T Value)

a = Wrap<Wrap<Map<int, int>>>(Wrap<Map<int, int>>(Map<int, int>()))
print(a == a)
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

### D4 (decided, §4) Immutable

A field of a `data` value that is held in an immutable variable cannot be assigned: the compile
error `assign-immutable`. Through a `mut` variable it can (M4). The variable decides, not the
field: the two cases make the same assignment, and only the variable differs.

Case: [data/immutable.kz](../corpus/data/immutable.kz)
```kurz
data Point(int X, int Y)

p = Point(1, 2)    // the variable decides: through `mut p` the same line compiles (field-through-mut.kz)
p.X = 3
print(p.X)
```

Case: [data/field-through-mut.kz](../corpus/data/field-through-mut.kz)
```kurz
data Point(int X, int Y)

mut p = Point(1, 2)
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

Case: [data/entry-with.kz](../corpus/data/entry-with.kz)
```kurz
// `with` copies an `Entry` as it copies every `data` value, and the value it copied from keeps its fields (D5)
a = Entry("Ann", 31)
b = a with { Value = 32 }
print(b.Value)
print(a.Value)
```

### D6 (decided, §4) A `data` type may inherit from another

A value of the derived type can be used wherever the base type is required.

Case: [control/match-unreachable-arm.kz](../corpus/control/match-unreachable-arm.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

void Show(User | NotFound u) {
    match u {
        User x => print("user {x.Name}")
        Admin a => print("admin {a.Level}")
        NotFound => print("none")
    }
}

Show(Admin("Ann", 2))
```

Case: [control/match-derived-arm.kz](../corpus/control/match-derived-arm.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

void Show(User | NotFound u) {
    match u {
        Admin a => print("admin {a.Level}")
        User x => print("user {x.Name}")
        NotFound => print("none")
    }
}

Show(Admin("Ann", 2))
Show(User("Bea"))
Show(NotFound)
```

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

A `data` type that is its own base, directly or through others, is the compile error `base-cycle`,
reported at the base that closes the cycle, for the condition C# reports as CS0146, a circular base
type dependency. The owner chose this on 2026-10-21, against `type-mismatch` (T1); the cost is one
more id. Only the condition is C#'s: C# reports CS0146 at the name of every type on the cycle
(checked 2026-10-21 with the .NET SDK 10.0.402: two classes that name each other as their base gave
two CS0146, three classes on a cycle three, a class that names itself one, and a class that only
derives from a cycle none). To find that base the files of a program are read in the order of their
paths, and the base that closes a cycle is the base of the declaration that completes it in that
reading. The owner chose this on 2026-10-21, against `base-cycle` at every base on the cycle, under
which no order is needed and a cycle through three types is three errors; the cost is that renaming
a file can move the error to another line. The path of a file is the one relative to the program's
root directory, with `/` as the separator, and two paths are compared code point by code point, case
kept, so that the order is the same on every platform. The owner chose this on 2026-10-21, against
the order in which the files are given to the compiler, as a C# project lists them; the cost is that
the order of the files is a rule of the language, and that renaming a file can move the error, as
above. Each file is read from its top; a type that names itself as its base is a cycle of one; a
cycle is one error, and a type that only derives from a cycle gets none *(assumed: the answers named
the base that closes the cycle and the order of the files, and did not say in which order a file is
read, whether a type that names itself as its base is a cycle of one, whether a cycle is one error,
or what a type that only derives from a cycle gets)*.

Case: [data/inherit-cycle.kz](../corpus/data/inherit-cycle.kz)
```kurz
data Node(int Value) : Node

print(1)
```

Case: [data/inherit-cycle-of-three.kz](../corpus/data/inherit-cycle-of-three.kz)
```kurz
data Admin(int Level) : User
data User(int Id) : Person
data Person(string Name) : Admin

print(1)
```

A type that only derives from a cycle, `Leaf` below, gets no error of its own, by the reading above.

Case: [data/inherit-from-a-cycle.kz](../corpus/data/inherit-from-a-cycle.kz)
```kurz
data Node(int Value) : Node
data Leaf(int Size) : Node

print(1)
```

### D7 (decided, §4) How `data` inheritance is written

`data Admin(int Level) : User` declares a `data` type that inherits from `User`. Its constructor
takes the fields of the base first, then its own. A field of the base with a default keeps its
place, so a call that leaves it out names the arguments after it (D11), as K16 says of the same
form for a class.

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

### D9 (decided, §4, §5) Unions

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

A value of a union stands where another union is required when its first case goes to the first case
of that union and each of its other cases to a case of it after the first, each by being that type,
by widening into it (T8) or by deriving from it (D6), since the first case is the success case (O1):
an `A | B` stands where `A | B | C` is required, and where `B | A` is required it is the compile
error `type-mismatch` (T1), as in Rust `Result<A, B>` and `Result<B, A>` are two types with no
conversion between them. The owner chose this on 2026-10-21, against accepting it wherever the other
union lists all of its cases, in whatever order; the cost is that swapping the order takes a
`match`. So `long | NotFound r = Find(1)` compiles where `Find` returns an `int | NotFound`, while a
`User | Admin` where `User | NotFound` is required is `type-mismatch`, since its `Admin` would
become a success (O1). The owner chose this on 2026-10-21, against the first case being the same
type while the other cases may widen, as Rust's `Result<i32, E>` is no `Result<i64, E>`, under which
`long | NotFound r = Find(1)` is refused while `long r = Find(1)` compiles, and against exact types
everywhere, with no widening or derivation between unions; the cost is that a conversion between
unions checks each case. A case after the first that would go to two of the cases after the first is
`type-mismatch` too, as a value that would go to two cases is *(assumed: the answers named a value
and a propagated case, not a case of a union)*. A case after the first that is one of the cases
after the first goes there all the same, and no other fit by widening or derivation is looked for,
so an `int | short` stands where `int | short | long` is required. The owner chose this on
2026-10-21, as for a value (T8), against two fits being `type-mismatch` even beside an exact case,
under which such a union needs a `match`; the cost is that adding a case to a union can change
without a word which case an existing union's case goes to, as the `short` of an `int | short` goes
to `long` where `int | long` is required and to `short` where `int | long | short` is, while under
the option turned down that addition is a compile error. The owner kept this choice with that cost
shown on 2026-10-21.

A value that is no case goes to a case as T8 says, and one that would go to two cases by deriving
from them, an `Admin` where `User | Person` is required with `Admin : User : Person`, is
`type-mismatch`, as one that would widen into two is (T8); a variable of the case's type picks the
case, as `User u = admin` makes the `Admin` a `User` (D6), where a conversion picks it for a number
(T8) and a `match` on the call for a propagated case (O3) *(assumed: the variable; the answers named
a conversion or a `match`, and a `data` value has no conversion to its base)*. The owner chose this
on 2026-10-21, against the nearest case, the most derived base or the narrowest type, as C#'s
better-conversion rule picks it, under which derivation and propagation would answer differently
from widening; the cost is that such code needs that conversion, variable or `match`. A value that
is one of the cases is that case, a `data` value among them, and no other fit by derivation is
looked for: a `User` where `User | Person` is required, with `User : Person`, is a `User`. The owner
chose this on 2026-10-21, against two fits being `type-mismatch` even beside an exact case, under
which that `User` is `type-mismatch` too; C#'s better-conversion rule prefers the identity
conversion, and Rust has only the exact fit; the cost is that adding a case to a union can change
without a word which case an existing value goes to, as a `User` goes to `Person` where
`int | Person` is required and to `User` where `int | Person | User` is, while under the option
turned down that addition is a compile error. The owner kept this choice with that cost shown on
2026-10-21. A number literal where a union is required takes the union's one number case, and has
its own type where the union has two or more, and a duration literal takes the union's one time
case, as T8 and L17 say.

Case: [data/union-more-cases.kz](../corpus/data/union-more-cases.kz)
```kurz
data A(int X)
data B(int Y)
data C(int Z)

A | B x = B(2)
A | B | C y = x
match y {
    A a => print("a {a.X}")
    B b => print("b {b.Y}")
    C c => print("c {c.Z}")
}
```

Case: [data/union-order.kz](../corpus/data/union-order.kz)
```kurz
data A(int X)
data B(int Y)

A | B x = B(2)
B | A y = x
match y {
    B b => print("b {b.Y}")
    A a => print("a {a.X}")
}
```

Case: [types/widening-into-union.kz](../corpus/types/widening-into-union.kz)
```kurz
byte b = 1
int | string v = b
match v {
    int i => print(i)
    string s => print(s)
}
```

Case: [types/widening-into-two-cases.kz](../corpus/types/widening-into-two-cases.kz)
```kurz
byte b = 1
short | int v = b
match v {
    short s => print("short {s}")
    int i => print("int {i}")
}
```

Case: [types/conversion-picks-the-case.kz](../corpus/types/conversion-picks-the-case.kz)
```kurz
byte b = 1
short | int w = short(b)
match w {
    short x => print("short {x}")
    int y => print("int {y}")
}
```

Case: [data/union-case-widens.kz](../corpus/data/union-case-widens.kz)
```kurz
data NotFound

int | NotFound Find(int id) {
    if id == 1 {
        return 10
    }
    return NotFound
}

long | NotFound r = Find(1)
match r {
    long n => print(n)
    NotFound => print("none")
}
```

Case: [data/union-case-derives.kz](../corpus/data/union-case-derives.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

Admin | NotFound a = Admin("a", 2)
User | NotFound u = a
match u {
    User x => print("user {x.Name}")
    NotFound => print("none")
}
```

Case: [data/union-case-after-the-first.kz](../corpus/data/union-case-after-the-first.kz)
```kurz
data User(string Name)
data Admin(int Level) : User
data NotFound

// the Admin is the case Admin, and no other fit is looked for (D9)
User | Admin a = Admin("a", 1)
User | NotFound u = a
match u {
    User x => print(x.Name)
    NotFound => print("none")
}
```

Case: [data/union-case-is-its-case.kz](../corpus/data/union-case-is-its-case.kz)
```kurz
short s = 1
byte | short x = s
int | short | long y = x
match y {
    int i => print("int {i}")
    short n => print("short {n}")
    long l => print("long {l}")
}
```

Case: [data/derives-from-two-cases.kz](../corpus/data/derives-from-two-cases.kz)
```kurz
data Person(string Name)
data User(int Id) : Person
data Admin(int Level) : User

User | Person v = Admin("a", 1, 2)
match v {
    User u => print(u.Id)
    Person p => print(p.Name)
}
```

Case: [data/value-is-its-case.kz](../corpus/data/value-is-its-case.kz)
```kurz
data Person(string Name)
data User(int Id) : Person

User | Person v = User("a", 1)
match v {
    User u => print("user {u.Id}")
    Person p => print(p.Name)
}
```

Case: [types/literal-into-two-cases.kz](../corpus/types/literal-into-two-cases.kz)
```kurz
long | double x = 5
match x {
    long n => print("long {n}")
    double d => print("double {d}")
}
```

Case: [types/literal-one-number-case.kz](../corpus/types/literal-one-number-case.kz)
```kurz
byte | string x = 5
match x {
    byte n => print("byte {n}")
    string s => print(s)
}
```

Case: [types/literal-two-number-cases.kz](../corpus/types/literal-two-number-cases.kz)
```kurz
byte | short x = 5
match x {
    byte b => print("byte {b}")
    short s => print("short {s}")
}
```

Case: [types/literal-widens-into-one-case.kz](../corpus/types/literal-widens-into-one-case.kz)
```kurz
short | long x = 5
match x {
    short s => print("short {s}")
    long n => print("long {n}")
}
```

Case: [types/literal-one-time-case.kz](../corpus/types/literal-one-time-case.kz)
```kurz
int | duration d = 106752days
match d {
    int n => print("int {n}")
    duration t => print("duration {t}")
}
```

Two unions with the same first case and the same other cases are one type, whatever the order of the
cases after the first, so `List<A | B | C>` and `List<A | C | B>` are one type. The owner chose this
on 2026-10-21, against two types, the order being part of the type as a generic type is invariant in
C#, under which a list of one cannot be passed where the other is required; the cost is that the
compiler orders the cases after the first itself, by the numbers of their tags (below), and that a reader
sees one type written two ways. A case's tag is the number its type has in the program, one number
for each type of the program, so that the order the compiler gives the cases after the first is that
of those numbers, and a case that goes to its own type in another union keeps its tag (§5, the
owner, 2026-10-21); the cost is that the numbers may differ between two builds of a program. How the
compiler gives out the numbers is its own, and no form of the language outside `raw` shows them, while
code that reads a tag through a pointer, the runtime's among it, reads the numbers of its own build;
the numbers are given out when the whole program is compiled, as every program is (§2); a tag is as
wide as the largest number among its union's cases needs, a byte while a program has at most 256
types, so that a union may take a wider tag than a count of its own cases would need; a case that widens or derives
into another case takes that case's number, with its value converted *(assumed: as §5 marks
them)*.

Case: [data/union-order-after-the-first.kz](../corpus/data/union-order-after-the-first.kz)
```kurz
data A(int X)
data B(int Y)
data C(int Z)

void Count(List<A | B | C> xs) {
    print(xs.Count)
}

mut items = List<A | C | B>()
items.Add(B(1))
items.Add(C(2))
Count(items)
```

### D10 (assumed, §4) Small values are plain values

A small `data` value is compiled as a plain value without a reference count, automatically.
There is no `struct` keyword.

No case: it changes speed and layout, not what a program prints.

### D11 (decided, §8) Default values and arguments by name

A field or a parameter can have a default value, written after its name: `int Count = 1`. An
argument can be passed by name: `Item(ProductId: 7)`. Both work as in C# before version 7.2:
arguments without a name fill the parameters in order, arguments by name follow them in any
order, a named argument never stands before an unnamed one, and a parameter that has a default
can be left out. *(the version is a proposed reading: C# 7.2 also lets a named argument in its own
position precede unnamed ones)* A name that matches no parameter, a parameter that gets two
arguments, and a parameter without a default that gets none are the compile error
`argument-mismatch` *(proposed)*.

Case: [functions/named-argument-order.kz](../corpus/functions/named-argument-order.kz)
```kurz
int Log(string s, int v) {
    print(s)
    return v
}

int Add(int a, int b) => a + b

print(Add(b: Log("b", 2), a: Log("a", 1)))
```

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

Case: [data/argument-twice.kz](../corpus/data/argument-twice.kz)
```kurz
data Item(int ProductId, int Count = 1)

print(Item(7, ProductId: 8).Count)
```

### D12 (decided, §4) `enum`

`enum Plan { Free, Pro }` declares a type with a fixed set of named values, written `Plan.Free`.
It is the short form of a union of cases without fields (D8, D9): a `match` on it has to cover every
value (O8), and two values are equal when they are the same value. A value is not an
integer. It has a number only where the declaration writes one; how that is written and read is
D18. An arm names a value as an expression names it, `Plan.Free =>`, and an arm of the type,
`Plan p =>`, takes every value. A `match` on a union that holds an enum lists the enum's values
beside the union's other cases: `Plan.Free`, `Plan.Pro` and `Invalid` are the arms of a `match` on
`Plan | Invalid`, and the union's case `Plan` is covered when every value has its arm (§4, the
owner, 2026-10-29). An arm of a value that an earlier arm names, or that follows the arm of its
type, and an arm of the type after arms that name every value, are `unreachable-arm` (C4), and so
is `else` after arms that cover every case of the subject, an enum's values counting for the enum
(C9). A `match` on an enum without values covers it with no arm, and an arm of its type or `else`
there is `unreachable-arm` *(assumed: as §4 marks it)*.

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

Case: [data/enum-arm-after-every-value.kz](../corpus/data/enum-arm-after-every-value.kz)
```kurz
enum Plan { Free, Pro }

void Show(Plan p) {
    match p {
        Plan.Free => print("free")
        Plan.Pro => print("pro")
        Plan other => print(other)
    }
}

Show(Plan.Pro)
```

Case: [data/enum-empty-arm.kz](../corpus/data/enum-empty-arm.kz)
```kurz
enum Never {}

void Show(Never n) {
    match n {
        Never x => print(x)
    }
}

print("declared")
```

Case: [data/enum-empty-else.kz](../corpus/data/enum-empty-else.kz)
```kurz
enum Never {}

void Show(Never n) {
    match n {
        else => print(1)
    }
}

print("declared")
```

Case: [data/enum-empty.kz](../corpus/data/enum-empty.kz)
```kurz
enum Never {}

void Show(Never n) {
    match n {
    }
}

print("declared")
```

Case: [data/enum-union-arms.kz](../corpus/data/enum-union-arms.kz)
```kurz
enum Plan { Free = 1, Pro = 2 }

void Show(int n) {
    match Plan.From(n) {
        Plan.Free => print("free")
        Plan.Pro => print("pro")
        Invalid => print("invalid")
    }
}

Show(2)
Show(7)
Show(1)
```

### D13 (decided, §4) Flags

`flags Access { Read, Write, Run }` declares a type whose values are sets of the listed names. It
gives what `[Flags]` and `HasFlag` give in C#. Each name is one bit, and the compiler numbers the
bits in order unless the declaration writes the number (D19). A name alone, `Access.Read`, is the
set that holds that name. `set.Has(Access.Read)` is `true` when the set holds the name. A set is not
one case, so a `match` cannot list it case by case. Such a `match` is the compile error `syntax`
(E4), the one id for text no rule gives a meaning; a parser cannot tell it from a `match` on an
enum, so the check is the type checker's (the owner, 2026-10-09, against an id of its own,
`match-on-flags`, the rejected option). The error is at the line of the `match`, as the case
expects, the line E4 gives every error only the type checker can see (the owner, 2026-10-10,
confirming the reading proposed after the review of round 13). How sets are combined is D17; the
case tests a set of one name.

Case: [data/flags.kz](../corpus/data/flags.kz)
```kurz
flags Access { Read, Write, Run }

p = Access.Read
print(p.Has(Access.Read))
print(p.Has(Access.Write))
```

Case: [data/flags-match.kz](../corpus/data/flags-match.kz)
```kurz
flags Access { Read, Write, Run }

p = Access.Read
match p {
    Access.Read => print("read")
    else => print("other")
}
```

### D14 (decided, §8) The fields of a primary constructor can be read from outside

A field that is declared in a primary constructor can be read wherever its type is visible, as
the positional members of a C# record can. It is the exception to F9, which makes a member
private to its type unless it says `pub`: without it every such field would carry `pub`. Whether
it can be assigned from outside its type is not a question of privacy but of M4, D4 and K2, which
hold from inside and from outside alike: a `mut` field of a class instance through every
reference, a field of a `data` value through a `mut` variable, and nothing else. The case under
N9 that assigns `a.Email = null` from a top-level function relies on this. *(proposed reading)*

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

### D17 (decided, §4) How sets of flags are combined

Sets of flags (D13) are combined with the bit operators, as in C# (T22). `a | b` holds the names
of both sets, `a & b` the names they share, and `a ^ b` the names that exactly one of them
holds. `set & ~Access.Write` takes a name out. Every `flags` type has the name `None` for the
set that holds nothing. `set.Has(other)` is `true` when the set holds every name of `other`, as
`HasFlag` is in C#. The record marks as *(assumed)* what `~set` is on its own: the names of the
type that the set does not hold. The case uses `~` only under `&`, where that reading and the
bits of C# give the same set.

Case: [data/flags-combine.kz](../corpus/data/flags-combine.kz)
```kurz
flags Access { Read, Write, Run }

p = Access.Read | Access.Write
print(p.Has(Access.Read))
print(p.Has(Access.Run))
print(p.Has(Access.Read | Access.Write))
print(p.Has(Access.Read | Access.Run))
q = p & ~Access.Write
print(q.Has(Access.Write))
print(q.Has(Access.Read))
print(Access.None.Has(Access.Read))
r = p ^ Access.Read
print(r.Has(Access.Write))
print(r.Has(Access.Read))
```

### D18 (decided, §4) The number of an `enum` value

A declaration writes the number of a value after `=`: `enum Plan { Free = 1, Pro = 2 }`. The two
directions are members of the type. `Plan.Pro.Number` is the number of a value, an `int`, and
`Plan.From(2)` is the value of a number, which it takes as an `int`, since a number is an `int`
(D20). Its result is `Plan | Invalid`: a number that no value has is an outcome (O1), not an
exception. `Plan(2)`, the form §4 sets aside, is `syntax` *(assumed: as §4 marks it)*. `Invalid` is a `data` type without fields that every file sees,
declared before the file's own types, so that a file's own `data Invalid` is `redeclared` (V9);
an arm names it as it names any case type (C4) (§4, the owner, 2026-10-29). Two values of an enum
compare with `<`, `<=`, `>` and `>=` by their numbers, as C# compares them: with `Free = 2` and
`Pro = 1`, `Plan.Free > Plan.Pro` is `true` (§4, the owner, 2026-10-29). A value of an enum is a
constant, which a default can be (F12) *(assumed: as §4 marks it)*. What a declaration that
breaks the shape gets, and an order of values without numbers, is D20.

Case: [data/enum-number.kz](../corpus/data/enum-number.kz)
```kurz
enum Plan { Free = 1, Pro = 2 }

print(Plan.Pro.Number)
match Plan.From(1) {
    Plan p => print(p == Plan.Free)
    Invalid => print("invalid")
}
match Plan.From(7) {
    Plan p => print(p == Plan.Free)
    Invalid => print("invalid")
}
```

Case: [data/enum-call.kz](../corpus/data/enum-call.kz)
```kurz
enum Plan { Free = 1, Pro = 2 }

print(Plan(2))
```

Case: [data/invalid-redeclared.kz](../corpus/data/invalid-redeclared.kz)
```kurz
data Invalid

print(Invalid)
```

Case: [data/enum-order-negative.kz](../corpus/data/enum-order-negative.kz)
```kurz
enum Level { High = 1, Low = -1 }

print(Level.Low < Level.High)
print(Level.High < Level.Low)
```

Case: [data/enum-order.kz](../corpus/data/enum-order.kz)
```kurz
enum Plan { Free = 2, Pro = 1 }

bool Below(Plan a, Plan b) {
    return a < b
}

print(Plan.Free > Plan.Pro)
print(Below(Plan.Free, Plan.Pro))
print(Below(Plan.Pro, Plan.Free))
```

### D20 (decided, §4) Numbers that do not fit the shape of D18

A declaration that writes the same number for two values, one that writes a number for some values
and not for all, and a use of `.Number` or `From` on an `enum` whose declaration writes no number
are the compile error `enum-number`. The alternative, C#'s, lets two values share a number and makes
them equal, which gives `From` a value nobody can predict; the error keeps D12's "a value has a
number only where one is written" simple. As Rust reads a declaration, a value's name written twice
is `redeclared`; a number is a constant expression, a negative one and one folded from literals
(T18) among them; and an enum without values is a type. A number's type is `int`, where Rust's is
`isize` (§4), so that one past the range of an `int` is `constant-overflow`, a literal without a
suffix, which takes the type `int` there (L10), and a folded one (T6) alike. A value may be named `Number` or `From`, the
names of D18's two members, as Rust compiles a value of either name, and the place of the name
tells which is meant: `Plan.From(2)`, a call, is D18's conversion and `Plan.From` the value, while
`p.Number` on a value is its number and `Plan.Number` on the type the value (§4, the owner,
2026-10-30). A comparison with `<`, `<=`, `>` or `>=` of values of an
enum whose declaration writes no number is `enum-number`, and so is a number that is not a constant
expression, while a number of another type gets the error a value of that type gets where an `int`
is required (T1): `type-mismatch` for `1.5` or `"two"`, `narrowing-conversion` for `5L` and
`sign-mix` for `2u` *(assumed: as §4 marks them)*.

Case: [data/enum-number-twice.kz](../corpus/data/enum-number-twice.kz)
```kurz
enum Plan { Free = 1, Pro = 1 }

print(Plan.Pro.Number)
```

Case: [data/enum-number-some.kz](../corpus/data/enum-number-some.kz)
```kurz
enum Plan { Free = 1, Pro }

print(Plan.Free.Number)
```

Case: [data/enum-number-none.kz](../corpus/data/enum-number-none.kz)
```kurz
enum Plan { Free, Pro }

print(Plan.Pro.Number)
```

Case: [data/enum-order-no-number.kz](../corpus/data/enum-order-no-number.kz)
```kurz
enum Plan { Free, Pro }

print(Plan.Free < Plan.Pro)
```

Case: [data/enum-value-twice.kz](../corpus/data/enum-value-twice.kz)
```kurz
enum Plan { Free, Pro, Free }

print(Plan.Pro)
```

Case: [data/enum-value-member-name.kz](../corpus/data/enum-value-member-name.kz)
```kurz
enum Plan { Free = 1, From = 2, Number = 3 }

void Show(int n) {
    match Plan.From(n) {
        Plan.From => print("from")
        Plan p => print(p)
        Invalid => print("invalid")
    }
}

print(Plan.From)
print(Plan.Number.Number)
Show(2)
Show(7)
```

Case: [data/enum-number-negative.kz](../corpus/data/enum-number-negative.kz)
```kurz
enum Level { High = 1, Low = -1 }

print(Level.Low.Number)
```

Case: [data/enum-number-folded.kz](../corpus/data/enum-number-folded.kz)
```kurz
enum Level { Low = 1, High = 2 + 3 }

print(Level.High.Number)
```

Case: [data/enum-empty.kz](../corpus/data/enum-empty.kz)

Case: [data/enum-from-no-number.kz](../corpus/data/enum-from-no-number.kz)
```kurz
enum Plan { Free, Pro }

print(Plan.From(1))
```

Case: [data/enum-number-not-constant.kz](../corpus/data/enum-number-not-constant.kz)
```kurz
int Two() => 2
enum Plan { Free = 1, Pro = Two() }

print(Plan.Pro)
```

Case: [data/enum-number-type.kz](../corpus/data/enum-number-type.kz)
```kurz
enum Plan { Free = 1, Pro = "two" }

print(Plan.Pro)
```

Case: [data/enum-number-real.kz](../corpus/data/enum-number-real.kz)
```kurz
enum Plan { Free = 1, Pro = 1.5 }

print(Plan.Free)
```

Case: [data/enum-number-long.kz](../corpus/data/enum-number-long.kz)
```kurz
enum Plan { Free = 1, Pro = 5L }

print(Plan.Pro)
```

Case: [data/enum-number-unsigned.kz](../corpus/data/enum-number-unsigned.kz)
```kurz
enum Plan { Free = 1, Pro = 2u }

print(Plan.Pro)
```

Case: [data/enum-number-past-int.kz](../corpus/data/enum-number-past-int.kz)
```kurz
enum Plan { Free = 1, Pro = 2147483648 }

print(Plan.Pro.Number)
```

### D19 (assumed, §4) The number of a set of flags

The number of a set of flags is written and read as the number of an `enum` value is (D18). A
declaration can write the number of each name, which is the value of its bit, as in C#:
`flags Access { Read = 1, Write = 2, Run = 4 }`. Without written numbers the names get 1, 2, 4
and so on, in their order. `set.Number` is the sum of the numbers the set holds.
`Access.From(5)` is the set with that number, and `Invalid` when the number has a bit that no
name has. A number written for a name that is not one bit (`0`, `3`), a bit written for two
names, a declaration with more names than the `int` has bits (31 names, the sign bit excluded),
and a name `None`, which D17 gives every flags type, are the compile error `flags-number`.
*(proposed)*

Case: [data/flags-not-one-bit.kz](../corpus/data/flags-not-one-bit.kz)
```kurz
flags Access { Read = 1, Write = 3 }

print(Access.Write.Number)
```

Case: [data/flags-number.kz](../corpus/data/flags-number.kz)
```kurz
flags Access { Read, Write, Run }

p = Access.Read | Access.Run
print(p.Number)
match Access.From(3) {
    Access a => print(a.Has(Access.Write))
    Invalid => print("invalid")
}
match Access.From(8) {
    Access a => print(a.Has(Access.Write))
    Invalid => print("invalid")
}
```
