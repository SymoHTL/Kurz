---
name: a-ruleset-write-the-forge-drops-merges-nothing
description: Twice on 2026-11-05 the forge answered the merge tool's write that switches the ruleset off for a merge over red with a body nobody could read ("unexpected end of JSON input"); the tool sent no merge call, its restores failed the same way, the read-back said active and the ruleset's update time had not moved, so nothing was off and nothing merged (pull request 328); the forge's status page said all was well; a third call about fifteen minutes later merged; read the ruleset and the pull request back, then wait before the next call instead of calling again at once
metadata:
  type: reference
---

## What happened

On 2026-11-05 the merge of pull request 328 over red, at the head 4fc4e8c, took three calls of
`tools/merge_pr.py`.

1. **The first two calls.**
   - The forge answered the `PUT` that switches the ruleset off with a body that could not be
     parsed, `unexpected end of JSON input`.
   - The tool printed `switching the ruleset off failed` and sent no merge call. Its restore
     attempts failed too.
   - The read-back of the ruleset said `active`, the pull request was read back `not merged`, and
     the call ended with exit 1.
   - The ruleset's `updated_at` had not moved since 22:49 UTC, before the two calls, so the
     switch-off had never landed.
2. **Around the same time.** Another call to the forge, resolving a review thread through
   GraphQL, answered "Something went wrong". The forge's status page said all systems were
   operational.
3. **The third call.** About fifteen minutes later, the tool merged with no change.

The note of the merge is issue 39, comment 6672343829.

## What the tool does

A switch-off that fails sends no merge call. The tool then reads the pull request and the ruleset
back and prints both. A ruleset read back as anything but `active` is exit 3 (CLAUDE.md, "Merging
over a red or missing check"). Gate: `self-tests` (the merge tool's cases "flip: a ruleset that
cannot be switched off merges nothing" and "flip: a refused switch-off is named as that, not as a
failed merge").

## How to apply

- **On exit 1 with `switching the ruleset off failed` and `gate read back: active`:** nothing was
  switched off and nothing merged. Read the ruleset once more, to be sure, with
  `gh api repos/<owner>/<name>/rulesets --jq '.[] | "\(.id) \(.enforcement) \(.updated_at)"'`.
  `judgment step`
- **Wait before the next call.** The second call, made soon after the first, met the same answer. Give the forge about
  fifteen minutes, then run the tool once, alone, so that its full output is read. `judgment step`
- **Hold every other merge meanwhile.** Each merge over red writes the same ruleset.
  `judgment step`
