---
name: the-review-runs-the-base-branch
description: The review workflow uses pull_request_target, so the reviewer, its rules and the workflow file come from the base branch; a pull request cannot change how it is reviewed, and the first one had to be reviewed off-pipeline
metadata:
  type: reference
---

`.github/workflows/review.yml` is triggered by `pull_request_target`. GitHub then takes the
workflow file from the pull request's **base** branch and gives the job its secrets and a token
that may write. The job checks out the base branch only; the reviewer fetches the pull request's
commits as data, builds the diff with `git diff`, and never checks them out or runs them.

What follows from it:

- A pull request cannot weaken its own review. Its changes to `tools/review/`, to
  `.review/review-rules.yaml` or to the workflow are reviewed by the versions already merged, and
  take effect for the next pull request.
- "I changed the rules and the reviewer ignores them" is this, not a bug.
- The rules file is read through the API from the default branch, never from a checkout. While
  the default branch has no rules file, the reviewer refuses to run unless it is given
  `--bootstrap-rules FILE`, and it refuses that flag as soon as the default branch has one.
- The pull request that introduced the reviewer could not be reviewed by CI: no workflow on the
  base branch, no rules there. It was reviewed off-pipeline (`review.py --pr N --local
  --bootstrap-rules ...`), which posts an audit note instead of the `review` status, so its merge
  needed the owner's approval for that item (`merge_pr.py --over-red`).
- A stacked pull request has another branch as its base. The workflow file then comes from that
  branch, and the ruleset does not apply until the pull request is retargeted to `main`.
- Skip instructions in commit messages do not stop `pull_request_target` runs (GitHub docs, read
  2026-10-01).
- Anyone can open a pull request on a public repository, and each review spends the owner's
  Claude seat. The job's `if:` therefore runs only for the owner, members and collaborators; a
  pull request from outside stays unreviewed (and unmergeable) until the owner dispatches the
  workflow by hand. `tools/lint_ci.py` pins that expression.
