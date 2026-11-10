---
name: a-test-never-picks-the-port-of-its-server
description: On 2026-11-06 a test of `HttpServerTests` failed twice, once in CI and once in the image, and on 2026-11-07 three of its tests failed once each in the same way: in the image its client was connected and reset within 0.5 ms of the test's start, before the program could have bound its port, while the program then waited in its accept until the test's time ended, so the client's connection had reached another socket on the port the test had chosen and freed for the program. A server program a test runs binds port 0 and writes the port it got into a file beside it, and the client connects only to that port
metadata:
  type: feedback
---

On 2026-11-06 `HttpServerTests.A_chunked_file_ends_its_body_in_the_write_of_its_last_piece` failed
twice. The first failure was in CI, in the gates run of pull request 322 (run 42202379628). The second
was in the image, on the take of `main` by pull request 342. The run in the image read:

- the client was connected and got `Connection reset by peer` at its first read, with no response,
  0.5 ms after the test's clock started, which the test starts before it launches the program;
- the program gave no answer within the test's 60 seconds, and `/proc` showed it waiting in
  `accept4` on its listener.

The client was connected before the program could have bound anything, so a socket other than the
program's listener had completed its handshake, and then closed it. The log of the CI run, cut by its
display, shows the same end: the program ended at its limit with no response, and the client's first
read failed. The test had picked the port itself: it took a port of its own block below the range the system
gives for port 0, tried a listener on it, freed it, and handed it to the program, which bound it a
moment later. Between that free and that bind, the port belonged to nobody. That block was itself
the fix of an earlier failure of the same shape (a port taken with port 0, freed, and given to
another test's socket), and the comment that recorded it named the same symptom. The runs did not
show which socket held the port this time.

On 2026-11-07 the same end came three more times, in three tests of the class, each passing again on
the same head:

- `A_chunked_file_ends_its_body_in_the_write_of_its_last_piece` in the image, on the take of `main`
  by pull request 350: the client was reset at its first read 0.53 ms after the test's clock started,
  and the program waited in `accept4` on its listener until its 60 seconds ended;
- `A_write_of_a_response_is_held_to_its_bound_across_the_calls_of_the_system_it_makes` in the image,
  on the take of `main` by pull request 317: the client's read got `Connection reset by peer`;
- `A_write_of_a_response_is_given_the_longer_of_the_grace_and_its_length_at_the_rate` in CI, in the
  gates run of pull request 373 (run 42380936179), with the same message.

Every test of the class took its port from the same block, so a fault of the block reaches all of them.
The block is a function of the process's id, so two runs of the suite whose ids are equal modulo 400
share it (inferred: no run showed that).

**Why:** a port that a test chooses and frees stays free for every socket on the machine until the
program binds it. The test runs beside other tests, other test runs, and the programs of the
corpus. A client that connects in that gap cannot tell the program's listener from any other socket
that answers. A block of ports of the test's own makes such a socket rarer, but it cannot exclude
one.

**How to apply:**

- A server program a test runs binds `127.0.0.1:0`. Once it listens, it writes the port the system
  gave it into a file named `port` in its working directory, with a newline after the number, which
  shows that the write is whole.
- The client waits until that file holds a whole number, or until the program has ended, and then
  connects to that port only. From the moment the client learns the port, the program's listener
  holds it, so no other socket can answer the connection.
- `HttpServerTests.Connect(executable, program)` is that wait. A new test of a server program calls
  it, and never chooses a port, tries it and frees it.
