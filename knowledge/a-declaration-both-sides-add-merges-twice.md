---
name: a-declaration-both-sides-add-merges-twice
description: After a take of main, build a program, not the compiler alone. A C function that main and a branch each declared in the runtime's one extern block, at different lines, merges with no conflict into two declarations. The compiler still builds, since the runtime is only embedded there, and the runtime's check then says redeclared and every test that builds a program fails (pull request 476, 2026-11-11, `fcntl` in the extern block of runtime/linux/linux.kz)
metadata:
  type: reference
---

## What happened

Pull request 476, which builds `Command` on Linux, declared `int fcntl(int fd, int command, ...)` at
the end of the `extern "C"` block of `runtime/linux/linux.kz`, for the move of a descriptor below 3.
Pull request 464, merged to `main` while 476 was in review, declared the same function near the top
of that block, for the start step that opens `/dev/null` in place of a closed stream.

The take of `main` merged with no conflict, since the two lines stood apart. The compiler built, as it
embeds the runtime's files as they are and reads none of them. The runtime's check then refused the
block: the second declaration was `redeclared` (V9). Every test that builds a program failed on it,
`RuntimeTests.The_runtime_checks_clean_for_every_target_and_common_calls_its_target` among them. The
fix took the one declaration `main` had (commit 24fbeb31).

## Why

A text merge sees lines. Two declarations of one name at two lines are no conflict, while the
checker reads them as one name declared twice. The build of the compiler cannot see it: the runtime
is checked when a program is built, never when the compiler is.

## How to apply

- After a take of `main`, run the gates on the merge commit, in the image the `gates` job runs in,
  which builds and runs programs, before the push
  ([a-merge-of-main-with-every-word-kept-can-fail-a-test](a-merge-of-main-with-every-word-kept-can-fail-a-test.md)).
  A build of the compiler alone says nothing of the runtime. `judgment step`
- A `redeclared` in a file of `runtime/` after a take is this trap: keep the declaration `main` has
  and take the branch's out, so that the take changes no line of `main`.
  Gate: `compiler-tests`
  (`RuntimeTests.The_runtime_checks_clean_for_every_target_and_common_calls_its_target`, and every
  test that builds a program).
