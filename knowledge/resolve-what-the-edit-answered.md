---
name: resolve-what-the-edit-answered
description: After the fix push, py -3 tools/pr_gates.py resolve --pr N prints which unresolved reviewer threads the head's edits answer (every file of the finding changed since the finding's commit, the reading the findings gate applies) and which stay open and why; --go resolves the former, one write a second. Two pull requests had this as a hand-written script (21 and 22, 2026-10-07) before the third need made it the tool
metadata:
  type: reference
---

A review round ends with threads to resolve. The rule (CLAUDE.md, "Git, pull requests &
merging") says a thread above low is answered by an edit of its file, and on tool, workflow or
hook code also by a written reply; the gate `pr-findings` reads exactly that, after the fact.
Resolving by hand in the browser is one click per thread and no check; the GraphQL mutation by
hand was a script per pull request, twice, before the third-time rule made it the tool.

## How to apply

1. Mark the pull request Draft if it is Ready, so that the push starts no review, and push the
   fix commit.
2. `py -3 tools/pr_gates.py resolve --pr <N>`: the plan. Each line is
   `would resolve <files> (<thread id>)` or `left open: <files> (<thread id>): <why>`, where why
   is "no finding of the reviewer" (a person's thread), "the reviewer's post names no file", "its
   commit is gone" (the finding's commit is not on the forge), "the file did not change", "not
   every file changed: <files> did not" (a finding of several files), "a person wrote in it" (a
   question or a reply after the reviewer's post, whoever wrote it) or "the head is still the
   finding's commit": the pull request, read right after the push, still names the old head for
   a moment, so wait and run the plan again ([[the-pull-request-shows-the-old-head-after-a-push]]).
3. What is left open: a person's thread is theirs to resolve; a reviewer post that names no file
   has nothing to answer and is resolved by hand; a thread on tool, workflow or hook
   code (`tools/`, `.github/`, `.githooks/`) with "the file did not change" gets its reply and is
   resolved by hand, since the tool resolves edits only; a thread elsewhere with "the file did not
   change" means the push missed a fix, which is the next round's one push; of several files
   the ones that did not change each follow that rule, a reply for tool, workflow or hook code
   and an edit elsewhere; a thread in which a person wrote is read, and resolved by hand once
   the question is answered; "its commit is gone"
   cannot be proven answered by an edit: on tool, workflow or hook code a reply answers it,
   elsewhere it holds the merge until the owner decides.
4. `py -3 tools/pr_gates.py resolve --pr <N> --go` resolves the plan, a second between writes,
   and prints each thread once the forge said it is resolved. A failure of any kind stops the run
   (exit 1): what was printed stays resolved, the thread of the failed write may or may not be,
   and the plan run shows what is left.
5. Mark the pull request Ready: the review and a gates run start on the head, and this gates run
   reads the threads resolved; the gates run of the push had read them open (the gates workflow
   runs at a push, at Ready and at an edit of the description). The merge tool takes the newest
   gates run of the head, and reads the threads again itself; an edit of the description after
   the review is text the review never read (HAZARD #13), so it is no way to run the gates again.

Gate: `self-tests` (twenty-one cases: the plan's eleven, and the write's ten, among them the pacing,
the order of write and print, and what a failure of any kind, an interrupt too, says; each named by a mutation in
`tools/red_proofs.json`). Which threads need a reply, and resolving a person's thread, are a
`judgment step`.
