---
name: a-hook-denies-by-exit-2-or-by-its-json
description: A PreToolUse hook blocks a tool call by exit 2, whatever it prints, or by a JSON decision "deny" on any other exit; every other end lets the call through, a crash and a missing interpreter included (Claude Code docs, the hooks page, read 2026-10-08)
metadata:
  type: reference
---

What the Claude Code documentation says of a `PreToolUse` hook (the hooks page,
code.claude.com/docs/en/hooks, read 2026-10-08):

- Exit 2 is a blocking error: it blocks the tool call whether or not the hook prints JSON, and
  even a JSON `permissionDecision` of `"allow"` cannot override it.
- Any other exit code does not block on its own. When the hook prints a JSON object that passes
  the schema, the exit code is ignored and the object decides: a `hookSpecificOutput` with
  `permissionDecision` `"deny"` blocks the call.
- A JSON object that fails the schema is a non-blocking error on any exit but 2: the call goes
  ahead.

The write-time hook of this repository (`tools/tree_gate.py --hook`, registered in
`.claude/settings.json`) uses the first way only. It prints no JSON, and the command in the
settings turns every end of the script but 0 into exit 2, so that a crash or a missing
interpreter denies the write instead of letting it through.

**How to apply:** a hook that must fail closed ends in exit 2 on every path but its pass. A hook
that decides by JSON lets the call through when its JSON is malformed or missing, so it fails
open on its own bugs. The review rule "hooks" carries both shapes. `judgment step`
