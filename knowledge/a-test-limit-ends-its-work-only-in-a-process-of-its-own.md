---
name: a-test-limit-ends-its-work-only-in-a-process-of-its-own
description: A test whose limit is `Task.Run(...).WaitAsync(...)` stops waiting at its limit and leaves the work running in the test host; the test of a chain of copies that branches (T33) had that shape in pull request 307, and pull request 342 moved its check into a process of its own, `kurz exec` through `Processes.Capture`, which ends the process's tree at its limit
metadata:
  type: reference
---

Pull request 307 made the checker end a chain of copies that branches at the bound of 64 copies
(T33). Its test checked the program in the test host and waited on that check with a limit:
`Task.Run(() => NotSupported(Branching)).WaitAsync(TimeSpan.FromSeconds(20))`. `WaitAsync` ends
the wait, and the test fails with a `TimeoutException` at 20 seconds, but nothing ends the task:
where the check does not end, as with that fix taken out, the check of about 2^64 copies goes on in
the test host, on a core and in its memory, while the other tests run, for as long as the host runs. The round of pull request 307 rated it low, and its walk at
the cap read it as one that holds (issue 39, comment 6659115974).

Pull request 342 runs the check in a process of its own: `kurz exec` with the program's text,
through `Processes.Capture` with a limit of 20 seconds, which ends the process's tree when the time
is up and answers -1 with "gave no answer within 20 s and was ended". With the fix of pull request
307 taken out of `CopyOf`, the test failed at its limit, the run of the four tests of the bound took
27 seconds, and no `kurz exec` was left running in the container. The round of pull request 342
read that limit as one that also holds the start of `dotnet` and the load of the compiler, which a
loaded runner can stretch past it (issue 19, comment 6680805232), so the test starts `kurz` as the
command's tests do, with their limit of two minutes.

What to do: a test whose limit must end the work runs the work in a process of its own, through
`Processes.Capture`, as the command's tests and the corpus's runner do
([[an-orphan-holds-the-output-of-its-program]] says what `Capture` ends at its limit, and what it
cannot end on Windows). A `CancellationToken` ends the work only where the work reads it, and the
checker reads none. On 2026-11-05 no other test of `compiler/Kurz.Compiler.Tests/` used `WaitAsync`
or `CancelAfter` as a limit.
