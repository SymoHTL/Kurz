---
name: quality-bar
description: How the quality bar for agent-driven work is implemented in this repository - each law and each part of the method mapped to the file, job or setting that implements it on GitHub, what GitHub does differently from GitLab, and every part that is not implemented with the reason
metadata:
  type: reference
---

# The quality bar in this repository

This repository is written mostly by AI agent sessions. The quality bar is what keeps that safe:
rules that ship with their gates, a knowledge store, an automated reviewer that has to complete
before a merge, and server-side merge checks. The method comes from a tutorial on quality bars
for agent-driven development that is not published and not part of this repository (the copy
used was read on 2026-10-01, its version of 2026-10-07 on that day). It is written for GitLab; this guide states its laws and says how
each part looks here, on GitHub.

The numbers that show the bar working are in [quality-bar-evidence.md](quality-bar-evidence.md).

## The laws, and what implements each

| Law | Here |
|---|---|
| 1. A rule ships with its gate | Every rule in `CLAUDE.md` ends in `Gate:`, `HAZARD` with the number of its issue, or `judgment step`. The review rule "rules for sessions" flags one that does not. |
| 2. A postmortem ships as rule, gate and knowledge entry | `knowledge/design-before-build.md` is the pattern: the rule is in `CLAUDE.md`, the gate is the tree gate, the story is the entry. |
| 3. Detection is built while building | Each decision tool carries its cases, and `self-tests` replays every recorded mutation; a case without one is not replayed. What that catches: a recorded case that no longer goes red, a tool without a self-test or without any recorded proof. A decision that was added with no case at all is invisible to it; the review rule "tools" names that shape, which makes it a judgment step. |
| 4. One definition per concept | One gate runner (`tools/gates.py`) for CI and local runs; one tree gate for CI, the write-time hook and the pre-push hook; one ruleset file for the server setting, its assertion and the merge tool; one pattern list (`tools/kit.py`). |
| 5. Facts are routed by kind | The "Knowledge" rules in `CLAUDE.md`; `knowledge/diagram-knowledge-routing.md`. |
| 6. Unchecked must never look clean | Floors in every scan; `BROKEN` for a gate that could not start, `NOT RUN` for a pull-request gate without its pull request, `PARTLY` for a gate that could read only part of what it checks; a batch that failed or ran out of time is not reviewed; a failed command is a refusal (`kit.Refused`). |
| 7. Every decision tool proves itself | `tools/red_proof.py` runs every self-test and replays every recorded mutation from `tools/red_proofs.json` on each run. |

## Section by section

