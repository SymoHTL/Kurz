---
name: git-stores-a-lone-carriage-return-as-binary
description: Under `* text=auto eol=lf`, git 2.53.0 stored a CRLF file as LF and left a file with a lone carriage return untouched, judging it binary (one probe repository, 2026-10-08); a corpus case that expects a carriage-return byte marks its path `-text`, and the git of the CI image is not pinned (HAZARD #15)
metadata:
  type: reference
---

What was seen on 2026-10-08, in a throwaway repository with the attributes line of this tree,
`* text=auto eol=lf`, under git 2.53.0 on the machine that runs the local gates:

- A file whose lines end in CRLF was stored with LF and checked out with LF.
- A file that holds a lone carriage return (a CR with no LF after it) was stored and checked out
  byte for byte: git judged it binary and converted nothing.

That is one version of git, in one probe. The git of the CI image is pinned by nothing
(HAZARD #15), so what it does with such a file there is assumed to be the same, not known.

**How to apply:** an expectation of the corpus is compared byte for byte, so a case that expects
a carriage-return byte in its output marks its path `-text` in `.gitattributes`, as the comment
there says; it does not lean on the binary judgment, which a later git may make differently.
`judgment step`
