---
name: what-developers-dislike-in-go
description: Research of 2026-10-30 - what developers dislike about Go, each finding with its source, title, URL and quoted sentence, and where Kurz stands after the owner's answers of that day; read it against a design change (playbook node B4)
metadata:
  type: reference
---

# What developers dislike about Go, and where Kurz stands

Sources read on 2026-10-30. Each verdict was read against the design record (`kurz-design.md`) and the
reference that day, and is written as it stands after the owner's answers of the same day.

How to read the evidence: survey figures are quoted from the Go Developer Survey results on go.dev. Issue
counts are the thumbs-up reactions on golang/go issues, read through the GitHub API on 2026-10-30; no
sentence holds them. The Stack Overflow Developer Survey 2025 technology page was checked as well. It
reports which languages people use and lists no complaints about Go, so no finding cites it. Two sources are
peer-reviewed studies of real Go code and are marked so. Sources marked "third party" are neither a
survey, nor a study, nor the Go project's own record. The language reference
covers only the sequential core, and the compiler builds no actor. So the verdicts on concurrency
(findings 9, 13, 15 and 18) rest on section 6 of the design record alone.

Summary: 25 findings: 15 avoided, 1 repeated in part; 9 answered and recorded.

Verdicts: AVOIDED, REPEATED (Kurz has the problem too), OPEN, NOT APPLICABLE; an answered finding names
the owner's choice and the section of the record whose trace holds each option with its cost.
Each finding keeps its sources; the prose, the code samples and the options are cut.
Before a design change, name the finding it meets (knowledge/diagram-design-change-playbook.md, B4).

## 1. Error handling is verbose: `if err != nil` after every call

