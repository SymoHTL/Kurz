#!/usr/bin/env python3
"""The language reference and the conformance corpus are one statement of the language, kept in
two places: `reference/*.md` holds the rules, `corpus/**/*.kz` holds the cases. No compiler runs
the cases, so this lint keeps the two from drifting apart. It checks shape, never meaning.

  lint_reference.py           check
  lint_reference.py --sync    rewrite the sample under every `Case:` line from its corpus file, naming
                              each chapter as it is written

A rule is a heading `### <ID> (<status>[, §n ...])` with status decided, assumed, proposed or open
(reference/00-about.md says what each means). A sample in the reference is a `Case:` line that
links a corpus file, followed by a ```kurz block that equals the file's body. Within one chapter
only the first `Case:` line of a file carries the sample. A corpus file starts with a header: what
to expect, and the rules it shows.

Fails on:
- a `###` heading that is not a rule heading; a rule id used twice;
- a decided or assumed rule that cites no section of kurz-design.md, or one that does not exist;
  an assumed rule whose cited sections mark nothing as assumed;
- a decided, assumed or proposed rule with neither a case nor a `No case:` line with a reason;
  an open rule with a case (a case would bake in a pick nobody made);
- a `Case:` line outside a rule, to a missing file, whose text is not the path, or whose sample
  is missing, differs from the file's body or is shown a second time in one chapter; a code block
  that is neither a case sample nor marked `text`, or that is never closed (also when the fence of
  another block is met inside it); a fence indented or made of tildes, which this lint would not
  read as one;
- a corpus file with a header that cannot be read, with no body, naming an unknown or an open
  rule, not linked from a rule it names, or linked from a rule it does not name;
- an expected error id that is not in its table (compile errors for `error`, the run-time table
  under `## Run-time errors` for `throws`), or whose table row lists none of the case's rules; an
  error line outside the file, on a blank line or in the header; a table row with an unknown
  rule, a repeated id, an id no case expects, or a rule that no case expecting the id names; a row
  under a `| id | rules | meaning |` head that does not read as one; a row that reads as one under
  any other head or outside a table, since its id would go unchecked;
- a design record that cannot be read, said once instead of as every section it would lack;
- fewer rules or cases than the floors."""
import contextlib
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

RULES_FLOOR, CASES_FLOOR = 40, 30
RULE = re.compile(r"### ([A-Z]\d+) \((decided|assumed|proposed|open)((?:, §\d+)*)\)")
CASE = re.compile(r"Case: \[([^\]]+)\]\(\.\./corpus/([^)]+)\)")
ERROR_ROW = re.compile(r"\| `([a-z][a-z-]*)` \| ([A-Z]\d+(?:, [A-Z]\d+)*) \| .*\S.* \|")
EXPECT = re.compile(r"// expect: (?:(output)|(throws) ([a-z][a-z-]*) at (\d+)|error ([a-z][a-z-]*) at (\d+))")
RUNTIME_TABLE = "## Run-time errors"
ERROR_HEAD = "| id | rules | meaning |"  # an error table starts with this line, and every row under it is read
SEPARATOR = re.compile(r"\|(?:-+\|)+")
# a fence the lint would not see: Markdown also opens a block with a fence indented up to three spaces, or of tildes
LOOKALIKE = re.compile(r" {1,3}```| {0,3}~~~")


def rule_heading(line):
    """The RULE match of a well-formed rule heading, or None: a title glued on is no heading."""
    m = RULE.match(line)
    return None if not m or (line[m.end():] and not line[m.end():].startswith(" ")) else m


def parse_case(text):
    """({expect, error, line, rules, body}, None) for one corpus file, or (None, why)."""
    head, blank, body = text.partition("\n\n")
    lines = head.rstrip("\n").split("\n")
    m = EXPECT.fullmatch(lines[0])
    if not m:
        return None, "the first line is not `// expect: output`, `// expect: throws <id> at <line>` or `// expect: error <id> at <line>`"
    rest = lines[1:]
    while not m.group(5) and rest and rest[0].startswith("// | "):  # what the program prints, also before it throws
        rest = rest[1:]
    if rest and re.fullmatch(r"// build: (?:test|release)", rest[0]):
        rest = rest[1:]
    if len(rest) != 1 or not re.fullmatch(r"// rules: [A-Z]\d+(?:, [A-Z]\d+)*", rest[0]):
        return None, "the header does not end in one `// rules: A1, B2` line"
    if not blank or not body.strip():
        return None, "no body after the header and one empty line"
    return {"expect": "output" if m.group(1) else "throws" if m.group(2) else "error", "error": m.group(3) or m.group(5),
            "line": int(m.group(4) or m.group(6) or 0), "rules": rest[0][len("// rules: "):].split(", "), "body": body}, None


