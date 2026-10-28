---
name: a-checkout-of-a-path-stages-it
description: `git checkout <ref> -- <path>` writes the file into the index as well as into the working tree, so the file is staged and a commit after it carries it, even one meant for other files; `git show <ref>:<path> > <path>` or `git restore --source=<ref> --worktree -- <path>` changes the working tree alone (seen 2026-10-21, probed with git 2.43.0 on 2026-10-28)
metadata:
  type: reference
---

On 2026-10-21 a session of the design round 20 took a file back to the text of the default branch
with `git checkout origin/main -- <file>`, and the file came out staged: a checkout with a path
writes the index and then the working tree, as `git help checkout` says of that form. A
`git commit` then takes the staged file with whatever else it was meant to take, and
`git commit -a` hides the difference.

Probed on 2026-10-28 with git 2.43.0, in a repository of one file with two commits:
`git checkout HEAD~1 -- f` left `git status --short` at `M  f`, the change in the index;
`git restore --source=HEAD~1 --worktree -- f` left it at ` M f`, the change in the working tree
only.

## How to apply

- To take another commit's text of a file into the working tree alone:
  `git show <ref>:<path> > <path>`, or `git restore --source=<ref> --worktree -- <path>`.
  `judgment step`
- After a `git checkout <ref> -- <path>`, `git restore --staged <path>` takes the file out of the
  index and leaves the working tree as it is; `git status --short` tells them apart, `M ` staged
  and ` M` not. `judgment step`
- Before a commit, `git diff --cached --stat` lists what the commit takes. `judgment step`
