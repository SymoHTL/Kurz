# CLAUDE.md — Kurz

Kurz is a programming language in its design phase. This repository is **public**. It holds the
design record, a knowledge store and the quality tools that gate them. Nothing here compiles Kurz.

Load [INDEX.md](INDEX.md) first and open an entry when its hook matches the task. Long procedures
are skills in `.claude/skills/`: `change-walk` (branch, gates, pull request, review, merge) and
`design-round` (a round of design questions and its record). `judgment step`

## Build & Test

- `py -3 tools/gates.py` runs every gate; `--pr N` adds the pull-request gates. CI runs the same
  file with `python3`. A gate ends as PASS, PARTLY, FAIL, BROKEN, NOT RUN or N/A; the docstring of
  `tools/gates.py` is the one definition of what each verdict does to the run. FAIL and BROKEN
  (the gate could not start) are red everywhere. NOT RUN (a pull-request gate without its pull
  request) is red in CI. PARTLY (the gate named something it could not read) is red, unless the
  gate is listed in `tools/gates.py` with the issue that records the unread part. The one listed
  gate is `merge-checks` (#7): it ends PARTLY on every CI run and `gates` stays green. A green
  `gates` therefore says "nothing wrong in what could be read"; the table at the end of the log
  says which gate read only a part. Gate: the `gates` job.
- `py -3 tools/<tool>.py --self-test` runs one tool's cases. A changed decision needs a case, and
  the case needs an entry in `tools/red_proofs.json` naming the mutation that turns it red.
  Gate: `self-tests` replays every recorded mutation and fails when one no longer turns its named
  case red. A decision that has no case is invisible to it: review rule "tools".
- Once per clone: `git config core.hooksPath .githooks`. Without it a push skips the tree gate.
  Gate: `push-hook`, in local runs only, where it is red until that line was run; HAZARD (#4) on
  a clone where nobody runs the gates.
- What the CI jobs depend on: PyYAML, pinned with its hash in `tools/review/requirements.txt`, and
  in the review job the Claude CLI, pinned to an exact version in `.github/workflows/review.yml`.
  The runner is named by its versioned label: the forge offers no digest for a hosted image, and
  the tools need nothing of it beyond a Python 3 and, for the CLI, its Node.
  Gate: `ci-config` (pip with `--require-hashes`, the CLI's exact version, the runner label).

### Before debugging the diff: stale output and phantom results

Each line names a trap; its evidence is in the entry it links.

- Green here, red in CI: a local run reads the working tree, CI reads commits
  ([knowledge/local-run-reads-the-working-tree.md](knowledge/local-run-reads-the-working-tree.md)).
  `judgment step`
- A mutation that still passes, in CI only or locally only: stale bytecode
  ([knowledge/stale-bytecode-hides-a-mutation.md](knowledge/stale-bytecode-hides-a-mutation.md)).
  Gate: `self-tests` (the replay writes no bytecode).
- A change to `tools/review/` or `.review/` reviews nothing until it is merged: the review in CI
  runs the default branch's workflow, reviewer and rules
  ([knowledge/the-review-runs-the-default-branch.md](knowledge/the-review-runs-the-default-branch.md)).
  Gate: `ci-config` (the checkout is pinned to the default branch).
- Numbers in `guides/quality-bar-evidence.md` are generated. Run `py -3 tools/quality_evidence.py`;
  a number typed by hand does not pass. Gate: `knowledge` (the digest of the blocks, and the TTL).
- Files under `tools/**/fixtures/` are payloads the platform really sent. Capture a new one;
  do not write one. Gate: review rule "tools".
- A head with no `gates` run at all, its required check pending: a merge conflict, or a
  workflow-skip literal in the head commit
  ([knowledge/a-skipped-job-reports-success.md](knowledge/a-skipped-job-reports-success.md)).
  Gate: the pre-push hook refuses a skip literal in a commit message; HAZARD (#4) where it is off.

## Tests: every decision is proven

1. **A case must be able to fail.** A negative case carries a positive control; an `all()` over a
   collection that can be empty checks that it is not. Gate: review rule "tools".
2. **Every recorded case was seen red.** The mutation is recorded and replayed on every run, so a
   gate that went soft turns the build red. Gate: `self-tests`.
3. **Unchecked never looks clean.** A scan that is satisfied by finding nothing has a floor; a
   failed command is a refusal; a gate that could not start is BROKEN; a pull-request gate without
   its pull request prints NOT RUN; a gate that could read only part says which part and ends
   PARTLY, never PASS. Gate: `self-tests` (each tool has its floor and refusal cases).
4. **No retry of a failure.** A red gate is fixed, not re-run; a retry is for infrastructure,
   after you know why it failed. One case is no retry: a review that stopped on `budget` is
   continued by running it again, because what converged is replayed. `judgment step`
5. **A paid result is in the log before it is posted.** A tool whose result cost money or time
   prints it in full before the first write that can be refused, paces its posts to the forge and
   waits on a rate-limit refusal. On 2026-10-02 a review lost 88 findings that existed only in
   memory when the forge refused the posts
   ([knowledge/a-paid-result-is-printed-before-it-is-posted.md](knowledge/a-paid-result-is-printed-before-it-is-posted.md)).
   Gate: `self-tests` (the reviewer's cases for the order, the pace and the wait).

## CI

- Two required checks on a pull request into `main`: the job `gates`, and the commit status
  `review`. Gate: the ruleset, asserted by `merge-checks`.
- The status `review` is posted by a review run: `success` when a review completed, `error` when
  the run ended as `REVIEW DID NOT COMPLETE`. Without a run the check stays pending. Nothing
  proves that a status of this name came from a completed review: every workflow run of the
  repository, and everyone who may write statuses, can post it. HAZARD (#11). Whether the pinned
  check accepts this status at all has not been seen: HAZARD (#5).
- `gates` red: read the `=== gates` table at the end of the log and fix the first row that is
  FAIL, BROKEN or NOT RUN, or PARTLY on a gate that is not listed. `judgment step`
- `review` pending: nothing reviewed this head. A Draft and a pull request from outside are
  reviewed on demand: `gh workflow run review.yml -f pr=N`. `judgment step`
- `review` pending on a Ready pull request, and no `review` run in the Actions list at all:
  [knowledge/pull-request-target-is-blocked-by-default.md](knowledge/pull-request-target-is-blocked-by-default.md).
  Dispatch the review as above. The policy that allows the event is the owner's setting:
  HAZARD (#10).
- A review run that failed (the status says `error`): read the run's last line,
  `REVIEW DID NOT COMPLETE (kind)`. `usage-limit`: wait for the reset, do not run it again now.
  `credential`: the owner fixes the secret. `budget`: run it again; what the run found is posted
  and what converged is replayed, so the next run continues. `base`: the pull request does not
  target `main`, so a status on its head would vouch for a diff nobody reviewed; retarget it, or
  review it off the pipeline. `rules`, `oversized`, `empty`, `bad-diff`: the line says what to do.
  `judgment step`
- A branch behind `main` fails what `main` already fixed: update the branch before debugging.
  `judgment step`

## Architecture

`kurz-design.md` records what the owner decided. `knowledge/` and `guides/` hold what a later
session must know; `INDEX.md` is their index. `tools/` holds the gates (one runner, `gates.py`),
the reviewer and the merge tool; `.review/` holds what the reviewer enforces;
`.github/workflows/` runs both. The map of every gate is
[knowledge/diagram-gate-map.md](knowledge/diagram-gate-map.md).

## Critical Rules

### A rule ships with its gate (hard rule)

A rule lands together with the test, lint or job that goes red when someone breaks it, named at
the end of the rule. If no gate can be built it is a HAZARD, labelled with the number of its
issue. Human judgment is labelled `judgment step`. Never an ungated rule.
Gate: review rule "rules for sessions".

### The design record

- **`kurz-design.md` is the one record of the design.** A statement in it is decided, or marked
  *(assumed)* because it was proposed and not objected to, or listed under "Open"; a decision is
  traced to the owner's choice in the pull request's description. Gate: review rule "design
  record". That the owner did choose it, and that a question from the conversation reached "Open"
  in the turn it came up: `judgment step`.
- **Design phase means brainstorming, not building.** No compiler, runtime or library code until
  the owner says build. On 2026-10-01 a v0 compiler was built after asking only for a name and a
  toolchain; every pick in it was void. Gate: `tree` (CI), the pre-push hook, and the write-time
  hook for the Write and Edit tools. A write through a shell command, and a hook cut off at its
  timeout, pass the write-time hook: HAZARD (#4).
- **The big design choices are asked before anything is derived from them.** The same incident;
  the procedure is the skill `design-round`. `judgment step`
- **A design round is numbered questions**, each with its options, what every option costs and a
  stated lean; the owner answers by number. A wish that cannot hold is contradicted in the reply,
  not recorded as decided. `judgment step`
- Retired on 2026-10-01: "after every design round, commit the record and push it to `main`".
  Nothing is pushed to `main` any more; a round's record takes the same walk as every change.

### A public repository

- **Future plans stay out**: what gets built when, steps, milestones, schedules, which program
  comes first, when the compiler is rewritten. Not in a file, a commit message, a branch name, a
  pull request, an issue. That the design holds a thing is not a plan; when it gets built is. The
  plan is kept outside this repository (2026-10-01). HAZARD (#2): no gate before the push; the
  reviewer flags a plan in the files, the title and the description afterwards, and reads no
  commit message and no branch name.
- **Nothing bound to a machine, a person or another workspace**: no absolute path, drive letter,
  profile directory, private address, e-mail address, a person's name where the role would do, or
  another project's internal name. Gate: `tree` and the pre-push hook for the shapes in files and
  commit messages; `pr-title` for the title, the description and the commit messages of the
  branch; review rule "every pull request" for names. The author and committer address of a
  commit is read by no gate: HAZARD (#12).
- **A push cannot be taken back.** A history rewrite leaves the old commits reachable on GitHub.
  Run the gates before the push, not after. Gate: the pre-push hook; HAZARD (#4) where it is off.

### Git, pull requests & merging

- **Nothing reaches `main` except through a pull request** that is green on its head, reviewed,
  and has every thread resolved. Gate: the ruleset (`tools/ruleset.json`), asserted by
  `merge-checks`. It does not hold while a merge over red has the ruleset switched off: see below.
- **Merge with `py -3 tools/merge_pr.py <pr> <full head sha>`**, never with a button or
  `gh pr merge`. Gate: the ruleset holds every merge to the required checks and the resolved
  threads. What only the tool adds (the exact head, the last scan of the title and the
  description, the read-back of the settings) is not held against a merge by the button:
  HAZARD (#14).
- **Merging over a red or missing check needs the owner's approval for that one pull request and
  head**, passed as `--over-red`. An approval is never standing. The tool switches the whole
  ruleset off for the one merge and restores it; while it is off nothing on the server holds any
  pull request or any push to `main`. Exit 3 means it is still off: say so at once and restore it
  as the skill `change-walk` says; `merge-checks` is red on every run until it is back.
  HAZARD (#3): the session holds the owner's token, so the permission classifier and this rule
  are the only guards on the approval.
- **One push per review round.** Read every thread, fix everything, push once. Never push to
  cancel a running review; a push to a Ready pull request does cancel it. `judgment step`
- **A finding above low is a thread, and a thread is answered by an edit.** On the design
  record, the reference, the corpus, the knowledge store and rule files the file must change
  before the thread is resolved; on tool and workflow code a written reply also counts.
  Gate: `pr-findings`.
- **Low findings open no thread and hold no merge** (the owner's decision, 2026-10-02). The
  reviewer collects them on the open issue labelled `review-lows`, where they are fixed together.
  A push that is needed anyway fixes the lows of its pull request too. A pull request needs no
  further round once a round reports nothing above low. Gate: `self-tests` for where the reviewer
  posts them; that they get fixed is a `judgment step`.
- **No workflow-skip literal** in a title, a description or a commit message. Gate: `pr-title`,
  and the pre-push hook for commit messages. `pr-title` cannot report a literal in the head
  commit of a pull request: that head gets no `gates` run at all.
- **A change to the quality tools, or one over 15 files, says what it can break** in a "Blast
  radius" section. Gate: `pr-breadth`; the reviewer audits the section against the list of
  changed files.
- **A change to a gate, tool, hook, workflow or review rule updates
  `knowledge/diagram-gate-map.md` or `guides/quality-bar.md`** in the same pull request,
  whichever describes what changed, or says in the description why neither applies.
  Gate: review rule "every pull request".
- **A title or description edited after the review is text the review never read.** Dispatch the
  review again before the merge. HAZARD (#13).

### Knowledge

- **Facts are routed by kind.** A rule with its gate: this file. A durable lesson, trap or recipe:
  one file in `knowledge/` plus its `INDEX.md` line, in the same pull request. In-flight state:
  issues. A fact bound to one machine or one person: private agent memory, never here.
  Gate: `knowledge` for the store's shape; routing itself is a `judgment step`.
- **A postmortem lands as a rule here with its gate, a knowledge entry and its `INDEX.md` line**,
  in one pull request, never as a private note. Gate: review rule "every pull request" (a lesson
  the description names and no changed file can record).
- **Wrap-up:** before a task's final message, promote every lesson that lives only in private
  memory into `knowledge/` and delete the private copy. HAZARD (#6).
- **The third time you hand-write the same check, it becomes a tool** with a self-test and a
  knowledge entry, in the same session. `judgment step`

## Reference Docs

- [guides/quality-bar.md](guides/quality-bar.md): how each part of the quality bar is implemented
  here, and what is not. [guides/quality-bar-evidence.md](guides/quality-bar-evidence.md): its numbers.
- Never in repository markdown: task lists, handoff notes, backlogs, status, plans.
  Gate: review rule "rules for sessions" for operational state; plans: HAZARD (#2).

## What is enforced here (checked 2026-10-02)

| Enforced by a mechanism | Only by instruction (HAZARD) |
|---|---|
| what may exist in the tree, and no credential or machine-bound string in files and commit messages (`tree`, the pre-push hook) | future plans stay out (#2) |
| merge checks on `main` (ruleset; `merge-checks` asserts it on every run and is red while it is off) | per-item approval for a merge over red, and the window in which the ruleset is off (#3) |
| every recorded tool decision still turns red (`self-tests`) | the local hooks being switched on, a write through a shell command, a hook cut off at its timeout (#4) |
| workflow facts (`ci-config`) | wrap-up (#6) |
| store shape, expiring numbers, generated numbers that match their digest (`knowledge`) | the bypass list and the auto-merge switch, in CI (#7) |
| title, description and commit messages of a pull request; breadth; answered findings (`pr-*`) | code samples in the design record being right (#1) |
| the reviewer's pinned model, the environment of its call, what a failed run keeps (`self-tests`) | that the `review` status satisfies the ruleset (#5, unverified), and that it came from a completed review (#11) |
| low findings collected on one issue instead of threads (`self-tests`) | that GitHub starts the review workflow: the event policy for `pull_request_target` (#10) |
| | the author and committer address a push publishes (#12) |
| | a title or description edited after the review (#13) |
| | a merge by the button, which skips what only the merge tool does (#14) |
