---
name: clang-names-no-line-of-the-program-for-a-refused-asm
description: clang 23 given a module of LLVM IR names no line of the program for an instruction of inline assembly it refuses, with Rust's `!srcloc` on the call or without (2026-11-13), while its C front end, which writes the `!srcloc` and reads it back, names the line of the `asm` statement, of two that define one label the second; and at -O0 it drops a call of no effect whose value nothing uses before anything is assembled, a `pure` asm of `memory(none)` or `memory(read)`, with `sideeffect` or without, so the build reads the line itself by assembling the program's `asm`s together in the order of their lines, each copy a call with an effect.
metadata:
  type: reference
---

Seen on 2026-11-13, while building `asm-refused` (R11; the owner, 2026-11-13, pull request 520,
comment 6847933349, "Rust's srcloc"). The compiler hands clang a module of LLVM IR (`Clang.Build` in
`compiler/Kurz.Compiler/Build/Clang.cs`). Rust's compiler puts a `!srcloc` on each call of inline
assembly "so we can map LLVM errors back to source locations" (`rustc_codegen_llvm/src/asm.rs`):
LLVM reports an error of inline assembly with the location cookie of the call whose text it was
reading, and the front end that wrote the cookie maps it back to its source
(`rustc_codegen_llvm/src/back/write.rs` for Rust). A module of IR comes with no such front end.

What `clang-23 -O0 -c` wrote on Linux x86-64, clang 23.1.4:

- a module with two calls of refused instructions, carrying `!srcloc !{i64 17}` and `!{i64 23}`:
  `<inline asm>:3:1: error: invalid instruction mnemonic 'badinstr'` and
  `<inline asm>:2:2: error: invalid instruction mnemonic 'otherbad'`, each with the template's line
  and `error: cannot compile inline asm`, and no number of the metadata; the module of the first call
  without the metadata wrote the same lines for it. The line clang names is one of the template,
  not of the program.
- a C file of three `__asm__` statements, the first and the third defining the label `twice`:
  `twice.c:4:20: error: symbol 'twice' is already defined`, the line of the third statement, the
  second to define the label. The module of IR of two functions, each one call that defines `twice`,
  wrote `<inline asm>:2:2: error: symbol 'twice' is already defined`, with no line.
- a module whose one call, `%r = call i64 asm ... "frobnicate $0"`, has no use: with `sideeffect`
  and `nounwind` alone, the refusal; with `nounwind willreturn memory(none)` or
  `nounwind willreturn memory(read)`, with `sideeffect` or without, an object and the exit code 0.
  A call that writes no memory, returns and does not unwind has no effect, so LLVM dropped it before
  the assembler read its text.

So a build of IR finds the line of a refused `asm` itself. `Builder.Build` assembles the program's
`asm`s together without the rest of the program, each as the emitter wrote it in IR of its own
(`AsmAlone`), in the order of their lines, each beside those before it that clang took, and names
each one whose addition clang refuses: one clang refuses alone, and of two that define one label the
second, where the C front end names it too. Each copy is a call with `sideeffect` and no attribute
but `nounwind`, whatever the `asm`'s options: the program's own `pure` call is kept by the use of its
value, and the copy's value has none.

Gate: `compiler-tests`
(`EmitterTests.An_instruction_the_assembler_refuses_is_asm_refused_at_the_line_of_its_asm` for the
order and the label two `asm`s define,
`EmitterTests.A_pure_asm_the_assembler_refuses_is_asm_refused_at_its_line_in_a_test_build_and_a_release_build`
for the copy's effect). Whether a clang of another major reads a `!srcloc` from IR is read by no test:
a `judgment step` where the pinned major changes. Related:
[[a-front-end-option-of-clang-does-not-reach-ir]].
