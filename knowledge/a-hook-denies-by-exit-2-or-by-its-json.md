---
name: a-hook-denies-by-exit-2-or-by-its-json
description: A PreToolUse hook blocks a tool call by exit 2, whatever it prints, or by a JSON decision "deny", which the harness reads on every exit code; every other end leaves the call to the normal permission flow, a crash, a timeout and a missing interpreter included (Claude Code docs, the hooks page, read 2026-10-08 and again 2026-10-09)
metadata:
  type: reference
---

What the Claude Code documentation says of a `PreToolUse` hook (the hooks page,
code.claude.com/docs/en/hooks, read 2026-10-08 and read again on 2026-10-09, after a review had
doubted it):

- The exit code does not act alone. In the page's own words, under "Exit code output": Claude
  Code "reads JSON output fields from stdout on every exit code, not just 0"; for the events with
  a decision model a parsed object that passes the schema takes effect beside the code.
- Exit 2 is a blocking error: it blocks the tool call whether or not the hook prints JSON, and
  even a JSON `permissionDecision` of `"allow"` cannot override it. (Before CLI 2.1.214 an exit 2
  together with JSON that failed the schema was a non-blocking error, and the call went ahead.)
- Any other exit code does not block on its own. With a parsed object that passes the schema the
  exit code is ignored and the object decides: a `hookSpecificOutput` with `permissionDecision`
  `"deny"` blocks the call. With no JSON, with plain text, or with JSON that fails the schema or
  cannot be parsed, the end is a non-blocking error: the call goes ahead. The page says it in a
  warning of its own: without valid JSON, exit 1 proceeds, and a hook that enforces a policy uses
  exit 2.
- A hook that cannot start (the shell's exit 127, an interpreter that is not there) is the same
  non-blocking error: the call goes ahead.
- A command hook that reaches its timeout is cancelled and its output discarded: the tool call
  continues through the normal permission flow. Nothing can be built on a hook that stalls.

The write-time hook of this repository (`tools/tree_gate.py --hook`, registered in
`.claude/settings.json`) uses the first way only. It prints no JSON, and the command in the
settings turns every end of the script but 0 into exit 2, so that a crash or a missing
interpreter denies the write instead of letting it through. The timeout stays open: HAZARD #4.

**How to apply:** a hook that must fail closed ends in exit 2 on every path but its pass. A hook
that decides by JSON lets the call through when its JSON is malformed or missing, so it fails
open on its own bugs, and nothing it prints helps once it exceeded its timeout. The review rule
"hooks" carries both shapes. `judgment step`
