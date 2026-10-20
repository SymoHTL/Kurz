---
name: a-tail-call-never-overflows-the-stack
description: clang at -O2 turns a self-recursive tail call into a loop, so a recursion that should exhaust the stack runs forever; the runtime's stack-overflow test hung on it (2026-10-15), the emitter marks every call of a Kurz function notail, and a C test of recursion keeps work after the call
metadata:
  type: reference
---

Seen on 2026-10-15: the smoke test of the runtime's stack-overflow handler, a C function that
called itself with `return deep(n + 1)`, never overflowed; the process spun until it was ended.

A call in tail position is turned into a jump by the optimizer: the frame is reused, the stack
does not grow, and the recursion is a loop. O11 says that an actor which exhausts its stack dies
with `stack-overflow` at the line of the call that did not fit, so a Kurz program's recursion has
to use the stack as written. Two things follow:

- The emitter marks every call of a Kurz function `notail`, so that LLVM never turns one into a
  jump (`compiler/Kurz.Compiler/Emit/Emitter.Calls.cs`). The line stored before each call is
  what the handler reports.
- A C test of the handler keeps work after the call, `return deep(n + 1) + pad[0]`, so that the
  call is not in tail position whatever the optimizer does.
