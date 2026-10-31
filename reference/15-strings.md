# 15. Strings and lists

The members of a string and of a list take C#'s names with Rust's results, as a map's members do
(§4; §12, the owner, 2026-10-31, "C# names"). A position in a string counts bytes (X2).

### X1 (decided, §12) Searching a string

`s.Contains(x)`, `s.StartsWith(x)` and `s.EndsWith(x)` are each a `bool`; `s.IndexOf(x)` is the
position of the first `x` in `s`, an `int?` that is `null` where `s` holds no `x`, as Rust's `find`
gives an `Option` where C#'s gives -1 (§12, the owner, 2026-10-31, "C# names"). The position counts
bytes (X2) and is at the start of a code point. The comparison is ordinal (T32). `x` is a `string`
or a `char`, and an empty `x` is found at the position 0, so that `s.Contains("")`,
`s.StartsWith("")` and `s.EndsWith("")` are `true`, as in C# and Rust *(assumed, §12: the `char`
form, a default of the question, and the empty `x`, not put to the owner)*.

Case: [strings/search.kz](../corpus/strings/search.kz)
```kurz
s = "hä, wörld"
print(s.Contains("wö"))
print(s.Contains('x'))
print(s.StartsWith("hä"))
print(s.EndsWith('d'))
print(s.IndexOf("wö"))
print(s.IndexOf('ö'))
print(s.IndexOf("x"))
print(s.IndexOf(""))
print("".EndsWith(""))
```

### X2 (decided, §12) A position counts bytes

A position in a string is an index into `s.Bytes` (T15), reached without a walk, as Rust's positions
in a `str` (§12, the owner, 2026-10-31, "Bytes, as Rust"). `s.Substring(start, length)` is a new
string of the `length` bytes from `start`. A `Substring` that reaches outside the string, a `start`
or a `length` below 0 among them, raises `index-out-of-range`, as a list's index does (M9), and one
whose start or end falls inside a code point raises `not-char-boundary`, which names the position,
as Rust's `&s[a..b]` panics (§12, the owner, 2026-10-31, "Two ids"). A negative `start` or `length` is
outside, and so is a `start + length` past the largest `int`; the bounds are read before the code
points, so that a `Substring` both outside the string and inside a code point raises
`index-out-of-range`, and the start's code point before the end's, as Rust's `&s[a..b]` reads a cut
*(assumed, §12, not put to the owner)*. Where the error line names the position is X10.

Case: [strings/substring.kz](../corpus/strings/substring.kz)
```kurz
// h is one byte, ä two, the comma and the space one each, w one, ö two: 11 bytes in all
s = "hä, wörld"
print(s.Substring(5, 3))
print(s.Substring(0, 3))
print(s.Substring(11, 0) == "")
```

Case: [strings/substring-outside.kz](../corpus/strings/substring-outside.kz)
```kurz
s = "abc"
print(s.Substring(1, 2))
print(s.Substring(2, 2))
```

Case: [strings/substring-inside-char.kz](../corpus/strings/substring-inside-char.kz)
```kurz
s = "hä"
print(s.Substring(0, 1))
print(s.Substring(0, 2))
```

Case: [strings/substring-outside-and-inside.kz](../corpus/strings/substring-outside-and-inside.kz)
```kurz
// the start, 2, falls inside `ä`, and the end, 7, past the string's 3 bytes: the bounds are read first
s = "hä"
print(s.Substring(2, 5))
```

### X3 (decided, §12) A change of case

`s.ToLower()` and `s.ToUpper()` change case by Unicode's full mapping, under which the length can
change: `"ß".ToUpper()` is `"SS"`, and a capital sigma at the end of a word lowers to the final form,
as Rust's `to_lowercase` and `to_uppercase`. `s.ToLowerAscii()` and `s.ToUpperAscii()` change `A` to
`Z` alone, as Rust's `to_ascii_lowercase` (§12, the owner, 2026-10-31, "Rust full + ASCII"). Under the
Unicode form the Kelvin sign U+212A lowers to `k`; under the ASCII form it stays. A change of case
takes no culture (§4).

