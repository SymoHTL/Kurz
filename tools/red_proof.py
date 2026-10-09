#!/usr/bin/env python3
"""Every decision tool proves itself, and keeps proving it. This runs each tool's `--self-test` and
then replays the recorded red proofs: for every entry of tools/red_proofs.json it breaks the tool
the way the entry says, runs the self-test again and expects the named case to FAIL.

Fails on: a tool (a .py under tools/ with a `__main__` entry, in either quote) that has no
`--self-test`; a self-test that fails, ran no case, printed no summary line, or counts a failed
case and exits 0; a tool with no recorded red proof; an entry whose anchor does not occur exactly
once in its file, or whose file cannot be read; an entry whose `expect` does not name exactly one
case of the unmutated self-test, or names one that is already red; a mutation that no longer turns
that case red (the gate went soft, or the proof went stale); an entry for a tool that does not
exist; fewer than FLOOR tools; a ledger that is not written as `--format` writes it (one entry per
line, two spaces in, text as it is), since a ledger written back another way differs on every
line, and the review reads and bills the whole file as a change.

An entry: {"tool": path of the tool, "file": the file to break (default: the tool), "anchor": text
that occurs exactly once, "replacement": what it becomes, "expect": the name of the case that must
fail, or a part of it that no other case shares}."""
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

FLOOR = 11  # the tools this tree holds; a self-test case keeps it at that number
LEDGER = "tools/red_proofs.json"
MAIN = re.compile(r"""__name__\s*==\s*["']__main__["']""")
SELF_TEST = re.compile(r"""["']--self-test["']""")
SUMMARY = re.compile(r"^(\d+) cases, (\d+) failed$", re.M)


def tools_in(root):
    found = []
    for d, _, fs in os.walk(os.path.join(root, "tools")):
        for f in sorted(fs):
            path = os.path.join(d, f)
            if f.endswith(".py") and MAIN.search(kit.read(path)):
                found.append(os.path.relpath(path, root).replace(os.sep, "/"))
    return sorted(found)


