---
name: clang-names-no-line-of-the-program-for-a-refused-asm
description: clang 23 given a module of LLVM IR names no line of the program for an instruction of inline assembly it refuses, with Rust's `!srcloc` on the call or without (2026-11-13), while its C front end, which writes the `!srcloc` and reads it back, names the line of the `asm` statement, of two that define one label the second; and at -O0 it drops a call of no effect whose value nothing uses before anything is assembled, a `pure` asm of `memory(none)` or `memory(read)`, with `sideeffect` or without, and it refuses a label of module-level assembly that is a symbol the module defines where it writes that definition, with no `<inline asm>` line, so the build reads the line itself by assembling the program's `asm`s together in the order LLVM meets them, the order the compiler writes them, in one file as across files, beside a label for each symbol the program defines, an `asm` statement's copy a call with an effect in the copies' `main`, and in a release build a copy for each call its code generator holds once it placed the blocks, read from its machine IR by the `!srcloc` the emitter puts on it, which clang keeps on every copy, one its optimiser inlines and one its tail duplication makes (2026-11-13).
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
- a module whose module-level assembly, as the emitter writes a `naked` function's, defines the label
  `main:`, and which defines the function `@main`: `<unknown>:0: error: symbol 'main' is already
  defined` and `error: cannot compile inline asm`, with no `<inline asm>` line, since LLVM writes the
  module-level assembly before the definitions and finds the clash where it writes `main`; the same
  for the label `.L.str.0:` and the private constant `@.str.0`, which LLVM names `.L.str.0` in the
  assembly, and the same with `--target=x86_64-pc-windows-msvc`. The same label after a label
  `"main":` of module-level assembly wrote `<inline asm>:6:1: error: symbol 'main' is already
  defined`, in the text of the second.

So a build of IR finds the line of a refused `asm` itself. `Builder.Build` assembles the program's
`asm`s together without the rest of the program, beside a label of module-level assembly for each
symbol the program's IR defines, so that a `naked` function's label that is one of them is refused in
its text, each as the emitter wrote it in IR of its own (`AsmAlone`), in the order LLVM meets them,
the order the compiler writes them (R11, R12; the owner, 2026-11-13, pull request 530, comment
6855725580, "LLVM's order everywhere"), each beside those before it that clang took, and names each
one whose addition clang refuses: one clang refuses alone, and of two `naked` functions that define
one label the one LLVM meets second, which LLVM's order makes the earlier line's where the two are
called out of the order of their lines (`clang-23 -O0 -c` on the copies, 23.1.4, 2026-11-13, refused the label of the
function whose first call comes later).
It does so on clang's `<inline asm>` line and on `cannot compile inline asm` alone. The copies'
`asm` statements are calls in the function `main`, which every program defines, in place of its
label: a function of the copies' own, `kz.asm0`, is a symbol a program's directive can define too,
and `.equiv kz.asm0, 1` in the copy of that name drew `<inline asm>:2:18: error: redefinition of
'kz.asm0'`, a refusal the program's own build does not have (`clang-23 -O0 -c` on the copies, 23.1.4, 2026-11-13). The copy of an `asm` statement
is a call with `sideeffect` and no attribute but `nounwind`, whatever the `asm`'s options, and a
`naked` function's copy is its module-level assembly: in a test build the program's own `pure` call
is kept by the store of its value into its variable, and the copy's value has none. A release build
drops a `pure` call whose outputs nothing reads before anything is assembled, so it names no refusal
of it, where a test build of the same program is `asm-refused` (R11, the owner, 2026-11-13, pull
request 530, comment 6854386584, "As Rust, builds differ").

