# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (decided, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions` and `Uri`, with `UriError` and `UriKind`, as `std/`
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
every `pub` type of `std/` and against a list written in `std/`; the two of `Uri` are the record's
assumptions).

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
reason, so that the id stands in this rule and not in [12-errors.md](12-errors.md).

### W4 (decided, §12) `HttpHeaders`

`HttpHeaders` is an ordered list of names and values, a class of `std/`: `h.Get(name)` gives the
first value of the name, a `string?`, `h.GetAll(name)` gives every value, and `h.Add(name, value)`,
`h.Set(name, value)` and `h.Remove(name)` change it. A name compares without regard to ASCII case
and is written back as it was spelled, and repeated headers, `Set-Cookie`, stay apart, as hyper's
`HeaderMap` and ASP.NET Core's `IHeaderDictionary` keep them (§12, the owner, 2026-10-31, "Ordered
list type"). `GetAll` gives a `List<string>`, `Set` replaces every value of the name by one, as
ASP.NET Core's indexer does, and `Remove` removes every value of it *(assumed, §12: proposed with the
product pass and not put to the owner)*.

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
