# 18. Settings

A program declares its settings as one `data` type with the modifier `settings`, which the runtime
fills at the program's start from four layers, a file in Kurz's syntax, a file for the environment,
the environment's variables and the command line, and reaches the value by the name `settings`
(§12, the owner, 2026-10-30, "Typed settings data", and 2026-11-11, #448, comment 6790380063).
The compiler builds no settings type (`compiler/README.md`), so that no rule of this chapter has a
case: a case's program also runs with no environment, no settings file and no argument.

### B1 (decided, §12) The settings type and the name `settings`

A program names its settings type by the modifier `settings` before one `data` type,
`settings data AppSettings(string Root, secret string SecretKey, string Address = "127.0.0.1:9000")`,
and its code reaches the value by the name `settings`, `Serve(settings.Address)`, which every file
sees and the program does not declare, as it reaches `args` (S13). A second type with the modifier
in one program is a compile error (§12, the owner, 2026-11-11, Q1 (a)). The type's constraints check
the value the layers give (B2), and a program with a settings type is `not supported` in a build for
Windows, as one that reads a file is (§12, the owner, 2026-10-30, "Not supported, as files").
*(assumed, §12: every function and method of every file sees `settings`, where the top-level
statements alone see `args` (S13); the name stands in a program that declares a settings type, where
a declaration of the program's own named `settings` is `redeclared`, as one named `Log` is (P1), and
is an ordinary name in a program that declares none; `settings` is no `mut` value)*

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
The settings are read once, at the start, and no layer is read again while the program runs.
*(assumed, §12)*

No case: as B1's.

### B3 (decided, §12) Where the files are

The runtime looks for `settings.kz` in the working directory, or in the directory the variable
`KURZ_SETTINGS` names, and for `settings.<env>.kz` beside it (§12, the owner, 2026-11-11, Q2 (c)).
*(assumed, §12: `KURZ_SETTINGS` names a directory, which takes the working directory's place for
both files, and not the path of `settings.kz` itself)*

A file is a call of the type's constructor with named arguments that gives the fields its layer
sets and no others, a nested `data` field by its own constructor's call,
`AppSettings(Root: "/data", Server: ServerSettings(Port: 9000))`.
*(assumed, §12: the defaults of the round of 2026-11-11)*

No case: as B1's.

### B4 (decided, §12) The environment's variables

A variable names a setting by each name of its path in upper snake case, `__` between the parts,
after the prefix the declaration names, and with no prefix where it names none, as Rust's `config`
and `figment` read variables: under `settings("APP_") data AppSettings(...)`, `APP_ACCESS_KEY` sets
`AccessKey` and `APP_LOGGING__ORDERS` sets the key `Orders` of the map `Logging` (§12, the owner,
2026-11-11, Q3 (b)), where the key arrives as `ORDERS`, the cost the option named. A key of a map
read from a variable is matched without case. *(assumed, §12: the words of the question's lean)* A list's item is a variable of its
own, `NAME__0`, `NAME__1`. *(assumed, §12: the defaults of the round of 2026-11-11)*

No case: as B1's.

### B5 (decided, §12) The command line

An option names a setting by each name of its path in kebab case, `.` between the parts, its value
the next argument or after `=`: `--access-key AK`, `--access-key=AK`, `--logging.orders warning`
(§12, the owner, 2026-11-11, Q4 (b)). `args` keeps every argument (S13), and an argument that names
no setting is left to the program, as ASP.NET leaves it. *(assumed, §12: the question's words for
either form)* A list's item is an option repeated, `--hosts a --hosts b`. *(assumed, §12: the
defaults of the round of 2026-11-11)*

No case: as B1's.

### B6 (assumed, §12) The types of the fields

A field is a `string`; a number or a `bool`, read by its `Parse` (X7) where the layer gives a text,
a variable's value or an option's; a `duration`, in its text (§8); an `enum`, by its member's name;
a nested `data` type; a nullable of one of these; a `List` of one of these, from a list literal in a
file, the variables of B4 and the options of B5; or a `Map` with `string` keys. *(assumed, §12: the
defaults of the round of 2026-11-11)*

No case: as B1's.

### B7 (assumed, §12) A start that refuses

A start with a missing setting, a value that its field's type cannot read or a value that the
type's constraints refuse writes one line on standard error for each, naming the setting's path,
the source of its value, a file and its line, a variable or an argument, and the reason, and ends the
process with the exit code 1, before the program's first statement. The value of a `secret` field
is never written. *(assumed, §12: the defaults of the round of 2026-11-11)*

No case: a case's header holds no line of standard error, and as B1's.

### B8 (open) A source of the program's own

How a program adds a source of its settings beside the four layers of B2, the owner's note on Q1
of 2026-11-11 (§14):

- (a) An interface that a class of the program implements, as ASP.NET's `IConfigurationProvider`
  and figment's `Provider` are, giving pairs of a setting's path and its text, read as a variable's
  value is (B4), with the place a refusal names (B7), and the declaration names the sources and
  where each stands among the four layers.
- (b) A function of the program that the runtime calls at the start with the value of the four
  layers, and whose result is the settings, checked by the type's constraints.

Lean: (a), as both ASP.NET and figment let a program add a provider.
