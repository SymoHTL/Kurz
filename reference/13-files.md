# 13. Files, sockets and the process

### S1 (decided, §12) A failed call returns `IoError`

A call on a file or a socket returns a failure of the system, or of the path or the address it was
given, as the case `IoError` of its result, and raises no exception for it, while what raises anywhere
raises in it too (S3, S6): `File.Open(path)` is a `File | IoError`. Every file sees `data IoError(IoKind Kind, int
Code, string Path)`, as it sees `Invalid` (D18): `Kind` is the kind of the failure, `Code` the
system's number for it, and `Path` the path or the address the call was about. The text of an
`IoError` (A3) is the words of its kind, its name's words in lower case, a colon and its path,
`not found: data/x.bin`, and the words alone where the path is empty; the kind `Other` adds the
system's number after its words, `other (os error 24): probe.txt`. `IoKind` is an `enum` (D12)
whose values are the kinds Rust 1.82's `ErrorKind` gives the error numbers of Linux, with each
abbreviation written out: `NotFound`, `PermissionDenied`, `AlreadyExists`, `NotADirectory`,
`IsADirectory`, `DirectoryNotEmpty`, `ReadOnlyFilesystem`, `FilesystemLoop`,
`StaleNetworkFileHandle`, `InvalidInput`, `InvalidFilename`, `ArgumentListTooLong`, `StorageFull`,
`FilesystemQuotaExceeded`, `FileTooLarge`, `NotSeekable`, `TooManyLinks`, `CrossesDevices`,
`ResourceBusy`, `ExecutableFileBusy`, `Deadlock`, `Interrupted`, `WouldBlock`, `OutOfMemory`,
`Unsupported`, `TimedOut`, `BrokenPipe`, `AddressInUse`, `AddressNotAvailable`, `NetworkDown`,
`NetworkUnreachable`, `HostUnreachable`, `ConnectionRefused`, `ConnectionReset`,
`ConnectionAborted` and `NotConnected`; beside them `WriteZero` and `InvalidData`, which no number gives,
and `Other` for a number none of them has, which Rust gives `Uncategorized`. A path that holds a NUL byte is `InvalidInput` and a write
of which the system takes no byte `WriteZero`, each with the number 0, which every error that is not the system's has. A call that
names two paths names the first in its error. *(assumed, §12: what raises in a call on a file or a socket, the
list, the words, the number 0, the text without a path and the first path)*

Case: [system/open-missing.kz](../corpus/system/open-missing.kz)
```kurz
match File.Open("no-such-file.txt") {
    File => print("opened")
    IoError e => {
        print(e)
        print(e.Kind)
        print(e.Code)
    }
}
```

Case: [system/path-with-nul.kz](../corpus/system/path-with-nul.kz)
```kurz
match File.Create("a\0b") {
    File => print("created")
    IoError e => {
        print(e.Kind)
        print(e.Code)
    }
}
```

### S2 (decided, §12) Opening and creating a file

`File.Open(path)` opens the file at `path` to read it, and `File.Create(path)` creates a file at
`path` to write it, or empties the one there; each returns `File | IoError`. A path is a `string`,
relative to the working directory of the process where it does not start with `/`. A file that
`File.Create` makes takes the permissions 0666 less the process's `umask`. A descriptor is opened
close-on-exec. A read of a file opened by `File.Create`, or a write of one opened by `File.Open`, is
the error the system gives, `Other` with the number 9. *(assumed, §12: the path a `string`, the
working directory, close-on-exec and the error of the wrong direction)*

Case: [system/write-read.kz](../corpus/system/write-read.kz)
```kurz
int | IoError Roundtrip() {
    file = File.Create("roundtrip.txt")
    file.Write("hello".Bytes)
    file.Close()
    f = File.Open("roundtrip.txt")
    mut buffer = Bytes()
    print(f.Read(mut buffer, 100))
    print(string.FromUtf8(buffer) else {
        InvalidUtf8 => "not UTF-8"
    })
    end = f.Read(mut buffer, 100)
    File.Delete("roundtrip.txt")
    return end
}

match Roundtrip() {
    int end => print(end)
    IoError e => print(e)
}
```

