---
name: a-test-of-dotnets-own-text-breaks-on-a-runtime-patch
description: On 2026-11-10, the day .NET runtime 10.0.15 was released, the Windows job's install step took it up between two runs on the same runner image, and 10.0.15 writes 2^-958 and 2^-25 with the digits the program writes, where 10.0.14 writes 16 digits that read back as other values; the `FloatingTests` assertion that listed those two doubles as .NET's misreads went red on a head that touched neither. A test that holds what .NET itself writes accepts the text of each runtime it can run on, and holds the program's own text apart.
metadata:
  type: reference
---

Seen on 2026-11-10. A head whose diff touched only the checker of `match` and its tests went red twice on the
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
  - .NET's release list gives 10.0.15 the release date 2026-11-10.
  - .NET runs an app on the newest patch it finds of the release the app targets. A program built
    for `net10.0`, run where 10.0.14 and 10.0.15 were both installed, named its runtime as
    ".NET 10.0.15". So the suite ran on 10.0.15 (inferred for Windows: no log line names the
    runtime of the test host there).
- A program that prints .NET's invariant text of both doubles, and whether it reads back, gave on
  each runtime, which it named itself. It ran on Linux x64: on 10.0.13 in a development image, on
  the image `mcr.microsoft.com/dotnet/runtime:10.0.14`, and on .NET's archive of 10.0.15 for Linux
  x64, whose SHA-512 matched the release list. It did not run on Windows.
  - 10.0.13 and 10.0.14: `4.104536801298376E-289` and `2.980232238769531E-08`, neither reads back;
  - 10.0.15: `4.1045368012983762E-289` and `2.9802322387695312E-08`, both read back. These are the
    texts the test finds in the program's output, A4's fewest digits that read back.
- So under 10.0.15 the list of misreads is empty, and the program's text did not change.
- The `gates` job passed on the same head.

What holds since 2026-11-10:
- The test accepts each run's misreads as a subset of the two it names.
- It holds the program's text for both doubles by `Assert.Contains`, whatever the runtime writes.
- The runtime the Windows job installs is pinned to no version: `CLAUDE.md` lists it with the tools
  of HAZARD (#15), and says why it stays open: `kurz` is a .NET 10 command that does not carry its
  runtime, so a machine runs it on the newest patch it has, and the job installs the newest patch
  too.
- A test that holds what a dependency itself does names the version where that changed, or
  accepts both.

Gate: `compiler-tests` in `compiler-windows`, the test named above; the unpinned runtime: HAZARD (#15).
