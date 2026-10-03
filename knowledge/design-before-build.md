---
name: design-before-build
description: Postmortem of 2026-10-01 - a v0 compiler was built before the big design choices were asked; in the design phase the tree gate refuses every path that is not design record, reference, corpus, knowledge or quality tooling, and compiler code hidden under tools/ is left to the review
metadata:
  type: feedback
---

On 2026-10-01, in the first design session, the agent wrote a working v0 compiler after asking
the owner for nothing but a name and a toolchain. The owner stopped it: the session was meant to
be a brainstorm over the biggest design choices, not an implementation. Every syntax and semantics
pick that the v0 had made on its own was void, and the prototype was never committed
(`kurz-design.md`, section "Prototype").

**Why:** a compiler fixes hundreds of decisions as a side effect of being written. Built before
the design round, it turns the agent's guesses into the starting point that the owner then has to
argue against. The cost was a session's worth of work thrown away, and the owner's trust that
"brainstorm" would be read as "ask".

**How to apply:** in the design phase the tree holds the design record, the reference, the corpus,
the knowledge store and the quality tools, and nothing else. The gate is `tools/tree_gate.py`, and
its allowlist is a rule about paths. It runs in three places, and only the first always holds:

- In CI (`tree`), on every push that starts the workflow. That is after the push has published.
  Two heads start no run at all: one whose head commit carries a workflow-skip literal, and a
  pull request with a merge conflict ([a-skipped-job-reports-success](a-skipped-job-reports-success.md)).
  There only the pending required check holds the merge; the pushed paths and strings were read
  by nothing unless the pre-push hook was on.
- Before a push (`.githooks/pre-push`), in a clone that switched the hook on with
  `git config core.hooksPath .githooks`. Where it is off nothing runs: HAZARD #4.
- At write time (the hook in `.claude/settings.json`), for the Write and Edit tools of an agent
  session. A file written through a shell command, and a hook the harness cut off at its timeout,
  pass it: HAZARD #4.

A path rule cannot see what a file does. A tool under `tools/` that lexes, parses or generates
Kurz passes the tree gate; the review rule "tools" names that shape, and a reviewer is the only
check. The rule in `CLAUDE.md` is retired, and the allowlist widened, in one change when the owner
says build. Related: [[samples-obey-the-rules-beside-them]].
