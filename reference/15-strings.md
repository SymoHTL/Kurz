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

A call that fits neither form, a `string` or a `char`, is `argument-mismatch`, with a line for each form (F13).

Case: [strings/search-of-a-number.kz](../corpus/strings/search-of-a-number.kz)
```kurz
s = "ab"
print(s.Contains(5))
```

### X2 (decided, §12) A position counts bytes

A position in a string is an index into `s.Bytes` (T15), reached without a walk, as Rust's positions
in a `str` (§12, the owner, 2026-10-31, "Bytes, as Rust"). `s.Substring(startIndex, length)` is a new
string of the `length` bytes from `startIndex`. A `Substring` that reaches outside the string, a
`startIndex` or a `length` below 0 among them, raises `index-out-of-range`, as a list's index does (M9), and one
whose start or end falls inside a code point raises `not-char-boundary`, which names the position,
as Rust's `&s[a..b]` panics (§12, the owner, 2026-10-31, "Two ids"). A negative `startIndex` or `length`
is outside, and so is a `startIndex + length` past the largest `int`; the bounds are read before the code
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
as Rust's `to_lowercase` and `to_uppercase`. `s.ToLowerAscii()` and `s.ToUpperAscii()` change the
letters `A` to `Z` and `a` to `z` alone, as Rust's `to_ascii_lowercase` (§12, the owner, 2026-10-31, "Rust full + ASCII"). Under the
Unicode form the Kelvin sign U+212A lowers to `k`; under the ASCII form it stays. A change of case
takes no culture (§4). The mapping is that of the Unicode Character Database of the version Rust's
stable release pins, 17.0.0, in tables generated from the database's files (§12, the owner,
2026-10-31, "Rust's pin" and "Generated, as Rust"). A capital sigma lowers to `ς` where a cased
character stands before it and none after it, past the case-ignorable characters on either side, as
Unicode's Final_Sigma and Rust's `to_lowercase` read it, so that `"ὈΔΥΣΣΕΎΣ".ToLower()` is
`"ὀδυσσεύς"`; the mappings that hold for one language are left out; and a change of case called with an
argument, `s.ToLower(1)` among them, is `argument-mismatch` *(assumed, §12: the defaults of the build's product pass, not put to the owner)*.

Case: [strings/case.kz](../corpus/strings/case.kz)
```kurz
print("straße".ToUpper())
print("ΣΑΣ".ToLower())
print("ὈΔΥΣΣΕΎΣ".ToLower())
print("\u212A".ToLower() == "k")
print("\u212A".ToLowerAscii() == "k")
print("http/1.1 äb".ToUpperAscii())
print("ÄB".ToLowerAscii())
```

Case: [strings/case-of-a-number.kz](../corpus/strings/case-of-a-number.kz)
```kurz
s = "Ab"
print(s.ToLower(1))
```

### X4 (decided, §12) Trimming

`s.Trim()`, `s.TrimStart()` and `s.TrimEnd()` take the characters of Unicode's White_Space from both
ends, the start or the end, as Rust's `trim` and C#'s `Trim()`; `s.TrimAscii()`, `s.TrimStartAscii()`
and `s.TrimEndAscii()` take ASCII's whitespace alone, as Rust's `trim_ascii` (§12, the owner,
2026-10-31, "Unicode + TrimAscii"). ASCII's whitespace is the space, the tab, the line feed, the form
feed and the carriage return, as Rust's `is_ascii_whitespace`, and `s.Trim(c)` with a `char` takes that
character from both ends, as C#'s `Trim(char)` *(assumed, §12: the names `TrimStartAscii` and
`TrimEndAscii`, the five characters and the `char` form)*. `s.TrimStart(c)` and `s.TrimEnd(c)` take
the `char` from the start or the end, as C#'s `TrimStart(char)` and `TrimEnd(char)` (§12, the owner,
2026-10-31, "TrimStart(c), TrimEnd(c)"). A trim of a `char` takes every repeat of it at its end, as C#'s
does, so that `"xxaxx".TrimStart('x')` is `axx` and a string of that `char` alone gives `""`
*(assumed, §12)*. A block of a trim the system refuses raises `allocation-failed` by T15 and M9, as
every block of a string does. The forms, written as signatures and not as code a case runs,
with C#'s names for their parameters (§12, the owner, 2026-10-31, "C#'s names"):

