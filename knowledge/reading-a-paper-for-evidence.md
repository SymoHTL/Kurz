---
name: reading-a-paper-for-evidence
description: Recipe for citing a number from a paper in the design record - extract the PDF text with pypdf, find the sentence, quote the figure with its conditions and link the source; used for the value-tree benchmark on 2026-10-01
metadata:
  type: reference
---

The design record cites measured results where it claims speed, for example the functional
red-black tree in the memory section of `kurz-design.md`. A number goes in only after the sentence
that states it was read in the source itself. How that was done on 2026-10-01:

1. Download the PDF. A page fetcher that converts pages to text cannot read a binary PDF.
2. Extract the text with `pypdf` (`PdfReader(path).pages[n].extract_text()`), page by page. An
   agent's built-in PDF reader may depend on a renderer that is not installed.
3. On a Windows console set `PYTHONIOENCODING=utf-8` before printing: the default code page
   cannot encode the ligatures and symbols in a paper, and the run dies on the first one.
4. Search the extracted text for the figure and read the sentences around it. Record the figure
   together with what it was measured on: the workload and its size, the machine, and what it is
   compared with.
5. In the design record, state the figure with those conditions, link the paper, and say plainly
   when no measurement of Kurz itself exists.

**Why:** a remembered benchmark is usually right about the direction and wrong about the size or
the conditions. In that session the same technique measured "within 10%" in one paper and "19%
faster on one CPU, about equal on another" in a later one; both are in the record, each with its
source.
