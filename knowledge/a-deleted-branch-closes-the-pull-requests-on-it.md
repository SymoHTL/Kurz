---
name: a-deleted-branch-closes-the-pull-requests-on-it
description: Postmortem of 2026-11-01 - pull request 188 was merged and its branch record-packages was deleted with git push origin --delete; pull request 192 had that branch as its base, and the forge closed it. It was reopened and retargeted by hand. A merged branch is deleted with py -3 tools/merge_pr.py --delete-branch <pr>, which gives every open pull request on the branch the merged pull request's base, reads the list again and deletes the branch only when none is left on it
metadata:
  type: feedback
---

## What happened

On 2026-11-01 pull request 188 was merged at 63c81d9. Item 5 of section 4 of the skill `change-walk`
said "Delete the branch", and the branch `record-packages` was deleted with
`git push origin --delete`. Pull request 192 had `record-packages` as its base, and the forge closed
it. Nothing had checked whether an open pull request had that branch as its base (#156, item 10).

The deletion was undone, and nothing was lost:

1. The branch was pushed again at the head of pull request 188.
2. Pull request 192 was reopened with `PATCH pulls/192 state=open`.
3. Its base was set to `main` with `PATCH pulls/192 base=main`.
4. The branch was deleted again.

A second stack was open on the same day: the base of pull request 185 is `source-raw-literal`, the
branch of pull request 184.

## Why

The forge's documentation says that when a head branch is deleted after its pull request has been
merged, the forge gives every open pull request on that branch the merged pull request's base (the
page "Creating and deleting branches within your repository", below the steps of a deletion on the
web). The deletion by `git push origin --delete` on 2026-11-01 got no such change: the forge closed
pull request 192. Which ways of deleting get it was not seen here:

- The button on the pull request and the repository's setting "Automatically delete head branches"
  were not tried. The setting is the owner's, and `merge-checks` does not read it.
- A deletion through the API (`DELETE git/refs/heads/<branch>`), which the command below sends, is
  taken as one that closes them, as the push did.

The two repairs differ in what they start:

- A reopening is the event `reopened`, and `review.yml` runs on it: it starts a review run.
- A change of base is the event `edited`. `gates.yml` runs on it, `review.yml` does not: it starts a
  `gates` run and no review.

## What changed

`py -3 tools/merge_pr.py --delete-branch <pr>` deletes the head branch of a merged pull request
through the forge. Before it deletes anything:

- It refuses a pull request that is not merged, a branch of another repository, and a branch whose
  head is not the merged head: such a branch holds commits the merge did not take.
- A branch that is already deleted is no error (exit 0).
- It prints the branch's head, with the push that puts it back, and each open pull request on the
  branch, with the call that changes its base back. A Ready pull request, and one at its review cap,
  is named as having a diff against its new base that no review has read: a stacked pull request
  gets no review in CI ([[the-review-runs-the-default-branch]]), and the change of base starts none.
- It gives each of them the merged pull request's base, so a chain keeps its order: the pull request
  on the branch of a pull request merged into another branch goes to that branch, not to `main`.
- It reads the list again, and refuses the deletion while a pull request still has the branch as its
  base. After a refused change of base it sends no further write.
- A write whose answer is missing is read back: exit 4 where the read-back fails too.

`--dry-run` prints the same and writes nothing. The tool's docstring has the exit codes.

## How to apply

- After a merge: `py -3 tools/merge_pr.py --delete-branch <N> --dry-run`, then the same without
  `--dry-run`. Never `git push origin --delete`, and never the forge's button on the branch.
  Gate: `self-tests` (the merge tool's cases "delete:" and the calls of `--delete-branch`, each with
  its mutation in `tools/red_proofs.json`, over the forge's answers for pull requests 184, 185 and
  188, captured on 2026-11-01). A deletion by hand or by a button skips the command, as a merge by
  the button skips the merge tool: HAZARD (#14).
- A pull request that the command retargeted may be another session's. Tell its owner that its base
  changed: a Ready one needs a review of its diff against the new base (CLAUDE.md, CI). `judgment step`
