#!/usr/bin/env python3
"""The one allowed way a pull request reaches main, and the check that the server-side merge rules
are on. The merge button stays open to anyone who may write: it gets the ruleset's checks and
skips what only this tool adds (HAZARD #14).

  merge_pr.py <pr> <full head sha> [--dry-run]
  merge_pr.py <pr> <full head sha> --over-red <pr>@<full head sha>=<check>[,<check>]
  merge_pr.py --assert-settings      the live ruleset equals tools/ruleset.json and is active
  merge_pr.py --apply-settings       create or update that ruleset (owner-approved, once)

A merge needs: the exact head that was looked at; an open pull request into the default branch that
is not a Draft, not behind its base and has no auto-merge armed; every review thread resolved, and
resolved by an edit (tools/pr_gates.py); every required check of the ruleset `success` on that head;
merge rules on the server that are what tools/ruleset.json says, every one of them read: a setting
this token cannot read refuses the merge; a title and a description the title gate accepts, because
this tool writes them into the commit on main.

--over-red is the gate-flip: the owner approved merging THIS head with THESE checks not green. The
ruleset is switched off for the one merge, restored on every exit path and read back. While it is
off nothing on the server holds any pull request or any push to main, and after exit 3 that lasts
until someone switches it back on; so the approval is per item and never standing. The tool cannot
verify the approval: it is the caller's statement (HAZARD #3), and the record says so. The waiver
lets through only what it names:
- it names a pull request and a head; another pull request is refused, another head voids it;
- a name waives a required check that failed, was skipped or never reported on this head; a check
  nobody named still blocks;
- a name that waives nothing (a passing check, an unknown name) stops the run, so a typo cannot
  print as a waiver;
- a check that is still running is never waived: its result is coming and nobody has read it;
- unresolved threads are never waived.
A check run counts only when the app the ruleset pins reported it, and a commit status only when
its creator is the login that app posts as (the Actions bot); that the ruleset reads a status that
way was seen on 2026-10-07, when the head of pull request 22 was mergeable on its `review` status
alone (#5). The statuses are read from the list endpoint, the one that names the creator: the
combined status drops it (knowledge/the-combined-status-drops-the-creator.md). Both required
checks are pinned to the app every workflow run of this repository reports as, so `review` proves
that a workflow run of this repository posted it, not which one (HAZARD #11).

A write whose answer cannot be read, that ran out of time, or that ended in an answer nobody
expected may have landed. It is never reported as refused: the state is read back, the ruleset is
restored and read back, and the exit code says what is known.

Exit codes: 0 merged (or dry run that would merge, or settings as expected); 1 refused; 2 usage;
3 GATE NOT RESTORED, switch the ruleset back on by hand: it outranks every other code, and the
lines above it say what became of the merge; 4 merge state unknown, look at the pull request
before doing anything else; 5 settings: nothing wrong in what this token could read, and at least
one setting NOT CHECKED; 6 merged over red, but the record of the waiver could not be posted: post
it on the pull request by hand."""
import contextlib
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402
import pr_gates  # noqa: E402

RULESET_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ruleset.json")
NOT_GREEN = {"failed", "skipped", "absent"}


def ruleset():
    with open(RULESET_FILE, encoding="utf-8") as f:
        return json.load(f)


def required(rules):
    """{context: the app pinned to report it, or None} of the required checks."""
    for rule in rules["rules"]:
        if rule["type"] == "required_status_checks":
            return {c["context"]: c.get("integration_id") for c in rule["parameters"]["required_status_checks"]}
    return {}


# A commit status names its creator, not an app. The one app a context is pinned to here is GitHub
# Actions, whose workflow runs post statuses as this login; a status from any other creator does
# not satisfy a pinned context, whatever its name says. Only the list of statuses
# (`commits/{sha}/statuses`) names the creator: the combined status (`commits/{sha}/status`) drops
# it, so a status read from there never matched the poster and the context read as absent, on the
# first head with a real `review` status (2026-10-07). The list holds every status ever posted for
# the head, newest first, so the newest of a context counts.
STATUS_POSTERS = {15368: "github-actions[bot]"}


def head_states(repo, head, contexts):
    """The state of every required context on one head, from its check runs and its list of
    statuses (context_states)."""
    runs = kit.gh_json(f"repos/{repo}/commits/{head}/check-runs?per_page=100")["check_runs"]
    statuses = kit.gh_json(f"repos/{repo}/commits/{head}/statuses?per_page=100")
    return context_states(contexts, runs, statuses)


def context_states(contexts, check_runs, statuses):
    """{context: success | failed | skipped | running | absent} on one head. A context that both a
    check run and a commit status report must be green in both. The newest check run of a name
    counts, and only one from the app pinned for that context; the newest status of the context
    counts, and only one from the login that app posts as (STATUS_POSTERS): with the ruleset off,
    this reading is all that holds a check nobody waived."""
    states = {}
    for ctx, app in contexts.items():
        seen = []
        runs = [r for r in check_runs if r["name"] == ctx and (app is None or (r.get("app") or {}).get("id") == app)]
        if runs:
            run = max(runs, key=lambda r: r["id"])
            seen.append("running" if run["status"] != "completed" else
                        {"success": "success", "skipped": "skipped"}.get(run["conclusion"], "failed"))
        posts = [s for s in statuses if s["context"] == ctx and (app is None or (s.get("creator") or {}).get("login") == STATUS_POSTERS.get(app))]
        if posts:
            post = max(posts, key=lambda s: s["id"])
            seen.append({"success": "success", "pending": "running"}.get(post["state"], "failed"))
        states[ctx] = next((s for s in ("running", "failed", "skipped") if s in seen), "success" if seen else "absent")
    return states


def parse_waiver(spec):
    m = re.fullmatch(r"(\d+)@([0-9a-f]{40})=([\w ./-]+(?:,[\w ./-]+)*)", spec or "")
    return (int(m.group(1)), m.group(2), [j.strip() for j in m.group(3).split(",")]) if m else None


