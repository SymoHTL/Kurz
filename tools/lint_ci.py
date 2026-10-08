#!/usr/bin/env python3
"""Unit facts of the two workflows. A one-token YAML fact that nothing compiles gets a check here:
- both: actions pinned by commit SHA, the hosted runner named by its versioned label (the forge
  offers no digest for a hosted image), a timeout on every job, pip with
  --require-hashes, no job or step that may fail quietly (`continue-on-error`), no step-level `if`,
  no reusable-workflow job, no `defaults` and no step with a shell or a working directory of its
  own, no expression inside a `run:` value (a value reaches a script through `env:`), a secret only
  as plain `secrets.<NAME>` in a step's `env:`, no key twice in one mapping;
- gates.yml: runs on every push to a pull request, on every edit of its title or description, and
  on pushes to main, with no path or branch filter; exactly one job, `gates`, with no `if` (a
  skipped job reports success to a required check); read-only token; no secret; the last step runs
  the full gate runner and carries nothing but its name, env and run.
  Not covered by any trigger: a review that posts its findings and a thread that is resolved start
  no run, so the `gates` check of a head is the verdict of the moment it ran (tools/pr_gates.py);
- review.yml: pull_request_target plus a manual dispatch; the job's `if` is the pinned rule: a
  dispatch on the default branch, or a pull request into the default branch that is no Draft,
  whose Ready event is not that of a head labelled `reviewed-<its sha>` (its review ran off the
  pipeline; every other head is reviewed by the run its event starts) and that comes from someone
  who may write here; the checkout names the default branch and never
  pull-request code; one review per pull request at a time, held on the job, so a run whose job is
  skipped cancels nothing; the job timeout equals the budget the script is told; the Claude CLI is
  pinned to an exact version and proven by running it; the one credential secret is read; the
  last step runs the reviewer and carries nothing but its name, env and run.
  A copy of review.yml on another branch can drop every one of these, and a dispatch on that branch
  runs the copy with this repository's token (HAZARD #11); the lint of that branch is its own copy.
`--self-test` breaks each fact in the real files, one token at a time, and expects its error."""
import os
import re
import shutil
import sys
import tempfile

import yaml

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

WORKFLOWS = ".github/workflows"
FILES = ["gates.yml", "review.yml"]
PR_TYPES = {"opened", "synchronize", "reopened", "ready_for_review", "edited"}
REVIEW_TYPES = ["opened", "synchronize", "reopened", "ready_for_review"]
REVIEW_IF = ("(github.event_name == 'workflow_dispatch' && "
             "github.ref == format('refs/heads/{0}', github.event.repository.default_branch)) || "
             "(github.event_name == 'pull_request_target' && "
             "github.event.pull_request.base.ref == github.event.repository.default_branch && "
             "github.event.pull_request.draft == false && "
             "!(github.event.action == 'ready_for_review' && contains(github.event.pull_request.labels.*.name, format('reviewed-{0}', github.event.pull_request.head.sha))) && "
             "contains(fromJSON('[\"OWNER\",\"MEMBER\",\"COLLABORATOR\"]'), "
             "github.event.pull_request.author_association))")
REVIEW_GROUP = "review-${{ github.event.pull_request.number || inputs.pr }}"
REVIEW_REF = "${{ github.event.repository.default_branch }}"
REVIEW_PR = "${{ github.event.pull_request.number || inputs.pr }}"
REVIEW_SECRETS = {"CLAUDE_CODE_OAUTH_TOKEN"}
STEP_KEYS = {"name", "env", "run"}  # what the step that decides may carry
START_LINE = 'echo "REVIEW_STARTED=$(date +%s)" >> "$GITHUB_ENV"'  # the one write to GITHUB_ENV


def parse(text):
    data = kit.load_yaml(text)
    if not isinstance(data, dict) or not isinstance(data.get("jobs"), dict):
        raise ValueError("no jobs")
    on = data.get("on", data.get(True))  # YAML 1.1 reads the bare key `on` as a boolean
    return data, on if isinstance(on, dict) else {}


