---
name: a-client-case-forks-its-server
description: A case of the HTTP client cannot answer its own `Get` from the same thread, since the program is one actor whose calls block, and `Command.Spawn()` is not emitted; the client's cases fork the program under `allow: raw native`, serve in the child and kill it at the end; the child serves its connections one at a time, each ended by the script and by no timer of the server, a connection past its answers closed at once (2026-11-06); a fork that fails ends the case before any `kill`, since `kill(-1, 9)` reaches every process the user may signal (2026-11-06); and the listener's `Accept` is bounded before the fork, since a child whose program ended by an error waits on `Accept` without end and holds the output the suite reads, which hung a run of the suite for 80 minutes (2026-11-05)
metadata:
  type: reference
---

## What happened

The server's cases (W9) run a server and a client in one program, in turn: the client writes every
request and shuts its side down, then `ServeConnection` serves the connection to its end, then the
client reads what came back. The client's `Get` (W13) cannot run that way. It connects, writes its
request and waits for the response in one call, and nothing else runs in the program while it waits,
since the compiler runs every actor on the program's one thread and a call blocks it (S10). A server in the same thread
never gets to answer.

A child process would serve it, but the checker says `not supported` to `Spawn()` and `ChildProcess`
(S19): `kurz run` of a program that spawns `/proc/self/exe` stops at its build (2026-11-05). A
`Command`'s `Output()` and `Status()` (S18) are built on Linux since 2026-11-11 (pull request 476), and
each waits for its child's end before the program goes on, as the runs of `ChildTests` showed that day,
so neither runs a server beside the client.

## What to do

- The case declares `fork`, `kill`, `waitpid` and `_exit` in an `extern "C"` block, under the header
  line `// allow: raw native`, which the corpus's run grants (R7, R16). `kurz run` grants neither, so
  such a case runs through the suite alone:
  `dotnet test ... --filter 'DisplayName~http/client'`.
- The program binds the listener before the fork, so that both processes hold it and the parent
  knows the port. The child serves and leaves with `_exit(0)` in a `raw` block, so that it runs none
  of the parent's statements after the fork and prints nothing. The parent runs the client and ends
  the child with `kill(child, 9)` and `waitpid(child, 0, 0)`.
- A fork that fails gives -1. The parent prints `fork failed` and returns before the client, so that
  no `kill` or `waitpid` takes a pid of 0 or below: `kill(-1, 9)` sends the signal to every process
  the user may signal, and `kill(0, 9)` to the whole process group. Every forking case had the
  pattern without the check, which the review of pull request 317 found (2026-11-06).
- The program bounds the listener's `Accept` before the fork, `SetAcceptTimeout(10s)`, so that a
  child whose program ended by an error before it could end the child ends by itself 10 seconds after
  its last connection and lets go of the output the suite reads. `Accept` waits without a bound by
  default (S9): a child that waited so held that output after its program raised, and a run of the
  suite waited on it for 80 minutes (2026-11-05). A case's own calls leave less than 10 seconds
  between two connections.
- The child serves one connection at a time, as `HttpServer.Serve` does. A client whose first
  connection is idle in its pool and that opens a second one waits until the first ends. The script
  ends a connection, never a timer: the child of `client-pool.kz` closes each connection after the
  number of responses the case gives it and reads with no bound (`SetReadTimeout(null)`), so that
  the output does not rest on the time the parent takes between two requests. A keep-alive of
  100 ms, 50 ms or 5 seconds made the pool case's connection numbers rest on the runner's speed
  (the review of pull request 317, 2026-11-06).
- A case whose server is raw, its answers scripted, reads each request with `HttpRequest.Read` before it
  writes the answer. A close with the request unread would send the client a reset in place of the
  answer (S9). Past its answers the child keeps accepting and closes each connection at once, so that
  a request the client should not have sent shows as a wrong line, and not as a call that waits on a
  connection the parent's listener holds and nobody serves.
- A case of the pool tells connections apart by a number the child writes into each answer: the
  server's number of its connection, counted at each `Accept`.

The gate is `compiler-tests`: the client's cases under `corpus/http/` run on Linux, and Windows says
`not supported` to them at their first call on a socket (`FileCases` in `RunTests`). That a new case
follows this recipe is a `judgment step` for its author.
