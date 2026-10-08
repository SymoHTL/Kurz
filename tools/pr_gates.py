#!/usr/bin/env python3
"""Gates that read a pull request rather than the tree. Usage: pr_gates.py title|breadth|findings|resolve [--pr N] [--go]
(the number comes from PR_NUMBER in CI).

title     a workflow-skip literal, a credential-shaped or machine-bound string or a conflict marker
          in the title, in the description or in any commit message of the branch. The merge tool
          writes the title and the description into the squash commit on main; a squash merge by
          any other route takes the commit messages, which is this repository's default
          (tools/fixtures/repository.json). A skipped workflow there leaves main ungated, and no
          pre-push hook ever sees that commit.
breadth   a pull request over BREADTH_FILES files, or one that touches the quality infrastructure,
          without a non-empty "## Blast radius" section in its description.
findings  the review policy: every finding the reviewer posted is resolved, and resolved by an
          edit. A finding on the design record, the reference, the corpus, the knowledge store or
          a rule file counts as answered only when that file changed after the finding; a finding
          on tool or workflow code may also be answered by a written reply: a comment in its
          thread from someone who may write here, that is not a post of the reviewer. The check is
          per file, not per line: one real change to a file answers every finding on it.
resolve   not a gate: the session's step after its fix push. Every unresolved thread of the
          reviewer whose files all changed since the finding's commit is resolved, because its
          edit is the answer as `findings` reads it; a thread without findings, a reviewer post that
          names no file, a finding whose commit is gone and one whose file did not change are left
          open and printed with the reason and the thread id,
          since a person's question and a written reply are the session's to give. Without --go
          the plan is printed and nothing is written; writes are a second apart. Two pull requests
          had this as a hand-written script before the third need made it the tool (2026-10-07).
A failed API call is a refusal (exit 1), never a pass. Known limit, printed on every run: neither a
resolved thread nor the review that posts its findings starts a pipeline, so this verdict is the
one of the moment the job ran. Re-run the gates after resolving; the merge tool reads the threads
again at the merge."""
import contextlib
import io
import json
import os
import re
import sys
import time
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402
import tree_gate  # noqa: E402

BREADTH_FILES = 15
INFRA = ("tools/", ".github/", ".review/", ".claude/", ".githooks/")  # directories the quality bar lives in
INFRA_FILES = ("CLAUDE.md", ".gitattributes")  # what every session, or every checkout, obeys
CODE = ("tools/", ".github/", ".githooks/")  # a written rationale can answer a finding here
MARKER = "<!-- kurz-review:"  # every post of the reviewer carries one
MARK = re.compile(r"<!-- kurz-review:findings (\[.*?\]) -->", re.S)
TRUSTED = {"OWNER", "MEMBER", "COLLABORATOR"}
skip_literal = kit.skip_literal
BOTS = {"github-actions", "github-actions[bot]"}  # the Actions token's login, as GraphQL and as REST spell it
THREADS = """query($owner: String!, $name: String!, $pr: Int!, $after: String) {
  repository(owner: $owner, name: $name) { pullRequest(number: $pr) {
    reviewThreads(first: 50, after: $after) { pageInfo { hasNextPage endCursor } nodes {
      id isResolved path
      comments(first: 30) { nodes { author { login } authorAssociation body originalCommit { oid } } } } } } } }"""


def lf(text):
    """A text of the forge with LF line ends: the web form saves a description with CRLF."""
    return (text or "").replace("\r\n", "\n").replace("\r", "\n")


def trusted(login, association):
    """True for a post of the Actions token or of someone who may write here. Anyone can comment on
    a public repository, so a marker counts only from these."""
    return login in BOTS or association in TRUSTED


