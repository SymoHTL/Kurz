#!/usr/bin/env python3
"""The one definition of what a tool's command line takes (#156). A tool names its calls in a table,
{name: (word, ...)}, where a word is the literal a caller types or a Value, one argument that its test
accepts. A call is one of them, each alone: its words in their places, nothing before, between or after.

`refused(argv, calls, tool)` is True where argv is no call of the table, and then it has printed the
refusal to stderr, which the harness gives the session as the reason of a hook's exit 2
(knowledge/a-hook-denies-by-exit-2-or-by-its-json.md):
`REFUSED, nothing run: <arguments> is not a call of this tool`, or `<option> needs <what>` where
an option's argument is missing or not accepted, then the usage, which is made from the same table, so
that it names what is taken and nothing else. The arguments are printed in ASCII, a character outside it
as its escape, so that no log's code page can end the refusal in a traceback. The tool then exits
REFUSED (2) before it reads, writes, fetches or runs anything: `--help`, `-h`, a mistyped option, a
positional argument, a repeated option and two calls together are refused, as `tools/red_proof.py`
refuses them (#297). Before that, a mistyped option was read as the tool's default run: every gate, a
build of the compiler, a rewrite of the case tables, and, in `tools/tree_gate.py`, a scan of the
working tree in place of the write-time hook's check, which let every write through.

The route: every tool of ROUTED begins its `__main__` with
    if calls.refused(sys.argv[1:], CALLS, "<its path>"):
        sys.exit(calls.REFUSED)
so that nothing in that block reads an argument before the question. Gate: `self-tests` (this module's
cases "route:", which read each of them). The one argument read before it is the write-time hook's:
`tools/tree_gate.py` denies a `--hook` call by exit 2 where `kit.py` or this module does not import. A tool outside ROUTED keeps the parser it has (`merge_pr.py`, `pr_gates.py`, `quality_evidence.py`,
`red_proof.py`, `review/review.py`, `kit.py`), and no gate holds it to the route: no case reads its
`__main__`, and no rule of the review reads a tool's command line.

Calls: `--self-test` alone runs this module's cases; any other call is refused as above."""
import contextlib
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

REFUSED = 2  # not kit.PARTLY (5): a refused call is a FAIL where a gate's command is refused
ROUTED = ("tools/gates.py", "tools/compiler_gate.py", "tools/unicode_tables.py", "tools/lint_reference.py",
          "tools/lint_knowledge.py", "tools/lint_ci.py", "tools/tree_gate.py")


class Value:
    """One argument of a call: `name` stands for it in the usage, `what` says in a refusal what it must be,
    and `accepts` tests the word a caller typed."""

    def __init__(self, name, what, accepts=lambda word: True):
        self.name, self.what, self.accepts = name, what, accepts


def understood(argv, calls):
    """The name of the call argv is, or None: argv has each word of one call in its place, and no other."""
    for name, words in calls.items():
        if len(argv) == len(words) and all(w.accepts(a) if isinstance(w, Value) else a == w for w, a in zip(words, argv)):
            return name
    return None


def shown(argv):
    """The arguments as the refusal prints them: ASCII, a character outside it as its escape, and an empty
    argument as two quotes, so that it is seen."""
    return " ".join(a.encode("ascii", "backslashreplace").decode("ascii") if a else '""' for a in argv) or "(no argument)"


def why(argv, calls):
    """The first line of the refusal: what an option needs where argv begins as a call that takes an argument
    and that argument is missing or not accepted; otherwise that argv is not a call of the tool."""
    for words in calls.values():
        for k, w in enumerate(words):
            if isinstance(w, Value) and argv[:k] == list(words[:k]) and len(argv) <= len(words) and k > 0 \
                    and (len(argv) == k or not w.accepts(argv[k])):
                return f"{shown(list(words[:k]))} needs {w.what}"
    return f"{shown(argv)} is not a call of this tool"


def usage(calls, tool):
    """Every call of the table, one a line, its arguments by name."""
    lines = [" ".join([tool] + [w.name if isinstance(w, Value) else w for w in words]) for words in calls.values()]
    return "usage: " + "\n       ".join(lines) + f"\n  each call alone; the docstring of {tool} says what each does"


def refused(argv, calls, tool, out=None):
    """True where argv is no call of `calls`, with the refusal printed to stderr (or to `out`, in a case): the line
    `why` gives and the usage."""
    if understood(argv, calls) is not None:
        return False
    out = out or (lambda line: print(line, file=sys.stderr))
    out(f"REFUSED, nothing run: {why(argv, calls)}")
    out(usage(calls, tool))
    return True


def route_errors(path, text):
    """What keeps a tool's `__main__` from the route: the block must begin by asking `refused` with the
    tool's own path and exit REFUSED where it refuses, before it reads any argument."""
    marker = 'if __name__ == "__main__":\n'
    if text.count(marker) != 1:
        return [f"{path}: no one `__main__` block to read"]
    gate = f'    if calls.refused(sys.argv[1:], CALLS, "{path}"):\n        sys.exit(calls.REFUSED)\n'
    if not text.split(marker, 1)[1].startswith(gate):
        return [f"{path}: its `__main__` does not begin by asking calls.refused with its own path and exiting calls.REFUSED"]
    return []


CALLS = {"self-test": ("--self-test",)}


