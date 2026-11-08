---
name: an-orphan-holds-the-output-of-its-program
description: Postmortem of 2026-11-05 - a corpus case of the HTTP client forks its server into a child and kills it at its end; a mutation made the program raise an overflow before the kill, and the child, taken over by the first process of its system, went on waiting in an `Accept` with no bound while it held the output the program had given it, and `Processes.Capture`, which waited on that output with no limit once the program had ended, hung a run of the suite for 80 minutes; the run now waits on the output within its limit and, on Linux, ends each process that holds the write end of its pipes
metadata:
  type: feedback
---

## What happened

On 2026-11-05 a session proved the cases of the client's call time (W16) by mutation, one change of
`std/` at a time against the case `client-timeout`, which was then on the client's branch. The case
forks its HTTP server into a child of the program, as the client's cases do: the child serves in a loop of `Accept`, and the program, the
client, ends it with `kill(child, 9)` once its calls are done.

One mutation turned the check of the call's time into a sum of its start and its bound. The case holds
a call with the largest `duration`, so the sum overflowed, and the program ended with the error of the
overflow before it reached `kill`. The child lived on:

- the first process of the container took it over: the report of the hang named the child's parent
  as 1, and the command that read it is not recorded. An orphan goes to its nearest ancestor marked as
  a subreaper, by `PR_SET_CHILD_SUBREAPER` of prctl(2), and to the first process where there is none,
  as in that container;
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
  `pipe:[inode]`, a name that both ends of a pipe share. Where the streams have not ended in time, it
  finds each other process that holds the write end of one of those pipes: `/proc/<pid>/fd` names the
  pipe, and the access mode of the flags in `/proc/<pid>/fdinfo` says which end it is. It ends each one
  alone, not the processes it started: one of them that holds a write end is a holder of its own. The
  run then answers -1, and its error stream ends with
  `` `<file>` ended, and an orphan holds its output: pid <n>, ended ``, one such part for each holder,
  with `not ended` for one it did not end.
- A handle that closes while it is read is no writer, and the process's other handles are read. Flags
  that are no octal number, and a `stat` that ends before the start, make no holder.
- In the dev container on 2026-11-05,
  `(readlink /proc/self/fd/1; grep flags /proc/self/fdinfo/1) | (readlink /proc/self/fd/0; grep flags /proc/self/fdinfo/0; cat)`
  printed one `pipe:[...]` name for both ends, with the flags `00` on the read end and `01` on the
  write end.
- A read end alone makes no holder. A child that another thread of the run's own process forked holds
  that process's read ends until its exec, and a scan by the pipe's name alone would end it, and with
  it another run that was starting (the review of pull request 319).
- Before it ends a holder, the run reads the holder's start again, `starttime`, the 22nd field of
  `/proc/<pid>/stat`, in clock ticks since the boot (proc(5)), and passes over a process that took the
  id since. The fields are counted from after the last `)`, since the name in the second field may hold
  spaces and brackets.
- On Windows no list of a process's handles is read, so nothing there ends a process that holds the
  streams. The run ends at its limit all the same and says `no process that holds it was found`. A
  build for Windows says `not supported` to every call on a socket (S11), so no case of a socket forks
  a server there.
- A program that forks a child that waits, a server among them, must end the child at its end and
  bound the child's wait, so that a child whose program an error ended first ends by itself.

The gate is `compiler-tests`, in five tests of `CaptureTests`:
- `A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit`: `/bin/sh` starts
  `sleep 60` in the background and ends at once. The run must end after its limit of 3 seconds and
  within 20, with -1, the child's pid in the text and the child no longer running within 10 seconds of
  the run's end: the run sends the kill and does not wait for the child to end, and on 2026-11-08 a
  run of the `gates` job still showed the child running when the test read its state at once. With the wait on
  the streams unbounded, the test fails at its 20 seconds. On Windows, `cmd` leaves `ping` running with
  its output, and the run must end so with `no process that holds it was found`.
- `Only_a_process_that_holds_the_write_end_of_a_pipe_of_the_run_is_its_holder`: a tree laid out as
  `/proc` is, in which only the processes that hold the write end of the run's pipe are holders, and not
  one that holds its read end, nor the run's own process. A handle whose `fdinfo` is gone is no writer,
  while the same process's write end makes it a holder; flags that are no octal number and a `stat` cut
  after the name make none.
- `A_holder_whose_id_another_process_took_since_is_not_ended`: a running `sleep` found with a start
  that is not its own is not ended, and with its own it is, and the end gives back what it ended.
- `A_holder_is_ended_alone_and_not_the_processes_it_started`: a `/bin/sh` that started a `sleep` that
  holds none of its streams is ended, and the `sleep` still runs.
- `The_error_names_each_holder_and_whether_it_was_ended`: the line names each holder, `ended` or `not
  ended`.

The tree's test, the start's test and the test of the holder alone return at once on Windows, where no
`/proc` is read, and are counted as passed there: they check nothing on Windows.
