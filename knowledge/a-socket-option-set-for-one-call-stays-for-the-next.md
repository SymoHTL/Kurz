---
name: a-socket-option-set-for-one-call-stays-for-the-next
description: The runtime's WriteWithin set SO_SNDTIMEO to what remained of its bound before each write call and left it there when it returned, so a later plain write of the same stream, the 100 Continue of a later request among them, ran under the last remainder and not the stream's default bound (pull request 350, found by its review on 2026-11-06); a call that sets an option of a stream for its own bound puts the stream's own value back before it returns, and a test reads the option back after the call
metadata:
  type: reference
---

## What happened

`WriteWithin` in `runtime/linux/linux.kz` is the write the server's responses take (W10). It holds
the write to one bound across the calls of the system it makes: before each call of `write` it sets
`SO_SNDTIMEO` to what remains of that bound. As pull request 350 brought it in, it returned with the
option still at the last remainder it had set.

A later write of the same stream that sets no time of its own then ran under that remainder, not
under the stream's default bound (S9). One such write is the `HTTP/1.1 100 Continue` that the first
read of a later request's body sends (`HttpBody.Take` in `std/std.kz`). The review of pull request
350 found it (issue 39, comment 6682747022, item 19).

Pull request 351 made `WriteWithin` set `SO_SNDTIMEO` back to the stream's default bound before it
returns. Where that reset fails after a write that did not, the reset's failure is the result. Its
test reads the option back through `getsockopt` at each request of one connection. With the reset
taken out, the second request read `0 100000`, the 100 ms the first response left, and the test
failed. With it in place, it reads `5 0` at both requests.

## What to do

- A call that sets an option of a stream for a bound of its own puts the stream's own value back
  before it returns, on every path, a failed write among them. Otherwise the next call of the stream
  that sets nothing runs under the value the first one left.
- A test of such a call reads the option back after it returns, on a later call of the same stream,
  and does not stop at the result of the call that set it.

Gate: `compiler-tests`
(`HttpServerTests.A_response_held_to_its_rate_leaves_the_stream_its_default_bound`) for
`WriteWithin`. Another call that sets an option for itself and leaves it set is caught by no rule of
the review: a `judgment step` for its author.