def title_errors(title, messages, body=""):
    """What the title, the description and the commit messages hold against a squash merge. Any of
    them can become the commit on main, so each is scanned with the tree gate's own patterns, like
    a commit a push would publish. A message is named by its place in the list, never quoted."""
    title, body, messages = lf(title), lf(body), [lf(m) for m in messages]
    errors = [f"the title carries a workflow-skip literal ({skip_literal(title)})"] if skip_literal(title) else []
    errors += [f"a commit message carries a workflow-skip literal ({skip_literal(m)}): {m.splitlines()[0][:60]!r}"
               for m in messages if skip_literal(m)]
    if skip_literal(body):
        errors.append(f"the description carries a workflow-skip literal ({skip_literal(body)})")
    errors += tree_gate.check_text("the title", title) + tree_gate.check_text("the description", body)
    for n, m in enumerate(messages, 1):
        errors += tree_gate.check_text(f"the message of commit {n} of {len(messages)}", m)
    return errors


def breadth_errors(body, paths):
    why = []
    if len(paths) > BREADTH_FILES:
        why.append(f"{len(paths)} files, over {BREADTH_FILES}")
    infra = sorted({p.split("/")[0] + "/" for p in paths if p.startswith(INFRA)} | {p for p in paths if p in INFRA_FILES})
    if infra:
        why.append(f"touches {', '.join(infra)}")
    section = re.search(r"^## Blast radius[ \t]*\n(.*?)(?=^## |\Z)", lf(body), re.M | re.S)
    if why and not (section and section.group(1).strip()):
        return [f"the description needs a non-empty '## Blast radius' section ({'; '.join(why)})"]
    return []


def finding_threads(threads):
    """(thread, findings) for every thread whose first comment is a trusted reviewer post with findings."""
    out = []
    for t in threads:
        nodes = t["comments"]["nodes"]
        first = nodes[0] if nodes else None
        if not first:
            continue
        m = MARK.search(first.get("body") or "")
        if m and trusted((first.get("author") or {}).get("login", ""), first.get("authorAssociation")):
            out.append((t, json.loads(m.group(1))))
    return out


def answers(comment):
    """A written reason: a comment from someone who may write here that is not a post of the
    reviewer. The reviewer posts under the Actions token in CI and under the login that started it
    off the pipeline, so its posts are told by their marker, not by their author."""
    return comment.get("authorAssociation") in TRUSTED and MARKER not in (comment.get("body") or "")


def findings_errors(threads, changed_since):
    """changed_since(path, sha) -> True when the file at the head differs from the file at sha."""
    errors = []
    for t, findings in finding_threads(threads):
        paths = sorted({f["file"] for f in findings})
        if not t["isResolved"]:
            errors.append(f"unresolved review finding on {', '.join(paths)}")
            continue
        nodes = t["comments"]["nodes"]
        sha = (nodes[0].get("originalCommit") or {}).get("oid")
        replied = any(answers(c) for c in nodes[1:])
        for path in paths:
            changed = bool(sha) and changed_since(path, sha)
            if changed or (replied and path.startswith(CODE)):
                continue
            how = "edit it or reply with the reason, then resolve" if path.startswith(CODE) else \
                "edit the file so the misreading is impossible, then resolve"
            errors.append(f"finding on {path} was resolved without changing the file: {how}")
    return errors


RESOLVE = "mutation($thread: ID!) { resolveReviewThread(input: {threadId: $thread}) { thread { isResolved } } }"


def resolve_plan(threads, changed_since):
    """(to resolve, left open) among the unresolved threads. A finding thread whose files all changed
    since the finding's commit is answered by its edit, the reading `findings` applies, and may be
    resolved; everything else is left with its reason: a thread without findings is a person's, a
    reviewer post that names no file names nothing to check, a finding whose commit is gone cannot
    be proven answered, a finding whose file did not change is answered by an edit or, on tool,
    workflow or hook code, by a reply, which this tool cannot write."""
    findings_of = {id(t): f for t, f in finding_threads(threads)}  # None below: not a reviewer post; []: one that names no file
    resolve, left = [], []
    for t in threads:
        if t["isResolved"]:
            continue
        nodes = t["comments"]["nodes"]
        sha = (nodes[0].get("originalCommit") or {}).get("oid") if nodes else None
        findings = findings_of.get(id(t))
        paths = sorted({f["file"] for f in findings}) if findings is not None else [t.get("path") or "?"]
        if findings is not None and sha and paths and all(changed_since(p, sha) for p in paths):
            resolve.append((t, paths))
        else:
            why = ("no finding of the reviewer" if findings is None else "the reviewer's post names no file" if not findings
                   else "its commit is gone" if not sha else "the file did not change")
            left.append((t, paths, why))
    return resolve, left


