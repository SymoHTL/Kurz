---
name: a-skipped-job-reports-success
description: On GitHub a job skipped by `if:` reports success to a required check, while a workflow that never starts leaves the check pending; a head with no gates run at all has a merge conflict or a skip literal in its head commit; how the two workflows here are built around that
metadata:
  type: reference
---

From GitHub's page on troubleshooting required status checks, read on 2026-10-01:

- A job that a conditional skips reports **success**. A required check with that job's name is
  satisfied, and the pull request can merge although nothing ran.
- A workflow that does not start at all (a path filter, a branch filter, a skip instruction in the
  commit message, a merge conflict on the pull request) leaves the required check **pending**, and
  pending blocks the merge.
- A required check has to pass on the latest commit of the pull request.

Two ways a workflow does not start, each read again on 2026-10-02:

- "Workflows will not run on `pull_request` activity if the pull request has a merge conflict"
  (GitHub docs, "Events that trigger workflows"). `pull_request_target` runs anyway.
- The skip instructions are `[skip ci]`, `[ci skip]`, `[no ci]`, `[skip actions]`,
  `[actions skip]` and the trailer `skip-checks: true`. They stop `push` and `pull_request`
  workflows when they stand in the commit message of a push, or in the head commit of a pull
  request (GitHub docs, "Skipping workflow runs"). They do not stop `pull_request_target`.

How this repository is built around it:

- `gates.yml` has one job, `gates`, with no `if:` on the job or on any step and no path filter.
  The runner behind it, `tools/gates.py`, runs every gate; which verdicts turn the run red is
  defined once, in its docstring. `tools/lint_ci.py` pins those facts of the workflow, and its
  self-test breaks each one in the real file.
- The review job is allowed to be skipped: a Draft and a pull request from outside are not
  reviewed automatically. So the required check is not the job. It is the commit status `review`,
  which the reviewer posts when a review completed. A skipped job posts nothing, and the check
  stays pending.
- Whether that status satisfies the ruleset's pinned app had not been seen on the live platform
  by 2026-10-02, because no review had run in CI yet. On 2026-10-07 it did: pull request 22 was
  mergeable on the status alone and merged with no waiver (issue #5 closed). The merge tool, which
  reads the same status, has to read it from the endpoint that names its creator:
  [[the-combined-status-drops-the-creator]]. What stays ungated: any workflow run of this
  repository can post a status of that name: HAZARD #11, see [[the-review-runs-the-default-branch]].
- A head with no `gates` run at all, and the check pending, is one of the two cases above: look
  for a merge conflict first, then for a skip literal in the head commit's message.
- A skip literal can also reach `main`, where it would skip the gates run on the merge commit.
  The squash commit is written from the title and the description by the merge tool, and from
  the commit messages by a merge through the button. So `tools/pr_gates.py title` (the gate
  `pr-title`) refuses a skip literal in the title, in the description and in every commit message
  of the branch, the merge tool checks the title and the description again at the merge, and the
  pre-push hook refuses one in a commit message before it is published. `pr-title` cannot report
  a literal in the head commit, because its own job does not start then: that is the pending
  check above.
