---
name: what-goose-does-for-speed
description: Research of 2026-10-30 - the Goose language's claim to run faster than Rust, how strong its evidence is, the mechanisms its speed comes from (G1 to G15, W1 to W3), each with its source and quoted sentence, and which of them Kurz takes after the owner's answers of that day; read it against a design change (playbook node B4)
metadata:
  type: reference
---

# What Goose does for speed, and what Kurz takes from it

Read on 2026-10-30. Every fact about Goose comes from the page named beside it, with the sentence
quoted; the names resolve under "Sources" at the end. Nothing was built, run or measured for this
note. A figure computed for this note from published numbers says so.

Verdicts: ALREADY (Kurz has it), ADOPTABLE (a question for the owner; the item says whether it was asked), CONFLICTS (with a
decision of the record), UNKNOWN. An answered question names the owner's choice and the section of
the record whose trace holds each option with its cost. Before a design change, name the mechanism
or the finding it meets (knowledge/diagram-design-change-playbook.md, B4).

## What Goose is

- Goals: "A memory-safe systems language with no heap allocator, garbage collector, or lifetime
  annotations. In this repository's benchmarks, Goose runs faster than C++ and Rust while using less
  memory." Safety without annotations: "Every reference and slice has a static root: the variable
  that bounds its target's lifetime." and "The compiler infers roots and specializes functions for
  them, so no annotations are needed." (front page)
- State: "Goose is new.", "a whole-program compiler of about thirty thousand lines of C++" (front
  page); "The Goose Language — Specification (v4 draft)" (spec); repository created
  `"created_at": "2026-08-30T21:10:01Z"` (GitHub API); releases v2026.1 (2026-10-14), v2026.2
  (2026-10-16), v2026.3 (2026-10-21) (releases). "I had designed Goose several years ago, and had
  started to implement it". "These are deliberately out of scope for now: moves for resizable
  values, more than one resizable per struct, labeled break, namespace privacy, and more OS/library
  access." "The language features reusable and reusable[] are deprecated in favor of these pool
  types." (front page)
- Memory: "There is no general-purpose heap and no malloc. All dynamic allocation is expressed as
  values living on data stacks, owned by locals (or globals)."; "N data stacks — large
  virtual-address-space reservations (reserved up front, committed on use). N is computed
  statically by the compiler (§10.3). Each data stack is a bump pointer." (spec §1.2); "Values are
  pushed in lifetime order and popped en masse when the owning scope exits."; "At most one
  resizable value is live per stack, and for its entire lifetime it is the topmost value on that
  stack."; "The compiler proves all of this statically; there is no runtime bookkeeping beyond the
  bump pointers themselves." (spec §1.3). Pools: "Since a slot's memory is never actually freed, a
  stale slot reads a different value of the correct type rather than corrupting memory." (front
  page)
- Compilation: "Goose compiles to one C file, so it runs wherever a C compiler does and calls C
  directly through extern fn."; "The bundled TinyCC backend compiles and runs a program
  in-process"; "Everything is monomorphized." (front page); "Goose is a closed-world, whole-program
  compiler." (spec §10.1)

## The claim "faster than Rust"

- Source: "Over sixteen benchmarks Goose runs at 3.3x the speed of idiomatic C++, 1.16x
  hand-optimized C++ and 1.12x the best safe Rust, and uses 1.9x, 1.3x and 1.2x less memory
  (results). These gains come from Goose's data layouts and memory model." (front page)
- Press restates the claim, as GIGAZINE's title does (GIGAZINE); none of the articles runs a
  benchmark: "I did not run the benchmark suite or build a production application in Goose."
  (dev.to); "a lack of reproduction benchmarks by third parties" (note.com).
- "How many times faster (or smaller) Goose is than the best row of each comparison at the large
  size" (results), against Rust, as MSVC build / clang build / memory: sum 1.05/0.98/1.00, push
  1.08/1.08/1.00, strlist 1.24/1.22/1.04, records 2.26/2.02/3.98, tree 1.09/1.03/1.00, interp
  1.41/1.29/2.04, graph 1.16/1.32/1.00, words 1.30/1.74/1.32, particles 1.00/1.03/1.00, sexp
  1.11/1.16/2.43, lru 0.81/1.09/1.03, scene 0.82/1.02/1.35, calc 0.91/0.89/1.00, bintrees
  1.14/1.16/0.99, respond 1.19/1.35/1.00, blur 1.00/1.12/1.00; geometric mean 1.13/1.19/1.25.
  `words` "compares different hash functions and table policies" (summary). Under clang six rows lie
  within 10% of Rust (computed for this note).
