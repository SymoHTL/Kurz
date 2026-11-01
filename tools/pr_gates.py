#!/usr/bin/env python3
"""Gates that read a pull request rather than the tree. Usage: pr_gates.py title|breadth|findings|resolve|cap [--pr N]
[--head SHA] [--analysis FILE] [--go]
(the number comes from PR_NUMBER in CI).

title     a workflow-skip literal, a credential-shaped or machine-bound string or a conflict marker
          in the title, in the description or in any commit message of the branch. The merge tool
          writes the title and the description into the squash commit on main; a squash merge by
          any other route takes the commit messages, which is this repository's default
          (tools/fixtures/repository.json). A skipped workflow there leaves main ungated, and no
          pre-push hook ever sees that commit.
breadth   a pull request over BREADTH_FILES files, or one that touches the quality infrastructure,
          without a non-empty "## Blast radius" section in its description.
findings  the review policy: every finding the reviewer posted as a review thread (above low:
          lows go to the review-lows issue and hold no merge) is resolved, and resolved by an
          edit. A finding on the design record, the reference, the corpus, the knowledge store or
          a rule file counts as answered only when that file changed after the finding; a finding
          on tool, workflow or hook code may also be answered by a written reply: a comment in its
          thread from someone who may write here, that is not a post of the reviewer. The check is
          per file, not per line: one real change to a file answers every finding on it. On a
          pull request at the owner's cap of review rounds (kit.ROUND_CAP, counted from its labels
          by kit.review_rounds) a finding on any file is also answered by its move to the
          collection ticket: a reply from someone who may write here that links an open issue
          labelled kit.LEFTOVERS_LABEL, and the errors name that move (MOVE), since no edit follows the
          round at the cap. Below the cap such a reply is a written reply like any other: it answers a
          finding on tool, workflow and hook code, and nothing on the other files.
resolve   not a gate: the session's step after its fix push. Every unresolved thread of the
          reviewer whose files all changed since the finding's commit is resolved, because its
          edit is the answer as `findings` reads it, and so is, at the cap, one whose posts after
          the reviewer's all move it to the collection ticket; a thread without findings, a reviewer post that
          names no file, a finding whose commit is gone, one whose files did not all change, one in
          which a person wrote after the reviewer, and one whose commit the pull request still
          shows as its head (right after a push, for a moment) are left open and printed with
          the reason and the thread id,
          since a person's question and a written reply are the session's to give. Without --go
          the plan is printed and nothing is written; writes are a second apart. --head SHA (or
          --head=SHA), the commit just pushed (in full, or its first seven or more hex digits), refuses the run while
          the pull request shows another head: the forge names the old one for a moment after a
          push. Two pull requests
          had this as a hand-written script before the third need made it the tool (2026-10-07).
cap       not a gate: the session's walk of a pull request at the review cap, which sessions took by
          hand for every pull request at the cap from 2026-10-21 until it became this command
          (2026-10-29). It refuses below the cap, with no or several open collection tickets, with
          an open finding made on another commit than the head, with a thread in which a person
          wrote anything but a move, and with --go where the section it would post has no reading
          (no --analysis FILE, or a blank one); it prints the section of the collection ticket (the
          head, the day of the round,
          every open finding with the address of its thread, the session's reading from
          --analysis FILE, the low findings with the addresses of their comments, whose count must be
          the one the reviewer's notes name), and with --go posts it as a comment on the ticket,
          replies in every open finding thread with the ticket and the section's address, resolves
          as `resolve` does, and reads the threads back: one still open is exit 1. Writes are a
          second apart. A section or a reply that is there is not written again, so a walk that
          stopped is run again.
A failed API call is a refusal (exit 1), never a pass. Known limit, printed by `findings` and by
`resolve`: neither a resolved thread nor the review that posts its findings starts a pipeline, so
the verdict is the one of the moment the job ran. Re-run the gates after resolving; the merge tool reads the threads
again at the merge."""
import contextlib
import datetime
import io
import json
import os
import re
import sys
import tempfile
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
LIMIT = "known limit: neither a resolved thread nor a posted review starts a pipeline; re-run the gates after resolving"
STILL = ("the head is still the finding's commit: nothing was pushed since, or the push has not reached "
         "the pull request yet; run the plan again")  # the forge names the old head for a moment after a push
ON_HEAD = ("the finding sits on the pushed head: nothing was pushed since it; its edit answers it, or on tool, "
           "workflow or hook code also a reply")
REPLIED = ("a person wrote in it: read and answer the reply, then resolve it by hand once its edit, or on tool, "
           "workflow or hook code a reply, answers the finding")
BOTS = {"github-actions", "github-actions[bot]"}  # the Actions token's login, as GraphQL and as REST spell it
THREADS = """query($owner: String!, $name: String!, $pr: Int!, $after: String) {
  repository(owner: $owner, name: $name) { pullRequest(number: $pr) {
    reviewThreads(first: 50, after: $after) { pageInfo { hasNextPage endCursor } nodes {
      id isResolved path
      comments(first: 30) { nodes { author { login } authorAssociation body createdAt url originalCommit { oid } } } } } } } }"""


def lf(text):
    """A text of the forge with LF line ends: the web form saves a description with CRLF."""
    return (text or "").replace("\r\n", "\n").replace("\r", "\n")


def trusted(login, association):
    """True for a post of the Actions token or of someone who may write here. Anyone can comment on
    a public repository, so a marker counts only from these. The association is enough here, though
    a collaborator with read access alone carries COLLABORATOR too: what it decides holds a merge
    (a finding to answer, a thread a person wrote in) and never passes one; the evidence counts,
    which such a post would inflate, read the collaborators list instead (tools/quality_evidence.py)."""
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
        errors += tree_gate.check_text(f"the message of commit {n} of {len(messages)}", kit.message_text(m))
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
    """(thread, findings) for every thread whose first comment is a trusted reviewer post with findings.
    The opening post is told by its marker and a trusted association, not by a login: off the
    pipeline the reviewer posts under the login that started it. What a later post is, and whose,
    is `spoke`'s to say."""
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


def spoke(comment):
    """A later post in a thread that is not the reviewer's: a person's question or reply, whoever
    wrote it, whatever the association, since a question from anyone is read before the thread is
    resolved. The reviewer writes no later post; one that carries its marker under the Actions
    login is read as its own all the same, so a marker in a person's reply, pasted or typed, does
    not make it the reviewer's, a post under the Actions login without the marker is another
    workflow's, and a later post under a person's login is that person's, whatever it carries.
    The opening post is read otherwise (`finding_threads`): by its marker and a trusted
    association, since off the pipeline the reviewer posts under the login that started it."""
    bot = ((comment.get("author") or {}).get("login")) in BOTS
    return not (bot and MARKER in (comment.get("body") or ""))


def moved_to(repo, tickets):
    """moved(comment) -> True for a reply that moves its thread's finding to a collection ticket: a
    post from someone who may write here that is not the reviewer's (`answers`), linking one of
    `tickets`, the numbers of the open collection tickets, as #N or by the issue's address in this
    repository. With no ticket nothing moves: below the cap `leftovers` passes none."""
    tickets = set(tickets)

    def moved(comment):
        body = comment.get("body") or ""
        named = {int(n) for n in re.findall(r"(?<![\w&/])#(\d+)\b", body)}
        named |= {int(n) for n in re.findall(rf"https://github\.com/{re.escape(repo)}/issues/(\d+)\b", body, re.I)}
        return answers(comment) and bool(named & tickets)
    return moved


def tickets(repo, pages=None):
    """The open collection tickets: the open issues labelled kit.LEFTOVERS_LABEL, as the forge lists them."""
    issues = (pages or kit.gh_pages)(f"repos/{repo}/issues?state=open&labels={kit.LEFTOVERS_LABEL}&per_page=100")
    return [i for i in issues if "pull_request" not in i]  # the endpoint lists pull requests too


def leftovers(repo, pr, pages=None):
    """(review rounds, moved) for a pull request as the forge sent it: the rounds from its labels, and
    `moved_to` over the open collection tickets, which are asked for only at the cap: below it no
    reply moves a finding, so nothing is read."""
    if "labels" not in pr:  # no list is no count: zero rounds would read as a pull request below the cap
        raise kit.Refused("the forge sent the pull request without its labels, so its review rounds cannot be counted")
    rounds = kit.review_rounds([label["name"] for label in pr["labels"]])
    if rounds < kit.ROUND_CAP:
        return rounds, moved_to(repo, ())
    return rounds, moved_to(repo, [i["number"] for i in tickets(repo, pages)])


# What a finding still open at the cap asks for: no edit follows the round at the cap, so the error names the move.
MOVE = f"at the review cap no edit follows: reply with a link to the collection ticket, the open issue labelled {kit.LEFTOVERS_LABEL}, then resolve"


