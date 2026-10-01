#!/usr/bin/env python3
"""The one way a pull request reaches main, and the check that the server-side merge rules are on.

  merge_pr.py <pr> <full head sha> [--dry-run]
  merge_pr.py <pr> <full head sha> --over-red <pr>@<full head sha>=<check>[,<check>]
  merge_pr.py --assert-settings      the live ruleset equals tools/ruleset.json and is active
  merge_pr.py --apply-settings       create or update that ruleset (owner-approved, once)

A merge needs: the exact head that was looked at; an open pull request into the default branch that
is not a Draft, not behind its base and has no auto-merge armed; every review thread resolved, and
resolved by an edit (tools/pr_gates.py); every required check of the ruleset `success` on that head.

--over-red is the gate-flip: the owner approved merging THIS head with THESE checks not green. The
ruleset is switched off for the one merge, restored on every exit path and read back. While it is
off every pull request could merge ungated, so the approval is per item and never standing. The
waiver lets through only what it names:
- it names a pull request and a head; another pull request is refused, another head voids it;
- a name waives a required check that failed, was skipped or never reported on this head; a check
  nobody named still blocks;
- a name that waives nothing (a passing check, an unknown name) stops the run, so a typo cannot
  print as a waiver;
- a check that is still running is never waived: its result is coming and nobody has read it;
- unresolved threads are never waived.

Exit codes: 0 merged (or dry run that would merge, or settings as expected); 1 refused; 2 usage;
3 GATE NOT RESTORED, switch the ruleset back on by hand; 4 merge state unknown, look at the pull
request before doing anything else."""
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
    for rule in rules["rules"]:
        if rule["type"] == "required_status_checks":
            return [c["context"] for c in rule["parameters"]["required_status_checks"]]
    return []


def context_states(contexts, check_runs, statuses):
    """{context: success | failed | skipped | running | absent} on one head. A context that both a
    check run and a commit status report must be green in both. The newest check run of a name counts."""
    states = {}
    for ctx in contexts:
        seen = []
        runs = [r for r in check_runs if r["name"] == ctx]
        if runs:
            run = max(runs, key=lambda r: r["id"])
            seen.append("running" if run["status"] != "completed" else
                        {"success": "success", "skipped": "skipped"}.get(run["conclusion"], "failed"))
        for s in statuses:
            if s["context"] == ctx:
                seen.append({"success": "success", "pending": "running"}.get(s["state"], "failed"))
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
    return errors


def settings_errors(expected, info, live_rules, rulesets, detail):
    """(errors, notes) from what the forge answered: the repository, the rules active on its default
    branch, the list of rulesets, and detail(id) -> one ruleset in full. A field this token cannot
    read becomes a NOT CHECKED note, never a pass."""
    errors, notes = compare_rules(expected, live_rules), []
    if "allow_auto_merge" not in info:
        notes.append("auto-merge setting NOT CHECKED: this token cannot read it (an admin run checks it)")
    elif info["allow_auto_merge"]:
        errors.append("auto-merge is allowed in the repository settings")
    mine = [r for r in rulesets if r["name"] == expected["name"]]
    if len(mine) != 1:
        return errors + [f"{len(mine)} rulesets are named {expected['name']!r}, expected 1"], notes
    one = detail(mine[0]["id"])
    if one.get("enforcement") != "active":
        errors.append(f"the ruleset is {one.get('enforcement')}, not active")
    if "bypass_actors" not in one:
        notes.append("bypass list NOT CHECKED: this token cannot read it (an admin run checks it)")
    elif one["bypass_actors"]:
        errors.append("the ruleset has bypass actors; nobody may bypass the merge checks")
    return errors, notes


def find_ruleset(repo):
    name = ruleset()["name"]
    found = [r for r in kit.gh_json(f"repos/{repo}/rulesets") if r["name"] == name]
    if len(found) != 1:
        raise kit.Refused(f"{len(found)} rulesets are named {name!r}, expected 1")
    return found[0]["id"]


def send(path, payload, method="PUT"):
    return json.loads(kit.run(["gh", "api", "-X", method, path, "--input", "-"], stdin=json.dumps(payload)) or "{}")


