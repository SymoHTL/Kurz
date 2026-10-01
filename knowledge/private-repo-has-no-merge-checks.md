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
- Secret scanning with push protection is likewise on by default for a public repository (seen in
  `security_and_analysis` on 2026-10-01) and is a paid feature for a private one.
- The decision here was to make the repository public. The price is that everything pushed is
  published at once: see [[a-force-push-does-not-unpublish]].

The live rules are asserted on every gates run (`merge-checks`, `tools/merge_pr.py
--assert-settings`) against `tools/ruleset.json`, so a repository that silently lost its ruleset,
for example by being made private again, turns red.
