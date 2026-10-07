---
name: a-cancel-does-not-beat-the-runner
description: Postmortem of 2026-10-07 - the review run that GitHub starts when a pull request goes Ready was cancelled six seconds in, while the forge still listed it as queued, and ran two paid passes before the runner got the signal; the findings died with the process and nothing was posted. A run that started is a bill, cancelled or not, so a run that must not start is kept from starting - a completed off-pipeline review labels the pull request reviewed-off-pipeline and the job's if skips it - and nothing is cancelled to save its cost
metadata:
  type: feedback
---

On 2026-10-07 pull request 21 had been reviewed off the pipeline (`review.py --pr 21 --local
--passes 1`, with the owner's go-ahead), its findings answered and its threads resolved. Marking
it Ready starts the review workflow on `pull_request_target`, and that run would have reviewed
the same head again, at the pass cap, for nothing. The session marked the pull request Ready and
cancelled the run six seconds later, while the forge still listed it as queued, as it had done
for pull requests 9 and 20. Those two runs ended before any model call. This one did not: the job
had already been handed to a runner, the cancellation reached it only after about seventy
seconds, and by then two of the four batches had finished a pass (9 findings), the other two were
in flight, and the process was killed where it ran. The log holds the two "found N" lines and
nothing else: the reviewer prints every finding before it posts, but at the end of the run, so a
run killed before its end loses them. Nothing was posted: no status, no thread, no low. The cost,
which only the run's last line would have named, is estimated at 3 to 5 USD from the local run's
price per pass. The owner had not approved it.

Why it worked twice and failed once: cancelling a run is a request to the forge, not to the
process. A queued run ends before it starts only when no runner has taken it yet, and how long
that takes is the forge's queue, which the caller does not control. A procedure that depends on
winning that race spends money at random.

**How to apply:**

- A run that started is a bill, cancelled or not. Never start one, and never let the forge start
  one, meaning to cancel it; `gh run cancel` is for a run that is wrong, not for one that is
  unwanted.
- A run that must not start is kept from starting at its trigger: the review workflow's job skips
  a pull request labelled `reviewed-off-pipeline`, and a completed run of `review.py --local`
  puts that label on the pull request. `tools/lint_ci.py` pins the clause of the job's `if`, and
  the reviewer's self-test proves the label. No tool removes the label: the ruleset still wants
  the `review` status, so a stale label keeps a paid run from starting and holds nothing else.
- A head whose review ran off the pipeline has no `review` status and is merged with the owner's
  approval for that head (`merge_pr.py --over-red`), as before; the label changes nothing there.
- Findings that exist only in a running process are not yet a result. The reviewer prints its
  findings in full before it posts, but at the end of the run; a kill before the end loses them
  all the same. The answer here is the trigger, not the print: the run is not started.