def merge(repo, number, sha, pr, waived):
    """Merge exactly `sha`. With a waiver the ruleset is off for the one call and restored on every path."""
    payload = {"sha": sha, "merge_method": "squash", "commit_title": f"{pr['title']} (#{number})", "commit_message": pr["body"] or ""}
    gate = find_ruleset(repo) if waived else None
    landed, code = False, 0
    try:
        if gate:
            send(f"repos/{repo}/rulesets/{gate}", {"enforcement": "disabled"})
        send(f"repos/{repo}/pulls/{number}/merge", payload)
        landed = True
        print(f"merged {repo}#{number} at {sha}")
    except kit.Refused as e:
        print(f"the merge call failed: {e}")
        try:  # a write that may have landed is never reported as a plain failure
            landed = kit.gh_json(f"repos/{repo}/pulls/{number}").get("merged") is True
            code = 0 if landed else 1
            print("read back: the pull request IS merged" if landed else "read back: not merged")
        except kit.Refused:
            print("MERGE STATE UNKNOWN: look at the pull request before doing anything else")
            code = 4
    finally:
        if gate:
            for _ in range(3):
                try:
                    send(f"repos/{repo}/rulesets/{gate}", {"enforcement": "active"})
                    break
                except kit.Refused:
                    time.sleep(2)
            try:
                back = kit.gh_json(f"repos/{repo}/rulesets/{gate}").get("enforcement")
            except kit.Refused:
                back = "unreadable"
            print(f"gate read back: {back}")
            if back != "active":
                print("GATE NOT RESTORED: set the ruleset's enforcement back to active by hand NOW")
                code = 3
    if landed and waived:  # the record follows the merge, whatever became of the gate afterwards
        record = ", ".join(f"`{name}` ({state})" for name, state in waived)
        try:
            send(f"repos/{repo}/issues/{number}/comments", {"body":
                f"Merged over red at `{sha}` with the owner's approval for this item. Waived on this head: {record}."}, method="POST")
        except kit.Refused as e:
            print(f"the waiver record could not be posted on the pull request: {e}")
    return code


def thread_reasons(threads, changed_since):
    """What the threads of a pull request hold against a merge: any unresolved thread, and any
    review finding that was resolved without its answer (tools/pr_gates.py)."""
    unresolved = sum(not t["isResolved"] for t in threads)
    errors = [f"{unresolved} unresolved threads"] if unresolved else []
    return errors + [e for e in pr_gates.findings_errors(threads, changed_since) if not e.startswith("unresolved")]


def arguments(argv):
    """(pull request, sha, waiver or None) of a merge call, or None when the call is not understood.
    An --over-red whose value is missing or is not a waiver is not understood: it never falls back
    to a plain merge."""
    spec = argv[argv.index("--over-red") + 1] if "--over-red" in argv[:-1] else None
    waiver = parse_waiver(spec) if spec else None
    positional = [a for a in argv if not a.startswith("--") and a != spec]
    if ("--over-red" in argv and not waiver) or len(positional) != 2 or not positional[0].isdigit():
        return None
    return int(positional[0]), positional[1], waiver


