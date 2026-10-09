---
name: quality-bar-evidence
description: The numbers behind the quality bar of this repository - gates, self-test cases and replayed red proofs per tool, the store, the design record and the forge; every number is generated and expires
generated: 2026-10-10
digest: 5565409279930f00d0d9177fb20edb353b1c434fc4143af0a41cb103e8ccd30c
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
| `tree` | every run | a path the design phase does not allow; a credential-shaped or machine-bound string or a merge-conflict marker, in a file or in its name; a file that is not UTF-8 text; a link or a submodule; fewer files than the floor. Before a push, the same in every commit it publishes, in the name of every ref it publishes and in the message and the name of every annotated tag and of each tag it points at, and a tag or a ref that points straight at a blob or a tree |
| `knowledge` | every run | an INDEX link to a missing file; an entry without an INDEX line, a hook or frontmatter; a store file named in an entry, in CLAUDE.md or in a skill that does not exist; a link in a store file that reaches no entry or file or leaves the repository; a nested entry; a LIVING entry without diagram or update triggers; expired or future-dated numbers in a guide; a generated block that is empty or is not what the page's digest says; fewer entries or living diagrams than their floors |
| `reference` | every run | a rule of the reference without id, status or the record section it cites; a decided, assumed or proposed rule with neither a case nor a reason; a case on an open rule; a sample that differs from its corpus file; a code block that is neither a case sample nor marked text, or that is never closed, and a fence the lint would not read; a corpus header that cannot be read or names an unknown rule; an error id the error table does not list, or lists for other rules; an error-table row that does not read as one, or a row that reads as one outside an error table; a design record, a chapter or a corpus file that cannot be read, named with what went unchecked; fewer rules or cases than the floors |
| `ci-config` | every run | a workflow fact that changed: an action not pinned by commit SHA, another runner label, an unpinned CLI, pip without hashes; a job in a container or beside services; the credential anywhere but the env of the job's last step; a gates job that can be skipped, a step with an `if` or one that may fail quietly; a path filter, a filter on pull_request or a push trigger not limited to main; a wider token; a key twice in one mapping; an expression inside a run line; a review job whose `if`, checkout or concurrency is not the pinned one; a workflow without facts |
| `merge-checks` | every run | the rules active on main differ from tools/ruleset.json or carry a parameter that file does not name, the ruleset is not active, it has bypass actors, or auto-merge is allowed; PARTLY where the token cannot read the last two |
| `pr-title` | pull requests | a workflow-skip literal, a credential-shaped or machine-bound string or a conflict marker in the title, the description or a commit message of the branch: each of them can become the squash commit on main |
| `pr-breadth` | pull requests | more than 15 files, or a change to the quality infrastructure, without a Blast radius section |
| `pr-findings` | pull requests | a review finding that is unresolved, or resolved without the edit (or, on tool, workflow or hook code, the reply) that answers it |
| `push-hook` | local runs | this clone would publish without the tree gate: core.hooksPath is not .githooks |
<!-- /generated:gates -->

## Self-tests and red proofs

Each tool that makes a decision carries its cases. Each recorded mutation is applied again on
every run and has to turn its case red; a mutation that stops doing so fails the build.

<!-- generated:self-tests -->
| Tool | Self-test cases | Red proofs replayed |
|---|---|---|
| `tools/gates.py` | 33 | 23 |
| `tools/kit.py` | 22 | 19 |
| `tools/lint_ci.py` | 89 | 89 |
| `tools/lint_knowledge.py` | 91 | 74 |
| `tools/lint_reference.py` | 77 | 81 |
| `tools/merge_pr.py` | 148 | 133 |
| `tools/pr_gates.py` | 96 | 98 |
| `tools/quality_evidence.py` | 40 | 45 |
| `tools/red_proof.py` | 35 | 34 |
| `tools/review/review.py` | 264 | 239 |
| `tools/tree_gate.py` | 107 | 82 |
| **total** | 1002 | 917 |
<!-- /generated:self-tests -->

## The store and the review rules

<!-- generated:store -->
| Store | Count |
|---|---|
| Entries in INDEX.md | 31 |
| tagged (untagged) | 2 |
| tagged HARD | 14 |
| tagged LIVING | 4 |
| tagged POSTMORTEM | 4 |
| tagged RECIPE | 2 |
| tagged TRAP | 9 |
| Review rule sections | 11 |
| Review rules | 64 |
<!-- /generated:store -->

## The design record

A statement marked *(assumed)* was proposed and not objected to; it is not a decision.

<!-- generated:design -->
| Design record | Count |
|---|---|
| Sections | 15 |
| Statements marked *(assumed)* | 53 |
| Open questions | 6 |
<!-- /generated:design -->

## The reference and the corpus

In the reference a `proposed` rule is what a case needed and the record does not say, and an
`open` rule is a fork nobody chose: neither is a decision. The cases are counted here, not run:
nothing in this repository executes Kurz.

<!-- generated:reference -->
| Reference and corpus | Count |
|---|---|
| Rules in the reference | 182 |
| of them decided | 143 |
| of them assumed | 39 |
| of them proposed | 0 |
| of them open | 0 |
| Corpus cases, none of them run | 270 |
| Compile-error ids | 51 |
| Run-time error ids | 6 |
<!-- /generated:reference -->

## The forge

Findings above low are counted from the markers in the reviewer's threads, lows from the markers of
the notes it leaves on the pull request, one note per part it posts; the issue that collects them is
not read. A merge over red is counted from the record that the merge tool posts. A pull request
counts when someone who may write here opened it or when it was merged, a hazard issue when such a
person opened it or put the hazard label on it, and a post only from such a person or the Actions
token; who may write here is the collaborators list with push access, not a record's
`author_association`. The reason: anyone can open a pull request or an issue on, or comment in, a
public repository (the GitHub docs, read 2026-10-09: "Creating an issue" says people with read
access can create one, which a public repository gives everyone; "Creating a pull request from a
fork" says write access to the head branch is enough, which a fork gives).

<!-- generated:forge -->
| Forge | Count |
|---|---|
| Pull requests opened by a writer, or merged | 7 |
| Pull requests merged | 6 |
| Merged over red, with a recorded waiver | 4 |
| Review findings posted: high | 40 |
| Review findings posted: medium | 518 |
| Review findings posted: low | 687 |
| Open HAZARD issues | 14 |
<!-- /generated:forge -->
