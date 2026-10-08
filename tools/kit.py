"""Shared by every tool here: process calls that never read a failure as a pass, the GitHub CLI
wrapper, the patterns for strings that must not be in a public tree, and the self-test reporter.
`--self-test` covers the calls and the reporter; the patterns have their cases in tools/tree_gate.py."""
import contextlib
import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# label -> pattern; an error names the label, never the value it matched.
SECRETS = {
    "gitlab-token": r"glpat-[A-Za-z0-9_\-]{10,}",
    "github-token": r"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{22,})",
    # anchored on the left: kebab names such as "task-tracking-..." contain "sk-". The letter of a
    # \n, \t or \r escape is no anchor: in JSON and in string literals a value begins a line after one.
    "api-key": r"(?:(?<![A-Za-z0-9])|(?<=\\[ntr]))sk-[A-Za-z0-9_\-]{20,}",
    "aws-key": r"AKIA[A-Z0-9]{12,}",
    "private-key": r"BEGIN [A-Z ]*PRIVATE KEY",
    "bearer": r"Bearer [A-Za-z0-9_\-\.]{25,}",
}
# Facts bound to one machine or one person. The repository is public, so none of them belongs in it.
MACHINE = {
    # a drive letter and its separator, also the doubled backslash of JSON and of string literals;
    # `x://` is left alone, it starts a URL
    "drive-path": r"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?!/)",
    # a user's directory: Windows seen from a POSIX shell, from WSL or from Cygwin, macOS, Linux, root's.
    # /home/runner is the hosted CI runner's and names nobody.
    "profile-path": r"(?:(?<![A-Za-z0-9])|(?<=\\[ntr]))(?:(?:/(?:mnt|cygdrive))?/(?:[a-z]/)?Users/|/home/(?!runner\b)|/root/)[A-Za-z0-9]",
    # four dotted numbers that are not loopback, also at the end of a sentence; write a four-part
    # version as v1.0.0.0
    "ip-address": r"(?:(?<![\w.])|(?<=\\[ntr]))(?!127\.)(?!0\.0\.0\.0(?!\d))(?:\d{1,3}\.){3}\d{1,3}(?!\w|\.\d)",
    # the private ranges of IPv6: unique local and link-local addresses
    "ip6-address": r"(?i:(?<![\w:])(?:f[cd][0-9a-f]{2}|fe[89ab][0-9a-f]):[0-9a-f]{0,4}:)",
    # not the documentation domains, not the public no-reply address of Claude's commit trailers,
    # and not the user part of an SSH remote. A GitHub no-reply address names an account: it passes
    # only in a commit's trailer line (message_text below)
    "email": r"(?<![A-Za-z0-9._%+-])(?!noreply@anthropic\.com\b)(?!git@github\.com:)[A-Za-z0-9._%+-]+"
             r"@(?!example\.(?:com|org|net)\b)(?:[A-Za-z0-9-]+\.)+[A-Za-z]{2,}",
}
CONFLICT = re.compile(r"^(<<<<<<< |>>>>>>> )", re.M)
# A trailer line that names a co-author or a signer by the GitHub no-reply address of an account. The
# forge writes such lines into a squash, and the line publishes no more than the author line of the
# same commit does, which no gate reads (HAZARD #12). Anywhere else the address is machine-bound.
NOREPLY_TRAILER = re.compile(r"(?im)^(?:co-authored-by|signed-off-by):[^<\n]*<[A-Za-z0-9+._-]+@users\.noreply\.github\.com>[ \t]*$")


def message_text(message):
    """A commit message as the patterns read it: without the trailer lines of NOREPLY_TRAILER."""
    return NOREPLY_TRAILER.sub("", message)
# What makes the forge start no workflow for a commit. On main that is a commit nothing gated.
SKIP_LITERALS = [r"\[skip ci\]", r"\[ci skip\]", r"\[no ci\]", r"\[skip actions\]", r"\[actions skip\]",
                 r"^skip-checks:[ \t]*true[ \t]*$"]
# Exit code of a gate that found nothing wrong in what it could read and named what it could not
# read. The runner shows it as PARTLY, never as a pass, and turns red unless the gate is listed with
# the issue that records the unread part (PARTLY_OK in tools/gates.py).
PARTLY = 5


