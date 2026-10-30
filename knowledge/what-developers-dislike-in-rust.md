---
name: what-developers-dislike-in-rust
description: Research of 2026-10-30 - what developers dislike about Rust, each finding with its source, title, URL and quoted sentence, and where Kurz stands after the owner's answers of that day; read it against a design change (playbook node B4)
metadata:
  type: reference
---

# What developers dislike about Rust, and where Kurz stands

Sources read on 2026-10-30. Each verdict was read against the design record (`kurz-design.md`) and the
reference that day, and is written as it stands after the owner's answers of the same day.

Sources are the Rust project's own surveys and records where they exist. A source marked "last
resort" is a widely cited article, tutorial or forum thread outside the project's record. Section
numbers name sections of `kurz-design.md`; rule ids (M7, O3, T4 ...) name rules of `reference/`.

Summary: 25 findings: 5 avoided, 1 not applicable; 19 answered and recorded.

Verdicts: AVOIDED, REPEATED (Kurz has the problem too), OPEN, NOT APPLICABLE; an answered finding names
the owner's choice and the section of the record whose trace holds each option with its cost.
Each finding keeps its sources; the prose, the code samples and the options are cut.
Before a design change, name the finding it meets (knowledge/diagram-design-change-playbook.md, B4).

## 1. Builds are slow, above all the rebuild after a small change

