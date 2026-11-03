---
name: kurz-ir-marks-no-parameter-noundef
description: The IR the emitter writes marks no parameter `noundef`, where clang's IR of the same function in C marks every one; for MD5's F and G that changes no machine code (clang 23.1.4, probe of 2026-11-02), and the gap pull request 159 measured between two forms of F and G came from G's form alone, while F joined by an or is faster than its two terms added one at a time, the form that change had turned down, and the select OpenSSL's assembly writes is faster than the or (issue 157, 2026-11-03). A reason given for a gap between two forms is a reading until the IR, the machine code and a measurement of each change alone show it
metadata:
  type: reference
---

## What happened

Pull request 159 wrote SHA-256 and MD5 of the standard library in the form of RustCrypto's software
rounds, but for F and G: `md-5`'s `op_f` joins F's two terms, `b & c` and `!b & d`, with an or, and
`op_g` adds G's two terms together first, where the change added each term into the sum one at a
time. The two terms of each share no bit, so every form gives the same number. One run of three sides
on 2026-11-01, release builds in the dev image, the best of five each at a load average of 7.1 before
and 7.8 after, MD5 of the same 16 MiB hashed three times: 53.46 ms with the terms added one at a time,
56.50 ms in `op_f` and `op_g`'s forms, and 52.61 ms in Rust (pull request 159's description); the
spread of the five runs was not kept. The change put the whole gap on F's or, and its cause on
`noundef`, which the emitter writes on no parameter (the word is in no source of the compiler) and
clang's IR of the same function in C writes on every one: with it, the change said, LLVM splits the or
into two adds into the sum. Neither had a probe behind it, and the review of pull request 170 found
both. The probe and the measurement below refute both.

## The probe, 2026-11-02

Clang 23.1.4 at `-O2`, the level `kurz build --release` passes. The IR of `kurz build --release` was
kept by a script named `clang-23`, first on `PATH`, that copies the `.ll` file it is given and runs the
real clang; the command has no option that keeps it.

- F and G alone, each in both forms, as functions of a program, and the same IR with `noundef` on
  every parameter: `noundef` marks F's or `disjoint` and changes nothing else. The machine code is the
  same with it and without it. LLVM splits no or into adds: it writes F's or as `((c ^ d) & b) ^ d`,
  and it turns G's two terms added together first into an or too.
- MD5's block (`HashBlock`, into which every step is inlined), in std's form and in `op_f` and
  `op_g`'s forms, those also with `noundef` on the parameters of `Md5F` and `Md5G`: `noundef` raises
  the ors marked `disjoint` in the optimised IR from 41 to 73, and the machine code stays the same,
  byte for byte. The two forms give 699 and 646 instructions.
- A program that hashes 16 MiB 20 times, of which filling the 16 MiB takes 17 to 36 ms (the same
  program with no hash, 10 runs at a load average of 14.2), built in each
  form of F and G with the compiler of `main` at 2050e82. The three forms ran in turn, their order
  rotated every round, 21 runs each, at a load average of 7.3 before and 7.0 after on 20 CPUs:

  | F | G | median | range |
  |---|---|---|---|
  | each term added into the sum | each term added into the sum | 377 ms | 374 to 384 ms |
  | its two terms joined by an or | each term added into the sum | 368 ms | 365 to 377 ms |
  | its two terms joined by an or | its two terms added together first | 397 ms | 395 to 402 ms |

  The first two again, alternating, 40 runs each at a load average of 6.6 before and 6.4 after: 377
  ms (373 to 408) against 369 ms (364 to 388). Of the 40 rounds, each the two runs back to back,
  F's or was the faster in 37, the two took the same time in one, and std's form was the faster in
  two.

So the gap came from G's form, and F's or is the faster of these two forms of F. A third form, the
select OpenSSL's x86-64 assembly writes, `((c ^ d) & b) ^ d`, is faster than the or: 51.76 ms against
52.14 ms for MD5 of 16 MiB hashed three times, the best of 9, and 51.77 against 52.09 ms in a run of
the two alone, the best of 15 (issue 157, 2026-11-03, comment 6594121046). In that run each of the two
ran beside a byte-identical copy of its binary: the or took 52.09 and 52.10 ms, the select 51.77 and
51.78 ms, a gap of 0.32 ms between the two against 0.01 ms between two copies of one binary. std
writes F so, and the
comment on `Md5F` gives the numbers. Of the or, LLVM (clang 23) makes the select in 14 of F's 16
steps and keeps the or, with a `not`, in the last two (the same comment). Why the other forms cost
what they do is not shown; `noundef` is not it, since the machine code does not change with it.

## What to do

- A reason given for a gap between two forms is what a probe shows: the IR the build hands clang,
  what clang makes of it at the build's level, and the machine code, with the clang release and the
  date. Without that, the text calls it a reading.
- Measure each change alone. A run that changes two forms at once cannot say which one the gap comes
  from.
- Measure the forms side by side, in turn, and keep the spread; no test tells them apart, since all
  give the same number.
- The reason goes beside the code, as the comment on `Md5F` does, and this entry names it.