def resolve_threads(repo, number, go, get=None):
    """Prints what is left open with its reason and id, then each answered thread as it would be
    resolved or, with `go`, once the forge said it is, one write a second: (resolved, left open).
    An answer that does not say resolved is a refusal; what was resolved before it stays so."""
    get = get or kit.gh_json
    head = get(f"repos/{repo}/pulls/{number}")["head"]["sha"]
    threads = fetch_threads(repo, number, get)
    changed = lambda path, sha: blob_at(repo, path, sha, get) != blob_at(repo, path, head, get)
    resolve, left = resolve_plan(threads, changed)
    for t, paths, why in left:
        print(f"left open: {', '.join(paths) or '(no file)'} ({t.get('id')}): {why}")
    for t, paths in resolve:
        if not go:
            print(f"would resolve {', '.join(paths)} ({t['id']})")
            continue
        answer = get("graphql", "-f", f"query={RESOLVE}", "-f", f"thread={t['id']}")
        if not (((answer.get("data") or {}).get("resolveReviewThread") or {}).get("thread") or {}).get("isResolved"):
            raise kit.Refused(f"the forge did not resolve the thread on {', '.join(paths)} ({t['id']}): {str(answer)[:200]}")
        print(f"resolved {', '.join(paths)} ({t['id']})")  # after the forge said so, never before
        time.sleep(1)
    return len(resolve), len(left)


def resolve_command(repo, number, go, get=None):
    """The exit code of the resolve command: 0 when the plan ran, 1 when a read or a write failed. Any
    failure after the first write leaves threads resolved, so the message says what stands."""
    try:
        resolved, left = resolve_threads(repo, number, go, get)
    except BaseException as e:  # whatever failed, an interrupt too: the lines printed above are what the forge confirmed
        print(f"ERROR: resolve stopped: {type(e).__name__}: {e}. The threads printed as resolved above stay resolved; "
              f"if a write failed, its thread may or may not be: run the plan again, it shows what is left")
        return 1
    print(f"resolve: {resolved} threads {'resolved' if go else 'to resolve (plan only: add --go)'}, {left} left open")
    return 0


def fetch_threads(repo, number, get=None):
    owner, name = repo.split("/")
    threads, after = [], None
    while True:
        args = ["graphql", "-f", f"query={THREADS}", "-F", f"owner={owner}", "-F", f"name={name}", "-F", f"pr={number}"]
        if after:
            args += ["-f", f"after={after}"]
        page = (get or kit.gh_json)(*args)["data"]["repository"]["pullRequest"]["reviewThreads"]
        threads += page["nodes"]
        if not page["pageInfo"]["hasNextPage"]:
            return threads
        after = page["pageInfo"]["endCursor"]


def blob_at(repo, path, ref, get=None):
    """The blob id of a file at a commit, or None when the commit is there and the file is not.
    Only the forge's own 404 for the file counts as absent, and only once the commit itself
    answers: an unknown commit proves nothing about the file."""
    get = get or kit.gh_json
    try:
        return get(f"repos/{repo}/contents/{urllib.parse.quote(path)}?ref={ref}").get("sha")
    except kit.Refused as e:
        if "(HTTP 404)" not in str(e):
            raise
    get(f"repos/{repo}/commits/{ref}")
    return None


def pr_number(argv, env=None):
    env = os.environ if env is None else env
    raw = (argv[argv.index("--pr") + 1:] or [""])[0] if "--pr" in argv else env.get("PR_NUMBER", "")
    if not raw.isdigit():
        raise kit.Refused("no pull request number: pass --pr N or set PR_NUMBER")
    return int(raw)


