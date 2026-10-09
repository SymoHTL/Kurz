#!/usr/bin/env python3
"""Run every gate of this repository, in CI and locally, and say what each one did.

  gates.py             in CI: every gate; red when one fails or could not run
  gates.py [--pr N]    locally: the same gates; without --pr the pull-request gates print NOT RUN

A gate ends as PASS, PARTLY, FAIL, BROKEN, NOT RUN or N/A.
FAIL     the gate found what it exists to find, or its tool crashed or could not be opened: any
         exit but 0 and kit.PARTLY. Red.
BROKEN   the gate's process could not start. Red, in CI and locally: nothing was checked.
NOT RUN  a pull-request gate, and no pull request is known. Red in CI. Locally it is loud and does
         not fail the run, because a branch has no pull request before it is pushed.
PARTLY   the gate found nothing wrong in what it could read and named what it could not read (its
         tool exits with kit.PARTLY). Red, unless the gate is listed in PARTLY_OK with the issue
         that records what it cannot read. For a listed gate the run stays green, so the required
         check `gates` is satisfied although that part was not read: the table and the count line
         show PARTLY, never PASS, and the issue says what is unread.
N/A      the gate has nothing to look at: the pull-request gates on a push to main, the local
         gates in CI.
A run in which no gate ran at all is red. This list is the single definition of "the gates": the
workflow, the guide's evidence and the gate map all read it."""
import contextlib
import io
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

# (name, tool and arguments, where it applies: "always" | "pr" | "local", what turns it red)
GATES = [
    ("self-tests", ["tools/red_proof.py"], "always",
     "a tool's self-test fails, ran no case or contradicts its own exit code; a tool has no self-test or no recorded red proof; "
     "a recorded mutation no longer turns its one named case red"),
    ("tree", ["tools/tree_gate.py"], "always",
     "a path the design phase does not allow; a credential-shaped or machine-bound string or a merge-conflict marker, "
     "in a file or in its name; a file that is not UTF-8 text; a link or a submodule; fewer files than the floor. Before a "
     "push, the same in every commit it publishes, in the name of every ref it publishes and in the message and the name "
     "of every annotated tag and of each tag it points at, and a tag that points at a blob or a tree"),
    ("knowledge", ["tools/lint_knowledge.py"], "always",
     "an INDEX link to a missing file; an entry without an INDEX line, a hook or frontmatter; a store file named in an entry, "
     "in CLAUDE.md or in a skill that does not exist; a link in a store file that reaches no entry or file or leaves the "
     "repository; a nested entry; a LIVING entry without diagram or update triggers; "
     "expired or future-dated numbers in a guide; a generated block that is empty or is not what the page's digest says; "
     "fewer entries or living diagrams than their floors"),
    ("reference", ["tools/lint_reference.py"], "always",
     "a rule of the reference without id, status or the record section it cites; a decided, assumed or proposed rule with "
     "neither a case nor a reason; a case on an open rule; a sample that differs from its corpus file; a code block that is "
     "neither a case sample nor marked text, or that is never closed, and a fence the lint would not read; a corpus header that "
     "cannot be read or names an unknown rule; an error id the error table does not list, or lists for other rules; an "
     "error-table row that does not read as one, or a row that reads as one outside an error table; a design record that "
     "cannot be read; "
     "fewer rules or cases than the floors"),
    ("ci-config", ["tools/lint_ci.py"], "always",
     "a workflow fact that changed: an action not pinned by commit SHA, another runner label, an unpinned CLI, pip without hashes; "
     "a job in a container or beside services; the credential anywhere but the env of the job's last step; "
     "a gates job that can be skipped, a step with an `if` or one that may fail quietly; a path filter, a filter on pull_request "
     "or a push trigger not limited to main; a wider token; "
     "a key twice in one mapping; an expression inside a run line; a review job whose `if`, checkout or concurrency is not the "
     "pinned one; a workflow without facts"),
    ("merge-checks", ["tools/merge_pr.py", "--assert-settings"], "always",
     "the rules active on main differ from tools/ruleset.json or carry a parameter that file does not name, "
     "the ruleset is not active, it has bypass actors, or auto-merge is allowed; PARTLY where the token cannot read the last two"),
    ("pr-title", ["tools/pr_gates.py", "title"], "pr",
     "a workflow-skip literal, a credential-shaped or machine-bound string or a conflict marker in the title, the description "
     "or a commit message of the branch: each of them can become the squash commit on main"),
    ("pr-breadth", ["tools/pr_gates.py", "breadth"], "pr",
     "more than 15 files, or a change to the quality infrastructure, without a Blast radius section"),
    ("pr-findings", ["tools/pr_gates.py", "findings"], "pr",
     "a review finding that is unresolved, or resolved without the edit (or, on tool, workflow or hook code, the reply) that answers it"),
    ("push-hook", ["tools/gates.py", "--check-push-hook"], "local",
     "this clone would publish without the tree gate: core.hooksPath is not .githooks"),
]
VERDICTS = ("PASS", "PARTLY", "FAIL", "BROKEN", "NOT RUN", "N/A")
# gate -> the issue that records what it cannot read where it ends PARTLY. Any other PARTLY is red.
PARTLY_OK = {"merge-checks": 7}


