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
of that round's leftovers. The test that holds the corpus to the rule found eight more, each a case
of `corpus/http/` that forks its server, `client-get.kz` among them: each declared `waitpid` with a
`long` and called it with `0`, reaching no call of the runtime's, and each declares it with the
`int*` and calls it with `null` since the batch of pull request 674's leftovers.

A program, a case among them, declares a C function that the runtime declares with the types the
runtime gives it: its declarations stand in the `extern "C"` blocks of `runtime/linux/linux.kz` and
of each architecture's file (`runtime/linux/x86-64/x86-64.kz`, `runtime/linux/arm64/arm64.kz`).

Gate: `compiler-tests`
(`CorpusTests.A_case_declares_a_c_function_as_the_runtime_and_the_standard_library_declare_it`, which
holds every declaration in a case's `extern "C"` blocks of a function that `runtime/` or `std/`
declares to one of their declarations of it, with a floor of ten such declarations, and leaves out a
case that expects a compile error, which is never emitted); a program outside the corpus builds where
it reaches no call of the runtime's, and clang refuses it where it does: `judgment step`.
