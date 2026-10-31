# 14. Hashes

### H1 (decided, §12) The hashes

`Sha256.Hash(b)`, `Md5.Hash(b)` and `Hmac.Sha256(key, message)` give the digest of the bytes of `b`,
or of `message` under `key`, as `Bytes` in the order their standards write it: 32 bytes of SHA-256
(FIPS 180-4), 16 of MD5 (RFC 1321) and 32 of HMAC-SHA256 (RFC 2104 over SHA-256, the tests of RFC 4231).
`Crc32.Hash(b)` gives the CRC-32/ISO-HDLC of the bytes, zlib's, as a `uint`, whose check value, of the
bytes of `123456789`, is `0xCBF43926`. A key of HMAC takes any length: a key longer than the block of 64
bytes is hashed first. A `string` is hashed through `s.Bytes`. The code of the four is Kurz in the
standard library (§12, the owner, 2026-10-30, "In Kurz, as RustCrypto"; 2026-10-31, "`Sha256.Hash(b)`",
the option that named the four, and "`Bytes`, CRC32 a `uint`"). They need no grant and build for every target, Windows among them; their
parameters are named `bytes`, `key` and `message`; and a block the system refuses for a digest or a
state ends the process with `allocation-failed`, as a list's does (M9). Every file sees the names
`Sha256`, `Md5`, `Crc32`, `Hmac`, `Hex` and `FixedTime`, and a file's own declaration of one, a type, a
function, a variable or a parameter at any level, is `redeclared` (V9), while a field takes the name. *(assumed, §12: no grant, every target, the `string`
through `s.Bytes`, the names of the parameters, the refused block and the six names)*

Case: [hashing/sha256.kz](../corpus/hashing/sha256.kz)
```kurz
print(Hex.Encode(Sha256.Hash("abc".Bytes)))
print(Hex.Encode(Sha256.Hash(Bytes())))
print(Hex.Encode(Sha256.Hash("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq".Bytes)))
print(Hex.Encode(Sha256.Hash("abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu".Bytes)))
mut million = Bytes()
while million.Count < 1000000 {
    million.Add(97)
}

Bytes digest = Sha256.Hash(million)
print(Hex.Encode(digest))
print(digest.Count)
```

Case: [hashing/md5.kz](../corpus/hashing/md5.kz)
```kurz
print(Hex.Encode(Md5.Hash("".Bytes)))
print(Hex.Encode(Md5.Hash("a".Bytes)))
print(Hex.Encode(Md5.Hash("abc".Bytes)))
print(Hex.Encode(Md5.Hash("message digest".Bytes)))
print(Hex.Encode(Md5.Hash("abcdefghijklmnopqrstuvwxyz".Bytes)))
print(Hex.Encode(Md5.Hash("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".Bytes)))
print(Hex.Encode(Md5.Hash("12345678901234567890123456789012345678901234567890123456789012345678901234567890".Bytes)))
print(Md5.Hash("a".Bytes).Count)
```

Case: [hashing/crc32.kz](../corpus/hashing/crc32.kz)
```kurz
print(Crc32.Hash("123456789".Bytes))
print(Crc32.Hash(Bytes()))
uint check = Crc32.Hash("The quick brown fox jumps over the lazy dog".Bytes)
print(check)
```

Case: [hashing/hmac.kz](../corpus/hashing/hmac.kz)
```kurz
// the bytes `value` repeated `count` times
Bytes Repeated(byte value, int count) {
    mut b = Bytes()
    while b.Count < count {
        b.Add(value)
    }

    return b
}

print(Hex.Encode(Hmac.Sha256(Repeated(11, 20), "Hi There".Bytes)))
print(Hex.Encode(Hmac.Sha256("Jefe".Bytes, "what do ya want for nothing?".Bytes)))
print(Hex.Encode(Hmac.Sha256(Repeated(170, 20), Repeated(221, 50))))
mut key = Bytes()
while key.Count < 25 {
    key.Add(byte(key.Count + 1))
}

print(Hex.Encode(Hmac.Sha256(key, Repeated(205, 50))))
print(Hex.Encode(Hmac.Sha256(Repeated(12, 20), "Test With Truncation".Bytes)[0..<16]))
print(Hex.Encode(Hmac.Sha256(Repeated(170, 131), "Test Using Larger Than Block-Size Key - Hash Key First".Bytes)))
print(Hex.Encode(Hmac.Sha256(Repeated(170, 131), "This is a test using a larger than block-size key and a larger than block-size data. The key needs to be hashed before being used by the HMAC algorithm.".Bytes)))
```

Case: [hashing/name-redeclared.kz](../corpus/hashing/name-redeclared.kz)
```kurz
// the hexadecimal digits of a number, under a name every file sees
data Hex(int Digits)

print(1)
```

### H2 (decided, §12) `Hex.Encode`

