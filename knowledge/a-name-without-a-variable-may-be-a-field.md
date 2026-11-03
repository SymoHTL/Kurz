---
name: a-name-without-a-variable-may-be-a-field
description: The checker's reading of what stands after `mut` (M7) returned on a name the lookup of variables found nothing for, because the name's own reading reports a name that is nothing; since pull request 181 a field of the instance stands by its name alone inside a method (K7) and has no variable, so a field passed as `mut` in its method was neither refused without `mut` nor given the field's place, and the build ended in the compiler's own error (2026-11-02); a check that lets a name without a variable pass reads what else the name can be first
metadata:
  type: feedback
---

The checker reads what stands after `mut` in a call (M7) in `MutArgument`
(`compiler/Kurz.Compiler/Semantics/Checker.Calls.cs`). It looks up the name at the root of the
path among the variables in scope and, where it finds none, returns without a word, because the
name's own reading reports a name that is nothing.

Pull request 181 brought classes, and with them a second thing a name can be: inside a method, a
field of the instance named alone (K7), which no variable carries. The name's own reading finds the
field and reports nothing, so a field passed as `mut` went through unchecked. One without `mut` was
not refused (K1), and the emitter, which gives a `mut` argument the place of the variable the
checker recorded for the name, found none and stopped with its own error: "the checker did not read
the name `N` at line 7". `Bump(mut N)` in a method of `class C(int N)` ended so on `main`, and so did
the same call in `class C(mut int N)`, where M7 lets the call stand (2026-11-02). No case and no test
of the suite passed a field as `mut`.

Since the fix, `MutArgument` reads a name without a variable as a field of the instance before it
lets it pass: a field without `mut` is `mut-required`, the same field twice is `mut-at-call`, and
the emitter gives the call the field's own place. Inside a lambda the field is passed as well, through
the reference to the instance the lambda holds (F11 with K2: the owner, 2026-11-02, #110, comment
6579495475); until that answer it was `capture-assign`.
The assignment (`CheckAssignOrDeclare`) and a `mut` method's receiver already read the field, and
refuse one without `mut`. The shape to look for is an early return on a name the lookup of
variables does not find, on the ground that another reading reports it: that reading may find the
name as something the return never checks.

`CheckerTests.A_mut_parameter_takes_a_mut_variable_marked_mut_at_the_call` holds the refusals and
a `mut` field passed clean.
`ListTests.A_mut_field_of_the_instance_passed_as_mut_is_written_in_its_place` builds and runs a
fenced program that passes an `int` field and a list field and prints what the callee wrote.
