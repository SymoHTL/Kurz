# 12. Errors

The errors the corpus cases expect. An id is what a corpus header names: `// expect: error <id> at
<line>` for a compile error, `// expect: throws <id> at <line>` for an exception the program
raises when it runs (E3). The two tables share one namespace of ids. An id that no case can expect stands in its rule and not
here: `invalid-source` (L1), since the tools read every case as UTF-8, and `files-not-allowed`,
`network-not-allowed` and `env-not-allowed` (S12), since a case is a program and no package, and
`invalid-argument` (S13), since a case gives its program no argument; the three exceptions are
`print-failed` (A1), which has its row by the owner's choice (§6, 2026-10-21), `allocation-failed`
(O11), which has its row by the owner's choice (§6, 2026-10-23), and `random-failed` (M9), which has
its row by the owner's choice (§4, 2026-10-30), and each says in it why no case expects it. The lint
`md5-insecure` (H5) is a warning, which builds the program, and no error: it has no row, and no case
expects it (V12).

## Compile errors

| id | rules | meaning |
|---|---|---|
| `unused-variable` | V5, V6 | a variable that is never read, in a release build, where a test build warns (V5); a parameter and a loop variable are no variables for this (V13) |
| `assign-immutable` | V7, V8, D4, K1, F8, K14 | an assignment to a variable or a parameter that is not `mut`, into a path that starts at one (M4: for a `data` value the variable at the root decides, so `root.Left = x` through a `mut` variable is allowed), or to a field of a class instance that is not `mut`; inside a constructor a field without `mut` is assigned once (K14), and a second time is this error |
| `redeclared` | V9, F16, R16, T15, D18, D20, C6, S13, S14, H1, X7, H7 | a declaration, in any of its forms, of a name that is already visible, or inside a top-level function of the name of a variable at the outermost level of the top-level code, the program's `args` not counted as one (S13); a C declaration of a name that another function of the package, a type or another C declaration has, at the second of the two (R16); a file's own `InvalidUtf8` (T15), `Invalid` (D18) or `Entry` (C6), and its own type, function, variable or parameter `EnvError`, `EnvKind` or `Environment` at any level (S14); a value of an `enum` whose name an earlier value has, or named `Number` or `From` (D20); a variable `args` declared with its type at the outermost level of the top-level code, `List<string> args = ["a"]`, whose name the program's arguments take (`args = value` there stands open), and a lambda's parameter or a block's variable `args` within the top-level code, which sees it (S13); a file's own `Sha256`, `Md5`, `Crc32`, `Hmac`, `Hex` or `FixedTime` (H1); a file's own `ParseError` or `ParseKind` (X7), and `HexError` or `HexKind` (H7) |
| `argument-mismatch` | D11, F13, X1, X9 | a call with an argument whose name no parameter has, two arguments for one parameter, or none for a parameter without a default; a call of a name that has more than one function and fits none of them, at the call (F13), and a call of a string's search or of a list's `IndexOf`, members of two forms, that fits neither, `s.Contains(5)` among them (X1, X9) |
| `enum-number` | D20 | an `enum` that numbers two values alike or only some of its values, or with a number that is not a constant expression, and `.Number`, `From`, `<`, `<=`, `>` and `>=` on one that numbers none |
| `flags-number` | D19 | a flags name whose number is not one bit of its own, more names than the `int` has bits, or a name `None` |
| `unknown-name` | V10, F16, S13, E1 | a name that is not visible at this place, a top-level variable read inside a top-level function included, and `args` there, while a lambda among the top-level statements sees it (S13); a type argument that no declaration names, before the error of a call that does not take its arguments, `List<Nope>(1)` (E1) |
| `type-mismatch` | T1, T13, T19, N4, N5, N6, C3, O4, K5, C8, D9, T8, R17, D20, M9, C6, D3, M11, H6, X9, X8, T18 | a value of one type where another type is required, `??`, `?.` or `?[` on a value whose type is not nullable (N4, N5), a written union type that lists only some of a call's cases (O4), a union where one is required whose first case its first case does not go to, or where one of its other cases goes to none of the cases after the first, or, being none of them, to two of them (D9, the case of two fits an assumption there), a value that is no case of a union and would widen into two of its cases, a literal among them by its own type where the union has two or more number cases (T8), or derive from two of them (D9), an arithmetic operator on two class instances (K5), a range end that is no integer (C8), the number of an `enum` value whose type is no integer (D20), a map's key of a type `==` does not take, a map among them (M9), two names in a `for` loop over a list or a range (C6, a proposed reading there), and `==` on two lists whose items `==` does not take (M9), on a `data` value that holds a map, a function value, a file, a socket (D3) or a hasher (H6), or on two hashers (H6), a list's `IndexOf` over items `==` does not take (X9), and a sort of a list whose item type or key type `<` does not order (X8), as an operator on two values it does not take, `true < false` among them, is (T18) |
| `missing-return` | F3 | a function with a result whose end can be reached without a `return`, or a bare `return` in one |
| `not-visible` | F9, A9, A15 | a member, type or function used where it is not visible; `print` of an instance, or an interpolation of one, whose `Text()` is not `pub`, from outside its class (A9); the same inside the class is A15's reading |
| `duplicate-function` | F12 | two functions of one name whose parameters do not differ |
| `constant-overflow` | T6, L10, L11, L17, T28, D20, M11 | an operation of a constant expression of an integer or `decimal` type whose result leaves the range of its type (T6), a literal that fits no integer type (L10), a literal outside the range of its type, a floating-point one (L11) included, or one that does not fit the type it takes, written for it (L10, L17) or the other operand's (L17, T28), and a written integer conversion of a constant whose value lies outside the target (T6), and the number of an `enum` value past the range of an `int` (D20); a fraction or exponent literal inside a written conversion keeps its own type (L17) |
| `constant-divide-by-zero` | T25 | an integer or `decimal` division or remainder by a constant zero, a literal or folded from literals |
| `narrowing-conversion` | T7, T28, C8, D20 | a wider integer type put into a narrower one without a conversion, whatever the signedness, the `int` of a promoted operation, the loop variable of a range of a wider type (C8) and the number of an `enum` value of a wider type (D20) included |
| `sign-mix` | T9, T23, C8, D20 | an operation between a signed and an unsigned integer, the two ends of a range (C8) included, that C# joins through `long` or refuses (`uint` or `ulong` with a signed type), or a value put where a type of the other signedness and at least its width is required, into which it does not widen, the number of an `enum` value of type `uint`, `2u` (D20), among them |
| `string-index` | T15 | an index applied to a string instead of to `.Bytes` or `.Chars` |
| `nullable-unchecked` | N2, N3, N7, N9, N10 | a nullable value used without a check, under `<`, `<=`, `>` or `>=` among them (N10) |
| `mut-required` | M5, M7, H3, S13, X8, X9 | a `mut` method called on, or a `mut` argument taken from, something that is not `mut`, the program's `args` among them (S13) |
| `mut-at-call` | M7 | an argument for a `mut` parameter without `mut` in front of it, `mut` in front of an argument whose parameter is not `mut`, or one variable passed as `mut` twice |
| `changed-while-held` | M7 | a read of what a call takes as `mut` (an argument after `mut`, the receiver of a `mut` method), of a value that holds it or of one it holds, whose type is not copied and whose value the expression still holds when the call runs, or when an assignment or a `mut` method in a statement of an `else` arm's block inside the expression changes it; neither a read of the receiver of `Add`, or of another `mut` method that stores what its argument gave before it changes its receiver, `AddRange` among them, in its own argument, against that method's change of it, unless a lambda that the argument's value can keep makes the read, nor a second argument after `mut`, which is `mut-at-call`; at the line of what changes it, once for each change |
| `unlisted-case` | O1, O3, O9 | a case that would leave a function whose return type does not list it after its first case, the success (O1); a return type that is no union lists none; a case that is none of the cases after the first and would go to two of them (O3) |
| `match-not-exhaustive` | O8, C9, D12 | a `match` that lists neither every case nor an `else` arm |
| `braces-required` | C1 | a block that a construct requires, replaced by a statement or opened on the next line, at the line where the block should have opened (C1); a method signature inside a type body with its `{` on the next line is `syntax` instead |
| `reference-cycle` | R2, R4, R8, R9 | a class that can reach itself through strong fields, one of them `mut` |
| `weak-not-nullable` | R5 | a `weak` type written without its `?` |
| `weak-value` | R5 | `weak` in front of a type whose values are not instances of a class |
| `raw-not-allowed` | R7 | a `raw` block in a package without the grant `allow raw`, or in a program without a project file, but for a case of the corpus whose header line grants it |
| `raw-required` | R10, R11, R13, R16, R17 | an `asm`, or a read or a write through a raw pointer (R10), outside a `raw` block and outside the body of a `naked` function; a read or a write of a top-level `static` (R13), a member that shows the memory of a string or a list, `string.Allocate` or `List<T>.Allocate` (R15), or a call of a function that a block of C functions declares or through a C function type (R16, R17), outside a `raw` block |
| `asm-operand` | R11 | a breach of the rules of an `asm`'s template and its operands: an operand the template does not use, but one in an explicit register, which the template names nowhere, a name in the template that no operand has, a word or a register class that is not one of Rust's, or a register class that the target the build compiles for does not have |
| `counted-pointee` | R10, R14 | a pointer to a class, a list, a string or a `data` value that holds counted references, `&x` of a variable of such a type (R10), `SizeOf` of such a type, or a list's `Pointer` member, `List<T>.FromRaw` or `List<T>.Allocate` of such an element type (R15), or a pointer to any other type that holds a counted reference (R10) |
| `naked-not-allowed` | R12 | a `naked` function in a package without the grant `allow raw`, or in a program without a project file, but for a case of the corpus whose header line grants it; its body raises no `raw-not-allowed` beside it |
| `naked-body` | R12 | a body of a `naked` function other than one `asm`, or an operand of that `asm` other than `sym` and `const` |
| `native-not-allowed` | R16 | a block of C functions, or a function of the package's own in the C calling convention at its declaration, in a package without the grant `allow native`, or in a program without a project file, but for a case of the corpus whose header line grants it |
| `native-type` | R16 | a declaration of a block of C functions with a type that cannot cross to C or with a `mut` parameter, or an argument in the place of its `...` that is a `bool`, a `float` or a number smaller than an `int` |
| `semicolon` | L5 | a `;` that ends, starts or follows a statement: a statement ends where its line ends; a `;` anywhere else is `syntax` |
| `reserved-word` | L12, L18, L19 | a core word, a built-in type's name, or a keyword the file imports, used as a name |
| `capture-assign` | F11 | an assignment inside a lambda to a variable around it |
| `reversed-range` | C8, M9 | a range between literals whose end lies below its start, in a `for` loop or as the part of a list |
| `ambiguous-call` | F13 | a call that fits more than one function, none of them exactly |
| `cannot-infer` | T27, F7, M11 | a call of a generic function whose type arguments are neither written nor inferable from its arguments, two instances of different classes with one base in one parameter's positions included, and a list literal where no list type stands whose items give no one type, `[]` and `[1, "a"]` among them (M11) |
| `equal-by-unknown` | K15 | an `equal by` clause that names something that is not a field of the class |
| `equals-method` | K5 | a method named `Equals` or `GetHashCode` on a class, at the method; the message points to `equal by` (K8) |
| `mutable-key` | K12 | a class whose `equal by` clause names a `mut` field, as the key type of a `Map`, at the line that writes that type |
| `base-field-clash` | K16, K17 | a parameter with a base field's name and another type, in the explicit form (K17) or the short one (K16), or with its name and type while the base receives something else |
| `no-text` | A4, A7, A10, S5, X6 | the text of an instance of a class that neither declares nor inherits a `Text()` (A9), of a value that holds one, of a union with such a case or with a success without a value (A7), of a value whose interface or type parameter declares none, of a `File`, a `TcpListener` or a `TcpStream` (S5, §12, assumed), or of an item of the list `string.Join` takes (X6) |
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
| `syntax` | E4, L4, L5, L9, L11, L12, L13, L14, T21, K5, D13, D18, R12, R16, M11 | text that no rule gives a meaning: the C `for` with three parts, `else` on a line of its own, an arm after `else`, a call with a `void` success used as a value, a name with a character outside ASCII or an `@` name (L9), a suffix with a lower-case `l` or an integer suffix after a fraction or an exponent (L11), text after an opening `"""` or before a closing one, or `""""` at the end of a line (L13), a hexadecimal, binary, suffixed literal or one with an exponent before a unit (L14), anything but one code point in a `char` literal (T21), an operator declaration (K5), a `match` on a set of flags (D13), an `enum` called as a function, `Plan(2)` (D18), a `;` where no statement ends (L5), a core word where an expression, an untyped lambda parameter or the name of a named argument stands (L12), a trailing comma in the braces of `with` (L4), `..` before an item of a list literal, `[..xs]` (M11), `naked` before a method, a lambda or a function declared inside a block of code (R12), a block of C functions anywhere but at the top level of a file, inside a function, the body of a type or a block of code among them (R16) |
| `inexact-literal` | L14 | a decimal fraction before a unit that does not fall on a whole nanosecond, or on a whole byte before `kb`, `mb` and `gb` |
| `missing-member` | K7 | a class that names an interface and lacks one of its methods, a method inherited without `pub` not counting, at the class |
| `interface-method-private` | K7 | a method that implements a method of an interface the class names, without `pub`, at the method |
| `unreachable-arm` | C4, C9, D12, N1 | an arm of a `match` that can never run: an earlier arm covers its type, a literal arm follows the arm of its type, or `else` follows arms that cover every case (C9); an arm of an `enum` value that an earlier arm names or that follows the arm of its type, and the arm of the type after arms that name every value, and the arm of the type or `else` in a `match` on an `enum` without values (D12); a second `null` arm in one `match` (N1) |
| `override-return-type` | K7 | an `override` whose return type differs from the overridden method's, at the method |
| `default-not-constant` | F12 | a parameter's default value that is not a constant expression, at the parameter |
| `base-cycle` | D6 | a `data` type that is its own base, directly or through others, at the base that closes the cycle: the base of the declaration that completes it where the files of the program are read in the order of their paths, each relative to the program's root directory with `/` as the separator, compared code point by code point, case kept; that each file is read from its top, that a type that names itself as its base is a cycle of one, that a cycle is one error and that a type that only derives from a cycle gets none are assumptions there |

