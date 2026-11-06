---
name: a-clean-merge-of-main-can-fail-a-test
description: A take of main whose text conflicts were only disjoint words, and whose own diff was the reviewed one, failed a test on the merged head (pull request 288, 2026-11-06): a test row main had added for one rule expected an error that the pull request's own rule now reports first; neither the text merge nor the comparison of the own diff can see that, so the gates run on the merge commit, in the image, before the push and before the take is described to the owner
metadata:
  type: reference
---

## What happened

Pull request 288 makes a variable that is named like a type parameter of its function
`redeclared` (V9). Pull request 280, on `main`, added a row to
`CheckerTests.A_const_assert_stops_the_build_where_its_condition_is_false`:

```
void F<T>(T value) { if true { T = 1; const assert(true, "{T}") } }
```

The row expects `const-assert-failed` at line 7.

288's take of `main` had two text conflicts: the row of `redeclared` in
`reference/12-errors.md`, and a comment in `CheckerTests.cs`. Each side changed other words of
those lines, so the words merged with every word kept. The own diff of the merge was the reviewed
one, plus `main`'s words where the blocks met. The park on the pull request said "No code line
differs" (issue 288, comment 6673350639).

The gates on the merge commit, in the image, then failed one test of 5345: on the merged tree the
checker reports `6: redeclared` for `T = 1`, and the row's error never comes (issue 288, comment
6674235408). The test fix, a row that expects `redeclared`, is a change no review read, so the
owner's answer covers it too.

## Why

A text merge joins lines, not meanings. Here one side's rule changed what a test of the other side
reads. The comparison of the own diff is no help either: it compares the change against the reviewed
one, and a test from `main` is not part of that change.

## How to apply

- Run the gates on the merge commit, in the image the `gates` job runs in, before the push. Do it
  also before a take of `main` is described to the owner as one that changes nothing.
  `judgment step`
- A test that fails there, in a file the pull request never changed, is a conflict of meaning.
  Its fix is a new change, and it is named to the owner with the take.
  `judgment step`
- After the push, the `gates` job on the pushed head is red on such a test. The merge tool merges
  no head whose `gates` is red unless `--over-red` names `gates` too, and the standing approval at
  the cap names `review` alone.
  Gate: `compiler-tests`, in the `gates` run of the pushed head.
