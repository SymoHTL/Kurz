#!/usr/bin/env python3
"""Lint for the knowledge store: knowledge/, guides/ and INDEX.md. Fails on: an INDEX link to a
missing file, a store file no INDEX line links to, a path of a store file that does not exist,
written in an entry, in CLAUDE.md or in a skill, an INDEX line without a hook after its em dash,
an entry whose frontmatter lacks a non-empty name, description or metadata.type, a nested or
non-.md file under knowledge/ or guides/, an entry tagged LIVING without a mermaid block or an
"Update triggers" section, a scan that found fewer than FLOOR entries, and a guide whose numbers
expired: `ttl_days:` without `generated:`, a TTL over TTL_CAP, a `generated:` date more than one
day ahead, or a TTL that ran out. Reports without failing: [[wikilinks]] that resolve to nothing,
the days every TTL has left, and a TTL in its last week. Credentials, machine-bound strings and
conflict markers are the tree gate's job (tools/tree_gate.py), for the whole tree.
`--self-test` plants one case per rule, plus precision cases that must stay clean."""
import datetime
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

# Every other rule is satisfied by finding nothing: a scan of the wrong tree prints "0 errors".
# Keep the floor well below the real entry count and well above zero; raise it as the store grows.
FLOOR = 5
TTL_CAP = 90
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


def lint(root, floor=FLOOR, today=None):
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

    # Who else names store files: the rules file and the skills. The index has its own rule above.
    skills = os.path.join(root, ".claude", "skills")
    readers = ["CLAUDE.md"] + [f".claude/skills/{d}/SKILL.md" for d in (sorted(os.listdir(skills)) if os.path.isdir(skills) else [])]
    for rel in sorted(files) + [r for r in readers if os.path.isfile(os.path.join(root, r))]:
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
        errors, warnings = errors + e, warnings + w
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
    }
    results = []
    for label, (root, needle) in cases.items():
        errors, warnings = lint(root, floor=3, today=today)
        if needle and needle.startswith("warn:"):
            hit = not errors and any(needle[5:] in w for w in warnings)
        else:
            hit = any(needle in e for e in errors) if needle else not errors and not warnings
        results.append((label, hit, f"errors={errors} warnings={warnings}"))
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
