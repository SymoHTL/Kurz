# 12. Errors

The errors the corpus cases expect. An id is what a corpus header names: `// expect: error <id> at
<line>` for a compile error, `// expect: throws <id> at <line>` for an exception the program
raises when it runs (E3). The two tables share one namespace of ids.

## Compile errors

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read; a parameter and a loop variable are no variables for this (V13) |
| `assign-immutable` | V7, V8, D4, K1, F8, K14 | an assignment to a variable or a parameter that is not `mut`, into a path that starts at one (M4: for a `data` value the variable at the root decides, so `root.Left = x` through a `mut` variable is allowed), or to a field of a class instance that is not `mut`; inside a constructor a field without `mut` is assigned once (K14), and a second time is this error |
| `redeclared` | V9, F16 | a declaration, in any of its forms, of a name that is already visible, or of a top-level variable's name inside a top-level function |
| `argument-mismatch` | D11 | a call with an argument whose name no parameter has, two arguments for one parameter, or none for a parameter without a default |
| `enum-number` | D20 | an `enum` that numbers two values alike or only some of its values, or `.Number` and `From` on one that numbers none |
| `flags-number` | D19 | a flags name whose number is not one bit of its own, more names than the `int` has bits, or a name `None` |
| `unknown-name` | V10, F16 | a name that is not visible at this place, a top-level variable read inside a top-level function included |
| `type-mismatch` | T1, T13, T19, N6, C3 | a value of one type where another type is required |
| `missing-return` | F3 | a function with a result whose end can be reached without a `return`, or a bare `return` in one |
| `not-visible` | F9, A9, A15 | a member, type or function used where it is not visible; `print` of an instance, or an interpolation of one, whose `Text()` is not `pub`, from outside its class (A9); the same inside the class is A15's reading |
| `duplicate-function` | F12 | two functions of one name whose parameters do not differ |
| `constant-overflow` | T6, L10 | an expression of literals whose result leaves the range of its type, or a literal that fits no integer type |
| `constant-divide-by-zero` | T25 | a division or remainder of literals by the literal `0` |
| `narrowing-conversion` | T7, T28 | a wider integer type put into a narrower one without a conversion, whatever the signedness, the `int` of a promoted operation included |
| `sign-mix` | T9, T23 | an operation between a signed and an unsigned integer that C# joins through `long` or refuses (`uint` or `ulong` with a signed type), or a value put where a type of the other signedness and at least its width is required, into which it does not widen |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2, N3, N7, N9 | a nullable value used without a check |
| `mut-required` | M5, M7 | a `mut` method called on, or a `mut` argument taken from, something that is not `mut` |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it, `mut` in front of an argument whose parameter is not `mut`, or one variable passed as `mut` twice |
| `unlisted-case` | O3, O9 | a case that would leave a function whose return type does not list it |
| `match-not-exhaustive` | O8, C9, D12 | a `match` that lists neither every case nor an `else` arm |
| `braces-required` | C1 | a statement where a block between braces is required |
| `reference-cycle` | R2, R4, R8, R9 | a class that can reach itself through strong fields, one of them `mut` |
| `weak-not-nullable` | R5 | a `weak` type written without its `?` |
| `weak-value` | R5 | `weak` in front of a type whose values are not instances of a class |
| `raw-not-allowed` | R7 | a `raw` block in a package without the grant `allow raw`, or in a program without a project file |
| `semicolon` | L5 | a `;`: a statement ends where its line ends |
| `reserved-word` | L12, L18, L19 | a core word, a built-in type's name, or a keyword the file imports, used as a name |
| `capture-assign` | F11 | an assignment inside a lambda to a variable around it |
| `reversed-range` | C8 | a range between literals whose end lies below its start |
| `ambiguous-call` | F13 | a call that fits more than one function, none of them exactly |
| `cannot-infer` | T27, F7 | a call of a generic function whose type arguments are neither written nor inferable from its arguments |
| `equal-by-unknown` | K15 | an `equal by` clause that names something that is not a field of the class |
| `base-field-clash` | K17 | a parameter with a base field's name and another type, or with its name and type while the base receives something else |
| `no-text` | A4, A7, A10 | the text of an instance of a class that neither declares nor inherits a `Text()` (A9), of a value that holds one, or of a value whose interface or type parameter declares none |
| `block-indentation` | L13 | a line of a `"""` block that is indented less than the closing line |
| `throw-needs-value` | O7 | a bare `throw` anywhere but in an arm of `else` |
| `break-outside-loop` | C7 | `break` or `continue` with no loop around it, a lambda's body included |
| `unknown-escape` | L16 | a backslash before a character that starts no escape, or before `u`, `U` or `x` without the digits it takes |
| `static-state` | K18 | a `static mut` field, or a static field whose type is or holds a class |
| `override-without-virtual` | K7 | `override` on a method that no base class declares, or that the nearest base class declaring it marks neither `virtual` nor `override` |
| `hides-member` | K7 | a method of a derived class with the name and the parameter types, in order, of a base method it can see, without `override` |
| `no-primary-constructor` | K16 | the short form of inheritance against a base without a primary constructor |
| `constructor-must-chain` | K14 | a further constructor of a class with a primary constructor that does not call it |
| `field-unassigned` | K14 | a field without `mut` and without `=` that a constructor leaves unassigned on a path or reads first, or such a field in a class with a primary constructor |
| `syntax` | E4 | text that no rule gives a meaning: the C `for` with three parts, `else` on a line of its own, an arm after `else`, a call with a `void` success used as a value |

