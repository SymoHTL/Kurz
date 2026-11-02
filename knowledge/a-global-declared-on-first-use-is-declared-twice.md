---
name: a-global-declared-on-first-use-is-declared-twice
description: The emitter of pull request 181 declared the flag of a release under way with the first class release it built, on the test that its list of released classes was still empty; that release builds the releases of its fields' classes before its class is on the list, so a program whose first released class held a field of another class declared the flag twice and clang refused it (2026-11-02); a module global is declared where its existence is decided once
metadata:
  type: feedback
---

The emitter builds a helper function the first time a call needs it, and builds it there and then
(`Helper` in `compiler/Kurz.Compiler/Emit/Emitter.Data.cs`): a helper that calls other helpers
builds them inside its own body, before its own builder returns.

Pull request 181 counted the instances of classes and freed them off one work list per class, with
one flag per thread that says a release is under way (the owner, 2026-11-01, "Iterative, never
crashes"). `ReleaseInstance` declared that flag when the emitter's list of released classes,
`released`, was still empty, and put its own class on the list only after it had built the class's
`DropInstance`. `DropInstance` lets go of each field, and for a field of a class type builds that
class's `ReleaseInstance`, which found `released` still empty and declared the flag a second time.
Clang refuses such a module: `redefinition of global '@kz.releasing'`.

So every program whose first released class held a field of another class failed to build, with
the compiler's "clang did not build the program": `class Outer(Inner? I, int N)` released before
any `Inner` was (2026-11-02). The suite held no such program: in each of its programs a temporary
or a local of the field's class was released first.

Since the fix, `EmitDrain` declares the flag; the emitter calls it once, after the last function,
exactly when `released` is not empty. The shape to look for is a global, a type or a constant
declared at its first use by a test of state that the builder changes only after its body has run:
a builder nested in that body sees the state from before the change. Declare it where its
existence is decided once, after the functions or under a set of the names already declared, as
`helpers` holds the helpers built, never on a count the outer builder has not raised yet.

`ListTests.The_first_class_a_program_releases_may_hold_a_field_of_another_class` builds and runs
that program.