def run_gate(gate, repo, number, get=None, pages=None):
    """(errors, what was read). `get` and `pages` are the forge calls; a self-test passes its own."""
    get, pages = get or kit.gh_json, pages or kit.gh_pages
    pr = get(f"repos/{repo}/pulls/{number}")
    if gate == "title":
        commits = pages(f"repos/{repo}/pulls/{number}/commits?per_page=100")
        if not commits:
            raise kit.Refused("the pull request lists no commits")
        if len(commits) != pr.get("commits"):  # the forge lists at most 250: a longer branch has messages nobody read
            raise kit.Refused(f"the pull request has {pr.get('commits')} commits and the forge listed {len(commits)}: not every message was read")
        return (title_errors(pr["title"], [c["commit"]["message"] for c in commits], pr["body"]),
                f"title, description and {len(commits)} commit messages")
    if gate == "breadth":
        files = pages(f"repos/{repo}/pulls/{number}/files?per_page=100")
        if not files:
            raise kit.Refused("the pull request lists no files")
        # a renamed file counts where it came from too: moving one out of the infrastructure touches it
        return breadth_errors(pr["body"], [p for f in files for p in (f["filename"], f.get("previous_filename")) if p]), f"{len(files)} files"
    if gate == "findings":
        head = pr["head"]["sha"]
        threads = fetch_threads(repo, number, get)
        changed = lambda path, sha: blob_at(repo, path, sha, get) != blob_at(repo, path, head, get)
        print("known limit: neither a resolved thread nor a posted review starts a pipeline; re-run the gates after resolving")
        return findings_errors(threads, changed), f"{len(threads)} threads, {len(finding_threads(threads))} with findings"
    raise kit.Refused(f"unknown gate {gate!r}: title, breadth or findings")


