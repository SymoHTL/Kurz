---
name: a-hidden-console-takes-the-childs-streams-on-windows
description: On Windows a child process started with CreateNoWindow gets a hidden console of its own, and when none of its streams is redirected its standard handles are that console's and not the parent's, so a program run that way prints into nothing; the five command tests that run a program were empty on the Windows job (2026-10-20). The command starts the built program without the flag.
metadata:
  type: reference
---

Seen on 2026-10-20: the command tests that run a built program through `kurz run` and `kurz exec`
passed on Linux and failed on the Windows job with the right exit code and empty streams, output
and error alike, while the tests that run the same executables directly, with both streams
captured, passed on both.

The command started every process through one start-up record that set `CreateNoWindow`, for
clang as for the program. On Windows that flag is `CREATE_NO_WINDOW`: the child gets a console of
its own, hidden, and when the parent redirects none of the child's streams the child's standard
handles are the handles of that new console, not the parent's. The program's `write` calls
reached a console nobody read. Linux has no console in this sense and ignores the flag, which is
why the suite was green there.

A process that is meant to write on the caller's streams is started without the flag, so that it
shares the caller's console and inherits the caller's standard handles, pipes included
(`Processes.Inherit` in `compiler/Kurz.Compiler/Build/Processes.cs`). A process whose streams
are captured keeps the flag: its handles are set by the redirection, whatever console it gets.
The Windows job runs the command tests and is the check; a Linux run cannot see the defect.
