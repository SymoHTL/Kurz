# 18. Settings

A program declares its settings as one `data` type with the modifier `settings`, which the runtime
fills at the program's start from four layers, a file in Kurz's syntax, a file for the environment,
the environment's variables and the command line, and from the sources of the program's own, and
reaches the value by the name `settings` (§12, the owner, 2026-10-30, "Typed settings data",
2026-11-11, #448, comment 6790380063, and 2026-11-12, comments 6815615127, 6818927638, 6819120661,
6819139204, 6819246612 and 6819319795). The compiler builds no settings type (`compiler/README.md`),
so that no rule of this chapter has a case: a case's program also runs with no environment, no
settings file and no argument.

### B1 (decided, §12) The settings type and the name `settings`

A program names its settings type by the modifier `settings` before one `data` type,
`settings data AppSettings(string Root, secret string SecretKey, string Address = "127.0.0.1:9000")`,
and its code reaches the value by the name `settings`, `Serve(settings.Address)`, which the program
does not declare, as it reaches `args` (S13) (§12, the owner, 2026-11-11, Q1 (a)). Every function and
method of every file sees `settings`, where the top-level statements alone see `args` (S13), and
`settings` is no `mut` value: no code changes it, and a reload publishes a new value in its place
(B9) (§12, the owner, 2026-11-12, the reading said back, and B9). A second type with the modifier in
one program is `redeclared` at the second, as a second declaration of a name is (V9). The modifier
stands on a `data` type alone, and on another type or on a generic `data` type is a compile error
(§12, the owner, 2026-11-12, details 1 and 9), whose id §14 leaves open. The type's constraints check the value the layers
give (B2), and a program with a settings type is `not supported` in a build for Windows, as one that
reads a file is (§12, the owner, 2026-10-30, "Not supported, as files"). *(assumed, §12: the name
stands in a program that declares a settings type, where a declaration of the program's own named
`settings` is `redeclared`, as one named `Log` is (P1), and is an ordinary name in a program that
declares none)*

No case: the compiler builds no settings type.

### B2 (decided, §12) The layers

The runtime fills the settings type at the start from four layers, each read after the one before
it and winning over it where both set a field, as ASP.NET's configuration providers do: the file
`settings.kz` (B3), the file `settings.<env>.kz` where it exists, `<env>` the value of the variable
`KURZ_ENVIRONMENT` and `Production` where that is not set, the environment's variables (B4), and
the command line (B5) (§12, the owner, 2026-10-30, "Typed settings data", "Kurz-syntax file" and
"Yes, as ASP.NET"). *(assumed, §12: a source read after another wins over it)* A field's default in
the declaration is the lowest layer, a nullable field that no layer sets is `null`, and any other
field that no layer sets is missing (B7). *(assumed, §12: the defaults of the round of 2026-11-11)*
The layers merge a nested `data` leaf by leaf, a `List` item by item by its index and a `Map` key by
key, as ASP.NET's configuration merges, so that a list a later layer shortens keeps the lower layer's
later items (§12, the owner, 2026-11-12, detail 3). The sources of the program's own (B8) stand among
the layers where the function that makes them places each (B15). The variables and the command line
are read once, at the start; the settings file is read again where the declaration turns its watch on
(B13, B14), and a source where it says that its source changed (B12) (§12, the owner, 2026-11-12).
*(assumed, §12: `settings.<env>.kz` with it, as B13 reads the answer)*

No case: as B1's.

### B3 (decided, §12) Where the files are

