---
name: a-suite-grows-into-its-timeout
description: The compiler gate gave restore, build and tests 900 seconds, and the jobs that run it 25 minutes each; on 2026-11-17 the Windows job's run met the 900 twice with no test named, green when run again, and the gates job took 23 min 44 s of its 25. A timeout bounds a hang; the suite grows into it.
metadata:
  type: reference
---

## What happened

`tools/compiler_gate.py` ends `dotnet test` after `TIMEOUT` seconds, for restore, build and tests
together, and reports `dotnet test: no answer within N s` with no test named. It held 900, and each
of the three jobs that run the gate held 25 minutes.

- The Windows job `compiler-windows` ran its step in 8 to 15 minutes in the night of 2026-11-16
  to 17, while the same tests took about 5 minutes in the dev container on Linux. Two runs met the
  gate's limit: 43985873704 (pull request 561's head `70d53e1f`, which had passed in run
  43983648390) and 43999911148 (pull request 562's head `7b9ad8be`). Both were green when the failed
  job was run again, the first with its step at 864 s.
- The `gates` job, the required check, took 14 to 17 minutes on the same heads, but 23 min 44 s in
  run 43999911205: 641 s in the replay of the red proofs, 743 s in the compiler gate, the rest in the
  container and the other gates. A job cut off at its own limit ends with no table of the gates at
  all.

Each change that adds tests or red proofs moves every run closer to these limits, so the failure
comes back more often, on heads that have nothing to do with it, and on a slow runner first.

The limit is there to end a hang, not to measure speed. Its size has to leave room above the slowest
run seen, and the gate's own limit has to end before the limit of the job around it, or the job is
cut off and the gate prints nothing of what it saw.

## What to do

- A run that ends with "no answer within N s" and names no test: compare the step's time with the
  limit on earlier heads before reading it as a hang of the change. Two failures at the limit on heads
  that also passed are the suite's time, not a test.
- The fix is the budget, raised with room above the slowest run seen: the gate's `TIMEOUT`, and the
  `timeout-minutes` of a job that would cut the gate off. The self-test case "the budget of the tests
  and of the version, with what a job spends outside the gate, ends before the limit of every job
  that runs it" holds `TIMEOUT + VERSION_TIMEOUT` plus each job's share in `JOBS` within that job's
  `timeout-minutes`, for `compiler-windows.yml`, `compiler-arm.yml` and `gates.yml`.
- A share in `JOBS` is a measured time with room, not a bound anything enforces: the replay of the
  red proofs grows with the ledger. When a job's run nears its limit, measure the steps again (the
  `=== gate:` lines of the gates log carry their start times) and raise the share and the limit in a
  change of their own, with the runs named.

Gate: `self-tests` (the case above and its two red proofs in `tools/red_proofs.json`); that a share
still covers what its job spends outside the gate is a `judgment step`: the shares are read by hand
from the logs, and nothing measures them again.
