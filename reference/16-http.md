# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (decided, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions`, `Uri`, `HttpBody` and `HttpWriter`, with `UriError`,
`UriKind` and `HttpRate`, as `std/`
declares them, as it sees `File` and `TcpStream` (S2, S8). A file's own declaration of one of these
names that binds it in a scope, a type, a type parameter, a function, a variable, a parameter, a
lambda's parameter, a variable in a block, a loop variable or a name an arm of `match` binds, is
`redeclared` (V9), whether `std/` declares that type or not, so that no program stops building when
`std/` adds it. A member so named, a field, a parameter of a primary constructor among them (K1), a
`static` field, a method, an `enum` value or a member of a `data` type, is no declaration of the
name and builds. Outside its
class the name is the type; inside a class that has such a member, a type's place finds the type and
an expression finds the member first, as C# reads a simple name (`HttpError.Make()` inside a class
with an `int HttpError` is CS1061), so that inside `class S(int HttpError)` the call
`HttpError(HttpKind.Protocol, "x")` reads the field and is `type-mismatch`.
The names are a list the compiler holds, each a type `std/` declares or a name it reserves, where
Rust writes its list in `std`, as the module `std::prelude::rust_2021` that its compiler names by
edition (§12: the owner,
2026-11-01, Q7 (b), that every file sees them, and 2026-11-02, that a file's own declaration of one
is `redeclared` whether `std/` declares it or not, that only a declaration that binds the name in a
scope is one, against every member so named, that a type parameter so named is one, against an
exception like a member's, as Rust's `fn f<String>(x: String)` shadows, that an expression inside a
class finds a member so named first, as C# does, against a call's name looked up as a type first, and that the compiler holds
the list, against
every `pub` type of `std/` and against a list written in `std/`, and 2026-11-03, that `HttpBody` and
`HttpWriter` are two of them, against a body type no program can name; the two of `Uri`, and
`HttpRate`, whose type a program names to change a rate of `HttpLimits`, are the record's assumptions).

Case: [http/http-error-redeclared.kz](../corpus/http/http-error-redeclared.kz)
```kurz
data HttpError(int Code)

print(1)
```

Case: [http/http-kind-redeclared.kz](../corpus/http/http-kind-redeclared.kz)
```kurz
enum HttpKind { Other }

print(1)
```

Case: [http/http-error-kind.kz](../corpus/http/http-error-kind.kz)
```kurz
// the count of the bytes of a request line, or the failure of HTTP
int | HttpError Count(string line) {
    if line == "" {
        return HttpError(HttpKind.Protocol, "malformed request: no request line")
    }

    return line.Bytes.Count
}

void Show(string line) {
    match Count(line) {
        int n => print(n)
        HttpError e => {
            match e.Kind {
                HttpKind.Protocol => print("protocol: {e.Detail}")
                HttpKind.TooLarge => print("too large: {e.Detail}")
                HttpKind.Status => print("status: {e.Detail}")
                HttpKind.Unsupported => print("unsupported: {e.Detail}")
            }
        }
    }
}

Show("GET / HTTP/1.1")
Show("")
err = HttpError(HttpKind.TooLarge, "request header too large")
print(err.Kind)
print(err == HttpError(HttpKind.TooLarge, "request header too large"))
```

Case: [http/http-error-function.kz](../corpus/http/http-error-function.kz)
```kurz
int HttpError(int code) => code

print(1)
```

Case: [http/http-kind-variable.kz](../corpus/http/http-kind-variable.kz)
```kurz
int HttpKind = 3

print(HttpKind)
```

Case: [http/http-error-parameter.kz](../corpus/http/http-error-parameter.kz)
```kurz
int Twice(int HttpError) => HttpError * 2

print(Twice(2))
```

Case: [http/http-error-lambda-parameter.kz](../corpus/http/http-error-lambda-parameter.kz)
```kurz
twice = (int HttpError) => HttpError * 2

print(twice(2))
```

Case: [http/http-kind-block-variable.kz](../corpus/http/http-kind-block-variable.kz)
```kurz
if true {
    int HttpKind = 2
    print(HttpKind)
}
```

Case: [http/http-error-type-parameter.kz](../corpus/http/http-error-type-parameter.kz)
```kurz
int Same<HttpError>(HttpError x) => 1

print(Same(2))
```

Case: [http/http-kind-type-parameter.kz](../corpus/http/http-kind-type-parameter.kz)
```kurz
data Box<HttpKind>(HttpKind V)

print(Box(1).V)
```

Case: [http/http-kind-loop-variable.kz](../corpus/http/http-kind-loop-variable.kz)
```kurz
for HttpKind in [1, 2] {
    print(HttpKind)
}
```

Case: [http/http-kind-arm-binding.kz](../corpus/http/http-kind-arm-binding.kz)
```kurz
int | string v = 3

match v {
    int HttpKind => print(HttpKind)
    string s => print(s)
}
```

Case: [http/http-name-as-member.kz](../corpus/http/http-name-as-member.kz)
```kurz
enum Error { HttpError, Io }

data Reply(int HttpKind)

class Server(int HttpError) {
    pub static int HttpKind = 2

    pub int Code() => HttpError

    pub int HttpServer() => HttpError + 1
}

print(Error.HttpError)
print(Server(7).Code())
print(Server(2).HttpServer())
print(Server.HttpKind)
print(Reply(4).HttpKind)
```

### W2 (decided, §12) `HttpError` and `HttpKind`

A failure of HTTP is a case of its own, `data HttpError(HttpKind Kind, string Detail)`, beside a
failure of the network, which stays an `IoError` (S1), neither wrapped in the other (§12, the owner,
2026-10-31, "HttpError + record's rule" and "IoError + HttpError"), and stays a `data` type in
`std/` (§12, the owner, 2026-11-02, Q15 (a), against `HttpError` as a class). Its kinds are
`enum HttpKind { Protocol, TooLarge, Status, Unsupported }`. An `HttpError` prints its detail alone,
`malformed response: no status line`, with no words of its kind (§12, the owner, 2026-11-01, Q4 (a)).
It gives that text through a `Text()` that `std/` declares in its body, as a `data` type declares its
text (A5) (§12, the owner, 2026-11-02). `std/` declares `HttpError` without that body, since the
compiler reads no body of a `data` type (D16) and `std/` compiles with every program, so a build says
`not supported` to the text of an `HttpError` and of a value of a type whose text can hold an
`HttpError`'s text, a list, a union, a map or a `data` type with one among its items, its cases, its
keys and values or its fields, whatever the value holds when the program runs, a class whose own
`Text()` reads none being no such type, at the expression where
`print`, a hole of an interpolated string (L6) or `string.Join` (X6) takes it (§12, the owner,
2026-11-02, the design standing) *(assumed: the values and the expressions named, as §12 marks
them)*, and to a call of that `Text()`, `e.Text()`, at the call, with the same words (§12, the
owner, 2026-11-02, against `unknown-name`, which says that the method does not exist, and against a
`Text()` that builds by reading the body, the mechanism Q15 (a) set aside).

Case: [http/http-error-kind.kz](../corpus/http/http-error-kind.kz)

Case: [http/http-error-text.kz](../corpus/http/http-error-text.kz)
```kurz
print(HttpError(HttpKind.Protocol, "malformed response: no status line"))
```

Case: [http/http-error-text-call.kz](../corpus/http/http-error-text-call.kz)
```kurz
e = HttpError(HttpKind.Protocol, "malformed response: no status line")
print(e.Text())
```

### W4 (decided, §12) `HttpHeaders`

`HttpHeaders` is an ordered list of names and values, a class of `std/`: `h.Get(name)` gives the
first value of the name, a `string?`, `h.GetAll(name)` gives every value, and `h.Add(name, value)`,
`h.Set(name, value)` and `h.Remove(name)` change it. A name compares without regard to ASCII case
and is written back as it was spelled, and repeated headers, `Set-Cookie`, stay apart, as hyper's
`HeaderMap` and ASP.NET Core's `IHeaderDictionary` keep them (§12, the owner, 2026-10-31, "Ordered
list type"). `GetAll` gives a `List<string>`, `Set` replaces every value of the name by one, as
ASP.NET Core's indexer does, and `Remove` removes every value of it *(assumed, §12: proposed with the
product pass and not put to the owner)*. A program walks the headers by `h.Count`, `h.NameAt(i)` and
`h.ValueAt(i)`, and by `for name, value in h`, which walks them in their order as two names walk a
map's entries; the `for` is a pattern (C6), as C#'s `foreach` takes any type with
`GetEnumerator()`, so that any class with `Count`, `NameAt(i)` and `ValueAt(i)` is walked with two
names (§12, the owner, 2026-11-02, #110, comment 6573113786, and its form, comment 6573265513).

Case: [http/http-headers.kz](../corpus/http/http-headers.kz)
```kurz
h = HttpHeaders()
h.Add("Host", "example.com")
h.Add("Set-Cookie", "a=1")
h.Add("set-cookie", "b=2")
print(h.Get("HOST") ?? "none")
print(h.Get("Accept") ?? "none")
print(h.GetAll("SET-COOKIE"))
h.Set("Set-Cookie", "c=3")
print(h.GetAll("set-cookie"))
h.Remove("host")
print(h.Get("Host") ?? "gone")
```

### W5 (decided, §12) Reading a request

`HttpRequest.Read(stream)` reads the next request of a `TcpStream`, its head whole through the
stream's buffer (S24) and its body left in the stream for `r.Body` (W6), and gives the request, a
failure of the network as an `IoError`, or a request the server refuses as an `HttpError`, the
parser alone beside `Serve` (§12, the owner, 2026-10-31, "+ raw parser layer" and "HttpError +
record's rule"). `r.Method` is the method as sent, compared exactly, `r.Path` and `r.Query` the target
split at its first `?`, raw, and `r.Headers` each header as sent, its value without the spaces and
tabs around it (W4) (§12, assumed with the product passes of 2026-10-31 and 2026-11-03). A refusal
is of the kind `Protocol` where the server answers `400`, `TooLarge` where it answers `413`, `414`
or `431`, and `Unsupported` where it answers `501`, `505` or `417`, for the causes §12 lists with
each status (the owner, 2026-10-31, "Status line only", and 2026-11-03, "`HttpKind` keeps the
record's four kinds"), each with a text that names the cause and quotes no value of the request,
`malformed request: header without a colon`, `request line too long` or `unsupported HTTP version`
(§12, the owner, 2026-11-03, "The server's error texts mirror the client's" and "The refusals that
had no text take the kind `Unsupported`"). In a build for Windows the call is `not supported` at the
line of the program that makes it, though the read of the socket lies in `std/` (§12, "Loop not
supported"). *(assumed, §12, not put to the owner: a stream that ends before its request line is
`malformed request: no request line`, which the server answers with nothing; a line ended by a LF
alone is refused, as RFC 9112, section 2.2, lets a server refuse it; and a valid `Host` is the host
of RFC 3986 with an optional port)*

Case: [http/request-read.kz](../corpus/http/request-read.kz)
```kurz
// the request a client sends, as a server reads it from its connection: the method, the path, the query after the first `?`,
// and the headers, each as the client sent it, a value without the spaces and tabs around it
void | IoError Show(string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    match HttpRequest.Read(server) {
        HttpRequest r => print("{r.Method} {r.Path} [{r.Query}] Host={r.Headers.Get("host") ?? "none"} Accept={r.Headers.Get("Accept") ?? "none"}")
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

void | IoError All() {
    Show("GET /a?x=1 HTTP/1.1\r\nHost: example.com\r\nAccept: */*\r\n\r\n")
    Show("GET /a? HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET /a HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("OPTIONS * HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET http://h/a?b HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET / HTTP/1.0\r\n\r\n")
    Show("BREW /pot HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET / HTTP/1.1\r\nHost: h\r\nAccept: \t a, b \t\r\n\r\n")
}

All() else {
    IoError e => print(e)
}
```

Case: [http/request-refused.kz](../corpus/http/request-refused.kz)
```kurz
// a head the server cannot serve is an `HttpError` of the kind `Protocol`, which the server answers `400`; its text names what
// was wrong and quotes no value of the request
void | IoError Show(Bytes request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request)
    client.ShutdownWrite()
    match HttpRequest.Read(server) {
        HttpRequest r => print("read {r.Method} {r.Path}")
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

// a request with the byte 255, which no UTF-8 text holds, between two pieces
Bytes Split(string before, string after) {
    mut bytes = before.Bytes
    bytes.Add(255)
    bytes.AddRange(after.Bytes)
    return bytes
}

void | IoError All() {
    Show("".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\n".Bytes)
    Show(" / HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Show("GET /\r\nHost: h\r\n\r\n".Bytes)
    Show("G(T / HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\nHost: h\n\n".Bytes)
    Show("CONNECT h:443 HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nAccept: */*\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: a b\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\n X: y\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\nNoColon\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost : h\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\nX: a\u0000b\r\n\r\n".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: +5\r\n\r\nhello".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nhello".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nTransfer-Encoding: chunked\r\n\r\nhello".Bytes)
    Show(Split("GET /", " HTTP/1.1\r\nHost: h\r\n\r\n"))
    Show(Split("GET / HTTP/1.1\r\nHost: h\r\nX: ", "\r\n\r\n"))
}

All() else {
    IoError e => print(e)
}
```

Case: [http/request-refused-unsupported.kz](../corpus/http/request-refused-unsupported.kz)
```kurz
// what the server answers `505`, `501` and `417` is an `HttpError` of the kind `Unsupported`, and what it answers `413`, `414` and
// `431` one of the kind `TooLarge`, with the limits of `HttpLimits()`: a declared body of 30000000 bytes, a request line of 8192
// bytes with its line end, 100 headers and 32768 bytes of them
void | IoError Show(string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    client.ShutdownWrite()
    match HttpRequest.Read(server) {
        HttpRequest r => print("read {r.Method} {r.Path}")
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

void | IoError All() {
    Show("GET / HTTP/2.0\r\nHost: h\r\n\r\n")
    Show("GET / HTTP/1.2\r\nHost: h\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked, gzip\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nExpect: 200-ok\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 30000001\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 99999999999999999999999\r\n\r\n")
    mut line = "GET /"
    while line.Bytes.Count < 8180 {
        line = "{line}aaaaaaaaaa"
    }

    Show("{line} HTTP/1.1\r\nHost: h\r\n\r\n")
    mut many = "GET / HTTP/1.1\r\nHost: h\r\n"
    for i in 0..<100 {
        many = "{many}X-{i}: v\r\n"
    }

    Show("{many}\r\n")
    mut big = "GET / HTTP/1.1\r\nHost: h\r\n"
    while big.Bytes.Count < 33000 {
        big = "{big}X-Big: vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv\r\n"
    }

    Show("{big}\r\n")
}

All() else {
    IoError e => print(e)
}
```

### W6 (decided, §12) The body of a request

`r.Body` is an `HttpBody`: `Read(mut into, max)` appends at most `max` bytes of the body and gives
their count, `0` at the end of the body, `ReadAll(max)` gives the whole body, and `ReadAllText(max)`
gives it as a `string`, each a failure of HTTP as an `HttpError` (§12, the owner, 2026-10-31,
"HttpError + record's rule", and 2026-11-01, Q2 (a)). A body is a declared `Content-Length`, chunks,
or none, and once it is read the stream holds the next request of the connection. The first read
of a body whose client sent `Expect: 100-continue` sends `HTTP/1.1 100 Continue` before it, so that
a server that refuses the request without reading the body sends none (§12, "Status line only").
*(assumed, §12, not put to the owner: a chunk's extensions and the trailer lines dropped, a chunk's
size line of at most `MaxChunkSizeLine` bytes, `ReadAllText`'s `InvalidUtf8` for a body that is not
UTF-8, chunks held to `MaxRequestBodySize` as they are read, as a declared length is held before,
`ReadAll(max)` refusing a body over `max` with `request body over limit`, a failure of HTTP staying,
every read after it giving it again, and `100 Continue` sent to a request of HTTP/1.1 alone, and to
none whose body is empty)*

Case: [http/request-body.kz](../corpus/http/request-body.kz)
```kurz
// the body of a request is read from its stream as the client sends it: a declared length, chunks with their extensions and
// trailer lines dropped, or none; the stream then holds the next request of the connection
void | IoError | HttpError Body(TcpStream server) {
    r = HttpRequest.Read(server)
    body = r.Body.ReadAll(1000)
    text = string.FromUtf8(body) else {
        InvalidUtf8 => "?"
    }

    print("{r.Method} {r.Path}: {body.Count} [{text}]")
}

void | IoError | HttpError All() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("POST /len HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhelloGET /next HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Body(server)
    Body(server)
    client.Write("POST /chunks HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: Chunked\r\n\r\n5\r\nhello\r\n6;name=value\r\n world\r\n0\r\nX-Trailer: 1\r\n\r\n".Bytes)
    client.Write("GET /after HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Body(server)
    Body(server)
    client.Write("GET /none HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Body(server)

    // a read appends at most what it was asked for, and gives 0 at the end, again and again
    client.Write("POST /parts HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhello".Bytes)
    parts = HttpRequest.Read(server)
    mut got = Bytes()
    p1 = parts.Body.Read(mut got, 2)
    p2 = parts.Body.Read(mut got, 2)
    p3 = parts.Body.Read(mut got, 2)
    p4 = parts.Body.Read(mut got, 2)
    p5 = parts.Body.Read(mut got, 2)
    print("{p1} {p2} {p3} {p4} {p5}")

    // the whole body over the bound of `ReadAll`
    client.Write("POST /big HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhello".Bytes)
    big = HttpRequest.Read(server)
    match big.Body.ReadAll(4) {
        Bytes b => print(b.Count)
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }

    // the whole body as text, and a body that is no UTF-8
    client.Write("POST /text HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\n\r\nhi".Bytes)
    mut bad = "POST /bad HTTP/1.1\r\nHost: h\r\nContent-Length: 1\r\n\r\n".Bytes
    bad.Add(255)
    client.Write(bad)
    for i in 0..<2 {
        r = HttpRequest.Read(server)
        match r.Body.ReadAllText(10) {
            string t => print(t)
            IoError e => print(e)
            HttpError e => print("{e.Kind}: {e.Detail}")
            InvalidUtf8 => print("not UTF-8")
        }
    }
}

All() else {
    IoError e => print(e)
    HttpError e => print("{e.Kind}: {e.Detail}")
}
```

Case: [http/request-body-errors.kz](../corpus/http/request-body-errors.kz)
```kurz
// a body the client breaks off, or chunks that are not chunks, is an `HttpError`, and every read after it gives it again
// the chunks' failures, each read once more after it: a chunk over the limit of the body stays `TooLarge`, though the line that
// follows it, `hello`, is no size
void | IoError | HttpError Show(string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    client.ShutdownWrite()
    r = HttpRequest.Read(server)
    match r.Body.ReadAll(100) {
        Bytes b => print(b.Count)
        IoError e => print(e)
        HttpError e => {
            mut got = Bytes()
            match r.Body.Read(mut got, 100) {
                int n => print("{e.Kind}: {e.Detail}, then {n}")
                IoError again => print(again)
                HttpError again => print("{e.Kind}: {e.Detail}, then {again.Kind}: {again.Detail}")
            }
        }
    }
}

void | IoError | HttpError All() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nhello".Bytes)
    client.ShutdownWrite()
    r = HttpRequest.Read(server)
    mut got = Bytes()
    n = r.Body.Read(mut got, 100)
    match r.Body.Read(mut got, 100) {
        int m => print(m)
        IoError e => print(e)
        HttpError e => print("{n} {e.Kind}: {e.Detail}")
    }

    match r.Body.Read(mut got, 100) {
        int m => print(m)
        IoError e => print(e)
        HttpError e => print("again {e.Kind}: {e.Detail}")
    }

    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\nhello\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhelloX\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n2000000\r\nhello\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhel")
}

All() else {
    IoError e => print(e)
    HttpError e => print("{e.Kind}: {e.Detail}")
}
```

Case: [http/request-continue.kz](../corpus/http/request-continue.kz)
```kurz
// a client that sends `Expect: 100-continue` waits for the server's `100 Continue` before it sends the body; the body's first read
// sends it, so a server that refuses the request without reading its body sends none; HTTP/1.0 and a body of no bytes get none
string | IoError Waiting(TcpStream client) {
    client.SetReadTimeout(50ms)
    mut got = Bytes()
    match client.Read(mut got, 100) {
        int n => {
            text = string.FromUtf8(got) else {
                InvalidUtf8 => "?"
            }

            return text.Trim()
        }

        IoError e => return "{e.Kind}"
    }
}

void | IoError | HttpError Expecting(string label, string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    r = HttpRequest.Read(server)
    if label == "1.1" {
        before = Waiting(client)
        print("before: {before}")
    }

    body = r.Body.ReadAll(100)
    if body.Count > 0 {
        text = string.FromUtf8(body) else {
            InvalidUtf8 => "?"
        }

        print(text)
    }

    after = Waiting(client)
    if label == "1.1" {
        print("after: {after}")
    } else {
        print("{label}: {after}")
    }
}

void | IoError | HttpError All() {
    Expecting("1.1", "POST / HTTP/1.1\r\nHost: h\r\nExpect: 100-Continue\r\nContent-Length: 2\r\n\r\nhi")
    Expecting("1.0", "POST / HTTP/1.0\r\nExpect: 100-continue\r\nContent-Length: 2\r\n\r\nhi")
    Expecting("no body", "POST / HTTP/1.1\r\nHost: h\r\nExpect: 100-continue\r\nContent-Length: 0\r\n\r\n")
}

All() else {
    IoError e => print(e)
    HttpError e => print("{e.Kind}: {e.Detail}")
}
```

### W7 (decided, §12) `HttpLimits`

The limits of a request are a `data HttpLimits` with the names and the defaults of Kestrel's
`KestrelServerLimits`: a request line of at most 8192 bytes with its line end, header lines of at
most 32768 bytes in all with theirs, at most 100 headers, a declared body of at most 30000000
bytes, a head that arrives within 30 seconds, and a body and a response taken at 240 bytes a second
after a grace of 5 seconds, each rate an `HttpRate(int BytesPerSecond, duration Grace)` (§12, the
owner, 2026-10-31, "HttpLimits data", 2026-11-01, "Kestrel's bounds" and "Add Kestrel's response
rate", and 2026-11-03), and 100 requests to a connection, an idle connection closed after 5 seconds,
64 KiB of a body drained and a chunk's size line of 4096 bytes (§12, the owner, 2026-11-03).
`HttpRequest.Read(stream, limits)` reads with other limits (§12, assumed: `ServeConnection` takes
them in a second form too). *(assumed, §12, not put to the owner: the lines counted as Kestrel
counts them, the request line with its CR LF against `MaxRequestLineSize` and the header lines with
theirs against `MaxRequestHeadersTotalSize` and 2 more bytes for the empty line, as Kestrel's
`Http1Connection` begins each request with `MaxRequestHeadersTotalSize + 2` bytes allowed)*

Case: [http/http-limits.kz](../corpus/http/http-limits.kz)
```kurz
// the bounds a server holds a connection to, each with the default ASP.NET Core's Kestrel gives it, and a rate a slow client is
// held to after its grace
d = HttpLimits()
print("{d.MaxRequestLineSize} {d.MaxRequestHeadersTotalSize} {d.MaxRequestHeaderCount} {d.MaxRequestBodySize} {d.RequestHeadersTimeout}")
print("{d.MinRequestBodyDataRate.BytesPerSecond} {d.MinRequestBodyDataRate.Grace} {d.MinResponseDataRate.BytesPerSecond} {d.MinResponseDataRate.Grace}")
print("{d.MaxRequestsPerConnection} {d.KeepAliveTimeout} {d.MaxDrainSize} {d.MaxChunkSizeLine}")
mine = HttpLimits(MaxRequestBodySize: 1mb)
print("{mine.MaxRequestBodySize} {mine.MaxRequestHeadersTotalSize}")
```

Case: [http/request-limits.kz](../corpus/http/request-limits.kz)
```kurz
// a request read with limits of the program's own: a request line of at most 19 bytes with its CR LF, at most 2 header lines of at
// most 40 bytes in all with their CR LFs, and a body of at most 5 bytes, declared or in chunks
void | IoError Show(string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    client.ShutdownWrite()
    small = HttpLimits(MaxRequestLineSize: 19, MaxRequestHeadersTotalSize: 40, MaxRequestHeaderCount: 2, MaxRequestBodySize: 5)
    match HttpRequest.Read(server, small) {
        HttpRequest r => {
            match r.Body.ReadAll(100) {
                Bytes b => {
                    if b.Count > 0 {
                        print("{r.Method} {r.Path}: {b.Count}")
                    } else {
                        print("{r.Method} {r.Path}")
                    }
                }

                IoError e => print(e)
                HttpError e => print("{e.Kind}: {e.Detail}")
            }
        }

        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

void | IoError All() {
    Show("GET /abcd HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET /abc HTTP/1.1\r\nHost: h\r\nA: 1\r\n\r\n")
    Show("GET /abc HTTP/1.1\r\nHost: h\r\nA: 1\r\nB: 2\r\n\r\n")
    Show("GET /abc HTTP/1.1\r\nHost: h\r\nA: 123456789012345678901234567890\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 6\r\n\r\nhello!")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n1\r\n!\r\n0\r\n\r\n")
}

All() else {
    IoError e => print(e)
}
```