```text
string Trim()
string TrimStart()
string TrimEnd()
string TrimAscii()
string TrimStartAscii()
string TrimEndAscii()
string Trim(char trimChar)
string TrimStart(char trimChar)
string TrimEnd(char trimChar)
```

Case: [strings/trim.kz](../corpus/strings/trim.kz)
```kurz
s = "\u00A0 a b\t\n"
print(s.Trim() == "a b")
print(s.TrimAscii() == "\u00A0 a b")
print(s.TrimStart() == "a b\t\n")
print(s.TrimEndAscii() == "\u00A0 a b")
print("xxaxx".Trim('x'))
print("xxaxx".TrimStart('x'))
print("xxaxx".TrimEnd('x'))
print("xxx".Trim('x') == "")
```

A call of `Trim`, `TrimStart` or `TrimEnd` that fits neither form, with no argument or a `char`, `s.Trim(5)` among them, is `argument-mismatch`, with a line for each form (F13).

Case: [strings/trim-of-a-number.kz](../corpus/strings/trim-of-a-number.kz)
```kurz
s = " a "
print(s.Trim(5))
```

### X5 (decided, §12) Splitting and replacing

`s.Split(separator)` gives a `List<string>` of the pieces between the separators, each a new string,
an empty piece kept, as C#'s `Split` and Rust's `split`; `s.Replace(oldValue, newValue)` replaces each
`oldValue` from the left, none overlapping the one before. The separator, and the value replaced, is a
`string` or a `char` *(assumed, §12: the list of new strings, the empty piece and the `char` form,
defaults of the question; the order of the replace, not put to the owner)*. `s.Split("")` gives the
whole string as its one piece, and `s.Replace("", x)` gives the string as it is (§12, the owner,
2026-10-31, "No piece, no change"). A `char` is replaced by a `char` too, `s.Replace('-', '+')`, as
C#'s `Replace(char, char)` (§12, the owner, 2026-10-31, "Replace(char, char)"), so that `Replace` has
three forms: a `string` by a `string`, a `char` by a `string` and a `char` by a `char`; a `string` by a
`char` is none of them, since the record names no such form. A block of `Split` or `Replace` the
system refuses, and a result of more bytes than the largest `int`, raise `allocation-failed` by T15 and
M9, as every block of a string does. The forms, as signatures (X4), with C#'s names for their
parameters (§12, the owner, 2026-10-31, "C#'s names"):

```text
List<string> Split(string separator)
List<string> Split(char separator)
string Replace(string oldValue, string newValue)
string Replace(char oldChar, string newValue)
string Replace(char oldChar, char newChar)
```

Case: [strings/split-replace.kz](../corpus/strings/split-replace.kz)
```kurz
print("a,b,,c".Split(","))
print("a, b".Split(", ").Count)
print("abc".Split("").Count)
print("a-b".Split('-'))
print("aaa".Replace("aa", "b"))
print("abc".Replace("", "x"))
print("a-b-c".Replace('-', '+'))
```

A call of `Split` that fits neither form, a `string` or a `char`, or of `Replace` that fits none of its three, is `argument-mismatch`, with a line for each form (F13).

Case: [strings/split-of-a-number.kz](../corpus/strings/split-of-a-number.kz)
```kurz
s = "a,b"
print(s.Split(5))
```

Case: [strings/replace-string-with-char.kz](../corpus/strings/replace-string-with-char.kz)
```kurz
// `Replace` takes a `string` by a `string`, a `char` by a `string` or a `char` by a `char`, and no `string` by a `char`
s = "a-b"
print(s.Replace("-", '+'))
```

### X6 (decided, §12) Joining

