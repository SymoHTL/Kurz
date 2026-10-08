#!/usr/bin/env python3
"""Regenerate the numbers of guides/quality-bar-evidence.md. Every number in that guide lives in a
marked block (`<!-- generated:NAME -->` ... `<!-- /generated:NAME -->`) that this tool computes
from the tree, from git and from the forge; the prose between the blocks carries no measurement.
The frontmatter's `generated:` date and the `digest:` of the blocks are stamped on every run.
tools/lint_knowledge.py counts `ttl_days:` down and fails once it ran out, and fails when the
blocks are not what the digest says: numbers somebody typed are not this tool's numbers.

  quality_evidence.py            recompute every block and rewrite the file
  quality_evidence.py --print    recompute and print, write nothing

It refuses to write (exit 1, file untouched) when a block it computed has no marker in the file,
when the file has a block it did not compute, when a marker is not closed, when the file has no
`generated:` or no `digest:` line, or while a self-test is red: otherwise some numbers would stay
old, or be no evidence, while the date said they were new. The new text is built completely before
anything is written, and written through a temp file.

What it counts on the forge it counts only from pull requests and posts of the Actions token and of
people who may write here (tools/pr_gates.py `trusted`): anyone can open a pull request on, or
comment in, a public repository."""
import contextlib
import datetime
import json
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gates  # noqa: E402
import kit  # noqa: E402
import lint_knowledge  # noqa: E402
import lint_reference  # noqa: E402
import pr_gates  # noqa: E402
import red_proof  # noqa: E402

EVIDENCE, BLOCK = lint_knowledge.EVIDENCE, lint_knowledge.BLOCK


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
    sections = kit.load_yaml(kit.read(os.path.join(root, ".review/review-rules.yaml")))["sections"]
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


def block_reference(root):
    errors, counted = lint_reference.lint(root)
    if errors:
        raise kit.Refused(f"the reference lint is not green, so its counts are not evidence: {errors[0]}")
    return table(["Reference and corpus", "Count"], [
        ("Rules in the reference", counted["rules"]),
        *[(f"of them {status}", counted[status]) for status in ("decided", "assumed", "proposed", "open")],
        ("Corpus cases, none of them run", counted["cases"]),
        ("Compile-error ids", counted["errors"]),
        ("Run-time error ids", counted["runtime"]),
    ])


def block_forge(root):
    repo = kit.repo()
    pulls = [p for p in kit.gh_pages(f"repos/{repo}/pulls?state=all&per_page=100")
             if pr_gates.trusted((p.get("user") or {}).get("login", ""), p.get("author_association"))]
    merged = [p for p in pulls if p.get("merged_at")]
    severity, over_red = {"high": 0, "medium": 0, "low": 0}, 0
    for p in pulls:
        # Threads hold the findings above low. The lows live on the issue that collects them, but the
        # reviewer leaves a note on the pull request listing them with the same marker (Forge.lows),
        # so the issue itself is not read here.
        comments = kit.gh_pages(f"repos/{repo}/pulls/{p['number']}/comments?per_page=100")
        notes = kit.gh_pages(f"repos/{repo}/issues/{p['number']}/comments?per_page=100")
        for c in comments + notes:
            if not pr_gates.trusted((c.get("user") or {}).get("login", ""), c.get("author_association")):
                continue
            for raw in pr_gates.MARK.findall(c.get("body") or ""):
                try:
                    found = json.loads(raw)
                except ValueError:
                    continue  # a damaged marker counts nothing; it must not stop the evidence
                for f in found if isinstance(found, list) else []:
                    if isinstance(f, dict) and isinstance(f.get("severity"), str) and f["severity"] in severity:
                        severity[f["severity"]] += 1
            over_red += (c.get("body") or "").startswith("Merged over red")
    # the issues endpoint lists pull requests too
    hazards = [i for i in kit.gh_pages(f"repos/{repo}/issues?state=open&labels=hazard&per_page=100") if "pull_request" not in i]
    return table(["Forge", "Count"], [
        ("Pull requests opened", len(pulls)), ("Pull requests merged", len(merged)), ("Merged over red, with a recorded waiver", over_red),
        ("Review findings posted: high", severity["high"]), ("Review findings posted: medium", severity["medium"]),
        ("Review findings posted: low", severity["low"]), ("Open HAZARD issues", len(hazards)),
    ])


