---
name: a-block-another-thread-frees-is-read-under-what-keeps-it
description: On the worker threads of section 6 (U35), a block that another thread can free, an ended actor's first page, a message's heap of its own, the block of a call's message, is read only under what keeps it, the thread's epoch stored with its fence, the lock of the process or the hold of its actor; the build of pull request 666 met, on 2026-11-27, three reads outside all three and one race in a worker's count of itself as parked, each of which one thread never shows, and the runner and the suite on one thread pass over every one of them
metadata:
  type: feedback
---

On one thread nothing frees a block while another part of the runtime reads it: the one thread
runs the read and the free in some order, and the order is the program's. With worker threads
(U35, section 6) a block can be freed by another thread between two reads of one function: the
first page of an ended actor, once every thread passed a turn (T16 2), a message's heap of its own,
which every message built on worker threads has (U35, assumed in section 6), freed when the message
ends (the owner, comment 7192965131, "Free when message ends"), and the block of a call's message, which the
callee's thread frees once it answered. A read of such a block is safe only under one of three
things that keep it:

- the thread's epoch, stored at the start of its turn and followed by a fence of sequential
  consistency, so that no read of the turn comes before the store a freeing thread reads
  (`WorkerTurn`, `Reclaimed`), or the main thread's pin around a `send` or a call of the top-level
  code (`Pinned`, `PinnedHeld`);
- the lock of the process, which waits for the turn of every other worker (`LoopHeld`, `WorldTaken`);
- the hold of the actor that owns the block, taken by the thread that runs its crash, its end or
  its restart (`HeldAlone`, `EndChildren`).

The build of pull request 666 met three reads outside all three and one race beside them, which is
no read of a freed block, each of which passes every run on one thread; its description gives the
runs of 2026-11-27, of builds with lines that print at a fault, beside a run
of the suite, with the fenced allocator:

- the wait's read of the chain of its call's message, a block the callee's worker had freed: a loop
  program on four worker threads faulted in 614 of 1000 runs, and `crash-restarts-fresh.kz` on six in
  23 of 3000, the first fault of each at that read; the main thread reads that block only under the
  lock of the process, where its wait found nothing left to run (`Answer`);
- the race: a worker that counted itself parked with nothing pending before its last look at its queues, so
  that the main thread raised `deadlock` while that worker went on with an actor: one false
  `deadlock` of the top-level code in 700 runs of the loop program's build without `--release`,
  which that count explains (read in the code, no probe shows the order); it counts itself only after
  that look (`WorkerPark`);
- the confirmation of a loop of layer 3 that read the reply of a wait whose callee had answered on
  another thread: it confirms under the lock of the process, and only while the wait's own reply
  has no answer (`Kept`, `Stuck`);
- the epoch stored with no fence after it, so that on a CPU that moves a load before a store a read
  of the turn came before the store the freeing thread read: the store is followed by a fence of
  sequential consistency, a reading of the code that no run shows.

With every fix, on 2026-11-27, the loop program on four worker threads ended with no fault and no `deadlock` of the
top-level code in 1000 runs, and `crash-restarts-fresh.kz` on six in 3000.

A fifth shape stands in the walk of layer 3, which reads with no lock: each word of an activation's
wait is read once, the actor the reply reaches before the reply's word (`Onward`), so that a reply
its waiter's answer let go of meanwhile is read as set; the two words of the wait, read one after the
other, can still belong to two waits of an activation that moved between them (`compiler/README.md`,
the paragraph of the worker threads, what is not built).

A read of a block another thread frees names, in its comment, which of the three keeps it. The
suite holds the shapes it can run: `ThreadsTests` on four worker threads, and `RunTests`' crash
cases on four threads (`CrashPathCases`); a race those runs do not meet is read in the code alone,
and no ThreadSanitizer job runs (section 6, assumed there).
