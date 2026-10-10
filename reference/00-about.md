# The Kurz reference

[kurz-design.md](../kurz-design.md) records what was decided and why. This reference says the
same thing rule by rule, precisely enough to write a compiler and its tests against it. It adds
nothing to the design: where the record is silent, a rule here is marked `proposed` or `open`,
and it stays that way until the owner has answered.

It covers the sequential core: what a program without actors needs. Actors, distribution,
keywords, jobs, tests, packages and clients (sections 6, 7 and 9 to 13 of the record) are
outside it.

## The status of a rule

Every rule has an id and a status, and cites the sections of the record it comes from.

- `decided`: the owner chose it.
- `assumed`: the record marks it *(assumed)*: it was proposed to the owner and not objected to.
- `proposed`: the record does not say it. It is here because the cases need something to stand
  on, and it is what C# does or the smallest thing that works. Nobody has confirmed it. A case
  may rely on it and falls with it.
- `open`: a fork nobody has chosen. The rule states the options. No case may rely on it.

The status is about the meaning. Spellings mostly come from the record's samples, which the
record calls illustrative unless its section 8 lists them. A spelling can change without the rule
changing.

An open rule is answered in a design round, by its id. The answer goes into the record first,
then the rule changes its status here.

## Cases

Every sample in this reference is a file of the conformance corpus, under `corpus/`. A file
starts with a header and an empty line; the rest is the program.

```text
// expect: output                     the program compiles, runs and prints exactly these lines
// | 9
// rules: V1                          the rules this case shows

// expect: error assign-immutable at 6    it does not compile: this error, reported on this line of the file
// rules: V7

// expect: throws overflow at 7       it compiles, prints these lines, then this exception ends it on this line
// | before
// build: test                        optional: the case holds for this kind of build only (test or release)
// rules: T5
```

The ids of both kinds are listed in chapter 12. No compiler exists, so nothing runs these files. `tools/lint_reference.py` keeps the
reference and the corpus consistent with each other; whether an expectation is right is decided
by reading it against the rules, and by the review.

## What every case relies on

### A1 (assumed, §8) `print`

`print(value)` writes the text of the value and a line break to standard output. The corpus needs
one way to show a result, and the record's samples use this word. The naming of the standard
library is open (record, section 14), so the word can change.

No case: every case that expects output shows it.

### A2 (assumed, §8) The text of a value

The text of an integer is its decimal digits, with a leading `-` when negative. The text of a
string is the string. The text of a `bool` is `true` or `false`. The same text is used inside an
interpolated string (L6).

No case: every case that expects output shows it.

### A3 (decided, §8) The text of a `data` value

The text of a `data` value is derived from its type, in the manner of C# records: the name of the
type, then ` { `, then its fields in the order of their declaration, each as `Name = text` and
with `, ` between them, then ` }`. The text of a field is the text of its value. A `data` value
without fields (D8) has no braces: it shows its name alone, as A4 says.

Case: [data/print.kz](../corpus/data/print.kz)
```kurz
data User(int Id, string Name)

u = User(1, "Ann")
print(u)
print("got {u}")
```

### A4 (decided, §8) The text of the other values

The other values have a text that is derived from their type, as a `data` value has (A3). A list
shows its items between `[` and `]`, each as its own text and with `, ` between them; a list
without items shows `[]`. An `enum` value shows its name without the name of its type. A `data`
value without fields (D8) shows the name of its type. Null shows `null`. A `double` shows the
shortest digits that read back as the same number: `0.5`, and `0.30000000000000004` for
`0.1 + 0.2`.

A class instance is the exception. It has a text only when its class declares one (A5). Printing
an instance of a class that declares none, or putting one into an interpolated string (L6), is
the compile error `no-text`. An instance can reach itself through a `weak` reference, so a
derived text would need a rule for where to stop. The case shows one line for each sentence of
the first paragraph: a list, an `enum` value, a `data` value without fields, null and two
`double`s; the texts of A3 and of the class cases have cases of their own.

Case: [values/text.kz](../corpus/values/text.kz)
```kurz
data Empty
enum Plan { Free, Pro }

string? Email(int id) {
    if id == 1 {
        return "a@example.com"
    }
    return null
}

mut xs = List<int>()
xs.Add(1)
xs.Add(2)
xs.Add(3)
print(xs)
print(Plan.Pro)
print(Empty)
print(Email(2))
print(0.5)
print(0.1 + 0.2)
```

Case: [classes/print-without-text.kz](../corpus/classes/print-without-text.kz)
```kurz
class Counter(mut int Count)

c = Counter(0)
print(c)
```

### A5 (decided, §8) How a class declares its text

