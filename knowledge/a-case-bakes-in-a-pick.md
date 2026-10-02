---
name: a-case-bakes-in-a-pick
description: Writing the first conformance cases (2026-10-02) needed rules the design record never states and met forks nobody had chosen; a runnable case fixes a spelling and a meaning, so the reference marks such rules proposed or open and the lint refuses a case on an open rule
metadata:
  type: feedback
---

The design record was written as decisions with illustrative samples. When the sequential core
was written down rule by rule on 2026-10-02, with one small program per rule, the cases needed
more than the record holds. 33 of the 121 rules are things the record never states: that
`print` exists, that a block body ends with `return`, where a variable is visible, what "used"
means for the unused-variable error, that `match` has to list every case. 24 more are forks the
record leaves open or contradicts itself on: two samples use `;` although section 8 says there
are no semicolons, one sample writes a `weak` type with `?` and without, a range is written
`0..150` and nobody said whether the end is included.

**Why:** a case is concrete. `for x in xs { }` in a file that "must print 4 and 5" decides the
spelling of the loop, the scope of `x` and the order of iteration, whether anybody chose them or
not. Once sixty cases use it, it is the language. The v0 compiler failed the same way
([[design-before-build]]): picks made by writing code instead of by asking.

**How to apply:**

- A rule the record does not carry is never `decided` in the reference. It is `proposed` when
  cases need it to stand on, and it says so in its text; it is `open` when it is a real fork,
  with its options and a lean.
- No case on an open rule. `tools/lint_reference.py` (gate `reference`) refuses one, and refuses
  a case that names an open rule in its header.
- Where a case would need an open rule, write the case around it and say so in the rule. The
  corpus puts one statement per line and writes `weak T?` for that reason.
- A design round asks about `proposed` and `open` rules by id. A `proposed` rule that Simon does
  not object to is still not decided: it goes into the record as *(assumed)* first.
- When writing a sample for the record, expect the same thing: a sample decides more than the
  sentence next to it ([[samples-obey-the-rules-beside-them]]).
