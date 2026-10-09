#!/usr/bin/env python3
"""Lint for the knowledge store: knowledge/, guides/ and INDEX.md. Fails on: an INDEX link to a
missing file, a store file no INDEX line links to, a path of a store file that does not exist,
written in an entry, in CLAUDE.md, in the review rules or in a skill, a link in a store file (inline
or reference-style, to a file of any kind, read outside code spans and fences and percent-decoded)
that, resolved from that file as GitHub resolves it and in its exact spelling, reaches no entry or no
file or leaves the repository,
an INDEX line without a hook after its em dash,
an entry whose frontmatter lacks a non-empty name, description or metadata.type, a nested or
non-.md file under knowledge/ or guides/, an entry tagged LIVING without a mermaid block or an
"Update triggers" section, a LIVING tag that is not the bare word, a scan that found fewer than
FLOOR entries, and a guide whose numbers expired: `ttl_days:` that is not a whole number or comes
without `generated:`, a `generated:` that is not a date, a TTL over TTL_CAP, a `generated:` date
more than one day ahead, or a TTL that ran out. An entry with generated blocks (the evidence
guide) fails on an empty block, on blocks that are not what its `digest:` line says (a hand edit,
or a page the tool never wrote), and on a missing `ttl_days:`.
The real tree is also checked for what the rules above are satisfied without: CLAUDE.md, a skill
file in every skill directory and at least one skill, LIVING_FLOOR living entries, and the
evidence guide with its blocks. Reports without failing: [[wikilinks]] that resolve to nothing,
the days a TTL that has not run out has left, and a TTL in its last week. Credentials, machine-bound strings and
conflict markers are the tree gate's job (tools/tree_gate.py), for the whole tree.
`--self-test` plants one case per rule, plus precision cases that must stay clean."""
import datetime
import hashlib
import os
import posixpath
import re
import sys
import tempfile
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

# Every other rule is satisfied by finding nothing: a scan of the wrong tree prints "0 errors".
# Keep the floor well below the real entry count and well above zero; raise it as the store grows.
FLOOR = 5
LIVING_FLOOR = 4  # the living diagrams: a rewrite of INDEX.md that drops the tag switches their rule off
TTL_CAP = 90
EVIDENCE = "guides/quality-bar-evidence.md"  # the entry whose numbers are generated
BLOCK = re.compile(r"<!-- generated:([\w-]+) -->\n(.*?)<!-- /generated:\1 -->", re.S)
STORE = ("knowledge", "guides")
# A value that is really there: not empty, and not an empty YAML scalar ("", '', ~, null).
VALUE = r"(?![ \t]*$)(?!(?:\"\"|''|~|null)[ \t]*$)"
# `type:` indented under `metadata:`, with a value; other metadata keys may come first.
META_TYPE = re.compile(r"^metadata:[ \t]*\n(?:[ \t]+\S.*\n)*?[ \t]+type:[ \t]*" + VALUE + r"\S", re.M)
LINK = r"\]\(((?:knowledge|guides)/[^)#]+\.md)(?:#[^)]*)?\)"  # an #anchor is still a link
# The path of a store file, as prose, rules and skills write it: in a link or in backticks.
REFERENCE = re.compile(r"(?<![\w/.-])((?:knowledge|guides)/[A-Za-z0-9._-]+\.md)")
# A link as a store file writes it, relative to itself or from the root with a "/": inline, `[text](target)`,
# with a title in double quotes, single quotes or parentheses, or with the target in angle brackets (a
# target with a space); or reference-style, `[name]: target`, where a footnote `[^1]:` is no link. The
# target is read percent-decoded, as GitHub reads it.
INLINE = re.compile(r"\]\((?:<([^>\n]*)>|([^)\s]+))(?:[ \t]+(?:\"[^\"]*\"|'[^']*'|\([^)]*\)))?\)")
DEFINED = re.compile(r"^[ \t]{0,3}\[(?!\^)[^\]]+\]:[ \t]*(\S+)", re.M)
# code spans of one or two backticks and fences of backticks or tildes: GitHub shows a link there as text.
# An indented code block is not taken out: a link shape there is read as a link.
CODE = re.compile(r"```.*?```|~~~.*?~~~|``[^`\n].*?``|`[^`\n]*`", re.S)
URL = re.compile(r"[A-Za-z][A-Za-z0-9+.-]*:")  # https:, mailto: and the like: not a file of this tree


