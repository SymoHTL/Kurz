---
name: a-skipped-job-reports-success
description: On GitHub a job skipped by `if:` reports success to a required check, while a workflow that never starts leaves the check pending; how the two workflows here are built around that
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

How this repository is built around it:

- `gates.yml` has one job, `gates`, with no `if:` on the job or on any step and no path filter.
  The runner behind it, `tools/gates.py`, runs every gate and is red when one fails or could not
  run. `tools/lint_ci.py` pins those facts, and its self-test breaks each one in the real file.
- The review job is allowed to be skipped: a Draft and a pull request from outside are not
  reviewed automatically. So the required check is not the job. It is the commit status `review`,
  which the reviewer posts only when a review completed. A skipped job posts nothing, and the
  check stays pending. Whether that status satisfies the ruleset's pinned app is not yet seen on
  the live platform: HAZARD #5.
- The skip instructions are `[skip ci]`, `[ci skip]`, `[no ci]`, `[skip actions]`,
  `[actions skip]` and the trailer `skip-checks: true`, for `push` and `pull_request` events
  (GitHub docs, "Skipping workflow runs", read 2026-10-01). On a pull request they leave the checks
  pending; in a squash-merge title they would skip the run on `main`. `tools/pr_gates.py title`
  refuses them in the title and in every commit message of the branch.
