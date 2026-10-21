# 12. Errors

The errors the corpus cases expect. An id is what a corpus header names: `// expect: error <id> at
<line>` for a compile error, `// expect: throws <id> at <line>` for an exception the program
raises when it runs (E3). The two tables share one namespace of ids. An id that no case can expect stands in its rule and not
here: `invalid-source` (L1), since the tools read every case as UTF-8; the one exception is
`print-failed` (A1), which has its row by the owner's choice (§6, 2026-10-21) and says in it why no case
expects it.

## Compile errors

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read; a parameter and a loop variable are no variables for this (V13) |
| `assign-immutable` | V7, V8, D4, K1, F8, K14 | an assignment to a variable or a parameter that is not `mut`, into a path that starts at one (M4: for a `data` value the variable at the root decides, so `root.Left = x` through a `mut` variable is allowed), or to a field of a class instance that is not `mut`; inside a constructor a field without `mut` is assigned once (K14), and a second time is this error |
| `redeclared` | V9, F16 | a declaration, in any of its forms, of a name that is already visible, or inside a top-level function of the name of a variable at the outermost level of the top-level code |
| `argument-mismatch` | D11, F13 | a call with an argument whose name no parameter has, two arguments for one parameter, or none for a parameter without a default; a call of a name that has more than one function and fits none of them, at the call (F13) |
| `enum-number` | D20 | an `enum` that numbers two values alike or only some of its values, or `.Number` and `From` on one that numbers none |
| `flags-number` | D19 | a flags name whose number is not one bit of its own, more names than the `int` has bits, or a name `None` |
| `unknown-name` | V10, F16 | a name that is not visible at this place, a top-level variable read inside a top-level function included |
| `type-mismatch` | T1, T13, T19, N6, C3, O4, K5, C8, D9, T8, R17 | a value of one type where another type is required, a written union type that lists only some of a call's cases (O4), a union where one is required whose first case its first case does not go to, or where one of its other cases goes to none of the cases after the first, or, being none of them, to two of them (D9, the case of two fits an assumption there), a value that is no case of a union and would widen into two of its cases, a literal among them by its own type where the union has two or more number cases (T8), or derive from two of them (D9), an arithmetic operator on two class instances (K5) and a range end that is no integer (C8) included |
| `missing-return` | F3 | a function with a result whose end can be reached without a `return`, or a bare `return` in one |
| `not-visible` | F9, A9, A15 | a member, type or function used where it is not visible; `print` of an instance, or an interpolation of one, whose `Text()` is not `pub`, from outside its class (A9); the same inside the class is A15's reading |
| `duplicate-function` | F12 | two functions of one name whose parameters do not differ |
| `constant-overflow` | T6, L10, L11, L17, T28 | an operation of a constant expression of an integer or `decimal` type whose result leaves the range of its type (T6), a literal that fits no integer type (L10), a literal outside the range of its type, a floating-point one (L11) included, or one that does not fit the type it takes, written for it (L10, L17) or the other operand's (L17, T28), and a written integer conversion of a constant whose value lies outside the target (T6); a fraction or exponent literal inside a written conversion keeps its own type (L17) |
| `constant-divide-by-zero` | T25 | an integer or `decimal` division or remainder by a constant zero, a literal or folded from literals |
| `narrowing-conversion` | T7, T28, C8 | a wider integer type put into a narrower one without a conversion, whatever the signedness, the `int` of a promoted operation and the loop variable of a range of a wider type (C8) included |
| `sign-mix` | T9, T23, C8 | an operation between a signed and an unsigned integer, the two ends of a range (C8) included, that C# joins through `long` or refuses (`uint` or `ulong` with a signed type), or a value put where a type of the other signedness and at least its width is required, into which it does not widen |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2, N3, N7, N9 | a nullable value used without a check |
| `mut-required` | M5, M7 | a `mut` method called on, or a `mut` argument taken from, something that is not `mut` |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it, `mut` in front of an argument whose parameter is not `mut`, or one variable passed as `mut` twice |
| `unlisted-case` | O1, O3, O9 | a case that would leave a function whose return type does not list it after its first case, the success (O1); a return type that is no union lists none; a case that is none of the cases after the first and would go to two of them (O3) |
| `match-not-exhaustive` | O8, C9, D12 | a `match` that lists neither every case nor an `else` arm |
| `braces-required` | C1 | a block that a construct requires, replaced by a statement or opened on the next line, at the line where the block should have opened (C1); a method signature inside a type body with its `{` on the next line is `syntax` instead |
| `reference-cycle` | R2, R4, R8, R9 | a class that can reach itself through strong fields, one of them `mut` |
| `weak-not-nullable` | R5 | a `weak` type written without its `?` |
| `weak-value` | R5 | `weak` in front of a type whose values are not instances of a class |
| `raw-not-allowed` | R7 | a `raw` block in a package without the grant `allow raw`, or in a program without a project file, but for a case of the corpus whose header line grants it |
| `raw-required` | R10, R11, R13, R16, R17 | an `asm`, or a read or a write through a raw pointer (R10), outside a `raw` block and outside the body of a `naked` function; a read or a write of a top-level `static` (R13), a member that shows the memory of a string or a list (R15), or a call of a C function or through a C function type (R16, R17), outside a `raw` block |
| `asm-operand` | R11 | a breach of the rules of an `asm`'s template and its operands: an operand the template does not use, a name in the template that no operand has, or a word or a register class that is not one of Rust's |
| `counted-pointee` | R10, R14 | a pointer to a class, a list, a string or a `data` value that holds counted references, `&x` of a variable of such a type (R10), `SizeOf` of such a type, or a list's `Pointer` member or `List<T>.FromRaw` of such an element type (R15) |
| `naked-not-allowed` | R12 | a `naked` function in a package without the grant `allow raw`, or in a program without a project file, but for a case of the corpus whose header line grants it; its body raises no `raw-not-allowed` beside it |
| `naked-body` | R12 | a body of a `naked` function other than one `asm`, or an operand of that `asm` other than `sym` and `const` |
| `native-not-allowed` | R16 | a block of C functions in a package without the grant `allow native`, or in a program without a project file, but for a case of the corpus whose header line grants it |
| `native-type` | R16 | a declaration of a block of C functions with a type that cannot cross to C, or an argument in the place of its `...` that is a number smaller than an `int`, a `bool` or a `float` |
| `semicolon` | L5 | a `;` that ends, starts or follows a statement: a statement ends where its line ends; a `;` anywhere else is `syntax` |
| `reserved-word` | L12, L18, L19 | a core word, a built-in type's name, or a keyword the file imports, used as a name |
| `capture-assign` | F11 | an assignment inside a lambda to a variable around it |
| `reversed-range` | C8 | a range between literals whose end lies below its start |
| `ambiguous-call` | F13 | a call that fits more than one function, none of them exactly |
| `cannot-infer` | T27, F7 | a call of a generic function whose type arguments are neither written nor inferable from its arguments, two instances of different classes with one base in one parameter's positions included |
| `equal-by-unknown` | K15 | an `equal by` clause that names something that is not a field of the class |
| `base-field-clash` | K16, K17 | a parameter with a base field's name and another type, in the explicit form (K17) or the short one (K16), or with its name and type while the base receives something else |
| `no-text` | A4, A7, A10 | the text of an instance of a class that neither declares nor inherits a `Text()` (A9), of a value that holds one, or of a value whose interface or type parameter declares none |
| `block-indentation` | L13 | a line of a `"""` block that is indented less than the closing line |
| `throw-needs-value` | O7 | a bare `throw` anywhere but in an arm of `else` |
| `break-outside-loop` | C7 | `break` or `continue` with no loop around it, a lambda's body included |
| `unknown-escape` | L16 | a backslash before a character that starts no escape, or before `u`, `U` or `x` without the digits it takes |
| `static-state` | K18, R13 | a `static mut` field, or a static field whose type is or holds a class, in every package (K18); a top-level `static mut` or `thread static mut` in a package without the grant `allow raw`, of a type a pointer cannot point to, without an initial value or with one that is no constant, or with `pub` before it (R13) |
| `override-without-virtual` | K7 | `override` on a method that no base class declares, or that the nearest base class declaring it marks neither `virtual` nor `override` |
| `hides-member` | K7 | a method of a derived class with the name and the parameter types, in order, of a base method it can see, without `override` |
| `no-primary-constructor` | K16 | the short form of inheritance against a base without a primary constructor |
| `constructor-must-chain` | K14 | a further constructor of a class with a primary constructor that does not call it |
| `field-unassigned` | K14 | a field without `=`, `mut` or not, that a constructor leaves unassigned on a path or reads before its first assignment, or such a field in a class with a primary constructor |
| `syntax` | E4, L4, L5, L9, L11, L12, L13, L14, T21, K5, D13 | text that no rule gives a meaning: the C `for` with three parts, `else` on a line of its own, an arm after `else`, a call with a `void` success used as a value, a name with a character outside ASCII or an `@` name (L9), a suffix with a lower-case `l` or an integer suffix after a fraction or an exponent (L11), text after an opening `"""` or before a closing one, or `""""` at the end of a line (L13), a hexadecimal, binary, suffixed literal or one with an exponent before a unit (L14), anything but one code point in a `char` literal (T21), an operator declaration (K5), a `match` on a set of flags (D13), a `;` where no statement ends (L5), a core word where an expression, an untyped lambda parameter or the name of a named argument stands (L12), a trailing comma in the braces of `with` (L4) |
| `inexact-literal` | L14 | a decimal fraction before a unit that does not fall on a whole nanosecond, or on a whole byte before `kb`, `mb` and `gb` |
| `missing-member` | K7 | a class that names an interface and lacks one of its methods, a method inherited without `pub` not counting, at the class |
| `interface-method-private` | K7 | a method that implements a method of an interface the class names, without `pub`, at the method |
| `unreachable-arm` | C4, C9 | an arm of a `match` that can never run: an earlier arm covers its type, a literal arm follows the arm of its type, or `else` follows arms that cover every case (C9) |
| `override-return-type` | K7 | an `override` whose return type differs from the overridden method's, at the method |
| `default-not-constant` | F12 | a parameter's default value that is not a constant expression, at the parameter |
| `base-cycle` | D6 | a `data` type that is its own base, directly or through others, at the base that closes the cycle: the base of the declaration that completes it where the files of the program are read in the order of their paths, each relative to the program's root directory with `/` as the separator, compared code point by code point, case kept; that each file is read from its top, that a type that names itself as its base is a cycle of one, that a cycle is one error and that a type that only derives from a cycle gets none are assumptions there |

