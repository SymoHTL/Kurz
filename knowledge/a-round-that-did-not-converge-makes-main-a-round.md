---
name: a-round-that-did-not-converge-makes-main-a-round
description: Postmortem of 2026-11-01 - pull request 181's one review round (7c696f8) completed with "NOT converged on 1 files" of its 23. After the walk at the cap, main was merged in (3523ce1) with two pull requests that shared none of its files, yet the cache, which stores only the files of a batch that converged, did not answer that file. The run ended as cap before any pass (run 41339738542), and the owner had to choose between a merge over red, a paid round and hold, and chose the round. A pull request at its cap whose round did not converge on a file cannot take main for free, and the merge tool's refusal of a branch behind main at the cap says which case it is, from the head's review status, failing closed
metadata:
  type: feedback
---

## What happened

Pull request 181 had its one review round in CI on its head 7c696f8. The run completed and posted
`review` at `success`, with the description "review completed, NOT converged on 1 files: 56 new
findings (2 high, 24 medium, 30 low), 8 batches, 28 passes, $7.50". One batch had stopped at the
pass cap.

The walk at the cap was taken at that head ([[the-walk-at-the-cap-is-a-command]]). Then `main` was
merged into the branch, as 3523ce1, which brought in pull requests 179 and 183. Those two shared
none of the 23 files, so every diff block of 3523ce1 was byte-identical to the one the round had
read. The run on 3523ce1 still ended as `cap` before any pass (run 41339738542), with `review` at
`error`, and the owner was asked to choose: merge over red at 3523ce1, pay one more round, or hold
the pull request. The owner chose the round (pull request 181, comment 6562516102). The review tool
lifts the cap for no one pull request, so the label `review-round-1` was taken off for that one
round, and the round's run put it back. The label reads one round where there were two
(HAZARD #41, a label taken off by hand).

## Why the replay did not answer it

After a run, `tools/review/review.py` stores the hash of a file's diff block only when the file's
batch converged. A batch at the pass cap is not stored ([[a-review-the-cache-answers-is-no-round]]),
so a head whose blocks are all unchanged still has that batch's files to review. At the cap, a run
with a file to review ends as `cap` before any pass.

The condition was written down: the rule "A pull request gets at most one review round" says that
a merge of `main` replays whole only where "the round converged on every file", and the walk at the
cap (the skill `change-walk`, section 3, item 8, sub-item 5) asks that the round's status not say
"NOT converged". It was a `judgment step` read from memory, and it was missed. That check is the
part of sub-item 5 a tool can read from the forge.

## What changed

`tools/merge_pr.py` refuses a branch behind `main`, as before. At the cap the refusal also says what
taking `main` costs, from the status `review` that counts on the head (`taking_main`). It names one
of three cases:

- The round converged on every file. Then a merge of `main` replays from the cache, as no round,
  where `main` changed none of the pull request's files and none of the reviewer's code and rules,
  and the title and the description are as the round read them. Otherwise it is a paid round. The
  tool does not read what `main` changed.
- The round did not converge on N files. Taking `main` costs a paid round, and the merge needs the
  owner's choice: a paid round, over red at the new head, or hold. The status gives the count. The
  files are named in the review's note "Automated review did not converge".
- Whether the round converged is not known: there is no status from the poster, or the status is
  pending or an error, or its description is in neither form. The refusal says what it says for a
  round that did not converge, and it names the state it read. A run at the cap that ended as `cap`
  on the reviewed head puts its `error` in the place of the round's status, so this case covers
  that head too.

Below the cap the refusal is unchanged. The tool merges no branch behind `main` in any case.

## How to apply

- Before taking `main` at the cap, run the dry run at the head the round reviewed,
  `py -3 tools/merge_pr.py <N> <full head sha> --dry-run`, and read its line on the branch behind
  `main`.
- When it says the round did not converge, or that this is not known, ask the owner before the
  merge: a paid round, over red at the new head, or hold.
- When it says the round converged, the rest of the check of sub-item 5 is still the caller's:
  which files `main` changed, and that the title and the description are unchanged.
- The status proves only that a workflow run of this repository posted it (HAZARD #11). A review
  that stopped at the pass cap still posts `success` (HAZARD #17), which is why the tool reads the
  description and not the state alone.

Gate: `self-tests` (the merge tool's cases "taking main": below the cap, did not converge and
converged, each over a status the forge sent; not known, over the converged one edited; and the
reviewer's own words; and the case "run: at
the cap a branch behind main is refused ...", which reads the refusal as the tool prints it). What
`main` changed is a `judgment step`.