def findings_errors(threads, changed_since, moved=lambda comment: False, capped=False):
    """changed_since(path, sha) -> True when the file at the head differs from the file at sha;
    moved(comment) -> True for a reply that moved the finding to the collection ticket (`leftovers`);
    capped: the pull request is at the review cap, so an error names the move, not an edit."""
    errors = []
    for t, findings in finding_threads(threads):
        paths = sorted({f["file"] for f in findings})
        if not t["isResolved"]:
            errors.append(f"unresolved review finding on {', '.join(paths)}" + (f": {MOVE}" if capped else ""))
            continue
        nodes = t["comments"]["nodes"]
        sha = (nodes[0].get("originalCommit") or {}).get("oid")
        replied = any(answers(c) for c in nodes[1:])
        if any(moved(c) for c in nodes[1:]):  # at the review cap: the finding waits on the collection ticket
            continue
        for path in paths:
            changed = bool(sha) and changed_since(path, sha)
            if changed or (replied and path.startswith(CODE)):
                continue
            how = MOVE if capped else "edit it or reply with the reason, then resolve" if path.startswith(CODE) else \
                "edit the file so the misreading is impossible, then resolve"
            errors.append(f"finding on {path} was resolved without changing the file: {how}")
    return errors


RESOLVE = "mutation($thread: ID!) { resolveReviewThread(input: {threadId: $thread}) { thread { isResolved } } }"


def said_resolved(answer):
    """True when the forge's answer to RESOLVE says the thread is resolved; the shape is the captured
    tools/fixtures/resolve-thread.json."""
    return bool((((answer.get("data") or {}).get("resolveReviewThread") or {}).get("thread") or {}).get("isResolved"))


def once(read):
    """read(*args), asked once per args: a second read of the forge could disagree with the first."""
    seen = {}

    def first(*args):
        if args not in seen:
            seen[args] = read(*args)
        return seen[args]
    return first


def resolve_plan(threads, changed_since, head=None, proven=False, moved=lambda comment: False):
    """(to resolve, left open) among the unresolved threads. A finding thread whose files all changed
    since the finding's commit is answered by its edit, the reading `findings` applies, and may be
    resolved, and so may one whose posts after the reviewer's all move it to the collection ticket
    (`moved`, at the cap), whatever changed; everything else is left with its reason: a thread without findings is a person's, a
    reviewer post that names no file names nothing to check, a finding whose commit is gone cannot
    be proven answered, a finding whose file did not change (of several, the reason names the
    ones that did not) is answered by an edit or, on tool, workflow or hook code, by a reply,
    which this tool cannot write. A finding thread in which a person wrote after the reviewer is
    left open whatever changed: the reply is read before the thread is resolved, by hand. A
    finding whose commit is `head`, the head the pull request shows, cannot have changed since
    it: right after a push the forge still names the old head for a moment, and the reason says
    so instead of "did not change" for a finding that sits on that head. With `proven`, the head is
    the pushed commit the session named, so that finding sits on the pushed head and the reason
    says that. A finding from an earlier round reads as unchanged on a stale read, which only
    `expect` in resolve_threads refuses. Each file is read once per commit, so the plan and the
    reason it prints rest on the same answer."""
    changed_since = once(changed_since)  # a second read of the same blob could disagree with the first
    findings_of = {id(t): f for t, f in finding_threads(threads)}  # None below: not a reviewer post; []: one that names no file
    resolve, left = [], []
    for t in threads:
        if t["isResolved"]:
            continue
        nodes = t["comments"]["nodes"]
        sha = (nodes[0].get("originalCommit") or {}).get("oid") if nodes else None
        findings = findings_of.get(id(t))
        paths = sorted({f["file"] for f in findings}) if findings is not None else [p for p in [t.get("path")] if p]
        said = [c for c in nodes[1:] if spoke(c)]
        if findings is not None and said and all(moved(c) for c in said):  # the move is the answer; any other post is read first
            resolve.append((t, paths))
            continue
        if findings is not None and any(spoke(c) for c in nodes[1:]):
            left.append((t, paths, REPLIED))
            continue
        if findings is not None and sha and paths and all(changed_since(p, sha) for p in paths):
            resolve.append((t, paths))
        else:
            unchanged = [p for p in paths if not changed_since(p, sha)] if findings and sha and sha != head else paths
            why = ("no finding of the reviewer" if findings is None else "the reviewer's post names no file" if not findings
                   else (ON_HEAD if proven else STILL) if sha and sha == head
                   else "its commit is gone" if not sha
                   else ("the file did not change" if len(paths) == 1 else "none of its files changed") if unchanged == paths
                   else f"not every file changed: {', '.join(unchanged)} did not")
            left.append((t, paths, why))
    return resolve, left


def resolve_threads(repo, number, go, get=None, expect=None, pages=None):
    """Prints what is left open with its reason and id, then each answered thread as it would be
    resolved or, with `go`, once the forge said it is, one write a second: (answered, left open),
    where answered counts the threads resolved with `go` and the ones to resolve without it.
    An answer that does not say resolved is a refusal; what was resolved before it stays so.
    `expect` is the commit the session pushed, in full or as a prefix of seven or more hex digits in
    any case: while the pull request shows another head, the push has not reached it yet (or the
    checkout is elsewhere), and no thread is read, nothing is compared or written. `pages` reads the
    collection tickets at the cap (`leftovers`)."""
    get = get or kit.gh_json
    pr = get(f"repos/{repo}/pulls/{number}")
    head = pr["head"]["sha"]
    if expect and not re.fullmatch(r"[0-9a-fA-F]{7,40}", expect):
        raise kit.Refused(f"--head {expect!r} is no commit: pass the pushed commit, in full or its first seven or more hex digits")
    if expect and not head.lower().startswith(expect.lower()):
        raise kit.Refused(f"the pull request shows head {head}, not {expect}: the push has not reached it yet, or the checkout is elsewhere; run again")
    threads = fetch_threads(repo, number, get)
    changed = lambda path, sha: blob_at(repo, path, sha, get) != blob_at(repo, path, head, get)
    moved = leftovers(repo, pr, pages)[1]
    resolve, left = resolve_plan(threads, changed, head, proven=bool(expect), moved=moved)
    for t, paths, why in left:
        print(f"left open: {', '.join(paths) or '(no file)'} ({t.get('id')}): {why}")
    for t, paths in resolve:
        if not go:
            print(f"would resolve {', '.join(paths)} ({t['id']})")
            continue
        answer = get("graphql", "-f", f"query={RESOLVE}", "-f", f"thread={t['id']}")
        if not said_resolved(answer):
            raise kit.Refused(f"the forge's answer did not confirm the thread on {', '.join(paths)} ({t['id']}) as resolved: {str(answer)[:200]}")
        print(f"resolved {', '.join(paths)} ({t['id']})")  # after the forge said so, never before
        time.sleep(1)
    return len(resolve), len(left)


def resolve_command(repo, number, go, get=None, expect=None, pages=None):
    """The exit code of the resolve command: 0 when the plan ran, 1 when `--head` was refused (no
    commit, or another head shown), a read or a write failed, or the run was interrupted. Any
    failure after the first write leaves threads resolved, so the message says what stands."""
    try:
        resolved, left = resolve_threads(repo, number, go, get, expect, pages)
    except BaseException as e:  # whatever failed, an interrupt too: the lines printed above are what the forge confirmed
        print(f"ERROR: resolve stopped: {type(e).__name__}: {e}. The threads printed as resolved above stay resolved; "
              f"if a write failed, its thread may or may not be: run the plan again, it shows what is left")
        print(LIMIT)
        return 1
    print(f"resolve: {resolved} threads {'resolved' if go else 'to resolve (plan only: add --go)'}, {left} left open")
    print(LIMIT)
    return 0


REPLY = ("mutation($thread: ID!, $body: String!) { addPullRequestReviewThreadReply("
         "input: {pullRequestReviewThreadId: $thread, body: $body}) { comment { id } } }")


def words_and(words):
    """'a', 'a and b', 'a, b and c'."""
    return " and ".join(w for w in (", ".join(words[:-1]), words[-1]) if w) if words else ""


def section_header(number, head):
    """The heading of a pull request's section on the collection ticket."""
    return f"## Pull request #{number} at `{head[:7]}`"


def first_line(comment):
    """The first line of a comment as the REST endpoints send it."""
    return lf(comment.get("body")).split("\n", 1)[0]


def by_writer(comment):
    """`trusted` for a comment as the REST endpoints send it."""
    return trusted((comment.get("user") or {}).get("login", ""), comment.get("author_association"))


