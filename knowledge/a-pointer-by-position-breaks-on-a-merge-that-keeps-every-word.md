---
name: a-pointer-by-position-breaks-on-a-merge-that-keeps-every-word
description: A clause of the record or the reference that names another clause by its place ("the assumed clause before this one") names a third clause after a merge of main that put one between them; on 2026-11-08 the take of main by pull request 387 merged its text word by word with every word kept, and its two pointers named the clause pull request 378 had added in the meantime, not the owner's sentence of 2026-11-06 they meant; the reference lint refuses a clause, reading, sentence or statement "before this one" or "after this one" in the record and the reference, and a clause names the other by its date and comment, its section or its rule
metadata:
  type: reference
---

## What happened

Pull request 387 wrote into the record's section 4, in the bullet on a generic function checked in
its copies, that a later call that reads a copy already made makes no chain of its own, and pointed
at the clause it stood on with "(the assumed clause before this one)". T33 of
`reference/03-types.md` said the same with "(the assumed reading before this one)". The clause they
meant was #361's assumed clause, that a copy is made at the first call that names its type
arguments.

Pull request 378 reached `main` while 387 was open. It made #361's clause a decided sentence, the
owner's answer of 2026-11-06 (issue 39, comment 6683085368), and put an assumed clause of its own
right after it, on a call that infers its type arguments and on a fault that several copies give.

387's take of `main` on 2026-11-08 had one conflict, in that bullet. It was merged word by word, and
the reading of the merge said that the two sides changed different words and that every word of both
was kept, which is one of the conflicts the owner's standing approval for a take covers. The merged
text was right word for word, and its two pointers now named #378's clause. Nothing that compares
words can see it: no word changed. The round of the follow-up, pull request 401, found it as a high
finding, beside a parenthesis of 401 that the decided sentence contradicted.

## What to do

- A clause names the clause it stands on by what does not move in a merge: its date and comment ("the
  owner's answer of 2026-11-06, comment 6683085368 on issue 39"), its section, or the rule's id.
- `tools/lint_reference.py` refuses, outside a code block, a clause, reading, sentence or statement
  "before this one" or "after this one" in `kurz-design.md` and `reference/*.md`, a pointer that a
  line break splits among them. Gate: `reference`.
- The lint does not read the wider words of place, "the sentence above", "the reading below", "the
  readings above", "the statement after": about twelve stand in the record and the reference, and none
  has broken. A take whose conflict sits beside one of them reads it by hand: `judgment step`.
- The kin of this trap is [a merge of main with every word kept can fail a test](a-merge-of-main-with-every-word-kept-can-fail-a-test.md):
  every word kept keeps neither every test nor every meaning.
