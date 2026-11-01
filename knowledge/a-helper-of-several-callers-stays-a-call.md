---
name: a-helper-of-several-callers-stays-a-call
description: LLVM takes a bonus of 15000 off the cost of the sole call of a function of local linkage, against a threshold of 225, so such a function goes into its one caller at nearly any size, and a helper that holds that call grows past the threshold and stays a call at each of its several callers; a cheap case at its top saves no call, and is tested at the call site instead (measured 2026-11-01, clang 23.1.4)
metadata:
  type: reference
---

## What happened

A change of `UnicodeCase` (X3) gave the final sigma a helper of its own, `CaseOf`, which three walks
call: the one that looks for the first code point that changes, the one that writes, and
`CaseWalk`. `CaseOf` held the one call of `CaseMapped`, which tested the ASCII letters first and
went on to two binary searches. In one run, a release build of a program that lowers a text of
ASCII alone, 1.3 million bytes 50 times, took 115 ms with the change and 136 ms with the compiler
before it, the best of five each in the dev image, whose `kurz --version --verbose` names clang
23.1.4 at `/usr/bin/clang-23`.

`nm` of the executable listed `std.CaseOf` as a function of its own and no `CaseMapped`, whose one
caller is `CaseOf`, so its body was in `CaseOf`; `objdump -d` counted three calls of `CaseOf`. Every
code point, ASCII too, was a call with its two out-parameters in memory. LLVM's inliner takes
`LastCallToStaticBonus` off the cost of a call where `isSoleCallToLocalFunction` holds, a callee of
local linkage with one live use (`llvm/lib/Analysis/InlineCost.cpp`), 15000 by default
(`getInliningLastCallToStaticBonus` in `llvm/include/llvm/Analysis/TargetTransformInfoImpl.h`),
against an `inline-threshold` of 225; both read on LLVM's main branch on 2026-11-01. The emitter
writes every function `internal` (`Emitter.cs`). `CaseOf`, with three callers and no bonus, stayed
over the threshold.

## What to do

- A cheap case is tested at the call site, before the call. The walks test `c < 128` and take
  `CaseAscii`, and `c < 256` and take `CaseLatin`, each small enough to be inlined at every caller,
  and call `CaseOf` only above Latin-1, as Rust's `conversions::to_lower` and `to_upper` map Latin-1
  without their table (`library/core/src/unicode/unicode_data.rs` on Rust's master branch, read
  2026-11-01). With the ASCII test alone, a later run gave 75 ms for the same program against 126 ms
  before the change.
- What stayed a call, in an executable of `kurz build --release`: `nm <executable> | grep <name>`
  lists a function the optimiser kept, and
  `objdump -d --no-show-raw-insn <executable> | grep -c "call.*<name>"` counts its calls. A function
  inlined at every caller has no symbol.
