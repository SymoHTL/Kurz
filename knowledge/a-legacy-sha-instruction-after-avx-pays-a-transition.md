---
name: a-legacy-sha-instruction-after-avx-pays-a-transition
description: The SHA instructions have no VEX form; built for a CPU with AVX, SHA-256 of 48 MiB took 594 ms instead of 19 because LLVM cleans the upper halves of the YMM registers only before a call and a return, and a legacy instruction run while they are dirty pays a transition of the register state (pull request 173, 2026-11-01); the emitter cleans them once before a run that a test of `X86.Sha` guards
metadata:
  type: feedback
---

On a CPU with AVX, LLVM writes every vector instruction it can in its VEX form, and its pass that
inserts `vzeroupper` (`X86VZeroUpper.cpp` in LLVM's x86 back end) cleans the upper halves of the YMM
registers before a call and a return alone. The SHA instructions (`sha256rnds2`, `sha256msg1`,
`sha256msg2`, `sha1rnds4` and the others) have no VEX form. A legacy SSE instruction that runs while
the upper halves are dirty pays for them on Intel's cores, by Intel's 64 and IA-32 Architectures
Optimization Reference Manual, its section on mixing AVX code with SSE code: before Skylake a
transition of the whole register state, from Skylake on a dependence on the upper halves and a blend
at every such instruction.

Pull request 173 built the standard library's SHA-256 through `X86.Sha` and measured it built for
`raptorlake`, on one x86-64 machine with SHA and AVX2, a core of that generation: the hash stored its constants through YMM registers just before its rounds, and 48 MiB
took 594 ms, where the same rounds after a `vzeroupper` took 19 ms (2026-11-01). Cleaning at the
entry of every function that runs a guarded run was not enough either: the rounds written as small
functions, which LLVM inlines into one loop, took 36 ms, since the cleaning then sat inside the loop.

The emitter now calls `llvm.x86.avx.vzeroupper` once, before the first statement of a run that a
test of `X86.Sha` guards, where the build's CPU has AVX (`CleanUpperHalves` in
`compiler/Kurz.Compiler/Emit/Emitter.Intrinsics.cs`), and not in a function the run calls. The
shape to look for is any legacy-only instruction, SHA's or a later one without a VEX form, written
where the build's CPU has AVX: measure it built for a CPU with AVX, not for the baseline, where
nothing dirties the upper halves. A 256-bit instruction LLVM writes inside such a run, before the
SHA instruction, is not cleaned after.

`IntrinsicTests.On_a_cpu_with_avx_a_run_a_test_of_sha_guards_cleans_the_upper_halves_once_and_a_function_it_calls_does_not`
and `IntrinsicTests.A_named_cpu_reaches_clang_and_a_guarded_function_carries_its_features_in_front_of_the_blocks`
hold the one call and its place; the time is held by nothing but the measurement.
