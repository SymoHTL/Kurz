---
name: pull-request-target-is-blocked-by-default
description: Since GitHub's workflow execution protections (generally available 2026-09-17) a public repository without an Actions event policy gets a default rule that blocks pull_request_target, in evaluate mode until it is enforced on 2026-11-02; the review workflow needs an event policy that allows the event for its file, or a manual dispatch for every head
metadata:
  type: reference
---

The review workflow starts on `pull_request_target`, because that event runs the default
branch's workflow with secrets and a token that may write, and never the pull request's code
([[the-review-runs-the-default-branch]]). GitHub now treats that event as unsafe by default.

What GitHub says (changelog "Workflow execution protections in GitHub Actions generally
available", 2026-09-17, and the docs page "Securely using pull_request_target", both read
2026-10-02):

- A public repository that has no applicable Actions event policy gets a default rule that
  blocks workflows triggered by `pull_request_target`. Private and internal repositories do not.
- The default rule runs in evaluate mode first: runs continue, and "Policy insights" (in the
  repository settings, under the Actions policies) shows which runs it would have blocked.
- On 2026-11-02 it is enforced "for affected repositories that were using the default
  `pull_request_target` policy before general availability". Neither page says what holds for a
  repository created after general availability; this one was created on 2026-10-01.
- To keep the event, an Actions event policy has to allow it explicitly. A policy can be scoped
  to one workflow file.

What was observed here on 2026-10-02: `gh api repos/OWNER/REPO/actions/policies` answers
`{"total_count":0,"policies":[]}`. The default rule is not listed as a policy, so this endpoint
does not show whether it is in evaluate mode or enforced. No `pull_request_target` run has been
attempted yet, because the default branch has no review workflow before the first merge.

**How to apply:**

- A Ready pull request whose `review` status stays pending, with no `review` run in the Actions
  list at all, is this before it is anything else. A job skipped by its `if:` still shows a run
  ([[a-skipped-job-reports-success]]).
- The default rule names `pull_request_target` only. By that wording a `workflow_dispatch` run
  is not blocked, and it runs the default branch's workflow just the same:
  `gh workflow run review.yml -f pr=N` reviews one head on demand. Not tried here yet.
- The lasting fix is the owner's setting: an event policy that allows `pull_request_target` and
  `workflow_dispatch` for `.github/workflows/review.yml` only. Nothing asserts that policy
  (HAZARD #10); once it exists, `merge-checks` can read it through the endpoint above.
- Do not switch the review to `pull_request` to get around the block. That event runs the
  workflow file of the pull request itself, so a pull request could rewrite its own review.
