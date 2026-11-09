---
name: a-mask-cannot-tell-apart-the-values-it-hides
description: The case serve-keep-alive held that a handler's Date is kept, yet showed every Date by the length of its text, and the handler's `Sun, 06 Nov 1994 08:49:37 GMT` is 29 bytes like the server's own, so a server that wrote its Date in place of the handler's printed the same line (found by the review of pull request 301, 2026-11-05); a case that masks a changing value shows the value its rule is about as it is
metadata:
  type: reference
---

## What happened

The case `corpus/http/serve-keep-alive.kz` holds that the server keeps a `Date` the handler set
(W9). The server writes its own `Date` beside every other response, and that value changes every
second. So the case's `Wire` showed every `Date` by the length of its text, `Date: <29>`, to keep the
expectation the same from run to run.

The handler's `Date`, `Sun, 06 Nov 1994 08:49:37 GMT`, is 29 bytes too. A server that wrote its own
`Date` in place of the handler's printed the same `Date: <29>`, so the case could not fail on the
rule it named. The review of pull request 301 found it (2026-11-05).

Pull request 351 shows the handler's `Date` as it is and masks only another one. The mutation
`handler-date-replaced`, the server's `Date` written in place of the handler's, turned
`serve-keep-alive` red. Under the old mask, the two read the same.

## What to do

- A mask maps many values to one text. Before a case masks a value that changes, check that the
  value its rule is about cannot fall into the same mask, and show that value as it is.
- A mutation that puts the other value in its place shows that the case can fail.

Gate: `compiler-tests` runs `serve-keep-alive` with the handler's `Date` shown. A new mask that takes
in the value a case's rule is about is caught by no rule of the review: a `judgment step` for the
case's author.
