---
name: quality-bar
description: How the quality bar for agent-driven work is implemented in this repository - each part of the source tutorial mapped to the file, job or setting that implements it on GitHub, and every part that is not implemented with the reason
metadata:
  type: reference
---

# The quality bar in this repository

This repository is written mostly by AI agent sessions. The quality bar is what keeps that safe:
rules that ship with their gates, a knowledge store, an automated reviewer that has to complete
before a merge, and server-side merge checks. It implements a tutorial called "Building a
high-control quality bar for agent-driven development", which is not part of this repository
(the copy used was read on 2026-10-01; its file hash starts with `67d144f4ddf6`). That tutorial is
written for GitLab; this guide says how each part looks here, on GitHub, and it is kept current
with the gates it describes: a change to a gate, a tool, a hook or a review rule updates this
guide in the same pull request.

The numbers that show the bar working are in [quality-bar-evidence.md](quality-bar-evidence.md).

## The laws, and what implements each

| Law | Here |
|---|---|
| 1. A rule ships with its gate | Every rule in `CLAUDE.md` ends in `Gate:`, `HAZARD (...; #issue)` or `judgment step`. The review rule "rules for sessions" flags one that does not. |
| 2. A postmortem ships as rule, gate and knowledge entry | `knowledge/design-before-build.md` is the pattern: the rule is in `CLAUDE.md`, the gate is the tree gate, the story is the entry. |
| 3. Detection is built while building | Each tool carries its cases; a new decision without a case and a red proof turns `self-tests` red. |
| 4. One definition per concept | One gate runner (`tools/gates.py`) for CI and local runs; one tree gate for CI, the write-time hook and the pre-push hook; one ruleset file for the server setting, its assertion and the merge tool; one pattern list (`tools/kit.py`). |
| 5. Facts are routed by kind | The "Knowledge" rules in `CLAUDE.md`; `knowledge/diagram-knowledge-routing.md`. |
| 6. Unchecked must never look clean | Floors in every scan; `NOT RUN` as its own verdict, and `PARTLY` for a gate that could read only part of what it checks; the `review` status exists only after a completed review, and a batch that failed or ran out of time is not reviewed; a failed command is a refusal (`kit.Refused`). |
| 7. Every decision tool proves itself | `tools/red_proof.py` runs every self-test and replays every recorded mutation from `tools/red_proofs.json` on each run. |

## Section by section

