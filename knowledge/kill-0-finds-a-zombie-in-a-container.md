---
name: kill-0-finds-a-zombie-in-a-container
description: A test that asks whether a process still lives reads the process's state in /proc, never `kill -0`. In a container whose first process reaps no orphan, a killed process stays a zombie, and `kill -0` finds a zombie as it finds a running process. On 2026-11-11 the test of the bound's kill passed under the mutation that killed the child's whole process group, until it read /proc (pull request 480).
metadata:
  type: reference
---

## What happened

Pull request 480 builds `Timeout(d)`. At the bound, the call kills the child it started and no
process the child started. Its test, `ChildTests.A_kill_at_the_bound_reaches_the_child_alone_and_a_process_it_started_lives_on`,
lets `sh` start a `sleep 5` with its streams sent away and then become `sleep 10`. After the call,
it asked whether the `sleep 5` still lived with `kill -0 <pid>`.

The red proof put the child in a process group of its own and killed the group at the bound. The
test still passed. The `sleep 5` had been killed, but the dev container's first process reaps no
orphan. The killed process stayed a zombie, and `kill -0` succeeds on a zombie, as on any process
that still has an entry. The cleanup line, `kill <pid>`, succeeded on the zombie too.

The test now runs `grep -q sleeping /proc/<pid>/status`. That reads `State: S (sleeping)` for the
live `sleep`, and finds no entry or `State: Z (zombie)` for a killed one. Under the same mutation
the test FAILs (`exit status 1: sh`).

## Why

A process that ends stays in the table of processes as a zombie until its parent waits for it. An
orphan's parent becomes the first process of its namespace, or a subreaper, which is expected to wait
for it. A container whose first process is a shell or a `sleep` waits for nothing, so the zombie
stays. `kill -0` only checks that a process exists and that the caller may signal it, and a zombie
exists. `CaptureTests.Lives` reads `/proc/<pid>/stat` for the same reason.

## How to apply

- A test that asks whether a process lives reads its state in `/proc` (`/proc/<pid>/status` or
  the third field of `/proc/<pid>/stat`): an entry that is gone or `Z` is a process that ended.
  It never uses `kill -0` or `kill` succeeding as that answer. Gate: `compiler-tests`
  (`ChildTests.A_kill_at_the_bound_reaches_the_child_alone_and_a_process_it_started_lives_on`, and
  its red proof, the group kill) for the bound's kill. Another test that reads a process's life
  another way: `judgment step`.
- A red proof that does not turn its test red is read before it is recorded: here it showed that
  the test could not fail. `judgment step`
