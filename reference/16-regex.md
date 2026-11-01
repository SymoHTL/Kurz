# 16. Regular expressions

A `Regex` matches text by a pattern, from the standard library beside the members of a string (X1
to X10), as C#'s base library gives `Regex` (§12, the owner, 2026-11-01, "In std"). A pattern is
usually written as a raw literal (L20), so that `\d` and `{2,4}` read as they do in the
documentation of the engines.

### G1 (decided, §12) The names

Every file sees the names `Regex`, `Match` and `RegexError` (§12, the owner, 2026-11-01, "In std",
whose cost named the three) and `RegexKind`, and a file's own declaration of one, a type, a
function, a variable or a parameter at any level, is `redeclared` (V9), while a field takes the
name. A group of a match is a `Match` too, as Rust's `Captures::get` gives one, so that no name
`Group` is taken, and the text of a `Regex` is its pattern, as Rust's `Display` gives it.
*(assumed, §12: `RegexKind` among the names, the forms of a declaration, as H1's, the group a
`Match` and the text)*

Case: [regex/regex-redeclared.kz](../corpus/regex/regex-redeclared.kz)
```kurz
data Match(int Index)

print(1)
```

### G2 (decided, §12) Two engines

`Regex.New(pattern)` runs the pattern on the linear engine, and `Regex.New(pattern, backtracking:
true)` on the backtracking one (§12, the owner, 2026-11-01, "Both, flag per pattern"). A search of
the linear engine takes time in proportion to the pattern's size times the text's length, whatever
pattern and text an attacker writes, as Rust's `regex` crate: the pattern is built into an
automaton that runs over the text once, a Pike VM with a lazy DFA where it pays, the automaton
bounded by the pattern's size and the lazy DFA's cache by a fixed capacity, which it empties when it
fills. Its cost is that the linear engine takes no backreference and no lookaround, which no such
automaton matches: a literal pattern that holds one is `regex-invalid` (G3). The backtracking engine
takes .NET's backreferences `\1` and `\k<name>`, its lookarounds `(?=`, `(?!`, `(?<=` and `(?<!`
and its atomic group `(?>`, and bounds no time, so that a crafted text can take time exponential in
its length and holds its actor's thread. For a pattern both take, both give the same match: the
leftmost one and, among the alternatives at its start, the first that matches, so that `a|ab`
matches `a` in `ab` on either. The automaton is bounded at 10 MiB and the lazy DFA's cache at 2 MiB,
the defaults of Rust's `size_limit` and `dfa_size_limit`. *(assumed, §12: the linear engine without
the flag, the flag's name and type, the constructs under it, no time bound, the same match on both
engines and the two bounds)*

Case: [regex/backtracking.kz](../corpus/regex/backtracking.kz)
```kurz
twice = Regex.New(r"(\w+) \1", backtracking: true)
print(twice.Match("say hello hello there")?.Value)
weight = Regex.New(r"\d+(?= kg)", backtracking: true)
print(weight.Match("5 m, 12 kg")?.Value)
print(Regex.New("a|ab").Match("ab")?.Value)
print(Regex.New("a|ab", backtracking: true).Match("ab")?.Value)
```

Case: [regex/backreference-linear.kz](../corpus/regex/backreference-linear.kz)
```kurz
twice = Regex.New(r"(\w+) \1")
print(twice.IsMatch("a a"))
```

### G3 (decided, §12) A pattern's errors