- Evidence: Go Developer Survey 2024 H1 Results (https://go.dev/blog/survey2024-h1-results): "The top responses in the closed-form were learning how to write Go effectively (15%) and the verbosity of error handling (13%)." [ On | No ] syntactic support for error handling (https://go.dev/blog/error-syntax): "The verbosity is real, and so it's no wonder that complaints about error handling have topped our annual user surveys for years."
- Since: the Go team tried `check`/`handle` (2018), `try` (2019) and `?` (2024). It dropped all three, and in June 2025 it stopped work on a syntax change: "To be more precise, we should stop trying to solve the syntactic problem, at least for the foreseeable future." The remedy splits the community. The most-upvoted golang/go issue is "proposal: leave "if err != nil" alone?" (https://github.com/golang/go/issues/32825, 2007 thumbs-up).
- Kurz: AVOIDED. Section 5 says: "**Unwrap rule:** calling such a method yields the success value directly. Every other case leaves the calling method and goes to its caller." Propagation costs zero extra lines per call.

## 2. Idioms, project layout and a consistent style are hard to keep

- Evidence: Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025): "The top three frustrations respondents reported were "Ensuring our Go code follows best practices / Go idioms" (33% of respondents), "A feature I value from another language isn't part of Go" (28%), and "Finding trustworthy Go modules and packages" (26%)." Go Developer Survey 2024 H2 Results (https://go.dev/blog/survey2024-h2-results): "The most commonly reported challenges for teams were maintaining consistent coding standards across our Go codebase (58%), identifying performance issues in a running Go program (58%) and identifying resource usage inefficiencies in a running Go program (57%)."
- Since: Go 1.26 (https://go.dev/doc/go1.26) rebuilt `go fix` as "the home of Go's *modernizers*", which rewrite code "to the latest idioms and core library APIs".
- Kurz: answered: "Formatter + lint", section 12 of the record. Before the answers: OPEN.

## 3. No sum types or discriminated unions

- Evidence: proposal: spec: add sum types / discriminated unions (https://github.com/golang/go/issues/19412): open since 2017, 1044 thumbs-up. Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025): "These open-text comments largely focused on error handling and reporting patterns, enums and sum types, nil pointer safety, and general expressivity / verbosity".
- Since: nothing in the language. The Go FAQ (https://go.dev/doc/faq#variant_types): "We considered adding variant types to Go, but after discussion decided to leave them out because they overlap in confusing ways with interfaces."
- Kurz: AVOIDED. Section 5 says: "The return type is a union. The **first case is the success case**." It also says: "**`match` lists every case** of the union; a missing case is a compile error." Unions are decided as D9 and the exhaustive `match` as O8.

## 4. Generics stop short: no generic methods, no switch on a type parameter

- Evidence: proposal: spec: allow type parameters in methods (https://github.com/golang/go/issues/49085): 907 thumbs-up. spec: generic methods for Go (https://github.com/golang/go/issues/77273): 965 thumbs-up. proposal: spec: generics: type switch on parametric types (https://github.com/golang/go/issues/45380): open, 494 thumbs-up.
- Since: Go 1.27 (https://go.dev/doc/go1.27): "Go 1.27 now supports generic methods: a method declaration may declare its own type parameters." The limit stays for interfaces: "Note that methods of interfaces may not declare type parameters nor can interface methods be implemented by generic methods."
- Kurz: answered: "Both, as C#", section 4 of the record. Before the answers: OPEN.

## 5. No real enums: `iota` constants

- Evidence: proposal: spec: add typed enum support (https://github.com/golang/go/issues/19814): open, 836 thumbs-up. Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025) found "that majorities of respondents enjoy using languages with inheritance, type-safe enums, and exceptions", with the table row "Type-safe enums | 65%".
- Kurz: AVOIDED. Section 4 makes an `enum` "the short form of a union of cases without fields": its values are cases, and "a `match` has to list every one".

## 6. Verbose function literals and no string interpolation

- Evidence: proposal: spec: short function literals (https://github.com/golang/go/issues/21498): open, 674 thumbs-up, 988 comments. proposal: Go 2: string interpolation (https://github.com/golang/go/issues/34174): closed, 438 thumbs-up. The 2025 survey lists "general expressivity / verbosity" among the frustrations (finding 3).
- Kurz: AVOIDED. Section 4 decides "lambdas as in C# (`x => x * 2`)".

## 7. Nil pointer dereferences panic at run time

- Evidence: Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025) names "nil pointer safety" among the top frustrations, with the answers "There is nothing (in the compiler) that stops me from using a maybe nil pointer, or using a value without checking the err first." and "I like [Go] but I didn't expect it to have nil pointer exceptions :)".
- Kurz: AVOIDED. Section 4 says: "Null exists and is enforced: `T?` is short for `T | null`. Using a nullable value without a check is a compile error." That error is `nullable-unchecked` (N2).

## 8. Finding trustworthy modules

- Evidence: Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025): "Finding trustworthy Go modules and packages" (26%), the third frustration (finding 2), and "One is that they considered many 3rd-party modules to be of marginal quality, making it hard for really good modules to stand out."
- Since: the survey post suggests "quality signals" on pkg.go.dev.
- Kurz: answered: "Index with grants", section 13 of the record. Before the answers: OPEN.

## 9. Data races with no compile-time safety

- Evidence: A Study of Real-World Data Races in Golang, a peer-reviewed study (PLDI 2022) (https://arxiv.org/abs/2204.00764): "We have deployed this detector in Uber's 46 million lines of Go codebase hosting 2100 distinct microservices, found over 2000 data races, and fixed over 1000 data races, spanning 790 distinct code patches submitted by 210 unique developers over a six-month period." The Go FAQ (https://go.dev/doc/faq#atomic_maps): "This was not an easy decision, however, since it means uncontrolled map access can crash the program." The sync package source (https://github.com/golang/go/blob/master/src/sync/mutex.go): "A Mutex must not be copied after first use."
- Kurz: AVOIDED. Section 6 says: "An actor handles one message at a time, so there are no locks and no data races inside it." It also says: "Two actors can never reach the same mutable object." Kurz has no mutex to copy.

## 10. Garbage-collector tuning and memory limits

- Evidence: Go Developer Survey 2024 H2 Results (https://go.dev/blog/survey2024-h2-results): teams report "identifying resource usage inefficiencies in a running Go program (57%)" (finding 2). A Guide to the Go Garbage Collector (https://go.dev/doc/gc-guide): "Furthermore, in some applications, these transient peaks can be rare and hard to predict, leading to occasional, unavoidable, and potentially costly out-of-memory conditions." proposal: arena: new package providing memory arenas (https://github.com/golang/go/issues/51317): open, 396 thumbs-up.
- Since: `GOMEMLIMIT` in Go 1.19. Go 1.26 (https://go.dev/doc/go1.26): "The Green Tea garbage collector, previously available as an experiment in Go 1.25, is now enabled by default after incorporating feedback."
- Kurz: REPEATED in part: the sweep of an actor's heap paces itself as Go's collector does, "Live size x2, as Go", with the factor a setting of the deployment, a knob as `GOGC` is (section 13 of the record). Section 3 says: "No lifetimes, no borrow annotations, no global garbage collector." It adds: "Each actor has its own heap." A dead actor's heap is freed whole.

## 11. Zero values stand in for "absent": nil maps, unset fields, pointers as options

- Evidence: The Go Programming Language Specification (https://go.dev/ref/spec#Map_types): "A nil map is equivalent to an empty map except that no elements may be added." spec: expression to create pointer to simple types (https://github.com/golang/go/issues/45624): 618 thumbs-up. proposal: encoding/json, encoding/xml: support zero values of structs with omitempty (https://github.com/golang/go/issues/11939): 240 thumbs-up.
- Since: Go 1.24 (https://go.dev/doc/go1.24) added `omitzero`: "In particular, unlike omitempty, omitzero omits zero-valued time.Time values, which is a common source of friction." Go 1.26 (https://go.dev/doc/go1.26) lets `new` take an expression, "particularly useful when working with serialization packages such as encoding/json or protocol buffers that use a pointer to represent an optional value".
- Kurz: AVOIDED. K14 (decided) sets C#'s rule aside, "under which a constructor assigns any number of times and an unassigned field gets the type's zero value, `default(T)`, which Kurz has nowhere else".

## 12. Loop variables captured by closures

- Evidence: Fixing For Loops in Go 1.22 (https://go.dev/blog/loopvar-preview): "This kind of mistake has caused production problems at many companies, including a publicly documented issue at Lets Encrypt." spec: less error-prone loop variable scoping (https://github.com/golang/go/issues/60078): 453 thumbs-up.
- Since: Go 1.22 (https://go.dev/doc/go1.22): "The long-standing "for" loop gotcha with accidental sharing of loop variables between iterations is now resolved."
- Kurz: AVOIDED, for every variable and not only loop variables. F15 (decided): "A lambda takes the value each variable it reads (F11) has when the lambda is made, as an assignment does (M3)." F11 makes an assignment to a captured variable the error `capture-assign`.

## 13. Blocking bugs and channel deadlocks

- Evidence: Understanding Real-World Concurrency Bugs in Go, a peer-reviewed study (ASPLOS 2019) (https://songlh.github.io/paper/go-study.pdf): "We analyzed 171 concurrency bugs in total, with more than half of them caused by non-traditional, Go-specific problems." The same paper: "Overall, we found that there are around 42% blocking bugs caused by errors in protecting shared memory, and 58% are caused by errors in message passing." And: "The built-in deadlock detector can only detect two blocking bugs, BoltDB#392 and BoltDB#240, and fail in all other cases".
- Kurz: AVOIDED for channels, which Kurz does not have. The blocking bugs the study counts in message passing meet the three layers of section 6 on deadlocks: a compile error for a loop of waiting calls between actor types, detection at run time at every wait, and a timeout on every call; across machines a crossing of chains falls to the timeout.

## 14. No immutability

- Evidence: proposal: spec: immutable type qualifier (https://github.com/golang/go/issues/27975): 387 thumbs-up, closed as declined on 2026-04-08. The proposal opens: "Immutability is a technique used to prevent mutable shared state, which is a very common source of bugs, especially in concurrent environments".
- Kurz: AVOIDED. Section 4 says: "Immutable by default; `mut` marks what may change." M2 (decided): "Without `mut`, nothing reachable through a variable changes." The guarantee stops at a class instance, whose `mut` fields change "through every reference to the instance" (K2), as in C#.

## 15. Goroutine leaks

- Evidence: Go 1.26 Release Notes (https://go.dev/doc/go1.26): "A *leaked* goroutine is a goroutine blocked on some concurrency primitive (channels, sync.Mutex, sync.Cond, etc) that cannot possibly become unblocked." It shows a real-world leak: an early `return` in a collector loop leaves every sender blocked on an unbuffered channel. The paper of finding 13 shows the same shape in Kubernetes.
- Since: the `goroutineleak` profile, an experiment in Go 1.26 and generally available in Go 1.27 (https://go.dev/doc/go1.27). Its limit, in the Go 1.26 notes: "While it is impossible to detect permanently blocked goroutines in all cases, this approach detects a large class of such leaks."
- Kurz: answered: "With its supervisor", section 6 of the record. Before the answers: OPEN.

## 16. Network timeouts default to none

- Evidence: net/http source (https://github.com/golang/go/blob/master/src/net/http/client.go): "A Timeout of zero means no timeout." gosec, third party (https://github.com/securego/gosec): "G112 — Detect `ReadHeaderTimeout` not configured as a potential risk" and "G114 — Use of `net/http` serve function that has no support for setting timeouts".
- Kurz: answered: "5 s default, settable" for a read, a write and `Connect`, "Second form, as Rust" for another bound of `Connect`, and "Accept waits, as Rust", section 12 of the record; how the bound holds for an actor's socket, which runs without blocking under `epoll`, stands open (section 14). Before the answers: REPEATED.

## 17. Errors lose where they came from

- Evidence: Results from the 2025 Go Developer Survey (https://go.dev/blog/survey2025): the open-text frustrations "largely focused on error handling and reporting patterns" (finding 3), with the answer "Still not sure what is the best way to do error handling." proposal: errors: add (stack)trace at error annotation (https://github.com/golang/go/issues/60873): open, 13 thumbs-up, "If we just `return err` one time we loose the path."
- Kurz: answered: "Hop lines in test builds", section 5 of the record. Before the answers: OPEN.

## 18. context.Context threaded through every call

- Evidence: Go Concurrency Patterns: Context (https://go.dev/blog/context): "At Google, we require that Go programmers pass a Context parameter as the first argument to every function on the call path between incoming and outgoing requests." proposal: context/v2: update context package (https://github.com/golang/go/issues/28342): open, 50 thumbs-up, "Context values are passed everywhere explicitly, which troubles some people."
- Kurz: answered: "Deadline on chain ID", section 6 of the record. Before the answers: OPEN.

## 19. A nil interface is not equal to an interface that holds a nil pointer

- Evidence: the Go FAQ (https://go.dev/doc/faq#nil_error): "This situation can be confusing, and arises when a nil value is stored inside an interface value such as an error return".
- Kurz: AVOIDED. N1 (decided): "`T?` is short for `T | null`: a value of that type is a `T` or `null`." There is one `null`, a case of a union and not a pointer value inside a box.

## 20. Slices alias when `append` reuses the array

- Evidence: The Go Programming Language Specification (https://go.dev/ref/spec#Appending_and_copying_slices): "Otherwise, append re-uses the underlying array." Robust generic functions on slices (https://go.dev/blog/generic-slice-functions): "In fact, this task is so error-prone that we should not put the burden on the user to write it." The data-race study of finding 9 counts slices passed to goroutines among its patterns.
- Kurz: AVOIDED. M3 (decided): "Assigning a value or passing it gives the other side its own value".

## 21. Shadowing with `:=`

- Evidence: spec: various changes to := (https://github.com/golang/go/issues/377): open since 2009, 119 comments: "The := in the if statement causes a new err variable that shadows the return parameter."
- Since: a `shadow` analyzer in golang.org/x/tools, which its own documentation says "generates too many false positives" and is not enabled by default.
- Kurz: AVOIDED. V8 (decided): "`name = expression` assigns when a variable of that name is visible, and declares one when none is. A variable therefore cannot hide another one".

## 22. Unused variables and imports are compile errors

- Evidence: the Go FAQ (https://go.dev/doc/faq#unused_variables_and_imports): "For these reasons, Go refuses to compile programs with unused variables or imports, trading short-term convenience for long-term build speed and program clarity." It also says: "Some have asked for a compiler option to turn those checks off or at least reduce them to warnings."
- Kurz: answered: "Warn in test, error release", section 8 of the record. Before the answers: REPEATED.

## 23. Interfaces are satisfied implicitly

- Evidence: the Go FAQ (https://go.dev/doc/faq#guarantee_satisfies_interface): "You can ask the compiler to check that the type T implements the interface I by attempting an assignment using the zero value for T or pointer to T, as appropriate". The idiom is `var _ I = (*T)(nil)`.
- Kurz: AVOIDED. Section 4: "a class names its base class and its interfaces after `:`", as in C#, and K6 (decided): "A class inherits from at most one class and implements any number of interfaces, as in C#." A generic limit `T: Shape` accepts only a type that names `Shape` (T19).

## 24. The time-format layout `2006-01-02 15:04:05`

- Evidence: the time package source (https://github.com/golang/go/blob/master/src/time/format.go): "It is a regrettable historic error that the date uses the American convention of putting the numerical month before the day." Issues such as "time: Time.Format() should fail when the layout string is unexpected" (https://github.com/golang/go/issues/34997) show the trap, with few votes.
- Kurz: answered: "C# patterns + RFCs", section 12 of the record. Before the answers: OPEN.

## 25. `defer` runs at the end of the function, not of the loop body

- Evidence: The Go Programming Language Specification (https://go.dev/ref/spec#Defer_statements): "Instead, deferred functions are invoked immediately before the surrounding function returns, in the reverse order they were deferred." The Go toolchain made this mistake itself: "cmd/covdata: too many open files due to `defer f.Close()` in for loop" (https://github.com/golang/go/issues/68468), with backports to Go 1.21 and 1.22.
- Since: nothing in the language. The third-party staticcheck rule SA9001 flags it: "Defers in range loops may not run when you expect them to".
- Kurz: AVOIDED. Kurz has no `defer`: a file or a socket closes when the last value that holds it lets go of it ("At the last count, as Rust Drop", section 12 of the record), which a value made in a loop's body does at the end of that body, as its count drops at the end of its scope (section 4).
