---
name: a-test-of-saturation-takes-the-largest-value
description: A sum that saturates at the largest long is tested with the largest value the type holds, since a large value that passes the bound only after the monotonic clock has run long enough passes the test on a machine that started recently, with the saturation taken out; the bound of 106751 days in connect-timeout.kz passes it only after about 23.8 hours, and the mutation stayed green on a machine that had run 19 (pull request 203, 2026-11-01)
metadata:
  type: reference
---

## What happened

Pull request 203 gave `Connect` a deadline, the monotonic clock's time plus the bound, which the
runtime's `Deadline` saturates at the largest `long`, so that a bound longer than the clock can count
waits as long as it can and does not overflow (`runtime/linux/linux.kz`, `Deadline`). The case
`corpus/system/connect-timeout.kz` meant to reach that saturation and first asked for 106751 days, the
most whole days a `duration` holds.

The mutation that took the saturation out, `if false` in place of its test, stayed green. 106751 days
are 9223286400 seconds, 85636.85 seconds short of the largest `long` in nanoseconds, so the sum
passes it only once the monotonic clock, which on Linux counts from the boot, has run about 23.8 hours.
The machine that ran the mutation had run about 19, and every sum fit. On a machine that had run a
day the same case would have overflowed, so the mutation's verdict hung on the time since the boot,
and a runner of the forge, started for the job, would not have turned it red.

The case now asks for `9223372036854775807ns`, the largest `duration`; the sum passes the largest
`long` on every machine whose clock has run a nanosecond, and the mutation turned red.

## What to do

- A test of a saturation, a clamp or an overflow takes the largest value of the type, or the smallest,
  never a large value that reaches the bound only together with a value the machine supplies, a clock,
  an address, a size of the system.
- Where the bound can be reached only with such a value, the test sets that value, as a runtime edit of
  `ListTests.Edited` can return a clock of its own, and does not wait for the machine to reach it.
- The same shape as the bytes of 128 and more in
  [a-word-of-bytes-is-read-at-any-address.md](a-word-of-bytes-is-read-at-any-address.md): a test of a
  boundary holds the values that reach it, and the mutation that removes the boundary's handling is run
  to show they do.