Case: [system/open-missing.kz](../corpus/system/open-missing.kz)

### S3 (decided, §12) Reading appends to a list

`f.Read(mut into, max)` reads up to `max` bytes, an `int`, appends them at the end of `into`, a
`Bytes`, and returns their count, `int | IoError`. A read at the end of a file, or of a connection its
peer closed, reads no bytes and returns 0. `into` grows as `Add` grows a list (M9): the room for
`max` more bytes is made before the read, so that a count and a `max` past the largest `int` raise
`allocation-failed` before any byte is read, and a block another value shares is copied first (M3).
A `max` below 0 raises `index-out-of-range`. `into.Clear()` empties a list and keeps its room (M9).
*(assumed, §12: the room before the read and a `max` below 0)*

Case: [system/read-appends.kz](../corpus/system/read-appends.kz)
```kurz
void | IoError Chunks() {
    File.WriteAll("chunks.txt", "abcdef".Bytes)
    f = File.Open("chunks.txt")
    mut buffer = Bytes()
    f.Read(mut buffer, 3)
    print(buffer.Count)
    f.Read(mut buffer, 3)
    print(buffer.Count)
    buffer.Clear()
    print(buffer.Count)
    f.Close()
    File.Delete("chunks.txt")
}

Chunks() else {
    IoError e => print(e)
}
```

Case: [system/write-read.kz](../corpus/system/write-read.kz)

Case: [system/read-negative-max.kz](../corpus/system/read-negative-max.kz)
```kurz
void | IoError Negative() {
    File.WriteAll("negative.txt", "abc".Bytes)
    f = File.Open("negative.txt")
    File.Delete("negative.txt")
    mut buffer = Bytes()
    int max = -1
    f.Read(mut buffer, max)
}

Negative() else {
    IoError e => print(e)
}
```

### S4 (assumed, §12) Writing writes every byte

`f.Write(bytes)` writes every byte of `bytes`, a `Bytes`, and returns `void | IoError`: where the
system takes a part, or a signal interrupts the call (`EINTR`), it is called again with the rest, as
`print` writes (record, section 6). A write of which the system takes no byte is `WriteZero` (S1).

Case: [system/write-read.kz](../corpus/system/write-read.kz)

### S5 (decided, §12) A file closes at its last count

A `File` is a handle: a copy is a second holder of the one file, as a list's copy holds its block
(section 3 of the record), and the file closes when the last value that holds it lets go of it,
ignoring an error of that close. `f.Close()` closes it before that and returns `void | IoError`; a
`Close` through one holder closes it for all. Every call on a closed file, `Close` among them, returns
the error the system gives a descriptor that is not open, `Other` with the number 9, with the path
the file was opened with, since a close stores -1 in the file's descriptor before the system closes
it, so that no call that starts after that store passes the system a number it may have given
another file. A call through another holder that read the descriptor before the store and enters
the system after the close can still reach such a number, and so read or write a file its holder
never opened, while a call already in the system keeps the file it entered with: the record names that hazard in the cost of the answer and keeps what bounds it open
(§12, §14). A `File` has no text: `print(f)` is `no-text`. *(assumed, §12: the closed file's
error, the -1 stored at the close, the close that ignores its error, and no text)*

Case: [system/close-twice.kz](../corpus/system/close-twice.kz)
```kurz
void | IoError Twice() {
    f = File.Create("twice.txt")
    File.Delete("twice.txt")
    f.Close()
    f.Close() else {
        IoError e => {
            print(e)
            print(e.Code)
        }
    }
}

Twice() else {
    IoError e => print(e)
}
```

Case: [system/print-file.kz](../corpus/system/print-file.kz)
```kurz
void | IoError Show() {
    f = File.Create("shown.txt")
    File.Delete("shown.txt")
    print(f)
}

Show() else {
    IoError e => print(e)
}
```

