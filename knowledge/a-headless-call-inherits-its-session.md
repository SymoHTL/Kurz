---
name: a-headless-call-inherits-its-session
description: A Claude CLI call started from inside an agent session inherits that session's environment - its effort level and its session switches; the --effort flag loses against the inherited variable; the reviewer builds the child's environment itself (probed 2026-10-02)
metadata:
  type: reference
---

Seen on 2026-10-01, when the first review was started from inside an agent session, and probed on
2026-10-02 with Claude Code CLI 2.1.283.

- A session exports its own configuration into every process it starts: among others
  `CLAUDE_CODE_EFFORT_LEVEL`, a session id, a marker that the process is a child session, and
  switches for features of the session. A script that passes its environment on hands all of it to
  the CLI it calls.
- `--safe-mode` does not stop that. Its help text says so: customizations are off, while auth and
  model selection "work normally".
- The probe, one small prompt, the same flags as the reviewer's call:

  | What was set | Output tokens |
  |---|---|
  | nothing (the session's effort inherited: `max`) | 1318 |
  | `--effort low` on the command line | 1269 |
  | `--effort low --setting-sources project` | 1355 |
  | `CLAUDE_CODE_EFFORT_LEVEL=low` in the child's environment | 349 |

  The flag loses against the inherited variable. Only the variable itself decided.
- The size of the effect depends on the prompt. On a full review batch the effort level made no
  difference that could be measured (`knowledge/what-a-review-pass-costs.md`). The trap is not
  the bill of that day. It is a reviewer whose configuration depends on who started it: the same
  script would have reviewed with one effort from a session and with another in CI.

What the reviewer does (`model_env` in `tools/review/review.py`):

- the child gets no variable whose name starts with `CLAUDE`, except the login
  (`CLAUDE_CODE_OAUTH_TOKEN`) and the place the CLI keeps it (`CLAUDE_CONFIG_DIR`);
- `CLAUDE_CODE_EFFORT_LEVEL` is then set to the effort pinned next to the model id;
- the forge tokens are dropped as before: the model needs none.

**How to apply:** a script that calls the CLI builds the child's environment and pins what decides
the answer. Do not rely on a command-line flag to override a variable the caller exported, and do
not read "safe mode" as isolation.
