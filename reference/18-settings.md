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
(§12, the owner, 2026-11-12, details 1 and 9), its refusal decided and its id open, §14 naming no option for it yet. The type's constraints check the value the layers
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
detail 8), its refusal decided and its id open, §14 naming no option for it yet. An empty value is an empty `string`, and a value that any other type's `Parse` refuses,
and no variable sets `null`, at the cost that no variable clears a nullable field (§12, the owner,
2026-11-12, detail 5). A list's item is a variable of its own, `NAME__0`, `NAME__1`. *(assumed, §12:
the defaults of the round of 2026-11-11)*

No case: as B1's.

### B5 (decided, §12) The command line

An option names a setting by each name of its path in kebab case, `.` between the parts, its value
the next argument or after `=`, a `bool` field's only after `=` (below): `--access-key AK`, `--access-key=AK`, `--logging.orders warning`
(§12, the owner, 2026-11-11, Q4 (b)). `args` keeps every argument (S13), a `secret` field's value given
there among them, as a plain `string`, as ASP.NET keeps `args` whole, and an argument that names no
setting is left to the program, as ASP.NET leaves it (§12, the owner, 2026-11-12, the reading said
back, and detail 13). An option with no value, `--verbose`, sets a `bool` field to `true`, as `clap`'s
flag, and another option with no value, last or followed by an argument that starts with `--`,
refuses the start; an empty value is read as a variable's is (B4); and a map's key from an option is
matched as one from a variable (§12, the owner, 2026-11-12, details 5, 11 and 12). An option of a `bool` field takes a value only after `=`, as `clap`'s flag takes none from the next argument: `--verbose input.txt` sets the field to `true` and leaves `input.txt` to the program, and `--verbose=false` sets it to `false` (§12, the owner, 2026-11-12, comment 6821891640, S4, "As clap: value only after =", against the next argument taken as its value, as every other option takes it, at the cost of a flag that cannot stand before an argument of the program; the cost is that `--verbose false` sets the field to `true` and leaves `false` to the program, unlike every other option). A list's item is an
option repeated, `--hosts a --hosts b`. *(assumed, §12: the defaults of the round of 2026-11-11)*

No case: as B1's.

### B6 (assumed, §12) The types of the fields

A field is a `string`; a number or a `bool`, read by its `Parse` (X7) where the layer gives a text,
a variable's value or an option's; a `duration`, in its text (§8); an `enum`, by its member's name;
a nested `data` type; a nullable of one of these; a `List` of one of these, from a list literal in a
file, the variables of B4 and the options of B5; or a `Map` with `string` keys. *(assumed, §12: the
defaults of the round of 2026-11-11)* An `enum`'s member is read without case in every layer, as
`Enum.Parse` with `ignoreCase` and `clap`'s value enums read it; a field of another type is a compile
error, its refusal decided and its id open, §14 naming no option for it yet; and a `Map`'s values are of the types above, written in a file as a map literal (§12, the
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