def read_tree(root):
    """({chapter path: text}, {corpus path relative to corpus/: text}, the design record's section numbers and texts,
    None), or with None for the sections and the reason last when the record cannot be read."""
    chapters, cases = {}, {}
    ref = os.path.join(root, "reference")
    for name in sorted(os.listdir(ref)) if os.path.isdir(ref) else []:
        if name.endswith(".md"):
            chapters[f"reference/{name}"] = kit.read(os.path.join(ref, name))
    for d, _, fs in os.walk(os.path.join(root, "corpus")):
        for f in sorted(fs):
            if f.endswith(".kz"):
                full = os.path.join(d, f)
                cases[os.path.relpath(full, os.path.join(root, "corpus")).replace(os.sep, "/")] = kit.read(full)
    try:
        record = kit.read(os.path.join(root, "kurz-design.md"))
    except OSError as e:
        return chapters, cases, None, f"{type(e).__name__}: {e}"
    parts = re.split(r"(?m)^## (\d+)\. .*$", record)
    return chapters, cases, dict(zip(parts[1::2], parts[2::2])), None


def lint(root, rules_floor=RULES_FLOOR, cases_floor=CASES_FLOOR):
    chapters, files, sections, unread = read_tree(root)
    errors, rules, links, error_ids = [], {}, [], {}
    if unread:
        errors.append(f"kurz-design.md cannot be read ({unread}): no rule's sections were checked")
    parsed = {}
    for path, text in files.items():
        parsed[path], why = parse_case(text)
        if why:
            errors.append(f"corpus/{path}: {why}")

    for chapter, text in chapters.items():
        lines, current, fence, covered, shown, in_table = text.split("\n"), None, None, set(), set(), False
        table = "error"  # rows before the run-time heading are compile errors, which a case expects with `error`
        for n, line in enumerate(lines):
            where = f"{chapter}:{n + 1}"
            if line.strip() == RUNTIME_TABLE:
                table = "throws"
            if LOOKALIKE.match(line):
                errors.append(f"{where}: a fence that is indented or made of tildes: the lint reads only ``` at the start of a line")
            if fence is not None:
                if line == "```":
                    fence = None
                if not line.startswith("```") or fence is None:
                    continue
                # another fence inside an open block: the block was never closed, and this line opens the next one
                errors.append(f"{where}: a code block that is never closed: a fence opens here inside it")
            if line.startswith("```"):
                fence = line[3:]
                sample_of_case = n > 0 and CASE.fullmatch(lines[n - 1])
                if fence == "kurz" and not sample_of_case:
                    errors.append(f"{where}: a Kurz sample that is not a corpus case: put a `Case:` line directly above it")
                elif fence not in ("kurz", "text"):
                    errors.append(f"{where}: a code block marked neither `kurz` nor `text`")
                continue
            if line == ERROR_HEAD:
                in_table = True
                continue
            in_table = in_table and line.startswith("|")
            if not in_table and ERROR_ROW.fullmatch(line):
                errors.append(f"{where}: a row that reads as an error id outside a table headed {ERROR_HEAD}: the id would go unchecked")
            row = ERROR_ROW.fullmatch(line) if in_table and not SEPARATOR.fullmatch(line) else None
            if in_table and not row and not SEPARATOR.fullmatch(line):
                errors.append(f"{where}: an error-table row that does not read | `id` | A1, B2 | meaning |")
            if row:
                if row.group(1) in error_ids:
                    errors.append(f"{where}: error id {row.group(1)} is in the tables twice")
                error_ids[row.group(1)] = (where, row.group(2).split(", "), table)
            if line.startswith("### "):
                m = rule_heading(line)
                if not m:
                    errors.append(f"{where}: a `###` heading that is not a rule: `### <ID> (decided|assumed|proposed|open[, §n]) title`")
                    current = None
                    continue
                current, status, cited = m.group(1), m.group(2), re.findall(r"§(\d+)", m.group(3))
                if current in rules:
                    errors.append(f"{where}: rule id {current} is used twice")
                rules[current] = status
                missing = [s for s in cited if sections is not None and s not in sections]
                if missing:
                    errors.append(f"{where}: rule {current} cites §{missing[0]}, which kurz-design.md does not have")
                elif status in ("decided", "assumed") and not cited:
                    errors.append(f"{where}: rule {current} is {status} and cites no section of kurz-design.md")
                elif status == "assumed" and sections is not None and not any("*(assumed" in sections[s] for s in cited):
                    errors.append(f"{where}: rule {current} is assumed, and no section it cites marks anything as assumed")
            elif line.startswith("#"):
                current = None
            if re.fullmatch(r"No case: .*\S.*", line) and current:
                covered.add(current)
            if line.startswith("Case:"):
                m = CASE.fullmatch(line)
                if not m or m.group(1) != m.group(2):
                    errors.append(f"{where}: a Case line reads `Case: [path](../corpus/path)`, with the same path twice")
                    continue
                path = m.group(2)
                if current is None:
                    errors.append(f"{where}: a Case line outside a rule")
                    continue
                covered.add(current)
                links.append((current, path, where))
                if path not in files:
                    errors.append(f"{where}: corpus/{path} does not exist")
                elif parsed[path]:
                    block = "```kurz\n" + parsed[path]["body"].rstrip("\n") + "\n```"
                    if path in shown:
                        if lines[n + 1:n + 2] == ["```kurz"]:
                            errors.append(f"{where}: the sample of corpus/{path} is shown a second time in this chapter: run tools/lint_reference.py --sync")
                    elif "\n".join(lines[n + 1:n + 1 + block.count("\n") + 1]) != block:
                        errors.append(f"{where}: the sample is missing or differs from corpus/{path}: run tools/lint_reference.py --sync")
                    shown.add(path)
        if fence is not None:
            errors.append(f"{chapter}: a code block that is never closed")
        for rule in [r for r in rules if r not in covered and rules[r] != "open"]:
            if any(line.startswith(f"### {rule} ") for line in lines):
                errors.append(f"{chapter}: rule {rule} is {rules[rule]} and has neither a case nor a `No case:` line")

    for rule, path, where in links:
        if rules.get(rule) == "open":
            errors.append(f"{where}: rule {rule} is open and has a case; a case would bake in a pick nobody made")
        if parsed.get(path) and rule not in parsed[path]["rules"]:
            errors.append(f"{where}: rule {rule} links corpus/{path}, which does not name it in `// rules:`")
    for path, case in parsed.items():
        if not case:
            continue
        for rule in case["rules"]:
            if rule not in rules:
                errors.append(f"corpus/{path}: names rule {rule}, which the reference does not have")
            elif rules[rule] == "open":
                errors.append(f"corpus/{path}: relies on rule {rule}, which is open")
            elif (rule, path) not in {(r, p) for r, p, _ in links}:
                errors.append(f"corpus/{path}: names rule {rule}, which has no Case line for it")
        if case["expect"] in ("error", "throws"):
            body_at = files[path].split("\n")
            kind = "error" if case["expect"] == "error" else "run-time error"
            if case["error"] not in error_ids or error_ids[case["error"]][2] != case["expect"]:
                errors.append(f"corpus/{path}: expects {kind} {case['error']}, which is not in the {kind} table")
            elif not set(case["rules"]) & set(error_ids[case["error"]][1]):
                errors.append(f"corpus/{path}: expects {kind} {case['error']} and names no rule that the error table lists for it")
            if not (1 <= case["line"] <= len(body_at)) or not body_at[case["line"] - 1].strip() or body_at[case["line"] - 1].startswith("// "):
                errors.append(f"corpus/{path}: the error line {case['line']} is outside the file, empty or in the header")
    raised = {(c["error"], r) for c in parsed.values() if c and c["expect"] in ("error", "throws") for r in c["rules"]}
    for eid, (where, of_rules, kind) in error_ids.items():
        errors += [f"{where}: error {eid} names rule {r}, which the reference does not have" for r in of_rules if r not in rules]
        if eid not in {e for e, _ in raised}:
            errors.append(f"{where}: no corpus case {'throws' if kind == 'throws' else 'expects error'} {eid}")
        else:
            errors += [f"{where}: error {eid} lists rule {r}, and no case that names {r} expects it"
                       for r in of_rules if r in rules and (eid, r) not in raised]
    if len(rules) < rules_floor or len(files) < cases_floor:
        errors.append(f"only {len(rules)} rules and {len(files)} cases found, floors are {rules_floor} and {cases_floor}: is this the right tree?")
    count = {s: sum(v == s for v in rules.values()) for s in ("decided", "assumed", "proposed", "open")}
    kinds = [kind for *_, kind in error_ids.values()]
    return errors, {"rules": len(rules), "cases": len(files), "errors": kinds.count("error"), "runtime": kinds.count("throws"), **count}


