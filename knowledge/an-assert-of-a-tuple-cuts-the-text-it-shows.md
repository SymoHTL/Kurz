---
name: an-assert-of-a-tuple-cuts-the-text-it-shows
description: xunit's message for a failed `Assert.Equal` of two tuples shows each string member cut after 50 characters, at `···`, so a test that compares a program's exit code, output and error as one tuple loses, when it goes red, the error text that says why; a red run of a server test in CI lost so the program's error after its limit of 60 seconds (2026-11-06); a test that compares what a program gave asserts with a message that carries each text whole, as `CaptureTests` (pull request 356) and `HttpServerTests` (pull request 365) do
metadata:
  type: reference
---

## What happened

`HttpServerTests.A_chunked_file_ends_its_body_in_the_write_of_its_last_piece` compared what its program gave as one tuple,
`Assert.Equal((0, "", "served\n", 100, ""), (code, error, output, whole, failed))`. It went red once in CI, on pull request 322
(run 42202379628 of `gates`, 2026-11-06), and the log held:

```
Expected: Tuple (0, "", "served\n", 100, "")
Actual:   Tuple (-1, "\n`<the build directory>/http-server-coded-file/p"···, "", 0, "after 0 responses: Unable to read data from the tr"···)
```

(The build directory's path is left out here; the log held it.)

The program's error text, which says why the run ended at its limit, and the client's error were both cut at `···`. xunit's
display of a value cuts a string to its first 50 characters. The red was not seen again, so its cause stayed undiagnosed. A test
of the same file lost the program's output another way: the client's `IOException` ended it before it read the program's result
(run 42202044451, attempt 1, on pull request 317).

A collection is cut too: its message shows the items around the first difference and `···` for the rest, as in
`[···, "2 /b", "1 /a", "1 /c"]`.

## What to do

- A test that compares a program's exit code, output or error asserts with `Assert.True(condition, message)`, and the message
  carries each of them whole, with the time since the run started: `output [...] error [...] after ...`. The condition is the
  same comparison as before, the tuple compared with `==`, so the test holds the same values.
- A client's `IOException` in such a test is caught and kept with the time it came, so that the test still awaits the program and
  reaches that assert.
- `CaptureTests.A_child_that_holds_the_output_after_its_program_ended_ends_the_run_at_its_limit` (pull request 356) and the two
  tests of `HttpServerTests` that pull request 365 changed have this shape. Many tests of `compiler/` still compare a program's
  output in a tuple. Such a tuple loses nothing while its test passes, and a change to that test for another reason is the place
  to give it the message.

That a new test asserts so is a `judgment step` for its author: no gate can tell which strings a test compares can grow long.
The review of pull request 365 found the lesson.
