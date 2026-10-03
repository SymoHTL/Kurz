# 12. Compile errors

The errors the cases expect. An id is what a corpus header names in `// expect: error <id> at
<line>`. The exceptions a program can raise when it runs have no ids yet: E3.

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read |
| `assign-immutable` | V7, V8, D4, K1, F8 | an assignment to a variable or a parameter that is not `mut`, into a path that starts at one (M4: for a `data` value the variable at the root decides, so `root.Left = x` through a `mut` variable is allowed), or to a field of a class instance that is not `mut` |
| `redeclared` | V9 | a declaration, in any of its forms, of a name that is already visible |
| `argument-mismatch` | D11 | a call with an argument whose name no parameter has, two arguments for one parameter, or none for a parameter without a default |
| `enum-number` | D20 | an `enum` that numbers two values alike or only some of its values, or `.Number` and `From` on one that numbers none |
| `flags-number` | D19 | a flags name whose number is not one bit of its own, more names than the `int` has bits, or a name `None` |
| `unknown-name` | V10 | a name that is not visible at this place |
| `type-mismatch` | T1, T13, T19, N6, C3 | a value of one type where another type is required |
| `missing-return` | F3 | a function with a result whose end can be reached without a `return`, or a bare `return` in one |
| `not-visible` | F9 | a member, type or function used where it is not visible |
| `duplicate-function` | F12 | two functions of one name whose parameters do not differ |
| `constant-overflow` | T6, L10 | an expression of literals whose result leaves the range of its type, or a literal that fits no integer type |
| `constant-divide-by-zero` | T25 | a division or remainder of literals by the literal `0` |
| `narrowing-conversion` | T7 | a wider integer type put into a narrower one without a conversion |
| `sign-mix` | T9 | an operation between a signed and an unsigned integer |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2, N3, N7, N9 | a nullable value used without a check |
| `mut-required` | M5, M7 | a `mut` method called on, or a `mut` argument taken from, something that is not `mut` |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it, `mut` in front of an argument whose parameter is not `mut`, or one variable passed as `mut` twice |
| `unlisted-case` | O3, O9 | a case that would leave a function whose return type does not list it |
| `match-not-exhaustive` | O8, C9, D12 | a `match` that lists neither every case nor an `else` arm |
| `braces-required` | C1 | a statement where a block between braces is required |
| `reference-cycle` | R2, R4, R8 | a class that can reach itself through strong fields, one of them `mut` |
| `weak-not-nullable` | R5 | a `weak` type written without its `?` |
| `weak-value` | R5 | `weak` in front of a type whose values are not instances of a class |
| `raw-not-allowed` | R7 | a `raw` block in a package without the grant `allow raw`, or in a program without a project file |
| `semicolon` | L5 | a `;`: a statement ends where its line ends |
| `reserved-word` | L12 | a core word, or a keyword the file imports, used as a name |
| `capture-assign` | F11 | an assignment inside a lambda to a variable around it |
| `reversed-range` | C8 | a range between literals whose end lies below its start |
| `ambiguous-call` | F13 | a call that fits more than one function, none of them exactly |
| `cannot-infer` | T27, F7 | a call of a generic function whose type arguments are neither written nor inferable from its arguments |
| `equal-by-unknown` | K15 | an `equal by` clause that names something that is not a field of the class |
| `base-field-clash` | K17 | a parameter with a base field's name and another type, or with its name and type while the base receives something else |
| `no-text` | A4, A7 | the text of an instance of a class that declares none, or of a value that holds one |
| `block-indentation` | L13 | a line of a `"""` block that is indented less than the closing line |
| `throw-needs-value` | O7 | a bare `throw` anywhere but in an arm of `else` |
| `break-outside-loop` | C7 | `break` or `continue` with no loop around it, a lambda's body included |
| `syntax` | E4 | text that no rule gives a meaning: the C `for` with three parts, `else` on a line of its own, an arm after `else`, a call with a `void` success used as a value |

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

### E3 (open) How a case names the exception it expects

`// expect: throws` says only that the program ends with an exception. A case for a reversed range
(C8), an overflow in a test build (T5), a conversion that loses its value (T20) or an index out of
range (T14) therefore passes with any exception thrown from any line, and the eight such cases
cannot tell a right compiler from a wrong one. The options:

- (a) Run-time errors get ids of their own in a second table of this chapter (`overflow`,
  `divide-by-zero`, `index-out-of-range`, `reversed-range`, `conversion-overflow`), the header
  becomes `// expect: throws <id> at <line>` with the line counted as for a compile error, and
  the lint checks the id and the line as it does for compile errors. The exit code of a program
  that ends with an exception (O6) is pinned with it. Cost: the lint and its cases change, and
  every id is one more word the runtime has to carry in its exceptions.
- (b) As now: the cases say that something is thrown, and the reviewer reads whether it is the
  right thing. Cost: the weakness above stays.

The lean is (a).

### E4 (proposed) One id for text no rule gives a meaning

Several rules rule a construct out without naming its error: C8 the three-part `for`, C2 an
`else` on a line of its own, C9 an arm after `else`, O10 a call with a `void` success used as a
value. Each is the compile error `syntax`, one id for every text that no rule of this reference
gives a meaning, reported on the line where the text stops making sense. A more exact id can
replace it for a construct whose rule names one.

Case: [control/c-style-for.kz](../corpus/control/c-style-for.kz)
```kurz
for (i = 0; i < 3; i++) {
    print(i)
}
```
