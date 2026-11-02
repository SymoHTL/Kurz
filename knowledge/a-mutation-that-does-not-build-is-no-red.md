---
name: a-mutation-that-does-not-build-is-no-red
description: While the tests of T33's build were proven by mutation (pull request 215, 2026-11-01), a runner read a failed `dotnet test` as the named test turned red; two mutations did not build, a constant `false` before code (CS0162) and a method that no longer read `this` (CA1822), since the compiler builds with warnings as errors, so no test ran and one red proof proved nothing. A compiler mutation is red only where the named test is among the failed tests of a run that printed its summary
metadata:
  type: feedback
---

## What happened

Pull request 215 proved each decision of T33's build by breaking it in `compiler/`, running the
suites that need no clang, and expecting the named test to fail (`CLAUDE.md`, "Tests: every
decision is proven"). The runner was a script outside this repository: it ran
`dotnet test ... --filter <the suites>` and wrote `RED` when the run exited non-zero.

`compiler/Directory.Build.props` builds with `TreatWarningsAsErrors` and `AnalysisLevel`
`latest-recommended`, so a mutation that only adds a warning does not build. Two did not:

- the bound on a chain of copies, broken with `if (false)` before the code that refuses a deeper
  chain: `error CS0162: Unreachable code detected`. The runner wrote
  `RED    the depth of a chain -> A_chain_of_copies_deeper_than_the_build_reads`, and no test had run;
- a second mutation, in a later run of the same runner, which took out a method's one read of `this`:
  `error CA1822: Member '...' does not access instance data and can be marked as static`.

The first was in the description of pull request 215 as a red proof. The runner was changed to
write `NOT RUN (no test ran; a build error?)` with the build's error where the run printed no test
summary, every mutation was run again with it, and the two were written in forms that build: the
bound one higher and one lower, a condition the analyzer cannot fold. The test of the bound was
then seen red for what it holds, at 64 copies read and 65 refused (commit f5f654b). The review
round of pull request 215 (2026-11-02) asked for this entry.

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
