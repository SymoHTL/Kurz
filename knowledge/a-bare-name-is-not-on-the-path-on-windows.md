---
name: a-bare-name-is-not-on-the-path-on-windows
description: A test that empties PATH and ProgramFiles to prove "no clang" found clang on the Windows job (2026-10-20) by one of two causes its run cannot tell apart, the bare name handed to the operating system, which searches the starting program's directory, the current directory and the system directories before the path, or the Program Files directory of a child, which its environment cannot move; the compiler resolves each name to a full path itself, through the path's directories or the Program Files place, and the search takes both as arguments, which removes both causes from the test.
metadata:
  type: reference
---

Seen on 2026-10-20: the command test that sets `PATH` and `ProgramFiles` to an empty directory
and expects the command to find no clang passed on Linux and got the exit code 0 on the Windows
job, with the program built and run, on the head where the search already read `ProgramFiles`
from the environment.

Two causes give that run, and the run does not tell them apart:

- The search handed the bare name `clang.exe` to the operating system. A name with no directory
  in a command line is looked for, in this order, in the directory the starting program was
  loaded from, the current directory of the parent, the 32-bit and the 16-bit system directories,
  the Windows directory and only then the directories of the path (the documentation of
  `CreateProcessW`, parameter `lpCommandLine`, read on 2026-11-01), so an emptied path does not
  empty the search. On Linux the bare name is resolved through the path alone.
- The command ran as a child of the test, and a child on the Windows runner reads the system's
  Program Files directory whatever its parent set, where the installer's clang is (seen on
  2026-10-23:
  [a-windows-child-gets-its-own-program-files.md](a-windows-child-gets-its-own-program-files.md)).

Each finds clang on the Windows job and neither on Linux, which is why the suite was green there.
The change that answered the run removes both. The compiler hands the operating system no bare
name: it resolves each name to a full path itself (`Clang.Places` in
`compiler/Kurz.Compiler/Build/Clang.cs`), a versioned name and the bare name through the path's
directories, the bare name as the first `clang` or `clang.exe` a directory of the path holds, and
the Windows installer's place as a file under the `ProgramFiles` directory; each is run by that
full path, and a name no place holds is named among the places looked at and never run. And
`Clang.Find` takes the path and the Program Files directory as arguments, so that a test gives it
an empty directory for both in its own process, with no child (`CommandTests`, the test of a
version with no clang found).
