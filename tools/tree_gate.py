#!/usr/bin/env python3
"""What may exist in this public tree. Fails on: a path the design phase does not allow, a
credential-shaped string, a machine-bound string (drive or profile path, private IP address,
e-mail address) in a file or in its name, a merge-conflict marker, a file that is not UTF-8 text,
a listed entry that is not a regular file (a link, a submodule), or a scan of fewer than FLOOR
files.

Three callers, one definition:
  (no flag)    every tracked or untracked-but-not-ignored file of the working tree  (CI, local gates)
  --pre-push   every commit a `git push` is about to publish: its message and the files it adds or
               changes, a link or a submodule refused as in the working tree; the name of every ref
               it publishes; the message and the name of every annotated tag it pushes, and of each
               tag that one points at, and a tag that points at a blob or a tree is refused. Not the
               author, committer or tagger, which a push publishes as well (HAZARD #12)
                                                                                   (.githooks/pre-push)
  --hook       one Write/Edit call of an agent session, path rule only              (.claude/settings.json)
`--self-test` plants one case per rule and per pattern, plus precision cases that must stay clean.
In --hook mode the script blocks a call by exit 2 and prints no JSON decision, the other way the
harness accepts (knowledge/a-hook-denies-by-exit-2-or-by-its-json.md), so every failure path there
ends in exit 2, a
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
    """Errors for one file, given its repository path and its bytes. A push publishes the path as
    well, so the name is scanned like the content."""
    reason = check_path(path)
    errors = [reason] if reason else []
    errors += check_text(f"{path} (name)", path)
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


def pushed_tags(stdin_text, cwd=None):
    """The annotated tags a push publishes: the pushed objects that are tags. A tag's commit is
    checked by pushed_commits only when the push publishes it for the first time; pushed_commits
    has refused a line it does not understand already."""
    shas = [line.split()[1] for line in stdin_text.splitlines() if len(line.split()) == 4]
    return [sha for sha in shas if set(sha) != {"0"} and kit.run(["git", "cat-file", "-t", sha], cwd=cwd).strip() == "tag"]


def pushed_refs(stdin_text):
    """The names a push publishes: the remote ref of every line that publishes something. A deletion
    publishes no name."""
    return [line.split()[2] for line in stdin_text.splitlines() if len(line.split()) == 4 and set(line.split()[1]) != {"0"}]


def check_tag(sha, cwd=None, seen=()):
    """The message of an annotated tag, which the push publishes beside what it points at, read as a
    commit message is (kit.message_text): a trailer's no-reply address passes. The name in the tag's
    own header is published too, under the ref or through a chain, so it is scanned like a ref name.
    A tag that points at a tag publishes that one too, so the chain is followed; one that points at
    a blob or a tree publishes an object no other check reads, and is refused. The header also
    names the tagger, published like a commit's author and read by no gate (HAZARD #12)."""
    header, _, message = kit.run(["git", "cat-file", "tag", sha], cwd=cwd).partition("\n\n")
    fields = dict(line.split(" ", 1) for line in header.splitlines() if " " in line)
    target, kind = fields.get("object", ""), fields.get("type", "")
    errors = check_text(f"{sha[:8]} (tag message)", kit.message_text(message))
    errors += check_text(f"{sha[:8]} (tag name)", fields.get("tag", ""))
    if kind == "tag" and target not in seen:
        errors += check_tag(target, cwd, (*seen, sha))
    elif kind not in ("commit", "tag"):
        errors.append(f"{sha[:8]} (tag): points at a {kind or 'nothing'}, not a commit: what it publishes is read by no check")
    return errors


def check_commit(sha, cwd=None):
    message = kit.run(["git", "log", "-1", "--format=%B", sha], cwd=cwd)
    errors = check_text(f"{sha[:8]} (message)", kit.message_text(message))
    if kit.skip_literal(message):
        errors.append(f"{sha[:8]} (message): workflow-skip literal: the forge would start no workflow for this commit")
    # every path but a deletion, a file turned into a link (T) included
    names = kit.run(["git", "show", "--format=", "--name-only", "--diff-filter=d", "-z", sha], cwd=cwd).split("\0")
    for path in filter(None, names):
        mode = kit.run(["git", "--literal-pathspecs", "ls-tree", "-z", sha, "--", path], cwd=cwd).split(" ", 1)[0]
        if mode in ("120000", "160000"):  # a link or a submodule: what is published is no file, as working_tree() refuses
            errors += [f"{sha[:8]} {e}" for e in [check_path(path)] if e]
            errors.append(f"{sha[:8]} {path}: not a regular file (a link or a submodule)")
            continue
        blob = kit.run(["git", "show", f"{sha}:{path}"], cwd=cwd, binary=True)  # as stored: decoding it here would hide a file that is not UTF-8
        errors += [f"{sha[:8]} {e}" for e in check_file(path, blob)]
    return errors