def context(argv, env):
    """What applies in this run: {"ci": bool, "pr": a number, "N/A" or None (unknown)}. A `--pr`
    without its number is refused, not read as no pull request: pr_gates.py refuses the same."""
    ci = env.get("GITHUB_ACTIONS") == "true"
    if "--pr" in argv:
        pr = (argv[argv.index("--pr") + 1:] or [""])[0]
        if not pr.isdigit():
            raise kit.Refused("--pr needs the number of the pull request")
    elif ci:
        # only a push to main has no pull request to look at; a push elsewhere is a run nobody asked for
        on_main = env.get("GITHUB_EVENT_NAME") == "push" and env.get("GITHUB_REF") == "refs/heads/main"
        pr = "N/A" if on_main else env.get("PR_NUMBER") or None
    else:
        pr = None
    return {"ci": ci, "pr": pr}


def run_all(gates, ctx, runner, partly_ok=PARTLY_OK):
    """[(name, verdict)], the count per verdict and the exit code.
    runner(command, pr) -> exit code, or None when it could not start."""
    verdicts = []
    for name, command, where, _ in gates:
        if (where == "local" and ctx["ci"]) or (where == "pr" and ctx["pr"] == "N/A"):
            verdicts.append((name, "N/A"))
        elif where == "pr" and not ctx["pr"]:
            verdicts.append((name, "NOT RUN"))
        else:
            code = runner(command, ctx["pr"])
            verdicts.append((name, "BROKEN" if code is None else "PASS" if code == 0 else "PARTLY" if code == kit.PARTLY else "FAIL"))
    count = {v: sum(verdict == v for _, verdict in verdicts) for v in VERDICTS}
    unlisted = [name for name, verdict in verdicts if verdict == "PARTLY" and name not in partly_ok]
    ran = count["PASS"] + count["PARTLY"] + count["FAIL"]
    red = count["FAIL"] or count["BROKEN"] or unlisted or not ran or (ctx["ci"] and count["NOT RUN"])
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


def hooks_path():
    return kit.run(["git", "config", "--get", "core.hooksPath"])


