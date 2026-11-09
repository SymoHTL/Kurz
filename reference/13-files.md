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

A read goes through a buffer of 8 KiB that the handle keeps, a `File`'s and a `TcpStream`'s alike
(§12, the owner, 2026-11-02, "the stream keeps a read buffer", 8 KiB "as Rust's `BufReader`", and a
`File` too, against the stream alone). It reads as Rust's `BufReader::read` reads: the bytes the
buffer holds go first, at most `max` of them, with no system call; where it holds none, a `max` of
8 KiB or more is read straight into `into`, past the buffer, and a smaller one fills the buffer with
one read of the system and takes at most `max` of what that read gave (§12, the owner, 2026-11-03,
issue 39, comment 6605104107). A `max` of 0 takes none of the bytes the buffer holds and keeps them;
where the buffer holds none, a `max` of 0 goes straight, so that it reads nothing and waits for no
byte of the system, as it did before the buffer. A `max` of 0 enters the call as every `Read` does,
so that through a holder of a closed handle it gets S5's error, and otherwise takes the handle's lock
to look at the buffer, as every `Read` does (below). `Read`, a `File`'s `Lines()` (S16), and `Fill` and
`Consume` (S24) are the ways to the buffered bytes. The buffer is
made at the handle's first read that fills it, and every holder shares it (S5), under the handle's
lock: a fill holds the lock while its read waits, so that a read or a pull through another holder
waits behind it, and a read that goes straight lets it go before it waits. A read that fails, or
whose time runs out (S9), stores nothing in the buffer. A `Write` after buffered reads of one handle
would land where the program's reads ended, the position moved back over the bytes read ahead, as
C#'s `FileStream` moves it, which no handle reaches, since `File.Open` opens a file to read and
`File.Create` to write (S2) and a stream has no position; a second `File.Open` of a file has its own
descriptor, position and buffer. *(assumed, §12: the `max` of 0, the buffer made at the first fill,
every holder sharing it under the lock, the read that fails storing nothing, the write after
buffered reads and the second handle)*

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

Case: [system/read-buffer-fill.kz](../corpus/system/read-buffer-fill.kz)
```kurz
void | IoError Show() {
    mut small = Bytes()
    for i in 0..<3000 {
        small.Add(byte(i % 251))
    }

    // 3000 bytes read one at a time from a stream: a read of the system fills the buffer of 8 KiB with what the stream holds, and the
    // reads after it take their byte from the buffer, in order
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(small)
    mut got = Bytes()
    for i in 0..<3000 {
        server.Read(mut got, 1)
    }

    print("{got.Count} {got == small}")

    // the same from a file
    mut sent = Bytes()
    for i in 0..<20000 {
        sent.Add(byte(i % 251))
    }

    File.WriteAll("read-buffer-fill.txt", sent)
    f = File.Open("read-buffer-fill.txt")
    File.Delete("read-buffer-fill.txt")
    mut read = Bytes()
    for i in 0..<3000 {
        f.Read(mut read, 1)
    }

    print("{read.Count} {read == small}")
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-second.kz](../corpus/system/read-buffer-second.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a second `File.Open` of a file has its own descriptor, position and buffer: the first handle's read of one byte fills its buffer
// with the file's 6 bytes, and the second handle's first read still starts at the file's first byte; each handle's next read takes
// the byte after its own
void | IoError Show() {
    File.WriteAll("read-buffer-second.txt", "abcdef".Bytes)
    f = File.Open("read-buffer-second.txt")
    mut one = Bytes()
    f.Read(mut one, 1)
    h = File.Open("read-buffer-second.txt")
    File.Delete("read-buffer-second.txt")
    mut two = Bytes()
    h.Read(mut two, 1)
    print("{Text(one)} {Text(two)}")
    f.Read(mut one, 1)
    h.Read(mut two, 1)
    print("{Text(one)} {Text(two)}")
    f.Close()
    h.Close()
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-straight.kz](../corpus/system/read-buffer-straight.kz)
```kurz
void | IoError Show() {
    mut sent = Bytes()
    for i in 0..<20000 {
        sent.Add(byte(i % 251))
    }

    File.WriteAll("read-buffer-straight.txt", sent)
    g = File.Open("read-buffer-straight.txt")
    File.Delete("read-buffer-straight.txt")

    // a read of 8 KiB or more into an empty buffer goes straight to the list
    mut large = Bytes()
    print(g.Read(mut large, 10000))

    // a smaller one fills the buffer with 8192 bytes and takes one; a read of 8 KiB or more then takes the 8191 the buffer holds and
    // no more, with no system call, and the next one, with the buffer empty, goes straight to the list again
    one = g.Read(mut large, 1)
    rest = g.Read(mut large, 10000)
    print("{one} {rest}")
    print(g.Read(mut large, 10000))
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-limit.kz](../corpus/system/read-buffer-limit.kz)
```kurz
// a read of `first` bytes into the empty buffer of a file of 20000 bytes, then a read of 8 KiB or more: the counts and whether the
// bytes read are the file's first ones in order
void | IoError Reads(int first) {
    mut sent = Bytes()
    for i in 0..<20000 {
        sent.Add(byte(i % 251))
    }

    File.WriteAll("read-buffer-limit.txt", sent)
    f = File.Open("read-buffer-limit.txt")
    File.Delete("read-buffer-limit.txt")
    mut read = Bytes()
    a = f.Read(mut read, first)
    b = f.Read(mut read, 10000)
    mut same = true
    for i in 0..<read.Count {
        if read[i] != sent[i] {
            same = false
        }
    }

    print("{a} {b} {same}")
    f.Close()
}

// 8191, one byte short of the buffer, fills it with 8192 bytes and takes 8191, so that the next read takes the one byte the buffer
// holds and no more; 8192 goes straight to the list and leaves the buffer empty, so that the next read goes straight too, where a fill
// of 8192 would give the same bytes and leave the buffer as empty
Reads(8191) else {
    IoError e => print(e)
}

Reads(8192) else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-zero.kz](../corpus/system/read-buffer-zero.kz)
```kurz
void | IoError Show() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    server.SetReadTimeout(50ms)
    mut got = Bytes()

    // a `max` of 0 reads nothing and waits for no byte, where a read that waited would run out of time
    print(server.Read(mut got, 0))

    // and it reads nothing where the stream holds bytes, which the next read takes
    client.Write("ab".Bytes)
    print(server.Read(mut got, 0))
    print(server.Read(mut got, 2))
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-zero-held.kz](../corpus/system/read-buffer-zero-held.kz)
```kurz
void | IoError Show() {
    mut sent = Bytes()
    for i in 0..<20000 {
        sent.Add(byte(i % 251))
    }

    File.WriteAll("read-buffer-zero-held.txt", sent)
    f = File.Open("read-buffer-zero-held.txt")
    File.Delete("read-buffer-zero-held.txt")

    // a read of one byte fills the buffer with 8192 bytes and takes one; a `max` of 0 then takes none of the 8191 the buffer holds and
    // keeps them, so that the next read of 8 KiB or more takes all of them, the file's first 8192 bytes in order
    mut read = Bytes()
    one = f.Read(mut read, 1)
    zero = f.Read(mut read, 0)
    rest = f.Read(mut read, 10000)
    mut same = read.Count == 8192
    for i in 0..<read.Count {
        if read[i] != sent[i] {
            same = false
        }
    }

    print("{one} {zero} {rest} {same}")
    f.Close()
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-zero-closed.kz](../corpus/system/read-buffer-zero-closed.kz)
```kurz
// a `Close` through one holder after a read of one byte filled the buffer with the file's 3 bytes: through the other holder, a `max`
// of 0 enters the call as every `Read` does, and gets the closed handle's error, not the 0 of a read that looks at no buffer; the list
// keeps the one byte read before
void | IoError Show() {
    File.WriteAll("read-buffer-zero-closed.txt", "abc".Bytes)
    f = File.Open("read-buffer-zero-closed.txt")
    File.Delete("read-buffer-zero-closed.txt")
    g = f
    mut read = Bytes()
    print(f.Read(mut read, 1))
    f.Close()
    match g.Read(mut read, 0) {
        int n => print(n)
        IoError e => print(e)
    }

    print(read.Count)
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-failed.kz](../corpus/system/read-buffer-failed.kz)
```kurz
void | IoError Show() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    server.SetReadTimeout(50ms)
    mut got = Bytes()

    // a read that would fill the buffer, and one that goes straight to the list, each run out of time with nothing sent: each stores
    // nothing and lets the next read in
    match server.Read(mut got, 1) {
        int n => print(n)
        IoError e => print("{e.Kind} {e.Code}")
    }

    match server.Read(mut got, 10000) {
        int n => print(n)
        IoError e => print("{e.Kind} {e.Code}")
    }

    client.Write("x".Bytes)
    n = server.Read(mut got, 1)
    print("{n} {got[0]}")
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-close.kz](../corpus/system/read-buffer-close.kz)
```kurz
void | IoError Show() {
    // a read of one byte fills the file's buffer with the 20 bytes the file holds; its close drops the 19 no read took, and a read
    // after it is S5's error and takes none of them
    File.WriteAll("read-buffer-close.txt", "abcdefghijklmnopqrst".Bytes)
    f = File.Open("read-buffer-close.txt")
    File.Delete("read-buffer-close.txt")
    mut read = Bytes()
    print(f.Read(mut read, 1))
    f.Close()
    match f.Read(mut read, 1) {
        int n => print(n)
        IoError e => print("{e.Kind} {e.Code}")
    }
}

