# 12. Compile errors

The errors the cases expect. An id is what a corpus header names in `// expect: error <id> at
<line>`.

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read |
| `assign-immutable` | V7, V8, D4, K1, F8 | an assignment to a variable, a field or a parameter that is not `mut` |
| `redeclared` | V9 | a declaration with a written type, of a name that is already visible |
| `unknown-name` | V10 | a name that is not visible at this place |
| `type-mismatch` | T1, T13, T19, N6, C3 | a value of one type where another type is required |
| `constant-overflow` | T6 | an expression of literals whose result leaves the range of its type |
| `narrowing` | T7 | a wider integer type put into a narrower one without a conversion |
| `sign-mix` | T9 | an operation between a signed and an unsigned integer |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2, N3 | a nullable value used without a check |
| `mut-required` | M5, M7 | a change through something that is not `mut` |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it |
| `unlisted-case` | O3, O9 | a case that would leave a function whose return type does not list it |
| `match-not-exhaustive` | O8, C9, D12 | a `match` that lists neither every case nor an `else` arm |
| `braces-required` | C1 | a statement where a block between braces is required |
| `reference-cycle` | R2, R4, R8 | a class that can reach itself through strong fields, one of them `mut` |
| `semicolon` | L5 | a `;`: a statement ends where its line ends |
| `reserved-word` | L12 | a core word, or a keyword the file imports, used as a name |
| `capture-assign` | F11 | an assignment inside a lambda to a variable around it |

### E1 (assumed, §8) One error per case, with a line

A case that expects an error expects exactly that one, reported on the named line of the file.
The line is the one that holds the offending construct. For `unused-variable` that is the
declaration; for `reference-cycle` it is the first declaration, in source order, of a class on
the path.

No case: every case that expects an error shows it.

### E2 (decided, §8) The ids

An error is identified by a word, such as `unused-variable`. A case pins the id and the line of
an error and never its message, which stays free to improve.

No case: every case that expects an error names its id and its line.
