---
name: a-squash-merged-branch-looks-unmerged-to-git
description: git branch -d refuses a local branch whose pull request was squash-merged, and git cherry origin/main <branch> marks every commit of it +, since the squash commit matches none of the branch's commits; neither says whether the content is on main. On 2026-11-09 three local branches were read with patch-id against the fetched heads of their pull requests, and the commits that matched none were looked up on main by their added lines
metadata:
  type: feedback
---

On 2026-11-09 a session read three local branches before deleting them: `classes-review-leftovers`,
`classes-construction-order` and `classes-r9-2`. `git branch -d` refused each as not merged, and
`git cherry origin/main <branch>` marked every commit `+`. The patch-ids of their commits, compared
with those of the fetched heads of their pull requests (371, 291, 330, 289, 328, 249 and 245),
matched every commit but one per branch. Two of those, 19e057f9 and 4cac83da, were found on main
by their added lines; `classes-r9-2` held work that is not on main and was kept. A second reading
on 2026-11-10 gave the same.

**Why:** a squash merge puts one new commit on main. No commit of the branch is an ancestor of
main, which is what `git branch -d` asks, and none has the squash commit's patch-id, which is what
`git cherry` compares. A pull request's head holds the branch's commits as they were pushed, so
their patch-ids match; a commit made after the last push matches none.

**How to apply:**

- Fetch the head of each merged pull request the branch fed: `git fetch origin pull/<N>/head:<ref>`.
  `judgment step`
- Compare `git log -p <merge base>..<branch> | git patch-id --stable` with the same for each head;
  a commit whose patch-id a merged head has reached main with that pull request. `judgment step`
- Look a commit that matches no head up on main by a line it adds,
  `git log -S '<line>' origin/main`; a branch with a commit found nowhere holds work that is not on
  main and is kept. `judgment step`
- `git branch -D` only a branch whose every commit was accounted for. `judgment step`