Show() else {
    IoError e => print(e)
}
```

### S24 (decided, §12) Looking into the read buffer

`s.Fill(mut into)` appends to `into`, a `Bytes`, what the read buffer of a `File` or a `TcpStream`
holds (S3), all of it, and consumes none of it, as Rust's `BufRead::fill_buf` gives the buffer: where
the buffer holds bytes it makes no system call, and where it holds none it first fills it with one
read of the system, as a `Read` below 8 KiB fills it. `s.Consume(n)` drops the first `n` of the bytes
the buffer holds, as `BufRead::consume` does, so that the next `Read` or `Fill` starts after them. A
reader so looks at a message's head without taking what follows it: it fills,
finds the head's end in the bytes it was given, and consumes that far (§12, the owner, 2026-11-03,
issue 110, comment 6615078257, against the same two members visible to `std/` alone and against a
`Read` of one byte at a time; the cost is two public members, and a program can misuse `Consume`).
Each `Fill` appends a copy of what the buffer holds, up to 8 KiB, to `into`, where Rust's `fill_buf`
lends a slice of the buffer, so that a `Fill` of a buffer that still holds bytes copies them again
and `into` grows with each call.

`Consume` returns `void | IoError` and is a call in flight, as every call on a handle is (S5), so that
through a holder of a closed handle it gets the closed handle's error; an `n` past the bytes the
buffer holds, or below 0, raises `index-out-of-range`, as an index past a list's end does, the bound
Rust's documentation of `consume` states, enforced as C#'s `PipeReader.AdvanceTo` enforces it (§12,
the owner, 2026-11-03, issue 110, comment 6615355264, against such an `n` clamped, as Rust's
`BufReader` clamps it, and against a plain `void` that does nothing on a closed handle). An `n` below
0 raises before the call enters the handle, as a `Read`'s `max` below 0 does, so that it raises
through a holder of a closed handle too, and an `n` past the bytes held is found under the handle's
lock, after the call entered, so that through a holder of a closed handle it gets the closed handle's
error, as C#'s `FileStream.Read` checks its arguments before it finds the stream closed (§12, the
owner, 2026-11-04, issue 39, comment 6643376941, Q2 of issue 156, comment 6621376360, against the
closed handle first and against the order left unwritten).

`Fill` returns the count it appended, `int | IoError`, 0 at the end of a file or of a connection its
peer closed, as a `Read` does, and `into` grows as `Add` grows a list (M9). A pull of a line (S16)
starts after the bytes a `Consume` dropped, as the next `Read` does. A `Fill` is a call in
flight too, and one that fills is a read as a `Read`'s fill is: it holds the handle's lock while its
read waits, its time runs out as a read's does (S9), a read that fails stores nothing, and one that
finds the handle marked by a `Close` when its read returns gives `into` all it read and keeps none of
it, having no `max`, where a `Read`'s fill gives at most `max` of it; a stream's `Fill` whose read
returned with no byte after the mark gets the closed handle's error, as a stream's `Read` does (S5).
*(assumed, §12: `Fill`'s count and its 0 at the end, `into` growing as `Add` grows a list, a pull
of a line starting after the bytes consumed, `Fill` as a call in flight whose fill
holds the lock, runs out of time, fails and finds the mark as a `Read`'s fill does, giving all it read
where a `Read`'s fill gives at most `max`)*

Case: [system/fill-consume.kz](../corpus/system/fill-consume.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a file's bytes looked at and consumed: a `Fill` appends what the buffer holds and consumes none of it, so that a second gives the same
// bytes again; a `Consume` drops bytes from the front, and the `Read` and the `Fill` after it start past them; at the end of the file a
// `Fill` appends nothing and gives 0
void | IoError Look() {
    File.WriteAll("fill-consume.txt", "abcdef".Bytes)
    f = File.Open("fill-consume.txt")
    File.Delete("fill-consume.txt")
    mut seen = Bytes()
    first = f.Fill(mut seen)
    second = f.Fill(mut seen)
    print("{first} {second} {seen.Count}")
    print(Text(seen))
    f.Consume(2)
    mut rest = Bytes()
    read = f.Read(mut rest, 2)
    print("{read} {Text(rest)}")
    rest.Clear()
    filled = f.Fill(mut rest)
    print("{filled} {Text(rest)}")
    f.Consume(filled)
    print(f.Fill(mut rest))
    f.Close()
}

Look() else {
    IoError e => print(e)
}
```

Case: [system/fill-head.kz](../corpus/system/fill-head.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// the index after the first blank line, "\r\n\r\n", in `bytes`, or -1 where it has none
int HeadEnd(Bytes bytes) {
    for i in 3..<bytes.Count {
        if bytes[i - 3] == byte(13) && bytes[i - 2] == byte(10) && bytes[i - 1] == byte(13) && bytes[i] == byte(10) {
            return i + 1
        }
    }

    return -1
}

// a request's head read from a stream without taking what follows it: each fill appends what the buffer holds, the bytes before the
// blank line are consumed, and the body after it is left to the reads that follow
void | IoError Serve() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("GET / HTTP/1.1\r\nHost: a\r\n\r\nhello".Bytes)
    client.ShutdownWrite()
    mut head = Bytes()
    mut end = -1
    while end < 0 {
        before = head.Count
        got = server.Fill(mut head)
        if got == 0 {
            print("no head")
            return
        }

        end = HeadEnd(head)
        if end < 0 {
            server.Consume(got)
        } else {
            server.Consume(end - before)
        }
    }

    print("head {end}")
    mut body = Bytes()
    mut more = 1
    while more > 0 {
        more = server.Read(mut body, 100)
    }

    print(Text(body))
    print(server.Fill(mut body))
    server.Close()
    client.Close()
}

Serve() else {
    IoError e => print(e)
}
```

Case: [system/fill-held.kz](../corpus/system/fill-held.kz)
```kurz
// the read system calls of the process so far, the line `syscr` of the system's `/proc/self/io`, and -1 where it has none
long | IoError Reads() {
    bytes = File.ReadAll("/proc/self/io")
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => ""
    }

    for line in text.Split('\n') {
        if line.StartsWith("syscr:") {
            return long.Parse(line.Replace("syscr:", "")) else {
                ParseError => -1
            }
        }
    }

    return -1
}

// 1000 fills of a buffer that holds a file's 6 bytes make no read of the system, fewer than 100 reads between the counts, which read
// `/proc/self/io` themselves; once they are consumed, each of 1000 fills at the end of the file reads, 1000 reads or more
void | IoError Show() {
    File.WriteAll("fill-held.txt", "abcdef".Bytes)
    f = File.Open("fill-held.txt")
    File.Delete("fill-held.txt")
    mut seen = Bytes()
    f.Fill(mut seen)
    before = Reads()
    for i in 0..<1000 {
        seen.Clear()
        f.Fill(mut seen)
    }

    after = Reads()
    print("{seen.Count} {before > 0 && after - before < 100}")
    f.Consume(6)
    start = Reads()
    mut given = 0
    for i in 0..<1000 {
        given = given + f.Fill(mut seen)
    }

    end = Reads()
    print("{given} {start > 0 && end - start >= 1000}")
    f.Close()
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/fill-timeout.kz](../corpus/system/fill-timeout.kz)
```kurz
// a fill that waits on a stream with nothing sent runs out of time as a read does, stores nothing, and lets the next fill in
void | IoError Show() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    server.SetReadTimeout(50ms)
    mut got = Bytes()
    match server.Fill(mut got) {
        int n => print(n)
        IoError e => print("{e.Kind} {e.Code}")
    }

    client.Write("x".Bytes)
    n = server.Fill(mut got)
    print("{n} {got[0]}")
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/consume-past.kz](../corpus/system/consume-past.kz)
```kurz
// the buffer holds the file's 3 bytes: consuming all of them passes, and one more is past what it holds
void | IoError Past() {
    File.WriteAll("consume-past.txt", "abc".Bytes)
    f = File.Open("consume-past.txt")
    File.Delete("consume-past.txt")
    mut seen = Bytes()
    f.Fill(mut seen)
    f.Consume(3)
    f.Consume(1)
}

Past() else {
    IoError e => print(e)
}
```

Case: [system/consume-negative.kz](../corpus/system/consume-negative.kz)
```kurz
void | IoError Negative() {
    File.WriteAll("consume-negative.txt", "abc".Bytes)
    f = File.Open("consume-negative.txt")
    File.Delete("consume-negative.txt")
    int n = -1
    f.Consume(n)
}

Negative() else {
    IoError e => print(e)
}
```

Case: [system/consume-closed.kz](../corpus/system/consume-closed.kz)
```kurz
// a `Close` through one holder while the buffer holds the file's 3 bytes: through the other holder, a `Consume` of 5, past the bytes
// held, gets the closed handle's error, which the call finds as it enters, before it reads what the buffer holds
void | IoError Closed() {
    File.WriteAll("consume-closed.txt", "abc".Bytes)
    f = File.Open("consume-closed.txt")
    File.Delete("consume-closed.txt")
    g = f
    mut seen = Bytes()
    f.Fill(mut seen)
    f.Close()
    g.Consume(5) else {
        IoError e => print(e)
    }

    print(seen.Count)
}

Closed() else {
    IoError e => print(e)
}
```

Case: [system/consume-closed-negative.kz](../corpus/system/consume-closed-negative.kz)
```kurz
// a `Close` through one holder while the buffer holds the file's 3 bytes: through the other holder, a `Consume` of 0 gets the closed
// handle's error, as it enters the call, and a `Consume` of -1 raises `index-out-of-range` before the call enters, closed handle or not
void | IoError Closed() {
    File.WriteAll("consume-closed-negative.txt", "abc".Bytes)
    f = File.Open("consume-closed-negative.txt")
    File.Delete("consume-closed-negative.txt")
    g = f
    mut seen = Bytes()
    f.Fill(mut seen)
    f.Close()
    g.Consume(0) else {
        IoError e => print(e)
    }

    g.Consume(-1) else {
        IoError e => print(e)
    }
}

Closed() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-close-stream.kz](../corpus/system/read-buffer-close-stream.kz)
```kurz
// a stream's close drops the bytes its buffer holds, and the peer reads the end of the stream, where bytes left in the system's queue
// would have reset it: the server reads one byte, then fills and consumes until all 10 of the client's bytes were read from the system,
// whatever each read of the system gave, so that the queue is empty and the buffer holds the last of them when the server closes
void | IoError Show() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("abcdefghij".Bytes)
    mut got = Bytes()
    mut taken = server.Read(mut got, 1)
    mut look = Bytes()
    mut held = server.Fill(mut look)
    while taken + held < 10 {
        server.Consume(held)
        taken = taken + held
        look.Clear()
        held = server.Fill(mut look)
    }

    print(taken + held)
    server.Close()
    mut last = Bytes()
    print(client.Read(mut last, 16))
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/fill-closed.kz](../corpus/system/fill-closed.kz)
```kurz
// a `Close` through one holder while the buffer holds the file's bytes: a `Fill` and a `Consume` through the other holder find the mark
// before they reach the buffer, and get the closed handle's error
void | IoError Closed() {
    File.WriteAll("fill-closed.txt", "abc".Bytes)
    f = File.Open("fill-closed.txt")
    File.Delete("fill-closed.txt")
    g = f
    mut seen = Bytes()
    f.Fill(mut seen)
    f.Close()
    match g.Fill(mut seen) {
        int n => print(n)
        IoError e => print(e)
    }

    g.Consume(0) else {
        IoError e => print(e)
    }
}

