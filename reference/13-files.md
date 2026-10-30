# 13. Files and sockets

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
and `Other` for a number none of them has, which Rust gives `Uncategorized`. A path that holds a NUL byte is `InvalidInput`, a name of
a directory's entry that is not UTF-8 `InvalidData`, and a write of which the system takes no byte
`WriteZero`, each with the number 0, which every error that is not the system's has. A call that
names two paths names the first in its error. *(assumed, §12: what raises in such a call, the list, the
words, the number 0, the text without a path and the first path)*

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
it, so that no later call passes the system a number it may have given another file. A `File` has no text: `print(f)` is `no-text`. *(assumed, §12: the closed file's error,
the close that ignores its error, and no text)*

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
`..`, in the order the system gives them, `List<string> | IoError`; and `Directory.Delete(path)`
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
inside `[` and `]` with a scope id in digits after a `%` and a port of digits alone, as Rust's parse of
a socket address reads one, and any other host, `[::1]` before the port `+0` among them, is looked up
as it is written. An address that holds a NUL byte is `InvalidInput`, as a path is (S1). A listener
takes every interface where its address is a wildcard, `0.0.0.0`, `[::]`, which takes IPv4 too on
Linux's dual stack, on by default, or a name that resolves to either. A `TcpListener` and a `TcpStream` are handles as a `File` is (S5); a stream reads, writes
and closes as a file does (S3, S4, S5), and a listener accepts and closes. A listener's socket reuses
its address and passes `listen` a backlog of 128. An address without a `:`, a port that is not a
number from 0 to 65535 and a name that gives no address are `InvalidInput`, and a lookup that fails is
`NotFound`, each with the number 0, but for the error of the system a lookup names. The error of a
connection that `Accept` gave names the string its listener's `Bind` was given, `127.0.0.1:0` where
the system picked the port, and one that `Connect` made the string it was given. *(assumed, §12: the
reuse of the address, the 128, the type of `Port`, the grammar of the port and of a literal, the NUL
byte, the errors of an address and of a lookup, the error of the last address tried, and the string a
connection's error names; the kind of a failed lookup is open, record section 14)*

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
none of these calls. A build for a target other than Linux and Windows meets the question the record
keeps open (section 14), of a runtime that lacks a function the code outside its directory calls.

No case: a case does not name its target (record, section 14).

### S12 (decided, §13) Grants

A package that calls the file functions of this chapter, `File` and `Directory`, needs `allow files`,
and one that calls its socket functions `allow network`. A call without the grant is the compile
error `files-not-allowed` or `network-not-allowed` at the call. The program itself needs neither.
*(assumed, §13: `Directory` among the file functions, and the package whose code makes the call)*

No case: a case is a program and no package, and the program needs no grant.
