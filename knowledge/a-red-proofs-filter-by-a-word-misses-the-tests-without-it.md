---
name: a-red-proofs-filter-by-a-word-misses-the-tests-without-it
description: the first red proof of pull request 565's atomic types (2026-11-17) ran each mutation against the focused filter `FullyQualifiedName~tomic|DisplayName~memory/atomic|...`, and a mutation that the test of `AtomicPtr<T>`'s pointee alone held stayed GREEN, since that test had no "atomic" in its name; a red proof's focused filter by a word of the test names reaches only the tests that hold the word, so it selects the tests by class, or reads the count of tests the run names against the tests that hold the decision
metadata:
  type: feedback
---

## What happened

In pull request 565, on 2026-11-17, the red proofs of the atomic types (R19) ran each mutation under
the mutation runner against one focused filter,
`FullyQualifiedName~tomic|DisplayName~memory/atomic|FullyQualifiedName~RunTests.At_least_the_floor`.
The first run, on `49c7f833`, left two mutations GREEN. One of them was held by the test of
`AtomicPtr<T>`'s pointee alone, whose name held no "atomic", so the filter did not reach it, and
every test the filter reached passed. The test was renamed in `e2238623`, and the second run saw the
mutation FAIL. The pull request's description reported it, and the review of its round at the cap
found, as the reading of that round on issue 39 (comment 6957856856) confirmed, that no file recorded
it.

The filter read as a set of the decision's tests, and it is a set of names. A test of the same
decision named after another word, a test written later, or a test of another class that holds the
decision is outside it, and a mutation it misses reads GREEN, which says that no test holds the
decision when one does.

## How to apply

- Select a red proof's tests by class, `FullyQualifiedName~CheckerTests|FullyQualifiedName~EmitterTests`,
  with each corpus case by its path in the display name
  ([a corpus case is filtered by its display name](a-corpus-case-is-filtered-by-its-display-name.md)),
  and not by a word the tests' names are meant to share. `judgment step`
- Read the count of tests the run names against the tests that hold the decision, and read a GREEN
  against them before it is reported as a decision no test holds. `judgment step`
- The mutation runner reads the summary line of the run it is given: it cannot know which tests the
  filter should have reached, and nothing compares a filter with the tests of a decision.
  `judgment step`
