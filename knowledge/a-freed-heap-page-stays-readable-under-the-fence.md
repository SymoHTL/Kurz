---
name: a-freed-heap-page-stays-readable-under-the-fence
description: The fenced allocator of ListTests gives every freed block back to the system, but std's heaps give their pages to a pool, so a read of a freed heap's page passes under the fence; on 2026-11-17 the mutation that left a child's supervisor word pointing at a supervisor whose end had freed its record stayed green under ListTests.Fenced, until the test wrote that record's heap word before the free; a fenced program shows no read of an actor's record, which stands in its heap's first page
metadata:
  type: feedback
---

The fence (`ListTests.Fence`, `ListTests.Fenced`) turns a block of the allocator into pages of its own
and gives them back to the system when the block is freed, so that a read or a write of a freed block
ends the run with the exit code 139. A heap of `std/` (section 3) is something else: `HeapFree` puts each
page of the heap into the allocator's pool (`Pooled` in `std/std.kz`), where it stays mapped, with its
bytes as they were but for the pool's link in its first word, until `HeapNew` takes it again. The fence
does not reach those pages.

An actor's record and its state stand in the first page of the actor's first heap (`ActorNew`). Since
the table of slots and generations of section 6 (2026-11-16, comment 6919351399) an actor that ends frees
that heap at once, so a record can be read after its free. On 2026-11-17 the mutation that kept a child's
supervisor word, `-72`, when its supervisor ended while the child waited in its current message
(`EndChildren`) stayed green: the child's crash after the wait reached `Fell`, which read the freed
supervisor's heap word, `-64`, and copied the crash's text into that heap, and the run printed what the
test asked, under the fence. The test
(`ActorTests.A_child_whose_supervisor_ended_while_it_waited_takes_its_own_crash_and_writes_its_log_line`)
now edits the runtime so that `EndActor` writes 16 into the heap word of a record it frees at once,
before the free; the same mutation then ends the run before the child's log line, and the test is red.

The other readers of a freed record were held by what they print instead: a record freed while the run
queue holds it is freed again when the scheduler takes it off, which a count of the heaps shows
(`ActorTests.An_ended_actor_frees_its_heap_at_once_or_where_it_stands_in_the_run_queue_when_the_scheduler_takes_it_off`),
and a message for an actor that ended is built in its caller's heap, never in the freed page
(`ListTests`, the line `actor-end-wait-fenced`).

**How to apply:** a test that claims "no freed block is read" of a record or of anything else that lives
in a heap's pages, not in a block of its own, proves nothing by the fence alone. Write a value the read
cannot survive into the word it would read, in the test's own edit of the runtime, before the free, and
see the test go red under a mutation that reads it.
