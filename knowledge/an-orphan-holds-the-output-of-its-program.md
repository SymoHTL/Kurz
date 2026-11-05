---
name: an-orphan-holds-the-output-of-its-program
description: Postmortem of 2026-11-05 - a corpus case of the HTTP client forks its server into a child and kills it at its end; a mutation made the program raise an overflow before the kill, and the child, taken over by the first process of its system, went on waiting in an `Accept` with no bound while it held the output the program had given it, and `Processes.Capture`, which waited on that output with no limit once the program had ended, hung a run of the suite for 80 minutes; the run now waits on the output within its limit and ends each process that holds it
metadata:
  type: feedback
---

## What happened

On 2026-11-05 a session proved the cases of the client's call time (W16) by mutation, one change of
`std/` at a time against `corpus/http/client-timeout.kz`. The case forks its HTTP server into a child
of the program, as the client's cases do: the child serves in a loop of `Accept`, and the program, the
client, ends it with `kill(child, 9)` once its calls are done.

One mutation turned the check of the call's time into a sum of its start and its bound. The case holds
a call with the largest `duration`, so the sum overflowed, and the program ended with the error of the
overflow before it reached `kill`. The child lived on:

- the first process of the container took it over, as it takes every orphan;
- it waited in `Accept`, which waits without a bound by default (S9);
- it held the standard output and the standard error it had taken from the program at the fork.

`Processes.Capture`, the run of a program that the corpus's runner and the suites use, saw the program
end within its limit of 30 seconds and then waited for the end of the output with no limit. The end of
the program's tree, which the limit runs, never ran, and it would not have reached a process the first
process had taken over. The run of the suite hung for 80 minutes, until the child was killed by hand,
and every other run that waited for the machine waited with it.

## What to do

- The run waits for a program's streams within what remains of its limit, and at least 2 seconds once
  the program has ended.
- On Linux it reads the pipes of the two streams from its own ends of them in `/proc/self/fd`, as
  `pipe:[inode]`. Where the streams have not ended in time, it finds each other process that holds one
  of those pipes in `/proc/<pid>/fd` and ends it with its tree. The run then answers -1, and its error
  stream ends with `` `<file>` ended, and an orphan holds its output: pid <n>, ended ``.
- On Windows no list of a process's handles is read: the wait is bounded the same way, and the text
  says that no process that holds the output was found. A build for Windows says `not supported` to
  the client's cases and to every case of a socket, so none of them forks there.
- A case that forks a server still ends its child, and bounds the child's `Accept` so that a child
  whose program ended first ends by itself.

The gate is `compiler-tests`, in
`CaptureTests.A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit`:
`/bin/sh` starts `sleep 60` in the background and ends at once. The run must end after its limit of
3 seconds and within 20, with -1, the child's pid in the text and the child no longer running. With
the wait on the streams unbounded, the test fails at its 20 seconds.