def unreadable(name, e):
    return [f"{name}: cannot be read as a workflow: {type(e).__name__}: {(str(e).splitlines() or [''])[0]}"]


def strings(node, path=()):
    """(the keys that lead to it, the text) of every string in parsed YAML; an item of a list is
    named by its index."""
    if isinstance(node, dict):
        for key, value in node.items():
            yield from strings(value, path + (str(key),))
    elif isinstance(node, list):
        for n, value in enumerate(node):
            yield from strings(value, path + (str(n),))
    elif isinstance(node, str):
        yield path, node


def secret_errors(name, data, allowed):
    """A secret may only be read as `${{ secrets.<NAME> }}`, alone, as the value of a variable in the
    `env:` of a job's last step, the one that decides, and only the names in `allowed`, each of which
    must be read. A step before it, an install among them, never holds the credential."""
    errors, read = [], set()
    for path, value in strings(data):
        # the whole value, not what a non-greedy match of `}}` leaves: a literal `}}` inside the expression
        # hides nothing, and the forge resolves context names case-insensitively
        if not ((path and path[-1] == "if") or "${{" in value) or not re.search(r"\bsecrets\b", value, re.I):
            continue
        plain = re.fullmatch(r"\$\{\{\s*secrets\.(\w+)\s*\}\}", value)
        steps = ((data.get("jobs") or {}).get(path[1]) or {}).get("steps") if len(path) == 6 else None
        last = isinstance(steps, list) and path[3] == str(len(steps) - 1)
        in_env = len(path) == 6 and path[0] == "jobs" and path[2] == "steps" and last and path[4] == "env"
        if plain and in_env and plain.group(1) in allowed:
            read.add(plain.group(1))
        else:
            errors.append(f"{name}: a secret is read at {'.'.join(path)}: only {sorted(allowed) or 'no secret'} may be read, "
                          f"as plain `secrets.<NAME>` in the env of the job's last step")
    if read != set(allowed):
        errors.append(f"{name}: the secrets read must be exactly {sorted(allowed)}, found {sorted(read)}")
    return errors


def common(name, text, data, secrets):
    errors = []
    # the parsed steps, not the text: a flow-style step or a quoted key is a step too
    for used in [str(s["uses"]) for job in data["jobs"].values() for s in (job.get("steps") or []) if "uses" in s]:
        if not re.fullmatch(r"[\w.-]+/[\w./-]+@[0-9a-f]{40}", used):
            errors.append(f"{name}: action not pinned by commit SHA: {used}")
    for line in text.splitlines():
        # every spelling of an install: pip, pip3, pip3.12, pipx, python -m pip, with options before `install`
        if re.search(r"\bpip(?:3(?:\.\d+)?|x)?\"?(?:\s+-\S+)*\s+install\b", line) and "--require-hashes" not in line:
            errors.append(f"{name}: pip install without --require-hashes")
    if "defaults" in data:
        errors.append(f"{name}: the workflow sets `defaults`: a shell nobody pinned can turn every step into a no-op")
    if "env" in data:
        errors.append(f"{name}: the workflow sets `env`: a variable the deciding step inherits from there is read by no fact")
    for jid, job in data["jobs"].items():
        steps = job.get("steps") or []
        if "uses" in job:
            errors.append(f"{name}: job {jid} calls another workflow; a waiver could not tell what it covers")
        if "permissions" in job:
            errors.append(f"{name}: job {jid} sets its own permissions, which replace the workflow's grant that the facts read")
        if "env" in job:
            errors.append(f"{name}: job {jid} sets `env`: a variable the deciding step inherits from there is read by no fact")
        for run in (str(s.get("run", "")) for s in steps):
            # the shell of a later step sources what an earlier one wrote there: only the pinned start line may
            if re.search(r"GITHUB_(?:ENV|PATH)", run) and run.strip() != START_LINE:
                errors.append(f"{name}: job {jid} has a step that writes GITHUB_ENV or GITHUB_PATH: only the start-time line may")
            if re.search(r"\b(?:git\s+(?:checkout|fetch|pull|switch)|gh\s+pr\s+checkout)\b", run):
                errors.append(f"{name}: job {jid} has a step that checks out or fetches by hand: that can be pull-request code")
        if "container" in job or "services" in job:
            errors.append(f"{name}: job {jid} runs in a container or beside services: their images are pinned by no fact")
        if job.get("runs-on") != "ubuntu-24.04":
            errors.append(f"{name}: job {jid} runner image is not pinned to ubuntu-24.04")
        if "timeout-minutes" not in job:
            errors.append(f"{name}: job {jid} has no timeout")
        if "continue-on-error" in job or any("continue-on-error" in s for s in steps):
            errors.append(f"{name}: job {jid} uses continue-on-error")
        if any("if" in s for s in steps):
            errors.append(f"{name}: job {jid} has a step that may be skipped (`if`)")
        if "defaults" in job:
            errors.append(f"{name}: job {jid} sets `defaults`: a shell nobody pinned can turn every step into a no-op")
        if any("shell" in s or "working-directory" in s for s in steps):
            errors.append(f"{name}: job {jid} has a step with its own shell or working directory")
        if any("${{" in str(s.get("run", "")) for s in steps):
            errors.append(f"{name}: job {jid} has a run line that interpolates an expression into the shell: pass the value through env")
    return errors + secret_errors(name, data, secrets)