| Tutorial part | Implemented here as |
|---|---|
| `CLAUDE.md`: rules with gates | [CLAUDE.md](../CLAUDE.md), in the tutorial's section order. |
| Knowledge store, index, lint with floor and self-test | `knowledge/`, `INDEX.md`, `tools/lint_knowledge.py` (gate `knowledge`). |
| Routing and the memory audit | Rules in `CLAUDE.md`. The audit tool belongs to the machine, not to this repository; nothing here forces the wrap-up (HAZARD #6). |
| Zero findings on the store | `tools/pr_gates.py findings` (gate `pr-findings`): a finding on anything but tool and workflow code is answered only by changing the file. |
| Skills | `.claude/skills/change-walk` and `.claude/skills/design-round`. |
| Hooks for shapes that already cost | One: writing code in the design phase, which happened on 2026-10-01. `.claude/settings.json` calls `tools/tree_gate.py --hook`. |
| The tools law | `tools/red_proof.py` and `tools/red_proofs.json` (gate `self-tests`). |
| Pipeline jobs | One job, `gates`, running `tools/gates.py`. See "Gates" below for which of the tutorial's jobs exist. |
| CI configuration is code | `tools/lint_ci.py` (gate `ci-config`): each fact is broken in the real workflow file by its self-test. |
| Test-quality clauses | The "Tests" section of `CLAUDE.md`, cut down to what a tree of tools can break; see "Not implemented". |
| Automated reviewer | `tools/review/review.py`, `.review/review-rules.yaml`, `.github/workflows/review.yml`. |
| Server-side merge checks | A repository ruleset, defined in `tools/ruleset.json`, asserted on every gates run (gate `merge-checks`). |
| Reading the review before merging, threads, one push per round | `tools/merge_pr.py` checks them at the moment of the merge; the round discipline is a rule in `CLAUDE.md`. |
| Gate-flip | `tools/merge_pr.py --over-red`. |
| CI triage | The "CI" section of `CLAUDE.md`. |
| Agent operations (isolation, notes file, owner queue, worktrees) | Machine-level, outside this repository. Nothing here depends on it. |
| Living diagrams | Four entries tagged LIVING in `INDEX.md`; the lint requires each to have a diagram and its update triggers. |
| Living guides | This guide and the evidence guide; the tree gate keeps both portable, the lint expires the numbers. |
| Not in the tutorial: the product is a language definition | `reference/` (rules with an id and a status), `corpus/` (one case per file) and `tools/lint_reference.py` (gate `reference`), which keeps the two consistent. Nothing runs a case yet (HAZARD #1). |

## Gates

The list of gates has one definition, `GATES` in `tools/gates.py`. It is printed, with what turns
each gate red, in the evidence guide; the picture is `knowledge/diagram-gate-map.md`. Of the jobs
the tutorial lists:

- **Exist here:** the knowledge lint, the reference lint (this repository's own: the tutorial has
  no such job), the secret scan (part of the tree gate, on top of GitHub's
  own secret scanning and push protection), title hygiene, the breadth gate, the review, the
  reviewer's own unit suite (run with every other self-test), and the verdict idea, which here is
  the runner itself: one job that is red unless every gate reported a pass.
- **Have nothing to check yet:** compile, unit, end-to-end, UI quality, migration drift, coverage,
  the complexity ratchet, the secrets gate for deploy directories, the deploy lane. There is no
  product code, no database and no deployment. They arrive with the compiler, not before.

## What is different on GitHub

| GitLab, in the tutorial | GitHub, here |
|---|---|
| "All threads must be resolved" | Ruleset rule `pull_request` with `required_review_thread_resolution`. |
| "Pipelines must succeed" | Ruleset rule `required_status_checks`, each check pinned to the GitHub Actions app so that nobody else can report it. |
| "Skipped pipelines count as successful: off" | No such switch. A job skipped by `if:` reports success, so the `gates` job has no `if:` anywhere, and the review's required check is a status that only a completed review posts (`knowledge/a-skipped-job-reports-success.md`). |
| Draft lane as a blocking manual job | A Draft cannot be merged, and marking it Ready starts the review. In Draft the review runs on demand: `gh workflow run review.yml -f pr=N`. |
| Rules fetched from the target branch | `pull_request_target`: workflow, reviewer and rules all come from the default branch (`knowledge/the-review-runs-the-default-branch.md`). |
| A merge-request pipeline needs no permission to start | GitHub blocks `pull_request_target` in a public repository unless an Actions event policy allows it; the default rule is in evaluate mode until 2026-11-02 (`knowledge/pull-request-target-is-blocked-by-default.md`). The policy is the owner's setting and nothing asserts it yet (HAZARD #10). Without it the review is dispatched by hand for each head. |
| Unanchored thread for the lows | GitHub has no resolvable thread without a file, so the lows share a file-level thread on the first file that has one. A comment holds 65,536 characters, so a thread carries at most twenty findings and the rest go into further threads. |
| No per-request override of the pipeline check | The same. The gate-flip switches the ruleset's enforcement off for one merge, restores it and reads it back. While it is off nothing on the server holds any pull request or a push to `main`; the tool's own reading of the checks is the only gate, so it counts a check run only from the app the ruleset pins. |
| Pipeline-control literals in the title | The workflow-skip literals; a squash merge puts the title on `main`. |
| Editing title or description starts no pipeline | The gates do run on `edited`. The review does not, by design: the description is part of its cache key, and an edit would bill a full review. Re-run it by hand after an edit that matters. |
| Trigger jobs must never be waived | There are none: `tools/lint_ci.py` refuses a job that calls another workflow. |
| Diff versions to tell whether a file changed since a finding | The blob id of the file at the finding's commit against the blob id at the head. |

## The reviewer

One job, one rules file, every pull request, no path allowlist. It follows the tutorial's design:
a read-only model call with no tools and an empty working directory; the pull request as data in
tags with a random suffix; batches of about thirty thousand characters; each batch reviewed again
until a pass adds nothing above low (at least two passes, at most five); findings identified by
file and line; a time budget counted from the job's start; a replay cache of per-file diff hashes
whose key covers the script, the rules, the title and the description; notes that are never
silent and never repeated.

Six things are specific to this repository:

- **The model is pinned by exact id** in `review.py`, and an answer from any other model is
  refused. **The effort is pinned next to it.** The model call gets an environment of its own:
  no forge token and nothing of a Claude session that happens to run the script, because such a
  session exports its own effort and switches
  (`knowledge/a-headless-call-inherits-its-session.md`).
- **A run that fails or runs out of time keeps what it has.** It posts what the finished passes
  found, stores the files whose batches converged, and then ends red. The next run replays those
  files and continues with the rest, so a diff that needs more than one run is reviewed across
  them and no paid pass is thrown away. A batch counts as reviewed only when it converged or used
  every pass it may have, and a file only when the forge took its findings: a thread it refuses
  costs that thread's files, not the run. The first version here posted nothing unless every batch had run, and
  let a batch that ran out of time after one pass stand as reviewed.
- **Passes run in rounds.** Every batch gets one pass before any batch gets a second, so a run
  that runs out of time has read every file once. The first version gave each batch all its
  passes in turn, which on a large diff spends the budget on the first batches and never reads
  the last ones (`knowledge/what-a-review-pass-costs.md`).
- **The log is written line by line**, one line per pass with its duration. A pass takes minutes
  (`knowledge/what-a-review-pass-costs.md`), and a log that fills only at the end hides a run
  that will not finish.
- **A pull request from outside is not reviewed automatically.** The repository is public and a
  review spends the owner's Claude seat, so the job runs for the owner, members and collaborators.
  The owner dispatches the workflow for anyone else.
- **A run outside CI is an audit, not a pass.** `review.py --pr N --local` posts findings and an
  audit note with the script hash, the model and whether it converged. It cannot post the `review`
  status, so a merge after it needs the owner's approval for that item.

The credential is a repository secret, `CLAUDE_CODE_OAUTH_TOKEN` or `ANTHROPIC_API_KEY`. Without
one the job is red: a review that cannot run must never look like a review that found nothing.

## Hazards

Each rule that has no gate is labelled where it stands and has an open issue with the label
`hazard`. The list at the end of `CLAUDE.md` says which practice is enforced by a mechanism and
which only by instruction. An issue closes when its gate exists, and the gate map changes in the
same pull request.

## Not implemented, and why

- **A vendored copy of the tutorial.** It belongs to its authors; this guide maps it instead.
- **The clauses about browser tests** (clicks that prove reachability, render-time assertions,
  option order, teardown filters, geometry probes) and the build-time confidentiality gates for
  telemetry. There is no user interface and no telemetry.
- **Ratchets and convention tests over product code.** There is no product code. The one
  convention that exists today is over the tools themselves: every tool has a self-test and a red
  proof.
- **A scheduled job that asserts the merge settings.** The assertion runs with every gates run
  instead. The bypass list and the auto-merge switch need a token the job does not have; the job
  prints NOT CHECKED for both and the gate ends as PARTLY, which is not red and is not counted
  as a pass. A local run with the owner's login checks them (HAZARD #7).
- **The launcher, the per-window isolation, the worktree-removal script.** They belong to a
  machine that runs many sessions at once, and they live there.
- **A token-usage block in the evidence.** Only a machine with the session logs could fill it.
- **Running the corpus.** No compiler exists. The reference lint checks the shape of a case and
  that the reference shows it unchanged; whether an expected output or error is right is decided
  by reading and by the review (HAZARD #1).

## Changing the bar

A change to a gate, a tool, a hook or a review rule carries, in the same pull request: the case
and its red proof, the gate map, this guide when the mapping changes, and a "Blast radius" section
in the description. The reviewer flags a change that does none of it and does not say why.
