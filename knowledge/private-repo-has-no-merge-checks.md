---
name: private-repo-has-no-merge-checks
description: On a free GitHub account, rulesets and branch protection are refused for a private repository (HTTP 403, 2026-10-01); server-side merge checks need a public repository or a paid plan
metadata:
  type: reference
---

Checked on 2026-10-01 against this repository while it was still private, on a personal account
on the free plan:

- `GET /repos/{owner}/{repo}/rulesets` and `GET /repos/{owner}/{repo}/branches/main/protection`
  both answered `403` with the message "Upgrade to GitHub Pro or make this repository public to
  enable this feature."
- After the repository became public the same ruleset call answered `[]`, and a ruleset could be
  created.

What follows from it:

- "All threads resolved", "required checks must pass" and "no direct push to the default branch"
  cannot be enforced by the server on a private repository of a free account. A merge tool and an
  instruction are all that is left, and neither stops a person with a merge button.
- Secret scanning with push protection was enabled here (`security_and_analysis`, read on
  2026-10-01 and again on 2026-10-02 with `gh api repos/<owner>/<name>`). No gate asserts that it
  stays on: HAZARD #25.
- The decision here was to make the repository public. The price is that everything pushed is
  published at once: see [[a-force-push-does-not-unpublish]].

The live rules are asserted on every gates run (`merge-checks`, `tools/merge_pr.py
--assert-settings`) against `tools/ruleset.json`. What that run does when the rules cannot be
read, as the code stands on 2026-10-02:

- A read of the rules that the forge refuses, such as the 403 above, ends the tool with
  `REFUSED: could not check` and exit 1. The gate is FAIL, and the `gates` job is red.
- A ruleset list that is empty, or that holds no ruleset of the expected name, is an error
  (`0 rulesets are named ...`): FAIL as well.
- Only two fields that the job's token cannot read while everything else answered, the
  auto-merge setting and the bypass list, become `NOT CHECKED` notes. The gate then ends PARTLY,
  which the runner accepts for this one gate (HAZARD #7).

So a repository that lost its ruleset, for example by being made private again, turns red; it
does not end PARTLY.