def self_test_of(root, tool):
    """(exit code, output) of one tool's self-test, run inside `root`, with its temporary files beside
    `root`, in `tmp` next to it: what a mutant leaves behind (a mutation that disables a removal does)
    goes with the directory the replay removes, instead of piling up in the system's temporary
    directory, and lies outside the copy a suite reads as the tree, so no suite reads a leftover of
    an earlier one. A directory that cannot be made is a failed run (99), like a process that could
    not start. No bytecode is written: Python takes a cached module for current when the source has
    the same size and the same second of modification, so two equally long mutations of a shared
    module read as the first one."""
    scratch = os.path.join(os.path.dirname(root), "tmp")
    try:
        os.makedirs(scratch, exist_ok=True)
        p = subprocess.run([sys.executable, os.path.join(root, tool), "--self-test"], cwd=root, capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=600,
                           env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1", "TMPDIR": scratch, "TEMP": scratch, "TMP": scratch})
    except (OSError, subprocess.TimeoutExpired) as e:
        return 99, f"{type(e).__name__}: {e}"
    return p.returncode, p.stdout + p.stderr


def case_lines(out):
    """The lines kit.report printed for the cases: `ok   <name>` or `FAIL <name> <detail>`."""
    return [line for line in out.splitlines() if line.startswith(("ok   ", "FAIL "))]


def named(lines, expect):
    """The case lines `expect` names: the case of exactly that name, else every case that holds the text."""
    if not expect.strip():
        return []
    return [line for line in lines if line[5:].strip() == expect] or [line for line in lines if expect in line[5:]]


def failed(out, green):
    """The names of the cases a run printed as FAIL, out of the cases that were ok in `green`. A FAIL
    line carries its detail after the name, so it belongs to the longest name it starts with."""
    names = [line[5:].strip() for line in green if line.startswith("ok")]
    red = set()
    for line in case_lines(out):
        fits = [n for n in names if line.startswith("FAIL ") and (line[5:].rstrip() == n or line[5:].startswith(n + " "))]
        if fits:
            red.add(max(fits, key=len))
    return red


def canonical(ledger):
    """The ledger's one form: one entry per line, two spaces in, text as it is (no escaped characters),
    so that a diff shows the entries that changed. A ledger written back another way (an indent, escaped
    text) differs on every line, and a review reads and bills the whole file as a change (2026-10-09)."""
    return "[\n" + ",\n".join("  " + json.dumps(e, ensure_ascii=False) for e in ledger) + "\n]\n"


def format_ledger(root):
    """Rewrite the ledger in its form; True when the file changed."""
    path = os.path.join(root, LEDGER)
    with open(path, encoding="utf-8") as f:
        text = f.read()
    want = canonical(json.loads(text))
    if want != text:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(want)
    return want != text


def check(root, floor=FLOOR):
    """(errors, {tool: {"cases": n, "proofs": n}})."""
    errors, stats, green = [], {}, {}
    tools = tools_in(root)
    if len(tools) < floor:
        errors.append(f"only {len(tools)} tools found under tools/, floor is {floor}: is this the right tree?")
    try:
        with open(os.path.join(root, LEDGER), encoding="utf-8") as f:
            text = f.read()
        ledger = json.loads(text)
    except (OSError, ValueError) as e:
        return errors + [f"{LEDGER} cannot be read: {type(e).__name__}"], stats
    if text != canonical(ledger):
        errors.append(f"{LEDGER} is not written one entry per line, two spaces in, text as it is: a ledger written back "
                      f"another way differs on every line, and the review reads and bills the whole file; "
                      f"`py -3 tools/red_proof.py --format` writes it in its form")
    for tool in tools:
        stats[tool] = {"cases": 0, "proofs": sum(e.get("tool") == tool for e in ledger)}
        if not SELF_TEST.search(kit.read(os.path.join(root, tool))):
            errors.append(f"{tool}: a decision tool without a --self-test")
            continue
        code, out = self_test_of(root, tool)
        green[tool] = case_lines(out)
        ran = SUMMARY.findall(out)
        count, lost = (int(n) for n in ran[-1]) if ran else (0, 0)  # the last summary is the run's own
        stats[tool]["cases"] = count
        if code == 0 and lost:
            errors.append(f"{tool}: its self-test counts {lost} failed cases and exits 0")
        elif code != 0 or not count:
            said = "; ".join(line.strip() for line in out.splitlines() if line.startswith("FAIL"))
            errors.append(f"{tool}: its self-test does not pass (exit {code}): {said[:300] or out.strip()[-300:]}")
        if not stats[tool]["proofs"]:
            errors.append(f"{tool}: no recorded red proof in {LEDGER}")
    for n, entry in enumerate(ledger, 1):
        label = f"{LEDGER} entry {n} ({entry.get('expect', '?')})"
        tool, target = entry.get("tool"), entry.get("file") or entry.get("tool")
        if tool not in tools:
            errors.append(f"{label}: names {tool}, which is not a tool here")
            continue
        path = os.path.join(root, target)
        try:
            original = kit.read(path)
        except OSError:
            errors.append(f"{label}: {target} cannot be read")
            continue
        if original.count(entry["anchor"]) != 1:
            errors.append(f"{label}: the anchor occurs {original.count(entry['anchor'])} times in {target}, expected 1")
            continue
        hit = named(green.get(tool, []), entry.get("expect", ""))
        if len(hit) != 1:
            errors.append(f"{label}: the expect text names {len(hit)} cases of the unmutated self-test, it must name one")
            continue
        if not hit[0].startswith("ok"):
            errors.append(f"{label}: that case is already red before the mutation, so the mutation proves nothing")
            continue
        try:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(original.replace(entry["anchor"], entry["replacement"]))
            code, out = self_test_of(root, tool)
        finally:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(original)
        if code == 0 or hit[0][5:].strip() not in failed(out, green[tool]):
            errors.append(f"{label}: the mutation no longer turns that case red (exit {code})")
    return errors, stats


def copy_of_tree(into):
    """A copy of the repository under `into`, so that a mutation never touches the working tree."""
    target = os.path.join(into, "tree")
    shutil.copytree(kit.ROOT, target, ignore=shutil.ignore_patterns(".git", "__pycache__"))
    return target


def replay():
    """check() on a scratch copy of the repository, which is removed afterwards, whatever check() ended
    in; a removal that fails is said with the path, never raised (kit.scratch)."""
    with kit.scratch("red-proof-") as tmp:
        return check(copy_of_tree(tmp))


def self_test():
    with kit.scratch("red-proof-cases-") as base:
        return cases_in(base)


def cases_in(base):
    """The cases of self_test, every tree a directory under `base`."""
    toy = ('import sys\n'
           'def add(a, b):\n    return a + b\n'
           'if __name__ == "__main__":\n'
           '    if "--self-test" in sys.argv:\n'
           '        ok = add(1, 1) == 2\n'
           '        print("ok  " if ok else "FAIL", "adds")\n'
           '        print(f"1 cases, {0 if ok else 1} failed")\n'
           '        sys.exit(0 if ok else 1)\n')
    proof = {"tool": "tools/toy.py", "anchor": "return a + b", "replacement": "return a - b", "expect": "adds"}
    # two cases, and the name of the first is the start of the name of the second
    pair = ('import sys\n'
            'def add(a, b):\n    return a + b\n'
            'if __name__ == "__main__":\n'
            '    if "--self-test" in sys.argv:\n'
            '        ok, more = add(1, 1) == 2, add(2, 2) == 4\n'
            '        print("ok  " if ok else "FAIL", "adds")\n'
            '        print("ok  " if more else "FAIL", "adds twice")\n'
            '        print(f"2 cases, {2 - ok - more} failed")\n'
            '        sys.exit(0 if ok and more else 1)\n')
    # kit.report prints a failed case as `FAIL <name> <detail>`: this toy does the same
    detailed = pair.replace('print("ok  " if ok else "FAIL", "adds")', 'print("ok   adds" if ok else f"FAIL adds got {add(1, 1)}")')
    quiet = toy.replace('        print(f"1 cases, {0 if ok else 1} failed")\n', "")
    lying = toy.replace("== 2", "== 3").replace("sys.exit(0 if ok else 1)", "sys.exit(0)")
    late = toy.replace('        print(f"1 cases, {0 if ok else 1} failed")\n',
                       '        print("1 cases, 0 failed")\n        print("1 cases, 1 failed")\n')
    assert quiet != toy and lying != toy and late != toy

    def tree(files, ledger):
        root = tempfile.mkdtemp(dir=base)
        os.mkdir(os.path.join(root, "tools"))
        for name, text in {**files, "red_proofs.json": canonical(ledger)}.items():
            with open(os.path.join(root, "tools", name), "w", encoding="utf-8") as f:
                f.write(text)
        return root

    trees = {
        "a tool with its proof passes": (tree({"toy.py": toy}, [proof]), None),
        "floor catches an empty scan": (tree({}, []), "floor"),
        "a tool without a self-test": (tree({"toy.py": toy.replace('"--self-test"', '"--check"')}, [proof]), "without a --self-test"),
        "a self-test that fails": (tree({"toy.py": toy.replace("== 2", "== 3")}, [proof]), "does not pass"),
        "a self-test that ran no case": (tree({"toy.py": toy.replace("1 cases", "0 cases")}, [proof]), "does not pass"),
        "a tool with no recorded proof": (tree({"toy.py": toy}, []), "no recorded red proof"),
        "a mutation that changes nothing": (tree({"toy.py": toy}, [{**proof, "replacement": "return b + a"}]), "no longer turns"),
        "a proof that names a case the self-test does not have": (tree({"toy.py": toy}, [{**proof, "expect": "subtracts"}]), "names 0 cases"),
        "an anchor that is gone": (tree({"toy.py": toy}, [{**proof, "anchor": "return a * b"}]), "occurs 0 times"),
        "an anchor that occurs twice": (tree({"toy.py": toy}, [{**proof, "anchor": "a"}]), "times in tools/toy.py, expected 1"),
        "a proof for a tool that is not there": (tree({"toy.py": toy}, [proof, {**proof, "tool": "tools/gone.py"}]), "not a tool here"),
        "a tool written with single quotes is a tool": (tree({"toy.py": toy.replace('"__main__"', "'__main__'")}, []), "no recorded red proof"),
        "a self-test asked for with single quotes is one": (tree({"toy.py": toy.replace('"--self-test"', "'--self-test'")}, [proof]), None),
        "a self-test that exits 1 does not pass, whatever it prints":
            (tree({"toy.py": toy.replace("sys.exit(0 if ok else 1)", "sys.exit(1)")}, [proof]), "does not pass (exit 1)"),
        "a self-test that prints no summary line does not pass": (tree({"toy.py": quiet}, [proof]), "does not pass (exit 0)"),
        "a self-test that counts a failed case and exits 0": (tree({"toy.py": lying}, [proof]), "counts 1 failed cases and exits 0"),
        "the last summary line is the one that counts": (tree({"toy.py": late}, [proof]), "counts 1 failed cases and exits 0"),
        "a proof whose file cannot be read": (tree({"toy.py": toy}, [proof, {**proof, "file": "tools/gone.py"}]), "tools/gone.py cannot be read"),
        "an expect that is the name of one case names it, although another case holds the text": (tree({"toy.py": pair}, [proof]), None),
        "a FAIL line that carries a detail after the name is that case's": (tree({"toy.py": detailed}, [proof]), None),
        "an expect that fits two cases names none": (tree({"toy.py": pair}, [{**proof, "expect": "add"}]), "names 2 cases"),
        "an empty expect names no case": (tree({"toy.py": toy}, [{**proof, "expect": ""}]), "names 0 cases"),
        "a proof for a case that is already red proves nothing":
            (tree({"toy.py": toy.replace("== 2", "== 3")}, [proof]), "already red before the mutation"),
        # this mutation turns "adds twice" red and leaves "adds" green
        "a mutation that turns another case red does not prove the named one":
            (tree({"toy.py": pair}, [{**proof, "replacement": "return a + b if a != 2 else 9"}]), "no longer turns"),
    }
    broken = tree({"toy.py": toy}, [proof])
    with open(os.path.join(broken, LEDGER), "w", encoding="utf-8") as f:
        f.write("[")
    trees["a ledger that is not JSON"] = (broken, "cannot be read")
    reformatted = tree({"toy.py": toy}, [proof])
    with open(os.path.join(reformatted, LEDGER), "w", encoding="utf-8") as f:
        json.dump([proof], f, indent=1)  # the same entries, every line different: what a one-off script wrote on 2026-10-09
    trees["a ledger written another way is refused, with the option that writes it in its form"] = (reformatted, "not written one entry per line")
    cases = []
    for name, (root, needle) in trees.items():
        errors, _ = check(root, floor=1)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))
    changed = format_ledger(reformatted)
    errors, _ = check(reformatted, floor=1)
    cases.append(("--format writes the ledger in its form, after which the check passes, and a second run changes nothing",
                  changed and not errors and not format_ledger(reformatted) and kit.read(os.path.join(reformatted, LEDGER)) == canonical([proof]),
                  (changed, errors)))
    two = [proof, {**proof, "expect": "adds \u00e9"}]
    cases.append(("the form is one entry per line, two spaces in, text as it is, so a diff shows the entries that changed",
                  canonical(two) == '[\n  {"tool": "tools/toy.py", "anchor": "return a + b", "replacement": "return a - b", "expect": "adds"},\n'
                                    '  {"tool": "tools/toy.py", "anchor": "return a + b", "replacement": "return a - b", "expect": "adds \u00e9"}\n]\n',
                  canonical(two)))
    root = tree({"toy.py": toy}, [proof])
    errors, _ = check(root, floor=1)  # no error: the replay did write the mutation before it restored the file
    cases.append(("the mutation is written before the self-test runs, and the file is restored after it",
                  not errors and kit.read(os.path.join(root, "tools", "toy.py")) == toy, errors))
    # a self-test that leaves a temporary directory behind on purpose, as a mutant that disables a removal does
    leaky = toy.replace("import sys\n", "import sys, tempfile\n").replace(
        "        ok = add(1, 1) == 2\n", "        tempfile.mkdtemp(prefix=\"red-proof-leak-\")\n        ok = add(1, 1) == 2\n")
    assert leaky != toy
    root = tree({"toy.py": leaky}, [proof])
    errors, _ = check(root, floor=1)
    outside = glob.glob(os.path.join(tempfile.gettempdir(), "red-proof-leak-*"))
    for leaked in outside:  # not under this suite's directory: removed here, so that a red case leaves nothing behind
        shutil.rmtree(leaked, ignore_errors=True)
    beside = glob.glob(os.path.join(os.path.dirname(root), "tmp", "red-proof-leak-*"))
    under = glob.glob(os.path.join(root, "**", "red-proof-leak-*"), recursive=True)
    cases.append(("a self-test's temporary files are made beside the copy, one run green and one mutated, none under the copy a suite reads and none in the system's own",
                  not errors and len(beside) == 2 and not under and not outside, (errors, beside, under, outside)))
    seen = []

    def small_copy(into):
        target = os.path.join(into, "tree")
        os.mkdir(target)
        return target

    def noting_check(tree_root, floor=FLOOR):
        seen.append(tree_root if os.path.isdir(tree_root) else None)
        return [], {}

    g = globals()
    saved = g["check"], g["copy_of_tree"]
    g["check"], g["copy_of_tree"] = noting_check, small_copy
    try:
        replayed = replay()
    finally:
        g["check"], g["copy_of_tree"] = saved
    cases.append(("a replay removes the copy it checked",
                  replayed == ([], {}) and len(seen) == 1 and bool(seen[0]) and not os.path.exists(os.path.dirname(seen[0])), seen))
    errors, _ = check(tree({"toy.py": toy}, [proof]))
    cases.append(("one tool is below the real floor", any("floor is" in e for e in errors), errors))
    here = tools_in(kit.ROOT)
    cases.append(("the floor is the number of tools this tree holds, so a tool that drops out is seen", len(here) == FLOOR, here))
    # A tool that imports a module: with bytecode on, the run would leave the module's cache behind.
    shared = toy.replace("import sys\n", "import sys\nimport helper\n").replace("add(1, 1) == 2", "helper.two() == 2")
    root = tree({"toy.py": shared, "helper.py": "def two():\n    return 2\n"},
                [{**proof, "file": "tools/helper.py", "anchor": "return 2", "replacement": "return 3"}])
    inherited = os.environ.pop("PYTHONDONTWRITEBYTECODE", None)  # the machine may have switched bytecode off by itself
    try:
        errors, stats = check(root, floor=1)
    finally:
        if inherited is not None:
            os.environ["PYTHONDONTWRITEBYTECODE"] = inherited
    cached = [d for d, _, _ in os.walk(root) if os.path.basename(d) == "__pycache__"]
    cases.append(("a run leaves no cached module for the next mutation to be read through",
                  not errors and stats["tools/toy.py"]["cases"] == 1 and not cached, (errors, cached)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    if "--format" in sys.argv:
        print(f"{LEDGER}: " + ("rewritten in its form" if format_ledger(kit.ROOT) else "already in its form"))
        sys.exit(0)
    errors, stats = replay()
    for tool, s in stats.items():
        print(f"{tool}: {s['cases']} self-test cases, {s['proofs']} red proofs")
    for e in errors:
        print("ERROR:", e)
    print(f"self-tests: {len(stats)} tools, {sum(s['cases'] for s in stats.values())} cases, "
          f"{sum(s['proofs'] for s in stats.values())} red proofs replayed, {len(errors)} errors")
    sys.exit(1 if errors else 0)
