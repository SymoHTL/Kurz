---
name: a-quoted-marker-counts-as-one
description: The evidence generator counts every occurrence of the text `*(assumed` in the design record, so a marker quoted in prose counts as a statement; on 2026-10-10 the generated page said one more than the judge counted by hand, until the sentence of section 14 that quoted the marker was reworded to describe it
metadata:
  type: reference
---

`tools/quality_evidence.py` counts the record's assumed statements as every match of `\*\(assumed`
in `kurz-design.md`. The count is of text, not of statements: a marker quoted in prose, a marker in
a code span and two markers in one sentence each count as one. On 2026-10-10 the paragraph of
section 14 that tells how the reference's forks are handled quoted the marker's text to say how four
readings were recorded; the generated page said 74 where the acceptance judge counted 73 statements,
and its row failed against the pull request's description until the sentence was reworded to
describe the marker instead of quoting it.

What to do: describe a marker in prose ("recorded as assumed") instead of quoting its text, or
expect the quoted one in the count. The page's prose names the counting rule
(`guides/quality-bar-evidence.md`, "The design record"), and the row's label says occurrences.
