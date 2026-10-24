---
name: a-borrow-outlives-its-source
description: The counting of lists (pull request 63) read a list from a place without a count of its own and kept it while code that could change the place ran; three shapes reached main, one inside an expression (2026-10-24), which the checker refuses since the owner's answers (M7), the name an arm binds, which holds a count of its own since, and an assignment in an else arm's block inside an expression (2026-10-25), which the checker's walk of M7 missed and refuses since; a borrow names what keeps its source unchanged while it is used, and a walk that checks it names every statement that changes a place
metadata:
  type: feedback
---

The emitter of pull request 63 counts the holders of a list's block, and a value read from a
variable, a field or an item is borrowed: no count is raised for the read, since the place still
holds one (`compiler/README.md`, the counting paragraph). A borrow is sound only while nothing can
change the place it came from. Three shapes in which something could reached `main`:

- **Inside one expression.** `G(xs, F(mut xs))`: `G`'s first argument is read as a borrowed
  pointer, then `F` runs and gives `xs` a new block, and `G` receives a freed one. The emitter held
  the borrowed arguments of a call with a `mut` slot of its own, which covered `Fill(mut a, a)` and
  missed the call around it, `with` and an index. The review of pull request 63 found them, and
  they went to issue 39 at the review cap. The owner answered on 2026-10-24 that such a read is a
  compile error (record, section 4; reference M7), so the checker refuses the places M7 lists and
  the guard is gone; a shape its walk misses is caught only by a fenced program that holds it, or by
  the review (the third shape).
- **Across statements.** `match u { List<int> l => { u = Empty; ...; print(l) } }`: the name `l`
  was the payload of `u`'s list, borrowed, and the assignment freed it. Built on 2026-10-24 before
  the fix, the program printed `[7, 8]`, the items of a list made after the assignment in the freed
  block, instead of `[1, 2]`. It was found while the guard was being proved unreachable, not by a
  test: the fenced programs of `ListTests` held no arm that changes its subject.
- **Through an arm's statement.** `Sum(numbers, Find(2) else { NotFound => { numbers = List<int>(); 5 } })`:
  the checker's walk of M7 (pull request 64) counted the `mut` arguments in an arm's statements as
  changes and not an assignment or a `mut` method, so `Sum` was given a block the assignment freed.
  Built on 2026-10-25, the program ended with `error: index-out-of-range` at `Sum`'s read: the
  freed block had been given out again for the list of one item. The review of pull request 64
  found it, at the cap; the owner answered on 2026-10-25 that it is a compile error,
  `changed-while-held`, as Rust's E0506, and the walk counts every statement that changes a place.

**Why:** a borrow is a claim about every path from the read to the last use, and the claim was
checked only for the paths the author thought of. A program that frees nothing never shows the
defect; one whose freed blocks are given out again shows it as wrong output, not as a crash.

**How to apply:** every construct of the emitter that reads a place and keeps what it read past a
call or a statement names what keeps the place unchanged: the checker's refusal within one
expression (M7, `Semantics/Checker.Reads.cs`), no state outside a function that holds a list (R13),
the count a loop holds of its collection (C6), the count the name an arm binds holds. A construct
that has none takes a count of what it read. Each one gets a line in a fenced program of `ListTests`
(an allocator that makes every freed block inaccessible), where a read after the free ends the run
with the system's signal. A refusal of the checker names every form that changes a place, a `mut`
argument, a `mut` method, an assignment, in every place a statement can stand inside an expression,
each with a refused case and an allowed one. Gate: `compiler-tests` for the shapes those programs
and the checker's tests hold; a new borrowing construct that no program reaches, and a change the
walk does not count: review rule "compiler". Related:
[[guarantee-needs-its-mechanism]].
