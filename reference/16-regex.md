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
engines and the two bounds)* The automaton's size is counted as 16 bytes for each of its states and 4
bytes for each number that bounds a range of its classes, the same in the check of a literal (G3) and
in `Regex.New`, so that `a{655357}` builds and `a{655358}` is `TooBig`. *(assumed, §12: the default
of the question put to the owner on 2026-11-11, issue 176, comment 6808653712)* The states are three
for the whole match, its start, its end and the match; one for a character, a class or an assertion;
two for a group with a name or a number beside its content; the sum of a sequence's items; the sum of
an alternation's branches with two for each `|`; and for a repetition `{n,m}` n copies of its item and
m - n optional copies of one state more each, for `*` one copy and two states, for `{n,}` n copies and
one state. A class counts the numbers of its ranges once where it is written, after `(?i)` folded it
and after its negation, a letter that `(?i)` gives another case counts as the class of its cases, and
a pattern with a word boundary counts the numbers of `\w`'s ranges once more. A size at the bound
builds. A search takes time in proportion to the text's length times the automaton's states times the
positions a thread carries, none for `IsMatch` and for the members that give a match two for the whole
match and two for each group, which the Pike VM copies from thread to thread as Rust's `PikeVM` copies
its slots; beside the automaton a search holds two lists of threads, its states times those positions,
which the bound does not count, as Rust's `size_limit` does not count the `PikeVM`'s cache. *(assumed,
§12: the states and the ranges of each construct, a size at the bound that builds, and the time and the
memory of a search)*

The backtracking engine reads its constructs as .NET's does: a lookbehind of any length; a
backreference of a group that did not take part fails; under `(?i)` a backreference compares by the
simple case folding, so that `(?i)(a)\1` matches `aA`; a backreference to a later group or to its own
group parses and fails while the group has no text, so that `(a|b\1)+` matches `aba`; a positive
lookaround keeps the groups it took and a negative one none; `\k<name>` is the one spelling of a
named backreference; a search has no limit on its steps; and the automaton is counted and bounded as
the linear engine's, a backreference one state and a lookaround or an atomic group two beside its
content, so that a pattern is `TooBig` on both engines or on neither. *(assumed, §12: the defaults of
the questions put to the owner on 2026-11-12, issue 176, comment 6811727642, Q1 to Q7 and Q10)* A
backreference to a group the pattern does not have is `Syntax` at its `\`, as .NET refuses one when
the pattern is made, and one inside a class is `Unsupported` at its `\`. A search keeps its choices
on a stack in memory, not on the process's stack, as .NET's interpreter does; a loop that comes back
to its start with no character read since fails that way, as the Pike VM's thread that comes back to
a state at the same position ends, so that `(a*)*` ends and both engines give the same match; and a
lookbehind tries the starts before its position from the nearest one on, so that a group inside a
lookbehind takes the text of the nearest start its content matches from, where .NET matches a
lookbehind from right to left. *(assumed, §12: Q9 of that comment, taken at its default; the
lookbehind's start, the default of Q15, put to the owner on 2026-11-12, issue 176, comment
6812042360; and the rest proposed with this record and not put to the owner)*

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

Case: [regex/bound.kz](../corpus/regex/bound.kz)
```kurz
void Try(string pattern) {
    match Regex.New(pattern) {
        Regex re => print(re)
        RegexError e => print(e)
    }
}

Try(r"a{655357}")
Try(r"a{655358}")
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
owner)* A `Regex` and a `Match` hold no `mut` field, so `==` compares two of them by their content
(K4): two `Regex`es of one pattern are equal. *(assumed, §12: the default of the question put to the owner on 2026-11-11, issue 176, comment 6808653712)*

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

Case: [regex/equality.kz](../corpus/regex/equality.kz)
```kurz
digits = Regex.New(r"\d+")
same = Regex.New(r"\d+")
print(digits == same)
print(digits == Regex.New(r"\d*"))
first = digits.Match("a12")
again = same.Match("a12")
other = digits.Match("a34")
if first != null && again != null && other != null {
    print(first == again)
    print(first == other)
}
```

### G5 (decided, §12) Replacing and splitting

`re.Split(s)` gives the pieces between the matches with the text of each of the pattern's groups
between them, as C#'s `Regex.Split` (§12, the owner, 2026-11-01, "Groups included"), so that a group
added for grouping alone changes the result, and a group that did not take part is left out, as C#'s
leaves it out. `re.Replace(s, replacement)` replaces every match, as C#'s `Replace` and Rust's
`replace_all`, and reads the replacement as Rust's does: `$name` takes the longest run of letters,
digits and `_`, so that `$1a` names a group `1a`, a name of digits alone names a group by its number,
with `0`s before it or not, as does a name of one `+` and digits inside braces, `${+1}`, as Rust's
parse of a `usize` reads one, `${name}` ends the name at its brace, `$$` is a `$`, and a group that does not exist or did not take
part gives no text. A replacement with `${name}` is written as a raw literal (L20), since `{` opens
an interpolation in every other literal (L6). A `$` that names no group, at the end, before a byte
that is no letter, digit or `_`, or before a `{` without its `}`, is a `$`. *(assumed, §12: the group left out of `Split`,
`Replace` of every match and Rust's reading of the replacement, where C# reads `$1a` as group 1 and
an `a` and keeps `$9` of a group that does not exist as it is written)* Both take the matches `Matches` gives
(G4), so that an empty match right after another match is passed over, and `Split` adds a match's
groups in the order of their numbers; a text with no match is given back by `Replace` as it is and
by `Split` as one piece. *(assumed, §12: the defaults of the questions put to the owner on
2026-11-12, issue 176, comment 6810129884)*

Case: [regex/replace.kz](../corpus/regex/replace.kz)
```kurz
date = Regex.New(r"(?<year>\d{4})-(?<month>\d{2})")
print(date.Replace("2026-11 and 2027-01", "$2/$1"))
print(date.Replace("2026-11", r"${month}.${year}"))
print(date.Replace("2026-11", r"${1}a"))
print(date.Replace("2026-11", "$1a"))
print(date.Replace("2026-11", "$$"))
print(date.Replace("2026-11", r"${+1}"))
print(Regex.New("a").Replace("banana", "o"))
```

Case: [regex/split.kz](../corpus/regex/split.kz)
```kurz
print(Regex.New("[-+]").Split("1+2-3"))
print(Regex.New("([-+])").Split("1+2-3"))
print(Regex.New(r"(-)|(\+)").Split("1+2-3"))
print(Regex.New(",").Split(",a"))
```

Case: [regex/walk.kz](../corpus/regex/walk.kz)
```kurz
print(Regex.New("x*").Split("axb"))
print(Regex.New("x*").Replace("axb", "-"))
print(Regex.New(",").Split("abc"))
print(Regex.New(",").Replace("abc", "-"))
print(Regex.New("(?<sign>[a-z])(=)").Split("1a=2"))
```
