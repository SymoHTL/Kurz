---
name: a-change-before-a-hot-loop-can-move-its-head-across-64-bytes
description: Issue 157's bisect put 20% of the Cyrillic rows of `ToLower` and `ToUpper` on the small-count branches that pull request 197 gave `Copy`, inlined into `UnicodeCase` before its walk; the same machine code moved 16 bytes on, by nops at its entry that ran on every call, took 72.3 ms against 86.2, no instruction of the walk changed, so the gap was the function's place (read as the walk's head against 64 bytes, inferred) (2026-11-19, clang 23.1.4). A gap between two builds is read as the code's only after the function was moved by 16, 32 and 48 bytes
metadata:
  type: feedback
---

## What happened

Issue 157's sweep found the rows `cyrillic` (`ToLower` of a Cyrillic text) and `upper-cyrillic` (`ToUpper` of it)
about 20% slower than at the head of pull request 167, and the bisect (issue 157, comment 7001076813) put the gap
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
  bytes, -16.1% and -16.4%. So the gap goes with where the function stands, not with `Copy`'s branches, which the moved builds
  all hold. That the cause is the head of the walk against 64 bytes is a reading (inferred): a move of the function
  moves the walk's inner blocks as well, and no hardware counter was read (none in the container). The table's builds
  put the nops after the function's `.cfi_startproc`, where they run on every call, and moved that one function;
  with 16 bytes of nops before the label of every function of the program instead, where they never run, so that
  every function moved 16 bytes within its 64, `cyrillic` took -15.5% and -15.6% and `upper-cyrillic` -15.5% in each
  of two more runs (checked, issue 157, comment 7004204396).
- Forms of the standard library measured against it in one run (checked): the copy as the byte loop it had before
  pull request 197, +3.7% and +0.9%, the head at 32 mod 64 with it; the copy as a call of a function
  that is not inlined, -4.0% and -4.6%, with the walk's head at 0 mod 64 but the function shorter and the walk's
  inner blocks moved as well, where the shift of 16 bytes that put the head at 0 mod 64 took 16%, so the head
  alone does not set the time; `Copy`'s path for more than 16 bytes made such a call, +0.9% and -0.1%. Each
  moves the head by its own size (inferred): none of them is a mechanism, and any change of the function moves it again.
  `-falign-loops=64`, in a sweep of every row of issue 157 on 2026-11-19 (checked, a run of its own, neither of the
  two runs of the shifts, best of 11), took `cyrillic` from 84.69 to 71.08 ms and `upper-cyrillic` from 83.83 to
  70.35 ms, -16.1% each, and made 42 of 102 rows slower beyond the band of two copies of `main`, by up to 12.2%: no free fix
  either, and a choice of the build's layout, as the alignment of functions of pull request 240 was the owner's. The
  owner took it on 2026-11-20 (issue 157, comment 7051500158, S2): a release build aligns every loop's head at 64 bytes.
- Every row of issue 157, timed with its program moved in those ways (checked, 2026-11-19, issue 157, comment 7004204396):
  `main` ad2239c9 and six layouts of its instructions, every function moved by 64, 256 or 4096 bytes or by 16, 32 or
  48 bytes within its 64, in two runs of 102 and 101 rows, best of 11; the 102nd row, `crc`'s one, timed in the
  first run alone, is read from that run. Two copies of `main` differed by 0.7% and 0.5% at the median row; the
  largest change one layout made in both runs the same way was 1.8% at the median, over 2% on 49 of the 102 rows and
  up to 33.5% (`parse-ulong`). The moves within 64 bytes made most of it: over 5% on 48 of 303 pairs of a row and a
  layout, against 6 of 303 for whole functions moved by 64 to 4096 bytes. The noise of "no row slower beyond noise"
  is the larger of the band of two copies and the row's own layout band, the largest change any of the six layouts
  makes to the row, both measured in the same run as the change (the owner, 2026-11-20, issue 157, comment
  7051500158, S3, option 1, against the band of two copies alone and against a fixed share with a repeat, at the
  cost of the six layouts timed in every sweep and a slowdown within a row's layout band unseen on that row).

## What to do

- Before a gap between two builds is read as the code's, move the function that holds the loop by 16, 32 and 48
  bytes and time each beside the unmoved build: where one of them closes the gap, the gap goes with the function's
  place, and a change of its code that only moves it is no cure.
- The recipe: keep the IR a release build hands clang (a script named `clang-23` first on `PATH` that copies the
  `.ll` it is given, as knowledge/kurz-ir-marks-no-parameter-noundef.md describes), compile it to assembly with the
  release build's options (`-O2 -mllvm -align-all-functions=6 -S`, `Clang.Release` in
  `compiler/Kurz.Compiler/Build/Clang.cs`, read at ad2239c9 on 2026-11-19, and `-falign-loops=64` with them since the
  owner's answer S2 of 2026-11-20), put `.nops N` just before the function's
  label, after its `.p2align 6`, where they never run (after `.cfi_startproc` they run on every call), assemble it
  with clang's assembler and the feature `+fast-15bytenop` (`clang-23 -cc1as -triple x86_64-pc-linux-gnu -filetype
  obj -target-cpu x86-64 -target-feature +fast-15bytenop -mrelocation-model pic`), without which the assembler pads
  an alignment with nops of at most 10 bytes where the direct build's are of 15 (clang 23.1.4, seen on 2026-11-19:
  the `.text` of the unmoved build made without it and of the direct build, compared in hex, differed in
  padding nops alone, every instruction the same and at the same address), link it as the build links
  (`clang-23 -O2`, `-lgcc_s -lm`: `Clang.Arguments` in the same file passes each library the C functions of a
  program come from (R16) as `-l`, `gcc_s` named by the runtime's `extern "C" in "gcc_s"` in
  `runtime/linux/linux.kz`, and adds `-lm` on Linux), and check that
  the unmoved build made that way has the instructions of the direct build (an `objdump -d` with addresses, branch
  targets and nops taken out shows no difference) and, with that feature, the same bytes in `.text`.
- Read where the loop's head stands, `objdump -d` of the function and its address mod 64, in both builds of every
  speed change of a function with a hot loop, and name it beside the times.

On 2026-11-19, at ad2239c9, no workflow of `.github/workflows/` runs a benchmark, so nothing goes red when a change
skips this: `judgment step`.