`string.Join(separator, values)` takes a list of any item type and writes the text of each item (A4)
with the separator between two items, as C#'s `Join` calls `ToString`; an item type that has no text
is `no-text` (§12, the owner, 2026-10-31, "Any list"). The separator is a `string` or a `char`, as
C#'s `Join(char, ...)` (§12, the owner, 2026-10-31, "Join(char, items)"). A call that fits neither
form, a separator of another type or values that are no list, is `argument-mismatch`, with a line for
each form (F13). An empty list literal, `string.Join(",", [])`, is a list from which no item type is
read: it gives `T` nothing, and no list type stands where it does, so it is `cannot-infer` by M11, as
`x = []` is. A block of `Join` the system refuses, and a result of more bytes than the largest `int`,
raise `allocation-failed` by T15 and M9, as every block of a string does. The forms, as signatures
(X4), with C#'s names for their parameters (§12, the owner, 2026-10-31, "C#'s names"):

```text
string Join<T>(string separator, List<T> values)
string Join<T>(char separator, List<T> values)
```

Case: [strings/join.kz](../corpus/strings/join.kz)
```kurz
print(string.Join(", ", ["a", "b", "c"]))
print(string.Join("-", [1, 2, 3]))
print(string.Join(", ", List<string>()) == "")
print(string.Join('/', ["a", "b"]))
```

Case: [strings/join-no-text.kz](../corpus/strings/join-no-text.kz)
```kurz
// `Counter` declares no `Text()`, so an instance has no text (A5), and a list of them has none (A7)
class Counter(mut int Count)

print(string.Join(", ", [Counter(0)]))
```

Case: [strings/join-no-text-file.kz](../corpus/strings/join-no-text-file.kz)
```kurz
// a `File` has no text (S5), so a list of them has none for `Join` to write
void Show(List<File> files) {
    print(string.Join(", ", files))
}
```

Case: [strings/join-of-a-number.kz](../corpus/strings/join-of-a-number.kz)
```kurz
// `5` is no list, so neither form, a `string` or a `char` separator, reads an item type from it
print(string.Join(", ", 5))
```

Case: [strings/join-separator-of-a-number.kz](../corpus/strings/join-separator-of-a-number.kz)
```kurz
// the separator is a `string` or a `char`, so `5` fits neither form
print(string.Join(5, ["a"]))
```

