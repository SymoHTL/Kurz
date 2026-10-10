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
stdin, working directory an empty temporary directory, an environment that `model_env` builds
from an allow-list, and

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

- A wrong credential, provoked on 2026-10-02 by making the reviewer's own call with a login
  variable that holds no credential and an empty configuration directory: exit code 1,
  `is_error: true`, `api_error_status: 401`, `result` "Failed to authenticate. API Error: 401
  Invalid bearer token", `modelUsage` empty, and again `subtype: "success"`. The call is refused
  before any model runs, so it costs nothing. Kept as `cli-bad-credential.json`.

The fixtures, each with its source and date, are listed in `tools/fixtures/SOURCES.txt`.

**How to apply:** decide on `is_error`, the exit code and the presence of `structured_output`,
never on `subtype`. A usage limit cannot be provoked on purpose; the unit suite edits the real
error payload for that case and says so. A credential that is missing altogether is not an answer
of the CLI here: the reviewer refuses to start in CI without the login variable. When the CLI
version in `.github/workflows/review.yml` is bumped, or the pinned model (`MODEL` in
`tools/review/review.py`) changes, probe again and replace the fixtures.