### S6 (decided, §12) A whole file

`File.ReadAll(path)` reads the whole file at `path` into a `Bytes`, `Bytes | IoError`, and
`File.WriteAll(path, bytes)` creates or empties the file at `path` and writes `bytes` into it,
`void | IoError`. A file of more bytes than a list holds raises `allocation-failed` (M9).

Case: [system/whole-file.kz](../corpus/system/whole-file.kz)
```kurz
void | IoError Whole() {
    File.WriteAll("whole.txt", "abc".Bytes)
    bytes = File.ReadAll("whole.txt")
    print(bytes.Count)
    print(string.FromUtf8(bytes) else {
        InvalidUtf8 => "not UTF-8"
    })
    File.Delete("whole.txt")
}

Whole() else {
    IoError e => print(e)
}
```

### S7 (decided, §12) Deleting, renaming and asking about a file

`File.Delete(path)` deletes the file at `path`, `void | IoError`; `File.Rename(from, to)` moves the
file at `from` to `to`, over a file that `to` names, in one system call within one file system,
`void | IoError`; `File.Size(path)` is its size in bytes, `long | IoError`; and `File.Exists(path)` is
whether anything has the path, `bool | IoError`, `false` where nothing has it and an `IoError` where
the system cannot tell. *(assumed, §12: the types of `Size` and `Exists`)*

Case: [system/delete-rename-size.kz](../corpus/system/delete-rename-size.kz)
```kurz
void | IoError Move() {
    File.WriteAll("first.txt", "four".Bytes)
    File.Rename("first.txt", "second.txt")
    print(File.Size("second.txt"))
    print(File.Exists("first.txt"))
    print(File.Exists("second.txt"))
    File.Delete("second.txt")
    File.Delete("gone.txt") else {
        IoError e => print(e)
    }
}

Move() else {
    IoError e => print(e)
}
```

### S8 (decided, §12) Directories

`Directory.Create(path)` creates the directory at `path` and every directory above it that is
missing, with the permissions 0777 less the `umask`, `void | IoError`, and succeeds where the
directory exists; `Directory.List(path)` is the names of the directory's entries, without `.` and
`..`, in the order the system gives them, or an `IoError`: each name that is UTF-8 as a string, and
each that is not as a case that carries its bytes, so that one such name hides no other entry (§12,
the owner, 2026-10-30), the name and the form of that case standing open (§14), and a path, a `string`, naming no such entry to
open, rename or delete it *(assumed, §12)*, while whether the file functions take a path given as bytes for such
an entry, as Rust's `Path` holds an `OsStr`, or such an entry stays one a program lists and cannot open, rename or
delete, stands open (§14); and `Directory.Delete(path)`
deletes an empty directory, `void | IoError`. *(assumed, §12: the permissions, the order and the
entries left out)*

Case: [system/directories.kz](../corpus/system/directories.kz)
```kurz
void | IoError Tree() {
    Directory.Create("tree/inner")
    File.WriteAll("tree/inner/one.txt", "1".Bytes)
    print(Directory.List("tree/inner"))
    Directory.Delete("tree/inner") else {
        IoError e => print(e.Kind)
    }
    File.Delete("tree/inner/one.txt")
    Directory.Delete("tree/inner")
    Directory.Delete("tree")
    print(File.Exists("tree"))
}

Tree() else {
    IoError e => print(e)
}
```

### S9 (decided, §12) Listening and connecting

