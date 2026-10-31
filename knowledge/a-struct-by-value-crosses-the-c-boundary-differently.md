---
name: a-struct-by-value-crosses-the-c-boundary-differently
description: A 16-byte struct passed or returned by value is lowered by each platform's C ABI, in two registers on System V x86-64 and through a hidden pointer on Windows x64; IR that declares such a parameter as an aggregate matches one of them at most, which is why no type that crosses to C is an aggregate (2026-10-15)
metadata:
  type: reference
---

Caught on 2026-10-15 while the boundary of the runtime of that day, written in C, was designed,
before any program crossed it.

The emitted IR declares the C functions it calls. A string here is a pointer and a count, a list
a pointer and a count: 16 bytes each. A C function that takes such a struct by value is called by
the rules of the platform's C ABI, which clang applies when it compiles the C: on System V x86-64
the struct goes in two integer registers, on Windows x64 it goes by a hidden pointer to a copy,
and a struct returned by value goes through a hidden result pointer there. An LLVM function type
that names the struct as an aggregate parameter is not lowered by those rules: the IR would have
to be written differently for each platform the compiler targets, and a mismatch reads the wrong
registers without a diagnostic.

The runtime is Kurz since round 17 (section 2 of the design record), so the boundary of that day
is gone; the trap stands at every declaration of a C function (R16). The checker lets numbers,
`bool`, pointers and C function pointers cross to C and nothing else (`Crosses` in
`compiler/Kurz.Compiler/Semantics/Checker.Raw.cs`): a `data` value, a string or a list in a
declaration's parameter or result is `native-type`, so no emitted declaration names an aggregate.
A rule that lets a `data` type cross by value needs the platform's lowering written for each
target, or a pointer to a copy, before it is a line of the emitter.