def sync(root, say=print):
    """Rewrite the sample under every Case line the lint accepts (outside a code block, inside a
    rule, its text the path) from its corpus file, and name each chapter through `say` as it is
    written. Every chapter is computed before one is written: a sample that is never closed refuses
    the whole run, because dropping it would drop the rest of its chapter. A write that fails is a
    refusal that names the chapters written before it, and leaves no temporary file. Returns the
    chapters written."""
    chapters, files, _, _ = read_tree(root)
    new = {}
    for chapter, text in chapters.items():
        lines, out, n, shown, fence, current = text.split("\n"), [], 0, set(), None, None
        while n < len(lines):
            line = lines[n]
            out.append(line)
            n += 1
            if fence is not None:  # a Case line inside a code block is text, as the lint reads it
                if line == "```":
                    fence = None
                continue
            if line.startswith("```"):
                fence = line[3:]
                continue
            if line.startswith("#"):
                heading = rule_heading(line) if line.startswith("### ") else None
                current = heading.group(1) if heading else None
            m = CASE.fullmatch(line)
            accepted = m and current and m.group(1) == m.group(2) and m.group(2) in files
            case = parse_case(files[m.group(2)])[0] if accepted else None
            if not case:
                continue
            if n < len(lines) and lines[n] == "```kurz":  # drop the old sample: it ends at the next fence, which must close it
                close = next((i for i in range(n + 1, len(lines)) if lines[i].startswith("```")), None)
                if close is None or lines[close] != "```":
                    raise kit.Refused(f"{chapter}:{n + 1}: this sample is never closed; nothing was rewritten")
                n = close + 1
            if m.group(2) not in shown:  # in one chapter only the first Case line of a file carries the sample
                out += ["```kurz", *case["body"].rstrip("\n").split("\n"), "```"]
            shown.add(m.group(2))
        if out != lines:
            new[chapter] = out
    written = []
    for chapter, out in new.items():
        path, tmp = os.path.join(root, chapter), None
        try:
            fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
            with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
                f.write("\n".join(out))
            os.replace(tmp, path)
        except OSError as e:
            if tmp:
                with contextlib.suppress(OSError):
                    os.remove(tmp)
            raise kit.Refused(f"wrote only {len(written)} of {len(new)} chapters ({', '.join(written) or 'none'}); "
                              f"{chapter} failed: {e}") from e
        written.append(chapter)
        say(f"rewrote the samples of {chapter}")
    return written


