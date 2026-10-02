# The Kurz reference

[kurz-design.md](../kurz-design.md) records what was decided and why. This reference says the
same thing rule by rule, precisely enough to write a compiler and its tests against it. It adds
nothing to the design: where the record is silent, a rule here is marked `proposed` or `open`,
and it stays that way until Simon has answered.

It covers the sequential core: what a program without actors needs. Actors, distribution,
keywords, jobs, tests, packages and clients (sections 6, 7 and 9 to 13 of the record) are not
covered yet.

## The status of a rule

Every rule has an id and a status, and cites the sections of the record it comes from.

- `decided`: Simon chose it.
- `assumed`: the record marks it *(assumed)*: it was proposed to Simon and not objected to.
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

No compiler exists yet, so nothing runs these files. `tools/lint_reference.py` keeps the
reference and the corpus consistent with each other; whether an expectation is right is decided
by reading it against the rules, and by the review.

## What every case relies on

### A1 (proposed) `print`

`print(value)` writes the text of the value and a line break to standard output. The corpus needs
one way to show a result, and the record's samples use this word. The naming of the standard
library is open (record, section 14), so the word can change.

No case: every case that expects output shows it.

### A2 (proposed) The text of a value

The text of an integer is its decimal digits, with a leading `-` when negative. The text of a
string is the string. The text of a `bool` is `true` or `false`. The same text is used inside an
interpolated string (L6).

No case: every case that expects output shows it.

### A3 (open) The text of other values

What `print` and interpolation produce for `null`, for a `data` value, a class instance, a
collection, a `float`, `double` or `decimal`. Options: (a) no text at all, so that printing one is
a compile error until the type says how; (b) a fixed form derived from the type, as C# records
have; (c) text only for the built-in types. Lean: (b) for `data` and collections, because a
language whose selling point is short code should print a record without a helper.
