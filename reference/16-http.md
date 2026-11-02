# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (assumed, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions` and `Uri`, with `UriError` and `UriKind`, as `std/`
declares them, as it sees `File` and `TcpStream` (S2, S8). A file's own declaration of one of these
names that `std/` declares, in any of its forms, a type, a function, a variable or a parameter, is
`redeclared` (V9); a name of the list that `std/` does not declare is no type a file sees, and a
file's own declaration of it is its own. The names are a list the compiler holds, each a `pub` type
of `std/`, where Rust writes its list in `std`, as the module `std::prelude::rust_2021` that its
compiler names by edition (§12: the owner, 2026-11-01, Q7 (b), that every file sees them, and
2026-11-02, that the compiler holds the list, against every `pub` type of `std/` and against a list
written in `std/`; the list of names and the two of `Uri` are the record's assumptions).

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

### W2 (decided, §12) `HttpError` and `HttpKind`

A failure of HTTP is a case of its own, `data HttpError(HttpKind Kind, string Detail)`, beside a
failure of the network, which stays an `IoError` (S1), neither wrapped in the other (§12, the owner,
2026-10-31, "HttpError + record's rule" and "IoError + HttpError") *(assumed: that it stays a `data`
type in `std/`, as §12 marks it)*. Its kinds are
`enum HttpKind { Protocol, TooLarge, Status, Unsupported }`. An `HttpError` prints its detail alone,
`malformed response: no status line`, with no words of its kind (§12, the owner, 2026-11-01, Q4 (a)).
It gives that text through a `Text()` that `std/` declares in its body, as a `data` type declares its
text (A5) (§12, the owner, 2026-11-02). `std/` declares `HttpError` without that body, since the
compiler reads no body of a `data` type (D16) and `std/` compiles with every program, so a build says
`not supported` to the text of an `HttpError` and of a value that holds one, wherever it is taken
(§12, the owner, 2026-11-02, the design standing).

Case: [http/http-error-kind.kz](../corpus/http/http-error-kind.kz)
