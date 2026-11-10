---
name: the-pull-request-shows-the-old-head-after-a-push
description: Read one second after a push, the pull request (GET repos/{owner}/{repo}/pulls/{n}) still named the old head, a minute later the new one, and 15 seconds after the next push (seen 2026-10-08); the resolve tool had compared the finding's commit with itself and reported the answered thread as "the file did not change"; pass the pushed commit as --head, and the tool refuses while the pull request shows another head: run it again. On 2026-11-09 a push that printed "cannot lock ref" moved the branch while the pull request kept the old head and no run started on the pushed commit; an empty commit on it, pushed as any push, was taken up
metadata:
  type: reference
---

Seen on 2026-10-08 on pull request 23. `py -3 tools/pr_gates.py resolve --pr 23 --go`, run in the
same shell line as the `git push` that carried the round's fix, printed
`left open: tools/pr_gates.py (<thread id>): the file did not change`. The finding sat on the old
head, and the pull request, read by the tool about a second after the push, still named that
head: the tool compared the file at the finding's commit with the file at the same commit. The
plan run again a minute later said `would resolve`, and `--go` resolved the thread. The gates
runs of the new head reached their pull-request gates after that and were green; the review run
of the head had started in the same shell line, with the thread still open. At the next push,
the same day, the pull request read 15 seconds after it named the new head at the first read.
So the old head was seen one second after a push and not after 15 seconds; what lies between
was not measured.

The shape: a tool that reads the head of a pull request from the forge right after the push
reads a value the forge has not updated yet, and every comparison against that head is a
comparison against the old head. The `findings` gate reads the head the same way, so a local
`gates.py --pr N` right after a push can read the old head too. In CI the gates runs of the two
heads pushed on 2026-10-08 read the threads resolved and were green; whether a run the head's
own event starts can read the old head was not seen, and nothing checks it: the sign would be a
red `pr-findings` on a head whose threads were resolved before the run, and the answer a re-run
of the gates.

## When the pull request does not catch up

Seen on 2026-11-09 on pull request 426. The merge of `main` into its branch was pushed as
77be7dff, and `git push` printed `remote rejected` with `cannot lock ref`: the branch "is at
77be7dff but expected 17ca00ec", the head before. The branch had moved all the same:
`git ls-remote` and the branches API named 77be7dff. The pull request did not: it named 17ca00ec
eight minutes later, and again when read before the next push, and no workflow run started on
77be7dff, neither `gates` nor `compiler-windows` nor the review run. The forge's status page named
no incident. An empty commit on 77be7dff, so the same tree, pushed as any push (8f64ef0a, the
message saying why), was taken up: the pull request named it, and the three workflows ran on it.
Closing and reopening the pull request was not tried, since a reopen can start a review run, and
a run that started is a bill ([[a-cancel-does-not-beat-the-runner]]).

The shape: the lag above ends by itself within a minute; this one did not. The `gates` workflow
starts on the pull request's events (`synchronize` among them, in `.github/workflows/gates.yml`),
and a pull request that did not take up the push sends none, so the checks it shows stay those of
the old head: on pull request 426 they were green, `gates`, `compiler-windows` and `review` on
17ca00ec. Nothing is merged wrongly: the merge tool takes the head in full
and refuses while the pull request shows another (`tools/merge_pr.py`). What is lost is the time
spent waiting for runs that never start. What made the forge fail the lock was not found.

## How to apply

- The resolve tool takes the pushed commit as `--head`, in full or its first seven or more hex
  digits, and refuses, before any read of a thread or any write, while the pull request shows
  another head: `the pull request shows head <a>, not <b>: the push has not reached it yet, or
  the checkout is elsewhere; run again`, the head shown in full and the `--head` as given.
  Gate: `self-tests` (a forge that shows
  another head is refused with nothing read past the pull request, one that shows the pushed
  head runs for the full, the short and the upper-case form, and a value that is no commit is
  refused; each with its mutation in `tools/red_proofs.json`).
- Without `--head`, the plan names the shape only where a finding sits on the head the pull
  request shows: `the head is still the finding's commit: nothing was pushed since, or the push
  has not reached the pull request yet; run the plan again`; a finding from an earlier round
  reads as "the file did not change" on such a read, with no sign. Gate: `self-tests` (the plan
  with the finding's commit as the head, and the write path with a forge that still shows it;
  each with its mutation). With `--head` matched, the same finding is reported as `the finding
  sits on the pushed head`, since the match proved the push landed, with its case and mutation.
- After the push, run the plan with `--head` and without `--go` first; `--go` comes when the plan
  lists the answered threads, and Ready after `--go` printed them as resolved
  ([[resolve-what-the-edit-answered]]). `judgment step`
- A local `gates.py --pr N` right after a push reads the head the same way and has no `--head`:
  compare the head of `gh pr view N --json headRefOid` with the pushed commit before running it,
  or read its `pr-findings` verdict as one about the head the forge showed. `judgment step`
- After a push that printed `cannot lock ref`, or when no run started on the pushed commit within
  a few minutes, compare the branch (`git ls-remote origin <branch>`) with the pull request's head
  (`gh pr view N --json headRefOid`). Where the branch has the commit and the pull request does
  not, commit nothing but an empty commit on it (`git commit --allow-empty`, the message saying
  that the pull request did not take up the push) and push it as any push, never with `--force`;
  never close and reopen the pull request to wake it. A merge note names the empty commit as the
  head and says its tree is the pushed one's, so that gates run on that tree stand. HAZARD (#432):
  nothing notices a push the pull request did not take up.
