#!/usr/bin/env python3
"""Unit facts of the two workflows. A one-token YAML fact that nothing compiles gets a check here:
- both: actions pinned by commit SHA, the runner image pinned, a timeout on every job, pip with
  --require-hashes, no job or step that may fail quietly (`continue-on-error`), no step-level `if`,
  no reusable-workflow job;
- gates.yml: runs on every pull request event that can change a verdict and on pushes to main,
  with no path or branch filter; exactly one job, `gates`, with no `if` (a skipped job reports
  success to a required check); read-only token; the job runs the full gate runner;
- review.yml: pull_request_target plus a manual dispatch, so the reviewer and its rules come from
  the default branch; the checkout never names pull-request code; no pull-request text is put into a
  shell line; the job's `if` is the pinned draft/outsider rule; the job timeout equals the budget
  the script is told; the Claude CLI is pinned to an exact version; only the two credential
  secrets are read; one review per pull request at a time.
`--self-test` breaks each fact in the real files, one token at a time, and expects its error."""
import os
import re
import sys

import yaml

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

WORKFLOWS = ".github/workflows"
PR_TYPES = {"opened", "synchronize", "reopened", "ready_for_review", "edited"}
REVIEW_TYPES = ["opened", "synchronize", "reopened", "ready_for_review"]
REVIEW_IF = ("github.event_name == 'workflow_dispatch' || (github.event.pull_request.draft == false && "
             "contains(fromJSON('[\"OWNER\",\"MEMBER\",\"COLLABORATOR\"]'), "
             "github.event.pull_request.author_association))")
REVIEW_SECRETS = {"CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY"}
PR_TEXT = re.compile(r"\$\{\{[^}]*(?:github\.event\.pull_request|github\.head_ref|inputs\.)[^}]*\}\}")


def parse(text):
    data = yaml.safe_load(text)
    if not isinstance(data, dict) or not isinstance(data.get("jobs"), dict):
        raise ValueError("no jobs")
    on = data.get("on", data.get(True))  # YAML 1.1 reads the bare key `on` as a boolean
    return data, on if isinstance(on, dict) else {}


def common(name, text, data):
    errors = []
    for used in re.findall(r"^\s*-?\s*uses:\s*(\S+)", text, re.M):
        if not re.fullmatch(r"[\w.-]+/[\w./-]+@[0-9a-f]{40}", used):
            errors.append(f"{name}: action not pinned by commit SHA: {used}")
    for line in text.splitlines():
        if re.search(r"\bpip\"? install\b", line) and "--require-hashes" not in line:
            errors.append(f"{name}: pip install without --require-hashes")
    for jid, job in data["jobs"].items():
        steps = job.get("steps") or []
        if "uses" in job:
            errors.append(f"{name}: job {jid} calls another workflow; a waiver could not tell what it covers")
        if job.get("runs-on") != "ubuntu-24.04":
            errors.append(f"{name}: job {jid} runner image is not pinned to ubuntu-24.04")
        if "timeout-minutes" not in job:
            errors.append(f"{name}: job {jid} has no timeout")
        if "continue-on-error" in job or any("continue-on-error" in s for s in steps):
            errors.append(f"{name}: job {jid} uses continue-on-error")
        if any("if" in s for s in steps):
            errors.append(f"{name}: job {jid} has a step that may be skipped (`if`)")
    return errors


def gates_facts(text):
    name = "gates.yml"
    try:
        data, on = parse(text)
    except (yaml.YAMLError, ValueError) as e:
        return [f"{name}: cannot be read as a workflow: {type(e).__name__}"]
    errors = common(name, text, data)
    if set(on) != {"pull_request", "push"}:
        errors.append(f"{name}: triggers are {sorted(map(str, on))}, expected pull_request and push")
    pr, push = on.get("pull_request") or {}, on.get("push") or {}
    if not PR_TYPES <= set(pr.get("types") or []):
        errors.append(f"{name}: pull_request types must include {sorted(PR_TYPES)}")
    if push.get("branches") != ["main"]:
        errors.append(f"{name}: push must be limited to the branch main")
    if set(pr) - {"types"} or set(push) - {"branches"}:
        errors.append(f"{name}: a path filter or branch filter can skip the gates")
    if data.get("permissions") != {"contents": "read", "pull-requests": "read"}:
        errors.append(f"{name}: permissions must be exactly contents: read, pull-requests: read")
    if list(data["jobs"]) != ["gates"]:
        errors.append(f"{name}: jobs must be exactly [gates]")
    job = data["jobs"].get("gates") or {}
    if "if" in job:
        errors.append(f"{name}: the job gates may be skipped (`if`), and a skipped job reports success")
    steps = job.get("steps") or []
    last = steps[-1] if steps else {}
    if not re.fullmatch(r'"\$RUNNER_TEMP/venv/bin/python" tools/gates\.py', str(last.get("run", "")).strip()):
        errors.append(f"{name}: the last step must be the gates step, running tools/gates.py with no flags")
    if (last.get("env") or {}).get("PR_NUMBER") != "${{ github.event.pull_request.number }}":
        errors.append(f"{name}: the gates step needs PR_NUMBER from the pull request event")
    return errors