Case: [strings/join-empty-literal.kz](../corpus/strings/join-empty-literal.kz)
```kurz
// `[]` gives `T` nothing, and no list type stands where it does
print(string.Join(",", []))
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
after its digits and no exponent (§12, the owner, 2026-10-31, "Without trailing sign"). A string that
holds a byte the form does not take is `InvalidDigit` though its digits run past the range, the whole
string read before the range, as C#'s `Parse` reads it, so that `long.Parse("99999999999999999999x")`
is `InvalidDigit` (§12, the owner, 2026-10-31, "InvalidDigit, as C#"). Whitespace is the tab, the line
feed, the vertical tab, the form feed, the carriage return and the space, and a string of whitespace
alone is `Empty`, and a sign with no digits, `"+"` or `"-"`, is `InvalidDigit`; whitespace stands
before the sign and after the digits alone, so that `"- 5"` is `InvalidDigit`, as C#'s Integer style
reads it under the invariant culture;
`,` stands among the digits before the point, its
groups not checked, so that `"1,2,3"` reads as 123; `Infinity`, `-Infinity` and `NaN`, the words of
`double` and `float`, are read in any case; a number may start or end with its point; a sign before
the digits of an unsigned type reads as C#'s `Parse` reads it, `"-0"` as 0 and `"-1"` as `TooSmall`; `float.Parse` reads as `double.Parse`
does, a number at or past the halfway point above the largest finite `float` being infinity
(below); and `ParseError` and `ParseKind` are types
every file sees, so that a file's own declaration of one is `redeclared` (V9), as a file's own
`InvalidUtf8` is (T15) *(assumed, §12: the readings said back with the answers and the defaults of the
build's product pass, not put to the owner as questions, and the two names, a default of the question)*.
A `decimal` zero read with a sign keeps it, so that `print(decimal.Parse("-0.00"))` shows `-0.00`, as
C#'s `decimal.Parse` keeps the sign in the value's bits (§12, the owner, 2026-10-31, "-0.00, keep the
sign"). A `double` is the one nearest the number its digits write, ties to the even one, and a `float`
is rounded once, from the digits, as Rust's `parse` and C#'s `Parse` since .NET Core 3.0 read them,
by Rust's mechanism: Clinger's fast path where the digits and the power of ten are exact in the type,
that is, where the digits are an integer of at most 2^53 and the power is from 10^-22 to 10^22 for a
`double`, and the digits are at most 2^24 and the power from 10^-10 to 10^10 for a `float`, as Rust's limits are, with a power up to 10^37 or 10^17
first moved into the digits where they stay within that limit, Eisel-Lemire's with a
table of the 128-bit powers of five from 5^-342 to 5^308, and for the strings the two cannot settle a
slow path over at most 768 of the digits on the stack, so that a parse asks the allocator for nothing;
the digits count from the first that is not 0, and those past the 768th count as a remainder that is
not 0 where one of them is not, so that a number at a halfway point with such a digit rounds up
*(assumed, §12: the nearest value, ties to the even one, a `float` rounded once, from the digits, and
Rust's mechanism, with its limits, its table and its slow path, defaults of the build's product pass)*.
The forms are .NET's: a `,` only after the first digit, as many as stand, so that `",5"` is
`InvalidDigit` and `"1,,2"` reads as 12; a sign before `Infinity` and `NaN`, and no `Inf`; a point
with a digit beside it and an exponent with a digit after its sign, so that `"."` and `"1e"` are
`InvalidDigit`; no hexadecimal form and no `_` between digits. A `decimal` reads none of the words,
which are named for `double` and `float` alone, so that `decimal.Parse("NaN")` is `InvalidDigit`, as its
exponent and a sign after its digits are. A number at or past the halfway point above the largest
finite `double` or `float` is an infinity and one at or below half the smallest positive one a zero,
each with the sign, so that `"-1e-999"` reads as `-0`, and `"NaN"` and `"-NaN"` read as the same NaN. A `decimal` keeps the digits that fit in
its 96 bits at a scale of at most 28 and rounds half to even at the cut, every digit after it counted; a
carry of the rounding past 96 bits makes the number one digit shorter, 2^96 / 10 rounded up, and lowers
the scale by one, as .NET's
`NumberToDecimal` does, so that `"7922816251426433759354395033.55"` reads as
7922816251426433759354395034, and at a scale of 0 puts the number past the range. The number is held to
its range after the rounding, so that `"79228162514264337593543950335.5"` is `TooLarge` and
`"-79228162514264337593543950335.5"` is `TooSmall`; a zero keeps its
scale, at most 28 *(assumed, §12: the defaults of the build's product pass, not put to the owner)*. A NUL
after the number is `InvalidDigit` for every number type, as Rust's `parse` reads it, where .NET's
`Parse` skips NUL characters after the digits, so that `"5\0"` is `InvalidDigit` (§12, the owner,
2026-10-31, "InvalidDigit, as Rust").
The reading takes no culture (§4).

Case: [strings/parse.kz](../corpus/strings/parse.kz)
```kurz
void Show(string s) {
    match long.Parse(s) {
        long n => print(n)
        ParseError e => print(e)
    }
}

Show("42")
Show("+5")
Show("")
Show("4x")
Show("9223372036854775808")
Show("-9223372036854775809")
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
void Show(string s) {
    match long.Parse(s) {
        long n => print(n)
        ParseError e => print(e)
    }
}

void ShowDouble(string s) {
    match double.Parse(s) {
        double d => print(d)
        ParseError e => print(e)
    }
}

void ShowDecimal(string s) {
    match decimal.Parse(s) {
        decimal m => print(m)
        ParseError e => print(e)
    }
}

