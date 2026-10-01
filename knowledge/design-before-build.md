---
name: design-before-build
description: Postmortem of 2026-10-01 - a v0 compiler was built before the big design choices were asked; why the tree gate allows no compiler code in the design phase
metadata:
  type: feedback
---

On 2026-10-01, in the first design session, the agent wrote a working v0 compiler after asking
Simon for nothing but a name and a toolchain. Simon stopped it: the session was meant to be a
brainstorm over the biggest design choices, not an implementation. Every syntax and semantics pick
that the v0 had made on its own was void, and the prototype was never committed
(`kurz-design.md`, section "Prototype").

**Why:** a compiler fixes hundreds of decisions as a side effect of being written. Built before
the design round, it turns the agent's guesses into the starting point that the owner then has to
argue against. The cost was a session's worth of work thrown away, and the owner's trust that
"brainstorm" would be read as "ask".

**How to apply:** in the design phase the tree holds the design record, the reference, the corpus,
the knowledge store and the quality tools, and nothing else. The gate is `tools/tree_gate.py`: its
allowlist refuses any other path in CI (`tree`), at write time (the hook in
`.claude/settings.json`) and before a push (`.githooks/pre-push`). When Simon says build, the rule
in `CLAUDE.md` is retired in the same change that widens the allowlist. A tool under `tools/` that
lexes, parses or generates Kurz would slip past the path rule; the review rule "tools" names that
shape. Related: [[samples-obey-the-rules-beside-them]].
