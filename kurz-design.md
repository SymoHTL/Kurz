# Kurz language design

Status: brainstorm record, 2026-10-01. Everything under "Decided" was chosen by Simon during the session. Anything marked *assumed* was proposed and not objected to, but never explicitly confirmed. Keyword spellings in code samples are illustrative unless listed in the syntax section.

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

- The compiler is written in C# first and rewritten in Kurz once Kurz can carry it.
- The compiler emits LLVM IR directly. Going through C source is not wanted. LLVM as a build-time dependency is acceptable.
- A compiled program never relies on another language, runtime or tooling. The whole standard library is written in Kurz.
- Kurz can call C-ABI functions (graphics, audio, operating system), but nothing in the standard library needs this beyond the operating system boundary.
- TLS follows the Rust approach: the protocol is implemented in Kurz, the cipher primitives come from an established library through the C-ABI at first.
- Compilation is whole-program. The cycle rule (section 3), deadlock analysis (section 6), `mock` (section 11) and package permissions (section 13) depend on the compiler seeing all source at once.
- A package system is decided later.

## 3. Memory

- No lifetimes, no borrow annotations, no global garbage collector.
- Immutable by default. Immutable data cannot form reference cycles, so compiler-inserted reference counting is enough, and most counting is optimized away at compile time.
- Mutable state lives inside exactly one actor. Each actor has its own heap. When an actor dies its whole heap is freed at once.
- There is **no cycle collector**. A possible reference cycle is a compile error. The developer marks the back-pointer `weak`.
  - `weak` means "I point at this but do not keep it alive". A weak reference is nullable and becomes null when its target is freed. It is the same idea as `WeakReference<T>` in C#.
  - Only `mut` fields can close a cycle, so with immutable-by-default the error is rare.
  - A `mut` field of interface type could hold any implementer; the compiler needs the whole program to judge it.
  - General graphs use the pattern "one owner holds all nodes, edges are `weak` or indices".

```
class Node {
    mut List<Node> children
    mut weak Node? parent
}
```

## 4. Types and paradigm

- Static typing with inference.
- Immutable by default; `mut` marks what may change.
- `data`: immutable records, equal by content. A `data` type may inherit from another `data` type. Unions (`Circle | Square`) are available as well.
- `class`: may have `mut` fields, single inheritance plus interfaces as in C#. Classes are the tool for big inheritance trees. A class with `mut` fields compares by identity; an immutable class compares by content. Equality is overridable, as in C#.
- `actor`: the concurrent unit (section 6). Actors implement interfaces but do not inherit from each other.
- Small `data` values are compiled as plain values without reference counts, automatically; there is no separate `struct` keyword. *(assumed)*
- Numbers use C# names and sizes: `byte`, `short`, `int` (32-bit), `long` (64-bit), `float`, `double`, `decimal`.
- Integer overflow wraps silently in release builds. Around that:
  - Compile errors for constant expressions that overflow, implicit narrowing (`long` into `int`) and mixing signed with unsigned.
  - Durations and timestamps are their own 64-bit types, never raw integers. This removes the bug class where an uptime counter overflows after weeks of running.
  - Test builds throw on overflow instead of wrapping. An explicit wrapping operator exists for intended cases such as hashes.
  - Tests can fast-forward virtual time (section 11).
- Strings are UTF-8 and immutable; there is no flag to change the encoding. Indexing goes through `.Bytes` or `.Chars`. Conversion happens at the edges (for example `text.ToUtf16()` for Windows APIs).
- Array index out of range and division by zero raise exceptions. *(assumed)*
- Null exists and is enforced: `T?` is short for `T | null`. Using a nullable value without a check is a compile error. `?.` and `??` work as in C#.
- Generics as in C# (`Map<int, User>`), lambdas as in C# (`x => x * 2`), primary constructors (`class User(string Name, mut int Age)`).

## 5. Outcomes and errors

The language enforces the C# `OneOf<>` pattern: every method declares every outcome that can happen under normal use.

