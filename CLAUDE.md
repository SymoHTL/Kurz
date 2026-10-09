# CLAUDE.md — Kurz

Kurz is a programming language in its design phase. This repository is **public**. It holds the
design record, the language reference with its conformance corpus, a knowledge store and the
quality tools that gate them. Nothing here compiles Kurz, and nothing runs the corpus.

Load [INDEX.md](INDEX.md) first and open an entry when its hook matches the task. Long procedures
are skills in `.claude/skills/`: `change-walk` (branch, gates, pull request, review, merge) and
`design-round` (a round of design questions and its record). `judgment step`

## Build & Test

The commands are written for Windows; elsewhere `py -3` is `python3`, as in CI.

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
- `py -3 tools/lint_reference.py --sync` rewrites every sample in `reference/` from its corpus
  file. Change the `.kz` file, then sync: a sample edited by hand is red until it is overwritten.
  Gate: `reference`.
- Once per clone: `git config core.hooksPath .githooks`. Without it a push skips the tree gate.
  Gate: `push-hook`, in local runs only, where it is red until that line was run; HAZARD (#4) on
  a clone where nobody runs the gates.
- What the CI jobs depend on: PyYAML, pinned with the hash of one wheel in
  `tools/review/requirements.txt` (the CPython 3.12 x86_64 wheel the image has: another Python
  fails the install, loudly), and in the review job the Claude CLI, pinned to an exact version in
  `.github/workflows/review.yml`. The runner is named by its versioned label: the forge offers no
  digest for a hosted image. Of the image the tools use python3, node (for the CLI), gh (every
  forge call) and git, none of them pinned: HAZARD (#15).
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
   collection that can be empty checks that it is not; a captured payload that holds none of the
   thing under test (the status fixture captured on 2026-10-01, before the first status, which
   the first status exposed on 2026-10-07) is captured again once the platform has sent one, and
   the case over it has a floor. Gate: review rule "tools" for the floor; the re-capture is a
   `judgment step`.
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
   prints it in full, in an encoding that cannot fail on a character, before the first write that
   can be refused; it paces its posts to the forge and waits on a rate-limit refusal. On 2026-10-02
   a review lost 88 findings that existed only in memory when the forge refused the posts; on
   2026-10-03 a run of 30 paid passes ended while printing, on an arrow the log's code page did
   not hold ([knowledge/a-paid-result-is-printed-before-it-is-posted.md](knowledge/a-paid-result-is-printed-before-it-is-posted.md)).
   Gate: `self-tests` (the reviewer's cases for the order, the encoding, the pace and the wait).
6. **A paid run starts on a bill that was counted, not remembered.** Every review run a session
   starts spends the owner's seat: a run off the pipeline, a dispatch
   (`gh workflow run review.yml`), and the run the forge starts by itself when a session marks a
   pull request Ready or pushes to a Ready one; the Ready run alone is skipped when the head
   carries the label `reviewed-<its sha>`, which a completed off-pipeline review of that head adds.
   A run in CI starts without a question to the owner: the question before every run, set on
   2026-10-02, was retired by the owner on 2026-10-07 after it had stalled every round; the bill
   is counted before the run and named in the report after it. A run off the pipeline spends the
   seat at local prices and starts only after the owner said go to a bill that was named. The
   bill is batches times passes times the price of a pass where it runs (in CI 0.19 to 0.27 USD,
   the top rounded up from 0.264,
   off the pipeline about 1.3 to 1.8 USD, measured in
   [knowledge/what-a-review-pass-costs.md](knowledge/what-a-review-pass-costs.md)), with the batches from
   `py -3 tools/review/review.py --pr N --plan` on the head that will be reviewed; a run in CI
   takes every pass up to the cap, so its bill is the plan's upper bound. On 2026-10-02 a bill was
   named from the 20 batches of an earlier run; the head had grown to 30, and the run was stopped
   at its first line ([knowledge/what-a-review-pass-costs.md](knowledge/what-a-review-pass-costs.md)).
   A run that started is a bill, cancelled or not: on 2026-10-07 a run cancelled while the forge
   still listed it as queued was already on a runner, and two of its batches finished a pass
   before the signal reached it ([knowledge/a-cancel-does-not-beat-the-runner.md](knowledge/a-cancel-does-not-beat-the-runner.md)).
   Gate: `self-tests` for the plan (it calls no model and posts nothing) and for the label a
   completed off-pipeline review adds, `ci-config` for the clause of the workflow that skips the
   Ready event of a labelled head; counting and naming the bill, and the go-ahead for a run off the
   pipeline, are a `judgment step`.
7. **An expectation nobody runs is not a proof.** Nothing runs `corpus/`. The lint checks the
   shape of a case; whether its expected output or error is right is decided by reading it
   against the rules it names ([knowledge/samples-obey-the-rules-beside-them.md](knowledge/samples-obey-the-rules-beside-them.md)).
   HAZARD (#1); review rule "corpus" carries the defect shapes.

## CI

- Two required checks on a pull request into `main`: the job `gates`, and the commit status
  `review`, both `success` on a head that is up to date with `main`. A merge into `main` therefore
  puts every other open pull request behind: its branch takes `main`, the new head gets a new
  `gates` run and needs a new `review` status, which only another review run posts.
  Gate: the ruleset, asserted by `merge-checks`.
- The `gates` job runs the pull request's own copy of the gates: a change that weakens a gate is
  judged by the weakened gate. HAZARD (#16); the reviewer, which runs the default branch's rules,
  flags a weakened gate without its reason.
- The status `review` is posted by a review run: `success` when a review completed, `error` when
  the run ended as `REVIEW DID NOT COMPLETE`. Without a run the check stays pending. A review that
  stopped at the pass cap with defects above low still open completed, and posts `success` with
  "NOT converged" in its description: HAZARD (#17). Nothing proves that a status of this name came
  from a completed review: every workflow run of the repository, and everyone who may write
  statuses, can post it. HAZARD (#11). The pinned check accepts this status: seen once, on
  2026-10-07, when pull request 22 was mergeable on it alone and merged with no waiver through the
  fixed copy of the merge tool on the branch that carried the fix (#5, closed); nothing re-checks
  the platform on that, HAZARD (#24), and it fails closed: a status the ruleset stopped accepting leaves the
  merge pending, never open. The tool reads the status from the list of statuses, an endpoint that names
  the creator, which the combined status does not ([knowledge/the-combined-status-drops-the-creator.md](knowledge/the-combined-status-drops-the-creator.md)).
  Gate: `self-tests` for the tool's read of the captured status; the platform's acceptance is a
  fact seen, not a gate.
- `gates` red: read the `=== gates` table at the end of the log and fix the first row that turned
  the run red: FAIL, BROKEN or NOT RUN, or PARTLY on a gate that is not listed (the docstring of
  `tools/gates.py` is the one definition). A red `pr-title` or `pr-breadth` is fixed by editing
  the title or the description, which runs the job again; the others by a push. `judgment step`
- `review` pending: no review of this head has finished. Look for a running `review` run in the
  Actions list before a dispatch: a new run cancels it, and both are billed. A Draft and a pull
  request from outside are reviewed on demand: `gh workflow run review.yml -f pr=N`.
  `judgment step`
- `review` pending on a Ready pull request, and no `review` run in the Actions list at all:
  [knowledge/pull-request-target-is-blocked-by-default.md](knowledge/pull-request-target-is-blocked-by-default.md).
  Dispatch the review as above. The policy that allows the event is the owner's setting:
  HAZARD (#10).
- `review` pending on a Ready pull request whose head carries the label `reviewed-<that head's
  sha>`: by design, the review of that head ran off the pipeline, which posts no status; the
  merge needs the owner's approval for that head (`--over-red`). Any other
  head is reviewed by the run its event starts, a push to the Ready pull request or the Ready of a
  head pushed during the Draft; a head whose run did not start gets a dispatch. Gate: `ci-config`
  pins the clause; asking the approval is a `judgment step`.
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

`kurz-design.md` records what the owner decided. `reference/` states the same design rule by
rule, each rule with an id and a status, and `corpus/` holds its cases: one small program per
file, with what it prints or the error it raises. `knowledge/` and `guides/` hold what a later
session must know; `INDEX.md` is their index. `tools/` holds the gates (one runner, `gates.py`),
the reviewer and the merge tool; `.review/` holds what the reviewer enforces;
`.github/workflows/` runs both. The map of every gate is
[knowledge/diagram-gate-map.md](knowledge/diagram-gate-map.md).

## Critical Rules

### A rule ships with its gate (hard rule)

A rule lands together with the test, lint or job that goes red when someone breaks it, named at
the end of the rule. If no gate can be built it is a HAZARD, labelled with the number of its
issue. Human judgment is labelled `judgment step`. Never an ungated rule. A rule whose gate is a
review rule is held only for a breach the reviewer rates above low: a low finding opens no
thread and holds no merge (below), so such a gate is a `judgment step` for the lows.
Gate: review rule "rules for sessions".

### The design record

- **`kurz-design.md` is the one record of the design.** A statement in it is decided, or marked
  *(assumed)* because it was proposed and not objected to, or listed under "Open"; a decision is
  traced to the owner's choice in the pull request's description. Gate: review rule "design
  record". That the owner did choose it, and that a question from the conversation reached "Open"
  in the turn it came up: `judgment step`.
- **A code sample in the record obeys every rule beside it.** Nothing runs a sample; each one is
  read against the rules of its section before it goes in
  ([knowledge/samples-obey-the-rules-beside-them.md](knowledge/samples-obey-the-rules-beside-them.md)).
  HAZARD (#1); the review rule "design record" flags the shapes it can see in one batch.
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

### The reference and the corpus

- **The reference adds nothing to the design.** A rule is `decided` or `assumed` only with the
  section of `kurz-design.md` it comes from. What the record does not say is `proposed` when a
  case has to stand on it, and `open` when it is a fork. Neither is a decision; a design round
  asks about them by id. Gate: `reference` for the cited section; that a rule says no more than
  its section is review rule "reference".
- **An answer reaches the record first.** A rule becomes `decided` in the pull request that
  writes the owner's choice into `kurz-design.md`, never before. Gate: `reference` (a decided
  rule cites a section that exists); the order is a `judgment step`.
- **Every Kurz sample in the reference is a corpus case**, shown under a `Case:` line that links
  its file. Gate: `reference`.
- **No case stands on an open rule**: it would bake in a pick nobody made. A rule that can have
  no case says why in a `No case:` line. Gate: `reference`.
- **An error a case expects has an id in the error table** (`reference/12-errors.md`), and the
  table names the rules that raise it. Gate: `reference`.

### A public repository

- **Future plans stay out**: what gets built when, steps, milestones, schedules, the order in
  which parts are built, when a part is replaced. Not in a file, a commit message, a branch name, a
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
- **A push cannot be taken back.** A history rewrite leaves the old commits reachable on GitHub
  ([knowledge/a-force-push-does-not-unpublish.md](knowledge/a-force-push-does-not-unpublish.md)).
  Run the gates before the push, not after. Gate: the pre-push hook; HAZARD (#4) where it is off.
- **A false positive of the pre-push hook is fixed in the tree gate's patterns** (`tools/kit.py`,
  `tools/tree_gate.py`) with a case and its red proof, never by `git push --no-verify`.
  HAZARD (#4): nothing stops the flag.

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
  pull request or any push to `main`, so the tool reads the checks and the threads again once it
  is off and merges nothing when they changed (exit 1, the ruleset restored; the approval is
  given again for the new state, or not at all). What changes between that reading and the
  merge call is caught by nobody: nothing else merges or pushes meanwhile. Exit 3 means it was not read back as active, so it may still
  be off: say so at once, read it back and restore it as the skill `change-walk` says; `merge-checks` is red on every run until it is back.
  HAZARD (#3): the session holds the owner's token, so the permission classifier and this rule
  are the only guards on the approval.
- **One push per review round.** Read every thread, fix everything, mark the pull request Draft,
  push once, resolve the threads the push answered, then mark it Ready (the skill `change-walk`,
  step 7), so the one review and the gates run on a head with its threads resolved. Never push
  to cancel a running review; a push to a Ready pull request does cancel it. `judgment step`
- **A finding above low is a thread, and a thread is answered by an edit.** On the design
  record, the reference, the corpus, the knowledge store and rule files the file must change
  before the thread is resolved; on tool, workflow and hook code a written reply also counts. After
  the fix push, `py -3 tools/pr_gates.py resolve --pr N --head <sha> --go`, with the pushed commit,
  resolves the reviewer's threads whose files all changed since the finding's commit and leaves
  the rest open with the reason (a thread in which a person wrote among them); right after the
  push the forge still names the old head for a moment, and the tool refuses until it shows the
  pushed one (run it again,
  [knowledge/the-pull-request-shows-the-old-head-after-a-push.md](knowledge/the-pull-request-shows-the-old-head-after-a-push.md));
  a thread answered by a reply is resolved by hand
  ([knowledge/resolve-what-the-edit-answered.md](knowledge/resolve-what-the-edit-answered.md)).
  Gate: `pr-findings`.
- **Low findings open no thread and hold no merge** (the owner's decision, 2026-10-02). The
  reviewer collects them on the open issue labelled `review-lows`, where they are fixed together.
  A push that is needed anyway fixes the lows of its pull request too. A pull request needs no
  further round once a round reports nothing above low; a head that then takes `main` is a new
  head without a `review` status, and the run that posts one is a paid round like any other
  (Tests, item 6). Gate: `self-tests` for where the reviewer posts them; that they get fixed is a
  `judgment step`.
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

- **Facts are routed by kind.** A rule with its gate: this file. A durable lesson, trap or short
  recipe: one file in `knowledge/` plus its `INDEX.md` line, in the same pull request; a procedure
  longer than about fifteen lines: a skill in `.claude/skills/`. In-flight state:
  issues. A fact bound to one machine or one person: private agent memory, never here.
  Gate: `knowledge` for the store's shape; routing itself is a `judgment step`.
- **A postmortem lands as a rule here with its gate, a knowledge entry and its `INDEX.md` line**,
  in one pull request, never as a private note. Gate: review rule "every pull request" for a
  lesson the description names and no changed file can record; that the rule, the entry and the
  index line all three landed is a `judgment step`.
- **Wrap-up:** before a task's final message, promote every lesson that lives only in private
  memory into `knowledge/` and delete the private copy; a fact bound to one machine or one person
  is not a lesson and stays private, as the routing above says. HAZARD (#6).
- **The third time you hand-write the same check, it becomes a tool** with a self-test and a
  knowledge entry, in the same session. `judgment step`

## Reference Docs

- [guides/quality-bar.md](guides/quality-bar.md): how each part of the quality bar is implemented
  here, and what is not. [guides/quality-bar-evidence.md](guides/quality-bar-evidence.md): its numbers.
- Never in repository markdown: task lists, handoff notes, backlogs, status, plans.
  Gate: review rule "rules for sessions" for operational state; plans: HAZARD (#2).

## What is enforced here (checked 2026-10-03)

| Enforced by a mechanism | Only by instruction (HAZARD) |
|---|---|
| what may exist in the tree, and no credential or machine-bound string in files and commit messages (`tree`, the pre-push hook) | future plans stay out (#2) |
| merge checks on `main` (ruleset; `merge-checks` asserts it on every run and is red while it is off) | per-item approval for a merge over red, and the window in which the ruleset is off (#3) |
| every recorded tool decision still turns red (`self-tests`) | the local hooks being switched on, a write through a shell command, a hook cut off at its timeout (#4) |
| workflow facts (`ci-config`) | wrap-up (#6) |
| store shape, expiring numbers, generated numbers that match their digest (`knowledge`) | the bypass list and the auto-merge switch, in CI (#7) |
| the reference and the corpus agree in shape (`reference`) | a reference rule saying no more than the record section it cites (review rule "reference": `judgment step`) |
| title, description and commit messages of a pull request; breadth; answered findings (`pr-*`) | record samples and corpus expectations being right: nothing runs them (#1) |
| the reviewer's pinned model, the environment of its call, what a failed run keeps, and the merge tool's read of a status from the endpoint that names its creator (`self-tests`) | that the `review` status came from a completed review (#11), and that the ruleset still accepts the status: seen once on 2026-10-07, re-checked by nothing, failing closed (#24) |
| low findings collected on one issue instead of threads (`self-tests`) | that GitHub starts the review workflow: the event policy for `pull_request_target` (#10) |
| | the author and committer address a push publishes (#12) |
| | a title or description edited after the review (#13) |
| | a merge by the button, which skips what only the merge tool does (#14) |
| | the image's python3, node, gh and git, which no workflow pins (#15) |
| | the gates job judging a pull request with the pull request's own gates (#16) |
| | a review that stopped at the pass cap posting `success` (#17) |
| | the evidence digest, which a recomputed hash passes (#18) |
| | GitHub push protection staying on (#25) |
