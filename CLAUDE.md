# CLAUDE.md — Kurz

Kurz is a programming language. This repository is **public**. It holds the design record, the
language reference with its conformance corpus, a knowledge store, the quality tools that gate
them, and `compiler/`, the one directory the tree gate admits for the compiler's files since the
change that retired the design-phase rule (pull request 29), after the owner said build on 2026-10-09.
The quality tools compile nothing; `compiler/` is where a compiler of Kurz is admitted, and its suite
builds and runs every corpus case the compiler emits, while the expectation of every other case is
read against its rules (HAZARD #1).

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
  case red. A decision that has no case is invisible to it: review rule "tools". The ledger is
  written one entry per line, two spaces in, text as it is, as `py -3 tools/red_proof.py --format`
  writes it: a ledger written back another way differs on every line, and the review reads and
  bills the whole file as a change ([knowledge/a-re-serialized-ledger-is-a-whole-file-diff.md](knowledge/a-re-serialized-ledger-is-a-whole-file-diff.md)).
  Gate: `self-tests`.
- `cd compiler && dotnet test Kurz.sln -c Release -p:RestoreLockedMode=true --disable-build-servers`
  builds the compiler and runs its unit tests, with the SDK that `compiler/global.json` pins (the
  dotnet host reads it from the working directory, so the command runs inside `compiler/`) and the
  packages the lock files pin; `compiler/README.md` says what the suites cover.
  `py -3 tools/compiler_gate.py` is that run as the gate reads it: red when `compiler/NuGet.config`
  is missing, is not well-formed XML, does not open its package sources with `<clear />` or maps no
  package to a source; on a failed test, a skipped test, fewer passed tests than its floor, no
  summary line at all, a non-zero exit with no failed test, a restore outside the lock files, a
  missing `dotnet` or a run out of time; then it runs `kurz --version`, prints what the command
  wrote, the compiler's version, the major release of LLVM the compiler pins and the clang it found
  with that clang's release, and is red where the command refused that clang or found none, as
  section 2 of the design record requires of it. The suites need the clang of that release, so on
  a machine without it the gate is red: run the gates in the image the `gates` job runs in
  ([knowledge/the-gates-job-runs-in-its-image.md](knowledge/the-gates-job-runs-in-its-image.md)).
  The same gate runs on Windows in the workflow `compiler-windows`, which installs the pinned
  release on every run and which the ruleset does not require: a red Windows run holds no merge
  (HAZARD #30). Gate: `compiler-tests`, in every gates run.
- `py -3 tools/lint_reference.py --sync` rewrites every sample in `reference/` from its corpus
  file. Change the `.kz` file, then sync: a sample edited by hand is red until it is overwritten.
  Gate: `reference`.
- Once per clone: `git config core.hooksPath .githooks`. Without it a push skips the tree gate.
  Gate: `push-hook`, in local runs only, where it is red until that line was run; HAZARD (#4) on
  a clone where nobody runs the gates.
- What the CI jobs depend on: PyYAML, pinned with the hash of one wheel in
  `tools/review/requirements.txt` (the CPython 3.12 x86_64 wheel the images have: another Python
  fails the install, loudly), and in the review job the Claude CLI, pinned to an exact version in
  `.github/workflows/review.yml`. The `gates` job runs in the image `ci/Dockerfile` defines, named
  in `gates.yml` by its digest: a base named by its digest, clang of the LLVM major the compiler
  pins (`Clang.PinnedMajor` in `compiler/Kurz.Compiler/Build/Clang.cs`, the one definition of that
  major, to which `ci-config` holds the image's `LLVM_MAJOR` and the Windows job's release), from the
  packages LLVM publishes, the SDK `compiler/global.json` pins and gh, each download
  checked by its hash or its key, as section 2 of the design record says (the owner, 2026-10-22,
  "Public GHCR image"). `.github/workflows/image.yml` builds it and publishes it to the GitHub
  container registry on a push that changes it; the walk of a change to it, and the local run of
  the gates in the same image, are in
  [knowledge/the-gates-job-runs-in-its-image.md](knowledge/the-gates-job-runs-in-its-image.md).
  The other jobs run on hosted runners named by their versioned labels: the forge offers no
  digest for a hosted image. Of those images the tools use python3, node (for the CLI), gh (every
  forge call) and git, of the Windows image zstd and tar, which unpack LLVM's archive there, and of
  the gates image python3, git and the point release of the pinned LLVM major, which the
  distribution's and LLVM's packages give on the day the image is built: none of them pinned to a
  version, HAZARD (#15). The Windows job installs the release `LLVM_VERSION` names, of the major the
  compiler pins, from a cache of LLVM's archive for Windows, which it checks by the SHA-256 in
  `LLVM_SHA256` before it unpacks it.
  Gate: `ci-config` (pip with `--require-hashes`, the CLI's exact version, the runner labels, the
  gates image by its digest and the facts of its Dockerfile, the LLVM major of the image and of the
  Windows job against the compiler's).

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
   not hold ([knowledge/a-paid-result-is-printed-before-it-is-posted.md](knowledge/a-paid-result-is-printed-before-it-is-posted.md));
   on 2026-10-30 the compiler gate on Windows ended so while it printed the output of a red test
   run, before it named the failing test ([knowledge/a-windows-log-takes-the-ansi-code-page.md](knowledge/a-windows-log-takes-the-ansi-code-page.md)).
   Gate: `self-tests` (the reviewer's cases for the order, the encoding, the pace and the wait, and
   the compiler gate's cases for the encoding, one of them through `main`).
6. **A paid run starts on a bill that was counted, not remembered.** Every review run a session
   starts spends the owner's seat: a run off the pipeline, a dispatch
   (`gh workflow run review.yml`), and the run the forge starts by itself when a session marks a
   pull request Ready or pushes to a Ready one; the Ready run alone is skipped when the head
   carries the label `reviewed-<its sha>`, which a completed off-pipeline review of that head adds.
   A run in CI starts without a question to the owner: the question before every run, set on
   2026-10-02, was retired by the owner on 2026-10-07 after it had stalled every round; the bill
   is counted before the run and named in the report after it. A run off the pipeline spends the
   seat at local prices and starts only after the owner said go to a bill that was named. The
   bill is batches times passes times the price of a pass where it runs (in CI 0.19 to 0.52 USD,
   the mean of a pass in the dearest run seen, 0.5119 on 2026-10-10, rounded up,
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
7. **An expectation nobody runs is not a proof.** The compiler's corpus runner (`RunTests` in
   `compiler/Kurz.Compiler.Tests/`) builds every case that expects a run and that the compiler
   emits, runs it, and holds what it prints, its exit code and its error line to the header; it
   lists each case it does not run with the construct or the reason, refuses a stale entry and
   has a floor. The lint checks the shape of every case; the expectation of a case the runner does
   not run is decided by reading it against the rules it names
   ([knowledge/samples-obey-the-rules-beside-them.md](knowledge/samples-obey-the-rules-beside-them.md)).
   Gate: `compiler-tests` for the cases the runner runs; HAZARD (#1) for the rest; review rule
   "corpus" carries the defect shapes.

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
- `review` pending: no review of this head posted a status. Look for a running `review` run in the
  Actions list before a dispatch: a new run cancels it, and both are billed. A Draft and a pull
  request from outside are reviewed on demand: `gh workflow run review.yml -f pr=N`.
  `judgment step`
- `review` pending on a Ready pull request, and no `review` run in the Actions list at all:
  [knowledge/pull-request-target-is-blocked-by-default.md](knowledge/pull-request-target-is-blocked-by-default.md).
  Dispatch the review as above. The policy that allows the event is the owner's setting:
  HAZARD (#10).
- `review` pending on a Ready pull request whose head carries the label `reviewed-<that head's
  sha>`: the job skips the Ready event of such a head by design, because its review ran off the
  pipeline, which posts no status; the merge needs the owner's approval for that head
  (`--over-red`). An unlabelled head current at Ready is reviewed by the Ready's run, a head pushed
  after Ready by its push's run; a head whose run did not start gets a dispatch. Gate: `ci-config`
  pins the clause; asking the approval is a `judgment step`.
- A review run that failed (the status says `error`): read the run's last line,
  `REVIEW DID NOT COMPLETE (kind)`. `usage-limit`: wait for the reset, do not run it again now.
  `credential`: the owner fixes the secret. `budget`: run it again; what the run found is posted
  and what converged is replayed, so the next run continues. `base`: the pull request does not
  target `main`, so a status on its head would vouch for a diff nobody reviewed; retarget it, or
  review it off the pipeline. `cap`: the pull request had its one review round; no run follows,
  and the rule "A pull request gets at most one review round" says what does. `rules`,
  `oversized`, `empty`, `bad-diff`: the line says what to do. A run that labelled the pull
  request is a round whatever kind it ends with (that rule), so running it again after any of
  them, `budget`, `usage-limit`, `head-moved` and the reviewer's own defects among them, takes the
  one round, and after it no run follows.
  `judgment step`
- A branch behind `main` fails what `main` already fixed: update the branch before debugging. At
  the review cap the update is a new head outside the cap's approval: ask the owner first.
  `judgment step`

## Architecture

`kurz-design.md` records what the owner decided. `reference/` states the same design rule by
rule, each rule with an id and a status, and `corpus/` holds its cases: one small program per
file, with what it prints or the error it raises. `knowledge/` and `guides/` hold what a later
session must know; `INDEX.md` is their index. `tools/` holds the gates (one runner, `gates.py`),
the reviewer and the merge tool; `.review/` holds what the reviewer enforces;
`.github/workflows/` runs both, the compiler's Windows job and the build of the image `ci/Dockerfile`
defines, which the `gates` job runs in. `compiler/` is the compiler's
directory, the one directory the tree gate's allowlist was widened by for it: there it admits
`.cs`, `.csproj`, `.sln`, `.props`, `.targets`, `.json`, `.md` and `.editorconfig` files outside
`bin/`, `obj/` and dot-directories, plus `compiler/NuGet.config` by name, and C# sources and
project files nowhere else (since pull
request 29 retired the design-phase rule, after the owner said build on 2026-10-09). It holds the
library `Kurz.Compiler` (`Text/` reads a source file, `Syntax/` lexes and parses it, `Semantics/`
checks the tree against the rules of chapters 1 to 3, 5, 6, 8 to 10, 11's raw forms and 13's calls on files that
`compiler/README.md` lists, and says `not supported` to the rest, `Emit/` turns a program the checker read
in full, the runtime of its target and the standard library into LLVM's IR, `Corpus/` reads a case header,
`Build/` finds the clang of the pinned LLVM major, carries and checks the runtime and the standard library,
builds a program into an executable, and is the
command `kurz`, whose program is `Kurz/`), the xunit suite `Kurz.Compiler.Tests`, which runs every corpus case
through the front end and every case it reads clean through the checker, builds and runs every case that
expects a run and that the compiler emits, and checks the runtime and the standard library for every target, and
`compiler/README.md`. `runtime/` holds the runtime a built program links, written in Kurz (design record,
section 2): `common/` and a directory per target, where the tree gate admits `.kz` files at any depth and the
compiler refuses or says `not supported` to a place it does not read, and which the
build of the compiler embeds. `std/` holds the standard library's Kurz code, the allocator of section 3
(design record, section 2: the owner, 2026-10-24), where the tree gate admits `.kz` files at any depth and the
compiler refuses or says `not supported` to a place it does not read, and which the build of the compiler embeds too and
a build compiles with every program, as it compiles the runtime. The map of every gate is
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
- **Retired by pull request 29, after the owner said build on 2026-10-09: "Design phase means
  brainstorming, not building."** The owner said build on 2026-10-09, after design round 13; the rule
  held until that change merged, and its gate stays with a widened allowlist. The rule had held since 2026-10-01, when a v0
  compiler was built after asking only for a name and a toolchain, and every pick in it was void
  ([knowledge/design-before-build.md](knowledge/design-before-build.md)). What stays: the tree
  gate admits C# sources and project files only under `compiler/`, with `.json`, `.md` and
  `.editorconfig` files there, outside `bin/`, `obj/` and dot-directories; compiler code written as an allowed file elsewhere, a `.py` under `tools/`, is left to the
  review rule "tools"; and a pick the record does not
  hold is a question for a design round before it is a line of code (the next rule). Gate: `tree`
  (CI), the pre-push hook and the write-time hook for the paths; a pick made in code is review
  rule "compiler". A write through a shell command, and a hook cut off at its timeout, pass the
  write-time hook: HAZARD (#4).
- **The big design choices are asked before anything is derived from them.** The same incident;
  the procedure is the skill `design-round`. `judgment step`
- **A change to the record names the research finding it meets, or says it meets none.** The research
  entries (`knowledge/what-developers-dislike-in-*.md`, `knowledge/what-goose-does-for-speed.md`) hold
  what developers dislike in C#, Rust, Go and Elixir, and the mechanisms of Goose's speed, each
  finding with its verdict for Kurz and the section of the record that answers it; node B4 of
  [knowledge/diagram-design-change-playbook.md](knowledge/diagram-design-change-playbook.md) reads a
  change against them, so that the record does not take up a mistake those languages made (the owner,
  2026-10-30, "Lint + coverage"). Gate: review rule "design record" for the name in the description;
  `knowledge` for each verdict's citations, a section the record has, and at least one for a finding
  that Kurz repeats or leaves open, or that an answer settled; what the cited section says on the
  finding, and whether a change that says it meets none does meet one, are a `judgment step`.
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

### The compiler

- **A value the emitter borrows names what keeps its source unchanged while it is used.** A list
  or a string read from a variable, a field or an item takes no count of its own (`compiler/README.md`), so a
  construct that keeps what it read past a call or a statement either takes a count or rests on a
  refusal of the checker; two that did neither reached `main` with pull request 63, a refusal
  whose walk missed an assignment in an arm reached it with pull request 64, and two in an
  interpolation, a string's hole and one hole that was the string itself, with pull request 78
  ([knowledge/a-borrow-outlives-its-source.md](knowledge/a-borrow-outlives-its-source.md)). Each
  such construct has a line in a fenced program of `ListTests`, and a refusal counts every form that
  changes a place. Gate: `compiler-tests`
  (`ListTests.No_block_is_read_or_written_after_it_is_freed`, and `CheckerTests` for the changes
  the walk counts) for the shapes those tests hold; a borrowing construct no program reaches, and a
  change the walk does not count: review rule "compiler".

- **The case tables are generated, never edited.** `compiler/Kurz.Compiler/Emit/CaseTables.cs` is what
  `tools/unicode_tables.py` writes from the Unicode Character Database of the version Rust's stable
  release pins, each of its three files fetched and checked by its SHA-256 (section 12 of the design
  record: the owner, 2026-10-31, "Rust's pin" and "Generated, as Rust"); a newer version comes in by a
  pull request that runs the tool and lists the mappings it prints, with no question to the owner
  ([knowledge/a-new-unicode-version-is-one-generator-run.md](knowledge/a-new-unicode-version-is-one-generator-run.md)).
  Gate: `unicode-tables`, which ends PARTLY, and so red, where a file cannot be fetched.

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
  head**, passed as `--over-red`. An approval is never standing, but for one case: a pull request
  at the review cap (the rule "A pull request gets at most one review round" below) carries the
  owner's approval of 2026-10-21, which holds for the cap of one since 2026-10-22, to merge over red
  at the head its round at the cap reviewed, when that round completed, for the check `review` and for `gates` when its table shows `pr-findings` as its
  one FAIL; a round at the cap that did not complete, any other red check or gate, and a head that took
  `main` after that round need the owner's approval for that head as for any other. Whether those conditions hold is read by the caller, a `judgment step`: the merge tool
  takes `--over-red` as the caller's statement (HAZARD #3). Retired on
  2026-10-21 by that cap: "An approval is never standing" with no case. The tool switches the whole
  ruleset off for the one merge and restores it; while it is off nothing on the server holds any
  pull request or any push to `main`, so the tool reads the checks and the threads again once it
  is off and merges nothing when they changed (exit 1, the ruleset restored; the approval is
  given again for the new state, or not at all). What changes between that reading and the
  moment the tool restores the ruleset is caught by nobody, and nothing stops another merge or
  push in that window: HAZARD (#27). Exit 3 means it was not read back as active, so it may still
  be off: say so at once, read it back and restore it as the skill `change-walk` says; `merge-checks` is red on every run until it is back.
  HAZARD (#3): the session holds the owner's token, so the permission classifier and this rule
  are the only guards on the approval.
- **One push per review round.** Read every thread, fix everything, mark the pull request Draft,
  push once, resolve the threads the push answered, then mark it Ready (the skill `change-walk`,
  step 7), so the one review and the gates run on a head with its threads resolved. Never push
  to cancel a running review; a push to a Ready pull request does cancel it. No fix push follows
  the round at the cap: the cap below. `judgment step`
- **A pull request gets at most one review round** (the owner, 2026-10-22). Retired by it: the cap of
  three rounds the owner set on 2026-10-21, after pull request 33 took thirteen rounds without
  converging ([knowledge/rounds-do-not-converge.md](knowledge/rounds-do-not-converge.md)). A round is a
  review run that labelled the pull request: a run with files to review, in CI or off the
  pipeline, puts the label `review-round-<n>` on before its first pass, and it is a round whatever
  follows (a pass, a limit, a cut-off, a failure); a dry run, `--plan` and a run that ends before
  the label (closed, the secret missing, nothing to review, the cap) put none on. The count is the
  highest `n`, the rounds before the cap count, and the rounds a pull request had before the labels
  are counted by one label put on by hand. At one every further run ends before its first pass
  as `REVIEW DID NOT COMPLETE (cap)`, in CI with the status `review` at `error`, naming the cap; on
  the head the round at the cap reviewed that error takes the place of the round's status, so at the
  cap no run is started (no Draft and Ready, no dispatch, no push). What the round at the cap left open
  goes on the collection ticket, the open issue labelled `review-leftovers`: one section per pull
  request, with the head the findings were made on, the links of its threads and the link of its
  lows on the `review-lows` issue; every open thread gets a reply that links the ticket and is
  resolved, and the pull request merges as it is, on the approval the cap gives (above). The skill
  `change-walk` has the walk (section 3, item 8), and `py -3 tools/pr_gates.py cap --pr N --analysis <file>`
  takes it: the section as a comment on the ticket, the replies and the resolutions
  ([knowledge/the-walk-at-the-cap-is-a-command.md](knowledge/the-walk-at-the-cap-is-a-command.md)).
  Gate: `self-tests` (the count, the label before
  the first pass, the end at the cap and its status; the move to the collection ticket in
  `pr-findings`, in the resolve command, in the cap command and in the merge tool, whose wiring of it is
  `merge_pr.held_by`; a section posted with no reading, which the cap command refuses); what the reading says and
  starting no run at the cap are a `judgment step`; a
  label taken off or put on by hand, a second open issue that carries the label, two runs of one
  pull request that start at the same moment, and a dry run, which puts on no label: HAZARD (#41).
  The gates job lists the ticket with `issues: read`, which `ci-config` pins.
- **A finding above low is a thread, and a thread is answered by an edit.** On the design
  record, the reference, the corpus, the knowledge store and rule files the file must change
  before the thread is resolved; on tool, workflow and hook code a written reply also counts; at
  the review cap (the rule above) a thread on any file is also answered by its move to the
  collection ticket, a reply from someone who may write here that links the open issue labelled
  `review-leftovers`, as `#N` or by its address; below the cap such a reply is a written reply like
  any other, which answers a finding on tool, workflow and hook code and nothing on the other files.
  Retired on 2026-10-21 by that cap: "the file must change" with no case, on the design record,
  the reference, the corpus, the knowledge store and rule files. After
  the fix push, `py -3 tools/pr_gates.py resolve --pr N --head <sha> --go`, with the pushed commit,
  resolves the reviewer's threads whose files all changed since the finding's commit and leaves
  the rest open with the reason (a thread in which a person wrote among them); right after the
  push the forge still names the old head for a moment, and the tool refuses until it shows the
  pushed one (run it again,
  [knowledge/the-pull-request-shows-the-old-head-after-a-push.md](knowledge/the-pull-request-shows-the-old-head-after-a-push.md));
  a thread answered by a reply is resolved by hand
  ([knowledge/resolve-what-the-edit-answered.md](knowledge/resolve-what-the-edit-answered.md)),
  and at the cap the same command, with the head the round at the cap reviewed, resolves a thread whose
  replies all move it to the collection ticket. Gate: `pr-findings`.
- **The acceptance judge's table is a comment on the pull request, and the description names no
  verdict.** The review reads a verdict in the description as a claim of approval: on 2026-10-10 one
  head drew eight high findings for its "overall: pass" (the owner's choice of 2026-10-11, against
  changing the review rule). The judge reads the tree that becomes the head, nothing is committed
  after its last reading, and the acceptance text names that commit
  ([knowledge/the-judged-tree-is-the-committed-tree.md](knowledge/the-judged-tree-is-the-committed-tree.md)).
  Gate: review rule "every pull request" for the verdict; the order of judging and committing is a
  `judgment step`.
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
| merge checks on `main` (ruleset; `merge-checks` asserts it on every run and is red while it is off) | per-item approval for a merge over red (#3), and the window in which the ruleset is off (#27) |
| every recorded tool decision still turns red (`self-tests`) | the local hooks being switched on, a write through a shell command, a hook cut off at its timeout, a push with `--no-verify` (#4) |
| workflow facts, the image the `gates` job runs in, named by its digest, with the facts of its Dockerfile, and the LLVM major of that image and of the Windows job's install, held to the compiler's (`ci-config`) | wrap-up (#6) |
| store shape, expiring numbers, generated numbers that match their digest, and the sections of the design record the research findings cite (`knowledge`) | the bypass list and the auto-merge switch, in CI (#7) |
| the compiler builds and its unit tests pass, with the SDK `compiler/global.json` pins and a restore inside the lock files, every corpus case the compiler emits builds and runs as its header says, and `kurz --version` names the clang of the pinned LLVM major it found, printed by the gate (`compiler-tests`, in `gates`; on Windows in `compiler-windows`) | the Windows job is not a required check (#30) |
| the case tables of X3 are what the pinned version of the Unicode Character Database gives (`unicode-tables`) | |
| the reference and the corpus agree in shape (`reference`) | a reference rule saying no more than the record section it cites (review rule "reference": `judgment step`) |
| title, description and commit messages of a pull request; breadth; answered findings (`pr-*`) | record samples, and the expectations of the corpus cases the compiler does not emit, being right: nothing runs them (#1) |
| the reviewer's pinned model, the environment of its call, what a failed run keeps, and the merge tool's read of a status from the endpoint that names its creator (`self-tests`) | that the `review` status came from a completed review (#11), and that the ruleset still accepts the status: seen once on 2026-10-07, re-checked by nothing, failing closed (#24) |
| low findings collected on one issue instead of threads (`self-tests`) | that GitHub starts the review workflow: the event policy for `pull_request_target` (#10) |
| at most one review round per pull request, and at the cap a finding moved to the collection ticket (`self-tests`) | the round labels, which anyone who may triage can change, a second issue with the collection label, two runs that start at the same moment, and a dry run, which no label counts (#41) |
| | the author and committer address a push publishes (#12) |
| | a title or description edited after the review (#13) |
| | a merge by the button, which skips what only the merge tool does (#14) |
| | the hosted images' python3, node, gh and git, the Windows image's zstd and tar, and the gates image's python3, git and LLVM point release, pinned to no version (#15) |
| | the gates job judging a pull request with the pull request's own gates (#16) |
| | a review that stopped at the pass cap posting `success` (#17) |
| | the evidence digest, which a recomputed hash passes (#18) |
| | GitHub push protection staying on (#25) |
