---
name: posix-signal-types-hide-under-std-c11
description: Under -std=c11 the C library declares only what the C standard names, so siginfo_t, stack_t, sigaltstack and SA_ONSTACK are missing from signal.h until a feature macro asks for them; the runtime defines _XOPEN_SOURCE 700 before its first include (2026-10-20)
metadata:
  type: reference
---

Seen on 2026-10-20: the suite's toolchain fixture compiled the runtime with `-std=c11` and clang
answered `unknown type name 'siginfo_t'` and `use of undeclared identifier 'stack_t'`, on a
machine where the same file had compiled without the standard flag.

With `-std=c11` the library headers hide every declaration the C standard does not name, the
signal stack and the extended fault handler among them; with the GNU default (`-std=gnu17`) they
are visible, which is why a compile by hand passes and the compiler's own command does not. The
feature macro that asks for them is `_XOPEN_SOURCE 700` (POSIX.1-2008 with the XSI extensions,
which `sigaltstack` and `SA_ONSTACK` belong to), defined before the first include and outside
Windows, where the headers are another library's. The runtime does that in its first lines
(`compiler/Kurz.Compiler/Runtime/kurz_rt.c`), and the fixture compiles it with the flags the
compiler uses, so that a header the runtime needs is found by the same command in every place.