Closed() else {
    IoError e => print(e)
}
```

### S27 (decided, §12) Moving the position

`f.Seek(offset)` moves the position of a `File` or a `TcpStream` to `offset` bytes from the start of
the file and gives `void | IoError`, as Rust's `Seek::seek(SeekFrom::Start(n))` through a
`BufReader` and C#'s `FileStream.Position = n`. The bytes the read buffer holds (S3, S24) are
dropped, as `BufReader::seek` discards its buffer, so that the next `Read`, `Fill` or pull of a line
reads at the new position. A handle the system cannot seek, a socket or a pipe, gives the `IoError`
of the kind `NotSeekable` (S1) (§12, the owner, 2026-11-08, #110, comment 6721051459, G5 (a) of
comment 6717319917, against a part of a file as a response and against a read at an offset that
moves no position; the cost is the system call and this rule, and a position every holder of the
handle shares, as it shares the buffer, S5). *(assumed, §12, the defaults of the product pass of G5
(#110, comment 6728933408): `offset` a `long`, as `File.Size` gives one (S7) and C#'s `Position` is
one; an offset past the
end allowed, a read there giving no bytes and a write there extending the file with bytes of 0 before
it, as C# and Rust allow it; a file `File.Create` made seeking too, since a write goes to the system
with no buffer of its own (S4); the seek a call in flight on the handle (S5) that takes the buffer's
lock, so that a fill through another holder puts no bytes of the old position after it; a failed
seek keeping the buffer, as `BufReader::seek` keeps it where the inner seek fails; and in a build for
Windows `not supported`, as every call on a file, S11)* An `offset` below 0 stops the program with
`index-out-of-range` before the call enters the handle, as a `Read`'s `max` below 0 does (§12, the
owner, 2026-11-09, #110, comment 6735258248, G5 Q3, "index-out-of-range", against the system's
`EINVAL` as an `IoError` of the kind `InvalidInput`).

Case: [system/seek-read.kz](../corpus/system/seek-read.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a read of three bytes leaves the rest of the file in the handle's buffer: a seek drops it, so that the read after it starts at the
// offset, before those bytes or past them, and a read past the end of the file gives no bytes
void | IoError Run() {
    File.WriteAll("seek-read.txt", "0123456789".Bytes)
    f = File.Open("seek-read.txt")
    File.Delete("seek-read.txt")
    mut first = Bytes()
    f.Read(mut first, 3)
    print(Text(first))
    f.Seek(1)
    mut back = Bytes()
    f.Read(mut back, 3)
    print(Text(back))
    long tail = 10 - 3
    f.Seek(tail)
    mut ahead = Bytes()
    f.Read(mut ahead, 3)
    print(Text(ahead))
    f.Seek(100)
    mut past = Bytes()
    n = f.Read(mut past, 3)
    print(n)
    f.Seek(0)
    mut whole = Bytes()
    f.Read(mut whole, 100)
    print(Text(whole))
}

Run() else {
    IoError e => print(e)
}
```

Case: [system/seek-fill.kz](../corpus/system/seek-fill.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a fill after a seek holds the bytes at the offset, not the ones the buffer held, and a seek after a consume starts at its offset,
// not where the consume left the buffer
void | IoError Run() {
    File.WriteAll("seek-fill.txt", "abcdef".Bytes)
    f = File.Open("seek-fill.txt")
    File.Delete("seek-fill.txt")
    mut all = Bytes()
    f.Fill(mut all)
    print(Text(all))
    f.Seek(3)
    mut rest = Bytes()
    f.Fill(mut rest)
    print(Text(rest))
    f.Consume(1)
    f.Seek(5)
    mut last = Bytes()
    f.Fill(mut last)
    print(Text(last))
}

Run() else {
    IoError e => print(e)
}
```

Case: [system/seek-write.kz](../corpus/system/seek-write.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a file the program creates seeks too: a write after a seek lands at the offset, one past the end extends the file with bytes of 0
// before it, and a seek after a `Close` gives the closed handle's error
void | IoError Run() {
    f = File.Create("seek-write.txt")
    f.Write("abcdef".Bytes)
    f.Seek(2)
    f.Write("XY".Bytes)
    f.Seek(8)
    f.Write("Z".Bytes)
    f.Close()
    f.Seek(0) else {
        IoError e => print(e)
    }

    bytes = File.ReadAll("seek-write.txt")
    File.Delete("seek-write.txt")
    mut head = Bytes()
    for i in 0..<6 {
        head.Add(bytes[i])
    }

    print(Text(head))
    print(bytes.Count)
    print(bytes[6])
    print(bytes[8])
}

Run() else {
    IoError e => print(e)
}
```

Case: [system/seek-stream.kz](../corpus/system/seek-stream.kz)
```kurz
string Text(Bytes bytes) {
    text = string.FromUtf8(bytes) else {
        InvalidUtf8 => "?"
    }

    return text
}

// a stream cannot seek: the system's `ESPIPE` is `NotSeekable`, and the failed seek keeps the bytes the buffer holds for the read
// after it
void | IoError Run() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("hello".Bytes)
    client.ShutdownWrite()
    mut seen = Bytes()
    server.Fill(mut seen)
    server.Seek(0) else {
        IoError e => {
            print(e.Kind)
            print(e.Code)
        }
    }

    mut read = Bytes()
    server.Read(mut read, 100)
    print(Text(read))
}

Run() else {
    IoError e => print(e)
}
```

Case: [system/seek-negative.kz](../corpus/system/seek-negative.kz)
```kurz
void | IoError Negative() {
    File.WriteAll("seek-negative.txt", "abc".Bytes)
    f = File.Open("seek-negative.txt")
    File.Delete("seek-negative.txt")
    long offset = -1
    f.Seek(offset)
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
`Close` through one holder ends the handle for every holder at once: it marks the handle closed, its
mark, so that every later call through any holder, `Close` among them, returns the error the system
gives a descriptor that is not open, `Other` with the number 9, with the path the file was opened
with (§12, the owner, 2026-11-02, issue 110, comment 6573974441, and the error the answer of comment
6574625739 names, "the one error every call on a closed handle gets"), and the descriptor itself is
released only when the last call in flight on the handle returns, as Rust's `Arc<File>` closes the
file with its last clone, so that no call reaches a number the system has given another file since: a
call already in flight when the `Close` runs keeps the descriptor and completes, and the `Close`
succeeds at once, an error of the close the last call makes dropped, as the close at the last count
drops it (§12, the owner, 2026-11-02, against a lock around each call, against keeping the hazard and
against a new error for a deferred close; the cost is a count kept around each call on a handle, two
atomic operations, and a `Close` that shuts a socket down holds a count of its own across the
shutdown, so that the release comes after it (S9; the owner, 2026-11-02, on a review's finding)). A
call in flight is a `Read`, a `Write`, a stream's `ShutdownWrite`, a listener's `Accept`, a setter of
a bound (S9), a `Fill` or a `Consume` (S24) or a pull of a loop over the lines (S16), each from after
its arguments have run to its return (§12, the owner, 2026-11-02, issue 110, comment 6588037045;
`ShutdownWrite`'s, issue 39, comment 6583351876; `Consume`'s, issue 110, comment 6615355264; `Fill`'s
assumed, S24); a loop over the lines is in flight while it pulls a line, and not while its block runs,
so that a `Close` in its block ends the handle and the next pull gets the error above, as a borrow
of `&mut self` in Rust lasts one call, `read_line`'s, while Rust's loop over `lines()` holds its
reader for the whole loop and refuses a close in its block when it compiles (the same comment,
against a count held for the loop's whole
block, at the cost of a `Close` in that block whose release would wait for its own loop). The bytes
the file's buffer holds that no read took (S3) are dropped at the close, as dropping Rust's
`BufReader` gives them up (§12, the owner, 2026-11-02): a read that fills the buffer is a call in
flight, and one that finds the mark when its read returns gives what it took, at most `max` of what
that read gave, as S3's fill takes it, and keeps none of the rest in the buffer, a stream's fill whose
read returned with no byte after the mark getting the closed handle's error, as every read the
`Close`'s shutdown wakes gets it (S9), while every later call through any holder finds the mark before
it reaches the buffer (§12, the owner, 2026-11-03). A `File` has no text: `print(f)` is `no-text`. *(assumed, §12: the close at the
last count that ignores its error, no text, and the bound between the two halves of the answer of
2026-11-03: a stream's fill whose read returned with no byte after the mark gets the closed handle's
error, not a 0, and one whose read gave bytes gives what it took)*

No case: a case runs one thread, and a call in flight beside a `Close` needs two.

Case: [system/read-buffer-close.kz](../corpus/system/read-buffer-close.kz)

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

Case: [system/setter-after-close.kz](../corpus/system/setter-after-close.kz)
```kurz
// a stream's setter and a listener's on a handle closed before get the system's error of a descriptor that is not open, as every
// call after `Close` does
void | IoError Closed() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    client.Close()
    client.SetReadTimeout(1s) else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    listener.Close()
    listener.SetAcceptTimeout(1s) else {
        IoError e => print("{e.Kind} {e.Code}")
    }
}

