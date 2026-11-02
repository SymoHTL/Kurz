---
name: a-front-end-option-of-clang-does-not-reach-ir
description: clang's options of code generation that its C front end reads, `-falign-functions=64` among them, do nothing to a module of LLVM IR that clang compiles, since IR does not pass through that front end; seen on 2026-11-02, when `-falign-functions=64` left every function of the IR at LLVM's `.prefalign 4` and `-mllvm -align-all-functions=6` put `.p2align 6` on all of them, so the compiler hands LLVM's own option through `-mllvm` and a test reads the assembly, not the argument.
metadata:
  type: reference
---

Seen on 2026-11-02, while building the alignment of a release build, which the owner chose on
issue 156 (comment 6580739215, "functions at 64"). The compiler hands clang a module of LLVM IR
(`Clang.Build` in `compiler/Kurz.Compiler/Build/Clang.cs`). clang reads an option such as
`-falign-functions=64` in its front end, which turns C into IR and sets the alignment on each
function it writes. A `.ll` file skips that front end, so the option reaches nothing, and clang
gives no warning.

What `clang-23 -O2 -S` wrote for one module of 40 functions, with each option (the alignment
directives above the functions):

- no option: `.prefalign 4` on all 40, LLVM's preference of 16 bytes;
- `-falign-functions=64`: `.prefalign 4` on all 40, the same as with no option;
- `-mllvm -align-all-functions=6`: `.p2align 6` on all 40.

`-mllvm` hands an option to LLVM's code generator, which compiles the IR, so it reaches every
function. A pass of the build that is wanted for IR is therefore named through `-mllvm`, under
LLVM's name for it, or set in the IR itself. A front-end option's effect on C says nothing about
its effect here.

A test that only pins the argument string cannot tell the two options apart: both are in the
list, and only one aligns anything. The test of the release build reads what LLVM wrote instead.
`ClangTests.A_release_build_aligns_every_function_at_64_bytes_and_a_test_build_leaves_llvms_alignment`
builds a module to assembly with the arguments `Clang.Arguments` gives, finds every function's
label and reads the directive above it. Gate: `compiler-tests`, for the alignment option. Any
other option of the front end handed for IR is a `judgment step`.