def expiry(rel, fm, today):
    """(errors, warnings) for one entry's ttl_days/generated pair. Entries without ttl_days have none."""
    ttl = re.search(r"^ttl_days:[ \t]*(\d+)[ \t]*$", fm, re.M)
    if not ttl:
        return ([f"{rel}: ttl_days is not a whole number"] if re.search(r"^ttl_days:", fm, re.M) else []), []
    days = int(ttl.group(1))
    generated = re.search(r"^generated:[ \t]*(\d{4}-\d{2}-\d{2})[ \t]*$", fm, re.M)
    if not generated:
        return [f"{rel}: ttl_days without a generated: YYYY-MM-DD date"], []
    try:
        made = datetime.date.fromisoformat(generated.group(1))
    except ValueError:
        return [f"{rel}: generated: is not a date"], []
    errors = []
    if days > TTL_CAP:
        errors.append(f"{rel}: ttl_days {days} is over the cap of {TTL_CAP}")
    if (made - today).days > 1:  # one day of slack for a local clock ahead of the CI runner's
        errors.append(f"{rel}: generated: {made} is in the future")
    left = days - (today - made).days
    if left <= 0:
        errors.append(f"{rel}: numbers expired {-left} days ago: regenerate them (tools/quality_evidence.py)")
        return errors, []
    return errors, [f"{rel}: numbers expire in {left} days" + (": regenerate soon" if left <= 7 else "")]


def exists_exactly(root, path):
    """True when `path` is under `root` in exactly this spelling, part by part. GitHub resolves a link
    case-sensitively, the file systems of Windows and macOS do not, so os.path.isfile would pass a
    link here that is broken on the page."""
    here = root
    for part in path.split("/"):
        if not os.path.isdir(here) or part not in os.listdir(here):
            return False
        here = os.path.join(here, part)
    return True


def link_errors(root, rel, body, files):
    """Errors for the links of one store file, resolved as GitHub resolves them: from the file's own
    directory, or from the root when the target starts with "/", percent-decoded, in the exact
    spelling, and read outside code spans and fences, where GitHub shows a link as text (INLINE,
    DEFINED and CODE say which shapes). REFERENCE reads only the spelling from the root, which the
    rules and the skills use; inside the store a link is written relative to its file. A link into
    the store must reach an entry or the store directory itself, a link out of it a file or a
    directory, the root included."""
    errors, prose = [], CODE.sub("", body)
    inline = [bracketed or bare for bracketed, bare in INLINE.findall(prose)]
    for raw in sorted(set(inline) | set(DEFINED.findall(prose))):
        written = raw.split("#", 1)[0]  # the target as the file spells it, without the anchor
        target = urllib.parse.unquote(written)
        if not target or URL.match(target):
            continue  # an anchor of this page, or https:, mailto: and the like
        rooted = target.startswith("/")
        path = posixpath.normpath(target.lstrip("/") if rooted else posixpath.join(posixpath.dirname(rel), target))
        if path == "." or path in STORE:
            continue  # the root, or a store directory itself: GitHub lists a directory
        if path == ".." or path.startswith("../"):
            errors.append(f"{rel} links {written}, which leaves the repository")
        elif path.split("/")[0] in STORE:
            if path not in files:
                errors.append(f"{rel} links {written}, which resolves to {path}: no entry of the store")
        elif not exists_exactly(root, path):
            errors.append(f"{rel} links {written}, which resolves to {path}: no such file in this spelling")
    return errors