def self_test():
    with kit.scratch("lint-reference-") as base:  # every tree of the suite; a removal that fails is said, not raised
        return cases_in(base)


def cases_in(base):
    record = "# Design\n\n## 1. Goals\n\n- fast\n\n## 4. Types\n\n- numbers\n- small values *(assumed)*\n"
    case = "// expect: output\n// | 9\n// rules: V1\n\nx = 4\nprint(x + 5)\n"
    bad = "// expect: error assign-immutable at 6\n// rules: V2\n\nx = 4\nprint(x)\nx = 5\n"
    thrown = "// expect: throws overflow at 6\n// | 1\n// rules: V5\n\nprint(1)\nprint(2147483647 + x)\n"
    chapter = ("# Variables\n\n### V1 (decided, §4) Declaration\n\nA name.\n\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n"
               "```kurz\nx = 4\nprint(x + 5)\n```\n\n### V2 (assumed, §4) Assignment\n\nNo second assignment.\n\n"
               "Case: [vars/assign.kz](../corpus/vars/assign.kz)\n```kurz\nx = 4\nprint(x)\nx = 5\n```\n\n"
               "### V3 (open) Shadowing\n\nNobody chose.\n\n### V4 (proposed) Scope\n\nNo case: needs blocks.\n\n"
               "### V5 (decided, §4) Overflow\n\nThrows.\n\nCase: [vars/overflow.kz](../corpus/vars/overflow.kz)\n"
               "```kurz\nprint(1)\nprint(2147483647 + x)\n```\n\n"
               "## Errors\n\n| id | rules | meaning |\n|---|---|---|\n| `assign-immutable` | V2 | a second assignment |\n\n"
               "## Run-time errors\n\n| id | rules | meaning |\n|---|---|---|\n| `overflow` | V5 | arithmetic left its type |\n")
    good = {"kurz-design.md": record, "reference/02-variables.md": chapter, "corpus/vars/declare.kz": case, "corpus/vars/assign.kz": bad,
            "corpus/vars/overflow.kz": thrown}

    def tree(change=None, drop=()):
        root = tempfile.mkdtemp(dir=base)
        for path, text in {**good, **(change or {})}.items():
            if path in drop:
                continue
            full = os.path.join(root, *path.split("/"))
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
        return root

    def edit(path, old, new):
        assert good[path].count(old) == 1, (path, old)
        return {path: good[path].replace(old, new)}

    ref, declare, assign, overflow = "reference/02-variables.md", "corpus/vars/declare.kz", "corpus/vars/assign.kz", "corpus/vars/overflow.kz"
    trees = {
        "a clean reference and corpus pass": (tree(), None),
        "floors catch an empty scan": (tree(drop=(ref, declare, assign)), "floors are"),
        "a heading that is not a rule": (tree(edit(ref, "### V3 (open) Shadowing", "### Shadowing")), "not a rule"),
        "an unknown status": (tree(edit(ref, "### V3 (open)", "### V3 (maybe)")), "not a rule"),
        "a rule heading with its title glued on": (tree(edit(ref, "### V3 (open) Shadowing", "### V3 (open)Shadowing")), "not a rule"),
        "a rule id used twice": (tree(edit(ref, "### V3 (open)", "### V1 (open)")), "used twice"),
        "a decided rule without a section": (tree(edit(ref, "### V1 (decided, §4)", "### V1 (decided)")), "cites no section"),
        "a section the record does not have": (tree(edit(ref, "### V1 (decided, §4)", "### V1 (decided, §9)")), "does not have"),
        "an assumed rule where nothing is assumed": (tree(edit(ref, "### V2 (assumed, §4)", "### V2 (assumed, §1)")), "marks anything as assumed"),
        "a rule without a case or a reason": (tree(edit(ref, "No case: needs blocks.", "Later.")), "neither a case nor"),
        "a `No case:` line without a reason": (tree(edit(ref, "No case: needs blocks.", "No case: ")), "neither a case nor"),
        "an open rule with a case": (tree({**edit(ref, "### V1 (decided, §4)", "### V1 (open)")}), "is open and has a case"),
        "a case that relies on an open rule": (tree({**edit(ref, "### V1 (decided, §4)", "### V1 (open)")}), "which is open"),
        "a Case line outside a rule": (tree(edit(ref, "# Variables\n", "# Variables\n\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n")), "outside a rule"),
        "a Case line to a missing file": (tree(drop=(declare,)), "does not exist"),
        "a Case line whose text is not the path": (tree(edit(ref, "[vars/declare.kz]", "[the case]")), "the same path twice"),
        "a sample that differs from its file": (tree(edit(ref, "print(x + 5)\n```", "print(x + 6)\n```")), "differs from corpus/vars/declare.kz"),
        "a sample that is missing": (tree(edit(ref, "```kurz\nx = 4\nprint(x + 5)\n```\n", "")), "missing or differs"),
        "a Kurz sample that is not a case": (tree(edit(ref, "Nobody chose.", "Nobody chose.\n\n```kurz\ny = 1\n```")), "not a corpus case"),
        "a code block without a kind": (tree(edit(ref, "Nobody chose.", "Nobody chose.\n\n```\ny = 1\n```")), "neither `kurz` nor `text`"),
        "a header that cannot be read": (tree(edit(declare, "// expect: output", "// expects output")), "the first line is not"),
        "a header without rules": (tree(edit(declare, "// rules: V1\n", "")), "does not end in one"),
        "a rules line that is not a list of ids": (tree(edit(declare, "// rules: V1", "// rules: v1")), "does not end in one"),
        "a case without a body": (tree({declare: "// expect: output\n// rules: V1\n"}), "no body"),
        "a case that names an unknown rule": (tree(edit(declare, "// rules: V1", "// rules: V1, V9")), "does not have"),
        "a case not linked from a rule it names": (tree(edit(declare, "// rules: V1", "// rules: V1, V4")), "has no Case line for it"),
        "a case linked from a rule it does not name": (tree(edit(assign, "// rules: V2", "// rules: V4")), "does not name it"),
        "an error id that is not in the table": (tree(edit(assign, "error assign-immutable at", "error assign-twice at")), "not in the error table"),
        "an error line outside the file": (tree(edit(assign, "at 6", "at 60")), "outside the file"),
        "an error line in the header": (tree(edit(assign, "at 6", "at 2")), "outside the file, empty or in the header"),
        "a throws header without an id and a line": (tree(edit(overflow, "throws overflow at 6", "throws")), "the first line is not"),
        "a throws id that is not in the run-time table": (tree(edit(overflow, "throws overflow at 6", "throws boom at 6")),
                                                          "run-time error boom, which is not in the run-time error table"),
        "a throws id taken from the compile-error table": (tree(edit(overflow, "throws overflow at 6", "throws assign-immutable at 6")),
                                                           "not in the run-time error table"),
        "an error id taken from the run-time table": (tree(edit(assign, "error assign-immutable at 6", "error overflow at 6")),
                                                      "error overflow, which is not in the error table"),
        "a throws line in the header": (tree(edit(overflow, "at 6", "at 1")), "outside the file, empty or in the header"),
        "a run-time row that no case expects": (tree(edit(ref, "| `overflow` | V5 | arithmetic left its type |\n",
                                                          "| `overflow` | V5 | arithmetic left its type |\n| `divide-by-zero` | V5 | by zero |\n")),
                                                "no corpus case throws divide-by-zero"),
        "an error line on the blank line after the header": (tree(edit(assign, "at 6", "at 3")), "outside the file, empty or in the header"),
        "an error line just past the end of the file": (tree(edit(assign, "at 6", "at 7")), "outside the file, empty or in the header"),
        "an error id no case expects": (tree(edit(ref, "| `assign-immutable` | V2 | a second assignment |",
                                               "| `assign-immutable` | V2 | a second assignment |\n| `unused-variable` | V1 | never read |")), "no corpus case expects"),
        "an error row with an unknown rule": (tree(edit(ref, "| V2 | a second", "| V9 | a second")), "names rule V9"),
        "an error id in the table twice": (tree(edit(ref, "| `assign-immutable` | V2 | a second assignment |",
                                                 "| `assign-immutable` | V2 | a second assignment |\n| `assign-immutable` | V2 | again |")), "in the tables twice"),
        "an error case that names no rule of its error": (tree(edit(ref, "| V2 | a second", "| V1 | a second")), "names no rule that the error table lists"),
        "an error row with a rule that no case shows raising it": (tree(edit(ref, "| V2 | a second", "| V1 | a second")), "no case that names V1 expects it"),
        "a code block that is never closed": (tree(edit(ref, "| `assign-immutable` | V2 | a second assignment |\n",
                                                    "| `assign-immutable` | V2 | a second assignment |\n\n```text\nnever closed\n")), "never closed"),
        # the first sample is never closed, and the fence of the next one is met inside it
        "an unclosed block that another block follows": (tree(edit(ref, "x = 4\nprint(x + 5)\n```\n\n### V2", "x = 4\nprint(x + 5)\n\n### V2")),
                                                         "a fence opens here inside it"),
        "an indented fence": (tree(edit(ref, "Nobody chose.", "Nobody chose.\n\n  ```text\ny = 1\n  ```")), "indented or made of tildes"),
        "a fence of tildes": (tree(edit(ref, "Nobody chose.", "Nobody chose.\n\n~~~\ny = 1\n~~~")), "indented or made of tildes"),
        "an indented fence of tildes": (tree(edit(ref, "Nobody chose.", "Nobody chose.\n\n  ~~~\ny = 1\n  ~~~")), "indented or made of tildes"),
        "an error-table row that misses its pattern": (tree(edit(ref, "| `assign-immutable` | V2 |", "| assign-immutable | V2 |")),
                                                       "an error-table row that does not read"),
        "a row that reads as an error id under another head is refused, not read as an id and not passed over": (
            tree(edit(ref, "## Errors\n", "| word | rules | meaning |\n|---|---|---|\n| `ghost` | V1 | elsewhere |\n\n## Errors\n")),
            "outside a table headed"),
    }
    def attempt(fn, *args):
        """The result, or the exception as a value: a broken lint then fails the case that met it instead of ending the suite."""
        try:
            return fn(*args)
        except Exception as e:
            return e

    cases = []
    def errors_of(root):
        got = attempt(lint, root, 4, 2)
        return got[0] if isinstance(got, tuple) else [f"the lint crashed: {got!r}"]

    def header(text):
        got = attempt(parse_case, text)
        return got[0] if isinstance(got, tuple) else got  # a crash is neither a header nor None

    for name, (root, needle) in trees.items():
        errors = errors_of(root)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))
    counted = attempt(lambda: lint(tree(), 4, 2)[1])
    want = {"rules": 5, "cases": 3, "errors": 1, "runtime": 1, "decided": 2, "assumed": 1, "proposed": 1, "open": 1}
    cases.append(("rules, cases and statuses are counted", counted == want, counted))
    unread = errors_of(tree(drop=("kurz-design.md",)))
    cases.append(("an unreadable record is reported once, not as sections it lacks",
                  sum("kurz-design.md cannot be read" in e for e in unread) == 1
                  and not any("which kurz-design.md does not have" in e for e in unread), unread))
    with_output = header("// expect: output\n// | a\n// | b\n// build: test\n// rules: V1, V2\n\nprint(1)\n")
    cases.append(("a header with output lines and a build is read", isinstance(with_output, dict) and with_output["rules"] == ["V1", "V2"], with_output))
    cases.append(("a build that does not exist is refused", header("// expect: throws overflow at 4\n// build: debug\n// rules: V1\n\nx\n") is None, ""))
    thrown_case = header("// expect: throws overflow at 4\n// | a\n// rules: V1\n\nx\n")
    cases.append(("a case that prints and then throws is read, with the id and the line of what it throws",
                  isinstance(thrown_case, dict) and (thrown_case["expect"], thrown_case["error"], thrown_case["line"]) == ("throws", "overflow", 4),
                  thrown_case))
    cases.append(("an error case carries no output lines", header("// expect: error unused-variable at 5\n// | a\n// rules: V1\n\nx\n") is None, ""))
    quiet = list().append  # a sync inside a case names its chapters to nobody
    stale = tree(edit(ref, "print(x + 5)\n```", "print(x + 6)\n```"))
    changed = attempt(sync, stale, quiet)
    cases.append(("--sync rewrites a stale sample from its file", changed == [ref] and not errors_of(stale), (changed, errors_of(stale))))
    bare = tree(edit(ref, "```kurz\nx = 4\nprint(x + 5)\n```\n", ""))
    cases.append(("--sync inserts a missing sample", attempt(sync, bare, quiet) == [ref] and not errors_of(bare), errors_of(bare)))
    again = ("### V6 (decided, §4) Again\n\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n```kurz\nx = 4\nprint(x + 5)\n```\n\n"
             "### V3 (open) Shadowing")
    twice = tree({**edit(ref, "### V3 (open) Shadowing", again), declare: case.replace("// rules: V1", "// rules: V1, V6")})
    cases.append(("a sample shown twice in one chapter", any("a second time" in e for e in errors_of(twice)), errors_of(twice)))
    cases.append(("--sync keeps the first sample of a file and drops the second", attempt(sync, twice, quiet) == [ref] and not errors_of(twice)
                  and kit.read(os.path.join(twice, ref)).count("print(x + 5)") == 1, errors_of(twice)))
    clean = tree()
    cases.append(("--sync leaves a current reference alone", attempt(sync, clean, quiet) == [] and kit.read(os.path.join(clean, ref)) == chapter, ""))
    first = "reference/01-source.md"  # sorts before the chapter that refuses: a write made before the refusal shows in it
    extra = ("# Source\n\n### S1 (decided, §1) Files\n\nA file.\n\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n"
             "```kurz\nx = 4\nprint(x + 6)\n```\n")
    unclosed = tree({**edit(ref, "print(x)\nx = 5\n```\n", "print(x)\nx = 5\n"), first: extra})
    before = {p: kit.read(os.path.join(unclosed, p)) for p in (first, ref)}
    refusal = attempt(sync, unclosed, quiet)
    cases.append(("--sync refuses a sample that is never closed, and writes nothing, not even the chapter before it",
                  isinstance(refusal, kit.Refused) and all(kit.read(os.path.join(unclosed, p)) == t for p, t in before.items()),
                  repr(refusal)))
    # the first sample is never closed and a later block is: the later fence must not be taken as the end of the first
    followed = tree(edit(ref, "x = 4\nprint(x + 5)\n```\n\n### V2", "x = 4\nprint(x + 5)\n\n### V2"))
    before, refusal = kit.read(os.path.join(followed, ref)), attempt(sync, followed, quiet)
    cases.append(("--sync refuses an unclosed sample that a later sample follows, and drops nothing between them",
                  isinstance(refusal, kit.Refused) and kit.read(os.path.join(followed, ref)) == before, (repr(refusal), errors_of(followed))))
    # inside a rule, above the file's own Case line: read as a Case line, it would carry the sample into the block
    inside = tree(edit(ref, "A name.\n", "A name.\n\n```text\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n```\n"))
    cases.append(("--sync leaves a Case line inside a code block alone", attempt(sync, inside, quiet) == [] and not errors_of(inside),
                  errors_of(inside)))
    outside = tree(edit(ref, "# Variables\n", "# Variables\n\nCase: [vars/declare.kz](../corpus/vars/declare.kz)\n"))
    cases.append(("--sync adds no sample under a Case line outside a rule", attempt(sync, outside, quiet) == [],
                  kit.read(os.path.join(outside, ref))[:120]))
    misnamed = tree(edit(ref, "[vars/declare.kz](../corpus/vars/declare.kz)\n```kurz\nx = 4\nprint(x + 5)",
                         "[the case](../corpus/vars/declare.kz)\n```kurz\nx = 4\nprint(x + 6)"))
    cases.append(("--sync leaves the sample under a Case line whose text is not the path alone", attempt(sync, misnamed, quiet) == [], ""))
    two = tree({**edit(ref, "print(x + 5)\n```", "print(x + 6)\n```"), first: extra})  # both chapters hold a stale sample
    said, real = [], os.replace

    def failing(src, dst):
        if dst.replace(os.sep, "/").endswith(ref):
            raise OSError("disk full")
        return real(src, dst)
    os.replace = failing
    try:
        refusal = attempt(sync, two, said.append)
    finally:
        os.replace = real
    left = [f for f in os.listdir(os.path.join(two, "reference")) if f.endswith(".tmp")]
    cases.append(("--sync names each chapter as it writes it, and a failed write is a refusal that names what was written",
                  isinstance(refusal, kit.Refused) and f"wrote only 1 of 2 chapters ({first})" in str(refusal)
                  and said == [f"rewrote the samples of {first}"], (repr(refusal), said)))
    cases.append(("--sync leaves no temporary file behind a failed write", isinstance(refusal, kit.Refused) and left == [], left))
    two = tree({**edit(ref, "print(x + 5)\n```", "print(x + 6)\n```"), first: extra})
    made, real_mkstemp = [], tempfile.mkstemp

    def no_room(*args, **kwargs):
        made.append(1)
        if len(made) == 2:
            raise OSError("no space left on device")
        return real_mkstemp(*args, **kwargs)
    tempfile.mkstemp = no_room
    try:
        refusal = attempt(sync, two, quiet)
    finally:
        tempfile.mkstemp = real_mkstemp
    cases.append(("--sync: a temporary file that cannot be made is the same refusal, naming the chapter written before it",
                  isinstance(refusal, kit.Refused) and f"wrote only 1 of 2 chapters ({first})" in str(refusal), repr(refusal)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    if "--sync" in sys.argv:
        try:
            sync(kit.ROOT)  # it names each chapter as it writes it
        except kit.Refused as e:
            print(f"REFUSED: {e}")
            sys.exit(1)
    errors, counted = lint(kit.ROOT)
    for e in errors:
        print("ERROR:", e)
    print(f"reference lint: {counted['rules']} rules ({counted['decided']} decided, {counted['assumed']} assumed, {counted['proposed']} proposed, "
          f"{counted['open']} open), {counted['cases']} cases, {counted['errors']} compile-error ids, "
          f"{counted['runtime']} run-time error ids, {len(errors)} errors")
    sys.exit(1 if errors else 0)