def decide(number, sha, pr, default_branch, states, thread_errors, behind_by, waiver):
    """(verdict, reasons, waived): verdict is merge, over-red or refuse; waived is what the waiver
    really covered, as [(check, state)]."""
    reasons = []
    if not re.fullmatch(r"[0-9a-f]{40}", sha or ""):
        return "refuse", ["need the full 40-character head sha"], []
    if pr["head"]["sha"] != sha:
        reasons.append(f"the head is {pr['head']['sha'][:8]}, not the {sha[:8]} that was looked at")
    if pr["state"] != "open":
        reasons.append("the pull request is not open")
    if pr["draft"]:
        reasons.append("the pull request is a Draft")
    if pr["base"]["ref"] != default_branch:
        reasons.append(f"the base is {pr['base']['ref']}, not {default_branch}: retarget it first")
    if pr.get("auto_merge"):
        reasons.append("auto-merge is armed: it would fire on whatever the head is")
    if behind_by:
        reasons.append(f"the branch is {behind_by} commits behind {default_branch}: update it and let the checks run")
    reasons += thread_errors
    if not states:
        reasons.append("the ruleset names no required checks: nothing would be verified")
    reasons += [f"{ctx} is still running: nobody has read its result" for ctx, s in states.items() if s == "running"]

    waived, named = [], []
    if waiver:
        w_number, w_sha, named = waiver
        if w_number != number:
            return "refuse", [f"the waiver names pull request {w_number}, this run merges {number}"], []
        if w_sha != pr["head"]["sha"]:
            return "refuse", [f"the waiver was given for head {w_sha[:8]}; the head is now {pr['head']['sha'][:8]}, so it is void"], []
        for name in named:
            if name not in states:
                reasons.append(f"waiver: {name!r} waives nothing: it is not a required check ({', '.join(states)})")
            elif states[name] == "success":
                reasons.append(f"waiver: {name!r} waives nothing: it passed on this head")
            elif states[name] in NOT_GREEN:
                waived.append((name, states[name]))
    reasons += [f"{ctx} is {s} on this head" + ("" if waiver else ": fix it, or the owner approves this item (--over-red)")
                for ctx, s in states.items() if s in NOT_GREEN and ctx not in named]
    if reasons:
        return "refuse", reasons, []
    return ("over-red" if waived else "merge"), [], waived


def compare_rules(expected, live_rules):
    """Errors when the rules active on the default branch do not carry what tools/ruleset.json says.
    live_rules is the answer of `rules/branches/<branch>`, which lists active rulesets only. A
    parameter the server carries and the file does not name is an error too: the forge adds
    parameters with defaults of its own, and a default nobody looked at is an unchecked merge rule."""
    errors = []
    by_type = {r["type"]: r for r in live_rules}
    for rule in expected["rules"]:
        live = by_type.get(rule["type"])
        if not live:
            errors.append(f"rule {rule['type']} is not active on the default branch")
            continue
        wanted, carried = rule.get("parameters") or {}, live.get("parameters") or {}
        for key, want in wanted.items():
            got = carried.get(key)
            if key == "required_status_checks":
                want, got = sorted(map(json.dumps, want)), sorted(json.dumps({k: c.get(k) for k in ("context", "integration_id")}) for c in got or [])
            if got != want:
                errors.append(f"rule {rule['type']}: {key} is {got}, expected {want}")
        errors += [f"rule {rule['type']}: the server carries {key} = {json.dumps(carried[key])}, which tools/ruleset.json does not name: "
                   f"decide it and pin it there" for key in sorted(set(carried) - set(wanted))]
    errors += [f"the server carries a rule {t}, which tools/ruleset.json does not name: decide it and name it there"
               for t in sorted(set(by_type) - {r["type"] for r in expected["rules"]})]
    return errors


def settings_errors(expected, info, live_rules, rulesets, detail):
    """(errors, notes) from what the forge answered: the repository, the rules active on its default
    branch, the list of rulesets, and detail(id) -> one ruleset in full. A field this token cannot
    read becomes a NOT CHECKED note, never a pass. The rules are compared for the one ruleset the
    file names: the same ruleset whose enforcement and bypass list are read. A rule that another
    ruleset puts on the default branch is an error, because nobody defined it here."""
    errors, notes = [], []
    if "allow_auto_merge" not in info:
        notes.append("auto-merge setting NOT CHECKED: this token cannot read it (an admin run checks it)")
    elif info["allow_auto_merge"]:
        errors.append("auto-merge is allowed in the repository settings")
    mine = [r for r in rulesets if r["name"] == expected["name"]]
    if len(mine) != 1:
        return errors + [f"{len(mine)} rulesets are named {expected['name']!r}, expected 1"], notes
    errors += compare_rules(expected, [r for r in live_rules if r.get("ruleset_id") == mine[0]["id"]])
    others = sorted({str(r.get("ruleset_id")) for r in live_rules if r.get("ruleset_id") != mine[0]["id"]})
    if others:
        errors.append(f"other rulesets put rules on the default branch too (ruleset {', '.join(others)}): "
                      f"tools/ruleset.json is the one definition of the merge rules")
    one = detail(mine[0]["id"])
    if one.get("enforcement") != "active":
        errors.append(f"the ruleset is {one.get('enforcement')}, not active")
    if "bypass_actors" not in one:
        notes.append("bypass list NOT CHECKED: this token cannot read it (an admin run checks it)")
    elif one["bypass_actors"]:
        errors.append("the ruleset has bypass actors; nobody may bypass the merge checks")
    return errors, notes


def settings_exit(errors, notes):
    """An error is red. A setting that could not be read is its own exit code, never the 0 of a pass."""
    return 1 if errors else kit.PARTLY if notes else 0


def find_ruleset(repo):
    name = ruleset()["name"]
    found = [r for r in kit.gh_json(f"repos/{repo}/rulesets") if r["name"] == name]
    if len(found) != 1:
        raise kit.Refused(f"{len(found)} rulesets are named {name!r}, expected 1")
    return found[0]["id"]


# What a call to the forge can end in besides its answer: a refusal, no answer, or an answer that
# is not what the code expects. After a write each of them goes through the read-backs.
ANSWER_ERRORS = (kit.Refused, LookupError, ValueError, TypeError, AttributeError)


def send(path, payload, method="PUT"):
    """One write to the forge, and its answer as an object. An answer that cannot be read, and a
    5xx, is kit.Unanswered, not a refusal: the write may have landed (kit.gh_send)."""
    answer = kit.gh_send(method, path, payload)
    if not isinstance(answer, dict):
        raise kit.Unanswered(f"gh api -X {method} {path}: the answer is not an object")
    return answer


