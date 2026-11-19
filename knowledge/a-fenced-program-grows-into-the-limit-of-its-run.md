---
name: a-fenced-program-grows-into-the-limit-of-its-run
description: The fenced program of RegexTests' backtracking engine ran its 6422 rows in one program and made 818441 blocks, each of which the fence maps and gives back; it ran 4.7 s alone in the dev container, 0.13 s without the fence, and met the 30 s of a program's run in the gates job three times (2026-11-12 and 2026-11-14), each green when run again. The time grows with the blocks, and the lines the program builds made 1.8 percent of them; HoldToNet's fenced run takes its patterns in programs of at most 1500 rows since 2026-11-19
metadata:
  type: feedback
---

## What happened

`Toolchain.Run` runs a built program with a limit of 30 s, the bound of a hang (`Processes.Capture`). `RegexTests.HoldToNet` built
one program of every pattern and text a test holds to .NET's matches and ran it twice: with the runtime, and with the fence
(`ListTests.Fenced`), which gives every block pages of its own, mapped at its allocation with an inaccessible page after it, and
made inaccessible and given back at its free. Since 2026-11-19 it runs the fenced program in ranges of the patterns, and the
program without the fence stays one (the section "What to do").

`RegexTests.The_backtracking_engine_reads_backreferences_look_arounds_and_atomic_groups_as_net_does` failed three times in the
`gates` job with its fenced program ended at that limit, exit -1, on heads that could not reach it, each green when run again:
run 43260266243 (pull request 514, comment 6830144334), run 43624514303 (pull request 540, comment 6882196431) and run
43634665720 (pull request 544, comment 6883593522). The log of the last names the test at 32 s and shows the error cut at its
first 50 characters, so the line that says where the program stood is lost
([an-assert-of-a-tuple-cuts-the-text-it-shows.md](an-assert-of-a-tuple-cuts-the-text-it-shows.md)).

Measured on 2026-11-19 in the dev container, with the test's own programs, each run five times through `Processes.Capture` by
a scratch test; the blocks counted by a scratch edit of the standard library's `Allocate` and `Free`, each of which adds one to
its count, printed at the program's end:

| program | without the fence | fenced, alone | fenced, while the whole suite ran |
|---|---|---|---|
| the backtracking engine's, 6422 rows | 0.12 to 0.15 s | 4.6 to 4.8 s | 6.2 to 7.9 s |
| both engines' matches, 7200 rows | 0.16 to 0.18 s | 4.5 to 5.0 s | 6.7 to 7.8 s |

- The fenced backtracking program made 818441 blocks and freed all but 6. The same walk with no line of text built made 803672,
  so the lines it prints made 1.8 percent of them; the 338 calls of `Regex.New` made 25823. The searches and the matches they
  give make the rest.
- Its two halves ran 2.3 s and 2.4 s under the fence, with 399456 and 418991 blocks: the time grows with the blocks, about 5.8 µs
  a block there.
- In the `gates` job the same program needed more than 30 s, more than six times its time alone in the dev container (inferred:
  the runner's four cores under the suite's load; a test that passes logs no time there).

## What to do

- A test whose program `gave no answer within 30 s` on a head that cannot reach it: time the program alone, with the fence and
  without it, and count its blocks, before reading it as a hang. A fenced program far slower than its twin without the fence pays
  the fence for each block.
- Split such a program's work into programs that each stay well within the limit, the same rows in the same order, rather than
  give it a longer limit: the limit bounds a hang, as the compiler gate's own does
  ([a-suite-grows-into-its-timeout.md](a-suite-grows-into-its-timeout.md)). `HoldToNet` runs its fenced program in ranges of the
  patterns of at most `FencedRows` rows, 1500, and holds the ranges' lines to .NET's in the order of the patterns; the program
  without the fence stays one. The backtracking test's five ranges made at most 192222 blocks each (23.5 percent of the one
  program's) and ran 0.4 to 2.0 s while the whole suite ran; the matches test's five ranges ran 0.8 to 1.8 s there, and their
  blocks were not counted.
- The text a program builds is not the first cost to cut: count the blocks of the program without it.

Gate: `compiler-tests` for the ranges (the two tests through `HoldToNet`, which a range that passes over a pattern, ends one
pattern short or puts its lines before the earlier ranges' turns red, each seen red on 2026-11-19). That a program a test runs
stays well within its limit is a `judgment step`: nothing measures a program's time against it.