BLOCKS = {"gates": block_gates, "self-tests": block_self_tests, "store": block_store, "design": block_design,
          "reference": block_reference, "forge": block_forge}


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
    for line in ("generated", "digest"):
        if not re.search(rf"^{line}:[ \t]*\S", text, re.M):
            raise kit.Refused(f"the file has no {line}: line to stamp")
    text = BLOCK.sub(lambda m: f"<!-- generated:{m.group(1)} -->\n{blocks[m.group(1)]}\n<!-- /generated:{m.group(1)} -->", text)
    text = re.sub(r"^generated:.*$", f"generated: {today.isoformat()}", text, count=1, flags=re.M)
    return re.sub(r"^digest:.*$", f"digest: {lint_knowledge.digest(text)}", text, count=1, flags=re.M)  # last: the digest covers the date


def update(path, compute, today, write=True):
    """Compute every block, then write once. Any failure leaves the file as it was."""
    text = kit.read(path)
    new = rewrite(text, {name: fn() for name, fn in compute.items()}, today)
    if write:
        fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
        try:
            with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
                f.write(new)
            os.replace(tmp, path)
        finally:
            with contextlib.suppress(FileNotFoundError):
                os.remove(tmp)  # gone after the replace; left behind by a write or a replace that failed
    return new


def writes(argv):
    """Whether a run with these arguments writes the file: --print recomputes and prints only."""
    return "--print" not in argv


