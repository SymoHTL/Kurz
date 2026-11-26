---
name: a-plain-data-type-is-no-block
description: A data type is laid out as a counted block only where it has a base, a type derives from it or it holds a value of its own type; every other one stands in its variable's own place, so a test of code that copies or counts a data block over such a type never reaches that code (issue 646, 2026-11-25)
metadata:
  type: reference
---

## What happened

Issue 646 built a path of `data` fields passed as `mut`: a block on the path that another value holds
is copied first, and one the variable holds alone is written where it stands (M7, `OwnDataBlock` in
`compiler/Kurz.Compiler/Emit/Emitter.Data.cs`). Its fenced line in `ListTests` (`mut-path-fenced`) and
its corpus case passed two paths of one variable, `Two(mut p.A, mut p.B)`, with
`data Pair(List<int> A, List<int> B)`.

A mutation that turned the copy if shared off, `layout.IsBlock(data) && shared` changed to
`layout.IsBlock(data) && false`, so that the second path copies the block the first path's place
stands in and lets go of it, stayed GREEN on every test. `Pair` was no block: `Layout.IsBlock` holds a
data type that has a base, from which a type derives, or that holds a value of its own type (the
summary of `IsBlock` in `compiler/Kurz.Compiler/Emit/Layout.cs`, D7). Every other data type is a
structure in its variable's own place, a path through it is an address in that place, and `held = p`
copies the structure with a count of each list it holds. Neither branch of the block test ran.

With `data Tagged(int Tag) : Pair` beside it, `Pair` is a block. The same program built with the
mutation printed `[1]` for `p.A`, the callee's write lost in the copy the second path let go of, and
`[4, 6]` for `q.A`, a freed block taken by the next one; built without it, `[1, 5]` and `[3, 5]`. The
fenced line and the case gained the derived type before the pull request opened.

## What to do

- A test of emitter code that copies, counts or lets go of a data block uses a type laid out as a
  block: one with a base, one a type derives from, or one that holds itself. A `data` type with none
  of these reaches no block code at all.
- A case that reads both layouts holds one type of each, as `corpus/values/mut-path-argument.kz` does
  with `Pair` and `Config`.
- When a mutation of block code stays GREEN, ask first whether any type of the tests is a block.

Gate: `compiler-tests` runs `mut-path-fenced` and the case with `Pair` laid out as a block. A new test
of block code over a data type that is no block is caught by no rule: a `judgment step` for its author.