`Hex.Encode(b)` gives the hexadecimal of the bytes of `b` as a `string`: two digits for each byte, the
higher half first, in lower case, as Rust's `hex::encode`; the bytes of an empty list give the empty
string. `print` of a `Bytes` stays the list of its numbers (A6) (§12, the owner, 2026-10-31, "`Hex.Encode(b)`,
lower case").

Case: [hashing/hex.kz](../corpus/hashing/hex.kz)
```kurz
Bytes b = [0, 1, 15, 16, 171, 255]
print(Hex.Encode(b))
print(Hex.Encode(Bytes()) == "")
print(b)
```

Case: [hashing/sha256.kz](../corpus/hashing/sha256.kz)

Case: [hashing/md5.kz](../corpus/hashing/md5.kz)

Case: [hashing/hmac.kz](../corpus/hashing/hmac.kz)

### H3 (decided, §12) A hasher takes a message in pieces

`Sha256.New()`, `Md5.New()` and `Crc32.New()` make a hasher of the type `Sha256`, `Md5` or `Crc32`, and
`Hmac.NewSha256(key)` one of the type `Hmac` under the key. `h.Update(b)` adds the bytes of `b` to the
message, and `h.Finish()` gives the digest of the message, as the hash of H1 gives it, a `uint` for a
`Crc32` and `Bytes` for the others, and starts the hasher over, a hasher of `Hmac` under its key, so that
a `Finish` with no `Update` before it gives the digest of no bytes (§12, the owner, 2026-10-31, "Yes, as
Rust"). `Update` and `Finish` change the hasher, so that their receiver is one a list's `Add` takes, a
value reachable through a `mut` variable (M5); another receiver is `mut-required`. *(assumed, §12:
`Hmac.NewSha256` and the type `Hmac`; the `mut` receiver)*

Case: [hashing/pieces.kz](../corpus/hashing/pieces.kz)
```kurz
Bytes message = "The quick brown fox jumps over the lazy dog".Bytes
mut h = Sha256.New()
h.Update(message[0..<10])
h.Update(Bytes())
h.Update(message[10..<message.Count])
print(Hex.Encode(h.Finish()))
print(Hex.Encode(Sha256.Hash(message)))
print(Hex.Encode(h.Finish()))
mut c = Crc32.New()
c.Update(message[0..<3])
c.Update(message[3..<message.Count])
print(c.Finish())
print(c.Finish())
mut m = Hmac.NewSha256("key".Bytes)
m.Update(message[0..<20])
m.Update(message[20..<message.Count])
print(Hex.Encode(m.Finish()))
m.Update(message)
print(Hex.Encode(m.Finish()))
mut d = Md5.New()
d.Update(message)
print(Hex.Encode(d.Finish()))
```

Case: [hashing/update-without-mut.kz](../corpus/hashing/update-without-mut.kz)
```kurz
h = Sha256.New()
h.Update("abc".Bytes)
print(h)
```

### H4 (decided, §12) A tag is checked in fixed time

`Hmac.VerifySha256(key, message, tag)` is a `bool`: whether `tag` is the tag of H1 of the message under
the key. `FixedTime.Equal(a, b)` is a `bool`: whether two `Bytes` hold the same bytes. Bytes of another
count give `false`; else the Kurz code of both reads every byte and stops at no difference, which
promises nothing of the code the backend makes of it (§12, the owner, 2026-10-31, "Verify + fixed-time
equal"). *(assumed, §12: the two names and the words of the promise)*

Case: [hashing/verify.kz](../corpus/hashing/verify.kz)
```kurz
Bytes key = "key".Bytes
Bytes message = "The quick brown fox jumps over the lazy dog".Bytes
Bytes tag = Hmac.Sha256(key, message)
print(Hmac.VerifySha256(key, message, tag))
print(Hmac.VerifySha256(key, "The quick brown fox jumps over the lazy cat".Bytes, tag))
print(Hmac.VerifySha256(key, message, tag[0..<16]))
print(FixedTime.Equal(tag, Hmac.Sha256(key, message)))
print(FixedTime.Equal("abc".Bytes, "abd".Bytes))
print(FixedTime.Equal(Bytes(), Bytes()))
```

### H5 (decided, §12) The lint `md5-insecure`

Every call of a function of `Md5`, `Md5.Hash` and `Md5.New`, is the lint `md5-insecure`: a warning at the
call, in every build, which builds the program, written as the command writes an error, with `warning:`
before the id, `app.kz:3: warning: md5-insecure: <text>` (§12, the owner, 2026-10-31, "A lint at each call"
and "Warning, build goes on"). A line in `project.kz` silences the lint for its package; the form of that
line stands open (§14): a table of lint ids with a level, as Cargo's `[lints]`, or a list of the ids
it silences; while it stands open, nothing silences the lint. *(assumed, §12: the id, the form of the warning, and that the calls are those of
`Md5`'s functions, which a hasher's `Update` and `Finish` are not)*

No case: a warning changes neither what a program prints nor whether it compiles, and a corpus header
cannot expect one (V12).

### H6 (assumed, §12) A hasher is a value

A hasher is a value: a copy goes on from where the hasher stood, as a list's copy does (M3), and a change
of one leaves the other as it was. The text of a hasher is the name of its type and nothing of its state,
`Sha256`. `==` on two hashers is `type-mismatch`, as on two files, and so is `==` on a `data` value
that holds one, as on one that holds a file (D3). *(assumed, §12)*

Case: [hashing/hasher-copy.kz](../corpus/hashing/hasher-copy.kz)
```kurz
mut h = Sha256.New()
h.Update("ab".Bytes)
mut other = h
h.Update("c".Bytes)
other.Update("d".Bytes)
print(Hex.Encode(h.Finish()))
print(Hex.Encode(other.Finish()))
print(h)
c = Crc32.New()
m = Hmac.NewSha256("key".Bytes)
print("{c} {m}")
print([Crc32.New()])
```

Case: [hashing/hasher-equal.kz](../corpus/hashing/hasher-equal.kz)
```kurz
a = Sha256.New()
b = Sha256.New()
print(a == b)
```
