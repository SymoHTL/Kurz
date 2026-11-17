---
name: a-field-read-holds-its-block-to-the-statements-end
description: The first red proof of the count a copy's memo holds (pull request 550, mutation M16) stayed green, since its case reached the block by a field read given as an argument, which the statement holds to its end whatever the memo does; a red proof of a held count reaches the block through a path its statement does not hold
metadata:
  type: reference
---

## What happened

A call of more than one copy keeps one memo for all its copies (U12), and the memo holds a count of
each block it maps until the call's last copy, so that no block made meanwhile takes the address of
one it maps (section 6 of the design record). Pull request 550 proved that count red with mutation
M16: the count taken off, none let go of.

Its case was `Pair(copy h.B, copy Swap(h))`, where `Swap` replaces `h.B` before the second copy. The
mutation stayed NOT RED. The argument `h.B` is a field read, "which a statement holds to its end
anyway", as the description of pull request 550 says, so that a later argument that replaces the
field frees nothing the statement reads (the rule "A value the emitter borrows names what keeps its
source unchanged" of `CLAUDE.md`). The block stayed alive through the statement's own hold, no new
block could take its address, and the memo's count changed nothing a run could see.

The case `corpus/actors/copy-memo-keeps-block.kz` now reaches the block through its holder alone:
`Pair(copy h, copy Swap(h))`. Nothing in the statement holds `h.B`, so `Swap` frees it while the
call's session is open, while only the memo's count keeps its address from a block made meanwhile,
and M16 turned the case red. The whole batch, M13 to M18, ran again, each mutation red.

## What to do

- A red proof of a count that keeps a block alive breaks that count and frees the block before the
  read that needs it. Reach the block through a path its statement does not hold: a field of a value
  the argument holds, an item of a list, never a field read or a variable given as the argument
  itself.
- When such a mutation is NOT RED, ask first what else holds the block: the statement's hold of a
  field read, a variable's count, a temporary of the region.

Gate: `compiler-tests` runs `copy-memo-keeps-block.kz`, which reaches the block through its holder.
A new red proof that reaches its block by a path the statement holds is caught by no rule: a
`judgment step` for the author of the proof.
