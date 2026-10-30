---
name: diagram-gate-map
description: Living diagram - every gate, hook, server-side rule and process law of this repository, grouped by where it runs, each with what turns it red or the HAZARD issue that stands in for a gate
metadata:
  type: reference
---

Every node says what fails it, names the HAZARD issue that stands in for a missing gate, or is
labelled a judgment step. A rule of `CLAUDE.md` or of a skill that names a gate names a node of
this map; where another diagram writes "review rule", it means node R5, and a review rule is a
model reading a diff, not a mechanism. The verdicts of the runner (PASS, PARTLY, FAIL, BROKEN,
NOT RUN, N/A) are defined once, in the docstring of `tools/gates.py`.

```mermaid
flowchart TD
    subgraph W["Write time: one agent session"]
        W1["write-time hook, tree_gate.py --hook:
        denies a Write or Edit to a path the allowlist does not hold (under compiler/, since pull request 29 retired the
        design-phase rule: .cs, .csproj, .sln, .props, .targets, .json, .md and .editorconfig files outside bin/, obj/
        and dot-directories; under runtime/, .kz files in its directories; under std/, .kz files at any depth), and every call
        the gate could not judge; inside or outside the repository is decided by the file system's
        identity of directories, so a path spelled in another case is inside where case is ignored.
        It denies by exit 2 and prints no JSON. The command falls back to python3 without the py launcher.
        Not held: a write through a shell command, a hook cut off at its timeout. HAZARD issue 4"]
    end
    subgraph P["Push time: this clone and the forge"]
        P1["pre-push hook, tree_gate.py --pre-push:
        refuses the push when a commit about to be published holds, in a file or in its name, a credential,
        a machine-bound string or a conflict marker, or holds a disallowed path, a file that is not UTF-8 text
        or a link or a submodule, when its message holds one of the first three or a workflow-skip literal
        (a GitHub no-reply address passes in a trailer line only, in a tag's message too), when the message of an
        annotated tag it pushes, or of a tag that one points at, holds one of the first three, when a tag points at a
        blob or a tree, when a ref is aimed straight at a blob or a tree, or when the name of a ref it publishes,
        or the name in a tag's own header, holds one of them.
        Off until core.hooksPath is set: HAZARD issue 4"]
        P2["future plans in a commit: no gate before the push. HAZARD issue 2"]
        P3["author and committer name and e-mail, published with every commit, and the tagger of an annotated tag:
        read by no gate. HAZARD issue 12"]
        P4["GitHub push protection: the forge refuses a push that holds a secret of a pattern it knows.
        Enabled, read 2026-10-02 from the repository's security_and_analysis.
        No gate asserts that it stays on: HAZARD issue 25"]
    end
    subgraph G["CI: job gates, on every pull request event and on a push to main"]
        G0["the run is red on a FAIL, a BROKEN, a NOT RUN,
        on a PARTLY of any gate but merge-checks, and when no gate ran.
        The pull-request gates are N/A only on a push to main; a push to any other branch,
        and a --pr without its number, do not pass as no pull request.
        The job runs the pull request's own copy of every gate: HAZARD issue 16"]
        G1["self-tests: a tool self-test fails, ran no case or contradicts its exit code;
        a tool has no self-test or no recorded red proof; a recorded mutation
        no longer turns its one named case red; fewer tools than the floor; a ledger written back in
        another form than its one (one entry per line, two spaces in, text as it is; --format writes it). The replay runs each
        self-test, green and mutated, in a copy of the tree, with its temporary files beside that copy,
        outside the tree a suite reads; both go when the check is done; a copy that could not be removed is named.
        Not seen: a decision that has no case. Node R5, rule tools"]
        G2["tree: disallowed path, credential, machine-bound string, conflict marker,
        non-UTF-8 file, a link or a submodule, fewer files than the floor.
        A shape counts after the letter of a newline or tab escape too, as a value begins a line in JSON;
        before a push every path of a commit but a deletion is read, and a file turned into a link is refused;
        a file's name is scanned like its content"]
        G3["knowledge: broken INDEX link, entry without INDEX line, hook or frontmatter;
        a store file named in an entry, CLAUDE.md, the review rules or a skill that does not exist;
        a link in a store file (inline or reference-style, outside code, percent-decoded) that, resolved from that
        file in its exact spelling, reaches no entry, file or directory, or leaves the repository;
        nested entry;
        LIVING entry without diagram or update triggers, fewer living entries than their floor;
        expired or future-dated numbers, a TTL over 90 days; a generated block that is empty, or a page
        whose digest is stale: a number, the generated date or the TTL edited by hand
        (a recomputed digest passes: HAZARD issue 18); fewer entries than the floor;
        in the research entries, a finding with no verdict, two, or one in no word of its entry,
        a section of the design record a verdict cites that the record does not have, a repeated,
        open or answered finding that cites none (not what the section says: review rule design record),
        an entry with no finding, fewer research entries than their floor"]
        G9["reference: a rule without id, status or the record section it cites;
        a decided, assumed or proposed rule with neither a case nor a reason;
        a case on an open rule; a sample that differs from its corpus file;
        a code block that is neither a case sample nor marked text, or that is never closed;
        a fence indented or made of tildes outside a code block, which the lint would not read (inside one it is content);
        a corpus header that cannot be read or names an unknown rule;
        an error id its table (compile or run-time) does not list, or lists for other rules;
        a table row whose id no case expects, unless it is a run-time row whose no-case note gives the reason none can,
        and such a note on a compile-error row or on a row that a case does expect;
        a row that reads as an error id outside a table headed id, rules, meaning;
        an error-table row that does not read as one; a design record, a chapter or a corpus file that cannot be
        read, missing or not UTF-8, named with what went unchecked;
        fewer rules or cases than the floors"]
        G10["compiler-tests: the compiler under compiler/ does not build, or a unit test fails;
        no test summary line at all, fewer passed tests than the floor, a skipped test, a non-zero exit with no failed test;
        compiler/NuGet.config missing, without the clear of the inherited sources or without a source mapping;
        a restore outside the lock files (locked mode), an SDK other than the one compiler/global.json pins;
        dotnet missing or out of time; kurz --version, which it prints, exited with another code than 0 (the command
        refused the clang it found or found none, did not start or ran out of time), or exited with 0 and wrote no line
        that names a clang release in the command's one-line form. Read: the output of dotnet test, one summary per
        test project, summed;
        the floor sits above the suite without either corpus theory, so a run that lost one cannot pass.
        Inside the suite: the walk of every corpus case the checker reads clean, red on a node a code generator reads
        that the checker's record (SemanticModel) holds no entry for, or on fewer than 160 cases or 2,900 entries
        walked; and the runtime under runtime/ with the standard library under std/, embedded in the compiler, red
        on a file the compiler carries other than the tree's, on a file out of its place, on a diagnostic of its check
        for any target, or on a function common/ or std/ calls that a target's directory gives with other parameters
        or another result;
        and the corpus runner, which builds every case that expects a run and that the compiler emits with the
        pinned clang and runs it, and again under an allocator that makes every freed block inaccessible, red on
        output, an exit code or an error line other than the header's, on a case
        the emitter refuses that its list does not name with the emitter's words, on a stale entry, or on fewer
        than 241 cases run on Linux or 227 on Windows, and on Linux on a crash whose trace is empty, holds a line that
        is not a frame, does not end at main or at the cut after a hundred frames, or, in a test build, does not start
        at the error line's place; TraceTests, red on a trace
        other than the one each Linux program pins: its frames, names and lines, the cut and its count, the
        switch KURZ_TRACE=0, the release build with and without --lines, an executable stripped of its symbols
        or of its line tables, and a stack overflow's error line, cut trace and exit code in a test build and a
        release build with and without --lines, with KURZ_TRACE=0, inside the runtime, and a fault outside the
        guard page that the system ends, a trace cut where the reader could not map its pages with a count other than one line a frame, and on a count
        past the cut that reads the file at every frame, whose
        release run with --lines outlasts the 30 seconds of a run; the build command's lines and refusals; and the refused write and the refused allocation,
        each with its error line; DataTests, red on a tag wider or narrower than the largest type number among the
        cases needs, two layouts for one union, a value that does not come back out of a union, a block not copied
        before a write, a path assignment that does not evaluate its value first, a static of a type with a base laid
        out as a plain value, an arm of a derived type that misses a value of its base that holds it, a literal arm on
        a union or an else used as a statement that does not run, the text of a thrown value, or a not supported at
        another line or in other words; and EmitterTests on the largest string, red on a join or a string.FromRaw of
        more bytes than the largest int that ends otherwise than in allocation-failed with that count, and on a
        conversion from an unsigned type to a signed one, red on a value above the signed maximum that raises no
        overflow in a test build, and on a function whose first instruction starts a block, which clang refuses
        without the entry block's branch, and on a function without uwtable, or without the inline stack probe on Linux
        or with it on Windows; and ListTests, red on a list's or a string's block read or written after it was
        freed, under an allocator that makes every freed block inaccessible (a borrow that outlived its source, the
        name an arm binds among the programs), on lists or strings made and dropped that outgrow a system giving
        sixteen pieces, and on the number allocation-failed reports; and FloatingTests, red on the text of a double or a float that
        differs from what .NET writes, or where .NET's does not read back, from the fewest digits that do, on a
        conversion into an integer type at either end of its range that keeps a value it loses or loses one it
        keeps, in a test and a release build, and on a comparison, a field's equality, a widening, a remainder,
        a list or a hole of them that differs from C#'s; and DecimalTests, red on a sum, a difference, a product, a
        quotient, a remainder, a comparison or a conversion into double, float or long of a decimal that differs
        from System.Decimal's (a zero computed without its sign, and the exact remainder where System.Decimal
        raises), on a double or a float converted into decimal other than its exact binary value rounded, on a
        result beyond the range, a conversion into decimal that loses the value or a division by zero that does
        not raise in a test and a release build, and on a conversion into an integer type that keeps a value it
        loses or loses one it keeps; and TimeTests, red on the text of a duration other than its units from days
        down, on the text of a timestamp other than its instant in UTC as RFC 3339 and chrono write it, on time
        arithmetic or a conversion into the narrow pair beyond the range that does not raise in a test and a
        release build, on one at the edges that does, on a wide value not aligned to 16, and on a widening, a
        comparison, a field, a union, a list or a hole of them that shows another value; and FileTests, red on a
        file that stays open after the last value that holds it let go of it, under a limit of 32 descriptors, on a
        close through one copy that leaves the other open, on a read into a list another value holds that changes
        that value, on a count and a max past the largest int that read before allocation-failed, on a whole file of
        the largest int of bytes (lowered to 20) that is not read whole or one byte more that is, on a read or a
        write in the wrong direction that is not error 9, on a file or a directory whose mode is not 0666 or 0777
        less the umask, on a descriptor opened without close-on-exec, on a directory
        created again that fails, on the text of an error without a path, on an address read otherwise than Rust
        reads it, a scope id and a port with a sign among them, on a bind of IPv6's loopback that does not give
        what the machine's kernel gives, the bound address where it holds `::1` and its error elsewhere, on a listener on IPv6's wildcard that a connection of IPv4 does not
        reach where the kernel's dual stack is on, on a write to a connection whose peer closed it that is not
        BrokenPipe, and on a build for Windows that does not say not supported at a call on a file.
        Not seen: the expectation of a case the compiler does not emit, and of a case the runner lists as not run
        (HAZARD issue 1): the suite checks the front end's and the checker's errors against those headers and runs
        no program of them"]
        G4["ci-config: a pinned fact of a workflow changed. All four workflows: an action not pinned by commit SHA (flow-style
        steps included), another runner label, any spelling of pip or pipx without hashes, no timeout,
        continue-on-error, a step with an if, an expression inside a run line, a secret read anywhere but the
        env of the job's last step (in any case, behind any literal), a workflow- or job-level env, a job-level
        permissions grant, a job beside services, a job in a container but the gates job in its image,
        a step that writes GITHUB_ENV or GITHUB_PATH, a step that checks out or fetches by hand,
        the deciding step with any env key but the pinned ones, a key twice in one mapping.
        gates.yml: an if on the job (a skipped job reports success to the required check),
        a path filter, any filter on pull_request, a push trigger for any branch but main, a token other than
        contents, issues, packages and pull-requests read (issues for the collection ticket, G8; packages for the image),
        a second job, a job that is not in ghcr.io/symohtl/kurz-ci named by its digest and pulled with the job's
        token, an SDK step of its own (the image's SDK is the one compiler/global.json pins).
        review.yml: an if, a checkout ref, a concurrency group or permissions other than the pinned ones,
        no start-time step first, the budget anywhere but on the review step,
        a CLI that is not at an exact version.
        compiler-windows.yml: another label than the versioned Windows one, an if, a wider token, a second job,
        a checkout that keeps the credential, no SDK step, a second one or one with a version typed in the workflow,
        an env on the compiler step, an LLVM install that is not the pinned script (the archive checked by its
        SHA-256 before it is unpacked), an LLVM_VERSION of another major than the compiler's Clang.PinnedMajor,
        an LLVM_SHA256 that is no SHA-256, a third variable in the install's env, no cache of the archive
        before the install, or one with another path than llvm-archive under runner.temp, or keyed by another
        release.
        image.yml: triggers other than a push that changes ci/Dockerfile or the workflow, on any branch, and a dispatch;
        a token other than contents read and packages write; an if, a second job, a checkout that keeps the credential;
        a publish step other than the pinned login through standard input, build, push and digest line.
        ci/Dockerfile: a base not named by its digest, a second FROM, an SDK other than compiler/global.json's, a
        download used unchecked (the LLVM key by its fingerprint, the SDK by its SHA-512, gh by its SHA-256), an LLVM
        major that is no number or not the compiler's Clang.PinnedMajor, or a compiler that names no such major.
        A workflow file without facts here.
        The hosted images' python3, node, gh and git, the Windows image's zstd and tar, which unpack the LLVM archive,
        and the gates image's python3, git and LLVM point release (its major is the compiler's), are pinned to no
        version: HAZARD issue 15"]
        G5["merge-checks: live rules on main differ from tools/ruleset.json
        or carry a parameter that file does not name, ruleset not active,
        or a read of the rules that the forge refuses. Two settings turn it red only in a local run
        with the owner's login (L1): the bypass list and the auto-merge switch. In CI the job token
        cannot read them, they print NOT CHECKED, the gate ends PARTLY and the run stays green
        for this gate only: a bypass actor added on the server is seen by no CI run. HAZARD issue 7"]
        G6["pr-title: a workflow-skip literal, a credential, a machine-bound string or a conflict marker
        in the title, the description or a commit message of the branch:
        each can become the squash commit on main. A branch with more commits than the forge listed is refused.
        A skip literal in the head commit stops this very job: that head has no gates run,
        and the ruleset holds it as a missing check"]
        G7["pr-breadth: over 15 files, or quality infrastructure touched
        (tools, workflows, review rules, skills, hooks, CLAUDE.md, .gitattributes; a file moved out of it counts),
        without a Blast radius section"]
        G8["pr-findings: a review thread unresolved, or resolved without the edit that answers it;
        on tool, workflow and hook code a written reply also answers. The resolve command of tools/pr_gates.py plans
        the reviewer's threads whose files all changed since the finding's commit, resolves them with --go, and leaves the rest open
        with the reason, a person's reply in the thread and a head the forge still shows as the
        finding's commit among them (self-tests).
        At the review cap (R7) a finding on any file is also answered by a reply from someone who may write here
        that links an open issue labelled review-leftovers, the collection ticket; the gate reads that list only
        at the cap, and the resolve command resolves a thread whose replies all move it (self-tests).
        The cap command of tools/pr_gates.py takes that walk: the section as a comment on the ticket, the replies,
        the resolutions, and a refusal below the cap, of an open finding of another head and of a thread a person
        wrote in (self-tests).
        The gates job reads that list with issues: read, the one grant of its token beyond the tree and the
        pull request (ci-config pins the grant); a refused read fails the gate closed.
        The gate's verdict is the one of the moment the job ran: resolving a thread starts no run"]
    end
    subgraph C["CI: workflow compiler-windows, on the same events as the job gates"]
        C1["job compiler-windows: the gate compiler-tests (G10) on the versioned Windows runner label.
        Not a required check of the ruleset: a red run holds no merge. HAZARD issue 30"]
    end
    subgraph R["CI: workflow review, pull_request_target and dispatch"]
        R1["job review-run: red when the review did not complete. The last line names the kind:
        credential, usage-limit, budget, oversized, base, closed, cap, rules, empty, bad-diff, head-moved,
        wrong-model, bad-output, cli-missing, api, failed, the last also for a post the forge refused,
        a finding above low that landed as a plain note, and a rate limit with no wait left.
        Every finding is printed to the log, in UTF-8, before anything is posted; what was found is posted
        and what converged is stored first, so the next run continues. A finding reported on an earlier
        head is told to the model and hides nothing on this head's lines"]
        R2["status review: success only from a review that completed on this head;
        at the pass cap it says NOT converged, and is success all the same: HAZARD issue 17.
        Draft, outside pull request, the Ready event of a head labelled reviewed- plus its own sha, no run:
        stays pending; a skipped job reports a check named review-run, never this status.
        It satisfied the ruleset's pinned app on 2026-10-07 (issue 5 closed; a fact seen once, re-checked by nothing: HAZARD issue 24,
        failing closed: a status the ruleset stopped accepting leaves the merge pending);
        the merge tool reads it from the list of statuses, the endpoint that names the creator, which the combined
        status drops (self-tests: the read of the captured status).
        Any workflow run of this repository can post the same status: HAZARD issue 11"]
        R3["findings: high and medium become threads on the pull request, per file,
        and block through thread resolution, not through the job.
        Lows are collected on the open issue labelled review-lows, in parts, each part a note on the pull request
        first and then a comment on the issue; a part whose post failed is printed in the log, its note withdrawn
        when the comment was refused, or failed otherwise and the issue, read back, does not show it (a note that
        got no answer is read back on the pull request the same way, and one that landed goes on), its file
        reviewed again by the next run, and the run ends red;
        they block nothing: judgment step"]
        R4["the workflow starts at all: GitHub's default rule may block pull_request_target here (whether it
        enforces or only evaluates was not known on 2026-10-02); the owner's event policy allows
        pull_request_target and workflow_dispatch for .github/workflows/review.yml, active since 2026-10-03
        (read 2026-10-09 from the repository's Actions policies, gh api repos/OWNER/REPO/actions/policies).
        Nothing asserts the policy: HAZARD issue 10"]
        R5["review rules, .review/review-rules.yaml of the default branch: one section per surface, the
        section compiler for compiler/, runtime/ and std/, which reads a borrow of the emitter that names no count and
        no refusal of the checker, and a change inside an expression that the checker's walk of M7 does not count,
        the section design record, which reads a change to the record whose description neither names the research
        finding it meets nor says it meets none (node B4 of the design playbook; the citations are G3's).
        Red: a finding above low that is left unresolved, or resolved without its edit (G8, pr-findings).
        The reviewer is a model that is given one batch of the diff: it can miss a defect, it cannot
        see a file outside its batch, and a breach it rates low opens no thread and holds no merge.
        A rule that names a review rule as its gate has this much; the rest of it is a judgment step"]
        R6["what runs: the workflow file, the reviewer and the rules of the default branch; ci-config pins
        that nothing of the pull request is checked out, and that the job skips the Ready event of a head
        labelled reviewed- plus its own sha, which a completed off-pipeline review of that head adds (every
        other head is reviewed by the run its event starts; a run that started is a bill). A dispatch on
        another ref runs that ref's copy, and the job's if refuses it only in the default branch's copy:
        HAZARD issue 11"]
        R7["review rounds, the owner's cap of one (2026-10-22; three from 2026-10-21): a round is a run that labelled the pull request;
        a run with files to review, in CI or off the pipeline, puts review-round-n on before its first pass,
        a round whatever follows, and the count is the highest n (kit.review_rounds); at one every run ends
        before any pass as cap, in CI with the status review at error naming the cap, on the head the round at the cap
        reviewed too. The end is inside the job: a job its if skips reports success.
        Red: self-tests (the count, the label before the first pass, the end and its status).
        A label taken off or put on by hand, a second issue with the collection label, two runs that start
        at the same moment, and a dry run, which puts on no label: HAZARD issue 41"]
    end
    subgraph M["Merge: server side and the merge tool"]
        M1["ruleset on main: pull request required, squash only, every thread resolved,
        gates and review green on the head, branch up to date,
        no force push, no deletion, nobody bypasses"]
        M2["merge_pr.py: refuses a head other than the one named, a Draft,
        a branch behind its base, armed auto-merge, an unanswered finding (at the review cap a finding
        moved to the collection ticket is answered, R7 and G8),
        a required check that is not success from the app the ruleset pins,
        live merge rules that differ from tools/ruleset.json or that it could not read in full,
        a title or description the title gate refuses, an option it does not know.
        A merge by the button or gh pr merge still meets M1, but not the head pin, the auto-merge check,
        the comparison with the live rules, or the re-check of the title gate and of the answered findings
        at the moment of the merge: HAZARD issue 14"]
        M3["merge_pr.py --over-red: ruleset off for one merge, restored and read back;
        while it is off nothing on the server holds any pull request or a push to main, so the checks
        and the threads are read again once it is off, and a change that reading finds merges nothing (exit 1,
        the gate restored); what changes between that reading and the restore of the ruleset, the merge call
        included, is caught by nobody, so the window is narrowed, not closed, and nothing stops another merge
        or push in it: HAZARD issue 27.
        Exit 3 when the gate was not read back as on, exit 6 when the waiver record is missing.
        The owner approves each item: judgment step, HAZARD issue 3; at the review cap (R7) the owner's
        approval of 2026-10-21, which holds for the cap of one since 2026-10-22, is given for the head the round at the cap reviewed, when that round completed,
        for review and for gates when pr-findings is its one FAIL, conditions the caller reads (judgment
        step). The tool refuses a branch behind its base (M2), so when main moves after the round at the cap
        the branch is brought up to date as a new head, which only the owner's approval of that head merges"]
        M4["title or description edited after the review: the status stays green on text
        the review never read. HAZARD issue 13"]
    end
    subgraph L["Local run: tools/gates.py"]
        L1["the same gates as CI; pull-request gates print NOT RUN without --pr, which is loud and not red;
        with the owner's login merge-checks reads everything and ends PASS"]
        L2["push-hook: core.hooksPath is not .githooks. Red in every clone until it is set"]
    end
    subgraph X["Process: no mechanism"]
        X1["a statement is recorded as decided only after the owner chose it; a proposal nobody
        objected to is recorded as assumed; a question nobody answered goes under Open: judgment step.
        The shape of the record is node R5, rule design record"]
        X2["samples in the design record, and expectations of the corpus cases the compiler does not emit, are
        right: nothing runs them, HAZARD issue 1"]
        X3["wrap-up: lessons promoted out of private memory: HAZARD issue 6"]
        X4["one push per review round, and none after the round at the cap (R7): judgment step"]
    end
    W --> P --> G --> M
    P --> R --> M
    P --> C
    L -.-> P
```

