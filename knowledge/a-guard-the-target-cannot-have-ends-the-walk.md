---
name: a-guard-the-target-cannot-have-ends-the-walk
description: On a 64-bit Arm build, a run that a test of an x86 class guards behind another condition (`if n > 2 && X86.Sse41.IsSupported`, the standard library's SHA-256) sent the emitter's walk of a block into itself until the compiler's stack overflowed, and the right side of `&&` after such a test was compiled with x86 features into the Arm module (pull request 173, 2026-11-02); a guard the target cannot have is a constant `false` that ends the walk, read before any guarded function is made
metadata:
  type: feedback
---

The emitter compiles the rest of a block from the first statement a test guards as a function of
its own, for the sets that statement's guards name and the function lacks (`EmitStatements` and
`EmitGuarded` in `compiler/Kurz.Compiler/Emit/Emitter.Intrinsics.cs`). The guarded function's body
is the same run of statements, walked again, where the sets are now known `true`, so the walk
does not start a function again.

On a 64-bit Arm machine a test of a class under `X86` is a constant `false`, which no function can
make `true`. The guard of a run behind another condition, `if n > 2 && X86.Sse41.IsSupported`, is not
answered by the test alone, so the walk saw a guard the function lacked, started a guarded function
for it, and in that function saw the same guard, still not `true`: it recursed until the compiler's
own stack overflowed. The right side of `&&` after such a test was compiled as a function with x86's
`target-features` inside an Arm module. The standard library's SHA-256 has such a run, so every
program that called it failed to build for Arm. The test of the SIMD search found it while building
for Arm (2026-11-02); no suite runs on an Arm machine.

Since the fix a run starts a guarded function only where every guard is unknown
(`guards.All(c => Known(c) is null)`), and a right side whose guards hold one the target cannot
have is never reached (`guards.Any(c => Known(c) == false)`). The shape to look for is a walk that
makes a function for a condition and walks the same nodes inside it: it ends only where the
function changes what the condition reads, so a condition no function can change has to be read
first, as a constant.

`IntrinsicTests.On_an_arm_machine_a_run_and_a_right_side_that_a_test_of_an_x86_class_guards_are_never_reached`
builds both shapes and the standard library's SHA-256 for Arm, with the same program on x86-64 as
the positive control.
