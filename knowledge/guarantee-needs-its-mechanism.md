---
name: guarantee-needs-its-mechanism
description: The design record once claimed durable actor state survives the loss of its machine, with a log on that machine's own disk (2026-10-01); a guarantee is written with its mechanism and the failure it survives
metadata:
  type: feedback
---

On 2026-10-01 the first version of the "Durable actors" section of `kurz-design.md` claimed that
a `durable` actor's state survives the loss of its machine. The mechanism it described was a
message log on the local disk. A log on the machine's own disk does not survive the loss of
that machine. The claim was corrected in the next design round, and it turned into a real design
question: `durable` now has three modes (local disk, a copy on N machines before a message counts,
a background copy), each with its cost.

**Why:** "survives X" was written as a property of the feature instead of a consequence of a
mechanism. Nobody had to ask "survives what, and how", so the gap between the promise and the
mechanism stayed invisible. A wrong guarantee in a design record is worse than a missing one: the
next decision is built on it.

**How to apply:** a guarantee in the design record or the reference names (1) the failure it
survives, (2) the mechanism that makes it survive, and (3) what it costs. "Cannot happen",
"never lost", "always" and "provably" are the words to stop at. If the mechanism is not decided,
the guarantee is not either: it goes under "Open". A wish that no mechanism can deliver is
contradicted in the reply to Simon, not recorded. The review rule "design record" names this
shape; there is no mechanical gate for it.