Case: [strings/case.kz](../corpus/strings/case.kz)
```kurz
print("straße".ToUpper())
print("ΣΑΣ".ToLower())
print("\u212A".ToLower() == "k")
print("\u212A".ToLowerAscii() == "k")
print("http/1.1 äb".ToUpperAscii())
print("ÄB".ToLowerAscii())
```

### X4 (decided, §12) Trimming

`s.Trim()`, `s.TrimStart()` and `s.TrimEnd()` take the characters of Unicode's White_Space from both
ends, the start or the end, as Rust's `trim` and C#'s `Trim()`; `s.TrimAscii()`, `s.TrimStartAscii()`
and `s.TrimEndAscii()` take ASCII's whitespace alone, as Rust's `trim_ascii` (§12, the owner,
2026-10-31, "Unicode + TrimAscii"). ASCII's whitespace is the space, the tab, the line feed, the form
feed and the carriage return, as Rust's `is_ascii_whitespace`, and `s.Trim(c)` with a `char` takes that
character from both ends, as C#'s `Trim(char)` *(assumed, §12: the names `TrimStartAscii` and
`TrimEndAscii`, the five characters and the `char` form)*.

Case: [strings/trim.kz](../corpus/strings/trim.kz)
```kurz
s = "\u00A0 a b\t\n"
print(s.Trim() == "a b")
print(s.TrimAscii() == "\u00A0 a b")
print(s.TrimStart() == "a b\t\n")
print(s.TrimEndAscii() == "\u00A0 a b")
print("xxaxx".Trim('x'))
```

### X5 (decided, §12) Splitting and replacing

