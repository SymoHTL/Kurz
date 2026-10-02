---
name: what-a-review-pass-costs
description: Measured on 2026-10-02 - one review pass over a 30k-character batch thinks 60k to 120k tokens and takes 10 to 14 minutes on every model and effort tried; per pass about 1.4 to 1.8 USD on claude-opus-5-5, 3.5 to 4.3 on claude-fable-5-1; what that means for a large pull request
metadata:
  type: reference
---

Measured on 2026-10-02 with Claude Code CLI 2.1.283, through the reviewer's own call
(`tools/review/review.py`: `command`, `model_env`, the system prompt, the rules). Every row is one
first pass over the same batch: 30k characters of `tools/merge_pr.py` from pull request 8, which
with the rules and the description makes a prompt of 37.7k characters, about 16k input tokens.

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

What follows from it:

- The reviewer is pinned to `claude-opus-5-5` (`MODEL` in `review.py`). Changing the model means
  capturing the answers under `tools/review/fixtures/` again: the unit suite reads real answers of
  the pinned model.
- A review costs batches times passes, and a batch needs at least two passes. An ordinary pull
  request of one batch: two passes, about 3 USD, about 20 minutes. Pull request 8, which
  introduced the whole quality bar, had 16 batches: at least 32 passes, about 50 USD, and with
  three workers more than the 85 minutes one run has. Its first review ran 45 minutes and had to be
  stopped with nothing posted.
- That is why a run keeps what it has (it posts what it found and stores what converged before it
  ends red), and why the log prints one line per pass.
- A change to `review.py`, `tools/kit.py`, the rules file, the title or the description drops the
  replay cache: the next round reviews every file again. Finish those before the review starts.
- Keep a pull request small. The review bill grows with the diff, and it is paid again in every
  round that touches the reviewer itself.
