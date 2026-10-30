---
name: a-probe-under-the-fence-is-a-command
description: The test assembly of the compiler is a command that builds a Kurz program from a file and runs it, with --fenced under the allocator that makes every block it frees inaccessible, the suites' own; a session probes freed memory through it instead of writing a throwaway test that edits the runtime a second time (eight such tests on 2026-10-29 and 2026-10-30 made it the command)
metadata:
  type: reference
---

## What happened

A construct that borrows a list or a string is held to "no block is read or written after it is
freed" by a fenced program of `ListTests` ([[a-borrow-outlives-its-source]]): the allocator of
`std/` with every freed block made inaccessible, `ListTests.Fenced`, the one definition of that
edit. A session that wanted to know whether a program of its own touched a freed block had no way
to run it under that allocator but a test: on 2026-10-29 and 2026-10-30 a session wrote eight
throwaway tests that each built one program with `ListTests.Fenced` and printed what it did, and
deleted them again. The third-time rule makes that a command.

## How to apply

1. Build the suites (`compiler/README.md`, "Building and testing"); the run leaves
   `Kurz.Compiler.Tests/bin/Release/net10.0/Kurz.Compiler.Tests.dll` under `compiler/`.
2. From inside `compiler/`:
   `dotnet Kurz.Compiler.Tests/bin/Release/net10.0/Kurz.Compiler.Tests.dll <file> --fenced`,
   with `--grant <name>` for each grant the program needs (`raw` for a `raw` block). It prints
   what the program wrote, what it wrote on its error stream and `exit <code>`.
3. Read the code: a read or a write of a freed block ends the program on the system's signal,
   `exit 139` on Linux (128 and SIGSEGV), an access violation on Windows; a clean program prints
   what it prints without the flag. Run it again without `--fenced` as the control: a program that
   ends the same way there fails for another reason.
4. The command's own exit code is 0 where the program was built and ran, whatever the program's,
   1 where the program has errors, which it prints, and 2 for an argument it does not read.

A program that a probe found at fault becomes a line of a fenced program of `ListTests`: the probe
answers a question, the suite holds the answer.

Gate: `compiler-tests` (`ProbeTests`: a clean program with and without the fence, a read through a
list's pointer after its block was freed that ends the program under the fence and prints the
value without it, a program's errors, and a command line the probe does not read).
