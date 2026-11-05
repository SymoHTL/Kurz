---
name: a-client-case-forks-its-server
description: A case of the HTTP client cannot answer its own `Get` from the same thread, since the program is one actor whose calls block, and `Command.Spawn()` is not emitted; the client's cases fork the program under `allow: raw native`, serve in the child and kill it at the end; the child serves its connections one at a time, so that a second connection is accepted only once the first ends, at the server's `KeepAliveTimeout` or the client's close (2026-11-05); and the listener's `Accept` is bounded before the fork, since a child whose program ended by an error waits on `Accept` without end and holds the output the suite reads, which hung a run of the suite for 80 minutes (2026-11-05)
metadata:
  type: reference
---

## What happened

The server's cases (W9) run a server and a client in one program, in turn: the client writes every
request and shuts its side down, then `ServeConnection` serves the connection to its end, then the
client reads what came back. The client's `Get` (W13) cannot run that way. It connects, writes its
request and waits for the response in one call, and nothing else runs in the program while it waits,
since the compiler builds no actors and a call blocks its thread (S10). A server in the same thread
never gets to answer.

A child process would serve it, but the emitter says `not supported` to `Command` and `ChildProcess`
(S17 to S22): `kurz run` of a program that spawns `/proc/self/exe` stops at its build (2026-11-05).

## What to do

- The case declares `fork`, `kill`, `waitpid` and `_exit` in an `extern "C"` block, under the header
  line `// allow: raw native`, which the corpus's run grants (R7, R16). `kurz run` grants neither, so
  such a case runs through the suite alone:
  `dotnet test ... --filter 'DisplayName~http/client'`.
- The program binds the listener before the fork, so that both processes hold it and the parent
  knows the port. The child serves and leaves with `_exit(0)` in a `raw` block, so that it runs none
  of the parent's statements after the fork and prints nothing. The parent runs the client and ends
  the child with `kill(child, 9)` and `waitpid(child, 0, 0)`.
- The program bounds the listener's `Accept` before the fork, `SetAcceptTimeout(10s)`, so that a
  child whose program ended by an error before it could end the child ends by itself 10 seconds after
  its last connection and lets go of the output the suite reads. `Accept` waits without a bound by
  default (S9): a child that waited so held that output after its program raised, and a run of the
  suite waited on it for 80 minutes (2026-11-05). A case's own calls leave less than 10 seconds
  between two connections.
- The child serves one connection at a time, as `HttpServer.Serve` does. A client whose first
  connection is idle in its pool and that opens a second one waits until the first ends: at the
  server's `KeepAliveTimeout`, or when the client closes it. A case that needs a second connection sets
  a short `KeepAliveTimeout`, 100 ms in `client-pool.kz`. A case that needs the first connection again
  keeps the default 5 seconds, so that the client's next request comes long before it.
- A case whose server is raw, its answers scripted, reads each request with `HttpRequest.Read` before it
  writes the answer. A close with the request unread would send the client a reset in place of the
  answer (S9).
- A case of the pool tells connections apart by a number the child writes into each answer: the
  server's number of its connection, counted at each `Accept`.

The gate is `compiler-tests`: the client's cases under `corpus/http/` run on Linux, and Windows says
`not supported` to them at their first call on a socket (`FileCases` in `RunTests`). That a new case
follows this recipe is a `judgment step` for its author.
