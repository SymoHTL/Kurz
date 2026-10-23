---
name: a-windows-child-gets-its-own-program-files
description: on the Windows runner three child processes (the command kurz, cmd.exe and pwsh) read ProgramFiles as the system's Program Files folder although their parent had set the variable to another directory, while the PATH their parent set reached them; a test cannot move the place the clang search reads through ProgramFiles by the environment of a process it starts (2026-10-23)
metadata:
  type: reference
---

## What happened

On 2026-10-23 a test of `CommandTests` started the command `kurz --version` as a process with
`PATH` and `ProgramFiles` both set to an empty directory, so that the clang search would find
nothing. On the Windows runner the command found `LLVM\bin\clang.exe` under the system's Program
Files folder, the LLVM the job had installed, and exited with 0 (the `compiler-windows` run of pull
request 54's head 36dc840).

A probe on the same runner image (`windows-2025-vs2026`, version 20261021.278.1; the
`compiler-windows` run 39950672697 of pull request 55, the test `WindowsEnvironmentProbe` of its
first commit) started `cmd.exe /c set` and `pwsh` from the test process. Both variables were set
to an empty directory through `ProcessStartInfo.Environment`, as `CommandTests` had set them for
the command. Both children read:

- `Path` as the parent set it (pwsh puts its own directory ahead of it);
- `ProgramFiles`, `ProgramW6432` and `CommonProgramFiles` as the system's folders, Program Files
  and its Common Files, not as the parent set them.

The test process and pwsh said they were 64-bit processes. Three children read `ProgramFiles` the
same way: a .NET program (the command), a .NET shell (pwsh) and a native one (cmd.exe). What sets
the variable in the child, Windows or each program, was not probed; the observation is the same
for all three.

## What to do

- A test of the clang search that needs `ProgramFiles` elsewhere gives the search its directory
  in the test's own process (`Clang.Find(path, programFiles, windows, ask)`), as `ClangTests` and
  the empty search of `CommandTests` do. The children on the runner did not take one.
- The search reads `ProgramFiles` as the record says. Where a child reads that variable as the
  system's folder, as the three above did, the installer's directory is the one under that folder.
- `PATH` reaches a child as set, so a process test can empty the path. On Windows it cannot empty
  the search: the installer's directory stays.

The gate is the Windows job: `compiler-tests` there is red when a test expects a child to read a
`ProgramFiles` it set.
