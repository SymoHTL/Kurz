---
name: a-probe-under-the-fence-is-a-command
description: The test assembly of the compiler is a command that builds a Kurz program from a file and runs it, with --fenced under the allocator that makes every block it frees inaccessible, the suites' own; a session probes freed memory through it instead of writing a throwaway test that builds its program with ListTests.Fenced (eight such tests on 2026-10-29 and 2026-10-30 made it the command)
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
3. Read the code: a read or a write of a freed block, and a write past the end of a block's size rounded up to 16, end the
   program on the system's signal,
   `exit 139` on Linux (128 and SIGSEGV), and on Windows a code other than 0 and 1, which no run recorded; a `Free` given another
   size than its block was allocated with ends it so after the line `fence: Free was given a size other than its block was
   allocated with` on its error stream; a clean program prints
   what it prints without the flag. Run it again without `--fenced` as the control: a program that
   ends the same way there fails for another reason.
4. The command's own exit code is 0 where the program was built and ran, whatever the program's,
   1 where the program has errors, which it prints, and 2 for an argument it does not read or a
   file it does not find.

A program that a probe found at fault becomes a line of a fenced program of `ListTests`: the probe
answers a question, the suite holds the answer.

The fence gives no block back: each block is memory of its own from the system, a page at the
least, made inaccessible at its free and kept so. A program that allocates a lot over its run ends
at the machine's memory, where the system's out-of-memory kill ends it, which the probe prints as
`exit 137` on Linux (128 and SIGKILL, expected and not seen) and which is no read of a freed block:
on 2026-10-30 a program that made a list for each chunk of 64 bytes of an input of 11 MiB took its
memory under the fence to 3.7 GB. Probe such a program with less data, and read 137 as the fence's
cost, 139 as a fault.

## A claim of a freed block is the probe's result

On 2026-11-11, reading the findings of the round of pull request 456, a session wrote up a `mut`
receiver reached through a field of an instance as a write after free, on a run without the fence
that printed the value it expected. The probe under the fence exited 0: the read of the instance
holds a count to the end of the statement (`InstanceField`, K2), so the method writes into a block
that is still alive. The reading on issue 39 was corrected (comment 6788627649), and the finding
became a comment the code lacked and a line of the fenced program, in pull request 458
(`Emitter.Calls.cs`, `ListTests.No_block_is_read_or_written_after_it_is_freed`). A freed block
keeps its bytes until something else takes it, so a run without the fence shows nothing of a freed
block either way: before a read or a write after free goes into a reading, a thread or an issue, the program runs
under `--fenced`, with the run without it as the control (`judgment step`).

Gate: for the command, `compiler-tests` (`ProbeTests`: a clean program with and without the fence,
a read through a list's pointer after its block was freed that ends the program under the fence and
prints the value without it, a program's errors, and a command line the probe does not read, a
missing file among them). For a claim of a freed block, that it was probed under the fence before
it went into a reading, a thread or an issue: `judgment step`, since no test reads what a session
writes.
