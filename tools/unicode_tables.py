#!/usr/bin/env python3
"""The case tables of X3, generated from the Unicode Character Database of the version Rust's stable
`char::UNICODE_VERSION` pins (the owner, 2026-10-31, "Rust's pin" and "Generated, as Rust"), from the mappings
Rust's `unicode-table-generator` takes for the tables of `core::unicode::conversions`, in levels of deltas, as
.NET's `CharUnicodeInfo` holds its mappings (issue 157: faster with the same results, under the owner's answer of
2026-11-01, "Allow, faster wins"), and the tables of G4 from the same version: the classes `\\d`, `\\w` and `\\s`
and the simple case folding, as Rust's `regex` reads them.

  unicode_tables.py              write compiler/Kurz.Compiler/Emit/CaseTables.cs from the database's files
                                 and print every mapping that changed against the file it replaces
  unicode_tables.py --check      the gate: red where the file is not what the files give, naming the
                                 first 40 mappings that differ and counting the rest
  unicode_tables.py --self-test

Each call alone: any other, `--help` among them, is refused with the usage and exit 2 before
anything is fetched or written (tools/calls.py).

The fourteen files are fetched from unicode.org at the pinned version and each is checked by its SHA-256
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
  Rust's `is_cased` and `is_case_ignorable` take from the same properties;
- G4's classes, as Rust's `regex` reads them (`regex-syntax`, `unicode::perl_digit`, `perl_word` and
  `perl_space`): the ranges of the general category Nd from UnicodeData.txt for `\\d`; of Alphabetic from
  DerivedCoreProperties.txt, the marks Mn, Mc and Me, Nd and Pc from UnicodeData.txt and Join_Control from
  PropList.txt, merged, for `\\w`; and of White_Space from PropList.txt for `\\s`. A range UnicodeData.txt
  writes as its first and its last line, `<..., First>` and `<..., Last>`, is taken whole;
- the characters of a group's name, as `regex-syntax` reads one (`is_capture_char`, through Rust's
  `char::is_alphabetic` and `char::is_numeric`): the ranges of Alphabetic from DerivedCoreProperties.txt, of
  which a name's first character is one, and of the general categories Nd, Nl and No from UnicodeData.txt;
- the simple case folding of CaseFolding.txt, its statuses C and S, as Rust's `regex` folds a pattern under
  `(?i)`: each set of the code points that fold together, as a cycle of each code point and the next of
  its set in ascending order, the last with the first;
- the classes of `\\p` and `\\P` of G4, as `regex-syntax` 0.8.11 reads a name (`unicode::ClassQuery`) and as
  `ucd-generate` 0.3.1 writes its tables: the general categories of UnicodeData.txt, Cn where it lists none
  and Cs left out, with the groups of PropertyValueAliases.txt, Other with Cs; the scripts of Scripts.txt;
  the script extensions, a script's code points less those ScriptExtensions.txt lists and those it lists for
  the script; the versions of DerivedAge.txt, an age every version up to it; the values of the three break
  properties of auxiliary/; and the binary properties of PropList.txt, DerivedCoreProperties.txt and
  emoji/emoji-data.txt, with Bidi_Mirrored from UnicodeData.txt. A range's end inside the surrogates moves
  out of them, as `--chars` writes it. Every name is resolved here, as the crate resolves it, into a key: a
  kind, then the name as `symbolic_name_normalize` gives it; a name the crate refuses has no key. The stream
  (`RegexProperties`) is the length of the keys' part as a varint, then each key, its length in one byte,
  its bytes, the offset of its first set in the sets' part as a varint and its number of sets in one byte,
  then the sets, each its number of ranges and each range's gap from the last end and its length, as
  varints; a set is stored once, and the ages in their order, so that an age is its first sets.
This tool refuses the forms its tables do not hold: a mapping that leaves its plane, a mapping past
the first two planes, to one code point or to several, a delta of 32768, and more distinct blocks than
an index of one byte numbers; and levels that, read back, do not give the mappings they were made
from. A table with fewer entries than its floor is refused, so that a file read as empty is never a
table."""
import base64
import hashlib
import http.client
import os
import re
import sys
import tempfile
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402
import calls  # noqa: E402

VERSION = "17.0.0"
FILES = {
    "UnicodeData.txt": "2e1efc1dcb59c575eedf5ccae60f95229f706ee6d031835247d843c11d96470c",
    "SpecialCasing.txt": "efc25faf19de21b92c1194c111c932e03d2a5eaf18194e33f1156e96de4c9588",
    "DerivedCoreProperties.txt": "24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08",
    "PropList.txt": "130dcddcaadaf071008bdfce1e7743e04fdfbc910886f017d9f9ac931d8c64dd",
    "CaseFolding.txt": "ff8d8fefbf123574205085d6714c36149eb946d717a0c585c27f0f4ef58c4183",
    "PropertyAliases.txt": "4441f573caf952ffece1d7c892e7715bd7136dfc26f96eb6f268bf1e474715fb",
    "PropertyValueAliases.txt": "64e9a5f76f7a1e8b5a47d6a1f9a26522a251208f5276bdfa1559dac7cf2e827a",
    "Scripts.txt": "9f5e50d3abaee7d6ce09480f325c706f485ae3240912527e651954d2d6b035bf",
    "ScriptExtensions.txt": "ec2107e58825a1586acee8e0911ce18260394ac8b87e535ca325f1ccbeb06bc6",
    "DerivedAge.txt": "f8ecdf768bdc210f201abd271d9bc587825618a86a7046a8146cc816393f1998",
    "auxiliary/GraphemeBreakProperty.txt": "d6b51d1d2ae5c33b451b7ed994b48f1f4dc62b2272a5831e7fd418514a6bae89",
    "auxiliary/WordBreakProperty.txt": "72274cac1e6b919507db35655c3e175aa27274668a1ece95c28d2069f2ad9852",
    "auxiliary/SentenceBreakProperty.txt": "871c0c985ad95125e25b302414065a10839d068970bceb383ecec138f22a0a18",
    "emoji/emoji-data.txt": "2cb2bb9455cda83e8481541ecf5b6dfda66a3bb89efa3fa7c5297eccf607b72b",
}
# the files of the classes of `\p` and `\P`, beside the five of the case tables
PROPERTY_FILES = ("PropertyAliases.txt", "PropertyValueAliases.txt", "Scripts.txt", "ScriptExtensions.txt", "DerivedAge.txt",
                  "auxiliary/GraphemeBreakProperty.txt", "auxiliary/WordBreakProperty.txt", "auxiliary/SentenceBreakProperty.txt",
                  "emoji/emoji-data.txt")
