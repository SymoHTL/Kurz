#!/usr/bin/env python3
"""What may exist in this public tree. Fails on: a path the design phase does not allow, a
credential-shaped string, a machine-bound string (drive or profile path, non-loopback IP address,
e-mail address), a merge-conflict marker, a file that is not UTF-8 text, or a scan of fewer than
FLOOR files.

Three callers, one definition:
  (no flag)    every tracked or untracked-but-not-ignored file of the working tree  (CI, local gates)
  --pre-push   every commit a `git push` is about to publish, messages included     (.githooks/pre-push)
  --hook       one Write/Edit call of an agent session, path rule only              (.claude/settings.json)
`--self-test` plants one case per rule and per pattern, plus precision cases that must stay clean.
In --hook mode only exit 2 blocks the call, so every failure path there ends in exit 2."""
import contextlib
import io
import json
import os
import re
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

# Every other rule is satisfied by finding nothing; a scan of the wrong directory prints "0 errors".
FLOOR = 5
# What the design phase allows, as full-path patterns. Anything else is refused.
ALLOWED = [
    r"CLAUDE\.md", r"INDEX\.md", r"README\.md", r"LICENSE(?:\.md)?", r"kurz-design\.md",
    r"\.gitattributes", r"\.gitignore",
    r"knowledge/[^/]+\.md", r"guides/[^/]+\.md",
    r"reference/[^/]+\.md", r"corpus/(?:[^/]+/)*[^/]+\.kz",
    r"tools/(?:[^/]+/)*[^/]+\.(?:py|json|txt)",
    r"\.review/[^/]+\.yaml", r"\.github/workflows/[^/]+\.yml", r"\.githooks/pre-push",
    r"\.claude/settings\.json", r"\.claude/skills/[^/]+/SKILL\.md",
]
PHASE = ("design phase: this tree holds the design record, the reference, the corpus, the knowledge store "
         "and the quality tools. No compiler, runtime or library code until Simon says build (CLAUDE.md)")


def check_path(path):
    """The reason a path is refused, or None."""
    return None if any(re.fullmatch(p, path) for p in ALLOWED) else f"{path}: not allowed here - {PHASE}"


def check_text(where, text):
    errors = [f"{where}: credential-shaped string ({label})" for label, p in kit.SECRETS.items() if re.search(p, text)]
    errors += [f"{where}: machine-bound string ({label})" for label, p in kit.MACHINE.items() if re.search(p, text)]
    if kit.CONFLICT.search(text):
        errors.append(f"{where}: merge-conflict marker")
    return errors


def check_file(path, data):
    """Errors for one file, given its repository path and its bytes."""
    reason = check_path(path)
    errors = [reason] if reason else []
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return errors + [f"{path}: not UTF-8 text"]
    return errors + check_text(path, text)


def scan(files, floor=FLOOR):
    """files: {repository path: bytes}."""
    errors = [e for path in sorted(files) for e in check_file(path, files[path])]
    if len(files) < floor:
        errors.append(f"only {len(files)} files scanned, floor is {floor}: is this the right tree?")
    return errors