## Run-time errors

The exceptions a program can raise, as the header of a case names them. The line is the one that
holds the construct that raised: the `throw`, the arithmetic, the index, the call of the type, or
the range of the loop. Every id of the table but `stack-overflow`, `print-failed`,
`allocation-failed` and `random-failed` is raised by the program's code, a check or a `throw`, with its file and its line
as constants (§6, an assumption there as to that list and the file), or in a test build by a check of
the runtime's own code (below); `print-failed` is raised by the runtime with the file and the line its
`print` passed as constants (A1; §6, the owner, 2026-10-23), and `allocation-failed` and
`random-failed` name no place (O11, M9). A stack overflow, which is, but for a check of the runtime's own code in a test build (below),
the one exception a primitive of the runtime raises with a line it reads from the tables, at the call
that did not fit or at a primitive's entry check, names no line in a release build built without its line
tables (O11; §6 for the primitive's raise, an assumption there), and in either build where the stack
could not be walked, the tables could not be read or the raising frame has no entry in them, each with
its note in place of the line, the first the crash path meets where more than one holds: the walk's,
the file's, the tables', then the entry's, so that a Windows release build built without `--lines`,
whose PDB holds names and no lines, writes the note that names `--lines`, and a program whose PDB is
missing writes the note that the tables could not be read (§6, the words of the four notes decided there, the owner, 2026-10-29,
where each is written and their order an assumption there). A raise by code
keeps its file and its line in each of these builds. In a test build, which keeps the program's
checks in the runtime's own code, its primitives (§2, decided there), a check of that code, on Linux
and on Windows, ends the process as a stack fault inside the allocator or the scheduler does (O11),
at the line of the innermost frame outside the runtime (§6, decided there); that the crash path
reads that line as it reads a primitive's, that the check is no raise by code, that it takes a
primitive's notes where that line is not known, and that it goes through the crash path to its error
line and its trace and ends the process there, with no `Reason` recorded, no policy run and no
abandoned frame walked, are assumptions there; what it does on the other targets is open (§14). On
those two systems, such a check that fires on the crash path itself, in the fault handler, the walk
of the stack for the line and the trace, the read of the line tables or the write of the error line
and the trace, ends the process with the exit code 1, the one line
`error: <id> (no line: raised in the crash path)` and no trace (§6, decided there); that the walk of
an actor's abandoned frames, the read of the map of counted pointers and the frees that follow it
count as the crash path too, a check that fires inside the allocator or the scheduler during those
frees staying open (§14), that the thread's flag of its crash path is set from the raise or the fault
to the moment the actor's frames are freed and again while the error line and the trace are written, that
the line is written on a line of its own, after a line break where the stream stands
inside a line already written, and that a check that fires while that line is written ends the
process with nothing more written, are assumptions there.
The error line names the file and the line where a line is known (§6, decided there). The
error line of a `throw` ends with a colon, and the text of the thrown value stands below it,
starting on the line below it, before the trace (§6, decided there), written as it is, its own line
breaks kept, and an empty text as an empty line (§6, an assumption there), the text made by the
crash path from the value's `Text()` while the actor that threw still exists, a `throw` crashing its
actor as no exception is caught (§5), and kept with its stack for that write (§6, decided there),
the first raising actor's text where one crash leads to another, and a raise while the value's
`Text()` runs ending the process with the exit code 1 and no trace, whatever the policies would have
chosen, its error line naming the file and the line that a throw or a check of the program's code
carries, with nothing below it, and being the fixed line above for a raise that carries none (§6,
assumptions there); the error line of `allocation-failed` names the size asked for and no place (O11;
§6, the owner, 2026-10-23); the error line of `random-failed` is `error: random-failed`, with no place
(M9; §4, assumed); the error line of `not-char-boundary` ends with a colon, and the position of the cut stands on the line
below it, as the text of a `throw` does (X10; §12, the owner, 2026-10-31, "Below, as throw text", the colon assumed); the
error line of every other id ends with its line, or with the note in its place (§6, decided there).

| id | rules | meaning |
|---|---|---|
| `thrown` | O5, O6, O7 | a `throw` statement of the program ran |
| `overflow` | T5, T20, T26, T29, T30, T31 | arithmetic that left the range of its type, or a conversion that lost its value, in a test build; a `decimal` result beyond its range and a conversion into `decimal` that loses the value, in every build (T30, T26), and so do `+`, `-` and the unary `-` on the time types beyond their range and a conversion into the narrow time pair that loses the value (T31) |
| `divide-by-zero` | T14, T25 | an integer or `decimal` division or remainder by zero |
| `index-out-of-range` | T14, M9, S3, X2 | an index outside the collection, a part of a list whose start lies below 0 or whose end lies past the count, a read of a file or a socket for a count below 0 (S3, §12, assumed), or a `Substring` that reaches outside its string (X2) |
| `reversed-range-at-run-time` | C8, M9 | a range whose end lay below its start when the loop or the part of a list reached it |
| `stack-overflow` | O11 | the stack of the actor is exhausted, on Linux and on Windows (on WebAssembly the program ends, §2, and on the ESP32 models and AVR §6 says what an overflow does); the line is the line of the call that did not fit, read from the line tables (§6, decided there as to the tables), the innermost frame outside the runtime where that one was the runtime's (§6, the owner, 2026-10-21), a line of the standard library's source where that frame's function was the library's, which the error line's file and the trace's first line name; every case that pins it pins a call of the program's own, with a recursion that reaches neither the standard library nor the runtime, kept as calls by the test build (the call confirmed by the owner on 2026-10-10, as O11 cites it; the library's source and the condition on the cases §6, an assumption there), and a release build built without its line tables names none (§2, the answer decided there, its reading an assumption) |
| `print-failed` | A1 | a `print` whose write call returned an error, a closed pipe, whose signal the runtime sets to ignored at its start (§6, an assumption there), a closed handle or a full disk, at the file and the line of the `print`, which the `print` passes, in every build (§6, decided there as to the id and the row, the owner, 2026-10-21, and as to the line, the owner, 2026-10-23) (no case: a case has no form that makes a write of standard output fail) |
| `allocation-failed` | O11, T15, M9, R15, S3, S6 | an allocation the system refused, or an operation that would make a string of more bytes, or a list of more items, than the largest `int`, `string.Allocate`, `List<T>.Allocate`, a read of a file or a socket and a read of a whole file among them (S3, S6) (§4, the owner, 2026-10-23 and 2026-10-24; §3, assumed for `string.Allocate` and `List<T>.Allocate`); the error line is `error: allocation-failed (<n> bytes)`, with the size asked for, for a list past the limit the bytes of its items, the count of its items after the operation times the item's size as M9 counts it, padding included, and for a growth the system refuses the bytes the allocator was asked for, the whole request with the list's own counts, before the allocator rounds it up (§4, the owner, 2026-10-24), and for a string's block the system refuses the bytes the allocator was asked for, the 16 of the string's counts among them (§4, assumed), and no place and no trace (§6, the owner, 2026-10-23) (no case: a case cannot make memory run out, and a string over the limit takes 2 GiB) |
| `not-char-boundary` | X2 | a `Substring` whose start or end falls inside a code point of its string; the error names the position (§12, the owner, 2026-10-31, "Two ids"), on the line below the error line, as the text of a `throw` stands (X10) |
| `random-failed` | M9 | the system gave no randomness for the keys of a map's hash when a thread's first map takes its first block, on Linux neither `getrandom` nor, where the system refuses that call or its pool is not ready, `/dev/urandom`, which ends the program there, where Rust's standard library panics for want of a seed for a `HashMap` (§4, the owner, 2026-10-30, "End with an error, as Rust" and "Rust's full path"); on Windows `ProcessPrng` gives the keys, which does not fail (§4, the owner, 2026-10-30, "ProcessPrng, as Rust 1.82"), and the error comes only where its library or the call is missing, on a system older than Windows 8 or Windows Server 2008 R2, the floor Microsoft's page of the call names (§4, assumed); the id is the name the owner's question gave as an example, and the error line is `error: random-failed`, with the exit code 1 and no place and no trace, as the line of `allocation-failed` (§4, assumed) (no case: a case cannot make the system refuse its randomness; `ListTests` runs a map on a runtime whose `Random` reports a failure) |

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
the file, E4 names the line. A call with a type argument that no declaration names and an argument
that does not fit, `List<Nope>(1)`, reports `unknown-name` at the type and nothing more (§8, the
owner, 2026-10-31, "unknown-name first").

Every corpus case that expects an error shows the rule; the case below shows the order of two errors
in one call.

Case: [values/list-of-unknown-type.kz](../corpus/values/list-of-unknown-type.kz)
```kurz
// `Nope` names no type, and `List<T>()` takes no argument: the unknown type is reported, and nothing more
xs = List<Nope>(1)
print(xs)
```

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
line (§6, where its reading for a case is an assumption) is words to a case: no case pins it, and
the corpus runner holds the trace of a case it runs on Linux to §6 alone, its first frame at the
error line's place in a test build and its last `main`, or, where the trace is cut, a hundred frames
above the cut line, and never to the case *(assumed: the reading of this rule
for the trace, since the header names an id and a line and nothing of a trace, proposed with the
record of round 16 and not put to the owner, and the runner's check of the trace, an assumption of
§6)*.
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

Case: [values/list-literal-spread.kz](../corpus/values/list-literal-spread.kz)
```kurz
xs = [1, 2]
ys = [..xs, 3]
print(ys.Count)
```
