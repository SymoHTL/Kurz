---
name: an-upper-bound-of-wall-clock-time-belongs-in-a-test
description: The cases serve-timeouts and serve-keep-alive expected `waited >= 100ms && waited < 2s` for each bound of the server's time; the review of pull request 301 read the upper bound as an expectation that rests on timing, which no rule fixes (2026-11-05); a case keeps the lower bound, which the rule fixes, and the upper bound of the wall's time goes into a test of the suite with a wide window
metadata:
  type: reference
---

## What happened

The cases `corpus/http/serve-timeouts.kz` and `corpus/http/serve-keep-alive.kz` measured each wait of
the server's bounds of time (W10) by `instant` and printed `waited >= 100ms && waited < 2s`, expecting
`true`. The review of pull request 301 read the upper bound as an expectation that depends on
timing (2026-11-05, on `serve-timeouts` and on `serve-keep-alive`). The review rule "corpus" names that
shape: a case whose expectation depends on something no rule fixes.

The bound fixes that a wait lasts at least its length. Nothing fixes how soon a loaded machine runs
the server after the bound has passed. `RunTests` runs every case twice, the second time under the
fenced allocator. On a loaded runner the upper bound can turn
the case red with no defect in W10, and the case's printed `false` says nothing about how long the
wait took.

Pull request 351 kept the lower bound in the cases. It moved the upper bound into
`HttpServerTests.A_bound_of_time_ends_its_connection_within_its_length`. There each bound is 300 ms,
each row must end within 250 ms to 2.5 s, and a failure gives the run's exit code and texts whole
(`Ran`).

## What to do

- A case prints what a rule fixes. For a bound of time, that is the lower bound: the wait lasted at
  least its length.
- An upper bound of the wall's time goes into a test of the suite. The test names its window, makes
  the window wide beside the bound under test, and gives what the run printed when it fails.

Gate: review rule "corpus" for a case whose expectation rests on timing. The window of the test is a
`judgment step`.