## Run-time errors

The exceptions a program can raise, as the header of a case names them. The line is the one that
holds the construct that raised: the `throw`, the arithmetic, the index, the call of the type, or
the range of the loop; a stack overflow, or an exception a primitive of the runtime raises, which is
`print-failed` or a `stack-overflow` at a primitive's entry check, every other id of the table being
raised by the program's code, a check or a `throw`, with its file and its line as constants (§6, an
assumption there as to that list and the file), names no line in a release build built without its line
tables (O11; §6 for the primitive's raise, an assumption there), and in either build where the stack
could not be walked, the tables could not be read or the raising frame has no entry in them, each with
its note in place of the line, the first the crash path meets where more than one holds: the walk's,
the file's, the tables', then the entry's, so that a Windows release build built without `--lines`,
whose PDB holds names and no lines, writes the note that names `--lines`, and a program whose PDB is
missing writes the note that the tables could not be read (§6, an assumption there). A raise by code
keeps its file and its line in each of these builds. The error line names the file and the line where
a line is known (§6, decided there).

| id | rules | meaning |
|---|---|---|
| `thrown` | O5, O6, O7 | a `throw` statement of the program ran |
| `overflow` | T5, T20, T26, T29 | arithmetic that left the range of its type, or a conversion that lost its value, in a test build |
| `divide-by-zero` | T14, T25 | an integer or `decimal` division or remainder by zero |
| `index-out-of-range` | T14, M9 | an index outside the collection |
| `reversed-range-at-run-time` | C8 | a range whose end lay below its start when the loop reached it |
| `stack-overflow` | O11 | the stack of the actor is exhausted, on Linux and on Windows (on WebAssembly the program ends, §2, and on the ESP32 models and AVR §6 says what an overflow does); the line is the line of the call that did not fit, read from the line tables (§6, decided there as to the tables), the innermost frame outside the runtime where that one was the runtime's (§6, the owner, 2026-10-21), a line of the standard library's source where that frame's function was the library's, which the error line's file and the trace's first line name; every case that pins it pins a call of the program's own, with a recursion that reaches neither the standard library nor the runtime, kept as calls by the test build (the call confirmed by the owner on 2026-10-10, as O11 cites it; the library's source and the condition on the cases §6, an assumption there), and a release build built without its line tables names none (§2, the answer decided there, its reading an assumption) |
| `print-failed` | A1 | a `print` whose write call returned an error, a closed pipe, whose signal the runtime sets to ignored at its start (§6, an assumption there), a closed handle or a full disk, at the line of the `print` and with none in a release build built without its line tables (§6, decided there as to the id and the row, an assumption there as to the line) (no case: a case has no form that makes a write of standard output fail) |

