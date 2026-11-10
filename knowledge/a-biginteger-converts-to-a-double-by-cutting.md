---
name: a-biginteger-converts-to-a-double-by-cutting
description: On .NET 10 `(double)` of a System.Numerics.BigInteger cuts the bits past the 53rd, where `(double)` of a `ulong` or a `long` rounds to the nearest double, ties to even, as LLVM's `uitofp` and `sitofp` do in a Kurz program; 2^53 + 3 gives 2^53 + 2 through a BigInteger and 2^53 + 4 through a ulong, and 2^63 - 1 gives 2^63 - 1024 against 2^63 (probed 2026-11-10, .NET SDK 10.0.402, runtime 10.0.13). The checker holds an integer constant as a BigInteger, so a fold that must give the double a program computes converts through the C# type of the program's integer
metadata:
  type: reference
---

## What happened

Pull request 423 had the checker decide a `ulong` constant count of `duration.FromNanoseconds` and
the other constructors of T38 by the `double` the program computes of it, which the emitter makes
with LLVM's `uitofp`, rounding to the nearest double, ties to even. The checker holds an integer
constant as a `BigInteger` (`IntegerConstant`, `compiler/Kurz.Compiler/Semantics/Constant.cs`), and
`(double)` of a `BigInteger` does not round: it cuts. The fold converts the constant as C#'s `ulong`
first, `(double)(ulong)k.Value`, whose conversion rounds as `uitofp` does (`CheckTimeFrom`,
`compiler/Kurz.Compiler/Semantics/Checker.Calls.cs`), and a code comment there records the cut.

A probe of 2026-11-10 (.NET SDK 10.0.402, runtime 10.0.13, Linux) read:

| value | `(double)` of the `BigInteger` | `(double)` of the `ulong` or the `long` |
|---|---|---|
| 2^53 + 3, 9007199254740995 | 2^53 + 2 | 2^53 + 4 |
| 2^63 - 1, 9223372036854775807 | 2^63 - 1024 | 2^63 |

## What to do

- A fold that must give the `double` the program computes converts the constant through the C#
  type of the program's integer, `(double)(ulong)` or `(double)(long)`, never `(double)` of the
  `BigInteger`.
- A fold whose constant can lie beyond a `ulong`, an integer literal typed `float` or `double`,
  reads the integer's invariant digits once with `float.Parse` or `double.Parse` (`FloatOf` and
  `DoubleOf`, `compiler/Kurz.Compiler/Semantics/Checker.Expressions.cs`), which rounds once, as C#
  folds the constant. The cut gave two defects of that kind, fixed by pull requests 436 (`float`)
  and 438 (`double`).
- A test of such a rounding takes a value whose cut and rounding differ: 2^53 + 3, where 2^53 + 1
  is a tie that both give as 2^53, and 2^63 - 1, which rounds out of a `long`'s range and cuts into
  it.

The gate is `compiler-tests` for T38's fold: `CheckerTests` refuses `duration.FromNanoseconds` of
2^63 - 1 as a `ulong` constant, which a cut would let through, and `FloatingTests` holds the
folds of an integer literal typed `float` and one typed `double` to the conversion at run time.
Another fold that converts a
`BigInteger` to a `double` is a `judgment step` for its author and the review.
