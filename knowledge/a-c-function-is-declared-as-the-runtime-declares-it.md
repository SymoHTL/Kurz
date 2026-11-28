---
name: a-c-function-is-declared-as-the-runtime-declares-it
description: The emitter declares a C function at its first call by the text of the declaration, one text once, so that a program whose `extern "C"` block gives a function the runtime declares another type of a parameter builds two declarations of one name wherever it reaches the runtime's call too, which clang refuses; `park-file-fork-wait.kz` declared `waitpid` with a `long` where the runtime has an `int*` and was refused so in a run of pull request 665's suite, while `park-file-fork.kz`, which reaches no call of the runtime's `waitpid`, built with the same line
metadata:
  type: feedback
---

A call of a C function declared in an `extern "C"` block is emitted with its declaration, written
from the function's types (`EmitCFunctionCall` in `compiler/Kurz.Compiler/Emit/Emitter.Calls.cs`),
and the module keeps a declaration once for each text it is given (`Declare` in
`compiler/Kurz.Compiler/Emit/Ir.cs`). The runtime is compiled with every program and declares its C
functions the same way, so that a program that declares one of them again with the same types adds
nothing, and a program that gives it another type adds a second declaration of the same name, which
clang refuses.

The second declaration is emitted only where both calls are: the runtime's call of a function is
emitted where the program reaches the runtime's code that makes it. `park-file-fork-wait.kz`
declared `waitpid(int pid, long status, int options)`, where `runtime/linux/linux.kz` declares
`waitpid(int pid, int* status, int options)`, and its `Command.New("mkfifo")...Status()` reaches
the runtime's wait for the child: a run of the whole suite on the tree of pull request 665's commit
`2c29ecc0` with that line failed the case's two rows, which clang refused as a second definition
(the description of pull request 665). The same line in `park-file-fork.kz`, which calls no
`Command` and no `Spawn`, built and ran, where nothing showed the difference, and the review of
pull request 665 found it there. Both cases declare `waitpid` as the runtime does since the batch
of that round's leftovers.

A program, a case among them, declares a C function that the runtime declares with the types the
runtime gives it: its declarations stand in the `extern "C"` blocks of `runtime/linux/linux.kz` and
of each architecture's file (`runtime/linux/x86-64/x86-64.kz`, `runtime/linux/arm64/arm64.kz`).

Gate: `compiler-tests` for a case that reaches the runtime's call too, since clang refuses the
module; a declaration that differs from the runtime's in a case that reaches no call of it builds,
and is read by the review rule "corpus": `judgment step`.
