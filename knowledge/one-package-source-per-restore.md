---
name: one-package-source-per-restore
description: with central package management, a restore that sees two package sources without a source mapping fails on NU1507 once warnings are errors; the sources come from every NuGet.config up the directory tree and the user profile, so a feed configured elsewhere on the machine turns the compiler's build red while a clean checkout is green. The repository's own compiler/NuGet.config clears the inherited sources, names the one source and maps every package to it (2026-10-10)
metadata:
  type: reference
---

## What happened

The first build of the compiler on 2026-10-10 failed with NU1507: central package management
(`ManagePackageVersionsCentrally` in `compiler/Directory.Build.props`) warns when more than one
package source is configured and no package source mapping says which source serves which
package, and `TreatWarningsAsErrors` made the warning an error. The second source was a feed
configured in the user profile of the machine, for another project; nothing in the repository
named it. A clean clone on a machine without that feed would have built.

NuGet reads every `NuGet.config` from the project directory up to the drive root, then the one in
the user profile, and merges their sources. A build is therefore green or red by the machine it
runs on, unless the repository says which sources count.

## How to apply

- `compiler/NuGet.config` begins its `packageSources` with `<clear />`, names the one source and
  maps `*` to it under `packageSourceMapping`. Every restore, on any machine and in CI, then
  resolves against that one source, whatever the machine has configured.
- Every package version stands in `compiler/Directory.Packages.props`, the restore writes
  `packages.lock.json` beside each project (`RestorePackagesWithLockFile`), and the gate restores
  in locked mode (`-p:RestoreLockedMode=true`): a package the lock files do not name fails the
  restore instead of being resolved anew.
- Gate: `compiler-tests` runs the locked restore on every gates run; `tree` admits
  `compiler/NuGet.config` by name (`tools/tree_gate.py`, with its red proof), so the file cannot
  be dropped for a machine-wide setting without the tree gate seeing a path it does not hold
  when a second config appears elsewhere under `compiler/`.