// whitespace around the number, and no separator in an integer
Show(" 7\t")
Show("1,000")
Show("  ")
match byte.Parse("-0") {
    byte b => print(b)
    ParseError e => print(e)
}
// a separator of thousands, the words of an infinity in any case, and a point at the start
ShowDouble(" 1,000.5 ")
ShowDouble("-infinity")
ShowDouble(".5")
// a `decimal` keeps its scale, and reads no sign after its digits, no exponent and no word
ShowDecimal("+2.50")
ShowDecimal("5-")
ShowDecimal("1e5")
ShowDecimal("NaN")
ShowDecimal("79228162514264337593543950336")
ShowDecimal("-79228162514264337593543950336")
```

Case: [strings/parse-integers.kz](../corpus/strings/parse-integers.kz)
```kurz
// each integer type reads its own range: a number past it is `too large`, below it `too small`
match sbyte.Parse("-128") {
    sbyte n => print(n)
    ParseError e => print(e)
}
match sbyte.Parse("-129") {
    sbyte n => print(n)
    ParseError e => print(e)
}
match byte.Parse("-0") {
    byte n => print(n)
    ParseError e => print(e)
}
match byte.Parse("-1") {
    byte n => print(n)
    ParseError e => print(e)
}
match ushort.Parse("65535") {
    ushort n => print(n)
    ParseError e => print(e)
}
match int.Parse("2147483648") {
    int n => print(n)
    ParseError e => print(e)
}
match long.Parse("-9223372036854775808") {
    long n => print(n)
    ParseError e => print(e)
}
match ulong.Parse("18446744073709551615") {
    ulong n => print(n)
    ParseError e => print(e)
}
match ulong.Parse("18446744073709551616") {
    ulong n => print(n)
    ParseError e => print(e)
}
// a byte the form does not take is read before the range, whitespace alone is `empty`, and a sign with no digits is
// `invalid digit`
match long.Parse("99999999999999999999x") {
    long n => print(n)
    ParseError e => print(e)
}
match int.Parse("\t\r\n") {
    int n => print(n)
    ParseError e => print(e)
}
match long.Parse("-") {
    long n => print(n)
    ParseError e => print(e)
}
match long.Parse("+") {
    long n => print(n)
    ParseError e => print(e)
}
// a NUL is no whitespace, where .NET's `Parse` takes NUL characters after the digits
match int.Parse("5\0") {
    int n => print(n)
    ParseError e => print(e)
}
// whitespace stands before the sign and after the digits alone
match int.Parse("- 5") {
    int n => print(n)
    ParseError e => print(e)
}
```

Case: [strings/parse-floating.kz](../corpus/strings/parse-floating.kz)
```kurz
void ShowDouble(string s) {
    match double.Parse(s) {
        double d => print(d)
        ParseError e => print(e)
    }
}

void ShowFloat(string s) {
    match float.Parse(s) {
        float f => print(f)
        ParseError e => print(e)
    }
}

// a `,` after the first digit, as many as stand, and none before it
ShowDouble("1,")
ShowDouble("1,,2")
ShowDouble(",5")
// a point with a digit beside it, and an exponent with a digit after its sign
ShowDouble("1.e5")
ShowDouble(".")
ShowDouble("1e")
// a sign before the words, and no `Inf`
ShowDouble("-NaN")
ShowDouble("Inf")
// a NUL after the number, as Rust reads it
ShowDouble("1.5\0")
// below the smallest `double` a zero with the sign, and the nearest `double`, ties to the even one
ShowDouble("-1e-999")
ShowDouble("2.4703282292062327e-324")
ShowDouble("2.4703282292062328e-324")
ShowDouble("9007199254740993")
ShowDouble("-1e999")
// a `float` is rounded once, from the digits: the last number stands just above a halfway point of two `float` values,
// and its nearest `double` is that point
ShowFloat("16777217")
ShowFloat("3.4028236e38")
ShowFloat("1.00000005960464477539062501")
// the halfway point 1 + 2^-53 ties to the even `double`, and a digit that is not 0 past the 768th rounds it up
mut digits = "1.00000000000000011102230246251565404236316680908203125"
ShowDouble(digits)
for i in 0..<746 {
    digits = "{digits}0"
}

ShowDouble("{digits}1")
```

Case: [strings/parse-decimal.kz](../corpus/strings/parse-decimal.kz)
```kurz
void ShowDecimal(string s) {
    match decimal.Parse(s) {
        decimal m => print(m)
        ParseError e => print(e)
    }
}

