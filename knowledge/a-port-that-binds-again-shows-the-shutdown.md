---
name: a-port-that-binds-again-shows-the-shutdown
description: on Linux a listener's port binds again after its Close because the Close's shutdown(2) frees it, with the descriptor still open; a test of the release reads the descriptor with fcntl(F_GETFD), which fails with EBADF only once it is closed (pull request 225's socket fact, found by its review, 2026-11-02)
metadata:
  type: reference
---

## What happened

A `Close` of a handle with a call in flight on it leaves the release of its descriptor to the last
call out, and one with none releases it at once; a socket's `Close` with a call in flight shuts the
socket down first, `shutdown(2)`, so that a call blocked on it returns at once
(reference/13-files.md, S5 and S9; pull request 225). A fact of `FileTests` closed a listener while
an `Accept` was in flight and bound a new listener to the same port, and its comment said that the
bind showed the descriptor released.

It showed the shutdown: a listener that is shut down listens no more, and its port binds again at
once, with the descriptor still open. The mutation `leave-never-releases`, in which the last call
out releases nothing, left that fact green, its bind of the port included, and turned only the fact
of a file red (pull request 225's mutations, 2026-11-02, Linux 6.8). The review of
pull request 225 found it, and pull request 243 changed the fact.

## What to do

- A test of the release reads the descriptor: `fcntl(fd, F_GETFD)` gives -1, `EBADF`, once it is
  closed, and the descriptor's flags, 0 or more, while it is open. The programs of `FileTests`
  declare `fcntl` in an `extern "C"` block and read the number the listener holds, the program's
  first descriptor, 3 (`DescriptorOpen`); the fact's positive control expects it open.
- A port that binds again shows only that nothing listens on it: after a shutdown, or after a
  release.

The gate is `compiler-tests`:
`FileTests.A_close_of_a_socket_shuts_it_down_so_a_call_in_flight_on_it_ends_with_the_closed_handle_s_error`
expects the listener's descriptor closed after the `Accept` in flight returned, and
`leave-never-releases` turns it red. A new test of a release that reads a port instead is caught by
no rule of the review: a `judgment step` for its author.
