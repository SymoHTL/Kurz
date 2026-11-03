---
name: the-code-generator-adds-a-sums-constant-last
description: LLVM's code generator moves the constant of a sum to the sum's last add, whatever order the source and the optimised IR give it. Each step of MD5 (`Md5F` to `Md5I` in `std/std.kz`) adds its constant t after the round's function, which puts one add more on the path from b to the next step than in OpenSSL's assembly, whose `lea` adds the constant off that path. A probe that hid the constants from LLVM took 46.80 ms where std's form took 51.79 (issue 157, 2026-11-03), and no order of the source changes where the constant lands
metadata:
  type: reference
---

## What happened

Pull request 258 took MD5's F as OpenSSL's select. After it, MD5 of 16 MiB hashed three times took
51.84 ms in the bytes bench of issue 157. .NET took 44.57 ms; it runs OpenSSL's x86-64 assembly,
`md5-x86_64.pl`. The review of pull request 258 asked for the gap that is left to be recorded here.

The probe, on 2026-11-03:

- **The build.** The bench's `hashes` program was built from `main` at 59facd3. Its IR was kept by
  a script named `clang-23`, first on `PATH`, that copies the `.ll` file it is handed and runs the
  real clang 23.1.4. Each form below was then compiled with clang 23.1.4 at `-O2`, the level
  `kurz build --release` passes.
- **The optimised IR adds the constant first.** `clang-23 -O2 -S -emit-llvm` adds the constant to
  the word, then a, then F: `add i32 %m, -680876936`, `add` of a, `add` of F. The sum of a, m and t
  is ready before F, and F's add is the last.
- **The machine code adds it after F.** `clang-23 -O2 -S` of the same IR adds m and a with
  `leal (%r10,%r8), %ecx` and adds that to F, `addl %ecx, %eax`. Only then does it add the
  constant, `addl $-680876936, %eax`, before `roll $7`.
- **What that costs.** The path from b to the next step is and, xor, add, add, rol and add: 6
  operations. OpenSSL's step takes 5, because its `lea` adds the constant into a and m off that
  path: `round1_step` of `crypto/md5/asm/md5-x86_64.pl` at `openssl-3.5.0` writes
  `lea $T_i($dst,%r10d),$dst` beside the select, then `add %r11d,$dst`, `rol` and `add $x,$dst`.
- **A fold that can move it.** The DAG combiner holds such a fold, in
  `llvm/lib/CodeGen/SelectionDAG/DAGCombiner.cpp` at `llvmorg-23.1.4`, line 1261:
  "Reassociate: (op (op x, c1), y) -> (op (op x, y), c1) iff (op x, c1) has one use". Each step's
  sum with its constant has one use, so the fold applies there. That it is this fold that moves the
  constant is a reading of the IR and the machine code: no trace of the combiner was run, since a
  release build of LLVM prints none.

Every order of the source measured gives the same time: H and I with a + m + t first, G's terms in
the other order, and each word of the block read where its step uses it. They took 51.76 to 51.85
ms (issue 157, comment 6594121046).

A probe hid each constant from LLVM. It joined each constant by an xor with a word LLVM cannot know
is 0: the top bit of the hasher's count of bytes. In the same run, that took 46.80 ms where the form
#258 took into std took 51.79, which is 1.05x of .NET's 44.57. It was not taken into std, for two
reasons:

- the probe's zero rests on the count staying below 2^63 bytes, and any such zero rests on what LLVM
  can see, which any release of LLVM can change;
- a zero that holds by construction would need a word of its own in the hasher's state, a change of
  its layout only to carry a trick.

That was the call of the session that drives this repository's pipeline, on 2026-11-03, for #258.
The owner was not asked.

## What to do

- Do not measure orders of the source against this. A constant in a sum on a hot path is added
  last by the code generator, whatever the source's order. Read where it lands in the machine code,
  not in the IR: the IR adds it first. The same holds for an option of clang
  ([[a-front-end-option-of-clang-does-not-reach-ir]]). `judgment step`: no job runs a benchmark or
  reads the machine code of std.
- Use an opaque value to keep a constant off the path. The clean way is a value the optimiser
  cannot see through: in Kurz `Hint.BlackBox(t)` (F18), Rust's `core::hint::black_box`, which the
  owner chose on 2026-11-03 after this probe (issue 157), and in C an empty `asm` barrier. With it
  at each of MD5's 64 constants, MD5 took 42.60 ms where std's form took 51.78 (issue 157, the best
  of 15). `judgment step`.
- Whether std may hide a constant behind a value of the program that LLVM cannot know, such as a
  word of the state or a count, is open. Its correctness rests on the program's state, and its
  speed on what LLVM happens not to see. It was not taken for #258 (above), and the owner has not
  been asked. `judgment step`.
