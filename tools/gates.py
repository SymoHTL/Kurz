#!/usr/bin/env python3
"""Run every gate of this repository, in CI and locally, and say what each one did.

  gates.py             in CI: every gate; red when one fails or could not run
  gates.py [--pr N]    locally: the same gates; without --pr the pull-request gates print NOT RUN

A gate ends as PASS, PARTLY, FAIL, NOT RUN or N/A. FAIL and, in CI, NOT RUN turn the run red. Locally a
NOT RUN is loud and does not fail the run, because a branch has no pull request before it is pushed.
NOT RUN means the gate could not start, or it is a pull-request gate and no pull request is known.
PARTLY means the gate found nothing wrong in what it could read and named what it could not read
(its tool exits with kit.PARTLY). It does not turn the run red, and it is not a pass: the table and
the count line show it, so the unread part never looks clean.
N/A means the gate has nothing to look at: the pull-request gates on a push to main, the local
gates in CI. This list is the single definition of "the gates": the workflow, the guide's evidence
and the gate map all read it."""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

# (name, tool and arguments, where it applies: "always" | "pr" | "local", what turns it red)
GATES = [
    ("self-tests", ["tools/red_proof.py"], "always",
     "a tool's self-test fails or ran no case; a tool has no self-test or no recorded red proof; "
     "a recorded mutation no longer turns its case red"),
    ("tree", ["tools/tree_gate.py"], "always",
     "a path the design phase does not allow; a credential-shaped or machine-bound string; a merge-conflict marker; "
     "a file that is not UTF-8 text; fewer files than the floor"),
    ("knowledge", ["tools/lint_knowledge.py"], "always",
     "an INDEX link to a missing file; an entry without an INDEX line, a hook or frontmatter; a store file named in an entry, "
     "in CLAUDE.md or in a skill that does not exist; a nested entry; a LIVING "
     "entry without diagram or update triggers; expired or future-dated numbers in a guide; fewer entries than the floor"),
    ("reference", ["tools/lint_reference.py"], "always",
     "a rule of the reference without id, status or the record section it cites; a decided, assumed or proposed rule with "
     "neither a case nor a reason; a case on an open rule; a sample that differs from its corpus file; a corpus header that "
     "cannot be read or names an unknown rule; an error id the error table does not list, or lists for other rules; "
     "fewer rules or cases than the floors"),
    ("ci-config", ["tools/lint_ci.py"], "always",
     "a workflow fact that changed: an unpinned action, image or CLI; a job or step that can be skipped or may fail quietly; "
     "a path filter; a wider token; pull-request code or text reaching the review job; a workflow without facts"),
    ("merge-checks", ["tools/merge_pr.py", "--assert-settings"], "always",
     "the rules active on main differ from tools/ruleset.json or carry a parameter that file does not name, "
     "the ruleset is not active, it has bypass actors, or auto-merge is allowed; PARTLY where the token cannot read the last two"),
    ("pr-title", ["tools/pr_gates.py", "title"], "pr",
     "a workflow-skip literal in the title, the description or a commit message of the branch; a credential-shaped or "
     "machine-bound string in the title or the description, which become the squash commit on main"),
    ("pr-breadth", ["tools/pr_gates.py", "breadth"], "pr",
     "more than 15 files, or a change to the quality infrastructure, without a Blast radius section"),
    ("pr-findings", ["tools/pr_gates.py", "findings"], "pr",
     "a review finding that is unresolved, or resolved without the edit (or, on tool code, the reply) that answers it"),
    ("push-hook", ["tools/gates.py", "--check-push-hook"], "local",
     "this clone would publish without the tree gate: core.hooksPath is not .githooks"),
]


def context(argv, env):
    """What applies in this run: {"ci": bool, "pr": a number, "N/A" or None (unknown)}."""
    ci = env.get("GITHUB_ACTIONS") == "true"
    if "--pr" in argv:
        pr = argv[argv.index("--pr") + 1]
    elif ci:
        pr = "N/A" if env.get("GITHUB_EVENT_NAME") == "push" else env.get("PR_NUMBER") or None
    else:
        pr = None
    return {"ci": ci, "pr": pr}


def run_all(gates, ctx, runner):
    """[(name, verdict)] and the exit code. runner(command, pr) -> exit code, or None when it could not start."""
    verdicts = []
    for name, command, where, _ in gates:
        if (where == "local" and ctx["ci"]) or (where == "pr" and ctx["pr"] == "N/A"):
            verdicts.append((name, "N/A"))
        elif where == "pr" and not ctx["pr"]:
            verdicts.append((name, "NOT RUN"))
        else:
            code = runner(command, ctx["pr"])
            verdicts.append((name, "NOT RUN" if code is None else "PASS" if code == 0 else "PARTLY" if code == kit.PARTLY else "FAIL"))
    count = {v: sum(verdict == v for _, verdict in verdicts) for v in ("PASS", "PARTLY", "FAIL", "NOT RUN", "N/A")}
    red = count["FAIL"] or not gates or (ctx["ci"] and count["NOT RUN"])
    return verdicts, count, 1 if red else 0


def runner(command, pr):
    print(f"\n=== gate: {' '.join(command)}", flush=True)
    env = dict(os.environ)
    if pr and pr != "N/A":
        env["PR_NUMBER"] = str(pr)
    try:
        return subprocess.run([sys.executable, *command], cwd=kit.ROOT, env=env).returncode
    except OSError as e:
        print(f"could not start: {e}")
        return None


