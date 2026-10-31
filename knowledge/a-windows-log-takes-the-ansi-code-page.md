---
name: a-windows-log-takes-the-ansi-code-page
description: Postmortem of 2026-10-30 - on the Windows runner the compiler gate found failed tests and died printing their output, on xunit's arrow under a difference, which the ANSI code page cp1252 lacks, so the log named no failing test; a tool that prints text it did not write sets its stdout to UTF-8 with backslashreplace first
metadata:
  type: feedback
---

On 2026-10-30 run 41057612007 of `compiler-windows` was red on the head of pull request 92 that
added a test before the change it tests. The log ended in a traceback, not in the gate's verdict:

`UnicodeEncodeError: 'charmap' codec can't encode character '↓' in position 982: character maps to <undefined>`

raised by `print(output[-6000:])` in `tools/compiler_gate.py`, the line that prints the output of a
red test run. The gate had judged the run red and was about to say why. The output held xunit's
message for two collections that differ, which marks the place of the difference with an arrow,
U+2193. The job runs the gate with its output redirected to the runner's log, a pipe, and Python on
Windows (3.12.10 in that run, the image's default, as the path of its traceback names) writes a pipe or a file in the system locale's encoding, the ANSI code page, not the
console's: "Non-character devices such as disk files and pipes use the system locale encoding
(i.e. the ANSI codepage)" (the Python 3.12 documentation of `sys.stdout`, read on 2026-10-30). On
the runner that is cp1252, which has no arrow. So `chcp 65001`, which sets the console's code page,
changes nothing here; `sys.stdout.reconfigure`, `PYTHONIOENCODING` and `PYTHONUTF8` do. The run
stayed red, since the traceback ends the process with 1, but the log named no failing test and
printed no line of the gate's own: the red proof of that test could not show which test was red.

The same trap ended a paid review on 2026-10-03
([a-paid-result-is-printed-before-it-is-posted.md](a-paid-result-is-printed-before-it-is-posted.md)),
and the reviewer was fixed then; the compiler gate prints text it did not write too, and was not.

**Why:** a print is taken as the one step that cannot fail, and into a log on Windows it fails on
the first character the ANSI code page lacks. The gate's own lines hold no such character; the
text that can hold one is what it passes on, written by the tests, the compiler or the forge.

**How to apply:** a tool that prints text another program wrote sets its stdout to UTF-8 with
`backslashreplace` before its first print (`sys.stdout.reconfigure`), and a self-test runs the
tool's own entry point with a cp1252 stdout and such a character in what it passes on. Done for
`tools/review/review.py` and `tools/compiler_gate.py` (`readable_log`, called first in `main`);
Gate: `self-tests`, the cases "a red run's output that quotes a character outside the log's code
page is printed" and "the gate's own run prints a red run's output into a cp1252 log, and names its
failing tests after it", and their red proofs, one of which removes the call in `main`.