`s.Split(separator)` gives a `List<string>` of the pieces between the separators, each a new string,
an empty piece kept, as C#'s `Split` and Rust's `split`; `s.Replace(old, new)` replaces each `old`
from the left, none overlapping the one before. The separator, and `old`, is a `string` or a `char`
*(assumed, §12: the list of new strings, the empty piece and the `char` form, defaults of the
question; the order of the replace, not put to the owner)*. `s.Split("")` gives the whole string as
its one piece, and `s.Replace("", x)` gives the string as it is (§12, the owner, 2026-10-31, "No
piece, no change").

Case: [strings/split-replace.kz](../corpus/strings/split-replace.kz)
```kurz
print("a,b,,c".Split(","))
print("a, b".Split(", ").Count)
print("abc".Split("").Count)
print("a-b".Split('-'))
print("aaa".Replace("aa", "b"))
print("abc".Replace("", "x"))
```

### X6 (decided, §12) Joining

`string.Join(separator, items)` takes a list of any item type and writes the text of each item (A4)
with the separator between two items, as C#'s `Join` calls `ToString`; an item type that has no text
is `no-text` (§12, the owner, 2026-10-31, "Any list").

Case: [strings/join.kz](../corpus/strings/join.kz)
```kurz
print(string.Join(", ", ["a", "b", "c"]))
print(string.Join("-", [1, 2, 3]))
print(string.Join(", ", List<string>()) == "")
```

Case: [strings/join-no-text.kz](../corpus/strings/join-no-text.kz)
```kurz
class Counter(mut int Count)

print(string.Join(", ", [Counter(0)]))
```

### X7 (decided, §12) Reading a number

`long.Parse(s)`, and the same call on each number type, gives the number or a `ParseError`:
`long | ParseError` (O1), as Rust's `parse` (§12, the owner, 2026-10-31, "Outcome long | ParseError").
`data ParseError(ParseKind Kind)` with `enum ParseKind { Empty, InvalidDigit, TooLarge, TooSmall }`
holds none of the input, and its text is the words of its kind in lower case, `too large` (§12, the
owner, 2026-10-31, "Without input"). `double.Parse` gives the kinds `Empty` and `InvalidDigit` alone,
and `double.Parse("1e999")` is infinity (§12, the owner, 2026-10-31, "Without input"); `decimal.Parse`
gives `TooLarge` and `TooSmall` as well, past the largest and below the smallest `decimal` (§12, the
owner, 2026-10-31, "TooLarge / TooSmall"). A number is read in C#'s invariant forms: whitespace
around it and a leading sign, and for `double`, `float` and `decimal` a point and `,` as a separator
of thousands, and for `double` and `float` an exponent and the words `Infinity` and `NaN` (§12, the
owner, 2026-10-31, "C#'s invariant forms"), so that `"1,5"` reads as 15; an integer reads no
separator and no point (§12, the owner, 2026-10-31, "C#'s Integer style"); a `decimal` reads no sign
after its digits and no exponent (§12, the owner, 2026-10-31, "Without trailing sign"). Whitespace is
the tab, the line feed, the vertical tab, the form feed, the carriage return and the space, and a
string of whitespace alone is `Empty`; `,` stands among the digits before the point, its groups not
checked, so that `"1,2,3"` reads as 123; `Infinity`, `-Infinity` and `NaN` are read in any case; a
number may start or end with its point; a sign before the digits of an unsigned type reads as C#'s
`Parse` reads it, `"-0"` as 0 and `"-1"` as `TooSmall`; `float.Parse` reads as `double.Parse` does,
a number past the largest `float` being infinity; and `ParseError` and `ParseKind` are types every
file sees, so that a file's own declaration of one is `redeclared` (V9), as a file's own
`InvalidUtf8` is (T15) *(assumed, §12: the readings said back with the answers, not put to the owner
as questions, and the two names, a default of the question)*. The reading takes no culture (§4).

Case: [strings/parse.kz](../corpus/strings/parse.kz)
```kurz
void Show(long | ParseError result) {
    match result {
        long n => print(n)
        ParseError e => print(e)
    }
}

Show(long.Parse("42"))
Show(long.Parse("+5"))
Show(long.Parse(""))
Show(long.Parse("4x"))
Show(long.Parse("9223372036854775808"))
Show(long.Parse("-9223372036854775809"))
match byte.Parse("-1") {
    byte b => print(b)
    ParseError e => print(e.Kind)
}
match double.Parse("1.5") {
    double d => print(d)
    ParseError e => print(e)
}
match double.Parse("1e999") {
    double d => print(d)
    ParseError e => print(e)
}
```

Case: [strings/parse-forms.kz](../corpus/strings/parse-forms.kz)
```kurz
void Show(long | ParseError result) {
    match result {
        long n => print(n)
        ParseError e => print(e)
    }
}

void ShowDouble(double | ParseError result) {
    match result {
        double d => print(d)
        ParseError e => print(e)
    }
}

void ShowDecimal(decimal | ParseError result) {
    match result {
        decimal m => print(m)
        ParseError e => print(e)
    }
}

// whitespace around the number, and no separator in an integer
Show(long.Parse(" 7\t"))
Show(long.Parse("1,000"))
Show(long.Parse("  "))
match byte.Parse("-0") {
    byte b => print(b)
    ParseError e => print(e)
}
// a separator of thousands, the words of an infinity in any case, and a point at the start
ShowDouble(double.Parse(" 1,000.5 "))
ShowDouble(double.Parse("-infinity"))
ShowDouble(double.Parse(".5"))
// a `decimal` keeps its scale, and reads no sign after its digits and no exponent
ShowDecimal(decimal.Parse("+2.50"))
ShowDecimal(decimal.Parse("5-"))
ShowDecimal(decimal.Parse("1e5"))
ShowDecimal(decimal.Parse("79228162514264337593543950336"))
ShowDecimal(decimal.Parse("-79228162514264337593543950336"))
```

Case: [strings/parse-error-redeclared.kz](../corpus/strings/parse-error-redeclared.kz)
```kurz
data ParseError(string Input)

print(1)
```

### X8 (decided, §12) Sorting a list

`xs.Sort()` sorts a list in place and stable, for the item types `<` orders; `xs.SortBy(x => key)`
sorts it by a key of such a type; `xs.SortUnstable()` sorts it without the buffer of the stable sort,
as Rust's `sort`, `sort_by_key` and `sort_unstable`. Each changes the list and takes it through a
`mut` variable, as `Add` does (M5) (§12, the owner, 2026-10-31, "Same + SortUnstable"). A list of
`double` or `float` values sorts as C#'s `double.CompareTo` orders them: NaN first, `-0` equal to `0`,
so that the stable sort keeps the order of `-0` and `0` (§12, the owner, 2026-10-31, "C# CompareTo").
A buffer the system refuses raises `allocation-failed`, as a list's growth does (M9) *(assumed, §12)*.
`SortBy` calls its key once for each item and keeps the keys beside the list while it sorts, as C#'s
`OrderBy` and Rust's `sort_by_cached_key` (§12, the owner, 2026-10-31, "Once per item"), in the order
of the list *(assumed, §12)*. A sort on an item type or a key type `<` does not order is
`type-mismatch`, as `<` on two values of that type is (T18) *(assumed, §12)*.

Case: [strings/sort.kz](../corpus/strings/sort.kz)
```kurz
data Person(string Name, int Age)

mut xs = [3, 1, 2]
xs.Sort()
print(xs)
// strings sort ordinal (T32): an upper-case letter before every lower-case one
mut words = ["pear", "Fig", "apple", "fig"]
words.Sort()
print(words)
mut people = [Person("Ann", 31), Person("Bo", 28), Person("Cy", 31), Person("Di", 28)]
people.SortBy(p => p.Age)
for p in people {
    print(p.Name)
}
mut ys = [3, 1, 2]
ys.SortUnstable()
print(ys)
// NaN first, and 0 before -0 as they stood, since the two are equal
double z = 0.0
mut ds = [2.5, z / z, 0.0, 1.0, -0.0]
ds.Sort()
print(ds)
```

Case: [strings/sort-without-mut.kz](../corpus/strings/sort-without-mut.kz)
```kurz
xs = [3, 1, 2]
xs.Sort()
print(xs)
```

Case: [strings/sort-by-key-once.kz](../corpus/strings/sort-by-key-once.kz)
```kurz
int Key(int x) {
    print("key {x}")
    return x
}

// the key is made once for each item, in the order of the list, and not at each comparison
mut xs = [3, 1, 2]
xs.SortBy(x => Key(x))
print(xs)
```

Case: [strings/sort-of-bools.kz](../corpus/strings/sort-of-bools.kz)
```kurz
mut xs = [true, false]
xs.Sort()
print(xs)
```

### X9 (decided, §12) Searching and growing a list

`xs.IndexOf(item)` is the position of the first item equal to `item` (D3), an `int?` as in X1;
`xs.IndexOf(run)`, taking a list, is the position of the first run of items equal to the items of
`run`, as C#'s `MemoryExtensions.IndexOf` takes a span, an empty `run` found at the position 0; and
`xs.AddRange(other)` appends the items of `other`, as C#'s `List.AddRange` (§12, the owner, 2026-10-31,
"C# overloads"). `xs.Reverse()` reverses a list in place, as C#'s `List.Reverse` and Rust's `reverse`.
`AddRange` and `Reverse` change the list and take it through a `mut` variable (M5) *(assumed, §12:
`Reverse`, a default of the question, and the empty `run`, not put to the owner)*.