SOURCE = "https://www.unicode.org/Public/{version}/ucd/{name}"
OUT = "compiler/Kurz.Compiler/Emit/CaseTables.cs"
# the levels: the code points of the first two planes in blocks of 32, an index of one byte per block, and the
# deltas of each distinct block, 16 bits each; MARK is the delta of a code point that maps to several code points
BLOCK = 32
PLANES = 0x20000
MARK = 0x8000
# the entries each table holds at the least; 17.0.0 holds 69 and 82 blocks of deltas, 1 and 102 multis, 158 and 464
# ranges, and 72, 802 and 10 ranges of the classes, 2994 code points that fold, and 761 and 146 ranges of a name's characters,
# and 79003 bytes of the classes of `\p`
FLOORS = {"LowerDeltas": 30, "UpperDeltas": 30, "LowerMultis": 1, "UpperMultis": 50, "Cased": 100, "CaseIgnorable": 300,
          "RegexDigit": 60, "RegexWord": 700, "RegexSpace": 8, "RegexFold": 2500, "RegexAlpha": 700, "RegexNumber": 130,
          "RegexProperties": 40000}
WIDTH = {"LowerIndex": 1, "UpperIndex": 1, "LowerDeltas": BLOCK, "UpperDeltas": BLOCK, "LowerMultis": 4, "UpperMultis": 4,
         "Cased": 2, "CaseIgnorable": 2, "RegexDigit": 2, "RegexWord": 2, "RegexSpace": 2, "RegexFold": 2,
         "RegexAlpha": 2, "RegexNumber": 2, "RegexProperties": 1}
NAMES = ("LowerIndex", "LowerDeltas", "LowerMultis", "UpperIndex", "UpperDeltas", "UpperMultis", "Cased", "CaseIgnorable",
         "RegexDigit", "RegexWord", "RegexSpace", "RegexFold", "RegexAlpha", "RegexNumber", "RegexProperties")


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
    """The merged ranges of one property of DerivedCoreProperties.txt or PropList.txt, as (first, last)."""
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
    return merge(found)


def merge(found):
    """Ranges as (first, last), sorted, with those that overlap or touch made one."""
    merged = []
    for a, b in sorted(found):
        if merged and a <= merged[-1][1] + 1:
            merged[-1] = (merged[-1][0], max(b, merged[-1][1]))
        else:
            merged.append((a, b))
    return merged


def category_ranges(unicode_data):
    """{general category: the ranges of its code points in UnicodeData.txt, in the file's order}; a range the file
    writes as its first and its last line, `<..., First>` and `<..., Last>`, is taken whole."""
    category, first = {}, None
    for line in unicode_data.splitlines():
        if not line.strip():
            continue
        f = line.split(";")
        cp = int(f[0], 16)
        if f[1].endswith(", First>"):
            first = cp
            continue
        start = first if first is not None and f[1].endswith(", Last>") else cp
        first = None
        category.setdefault(f[2], []).append((start, cp))
    return category


def categories(unicode_data, names):
    """The merged ranges of the code points of UnicodeData.txt whose general category is one of `names`."""
    found = category_ranges(unicode_data)
    return merge([r for name in names for r in found.get(name, [])])


def folds(case_folding):
    """The simple case folding of CaseFolding.txt, its statuses C and S, as Rust's `regex` folds a pattern under
    `(?i)`: every code point that folds with another, with the next code point of its set in ascending order, the
    last with the first, as (code point, next), in the order of the code points."""
    sets = {}
    for line in case_folding.splitlines():
        body = line.split("#")[0].strip()
        if not body:
            continue
        f = [x.strip() for x in body.split(";")]
        if f[1] not in ("C", "S"):
            continue
        a, b = int(f[0], 16), int(f[2], 16)
        sets.setdefault(b, {b}).add(a)
    out = []
    for members in sets.values():
        ordered = sorted(members)
        out += [(c, ordered[(k + 1) % len(ordered)]) for k, c in enumerate(ordered)]
    return sorted(out)


# the kinds of a key of the classes of `\p` (G4), its first byte: a name read alone (`\pL`, `\p{Greek}`), the value of each
# property the crate takes by value (`\p{sc=Greek}`), and the name of such a property, whose key holds its values' kind
KIND_ALONE, KIND_PROPERTY = 0, 8
VALUE_KINDS = {"General_Category": 1, "Script": 2, "Script_Extensions": 3, "Age": 4, "Grapheme_Cluster_Break": 5,
               "Word_Break": 6, "Sentence_Break": 7}
SURROGATES = (0xD800, 0xDFFF)
# the groups of the general categories, as PropertyValueAliases.txt writes them; Other holds Cs, which has no set of its own
GROUPS = {"Cased_Letter": ("Lu", "Ll", "Lt"), "Letter": ("Lu", "Ll", "Lt", "Lm", "Lo"), "Mark": ("Mn", "Mc", "Me"),
          "Number": ("Nd", "Nl", "No"), "Punctuation": ("Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po"), "Symbol": ("Sm", "Sc", "Sk", "So"),
          "Separator": ("Zs", "Zl", "Zp"), "Other": ("Cc", "Cf", "Cn", "Co", "Cs")}
