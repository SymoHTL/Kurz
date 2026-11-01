#!/usr/bin/env python3
"""The case tables of X3, generated from the Unicode Character Database of the version Rust's stable
`char::UNICODE_VERSION` pins (the owner, 2026-10-31, "Rust's pin" and "Generated, as Rust"), from the mappings
Rust's `unicode-table-generator` takes for the tables of `core::unicode::conversions`, in levels of deltas, as
.NET's `CharUnicodeInfo` holds its mappings (issue 157: faster with the same results, under the owner's answer of
2026-11-01, "Allow, faster wins").

  unicode_tables.py              write compiler/Kurz.Compiler/Emit/CaseTables.cs from the database's files
                                 and print every mapping that changed against the file it replaces
  unicode_tables.py --check      the gate: red where the file is not what the files give, naming the
                                 first 40 mappings that differ and counting the rest
  unicode_tables.py --self-test

The three files are fetched from unicode.org at the pinned version and each is checked by its SHA-256
before it is read: a file of another hash is refused (FAIL), and a file that cannot be fetched, or
whose download is cut off, leaves the tables unread (PARTLY, which is red: nothing was compared). A
fetched file is kept under its hash in the system's temporary directory, and read from there while its
hash holds; a kept file that cannot be read is fetched again.

What the tables hold, as Rust's generator takes them:
- the mappings: for every code point above ASCII, UnicodeData.txt's simple lowercase and uppercase
  mappings, then SpecialCasing.txt's mappings without a condition over them; a mapping to the code
  point itself is left out, so that SpecialCasing.txt's identity keeps the simple mapping; Final_Sigma,
  the one condition that is no language's, is the standard library's code (X3), and the mappings of a
  language are left out, as "no culture" (section 4) says;
- the levels of the mappings: the code points of the first two planes in blocks of 32, an index of one
  byte for each block, the number of its deltas, alike blocks sharing one number, and the 32 deltas of
  each distinct block, 16 bits each: the difference modulo 65536 between a code point and the one code
  point it maps to, 0 where it maps to none, and 32768 where it maps to several;
- the mappings to two or three code points, padded with zeros;
- the ranges of Cased and Case_Ignorable from DerivedCoreProperties.txt, ASCII among them, which
  Rust's `is_cased` and `is_case_ignorable` take from the same properties.
This tool refuses the forms its tables do not hold: a mapping that leaves its plane, a mapping past
the first two planes, to one code point or to several, a delta of 32768, and more distinct blocks than
an index of one byte numbers; and levels that, read back, do not give the mappings they were made
from. A table with fewer entries than its floor is refused, so that a file read as empty is never a
table."""
import hashlib
import http.client
import os
import re
import sys
import tempfile
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

VERSION = "17.0.0"
FILES = {
    "UnicodeData.txt": "2e1efc1dcb59c575eedf5ccae60f95229f706ee6d031835247d843c11d96470c",
    "SpecialCasing.txt": "efc25faf19de21b92c1194c111c932e03d2a5eaf18194e33f1156e96de4c9588",
    "DerivedCoreProperties.txt": "24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08",
}
SOURCE = "https://www.unicode.org/Public/{version}/ucd/{name}"
OUT = "compiler/Kurz.Compiler/Emit/CaseTables.cs"
# the levels: the code points of the first two planes in blocks of 32, an index of one byte per block, and the
# deltas of each distinct block, 16 bits each; MARK is the delta of a code point that maps to several code points
BLOCK = 32
PLANES = 0x20000
MARK = 0x8000
# the entries each table holds at the least; 17.0.0 holds 69 and 82 blocks of deltas, 1 and 102 multis, 158 and 464
# ranges
FLOORS = {"LowerDeltas": 30, "UpperDeltas": 30, "LowerMultis": 1, "UpperMultis": 50, "Cased": 100, "CaseIgnorable": 300}
WIDTH = {"LowerIndex": 1, "UpperIndex": 1, "LowerDeltas": BLOCK, "UpperDeltas": BLOCK, "LowerMultis": 4, "UpperMultis": 4,
         "Cased": 2, "CaseIgnorable": 2}


