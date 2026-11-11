---
name: rerere-replays-a-resolution-in-every-worktree
description: Git's rerere keeps its recorded resolutions in the common git directory, which every worktree of a clone shares, and it is on wherever that directory exists, with rerere.enabled unset; so a take of main in one worktree came out with a conflict already resolved by resolutions other merges of the clone had recorded (pull request 301, 2026-11-05: four of them, the merge aborted), and a take of main at the cap that holds such a conflict is outside the standing approval over red, which covers a merge with no conflict; take main with `git -c rerere.enabled=false merge --no-ff origin/main`, so that each conflict shows with its markers and nothing is replayed or recorded
metadata:
  type: reference
---

## What happened

On 2026-11-05 two takes of `main` came out with conflicts that git had already resolved:

- **Pull request 301.** An early merge of `main` reused four resolutions that rerere had recorded
  in other merges of the same clone. The merge was aborted.
- **Pull request 342.** Its take of `main` at the cap (57739e5) had one conflict in
  `kurz-design.md`, which a resolution recorded by an earlier local merge for its follow-up
  resolved. That take was parked for the owner (issue 342, comment 6671001741): a conflict is
  outside the standing approval over red.

## Why

- **rerere is on without a setting.** `git config rerere.enabled` prints nothing in the clone,
  but `.git/rr-cache` exists. Git turns rerere on when that directory exists and the setting is
  unset. Seen on 2026-11-06 with git 2.43.0, in a throwaway repository: a conflicting merge
  printed `Recorded preimage` only once `.git/rr-cache` had been created.
- **The directory is shared.** It lives in the common git directory (`git rev-parse
  --git-common-dir`), which every worktree of the clone shares. A conflict resolved and
  committed in one worktree is recorded there. The same conflict in a merge in any other
  worktree is resolved from it, with the line `Resolved '<file>' using previous resolution.`
- **The file still counts as unmerged, but it has no markers.** `git merge` still exits with
  "Automatic merge failed", and `git diff --name-only --diff-filter=U` still lists the file.
  The file itself has no conflict markers, so a reader sees a clean text. A `git add` then
  takes a resolution made for another branch without anyone reading it.
- **The standing approval covers a conflict only of the kinds it names.** The over-red rule of
  `CLAUDE.md` names them, and it is their definition; each came with an answer of the owner's on
  issue 39, but the one of 2026-11-09, which is on pull request 385:
  - text whose two sides changed different words, every word kept (2026-11-06, comment
    6680237539);
  - the floor of `RunTests` (2026-11-06, comment 6680659012), held to the bound the rule sets:
    `main`'s floor plus the pull request's own change to it. That bound came on 2026-11-07, with
    pull request 377, which answered the round of pull request 362 (comment 6683264161), and the
    owner chose it that day (comment 6711916489);
  - text in which both sides only appended, at the same end of an id list or of a table row, every
    word kept (2026-11-06, comment 6694541027);
  - a block whose sides have different line counts, read line by line, in which the sides changed
    different lines or both added items at the end of one list, every line kept (2026-11-07,
    comment 6710283110);
  - the numbers `tools/quality_evidence.py` generates in `guides/quality-bar-evidence.md`, nothing
    else of that file, resolved by that tool on the merged tree with the gate `knowledge` green
    (2026-11-09, pull request 385, comment 6735260898);
  - a chain of calls or a list whose two sides only added or removed different whole lines at its
    end, the line that was last or lines next to it, and changed no other line but by its end mark,
    taken off the line that was last or put on the line that became last, every added line kept,
    every removed one dropped, the end mark on the line that ends up last and on no other
    (2026-11-11, comment 6792362885, on the take of pull request 460).

  A conflict that rerere resolved is still a conflict, read as any other.

## How to apply

- Take `main` with rerere off for the one command:
  `git -c rerere.enabled=false merge --no-ff origin/main`. Each conflict then shows with its
  markers, and nothing is replayed or recorded. `judgment step`
- Read the merge's output before anything else. A line `Resolved ... using previous resolution`
  or `Recorded preimage` means rerere ran. Abort and merge again with the line above.
  `judgment step`
- A take of `main` that has a conflict is described to the owner as one, whoever resolved it.
  `judgment step`
- To survey which pull requests conflict with `main`, use `git merge-tree --write-tree origin/main
  <head>`. It does not use rerere. Seen on 2026-11-06 with git 2.43.0, in a second worktree of the
  same throwaway repository, whose rerere held a resolution for the conflict:
  `git merge-tree --write-tree --name-only <ours> <theirs>` still named the conflict, with exit 1.
  `judgment step`
