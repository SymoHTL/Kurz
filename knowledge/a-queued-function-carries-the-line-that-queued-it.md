---
name: a-queued-function-carries-the-line-that-queued-it
description: The emitter emits a lambda, a class's defaults and a constructor from their queues after the function that made or first needed them, and they took the line of the function emitted before them; once pull request 579 queued functions of the runtime at line 0 after the program's last statement, a streamed response's lambda was emitted at line 0 and ten tests of the Windows job ended in the compiler's own error (run 44295220997); each now carries the statement it was queued at, and a test of a line puts its target on a line nothing else the program emits stands on, since the mutation that kept the line before the lambda passed twice on a line that happened to be the lambda's own
metadata:
  type: feedback
---

The emitter does not emit a lambda, the defaults of a class's body or a constructor where the
program makes or first needs them: it queues them and emits each after the function it was queued
in (`Emitter.cs`, `Emitter.Lambdas.cs`, `Emitter.Classes.cs`). The line the emitter stands on while it
emits a function is what a refusal of the build names: a build for Windows says `not supported` at
the statement whose code reaches a call on a socket (S11, W1), and the code a lambda's body reaches is
emitted with the lambda.

Before pull request 579 a queued lambda, class's defaults and constructor took the line of the
function emitted before them. Pull request 579 added functions to the runtime that are queued at
line 0 after the program's last statement, so that on its first head a streamed response's lambda
was emitted at line 0, which no refusal can name: the build ended in the compiler's own error,
"std/std.kz:9069: the emitter does not lay out a call on a file or a socket", in ten tests of the
Windows job (run 44295220997, comment 6978086731). Since 579's head `3554b1a0` each carries the
program's statement it was queued at, as a queued function of the program does.

The trap in the test: the mutation that keeps the line before the lambda (`origin = lambda.Origin`
put back) passed twice in the theory's first row, the program of the Windows job's failure, since the
function emitted just before the lambda, an `std/` class's defaults emitted at their own line, stood
on the lambda's own line. The row `lambda` puts a lambda whose body alone reaches `std/` at a line no
other part of the program stands on, and only that row turned the mutation red. A test of the line a
construct is emitted at puts its target on a line that nothing else the program emits uses, so that
the line of a neighbour cannot pass for it.

`HttpResponseTests.A_build_for_windows_says_not_supported_at_the_statement_that_made_a_lambda_whose_write_reaches_a_socket`
holds the five rows: `first`, `lambda`, `capture`, `default` and `constructor`, each red under the
mutation of its own enqueue or emission (579's description, "Tests").