TOP = 0x10FFFF


def normalized(name):
    """A name as regex-syntax's `symbolic_name_normalize` gives it: a first `is` of any case dropped, ` `, `_` and `-`
    dropped, ASCII's capitals made small, every byte above ASCII dropped, and `is` with `c` alone after it kept as `isc`."""
    data = name.encode("utf-8")
    prefixed = len(data) >= 2 and data[:2].lower() == b"is"
    out = bytearray()
    for b in data[2 if prefixed else 0:]:
        if b in b" _-" or b > 0x7F:
            continue
        out.append(b + 32 if 65 <= b <= 90 else b)
    return "isc" if prefixed and out == b"c" else out.decode("ascii")


def chars(found):
    """Ranges as ucd-generate's `--chars` writes them: merged as code points, then an end inside the surrogates moved out of
    them, so that a range of surrogates alone is left out and one across them stays whole."""
    out = []
    for a, b in merge(found):
        a = SURROGATES[1] + 1 if SURROGATES[0] <= a <= SURROGATES[1] else a
        b = SURROGATES[0] - 1 if SURROGATES[0] <= b <= SURROGATES[1] else b
        if a <= b:
            out.append((a, b))
    return out


def rows(text):
    """The fields of each line of a file of the database that holds data, its comment left out."""
    for line in text.splitlines():
        body = line.split("#")[0].strip()
        if body:
            yield [x.strip() for x in body.split(";")]


def listed(text):
    """{value: [(first, last)]} of a file whose lines are `code points ; value`."""
    out = {}
    for f in rows(text):
        a, _, b = f[0].partition("..")
        out.setdefault(f[1], []).append((int(a, 16), int(b or a, 16)))
    return out


def property_sets(unicode_data, properties, prop_list, texts):
    """(sets, names, values): {kind: {canonical name: ranges}} of the classes of `\\p`, {normalized name: property} of
    PropertyAliases.txt, and {property: {normalized value: canonical value}} of PropertyValueAliases.txt."""
    names = {}
    for f in rows(texts["PropertyAliases.txt"]):
        for alias in f:
            names[normalized(alias)] = f[1]
    values, shorts = {}, {}
    for f in rows(texts["PropertyValueAliases.txt"]):
        prop = names.get(normalized(f[0]))
        if prop is None or prop == "Canonical_Combining_Class":
            continue
        for alias in f[1:]:
            values.setdefault(prop, {})[normalized(alias)] = f[2]
        shorts.setdefault(prop, {})[f[1]] = f[2]
    values["Script_Extensions"] = values.get("Script", {})
    category = category_ranges(unicode_data)
    category["Cn"] = invert(merge([r for found in category.values() for r in found]))
    gc_long = shorts.get("General_Category", {})
    gc = {gc_long.get(short, short): chars(found) for short, found in category.items() if short != "Cs"}
    for group, members in GROUPS.items():
        gc[group] = chars([r for m in members for r in category.get(m, [])])
    script = listed(texts["Scripts.txt"])
    of = {}
    for name, found in script.items():
        for a, b in found:
            for cp in range(a, b + 1):
                of[cp] = name
    sc_long = shorts.get("Script", {})
    extended = {name: set() for name in script}
    moved = set()
    for codes, found in listed(texts["ScriptExtensions.txt"]).items():
        for a, b in found:
            for cp in range(a, b + 1):
                moved.add(cp)
                for short in codes.split():
                    extended.setdefault(sc_long.get(short, short), set()).add(cp)
    scx = {}
    for name in extended:
        points = extended[name] | {cp for a, b in script.get(name, []) for cp in range(a, b + 1) if cp not in moved}
        scx[name] = chars([(cp, cp) for cp in points])
    ages = listed(texts["DerivedAge.txt"])
    order = sorted(ages, key=lambda v: tuple(int(x) for x in v.split(".")))
    binary = {}
    for text in (prop_list, properties, texts["emoji/emoji-data.txt"]):
        for name, found in listed(text).items():
            binary.setdefault(name, []).extend(found)
    binary["Bidi_Mirrored"] = [(int(f[0], 16), int(f[0], 16)) for f in (line.split(";") for line in unicode_data.splitlines() if line.strip())
                               if f[9] == "Y"]
    sets = {
        "General_Category": gc,
        "Script": {name: chars(found) for name, found in script.items()},
        "Script_Extensions": {name: found for name, found in scx.items() if found},
        "Age": {"V" + v.replace(".", "_"): chars(ages[v]) for v in order},
        "Grapheme_Cluster_Break": {k: chars(v) for k, v in listed(texts["auxiliary/GraphemeBreakProperty.txt"]).items()},
        "Word_Break": {k: chars(v) for k, v in listed(texts["auxiliary/WordBreakProperty.txt"]).items()},
        "Sentence_Break": {k: chars(v) for k, v in listed(texts["auxiliary/SentenceBreakProperty.txt"]).items()},
        "Binary": {k: chars(v) for k, v in binary.items()},
    }
    sets["General_Category"]["Any"] = [(0, TOP)]
    sets["General_Category"]["ASCII"] = [(0, 0x7F)]
    sets["General_Category"]["Assigned"] = chars(invert(sets["General_Category"].get("Unassigned", [])))
    return sets, names, values


def invert(found):
    """The ranges of every code point that `found` does not hold."""
    out, at = [], 0
    for a, b in found:
        if a > at:
            out.append((at, a - 1))
        at = b + 1
    if at <= TOP:
        out.append((at, TOP))
    return out


