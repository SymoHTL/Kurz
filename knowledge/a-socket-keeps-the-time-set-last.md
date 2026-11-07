---
name: a-socket-keeps-the-time-set-last
description: A time set on a socket with `SetReadTimeout` holds for every later read until it is set again, so a mutation that drops the set before one read stays green in a case whose bytes stop at once, since that read waits the time the set before it left; a case of what remains before each read sends its bytes one at a time, each before a read's time would run out, and holds the time the call took (2026-11-05)
metadata:
  type: feedback
---

## What happened

The client's call time (W16) sets `SO_RCVTIMEO` to what remains of the call before each read that
calls the system. Its first cases stopped each response halfway: a body of a length, a chunked body
and a body read to the close, each followed by nothing. On 2026-11-05 two mutations were run against
that first version of `corpus/http/client-timeout-body.kz`, with a control that changed a comment:

- the set dropped before a read of a body of a length: GREEN;
- the set dropped before a read of a chunk's line: GREEN;
- the control: GREEN.

Each mutated read waited the time the read before it had set, which was what remained of the call a
moment earlier. It ran out at about the same time and gave the same `TimedOut`. The case could not
tell a read that set its time from one that kept the last.

## What to do

- A case of a bound that is set before each read sends its bytes one at a time, each before the time
  a read would wait runs out. `client-timeout-body.kz` writes ten bytes 100 ms apart against a call of
  300 ms, for a body of a length, a body read to the close and a chunk's size line.
- The case holds both what the call gives and the time it took: `TimedOut` with the number 0, and
  less than 900 ms. A read that keeps the last time set reads every byte. Where the next step finds
  nothing remaining, the call still ends in `TimedOut`, but after a second, so the time is what
  shows it.
- The same holds for `SetWriteTimeout` and for any bound a socket keeps across calls.

The gate is `compiler-tests`: with the trickled bodies, both mutations above turn
`http/client-timeout-body.kz` red. That a new case of a bound set before each read trickles its
bytes is a `judgment step` for its author.
