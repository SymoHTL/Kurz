---
name: one-epoll-shared-across-a-fork
description: A child of `fork` shares its parent's `epoll`, so that an event of either process reaches the wait of the other, with data that names the other's memory; the first head of the poller (pull request 660) held `http/client-pool.kz`'s forked server to the limit of its run, and the runtime drops the poller in the child (`Forked`, by `pthread_atfork`), as libuv's `uv_loop_fork` and libevent's `event_reinit` make the loop again
metadata:
  type: feedback
---

An `epoll` descriptor that a process holds when it calls `fork` is one open file shared by the
parent and the child, as every descriptor is: the child's copy refers to the same open file
description as the parent's (fork(2); not read again at source for this entry). A registration names the open file it watches and its data, not the process
that made it, so a descriptor that either process registers can give its event to an `epoll_wait`
of the other, and the data the event carries is a word the other process wrote, an address in its
own memory.

The poller of pull request 660 was made at the first socket and its registrations' data was the
address of the waiting call's block. `http/client-pool.kz` starts a server in a forked child after
its first socket, and on the first head the child's wait took events the parent registered, wrote
the word of a block at an address of the parent's heap that the child's copy holds as something
else, and the run waited to the limit of the program's run (the change's description). The child
drops the poller it shares: `pthread_atfork` runs `Forked` in the child, which closes the
descriptor and sets the poller to none, so that the child's first registration, or its first
socket, makes a poller of its own, as the parent's first made the parent's: `PollAdd` calls `Poller`
before each registration, which a call on a socket that waits (`SocketWaited`, the read of an
accepted connection among them), the wait for a child (`ManyWaited`), the pool's `eventfd`
(`PoolArm`) and a registration armed again (`Rearm`) make, and `Attempt` calls it before it makes a
socket, in the child as in the parent, as libuv's `uv_loop_fork` and libevent's `event_reinit` make
the loop again in a child.

The child keeps the scheduler's count of registrations its parent made, and its waits name the
parent's poller: with no poller, `PollWait` sleeps the time it was asked for and gives no event,
so that the child's scheduler waits on its timers and does not spin (#39, the reading of pull
request 660, `linux.kz` 2883).

Gate: `compiler-tests` (`RunTests` runs `http/client-pool.kz`, whose forked server answers within
the limit only where the child holds a poller of its own).