def classes(sets, names, values):
    """{key: list of sets}, every key of the classes of `\\p` the crate builds, resolved as its `ClassQuery::canonicalize` and
    `unicode::class` resolve it, and {key: kind} of the properties it reads by value. A name alone is a property's name before a
    category's and a script's, but for `cf`, `sc` and `lc`, and the name of a property that is not binary is no class; a key of
    an age holds the sets of every version up to it."""
    gc_values = dict(values.get("General_Category", {}), any="Any", assigned="Assigned", ascii="ASCII")
    out, kinds = {}, {}
    ages = list(sets["Age"])
    for norm in sorted(set(names) | set(gc_values) | set(values.get("Script", {}))):
        if norm not in ("cf", "sc", "lc") and norm in names:
            found = sets["Binary"].get(names[norm])
        elif norm in gc_values:
            found = sets["General_Category"].get(gc_values[norm])
        else:
            found = sets["Script"].get(values["Script"][norm])
        if found:
            out[bytes([KIND_ALONE]) + norm.encode()] = [found]
    for norm, prop in names.items():
        if prop in VALUE_KINDS:
            kinds[bytes([KIND_PROPERTY]) + norm.encode()] = VALUE_KINDS[prop]
    for prop, kind in VALUE_KINDS.items():
        table = gc_values if prop == "General_Category" else values.get(prop, {})
        for norm, canon in table.items():
            if prop == "Age":
                found = [sets["Age"][v] for v in ages[:ages.index(canon) + 1]] if canon in sets["Age"] else None
            else:
                one = sets[prop].get(canon)
                found = [one] if one else None
            if found:
                out[bytes([kind]) + norm.encode()] = found
    return out, kinds


def varint(n, out):
    while n >= 0x80:
        out.append(n & 0x7F | 0x80)
        n >>= 7
    out.append(n)


def encode(sets, names, values):
    """The stream of the classes of `\\p`: the length of the keys' part as a varint, the keys in the order of their bytes,
    each its length in one byte, its bytes, the offset of its first set in the sets' part as a varint and its number of sets in
    one byte (a property's name: its values' kind and 0), then the sets, each stored once, the ages first in their order."""
    found, kinds = classes(sets, names, values)
    stored, part = {}, bytearray()

    def write(ranges):
        at = len(part)
        varint(len(ranges), part)
        last = -1
        for a, b in ranges:
            varint(a - last - 1, part)
            varint(b - a, part)
            last = b
        return at

    def store(ranges):
        key = tuple(ranges)
        if key not in stored:
            stored[key] = write(ranges)
        return stored[key]

    # the ages first, each written in its order even where two are alike, so that the key of an age, the one key of several sets
    # (`classes`), reads the sets of its versions in a row
    ages = []
    for v in sets["Age"].values():
        ages.append(write(v))
        stored.setdefault(tuple(v), ages[-1])
    keys = bytearray()
    for key in sorted(set(found) | set(kinds)):
        if len(key) > 255:
            raise ValueError(f"a key of {len(key)} bytes, longer than one byte counts")
        keys.append(len(key))
        keys += key
        if key in kinds:
            varint(kinds[key], keys)
            keys.append(0)
            continue
        at = ages[:len(found[key])] if len(found[key]) > 1 else [store(found[key][0])]
        varint(at[0], keys)
        keys.append(len(at))
    head = bytearray()
    varint(len(keys), head)
    return list(bytes(head + keys + part))


def decode(data):
    """{key: ranges} of a stream `encode` wrote, a property's name with its values' kind in place of ranges, the form the
    file's check compares two versions in."""
    data, at = bytes(data), 0

    def read():
        nonlocal at
        n, shift = 0, 0
        while True:
            b = data[at]
            at += 1
            n |= (b & 0x7F) << shift
            shift += 7
            if b < 0x80:
                return n

    end = read()
    end += at
    base = end
    out = {}
    while at < end:
        size = data[at]
        key = data[at + 1:at + 1 + size]
        at += 1 + size
        offset = read()
        count = data[at]
        at += 1
        if count == 0:
            out[key] = ("kind", offset)
            continue
        keep, at = at, base + offset
        found = []
        for _ in range(count):
            last = -1
            for _ in range(read()):
                a = last + 1 + read()
                last = a + read()
                found.append((a, last))
        out[key], at = tuple(merge(found)), keep
    return out


