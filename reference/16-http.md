# 16. HTTP

HTTP's types are Kurz code in the standard library, `std/`, as Rust's `std` is Rust code (§12, the
owner, 2026-11-01, Q7 (b)).

### W1 (assumed, §12) The types every file sees

Every file sees `HttpServer`, `HttpRequest`, `HttpResponse`, `HttpHeaders`, `HttpError`, `HttpKind`,
`HttpLimits`, `HttpClient`, `HttpClientOptions` and `Uri`, with `UriError` and `UriKind`, as `std/`
declares them, as it sees `File` and `TcpStream` (S2, S8). A file's own declaration of one of these
names is `redeclared` (V9). The names are a list the compiler holds, each a `pub` type of `std/`, as
Rust's `std::prelude::rust_2021` lists the names every module sees (§12: the owner, 2026-11-01, Q7 (b),
that every file sees them; the list of names, the two of `Uri`, and the list held in the compiler,
against every `pub` type of `std/` and against a list written in `std/`, are the record's assumptions).

Case: [http/http-error-redeclared.kz](../corpus/http/http-error-redeclared.kz)
```kurz
data HttpError(int Code)

print(1)
```

### W2 (decided, §12) `HttpError` and `HttpKind`

A failure of HTTP is a case of its own, `data HttpError(HttpKind Kind, string Detail)`, beside a
failure of the network, which stays an `IoError` (S1), neither wrapped in the other (§12, the owner,
2026-10-31, "HttpError + record's rule" and "IoError + HttpError"). Its kinds are
`enum HttpKind { Protocol, TooLarge, Status, Unsupported }`. An `HttpError` prints its detail alone,
`malformed response: no status line`, with no words of its kind (§12, the owner, 2026-11-01, Q4 (a)).

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