Closed() else {
    IoError e => print(e)
}
```

Case: [system/fill-closed.kz](../corpus/system/fill-closed.kz)

Case: [system/consume-closed.kz](../corpus/system/consume-closed.kz)

Case: [system/consume-closed-negative.kz](../corpus/system/consume-closed-negative.kz)

Case: [system/read-buffer-zero-closed.kz](../corpus/system/read-buffer-zero-closed.kz)

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
and closes as a file does (S3, S4, S5), and a listener accepts and closes. The bytes a stream's
buffer holds that no read took are dropped at its close, and only the bytes still in the system's
queue make the close send the peer a reset (§12, the owner, 2026-11-02). A `Close` of a stream
or a listener also shuts the socket down, `shutdown(2)`, so that a call that blocks on it in another
thread returns at once, as Go's close wakes it, a blocked `Accept` or `Read` with the error of a closed
handle, S5's, and a listener's port is free right away (§12, the owner, 2026-11-02, against a call that
waits for its bound). The shutdown runs only while calls are in flight, a `Close` with none releasing
the descriptor at once as before (against a shutdown at every `Close`); a blocked `Write` that the
shutdown wakes gets that error too, as Go's close gives a blocked write `net.ErrClosed` (against the
system's broken pipe or reset); a `Read` in flight that the shutdown meets with bytes the socket holds
takes them, and the next call gets that error (against failing the call and dropping the bytes); and a
stream's `Read` or `Write` or a listener's `Accept` that fails, or reads no byte, after the mark (S5)
gives that error, a real timeout or reset that races the `Close` among them (against the system's
answer, which would depend on the race) (§12, the owner, 2026-11-02). An `Accept` in flight whose
system call succeeds after the mark returns the stream it got, as a `Read` keeps the bytes it took,
and the next `Accept` gets that error, so that the connection it got is not thrown away, as Go's
`Accept` returns a connection whose system call finished before the close (§12, the owner,
2026-11-02, issue 39, comment 6578783655, against that error with the connection closed, at the cost
of a connection the system accepted and its client sees closed; the cost is a stream that reaches
the program after the listener's `Close`, which the program closes, or lets go of, as any other; the costs,
issue 110, comment 6588037045); a connection still waiting in the listener's queue is reset at the
listener's `Close`, by the release where no call is in flight and by the shutdown where one is, as
Linux resets the queue of a listener that stops listening: the system's behaviour, not a choice of
§12, which names it as a cost of a listener's `Close`; and a `Write` that the shutdown
ends after part of its bytes went out gives that error, the part sent not reported, as Rust's
`write_all` and C#'s `Stream.Write` report none (§12, the owner, 2026-11-02, the same comment, against
the count sent with the error left to the next call, at the cost of a `Write` that returns a count,
where a `Write` writes every byte and returns none (S4); the cost is that the program cannot tell
how many of its bytes went out before the shutdown; the costs, issue 110, comment 6588037045). An actor waiting in the poller on a
socket that another holder closes is woken at the close with that error, as Go's `net.Conn.Close`
wakes the reads and writes that wait (§12, the owner, 2026-11-02, against leaving it waiting until its
bound and against a kind of its own, as Go's `net.ErrClosed`; the cost is one wake-up for each caller
that waits); the poller is the one under `epoll` that §12 decides for an actor's socket, and a call
of a program without actors blocks its thread (S10) and waits in no poller, the shutdown waking it.
A listener's socket reuses
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

A read, a write and the connect of `TcpStream.Connect` on a socket wait at most 5 seconds by default,
as long as a call of an actor waits (§6), the bound of a read and a write holding for each call of the
system and that of `Connect` starting after the lookup of a name (below), and the time of a read and a write can be set for each socket, as
Rust's `set_read_timeout` and `set_write_timeout` set a time for each socket (§12, the
owner, 2026-10-30, against a timeout as an argument with no default and against a default in the
HTTP server alone). `Connect` creates the socket such a setter would set, and another time reaches it
through a second form that takes the time, `TcpStream.Connect(address, timeout)`, as Rust's
`TcpStream::connect_timeout` takes it (§12, the owner, 2026-10-30, "Second form, as Rust", against a
setting `Connect` reads before it creates the socket and against the 5 seconds alone), where Rust's
takes one address and looks nothing up, and this form is read as taking the name the first form takes,
looking it up and trying its addresses in turn, as the first form's `address` reads above (§12, the
owner, 2026-10-29, "Names too, as Rust", an answer on the first form), a reading under the marker on
the one bound of `TcpStream.Connect` below. A call whose time runs out
gives an `IoError` of the kind `TimedOut`, and `s.SetReadTimeout(d)` and `s.SetWriteTimeout(d)` set
the time of a stream's reads and of its writes, taking a `duration?`, `null` for no bound, as Rust's
`set_read_timeout(Option<Duration>)`, with a time of zero and a negative time refused as
`InvalidInput`, as Rust refuses a zero `Duration` (§12, the owner, 2026-11-01, Q8 (a), against
`WouldBlock`, which Rust gives on Linux for the same timeout, and against properties, as C#'s
`Socket.ReceiveTimeout`). A time of zero or below in the second form of `Connect` is `InvalidInput`
too, as Rust's `connect_timeout` refuses a zero `Duration` (§12, the owner, 2026-11-01, "Same:
InvalidInput", against the 5 seconds of the first form and against no bound). A time is cut down to
whole microseconds, and a positive time that would come to zero is raised to 1 microsecond, as Rust's
setters cut and raise it, and a time whose seconds would pass the largest `time_t`, which Rust bounds
by `time_t::MAX` seconds, cannot be written (checked 2026-11-01: `set_timeout` in
`library/std/src/sys/net/connection/socket/unix.rs` of Rust's master branch; §12, the owner,
2026-11-02, the socket pass's Q7 and Q8 at their defaults). *(assumed, §12: the parameter a
`duration?`; the time of the second form of `Connect` a `duration`; a setter giving
`void | IoError`; and every `EAGAIN` other than the one Q8 (a) names keeping `WouldBlock`, that one
being the only exception to the kinds of Rust's mapping, told by what the runtime sees: socket(7)
says that `SO_RCVTIMEO` and `SO_SNDTIMEO` make a call on a blocking socket fail with `EAGAIN` when
their time runs out, or with `EINPROGRESS` for `connect`, so that every `EAGAIN` of a read, a write
or an `Accept` on a blocking socket whose time is set is that case (checked 2026-11-01: socket(7) of
the Linux man-pages))* A read or a write whose time runs out gives `TimedOut` with the number of
`EAGAIN`, 11, and a `Connect` whose bound runs out `TimedOut` with the number 0, as an error that is
not the system's, while a connect the kernel times out keeps `ETIMEDOUT`, 110 (§12, the owner,
2026-11-02, "11 (EAGAIN) for a read or a write, 0 for Connect's own bound", against 110 for both and
against 0 for a read and a write too). The bound of a read and a write holds for each call of the
system, so that a `Write` of many bytes to a peer that takes them slowly can take longer than the
bound in all, as Rust's `write_all` does, and a peer that stops reading gives `TimedOut` after the
bound (§12, the owner, 2026-11-02, "per system call", against one deadline for the whole `Write`, as
Go's `SetWriteDeadline`). The bound of a read holds for the read of the system alone: a call that
waits for the handle's lock behind another holder's fill (S3, S24) waits with no bound, as Rust's
`Mutex::lock` waits while `set_read_timeout` bounds the `recv` (§12, the owner, 2026-11-04, issue 39,
comment 6643376941, Q1 of issue 156, comment 6621376360, against a bound counted from the call's
start with the wait included, and against a second call in flight refused; the cost is that a holder
whose fill waits with no bound keeps every other holder waiting as long). The socket options `SO_RCVTIMEO` and
`SO_SNDTIMEO`, which Rust's setters set, bound a call on a blocking socket outside an actor alone, and how
the bound holds for an actor's socket, which runs without blocking, stands open (§14). The bound of
`TcpStream.Connect` starts once the lookup of the name has given its addresses, the lookup waiting
without a bound of its own, as Rust's `connect` leaves `getaddrinfo`, and is one bound shared by its
attempts to connect to the addresses it tries in turn, not given to each (§12, the owner, 2026-11-02,
"one bound of 5 s after the lookup, shared by the addresses tried in turn", against a bound that
covers the lookup too, as Go's `DialTimeout`, against the bound spread over the addresses, as Go
spreads it, and against a bound for each address), at the cost of a call that waits past its time on
a slow name server, and of a first address that never answers, which spends the whole bound while
the later addresses are not tried. *(assumed, §12: the second form's lookup and its trial of the
addresses, a reading of the answer's `Connect(address, timeout)`, which names the first form's
parameter)*
`l.Accept()` waits without a bound by default, as Rust's `TcpListener::accept` waits, and a bound
can be set on a listener (§12, the owner, 2026-10-30, "Accept waits, as Rust", whose option read "No
default bound on Accept. Read, write and Connect keep 5 s, and a bound can still be set on a
listener", against the default of 5 seconds on `Accept` too). A listener's bound is set by
`l.SetAcceptTimeout(d)`, taking a `duration?` as the stream's setters do, and giving `void | IoError`,
refusing a time of zero or below as `InvalidInput` and cutting a time down to whole microseconds as
they do *(assumed, §12: following
from Q8 (a), which set a property, as C#'s `Socket.ReceiveTimeout`, aside for a socket)*, and how
the bound holds for an actor's socket stands open (§14).

`s.ShutdownWrite()` ends a stream's writes and keeps its reads, as Rust's
`TcpStream::shutdown(Shutdown::Write)` ends them: the peer's read then gives 0, the end of its stream
(S3), and the stream still reads what the peer sends until it closes. It gives `void | IoError`, S5's
error on a closed stream, and what the system answers as it is: a second call passes, and a call on a
stream whose peer reset the connection is `NotConnected` with the number 107. A call through one
holder ends the writes of every holder, so that a later write through any holder is `BrokenPipe` with
the number 32, and the reads go on. A listener has no such member, and there is no `ShutdownRead` and
no `Shutdown(how)` (§12, the owner, 2026-11-02, a half-close on `TcpStream` for the staged close of
the HTTP server, and the answers to the product pass of the call).

No case: a case runs one thread, and a call in flight beside a `Close` needs two; the runtime of
Linux has no poller (S10); and whether a `Close` with no call in flight shuts the socket down no
program can see, since the runtime shares no descriptor across processes.

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

Case: [system/read-timeout.kz](../corpus/system/read-timeout.kz)
```kurz
// a read whose time runs out is `TimedOut`, with the number the system gave, `EAGAIN`, after its time, which the system counts in
// its own ticks; the stream stays open, and the next read takes what the peer wrote. The time it took lies between the kernel's
// floor, the 50 ms it was given, and 4 s, a margin no rule fixes, set below the 5 s of the default, so that a setter that set
// nothing, whose read would run out at the default with the same `TimedOut 11`, prints `false`
void | IoError Wait() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.SetReadTimeout(50ms)
    mut buffer = Bytes()
    start = timestamp.Now
    match client.Read(mut buffer, 1) {
        int n => print(n)
        IoError e => print("{e.Kind} {e.Code}")
    }

    elapsed = timestamp.Now - start
    print(elapsed >= 40ms && elapsed < 4s)
    server.Write("x".Bytes)
    print(client.Read(mut buffer, 1))
}

Wait() else {
    IoError e => print(e)
}
```

Case: [system/write-timeout.kz](../corpus/system/write-timeout.kz)
```kurz
// a peer that never reads: the writes fill what the system holds for it, and the one that waits longer than its time is `TimedOut`.
// All of it takes less than 4 s, a margin no rule fixes, for the buffers the kernel sizes and the one wait of 50 ms, set below the 5 s
// of the default, so that a setter that set nothing, whose last write would wait the default, prints `false`
void | IoError Stall() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.SetWriteTimeout(50ms)
    piece = "0123456789abcdef".Bytes
    mut chunk = Bytes()
    while chunk.Count < 65536 {
        chunk.AddRange(piece)
    }

    start = timestamp.Now
    mut writing = true
    while writing {
        client.Write(chunk) else {
            IoError e => {
                print("{e.Kind} {e.Code}")
                writing = false
            }
        }
    }

    print(timestamp.Now - start < 4s)
    server.Close()
}

Stall() else {
    IoError e => print(e)
}
```

Case: [system/connect-timeout.kz](../corpus/system/connect-timeout.kz)
```kurz
// a listener that accepts nothing: once its queue of connections that wait is full, a connection waits, and the second form of
// `Connect` gives `TimedOut` after its time, with the number 0, since no error of the system came with it. The queue fills on
// Linux's defaults: the 128 a listener asks for, within `somaxconn`, and `tcp_abort_on_overflow` at 0, which drops a connection
// that finds the queue full where 1 would refuse it; a kernel that took every connection ends the loop at 1000 with no `TimedOut`
// line. All of it takes less than 4 s, a margin no rule fixes, set below the 5 s of the default, so that a bound that was not
// passed on prints `false`
void | IoError Full() {
    listener = TcpListener.Bind("127.0.0.1:0")
    mut held = List<TcpStream>()

    // the longest time a `duration` holds, about 292 years, is a bound too, which the queue's room meets at once
    held.Add(TcpStream.Connect("127.0.0.1:{listener.Port}", 9223372036854775807ns))
    mut timedOut = false
    start = timestamp.Now
    while !timedOut && held.Count < 1000 {
        match TcpStream.Connect("127.0.0.1:{listener.Port}", 100ms) {
            TcpStream s => held.Add(s)
            IoError e => {
                print("{e.Kind} {e.Code}")
                timedOut = true
            }
        }
    }

    print(held.Count > 1)
    print(timestamp.Now - start < 4s)
}