def generate(unicode_data, special_casing, properties, prop_list, case_folding, texts=None, floors=FLOORS):
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
        "RegexDigit": [x for r in categories(unicode_data, {"Nd"}) for x in r],
        "RegexWord": [x for r in merge(ranges(properties, "Alphabetic") + categories(unicode_data, {"Mn", "Mc", "Me", "Nd", "Pc"})
                                       + ranges(prop_list, "Join_Control")) for x in r],
        "RegexSpace": [x for r in ranges(prop_list, "White_Space") for x in r],
        "RegexFold": [x for pair in folds(case_folding) for x in pair],
        "RegexAlpha": [x for r in ranges(properties, "Alphabetic") for x in r],
        "RegexNumber": [x for r in categories(unicode_data, {"Nd", "Nl", "No"}) for x in r],
        "RegexProperties": encode(*property_sets(unicode_data, properties, prop_list, texts or {name: "" for name in PROPERTY_FILES})),
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
    "RegexDigit": "G4: the ranges of the general category Nd, which <c>\\d</c> matches, as <see cref=\"Cased\"/> holds its ranges.",
    "RegexWord": "G4: the ranges <c>\\w</c> matches, as Rust's <c>regex</c> reads it: Alphabetic, the marks Mn, Mc and Me, Nd, Pc and Join_Control, merged, as <see cref=\"Cased\"/> holds its ranges.",
    "RegexSpace": "G4: the ranges of the property White_Space, which <c>\\s</c> matches, as <see cref=\"Cased\"/> holds its ranges.",
    "RegexAlpha": "G4: the ranges of the property Alphabetic, of which the first character of a group's name is one, as Rust's <c>regex-syntax</c> reads a name through <c>char::is_alphabetic</c>, as <see cref=\"Cased\"/> holds its ranges.",
    "RegexNumber": "G4: the ranges of the general categories Nd, Nl and No, of which a later character of a group's name may be one beside Alphabetic, as Rust's <c>char::is_numeric</c> reads them, as <see cref=\"Cased\"/> holds its ranges.",
    "RegexProperties": "G4: the classes of <c>\\p</c> and <c>\\P</c>, as Rust's <c>regex-syntax</c> reads a name, in base64: the length of the keys' part as a varint; each key, its length in one byte, its bytes (a kind, 0 a name alone, 1 to 7 a value of General_Category, Script, Script_Extensions, Age, Grapheme_Cluster_Break, Word_Break and Sentence_Break, 8 the name of such a property, then the name as <c>symbolic_name_normalize</c> gives it), the offset of its first set in the sets' part as a varint and its number of sets in one byte, a union, or for a property's name its values' kind and 0; then the sets, each its number of ranges and each range's gap from the last end and its length, as varints.",
    "RegexFold": "G4: the simple case folding of the statuses C and S, which a pattern under <c>(?i)</c> reads, two numbers each: a code point that folds with another and the next code point of its set in ascending order, the last of a set with its first, in the order of the code points.",
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
        "/// <c>CharUnicodeInfo</c> holds its mappings, and the properties of every code point, ASCII among them; and G4's classes and",
        "/// simple case folding of the same version, as Rust's <c>regex</c> reads them.",
        "/// </summary>",
        "internal static class CaseTables",
        "{",
        "    /// <summary>The version of the Unicode Character Database the tables are generated from.</summary>",
        f"    public const string Version = \"{VERSION}\";",
    ]
    for name in NAMES:
        values = arrays[name]
        per = {1: 32, 2: 12, 4: 16}.get(WIDTH[name], 16)
        lines += ["", "    /// <summary>"]
        words, line = SUMMARIES[name].split(), "    ///"
        for word in words:
            if len(line) + 1 + len(word) > 136:
                lines.append(line)
                line = "    ///"
            line += " " + word
        if name in STRINGS:
            text = base64.b64encode(bytes(values)).decode("ascii")
            lines += [line, "    /// </summary>", f"    public const string {name} = \"\"\""]
            lines += ["        " + text[at:at + 128] for at in range(0, len(text), 128)]
            lines.append("        \"\"\";")
            continue
        lines += [line, "    /// </summary>", f"    public static readonly int[] {name} =", "    ["]
        for at in range(0, len(values), per):
            lines.append("        " + ", ".join(number(v, at + k, name) for k, v in enumerate(values[at:at + per])) + ",")
        lines.append("    ];")
    lines.append("}")
    return "\n".join(lines) + "\n"


ARRAY = re.compile(r"public static readonly int\[\] (\w+) =\s*\[(.*?)\];", re.S)
STRING = re.compile(r'public const string (\w+) = """(.*?)""";', re.S)
# the arrays the file holds as a string of base64, read by the compiler as bytes
STRINGS = ("RegexProperties",)


def parse(text):
    """The arrays of a written file, by name; an array written as base64 is read back as its bytes."""
    arrays = {name: [int(v, 0) for v in re.findall(r"-?(?:0x[0-9A-F]+|\d+)", body)] for name, body in ARRAY.findall(text)}
    for name, body in STRING.findall(text):
        try:
            arrays[name] = list(base64.b64decode("".join(body.split()), validate=True))
        except ValueError:
            arrays[name] = []
    return arrays


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
    for kind in ("Cased", "CaseIgnorable", "RegexDigit", "RegexWord", "RegexSpace", "RegexAlpha", "RegexNumber"):
        values = arrays.get(kind, [])
        for at in range(0, len(values) - 1, 2):
            for cp in range(values[at], values[at + 1] + 1):
                found[(kind, cp)] = True
    values = arrays.get("RegexFold", [])
    for at in range(0, len(values) - 1, 2):
        found[("fold", values[at])] = (values[at + 1],)
    try:
        for key, ranges_ in decode(arrays.get("RegexProperties") or [0]).items():
            found[("class", f"{key[0]}:{key[1:].decode('ascii', 'replace')}")] = ranges_
    except IndexError:
        found[("class", "the stream")] = ("cut off",)
    return found


def changes(old, new):
    """One line for each mapping or property that differs between two sets of arrays."""
    a, b = expand(old), expand(new)
    out = []
    for key in sorted(set(a) | set(b), key=lambda k: (k[0], str(k[1]) if k[0] == "class" else f"{k[1]:08X}")):
        if a.get(key) != b.get(key):
            if key[0] == "class":
                count = lambda v: "none" if v is None else f"{v[1]} kind" if v[0] == "kind" else f"{len(v)} ranges"  # noqa: E731
                out.append(f"class {key[1]}: {count(a.get(key))} -> {count(b.get(key))}")
                continue
            show = lambda v: "none" if v is None else ("yes" if v is True else " ".join(f"U+{c:04X}" for c in v))  # noqa: E731
            out.append(f"{key[0]} U+{key[1]:04X}: {show(a.get(key))} -> {show(b.get(key))}")
    return out


