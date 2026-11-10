---
name: a-corpus-case-is-filtered-by-its-display-name
description: the corpus suites are `[Theory]` methods whose `[MemberData]` gives each case's path, so a case's path stands in a test's display name and not in its fully qualified name; a run filtered by `FullyQualifiedName` and the case's name reached no test of the case and said 1 test, 0 failed, on a test of `CheckerTests` (pull request 428, 2026-11-09); filter a case by `DisplayName~<path>` and read the count of tests the run names
metadata:
  type: reference
---

## What happened

In pull request 428, on 2026-11-09, a run meant to build and run `http/serve-stream-length.kz` was
filtered by `FullyQualifiedName` and the case's name. The suites that read the corpus,
`CorpusTests`, `SemanticModelTests` and `RunTests`, are `[Theory]` methods whose `[MemberData]`
gives each case's path as the argument: the fully qualified name of such a test is its class and
its method, as `Kurz.Compiler.Tests.RunTests.A_built_case_runs_as_its_header_says`, and the path
stands only in its display name. The filter reached no test of the case, the run reached one test
of `CheckerTests`, and it said 1 test, 0 failed, which reads as a pass of the case. Filtered by the
display name, the same case ran 5 tests: the front end's and the checker's reading of it, its
semantic model, and both runs of `RunTests`.

## How to apply

- Filter a corpus case by its path in the display name,
  `dotnet test ... --filter 'DisplayName~http/serve-stream-length'`, as
  [a client case forks its server](a-client-case-forks-its-server.md) does for the client's cases.
  `judgment step`
- Read the count of tests the run names against the count the filter should reach, 5 for a case
  that is built and run: a filter that reaches none of the case passes on whatever else it
  matched. `judgment step`
