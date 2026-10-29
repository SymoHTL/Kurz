---
name: a-dotnet-open-takes-a-lock-that-does-not-wait
description: on Linux every FileStream that .NET opens takes a flock of its own that does not wait, shared or exclusive by its FileShare, and an open of a file another process holds with an exclusive flock throws IOException whatever FileShare it asks for; a lock meant to wait, and a read of a file held under such a lock, go around .NET's open (probed 2026-10-29, .NET SDK 10.0.402)
metadata:
  type: reference
---

## What happened

The record's places of `run` and `exec` (section 2, line 22) hold a lock file under the operating
system's lock, `flock` on Linux, which a second command waits for. Locked through .NET's own
`FileStream`, the lock did not wait: .NET's open takes a `flock` of its own with `LOCK_NB` before
it returns, and that one fails at once where another holder has the file.

A probe of 2026-10-29 in the image of the `gates` job (.NET SDK 10.0.402, root) held a file with
`flock -x <file> sleep 5` and opened it from .NET with
`new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)`:

    IOException: The process cannot access the file '<file>' because it is being used by another process.

A test that read the mark of a lock file a `Place` held failed the same way.

## What to do

- A lock that waits goes through libc: `open` with `O_CLOEXEC`, then `flock`, called directly, as
  `compiler/Kurz.Compiler/Build/Place.cs` does (its `Open`). The handle then carries no lock of
  .NET's.
- A test that reads a file another process holds under an exclusive `flock` reads it outside .NET's
  open, through `cat` on Linux, as `PlaceTests` reads the mark, or after the holder let it go.
- On Windows, .NET opens through `CreateFile` with the share mode asked for; the trap is Linux's.

The gate is `compiler-tests`: `PlaceTests` holds a place and reads its lock file while it is held.
