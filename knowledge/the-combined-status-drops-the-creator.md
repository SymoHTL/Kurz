---
name: the-combined-status-drops-the-creator
description: GitHub's combined status (GET repos/{owner}/{repo}/commits/{ref}/status) lists each status without its creator, while the list of statuses (GET .../commits/{ref}/statuses) names the creator and holds every status ever posted for the ref, newest first; a tool that pins a status to its poster reads the list and takes the newest of a context. The merge tool read the combined status and refused the first head with a real review status as absent while the forge called it mergeable (2026-10-07); its fixture of that endpoint had been captured before any status existed, an empty list no case could fail on
metadata:
  type: feedback
---

## What happened

On 2026-10-07 the review of pull request 22 completed in CI and posted the commit status
`review` = success on its head, the first real one in this repository. GitHub called the pull
request mergeable: the status satisfied the pinned check, which closed issue #5. The merge tool
refused the same head with `review=absent`; the head was merged with the fixed copy of the tool
on the branch that carried the fix, no waiver.

The tool read the statuses from the combined status,
`GET repos/{owner}/{repo}/commits/{ref}/status`, and kept only those whose `creator.login` is the
login the pinned app posts as. The combined status carries no `creator` at all: each entry has
`context`, `state`, `description`, `target_url`, `id`, `node_id`, `created_at`, `updated_at`,
`url` and an `avatar_url`, nothing more. So the filter dropped the one real status, and the
context read as absent.

The list of statuses, `GET repos/{owner}/{repo}/commits/{ref}/statuses`, carries `creator`
(`login`, `id`, `type`) on each entry. It is a different shape: GitHub's documentation of the
endpoint lists every status posted for the ref in reverse chronological order, the latest first,
not one per context (the captured list holds one entry, so the order was not seen here); so a
reader takes the newest of a context, or an older `error` from a run that later succeeded
outvotes the success.

## Why the self-test did not catch it

`tools/fixtures/status.json` was captured on 2026-10-01 from a head that had no status yet:
`"statuses": []`. Every case about statuses used a hand-built entry that carried a `creator`, and
the one case over the fixture read the gates check run, not a status. A payload that holds none of
the thing under test can fail no case about it.

## How to apply

- A status is pinned to its poster from the list of statuses, never from the combined status,
  and the newest status of a context from the pinned poster counts (`tools/merge_pr.py`,
  `head_states` and `context_states`). Gate: `self-tests`: the real status is read as success from
  the captured list, the same status from the captured combined status does not count, the tool
  asks the forge for the list, the newest status of a context counts, an older success does not
  outvote a newer error, and a context pinned to an app whose poster the tool does not know takes
  no status, with or without a creator; each case has its mutation in `tools/red_proofs.json`.
- A fixture captured before the platform had sent the thing under test is captured again once it
  has, and a case over a captured collection has a floor; `tools/fixtures/SOURCES.txt` says when
  and from which head each payload came. Gate: review rule "tools" (CLAUDE.md, Tests item 1).
- Issue #5 is closed by this: the ruleset accepted the status for the pinned app, seen once on
  2026-10-07 and re-checked by nothing. HAZARD #11 stays: any workflow run of this repository can
  post the status.
