---
name: what-a-review-pass-costs
description: Measured on 2026-10-02 - one review pass over a 30k-character batch thinks 60k to 120k tokens and takes 6 to 18 minutes, whatever the model and effort; per pass about 1.4 to 1.8 USD at list price on claude-opus-5-5, 3.5 to 4.3 on claude-fable-5-1; the first full review of a 20-batch pull request was 30 passes and about 45 USD; in CI a pass reports 0.19 to 0.26 USD (2026-10-07). A run in CI starts without a question to the owner since 2026-10-07; a run off the pipeline waits for the owner's go-ahead to the bill named
metadata:
  type: reference
---

Measured on 2026-10-02 with Claude Code CLI 2.1.283, through the reviewer's own call
(`tools/review/review.py`: `command`, `model_env`, the system prompt, the rules). The call's
environment is the reviewer's allow-list, not the session's, so the effort of a row is the one the
CLI was given (a call that inherits a session's environment ignores `--effort`:
[a-headless-call-inherits-its-session](a-headless-call-inherits-its-session.md)). Every row is one
first pass over the same batch: 30k characters of `tools/merge_pr.py` from pull request 8, which
with the rules and the description makes a prompt of 37.7k characters, about 16k input tokens.
Prices are the list prices of that day.

| Model | Effort | Wall time | Output tokens (thinking) | Cost (list price) | Findings |
|---|---|---|---|---|---|
| claude-opus-5-5 | high | 570 s | 61k (59k) | 1.35 USD | 8 |
| claude-opus-5-5 | medium | 756 s | 81k (79k) | 1.75 USD | 9 |
| claude-opus-5-5 | high, thinking capped at 20k | 708 s | 78k (75k) | 1.67 USD | 11 |
| claude-fable-5-1 | low | 663 s | 65k (64k) | 3.55 USD | 6 |
| claude-fable-5-1 | medium | 654 s | 63k | 3.45 USD | 6 |
| claude-fable-5-1 | high | 811 s | 79k | 4.27 USD | 5 |
| claude-fable-5-1 | xhigh | 655 s | 64k | 3.52 USD | 7 |
| claude-sonnet-5-5 | high | 854 s | 123k (122k) | 2.71 USD | 3 |

What the table says:

- **A pass is minutes, not seconds.** The model thinks four to eight times the size of its input
  before it answers, whatever the effort setting. The effort level changed nothing that could be
  told apart from the spread between two runs, and neither did `MAX_THINKING_TOKENS=20000` in the
  call's environment: that pass still thought 75k tokens.
- **The price is the model's.** The same work costs about 40 percent on `claude-opus-5-5` of what
  it costs on `claude-fable-5-1`.
- **The findings did not get better with the price.** All eight passes found the two defects that
  mattered most (an option the merge tool did not know was dropped, so a mistyped dry run merged
  for real; a setting that could not be read ended as exit 0). Together the three
  `claude-opus-5-5` passes reported every defect the other five passes reported but one, and five
  that none of the others reported. `claude-sonnet-5-5` thought longest and reported least. One
  batch is a small sample.
- **Cost is a list price.** With a subscription seat the same tokens count against the seat's
  limits instead.

The time a run has is one setting: `REVIEW_TIMEOUT_MIN`, counted from the job's start, of which
the reviewer keeps five minutes back for posting. In CI the workflow sets it to the job's
`timeout-minutes` (90 on 2026-10-02, pinned equal by `ci-config`); a run outside CI takes it from
the environment, 90 when it is not set.

The four runs over pull request 8, all on 2026-10-02 and all outside CI:

