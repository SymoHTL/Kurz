---
name: a-force-push-does-not-unpublish
description: After a history rewrite GitHub still serves the old commits by SHA and lists the force push with "Compare changes" in the Activity view; only a fresh repository (or GitHub Support) removes them, and a clone made before the switch can publish the old history again with one push
metadata:
  type: reference
---

When this repository was to go public on 2026-10-01, its earlier history held material that the
owner wanted to keep private. Rewriting the history and force-pushing would not have been enough:

- GitHub's own guide on removing sensitive data says that after a force push the old commits
  remain reachable "directly via their SHA-1 hashes in cached views on GitHub" and through pull
  requests that reference them, until GitHub Support runs a garbage collection (read 2026-10-01).
- The repository's Activity view lists every force push and offers "Compare changes" for it, which
  shows what the push removed (GitHub docs, "Using the activity view", read 2026-10-01).

What was done instead, on 2026-10-01: the old repository was renamed and kept private, a new
public repository was created under the old name, and only the cleaned history was pushed to it.
Checked the same day with `gh api repos/<owner>/<name>/commits/<old sha>` for commit ids of the
old history: the public repository answered "No commit found". The renamed private repository
was deleted on 2026-10-02.

Taking the old name has a price. GitHub's guide on renaming a repository warns against reusing
the original name: once a new repository has it, the name no longer redirects to the renamed one
(read 2026-10-02). A clone or worktree made before the switch still holds the old history, and
since the switch its remote URL names the public repository. A push of any branch from it publishes that
history. The ruleset holds only `main`, and the old history has no pre-push hook.

**How to apply:**

- Before any work after such a switch: delete every clone and worktree made before it, or point
  its remote somewhere else.
- Treat every push to this repository as publication that cannot be withdrawn. A push publishes
  the branch, every commit's message, and the name and e-mail address of every commit's author
  and committer.
- For what a push publishes, the check runs before the push: `.githooks/pre-push` runs the tree
  gate over every commit about to be published, its message included (enable it once per clone
  with `git config core.hooksPath .githooks`; where it is off, HAZARD #4). It knows shapes
  (credentials, machine-bound strings, workflow-skip literals, disallowed paths), not meaning,
  and it does not read the author or the committer: HAZARD #12. GitHub's account settings narrow
  it but do not close it (GitHub docs "Blocking command line pushes that expose your personal
  email address", read 2026-10-03): "keep my email addresses private" changes only the address
  GitHub itself writes into web commits, and "block command line pushes that expose my email"
  rejects a push whose head commit's author address is one of the account's own; the committer
  field, earlier commits of the push, names, and any address not on the account still go out.
- The title, the description and every comment of a pull request or an issue are public the
  moment they are submitted, and no check runs before that. `pr-title` reads the title, the
  description and the commit messages afterwards. Write them as carefully as a file.
- Content that only a reader can recognise, such as future plans, has no gate before the push.
  That is HAZARD #2. Do not write it into the working tree in the first place.
- If something was pushed that must not be public, do not reach for `--force`. Tell the owner: the
  options are GitHub Support or a fresh repository.
