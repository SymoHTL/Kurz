---
name: what-developers-dislike-in-csharp
description: Research of 2026-10-30 - what developers dislike about C#, each finding with its source, title, URL and quoted sentence, and where Kurz stands after the owner's answers of that day; read it against a design change (playbook node B4)
metadata:
  type: reference
---

# What developers dislike about C#, and where Kurz stands

Sources read on 2026-10-30. Each verdict was read against the design record (`kurz-design.md`) and the
reference that day, and is written as it stands after the owner's answers of the same day.

Surveys: the JetBrains page "The State of .NET 2025" (https://lp.jetbrains.com/the-state-of-dotnet-2025/)
names no complaint about the C# language by topic, and no other survey read gives one. The Stack
Overflow survey was not read. So the order below rests on how much space the language team's own
pages give a pitfall, not on survey numbers, and no finding carries a survey figure.

Summary: 23 findings: 11 avoided; 12 answered and recorded.

Verdicts: AVOIDED, REPEATED (Kurz has the problem too), OPEN, NOT APPLICABLE; an answered finding names
the owner's choice and the section of the record whose trace holds each option with its cost.
Each finding keeps its sources; the prose, the code samples and the options are cut.
Before a design change, name the finding it meets (knowledge/diagram-design-change-playbook.md, B4).

## 1. Null references get past the compiler

- Evidence: "Nullable reference types - C#" (https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references): "You declare which variables are intended to hold `null` and which aren't, and the compiler warns when those declarations don't match how your code uses them. The runtime behavior of your program is unchanged." The same page: "Use `!` sparingly. Each occurrence is a place the compiler can no longer protect you." and "Two patterns can leave a non-nullable reference holding `null` without a warning." "! (null-forgiving) operator - C# reference" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/null-forgiving): "The null-forgiving operator has no effect at run time."
- Kurz: AVOIDED. N2 (decided, §4): "Using a value of type `T?` where a `T` is needed, or reaching a member through it, without a check is the compile error `nullable-unchecked`." In §4, `!` only "turns a test around"; the record has no form that silences the check.

## 2. async and await have traps of their own

- Evidence: "Async/Await - Best Practices in Asynchronous Programming" (archived magazine article, https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming): "With async void methods, there is no Task object"; it names "“Why does my partially async code deadlock?”" the most-asked question of newcomers; and "it’s definitely true that asynchronous code tends to drive surrounding code to also be asynchronous."
- Kurz: AVOIDED. §6: "There is no `async`/`await`. Every call looks synchronous; the runtime parks the actor while it waits." No call returns a task that a caller can drop or block on, and no function has a colour.

## 3. A blocking call starves the thread pool

- Evidence: "Debug ThreadPool Starvation - .NET" (https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-threadpool-starvation): "ThreadPool starvation occurs when the pool has no available threads to process new work items and it often causes applications to respond slowly." and "This is the sync-over-async code pattern, which is known to block threads and is the most common cause of ThreadPool starvation."
- Kurz: answered: "Readiness I/O, as Tokio", section 12 of the record. Before the answers: OPEN.

## 4. Exceptions carry normal failures, and no signature shows them

