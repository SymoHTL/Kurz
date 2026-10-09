---
name: a-re-serialized-ledger-is-a-whole-file-diff
description: A one-off script wrote tools/red_proofs.json back with an indent of one; the entries were the same and every line was different, so the review plan of the pull request grew from 20 to 34 batches, 14 of them the ledger alone (2026-10-09); since 2026-10-09 (pull request 26) the tool refuses a ledger that is not in its one form, and --format writes it
metadata:
  type: reference
---

Seen on 2026-10-09 on pull request 26, before its fourth review round, in the plan:
`py -3 tools/review/review.py --pr 26 --plan` listed 34 batches where the round before had 20, and
14 of them held `tools/red_proofs.json` alone.

The session's helper scripts write the ledger one entry per line, two spaces in, the text as it
is. A one-off script that dropped two entries wrote it back with `json.dump(..., indent=1)`. The
entries were the same; every line was different. A review reads the diff against `main` batch by
batch and bills every batch. In CI a pass costs 0.19 to 0.31 USD (what-a-review-pass-costs, and the
run that followed on 2026-10-09: 55 passes, 17.01 USD) and a batch takes 2 to 5 passes, so fourteen
batches are 5 to 22 USD; off the pipeline, at 1.3 to 1.8 USD a pass, 36 to 126 USD. All of it for a
change of nothing, and every finding the reviewer made on them would have been a finding on noise.

What catches it, since 2026-10-09 (pull request 26): `tools/red_proof.py` refuses a ledger that is not
in its one form, locally and in CI (gate `self-tests`), and `py -3 tools/red_proof.py --format` writes it so. What showed
it: the plan's per-file batch list, read before Ready, and
`git diff --numstat main -- tools/red_proofs.json`: a change of some entries is about that many
lines, never the whole file.

The shape, for any generated file a review reads: a script that writes a file back in another
style than the one it was written in makes every line a change. Read the plan's batch list, or the
numstat, before a run that bills.