def push_hook(read=hooks_path):
    """0 when pushes from this clone go through the tree gate. git answers a setting that is not
    set with a failed command, so a refusal reads as not set."""
    try:
        path = read().strip()
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

    def case(name, gates, ctx, table, want_exit, want_verdicts=None, partly_ok=()):
        verdicts, count, code = run_all(gates, ctx, codes(table), partly_ok)
        ok = code == want_exit and (want_verdicts is None or dict(verdicts) == want_verdicts)
        cases.append((name, ok, f"exit {code}, {verdicts}"))

    case("all gates pass", always, ci_pr, {}, 0, {"a": "PASS", "b": "PASS"})
    case("one failing gate is red, and the others still run", always, ci_pr, {"a": 1}, 1, {"a": "FAIL", "b": "PASS"})
    case("a crashed gate is a failure", always, ci_pr, {"b": 2}, 1, {"a": "PASS", "b": "FAIL"})
    case("a listed gate that could read only part is PARTLY: not red, and not a pass", always, ci_pr, {"a": kit.PARTLY}, 0,
         {"a": "PARTLY", "b": "PASS"}, partly_ok={"a": 1})
    case("a gate that is not listed and ends PARTLY turns the run red", always, ci_pr, {"a": kit.PARTLY}, 1,
         {"a": "PARTLY", "b": "PASS"}, partly_ok={"b": 1})
    case("the same locally: an unlisted PARTLY is red", always, local, {"a": kit.PARTLY}, 1, {"a": "PARTLY", "b": "PASS"})
    partly = run_all(always, ci_pr, codes({"a": kit.PARTLY}), {"a": 1})[1]
    cases.append(("a PARTLY gate is counted apart from the passes", (partly["PARTLY"], partly["PASS"]) == (1, 1), partly))
    case("a failing gate next to a PARTLY one is still red", always, ci_pr, {"a": kit.PARTLY, "b": 1}, 1,
         {"a": "PARTLY", "b": "FAIL"}, partly_ok={"a": 1})
    listed = [name for name in PARTLY_OK if name not in [g[0] for g in GATES] or not isinstance(PARTLY_OK[name], int)]
    cases.append(("every gate that may end PARTLY is a gate of the list and names its issue", PARTLY_OK and not listed, listed))
    case("a gate that could not start is BROKEN, and red in CI", always, ci_pr, {"a": None}, 1, {"a": "BROKEN", "b": "PASS"})
    case("a gate that could not start is red locally too", always, local, {"a": None}, 1, {"a": "BROKEN", "b": "PASS"})
    case("no gates at all is red", [], ci_pr, {}, 1)
    case("a run in which no gate ran is red", [("p", ["p"], "pr", "")], local, {}, 1, {"p": "NOT RUN"})
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
    for argv in (["--pr"], ["--pr", "seven"]):
        try:
            context(argv, {})
            cases.append((f"context: {' '.join(argv)} is refused, not read as no pull request", False, "no exception"))
        except kit.Refused as e:
            cases.append((f"context: {' '.join(argv)} is refused, not read as no pull request", "needs the number" in str(e), str(e)))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/main", "PR_NUMBER": ""})
    cases.append(("context: a push to main has no pull request", ctx == {"ci": True, "pr": "N/A"}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/next", "PR_NUMBER": ""})
    cases.append(("context: a push to another branch is unknown, not N/A: its pull-request gates are NOT RUN", ctx == {"ci": True, "pr": None}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "pull_request", "PR_NUMBER": "3"})
    cases.append(("context: a pull request event carries its number", ctx == {"ci": True, "pr": "3"}, ctx))
    ctx = context([], {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "pull_request", "PR_NUMBER": ""})
    cases.append(("context: a pull request event without a number is unknown, not N/A", ctx == {"ci": True, "pr": None}, ctx))

    def refused():
        raise kit.Refused("`git config --get core.hooksPath` exited 1: ")

    with contextlib.redirect_stdout(io.StringIO()):
        hook = (push_hook(lambda: ".githooks\n"), push_hook(lambda: "hooks\n"), push_hook(lambda: "\n"), push_hook(refused))
    cases.append(("push hook: .githooks passes", hook[0] == 0, hook))
    cases.append(("push hook: another hooks directory fails", hook[1] == 1, hook))
    cases.append(("push hook: a setting that is empty or not set fails", hook[2:] == (1, 1), hook))
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
    try:
        ctx = context(sys.argv, os.environ)
    except kit.Refused as e:
        sys.exit(f"gates could not start: {e}")
    verdicts, count, code = run_all(GATES, ctx, runner)
    print("\n=== gates")
    for name, verdict in verdicts:
        unlisted = verdict == "PARTLY" and name not in PARTLY_OK
        print(f"{verdict:<8} {name}{'  (red: no issue records what this gate did not read)' if unlisted else ''}")
    hint = " (no pull request: pass --pr N)" if count["NOT RUN"] and not ctx["ci"] else ""
    print(f"GATES: {count['PASS']} passed, {count['PARTLY']} PARTLY checked, {count['FAIL']} failed, "
          f"{count['BROKEN']} could not start, {count['NOT RUN']} NOT RUN{hint}, {count['N/A']} not applicable")
    sys.exit(code)
