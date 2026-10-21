---
name: rounds-do-not-converge
description: Postmortem of 2026-10-21 - pull request 33 took thirteen review rounds in CI from 2026-10-15 to 2026-10-21, ten of them in one night, and the last six each returned 13 to 20 new findings, 3 to 7 of them medium, at 2.63 to 3.98 USD a round, 31.56 USD in all by the runs' own reports, without converging. The owner capped review rounds at three per pull request, the earlier rounds counted; the reviewer labels each round before its first pass and ends a fourth run before any pass, and what the third round leaves open goes on the collection ticket
metadata:
  type: feedback
---

Pull request 33 carried design round 15 into the design record and the reference. It was opened on
2026-10-15 and merged over red on 2026-10-21, at `7ec4e5e`, with the owner's approval, after
thirteen review runs in CI. Every row below is a run's own report: its first line (files, batches)
and its `FOUND:` line (findings, passes, the cost it names), read from the Actions logs on
2026-10-21. Times are UTC.

| Round | Started | Head | Files | Batches | Passes | Cost (USD) | New findings (high, medium, low) |
|---|---|---|---|---|---|---|---|
| 1 | 2026-10-15 20:21 | `c8900dca` | 1 | 1 | 3 | 0.64 | 17 (1, 10, 6) |
| 2 | 2026-10-15 21:06 | `14c50889` | 1 | 1 | 3 | 0.77 | 13 (0, 9, 4) |
| 3 | 2026-10-17 12:08 | `f73d7b4c` | 1 | 2 | 0 | 0.00 | none: it stopped on the weekly usage limit before its first pass |
| 4 | 2026-10-20 21:01 | `6b9debc5` | 1 | 2 | 8 | 1.95 | 17 (0, 10, 7) |
| 5 | 2026-10-20 22:38 | `6f769ac9` | 2 | 2 | 7 | 2.36 | 13 (0, 9, 4) |
| 6 | 2026-10-20 23:06 | `a85849a6` | 2 | 3 | 9 | 2.65 | 12 (0, 5, 7) |
| 7 | 2026-10-20 23:48 | `f088a921` | 3 | 4 | 10 | 2.84 | 17 (0, 6, 11) |
| 8 | 2026-10-21 01:12 | `808ac24b` | 3 | 4 | 9 | 2.63 | 13 (0, 6, 7) |
| 9 | 2026-10-21 02:57 | `dc0dc241` | 4 | 4 | 8 | 2.76 | 17 (0, 6, 11) |
| 10 | 2026-10-21 03:39 | `80f111e4` | 6 | 4 | 10 | 3.72 | 20 (0, 7, 13) |
| 11 | 2026-10-21 04:22 | `3aa3ce45` | 6 | 5 | 10 | 3.51 | 13 (0, 6, 7) |
| 12 | 2026-10-21 04:57 | `864bda7a` | 6 | 5 | 11 | 3.98 | 17 (0, 4, 13) |
| 13 | 2026-10-21 05:32 | `7ec4e5e8` | 7 | 4 | 8 | 3.75 | 18 (0, 3, 15) |

In all: 96 passes, 187 new findings (82 above low), 31.56 USD; the ten rounds of the night of
2026-10-20 cost 30.15 USD of it.

What the rows show:

- **The rounds did not converge.** Every round from the fourth on reported between 3 and 10
  findings above low, and a pull request needed a further round as long as a round reported any
  (`CLAUDE.md`, "Low findings open no thread and hold no merge"), so every round called for another. The count above low fell slowly, from 10 to 3
  over ten rounds, and never reached zero.
- **The diff grew with the fixes.** Each round's push answered its threads by edits, as the thread
  rule asks, and the edits widened the change: from one file in one batch to seven files in four
  or five batches. A round costs batches times passes, so the bill of a round grew from 0.64 to
  about 4 USD while what it found stayed the same size.
- **Nothing was replayed.** Every run read every file again ("0 replayed from the cache" in each
  first line): a file is replayed only when it did not change since a converged review, and every
  file changed in every round.
- **Each run was small on its own.** The bill was counted before each run as the walk says, and no
  single run was over 4 USD; what no rule counted was the rounds. The third round paid nothing and
  was a round all the same.

**What changed (the owner, 2026-10-21):**

- A pull request gets at most three review rounds, and the rounds before the cap count (`CLAUDE.md`,
  "A pull request gets at most three review rounds"). No fix push follows the third: what is still
  open goes on the collection ticket, the open issue labelled `review-leftovers`, one section per
  pull request with the head the findings were made on and the links of its threads and lows; each
  open thread gets a reply that links the ticket and is resolved, and the pull request merges as it
  is, over red, on the approval the cap gives, for the head the third round reviewed.
- The reviewer counts the rounds: a run that posts labels the pull request `review-round-<n>` before
  its first pass, the count is the highest `n`, and at three every run ends before its first pass as
  `REVIEW DID NOT COMPLETE (cap)`, in CI with the status `review` at `error`. The end is inside the
  job, since a job its `if` skips reports success ([[a-skipped-job-reports-success]]).
- At the cap, and only there, the gate `pr-findings`, the resolve command of `tools/pr_gates.py` and
  the merge tool take a reply that links the collection ticket as the answer to a finding on any file.
- Pull request 33's three open threads of its thirteenth review are the first section of the
  collection ticket (issue 39), and its fifteen lows are on the `review-lows` issue.

**How to apply:**

- Read the round count on the pull request before a round starts: `gh pr view <N> --json labels`.
  The third round is the last; the push before it carries every fix that can be made, the lows of
  the pull request too.
- A round is a bill whatever it finds or costs: a run cancelled after its first pass, and one that
  stopped on a limit, are rounds.
- Keep the change small, so that the three rounds are enough: the bill of a round, and what a round
  finds, grow with the diff ([[what-a-review-pass-costs]]).
- What the labels do not hold: a label taken off or put on by hand, two runs of one pull request
  that start at the same moment, and a dry run, which posts no label (HAZARD #41).
