---
name: an-empty-secret-passes-the-secret-list
description: A repository secret set to an empty value looks set; `gh secret list` shows the name and the date only, and the review run fails in seconds with `REVIEW DID NOT COMPLETE (credential)` (2026-10-10). Check the value's length before it goes into `gh secret set`
metadata:
  type: reference
---

On 2026-10-10 the owner rotated the reviewer's token with a shell command that piped the value into
`gh secret set`. The pipe carried nothing, the command succeeded, and `gh secret list` showed the
secret with a fresh date. The next review run ended after three seconds with
`REVIEW DID NOT COMPLETE (credential)`: the job's environment held the variable, empty. Nothing in
the forge shows a secret's length, so the listing cannot tell an empty secret from a set one.

What to do: read the value into a variable with a masked prompt, refuse to continue when its length
is zero, and only then pipe it into `gh secret set`. A `credential` ending of the review run is the
sign; the reviewer reports an empty value under that kind, as it does a wrong one. After the secret
is set again, start the run by hand (`gh workflow run review.yml -f pr=N`): the failed run is not
re-run by itself.