// a zero read with a sign keeps it
ShowDecimal("-0.00")
// past 28 places the number rounds half to even, every digit after the cut counted
ShowDecimal("0.00000000000000000000000000015")
ShowDecimal("0.00000000000000000000000000005")
ShowDecimal("1.0000000000000000000000000000500001")
// past 29 digits the digits that fit, rounded
ShowDecimal("7922816251426433759354395033.55")
// the range is held after the rounding
ShowDecimal("79228162514264337593543950335.4")
ShowDecimal("79228162514264337593543950335.5")
ShowDecimal("-79228162514264337593543950335.5")
ShowDecimal("1,000.5")
```

Case: [strings/parse-error-redeclared.kz](../corpus/strings/parse-error-redeclared.kz)
```kurz
data ParseError(string Input)

print(1)
```

### X8 (decided, §12) Sorting a list

`xs.Sort()` sorts a list in place and stable, for the item types `<` orders and their nullable forms
(below); `xs.SortBy(x => key)`
sorts it by a key of such a type; `xs.SortUnstable()` sorts it without the buffer of the stable sort,
as Rust's `sort`, `sort_by_key` and `sort_unstable`. Each changes the list and takes it through a
`mut` variable, as `Add` does (M5) (§12, the owner, 2026-10-31, "Same + SortUnstable"). A list of
`double` or `float` values sorts as C#'s `double.CompareTo` orders them: NaN first, `-0` equal to `0`,
so that the stable sort keeps the order of `-0` and `0` (§12, the owner, 2026-10-31, "C# CompareTo").
A buffer the system refuses, the keys `SortBy` keeps among them, raises `allocation-failed`, as a
list's growth does (M9) *(assumed, §12)*.
`SortBy` calls its key once for each item and keeps the keys beside the list while it sorts, as C#'s
`OrderBy` and Rust's `sort_by_cached_key` (§12, the owner, 2026-10-31, "Once per item"), in the order
of the list, one call for a list of one item and none for an empty one *(assumed, §12)*. The key is the
argument `keySelector`, the name C#'s `OrderBy` gives it, a lambda or a function value of one item
*(assumed, §12)*. A `SortBy` with no key, with two, with a key of another name or with a lambda of
another count of parameters than one is `argument-mismatch`, as a call that fits no form is (D11), and
every other key that is no function of one item, `xs.SortBy(1)`, a function value of another parameter
type or count among them, or a function that gives nothing, is `type-mismatch`, as an argument of a type
its parameter does not take is (F11) (§12, the owner, 2026-10-31, "As the checker does"). A sort of the
values of an enum that numbers none, as items or as keys, is `enum-number`, as `<` on two of them is
(D20) (§12, the owner, 2026-10-31, "enum-number"), and so is a sort of the values of its nullable form,
as items or as keys *(assumed, §12)*; a sort on any other item type or key type `<`
does not order, a nullable form of one it orders aside, is `type-mismatch`, as `<` on two values of that type is
(T18) *(assumed, §12)*. A list of
nullable items, and a nullable key, sort with `null` before every value, as C#'s
`Comparer<T>.Default` and Rust's ordering of `Option`, though `<` takes no nullable operand (§12, the
owner, 2026-10-31, "Null first, as C#/Rust").
A key that changes the list it sorts, through an instance whose field holds the list (K2), leaves its
change standing, and the sort of the list as it was read is dropped, as C#'s `List.Sort` sorts the list
it read while the field holds another (§12, the owner, 2026-11-01, "Change stands, as C#", #39, comment
6562795140).

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
// a list of one item makes its one key too, and an empty one none
mut one = [7]
one.SortBy(x => Key(x))
print(one)
mut empty = List<int>()
empty.SortBy(x => Key(x))
```

Case: [strings/sortby-key-changes-list.kz](../corpus/strings/sortby-key-changes-list.kz)
```kurz
class Bag(mut List<int> Items) {
    pub int Reset(int x) {
        Items = [0]
        return x
    }

    pub int Push(int x) {
        Items.Add(x * 10)
        return x
    }
}

// a key that replaces the list: the change stands, and the sort of the list it read is dropped
b = Bag([3, 1, 2])
b.Items.SortBy(x => b.Reset(x))
print(b.Items)
// a key that adds to the list: the adds stand, and the list is not sorted
c = Bag([3, 1, 2])
c.Items.SortBy(x => c.Push(x))
print(c.Items)
// a key that changes nothing sorts
d = Bag([3, 1, 2])
d.Items.SortBy(x => x)
print(d.Items)
```