def push_hook():
    try:
        path = kit.run(["git", "config", "--get", "core.hooksPath"]).strip()
    except kit.Refused:
        path = ""
    if path != ".githooks":
        print(f"core.hooksPath is {path or 'not set'}: run `git config core.hooksPath .githooks` once in this clone")
        return 1
    print("pushes from this clone go through .githooks/pre-push")
    return 0


def self_test():
    always = [("a", ["a"], "always", ""), ("b", ["b"], "always", "")]
    with_pr = always + [("p", ["p"], "pr", "")]
    with_local = always + [("l", ["l"], "local", "")]
    codes = lambda table: (lambda command, pr: table.get(command[0], 0))
    ci_pr, ci_push, local = {"ci": True, "pr": "7"}, {"ci": True, "pr": "N/A"}, {"ci": False, "pr": None}
    cases = []

    def case(name, gates, ctx, table, want_exit, want_verdicts=None):
        verdicts, count, code = run_all(gates, ctx, codes(table))
        ok = code == want_exit and (want_verdicts is None or dict(verdicts) == want_verdicts)
        cases.append((name, ok, f"exit {code}, {verdicts}"))

    case("all gates pass", always, ci_pr, {}, 0, {"a": "PASS", "b": "PASS"})
    case("one failing gate is red, and the others still run", always, ci_pr, {"a": 1}, 1, {"a": "FAIL", "b": "PASS"})
    case("a crashed gate is a failure", always, ci_pr, {"b": 2}, 1, {"a": "PASS", "b": "FAIL"})
    case("a gate that could read only part is PARTLY: not red, and not a pass", always, ci_pr, {"a": kit.PARTLY}, 0, {"a": "PARTLY", "b": "PASS"})
    partly = run_all(always, ci_pr, codes({"a": kit.PARTLY}))[1]
    cases.append(("a PARTLY gate is counted apart from the passes", (partly["PARTLY"], partly["PASS"]) == (1, 1), partly))
    case("a failing gate next to a PARTLY one is still red", always, ci_pr, {"a": kit.PARTLY, "b": 1}, 1, {"a": "PARTLY", "b": "FAIL"})
    case("a gate that could not start is NOT RUN, and red in CI", always, ci_pr, {"a": None}, 1, {"a": "NOT RUN", "b": "PASS"})
    case("no gates at all is red", [], ci_pr, {}, 1)
    case("in a pull request run the pull-request gates run", with_pr, ci_pr, {}, 0, {"a": "PASS", "b": "PASS", "p": "PASS"})
    case("a failing pull-request gate is red", with_pr, ci_pr, {"p": 1}, 1)
    case("on a push to main they have nothing to look at", with_pr, ci_push, {"p": 1}, 0, {"a": "PASS", "b": "PASS", "p": "N/A"})
    case("locally without a pull request they are NOT RUN, loudly", with_pr, local, {"p": 1}, 0, {"a": "PASS", "b": "PASS", "p": "NOT RUN"})
    case("locally with --pr they run", with_pr, {"ci": False, "pr": "7"}, {"p": 1}, 1)
    case("a CI pull request run without a number is NOT RUN, and red", with_pr, {"ci": True, "pr": None}, {}, 1,
         {"a": "PASS", "b": "PASS", "p": "NOT RUN"})
    case("local gates are N/A in CI", with_local, ci_pr, {"l": 1}, 0, {"a": "PASS", "b": "PASS", "l": "N/A"})
    case("local gates run locally", with_local, local, {"l": 1}, 1)
    ctx = context(["--pr", "12"], {})
    cases.append(("context: --pr names the pull request", ctx == {"ci": False, "pr": "12"}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "push", "PR_NUMBER": ""})
    cases.append(("context: a push has no pull request", ctx == {"ci": True, "pr": "N/A"}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "pull_request", "PR_NUMBER": "3"})
    cases.append(("context: a pull request event carries its number", ctx == {"ci": True, "pr": "3"}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "pull_request", "PR_NUMBER": ""})
    cases.append(("context: a pull request event without a number is unknown, not N/A", ctx == {"ci": True, "pr": None}, ctx))
    names = [g[0] for g in GATES]
    cases.append(("every gate has a name of its own and says what turns it red", len(set(names)) == len(names) and all(g[3] for g in GATES), names))
    missing = [g[1][0] for g in GATES if not os.path.isfile(os.path.join(kit.ROOT, g[1][0]))]
    cases.append(("every gate's tool exists", not missing, missing))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    if "--check-push-hook" in sys.argv:
        sys.exit(push_hook())
    ctx = context(sys.argv, os.environ)
    verdicts, count, code = run_all(GATES, ctx, runner)
    print("\n=== gates")
    for name, verdict in verdicts:
        print(f"{verdict:<8} {name}")
    hint = " (no pull request: pass --pr N)" if count["NOT RUN"] and not ctx["ci"] else ""
    print(f"GATES: {count['PASS']} passed, {count['PARTLY']} PARTLY checked, {count['FAIL']} failed, "
          f"{count['NOT RUN']} NOT RUN{hint}, {count['N/A']} not applicable")
    sys.exit(code)