def self_test():
    cases = []
    pr = Value("N", "the number of the pull request", str.isdigit)
    table = {"all": (), "pr": ("--pr", pr), "hook": ("--check-push-hook",), "self-test": ("--self-test",)}
    good = [[], ["--pr", "7"], ["--check-push-hook"], ["--self-test"]]
    cases.append(("understood: each call, its words in their places and its argument accepted, is named",
                  [understood(a, table) for a in good] == ["all", "pr", "hook", "self-test"], [understood(a, table) for a in good]))
    odd = (["--help"], ["-h"], ["--slef-test"], ["tools"], ["--self-test", "--self-test"], ["--self-test", "--pr", "7"],
           ["--pr", "7", "--pr", "8"], ["--pr", "7", "extra"], ["--pr=7"], ["--pr"], ["--pr", "seven"], ["--check-push-hook", "--self-test"])
    cases.append(("understood: --help, -h, a mistyped option, a positional argument, a repeated option, two calls together, a glued "
                  "argument, a missing one and one its test refuses are no call", [understood(a, table) for a in odd] == [None] * len(odd),
                  [understood(a, table) for a in odd]))
    printed = []
    said = [refused(a, table, "tools/x.py", printed.append) for a in (["--help"], ["--slef-test"], ["tools", "x"])]
    quiet = []
    let = [refused(a, table, "tools/x.py", quiet.append) for a in good]
    cases.append(("refused: a call that is no call of the table is refused with its arguments and the usage; a call of it prints nothing",
                  said == [True] * 3 and let == [False] * 4 and quiet == []
                  and printed[0::2] == ["REFUSED, nothing run: --help is not a call of this tool",
                                        "REFUSED, nothing run: --slef-test is not a call of this tool",
                                        "REFUSED, nothing run: tools x is not a call of this tool"]
                  and printed[1::2] == [usage(table, "tools/x.py")] * 3, (said, let, printed[:4])))
    needs = [why(a, table) for a in (["--pr"], ["--pr", "seven"], ["--pr", "7", "--pr", "8"], ["--pr=7"])]
    cases.append(("refused: an option whose argument is missing or not accepted says what it needs; a longer or a glued call does not",
                  needs == ["--pr needs the number of the pull request"] * 2
                  + ["--pr 7 --pr 8 is not a call of this tool", "--pr=7 is not a call of this tool"], needs))
    echoed = []
    refused(["--prü", ""], table, "tools/x.py", echoed.append)
    cases.append(("refused: the arguments are printed in ASCII, a character outside it as its escape, and an empty one as two quotes",
                  echoed[:1] == ["REFUSED, nothing run: --pr\\xfc \"\" is not a call of this tool"] and all(line.isascii() for line in echoed),
                  echoed[:1]))
    cases.append(("refused: no argument, where the table has no call without one, is named as such",
                  why([], {"self-test": ("--self-test",)}) == "(no argument) is not a call of this tool", why([], {"self-test": ("--self-test",)})))
    cases.append(("usage: every call of the table, one a line, its arguments by name",
                  usage(table, "tools/x.py") == "usage: tools/x.py\n       tools/x.py --pr N\n       tools/x.py --check-push-hook\n"
                  "       tools/x.py --self-test\n  each call alone; the docstring of tools/x.py says what each does", usage(table, "tools/x.py")))
    cases.append(("call: this module takes --self-test alone", understood(["--self-test"], CALLS) == "self-test"
                  and understood(["--self-test", "-v"], CALLS) is None and understood([], CALLS) is None, CALLS))
    routed = {p: kit.read(os.path.join(kit.ROOT, p)) for p in ROUTED}
    errors = [e for p, text in routed.items() for e in route_errors(p, text)]
    cases.append(("route: each tool of ROUTED begins its __main__ block by asking calls.refused with its own path; what stands above "
                  "the block is not read",
                  len(ROUTED) == 7 and not errors, errors))
    gate = '    if calls.refused(sys.argv[1:], CALLS, "tools/t.py"):\n        sys.exit(calls.REFUSED)\n'
    own = 'import sys\nif __name__ == "__main__":\n    if "--self-test" in sys.argv:\n        sys.exit(0)\n' + gate
    other = 'import sys\nif __name__ == "__main__":\n' + gate.replace("tools/t.py", "tools/u.py")
    kept = 'import sys\nif __name__ == "__main__":\n' + gate.replace("calls.REFUSED", "1")
    fine = 'import sys\nif __name__ == "__main__":\n' + gate + '    if "--self-test" in sys.argv:\n        sys.exit(0)\n'
    shapes = [bool(route_errors("tools/t.py", t)) for t in (own, other, kept, fine)]
    cases.append(("route: a tool that reads its arguments before the question, names another path in it or exits otherwise is off "
                  "the route; the routed shape is on it", shapes == [True, True, True, False], shapes))
    reached = []
    with contextlib.redirect_stderr(io.StringIO()) as text:
        codes = [main(a, lambda: reached.append(True) or 0) for a in (["--slef-test"], ["--self-test", "-v"], [])]
    refusal, before = text.getvalue(), list(reached)
    ran = main(["--self-test"], lambda: reached.append(True) or 0)
    cases.append(("call: a call of this module it does not take is refused with exit 2 and runs no case; --self-test alone runs them",
                  codes == [REFUSED] * 3 and not before and refusal.count("REFUSED, nothing run: ") == 3 and ran == 0 and reached == [True],
                  (codes, before, ran, refusal[:200])))
    return kit.report(cases)


def main(argv, test=None):
    """The exit code of this module's command; `test` stands in for self_test() in a case."""
    if refused(argv, CALLS, "tools/calls.py"):
        return REFUSED
    return (test or self_test)()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