| Run | `REVIEW_TIMEOUT_MIN` | Batches | What happened |
|---|---|---|---|
| first | 90 (the default) | 16 | Each batch got all its passes in turn. 16 batches need at least 32 passes; with three workers that is more than the 85 minutes left for passes. Stopped by hand after 45 minutes, nothing posted. |
| second | 300 | 20 | Three passes at once are slower each: the first three took 918, 933 and 1149 seconds. With up to five passes a batch the time could not have reached every batch. Stopped by hand after those three passes, nothing posted. |
| third | 150 | 20 | Passes in rounds. 30 passes in 136 minutes: every batch once, ten batches twice, 379 to 1054 seconds a pass. 294 findings (20 high, 126 medium, 148 low). It ended red (`failed`): the forge refused the last posts, and 88 low findings were lost ([[a-paid-result-is-printed-before-it-is-posted]]). About 45 USD at list price, estimated from 30 passes: the run ended before it printed its cost. |
| fourth | 240 | 30 | Limited to one pass a batch. Its bill had been named to the owner as about 30 USD, from the 20 batches of the third run; the branch had grown since, and the first line of the log said 30 batches, about 45 USD. Stopped by hand within a minute, nothing posted. The three passes that were cut off cost an estimated 0.3 USD. |

What follows from it:

- The reviewer is pinned to `claude-opus-5-5` (`MODEL` in `review.py`). Changing the model means
  capturing the answers under `tools/review/fixtures/` again: the unit suite reads real answers of
  the pinned model.
- A review costs batches times passes, and a batch needs at least two passes. An ordinary pull
  request of one batch: two passes, about 3 USD off the pipeline and about 0.5 USD in CI, about
  20 minutes. In CI a pass reported 0.19 to 0.26 USD on 2026-10-07 (five runs: 1.94 USD for 10
  passes, 1.42 for 6, 1.04 for 4, 1.85 for 7, 2.47 for 10); off the pipeline a pass on the pinned
  model reported 1.34 USD the same day, just under the range above. Why the two places report
  prices this far apart was not established.
- A run outside CI can be cut to a bill named in advance: `--passes N` (1 to 5, with `--local`
  or `--dry-run` only) gives a batch at most N passes, so `--passes 1` costs batches times one
  pass. What it gives up is the second pass, the one that shows whether the first found
  everything. Its audit note names the limit and says "converged: no" when a last allowed pass
  found something above low. The limit is part of the replay cache's key: a run without it does
  not replay what a limited run stored.
- A run keeps what it has: it prints every finding, posts what it found and stores what converged
  before it ends red, and the log prints one line per pass.
- Passes run in rounds (`in_rounds` in `review.py`): every batch gets one pass before any batch
  gets a second. A time budget of about batches times pass time divided by three buys one pass
  over everything; a run that ends there is red (`budget`), and the next run continues.
- A review run in CI, a dispatch (`gh workflow run review.yml`) or the run the forge starts by
  itself when the session marks a pull request Ready or pushes to a Ready one, starts without a
  question to the owner. The question before every run, the owner's decision of 2026-10-02 after
  the first bills, was retired by the owner on 2026-10-07: it had stalled every round for a run
  that costs one to three USD in CI (the runs above; the plan's upper bound at the pass cap is a
  few USD). The bill is counted before the run and
  named in the report after it. A run off
  the pipeline (`--local`) spends the seat at local prices and starts only after the owner said go
  to the bill named. `judgment step`
- The bill is counted on the head that will be reviewed, never taken from an earlier run:
  `py -3 tools/review/review.py --pr N --plan` prints the batches a run would read and the
  passes that is, calls no model and posts nothing; with `--passes N` it counts for that limit.
  The bill is those passes times the price of a pass above. The count is checked against the
  run's first line: a run off the pipeline that names more batches than the plan did is stopped,
  because the go-ahead covered the plan; a run in CI is reported with the count it names.
  Gate: `self-tests` for the plan; the rest is a `judgment step`.
- Low findings do not keep a review going: a batch converges when a pass adds nothing above low,
  and lows are collected on the issue labelled `review-lows` instead of threads. They are fixed
  together, or with a push that is needed anyway (the owner's decision, 2026-10-02).
- The replay cache is keyed on the reviewer (`review.py`, `tools/kit.py`), the rules, the model,
  the pass limit, the title and the description. In CI the reviewer and the rules are the default
  branch's, so the cache drops when such a change is merged (then for every open pull request) and
  when the title or the description is edited. In a run outside CI the reviewer's code is the
  working tree's, while the rules are still read from the default branch
  ([the-review-runs-the-default-branch](the-review-runs-the-default-branch.md)): finish a change
  to the code before that review starts, or the next round reviews every file again; a local edit
  of the rules reaches no review until it is merged.
- Keep a pull request small. The review bill grows with the diff.