class Unread(Exception):
    """A file of the database that could not be fetched: nothing was compared."""


def fetch(name, version=VERSION, digest=None, opener=urllib.request.urlopen, cache=None):
    """The text of one file of the database, checked by its SHA-256. A file of another hash raises
    ValueError; one that cannot be fetched raises Unread."""
    digest = digest or FILES[name]
    kept = os.path.join(cache or tempfile.gettempdir(), f"kurz-ucd-{digest}.txt")
    try:
        with open(kept, "rb") as f:
            data = f.read()
        if hashlib.sha256(data).hexdigest() == digest:
            return data.decode("utf-8")
    except OSError:  # no kept file, or one that cannot be read: fetched again
        pass
    try:
        with opener(SOURCE.format(version=version, name=name), timeout=60) as r:
            data = r.read()
    except (OSError, http.client.HTTPException) as e:
        raise Unread(f"{name}: could not be fetched: {e}") from None
    got = hashlib.sha256(data).hexdigest()
    if got != digest:
        raise ValueError(f"{name}: SHA-256 {got}, where {digest} is pinned: refused")
    try:
        with open(kept, "wb") as f:
            f.write(data)
    except OSError:
        pass  # a cache that cannot be written costs a fetch next time, nothing else
    return data.decode("utf-8")


def mappings(unicode_data, special_casing):
    """(lower, upper): every code point above ASCII and the code points its full mapping gives, where
    they are not the code point itself."""
    lower, upper = {}, {}
    for line in unicode_data.splitlines():
        if not line.strip():
            continue
        f = line.split(";")
        cp = int(f[0], 16)
        if f[13] and int(f[13], 16) != cp:
            lower[cp] = (int(f[13], 16),)
        if f[12] and int(f[12], 16) != cp:
            upper[cp] = (int(f[12], 16),)
    for line in special_casing.splitlines():
        body = line.split("#")[0].strip()
        if not body:
            continue
        f = [x.strip() for x in body.split(";")]
        if len(f) > 4 and f[4]:
            continue  # a condition: Final_Sigma or a language
        cp = int(f[0], 16)
        lo = tuple(int(x, 16) for x in f[1].split())
        up = tuple(int(x, 16) for x in f[3].split())
        if lo != (cp,):
            lower[cp] = lo
        if up != (cp,):
            upper[cp] = up
    return ({k: v for k, v in lower.items() if k > 0x7F}, {k: v for k, v in upper.items() if k > 0x7F})