## The rules of this chapter

### E1 (assumed, §8) One error per case, with a line

A corpus case that expects an error expects exactly that one, reported on the named line of the
file. The line is the one that holds the offending construct. For `unused-variable` that is the
declaration; for `reference-cycle` it is the first declaration, in source order, of a class on the
path. A construct over several lines reports the line of the smallest part that is wrong: an
argument its own line, a binary operator its operator's line; only an error of the whole reports the
first line. *(assumed: proposed on 2026-10-09 and not objected to)* A block that is missing, or that
opens on the next line, is an error of the whole construct, reported where the block should have
opened: for an `if` the line of its condition (the owner, 2026-10-10, confirming the reading proposed
after the review of round 13), its last line when the condition runs over several lines (the owner, 2026-10-11, confirming the reading proposed after the review of round 14),
and for the other constructs the line C1 names; where the error is only the type checker's, or a closer is missing at the end of
the file, E4 names the line.

No case: every corpus case that expects an error shows it.

### E2 (decided, §8) The ids

An error is identified by a word, such as `unused-variable`. A corpus case pins the id and the line of
an error and never its message, which stays free to improve.

No case: every corpus case that expects an error names its id and its line.

### E3 (decided, §5, §6, §8) How a case names the exception it expects

A corpus case that ends with an exception names it: `// expect: throws <id> at <line>`, with the id from
the run-time table above and the line counted as for a compile error. The lint checks both as it
checks a compile error's. The error line names the file and the line (§6, decided there); a case pins the
line, and the runner checks that the error line names the case's own file, so that a raise that lands
in a library's source fails the case *(assumed: the runner's check of the file, proposed with the
record of round 16)*. So a corpus case for a reversed range (C8), an overflow in a test build (T5), a
conversion that loses its value (T20) or an index out of range (T14) passes only with that
exception from that line, and a `throw` of the program is `thrown` at the line of the `throw`
that raised; the bare `throw` of an `else` arm (O5) is such a `throw`, and nothing is raised a
second time, because nothing catches. The trace of the calls that the runtime writes after the error
line (§6, where its reading for a case is an assumption) is words to a case: no case pins it, and a
case passes with or without it *(assumed: the reading of this rule for the trace, since the header
names an id and a line and nothing of a trace, so that the runner reads the error line and nothing
after it, proposed with the record of round 16 and not put to the owner)*.
The program's exit code is pinned with it (O6). The owner chose this on 2026-10-03, against a
header that says only that something is thrown; the cost is a word the runtime carries in every
exception.

