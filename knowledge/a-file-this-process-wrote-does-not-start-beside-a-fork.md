---
name: a-file-this-process-wrote-does-not-start-beside-a-fork
description: on Linux a file that the test host wrote itself and then runs does not start now and then ("Text file busy", ETXTBSY) while other threads start processes, because a child forked while the host held the file open for writing holds that descriptor up to its exec; Processes.Capture answers such a refusal as a run that did not start, which a trace test reads as an empty trace (probed 2026-11-01, .NET SDK 10.0.402)
metadata:
  type: reference
---

## What happened

`TraceTests` ran variants of a build that it wrote itself: a copy by `File.Copy`, and a section
header changed through `File.WriteAllBytes`. The other test classes built and ran programs on
their own threads at the same time. Now and then a variant did not start:

- the system refused it with `ETXTBSY`;
- `Processes.Capture` answered with the code -1 and "did not start: ... Text file busy";
- the test's split of the error stream found no frame in that text, so the trace was empty.

This is item 4 of #156, seen in the CI runs 41246453494 and 41325216976. Item 5, a "Text file busy"
in a local run of `TraceTests` on 2026-10-30, has the same cause. That run recorded no file name.
Linux refuses a file with `ETXTBSY` only while a description of it is open for writing, and the
trace tests of that day (`main` at 7b398b2) ran two kinds of file:

- the builds, which clang wrote in a process of its own, ended before the run started;
- the two variants, which the test host wrote itself with `File.Copy` and `File.WriteAllBytes`.

Outside `TraceTests`, two tests of `CommandTests` run a file the test host wrote, then and now:

- an output path written before a build writes over it, which runs seconds after the host's write;
- an empty file, which no system starts, and which the test expects not to start.

Linux refuses to run a file while any open file description of it is open for writing. .NET opens
its files with `O_CLOEXEC`, but a child that another thread forks in that time holds a copy of
every descriptor of the process, from its fork to its exec. The host closes its own descriptor;
the child's copy still holds the write.

A probe of 2026-11-01 ran in a Linux container with .NET SDK 10.0.402, for 20 seconds per row.
It wrote a copy of `true` with `File.Copy` and `File.WriteAllBytes` and ran it, in a loop:

    four threads starting `true` beside it:  3625 of 15361 runs did not start, Text file busy
    no such threads:                         0 of 25100
    the copy written by `cp` instead:        0 of 6324

## What to do

- A test runs no file that it wrote in its own process. It writes the bytes into a data file and
  has `cp` write the file it runs, as `TraceTests.Variant` does. The descriptor open for writing is
  then `cp`'s, and no child of the test host holds it.
- A run that did not start says so in its error text. An assertion on the trace alone shows an
  empty list there, so a message that carries the run's error names the cause.

The gate is `compiler-tests` for `TraceTests`, in two cases:

- a child opens the file of a build for writing itself and keeps it open, and the run is refused
  with an empty trace; with the child ended, the file starts and writes its trace;
- a hundred variants run while two threads start processes, and every one starts. With the
  variant written in the test host instead, 10 to 19 of the hundred did not start, in each of five
  runs (2026-11-01).

That no other test runs a file it wrote itself is a `judgment step`, read in review.
