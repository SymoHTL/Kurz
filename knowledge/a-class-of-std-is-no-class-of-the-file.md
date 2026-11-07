---
name: a-class-of-std-is-no-class-of-the-file
description: Pull request 276 brought `HttpHeaders` into every file as the symbol `std/`'s checker made (W1). The cycle rule's walk (R2) kept its fields in a table of the file's own classes only, and looked up every class a field reached, so a class of the program with a field of `HttpHeaders` stopped the checker with a `KeyNotFoundException` instead of building (found 2026-11-03). A walk over the classes of a file meets classes of std/ that are not in the file's tables
metadata:
  type: feedback
---

The checker judges the cycle rule (R2, R4) in `CheckReferenceCycles`
(`compiler/Kurz.Compiler/Semantics/Checker.Classes.cs`). It builds a table of the classes each field
reaches, one entry for each class the file declares (`classDeclared`). Then `RingThroughMut`
searches that table for a ring with a `mut` field on it, and it looked up every class it reached
with the table's indexer.

Pull request 276 brought a second kind of class into a file. A class of `std/` that the prelude
names (W1), `HttpHeaders`, is the symbol `std/`'s checker made, and `classDeclared` does not hold
it. A field whose type holds one, `class Message(HttpHeaders Headers)`, reached a class the table
had no entry for, and the checker stopped with `KeyNotFoundException`. The build never reported an
error, and nothing ran. No case and no test had a class of the program with such a field: the
tests of W4 used `HttpHeaders` from variables at the top level. Found on 2026-11-03, when the
request reader's probe declared such a class.

The search then passed over a class that had no entry in the table, since a class of `std/` named
no class of the file. R9 changed that: a field of a function type reaches every class a lambda of
the program captures, so a class of `std/` with such a field reaches the file's classes. The walk
now builds the entry of a class of `std/` from its fields when it first meets one, and it also
searches from the classes the lambdas capture, for a ring of `std/`'s classes alone that a lambda
of the file closes. `std/`'s own checker judges the rings among `std/`'s classes that no lambda of
the file closes. `CheckerTests.A_class_of_std_with_a_field_of_a_function_type_reaches_the_classes_the_file_s_lambdas_capture`
checks both rings, against an edited `std/`.

The shape to look for: a table keyed by the file's own classes, filled from `classDeclared`, and
read for a class that a type reached. Since W1 such a class can come from `std/`. Read the table
with `TryGetValue`, and say what a class of `std/` means to the walk.

`CheckerTests.A_class_of_the_file_may_hold_a_class_of_std_in_its_fields` checks a class with three
fields of `HttpHeaders`: one `mut`, one in a list and one nullable. It must check clean. Its control
is a ring of the file's own classes beside such a field, which must still be `reference-cycle`. The
test was seen red with the table's indexer put back, failing with the same `KeyNotFoundException`.