Full() else {
    IoError e => print(e)
}
```

Case: [system/accept-timeout.kz](../corpus/system/accept-timeout.kz)
```kurz
// an `Accept` whose time runs out is `TimedOut`, with the number the system gave, `EAGAIN`, after its time; the listener stays open,
// and the next `Accept` takes the connection that came. The time it took lies between the kernel's floor, the 50 ms it was given,
// and 4 s, a margin no rule fixes, set below the 5 s a stream's call waits by default, so that a setter that gave the listener that
// default in place of its time prints `false`; that `Accept` waits without a bound by default, and that the stream it gives gets the
// default, `FileTests` reads back by `getsockopt`
void | IoError Wait() {
    listener = TcpListener.Bind("127.0.0.1:0")
    listener.SetAcceptTimeout(50ms)
    start = timestamp.Now
    match listener.Accept() {
        TcpStream => print("accepted")
        IoError e => print("{e.Kind} {e.Code}")
    }

    elapsed = timestamp.Now - start
    print(elapsed >= 40ms && elapsed < 4s)
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("x".Bytes)
    mut buffer = Bytes()
    print(server.Read(mut buffer, 1))
}

Wait() else {
    IoError e => print(e)
}
```

Case: [system/timeout-refused.kz](../corpus/system/timeout-refused.kz)
```kurz
// a time of zero or below is refused, in a stream's setter, in a listener's and in the second form of `Connect`; and `null` is taken,
// which `FileTests` reads back as no bound by `getsockopt`
void | IoError Refused() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.SetReadTimeout(0s) else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    client.SetWriteTimeout(-1ms) else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    listener.SetAcceptTimeout(0s) else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    match TcpStream.Connect("127.0.0.1:{listener.Port}", 0s) {
        TcpStream => print("connected")
        IoError e => print("{e.Kind} {e.Code}")
    }

    client.SetReadTimeout(null)
    server.Close()
}

Refused() else {
    IoError e => print(e)
}
```

Case: [system/setter-after-close.kz](../corpus/system/setter-after-close.kz)

Case: [system/shutdown-write.kz](../corpus/system/shutdown-write.kz)
```kurz
// the writes of one end ended through one holder: its peer reads the end of the stream, a second call passes, and the other
// holder still reads and has its write refused; a stream whose peer reset it, and a closed one, get the system's answer
void | IoError Half() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    held = server
    server.ShutdownWrite()
    mut end = Bytes()
    print(client.Read(mut end, 16))
    server.ShutdownWrite()
    print("again")
    client.Write("hi".Bytes)
    mut buffer = Bytes()
    mut read = 1
    while buffer.Count < 2 && read > 0 {
        read = held.Read(mut buffer, 2 - buffer.Count)
    }

    print(string.FromUtf8(buffer) else {
        InvalidUtf8 => "not UTF-8"
    })
    held.Write("late".Bytes) else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    // a peer that closes with a byte it never read, still in the system's queue, resets the connection
    reset = TcpStream.Connect("127.0.0.1:{listener.Port}")
    accepted = listener.Accept()
    accepted.Write("x".Bytes)
    reset.Close()
    accepted.ShutdownWrite() else {
        IoError e => print("{e.Kind} {e.Code}")
    }

    server.Close()
    server.ShutdownWrite() else {
        IoError e => print("{e.Kind} {e.Code}")
    }
}

Half() else {
    IoError e => print(e)
}
```

Case: [system/read-buffer-zero.kz](../corpus/system/read-buffer-zero.kz)

Case: [system/read-buffer-failed.kz](../corpus/system/read-buffer-failed.kz)

Case: [system/read-buffer-close-stream.kz](../corpus/system/read-buffer-close-stream.kz)

Case: [system/fill-timeout.kz](../corpus/system/fill-timeout.kz)

### S10 (decided, §12) A call blocks its thread

A call on a file or a socket is a system call that blocks the thread that makes it. A program
without actors serves one connection at a time.

No case: a case prints what its calls return, and the same output stands for a call that blocks
and for one that switches; S9's cases `tcp-echo.kz` and `bind-name.kz` connect before they accept, which
the system completes without the accept.

### S11 (decided, §12) Not on Windows

In a build for Windows, a call of this chapter is `not supported`, and the runtime of Windows gives
none of these calls, and so are `args`, `Environment.Args()`, `Environment.Variable`, `timestamp.Now`
and `File.Modified` (S13 to S15; §12, assumed), `instant.Now` and `t.Elapsed` (S25; §12, the owner,
2026-11-04, the monotonic clock, Q6 (a)), and a `Command`'s calls, `Stdin` and `Environment.Exit`
(S17 to S23; §12, assumed), and a log call (P6; §12, the owner, 2026-11-05, L2). A build for any other target whose runtime lacks them says `not supported` at the
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
the command line and the clock need no grant (§13, the owner, 2026-10-30, "`allow env`, as Deno", the option that carried this sentence). A package that starts a program, by `Output()`, `Status()` or `Spawn()` of a `Command` (S17 to S19), needs `allow run` in the same way (§13, the owner, 2026-11-01, "allow run"), and a call without it is `run-not-allowed`, while `Command.New` and the members that set a command need none, since nothing runs before a call that starts; a read of standard input needs none (S22). *(assumed, §13: the id `run-not-allowed` and the members that set a command)* For a package on the
path that calls no such function itself, the error stands at its call that leads on toward the
function, a line of its own code, and names the path, as `Report -> Store.Save -> File.Write`, one
error for each such line (§13, the owner, 2026-10-30, "At its call, naming the path"). The program
itself needs neither. A call through an interface on the path counts as a call of every implementer of
`std/` and of the packages, and a call of a function value as a call of every such function whose value is
taken, while an implementer or a function of the program's own code is no path that needs the grant: a
path from a package's call ends there, and what that code calls is a path from the program's code, so
that `FileLog.Write -> Store.Save -> File.Write`, with `FileLog` the program's own logger, needs the
grant of `Store` and not of the package that called the logger, as the cost of the option set aside
reads it (§13, the owner, 2026-10-30, "Program's
own don't count"). Where several paths lead through one line, its one
error names the shortest, two of one length in the order of the source (§13, the owner, 2026-10-30, "The
shortest"). *(assumed, §13: `Directory` among the file functions; such a call counted where it never reaches
the function; of two paths of one length, the one that stands first where the two part, the call
written first in that function, or, at one call through an interface or a function value, the
implementer, or the function the function value holds, whose file comes first in the order of the file
paths (§4) and, in one file, whose line and then whose column come first)* Whether a `send` to an actor puts the sender's package on the path stands open (§14).

No case: a case is a program and no package, and the program needs no grant.

### S13 (decided, §2, §12) The command line

Top-level code sees `args`, a `List<string>` of the program's arguments without its name, and
`Environment.Args()` is the list with the program's name first (§12, the owner, 2026-10-30, "Both"). Only
the top-level statements see `args`: in a function it is `unknown-name`, as a top-level variable is
(V10), and a function may name a parameter or a variable of its own `args`, while a top-level `args` of
the program's own, `List<string> args = ["a"]`, is `redeclared` (§12, the owner, 2026-10-30, "Top-level
code only"). Whether `args = value` among the top-level statements assigns to the program's `args`, a
variable without `mut`, and is `assign-immutable`, as V8 reads `name = value` where the name is visible
(§8), or declares one and is `redeclared`, as the program's own `args` above is under "Top-level code
only" (§12), stands open (§14). A lambda written among the top-level statements sees `args`, as it sees a top-level variable it
captures (V10), so that within the top-level code `args` is a visible name, and a lambda's parameter or a
block's variable named `args` there is `redeclared` (V9). `args` is declared without `mut`, so that
`args.Add("x")` is `mut-required` (M5), and each call of `Environment.Args()` makes a new list.
*(assumed, §12: the lambda, a lambda's parameter and a block's variable, `args` without `mut`, the new
list)*
An argument that is not UTF-8 ends the process with the run-time error `invalid-argument` (§12, the
owner, 2026-10-30, "Refused, as Rust"): `Environment.Args()` raises it at its call, with its place and
its trace, and `args`, made before the first statement of a top-level code that names it, with no place
and no trace, as `random-failed`; no `catch` takes the raise (O6). A top-level code names `args` where
any of its statements does, a lambda's body and a branch that never runs among them, so that a program whose only `args` stands
in a lambda never called, or in a branch never taken, ends on an argument that is not UTF-8 it never reads, where Rust's
`env::args()` panics only during the iteration over its items. `args` checks the words it holds and
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

Case: [system/args-block-variable.kz](../corpus/system/args-block-variable.kz)
```kurz
if true {
    List<string> args = ["a"]
    print(args.Count)
}
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

Case: [system/environment-field.kz](../corpus/system/environment-field.kz)
```kurz
// a field takes the name `EnvError`, `EnvKind` or `Environment`, a parameter of a primary constructor among them (S14)
data Failure(int EnvError, int EnvKind)
class Process(int Environment)

failure = Failure(1, 2)
print(failure.EnvError + failure.EnvKind)
print(Process(4).Environment)
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

### S25 (decided, §12) The monotonic clock

`instant.Now` is a reading of the monotonic clock, an `instant` (T36), read outside a test from
`clock_gettime(CLOCK_MONOTONIC)` on Linux, which counts no time while the system is suspended, as
Rust's `Instant::now()` and .NET's `Stopwatch` read it there, and `start.Elapsed`, a `duration`,
measures the time since `start` (§12, the owner, 2026-11-04, issue 110, comment 6643368937, the
monotonic clock, Q1 (a) and Q5 (a)). In a test, `instant.Now` reads the runtime's clock, which
`advance` moves (§11, the same answer, Q7 (a)), and the real clock keeps running beneath it, so that
a reading in a test is the real one plus what `advance` added *(assumed, §11: the round's default,
not objected to)*; the reference has no rule for `advance` or for a test, so no case reads that
clock. A reading's text is its time since the clock's zero in the text of a duration (A12), and a
`data` value that holds one shows it so (§12, the same answer, Q4 (a)). `instant.Now` is
`not supported` in a build for Windows, as `timestamp.Now` is, and so is `t.Elapsed`, which reads
the clock (S11; §12, the same answer, Q6 (a)). *(assumed, §12: the defaults of that round, not
objected to: `t.Elapsed` is `instant.Now - t`; two readings may be equal, since the clock need not
move between two reads; the zero is one the record does not name, on Linux the boot; and
`instant.Now` reads the runtime's one monotonic clock, which the bounds of `Connect` read too.)*
That default is the value of `t.Elapsed` and not the order of its reads: `t.Elapsed` reads its
target first and then the clock, the order C# gives a property, whose receiver is evaluated before
its getter runs, and the order of Rust's `t.elapsed()`, which evaluates `t` before it reads
`Instant::now()` *(assumed, §12: the order, which the round did not name)*.

Case: [system/monotonic-clock.kz](../corpus/system/monotonic-clock.kz)
```kurz
instant start = instant.Now
instant later = instant.Now
print(later >= start)
duration taken = start.Elapsed
print(taken >= later - start)
print(instant.Now - start >= taken)
```

Case: [system/elapsed-order.kz](../corpus/system/elapsed-order.kz)
```kurz
instant Moved() {
    instant first = instant.Now
    mut later = instant.Now
    while later == first {
        later = instant.Now
    }

    return later
}

