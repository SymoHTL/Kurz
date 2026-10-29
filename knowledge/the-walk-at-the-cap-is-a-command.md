---
name: the-walk-at-the-cap-is-a-command
description: At the review cap, py -3 tools/pr_gates.py cap --pr N --analysis <file> prints the collection ticket's section for the pull request (the head, the day, every open finding with its thread's address, the session's reading, the lows with their addresses) and the reply each open finding thread gets; --go posts the section as a comment on the ticket, replies, resolves and reads the threads back. Sessions took this walk by hand for every pull request at the cap from 2026-10-21 until it became the command (2026-10-29)
metadata:
  type: reference
---

At the review cap no fix push follows the round ([[rounds-do-not-converge]]): what the round left
open goes on the collection ticket, the open issue labelled `review-leftovers`, one section per
pull request, every open thread gets a reply that links it and is resolved. By hand that was a
read of every thread's finding marker, a section written from them with each thread's address, a
search of the lows issue for the comments of the head, an edit of the ticket, a reply per thread
and the resolve command: the same steps for every pull request at the cap since 2026-10-21, which
the third-time rule makes a tool.

## How to apply

1. Read every open thread and write what the code at the reviewed head shows, grouped, in a
   file. The reading is the session's (`judgment step`).
2. `py -3 tools/pr_gates.py cap --pr <N> --analysis <file>`: the plan. It prints the section and
   the reply each open finding thread gets, and writes nothing. It refuses, before any write:
   - below the cap: there a finding is answered by its edit ([[resolve-what-the-edit-answered]]);
   - with no open issue labelled `review-leftovers`, or with several;
   - when an open finding was made on another commit than the head the pull request shows: a
     finding of an earlier round, or a head that moved after the round at the cap, which the
     cap's approval does not cover;
   - when a person wrote anything but a move in an open thread: read it and answer it first;
   - when the reviewer's notes on the pull request name lows on the head that the comments on
     the issue they name do not show: the counts must agree.
3. `py -3 tools/pr_gates.py cap --pr <N> --analysis <file> --go` posts the section as a comment on
   the ticket, replies in every open finding thread with the ticket and the section's address,
   resolves them as `resolve` does, a second between writes, and reads the threads back: a
   finding thread still open is exit 1, named. A walk that stopped is run again: a section on
   the ticket, as a comment or in the body as the walks by hand wrote it, is not posted twice,
   and a thread a reply moved gets no second reply.
4. Run the failed jobs of the gates again when `pr-findings` read the threads open, then merge at
   the reviewed head (the skill `change-walk`, section 4).

Why a comment and not the body: on 2026-10-29 the ticket's body held 205688 characters of
sections. A section in the body is a read, an edit and a write back of all of it, and a second
writer between the read and the write loses its edit; a comment is one write, with its own
address for the replies to link.

Where the lows are found: each of the reviewer's notes on the pull request carries its lows with
the head they were made on and names the issue that collects them; the comment on that issue
opens with `kit.lows_header` for the pull request and the head, the one definition the reviewer
writes it with.

Gate: `self-tests` (twenty-six cases of the walk over the captured thread, note and lows comment
of pull request 82, each named by a mutation in `tools/red_proofs.json`).
