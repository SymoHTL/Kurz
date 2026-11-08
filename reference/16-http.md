# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (decided, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions`, `HttpRequestMessage`, `Uri`, `HttpResponseMessage`, `HttpBody` and
`HttpWriter`, with `UriError`, `UriKind` and `HttpRate`, as `std/`
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
every `pub` type of `std/` and against a list written in `std/`, 2026-11-03, that `HttpBody` is one of
them, against a body type no program can name, and 2026-11-03, that `HttpWriter` is another, the writer
of the streamed response §12 gives, "A response can be streamed"; the two of `Uri`, and
`HttpRate`, whose type a program names to change a rate of `HttpLimits`, are the record's assumptions;
`HttpRequestMessage`: the owner, 2026-11-04, issue 156, comment 6643376941, Q2 (a); and
`HttpResponseMessage`, the response a client gets, is the owner's (W14; the owner, 2026-11-05,
issue 110, comment 6657251329, C2)).

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

Case: [http/client-name-redeclared.kz](../corpus/http/client-name-redeclared.kz)
```kurz
class HttpResponseMessage(int Status)

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
connection's socket: the handler keeps neither, and the connection waits for the handler's response,
as Rust moves a `hyper::Request<Incoming>` to another task (§12, the owner, 2026-11-02, issue 110,
comment 6573113786, against refusing a server's request that has a body as not sendable). The send
is written with `move`, as the send of a mutable class instance is (§12, the owner, 2026-11-03,
issue 110, comment 6600034158), and the handler can send it, since the parameter of the handler's
type is written `move`, `(move HttpRequest r) => HttpResponse` (§12, the owner, 2026-11-04, issue
156, comment 6643376941).

The server calls the handler with the word at the argument, `handler(move r)`, as every call of a
parameter written `move` does. An argument of such a parameter without the word is section 6's
`move-at-call` (§6, the owner, 2026-11-05, issue 156, comment 6657251688, Q3), and so is `move` at a
call in front of an argument whose parameter is not written `move`, as `mut-at-call` is for `mut`
(§6) *(assumed, §6: the name `move-at-call`, and the reading said back for the word in front of a
parameter not written `move`)*. At a send, `move` at an argument is the send's word, however the
method's parameter is written, and a `move` at a call that is both in front of a parameter not
written `move` and of a value held as its caller's is `move-at-call`, the call's word checked
first (§6) *(assumed, §6: those readings)*. A lambda given for the handler whose parameter is written without
`move` fits the type and sends nothing of its request: its check holds the parameter as its
caller's, so that a send or a `move` of it is section 6's `move-reachable` (§6) *(assumed, §6:
that error for it)*. A lambda's parameter
whose type is not written takes `move` from the type, and a lambda whose parameter is written with
`move` fits no type without it, with `type-mismatch` (§6, the owner, 2026-11-05, issue 156, comment
6657251688, Q4). A named function, or a value of a function type whose parameter is written
without `move`, given for the handler fits by the same rule (§6) *(assumed, §6: those readings)*.

Of the connection's stream, the body takes the read half with it for its life, and the server keeps
the write half, as tokio's `TcpStream::into_split` gives an `OwnedReadHalf` and an
`OwnedWriteHalf`. The server's paths into the read half end at the call, and the half comes back to
the server when the body ends, read to its end or drained when its last owner drops it, unless a
read or the drain closed the connection (below) (§12, the owner, 2026-11-05, issue 156, comment
6657251688, Q5, and comment 6666386552, Q7). The response, the `408` and the close stay the
server's, on the write half, and a read of the body that timed out is reported back to the server
at the body's end (§12, the owner, 2026-11-05, issue 156, comment 6666386552, Q7). A body's first
read, in any actor, signals the server through the halves' pair of types, and `100 Continue` is
written on the server's write half for a request that expects it while no response has begun, while a
handler that returns with the body unread by every actor keeps the close of a body unread at its
return (§12, the owner, 2026-11-05, issue 156, comment 6668914650, Q10). The write is made by
`std/`'s code of that pair at the first read, in the actor that reads, under its flag of whether a
response has begun, since the server is inside the handler's call (§12, the owner, 2026-11-05,
issue 156, comment 6669908509, Q12). The response is
written when the handler returns, while the moved body stays the other actor's to read to its end:
every read that keeps to the body's rate gives the body's bytes, then `0` at its end. A body left
unread is drained when its last owner drops it, and the connection reads its next request only after
that, with no new kind of error (§12, the owner, 2026-11-03, issue 39, comment 6601737306).

The drain reads the read half where the body's last owner drops it, bounded by the body's rate and
by `MaxDrainSize`, and where either ends it the connection closes, with no error to any reader, and
the actor that drops the body waits on the drain, at most for the time the body's rate gives
`MaxDrainSize`, about 273 seconds for the 64 KiB at 240 bytes a second, while
a drain under the rate before any response has begun ends the body as a read that timed out does,
so that the server sends the `408` (§12) *(assumed, §12: that reading)*. A `move` of the request
ends every path into it, `b = r.Body` included, by the region check of the record's section 6, and
is refused with `move-reachable` where section 6 refuses it (§12). Of the bounds of `HttpLimits`, a
moved body's reads meet two, the body's rate and, for a body without a declared length,
`MaxRequestBodySize`, while the limit of a declared body is none of them, since it is checked
against `Content-Length` before the handler runs. A body without a declared length, moved or not,
counts its bytes against `MaxRequestBodySize` as it is read, as Kestrel counts them: the read that
would pass it gives `HttpError(TooLarge, "request body over limit")` and none of its bytes, every
later read the same error, and the connection closes, the `413` sent only while no response has
begun, as Kestrel's `BadHttpRequestException` of 413 is (§12) *(assumed, §12: the count, which
corrects the reading said back on issue 156, comment 6644555381, item 2, as the owner's choice of
2026-11-05 names, issue 39, comment 6666386984, and the read past the limit)*. A read
of a moved body that falls under the body's rate gives `HttpError(Protocol, "request timed out")`,
as a handler's read does, and the connection closes; the `408` is sent only while no response has
begun (§12, the owner, 2026-11-03, issue 156, comment 6603205575), the error `request timed out`
being the owner's (§12, the owner, 2026-11-05, M1, issue 110, comment 6657251329, which replaced
the `IoError` with `IoKind.TimedOut` of 2026-11-03); the error is the server's own, in place of the
`IoError` of the kind `TimedOut` of 2026-11-03 (§12, the owner, 2026-11-06, pull request 347,
comment 6680238222) *(assumed, §12: the close and the `408` decided by the server at the report of
that read, which ends the body)*, and after a `408` the
response the handler returns is not written, with no error to any program (§12) *(assumed, §12: that
reading)*. The rate also runs between reads: a moved body held unread past the
rate's grace ends, its next read gives `request timed out`, and the connection closes (§12, the
owner, 2026-11-05, issue 156, comment 6666386552, Q8). Only the server can close without a read:
while the read half is away, the server times the grace from the body's last read, and at its end
it ends the body through the halves' pair of types, closes the connection and serves the next one,
so that the body's next read gives `request timed out` and its drop drains nothing (§12)
*(assumed, §12: the reading said back on issue 156, comment 6668914650)*. While the handler's call
runs, the server times nothing: it times the grace once the call has returned, and a read that
comes past the grace before then finds it passed and gives `request timed out` (§12, the owner,
2026-11-05, issue 156, comment 6669908509). The grace runs from the body's last read and does not
start again when the call returns, and a body that no read has reached counts it from that return
(§12, the owner, 2026-11-06, pull request 347, P2). The time between
reads counts in the body's rate as the time a read waits does, and a
time past the grace since the last read ends the body at once; a body that the handler reads
itself counts only the time a read waits, since Q8 was asked of a moved body (§12) *(assumed, §12:
those readings)*. That a moved body's read under the rate ends the body and closes the connection
is decided, as is its error (above), and the timing of a read's wait is open: how it is
timed on the socket of the actor that got the body stands open in §14, a timer in the poller's
wait or a deadline the scheduler reads at each switch, while the time between reads is the
server's, timed from the body's last read (above) (§12).

A send of an `HttpRequest` with `copy` is the compile error `copy-server-request`, which names
`move`, and a send of it with no word is section 6's `send-needs-word` (§12, the owner, 2026-11-03,
issue 156, comment 6603205575) *(assumed, §12: the error's name, `copy-server-request`; §6: the name
`send-needs-word`)*. A send with `copy` of any value whose type can hold an `HttpRequest` or an
`HttpBody`, a class instance, a list, a map, a union, a nullable `HttpRequest?` or a `data` type
among them, is refused with the same error, `r.Body` and an `HttpBody` made for an upload among
them, while `r.Headers` copies, and a send of such a value with no word is `send-needs-word`, as a
send of the request is (§12) *(assumed, §12: those readings)*. The request a client builds is a type of its own, `HttpRequestMessage`, sent as any value
is, so that the error is a rule of the type (§12, the owner, 2026-11-04, issue 156, comment
6643376941). It is a `data` type with a body of `Bytes`, and a body that streams is given to the
call, `client.Send(request, move body)` (§12, the owner, 2026-11-05, issue 156, comment 6657251688,
Q6, and comment 6668914650, Q11).
That body is an `HttpBody`, and a message whose `Bytes` body is not empty, sent with a body as well,
gives `HttpError(Protocol, "request has two bodies")` before any byte is sent (§12, the owner,
2026-11-05, issue 156, comment 6666386552, Q9) *(assumed, §12: the error's text)*, an error that
shows only when the code runs (§12) *(assumed, §12: that cost)*. The call's body parameter is
written `move`, since the call reads the body to its end, so that the call writes `move` at the
body, as section 6 asks of a call (§6, §12) *(assumed, §12: the parameter's word)*, and an
`HttpBody` for an upload is made from a stream, a file or a series of chunks (§12, the owner,
2026-11-05, issue 156, comment 6668914650, Q11) *(assumed, §12: the makers' names and forms)*.

No case: the compiler reads no `copy` and no `move`, at a send, on a parameter or at a call (§6), so
that no case can expect the move, `copy-server-request`, `send-needs-word` or `move-at-call`, and a
send to another actor needs two actors, which the compiler does not build, as it builds no HTTP
server or client of §12, which the halves of the stream, the first read's `100 Continue`, the rate
between reads, the count of a body without a declared length, the two bodies of a client's call
and the makers of an `HttpBody` need. The id
`copy-server-request` stands in this rule, and `send-needs-word` and `move-at-call` in the record's
section 6, as `move-reachable` does, and none of them in [12-errors.md](12-errors.md), since no case
can expect them (§12, the owner, 2026-11-03, issue 156, comment 6603359338) *(assumed, §6: that
answer's rule, which named the ids before it, applied to `move-at-call`)*.

### W4 (decided, §12) `HttpHeaders`

`HttpHeaders` is an ordered list of names and values, a class of `std/`: `h.Get(name)` gives the
first value of the name, a `string?`, `h.GetAll(name)` gives every value, and `h.Add(name, value)`,
`h.Set(name, value)` and `h.Remove(name)` change it. A name compares without regard to ASCII case
and is written back as it was spelled, and repeated headers, `Set-Cookie`, stay apart (§12, the owner, 2026-10-31, "Ordered list type").
hyper's `HeaderMap` keeps the values of a name apart and in their order but lowercases the name, and
ASP.NET Core's `IHeaderDictionary` holds the values of a name as one `StringValues`, so that neither
keeps one list of every name and value with each name's spelling (§12). `GetAll` gives a
new `List<string>`, so that a change to it changes nothing in the headers; `Set` of a name the
headers hold puts the value in the place of the name's first header, which keeps its spelling, and
takes out the name's other headers, as hyper's `HeaderMap::insert` keeps the entry, and `Set` of a
name they do not hold puts it after every header they hold; a name they do not hold gives `null`
from `Get` and an empty list from `GetAll`, and its `Remove` changes nothing (§12, the owner,
2026-11-02, the headers' pass, Q3 to Q7 at their defaults, #110, comment 6573113786, the questions
in comment 6567534197). `Remove` removes every value of the name *(assumed, §12: proposed with the
product pass and not put to the owner)*. `h.Add(name, value)` and `h.Set(name, value)` give `void | HttpError`, since a header's
value often comes from outside input, and a CR or a LF in the name or the value is
`HttpError(Protocol, "invalid header: CR or LF")`, refused where it is set, as Rust's `HeaderValue`
and Kestrel's header dictionary refuse it (§12, the owner, 2026-11-02, Q5, #110, comment
6573113786, its form, comment 6573265513, and its text, 2026-11-03, comment 6613356831). `Add` and
`Set` take an empty name and refuse a CR or a LF alone (§12, the owner, 2026-11-03, #110, comment
6615254370). Its members
are `string? Get(string name)`, `List<string> GetAll(string name)`,
`void | HttpError Add(string name, string value)`, `void | HttpError Set(string name, string value)`
and `void Remove(string name)`, as above, `Remove` giving `void` (§12, the owner, 2026-11-02, the
headers' pass at its defaults, #110, comment 6573113786). The class is made by `HttpHeaders()`, which holds no header;
a refused `Add` or `Set` changes nothing, `Set` checking before it changes a header; an empty value is taken; a name is written back as it was spelled in the head that
a writer of a request or a response writes; and `print(h)` gives the text of a class without a
`Text()` (A5) *(assumed, §12: readings of `std/`'s class not put to the owner, the constructor's
empty start among them)*. A program walks the headers by `h.Count`, `h.NameAt(i)` and
`h.ValueAt(i)`, and by `for name, value in h`, which walks them in their order as the index loop
with `Count` read once (§4, the owner, 2026-11-04, "Index loop, Count once"); the `for` is a pattern
(C6), as C#'s `foreach` takes any type with `GetEnumerator()`, so that any class with `Count`,
`NameAt(i)` and `ValueAt(i)` is walked with two names (§12, the owner, 2026-11-02, #110, comment
6573113786, and its form, comment 6573265513).
The checker refuses `for name, value in h`, as it refuses two names over any class, which is a
defect of its C6 and not the rule, so the cases walk the headers by `h.Count`, `h.NameAt(i)` and
`h.ValueAt(i)`.
`h.Count` is the read-only property `int Count` (K7), and `h.NameAt(i)` and `h.ValueAt(i)` give a
`string` and, with `i` outside 0 to `Count - 1`, stop the program with `index-out-of-range`, as a
list's index does (T14) (§12, the owner, 2026-11-03, #110, comment 6615254370).
The error line of that stop names `std/std.kz` and the line of the read inside it, and the trace
below it names the program's call, as §6 gives every exception the file and the line that raised it
*(assumed, §12: the default (a) parked on #110, comment 6616414692, against the program's line, as
Rust's `#[track_caller]` gives it)*.

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
    headers.Add("HOST", "c")
    headers.Set("host", "b")
    headers.Set("X-Id", "1")
}

