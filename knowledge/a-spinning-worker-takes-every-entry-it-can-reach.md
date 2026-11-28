---
name: a-spinning-worker-takes-every-entry-it-can-reach
description: On the worker threads of section 6 (U35), a worker that spins before it parks takes, at each of its turns, every entry another worker can take, so that an entry of an actor its holder runs is taken and dropped, and an actor a call woke into its caller's slot moves to another worker at every call; pull request 670 measured both before its two fixes, and a test whose expectation follows one thread's order fails on worker threads with no defect behind it
metadata:
  type: feedback
---

A worker that parks once it finds nothing looks at the queues once. A worker that spins before it
parks (the owner, 2026-11-26, #519, comment 7204486070, Q2, "Spin before park") looks at them at
each of its turns for the bound of the spin, so that it takes every entry another worker can take,
the moment it stands there. Two forms that one look never showed cost the build of pull request 670
before its fixes (2026-11-27, comment 7210832380, `KURZ_THREADS=4`, the least of 10 runs, the machine loaded
by other work):

- an entry of an actor that a thread holds, put where every worker takes from: a send to an actor that
  a worker runs put one, a worker that spins took it and dropped it (`Claimed`), and the send after it
  put another; the 300000 sends to one actor took 107.9 and 122.6 ms against 83.3 and 86.8 with no
  spin. A wake of a held actor puts no entry in and leaves it to its holder (`LeftToHolder`);
- the slot of the actor woken last of another worker, which a spinning worker took at each of its
  turns, so that the actor a call woke into its caller's slot moved to another worker at every call:
  the 100000 calls of one actor to another inside one message took 57.8 and 74.8 ms against 45.1 and
  54.0 with no spin. A worker that saw an actor in another worker's slot spins 3 µs before it takes
  it (`SlotWait` in `Stolen`).

Measured each alone on 2026-11-27 (20 runs in each of two orders, the machine loaded by other work,
and the four actors' counts, which send four messages, again in six orders of up to 100 runs, alike
in every variant within the noise, issue 39, comment 7226765242), each fix made its row faster
and no row slower beyond the noise, the bar the owner set for keeping
it (issue 39, comment 7221780113); the numbers stand in section 6 of the design record. A model of
the wake (`ModelTests`) finds the lost wake of a form that turns the mark whether the actor is held
or not (`Break.LeftUnheld`).

A third form is a test's, not the runtime's: an expectation that follows the order of one thread, the
actors that are ready run first in, first out (§6, Q5 of comment 6847932993), or a count of heaps,
where each message on worker threads has a heap of its own (U35). On worker threads such a test fails
with no defect behind it: issue 673 holds the run of `ActorTests` on the cores, 47 of 169 failed in one
run of 2026-11-27, among them orders and counts of crash lines and of printed lines and counts of heaps, beside
failures that need a reading each. A test that follows one thread's order or counts heaps sets
`KURZ_THREADS=1` itself (the owner, 2026-11-27, #519, comment 7215317481, Q1 of comment 7207859311,
"Only the ones that need it"), and says why. `ActorTests` departs from that answer and sets it on every program, since
issue 673 holds the class's run on the cores (the owner, 2026-11-28, issue 39, comment 7236862019, "Keep
the pin, cite #673 (Recommended)", to question 3 of comment 7228411861).

Before an entry of an actor goes where every worker takes from, or a worker takes from another
worker's slot, read what a worker that spins does with it at each of its turns; a race of the
spin shows in the time of a row, not in a fault, so the rows of messages and of calls between actors
are measured on four worker threads beside one thread.
