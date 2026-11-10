---
name: a-line-that-starts-with-a-hash-ends-the-rule
description: the reference lint and its `--sync` read every line of a reference chapter that starts with `#` as a heading and end the rule's text there, while Markdown reads a heading only as `#` followed by a space, so a hard-wrapped line of a rule that begins with an issue number, `#411` or `#110`, ends the rule there; every `Case:` line after it is "a Case line outside a rule", the rule "has neither a case nor a `No case:` line", and `--sync` writes no sample under those lines (9 errors, twice in pull request 428, and 29 errors in C4, 2026-11-09); wrap so that no line of a rule starts with `#`
metadata:
  type: reference
---

## What happened

Twice in pull request 428, on 2026-11-09, a sentence of W11 in `reference/16-http.md` was wrapped
again after an edit, and the new wrap put an issue number first on a line: `#411` once, `#110` the
other time. `tools/lint_reference.py --sync`, then the lint, gave 9 errors each time, none of them
on that line. The same day a reflowed paragraph of C4 in `reference/10-control.md` put "#110," first
on a line, and the lint gave 29 errors. In each case a wrap that started the line with a word gave
0 errors.

## Why

The lint walks a chapter line by line and takes a line that starts with `#` for a heading (`lint`
and `sync` in `tools/lint_reference.py`): one that starts with `### ` and reads as a rule opens that
rule, and any other ends the rule it stood in. The `Case:` lines after it then stood outside any
rule: each was "a Case line outside a rule", the rule "has neither a case nor a `No case:` line",
and `--sync` wrote no sample under them. Markdown reads a heading only as one to six `#` followed
by a space or the end of the line, so the rendered page showed nothing wrong. The design record is
not read so: the lint splits it at its `## <n>. ` sections alone.

## How to apply

- A rule's text wrapped by hand has no line that starts with `#`: move the break so that the issue
  number stays inside a line, or write "issue 110" or "pull request 411". Gate: `reference` goes
  red on such a line, with errors that name the `Case:` lines and the rule, not the line itself.
- Where the lint names `Case:` lines outside a rule, or a rule with neither a case nor a
  `No case:` line, while the rule's heading stands, look between that heading and the first such
  `Case:` line for a line that starts with `#`: `grep -n '^#[0-9]' reference/*.md`.
  `judgment step`
