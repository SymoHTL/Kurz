---
name: a-line-only-unit-writes-no-subprogram-on-linux
description: on Linux, LLVM writes the subprogram of a unit of line tables alone (clang's -gline-tables-only form, as the emitter writes it) into DWARF only for a function that holds an inlined call and, as an abstract entry, for one that was inlined, so an unoptimised build has no subprogram entry and a function's name comes from the symbol table alone (probed 2026-10-29, clang 23.1.4, GNU readelf 2.42)
metadata:
  type: reference
---

## What happened

The emitter gives every function of the program, the runtime and the standard library a subprogram
in the debug information, under the name a trace shows (`compiler/README.md`). On Windows CodeView
keeps those names in the PDB. On Linux a reader of the trace looked for them in `.debug_info` and
found none.

A probe of 2026-10-29 in the dev image (clang 23.1.4, GNU readelf 2.42) built the same kind of
program twice, counted with `readelf --debug-dump=info <executable> | grep -c <tag>`:

| build | `DW_TAG_compile_unit` | `DW_TAG_subprogram` | `DW_TAG_inlined_subroutine` |
|---|---|---|---|
| test build, unoptimised | 1 | 0 | 0 |
| release build with `--lines` | 1 | 47 | 190 |

In the release build the subprograms are the abstract entries of the inlined functions, `Half` and
`Twice` with `DW_AT_inline`, and the functions that hold inlined calls; a function into which
nothing was inlined has none in either build. That is LLVM's handling of a unit whose emission kind
is line tables alone.

## What to do

- On Linux a frame's name comes from the symbol table, `.symtab`, never from `.debug_info`: the
  emitter writes each function's symbol so that it carries the shown name, `f.Max<int>.8`, and the
  runtime reads it back (design record, section 6).
- `.debug_info` serves the inlined calls alone: a `DW_TAG_inlined_subroutine` names its function
  through `DW_AT_abstract_origin`, whose subprogram holds the shown name.
- An executable stripped of its symbols (`strip -s`) has no names left on Linux: its frames are
  written as `<unknown>` with their addresses.