def working_tree():
    listed = kit.run(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"]).split("\0")
    files = {}
    for path in filter(None, listed):
        full = os.path.join(kit.ROOT, path)
        if os.path.isfile(full):  # a tracked file that is gone is being deleted
            with open(full, "rb") as f:
                files[path] = f.read()
    return files


def pushed_commits(remote, stdin_text):
    """The commits a push would publish: reachable from the pushed refs, on no ref of that remote yet."""
    commits = []
    for line in stdin_text.splitlines():
        parts = line.split()
        if len(parts) != 4:
            raise kit.Refused(f"pre-push line not understood: {line!r}")
        local_sha = parts[1]
        if set(local_sha) == {"0"}:  # a ref being deleted publishes nothing
            continue
        commits += kit.run(["git", "rev-list", local_sha, "--not", f"--remotes={remote}"]).split()
    return list(dict.fromkeys(commits))


def check_commit(sha):
    errors = check_text(f"{sha[:8]} (message)", kit.run(["git", "log", "-1", "--format=%B", sha]))
    names = kit.run(["git", "show", "--format=", "--name-only", "--diff-filter=ACMR", "-z", sha]).split("\0")
    for path in filter(None, names):
        blob = kit.run(["git", "show", f"{sha}:{path}"])
        errors += [f"{sha[:8]} {e}" for e in check_file(path, blob.encode("utf-8"))]
    return errors


def hook_reason(raw):
    """The deny reason for one Write/Edit call, or None to allow it. Raises on input it cannot read."""
    tool_input = json.loads(raw)["tool_input"]
    target = tool_input.get("file_path") or tool_input["notebook_path"]
    full = os.path.normcase(os.path.realpath(target))
    root = os.path.normcase(os.path.realpath(kit.ROOT))
    if full != root and not full.startswith(root + os.sep):
        return None  # outside this repository: not this gate's business
    # Case as written, so the allowlist is not matched against a lower-cased Windows path.
    rel = os.path.relpath(os.path.realpath(target), os.path.realpath(kit.ROOT)).replace(os.sep, "/")
    return None if rel.startswith(".git/") else check_path(rel)


def hook(raw):
    try:
        why = hook_reason(raw)
    except Exception as e:  # unreadable input is a refusal, never a pass
        why = f"tree gate could not read the call: {type(e).__name__}"
    if why:
        print(why, file=sys.stderr)
        return 2
    return 0


def self_test():
    ok_files = {f"knowledge/e{n}.md": b"fact\n" for n in range(5)}
    # Built by concatenation, so this file never holds a string its own scan would refuse.
    secrets = {"gitlab-token": "glpat-" + "A" * 16, "github-token": "ghp_" + "a" * 36,
               "api-key": "sk-" + "a" * 24, "aws-key": "AKIA" + "B" * 16,
               "private-key": "BEGIN RSA " + "PRIVATE KEY", "bearer": "Bearer " + "c" * 30}
    machine = {"drive-path": "D:" + "/work/notes", "profile-path": "/c/" + "Users/someone",
               "ip-address": "10.1." + "2.3", "email": "someone@" + "mailhost.org"}
    cases = []

    def expect(name, files, needle, floor=5):
        errors = scan(files, floor)
        hit = any(needle in e for e in errors) if needle else not errors
        cases.append((name, hit, errors))

    expect("clean tree passes", ok_files, None)
    expect("floor catches an empty scan", {}, "floor")
    expect("compiler source is refused", {**ok_files, "Program.cs": b"class P {}\n"}, "Program.cs: not allowed")
    expect("Kurz library code outside the corpus is refused", {**ok_files, "src/list.kz": b"x = 1\n"}, "not allowed")
    expect("nested knowledge entry is refused", {**ok_files, "knowledge/sub/x.md": b"x\n"}, "not allowed")
    expect("a binary file is refused", {**ok_files, "tools/x.py": b"\xff\xfe\x00"}, "not UTF-8")
    expect("conflict marker", {**ok_files, "CLAUDE.md": ("<" * 7 + " HEAD\n").encode()}, "merge-conflict")
    for path in ("CLAUDE.md", "kurz-design.md", "reference/03-values.md", "corpus/values/with/path-write.kz",
                 "tools/review/review.py", "tools/review/fixtures/a.json", ".github/workflows/gates.yml",
                 ".review/review-rules.yaml", ".claude/skills/change-walk/SKILL.md", ".githooks/pre-push"):
        cases.append((f"allowed path: {path}", check_path(path) is None, check_path(path)))
    for label, value in secrets.items():  # one plant per pattern; the error must name that pattern
        expect(f"secret: {label}", {**ok_files, "guides/g.md": value.encode()}, f"credential-shaped string ({label})")
    for label, value in machine.items():
        expect(f"machine-bound: {label}", {**ok_files, "guides/g.md": value.encode()}, f"machine-bound string ({label})")
    for label, value in {**secrets, **machine}.items():  # a plant two patterns catch hides one switched off
        hits = [e for e in check_text("x", value)]
        cases.append((f"only its own pattern fires: {label}", len(hits) == 1, hits))
    unplanted = (set(kit.SECRETS) | set(kit.MACHINE)) ^ (set(secrets) | set(machine))
    cases.append(("every pattern has a plant", not unplanted, sorted(unplanted)))
    portable = ("task-tracking-belongs-in-the-tracker, version 3.12.1 and v1.0.0.0, 127.0.0.1, "
                "https://github.com/a/b, actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1, "
                "@anthropic-ai/claude-code@2.1.283, user@example.com, noreply@anthropic.com, git@github.com:a/b.git, "
                "e.g.: a/b, 20:46:53+02:00, ~/.claude/notes")
    cases.append(("portable words stay clean", not check_text("x", portable), check_text("x", portable)))

    call = lambda path: json.dumps({"tool_name": "Write", "tool_input": {"file_path": path}})
    inside = lambda rel: os.path.join(kit.ROOT, *rel.split("/"))
    for name, raw, want in [
        ("hook: compiler source inside the repo is denied", call(inside("src/Lexer.cs")), 2),
        ("hook: the design record is allowed", call(inside("kurz-design.md")), 0),
        ("hook: a corpus case is allowed", call(inside("corpus/values/a.kz")), 0),
        ("hook: a file outside the repo is not this gate's business", call(os.path.join(tempfile.gettempdir(), "x.cs")), 0),
        ("hook: unparseable stdin is denied", "not json", 2),
        ("hook: a call without a path is denied", "{}", 2),
    ]:
        with contextlib.redirect_stderr(io.StringIO()):  # the deny reason is for the agent, not for this log
            got = hook(raw)
        cases.append((name, got == want, f"exit {got}, wanted {want}"))
    return kit.report(cases)


def main(argv):
    if "--self-test" in argv:
        return self_test()
    if "--hook" in argv:
        try:
            raw = sys.stdin.read()
        except Exception:  # undecodable stdin: hook("") denies it
            raw = ""
        return hook(raw)
    try:
        if "--pre-push" in argv:
            remote = argv[argv.index("--pre-push") + 1]
            commits = pushed_commits(remote, sys.stdin.read())
            errors = [e for sha in commits for e in check_commit(sha)]
            scanned = f"{len(commits)} commits about to be published"
        else:
            files = working_tree()
            errors, scanned = scan(files), f"{len(files)} files"
    except (kit.Refused, IndexError) as e:
        print(f"ERROR: tree gate could not run: {e}")
        return 1
    for e in errors:
        print("ERROR:", e)
    print(f"tree gate: {scanned}, {len(errors)} errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