Case: [strings/sort-of-bools.kz](../corpus/strings/sort-of-bools.kz)
```kurz
mut xs = [true, false]
xs.Sort()
print(xs)
```

Case: [strings/sort-of-enums-without-numbers.kz](../corpus/strings/sort-of-enums-without-numbers.kz)
```kurz
enum Plan { Free, Pro }
mut plans = [Plan.Pro, Plan.Free]
plans.Sort()
print(plans)
```

Case: [strings/sort-of-nullables.kz](../corpus/strings/sort-of-nullables.kz)
```kurz
data Person(string Name, int? Age)

// `null` before every value, as C# and Rust order it
mut List<int?> xs = [3, null, 1, null]
xs.Sort()
print(xs)
mut people = [Person("Ann", 31), Person("Bo", 40), Person("Cy", null)]
people.SortBy(p => p.Age)
for p in people {
    print(p.Name)
}
```

Case: [strings/sort-of-ordered-items.kz](../corpus/strings/sort-of-ordered-items.kz)
```kurz
enum Level { High = 5, Low = -1, Mid = 0 }
data Person(string Name, string? Nick)

// an enum that numbers its values sorts by its numbers, not by its places (D18)
mut levels = [Level.High, Level.Low, Level.Mid]
levels.Sort()
print(levels)
mut letters = ['c', 'A', 'b']
letters.SortUnstable()
print(letters)
mut waits = [1h, 1s, 1min]
waits.Sort()
print(waits)
mut longs = [3000000000L, -1L, 2L]
longs.Sort()
print(longs)
float zero = float(0.0)
mut floats = [float(2.0), zero / zero, float(-1.5)]
floats.Sort()
print(floats)
mut List<string?> names = ["b", null, "a"]
names.Sort()
print(names)
mut people = [Person("Ann", "z"), Person("Bo", null), Person("Cy", "a")]
people.SortBy(p => p.Nick)
for p in people {
    print(p.Name)
}
```

Case: [strings/sort-by-two-keys.kz](../corpus/strings/sort-by-two-keys.kz)
```kurz
mut xs = [3, 1, 2]
xs.SortBy(x => x, x => -x)
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
`xs.AddRange(xs)` appends the items the list held before the call, so that the list is followed by
a copy of itself, as C#'s `List.AddRange` appends a list to itself: `AddRange` stores the items of its
argument before the list changes, and takes `Add`'s exception (M7; §4, the owner, 2026-10-31, "Store-first methods").

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

`IndexOf` has two forms, so that a literal argument keeps its own type (F14), and one that fits neither is
`argument-mismatch` (F13); items of a type `==` does not take give `type-mismatch`, as `==` on two such lists
does (M9).

Case: [strings/index-of-int-literal.kz](../corpus/strings/index-of-int-literal.kz)
```kurz
// `IndexOf` has two forms, so the literal keeps its own type, `int`, which fits neither (F14)
b = "ab".Bytes
print(b.IndexOf(97))
```

Case: [strings/index-of-functions.kz](../corpus/strings/index-of-functions.kz)
```kurz
mut fs = List<() => int>()
fs.Add(() => 1)
print(fs.IndexOf(() => 1))
```

Case: [strings/add-range-self.kz](../corpus/strings/add-range-self.kz)
```kurz
mut xs = [1, 2]
xs.AddRange(xs)
print(xs)
```

Case: [strings/add-range-without-mut.kz](../corpus/strings/add-range-without-mut.kz)
```kurz
xs = [1]
xs.AddRange([2])
print(xs)
```

### X10 (decided, §12) Where the error line of `not-char-boundary` names the position

The position of the cut stands on the line below the error line, where the text of a `throw` stands
(O6): `error: not-char-boundary at a.kz:5:`, then `byte 2 is inside a code point` (§12, the owner,
2026-10-31, "Below, as throw text"). The error line ends with a colon, as the line of a `throw` does
*(assumed, §12)*.

No case: a case pins the id and the line of the error (E3), and no case reads the lines below it.
