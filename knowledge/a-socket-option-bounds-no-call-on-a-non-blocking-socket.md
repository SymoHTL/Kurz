---
name: a-socket-option-bounds-no-call-on-a-non-blocking-socket
description: `SO_RCVTIMEO` and `SO_SNDTIMEO` bound a call that blocks and no call on a socket that does not block, so the poller (pull request 660) reads each bound back from the option and holds it on the runtime's timer; Linux keeps the option in jiffies, so a bound reads back rounded up to the kernel's tick, and the poller's descriptor, made before the first socket, moves every descriptor number a test reads
metadata:
  type: feedback
---

A socket made non-blocking returns `EAGAIN` at once where a call would wait, and the kernel
measures no time for it: `SO_RCVTIMEO` and `SO_SNDTIMEO` bound the waits of a blocking socket alone
(socket(7)). Pull request 660 made every socket non-blocking so that a call that would block waits
in the poller, and kept the bounds the program sets in those options, as section 12's "Same
bounds, poller timer" keeps their value and meaning: each wait reads its bound back from the option
(`SocketBound`) and holds it on the runtime's timer, which `advance` moves (Y10). A bound kept in
the option alone would have bounded nothing.

The value read back is the kernel's: Linux stores the option in jiffies, rounding a bound up to
the tick of its `HZ`, so that a bound of 1 ms reads back as one tick, 4 ms at an `HZ` of 250, and a
bound past the kernel's greatest timeout reads back as none (`sock_set_timeout`; the reading of pull
request 660 on #39, `linux.kz` 2780; not measured here). A case that needs a bound to the
millisecond reads it from the runtime, not back from the option.

The poller is made before the first socket (`Poller`), so that it takes the same descriptor in
every run, 3 where the program opened nothing before, and every socket's descriptor is one higher
than it was without it: the tests of `FileTests` and `HttpServerTests` that read a socket's options,
flags or close by its descriptor's number read the numbers after the poller's (pull request 660).
A test that names a descriptor by its number names the poller's first.

Gate: `compiler-tests` (`advance-read-bound.kz`, whose read ends at the `advance` of its bound only
where the bound stands on the runtime's timer; the descriptor tests of `FileTests` and
`HttpServerTests`).