def digest(text):
    """What the generated blocks of an entry hold, as one hash: each block's name and text, in order,
    after the `generated:` date and the `ttl_days:` that time them. A date moved by hand, or a
    longer TTL, then fails the digest like an edited number would."""
    h = hashlib.sha256()
    for key in ("generated", "ttl_days"):
        m = re.search(rf"^{key}:[ \t]*(\S*)", text, re.M)
        h.update(f"{key}:{m.group(1) if m else ''}\n".encode())
    for m in BLOCK.finditer(text):
        h.update(f"{m.group(1)}\n{m.group(2)}\n".encode())
    return h.hexdigest()


def generated(rel, body, fm):
    """Errors for the generated blocks of one entry. tools/quality_evidence.py stamps their digest
    beside the date, so a block that was edited by hand, or a date on a page that was never
    generated, does not pass for the tool's output."""
    blocks = BLOCK.findall(body)
    if not blocks:
        return []
    errors = [f"{rel}: the generated block {name} is empty: nothing generated it (tools/quality_evidence.py)"
              for name, inner in blocks if not inner.strip()]
    stamped = re.search(r"^digest:[ \t]*([0-9a-f]{64})[ \t]*$", fm, re.M)
    if not stamped or stamped.group(1) != digest(body):
        errors.append(f"{rel}: the generated blocks are not what the digest: line says: edited by hand, or never written by "
                      f"tools/quality_evidence.py")
    if not re.search(r"^ttl_days:", fm, re.M):
        errors.append(f"{rel}: generated numbers without ttl_days: they would never expire")
    return errors


def lint(root, floor=FLOOR, today=None, whole=True):
    """(errors, warnings). `whole` also demands what only the real tree has; a self-test tree that
    is built for one rule passes False."""
    today = today or datetime.date.today()
    errors, warnings = [], []
    index = kit.read(os.path.join(root, "INDEX.md"))
    linked = set(re.findall(LINK, index))
    files = set()
    for store in STORE:
        sdir = os.path.join(root, store)
        # Walk the whole tree: a nested or non-.md file would never be linted, so it is an error itself.
        found = {os.path.relpath(os.path.join(d, f), sdir).replace(os.sep, "/")
                 for d, _, fs in os.walk(sdir) for f in fs}
        flat = {f for f in found if f.endswith(".md") and "/" not in f}
        errors += [f"{store}/{f}: nested or not .md - the lint reads {store}/*.md only" for f in sorted(found - flat)]
        files |= {f"{store}/{f}" for f in flat}
    errors += [f"INDEX.md links missing file: {t}" for t in sorted(linked - files)]
    errors += [f"{f} has no INDEX.md line" for f in sorted(files - linked)]
    if len(files) < floor:
        errors.append(f"only {len(files)} entries found, floor is {floor}: is this the right tree?")
    living = set()
    for line in index.splitlines():
        m = re.search(LINK, line)
        if not m:
            continue
        tags, dash, hook = line[m.end():].partition("—")
        if not dash or not hook.strip():
            errors.append(f"INDEX.md: the line for {m.group(1)} has no hook after an em dash")
        if "LIVING" in tags.split():
            living.add(m.group(1))
        elif "LIVING" in tags:
            errors.append(f"INDEX.md: the line for {m.group(1)} carries LIVING inside other characters: the tag is the bare word")

    # Who else names store files: the rules file, the review rules and the skills. The index has its own rule above.
    skills = os.path.join(root, ".claude", "skills")
    skill_files = [f".claude/skills/{d}/SKILL.md" for d in (sorted(os.listdir(skills)) if os.path.isdir(skills) else [])]
    readers = ["CLAUDE.md", ".review/review-rules.yaml"] + skill_files
    present = [r for r in readers if os.path.isfile(os.path.join(root, r))]
    if whole:
        errors += [f"{r} is missing: it names store files and is checked with them" for r in readers if r not in present]
        if not skill_files:
            errors.append("no skill under .claude/skills: is this the right tree?")
        if len(living) < LIVING_FLOOR:
            errors.append(f"only {len(living)} entries tagged LIVING in INDEX.md, floor is {LIVING_FLOOR}")
        if EVIDENCE not in files or not BLOCK.search(kit.read(os.path.join(root, EVIDENCE))):
            errors.append(f"{EVIDENCE} is missing or has no generated block: the numbers of the bar would be nowhere, or would never expire")
    for rel in sorted(files) + present:
        for ref in sorted(set(REFERENCE.findall(kit.read(os.path.join(root, rel)))) - files):
            errors.append(f"{rel} names {ref}, which does not exist")

    names, links = set(), []
    for rel in sorted(files):
        body = kit.read(os.path.join(root, rel))
        errors += link_errors(root, rel, body, files)
        m = re.match(r"---\n(.*?)\n---\n", body, re.S)
        fm = m.group(1) if m else ""
        # [ \t]*, not \s*: an empty `name:` must not borrow the next line's key as its value.
        name = re.search(r"^name:[ \t]*" + VALUE + r"(\S+)", fm, re.M)
        if not name or not re.search(r"^description:[ \t]*" + VALUE + r"\S", fm, re.M) or not META_TYPE.search(fm):
            errors.append(f"{rel}: missing frontmatter name/description/metadata.type")
        else:
            names.add(name.group(1))
        if rel in living and ("```mermaid" not in body or not re.search(r"^## Update triggers[ \t]*$", body, re.M)):
            errors.append(f"{rel}: tagged LIVING but has no mermaid block or no '## Update triggers' section")
        e, w = expiry(rel, fm, today)
        errors, warnings = errors + e + generated(rel, body, fm), warnings + w
        links += [(rel, link) for link in re.findall(r"\[\[([^\]]+)\]\]", body)]
    warnings += [f"{rel}: [[{link}]] resolves to no entry" for rel, link in links if link not in names]
    return errors, warnings