- The return type is a union. The **first case is the success case**.
- **Unwrap rule:** calling such a method yields the success value directly. Every other case leaves the calling method and goes to its caller.
- **Honest signatures:** a propagated case must be listed in the calling method's own signature. Otherwise it is a compile error.
- To keep all cases instead of propagating, either `match` the call directly or give the variable an explicit union type.
- A postfix `else` block handles selected cases; unlisted ones still propagate. An arm can recover with a value, transform and return another case, or `throw`.
- **Exceptions** are only for situations the called method truly cannot recover from. There is no `catch`. An exception kills the actor it happens in (section 6).

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
user = store.Get(id) else { NotFound n => return Invalid("no user {id}") } // transform
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
- A call waits for its result by default. A keyword makes a call fire-and-forget. *(This is the reading of "the Elixir way" that was stated back to Simon; keyword name open.)*
- What may cross between actors: immutable `data`, immutable class instances and actor references. A mutable class instance crosses only with an explicit word at the call: `copy` (the receiver gets a deep copy) or `move` (the sender's variable is dead afterwards, nothing is copied). Two actors can never reach the same mutable object.
- Identity does not survive crossing: what arrives is a different object. Immutable classes compare by content, so this is invisible for them.

```
store.Add(copy order)
store.Add(move order)
```

### Scheduling

Switching between actors is cooperative by default: an actor gives up its core at actor calls and I/O. A runaway loop then costs one core, not the system, because other cores take over the waiting actors.

- A keyword on an actor opts into stronger fairness. Both forms are keywords: compiler-inserted checks at loop ends and function entry, and timer-based preemption. Spelling is open (`preempt actor ReportBuilder { ... }` as a sketch).
- The compiler warns about a loop inside an actor that has no switch point and no provable end.

### Crashes and supervision

- An exception kills only the actor it happens in.
- The supervisor is whoever spawned the actor; no extra code. Explicit supervisor trees exist only for other policies.
- Default on a child crash: restart it with fresh state.
- Restart limit as in Erlang: too many crashes in a short window crash the parent. The default is overridable.
- Top-level code is the root actor. If it crashes, the process exits with an error code.
- Calling a dead or unreachable actor raises an exception in the caller.
- Every call has a timeout (5 seconds by default, as in Elixir), then an exception in the caller.

### Deadlocks

Three layers, so that a hang cannot happen silently.

1. **Callbacks work.** Every call carries a chain ID. An actor waiting inside a chain lets in calls that belong to the same chain. `A.Save` waits on `B.Check`, which calls `A.Load`: this runs like a nested method call. Unrelated messages still wait.
2. **Compile error for loops of waiting calls between actor types**, analysed per method. A one-word override on the call means "different instance, I vouch". Exempt without override: a callback through a reference the caller passed in that same call.
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
- **Upgrades:** code versions never mix inside one cluster. A new version forms a new cluster and traffic switches over. State moves only where an actor says so with `upgrade`:
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

- Variables: `x = 5` declares an immutable variable, `mut x = 5` a mutable one. The type may be written: `int x = 5`. An unused variable is a compile error, which also catches typos that would otherwise declare a new variable.
- No semicolons. A newline ends a statement.
- No parentheses around conditions; braces are always required: `if x > 5 { ... }`.
- Signatures in C# order, type first: `User | NotFound Get(int id) => ...`.
- Everything is private by default: members are private to their type, top-level types and functions are private to their folder. `pub` exposes; `prot` exposes to inheriting types.
- A folder is a namespace; there is no `namespace` line. Files in one folder see each other without imports. `use` brings in other folders and packages.
- The project file is written in Kurz itself (`project.kz`), not in a separate format.
- String interpolation is always on: `"hello {name}"`.
- Primary constructors, C# lambdas, C# generics.
- Top-level statements instead of `Main`.

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

- There is no container. A singleton is a named actor; wiring is constructor parameters.
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
- **Runtime:** actors, supervisors, scheduler, timers, clustering, serialization.
- **System:** files, processes, environment, TCP/UDP, DNS.
- **Backend:** HTTP server and client, TLS, logging, the chore keywords.
- **Data:** database driver and the query keyword. MySQL first; the wire driver is built last.
- **Tooling:** test runner, formatter, editor language server. Package manager later.
- **Not in the box:** graphics, audio, windowing. These are packages over the C-ABI.

Database:

- Queries compile to SQL at build time, so SQL mistakes are compile errors and nothing is translated at run time.
- LINQ-style chains for the common case, checked raw SQL for complex queries. Interpolated values become parameters.
- Both schema directions are supported. Code first: the database is generated from `data` types. Database first: a tool command (`kurz db pull`, in the manner of `dotnet ef`) connects through the wire driver package and writes the schema into the repository as generated Kurz. Builds read only that file, so compilation stays sealed (section 9, rule 5).
- Failures: an unreachable database is an exception (supervisor and `retry` deal with it). A constraint violation or a missing row is a union case.

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

### Actors addressed by key

```
actor Session per int userId { ... }

Session[42].Add(item)      // the runtime finds it, or creates it, on some machine
```

No spawn, no lookup, no registry. The runtime places each key on a machine, puts idle actors to sleep and rebalances when machines join. For the developer this is horizontal scaling with zero code; the cost is compiler and runtime work. It needs a cluster-wide directory, and during a network split the three-step rule of section 7 applies per key.

### Durable actors

```
durable actor Subscription per int userId {
    mut plan = Plan.Free
    pub void Trial() { plan = Plan.Pro; wait 30days; plan = Plan.Free }
}
```

Every change reaches an actor as a message, so logging messages to disk makes its state survive crashes, deploys and machine death. Enabled only where the `durable` keyword is used. Kurz gets its own storage engine for this, written in Kurz.

### Inbox overflow

Waiting calls throttle themselves: the caller waits, so load cannot pile up. For everything else Simon's direction is that a full inbox spills into a storage engine local to the node or process, the same engine durable actors use, to free memory and queue work without cluttering everything else.

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

### Package permissions

```
use json                       // may touch: nothing
use mysql allow network        // may touch: network only
```

Whole-program compilation knows which package opens sockets, reads files or calls C. A package that exceeds what the project granted fails to compile.

### Constraints in types

```
data User(int Age where 0..150, string Email where IsEmail)

route POST "/users" (User user) => store.Add(user)
```

An invalid value cannot exist. Input from outside yields `User | Invalid` automatically, so validation needs no code in the handler. Custom validators must be possible, as C# has them; a validator is ordinary Kurz code.

### Clients and devices as cluster members

Wanted in principle (browser through WebAssembly, microcontrollers), but it needs far more than treating them as machines: a connection layer to clients, client state, and more. Server to server is simpler than server to client. It calls for the same kind of split C# projects have (backend, shared, client), where the developer states in code, easily and securely, which actor runs where, what it may access and how connections are made. Not designed yet.

### Speed

Runtime speed has priority everywhere; the compiler may be heavy. One planned optimization: an actor handles one message at a time, so everything allocated while handling it and not stored in actor state can be freed in one sweep at the end. Beating hand-written C in some scenarios is welcome.

## 14. Open

- The cycle rule against mutable trees. `mut List<Node> children` can close a cycle by itself (`a.children.Add(b)`, then `b.children.Add(a)`), so the rule of section 3, judged by types, rejects the `Node` example given there and every other mutable tree; `weak` on the back-pointer is not enough. Options raised on 2026-10-01: keep the rule strict (trees are immutable `data`, or one flat owner with `weak` or index edges), a single-owner rule where a node sits in one place and changes place with `move`, or a collector that a class opts into by keyword.
- What `mut` covers: only the variable, as `readonly` does in C#, or everything reachable through it, with `data` and collections behaving as values.
- How an actor reacts to a callee that crashed, timed out or is unreachable, given that there is no `catch`. `retry` and the circuit breaker need an answer.
- How a singleton actor is declared and addressed (section 11 calls it "a named actor"), and when an idle keyed actor that is not `durable` loses its state.
- What happens when the local storage behind a spilled inbox is full as well.
- The client and device split: where actors run, trust and access rules, the connection layer.
- How custom validators report what was wrong.
- Database migrations.
- Names: the fire-and-forget keyword, the deadlock override word, the fairness keywords.
- Where the TLS cipher primitives come from in the long run.
- Package system.

## 15. Prototype

A throwaway v0 exists outside this repository and was never committed. It was written before this design existed: a C# compiler (`Program.cs`) that emits C and builds through `zig cc`. Its syntax and semantics are not authoritative, and its backend contradicts section 2 (LLVM IR, not C). Only the name carries over.
