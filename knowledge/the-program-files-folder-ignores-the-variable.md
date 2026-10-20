---
name: the-program-files-folder-ignores-the-variable
description: Environment.GetFolderPath(SpecialFolder.ProgramFiles) answers the shell's known folder whatever the ProgramFiles variable says, so a test that pointed the variable at an empty directory still found the installed LLVM on the Windows job (2026-10-20); the clang search reads the variable, as the gate does.
metadata:
  type: reference
---

Seen on 2026-10-20: the command test that empties `PATH` and `ProgramFiles` and expects
`kurz: clang is not there` got the exit code 0 on the Windows job: the command had found
`LLVM\bin\clang.exe` under the real Program Files directory.

The search took that directory from `Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)`,
which asks the shell for the known folder and reads no environment variable; the variable the
test set, and the one a person sets to point the command elsewhere, changed nothing. The gate
`tools/compiler_gate.py` reads the variable `ProgramFiles`, so the two searches disagreed on
Windows alone.

The search reads the variable, and an unset or empty variable gives no candidate
(`Clang.Candidates` in `compiler/Kurz.Compiler/Build/Clang.cs`); `compiler/README.md` names the
variable. A place the compiler looks at is read from the environment, so that a test and a person
can move it; a known folder cannot be moved from outside.