- Benchmarks: "The first ten are small, each testing one construct, from sum (the control) to sexp
  (the flagship)."; "The benchmarks cover common algorithms that may benefit from this design, as
  well as workloads where Goose is likely to struggle." (design)
- Machine: "AMD Ryzen 9 9950X3D 16-Core Processor", "94 GB", "Windows 11 10.0.26200 (AMD64)",
  "MSVC 19.51.36260", "clang-cl 22.1.3", "rustc 1.98.0 (88d9e12ae 2026-08-18), -O -C
  codegen-units=1" (results).
- Method: "These are whole-process measurements on one machine. They include input generation,
  construction and teardown, use the best of three runs after two warm-ups, and do not pin cores."
  The aggregate picks per row: "For each backend and comparison tier, it uses the fastest Goose
  implementation. Memory figures are selected separately, using the implementation with the lowest
  memory use." and "No single implementation has all of these results." (summary)
- Rust code: "The Rust comparisons use safe, idiomatic implementations."; "Rust programmers
  commonly substitute a hasher crate, but this suite allows only std."; "bounds checks on, unwinding
  panics, and no target-cpu beyond the x86-64 baseline" (results).
- Reproduction: "The benchmarks themselves are bench/goose/*.goose, bench/cpp/*.cpp and
  bench/rust/*.rs"; "Generated by bench/run_bench.py on 2026-10-16 14:59."; "The compiler was
  rebuilt in Release from f6d31d0e"; "Every implementation of a benchmark, under every toolchain,
  prints the same checksum lines and the harness verifies they agree."; "Raw full-suite
  measurements are saved locally in bench/gen/results.json [...] that directory is ignored by Git.";
  "Differences under ~10% are within measurement noise."; "The allocator-heavy rows are the least
  reproducible in the suite, and they are the ones the headline ratio rests on."; "This CPU has a
  very large L3, so small and medium can sit entirely in cache." (results)
- Other suites, ported by the project: plb2 `matmul`, 2.42x Rust: "plb2 builds C and Rust for the
  baseline x86-64 instruction set, and that is what gives Goose's `simd` its lead on `matmul`. Built
  for this CPU (`-march=native`, `-C target-cpu=native`), C runs `matmul` in 0.211 s, level with
  Goose, and Rust in 0.324 s." (plb2). `loops`, 3.12x Rust: "Goose computes a libdivide-style magic
  number for the divisor before the loop and turns each `%` into a multiplication, a shift and a
  subtraction; C, C++ and Rust leave a `div` instruction in the loop." `levenshtein`, 1.32x Rust:
  "Goose's optimizer regroups integer min and max chains so that the operand carried from the
  previous iteration is combined last" (four-program suite). PLB `binarytrees`, up to 15x Rust: "The
  allocation-heavy problems (binarytrees, merkletrees) say as much about Windows' heap as about
  Rust" (PLB). The five-test suite (five-test suite) is ported too.
- Third party: LangArena, 50 tasks in 27 languages: "Goose: added (goose ea1f368, Clang/GCC
  variants)", "AMD Ryzen 7 3800X 8-Core Processor", "rustc 1.99.1" (LangArena results); the Goose
  code came through the pull request "Add Goose" (LangArena pull request) and lives in the Goose
  repository (Goose's LangArena port). Computed for this note over the 50 tasks, `Goose/Clang`
  against `Rust`: 38.1 s against 54.8 s, a geometric mean of 1.54, Goose faster in 43 of 50, ratios
  from 0.41 (Base64::Decode) to 12.4 (Json::ParseDom), 1.25x less memory. In Binarytrees::Obj
  (11.9x): "The tree's nodes live in a pool the run owns, so a link can be a 4-byte offset from the
  link itself, null for none." (binarytrees port)
- Against the claim, beside the points above: "Losses and near-ties remain." (summary); "This is
  what I get on my M1 if I ask the LLM to write more performant (still safe) solutions to a couple
  benchmarks", with Goose at 0.54x on `records` and 0.75x on `graph`, unverified (HN 2).
- Verdict of the research: Goose is faster on pointer-rich, allocation-rich and variable-size data,
  and level elsewhere. "It is not evidence that a random Rust service, command-line tool, or web
  backend becomes faster when rewritten in Goose." (dev.to)

## Where the speed comes from, and where Kurz stands

"Most of the advantages above are the same advantage seen from different angles: they shrink the
bytes touched per unit of work." and "If a benchmark's data is a flat array of scalars, expect no
advantage." (design). "Much of the speed comes from the memory model: no heap allocation or
per-object teardown, contiguous data, narrow links, and enums sized for the variant they contain."
(front page)

- G1, data stacks. "Freeing a structure with a million elements takes a single store, no matter how
  deeply it is nested." (front page); "C++ and Rust pay for tearing down every unique_ptr/Box/String
  they built, and that teardown is the same cache-hostile walk as building was."; a Rust arena "is
  2.8x to 5.9x faster than Box on the three benchmarks that have both." (design). Kurz: no heap at
  all CONFLICTS with the counted values of sections 3 and 4; a region freed in one sweep was
  ALREADY in part (the sweep at a message's end, section 13) and is answered: "Region per message",
  section 13 of the record (Q1), with the blocks that hold no counted pointer alone ("No counted
  pointers", section 13).
- G2, memory never moves. "push returns a reference to the new element, so a program can link
  elements while building the array. A vector<T> or Vec<T> may reallocate and cannot provide this
  guarantee." (front page); "a grown-to-n array occupies n elements, where Vec/vector carry up to 2x
  capacity slack on top of the allocator's own rounding." (design); "the interesting difference is
  in the source code, not in the timings." (results). Kurz: references into a list CONFLICT with
  section 3; the rounding of a list's block to its size class is ADOPTABLE, Q2, not asked. Rust's
  `Vec`: "its pointer points to `len` initialized, contiguous elements in order" (Rust `Vec`);
  jemalloc: "spaced such that there are four size classes for each doubling", "which limits
  internal fragmentation to approximately 20% for all but the smallest size classes." (jemalloc)
- G3, nested data inline. "A string, an array of strings, a record with variable-size fields and an
  array of those records are each stored as one contiguous block that contains no pointers."; the
  sample "is 29 bytes and 0 allocations. As C++ std::string + std::vector<Item> it is 160 bytes and
  1 allocation, and as Rust String + Vec<Item> it is 153 bytes and 4 allocations."; "an array of
  variable-size elements is sequential: you can iterate it but not index it" (front page). Kurz:
  answered: "Packed list in std", section 13 of the record (Q3).
- G4, enums in two sizes. "Variable-mode ADTs store each value at the size of its own variant
  rather than that of the largest variant." (front page); "records is 2.02-2.26x faster than Rust on
  3.98x less memory: a variable-mode enum stores each record at the size of its own payload,
  including inline text." (summary). Kurz: answered with G3: "Packed list in std", section 13 of
  the record (Q3).
- G5, narrow links. "A relative reference stores a typed, checked link as a 1, 2 or 4-byte
  offset."; "Saving it is a single write, and loading it is a single read followed by a verification
  pass that rejects malformed or hostile data." (front page); "With self-relative links and index
  slots it is 1.5x behind the Rust and C++ arenas." (results). Kurz: ALREADY as indices (section 3),
  with the arena whose handles carry a generation (section 3, 2026-10-30); saving as bytes UNKNOWN, open (section 14).
- G6, compact scalars. "packed structs (no padding), u8/u16/ varint array length fields, u8 enum
  tags, varint payload fields" (design); "Dropping #pragma pack(1) where the layout is already
  gap-free: no difference on either backend." (results). Kurz: ALREADY for enums; varint CONFLICTS
  with C#'s sizes (section 4).
- G7, in-place construction. "A value is constructed at its final destination through any depth of
  calls." (front page); "Rust moves large values around freely with memcpy, which is a well-known
  weakness." (design). Kurz: ALREADY for lists and strings.
- G8, bounds checks proven away. "Every index is bounds-checked, and the compiler removes checks it
  can prove unnecessary." (front page); "Every call site records what it proves about the arguments
  it passes" and "A specialization enters with the meet of those facts" (spec §10.5); "Under v145 the
  nine checks per pixel keep the loop scalar: proving them away makes the flat kernel 1.8x faster
  and the row-slice form 2.9x faster." but "Under clang the checks have no measurable cost: clang
  vectorises the checked loop to run as fast as the unchecked one" (results). Kurz: answered:
  "Goose's full set", section 13 of the record (Q4).
- G9, function values at compile time only. "Every call of a function value is direct and can be
  inlined, function values cannot escape, and there are no closure objects or function pointers, so
  a higher-order function compiles to exactly the loop it looks like." (front page). Kurz:
  CONFLICTS with section 4 (the owner, 2026-10-25, "Pair + heap env").
- G10, whole-program specialization. "Roots and stacks are compile-time constants inside each
  specialization, so pushes through references compile to direct bumps of a known global." (spec
  §10.1 to §10.2). Kurz: ALREADY (section 2), and a generic function is a copy per instance
  (section 4, 2026-10-30).
- G11, returning across frames. "return E from f returns E as the result of the innermost active
  call of f, however many frames up that call is."; "It is implemented as a hidden discriminant
  checked in each frame rather than as an unwinder" (front page); "calc takes 481/491 ms against the
  Rust arena's 436 ms." (summary). Kurz: ALREADY (section 5).
- G12, tag dispatch. "Calls dispatch on the tag through a jump table, and the compiler checks that
  the overloads cover every variant" (front page); "no vtable pointer per object (8 bytes on every
  polymorphic node), a jump table instead of an indirect call, and the arms stay inlinable."
  (design). Kurz: ALREADY for unions; classes keep C#'s virtual methods.
- G13, threads that share nothing. "A worker is compiled as a separate program with its own memory,
  and flat values are passed between threads through typed queues with a memcpy." (front page);
  "The suite does not benchmark saving and loading relative-reference structures or sending them
  through thread queues." (design). Kurz: ALREADY, and a big value is shared, not copied (section 6).
- G14, work above the C compiler. `simd fn` runs "every time the function is called, to run the
  widest version that the CPU running the program supports." (spec); a divisor turned into a
  multiply can lose: "In coro-prime-sieve this took 138 ms instead of 31." (PLB); min and max
  chains: "28.2 ms instead of 39.6"; "Goose's optimizer inlines the base case into the recursive
  calls" (four-program suite); "the compiler now keeps that pool's top and count in locals across
  the recursion instead of going through its header at every push" (results). Kurz: the CPU of a
  build is answered: "Build option, as Rust", section 2 of the record (Q5); "This instructs `rustc`
  to generate code specifically for a particular processor." (rustc book). Passes of Kurz's own
  above LLVM are ADOPTABLE, Q6, not asked.
- G15, arithmetic at the operands' width. "Arithmetic runs at the operands' own width (6.2), so an
  i32 kernel uses 32-bit arithmetic" (design); "The first version of blur summed nine u8 taps in u8
  and produced incorrect results; every tap needs as u16." (results). Kurz: CONFLICTS with T28
  (the owner, 2026-10-03).
- W1, no aliasing information. "Goose deliberately permits aliasing and so gives the backend no
  aliasing information." and "Rust gives LLVM noalias on &mut" (design); "Under clang, the same form
  is 1.10x faster than Rust (256 against 282 ms)." (results). Kurz: ADOPTABLE, Q7, not asked.
- W2, copies with no move. "By-value semantics with no move operation (4.1, TODO 3) mean that idioms
  a Rust programmer would express as a cheap move are an O(size) copy in Goose." (design). Kurz:
  ALREADY avoided (copy on write).
- W3, pools for lifetimes that are not nested. Lifetimes that are not nested "must go through
  reusable pools" (design). Kurz: ALREADY avoided (counts free in any order).

## The questions and the answers

Q1, Q3, Q4 and Q5 are answered above (G1, G3, G8, G14); Q4 against the research's lean, LLVM alone
with `raw`. Q2, Q6 and Q7 were not asked: compiler and std internals with no change a program sees.
For Q2, as built "a `List<int>` with room for 1024 items asks for 4112 bytes and gets 8192"
(computed for this note from `compiler/README.md` and `std/std.kz`); for Q7, a wrong `noalias` mark
is a silent miscompile.

## Downsides

- Goose's own pages: "You have to think about where data lives: who owns it, and how long its scope
  lasts."; "Recursive functions cannot keep growable data across a recursive call."; "There are no
  escaping closures, no function pointers and no dynamic dispatch beyond ADT tags, and only
  whole-program compilation is supported." (front page)
- Critics: "But the benchmarks are tiny, and it’s likely that Goose was tuned on them." and "You
  need a test suite comprised of a wide variety of real world programs, not 16 microbenchmarks,
  possibly specifically chosen to play to the strengths of your language." (HN 1); "I see a general
  lack of reserve()s on strings and vectors" (HN 2); "accessing a record that has been freed and
  re-used could leak sensitive information from one user to another." (HN 1); "If you have a fixed
  number of stacks, it seems like only a fixed number of values can change size at any given time."
  (HN 2); "Goose is a straightjacket by comparison." (HN 1); "message passing with literal memcpys
  (rather than, say, just a shared buffer, like is common in Rust) is very expensive when the
  messages are huge." (HN 2); "it remains a low level systems language that requires you to think
  hard about data organization." (HN 2); "It is completely unsuitable for tasks like 'writing a web
  server' or 'creating complex UI apps' because it cannot use closures or flexible heap
  structures."; maintainability is named a challenge "due to AI having implemented most of it"
  (note.com). Web serving is a Kurz target (sections 1 and 13), one more reason G1 and G9 stay out.

## Sources

- front page: The Goose Programming Language, https://aardappel.github.io/goose/
- summary: Goose benchmark summary, https://aardappel.github.io/goose/bench/summary.html
- results: Goose benchmark results, https://aardappel.github.io/goose/bench/results.html
- design: Goose benchmark design, https://aardappel.github.io/goose/bench/design.html
- spec: The Goose Language — Specification (v4 draft), https://aardappel.github.io/goose/docs/goose_spec.html
- GitHub API: Goose repository metadata, https://api.github.com/repos/aardappel/goose
- releases: Goose releases, https://github.com/aardappel/goose/releases
- plb2: plb2 in Goose, https://github.com/aardappel/goose/blob/master/bench/plb2/README.md
- five-test suite: https://github.com/aardappel/goose/blob/master/bench/kostya2/README.md
- PLB: Programming-Language-Benchmarks in Goose, https://github.com/aardappel/goose/blob/master/bench/plb/README.md
- four-program suite: https://github.com/aardappel/goose/blob/master/bench/bddicken/README.md
- Goose's LangArena port: https://github.com/aardappel/goose/blob/master/bench/kostya/README.md
- binarytrees port: https://github.com/aardappel/goose/blob/master/bench/kostya/goose/src/binarytrees.goose
- LangArena results, the file of 2026-10-21: https://github.com/kostya/LangArena/tree/main/results
- LangArena pull request "Add Goose": https://github.com/kostya/LangArena/pull/28
- HN 1: Hacker News, "Goose Programming Language" (2026-09-18), https://news.ycombinator.com/item?id=49748954
- HN 2: Hacker News, "The Goose Programming Language" (2026-10-24), https://news.ycombinator.com/item?id=50224100
- dev.to: "Goose vs Rust: Can a New Language Beat Rust at Memory-Safe Performance?", https://dev.to/jamilxt/goose-vs-rust-can-a-new-language-beat-rust-at-memory-safe-performance-5dpb
- note.com: "What is the new language 'Goose' that 'ditched the heap'?", https://note.com/humble_bobcat51/n/n74637f4c6e22?hl=en
- GIGAZINE: "Goose is a programming language that runs faster and uses less memory than C++ and Rust.", https://gigazine.net/gsc_news/en/20261027-goose/
- jemalloc: jemalloc manual, https://jemalloc.net/jemalloc.3.html
- Rust `Vec`: Rust standard library, https://doc.rust-lang.org/std/vec/struct.Vec.html
- rustc book: The rustc book, codegen options, https://doc.rust-lang.org/rustc/codegen-options/index.html
