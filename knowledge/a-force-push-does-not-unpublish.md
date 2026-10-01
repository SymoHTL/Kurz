---
name: a-force-push-does-not-unpublish
description: After a history rewrite GitHub still serves the old commits by SHA and lists the force push with "Compare changes" in the Activity view; only a fresh repository (or GitHub Support) removes them
metadata:
  type: reference
---

When this repository was made public on 2026-10-01, its earlier history held material that the
owner wanted to keep private. Rewriting the history and force-pushing would not have been enough:

- GitHub's own guide on removing sensitive data says that after a force push the old commits
  remain reachable "directly via their SHA-1 hashes in cached views on GitHub" and through pull
  requests that reference them, until GitHub Support runs a garbage collection (read 2026-10-01).
- The repository's Activity view lists every force push and offers "Compare changes" for it, which
  shows what the push removed (GitHub docs, "Using the activity view", read 2026-10-01).

What was done instead: the old repository was renamed and stays private, a new public repository
was created under the old name, and only the cleaned history was pushed to it. Afterwards the API
answered "No commit found" for the old commit ids on the public repository.

**How to apply:**

- Treat every push to this repository as publication that cannot be withdrawn. That includes
  branches, commit messages and pull request text.
- The check therefore runs before the push: `.githooks/pre-push` runs the tree gate over every
  commit about to be published, messages included (enable it once per clone with
  `git config core.hooksPath .githooks`). It knows shapes (credentials, machine-bound strings,
  disallowed paths), not meaning.
- Content that only a reader can recognise, such as future plans, has no gate before the push.
  That is HAZARD #2. Do not write it into the working tree in the first place.
- If something was pushed that must not be public, do not reach for `--force`. Tell the owner: the
  options are GitHub Support or a fresh repository.