print(Moved().Elapsed >= 0s)
```

Case: [values/text-instant.kz](../corpus/values/text-instant.kz)
```kurz
data Mark(instant At)

instant Reading(long nanoseconds) {
    long bits = nanoseconds
    raw {
        return *Ptr<instant>(&bits)
    }
}

print(Reading(0))
print(Reading(7505123456789))
print(Reading(-1))
print(Mark(Reading(1000000000)))
```

### S26 (decided, §12) A reading stays in its process

A reading in an actor's message or in a `durable` actor's state is the compile error
`instant-leaves-process`, since a reading means nothing in another process or after a restart, and a
deadline crosses as the `duration` left (§12, the owner, 2026-11-04, issue 110, comment 6643368937,
the monotonic clock, Q8 (a)) *(assumed, §12: the error's name, which the record does not give; a
reading at any depth of the message's or the state's type, and the error at the argument that
carries the message and at the declaration of the state)*.

No case: a case runs one actor and keeps no state past its run, and the compiler builds neither a
send to another actor nor a `durable` actor, so no case can hold the error, and its id stands in
this rule and not in [12-errors.md](12-errors.md), whose row comes with the case that expects it.

### S16 (decided, §12) The lines of a file

`for line in f.Lines() { }` runs its block once for each line of a file `File.Open` opened, pulling the
lines one at a time, as Rust's `BufRead::lines()` and C#'s `File.ReadLines` pull them; `line` is a
`string`. `\n` and `\r\n` end a line and are not part of it, as Rust's rule has it, where C# ends a
line at a lone `\r` too; the last line needs no line break, and a final `\n` gives no empty line after
it (§12, the owner, 2026-11-01, "Lazy, `for line in ...`",
"`f.Lines()` on an open File" and "As Rust"). A lone `\r` stays in its line, and an empty file gives no
line *(assumed, §12)*.

Case: [system/lines-read.kz](../corpus/system/lines-read.kz)
```kurz
void | IoError | InvalidUtf8 Show() {
    // `\n` and `\r\n` end a line, and the last line needs no line break
    File.WriteAll("lines-read.txt", "a\nb\r\n\r\nc".Bytes)
    f = File.Open("lines-read.txt")
    for line in f.Lines() {
        print(line)
    }

    // a lone `\r` stays in its line, and a final `\n` gives no empty line after it
    File.WriteAll("lines-read.txt", "x\ry\n".Bytes)
    g = File.Open("lines-read.txt")
    File.Delete("lines-read.txt")
    for line in g.Lines() {
        print(line.Bytes.Count)
        print(line.Contains('\r'))
    }
}

Show() else {
    IoError e => print(e)
    InvalidUtf8 u => print(u)
}
```

Case: [system/lines-empty.kz](../corpus/system/lines-empty.kz)
```kurz
int | IoError | InvalidUtf8 Count(string text) {
    File.WriteAll("lines-empty.txt", text.Bytes)
    f = File.Open("lines-empty.txt")
    File.Delete("lines-empty.txt")
    mut count = 0
    for line in f.Lines() {
        count = count + 1
    }

    return count
}

// an empty file has no line, one line break alone one empty line, and an empty line between two counts
print(Count("") else {
    IoError => -1
    InvalidUtf8 => -2
})
print(Count("\n") else {
    IoError => -1
    InvalidUtf8 => -2
})
print(Count("a\n\nb") else {
    IoError => -1
    InvalidUtf8 => -2
})
```

The `File` buffers its own reads and stays open after the loop, whether it ended, a `break` left it or
a failure ended it: the lines it did not pull stay in the `File`, so that a later `Lines()` or `Read`
goes on where the loop stopped, and a `Read` takes the buffered bytes first (§12, the owner, 2026-11-01,
"`f.Lines()` on an open File" and "Stays open, continues"). The buffer is the one every `Read` goes
through (S3), so that such a `Read` takes at most `max` of the buffered bytes first (§12, the owner,
2026-11-03, issue 39, comment 6605104107), and it holds 8 KiB, as Rust's `BufReader` (§12, the
owner, 2026-11-02).

Case: [system/lines-break-read.kz](../corpus/system/lines-break-read.kz)
```kurz
void | IoError | InvalidUtf8 Show() {
    File.WriteAll("lines-break.txt", "one\ntwo\nthree\nfour\n".Bytes)
    f = File.Open("lines-break.txt")
    File.Delete("lines-break.txt")
    for line in f.Lines() {
        print(line)
        break
    }

    // the file stays open, and a `Read` takes the bytes the loop left in its buffer first
    mut buffer = Bytes()
    print(f.Read(mut buffer, 4))
    print(string.FromUtf8(buffer).Trim())
    // a second loop goes on where the `Read` stopped
    for line in f.Lines() {
        print(line)
    }

    print(f.Read(mut buffer, 4))
}

Show() else {
    IoError e => print(e)
    InvalidUtf8 u => print(u)
}
```

`Close` gives up the bytes the buffer holds, so that a later `Lines()` or `Read` takes none of them
and gets the error of a closed file (S5; §12, the owner, 2026-11-02). A line longer than the buffer is read whole,
and one of more bytes than the largest `int` ends the process with `allocation-failed`, as a string
past it does *(assumed, §12)*. The loop keeps the `File` its collection gave when it started: an
assignment to the variable in the block changes the variable, not what the loop pulls *(proposed)*.

Case: [system/lines-keeps-its-file.kz](../corpus/system/lines-keeps-its-file.kz)
```kurz
void | IoError | InvalidUtf8 Show() {
    File.WriteAll("lines-keeps-a.txt", "a1\na2\n".Bytes)
    File.WriteAll("lines-keeps-b.txt", "b1\n".Bytes)
    mut f = File.Open("lines-keeps-a.txt")
    g = File.Open("lines-keeps-b.txt")
    File.Delete("lines-keeps-a.txt")
    File.Delete("lines-keeps-b.txt")
    // the loop keeps the `File` it started with: the assignment changes the variable, not what the loop pulls
    for line in f.Lines() {
        print(line)
        f = g
    }

    // after the loop the variable holds the other file
    for line in f.Lines() {
        print(line)
    }
}

Show() else {
    IoError e => print(e)
    InvalidUtf8 u => print(u)
}
```

A failure part-way through the read ends the loop and leaves the function through it, as a case of a
call does (O2, O3): a line that is not UTF-8 with the case `InvalidUtf8`, as `string.FromUtf8` gives it
(T15), and a failed read with an `IoError` (S1). A postfix `else` after the loop's block, on the line of
its closing brace, handles the cases it names, as after a call (O5), and the program goes on after the
loop; top-level code, which has no caller, writes it (O9) (§12, the owner, 2026-11-01, "As Rust" and
"Unwrap like a call"). The `At` of `InvalidUtf8` is the position in the line, and the line's bytes are
taken, so that a later loop goes on after them; an `IoError` in the middle of a line gives no part of it
*(assumed, §12)*, and the part read is taken as well *(proposed)*.

Case: [system/lines-invalid-utf8.kz](../corpus/system/lines-invalid-utf8.kz)
```kurz
void | IoError Show() {
    mut content = "first\nab".Bytes
    content.Add(byte(255))
    content.AddRange("\nthird\n".Bytes)
    File.WriteAll("lines-invalid.txt", content)
    f = File.Open("lines-invalid.txt")
    File.Delete("lines-invalid.txt")
    // a line that is not UTF-8 ends the loop with `InvalidUtf8`, whose `At` is the position in the line
    for line in f.Lines() {
        print(line)
    } else {
        InvalidUtf8 u => print("bad line at {u.At}")
    }

    // its bytes were taken, and the next loop goes on after them
    for line in f.Lines() {
        print(line)
    } else {
        InvalidUtf8 => print("bad line")
    }
}

Show() else {
    IoError e => print(e)
}
```

Case: [system/lines-propagate.kz](../corpus/system/lines-propagate.kz)
```kurz
// a failure of the read leaves the function through the loop, as a case of a call does
int | IoError | InvalidUtf8 Longest(File f) {
    mut longest = 0
    for line in f.Lines() {
        if line.Bytes.Count > longest {
            longest = line.Bytes.Count
        }
    }

    return longest
}

void Show(Bytes content) {
    File.WriteAll("lines-propagate.txt", content) else {
        IoError => return
    }

    f = File.Open("lines-propagate.txt") else {
        IoError => return
    }

    File.Delete("lines-propagate.txt") else {
        IoError => return
    }

    match Longest(f) {
        int n => print(n)
        IoError e => print(e)
        InvalidUtf8 u => print("not UTF-8 at {u.At}")
    }
}

Show("ab\nc\n".Bytes)
mut bad = "x\n".Bytes
bad.Add(byte(255))
Show(bad)
```

On a file `File.Create` opened, and on a closed file, the first pull gives the error the system gives a
descriptor that is not open to read, `Other` with the number 9 (S5) *(assumed, §12)*.

Case: [system/lines-not-open-to-read.kz](../corpus/system/lines-not-open-to-read.kz)
```kurz
// a file created to write, and a closed one, give the system's error for a descriptor that is not open to read
void | IoError Pull(File f) {
    for line in f.Lines() {
        print(line)
    } else {
        InvalidUtf8 => print("not UTF-8")
    }
}

void | IoError Show() {
    created = File.Create("lines-created.txt")
    File.Delete("lines-created.txt")
    Pull(created) else {
        IoError e => print(e)
    }

    File.WriteAll("lines-closed.txt", "a\n".Bytes)
    closed = File.Open("lines-closed.txt")
    File.Delete("lines-closed.txt")
    closed.Close()
    Pull(closed) else {
        IoError e => print(e)
    }
}

