---
name: quality-bar-evidence
description: The numbers behind the quality bar of this repository - gates, self-test cases and replayed red proofs per tool, the store, the design record and the forge; every number is generated and expires
generated: 2026-10-03
digest: 15ab3a852bf055bcd26919a4af23e351e773c271c7e84130b38fe4efb078e44a
ttl_days: 60
metadata:
  type: reference
---

# Quality bar: evidence

Every number on this page sits in a generated block. `tools/quality_evidence.py` recomputes the
blocks from the tree, from git and from the forge and stamps two lines above: the date, and a
digest of the blocks, the date and the TTL. The knowledge lint fails on a block that is empty, on a
page whose digest is stale (a number typed by hand, a date moved by hand, a longer TTL), and once
the days of the TTL ran out. The digest is a plain hash that every clone recomputes the same way:
it catches the edit that leaves it stale, not one that recomputes it with the tool's own code
(HAZARD #18). The prose between the blocks carries no measurement. What the bar consists
of, and why, is in [quality-bar.md](quality-bar.md).

When the TTL runs out, the gate `knowledge` is red from the first run after that day, on every
pull request that gets one and on `main`, whatever the change touches, until a pull request brings
a regenerated page; a head gated green before that day keeps its green check until something runs
the job again, because the workflow has no schedule. Regenerating it
needs a tree whose self-tests are green, because the tool refuses to count from a red one, a
login that can read the forge, and the shapes the tool parses: a `## N. Open` heading in
`kurz-design.md`, the index lines of `INDEX.md` and the `sections` of `.review/review-rules.yaml`.
A design round that renames the Open section makes the tool refuse until its pattern follows.

## The gates

One runner, `tools/gates.py`, runs these in CI and locally. Which verdicts turn a run red is
defined in its docstring.

<!-- generated:gates -->
| Gate | Runs on | What turns it red |
|---|---|---|
| `self-tests` | every run | a tool's self-test fails, ran no case or contradicts its own exit code; a tool has no self-test or no recorded red proof; a recorded mutation no longer turns its one named case red |
| `tree` | every run | a path the design phase does not allow; a credential-shaped or machine-bound string; a merge-conflict marker; a file that is not UTF-8 text; a link or a submodule; fewer files than the floor |
| `knowledge` | every run | an INDEX link to a missing file; an entry without an INDEX line, a hook or frontmatter; a store file named in an entry, in CLAUDE.md or in a skill that does not exist; a nested entry; a LIVING entry without diagram or update triggers; expired or future-dated numbers in a guide; a generated block that is empty or is not what the page's digest says; fewer entries or living diagrams than their floors |
| `ci-config` | every run | a workflow fact that changed: an action not pinned by commit SHA, another runner label, an unpinned CLI, pip without hashes; a gates job that can be skipped, a step with an `if` or one that may fail quietly; a path or branch filter; a wider token; a key twice in one mapping; an expression inside a run line; a review job whose `if`, checkout or concurrency is not the pinned one; a workflow without facts |
| `merge-checks` | every run | the rules active on main differ from tools/ruleset.json or carry a parameter that file does not name, the ruleset is not active, it has bypass actors, or auto-merge is allowed; PARTLY where the token cannot read the last two |
| `pr-title` | pull requests | a workflow-skip literal, a credential-shaped or machine-bound string or a conflict marker in the title, the description or a commit message of the branch: each of them can become the squash commit on main |
| `pr-breadth` | pull requests | more than 15 files, or a change to the quality infrastructure, without a Blast radius section |
| `pr-findings` | pull requests | a review finding that is unresolved, or resolved without the edit (or, on tool code, the reply) that answers it |
| `push-hook` | local runs | this clone would publish without the tree gate: core.hooksPath is not .githooks |
<!-- /generated:gates -->

## Self-tests and red proofs

Each tool that makes a decision carries its cases. Each recorded mutation is applied again on
every run and has to turn its case red; a mutation that stops doing so fails the build.

<!-- generated:self-tests -->
| Tool | Self-test cases | Red proofs replayed |
|---|---|---|
| `tools/gates.py` | 33 | 23 |
| `tools/kit.py` | 17 | 13 |
| `tools/lint_ci.py` | 82 | 82 |
| `tools/lint_knowledge.py` | 61 | 44 |
| `tools/merge_pr.py` | 127 | 109 |
| `tools/pr_gates.py` | 55 | 50 |
| `tools/quality_evidence.py` | 26 | 21 |
| `tools/red_proof.py` | 29 | 26 |
| `tools/review/review.py` | 235 | 201 |
| `tools/tree_gate.py` | 81 | 51 |
| **total** | 746 | 620 |
<!-- /generated:self-tests -->

## The store and the review rules

<!-- generated:store -->
| Store | Count |
|---|---|
| Entries in INDEX.md | 23 |
| tagged (untagged) | 2 |
| tagged HARD | 11 |
| tagged LIVING | 4 |
| tagged POSTMORTEM | 2 |
| tagged RECIPE | 1 |
| tagged TRAP | 5 |
| Review rule sections | 11 |
| Review rules | 63 |
<!-- /generated:store -->

## The design record

A statement marked *(assumed)* was proposed and not objected to; it is not a decision yet.

<!-- generated:design -->
| Design record | Count |
|---|---|
| Sections | 15 |
| Statements marked *(assumed)* | 15 |
| Open questions | 5 |
<!-- /generated:design -->

## The forge

Findings are counted from the markers the reviewer leaves in its own threads. A merge over red is
counted from the record that the merge tool posts.

<!-- generated:forge -->
| Forge | Count |
|---|---|
| Pull requests opened | 2 |
| Pull requests merged | 0 |
| Merged over red, with a recorded waiver | 0 |
| Review findings posted: high | 20 |
| Review findings posted: medium | 126 |
| Review findings posted: low | 60 |
| Open HAZARD issues | 12 |
<!-- /generated:forge -->