- Evidence: 2024 State of Rust Survey Results (https://blog.rust-lang.org/2025/02/13/2024-State-Of-Rust-Survey-results/): "Perhaps unsurprisingly, slow compilation was at the top of the list, as it seems to be a perennial concern of Rust users."; Rust compiler performance survey 2025 results (https://blog.rust-lang.org/2025/09/10/rust-compiler-performance-survey-2025-results/): "Around 45% respondents who answered that they are no longer using Rust said that at least one of the reasons why they stopped were long compile times." and "the fact that 55% of respondents have to wait more than ten seconds for a rebuild is far from ideal."; 2025 State of Rust Survey Results (https://blog.rust-lang.org/2026/03/02/2025-State-Of-Rust-Survey-results/): "resource usage (slow compile times and storage usage) is still up there."
- Since: Announcing Rust 1.90.0 (https://blog.rust-lang.org/2025/09/18/Rust-1.90.0/): "LLD is now the default linker on x86_64-unknown-linux-gnu". The performance survey names the parallel front end and the Cranelift backend as goals; neither is stable.
- Kurz: answered: "Recompute what changed", section 2 of the record. Before the answers: REPEATED.

## 2. The borrow checker and lifetimes are hard to learn

- Evidence: 2024 State of Rust Survey Results (https://blog.rust-lang.org/2025/02/13/2024-State-Of-Rust-Survey-results/): "Similarly as last year, around 31% of those who did not identify as Rust users cited the perception of difficulty as the primary reason for not using Rust."; the Rust FAQ, archived in the project's repository (https://github.com/rust-lang/prev.rust-lang.org/blob/master/en-US/faq.md): "While the rules themselves are simple, following them consistently is not, particularly for those unaccustomed to reasoning about lifetimes and ownership."
- Kurz: AVOIDED. Section 3 (R1, decided): "No lifetimes, no borrow annotations, no global garbage collector."; a value copies in meaning and shares its storage while no side writes (section 4, M3).

## 3. Fear that the language keeps growing more complex

- Evidence: 2025 State of Rust Survey Results (https://blog.rust-lang.org/2026/03/02/2025-State-Of-Rust-Survey-results/): "But at the same time concerns persist about the language becoming more and more complex." The 2024 results list the same worry among the top answers.
- Kurz: answered: "Printed counts", section 1 of the record. Before the answers: OPEN.

## 4. Async splits functions into two colours

- Evidence: 2024 State of Rust Survey Results (https://blog.rust-lang.org/2025/02/13/2024-State-Of-Rust-Survey-results/): "We have seen mentions of struggles with async programming (an all-time favourite), debuggability of errors (which people generally love, but they are not perfect for everyone) or Rust tooling being slow or resource intensive (rust-analyzer and rustfmt)."; the async vision document's status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Mixing sync and async code is tricky and it's not always obvious how to do it".
- Since: Announcing Rust 1.75.0 (https://blog.rust-lang.org/2023/12/28/Rust-1.75.0/): "Rust 1.75 supports use of async fn and -> impl Trait in traits."; the 2025 results name "the very popular let chains and async closures features, which were stabilized last year".
- Kurz: REPEATED in part: no `async` is written. Section 6: "There is no `async`/`await`. Every call looks synchronous; the runtime parks the actor while it waits." The colour does not vanish, it moves into the compiler: a function that can wait becomes a state machine (section 6, assumed), and a function that takes the C calling convention may not wait: "a wait for a reply inside it, or inside a function it calls that can wait, which section 6 makes a state machine, is a compile error" (section 2, decided).

## 5. Debugging support is weak

- Evidence: 2024 State of Rust Survey Results (https://blog.rust-lang.org/2025/02/13/2024-State-Of-Rust-Survey-results/): "Other challenges included subpar support for debugging Rust and high disk usage of Rust compiler artifacts."; 2025 State of Rust Survey Results (https://blog.rust-lang.org/2026/03/02/2025-State-Of-Rust-Survey-results/): "The debugging story slipped from 2nd to 4th place (~2pp)."; the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Stacktraces are full of gobbly gook and hard to read."
- Since: The 2025 results post: "We just started a survey to learn more about it!"
- Kurz: answered: "Variables in test builds", section 2 of the record. Before the answers: OPEN.

## 6. The editor is slow on large projects

- Evidence: Rust compiler performance survey 2025 results (https://blog.rust-lang.org/2025/09/10/rust-compiler-performance-survey-2025-results/): "Around 87% of respondents say that they use inline annotations in their editor as the primary mechanism of inspecting compiler errors, and around 33% of them consider waiting for these annotations to be a big blocker." and "In the open answers, we also received many reports of Rust Analyzer's performance and memory usage being a limiting factor."
- Since: The same post: "the distributed builds of the editor are now optimized with PGO, which provided 15-20% performance wins".
- Kurz: answered: the owner's words, read as a local language server that is the incremental compiler of "Recompute what changed", recorded as assumed in section 2 of the record. Before the answers: OPEN.

## 7. One async runtime per library, and blocking calls that stall it

- Evidence: the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "The async Rust support in Rust itself is very basic, consisting only of the core Future mechanism." and "Furthermore, the async ecosystem is fractured. Choosing one library may entail choosing a specific runtime."; Tokio's documentation of tasks (https://docs.rs/tokio/latest/tokio/task/index.html, 1.54.1): "A blocking operation performed in a task running on a thread that is also running other tasks would block the entire thread, preventing other tasks from running."
- Kurz: answered: "Readiness I/O, as Tokio", section 12 of the record. Before the answers: OPEN.

## 8. `Send` bounds spread through async code

- Evidence: the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Things have to be Send all the time, some things can't live across an await"; Stabilizing async fn in traits in 2023 (https://blog.rust-lang.org/inside-rust/2023/05/03/stabilizing-async-fn-in-trait/): "Send bounds are verbose, especially for traits with lots of methods".
- Since: Announcing `async fn` and return-position `impl Trait` in traits (https://blog.rust-lang.org/2023/12/21/async-fn-rpit-in-traits/) points to the `trait_variant` crate: "This creates two versions of your trait: LocalHttpService for single-threaded executors and HttpService for multithreaded work-stealing executors."
- Kurz: answered: "Inferred limit, as Send", section 6 of the record. Before the answers: OPEN.

## 9. Error handling needs two crates and a choice in every crate

- Evidence: the charter of the Rust Error Handling Project Group (https://github.com/rust-lang/project-error-handling/blob/master/CHARTER.md): "Use enums for types representing multiple failure cases that may need to be handled." and "Convert to a reporting type when the error is no longer expected to be handled beyond reporting e.g. `anyhow::Error` or `eyre::Report` or when trait object + downcast error handling is preferable."
- Kurz: answered: "Named unions", section 5 of the record. Before the answers: OPEN.

## 10. A small standard library, crates for everything, and the supply-chain risk

- Evidence: the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "basic helpers and utility crates are hard to find, and there are many choices, often with subtle differences between them"; Rust compiler performance survey 2025 results (https://blog.rust-lang.org/2025/09/10/rust-compiler-performance-survey-2025-results/): "It seems that the most popular (and effective) mechanisms for improving build performance are reducing the number of dependencies and their activated features, and splitting larger crates into smaller crates."; crates.io: Malicious crates faster_log and async_println (https://blog.rust-lang.org/2025/09/24/crates.io-malicious-crates-fasterlog-and-asyncprintln/): "faster_log - Published on May 25th, 2025, downloaded 7181 times" and "The malicious code was executed at runtime, when running or testing a project depending on them."; Procedural macros, the Rust Reference (https://doc.rust-lang.org/reference/procedural-macros.html): "Because of this, procedural macros have the same security concerns that Cargo's build scripts have."
- Since: crates.io: an update to the malicious crate notification policy (https://blog.rust-lang.org/2026/02/13/crates.io-malicious-crate-update/): "In all cases, the crates were deleted, the user accounts that published them were immediately disabled, and reports were made to upstream providers as appropriate."
- Kurz: answered: "Every package on path", section 13 of the record. Before the answers: OPEN.

## 11. The orphan rule blocks glue between two crates

- Evidence: Rust project goals, "Experiment with relaxing the Orphan Rule" (https://github.com/rust-lang/rust-project-goals/blob/main/src/2024h2/Relaxing-the-Orphan-Rule.md): "it prevents providing a third-party library that integrates two other libraries with each other, and instead requires convincing the author of one of the two libraries to add (optional) support for the other, or requires using a newtype wrapper."; the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Some of the problems are due to the design of Rust itself. The coherence rules in particular."
- Since: The goal's page gives its status as "Not accepted".
- Kurz: answered: "Any package + check" and "Rust's impl block", section 4 of the record: any package declares the glue with an `impl` block, and the build refuses two declarations of one pair. Before the answers: REPEATED.

## 12. `Pin` and self-referential structs

- Evidence: the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Working with Pin is really hard, but necessary in various scenarios".
- Kurz: NOT APPLICABLE. Kurz has neither borrowed references inside values ("No lifetimes, no borrow annotations", section 3) nor futures (section 6), so no value can point into itself; a part of a list is a copy, "`xs[a..<b]` and `xs[a..b]` are a new list of the items" (section 4).

## 13. Cancellation can cut a function at any await

- Evidence: the async status quo (https://rust-lang.github.io/wg-async/vision/submitted_stories/status_quo.html): "Cancellation can in principle occur at any point in time, which leads to subtle bugs".
- Kurz: answered: "Deadline on chain ID", "Run it, drop the reply", "One call id, callee drops" and "Run again, table restarts", section 6 of the record. Before the answers: OPEN.

## 14. A library's panic takes down the caller

- Evidence: Cloudflare outage on November 18, 2025 (https://blog.cloudflare.com/18-november-2025-outage/), a widely cited postmortem (last resort): "When the bad file with more than 200 features was propagated to our servers, this limit was hit — resulting in the system panicking." and its log line "thread fl2_worker_thread panicked: called Result::unwrap() on an Err value".
- Kurz: answered: "Compiler lists them", section 5 of the record. Before the answers: OPEN.

## 15. Too many string types

- Evidence: Storing UTF-8 Encoded Text with Strings, the Rust book (https://doc.rust-lang.org/book/ch08-02-strings.html): "New Rustaceans commonly get stuck on strings for a combination of three reasons: Rust's propensity for exposing possible errors, strings being a more complicated data structure than many programmers give them credit for, and UTF-8."
- Kurz: answered: "Edge cases only", section 12 of the record. Before the answers: OPEN.

## 16. Binaries are large

- Evidence: the Rust FAQ, archived in the project's repository (https://github.com/rust-lang/prev.rust-lang.org/blob/master/en-US/faq.md): "There are several factors that contribute to Rust programs having, by default, larger binary sizes than functionally-equivalent C programs." and "Rust programs compile with some debug symbols retained, even when compiling in release mode."
- Kurz: answered: "Plus an abort build", section 2 of the record. Before the answers: REPEATED.

## 17. Monomorphization bloats code

- Evidence: the Rust FAQ, archived in the project's repository (https://github.com/rust-lang/prev.rust-lang.org/blob/master/en-US/faq.md): "It results in fast code that is specialized for every call-site and statically dispatched, with the tradeoff that functions instantiated with many different types can cause "code bloat", where multiple function instances result in larger binaries than would be created with other translation strategies."
- Kurz: answered: "Copy per instance", section 4 of the record. Before the answers: REPEATED.

## 18. Integer overflow differs between debug and release builds

- Evidence: Data Types, the Rust book (https://doc.rust-lang.org/book/ch03-02-data-types.html): "When you're compiling in release mode with the --release flag, Rust does not include checks for integer overflow that cause panics."; "Thought: switch the default on overflow checking and provide RFC 560's scoped attribute for checked arithmetic" (https://internals.rust-lang.org/t/thought-switch-the-default-on-overflow-checking-and-provide-rfc-560s-scoped-attribute-for-checked-arithmetic/15118), a forum thread (last resort): "First, I find the current behavior confusing and easily misunderstood as making wrapping an acceptable default."
- Kurz: answered: "Project setting", section 4 of the record. Before the answers: REPEATED.

## 19. Graphs and back-pointers are hard in safe Rust

- Evidence: Learning Rust With Entirely Too Many Linked Lists (https://rust-unofficial.github.io/too-many-lists/), a widely cited tutorial (last resort): "Yes, linked lists are so truly awful that you deal with all of these concepts in making them real."
- Kurz: answered: "Arena with generations", section 3 of the record. Before the answers: REPEATED.

## 20. Macros are hard to debug and run unchecked code in the build

- Evidence: Rust compiler performance survey 2025 results (https://blog.rust-lang.org/2025/09/10/rust-compiler-performance-survey-2025-results/): "For example, expansion of derive proc macros is not currently cached, although work is underway to change that."; Procedural macros, the Rust Reference (https://doc.rust-lang.org/reference/procedural-macros.html): "Procedural macros run during compilation, and thus have the same resources that the compiler has." and "Endless loops are not caught by the compiler which hangs the compiler."
- Kurz: AVOIDED. Section 9 sets the rules of a keyword: "The pattern is data the editor reads without running code." (rule 1), "The output is plain Kurz and viewable. Errors point at the developer's source, not at the expansion." (rule 4), and "Keyword code cannot read files, network or clock." (rule 5).

## 21. Trait bounds sprawl through generic code

- Evidence: 2089-implied-bounds, the Rust RFC Book (https://rust-lang.github.io/rfcs/2089-implied-bounds.html): "The ToOwned bound has then to be carried everywhere".
- Since: The RFC was accepted; its tracking issue (https://github.com/rust-lang/rust/issues/44491) is open on 2026-10-30.
- Kurz: REPEATED: a type parameter takes interface limits as Rust takes trait bounds: "A type parameter is limited to the types that implement an interface by naming the interface after a colon" (T19, section 4), and a generic function sends a value of a type parameter only where the parameter carries a limit inferred for every type, as Rust's `Send` ("Inferred limit, as Send", section 6), so such limits reach the signatures of the callers as Rust's bounds do.

## 22. What unsafe code may do is not fully written down

- Evidence: Behavior considered undefined, the Rust Reference (https://doc.rust-lang.org/reference/behavior-considered-undefined.html): "There is no formal model of Rust's semantics for what is and is not allowed in unsafe code, so there may be more behavior considered unsafe."
- Kurz: answered: "Closed list + checks", section 3 of the record. Before the answers: OPEN.

## 23. Global state needs crates or `unsafe`

- Evidence: Disallow references to static mut, the Rust Edition Guide (https://doc.rust-lang.org/edition-guide/rust-2024/static-mut-references.html): "upholding mutability XOR aliasing for a static mut requires reasoning about your code globally, which can be particularly difficult in the face of reentrancy and/or multithreading."; Announcing Rust 1.80.0 (https://blog.rust-lang.org/2024/07/25/Rust-1.80.0/): "This completes the stabilization of functionality adopted into the standard library from the popular lazy_static and once_cell crates."
- Kurz: answered: "At first read", section 4 of the record. Before the answers: OPEN.

## 24. No stable ABI for Rust code

- Evidence: the Rust FAQ, archived in the project's repository (https://github.com/rust-lang/prev.rust-lang.org/blob/master/en-US/faq.md): "Committing to an ABI is a big decision that can limit potentially advantageous language changes in the future."; the pull request "Experimental feature gate proposal `crabi`" (https://github.com/rust-lang/rust/pull/105586): developers "often have to use the C ABI as a lowest-common-denominator for cross-language function calls".
- Kurz: answered: "None", section 13 of the record. Before the answers: REPEATED.

## 25. Mutex poisoning

- Evidence: Launching the Lock Poisoning Survey (https://blog.rust-lang.org/2020/12/11/lock-poisoning-survey/): "One of those API changes we're looking at is non-poisoning implementations of Mutex and RwLock."
- Since: The tracking issue for `std::sync::nonpoison` (https://github.com/rust-lang/rust/issues/134645) is open on 2026-10-30.
- Kurz: AVOIDED. Safe Kurz has no lock: "An actor handles one message at a time, so there are no locks and no data races inside it." (section 6), and a crash discards what a half-done change left: "Default on a child crash: restart it with fresh state." (section 6).
