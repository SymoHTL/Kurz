#!/usr/bin/env python3
"""The case tables of X3, generated from the Unicode Character Database of the version Rust's stable
`char::UNICODE_VERSION` pins (the owner, 2026-10-31, "Rust's pin" and "Generated, as Rust"), as Rust's
`unicode-table-generator` makes the tables of `core::unicode::conversions`.

  unicode_tables.py              write compiler/Kurz.Compiler/Emit/CaseTables.cs from the database's files
                                 and print every mapping that changed against the file it replaces
  unicode_tables.py --check      the gate: red where the file is not what the files give, naming each
                                 mapping that differs
  unicode_tables.py --self-test

The three files are fetched from unicode.org at the pinned version and each is checked by its SHA-256
before it is read: a file of another hash is refused (FAIL), and a file that cannot be fetched leaves
the tables unread (PARTLY, which is red: nothing was compared). A fetched file is kept under its hash
in the system's temporary directory, and read from there while its hash holds.

What the tables hold, as Rust's generator takes them:
- the mappings: for every code point above ASCII, UnicodeData.txt's simple lowercase and uppercase
  mappings, then SpecialCasing.txt's mappings without a condition over them; a mapping to the code
  point itself is left out, so that SpecialCasing.txt's identity keeps the simple mapping; Final_Sigma,
  the one condition that is no language's, is the standard library's code (X3), and the mappings of a
  language are left out, as "no culture" (section 4) says;
- the runs of the mappings to one code point, per plane, in Rust's two passes: consecutive code points
  with one delta make a run of step 1, and lone code points two apart with one delta a run of step 2,
  each run at most 256 code points; every mapping stays in its plane, and only the first two planes
  hold mappings;
- the mappings to two or three code points, padded with zeros;
- the ranges of Cased and Case_Ignorable from DerivedCoreProperties.txt, ASCII among them, which
  Rust's `is_cased` and `is_case_ignorable` take from the same properties.
A table with fewer entries than its floor is refused, so that a file read as empty is never a table."""
import hashlib
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
# the entries each table holds at the least; 17.0.0 holds 184 and 197 runs, 1 and 102 multis, 158 and 464 ranges
FLOORS = {"LowerRuns": 150, "UpperRuns": 150, "LowerMultis": 1, "UpperMultis": 50, "Cased": 100, "CaseIgnorable": 300}
WIDTH = {"LowerRuns": 4, "UpperRuns": 4, "LowerMultis": 4, "UpperMultis": 4, "Cased": 2, "CaseIgnorable": 2}


class Unread(Exception):
    """A file of the database that could not be fetched: nothing was compared."""


def fetch(name, version=VERSION, digest=None, opener=urllib.request.urlopen, cache=None):
    """The text of one file of the database, checked by its SHA-256. A file of another hash raises
    ValueError; one that cannot be fetched raises Unread."""
    digest = digest or FILES[name]
    kept = os.path.join(cache or tempfile.gettempdir(), f"kurz-ucd-{digest}.txt")
    if os.path.exists(kept):
        with open(kept, "rb") as f:
            data = f.read()
        if hashlib.sha256(data).hexdigest() == digest:
            return data.decode("utf-8")
    try:
        with opener(SOURCE.format(version=version, name=name), timeout=60) as r:
            data = r.read()
    except OSError as e:
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
    """(runs, multis) of one direction: runs as (first, last, step, delta), multis as (key, a, b, c)."""
    runs, multis = [], []
    for plane in range(17):
        singles = []
        for key in sorted(k for k in mapping if k >> 16 == plane):
            out = mapping[key]
            if any(c >> 16 != plane for c in out):
                raise ValueError(f"U+{key:04X}: a mapping that leaves its plane")
            if len(out) == 1:
                singles.append([key, key, 1, out[0] - key])
            else:
                multis.append((key,) + out + (0,) * (3 - len(out)))
        if singles and plane > 1:
            raise ValueError(f"plane {plane}: a mapping past the first two planes")
        joined = []
        for run in singles:
            if joined and joined[-1][3] == run[3] and run[0] - joined[-1][1] == 1:
                joined[-1][1] = run[1]
            else:
                joined.append(run)
        stepped = []
        for run in joined:
            last = stepped[-1] if stepped else None
            if last and last[3] == run[3] and run[0] == run[1] and run[0] - last[1] == 2 and (last[0] == last[1] or last[2] == 2):
                last[1], last[2] = run[1], 2
            else:
                stepped.append(run)
        for first, end, _, _ in stepped:
            if end - first > 255:
                raise ValueError(f"U+{first:04X}: a run of more than 256 code points")
        runs += [tuple(r) for r in stepped]
    return runs, multis


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
    lower_runs, lower_multis = tables(lower)
    upper_runs, upper_multis = tables(upper)
    out = {
        "LowerRuns": [x for r in lower_runs for x in r],
        "LowerMultis": [x for m in lower_multis for x in m],
        "UpperRuns": [x for r in upper_runs for x in r],
        "UpperMultis": [x for m in upper_multis for x in m],
        "Cased": [x for r in ranges(properties, "Cased") for x in r],
        "CaseIgnorable": [x for r in ranges(properties, "Case_Ignorable") for x in r],
    }
    for name, floor in floors.items():
        if len(out[name]) // WIDTH[name] < floor:
            raise ValueError(f"{name}: {len(out[name]) // WIDTH[name]} entries, fewer than the floor of {floor}")
    return out


