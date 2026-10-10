---
name: local-run-reads-the-working-tree
description: A local gates run reads the working tree, untracked files included, and CI reads the commit - a file that was never staged is green here and red there, and a file the gates create while they run exists only where they ran (2026-10-01, the first CI run was red on a bytecode directory no local run had ever made)
metadata:
  type: reference
---

What each run reads, as the tools stand on 2026-10-09:

- A local `py -3 tools/gates.py` reads the files on disk. The tree gate lists them with
  `git ls-files --cached --others --exclude-standard`: tracked files as they are in the working
  tree, plus every untracked file that is not ignored. The knowledge lint and the workflow lint
  read the directories directly.
- The `gates` job checks out one commit. It sees what was committed, and what the job itself
  wrote before the gate ran.

Seen on 2026-10-01, in the first `gates` run of pull request 8: the gate `tree` was red on
`tools/__pycache__/`, a directory of bytecode that Python had written while the earlier gates
imported their modules. The local runs had bytecode writing switched off (PYTHONDONTWRITEBYTECODE),
the runner's interpreter did not, so no local run had seen that directory. It is ignored in `.gitignore` since then,
and the proof replay writes none ([[stale-bytecode-hides-a-mutation]]).

**How to apply:** when a verdict differs between a local run and CI, compare what was read before
comparing the code. `judgment step`

- `git status --porcelain --untracked-files=all --ignored` prints every file that the local run
  may have read and the commit does not hold, or holds in another version; `tools/lint_knowledge.py`,
  `tools/lint_reference.py` and `tools/red_proof.py` walk their directories with `os.listdir` and
  `os.walk` (read on 2026-10-09), so they read ignored files too. A new fixture, a new knowledge entry or
  a new tool that was never staged passes here and is missing there: an INDEX link to a missing
  file, a tool without its red proof, a self-test that cannot find its payload.
- The reverse, red only in CI on a path nobody wrote: something the job created. Ignore it in
  `.gitignore` when it is a by-product, and find out why no local run made it.
- The pull-request gates read the forge, not the tree: they see the title, the description and
  the pushed commits, whatever the working tree holds.
