---
name: a-judge-cannot-see-that-a-number-was-generated
description: The committed evidence guide reads the same whether its numbers were generated on the head's tree or typed or left from an older tree, so the acceptance judge cannot tell from the file; the judge of pull request 435 (2026-11-10) got the diff of tools/quality_evidence.py --print on the final head against the committed guide, which differed only in the digest and two counts read from the forge. That diff goes into the judge's evidence
metadata:
  type: feedback
---

On 2026-11-10 the acceptance judge of pull request 435 read a row "The numbers of the evidence
guide are generated" (pull request 435, comment 6757964786). The guide alone could not show it.
The evidence was `tools/quality_evidence.py --print`, which writes nothing, run on head 7777dfd1
and diffed against the committed `guides/quality-bar-evidence.md`. They differed in the `digest:`
line and in two counts the tool reads from the forge, the review findings rated medium (4747 to
4754) and low (4755 to 4761), which had moved while other pull requests were reviewed.

**Why:** the gate `knowledge` checks that the blocks are what their `digest:` line says; it does
not run the tool, so blocks generated on an older tree pass under their own digest. A judge reads
text and sees numbers. The forge's counts move between the commit and the reading, so a fresh run
is never byte for byte the committed file, and a judge given the file alone has nothing to
compare.

**How to apply:**

- Before the judge reads: run `tools/quality_evidence.py --print` on the final head, diff it
  against the committed guide, and put the diff into the judge's evidence. `judgment step`
- Only the `digest:` line, the `generated:` date on another day and the counts read from the forge
  may differ. A block read from the tree that differs means the guide was not generated on that
  tree: generate it, commit, and the judge reads again. `judgment step`
- It is the evidence side of [the judged tree is the committed tree](the-judged-tree-is-the-committed-tree.md).
