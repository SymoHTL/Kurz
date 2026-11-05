---
name: a-limit-before-any-paid-pass-is-no-round
description: Postmortem of 2026-11-02 - the reviewer's spend limit was reached while eleven review runs were in flight or queued, and five of them stopped at their first call with no pass paid, yet each put review-round-1 on before its first pass and so took its pull request's one round; the owner's answer (issue 156) - a run that the usage limit ends before any paid pass, no pass done and no call billed, takes its own label off again and is no round, while a run that paid a pass, a cancel and any other failure stay rounds
metadata:
  type: feedback
---

## What happened

On 2026-11-02 at 11:49 UTC the reviewer's spend limit was reached while eleven review runs were
in flight or queued. Each run puts its round's label, `review-round-<n>`, on before its first
pass ([[rounds-do-not-converge]]), so each of them took its pull request's one round. The rows
are the runs' own reports:

| Pull request | Passes before the limit | Bill | Findings posted | Label |
|---|---|---|---|---|
| #207 | 0, 3 of 3 batches failed | 0.00 USD | 0 | `review-round-1` |
| #209 | 0, 4 of 4 batches failed | 0.00 USD | 0 | `review-round-1` |
| #221 | 0, 6 of 6 batches failed | 0.00 USD | 0 | `review-round-1` |
| #226 | 0, 2 of 2 batches failed | 0.00 USD | 0 | `review-round-1` |
| #229 | 0, 2 of 2 batches failed | 0.00 USD | 0 | `review-round-1` |
| #204 | 12, 2 of 4 batches failed | 3.38 USD | 33 | `review-round-1` |
| #224 | 9, 3 of 3 batches failed | 2.49 USD | 31 | `review-round-1` |

With the cap at one round, every one of them was at the cap: a run after the reset would have
ended as `cap` before any pass, and the five in the first rows were never reviewed. Nothing had
been paid for them, and nothing had been read. The rule then said that a labelled run is a round
"whatever follows (a pass, a limit, a cut-off, a failure)"; the label before the first pass was
there so that a run cancelled or failed after a paid pass is counted
([[a-cancel-does-not-beat-the-runner]]), and a limit at the first call was counted with it.

## What the owner decided (2026-11-02, issue 156)

A run that the usage limit stops before any paid pass is no review round. The label still goes
on before the first pass; where a run ends with `usage-limit` and no pass was paid, it takes its
own label off again. Chosen against a one-off removal of that day's labels with the rule
unchanged, against keeping the rule, and against also taking the label off runs that paid passes
before the limit. A run that paid passes and then hit the limit stays a round: #204 and #224 are
at the cap with what they posted. The five pull requests with no pass paid get their label taken
off by hand and are reviewed after the reset; the change to the reviewer touches none of them.

## What the reviewer does

- "No pass paid" is read on the whole run: no batch finished a pass, and no failed call was
  billed (a retried answer that could not be read is billed, and its cost travels with the
  error). A file replayed from the cache is no pass, so a run whose other files all hit the limit
  at their first call is no round.
- Such a run takes off the label it put on, `review-round-<rounds + 1>`, and no other, before it
  posts its note and its status. The note says that no pass was paid, that the run is no round,
  that the label was taken off, and to run it again after the reset; the status reads "review did
  not complete: usage-limit, before any paid pass: no round counted". The exit code stays 3.
- A removal the forge refuses ends the run as `failed`, in the words of a refused add (whether the
  write landed is not known, so the run may count as a round or as none: read the labels),
  followed by the limit's own line, which names the reset.
- Unchanged: a run that paid a pass, a run that another failure ends (`api`, `budget`, a crash), and
  a cancel, which ends the process where it is, keep their label and are rounds.

## How to apply

- After a usage limit, read the labels before the next run: `gh pr view <N> --json labels`. A run
  that paid nothing left no label; one that paid a pass left its round.
- Two runs of one pull request that start at the same moment label the same round, and one of them
  can take off the label the other counts on: HAZARD #41, as are labels changed by hand.

Gate: `self-tests` (the label taken off at a limit before any paid pass, the note and the status
that say so, the label kept after a paid pass, after a billed call, after another failure and on a
cancel, a refused removal ending as `failed`, and a replayed file being no pass, each with its red
proof).
