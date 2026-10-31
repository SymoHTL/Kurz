---
name: a-bare-name-is-not-on-the-path-on-windows
description: A program started by its bare name on Windows is searched for in the starting program's own directory, the current directory and the system directories before the path, which a test that empties the path to prove "no clang" cannot empty; the compiler resolves each name through the path's directories itself and runs the file by its full path (2026-10-20).
metadata:
  type: reference
---

Seen on 2026-10-20: the command test that sets `PATH` and `ProgramFiles` to an empty directory
and expects the command to find no clang passed on Linux and got the exit code 0 on the Windows
job, with the program built and run, on the head where the search already read `ProgramFiles`
from the environment.

The search handed the bare name `clang.exe` to the operating system. On Windows a process
created by a bare name is looked for, in this order, in the directory the starting program was
loaded from, the current directory, the system directories and only then the directories of the
path, so an emptied path does not empty the search; on Linux the bare name is resolved through
the path alone, which is why the suite was green there.

The compiler resolves each name through the path's directories itself (`Clang.Places` in
`compiler/Kurz.Compiler/Build/Clang.cs`, whose summary says so): a versioned name is a file
found there, the bare name is the first `clang` or `clang.exe` a directory of the path holds,
the Windows installer's place is a file under the `ProgramFiles` directory; each is run by its
full path, and a name no directory holds is named among the places looked at and never run.
`Clang.Find` takes the path and the Program Files directory as arguments, so that a test gives
it an empty directory for both in its own process (`CommandTests`, the test of a version with no
clang found). A child process cannot be given another Program Files directory by its
environment on the Windows runner, which
[a-windows-child-gets-its-own-program-files.md](a-windows-child-gets-its-own-program-files.md)
records.
