---
name: a-replay-inside-a-replay-has-no-end
description: Postmortem of 2026-11-02 - a mutation of the evidence tool's --self-test dispatch, written to prove a case, made its self-test fall through to the writing mode, whose recompute replays the ledger; the replay ran the mutated self-test again in its copy of the tree, which started another replay, without end, and it was stopped by hand. Every self-test the red-proof tool starts carries the mark KURZ_RED_PROOF_REPLAY, and a replay that finds it refuses before it copies anything
metadata:
  type: feedback
---

On 2026-11-02 a session proved the cases of a change to `tools/quality_evidence.py` with a script of
its own: write a mutation, run the tool's `--self-test`, see the named case fail, restore the file.
One mutation turned the dispatch of `run()`, `if call == "self-test": return self_test()`, into
`if False:`. The mutated tool's `--self-test` then took the path of a call with no argument, the
writing mode. Its recompute computes every block, and the block `self-tests` calls
`red_proof.replay()` to count the cases and the proofs. The replay copied the tree and ran every
tool's `--self-test` in the copy, the mutated evidence tool among them, which started another
replay inside that copy. Each level made a copy of the tree and a process for every self-test, and
the levels kept coming; the session stopped them by hand, process by process. No entry of
`tools/red_proofs.json` held the mutation: the script ran it before it wrote the ledger.

**Why:** a self-test that reaches the replay is a replay inside a replay, and nothing ended one.
The timeout of `self_test_of` (600 s) ends the process it started and nothing below it: the
processes that one had started kept running on their own, and they started more. A replay in CI
that had held such an entry would have done the same on the runner.

**How to apply:**

- `tools/red_proof.py` sets `KURZ_RED_PROOF_REPLAY=1` in the environment of every self-test it
  starts (`self_test_of`), and `replay()` refuses first thing when it finds it, before any copy, with a
  text that names the variable. A self-test that reaches the replay gets that refusal: the evidence
  tool raises it and exits 1, and the replay above reports the mutation as one that no longer turns
  its case red. Checked a single time with the mutation of that day and the mark set as a replay sets it:
  one level, exit 1, no copy made, the page unchanged.
- A mutation of a dispatch is proved by one that keeps the dispatch: the evidence tool's
  `return self_test()` becomes `return self_test() + 1`, and a case reads the exit code passed on.
- A proof script outside the replay runs each mutated self-test with the mark set, in a process
  group of its own, under a timeout that ends the whole group.
- `check()` runs the self-tests in the tree it is given and has no such guard; no self-test calls
  it on the real tree.
- A refusal with no replay running means the variable was exported by hand: unset it.

Gate: `self-tests`, the cases "a replay inside a self-test that a replay runs is refused before it
copies or checks anything, and says why" and "every self-test a replay runs is marked, so a replay
it starts is refused", and their red proofs, which run against stand-ins and never against a real
replay. Related: [[stale-bytecode-hides-a-mutation]], [[a-cancel-does-not-beat-the-runner]].
