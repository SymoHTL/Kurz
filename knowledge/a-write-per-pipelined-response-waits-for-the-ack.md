---
name: a-write-per-pipelined-response-waits-for-the-ack
description: The server's loop wrote each response with a write of its own; to a client that sends its requests at once, every write after the first waited for the client's acknowledgement of the one before, which a client that sends nothing until its answers are in gives only at its delayed-ACK timer, about 40 ms a batch; the loop holds such a response and writes it with the next, as hyper's buffered writes do, and a sequential bench never shows the stall
metadata:
  type: feedback
---

The loop of the server (W9) wrote each response with its own `write` as soon as the handler gave it.
A client that pipelines sends a batch of requests and reads nothing back until every response of the
batch is in. The socket keeps Nagle's algorithm on, as Linux and Rust's `TcpStream` keep it by
default. The first response of a batch goes out at once. Each later small write waits until the data
before it is acknowledged, and the client acknowledges a lone segment only when its delayed-ACK timer
runs out, about 40 ms on Linux.

Measured on 2026-11-04 with one connection, a `GET` answered `hello`, and the server built in release:

- Requests one at a time: 169 thousand a second, beside hyper's 154 thousand. Nothing waits there,
  since the client's next request carries the acknowledgement.
- Batches of 2 and of 4: 2000 batches did not finish in 60 seconds, so each batch took more than 30 ms.
- Batches of 16: five runs of 100000 requests had not ended after 13 minutes. A look at the server's
  socket found the 16 responses of a batch, 1936 bytes, waiting for their acknowledgement.

The loop now holds a response, up to 64 KiB, while the next request's head already stands whole in the
stream's buffer, and writes it with the response after it in one write. That is the policy of hyper's
`proto::h1::Conn`, which flushes its write buffer when the next read would wait. With it, the longest of
2000 batches of 2 and of 16 took 0.1 ms.

What the holding must keep, each with its case:

- **The order of what the client gets.** Held responses go out before a refusal and before the
  writer's own `500`, in the same write (`serve-refused`, `serve-close`).
- **What a handler may wait on.** Held responses go out before the handler of a request with a body
  runs. That handler may wait for the body, and its first read may send `100 Continue`, which must
  follow the earlier responses (`serve-drain`).

A sequential bench, one request per round trip, cannot show this stall. A bench of an HTTP server
measures a pipelined depth too.

Gate: `compiler-tests` runs the order cases above. That a response is held at all is a matter of time
alone, so no case can fail on it: HAZARD (#157), and the bench of pull requests that change the loop.
