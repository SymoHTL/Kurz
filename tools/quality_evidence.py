#!/usr/bin/env python3
"""Regenerate the numbers of guides/quality-bar-evidence.md. Every number in that guide lives in a
marked block (`<!-- generated:NAME -->` ... `<!-- /generated:NAME -->`) that this tool computes
from the tree, from git and from the forge; the prose between the blocks carries no measurement.
The frontmatter's `generated:` date is stamped on every run, and tools/lint_knowledge.py counts
its `ttl_days:` down and fails once it ran out.

  quality_evidence.py            recompute every block and rewrite the file
  quality_evidence.py --print    recompute and print, write nothing

It refuses to write (exit 1, file untouched) when a block it computed has no marker in the file,
when the file has a block it did not compute, when a marker is not closed, or when the file has no
`generated:` line: otherwise some numbers would stay old while the date said they were new. The
new text is built completely before anything is written, and written through a temp file."""
import datetime
import json
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gates  # noqa: E402
import kit  # noqa: E402
import red_proof  # noqa: E402

EVIDENCE = "guides/quality-bar-evidence.md"
BLOCK = re.compile(r"<!-- generated:([\w-]+) -->\n(.*?)<!-- /generated:\1 -->", re.S)
FINDINGS_MARK = re.compile(r"<!-- kurz-review:findings (\[.*?\]) -->", re.S)


def table(header, rows):
    lines = ["| " + " | ".join(header) + " |", "|" + "---|" * len(header)]
    return "\n".join(lines + ["| " + " | ".join(str(c) for c in row) + " |" for row in rows])


def block_gates(root):
    where = {"always": "every run", "pr": "pull requests", "local": "local runs"}
    return table(["Gate", "Runs on", "What turns it red"], [(f"`{n}`", where[w], why) for n, _, w, why in gates.GATES])


def block_self_tests(root):
    errors, stats = red_proof.check(red_proof.copy_of_tree())
    if errors:
        raise kit.Refused(f"the self-tests are not green, so their numbers are not evidence: {errors[0]}")
    rows = [(f"`{tool}`", s["cases"], s["proofs"]) for tool, s in stats.items()]
    return table(["Tool", "Self-test cases", "Red proofs replayed"],
                 rows + [("**total**", sum(s["cases"] for s in stats.values()), sum(s["proofs"] for s in stats.values()))])


def block_store(root):
    index = kit.read(os.path.join(root, "INDEX.md"))
    lines = [line for line in index.splitlines() if re.search(r"\]\((?:knowledge|guides)/", line)]
    tags = {}
    for line in lines:
        between = line[line.index(")") + 1:].partition("—")[0].split()
        for tag in between or ["(untagged)"]:
            tags[tag] = tags.get(tag, 0) + 1
    import yaml
    sections = yaml.safe_load(kit.read(os.path.join(root, ".review/review-rules.yaml")))["sections"]
    rows = [("Entries in INDEX.md", len(lines))] + [(f"tagged {tag}", n) for tag, n in sorted(tags.items())]
    rows += [("Review rule sections", len(sections)), ("Review rules", sum(len(s["rules"]) for s in sections))]
    return table(["Store", "Count"], rows)


