"""Shared by every tool here: process calls that never read a failure as a pass, the GitHub CLI
wrapper, the patterns for strings that must not be in a public tree, and the self-test reporter."""
import json
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# label -> pattern; an error names the label, never the value it matched.
SECRETS = {
    "gitlab-token": r"glpat-[A-Za-z0-9_\-]{10,}",
    "github-token": r"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{22,})",
    # anchored on the left: kebab names such as "task-tracking-..." contain "sk-"
    "api-key": r"(?<![A-Za-z0-9])sk-[A-Za-z0-9_\-]{20,}",
    "aws-key": r"AKIA[A-Z0-9]{12,}",
    "private-key": r"BEGIN [A-Z ]*PRIVATE KEY",
    "bearer": r"Bearer [A-Za-z0-9_\-\.]{25,}",
}
# Facts bound to one machine or one person. The repository is public, so none of them belongs in it.
MACHINE = {
    "drive-path": r"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?![\\/])",
    "profile-path": r"(?<![A-Za-z0-9])/(?:[a-z]/)?Users/[A-Za-z0-9]",
    # four dotted numbers that are not loopback; write a four-part version as v1.0.0.0
    "ip-address": r"(?<![\w.])(?!127\.)(?!0\.0\.0\.0(?!\d))(?:\d{1,3}\.){3}\d{1,3}(?![\w.])",
    # not the documentation domains, not the two public no-reply addresses commit trailers carry,
    # and not the user part of an SSH remote
    "email": r"(?<![A-Za-z0-9._%+-])(?!noreply@anthropic\.com\b)(?!git@github\.com:)[A-Za-z0-9._%+-]+"
             r"@(?!example\.(?:com|org|net)\b)(?!users\.noreply\.github\.com\b)(?:[A-Za-z0-9-]+\.)+[A-Za-z]{2,}",
}
CONFLICT = re.compile(r"^(<<<<<<< |>>>>>>> )", re.M)
# Exit code of a gate that found nothing wrong in what it could read and named what it could not
# read. The runner shows it as PARTLY: not red, and never counted as a pass.
PARTLY = 5


class Refused(Exception):
    """A command the tool depends on failed. The caller treats it as a refusal, never as a pass."""


def run(cmd, cwd=None, stdin=None, timeout=120):
    """stdout of a command that exited 0. Anything else raises Refused."""
    try:
        p = subprocess.run(cmd, cwd=cwd or ROOT, input=stdin, capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as e:
        raise Refused(f"{cmd[0]}: {type(e).__name__}: {e}") from None
    if p.returncode != 0:
        raise Refused(f"`{' '.join(cmd[:5])}` exited {p.returncode}: {(p.stderr or p.stdout).strip()[:400]}")
    return p.stdout


def gh_json(*args, stdin=None):
    """One GitHub API answer as parsed JSON, through the gh CLI (its login locally, GH_TOKEN in CI)."""
    out = run(["gh", "api", *args], stdin=stdin)
    try:
        return json.loads(out)
    except ValueError:
        raise Refused(f"gh api {args[0]}: answer is not JSON") from None


def gh_pages(path):
    """Every item of a paginated list endpoint."""
    pages = gh_json("--paginate", "--slurp", path)
    return [item for page in pages for item in page]


def repo():
    """owner/name of the repository the tool runs for."""
    name = os.environ.get("GITHUB_REPOSITORY") or run(
        ["gh", "repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"]).strip()
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", name):
        raise Refused(f"cannot tell the repository: {name!r}")
    return name


def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n")


def report(cases):
    """Print one line per self-test case and return the exit code.
    cases: [(name, passed, detail)]. No case at all fails: a self-test that tested nothing is not a pass."""
    failed = 0
    for name, passed, detail in cases:
        print("ok  " if passed else "FAIL", name, "" if passed else detail)
        failed += not passed
    if not cases:
        print("FAIL no self-test cases ran")
        failed = 1
    print(f"{len(cases)} cases, {failed} failed")
    return 1 if failed else 0