def merge(repo, number, sha, pr, waived):
    """Merge exactly `sha`. With a waiver the ruleset is off for the one call and restored on every path."""
    payload = {"sha": sha, "merge_method": "squash", "commit_title": f"{pr['title']} (#{number})", "commit_message": pr["body"] or ""}
    gate = find_ruleset(repo) if waived else None
    record = ", ".join(f"`{name}` ({state})" for name, state in waived)
    landed, code, step, unknown = False, 0, "the merge call", False
    try:
        if gate:
            step = "switching the ruleset off"
            send(f"repos/{repo}/rulesets/{gate}", {"enforcement": "disabled"})
            step = "the merge call"
        answer = send(f"repos/{repo}/pulls/{number}/merge", payload)
        if answer.get("merged") is not True:  # the status alone is not the merge
            raise kit.Refused(f"the forge answered without merged=true: {json.dumps(answer)[:200]}")
        landed = True
        print(f"merged {repo}#{number} at {sha}")
    except ANSWER_ERRORS as e:
        print(f"{step} failed: {type(e).__name__}: {e}")
        # Only an answer of the forge that says no is a refusal. A call that died on the way
        # (a timeout, a 5xx, an answer nobody could read) may still land after the read-back.
        # A switch-off that failed sent no merge call; what became of the ruleset is read back below.
        refused = step != "the merge call" or re.search(r"\(HTTP 4\d\d\)", str(e)) is not None
        try:  # a write that may have landed is never reported as a plain failure
            pull = kit.gh_json(f"repos/{repo}/pulls/{number}")
            merged, at = pull.get("merged") is True, (pull.get("head") or {}).get("sha")
            # the call named this head, so a merge at another head is somebody else's, not this one
            landed = merged and at == sha
            unknown = not merged and not refused
            code = 0 if landed else 4 if unknown else 1
            print("read back: the pull request IS merged" if landed
                  else f"read back: the pull request is merged at another head ({at}): not this merge, nothing is waived" if merged
                  else "read back: not merged")
        except ANSWER_ERRORS as again:
            print(f"the read-back failed too: {again}")
            unknown, code = True, 4
        if unknown:
            print("MERGE STATE UNKNOWN: the forge did not refuse the merge and it may still land. "
                  "Look at the pull request before doing anything else")
    finally:
        if gate:
            for attempt in (1, 2, 3):
                try:
                    send(f"repos/{repo}/rulesets/{gate}", {"enforcement": "active"})
                    break
                except ANSWER_ERRORS as e:  # the forge's answer decides what has to be done by hand
                    print(f"restoring the gate failed (attempt {attempt} of 3): {e}")
                    time.sleep(2)
            try:
                back = kit.gh_json(f"repos/{repo}/rulesets/{gate}").get("enforcement")
            except ANSWER_ERRORS as e:
                back = f"unreadable ({e})"
            print(f"gate read back: {back}")
            if back != "active":
                print("GATE NOT RESTORED: set the ruleset's enforcement back to active by hand NOW")
                code = 3
    if unknown and waived:
        print(f"If the merge landed, its waiver record is owed. Post on the pull request: waived on {sha}: {record}")
    if landed and waived:  # the record follows the merge, whatever became of the gate afterwards
        body = (f"Merged over red at `{sha}`. The caller of the merge tool stated the owner's approval for this pull request and "
                f"this head; the tool cannot verify that (HAZARD #3). Waived on this head: {record}.")
        try:
            send(f"repos/{repo}/issues/{number}/comments", {"body": body}, method="POST")
        except ANSWER_ERRORS as e:  # the comment is the only trace of what was waived on which head
            print(f"posting the waiver record failed: {e}")
            try:  # a write that may have landed is read back before it is called missing: posted twice is a record nobody trusts
                posted = any((c.get("body") or "") == body for c in kit.gh_pages(f"repos/{repo}/issues/{number}/comments?per_page=100"))
            except ANSWER_ERRORS as again:
                print(f"the record's read-back failed too: {again}")
                print(f"THE WAIVER RECORD MAY BE MISSING: look at the pull request, and only if it is not there post by hand: "
                      f"waived on {sha}: {record}")
                code = code or 6
            else:
                if posted:
                    print("read back: the waiver record IS on the pull request")
                else:
                    print(f"THE WAIVER RECORD IS MISSING (read back: not on the pull request), post it by hand: waived on {sha}: {record}")
                    code = code or 6
    return code


def thread_reasons(threads, changed_since):
    """What the threads of a pull request hold against a merge: any unresolved thread, and any
    review finding that was resolved without its answer (tools/pr_gates.py)."""
    unresolved = sum(not t["isResolved"] for t in threads)
    errors = [f"{unresolved} unresolved threads"] if unresolved else []
    return errors + [e for e in pr_gates.findings_errors(threads, changed_since) if not e.startswith("unresolved")]


def blockers(threads, changed_since, settings, squash, unread=()):
    """Everything besides the checks that holds a merge: the threads; merge rules that are not as
    tools/ruleset.json defines them, or that this token could not read (`unread`: a setting nobody
    read is not a pass); and what the title gate holds against the title and the description, which
    this tool writes into the commit on main. The required checks are read from the file, so a
    server that differs from it means this run would verify another list than the server enforces;
    with the ruleset off for an over-red merge nothing would correct that."""
    return (thread_reasons(threads, changed_since) + [f"merge checks are not as defined: {e}" for e in settings]
            + [f"merge checks were not read in full, run the merge with a login that can read them: {n}" for n in unread]
            + [f"the squash commit would carry it: {e}" for e in squash])


def arguments(argv):
    """(pull request, sha, waiver or None) of a merge call, or None when the call is not understood.
    An --over-red whose value is missing or is not a waiver is not understood: it never falls back
    to a plain merge. An option this tool does not know is not understood either: a mistyped
    --dry-run must not merge."""
    spec = argv[argv.index("--over-red") + 1] if "--over-red" in argv[:-1] else None
    waiver = parse_waiver(spec) if spec else None
    unknown = [a for a in argv if a.startswith("--") and a not in ("--dry-run", "--over-red")]
    positional = [a for a in argv if not a.startswith("--") and a != spec]
    if unknown or ("--over-red" in argv and not waiver) or len(positional) != 2 or not positional[0].isdigit():
        return None
    return int(positional[0]), positional[1], waiver


USAGE = ("usage: merge_pr.py <pr> <full head sha> [--dry-run] [--over-red <pr>@<full head sha>=<check>[,<check>]]\n"
         "       merge_pr.py --assert-settings | --apply-settings   (alone: a settings call takes no other argument)")