Where clang refuses a release build, the copies of the `asm` statements are made from the code its code
generator holds (R11; the owner, 2026-11-13, pull request 532, comment 6860019854, "Copies from optimised
IR"): the emitter puts the line of each `asm` of the program on its call, `!srcloc !{i64 <line>}`, as
Rust's compiler puts a `!srcloc` on each call where the build asks for neither LTO nor bitcode
(`allow_raw_span_inline_asm_srcloc` in `rustc_codegen_llvm/src/asm.rs` of Rust 1.100.0, read on
2026-11-13), `Clang.Generate` has clang write, at the release build's options, the machine IR its code
generator holds once it placed the blocks (`-S -mllvm -stop-after=block-placement`), and `Builder.Held`
takes a copy for each `INLINEASM` there with a mark, in its order, the `asm` of the mark's line whose
template the call has. A call the optimiser dropped gets no copy, and one LLVM copied gets one for each
copy, every one after the first refused where its text defines a symbol. Seen on 2026-11-13 with clang
23.1.4: `clang-23 -O2 -S -emit-llvm` on a module whose function `bump`, with the call of `.equiv k, 1`
marked `!srcloc !{i64 4}`, is called from two functions wrote the call into both, each with `!srcloc !0`
and `!0 = !{i64 4}`; `clang-23 -O2 -c` on it wrote `<inline asm>:2:12: error: redefinition of 'k'`, and
`clang-23 -O0 -c` built it. The code generator copies a call too: of a program the compiler emits, whose
`asm` stands in the block after an inner `if`, the module `-O2 -S -emit-llvm` wrote held one call,
`-O2 -c` wrote the same `redefinition of 'k'`, and `-O2 -S -mllvm -stop-after=block-placement` wrote two
`INLINEASM`, each with the call's `!0`, as LLVM's tail duplication copied the block into the two before
it (`llvm/lib/CodeGen/TailDuplicator.cpp` of LLVM 23.1.4, whose `shouldTailDuplicate` keeps no call of
inline assembly from a copy but an `asm goto`, `INLINEASM_BR`); the same for `x86_64-pc-windows-msvc`,
and for a module of IR of that shape for `aarch64-linux-gnu`. Clang given IR prints LLVM's diagnostic and
`cannot compile inline asm` without the call's cookie (`clang/lib/CodeGen/CodeGenAction.cpp` of LLVM
23.1.4), so the copies are read from the code generator's own text. A C function the IR declares,
`declare i32 @abs(i32)`, beside module-level assembly that defines the label `abs:` and a call of it,
built (`clang-23 -O0 -c` and `-O2 -c`), so such a function takes no label beside the copies. A global the IR
declares, `@declared = external global i64` or `@weakly = extern_weak global i64`, beside module-level
assembly that defines the labels `declared:` and `weakly:`, built (`clang-23 -O0 -c`), while the copies'
label `"declared":` beside the same assembly drew `symbol 'declared' is already defined`, so such a
global takes no label beside the copies.

Gate: `compiler-tests`
(`EmitterTests.An_instruction_the_assembler_refuses_is_asm_refused_at_the_line_of_its_asm` for the
order and the label two `asm`s define,
`EmitterTests.A_label_of_a_naked_function_that_is_a_symbol_of_the_program_is_asm_refused_at_the_line_of_its_asm`
for the labels of the program's symbols,
`EmitterTests.Of_two_naked_functions_that_define_one_label_the_build_names_the_one_LLVM_refuses_in_the_order_of_their_calls`
and `EmitterTests.Of_two_asm_statements_that_define_one_symbol_the_build_names_the_one_LLVM_refuses_in_the_order_of_their_functions`
for LLVM's order, `EmitterTests.The_copies_the_build_assembles_define_no_symbol_the_program_does_not`
for the copies' `main`,
`EmitterTests.A_pure_asm_the_assembler_refuses_is_asm_refused_at_its_line_in_a_test_build_and_a_release_build`
for the copy's effect,
`EmitterTests.A_pure_asm_whose_output_nothing_reads_is_asm_refused_in_a_test_build_and_builds_in_a_release_build`
for the release build that drops it,
`EmitterTests.An_asm_the_optimiser_of_a_release_build_copies_is_asm_refused_at_its_line_where_the_assembler_refuses_a_copy`
and `EmitterTests.A_release_build_names_no_asm_its_optimiser_dropped_beside_one_the_assembler_refuses`
for the optimiser's copies and drops,
`EmitterTests.Of_two_asm_statements_at_one_line_a_release_build_copies_each_call_as_its_own_asm`
for the template a copy is found by, and
`EmitterTests.A_global_the_IR_declares_and_does_not_define_takes_no_label_beside_the_copies`
for a declared global,
`EmitterTests.An_asm_the_code_generator_of_a_release_build_copies_is_asm_refused_at_its_line_where_the_assembler_refuses_a_copy`
for the code generator's copies, and
`EmitterTests.A_C_function_the_program_declares_takes_no_label_beside_the_copies`
for a declared C function). Whether a clang of another major reads a `!srcloc` from IR, and writes its
machine IR in the form the build reads, is read by no test:
a `judgment step` where the pinned major changes. Related:
[[a-front-end-option-of-clang-does-not-reach-ir]].
