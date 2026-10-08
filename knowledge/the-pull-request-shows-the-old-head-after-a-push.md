---
name: the-pull-request-shows-the-old-head-after-a-push
description: Right after a push, the pull request read from the forge (GET repos/{owner}/{repo}/pulls/{n}) can still name the old head for a few seconds (seen 2026-10-08); the resolve tool then compared each finding's commit with itself and reported every answered thread as "the file did not change"
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
of the head had started in the same shell line, with the thread still open.

The shape: a tool that reads the head of a pull request from the forge right after the push
reads a value the forge has not updated yet, and every comparison against that head is a
comparison against the old head. The `findings` gate reads the head the same way; in CI it runs
on the event of the head and cannot be early, and a local `gates.py --pr N` right after a push
can.

## How to apply

- The resolve plan names the shape: a finding whose commit is the head the pull request shows is
  left open with `the head is still the finding's commit: nothing was pushed since, or the push
  has not reached the pull request yet; run the plan again`, never with "the file did not
  change". Gate: `self-tests` (the plan with the finding's commit as the head, and the write path
  with a forge that still shows it; each with its mutation in `tools/red_proofs.json`).
- After the push, run the plan without `--go` and read its lines; that reason means wait and run
  it again; `--go` comes when the plan lists the answered threads, and Ready after `--go` printed
  them as resolved ([[resolve-what-the-edit-answered]]). `judgment step`
