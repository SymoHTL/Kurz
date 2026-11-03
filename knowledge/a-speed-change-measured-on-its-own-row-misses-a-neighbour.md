---
name: a-speed-change-measured-on-its-own-row-misses-a-neighbour
description: Pull request 197 measured the runtime's `Order` in a bench where `<` sat in several functions, so `Order` stayed a call, and shipped a form that doubled the time of the sweep's `<` row, where `Order` has one caller and went into the loop; a speed change is measured on the sweep's rows of every function it touches, with each function both as a call and inlined into a loop of one caller (issue 157, item 16, 2026-11-02)
metadata:
  type: feedback
---

## What happened

Pull request 197 (issue 157, item 11) made the runtime's `Order`, behind `<`, `<=`, `>`, `>=` and the
sorts of strings, compare the first byte before any word, and set the order to `-1` or `1` by a
comparison of the two bytes or words that differed. Its bench put `<` in several functions, so that
`Order` had several callers and stayed a call (`nm` of the executable lists `rt.common.Order`), and
there `<` of 200k words of 3 to 10 random letters went from 17.59 to 14.72 ms.

The sweep's strings program (issue 157, comment 6559384999) holds one `<`. There `Order` has one
caller, and LLVM puts such a function into its caller at nearly any size
([[a-helper-of-several-callers-stays-a-call]]). Inside the loop, LLVM turned the `-1` or `1` that
the comparison chose into a branch on that comparison, which for random words goes one way or the other
on every second pair. The same row went from 16.46 ms on `main` before the change to 34.30 ms after it,
against Rust's 18.11, the best of seven runs each; in item 16's bench of that shape, 16.36 ms at the
commit the change was merged onto and 35.65 ms at the change's head. Before the change, the order was
the difference of the two bytes, `int(x[i]) - int(y[i])`, a value, which LLVM compiled into `setb` and
`cmov` with no branch. The bench of pull request 197 had no row of that shape, and the sweep walk of
item 16 found the regression after the merge.

## What to do

- Measure a speed change on the sweep's rows of every function it touches, beside the rows of its own
  bench, and list them in the description. For `Same` and `Order` those are `compare-equal` and
  `compare-less` of the strings family; a sort of strings also reads `Order`, and a map whose keys
  are strings and `indexof-string` of the lists family, an `IndexOf` on a list of strings, read `Same`.
- Measure each touched function in both shapes: as a call, in a program where it has several
  callers, and inlined, in a program where one function holds its one caller, in a loop.
  `nm <executable> | grep <name>` says which shape a program has: a function inlined at its every
  caller has no symbol.
- Measure a result that a caller branches on in both forms, computed as a value and chosen by a test,
  each inlined into a loop of one caller: neither form wins everywhere. Where the first bytes differ,
  the difference of the two bytes, a value, kept the sweep's `<` at 16.46 ms against 34.30 for a `-1`
  or `1` chosen by a test (above). Where two words differ, item 16's `Order` took 49.65 ms on 50 rounds
  of 200k sorted neighbours with a `-1` or `1` chosen by a test of which word is the lower, against
  52.99 with a value made from the words' bits, the best of five runs of two builds that differ in that
  line alone. LLVM can turn constants that branches choose into a branch at the caller, and that
  branch mispredicts where the data is random.

No CI job runs a benchmark, so nothing goes red when a change skips this: `judgment step`.