def deciding_step(name, steps, command, what, env_keys):
    """The last step runs `command` and nothing else decides how: only name, env and run, and the
    env carries exactly `env_keys` (a BASH_ENV or a PYTHONSTARTUP there would run before it)."""
    last = steps[-1] if steps else {}
    errors = []
    if str(last.get("run", "")).strip() != command:
        errors.append(f"{name}: the last step must be the {what} step, running exactly `{command}`")
    if set(last) - STEP_KEYS:
        errors.append(f"{name}: the {what} step may carry only name, env and run, found {sorted(set(last) - STEP_KEYS)}")
    if set(last.get("env") or {}) != env_keys:
        errors.append(f"{name}: the {what} step's env must be exactly {sorted(env_keys)}, found {sorted(last.get('env') or {})}")
    return errors, last


def gates_facts(text):
    name = "gates.yml"
    try:
        data, on = parse(text)
    except (yaml.YAMLError, ValueError) as e:
        return unreadable(name, e)
    errors = common(name, text, data, set())
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
    step, last = deciding_step(name, job.get("steps") or [], '"$RUNNER_TEMP/venv/bin/python" tools/gates.py', "gates", {"GH_TOKEN", "PR_NUMBER"})
    errors += step
    if (last.get("env") or {}).get("PR_NUMBER") != "${{ github.event.pull_request.number }}":
        errors.append(f"{name}: the gates step needs PR_NUMBER from the pull request event")
    return errors