class Refused(Exception):
    """A command the tool depends on failed. The caller treats it as a refusal, never as a pass."""


class Unanswered(Refused):
    """A command that ran out of time, or a write whose answer cannot be read. What it was sent to
    do may have happened: a caller that sent a write must not report it as refused."""


def run(cmd, cwd=None, stdin=None, timeout=120, binary=False):
    """stdout of a command that exited 0, as text, or as the bytes it wrote when `binary`. A failed
    command raises Refused; one that ran out of time raises Unanswered."""
    text = {} if binary else {"text": True, "encoding": "utf-8", "errors": "replace"}
    try:
        p = subprocess.run(cmd, cwd=cwd or ROOT, input=stdin, capture_output=True, timeout=timeout, **text)
    except subprocess.TimeoutExpired:
        raise Unanswered(f"{cmd[0]}: no answer within {timeout} s") from None
    except OSError as e:
        raise Refused(f"{cmd[0]}: {type(e).__name__}: {e}") from None
    if p.returncode != 0:
        said = p.stderr or p.stdout
        said = said.decode("utf-8", "replace") if binary else said
        raise Refused(f"`{' '.join(cmd[:5])}` exited {p.returncode}: {said.strip()[:400]}")
    return p.stdout


def answer(out, what, write=False):
    """The parsed JSON of an answer. One that cannot be read is a refusal for a read. For a write it
    is Unanswered: the command exited 0, so the write landed, and only its answer is missing."""
    try:
        return json.loads(out)
    except ValueError:
        raise (Unanswered if write else Refused)(f"{what}: answer is not JSON") from None


WRITE_FLAGS = {"-X", "--method", "-f", "--raw-field", "-F", "--field", "--input"}  # what makes a gh api call a write
GATEWAY = re.compile(r"\(HTTP 50[234]\)")  # the forge's answer when it may have carried the write out, and gh exits 1


def gh_json(*args, stdin=None):
    """One GitHub API answer as parsed JSON, through the gh CLI (its login locally, GH_TOKEN in CI).
    A call that carries a body, names a method or sets a field is a write: an answer to it that
    cannot be read is Unanswered, not Refused (answer)."""
    return answer(run(["gh", "api", *args], stdin=stdin), f"gh api {args[0]}", write=stdin is not None or any(a in WRITE_FLAGS for a in args))


def gh_send(method, path, payload):
    """One write to the forge, as parsed JSON; an empty answer (204) is {}. A 502, 503 or 504 is
    Unanswered: the forge may have carried the write out, so a caller must not send it again before
    reading back. Every other refusal is the caller's."""
    what = f"gh api -X {method} {path}"
    try:
        out = run(["gh", "api", "-X", method, path, "--input", "-"], stdin=json.dumps(payload))
    except Refused as e:
        if GATEWAY.search(str(e)):
            raise Unanswered(f"{what}: {e}") from None
        raise
    return answer(out or "{}", what, write=True)


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


def skip_literal(text):
    """The workflow-skip literal a text carries, or None."""
    return next((p for p in SKIP_LITERALS if re.search(p, text or "", re.I | re.M)), None)


