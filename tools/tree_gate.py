#!/usr/bin/env python3
"""What may exist in this public tree. Fails on: a path the design phase does not allow, a
credential-shaped string, a machine-bound string (drive or profile path, private IP address,
e-mail address), a merge-conflict marker, a file that is not UTF-8 text, a listed entry that is
not a regular file (a link, a submodule), or a scan of fewer than FLOOR files.

Three callers, one definition:
  (no flag)    every tracked or untracked-but-not-ignored file of the working tree  (CI, local gates)
  --pre-push   every commit a `git push` is about to publish: its message and the files it adds or
               changes. Not its author and committer, which a push publishes as well   (.githooks/pre-push)
  --hook       one Write/Edit call of an agent session, path rule only              (.claude/settings.json)
`--self-test` plants one case per rule and per pattern, plus precision cases that must stay clean.
In --hook mode only exit 2 blocks the call, so every failure path there ends in exit 2, a
tools/kit.py that does not import included. The command in .claude/settings.json turns every
other way this script can end into exit 2 as well, and falls back to python3 where there is no
`py` launcher; the self-test runs that command. What the hook cannot hold: a write made through
a shell command, and a hook the harness cut off at its timeout (HAZARD #4)."""
import contextlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    import kit  # noqa: E402
except Exception:  # only exit 2 blocks a hook call: a kit that is broken must not let every write through
    if "--hook" not in sys.argv:
        raise
    print("tree gate could not start: tools/kit.py does not import", file=sys.stderr)
    sys.exit(2)

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
         "and the quality tools. No compiler, runtime or library code until the owner says build (CLAUDE.md)")


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


def entry(full, islink=os.path.islink):
    """What a listed path is on disk: "file"; "gone", a tracked file that is being deleted; or
    "special", a link, a submodule or a directory, whose published content cannot be read here."""
    if islink(full):
        return "special"
    if os.path.isfile(full):
        return "file"
    return "special" if os.path.lexists(full) else "gone"


