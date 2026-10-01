#!/usr/bin/env python3
"""Every decision tool proves itself, and keeps proving it. This runs each tool's `--self-test` and
then replays the recorded red proofs: for every entry of tools/red_proofs.json it breaks the tool
the way the entry says, runs the self-test again and expects the named case to FAIL.

Fails on: a tool (a .py under tools/ with a `__main__` entry) that has no `--self-test`; a
self-test that fails or ran no case; a tool with no recorded red proof; an entry whose anchor does
not occur exactly once in its file; a mutation that no longer turns the named case red (the gate
went soft, or the proof went stale); an entry for a tool that does not exist; fewer than FLOOR tools.

An entry: {"tool": path of the tool, "file": the file to break (default: the tool), "anchor": text
that occurs exactly once, "replacement": what it becomes, "expect": text of the case that must fail}."""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

FLOOR = 6
LEDGER = "tools/red_proofs.json"


def tools_in(root):
    found = []
    for d, _, fs in os.walk(os.path.join(root, "tools")):
        for f in sorted(fs):
            path = os.path.join(d, f)
            if f.endswith(".py") and '__name__ == "__main__"' in kit.read(path):
                found.append(os.path.relpath(path, root).replace(os.sep, "/"))
    return sorted(found)


def self_test_of(root, tool):
    """(exit code, output) of one tool's self-test, run inside `root`."""
    try:
        p = subprocess.run([sys.executable, os.path.join(root, tool), "--self-test"], cwd=root, capture_output=True,
                           text=True, encoding="utf-8", errors="replace", timeout=600)
    except (OSError, subprocess.TimeoutExpired) as e:
        return 99, f"{type(e).__name__}: {e}"
    return p.returncode, p.stdout + p.stderr


def check(root, floor=FLOOR):
    """(errors, {tool: {"cases": n, "proofs": n}})."""
    errors, stats = [], {}
    tools = tools_in(root)
    if len(tools) < floor:
        errors.append(f"only {len(tools)} tools found under tools/, floor is {floor}: is this the right tree?")
    try:
        with open(os.path.join(root, LEDGER), encoding="utf-8") as f:
            ledger = json.load(f)
    except (OSError, ValueError) as e:
        return errors + [f"{LEDGER} cannot be read: {type(e).__name__}"], stats
    for tool in tools:
        stats[tool] = {"cases": 0, "proofs": sum(e.get("tool") == tool for e in ledger)}
        if '"--self-test"' not in kit.read(os.path.join(root, tool)):
            errors.append(f"{tool}: a decision tool without a --self-test")
            continue
        code, out = self_test_of(root, tool)
        ran = re.search(r"^(\d+) cases, (\d+) failed$", out, re.M)
        stats[tool]["cases"] = int(ran.group(1)) if ran else 0
        if code != 0 or not ran or int(ran.group(1)) == 0:
            errors.append(f"{tool}: its self-test does not pass (exit {code}): {out.strip()[-300:]}")
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
        try:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(original.replace(entry["anchor"], entry["replacement"]))
            code, out = self_test_of(root, tool)
        finally:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(original)
        red = [line for line in out.splitlines() if line.startswith("FAIL") and entry["expect"] in line]
        if code == 0 or not red:
            errors.append(f"{label}: the mutation no longer turns that case red (exit {code})")
    return errors, stats


def copy_of_tree():
    """A scratch copy of the repository, so that a mutation never touches the working tree."""
    tmp = tempfile.mkdtemp(prefix="red-proof-")
    shutil.copytree(kit.ROOT, os.path.join(tmp, "tree"), ignore=shutil.ignore_patterns(".git", "__pycache__"))
    return os.path.join(tmp, "tree")


def self_test():
    toy = ('import sys\n'
           'def add(a, b):\n    return a + b\n'
           'if __name__ == "__main__":\n'
           '    if "--self-test" in sys.argv:\n'
           '        ok = add(1, 1) == 2\n'
           '        print("ok  " if ok else "FAIL", "adds")\n'
           '        print(f"1 cases, {0 if ok else 1} failed")\n'
           '        sys.exit(0 if ok else 1)\n')
    proof = {"tool": "tools/toy.py", "anchor": "return a + b", "replacement": "return a - b", "expect": "adds"}

    def tree(files, ledger):
        root = tempfile.mkdtemp()
        os.mkdir(os.path.join(root, "tools"))
        for name, text in {**files, "red_proofs.json": json.dumps(ledger)}.items():
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
        "a proof that names another case": (tree({"toy.py": toy}, [{**proof, "expect": "subtracts"}]), "no longer turns"),
        "an anchor that is gone": (tree({"toy.py": toy}, [{**proof, "anchor": "return a * b"}]), "occurs 0 times"),
        "an anchor that occurs twice": (tree({"toy.py": toy}, [{**proof, "anchor": "a"}]), "times in tools/toy.py, expected 1"),
        "a proof for a tool that is not there": (tree({"toy.py": toy}, [proof, {**proof, "tool": "tools/gone.py"}]), "not a tool here"),
    }
    broken = tree({"toy.py": toy}, [proof])
    with open(os.path.join(broken, LEDGER), "w", encoding="utf-8") as f:
        f.write("[")
    trees["a ledger that is not JSON"] = (broken, "cannot be read")
    cases = []
    for name, (root, needle) in trees.items():
        errors, _ = check(root, floor=1)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))
    root = tree({"toy.py": toy}, [proof])
    check(root, floor=1)
    cases.append(("the mutated file is restored", kit.read(os.path.join(root, "tools", "toy.py")) == toy, ""))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    errors, stats = check(copy_of_tree())
    for tool, s in stats.items():
        print(f"{tool}: {s['cases']} self-test cases, {s['proofs']} red proofs")
    for e in errors:
        print("ERROR:", e)
    print(f"self-tests: {len(stats)} tools, {sum(s['cases'] for s in stats.values())} cases, "
          f"{sum(s['proofs'] for s in stats.values())} red proofs replayed, {len(errors)} errors")
    sys.exit(1 if errors else 0)
