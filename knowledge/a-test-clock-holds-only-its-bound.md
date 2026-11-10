---
name: a-test-clock-holds-only-its-bound
description: On 2026-11-09 three tests of `HttpServerTests` missed their bounds of time once each on loaded machines, and passed again on the next run. Two clocks started before the program built its body of 32 MiB, and two read the time after an `await`, which goes on only once a thread of the pool takes it. With every thread of the pool held, the end of a run's task came 13.6 s after a connection that ended at 0.33 s. A test's clock starts at what its bound counts from, after the program's setup, and every wait and read inside it blocks the test's own thread; a program's end is read on a connection that the close of its listener resets on Linux
metadata:
  type: feedback
---

## What happened

On 2026-11-09 three tests of `compiler/Kurz.Compiler.Tests/HttpServerTests.cs` failed once each, and
each passed on its next run:

- `A_write_of_a_response_is_held_to_its_bound_across_the_calls_of_the_system_it_makes` printed
  `served` where `TimedOut` was expected, in the image, at a load of about 7 on 6 CPUs. Its 3 seconds
  of slow reads counted from before the program started, and the program builds its body of 32 MiB
  after its accept. The body's write has 2.1 seconds, and the slow reads take less than 4 MB, so the
  body could go out whole only where its bound ran out after the client went to full speed: the
  write began more than 0.9 seconds after the clock.
- `A_read_of_a_body_sets_what_remains_of_its_time_before_each_refill` read 1.717 s against its 1.7 s
  in CI. It waited its 800 ms with `await Task.Delay` and read the answer with `await ReadAsync`.
- `A_bound_of_time_ends_its_connection_within_its_length`, row `first`, read 3.03 s against its
  2.5 s in the image. Its clock started at a connection that Linux completes before any
  accept, while the program still built its body of 32 MiB, and stopped after `await run`.

A probe of the same day held every thread of the pool with 40 work items that sleep 3 seconds each,
once each clock had started:

- the run's task of the row `first` ended 13.6 s after the clock, while the client saw the
  connection end at 0.33 s: `Processes.Capture` reads the program's output with `ReadToEndAsync`,
  whose completions run on the pool;
- the refill test's `await Task.Delay(800)` went on at 1043 ms, after the server's answer at one
  second. A second byte sent that late leaves a refill given the whole grace again answering at one
  second too, so the test could not have failed on its defect.

Under the suite alone, and under the suite with six more busy processes, the same spans read 0.33
to 0.48 s, 1.003 to 1.012 s, and a first byte 63 to 232 ms after the clock. None of the three
failures came back, so which part took the time in each failure is inferred.

## What to do

- A test's clock starts at the event its bound counts from: the first byte of a response whose write
  is bounded, or the client's last byte to a program that has nothing left to do but accept. A
  program writes its port only once its setup is done.
- Every wait and read inside a timed span blocks the test's own thread (`Thread.Sleep`, a `Read`
  under `ReadTimeout`), never an `await`. A wait that paces the client blocks too, since it decides
  whether the test can fail on its defect.
- The end of a run's task, `await run` or a continuation on it, is no measure of a program's end. A
  program's end is read on a second connection to its listener that it never accepts: on Linux, the
  one system these tests run on, the close of the listener resets it, and a blocking read on it
  returns then; every row of `A_bound_of_time_ends_its_connection_within_its_length` read that reset
  306 ms or more after its clock (pull request 424, 2026-11-09), where its lower bound of 250 ms
  would refuse an early one.

The gate is `compiler-tests` for the three tests. That a new test of a time keeps its setup and every
`await` out of its span is a `judgment step` for its author.
