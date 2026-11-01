---
name: a-built-in-type-needs-no-declaration
description: Every file sees eleven data and enum types without declaring them, `Invalid`, `InvalidUtf8`, `IoError`, `ParseError` and `HexError` among them, so a case that uses one with no declaration is right and one that declares it is `redeclared`; the review of pull request 139 read `Invalid` in a case as undeclared (2026-10-31). The list is `BuiltInTypes` in the checker, each type with the rule that declares it in `BuiltInTypeRules`.
metadata:
  type: reference
---

Seen on 2026-10-31: the review of pull request 139 read a case that returns `Invalid` from a
function, with no `data Invalid` in the file, as a case that does not build. The case was right:
`Invalid` is the case of an enum's `From` that is no value of it, which every file sees (D18). The
case `corpus/outcomes/keep-argument-partial.kz` says so in a comment of its own, which a reader of
another case does not see.

Every file sees these types without declaring them, declared before the file's own types
(`BuiltInTypes` in `compiler/Kurz.Compiler/Semantics/Checker.cs`), each with the rule that
declares it (`BuiltInTypeRules` in the same file). The list on 2026-11-01:

| Type | What it is | Rule |
|---|---|---|
| `InvalidUtf8(int At)` | the case of `string.FromUtf8` that is not a string | T15 |
| `Invalid` | the case of an enum's `From` that is no value of it | D18 |
| `IoError(IoKind Kind, int Code, string Path)`, with the enum `IoKind` | a call on a file or a socket that failed | S1 |
| `EnvError(EnvKind Kind, string Name)`, with the enum `EnvKind` | a read of a variable of the environment that gave no string | S14 |
| `Entry<K, V>(K Key, V Value)` | the entry of a loop over a map | C6 |
| `ParseError(ParseKind Kind)`, with the enum `ParseKind` | a number type's `Parse` that gave no number | X7 |
| `HexError(HexKind Kind, int At)`, with the enum `HexKind` | a `Hex.Decode` that gave no bytes | H7 |

A file that declares one of these names is `redeclared` (V9), and the message names the rule that
declares the type, since no line of the file does (`corpus/types/invalid-utf8-redeclared.kz`,
`corpus/hashing/hex-kind-redeclared.kz`). A case or a sample uses such a type by its name alone; a
reader who takes one for undeclared looks the name up in the checker's list, which is the one
definition: this table is a copy of the day it was written.
