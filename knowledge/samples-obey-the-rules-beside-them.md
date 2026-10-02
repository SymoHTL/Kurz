---
name: samples-obey-the-rules-beside-them
description: A code sample in the design record broke the very rule it illustrated (2026-10-01); nothing executes a sample there, so read every sample against each rule stated in its section before it goes in
metadata:
  type: feedback
---

On 2026-10-01 the memory section of `kurz-design.md` stated the cycle rule ("a class may not reach
itself through strong fields when one field on that path is `mut`") and, a few lines further down,
showed a `Node` class with a strong `mut` list of child nodes and a `weak` parent. That class
reaches itself through a strong `mut` field, so the rule rejects its own example. It was found
only when the owner chose the strict form of the rule and the example was read against it. The
sample was replaced the same day by the flat-owner pattern (one owner holds the objects, the
edges are `weak` or indices); the section held that pattern when this entry was checked on
2026-10-02.

**Why:** a sample is written to show the idea and is never run. In a design document nothing
compiles, so a sample that contradicts its rule passes every check there is, and a reader takes
the sample as the truth because it is concrete.

**How to apply:**

- Read every sample against each rule stated in the same section before it goes in: mutability,
  the cycle rule, the null rule, honest signatures, visibility.
- In the reference, every Kurz sample is a corpus case, shown under a `Case:` line.
  `tools/lint_reference.py` (gate `reference`) refuses any other sample and any sample that
  differs from its file. Nothing runs a case, so its expectation is read against the rules it
  names in the same way (HAZARD #1).
- In `kurz-design.md` samples are illustrative and unchecked. That is HAZARD #1: nothing executes
  them. The review rule "design record" carries the defect shape, but the reviewer is given one
  batch of the diff, so it can judge a sample only against rules that the same batch shows.