def tables(mapping):
    """(index, deltas, multis) of one direction: the code points of the first two planes in blocks of BLOCK,
    `index` the number of each block's deltas, a block alike to one before it taking that one's number;
    `deltas` BLOCK numbers for each distinct block, the difference modulo 65536 between a code point and the one
    code point it maps to, 0 where it maps to none and MARK where it maps to several; and `multis` the mappings
    to several code points as (key, a, b, c), padded with zeros. Read back and refused where it does not give the
    mapping."""
    multis = []
    for plane in range(17):
        keys = sorted(k for k in mapping if k >> 16 == plane)
        if keys and plane > 1:
            raise ValueError(f"plane {plane}: a mapping past the first two planes")
        for key in keys:
            out = mapping[key]
            if any(c >> 16 != plane for c in out):
                raise ValueError(f"U+{key:04X}: a mapping that leaves its plane")
            if len(out) > 1:
                multis.append((key,) + out + (0,) * (3 - len(out)))
    index, numbers, deltas = [], {}, []
    for first in range(0, PLANES, BLOCK):
        block = []
        for cp in range(first, first + BLOCK):
            out = mapping.get(cp)
            if out is None:
                block.append(0)
            elif len(out) > 1:
                block.append(MARK)
            else:
                delta = (out[0] - cp) % 65536
                if delta == MARK:
                    raise ValueError(f"U+{cp:04X}: a delta of {MARK}, the mark of a mapping to several code points")
                block.append(delta)
        number = numbers.setdefault(tuple(block), len(numbers))
        if number == len(deltas) // BLOCK:
            deltas += block
        index.append(number)
    if len(numbers) > 256:
        raise ValueError(f"{len(numbers)} blocks of {BLOCK} code points, more than an index of one byte numbers")
    several = {m[0]: tuple(c for c in m[1:] if c) for m in multis}
    read = {}
    for cp in range(PLANES):
        d = deltas[index[cp // BLOCK] * BLOCK + cp % BLOCK]
        if d == MARK:
            read[cp] = several.get(cp)
        elif d:
            read[cp] = ((cp & ~0xFFFF) | ((cp + d) & 0xFFFF),)
    if read != mapping:
        raise ValueError("the levels do not give the mapping they were made from")
    return index, deltas, multis


def ranges(properties, name):
    """The merged ranges of one property of DerivedCoreProperties.txt, as (first, last)."""
    found = []
    for line in properties.splitlines():
        body = line.split("#")[0].strip()
        if not body:
            continue
        f = [x.strip() for x in body.split(";")]
        if f[1] != name:
            continue
        a, _, b = f[0].partition("..")
        found.append((int(a, 16), int(b or a, 16)))
    merged = []
    for a, b in sorted(found):
        if merged and a <= merged[-1][1] + 1:
            merged[-1] = (merged[-1][0], max(b, merged[-1][1]))
        else:
            merged.append((a, b))
    return merged


def generate(unicode_data, special_casing, properties, floors=FLOORS):
    """The arrays of the file, by name, as lists of integers."""
    lower, upper = mappings(unicode_data, special_casing)
    lower_index, lower_deltas, lower_multis = tables(lower)
    upper_index, upper_deltas, upper_multis = tables(upper)
    out = {
        "LowerIndex": lower_index,
        "LowerDeltas": lower_deltas,
        "LowerMultis": [x for m in lower_multis for x in m],
        "UpperIndex": upper_index,
        "UpperDeltas": upper_deltas,
        "UpperMultis": [x for m in upper_multis for x in m],
        "Cased": [x for r in ranges(properties, "Cased") for x in r],
        "CaseIgnorable": [x for r in ranges(properties, "Case_Ignorable") for x in r],
    }
    for name, floor in floors.items():
        if len(out[name]) // WIDTH[name] < floor:
            raise ValueError(f"{name}: {len(out[name]) // WIDTH[name]} entries, fewer than the floor of {floor}")
    return out


SUMMARIES = {
    "LowerIndex": "The levels of the lowercase mappings: for each block of 32 code points of the first two planes, the number of its 32 deltas in <see cref=\"LowerDeltas\"/>.",
    "LowerDeltas": "The deltas of the lowercase mappings, 32 for each distinct block: the difference modulo 65536 between a code point and the one code point it maps to, 0 where it maps to none, and 32768 where it maps to several, which <see cref=\"LowerMultis\"/> holds.",
    "UpperIndex": "The levels of the uppercase mappings, as <see cref=\"LowerIndex\"/> holds the lowercase ones.",
    "UpperDeltas": "The deltas of the uppercase mappings, as <see cref=\"LowerDeltas\"/> holds the lowercase ones.",
    "LowerMultis": "The lowercase mappings to two or three code points, four numbers each: the code point and the code points it gives, the fourth number zero where the mapping gives two.",
    "UpperMultis": "The uppercase mappings to two or three code points, as <see cref=\"LowerMultis\"/> holds the lowercase ones.",
    "Cased": "The ranges of the property Cased, the first and the last code point of each, in order.",
    "CaseIgnorable": "The ranges of the property Case_Ignorable, as <see cref=\"Cased\"/> holds its ranges.",
}


def number(value, at, name):
    """One number of an array as the file writes it: a code point in hexadecimal, a block's number and a
    delta of the levels in decimal."""
    return str(value) if name.endswith(("Index", "Deltas")) else f"0x{value:X}"


def render(arrays):
    """The C# file that holds the arrays."""
    lines = [
        f"// Generated by tools/unicode_tables.py from the Unicode Character Database {VERSION}: run the tool, never edit this file.",
        "namespace Kurz.Compiler.Emit;",
        "",
        "/// <summary>",
        "/// X3: the case tables of Unicode's full mappings and of the two properties Final_Sigma reads, of the version Rust's stable",
        "/// <c>char::UNICODE_VERSION</c> pins, the mappings of every code point above ASCII in levels of deltas, as .NET's",
        "/// <c>CharUnicodeInfo</c> holds its mappings, and the properties of every code point, ASCII among them.",
        "/// </summary>",
        "internal static class CaseTables",
        "{",
        "    /// <summary>The version of the Unicode Character Database the tables are generated from.</summary>",
        f"    public const string Version = \"{VERSION}\";",
    ]
    for name in ("LowerIndex", "LowerDeltas", "LowerMultis", "UpperIndex", "UpperDeltas", "UpperMultis", "Cased", "CaseIgnorable"):
        values = arrays[name]
        per = {1: 32, 2: 12, 4: 16}.get(WIDTH[name], 16)
        lines += ["", "    /// <summary>"]
        words, line = SUMMARIES[name].split(), "    ///"
        for word in words:
            if len(line) + 1 + len(word) > 136:
                lines.append(line)
                line = "    ///"
            line += " " + word
        lines += [line, "    /// </summary>", f"    public static readonly int[] {name} =", "    ["]
        for at in range(0, len(values), per):
            lines.append("        " + ", ".join(number(v, at + k, name) for k, v in enumerate(values[at:at + per])) + ",")
        lines.append("    ];")
    lines.append("}")
    return "\n".join(lines) + "\n"


ARRAY = re.compile(r"public static readonly int\[\] (\w+) =\s*\[(.*?)\];", re.S)


def parse(text):
    """The arrays of a written file, by name."""
    return {name: [int(v, 0) for v in re.findall(r"-?(?:0x[0-9A-F]+|\d+)", body)] for name, body in ARRAY.findall(text)}


def expand(arrays):
    """Every mapping and every property the arrays hold, as {(kind, code point): value}, the form two
    versions are compared in."""
    found = {}
    for kind in ("Lower", "Upper"):
        index, deltas = arrays.get(f"{kind}Index", []), arrays.get(f"{kind}Deltas", [])
        for cp in range(min(PLANES, len(index) * BLOCK)):
            at = index[cp // BLOCK] * BLOCK + cp % BLOCK
            if at < len(deltas) and deltas[at] not in (0, MARK):
                found[(kind.lower(), cp)] = ((cp & ~0xFFFF) | ((cp + deltas[at]) & 0xFFFF),)
        multis = arrays.get(f"{kind}Multis", [])
        for at in range(0, len(multis) - 3, 4):
            found[(kind.lower(), multis[at])] = tuple(c for c in multis[at + 1:at + 4] if c)
    for kind in ("Cased", "CaseIgnorable"):
        values = arrays.get(kind, [])
        for at in range(0, len(values) - 1, 2):
            for cp in range(values[at], values[at + 1] + 1):
                found[(kind, cp)] = True
    return found


def changes(old, new):
    """One line for each mapping or property that differs between two sets of arrays."""
    a, b = expand(old), expand(new)
    out = []
    for key in sorted(set(a) | set(b), key=lambda k: (k[0], k[1])):
        if a.get(key) != b.get(key):
            show = lambda v: "none" if v is None else ("yes" if v is True else " ".join(f"U+{c:04X}" for c in v))  # noqa: E731
            out.append(f"{key[0]} U+{key[1]:04X}: {show(a.get(key))} -> {show(b.get(key))}")
    return out


def files(fetcher=fetch):
    return [fetcher(name) for name in ("UnicodeData.txt", "SpecialCasing.txt", "DerivedCoreProperties.txt")]


def main(argv, root=kit.ROOT, fetcher=fetch, floors=FLOORS):
    path = os.path.join(root, OUT)
    try:
        arrays = generate(*files(fetcher), floors=floors)
    except Unread as e:
        print(f"unicode-tables: {e}; the tables were not compared")
        return kit.PARTLY
    except ValueError as e:
        print(f"unicode-tables: {e}")
        return 1
    text = render(arrays)
    old = kit.read(path) if os.path.exists(path) else ""
    differ = changes(parse(old), arrays)
    if "--check" in argv:
        if old == text:
            print(f"unicode-tables: {OUT} is what the database {VERSION} gives")
            return 0
        print(f"unicode-tables: {OUT} is not what the database {VERSION} gives; run tools/unicode_tables.py")
        for line in differ[:40] or ["the mappings agree, and the text of the file differs"]:
            print(f"  {line}")
        if len(differ) > 40:
            print(f"  ... {len(differ) - 40} more")
        return 1
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    if not old:
        print(f"unicode-tables: wrote {OUT} from the database {VERSION}, where there was none")
        return 0
    print(f"unicode-tables: wrote {OUT} from the database {VERSION}, {len(differ)} mappings and properties changed")
    for line in differ:
        print(f"  {line}")
    return 0


def self_test():
    cases = []

    def ud(cp, upper="", lower=""):
        return f"{cp};NAME;Lu;0;L;;;;;N;;;{upper};{lower};"

    def got(fn):
        try:
            return fn()
        except Exception as e:
            return e

    data = "\n".join([ud("0041", lower="0061"), ud("00C0", lower="00E0"), ud("00CC", lower="00EC"), ud("00E0", upper="00C0"),
                      ud("0130", lower="0069"), ud("00DF"), ud("01C5", upper="01C4", lower="01C6"), ud("03A3", lower="03C3")])
    special = "\n".join(["# a comment", "00DF; 00DF; 0053 0073; 0053 0053; # SHARP S", "0130; 0069 0307; 0130; 0130; # I WITH DOT",
                         "03A3; 03C2; 03A3; 03A3; Final_Sigma; # SIGMA", "00CC; 0069 0307 0300; 00CC; 00CC; lt; # I WITH GRAVE",
                         "01C5; 01C5; 01C5; 01C4; # DZ WITH CARON"])
    lower, upper = mappings(data, special)
    cases.append(("mappings: UnicodeData's simple mapping", lower.get(0xC0) == (0xE0,) and upper.get(0xE0) == (0xC0,), (lower, upper)))
    cases.append(("mappings: SpecialCasing's mapping without a condition takes the place of the simple one",
                  lower.get(0x130) == (0x69, 0x307) and upper.get(0xDF) == (0x53, 0x53), (lower, upper)))
    cases.append(("mappings: a mapping under Final_Sigma is left out", lower.get(0x3A3) == (0x3C3,), lower))
    cases.append(("mappings: a mapping of one language is left out", lower.get(0xCC) == (0xEC,), lower))
    cases.append(("mappings: an identity in SpecialCasing keeps the simple mapping", lower.get(0x1C5) == (0x1C6,), lower))
    cases.append(("mappings: an identity with no simple mapping is no mapping", 0xDF not in lower, lower))
    cases.append(("mappings: ASCII is left out", 0x41 not in lower, lower))

    def delta(levels, cp):
        index, deltas, _ = levels
        return deltas[index[cp // BLOCK] * BLOCK + cp % BLOCK]

    def brief(out):
        return out if isinstance(out, Exception) else f"{len(set(out[0]))} numbers in the index, {len(out[1]) // BLOCK} blocks, multis {out[2]}"

    out = got(lambda: tables({0xC0: (0xE0,), 0x100: (0x101,), 0x1E9E: (0xDF,)}))
    cases.append(("tables: a code point's delta is the difference to the one code point it maps to, modulo 65536",
                  not isinstance(out, Exception) and [delta(out, cp) for cp in (0xC0, 0x100, 0x1E9E, 0xC1, 0x41)] == [32, 1, 0xDF - 0x1E9E + 65536, 0, 0],
                  brief(out)))
    out = got(lambda: tables({0x100: (0x101,), 0x120: (0x121,), 0x140: (0x142,)}))
    cases.append(("tables: blocks alike share one number, and every block without a mapping is block 0",
                  not isinstance(out, Exception) and out[0][0x100 // BLOCK] == out[0][0x120 // BLOCK] == 1 and out[0][0x140 // BLOCK] == 2
                  and set(out[0]) == {0, 1, 2} and len(out[1]) == 3 * BLOCK, brief(out)))
    out = got(lambda: tables({0x130: (0x69, 0x307)}))
    cases.append(("tables: a mapping to two code points is marked and held among the multis, padded with a zero",
                  not isinstance(out, Exception) and delta(out, 0x130) == MARK and out[2] == [(0x130, 0x69, 0x307, 0)], brief(out)))
    out = got(lambda: tables({0x100: (0x100 + MARK,)}))
    cases.append(("tables: a delta of 32768, the mark of a mapping to several code points, is refused",
                  isinstance(out, ValueError) and "mark" in str(out), brief(out)))
    out = got(lambda: tables({0xFFFF: (0x10000,)}))
    cases.append(("tables: a mapping that leaves its plane is refused", isinstance(out, ValueError), brief(out)))

    global PLANES
    planes, PLANES = PLANES, 0x100  # levels of the first 256 code points alone, which do not reach U+0150
    try:
        out = got(lambda: tables({0x150: (0x151,)}))
    finally:
        PLANES = planes
    cases.append(("tables: levels that do not give the mapping they were made from are refused",
                  isinstance(out, ValueError) and "do not give" in str(out), brief(out)))

    def blocks(count):  # count blocks that differ, each from the one before by the delta of its first code point
        return {0x1000 + BLOCK * k: (0x1000 + BLOCK * k + k + 1,) for k in range(count)}

    out = got(lambda: tables(blocks(256)))
    cases.append(("tables: more than 256 distinct blocks, block 0 among them, are refused",
                  isinstance(out, ValueError) and "one byte" in str(out), brief(out)))
    out = got(lambda: tables(blocks(255)))
    cases.append(("tables: 256 distinct blocks are numbered in one byte",
                  not isinstance(out, Exception) and max(out[0]) == 255 and len(out[1]) == 256 * BLOCK, brief(out)))
    out = got(lambda: tables({0x20000: (0x20001,)}))
    cases.append(("tables: a mapping to one code point past the first two planes is refused",
                  isinstance(out, ValueError) and "first two planes" in str(out), out))
    out = got(lambda: tables({0x20000: (0x20041, 0x20042)}))
    cases.append(("tables: a mapping to several code points past the first two planes is refused",
                  isinstance(out, ValueError) and "first two planes" in str(out), out))
    props = "0041..005A    ; Cased # L&\n0061..007A ; Cased\n00AA ; Cased\n00AB ; Other\n0027 ; Case_Ignorable # Po"
    cases.append(("ranges: adjacent ranges merge and other properties stay out",
                  ranges(props, "Cased") == [(0x41, 0x5A), (0x61, 0x7A), (0xAA, 0xAA)] and ranges("0041..0042 ; Cased\n0043 ; Cased", "Cased") == [(0x41, 0x43)],
                  ranges(props, "Cased")))
    out = got(lambda: generate(data, special, props))
    cases.append(("generate: a table below its floor is refused", isinstance(out, ValueError) and "floor" in str(out), out))
    arrays = generate(data, special, props, floors={})
    cases.append(("render and parse: the written file reads back as the arrays", parse(render(arrays)) == arrays, parse(render(arrays))))
    deltas = list(arrays["LowerDeltas"])
    deltas[arrays["LowerIndex"][0xC0 // BLOCK] * BLOCK + 0xC0 % BLOCK] = 33
    changed = dict(arrays, LowerDeltas=deltas)
    said = changes(arrays, changed)
    cases.append(("changes: a changed mapping is named with both values", said == ["lower U+00C0: U+00E0 -> U+00E1"], said))

    with kit.scratch("kurz-ucd-") as cache:
        body = b"0041;A;Lu;0;L;;;;;N;;;;0061;\n"
        digest = hashlib.sha256(body).hexdigest()

        class Answer:
            def __init__(self, data):
                self.data = data

            def __enter__(self):
                return self

            def __exit__(self, *_):
                return False

            def read(self):
                return self.data

        asked = []

        def opener(url, timeout):
            asked.append(url)
            return Answer(body)

        out = got(lambda: fetch("UnicodeData.txt", digest="0" * 64, opener=opener, cache=cache))
        cases.append(("fetch: a file of another hash is refused", isinstance(out, ValueError) and "refused" in str(out), out))
        out = got(lambda: fetch("UnicodeData.txt", digest=digest, opener=opener, cache=cache))
        cases.append(("fetch: the pinned version's file, checked by its hash", out == body.decode() and asked[-1].endswith(f"/{VERSION}/ucd/UnicodeData.txt"), (out, asked)))
        asked.clear()
        out = got(lambda: fetch("UnicodeData.txt", digest=digest, opener=opener, cache=cache))
        cases.append(("fetch: a kept file whose hash holds is read without a fetch", out == body.decode() and not asked, (out, asked)))
        with open(os.path.join(cache, f"kurz-ucd-{digest}.txt"), "wb") as f:
            f.write(b"changed")
        out = got(lambda: fetch("UnicodeData.txt", digest=digest, opener=opener, cache=cache))
        cases.append(("fetch: a kept file of another hash is fetched again", out == body.decode() and len(asked) == 1, (out, asked)))

        def closed(url, timeout):
            raise OSError("no route")

        out = got(lambda: fetch("SpecialCasing.txt", digest="1" * 64, opener=closed, cache=cache))
        cases.append(("fetch: a file that cannot be fetched is unread", isinstance(out, Unread), out))

        class CutOff(Answer):
            def read(self):
                raise http.client.IncompleteRead(b"0041;A;")

        out = got(lambda: fetch("SpecialCasing.txt", digest="2" * 64, opener=lambda url, timeout: CutOff(body), cache=cache))
        cases.append(("fetch: a download cut off is unread", isinstance(out, Unread), out))
        other = b"0042;B;Lu;0;L;;;;;N;;;;0062;\n"
        other_digest = hashlib.sha256(other).hexdigest()
        os.makedirs(os.path.join(cache, f"kurz-ucd-{other_digest}.txt"))
        out = got(lambda: fetch("UnicodeData.txt", digest=other_digest, opener=lambda url, timeout: Answer(other), cache=cache))
        cases.append(("fetch: a kept file that cannot be read is fetched again", out == other.decode(), out))

        texts = {"UnicodeData.txt": data, "SpecialCasing.txt": special, "DerivedCoreProperties.txt": props}
        os.makedirs(os.path.join(cache, os.path.dirname(OUT)))
        import contextlib
        import io

        def run(argv, fetcher=lambda name: texts[name]):
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                code = main(argv, root=cache, fetcher=fetcher, floors={})
            return code, printed.getvalue()

        code, said = run([])
        written = kit.read(os.path.join(cache, OUT))
        cases.append(("main: writes the file where there was none", code == 0 and parse(written) == arrays and "there was none" in said, (code, said)))
        code, said = run(["--check"])
        cases.append(("main --check: the file the database gives passes", code == 0, (code, said)))
        with open(os.path.join(cache, OUT), "w", encoding="utf-8") as f:
            f.write(render(changed))
        code, said = run(["--check"])
        cases.append(("main --check: a file that differs fails, naming the mapping", code == 1 and "lower U+00C0: U+00E1 -> U+00E0" in said, (code, said)))
        code, said = run([])
        written = kit.read(os.path.join(cache, OUT))
        cases.append(("main: writes the file again and names every change", code == 0 and parse(written) == arrays and "lower U+00C0: U+00E1 -> U+00E0" in said, (code, said)))

        def unread(name):
            raise Unread(f"{name}: could not be fetched")

        code, said = run(["--check"], fetcher=unread)
        cases.append(("main --check: an unfetched file is PARTLY, never a pass", code == kit.PARTLY, (code, said)))

        def wrong(name):
            raise ValueError(f"{name}: SHA-256 0, where 1 is pinned: refused")

        code, said = run(["--check"], fetcher=wrong)
        cases.append(("main --check: a refused file fails", code == 1, (code, said)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit(main(sys.argv[1:]))