## Run-time errors

The exceptions a program can raise, as the header of a case names them. The line is the one that
holds the construct that raised: the `throw`, the arithmetic, the index, the call of the type, or
the range of the loop.

| id | rules | meaning |
|---|---|---|
| `thrown` | O5, O6, O7 | a `throw` statement of the program ran |
| `overflow` | T5, T20, T26, T29 | arithmetic that left the range of its type, or a conversion that lost its value, in a test build |
| `divide-by-zero` | T14, T25 | an integer division or remainder by zero |
| `index-out-of-range` | T14, M9 | an index outside the collection |
| `reversed-range-at-run-time` | C8 | a range whose end lay below its start when the loop reached it |

## The rules of this chapter

### E1 (assumed, §8) One error per case, with a line

A corpus case that expects an error expects exactly that one, reported on the named line of the file.
The line is the one that holds the offending construct. For `unused-variable` that is the
declaration; for `reference-cycle` it is the first declaration, in source order, of a class on
the path.

No case: every corpus case that expects an error shows it.

### E2 (decided, §8) The ids

An error is identified by a word, such as `unused-variable`. A corpus case pins the id and the line of
an error and never its message, which stays free to improve.

No case: every corpus case that expects an error names its id and its line.

### E3 (decided, §5, §8) How a case names the exception it expects

A corpus case that ends with an exception names it: `// expect: throws <id> at <line>`, with the id from
the run-time table above and the line counted as for a compile error. The lint checks both as it
checks a compile error's. So a corpus case for a reversed range (C8), an overflow in a test build (T5), a
conversion that loses its value (T20) or an index out of range (T14) passes only with that
exception from that line, and a `throw` of the program is `thrown` at the line of the `throw`
that raised; the bare `throw` of an `else` arm (O5) is such a `throw`, and nothing is raised a
second time, because nothing catches.
The program's exit code is pinned with it (O6). The owner chose this on 2026-10-03, against a
header that says only that something is thrown; the cost is a word the runtime carries in every
exception.

No case: every case that expects an exception names its id and its line.

### E4 (proposed) One id for text no rule gives a meaning

Several rules rule a construct out without naming its error: C8 the three-part `for`, C2 an
`else` on a line of its own, C9 an arm after `else`, D13 a `match` that lists a set of flags
case by case, O10 a call with a `void` success used as a
value. Each is the compile error `syntax`, one id for every text that no rule of this reference
gives a meaning, reported on the line where the text stops making sense. A more exact id can
replace it for a construct whose rule names one.

Case: [control/c-style-for.kz](../corpus/control/c-style-for.kz)
```kurz
for (i = 0; i < 3; i++) {
    print(i)
}
```