def low_links(repo, number, head, pages):
    """(count, addresses) of the low findings the review of `head` reported. The reviewer's notes on the
    pull request name each low with the head it was made on and the issue that collects it
    (tools/review/review.py), and the comments on that issue that open with kit.lows_header for this
    pull request and head are where the lows stand. The counts must agree: lows the notes name and no
    comment shows are a link nobody can write, a refusal, never a section that names fewer."""
    lows, issues, since = 0, set(), None
    for note in pages(f"repos/{repo}/issues/{number}/comments?per_page=100"):
        m = MARK.search(note.get("body") or "")
        mine = [f for f in json.loads(m.group(1)) if f.get("severity") == "low" and f.get("sha") == head] if m and by_writer(note) else []
        if not mine:
            continue
        named = re.search(r"collected in #(\d+)", note["body"])
        if not named:
            raise kit.Refused(f"the note {note.get('html_url')} names {len(mine)} low findings and no issue that collects them")
        lows, since = lows + len(mine), min(since or note["created_at"], note["created_at"])
        issues.add(int(named.group(1)))
    if not lows:
        return 0, []
    # the issue's comment follows its note; a minute earlier, so that a comment of the same second is listed too
    since = (datetime.datetime.strptime(since, "%Y-%m-%dT%H:%M:%SZ") - datetime.timedelta(minutes=1)).strftime("%Y-%m-%dT%H:%M:%SZ")
    parts = [c for n in sorted(issues) for c in pages(f"repos/{repo}/issues/{n}/comments?per_page=100&since={since}")
             if by_writer(c) and first_line(c) == kit.lows_header(number, head)]
    shown = sum(len(re.findall(r"^- \*\*low\*\* ", lf(c["body"]), re.M)) for c in parts)
    if shown != lows:
        raise kit.Refused(f"the reviewer's notes name {lows} low findings on {head[:8]}, and the comments of "
                          f"{', '.join(f'#{n}' for n in sorted(issues))} that name this pull request and head show {shown}")
    return lows, [c["html_url"] for c in parts]


def cap_section(number, head, rounds, found, analysis, lows):
    """The section of the collection ticket for a pull request at the cap: the head, the day of the
    round, each open finding with the address of its thread (the later findings of a thread point
    up to it), the session's reading, and the low findings with the addresses of their comments."""
    date = found[0][0]["comments"]["nodes"][0]["createdAt"][:10]
    lines = [section_header(number, head), "",
             f"{'One review round' if rounds == 1 else f'{rounds} review rounds, the last'} on {date}, the cap, at {head}; "
             "it merges with what it has. Open threads of that review, resolved on the pull request with a pointer here:", ""]
    for t, findings in found:
        url = t["comments"]["nodes"][0]["url"]
        if not findings:
            lines.append(f"- a post of the reviewer that names no file {url}")
        for n, f in enumerate(findings):
            lines.append(f"- **{f['severity']}** `{f['file']}` line {f['line']}: {f['title']} {url if n == 0 else '(in the thread above)'}")
    if analysis.strip():
        lines += ["", analysis.strip()]
    count, links = lows
    lines += ["", f"The {count} low finding{'s' if count != 1 else ''} of the same review: {words_and(links)}." if count
              else "The same review reported no low findings."]
    return "\n".join(lines) + "\n"


def cap_walk(repo, number, go, analysis="", get=None, pages=None, send=None):
    """The walk at the review cap; True when every finding thread is resolved, or without `go` when the
    plan was printed. Refused before any write: a pull request below the cap, none or several open
    collection tickets, an open finding made on another commit than the head the pull request shows
    (a finding of an earlier round, or a head that moved after the round at the cap, the owner's to
    decide), a thread in which a person wrote other than a move (read first), and with `go` a section
    to post with no reading, since a posted section is never written again. A section that is on
    the ticket, as a comment or in its body as the walks by hand wrote it, is not posted again, and a
    thread that a reply already moved gets no second one, so a walk that stopped is run again."""
    get, send = get or kit.gh_json, send or kit.gh_send
    pages = once(pages or kit.gh_pages)  # the rounds and the walk read the same list of tickets
    pr = get(f"repos/{repo}/pulls/{number}")
    head = pr["head"]["sha"]
    rounds, moved = leftovers(repo, pr, pages)
    if rounds < kit.ROUND_CAP:
        raise kit.Refused(f"{rounds} review rounds, below the cap of {kit.ROUND_CAP}: a finding is answered by its edit, not moved")
    open_tickets = tickets(repo, pages)
    if len(open_tickets) != 1:
        raise kit.Refused(f"{len(open_tickets)} open issues carry the label {kit.LEFTOVERS_LABEL}: the section goes on one")
    ticket = open_tickets[0]
    found = [(t, f) for t, f in finding_threads(fetch_threads(repo, number, get)) if not t["isResolved"]]
    if not found:
        print("cap: no finding thread is open, so nothing moves to the collection ticket")
        return True
    for t, _ in found:
        sha = (t["comments"]["nodes"][0].get("originalCommit") or {}).get("oid")
        if sha != head:
            raise kit.Refused(f"the open finding on {t.get('path')} ({t.get('id')}) was made on {sha}, not on the head {head}: "
                              "the walk covers the head the round at the cap reviewed")
        if any(spoke(c) and not moved(c) for c in t["comments"]["nodes"][1:]):
            raise kit.Refused(f"a person wrote in the thread on {t.get('path')} ({t.get('id')}): read and answer it first")
    posted = next((c["html_url"] for c in pages(f"repos/{repo}/issues/{ticket['number']}/comments?per_page=100")
                   if by_writer(c) and first_line(c) == section_header(number, head)), None)
    if not posted and section_header(number, head) in lf(ticket.get("body")).split("\n"):
        posted = ticket["html_url"]
    if posted:
        print(f"cap: the section is on #{ticket['number']} already: {posted}")
    else:
        if go and not analysis.strip():
            raise kit.Refused("no reading: --analysis FILE holds what the code at the head shows, and a posted section "
                              "is not written again")
        section = cap_section(number, head, rounds, found, analysis, low_links(repo, number, head, pages))
        print(section)  # in the log before the write that can be refused
        if go:
            posted = send("POST", f"repos/{repo}/issues/{ticket['number']}/comments", {"body": section}).get("html_url")
            if not posted:
                raise kit.Refused(f"the forge's answer to the section names no comment: read #{ticket['number']}, then run the walk again")
            print(f"cap: posted the section: {posted}")
            time.sleep(1)
    reply = (f"Moved to the collection ticket at the review cap: #{ticket['number']}, section "
             f"\"Pull request #{number} at `{head[:7]}`\": {posted or '(the address of the section once posted)'}")
    for t, _ in found:
        if any(moved(c) for c in t["comments"]["nodes"][1:]):
            continue
        if not go:
            print(f"would reply in {t.get('path')} ({t['id']}): {reply}")
            continue
        get("graphql", "-f", f"query={REPLY}", "-f", f"thread={t['id']}", "-f", f"body={reply}")
        print(f"replied in {t.get('path')} ({t['id']})")
        time.sleep(1)
    if not go:
        print(f"cap: {len(found)} finding threads to move and resolve (plan only: add --go)")
        return True
    resolve_threads(repo, number, True, get, head, pages)  # it reads the threads again, with the replies
    still = [t for t, _ in finding_threads(fetch_threads(repo, number, get)) if not t["isResolved"]]
    for t in still:
        print(f"still open: {t.get('path')} ({t.get('id')})")
    print(f"cap: {len(found) - len(still)} of {len(found)} finding threads moved to #{ticket['number']} and resolved")
    return not still


def cap_command(repo, number, go, reading=None, get=None, pages=None, send=None):
    """The exit code of the cap command: 0 when the walk ended with every finding thread resolved, or
    printed its plan; 1 when it was refused, the reading (a file) or a read of the forge or a write
    failed, a thread is still open, or the run was interrupted. What was written before a failure
    stands, a write cut off before its answer may have landed, and the walk skips what is there when
    run again."""
    try:
        done = cap_walk(repo, number, go, kit.read(reading) if reading else "", get, pages, send)
    except BaseException as e:  # whatever failed, an interrupt too: the lines printed above are what was written
        print(f"ERROR: cap stopped: {type(e).__name__}: {e}. What is printed above as posted, replied or resolved "
              "stands, and a write in flight when it stopped may have landed too; run the walk again, it reads the "
              "ticket and the threads and skips what is there")
        print(LIMIT)
        return 1
    print(LIMIT)
    return 0 if done else 1


def fetch_threads(repo, number, get=None):
    owner, name = repo.split("/")
    threads, after = [], None
    while True:
        # -f sends a string, -F a typed value: an all-digit repository name must stay a String for GraphQL
        args = ["graphql", "-f", f"query={THREADS}", "-f", f"owner={owner}", "-f", f"name={name}", "-F", f"pr={number}"]
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