A class declares its text with a method named `Text` that takes no parameters and returns a
`string`, as `ToString()` does in C#: `pub string Text() => "counter {Count}"`. `print` and an
interpolated string (L6) use its result as the text of the instance. The record holds, as
*(assumed)*, how the compiler keeps that cheap: it changes how the text is built and not what it
is, so no case shows it. A `Text()` of the base class counts, one without `pub` does not (A9); an
interface or a type parameter has a text through an interface that declares it (A10).

Case: [classes/text.kz](../corpus/classes/text.kz)
```kurz
class Counter(mut int Count) {
    pub string Text() => "counter {Count}"
}

c = Counter(3)
print(c)
print("got {c}")
```

### A9 (decided, §8) An inherited or a private `Text()`

`Text()` is inherited, and the class of the instance picks it when the program runs, as
`ToString()` is in C#: `print(admin)` has a text when only the base `User` declares `Text()`, and
a `User` variable that holds an `Admin` shows the text of `Admin` where `Admin` overrides it (K7).
A `Text()` without `pub` is the compile error `not-visible` wherever its text is used through
`print` or an interpolation from outside the class; inside the class `Text()` itself can be
called, as any private member can, and whether a `print` or an interpolation there is such a use is A15, with a
status of its own. The owner chose the inheritance on 2026-10-03, against counting only a
`pub Text()` the class itself declares, which would have made every class of a hierarchy repeat
the method; the cost is that whether a class has a text depends on its base, and that a
base-typed value may print more than its static type says.

Case: [classes/text-inherited.kz](../corpus/classes/text-inherited.kz)
```kurz
class User(string Name) {
    pub virtual string Text() => "user {Name}"
}

class Admin(int Level) : User {
    pub override string Text() => "admin {Name} {Level}"
}

class Guest(int Number) : User

User u = Admin("Bea", 1)
print(User("Ann"))
print(Admin("Ann", 3))
print(Guest("Cid", 7))
print(u)
```

Case: [classes/text-private.kz](../corpus/classes/text-private.kz)
```kurz
class Counter(int Count) {
    string Text() => "counter {Count}"
}

print(Counter(1))
```

### A10 (decided, §8) Printing through an interface or a type parameter

`print(x)` with a value whose static type is an interface (K6) compiles when the interface declares
`string Text()`, and is `no-text` otherwise; with a type parameter (T19) it compiles when the
parameter is limited to an interface that declares it, and is `no-text` otherwise, whatever the call
passes. A type meets such a limit when it has a text: every value with a derived text (A3, A4, A8,
A11 to A14), a class that declares or inherits a `pub Text()` (A5, A9), and a value whose interface
or type parameter has a text by the sentence before, without naming the interface (the owner,
2026-10-04, against an interface a class has to name). The match holds only for an interface whose
only member is `string Text()`, the one the standard library declares; an interface that declares
more is met by naming it, because a type that has a text does not have its other members (the owner,
2026-10-09). The owner chose the rule on 2026-10-03, against a check for each instantiation of a
generic function, which would have reported an error at a call site for a line inside another
function; the cost is that a generic function that prints its argument needs the limit, and that the
standard library would declare one interface for it.

Case: [classes/text-through-interface.kz](../corpus/classes/text-through-interface.kz)
```kurz
interface Shape {
    string Text()
}

class Dot : Shape {
    pub string Text() => "dot"
}

void Show(Shape s) {
    print(s)
}

Show(Dot())
```

Case: [classes/text-through-type-parameter.kz](../corpus/classes/text-through-type-parameter.kz)
```kurz
void Show<T>(T item) {
    print(item)
}

Show(1)
```

### A6 (decided, §8) The text of a map

A map shows its entries between `[` and `]`, each as `key: value` and with `, ` between them, in
the manner of a list: `[Ann: 31, Bea: 28]`. The record marks as *(assumed)* that a map without
entries shows `[]`. In which order the entries show is M10; the case holds one entry.

Case: [values/text-map.kz](../corpus/values/text-map.kz)
```kurz
mut ages = Map<string, int>()
ages["Ann"] = 31
print(ages)
```

### A7 (decided, §8) A value that holds an instance without a text

A3 and A4 make the text of a `data` value and of a list out of the texts of what they hold, and gives
an instance of a class a text only when its class declares one (A5). A list whose items are
instances of a class that declares none has no text either, and neither has a `data` value with
such a field: printing one, or putting it into an interpolated string (L6), is the compile error
`no-text`, as for the instance itself. The type of what is held is known where it is printed.

Case: [classes/print-list-without-text.kz](../corpus/classes/print-list-without-text.kz)
```kurz
class Counter(mut int Count)

mut xs = List<Counter>()
xs.Add(Counter(1))
print(xs)
```