def review_facts(text):
    name = "review.yml"
    try:
        data, on = parse(text)
    except (yaml.YAMLError, ValueError) as e:
        return [f"{name}: cannot be read as a workflow: {type(e).__name__}"]
    errors = common(name, text, data)
    if set(on) != {"pull_request_target", "workflow_dispatch"}:
        errors.append(f"{name}: trigger must be pull_request_target plus workflow_dispatch, found {sorted(map(str, on))}")
    if (on.get("pull_request_target") or {}).get("types") != REVIEW_TYPES:
        errors.append(f"{name}: pull_request_target types must be exactly {REVIEW_TYPES}")
    pr_input = (((on.get("workflow_dispatch") or {}).get("inputs") or {}).get("pr") or {})
    if pr_input.get("required") is not True:
        errors.append(f"{name}: the dispatch input pr must be required")
    if data.get("permissions") != {"contents": "read", "pull-requests": "write", "statuses": "write"}:
        errors.append(f"{name}: permissions must be exactly contents: read, pull-requests: write, statuses: write")
    concurrency = data.get("concurrency") or {}
    if concurrency.get("cancel-in-progress") is not True or "pull_request.number" not in str(concurrency.get("group")):
        errors.append(f"{name}: concurrency must be one review per pull request, cancelling the older run")
    if list(data["jobs"]) != ["review-run"]:
        errors.append(f"{name}: jobs must be exactly [review-run]")
    job = data["jobs"].get("review-run") or {}
    if " ".join(str(job.get("if", "")).split()) != REVIEW_IF:
        errors.append(f"{name}: the job `if` is not the pinned draft/outsider rule")
    steps = job.get("steps") or []
    for step in steps:
        with_ = step.get("with") or {}
        if "actions/checkout@" in str(step.get("uses", "")):
            if "ref" in with_ or "repository" in with_:
                errors.append(f"{name}: the checkout names a ref: it checks out pull-request code")
            if with_.get("persist-credentials") is not False:
                errors.append(f"{name}: the checkout must set persist-credentials: false")
        if PR_TEXT.search(str(step.get("run", ""))):
            errors.append(f"{name}: a run line interpolates pull-request or dispatch text into the shell")
    budget = [(s.get("env") or {}).get("REVIEW_TIMEOUT_MIN") for s in steps if "REVIEW_TIMEOUT_MIN" in (s.get("env") or {})]
    if budget != [job.get("timeout-minutes")] or budget == [None]:
        errors.append(f"{name}: REVIEW_TIMEOUT_MIN must equal the job's timeout-minutes")
    installs = re.findall(r"@anthropic-ai/claude-code(\S*)", text)
    if not installs or not all(re.fullmatch(r"@\d+\.\d+\.\d+", v) for v in installs):
        errors.append(f"{name}: the Claude CLI must be installed at an exact version")
    if "claude --version" not in text:
        errors.append(f"{name}: the Claude CLI install must be proven by running the binary")
    secrets = set(re.findall(r"secrets\.(\w+)", text))
    in_run = any("secrets." in str(s.get("run", "")) for s in steps)
    if secrets != REVIEW_SECRETS or in_run:
        errors.append(f"{name}: secret use must be exactly {sorted(REVIEW_SECRETS)}, in env only")
    return errors


def lint(root):
    errors = []
    for fname, facts in (("gates.yml", gates_facts), ("review.yml", review_facts)):
        try:
            errors += facts(kit.read(os.path.join(root, WORKFLOWS, fname)))
        except OSError as e:
            errors.append(f"{fname}: cannot be read: {e}")
    listed = sorted(os.listdir(os.path.join(root, WORKFLOWS)))
    if listed != ["gates.yml", "review.yml"]:
        errors.append(f"workflows are {listed}: a workflow without facts here is unchecked configuration")
    return errors


