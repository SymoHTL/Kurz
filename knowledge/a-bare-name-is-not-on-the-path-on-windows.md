---
name: a-bare-name-is-not-on-the-path-on-windows
description: A program started by its bare name on Windows is searched for in the starting program's own directory, the current directory and the system directories before the path, which a test that empties the path to prove "no clang" cannot empty; the compiler resolves each name through the path's directories itself and runs the file by its full path; and a command started with `ProgramFiles` emptied still found a clang on the Windows job, and none with `ProgramW6432` and `ProgramFiles(x86)` emptied beside it; where it found the clang was not read.
metadata:
  type: reference
---

Seen on 2026-10-20: the command test that sets `PATH` and `ProgramFiles` to an empty directory
and expects `kurz: clang is not there` passed on Linux and got the exit code 0 on the Windows
job, with the program built and run, on the head where the search already read `ProgramFiles`
from the environment.

The search handed the bare name `clang.exe` to the operating system. On Windows a process
created by a bare name is looked for, in this order, in the directory the starting program was
loaded from, the current directory, the system directories and only then the directories of the
path, so an emptied path does not empty the search; on Linux the bare name is resolved through
the path alone, which is why the suite was green there. On the runner image the command found a
clang with the path emptied on the head after this change as well, while the same search over the
two values in the suite's own process found none and a captured `cmd.exe` printed the path it was
given: `BuildTests.cs` holds those two apart, one test each. The head that emptied `ProgramW6432`
and `ProgramFiles(x86)` beside `ProgramFiles` in the command test got `kurz: clang is not there`
on the job. Where the command found a clang with `ProgramFiles` alone emptied, and why those two
matter, was not read; the command test empties the two beside `ProgramFiles` for the child it
starts.

The compiler resolves each name through the path's directories itself (`Clang.Candidates` in
`compiler/Kurz.Compiler/Build/Clang.cs`): a versioned name is a file found there, the bare name
is the first `clang` or `clang.exe` a directory of the path holds, the Windows installer's place
is a file under the `ProgramFiles` directory; each is run by its full path, and a name no
directory holds is named among the places looked at and never run. The search reads only the
path's directories and the Program Files directory it is given, so that a test, and a person, can
move what it is given.