def files(fetcher=fetch):
    return [fetcher(name) for name in ("UnicodeData.txt", "SpecialCasing.txt", "DerivedCoreProperties.txt", "PropList.txt", "CaseFolding.txt")] + [
        {name: fetcher(name) for name in PROPERTY_FILES}]


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
    cases.append(("tables: a mapping that leaves its plane is refused", isinstance(out, ValueError) and "leaves its plane" in str(out), brief(out)))

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
    def cat(cp, category, name="NAME"):
        return f"{cp};{name};{category};0;L;;;;;N;;;;;"

    classes = "\n".join([cat("0030", "Nd"), cat("0031", "Nd"), cat("005F", "Pc"), cat("0300", "Mn"), cat("0903", "Mc"), cat("20DD", "Me"),
                         cat("0660", "Nd"), cat("2160", "Nl"), cat("3400", "Lo", "<CJK Ideograph Extension A, First>"),
                         cat("4DBF", "Lo", "<CJK Ideograph Extension A, Last>"), cat("1D7CE", "Nd"), cat("1D7CF", "Nd"),
                         cat("00B2", "No")])
    cases.append(("categories: Nd's ranges, adjacent ones merged, other categories out",
                  categories(classes, {"Nd"}) == [(0x30, 0x31), (0x660, 0x660), (0x1D7CE, 0x1D7CF)], categories(classes, {"Nd"})))
    cases.append(("categories: a range written as its First and Last lines is taken whole",
                  categories(classes, {"Lo"}) == [(0x3400, 0x4DBF)], categories(classes, {"Lo"})))
    plist = "0009..000D    ; White_Space # Cc\n0020 ; White_Space # Zs\n200C..200D ; Join_Control # Cf\n0085 ; White_Space\n002D ; Dash"
    word_props = "0041..005A ; Alphabetic\n2160 ; Alphabetic # Nl\n0061..007A ; Alphabetic\n0041 ; Cased"
    out = got(lambda: generate(classes, "", word_props, plist, "", floors={}))
    cases.append(("generate: \\w is Alphabetic, the marks, Nd, Pc and Join_Control, merged",
                  not isinstance(out, Exception) and out["RegexWord"] == [0x30, 0x31, 0x41, 0x5A, 0x5F, 0x5F, 0x61, 0x7A, 0x300, 0x300, 0x660, 0x660,
                                                                          0x903, 0x903, 0x200C, 0x200D, 0x20DD, 0x20DD, 0x2160, 0x2160,
                                                                          0x1D7CE, 0x1D7CF], out if isinstance(out, Exception) else out["RegexWord"]))
    cases.append(("generate: \\s is White_Space and \\d is Nd",
                  not isinstance(out, Exception) and out["RegexSpace"] == [0x9, 0xD, 0x20, 0x20, 0x85, 0x85]
                  and out["RegexDigit"] == [0x30, 0x31, 0x660, 0x660, 0x1D7CE, 0x1D7CF], out))
    cases.append(("generate: a name's first character is Alphabetic, and a later one Alphabetic or of Nd, Nl or No",
                  not isinstance(out, Exception) and out["RegexAlpha"] == [0x41, 0x5A, 0x61, 0x7A, 0x2160, 0x2160]
                  and out["RegexNumber"] == [0x30, 0x31, 0xB2, 0xB2, 0x660, 0x660, 0x2160, 0x2160, 0x1D7CE, 0x1D7CF], out))
    # the floor of each table of G4 is held to the table: one above what the fixture gives is refused, one at it passes
    regex_floors = {"RegexDigit": 3, "RegexWord": 11, "RegexSpace": 3, "RegexAlpha": 3, "RegexNumber": 5}
    above = {name: got(lambda: generate(classes, "", word_props, plist, "", floors={name: floor + 1})) for name, floor in regex_floors.items()}
    cases.append(("generate: a table of G4 with one entry fewer than its floor is refused, each of them",
                  all(isinstance(o, ValueError) and name in str(o) for name, o in above.items()), above))
    at = got(lambda: generate(classes, "", word_props, plist, "", floors=regex_floors))
    cases.append(("generate: the tables of G4 at their floors pass", not isinstance(at, Exception), at))
    cases.append(("floors: every table but the two indexes has a floor",
                  all(FLOORS.get(name, 0) > 0 for name in NAMES if not name.endswith("Index")), FLOORS))
    folding = "\n".join(["# a comment", "004B; C; 006B; # K", "212A; C; 006B; # KELVIN SIGN", "00DF; F; 0073 0073; # SHARP S",
                          "1E9E; F; 0073 0073; # CAPITAL SHARP S", "1E9E; S; 00DF; # CAPITAL SHARP S", "0049; C; 0069; # I",
                          "0049; T; 0131; # I, Turkic", "0130; F; 0069 0307; # I WITH DOT ABOVE"])
    cases.append(("folds: the statuses C and S, each set a cycle in ascending order; F and T out",
                  folds(folding) == [(0x49, 0x69), (0x4B, 0x6B), (0x69, 0x49), (0x6B, 0x212A), (0xDF, 0x1E9E), (0x1E9E, 0xDF), (0x212A, 0x4B)],
                  folds(folding)))
    out = got(lambda: generate(data, special, props, plist, folding))
    cases.append(("generate: a table below its floor is refused", isinstance(out, ValueError) and "floor" in str(out), out))
    # the classes of `\p` (G4), from files small enough to read, each line in the form the database writes it; Latin_Sign is made
    # up, a binary property whose short name is the script Latin's, and so is Wordish, a script whose short name is Word_Break's
    p_data = "\n".join([cat("0024", "Sc"), "0028;LEFT PARENTHESIS;Ps;0;ON;;;;;Y;;;;;", cat("0030", "Nd"), cat("0041", "Lu"), cat("0061", "Ll"),
                        cat("00AD", "Cf"), cat("00C0", "Lu"), cat("01C5", "Lt"), cat("0391", "Lu"), cat("03B1", "Ll"),
                        cat("D800", "Cs", "<Non Private Use High Surrogate, First>"), cat("DB7F", "Cs", "<Non Private Use High Surrogate, Last>")])
    p_texts = {
        "PropertyAliases.txt": "# PropertyAliases\ngc ; General_Category\nsc ; Script\nscx ; Script_Extensions\nage ; Age\n"
                               "GCB ; Grapheme_Cluster_Break\nWB ; Word_Break\nSB ; Sentence_Break\nAlpha ; Alphabetic\nLatn ; Latin_Sign\n"
                               "cf ; Case_Folding\nlc ; Lowercase_Mapping\nccc ; Canonical_Combining_Class\nBidi_M ; Bidi_Mirrored\n"
                               "EPres ; Emoji_Presentation",
        "PropertyValueAliases.txt": "gc ; C ; Other\ngc ; Cf ; Format\ngc ; Cn ; Unassigned\ngc ; Cs ; Surrogate\ngc ; L ; Letter\n"
                                    "gc ; LC ; Cased_Letter\ngc ; Ll ; Lowercase_Letter\ngc ; Lt ; Titlecase_Letter\ngc ; Lu ; Uppercase_Letter\n"
                                    "gc ; Nd ; Decimal_Number ; digit\ngc ; Ps ; Open_Punctuation\ngc ; Sc ; Currency_Symbol\nsc ; Grek ; Greek\n"
                                    "sc ; Latn ; Latin\nsc ; WB ; Wordish\nsc ; Zyyy ; Common\nage ; 1.1 ; V1_1\nage ; 2.0 ; V2_0\n"
                                    "WB ; NU ; Numeric\nGCB ; CR ; CR\nSB ; AT ; ATerm\nccc ; 0 ; NR ; Not_Reordered",
        "Scripts.txt": "0024 ; Common # Sc\n0028 ; Common\n0030 ; Common\n0041 ; Latin\n0061 ; Latin\n00C0 ; Wordish\n0391 ; Greek\n03B1 ; Greek",
        "ScriptExtensions.txt": "0030 ; Grek Latn # Nd",
        "DerivedAge.txt": "0391 ; 2.0\n0024..0041 ; 1.1",
        "auxiliary/GraphemeBreakProperty.txt": "000D ; CR",
        "auxiliary/WordBreakProperty.txt": "0030 ; Numeric",
        "auxiliary/SentenceBreakProperty.txt": "002E ; ATerm",
        "emoji/emoji-data.txt": "231A..231B ; Emoji_Presentation",
    }
    p_props, p_list = "0041 ; Alphabetic\n0061 ; Alphabetic\n0391 ; Alphabetic", "0020 ; White_Space\n0024 ; Latin_Sign"
    out = [normalized(x) for x in ("Is_Greek", "IS c", "is", " Gr-e_ek \u00e9", "isc", "ASCII")]
    cases.append(("normalized: a first `is` of any case, ` `, `_`, `-` and the bytes above ASCII dropped, capitals made small, and `is` "
                  "with `c` alone kept as `isc`", out == ["greek", "isc", "", "greek", "isc", "ascii"], out))
    out = [chars([r]) for r in ((0xD000, 0xD900), (0xD800, 0xDFFF), (0xD7FF, 0xE000), (0xDC00, 0xE005))]
    cases.append(("chars: an end inside the surrogates moves out of them, a range of surrogates alone is left out, and one across them "
                  "stays whole", out == [[(0xD000, 0xD7FF)], [], [(0xD7FF, 0xE000)], [(0xE000, 0xE005)]], out))
    p_sets = got(lambda: property_sets(p_data, p_props, p_list, p_texts))
    if isinstance(p_sets, Exception):
        cases.append(("property_sets: the classes of the files above", False, p_sets))
    else:
        p_found, p_kinds = globals()["classes"](*p_sets)  # the function, whose name the fixture of the categories above takes here
        gc, scx, script = p_sets[0]["General_Category"], p_sets[0]["Script_Extensions"], p_sets[0]["Script"]
        cases.append(("property_sets: Other holds the surrogates inside its range across them, Surrogate has no set, Unassigned's last range "
                      "starts past them, and Letter is its categories merged",
                      (0x3B2, TOP) in gc["Other"] and "Surrogate" not in gc and (0xE000, TOP) in gc["Unassigned"]
                      and gc["Letter"] == [(0x41, 0x41), (0x61, 0x61), (0xC0, 0xC0), (0x1C5, 0x1C5), (0x391, 0x391), (0x3B1, 0x3B1)],
                      {k: gc.get(k) for k in ("Other", "Unassigned", "Surrogate", "Letter")}))
        cases.append(("property_sets: a script's extensions are its code points less those ScriptExtensions.txt lists, and those it lists "
                      "for the script", scx.get("Greek") == [(0x30, 0x30), (0x391, 0x391), (0x3B1, 0x3B1)]
                      and scx.get("Common") == [(0x24, 0x24), (0x28, 0x28)] and (0x30, 0x30) in script["Common"], (scx, script["Common"])))
        cases.append(("property_sets: Bidi_Mirrored is UnicodeData.txt's field Y", p_sets[0]["Binary"].get("Bidi_Mirrored") == [(0x28, 0x28)],
                      p_sets[0]["Binary"].get("Bidi_Mirrored")))

        def alone(name):
            return p_found.get(bytes([KIND_ALONE]) + name.encode())

        def by(kind, name):
            return p_found.get(bytes([kind]) + name.encode())

        cases.append(("classes: a name alone is a binary property's before a category's and a script's",
                      alone("latn") == [[(0x24, 0x24)]] and alone("alpha") == alone("alphabetic") == [[(0x41, 0x41), (0x61, 0x61), (0x391, 0x391)]]
                      and alone("greek") == [script["Greek"]] and alone("l") == [gc["Letter"]], (alone("latn"), alone("alpha"), alone("greek"))))
        cases.append(("classes: cf, sc and lc alone are categories, though each names a property",
                      alone("cf") == [[(0xAD, 0xAD)]] and alone("sc") == [[(0x24, 0x24)]] and alone("lc") == [gc["Cased_Letter"]],
                      (alone("cf"), alone("sc"), alone("lc"))))
        cases.append(("classes: the name of a property that is not binary is no class alone, though a script has that name too",
                      alone("wb") is None and alone("gc") is None and by(2, "wb") == [[(0xC0, 0xC0)]], (alone("wb"), alone("gc"), by(2, "wb"))))
        cases.append(("classes: the name of a property read by value keys its values' kind, and another property's name keys none",
                      [p_kinds.get(bytes([KIND_PROPERTY]) + n) for n in (b"gc", b"generalcategory", b"scx", b"age", b"wb", b"ccc", b"alpha")]
                      == [1, 1, 3, 4, 6, None, None], p_kinds))
        v11, v20 = p_sets[0]["Age"]["V1_1"], p_sets[0]["Age"]["V2_0"]
        cases.append(("classes: an age holds the sets of every version up to it, in their order",
                      by(4, "2.0") == by(4, "v20") == [v11, v20] and by(4, "1.1") == [v11], (by(4, "2.0"), v11, v20)))
        cases.append(("classes: a value is keyed by every alias under its property's kind, Script_Extensions' by the scripts' names",
                      by(3, "grek") == [scx["Greek"]] and by(2, "grek") == [script["Greek"]] and by(1, "digit") == by(1, "nd") == [[(0x30, 0x30)]]
                      and by(6, "nu") == [[(0x30, 0x30)]], sorted(k for k in p_found if k[0] != KIND_ALONE)))

        def offsets(data):  # {key: (the offset of its first set, its number of sets)}, the keys' part of a stream as `decode` reads it
            data, at = bytes(data), 0

            def read():
                nonlocal at
                n, shift = 0, 0
                while True:
                    at += 1
                    n |= (data[at - 1] & 0x7F) << shift
                    shift += 7
                    if data[at - 1] < 0x80:
                        return n

            end = read()
            end += at
            out = {}
            while at < end:
                key = data[at + 1:at + 1 + data[at]]
                at += 1 + data[at]
                offset = read()
                out[key] = (offset, data[at])
                at += 1
            return out

        stream = got(lambda: encode(*p_sets))
        decoded = got(lambda: decode(stream))
        if isinstance(decoded, Exception):
            cases.append(("encode and decode: the stream of the classes reads back", False, decoded))
        else:
            cases.append(("encode and decode: every key reads back as the union of its sets, and a property's name as its values' kind",
                          decoded.get(bytes([4]) + b"2.0") == tuple(merge(v11 + v20)) and decoded.get(bytes([KIND_ALONE]) + b"latn") == ((0x24, 0x24),)
                          and decoded.get(bytes([KIND_PROPERTY]) + b"sc") == ("kind", 2) and len(decoded) == len(p_found) + len(p_kinds),
                          {k: v for k, v in decoded.items() if k[0] in (4, KIND_PROPERTY)}))
            at = offsets(stream)
            cases.append(("encode: a set is stored once, its keys pointing at it, and an age's key at the first age",
                          at[bytes([KIND_ALONE]) + b"greek"] == at[bytes([2]) + b"greek"] and at[bytes([2]) + b"greek"][1] == 1
                          and at[bytes([4]) + b"2.0"] == (0, 2) and at[bytes([4]) + b"1.1"] == (0, 1), at))
            more = got(lambda: encode(*property_sets(p_data, p_props, p_list, {**p_texts, "Scripts.txt": p_texts["Scripts.txt"] + "\n0300 ; Greek"})))
            said = got(lambda: changes({"RegexProperties": stream}, {"RegexProperties": more}))
            cases.append(("changes: a class that changed is named with its ranges before and after",
                          isinstance(said, list) and "class 2:greek: 2 ranges -> 3 ranges" in said, said))
        alike = got(lambda: decode(encode(*property_sets(p_data, p_props, p_list, {**p_texts, "DerivedAge.txt": "0024 ; 1.1\n0024 ; 2.0"}))))
        cases.append(("encode: ages alike are each stored, so that an age reads the sets of its own versions",
                      isinstance(alike, dict) and alike.get(bytes([4]) + b"2.0") == ((0x24, 0x24),),
                      alike.get(bytes([4]) + b"2.0") if isinstance(alike, dict) else alike))
        name = "X" * 300
        out = got(lambda: encode(*property_sets(p_data, p_props, p_list, {**p_texts, "Scripts.txt": p_texts["Scripts.txt"] + f"\n00C0 ; {name}",
                                                                           "PropertyValueAliases.txt": p_texts["PropertyValueAliases.txt"] + f"\nsc ; Long ; {name}"})))
        cases.append(("encode: a key longer than one byte counts is refused", isinstance(out, ValueError) and "longer than one byte" in str(out), out))
    # a refusal of the tables, as a mutation of a case above makes, fails the two cases below and prints every case
    arrays = got(lambda: generate(data, special, props, plist, folding, p_texts, floors={}))
    if isinstance(arrays, Exception):
        cases.append(("render and parse: the written file reads back as the arrays", False, arrays))
        cases.append(("changes: a changed mapping is named with both values", False, arrays))
    else:
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

        if isinstance(arrays, Exception):
            cases.append(("main: the runs of main over the tables of the cases above", False, arrays))
        else:
            texts = {"UnicodeData.txt": data, "SpecialCasing.txt": special, "DerivedCoreProperties.txt": props, "PropList.txt": plist,
                     "CaseFolding.txt": folding, **p_texts}
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
    good = [[], ["--check"], ["--self-test"]]
    odd = [["--help"], ["-h"], ["--chek"], ["tables"], ["--check", "--check"], ["--check", "--self-test"]]
    cases.append(("call: no argument, --check and --self-test are its calls, each alone; --help, -h, a mistyped --check, a "
                  "positional argument and two calls together are not, so none of them writes the tables",
                  [calls.understood(a, CALLS) for a in good] == list(CALLS) and not any(calls.understood(a, CALLS) for a in odd),
                  ([calls.understood(a, CALLS) for a in good], [a for a in odd if calls.understood(a, CALLS)])))
    return kit.report(cases)


# the calls this tool takes, each alone; any other is refused before anything runs (tools/calls.py)
CALLS = {"write": (), "check": ("--check",), "self-test": ("--self-test",)}


if __name__ == "__main__":
    if calls.refused(sys.argv[1:], CALLS, "tools/unicode_tables.py"):
        sys.exit(calls.REFUSED)
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit(main(sys.argv[1:]))