def fixture_threads():
    """The review threads the forge really answered for a reviewed pull request (see
    tools/fixtures/SOURCES.txt). [] when the file is missing or unreadable, which fails the case
    that needs it instead of crashing the suite."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "review-threads.json")
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]
    except (OSError, ValueError, KeyError, TypeError):
        return []


def self_test():
    threads = fixture_threads()
    cases = []

    def check(name, errors, needle):
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    def got(fn):
        """What fn returned, or the exception it raised, as a value; an interrupt too, so that a case
        about one can fail instead of ending the suite."""
        try:
            return fn()
        except BaseException as e:
            return e

    check("clean title and messages", title_errors("Add the knowledge store", ["Add the lint\n\nbody"]), None)
    for literal in ("[skip ci]", "[ci skip]", "[no ci]", "[skip actions]", "[actions skip]"):
        check(f"title with {literal}", title_errors(f"Fix {literal}", ["ok"]), "the title carries")
    check("upper-case literal in a commit message", title_errors("ok", ["x\n\n[SKIP CI]"]), "a commit message carries")
    check("skip-checks trailer", title_errors("ok", ["x\n\nskip-checks: true\n"]), "a commit message carries")
    check("the word skip alone is fine", title_errors("Skip the ci chapter", ["skip-checks: false"]), None)
    # Built by concatenation, so this file never holds a string the tree gate would refuse.
    check("a machine-bound string in the description", title_errors("ok", ["ok"], "see D:" + "/work/notes"), "the description: machine-bound string")
    check("a credential shape in the title", title_errors("use ghp_" + "a" * 36, ["ok"], "ok"), "the title: credential-shaped string")
    check("a workflow-skip literal in the description", title_errors("ok", ["ok"], "why\n\n[skip ci]"), "the description carries")
    check("a plain description is clean", title_errors("ok", ["ok"], "## Summary\n\nWhat and why, with a link: https://github.com/a/b\n"), None)
    check("a skip-checks trailer in a description saved with CRLF", title_errors("ok", ["ok"], "why\r\n\r\nskip-checks: true\r\n"),
          "the description carries")
    check("a machine-bound string in a commit message", title_errors("ok", ["Fix\n\nsee D:" + "/work/notes"]),
          "the message of commit 1 of 1: machine-bound string")
    check("a credential shape in a later commit message", title_errors("ok", ["ok", "use ghp_" + "a" * 36]),
          "the message of commit 2 of 2: credential-shaped string")

    section = "## Summary\n\nx\n\n## Blast radius\n\n- the reviewer\n\n## Test plan\n"
    check("small change needs no section", breadth_errors("", ["kurz-design.md"]), None)
    check("the rules file is infrastructure: every session obeys it", breadth_errors("", ["CLAUDE.md"]), "touches CLAUDE.md")
    check("the attributes file is infrastructure: every checkout obeys it", breadth_errors("", [".gitattributes"]), "touches .gitattributes")
    check("a file whose name starts like the rules file is not it", breadth_errors("", ["CLAUDE.md.bak"]), None)
    check("infrastructure without the section", breadth_errors("## Summary\nx", ["tools/kit.py"]), "Blast radius")
    check("infrastructure with the section", breadth_errors(section, ["tools/kit.py", ".github/workflows/gates.yml"]), None)
    check("the section is found in a description saved with CRLF", breadth_errors(section.replace("\n", "\r\n"), ["tools/kit.py"]), None)
    check("empty section does not count", breadth_errors("## Blast radius\n\n## Test plan\nx", ["tools/kit.py"]), "Blast radius")
    check("many files without the section", breadth_errors(None, [f"knowledge/e{n}.md" for n in range(16)]), "16 files")
    check("fifteen files are not many", breadth_errors(None, [f"knowledge/e{n}.md" for n in range(15)]), None)

    found = finding_threads(threads)
    cases.append(("the real answer holds finding threads", len(found) >= 1, f"{len(found)} of {len(threads)} threads"))
    if found:
        thread, findings = found[0]

        def variant(resolved, replies=0, login=None, association=None, path=None, reply=None):
            """The real thread, edited: its state, its replies, its author, or the file its finding names."""
            t = json.loads(json.dumps(thread))
            t["isResolved"] = resolved
            first = t["comments"]["nodes"][0]
            said = {"body": "because", "author": {"login": "a-maintainer"}, "authorAssociation": "OWNER", **(reply or {})}
            t["comments"]["nodes"] = [first] + [dict(first, **said) for _ in range(replies)]
            if login is not None:
                first["author"]["login"], first["authorAssociation"] = login, association
            if path is not None:
                moved = json.dumps([dict(f, file=path) for f in findings])
                first["body"] = MARK.sub(lambda m: f"<!-- kurz-review:findings {moved} -->", first["body"])
            return [t]

        same, differs = (lambda p, s: False), (lambda p, s: True)
        check("unresolved finding blocks", findings_errors(variant(False), differs), "unresolved review finding")
        check("resolved and edited passes", findings_errors(variant(True), differs), None)
        check("resolved without an edit blocks", findings_errors(variant(True), same), "without changing the file")
        check("a reply answers a finding on tool code", findings_errors(variant(True, replies=1, path="tools/x.py"), same), None)
        check("a reply does not answer a finding on the design record",
              findings_errors(variant(True, replies=1, path="kurz-design.md"), same), "without changing the file")
        check("without a reply a finding on tool code needs the edit", findings_errors(variant(True, path="tools/x.py"), same), "edit it or reply")
        check("the reviewer's own follow-up is not a reply",
              findings_errors(variant(True, replies=1, path="tools/x.py", reply={"body": MARKER + "findings [] -->\nstill there"}), same),
              "edit it or reply")
        check("a comment from someone who may not write here is not a reply",
              findings_errors(variant(True, replies=1, path="tools/x.py", reply={"authorAssociation": "NONE"}), same), "edit it or reply")
        check("a stranger's marker is not a finding", findings_errors(variant(False, login="someone", association="NONE"), same), None)
        check("the Actions bot's marker is a finding", findings_errors(variant(False, login="github-actions", association="NONE"), same),
              "unresolved review finding")
        gone = json.loads(json.dumps(variant(True)))
        gone[0]["comments"]["nodes"][0]["originalCommit"] = None
        check("a finding whose commit is gone cannot be proven answered", findings_errors(gone, differs), "without changing the file")
    plain = [{"isResolved": False, "path": "a.md", "comments": {"nodes": [
        {"author": {"login": "someone"}, "authorAssociation": "OWNER", "body": "a human comment", "originalCommit": {"oid": "x"}}]}}]
    check("a human thread is the merge check's business, not this gate's", findings_errors(plain, lambda p, s: False), None)
    check("no threads at all is clean", findings_errors([], lambda p, s: False), None)

    def forge(missing=(), failing=None):
        """A forge call for blob_at: a path in `missing` answers 404; `failing` is what the file's call raises."""
        def get(path, *rest):
            if failing and "contents/" in path:
                raise failing
            if any(m in path for m in missing):
                raise kit.Refused(f"`gh api {path}` exited 1: gh: Not Found (HTTP 404)")
            return {"sha": "blob"}
        return get

    cases.append(("blob: a file that is there has its blob id", got(lambda: blob_at("o/n", "a.md", "c1", forge())) == "blob", ""))
    out = got(lambda: blob_at("o/n", "a.md", "c1", forge(missing=["contents/"])))
    cases.append(("blob: a file that is not at a commit that is there has none", out is None, repr(out)))
    out = got(lambda: blob_at("o/n", "a.md", "c1", forge(missing=["contents/", "commits/"])))
    cases.append(("blob: a commit the forge does not know is a refusal, not an absent file", type(out) is kit.Refused, repr(out)))
    # the endpoint is echoed in the refusal, and this commit id holds the digits 404
    out = got(lambda: blob_at("o/n", "a.md", "c404", forge(failing=kit.Refused("`gh api repos/o/n/contents/a.md?ref=c404` exited 1: HTTP 502"))))
    cases.append(("blob: a refusal that only holds the digits 404 stays a refusal", type(out) is kit.Refused, repr(out)))

    if found:
        # the forge answers the threads on two pages; the finding on the first names the design record, resolved
        # without a reply, so only the blob ids at its commit and at the head decide it; the second page is unresolved
        pages_of = {None: variant(True, path="kurz-design.md"), "c1": variant(False)}

        def paged(blob):
            """A forge whose `get` answers the pull request, two pages of threads, and `blob(ref)` for a file."""
            def get(*args):
                if args[0] == "graphql":
                    after = next((a[len("after="):] for a in args if a.startswith("after=")), None)
                    page = {"nodes": pages_of[after], "pageInfo": {"hasNextPage": after is None, "endCursor": "c1"}}
                    return {"data": {"repository": {"pullRequest": {"reviewThreads": page}}}}
                if "/contents/" in args[0]:
                    return {"sha": blob(args[0].split("?ref=")[1])}
                return {"title": "ok", "body": "", "head": {"sha": "h"}, "commits": 1}
            return get

        out = got(lambda: run_gate("findings", "o/n", 1, paged(lambda ref: "blob@" + ref)))
        cases.append(("findings: both pages of threads are read, and a blob that differs between the finding's commit and the head is the edit",
                      type(out) is tuple and [e[:28] for e in out[0]] == ["unresolved review finding on"] and out[1].startswith("2 threads, 2 with"),
                      repr(out)))
        out = got(lambda: run_gate("findings", "o/n", 1, paged(lambda ref: "blob")))
        cases.append(("findings: the same blob at the finding's commit and at the head is no edit",
                      type(out) is tuple and len(out[0]) == 2 and any("without changing the file" in e for e in out[0]), repr(out)))

        # resolve: which unresolved threads the tool may resolve, and what it writes
        counts = lambda plan: [len(plan[0]), len(plan[1])]
        plan = resolve_plan(variant(False), differs)
        cases.append(("resolve: an unresolved finding whose file changed is to be resolved", counts(plan) == [1, 0], repr(plan)))
        plan = resolve_plan(variant(False), same)
        cases.append(("resolve: a finding whose file did not change is left open",
                      counts(plan) == [0, 1] and plan[1][0][2] == "the file did not change", repr(plan)))
        plan = resolve_plan(variant(True), differs)
        cases.append(("resolve: a resolved thread is left alone", plan == ([], []), repr(plan)))
        plan = resolve_plan(plain, differs)
        cases.append(("resolve: a person's thread is left open, whatever changed",
                      counts(plan) == [0, 1] and plan[1][0][2] == "no finding of the reviewer", repr(plan)))
        lost = variant(False)
        lost[0]["comments"]["nodes"][0]["originalCommit"] = None
        plan = resolve_plan(lost, differs)
        cases.append(("resolve: a finding whose commit is gone is left open", counts(plan) == [0, 1] and plan[1][0][2] == "its commit is gone", repr(plan)))
        empty = variant(False)
        empty[0]["comments"]["nodes"][0]["body"] = MARKER + "findings [] -->"
        plan = resolve_plan(empty, differs)
        cases.append(("resolve: a reviewer post that names no file is left open",
                      counts(plan) == [0, 1] and plan[1][0][2] == "the reviewer's post names no file", repr(plan)))

        def resolving(blob, says=True, threads=1, breaks_at=None, error=None):
            """A forge for resolve_threads: the pull request, one page with the real thread unresolved
            (`threads` copies with their own ids), blob(ref) for a file, and the mutation, which it
            records and answers as told, or raises `error` (an error nobody expected) at write `breaks_at`."""
            written = []

            def get(*args):
                if args[0] == "graphql" and args[2].startswith("query=mutation"):
                    if breaks_at is not None and len(written) == breaks_at:
                        raise error or RuntimeError("an answer nobody expected")  # a kind no except tuple of this tool ever named
                    written.append(args[-1])
                    return {"data": {"resolveReviewThread": {"thread": {"isResolved": says}}}}
                if args[0] == "graphql":
                    nodes = [dict(variant(False)[0], id=f"t{n}") for n in range(threads)]
                    page = {"nodes": nodes, "pageInfo": {"hasNextPage": False, "endCursor": None}}
                    return {"data": {"repository": {"pullRequest": {"reviewThreads": page}}}}
                if "/contents/" in args[0]:
                    return {"sha": blob(args[0].split("?ref=")[1])}
                return {"title": "ok", "body": "", "head": {"sha": "h"}, "commits": 1}
            return get, written

        def quietly(fn):
            """(what fn returned or raised, what it printed)."""
            with contextlib.redirect_stdout(io.StringIO()) as printed:
                out = got(fn)
            return out, printed.getvalue()

        resolved_lines = lambda printed: sum(line.startswith("resolved ") for line in printed.splitlines())  # the error text says "resolved" too
        slept = []
        saved_sleep, time.sleep = time.sleep, slept.append
        try:
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, False, get))
            cases.append(("resolve: without --go the plan is printed and nothing is written",
                          out == (1, 0) and written == [] and "would resolve" in printed, repr((out, written, printed))))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: with --go the answered thread is resolved by its id",
                          out == (1, 0) and written == ["thread=t0"] and resolved_lines(printed) == 1, repr((out, written, printed))))
            get, written = resolving(lambda ref: "blob")
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: a thread whose file did not change is not written, with --go", out == (0, 1) and written == [], repr((out, written))))
            get, written = resolving(lambda ref: "blob@" + ref, says=False)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: an answer that does not say resolved is a refusal", type(out) is kit.Refused, repr(out)))
            cases.append(("resolve: a thread is printed as resolved only after the forge said so",
                          type(out) is kit.Refused and resolved_lines(printed) == 0, repr(printed)))
            slept.clear()
            get, written = resolving(lambda ref: "blob@" + ref, threads=2)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: writes are a second apart", out == (2, 0) and slept == [1, 1], repr((out, slept))))
            get, written = resolving(lambda ref: "blob@" + ref, threads=2, breaks_at=1)
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: a failure of any kind after a write says what stands",
                          out == 1 and resolved_lines(printed) == 1 and "resolve stopped: RuntimeError" in printed, repr((out, printed))))
            get, written = resolving(lambda ref: "blob@" + ref, threads=2, breaks_at=1, error=KeyboardInterrupt())
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: an interrupt during --go says what stands too",
                          out == 1 and resolved_lines(printed) == 1 and "resolve stopped: KeyboardInterrupt" in printed, repr((out, printed))))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: the command ends 0 when the plan ran", out == 0 and "1 threads resolved" in printed, repr((out, printed))))
        finally:
            time.sleep = saved_sleep

    pr = {"title": "ok", "body": "", "head": {"sha": "h"}, "commits": 1}
    commit, changed = {"commit": {"message": "x\n\n[skip ci]"}}, {"filename": "tools/kit.py"}
    listed = lambda commits, files: (lambda path: list(commits) if "/commits" in path else list(files))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: pr, listed([], [changed])))
    cases.append(("title: a pull request that lists no commits is refused", type(out) is kit.Refused and "no commits" in str(out), repr(out)))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: pr, listed([commit], [])))
    cases.append(("title: the messages of the commits the pull request lists are read",
                  type(out) is tuple and any("a commit message carries" in e for e in out[0]) and "1 commit messages" in out[1], repr(out)))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: {**pr, "commits": 251}, listed([commit], [])))
    cases.append(("title: a pull request with more commits than the forge listed is refused, not passed on the ones read",
                  type(out) is kit.Refused and "251 commits" in str(out) and "listed 1" in str(out), repr(out)))
    out = got(lambda: run_gate("breadth", "o/n", 1, lambda path: pr, listed([commit], [])))
    cases.append(("breadth: a pull request that lists no files is refused", type(out) is kit.Refused and "no files" in str(out), repr(out)))
    moved = {"filename": "knowledge/walk.md", "previous_filename": ".claude/skills/change-walk/SKILL.md", "status": "renamed"}
    out = got(lambda: run_gate("breadth", "o/n", 1, lambda path: pr, listed([], [moved])))
    cases.append(("breadth: a file moved out of the infrastructure touches it, so the section is needed",
                  type(out) is tuple and any(".claude/" in e for e in out[0]), repr(out)))
    out = got(lambda: run_gate("breadth", "o/n", 1, lambda path: pr, listed([], [changed])))
    cases.append(("breadth: the files the pull request lists are read",
                  type(out) is tuple and any("Blast radius" in e for e in out[0]) and out[1] == "1 files", repr(out)))
    out = got(lambda: run_gate("spelling", "o/n", 1, lambda path: pr, listed([commit], [changed])))
    cases.append(("a gate this tool does not have is refused", type(out) is kit.Refused and "unknown gate" in str(out), repr(out)))
    numbers = (got(lambda: pr_number(["title", "--pr", "7"], {})), got(lambda: pr_number(["title"], {"PR_NUMBER": "9"})))
    cases.append(("number: --pr and PR_NUMBER are read", numbers == (7, 9), repr(numbers)))
    refused = [got(lambda: pr_number(["title"], {})), got(lambda: pr_number(["title", "--pr"], {"PR_NUMBER": "9"})),
               got(lambda: pr_number(["title", "--pr", "x"], {}))]
    cases.append(("number: none, none after --pr, and one that is no number are refused",
                  all(type(r) is kit.Refused for r in refused), repr(refused)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    gate = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        number = pr_number(sys.argv)
        if gate == "resolve":
            sys.exit(resolve_command(kit.repo(), number, "--go" in sys.argv))
        errors, scanned = run_gate(gate, kit.repo(), number)
    except (kit.Refused, KeyError, IndexError, ValueError, TypeError, AttributeError) as e:
        print(f"ERROR: pr gate {gate or '?'} could not run: {type(e).__name__}: {e}")
        sys.exit(1)
    for e in errors:
        print("ERROR:", e)
    print(f"pr gate {gate}: {scanned}, {len(errors)} errors")
    sys.exit(1 if errors else 0)
