---
name: a-case-bakes-in-a-pick
description: Writing the first conformance cases (2026-10-02) needed rules the design record never states and met forks nobody had chosen; a runnable case fixes a spelling and a meaning, so the reference marks such rules proposed or open and the lint refuses a case on an open rule
metadata:
  type: feedback
---

The design record was written as decisions with illustrative samples. When the sequential core
was written down rule by rule on 2026-10-02, with one small program per rule, the cases needed
more than the record holds. 33 of the first 121 rules were things the record never states: that
`print` exists, that a block body ends with `return`, where a variable is visible, what "used"
means for the unused-variable error, that `match` has to list every case. 24 more were forks the
record left open or contradicted itself on: two samples used `;` although section 8 says there
are no semicolons, one sample wrote a `weak` type with `?` and without, a range was written
`0..150` and nobody had said whether the end is included.

The owner answered all of them the same day, in one round. Writing the cases for those answers
met seven new forks (how a set of flags is written, which overload a call picks, how a class
with a primary constructor inherits) and needed two more rules the record does not state. The
answers to those nine met nine forks again and one more unstated rule. It repeats with every
batch of cases, and each fork is narrower than the answer it came from: after "a class names the
fields that count" the fork is how it names them.

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
- A design round asks about `proposed` and `open` rules by id. A `proposed` rule that the owner
  does not object to is still not decided: it goes into the record as *(assumed)* first. When a
  batch is accepted with one word, only the rules the question named are decided; the ones that
  were not shown stay *(assumed)*, and the reply says so. Of the 33 accepted on 2026-10-02, six
  were named.
- An answer decides what its option said and nothing next to it. "A `"""` block, as in C#" does
  not say whether `{` interpolates inside. The rule becomes `decided` for the part that was
  chosen, its case is written around the rest, and the rest is a new `open` rule.
- The cases for an answer are written around the forks they meet, as the first ones were. The
  new forks are questions for the next round, not picks to fold into this one.
- When writing a sample for the record, expect the same thing: a sample decides more than the
  sentence next to it ([[samples-obey-the-rules-beside-them]]).