A reload builds a new value from every layer and source *(assumed, §12: from the pairs the source that called `Changed()` gave, or the watched file read again, and the pairs every other source and every other layer gave last, at the start or in a reload that passed, those of the variables and the command line the start's, the pairs of a reload that failed set aside, so that one source's bad pairs do not fail the reloads of the others, as ASP.NET's providers keep the data each loaded, so that no other source's `Load` runs outside its `Watch`; and one reload at a time, in the order the reloads reach the runtime, each merging the new pairs of the source that called `Changed()`, or of the watched file read again, with the newest pairs of every other source and every other layer, those of the start or of a reload that passed, in the order in which the start merges the layers and the sources (B2, B15), so that two reloads at once publish one value after the other and the last value published holds the new pairs of the reload that published it and the newest pairs of every other source and every other layer, those of the start or of a reload that passed)*, checks it as the start does, and publishes
it by one atomic swap of a pointer to the new value, which is never changed, as Rust's `arc-swap`
swaps an `Arc`, so that a reader sees the old value or the new one, whole. An actor reads the value
once for each message, as it stood when the message began (§12, the owner, 2026-11-12, B9 (b)), and
B16 gives the code that handles no message. A message and a handler call hold the value they began on as Rust's `arc-swap` holds an `Arc` it loaded, by a slot of the scheduler's thread: the load writes the value's address into a free slot of its thread, changes no count, and reads the pointer again, as `arc-swap` confirms a debt: where the pointer changed between, it gives up its slot as `arc-swap` pays a debt back; a reload, after its swap of the pointer and before it gives up the old value, walks every thread's slots and pays each slot that holds the old value, taking a count of it and emptying the slot by an atomic compare and swap, which a slot its call emptied first refuses, that count then given back; the call at its end empties its slot as `arc-swap` clears its debt, and gives back the count a reload took where one paid the slot; a thread whose slots are all taken takes a count at the load; and the old value is freed when the last count ends, its memory held beside the new value's until then (§12, the owner, 2026-11-12, comment 6821891640, S2, "Per-thread slots, arc-swap", against one count of the value that each message and each handler call changes twice, at the cost of a line of memory that every core writes under load; the cost is a few slots for each scheduler thread, a slot's write, a second read of the pointer and the slot's swap, each with its fence, at each message and each handler call, a walk of every thread's slots at each reload, and that code in the runtime) *(assumed, §12: that the load gives up its slot by an atomic compare and swap against the address it wrote, as the crate pays a debt back: where that succeeds, no reload paid the slot, and the load takes the slow path, taking no count of the value it loaded first; where it fails, a reload paid the slot, and the call holds the count that reload took, with no slot; that the slow path is `arc-swap`'s helping, and that a thread whose slots are all taken falls back to the count by it: the helping slot is three words of its thread, as `arc-swap` keeps the control word of its helping slot apart from the two words of its debt (checked 2026-11-12: `arc-swap` 1.9.5, `src/debt/helping.rs`, `Slots`, and `src/debt/mod.rs`, `Debt`): a control word, which holds nothing, a generation of the thread or the address of a value a reload hands over, told apart by a tag in its two low bits, which such an address leaves free, the allocation entry giving a multiple of 16 (§2), where `arc-swap` hands over the address of an envelope that holds the value, since a pointer of its own may lack that alignment; an address word, which names the settings' pointer the load reads; and an entry word, which holds the address of the value the load read; the load writes the address of the settings' pointer into the address word, swaps a new generation of its thread into the control word, reads the pointer, swaps the address of the value it read into the entry word, and swaps the control word back to nothing, as the crate's `get_debt` and `confirm` do; where that swap gives back the generation, no reload helped, and the call takes a count of that value and gives up the slot's entry by the compare and swap, giving one count back where that fails, since a reload then paid the entry; where a reload replaced the generation, the call holds the newest value with the count that reload took for it, and gives up the unused entry by the compare and swap, giving back the count a reload took for that entry where that fails; so that the call holds a count from then on and the helping slot is free for the thread's next load; and a reload, as it walks every thread's slots, pays an entry of the old value in a helping slot as one in a slot, and where a helping slot's control word holds a generation and its address word names the settings' pointer, takes a count of the newest value and hands it over by an atomic compare and swap of the control word from that generation to the value's address, giving that count back where the swap fails; that a generation steps by 4 in a word of 64 bits, so that its two low bits stay free for the tag, and that a thread whose generation wraps to 0 sets its slots aside, its helping slot among them, until no reload walks them, and takes others, as `arc-swap` puts its node in a cooldown at the wrap and gives it to a thread again once no writer walks it (`src/debt/helping.rs`, "ABA protection", and `src/debt/list.rs`, `new_helping`, `start_cooldown` and `check_cooldown`), so that a reload that read a generation before the wrap hands nothing to a load after it; at the cost of one helping slot of three words for each scheduler thread, four atomic writes, a count taken and a compare and swap at each load on that path, a count given back at that call's end, at each reload a look at every thread's helping slot with a count taken for each load it helps, a word of each thread's slots that a reload raises before it walks them and lowers after, and a set of slots set aside, and another made, at each wrap of a thread's generation, after 2^62 loads on that path; that a call ends by what it holds: a call that holds a slot empties it by the same compare and swap, as the crate clears its debt, and gives back the count a reload took where the swap fails, since a reload then paid the slot; a call that holds a count, from the slow path, from a reload that paid its slot or from a park, gives that count back and runs no compare and swap, so that no call empties an entry another load of its thread wrote since; that a read of the newest value outside a message holds it so for the statement it stands in; that a message or a handler call that holds a slot and parks, waiting on a blocking call or on another actor, takes a count of its value while its slot still holds it and then empties the slot by the compare and swap, before it leaves its thread, giving that count back where the swap fails, since a reload then paid the slot and the count that reload took is its own, and holds a count from then on, as `arc-swap` turns a guard into a full count and keeps its guard on one thread, at the cost of a count taken and given back at each such park; and that a part of the value kept past the call, a `string` or a `List` it holds, is a counted value of its own, so that keeping it holds that part and not the old value)*

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
called it and hands the pairs it gave to the runtime, which merges, checks and publishes them (B9);
at the start the source's first `Load` runs before `Watch` begins (B19), so that the two never
overlap and a source is reached from one actor at a time, as ASP.NET's reload token calls its
provider's `Load` on the thread that fires it, at the cost of a `Watch` held while `Load` runs (§12,
the owner, 2026-11-12, comment 6820218118, "Changed() calls Load"). `Changed()` gives nothing and returns once the runtime published the new value or kept the last good one (B10), a failed `Load` or check written as B7 writes it, as ASP.NET's reload token runs its provider's `Load` and the callbacks on the thread that fires it (§12, the owner, 2026-11-12, comment 6821891640, S1, "Waits, returns nothing", against a `bool` that says whether the value was published, at the cost of a result every source handles and a signature of the library to keep, and against a call that returns at once, the merge, the check and the publish running later, at the cost of a source that never learns when its value is seen or that it failed; the cost is a `Watch` held while the merge and the check run, and a source that cannot answer a failure) *(assumed, §12: that the `Watch` is held too while the reloads ahead of its own run, one at a time, and while the publish runs, the walk of every thread's slots among it)* *(assumed, §12: that the runtime moves each source into the actor of its `Watch` once its first `Load` answered, handed over as a value sent with `move` is (section 6) and not shared, so that the function of B15 gives each source as a value no other code keeps, which that check holds; that the handle stays in that actor, since `Watch`'s parameter has no `move` and a function sends a value only through a parameter marked so (section 6); and that a service with a state is an actor, reached by its messages (section 11), so that no state of a source is reached from two actors)*

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
and B18), its refusal decided and its id open, §14 naming no option for it yet. *(assumed, §12: a read the checker cannot see, through a method of an interface or a message to an actor, refuses the start at run time as B7 does)* Such a read in a `Load` at a reload fails the reload as B10 says (§12, the owner, 2026-11-12, comment 6821891640, S3, "The reload fails", against the value published before the reload, read with no check, at the cost of pairs that depend on the value they replace, so that the result depends on the order of the reloads, the cost detail 2 set aside at the start; the cost named then a flag of the thread, a `Load` running, read at each read of `settings`). The mark of that check is the running message's and not the thread's: a message that a `Load` sends carries the mark of its reload, and a read of `settings` in the message it starts fails that reload as a read in the `Load` does, the same mark at the start refusing the start, so that a read the `Load` reaches in another actor is seen, and a `Load` whose actor is parked marks no message of another actor that its thread runs meanwhile (§12, the owner, 2026-11-12, pull request 504, comment 6823581994, "Mark the Load's messages", on the high findings of that pull request's round, against S3 held to the `Load`'s own actor with the read in another actor left open, at the cost of a value that the order of the reloads can still decide through such a message, and against the value published before the reload for such a read, at the cost S3 was chosen against; the cost is one word in the header of each message, its copy at each send, and the read of the running message's mark at each read of `settings`, where the thread's flag was read) *(assumed, §12: that where no message runs, the check reads the mark of the actor's body that runs, `Watch`'s among them, which the runtime marks for its call of `Changed()`, and the start's for the first `Load`, kept where the running message's mark is read, at the cost of that mark's write when such a body starts and ends, so that a `Load` that runs in `Watch`'s body, which is no message of its actor, reads and sends under the mark of its reload; that a send copies the mark it runs under, so that a message sent from a marked message carries it too and a chain of messages from the `Load` is seen, at the cost of a reload that a read of `settings` along that chain fails although it feeds no pair of the `Load`, a logger's or another service's that a marked message reaches; that a mark of a reload marks until its reload or start ends, at its check or before it, after its `Load` returned: a read of `settings` under it before that check gives the value its message holds, or in a marked body the newest value, as any read there does, and fails the reload at its check, a read in a message the `Load` sent among them, wherever and whenever it runs before that check; that the mark is the number of its reload, the start's among them, and that one state word of the settings holds the number of the reload or the start that runs, a bit that it has ended and a bit that a read under its mark ran: a read of `settings` under a mark reads that word and, where the word holds the mark's number and has not ended, sets the read bit by one atomic compare and swap, reading the word again where the swap fails; the check ends the word by one atomic swap that sets the ended bit, and fails the reload where the word it swapped out holds the read bit; so that a read that finds the word open under its mark's number fails the reload, and one that finds it ended, or under a newer number, marks nothing, at the cost of that word, its read at each read of `settings` under a mark and a compare and swap where that read fails the reload, and of a reload that passes or fails by whether the scheduler runs such a read, in a message the `Load` sent, before or after the check's swap, an order the program does not choose; that a read under the mark of the start while the start's word is open, where no value has been published, gives no value: it sets the read bit and raises an exception, which no signature lists and no code catches (§5), in a message killing the message's actor as every exception does, and in a body, the first `Load`'s among them, ending that body, and the start's check then refuses the start from the read bit as it refuses a missing setting (B7), with its line on standard error and the exit code 1, the id of that exception open (§14); that the start, as a reload does, ends its word by its check's swap before it publishes its value, and publishes none where it refuses; that a message that began before the start published holds no value, and a read of `settings` in it after the start's word has ended gives the value the start published, reading the pointer again until the start sets it where the read comes between that end and the publication, the message holding that value from that read to its end as B9's message holds the value it began on, at the cost of that wait and of a message whose value is that of its first read and not of its beginning, while a start that refuses ends the process before such a read gives a value; that a mark whose reload or start has ended, at its check or before it, a reload whose `Load` gave an `IoError` or whose pairs failed before the check among them, marks nothing, the word ended by the same swap at that end, a read under it reading as its message or its body does, the value its message holds, at the cost of a read along the chain after that end that the order of the reloads can still decide)*

No case: as B1's.

### B19 (decided, §12) The interface of a source

`interface SettingsSource { Map<string, string> | IoError Load(); void Watch(SettingsReload reload) }`:
`Load` gives pairs of a setting's path and its text, each read as a variable's value is (B4), the
spelling of the path standing open (§14), and the
runtime calls `Watch` once after the first load, in an actor of its own, so that a source waits there
on its source and calls the handle of B12, whose `Changed()` runs `Load` in that actor (§12, the
owner, 2026-11-12, comment 6820218118, "Changed() calls Load"); a source whose source never changes
returns from `Watch` at once (§12, the owner, 2026-11-12, B19). *(assumed, §12: the runtime calls `Watch` after the start
published the value, so that `Watch` may read `settings`, and `Watch` has a body that returns at once, which such a source keeps; and that the runtime's call of `Watch` is the body of its actor and no message of it, so that `Watch` reads the newest value at each read, as code in no message does, each read held as a read outside a message is, and B9's value for each message does not hold it)*

No case: as B1's.
