---
name: a-struct-by-value-crosses-the-c-boundary-differently
description: A 16-byte struct passed or returned by value is lowered by each platform's C ABI, in two registers on System V x86-64 and through a hidden pointer on Windows x64; IR that declares such a parameter as an aggregate matches one of them at most, so the runtime's boundary takes and returns scalars only (2026-10-15)
metadata:
  type: reference
---

Caught on 2026-10-15 while the runtime's boundary was designed, before any program crossed it.

The emitted IR declares the runtime's functions and calls them. A string here is a pointer and a
count, a list a pointer and a count: 16 bytes each. A C function that takes such a struct by value
is called by the rules of the platform's C ABI, which clang applies when it compiles the C: on
System V x86-64 the struct goes in two integer registers, on Windows x64 it goes by a hidden
pointer to a copy, and a struct returned by value goes through a hidden result pointer there. An
LLVM function type that names the struct as an aggregate parameter is not lowered by those rules:
the IR would have to be written differently for each platform the compiler targets, and a mismatch
reads the wrong registers without a diagnostic.

The runtime's functions therefore take and return scalars only (`compiler/Kurz.Compiler/Runtime/kurz_rt.c`,
and the declarations in `compiler/Kurz.Compiler/Emit/Runtime.cs`): a string or a list crosses the
boundary as its two fields, and a result of that shape is written through a pointer the caller
passes. One text of IR then serves every platform the gate accepts.
