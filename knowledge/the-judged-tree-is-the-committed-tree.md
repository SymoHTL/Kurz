---
name: the-judged-tree-is-the-committed-tree
description: The acceptance judge's verdict covers one tree; a commit after the judged reading made a pull request's verdict cover a tree that was not its head (2026-10-10, eight high findings), so the judge reads the tree that becomes the head, nothing is committed after that reading, and the acceptance text names that commit
metadata:
  type: reference
---

On 2026-10-10 the acceptance judge read a pull request's tree and passed it; a commit after that
reading wrote one sentence into the design record, and the pull request's acceptance section named the
judged commit, not the head. The review rated the verdict eight times high: a verdict for a tree that
is not the head says nothing about the head, whatever the size of the difference.

What to do: the judge reads the tree that the head commit will hold, the commit is made after its
last reading, and nothing is committed after it; a fix the judge asks for goes into the tree before the
commit, and the judge reads again. The acceptance text names the head commit and says that the
judged tree is the committed one. A verdict in the description is text the review reads as a claim of
approval: the judge's table goes into a comment on the pull request, and the description says what
was judged and where the table is, with no verdict word of its own. The owner chose the comment on
2026-10-11, against changing the review rule, and the rule stands in `CLAUDE.md` under "Git, pull
requests & merging", with the review rule "every pull request" as its gate.
