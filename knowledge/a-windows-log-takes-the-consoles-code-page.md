---
name: a-windows-log-takes-the-consoles-code-page
description: Postmortem of 2026-10-30 - on the Windows runner the compiler gate found failed tests and died printing their output, on xunit's arrow under a difference, which cp1252 lacks, so the log named no failing test; a tool that prints text it did not write sets its stdout to UTF-8 with backslashreplace first
metadata:
  type: feedback
---

On 2026-10-30 run 41057612007 of `compiler-windows` was red on the head of pull request 92 that
added a test before the change it tests. The log ended in a traceback, not in the gate's verdict:

`UnicodeEncodeError: 'charmap' codec can't encode character '↓' in position 982: character maps to <undefined>`

raised by `print(output[-6000:])` in `tools/compiler_gate.py`, the line that prints the output of a
red test run. The gate had judged the run red and was about to say why. The output held xunit's
message for two collections that differ, which marks the place of the difference with an arrow,
U+2193; the job runs the gate with its output redirected to the runner's log, and on Windows a
redirected stdout takes the console's code page, cp1252, which has no arrow. The run stayed red,
since the traceback ends the process with 1, but the log named no failing test and printed no
line of the gate's own: the red proof of that test could not show which test was red.

The same trap ended a paid review on 2026-10-03
([a-paid-result-is-printed-before-it-is-posted.md](a-paid-result-is-printed-before-it-is-posted.md)),
and the reviewer was fixed then; the compiler gate prints text it did not write too, and was not.

**Why:** a print is taken as the one step that cannot fail, and on Linux and in an interactive
console it does not; in a log on Windows it fails on the first character the code page lacks, and
the text a gate prints there is written by the tests, the compiler or the forge, never by the gate.

**How to apply:** a tool that prints text another program wrote sets its stdout to UTF-8 with
`backslashreplace` before its first print (`sys.stdout.reconfigure`), and a self-test prints such a
character into a cp1252 stdout. Done for `tools/review/review.py` and `tools/compiler_gate.py`
(`readable_log`); Gate: `self-tests`, the case "a red run's output that quotes a character outside
the log's code page is printed" and its red proof.
