---
name: a-follow-up-built-on-its-parents-head-carries-the-parents-commits
description: A follow-up branch committed on its parent pull request's head still carries the parent's commits after the parent is squash-merged; they are not ancestors of main, so the follow-up's diff against main shows the parent's change again, and a merge of main into it meets the parent's text on both sides and in neither base, and brings back with no conflict every line of the parent's that the follow-up deleted. On 2026-11-10 the follow-up of pull request 429 was moved onto main by cherry-pick and rebase, and opened from there as pull request 435; on 2026-11-14 pull request 541, already pushed, took main by a merge, and a commit of its own took out again what the merge brought back
metadata:
  type: feedback
---

On 2026-11-09 a session committed the follow-up of pull request 429 on 429's reviewed head,
62fe2fdf. Pull request 429 was then squash-merged as a6d08f1d: main took one new commit with the
parent's change, and 62fe2fdf and the commits under it are not ancestors of main. On 2026-11-10
the follow-up's own commit was cherry-picked onto main and opened from there as pull request 435.

On 2026-11-14 pull request 541, stacked on pull request 540 and already pushed, took main by a
merge after 540's squash merge, 265f146. The merge raised two conflicts, in the introductions of
the Go and Elixir research entries. It also brought back, with no conflict, every line of 540's
that 541 had deleted: `Emitter.ActorNotEmitted`, its check in `Run`, `RunTests`' `ActorCases` and
a line of the compiler's README. Against the merge base, b65eeb0, 541's side adds those lines and
deletes them again, which is no change, while main's side adds them, so the merge takes main's
side. 541's commit 7c5e58d took them out again, with its tree checked equal to main plus 541's own
diff against 540's last head (`git diff 8ac5814 5b9ae7a`) applied.

**Why:** a squash merge writes a new commit and leaves the branch's commits off main. A branch
built on them meets main at a merge base from before the parent, so its diff against main holds
the parent's change as well as its own. A merge of main into it has the parent's text on both
sides and not in the base: every line of the parent's that the follow-up changed, and every line
of it that main changed after that head, is a conflict. A line of the parent's that the follow-up
deleted is no conflict: the base never held it, the follow-up's side does not hold it either, and
main's side adds it, so the merge keeps it without a word. A review, a judge or a gate run on such
a head reads the parent's diff again.

**How to apply:**

- Once the parent merges, fetch main and cherry-pick the follow-up's own commits onto it, or
  `git rebase --onto origin/main <parent head> <follow-up>`, while the follow-up is not pushed.
  `judgment step`
- A follow-up already pushed takes main by a merge: "never merge main into the follow-up", which
  this entry read until 2026-11-14, holds no more for it, since a rebase would rewrite what was
  published. After the merge, compare its tree with main plus the follow-up's own diff, from the
  parent's commit it was built on: read main's tree into a spare index (`GIT_INDEX_FILE`), apply
  `git diff <parent's last head>...<follow-up's last commit before the merge>` to it with
  `git apply --cached`, write it with `git write-tree` and `git diff` that tree against the merge;
  take out what differs, and check the trees equal again. The three dots diff from the two
  commits' merge base, the parent's commit the follow-up was built on: two dots, from the parent's
  last head, would also hold the reverse of every commit the parent took after the follow-up's
  base, which main holds through the parent's squash merge, and taking out what differs would
  take those commits out of the merge. For a follow-up built on the parent's last head the two
  diffs are the same. `judgment step`
- Before the push, `git diff origin/main...<follow-up> --stat` lists the follow-up's own files
  alone. `judgment step`
- Generate the evidence guide again on that tree (`tools/quality_evidence.py`), and judge and run
  the gates on that head, never on the head from before the parent merged. `judgment step`