def block_design(root):
    text = kit.read(os.path.join(root, "kurz-design.md"))
    open_section = re.search(r"^## \d+\. Open\n(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not open_section:
        raise kit.Refused("kurz-design.md has no Open section")
    return table(["Design record", "Count"], [
        ("Sections", len(re.findall(r"^## \d+\. ", text, re.M))),
        ("Statements marked *(assumed)*", len(re.findall(r"\*\(assumed", text))),
        ("Open questions", len(re.findall(r"^- ", open_section.group(1), re.M))),
    ])


def block_forge(root):
    repo = kit.repo()
    pulls = kit.gh_pages(f"repos/{repo}/pulls?state=all&per_page=100")
    merged = [p for p in pulls if p.get("merged_at")]
    severity, over_red = {"high": 0, "medium": 0, "low": 0}, 0
    for p in pulls:
        comments = kit.gh_pages(f"repos/{repo}/pulls/{p['number']}/comments?per_page=100")
        notes = kit.gh_pages(f"repos/{repo}/issues/{p['number']}/comments?per_page=100")
        for c in comments + notes:
            for raw in FINDINGS_MARK.findall(c.get("body") or ""):
                for f in json.loads(raw):
                    if f.get("severity") in severity:
                        severity[f["severity"]] += 1
            over_red += (c.get("body") or "").startswith("Merged over red")
    hazards = kit.gh_pages(f"repos/{repo}/issues?state=open&labels=hazard&per_page=100")
    return table(["Forge", "Count"], [
        ("Pull requests opened", len(pulls)), ("Pull requests merged", len(merged)), ("Merged over red, with a recorded waiver", over_red),
        ("Review findings posted: high", severity["high"]), ("Review findings posted: medium", severity["medium"]),
        ("Review findings posted: low", severity["low"]), ("Open HAZARD issues", len(hazards)),
    ])


BLOCKS = {"gates": block_gates, "self-tests": block_self_tests, "store": block_store, "design": block_design, "forge": block_forge}


def rewrite(text, blocks, today):
    """The new file text. Raises Refused instead of producing a file whose date and numbers disagree."""
    present = [m.group(1) for m in BLOCK.finditer(text)]
    opened = re.findall(r"<!-- generated:([\w-]+) -->", text)
    if sorted(opened) != sorted(present) or len(set(present)) != len(present):
        raise kit.Refused(f"a generated block is not closed, or occurs twice: {sorted(opened)} opened, {sorted(present)} complete")
    if set(blocks) - set(present):
        raise kit.Refused(f"computed blocks with no marker in the file: {sorted(set(blocks) - set(present))}")
    if set(present) - set(blocks):
        raise kit.Refused(f"blocks in the file that this run did not compute: {sorted(set(present) - set(blocks))}")
    if not re.search(r"^generated:[ \t]*\S", text, re.M):
        raise kit.Refused("the file has no generated: line to stamp")
    text = BLOCK.sub(lambda m: f"<!-- generated:{m.group(1)} -->\n{blocks[m.group(1)]}\n<!-- /generated:{m.group(1)} -->", text)
    return re.sub(r"^generated:.*$", f"generated: {today.isoformat()}", text, count=1, flags=re.M)


def update(path, compute, today, write=True):
    """Compute every block, then write once. Any failure leaves the file as it was."""
    text = kit.read(path)
    new = rewrite(text, {name: fn() for name, fn in compute.items()}, today)
    if write:
        fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(new)
        os.replace(tmp, path)
    return new


def self_test():
    today = datetime.date(2026, 10, 1)
    good = ("---\nname: e\ngenerated: 2026-01-01\nttl_days: 60\n---\n\nProse before.\n\n<!-- generated:a -->\nold a\n<!-- /generated:a -->\n\n"
            "Prose between.\n\n<!-- generated:b -->\nold b\n<!-- /generated:b -->\n\nProse after.\n")
    blocks = {"a": "new a", "b": "new b"}
    cases = []

    def refused(name, text, given, needle):
        try:
            rewrite(text, given, today)
            cases.append((name, False, "was not refused"))
        except Exception as e:  # anything but the refusal this case names is a failure of the case, not a crash
            cases.append((name, isinstance(e, kit.Refused) and needle in str(e), repr(e)))

    new = rewrite(good, blocks, today)
    cases.append(("blocks are replaced", "new a" in new and "new b" in new and "old" not in new, new))
    cases.append(("the date is stamped", "generated: 2026-10-01" in new and "2026-01-01" not in new, new))
    cases.append(("the prose is untouched", all(p in new for p in ("Prose before.", "Prose between.", "Prose after.", "ttl_days: 60")), new))
    cases.append(("a second run changes nothing", rewrite(new, blocks, today) == new, ""))
    refused("a computed block without a marker", good, {**blocks, "c": "x"}, "no marker in the file")
    refused("a block in the file that was not computed", good, {"a": "x"}, "did not compute")
    refused("no generated: line", good.replace("generated: 2026-01-01\n", ""), blocks, "no generated: line")
    refused("a marker that is not closed", good.replace("<!-- /generated:b -->", ""), blocks, "not closed")
    refused("a block that occurs twice", good + "<!-- generated:a -->\nx\n<!-- /generated:a -->\n", blocks, "occurs twice")

    path = os.path.join(tempfile.mkdtemp(), "evidence.md")
    with open(path, "w", encoding="utf-8") as f:
        f.write(good)

    def broken():
        raise kit.Refused("the forge did not answer")
    try:
        update(path, {"a": lambda: "new a", "b": broken}, today)
        cases.append(("a block that cannot be computed leaves the file untouched", False, "no refusal"))
    except kit.Refused:
        cases.append(("a block that cannot be computed leaves the file untouched", kit.read(path) == good, kit.read(path)))
    update(path, {"a": lambda: "new a", "b": lambda: "new b"}, today, write=False)
    cases.append(("--print writes nothing", kit.read(path) == good, ""))
    update(path, {"a": lambda: "new a", "b": lambda: "new b"}, today)
    cases.append(("a full run writes the file, and leaves no temp file", kit.read(path) == new and os.listdir(os.path.dirname(path)) == ["evidence.md"],
                  os.listdir(os.path.dirname(path))))
    cases.append(("the real guide has a marker for every block this tool computes, and no other",
                  sorted(m.group(1) for m in BLOCK.finditer(kit.read(os.path.join(kit.ROOT, EVIDENCE)))) == sorted(BLOCKS), ""))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    try:
        new = update(os.path.join(kit.ROOT, EVIDENCE), {name: (lambda fn=fn: fn(kit.ROOT)) for name, fn in BLOCKS.items()},
                     datetime.date.today(), write="--print" not in sys.argv)
    except (kit.Refused, OSError, KeyError, ValueError) as e:
        print(f"REFUSED, nothing written: {type(e).__name__}: {e}")
        sys.exit(1)
    print(new if "--print" in sys.argv else f"{EVIDENCE}: {len(BLOCKS)} blocks regenerated")
