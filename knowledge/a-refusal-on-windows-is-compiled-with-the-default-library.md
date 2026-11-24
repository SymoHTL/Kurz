---
name: a-refusal-on-windows-is-compiled-with-the-default-library
description: FileTests' Ran compiles a program on Windows once, with the default standard library, and holds it to the refusal of a call on a file, whatever runtimes the test gives it; a program that reads HttpServer.Live, which only an edited library such as ActorTests.Blocks has, failed there with unknown-name (run 44444911770 of compiler-windows, pull request 594, 2026-11-19)
metadata:
  type: reference
---

On 2026-11-19 run 44444911770 of `compiler-windows`, on `883f95bc` of pull request 594, failed in
`FileTests.The_case_an_arm_of_the_else_after_a_loop_over_the_lines_takes_is_let_go_of_once_at_the_arm_s_end`
with ``25: error unknown-name: `HttpServer` has no member `Live` (V10)`` and the same at line 27. The
test's second program counts the blocks that live across a round with `HttpServer.Live()`, which the
standard library has under the counting allocator alone (`ActorTests.Blocks`), and gave `Ran`
`runtimes: [ActorTests.Blocks.Value]`. `FileTests.Ran` (`compiler/Kurz.Compiler.Tests/FileTests.cs`)
builds and runs the program with each runtime it is given on Linux; on Windows, whose runtime gives no
call on a file (S11), it compiles the program once with `toolchain.Runtime`, the default library, and
holds that compilation to the one diagnostic `Emitter.NotOnWindows`, so the runtimes it is given are
not read there. The checker refused the name before the emitter reached the call on a file. The
`gates` run of the same head, 44444912457, which runs the suite on Linux, passed.

**How to apply:** a test of `FileTests` that gives `Ran` a library of its own, for a program that
names what only that library has, returns on Windows before that program and after the programs the
default library compiles, which hold the refusal there, as the test of the loop's arm does since
`ed3c4651` (pull request 594). `judgment step`: nothing reads a test's runtimes against the system it
runs on, and only the Windows job, which no merge waits for (HAZARD #30), runs it where it fails.