Case: [classes/print-data-without-text.kz](../corpus/classes/print-data-without-text.kz)
```kurz
class Counter(mut int Count)
data Box(Counter Item)

b = Box(Counter(1))
print(b)
```

### A8 (assumed, §8) The text of the remaining values

A `float` shows what a `double` shows (A4), in the sense of A11. A `decimal` shows the digits it
holds. A `char` shows its character. A duration shows its value in the units of L14: `1h 30min`,
split as A12 says. A set of flags (D13) shows its names with ` | ` between them, in the order of
A13. A `double` without a fraction, one that needs an exponent and one that is no number show what
C# prints for them: `1`, `1E+21`, `NaN`; which C# that is, and what the other special values show,
is A14.

Case: [values/text-more.kz](../corpus/values/text-more.kz)
```kurz
flags Access { Read, Write, Run }

print(1.0)
print(0.5f)
print(1.50m)
print(90min)
print(Access.Read | Access.Write)
for c in "ab".Chars {
    print(c)
}
```

### A11 (assumed, §8) The text of a `float`

A `float` shows the shortest digits that read back as the same `float`, not the digits of the
`double` it would widen to: `0.1f` shows `0.1`, as C# prints it, and not `0.10000000149011612`. The
owner accepted this reading by its id on 2026-10-09, in round 13, without its text shown, so it is
assumed and not decided.

Case: [values/text-float.kz](../corpus/values/text-float.kz)
```kurz
print(0.1f)
print(2.5f)
```

### A12 (assumed, §8) The text of a duration

A duration shows its value split into the units of L14 from the largest down, each unit at most once
and a unit whose count is zero left out: `90min` shows `1h 30min`, `3600s` shows `1h`, and a value
below a second shows its milliseconds, `1500ms` as `1s 500ms`. Zero shows `0ms`. A negative duration
shows `-` before the whole: `-1h 30min`. The case of A8 shows the split for `90min`. The owner
accepted this reading by its id on 2026-10-09, in round 13, without its text shown, so it is assumed
and not decided.

Case: [values/text-duration.kz](../corpus/values/text-duration.kz)
```kurz
print(1500ms)
print(0ms)
print(-90min)
print(3600s)
```

### A13 (assumed, §8) The text of a set of flags

The names show in the order of their declaration, whatever the order in which the set was combined,
and the empty set (D17) shows `None`: `Access.Write | Access.Read` shows `Read | Write`. The owner
accepted this reading by its id on 2026-10-09, in round 13, without its text shown, so it is assumed
and not decided.

Case: [values/text-flags.kz](../corpus/values/text-flags.kz)
```kurz
flags Access { Read, Write, Run }

print(Access.Write | Access.Read)
print(Access.None)
```

### A14 (assumed, §8) Which C# a `double` prints like

"What C# prints" is what .NET Core 3.0 and later print with the invariant culture, which is the
shortest digits that read back as the same number (A4), with `.` as the separator: an exponent form
from `1E+15` up and below `1E-05`, `-0` for a negative zero, `Infinity` and `-Infinity` for the
infinities, and `NaN`. Older .NET and other cultures print some of these differently (`0` for a
negative zero, a `∞` sign, a `,`), and none of that is meant. The owner accepted this reading by its
id on 2026-10-09, in round 13, without its text shown, so it is assumed and not decided.

Case: [values/text-double-special.kz](../corpus/values/text-double-special.kz)
```kurz
double z = 0.0
print(1e15)
print(1e14)
print(-0.0)
print(1.0 / z)
```

### A15 (decided, §8) A private `Text()` used by `print` or an interpolation inside its class

`print` of an instance inside the class, and an interpolation of one there (L6), reach `Text()` the
way a use from outside does, through the text of the value and not as a call of the member, so a
`Text()` without `pub` is `not-visible` there too (A9 for the use from outside). The owner's choice
of 2026-10-03 (A9) covered the inheritance and the `pub`, not the place of the use; the owner
decided this reading on 2026-10-09, in round 13, against one under which a private `Text()` has two
meanings. The cases: one for `print` and one for an interpolation.

Case: [classes/text-private-inside.kz](../corpus/classes/text-private-inside.kz)
```kurz
class Counter(int Count) {
    string Text() => "counter {Count}"

    pub void Show() {
        print(Counter(2))
    }
}

Counter(1).Show()
```

Case: [classes/text-private-inside-interpolation.kz](../corpus/classes/text-private-inside-interpolation.kz)
```kurz
class Counter(int Count) {
    string Text() => "counter {Count}"

    pub void Show() {
        print("{Counter(2)}")
    }
}

Counter(1).Show()
```
