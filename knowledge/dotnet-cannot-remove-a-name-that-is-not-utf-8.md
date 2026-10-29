---
name: dotnet-cannot-remove-a-name-that-is-not-utf-8
description: on Linux .NET reads a file name that is not UTF-8 with U+FFFD in place of each bad byte, so File.Delete of the name it read returns and removes nothing and Directory.Delete with recursive throws IOException "Directory not empty", for a root user as well; a removal that must not fail by permissions can still fail so (probed 2026-10-29, .NET SDK 10.0.402)
metadata:
  type: reference
---

## What happened

The removal of a place of `run` and `exec` (`compiler/Kurz.Compiler/Build/Place.cs`, its
`Remove`) had to be shown failing on Linux, where the tests run as root and no mode stops a
removal. A file whose name holds a byte that is not UTF-8 did it.

A probe of 2026-10-29 in the image of the `gates` job (.NET SDK 10.0.402, root) made a directory
holding a file named by the one byte `0xFF` (`touch "$dir/$(printf '\377')"`; `ls -b` shows
`\377`) and found:

- `Directory.EnumerateFileSystemEntries` names it `U+FFFD`, the replacement character, which
  encodes as three other bytes;
- `File.Delete` of that name returns without an error and removes nothing, since no file has the
  name it was given;
- `Directory.Delete(dir, recursive: true)` throws `IOException: Directory not empty`, and the
  directory stays.

## What to do

- Code that removes a tree it did not write itself, such as a place a program wrote into, expects
  this failure and handles it as any failed removal; `Place` passes over it silently, and a start
  that follows removes the place once the name is gone (the owner, 2026-10-29, "Silent").
- A test on Linux that needs a removal to fail, for a root user too, makes such a name, as
  `PlaceTests.A_removal_that_fails_is_passed_over_and_the_next_start_removes_the_place` does, and
  removes it again with `rm` from a shell, which takes the bytes as they are.
- `File.Delete` returning is no proof that the file is gone; read the directory again where that
  matters.

The gate is `compiler-tests`: the test above is red where the removal of such a name does not fail.