def option(argv, flag):
    """The value after `flag`, or after `flag=`; None without the flag; the flag without a value is a refusal."""
    joined = [a for a in argv if a.startswith(flag + "=")]
    if flag not in argv and not joined:
        return None
    value = joined[0][len(flag) + 1:] if joined else (argv[argv.index(flag) + 1:] or [""])[0]
    if not value or value.startswith("--"):
        raise kit.Refused(f"{flag} needs a value")
    return value


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
        rounds, moved = leftovers(repo, pr, pages)
        print(LIMIT)
        return (findings_errors(threads, changed, moved, rounds >= kit.ROUND_CAP),
                f"{len(threads)} threads, {len(finding_threads(threads))} with findings, {rounds} review rounds (the cap is {kit.ROUND_CAP})")
    raise kit.Refused(f"unknown gate {gate!r}: title, breadth, findings or resolve")


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


def fixture_pull_request():
    """The forge's real answer for one pull request (see tools/fixtures/SOURCES.txt). {} when the file
    is missing or unreadable, which fails the cases that need it instead of crashing the suite."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "pull-request.json")
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def fixture_issues(path=None):
    """The forge's real answer for the open issues of the collection label (tools/fixtures/leftovers-issues.json,
    see tools/fixtures/SOURCES.txt). [] when the file is missing, unreadable or not a list of issue records,
    which fails the case that needs it instead of crashing the suite. `path` is a self-test's own file."""
    path = path or os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "leftovers-issues.json")
    try:
        with open(path, encoding="utf-8") as f:
            issues = json.load(f)
    except (OSError, ValueError):
        return []
    return issues if isinstance(issues, list) and all(isinstance(i, dict) for i in issues) else []