def within(target, root, exists=os.path.exists, same=os.path.samefile):
    """The path of `target` inside `root` ("." for the root itself), or None when it lies outside.
    Decided by identity: each existing ancestor of the target is compared with the root by the file
    system, so a path spelled in another case where the file system ignores case (Windows, macOS)
    is inside, as the file it writes is. The path comes back as realpath returns it: existing parts
    in the case the file system stores, the rest as written, never lower-cased, so the allowlist is
    never matched against a lower-cased spelling."""
    full = os.path.realpath(target)
    path = full
    while True:
        if exists(path) and same(path, root):
            return os.path.relpath(full, path)
        parent = os.path.dirname(path)
        if parent == path:
            return None
        path = parent


def hook_reason(raw):
    """The deny reason for one Write/Edit call, or None to allow it. Raises on input it cannot read."""
    tool_input = json.loads(raw)["tool_input"]
    target = tool_input.get("file_path") or tool_input["notebook_path"]
    rel = within(target, kit.ROOT)
    if rel is None:
        return None  # outside this repository: not this gate's business
    rel = rel.replace(os.sep, "/")
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
    # every directory of the suite, and one beside the checkout for what must lie outside it wherever the
    # temporary directory is (the red-proof replay puts it inside); a removal that fails is said, not raised
    with kit.scratch("tree-gate-") as base, kit.scratch("tree-gate-outside-", dir=os.path.dirname(kit.ROOT)) as beside:
        return cases_in(base, beside)


