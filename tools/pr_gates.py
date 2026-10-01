#!/usr/bin/env python3
"""Gates that read a pull request rather than the tree. Usage: pr_gates.py title|breadth|findings [--pr N]
(the number comes from PR_NUMBER in CI).

title     a workflow-skip literal in the title or in any commit message of the branch. The title
          becomes the squash commit on main, and a skipped workflow there leaves main ungated.
breadth   a pull request over BREADTH_FILES files, or one that touches the quality infrastructure,
          without a non-empty "## Blast radius" section in its description.
findings  the review policy: every finding the reviewer posted is resolved, and resolved by an
          edit. A finding on the design record, the reference, the corpus, the knowledge store or
          a rule file counts as answered only when that file changed after the finding; a finding
          on tool or workflow code may also be answered by a written reply. The check is per file,
          not per line: one real change to a file answers every finding on it.
A failed API call is a refusal (exit 1), never a pass. Known limit, printed on every run: resolving
a thread starts no pipeline, so re-run the gates after resolving."""
import json
import os
import re
import sys
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

SKIP_LITERALS = [r"\[skip ci\]", r"\[ci skip\]", r"\[no ci\]", r"\[skip actions\]", r"\[actions skip\]",
                 r"^skip-checks:[ \t]*true[ \t]*$"]
BREADTH_FILES = 15
INFRA = ("tools/", ".github/", ".review/", ".claude/", ".githooks/")
CODE = ("tools/", ".github/", ".githooks/")  # a written rationale can answer a finding here
MARK = re.compile(r"<!-- kurz-review:findings (\[.*?\]) -->", re.S)
TRUSTED = {"OWNER", "MEMBER", "COLLABORATOR"}
THREADS = """query($owner: String!, $name: String!, $pr: Int!, $after: String) {
  repository(owner: $owner, name: $name) { pullRequest(number: $pr) {
    reviewThreads(first: 50, after: $after) { pageInfo { hasNextPage endCursor } nodes {
      id isResolved path
      comments(first: 30) { nodes { author { login } authorAssociation body originalCommit { oid } } } } } } } }"""


def skip_literal(text):
    return next((p for p in SKIP_LITERALS if re.search(p, text or "", re.I | re.M)), None)


def title_errors(title, messages):
    errors = [f"the title carries a workflow-skip literal ({skip_literal(title)})"] if skip_literal(title) else []
    errors += [f"a commit message carries a workflow-skip literal ({skip_literal(m)}): {m.splitlines()[0][:60]!r}"
               for m in messages if skip_literal(m)]
    return errors


def breadth_errors(body, paths):
    why = []
    if len(paths) > BREADTH_FILES:
        why.append(f"{len(paths)} files, over {BREADTH_FILES}")
    infra = sorted({p.split("/")[0] + "/" for p in paths if p.startswith(INFRA)})
    if infra:
        why.append(f"touches {', '.join(infra)}")
    section = re.search(r"^## Blast radius[ \t]*\n(.*?)(?=^## |\Z)", body or "", re.M | re.S)
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
        login = (first.get("author") or {}).get("login", "")
        m = MARK.search(first.get("body") or "")
        if m and (login == "github-actions" or first.get("authorAssociation") in TRUSTED):
            out.append((t, json.loads(m.group(1))))
    return out


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
        replied = len(nodes) > 1
        for path in paths:
            changed = bool(sha) and changed_since(path, sha)
            if changed or (replied and path.startswith(CODE)):
                continue
            how = "edit it or reply with the reason, then resolve" if path.startswith(CODE) else \
                "edit the file so the misreading is impossible, then resolve"
            errors.append(f"finding on {path} was resolved without changing the file: {how}")
    return errors


def fetch_threads(repo, number):
    owner, name = repo.split("/")
    threads, after = [], None
    while True:
        args = ["graphql", "-f", f"query={THREADS}", "-F", f"owner={owner}", "-F", f"name={name}", "-F", f"pr={number}"]
        if after:
            args += ["-f", f"after={after}"]
        page = kit.gh_json(*args)["data"]["repository"]["pullRequest"]["reviewThreads"]
        threads += page["nodes"]
        if not page["pageInfo"]["hasNextPage"]:
            return threads
        after = page["pageInfo"]["endCursor"]


