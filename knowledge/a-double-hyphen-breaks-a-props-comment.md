---
name: a-double-hyphen-breaks-a-props-comment
description: two hyphens inside an XML comment of compiler/Directory.Build.props (a command line option written in the comment) broke every restore of the solution with MSB4181, "the RestoreTask task returned false but did not log an error", and a restore of one project with NETSDK1013, an empty TargetFramework; XML allows no double hyphen inside a comment, so the shared props file was not read (2026-10-23)
metadata:
  type: reference
---

## What happened

On 2026-10-23 a comment added to `compiler/Directory.Build.props` named the command's version
option, two hyphens and its name. Every restore of `Kurz.sln` then failed with

    error MSB4181: The "RestoreTask" task returned false but did not log an error.

and nothing more, at any verbosity tried. A restore of one project said what was missing:

    error NETSDK1013: The TargetFramework value '' was not recognized.

`TargetFramework` is set in that props file alone: the file was not read. The XML specification
allows no `--` inside a comment, and the comment held one. Writing the option without its hyphens
fixed every restore.

## What to do

- A restore of the whole solution that fails with MSB4181 and no error of its own: restore one
  project (`dotnet restore Kurz.Compiler/Kurz.Compiler.csproj` from inside `compiler/`), whose
  error names the property that went missing, and read the file that sets it.
- In a comment of a `.props`, `.targets` or `.csproj` file, write a command line option without
  its hyphens, or describe it in words.

The build of the compiler is the gate: `compiler-tests` cannot restore, so it fails, and the
first error it prints is the MSB4181 line above.