def cases_in(base, beside):
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
              ("root's home directory", "profile-path", "/root/" + "work/x"),
              ("a GitHub no-reply address", "email", "ask 123+someone@" + "users.noreply.github.com"),
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
    expect("a machine-bound string in a file's name is refused", {**ok_files, "knowledge/host-10.1." + "2.3.md": b"fact\n"},
           "(name): machine-bound string (ip-address)")
    trailer = "Fix\n\nCo-authored-by: Someone <123+someone@" + "users.noreply.github.com>\n"
    cases.append(("a GitHub no-reply address passes in a commit's trailer line only",
                  check_text("m", kit.message_text(trailer)) == []
                  and check_text("m", kit.message_text(trailer.replace("Co-authored-by: ", "Ask "))) == ["m: machine-bound string (email)"],
                  check_text("m", kit.message_text(trailer))))
    named = "Fix\n\nCo-authored-by: see D:" + "/work/notes <123+someone@" + "users.noreply.github.com>\n"
    cases.append(("a trailer line's name part is scanned: a machine-bound string before the no-reply address is refused",
                  check_text("m", kit.message_text(named)) == ["m: machine-bound string (drive-path)"], check_text("m", kit.message_text(named))))
    plain = "Fix\n\nCo-authored-by: Someone <someone@" + "mailhost.org>\n"
    cases.append(("a trailer line with an ordinary address is refused: only the no-reply address passes",
                  check_text("m", kit.message_text(plain)) == ["m: machine-bound string (email)"], check_text("m", kit.message_text(plain))))
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
    root = tempfile.mkdtemp(dir=base)
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
    repo = tempfile.mkdtemp(dir=base)
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

    def gitlink(path, target):
        """Add a submodule entry that points at the commit `target`, in the index alone: no second repository is needed."""
        git("update-index", "--add", "--cacheinfo", f"160000,{target},{path}")
        git("commit", "--quiet", "-m", "a submodule")
        return git("rev-parse", "HEAD")

    built = got(lambda: (git("init", "--quiet"),
                         commit("first", {"knowledge/a.md": b"fact\n"}),
                         commit("see D:" + "/work/notes", {"knowledge/b.md": b"fact\n"}),
                         commit("a compiler", {"src/Lexer.cs": b"class L {}\n"}),
                         commit("a binary", {"knowledge/c.md": b"\xff\xfe\x00"}),
                         commit("a deletion", delete="knowledge/a.md"),
                         commit("quiet\n\n[skip " + "ci]", {"knowledge/d.md": b"fact\n"}),
                         commit("credited\n\nCo-authored-by: Someone <123+someone@" + "users.noreply.github.com>",
                                {"knowledge/e.md": b"fact\n"}),
                         link("knowledge/d.md", "D:" + "/work/notes"),
                         gitlink("sub", git("rev-list", "--max-parents=0", "HEAD"))))
    if isinstance(built, tuple):
        _, clean, message, lexer, binary, deletion, quiet, credited, linked, submodule = built
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
        cases.append(("pre-push: a file turned into a link is refused, as in the working tree",
                      has(of(linked), "knowledge/d.md: not a regular file"), of(linked)))
        cases.append(("pre-push: a co-author's GitHub no-reply address in a trailer passes", of(credited) == [], of(credited)))
        cases.append(("pre-push: a submodule is refused, as in the working tree", has(of(submodule), "sub: not a regular file"), of(submodule)))
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
        refs = got(lambda: pushed_refs(f"refs/tags/v1 {clean} refs/tags/v1 {'0' * 40}\n(delete) {'0' * 40} refs/heads/old {clean}\n{line}\n"))
        cases.append(("pre-push: the names a push publishes are its remote refs, a deletion's not among them",
                      refs == ["refs/tags/v1", "refs/heads/main"], refs))
        git("tag", "-a", "v1", "-m", "see D:" + "/work/notes", clean)
        tag = git("rev-parse", "v1")
        tags = got(lambda: pushed_tags(f"refs/tags/v1 {tag} refs/tags/v1 {'0' * 40}\n{line}\n", cwd=repo))
        cases.append(("pre-push: an annotated tag is read, and a branch is no tag", tags == [tag], tags))
        cases.append(("pre-push: a machine-bound string in an annotated tag's message is refused",
                      has(got(lambda: check_tag(tag, cwd=repo)), "(tag message): machine-bound string (drive-path)"),
                      got(lambda: check_tag(tag, cwd=repo))))
        git("tag", "-a", "v2", "-m", "release\n\nCo-authored-by: Someone <123+someone@" + "users.noreply.github.com>", clean)
        credited_tag = git("rev-parse", "v2")
        cases.append(("pre-push: a co-author's GitHub no-reply address in a tag's trailer passes, as in a commit",
                      got(lambda: check_tag(credited_tag, cwd=repo)) == [], got(lambda: check_tag(credited_tag, cwd=repo))))
        git("tag", "-a", "outer", "-m", "a tag of a tag", "v1")  # the message of v1 holds the drive path
        outer = git("rev-parse", "outer")
        cases.append(("pre-push: a tag that points at a tag is followed, and the inner tag's message is read",
                      has(got(lambda: check_tag(outer, cwd=repo)), f"{tag[:8]} (tag message): machine-bound string (drive-path)"),
                      got(lambda: check_tag(outer, cwd=repo))))
        with open(os.path.join(repo, "note.txt"), "wb") as f:
            f.write(b"a note\n")
        note = git("hash-object", "-w", "note.txt")
        os.remove(os.path.join(repo, "note.txt"))
        git("tag", "-a", "notetag", "-m", "a tag of a blob", note)
        notetag = git("rev-parse", "notetag")
        cases.append(("pre-push: a tag that points at a blob is refused: what it publishes is read by no check",
                      has(got(lambda: check_tag(notetag, cwd=repo)), "points at a blob"), got(lambda: check_tag(notetag, cwd=repo))))
        git("tag", "-a", "treetag", "-m", "a tag of a tree", f"{clean}^{{tree}}")
        treetag = git("rev-parse", "treetag")
        cases.append(("pre-push: a tag that points at a tree is refused too, for the same reason",
                      has(got(lambda: check_tag(treetag, cwd=repo)), "points at a tree"), got(lambda: check_tag(treetag, cwd=repo))))
        # a ref name cannot hold a drive path (git refuses a colon), but an address of the private ranges it can
        address = "10.1.2" + ".3"  # in two parts, so that this file holds no address
        git("tag", "-a", address, "-m", "a clean message", clean)
        named_tag = git("rev-parse", address)
        git("tag", "-a", "wrapper", "-m", "a tag of the named tag", address)
        wrapper = git("rev-parse", "wrapper")
        cases.append(("pre-push: the name in a tag's own header is scanned, through a chain too, where no ref name shows it",
                      has(got(lambda: check_tag(wrapper, cwd=repo)), f"{named_tag[:8]} (tag name): machine-bound string (ip-address)"),
                      got(lambda: check_tag(wrapper, cwd=repo))))
        # the script as the hook runs it, for its exit code: from tools/ of the repository it guards, since
        # its git calls run in kit.ROOT; a clean commit of its own, which no ref of the remote holds
        fresh = git("commit-tree", f"{clean}^{{tree}}", "-p", clean, "-m", "clean too")
        os.makedirs(os.path.join(repo, "tools"), exist_ok=True)
        for name in ("tree_gate.py", "kit.py"):
            shutil.copy(os.path.join(os.path.dirname(os.path.abspath(__file__)), name), os.path.join(repo, "tools", name))

        def pre_push(stdin_text):
            """(exit code, what the script printed): a crash ends in 1 too, so each case reads the text as well."""
            p = subprocess.run([sys.executable, os.path.join(repo, "tools", "tree_gate.py"), "--pre-push", "origin"],
                               input=stdin_text, cwd=repo,
                               capture_output=True, text=True, env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
            return p.returncode, p.stdout + p.stderr
        codes = got(lambda: (pre_push(f"refs/heads/side {fresh} refs/heads/side {'0' * 40}\n"), pre_push(line + "\n"),
                             pre_push("not four fields\n"), pre_push(f"refs/tags/v1 {tag} refs/tags/v1 {'0' * 40}\n"),
                             pre_push(f"refs/heads/host-10.1." + f"2.3 {fresh} refs/heads/host-10.1." + f"2.3 {'0' * 40}\n")))
        ended = lambda n, code, needle: isinstance(codes, tuple) and codes[n][0] == code and needle in codes[n][1]
        cases.append(("pre-push: the script ends in 0 for a clean push, and says what it scanned", ended(0, 0, "ref names about to be published, 0 errors"), codes))
        cases.append(("pre-push: the script ends in 1 for a push that holds a refused commit, and names the commit's defect",
                      ended(1, 1, "(message): machine-bound string (drive-path)"), codes))
        cases.append(("pre-push: the script ends in 1 for a line it cannot read, and says so", ended(2, 1, "pre-push line not understood"), codes))
        cases.append(("pre-push: the script ends in 1 for a tag whose message is refused, though its commit is published already, and names the tag",
                      ended(3, 1, "(tag message): machine-bound string (drive-path)"), codes))
        cases.append(("pre-push: the script ends in 1 for a ref whose name holds a machine-bound string, and names the ref",
                      ended(4, 1, "(ref name): machine-bound string (ip-address)"), codes))
    cases.append(("pre-push: the throwaway repository was built", isinstance(built, tuple), built))

    call = lambda path: json.dumps({"tool_name": "Write", "tool_input": {"file_path": path}})
    inside = lambda rel: os.path.join(kit.ROOT, *rel.split("/"))
    outside = os.path.join(beside, "x.cs")  # outside the checkout, whatever the temporary directory is
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
    # a file system that ignores case, stood in for by two functions: the repository's directory
    # spelled in another case is the same directory there, and a write into it is inside
    top, repository = os.path.realpath(os.path.join(os.sep, "r")), os.path.realpath(os.path.join(os.sep, "r", "kurz"))
    known = {top.lower(), repository.lower()}
    spelled = os.path.join(os.path.dirname(repository), "Kurz", "SRC", "x.cs")
    found = got(lambda: within(spelled, repository, exists=lambda p: p.lower() in known, same=lambda a, b: a.lower() == b.lower()))
    cases.append(("hook: a path spelled in another case is inside where the file system ignores case",
                  found == os.path.join("SRC", "x.cs"), found))

    # a kit that does not import: the hook has to end in 2 before it has read anything
    scratch = tempfile.mkdtemp(dir=base)
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
    shell, project, only_python3, nothing = shutil.which("sh"), tempfile.mkdtemp(dir=base), tempfile.mkdtemp(dir=base), tempfile.mkdtemp(dir=base)
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
            stdin_text = sys.stdin.read()
            commits = pushed_commits(remote, stdin_text)
            tags, refs = pushed_tags(stdin_text), pushed_refs(stdin_text)
            errors = ([e for sha in commits for e in check_commit(sha)] + [e for sha in tags for e in check_tag(sha)]
                      + [e for ref in refs for e in check_text(f"{ref} (ref name)", ref)])
            scanned = f"{len(commits)} commits, {len(tags)} annotated tags and {len(refs)} ref names about to be published"
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
