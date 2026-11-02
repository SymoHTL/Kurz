---
name: a-review-the-cache-answers-is-no-round
description: The owner, 2026-11-01, "Free replay isn't a round" - at the review cap a run whose every file replays from the reviewer's cache (each diff block byte-identical to one a converged review stored, under the same reviewer code, rules, model, title, description and pass limit) makes no pass, costs nothing, puts no round label on and completes, in CI with review at success; so pull requests with disjoint files are reviewed at the same time and merge one after another, each taking main with no paid round and no approval over red, while a run with one file the cache does not answer still ends as cap before any pass
metadata:
  type: feedback
---

## What the owner decided

Pull requests were reviewed one at a time. The ruleset requires a branch that is up to date with
`main`, and the status `review` is per commit. When one pull request merges, each other one takes
`main` and gets a new head, which the cap of one round ([[rounds-do-not-converge]]) did not let a review run
reach: the run ended as `cap` before it read the cache. The owner's answer of 2026-11-01 (issue 39,
"Free replay isn't a round"): a run on a new head that only replays is no round. The options
against it were switching off "up to date" in the ruleset (two pull requests that each pass can
break `main` together) and keeping one at a time (about 0.7 merges an hour); its cost was one
change to the review tool.

## Why the replay costs nothing

After each run `tools/review/review.py` stores the hash of every file's diff block whose batch
converged, under a key of the reviewer's own code (`review.py` and `tools/kit.py`, from the default
branch in CI), the rules, the model, the title, the description and the pass limit (`cache_key`,
`replayable`). A file whose block is byte-identical under the same key is not sent again. A run with
no file left makes no pass: its status reads "0 batches, 0 passes, $0.00", and the round label,
put on only before a first pass, stays as it was.

## When a head replays whole

- The round completed and converged on every file: a file of a batch at the pass cap, or of a
  batch that failed, is not stored.
- The title and the description are as the round read them. The review workflow runs on no edit,
  so an edit starts no run, and the run after it reads a new key and has every file to review.
- The merge of `main` changed no file of the pull request's diff. The block is
  `git diff --unified=3 <merge base> <head>` from its `diff --git` line on, the `index` line
  included, so a change of `main` anywhere in such a file changes the block.
- `main` took no change to `review.py`, `tools/kit.py` or `.review/review-rules.yaml` since the
  round: such a merge changes the key and drops the stored state of every open pull request.

The check before the push: `git diff --name-only <the merge base the round reviewed> origin/main`
names none of the pull request's files and none of those three. On a Draft,
`py -3 tools/review/review.py --pr N --plan` after the push prints 0 batches before a dispatch; at
the cap it refuses as `cap` when a file is left to review.

## How to apply

- Review several pull requests at the same time only when their files are disjoint and none of
  them changes the reviewer, `tools/kit.py` or the rules; one that does merges last.
- At the cap, the walk of the collection ticket comes first, at the head the round reviewed: the
  cap command refuses a finding made on another head ([[the-walk-at-the-cap-is-a-command]]). Then
  take `main` and push; on a Ready pull request the push starts the run.
- A replayed head is green: it merges with `merge_pr.py` and no `--over-red`.
- A wrong reading costs no money. The run ends as `cap` before any pass and before its label, its
  failure line names how many files of the head the cache does not answer, and the head carries
  `review` at `error`, which only the owner's approval of that head merges over.

Gate: `self-tests` (a run at the cap that only replays completes, makes no pass and labels nothing;
a run at the cap with one file to review ends as `cap`); the check before the push is a
`judgment step`.
