# Kurz language design

Status: brainstorm record, 2026-10-01 to 2026-10-10. Everything under "Decided" was chosen by the owner during the session. Anything marked *assumed* was proposed and not objected to, but never explicitly confirmed. A marker that carries the date of a review names a reading proposed after that review; it stands as assumed until the owner confirms or refuses it by number in a round. A marker *(proposed: ...)* inside a decided statement names a reading that the owner's answer did not reach; it is no decision, and a design round asks about it by the id of the reference rule that carries it. On 2026-10-02 the owner accepted, as one batch, the details the language reference had filled in to write its cases: the six that the question named are recorded as decided, the rest as *assumed*. Keyword spellings in code samples are illustrative unless listed in the syntax section.

## 1. Identity and goals

- Name: **Kurz**. Source files: `.kz`.
- Syntax in the spirit of C#.
- Main selling point: do as much as possible with as little code as possible.
- General purpose. Primary target: highly scalable, horizontally scalable backend systems.
- Games are a target, but the language gets no game-specific features.
- Should run on microcontrollers (Arduino, ESP32 class) without being designed for them. Consequence: the runtime is pay-per-use. A program that uses no actors links no scheduler.
- Compiled to native code, as fast as C, optimized for each target platform.
- Memory safety is proven by the compiler, not found by best-effort analysis. *(assumed)*

## 2. Toolchain

- The compiler emits LLVM IR directly. Going through C source is not wanted. LLVM as a build-time dependency is acceptable.
- LLVM covers x86, ARM, RISC-V, WebAssembly and AVR. Of the ESP32 family, the RISC-V models (C3, C6) work with upstream LLVM. The Xtensa models (classic ESP32, S2, S3) need Espressif's LLVM fork, because the upstream Xtensa backend is still experimental (checked 2026-10-01). The fork is acceptable where a project needs those models (confirmed by the owner on 2026-10-02).
- A compiled program never relies on another language, runtime or tooling. The whole standard library is written in Kurz.
- Kurz can call C-ABI functions (graphics, audio, operating system), but nothing in the standard library needs this beyond the operating system boundary.
- TLS follows the Rust approach: the protocol is implemented in Kurz, the cipher primitives come from an established library through the C-ABI (section 14 keeps open where they come from in the long run).
- Compilation is whole-program. The cycle rule (section 3), deadlock analysis (section 6), `mock` (section 11) and package permissions (section 13) depend on the compiler seeing all source at once.
- The package system is described in section 13.

## 3. Memory

- No lifetimes, no borrow annotations, no global garbage collector.
- Immutable by default. Immutable data cannot form reference cycles, so compiler-inserted reference counting is enough, and most counting is optimized away at compile time.
- Mutable state lives inside exactly one actor. Each actor has its own heap. When an actor dies its whole heap is freed at once. A big value that is shared between actors lives outside these heaps (section 6).
- There is **no cycle collector**. A possible reference cycle is a compile error.
  - The rule is judged by types, over the whole program: a class may not reach itself through strong fields when one field on that path is `mut`. Values cannot contain themselves (section 4), so the rule only ever applies to classes.
  - `weak` means "I point at this but do not keep it alive". A weak reference is nullable and becomes null when its target is freed. It is the same idea as `WeakReference<T>` in C#. A weak type always shows its `?`: `weak Node?` (the owner, 2026-10-02).
  - Between different types, `weak` on the back-pointer is enough: `Customer` holds its orders, `Order` points back `weak`. A class that reaches itself through a strong `mut` field is rejected even with a `weak` back-pointer, because `a.children.Add(b)` followed by `b.children.Add(a)` closes a ring through the children alone.
  - Only `mut` fields can close a cycle, so with immutable-by-default the error is rare.
  - A `mut` field of interface type could hold any implementer; the compiler needs the whole program to judge it. A field of function type, `mut` or not, is judged the same way: the lambda it holds can capture any class instance, so the field counts as reaching every class whose instance a lambda of the program captures, and the `mut` field that closes the ring can be the function field or any other field on the path; a button that stores a handler that reads the button is a cycle (the owner, 2026-10-03, against a weak capture inside stored lambdas and against forbidding such a capture; that a field without `mut` counts too follows from a ring the review showed through one, *(assumed)*). The cost is not local: once a class has a field of function type, any lambda anywhere in the program that captures an instance of that class, or of a class that holds one, makes it an error, and the lambda can sit in another file. An event handler escapes the error only in a program in which no lambda captures the button's class: through an interface (which must not hold the button), or by receiving the button as a parameter.
