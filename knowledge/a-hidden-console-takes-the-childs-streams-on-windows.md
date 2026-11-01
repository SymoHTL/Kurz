---
name: a-hidden-console-takes-the-childs-streams-on-windows
description: On Windows a child process started with CreateNoWindow does not share the parent's console, and when none of its streams is redirected its standard handles are not the parent's either, so a program run that way prints into no stream its caller reads; the five command tests that run a program were empty on the Windows job (2026-10-20), and where the output went, a hidden console of its own by this entry's reading, no probe showed. The command starts the built program without the flag.
metadata:
  type: reference
---

Seen on 2026-10-20: the command tests that run a built program through `kurz run` and `kurz exec`
passed on Linux and failed on the Windows job with the right exit code and empty streams, output
and error alike, while the tests that run the same executables directly, with both streams
captured, passed on both.

The command started every process through one start-up record that set `CreateNoWindow`, for
clang as for the program. On Windows that flag is `CREATE_NO_WINDOW`, which the documentation of
the process creation flags (read on 2026-11-01) describes as "a console application that is being
run without a console window. Therefore, the console handle for the application is not set": the
child does not share the parent's console. When the parent redirects none of the child's streams,
the child's standard handles are not the parent's either, and the program's `write` calls reached
no stream the test read. The documentation does not say where they went: that the child got a
hidden console of its own is the reading of this entry, not a probe. What the runs show is the
flag's part: the five tests were empty with it and pass on the Windows job since the command
starts the program without it. Linux has no console in this sense and ignores the flag, which is
why the suite was green there.

A process that is meant to write on the caller's streams is started without the flag, so that it
shares the caller's console and inherits the caller's standard handles, pipes included
(`Processes.Inheriting` in `compiler/Kurz.Compiler/Build/Processes.cs`, which `kurz run` and
`kurz exec` start the program through). A process whose streams are captured keeps the flag
(`Processes.Capture` in the same file, through which the command runs clang and its `--version`,
and the suite runs the built programs and the command itself, in `RunTests`, `CommandTests` and
`PlaceTests`): its handles are set by the redirection, whatever console it gets. The Windows job runs the command tests and is the check; a Linux run
cannot see the defect.