def self_test():
    today = datetime.date(2026, 10, 1)
    good = ("---\nname: e\ngenerated: 2026-01-01\ndigest: none\nttl_days: 60\n---\n\nProse before.\n\n<!-- generated:a -->\nold a\n<!-- /generated:a -->\n\n"
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
    fm = new.split("---\n")[1]
    cases.append(("the digest of the blocks is stamped, and the lint finds nothing against them",
                  f"digest: {lint_knowledge.digest(new)}" in fm and lint_knowledge.generated("e", new, fm) == [], fm))
    edited = new.replace("new a", "another a")
    cases.append(("a block edited after the run no longer fits the digest",
                  any("not what the digest" in e for e in lint_knowledge.generated("e", edited, fm)), lint_knowledge.generated("e", edited, fm)))
    moved = new.replace("generated: 2026-10-01", "generated: 2026-10-20")
    moved_fm = moved.split("---\n")[1]
    cases.append(("a date moved after the run no longer fits the digest",
                  any("not what the digest" in e for e in lint_knowledge.generated("e", moved, moved_fm)), lint_knowledge.generated("e", moved, moved_fm)))
    cases.append(("the prose is untouched", all(p in new for p in ("Prose before.", "Prose between.", "Prose after.", "ttl_days: 60")), new))
    cases.append(("a second run changes nothing", rewrite(new, blocks, today) == new, ""))
    refused("a computed block without a marker", good, {**blocks, "c": "x"}, "no marker in the file")
    refused("a block in the file that was not computed", good, {"a": "x"}, "did not compute")
    refused("no generated: line", good.replace("generated: 2026-01-01\n", ""), blocks, "no generated: line")
    refused("no digest: line", good.replace("digest: none\n", ""), blocks, "no digest: line")
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
    update(path, {"a": lambda: "new a", "b": lambda: "new b"}, today, write=writes(["--print"]))
    cases.append(("--print writes nothing", kit.read(path) == good, ""))
    update(path, {"a": lambda: "new a", "b": lambda: "new b"}, today, write=writes([]))
    cases.append(("a full run writes the file, and leaves no temp file", kit.read(path) == new and os.listdir(os.path.dirname(path)) == ["evidence.md"],
                  os.listdir(os.path.dirname(path))))
    cases.append(("the real guide has a marker for every block this tool computes, and no other",
                  sorted(m.group(1) for m in BLOCK.finditer(kit.read(os.path.join(kit.ROOT, EVIDENCE)))) == sorted(BLOCKS), ""))

    def got(fn, *args):
        """What fn returned, or the exception it raised, as a value."""
        try:
            return fn(*args)
        except Exception as e:
            return e

    def refuse(source, target):
        raise OSError("the disk refused the replace")

    saved_replace, os.replace = os.replace, refuse
    try:
        failed = got(update, path, {"a": lambda: "other a", "b": lambda: "other b"}, today)
    finally:
        os.replace = saved_replace
    cases.append(("a write that fails leaves the file as it was, and no temp file",
                  isinstance(failed, OSError) and kit.read(path) == new and os.listdir(os.path.dirname(path)) == ["evidence.md"],
                  (repr(failed), os.listdir(os.path.dirname(path)))))

    # the blocks, against a small tree and a forge that answers what it is told to
    root = tempfile.mkdtemp()
    os.mkdir(os.path.join(root, ".review"))
    for rel, body in {"INDEX.md": "- [a](knowledge/a.md) LIVING — hook\n- [b](guides/b.md) — hook\nnot an entry\n",
                      ".review/review-rules.yaml": "sections:\n  - name: s\n    always: true\n    rules: [one, two]\n",
                      "kurz-design.md": "## 1. Types\n\nx *(assumed)*\n\n## 2. Open\n\n- q1\n- q2\n"}.items():
        with open(os.path.join(root, rel), "w", encoding="utf-8") as f:
            f.write(body)
    store = got(block_store, root)
    cases.append(("store: entries, tags, sections and rules are counted",
                  all(row in str(store) for row in ("| Entries in INDEX.md | 2 |", "| tagged LIVING | 1 |", "| Review rule sections | 1 |", "| Review rules | 2 |")), store))
    design = got(block_design, root)
    cases.append(("design: sections, assumed statements and open questions are counted",
                  all(row in str(design) for row in ("| Sections | 2 |", "| Statements marked *(assumed)* | 1 |", "| Open questions | 2 |")), design))
    with open(os.path.join(root, "kurz-design.md"), "w", encoding="utf-8") as f:
        f.write("## 1. Types\n\nx\n")
    design = got(block_design, root)
    cases.append(("design: a record without its Open section is refused", isinstance(design, kit.Refused) and "no Open section" in str(design), repr(design)))

    saved_lint = lint_reference.lint
    try:
        lint_reference.lint = lambda tree: (["reference/02-variables.md:3: rule V1 is decided and cites no section"], {})
        red_reference = got(block_reference, root)
        # every count its own value, so that a row that reads the wrong key cannot pass
        lint_reference.lint = lambda tree: ([], {"rules": 11, "decided": 5, "assumed": 3, "proposed": 2, "open": 1, "cases": 13, "errors": 7, "runtime": 4})
        green_reference = got(block_reference, root)
    finally:
        lint_reference.lint = saved_lint
    cases.append(("reference: counts from a red lint are refused",
                  isinstance(red_reference, kit.Refused) and "not green" in str(red_reference), repr(red_reference)))
    rows = ("| Rules in the reference | 11 |", "| of them decided | 5 |", "| of them assumed | 3 |", "| of them proposed | 2 |",
            "| of them open | 1 |", "| Corpus cases, none of them run | 13 |", "| Compile-error ids | 7 |", "| Run-time error ids | 4 |")
    cases.append(("reference: rules by status, cases and error ids are counted, each row from its own key",
                  all(row in str(green_reference) for row in rows) and str(green_reference).count("|") == 10 * 3, green_reference))

    saved = red_proof.check, red_proof.copy_of_tree, kit.repo, kit.gh_pages
    red_proof.copy_of_tree = lambda: root
    try:
        red_proof.check = lambda tree: (["tools/x.py: its self-test does not pass (exit 1)"], {"tools/x.py": {"cases": 3, "proofs": 1}})
        red = got(block_self_tests, root)
        red_proof.check = lambda tree: ([], {"tools/x.py": {"cases": 3, "proofs": 1}, "tools/y.py": {"cases": 2, "proofs": 2}})
        green = got(block_self_tests, root)

        finding = lambda severity: f'<!-- kurz-review:findings [{{"file": "a.md", "line": 1, "severity": "{severity}", "title": "t"}}] -->'
        post = lambda login, association, body: {"user": {"login": login}, "author_association": association, "body": body}
        opened = lambda number, login, association, merged=None: {"number": number, "merged_at": merged, "user": {"login": login},
                                                                  "author_association": association}
        # every path in full: a query that asks for something else gets no answer
        answers = {
            "repos/o/n/pulls?state=all&per_page=100": [opened(1, "the-owner", "OWNER", "2026-10-01T00:00:00Z"),
                                                       opened(2, "a-collaborator", "COLLABORATOR"), opened(5, "a-stranger", "NONE")],
            "repos/o/n/pulls/1/comments?per_page=100": [
                post("github-actions[bot]", "NONE", finding("high")), post("a-stranger", "NONE", finding("high")),
                post("the-owner", "OWNER", "<!-- kurz-review:findings [not json] -->"),
                post("the-owner", "OWNER", '<!-- kurz-review:findings [{"severity": ["high"]}] -->')],
            "repos/o/n/issues/1/comments?per_page=100": [
                post("the-owner", "OWNER", "Merged over red at `abc`."), post("a-stranger", "NONE", "Merged over red at `abc`."),
                post("the-owner", "OWNER", finding("low"))],
            "repos/o/n/pulls/2/comments?per_page=100": [], "repos/o/n/issues/2/comments?per_page=100": [],
            # the stranger's pull request holds a finding of the Actions token: counted, it would show
            "repos/o/n/pulls/5/comments?per_page=100": [post("github-actions[bot]", "NONE", finding("medium"))],
            "repos/o/n/issues/5/comments?per_page=100": [],
            "repos/o/n/issues?state=open&labels=hazard&per_page=100": [{"number": 3}, {"number": 4, "pull_request": {"url": "x"}}],
        }

        def pages(path):
            if path not in answers:
                raise kit.Refused(f"the fake forge holds no answer for {path}")
            return answers[path]

        kit.repo, kit.gh_pages = (lambda: "o/n"), pages
        forge = got(block_forge, root)
    finally:
        red_proof.check, red_proof.copy_of_tree, kit.repo, kit.gh_pages = saved
    cases.append(("self-tests: numbers from a red self-test are refused", isinstance(red, kit.Refused) and "not green" in str(red), repr(red)))
    cases.append(("self-tests: a green run is a table with its totals", "| **total** | 5 | 3 |" in str(green), green))
    cases.append(("forge: pull requests, merges and open hazard issues are counted, and a pull request is no issue",
                  all(row in str(forge) for row in ("| Pull requests opened | 2 |", "| Pull requests merged | 1 |", "| Open HAZARD issues | 1 |")), forge))
    cases.append(("forge: a finding marker counts from the Actions token and from a maintainer, not from a stranger",
                  all(row in str(forge) for row in ("posted: high | 1 |", "posted: medium | 0 |", "posted: low | 1 |")), forge))
    cases.append(("forge: a damaged marker counts nothing and stops nothing, a severity that is no word included",
                  isinstance(forge, str), repr(forge)))
    cases.append(("forge: a pull request that someone without write access opened counts nothing, its findings included",
                  all(row in str(forge) for row in ("| Pull requests opened | 2 |", "posted: medium | 0 |")), forge))
    cases.append(("forge: a waiver record counts from a maintainer, not from a stranger", "| Merged over red, with a recorded waiver | 1 |" in str(forge), forge))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    write = writes(sys.argv[1:])
    try:
        new = update(os.path.join(kit.ROOT, EVIDENCE), {name: (lambda fn=fn: fn(kit.ROOT)) for name, fn in BLOCKS.items()},
                     datetime.date.today(), write=write)
    except (kit.Refused, OSError, KeyError, ValueError) as e:
        print(f"REFUSED, nothing written: {type(e).__name__}: {e}")
        sys.exit(1)
    print(f"{EVIDENCE}: {len(BLOCKS)} blocks regenerated" if write else new)
