# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (decided, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions` and `Uri`, with `UriError` and `UriKind`, as `std/`
declares them, as it sees `File` and `TcpStream` (S2, S8). A file's own declaration of one of these
names that binds it in a scope, a type, a function, a variable, a parameter, a lambda's parameter or
a variable in a block, is `redeclared` (V9), whether `std/` declares that type or not, so that no
program stops building when `std/` adds it. A member so named, a field, a `static` field, a method,
an `enum` value or a member of a `data` type, is no declaration of the name and builds, as a member
shadows no type in C# and Rust.
The names are a list the compiler holds, each a `pub` type of `std/`, where Rust writes its list in
`std`, as the module `std::prelude::rust_2021` that its compiler names by edition (§12: the owner,
2026-11-01, Q7 (b), that every file sees them, and 2026-11-02, that a file's own declaration of one
is `redeclared` whether `std/` declares it or not, that only a declaration that binds the name in a
scope is one, against every member so named, and that the compiler holds the list, against
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
HttpKind = 3

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
    HttpKind = 2
    print(HttpKind)
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
`not supported` to the text of an `HttpError` and of a value that holds one, a list, a union, a map
or a `data` value among them, at the expression where `print`, a hole of an interpolated string (L6)
or `string.Join` (X6) takes it (§12, the owner, 2026-11-02, the design standing).

Case: [http/http-error-kind.kz](../corpus/http/http-error-kind.kz)

Case: [http/http-error-text.kz](../corpus/http/http-error-text.kz)
```kurz
print(HttpError(HttpKind.Protocol, "malformed response: no status line"))
```