The runtime looks for `settings.kz` in the working directory, or in the directory the variable
`KURZ_SETTINGS` names, and for `settings.<env>.kz` beside it (§12, the owner, 2026-11-11, Q2 (c)).
`KURZ_SETTINGS` names a directory, which takes the working directory's place for both files, and not
the path of `settings.kz` itself (§12, the owner, 2026-11-12, the reading said back). A missing
`settings.kz` is a layer that sets nothing, as ASP.NET's `appsettings.json` is optional; a
`KURZ_SETTINGS` that names no directory refuses the start, an empty one is unset, and a relative one
is read from the working directory. An empty `KURZ_ENVIRONMENT` is unset, a value of it with `/`,
`\` or `..` refuses the start, and the file is named as the value is written, the file system
matching it (§12, the owner, 2026-11-12, details 4 and 6).

A file is a call of the type's constructor with named arguments that gives the fields its layer
sets and no others, a nested `data` field by its own constructor's call,
`AppSettings(Root: "/data", Address: "0.0.0.0:9000")`. A field without a default may be left out of a
file for another layer to set: the layers together are what B7 checks.
*(assumed, §12: the defaults of the round of 2026-11-11)* An unknown field in a file, or a file that
is no call of the type, refuses the start (§12, the owner, 2026-11-12, detail 7). *(assumed, §12: a
reading of those defaults: a file's call is checked at the start for the fields it names, each
against the type's field of that name and its type, and for the fields it leaves out once the layers
are merged)*

No case: as B1's.

### B4 (decided, §12) The environment's variables

A variable names a setting by each name of its path in upper snake case, `__` between the parts,
after the prefix the declaration names, and with no prefix where it names none, as Rust's `config`
and `figment` read variables: under `settings("APP_") data AppSettings(...)`, `APP_ACCESS_KEY` sets
`AccessKey` and `APP_LOGGING__ORDERS` sets `Orders` in `Logging` (§12, the owner, 2026-11-11,
Q3 (b)); where `Logging` is a map, the key arrives as `ORDERS`, the cost the option named. Where
the declaration names no prefix, a field named `Path`, `Home`, `User` or `Lang` reads the system's own
variable, the cost the question named for the forms without a prefix. The prefix is a string literal,
the declaration's first argument, written as the variables begin, its `_` part of it, and an empty
one is none (§12, the owner, 2026-11-12, detail 9). A key of a map read from a variable is matched
without case against the keys the lower layers gave, a match keeping the lower layer's spelling and a
new key arriving in upper case, and two keys of one file that differ in case alone refuse the start
(§12, the owner, 2026-11-12, the reading said back, and detail 10). A name breaks into words before a
capital that follows a small letter or a digit, and before the last capital of a run that a small
letter follows, `HTTPPort` as `HTTP_PORT` and `http-port` (B5), as the crate `heck` splits a name, and
two fields of one type whose names meet in one name are a compile error (§12, the owner, 2026-11-12,
detail 8), whose id §14 leaves open. An empty value is an empty `string`, and a value that any other type's `Parse` refuses,
and no variable sets `null`, at the cost that no variable clears a nullable field (§12, the owner,
2026-11-12, detail 5). A list's item is a variable of its own, `NAME__0`, `NAME__1`. *(assumed, §12:
the defaults of the round of 2026-11-11)*

No case: as B1's.

### B5 (decided, §12) The command line

An option names a setting by each name of its path in kebab case, `.` between the parts, its value
the next argument or after `=`: `--access-key AK`, `--access-key=AK`, `--logging.orders warning`
(§12, the owner, 2026-11-11, Q4 (b)). `args` keeps every argument (S13), a `secret` field's value given
there among them, as a plain `string`, as ASP.NET keeps `args` whole, and an argument that names no
setting is left to the program, as ASP.NET leaves it (§12, the owner, 2026-11-12, the reading said
back, and detail 13). An option with no value, `--verbose`, sets a `bool` field to `true`, as `clap`'s
flag, and another option with no value, last or followed by an argument that starts with `--`,
refuses the start; an empty value is read as a variable's is (B4); and a map's key from an option is
matched as one from a variable (§12, the owner, 2026-11-12, details 5, 11 and 12). So a `bool`
option followed by an argument that does not start with `--` takes that argument as its value, as
any option does: `--verbose input.txt` gives `input.txt` to `Parse`, which refuses the start (B7),
where `clap`'s flag takes no value, and `--verbose=false` sets the field to `false`. A list's item is an
option repeated, `--hosts a --hosts b`. *(assumed, §12: the defaults of the round of 2026-11-11)*

No case: as B1's.

### B6 (assumed, §12) The types of the fields

A field is a `string`; a number or a `bool`, read by its `Parse` (X7) where the layer gives a text,
a variable's value or an option's; a `duration`, in its text (§8); an `enum`, by its member's name;
a nested `data` type; a nullable of one of these; a `List` of one of these, from a list literal in a
file, the variables of B4 and the options of B5; or a `Map` with `string` keys. *(assumed, §12: the
defaults of the round of 2026-11-11)* An `enum`'s member is read without case in every layer, as
`Enum.Parse` with `ignoreCase` and `clap`'s value enums read it; a field of another type is a compile
error, whose id §14 leaves open; and a `Map`'s values are of the types above, written in a file as a map literal (§12, the
owner, 2026-11-12, details 12 and 14).

No case: as B1's.

### B7 (assumed, §12) A start that refuses

A start with a missing setting, a value that its field's type cannot read or a value that the
type's constraints refuse writes one line on standard error for each, naming the setting's path,
the source of its value, a file and its line, a variable, an argument or a source of the program's
own (B8), and the reason, and ends the process with the exit code 1. The value of a `secret` field
is never written. *(assumed, §12: the defaults of the round of 2026-11-11)* The reason for a `secret`
field names the error's id and the field's type, never the message of `Parse` or of a constraint,
which can quote the value (§12, the owner, 2026-11-12, detail 15); §14 leaves those ids open, with the
text of the line.

No case: a case's header holds no line of standard error, and as B1's.

### B8 (decided, §12) Sources of the program's own

A program adds sources of its settings of its own beside the four layers of B2 through an interface
that a class of the program implements, as ASP.NET's `IConfigurationProvider` and figment's
`Provider` let a program add a provider, its shape B19's, made by the function of B15 (§12, the owner,
2026-11-12, B8 (a), with the owner's note that the source may read a database, has its services
built, and tells the runtime when its values change). *(assumed, §12: `SettingsSource` and
`SettingsReload` are types of the standard library that every file sees, as `Log` is, and a source
whose first `Load` gives an `IoError` refuses the start as a missing setting does)*

No case: as B1's.

### B9 (decided, §12) What code sees after a reload

A reload builds a new value from every layer and source, checks it as the start does, and publishes
it by one atomic swap of a pointer to the new value, which is never changed, as Rust's `arc-swap`
swaps an `Arc`, so that a reader sees the old value or the new one, whole. An actor reads the value
once for each message, as it stood when the message began (§12, the owner, 2026-11-12, B9 (b)), and
B16 gives the code that handles no message. *(assumed, §12: the value carries a count, as the `Arc` a snapshot of section 11 is published in does, which a message and a handler call take at their start and give back at their end, so that an old value lives while a call that began on it runs and is freed when the last such call ends, its memory held beside the new value's until then, and the count costs two atomic changes of it for each message and each handler call, beside the load of the pointer)*

No case: as B1's.

### B10 (decided, §12) A reload that fails

A reloaded value that fails a check keeps the last value that passed, and each failure is written as
B7 writes it, without ending the process (§12, the owner, 2026-11-12, B10 (a)). *(assumed, §12: a
source whose `Load` gives an `IoError` at a reload fails so)*

No case: as B1's.

### B11 (decided, §12) Two stages

The four layers are read and merged first; each source of the program's own is made from that value
(B15); and the whole is checked once the sources' first loads have answered, bounded by a time the
declaration names, a source that has not answered in it refusing the start (B7) (§12, the owner,
2026-11-12, B11 (a), the chosen option's words). A source is not configured by its own layer.

No case: as B1's.

### B12 (decided, §12) A source says that its source changed

The runtime gives each source a handle, `SettingsReload`, which the source calls,
`reload.Changed()`, when its source changed (§12, the owner, 2026-11-12, B12 (a), and B19, "Load +
Watch", the option whose words name the call, "... and calls `reload.Changed()`", comment
6819483332, recorded in comment 6819319795). `Changed()` runs the source's `Load` in the actor that
called it and hands the pairs it gave to the runtime, which merges, checks and publishes them (B9),
so that a source is reached from one actor at a time, as ASP.NET's reload token calls its provider's
`Load` on the thread that fires it (§12, the owner, 2026-11-12, comment 6820218118, "Changed() calls
Load").

No case: as B1's.

### B13 (decided, §12) The settings file is watched where the program turns it on

The settings file is watched and read again on a change where the declaration turns its watch on
(B14), and is read once, at the start, where it does not; the variables and the command line are
read once, at the start (§12, the owner, 2026-11-12, B13 (b), "but its default disabled").
*(assumed, §12: the watch holds both files, where the answer said "the settings file", as ASP.NET's
host turns `reloadOnChange` on for both of its files; the runtime watches the files through the system's notification of a change,
inotify on Linux, as Rust's crate `notify` watches a file)*

No case: as B1's.

### B14 (decided, §12) Where the watch is turned on

The declaration turns the watch of B13 on by an argument beside the prefix,
`settings("APP_", watch: true) data AppSettings(...)`, as C#'s
`AddJsonFile(path, reloadOnChange: true)` turns it on in code (§12, the owner, 2026-11-12, B14).

No case: as B1's.

### B15 (decided, §12) The function that makes the sources

The declaration names a function of the program, `settings("APP_", sources: Sources)`, which the
runtime calls with the value the four layers gave (B11), and which builds each source with its
services as its constructor's arguments, as §11 wires a service, and gives the sources with the
place of each among the layers (§12, the owner, 2026-11-12, B15). The form of that place, and the
type of the value the function receives, stand open (§14).

No case: as B1's.

### B16 (decided, §12) Code that handles no message

One call of an HTTP handler (W9) reads the value once, as a message does (B9), as ASP.NET's
`IOptionsSnapshot` is read once for each request; code in no message and no handler call, the
top-level statements among them, reads the newest value at each read (§12, the owner, 2026-11-12,
B16).

No case: as B1's.

### B17 (decided, §12) A reload of the levels of the logs

A reload that changes the `Logging` part merges it with `KURZ_LOG` again as the start does (P10), and
a level the inspector set stays above both until the inspector clears it (§12, the owner,
2026-11-12, B17).

No case: as B1's, and as P4's.

### B18 (decided, §12) A read of `settings` while the start fills it

A read of `settings` in a field's default or in a constraint of the settings type, as C# refuses a
field initializer that reads `this` (CS0236), and one that the checker finds reachable from the
function of B15 or from a source's `Load`, is a compile error (§12, the owner, 2026-11-12, detail 2
and B18), whose id §14 leaves open. *(assumed, §12: a read the checker cannot see, through a method of an interface or a
message to an actor, refuses the start at run time as B7 does)*

No case: as B1's.

### B19 (decided, §12) The interface of a source

`interface SettingsSource { Map<string, string> | IoError Load(); void Watch(SettingsReload reload) }`:
`Load` gives pairs of a setting's path and its text, each read as a variable's value is (B4), the
spelling of the path standing open (§14), and the
runtime calls `Watch` once after the first load, in an actor of its own, so that a source waits there
on its source and calls the handle of B12; a source whose source never changes returns from `Watch`
at once (§12, the owner, 2026-11-12, B19). *(assumed, §12: the runtime calls `Watch` after the start
published the value, so that `Watch` may read `settings`, and `Watch` has a body that returns at once,
which such a source keeps)*

No case: as B1's.
