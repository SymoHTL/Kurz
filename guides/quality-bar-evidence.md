---
name: quality-bar-evidence
description: The numbers behind the quality bar of this repository - gates, self-test cases and replayed red proofs per tool, the store, the design record and the forge; every number is generated and expires
generated: 2026-10-02
digest: 279048c62b3604d2ce377c79296048bf05b87f3dfe8300011c36210d1ef92a1e
ttl_days: 60
metadata:
  type: reference
---

# Quality bar: evidence

Every number on this page sits in a generated block. `tools/quality_evidence.py` recomputes the
blocks from the tree, from git and from the forge and stamps two lines above: the date, and a
digest of the blocks. The knowledge lint fails on a block that is empty, on blocks that are not
what the digest says (a number typed by hand, or a page the tool never wrote), and once the days
of the TTL ran out. The prose between the blocks carries no measurement. What the bar consists
of, and why, is in [quality-bar.md](quality-bar.md).

When the TTL runs out, the gate `knowledge` is red on every open pull request and on `main`,
whatever the change touches, until a pull request brings a regenerated page. Regenerating it
needs a tree whose self-tests are green, because the tool refuses to count from a red one, and a
login that can read the forge.

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
| `tools/gates.py` | 31 | 20 |
| `tools/kit.py` | 11 | 7 |
| `tools/lint_ci.py` | 67 | 67 |
| `tools/lint_knowledge.py` | 54 | 36 |
| `tools/merge_pr.py` | 118 | 100 |
| `tools/pr_gates.py` | 48 | 43 |
| `tools/quality_evidence.py` | 25 | 20 |
| `tools/red_proof.py` | 28 | 25 |
| `tools/review/review.py` | 197 | 155 |
| `tools/tree_gate.py` | 77 | 47 |
| **total** | 656 | 520 |
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

## The design record and the reference

A statement marked *(assumed)* was proposed and not objected to; it is not a decision yet. In the
reference a `proposed` rule is what a case needed and the record does not say, and an `open` rule
is a fork nobody chose: neither is a decision. The cases are counted here, not run: no compiler
exists.

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
