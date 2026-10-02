---
name: quality-bar-evidence
description: The numbers behind the quality bar of this repository - gates, self-test cases and replayed red proofs per tool, the store, the design record and the forge; every number is generated and expires
generated: 2026-10-01
ttl_days: 60
metadata:
  type: reference
---

# Quality bar: evidence

Every number on this page sits in a generated block. `tools/quality_evidence.py` recomputes the
blocks from the tree, from git and from the forge and stamps the date above; the knowledge lint
counts the days down and fails once they ran out, so the next change in the repository has to
regenerate the page. The prose between the blocks carries no measurement. What the bar consists
of, and why, is in [quality-bar.md](quality-bar.md).

## The gates

One runner, `tools/gates.py`, runs these in CI and locally. A gate that could not run is not a
pass.

<!-- generated:gates -->
<!-- /generated:gates -->

## Self-tests and red proofs

Each tool that makes a decision carries its cases. Each recorded mutation is applied again on
every run and has to turn its case red; a mutation that stops doing so fails the build.

<!-- generated:self-tests -->
<!-- /generated:self-tests -->

## The store and the review rules

<!-- generated:store -->
<!-- /generated:store -->

## The design record and the reference

A statement marked *(assumed)* was proposed and not objected to; it is not a decision yet. In the
reference a `proposed` rule is what a case needed and the record does not say, and an `open` rule
is a fork nobody chose: neither is a decision. The cases are counted here, not run: no compiler
exists.

<!-- generated:design -->
<!-- /generated:design -->

## The forge

Findings are counted from the markers the reviewer leaves in its own threads. A merge over red is
counted from the record that the merge tool posts.

<!-- generated:forge -->
<!-- /generated:forge -->
