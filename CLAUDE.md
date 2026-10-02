# CLAUDE.md — Kurz

Kurz is a programming language in its design phase. This repository is **public**. It holds the
design record, the language reference with its conformance corpus, a knowledge store and the
quality tools that gate them. Nothing here compiles Kurz yet, and nothing runs the corpus.

Load [INDEX.md](INDEX.md) first and open an entry when its hook matches the task. Long procedures
are skills in `.claude/skills/`: `change-walk` (branch, gates, pull request, review, merge) and
`design-round` (a round of design questions and its record).

## Build & Test

- `py -3 tools/gates.py` runs every gate; `--pr N` adds the pull-request gates. CI runs the same
  file with `python3`. Its last line counts PASS, PARTLY, FAIL and NOT RUN. A NOT RUN is not a
  pass, and neither is PARTLY: that gate named something it could not read. Gate: the `gates` job.
- `py -3 tools/<tool>.py --self-test` runs one tool's cases. A changed decision needs a case, and
  the case needs an entry in `tools/red_proofs.json` naming the mutation that turns it red.
  Gate: `self-tests` (`tools/red_proof.py` replays every mutation).
- `py -3 tools/lint_reference.py --sync` rewrites every sample in `reference/` from its corpus
  file. Change the `.kz` file, then sync: a sample edited by hand is red until it is overwritten.
  Gate: `reference`.
