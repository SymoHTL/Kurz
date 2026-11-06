---
name: the-run-floor-is-measured-not-merged
description: RunFloor in RunTests.cs is the count of corpus cases built and run on each system, measured with no margin; when a branch and main both changed it, neither side's number is the merged tree's count (pull request 301's take of main, 2026-11-06: 486 on the branch, 461 on main, 495 merged), so the merged number is measured, by setting the system's floor to 999 and reading the count from the message of At_least_the_floor_of_cases_are_built_and_run; Windows is measured only by the compiler-windows job, so until a run there measures it, the higher of the two sides' numbers stands where neither side took a case out of that run, since the merged tree runs the cases of both
metadata:
  type: reference
---

## The number

`RunFloor` in `compiler/Kurz.Compiler.Tests/RunTests.cs` holds one count per system: the corpus
cases built and run. Its comment says that each number is measured on its system with no margin,
so one case that stops being built turns the test red, and a change that adds a case raises the
number. `At_least_the_floor_of_cases_are_built_and_run` fails with
`only <N> cases are built and run, the floor is <floor>`.

## The conflict

A change that adds cases raises the floor, so two changes that each add cases conflict on its
line. Neither side's number is right for the merged tree, which runs the cases of both. In pull
request 301's take of `main` 0fe579a1 (2026-11-06), the sides read `401 : 486` and `408 : 461`
(Windows : Linux), and the merged tree runs 495 cases on Linux.

The two wrong merges fail differently:

- **A number too high** is red at once.
- **A number too low** is green. A case that stops being built then goes unseen while the count
  stays above the floor.

## How to apply

- **Linux.** Set the system's number to 999 and run the one test in `compiler/`, in the image:
  `dotnet test Kurz.Compiler.Tests/Kurz.Compiler.Tests.csproj -c Release -p:RestoreLockedMode=true --disable-build-servers --filter 'FullyQualifiedName~At_least_the_floor_of_cases_are_built_and_run'`.
  Read `<N>` from the failure and set it. `judgment step`
- **Windows.** Only the `compiler-windows` job builds there. Until a run measures it, keep the
  higher of the two sides' numbers where neither side took a case out of the Windows run: the
  merged tree runs the cases of both, so its count is at least each. Pull request 301's take kept
  408. A pushed head with 999 measures it: pull request 351 set 999 in one commit, and in the
  next the count its Windows run printed, 416. `judgment step`
- Gate: `compiler-tests` turns red on a floor above the count, on Linux in `gates` and on Windows
  in `compiler-windows`. A floor below the count is read by nobody: `judgment step`.
