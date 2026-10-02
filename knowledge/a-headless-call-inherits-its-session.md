---
name: a-headless-call-inherits-its-session
description: A Claude CLI call started from inside an agent session inherits that session's environment - its effort level and its session switches; with the inherited variable set, the --effort flag changed nothing (probed 2026-10-02); the reviewer hands its call an allow-list of variables and nothing else
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
- The probe, one small prompt, the same flags as the reviewer's call, one run per row:

  | What was set | Output tokens |
  |---|---|
  | nothing (the session's effort inherited: `max`) | 1318 |
  | `--effort low` on the command line, the inherited variable still `max` | 1269 |
  | `--effort low --setting-sources project`, the inherited variable still `max` | 1355 |
  | `CLAUDE_CODE_EFFORT_LEVEL=low` in the child's environment, no flag | 349 |

  With the variable set, the flag changed nothing; the variable itself decided. No row has the
  flag with the variable unset, so the table does not show what the flag does where nothing is
  inherited, and nothing here relies on the flag.
- The size of the effect depends on the prompt. On a full review batch the effort level made no
  difference that could be measured (`knowledge/what-a-review-pass-costs.md`). The trap is not
  the bill of that day. It is a reviewer whose configuration depends on who started it: the same
  script would have reviewed with one effort from a session and with another in CI.

What the reviewer does since 2026-10-02 (`model_env` in `tools/review/review.py`): the child's
environment is an allow-list, not a filter.

- Kept: what a process needs to start and to find its files (`ENV_KEEP`: the search path, the
  home, profile, application-data and temporary directories, the locale), and the login
  (`LOGIN`: `CLAUDE_CODE_OAUTH_TOKEN` and `CLAUDE_CONFIG_DIR`, the place the CLI keeps a login).
- Set: `CLAUDE_CODE_EFFORT_LEVEL`, to the effort pinned next to the model id.
- Everything else is absent: the session's variables, the forge tokens, an API key, a base URL,
  a thinking budget, proxy settings. A proxy that a runner needs would have to be added to the
  list on purpose.
- What still depends on the caller is the content of the configuration directory, when the
  caller's own is passed on. In CI there is none: the login is the secret.

Checked on 2026-10-02 on Windows: with that environment the CLI printed its version and answered
one real pass. On the Linux runner it is unverified until the first review runs in CI.

**How to apply:** a script that calls the CLI builds the child's environment from a list of what
it needs and pins what decides the answer. Do not rely on a command-line flag to override a
variable the caller exported, and do not read "safe mode" as isolation.
