---
name: the-forge-fills-ruleset-parameters
description: GitHub adds parameters with defaults of its own to a ruleset rule that was created without them (seen 2026-10-01 with require_extra_approval_for_unattributed_changes); the definition file names every parameter, and merge-checks is red on one it does not name
metadata:
  type: reference
---

Seen on 2026-10-01, when the ruleset was first created from `tools/ruleset.json`:

- The `pull_request` rule came back with two parameters the file had not sent:
  `required_reviewers: []` and `require_extra_approval_for_unattributed_changes: true`.
- The second one is a setting in public preview. GitHub's page "Available rules for rulesets"
  (read 2026-10-01) calls it "Require an additional approval for unattributed Copilot pull
  requests": when Copilot opens a pull request that is not attributed to a person, the rule asks
  for one more approval than the number configured. It is on by default, for new and for existing
  rulesets. The published REST description did not list the parameter on that day.
- Maintainers of other repositories report that it made pull requests opened by an app identity
  wait for an approval although zero approvals were required. In a repository with one person
  nobody could give that approval. That was not seen here: pull request 8, opened with the owner's
  login and holding a commit co-authored by Claude, asked for no review while it was a Draft.

What follows from it:

- A definition file that lists only what you chose cannot see what the forge chose.
  `tools/merge_pr.py --assert-settings` therefore fails on a parameter that the server carries
  and `tools/ruleset.json` does not name. The fix is a decision: read what the parameter does, pin
  its value in the file, apply it with `--apply-settings`.
- The gate `merge-checks` can turn red on a day when nothing changed in this repository. That is
  the gate working: a merge rule changed.
- If a pull request ever waits for an approval that nobody here can give, look at this parameter
  first. Switching it off is the owner's decision and goes into `tools/ruleset.json`; it is not a
  reason to merge over red.