def working_tree(root=None, listed=None):
    """({repository path: bytes}, errors) for every tracked or untracked-but-not-ignored path."""
    root = root or kit.ROOT
    if listed is None:
        listed = kit.run(["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], cwd=root).split("\0")
    files, errors = {}, []
    for path in filter(None, listed):
        full = os.path.join(root, path)
        kind = entry(full)
        if kind == "file":
            with open(full, "rb") as f:
                files[path] = f.read()
        elif kind == "special":  # git publishes the link text or the submodule commit, and neither is what is on disk
            errors += [e for e in [check_path(path)] if e]
            errors.append(f"{path}: not a regular file (a link, a submodule or a directory)")
    return files, errors


def pushed_commits(remote, stdin_text, cwd=None):
    """The commits a push would publish: reachable from the pushed refs, on no ref of that remote yet."""
    commits = []
    for line in stdin_text.splitlines():
        parts = line.split()
        if len(parts) != 4:
            raise kit.Refused(f"pre-push line not understood: {line!r}")
        local_sha = parts[1]
        if set(local_sha) == {"0"}:  # a ref being deleted publishes nothing
            continue
        commits += kit.run(["git", "rev-list", local_sha, "--not", f"--remotes={remote}"], cwd=cwd).split()
    return list(dict.fromkeys(commits))


def check_commit(sha, cwd=None):
    message = kit.run(["git", "log", "-1", "--format=%B", sha], cwd=cwd)
    errors = check_text(f"{sha[:8]} (message)", message)
    if kit.skip_literal(message):
        errors.append(f"{sha[:8]} (message): workflow-skip literal: the forge would start no workflow for this commit")
    # every path but a deletion: a file turned into a link (T) carries its target as content, and is scanned like the rest
    names = kit.run(["git", "show", "--format=", "--name-only", "--diff-filter=d", "-z", sha], cwd=cwd).split("\0")
    for path in filter(None, names):
        blob = kit.run(["git", "show", f"{sha}:{path}"], cwd=cwd, binary=True)  # as stored: decoding it here would hide a file that is not UTF-8
        errors += [f"{sha[:8]} {e}" for e in check_file(path, blob)]
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
    machine = {"drive-path": "D:" + "/work/notes", "profile-path": "/c/" + "Users/someone", "ip-address": "10.1." + "2.3",
               "ip6-address": "fd12:" + "3456::1", "email": "someone@" + "mailhost.org"}
    # The same facts in the other shapes they come in: inside JSON, seen from WSL or Cygwin, on Linux, ending a sentence.
    shapes = [("an escaped drive path", "drive-path", '"cwd": "C:' + "\\\\work\\\\notes" + '"'),
              ("a WSL profile path", "profile-path", "/mnt/c/" + "Users/someone"),
              ("a Cygwin profile path", "profile-path", "/cygdrive/c/" + "Users/someone"),
              ("a Linux home directory", "profile-path", "/home/" + "someone/notes"),
              ("an address that ends a sentence", "ip-address", "it answered on 10.1." + "2.3."),
              ("a link-local IPv6 address", "ip6-address", "fe80" + "::1"),
              # a value that begins a line in JSON or a string literal follows the letter of an escape
              ("a home directory after a newline escape", "profile-path", '"log": "ok' + "\\n/home/" + 'someone/x"'),
              ("an address after a tab escape", "ip-address", '"ok' + "\\t10.1." + '2.3"')]
    cases = []

    def expect(name, files, needle, floor=FLOOR):
        errors = scan(files, floor)
        hit = any(needle in e for e in errors) if needle else not errors
        cases.append((name, hit, errors))

    def got(fn):
        """What fn returned, or the exception it raised, as a value: a case then fails instead of ending the suite."""
        try:
            return fn()
        except Exception as e:
            return e

    expect("clean tree passes", ok_files, None)
    expect("floor catches an empty scan", {}, "floor")
    four = {f"knowledge/e{n}.md": b"fact\n" for n in range(4)}
    cases.append(("floor: four files are below the gate's own floor", any("floor" in e for e in scan(four)), scan(four)))
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
    hits = check_text("x", '"out": "done' + "\\nsk-" + "a" * 24 + '"')
    cases.append(("secret: an api key after a newline escape", hits == ["x: credential-shaped string (api-key)"], hits))
    for label, value in machine.items():
        expect(f"machine-bound: {label}", {**ok_files, "guides/g.md": value.encode()}, f"machine-bound string ({label})")
    for name, label, value in shapes:
        hits = check_text("x", value)
        cases.append((f"machine-bound: {name}", hits == [f"x: machine-bound string ({label})"], hits))
    for label, value in {**secrets, **machine}.items():  # a plant two patterns catch hides one switched off
        hits = [e for e in check_text("x", value)]
        cases.append((f"only its own pattern fires: {label}", len(hits) == 1, hits))
    unplanted = (set(kit.SECRETS) | set(kit.MACHINE)) ^ (set(secrets) | set(machine))
    cases.append(("every pattern has a plant", not unplanted, sorted(unplanted)))
    portable = ("task-tracking-belongs-in-the-tracker, version 3.12.1 and v1.0.0.0, build 1.2.3.4.5, 127.0.0.1, "
                "https://github.com/a/b, c://x, actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1, "
                "@anthropic-ai/claude-code@2.1.283, user@example.com, noreply@anthropic.com, git@github.com:a/b.git, "
                "e.g.: a/b, 20:46:53+02:00, ~/.claude/notes, /home/runner/work, 2001:db8::1")
    cases.append(("portable words stay clean", not check_text("x", portable), check_text("x", portable)))

    # the working tree: what a listed path is on disk
    root = tempfile.mkdtemp()
    os.mkdir(os.path.join(root, "sub"))
    with open(os.path.join(root, "a.md"), "wb") as f:
        f.write(b"fact\n")
    files, errors = working_tree(root, ["a.md", "sub", "gone.md"])
    cases.append(("tree: a regular file is read, and a tracked file that is gone is a deletion",
                  files == {"a.md": b"fact\n"} and not any("gone.md" in e for e in errors), (files, errors)))
    cases.append(("tree: a listed entry that is no regular file is refused, not skipped",
                  any(e.startswith("sub: not a regular file") for e in errors) and any(e.startswith("sub: not allowed") for e in errors), errors))
    cases.append(("tree: a link is refused whatever it points at", entry(os.path.join(root, "a.md"), islink=lambda p: True) == "special", ""))

    # the commits of a push, against a throwaway repository
    repo = tempfile.mkdtemp()
    git = lambda *args: kit.run(["git", "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false",
                                 "-c", "core.autocrlf=false", *args], cwd=repo).strip()

    def commit(message, write=None, delete=None):
        for path, data in (write or {}).items():
            os.makedirs(os.path.dirname(os.path.join(repo, path)), exist_ok=True)
            with open(os.path.join(repo, path), "wb") as f:
                f.write(data)
        if delete:
            os.remove(os.path.join(repo, delete))
        git("add", "-A")
        git("commit", "--quiet", "-m", message)
        return git("rev-parse", "HEAD")

    def link(path, target):
        """Turn a tracked file into a symbolic link to `target`, in the index alone: the file system need not allow links."""
        with open(os.path.join(repo, "target.txt"), "wb") as f:
            f.write(target.encode())
        blob = git("hash-object", "-w", "target.txt")
        os.remove(os.path.join(repo, "target.txt"))
        git("update-index", "--add", "--cacheinfo", f"120000,{blob},{path}")
        git("commit", "--quiet", "-m", "a link")
        return git("rev-parse", "HEAD")

    built = got(lambda: (git("init", "--quiet"),
                         commit("first", {"knowledge/a.md": b"fact\n"}),
                         commit("see D:" + "/work/notes", {"knowledge/b.md": b"fact\n"}),
                         commit("a compiler", {"src/Lexer.cs": b"class L {}\n"}),
                         commit("a binary", {"knowledge/c.md": b"\xff\xfe\x00"}),
                         commit("a deletion", delete="knowledge/a.md"),
                         commit("quiet\n\n[skip " + "ci]", {"knowledge/d.md": b"fact\n"}),
                         link("knowledge/d.md", "D:" + "/work/notes")))
    if isinstance(built, tuple):
        _, clean, message, lexer, binary, deletion, quiet, linked = built
        of = lambda sha: got(lambda: check_commit(sha, cwd=repo))
        has = lambda errors, needle: isinstance(errors, list) and any(needle in e for e in errors)
        cases.append(("pre-push: a clean commit passes", of(clean) == [], of(clean)))
        cases.append(("pre-push: a machine-bound string in a commit message is refused",
                      has(of(message), "(message): machine-bound string (drive-path)"), of(message)))
        cases.append(("pre-push: a path the design phase does not allow is refused", has(of(lexer), "src/Lexer.cs: not allowed"), of(lexer)))
        cases.append(("pre-push: a file that is not UTF-8 is seen as it is stored", has(of(binary), "knowledge/c.md: not UTF-8 text"), of(binary)))
        cases.append(("pre-push: a deleted file is not scanned", of(deletion) == [], of(deletion)))
        cases.append(("pre-push: a workflow-skip literal in a commit message is refused",
                      has(of(quiet), "(message): workflow-skip literal"), of(quiet)))
        cases.append(("pre-push: a file turned into a link is scanned, and its target is the content",
                      has(of(linked), "knowledge/d.md: machine-bound string (drive-path)"), of(linked)))
        git("update-ref", "refs/remotes/origin/main", clean)
        git("update-ref", "refs/remotes/other/main", deletion)
        line = f"refs/heads/main {deletion} refs/heads/main {clean}"
        pushed = got(lambda: pushed_commits("origin", line + "\n", cwd=repo))
        cases.append(("pre-push: the commits on no ref of that remote are the ones published, another remote's refs do not count",
                      isinstance(pushed, list) and set(pushed) == {message, lexer, binary, deletion}, pushed))
        gone = got(lambda: pushed_commits("origin", f"(delete) {'0' * 40} refs/heads/old {clean}\n", cwd=repo))
        cases.append(("pre-push: a deleted ref publishes nothing", gone == [], gone))
        odd = got(lambda: pushed_commits("origin", line + " extra\n", cwd=repo))
        cases.append(("pre-push: a line that is not four fields is refused", isinstance(odd, kit.Refused), odd))
    cases.append(("pre-push: the throwaway repository was built", isinstance(built, tuple), built))

    call = lambda path: json.dumps({"tool_name": "Write", "tool_input": {"file_path": path}})
    inside = lambda rel: os.path.join(kit.ROOT, *rel.split("/"))
    outside = os.path.join(tempfile.gettempdir(), "x.cs")
    for name, raw, want in [
        ("hook: compiler source inside the repo is denied", call(inside("src/Lexer.cs")), 2),
        ("hook: the design record is allowed", call(inside("kurz-design.md")), 0),
        ("hook: a corpus case is allowed", call(inside("corpus/values/a.kz")), 0),
        ("hook: a file outside the repo is not this gate's business", call(outside), 0),
        ("hook: a path under .git/ is not this gate's business", call(inside(".git/COMMIT_EDITMSG")), 0),
        ("hook: a notebook call is read by its notebook_path", json.dumps({"tool_name": "NotebookEdit", "tool_input": {"notebook_path": outside}}), 0),
        ("hook: unparseable stdin is denied", "not json", 2),
        ("hook: a call without tool_input is denied", "{}", 2),
        ("hook: a call without a path is denied", json.dumps({"tool_name": "Write", "tool_input": {}}), 2),
    ]:
        with contextlib.redirect_stderr(io.StringIO()):  # the deny reason is for the agent, not for this log
            code = hook(raw)
        cases.append((name, code == want, f"exit {code}, wanted {want}"))

    # a kit that does not import: the hook has to end in 2 before it has read anything
    scratch = tempfile.mkdtemp()
    os.mkdir(os.path.join(scratch, "tools"))
    here = os.path.dirname(os.path.abspath(__file__))
    shutil.copy(os.path.abspath(__file__), os.path.join(scratch, "tools", "tree_gate.py"))

    def hook_exit(kit_text):
        with open(os.path.join(scratch, "tools", "kit.py"), "w", encoding="utf-8") as f:
            f.write(kit_text)
        return subprocess.run([sys.executable, os.path.join(scratch, "tools", "tree_gate.py"), "--hook"], input=call(outside),
                              capture_output=True, text=True, env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"}).returncode

    exits = got(lambda: (hook_exit(kit.read(os.path.join(here, "kit.py"))), hook_exit("def (:\n")))
    cases.append(("hook: a kit that does not import ends in exit 2, where the same call passes with a kit that does", exits == (0, 2), exits))

    # The command the session settings hand to the harness, run the way the harness runs it: by a
    # shell. Only exit 2 blocks a write, so the command has to end in 0 or in 2 whatever became of
    # the gate. The gate here is a stand-in that ends as it is told to; `python3` in the second
    # directory stands for the interpreter of a machine that has no `py` launcher.
    command = got(lambda: json.loads(kit.read(os.path.join(kit.ROOT, ".claude", "settings.json")))["hooks"]["PreToolUse"][0]["hooks"][0]["command"])
    shell, project, only_python3, nothing = shutil.which("sh"), tempfile.mkdtemp(), tempfile.mkdtemp(), tempfile.mkdtemp()
    os.mkdir(os.path.join(project, "tools"))
    with open(os.path.join(project, "tools", "tree_gate.py"), "w", encoding="utf-8") as f:
        f.write('import os, sys\nsys.exit(int(os.environ["GATE_EXIT"]))\n')
    with open(os.path.join(only_python3, "python3"), "w", encoding="utf-8", newline="\n") as f:
        f.write('#!/bin/sh\nexit "$GATE_EXIT"\n')
    os.chmod(os.path.join(only_python3, "python3"), 0o755)

    def command_exit(gate_exit, path=None):
        env = {**os.environ, "CLAUDE_PROJECT_DIR": project, "GATE_EXIT": str(gate_exit), **({"PATH": path} if path else {})}
        return subprocess.run([shell, "-c", command], input="{}", capture_output=True, text=True, env=env, timeout=60).returncode

    cases.append(("hook command: there is a shell to run it with (run the gates from a POSIX shell)", bool(shell) and isinstance(command, str), (shell, command)))
    for name, want, how in [
        ("hook command: a gate that allows ends in 0", 0, (0,)),
        ("hook command: a denial ends in 2", 2, (2,)),
        ("hook command: a gate that failed any other way ends in 2 too", 2, (1,)),
        ("hook command: without the launcher the gate runs under python3", 0, (0, only_python3)),
        ("hook command: with no interpreter at all the write is denied", 2, (0, nothing)),
    ]:
        code = got(lambda: command_exit(*how))
        cases.append((name, code == want, f"exit {code!r}, wanted {want}"))
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
            files, special = working_tree()
            errors, scanned = special + scan(files), f"{len(files)} files"
    except (kit.Refused, IndexError) as e:
        print(f"ERROR: tree gate could not run: {e}")
        return 1
    for e in errors:
        print("ERROR:", e)
    print(f"tree gate: {scanned}, {len(errors)} errors")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
