---
name: a-follow-up-built-on-its-parents-head-carries-the-parents-commits
description: A follow-up branch committed on its parent pull request's head still carries the parent's commits after the parent is squash-merged; they are not ancestors of main, so the follow-up's diff against main shows the parent's change again, and a merge of main into it meets the parent's text on both sides and in neither base. On 2026-11-10 the follow-up of pull request 429 was moved onto main by cherry-pick and rebase, and opened from there as pull request 435
metadata:
  type: feedback
---

On 2026-11-09 a session committed the follow-up of pull request 429 on 429's reviewed head,
62fe2fdf. Pull request 429 was then squash-merged as a6d08f1d: main took one new commit with the
parent's change, and 62fe2fdf and the commits under it are not ancestors of main. On 2026-11-10
the follow-up's own commit was cherry-picked onto main and opened from there as pull request 435.

**Why:** a squash merge writes a new commit and leaves the branch's commits off main. A branch
built on them meets main at a merge base from before the parent, so its diff against main holds
the parent's change as well as its own. A merge of main into it has the parent's text on both
sides and not in the base: every line of the parent's that the follow-up changed, and every line
of it that main changed after that head, is a conflict. A review, a judge or a gate run on such a
head reads the parent's diff again.

**How to apply:**

- Once the parent merges, fetch main and cherry-pick the follow-up's own commits onto it, or
  `git rebase --onto origin/main <parent head> <follow-up>`; never merge main into the follow-up.
  `judgment step`
- Before the push, `git diff origin/main...<follow-up> --stat` lists the follow-up's own files
  alone. `judgment step`
- Generate the evidence guide again on that tree (`tools/quality_evidence.py`), and judge and run
  the gates on that head, never on the head from before the parent merged. `judgment step`