Show() else {
    IoError e => print(e)
}
```

A case the loop can fail with that the function's return type does not list is `unlisted-case`, at the
loop's line, as for a call (O3, O9).

Case: [system/lines-unlisted.kz](../corpus/system/lines-unlisted.kz)
```kurz
// the read's `InvalidUtf8` can leave the function through the loop, and its return type does not list it
int | IoError Count(File f) {
    mut count = 0
    for line in f.Lines() {
        count = count + 1
    }

    return count
}

print(1)
```

Case: [system/lines-top-level.kz](../corpus/system/lines-top-level.kz)
```kurz
f = File.Open("lines-top-level.txt") else {
    IoError => throw
}
// top-level code has no caller: the loop's failures must be handled in an `else` after it, which this loop lacks
for line in f.Lines() {
    print(line)
}
```

`f.Lines()` stands as the collection of a `for` loop alone and has no type name: anywhere else it is
text no rule gives a meaning, `syntax` (E4) (§12, the owner, 2026-11-01, "Loop source only";
*(proposed: the id)*). An arm of the `else` that names a case the collection cannot give is
`type-mismatch` at the arm, as an arm of O5's that names no case of the call *(proposed)*, and after a
loop over a list, a range or a map, which fails with no case, every such arm is *(proposed)*. An `else` with no
arms there is `unreachable-arm` at the line of the `else`, as an `else` arm there is (§5, the owner,
2026-11-02). Two names, `for i, line in f.Lines()`, are `type-mismatch` at the loop's line, as over a
list (C6) *(proposed)*. An arm of the `else` stands after the loop: a `break` or `continue` in it
belongs to a loop around the loop, and with none it is `break-outside-loop` (C7) *(proposed)*.

Case: [system/lines-not-a-value.kz](../corpus/system/lines-not-a-value.kz)
```kurz
// `f.Lines()` stands as the collection of a `for` loop alone
void Show(File f) {
    print(f.Lines())
}

print(1)
```

Case: [system/lines-else-on-list.kz](../corpus/system/lines-else-on-list.kz)
```kurz
// an `else` after a loop takes the cases its collection can fail with: a list's has none
xs = [1, 2]
for x in xs {
    print(x)
} else {
    IoError e => print(e)
}
```

Case: [system/lines-else-empty-on-list.kz](../corpus/system/lines-else-empty-on-list.kz)
```kurz
// an `else` with no arms after a loop whose collection fails with no case: nothing can reach it
xs = [1, 2]
for x in xs {
    print(x)
} else {
}
```

Case: [system/lines-two-names.kz](../corpus/system/lines-two-names.kz)
```kurz
// two names walk the entries of a map, and a file's lines are no map
void Run(File f) {
    for i, line in f.Lines() {
        print(line)
    } else {
        IoError e => print(e)
        InvalidUtf8 u => print(u)
    }
}

print(1)
```

Case: [system/lines-break-in-arm.kz](../corpus/system/lines-break-in-arm.kz)
```kurz
// an arm of the `else` stands after the loop: its `break` belongs to a loop around the loop, and there is none
void | IoError Run(File f) {
    for line in f.Lines() {
        print(line)
    } else {
        InvalidUtf8 => break
    }
}

print(1)
```

A pull takes a lock in the `File`, so that two holders that pull lines at once each take whole lines
(§12, the owner, 2026-11-01, "Lock per pull"), and a `Read` takes it while it takes the buffered bytes
*(proposed)*.

No case: a case runs one actor, and two holders that pull at once need two.

### S17 (decided, §12) A command

`Command.New(program)` names a program to run, and the members of a `Command` set its arguments,
`Arg(x)` and `Args(list)`, its working directory, `Dir(path)`, its environment, `Env(name, value)`,
`EnvRemove(name)` and `EnvClear()`, its three streams, `Stdin(s)`, `Stdout(s)` and `Stderr(s)`, each a
value of `enum Stdio { Inherit, Piped, Null }`, its input, `Input(bytes)` (S19), and a bound on its time,
`Timeout(d)` (S20); `Output()` (S18), `Status()` (S18) and `Spawn()` (S19) run it (§12, the owner,
2026-11-01, "Chain, each step a new value"). Each member gives a new `Command` and leaves the one it was
called on as it was, so that a chain needs no `mut` and a loop writes `cmd = cmd.Arg(x)` on a `mut`
variable; where no other value holds the command a member is called on, the new command is that one
changed in place, as a write into a list's block that no other value holds (M3), so that a chain builds
into one block and a command is copied only where a program keeps it and calls a member on it again;
in `cmd = cmd.Arg(x)`, where no value but `cmd` holds the command, it changes in place, since the
assignment keeps no earlier command. The
names `Command`, `ChildProcess`, `ProcessOutput`, `ExitStatus` and `Stdio` are seen by every file, so
that a file's own declaration of one is `redeclared` (V9), as of `Environment` (S14) (§12, the owner,
2026-11-01, "Mixed: ChildProcess, ProcessOutput"), and so is `ExitError` (S18). A program that does not
start is an `IoError` (S1) whose path S21 gives. *(assumed, §12: the change in place and
its count, the answer given to a question on that answer, and `cmd = cmd.Arg(x)`; `ExitError` among the names; a command
without a text and without `==`)*

Case: [system/command-output.kz](../corpus/system/command-output.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Show() {
    out = Command.New("printf").Args(["%s-%s", "a"]).Arg("b").Output()
    print(string.FromUtf8(out.Stdout))
    print(out.Status.Code)
    print(out.Stderr.Count)
}

Show() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/command-reuse.kz](../corpus/system/command-reuse.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Show() {
    base = Command.New("printf").Arg("[%s]")
    a = base.Arg("a")
    b = base.Arg("b")
    mut c = base
    c = c.Arg("x")
    print(string.FromUtf8(a.Output().Stdout))
    print(string.FromUtf8(b.Output().Stdout))
    print(string.FromUtf8(c.Output().Stdout))
    print(string.FromUtf8(base.Output().Stdout))
}

Show() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/command-not-found.kz](../corpus/system/command-not-found.kz)
```kurz
match Command.New("kurz-case-no-such-program").Output() {
    ProcessOutput o => print(o.Status)
    IoError e => {
        print(e)
        print(e.Kind)
    }
    ExitError e => print(e)
}
```

Case: [system/command-redeclared.kz](../corpus/system/command-redeclared.kz)
```kurz
data Command(string Program)

print(1)
```

### S18 (decided, §12) Output, status and a code other than 0

`Output()` runs the command to its end and gives `ProcessOutput | IoError | ExitError`, where `data
ProcessOutput(ExitStatus Status, Bytes Stdout, Bytes Stderr)` holds what the child wrote as bytes, which
`string.FromUtf8` (T15) turns into text (§12, the owner, 2026-11-01, "Bytes, as Rust"). A child that ends
with a code other than 0, or that a signal ends, is a case of the call, which the unwrap rule sends to
the caller unless an `else` takes it (O2, O5) (§12, the owner, 2026-11-01, "A case of the call"); that
case is `data ExitError(string Program, ProcessOutput Output)`. `ExitStatus` holds `Code`, an `int` that is 128 and
the signal's number where a signal ended the child, `Signal`, an `int?` that holds that number, and
`Success`, a `bool` (§12, the owner, 2026-11-01, "Code int + Signal int?"). `Status()` runs the command
with the program's three streams and gives `void | IoError | ExitError`; the `ExitError` of `Status()`
and of `Wait()` (S19) holds the status and two empty `Bytes`. `ExitStatus` is `data ExitStatus(int Code,
int? Signal, bool Success)`, whose text A4 gives. The text of an `ExitError` is
the words of what ended the child, a colon and the program: `exit status 3: sh`, `signal 15: sh`.
*(assumed, §12: `Status()`'s result, its streams and the output of its `ExitError` and of `Wait()`'s, `ExitStatus` as a
`data` type, the name
`ExitError`, its fields and its text, `ProcessOutput` keeping the status, `Success` where `Code` is 0 and
`Signal` is `null`)*

Case: [system/command-output.kz](../corpus/system/command-output.kz)

Case: [system/command-exit-code.kz](../corpus/system/command-exit-code.kz)
```kurz
void | InvalidUtf8 Show(ExitError e) {
    print(e)
    print(e.Output.Status.Code)
    print(e.Output.Status.Signal)
    print(e.Output.Status.Success)
    print(string.FromUtf8(e.Output.Stdout))
    print(string.FromUtf8(e.Output.Stderr))
}

match Command.New("sh").Args(["-c", "printf out; printf err >&2; exit 3"]).Output() {
    ProcessOutput o => print(o.Status)
    IoError e => print(e)
    ExitError e => {
        Show(e) else {
            InvalidUtf8 u => print(u)
        }
    }
}
```

Case: [system/command-signal.kz](../corpus/system/command-signal.kz)
```kurz
match Command.New("sh").Args(["-c", "kill -TERM $$"]).Output() {
    ProcessOutput o => print(o.Status)
    IoError e => print(e)
    ExitError e => {
        print(e)
        print(e.Output.Status.Code)
        print(e.Output.Status.Signal)
        print(e.Output.Status.Success)
    }
}
```

Case: [system/command-status.kz](../corpus/system/command-status.kz)
```kurz
void | IoError | ExitError Run() {
    Command.New("true").Status()
    print("true ended")
    Command.New("false").Status()
    print("not reached")
}