def read(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read().replace("\r\n", "\n")


def load_yaml(text):
    """YAML as data. A key that occurs twice in one mapping is an error: the plain loader keeps the
    last value and drops the first without a word, and a dropped rule or fact looks like none."""
    import yaml  # PyYAML is needed only by the tools that read YAML, not by the hooks

    class Strict(yaml.SafeLoader):
        def construct_mapping(self, node, deep=False):
            keys = [self.construct_object(key, deep=True) for key, _ in node.value]
            twice = sorted({str(k) for k in keys if keys.count(k) > 1})
            if twice:
                raise yaml.constructor.ConstructorError(None, None, f"a key occurs twice: {', '.join(twice)}", node.start_mark)
            return super().construct_mapping(node, deep)

    return yaml.load(text, Loader=Strict)


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


def self_test():
    py, cases = sys.executable, []

    def got(fn):
        """What fn returned, or the exception it raised, as a value."""
        try:
            return fn()
        except Exception as e:
            return e

    out = got(lambda: run([py, "-c", "print('x')"]))
    cases.append(("run: the output of a command that exited 0", str(out).strip() == "x", repr(out)))
    out = got(lambda: run([py, "-c", "import sys; sys.exit(3)"]))
    cases.append(("run: a failed command is refused", type(out) is Refused and "exited 3" in str(out), repr(out)))
    out = got(lambda: run(["kurz-no-such-command"]))
    cases.append(("run: a command that is not there is refused", type(out) is Refused, repr(out)))
    out = got(lambda: run([py, "-c", "import time; time.sleep(30)"], timeout=1))
    cases.append(("run: a command that ran out of time is unanswered, not refused", type(out) is Unanswered, repr(out)))
    out = got(lambda: run([py, "-c", "import sys; sys.stdout.buffer.write(bytes([255, 254, 10]))"], binary=True))
    cases.append(("run: binary output comes back as the bytes that were written", out == bytes([255, 254, 10]), repr(out)))
    out = got(lambda: run([py, "-c", "import sys; sys.stderr.buffer.write(bytes([255])); sys.exit(1)"], binary=True))
    cases.append(("run: a failed binary command is refused with its exit code", type(out) is Refused and "exited 1" in str(out), repr(out)))
    cases.append(("answer: JSON is parsed", got(lambda: answer('{"a": 1}', "x")) == {"a": 1}, ""))
    out = got(lambda: answer("", "x"))
    cases.append(("answer: an unreadable answer to a read is refused", type(out) is Refused, repr(out)))
    out = got(lambda: answer("<html>", "x", write=True))
    cases.append(("answer: an unreadable answer to a write is unanswered, because the write landed", type(out) is Unanswered, repr(out)))
    said_by_run = {}

    def stub(answer_or_error):
        """run() replaced by what the forge is to answer, recording the command."""
        def fake(cmd, cwd=None, stdin=None, timeout=120, binary=False):
            said_by_run["cmd"] = cmd
            if isinstance(answer_or_error, Exception):
                raise answer_or_error
            return answer_or_error
        return fake
    real_run = globals()["run"]
    try:
        globals()["run"] = stub(Refused("`gh api -X POST repos/o/r/issues/1/comments` exited 1: gh: Bad Gateway (HTTP 502)"))
        out = got(lambda: gh_send("POST", "repos/o/r/issues/1/comments", {"body": "x"}))
        cases.append(("gh_send: a 5xx answer to a write is unanswered, not refused: the forge may have carried it out",
                      type(out) is Unanswered and "502" in str(out), repr(out)))
        globals()["run"] = stub(Refused("`gh api -X POST repos/o/r/issues/1/comments` exited 1: gh: Validation Failed (HTTP 422)"))
        out = got(lambda: gh_send("POST", "repos/o/r/issues/1/comments", {"body": "x"}))
        cases.append(("gh_send: a 4xx answer to a write is refused", type(out) is Refused and "422" in str(out), repr(out)))
        globals()["run"] = stub("")
        out = got(lambda: gh_send("DELETE", "repos/o/r/issues/comments/1", {}))
        cases.append(("gh_send: an empty answer to a write landed, and is {}", out == {} and said_by_run["cmd"][:4] == ["gh", "api", "-X", "DELETE"],
                      (repr(out), said_by_run.get("cmd"))))
        globals()["run"] = stub("<html>")
        out = got(lambda: gh_json("-X", "POST", "repos/o/r/issues"))
        cases.append(("gh_json: a call that names a method is a write, so its unreadable answer is unanswered", type(out) is Unanswered, repr(out)))
        out = got(lambda: gh_json("repos/o/r/issues", "-f", "title=x"))
        cases.append(("gh_json: a call that sets a field is a write", type(out) is Unanswered, repr(out)))
        out = got(lambda: gh_json("repos/o/r/issues"))
        cases.append(("gh_json: a call with neither is a read, so its unreadable answer is refused", type(out) is Refused, repr(out)))
    finally:
        globals()["run"] = real_run
    with contextlib.redirect_stdout(io.StringIO()) as said:
        codes = (report([]), report([("a", True, "")]), report([("a", True, ""), ("b", False, "why")]))
    cases.append(("report: no case at all is a failure, and so is one failed case", codes == (1, 0, 1), codes))
    cases.append(("report: the summary line counts cases and failures", "2 cases, 1 failed" in said.getvalue(), said.getvalue()[-80:]))
    return report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit("usage: kit.py --self-test")
