---
name: a-struct-copy-becomes-a-memcpy-call
description: At -O2 clang turns a struct assignment, and a loop that copies or clears bytes, into a call of the C library's memcpy or memset, so a runtime that promises its own loops at the boundary imports them all the same; the runtime copies its wide integers with a word loop and is compiled with -fno-builtin, and the object's undefined symbols are read by hand after every change to it (HAZARD #36).
metadata:
  type: reference
---

Seen on 2026-10-20: the decimal and duration code of `compiler/Kurz.Compiler/Runtime/kurz_rt.c`,
compiled with `-std=c11 -O2 -c`, imported `memcpy` and `memset`, while the runtime's boundary
(design record, section 2; `compiler/README.md`, "What the emitter emits") names neither. Five
assignments of a 196-byte struct, the runtime's wide integer, had become calls of `memcpy`, and
a loop that cleared one a call of `memset`: the optimizer recognises the idiom and calls the
library for it, whatever the source says.

What holds now: the runtime copies its wide integer with `kz_big_copy`, a loop over the words,
and `Clang.CompileRuntime` and the C path of `Clang.Link`
(`compiler/Kurz.Compiler/Build/Clang.cs`) pass `-fno-builtin`, which keeps the optimizer from
turning a loop into a library call. With both, the object's undefined symbols on Linux are
`write`, `_exit`, `malloc`, `__errno_location`, `sigaltstack`, `sigaction` and `sigemptyset`
(`nm -u` on the object). Nothing in the suite reads that list, HAZARD (#36): it is read by hand
after a change to the runtime, and the pull request names what was read.
