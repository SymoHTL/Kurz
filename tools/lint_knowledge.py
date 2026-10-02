#!/usr/bin/env python3
"""Lint for the knowledge store: knowledge/, guides/ and INDEX.md. Fails on: an INDEX link to a
missing file, a store file no INDEX line links to, a path of a store file that does not exist,
written in an entry, in CLAUDE.md or in a skill, an INDEX line without a hook after its em dash,
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
the days every TTL has left, and a TTL in its last week. Credentials, machine-bound strings and
conflict markers are the tree gate's job (tools/tree_gate.py), for the whole tree.
`--self-test` plants one case per rule, plus precision cases that must stay clean."""
import datetime
import hashlib
import os
import re
import sys
import tempfile

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
    return errors, [f"{rel}: numbers expire in {left} days" + (": regenerate soon" if left <= 7 else "")]


def digest(text):
    """What the generated blocks of an entry hold, as one hash: each block's name and text, in order."""
    h = hashlib.sha256()
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

    # Who else names store files: the rules file and the skills. The index has its own rule above.
    skills = os.path.join(root, ".claude", "skills")
    readers = ["CLAUDE.md"] + [f".claude/skills/{d}/SKILL.md" for d in (sorted(os.listdir(skills)) if os.path.isdir(skills) else [])]
    present = [r for r in readers if os.path.isfile(os.path.join(root, r))]
    if whole:
        errors += [f"{r} is missing: it names store files and is checked with them" for r in readers if r not in present]
        if len(readers) < 2:
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
    def tree(entries, index=None):
        root = tempfile.mkdtemp()
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
        """A guide with one generated block, stamped with the digest of that block unless told otherwise."""
        body = f"\n<!-- generated:a -->\n{inner}<!-- /generated:a -->\n"
        return (f"---\nname: ev\ndescription: d\n{ttl}digest: {stamp or digest(body)}\nmetadata:\n{nested}  type: reference\n---\n" + body)

    # a tree with everything the real one has: the rules file, a skill, four living diagrams, the evidence guide
    living = {f"{k}d{n}.md": diagram.replace("name: e7", f"name: d{n}") for n in range(4)}
    whole = {**good, **living, EVIDENCE: evidence(), "CLAUDE.md": "rules\n", ".claude/skills/walk/SKILL.md": "a skill\n"}
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
        "type: after another metadata key": (
            tree({**good, k + "v.md": "---\nname: v\ndescription: d\nmetadata:\n  local_reason: x\n  type: user\n---\n"}), None),
        "LIVING without a diagram": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md) LIVING — hook"]),
                                     "tagged LIVING"),
        "LIVING without update triggers": (
            tree({**good, k + "l.md": ok(7) + "```mermaid\nflowchart TD\n```\n"},
                 index=good_index + ["- [l](knowledge/l.md) LIVING — hook"]), "tagged LIVING"),
        "LIVING with both is clean": (tree({**good, k + "l.md": diagram},
                                           index=good_index + ["- [l](knowledge/l.md) LIVING — hook"]), None),
        "the word LIVING in a hook is not the tag": (tree(good, index=good_index[:2] + [
            "- [e2](knowledge/e2.md) — a LIVING thing"]), None),
        "ttl without generated": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\n")}), "without a generated"),
        "ttl that is not a number": (tree({**good, "guides/g.md": ok(5, "ttl_days: soon\ngenerated: 2026-10-01\n")}),
                                     "not a whole number"),
        "ttl over the cap": (tree({**good, "guides/g.md": ok(5, "ttl_days: 365\ngenerated: 2026-10-01\n")}), "over the cap"),
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
        "naming store files that exist is clean": (
            tree({**good, k + "r.md": ok(4) + "see knowledge/e0.md\n", "CLAUDE.md": "read `knowledge/e1.md`\n",
                  ".claude/skills/walk/SKILL.md": "and knowledge/e2.md\n"}, index=good_index + ["- [r](knowledge/r.md) — hook"]), None),
        "generated: that is not a date": (tree({**good, "guides/g.md": ok(5, "ttl_days: 30\ngenerated: 2026-02-30\n")}), "is not a date"),
        "a styled LIVING tag is refused, not dropped": (tree(good, index=good_index[:2] + ["- [e2](knowledge/e2.md) `LIVING` — hook"]),
                                                        "the tag is the bare word"),
        "generated blocks with their digest are clean": (tree({**good, "guides/g.md": evidence()}), "warn:expire in 29 days"),
        "an empty generated block": (tree({**good, "guides/g.md": evidence(inner="")}), "generated block a is empty"),
        "a generated block edited by hand": (tree({**good, "guides/g.md": evidence(stamp="0" * 64)}), "not what the digest"),
        "generated numbers without ttl_days": (tree({**good, "guides/g.md": evidence(ttl="")}), "would never expire"),
        "ttl_days nested under metadata does not count": (
            tree({**good, "guides/g.md": evidence(ttl="", nested="  ttl_days: 30\n")}), "would never expire"),
        "four entries are below the real floor": (tree({**good, k + "e3.md": ok(3)}), "floor is 5", {"floor": FLOOR}),
        "whole: a tree with everything the real one has is clean": (tree(whole, index=whole_index), "warn:expire in 29 days", real),
        "whole: without the rules file": (tree(without("CLAUDE.md"), index=whole_index), "CLAUDE.md is missing", real),
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