A pattern given to `Regex.New` as a literal without interpolation, raw or not (L20), with no flag or
a flag written as `true` or `false`, is checked when the program compiles: one that does not parse,
that takes a construct its engine does not take (G2) or whose automaton is above the bound (G2) is
the compile error `regex-invalid` at the literal, and the call gives a `Regex`. `Regex.New` of any
other string, a literal with interpolation among them, gives `Regex | RegexError` when it runs, as
Rust's `Regex::new` gives a `Result` (§12, the owner, 2026-11-01, "Compile time for literals").
`data RegexError(RegexKind Kind, int At)` with `enum RegexKind { Syntax, Unsupported, TooBig }` holds
a pattern that does not parse, a construct the engine does not take and an automaton above the
bound. `At` is the position in the pattern, counted in bytes, where the construct starts, as Rust's
error gives its span, and 0 for `TooBig`. Its text is the words of its kind in lower case and its
position, `syntax at 1`, as a `HexError`'s (H7), and it holds nothing of the pattern or of a text it
was matched against. *(assumed, §12: the id, the `Regex` of a checked literal, the flag written as a
literal, the error's type, kinds, position and text)*

Case: [regex/invalid-literal.kz](../corpus/regex/invalid-literal.kz)
```kurz
closing = Regex.New("a)")
print(closing.IsMatch("a"))
```

Case: [regex/runtime-error.kz](../corpus/regex/runtime-error.kz)
```kurz
void Try(string pattern) {
    match Regex.New(pattern) {
        Regex re => print(re)
        RegexError e => print(e)
    }
}

Try("a)")
Try(r"(a)\1")
Try(r"(?:\w{1000}){1000}")
Try(r"\d+")
word = "a)"
match Regex.New("{word}+") {
    Regex re => print(re)
    RegexError e => print(e.Kind)
}
```

### G4 (assumed, §12) Matching

`re.IsMatch(s)` is a `bool`; `re.Match(s)` is the first match, a `Match?`, `null` where none is; and
`re.Matches(s)` is every match, none overlapping another, a `List<Match>`. A `Match` has `Value`,
`Index` and `Length`, and `m.Group(n)` and `m.Group(name)` give a group, a `Match?`, `null` for a
group that did not take part and for one the pattern does not have, `m.Group(0)` the whole match. A
position and a length count bytes, as a position in a string does (X2), and `\d`, `\w`, `\s`, `\b`
and a match that ignores case take Unicode's classes from the tables of the version §12 pins. The syntax is that of Rust's `regex` crate, Perl's without backreferences and lookaround,
with `(?<name>...)` and `(?P<name>...)` both and the flags `(?i)`, `(?m)`, `(?s)` and `(?x)`; a
`Regex` has members alone, with no static `Regex.IsMatch(input, pattern)`. *(assumed, §12: the
whole rule, the defaults of the product pass, which the owner's answers of 2026-11-01 took as they
stand, and the spelling `Group`, the group 0 and the group the pattern does not have, not put to the
owner)*

Case: [regex/match.kz](../corpus/regex/match.kz)
```kurz
date = Regex.New(r"(\d{4})-(?<month>\d{2})-(\d{2})")
print(date.IsMatch("on 2026-11-01"))
print(date.IsMatch("on 2026-11"))
m = date.Match("on 2026-11-01!")
if m != null {
    print(m.Value)
    print(m.Index)
    print(m.Length)
    print(m.Group(1)?.Value)
    print(m.Group("month")?.Value)
    print(m.Group(0)?.Value)
}
print(date.Match("none")?.Value)
either = Regex.New("(a)|(b)").Match("b")
if either != null {
    print(either.Group(1)?.Value)
    print(either.Group(2)?.Value)
}
```

Case: [regex/matches.kz](../corpus/regex/matches.kz)
```kurz
for m in Regex.New(r"\d+").Matches("a1b22c333") {
    print("{m.Value} at {m.Index}")
}
print(Regex.New("aa").Matches("aaaa").Count)
print(Regex.New(r"\d").Matches("abc").Count)
```

Case: [regex/unicode.kz](../corpus/regex/unicode.kz)
```kurz
digits = Regex.New(r"\d+").Match("x١٢٣")
print(digits?.Index)
print(digits?.Length)
print(Regex.New("b").Match("éb")?.Index)
print(Regex.New(r"^\w+$").IsMatch("wörld"))
print(Regex.New("(?i)é").IsMatch("É"))
```

### G5 (decided, §12) Replacing and splitting

`re.Split(s)` gives the pieces between the matches with the text of each of the pattern's groups
between them, as C#'s `Regex.Split` (§12, the owner, 2026-11-01, "Groups included"), so that a group
added for grouping alone changes the result, and a group that did not take part is left out, as C#'s
leaves it out. `re.Replace(s, replacement)` replaces every match, as C#'s `Replace` and Rust's
`replace_all`, and reads the replacement as Rust's does: `$name` takes the longest run of letters,
digits and `_`, so that `$1a` names a group `1a`, a name of digits alone names a group by its number,
`${name}` ends the name at its brace, `$$` is a `$`, and a group that does not exist or did not take
part gives no text. A replacement with `${name}` is written as a raw literal (L20), since `{` opens
an interpolation in every other literal (L6). *(assumed, §12: the group left out of `Split`,
`Replace` of every match and Rust's reading of the replacement, where C# reads `$1a` as group 1 and
an `a` and keeps `$9` of a group that does not exist as it is written)*

Case: [regex/replace.kz](../corpus/regex/replace.kz)
```kurz
date = Regex.New(r"(?<year>\d{4})-(?<month>\d{2})")
print(date.Replace("2026-11 and 2027-01", "$2/$1"))
print(date.Replace("2026-11", r"${month}.${year}"))
print(date.Replace("2026-11", r"${1}a"))
print(date.Replace("2026-11", "$1a"))
print(date.Replace("2026-11", "$$"))
print(Regex.New("a").Replace("banana", "o"))
```

Case: [regex/split.kz](../corpus/regex/split.kz)
```kurz
print(Regex.New("[-+]").Split("1+2-3"))
print(Regex.New("([-+])").Split("1+2-3"))
print(Regex.New(r"(-)|(\+)").Split("1+2-3"))
print(Regex.New(",").Split(",a"))
```
