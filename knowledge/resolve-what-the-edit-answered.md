---
name: resolve-what-the-edit-answered
description: After the fix push, py -3 tools/pr_gates.py resolve --pr N prints which unresolved reviewer threads the head's edits answer (every file of the finding changed since the finding's commit, the reading the findings gate applies) and which stay open and why; --go resolves the former, one write a second. Two pull requests had this as a hand-written script (21 and 22, 2026-10-07) before the third need made it the tool
metadata:
  type: reference
---

A review round ends with threads to resolve. The rule (CLAUDE.md, "Git, pull requests &
merging") says a thread above low is answered by an edit of its file, and on tool or workflow
code also by a written reply; the gate `pr-findings` reads exactly that, after the fact.
Resolving by hand in the browser is one click per thread and no check; the GraphQL mutation by
hand was a script per pull request, twice, before the third-time rule made it the tool.

## How to apply

1. Push the fix commit. Then `py -3 tools/pr_gates.py resolve --pr <N>`: the plan. Each line is
   `would resolve <files> (<thread id>)` or `left open: <files>: <why>`, where why is "no finding
   of the reviewer" (a person's thread, or a reviewer post that names no file), "its commit is
   gone" (the finding's commit is not on the forge) or "the file did not change".
2. What is left open: a tool-code thread gets its reply and is resolved by hand, since the tool
   resolves edits only; a person's thread is theirs to resolve; a thread left open for want of an
   edit means the push missed a fix, which is the next round's one push.
3. `py -3 tools/pr_gates.py resolve --pr <N> --go` resolves the plan, a second between writes. An
   answer that does not say resolved is a refusal (exit 1): what was resolved before it stays so,
   and the plan run shows what is left.
4. The gate `pr-findings` ran at the push, before the threads were resolved; a description edit
   runs the gates again without a review, and the merge tool reads the threads again at the merge.
   The walk that needs no re-run: mark the pull request Draft before the push, resolve, then Ready,
   so the one review and the gates run on the head with its threads resolved.

Gate: `self-tests` (ten cases, the plan's six and the write's four, each named by a mutation in
`tools/red_proofs.json`). Which threads need a reply, and resolving a person's thread, are a
`judgment step`.
