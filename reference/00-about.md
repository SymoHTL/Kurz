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
starts with a header and an empty line; the rest is the program. A chapter shows a file's sample once,
under the first `Case:` line that names it; a later rule of the same chapter that the case names
carries the `Case:` line alone.

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

// expect: output
// | 1
// allow: raw                         optional: the grants of a `project.kz` the file takes: raw, native or raw native
// rules: R7
```

A case is a single file with no `project.kz`; its `// allow:` line gives it the grants a
`project.kz` would give its package (R7; §3, the owner, 2026-10-21). The line takes `raw`, `native` or
`raw native`, in that order, and each compiler's runner of the corpus reads it; a program outside the
corpus takes no grant from it *(assumed: as §3 marks it)*.

The ids of both kinds are listed in chapter 12. The compiler's test suite parses each file and checks the errors of its
front end, `syntax`, `semicolon`, `reserved-word`, `braces-required`, `unknown-escape` and `block-indentation`, or the absence of any diagnostic, against the header,
except for `data/flags-match.kz`, whose `syntax` depends on the type of the matched value (D13), whose front end the suite checks for no diagnostic, and which the checker, reading no `flags` type, holds to `not supported` and no error;
it runs every case its front end reads clean through the checker, which must report exactly the errors the header expects, or, for a case whose construct it does not read, `not supported` and no error, as `compiler/README.md` says; and it builds every case that expects a run and that the compiler emits, runs it, and compares what it prints, its exit code and its error line with the header. What the other cases print, and an error that a part the checker does not read raises, are checked only by reading and by the review. `tools/lint_reference.py` keeps the
reference and the corpus consistent with each other; whether an expectation is right is decided
by reading it against the rules, and by the review.

## What every case relies on

### A1 (assumed, §6, §8) `print`

