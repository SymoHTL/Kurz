---
name: a-reading-of-the-checker-is-probed-before-a-text-states-it
description: A proposed text for T34 (issue 156, comment 6621488565, item 4) read the checker's code and concluded that `Map<f.Type, T>` in a loop over `T.Fields` takes part in the walk; a probe with the built compiler (comment 6623863337, 2026-11-04) found that the walk takes no such call, and that one such form does not end within 30 seconds. A sentence that says what the compiler does is run on the compiler before it goes into the reference or the record
metadata:
  type: feedback
---

On 2026-11-04 the follow-up of pull request 294 proposed texts for T34 (issue 156, comment
6621488565). Item 4 read `Grows` in `Checker.Copies.cs`: the checker makes an edge for each type
parameter of the caller that a type argument mentions, and the field's type is none, so in
`Map<f.Type, T>` the `T` grows and the call takes part. The item proposed to say so in T34.

A probe with the compiler of `main` at fcc948e, run before the text was written, showed otherwise
(comment 6623863337). The programs were loops over `T.Fields` in `void Deep<T>(T x, int n)`:

- `Deep(Map<f.Type, T>(), n - 1)` gives no `growing-type-argument`; the copy is `kind-not-read` at
  the outer call.
- `Deep(Pair<f.Type, T>(f.Get(x), x), n - 1)` gives no `growing-type-argument` either, and its
  check does not end within 30 seconds. That is the defect of T33's bound recorded on issue 156,
  comment 6623885460: at fcc948e the bound is checked per chain, and a copy that makes one call for
  each of its two fields makes about 2^64 copies.
- The controls, `Deep(Map<int, T>(), n - 1)` and `Deep(Pair<int, T>(1, x), n - 1)`, are
  `growing-type-argument` at the call.

The reading missed what happens before `Grows`. On the function's text `f.Type` has no type
(`ResolveDotted` finds no pass outside a copy): there it is the error type, with a `not supported`
for a dotted name as a type that the build of fcc948e does not print, and the walk takes no such
call at all. The text was held, and the case went to the owner as a question. The owner answered
it on 2026-11-04 (comment 6643376941, item 4, answer (a)): `T` grows, and the call is refused at
its line, since the walk types `f.Type` on the text. What the probe showed is a defect of the
checker of fcc948e, which a type of its own for `f.Type` on the text ends.

**Why:** a reading of code follows the function it looks at, and what comes before that function
decides whether it runs. A sentence of the reference or the record that says what the compiler
does is taken by its readers as a rule. Nothing runs that sentence, so a wrong one stands as long
as no program meets it.

**How to apply:**

- Before a text says what the checker or the emitter does, run a program that shows it on the
  built compiler, beside a control that differs in the one thing the sentence is about, and cite
  the probe where the text is proposed.
- Where the probe and the reading differ, the text waits. The difference is a defect of the
  compiler or a question for the owner, never a sentence written from the code.
- It is the code's form of reading a figure in its source before citing it
  ([reading-a-paper-for-evidence](reading-a-paper-for-evidence.md)), and of running the behaviour a
  rule names before a case takes its numbers
  ([samples-obey-the-rules-beside-them](samples-obey-the-rules-beside-them.md)). `judgment step`
