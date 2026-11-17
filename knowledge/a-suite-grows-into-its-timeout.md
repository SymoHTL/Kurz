---
name: a-suite-grows-into-its-timeout
description: The compiler gate gave restore, build and tests 900 seconds; the Windows job's run grew to 864 s, and on 2026-11-17 two runs met the limit with no test named and were green when run again. A timeout bounds a hang; the suite grows into it.
metadata:
  type: reference
---

## What happened

`tools/compiler_gate.py` ends `dotnet test` after `TIMEOUT` seconds, for restore, build and tests
together, and reports `dotnet test: no answer within N s` with no test named. It held 900. The Windows
job `compiler-windows` ran its step in 8 to 15 minutes in the night of 2026-11-16 to 17, while the
same tests took about 5 minutes in the dev container on Linux. Two runs met the limit: 43985873704
(pull request 561's head `70d53e1f`, which had passed in run 43983648390) and 43999911148 (pull
request 562's head `7b9ad8be`). Both were green when the failed job was run again, the first with its
step at 864 s. Each change that adds tests moves the run closer to the limit, so the failure comes back
more often on every head, unrelated to the head.

The limit is there to end a hang, not to measure speed. Its size has to leave room above the slowest
runner's time, and the gate's own limit has to end before the job's `timeout-minutes`, or the job is
cut off and the gate prints nothing of what it saw.

## What to do

- A run that ends with "no answer within N s" and names no test: compare the step's time with the
  limit on earlier heads before reading it as a hang of the change. Two failures at the limit on heads
  that also passed are the suite's time, not a test.
- The fix is the gate's budget, raised with room above the slowest run seen. The self-test case "the
  budget of the tests and of the version ends before the limit of every job that runs the gate alone,
  less its setup" holds `TIMEOUT + VERSION_TIMEOUT + SETUP` within the `timeout-minutes` of
  `compiler-windows.yml` and `compiler-arm.yml`, so that a raise past a job's limit is red.
- When the slowest run nears the budget again, raise it again in a change of its own, with the runs
  that met it named, and the jobs' `timeout-minutes` with it where the case says so.

Gate: `self-tests` (the case above and its red proof in `tools/red_proofs.json`).