No case: every case that expects an exception names its id and its line.

### E4 (decided, §8) One id for text no rule gives a meaning

Several rules rule a construct out, some without naming its error and some naming this one: C8 the
three-part `for`, C2 an `else` on a line of its own, C9 an arm after `else`, D13 a `match` on a set
of flags, O10 a call with a `void` success used as a value, L9 a name with a character outside ASCII
or an `@` name, L11 a suffix with a lower-case `l` or an integer suffix after a fraction or an
exponent, L13 text after an opening `"""` or before a closing one or `""""` at the end of a line, L4 a trailing
comma in the braces of `with`, L14 a hexadecimal, binary or suffixed
literal or one with an exponent before a unit, T21 anything but one code point in a `char` literal,
`''` and `'''` included, K5 an operator declaration, L5 a `;` where no statement ends, L12 a core word
where an expression, an untyped lambda parameter or the name of a named argument stands. Each is the compile error `syntax`, one id for every text
that no rule of this reference gives a meaning, reported on the line where the text stops making
sense, the line of the first token no rule accepts, except for the three unclosed shapes below, whose
lines are named with them. Where the construct's rule names an id of its
own for that shape, that id is reported and not `syntax`; a shape a rule sends to `syntax` itself,
L5's `;` inside a construct or L12's core word in an expression, is `syntax`. The one id for a `match` on a set of flags and for the shapes of `"""` is the owner's choice of
2026-10-09, in round 13, in the answers on D13 and L13, against an id of its own for each; this rule
as a whole, which round 13 had not asked about, the owner confirmed on 2026-10-10, in round 14, with the token and the precedence of the rule's
own id, proposed after the review of round 13. An error that only the type checker can see, D13's `match` on a set of flags and
O10's call with a `void` success used as a value, is reported at the line of the construct (the owner,
2026-10-10). A `)`, `]` or `}` missing at the end of the file is reported at the last line that holds
code, a `"""` block that is never closed at its opening line, and an interpolation that no `}` closes
on its line at the line of its `{` (the owner, 2026-10-10).

Case: [source/unclosed-paren.kz](../corpus/source/unclosed-paren.kz)
```kurz
print((1 + 2)
    + 3
```

Case: [control/c-style-for.kz](../corpus/control/c-style-for.kz)
```kurz
for (i = 0; i < 3; i++) {
    print(i)
}
```