| Part of the method | Implemented here as |
|---|---|
| `CLAUDE.md`: rules with gates | [CLAUDE.md](../CLAUDE.md). |
| Knowledge store, index, lint with floor and self-test | `knowledge/`, `INDEX.md`, `tools/lint_knowledge.py` (gate `knowledge`). |
| Routing and the memory audit | Rules in `CLAUDE.md`. No audit tool is part of this repository, and nothing here forces the wrap-up (HAZARD #6). |
| Zero findings on the store | Zero findings above low: `tools/pr_gates.py findings` (gate `pr-findings`) reads the review threads, and a thread on anything but tool, workflow and hook code is answered only by changing the file. A low finding opens no thread and holds no merge (below), so a low on a knowledge entry or a guide can reach `main` and is fixed later. After the fix push, `tools/pr_gates.py resolve --head <the pushed commit>` lists the reviewer's threads whose files all changed since the finding's commit and the rest with the reason, and `--go` resolves the former (`knowledge/resolve-what-the-edit-answered.md`); `--head` refuses the run while the forge still shows the old head (`knowledge/the-pull-request-shows-the-old-head-after-a-push.md`). |
| Skills | `.claude/skills/change-walk` and `.claude/skills/design-round`. |
| Hooks for shapes that already cost | One: writing code in the design phase, which happened on 2026-10-01. `.claude/settings.json` calls `tools/tree_gate.py --hook` for the Write and Edit tools of a session. It does not hold a write made through a shell command, or a hook the harness cut off at its timeout (HAZARD #4). The pre-push hook is the same gate before a push, in a clone that switched it on (HAZARD #4 where it is off). The CI gate `tree` runs on every pull request event and every push to `main`, after the push has published; a branch without a pull request, and a head commit with a skip literal, get no run. |
| The tools law | `tools/red_proof.py` and `tools/red_proofs.json` (gate `self-tests`). |
| Pipeline jobs | Two workflows: `gates.yml` with the one job `gates`, which runs `tools/gates.py`, and `review.yml` with the job `review-run`, which runs the reviewer. See "Gates" below for which of the method's jobs exist. |
| CI configuration is code | `tools/lint_ci.py` (gate `ci-config`): each fact is broken in the real workflow file by its self-test. |
| Test-quality clauses | The "Tests" section of `CLAUDE.md`, cut down to what a tree of tools can break; see "Not implemented". |
| Automated reviewer | `tools/review/review.py`, `.review/review-rules.yaml`, `.github/workflows/review.yml`. |
| Server-side merge checks | A repository ruleset, defined in `tools/ruleset.json`, asserted on every gates run (gate `merge-checks`). A change to that file is red in `merge-checks` until the owner applies it to the live ruleset (`merge_pr.py --apply-settings`); from then on every other open pull request is red until it takes the change from `main`. |
| Reading the review before merging, threads, one push per round | `tools/merge_pr.py` checks them at the moment of the merge; the round discipline is a rule in `CLAUDE.md`. |
| Gate-flip | `tools/merge_pr.py --over-red`. |
| CI triage | The "CI" section of `CLAUDE.md`. |
| Agent operations (isolation, notes file, owner queue, worktrees) | Outside this repository. Nothing here depends on it. |
| Living diagrams | Four entries tagged LIVING in `INDEX.md`; the lint requires each to have a diagram and its update triggers, and at least four of them to exist. |
| Living guides | This guide and the evidence guide. The tree gate keeps both portable. The evidence guide's numbers are generated, carry a digest that a hand edit breaks, and expire: when the TTL runs out, `knowledge` is red on every pull request and on `main`, whatever the change touches, until a pull request regenerates the page with `tools/quality_evidence.py`. That tool refuses while a self-test or the reference lint is red and needs a login that can read the forge; so once the TTL has run out, a red reference blocks the only way back to green until it is fixed. |
| Not in the method: the product is a language definition | `reference/` (rules with an id and a status), `corpus/` (one case per file) and `tools/lint_reference.py` (gate `reference`), which keeps the two consistent in shape. Nothing runs a case (HAZARD #1). |
| Product rules: the decisions a diff must respect (section 7.5 of the guide's 2026-10-07 version) | The design record `kurz-design.md` and `reference/`: one statement per decision, the owner's choice with its date and the options it was chosen against, *(assumed)* and *(proposed)* for what the owner has not decided, and `open` rules for the forks. The reviewer carries the rule "design record", which flags a decision without the owner's choice behind it, as the guide's reviewer carries the product rules. The corpus holds the cases; the gate `reference` checks their shape and that every sample is a case, and nothing runs one (HAZARD #1): whether a rule gets a case, and whether its expectation is right, are judgment steps. The product pass, the product acceptance and the knowledge-scout hook are not here; see "Not implemented". |

## Gates

The list of gates has one definition, `GATES` in `tools/gates.py`, and so have the verdicts: the
docstring of that file. The run is red when a gate ends FAIL (it found what it exists to find),
BROKEN (it could not start), NOT RUN in CI (a pull-request gate without its pull request), or
PARTLY (it named something it could not read) unless that gate is listed in `PARTLY_OK` with the
issue that records the unread part. One gate is listed: `merge-checks` ends PARTLY on every CI
run, because the job's token cannot read the bypass list and the auto-merge setting (HAZARD #7).
For that one gate the required check `gates` is green although a part was not read; the table at
the end of the log shows PARTLY, never PASS. A run in which no gate ran is red.

The gates, with what turns each one red, are printed in the evidence guide's generated block;
the picture is `knowledge/diagram-gate-map.md`. Of the jobs the method lists:

- **Exist here:** the knowledge lint, the secret scan (part of the tree gate), title hygiene, the breadth gate, the
  review, the reviewer's own unit suite (run with every other self-test), and the verdict idea,
  which here is the runner itself.
- **Have nothing to check:** compile, unit, end-to-end, UI quality, migration drift, coverage,
  the complexity ratchet, the secrets gate for deploy directories, the deploy lane. There is no
  product code, no database and no deployment.

GitHub's own secret scanning and push protection are enabled for this repository
(`security_and_analysis` in the answer of `gh api repos/<owner>/<name>`, read 2026-10-02). They
are a setting of the forge, not a gate of this repository: nothing here asserts that they stay
on, and the tree gate does not rely on them.

## What is different on GitHub

| GitLab, in the method | GitHub, here |
|---|---|
| "All threads must be resolved" | Ruleset rule `pull_request` with `required_review_thread_resolution`. |
| "Pipelines must succeed" | Ruleset rule `required_status_checks`, each check pinned to the GitHub Actions app. The pin keeps out other apps and users. It does not tell one workflow run of this repository from another: a workflow that a branch adds can report a check or post a status of the same name (HAZARD #11). What stands against that is the review of every change to a workflow, and that only people who may write here can push a branch. |
| "Skipped pipelines count as successful: off" | No such switch. A job skipped by `if:` reports success, so the `gates` job has no `if:` anywhere, and the review's required check is not a job but the commit status `review`, which the reviewer posts when a review completed (`knowledge/a-skipped-job-reports-success.md`). That status satisfied the pinned check on 2026-10-07: pull request 22 was mergeable on it alone and merged with no waiver through the fixed copy of the merge tool on the branch that carried the fix (issue #5 closed); the acceptance was seen once, is re-checked by nothing (HAZARD #24) and fails closed, since a status the ruleset stopped accepting leaves the merge pending. The tool reads the status from the list of statuses, an endpoint that names the creator, which the combined status does not (`knowledge/the-combined-status-drops-the-creator.md`). |
| Draft lane as a blocking manual job | A Draft cannot be merged. Marking it Ready starts the review where the forge starts the workflow, which depends on the event policy two rows down, except the Ready event of a head labelled `reviewed-<its sha>`, which a completed off-pipeline review of that head adds (the job's `if`, pinned by `ci-config`; every other head is reviewed by the run its event starts, and the skipped job reports a check named `review-run`, never the required status `review`); a run that started is a bill, cancelled or not (`knowledge/a-cancel-does-not-beat-the-runner.md`). A Draft, and a pull request from outside, is reviewed on demand: `gh workflow run review.yml -f pr=N`, started on the default branch. |
| Rules fetched from the target branch | `pull_request_target`: workflow, reviewer and rules all come from the default branch (`knowledge/the-review-runs-the-default-branch.md`). A pull request into another branch is not reviewed in CI. |
| A merge-request pipeline needs no permission to start | GitHub's default policy blocks `pull_request_target` in a public repository unless an Actions event policy allows it. Whether the rule already blocks here or only evaluates was not known on 2026-10-02 (`knowledge/pull-request-target-is-blocked-by-default.md`). The policy is the owner's setting and nothing asserts it (HAZARD #10). Without it the review is dispatched by hand for each head. |
| Unanchored thread for the lows | Lows are not threads here (the owner's decision, 2026-10-02). The reviewer collects them as comments on the one open issue labelled `review-lows`, and leaves a note on the pull request that lists them, so that a later run does not report them again. They hold no merge; they are fixed together, or with a push that is needed anyway. |
| The size of a thread | A thread carries at most twenty findings, each with its title cut at 200 and its text at 2,000 characters, and further findings go into further threads. The forge's limit for one comment is taken as 65,536 characters. Rendering widens a text (a marker opened in a body is escaped, a quote or a character outside ASCII in a title is escaped in the marker's JSON), so a thread, a comment on the lows issue and a note of lows are each measured as rendered, and split until every part fits; self-test cases render findings made of such characters and check every part. The number itself is not in the forge's REST reference (looked for on 2026-10-02) and was not provoked here. |
| No per-request override of the pipeline check | GitHub has one: an actor on a ruleset's bypass list, set to "For pull requests only", can merge a pull request over unmet rules while the ruleset stays on for everything else (GitHub docs, "Creating rulesets for a repository", read 2026-10-02). It is not used here. A bypass is a standing permission of a role, which every session holding the owner's token would have for every pull request; the forge records each bypass in the repository's rule insights (GitHub docs, "Managing rulesets for a repository", read 2026-10-03), but as an act of that role, tied to no approval of one pull request and head, which is the rule here. So the bypass list is kept empty. Only a local `merge-checks` run with the owner's login asserts that: the job token in CI cannot read the list, prints NOT CHECKED and ends PARTLY (HAZARD #7), so an actor added to the list turns no CI run red. The gate-flip instead switches the ruleset's enforcement off for one merge, restores it and reads it back. Its price: while it is off nothing on the server holds any pull request or a push to `main`. |
| Pipeline-control literals in the title | The workflow-skip literals. The merge tool writes the title and the description into the squash commit; a squash by the button takes the commit's title (one commit) or the pull request's (several) and the commit messages, as the repository's settings say, and whatever is typed into its dialog. `pr-title` refuses a literal in the title, the description and the commit messages; the dialog's text is read by no gate (HAZARD #14). |
| Editing title or description starts no pipeline | The gates do run on `edited`. The review does not: the description is part of its cache key, and every edit would cost a full review. So after an edit the `review` status still stands, on text the review never read. Dispatching the review again after an edit that changes what the text says is an instruction, not a gate (HAZARD #13). |
| Trigger jobs must never be waived | There are none: `tools/lint_ci.py` refuses a job that calls another workflow. |
| Diff versions to tell whether a file changed since a finding | The blob id of the file at the finding's commit against the blob id at the head. |
| A merge only through the tool | The ruleset holds a merge by the button to the required checks and resolved threads. What only `tools/merge_pr.py` adds (the exact head, the last scan of title and description, the read-back of the settings) a button merge skips (HAZARD #14). |

## The reviewer

One job, one rules file, every pull request into the default branch, no path allowlist. It
follows the method's design: a read-only model call with no tools and an empty working
directory; the pull request as data in tags with a random suffix; batches of about thirty
thousand characters; each batch reviewed again until a pass adds nothing above low (at least two
passes, at most five); a time budget counted from the job's start; a replay cache of per-file
diff hashes whose key covers the script, the rules, the model, the title and the description;
notes that are never silent and never repeated.

What is specific to this repository:

- **The model is pinned by exact id** in `review.py`, and an answer from any other model is
  refused. **The effort is pinned next to it**: it is asked for through the call's environment,
  and the answer has no field that would confirm it (no field names the effort in the answers of
  CLI 2.1.283 captured on 2026-10-01 and 2026-10-02, `tools/review/fixtures/cli-*.json`). The model call gets an environment built
  from an allow-list: what a process needs to start and to find its login, and nothing else. No
  forge token, no variable of a Claude session that happens to run the script
  (`knowledge/a-headless-call-inherits-its-session.md`).
- **A run that fails or runs out of time keeps what it has.** It prints every finding, posts
  what the finished passes found, stores the files whose batches converged and whose findings
  the forge took, and then ends red. The next run replays those files and reviews the rest. A
  batch that had not converged starts again at its first pass: what it found is posted, its
  passes are paid again. The first version here, until 2026-10-02, posted nothing unless every
  batch had run, and
  let a batch that ran out of time after one pass stand as reviewed.
- **A batch at the pass cap counts as reviewed, and the review says so.** After five passes that
  still found something above low, the review completes: the status reads "NOT converged on N
  files" and a note names them. Their files are not stored, so the next run reads them again.
- **Passes run in rounds.** Every batch is read once before any is read twice. The first version,
  until 2026-10-02, gave each batch all its passes in turn, which on a large diff spends the budget on the first
  batches and never reads the last ones (`knowledge/what-a-review-pass-costs.md`).
- **A finding is in the log before it is posted, and posts are paced.** Every finding is printed
  in full, with what the passes cost, before the first post; the log takes every character
  (UTF-8, whatever the console's code page), and what is printed is already the withheld text of
  the next bullet, so the public Actions log shows no more than a post would; posts go out a
  second apart and wait on the forge's rate-limit refusal, and once the waits are spent the
  posting stops (`knowledge/a-paid-result-is-printed-before-it-is-posted.md`).
- **The log is written line by line**, one line per pass with its duration. A pass takes minutes,
  and a log that fills only at the end hides a run that will not finish.
- **A pull request from outside is not reviewed automatically.** The repository is public and a
  review spends the owner's Claude seat, so the job runs for the owner, members and collaborators.
  The owner dispatches the workflow for anyone else.
- **A run outside CI is an audit, not a pass.** `review.py --pr N --local` posts findings and an
  audit note with the git blob ids of the script and the rules, the model and whether it
  converged. It posts no `review` status, so a merge after it needs the owner's approval for
  that item. Such a run can be limited by its caller: `--passes N` gives a batch at most N
  passes, and with `--passes 1` every batch is read once. A batch whose last allowed pass still
  found something above low is at its cap: the note says "converged: no" and names the limit.
  A run that posts the status does not take the option, so no caller can make the required
  check cheaper than two passes.
- **The bill is counted before it is named.** `review.py --pr N --plan` prints the batches a run
  would read and the passes that is, calls no model and posts nothing. A run off the pipeline is
  held to that count, and the owner's go-ahead is for a bill named from it; a run in CI starts
  without a question (the owner's decision, 2026-10-07), and the report names the run's own count
  and cost (`CLAUDE.md`, "Tests", rule 6).
- **Text is checked when it is read, before it is printed or posted.** A finding's title and
  body are withheld where they hold a credential shape or a machine-bound string as the model's
  answer is parsed (`public` in `review.py`), so the log and the posts show the same text; a
  failure message is withheld the same way; a post that still holds one is refused by the tool
  itself.

The credential is one repository secret, `CLAUDE_CODE_OAUTH_TOKEN`. Without it the job is red: a
review that cannot run must never look like a review that found nothing. Every workflow run of a
branch of this repository can read it, a workflow that the branch adds included (HAZARD #11); an
environment limited to the default branch would close that, and is the owner's choice.

## Hazards

Each rule labelled HAZARD has an open issue with the label `hazard`, and the rule names its
number. A rule labelled `judgment step` has neither gate nor issue: it says that a person or a
session decides. The table at the end of `CLAUDE.md` says which practice is enforced by a
mechanism and which only by instruction. An issue closes when its gate exists, and the gate map
changes in the same pull request.

## Not implemented, and why

- **A vendored copy of the tutorial.** It belongs to its authors; this guide maps it instead.
- **The clauses about browser tests** (clicks that prove reachability, render-time assertions,
  option order, teardown filters, geometry probes) and the build-time confidentiality gates for
  telemetry. There is no user interface and no telemetry.
- **Ratchets and convention tests over product code.** There is no product code. The one
  convention here is over the tools themselves: every decision tool has a self-test and a
  recorded red proof.
- **A scheduled job that asserts the merge settings.** The assertion runs with every gates run
  instead. The bypass list and the auto-merge switch need a token the job does not have; the job
  prints NOT CHECKED for both and the gate ends PARTLY, as "Gates" above describes. A local run
  with the owner's login checks them (HAZARD #7).
- **A check that the `review` status came from a completed review.** Any workflow run of this
  repository can post it (HAZARD #11). Closing that needs an identity a pull request's own
  workflows cannot use, which is the owner's choice.
- **A check of the author and committer address** that a push publishes (HAZARD #12).
- **The launcher, the per-window isolation, the worktree-removal script.** They belong to a
  machine that runs many sessions at once.
- **A token-usage block in the evidence.** Only a machine with the session logs could fill it.
- **Running the corpus.** Nothing in this repository executes Kurz. The reference lint checks the
  shape of a case and that the reference shows it unchanged; whether an expected output or error
  is right is decided by reading and by the review (HAZARD #1).
- **The product pass, the product acceptance and the knowledge-scout hook** (section 7.5 of the
  guide's 2026-10-07 version). They judge a change to a product's surfaces, for users who pay, see
  and change things. This repository has no product surface; its questions to the owner are design
  questions, asked by the skill `design-round` before anything is derived from them.
- **Agent-behaviour plugins.** The guide's 2026-10-07 version installs none; its section 13 says why
  the two it once used were removed. Nothing here depends on one. The one `ponytail:` comment in
  `tools/` names a shortcut's ceiling and its upgrade path.

## Changing the bar

A change to a gate, a tool, a hook, a workflow or a review rule carries, in the same pull
request: an update of `knowledge/diagram-gate-map.md` or of this guide, whichever describes what
changed, or a sentence in the description that says why neither applies; a "Blast radius"
section in the description; and, for a decision of a tool, a hook or a workflow, its case and its
red proof (a review rule has no self-test). The gates for it are `self-tests` for the proofs,
`pr-breadth` for the section, and the review rule "every pull request", which flags a change whose
list of files holds neither the gate map nor this guide while the description gives no reason; a
review rule holds only above low, so for a low breach it is a `judgment step`.
