---
name: the-ir-ends-its-lines-in-crlf-on-windows
description: The emitter writes the IR with `StringBuilder.AppendLine`, which ends a line with `Environment.NewLine`, `\r\n` on Windows; a test that looks for a `\n` after a token in the IR, `\n}\n` for a function's end, passes on Linux and finds nothing on Windows (run 41006528116 of `compiler-windows`, 2026-10-30)
metadata:
  type: reference
---

On 2026-10-30 run 41006528116 of `compiler-windows`, on 02a1573 of pull request 89, failed in two
new tests of `EmitterTests`, `The_push_of_a_list_widens_its_count_without_a_sign` and
`The_helper_of_Add_holds_the_push_and_calls_the_making_of_room_out_of_line`, with 2928 passed. Each
cut a function out of the IR from its `define` to `IndexOf("\n}\n", start)`. The emitter ends every
line with `StringBuilder.AppendLine` (`compiler/Kurz.Compiler/Emit/Ir.cs`), which writes
`Environment.NewLine`: `\n` on Linux, `\r\n` on Windows. There a function ends in `\r\n}\r\n`, the
search gave -1, and the range threw `ArgumentOutOfRangeException` ("length ('-13963') must be a
non-negative value"). The same tests passed on Linux, in every gates run.

The IR is the same program on both: clang reads either line end. What differs is a test's text
search.

**How to apply:** a test that searches the IR for a line end looks for the token with the line end
before it, `\n}`, which both forms hold, or reads the IR after `ReplaceLineEndings("\n")`; never for
a `\n` after a token. The two tests look for `\n}` since. `judgment step`: nothing reads a test's
search for this shape, and only the Windows job, which no merge waits for (HAZARD #30), runs it
where it fails.