def review_facts(text):
    name = "review.yml"
    try:
        data, on = parse(text)
    except (yaml.YAMLError, ValueError) as e:
        return unreadable(name, e)
    errors = common(name, text, data, REVIEW_SECRETS)
    if set(on) != {"pull_request_target", "workflow_dispatch"}:
        errors.append(f"{name}: trigger must be pull_request_target plus workflow_dispatch, found {sorted(map(str, on))}")
    if (on.get("pull_request_target") or {}).get("types") != REVIEW_TYPES:
        errors.append(f"{name}: pull_request_target types must be exactly {REVIEW_TYPES}")
    pr_input = (((on.get("workflow_dispatch") or {}).get("inputs") or {}).get("pr") or {})
    if pr_input.get("required") is not True:
        errors.append(f"{name}: the dispatch input pr must be required")
    if data.get("permissions") != {"contents": "read", "issues": "write", "pull-requests": "write", "statuses": "write"}:
        errors.append(f"{name}: permissions must be exactly contents: read, issues: write, pull-requests: write, statuses: write")
    if list(data["jobs"]) != ["review-run"]:
        errors.append(f"{name}: jobs must be exactly [review-run]")
    job = data["jobs"].get("review-run") or {}
    if "concurrency" in data:
        errors.append(f"{name}: concurrency belongs on the job: at workflow level a run whose job is skipped cancels a running review")
    if job.get("concurrency") != {"group": REVIEW_GROUP, "cancel-in-progress": True}:
        errors.append(f"{name}: the job's concurrency must be one review per pull request, dispatched or not, cancelling the older run")
    if " ".join(str(job.get("if", "")).split()) != REVIEW_IF:
        errors.append(f"{name}: the job `if` is not the pinned rule (default branch only, no Draft, no outsider, no Ready of a head labelled reviewed-<sha>)")
    steps = job.get("steps") or []
    checkouts = [s.get("with") or {} for s in steps if "actions/checkout@" in str(s.get("uses", ""))]
    for with_ in checkouts:
        if with_.get("ref") != REVIEW_REF or "repository" in with_:
            errors.append(f"{name}: the checkout must name the default branch and no other repository: anything else can be pull-request code")
        if with_.get("persist-credentials") is not False:
            errors.append(f"{name}: the checkout must set persist-credentials: false")
    if len(checkouts) != 1:
        errors.append(f"{name}: expected exactly one checkout, found {len(checkouts)}")
    step, last = deciding_step(name, steps, '"$RUNNER_TEMP/venv/bin/python" tools/review/review.py', "review",
                               {"GH_TOKEN", "PR_NUMBER", "REVIEW_TIMEOUT_MIN", "CLAUDE_CODE_OAUTH_TOKEN"})
    errors += step
    if (last.get("env") or {}).get("PR_NUMBER") != REVIEW_PR:
        errors.append(f"{name}: the review step needs PR_NUMBER from the pull request event or the dispatch input")
    # the budget the script is told is the review step's own, and the job's timeout is that number
    if (last.get("env") or {}).get("REVIEW_TIMEOUT_MIN") != job.get("timeout-minutes") or job.get("timeout-minutes") is None:
        errors.append(f"{name}: REVIEW_TIMEOUT_MIN on the review step must equal the job's timeout-minutes")
    if not steps or str(steps[0].get("run", "")).strip() != START_LINE:
        errors.append(f"{name}: the first step must note the start time, exactly `{START_LINE}`: the deadline counts from it")
    installs = re.findall(r"@anthropic-ai/claude-code(\S*)", text)
    if not installs or not all(re.fullmatch(r"@\d+\.\d+\.\d+;?", v) for v in installs):
        errors.append(f"{name}: the Claude CLI must be installed at an exact version")
    if not any(re.search(r"^\s*claude --version\s*$", str(s.get("run", "")), re.M) for s in steps):
        errors.append(f"{name}: the Claude CLI install must be proven by running the binary, on a line of its own")
    return errors


def lint(root):
    """(errors, how many workflow files were read)."""
    errors, read = [], 0
    try:
        listed = sorted(os.listdir(os.path.join(root, WORKFLOWS)))
    except OSError as e:
        return [f"{WORKFLOWS} cannot be listed: {type(e).__name__}"], 0
    for fname, facts in (("gates.yml", gates_facts), ("review.yml", review_facts)):
        try:
            text = kit.read(os.path.join(root, WORKFLOWS, fname))
        except OSError as e:
            errors.append(f"{fname}: cannot be read: {type(e).__name__}")
            continue
        read += 1
        errors += facts(text)
    if listed != FILES:
        errors.append(f"workflows are {listed}: a workflow without facts here is unchecked configuration")
    return errors, read


