---
name: change-walk
description: >-
  Take one change to main in this repository: branch, local gates, push, Draft pull request,
  gates job, review, threads, merge with the merge tool. Use for every change, including a design
  round's record, and when a gate or the review is red and you need to know what to do next.
---

# The walk one change takes

The picture is `knowledge/diagram-change-walk.md`. Commands are for Git Bash on Windows; on other
systems `py -3` is `python3`.

## 0. Before anything

1. `git fetch origin` and branch off the fresh remote ref: `git switch -c <branch> origin/main`.
   Never commit on `main`; the ruleset refuses the push.
2. Once per clone: `git config core.hooksPath .githooks`. `py -3 tools/gates.py` tells you when it
   is missing (gate `push-hook`).
3. The repository is public. Nothing bound to this machine or its owner, and no future plans, in
   any file, commit message, branch name, title or description.

## 1. Local proof

1. Edit. A new decision in a tool gets a self-test case and an entry in `tools/red_proofs.json`:
   break the rule in one place, watch `py -3 tools/<tool>.py --self-test` print `FAIL <case>`,
   record the anchor, the replacement and the case name, and revert.
2. `py -3 tools/gates.py`. Everything must be PASS; the pull-request gates say NOT RUN until a
   pull request exists. PARTLY on `merge-checks` means this login cannot read two settings, as
   in CI; with the owner's login it is PASS.
3. A change to a gate, tool, hook or review rule also updates `knowledge/diagram-gate-map.md` and,
   when the mapping changes, `guides/quality-bar.md`.

## 2. Push and open a Draft

1. Commit. No workflow-skip literal in any message.
2. `git push -u origin <branch>`. The pre-push hook runs the tree gate over every commit; a
   refusal names the commit and the string's kind, never the string.
3. `gh pr create --draft --title "<title>" --body-file <file>`. The description says what and why.
   If the change touches `tools/`, `.github/`, `.review/`, `.claude/` or `.githooks/`, or more
   than 15 files, it has a `## Blast radius` section naming what can break and who reads it.
4. The `gates` job runs on every push and on every edit of the title or description. Red: open
   the log, read the `=== gates` table at its end, fix the first FAIL.

## 3. Review

1. Iterate in Draft. For a review on demand: `gh workflow run review.yml -f pr=<N>`.
2. When the description is final, mark it Ready once: `gh pr ready <N>`. That starts the review.
   It uses the workflow, the reviewer and the rules of the default branch.
3. Wait for the commit status `review`. `gh pr checks <N>` shows it.
   - `pending`: no review ran on this head. Ready, and no `review` run in the Actions list at
     all: GitHub's default policy blocked the event
     (`knowledge/pull-request-target-is-blocked-by-default.md`); dispatch it as in step 1.
   - `error`: open the run and read its last line, `REVIEW DID NOT COMPLETE (<kind>)`.
     `usage-limit`: wait for the reset the message names; re-running now spends nothing and fixes
     nothing. `credential`: only the owner can fix the secret. `budget`: re-run the job; what it
     found is posted, what converged is replayed, and the next run continues with the rest.
   - No review can run in CI (no credential yet, or the reviewer itself is what the pull request
     adds): ask the owner first, a review spends his Claude seat
     (`knowledge/what-a-review-pass-costs.md`). Then, in the background with the output in a
     file: `py -3 tools/review/review.py --pr <N> --local`. It prints one line per pass, posts
     the findings and an audit note, and no status: the merge then needs the owner's approval
     for that pull request and head.
4. Read every thread before fixing anything:
   `gh api graphql -f query='query { repository(owner:"OWNER", name:"REPO") { pullRequest(number: N) { reviewThreads(first: 100) { nodes { isResolved path comments(first: 1) { nodes { body } } } } } } }'`
5. Answer each finding by editing its file. On the design record, the reference, the corpus, the
   knowledge store and rule files nothing else counts: if the finding misread the text, change
   the text so that it cannot be misread. On tool and workflow code a written reply also counts.
6. Order: edit, resolve the threads, then push ONE commit with every fix. Never push to cancel a
   running review. Each push is a new round that costs a review.

## 4. Merge

1. All of these on the head commit: `gates` green, `review` green, zero unresolved threads,
   branch up to date with `main`.
2. Dry run: `py -3 tools/merge_pr.py <N> <full head sha> --dry-run`.
3. Merge: `py -3 tools/merge_pr.py <N> <full head sha>`. Never the merge button, never
   `gh pr merge`.
4. If a required check is red or missing for a cause outside the change, stop and ask the owner.
   Only with his approval for this pull request and this head:
   `py -3 tools/merge_pr.py <N> <sha> --over-red <N>@<sha>=<check>[,<check>]`. The tool switches
   the ruleset off for the one merge, restores it and reads it back. While it is off nothing on
   the server holds any other pull request or a push to `main`. Exit 3 means it is still off:
   say so at once. Exit 6 means the merge landed and the record of the waiver is missing: post
   the line the tool printed on the pull request.
5. Delete the branch. The `gates` job runs again on `main`.

## When something escaped

A defect that reached `main` is answered in one pull request with: the rule and its gate in
`CLAUDE.md`, a knowledge entry with its `INDEX.md` line, and the gate map. Never a private note.
