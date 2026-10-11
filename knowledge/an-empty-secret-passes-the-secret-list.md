---
name: an-empty-secret-passes-the-secret-list
description: A repository secret set to an empty value looks set; `gh secret list` shows the name and the date only, and the review run fails in seconds with `REVIEW DID NOT COMPLETE (credential)` (2026-10-10). Check the value's length before it goes into `gh secret set`
metadata:
  type: reference
---

On 2026-10-10 the owner rotated the reviewer's token with a shell command that piped the value into
`gh secret set`. The pipe carried nothing, the command succeeded, and `gh secret list` showed the
secret with a fresh date. The next review run ended after three seconds with
`REVIEW DID NOT COMPLETE (credential)`: the job's environment held the variable, empty. On that day
`gh secret list` showed the name and the update date and nothing else, so the listing could not tell
an empty secret from a set one.

What to do: read the value into a variable with a masked prompt, refuse to continue when its length
is zero, and only then pipe it into `gh secret set`. A `credential` ending of the review run points
at the credential and no further: the reviewer reports an empty value under that kind as it does a
wrong or expired one, so the ending cannot tell the two apart, and the length check is what rules an
empty value out. On 2026-10-10 run 38080825216 ended that way after three seconds with the empty value. After the
secret was set again no run started by itself, and a dispatch (`gh workflow run review.yml -f pr=N`)
started the next one.
