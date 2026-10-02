# 12. Compile errors

The errors the cases expect. An id is what a corpus header names in `// expect: error <id> at
<line>`.

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read |
| `assign-immutable` | V7, V8, D4, K1, F8 | an assignment to a variable, a field or a parameter that is not `mut` |
| `redeclared` | V9 | a declaration with a written type, of a name that is already visible |
| `unknown-name` | V10 | a name that is not visible at this place |
| `type-mismatch` | T1, N6, C3 | a value of one type where another type is required |
| `constant-overflow` | T6 | an expression of literals whose result leaves the range of its type |
| `narrowing` | T7 | a wider integer type put into a narrower one without a conversion |
| `sign-mix` | T9 | an operation between a signed and an unsigned integer |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2 | a nullable value used without a check |
| `mut-required` | M5, M7 | a change through something that is not `mut` |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it |
| `unlisted-case` | O3, O9 | a case that would leave a function whose return type does not list it |
| `match-not-exhaustive` | O8 | a `match` that does not list every case |
| `braces-required` | C1 | a statement where a block between braces is required |
| `reference-cycle` | R2, R4 | a class that can reach itself through strong fields, one of them `mut` |

### E1 (proposed) One error per case, with a line

A case that expects an error expects exactly that one, reported on the named line of the file.
The line is the one that holds the offending construct. For `unused-variable` that is the
declaration; for `reference-cycle` it is the first declaration, in source order, of a class on
the path.

No case: every case that expects an error shows it.

### E2 (open) The ids

The ids above are words, chosen for this corpus. Options: (a) words as here; (b) numbers with a
prefix, as C# has (`KZ0012`), which are stable when a message is reworded and easy to search for;
(c) both, a number with a word as its name. Also open: whether an error message is part of the
conformance corpus or only its id and line. Lean: (a) and the id and line only: a word says what
is wrong without a lookup, and a message has to stay free to improve.
