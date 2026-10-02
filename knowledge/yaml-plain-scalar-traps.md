---
name: yaml-plain-scalar-traps
description: Three YAML traps that bit while writing the review rules and the workflow lint (2026-10-01) - a colon-space turns a bullet into a mapping, a leading quote ends the scalar early, PyYAML keeps the last of two duplicate keys without a word; read YAML through kit.load_yaml, which refuses the third
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
   `permissions:` silently replaced the first, and a second `rules:` in a section of the review
   rules would silently drop the first list: the reviewer would run with fewer rules. The parsed
   result cannot show it, but the loader can: PyYAML hands a mapping's key nodes to
   `construct_mapping` before it builds the dictionary.

Also in the same family: YAML 1.1 reads the bare key `on` as the boolean `true`, so a parsed
workflow has its triggers under `True`. `tools/lint_ci.py` reads both.

**Gates** (all through `self-tests`):

- Traps 1 and 2: `load_rules` in `tools/review/review.py` refuses a rules file in which a rule is
  not a non-empty string, and the reviewer's unit suite loads the real rules file.
- Trap 3, since 2026-10-02: `load_yaml` in `tools/kit.py` is a `SafeLoader` whose
  `construct_mapping` refuses a key that occurs twice in one mapping. The rules loader and the
  workflow lint both read YAML through it, each with a case (a repeated key in a section, a
  repeated permission) and its red proof.
