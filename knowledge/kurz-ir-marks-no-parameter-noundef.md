---
name: kurz-ir-marks-no-parameter-noundef
description: The IR the emitter writes marks no parameter `noundef`, where clang's IR of the same function in C marks every one, and LLVM leaves a form as written where it would need to know that a value holds no undefined bits: MD5's F with its two terms joined by an or, as RFC 1321 and `md-5`'s `op_f` write it, kept the or in the sum, one operation longer on the path from b to the next step, and took 56.50 ms where the same terms added one at a time took 53.46 ms (pull request 159, 2026-11-01); a hot function of the standard library is written in the form LLVM gives where it knows `noundef`
metadata:
  type: reference
---

## What happened

Pull request 159 wrote SHA-256 and MD5 of the standard library in the form of RustCrypto's software
rounds. `md-5`'s `op_f` joins F's two terms, `b & c` and `!b & d`, with an or, and `op_g` adds
G's two first. The two terms share no bit, so the or, the add and two adds into the sum one at a
time give the same number. Clang's IR of the same function in C marks every parameter `noundef`,
which tells LLVM that the value holds no undefined bits, and with it LLVM splits the or into two
adds into the sum. The emitter of Kurz writes no `noundef` (the word is in no source of the
compiler), so LLVM kept the or of the two terms in the sum, one operation more on the path from b to
the next step.

Measured in one run of three sides, release builds (`kurz build --release`, `-O2`) in the dev image,
the best of five each at a load average of 7.1 before and 7.8 after: MD5 of the same 16 MiB hashed
three times took 53.46 ms with the two terms added into the sum one at a time, 56.50 ms with them
joined first as `op_f` and `op_g` write them, and 52.61 ms in Rust (pull request 159's
description). The comment on `Md5F` in `std/std.kz` holds the form and the reason for it.

## What to do

- Where a function of the standard library on a hot path follows a form of Rust or C, read whether
  what LLVM makes of that form rests on `noundef`, as the split of an or of two terms that share no
  bit does, and write the form LLVM makes of it where it knows `noundef`: here, the terms added one
  at a time.
- Measure the two forms side by side, as above; no test tells them apart, since both give the same
  number.
- The reason goes beside the code, as the comment on `Md5F` does, and this entry names it.