def blob_at(repo, path, ref):
    """The blob id of a file at a commit, or None when the file is not there."""
    try:
        return kit.gh_json(f"repos/{repo}/contents/{urllib.parse.quote(path)}?ref={ref}").get("sha")
    except kit.Refused as e:
        if "404" in str(e):
            return None
        raise


def pr_number(argv):
    raw = argv[argv.index("--pr") + 1] if "--pr" in argv else os.environ.get("PR_NUMBER", "")
    if not raw.isdigit():
        raise kit.Refused("no pull request number: pass --pr N or set PR_NUMBER")
    return int(raw)


def run_gate(gate, argv):
    repo, number = kit.repo(), pr_number(argv)
    pr = kit.gh_json(f"repos/{repo}/pulls/{number}")
    if gate == "title":
        commits = kit.gh_pages(f"repos/{repo}/pulls/{number}/commits?per_page=100")
        if not commits:
            raise kit.Refused("the pull request lists no commits")
        return title_errors(pr["title"], [c["commit"]["message"] for c in commits]), f"title and {len(commits)} commit messages"
    if gate == "breadth":
        files = kit.gh_pages(f"repos/{repo}/pulls/{number}/files?per_page=100")
        if not files:
            raise kit.Refused("the pull request lists no files")
        return breadth_errors(pr["body"], [f["filename"] for f in files]), f"{len(files)} files"
    if gate == "findings":
        head = pr["head"]["sha"]
        threads = fetch_threads(repo, number)
        changed = lambda path, sha: blob_at(repo, path, sha) != blob_at(repo, path, head)
        print("known limit: resolving a thread starts no pipeline; re-run the gates after resolving")
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

    check("clean title and messages", title_errors("Add the knowledge store", ["Add the lint\n\nbody"]), None)
    for literal in ("[skip ci]", "[ci skip]", "[no ci]", "[skip actions]", "[actions skip]"):
        check(f"title with {literal}", title_errors(f"Fix {literal}", ["ok"]), "the title carries")
    check("upper-case literal in a commit message", title_errors("ok", ["x\n\n[SKIP CI]"]), "a commit message carries")
    check("skip-checks trailer", title_errors("ok", ["x\n\nskip-checks: true\n"]), "a commit message carries")
    check("the word skip alone is fine", title_errors("Skip the ci chapter", ["skip-checks: false"]), None)

    section = "## Summary\n\nx\n\n## Blast radius\n\n- the reviewer\n\n## Test plan\n"
    check("small change needs no section", breadth_errors("", ["kurz-design.md"]), None)
    check("infrastructure without the section", breadth_errors("## Summary\nx", ["tools/kit.py"]), "Blast radius")
    check("infrastructure with the section", breadth_errors(section, ["tools/kit.py", ".github/workflows/gates.yml"]), None)
    check("empty section does not count", breadth_errors("## Blast radius\n\n## Test plan\nx", ["tools/kit.py"]), "Blast radius")
    check("many files without the section", breadth_errors(None, [f"knowledge/e{n}.md" for n in range(16)]), "16 files")
    check("fifteen files are not many", breadth_errors(None, [f"knowledge/e{n}.md" for n in range(15)]), None)

    found = finding_threads(threads)
    cases.append(("the real answer holds finding threads", len(found) >= 1, f"{len(found)} of {len(threads)} threads"))
    if found:
        thread, findings = found[0]

        def variant(resolved, replies=0, login=None, association=None, path=None):
            """The real thread, edited: its state, its replies, its author, or the file its finding names."""
            t = json.loads(json.dumps(thread))
            t["isResolved"] = resolved
            first = t["comments"]["nodes"][0]
            t["comments"]["nodes"] = [first] + [dict(first, body="because") for _ in range(replies)]
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
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    gate = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        errors, scanned = run_gate(gate, sys.argv)
    except (kit.Refused, KeyError, IndexError, ValueError) as e:
        print(f"ERROR: pr gate {gate or '?'} could not run: {type(e).__name__}: {e}")
        sys.exit(1)
    for e in errors:
        print("ERROR:", e)
    print(f"pr gate {gate}: {scanned}, {len(errors)} errors")
    sys.exit(1 if errors else 0)
