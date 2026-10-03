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

// expect: throws                     it compiles, prints these lines, then ends with an exception
// | before
// build: test                        optional: the case holds for this kind of build only (test or release)
// rules: T5
```

No compiler exists, so nothing runs these files. `tools/lint_reference.py` keeps the
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
with `, ` between them, then ` }`. The text of a field is the text of its value.

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
derived text would need a rule for where to stop.

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
print("plan {Plan.Free}")
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
is, so no case shows it.

Case: [classes/text.kz](../corpus/classes/text.kz)
```kurz
class Counter(mut int Count) {
    pub string Text() => "counter {Count}"
}

c = Counter(3)
print(c)
print("got {c}")
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

A4 makes the text of a list and of a `data` value out of the texts of what they hold, and gives
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

A `float` shows what a `double` shows (A4). A `decimal` shows the digits it holds. A `char` shows
its character. A duration shows its value in the units of L14: `1h 30min`. A set of flags (D13)
shows its names with ` | ` between them. A `double` without a fraction, one that needs an
exponent and one that is no number show what C# prints for them: `1`, `1E+21`, `NaN`.

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
