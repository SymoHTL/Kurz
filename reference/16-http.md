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
`HttpError`'s text, a list, a union, a nullable form, a map or a `data` type with one among its
items, its cases, its value, its keys and values or its fields, whatever the value holds when the
program runs, a class whose own `Text()` reads none being no such type, at the expression where
`print`, a hole of an interpolated string (L6) or `string.Join` (X6) takes it (§12, the owner,
2026-11-02, the design standing), so that the text of an HTTP outcome,
`string | IoError | HttpError` among them, is `not supported` whatever case it holds, until a
`match` gives each case its own arm, and an empty `List<HttpError>` and a null `HttpError?` are
refused as well (§12, the owner, 2026-11-03, Q2 (a))
*(assumed: the values and the expressions named, as §12 marks them)*, and to a call of that
`Text()`, `e.Text()`, at the call, with the same words (§12, the
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

### W3 (decided, §12) A server's request sent to another actor moves with its body

A server's `HttpRequest` sent to another actor moves there with its body, `r.Body`, a stream on the
connection's socket: the handler keeps neither, and the connection waits for the handler's
response, as Rust moves a `hyper::Request<Incoming>` to another task (§12, the owner, 2026-11-02,
issue 110, comment 6573113786, against refusing a server's request that has a body as not
sendable). The send is written with `move`, as the send of a mutable class instance is (§12, the
owner, 2026-11-03, issue 110, comment 6600034158). The response is written when the handler
returns. The moved body stays the other actor's to read to its end: every read gives the body's
bytes, then `0` at its end. A body left unread is drained when its last owner drops it, and the
connection reads its next request only after that, with no new kind of error (§12, the owner,
2026-11-03, issue 39, comment 6601737306). A `move` of the request ends every path into it,
`b = r.Body` included, by the region check of the record's section 6 (§12). A read of a moved body
that falls under the body's rate gives `IoError` with `IoKind.TimedOut` and the connection closes;
the `408` is sent only while no response has begun (§12, the owner, 2026-11-03, issue 156, comment
6603205575). A `copy` of a server's request is the compile error `copy-server-request`, which names
`move`, and a send of it with no word is section 6's error (§12, the same answer) *(assumed, §12:
the error's name)*. A request a client builds is sent as any value is (§12).

No case: a case runs one actor, and a send to another actor needs two, which the compiler does not
build, so that the move is not built either. No case expects `copy-server-request` for the same
reason, so that the id stands in this rule and not in [12-errors.md](12-errors.md), whose row
comes with the case that expects it (§12, the owner, 2026-11-03, issue 156, comment 6603359338).

### W4 (decided, §12) `HttpHeaders`

`HttpHeaders` is an ordered list of names and values, a class of `std/`: `h.Get(name)` gives the
first value of the name, a `string?`, `h.GetAll(name)` gives every value, and `h.Add(name, value)`,
`h.Set(name, value)` and `h.Remove(name)` change it. A name compares without regard to ASCII case
and is written back as it was spelled, and repeated headers, `Set-Cookie`, stay apart (§12, the owner, 2026-10-31, "Ordered list type").
hyper's `HeaderMap` keeps the values of a name apart and in their order but lowercases the name, and
ASP.NET Core's `IHeaderDictionary` holds the values of a name as one `StringValues`, so that neither
keeps one list of every name and value with each name's spelling (§12). `GetAll` gives a
`List<string>`, `Set` replaces every value of the name by one, as ASP.NET Core's indexer does, and
`Remove` removes every value of it *(assumed, §12: proposed with the product pass and not put to the
owner)*. `h.Add(name, value)` and `h.Set(name, value)` give `void | HttpError`, since a header's
value often comes from outside input, and a CR or a LF in the name or the value is
`HttpError(Protocol, "invalid header: CR or LF")`, refused where it is set, as Rust's `HeaderValue`
and Kestrel's header dictionary refuse it (§12, the owner, 2026-11-02, Q5, #110, comment
6573113786, its form, comment 6573265513, and its text, 2026-11-03, comment 6613356831). `Add` and
`Set` take an empty name and refuse a CR or a LF alone (§12, the owner, 2026-11-03, #110, comment
6615254370). The class
is made by `HttpHeaders()`, which holds no header; its members are `string? Get(string name)`,
`List<string> GetAll(string name)`, `void | HttpError Add(string name, string value)`,
`void | HttpError Set(string name, string value)` and `void Remove(string name)`; a refused `Add` or
`Set` changes nothing, `Set` checking before it removes; `Set` puts its value after every value the
headers hold, as a `Remove` and then an `Add`; `Remove` of a name the headers do not hold changes
nothing; an empty value is taken; a name is written back as it was spelled in the head that
a writer of a request or a response writes; and `print(h)` gives the text of a class without a
`Text()` (A5) *(assumed, §12: readings of `std/`'s class not put to the owner, the constructor's
empty start and the result of `Remove` among them)*. A program walks the headers by `h.Count`, `h.NameAt(i)` and
`h.ValueAt(i)`, and by `for name, value in h`, which walks them in their order as two names walk a
map's entries; the `for` is a pattern (C6), as C#'s `foreach` takes any type with
`GetEnumerator()`, so that any class with `Count`, `NameAt(i)` and `ValueAt(i)` is walked with two
names (§12, the owner, 2026-11-02, #110, comment 6573113786, and its form, comment 6573265513).
`h.Count` is the read-only property `int Count` (K7), and `h.NameAt(i)` and `h.ValueAt(i)` give a
`string` and, with `i` outside 0 to `Count - 1`, stop the program with `index-out-of-range`, as a
list's index does (T14) (§12, the owner, 2026-11-03, #110, comment 6615254370).

Case: [http/http-headers.kz](../corpus/http/http-headers.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Host", "example.com")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
print(h.Get("HOST") ?? "none")
print(h.Get("host") ?? "none")
print(h.Get("Accept") ?? "none")
```

Case: [http/http-headers-get-all.kz](../corpus/http/http-headers-get-all.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Set-Cookie", "a=1")
    headers.Add("Host", "example.com")
    headers.Add("set-cookie", "b=2")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
print(h.GetAll("SET-COOKIE"))
print(h.GetAll("Accept"))
```

Case: [http/http-headers-set.kz](../corpus/http/http-headers-set.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Set-Cookie", "a=1")
    headers.Add("set-cookie", "b=2")
    headers.Set("SET-COOKIE", "c=3")
    headers.Set("Content-Type", "text/plain")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
print(h.GetAll("set-cookie"))
print(h.Get("content-type") ?? "none")
```

Case: [http/http-headers-remove.kz](../corpus/http/http-headers-remove.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Set-Cookie", "a=1")
    headers.Add("Host", "example.com")
    headers.Add("set-cookie", "b=2")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
h.Remove("SET-COOKIE")
h.Remove("Accept")
print(h.GetAll("set-cookie"))
print(h.Get("host") ?? "none")
```

Case: [http/http-headers-refused.kz](../corpus/http/http-headers-refused.kz)
```kurz
h = HttpHeaders()
h.Add("X-Name", "a\r\nInjected: 1") else {
    HttpError e => print("{e.Kind}: {e.Detail}")
}
h.Add("X-Na\nme", "a") else {
    HttpError e => print("{e.Kind}: {e.Detail}")
}
h.Add("Set-Cookie", "a=1") else {
    HttpError e => print("{e.Kind}: {e.Detail}")
}
h.Set("Set-Cookie", "b=2\r") else {
    HttpError e => print("{e.Kind}: {e.Detail}")
}
print(h.Get("X-Name") ?? "none")
print(h.GetAll("Set-Cookie"))
```

Case: [http/http-headers-walk.kz](../corpus/http/http-headers-walk.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Host", "example.com")
    headers.Add("Set-Cookie", "a=1")
    headers.Add("set-cookie", "b=2")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
// `Count` is read without a call; each name keeps its spelling, and a repeated name stays apart, in the order it came
print(h.Count)
for i in 0..<h.Count {
    print(h.NameAt(i))
    print(h.ValueAt(i))
}
```

Case: [http/http-headers-set-order.kz](../corpus/http/http-headers-set-order.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("Host", "a")
    headers.Add("Accept", "text/plain")
    headers.Set("host", "b")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
// `Set` takes out every value of the name and puts its value after every value the headers hold
print(h.NameAt(0))
print(h.NameAt(1))
print(h.ValueAt(1))
```

Case: [http/http-headers-empty-name.kz](../corpus/http/http-headers-empty-name.kz)
```kurz
void | HttpError Fill(HttpHeaders headers) {
    headers.Add("", "x")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
// an empty name is taken, as std/'s class took it; a CR or a LF alone is refused
print(h.Get("") ?? "none")
print(h.Count)
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

### W8 (decided, §12) Writing a response

A response is made by a function for each kind of body, `HttpResponse.Text(200, "hi")`,
`HttpResponse.Bytes(200, b)`, `HttpResponse.File(200, path)` and `HttpResponse.Empty(204)`, and the
rest is set through `response.Headers` (W4) (§12, the owner, 2026-11-01, Q1 (a)). Each sets the
`Content-Type` ASP.NET Core's `Results` sets, `text/plain; charset=utf-8` for `Text`,
`application/octet-stream` for `Bytes` and `File`, and none for `Empty`, which a handler replaces with
`Set` (§12, the owner, 2026-11-03). A status outside 100 to 599 stops the program with a run-time
error (§12, the owner, 2026-11-02, Q5). `response.Write(stream)` writes the status line, the headers
and the `Content-Length` of the body, which a `File` takes from its size when it is written, and then
the body, and gives `void | IoError`; `response.WriteHead(stream)` writes the same head without the
body, for a `HEAD` (§12, the owner, 2026-11-02, Q10 and Q13). Neither writes `Date` or `Connection`,
which the server's loop adds (§12, assumed). A `1xx`, a `204` and a `304` get no body (§12, assumed),
and an empty `205` is written with `Content-Length: 0` (§12, the owner, 2026-11-02, Q14). A response
`Write` cannot write, a `205` with a body or a `Content-Length` its body contradicts, is answered
`500 Internal Server Error` with an empty body and `Connection: close`, raising nothing (§12, the
owner, 2026-11-02, Q5 and Q14, and assumed). A `Transfer-Encoding` the handler sets is kept, `chunked`
added as its last coding where it does not end in it, and the body is chunked, a coding it names
being announced and not applied; beside a `Content-Length` it gets no answer (§12, the owner,
2026-11-03). A status RFC 9110 and RFC 6585 name no reason phrase for is written with an empty one
(§12, assumed). In a build for Windows `Write` and `WriteHead` are `not supported` at the line of the
program that calls them (§12, "Loop not supported"). *(assumed, §12, not put to the owner: the
headers written in the order they were set, the `Content-Type` first, and the `Content-Length` of
the body after them where the handler set none; a handler's `Content-Length` contradicted when it is
no number or another number than the body's length; no `Content-Length` for a `1xx`; a file whose
size cannot be read answered with that `500`, `Write` giving the `IoError` of the read; a
`Transfer-Encoding` beside a `Content-Length` giving an `IoError` of the kind `InvalidInput`, nothing
written; and a file whose size changes while it is sent giving one of the kind `InvalidData`)*

Case: [http/response-write.kz](../corpus/http/response-write.kz)
```kurz
// a response is written as its status line, its headers in the order they were set, the `Content-Length` of its body and the
// body; each function that makes one sets the `Content-Type` ASP.NET Core's `Results` sets, which a handler replaces with `Set`;
// a file's length is taken when the response is written, so that the file may be made after the response
void | IoError Wire(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.Write(server)
    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    print(text.Replace("\r\n", "|"))
}

void | IoError | HttpError All() {
    Wire(HttpResponse.Text(200, "hi"))
    Wire(HttpResponse.Bytes(404, "ab".Bytes))
    file = HttpResponse.File(201, "response-write.txt")
    File.WriteAll("response-write.txt", "file body".Bytes)
    Wire(file)
    File.Delete("response-write.txt")
    Wire(HttpResponse.Empty(202))
    html = HttpResponse.Text(200, "<p/>")
    html.Headers.Set("Content-Type", "text/html")
    html.Headers.Add("X-Id", "7")
    Wire(html)
    Wire(HttpResponse.Empty(299))
    print(HttpResponse.Empty(301).Status)
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

Case: [http/response-head.kz](../corpus/http/response-head.kz)
```kurz
// `WriteHead` writes the head alone, with the `Content-Length` the body would have, the answer to a `HEAD`
void | IoError Head(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.WriteHead(server)
    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    print(text.Replace("\r\n", "|"))
}

void | IoError All() {
    Head(HttpResponse.Text(200, "hello"))
    File.WriteAll("response-head.txt", "file body".Bytes)
    Head(HttpResponse.File(200, "response-head.txt"))
    File.Delete("response-head.txt")
}

All() else {
    IoError e => print(e)
}
```

Case: [http/response-bodiless.kz](../corpus/http/response-bodiless.kz)
```kurz
// a `1xx`, a `204` and a `304` have no body, so neither a body nor its length nor a `Transfer-Encoding` is written; an empty `205`
// says `Content-Length: 0`
void | IoError Wire(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.Write(server)
    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    print(text.Replace("\r\n", "|"))
}

void | IoError | HttpError All() {
    Wire(HttpResponse.Empty(204))
    Wire(HttpResponse.Text(304, "dropped"))
    Wire(HttpResponse.Empty(100))
    Wire(HttpResponse.Empty(205))
    coded = HttpResponse.Text(204, "dropped")
    coded.Headers.Set("Transfer-Encoding", "gzip")
    Wire(coded)
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

Case: [http/response-unwritable.kz](../corpus/http/response-unwritable.kz)
```kurz
// a response the writer cannot write, a `205` with a body or a `Content-Length` its body contradicts, is answered `500` with no
// body and a close, and the write gives nothing more; a file that cannot be read is answered so too, and the write gives its
// failure
void | IoError Wire(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.Write(server) else {
        IoError e => print(e)
    }

    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    print(text.Replace("\r\n", "|"))
}

void | IoError | HttpError All() {
    Wire(HttpResponse.Text(205, "x"))
    cut = HttpResponse.Text(200, "hello")
    cut.Headers.Set("Content-Length", "4")
    Wire(cut)
    words = HttpResponse.Text(200, "hello")
    words.Headers.Set("Content-Length", "five")
    Wire(words)
    same = HttpResponse.Text(200, "hello")
    same.Headers.Set("Content-Length", " 5")
    Wire(same)
    Wire(HttpResponse.File(200, "response-unwritable.txt"))
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

Case: [http/response-transfer-encoding.kz](../corpus/http/response-transfer-encoding.kz)
```kurz
// a `Transfer-Encoding` the handler sets is kept as it set it, `chunked` added as its last coding unless it is, and the body is
// chunked, a coding it names being announced and not applied; beside a `Content-Length` it is a response that gets no answer
void | IoError Wire(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.Write(server) else {
        IoError e => print(e.Kind)
    }

    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    shown = text.Replace("\r\n", "|")
    print("[{shown}]")
}

void | IoError | HttpError All() {
    gzip = HttpResponse.Text(200, "hello")
    gzip.Headers.Set("Transfer-Encoding", "gzip")
    Wire(gzip)
    empty = HttpResponse.Text(200, "")
    empty.Headers.Set("Transfer-Encoding", "Chunked")
    Wire(empty)
    both = HttpResponse.Text(200, "hello")
    both.Headers.Set("Transfer-Encoding", "chunked")
    both.Headers.Set("Content-Length", "5")
    Wire(both)
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

Case: [http/response-large.kz](../corpus/http/response-large.kz)
```kurz
// a body over 64 KiB goes out whole, and a file in pieces of 64 KiB, each a chunk where the body is chunked; the wire is printed
// with each CR LF as `|` and each run of the byte `z` as its length in brackets
void | IoError Wire(HttpResponse r) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    r.Write(server)
    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    mut shown = Bytes()
    mut run = 0
    for b in got {
        if b == 122 {
            run = run + 1
            continue
        }

        if run > 0 {
            shown.AddRange("[{run}]".Bytes)
            run = 0
        }

        shown.Add(b)
    }

    if run > 0 {
        shown.AddRange("[{run}]".Bytes)
    }

    text = string.FromUtf8(shown) else {
        InvalidUtf8 => "?"
    }

    print(text.Replace("\r\n", "|"))
}

Bytes Repeated(byte value, int count) {
    mut b = Bytes()
    while b.Count < count {
        b.Add(value)
    }

    return b
}

void | IoError | HttpError All() {
    body = Repeated(122, 70000)
    text = string.FromUtf8(body) else {
        InvalidUtf8 => ""
    }

    Wire(HttpResponse.Text(200, text))
    chunked = HttpResponse.Text(200, text)
    chunked.Headers.Set("Transfer-Encoding", "chunked")
    Wire(chunked)
    File.WriteAll("response-large.txt", body)
    Wire(HttpResponse.File(200, "response-large.txt"))
    file = HttpResponse.File(200, "response-large.txt")
    file.Headers.Set("Transfer-Encoding", "chunked")
    Wire(file)
    File.Delete("response-large.txt")
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

### W9 (decided, §12) Serving connections

`HttpServer.ServeConnection(stream, handler)` serves every request of one accepted `TcpStream` in
their order with the handler, a function of the type `(HttpRequest) => HttpResponse`, and closes the
stream, and `HttpServer.Serve(listener, handler)` accepts each connection of a `TcpListener` and
serves it so, one at a time (§12, the owner, 2026-10-30, "Handler function", and 2026-10-31, "+ raw
parser layer"). `ServeConnection` gives `void | IoError | HttpError`: `void` where the connection
ended between two requests, the `IoError` of the network, or the `HttpError` of a request the server
refused, whose detail `Serve` drops (§12, the owner, 2026-11-03, "The detail of a refused request
goes to `ServeConnection`'s result"); `Serve` gives `void | IoError`, returning where an `Accept`
fails other than for one connection (§12, assumed). Each takes an `HttpLimits` (W7) in a second form,
a limit of zero or below being an `IoError` of the kind `InvalidInput` that names the member, before a
read or an accept (§12, the owner, 2026-11-02, Q14, and assumed). A connection of HTTP/1.1 stays open
for its next request, and one of HTTP/1.0, one whose request or response says `Connection: close`,
one after a `500`, the writer's own among them, and one after the last request
`MaxRequestsPerConnection` allows closes after the response (§12, the owner, 2026-10-31, "Persistent,
100 req, 5 s", and 2026-11-02, Q11, and assumed). The server writes its `Date` and its
`Connection: close` where the handler set none, and keeps the handler's (§12, the owner, 2026-11-02,
Q10). A `HEAD` is answered with the head alone, as `WriteHead` writes it (§12, the owner, 2026-11-02,
Q13). A body the handler did not read is read and dropped up to `MaxDrainSize`, past which the
connection closes, a request that expects `100-continue` whose body the handler did not read closes it
with no `100 Continue` sent, and a chunked body malformed while it is dropped closes it without an
answer (§12, assumed). A request the server refuses is answered by its status line alone, with an empty
body and `Connection: close`, and the connection closes (§12, the owner, 2026-10-31, "Status line
only"). A handler that throws ends what runs the server, as an exception ends the actor it happens in
(§12). In a build for Windows `Serve` and `ServeConnection` are `not supported` at the line of the
program that calls them (§12, "Loop not supported"). *(assumed, §12, not put to the owner: the
`Date` written once a second, as hyper caches it; a refusal's status line with `Content-Length: 0` and
the server's `Date`, as Kestrel writes it; a handler's `Transfer-Encoding` dropped for a request of
HTTP/1.0 and the body framed by its length; the body the handler left read before the response is
written, so that a body past `MaxDrainSize` is answered with `Connection: close`; the wait for the
first byte of a request after the first bounded by `KeepAliveTimeout`, and the reads of a request by
the 5 seconds of a read on a socket; a connection aborted, reset or refused before `Accept` took it
passed over, as hyper's loop of accepts passes over it; and the response to a request with no body whose
next request's head stands whole in the stream's buffer held, up to 64 KiB, and written with the response
after it in one write, as hyper's `Conn` buffers its writes, so that a client that sends its requests at
once gets their responses at once and not one segment per wait for its acknowledgement, the responses
held going out before a refusal, a `100 Continue` and a handler that may read a body)*

Case: [http/serve-keep-alive.kz](../corpus/http/serve-keep-alive.kz)
```kurz
// a connection of HTTP/1.1 serves its requests in their order and stays open, each response with the server's `Date`, until the
// client ends it between two requests; a connection that ends before its first request is answered with nothing; and the server
// writes its `Date` and its `Connection` beside the handler's response without changing it, so that a response a handler gives
// again is written as it was made, and a `Date` the handler set is kept; a connection whose client is idle after a request closes
// at `KeepAliveTimeout`
// serves `requests`, sent at once and then the client's end, on one connection with `handler` within `limits`, and prints the
// loop's result and what the client got
void Exchange(string requests, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(requests.Bytes) else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    mut result = "void"
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => {
            result = "io: {e}"
        }
        HttpError e => {
            result = "http: {e.Detail}"
        }
    }

    print(result)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits())
Exchange("", hello, HttpLimits())
same = HttpResponse.Text(200, "same")
reused = (HttpRequest r) => same
Exchange("GET /a HTTP/1.0\r\n\r\n", reused, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n", reused, HttpLimits())
dated = (HttpRequest r) => {
    response = HttpResponse.Empty(204)
    response.Headers.Set("Date", "Sun, 06 Nov 1994 08:49:37 GMT") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\n", dated, HttpLimits())
Idle()

// a client that sends one request and then nothing is served, and its connection closes without an answer once it has been idle
// for `KeepAliveTimeout`
void Idle() {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n".Bytes) else {
        IoError e => return
    }

    start = timestamp.Now
    HttpServer.ServeConnection(server, (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}"), HttpLimits(KeepAliveTimeout: 100ms)) else {
        IoError e => print(e)
        HttpError e => print(e.Detail)
    }

    waited = timestamp.Now - start
    print(waited >= 100ms && waited < 2s)
    print(Wire(client))
}
```

Case: [http/serve-close.kz](../corpus/http/serve-close.kz)
```kurz
// a connection closes after the response to a request of HTTP/1.0, to a request or a response that says `Connection: close`, to a
// `500`, the writer's own among them, and to the last request `MaxRequestsPerConnection` allows, and the server writes
// `Connection: close` where the handler set none; a request the client sent after it is left unread; and the writer's own `500`
// to the second of two requests sent at once goes out after the response to the first
// serves `requests`, sent at once and then the client's end, on one connection with `handler` within `limits`, and prints the
// loop's result and what the client got
void Exchange(string requests, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(requests.Bytes) else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    mut result = "void"
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => {
            result = "io: {e}"
        }
        HttpError e => {
            result = "http: {e.Detail}"
        }
    }

    print(result)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
closing = (HttpRequest r) => {
    response = HttpResponse.Text(200, "bye")
    response.Headers.Set("Connection", "close") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

failing = (HttpRequest r) => HttpResponse.Empty(500)
unwritable = (HttpRequest r) => HttpResponse.Text(205, "x")
partly = (HttpRequest r) => {
    if r.Path == "/b" {
        return HttpResponse.Text(205, "x")
    }

    return HttpResponse.Text(200, "hi {r.Path}")
}

two = "GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n"
Exchange("GET /a HTTP/1.0\r\n\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\nConnection: keep-alive, Close\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits())
Exchange(two, closing, HttpLimits())
Exchange(two, failing, HttpLimits())
Exchange(two, unwritable, HttpLimits())
Exchange("{two}GET /c HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits(MaxRequestsPerConnection: 2))
Exchange(two, partly, HttpLimits())
```

Case: [http/serve-refused.kz](../corpus/http/serve-refused.kz)
```kurz
// a request the server refuses is answered by its status line alone, with an empty body, the server's `Date` and a close, and the
// detail is the loop's result; a request after one the server served is refused the same way
// serves `requests`, sent at once and then the client's end, on one connection with `handler` within `limits`, and prints the
// loop's result and what the client got
void Exchange(string requests, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(requests.Bytes) else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    mut result = "void"
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => {
            result = "io: {e}"
        }
        HttpError e => {
            result = "http: {e.Detail}"
        }
    }

    print(result)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
Exchange("GET /a HTTP/1.1\r\n\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/2.0\r\nHost: h\r\n\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\nContent-Length: 6\r\n\r\n", hello, HttpLimits(MaxRequestBodySize: 5))
```

Case: [http/serve-head.kz](../corpus/http/serve-head.kz)
```kurz
// a `HEAD` is answered with the head of the response alone, its `Content-Length` that of the body; to a request of HTTP/1.0 a
// handler's `Transfer-Encoding` is dropped and the body goes with its length
// serves `requests`, sent at once and then the client's end, on one connection with `handler` within `limits`, and prints the
// loop's result and what the client got
void Exchange(string requests, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(requests.Bytes) else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    mut result = "void"
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => {
            result = "io: {e}"
        }
        HttpError e => {
            result = "http: {e.Detail}"
        }
    }

    print(result)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
coded = (HttpRequest r) => {
    response = HttpResponse.Text(200, "hello")
    response.Headers.Set("Transfer-Encoding", "gzip") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

Exchange("HEAD /a HTTP/1.1\r\nHost: h\r\n\r\nGET /b HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits())
Exchange("GET /a HTTP/1.0\r\n\r\n", coded, HttpLimits())
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\n", coded, HttpLimits())
```

Case: [http/serve-drain.kz](../corpus/http/serve-drain.kz)
```kurz
// a body the handler did not read is read and dropped up to `MaxDrainSize`, and the next request served; a body past it closes the
// connection, and so does a request that expects `100-continue` whose body the handler did not read, with no `100 Continue` sent;
// a chunked body malformed while it is dropped closes the connection without an answer; and a response the server holds while the
// next request stands in the buffer goes out before that request's `100 Continue`
// serves `requests`, sent at once and then the client's end, on one connection with `handler` within `limits`, and prints the
// loop's result and what the client got
void Exchange(string requests, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(requests.Bytes) else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    mut result = "void"
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => {
            result = "io: {e}"
        }
        HttpError e => {
            result = "http: {e.Detail}"
        }
    }

    print(result)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
reading = (HttpRequest r) => {
    text = r.Body.ReadAllText(100) else {
        else => "?"
    }

    return HttpResponse.Text(200, text)
}

small = HttpLimits(MaxDrainSize: 4)
next = "GET /b HTTP/1.1\r\nHost: h\r\n\r\n"
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 4\r\n\r\nabcd{next}", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nabcde{next}", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n2\r\nab\r\n0\r\n\r\n{next}", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\n", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\nok", reading, small)
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nPOST /b HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\nok", reading, small)
```

Case: [http/serve-limits.kz](../corpus/http/serve-limits.kz)
```kurz
// a limit of zero or below is an `IoError` of the kind `InvalidInput` that names the member, at `ServeConnection` before it reads
// and at `Serve` before it accepts
void Show(HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.ShutdownWrite() else {
        IoError e => return
    }

    hello = (HttpRequest r) => HttpResponse.Empty(204)
    HttpServer.ServeConnection(server, hello, limits) else {
        IoError e => print(e)
        HttpError e => print(e.Detail)
    }

    HttpServer.Serve(listener, hello, limits) else {
        IoError e => print(e)
    }
}

Show(HttpLimits(MaxRequestLineSize: 0))
Show(HttpLimits(MaxRequestHeadersTotalSize: -1))
Show(HttpLimits(MaxRequestHeaderCount: 0))
Show(HttpLimits(MaxRequestBodySize: 0))
Show(HttpLimits(RequestHeadersTimeout: 0s))
Show(HttpLimits(MinRequestBodyDataRate: HttpRate(0, 5s)))
Show(HttpLimits(MinResponseDataRate: HttpRate(240, -1s)))
Show(HttpLimits(MaxRequestsPerConnection: 0))
Show(HttpLimits(KeepAliveTimeout: 0s))
Show(HttpLimits(MaxDrainSize: 0))
Show(HttpLimits(MaxChunkSizeLine: 0))
```

Case: [http/serve-accept.kz](../corpus/http/serve-accept.kz)
```kurz
// `Serve` accepts each connection and serves it, one at a time, until an `Accept` fails other than for one connection, here at the
// time the listener gives its accepts
void | IoError All() {
    listener = TcpListener.Bind("127.0.0.1:0")
    listener.SetAcceptTimeout(200ms)
    first = TcpStream.Connect("127.0.0.1:{listener.Port}")
    second = TcpStream.Connect("127.0.0.1:{listener.Port}")
    first.Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    first.ShutdownWrite()
    second.Write("GET /b HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    second.ShutdownWrite()
    HttpServer.Serve(listener, (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")) else {
        IoError e => print(e.Kind)
    }

    Body(first)
    Body(second)
}

// the body of the response the client got
void | IoError Body(TcpStream client) {
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    print(text.Split("\r\n\r\n")[1])
}

All() else {
    IoError e => print(e)
}
```

### W10 (decided, §12) The server's bounds of time

The server's loop holds a request's head to `RequestHeadersTimeout` from its first byte and its body
to `MinRequestBodyDataRate` after that rate's grace, and a request that falls under either is answered
`408 Request Timeout` and its connection closes, the handler's response unwritten (§12, the owner,
2026-11-01, "Kestrel's bounds", and assumed), and `ServeConnection` gives the `HttpError` of that refused request,
`HttpError(Protocol, "request timed out")` (§12, the owner, 2026-11-03, "The server's error texts mirror the client's"); it holds a response to `MinResponseDataRate` after its
grace, and under it the connection closes, since its status has gone out (§12, the owner, 2026-11-01,
"Add Kestrel's response rate"); and an idle connection closes without an answer at `KeepAliveTimeout`
(§12, the owner, 2026-10-31, "Persistent, 100 req, 5 s", and 2026-11-03). *(assumed, §12, not put to
the owner: each read of a head waiting the smaller of the 5 seconds of a read on a socket and what is
left of the head's time; the body's rate counted over the time its reads wait, as Kestrel counts it,
a read of the handler that falls under it giving the `IoError` of the kind `TimedOut`; each write of a
response given the longer of the grace and its length at the rate, as Kestrel gives it, for each call
of the system the write makes; and `HttpRequest.Read` and `Write`, the raw layer, setting no time on
the stream, so that a program's own loop holds the times it sets itself)*

Case: [http/serve-timeouts.kz](../corpus/http/serve-timeouts.kz)
```kurz
// a head that does not arrive whole within `RequestHeadersTimeout` from its first byte, and a body whose reads fall under
// `MinRequestBodyDataRate` after its grace, are answered `408 Request Timeout` and close the connection, the handler's response
// unwritten, and the connection gives `HttpError(Protocol, "request timed out")`; a response whose write is not taken within the longer of the grace of `MinResponseDataRate` and its length at that rate
// closes it, since its status has gone out
void Stall(string sent, (HttpRequest) => HttpResponse handler, HttpLimits limits) {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write(sent.Bytes) else {
        IoError e => return
    }

    start = timestamp.Now
    HttpServer.ServeConnection(server, handler, limits) else {
        IoError e => print(e.Kind)
        HttpError e => print(e.Detail)
    }

    waited = timestamp.Now - start
    print(waited >= 100ms && waited < 2s)
    print(Wire(client))
}

// what the client got, in brackets, each CR LF as `|` and the text of a `Date` by its length, and `reset` after it where the server
// closed the connection with a request of the client unread
string Wire(TcpStream client) {
    mut got = Bytes()
    mut after = ""
    while true {
        n = client.Read(mut got, 65536) else {
            IoError e => {
                after = " {e.Kind}"
                break
            }
        }

        if n == 0 {
            break
        }
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    mut shown = ""
    lines = text.Split("\r\n")
    for i in 0..<lines.Count {
        line = lines[i]
        if line.StartsWith("Date: ") {
            shown = "{shown}Date: <{line.Bytes.Count - 6}>"
        } else {
            shown = "{shown}{line}"
        }

        if i < lines.Count - 1 {
            shown = "{shown}|"
        }
    }

    return "[{shown}]{after}"
}

reading = (HttpRequest r) => {
    text = r.Body.ReadAllText(100) else {
        IoError e => "{e.Kind}"
        else => "?"
    }

    print("the handler read: {text}")
    return HttpResponse.Text(200, text)
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
Stall("GET /a HTTP/1.1\r\nHost: h\r\n", hello, HttpLimits(RequestHeadersTimeout: 100ms))
Stall("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab", reading, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms)))

// the same where the request says `Connection: close`, so that no drain of the body follows the handler
Stall("POST /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\nContent-Length: 10\r\n\r\nab", reading, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms)))
Unread()

// a response of 32 MiB to a client that reads none of it, which the buffers of the connection cannot hold
void Unread() {
    listener = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => return
    }

    client = TcpStream.Connect("127.0.0.1:{listener.Port}") else {
        IoError e => return
    }

    server = listener.Accept() else {
        IoError e => return
    }

    client.Write("GET /a HTTP/1.1\r\nHost: h\r\n\r\n".Bytes) else {
        IoError e => return
    }

    mut piece = Bytes()
    while piece.Count < 65536 {
        piece.Add(122)
    }

    mut body = Bytes()
    while body.Count < 33554432 {
        body.AddRange(piece)
    }

    large = HttpResponse.Bytes(200, body)
    start = timestamp.Now
    HttpServer.ServeConnection(server, (HttpRequest r) => large, HttpLimits(MinResponseDataRate: HttpRate(1_000_000_000, 100ms))) else {
        IoError e => print(e.Kind)
        HttpError e => print(e.Detail)
    }

    waited = timestamp.Now - start
    print(waited >= 100ms && waited < 2s)
}
```