- A tree is a recursive `data` value. It is updated in place while nobody else holds it; when somebody holds an older version, an update copies the path from the root to the changed node. It has no parent pointers: the parent is passed down while walking.
- Objects with identity that point at each other (scene graph, UI tree, general graphs) have one flat owner; the edges are `weak` or indices. Each hop through a `weak` edge pays a small liveness check.
- A grid is a flat array and a chunked world is a map of chunk values; the rule does not touch either.
- Known trap: a write to one large flat array while a snapshot of it is still held copies the whole array once. Large data is chunked, and the compiler can warn where a write provably copies.
- **Lifetimes.** When an instance is freed follows from what holds it: a variable holds its instance until the end of the block that declares the variable, a field as long as its owner lives, a temporary until the end of its statement; the instance is freed when the last of these ends, and a `weak` reference to it is null from then on. *(assumed: proposed on 2026-10-03, against releasing a variable after its last use as Swift's ARC may, which would free an instance that a later line still reads through a `weak` reference)*
- **Raw memory.** Collections, the allocator and the scheduler are written in Kurz, so some Kurz code has to touch raw memory. That code sits in `raw` blocks (pointers, manual allocation). A `raw` block compiles only in a package that the project grants `allow raw` in `project.kz`. Any package can be granted it, the project's own code included: it is discouraged, not reserved for the standard library (the owner, 2026-10-01). The compiler's memory guarantees cover everything outside `raw` blocks, and the build lists every package that holds the grant. The grant is per package and written like `allow network` (confirmed by the owner on 2026-10-02).
- Considered and dropped on 2026-10-01: a single-owner rule in which a node sits in one place and changes place with `move`, and a collector that a class opts into by keyword. The collector could return later as an opt-in keyword without breaking code.
- Expected speed, taken from a language that uses the same technique; no Kurz measurement exists. Koka's purely functional red-black tree, updated in place this way, ran 42 million inserts within 10% of C++ `std::map` ([Perceus, MSR-TR-2020-42](https://www.microsoft.com/en-us/research/wp-content/uploads/2020/11/perceus-tr-v1.pdf)); a later paper measured it 19% faster on one CPU and about equal on another ([Frame Limited Reuse, MSR-TR-2021-30](https://www.microsoft.com/en-us/research/wp-content/uploads/2021/11/flreuse-tr.pdf)).

```
data Tree(int Key, Tree? Left, Tree? Right)

mut root = Tree(8, null, null)
root.Insert(3)                 // a `mut` method: walks down and writes in place
root.Key = 9

class Scene { mut Map<int, Node> nodes }      // owns every node
class Node {
    mut List<weak Node?> children
    mut weak Node? parent
}
```

## 4. Types and paradigm

- Static typing with inference.
- Immutable by default; `mut` marks what may change.
- **Values.** `data`, collections, strings and numbers are values (the owner chose this as "probably" on 2026-10-01; the cycle rule of section 3 and the `mut` parameters below were chosen afterwards and build on it):
  - Without `mut`, nothing reachable through a variable changes. With `mut`, it changes in place. The guarantee stops at a class instance: its `mut` fields change through every reference to it, whatever holds the reference (the class bullet below), so what a variable reaches through an instance is not covered. *(assumed: the qualification; it follows from the owner's choice on `mut` fields)*
  - Assigning or passing a value copies it in meaning. In practice the storage is shared until one side writes, and a write happens in place when nobody else holds the value. Where the compiler cannot prove that, a counter check precedes the write.
  - There is one family of collections, and every value can cross between actors.
  - Assigning into a path of a `mut` variable is short for nested `with`.
  - A method that changes its own value carries a `mut` marker and can be called only on a `mut` variable.
  - A function changes a caller's value only through a `mut` parameter, as `ref` does in C#, and the caller writes `mut` at the call as well. Without both, a method cannot change the collection it was handed.
  - Classes stay references with identity and change only through their own `mut` fields.

```
mut order = GetOrder(id)
order.Customer.Address.City = "Wien"     // short for three nested `with`, done in place
users[id].Name = name                    // the same inside a map held by an actor

void Fill(List<int> xs) { xs.Add(1) }          // compile error: xs is not mut
void Fill(mut List<int> xs) { xs.Add(1) }
Fill(mut numbers)                              // the caller sees and allows the change
```

- `data`: immutable records, equal by content. The fields compare as the fields of a C# record do: a value with a NaN field equals itself and is found again as a key of a map, while `==` on the field alone says false, as in C# (the owner, 2026-10-09, against the field's `==`, under which such a value is unequal to itself and cannot be found in a map). Each field compares by the equality Kurz gives its type, a class field as the `class` item below says, by identity, by content or by its named fields, and NaN equal to NaN is the one thing taken from C#'s `Equals`; a method a class names `Equals` plays no part (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A `data` type may inherit from another `data` type. Unions (`Circle | Square`) are available as well.
- `class`: may have `mut` fields, single inheritance plus interfaces as in C#. Classes are the tool for big inheritance trees. A class with `mut` fields compares by identity; an immutable class compares by content. A class can replace that by naming the fields that count: two instances are then equal when those fields are equal, and the hash is derived from the same fields (the owner, 2026-10-02). A `double` field among them compares as a field of a `data` value does, NaN equal to NaN (the owner, 2026-10-10). This replaces "equality is overridable, as in C#": C# overrides `Equals` of `object`, and Kurz has no type that every value belongs to. The cost: a class cannot bring comparison logic of its own, such as a name compared without regard to case. There are no operator declarations: `+` on two instances is a method such as `a.Add(b)`, and a method named `Equals` is an ordinary member that changes nothing about `==` (the owner, 2026-10-09, against C#'s operator declarations with the comparisons excluded; the cost is that a vector type reads as `a.Add(b).Scale(2)`). `a + b` written on two instances is the error for a value of one type where another is required, the error of an arithmetic operator on operands it has no meaning for (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). The fields are named in a clause after the head of the class: `class User(int Id, mut string Name) equal by Id` (the owner, 2026-10-02, against a marker word in front of each field). `equal` and `by` become core words with it. The clause names one or more fields with `, ` between them, stands after the primary constructor and the base part and before a body, may name a field of the primary constructor, of the body or of the base, and a name that is no field of the class is the compile error `equal-by-unknown`. *(assumed: the reference's reading K15, accepted by its id on 2026-10-09 without its text shown)* A `mut` field can be named in the clause (the owner, 2026-10-02, against the lean to refuse it); a class without the clause is not touched by that and compares as before. *(assumed: this reading of the owner's answer, "yes if the equal thingy is overridden otherwise no")* The cost of a `mut` field that counts: when it is assigned, the hash of the instance changes, so an instance that is a key of a map at that moment is no longer found under it, and nothing reports that. Across inheritance: instances of different classes are never equal, whatever their fields hold; the fields of the base count as fields of the class when `mut` decides between identity and content; and the clause of the base is inherited, a clause of the derived class adding its fields to the base's (the owner, 2026-10-03, by choosing the option whose contrast was a derived clause "replacing instead of extending" the base's; against C#'s record equality, which compares the run-time types and every field with a replacing clause, and against equality by the fields of the static type, under which `a == b` and `b == a` can differ). The cost: a base-typed collection cannot find an instance by a base-typed key.
- `actor`: the concurrent unit (section 6). Actors implement interfaces but do not inherit from each other.
- Small `data` values are compiled as plain values without reference counts, automatically; there is no separate `struct` keyword. *(assumed)*
- Numbers use C# names and sizes. The integer types are C#'s set, signed and unsigned: `sbyte`, `byte`, `short`, `ushort`, `int` (32-bit), `uint`, `long` (64-bit), `ulong` (the owner, 2026-10-02; the first list had `byte`, `short`, `int` and `long` only). The others are `float`, `double` and `decimal`. C#'s `nint` and `nuint` are not in the set: a size or an index is an `int` or a `long` (the owner, 2026-10-09, against pointer-sized types whose width follows the target, 32 bits on a device and 64 on a server, under which a constant's overflow would depend on the target and a value of that type could not be sent between machines without naming a width; the cost is that ported interop code writes `long`).
- Integer overflow wraps silently in release builds. Around that:
  - Compile errors for constant expressions that overflow, implicit narrowing (`long` into `int`) and mixing `uint` or `ulong` with a signed type (below). A constant expression under `+%`, `-%` or `*%` folds to the wrapped value instead, as the operator promises for every value, `2147483647 +% 1` to `-2147483648`; a conversion written on a constant that does not fit its target, `byte(300)`, is the overflow error, as C# refuses a constant cast that does not fit (the owner, 2026-10-09, against both being the error, under which a checksum written on literals does not compile, and against both computing, under which `byte(300)` is `44` and a value is lost without a word; the cost is a constant folder that knows two kinds of operator). An overflowing constant expression is the error for the integer types and `decimal` alone: one of `float` or `double` folds to the value IEEE 754 gives it, an infinity or NaN included, as C# folds it. A literal whose value lies outside the range of the type it takes is the error whatever that type, a duration (below) included: `1e400` or `3.5e38f` is the error, as C# refuses such a literal, and one that rounds to zero or to a subnormal is that value, as there. A constant fits the target of a written integer conversion when its value, the fraction dropped, lies in the target's range: `int(2.5)` is `2`, and `int(1e10)`, `int(1.0 / 0.0)` and a NaN are the overflow error (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A written conversion to a floating-point type folds as IEEE 754 has it, to an infinity beyond the range and with rounding, never the error (the owner, 2026-10-10).
  - Durations and timestamps are their own 64-bit types, never raw integers. This removes the bug class where an uptime counter overflows after weeks of running. Their names are `duration` and `timestamp`, in lower case like the other built-in types, and one step of their 64 bits is one nanosecond, which holds about 292 years of duration and, counted from 1970-01-01 *(assumed: the zero point)*, timestamps from 1677 to 2262 (the owner, 2026-10-03, against capitalized names like the types of the library, a step of one millisecond and .NET's tick of 100 nanoseconds). The wider variant the owner asked for with that choice is a second pair of 128 bits with the same step, into which the narrow pair widens without a word and from which a conversion is checked as every conversion is (the owner, 2026-10-04, against a 64-bit pair with a millisecond step and against a type of the library). Their names, `longduration` and `longtimestamp`, stand to the narrow pair as `long` stands to `int`; an operation between the two pairs widens the narrow operand and has the wide type; and a duration literal is a `duration`, or a `longduration` when its value does not fit one, takes a written or expected type when the value fits, and is the overflow error for a constant beyond both ranges. *(assumed: the reference's readings under T29, the names, the mixed operations and the type of a literal, accepted by the rule's id on 2026-10-09 without their text shown)* The cost: 128-bit arithmetic, and a second name for every operation on time in the library.
  - Test builds throw on overflow instead of wrapping. An explicit wrapping operator exists for intended cases such as hashes: `+%`, `-%` and `*%`, which wrap in every build (the owner, 2026-10-02).
  - Tests can fast-forward virtual time (section 11).
- Around numbers, from the rounds of 2026-10-02:
  - Number literals take every form C# gives them: decimal digits, `0x` and `0b` digits, the prefix letter in either case and `_` allowed directly after the prefix as well, `_` between digits, a fraction, with or without digits before the `.`, and an exponent (`1e3`) for a `double`, and the C# suffixes in upper or lower case, except that a suffix holding a lower-case `l` is a compile error, because `1l` reads as `11`, where C# only warns (the owner, 2026-10-09, against the forms listed before alone, under which `1e-9` is written out in full; the cost is that the lexer is C#'s).
  - An integer literal is an `int`, or a `long` when its value does not fit, or a `ulong` when a `long` does not, without C#'s step through `uint` (the owner, 2026-10-10, confirming the reference's reading L10 and its ladder); where an integer type is written or expected it takes that type *(assumed)*, and is the overflow error below when the value does not fit it; where the written type is no number type, `string s = 300`, the literal is the error for a value of one type where another is required, as any value is *(assumed: it follows from the type error of this section and was not put to the owner)*. A written or expected type reaches every literal of a constant expression, so `long big = 2147483647 + 1` is a `long`, and a literal that is one operand of an operator whose other operand has a type takes that type, so the `1` of `u + 1` with a `uint` is a `uint` and the `100` of `sbyte low = -100` is an `sbyte` (the owner, 2026-10-10, confirming the reference's reading L17, which round 13 had not asked about and which the record had credited to 2026-10-09 in error; C# converts the constant `int` instead, which Kurz cannot do across signedness). The promotion of narrow operands below is for the operands of a run-time operation. A constant expression is folded in the type its literals take, one operation at a time, each operation by the rule of its own operator: under `+ - * / %` an operation whose result leaves an integer or `decimal` type is the constant-overflow error, where a floating-point one folds to its IEEE 754 value (above), and under `+%`, `-%` and `*%` it wraps in that type. So `sbyte low = -100` and `byte c = 200 + 50` compile, `byte c = 200 + 100` is the error, and so is `byte c = 200 + 100 - 100`, whose result would fit but whose first sum does not; `byte b = 255 +% 1` is `0`, and `2147483647 + 1 +% 0`, with no written type and so in `int`, is the error at the `+`. A literal that does not fit the type it takes is the constant-overflow error wherever it stands: `byte c = 300`, never an `int` narrowed, and `b + 300` with a `byte` `b`, whose `300` takes `byte` by the sentence above, as much as `sum +% 300` (below); the cost is that `b * 1000` on a `byte` is written `int(b) * 1000`. Kurz departs from C# here, where a constant expression has the type of its operands, `int` for `2147483647 + 1` whatever the written type, and overflows there (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). The target of a written conversion is such a written type and reaches the literals inside it, so `long(2147483647 + 1)` is `2147483648`; a `-` written directly before a literal belongs to the literal, so `sbyte x = -128` and `int x = -2147483648` compile; a written floating-point type reaches integer literals as well, so `double d = 1 / 2` is `0.5`, where C# divides the integers first; a fraction or exponent literal beside an integer operand keeps its type, and the integer operand widens to it, so `b + 1.5` with a `byte` is a `double`; and literals of different types in one constant expression fold in the type `+` would find for them at run time (the owner, 2026-10-10).
  - A narrower integer becomes a wider one without a word when the wider type holds every value of the narrower, as in C#: within one signedness, and across it where C# widens (`byte` into `short`, `int` and `long`, `ushort` into `int` and `long`, `uint` into `long`); every integer type becomes `float`, `double` or `decimal` without a word, rounded to the nearest value the type holds where it does not hold every value, as in C#, and that rounding is not a loss in the sense of the conversion rule below; a signed type never becomes unsigned without a word (the owner, 2026-10-03, against widening within one signedness only, under which `int x = b` with a `byte` needs `int(b)`). The cost: `b + i` with a `byte` and an `int` is an `int`, and `float f = i` rounds without a word where `float(i)` is checked. Where the two rules meet: an operation finds its common type as C# does, except where C# joins a signed with an unsigned operand through `long` or refuses the pair, which is `uint` or `ulong` with a signed type; there Kurz reports the error for mixing signed with unsigned. So `u + i` with a `uint` and an `int` is the error where C# computes a `long`, while `sb + b` with an `sbyte` and a `byte` is an `int` and `u + b` with a `uint` and a `byte` is a `uint`, as there. The error looks at the two operand types as written, before any promotion: a `byte` or a `ushort` beside a `uint` becomes a `uint` and beside a `ulong` a `ulong`, as C#'s binary numeric promotion makes it, not an `int` first (the promotion bullet below). (the owner, 2026-10-04, against C#'s `long` for `uint` with `int`)
  - A conversion on purpose is the type's name used as a function: `int(value)`. A conversion that loses the value behaves as overflow does: it wraps in a release build and throws in a test build (the owner, 2026-10-02); this is for a value the program computes, since a conversion written on a constant that does not fit is the compile error above. From a `double`, `float` or `decimal` source into an integer type, a dropped fraction is no loss: `int(2.5)` is `2`, toward zero, as in C#. A value outside the target's range, an infinity and NaN lose the value: a test build throws, and a release build gives the target's largest or smallest value for an out-of-range value and an infinity, and `0` for NaN (the owner, 2026-10-03, against wrapping the integer part as an integer source wraps, and against counting a dropped fraction as a loss). The saturation is Kurz's own definition: C# leaves the result unspecified, .NET saturates on every platform since .NET 9, and for a `decimal` source C# throws in every build. So "wraps" describes an integer source and "saturates" a floating-point or `decimal` one. Between the floating-point types and `decimal` the IEEE 754 rules of C# hold: a `double` into a `float` rounds and becomes an infinity beyond the range, NaN stays NaN and an infinity stays an infinity; a `decimal`, which has neither, saturates in a release build and throws in a test build as an integer target does; rounding into `decimal` is no loss. *(assumed: the targets other than integers, which the question did not name)*
  - `+ - * / %`, the comparisons and `&& || !` have the meaning and the precedence they have in C#. Integer division drops the fraction, toward zero. *(assumed)* `+%` and `-%` take the precedence of `+` and `-`, and `*%` that of `*` *(assumed: proposed on 2026-10-09 and not objected to)*, and the associativity of those operators as well (the owner, 2026-10-10). `%` by zero raises as `/` by zero does; a floating-point division by zero yields an infinity, or NaN for `0.0 / 0.0`, as IEEE 754 and C# have it; and a division or remainder of literals by the literal `0` (`10 / 0`) is a compile error of its own, because that division can never succeed (the owner, 2026-10-10, confirming the reference's reading T25, which round 13 had not asked about and which the record had credited to 2026-10-09 in error). That error reaches further than the confirmed sentence: it is for a divisor that is a constant zero whatever the dividend, `x / 0` with a run-time `x` and `1 / (1 - 1)` with a folded divisor included, as C# reports a constant divisor of zero; it is for the integer types and `decimal`, as there; and a floating-point division by a constant zero is no error and yields the infinity or NaN above, as there (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). An operand narrower than `int` is promoted to `int` before `+ - * / %`, unary `-`, `~`, `& | ^`, `<<`, `>>` and the comparisons, as in C#: the sum of two `byte`s is an `int`, a shift on one has the width 32, and a negative shift count is masked as there; where the other operand is a `uint`, a `long` or a `ulong`, the narrow operand takes that type instead of `int`, as C#'s binary numeric promotion does, so that `u + b` is a `uint` and `l + b` a `long` (the owner, 2026-10-03, against computing in the operands' type). The cost: `byte c = a + b` needs `byte(a + b)`; an overflow never happens at 8 or 16 bits, but `ushort * ushort` is computed in a signed `int` and can overflow it. The wrapping operators `+%`, `-%` and `*%` do not promote: they compute in the wider of their operand types and wrap there, so that an 8-bit checksum is `a +% b` on two `byte`s. (the owner, 2026-10-04, against promoting them, which would put a mask on every narrow checksum; C# has no such operators) Under the wrapping operators, which do not promote, operands of different signedness are the error for mixing signed with unsigned, whatever their widths, and a literal operand takes the type of the other operand, so that `sum +% 1` on a `byte` computes in `byte` (the owner, 2026-10-09, confirming the reference's reading T28). A literal that does not fit that type, `sum +% 300` on a `byte`, is the constant-overflow error, as a literal that does not fit a written type is (the owner, 2026-10-10, confirming the reading proposed after the review of round 13).
  - `&`, `|`, `^`, `~`, `<<` and `>>` work on the bits of integers, with the meaning and the precedence they have in C#. Between two types `|` still makes a union; between two values it combines bits. (the owner, 2026-10-02)
  - A duration or a size is a number with a unit from a fixed list: `5min`, `30s`, `256kb`. The list is `ms`, `s`, `min`, `h` and `days` for a duration, and `kb`, `mb` and `gb` for a number of bytes, in steps of 1024. (the owner, 2026-10-02) The number before the unit is a decimal integer or a decimal fraction: `1.5s` is 1500 ms and `0.5h` is 30 min. A fraction that does not fall on a whole nanosecond, or on a whole byte before `kb`, `mb` and `gb`, is the compile error `inexact-literal`; a hexadecimal, a binary or a suffixed literal before a unit is the compile error `syntax`, the one for text no rule gives a meaning (the owner, 2026-10-09, against a decimal integer alone, under which one and a half seconds is written `1500ms`; the cost is a row in the error table and a decimal-to-nanosecond conversion in the compiler). A number with an exponent before a unit is that error as well, since the number before a unit is a decimal integer or a decimal fraction and nothing else; and the unit is read before any suffix, the longest unit that matches, so that `1ms` is a millisecond and not `1m` followed by `s`, and only a suffix the writer spells before a unit, `1Ums`, is the error (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A duration beyond 64 bits of nanoseconds is a `longduration`, and one beyond both ranges the overflow error, by the reading of a duration literal under the wide pair above.
- Strings are UTF-8 and immutable; there is no flag to change the encoding. Indexing goes through `.Bytes` or `.Chars`. `.Chars` yields Unicode code points, one element per code point; graphemes are left to a library (the owner, 2026-10-02). Reading `.Chars` at a position walks the string from its start, because a code point takes one to four bytes; `.Bytes` at a position is direct. The element type of `.Chars` is `char`: one code point in 32 bits, not the UTF-16 unit of C#, which cannot hold every code point (the owner, 2026-10-02). The string itself stays UTF-8: `.Chars` decodes one code point at a time while it is walked and builds no second copy of the text. Four bytes are used only where a `char` is stored, in a variable or in a `List<char>`. Conversion happens at the edges (for example `text.ToUtf16()` for Windows APIs). A `char` has a literal, `'a'`: one code point between single quotes, with the escapes of a string; more than one code point between the quotes is a compile error (the owner, 2026-10-09, against no literal, under which every comparison of a character goes through `.Chars` or `char(97)`; the cost is a second quote in the lexer). Anything but exactly one code point between the quotes is that error, the empty `''` included, as C# refuses it; the quote itself is written `\'`, as in C#, and a bare `'` between the quotes ends the literal, so `'''` is an empty literal followed by a quote that opens one that never closes, and that error (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A `{` between the quotes is the character, and needs no `\{` (the owner, 2026-10-10).
- Array index out of range and an integer or `decimal` division or remainder by zero raise exceptions; a floating-point division by zero yields an infinity or NaN, and a floating-point remainder by zero NaN, as in C# (section 4; the `decimal` and the remainder the owner confirmed on 2026-10-10). *(assumed)*
- Null exists and is enforced: `T?` is short for `T | null`. Using a nullable value without a check is a compile error. `?.` and `??` work as in C#. What counts as a check follows the flow of the code, as the nullable analysis of C# does (the owner, 2026-10-02, in three steps on that day: first only the block of `if name != null { ... }`, then also the code after an `if name == null { ... }` whose block always leaves, then "full C#"). What the nullable analysis of C# 12 proves about a variable holds here *(assumed: the version)*; the forms that follow are the ones the reference's cases show, not a closed list. A value is known to be not null in the block of `if name != null { ... }` and in the `else` of `if name == null { ... }`; after an `if name == null { ... }` whose block always leaves, for the rest of the block that holds the `if`; in the body of `while name != null { ... }`; on the right of `name != null && ...` and of `name == null || ...`; and after an assignment of a value that is not nullable, up to the end of the block that holds the assignment, with the join C# makes where branches or the paths around a loop meet: nullable when any path leaves it nullable. `!` turns a test around, and a test of `a?.b != null` shows that `a` is not null either. A block always leaves when its end cannot be reached, as C# decides reachability: it ends in `return` or `throw`, in `break` or `continue` inside a loop, or in an `if` with `else` or a `match` whose every arm leaves. *(assumed: the definition and the join; the question showed `return`)* An assignment replaces what is known about a variable (the owner, 2026-10-02): assigning a nullable value to a narrowed `mut` variable is allowed and makes it nullable again from that line on, as in C#, so that a loop can walk a chain with `node = node.Next`. Handing a variable to a `mut` parameter counts as such an assignment, as `ref` does there, and so does a call of a `mut` method on a value that a narrowed path runs through. *(assumed: the last sentence; without it a function could set to null what its caller has checked)* The cost of all of it: the checker follows the statements of a function in order, through its branches and loops, instead of deciding once for a block. A path of fields such as `user.Email` is narrowed as a variable is; an index and the result of a call are not, as in C#, and take `??`, `?.` or a variable of their own. One part of "as in C#" does not carry over as it stands: C# keeps a path narrowed across a call that can assign the field through another reference, which its analysis can afford because it only warns, and here an unchecked use is an error. A narrowed path through a `mut` field of a class instance or through a `weak` reference, which a call or an assignment can change, stays narrowed only up to the next call and the next assignment; a second use after a call takes a variable of its own (the owner, 2026-10-03, against never narrowing such a path and against the C# way with a check when the program runs). The cost: an error that depends on a call between the test and the use. Such a path is narrowed by a test, and, when no `weak` reference is in it, by an assignment of a value whose type has no `null`, `acc.Email = "x"`, which narrows it until the next call or assignment, as C#'s flow analysis reads it (the owner, 2026-10-09, against narrowing by a test only, under which a line that just set the field cannot use it); a path with a `weak` reference anywhere in it, the reference itself or a `weak mut` field assigned as `node.Parent = Node()`, is narrowed by a test only and never by an assignment: after `w = Node()` nothing strong holds the node, and it can be gone on the next line (the owner, 2026-10-10, confirming the `weak` part, proposed on 2026-10-03 when the review found the hole, and choosing the test alone for every path with a `weak` reference in it, against narrowing a `weak mut` field by the assignment as a `mut` field is). Inside a lambda, what was known about a variable, or about a path through `data` values, where the lambda was made holds, because the lambda takes the value the variable has at that point (the lambda bullet below); what was known about a path through a `mut` field or a `weak` reference does not hold inside it, because the lambda takes the reference and not the field. *(assumed: the last clause, proposed the same day)* A call of a function that returns `T?` yields a nullable value: `null` does not leave the calling function the way a case of section 5 does (the owner, 2026-10-03). `null` is a value of nullable types only. *(assumed)*
- Generics as in C# (`Map<int, User>`), lambdas as in C# (`x => x * 2`), primary constructors (`class User(string Name, mut int Age)`). A type argument a call does not write is inferred from the arguments: a type parameter takes the one type every argument in its positions has; different types there, or no argument in its positions, are the compile error `cannot-infer`, and the type arguments are written (the owner, 2026-10-10, confirming the reference's reading T27, which round 13 had not asked about and which the record had credited to 2026-10-09 in error; C# would widen a `byte` to an `int` to make `Max(b, i)` an `int`, which the conversion rules above do not allow, and C#'s inference has changed between its versions, so no version of it is named). A literal argument takes the type the other arguments in the parameter's positions fix, so `Max(b, 1)` with a `byte` infers `byte`; two instances of different classes with one base are `cannot-infer`, since no common base is searched for (the owner, 2026-10-10).
- A type parameter is limited inside the brackets: `T Max<T: Comparable>(T a, T b)`. `where` keeps its one meaning, the constraint on a value (section 13). (the owner, 2026-10-02)
- The type of a function is written with an arrow: `(int) => bool`. Its result may be `void`, `(int) => void` (C#'s `Action<int>`); `(int) => int?` returns an `int?`, and a nullable function type is written `((int) => int)?`. *(assumed: proposed on 2026-10-09 and not objected to)* A lambda whose body is a call with a `void` success, `x => print(x)`, is a lambda with a `void` result and no use of the call as a value (the owner, 2026-10-10). A lambda reads the variables around it and cannot assign them. (the owner, 2026-10-02) The cost: a lambda cannot count or sum into a variable of its function; that stays a loop. A lambda takes the value each of those variables has when the lambda is made, as an assignment does; what its function assigns to a `mut` variable after that is not seen inside (the owner, 2026-10-03, against the C# way, in which the lambda reads the variable itself). The cost: a lambda that is called later does not see a newer value, which a C# developer expects it to. A function declared at the top level reads no top-level variable: what it needs comes through its parameters (the owner, 2026-10-03, against reading the variable at the call as a C# local function does, and against taking its value at the declaration as a lambda does). The cost: a counter or a table of the program is passed into every function, or made a field of a class. The variable's name stays reserved inside the function: declaring a local of that name is a compile error (the owner, 2026-10-04, against a local that the unused-variable rule reports unless the body reads it afterwards, which a function meant to reset the program's counter would declare, and compile with when it reads the local). The cost: a top-level variable added later turns every top-level function that already declares that name into a compile error. A parameter of the function, a parameter of a lambda inside it and a loop variable are such declarations too, and every variable declared outside a function reserves its name, above or below the function and at any depth of the top-level code. *(assumed: the reference's reading F16, accepted by its id on 2026-10-09 without its text shown)* In `count = count + 1` inside such a function the one error is the one for the declaration on the left, which the compiler reads first to learn whether the line declares (the owner, 2026-10-09, against reporting the read on the right first, whose message points at the read while the fix is the whole line).
- Class bodies follow C# (the owner, 2026-10-02): a class names its base class and its interfaces after `:`, an interface is declared with `interface`, and further constructors, `static` members and `override` are written as there: a method that a derived class overrides is marked `virtual` in the base and `override` in the derived class, `override` on a method the base does not mark `virtual` is a compile error, and so is a method of a derived class with the name and the parameters of a base method but without `override` (the owner, 2026-10-04, against every method being overridable without a marker, which would differ from C#). As in C#, a method marked `override` can be overridden again in a class further down, the nearest declaration in the whole chain of bases is the one that has to be `virtual` or `override`, and a method of an interface the class implements is no base method and takes no `override`. A method that implements a method of an interface the class names is written `pub`, as C# demands `public` of an implicit implementation: a class that names an interface and lacks one of its methods is the compile error `missing-member`, at the class, naming the method, and a method that is there without `pub` is `interface-method-private`, at the method (the owner, 2026-10-09, against `pub` implied by the interface, under which a method that looks private is public, and against one id for both mistakes). A method of the class implements an interface method when its name, its parameter types in their order and its return type are the interface's; a `pub` method inherited from the base class counts, as in C# (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). One inherited without `pub` does not count: the class is `missing-member` (the owner, 2026-10-10). Where Kurz departs from C#: hiding is an error, not a warning, and C#'s `new` has no counterpart; the cost is that adding a method to a base class breaks every derived class, in every package, that already has a method of that name and those parameters. There is no `sealed`, so every override can be overridden again, and an override has the return type of the method it overrides (the owner, 2026-10-09, confirming the reference's reading, and on 2026-10-10 the exact return type against the covariant return of C# 9; C#'s `sealed override` was not offered as an option); an override whose return type differs is the compile error `override-return-type`, at the method (the owner, 2026-10-09, against the general type error at the method's line, whose message would name no override). A constructor, and only a constructor, assigns each field without `mut` once before its body ends, as C# lets a constructor set a `readonly` field; a further constructor of a class that has a primary constructor calls the primary one first, as C# 12 requires, and a class without one has C#'s constructors (the owner, 2026-10-03, against a class with a primary constructor having no further constructor). The cost: the flow analysis that proves "once, before the end". The once is per instance, stricter than C#, which allows any number of assignments: a field that the primary constructor or an `=` in the body sets is assigned by no constructor, and a field without `mut` and without `=` is assigned exactly once on every path of every constructor that does not chain; such a field cannot exist in a class with a primary constructor, which a call of that constructor would leave unset. A further constructor without the call of the primary one, and a path that leaves such a field unassigned or reads it first, are compile errors. A `mut` field without `=` is assigned on every path of such a constructor as well, any number of times, because Kurz has no zero value that it could hold otherwise (the owner, 2026-10-09, confirming the reference's reading; what it rules out is a `mut` field that starts at a zero value as a C# field does, which Kurz has not). Reading such a field before its first assignment is that error too (the owner, 2026-10-10). In a class with a primary constructor, which assigns no such field, a `mut` field written between the braces without `=` is the compile error `field-unassigned`, as a field without `mut` is (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A constructor written in the body is private without `pub`, as a member is; the primary constructor, and the empty constructor of a class without one, are visible wherever the class is. (the owner, 2026-10-04, against C#'s rules, under which a constructor assigns any number of times and an unassigned field gets the type's zero value, `default(T)`, which Kurz has nowhere else) A `static` field exists once for the program, so it holds an immutable value only: `static mut`, and a static field whose type is or holds a class, are compile errors, because a `mut` field would be state that every actor reaches, against section 6, and a class instance, mutable or not, lives in one actor's heap, which no other actor reaches into (the owner, 2026-10-03, against one copy per actor). *(assumed: that the heap is the reason for an immutable class and for a value such as a `List<Button>`, and that a static field of a generic class exists once per type argument, as in C#)* A field is written `Type name`, a method like a function. There is no property syntax until something needs it. A method that changes its own value carries `mut` in front of its return type: `mut void Add(T item)`. That marker is for values: a method of a class assigns the `mut` fields of its instance without one and can be called through every reference (the owner, 2026-10-02). The cost: a call on a class instance does not show whether it changes the instance. A `data` type takes its methods in a body of the same form. *(assumed: the last sentence)*
- A `mut` field of a class instance can be assigned through every reference to the instance, whether the variable that holds the reference is `mut` or not: the variable holds a reference, and the reference does not change. (the owner, 2026-10-02)
- `data` inheritance is written `data Admin(int Level) : User`; the fields of the base come first in the constructor (the owner, 2026-10-02). A value of the derived type never equals a value of the base type. *(assumed: the last sentence)*
- A class with a primary constructor inherits in two forms (the owner, 2026-10-02). The short one is the form of `data`: `class Admin(int Level) : User`, whose constructor takes the fields of the base first. The explicit one passes the arguments of the base itself, so that they can be computed: `class Guest(int Number) : User("guest {Number}")`, whose constructor takes only what the class lists. In the explicit form a parameter that has the name and the type of a field of the base is that field and no new one, as in a C# record: `class Admin(string Name, int Level) : User(Name)` holds `Name` once (the owner, 2026-10-02). The cost: a name decides whether a parameter is a field. A parameter with a base field's name and another type, or with its name and type while the base receives something else, is the compile error `base-field-clash`; C# rejects the first, a positional parameter whose type differs from the inherited member it names, and only warns about the second, so Kurz is stricter there. *(assumed: the reference's reading K17, accepted by its id on 2026-10-09 without its text shown)* The short form follows the primary constructor of the base and is a compile error when the base has none; a listed parameter with a base field's name and type is that field in that form too, and one with a base field's name and another type is the clash error there as well (the owner, 2026-10-10, confirming the reference's K16, which the record had not carried); the base's defaults keep their place, so the class's own parameters come after them (the owner, 2026-10-03, against taking the empty constructor of a base without a primary one). The cost: such a base forces the explicit form.
- `enum Plan { Free, Pro }` is the short form of a union of cases without fields: its values (`Plan.Free`) are cases, a `match` has to list every one, and a value has a number only where one is written. (the owner, 2026-10-02) A number is written in the declaration, `enum Plan { Free = 1, Pro = 2 }`, and the two directions are members of the type: `Plan.Pro.Number` is the number of a value, and `Plan.From(2)` is the value of a number, with the result `Plan | Invalid` (the owner, 2026-10-02, against the C# form `int(Plan.Pro)` and `Plan(2)`, which throws). A number arrives from a file, a database or the wire, where a wrong one is normal use, so it is an outcome (section 5) and not an exception. *(assumed: a declaration writes a number for every value or for none, and one that numbers two values alike or only some of them, or `.Number` and `From` on one that numbers none, is the compile error `enum-number`, the reference's reading D20, accepted by its id on 2026-10-09 without its text shown; the number is an `int`; `Invalid` is the type that input from outside yields, section 13)*
- What `[Flags]` and `HasFlag` give in C# is a declaration of its own: `flags Access { Read, Write, Run }`. A value of the type is a set of these names. Each name is one bit, and the compiler numbers the bits in order unless the declaration writes the number. A set is tested with `p.Has(Access.Read)`. The cost: one more core word. (the owner, 2026-10-02) A set is not one case, so a `match` cannot list it case by case; a `match` on a set is the compile error for text no rule gives a meaning, at the line of the `match` (the owner, 2026-10-09, against an id of its own; the line the owner confirmed on 2026-10-10). Sets are combined with the bit operators, as in C#: `Access.Read | Access.Write` holds both names, `set & ~Access.Write` takes a name out, `&` keeps what two sets share and `^` what exactly one of them holds. Every `flags` type has the name `None` for the set that holds nothing. `Has` is true when the set holds every name of its argument, as `HasFlag` is. The cost: taking a name out reads as arithmetic on bits. (the owner, 2026-10-02, against methods such as `With` and `Without`) `~set` holds the names of the type that `set` does not hold. *(assumed: C# flips every bit of the number, also the bits no name has; the question showed `~` only next to `&`)* The number of a set is written and read as the number of an `enum` value is: a declaration can write the number of each name, which is the value of its bit as in C# (`flags Access { Read = 1, Write = 2, Run = 4 }`); without written numbers the names get 1, 2, 4 and so on in their order; `set.Number` is the sum of the numbers it holds, and `Access.From(5)` is the set with that number, or `Invalid` when the number has a bit that no name has. *(assumed: the question about numbers showed an `enum` only)*
- A value is made by the type's name and the arguments in order, or by name and with defaults (section 8); there is no `new`. A `data` type without fields has exactly one value, written as the bare name. `mut` comes before a written type: `mut int x = 5`. A parameter is immutable inside its function unless it is marked `mut`; a class instance it holds keeps its `mut` fields assignable, as through every reference. *(assumed: all four, and the clause on instances)*
- The samples use these collection members: `List<T>()`, `Add`, `Count`, an index counted from 0, `Where`; `Map<K, V>()`, `map[key] = value`, and `map[key]`, which yields the value or `null`. *(assumed; the naming of the standard library stays open, section 14)* A map has no order a program may rely on: a loop over it and its text (section 8) yield the entries in the order of the map's storage, which can differ between runs and versions, as for a `Dictionary` in C# (the owner, 2026-10-03: "if it isn't sorted, it isn't sorted", against an insertion order and a key order). The cost: a test cannot pin the text of a map with several entries.

## 5. Outcomes and errors

The language enforces the C# `OneOf<>` pattern: every method declares every outcome that can happen under normal use.

- The return type is a union. The **first case is the success case**.
- **Unwrap rule:** calling such a method yields the success value directly. Every other case leaves the calling method and goes to its caller.
- **Honest signatures:** a propagated case must be listed in the calling method's own signature. Otherwise it is a compile error.
- To keep all cases instead of propagating, either `match` the call directly or give the variable an explicit union type. A written union type holds every case the call can return: one that lists only some of them is the compile error for a value of one type where another is required, as an assignment in C# must fit its type; the other cases are kept by naming them, or handled by a `match` (the owner, 2026-10-09, against a written type as a filter that lets the missing cases leave the function, under which a declaration ends the function on some paths without a word; the cost is that a function with five cases forces a five-case type on every variable that keeps its result). A written type that is the success case itself, `User?` for a call that returns `User? | DbError`, names no other case and unwraps as the rule above says; one that names any case beyond the success case names them all (the owner, 2026-10-10, confirming the reading proposed after the review of round 13).
- A postfix `else` block handles selected cases; unlisted ones still propagate. An arm can recover with a value, transform and return another case, or `throw`.
- **Exceptions** are only for situations the called method truly cannot recover from. There is no `catch`. An exception kills the actor it happens in (section 6).
- **`match` lists every case** of the union; a missing case is a compile error. A default arm is written `else`. An arm can also test a literal, and `match` can be used as an expression that yields the value of the arm that ran. There are no patterns over fields until something needs them. (the owner, 2026-10-02) When a value fits several arms, the first arm in source order whose type the value is runs, as C#'s `switch` does; an arm may name a type derived from a case (`Admin` where the case is `User`), and an arm that can never run, because an earlier arm covers its type, is the compile error `unreachable-arm` (the owner, 2026-10-09, against arms that name cases only, under which a `match` cannot tell an `Admin` from a `User`; the cost is a row in the error table and a subtype check per arm). An `else` after arms that cover every case, and a literal arm after an arm for its whole type, are `unreachable-arm` too (the owner, 2026-10-10).
- **Top-level code has no caller**, so a call there has to handle every case that is not success; letting one propagate is a compile error. (the owner, 2026-10-02)
- **`throw` takes a value or a text**: `throw ConfigMissing(path)` or, as the short form, `throw "no config"` (the owner, 2026-10-02). It is a statement and can stand wherever one can. The bare `throw` of an `else` arm stays. What a supervisor reads as the `Reason` of a crashed child (section 6) is that value. The type of `Reason` is the union of everything the child's code can throw, found over the whole program (the owner, 2026-10-03, against a fixed `Reason` type that carries the thrown value's text and the place, and against limiting `throw` to `data` values and text). The cost: the type of `c.Reason` changes when a function the child calls changes what it throws, and a `match` over it has to follow. The runtime's own errors (overflow, a conversion that lost its value, division by zero, an index out of range, a reversed range, an exhausted stack, the last by the owner's choice of 2026-10-09 in section 6: the run-time errors of the reference's chapter 12) are values of one `data` type of the runtime that names the error, and that type is a member of every `Reason` in every build, although a test build raises more of them than a release build. The place and the chain id are fields of the `Crashed` case beside `Reason`, not part of the thrown value. A thrown class instance is moved out of the dying child's heap into the supervisor's, with everything it reaches, before the heap is freed; the cost is that walk, once per crash. (the owner, 2026-10-04, against a compile error for a thrown class instance)
- **The success case may be `void`**: `void | NotFound Remove(int id)`; the call is then a statement. (the owner, 2026-10-02)

```
data User(int Id, string Name, string? Email)
data NotFound
data Invalid

actor UserStore {
    mut users = Map<int, User>()

    pub User | NotFound Get(int id) => users[id] ?? NotFound
}

pub User | NotFound | Invalid Rename(UserStore store, int id, string name) {
    if name == "" { return Invalid }
    user = store.Get(id)                 // user is a User; NotFound went to the caller
    return user with { Name = name }
}

// keep all cases
User | NotFound result = store.Get(id)

match store.Get(id) {
    User u   => print(u.Name)
    NotFound => print("none")
}

// handle selected cases
user = store.Get(id) else { NotFound => User.Guest }                       // recover
user = store.Get(id) else { NotFound => return Invalid }                   // transform
user = store.Get(id) else { NotFound => throw }                            // cannot happen here
```

## 6. Concurrency

### Actors

An actor is an object with private state, its own heap and an inbox. In C# terms: a class with a private `Channel<T>` and one task looping over it, all written by the compiler.

- Nobody outside can touch its fields.
- Calls on an actor look like ordinary method calls. The compiler turns each into a message.
- An actor handles one message at a time, so there are no locks and no data races inside it.
- An idle actor costs no CPU and a few hundred bytes. Millions run on a handful of OS threads.
- There is no `async`/`await`. Every call looks synchronous; the runtime parks the actor while it waits.
- An actor parked in the middle of a call is a compiler-made state machine, built only for functions that can wait; the whole-program view knows which ones. All other code runs on the ordinary stack. *(assumed)*
- Messages from one actor to another arrive in the order they were sent. *(assumed)*
- A call waits for its result by default. `send` makes a call fire-and-forget: `send store.Add(user)`. *(Waiting by default is the reading of "the Elixir way" that was stated back to the owner.)*
- What may cross between actors: values (`data`, collections, strings, numbers), immutable class instances and actor references. A mutable class instance crosses only with an explicit word at the call: `copy` (the receiver gets a deep copy) or `move` (the sender's variable is dead afterwards, nothing is copied). Two actors can never reach the same mutable object.
- How a value crosses on one machine is the runtime's decision and invisible in code: a small value is copied into the receiver's heap, a big one is shared by pointer. Shared data lives outside the actor heaps and is counted with counters that are safe across cores. Erlang does the same for binaries above 64 bytes.
- Identity does not survive crossing: what arrives is a different object. Immutable classes compare by content, so this is invisible for them.

```
store.Add(copy order)
store.Add(move order)
```

### Scheduling

Switching between actors is cooperative by default: an actor gives up its core at actor calls and I/O. A runaway loop then costs one core, not the system, because other cores take over the waiting actors.

- An actor opts into stronger fairness after its name, where `per` sits as well. `yield checks`: the compiler inserts switch points at loop ends and function entry. `yield timer`: a timer interrupt switches the actor out.
- The compiler warns about a loop inside an actor that has no switch point and no provable end.

```
actor ReportBuilder yield checks { ... }
actor ReportBuilder yield timer { ... }
```

### Crashes and supervision

- An exception kills only the actor it happens in.
- The supervisor is whoever spawned the actor; no extra code. Explicit supervisor trees exist only for other policies.
- Default on a child crash: restart it with fresh state.
- Restart limit as in Erlang: too many crashes in a short window crash the parent. The default is overridable.
- Top-level code is the root actor. If it crashes, the process exits with the exit code 1 (the owner, 2026-10-04, against a distinct code such as 70).
- Calling a dead or unreachable actor raises an exception in the caller.
- Every call has a timeout (5 seconds by default, as in Elixir), then an exception in the caller.
- A caller cannot handle a callee's crash, timeout or unreachability. The exception kills the caller as well, and then every waiting caller up the chain; supervisors restart them. State that has to survive this is `durable` (section 13). `retry` runs its block in a child actor and watches it. The owner chose this "for now" on 2026-10-01; the alternative was an opt-in `Failed` case in the postfix `else`.
- `attempt { ... }` runs its block as a one-shot child actor with the current actor as its supervisor. The child's exception arrives as the union case `Crashed`, and the parent lives. The block gets values only and cannot touch the parent's `mut` state, so nothing half-changed survives. "No `catch`" still holds: an actor never catches its own exception, only its child's death. `retry` is a loop over `attempt`, and wrapping a call in `attempt` is how a failed callee is handled where that is wanted.
- Policy at `spawn`: `on crash restart` (the default), `on crash stop`, `on crash escalate` (the supervisor crashes too), or a block that decides. Knobs: `after <duration>` pauses before the restart, `max N in <duration>` sets the limit. The same words apply to `per` actors. Group strategies in the manner of Erlang (one dies, all siblings restart) are not in the design.
- A reference stays valid across a restart and reaches the new instance. A caller that was not waiting at the time of the crash notices nothing, except that the state is fresh.
- An actor that exhausts its stack dies as it does for any exception, with a `Reason` of the runtime's `data` type (section 5) that is the exception `stack-overflow`, at the line of the call that did not fit (the owner, 2026-10-10, confirming the reference's reading O11); its parent lives under `on crash restart` and `on crash stop`, and crashes too under `on crash escalate`, as for any crash (above), and at the top level, the root actor, the process exits with the exit code 1 (above); an allocation that fails ends the process with the exit code 1 and a line on standard error, because the memory the actors' heaps are carved from is the process's, and no actor can run on (the owner, 2026-10-09, against both ending the process, under which one runaway recursion in a worker takes the server down, and against an allocation failure as a `Reason`, which would need a reserve to build the `Crashed` value when nothing can be allocated; the cost is a guard page per actor stack, a stack probe in every frame larger than that page so that no frame steps past it, a signal stack per thread for the handler that turns the fault into the exception, and a program that cannot recover from memory pressure).

```
rows = attempt { Import(file) } else { Crashed c => return ImportFailed(c.Reason) }

w = spawn Worker(queue)                                   // default: restart fresh; too many crashes crash the spawner
w = spawn Worker(queue) on crash stop
w = spawn Worker(queue) on crash restart after 1s max 10 in 1min
w = spawn Worker(queue) on crash (c) {
    log("died: {c.Reason}")
    restart
}
```

### Deadlocks

Three layers, so that a hang cannot happen silently.

1. **Callbacks work.** Every call carries a chain ID. An actor waiting inside a chain lets in calls that belong to the same chain. `A.Save` waits on `B.Check`, which calls `A.Load`: this runs like a nested method call. Unrelated messages still wait.
2. **Compile error for loops of waiting calls between actor types**, analysed per method. `vouch` on the call overrides it and means "different instance, I vouch": `vouch c.Size()`. Exempt without override: a callback through a reference the caller passed in that same call.
3. **Instant runtime detection.** On every wait the runtime follows the wait line. If it leads back, one actor gets an exception immediately, naming both chains, and its supervisor restarts it.

Across machines, layer 3 sees chain loops but not crossing chains; those fall to the timeout.

Side effect: the chain ID doubles as a trace ID, so log lines can carry it automatically.

## 7. Distribution

- Built into the language and on by default. A compiler flag turns it off for single-machine targets such as ESP32.
- A call on an actor works unchanged whether the actor lives in this process or on another machine. Clustering, remote spawn and serialization are part of the runtime.
- **Network split**, applied to everything cluster-wide (singleton actors as well as jobs):
  1. The side holding the majority of machines runs it.
  2. On an exact tie, the side holding the oldest machine runs it.
  3. If no side qualifies, the policy is per item: `skip` (default), or run on all sides with a developer-written `merge` block that is called with both results when the cluster heals.
  - Kurz can compare and replay its own state only. Effects on the outside world (an email, a payment) cannot be merged.
- No hot code reload; it would cost runtime performance.
- **Upgrades:** code versions never mix inside one cluster. A new version forms a new cluster and traffic switches over. Clients outside the cluster are the exception (section 13). State moves only where an actor says so with `upgrade`:
  - No `upgrade` line: the actor starts fresh in the new cluster, the same as after a crash.
  - `upgrade keep field`: the field is carried over, matched by name and type. A new field gets its default, a removed field is dropped.
  - Block form for when the shape really changed.

```
actor UserStore {
    mut users = Map<int, User>()
    mut cache = Map<int, Page>()

    upgrade keep users                                          // cache starts empty
    upgrade (old) { users = old.users.Where(u => u.Active) }    // or transform by hand
}
```

```
every 5min on split run {
    RecountStock()
} merge (mine, theirs) => mine.Newer(theirs)
```

## 8. Syntax

- Variables: `x = 5` declares an immutable variable, `mut x = 5` a mutable one. The type may be written: `int x = 5`. An unused variable is a compile error, which also catches typos that would otherwise declare a new variable. A parameter that its function never reads, and the variable of a `for` loop whose body never reads it, are no error (the owner, 2026-10-03, against making them errors like a variable, with the words that the compiler just optimizes it).
- No semicolons. A newline ends a statement. A `;` that ends, starts or follows a statement is the semicolon error, one for a run of them, and the statement after it parses on its own; a `;` anywhere else, inside parentheses, brackets or an interpolation, or where an operand, a name, a member, a parameter or an arm is expected, is the error for text no rule gives a meaning (the owner, 2026-10-10, confirming the front end's reading).
- No parentheses are needed around a condition, and braces are always required: `if x > 5 { ... }`. A condition in parentheses, `if (x > 5) {`, compiles all the same, since an expression in parentheses is an expression like any other; a `{` on the next line is the compile error for a missing block, because the line ends the statement and nothing continues it there (the owner, 2026-10-09, against continuing the statement onto the `{`, a further exception to the line rule, and against an error of its own for the parentheses, a special case in the parser for one spelling). The same error, at the line where the header ends, is raised wherever a block that a construct takes opens on the next line or is replaced by a statement: the body of a function outside a type body or of a constructor (at the first line of its signature), the body of a `while` or a `for`, the arms of a `match` or of the `else` after a call, an `else` or `raw` block, and the body of a `class`, `data`, `interface`, `enum` or `flags` or the braces of `with`; a method signature inside a type body that ends its line is the bodiless form of an interface member, so its `{` on the next line is the error for text no rule gives a meaning (the owner, 2026-10-10, confirming the front end's readings).
- Signatures in C# order, type first: `User | NotFound Get(int id) => ...`.
- Everything is private by default: members are private to their type, top-level types and functions are private to their folder. `pub` exposes; `prot` exposes to inheriting types. The fields of a primary constructor are the exception: they can be read wherever their type is visible, as the positional members of a C# record can (the owner, 2026-10-02). Without that, every such field would carry `pub`. The cost: a field that has to stay private is declared in the body instead.
- A folder is a namespace; there is no `namespace` line. Files in one folder see each other without imports. `use` brings in other folders and packages.
- The project file is written in Kurz itself (`project.kz`), not in a separate format. It holds one `deploy` block per deployment with that deployment's switches: the inbox mode (section 13) and, by the same idea, distribution on or off and the target platform. *(assumed)*
- String interpolation is always on: `"hello {name}"`.
- A string over several lines is written as a raw string literal is in C# 11: it opens with `"""` at the end of a line and closes with `"""` on a line of its own. The text starts on the line after the opening and ends before the closing line, and the indentation of the closing line is removed from every line; a line that is indented less is a compile error, as there (the owner, 2026-10-02). A `"` inside the block is an ordinary character, as there. Unlike there, `{expression}` and `\` keep the meaning they have in every other literal, so a brace of JSON or CSS is written `\{` (the owner, 2026-10-02, against a block without interpolation, which was the form offered). `"""` opens a block only at the end of a line: `"""abc"""` on one line, text after an opening `"""` and text before a closing one are compile errors, and a line of the block that holds only whitespace is exempt from the indentation rule and is an empty line of the text, as in C# 11 (the owner, 2026-10-09, against a one-line form that holds `abc` with a `"` as an ordinary character, as C# 11 has it, and against an id of its own for the malformed shapes; the escape gives a `"` without the one-line form). Only characters other than whitespace count as text beside a `"""`, as C# 11 has it, so trailing spaces after the opening and the indentation before the closing are no error; and code may follow the closing `"""` on its line, `""")` closing a call, as C# 11 allows, since "on a line of its own" is about what stands before it (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). There is no block that opens with four or more quotes, as C# 11 has: `""""` at the end of a line is a compile error (the owner, 2026-10-10). A lone carriage return, one that no line feed follows, is an ordinary character inside a literal, except at the end of a line of the block, where every trailing one is dropped with the line's end, and whitespace outside a literal, so a file whose lines end in a carriage return alone is one line (the owner, 2026-10-10, confirming the front end's reading).
- Primary constructors, C# lambdas, C# generics.
- Top-level statements instead of `Main`.
- A statement continues on the next line while a `(` or a `[` is open, when its line ends in a binary operator, a comma or `=>`, and when the next line starts with `.`. Nothing else continues it. There is no `;`: not at the end of a line and not between two statements. (the owner, 2026-10-02) The `=` of an assignment counts as a binary operator here. *(assumed)* In detail (the owner, 2026-10-10, confirming the front end's readings): a line that starts with `?.` continues the statement as one that starts with `.` does, and empty lines and comment lines between the statement and that line do not end it; a line break inside the braces of an `enum`, `flags` or `with` body and inside the `< >` of type arguments or type parameters does not end the statement, as one inside parentheses does not; a line break after the `:` of a base list continues the declaration; a line that ends in the `|` of a type or in `..` or `..<` continues on the next; and a trailing comma is accepted in an `enum` or `flags` body and refused in the braces of `with`.
- `name = expression` assigns when a variable of that name is visible and declares one when none is, so a variable cannot hide another one. A variable counts as used when it is read at least once; being assigned again does not count. A `mut` variable that nothing changes is a warning, not an error. (the owner, 2026-10-02)
- A written type always declares; when the name is already visible that is a compile error. A variable is visible from its declaration to the end of its block, and every declaration has a value. *(assumed)*
- `a..b` is a range that includes both ends, `a..<b` leaves the end out. A loop over numbers is `for i in 0..<n`; the C form with three parts does not exist. (the owner, 2026-10-02) A range never counts down, and a range whose end lies below its start is an error, not an empty range: a compile error when both ends are constant expressions, an exception when the program runs otherwise (the owner, 2026-10-02, against the proposal that such a range holds no number). `a..<a` holds no number and is valid. The cost: `for i in 1..n` throws when `n` is 0, so a loop that may run zero times is written with `..<`. The range and its loop variable have the common type of the two ends, found as `+` finds it: `0..<n` with a `long` `n` is a range of `long`, and integer ends for which `+` raises the error for mixing signed with unsigned are that error (the owner, 2026-10-09, against a range that is always `int`, under which a loop over a `long` range is written by hand; the cost is that the loop variable's type follows the end, which a reader finds at the declaration of `n`). An end whose type is no integer type, `0..<1.5` or `0..2.0` whatever its value, is the error for a value of one type where another is required (the owner, 2026-10-10, confirming the reading proposed after the review of round 13).
- `else` and `else if` follow the closing brace on its line; a condition is a `bool`; `while condition { }`, `for name in collection { }`, `break` and `continue` mean what they mean in C#. *(assumed)*
- A function body is an expression after `=>` or a block with `return`; a function without a result is declared `void`; a function or a type can be used above its declaration. *(assumed)*
- Functions may share a name when their parameters differ, a parameter or a field may have a default value (`int Count = 1`), and an argument may be passed by name (`Item(ProductId: 7)`). The owner chose overloads on 2026-10-02 against the proposal to leave them out. When a call fits several functions, the one whose parameters have exactly the types of the arguments, with no default value used, wins; any other call that fits more than one is a compile error (the owner, 2026-10-02). The cost of that rule: where two functions fit through widening, the call needs a conversion such as `long(x)`. For this rule a literal argument has the type it has on its own, `int` for `4`, so `Show(4)` picks `Show(int)` over `Show(long)`, as in C# (the owner, 2026-10-02). The cost of overloads as such: a name no longer stands for one function, for the reader and for `mock` (section 11), and the name of a parameter becomes something its callers depend on. Arguments are evaluated from left to right, as in C# *(assumed: proposed on 2026-10-09 and not objected to)*, in the order written at the call, named arguments included (the owner, 2026-10-10). A parameter's default value is a constant expression, as C# requires of it, and one that is not is the compile error `default-not-constant`, at the parameter (the owner, 2026-10-10, against the general type error and the id for text no rule gives a meaning; the spelling of the id and its line are *(assumed: proposed on 2026-10-10, after the owner's choice)*). A field's default may be any expression, as in C# (the owner, 2026-10-10, against constants alone, under which a field of a collection type could have no default).
- A source file is read as UTF-8, and a string literal holds the code points written in it, as they are written, without normalization (the owner, 2026-10-09, confirming the reference's reading L1). A file that is not valid UTF-8, an encoded surrogate included, is one compile error, `invalid-source`, at the line of the first bad byte; a byte-order mark at the start of the file is skipped, because some Windows editors and tools write one (the owner, 2026-10-09, against refusing the mark too, under which a file saved with the mark does not compile; the cost is one accepted leading code point that is not part of the text). No conformance case shows the error: the lint and every gate read a corpus file as UTF-8, so a case that is not would be refused by the tools as they stand, and the reference names the id in its rule and not in its table of the ids cases expect.
- `//` starts a comment that runs to the end of its line. In a string literal `\n`, `\t`, `\"` and `\\` mean what they mean in C#, and `\{` is a brace that starts no interpolation; the other escapes of C# 12 (`\'`, `\0`, `\a`, `\b`, `\f`, `\r`, `\v`, `\u` with four hex digits, `\U` with eight and `\x` with one to four) mean what they mean there, and a backslash before any other character, or before a `u`, `U` or `x` without the digits it takes, is a compile error, as there (the owner, 2026-10-03, against the five alone; the version, under which the `\e` of C# 13 is such an error, is *(assumed)*). A `\u`, `\U` or `\x` whose digits name a surrogate, U+D800 to U+DFFF, or a value beyond U+10FFFF is that error too, since a UTF-8 string cannot hold a lone surrogate (the owner, 2026-10-10, confirming the front end's reading). `true` and `false` are the values of `bool`. A name starts with a letter or `_` and goes on with letters, digits and `_`; upper and lower case differ. A letter is one of ASCII's: a name with any other character, an umlaut included, is a compile error, and C#'s `@` names do not exist (the owner, 2026-10-09, against any Unicode letter as C# allows, under which two names can look the same, and against `@keyword` names, a second spelling C# has for interop; the cost is that a C# program with an umlaut in a name is edited). *(assumed: the comment, the five escapes, `true` and `false`, and the rest of the form of a name)*
- Only the core words are reserved. A user-defined keyword (section 9) is reserved in the files that import it. (the owner, 2026-10-02) The core words are the words the language uses as syntax, listed in the reference as a closed list (the owner, 2026-10-03, against reserving every keyword of C#, which would take `goto`, `unsafe` and `checked` from programs for nothing). The names of the built-in types (`int`, `string`, `bool`, ..., and `duration` and `timestamp` with their wide pair) are core words as well, as most of them are keywords in C# (the owner, 2026-10-04, against ordinary names that a declaration hides in its block). The cost: nothing can be called `string`, and a ported program that calls a local `duration` or `timestamp`, which C# does not reserve, has to rename it. A core word used as a name reports the one error at its declaration, and a use of it afterwards none; a core word where an expression stands, `print(if)`, and an untyped lambda parameter or the name of a named argument that is a core word, `by => by.Id` or `f(by: 1)`, are the error for text no rule gives a meaning, where the typed parameter `(int by)` is the reserved-word error; and `match` starts a match when a token that can start a value follows it, and is a name otherwise, so `match.Add(1)` is a call (the owner, 2026-10-10, confirming the front end's readings).
- A compile error is identified by a word, such as `unused-variable`. Tests of the compiler pin the id and the line of an error, never its message (the owner, 2026-10-02). The exceptions a program can raise have ids as well, and a test pins the id and the line that raised (the owner, 2026-10-03, against tests that say only that something is thrown). An error is reported on the line that holds the offending construct: the declaration for an unused variable, the first class declaration on the path for a possible cycle. *(assumed: the last sentence)* A construct over several lines reports the line of the smallest part that is wrong, an argument its own line and a binary operator its operator's line; only an error of the whole reports the first line. *(assumed: proposed on 2026-10-09 and not objected to)* A block that is missing, or that opens on the next line, is an error of the whole construct, reported where the construct's header ends, the line where the block should have opened: for an `if` the last line of its condition, for the other constructs the line named with them in section 8 (the owner, 2026-10-10, confirming the reading proposed after the review of round 13). A `)`, `]` or `}` missing at the end of the file is reported at the last line that holds code, a block of `"""` that is never closed at its opening line, and an interpolation that no `}` closes on its line at the line of its `{` (the owner, 2026-10-10, confirming the front end's readings). A text that no rule gives a meaning is one compile error, `syntax`, reported on the line where the text stops making sense, the line of the first token no rule accepts; where the construct's rule names an id of its own, that id is reported and not `syntax` (the owner, 2026-10-10, confirming the reference's reading E4, which round 13 had not asked about and which the record had credited to 2026-10-09 in error, and with it the token and the precedence of the rule's own id). Such an error that only the type checker can see, a `match` on a set of flags or a call with a `void` success used as a value, is reported at the line of the construct (the owner, 2026-10-10).
- `print(value)` writes the text of a value and a line break. The text of an integer is its decimal digits, of a string the string, of a `bool` `true` or `false`. *(assumed; the name belongs to the standard library, whose naming is open)* A `data` value has a text derived from its type, in the manner of C# records: `User { Id = 1, Name = Ann }`. The other values have a derived text as well: a list shows its items between brackets, `[1, 2, 3]`; an `enum` value and a `data` value without fields show their name; null shows `null`; a `double` shows the shortest digits that read back as the same number. A class instance is the exception: it has a text only when its class or a base class declares one, and printing an instance of a class that neither declares nor inherits one is a compile error. An instance can reach itself through a `weak` reference, so a derived text would need a rule for where to stop. The cost: one declaration in every class hierarchy that gets printed. (the owner, 2026-10-02; "or a base class" and "hierarchy" follow from the next choice) The method is inherited, and the class of the instance picks it when the program runs, as `ToString()` is in C#, so a `User` variable that holds an `Admin` prints the `Admin`'s text; a `Text()` without `pub` cannot be used by `print`, which is a use outside the class, and that is the error for a member that is not visible (the owner, 2026-10-03, against counting only a `pub Text()` the class itself declares). `print` of an instance inside its own class, and an interpolation of one there, reach `Text()` as a use from outside does, so a `Text()` without `pub` is that error there too (the owner, 2026-10-09, against a reading under which a private `Text()` has two meanings). Through an interface the text exists when the interface declares `string Text()`, and through a type parameter when the parameter is limited to an interface that declares it; otherwise printing is the same compile error as for a class without text (the owner, 2026-10-03, against a check for each instantiation of a generic function). A type meets such a limit when it has a text: the values above, a class that declares or inherits a `pub Text()`, and a value whose interface or type parameter has a text by the sentence before, without naming the interface. (the owner, 2026-10-04, against an interface a class has to name) The match holds only for an interface whose only member is `string Text()`; an interface that declares more is met by naming it, because a type that has a text does not have its other members. *(assumed: the reference's reading A10, accepted by its id on 2026-10-09 without its text shown)* The cost: a generic function that prints its argument needs the limit, and a base-typed value may print more than its static type says. A class declares its text with a method of a fixed name that takes no parameters, `pub string Text() => "counter {Count}"`, as `ToString()` does in C# (the owner, 2026-10-02, against a list of the fields that show). The owner asked with it that such a text is built with as little work as the compiler can manage. The mechanism for that *(assumed: it was proposed in the reply to that wish)*: an interpolated string is compiled into writes into one buffer, without a string of its own for each part; and where a text goes straight into `print` or into another interpolated string, a `Text()` whose body is a literal or an interpolated string writes into the buffer of its caller, so that for such a body the instance's text is never a string of its own. A `Text()` with a block body, or one that returns what another function returns, builds that string first and pays its allocation. What stays: a number is turned into digits when the program runs, and a text that is kept, in a variable or a field, is one allocation. No number is promised for it, and none of the gates of section 13 measures it. A map shows its entries between brackets, each as `key: value`: `[Ann: 31, Bea: 28]` (the owner, 2026-10-02); a map without entries shows `[]`, as a list does. *(assumed: the last part)* The remaining texts *(assumed: they were listed with that question and not objected to)*: a `float` shows what a `double` shows, a `decimal` the digits it holds, a `char` its character, a duration its value in the units of its literals (`1h 30min`), and a set of flags its names with ` | ` between them; a `double` without a fraction, one that needs an exponent and one that is no number show what C# prints for them (`1`, `1E+21`, `NaN`). In detail *(assumed: the reference's readings A11 to A14, accepted by their ids on 2026-10-09 without their text shown; that the exponent form starts at `1E-05` itself, and that it is the remainder below a second that shows in milliseconds, came from the review of 2026-10-10)*: a `float` shows the shortest digits that read back as the same `float`, `0.1f` as `0.1`; a duration shows its value split into its units from the largest down, each unit at most once and a zero count left out, the remainder below a second in milliseconds and, when no formatting is chosen, the remainder below a millisecond in microseconds and nanoseconds, `1s 250us` (the owner, 2026-10-10, against rounding or cutting to milliseconds), zero as `0ms` and a negative one with `-` before the whole; a set of flags shows its names in the order of their declaration and the empty set as `None`; and what C# prints for a `double` is what .NET Core 3.0 and later print with the invariant culture, an exponent form from `1E+15` up and from `1E-05` down, `-0` for a negative zero, `Infinity`, `-Infinity` and `NaN`. The order of a map's entries is the one of its storage (section 4). A list or a `data` value that holds an instance of a class without a text has no text either: printing it is the same compile error, because the type of what it holds is known where it is printed (the owner, 2026-10-03, against showing the name of the class). The cost: a `data` type with such a field cannot be printed.

## 9. Keywords that transform code

- Core words live in the compiler: `actor`, `data`, `match`, `mut`, `spawn`, `weak`, `pub` and the like.
- Chore keywords are written in Kurz, with the same mechanism users get, and ship in the standard library.
- Chores that should become keywords:
  - HTTP endpoints and routing
  - retry, timeout, circuit breaker around a call
  - caching
  - mapping between types
  - validation
  - database queries
  - scheduled jobs
  - state machines
  - dependency wiring
  - logging

### User-defined keywords

Users can define full block keywords. The rules that keep editor tooling easy:

1. **Free shape, declared as a pattern.** The head is built from slots: fixed words, expressions, types, names, blocks, declarations, with optional and repeated parts. The pattern is data the editor reads without running code. A keyword starts with its own word, and two keywords used in one file may not overlap.
2. **The inside is plain Kurz.** A keyword never adds new syntax inside its slots.
3. **Declared and imported.** A file names the keywords it uses.
4. **The output is plain Kurz and viewable.** Errors point at the developer's source, not at the expansion.
5. **Sealed at compile time.** Keyword code cannot read files, network or clock.
6. **No capture.** A keyword only touches what is passed to it. Names it introduces inside its body are declared in its pattern.
7. **Read-only type information.** A keyword may inspect the fields and methods of types it is given.
8. **No redefining** built-in keywords or each other.

```
keyword every <duration interval> [per <scope where>] <block body>
keyword route <verb> <string path> <method handler>
keyword retry <int times> <block body> [else <block fallback>]
```

## 10. Scheduled jobs

- Position gives the default scope; `per` overrides it.
- Defaults: ticks follow the clock, a tick is skipped if the previous run is still going, ticks missed during downtime are dropped. A job never runs in parallel with itself.

```
every 5min { CleanupSessions() }          // top level: once in the cluster
every 5min per node { FlushMetrics() }    // on each machine

actor Session {
    every 30s { Ping() }                  // inside an actor: belongs to each instance
}

every 30s after done { }                  // 30s after the last run finished
every 30s overlap queue { }               // queue a tick instead of skipping it
every 30s catchup { }                     // run ticks missed during downtime
at 03:00 daily { }                        // calendar schedule
```

## 11. Dependency injection and tests

- There is no container. A singleton is an actor declared `per cluster` or `per node` and addressed by its type name (section 13). For actors spawned by hand, wiring is constructor parameters.
- Tests swap implementations with `mock`. The swap happens at compile time and only in test builds, so no interface has to exist just for faking.

```
test "rename fails for unknown user" {
    mock UserStore with FakeStore
    ...
}
```

- The runtime owns clock and scheduler, so a test can fast-forward virtual time. Timers, jobs and timeouts all fire; the test takes milliseconds.

```
test "survives 60 days" {
    server = spawn TcpServer
    advance 60days
    server.Ping()
}
```

## 12. Standard library

Everything is self-written. Tiers:

- **Core** (runs on microcontrollers): numbers, strings, collections, math, time.
- **Runtime:** actors, supervisors, scheduler, timers, clustering, serialization, `Stream<T>`.
- **System:** files, processes, environment, TCP/UDP, DNS.
- **Backend:** HTTP server and client, TLS, `Sealed<T>`, logging, the chore keywords.
- **Data:** database driver and the query keyword. MySQL first; the wire driver is built last.
- **Tooling:** test runner, formatter, editor language server. Package manager later.
- **Not in the box:** graphics, audio, windowing. These are packages over the C-ABI.

Database:

- Queries compile to SQL at build time, so SQL mistakes are compile errors and nothing is translated at run time.
- LINQ-style chains for the common case, checked raw SQL for complex queries. Interpolated values become parameters.
- Both schema directions are supported. Code first: the database is generated from `data` types. Database first: a tool command (`kurz db pull`, in the manner of `dotnet ef`) connects through the wire driver package and writes the schema into the repository as generated Kurz. Builds read only that file, so compilation stays sealed (section 9, rule 5).
- Failures: an unreachable database is an exception (supervisor and `retry` deal with it). A constraint violation or a missing row is a union case.

Migrations, code first:

- A migration is generated by diff, kept in the repository and editable: `kurz db migrate "add size"`, in the manner of `dotnet ef migrations add`. Three differences from EF:
  - A rename is stated in code (`string FullName was Name`) and never guessed as drop plus add.
  - There is no model snapshot file. The compiler replays the migrations to know the schema before, so nothing conflicts on merge.
  - SQL inside a migration is checked against the schema as of that step.
- The new cluster applies pending migrations itself, once (one machine, a cluster-wide lock), before traffic switches. A tool command (`kurz db update`) exists for pipelines that want the step explicit.
- Old and new version share the database during the switch, so the compiler splits every migration in two. Steps that are safe for the old code (a new table, a new column with a default, a new index) run before the switch. Steps that break the old code (drop, tighten) run after the old cluster is gone.
  - The compiler knows every column the old release touches, because all queries are compiled and `kurz release` snapshots them.
  - A step that cannot be made safe is a build error that names the old query it breaks.
  - A renamed column keeps its old physical name until the second phase.
  - There are no down scripts. Before the second phase nothing is destroyed, so rolling back is switching traffic back.

Database first: migrations belong to whoever owns the database. After a change, `kurz db pull`, and the compiler flags every query that no longer fits.

```
users = db.Users.Where(u => u.Age > 18).OrderBy(u => u.Name).Take(10)
users = sql "SELECT * FROM users WHERE age > {minAge}"
```

Formats:

- No text or interchange format ships in the box, not even JSON.
- In the box: a Kurz-native binary format, which clustering needs anyway.
- JSON and Protocol Buffers are first-party packages: written by the language project and versioned with it.
- `route` is format-neutral; a format plugs in.

## 13. Further features

Agreed in a later brainstorm round. Each one is compiled into a program only when the program uses it.

### Actors addressed by key or by name

`per` on an actor says how many of it exist and where:

```
actor Metrics per node { }          // one per machine:    Metrics.Count("hit")
actor Billing per cluster { }       // one in the cluster: Billing.Charge(order)
actor Session per int userId { }    // one per key:        Session[42].Add(item)
actor Cart per caller { }           // one per signed-in client (see "Clients and devices")
actor Circuit per connection { }    // one per client connection
actor Worker { }                    // spawned by hand, the reference is passed around
```

No spawn, no lookup, no registry. The runtime finds a keyed actor or creates it on some machine, places each key, puts idle actors to sleep and rebalances when machines join. For the developer this is horizontal scaling with zero code; the cost is compiler and runtime work. It needs a cluster-wide directory, and during a network split the three-step rule of section 7 applies per key.

- A `per cluster` or `per node` actor is the singleton: no registration and no wiring, and `mock` works on it unchanged. The dependency is no longer visible in a constructor; the compiler can list who uses what.
- A keyed actor that is not `durable` is dropped after an idle time: 10 minutes by default, overridable (`idle 10min` as a sketch). Its state is gone and the next call starts it fresh.

### Durable actors

```
durable actor Subscription per int userId {
    mut plan = Plan.Free
    pub void Trial() {
        plan = Plan.Pro
        wait 30days
        plan = Plan.Free
    }
}
```

Every change reaches an actor as a message, so logging messages to disk makes its state survive crashes and restarts. A log on the local disk does not survive the loss of the machine, which the first version of this section claimed. `durable` therefore has three modes:

- Local disk only: one disk write per message; the state is lost with the machine.
- A copy on N machines before a message counts as stored (`durable 2`): survives the loss of a machine and costs a network round trip per message.
- A copy sent in the background: fast, and the last moments are lost when the machine dies.

The default is two copies when distribution is on and local disk on a single machine. The spelling of the first and third mode is not fixed. Enabled only where the `durable` keyword is used. Kurz gets its own storage engine for this, written in Kurz.

### Inbox overflow

Waiting calls throttle themselves: the caller waits, so load cannot pile up. For everything else the owner's direction is that a full inbox spills into a storage engine local to the node or process, the same engine durable actors use, to free memory and queue work without cluttering everything else.

When that storage is full as well, or the target has none, the sender gets an exception. The inbox works in different modes depending on the deployment:

- `spill`: memory first, then local storage, then an exception in the sender.
- `fail`: a fixed number of slots; when they are full, an exception in the sender.
- `drop oldest`: when full, the oldest waiting message is discarded.
- `drop newest`: when full, the arriving message is discarded. *(assumed: only the name was shown to the owner)*

The default is set per deployment in `project.kz`, and an actor can override it after its name. *(assumed: the owner confirmed the modes; the placement was part of the same question)*

```
// project.kz
deploy server { inbox spill }
deploy esp32  { inbox 32 fail }
```

```
actor Telemetry inbox 1000 drop oldest { }
```

### Cluster simulation tests

Required.

```
test cluster 5 seed 1337 {
    split 2 | 3
    advance 10min
    heal
    check Stock[7].Count() == 40
}
```

Clock, scheduler and network are all Kurz, so several machines run inside one process, deterministically. Any failure reproduces from its seed. C-ABI calls break the determinism.

### Record and replay

An actor is deterministic given its messages. Recording inbox and I/O results turns a crash into a file that can be stepped through. This is built for local development and testing only, not for production environments.

### Packages

```
// project.kz
use "github.com/kurz-lang/json" 1.2
use "github.com/symo/mysql" 0.9 allow network
```

```
use json                       // may touch: nothing
use mysql allow network        // may touch: network only
```

- **Permissions.** Whole-program compilation knows which package opens sockets, reads files, calls C or contains `raw` blocks (section 3). A package that exceeds what the project granted fails to compile.
- **Source only.** The whole-program checks (cycle rule, deadlock rule, permissions, `mock`) need the source, so closed-source binary packages of Kurz code cannot exist. *(assumed)*
- **Home.** A git URL plus a version, pinned by content hash in a lock file. There is no central registry to run; a registry, where one exists, is an index over git (the owner, 2026-10-01, round 4).
- **Versions.** The resolver picks the lowest version that satisfies everyone, as Go does and as NuGet does for transitive packages: no surprise upgrades, and a security fix needs an explicit bump.
  - One version of a package per program; two versions would mean two copies of each type and of each `per cluster` actor inside. *(assumed)*
  - The compiler checks version numbers: `kurz release` compares the `pub` surface with the last release, with the same machinery as for `open`, and a breaking change without a major bump is an error. *(assumed)*
- **C code.** A package declares the C-ABI functions of libraries that already exist on the target (operating system, driver, SDK), or ships prebuilt binaries; both need `allow native`. Kurz never compiles C, and there is no C compiler in the toolchain. A prebuilt binary is opaque; `allow native` is the visible flag.

### Constraints in types

```
data User(int Age where 0..150, string Email where IsEmail else "not an email")
data Range(int From, int To) where From <= To          // a rule across fields

bool IsEmail(string s) => s.Contains("@")

route POST "/users" (User user) => store.Add(user)
```

An invalid value cannot exist. Input from outside yields `User | Invalid` automatically, so validation needs no code in the handler. Custom validators must be possible, as C# has them; a validator is ordinary Kurz code.

- `Invalid` carries every failure (field, rule, message), not only the first, so a form can show all of them at once.
- A validator is a pure function returning `bool`: no I/O and no actor calls. A check that needs the outside world ("email already taken") is not a type rule; it is a union case of the method.

### Clients and devices

Browsers (through WebAssembly), apps and microcontrollers take part, but not as cluster machines: server to client is harder than server to server. The owner's requirement: the developer states in code, easily and securely, which actor runs where, what it may access and how connections are made. Decided on 2026-10-01:

- **Placement.** One project. `on <deployment>` after an actor's name says where it runs; an actor without it runs on the server. The compiler builds one binary per `deploy` block in `project.kz`. A deployment with `connect` is outside, a client; one without is a cluster member. Whatever both sides use, `data` types and pure functions, is compiled into both; there is no shared project. A client holds only call stubs for server actors, so server code cannot ship. The build lists what ships to each client.
- **Two zones.** Inside is the cluster. Outside is everything that connects in: browser, app, device. The outside reaches only methods marked `open`. The server checks every incoming call against a table the compiler made, so a modified client cannot call anything else. Arguments are validated on arrival by their type constraints; the same constraints run in the client, so a rule is written once.
- **Identity.** Inside an `open` method, `caller` says who is on the other end. Sign-in code sets it (`caller.SignIn(user.Id)`); it is kept on the server and cannot be forged. A device gets its key when it is flashed.
  - `per caller`: one actor per signed-in identity. From outside it is addressed by its type name, and a client only ever reaches its own; inside code addresses it by key (`Cart[userId]`). Reading another user's data by changing an id is impossible by construction.
  - `per connection`: the same, but the actor lives and dies with one connection, like a Blazor circuit.
  - Other keyed actors stay reachable when they have `open` methods, and check `caller` themselves.
- **Direction.** Nothing shared ever waits on the outside, because a slow or hostile client would hold a server actor until the timeout.
  - A shared actor (`per cluster`, `per node`, keyed, spawned by hand) reaches a client only with `send`; a waiting call is a compile error.
  - A client's own actor (`per connection`, `per caller`) may wait on that client, since a stall then hurts only that client. Shared actors reach such an actor with `send` only, so nothing shared stalls behind it. The compiler tracks which actors can stall.
  - Device state reaches the backend through a server-side twin actor that the device reports to. A `per caller` actor may also ask its device and wait.
- **Streams.** `Stream<T>` is in the box: a sequence pulled by its consumer. It crosses actors and the boundary, and the runtime batches and paces it. It is the same idea as `IAsyncEnumerable<T>` in SignalR streaming. Big payloads travel as streams, because one message is one value in memory: a 2 GB argument cannot exist, and a hostile client could claim one.
- **Offline.** A waiting call from the outside to the inside has the union case `Offline`, because a lost connection is normal there. It is handled with `else` or listed in the signature. Inside the cluster the exception rule of section 6 stays.
- **Versions.** Clients in the field run old code for months, so "versions never mix" (section 7) cannot hold at this boundary. Breaking changes always happen, and keeping even one version compatible is hard for some teams, so compatibility is opt-in per deployment:
  - `supports current` is the default: nothing is kept compatible and every breaking change is free. A client of an older release gets `Outdated` when it connects; a browser tab reloads itself. The cost is that each release interrupts every connected client.
  - `supports 2..` keeps releases from 2 on compatible, checked by the compiler. `kurz release web 1.4` writes a snapshot of the `open` surface into the repository. The build fails when a change breaks a supported release: a method removed, a type changed, a field removed, a union case the old client does not know. An added field needs a default. The compile error is not a ban on breaking; it is the list of who breaks.
  - A breaking change stays possible while an old release is supported, through a shim that translates the old shape (`was`). The error names the missing shim, and once the old release is dropped the compiler flags the shim as dead.
  - `caller.Version` is there for changes of meaning with the same shape. The compiler sees shape only.
  - Only the handshake is frozen forever: connecting, `Outdated`, and where the update is.
  - The wire format at the boundary carries field numbers, which is slower than inside the cluster.
- **`secret`.** A secret never crosses the boundary in the clear, in either direction, and is never logged.
  - A `secret` field in a normal type is stripped when the value crosses and does not exist on the other side: code there that touches it is a compile error. No separate type per side is needed. A secret lives on the side whose code touches it; both sides touching it is a compile error.
  - A whole `secret data` type cannot cross at all; trying is a compile error. A private key marked this way provably never leaves the client.
  - The only way across is `Sealed<T>`, in the box: the value is encrypted on the sending client for named recipients, and the server stores and routes bytes it cannot open. This is what end-to-end encrypted apps build on.
  - Key exchange, several devices per user and groups are a first-party package. The cipher primitives come through the C-ABI, as with TLS; where they come from in the long run is open (section 14).
  - Limits: the server cannot validate, search or index sealed content, so constraints run on the receiving client after opening. In a browser the server delivers the client code, so a compromised server can ship code that leaks keys; a native app and a flashed device do not have that hole. Who talks to whom, when and how much stays visible.
- Limits per client connection (message size, messages in flight, rate) are on by default and set in the `deploy` block; a client that exceeds them is disconnected. *(assumed)*
- Transport is the runtime's job: WebSocket for browsers, a TLS socket for devices, the Kurz binary format on both. The UI toolkit in the browser is a package, like graphics. *(assumed)*

```
// project.kz
deploy server { }
deploy web {
    target wasm
    connect "wss://shop.example.com"
}
deploy sensor {
    target esp32
    connect "tls://iot.example.com"
    inbox 32 drop oldest
}
```

```
data Item(int ProductId, int Count where 1..99)
data User(int Id, string Name, secret string PasswordHash)

actor Catalog per cluster {
    open List<Product> Search(string text) => products.Where(p => p.Name.Contains(text))
    pub void Reindex() { ... }                   // inside only
}

actor Cart per caller {                          // server side, one per signed-in caller
    mut items = List<Item>()
    open void Add(Item item) { items.Add(item) }
}

actor CartView on web {                          // runs in the browser
    void AddClicked(int productId) {
        Cart.Add(Item(productId, 1)) else { Offline => ShowBanner("offline") }
    }
}

actor Thermometer per caller {                   // server-side twin of one device
    mut last = Reading(0)
    open void Report(Reading r) { last = r }
    pub Reading Last() => last
}

actor Probe on sensor {                          // runs on the ESP32
    every 10s { send Thermometer.Report(ReadAdc()) }
}

temp = Thermometer[12].Last()                    // the backend asks the twin, never the device
```

```
actor Transfer per connection {                  // the client's own actor: it may wait on that client
    open FileId Upload(string name, Stream<Bytes> content) {
        file = Files.Create(name)
        for chunk in content { file.Append(chunk) }
        return file.Close()
    }
    open Stream<Bytes> Download(FileId id) => Files.Open(id).Chunks(256kb)
}

id = Transfer.Upload(picked.Name, picked.Chunks(256kb))            // client
for chunk in Transfer.Download(id) { save.Append(chunk) }
```

```
// project.kz
deploy web { supports current }     // an old tab gets Outdated and reloads itself
deploy app { supports 2.. }         // kept compatible, checked by the compiler
```

```
data User(int Id, string FirstName, string LastName)

was 1.4 User(int Id, string Name) {
    up   => User(Id, Name.Before(" "), Name.After(" "))     // arriving from a 1.4 client
    down => User(Id, "{FirstName} {LastName}")              // leaving to a 1.4 client
}
```

```
secret data PrivateKey(Bytes Value)                        // client only: sending it does not compile
secret data Message(string Text)

box = Sealed<Message>.For(bob.PublicKey, Message("hi"))    // client A
send Chat[room].Post(box)                                  // the server cannot open it
msg = box.Open(myKey) else { Forged => return }            // client B
```

### Speed

Runtime speed has priority everywhere; the compiler may be heavy. One planned optimization: an actor handles one message at a time, so everything allocated while handling it and not stored in actor state can be freed in one sweep at the end. Beating hand-written C in some scenarios is welcome.

Goal (the owner, 2026-10-01): Kurz should not be meaningfully slower than C++ in computation and memory work, or than ASP.NET Core in web serving. This is a goal with gates, not a guarantee for every program. Safe code pays for the checks the compiler cannot remove: the index check, the counter check before a write, counter updates on shared values and the liveness check on a `weak` edge. C++ pays none of them and proves nothing. Where a measured hot path needs it, a `raw` block removes them (section 3). The numbers that turn a gate red (the owner, 2026-10-02, accepted for now): a value tree within 1.1x of C++ `std::map`, HTTP serving within 1.1x of ASP.NET Core, and an idle actor at most 512 bytes. Each number is a ratchet from its first measurement: it may only get tighter, once the evaluation that section 14 keeps open has confirmed it; until then a measurement may still move it. The owner wants the numbers looked at again against measurements (section 14).

## 14. Open

- Whether the numbers behind the speed goal (section 13) hold up against measurements. The owner found a first proposal too loose (within 1.3x and 2x) and accepted the present ones on 2026-10-02 with the words that they need further evaluation.
- What the language reference could not take from this record. Each fork it met is a rule marked `open` there, or, where cases already stand on the rest of the rule, a question listed below that the rule points at (the texts of A12 and F12 did that until round 14); each thing it had to fill in so that a case could be written is marked `proposed`; all are answered by their id ([reference/00-about.md](reference/00-about.md)). After round 10 (2026-10-03) the reference had no `open` and no `proposed` rule; the review of the same day found gaps, and round 11 (the same day) answered them, and the review of that round left two `open` rules and a dozen proposed readings, which round 12 (2026-10-04) answered. The reviews of 2026-10-08 and 2026-10-09 then listed 25 questions here: round 13 (2026-10-09) answered 21, and the four it did not reach are recorded as assumed, proposed on 2026-10-09 and not objected to; the one question of L14 that no answer covered, a duration beyond 64 bits of nanoseconds, is answered by the reading of a duration literal under the wide pair (section 4), as an assumption. The reference has no `open` rule; the readings inside decided rules that are still marked *(proposed)* stand until a round takes them up, and round 13 left one of its own, the id and the line of the stack exception. Round 14 (2026-10-10) answered, by number in the pull request that records it, the 19 readings the two reviews of 2026-10-10 had led to, the three forks they found, the four readings round 13 had not asked about, the associativity of the wrapping operators, 21 readings the third review raised and the readings the compiler's front end had taken; what it opened stands below.
- The naming of the standard library.
- What a full inbox does to a waiting call under the `drop` modes.
- Over-the-air update for devices: whether it is the runtime's or a library's. A device that gets `Outdated` has to be able to update itself.
- Where the cipher primitives come from in the long run, TLS's (section 2) and `Sealed<T>`'s (section 13) alike.
- A formatting of a text that can be chosen, which the owner's answer on the text of a duration (section 8, 2026-10-10) presumes: how it is written and what it can choose; the record has none.
- Whether `us` and `ns` join the units of a duration literal (section 4), so that the text `1s 250us` reads back as a literal.

## 15. Prototype

A throwaway v0 exists outside this repository and was never committed. It was written before this design existed: a C# compiler (`Program.cs`) that emits C and builds through `zig cc`. Its syntax and semantics are not authoritative, and its backend contradicts section 2 (LLVM IR, not C). Only the name carries over.
