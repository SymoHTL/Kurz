---
name: what-developers-dislike-in-elixir
description: Research of 2026-10-30 - what developers dislike about Elixir, each finding with its source, title, URL and quoted sentence, and where Kurz stands after the owner's answers of that day; read it against a design change (playbook node B4)
metadata:
  type: reference
---

# What developers dislike about Elixir, and where Kurz stands

Sources read on 2026-10-30. Each verdict was read against the design record (`kurz-design.md`) and the
reference that day, and is written as it stands after the owner's answers of the same day.

How to read the evidence: survey figures come from the community survey State of Elixir 2025
(1018 participants) and from the Stack Overflow Developer Survey 2025; where a figure stands in a table
or a chart and in no sentence, the quote is the table's text as the page prints it, and says so. Every
other source is the project's own record (elixir-lang.org, hexdocs, erlang.org), except one research
paper, marked so, and the Dashbit post that announced Nx (finding 6). Verdicts on actors, distribution and keywords rest on the design record alone: the reference
leaves them out, and the compiler builds no actor.

Summary: 24 findings: 5 avoided, 1 not applicable, 2 open, 1 repeated for some sends and answered for the rest, 4 repeated in part (the hiring pool, a call whose caller crashed, a lasting fault, a large expansion); 11 answered and recorded.

Verdicts: AVOIDED, REPEATED (Kurz has the problem too), OPEN, NOT APPLICABLE; an answered finding names
the owner's choice and the section of the record whose trace holds each option with its cost.
Each finding keeps its sources; the prose, the code samples and the options are cut.
Before a design change, name the finding it meets (knowledge/diagram-design-change-playbook.md, B4).

## 1. A small ecosystem and a small hiring pool

