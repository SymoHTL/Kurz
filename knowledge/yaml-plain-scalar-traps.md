---
name: yaml-plain-scalar-traps
description: Three YAML traps that bit while writing the review rules and the workflow lint (2026-10-01) - a colon-space turns a bullet into a mapping, a leading quote ends the scalar early, PyYAML keeps the last of two duplicate keys without a word
metadata:
  type: reference
---

All three were hit on 2026-10-01 while writing `.review/review-rules.yaml` and
`tools/lint_ci.py`.

1. **A colon followed by a space turns a list item into a mapping.** The bullet
   `- A cost assumed away: a feature described ...` loaded as `{"A cost assumed away": "a feature
   described ..."}`, not as text. Nothing failed at load time; the reviewer would have been handed
   a dictionary where a rule was expected. Write a dash instead of the colon, or quote the whole
   bullet, or use a block scalar (`>-`).
2. **A bullet that starts with a quote is a quoted scalar.** `- "As in C#" used for ...` ends the
   scalar at the second quote and fails to parse on what follows. Quote the whole bullet in single
   quotes.
3. **PyYAML accepts duplicate keys and keeps the last one.** A second `contents:` under
   `permissions:` silently replaced the first. A lint that wants to catch "the same key twice"
   has to look at the text, not at the parsed result.

Also in the same family: YAML 1.1 reads the bare key `on` as the boolean `true`, so a parsed
workflow has its triggers under `True`. `tools/lint_ci.py` reads both.

**Gates:** `load_rules` in `tools/review/review.py` refuses a rules file in which a rule is not a
non-empty string, and the reviewer's unit suite loads the real rules file, so trap 1 and trap 2
turn `self-tests` red. Trap 3 has no gate: the workflow lint compares parsed values.