def run(argv):
    repo = kit.repo()
    if "--assert-settings" in argv or "--apply-settings" in argv:
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
        return 1 if errors else 0

    understood = arguments(argv)
    if not understood:
        print("usage: merge_pr.py <pr> <full head sha> [--dry-run] [--over-red <pr>@<full head sha>=<check>[,<check>]]")
        return 2
    number, sha, waiver = understood
    pr = kit.gh_json(f"repos/{repo}/pulls/{number}")
    head, default_branch = pr["head"]["sha"], kit.gh_json(f"repos/{repo}")["default_branch"]
    runs = kit.gh_json(f"repos/{repo}/commits/{head}/check-runs?per_page=100")["check_runs"]
    statuses = kit.gh_json(f"repos/{repo}/commits/{head}/status?per_page=100")["statuses"]
    states = context_states(required(ruleset()), runs, statuses)
    threads = pr_gates.fetch_threads(repo, number)
    changed = lambda path, at: pr_gates.blob_at(repo, path, at) != pr_gates.blob_at(repo, path, head)
    behind = kit.gh_json(f"repos/{repo}/compare/{pr['base']['ref']}...{head}")["behind_by"]

    verdict, reasons, waived = decide(number, sha, pr, default_branch, states, thread_reasons(threads, changed), behind, waiver)
    print(f"{repo}#{number} at {head[:8]}: " + ", ".join(f"{c}={s}" for c, s in states.items()) + f"; {len(threads)} threads; behind by {behind}")
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

    pr, runs, status, live = load("pull-request.json"), load("check-runs.json")["check_runs"], load("status.json")["statuses"], load("branch-rules.json")
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
    real_states = context_states(["gates", "review"], runs, status)
    case("states: the real check run of the gates job is read", real_states["gates"] in ("success", "failed"), real_states)
    run0 = next(r for r in runs if r["name"] == "gates")
    newer = dict(run0, id=run0["id"] + 1)
    for name, change, want in [("a completed success", {"status": "completed", "conclusion": "success"}, "success"),
                               ("a failure", {"status": "completed", "conclusion": "failure"}, "failed"),
                               ("a cancelled run", {"status": "completed", "conclusion": "cancelled"}, "failed"),
                               ("a skipped job is not a success", {"status": "completed", "conclusion": "skipped"}, "skipped"),
                               ("a queued run", {"status": "queued", "conclusion": None}, "running"),
                               ("a run in progress", {"status": "in_progress", "conclusion": None}, "running")]:
        got = context_states(["gates"], [dict(run0, **change)], [])["gates"]
        case(f"states: {name}", got == want, got)
    got = context_states(["gates"], [dict(run0, status="completed", conclusion="success"), dict(newer, status="completed", conclusion="failure")], [])
    case("states: the newest run of a name counts", got["gates"] == "failed", got)
    case("states: a check nobody reported is absent", context_states(["review"], runs, [])["review"] == "absent")
    one = lambda state: [{"context": "review", "state": state}]
    case("states: a success status", context_states(["review"], [], one("success"))["review"] == "success")
    case("states: a pending status is running", context_states(["review"], [], one("pending"))["review"] == "running")
    case("states: an error status is a failure", context_states(["review"], [], one("error"))["review"] == "failed")
    both = context_states(["gates"], [dict(run0, status="completed", conclusion="success")], [{"context": "gates", "state": "failure"}])
    case("states: a check and a status of one name must both pass", both["gates"] == "failed", both)

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
    case("settings: required checks are named", required(expected) == ["gates", "review"], required(expected))
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

    # the gate-flip, against a forge that records every write and fails where it is told to
    class Flip:
        def __init__(self, fail=(), merged=False, restore_fails=0):
            self.fail, self.merged, self.restore_fails = set(fail), merged, restore_fails
            self.calls, self.enforcement, self.payload = [], "active", None

        def send(self, path, payload, method="PUT"):
            what = ({"disabled": "off", "active": "on"}[payload["enforcement"]] if "/rulesets/" in path
                    else "merge" if path.endswith("/merge") else "record")
            self.calls.append(what)
            if what == "on" and self.restore_fails > 0:
                self.restore_fails -= 1
                raise kit.Refused("refused")
            if what in self.fail:
                raise kit.Refused("refused")
            if what in ("off", "on"):
                self.enforcement = payload["enforcement"]
            if what == "merge":
                self.merged, self.payload = True, payload
            return {}

        def gh_json(self, path):
            kind = "gate-readback" if "/rulesets/" in path else "pr-readback"
            if kind in self.fail:
                raise kit.Refused("refused")
            return {"enforcement": self.enforcement} if kind == "gate-readback" else {"merged": self.merged}

    def flip(waived, **how):
        fake, g = Flip(**how), globals()
        saved = g["send"], g["find_ruleset"], kit.gh_json, time.sleep
        g["send"], g["find_ruleset"], kit.gh_json, time.sleep = fake.send, (lambda repo: 7), fake.gh_json, (lambda seconds: None)
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                code = merge("o/r", number, sha, ready, waived)
        finally:
            g["send"], g["find_ruleset"], kit.gh_json, time.sleep = saved
        return code, fake

    over = [("review", "absent")]
    code, f = flip([])
    case("flip: without a waiver the ruleset is never touched", (code, f.calls) == (0, ["merge"]), (code, f.calls))
    case("flip: the merge names the exact head and squashes", bool(f.payload) and f.payload.get("sha") == sha and f.payload.get("merge_method") == "squash", f.payload)
    code, f = flip(over)
    case("flip: with a waiver the ruleset is off for the one merge, restored, and the waiver is recorded",
         (code, f.calls, f.enforcement) == (0, ["off", "merge", "on", "record"], "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"merge"})
    case("flip: a refused merge still restores the gate, and records nothing",
         (code, f.calls, f.enforcement) == (1, ["off", "merge", "on"], "active"), (code, f.calls, f.enforcement))
    code, f = flip(over, fail={"merge"}, merged=True)
    case("flip: a merge call that failed but landed is a merge, and is recorded", (code, f.calls) == (0, ["off", "merge", "on", "record"]), (code, f.calls))
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
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    try:
        sys.exit(run(sys.argv[1:]))
    except (kit.Refused, KeyError, IndexError, ValueError) as e:
        print(f"REFUSED: could not check: {type(e).__name__}: {e}")
        sys.exit(1)
