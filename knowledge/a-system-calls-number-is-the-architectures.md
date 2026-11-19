---
name: a-system-calls-number-is-the-architectures
description: The Linux runtime's `LockWait` and `LockWake` called `futex` by x86-64's number, 202, in `runtime/linux/linux.kz`, which Arm Linux shares and where 202 is `accept` and `futex` is 98, so that a file lock's wait on Arm returned at once and its loop spun (inferred: no run on Arm); pull request 579 moved the two calls into each architecture's file, and on 2026-11-18 the same shape was found in `IsDirectory`, which read `st_mode` at byte 24 of x86-64's `struct stat`, where AArch64's holds `st_uid`; a system call's number and a layout of the kernel's structs stand in each architecture's file
metadata:
  type: feedback
---

The numbers of Linux's system calls differ per architecture. `futex` is 202 on x86-64 and 98 on
AArch64, whose table is the kernel's generic one, where 202 is `accept` (the kernel's
`asm-generic/unistd.h` and x86-64's `asm/unistd_64.h`, as a system installs them, read
on 2026-11-18). The kernel's structs differ the same way: `st_mode` stands at byte 24 of x86-64's
`struct stat`, after an `st_nlink` of 8 bytes, and at byte 16 of the generic one, which AArch64 takes,
where byte 24 is `st_uid`; `st_size` at 48 and `st_mtime` at 88 agree (the kernel's `asm/stat.h` of
x86-64 and `asm-generic/stat.h`, as a system installs them, read on 2026-11-18; the C library's
structs not checked at source).

`runtime/linux/linux.kz` is the runtime of every Linux architecture. On `main` before pull request 579
it gave `LockWait` and `LockWake` as `syscall(202, ...)`: on Arm Linux a file lock's wait called
`accept`, returned at once, and its loop spun (579's description; inferred: no run on Arm). Pull request 579 moved the two calls
into each architecture's file, `runtime/linux/x86-64/x86-64.kz` and `runtime/linux/arm64/arm64.kz`,
each with its own number. The same shape was found in the same file on 2026-11-18: `IsDirectory`,
which `Directory.Create` calls on a path that is there, read `st_mode` at byte 24, so that on Arm
Linux it read the owner's user id as the type of what has the path, and the creation of a directory
that was there failed where that id is less than 16384, which holds no `S_IFDIR` (inferred from the
headers: no run on Arm reads it). Since 2026-11-18 each architecture's file gives the byte (`StatModeAt`), and the emitted call of
`Directory.Create` passes it to `CreateDirectories`, since the target's file is checked alone and
calls none of the architecture file's functions by name.

The suite runs every program on x86-64, and the Arm job runs the tests `ARM_FILTER` names alone (`ActorTests`,
`RuntimeTests`, two of `EmitterTests` and the actors' cases of `RunTests`), in which no
lock is ever found taken while one thread runs every program, and no directory is created (inferred
from `ARM_FILTER` in `tools/compiler_gate.py`, read on 2026-11-18 against its floor, `ARM_FLOOR` 341): no run
on Arm reached either line. So a number or a
layout of the kernel that differs per architecture stands in each architecture's file, and a test
reads it in the IR emitted for each Linux target, which needs no run on that target:
`EmitterTests.The_lock_word_s_wait_and_wake_call_futex_by_its_number_on_each_architecture_and_windows_waits_on_the_address`
for `futex`, its operations (`FUTEX_WAIT_PRIVATE`, 128, and `FUTEX_WAKE_PRIVATE`, 129) and the value
the wait sleeps on (2), and
`EmitterTests.A_directory_s_creation_reads_st_mode_at_the_byte_of_its_architecture_s_struct_stat` for
`st_mode`. A new call by number, or a new read of a struct the kernel fills, in `linux.kz` is read
against both tables before it goes in.

A test of an offset in the IR follows the value from its store to the load, through each function that hands it
on, and looks for no constant operand: the emitter writes the literal offset of a pointer's read as a value,
`*Ptr<int>(p + 24)` as `sext i32 24 to i64` and a `getelementptr` by that value, and `IsDirectory`'s offset reaches its
load through its parameter `modeAt`, stored and loaded again before its `getelementptr` (the IR of `main` at `8ab06f49`,
probed on 2026-11-19: a function of a program that reads at `p + 24`, and the directory test's program for each Linux
architecture). The directory test's first form (581's commit `42832a42`) is the evidence: there `IsDirectory` read
`st_mode` at a literal offset, which the emitter turned into a value (`sext i32 24 to i64`), so that the offset of that
read was no constant operand: that form held `IsDirectory` to no pointer offset by a constant 16 or 24, and the mutation
that read 24 on every architecture passed it (GREEN on `42832a42`); since 2026-11-18 the test follows `StatModeAt`'s result into `CreateDirectories`, through `MakeDirectory` into
`IsDirectory`, and the parameter there to the load of `st_mode`.
