---
name: stale-bytecode-hides-a-mutation
description: Python takes a cached .pyc for current when the source has the same size and the same second of modification; two equally long mutations of a shared module, written within one second, ran as the first one and a red proof failed in CI only (2026-10-01)
metadata:
  type: reference
---

Seen on 2026-10-01 in the first `gates` runs, and understood on 2026-10-02.

`tools/red_proof.py` replays every recorded mutation: it rewrites one file in a scratch copy of the
tree, runs the tool's self-test, restores the file. In CI one proof was reported as "the mutation
no longer turns that case red", while the same replay was red on the machine it was written on.

The cause:

- Two neighbouring ledger entries mutate the same shared module, `tools/kit.py`, and both make
  it exactly 20 bytes shorter.
- A module that is imported is cached as `__pycache__/<name>.pyc`. Python reuses the cache when
  the source's size and its modification time, in whole seconds, equal the ones the cache
  recorded. It does not look at the content.
- The CI runner replayed both entries within one second. The second run imported the bytecode of
  the first mutation, so the case the second entry names stayed green and another one failed.
- On the machine the tools were written on, Python is started with `PYTHONDONTWRITEBYTECODE=1`.
  No cache is ever written there, so the replay could not go wrong, and the tree never held a
  `__pycache__` directory either. That one variable explains both differences between the first
  local run and the first CI run.

Reproduced on 2026-10-02 with bytecode switched on: a module rewritten with the same length and
the same modification second ran as its old version; one second later it ran as the new one.

What follows from it:

- `red_proof.py` runs every self-test with `PYTHONDONTWRITEBYTECODE=1`, and its scratch copy
  leaves `__pycache__` out. Its self-test has a case for it: a tool that imports a module, a run
  with the variable removed from the caller's environment, and no cache directory afterwards.
- A harness that rewrites source files and runs them again must not let the interpreter cache
  them. The same holds for a test that edits a module and re-imports it in a new process.
- "Green here, red there" between a machine and CI: compare the environment variables that
  change what the interpreter does before you compare the code.
