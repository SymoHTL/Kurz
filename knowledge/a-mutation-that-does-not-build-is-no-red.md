---
name: a-mutation-that-does-not-build-is-no-red
description: While the tests of the reading of a type in generic code were proven by mutation for pull request 215 (2026-11-01), a runner read a failed `dotnet test` as the named test turned red. The compiler builds with warnings as errors, so a constant `false` before code (CS0162) and a method that no longer reads `this` (CA1822) do not build, and one red proof, of a constant `if (false)`, was a build error that proved nothing (caught before the pull request was opened). A compiler mutation is red only where the named test is among the failed tests of a run that printed its summary. The converse held of one kind of value alone on 2026-11-19: pull request 588 gave no red proof of a lookup because clang refuses the name its loss leaves, which holds of `Taken`'s made-up names, and the mutation for `CopiedLate`'s slots built and was red (pull request 589); a reason argued from a refusal of the toolchain is read for each kind of value the decision serves
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

## A refusal that holds of one kind of value

Pull request 588 gave no red proof of the lookup of the stand-ins at a region's walk (`LetGo` in
`Emit/Emitter.Lists.cs`), on the reason that its loss leaves a name in the IR that clang does not
build (inferred, not run). The lookup serves two kinds of stand-in: a made-up name for a value a
slot took (`Taken`), which no IR value has, and the slot of a copy of a call of more than one copy
(`CopiedLate`), which is an IR value's name. The reason held of the first kind alone. The mutations
of the change that answered 588's round (pull request 589, 2026-11-19) showed both: with the lookup
gone, clang refused the IR at a `taken#N` name ("expected value token"), the build error 588 named;
with the lookup kept for `Taken`'s names alone, the IR built, the slot's own address was let go of in
place of the copy loaded from it, the case `actors/copy-left-early.kz` stayed green, plain and
fenced, since what it prints names no count of blocks, and the count test, the order test and the
test of a call left before its last copy were red (`Copied 1` where 0, 2 frees where 3, `[4, 2]`
where `[4, 0]`). The round of 589 asked for this section.

## What to do

- A compiler mutation is red only where the named test is among the failed tests of a run that
  printed its summary (`Failed!  - Failed: <n>`). A non-zero exit with no summary, or with an
  `error CS` or `error CA` line before it, is a build error: the mutation is NOT RUN, never red.
- Write a mutation that removes a decision in a form the build takes: a condition the C# compiler
  cannot fold (`s.Line < 0`), not a constant; a changed value, not deleted code that leaves a
  parameter, a variable or `this` unused.
- Rebuild after the file is restored, so that the next mutation does not start from a failed build.
- A reason for no red proof that rests on a refusal of the toolchain, clang's or the C# compiler's,
  holds of the values the refusal meets alone. Where the decision serves more than one kind of value,
  break it for each kind and run each mutation: one that builds is read as any other, and a test that
  names no count can stay green where the count's tests turn red.

## What catches it

- A tool's mutation: `self-tests`. `tools/red_proof.py` counts a recorded mutation as red only
  where the self-test prints `FAIL` with the named case; a mutation that keeps the tool from
  starting prints no such line and is refused with the same words as one that turns another case
  red: "the mutation no longer turns that case red".
- A compiler mutation: nothing in this repository. Its runs are not recorded here, and reading a
  run as red is a `judgment step`, as is the description of the pull request that claims it.
- A reason for no red proof argued from a refusal of the toolchain: nothing; that it holds of each
  kind of value the decision serves is a `judgment step` of the author of the description that gives it.
