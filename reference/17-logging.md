# 17. Logging

A program logs through `Log`, which every file sees, with C#'s six levels as the names of its
functions and Kurz's interpolated string as the template, as `ILogger`'s surface reads, and each
call is a call site with a static record, as Rust's `tracing` gives each of its macros a
`Callsite` (§12, the owner, 2026-11-04, Q1 (a) and Q2 (a)). Spans, sinks, the inspector's levels,
a `deploy` block's ceiling, `secret-logged` and the instruments of §12 have no rule here.

### P1 (decided, §12) `Log` and its functions

A log call is a call of a function of `Log`, `Log.Information("Paid {order.Id}")`, named after
its level: `Log.Trace`, `Log.Debug`, `Log.Information`, `Log.Warning`, `Log.Error` and
`Log.Critical`, and a program's own type named `Log` is `redeclared` (V9) (§12, the owner,
2026-11-04, Q1 (a)). *(assumed, §12: C#'s six levels with a function named after each)* A file's own
function, variable or parameter `Log` at any level is `redeclared` too, as of `Environment` (S14), a
member of `Log` that names no level is `unknown-name` (V10), and a log call gives nothing: it is a
statement, as a call of `print` is. *(proposed)*

Case: [logging/log-redeclared.kz](../corpus/logging/log-redeclared.kz)
```kurz
data Log(int Level)

print(1)
```

Case: [logging/log-unknown-level.kz](../corpus/logging/log-unknown-level.kz)
```kurz
Log.Verbose("started")
```

### P2 (decided, §12) The template

The one argument of a log call is its template, a string literal (L6): each hole is an expression
checked as every hole is, and becomes a field of the line named by its text, `{order.Id}` the field
`order.Id`, its value kept with its type (§12, the owner, 2026-11-04, Q2 (a)). A template that is
not a string literal is the compile error `log-template`. *(assumed, §12: a hole that is no name
and no path, `{a + b}`, gives a field named by its text; `log-template`)* The spaces between a
hole's braces and its text are no part of the field's name, `{ who }` the field `who`.
*(proposed)*

Case: [logging/template-not-literal.kz](../corpus/logging/template-not-literal.kz)
```kurz
text = "Paid"
Log.Information(text)
```

Case: [logging/template-hole-checked.kz](../corpus/logging/template-hole-checked.kz)
```kurz
Log.Information("Paid {total}")
```

### P3 (decided, §12) The levels, and a call site that is off

The levels from the lowest are `Trace`, `Debug`, `Information`, `Warning`, `Error` and
`Critical`, and `None` in a filter (P4) is above them all. A call site whose level is below the
level its category is filtered at is off: it runs none of its holes, allocates nothing and writes
nothing, and costs a load of the lowest level any category is on at, a load of the call site's
cached state and a branch, as Rust's `tracing` reads its level and its call site's interest (§12,
the owner, 2026-11-04, Q2 (a) and Q11 (b)). A call site that is on runs each hole once, in its
order, and writes its line (P6). Where nothing is set, the level is `Information`.
*(assumed, §12: C#'s six levels, `None` in a filter and `Information` the default)*

Case: [logging/off-runs-no-hole.kz](../corpus/logging/off-runs-no-hole.kz)
```kurz
int Counted() {
    print("counted")
    return 1
}

Log.Debug("off {Counted()}")
Log.Trace("off {Counted()}")
print("done")
```

### P4 (decided, §12) Where the levels come from

The runtime reads the environment's variable `KURZ_LOG`, `KURZ_LOG="Warning,Shop.Orders=Debug"`,
as Rust's `RUST_LOG` is read: a level alone sets the level of every category, and `prefix=level`
the level of each category that starts with the prefix, the longest matching prefix winning, as
ASP.NET's `Logging:LogLevel`. A `Logging` part of the program's settings type, with a level for
each prefix of a category, wins where the program declares one (§12, the owner, 2026-11-04,
Q9 (c)). An unknown level or a malformed filter refuses the start with `log-filter`.
*(assumed, §12: `log-filter`)*

No case: a case gives its program no environment and declares no settings type, and its header
holds no line of standard error.

### P5 (proposed) `KURZ_LOG`

`KURZ_LOG` is read before the program's first statement, and only by a program with a log call in
one of its files, as Rust reads `RUST_LOG` only where a subscriber is set up. Its entries are
separated by commas, the spaces around an entry left out, and an empty entry is passed over, as
Rust's filter passes it over, so that an empty `KURZ_LOG` is as none. An entry is a level, which
sets the level of every category, the last such entry winning, or `prefix=level`, with no space
around the `=`. A category takes the level of the entry with the longest prefix it starts with,
the last of two alike, as .NET's rule selector picks a rule, and else the level of the entries
without a prefix, else `Information`. Level names and prefixes are compared with no regard to the
case of a letter, as .NET compares them. Any other entry, an unknown level among them and
`=Debug`, ends the process before its first statement with the line
`error: log-filter (<the entry>)` on standard error and the exit code 1, as `cpu-lacks-set`
refuses a start (R18), and a value that is not UTF-8 with `error: log-filter (not unicode)`.

No case: as P4's.

### P6 (decided, §12) The line

By default each event a call site that is on makes is one text line on standard error, with its
time, level, category, chain ID, message and fields (§12, the owner, 2026-11-04, Q6 (a)); where the
chain ID and the actor's parts stand in a line, and what a line written outside an actor holds in
their place, stands open (P8). The
category of a call is the name of the type whose method holds it, as C#'s `ILogger<T>` names it,
taken with no container (§12, the owner, 2026-11-04, Q1 (a)), and the category of top-level code
is its file's. The message is the template with its holes filled as an interpolation fills them
(L6). A line's time is UTC in the named form of RFC 3339 with milliseconds, and a line break
inside a value is written as `\n`, so that no value forges a line. Logs build wherever `eprint`
builds (A17; §12, the owner, 2026-11-04, Q16 (a)).
*(assumed, §12: the time, the line break, the category of top-level code)* A build for Windows,
whose runtime gives no clock for a line's time, says `not supported` at a log call (S11).
*(proposed)*

No case: a case's header holds no line of standard error, and a case that expects output writes
nothing to it.

### P7 (proposed) The layout of a line

A line is the time, a space, the level's name as C# writes it, a space, the category, a colon and
a space, the message, and each field in the template's order as a space, its name, `=` and its
value: `2026-11-05T01:23:45.678Z Information Shop.Orders: Paid 17 order.Id=17`, as Rust's
`tracing_subscriber::fmt` lays a line out. The time has three digits of milliseconds. A field of
type `string` stands in double quotes, with `"` and `\` in it after a `\`; a value of any other
type is its text (A2 to A16). A line feed in the message or in a field is written as `\n` and a
carriage return as `\r`, as Rust's `escape_debug` writes them. The category of top-level code is
the name of its file without `.kz`. Where lines were dropped (P9), the line
`<time> Warning Kurz.Log: dropped <n> lines`, `line` for one, stands before the next line written.

No case: as P6's.

### P8 (open) The chain ID and the actor in a line

A line holds the chain ID, and names the actor's type, its key or name where it has one, and the
node (§12, assumed there). Where they stand in a line of P7's layout, and what a line written
outside an actor holds in their place, nobody chose: (a) after the category in brackets,
`Shop.Orders[4bf92f35…]:`, as .NET's console logger writes an event id; (b) as fields after the
template's, `chain=4bf92f35…`, as Rust's `tracing_subscriber::fmt` writes a span's fields.

### P9 (decided, §12) The writer, and the lines dropped

A line that cannot be written is dropped and counted, and the next line written says how many were
dropped (§12, the owner, 2026-11-04, Q7 (a)). A failed write to the log's stream raises nothing
and counts as dropped, where a failed `print` raises `print-failed` (A1). *(assumed, §12: a
failed write)* Where §12 has a call put its line into a buffer of its thread and a writer on the
runtime's pool of threads write the buffers out, the runtime starts no thread: the thread that logs
writes its line in one call of the system, as `print` writes (A1), and a line is dropped where that
write fails. Its cost is a call of the system for each line written, in the thread that logs, which
a slow standard error slows, the cost Q7 (a) set aside. *(proposed)*

No case: as P6's.