SUMMARIES = {
    "LowerRuns": "The runs of the lowercase mappings to one code point, four numbers each: the first and the last code point, the step, 1 or 2, and the delta every code point of the run takes, a code point between them that the step skips taking none.",
    "LowerMultis": "The lowercase mappings to two or three code points, four numbers each: the code point and the code points it gives, a zero after the last.",
    "UpperRuns": "The runs of the uppercase mappings to one code point, as <see cref=\"LowerRuns\"/> holds the lowercase ones.",
    "UpperMultis": "The uppercase mappings to two or three code points, as <see cref=\"LowerMultis\"/> holds the lowercase ones.",
    "Cased": "The ranges of the property Cased, the first and the last code point of each, in order.",
    "CaseIgnorable": "The ranges of the property Case_Ignorable, as <see cref=\"Cased\"/> holds its ranges.",
}


def number(value, at, name):
    """One number of an array as the file writes it: a code point in hexadecimal, a run's step and
    delta in decimal."""
    return str(value) if name.endswith("Runs") and at % 4 >= 2 else f"0x{value:X}"


def render(arrays):
    """The C# file that holds the arrays."""
    lines = [
        f"// Generated by tools/unicode_tables.py from the Unicode Character Database {VERSION}: run the tool, never edit this file.",
        "namespace Kurz.Compiler.Emit;",
        "",
        "/// <summary>",
        "/// X3: the case tables of Unicode's full mappings and of the two properties Final_Sigma reads, of the version Rust's stable",
        "/// <c>char::UNICODE_VERSION</c> pins, in the runs Rust's <c>core::unicode::conversions</c> holds them in, for every code",
        "/// point above ASCII.",
        "/// </summary>",
        "internal static class CaseTables",
        "{",
        "    /// <summary>The version of the Unicode Character Database the tables are generated from.</summary>",
        f"    public const string Version = \"{VERSION}\";",
    ]
    for name in ("LowerRuns", "LowerMultis", "UpperRuns", "UpperMultis", "Cased", "CaseIgnorable"):
        values = arrays[name]
        per = WIDTH[name] * (4 if WIDTH[name] == 4 else 6)
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
        runs = arrays.get(f"{kind}Runs", [])
        for at in range(0, len(runs) - 3, 4):
            first, last, step, delta = runs[at:at + 4]
            for cp in range(first, last + 1, step):
                found[(kind.lower(), cp)] = (cp + delta,)
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

    data = "\n".join([ud("0041", lower="0061"), ud("00C0", lower="00E0"), ud("00E0", upper="00C0"), ud("0130", lower="0069"),
                      ud("00DF"), ud("01C5", upper="01C4", lower="01C6"), ud("03A3", lower="03C3")])
    special = "\n".join(["# a comment", "00DF; 00DF; 0053 0073; 0053 0053; # SHARP S", "0130; 0069 0307; 0130; 0130; # I WITH DOT",
                         "03A3; 03C2; 03A3; 03A3; Final_Sigma; # SIGMA", "0049; 0131; 0049; 0049; tr Not_Before_Dot; # I",
                         "01C5; 01C6; 01C5; 01C4; # DZ WITH CARON"])
    lower, upper = mappings(data, special)
    cases.append(("mappings: UnicodeData's simple mapping", lower.get(0xC0) == (0xE0,) and upper.get(0xE0) == (0xC0,), (lower, upper)))
    cases.append(("mappings: SpecialCasing's mapping without a condition takes the place of the simple one",
                  lower.get(0x130) == (0x69, 0x307) and upper.get(0xDF) == (0x53, 0x53), (lower, upper)))
    cases.append(("mappings: a conditional mapping is left out", lower.get(0x3A3) == (0x3C3,) and 0x49 not in lower, lower))
    cases.append(("mappings: an identity in SpecialCasing keeps the simple mapping", lower.get(0x1C5) == (0x1C6,) and 0xDF not in lower, lower))
    cases.append(("mappings: ASCII is left out", 0x41 not in lower, lower))

    def runs_of(mapping):
        return tables(mapping)[0]

    cases.append(("tables: consecutive code points with one delta make a run of step 1",
                  runs_of({0xC0: (0xE0,), 0xC1: (0xE1,), 0xC2: (0xE2,), 0xC4: (0xC5,)}) == [(0xC0, 0xC2, 1, 32), (0xC4, 0xC4, 1, 1)],
                  runs_of({0xC0: (0xE0,), 0xC1: (0xE1,), 0xC2: (0xE2,), 0xC4: (0xC5,)})))
    cases.append(("tables: lone code points two apart with one delta make a run of step 2",
                  runs_of({0x100: (0x101,), 0x102: (0x103,), 0x104: (0x105,)}) == [(0x100, 0x104, 2, 1)],
                  runs_of({0x100: (0x101,), 0x102: (0x103,), 0x104: (0x105,)})))
    cases.append(("tables: a run of step 1 does not take a code point two after it",
                  runs_of({0xC0: (0xC1,), 0xC1: (0xC2,), 0xC3: (0xC4,)}) == [(0xC0, 0xC1, 1, 1), (0xC3, 0xC3, 1, 1)],
                  runs_of({0xC0: (0xC1,), 0xC1: (0xC2,), 0xC3: (0xC4,)})))
    cases.append(("tables: a mapping to two code points is a multi, padded with a zero",
                  tables({0x130: (0x69, 0x307)}) == ([], [(0x130, 0x69, 0x307, 0)]), tables({0x130: (0x69, 0x307)})))
    out = got(lambda: tables({0xFFFF: (0x10000,)}))
    cases.append(("tables: a mapping that leaves its plane is refused", isinstance(out, ValueError), out))
    out = got(lambda: tables({cp: (cp + 1,) for cp in range(0x1000, 0x1000 + 257)}))
    cases.append(("tables: a run of more than 256 code points is refused", isinstance(out, ValueError), out))
    props = "0041..005A    ; Cased # L&\n0061..007A ; Cased\n00AA ; Cased\n00AB ; Other\n0027 ; Case_Ignorable # Po"
    cases.append(("ranges: adjacent ranges merge and other properties stay out",
                  ranges(props, "Cased") == [(0x41, 0x5A), (0x61, 0x7A), (0xAA, 0xAA)] and ranges("0041..0042 ; Cased\n0043 ; Cased", "Cased") == [(0x41, 0x43)],
                  ranges(props, "Cased")))
    out = got(lambda: generate(data, special, props))
    cases.append(("generate: a table below its floor is refused", isinstance(out, ValueError) and "floor" in str(out), out))
    arrays = generate(data, special, props, floors={})
    cases.append(("render and parse: the written file reads back as the arrays", parse(render(arrays)) == arrays, parse(render(arrays))))
    changed = dict(arrays, LowerRuns=[0xC0, 0xC0, 1, 33] + arrays["LowerRuns"][4:])
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