# (file, case, anchor that occurs exactly once, replacement, text the error must contain)
MUTATIONS = [
    ("gates.yml", "edits no longer re-run the gates", "ready_for_review, edited]", "ready_for_review]", "pull_request types"),
    ("gates.yml", "a path filter", "    branches: [main]\n", "    branches: [main]\n    paths: ['tools/**']\n", "path filter"),
    ("gates.yml", "the gates job can be skipped", "  gates:\n    runs-on:", "  gates:\n    if: github.actor != 'x'\n    runs-on:",
     "may be skipped"),
    ("gates.yml", "a floating runner image", "runs-on: ubuntu-24.04", "runs-on: ubuntu-latest", "runner image"),
    ("gates.yml", "an action by tag", "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "actions/checkout@v7", "not pinned"),
    ("gates.yml", "pip without hashes", "--quiet --require-hashes -r", "--quiet -r", "--require-hashes"),
    ("gates.yml", "the local flag in CI", "tools/gates.py'", "tools/gates.py --local'", "the gates step"),
    ("gates.yml", "a swallowed failure", "tools/gates.py'", "tools/gates.py || true'", "the gates step"),
    ("gates.yml", "no timeout", "    timeout-minutes: 15\n", "", "no timeout"),
    ("gates.yml", "a write token", "pull-requests: read", "pull-requests: write", "permissions"),
    ("gates.yml", "continue-on-error", "      - name: Run every gate\n", "      - name: Run every gate\n        continue-on-error: true\n",
     "continue-on-error"),
    ("gates.yml", "a step that can be skipped", "      - name: Run every gate\n", "      - name: Run every gate\n        if: always()\n",
     "step that may be skipped"),
    ("gates.yml", "no pull request number", "          PR_NUMBER: ${{ github.event.pull_request.number }}\n", "", "PR_NUMBER"),
    ("gates.yml", "a second job", "\njobs:\n", "\njobs:\n  extra:\n    uses: ./.github/workflows/x.yml\n", "calls another workflow"),
    ("review.yml", "the reviewer runs the pull request's own copy", "  pull_request_target:", "  pull_request:", "trigger must be"),
    ("review.yml", "no review when a Draft goes Ready", "reopened, ready_for_review]", "reopened]", "types must be exactly"),
    ("review.yml", "Drafts are reviewed on every push", "(github.event.pull_request.draft == false &&\n      contains(",
     "(contains(", "draft/outsider rule"),
    ("review.yml", "anyone's pull request spends the seat", '\'["OWNER","MEMBER","COLLABORATOR"]\'',
     '\'["OWNER","MEMBER","COLLABORATOR","NONE"]\'', "draft/outsider rule"),
    ("review.yml", "pull-request code is checked out", "          fetch-depth: 0\n",
     "          fetch-depth: 0\n          ref: ${{ github.event.pull_request.head.sha }}\n", "checks out pull-request code"),
    ("review.yml", "the token stays in the checkout", "persist-credentials: false", "persist-credentials: true", "persist-credentials"),
    ("review.yml", "the budget and the timeout differ", "    timeout-minutes: 90\n", "    timeout-minutes: 60\n", "REVIEW_TIMEOUT_MIN"),
    ("review.yml", "a floating Claude CLI", "claude-code@2.1.283", "claude-code@latest", "exact version"),
    ("review.yml", "an install nobody ran", " && claude --version && exit 0", " && exit 0", "proven by running"),
    ("review.yml", "a wider token", "  statuses: write\n", "  statuses: write\n  actions: write\n", "permissions must be exactly"),
    ("review.yml", "no status permission", "  statuses: write\n", "", "permissions"),
    ("review.yml", "pull-request text in a shell line", '        run: echo "REVIEW_STARTED=$(date +%s)" >> "$GITHUB_ENV"\n',
     '        run: echo "${{ github.event.pull_request.title }}" >> "$GITHUB_ENV"\n', "interpolates"),
    ("review.yml", "parallel reviews of one pull request", "cancel-in-progress: true", "cancel-in-progress: false", "concurrency"),
    ("review.yml", "another secret", "          REVIEW_TIMEOUT_MIN: 90\n",
     "          REVIEW_TIMEOUT_MIN: 90\n          OTHER: ${{ secrets.DEPLOY_KEY }}\n", "secret use"),
    ("review.yml", "an optional pull request number", "        required: true\n", "        required: false\n", "must be required"),
]


def self_test():
    facts = {"gates.yml": gates_facts, "review.yml": review_facts}
    real = {f: kit.read(os.path.join(kit.ROOT, WORKFLOWS, f)) for f in facts}
    cases = [(f"the real {f} is clean", not facts[f](real[f]), facts[f](real[f])) for f in facts]
    for fname, case, old, new, needle in MUTATIONS:
        if real[fname].count(old) != 1:
            cases.append((f"{fname}: {case}", False, f"anchor occurs {real[fname].count(old)} times, expected 1"))
            continue
        errors = facts[fname](real[fname].replace(old, new))
        cases.append((f"{fname}: {case}", any(needle in e for e in errors), errors))
    cases.append(("garbage is not a workflow", any("cannot be read" in e for e in gates_facts("jobs: [")), ""))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    errors = lint(kit.ROOT)
    for e in errors:
        print("ERROR:", e)
    print(f"ci lint: 2 workflows, {len(errors)} errors")
    sys.exit(1 if errors else 0)
