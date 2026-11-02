---
name: a-mutation-that-does-not-build-is-no-red
description: While the tests of the reading of a type in generic code were proven by mutation for pull request 215 (2026-11-01), a runner read a failed `dotnet test` as the named test turned red. The compiler builds with warnings as errors, so a constant `false` before code (CS0162) and a method that no longer reads `this` (CA1822) do not build, and one red proof, of a constant `if (false)`, was a build error that proved nothing (caught before the pull request was opened). A compiler mutation is red only where the named test is among the failed tests of a run that printed its summary
metadata:
  type: feedback
---

## What happened

Pull request 215 built the reading of a type in generic code (T33: `T.Kind`, and a copy of a
generic function for each type it is called with). Each decision was proven by breaking it in
`compiler/`, running the suites that need no clang, and expecting the named test to fail
(`CLAUDE.md`, "Tests: every decision is proven"). The runner was a script outside this
repository: it ran `dotnet test ... --filter <the suites>` and wrote `RED` when the run exited
non-zero.

`compiler/Directory.Build.props` builds with `TreatWarningsAsErrors` and `AnalysisLevel`
`latest-recommended`, so a mutation that only adds a warning does not build:

- The bound on a chain of copies was broken with a constant `if (false)` before the code that
  refuses a deeper chain, and the runner wrote
  `RED    the depth of a chain -> A_chain_of_copies_deeper_than_the_build_reads`.
- The runner was then changed to write `NOT RUN (no test ran; a build error?)`, with the build's
  error, where a run printed no test summary, and every mutation was run again with it. Two
  mutations of a later run stopped so: one with a constant `false` before code,
  `error CS0162: Unreachable code detected`, and one that took out a method's one read of `this`,
  `error CA1822: Member '...' does not access instance data and can be marked as static`.

A constant `if (false)` before code is the shape CS0162 refuses, so the bound's `RED` was a build
error and proved nothing. It was caught before the pull request was opened: the bound was broken
again in forms that build, one copy higher and one lower, and both turned the test of commit
f5f654b red (`A_chain_of_as_many_copies_as_the_build_reads_is_read_and_one_more_is_not_supported`,
which holds 64 copies read and 65 refused). The review round of pull request 215 (2026-11-02)
asked for this entry.

## What to do

- A compiler mutation is red only where the named test is among the failed tests of a run that
  printed its summary (`Failed!  - Failed: <n>`). A non-zero exit with no summary, or with an
  `error CS` or `error CA` line before it, is a build error: the mutation is NOT RUN, never red.
- Write a mutation that removes a decision in a form the build takes: a condition the analyzer
  cannot fold (`s.Line < 0`), not a constant; a changed value, not deleted code that leaves a
  parameter, a variable or `this` unused.
- Rebuild after the file is restored, so that the next mutation does not start from a failed build.

## What catches it

- A tool's mutation: `self-tests`. `tools/red_proof.py` counts a recorded mutation as red only
  where the self-test prints `FAIL` with the named case; a mutation that keeps the tool from
  starting prints no such line and is refused as one that turns another case red is: "the mutation
  no longer turns that case red".
- A compiler mutation: nothing in this repository. Its runs are not recorded here, and reading a
  run as red is a `judgment step`, as the description of the pull request that claims it.