- Once per clone: `git config core.hooksPath .githooks`. Without it a push skips the tree gate.
  Gate: `push-hook`, in local runs only; HAZARD (#4) on a clone where nobody runs the gates.
- The only dependency is PyYAML, pinned with its hash in `tools/review/requirements.txt`.
  Gate: `ci-config` (pip must run with `--require-hashes`).

### Stale output and phantom results: check before debugging the diff

- A local run sees the working tree, untracked files included. CI sees commits. A file you did
  not stage is the usual reason for "green here, red there".
- CI also sees what the gates themselves create, and runs Python the way a clean machine does.
  The first run was red on `tools/__pycache__/` and on one red proof that read stale bytecode:
  the machine the tools were written on never writes bytecode. Such files are ignored in this
  repository's own `.gitignore`, and the proof replay writes none. Gate: `self-tests`
  ([knowledge/stale-bytecode-hides-a-mutation.md](knowledge/stale-bytecode-hides-a-mutation.md)).
- The review in CI runs the **base branch's** reviewer and rules, never the pull request's. A
  change to `tools/review/` or `.review/` reviews nothing until it is merged.
- Numbers in `guides/quality-bar-evidence.md` are generated. Run `py -3 tools/quality_evidence.py`;
  an edit by hand is overwritten and its date lies. Gate: `knowledge` (the TTL).
- Files under `tools/**/fixtures/` are payloads the platform really sent. Capture a new one;
  do not write one. Gate: review rule "tools".
- A pull request with a merge conflict starts no `gates` run: its required check stays pending.

## Tests: every decision is proven

1. **A case must be able to fail.** A negative case carries a positive control; an `all()` over a
   collection that can be empty checks that it is not. Gate: review rule "tools".
2. **Every case was seen red.** The mutation is recorded and replayed on every run, so a gate
   that went soft turns the build red. Gate: `self-tests`.
3. **Unchecked never looks clean.** A scan that is satisfied by finding nothing has a floor; a
   failed command is a refusal; a gate that could not run prints NOT RUN and is red in CI; a
   gate that could read only part says which part and ends PARTLY, never PASS.
   Gate: `self-tests` (each tool has its floor and refusal cases).
4. **No retry of a failure.** A red gate is fixed, not re-run; a retry is for infrastructure,
   after you know why it failed. `judgment step`
5. **An expectation nobody runs is not a proof.** No compiler runs `corpus/`. The lint checks the
   shape of a case; whether its expected output or error is right is decided by reading it
   against the rules it names. HAZARD (#1); review rule "corpus" carries the defect shapes.

## CI

- Two required checks on a pull request into `main`: `gates` (the job) and `review` (a commit
  status that only a completed review posts). Gate: the ruleset, asserted by `merge-checks`.
- `gates` red: read the `=== gates` table at the end of the log and fix the first FAIL.
- `review` pending: nothing reviewed this head. A Draft and a pull request from outside are
  reviewed on demand: `gh workflow run review.yml -f pr=N`.
- `review` red: read the run's last line, `REVIEW DID NOT COMPLETE (kind)`. `usage-limit`: wait
  for the reset, do not re-run now. `credential`: the owner fixes the secret. `budget`: re-run
  it; what the run found is posted and what converged is replayed, so the next run continues.
  `rules`, `oversized`: the line says what to do.
- A branch behind `main` fails what `main` already fixed: update the branch before debugging.

## Architecture

`kurz-design.md` records what Simon decided. `reference/` states the same design rule by rule,
each rule with an id and a status, and `corpus/` holds its cases: one small program per file, with
what it prints or the error it raises. `knowledge/` and `guides/` hold what a later session must
know; `INDEX.md` is their index.
`tools/` holds the gates (one runner, `gates.py`), the reviewer and the merge tool; `.review/`
holds what the reviewer enforces; `.github/workflows/` runs both. The map of every gate is
[knowledge/diagram-gate-map.md](knowledge/diagram-gate-map.md).

## Critical Rules

### A rule ships with its gate (hard rule)

A rule lands together with the test, lint or job that goes red when someone breaks it, named at
the end of the rule. If no gate can be built it is a HAZARD, labelled as one, with an issue. Human
judgment is labelled `judgment step`. Never an ungated rule. Gate: review rule "rules for sessions".

### The design record

- **`kurz-design.md` is the one record of the design.** A decision goes in once Simon has chosen
  it. Something proposed and not objected to is marked *(assumed)*. A question nobody has answered
  goes under "Open" in the turn it comes up, not only into chat. Gate: review rule "design record".
- **Design phase means brainstorming, not building.** No compiler, runtime or library code until
  Simon says build. On 2026-10-01 a v0 compiler was built after asking only for a name and a
  toolchain; every pick in it was void. Gate: `tree` (CI), the write-time hook and the pre-push hook.
- **A design round is numbered questions**, each with its options, what every option costs and a
  stated lean; Simon answers by number. A wish that cannot hold is contradicted in the reply, not
  recorded as decided. `judgment step`
- Retired on 2026-10-01: "after every design round, commit the record and push it to `main`".
  Nothing is pushed to `main` any more; a round's record takes the same walk as every change.

### The reference and the corpus

- **The reference adds nothing to the design.** A rule is `decided` or `assumed` only with the
  section of `kurz-design.md` it comes from. What the record does not say is `proposed` when a
  case has to stand on it, and `open` when it is a fork. Neither is a decision; a design round
  asks about them by id. Gate: `reference` for the cited section; that a rule says no more than
  its section is review rule "reference".
- **An answer reaches the record first.** A rule becomes `decided` in the pull request that
  writes Simon's choice into `kurz-design.md`, never before. Gate: `reference` (a decided rule
  cites a section that exists); the order is a `judgment step`.
- **Every Kurz sample in the reference is a corpus case**, shown under a `Case:` line that links
  its file. Gate: `reference`.
- **No case stands on an open rule**: it would bake in a pick nobody made. A rule that can have
  no case says why in a `No case:` line. Gate: `reference`.
- **An error a case expects has an id in the error table** (`reference/12-errors.md`), and the
  table names the rules that raise it. Gate: `reference`.

### A public repository

- **Future plans stay out**: what gets built when, steps, milestones, schedules, which program
  comes first, when the compiler is rewritten. Not in a file, a commit message, a pull request,
  an issue. That the design holds a thing is not a plan; when it gets built is. Simon keeps the
  plan local (2026-10-01). HAZARD (no gate before the push; #2); the reviewer flags it after.
- **Nothing bound to a machine, a person or another workspace**: no absolute path, drive letter,
  profile directory, private address, e-mail address, or another project's internal name.
  Gate: `tree` and the pre-push hook for the shapes; `pr-title` for the title and the description,
  which become the squash commit on `main`; review rule "every pull request" for names.
- **A push cannot be taken back.** A history rewrite leaves the old commits reachable on GitHub.
  Run the gates before the push, not after. Gate: the pre-push hook; HAZARD (#4) where it is off.

### Git, pull requests & merging

- **Nothing reaches `main` except through a pull request** that is green on its head, reviewed,
  and has every thread resolved. Gate: the ruleset (`tools/ruleset.json`), asserted by `merge-checks`.
- **Merge with `py -3 tools/merge_pr.py <pr> <full head sha>`**, never with a button or
  `gh pr merge`. Gate: the ruleset refuses what the tool would refuse; the tool adds the exact head.
- **Merging over a red or missing check needs Simon's approval for that one pull request and
  head**, passed as `--over-red`. An approval is never standing. HAZARD (#3: the session holds the
  owner's token; the permission classifier and this rule are the only guards).
- **One push per review round.** Read every thread, fix everything, push once. Never push to
  cancel a running review. `judgment step`
- **A review finding is answered by an edit.** On the design record, the reference, the corpus,
  the knowledge store and rule files the file must change before the thread is resolved; on tool
  and workflow code a written reply also counts. Lows on tool code that are not fixed in the round
  go to an issue labelled `review-lows`. Gate: `pr-findings`.
- **No workflow-skip literal** in a title, a description or a commit message. Gate: `pr-title`.
- **A change to the quality tools, or one over 15 files, says what it can break** in a "Blast
  radius" section. Gate: `pr-breadth`; the reviewer audits the section against the diff.
- **A change to a gate, tool, hook or review rule updates the gate map and the guide** in the same
  pull request, or says why neither applies. Gate: review rule "every pull request".

### Knowledge

- **Facts are routed by kind.** A rule with its gate: this file. A durable lesson, trap or recipe:
  one file in `knowledge/` plus its `INDEX.md` line, in the same pull request. In-flight state:
  issues. A fact bound to this machine or to Simon: private agent memory, never here.
  Gate: `knowledge` for the store's shape; routing itself is a `judgment step`.
- **Wrap-up:** before a task's final message, promote every lesson that lives only in private
  memory into `knowledge/` and delete the private copy; run the machine's memory audit.
  HAZARD (no gate; #6).
- **The third time you hand-write the same check, it becomes a tool** with a self-test and a
  knowledge entry, in the same session. `judgment step`

## Reference Docs

- [guides/quality-bar.md](guides/quality-bar.md): how each part of the quality bar is implemented
  here, and what is not. [guides/quality-bar-evidence.md](guides/quality-bar-evidence.md): its numbers.
- Never in repository markdown: task lists, handoff notes, backlogs, status, plans.

## What is enforced here (checked 2026-10-02)

| Enforced by a mechanism | Only by instruction (HAZARD) |
|---|---|
| what may exist in the tree, and no credential or machine-bound string (`tree`, hooks) | future plans stay out (#2) |
| merge checks on `main` (ruleset; `merge-checks` asserts it on every run) | per-item approval for a merge over red (#3) |
| every tool decision proven red (`self-tests`) | the local hooks being switched on (#4) |
| workflow facts (`ci-config`) | wrap-up and the memory audit (#6) |
| store shape and expiring numbers (`knowledge`) | the bypass list and the auto-merge switch, in CI (#7) |
| the reference and the corpus agree in shape (`reference`) | a reference rule saying no more than the record section it cites (review only) |
| title, breadth and answered findings (`pr-*`) | record samples and corpus expectations being right: nothing runs them (#1) |
| a review that completed (`review` status) | that this status satisfies the ruleset (#5, unverified) |