h = HttpHeaders()
Fill(h) else {
    HttpError e => print(e.Detail)
}
// `Set` puts its value in the place of the name's first header, which keeps its spelling, takes out the name's
// other headers, and puts a name the headers do not hold after every header they hold
print(h.Count)
for i in 0..<h.Count {
    print("{h.NameAt(i)}: {h.ValueAt(i)}")
}
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
split at its first `?`, with no decoding, and `r.Headers` each header as sent, its value without the
spaces and tabs around it (W4) (§12, assumed with the product passes of 2026-10-31 and 2026-11-03). A
target in absolute form, `http://host/a?b`, gives its path alone, `/a` and `b`, and its authority
stands as the `Host` in place of the client's (§12, the owner, 2026-11-05, Q2). A refusal
is of the kind `Protocol` where the server answers `400`, or `408` for a request that ran out of its
time (W10), `TooLarge` where it answers `413`, `414`
or `431`, and `Unsupported` where it answers `501`, `505` or `417`, for the causes §12 lists with
each status (the owner, 2026-10-31, "Status line only", and 2026-11-03, "`HttpKind` keeps the
record's four kinds"), each with a text that names the cause and quotes no value of the request,
`malformed request: header without a colon`, `request line too long` or `unsupported HTTP version`
(§12, the owner, 2026-11-03, "The server's error texts mirror the client's" and "The refusals that
had no text take the kind `Unsupported`"). A `Transfer-Encoding` whose last coding is not `chunked` is
answered `400`, of the kind `Protocol`, and one that names another coding before its last `chunked` is
answered `501`, of the kind `Unsupported` (§12, the owner, 2026-11-05, Q1); a request of
HTTP/1.0 with a `Transfer-Encoding` is `malformed request: transfer encoding in HTTP/1.0` (Q7); and the
method `CONNECT` is `unsupported method`, answered `501` (Q3). A target or a header value that is not UTF-8
is `malformed request: target not UTF-8` or `malformed request: header not UTF-8`, since a `string` is
UTF-8, at the cost that a value sent in Latin-1 is refused where bytes would pass (§12, the owner,
2026-10-31). In a build for Windows the call is `not supported` at the
line of the program that makes it, though the read of the socket lies in `std/` (§12, "Loop not
supported"). *(assumed, §12, not put to the owner: a stream that ends before its request line is
`malformed request: no request line`, which the server answers with nothing; a line ended by a LF
alone is refused, as RFC 9112, section 2.2, lets a server refuse it; a valid `Host` is the host
of RFC 3986 with an optional port; the lines of a `Transfer-Encoding` given more than once read as one
list in their order, each coding without regard to ASCII case, with the spaces and tabs around it taken
off, and an empty item between two commas skipped, as RFC 9110, section 5.6.1, has a list read; the
texts of Q1's two refusals, `malformed request: transfer encoding not ending in chunked` and
`unsupported transfer encoding`; a `chunked` before the last one counted as another coding, so that
`chunked, chunked` is `unsupported transfer encoding`, as RFC 9112, section 6.1, forbids `chunked`
twice; a target is in absolute form where it starts with a scheme and `://`, the scheme of RFC 3986, a
letter and then letters, digits, `+`, `-` and `.`, whatever it names, its empty path `/`; the
client's own `Host` held to the rules of any other request, two of them, none in HTTP/1.1 and one that
is no valid `Host` refused, before the authority takes its place; an authority that is empty, or that
is no valid `Host`, one with a userinfo among them, since a `Host` holds no `@`, is
`malformed request: bad Host`, as RFC 9110, section 4.2.1, refuses both; where a request meets more than
one refusal, the first the reader meets answers it, in this order: a request line or headers over their
limits as they are read, then the request line (its method, its target, its version, then `CONNECT`),
each header, `Host`, `Content-Length`, a `Content-Length` beside a `Transfer-Encoding`, a
`Transfer-Encoding` in HTTP/1.0, its last coding, a coding before its last `chunked`, a declared body
over its limit, and `Expect`; and a NUL, a lone CR or any other control byte but a tab in a header's
value is `malformed request: control byte in a header`, as RFC 9110, section 5.5, has a value of
visible characters, spaces and tabs, a byte over 127 passing where the value is UTF-8)*

Case: [http/request-read.kz](../corpus/http/request-read.kz)
```kurz
// the request a client sends, as a server reads it from its connection: the method, the path, the query after the first `?`,
// and the headers, each as the client sent it, a value without the spaces and tabs around it; a target in absolute form gives its
// path, `/` where it has none, and its authority stands as the `Host`
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
    Show("GET http://example.org/a?b HTTP/1.1\r\nHost: h\r\n\r\n")
    Show("GET HTTP://h:8080 HTTP/1.1\r\nHost: h\r\n\r\n")
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
    Show("GET / HTTP/1.1\r\nAccept: */*\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: a\r\nHost: b\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: a b\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\n X: y\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\nNoColon\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost : h\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\nX: a\u0000b\r\n\r\n".Bytes)
    Show("GET / HTTP/1.1\r\nHost: h\r\nX: a\rbc: d\r\n\r\n".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: +5\r\n\r\nhello".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nContent-Length: 6\r\n\r\nhello".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\nTransfer-Encoding: chunked\r\n\r\nhello".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip\r\n\r\n".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked, gzip\r\n\r\n".Bytes)
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: br\r\n\r\n".Bytes)
    Show("POST / HTTP/1.0\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n".Bytes)
    Show("GET http://u@h/ HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
    Show("GET http:///a HTTP/1.1\r\nHost: h\r\n\r\n".Bytes)
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
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip, chunked\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: gzip\r\nTransfer-Encoding: chunked\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n")
    Show("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: \t CHUNKED \t\r\n\r\n0\r\n\r\n")
    Show("CONNECT h:443 HTTP/1.1\r\nHost: h\r\n\r\n")
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
    // over the size with 4 headers, far under the count
    mut v = "v"
    while v.Bytes.Count < 12000 {
        v = "{v}{v}"
    }

    Show("GET / HTTP/1.1\r\nHost: h\r\nX-A: {v}\r\nX-B: {v}\r\nX-C: {v}\r\n\r\n")
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
A chunked body's trailer is read and dropped within what the head left of `MaxRequestHeadersTotalSize`
and `MaxRequestHeaderCount`, past which a read gives `HttpError(TooLarge, "request header too large")`
(§12, the owner, 2026-11-05, Q8); where the server drops a body the handler did not read, such a trailer
closes the connection as a chunked body malformed while it is dropped, and a handler whose read gave
it has its response written with a close (W9, §12, assumed). `Read` gives `int | IoError | HttpError`, `ReadAll`
`Bytes | IoError | HttpError` and `ReadAllText` `string | IoError | HttpError | InvalidUtf8`: a failure
of the network is the stream's `IoError`, a body the stream ends inside
`HttpError(Protocol, "malformed request: unexpected end of body")`, a chunk that is not well formed
`malformed request: bad chunk` or `malformed request: bad chunk size`, of the same kind, and a body over
`MaxRequestBodySize` or over the `max` of `ReadAll` `HttpError(TooLarge, "request body over limit")`
(§12, the owner, 2026-11-03, "The server's error texts mirror the client's").
*(assumed, §12, not put to the owner: a `Read` with a `max` of zero or below giving 0 and reading
nothing, but after a failure of HTTP, which it gives again; a chunk's extensions dropped, the trailer and the head one budget,
the empty line of each beside it, `ReadAll(max)` taking any `max` up to the largest `int` (a default the
owner's answer of 2026-11-05 took), a chunk's size line of at most `MaxChunkSizeLine` bytes, `ReadAllText`'s `InvalidUtf8` for a body that is not
UTF-8, chunks held to `MaxRequestBodySize` as they are read, as a declared length is held before,
`ReadAll(max)` refusing a body over `max` with `request body over limit`, every body where `max` is
below zero, an empty one among them, a declared length before its first byte is read, so that the body
stays in the stream for the reads after it, and a chunked body once more than `max` of its bytes were
read, which are dropped; a failure of HTTP staying, every read after it giving it again; and
`100 Continue` sent to a request of HTTP/1.1 alone, and to none whose body is absent or a declared
length of 0)*

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

    // a read appends at most what it was asked for, nothing where that is 0, and gives 0 at the end, again and again
    client.Write("POST /parts HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhello".Bytes)
    parts = HttpRequest.Read(server)
    mut got = Bytes()
    p0 = parts.Body.Read(mut got, 0)
    p1 = parts.Body.Read(mut got, 2)
    p2 = parts.Body.Read(mut got, 2)
    p3 = parts.Body.Read(mut got, 2)
    p4 = parts.Body.Read(mut got, 2)
    p5 = parts.Body.Read(mut got, 2)
    print("{p0} {p1} {p2} {p3} {p4} {p5}")

    // the whole body over the bound of `ReadAll`
    client.Write("POST /big HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhello".Bytes)
    big = HttpRequest.Read(server)
    match big.Body.ReadAll(4) {
        Bytes b => print(b.Count)
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }

    // a declared length over the bound is refused before its first byte is read, so that the body stays for the reads after it
    mut rest = Bytes()
    print(big.Body.Read(mut rest, 100))

    // a bound of the largest `int`, which the read counts past without passing the range of an `int`
    client.Write("POST /most HTTP/1.1\r\nHost: h\r\nContent-Length: 5\r\n\r\nhello".Bytes)
    most = HttpRequest.Read(server)
    match most.Body.ReadAll(2147483647) {
        Bytes b => print("{most.Method} {b.Count}")
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

    // a bound below zero refuses every body, an empty chunked one among them, as it refuses every declared length
    client.Write("POST /below HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n".Bytes)
    below = HttpRequest.Read(server)
    match below.Body.ReadAll(-1) {
        Bytes b => print(b.Count)
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

All() else {
    IoError e => print(e)
    HttpError e => print("{e.Kind}: {e.Detail}")
}
```

Case: [http/request-trailer.kz](../corpus/http/request-trailer.kz)
```kurz
// the trailer of a chunked body is held to what its head left of the head's limits, 100 lines and 32768 bytes of them by
// `HttpLimits()`: the head's 2 lines and 98 of the trailer pass and 99 do not; the head's 37 bytes, `Host: h` and
// `Transfer-Encoding: chunked` each with its CR LF, and 32731 of the trailer, each line with its CR LF, pass, and a byte more does
// not, so that a trailer given a budget of its own, or lines counted without their CR LF, would pass both
void | IoError Show(string label, string trailer) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write("POST / HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n{trailer}\r\n".Bytes)
    client.ShutdownWrite()
    r = HttpRequest.Read(server) else {
        HttpError e => {
            print("{label}: {e.Kind}: {e.Detail}")
            return
        }
    }

    match r.Body.ReadAll(100) {
        Bytes b => print("{label}: {b.Count}")
        IoError e => print(e)
        HttpError e => print("{label}: {e.Kind}: {e.Detail}")
    }
}

// `count` lines of a trailer, each `X-<i>: ` and `size` bytes of `v`, a power of 2
string Lines(int count, int size) {
    mut v = "v"
    while v.Bytes.Count < size {
        v = "{v}{v}"
    }

    mut lines = ""
    for i in 0..<count {
        lines = "{lines}X-{i}: {v}\r\n"
    }

    return lines
}

// the line `X-<i>: ` with `size` bytes of `v`, and its CR LF
string Line(int i, int size) {
    mut v = ""
    while v.Bytes.Count < size {
        v = "{v}v"
    }

    return "X-{i}: {v}\r\n"
}

void | IoError All() {
    Show("98 lines", Lines(98, 1))
    Show("99 lines", Lines(99, 1))
    // 7 lines of 4103 bytes are 28721, and an eighth of 4010 makes 32731, what the head left
    Show("32731 bytes", "{Lines(7, 4096)}{Line(7, 4003)}")
    Show("32732 bytes", "{Lines(7, 4096)}{Line(7, 4004)}")
    Show("1 line of 65536", Lines(1, 65536))
}

All() else {
    IoError e => print(e)
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

The limits of a request are a `data HttpLimits`, its first seven members with the names and the
defaults of Kestrel's `KestrelServerLimits`: a request line of at most 8192 bytes, header lines of at
most 32768 bytes in all, at most 100 headers, a declared body of at most 30000000
bytes, a head that arrives within 30 seconds, and a body and a response taken at 240 bytes a second
after a grace of 5 seconds, each rate an `HttpRate(int BytesPerSecond, duration Grace)` (§12, the
owner, 2026-10-31, "HttpLimits data", 2026-11-01, "Kestrel's bounds" and "Add Kestrel's response
rate", and 2026-11-03); and the four numbers the server held fixed made members,
`MaxRequestsPerConnection`, 100 requests to a connection, `KeepAliveTimeout`, an idle connection
closed after 5 seconds, Kestrel's name with the owner's number where Kestrel waits 130 seconds,
`MaxDrainSize`, 64 KiB of a body drained, and `MaxChunkSizeLine`, a chunk's size line of 4096 bytes,
the three without a limit of Kestrel's (§12, the owner, 2026-10-31, "Persistent, 100 req, 5 s", and
2026-11-03).
`HttpRequest.Read(stream)` reads with the limits of `HttpLimits()`, and the limits a server is given
reach the parser through an entry inside `std/` that no program calls (§12, the owner, 2026-11-02,
Q3, #110, comment 6573113786), so that a program holds a request to other limits by
`ServeConnection(stream, handler, limits)` (§12, assumed: `ServeConnection` takes them in a second
form too). *(assumed, §12, not put to the owner: the lines counted as Kestrel
counts them, the request line with its CR LF against `MaxRequestLineSize` and the header lines with
theirs against `MaxRequestHeadersTotalSize` and 2 more bytes for the empty line, as Kestrel's
`Http1Connection` begins each request with `MaxRequestHeadersTotalSize + 2` bytes allowed)*

Case: [http/http-limits.kz](../corpus/http/http-limits.kz)
```kurz
// the bounds a server holds a connection to, the first seven with the defaults ASP.NET Core's Kestrel gives them and the four after
// them the server's own, an idle connection's 5 seconds where Kestrel waits 130, and a rate a slow client is held to after its grace
d = HttpLimits()
print("{d.MaxRequestLineSize} {d.MaxRequestHeadersTotalSize} {d.MaxRequestHeaderCount} {d.MaxRequestBodySize} {d.RequestHeadersTimeout}")
print("{d.MinRequestBodyDataRate.BytesPerSecond} {d.MinRequestBodyDataRate.Grace} {d.MinResponseDataRate.BytesPerSecond} {d.MinResponseDataRate.Grace}")
print("{d.MaxRequestsPerConnection} {d.KeepAliveTimeout} {d.MaxDrainSize} {d.MaxChunkSizeLine}")
mine = HttpLimits(MaxRequestBodySize: 1mb)
print("{mine.MaxRequestBodySize} {mine.MaxRequestHeadersTotalSize}")
```

Case: [http/request-limits.kz](../corpus/http/request-limits.kz)
```kurz
// a request served with limits of the program's own, which `ServeConnection` takes, since `HttpRequest.Read` reads under those of
// `HttpLimits()` alone: a request line of at most 19 bytes with its CR LF, at most 2 header lines of at most 40 bytes in all with
// their CR LFs, and a body of at most 5 bytes, declared or in chunks
void | IoError Show(string request) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    client.Write(request.Bytes)
    client.ShutdownWrite()
    shown = (HttpRequest r) => {
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

        return HttpResponse.Empty(204)
    }

    small = HttpLimits(MaxRequestLineSize: 19, MaxRequestHeadersTotalSize: 40, MaxRequestHeaderCount: 2, MaxRequestBodySize: 5)
    HttpServer.ServeConnection(server, shown, small) else {
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

Case: [http/request-refused-unsupported.kz](../corpus/http/request-refused-unsupported.kz)

### W8 (decided, §12) Writing a response

A response is made by a function for each kind of body, `HttpResponse.Text(200, "hi")`,
`HttpResponse.Bytes(200, b)`, `HttpResponse.File(200, path)` and `HttpResponse.Empty(204)`, and the
rest is set through `response.Headers` (W4) (§12, the owner, 2026-11-01, Q1 (a)). Each sets the
`Content-Type` ASP.NET Core's `Results` sets, `text/plain; charset=utf-8` for `Text`,
`application/octet-stream` for `Bytes` and `File`, and none for `Empty`, which a handler replaces with
`Set` (§12, the owner, 2026-11-03). A status outside 200 to 599 stops the program with a run-time
error (§12, the owner, 2026-11-02, Q5, and 2026-11-05, S2: a `1xx` is interim, and the server alone sends
`100 Continue`), the error `thrown` of a `throw` in `std/` (O5), with the text
`status <n> is outside 200 to 599` (§12, assumed). `response.Write(stream)` writes the status line, the
headers and, where the body is not chunked, the `Content-Length` of the body, which a `File` takes from its size when it is written, and then
the body, and gives `void | IoError`; `response.WriteHead(stream)` writes the same head without the
body, for a `HEAD` (§12, the owner, 2026-11-02, Q10 and Q13). Neither writes `Date`, nor `Connection` but on its own
`500` (below), which the server's loop adds (§12, assumed). A `204` and a `304` get no body, no
`Content-Length` of the writer's and no `Transfer-Encoding`, a handler's dropped (§12, assumed),
and an empty `205` is written with `Content-Length: 0` (§12, the owner, 2026-11-02, Q14).
`HttpResponse.Text` and `HttpResponse.Bytes` given a `205` and a body that is not empty stop the
program with `thrown` and the text `status 205 takes no body`, as a status outside the range does,
since they see the status and the body (§12, assumed: the reading said back with Q14). A response
`Write` cannot write, a `File` of `205` whose file holds bytes or a `Content-Length` its body
contradicts, is answered `500 Internal Server Error` with an empty body and `Connection: close`, raising nothing but where
the size of its file cannot be read (below) (§12, the owner, 2026-11-02, Q5 and Q14, and assumed). A `Transfer-Encoding` the handler sets is kept, `chunked`
added as its last coding where it does not end in it, and the body is chunked, a coding it names
being announced and not applied; beside a `Content-Length` it gets no answer (§12, the owner,
2026-11-03). A status RFC 9110 and RFC 6585 name no reason phrase for is written with an empty one
(§12, assumed). In a build for Windows `Write` and `WriteHead` are `not supported` at the line of the
program that calls them (§12, "Loop not supported"). *(assumed, §12, not put to the owner: the
headers written in the order they were set, the `Content-Type` first, and the `Content-Length` of
the body after them where the handler set none; a handler's `Content-Length` contradicted when it is
no number or another number than the body's length; a file whose
size cannot be read answered with that `500`, `Write` giving the `IoError` of the read; a
`Transfer-Encoding` beside a `Content-Length` giving an `IoError` of the kind `InvalidInput`, nothing
written, before the `Content-Length` is held against the body; a chunked `Text` or `Bytes` written as
one chunk and the last, and a `File` as a chunk for each piece of 64 KiB it is read in; and a file
whose size changes while it is sent giving one of the kind `InvalidData`)*

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
// a `204` and a `304` have no body, so neither a body nor its length nor a `Transfer-Encoding` is written; an empty `205`
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
// a response the writer cannot write, a `205` whose file holds bytes or a `Content-Length` its body contradicts, is answered `500`
// with no body and a close, and the write gives nothing more; a file that cannot be read is answered so too, and the write gives its
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
    File.WriteAll("response-unwritable-205.txt", "x".Bytes)
    Wire(HttpResponse.File(205, "response-unwritable-205.txt"))
    File.Delete("response-unwritable-205.txt")
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
goes to `ServeConnection`'s result"), and `void` too where the server closed the connection after a
response, where the client reset it between two requests and where it stayed idle for
`KeepAliveTimeout` (W10), while `Serve` drops each connection's result, its `IoError` among them, and
accepts the next (§12, assumed); `Serve` gives `void | IoError`, returning where an `Accept`
fails other than for one connection (§12, assumed). Each takes an `HttpLimits` (W7) in a second form,
a limit of zero or below being an `IoError` of the kind `InvalidInput` that names the member, before a
read or an accept (§12, the owner, 2026-11-02, Q14, and assumed). A connection of HTTP/1.1 stays open
for its next request, and one of HTTP/1.0, one whose request or response says `Connection: close`, an option of a
`Connection` header's comma-separated list that is `close` without regard to ASCII case and to the
white space around it,
one after a `500`, the writer's own among them, and one after the last request
`MaxRequestsPerConnection` allows closes after the response (§12, the owner, 2026-10-31, "Persistent,
100 req, 5 s", and 2026-11-02, Q11, and assumed). The server writes its `Date` where the handler set none and keeps the handler's (§12, the owner,
2026-11-02, Q10), and before a close its own `Connection: close` where the handler set no `Connection`
(Q10), and beside a handler's `Connection` that does not say `close`, read as the option above (§12, the
owner, 2026-11-05, Q6). A connection the server closes once it has
written a response or a refusal is closed in stages: its writes ended, then what the client still
sends read and dropped, then the close, as RFC 9112, section 9.6, has it (§12, the owner, 2026-11-02,
"a half-close on `TcpStream`"). A `HEAD` is answered with the head alone, as `WriteHead` writes it (§12, the owner, 2026-11-02,
Q13). A body the handler did not read is read and dropped up to `MaxDrainSize`, past which the
connection closes, a request that expects `100-continue` whose body the handler did not read closes it
with no `100 Continue` sent, and a chunked body malformed while it is dropped, one whose trailer passes
what the head left of its limits among them, closes it without an answer, the handler's response not
written (§12, assumed). A request the server refuses is answered by its status line alone, with an empty
body and `Connection: close`, and the connection closes (§12, the owner, 2026-10-31, "Status line
only"). A handler that throws ends what runs the server, as an exception ends the actor it happens in
(§12). In a build for Windows `Serve` and `ServeConnection` are `not supported` at the line of the
program that calls them (§12, "Loop not supported"). *(assumed, §12, not put to the owner: the
drop of the close in stages bounded by `MaxDrainSize` and `KeepAliveTimeout`, a drop of its own after
a body whose drop passed `MaxDrainSize`, up to `MaxDrainSize` more, and after a body the handler left
unread for `100-continue`; no close in stages where the client ended the connection, where it stayed
idle, where the network failed, or where the write of a response failed, one that ran out of its time
among them, the connection then closed at once and the failure the result of `ServeConnection`; a
half-close the system refuses, or a read of the drop that fails, ending the close in stages at once;
a handler whose read of a body gave an error, a trailer past what the head left among them, answered
by its response with a close, as after any read of its body that failed; the
`Date` written once a second, as hyper caches it; a refusal's status line with `Content-Length: 0` and
the server's `Date`, as Kestrel writes it; a handler's `Transfer-Encoding` dropped for a request of
HTTP/1.0 and the body framed by its length, as hyper drops it, at the cost that a body the handler
coded, as `gzip`, reaches such a client without the name of its coding; no `Connection` of the server's
on a response after which it keeps the connection; the body the handler left read before the response is
written, so that a body past `MaxDrainSize` is answered with `Connection: close`; the wait for the
first byte of each request, the first of a connection among them, bounded by `KeepAliveTimeout`, and
the reads of a request by the head's time and the body's rate alone (W10); a connection aborted, reset or refused before `Accept` took it
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
// again is written as it was made, and a `Date` the handler set is kept
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

// what the client got, in brackets, each CR LF as `|` and the text of the server's `Date` by its length, the handler's as it is, and
// `reset` after it where the server closed the connection with a request of the client unread
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
        if line.StartsWith("Date: ") && line != "Date: Sun, 06 Nov 1994 08:49:37 GMT" {
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
```

Case: [http/serve-close.kz](../corpus/http/serve-close.kz)
```kurz
// a connection closes after the response to a request of HTTP/1.0, to a request or a response that says `Connection: close`, to a
// `500`, the writer's own among them, and to the last request `MaxRequestsPerConnection` allows, and the server writes
// `Connection: close` where the handler's `Connection` does not say it, beside a handler's `keep-alive`; a request the client sent after it is left unread; and the writer's own `500`
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

// a response the writer cannot write: its `Content-Length` contradicts its body
HttpResponse Unwritable() {
    response = HttpResponse.Text(200, "x")
    response.Headers.Set("Content-Length", "2") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

hello =(HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
closing = (HttpRequest r) => {
    response = HttpResponse.Text(200, "bye")
    response.Headers.Set("Connection", "close") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

keeping = (HttpRequest r) => {
    response = HttpResponse.Text(200, "kept")
    response.Headers.Set("Connection", "keep-alive") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}

failing = (HttpRequest r) => HttpResponse.Empty(500)
unwritable = (HttpRequest r) => Unwritable()
partly = (HttpRequest r) => {
    if r.Path == "/b" {
        return Unwritable()
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
Exchange("GET /a HTTP/1.0\r\n\r\n", keeping, HttpLimits())
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
// a chunked body malformed while it is dropped closes the connection without an answer, and so does one whose trailer passes what
// the head left of its limits, here one line of `MaxRequestHeaderCount` 3; and a response the server holds while the
// next request stands in the buffer goes out before that request's `100 Continue`; a `MaxDrainSize` of the largest `int` drops a
// body as any other does
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
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n0\r\nX-a: 1\r\nX-b: 2\r\n\r\n", hello, HttpLimits(MaxRequestHeaderCount: 3))
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\n", hello, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\nok", reading, small)
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nPOST /b HTTP/1.1\r\nHost: h\r\nContent-Length: 2\r\nExpect: 100-continue\r\n\r\nok", reading, small)
Exchange("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 4\r\n\r\nabcd{next}", hello, HttpLimits(MaxDrainSize: 2147483647))
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
`408 Request Timeout` and its connection closes (§12, the owner, 2026-11-01, "Kestrel's bounds"), the
handler's response unwritten (§12, assumed), and `ServeConnection` gives the `HttpError` of that refused request,
`HttpError(Protocol, "request timed out")` (§12, the owner, 2026-11-03, "The server's error texts mirror the client's"); it holds a response to `MinResponseDataRate` after its
grace, and under it the connection closes, since its status has gone out (§12, the owner, 2026-11-01,
"Add Kestrel's response rate"), and `ServeConnection` gives that write's `IoError` of the kind
`TimedOut` (§12, assumed); and an idle connection closes without an answer at `KeepAliveTimeout`
(§12, the owner, 2026-10-31, "Persistent, 100 req, 5 s", and 2026-11-03), `ServeConnection` giving
`void` (W9). The server measures these
bounds by `instant` and holds them by the socket's own times, `SO_RCVTIMEO` set before each refill to
what remains of the bound that read is under (§12, the owner, 2026-11-04, the monotonic clock, Q9 (a));
a read of a head waits what remains of the head's time alone, with no 5 seconds of a read on a socket
inside the server (Q10 (b)); before each refill of a body the read may wait until the body would fall
under its rate, the longer of the grace and the bytes read so far at the rate, less the time its reads
waited already (Q11 (a)); and a handler's read of a body that falls under its rate gives
`HttpError(Protocol, "request timed out")`, answered `408` (Q12 (b)) (#110, comment 6643368937).
*(assumed, §12, not put to the owner: `KeepAliveTimeout` the wait for the first byte of each request,
the first of a connection among them; a remainder of zero or below the timeout, with no call of the
system; each write of a response, a refusal's among them, given the longer of the grace and its length
at the rate, as Kestrel gives it, what remains of that bound set before each call of the system the
write makes; and `HttpRequest.Read`, the reads of the body it gives,
`Write` and `WriteHead`, the raw layer, setting no time on the stream and holding no rate, so that a
program's own loop holds the times it sets itself)*

Case: [http/serve-timeouts.kz](../corpus/http/serve-timeouts.kz)
```kurz
// a head that does not arrive whole within `RequestHeadersTimeout` from its first byte, and a body whose reads fall under
// `MinRequestBodyDataRate` after its grace, are answered `408 Request Timeout` and close the connection in stages, which waits at
// most `KeepAliveTimeout` for the end of a client that sends nothing more, the handler's response
// unwritten, the handler's read of such a body giving `HttpError(Protocol, "request timed out")`, and the connection gives that error
// too; a connection whose first request, or whose next one after a response, sends no byte within `KeepAliveTimeout` is closed with
// nothing more written; a response whose
// write is not taken within the longer of the grace of `MinResponseDataRate` and its length at that rate closes it, since its status
// has gone out
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
    print(waited >= 100ms)
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
        HttpError e => e.Detail
        else => "?"
    }

    print("the handler read: {text}")
    return HttpResponse.Text(200, text)
}

hello = (HttpRequest r) => HttpResponse.Text(200, "hi {r.Path}")
Stall("GET /a HTTP/1.1\r\nHost: h\r\n", hello, HttpLimits(RequestHeadersTimeout: 100ms, KeepAliveTimeout: 500ms))
Stall("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab", reading, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms), KeepAliveTimeout: 500ms))

// the same where the request says `Connection: close`, so that no drain of the body follows the handler
Stall("POST /a HTTP/1.1\r\nHost: h\r\nConnection: close\r\nContent-Length: 10\r\n\r\nab", reading, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms), KeepAliveTimeout: 500ms))

// a handler that reads none of such a body, which the server drains after it
Stall("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab", hello, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms), KeepAliveTimeout: 500ms))

// a client that sends nothing at all
Stall("", hello, HttpLimits(KeepAliveTimeout: 100ms))
Stall("GET /a HTTP/1.1\r\nHost: h\r\n\r\n", hello, HttpLimits(KeepAliveTimeout: 100ms))
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
    print(waited >= 100ms)
}
```

### W11 (decided, §12) A streamed response

A handler streams a body by `HttpResponse.Stream(status, (HttpWriter w) => ...)`, a function of the
type `(HttpWriter) => void | IoError`: the server writes the head and then calls the function
*(assumed: in the system call of the function's first write, or at its return where it wrote nothing,
as `Results.Stream` on Kestrel sends it; put to the owner, #110, comment 6645606334)*, each
`w.Write` sends one chunk, and the function's return ends the body; to a peer of HTTP/1.0 the body
goes without chunks and the close of the connection ends it; and the writer lives only inside the
call (§12, the owner, 2026-11-03, "A response can be streamed"). The function takes its writer
without `mut`, since `HttpWriter` is a class of `std/` (§12, the owner, 2026-11-03, "The callback of
a streamed response takes its writer without `mut`"). `w.Write` takes a `Bytes` or a `string` and
gives `void | IoError`, the function gives `void | IoError` too, and an `IoError` closes the
connection, since no status can be sent after the head went out (§12, the owner, 2026-11-03).
`Stream` sends `application/octet-stream`, which a handler replaces with `Set` (§12, the owner,
2026-11-03). A status that has no body, 100 to 199, `204`, `205` or `304`, stops the program at
`Stream` with `status <n> has no body to stream`, and a write after the function returned gives an
`IoError` of the kind `InvalidInput`, `writer used after its response ended`, and writes nothing
(§12, the owner, 2026-11-04, #110, comment 6643368937, Q2 and Q8 of comment 6623201585).
*(assumed, §12, not put to the owner: a `HEAD` answered with the head,
`Transfer-Encoding: chunked` in it, and the function not called; a `Content-Length` the handler set
giving `InvalidInput` with nothing written; a write of no bytes writing nothing; each write held to
`MinResponseDataRate` (W10); `response.Write(stream)` chunking always, and `WriteHead` writing the
head alone; the function's `IoError` ending the body without its last chunk, the head written where
no write sent it, and `Write` and `ServeConnection` giving that error; the function reading the
request's body while it writes, what it left unread drained after it and the connection closing
where that is past `MaxDrainSize` or the request still awaits `100 Continue`, a read before the head
went out sending `100 Continue` and none after it; no bound on the stream's whole time; the CR LF
that ends a chunk going out with the write after it; and the
end of the body after the function's return held where the next request's head stands whole in the
stream's buffer, as a response is held (W9), and written with the response after it)* The
response holds its function in a field of a function type, so that a function that reads a class
that holds a `mut` response closes a ring through it (R9), which a handler that reads that class
does not, by R9's reading of a field of a function type (§3, the owner, 2026-11-04, #110, comment
6643368937), whose case `http/stream-cycle.kz` stands under R9.

Case: [http/response-stream.kz](../corpus/http/response-stream.kz)
```kurz
// a response a function writes goes with `Transfer-Encoding: chunked` and no `Content-Length`, each `w.Write` of a `Bytes` or a
// `string` one chunk and a write of no bytes none, and the function's return writes the chunk of no bytes that ends the body; its
// `Content-Type` is `application/octet-stream`, which a handler replaces, and a `Transfer-Encoding` the handler set is kept, `chunked`
// added; a `Content-Length` the handler set is `InvalidInput` with nothing written; a function's `IoError` ends the body without its
// last chunk, the head written where no write sent it, and `Write` gives that error; `WriteHead` writes the head alone and calls no
// function; and a writer kept past its call gives `IoError(InvalidInput)` and writes nothing
class Keeper {
    pub mut List<HttpWriter> Kept = List<HttpWriter>()

    pub void Keep(HttpWriter w) {
        Kept.Add(w)
    }
}

// what the client got from `r.Write`, or from `r.WriteHead` where `head`, each CR LF as `|`, after what the call gave
void | IoError Wire(HttpResponse r, bool head) {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    mut result = "void"
    if head {
        r.WriteHead(server) else {
            IoError e => {
                result = "{e.Kind}"
            }
        }
    } else {
        r.Write(server) else {
            IoError e => {
                result = "{e.Kind}"
            }
        }
    }

    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    shown = text.Replace("\r\n", "|")
    print("{result} [{shown}]")
}

// a write of more than 64 KiB, its chunk's size line and the count of the bytes the client got
void | IoError Big() {
    listener = TcpListener.Bind("127.0.0.1:0")
    client = TcpStream.Connect("127.0.0.1:{listener.Port}")
    server = listener.Accept()
    mut body = Bytes()
    for i in 0..<70000 {
        body.Add(97)
    }

    HttpResponse.Stream(200, (HttpWriter w) => w.Write(body)).Write(server)
    server.ShutdownWrite()
    mut got = Bytes()
    while client.Read(mut got, 65536) > 0 {
    }

    text = string.FromUtf8(got) else {
        InvalidUtf8 => "?"
    }

    lines = text.Split("\r\n")
    print("{got.Count} {lines[4]} {lines[5].Bytes.Count} {lines[6]}")
}

void | IoError | HttpError All() {
    Wire(HttpResponse.Stream(200, (HttpWriter w) => {
        w.Write("hello")
        w.Write(Bytes())
        w.Write(" world".Bytes)
    }), false)
    Wire(HttpResponse.Stream(200, (HttpWriter w) => w.Write("")), false)
    events = HttpResponse.Stream(201, (HttpWriter w) => w.Write("data: 1"))
    events.Headers.Set("Content-Type", "text/event-stream")
    Wire(events, false)
    coded = HttpResponse.Stream(200, (HttpWriter w) => w.Write("x"))
    coded.Headers.Set("Transfer-Encoding", "gzip")
    Wire(coded, false)
    sized = HttpResponse.Stream(200, (HttpWriter w) => w.Write("x"))
    sized.Headers.Set("Content-Length", "1")
    Wire(sized, false)
    Wire(HttpResponse.Stream(200, (HttpWriter w) => {
        w.Write("a")
        size = File.Size("response-stream-missing.txt")
        w.Write("{size}")
    }), false)
    Wire(HttpResponse.Stream(200, (HttpWriter w) => {
        size = File.Size("response-stream-missing.txt")
        w.Write("{size}")
    }), false)
    Wire(HttpResponse.Stream(200, (HttpWriter w) => {
        print("called")
        w.Write("c")
    }), true)
    keeper = Keeper()
    Wire(HttpResponse.Stream(200, (HttpWriter w) => {
        keeper.Keep(w)
        w.Write("k")
    }), false)
    keeper.Kept[0].Write("late") else {
        IoError e => print(e)
    }

    Big()
}

All() else {
    IoError e => print(e)
    HttpError e => print(e.Detail)
}
```

Case: [http/serve-stream.kz](../corpus/http/serve-stream.kz)
```kurz
// the loop writes a response a function writes with its `Date`, chunked to HTTP/1.1 and kept alive, and to HTTP/1.0 as it is with
// `Connection: close`, the close ending the body; a `HEAD` gets the head alone and calls no function; the function may read the
// request's body while it writes, a read before its first write sending `100 Continue` and none after it, and what it left unread is
// drained after it, the connection closing where that is past `MaxDrainSize` or the request still awaits `100 Continue`, and a body
// malformed there closing it with that error; a function's `IoError` closes the connection, which gives it; and a `Content-Length`
// the handler set is `InvalidInput` with nothing written, to HTTP/1.0 too
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


// the body of `r` whole, the failures of HTTP as `InvalidData`
Bytes | IoError Whole(HttpRequest r) {
    return r.Body.ReadAll(100) else {
        HttpError e => return IoError(IoKind.InvalidData, 0, e.Detail)
    }
}

paths = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
    w.Write("a")
    w.Write(r.Path)
})
called = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
    print("called {r.Path}")
    w.Write("b")
})
echo = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
    if r.Path == "/late" {
        w.Write("x")
    }

    w.Write(Whole(r))
})
unread = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => w.Write("u"))
failing = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
    w.Write("f")
    size = File.Size("serve-stream-missing.txt")
    w.Write("{size}")
})
sized = (HttpRequest r) => {
    response = HttpResponse.Stream(200, (HttpWriter w) => w.Write("s"))
    response.Headers.Set("Content-Length", "1") else {
        HttpError e => return HttpResponse.Empty(500)
    }

    return response
}
get = "GET /n HTTP/1.1\r\nHost: h\r\n\r\n"
Exchange("GET /a HTTP/1.1\r\nHost: h\r\n\r\nGET /bc HTTP/1.1\r\nHost: h\r\n\r\n", paths, HttpLimits())
Exchange("GET /a HTTP/1.0\r\n\r\n", paths, HttpLimits())
Exchange("HEAD /h HTTP/1.1\r\nHost: h\r\n\r\n{get}", called, HttpLimits())
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\n\r\nabc{get}", echo, HttpLimits())
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\n\r\nabc{get}", unread, HttpLimits())
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\n\r\nabc{get}", unread, HttpLimits(MaxDrainSize: 2))
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\nExpect: 100-continue\r\n\r\nabc{get}", echo, HttpLimits())
Exchange("POST /late HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\nExpect: 100-continue\r\n\r\nabc{get}", echo, HttpLimits())
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nContent-Length: 3\r\nExpect: 100-continue\r\n\r\nabc{get}", unread, HttpLimits())
Exchange("GET /f HTTP/1.1\r\nHost: h\r\n\r\n{get}", failing, HttpLimits())
Exchange("GET /s HTTP/1.0\r\n\r\n", sized, HttpLimits())
Exchange("POST /e HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n{get}", unread, HttpLimits())
```

Case: [http/serve-stream-timeouts.kz](../corpus/http/serve-stream-timeouts.kz)
```kurz
// a streamed response's request body that falls under `MinRequestBodyDataRate` after the function returned closes the connection
// after the whole response, with no `408`, since its status has gone out, and the connection gives `HttpError(Protocol, "request
// timed out")`: a body the function left unread, while it is drained, and one whose read in the function ran out of its time, that
// read giving the same error (section 12, Q12 (b))
// serves `sent`, which the client does not end, on one connection with `handler` within `limits`, and prints the loop's result,
// whether it took 100 ms to 2 s, and what the client got
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


unread = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => w.Write("u"))
swallowing = (HttpRequest r) => HttpResponse.Stream(200, (HttpWriter w) => {
    got = r.Body.ReadAll(100) else {
        IoError e => {
            w.Write("read: {e.Kind}")
            return
        }
        HttpError e => {
            w.Write("read: {e.Detail}")
            return
        }
    }

    w.Write(got)
})
Stall("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab", unread, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms)))
Stall("POST /a HTTP/1.1\r\nHost: h\r\nContent-Length: 10\r\n\r\nab", swallowing, HttpLimits(MinRequestBodyDataRate: HttpRate(1000, 100ms)))
```

### W12 (decided, §12) `Uri`

`Uri.Parse(text)` gives a `Uri`, or a `UriError(UriKind Kind, int At)`, which holds the kind of the
failure and the position of the first byte the parse refused, counted in bytes from 0, as `HexError`
holds its `At`, and no input (§12, the owner, 2026-10-31, "A Uri type", and 2026-11-01, Q3 (a)). A
`Uri` is `scheme://host:port/path?query#fragment`, and `u.Scheme`, `u.Host`, `u.Port`, `u.Path` and
`u.Query` are its parts as written, with no escaping and no unescaping, named after .NET's `Uri`, whose
path is its `AbsolutePath`, whose `Query` keeps the `?` and which lowers the scheme and the host (§12,
assumed). *(assumed, §12, not put to the owner: the port 80 for `http` and 443 for `https` where
none is written, an empty port among them, and -1 for another scheme, the scheme compared without
regard to ASCII case, here and for `Get`'s refusal (W13), as RFC 3986, section 3.1, compares it; `/` for an empty path; the query
without its `?`, as `HttpRequest.Query` holds it (W5); a fragment after `#` and a user's part before
an `@` of the host read, and no part; a host that is a name, an IPv4 address, or an IPv6 address
between brackets, held with them; a `Uri` a class whose parts `Uri.Parse` alone sets, so that it
compares by identity (K4), and `Uri()`, with no text, the empty scheme and host, the port -1, the path
`/` and the empty query, which `Get` refuses as a scheme other than `http` and `https` (W13); and the
values of `UriKind`: `Character`, checked first, for a byte
that no part of a `Uri` takes, all but RFC 3986's letters, digits, `-._~`, delimiters
`:/?#[]@!$&'()*+,;=` and `%`, a space, a control byte and a byte past ASCII among them, and for a `%`
without two hex digits after it; `Scheme` for a scheme that is missing, begins with no letter, or holds
a byte other than a letter, a digit, `+`, `-` and `.`; `Host` for no `//` after the scheme, an empty
host, an address between brackets that is not closed, is empty or holds a byte other than a hex digit,
`:` and `.`, a name that holds `[` or `]`, and a byte after the brackets, before the first `/`, `?` or
`#`, other than `:`; and `Port` for a byte that is no digit, at that byte, and a number past 65535, at
the digit that passes it; `At` 0 for a text that is empty or begins with no letter, the text's length
for a scheme with no `:` after it, and the `%` for a `%` without two hex digits)*

Case: [http/uri-parse.kz](../corpus/http/uri-parse.kz)
```kurz
// `Uri.Parse` gives the parts of an address as written, the scheme's port where none is written, `/` for an empty path and the
// query without its `?`, a fragment and a user's part read and dropped; and for text that is no address the kind of the failure
// and the position of the first byte refused
void Show(string text) {
    match Uri.Parse(text) {
        Uri u => print("{u.Scheme}|{u.Host}|{u.Port}|{u.Path}|{u.Query}")
        UriError e => print("{e.Kind} {e.At}")
    }
}

Show("http://127.0.0.1:8080/a/b?x=1&y=2#top")
Show("HTTP://example.com")
Show("https://[::1]/")
Show("http://user:pw@host:81/p?")
Show("ftp://files.example/readme")
Show("http://example.com?q")
Show("http://hh:/x")
Show("http://h#frag")
Show("http://h:65535/a%20b?c=%41")
Show("")
Show("/relative")
Show("example.com")
Show("1http://x")
Show("ht_tp://x")
Show("http:x")
Show("http:///x")
Show("http://[::1/")
Show("http://[]/")
Show("http://[::1]x/")
Show("http://a]b/")
Show("http://h:8x/")
Show("http://h:65536/")
Show("http://a b/")
Show("http://h/%zz")
Show("http://h/%4")
Show("http://h/é")
```

### W13 (decided, §12) The client and its pool

`HttpClient()` makes a client with `HttpClientOptions()`, and `HttpClient(options)` one with other
options, as .NET's `HttpClient`, `client.Options` being the options it was made with (§12, the owner,
2026-10-31, "Instance", the member assumed); `client.Get(uri)` sends a
`GET` of the `Uri` and gives `HttpResponseMessage | IoError | HttpError`, the failures of the network
an `IoError` with the texts of a lookup and a `Connect` (S9) and those of HTTP an `HttpError`, neither
wrapped in the other (§12, the owner, 2026-10-31, "IoError + HttpError"), and an address of `https`
`HttpError(Unsupported, "https is not supported: no TLS")`, before any connect (§12, the same answer).
A client keeps a pool of connections, several to each host, as .NET's `SocketsHttpHandler` keeps
one, each connection carrying one request at a time, as hyper's client `conn` carries it, and a
response whose body is open holds its connection, so that two responses read side by side use two
(§12, the owner, 2026-10-31, "Full pool"). The pool takes .NET's defaults, no limit on the connections
to one host, an idle connection evicted after 1 minute and no bound on a connection's life, each set
by `HttpClientOptions` (§12, the owner, 2026-11-01, Q5 (a)). A call that finds `MaxConnectionsPerServer`
connections to its host and port held by open bodies gives `HttpError(TooLarge, "too many connections to
the host")` at once, since in a program of one actor no other actor gives one back (C3); a scheme other
than `http` and `https` gives `HttpError(Unsupported, "unsupported scheme")`, before any connect (C4);
and the reads, the writes and the connect of a connection are bounded by the call's time alone (W16),
in the place of C1's reading, which bounded the reads and writes by no time and the connect by the 5
seconds of `Connect` while `std/` held no bound of the call's 100 seconds (C1) (§12, the owner,
2026-11-05, issue 110, comment 6657251329). A program of one actor keeps the pool in that actor, which
it cannot tell from the pool's own actor (§12, assumed). *(assumed, §12: the options with .NET's names,
`MaxConnectionsPerServer = 2147483647`, .NET's `int.MaxValue`, `PooledConnectionIdleTimeout = 1min`,
`PooledConnectionLifetime` the largest `duration`, since no member of `HttpLimits` turns off (W7),
`MaxAutomaticRedirections`, the count of redirects (W15), and `Limits = HttpLimits()` (W14); a
`MaxConnectionsPerServer` at or below 0 giving `too many connections to the host` to every call, a
`PooledConnectionIdleTimeout` or `PooledConnectionLifetime` at or below 0 passed by each connection by
the checkout after its body, which closes it, and a `MaxAutomaticRedirections` at or below 0 following
none (W15), none of these values refused; a pool with no timer, which at
each call takes back each connection whose body ended, closes each whose response said
`Connection: close`, was of HTTP/1.0 without `Connection: keep-alive`, was read to the close or failed,
and each whose body was let go of unread, then closes each connection idle longer than
`PooledConnectionIdleTimeout` since its body ended or open longer than `PooledConnectionLifetime`, as
hyper-util's pool passes over expired connections at its checkout, and takes the connection of the
host and port that went idle last, as hyper-util's `pop()` takes it; a request on a connection of the
pool that cannot be written, or whose connection ends before the first byte of a response, sent once
more on a new connection, as .NET sends a request again on a connection the server shut down, and a
new connection given no second try; the request line `GET`, the path, with `?` and the query after it
where the query is not empty, and `HTTP/1.1`, so that `http://h/p?` and `http://h/p` send `/p`, and the
request with `Host`, the port in it where it is not 80, and no other header; the connect a call of `TcpStream.Connect`, so that a package on
the path of a call of `Get` needs `allow network` (S12); and the pool's times read from `instant.Now` (S25), as the server's
bounds read theirs)*

Case: [http/client-get.kz](../corpus/http/client-get.kz)
```kurz
// a client's `Get` gives the status, the headers and the body of the response, and the next `Get` to the same host and port is
// carried on the connection the first one opened, once its body was read to its end; a status that is no success is a response
// like any other, which `EnsureSuccess` turns into an `HttpError` of the kind `Status`; the request a `GET` with `Host` alone, the
// port in it
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request answered with the number of its
// connection, its path and its query, the path `/missing` with `404`, and the path `/request` with the count of the request's
// headers and its `Host`
void Serve(TcpListener on) {
    mut count = 0
    while true {
        stream = on.Accept() else {
            IoError e => return
        }

        count = count + 1
        number = count
        HttpServer.ServeConnection(stream, (HttpRequest r) => Answer(number, r)) else {
            IoError e => {
            }
            HttpError e => {
            }
        }
    }
}

HttpResponse Answer(int number, HttpRequest r) {
    if r.Path == "/missing" {
        return HttpResponse.Text(404, "none")
    }

    if r.Path == "/request" {
        host = r.Headers.Get("Host") ?? "none"
        return HttpResponse.Text(200, "{r.Headers.Count} {host}")
    }

    return HttpResponse.Text(200, "{number} {r.Path} {r.Query}".Trim())
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

void | IoError | HttpError | InvalidUtf8 Run(int port) {
    client = HttpClient()
    first = client.Get(Address(port, "/a"))
    kind = first.Headers.Get("Content-Type") ?? "none"
    text = first.Body.ReadAllText(100)
    print("{first.Status} {kind} {text}")

    second = client.Get(Address(port, "/b?x=1"))
    print("{second.Status} {second.Body.ReadAllText(100)}")

    missing = client.Get(Address(port, "/missing"))
    print("{missing.Status} {missing.IsSuccess} {missing.Body.ReadAllText(100)}")
    missing.EnsureSuccess() else {
        HttpError e => print("{e.Kind}: {e.Detail}")
    }

    third = client.Get(Address(port, "/c"))
    third.EnsureSuccess()
    print("{third.Status} {third.IsSuccess} {third.Body.ReadAllText(100)}")

    request = client.Get(Address(port, "/request"))
    sent = request.Body.ReadAllText(100)
    print("{request.Status} {sent.Replace(":{port}", ":PORT")}")
}

listener = TcpListener.Bind("127.0.0.1:0") else {
    IoError e => {
        print(e)
        return
    }
}

// the listener's `Accept` waits at most 10 seconds, so that a child whose program ended before it could end the child ends by
// itself and does not hold the output the suite reads
listener.SetAcceptTimeout(10s) else {
    IoError e => {
        print(e)
        return
    }
}

mut child = 0
raw {
    child = fork()
}

// a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
if child < 0 {
    print("fork failed")
    return
}

if child == 0 {
    Serve(listener)
    raw {
        _exit(0)
    }
}

Run(listener.Port) else {
    IoError e => print(e)
    HttpError e => print("{e.Kind}: {e.Detail}")
    InvalidUtf8 => print("not UTF-8")
}

raw {
    kill(child, 9)
    waitpid(child, 0, 0)
}
```

Case: [http/client-pool.kz](../corpus/http/client-pool.kz)
```kurz
// a client's pool: a response whose body is open holds its connection, so that a second `Get` opens a second one, past
// `MaxConnectionsPerServer` an `HttpError`; a body read to its end gives its connection back, even where its response is let go of
// after the read, and the next `Get` takes it; a new connection is opened where the response let go of its body unread, where an
// idle connection outlived `PooledConnectionIdleTimeout` or `PooledConnectionLifetime`, where the response said `Connection: close`,
// and where the server closed the idle connection, the request answered on a new one; and of two idle connections to the host
// the next `Get` takes the one whose body ended last
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    int usleep(int microseconds)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request answered with the number of its
// connection and its path, the path `/close` with `Connection: close`, and the connection closed after `most` responses, or where
// the client ends it, its reads bounded by no time, so that no timer of the server ends a connection; `most` 0 is `Two`'s server
void Serve(TcpListener on, int most) {
    if most == 0 {
        Two(on)
        return
    }

    mut number = 0
    while true {
        stream = on.Accept() else {
            IoError e => return
        }

        number = number + 1
        stream.SetReadTimeout(null) else {
            IoError e => {
            }
        }

        mut served = 0
        mut open = true
        while open && served < most {
            match HttpRequest.Read(stream) {
                HttpRequest r => {
                    open = Answer(stream, number, r.Path) && r.Path != "/close"
                    served = served + 1
                }
                IoError e => {
                    open = false
                }
                HttpError e => {
                    open = false
                }
            }
        }

        stream.Close() else {
            IoError e => {
            }
        }
    }
}

// two connections open at once, each answering one request, and the third request read on the first connection, or on the second
// where none comes on the first within 10 seconds, each answered with the number of its connection
void Two(TcpListener on) {
    one = on.Accept() else {
        IoError e => return
    }

    Next(one, 1)
    two = on.Accept() else {
        IoError e => return
    }

    Next(two, 2)
    one.SetReadTimeout(10s) else {
        IoError e => {
        }
    }

    if !Next(one, 1) {
        Next(two, 2)
    }
}

// the next request of `stream` read and answered: whether one came
bool Next(TcpStream stream, int number) {
    match HttpRequest.Read(stream) {
        HttpRequest r => return Answer(stream, number, r.Path)
        IoError e => return false
        HttpError e => return false
    }
}

// the response of the connection `number` to a request of `path`, its body the number and the path: whether it was written
bool Answer(TcpStream stream, int number, string path) {
    body = "{number} {path}"
    length = body.Bytes.Count
    mut close = ""
    if path == "/close" {
        close = "Connection: close\r\n"
    }

    stream.Write("HTTP/1.1 200 OK\r\nContent-Length: {length}\r\n{close}\r\n{body}".Bytes) else {
        IoError e => return false
    }

    return true
}

// a server started in a child on a port of its own, and the client's part run against it, and the child ended; the listener's
// `Accept` waits at most 10 seconds, so that a child whose program ended before it could end the child ends by itself and does not
// hold the output the suite reads
void Against(int most, (int) => void | IoError | HttpError | InvalidUtf8 part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on, most)
        raw {
            _exit(0)
        }
    }

    part(on.Port) else {
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
        InvalidUtf8 => print("not UTF-8")
    }

    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

// the body of a `GET` of `path`, the response let go of after the read
string | IoError | HttpError | InvalidUtf8 Fetch(HttpClient client, int port, string path) {
    response = client.Get(Address(port, path))
    return response.Body.ReadAllText(100)
}

// the status of a `GET` of `path`, the response let go of with its body unread
int | IoError | HttpError Peek(HttpClient client, int port, string path) {
    response = client.Get(Address(port, path))
    return response.Status
}

void Wait(int milliseconds) {
    raw {
        usleep(milliseconds * 1000)
    }
}

void | IoError | HttpError | InvalidUtf8 SideBySide(int port) {
    client = HttpClient()
    first = client.Get(Address(port, "/a"))
    second = client.Get(Address(port, "/b"))
    print(second.Body.ReadAllText(100))
    print(first.Body.ReadAllText(100))
}

void | IoError | HttpError | InvalidUtf8 Limited(int port) {
    client = HttpClient(HttpClientOptions(MaxConnectionsPerServer: 1))
    first = client.Get(Address(port, "/a"))
    match client.Get(Address(port, "/b")) {
        HttpResponseMessage r => print("got {r.Status}")
        IoError e => print(e)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }

    print(first.Body.ReadAllText(100))
    print(Fetch(client, port, "/c"))
}

void | IoError | HttpError | InvalidUtf8 ReadThenLetGo(int port) {
    client = HttpClient()
    Fetch(client, port, "/a")
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 LetGoUnread(int port) {
    client = HttpClient()
    Peek(client, port, "/a")
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 IdleTooLong(int port) {
    client = HttpClient(HttpClientOptions(PooledConnectionIdleTimeout: 1ms))
    Fetch(client, port, "/a")
    Wait(20)
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 LivedTooLong(int port) {
    client = HttpClient(HttpClientOptions(PooledConnectionLifetime: 1ms))
    Fetch(client, port, "/a")
    Wait(20)
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 Closing(int port) {
    client = HttpClient()
    Fetch(client, port, "/close")
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 ClosedByServer(int port) {
    client = HttpClient()
    Fetch(client, port, "/a")
    print(Fetch(client, port, "/b"))
}

void | IoError | HttpError | InvalidUtf8 LatestIdle(int port) {
    client = HttpClient()
    first = client.Get(Address(port, "/a"))
    second = client.Get(Address(port, "/b"))
    print(second.Body.ReadAllText(100))
    print(first.Body.ReadAllText(100))
    print(Fetch(client, port, "/c"))
}

Against(1, (int port) => SideBySide(port))
Against(2, (int port) => Limited(port))
Against(2, (int port) => ReadThenLetGo(port))
Against(2, (int port) => LetGoUnread(port))
Against(2, (int port) => IdleTooLong(port))
Against(2, (int port) => LivedTooLong(port))
Against(2, (int port) => Closing(port))
Against(1, (int port) => ClosedByServer(port))
Against(0, (int port) => LatestIdle(port))
```

Case: [http/client-errors.kz](../corpus/http/client-errors.kz)
```kurz
// an address of `https` and one of another scheme than `http` are refused before any connect, the failure of a connect is the
// network's `IoError`, and `HttpClientOptions()` holds .NET's defaults, no bound on the connections to one host, an idle
// connection kept 1 minute and no bound on a connection's life, and 20 redirects, the owner's count, as browsers allow, where
// .NET follows 50, and the limits of a request's head for a response's
void Show(HttpClient client, string text) {
    at = Uri.Parse(text) else {
        UriError e => {
            print("{e.Kind} {e.At}")
            return
        }
    }

    match client.Get(at) {
        HttpResponseMessage r => print(r.Status)
        IoError e => print(e.Kind)
        HttpError e => print("{e.Kind}: {e.Detail}")
    }
}

plain = HttpClient()
Show(plain, "https://127.0.0.1:1/")
Show(plain, "ftp://127.0.0.1:1/")
listener = TcpListener.Bind("127.0.0.1:0") else {
    IoError e => {
        print(e)
        return
    }
}

port = listener.Port
listener.Close() else {
    IoError e => print(e)
}

Show(plain, "http://127.0.0.1:{port}/")
o = HttpClientOptions()
print("{o.MaxConnectionsPerServer} {o.PooledConnectionIdleTimeout == 1min} {o.PooledConnectionLifetime == 9223372036854775807ns} {o.MaxAutomaticRedirections} {o.Limits == HttpLimits()}")
```

Case: [http/client-malformed.kz](../corpus/http/client-malformed.kz)
```kurz
// a head that is no response of HTTP/1.x is an `HttpError` in the words of the server's for a request, `response` in place of
// `request`, and a connection that ends before a byte of a response `malformed response: no status line`, a new connection that
// ends so given no second try; a status line may leave its reason out
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request on it read and answered with the next of
// `answers`, each `#` in it the number of the connection, and the connection closed where the next is `close` or the client ends it
void Serve(TcpListener on, List<string> answers) {
    mut next = 0
    mut number = 0
    while next < answers.Count {
        stream = on.Accept() else {
            IoError e => return
        }

        number = number + 1
        mut open = true
        while open && next < answers.Count {
            match HttpRequest.Read(stream) {
                HttpRequest r => {
                    stream.Write(answers[next].Replace("#", "{number}").Bytes) else {
                        IoError e => {
                            open = false
                        }
                    }

                    next = next + 1
                    if next < answers.Count && answers[next] == "close" {
                        next = next + 1
                        open = false
                    }
                }
                IoError e => {
                    open = false
                }
                HttpError e => {
                    open = false
                }
            }
        }

        stream.Close() else {
            IoError e => {
            }
        }
    }

    // a connection past the answers, a request the client should not have sent, closed at once, so that it shows as a line of the
    // output and not as a call that waits on a connection nobody serves
    while true {
        extra = on.Accept() else {
            IoError e => return
        }

        extra.Close() else {
            IoError e => {
            }
        }
    }
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against(List<string> answers, (int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on, answers)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

// a `GET` of `/` and its status and body, or its failure
string Show(HttpClient client, int port) {
    match client.Get(Address(port, "/")) {
        HttpResponseMessage r => {
            match r.Body.ReadAllText(100) {
                string text => return "{r.Status} [{text}]"
                IoError e => return "{r.Status} {e}"
                HttpError e => return "{r.Status} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} not UTF-8"
            }
        }
        IoError e => return "{e}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Responses(int port) {
    client = HttpClient()
    for i in 0..<19 {
        print(Show(client, port))
    }
}

Against([
    "HTTX/1.1 200 OK\r\n\r\n",
    "HTTP/2.0 200 OK\r\n\r\n",
    "HTTP/1.1 20 OK\r\n\r\n",
    "HTTP/1.1 099 Low\r\n\r\n",
    "HTTP/1.1 200OK\r\n\r\n",
    "HTTP/1.1 200\r\nContent-Length: 2\r\n\r\nok",
    "close",
    "HTTP/1.1 200 OK\r\n Folded: x\r\n\r\n",
    "HTTP/1.1 200 OK\r\nBad Name: x\r\n\r\n",
    "HTTP/1.1 200 OK\r\nNoColon\r\n\r\n",
    "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nX: a\rbc: d\r\n\r\n",
    "HTTP/1.1 200 OK\r\nContent-Length: x\r\n\r\n",
    "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Length: 2\r\n\r\n",
    "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nTransfer-Encoding: chunked\r\n\r\n",
    "HTTP/1.1 200 OK\r\nTransfer-Encoding: gzip\r\n\r\n",
    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: x\r\n\r\n",
    "HTTP/1.1 200 OK\nX: y\r\n\r\n",
    "HTTP/1.1 200 OK\r\nX: y",
    "close",
    "",
    "close",
    "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nafter",
], (int port) => Responses(port))
```

No case: of the port 80 left out of `Host`, since that case would listen on port 80, which a run of the suite may not be
allowed to bind; and of the grant, since a case is a program and no package, and the program needs no grant (S12).

### W14 (decided, §12) Reading a response

A response's body is an `HttpBody` (W6), read as a stream with `Read`, or by `ReadAll(max)` and
`ReadAllText(max)`, past whose bound it is `HttpError(TooLarge, "response body over limit")`, a chunked
one decoded (§12, the owner, 2026-10-31, "Stream + bounded helpers", and 2026-11-01, Q2 (a)); a
malformed response is `HttpError(Protocol, "malformed response: <what>")`, and a head over its limit
`HttpError(TooLarge, "response header too large")` (§12, the owner, 2026-10-31, "IoError + HttpError").
A status of `404` or `500` is a response like any other: `response.IsSuccess` holds from 200 to 299,
and `response.EnsureSuccess()` gives `void | HttpError`, of the kind `Status` for a status that is no
success (§12, the owner, 2026-10-31, "Never, a value", and 2026-11-01, issue 110, comment 6557433114,
Q6 (a), the range assumed).
The response is an `HttpResponseMessage`, as .NET names it, with `Status`, `Headers` and `Body`, as the
client's request is an `HttpRequestMessage` (2026-11-04, issue 156, comment 6643376941, Q2 (a) of comment
6620774918), and
its failures take the texts of the server for a
request with `response` in their place (C4) (§12, the owner, 2026-11-05, issue 110, comment
6657251329): `malformed response: no status line` for a
connection that ends before the first byte of a response, `incomplete` for one that ends inside the
head, `bad status line`, `line not ended by CR LF`, the server's words for a header line, `bad
Content-Length` and `Content-Length beside Transfer-Encoding`, `unexpected end of body`, `bad chunk`
and `bad chunk size`, `unexpected 101` for a `101`, each after `malformed response: ` and of the kind
`Protocol` (above), `HttpError(Unsupported, "unsupported HTTP
version")` for a version other than 1.0 and 1.1 and `HttpError(Unsupported, "unsupported transfer
encoding")` for a coding other than `chunked`, and `HttpError(Status, "response status 404")` from
`EnsureSuccess()`, the status in it. *(assumed, §12: a status line of `HTTP/1.0` or `HTTP/1.1`, three digits from 100
and a reason that may be left out, as httparse reads it; a `1xx` other than `101` read past, as hyper
reads past it; no body for a `1xx`, a `204` and a `304`, chunks where `Transfer-Encoding` is
`chunked`, the length of `Content-Length`, and otherwise the bytes up to the close (RFC 9112, section
6.3); and a response's head held to the limits of a request's head in the client's `Limits`, the status
line to `MaxRequestLineSize` bytes with its CR LF, the header lines to `MaxRequestHeadersTotalSize`
bytes with theirs, the empty line that ends the head counted in neither, and their count to
`MaxRequestHeaderCount`, each past its limit `response header too large` (above))*

Case: [http/client-get.kz](../corpus/http/client-get.kz)

Case: [http/client-response.kz](../corpus/http/client-response.kz)
```kurz
// a response's body is chunked, of its `Content-Length`, or read to the close where its head says neither, and a `1xx`, a `204` and
// a `304` have none; the trailer of a chunked body is read past, each line within the size line, and the connection serves the
// next response; a `1xx` before the response is read past; a response of HTTP/1.0 keeps its connection only with
// `Connection: keep-alive`, and one with `Connection: close` or read to the close keeps it not; `ReadAllText(max)` past its bound and a
// body that ends early or is malformed give the response's errors
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request on it read and answered with the next of
// `answers`, each `#` in it the number of the connection, and the connection closed where the next is `close` or the client ends it
void Serve(TcpListener on, List<string> answers) {
    mut next = 0
    mut number = 0
    while next < answers.Count {
        stream = on.Accept() else {
            IoError e => return
        }

        number = number + 1
        mut open = true
        while open && next < answers.Count {
            match HttpRequest.Read(stream) {
                HttpRequest r => {
                    stream.Write(answers[next].Replace("#", "{number}").Bytes) else {
                        IoError e => {
                            open = false
                        }
                    }

                    next = next + 1
                    if next < answers.Count && answers[next] == "close" {
                        next = next + 1
                        open = false
                    }
                }
                IoError e => {
                    open = false
                }
                HttpError e => {
                    open = false
                }
            }
        }

        stream.Close() else {
            IoError e => {
            }
        }
    }

    // a connection past the answers, a request the client should not have sent, closed at once, so that it shows as a line of the
    // output and not as a call that waits on a connection nobody serves
    while true {
        extra = on.Accept() else {
            IoError e => return
        }

        extra.Close() else {
            IoError e => {
            }
        }
    }
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against(List<string> answers, (int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on, answers)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

// a `GET` of `/` and its response: the status, the server's number of the connection, the count of the headers and the body read
// as text within `max` bytes, or the failure; the response let go of at the return
string Show(HttpClient client, int port, int max) {
    match client.Get(Address(port, "/")) {
        HttpResponseMessage r => {
            number = r.Headers.Get("X-Connection") ?? "-"
            match r.Body.ReadAllText(max) {
                string text => return "{r.Status} {number} {r.Headers.Count} [{text}]"
                IoError e => return "{r.Status} {number} {e}"
                HttpError e => return "{r.Status} {number} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} {number} not UTF-8"
            }
        }
        IoError e => return "{e}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Responses(int port) {
    client = HttpClient()
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
    print(Show(client, port, 10))
    print(Show(client, port, 10))
    print(Show(client, port, 100))
    print(Show(client, port, 100))
}

Against([
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\n4\r\nwiki\r\n5\r\npedia\r\n0\r\n\r\n",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nafter\r\n0\r\nX-Checksum: 1\r\nX-Count: 2\r\n\r\n",
    "HTTP/1.1 304 Not Modified\r\nX-Connection: #\r\nContent-Length: 5\r\n\r\n",
    "HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 103 Early Hints\r\nLink: </s>\r\n\r\nHTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 2\r\n\r\nok",
    "HTTP/1.1 204 No Content\r\nX-Connection: #\r\n\r\n",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\n\r\nall of it",
    "close",
    "HTTP/1.0 200 OK\r\nX-Connection: #\r\nContent-Length: 3\r\n\r\none",
    "HTTP/1.0 200 OK\r\nX-Connection: #\r\nConnection: keep-alive\r\nContent-Length: 3\r\n\r\ntwo",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 5\r\nConnection: close\r\n\r\nthree",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 20\r\n\r\n01234567890123456789",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\n14\r\n01234567890123456789\r\n0\r\n\r\n",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 10\r\n\r\nabc",
    "close",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n",
    "close",
], (int port) => Responses(port))
```

Case: [http/client-malformed.kz](../corpus/http/client-malformed.kz)

Case: [http/client-limits.kz](../corpus/http/client-limits.kz)
```kurz
// a response's head is held to the limits of a request's head in the client's `Limits`: its status line to `MaxRequestLineSize`
// bytes with its CR LF, its header lines to `MaxRequestHeadersTotalSize` bytes with theirs, and their count to
// `MaxRequestHeaderCount`, each past its limit `response header too large`
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request on it read and answered with the next of
// `answers`, each `#` in it the number of the connection, and the connection closed where the next is `close` or the client ends it
void Serve(TcpListener on, List<string> answers) {
    mut next = 0
    mut number = 0
    while next < answers.Count {
        stream = on.Accept() else {
            IoError e => return
        }

        number = number + 1
        mut open = true
        while open && next < answers.Count {
            match HttpRequest.Read(stream) {
                HttpRequest r => {
                    stream.Write(answers[next].Replace("#", "{number}").Bytes) else {
                        IoError e => {
                            open = false
                        }
                    }

                    next = next + 1
                    if next < answers.Count && answers[next] == "close" {
                        next = next + 1
                        open = false
                    }
                }
                IoError e => {
                    open = false
                }
                HttpError e => {
                    open = false
                }
            }
        }

        stream.Close() else {
            IoError e => {
            }
        }
    }

    // a connection past the answers, a request the client should not have sent, closed at once, so that it shows as a line of the
    // output and not as a call that waits on a connection nobody serves
    while true {
        extra = on.Accept() else {
            IoError e => return
        }

        extra.Close() else {
            IoError e => {
            }
        }
    }
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against(List<string> answers, (int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on, answers)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

// a `GET` of `/` and its status and body, or its failure
string Show(HttpClient client, int port) {
    match client.Get(Address(port, "/")) {
        HttpResponseMessage r => {
            match r.Body.ReadAllText(100) {
                string text => return "{r.Status} [{text}]"
                IoError e => return "{r.Status} {e}"
                HttpError e => return "{r.Status} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} not UTF-8"
            }
        }
        IoError e => return "{e}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Responses(int port) {
    client = HttpClient(HttpClientOptions(Limits: HttpLimits(MaxRequestLineSize: 20, MaxRequestHeadersTotalSize: 40, MaxRequestHeaderCount: 2)))
    for i in 0..<6 {
        print(Show(client, port))
    }
}

Against([
    "HTTP/1.1 204 ABCDEF\r\n\r\n",
    "HTTP/1.1 204 ABCDE\r\n\r\n",
    "HTTP/1.1 204 OK\r\nX: 12345678901234567890123456789012345\r\n\r\n",
    "HTTP/1.1 204 OK\r\nX: 123456789012345678901234567890123456\r\n\r\n",
    "HTTP/1.1 204 OK\r\nA: 1\r\nB: 2\r\nC: 3\r\n\r\n",
    "HTTP/1.1 204 OK\r\nA: 1\r\nB: 2\r\n\r\n",
], (int port) => Responses(port))
```

### W15 (decided, §12) Redirects

The client follows up to 20 redirects: a `301`, `302` or `303` becomes a `GET`, a `307` or `308` keeps
its method, a redirect to `https` is the error of an address of `https`, a chain past its count
`HttpError(Protocol, "too many redirects")`, and the options set the count, 0 for none (§12, the
owner, 2026-10-31, "Up to 5, as ureq", and 2026-11-06, issue 39, comment 6683103582, the count 20, as
browsers allow). `Get` sends neither `Authorization` nor `Cookie`, so that their
drop where the host changes (§12, the same answer) has nothing to drop in it. *(assumed, §12: the
option's name `MaxAutomaticRedirections`, as .NET's, and a count at or below 0 giving the first
redirect as the response; a `Location` resolved against the address it
answers (RFC 3986, section 5.2), its dot segments removed; a redirect without a `Location`, or with
one that resolves to no `Uri`, the response, as .NET gives it; and the body of a redirect drained up to
`MaxDrainSize` (W7), so that its connection goes back to the pool, and its connection closed past it)*

Case: [http/client-redirects.kz](../corpus/http/client-redirects.kz)
```kurz
// a client follows up to 20 redirects of a `GET`, a `301`, `302`, `303`, `307` and `308` alike, each `Location` resolved against
// the address it answers, on the connection the chain began on; a twenty-first is `too many redirects`, a redirect to `https` the
// error of an address of `https`, and a redirect without a `Location`, or with one that is no `Uri`, is the response;
// `MaxAutomaticRedirections` sets the count, 0 for none; and a redirect whose body is longer than `MaxDrainSize` closes its
// connection
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request answered by `Answer`
void Serve(TcpListener on) {
    mut count = 0
    while true {
        stream = on.Accept() else {
            IoError e => return
        }

        count = count + 1
        number = count
        port = on.Port
        HttpServer.ServeConnection(stream, (HttpRequest r) => Answer(port, number, r)) else {
            IoError e => {
            }
            HttpError e => {
            }
        }
    }
}

HttpResponse Redirect(int status, string location) {
    response = HttpResponse.Empty(status)
    response.Headers.Set("Location", location) else {
        HttpError e => {
        }
    }

    return response
}

// `/r/0` the number of the connection and the path, `/r/N` a `302` to `/r/N-1`, and the other paths their redirects
HttpResponse Answer(int port, int number, HttpRequest r) {
    path = r.Path
    if path.StartsWith("/r/") {
        b = path.Bytes
        mut n = 0
        for i in 3..<b.Count {
            n = n * 10 + (b[i] - 48)
        }

        if n == 0 {
            return HttpResponse.Text(200, "{number} {path}")
        }

        return Redirect(302, "/r/{n - 1}")
    }

    if path == "/see" {
        return Redirect(303, "/r/0")
    }

    if path == "/moved" {
        return Redirect(301, "http://127.0.0.1:{port}/r/0")
    }

    if path == "/dir/temp" {
        return Redirect(307, "../r/0")
    }

    if path == "/perm" {
        if r.Query == "q=1" {
            return HttpResponse.Text(200, "{number} /perm q=1")
        }

        return Redirect(308, "?q=1")
    }

    if path == "/secure" {
        return Redirect(302, "https://127.0.0.1/")
    }

    if path == "/bad" {
        return Redirect(302, "http://a b/")
    }

    if path == "/long" {
        response = HttpResponse.Text(302, "0123456789")
        response.Headers.Set("Location", "/r/0") else {
            HttpError e => {
            }
        }

        return response
    }

    return HttpResponse.Empty(302)
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against((int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// a `GET` of `path` and its status and body, or its failure
string Show(HttpClient client, int port, string path) {
    at = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return "{e.Kind} {e.At}"
    }

    match client.Get(at) {
        HttpResponseMessage r => {
            match r.Body.ReadAllText(100) {
                string text => return "{r.Status} [{text}]"
                IoError e => return "{r.Status} {e}"
                HttpError e => return "{r.Status} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} not UTF-8"
            }
        }
        IoError e => return "{e}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Followed(int port) {
    client = HttpClient()
    print(Show(client, port, "/r/20"))
    print(Show(client, port, "/see"))
    print(Show(client, port, "/moved"))
    print(Show(client, port, "/dir/temp"))
    print(Show(client, port, "/perm"))
    print(Show(client, port, "/secure"))
    print(Show(client, port, "/none"))
    print(Show(client, port, "/bad"))
    print(Show(client, port, "/r/21"))
    print(Show(client, port, "/r/0"))
}

void None(int port) {
    print(Show(HttpClient(HttpClientOptions(MaxAutomaticRedirections: 0)), port, "/r/1"))
}

void One(int port) {
    client = HttpClient(HttpClientOptions(MaxAutomaticRedirections: 1))
    print(Show(client, port, "/r/1"))
    print(Show(client, port, "/r/2"))
}

void Long(int port) {
    print(Show(HttpClient(HttpClientOptions(Limits: HttpLimits(MaxDrainSize: 4))), port, "/long"))
}

Against((int port) => Followed(port))
Against((int port) => None(port))
Against((int port) => One(port))
Against((int port) => Long(port))
```

### W16 (decided, §12) The call's time

A call of the client ends with `IoError(TimedOut)` after 100 seconds, from the lookup of the name to the
last byte the helpers read, and the options change it, as .NET's `HttpClient.Timeout` (§12, the owner,
2026-10-31, "100 s, as .NET"); each read on the client's sockets is bounded by what remains of the call's
time, set before it, and a socket's 5 seconds do not apply inside the client (§12, the owner, 2026-11-01,
"Reads use the 100 s budget"). The member `Timeout` of `HttpClientOptions`, a `duration`, sets the call's
time, 100 seconds by default; a time of zero or below makes each call `TimedOut` at once, before any
lookup or connect, and no value turns the bound off; every end of the call's time is `IoError(TimedOut,
0, "")`, as the bound of `Connect` gives it, a read or a write that the system ended with its 11 among
them; and one time bounds the whole call, its redirects (W15), the drains of their bodies and a request
sent once more on a new connection (W13) among it, as .NET's `Timeout` covers the whole of a send (§12,
the owner, 2026-11-05, issue 110, comment 6657251329, T1 to T3). *(assumed, §12: a `TimedOut` the system
gives of its own while the call's time runs, its 110, in the form of every other end of the call's time,
since the client does not tell it from its own; the client reading what remains before each step and
giving `TimedOut` when nothing remains, so that it passes no time of zero or below to the setters and to
`Connect`, which refuse it (S9); each write bounded by what remains in the same way, and the connect
through the second form of `Connect`, given what remains, with no 5 seconds of its own; a body read with
`Read` held to the call's time as the helpers' reads are; a request whose time ran out not sent once more
on a new connection, and its connection not given back to the pool, closed when the program lets go of
the response or, where the time ran out before a response, at once; the bound held by the socket's own
times, `SO_RCVTIMEO` and `SO_SNDTIMEO`, set before each read and each write that calls the system, as the
server holds its bounds (W10), how it holds on an actor's socket standing open (§14); the lookup of the
name bounded by nothing and its time counted against the call's, how the call's time holds over it
standing open (§14), so that a connect given what remains can end later than the call's time by the
lookup's, and the step after it then finds nothing remaining; and the time read from `instant.Now` (S25)
as the time passed against the bound, never as the sum of the start and the bound, which can overflow
(T36))*

Case: [http/client-timeout.kz](../corpus/http/client-timeout.kz)
```kurz
// a call of the client has 100 seconds by default, which the options set; a time of zero or below gives `TimedOut` with the number
// 0 before any connect; the largest `duration` is a time like any other, which the call's arithmetic holds without passing the
// clock's range; a read waits what remains of the call's time and not a socket's 5 seconds; a chain of redirects has one
// time in all; a server that does not answer in time ends the call with `TimedOut` after its time, and so does a connect that the
// server's queue holds
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    int usleep(int microseconds)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request answered by `Answer`
void Serve(TcpListener on) {
    mut count = 0
    while true {
        stream = on.Accept() else {
            IoError e => return
        }

        count = count + 1
        number = count
        HttpServer.ServeConnection(stream, (HttpRequest r) => Answer(number, r)) else {
            IoError e => {
            }
            HttpError e => {
            }
        }
    }
}

// the child's thread held for `ms` milliseconds
void Sleep(int ms) {
    raw {
        usleep(ms * 1000)
    }
}

// `/slow/5200` and `/slow/1000` answered after that many milliseconds, `/r/N` a `302` to `/r/N-1` after 150 milliseconds, and the
// other paths with the number of the connection and the path
HttpResponse Answer(int number, HttpRequest r) {
    path = r.Path
    if path == "/slow/5200" || path == "/slow/1000" {
        if path == "/slow/5200" {
            Sleep(5200)
        } else {
            Sleep(1000)
        }

        return HttpResponse.Text(200, "late")
    }

    if path.StartsWith("/r/") && path != "/r/0" {
        n = path.Bytes[3] - 48
        Sleep(150)
        response = HttpResponse.Empty(302)
        response.Headers.Set("Location", "/r/{n - 1}") else {
            HttpError e => {
            }
        }

        return response
    }

    return HttpResponse.Text(200, "{number} {path}")
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against((int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// a `GET` of `path` with a call's time of `timeout`, and its status and body, or its failure
string Show(int port, string path, duration timeout) {
    at = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return "{e.Kind} {e.At}"
    }

    match HttpClient(HttpClientOptions(Timeout: timeout)).Get(at) {
        HttpResponseMessage r => {
            match r.Body.ReadAllText(100) {
                string text => return "{r.Status} [{text}]"
                IoError e => return "{r.Status} {e.Kind} {e.Code}"
                HttpError e => return "{r.Status} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} not UTF-8"
            }
        }
        IoError e => return "{e.Kind} {e.Code}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Calls(int port) {
    print(Show(port, "/n", 0s))
    print(Show(port, "/n", -1s))
    print(Show(port, "/n", 100s))
    print(Show(port, "/n", 9223372036854775807ns))
    print(Show(port, "/slow/5200", 10s))
    print(Show(port, "/r/3", 5s))
    print(Show(port, "/r/3", 400ms))
    start = instant.Now
    print(Show(port, "/slow/1000", 200ms))
    elapsed = instant.Now - start
    print(elapsed >= 200ms && elapsed < 900ms)
}

// a connect the queue of a listener that accepts nothing holds, as `connect-timeout.kz` fills it: the call ends at its time
void | IoError Queued() {
    listener = TcpListener.Bind("127.0.0.1:0")
    mut held = List<TcpStream>()
    mut full = false
    while !full && held.Count < 1000 {
        match TcpStream.Connect("127.0.0.1:{listener.Port}", 100ms) {
            TcpStream s => held.Add(s)
            IoError e => {
                full = true
            }
        }
    }

    start = instant.Now
    print(Show(listener.Port, "/", 300ms))
    elapsed = instant.Now - start
    print(elapsed >= 300ms && elapsed < 4s)
}

print(HttpClientOptions().Timeout == 100s)
Against((int port) => Calls(port))
Queued() else {
    IoError e => print(e)
}
```

Case: [http/client-timeout-body.kz](../corpus/http/client-timeout-body.kz)
```kurz
// a call's time holds to the last byte read of its response: a body of a length, a chunked body between its chunks and a body read
// to the close each give `TimedOut` with the number 0 once the time has passed, and so does a head that stops halfway; a call whose
// time ran out on a connection of the pool is not sent again, and no connection whose call ran out is used again; and a body whose
// bytes come one at a time, each before a read's time would run out, still ends at the call's time, since each read waits only what
// remains of it: a body of a length, one read to the close, and a chunk's size line
extern "C" {
    int fork()
    int kill(int pid, int signal)
    int waitpid(int pid, long status, int options)
    int usleep(int microseconds)
    void _exit(int code)
}

// the server, in a child of the program's process: each connection in turn, each request on it read and answered with the next of
// `answers`, each `#` in it the number of the connection, followed by each next that begins with `~`, written a byte at a time, the
// connection held where the next is `hold` until the client ends it, and closed then
void Serve(TcpListener on, List<string> answers) {
    mut next = 0
    mut number = 0
    while next < answers.Count {
        stream = on.Accept() else {
            IoError e => return
        }

        number = number + 1
        mut open = true
        while open && next < answers.Count {
            match HttpRequest.Read(stream) {
                HttpRequest r => {
                    stream.Write(answers[next].Replace("#", "{number}").Bytes) else {
                        IoError e => {
                            open = false
                        }
                    }

                    next = next + 1
                    while next < answers.Count && answers[next].StartsWith("~") {
                        Trickle(stream, answers[next])
                        next = next + 1
                    }

                    if next < answers.Count && answers[next] == "hold" {
                        next = next + 1
                        Hold(stream)
                        open = false
                    }
                }
                IoError e => {
                    open = false
                }
                HttpError e => {
                    open = false
                }
            }
        }

        stream.Close() else {
            IoError e => {
            }
        }
    }

    // a connection past the answers, a request the client should not have sent, closed at once, so that it shows as a line of the
    // output and not as a call that waits on a connection nobody serves
    while true {
        extra = on.Accept() else {
            IoError e => return
        }

        extra.Close() else {
            IoError e => {
            }
        }
    }
}

// the bytes of `text` after its `~` written one at a time, 100 milliseconds apart, until a write fails
void Trickle(TcpStream stream, string text) {
    for i in 1..<text.Bytes.Count {
        raw {
            usleep(100000)
        }

        stream.Write(text.Substring(i, 1).Bytes) else {
            IoError e => return
        }
    }
}

// the stream read until the client ends it
void Hold(TcpStream stream) {
    mut sink = Bytes()
    while true {
        n = stream.Read(mut sink, 4096) else {
            IoError e => return
        }

        if n == 0 {
            return
        }
    }
}

// the server started in a child, the client's part run against it, and the child ended; the listener's `Accept` waits at most 10
// seconds, so that a child whose program ended before it could end the child ends by itself and does not hold the output the suite
// reads
void Against(List<string> answers, (int) => void part) {
    on = TcpListener.Bind("127.0.0.1:0") else {
        IoError e => {
            print(e)
            return
        }
    }

    on.SetAcceptTimeout(10s) else {
        IoError e => {
            print(e)
            return
        }
    }

    mut child = 0
    raw {
        child = fork()
    }

    // a fork that failed ends the case before the client, so that no `kill` or `waitpid` takes a pid of 0 or below
    if child < 0 {
        print("fork failed")
        return
    }

    if child == 0 {
        Serve(on, answers)
        raw {
            _exit(0)
        }
    }

    part(on.Port)
    raw {
        kill(child, 9)
        waitpid(child, 0, 0)
    }
}

// the address of `path` on the server
Uri Address(int port, string path) {
    u = Uri.Parse("http://127.0.0.1:{port}{path}") else {
        UriError e => return Uri()
    }

    return u
}

// a `GET` of `/` and its status, the server's number of the connection and the body read as text, or the failure
string Show(HttpClient client, int port) {
    match client.Get(Address(port, "/")) {
        HttpResponseMessage r => {
            number = r.Headers.Get("X-Connection") ?? "-"
            match r.Body.ReadAllText(100) {
                string text => return "{r.Status} {number} [{text}]"
                IoError e => return "{r.Status} {number} {e.Kind} {e.Code}"
                HttpError e => return "{r.Status} {number} {e.Kind}: {e.Detail}"
                InvalidUtf8 => return "{r.Status} {number} not UTF-8"
            }
        }
        IoError e => return "{e.Kind} {e.Code}"
        HttpError e => return "{e.Kind}: {e.Detail}"
    }
}

void Responses(int port) {
    client = HttpClient(HttpClientOptions(Timeout: 300ms))
    for i in 0..<7 {
        print(Show(client, port))
    }
}

// one call of a time of 300 milliseconds whose body comes a byte at a time, and whether it ended at its time and not at the body's
// last byte, a second later
void Trickled(int port) {
    start = instant.Now
    print(Show(HttpClient(HttpClientOptions(Timeout: 300ms)), port))
    elapsed = instant.Now - start
    print(elapsed >= 300ms && elapsed < 900ms)
}

Against([
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 10\r\n\r\nabc",
    "hold",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n",
    "hold",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\n\r\nabc",
    "hold",
    "HTTP/1.1 200 OK\r\nX-Conn",
    "hold",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 2\r\n\r\nok",
    "",
    "hold",
    "HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 4\r\n\r\nnext",
], (int port) => Responses(port))
Against(["HTTP/1.1 200 OK\r\nX-Connection: #\r\nContent-Length: 10\r\n\r\n", "~abcdefghij"], (int port) => Trickled(port))
Against(["HTTP/1.1 200 OK\r\nX-Connection: #\r\n\r\n", "~abcdefghij"], (int port) => Trickled(port))
Against(["HTTP/1.1 200 OK\r\nX-Connection: #\r\nTransfer-Encoding: chunked\r\n\r\n", "~3;abcdefghij\r\nabc\r\n0\r\n\r\n"],
    (int port) => Trickled(port))
```

No case: of a write that waits, since the request of `Get` is a head of a few bytes, which the socket's buffer takes at once
whatever the server reads; of the lookup, since a case would need a name server that does not answer, which a run of the suite
does not have; and of an actor's socket, since a program of the compiler runs in one actor (W13).