`TcpListener.Bind(address)` listens on an address; `l.Port` is the port it bound, an `int`, which the
system picks for the port 0; `l.Accept()` returns a connection that waits as a `TcpStream`; and
`TcpStream.Connect(address)` connects to a listener. `Bind`, `Accept` and `Connect` each return their
value or an `IoError`, and `l.Port` is read once, at the bind. An address is a host and a port after
its last `:`: a host that is a literal of IPv4, `"127.0.0.1:8080"`, or of IPv6, `"[::1]:8080"`, is read
with no lookup, and any other, `"localhost:8080"`, is looked up through `getaddrinfo`, whose addresses
are tried in its order until one binds or connects, the error being the last one's. The port is read
as Rust reads a `u16`, a `+` and zeros in front taken. A literal is what `inet_pton` reads, of IPv6
inside `[` and `]` with a scope id after a `%` of digits from 0 to 4294967295, zeros in front taken,
and a port of digits alone, as Rust's parse of a socket address reads one, and any other host,
`[::1]` before the port `+0`, an empty scope id and one past 4294967295 among them, is looked up as
it is written. An address that holds a NUL byte is `InvalidInput`, as a path is (S1). A listener
takes every interface where its address is a wildcard, `0.0.0.0`, `[::]`, which takes IPv4 too on
Linux's dual stack, on by default, since the runtime sets no `IPV6_V6ONLY`, as Rust sets none, or a
name that resolves to either. A `TcpListener` and a `TcpStream` are handles as a `File` is (S5); a stream reads, writes
and closes as a file does (S3, S4, S5), and a listener accepts and closes. A listener's socket reuses
its address and passes `listen` a backlog of 128. The line between the errors of a name is drawn by
what `getaddrinfo` returns, asked for stream sockets of any family, as Rust's `LookupHost` asks: an
address without a `:`, a port that is not a number from 0 to 65535 and a name whose lookup succeeds
with no address are `InvalidInput`, as Rust gives them; a lookup that `getaddrinfo` fails with
`EAI_AGAIN`, a failure the resolver calls temporary, or `EAI_FAIL`, a failure of the name server, is
`Other`, and one that it fails with `EAI_NONAME`, `EAI_NODATA` or any other of its errors but
`EAI_SYSTEM` is `NotFound`, each with the number 0, so that the text of such an `Other` reads
`other (os error 0): db.local:5432` (S1); a lookup that fails with `EAI_SYSTEM` is the
error of the system's number in `errno`, its kind and its number those of that error. The error of a connection that `Accept` gave names the string its listener's `Bind`
was given, `127.0.0.1:0` where the system picked the port, and one that `Connect` made the string it
was given. The kind of a lookup that `getaddrinfo` fails is decided (§12, the owner, 2026-10-30,
"NotFound, as C#", and "Split NotFound / Other", which gives `EAI_AGAIN` and `EAI_FAIL` the kind
`Other`, as Rust gives every failed lookup one kind, where .NET reads them as `TryAgain` and
`NoRecovery`). *(assumed, §12: the reuse of the address, the 128, the type of `Port`, the
grammar of the port and of a literal, the NUL byte, the errors of an address, `InvalidInput` for a
lookup that succeeds with no address, the error of the system `EAI_SYSTEM` passes on, the error of
the last address tried, and the string a connection's error names)*

A read, a write and `TcpStream.Connect` on a socket wait at most 5 seconds by default, as long as a
call of an actor waits (§6), and the time of a read and a write can be set for each socket, as
Rust's `set_read_timeout` and `set_write_timeout` set a time for each socket (§12, the
owner, 2026-10-30, against a timeout as an argument with no default and against a default in the
HTTP server alone). `Connect` creates the socket such a setter would set, and another time reaches it
through a second form that takes the time, `TcpStream.Connect(address, timeout)`, as Rust's
`TcpStream::connect_timeout` takes it (§12, the owner, 2026-10-30, "Second form, as Rust", against a
setting `Connect` reads before it creates the socket and against the 5 seconds alone), where Rust's
takes one address and looks nothing up, and this form takes the name the first form takes, looks it up
and tries its addresses in turn; the type of the time stands open with the member that sets it, and
with them what a time of zero and a negative time do, and how the time meets the lookup (§14). The socket options `SO_RCVTIMEO` and
`SO_SNDTIMEO`, which Rust's setters set, bound a call on a blocking socket outside an actor alone, and how
the bound holds for an actor's socket, which runs without blocking, stands open (§14). *(assumed, §12:
the bound of `TcpStream.Connect` for the whole call, every address it tries among it, the lookup
aside)*
`l.Accept()` waits without a bound by default, as Rust's `TcpListener::accept` waits, and a bound
can be set on a listener (§12, the owner, 2026-10-30, "Accept waits, as Rust", whose option read "No
default bound on Accept. Read, write and Connect keep 5 s, and a bound can still be set on a
listener", against the default of 5 seconds on `Accept` too). What a call gives when its time runs out, the member that sets the
time and how the bound holds for an actor's socket stand open (§14).

Case: [system/tcp-echo.kz](../corpus/system/tcp-echo.kz)
```kurz
void | IoError Echo() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("hello".Bytes)
    mut buffer = Bytes()
    mut read = 1
    while buffer.Count < 5 && read > 0 {
        read = server.Read(mut buffer, 5 - buffer.Count)
    }

    print(string.FromUtf8(buffer) else {
        InvalidUtf8 => "not UTF-8"
    })
}

Echo() else {
    IoError e => print(e)
}
```

Case: [system/bind-name.kz](../corpus/system/bind-name.kz)
```kurz
void | IoError Named() {
    listener = TcpListener.Bind("localhost:0")
    print(listener.Port > 0)
    client = TcpStream.Connect("localhost:{listener.Port}")
    server = listener.Accept()
    client.Close()
    server.Close()
    print("connected")
}

Named() else {
    IoError e => print(e)
}
```

Case: [system/connect-refused.kz](../corpus/system/connect-refused.kz)
```kurz
void | IoError Refused() {
    listener = TcpListener.Bind("127.0.0.1:0")
    port = listener.Port
    listener.Close()
    match TcpStream.Connect("127.0.0.1:{port}") {
        TcpStream => print("connected")
        IoError e => {
            print(e.Kind)
            print(e.Code)
        }
    }
}

Refused() else {
    IoError e => print(e)
}
```

Case: [system/bad-address.kz](../corpus/system/bad-address.kz)
```kurz
match TcpListener.Bind("127.0.0.1") {
    TcpListener => print("bound")
    IoError e => print(e)
}

match TcpStream.Connect("127.0.0.1:65536") {
    TcpStream => print("connected")
    IoError e => {
        print(e)
        print(e.Code)
    }
}
```

### S10 (decided, §12) A call blocks its thread

A call on a file or a socket is a system call that blocks the thread that makes it. A program
without actors serves one connection at a time.

No case: a case prints what its calls return, and the same output stands for a call that blocks
and for one that switches; S9's cases `tcp-echo.kz` and `bind-name.kz` connect before they accept, which
the system completes without the accept.

### S11 (decided, §12) Not on Windows

In a build for Windows, a call of this chapter is `not supported`, and the runtime of Windows gives
none of these calls, and so are `args`, `Environment.Args()`, `Environment.Variable`, `timestamp.Now`
and `File.Modified` (S13 to S15; §12, assumed). A build for any other target whose runtime lacks them says `not supported` at the
call as well, as the record says of a call of the program that reaches a function a target's runtime
lacks (§2, §12, the owner, 2026-10-30).

No case: a case does not name its target (record, section 14).

### S12 (decided, §13) Grants

A package that calls the file functions of this chapter, `File` and `Directory`, needs `allow files`,
and one that calls its socket functions `allow network`. Every package on the path of calls from the
program's code to such a function needs the grant, so that no package borrows a grant through
another that has it (§13, the owner, 2026-10-30, "Every package on path"). A call without the grant
is the compile error `files-not-allowed` or `network-not-allowed` at the call. A package that reads a
variable of the environment needs `allow env` in the same way, and a read without it is `env-not-allowed`;
the command line and the clock need no grant (§13, the owner, 2026-10-30, "`allow env`, as Deno", the option that carried this sentence). For a package on the
path that calls no such function itself, the error stands at its call that leads on toward the
function, a line of its own code, and names the path, as `Report -> Store.Save -> File.Write`, one
error for each such line (§13, the owner, 2026-10-30, "At its call, naming the path"). The program
itself needs neither. A call through an interface on the path counts as a call of every implementer of
`std/` and of the packages, and a call of a function value as a call of every such function whose value is
taken, while an implementer or a function of the program's own code is no path that needs the grant: a
path from a package's call ends there, and what that code calls is a path from the program's code, so
that `FileLog.Write -> Store.Save -> File.Write`, with `FileLog` the program's own logger, needs the
grant of `Store` and not of the package that called the logger (§13, the owner, 2026-10-30, "Program's
own don't count"). Where several paths lead through one line, its one
error names the shortest, two of one length in the order of the source (§13, the owner, 2026-10-30, "The
shortest"). *(assumed, §13: `Directory` among the file functions; such a call counted where it never reaches
the function; of two paths of one length, the one that stands first where the two part, the call
written first in that function, or, at one call through an interface or a function value, the
implementer whose file comes first in the order of the paths and, in one file, whose line comes first)* Whether a `send` to an actor puts the sender's package on the path stands open (§14).

No case: a case is a program and no package, and the program needs no grant.

### S13 (decided, §2, §12) The command line

Top-level code sees `args`, a `List<string>` of the program's arguments without its name, and
`Environment.Args()` is the list with the program's name first (§12, the owner, 2026-10-30, "Both"). Only
the top-level statements see `args`: in a function it is `unknown-name`, as a top-level variable is
(V10), and a function may name a parameter or a variable of its own `args`, while a top-level `args` of
the program's own, `List<string> args = ["a"]`, is `redeclared` (§12, the owner, 2026-10-30, "Top-level
code only"). Whether `args = value` among the top-level statements assigns to the program's `args`, a
variable without `mut`, and is `assign-immutable` (V8), or declares one and is `redeclared`, stands open
(§14). A lambda written among the top-level statements sees `args`, as it sees a top-level variable it
captures (V10), so that within the top-level code `args` is a visible name, and a lambda's parameter or a
block's variable named `args` there is `redeclared` (V9). `args` is declared without `mut`, so that
`args.Add("x")` is `mut-required` (M5), and each call of `Environment.Args()` makes a new list.
*(assumed, §12: the lambda, a lambda's parameter and a block's variable, `args` without `mut`, the new
list)*
An argument that is not UTF-8 ends the process with the run-time error `invalid-argument` (§12, the
owner, 2026-10-30, "Refused, as Rust"): `Environment.Args()` raises it at its call, with its place and
its trace, and `args`, made before the first statement of a top-level code that names it, with no place
and no trace, as `random-failed`; no `catch` takes the raise (O6). A top-level code names `args` where
any of its statements does, a lambda's body and a branch that never runs among them. `args` checks the words it holds and
not the program's name, which it does not hold, as C#'s `args`, while `Environment.Args()` checks the
name too. The program's name is the first argument as the process was started, under `kurz run` and
`kurz exec` the path of the executable in the command's place, as `cargo run` gives its program the
path in `target`. `kurz run app.kz -- a b` gives the program the words after the first `--`, a further
`--` among them, as `kurz exec "<program text>" -- a b` does, and a word after the file or the text
without `--` is a command line the command cannot read (§2, the owner, 2026-10-30, "After `--`, as
cargo"). *(assumed, §12: the id, the place of the refusal of `args` and of `Environment.Args()`, what names
`args`, the words `args` checks, the name, under `kurz run` and `kurz exec` too)*

No case expects `invalid-argument`: a case gives its program no argument; `ProcessTests` runs a built
program with an argument that is not UTF-8.

Case: [system/arguments.kz](../corpus/system/arguments.kz)
```kurz
print(args)
print(args.Count)
print(Environment.Args().Count)
```

Case: [system/args-parameter.kz](../corpus/system/args-parameter.kz)
```kurz
int Count(List<string> args) {
    return args.Count
}

int Local() {
    List<string> args = ["a", "b"]
    return args.Count
}

print(Count(args))
print(Local())
```

Case: [system/args-in-function.kz](../corpus/system/args-in-function.kz)
```kurz
int Count() {
    return args.Count
}

print(Count())
```

Case: [system/args-redeclared.kz](../corpus/system/args-redeclared.kz)
```kurz
List<string> args = ["a"]
print(args.Count)
```

Case: [system/args-lambda-parameter.kz](../corpus/system/args-lambda-parameter.kz)
```kurz
f = (int args) => args + 1
print(f(1))
```

Case: [system/args-in-lambda.kz](../corpus/system/args-in-lambda.kz)
```kurz
(() => int) count = () => args.Count
print(count())
```

Case: [system/args-without-mut.kz](../corpus/system/args-without-mut.kz)
```kurz
args.Add("x")
print(args)
```

Case: [system/args-new-list.kz](../corpus/system/args-new-list.kz)
```kurz
mut all = Environment.Args()
all.Add("x")
print(all.Count)
print(Environment.Args().Count)
```

### S14 (decided, §12, §13) The environment

`Environment.Variable(name)` reads one variable of the process's environment, `string | EnvError`: a
variable that is not set is the kind `NotPresent`, and one whose value is not UTF-8 the kind
`NotUnicode` (§12, the owner, 2026-10-30, "An outcome, as Rust"). Every file sees `data EnvError(EnvKind
Kind, string Name)` and `enum EnvKind { NotPresent, NotUnicode }`, as it sees `IoError` (S1), and the
text of an `EnvError` (A3) is the words of its kind in lower case, a colon and the name,
`not present: HOME`; a name that is empty or holds `=` or a NUL byte is `NotPresent`. A file's own
declaration of `EnvError`, `EnvKind` or `Environment`, a type, a function, a variable or a parameter at
any level, is `redeclared` (V9), while a field takes the name, as H1 says of the names of hashing. A package that
reads a variable needs `allow env` (S12). *(assumed, §12: the name `Variable`, the type, its text and
the names that are `NotPresent`, a file's own `EnvError`, `EnvKind` and `Environment`)*

Case: [system/environment-missing.kz](../corpus/system/environment-missing.kz)
```kurz
string Read(string name) => Environment.Variable(name) else {
    EnvError e => "{e}"
}

match Environment.Variable("KURZ_CASE_NOT_SET") {
    string value => print(value)
    EnvError e => {
        print(e)
        print(e.Kind)
        print(e.Name)
    }
}
print(Read("A=B"))
```

Case: [system/environment-name-redeclared.kz](../corpus/system/environment-name-redeclared.kz)
```kurz
data EnvError(int Code)

print(1)
```

### S15 (decided, §12) The clock and the time of a file

`timestamp.Now` is the time of the wall clock, a `timestamp` (T24), read from
`clock_gettime(CLOCK_REALTIME)` on Linux, and `File.Modified(path)` is the time the file at `path` was
last written, `timestamp | IoError` (§12, the owner, 2026-10-30, "`Timestamp.Now`, as C#" and
"`timestamp.Now`"). The clock goes back where the system's clock is set back; a directory gives its
time; and a time the 64 bits of a `timestamp` do not hold is an `IoError` of the kind `InvalidData`.
*(assumed, §12: the clock set back, the directory and the time out of range)*

Case: [system/clock.kz](../corpus/system/clock.kz)
```kurz
timestamp first = timestamp.Now
timestamp second = timestamp.Now
duration between = second - first
print(first + between == second)
print(first + between + 1ns == second)
```

Case: [system/file-modified.kz](../corpus/system/file-modified.kz)
```kurz
void | IoError Times() {
    File.WriteAll("stamped.txt", "x".Bytes)
    timestamp written = File.Modified("stamped.txt")
    timestamp again = File.Modified("stamped.txt")
    print(again == written)
    File.Delete("stamped.txt")
    match File.Modified("no-such-file.txt") {
        timestamp t => print(t)
        IoError e => print(e)
    }
}

Times() else {
    IoError e => print(e)
}
```