Run() else {
    IoError e => print(e)
    ExitError e => {
        print(e)
        print(e.Output.Stdout.Count)
    }
}
```

### S19 (decided, §12) A child beside the program, and its input

`Spawn()` starts the command and gives `ChildProcess | IoError`. A `ChildProcess`'s `Stdin`, `Stdout`
and `Stderr` are each a `File?`, a stream where the command set it to `Stdio.Piped` and `null`
otherwise, which is read, written and closed as a `File` is (S3 to S5); `Wait()` waits for the child's
end and gives `void | IoError | ExitError`, `WaitWithOutput()` gives `ProcessOutput | IoError |
ExitError` as `Output()` does, `Kill()` sends it `SIGKILL` and gives `void | IoError`, and `Id` is its
process's number, an `int`. `Input(bytes)` gives the child an input that the call that waits writes
while it reads the output, so that neither side waits on a full pipe (§12, the owner, 2026-11-01, "Both
forms"). `Input` beside `Spawn()`, or beside a member `Stdin`, is `InvalidInput` (S1) at the call that
starts the child, so that a spawned child takes its input through `child.Stdin`, and `Status()` writes
the input and closes the child's input (§12, the owner, 2026-11-02, "`Input` beside `Spawn()` or beside
a `Stdin` member is `InvalidInput` at the start"). `Status()` with a `Stdout` set to `Stdio.Piped`
closes the program's end of that pipe at once, so that a child that writes there is ended by `SIGPIPE`
(S21) and the call gives an `ExitError` (§12, the owner, 2026-11-02, "`Status()` with a piped Stdout
closes the program's end of that pipe at once"). A second wait of either kind gives the status the
first gave, and `WaitWithOutput()` reads each pipe from where the program stopped, a pipe read to its
end or closed giving empty `Bytes`; `Kill()` on a child that ended or was waited for gives `void` and
sends no signal; a child let go of without a `Wait()` is neither waited for nor killed. *(assumed, §12:
the results of `Wait()`, `WaitWithOutput()` and `Kill()`, the signal, the later waits, `Kill()` after the
end, the child let go of, `Id`'s type, `Status()` writing the input under `poll`, and a piped `Stderr`
under `Status()` closed as a piped `Stdout` is)*

Case: [system/spawn-pipes.kz](../corpus/system/spawn-pipes.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Run() {
    child = Command.New("cat").Stdin(Stdio.Piped).Stdout(Stdio.Piped).Spawn()
    print(child.Id > 0)
    match child.Stdout {
        File => print("stdout piped")
        null => print("no stdout")
    }
    match child.Stderr {
        File => print("stderr piped")
        null => print("no stderr")
    }
    match child.Stdin {
        File input => {
            input.Write("piped".Bytes)
            input.Close()
        }
        null => print("no input")
    }
    out = child.WaitWithOutput()
    print(string.FromUtf8(out.Stdout))
}

Run() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/spawn-kill.kz](../corpus/system/spawn-kill.kz)
```kurz
void | IoError | ExitError Run() {
    child = Command.New("sleep").Arg("10").Spawn()
    child.Kill()
    child.Wait()
    print("not reached")
}

Run() else {
    IoError e => print(e)
    ExitError e => {
        print(e)
        print(e.Output.Status.Code)
        print(e.Output.Status.Signal)
    }
}
```

Case: [system/command-input.kz](../corpus/system/command-input.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Run() {
    small = Command.New("cat").Input("hi".Bytes).Output()
    print(string.FromUtf8(small.Stdout))
    mut big = Bytes()
    while big.Count < 1048576 {
        big.Add(120)
    }

    echoed = Command.New("cat").Input(big).Output()
    print(echoed.Stdout.Count)
}

Run() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/spawn-input.kz](../corpus/system/spawn-input.kz)
```kurz
match Command.New("cat").Input("hi".Bytes).Spawn() {
    ChildProcess c => print(c.Id > 0)
    IoError e => print(e)
}

match Command.New("cat").Stdin(Stdio.Piped).Input("hi".Bytes).Output() {
    ProcessOutput o => print(o.Status)
    IoError e => print(e)
    ExitError e => print(e)
}
```

Case: [system/status-piped.kz](../corpus/system/status-piped.kz)
```kurz
Command.New("yes").Stdout(Stdio.Piped).Status() else {
    IoError e => print(e)
    ExitError e => print(e)
}
```

Case: [system/status-input.kz](../corpus/system/status-input.kz)
```kurz
Command.New("cat").Input("hi\n".Bytes).Status() else {
    IoError e => print(e)
    ExitError e => print(e)
}
```

### S20 (decided, §12) A bound on a child's time

`Timeout(d)`, a `duration`, bounds the run from its start: when it has passed, the call that waits
kills the child, waits for its end and gives an `IoError` of the kind `TimedOut` (S1) whose path is the
program, `timed out: sleep`; without it a call waits without a bound (§12, the owner, 2026-11-01,
"Optional .Timeout(d)"). A bound of 0 or below has passed at the start, so that the child is killed at
once and the call gives `TimedOut`. Under `Spawn()` the bound is held by `Wait()` and `WaitWithOutput()`,
so that a read of the child's pipe before them and a child let go of are not bounded, and a child that
ended before the bound gives its own status. *(assumed, §12: the kill by `SIGKILL`, the path, a bound of 0
or below, the bound under `Spawn()` and the child's end)*

Case: [system/command-timeout.kz](../corpus/system/command-timeout.kz)
```kurz
void | IoError | ExitError Run() {
    Command.New("sleep").Arg("10").Timeout(100ms).Status()
    print("not reached")
}

Run() else {
    IoError e => {
        print(e)
        print(e.Kind)
    }
    ExitError e => print(e)
}
```

### S21 (assumed, §12) How a child starts

There is no shell: each argument reaches the program as it is written, and a program that wants a shell
runs `sh -c`. A program named without a `/` is looked up in the `PATH` the child gets, and in the
system's default path where it gets none; an empty program is `NotFound` (S1) at the call that starts
it, with the empty path; a program named with a `/` but not from the root is found from the directory
`Dir` names. A child inherits the program's environment, which `Env`, `EnvRemove` and `EnvClear` change,
and runs in the program's working directory unless `Dir` names another; the members apply in the order
they were called, the later one winning, so that `EnvClear()` drops what an earlier `Env` set and a
second `Dir` replaces the first. It gets back the default action of `SIGPIPE`, which the
runtime sets to ignored at its start (§6), and no descriptor but its three streams. `Output()` pipes the
child's output and error and gives it an input at its end; `Status()` and `Spawn()` give it the
program's three streams; a member `Stdin`, `Stdout` or `Stderr` changes that, and `Input` gives the
child its input (S19); `Output()` gives empty `Bytes` for a stream set to `Stdio.Inherit` or `Stdio.Null`.
A call that waits for a child blocks its thread in a program without actors, and in an actor parks it
while the wait runs on the pool of threads for blocking calls, as a call on a file does (S10). A start that fails is an
`IoError` (S1) whose path is the program, or the directory where that is what does not exist, and never
an argument, the input or the output. A NUL byte in the program, an argument, the directory, or a name
or a value of the environment is `InvalidInput` with the number 0 at the call that starts the child. A
build for Windows says `not supported` to each of these calls, as to a call on a file (S11).

Case: [system/command-not-found.kz](../corpus/system/command-not-found.kz)

Case: [system/command-env-dir.kz](../corpus/system/command-env-dir.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Run() {
    Directory.Create("command-dir")
    script = "printf '%s|%s|%s' \"$KURZ_CASE_A\" \"$KURZ_CASE_B\" \"$(basename \"$(pwd -P)\")\""
    out = Command.New("sh").Args(["-c", script]).Env("KURZ_CASE_A", "1").Env("KURZ_CASE_B", "2").EnvRemove("KURZ_CASE_B").Dir("command-dir").Output()
    print(string.FromUtf8(out.Stdout))
    Directory.Delete("command-dir")
}

Run() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/command-words.kz](../corpus/system/command-words.kz)
```kurz
void | IoError | ExitError | InvalidUtf8 Run() {
    words = Command.New("printf").Args(["%s", "$HOME *"]).Output()
    print(string.FromUtf8(words.Stdout))
}

Run() else {
    IoError e => print(e)
    ExitError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/command-nul.kz](../corpus/system/command-nul.kz)
```kurz
match Command.New("printf").Arg("a\0b").Output() {
    ProcessOutput o => print(o.Status)
    IoError e => {
        print(e)
        print(e.Code)
    }
    ExitError e => print(e)
}
```

### S22 (decided, §12) Standard input

`Stdin` reads the program's standard input: `for line in Stdin.Lines() { }` takes its lines,
`Stdin.ReadAll()` reads what is left into a `Bytes`, `Bytes | IoError`, `Stdin.Read(mut into, max)`
appends up to `max` bytes as `f.Read` does (S3), and `Stdin.ReadLine()` gives one line, `string? |
IoError | InvalidUtf8`, `null` at the end; one buffer serves the whole process, behind a lock taken for
each pull (§12, the owner, 2026-11-01, "A type Stdin" and "Also Stdin.ReadLine()"). The lines are read
as a file's are: `\n` and `\r\n` end a line and are taken off it, no empty line follows the last line
break, a line that is not UTF-8 ends the read with `InvalidUtf8` (T15), `Stdin.Lines()` stands as a
`for` loop's collection alone, a failure leaves the loop as a call's case leaves it, and the next read
goes on where the loop stopped. A read waits until the other end writes or closes, with no bound, and
runs as a call on a file does (S10). `Stdin` has no `Close`; its `IoError` names the path `<stdin>`; a read
needs no grant (S12); a file's own declaration of `Stdin` is `redeclared` (V9); and a build for Windows
says `not supported` to it (S11). *(assumed, §12: the reading of the lines as a file's, `ReadAll`'s
result, the wait of a read, no `Close`, the path, the grant, the name and Windows)*

Both cases read the standard input a case is given, which holds nothing (chapter 0).

Case: [system/stdin-empty.kz](../corpus/system/stdin-empty.kz)
```kurz
match Stdin.ReadLine() {
    string line => print(line)
    null => print("end")
    IoError e => print(e)
    InvalidUtf8 e => print(e)
}

void | IoError Rest() {
    all = Stdin.ReadAll()
    print(all.Count)
    mut into = Bytes()
    print(Stdin.Read(mut into, 16))
}

Rest() else {
    IoError e => print(e)
}
```

Case: [system/stdin-lines.kz](../corpus/system/stdin-lines.kz)
```kurz
void | IoError | InvalidUtf8 Echo() {
    for line in Stdin.Lines() {
        print(line)
    }
    print("end")
}

Echo() else {
    IoError e => print(e)
    InvalidUtf8 e => print(e)
}
```

Case: [system/stdin-redeclared.kz](../corpus/system/stdin-redeclared.kz)
```kurz
data Stdin(int Descriptor)

print(1)
```

### S23 (decided, §12) The exit code

A program sets its exit code with `Environment.Exit(code)` and with a top-level `return n` (F17) (§12, the
owner, 2026-11-01, "Top-level return n + Exit"). `Environment.Exit(code)` ends the process at once with
`code`: the other actors end where they stand, no handle is closed at its last count but by the system,
and nothing a write that returned before the call wrote is lost, since `print` and `f.Write` keep no
buffer, while a write in progress in another actor can be cut short. No path
goes on after the call, so that no `return` follows it, as after a `throw`; Linux takes the low 8 bits
of the code, `Environment.Exit(256)` ending with 0; a call in any actor ends the process; and a build for
Windows says `not supported` to it (S11). A `return` with an `int` among the top-level statements ends
the program with that code, of which Linux takes the low 8 bits too, and a build for Windows says
`not supported` to it (F17). *(assumed, §12: the name, the end at once and what it keeps and cuts,
the path after the call, the low 8 bits, any actor and Windows)*

No case for a code other than 0: a case that expects output ends with 0.

Case: [system/exit-in-function.kz](../corpus/system/exit-in-function.kz)
```kurz
int Stop() {
    print("stopping")
    Environment.Exit(0)
}

print("start")
code = Stop()
print(code)
```