## Update triggers

- `tools/gates.py` (the `GATES` list, a verdict, `PARTLY_OK`) changes: the subgraphs G and L.
- `.github/workflows/gates.yml` (its events) changes: the header of subgraph G; its token's grant: G4 and G8.
- `tools/red_proof.py` or the shape of `tools/red_proofs.json` changes: G1.
- `tools/tree_gate.py`, `.claude/settings.json` (the hook command), `.githooks/pre-push` change:
  W1, P1, G2.
- `tools/kit.py` (ROUND_LABEL, ROUND_CAP, LEFTOVERS_LABEL, `review_rounds`) changes: R7, G8, M2 and M3.
- `tools/kit.py` (SECRETS, MACHINE, CONFLICT, NOREPLY_TRAILER, the skip literals) changes: P1, G2, G6 and M2;
  W1 reads paths only, so a pattern reaches it nowhere. `kit.scratch` or `kit.writable_then_retry` changes: G1,
  whose replay runs every suite in it; `kit.message_text` changes: P1 and G6, which read commit and tag messages
  through it.
- `tools/lint_knowledge.py` changes a check: G3. `tools/lint_ci.py` changes a fact: G4 and R6.
- `tools/compiler_gate.py` changes (the command, the floor, a verdict, the config check): G10 and C1.
  `compiler/global.json` changes the SDK: G10, whose command reads it from inside `compiler/`, and C1, whose SDK
  step installs it. A lock file, `compiler/Directory.Packages.props`, `compiler/Directory.Build.props` or
  `compiler/NuGet.config` changes what the gate restores or builds: G10 and C1.
- `.github/workflows/compiler-windows.yml` (its events, runner or steps) changes: the subgraph C and G4.
- `tools/lint_reference.py` changes a check: G9 and X2. The emitter emits more of the corpus, or the runner's
  lists change: G10 and X2.
- `tools/pr_gates.py` changes a pull-request gate: G6, G7, G8; its `resolve` command plans, and with `--go` resolves, the reviewer's threads that G8 reads as answered by an edit.
- `.github/workflows/review.yml`, `tools/review/review.py` or `.review/review-rules.yaml`
  changes: the subgraph R.
- `tools/ruleset.json` or `tools/merge_pr.py` changes: the subgraph M and G5.
- A HAZARD issue closes or opens: the node that names it, and the table at the end of `CLAUDE.md`.
- A rule in `CLAUDE.md` gains or loses its gate: the node of that gate, or the subgraph X.
