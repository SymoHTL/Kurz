---
name: a-word-of-bytes-is-read-at-any-address
description: A read through a Ptr<ulong> is a load LLVM takes to be at a multiple of 8 (the emitter writes no align, which LLVM reads as the ABI alignment of i64), and a key's bytes stand at any address, so std reads such a word byte by byte and LLVM joins the bytes into one load (WordAt); and a test of a word made from bytes holds bytes of 128 and more, since with none a byte widened with its sign passed every test (pull request 161, 2026-11-01)
metadata:
  type: reference
---

## What happened

Pull request 161 made SipHash-1-3 read a map key's bytes a word of 8 at a time, and the runtime's
`Same` compare two strings 8 bytes at a time. It met two traps, both caught before the merge.

1. **The address.** The emitter writes a read through a pointer, `*Ptr<ulong>(p)`, as
   `load i64, ptr %p` with no `align` (`Emitter.Expressions.cs`, `EmitUnary`, "R10: the read
   through a pointer"). LLVM's reference says of a load: "An omitted `align` argument means that the
   operation has the ABI alignment for the target", 8 for an `i64` on x86-64 and AArch64, and
   "Overestimating the alignment results in undefined behavior" (`llvm/docs/LangRef.md` at the tag
   llvmorg-23.1.4, read 2026-11-01). A key's bytes stand at any address: a piece of a literal is its
   bytes in place, at any offset in the literal. So `std.WordAt` reads the 8 bytes one by one and
   puts them together, the first lowest, a pattern LLVM joins into one load with no alignment assumed;
   `QuarterAt` does so for 4 bytes.
2. **The test.** The first tests of the word reads ran on bytes of which none was 128 or more. A
   mutation that widened `WordAt`'s first byte with its sign, which sets every bit above that byte
   where the byte is 128 or more, passed all three tests, and so did one that widened `SipTail`'s last
   byte with its sign. The bytes (i * 37 + 200) % 256, 14 of the first 24 of them 128 or more, turned
   both red.

## What to do

- A word read from bytes at an address nobody made a multiple of 8 goes through `std.WordAt` (or
  `QuarterAt`); a read through a `Ptr<ulong>` is for an address that is one, as a loop that first steps
  byte by byte to such an address reads (`Utf8Invalid` in the runtime).
- What LLVM made of it, in an executable of `kurz build --release`: `objdump -d --no-show-raw-insn`
  shows one `mov` of 8 bytes where the bytes were joined, and eight byte loads where they were not.
- A test of code that widens bytes into a word holds bytes of 128 and more, and the mutation list
  holds the widening of a byte with its sign.
