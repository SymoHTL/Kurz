---
name: pull-request-target-is-blocked-by-default
description: Since GitHub's workflow execution protections (generally available 2026-09-17) a public repository without an Actions event policy gets a default rule that blocks pull_request_target; since 2026-10-03 the owner's policy allows pull_request_target and workflow_dispatch for the review workflow (a restrict_action_events rule, both events, since the list is closed; a policy with no rules allows nothing); a Ready pull request with no review run is still checked for this first, and the review is dispatched on the default branch when the event did not fire
metadata:
  type: reference
---

The review workflow starts on `pull_request_target`, because for that event GitHub takes the
workflow file from the default branch and gives the run secrets and a token that may write
([[the-review-runs-the-default-branch]]). The event itself protects nothing more than that: a
workflow that checks out or runs the pull request's head runs that code with those secrets.
Here the safety comes from the workflow never doing so, which `ci-config` pins (the one checkout
names the default branch). GitHub treats the event as unsafe by default.

What GitHub says (changelog "Workflow execution protections in GitHub Actions generally
available", 2026-09-17, and the docs page "Securely using pull_request_target", both read
2026-10-02):

- A public repository that has no applicable Actions event policy gets a default rule that
  blocks workflows triggered by `pull_request_target`. Private and internal repositories do not.
- The default rule runs in evaluate mode first: runs continue, and "Policy insights" (in the
  repository settings, under the Actions policies) shows which runs it would have blocked.
- On 2026-11-02 it is enforced "for affected repositories that were using the default
  `pull_request_target` policy before general availability". Neither page says what holds for a
  repository created after general availability; this one was created on 2026-10-01. So for this
  repository it is not known whether the rule blocks already or only evaluates.
- To keep the event, an Actions event policy has to allow it explicitly. A policy can be scoped
  to one workflow file.

What was observed here on 2026-10-02: `gh api repos/OWNER/REPO/actions/policies` answers
`{"total_count":0,"policies":[]}`. The default rule is not listed as a policy, so this endpoint
does not show whether it is in evaluate mode or enforced. No `pull_request_target` run had been
attempted by that day, because the default branch has no review workflow before the first merge.

**How to apply:**

- A Ready pull request whose `review` status stays pending is checked for this first: look for
  a `review` run of that head in the Actions list. How a blocked event shows there is not
  verified: neither page says it, and none was seen here by 2026-10-02. The working assumption
  is that no run appears at all, while a job skipped by its `if:` (a Draft, a pull request from
  outside) still shows a run ([[a-skipped-job-reports-success]]).
- The default rule names `pull_request_target` only. By that wording a `workflow_dispatch` run
  is not blocked: `gh workflow run review.yml -f pr=N` reviews one head on demand. Not tried
  here by 2026-10-02.
- A dispatch runs the workflow file and the tools of the ref it is started on, and `gh workflow
  run` takes the default branch only when `--ref` is left out. Start it on the default branch.
  The job's `if` refuses a dispatch on any other ref, and `ci-config` pins that expression; but
  the copy of the workflow on another branch can drop the `if`, and a dispatch on that branch
  runs the copy with this repository's token. That is HAZARD #11.
- The lasting fix is the owner's setting, in place since 2026-10-03: a repository Actions policy
  with `enforcement: active`, scoped by `conditions.workflow_path.include` to
  `.github/workflows/review.yml`, holding one rule of type `restrict_action_events` whose
  `parameters.allowed_events` lists `pull_request_target` and `workflow_dispatch`. Both events,
  because the rule is a closed list: it would block the on-demand review as well as the event it
  was made for if only one were named. A policy with an empty `rules` array allows nothing: on 2026-10-03 the empty policy was in place when a pull
  request was marked Ready, and the `pull_request_target` run started as before, so the empty
  policy neither blocked nor, as far as can be seen, replaced the default. The policy is read
  with `GET /repos/{owner}/{repo}/actions/policies/{id}` and replaced whole with `PUT` on the
  same path (`name` and `enforcement` are required). Nothing asserts the policy (HAZARD #10).
- Do not switch the review to `pull_request` to get around the block. That event runs the
  workflow file of the pull request itself, so a pull request could rewrite its own review.