def self_test():
    """Each rule against a tree built to break it. A rule that never fired here is not a gate."""
    with kit.scratch("lint-knowledge-") as base:  # a removal that fails is said, not raised
        return cases_in(base)


def cases_in(base):
    """The cases of self_test, each tree a directory under `base`; `base` also holds a file beside
    the trees, for the link that leaves its repository."""
    with open(os.path.join(base, "outside.md"), "w", encoding="utf-8") as handle:
        handle.write("beside every tree\n")

    def tree(entries, index=None):
        root = tempfile.mkdtemp(dir=base)
        for store in STORE:
            os.mkdir(os.path.join(root, store))
        for rel, body in entries.items():
            os.makedirs(os.path.dirname(os.path.join(root, rel)), exist_ok=True)
            with open(os.path.join(root, rel), "w", encoding="utf-8") as f:
                f.write(body)
        lines = index if index is not None else [f"- [{rel}]({rel}) — hook" for rel in entries]
        with open(os.path.join(root, "INDEX.md"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        return root

    def ok(n, extra=""):
        return f"---\nname: e{n}\ndescription: d\n{extra}metadata:\n  type: project\n---\n\nfact\n"

    today = datetime.date(2026, 10, 1)
    good = {f"knowledge/e{n}.md": ok(n) for n in range(3)}
    good_index = [f"- [{rel}]({rel}) — hook" for rel in good]
    k = "knowledge/"
    diagram = ok(7) + "```mermaid\nflowchart TD\n  A --> B\n```\n\n## Update triggers\n\n- a file\n"

    def evidence(inner="| a | 1 |\n", stamp=None, ttl="ttl_days: 30\ngenerated: 2026-09-30\n", nested=""):
        """A guide with one generated block, stamped with the digest of the page unless told otherwise."""
        body = f"\n<!-- generated:a -->\n{inner}<!-- /generated:a -->\n"
        page = lambda d: f"---\nname: ev\ndescription: d\n{ttl}digest: {d}\nmetadata:\n{nested}  type: reference\n---\n" + body
        return page(stamp or digest(page("0" * 64)))

    # a tree with everything the real one has: the rules file, a skill, four living diagrams, the evidence guide
    living = {f"{k}d{n}.md": diagram.replace("name: e7", f"name: d{n}") for n in range(4)}
    whole = {**good, **living, EVIDENCE: evidence(), "CLAUDE.md": "rules\n", ".claude/skills/walk/SKILL.md": "a skill\n",
             ".review/review-rules.yaml": "review rules\n"}
    whole_index = good_index + [f"- [{rel}]({rel}) LIVING — hook" for rel in living] + [f"- [ev]({EVIDENCE}) — hook"]
    without = lambda *gone: {rel: body for rel, body in whole.items() if rel not in gone}
    real = {"whole": True}
    cases = {
        "clean tree passes": (tree(good), None),
        "floor catches an empty scan": (tree({}, index=[]), "floor"),
        "missing file": (tree(good, index=["- [x](knowledge/gone.md) — hook"] + good_index), "missing file"),
        "anchored link to a missing file": (tree(good, index=["- [x](knowledge/gone.md#part) — hook"] + good_index),
                                            "missing file"),
        "anchored link counts as the entry's line": (
            tree(good, index=[line.replace(") —", "#part) —") for line in good_index]), None),
        "unindexed file": (tree(good, index=[]), "no INDEX.md line"),
        "unindexed guide": (tree({**good, "guides/g.md": ok(5)}, index=good_index), "guides/g.md has no INDEX.md line"),
        "indexed guide is clean": (tree({**good, "guides/g.md": ok(5)}), None),
        "nested entry": (tree({**good, k + "sub/x.md": ok(5)}), "nested or not .md"),
        "nested guide": (tree({**good, "guides/sub/x.md": ok(5)}), "nested or not .md"),
        "file that is not .md": (tree({**good, k + "notes.txt": "x\n"}, index=good_index), "nested or not .md"),
        "INDEX line without a hook": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md)"]), "no hook"),
        "INDEX line with an empty hook": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md) —  "]), "no hook"),
        "no frontmatter": (tree({**good, k + "bad.md": "no frontmatter\n"}), "frontmatter"),
        "empty name: does not borrow the next key": (
            tree({**good, k + "n.md": "---\nname:\ndescription: d\nmetadata:\n  type: project\n---\n"}), "frontmatter"),
        "empty description:": (tree({**good, k + "d.md": "---\nname: d\ndescription:\nmetadata:\n  type: project\n---\n"}),
                               "frontmatter"),
        "no metadata.type": (tree({**good, k + "t.md": "---\nname: t\ndescription: d\n---\n"}), "frontmatter"),
        "empty type: does not borrow the next key": (
            tree({**good, k + "u.md": "---\nname: u\ndescription: d\nmetadata:\n  type:\n  local_reason: x\n---\n"}),
            "frontmatter"),
        'description: "" is empty': (
            tree({**good, k + "q.md": '---\nname: q\ndescription: ""\nmetadata:\n  type: project\n---\n'}), "frontmatter"),
        'name: "" is empty': (
            tree({**good, k + "nq.md": '---\nname: ""\ndescription: d\nmetadata:\n  type: project\n---\n'}), "frontmatter"),
        "type: null is empty": (
            tree({**good, k + "tn.md": "---\nname: tn\ndescription: d\nmetadata:\n  type: null\n---\n"}), "frontmatter"),
        "type: after another metadata key": (
            tree({**good, k + "v.md": "---\nname: v\ndescription: d\nmetadata:\n  local_reason: x\n  type: user\n---\n"}), None),
        "LIVING without a diagram": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md) LIVING — hook"]),
                                     "tagged LIVING"),
        "LIVING without update triggers": (
            tree({**good, k + "l.md": ok(7) + "```mermaid\nflowchart TD\n```\n"},
                 index=good_index + ["- [l](knowledge/l.md) LIVING — hook"]), "tagged LIVING"),
        "LIVING with update triggers but no diagram": (
            tree({**good, k + "l.md": diagram.replace("```mermaid\nflowchart TD\n  A --> B\n```\n", "")},
                 index=good_index + ["- [l](knowledge/l.md) LIVING — hook"]), "tagged LIVING"),
        "LIVING with both is clean": (tree({**good, k + "l.md": diagram},
                                           index=good_index + ["- [l](knowledge/l.md) LIVING — hook"]), None),
        "the word LIVING in a hook is not the tag": (tree(good, index=good_index[:2] + [
            "- [e2](knowledge/e2.md) — a LIVING thing"]), None),
        "ttl without generated": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\n")}), "without a generated"),
        "ttl that is not a number": (tree({**good, "guides/g.md": ok(5, "ttl_days: soon\ngenerated: 2026-10-01\n")}),
                                     "not a whole number"),
        "ttl over the cap": (tree({**good, "guides/g.md": ok(5, "ttl_days: 91\ngenerated: 2026-10-01\n")}), "over the cap of 90"),
        "ttl at the cap is clean": (tree({**good, "guides/g.md": ok(5, "ttl_days: 90\ngenerated: 2026-10-01\n")}), "warn:expire in 90 days"),
        "generated in the future": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-10-03\n")}),
                                    "in the future"),
        "one day ahead is clock slack": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-10-02\n")}),
                                         "warn:expire in 31 days"),
        "expired on the expiry day": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-09-01\n")}),
                                      "expired 0 days ago"),
        "last week warns": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-09-05\n")}),
                            "warn:expire in 4 days: regenerate soon"),
        "fresh numbers report their days": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-09-30\n")}),
                                            "warn:expire in 29 days"),
        # A warning must fire too, and must NOT fail the run: "warn:" checks the warnings instead.
        "dangling wikilink warns": (tree({**good, k + "w.md": ok(7) + "see [[nowhere]]\n"}), "warn:resolves to no entry"),
        "resolved wikilink is silent": (tree({**good, k + "r.md": ok(4) + "see [[e0]]\n"}), None),
        "an entry names a store file that is not there": (tree({**good, k + "r.md": ok(4) + "see `knowledge/gone.md`\n"}),
                                                          "knowledge/r.md names knowledge/gone.md"),
        "the rules file names a store file that is not there": (
            tree({**good, "CLAUDE.md": "read [it](guides/gone.md)\n"}, index=good_index), "CLAUDE.md names guides/gone.md"),
        "a skill names a store file that is not there": (
            tree({**good, ".claude/skills/walk/SKILL.md": "the picture is `knowledge/gone.md`\n"}, index=good_index),
            ".claude/skills/walk/SKILL.md names knowledge/gone.md"),
        "a link relative to its entry that leads nowhere": (
            tree({**good, k + "r.md": ok(4) + "see [it](gone.md)\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a link rooted at / that leads nowhere": (
            tree({**good, k + "r.md": ok(4) + "see [it](/guides/gone.md#part)\n"}),
            "knowledge/r.md links /guides/gone.md, which resolves to guides/gone.md: no entry of the store"),
        "a link to a nested file reaches no entry": (
            tree({**good, k + "sub/x.md": ok(5), k + "r.md": ok(4) + "see [it](sub/x.md)\n"}),
            "knowledge/r.md links sub/x.md, which resolves to knowledge/sub/x.md: no entry of the store"),
        "a link from a guide up to a root file that is not there": (
            tree({**good, "guides/g.md": ok(5) + "see [the rules](../CLAUDE.md)\n"}),
            "guides/g.md links ../CLAUDE.md, which resolves to CLAUDE.md: no such file in this spelling"),
        "a link to a file that is not Markdown, which is not there": (
            tree({**good, k + "r.md": ok(4) + "see [it](../tools/gone.py)\n"}),
            "knowledge/r.md links ../tools/gone.py, which resolves to tools/gone.py: no such file in this spelling"),
        "a link with a title is a link": (
            tree({**good, k + "r.md": ok(4) + "see [it](gone.md \"the title\")\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a link with a single-quoted title is a link": (
            tree({**good, k + "r.md": ok(4) + "see [it](gone.md 'the title')\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a link with a parenthesized title is a link": (
            tree({**good, k + "r.md": ok(4) + "see [it](gone.md (the title))\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a target in angle brackets, with a space, resolves": (
            tree({**good, k + "r.md": ok(4) + "see [it](<../tools/my note.txt>)\n", "tools/my note.txt": "x\n"}), None),
        "a target in angle brackets that leads nowhere is refused": (
            tree({**good, k + "r.md": ok(4) + "see [it](<gone.md>)\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a footnote definition is no link": (
            tree({**good, k + "r.md": ok(4) + "a claim[^1]\n\n[^1]: The forge says so\n"}), None),
        "a link to the root or to a store directory itself reaches a directory GitHub lists": (
            tree({**good, k + "r.md": ok(4) + "see [up](../), [here](./) and [root](/)\n"}), None),
        "a link shape in a tilde fence or in a two-backtick span is text, not a link": (
            tree({**good, k + "r.md": ok(4) + "write ``[it](gone.md)`` as in\n\n~~~\n[it](gone.md)\n~~~\n"}), None),
        "a reference-style link is a link": (
            tree({**good, k + "r.md": ok(4) + "see [it][1]\n\n[1]: gone.md\n"}),
            "knowledge/r.md links gone.md, which resolves to knowledge/gone.md: no entry of the store"),
        "a link spelled in another case than the file is broken on the page, whatever the file system says": (
            tree({**good, k + "r.md": ok(4) + "see [it](../tools/X.py)\n", "tools/x.py": "code\n"}),
            "knowledge/r.md links ../tools/X.py, which resolves to tools/X.py: no such file in this spelling"),
        "a link shape inside a code span or a fence is text, not a link": (
            tree({**good, k + "r.md": ok(4) + "write `[it](gone.md)`, as in\n\n```text\n[it](gone.md)\n```\n"}), None),
        "a percent-encoded target is decoded before the lookup": (
            tree({**good, k + "r.md": ok(4) + "see [it](../tools/my%20note.txt) and [that](e%30.md)\n", "tools/my note.txt": "x\n"}), None),
        # base/outside.md is there, so the link reaches a file; what refuses it is the rule on leaving the
        # repository, and the message checked is that rule's (the lookup would say "no such file")
        "a link that leaves the repository": (
            tree({**good, k + "r.md": ok(4) + "see [it](../../outside.md)\n"}),
            "knowledge/r.md links ../../outside.md, which leaves the repository"),
        "relative links that resolve are clean": (
            tree({**good, k + "r.md": ok(4) + "see [a](e0.md#part), [b](../guides/g.md), [c](../CLAUDE.md), [d](/knowledge/e1.md),"
                  " [e](./e2.md) and [web](https://example.com/x.md)\n", "guides/g.md": ok(5), "CLAUDE.md": "rules\n"},
                 index=good_index + ["- [r](knowledge/r.md) — hook", "- [g](guides/g.md) — hook"]), None),
        "naming store files that exist is clean": (
            tree({**good, k + "r.md": ok(4) + "see knowledge/e0.md\n", "CLAUDE.md": "read `knowledge/e1.md`\n",
                  ".claude/skills/walk/SKILL.md": "and knowledge/e2.md\n"}, index=good_index + ["- [r](knowledge/r.md) — hook"]), None),
        "generated: that is not a date": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-02-30\n")}), "is not a date"),
        "a styled LIVING tag is refused, not dropped": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md) `LIVING` — hook"]),
                                                        "the tag is the bare word"),
        "generated blocks with their digest are clean": (tree({**good, "guides/g.md": evidence()}), "warn:expire in 29 days"),
        "an empty generated block": (tree({**good, "guides/g.md": evidence(inner="")}), "generated block a is empty"),
        "a generated block edited by hand": (tree({**good, "guides/g.md": evidence(stamp="0" * 64)}), "not what the digest"),
        "a number edited under a valid stamp": (
            tree({**good, "guides/g.md": evidence().replace("| a | 1 |", "| a | 2 |")}), "not what the digest"),
        "a generated date moved by hand under a valid stamp": (
            tree({**good, "guides/g.md": evidence().replace("generated: 2026-09-30", "generated: 2026-10-01")}), "not what the digest"),
        "a ttl raised by hand under a valid stamp": (
            tree({**good, "guides/g.md": evidence().replace("ttl_days: 30", "ttl_days: 60")}), "not what the digest"),
        "generated numbers without ttl_days": (tree({**good, "guides/g.md": evidence(ttl="")}), "would never expire"),
        "ttl_days nested under metadata does not count": (
            tree({**good, "guides/g.md": evidence(ttl="", nested="  ttl_days: 30\n")}), "would never expire"),
        "four entries are below the real floor": (tree({**good, k + "e3.md": ok(3)}), "floor is 5", {"floor": FLOOR}),
        "whole: a tree with everything the real one has is clean": (tree(whole, index=whole_index), "warn:expire in 29 days", real),
        "whole: without the rules file": (tree(without("CLAUDE.md"), index=whole_index), "CLAUDE.md is missing", real),
        "whole: without the review rules": (
            tree(without(".review/review-rules.yaml"), index=whole_index), ".review/review-rules.yaml is missing", real),
        "whole: the review rules name a store file that is not there": (
            tree({**whole, ".review/review-rules.yaml": "read knowledge/gone.md\n"}, index=whole_index),
            ".review/review-rules.yaml names knowledge/gone.md", real),
        "whole: without a skill": (tree(without(".claude/skills/walk/SKILL.md"), index=whole_index), "no skill under", real),
        "whole: a skill directory without its SKILL.md": (
            tree({**whole, ".claude/skills/other/notes.md": "no skill file\n"}, index=whole_index), ".claude/skills/other/SKILL.md is missing", real),
        "whole: three living entries are below the floor": (
            tree(whole, index=[line.replace("d3.md) LIVING", "d3.md)") for line in whole_index]), "tagged LIVING in INDEX.md, floor is 4", real),
        "whole: without the evidence guide": (
            tree(without(EVIDENCE), index=whole_index[:-1]), "is missing or has no generated block", real),
        "whole: an evidence guide without a generated block": (
            tree({**whole, EVIDENCE: ok(9, "ttl_days: 30\ngenerated: 2026-09-30\n")}, index=whole_index), "is missing or has no generated block", real),
    }
    results = []
    for label, (root, needle, *how) in cases.items():
        errors, warnings = lint(root, **{"floor": 3, "today": today, "whole": False, **(how[0] if how else {})})
        if needle and needle.startswith("warn:"):
            hit = not errors and any(needle[5:] in w for w in warnings)
        else:
            hit = any(needle in e for e in errors) if needle else not errors and not warnings
        results.append((label, hit, f"errors={errors} warnings={warnings}"))
    errors, warnings = lint(tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-08-20\n")}), floor=3, today=today,
                            whole=False)
    results.append(("an expired page gets no 'expire in' warning",
                    any("expired 12 days ago" in e for e in errors) and not any("expire in" in w for w in warnings),
                    f"errors={errors} warnings={warnings}"))
    errors, _ = lint(tree(good), today=today)
    results.append(("left to its defaults the lint demands the whole tree and the real floor",
                    any("CLAUDE.md is missing" in e for e in errors) and any("floor is 5" in e for e in errors), errors))
    return kit.report(results)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    try:
        errors, warnings = lint(kit.ROOT)
    except OSError as e:
        print(f"ERROR: knowledge lint could not run: {e}")
        sys.exit(1)
    for w in warnings:
        print("warn:", w)
    for e in errors:
        print("ERROR:", e)
    print(f"knowledge lint: {len(errors)} errors, {len(warnings)} warnings")
    sys.exit(1 if errors else 0)