- Evidence: "Why doesn't C# have checked exceptions?" (archived C# FAQ, https://learn.microsoft.com/en-us/archive/blogs/csharpfaq/why-doesnt-c-have-checked-exceptions): "Checked exceptions are a very hotly debated topic in some circles, particularly for experienced Java developers moving to, or additionally learning, C#." and "Note that without the CLR itself supporting checked exceptions, it would be effectively impossible for C# to do so alone." "Best practices for exceptions - .NET" (https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions): "For conditions that are likely to occur but might trigger an exception, consider handling them in a way that avoids the exception."
- Kurz: AVOIDED. §5: "The language enforces the C# `OneOf<>` pattern: every method declares every outcome that can happen under normal use." O3 (decided, §5), honest signatures: "a propagated case must be listed in the calling method's own signature. Otherwise it is a compile error." What a signature does not show is a crash, which no code catches: "There is no `catch`. An exception kills the actor it happens in" (§5).

## 5. No discriminated unions, no exhaustive match

- Evidence: "C# 15 union types" on the .NET blog (https://devblogs.microsoft.com/dotnet/csharp-15-union-types/): "Union types have been frequently requested for C#, and they're here."
- Since: C# 15 union types, which the same post says "declares that a value is exactly one of a fixed set of types with compiler-enforced exhaustive pattern matching". The post: "Union types are available starting with .NET 11 Preview 2."
- Kurz: AVOIDED. D9 (decided, §4, §5) makes unions part of the language, and §5 says "`match` lists every case of the union; a missing case is a compile error." (O8).

## 6. An enum is an integer in disguise

- Evidence: "Parameter Design - Framework Design Guidelines" (https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/parameter-design): "The CLR allows casting any integer value into an enum value even if the value is not defined in the enum."
- Kurz: AVOIDED. D12 (decided, §4): an enum "is the short form of a union of cases without fields (D8, D9): a `match` on it covers every value (O8)", and "A value is not an integer." A number from outside goes through `From`, whose result lists `Invalid`, because "A number arrives from a file, a database or the wire, where a wrong one is normal use, so it is an outcome (section 5) and not an exception." (§4).

## 7. Garbage collection pauses the program

- Evidence: "Latency Modes - .NET" (https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency): "To reclaim objects, the garbage collector (GC) must stop all the executing threads in an application."
- Kurz: answered: "After a threshold", section 13 of the record. Before the answers: OPEN.

## 8. Shared collections race between threads

- Evidence: "Thread-Safe collections - .NET" (https://learn.microsoft.com/en-us/dotnet/standard/collections/thread-safe/): "However, the System.Collections.Generic classes don't provide any thread synchronization; user code must provide all synchronization when items are added or removed on multiple threads concurrently."
- Kurz: AVOIDED. §6: "An actor handles one message at a time, so there are no locks and no data races inside it." and "Two actors can never reach the same mutable object."

## 9. `==` and `Equals` give two answers

- Evidence: "C# Equality comparisons - C#" (https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/expressions/equality): "Type authors can change those defaults, but that mental model prevents subtle bugs where two objects that look identical aren't considered equal, or where a mutation through one variable silently changes what another variable sees." The same page: "When variables are typed as an interface, `==` checks whether the interface variables refer to the same object. A call to `Equals` still runs the underlying object's implementation."
- Kurz: answered: "Error on Equals" (the owner, 2026-10-30), section 4 of the record. A `double` or a `float` compares as C#'s `Equals` does, NaN equal to NaN, in every place section 4 names: a field of a `data` value (the owner, 2026-10-09 and 2026-10-10), the `double` or `float` case of a union in such a field (the owner, 2026-10-24), a map's key alone and as a field of a `data` key, and an item of a list a `data` value or a key holds ("As C#" and "Equals inside data/keys", the owner, 2026-10-30), and the case of a union key, a reading of the second that section 4 marks assumed; while `==` on such a value alone, on a union that stands alone and on two lists that stand alone compares by IEEE's `==`: a second equality beside `==`, which the owner chose with that cost named. Before the answers: REPEATED.

## 10. A mutable key gets lost in a hash table

- Evidence: "Object.GetHashCode Method" (https://learn.microsoft.com/en-us/dotnet/api/system.object.gethashcode): "Otherwise, you might think that the mutable object is lost in the hash table."
- Kurz: answered: "Error at the Map key", section 4 of the record, for a class that is itself the key type; whether the error reaches a key type that holds such a class, or one that a type parameter stands for, stands open (section 14). Before the answers: REPEATED.

## 11. A method on a struct changes a copy

- Evidence: "Struct Design - Framework Design Guidelines" (https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/struct): "Because the copy is created implicitly, developers might not be aware that they are mutating the copy, and not the original value."
- Kurz: AVOIDED. M5 (decided, §4): a `mut` method called "on a value that is not reachable through a `mut` variable is the compile error `mut-required`." M4 (decided, §4): "Assigning to a field at the end of a path that starts at a `mut` variable changes that variable's value." F11 (decided, §4) makes assigning a captured variable inside a lambda the compile error `capture-assign`.

## 12. Allocations the source does not show

- Evidence: "Boxing and Unboxing - C#" (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/types/boxing-and-unboxing): "When a value type is boxed, a new object must be allocated and constructed." and "Boxing is implicit; unboxing is explicit."
- Kurz: answered: "Warning rule + report", section 3 of the record. Before the answers: REPEATED.

## 13. A query runs again at every read

- Evidence: "CA1851: Possible multiple enumerations of 'IEnumerable' collection" (https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1851): "If the enumeration operation itself is expensive, for example, a query into a database, multiple enumerations would hurt the performance of the program." and "If the enumeration operation has side effects, multiple enumerations might result in bugs." "Client vs. Server Evaluation - EF Core" (https://learn.microsoft.com/en-us/ef/core/querying/client-eval): "Though if you're enumerating multiple times, then storing results in a list helps more since there's only one query to the database."
- Kurz: answered: "Consumed by its read", section 12 of the record. Before the answers: OPEN.

## 14. A forgotten Dispose leaks a handle

- Evidence: "CA2000: Dispose objects before losing scope" (https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2000): "A local object of an IDisposable type is created, but the object is not disposed before all references to the object are out of scope."
- Kurz: AVOIDED. S5 (decided, §12): "the file closes when the last value that holds it lets go of it" (the owner, 2026-10-29, "At the last count, as Rust Drop", against a `using` block).

## 15. An event handler keeps its listener alive

- Evidence: "Weak event patterns - WPF" (https://learn.microsoft.com/en-us/dotnet/desktop/wpf/events/weak-event-patterns): "Listening for events can lead to memory leaks." and "However, this technique creates a strong reference from the event source to the event listener."
- Kurz: answered: "Subscription value" and "Ring stays an error", section 3 of the record: the holder of a subscription decides how long the source keeps the handler, and a handler that reads that holder closes a ring, which is a compile error instead of a listener kept alive. Before the answers: REPEATED.

## 16. Dates and times without a zone

- Evidence: "Compare types related to date and time - .NET" (https://learn.microsoft.com/en-us/dotnet/standard/datetime/choosing-between-datetime): "Unless a particular DateTime value represents UTC, that date and time value is often ambiguous or limited in its portability." The page also advises: "consider DateTimeOffset as the default date and time type for application development." and notes: "The time zone is often not known, particularly in web or network apps."
- Kurz: answered: "Rust's split", sections 12 and 10 of the record. Before the answers: OPEN.

## 17. String comparison depends on the machine's culture

- Evidence: "Best Practices for Comparing Strings in .NET" (https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-strings): "Some overloads with default parameters (those that search for a Char in the string instance) perform an ordinal comparison, whereas others (those that search for a string in the string instance) are culture-sensitive. It's difficult to remember which method uses which default value, and easy to confuse the overloads." and "The canonical example is the Turkish-I problem."
- Kurz: answered: "Ordinal core", section 4 of the record. Before the answers: OPEN.

## 18. Integer overflow is silent by default

- Evidence: "The checked and unchecked statements - overflow-checking - C# reference" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked): "By default the value of that option is unset and integral-type arithmetic operations and conversions are executed in an **unchecked** context." and "In an unchecked context, the operation result is truncated by discarding any high-order bits that don't fit in the destination type." No source read counts this as a complaint; the evidence shows the default, not how many dislike it.
- Kurz: answered: "Project setting", section 4 of the record. Before the answers: REPEATED in a release build.

## 19. Two packages want two versions of a third

- Evidence: "NuGet Error NU1107" (https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1107): "Two different packages are asking for two different versions of 'PackageA'. The project needs to choose which version of 'PackageA' to use." and "Generally, picking the higher version is the right choice."
- Kurz: answered: "One + override", section 13 of the record. Before the answers: OPEN.

## 20. Reflection breaks trimming and native AOT

- Evidence: "Fixing trim warnings - .NET" (https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings): "Trim warnings indicate potential issues that could cause behavior changes or crashes after trimming." and, of the ways to fix them, "Eliminate reflection - This is always the best option if possible."
- Kurz: AVOIDED. §2: "Compilation is whole-program." The chores C# does by reflection (mapping, validation, dependency wiring, database queries) are compile-time keywords in §9, under rule 5, "Sealed at compile time. Keyword code cannot read files, network or clock.", and rule 7, "Read-only type information." The record names no reflection at run time: section 4's reading of a type in generic code happens while the program compiles, in each copy, and the word stands there for C#'s alone.

## 21. A dependency injection container fails at run time

- Evidence: "Dependency injection guidelines - .NET" (https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines): "**Scope leakage**: A singleton can inadvertently capture scoped or transient dependencies, effectively promoting them to singletons and causing unintended side effects." and "When you validate the scopes, you get an InvalidOperationException with a message similar to "Cannot consume scoped service 'Bar' from singleton 'Foo'."."
- Kurz: AVOIDED. §11: "There is no container. A singleton is an actor declared `per cluster` or `per node`", and `mock` swaps an implementation at compile time.

## 22. An ORM query fails when it runs

- Evidence: "Client vs. Server Evaluation - EF Core" (https://learn.microsoft.com/en-us/ef/core/querying/client-eval): "If EF Core detects an expression, in any place other than the top-level projection, which can't be translated to the server, then it throws a runtime exception."
- Kurz: AVOIDED. §12: "Queries compile to SQL at build time, so SQL mistakes are compile errors and nothing is translated at run time." A missing row is a union case (§12).

## 23. Too many ways to write one thing

- Evidence (an article, the last resort): "What’s coming in C# 12? Devs debate whether it's too much or not enough" (https://www.devclass.com/development/2023/05/30/whats-coming-in-c-12-devs-debate-whether-its-too-much-or-not-enough/1630606): "Another common complaint is that C# risks becoming too complex, or may already be."
- Kurz: answered: "Printed counts", section 1 of the record, and "Grant for keywords", section 9. Before the answers: OPEN.
