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
data Journal(File Out)

void Same(Journal a, Journal b) {
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
// a union beside a value of one of its cases takes no `==`, as Rust refuses `Ok(5) == 5` on a
// `Result<i32, String>` (D3)
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

A value of the derived type can be used wherever the base type is required. An arm of a `data`
type covers the cases of a union whose types derive from it (C4; §5, the owner, 2026-11-09, #408,
comment 6748445211, "Cover, as a base class"), and one that no case derives from is `type-mismatch`,
as an arm of a type the value cannot have is (C4; §5, the owner, 2026-10-11).

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

Case: [control/match-data-arm-no-derived.kz](../corpus/control/match-data-arm-no-derived.kz)
```kurz
data Beast(string Name)
data Stray(string Name)
data Gone

string Say(Stray | Gone v) => match v {
    Beast b => b.Name
    else => "gone"
}

print(Say(Gone))
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

### D21 (decided, §4) A derived `data` type without fields of its own

`data Dot : Shape` writes no fields of its own and has the fields of its base (D7): its
constructor takes them in their order, and a value of it is made as a value of any type with
fields (D2). Its bare name is its one value (D8) only where no base above it has a field either;
where one has, the bare name stands for no value (D2).
The cost is that this reading depends on every base above the type: where `Tag` of `data Mark : Tag`,
which has no field, takes one, each bare `Mark` becomes `syntax` (D2) and each `Mark(...)` takes that field, in
any file, with no edit to `Mark`. What softens it is that each use of `Mark` whose reading changes
is a compile error at its line: its one use was the bare name, since `Mark()` of a type without
fields is `syntax` (D8), and the bare name becomes `syntax` (D2). A derived type that writes fields
of its own meets the same when its base takes a field, wherever an argument of its call no longer
fits, since the fields of the base come first in its constructor (D7): such a call lacks an argument
for a field without a default, `argument-mismatch` (D11), or else passes one of a type the field it
moved to does not take, `type-mismatch` (T1). The cost that stays is the silent case, a positional argument that moves into
the base's new field before a default, in a call of any type derived from it, `Mark(...)` among them
where `Tag` already has a field: where the moved arguments fit and a default fills the last
field, the call compiles with another reading. With `data Admin(int Level = 1) : User`, `Admin(5)`
gives `Level` 5, and once `User` takes a first field `int Id` it gives `Id` 5 and `Level` 1 (the
owner, 2026-11-04, comment 6643376941 on issue 156).

Case: [data/inherit-no-own-fields.kz](../corpus/data/inherit-no-own-fields.kz)
```kurz
data Shape(int A, int B)
data Dot : Shape
data Spot : Dot
data Tag
data Mark : Tag

int Sum(Shape s) => s.A + s.B

d = Dot(1, 2)
print(d.A)
print(d)
print(Sum(d))
print(Spot(4, 5))
m = Mark
print(m == Mark)
```

Case: [data/inherit-no-own-fields-bare.kz](../corpus/data/inherit-no-own-fields-bare.kz)
```kurz
data Shape(int A, int B)
data Dot : Shape

print(Dot)
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
int Show(string s, int v) {
    print(s)
    return v
}

int Add(int a, int b) => a + b

print(Add(b: Show("b", 2), a: Show("a", 1)))
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

Case: [classes/method-default-override-missing.kz](../corpus/classes/method-default-override-missing.kz)
```kurz
class A {
    pub virtual int F(int x = 1) => x
}

class B : A {
    pub override int F(int x) => x * 10
}

A a = B()
print(a.F())
print(B().F())
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
case tests a set of one name. Two sets of one `flags` type are equal under `==` when they hold the
same names, which is the same number (D19), and `!=` when they do not; a set is the key of a map,
hashed by its number, and a field that holds a set counts in the `==` of a `data` value (D3). Sets
have no order: `<`, `<=`, `>` and `>=` on two sets are `type-mismatch` (T18), and so is a sort by a
set (X8) (§4, the owner, 2026-11-11, issue 452, "Equal by names, key, no order"). `==` and `!=` on
sets of two `flags` types, or on a set and a value of another type, are `type-mismatch` (T18) as
well (§4, the owner, 2026-11-11, issue 452, "All as built, decided"). A nullable form of the set's
type and `null` are no other type there: `==` and `!=` with them are lifted as with every nullable
value (N10), so that `s == null` and `s == Access.Read` compile for an `Access? s` (§4, the owner,
2026-11-11, issue 452, "Not another type, as built"). In a generic function's copy for a set, `<` on
two values of its type parameter is this `type-mismatch` too, as T33 says of the arm
`TypeKind.Enum`, the kind of a `flags` type.

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

Case: [data/flags-equal.kz](../corpus/data/flags-equal.kz)
```kurz
flags Access { Read, Write, Run }

a = Access.Read | Access.Write
b = Access.Write | Access.Read
print(a == b)
print(a != b)
print(a == Access.Read)
mut names = Map<Access, string>()
names[a] = "read and write"
print(names[b] ?? "none")
print(names[Access.Run] ?? "none")
```

Case: [data/flags-order.kz](../corpus/data/flags-order.kz)
```kurz
flags Access { Read, Write, Run }

print(Access.Read < Access.Write)
```

Case: [data/flags-equal-two-types.kz](../corpus/data/flags-equal-two-types.kz)
```kurz
flags Access { Read, Write }
flags Mode { Fast, Slow }

print(Access.Read == Mode.Fast)
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
(K7), but for a property, which §14 leaves open. A `data` type that derives from another, or that
another derives from, takes `virtual` and `override` in its body as a class does (K7) (§4, the owner,
2026-11-11, "A class's rules"); an `override` is `mut` exactly when the method it overrides is, else `override-mut` (§4, the owner, 2026-11-12, comment 6819137865, H1); a type derived from one with a body holds each type's fields of the primary constructor and of the body together, the base's first, in `==`, the hash and the text (H2); a `with` on a value of a base that holds a value of a derived type gives a value of the derived type, every field of its body computed again (H3), and of the body of every type of its chain, in the order of a construction, the derived type's first, the result's static type the receiver's *(assumed, §4: that order and that type)* *(assumed, §4: the rest as a class's, H4 of that product pass, which the owner left at its default: an override takes `pub` as written (the owner, 2026-11-02); a method of a derived type with the name and the parameter types of a base's `pub` method and no `override` is `hides-member`, an `override` of a method neither `virtual` nor `override` `override-without-virtual`, as is an `override` in a type without a base, which overrides nothing, whether another type derives from it or none does, and one with another return type `override-return-type`; a field of a derived type's body named like a field of a base or a `pub` method of a base's body, a method of it named like a field of a base, and a field of its primary constructor named like a field of a base's body, are `redeclared` (V9), and a method of it named like a method of a base's body is read by the rules of an override, `hides-member` and its kin (K7); the initializers of a derived type's body run before its base's, in a class's order (the owner, 2026-11-03, #39, comment 6600033456), so that an initializer of a derived type reads the fields of every primary constructor, which the construction gives first, and the fields of its own body above it, and a field of a base's body is no name there (V10); a private member of a base's body is visible to the base alone (F9), and a member of a derived type's body may take the name of a private method of a base's body, which it does not see, as a member of a derived class may (K7); and the text of a value is that of the type it holds when the program runs, its own `Text()` or the nearest one a base's body declares, else A3's; the cost is a branch on the number of the type a value holds at each call of a `virtual` method that a type derived from the receiver's overrides, and at each `with` and each text of a value of a type another derives from)*; a field declared in the body is a field as one of the primary constructor is, in `==`, the hash and the
text (§4, the owner, 2026-11-11, "Every field counts"), and in `with` what B4 and comment 6821275386
make it, computed again and never named; and a `static` method of the body is read as one of a class body is (K7, K18, K19) (§4, the
owner, 2026-11-11, option (a) of the question of comment 6788371936). A field of the body is written
`Type Name = value`, `pub` before it where code outside the type reads it (F9) and `mut` where it is
assigned, and has an initializer: one without `=` is `field-unassigned` (§4, the owner, 2026-11-12,
comment 6819137865, B1). The initializer reads the fields of the primary constructor and those of the
body above it (B2), and runs once, when the value is made, by a construction or `T.Make` (B3); a field
below it, a method of the type and `this` are `unknown-name` there, as in the default of a class's
field (K7) *(assumed, §4)*. `with` computes the fields of the body again from the copy's fields (B4),
and a `with` that names one is `assign-immutable` (§4, the owner, 2026-11-12, comment 6821275386). The text of one type writes them after the fields of its primary constructor, in the order of their declaration (B5), and the text of a derived type each type's fields together, the base's first (H2), one
without `pub` among them (B6), which code outside the type reads nowhere else: a read of it there is `not-visible` (F9), and an assignment of it there is `not-visible` too, with `mut` or without; where it has no `mut`, this is the error and not `assign-immutable`, since its access is read before the assignment, as C# reports a
private field's access before its `readonly` *(assumed, §4: the assignment and that order)*. One without `mut` is given by its initializer alone, so an assignment of it, or of a field of the value it holds,
inside its type, or outside it where it is `pub`, is `assign-immutable`, in a `mut` method too; one with `mut` is assigned by a `mut` method (B7), and through a `mut` variable as D4 lets a field of the primary
constructor be, inside its type, or outside it where it is `pub` too *(assumed, §4)*. A field of the body that holds an instance of a class takes what a field of the primary
constructor may (B9), and one named like a field above it, a method or a parameter is `redeclared` (V9)
(B10). A method without `mut` changes nothing of
the value it was called on: an assignment of a field in it, or of a field on a path of fields from it,
is `assign-immutable`, and a call of a `mut` method on its value or on such a path, a call of a `mut`
method of its type by its name alone and a field passed as `mut` are `mut-required`. A `mut` method is
called on a `mut` variable, on a path of `data` fields from one and on a `mut` field of an instance
(M5), and §14 leaves its call on an item of a list or a value of a map open. Its value is what its call
takes as `mut` (M7), so a read of that value that the call's arguments still hold is
`changed-while-held`. Its call takes its receiver, the place of its value, before its arguments (F12),
and a `mut` method on a `mut` field of an instance writes into the place its call took: where an
argument replaces the instance that held the field, into that field of the old instance, and where
an argument assigns the field itself, into the new value, as C# writes through `ref this` (§4, the owner, 2026-11-12, issue 39, comment 6819138235, item 13). The cost is a write into an instance the path no longer holds, which a later read through the path does not see, with no error. *(assumed, §4: that the call holds a count of the instance whose field it took, from before its arguments to the end of the statement, so that an argument that replaces the instance does not free the place the call writes into, at the cost of a count taken and given back for each such call; that a `mut` method whose own body assigns the field its call took reads and writes after that the new value, as an argument that assigns the field does, while what it wrote before that goes with the value the assignment replaced, at the cost of those writes, lost with no error; and that an argument that reaches the receiver's place through an instance of a class, `b` in `b.D.Reset(b)`, holds that instance and not the value of its field, so that it is no read `changed-while-held` names, at the cost of a method that reads its argument's field and sees it change while it writes through its receiver, the aliasing that error refuses where a read holds the value itself)* A method takes no name of a field of its type, and inside a method a parameter or
a variable takes no name of a member of its type (`redeclared`, V9), and a `Text()` without `pub`
gives no text to `print`, an interpolation or `string.Join` (X6), inside its type too (`not-visible`, A9)
*(assumed, §4: those readings)*.

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

Case: [data/body-fields.kz](../corpus/data/body-fields.kz)
```kurz
data Order(int Price, int Count) {
    pub int Total = Price * Count
    string Note = "{Total} in all"
}

o = Order(3, 2)
print(o)
print(o.Total)
p = o with { Price = 5 }
print(p)
print(o == Order(3, 2))
print(o == p)
```

Case: [data/body-field-mut.kz](../corpus/data/body-field-mut.kz)
```kurz
data Counter(int Start) {
    pub mut int Count = Start

    pub mut void Increment() {
        Count = Count + 1
    }
}

mut c = Counter(5)
c.Increment()
c.Increment()
print(c)
print(c with { Start = 1 })
```

Case: [data/body-field-with.kz](../corpus/data/body-field-with.kz)
```kurz
data Order(int Price, int Count) {
    pub int Total = Price * Count
}

o = Order(3, 2)
p = o with { Total = 5 }
print(p)
```

Case: [data/body-field-unassigned.kz](../corpus/data/body-field-unassigned.kz)
```kurz
data Order(int Price, int Count) {
    pub int Total
}

print(Order(3, 2))
```

Case: [data/body-field-not-mut.kz](../corpus/data/body-field-not-mut.kz)
```kurz
data Counter(int Start) {
    pub int Count = Start

    pub mut void Increment() {
        Count = Count + 1
    }
}

mut c = Counter(5)
c.Increment()
print(c)
```

Case: [data/body-field-private.kz](../corpus/data/body-field-private.kz)
```kurz
data Order(int Price, int Count) {
    int Total = Price * Count
}

o = Order(3, 2)
print(o.Total)
```

Case: [data/body-field-private-assigned.kz](../corpus/data/body-field-private-assigned.kz)
```kurz
data Order(int Price, int Count) {
    mut int Total = Price * Count
}

mut o = Order(3, 2)
o.Total = 1
```

Case: [data/body-field-private-immutable-assigned.kz](../corpus/data/body-field-private-immutable-assigned.kz)
```kurz
data Order(int Price, int Count) {
    int Total = Price * Count
}

mut o = Order(3, 2)
o.Total = 1
```

Case: [data/body-field-later.kz](../corpus/data/body-field-later.kz)
```kurz
data Order(int Price, int Count) {
    pub int Total = Price * Count + Fee
    pub int Fee = 1
}

print(Order(3, 2))
```

Case: [types/type-fields-body.kz](../corpus/types/type-fields-body.kz)
```kurz
data Order(int Price, int Count) {
    pub int Total = Price * Count
    string Note = "{Total} in all"
}

void ShowFields<T>(T value) {
    for f in T.Fields {
        print("{f.Name} = {f.Get(value)}")
    }
}

T Remake<T>(T value) => T.Make(f => f.Get(value))

ShowFields(Order(3, 2))
print(Remake(Order(5, 2)))
```

Case: [data/text-declared.kz](../corpus/data/text-declared.kz)
```kurz
data User(int Id, string Name) {
    pub string Text() => "{Name} ({Id})"
}

u = User(1, "Ann")
print(u)
print("got {u}")
```

Case: [values/mut-method-place.kz](../corpus/values/mut-method-place.kz)
```kurz
data D(int N, string S) {
    pub mut void Add(int k) {
        N = N + k
        S = "{S}+"
    }
}

class Inner(mut D V)
class H(mut Inner In)

int Replace(H h) {
    h.In = Inner(D(5, "new{2}"))
    return 1
}

top = H(Inner(D(1, "old{1}")))
old = top.In
top.In.V.Add(Replace(top))
print(top.In.V.N)
print(top.In.V.S)
print(old.V.N)
print(old.V.S)
```

Case: [values/mut-method-field-assigned.kz](../corpus/values/mut-method-field-assigned.kz)
```kurz
data D(int N, string S) {
    pub mut void Add(int k) {
        N = N + k
        S = "{S}+"
    }
}

class Inner(mut D V)

int SetV(Inner i) {
    i.V = D(7, "set{1}")
    return 1
}

box = Inner(D(1, "old{1}"))
box.V.Add(SetV(box))
print(box.V.N)
print(box.V.S)
```

Case: [data/body-inherit.kz](../corpus/data/body-inherit.kz)
```kurz
data Shape(int W, int H) {
    pub int Area = W * H

    pub virtual string Kind() => "shape"

    pub string Size() => "{W} by {H}"
}

data Box(int D) : Shape {
    pub int Volume = W * H * D

    pub override string Kind() => "box"
}

string Describe(Shape s) => "{s.Kind()} {s.Area}"

b = Box(2, 3, 4)
print(b)
print(b.Area)
print(b.Volume)
print(Describe(b))
print(Describe(Shape(1, 5)))
Shape wide = b
t = wide with { W = 5 }
print(t)
print(t == Box(5, 3, 4))
print(Box(6, 3, 1).Size())
```

Case: [data/body-inherit-order.kz](../corpus/data/body-inherit-order.kz)
```kurz
int Note(string s, int v) {
    print("{s} {v}")
    return v
}

data A(int X) {
    pub int P = Note("base", X)
}

data B(int Y) : A {
    pub int Q = Note("own", Y)
}

print(B(1, 2))
```

Case: [data/body-inherit-mut.kz](../corpus/data/body-inherit-mut.kz)
```kurz
data Counter(int N) {
    pub virtual mut void Bump() {
        N = N + 1
    }

    pub int Doubled() => N * 2
}

data Twice(int K) : Counter {
    pub override mut void Bump() {
        N = N + 2
    }
}

mut Counter c = Twice(1, 7)
c.Bump()
print(c)
print(c.Doubled())
mut d = Counter(0)
d.Bump()
print(d)
print(Twice(5, 1).Doubled())
```

Case: [data/body-override-mut.kz](../corpus/data/body-override-mut.kz)
```kurz
data Counter(int N) {
    pub virtual mut void Bump() {
        N = N + 1
    }
}

data Fixed(int K) : Counter {
    pub override void Bump() {
        print(K)
    }
}

print(Fixed(1, 2))
```

Case: [values/mut-method-body-assigned.kz](../corpus/values/mut-method-body-assigned.kz)
```kurz
data D(int N, string S) {
    pub mut void Reset(Inner b) {
        keep = S
        N = N + 100
        b.V = D(5, "r1")
        N = N + 1
        S = "{S}+{keep}"
    }
}

class Inner(mut D V)

box = Inner(D(1, "x"))
box.V.Reset(box)
print(box.V.N)
print(box.V.S)
```

Case: [data/body-inherit-defaults.kz](../corpus/data/body-inherit-defaults.kz)
```kurz
data A(int X = 3) {
    pub int P = X + 100
}

data B(int Y = 7) : A {
    pub int Q = Y * 2
}

print(B())
print(B(1))
print(B(1, 2))
print(B(Y: 5))
```

Case: [data/body-override-nothing.kz](../corpus/data/body-override-nothing.kz)
```kurz
data Shape(int W) {
    pub override string Kind() => "shape"
}

data Box(int D) : Shape

print(Box(1, 2).Kind())
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
expression, while a constant of another type gets the error a value of that type gets where an `int`
is required (T1): `type-mismatch` for `1.5` or `"two"`, `narrowing-conversion` for `5L` and
`sign-mix` for `2u` *(assumed: as §4 marks them)*. The text of a number that is not a constant says
so, then the rule: "the number of `Pro` is no constant: a name's number is a constant expression of
type `int`, a literal or literals that T6 folds (D20)" (§4, the owner, 2026-11-11, issue 452, "'is no
constant', both").

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
names and a name `None`, which D17 gives every flags type, are the compile error `flags-number`.
*(proposed)*

A declaration that writes a number for some names and not for all, and a number that is not a
constant expression, are `flags-number` too, as D20 says of an `enum`; so is a declaration of
more than 31 names, one for each bit of an `int` below its sign, whose names are counted before
their numbers are read, so that it is one `flags-number`, at its 32nd name, whether it writes
numbers or not, however many names follow (§4, the owner, 2026-11-11, issue 452, "Same for 33+, as
built"). `From` and `Has` take arguments named `number` and `flag` (D11). A flags type
without names is a type, whose one set is `None` (§4, the owner, 2026-11-11, issue 452, "All as
built"). A declaration of 32 names that also writes a wrong number gets that one `flags-number`
alone, at its 32nd name (§4, the owner, 2026-11-11, issue 452, "All as built, decided"), a number of
another type or outside the range of an `int` among the wrong numbers, with no `type-mismatch`,
`narrowing-conversion`, `sign-mix` or `constant-overflow` (§4, the owner, 2026-11-11, issue 452,
"Count alone, as built", and for `type-mismatch` and `sign-mix` "All four, as built", comment
6800642597). A negative number is `flags-number`, `-4` and `-2147483648` among them, the
minus before a literal being part of the literal (L17) (§4, the owner, 2026-11-11, issue 452, "All as
built, decided").

The rest of a declaration is read as D20 reads an `enum`'s. In a declaration of at most 31 names, a
number that is no constant is
`flags-number` before its type is read, `Write = Two()` with `long Two()` among them, as D20 reads an
`enum`'s (§4, the owner, 2026-11-11, issue 452, "Constant first"), and its text says so, then the
rule: "the number of `Write` is no constant: a name's number is a constant expression of type
`int`, a literal or literals that T6 folds (D19)" (§4, the owner, 2026-11-11, issue 452, "'is no
constant', both"). In a declaration of at most 31 names, a constant's type and range are read
before its bit: a constant of another type gets the error a value of that type gets where an `int`
is required (T1), whether or not its value is one bit, `type-mismatch` for `1.5` or `"two"`,
`narrowing-conversion` for `5L` and `3L` and `sign-mix` for `2u` and `3u`, and a number outside the
range of an `int` is `constant-overflow` (L10, T6), `2147483648`, the bit of the sign, among them,
while a negative number inside it is `flags-number`. A call on the type by any name other than
`From`, `Access.Has(x)` and `Access.Number(...)` among them, is `unknown-name` (V10) whatever names
the type declares, since a flags type's one function is `From` (§4, the owner, 2026-11-11, issue 452, "All as
built, decided" and "As built: unknown-name").

A name may be `From`, `Number` or `Has`, and the place of the name tells which is meant:
`Access.From(5)`, a call, is the conversion and `Access.From` the set, `s.Has(x)` the test and
`Access.Has` the set, `s.Number` on a set its number and `Access.Number` on the type the set.
*(proposed)*

Case: [data/flags-number-type.kz](../corpus/data/flags-number-type.kz)
```kurz
flags Access { Read = 1, Write = "two" }

print(Access.Write)
```

Case: [data/flags-number-long.kz](../corpus/data/flags-number-long.kz)
```kurz
flags Access { Read = 1, Write = 2L }

print(Access.Write)
```

Case: [data/flags-number-unsigned.kz](../corpus/data/flags-number-unsigned.kz)
```kurz
flags Access { Read = 1, Write = 2u }

print(Access.Write)
```

Case: [data/flags-number-past-int.kz](../corpus/data/flags-number-past-int.kz)
```kurz
flags Access { Read = 1, Write = 2147483648 }

print(Access.Write.Number)
```

Case: [data/flags-number-negative.kz](../corpus/data/flags-number-negative.kz)
```kurz
flags Sign { Low = 1, High = -2147483648 }

print(Sign.Low)
```

Case: [data/flags-number-order.kz](../corpus/data/flags-number-order.kz)
```kurz
flags Access { Read = 1, Write = 3L }

print(Access.Write)
```

Case: [data/flags-number-minus.kz](../corpus/data/flags-number-minus.kz)
```kurz
flags Sign { Low = 1, High = -4 }

print(Sign.Low)
```

Case: [data/flags-number-not-constant.kz](../corpus/data/flags-number-not-constant.kz)
```kurz
long Two() => 2L

flags Access { Read = 1, Write = Two() }

print(Access.Read)
```

Case: [data/flags-type-call.kz](../corpus/data/flags-type-call.kz)
```kurz
flags Access { Read, Write }

print(Access.Has(Access.Read))
```

Case: [data/flags-type-call-declared.kz](../corpus/data/flags-type-call-declared.kz)
```kurz
flags Access { Read, Has }

print(Access.Has(Access.Read))
```

Case: [data/flags-number-count-first.kz](../corpus/data/flags-number-count-first.kz)
```kurz
flags Many {
    F1 = 3, // not one bit, a number error the count comes before
    F2 = 2,
    F3 = 4,
    F4 = 8,
    F5 = 16,
    F6 = 32,
    F7 = 64,
    F8 = 128,
    F9 = 256,
    F10 = 512,
    F11 = 1024,
    F12 = 2048,
    F13 = 4096,
    F14 = 8192,
    F15 = 16384,
    F16 = 32768,
    F17 = 65536,
    F18 = 131072,
    F19 = 262144,
    F20 = 524288,
    F21 = 1048576,
    F22 = 2097152,
    F23 = 4194304,
    F24 = 8388608,
    F25 = 16777216,
    F26 = 33554432,
    F27 = 67108864,
    F28 = 134217728,
    F29 = 268435456,
    F30 = 536870912,
    F31 = 1073741824,
    F32 = 1 // the bit 1, held by no name of one bit: F1's 3 is not one bit
}

print(Many.F2)
```

Case: [data/flags-number-count-type.kz](../corpus/data/flags-number-count-type.kz)
```kurz
flags Many {
    F1 = 3L,
    F2 = 2,
    F3 = 4,
    F4 = 8,
    F5 = 16,
    F6 = 32,
    F7 = 64,
    F8 = 128,
    F9 = 256,
    F10 = 512,
    F11 = 1024,
    F12 = 2048,
    F13 = 4096,
    F14 = 8192,
    F15 = 16384,
    F16 = 32768,
    F17 = 65536,
    F18 = 131072,
    F19 = 262144,
    F20 = 524288,
    F21 = 1048576,
    F22 = 2097152,
    F23 = 4194304,
    F24 = 8388608,
    F25 = 16777216,
    F26 = 33554432,
    F27 = 67108864,
    F28 = 134217728,
    F29 = 268435456,
    F30 = 536870912,
    F31 = 1073741824,
    F32 = 1
}

print(Many.F2)
```

Case: [data/flags-number-count-unsigned.kz](../corpus/data/flags-number-count-unsigned.kz)
```kurz
flags Many {
    F1 = 3u, // sign-mix in a declaration of at most 31 names, its type read before its bit
    F2 = 2,
    F3 = 4,
    F4 = 8,
    F5 = 16,
    F6 = 32,
    F7 = 64,
    F8 = 128,
    F9 = 256,
    F10 = 512,
    F11 = 1024,
    F12 = 2048,
    F13 = 4096,
    F14 = 8192,
    F15 = 16384,
    F16 = 32768,
    F17 = 65536,
    F18 = 131072,
    F19 = 262144,
    F20 = 524288,
    F21 = 1048576,
    F22 = 2097152,
    F23 = 4194304,
    F24 = 8388608,
    F25 = 16777216,
    F26 = 33554432,
    F27 = 67108864,
    F28 = 134217728,
    F29 = 268435456,
    F30 = 536870912,
    F31 = 1073741824,
    F32 = 1
}

print(Many.F2)
```

Case: [data/flags-number-count-real.kz](../corpus/data/flags-number-count-real.kz)
```kurz
flags Many {
    F1 = 1.5, // type-mismatch in a declaration of at most 31 names, its type read before its bit
    F2 = 2,
    F3 = 4,
    F4 = 8,
    F5 = 16,
    F6 = 32,
    F7 = 64,
    F8 = 128,
    F9 = 256,
    F10 = 512,
    F11 = 1024,
    F12 = 2048,
    F13 = 4096,
    F14 = 8192,
    F15 = 16384,
    F16 = 32768,
    F17 = 65536,
    F18 = 131072,
    F19 = 262144,
    F20 = 524288,
    F21 = 1048576,
    F22 = 2097152,
    F23 = 4194304,
    F24 = 8388608,
    F25 = 16777216,
    F26 = 33554432,
    F27 = 67108864,
    F28 = 134217728,
    F29 = 268435456,
    F30 = 536870912,
    F31 = 1073741824,
    F32 = 1
}

print(Many.F2)
```

Case: [data/flags-number-count-many.kz](../corpus/data/flags-number-count-many.kz)
```kurz
flags Many {
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    F25,
    F26,
    F27,
    F28,
    F29,
    F30,
    F31,
    F32,
    F33
}

print(Many.F2)
```

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
