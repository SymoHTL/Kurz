---
name: the-write-time-hook-runs-the-default-checkouts-gate
description: The write-time hook runs the tree gate of the checkout the session was opened in, so a worktree whose branch widens the allowlist still has its new paths denied by the hook until that branch merges; the branch's own copy of the gate holds in CI and in the pre-push hook (2026-10-15)
metadata:
  type: reference
---

Seen on 2026-10-15, in the worktree of the change that admits the runtime's C source under
`compiler/`.

The write-time hook is configured with the project directory the session was opened in: its
command runs `tools/tree_gate.py` from there, not from the worktree the file is written into. A
branch that widens the allowlist, as the change that admitted `.c` and `.h` under `compiler/`
did, therefore has every file of the new kind refused by the hook in its own worktree, while the
same gate's copy on the branch admits it. The hook saw the default branch's allowlist until the
widening merged.

What holds for the push is the branch's own copy: the pre-push hook runs the gate of the checkout
that pushes, and the `tree` job runs the pull request's copy (the rule "The `gates` job runs the
pull request's own copy of the gates", HAZARD #16 in `CLAUDE.md`). A file of the new kind is
placed by a shell command, which the write-time hook does not see (HAZARD #4), and the branch's
gate is run by hand before the push, `tools/tree_gate.py --self-test` and `tools/gates.py`; the
pull request's description says that the file was placed this way and why.