def fixture_json(name):
    """A payload the forge really sent, from tools/fixtures/ (see tools/fixtures/SOURCES.txt). None when the
    file is missing or unreadable, which fails the cases that need it instead of crashing the suite."""
    try:
        with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", name), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def fixture_answer():
    """The forge's real answer to RESOLVE (see tools/fixtures/SOURCES.txt). {} when the file is missing
    or unreadable, which fails the cases that need it instead of crashing the suite."""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures", "resolve-thread.json")
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def self_test():
    threads = fixture_threads()
    captured = fixture_answer()
    recorded = fixture_pull_request()  # each fake below answers it, edited only where a comment says so
    cases = []

    def check(name, errors, needle):
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    cases.append(("fixture: the recorded pull request carries a title and a head sha, which the fakes below answer",
                  bool(recorded.get("title")) and bool((recorded.get("head") or {}).get("sha")), sorted(recorded)))

    def got(fn):
        """What fn returned, or the exception it raised, as a value; an interrupt too, so that a case
        about one can fail instead of ending the suite."""
        try:
            return fn()
        except BaseException as e:
            return e

    def quietly(fn):
        """(what fn returned or raised, what it printed)."""
        with contextlib.redirect_stdout(io.StringIO()) as printed:
            out = got(fn)
        return out, printed.getvalue()

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
    noreply = "<123+someone@" + "users.noreply.github.com>"
    check("a co-author's GitHub no-reply address in a commit's trailer is clean",
          title_errors("ok", [f"Fix\n\nCo-authored-by: Someone {noreply}\n"]), None)
    check("a GitHub no-reply address in a commit message's subject line is machine-bound",
          title_errors("ok", [f"Fix, ask {noreply}\n"]), "the message of commit 1 of 1: machine-bound string (email)")
    check("a GitHub no-reply address in a commit message's body, after the blank line, is machine-bound: a trailer line alone passes",
          title_errors("ok", [f"Fix\n\nask {noreply}\n"]), "the message of commit 1 of 1: machine-bound string (email)")
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
    check("many files without the section", breadth_errors("", [f"knowledge/e{n}.md" for n in range(16)]), "16 files")
    check("fifteen files are not many", breadth_errors("", [f"knowledge/e{n}.md" for n in range(15)]), None)

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
        moving = {"body": "Moved to the collection ticket #39, with the head of this review and the link of this thread."}
        to_ticket = moved_to("o/n", [39])
        check("a finding moved to the collection ticket is answered, on the design record too",
              findings_errors(variant(True, replies=1, path="kurz-design.md", reply=moving), same, to_ticket), None)
        check("a moved finding that is still unresolved blocks: the move is no resolution",
              findings_errors(variant(False, replies=1, path="kurz-design.md", reply=moving), same, to_ticket), "unresolved review finding")
        addresses = [findings_errors(variant(True, replies=1, path="kurz-design.md", reply={"body": f"Moved to {url}"}), same, to_ticket)
                     for url in ("https://github.com/o/n/issues/39", "https://github.com/O/N/issues/39#issuecomment-1",
                                 "https://github.com/x/n/issues/39", "https://github.com/o/n/issues/390")]
        cases.append(("the collection ticket's address in this repository moves a finding, in any case; another repository's or another number's does not",
                      [bool(e) for e in addresses] == [False, False, True, True], addresses))
        check("a reply that links another issue moves nothing",
              findings_errors(variant(True, replies=1, path="kurz-design.md", reply={"body": "see #40"}), same, to_ticket), "without changing the file")
        check("#39 inside a word, a path or a character reference is no link",
              findings_errors(variant(True, replies=1, path="kurz-design.md", reply={"body": "PR#39, x/#39 and &#39;"}), same, to_ticket),
              "without changing the file")
        check("an outsider's reply that links the collection ticket moves nothing",
              findings_errors(variant(True, replies=1, path="kurz-design.md", reply={**moving, "authorAssociation": "NONE"}), same, to_ticket),
              "without changing the file")
        check("the reviewer's own post that names the collection ticket moves nothing",
              findings_errors(variant(True, replies=1, path="kurz-design.md", reply={"body": MARKER + "findings [] --> #39"}), same, to_ticket),
              "without changing the file")
        check("with no collection ticket nothing moves", findings_errors(variant(True, replies=1, path="kurz-design.md", reply=moving), same,
                                                                         moved_to("o/n", [])), "without changing the file")
        open_ones = ((True, "kurz-design.md"), (True, "tools/kit.py"), (False, "kurz-design.md"))
        at_cap = [findings_errors(variant(resolved, path=path), same, to_ticket, capped=True) for resolved, path in open_ones]
        below_cap = [findings_errors(variant(resolved, path=path), same, to_ticket) for resolved, path in open_ones]
        cases.append(("findings: at the cap every finding still open names the move to the collection ticket, never an edit, which no push follows; below the cap none names the move",
                      all(len(e) == 1 and e[0].endswith(MOVE) and "edit it" not in e[0] and "edit the file" not in e[0] for e in at_cap)
                      and all(len(e) == 1 and MOVE not in e[0] for e in below_cap), repr((at_cap, below_cap))))
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

    # the collection tickets, from the forge's real answer for the open issues of the collection label
    listed_issues = fixture_issues()
    real_label = next((label for i in listed_issues for label in i.get("labels") or []), None)
    cases.append(("fixture: the captured list of the collection label holds the open ticket #39 with that label, the shape the cases below read",
                  bool(listed_issues) and listed_issues[0].get("number") == 39 and "pull_request" not in listed_issues[0]
                  and (real_label or {}).get("name") == kit.LEFTOVERS_LABEL, repr(listed_issues)[:200]))
    with kit.scratch("pr-gates-fixture-") as folder:
        bad = os.path.join(folder, "issues.json")
        with open(bad, "w", encoding="utf-8") as f:
            f.write('{"message": "Not Found"}')  # the shape of a forge error, which a capture could hold
        cases.append(("fixture: a capture that is not a list of issues reads as none, so the case that needs it fails, not the suite",
                      fixture_issues(bad) == [], repr(fixture_issues(bad))))
    labelled = lambda *names: dict(recorded, labels=[dict(real_label or {}, name=n) for n in names])  # edited: the captured label, renamed
    # the captured ticket as it is, and edited: a pull request #41 beside it, which the list endpoint names too
    tickets = [listed_issues[0] if listed_issues else {},
               dict(listed_issues[0] if listed_issues else {}, number=41, pull_request={"url": "x"})]
    asked = []

    def ticket_pages(path):
        asked.append(path)
        return json.loads(json.dumps(tickets))

    reply = {"body": "Moved to #39 and #41", "author": {"login": "a-maintainer"}, "authorAssociation": "OWNER"}
    out = got(lambda: leftovers("o/n", labelled("bug", f"{kit.ROUND_LABEL}{kit.ROUND_CAP - 1}"), ticket_pages))
    cases.append(("leftovers: below the cap the collection tickets are not asked for, and no reply moves a finding",
                  type(out) is tuple and out[0] == kit.ROUND_CAP - 1 and out[1](reply) is False and asked == [], repr((out, asked))))
    out = got(lambda: leftovers("o/n", labelled(f"{kit.ROUND_LABEL}1", f"{kit.ROUND_LABEL}{kit.ROUND_CAP}"), ticket_pages))
    cases.append(("leftovers: at the cap the open issues of the collection label are read, and a reply that links one moves a finding",
                  type(out) is tuple and out[0] == kit.ROUND_CAP and out[1](reply) is True
                  and asked == [f"repos/o/n/issues?state=open&labels={kit.LEFTOVERS_LABEL}&per_page=100"], repr((out, asked))))
    cases.append(("leftovers: a pull request the list of issues names is no collection ticket",
                  type(out) is tuple and out[1](dict(reply, body="Moved to #41")) is False, repr(out)))
    out = got(lambda: leftovers("o/n", {k: v for k, v in recorded.items() if k != "labels"}, ticket_pages))
    cases.append(("leftovers: a pull request the forge sent without its labels is a refusal, not a count of none",
                  isinstance(out, kit.Refused) and "without its labels" in str(out), repr(out)))

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
                return dict(recorded)  # unedited: its head is not the commit of the finding
            return get

        out = got(lambda: run_gate("findings", "o/n", 1, paged(lambda ref: "blob@" + ref)))
        cases.append(("findings: both pages of threads are read, and a blob that differs between the finding's commit and the head is the edit",
                      type(out) is tuple and [e[:28] for e in out[0]] == ["unresolved review finding on"] and out[1].startswith("2 threads, 2 with"),
                      repr(out)))
        out = got(lambda: run_gate("findings", "o/n", 1, paged(lambda ref: "blob")))
        cases.append(("findings: the same blob at the finding's commit and at the head is no edit",
                      type(out) is tuple and len(out[0]) == 2 and any("without changing the file" in e for e in out[0]), repr(out)))

        def capped_at(rounds, reply=moving):
            """A forge whose pull request carries the label of `rounds` rounds and one resolved thread on the design
            record, its file unchanged, with a reply, by default one that moves it to the collection ticket."""
            def get(*args):
                if args[0] == "graphql":
                    page = {"nodes": variant(True, replies=1, path="kurz-design.md", reply=reply), "pageInfo": {"hasNextPage": False}}
                    return {"data": {"repository": {"pullRequest": {"reviewThreads": page}}}}
                if "/contents/" in args[0]:
                    return {"sha": "blob"}
                return labelled(f"{kit.ROUND_LABEL}{rounds}")
            return get

        below, at = (got(lambda: run_gate("findings", "o/n", 1, capped_at(n), ticket_pages)) for n in (kit.ROUND_CAP - 1, kit.ROUND_CAP))
        cases.append(("findings: a thread moved to the collection ticket passes at the cap, and the same thread below the cap is an error",
                      type(below) is tuple and any("without changing the file" in e for e in below[0])
                      and type(at) is tuple and at[0] == [] and f"{kit.ROUND_CAP} review rounds" in at[1], repr((below, at))))
        below, at = (got(lambda: run_gate("findings", "o/n", 1, capped_at(n, {"body": "because"}), ticket_pages)) for n in (kit.ROUND_CAP - 1, kit.ROUND_CAP))
        cases.append(("findings: at the cap the gate's error for a finding not moved names the move, and below the cap the edit",
                      type(below) is tuple and any("edit the file" in e for e in below[0])
                      and type(at) is tuple and len(at[0]) == 1 and at[0][0].endswith(MOVE), repr((below, at))))

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
        both = variant(False)
        named = json.dumps([dict(findings[0], file="a.md"), dict(findings[0], file="b.md")])
        both[0]["comments"]["nodes"][0]["body"] = MARK.sub(lambda m: f"<!-- kurz-review:findings {named} -->", both[0]["comments"]["nodes"][0]["body"])
        half, whole = resolve_plan(both, lambda p, s: p == "a.md"), resolve_plan(both, differs)
        cases.append(("resolve: a finding whose files did not all change is left open, naming the unchanged",
                      counts(half) == [0, 1] and half[1][0][2] == "not every file changed: b.md did not" and counts(whole) == [1, 0],
                      repr((half, whole))))
        plan = resolve_plan(both, same)
        cases.append(("resolve: a finding of several files of which none changed is left open, saying so",
                      counts(plan) == [0, 1] and plan[1][0][2] == "none of its files changed", repr(plan)))
        calls = []

        def flaky(path, sha):
            """a.md changed; b.md answers no, then yes: a forge whose second read disagrees with the first."""
            calls.append(path)
            return path == "a.md" or calls.count(path) > 1

        plan = resolve_plan(both, flaky)
        cases.append(("resolve: each file of a finding is read once, so a second read cannot disagree with the first",
                      counts(plan) == [0, 1] and plan[1][0][2] == "not every file changed: b.md did not" and sorted(calls) == ["a.md", "b.md"],
                      repr((plan, calls))))
        plan = resolve_plan(variant(False, replies=1), differs)
        cases.append(("resolve: a finding thread in which a person wrote is left open, whatever changed",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))
        spellings = [resolve_plan(variant(False, replies=1, reply={"body": MARKER + "findings [] -->\nstill there",
                                                                 "author": {"login": login}}), differs)
                     for login in ("github-actions", "github-actions[bot]")]  # as GraphQL and as REST spell the Actions login
        cases.append(("resolve: a later post of the reviewer itself, under the Actions login as GraphQL or REST spell it, is no person's reply",
                      all(counts(p) == [1, 0] for p in spellings), repr(spellings)))
        plan = resolve_plan(variant(False, replies=1, reply={"author": {"login": "github-actions"}}), differs)
        cases.append(("resolve: a post under the reviewer's login without its marker holds the thread",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))
        plan = resolve_plan(variant(False, replies=1, reply={"authorAssociation": "NONE"}), differs)
        cases.append(("resolve: an outsider's reply holds the thread too",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))
        plan = resolve_plan(variant(False, replies=1, reply={"body": MARKER + "findings [] --> pasted"}), differs)
        cases.append(("resolve: a marker in a person's reply does not make it the reviewer's",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))
        bare = [dict(plain[0], path=None)]
        plan = resolve_plan(bare, differs)
        cases.append(("resolve: a person's thread without a path names no placeholder",
                      counts(plan) == [0, 1] and plan[1][0][1] == [], repr(plan)))
        still = (thread["comments"]["nodes"][0].get("originalCommit") or {}).get("oid")  # the real finding's commit
        plan = resolve_plan(variant(False), same, head=still)
        cases.append(("resolve: a finding whose commit is still the head is left open, saying so",
                      bool(still) and counts(plan) == [0, 1] and plan[1][0][2].startswith("the head is still the finding's commit"),
                      repr((still, plan))))
        plan = resolve_plan(variant(False, replies=1, path="kurz-design.md", reply=moving), same, head=still, moved=to_ticket)
        cases.append(("resolve: a finding thread whose replies all move it to the collection ticket is resolved, whatever changed and whatever the head",
                      counts(plan) == [1, 0], repr(plan)))
        asking = variant(False, replies=1, path="kurz-design.md", reply=moving)
        asking[0]["comments"]["nodes"].append(dict(asking[0]["comments"]["nodes"][0], body="Why not fix it here?",
                                                   author={"login": "someone"}, authorAssociation="NONE"))
        plan = resolve_plan(asking, same, moved=to_ticket)
        cases.append(("resolve: a person's post beside the move holds the thread: it is read first",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))
        plan = resolve_plan(variant(False, replies=1, path="kurz-design.md", reply=moving), same)
        cases.append(("resolve: without the cap's moves the same reply holds the thread",
                      counts(plan) == [0, 1] and plan[1][0][2].startswith("a person wrote in it"), repr(plan)))

        PUSHED = "0123456789abcdef0123456789abcdef01234567"  # the head the fake forge shows, commit-shaped

        def refused_answer():
            """The captured answer, edited to say the thread is not resolved (a test may edit a loaded payload)."""
            answer = json.loads(json.dumps(captured))
            thread = ((answer.get("data") or {}).get("resolveReviewThread") or {}).get("thread")
            if isinstance(thread, dict):
                thread["isResolved"] = False
            return answer

        def resolving(blob, says=True, threads=1, breaks_at=None, error=None, head=PUSHED, pr=None, node=None):
            """A forge for resolve_threads: the pull request (`pr`, else the recorded one) at `head`, one page with the real
            thread unresolved, or `node` (`threads` copies with their own ids), blob(ref) for a file, and the mutation, which it
            records and answers as told, or raises `error` (an error nobody expected) at write `breaks_at`.
            The mutation's answer is the captured one (tools/fixtures/resolve-thread.json), edited to say the
            thread is not resolved where `says` is false. `get.asked` lists what was asked."""
            written, asked = [], []

            def get(*args):
                asked.append(args[0])
                if args[0] == "graphql" and args[2].startswith("query=mutation"):
                    if breaks_at is not None and len(written) == breaks_at:
                        raise error or RuntimeError("an answer nobody expected")  # a kind no except tuple of this tool ever named
                    written.append(args[-1])
                    return json.loads(json.dumps(captured)) if says else refused_answer()
                if args[0] == "graphql":
                    nodes = [dict(node or variant(False)[0], id=f"t{n}") for n in range(threads)]
                    page = {"nodes": nodes, "pageInfo": {"hasNextPage": False, "endCursor": None}}
                    return {"data": {"repository": {"pullRequest": {"reviewThreads": page}}}}
                if "/contents/" in args[0]:
                    return {"sha": blob(args[0].split("?ref=")[1])}
                return dict(pr or recorded, head=dict(recorded.get("head") or {}, sha=head))  # edited: the head this case pushed
            get.asked = asked
            return get, written

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
            cases.append(("resolve: a thread whose file did not change is not written, with --go",
                          out == (0, 1) and written == [] and "the file did not change" in printed, repr((out, written, printed))))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get, expect="f" * 40))
            cases.append(("resolve: a pull request that shows another head than the pushed one is refused before any read of a thread or any write",
                          type(out) is kit.Refused and f"shows head {PUSHED}, not {'f' * 40}" in str(out) and written == []
                          and get.asked == ["repos/o/n/pulls/1"], repr((out, written, get.asked))))
            forms = []
            for form in (PUSHED, PUSHED[:7], PUSHED[:12].upper()):
                get, written = resolving(lambda ref: "blob@" + ref)
                forms.append((quietly(lambda: resolve_threads("o/n", 1, True, get, expect=form))[0], written))
            cases.append(("resolve: --head takes the pushed commit in full, as a prefix of seven digits, and in upper case",
                          all(out == (1, 0) and written == ["thread=t0"] for out, written in forms), repr(forms)))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get, expect="xyz"))
            cases.append(("resolve: --head that is no commit prefix is refused",
                          type(out) is kit.Refused and "no commit" in str(out) and written == [], repr((out, written))))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get, expect=PUSHED[:6]))
            cases.append(("resolve: --head shorter than seven digits is refused as no commit",
                          type(out) is kit.Refused and "no commit" in str(out) and written == [], repr((out, written))))
            get, written = resolving(lambda ref: "blob@" + ref, head=still)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get, expect=still))
            cases.append(("resolve: with --head matched, a finding on that head is reported as sitting on the pushed head",
                          bool(still) and out == (0, 1) and written == [] and "sits on the pushed head" in printed
                          and "run the plan again" not in printed, repr((out, written, printed))))
            get, written = resolving(lambda ref: "blob@" + ref, head=still)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: a forge that still shows the finding's commit as the head writes nothing, and says so",
                          bool(still) and out == (0, 1) and written == [] and "the head is still the finding's commit" in printed,
                          repr((out, written, printed))))
            get, written = resolving(lambda ref: "blob@" + ref, says=False)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: an answer that does not say resolved is a refusal", type(out) is kit.Refused, repr(out)))
            cases.append(("resolve: a thread is printed as resolved only after the forge said so",
                          type(out) is kit.Refused and resolved_lines(printed) == 0, repr(printed)))
            cases.append(("resolve: the forge's captured answer to the mutation reads as resolved, and the same answer edited to false does not",
                          "resolveReviewThread" in (captured.get("data") or {}) and said_resolved(captured) is True
                          and said_resolved(refused_answer()) is False, repr(captured)))
            slept.clear()
            get, written = resolving(lambda ref: "blob@" + ref, threads=2)
            out, printed = quietly(lambda: resolve_threads("o/n", 1, True, get))
            cases.append(("resolve: writes are a second apart", out == (2, 0) and slept == [1, 1], repr((out, slept))))
            get, written = resolving(lambda ref: "blob@" + ref, threads=2, breaks_at=1)
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: a failure of any kind after a write says what stands",
                          out == 1 and resolved_lines(printed) == 1 and "resolve stopped: RuntimeError" in printed
                          and "re-run the gates after resolving" in printed, repr((out, printed))))
            get, written = resolving(lambda ref: "blob@" + ref, threads=2, breaks_at=1, error=KeyboardInterrupt())
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: an interrupt during --go says what stands too",
                          out == 1 and resolved_lines(printed) == 1 and "resolve stopped: KeyboardInterrupt" in printed
                          and "re-run the gates after resolving" in printed, repr((out, printed))))
            get, written = resolving(lambda ref: "blob@" + ref)
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get))
            cases.append(("resolve: the command ends 0 when the plan ran, and prints the known limit",
                          out == 0 and "1 threads resolved" in printed and "re-run the gates after resolving" in printed, repr((out, printed))))
            asked.clear()
            get, written = resolving(lambda ref: "blob", pr=labelled(f"{kit.ROUND_LABEL}{kit.ROUND_CAP}"),
                                     node=variant(False, replies=1, path="kurz-design.md", reply=moving)[0])
            out, printed = quietly(lambda: resolve_command("o/n", 1, True, get, pages=ticket_pages))
            cases.append(("resolve: at the cap the collection tickets are read, and a thread moved to one is resolved although its file did not change",
                          out == 0 and written == ["thread=t0"] and len(asked) == 1, repr((out, written, asked, printed))))
        finally:
            time.sleep = saved_sleep

    # edited: no description, so the breadth gate has a section to miss; the recorded one lists 1 commit, as these cases do
    pr = dict(recorded, body="")
    commit, changed = {"commit": {"message": "x\n\n[skip ci]"}}, {"filename": "tools/kit.py"}
    listed = lambda commits, files: (lambda path: list(commits) if "/commits" in path else list(files))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: pr, listed([], [changed])))
    cases.append(("title: a pull request that lists no commits is refused", type(out) is kit.Refused and "no commits" in str(out), repr(out)))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: pr, listed([commit], [])))
    cases.append(("title: the messages of the commits the pull request lists are read",
                  type(out) is tuple and any("a commit message carries" in e for e in out[0]) and "1 commit messages" in out[1], repr(out)))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: {**pr, "commits": 251}, listed([commit], [])))  # edited: more commits than the forge lists
    cases.append(("title: a pull request with more commits than the forge listed is refused, not passed on the ones read",
                  type(out) is kit.Refused and "251 commits" in str(out) and "listed 1" in str(out), repr(out)))
    out = got(lambda: run_gate("title", "o/n", 1, lambda path: dict(pr, body=None), listed([{"commit": {"message": "x"}}], [])))  # edited: a null body, as the forge sends one
    cases.append(("title: a description the forge sends as null is read as empty", out == ([], "title, description and 1 commit messages"),
                  repr(out)))
    out = got(lambda: run_gate("breadth", "o/n", 1, lambda path: dict(pr, body=None), listed([], [changed])))  # edited: a null body, as the forge sends one
    cases.append(("breadth: a description the forge sends as null needs the section, like an empty one",
                  type(out) is tuple and any("Blast radius" in e for e in out[0]), repr(out)))
    sent = []

    def threads_once(*args):
        sent.append(args)
        return {"data": {"repository": {"pullRequest": {"reviewThreads": {"nodes": [], "pageInfo": {"hasNextPage": False}}}}}}

    fetched = got(lambda: fetch_threads("o/0123", 7, threads_once))  # kept: a crash in the call must fail the case
    flags = {a: sent[0][n - 1] for n, a in enumerate(sent[0]) if a in ("owner=o", "name=0123", "pr=7")} if sent else {}
    cases.append(("threads: owner and name go as strings, so an all-digit name stays a name; the number goes typed",
                  fetched == [] and flags == {"owner=o": "-f", "name=0123": "-f", "pr=7": "-F"}, repr((fetched, flags))))
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
    heads = (got(lambda: option(["resolve", "--head", "abc"], "--head")), got(lambda: option(["resolve"], "--head")),
             got(lambda: option(["resolve", "--head"], "--head")), got(lambda: option(["resolve", "--head", "--go"], "--head")))
    cases.append(("option: --head reads its value, none is None, and the flag without a value or before another flag is refused",
                  heads[:2] == ("abc", None) and all(type(r) is kit.Refused for r in heads[2:]), repr(heads)))
    joined = (got(lambda: option(["resolve", "--head=abc", "--go"], "--head")), got(lambda: option(["resolve", "--head="], "--head")))
    cases.append(("option: --head=SHA is read too, and --head= without a value is refused",
                  joined[0] == "abc" and type(joined[1]) is kit.Refused, repr(joined)))

    # the walk at the cap, over the thread, the note and the comment on the lows issue of pull request 82 as captured
    def captured_list(name, *keys):
        """A list in a captured payload, under its keys; [] for a file that is missing or of another shape,
        which fails the fixture cases below instead of crashing the suite."""
        value = fixture_json(name)
        for key in keys:
            value = value.get(key) if isinstance(value, dict) else None
        return value if isinstance(value, list) else []

    walked = captured_list("cap-thread.json", "data", "repository", "pullRequest", "reviewThreads", "nodes")
    notes82, lows82 = captured_list("cap-notes.json"), captured_list("cap-lows.json")
    cap_found = finding_threads(walked)
    opening = cap_found[0][0]["comments"]["nodes"][0] if cap_found else {}
    reviewed = (opening.get("originalCommit") or {}).get("oid") or ""
    cases.append(("fixture: the captured thread of pull request 82 holds two findings made on its head, with the address and the time of its first post",
                  len(cap_found) == 1 and len(cap_found[0][1]) == 2 and len(reviewed) == 40 and all(f.get("sha") == reviewed for f in cap_found[0][1])
                  and bool(opening.get("url")) and bool(opening.get("createdAt")), repr(walked)[:300]))
    note_mark = MARK.search((notes82[0].get("body") or "") if notes82 else "")
    noted = [f for f in json.loads(note_mark.group(1)) if f.get("severity") == "low" and f.get("sha") == reviewed] if note_mark else []
    cases.append(("fixture: the captured note names five lows on that head, collected in #19, and the captured comment on #19 opens with the lows header of pull request 82 at that head and lists five",
                  len(noted) == 5 and "collected in #19" in notes82[0]["body"] and len(lows82) == 1 and bool(reviewed)
                  and first_line(lows82[0]) == kit.lows_header(82, reviewed) and len(re.findall(r"^- \*\*low\*\* ", lf(lows82[0]["body"]), re.M)) == 5,
                  repr((notes82, lows82))[:300]))
    cases.append(("words: one word, two with and, three with a comma and and",
                  [words_and([]), words_and(["a"]), words_and(["a", "b"]), words_and(["a", "b", "c"])] == ["", "a", "a and b", "a, b and c"], ""))
    if cap_found and len(noted) == 5 and lows82 and listed_issues:
        captured_ticket = listed_issues[0]  # the collection ticket #39 as captured, its body with the section of pull request 33
        posted_at = "https://github.com/o/n/issues/39#issuecomment-1"

        def walking(rounds=kit.ROUND_CAP, thread=None, notes=None, lows=None, open_tickets=None, ticket_comments=(), flips=True):
            """A forge for cap_walk. The pull request is the recorded one, edited to carry `rounds` rounds and the captured
            thread's commit as its head. Its one thread is `thread`, or the captured one edited to be unresolved with its
            first post alone (the walk's reply and resolution are what a case writes). `notes` on it and `lows` on #19 are
            the captured ones unless given; the tickets are `open_tickets`, else the captured #39 with `ticket_comments`.
            A reply adds itself to the thread (an edit: the captured first post with the reply's text under a writer's
            login), the resolution marks it resolved unless `flips` is false, and a post on #39 is answered with the
            captured comment on #19, edited to carry the posted text and an address on #39. `log` lists the writes,
            `reads` the lists asked for."""
            state = json.loads(json.dumps(thread or dict(walked[0], isResolved=False, comments={"nodes": walked[0]["comments"]["nodes"][:1]})))
            log, reads, comments = [], [], [json.loads(json.dumps(c)) for c in ticket_comments]
            pr = labelled(f"{kit.ROUND_LABEL}{rounds}")
            pr["head"] = dict(pr.get("head") or {}, sha=reviewed)

            def get(*args):
                if args[0] == "graphql" and "addPullRequestReviewThreadReply" in args[2]:
                    body = next(a for a in args if a.startswith("body="))[len("body="):]
                    log.append(("reply", next(a for a in args if a.startswith("thread=")), body))
                    state["comments"]["nodes"].append(dict(state["comments"]["nodes"][0], body=body, author={"login": "a-maintainer"},
                                                           authorAssociation="OWNER"))
                    return {}  # the walk reads nothing of the answer: the threads it reads back are its proof
                if args[0] == "graphql" and args[2].startswith("query=mutation"):
                    log.append(("resolve", args[-1]))
                    state["isResolved"] = state["isResolved"] or flips
                    return json.loads(json.dumps(captured))
                if args[0] == "graphql":
                    page = {"nodes": [json.loads(json.dumps(state))], "pageInfo": {"hasNextPage": False}}
                    return {"data": {"repository": {"pullRequest": {"reviewThreads": page}}}}
                if "/contents/" in args[0]:
                    return {"sha": "blob"}
                return json.loads(json.dumps(pr))

            def pages(path):
                reads.append(path)
                answers_ = {"labels=": [captured_ticket] if open_tickets is None else open_tickets, "/issues/82/comments": notes82 if notes is None else notes,
                            "/issues/19/comments": lows82 if lows is None else lows, "/issues/39/comments": comments}
                return json.loads(json.dumps(next(v for k, v in answers_.items() if k in path)))

            def send(method, path, payload):
                log.append(("post", path, payload["body"]))
                comments.append(dict(lows82[0], body=payload["body"], html_url=posted_at))
                return comments[-1]
            return get, pages, send, log, reads

        def walk(go, **forge):
            """(what cap_walk returned or raised, what it printed, the writes, the reads)."""
            get, pages, send, log, reads = walking(**forge)
            out, printed = quietly(lambda: cap_walk("o/n", 82, go, "What the code at that head shows.", get, pages, send))
            return out, printed, log, reads

        section82 = (
            "## Pull request #82 at `fb82fbc`\n\n"
            "One review round on 2026-10-29, the cap, at fb82fbcd5dcbdc39dd0b0fb1acceed0f18d7a139; it merges with what it has. "
            "Open threads of that review, resolved on the pull request with a pointer here:\n\n"
            "- **medium** `compiler/Kurz.Compiler.Tests/FileTests.cs` line 65: Windows refusal is checked against the emitter's own constant, "
            "and not at its line https://github.com/SymoHTL/Kurz/pull/82#discussion_r4418327083\n"
            "- **medium** `compiler/Kurz.Compiler.Tests/FileTests.cs` line 183: The positive control does not test the limit its comment "
            "describes (in the thread above)\n\n"
            "What the code at that head shows.\n\n"
            "The 5 low findings of the same review: https://github.com/SymoHTL/Kurz/issues/19#issuecomment-6509258622.\n")
        slept = []
        saved_sleep, time.sleep = time.sleep, slept.append
        try:
            out, printed, log, reads = walk(False)
            cases.append(("cap: the section holds the head, the day, each finding with its thread's address once, the reading and the lows with their address, as the walk by hand wrote it for this thread",
                          section82 in printed, printed))
            cases.append(("cap: without --go the section and the replies are printed and nothing is written",
                          out is True and log == [] and "would reply in" in printed, repr((out, log))))
            slept.clear()
            out, printed, log, reads = walk(True)
            reply = next((w[2] for w in log if w[0] == "reply"), "")
            cases.append(("cap: with --go the section is posted on the ticket, the thread gets the reply that links the ticket and the section, then it is resolved, a second apart",
                          out is True and [w[0] for w in log] == ["post", "reply", "resolve"] and log[0][1] == "repos/o/n/issues/39/comments"
                          and log[0][2] == section82 and reply.startswith("Moved to the collection ticket at the review cap: #39, ")
                          and reply.endswith(posted_at) and moved_to("o/n", [39])({"body": reply, "authorAssociation": "OWNER"})
                          and slept == [1, 1, 1] and sum("labels=" in r for r in reads) == 1, repr((out, log, slept, reads))))
            out, printed, log, reads = walk(True, rounds=kit.ROUND_CAP - 1)
            cases.append(("cap: a pull request below the cap is refused before any list is read and anything is written",
                          type(out) is kit.Refused and "below the cap" in str(out) and reads == [] and log == [], repr((out, reads, log))))
            refusals = [walk(True, open_tickets=t)[::2] for t in ([], [captured_ticket, dict(captured_ticket, number=40)])]
            cases.append(("cap: no open collection ticket, and two, are refused before anything is written",
                          all(type(out) is kit.Refused and "the section goes on one" in str(out) and log == [] for out, log in refusals), repr(refusals)))
            elsewhere = json.loads(json.dumps(walked[0]))
            elsewhere["isResolved"], elsewhere["comments"]["nodes"] = False, elsewhere["comments"]["nodes"][:1]
            elsewhere["comments"]["nodes"][0]["originalCommit"] = {"oid": "0" * 40}  # edited: a finding of another head
            out, printed, log, reads = walk(True, thread=elsewhere)
            cases.append(("cap: an open finding made on another commit than the head is refused before anything is written",
                          type(out) is kit.Refused and f"not on the head {reviewed}" in str(out) and log == [], repr((out, log))))
            asking = json.loads(json.dumps(walked[0]))  # edited: unresolved, a person's question after the reviewer's post
            asking["isResolved"] = False
            asking["comments"]["nodes"] = asking["comments"]["nodes"][:1] + [dict(asking["comments"]["nodes"][0], body="Why not fix it here?",
                                                                                   author={"login": "someone"}, authorAssociation="NONE")]
            out, printed, log, reads = walk(True, thread=asking)
            cases.append(("cap: a thread a person wrote in is refused before anything is written",
                          type(out) is kit.Refused and "a person wrote" in str(out) and log == [], repr((out, log))))
            again = json.loads(json.dumps(walked[0]))  # as captured: the reply of the walk by hand, resolved; edited: unresolved
            again["isResolved"] = False
            on_ticket = dict(lows82[0], body=section82, html_url=posted_at)  # edited: the section posted on #39
            out, printed, log, reads = walk(True, thread=again, ticket_comments=[on_ticket])
            cases.append(("cap: a section that is on the ticket is not posted again, and a thread a reply moved gets no second one: the walk resolves it",
                          out is True and [w[0] for w in log] == ["resolve"] and "already" in printed, repr((out, log))))
            by_hand = dict(captured_ticket, body=lf(captured_ticket.get("body")) + "\n\n## Pull request #82 at `fb82fbc`\n\nx\n")  # edited: the section in the body
            out, printed, log, reads = walk(True, open_tickets=[by_hand])
            cases.append(("cap: a section in the ticket's body, as the walks by hand wrote it, is not posted again, and the reply links the ticket",
                          out is True and [w[0] for w in log] == ["reply", "resolve"] and log[0][2].endswith(captured_ticket.get("html_url") or "?"),
                          repr((out, log))))
            out, printed, log, reads = walk(True, thread=dict(walked[0], isResolved=True))
            cases.append(("cap: with no finding thread open nothing is written and no low is read",
                          out is True and log == [] and not any("/issues/82/" in r for r in reads), repr((out, log, reads))))
            out, printed, log, reads = walk(True, lows=[])
            cases.append(("lows: notes that name lows no comment on their issue shows are refused before anything is written",
                          type(out) is kit.Refused and "name 5 low findings" in str(out) and "show 0" in str(out) and log == [], repr((out, log))))
            other = [dict(notes82[0], body=notes82[0]["body"].replace(reviewed, "1" * 40))]  # edited: the lows of another head
            out, printed, log, reads = walk(False, notes=other)
            cases.append(("lows: no note on the head is a review without lows, said so, and the issue is not read",
                          out is True and "The same review reported no low findings." in printed and not any("/issues/19/" in r for r in reads),
                          repr((out, reads))))
            twice = [notes82[0], dict(notes82[0], id=2)]  # edited: a second part of five
            parts = [lows82[0], dict(lows82[0], html_url="https://github.com/SymoHTL/Kurz/issues/19#issuecomment-2")]
            out, printed, log, reads = walk(False, notes=twice, lows=parts)
            cases.append(("lows: two parts are counted together and both addresses are named",
                          out is True and "The 10 low findings of the same review: https://github.com/SymoHTL/Kurz/issues/19#issuecomment-6509258622 and "
                          "https://github.com/SymoHTL/Kurz/issues/19#issuecomment-2." in printed, printed[-300:]))
            outsider = {"user": {"login": "someone"}, "author_association": "NONE"}  # edited: the author
            stranger_note = walk(False, notes=[dict(notes82[0], **outsider)])
            stranger_part = walk(False, lows=[lows82[0], dict(lows82[0], **outsider)])  # a copy from an outsider
            cases.append(("lows: a note from someone who may not write here names no low, and a comment from one shows none",
                          "The same review reported no low findings." in stranger_note[1]
                          and "The 5 low findings of the same review: https://github.com/SymoHTL/Kurz/issues/19#issuecomment-6509258622." in stranger_part[1],
                          repr((stranger_note[0], stranger_part[0]))))
            out, printed, log, reads = walk(True, ticket_comments=[dict(on_ticket, **outsider)])
            cases.append(("cap: a section on the ticket from someone who may not write here is none: the section is posted",
                          out is True and [w[0] for w in log] == ["post", "reply", "resolve"], repr((out, log))))
            out, printed, log, reads = walk(False)
            cases.append(("lows: the issue is read from a minute before the first note, so a comment of the note's second is listed too",
                          "repos/o/n/issues/19/comments?per_page=100&since=2026-10-29T21:56:14Z" in reads, repr(reads)))
            nameless = [dict(notes82[0], body=notes82[0]["body"].replace("collected in #19", "collected"))]  # edited: no issue named
            out, printed, log, reads = walk(True, notes=nameless)
            cases.append(("lows: a note that names lows and no issue is refused before anything is written",
                          type(out) is kit.Refused and "no issue that collects them" in str(out) and log == [], repr((out, log))))
            get, pages, send, log, reads = walking()
            out, printed = quietly(lambda: cap_walk("o/n", 82, True, "What the code at that head shows.", get, pages, lambda *a: {}))
            cases.append(("cap: the section is printed before its post, and an answer to the post that names no comment is refused before any reply",
                          type(out) is kit.Refused and "names no comment" in str(out) and section82 in printed and log == [], repr((out, log))))
            out, printed, log, reads = walk(False, rounds=kit.ROUND_CAP + 1)
            cases.append(("cap: a pull request past the cap names its rounds",
                          out is True and f"{kit.ROUND_CAP + 1} review rounds, the last on 2026-10-29, the cap, at {reviewed}" in printed, printed[:400]))
            one = cap_section(82, reviewed, kit.ROUND_CAP, cap_found, "", (1, ["u"]))
            cases.append(("cap: one low finding is named in the singular", one.endswith("\nThe 1 low finding of the same review: u.\n"), one[-200:]))
            out, printed, log, reads = walk(True, flips=False)
            cases.append(("cap: a finding thread still open when the threads are read back after the resolution is a failed walk, named",
                          out is False and "still open: compiler/Kurz.Compiler.Tests/FileTests.cs" in printed, repr((out, printed[-300:]))))
            silent = json.loads(json.dumps(walked[0]))  # edited: unresolved, a post of the reviewer whose findings name no file
            silent["isResolved"], silent["comments"]["nodes"] = False, silent["comments"]["nodes"][:1]
            silent["comments"]["nodes"][0]["body"] = MARK.sub(lambda m: "<!-- kurz-review:findings [] -->", silent["comments"]["nodes"][0]["body"])
            out, printed, log, reads = walk(False, thread=silent)
            cases.append(("cap: a post of the reviewer that names no file is listed with its address",
                          out is True and f"- a post of the reviewer that names no file {opening.get('url')}\n" in printed, printed[:600]))
            unread = []
            for blank in ("", " \n"):
                get, pages, send, log, reads = walking()
                out, printed = quietly(lambda: cap_walk("o/n", 82, True, blank, get, pages, send))
                unread.append((out, log))
            cases.append(("cap: with --go a section with no reading, or a blank one, is refused before anything is written",
                          all(type(out) is kit.Refused and "no reading" in str(out) and log == [] for out, log in unread), repr(unread)))
            with tempfile.TemporaryDirectory() as scratch:
                reading = os.path.join(scratch, "reading.md")
                with open(reading, "w", encoding="utf-8") as f:
                    f.write("What the code at that head shows.\n")
                get, pages, send, log, reads = walking(rounds=kit.ROUND_CAP - 1)
                refused_code = quietly(lambda: cap_command("o/n", 82, True, reading, get, pages, send))
                get, pages, send, log, reads = walking(flips=False)
                open_code = quietly(lambda: cap_command("o/n", 82, True, reading, get, pages, send))
                get, pages, send, log, reads = walking()
                done_code = quietly(lambda: cap_command("o/n", 82, True, reading, get, pages, send))
                get, pages, send, log, reads = walking()

                def cut(method, path, payload):
                    send(method, path, payload)  # the post lands, and its answer never comes
                    raise TimeoutError("no answer")
                cut_code = quietly(lambda: cap_command("o/n", 82, True, reading, get, pages, cut))
            cases.append(("cap: the command ends 1 on a refusal and on a thread still open, 0 when every finding thread is resolved, and prints the known limit",
                          [refused_code[0], open_code[0], done_code[0]] == [1, 1, 0] and "cap stopped" in refused_code[1]
                          and all("re-run the gates after resolving" in printed for _, printed in (refused_code, done_code)),
                          repr((refused_code, open_code[0], done_code[0]))))
            cases.append(("cap: a write cut off before its answer is named as one that may have landed, and the walk as the way on",
                          cut_code[0] == 1 and [w[0] for w in log] == ["post"]
                          and "a write in flight when it stopped may have landed too; run the walk again" in cut_code[1], repr((cut_code, log))))
        finally:
            time.sleep = saved_sleep
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    gate = sys.argv[1] if len(sys.argv) > 1 else ""
    try:
        number = pr_number(sys.argv)
        if gate == "resolve":
            sys.exit(resolve_command(kit.repo(), number, "--go" in sys.argv, expect=option(sys.argv, "--head")))
        if gate == "cap":
            sys.exit(cap_command(kit.repo(), number, "--go" in sys.argv, option(sys.argv, "--analysis")))
        errors, scanned = run_gate(gate, kit.repo(), number)
    except (kit.Refused, KeyError, IndexError, ValueError, TypeError, AttributeError) as e:
        print(f"ERROR: pr gate {gate or '?'} could not run: {type(e).__name__}: {e}")
        sys.exit(1)
    for e in errors:
        print("ERROR:", e)
    print(f"pr gate {gate}: {scanned}, {len(errors)} errors")
    sys.exit(1 if errors else 0)