Case: [strings/list-search.kz](../corpus/strings/list-search.kz)
```kurz
// a request's head ends at the first empty line: 14 bytes of the request line, its line break, 7 of the header
b = "GET / HTTP/1.1\r\nHost: a\r\n\r\nbody".Bytes
print(b.IndexOf("\r\n\r\n".Bytes))
// `IndexOf` takes an item or a run, so a literal keeps its own type, `int`, and `byte(13)` is the item (F14)
print(b.IndexOf(byte(13)))
print(b.IndexOf("xyz".Bytes))
xs = [1, 2, 3, 2, 3]
print(xs.IndexOf([2, 3]))
print(xs.IndexOf(List<int>()))
mut ys = [1]
ys.AddRange([2, 3])
print(ys)
ys.Reverse()
print(ys)
```

Case: [strings/add-range-without-mut.kz](../corpus/strings/add-range-without-mut.kz)
```kurz
xs = [1]
xs.AddRange([2])
print(xs)
```

### X10 (decided, §12) Where the error line of `not-char-boundary` names the position

The position of the cut stands on the line below the error line, where the text of a `throw` stands
(O6): `error: not-char-boundary at a.kz:5`, then `byte 2 is inside a code point` (§12, the owner,
2026-10-31, "Below, as throw text"). The error line ends with a colon, as the line of a `throw` does
*(assumed, §12)*.

No case: a case pins the id and the line of the error (E3), and no case reads the lines below it.