`print(value)` writes the text of the value and a line break to standard output; the line break
is one line feed on every platform (the owner, 2026-10-10, against the platform's own line ending,
under which the output of a program differs by the machine that runs it; the cost is that a tool on
Windows that expects the two-byte line break in a program's output reads bare line feeds). The corpus needs
one way to show a result, and the record's samples use this word. The naming of the standard
library is open (record, section 14), so the word can change. A `print` whose write fails raises the
run-time error `print-failed` (§6, the owner, 2026-10-21), at the file and the line of the `print`, which
the `print` passes, in every build (§6, the owner, 2026-10-23).

No case: every case that expects output shows it, and no case can make a write of standard output
fail, which `print-failed` needs.

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

A value of a generic `data` type (T19) shows the name its declaration gives, without the type
arguments: `Pair<int, string>(1, "one")` shows `Pair { First = 1, Second = one }`, as a C# record
does (§8, the owner, 2026-10-23).

Case: [data/print-generic.kz](../corpus/data/print-generic.kz)
```kurz
data Pair<A, B>(A First, B Second)

print(Pair<int, string>(1, "one"))
```

A field of a union shows the text of its case's value inside the braces (A4).

Case: [data/print-union-field.kz](../corpus/data/print-union-field.kz)
```kurz
data Box(int | string V)

print(Box(5))
print(Box("five"))
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

Case: [values/enum-text.kz](../corpus/values/enum-text.kz)
```kurz
enum Plan { Free = 1, Pro = 2 }
data Box(Plan P)

mut xs = List<Plan>()
xs.Add(Plan.Free)
xs.Add(Plan.Pro)
p = Plan.Free
print(p)
print(xs)
print(Box(Plan.Pro))
print("plan {xs[1]}")
Plan | Invalid none = Plan.From(9)
print(none)
```

Case: [classes/print-without-text.kz](../corpus/classes/print-without-text.kz)
```kurz
class Counter(mut int Count)

c = Counter(0)
print(c)
```

A value of a union (D9) shows the text of the value of its case: an `int | string` that holds 5
shows `5`, and a `data` value with a field of that union `Box { V = 5 }`, as a C# record shows a
field of type `object` (§8, the owner, 2026-10-23). A union with a case that has no text has none
itself (A7).

Case: [data/print-union.kz](../corpus/data/print-union.kz)
```kurz
int | string v = 5
int | string w = "five"
print(v)
print(w)
```

Case: [data/print-union-field.kz](../corpus/data/print-union-field.kz)

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
only member is `string Text()`, the one a standard library would declare; an interface that declares
more is met by naming it, because a type that has a text does not have its other members. *(assumed:
the reference's reading, accepted by its id on 2026-10-09 without its text shown)* The owner chose the rule on 2026-10-03, against a check for each instantiation of a
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

A value of a union (D9) with a case that has no text, an instance of such a class or the success
without a value (O10), has no text either: printing one, or putting it into an interpolated string,
is `no-text` (§8, the owner, 2026-10-24).

Case: [classes/print-union-without-text.kz](../corpus/classes/print-union-without-text.kz)
```kurz
class Counter(mut int Count)

int | Counter v = Counter(1)
print(v)
```

Case: [outcomes/print-void-union.kz](../corpus/outcomes/print-void-union.kz)
```kurz
data NotFound

void Show(void | NotFound v) {
    print(v)
}

Show(NotFound)
```

### A8 (assumed, §8) The text of the remaining values

A `float` shows what a `double` shows (A4), in the sense of A11. A `decimal` shows the digits it
holds, `1.50m` as `1.50`, with a `-` before a zero whose sign is set, `-(0.0m)` as `-0.0`, as a
`double` shows `-0` (the owner, 2026-10-28, "Sign, as Rust", as rust_decimal shows such a zero,
against no sign, as C# shows none; the cost is that an amount can show as `-0.00`; which zeros
have a sign is T30). A `char` shows its character. A duration shows its value split into units, `1h
30min`, as A12
says. A timestamp shows its instant in UTC, as A16 says. A set of flags (D13) shows its names with ` | ` between them, in the order of
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

Case: [values/text-decimal.kz](../corpus/values/text-decimal.kz)
```kurz
decimal Negate(decimal a) => -a

print(1.50m)
print(Negate(0.0m))
print(Negate(Negate(0.0m)))
```

### A11 (assumed, §8) The text of a `float`

A `float` shows the shortest digits that read back as the same `float`, not the digits of the
`double` it would widen to: `0.1f` shows `0.1`, as C# prints it, and not `0.10000000149011612`. The
owner accepted this reading by its id on 2026-10-09, in round 13, without its text shown, so it is
assumed and not decided. A `float` takes the exponent form from `1E+09` up, as .NET Core 3.0 and
later print a `float`: `123456790f` shows `123456790` and `1e9f` shows `1E+09` (the owner,
2026-10-28, "As C# prints a float", against the bounds of a `double`, under which `1e9f` shows
`1000000000`); below `1E-04` it takes it as a `double` does (A14).

Case: [values/text-float.kz](../corpus/values/text-float.kz)
```kurz
print(0.1f)
print(2.5f)
print(123456790f)
print(1e9f)
```

### A12 (assumed, §8) The text of a duration

A duration shows its value split into units from `days` down, those of L14 and the two below a
millisecond named below, each unit at most once
and a unit whose count is zero left out: `90min` shows `1h 30min`, `3600s` shows `1h`, and the remainder
below a second shows its milliseconds, `1500ms` as `1s 500ms`. Zero shows `0ms`. A remainder
below a millisecond shows its microseconds and nanoseconds the same way, `1s 250us` and `1us 500ns`,
when no formatting is chosen (the owner, 2026-10-10, against rounding or cutting to milliseconds); a
formatting that can be chosen is a question the record lists (section 14); `us` and `ns` are units of a
literal as well (L14). A negative duration
shows `-` before the whole: `-1h 30min`. The case of A8 shows the split for `90min`. A size is an integer (L14) and shows its digits, `1.5kb`
as `1536`, which the case of L14 shown below prints as well. The owner
accepted this reading by its id on 2026-10-09, in round 13, without its text shown, so it is assumed
and not decided; that it is the remainder below a second that shows in milliseconds came from the
first review of 2026-10-10, and was not put to the owner by its text. *(assumed: that sentence)*

Case: [values/text-duration-sub-millisecond.kz](../corpus/values/text-duration-sub-millisecond.kz)
```kurz
print(1.00025s)
print(0.0000015s)
// with no formatting chosen, the remainder below a millisecond shows in microseconds and nanoseconds (A12, the owner's choice of 2026-10-10)
```

Case: [source/unit-literal-fraction.kz](../corpus/source/unit-literal-fraction.kz)
```kurz
print(1.5s)
print(0.5h)
print(1.5kb)
```

Case: [source/unit-literal-micro.kz](../corpus/source/unit-literal-micro.kz)
```kurz
print(250us)
print(1500ns)
print(0.5us)
print(1us)
```

Case: [values/text-duration.kz](../corpus/values/text-duration.kz)
```kurz
print(1500ms)
print(0ms)
print(-90min)
print(3600s)
```

Case: [values/text-longduration.kz](../corpus/values/text-longduration.kz)
```kurz
longduration Plus(longduration a, longduration b) => a + b

print(200000days)
print(Plus(200000days, 1ns))
print(-200000days)
```

### A16 (decided, §8) The text of a timestamp

A `timestamp` and a `longtimestamp` show their instant in UTC as RFC 3339 writes it,
`2026-10-28T09:30:00Z`: the fraction of a second in groups of three digits, as many groups as hold it
and none for a whole second, `09:30:01.500Z`, `09:30:01.000250Z`, `09:30:00.000000001Z`, and a year
outside 0000 to 9999, which only a `longtimestamp` reaches, with its sign and at least four digits,
`+10000-01-01T00:00:00Z`, `-0001-12-31T23:59:59Z`, as Rust's `chrono` writes them, the expanded
years of ISO 8601, which RFC 3339 does not have (§8, the owner, 2026-10-28, "RFC 3339 UTC, digits
as needed", then "Groups of three, as chrono" and "Sign + 4 or more digits, as chrono", against
leaving the text open, nine digits always and the count of nanoseconds since 1970, then against
the fewest digits and against six digits of a year as JavaScript writes them). The instant counts
from 1970-01-01 (T24) *(assumed: the calendar, the
Gregorian one before its introduction as well, with no leap second and a year 0 before the year 1,
as chrono and Unix time count, as §8 marks it)*. No construct makes a timestamp of a number but a
read through a pointer (R10, R14), which the cases below take.

Case: [values/text-timestamp.kz](../corpus/values/text-timestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        print(*Ptr<timestamp>(&bits))
    }
}

Show(0)
Show(1793179800000000000)
Show(1793179801500000000)
Show(1793179801000250000)
Show(-1)
```

Case: [values/text-longtimestamp.kz](../corpus/values/text-longtimestamp.kz)
```kurz
void Show(long nanoseconds) {
    long bits = nanoseconds
    raw {
        longtimestamp epoch = *Ptr<timestamp>(&bits)
        print(epoch + 2932897days)
        print(epoch - 719528days)
        print(epoch - 719528days - 1s)
    }
}

Show(0)
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
shortest digits that read back as the same number (A4) where .NET's text reads back (below), with
`.` as the separator: an exponent form
for a nonzero magnitude of `1E+17` or more or below `1E-04`, so that `1E+16` shows `10000000000000000`,
`1E-05` is the first power of ten below that bound it shows and `0.0001` the last without it, `0` for
zero, `-0` for a negative zero, `Infinity` and `-Infinity` for the infinities, and `NaN`. Older .NET
and other cultures print some of these differently (`0` for a negative zero, a `∞` sign, a `,`,
`1E+15` for `1e15`), and none of that is meant. The owner accepted this reading by its
id on 2026-10-09, in round 13, without its text shown, so it is assumed and not decided, but for the
bound `1E+17`, which the owner chose (below); that the
exponent form starts below `1E-04`, at `1E-05` for the powers of ten, came from the first review of
2026-10-10, and was not put to the owner by its text. *(assumed: that sentence)* The bound `1E+17` is
the owner's (2026-10-28, "As .NET prints: 1E+17"): .NET 10 writes `1e15` as `1000000000000000`, as
the source of .NET Core 3.0 has it, where the exponent form starts when the part before the point
would take more places than 17 and than the count of the digits; against keeping `1E+15`, the bound
of .NET Framework's shorter format, which this rule named before. Where .NET writes a text that does
not read back as the value, as .NET 10 writes the power of two `2.9802322387695312E-08` as
`2.980232238769531E-08`, the text is the shortest digits that do, as A4 says, the nearest to the
value among them, as Ryu takes them. *(assumed: A4's digits over such a text of .NET's, which .NET
promises to read back, and the nearest among them)* Of two such texts equally near the
value, the text is the one whose last digit is even, as Ryu and Rust's `ryu` crate take it: 2^-25,
`2.98023223876953125E-08`, shows `2.9802322387695312E-08` and not `2.9802322387695313E-08`.
*(assumed: the tie, which no answer named)*

Case: [values/text-double-special.kz](../corpus/values/text-double-special.kz)
```kurz
double z = 0.0
print(1e16)
print(1e17)
print(0.0001)
print(0.00001)
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
