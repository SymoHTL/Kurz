---
name: a-test-of-dotnets-own-text-breaks-on-a-runtime-patch
description: On 2026-11-10 the Windows job's install step took up .NET runtime 10.0.15 between two runs on the same runner image, and 10.0.15 writes 2^-958 and 2^-25 with the digits the program writes, where 10.0.14 writes digits that read back as other values; the `FloatingTests` assertion that listed those two doubles as .NET's misreads went red on a head that touched neither. A test that holds what .NET itself writes accepts the text of each runtime it can run on, and holds the program's own text apart.
metadata:
  type: reference
---

Seen on 2026-11-10. A head that changed only the checker of `match` went red twice on the
Windows job, on `FloatingTests.A_double_shows_what_dotnet_writes_and_where_that_does_not_read_back_the_fewest_digits_that_do`:
"Expected [292733975779082240, 4494592428115755008], Actual []", at the assertion of the misreads,
which comes after the loop over every value. The two attempts started at 16:11 and 16:25 UTC. The
run of `main` that started at 16:05 had passed. All three ran on the runner image
windows-2025-vs2026 20261103.298.1.

- The logs differ in the step that installs .NET (`actions/setup-dotnet` with
  `compiler/global.json`, which pins the SDK, 10.0.402).
  - At 16:06 the run of `main` found ".NET Core Runtime with version '10.0.14' is already installed".
  - At 16:11 and at 16:26 the failing attempts downloaded `dotnet-runtime-10.0.15-win-x64.zip`,
    and the step printed "Installed version is 10.0.15".
  - Nothing else in the runs changed. .NET runs an app on the newest patch it finds of the
    release the app targets, so the suite ran on 10.0.15 (inferred: no log line names the
    runtime of the test host).
- The test lists the doubles whose text from .NET does not read back as the value; for those, A4
  writes the fewest digits that do.
  - Under 10.0.15 the list was empty, and the loop before the failing assertion held the program's
    text equal to .NET's for every value. So 10.0.15 writes the program's text for both doubles.
  - The program's text did not change.
- The `gates` job passed on the same head.

What holds now:
- The test accepts each run's misreads as a subset of the two it names.
- It holds the program's text for both doubles by `Assert.Contains`, whatever the runtime writes.
- The runtime the Windows job installs is pinned to no version (HAZARD #15). A test that holds what
  a dependency itself does names the version where that changed, or accepts both.

Gate: `compiler-tests` in `compiler-windows`, the test named above; the unpinned runtime: HAZARD (#15).
