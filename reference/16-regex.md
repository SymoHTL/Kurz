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
in `Regex.New`, so that `a{655357}` builds and `a{655358}` is `TooBig` (§12, the owner, 2026-11-12, issue 176,
comment 6819137493, Q9 of comment 6808653712). The states are three
for the whole match, its start, its end and the match; one for a character, a class or an assertion;
two for a group with a name or a number beside its content; the sum of a sequence's items; the sum of
an alternation's branches with two for each `|`; and for a repetition of n to m, written `{n,m}`, `{n}`
(m is n) or `?` (0 to 1), n copies of its item and m - n optional copies of one state more each, for
one of n or more, written `{n,}` or `+` (n is 1), n copies and one state, but for `*` and `{0,}` one
copy and two states, a lazy repetition as the greedy one, and a repetition of an item that matches no
character taken with its least and its most at most 1, as Rust's `Hir::repetition` takes it, so that
`\b{1000000}` builds. A class counts the numbers of its ranges once where it is written, after `(?i)` folded it
and after its negation, a letter that `(?i)` gives another case counts as the class of its cases, and
a pattern with a word boundary counts the numbers of `\w`'s ranges once more. A size at the bound
builds. A search takes time in proportion to the text's length times the automaton's states times one
more than the positions a thread carries, which are none for `IsMatch`, so that it takes the text's
length times the states, and for the members that give a match two for the whole match and two for
each group, which the Pike VM copies from thread to thread as Rust's `PikeVM` copies its slots; beside
the automaton a search holds two lists of threads, its states times one more than those positions,
which the bound does not count, as Rust's `size_limit` does not count the `PikeVM`'s cache. *(assumed,
§12: the states and the ranges of each construct, a size at the bound that builds, and the time and the
memory of a search)*

The lazy DFA runs before the Pike VM: its states are sets of the automaton's states, each with what
the character before its position is to the assertions, and each transition is made on the first
character of its class that needs it and read from a table after that, over classes of code points
where Rust's are of bytes, the code points that every operation of the automaton and every assertion
read alike being one class, made once with the pattern. A state's members are the threads begun
before its position: the thread of a new start is added at each transition, before the transition
reads its character, and is not kept in the stored state, while the threads it reaches over the
transition are members of the next state, so that a member reached both from an earlier start and
from the current one keeps the earlier start, which costs time and never a match. `IsMatch` takes its answer from it, and `Match`, `Matches`, `Replace`
and `Split` run the Pike VM only where it finds a match left, from the last position up to that
match's end, the end included, at which its state had no member, before which no match starts, so
that `a*b` over `aab` runs the Pike VM from 0; where the DFA gives a search up or is not used, the
Pike VM answers `IsMatch` and runs `Match`, `Matches`, `Replace` and `Split` alone. Its cache is made for each call, beside
the automaton and outside its bound, and the searches of one `Matches`, `Replace` or `Split` share it
and its count of emptyings, so that a call whose DFA gave up runs the Pike VM for the rest of the
call. It counts a state as 4 bytes for each column of its row and each of its members and 32 bytes
of its own, and it is emptied when the next state does not fit. At an emptying the DFA gives the
search up where the cache was emptied at least three times before and the searches read fewer bytes of UTF-8
since the last emptying than 10 times the states made since then, as Rust's meta engine sets
`minimum_cache_clear_count` and `minimum_bytes_per_state` and its `try_clear_cache` checks before it
clears; and the DFA is not used where two states that each hold every state of the automaton as a
member, with their rows, and 8 bytes for each state of the automaton exceed the cache's 2 MiB, as
Rust's `minimum_cache_capacity` asks room for two such states beside three sentinel states, which
this cache does not have; so that a search's time stays in proportion to the pattern's size times
the text's length. The cache and the Pike VM's lists of threads are lists, and one that cannot grow
ends the process with `allocation-failed`, as a list's does (M9). The DFA costs `Regex.New` the
making of its classes, once for each pattern; a call the making of the states its cache holds, which
a short text pays before the Pike VM runs; and a call up to 2 MiB for its cache (§12, the owner, 2026-11-12, issue 176, comment 6819137493,
Q1, Q2 and Q4 of comment 6813110851). *(assumed, §12: the count of a state, the members a state
keeps, the cache the searches of one call share, the checks at an emptying and of the capacity, and
the end of a list that cannot grow)* Below a text length measured on a benchmark the Pike VM runs
alone, without the DFA (§12, the owner, 2026-11-12, issue 176, comment 6819137493, Q3 of comment
6813110851); the compiler does not build that length, and its DFA runs first at every length.

