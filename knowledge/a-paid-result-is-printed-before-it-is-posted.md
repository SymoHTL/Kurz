---
name: a-paid-result-is-printed-before-it-is-posted
description: Postmortems of 2026-10-02 and 2026-10-03 - the first full review posted 40 comments in a row, GitHub's secondary rate limit refused the rest, and 88 findings that existed only in memory were lost with about 45 USD of passes behind them; a result that cost money is written to the log in full before the first post, and posts are paced one second apart with a wait on that refusal
metadata:
  type: feedback
---

On 2026-10-02 the first full review of pull request 8 ran 30 passes and found 294 defects. It
then posted them: one comment per file for the high and medium findings, and the lows in comments
of twenty. After 40 posts without a pause the forge answered the 41st with

`gh: You have exceeded a secondary rate limit and have been temporarily blocked from content creation.` (HTTP 403)

and the same for every post after it. The last five comments, 88 low findings, were never posted.
The comment that stores what was reviewed was refused as well, and so was the note that says the
review did not complete. The findings lived only in the process: the log held one line per pass
and one line per refused post with a count, not the text. When the process ended they were gone,
together with the line that states what the run cost. The answer is kept as
`tools/review/fixtures/gh-rate-limit-refusal.txt`.

What GitHub documents (REST API docs, "Rate limits for the REST API" and "Best practices for
using the REST API", read 2026-10-02): in general no more than 80 content-generating requests per
minute and 500 per hour, with lower limits on some endpoints and no way to read the remaining
allowance; for many `POST`, `PATCH`, `PUT` or `DELETE` requests, wait at least one second between
each; after a secondary rate limit without a `retry-after` header, wait at least one minute, then
longer. How fast the run posted was not recorded: it started one process per post, with no pause
between them.

**Why:** the tool treated posting as the cheap, safe end of the run and the model passes as the
part that can fail. It was the other way round: the passes had succeeded and were paid for, and
the only copy of their result was held back until a write to a rate-limited service had worked.

**How to apply** (`tools/review/review.py`; Gate: `self-tests`, each with its case and red proof):

- Every finding is printed to the log in full, followed by a `FOUND:` line with the counts and
  the cost, before the first post. Whatever becomes of the posting, the log holds the result.
- The log takes every character. On 2026-10-03 the second full review of the same pull request
  ran its 30 passes, printed 235 of its 237 findings and died on the 236th: it quoted an arrow,
  the output was redirected to a file, and on Windows a redirected stdout takes the console's code
  page (cp1252), which has no arrow. The exception ended the run before the first post; the two
  findings after it, and the posting of all of them, were lost. The print is the one copy of a
  paid result, so it must not be able to fail on its content: the reviewer sets its stdout to
  UTF-8 with `backslashreplace` before anything else (`sys.stdout.reconfigure`). An interactive
  console was never the problem: since Python 3.6 it writes Unicode; a pipe or a file is.
- Posts go out at least one second apart (`PACE_S`).
- A post that the forge refuses with that message waits and is sent again: 60 seconds, then 120
  (`RATE_WAITS`), spent once per run so that the waits fit into the time kept back for posting.
  A write that got no answer is never sent again: it may have landed. Once both waits are spent
  and the forge refuses again, the posting stops: every further write would go into the limit and
  lengthen the block; the rest is counted as not posted.
- A finding above low that no anchor takes lands as a plain note without a marker, and counts as
  not posted: a note is not a thread, nothing holds the merge for it, and the next run posts it
  again.
- A post that still fails is printed as `NOT POSTED` with the file, line and title of each
  finding. Its files are not stored as reviewed, so the next run reviews them again, and the run
  ends red (`failed`).
- Reporting a failure tries each write once and never raises.
- Low findings are no longer comments on the pull request, twenty at a time; they are collected
  on one issue. A large review writes far fewer posts.

The general shape: when a step that costs money or time is followed by a step that can be
refused, the result is made durable (a log, a file) between the two.