- Evidence: State of Elixir 2025 (https://elixir-hub.com/surveys/2025), question 39, "If your company doesn't use Elixir – what are the main reasons?", 211 answers, as the page's table reads: "Lack of Elixir expertise in team 118 resp. 55.9%" and "Hiring concerns 66 resp. 31.3%"; its summary of question 41: "teams should still expect a narrower talent pool than for mainstream stacks". Question 44, 447 answers: "Adoption, Marketing & Jobs 89 resp. 19.9%" and "Libraries & Ecosystem Maintenance 64 resp. 14.3%", with the summary "There are frequent mentions of 'abandonware' and a lack of mature integrations for standard enterprise services." Stack Overflow Developer Survey 2025 (https://survey.stackoverflow.co/2025/technology/): the chart of languages used, all respondents, labels Elixir "2.7%" (847 respondents in the page's data), while its text reads "... the most admired programming language (72%), followed by Gleam (70%), Elixir (66%) and Zig (64%)."
- Kurz: answered for the libraries: "C-ABI + own + bindgen", section 12 of the record. REPEATED in part for the hiring pool: a new language starts with none, and section 1 takes the syntax "in the spirit of C#". Before the answers: REPEATED.

## 2. Editor tooling: an unstable language server, and three of them

- Evidence: State of Elixir 2025, question 45, 244 answers, the top row of the page's table: "Better LSP, IDE Support & Debuggers 58 resp. 23.8%"; question 44: "Tooling, LSP & IDE Experience 82 resp. 18.3%", and its summary: "A significant number of comments cite instability with the Language Server (LSP), slow compilation times, and a lack of advanced debugging tools compared to other ecosystems." Announcing the official Elixir Language Server team (https://elixir-lang.org/blog/2024/08/15/welcome-elixir-language-server-team/): the community "had, for some time, three distinct language server implementations, each with their own strengths."
- Since: the same post says the maintainers agreed "to move forward with a single Language Server Protocol project", and its update reads "the new language server is called Expert and is currently available in alpha as open source." The release post of Elixir v1.18 (https://elixir-lang.org/blog/2024/12/19/elixir-v1-18-0-released/) names "LSP listeners" in its title.
- Kurz: OPEN: the owner's words are read as a local language server that is the incremental compiler of "Recompute what changed", a reading recorded as assumed in section 2 of the record and not an answer.

## 3. Slow compilation, and one change recompiles many files

- Evidence: Macro-related anti-patterns (https://elixir.hexdocs.pm/macro-anti-patterns.html): "These dependencies may lead to dependency graphs where changing a single file causes several files to be recompiled." State of Elixir 2025, question 44, as quoted in finding 2: comments cite "slow compilation times".
- Since: Elixir v1.19 released (https://elixir-lang.org/blog/2025/10/16/elixir-v1-19-0-released/): "This release includes two compiler improvements that can lead up to 4x faster builds in large codebases."
- Kurz: answered: "Recompute what changed", section 2 of the record. Before the answers: OPEN.

## 4. Dynamic typing: a wrong shape of data fails at run time

- Evidence: State of Elixir 2025, question 26, "What do you expect from the Elixir type system?", 982 answers, the top row of the page's table: "Improvement over existing typing (Dialyzer, etc.) 774 resp. 78.8%"; the summary of question 44: "Users also view the upcoming Type System as a critical necessity for maintaining larger codebases."
- Since: "Elixir v1.20 released: now a gradually typed language", the post of 2026-06-03 (https://elixir-lang.org/blog/2026/06/03/elixir-v1-20-0-released/): the compiler can "perform type inference and gradually type check every Elixir program, without introducing type annotations", and reports "typing violations that are guaranteed to fail at runtime if executed". Type signatures that a developer writes are not part of v1.20.
- Kurz: AVOIDED. T1 (decided, §4): "Every variable has one type, fixed when it is declared." Section 6: "Calls on an actor look like ordinary method calls. The compiler turns each into a message.", so a message is checked like a call, and section 7's "code versions never mix inside one cluster" keeps both ends of a remote call on one set of types.

## 5. Few debugging tools, and a live distributed system is hard to inspect

- Evidence: State of Elixir 2025, question 44 summary: "a lack of advanced debugging tools compared to other ecosystems"; question 45: "Better LSP, IDE Support & Debuggers 58 resp. 23.8%"; question 26: "Improved debugging 496 resp. 50.5%".
- Kurz: answered: "Read-only inspector", section 7 of the record. Before the answers: OPEN.

## 6. Slow at CPU-bound work

- Evidence: Erlang FAQ (https://www.erlang.org/faq/introduction.html): "The most common class of 'less suitable' problems is characterised by performance being a prime requirement and constant-factors having a large effect on performance. Typical examples are image processing, signal processing, sorting large volumes of data and low-level protocol termination." State of Elixir 2025, question 9, the page's table rows for ratings 1 to 5: "Performance 0.8% 4.8% 30.8% 38.8% 24.8%" against "Concurrency 0.1% 0.0% 2.4% 11.9% 85.5%"; question 8, reasons to choose Elixir: "Performance 292 resp. 29.7%" against "Concurrency 719 resp. 73.2%".
- Since: Nx (https://dashbit.co/blog/nx-numerical-elixir-is-now-publicly-available): "Nx is a multi-dimensional tensors library for Elixir with multi-staged compilation to the CPU/GPU." The same post names the cost it works around: "One of the main concerns is that immutability can be expensive when working with large blobs of memory."
- Kurz: AVOIDED. Section 1: "Compiled to native code, as fast as C, optimized for each target platform." Section 2: the compiler emits LLVM IR, and compiler/README.md shows the emitter built.

## 7. A GenServer turns into a bottleneck that serialises every call

- Evidence: Process-related anti-patterns (https://elixir.hexdocs.pm/process-anti-patterns.html): "When you use a process for code organization, it can create bottlenecks in the system." GenServer (https://elixir.hexdocs.pm/GenServer.html): "Use processes only to model runtime properties, such as mutable state, concurrency and failures, never for code organization."
- Kurz: answered: "Functions + snapshots", section 11 of the record. Before the answers: REPEATED.

## 8. Mailboxes have no bound and no backpressure

- Evidence: Announcing GenStage (https://elixir-lang.org/blog/2016/07/14/announcing-genstage/): "how do we prevent a process from receiving too many messages?" and "We need a back-pressure mechanism that allows the receiving process to specify how much it can handle".
- Since: GenStage and Broadway, where "the communication between stages is demand-driven" (same post); State of Elixir 2025, question 34: "Broadway / GenStage / Flow 239 resp. 29.1%".
- Kurz: answered: "Raise + spill cap", section 13 of the record. Before the answers: OPEN.

## 9. Call timeouts: a fixed default, a crashed caller, and work nobody waits for

- Evidence: GenServer (https://elixir.hexdocs.pm/GenServer.html), on `call/3`: "The default value is 5000. If no reply is received within the specified time, the function call fails and the caller exits." The same paragraph describes the server in that case as "just late with the reply".
- Since: the gen_server documentation of Erlang/OTP (https://www.erlang.org/doc/apps/stdlib/gen_server.html) says that since OTP 24 `gen_server:call` uses process aliases, so a late reply no longer reaches the caller; the server still does the work.
- Kurz: answered for a caller that waits: "Deadline on chain ID", section 6 of the record: an inbox drops a message whose deadline passed. REPEATED in part for a call whose caller crashed: it runs to its end and its reply is dropped ("Run it, drop the reply", section 6), the work nobody waits for that this finding names, which the owner took at that cost, except a call still waiting in the inbox of a callee the crash ends, an actor the caller spawned by hand, which stops after the message it runs and does not run that call ("After current message", section 6). Before the answers: REPEATED.

## 10. Let it crash: a lasting fault restarts in a loop, and the limit takes the tree down

- Evidence: Supervisor (https://elixir.hexdocs.pm/Supervisor.html): "`:max_restarts` - the maximum number of restarts allowed in a time frame. Defaults to `3`." and "`:max_seconds` - the time frame in which `:max_restarts` applies. Defaults to `5`." Supervisor Behaviour, OTP design principles (https://www.erlang.org/doc/system/sup_princ.html): "If more than `MaxR` number of restarts occur in the last `MaxT` seconds, the supervisor terminates all the child processes and then itself." Of the supervisor above it, the page says: "It either restarts the terminated supervisor or terminates itself."
- Kurz: answered for the tree: "Growing pause" and "No limit, as Akka", section 6 of the record: a lasting fault takes no tree down, and a limit holds where `max N in` is written; a `retry` of a message that crashes its callee every time restarts that callee up to the `retry`'s count, a cost the owner took ("Keep, name the costs", section 6). REPEATED in part: a lasting fault restarts about every 30 s without end. Before the answers: REPEATED.

## 11. Hot code upgrades are too complex to use

- Evidence: Mix release (https://mix.hexdocs.pm/Mix.Tasks.Release.html): "The reason we don't provide hot code upgrades is because they are very complicated to perform in practice, as they require careful coding of your processes and applications as well as extensive testing."
- Since: releases ship without them, and the same page points to "other techniques that are language agnostic to upgrade their systems, such as Blue/Green deployments".
- Kurz: answered: "Release matches waits", section 7 of the record; how the `upgrade` lines of a durable actor meet its log stands open (section 14). Before the answers: OPEN.

## 12. Configuration read at compile time instead of at run time

- Evidence: Mix release (https://mix.hexdocs.pm/Mix.Tasks.Release.html), on build-time configuration: "it is evaluated whenever you compile your code or whenever you assemble the release." Application (https://elixir.hexdocs.pm/Application.html): "In any case, compile-time environments should be avoided." and "Whenever possible, reading the application environment at runtime should be the first choice."
- Since: `config/runtime.exs` runs at start, and `Application.compile_env` makes a compile-time read explicit; the Application page says Mix tools "compare the compilation values with the runtime values whenever your system starts", "raising an error in case they differ".
- Kurz: answered: "Typed settings data", section 12 of the record; a program with settings does not build for Windows ("Not supported, as files", section 12). Before the answers: OPEN.

## 13. Macros: code nobody sees, large expansions, compile-time dependencies

- Evidence: Macro-related anti-patterns (https://elixir.hexdocs.pm/macro-anti-patterns.html): "The use/1 directive allows a module to inject any type of code into another, including propagating dependencies."; "When a macro generates a large amount of code, it impacts how the compiler and/or the runtime work."; and "their indiscriminate use can compromise the evolution of a system, reducing its maintainability."
- Kurz: answered for code nobody sees: "kurz expand + editor", section 9 of the record; a change that recompiles what depends on it meets "Recompute what changed", section 2. REPEATED in part: a large expansion has no bound, an answer taken against a compile error past a size the project sets (section 9). Before the answers: OPEN.

## 14. Strings, charlists and lists of integers

- Evidence: Binaries, strings, and charlists (https://elixir.hexdocs.pm/binaries-strings-and-charlists.html): "A charlist is a list of integers where all the integers are valid code points." Charlists "are often used for interoperability with Erlang".
- Since: a charlist prints with the `~c` sigil, which, as the same page says, "indicates the fact that we are dealing with a charlist and not a regular string."
- Kurz: AVOIDED. T15 (decided, §4): "A string is UTF-8 and immutable." Bytes are `Bytes`, a second name of `List<byte>` (section 4), `char` is a type of its own (T21, decided), and A4 (decided, §8): "A list shows its items between `[` and `]`, each as its own text", so a list of numbers never prints as text.

## 15. Cluster security: one shared cookie, and clear text

- Evidence: Distributed Erlang (https://www.erlang.org/doc/system/distributed.html): "Furthermore, the communication between nodes is by default in clear text." Its warning: starting a distributed node without `-proto_dist inet_tls` exposes it "to attacks that may give the attacker complete access to the node and by extension the cluster."
- Since: TLS distribution exists as an option (https://www.erlang.org/doc/apps/ssl/ssl_distribution.html); the warning above stands in the documentation read on 2026-10-30, so the default had not changed then.
- Kurz: answered: "Mutual TLS default", section 7 of the record. Before the answers: OPEN.

## 16. A full mesh and a global name registry limit how far a cluster grows

- Evidence: Distributed Erlang: "This way, there is always a fully connected network." Evaluating Scalable Distributed Erlang for Scalability and Reliability, IEEE Transactions on Parallel and Distributed Systems, vol. 28, no. 8, 2017 (a research paper, not the project's own record; https://eprints.bournemouth.ac.uk/30277/7/07820204.pdf): "First, maintaining a fully connected mesh of Erlang nodes means that a system with n nodes must maintain O(n²) active TCP/IP connections and this induces significant network traffic above 40 nodes. Second, maintaining a global process namespace incurs high synchronisation and communication costs". State of Elixir 2025, question 33: "over 80% of production systems run on just 1 to 5 nodes."
- Since: hidden nodes, whose "Connections between hidden nodes and other nodes are not transitive" (Distributed Erlang), keep a node out of the mesh; discovery and distributed registries come from libraries (question 34 of the survey lists libcluster, Horde and Syn).
- Kurz: answered: "Consistent hash, Orleans", section 7 of the record. Before the answers: OPEN.

## 17. Net splits are left to the application

- Evidence: global (https://www.erlang.org/doc/apps/kernel/global.html): "A network of overlapping partitions might cause the internal state of `global` to become inconsistent." and, of its resolver `random_exit_name/3`: "The function randomly selects one of the pids for registration and kills the other one." Mnesia system information (https://www.erlang.org/doc/apps/mnesia/mnesia_chap7.html): "Determining what data to keep after a communication failure is outside the scope of Mnesia." State of Elixir 2025, question 34: "Distributed state (Mnesia / ETS across nodes) 83 resp. 10.1%".
- Since: since OTP 25, `global` by default cuts overlapping partitions into fully connected ones (same page). The Mnesia page suggests determining "which 'island' contains most of the nodes" and "Using option {majority,true} for critical tables"; the repair of data stays with the application.
- Kurz: answered: "Raft membership", "Phi accrual + majority, as Akka" and "Raft: neither side wins", section 7 of the record, and a durable `send` whose second copy is across a split is refused ("Keep 2, name the cost", section 13). Open there (section 14): whether the policy that runs an item on all sides with a `merge` block still has a side to run on, and what a durable `send` refused during a split gives its sender. Before the answers: OPEN.

## 18. Messages between nodes can be lost

- Evidence: Processes, Erlang reference manual (https://www.erlang.org/doc/system/ref_man_processes.html): "When communicating over the distribution, signals can be lost if the distribution channel goes down." Of order it guarantees one thing: "if an entity sends multiple signals to the same destination entity, the order is preserved;"
- Since: no change; teams add acknowledgements or an external queue (State of Elixir 2025, question 34: "Message queues / streams (RabbitMQ, Kafka, AWS SQS/SNS, Google Pub/Sub, NATS) 257 resp. 31.3%").
- Kurz: REPEATED for a `send` to an actor that is not durable on another machine, delivered one time or not at all, as Erlang delivers it (section 7, assumed there); answered for a durable actor: "Durable: at least once", section 7 of the record. Before the answers: OPEN.

## 19. Selective receive scans the whole mailbox

- Evidence: Processes, Erlang efficiency guide (https://www.erlang.org/doc/system/eff_guide_processes.html), of that pattern: "the entire message queue must be searched" and "This is very expensive for processes with long message queues".
- Kurz: answered: "One-shot reply slot", section 6 of the record. Before the answers: OPEN.

## 20. Blocking native code stalls the threads that run processes

- Evidence: erl_nif (https://www.erlang.org/doc/apps/erts/erl_nif.html): "It is difficult to give an exact maximum amount of time that a native function is allowed to work, but usually a well-behaving native function is to return to its caller within 1 millisecond." and "A native function doing lengthy work before returning degrades responsiveness of the VM, and can cause miscellaneous strange behaviors."
- Kurz: OPEN: "Readiness I/O, as Tokio", section 12 of the record, covers the calls on files and sockets; a `raw` call of a C function that blocks holds its thread and the actors that thread runs, which stands open (section 14). Before the answers: OPEN.

## 21. Large process state and message copies cost CPU

- Evidence: Processes, Erlang efficiency guide: "All data in messages sent between Erlang processes is copied, except for refc binaries and literals on the same Erlang node." Erlang garbage collector (https://www.erlang.org/doc/apps/erts/garbagecollection.html): "the garbage collection algorithm used is proportional to the amount of live data on the heap."
- Since: BEAM collects by generation, so most collections skip the old heap (same page); the anti-pattern "Sending unnecessary data" (process anti-patterns page) warns against large messages.
- Kurz: answered: "After a threshold", section 13 of the record; a message is not copied whole: "a small value is copied into the receiver's heap, a big one is shared by pointer" (section 6). Before the answers: OPEN.

## 22. Off-heap binaries kept alive by processes that rarely collect

- Evidence: Erlang garbage collector: "This approach does not catch all problems with binary memory not being released soon enough, but it does catch a lot of them." binary (https://www.erlang.org/doc/apps/stdlib/binary.html): "Using copy/1 on a binary that references a larger binary can potentially free up the larger binary for garbage collection."
- Kurz: AVOIDED for a big value a count holds. Section 6 shares big values as Erlang does ("Erlang does the same for binaries above 64 bytes"), but a count drops when a holder ends, with no collection in between: section 4 (the owner, 2026-10-24, "Full counting now") lowers it "at the end of its scope, at an assignment over it and where the value that holds it ends". The actor's sweep of section 13, which runs after a threshold ("Live size x2, as Go"), is for what no count frees.

## 23. The atom table has a fixed limit, and atoms are never freed

- Evidence: System limits (https://www.erlang.org/doc/system/system_limits.html): "By default, the maximum number of atoms is 1,048,576." String (https://elixir.hexdocs.pm/String.html), on `to_atom/1`: "Warning: this function creates atoms dynamically and atoms are not garbage-collected. Therefore, string should not be an untrusted value, such as input received from a socket or during a web request."
- Kurz: NOT APPLICABLE. Kurz has no atom type: enum values are declared in source, and strings are counted values freed at their last count (section 4).

## 24. Two GenServers that call each other deadlock

- Evidence: gen_server (https://www.erlang.org/doc/apps/stdlib/gen_server.html), among the exit reasons of `call`: "calling_self - A call to self/0 would hang indefinitely." No reason in that list covers a cycle through a second process; such a cycle ends in `timeout`.
- Kurz: AVOIDED. Section 6, Deadlocks, decides three layers: a call of the waiting chain runs ("`A.Save` waits on `B.Check`, which calls `A.Load`: this runs like a nested method call."), a "Compile error for loops of waiting calls between actor types", and "Instant runtime detection" that raises in one actor "naming both chains".
