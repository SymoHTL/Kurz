---
name: claude-cli-headless-answer
description: Shape of a headless Claude CLI answer as the reviewer uses it - an API error still says subtype "success"; trust is_error, the exit code and structured_output; the flags for a call with no tools
metadata:
  type: reference
---

Probed on 2026-10-01 with Claude Code CLI 2.1.283. The raw answers are kept as fixtures under
`tools/review/fixtures/`; the two answers of the pinned model were captured again on 2026-10-02,
when the pinned model changed.

The call the reviewer makes (`tools/review/review.py`, `command` and `call_model`): prompt on
stdin, working directory an empty temporary directory, an environment built by `model_env`, and

```
claude -p --model <exact id> --safe-mode --tools "" --strict-mcp-config
       --mcp-config '{"mcpServers":{}}' --no-session-persistence --disable-slash-commands
       --permission-prompts none --output-format json
       --system-prompt-file <file> --json-schema <schema>
```

`--safe-mode` switches customizations off: plugins, hooks, skills, MCP servers and `CLAUDE.md`
files, while the login still works. It does not isolate the call from the machine: model
selection and the environment of the calling process still apply
(`knowledge/a-headless-call-inherits-its-session.md`). `--tools ""` removes every tool. The
empty directory means there is no project to load.

What the answer looks like:

- One JSON object. On success `is_error` is false, `structured_output` holds the object the
  schema asked for, `result` holds the same as text, `total_cost_usd` and `modelUsage` say what it
  cost and which model answered.
- **An API error still carries `"subtype": "success"`.** With a model id that does not exist the
  CLI exited 1 and answered `is_error: true`, `terminal_reason: "api_error"`,
  `api_error_status: 404`, `structured_output` absent, and `subtype: "success"`. A reader of
  `subtype` takes the failure for a pass.
- `modelUsage` is keyed by the model that really answered. The reviewer refuses an answer whose
  key is not its pinned model, so a silent fallback cannot review in its place.

**How to apply:** decide on `is_error`, the exit code and the presence of `structured_output`,
never on `subtype`. A usage limit and a missing credential could not be provoked on purpose; the
unit suite edits the real error payload for those two cases and says so. When the CLI version in
`.github/workflows/review.yml` is bumped, probe again and replace the fixtures.