The backtracking engine reads its constructs as .NET's does: a lookbehind of any length; a
backreference of a group that did not take part fails; under `(?i)` a backreference compares by the
simple case folding, the flag in force at the backreference deciding, as .NET's does, so that `(?i)(a)\1`
and `(a)(?i)\1` match `aA` and `(?i)(a)(?-i)\1` does not; a backreference to a later group or to its own
group parses and fails while the group has no text, so that `(a|b\1)+` matches `aba`; a positive
lookaround keeps the groups it took and a negative one none; `\k<name>` is the one spelling of a
named backreference; a search has no limit on its steps; and the automaton is counted and bounded as
the linear engine's, a backreference one state and a lookaround or an atomic group two beside its
content, so that a pattern both engines take is `TooBig` on both or on neither; a construct the linear
engine does not take is `Unsupported` there, which the parse reports before the automaton is counted (§12, the owner, 2026-11-12, issue 176,
comment 6819137493, Q1 to Q7 and Q10 of comment 6811727642, but for the flag in force at the
backreference, the states each construct counts and the `Unsupported` of the linear engine)
*(assumed, §12: those three, the last Q8 of comment 6811727642 at its default)* A
backreference to a group the pattern does not have is `Syntax` at its `\`, as .NET refuses one of a
single digit when the pattern is made, and one inside a class is `Unsupported` at its `\`. Where the
parse refuses a construct, a backreference it read before it stopped, to a group whose `(` it did not read, is
the error, the first such where it read several, as .NET, which counts the groups before it parses,
reports `\9(` and `(\9`, so that `\8\9(` is `Syntax` at 0; a group whose `(` it read counts, so that
`(a\1` is `Syntax` at its `(`, which no `)` closes; and a group after
the refused construct is not read, so that `\2(a)\q(b)` is `Syntax` at 0 where .NET reports the `\q`. A backreference by number reads every digit
after its `\`, so that `\10` names group 10 and is `Syntax` in a pattern of fewer groups, where .NET
reads a number of two or more digits that names no group as an octal escape (`(a)\10` matches `a`
and U+0008 there), and `\0` is `Unsupported`, as on the linear engine. A search keeps its choices
on a stack in memory, not on the process's stack, as .NET's interpreter does, a stack that grows
with the text's length times the choices a way keeps open, beside the automaton and outside its
bound, where the Pike VM's lists of threads, outside it too, are sized by the automaton's states,
and a stack that cannot grow ends the process with `allocation-failed`, as a list's does (M9); a loop that comes back
to its start with no character read since fails that way, as the Pike VM's thread that comes back to
a state at the same position ends, so that `(a*)*` ends and both engines give the same match, where
.NET takes a last iteration that reads no character: on `aa` group 1 is `aa` here and empty at 2 in
.NET; and a lookbehind matches its content from right to left, from its position back, as .NET
matches it, so that `(?<=)b` and `(?<=a*)b` match `b` and a group inside a lookbehind takes the text
its content reads so (§12, the owner, 2026-11-12, issue 176, comment 6819137493, Q15 of comment
6812042360); the compiler does not build that direction: its lookbehind tries its position and each
one before it, the nearest first, its content ending at its position, so that a group inside it
takes the text of the nearest start its content matches from. *(assumed, §12: the stack in memory, Q9 of comment
6811727642, taken at its default; and, proposed with this record and not put to the owner, the errors of a
backreference, the reading of its digits and a loop that comes back to its start with no character read)*

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
and a match that ignores case take Unicode's classes from the tables of the version §12 pins. The syntax is that of Rust's `regex` crate, Perl's without backreferences and lookaround on the
linear engine, to which `backtracking: true` adds the backreferences, lookarounds and atomic group
of G2,
with `(?<name>...)` and `(?P<name>...)` both and the flags `(?i)`, `(?m)`, `(?s)` and `(?x)`; a
`Regex` has members alone, with no static `Regex.IsMatch(input, pattern)`. *(assumed, §12: the
whole rule, the defaults of the product pass, which the owner's answers of 2026-11-01 took as they
stand, and the spelling `Group`, the group 0 and the group the pattern does not have, not put to the
owner)* The flags `U`, `R` and `u` read as Rust's crate reads them: `U` swaps greedy and lazy; under
`(?mR)` `^` and `$` take `\r`, `\n` and `\r\n` as a line's end, and under `(?R)` and without `(?s)` a `.` takes neither;
and `u` is in force from the start, without it `\d`, `\w`, `\s`, the word boundaries and the folding
of `(?i)` are ASCII's, and a construct that would match a byte of no character, `.`, a class that holds
more than ASCII after its negation, `\D`, `\W`, `\S`, `\xNN` above 7F and a class's character above
ASCII, is `Syntax` at its start (G3), after any error of the parse and of the levels, and among
several the first that Rust's translator meets, a class's items before the class.
*(assumed, §12: proposed with this record and not put to the owner, as item 2 of the round of pull
request 484 asked, issue 39, comment 6810616304)* The classes `\p{...}` and `\P{...}`, and `\pX` of one letter, read a
name as Rust's crate reads it, from the tables of the version §12 pins, where the crate holds 16.0.0, so that a name or a
code point of 17.0.0 alone is a class here and `Syntax` or no match in Rust: a general category, a script, a binary
property of that version's `PropList.txt`, `DerivedCoreProperties.txt` or `emoji-data.txt`, or `Bidi_Mirrored` of
its `UnicodeData.txt`, the files the crate's tables of binary properties are made from, and `Any`, `ASCII` and `Assigned`, alone or as values of the general category, or by
value the general category, the script, the script extensions, the age, which holds every version up to it, and the break
properties Grapheme_Cluster_Break, Sentence_Break and Word_Break, as `name=value`, `name:value` or `name!=value`, which
negates the class once more. A binary property of another file, as `Full_Composition_Exclusion` of
`DerivedNormalizationProps.txt`, is no class. A name is compared after three steps in turn: a first `is`, in either case of each
letter, is set aside where the name begins with it as written; then ` `, `_` and `-` and every byte above ASCII are
dropped and its capitals made small; then a name that had that `is` and is `c` alone is `isc`, so that `Is_C` is
`isc` and `I_sGreek` keeps its `is`. A name alone is a binary property's, but for `cf`, `sc` and
`lc`, then a general category's, then a script's, and the name alone of a property that is not binary is no class. Under
`(?i)` a class is folded before `\P` or `!=` negates it, as Rust's translator folds it. A name for which the tables of
that version, read so, hold no class, and every `\p` and `\P` under `(?-u)`, is `Syntax` at its `\` (G3), after any
error of the parse and of the levels, and among several the first Rust's translator meets, as for the flag `u`; a class the pattern's end cuts off is `Syntax` at the
end, and a `\` where its letter stands is `Syntax` at that `\`. *(assumed, §12: the owner's answer of 2026-11-12 to Q1 to Q6 of issue 176, comment
6812529825, read as all six standing as built and said back in comment 6819137493, for the version, every property the crate reads and a name with no class `Syntax` at its `\`; the readings of the crate asked of it that day for the rest)* A `Regex` and a `Match` hold no `mut` field, so `==` compares two of them by their content
(K4): two `Regex`es of one pattern on one engine are equal, and one pattern made with and without the flag
`backtracking` gives two that are not. (§12, the owner, 2026-11-12, issue 176, comment 6819137493, Q12 of comment 6808653712, whose option named the engine)

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
print(digits == Regex.New(r"\d+", backtracking: true))
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
by `Split` as one piece (§12, the owner, 2026-11-12, issue 176, comment 6819137493, Q1 and Q2 of
comment 6810129884, the walk and the order).

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