def run(argv):
    if "--assert-settings" in argv or "--apply-settings" in argv:
        if len(argv) != 1:  # `--apply-settings --dry-run` would write
            print(USAGE)
            return 2
        repo = kit.repo()
        if "--apply-settings" in argv:
            existing = [r for r in kit.gh_json(f"repos/{repo}/rulesets") if r["name"] == ruleset()["name"]]
            if existing:
                send(f"repos/{repo}/rulesets/{existing[0]['id']}", ruleset())
            else:
                send(f"repos/{repo}/rulesets", ruleset(), method="POST")
            print("ruleset written")
        info = kit.gh_json(f"repos/{repo}")
        errors, notes = settings_errors(ruleset(), info, kit.gh_json(f"repos/{repo}/rules/branches/{info['default_branch']}"),
                                        kit.gh_json(f"repos/{repo}/rulesets"), lambda rid: kit.gh_json(f"repos/{repo}/rulesets/{rid}"))
        for n in notes:
            print("note:", n)
        for e in errors:
            print("ERROR:", e)
        print(f"merge checks on {repo}: {len(errors)} errors, {len(notes)} NOT CHECKED")
        return settings_exit(errors, notes)

    understood = arguments(argv)
    if not understood:
        print(USAGE)
        return 2
    number, sha, waiver = understood
    repo = kit.repo()
    pr, info = kit.gh_json(f"repos/{repo}/pulls/{number}"), kit.gh_json(f"repos/{repo}")
    head, default_branch = pr["head"]["sha"], info["default_branch"]
    settings, notes = settings_errors(ruleset(), info, kit.gh_json(f"repos/{repo}/rules/branches/{default_branch}"),
                                      kit.gh_json(f"repos/{repo}/rulesets"), lambda rid: kit.gh_json(f"repos/{repo}/rulesets/{rid}"))
    states = head_states(repo, head, required(ruleset()))
    threads = pr_gates.fetch_threads(repo, number)
    changed = lambda path, at: pr_gates.blob_at(repo, path, at) != pr_gates.blob_at(repo, path, head)
    behind = kit.gh_json(f"repos/{repo}/compare/{pr['base']['ref']}...{head}")["behind_by"]

    held = blockers(threads, changed, settings, pr_gates.title_errors(pr["title"], [], pr["body"]), notes)
    verdict, reasons, waived = decide(number, sha, pr, default_branch, states, held, behind, waiver)
    print(f"{repo}#{number} at {head[:8]}: " + ", ".join(f"{c}={s}" for c, s in states.items()) + f"; {len(threads)} threads; behind by {behind}")
    for n in notes:
        print("note:", n)
    for r in reasons:
        print("REFUSED:", r)
    if verdict == "refuse":
        return 1
    for name, state in waived:
        print(f"WAIVED with the owner's approval for this item: {name} ({state}) on {head}")
    if "--dry-run" in argv:
        print(f"dry run: would merge ({verdict})")
        return 0
    return merge(repo, number, sha, pr, waived)