PLAIN_JOB = "  extra:\n    runs-on: ubuntu-24.04\n    timeout-minutes: 5\n    steps:\n      - run: echo ok\n"
START = '        run: echo "REVIEW_STARTED=$(date +%s)" >> "$GITHUB_ENV"\n'
SECRET = "          CLAUDE_CODE_OAUTH_TOKEN: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}\n"
# (file, case, anchor that occurs exactly once, replacement, text the error must contain)
MUTATIONS = [
    ("gates.yml", "edits no longer re-run the gates", "ready_for_review, edited]", "ready_for_review]", "pull_request types"),
    ("gates.yml", "a path filter", "    branches: [main]\n", "    branches: [main]\n    paths: ['tools/**']\n", "path filter"),
    ("gates.yml", "another trigger", "    branches: [main]\n", "    branches: [main]\n  workflow_dispatch:\n", "triggers are"),
    ("gates.yml", "pushes to another branch count as main", "    branches: [main]\n", "    branches: [main, next]\n", "push must be limited"),
    ("gates.yml", "the gates job can be skipped", "  gates:\n    runs-on:", "  gates:\n    if: github.actor != 'x'\n    runs-on:",
     "may be skipped"),
    ("gates.yml", "a floating runner image", "runs-on: ubuntu-24.04", "runs-on: ubuntu-latest", "runner image"),
    ("gates.yml", "an action by tag", "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "actions/checkout@v7", "not pinned"),
    ("gates.yml", "a flow-style step whose action is not pinned", "      - name: Install pinned dependencies\n",
     "      - {uses: actions/setup-node@v4}\n      - name: Install pinned dependencies\n", "not pinned"),
    ("gates.yml", "pip without hashes", "--quiet --require-hashes -r", "--quiet -r", "--require-hashes"),
    ("gates.yml", "pip3 without hashes", '          python3 -m venv "$RUNNER_TEMP/venv"\n',
     '          python3 -m venv "$RUNNER_TEMP/venv"\n          pip3 install pyyaml\n', "--require-hashes"),
    ("gates.yml", "pip with an option before install, without hashes", '          python3 -m venv "$RUNNER_TEMP/venv"\n',
     '          python3 -m venv "$RUNNER_TEMP/venv"\n          pip -q install pyyaml\n', "--require-hashes"),
    ("gates.yml", "pipx without hashes", '          python3 -m venv "$RUNNER_TEMP/venv"\n',
     '          python3 -m venv "$RUNNER_TEMP/venv"\n          pipx install pyyaml\n', "--require-hashes"),
    ("gates.yml", "a job-level permissions grant", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    permissions: write-all\n",
     "sets its own permissions"),
    ("gates.yml", "a job-level env", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    env:\n      BASH_ENV: x\n", "sets `env`"),
    ("gates.yml", "a workflow-level env", "\njobs:\n", "\nenv:\n  BASH_ENV: x\n\njobs:\n", "the workflow sets `env`"),
    ("gates.yml", "a shell file sourced before the gates step", "          GH_TOKEN: ${{ github.token }}\n",
     "          GH_TOKEN: ${{ github.token }}\n          BASH_ENV: x\n", "env must be exactly"),
    ("gates.yml", "a step that writes GITHUB_ENV", '          python3 -m venv "$RUNNER_TEMP/venv"\n',
     '          python3 -m venv "$RUNNER_TEMP/venv"\n          echo "BASH_ENV=x" >> "$GITHUB_ENV"\n', "writes GITHUB_ENV"),
    ("gates.yml", "a secret read as Secrets", "          GH_TOKEN: ${{ github.token }}\n",
     "          GH_TOKEN: ${{ github.token }}\n          KEY: ${{ Secrets.DEPLOY_KEY }}\n", "a secret is read"),
    ("gates.yml", "a secret behind a literal }}", "          GH_TOKEN: ${{ github.token }}\n",
     "          GH_TOKEN: ${{ github.token }}\n          KEY: \"${{ format('}}', secrets.DEPLOY_KEY) }}\"\n", "a secret is read"),
    ("gates.yml", "the local flag in CI", "tools/gates.py'", "tools/gates.py --local'", "must be the gates step"),
    ("gates.yml", "a swallowed failure", "tools/gates.py'", "tools/gates.py || true'", "must be the gates step"),
    ("gates.yml", "a shell that runs nothing", "      - name: Run every gate\n", "      - name: Run every gate\n        shell: true {0}\n",
     "own shell or working directory"),
    ("gates.yml", "the gates step runs somewhere else", "      - name: Run every gate\n",
     "      - name: Run every gate\n        working-directory: /tmp\n", "may carry only name, env and run"),
    ("gates.yml", "a default shell for every step", "\njobs:\n", "\ndefaults:\n  run:\n    shell: true {0}\n\njobs:\n", "the workflow sets `defaults`"),
    ("gates.yml", "a default shell for the job", "  gates:\n    runs-on:", "  gates:\n    defaults:\n      run:\n        shell: true {0}\n    runs-on:",
     "job gates sets `defaults`"),
    ("gates.yml", "no timeout", "    timeout-minutes: 15\n", "", "no timeout"),
    ("gates.yml", "a write token", "pull-requests: read", "pull-requests: write", "permissions"),
    ("gates.yml", "continue-on-error", "      - name: Run every gate\n", "      - name: Run every gate\n        continue-on-error: true\n",
     "uses continue-on-error"),
    ("gates.yml", "a job that may fail quietly", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    continue-on-error: true\n",
     "uses continue-on-error"),
    ("gates.yml", "a step that can be skipped", "      - name: Run every gate\n", "      - name: Run every gate\n        if: always()\n",
     "step that may be skipped"),
    ("gates.yml", "no pull request number", "          PR_NUMBER: ${{ github.event.pull_request.number }}\n", "", "needs PR_NUMBER"),
    ("gates.yml", "a second job that calls a workflow", "\njobs:\n", "\njobs:\n  extra:\n    uses: ./.github/workflows/x.yml\n", "calls another workflow"),
    ("gates.yml", "a second job of its own", "\njobs:\n", "\njobs:\n" + PLAIN_JOB, "jobs must be exactly [gates]"),
    ("gates.yml", "the branch name in a shell line", '          python3 -m venv "$RUNNER_TEMP/venv"\n',
     '          python3 -m venv "${{ github.head_ref }}"\n', "interpolates"),
    ("gates.yml", "a secret in the gates job", "          GH_TOKEN: ${{ github.token }}\n",
     "          GH_TOKEN: ${{ github.token }}\n          KEY: ${{ secrets.DEPLOY_KEY }}\n", "a secret is read"),
    ("review.yml", "the reviewer runs the pull request's own copy", "  pull_request_target:", "  pull_request:", "trigger must be"),
    ("review.yml", "no review when a Draft goes Ready", "reopened, ready_for_review]", "reopened]", "types must be exactly"),
    ("review.yml", "Drafts are reviewed on every push", "      github.event.pull_request.draft == false &&\n", "", "pinned rule"),
    ("review.yml", "a pull request reviewed off the pipeline is reviewed again on Ready",
     "      !(github.event.action == 'ready_for_review' && contains(github.event.pull_request.labels.*.name, format('reviewed-{0}', github.event.pull_request.head.sha))) &&\n",
     "", "pinned rule"),
    ("review.yml", "a label not tied to the head skips Ready for every head",
     "      !(github.event.action == 'ready_for_review' && contains(github.event.pull_request.labels.*.name, format('reviewed-{0}', github.event.pull_request.head.sha))) &&\n",
     "      !(github.event.action == 'ready_for_review' && contains(github.event.pull_request.labels.*.name, 'reviewed-off-pipeline')) &&\n", "pinned rule"),
    ("review.yml", "anyone's pull request spends the seat", '\'["OWNER","MEMBER","COLLABORATOR"]\'',
     '\'["OWNER","MEMBER","COLLABORATOR","NONE"]\'', "pinned rule"),
    ("review.yml", "a dispatch on any branch runs that branch's reviewer",
     "      (github.event_name == 'workflow_dispatch' &&\n      github.ref == format('refs/heads/{0}', github.event.repository.default_branch)) ||\n",
     "      github.event_name == 'workflow_dispatch' ||\n", "pinned rule"),
    ("review.yml", "a pull request into another branch is reviewed, and its status stays on the head",
     "      github.event.pull_request.base.ref == github.event.repository.default_branch &&\n", "", "pinned rule"),
    ("review.yml", "pull-request code is checked out", "          ref: ${{ github.event.repository.default_branch }}\n",
     "          ref: ${{ github.event.pull_request.head.sha }}\n", "must name the default branch"),
    ("review.yml", "the checkout takes whatever the event points at", "          ref: ${{ github.event.repository.default_branch }}\n", "",
     "must name the default branch"),
    ("review.yml", "another repository is checked out", "          fetch-depth: 0\n", "          fetch-depth: 0\n          repository: someone/else\n",
     "must name the default branch"),
    ("review.yml", "the token stays in the checkout", "persist-credentials: false", "persist-credentials: true", "persist-credentials"),
    ("review.yml", "the budget and the timeout differ", "    timeout-minutes: 90\n", "    timeout-minutes: 60\n", "REVIEW_TIMEOUT_MIN"),
    ("review.yml", "the budget on another step than the review step", "          REVIEW_TIMEOUT_MIN: 90\n", "", "REVIEW_TIMEOUT_MIN on the review step"),
    ("review.yml", "no start time to count the deadline from", START, "        run: echo started\n", "must note the start time"),
    ("review.yml", "a checkout by hand", START, START + '      - run: git fetch origin "$HEAD" && git checkout FETCH_HEAD\n',
     "checks out or fetches by hand"),
    ("review.yml", "a pull request checked out through gh", START, START + '      - run: gh pr checkout "$PR_NUMBER"\n',
     "checks out or fetches by hand"),
    ("review.yml", "a floating Claude CLI", "claude-code@2.1.283", "claude-code@latest", "exact version"),
    ("review.yml", "an install nobody ran", "          claude --version\n", "", "proven by running"),
    ("review.yml", "a wider token", "  statuses: write\n", "  statuses: write\n  actions: write\n", "permissions must be exactly"),
    ("review.yml", "no status permission", "  statuses: write\n", "", "permissions"),
    ("review.yml", "no permission to post on the issue that collects lows", "  issues: write  # low findings are collected on an issue\n", "",
     "permissions"),
    ("review.yml", "pull-request text in a shell line", START,
     '        run: echo "${{ github.event.pull_request.title }}" >> "$GITHUB_ENV"\n', "interpolates"),
    ("review.yml", "the dispatch input in a shell line", START, '        run: echo "${{ inputs.pr }}" >> "$GITHUB_ENV"\n', "interpolates"),
    ("review.yml", "the whole event in a shell line", START, '        run: echo "${{ toJSON(github.event) }}" >> "$GITHUB_ENV"\n', "interpolates"),
    ("review.yml", "an env value in a shell line", START, '        run: echo "${{ env.TITLE }}" >> "$GITHUB_ENV"\n', "interpolates"),
    ("review.yml", "a push lets the review of a superseded head run to its end", "cancel-in-progress: true", "cancel-in-progress: false",
     "job's concurrency"),
    ("review.yml", "dispatched reviews share one group", "      group: review-${{ github.event.pull_request.number || inputs.pr }}\n",
     "      group: review-${{ github.event.pull_request.number }}\n", "job's concurrency"),
    ("review.yml", "a skipped run cancels a running review", "\njobs:\n",
     "\nconcurrency:\n  group: review-${{ github.event.pull_request.number || inputs.pr }}\n  cancel-in-progress: true\n\njobs:\n",
     "belongs on the job"),
    ("review.yml", "another secret", SECRET, SECRET + "          OTHER: ${{ secrets.DEPLOY_KEY }}\n", "a secret is read"),
    ("review.yml", "every secret at once", SECRET, SECRET + "          EVERY: ${{ toJSON(secrets) }}\n", "a secret is read"),
    ("review.yml", "a secret by index", SECRET, SECRET + "          OTHER: ${{ secrets['CLAUDE_CODE_OAUTH_TOKEN'] }}\n", "a secret is read"),
    ("review.yml", "a secret handed to an action", "          fetch-depth: 0\n",
     "          fetch-depth: 0\n          token: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}\n", "a secret is read"),
    ("review.yml", "the credential in a shell line", START, '        run: echo "${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}"\n', "a secret is read"),
    ("review.yml", "no credential at all", SECRET, "", "secrets read must be exactly"),
    ("review.yml", "the credential on the step that installs the CLI", "      - name: Install the Claude CLI, proven by running it\n",
     "      - name: Install the Claude CLI, proven by running it\n        env:\n"
     "          CLAUDE_CODE_OAUTH_TOKEN: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}\n", "a secret is read"),
    ("gates.yml", "a job in a container", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    container: python:3\n",
     "container or beside services"),
    ("review.yml", "a service beside the review", "    timeout-minutes: 90\n",
     "    timeout-minutes: 90\n    services:\n      cache:\n        image: redis\n", "container or beside services"),
    ("review.yml", "an optional pull request number", "        required: true\n", "        required: false\n", "must be required"),
    ("review.yml", "the review step runs with a flag", "tools/review/review.py'", "tools/review/review.py --local'", "must be the review step"),
    ("review.yml", "the review step does not know its pull request", "          PR_NUMBER: ${{ github.event.pull_request.number || inputs.pr }}\n", "",
     "the review step needs PR_NUMBER"),
    ("review.yml", "a second job of its own", "\njobs:\n", "\njobs:\n" + PLAIN_JOB, "jobs must be exactly [review-run]"),
    ("review.yml", "a second checkout", "      - name: Install pinned dependencies\n",
     "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1\n        with:\n"
     "          ref: ${{ github.event.repository.default_branch }}\n          persist-credentials: false\n"
     "      - name: Install pinned dependencies\n", "exactly one checkout"),
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
    cases.append(("YAML without jobs is not a workflow", any("cannot be read" in e for e in gates_facts("on: push\n")), gates_facts("on: push\n")))
    twice = review_facts(real["review.yml"].replace("  statuses: write\n", "  statuses: write\n  contents: write\n"))
    cases.append(("a key that occurs twice is refused, not read as its last value", any("occurs twice" in e for e in twice), twice))

    def tree(files):
        root = tempfile.mkdtemp()
        os.makedirs(os.path.join(root, WORKFLOWS))
        for fname in files:
            with open(os.path.join(root, WORKFLOWS, fname), "w", encoding="utf-8", newline="\n") as f:
                f.write(real.get(fname, real["gates.yml"]))
        return root

    for name, root, needle in [("lint: the two real workflows are clean", tree(FILES), None),
                               ("lint: a third workflow is unchecked configuration", tree(FILES + ["extra.yml"]), "without facts"),
                               ("lint: a missing workflow is an error", tree(["gates.yml"]), "review.yml: cannot be read"),
                               ("lint: no workflows directory is an error", tempfile.mkdtemp(), "cannot be listed")]:
        errors, read = lint(root)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))
        if needle and "review.yml" in needle:
            cases.append(("lint: the count is of the workflows read, not of the ones expected", read == 1, read))
        shutil.rmtree(root, ignore_errors=True)
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    errors, read = lint(kit.ROOT)
    for e in errors:
        print("ERROR:", e)
    print(f"ci lint: {read} workflows read, {len(errors)} errors")
    sys.exit(1 if errors else 0)
