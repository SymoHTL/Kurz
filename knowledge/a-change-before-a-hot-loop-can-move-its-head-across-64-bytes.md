---
name: a-change-before-a-hot-loop-can-move-its-head-across-64-bytes
description: Issue 157's bisect put 20% of the Cyrillic rows of `ToLower` and `ToUpper` on the small-count branches that pull request 197 gave `Copy`, inlined into `UnicodeCase` before its walk; the same machine code moved 16 bytes on took 72.3 ms against 86.2, so the whole gap was where the walk's head stood against 64 bytes, and no instruction of the walk had changed (2026-11-19, clang 23.1.4). A gap between two builds is read as the code's only after the function was moved by 16, 32 and 48 bytes
metadata:
  type: feedback
---

## What happened

Issue 157's sweep found the rows `cyrillic` (`ToLower` of a Cyrillic text) and `upper-cyrillic` (`ToUpper` of it)
about 20% slower than at the head of pull request 167, and the bisect (issue 157, comment 7001076813) found the step
at the merge of pull request 197 into that branch: `Copy`'s branches for up to 16 bytes (two words of 8, two of 4,
three bytes) inlined into `std.UnicodeCase`, where a byte loop stood, and the head of the walk after them moved from
0 to 32 mod 64. It did not say which of the two cost the time. Read on 2026-11-19 (checked):

- `UnicodeCase` calls `Copy` once per call, for the bytes before the first code point that changes, before its walk:
  in `objdump -d` of `main` (c7b162e6), the count tests of `Copy` stand at 0x4620 to 0x4790 and the walk's head at
  0x4830, and no jump of the walk goes into them but its exit to the function's one return. The function's
  instructions are those of the bisect's build of the merge, and in `main`, where pull request 240 aligned every
  function at 64 bytes, the head stands at 48 mod 64.
- The same machine code moved on by nops at the function's entry, measured alone (release builds of the same IR,
  the best of 11 of each side in one run, beside a byte-identical copy of `main`'s binary; issue 157's lowering
  program, clang 23.1.4 in the dev image, an x86-64 processor of 20 CPUs, load average 10.9 before and 11.4 after):

  | the walk's head | cyrillic | upper-cyrillic |
  |---|---|---|
  | 48 mod 64 (`main`, and its copy) | 86.23 and 86.22 ms | 83.93 and 84.89 ms |
  | 48 mod 64 (the same, through assembly) | 85.83 | 84.39 |
  | 0 mod 64 (moved 16 bytes) | 72.33 | 70.97 |
  | 16 mod 64 (moved 32 bytes) | 82.97 | 82.87 |
  | 32 mod 64 (moved 48 bytes) | 86.93 | 85.69 |

  In a second run, 16 bytes on gave -15.0% and -15.8%, and `-falign-loops=64`, which puts every loop's head at 64
  bytes, -16.1% and -16.4%. So `Copy`'s branches cost nothing that shows; the whole gap was the walk's place.
- Forms of the standard library measured against it in one run (checked): the copy as the byte loop it had before
  pull request 197, +3.7% and +0.9%, since the head stands at 32 mod 64 with it; the copy as a call of a function
  that is not inlined, -4.0% and -4.6%; `Copy`'s path for more than 16 bytes made such a call, +0.9% and -0.1%. Each
  moves the head by its own size: none of them is a mechanism, and the next change of the function moves it again.
  `-falign-loops=64`, in one sweep of every row of issue 157 on 2026-11-19 (checked, one run, best of 11), took the
  two rows back by 16.1% and made 42 of 102 rows slower beyond the band of two copies of `main`, by up to 12.2%: no free fix
  either, and a choice of the build's layout, as the alignment of functions of pull request 240 was the owner's.

## What to do

- Before a gap between two builds is read as the code's, move the function that holds the loop by 16, 32 and 48
  bytes and time each beside the unmoved build: where one of them closes the gap, the gap is the loop's place, and
  no change of its code is its cause or its cure.
- The recipe: keep the IR a release build hands clang (a script named `clang-23` first on `PATH` that copies the
  `.ll` it is given, as knowledge/kurz-ir-marks-no-parameter-noundef.md describes), compile it to assembly with the
  release build's options (`-O2 -mllvm -align-all-functions=6 -S`), put `.nops N` after the `.cfi_startproc` of the
  function's label, assemble and link it as the build links (`-O2`, `-lgcc_s -lm`), and check that the unmoved build
  made that way has the instructions of the direct build (an `objdump -d` with addresses, branch targets and nops
  taken out shows no difference).
- Read where the loop's head stands, `objdump -d` of the function and its address mod 64, in both builds of every
  speed change of a function with a hot loop, and name it beside the times.

No CI job runs a benchmark, so nothing goes red when a change skips this: `judgment step`.