def self_test():
    fixtures = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fixtures")

    def load(name):
        with open(os.path.join(fixtures, name), encoding="utf-8") as f:
            return json.load(f)

    pr, runs, live = load("pull-request.json"), load("check-runs.json")["check_runs"], load("branch-rules.json")
    combined, statuses = load("status.json"), load("statuses.json")  # one real status, from the two endpoints
    info, listed, full = load("repository.json"), load("rulesets.json"), load("ruleset-detail.json")
    sha, number, cases = pr["head"]["sha"], pr["number"], []

    def case(name, passed, detail=""):
        cases.append((name, bool(passed), detail))

    def attempt(fn, *args):
        """The result, or the exception as a value: a broken tool then fails its case instead of ending the suite."""
        try:
            return fn(*args)
        except Exception as e:
            return e

    # states, from what the forge really answered for one head
    run0 = next(r for r in runs if r["name"] == "gates")
    pin = run0["app"]["id"]  # the app that really reported the check: the id the ruleset pins
    G, R = {"gates": pin}, {"review": pin}
    real_states = context_states({**G, **R}, runs, statuses)
    case("states: the real check run of the gates job is read", real_states["gates"] in ("success", "failed"), real_states)
    case("states: the real review status is read from the list of statuses, which names its creator",
         len(statuses) >= 1 and real_states["review"] == "success", (len(statuses), real_states))
    trap = combined["statuses"]
    case("states: the combined status drops the creator, so the same status read from it does not count for the pinned context",
         len(trap) >= 1 and all("creator" not in s for s in trap) and context_states(R, [], trap)["review"] == "absent", trap)
    asked = []

    def forge_of_head(path):
        asked.append(path)
        return (load("check-runs.json") if "/check-runs?" in path else load("statuses.json") if "/statuses?" in path
                else load("status.json") if "/status?" in path else {})

    saved, kit.gh_json = kit.gh_json, forge_of_head
    try:
        read = attempt(head_states, "o/r", sha, {**G, **R})
    finally:
        kit.gh_json = saved
    case("read: the tool asks the forge for the list of statuses, and reads the review from it",
         isinstance(read, dict) and read["review"] == "success" and any("/statuses?" in p for p in asked), (read, asked))
    two = [{"context": "review", "state": "error", "creator": {"login": "github-actions[bot]"}, "id": 1},
           {"context": "review", "state": "success", "creator": {"login": "github-actions[bot]"}, "id": 2}]
    case("states: the newest status of a context counts, whatever the order of the list",
         (context_states(R, [], two)["review"], context_states(R, [], two[::-1])["review"]) == ("success", "success"))
    case("states: an older success does not outvote a newer error", context_states(R, [], [dict(two[0], id=3), two[1]])["review"] == "failed")
    newer = dict(run0, id=run0["id"] + 1)
    for name, change, want in [("a completed success", {"status": "completed", "conclusion": "success"}, "success"),
                               ("a failure", {"status": "completed", "conclusion": "failure"}, "failed"),
                               ("a cancelled run", {"status": "completed", "conclusion": "cancelled"}, "failed"),
                               ("a skipped job is not a success", {"status": "completed", "conclusion": "skipped"}, "skipped"),
                               ("a queued run", {"status": "queued", "conclusion": None}, "running"),
                               ("a run in progress", {"status": "in_progress", "conclusion": None}, "running")]:
        got = context_states(G, [dict(run0, **change)], [])["gates"]
        case(f"states: {name}", got == want, got)
    got = context_states(G, [dict(run0, status="completed", conclusion="success"), dict(newer, status="completed", conclusion="failure")], [])
    case("states: the newest run of a name counts", got["gates"] == "failed", got)
    rerun = context_states(G, [dict(newer, status="completed", conclusion="success"), dict(run0, status="completed", conclusion="failure")], [])
    case("states: a green re-run after a failure is green, whatever the order of the list", rerun["gates"] == "success", rerun)
    case("states: a check nobody reported is absent", context_states(R, runs, [])["review"] == "absent")
    one = lambda state, login="github-actions[bot]": [{"context": "review", "state": state, "creator": {"login": login}, "id": 1}]
    case("states: a success status", context_states(R, [], one("success"))["review"] == "success")
    case("states: a status of the pinned context from another creator than the Actions bot does not count",
         (context_states(R, [], one("success", "someone"))["review"], context_states(R, [], [{"context": "review", "state": "success", "id": 1}])["review"])
         == ("absent", "absent"))
    case("states: a context nobody pinned takes a status from any creator", context_states({"review": None}, [], one("success", "someone"))["review"] == "success")
    case("states: a pending status is running", context_states(R, [], one("pending"))["review"] == "running")
    case("states: an error status is a failure", context_states(R, [], one("error"))["review"] == "failed")
    both = context_states(G, [dict(run0, status="completed", conclusion="success")],
                          [{"context": "gates", "state": "failure", "creator": {"login": "github-actions[bot]"}, "id": 1}])
    case("states: a check and a status of one name must both pass", both["gates"] == "failed", both)
    passed = dict(run0, status="completed", conclusion="success")
    stranger = dict(passed, app={**run0["app"], "id": pin + 1})
    case("states: a check run from another app than the pinned one does not count",
         (context_states(G, [stranger], [])["gates"], context_states(G, [passed], [])["gates"]) == ("absent", "success"))
    case("states: a context nobody pinned takes a check run from any app", context_states({"gates": None}, [stranger], [])["gates"] == "success")

    # the decision
    ready = json.loads(json.dumps(pr))
    ready.update(state="open", draft=False, auto_merge=None)
    ready["base"]["ref"] = "main"
    green = {"gates": "success", "review": "success"}

    def verdict(states=green, threads=(), behind=0, waiver=None, given=sha, **change):
        return decide(number, given, {**ready, **change}, "main", states, list(threads), behind, parse_waiver(waiver) if waiver else None)

    case("decide: all green merges", verdict()[0] == "merge", verdict())
    case("decide: a short sha is refused, and told why", verdict(given=sha[:12])[1] == ["need the full 40-character head sha"], verdict(given=sha[:12]))
    case("decide: a moved head is refused", verdict(given="0" * 40)[0] == "refuse")
    case("decide: a Draft is refused", verdict(draft=True)[0] == "refuse")
    case("decide: a closed pull request is refused", verdict(state="closed")[0] == "refuse")
    case("decide: armed auto-merge is refused", verdict(auto_merge={"merge_method": "squash"})[0] == "refuse")
    case("decide: a branch behind its base is refused", verdict(behind=2)[0] == "refuse")
    case("decide: another base branch is refused", decide(number, sha, ready, "trunk", green, [], 0, None)[0] == "refuse")
    case("decide: an unresolved thread is refused", verdict(threads=["1 unresolved threads"])[0] == "refuse")
    case("decide: no required checks is refused", verdict(states={})[0] == "refuse")
    for state in ("failed", "skipped", "absent", "running"):
        case(f"decide: a {state} check is refused", verdict(states={**green, "review": state})[0] == "refuse")
    red = {"gates": "success", "review": "absent"}
    w = f"{number}@{sha}=review"
    v = verdict(states=red, waiver=w)
    case("waiver: a named absent check merges over red", v[0] == "over-red" and v[2] == [("review", "absent")], v)
    case("waiver: a failed and a skipped check, both named", verdict(states={"gates": "failed", "review": "skipped"},
                                                                    waiver=f"{number}@{sha}=gates,review")[0] == "over-red")
    case("waiver: a check nobody named still blocks", verdict(states={"gates": "failed", "review": "skipped"}, waiver=w)[0] == "refuse")
    case("waiver: a running check is never waived", verdict(states={**green, "review": "running"}, waiver=w)[0] == "refuse")
    case("waiver: an unresolved thread is never waived", verdict(states=red, threads=["1 unresolved threads"], waiver=w)[0] == "refuse")
    case("waiver: another pull request is refused", verdict(states=red, waiver=f"{number + 1}@{sha}=review")[0] == "refuse")
    moved = verdict(states=red, waiver=f"{number}@{'0' * 40}=review")
    case("waiver: another head voids it", moved[0] == "refuse" and "void" in moved[1][0], moved)
    typo = verdict(states=red, waiver=f"{number}@{sha}=reveiw")
    case("waiver: a mistyped name waives nothing and stops", typo[0] == "refuse" and any("waives nothing" in r for r in typo[1]), typo)
    passing = verdict(states=red, waiver=f"{number}@{sha}=review,gates")
    case("waiver: naming a passing check stops the run", passing[0] == "refuse" and passing[2] == [], passing)
    case("waiver: the record holds only what was waived", verdict(states=red, waiver=w)[2] == [("review", "absent")])
    case("waiver: a spec without a full sha is not a waiver", parse_waiver(f"{number}@{sha[:10]}=review") is None)
    case("waiver: a spec without checks is not a waiver", parse_waiver(f"{number}@{sha}=") is None)

    # the server-side settings, against the real answer of the rules endpoint
    expected = ruleset()
    case("settings: the live rules match the definition", not compare_rules(expected, live), compare_rules(expected, live))
    case("settings: required checks are named, each with the app pinned to report it",
         list(required(expected)) == ["gates", "review"] and set(required(expected).values()) == {pin}, required(expected))
    without = [r for r in live if r["type"] != "required_status_checks"]
    case("settings: a missing rule is an error", any("required_status_checks is not active" in e for e in compare_rules(expected, without)))
    loose = json.loads(json.dumps(live))
    next(r for r in loose if r["type"] == "pull_request")["parameters"]["required_review_thread_resolution"] = False
    case("settings: thread resolution switched off is an error", any("required_review_thread_resolution" in e for e in compare_rules(expected, loose)))
    fewer = json.loads(json.dumps(live))
    next(r for r in fewer if r["type"] == "required_status_checks")["parameters"]["required_status_checks"].pop()
    case("settings: a required check removed is an error", any("required_status_checks is" in e for e in compare_rules(expected, fewer)))
    anyone = json.loads(json.dumps(live))
    for c in next(r for r in anyone if r["type"] == "required_status_checks")["parameters"]["required_status_checks"]:
        c["integration_id"] = None
    case("settings: a check anyone may report is an error", any("required_status_checks is" in e for e in compare_rules(expected, anyone)))
    case("settings: no active rules at all is an error", len(compare_rules(expected, [])) == len(expected["rules"]))
    grown = json.loads(json.dumps(live))
    next(r for r in grown if r["type"] == "pull_request")["parameters"]["a_parameter_the_forge_added"] = True
    case("settings: a parameter the forge added and nobody pinned is an error",
         any("a_parameter_the_forge_added" in e and "does not name" in e for e in compare_rules(expected, grown)), compare_rules(expected, grown))
    more = live + [{"type": "required_signatures", "ruleset_id": live[0]["ruleset_id"]}]
    case("settings: a rule the server carries and the file does not name is an error",
         any("a rule required_signatures" in e for e in compare_rules(expected, more)) and not compare_rules(expected, live), compare_rules(expected, more))

    def settings(repo=info, rulesets=listed, **change):
        return settings_errors(expected, repo, live, rulesets, lambda rid: {**full, **change})

    case("settings: the real repository and ruleset are clean, with nothing unchecked", settings() == ([], []), settings())
    case("settings: auto-merge allowed is an error", any("auto-merge is allowed" in e for e in settings(repo={**info, "allow_auto_merge": True})[0]))
    hidden = {k: v for k, v in info.items() if k != "allow_auto_merge"}
    got = attempt(lambda: settings(repo=hidden))
    case("settings: an auto-merge setting the token cannot read is NOT CHECKED",
         isinstance(got, tuple) and got[0] == [] and len(got[1]) == 1 and "auto-merge setting NOT CHECKED" in got[1][0], got)
    case("settings: a disabled ruleset is an error", any("is disabled, not active" in e for e in settings(enforcement="disabled")[0]))
    case("settings: a ruleset in evaluate mode is an error", any("is evaluate, not active" in e for e in settings(enforcement="evaluate")[0]))
    actor = [{"actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always"}]
    case("settings: a bypass actor is an error", any("bypass actors" in e for e in settings(bypass_actors=actor)[0]))
    blind = settings_errors(expected, info, live, listed, lambda rid: {k: v for k, v in full.items() if k != "bypass_actors"})
    case("settings: a bypass list the token cannot read is NOT CHECKED", blind[0] == [] and any("bypass list NOT CHECKED" in n for n in blind[1]), blind)
    got = attempt(lambda: settings(rulesets=[]))
    case("settings: no ruleset of that name is an error", isinstance(got, tuple) and any("0 rulesets are named" in e for e in got[0]), got)
    case("settings: two rulesets of that name is an error", any("2 rulesets are named" in e for e in settings(rulesets=listed + listed)[0]))
    elsewhere = [dict(r, ruleset_id=r["ruleset_id"] + 1) if r["type"] == "required_status_checks" else r for r in live]
    got = settings_errors(expected, info, elsewhere, listed, lambda rid: full)[0]
    case("settings: a rule that only another ruleset supplies is missing from this one",
         any("required_status_checks is not active" in e for e in got), got)
    case("settings: a rule another ruleset puts on the branch is an error", any("other rulesets put rules" in e for e in got), got)
    nothing = lambda p, s: False
    case("blockers: merge rules that differ from the definition hold the merge",
         blockers([], nothing, ["rule deletion is not active on the default branch"], []) ==
         ["merge checks are not as defined: rule deletion is not active on the default branch"] and blockers([], nothing, [], []) == [])
    case("blockers: a setting that could not be read holds the merge",
         blockers([], nothing, [], [], ["bypass list NOT CHECKED"]) ==
         ["merge checks were not read in full, run the merge with a login that can read them: bypass list NOT CHECKED"])
    case("blockers: what the title gate holds against title and description holds the merge",
         blockers([], nothing, [], ["the description: machine-bound string (drive-path)"]) ==
         ["the squash commit would carry it: the description: machine-bound string (drive-path)"])
    case("settings: nothing wrong and nothing unread is exit 0", settings_exit([], []) == 0)
    case("settings: a setting NOT CHECKED is its own exit code, not the 0 of a pass", settings_exit([], ["a note"]) == kit.PARTLY != 0)
    case("settings: an error is red, whatever else could not be read", settings_exit(["an error"], ["a note"]) == 1)
    asked = []

    def no_forge():
        asked.append("repo")
        raise kit.Refused("the forge was asked")

    saved, kit.repo = kit.repo, no_forge
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            got = attempt(run, ["--apply-settings", "--dry-run"]), attempt(run, ["--assert-settings", "8"])
    finally:
        kit.repo = saved
    case("call: a settings call with another argument is usage, and asks the forge nothing", got == (2, 2) and not asked, (got, asked))

    # the call
    spec = f"{number}@{sha}=review"
    case("call: a pull request and a head", attempt(arguments, [str(number), sha]) == (number, sha, None))
    case("call: a dry run is the same call", attempt(arguments, [str(number), sha, "--dry-run"]) == (number, sha, None))
    case("call: a waiver is parsed", attempt(arguments, [str(number), sha, "--over-red", spec]) == (number, sha, (number, sha, ["review"])))
    case("call: a bare check name is not a waiver, and not a plain merge either", attempt(arguments, [str(number), sha, "--over-red", "review"]) is None)
    case("call: --over-red without a value is not understood", attempt(arguments, [str(number), sha, "--over-red"]) is None)
    case("call: a head alone is not understood", attempt(arguments, [sha]) is None)
    case("call: a pull request alone is not understood", attempt(arguments, [str(number)]) is None)
    case("call: a pull request that is no number is not understood", attempt(arguments, ["x", sha]) is None)
    case("call: a mistyped option is not understood, so it cannot merge", attempt(arguments, [str(number), sha, "--dryrun"]) is None)
    case("call: --over-red glued to its value is not understood", attempt(arguments, [str(number), sha, f"--over-red={spec}"]) is None)

    # the threads, from what the forge really answered for a reviewed pull request
    threads = pr_gates.fixture_threads()
    found = pr_gates.finding_threads(threads)
    case("threads: the real answer holds a finding thread", len(found) >= 1, f"{len(found)} of {len(threads)}")
    if found:
        def thread(resolved, body=None):
            t = json.loads(json.dumps(found[0][0]))
            t["isResolved"] = resolved
            t["comments"]["nodes"] = t["comments"]["nodes"][:1]
            if body is not None:
                t["comments"]["nodes"][0]["body"] = body
            return t

        same, differs = (lambda p, s: False), (lambda p, s: True)
        case("threads: none is nothing against the merge", thread_reasons([], same) == [])
        case("threads: a resolved and answered finding is nothing against it", thread_reasons([thread(True)], differs) == [])
        got = thread_reasons([thread(False), thread(False)], differs)
        case("threads: unresolved threads are counted, once", got == ["2 unresolved threads"], got)
        got = thread_reasons([thread(False, body="a question from a person")], differs)
        case("threads: an unresolved thread without a finding blocks too", got == ["1 unresolved threads"], got)
        got = thread_reasons([thread(True)], same)
        case("threads: a finding resolved without its answer blocks", len(got) == 1 and "without changing the file" in got[0], got)

    # one write, against a gh that answers what it is told to
    def sent(answer):
        def run(cmd, **how):
            if isinstance(answer, Exception):
                raise answer
            return answer
        saved, kit.run = kit.run, run
        try:
            return attempt(send, "repos/o/r/pulls/1/merge", {})
        finally:
            kit.run = saved

    case("send: an answer is read as the object it is, and no answer as an empty one", (sent('{"merged": true}'), sent("")) == ({"merged": True}, {}))
    got = sent(kit.Refused("`gh api -X PUT repos/o/r/pulls/1/merge` exited 1: gh: Service Unavailable (HTTP 503)"))
    case("send: a 5xx is unanswered, not refused: the merge may have landed, and is read back", type(got) is kit.Unanswered, repr(got))
    got = sent("<html>502</html>")
    case("send: an answer that is not JSON is unanswered, because the write landed", type(got) is kit.Unanswered, repr(got))
    got = sent("[]")
    case("send: an answer that is not an object is unanswered too", type(got) is kit.Unanswered, repr(got))

    # the gate-flip, against a forge that records every write and fails where it is told to
    class Flip:
        def __init__(self, fail=(), merged=False, restore_fails=0, how="gh: not mergeable (HTTP 405)", silent=False, lands=(), error=None,
                     merged_head=None):
            self.fail, self.merged, self.restore_fails, self.how, self.silent = set(fail), merged, restore_fails, how, silent
            self.lands, self.error = set(lands), error  # writes that are applied although they fail; what a failure raises
            self.merged_head = merged_head or sha  # the head the pull request is merged at, when it is
            self.calls, self.reads, self.enforcement, self.payload, self.records = [], [], "active", None, []

        def send(self, path, payload, method="PUT"):
            what = ({"disabled": "off", "active": "on"}[payload["enforcement"]] if "/rulesets/" in path
                    else "merge" if path.endswith("/merge") else "record")
            self.calls.append(what)
            if what == "on" and self.restore_fails > 0:
                self.restore_fails -= 1
                if "on" in self.lands:
                    self.enforcement = "active"
                raise kit.Refused("refused")
            if what in self.fail:
                if what in self.lands and what in ("off", "on"):
                    self.enforcement = payload["enforcement"]
                if what in self.lands and what == "record":
                    self.records.append(payload["body"])
                raise self.error or kit.Refused(self.how)
            if what == "record":
                self.records.append(payload["body"])
            if what in ("off", "on"):
                self.enforcement = payload["enforcement"]
            if what == "merge":
                self.merged, self.payload = not self.silent, payload
                return {} if self.silent else {"merged": True, "sha": "f" * 40}
            return {}

        def gh_json(self, path):
            kind = "gate-readback" if "/rulesets/" in path else "pr-readback"
            self.reads.append(kind)
            if kind in self.fail:
                raise self.error or kit.Refused("refused")
            return {"enforcement": self.enforcement} if kind == "gate-readback" else {"merged": self.merged, "head": {"sha": self.merged_head}}

        def gh_pages(self, path):
            self.reads.append("record-readback")
            if "record-readback" in self.fail:
                raise self.error or kit.Refused("refused")
            return [{"body": body} for body in self.records]

    def flip(waived, **how):
        fake, g = Flip(**how), globals()
        saved = g["send"], g["find_ruleset"], kit.gh_json, kit.gh_pages, time.sleep
        g["send"], g["find_ruleset"], kit.gh_json, kit.gh_pages, time.sleep = (fake.send, (lambda repo: 7), fake.gh_json, fake.gh_pages,
                                                                              (lambda seconds: None))
        try:
            with contextlib.redirect_stdout(io.StringIO()) as printed:
                code = attempt(merge, "o/r", number, sha, ready, waived)  # an exception that escapes is the case's result
        finally:
            g["send"], g["find_ruleset"], kit.gh_json, kit.gh_pages, time.sleep = saved
        fake.out = printed.getvalue()
        return code, fake

    over = [("review", "absent")]
    code, f = flip([])
    case("flip: without a waiver the ruleset is never touched", (code, f.calls) == (0, ["merge"]), (code, f.calls))
    case("flip: the merge names the exact head and squashes", bool(f.payload) and f.payload.get("sha") == sha and f.payload.get("merge_method") == "squash", f.payload)
    case("flip: the squash commit carries the pull request's title and description, which the title gate read",
         bool(f.payload) and f.payload.get("commit_title") == f"{ready['title']} (#{number})" and f.payload.get("commit_message") == (ready["body"] or ""),
         f.payload)
    code, f = flip(over)
    case("flip: with a waiver the ruleset is off for the one merge, restored, and the waiver is recorded",
         (code, f.calls, f.enforcement) == (0, ["off", "merge", "on", "record"], "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"merge"})
    case("flip: a refused merge still restores the gate, and records nothing",
         (code, f.calls, f.enforcement) == (1, ["off", "merge", "on"], "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"merge"}, merged=True)
    case("flip: a merge call that failed but landed is a merge, and is recorded", (code, f.calls) == (0, ["off", "merge", "on", "record"]), (code, f.calls))
    code, f = flip(over, fail={"merge"}, merged=True, merged_head="e" * 40)
    case("flip: a pull request merged at another head is somebody else's merge: refused, and nothing is recorded as waived",
         (code, "record" in f.calls) == (1, False) and "another head" in f.out, (code, f.calls, f.out))
    code, f = flip(over, fail={"merge"}, merged=True, merged_head="e" * 40, how="gh: timeout awaiting response")
    case("flip: a merge call that died, with the pull request merged at another head, is not this merge",
         (code, "record" in f.calls) == (1, False) and "MERGE STATE UNKNOWN" not in f.out, (code, f.calls, f.out))
    code, f = flip(over, fail={"merge", "pr-readback"})
    case("flip: a merge whose state cannot be read is exit 4, with the gate restored", (code, f.enforcement) == (4, "active"), (code, f.enforcement))
    code, f = flip(over, restore_fails=2)
    case("flip: a restore that fails is tried again", (code, f.enforcement, f.calls.count("on")) == (0, "active", 3), (code, f.calls))
    code, f = flip(over, restore_fails=3)
    case("flip: a gate that stays off is exit 3, and the merge is still recorded",
         (code, f.enforcement, f.calls[-1]) == (3, "disabled", "record"), (code, f.enforcement, f.calls))
    code, f = flip(over, fail={"gate-readback"})
    case("flip: a gate that cannot be read back counts as not restored", code == 3, code)
    code, f = flip(over, fail={"off"})
    case("flip: a ruleset that cannot be switched off merges nothing", (code, "merge" in f.calls, f.enforcement) == (1, False, "active"), (code, f.calls))
    case("flip: a refused switch-off is named as that, not as a failed merge",
         "switching the ruleset off failed" in f.out and "the merge call failed" not in f.out, f.out)
    code, f = flip(over, fail={"merge"})
    case("flip: a refused merge is named as that", "the merge call failed" in f.out and "switching the ruleset off failed" not in f.out, f.out)
    code, f = flip(over, restore_fails=3)
    case("flip: every refused restore is printed with the forge's answer", f.out.count("restoring the gate failed") == 3 and "refused" in f.out, f.out)
    code, f = flip(over, fail={"merge", "pr-readback"}, restore_fails=3)
    case("flip: a gate that stays off outranks an unknown merge state, and both are said",
         code == 3 and "MERGE STATE UNKNOWN" in f.out and "GATE NOT RESTORED" in f.out, (code, f.out))
    code, f = flip(over, fail={"record"})
    case("flip: a merge whose waiver record could not be posted is exit 6, and says what to post",
         (code, f.calls[-1], f.merged) == (6, "record", True) and "`review` (absent)" in f.out, (code, f.calls, f.out))
    code, f = flip(over, fail={"record"}, restore_fails=3)
    case("flip: a gate that stays off outranks a missing record", code == 3 and "THE WAIVER RECORD IS MISSING" in f.out, (code, f.out))
    code, f = flip(over, fail={"record"}, lands={"record"}, how="gh: timeout awaiting response")
    case("flip: a record whose write died but landed is read back, found, and not called missing",
         code == 0 and "the waiver record IS on the pull request" in f.out and "MISSING" not in f.out and f.reads.count("record-readback") == 1,
         (code, f.reads, f.out))
    code, f = flip(over, fail={"record", "record-readback"}, how="gh: timeout awaiting response")
    case("flip: a record whose write died and cannot be read back may be posted: exit 6 says to look before posting again",
         code == 6 and "MAY BE MISSING" in f.out and "IS MISSING" not in f.out, (code, f.out))
    code, f = flip(over, restore_fails=3, lands={"on"})
    case("flip: a restore that failed three times but landed is read back as active: the gate is on, exit 0",
         (code, f.enforcement) == (0, "active") and "GATE NOT RESTORED" not in f.out and "gate-readback" in f.reads, (code, f.reads, f.out))
    code, f = flip([], fail={"merge"}, how="gh: timeout awaiting response")
    case("flip: a merge call that died without an answer is exit 4, not a refusal", code == 4 and "MERGE STATE UNKNOWN" in f.out, (code, f.out))
    code, f = flip([], fail={"merge"})
    case("flip: a merge the forge refused is exit 1", code == 1 and "MERGE STATE UNKNOWN" not in f.out, (code, f.out))
    code, f = flip([], silent=True)
    case("flip: an answer without merged=true is not taken for a merge", code == 4 and "merged o/r" not in f.out, (code, f.out))
    code, f = flip(over, fail={"merge"}, how="gh: timeout awaiting response")
    case("flip: an unknown merge state says which waiver record is owed", code == 4 and "its waiver record is owed" in f.out
         and "`review` (absent)" in f.out and "record" not in f.calls, (code, f.calls, f.out))
    code, f = flip(over, fail={"merge", "pr-readback"})
    case("flip: a read-back that fails is printed with the forge's answer", "the read-back failed too" in f.out, f.out)
    code, f = flip(over, fail={"gate-readback"})
    case("flip: a gate that cannot be read back says why", "gate read back: unreadable (" in f.out, f.out)
    code, f = flip(over, fail={"off"}, lands={"off"}, how="gh: timeout awaiting response")
    case("flip: a switch-off that landed and then failed is undone: nothing merged, the gate restored and read back",
         (code, "merge" in f.calls, "on" in f.calls, f.enforcement) == (1, False, True, "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"off"}, lands={"off"}, restore_fails=3)
    case("flip: a switch-off that landed and cannot be undone is exit 3",
         (code, f.enforcement) == (3, "disabled") and "GATE NOT RESTORED" in f.out, (code, f.enforcement, f.out))
    odd = ValueError("an answer nobody expected")
    code, f = flip(over, fail={"merge"}, error=odd)
    case("flip: a merge call that ends in an answer nobody expected is exit 4, with the gate restored",
         (code, f.enforcement) == (4, "active") and "MERGE STATE UNKNOWN" in f.out, (code, f.enforcement, f.out))
    code, f = flip(over, fail={"off"}, error=odd)
    case("flip: a switch-off that ends in an answer nobody expected merges nothing and leaves the gate on",
         (code, "merge" in f.calls, f.enforcement) == (1, False, "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"merge", "pr-readback"}, error=odd)
    case("flip: a read-back that ends in an answer nobody expected is exit 4, not a crash", code == 4 and "the read-back failed too" in f.out, (code, f.out))
    code, f = flip(over, fail={"gate-readback"}, error=odd)
    case("flip: a gate read-back that ends in an answer nobody expected counts as not restored", code == 3, (code, f.out))
    code, f = flip(over, fail={"record"}, error=odd)
    case("flip: a record that ends in an answer nobody expected is exit 6", code == 6 and "THE WAIVER RECORD IS MISSING" in f.out, (code, f.out))
    code, f = flip(over, fail={"on"}, error=odd)
    case("flip: a restore that ends in an answer nobody expected is read back, and a gate that is off is exit 3",
         (code, f.enforcement) == (3, "disabled"), (code, f.calls, f.enforcement))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    try:
        sys.exit(run(sys.argv[1:]))
    except ANSWER_ERRORS as e:  # before any write: merge() handles what follows one
        print(f"REFUSED: could not check: {type(e).__name__}: {e}")
        sys.exit(1)
