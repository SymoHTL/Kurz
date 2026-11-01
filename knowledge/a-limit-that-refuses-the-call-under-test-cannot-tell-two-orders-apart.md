---
name: a-limit-that-refuses-the-call-under-test-cannot-tell-two-orders-apart
description: A test of the order in which `Reallocate` takes a kept block and the target's `Resize` (pull request 158) ran under a limit on the bytes the system gives; its first form refused `Resize` past the limit too, and a refused `Resize` sends `Reallocate` to the allocation entry, which takes the kept block, so the order that asks `Resize` first passed under it as well (caught before the push, 2026-11-01). A limit counts the call whose place in the order is under test and refuses only the calls after it
metadata:
  type: feedback
---

## What happened

Pull request 158 made a block of more than 262144 bytes that outgrows its room take a kept block
before the target's `Resize` (the owner, 2026-11-01, "Kept block first", record section 2). The
test, `ListTests.A_block_of_more_than_262144_bytes_that_outgrows_its_room_takes_a_kept_block_before_the_targets_Resize`,
makes and drops a big list for 20 rounds under a runtime that counts the bytes the system gives,
with nothing counted back, and refuses a request past a limit (`Taking`): the kept-first order stays
within the limit, and an order that asks `Resize` first takes the system's bytes again each round
and goes past it.

The first form of `Taking` refused `Resize` past the limit as well as `Memory`. Under it the
mutation that asks `Resize` first passed. A refused `Resize` gives null, and `Reallocate` then goes
to the allocation entry, which takes the kept block before all else (`Reused`): the order under test reached
the same kept block whichever of the two came first, so the limit could not tell them apart. The
mutation was seen to pass before the push, and `Taking` was fixed then: it counts the bytes a
`Resize` grows by and refuses only `Memory`, the call that neither order puts before the kept block.

## What to do

- Before a test of the order of two calls runs under a limit or a fault, ask what each order does
  when the limit refuses each call. A refused call that falls through to the other one makes the two
  orders end in the same place, and the test passes for both.
- Count the call whose place in the order is under test, and refuse only what comes after both.
- The mutation that swaps the order is run under the test and seen red, as every decision is
  (`CLAUDE.md`, "Tests: every decision is proven"); here it was the run that found the defect.
