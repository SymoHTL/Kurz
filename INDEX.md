# INDEX

One line per entry: a link plus a hook that carries the payload. Add your line WITH your entry, in
the same pull request. HARD = a trap that bit at least once; do not re-derive it, read the file.
TRAP = a trap caught before it cost. RECIPE = a working procedure. POSTMORTEM = the story.
LIVING = a diagram kept current with the system it draws.

## Design work

- [Design before build](knowledge/design-before-build.md) POSTMORTEM HARD — a v0 compiler was built before the big choices were asked and every pick in it was void (2026-10-01). In the design phase the tree gate refuses any path that is not design record, reference, corpus, knowledge or quality tooling, at write time, before a push and in CI.
- [Samples obey the rules beside them](knowledge/samples-obey-the-rules-beside-them.md) HARD — the cycle-rule section showed a class that its own rule rejects. Nothing runs a sample, so read each one against every rule in its section; samples in the design record and expectations in the corpus stay unrun (HAZARD #1).
- [A guarantee needs its mechanism](knowledge/guarantee-needs-its-mechanism.md) HARD — "durable state survives machine loss" was claimed for a log on that machine's own disk. Write a guarantee as: the failure it survives, the mechanism, the cost; without a mechanism it goes under Open.
- [Reading a paper for evidence](knowledge/reading-a-paper-for-evidence.md) RECIPE — cite a figure only after reading its sentence in the source: extract the PDF with pypdf (set PYTHONIOENCODING=utf-8 on a Windows console), record the workload, machine and baseline, link the paper.
- [A case bakes in a pick](knowledge/a-case-bakes-in-a-pick.md) TRAP — writing the first conformance cases needed 33 rules the design record never states (`print`, block bodies, scope, what "used" means) and met 24 forks. A rule the record does not carry is `proposed` or `open` in the reference, never `decided`; the lint refuses a case on an open rule.
- [Design change playbook](knowledge/diagram-design-change-playbook.md) LIVING — before, while and after writing a change to the design record: the owner chose it, each statement has one status, guarantees carry mechanism and cost, samples read against their rules, cross-references and Open updated in the same change, and the reference rules that cite the changed sections follow in the same pull request.

## Publishing and the forge

- [A force push does not unpublish](knowledge/a-force-push-does-not-unpublish.md) HARD — after a history rewrite GitHub still serves the old commits by SHA and shows the force push with "Compare changes". Only a fresh repository or GitHub Support removes them, so the gate is before the push: enable `.githooks/pre-push`, and never write future plans into the tree.
- [A private repository has no merge checks](knowledge/private-repo-has-no-merge-checks.md) HARD — on a free account, rulesets and branch protection answer 403 for a private repository. This one is public for that reason; `merge-checks` turns red if the ruleset is ever gone.
- [The forge fills ruleset parameters](knowledge/the-forge-fills-ruleset-parameters.md) TRAP — GitHub added `require_extra_approval_for_unattributed_changes: true` to the ruleset on its own. `merge-checks` fails on any parameter the server carries and `tools/ruleset.json` does not name, so a new default becomes a decision instead of a surprise.
- [A skipped job reports success](knowledge/a-skipped-job-reports-success.md) TRAP — a job skipped by `if:` satisfies a required check, a workflow that never starts leaves it pending. The gates job has no `if:`; the review's required check is a status only a completed review posts.
- [The review runs the default branch](knowledge/the-review-runs-the-default-branch.md) TRAP — with pull_request_target the workflow, the reviewer and its rules come from the default branch (since 2025-12-08 not from a stacked pull request's base): a pull request cannot change its own review, a change to the reviewer acts only after it merged, and the pull request that introduced it had to be reviewed off-pipeline and merged with the owner's approval.
- [pull_request_target is blocked by default](knowledge/pull-request-target-is-blocked-by-default.md) TRAP — a public repository without an Actions event policy gets a default rule that blocks `pull_request_target`: evaluate mode now, enforced from 2026-11-02. A Ready pull request with no `review` run at all is this; dispatch `review.yml` by hand until the owner's policy allows the event for that one workflow file (HAZARD #10).

## Tools

- [Claude CLI headless answer](knowledge/claude-cli-headless-answer.md) TRAP — an API error still says `"subtype": "success"`. Decide on `is_error`, the exit code and `structured_output`; check `modelUsage` for the pinned model. The entry has the flags for a call with no tools.
- [A headless call inherits its session](knowledge/a-headless-call-inherits-its-session.md) TRAP — a CLI call started inside an agent session takes that session's effort and switches from the environment, `--safe-mode` does not stop it, and `--effort` loses against the inherited variable. The reviewer builds the child's environment itself and pins the effort beside the model.
- [What a review pass costs](knowledge/what-a-review-pass-costs.md) HARD — one pass over a 30k-character batch thinks 60k to 120k tokens and takes 10 to 14 minutes at every effort; about 1.4 to 1.8 USD on `claude-opus-5-5`, 3.5 to 4.3 on `claude-fable-5-1`, with no better findings for the price. A 16-batch pull request cannot be reviewed in one 85-minute run, so passes run in rounds, every batch once before any twice; keep diffs small and finish reviewer changes before the review starts.
- [Stale bytecode hides a mutation](knowledge/stale-bytecode-hides-a-mutation.md) HARD — Python reuses a `.pyc` when the source has the same size and the same second of modification, so two equally long mutations of `tools/kit.py` ran as the first one, in CI only. The red-proof replay writes no bytecode; a harness that rewrites source and runs it again must not let the interpreter cache it.
- [YAML plain scalar traps](knowledge/yaml-plain-scalar-traps.md) HARD — in a YAML list a colon-space turns a bullet into a mapping and a leading quote ends it early; PyYAML keeps the last of two duplicate keys silently. Quote the bullet or use `>-`.

## The quality bar

- [Gate map](knowledge/diagram-gate-map.md) LIVING — every gate, hook, server-side rule and process law by where it runs, with what turns it red or the HAZARD issue that stands in for it.
- [The walk one change takes](knowledge/diagram-change-walk.md) LIVING — branch, local gates, push through the pre-push hook, Draft, gates job, Ready, review on the default branch's rules, threads answered by edits, merge of the exact head through the tool; and where each step bounces back.
- [Knowledge routing](knowledge/diagram-knowledge-routing.md) LIVING — where a fact goes: decisions to the design record, rules with gates to CLAUDE.md, lessons to knowledge/, open work to issues, and anything machine-bound or about future plans to private memory, never here.
- [Quality bar guide](guides/quality-bar.md) — how each part of the source tutorial is implemented here on GitHub, what is different from GitLab, and what is not implemented and why.
- [Quality bar evidence](guides/quality-bar-evidence.md) — generated numbers for gates, self-tests, red proofs, the store and the forge; they expire after `ttl_days` and the lint then fails until `tools/quality_evidence.py` ran.
