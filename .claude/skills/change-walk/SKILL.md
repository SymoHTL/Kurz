---
name: change-walk
description: >-
  Take one change to main in this repository: branch, local gates, push, Draft pull request,
  gates job, review, threads, merge with the merge tool. Use for every change, including a design
  round's record, and when a gate or the review is red and you need to know what to do next.
---

# The walk one change takes

The picture is `knowledge/diagram-change-walk.md`; what turns each gate red is on
`knowledge/diagram-gate-map.md`. Commands are for Git Bash on Windows; on other systems `py -3`
is `python3`. Every step ends in its gate, a HAZARD with its issue, or `judgment step`.

## 0. Before anything

1. `git fetch origin` and branch off the fresh remote ref: `git switch -c <branch> origin/main`.
   Never commit on `main`. Gate: the ruleset refuses the push.
2. Once per clone: `git config core.hooksPath .githooks`. Gate: `push-hook`, which makes every
   local `py -3 tools/gates.py` red until that line was run; HAZARD (#4) where nobody runs it.
3. The repository is public. Nothing bound to one machine or one person, and no future plans, in
   any file, commit message, branch name, title or description. Gate: `tree` and the pre-push
   hook for the shapes; plans, and everything in a branch name: HAZARD (#2).

## 1. Local proof

1. Edit. A new or changed decision in a tool gets a self-test case and an entry in
   `tools/red_proofs.json`: break the rule in one place, watch
   `py -3 tools/<tool>.py --self-test` print `FAIL <case>`, record the anchor, the replacement and
   the case name, and revert. Gate: `self-tests` replays what is recorded; a decision that got no
   case is seen only by the review rule "tools": `judgment step`.
2. `py -3 tools/gates.py`. The run is red on a FAIL, on a BROKEN (a gate that could not start)
   and on a PARTLY of any gate but `merge-checks`. The pull-request gates say NOT RUN until a
   pull request exists; that is loud and not red. PARTLY on `merge-checks` means this login
   cannot read two settings, as in CI (HAZARD #7); with the owner's login it is PASS.
   Gate: the exit code of the run.
3. A change to a gate, tool, hook, workflow or review rule also updates
   `knowledge/diagram-gate-map.md` or `guides/quality-bar.md`, whichever describes what changed,
   or the description says why neither applies. Gate: review rule "every pull request".

## 2. Push and open a Draft

1. Commit. No workflow-skip literal in any message. Gate: `pr-title`, and the pre-push hook
   before the push. A literal in the head commit stops the `gates` run itself: that head has no
   run, and its required check stays pending.
2. `git push -u origin <branch>`. The pre-push hook runs the tree gate over every commit, its
   message included; a refusal names the commit and the string's kind, never the string.
   Gate: the pre-push hook; HAZARD (#4) where it is off. The author and committer address of
   the commits is published too and read by no gate: HAZARD (#12).
3. `gh pr create --draft --title "<title>" --body-file <file>`. The description says what and why.
   If the change touches `tools/`, `.github/`, `.review/`, `.claude/` or `.githooks/`, or more
   than 15 files, it has a `## Blast radius` section naming what can break and who reads it.
   Gates: `pr-title`, `pr-breadth`.
4. The `gates` job runs on every push and on every edit of the title or description. Red: open
   the log, read the `=== gates` table at its end, fix the first row that is FAIL, BROKEN or
   NOT RUN, or PARTLY on a gate other than `merge-checks`. No run at all on a head: a merge
   conflict, or a skip literal in the head commit. Gate: the required check `gates`.

## 3. Review

A review run spends the owner's Claude seat: about 1.4 to 1.8 USD and 6 to 18 minutes per pass (`knowledge/what-a-review-pass-costs.md`), at
least two passes per batch of the diff (`knowledge/what-a-review-pass-costs.md`). A run in CI, the one
Ready or a push starts or a dispatch, starts without a question to the owner (the owner's decision,
2026-10-07); its bill is named in the report. A run off the pipeline starts after the owner said go
to the bill named. `judgment step`
Count the batches on the head that will be reviewed, with
`py -3 tools/review/review.py --pr <N> --plan`, which pays nothing; never take them from an
earlier run. Gate: `self-tests` (a plan calls no model and posts nothing).

1. Iterate in Draft. A Draft, and a pull request from outside, is reviewed only on demand:
   `gh workflow run review.yml -f pr=<N>`, without `--ref`, so that it runs on the default
   branch. Gate: the job's `if` refuses another ref, pinned by `ci-config`; a copy of the
   workflow on another branch can drop it: HAZARD (#11).
2. When the description is final, mark it Ready once: `gh pr ready <N>`. Where the forge starts
   the workflow, that starts the review, with the workflow, the reviewer and the rules of the
   default branch. When the review ran off the pipeline instead (the last bullet of step 3, while
   the pull request was still a Draft), wait until `gh pr view <N> --json labels` shows
   `reviewed-<sha>` for the head you mark Ready: the job then skips that Ready event, while any
   other head, pushed before or after Ready, is reviewed by the run its event starts. Never mark
   Ready meaning to cancel the run: a run that started is a bill, cancelled or not
   (`knowledge/a-cancel-does-not-beat-the-runner.md`). Gate: `ci-config` pins the clause that
   skips the Ready event of the labelled head; the decision to mark Ready is a `judgment step`.
   Whether the run starts is the event policy: HAZARD (#10). An edit of the title or the
   description after the review is text the review never read: HAZARD (#13).
3. Wait for the commit status `review`. `gh pr checks <N>` shows it. Gate: the required check.
   - `pending` on a pull request whose head carries the label `reviewed-<its sha>`: by design, the
     review of that head ran off the pipeline; the step is the owner's approval for that head at the
     merge (section 4, item 4), not a dispatch. On any other head, `pending` means no
     review ran on it. Ready, and no `review` run of this head in the Actions list: check
     `knowledge/pull-request-target-is-blocked-by-default.md` first, then dispatch it as in step 1.
   - `error`: open the run and read its last line, `REVIEW DID NOT COMPLETE (<kind>)`.
     `usage-limit`: wait for the reset the message names; running it again now fixes nothing.
     `credential`: only the owner can fix the secret. `budget`: run it again; what it found is
     posted, what converged is replayed, and the next run continues with the rest.
     `head-moved`: a push arrived; review the new head. `oversized`: split the change. `base`:
     the pull request must target the default branch. `rules`, `empty`, `bad-diff`: the line says
     what to change. `wrong-model`, `bad-output`, `cli-missing`, `api`, `failed`: a defect of the
     reviewer or the platform; find the cause before any further run.
   - No review can run in CI (no credential, or the reviewer itself is what the pull request
     adds): with the owner's go-ahead, in the background with the output in a file:
     `py -3 tools/review/review.py --pr <N> --local`. It prints one line per pass and every
     finding in full, posts the findings, labels the pull request `reviewed-<head sha>` so that
     Ready of that head starts no second run, posts an audit note that says whether the label
     landed, and posts no status (a label write the forge refuses ends the run with
     `REVIEW DID NOT COMPLETE (failed)`: add the label by hand before Ready): the merge then needs
     the owner's approval for that pull request and head. Run it while the pull request is a
     Draft, before step 2. HAZARD (#3). When the owner caps the bill, add `--passes <N>`: a batch
     then gets at most N passes, and the note names the limit.
4. Read every thread before fixing anything. Every thread, with its first comment:
   `gh api graphql --paginate -f owner=<owner> -f name=<name> -F pr=<N> -f query='query($owner: String!, $name: String!, $pr: Int!, $endCursor: String) { repository(owner: $owner, name: $name) { pullRequest(number: $pr) { reviewThreads(first: 50, after: $endCursor) { pageInfo { hasNextPage endCursor } nodes { isResolved path comments(first: 1) { nodes { body } } } } } } }'`
   What is still unanswered: `py -3 tools/pr_gates.py findings --pr <N>`. Gate: `pr-findings`.
5. Answer each finding by editing its file. On the design record, the reference, the corpus, the
   knowledge store and rule files nothing else counts: if the finding misread the text, change
   the text so that it cannot be misread. On tool, workflow and hook code a written reply also
   counts. Gate: `pr-findings`.
6. Low findings are not threads. The reviewer collects them on the open issue labelled
   `review-lows`; they hold no merge and are fixed together, or with a push that is needed
   anyway. A pull request needs no further round once a round reports nothing above low.
   Gate: `self-tests` for where the reviewer puts them; fixing them is a `judgment step`.
7. Order: edit, resolve the threads, then push ONE commit with every fix. Each push is a new
   head that needs its own review. Never push to cancel a running review; a push to a Ready
   pull request does cancel it. `judgment step`

## 4. Merge

1. All of these on the head commit: `gates` green, `review` green, zero unresolved threads,
   branch up to date with `main`. Gate: the ruleset.
2. Dry run: `py -3 tools/merge_pr.py <N> <full head sha> --dry-run`. Gate: the tool's exit code.
3. Merge: `py -3 tools/merge_pr.py <N> <full head sha>`. Never the merge button, never
   `gh pr merge`: the ruleset holds those to the checks and the threads, but not to the exact
   head, the last scan of title and description, or the read-back of the settings. HAZARD (#14).
4. If a required check is red or missing for a cause outside the change, stop and ask the owner.
   Only with the owner's approval for this pull request and this head:
   `py -3 tools/merge_pr.py <N> <sha> --over-red <N>@<sha>=<check>[,<check>]`. The tool switches
   the ruleset off for the one merge, restores it and reads it back. While it is off nothing on
   the server holds any other pull request or a push to `main`. HAZARD (#3): the approval is the
   caller's statement; the tool cannot verify it.
   - Exit 3: the ruleset is still off. Say so at once, then switch it back on and check it:
     `gh api repos/<owner>/<name>/rulesets --jq '.[] | "\(.id) \(.name) \(.enforcement)"'`,
     `gh api -X PUT repos/<owner>/<name>/rulesets/<id> -f enforcement=active`,
     `py -3 tools/merge_pr.py --assert-settings`. Gate: `merge-checks` is red until it is back.
   - Exit 4: the state of the merge is unknown. Look at the pull request before anything else.
   - Exit 6: the merge landed and the record of the waiver is missing: post the line the tool
     printed on the pull request.
5. Delete the branch. The `gates` job runs again on `main`; a red run there is fixed by the next
   pull request, before any other merges. `judgment step`

## When something escaped

A defect that reached `main` is answered in one pull request with: the rule and its gate in
`CLAUDE.md`, a knowledge entry with its `INDEX.md` line, and the gate map. Never a private note.
Gate: review rule "every pull request", for a lesson the description names while no changed file
records it.
