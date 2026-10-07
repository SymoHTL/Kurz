---
name: the-review-runs-the-default-branch
description: The review workflow uses pull_request_target, so the workflow file, the reviewer and its rules come from the repository's default branch, never from the pull request; a change to the reviewer or its rules reviews nothing until it is merged, the first pull request had to be reviewed off-pipeline, and what the status proves is only that some workflow run of this repository posted it (HAZARD #11)
metadata:
  type: reference
---

`.github/workflows/review.yml` is triggered by `pull_request_target`. GitHub runs that event "in
the context of the default branch of the base repository": the workflow file is the one on the
default branch, and `GITHUB_SHA` is the last commit on the default branch (GitHub docs, "Events
that trigger workflows", read 2026-10-02). The job gets its secrets and a token that may write.
It checks out the default branch only; the reviewer fetches the pull request's commits as data,
builds the diff with `git diff`, and never checks them out or runs them.

Until 2025-12-08 the event took the workflow from the pull request's base branch, whichever
branch that was (GitHub changelog, 2025-11-07, "Actions pull_request_target and environment
branch protections changes"). Older write-ups say "base branch", and so did the first version of
this entry.

What follows from it:

- A review that the forge starts for a pull request uses the merged workflow, reviewer and
  rules. A pull request's changes to `tools/review/`, to `.review/review-rules.yaml` or to the
  workflow are reviewed by the versions already merged, and take effect for the next pull request.
- "I changed the rules and the reviewer ignores them" is this, not a bug.
- The rules file is read through the API from the default branch, never from a checkout. While
  the default branch has no rules file, the reviewer refuses to run unless it is given
  `--bootstrap-rules FILE`, and it refuses that flag as soon as the default branch has one.
- The pull request that introduced the reviewer could not be reviewed by CI: no workflow on the
  default branch, no rules there. It was reviewed off-pipeline on 2026-10-02 (`review.py --pr N
  --local --bootstrap-rules ...`), which posts an audit note instead of the `review` status, so
  its merge needs the owner's approval for that item (`merge_pr.py --over-red`).
- A stacked pull request has another branch as its base. It gets no review in CI: the job's `if`
  skips a pull request whose base is not the default branch, and a dispatched review refuses it
  (kind `base`). A `review` status on its head would also count for a pull request of the same
  head into the default branch, whose diff nobody reviewed. Retarget it first, or review it
  off-pipeline, which posts no status.
- Skip instructions in commit messages do not stop `pull_request_target` runs (GitHub docs,
  "Skipping workflow runs", read 2026-10-02).
- Anyone can open a pull request on a public repository, and each review spends the owner's
  Claude seat. The job's `if:` therefore runs only for the owner, members and collaborators; a
  pull request from outside stays unreviewed (and unmergeable) until the owner dispatches the
  workflow by hand. `tools/lint_ci.py` pins that expression.
- GitHub blocks this event by default in a public repository:
  [[pull-request-target-is-blocked-by-default]].

What it does not give:

- The hand dispatch runs the ref it is started on. `gh workflow run review.yml -f pr=N` without
  `--ref` takes the default branch, and the job's `if` refuses any other ref. That `if` lives in
  the file itself: a branch can carry a copy of the workflow without it, and a dispatch on that
  branch runs the copy, with its own reviewer, this repository's secrets and a token that may
  post statuses.
- The ruleset pins `review` to the app that every workflow run of this repository reports as
  (`tools/fixtures/branch-rules.json` shows the pin); a commit status posted with a workflow's
  token satisfied that pin on 2026-10-07 (issue #5 closed), seen once and re-checked by nothing. A
  workflow that a branch adds, with
  `statuses: write`, can post `review` = success on any head the statuses API is given, its own
  or another pull request's. So the status proves that a workflow run of this repository posted
  it, not that a review completed.
- Both gaps are HAZARD #11. The one thing that holds them is that only people who may write here
  can push a branch or dispatch a workflow. That the merged reviewer reads a workflow change holds
  nothing: a branch that never becomes a pull request can still run its copy, and a status on a
  pull request's own head is green the moment it is pushed, before any thread exists, so the
  button can merge before the first pass ends. Closing it needs an identity that a pull request's own
  workflows cannot use (a dedicated app, or an environment only the default branch may use),
  which is the owner's choice.
