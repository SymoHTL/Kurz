#!/usr/bin/env python3
"""Gate `compiler-tests`: builds the compiler under compiler/ and runs its unit tests, with the SDK
that compiler/global.json pins and the packages that the lock files pin (a restore in locked mode
refuses a package the lock files do not name), from the one package source compiler/NuGet.config
names.

  compiler_gate.py               build and test; exit 0 when every check passed
  compiler_gate.py --self-test

Red when: compiler/NuGet.config is missing, does not clear the inherited package sources or maps
no package to a source (knowledge/one-package-source-per-restore.md); the dotnet command is missing
or the build fails (the output names the first errors); a test fails; a test was skipped (a skipped
test is work nobody checked that reads as green); fewer tests passed than FLOOR, or no summary line
was printed at all, so that a run that tested nothing cannot pass; the test command exited non-zero
although no test failed (a project that did not build beside one that did); the test command ran
out of time. The summary line is the one the test runner prints,
`Passed! - Failed: 0, Passed: N, Skipped: 0, Total: N`, one per test project, and the counts are
summed over them."""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

SOLUTION = "compiler/Kurz.sln"
CONFIG = "compiler/NuGet.config"
FLOOR = 650  # below the tests the suites hold, above what is left without the corpus theory (over 300 of them): a run that lost a project or the corpus cannot pass
TIMEOUT = 900  # seconds for restore, build and tests together
COMMAND = ["dotnet", "test", SOLUTION, "--nologo", "-c", "Release", "-p:RestoreLockedMode=true"]
SUMMARY = re.compile(r"^(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", re.M)
ERROR_LINE = re.compile(r"^.*(?:error [A-Z]+\d+|error MSB\d+|error NU\d+).*$", re.M)


def config_errors(root):
    """What is wrong with the package-source config under root: missing, without the `<clear />` that
    drops the machine's own sources, or without a mapping of packages to a source. An allowlist of
    paths cannot see a deleted file, so the gate reads it."""
    path = os.path.join(root, CONFIG)
    if not os.path.isfile(path):
        return [f"{CONFIG} is missing: a restore would read the machine's own package sources"]
    text = kit.read(path)
    errors = []
    if "<clear />" not in text and "<clear/>" not in text:
        errors.append(f"{CONFIG} does not clear the inherited package sources")
    if "<packageSourceMapping>" not in text:
        errors.append(f"{CONFIG} maps no package to its source")
    return errors


def run_tests(cwd=None, timeout=TIMEOUT, run=subprocess.run):
    """(exit code, combined output) of the test command. A command that cannot start or ran out of
    time is a refusal (kit.Unanswered is a kit.Refused): nothing was proven."""
    try:
        p = run(COMMAND, cwd=cwd or kit.ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired:
        raise kit.Unanswered(f"dotnet test: no answer within {timeout} s") from None
    except OSError as e:
        raise kit.Refused(f"dotnet: {type(e).__name__}: {e}") from None
    return p.returncode, p.stdout + p.stderr


def judge(code, output, floor=FLOOR):
    """(errors, counts) from the test command's exit code and output. counts = (failed, passed, skipped, total)."""
    summaries = SUMMARY.findall(output)
    failed = sum(int(s[1]) for s in summaries)
    passed = sum(int(s[2]) for s in summaries)
    skipped = sum(int(s[3]) for s in summaries)
    total = sum(int(s[4]) for s in summaries)
    errors = []
    if not summaries:
        first = ERROR_LINE.findall(output)[:5]
        errors.append("no test summary line: nothing was proven" + (" - " + " | ".join(line.strip()[:200] for line in first) if first else f" (exit {code})"))
        return errors, (failed, passed, skipped, total)
    if failed:
        errors.append(f"{failed} tests failed")
    if skipped:
        errors.append(f"{skipped} tests skipped: a skipped test is unchecked work")
    if passed < floor:
        errors.append(f"only {passed} tests passed, floor is {floor}")
    if code != 0 and not failed:
        errors.append(f"dotnet test exited {code} although no test failed")
    return errors, (failed, passed, skipped, total)


def main():
    errors = config_errors(kit.ROOT)
    if errors:
        for e in errors:
            print("ERROR:", e)
        print(f"compiler tests: 0 passed, {len(errors)} errors")
        return 1
    try:
        code, output = run_tests()
    except kit.Refused as e:  # a run out of time (kit.Unanswered) as much as a command that cannot start
        print(f"ERROR: {e}")
        print("compiler tests: 0 passed, 1 errors")
        return 1
    errors, (failed, passed, skipped, total) = judge(code, output)
    if errors:
        print(output[-6000:])
    for e in errors:
        print("ERROR:", e)
    print(f"compiler tests: {passed} passed, {failed} failed, {skipped} skipped, {total} total, {len(errors)} errors")
    return 1 if errors else 0


GREEN = ("Passed!  - Failed:     0, Passed:   704, Skipped:     0, Total:   704, Duration: 2 s - Kurz.Compiler.Tests.dll (net10.0)\n")
RED = ("Failed!  - Failed:     5, Passed:    77, Skipped:     0, Total:    82, Duration: 107 ms - Kurz.Compiler.Tests.dll (net10.0)\n")
BUILD_ERROR = ("  Determining projects to restore...\n"
               "compiler/Kurz.Compiler/Syntax/Lexer.cs(710,46): error CS0019: Operator '+' cannot be applied [compiler/Kurz.Compiler/Kurz.Compiler.csproj]\n"
               "Build FAILED.\n")
CONFIG_TEXT = ("<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"one\" value=\"https://example.invalid/v3/index.json\" />\n"
               "  </packageSources>\n  <packageSourceMapping>\n    <packageSource key=\"one\"><package pattern=\"*\" /></packageSource>\n"
               "  </packageSourceMapping>\n</configuration>\n")


def self_test():
    cases = []

    def case(name, code, output, needle, floor=FLOOR):
        errors, _ = judge(code, output, floor)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    case("a green run with enough tests passes", 0, GREEN, None)
    case("a failed test is red", 1, RED, "5 tests failed")
    case("a skipped test is red", 0, GREEN.replace("Skipped:     0", "Skipped:     1"), "skipped")
    case("fewer passed tests than the floor is red", 0, GREEN.replace("Passed:   704", "Passed:   649"), "floor is")
    cases.append(("the floor is at least 650 tests", FLOOR >= 650, FLOOR))
    case("no summary line is red: nothing was proven", 0, "Build succeeded.\n", "nothing was proven")
    case("a build failure names its first error", 1, BUILD_ERROR, "CS0019")
    case("a non-zero exit with no failed test is red", 3, GREEN, "exited 3")
    case("two projects' summaries are summed", 0, GREEN.replace("Passed:   704", "Passed:   330") * 2, None)  # each alone is below the floor
    errors, counts = judge(0, GREEN + GREEN.replace("Passed:   704", "Passed:    10").replace("Total:   704", "Total:    10"))
    cases.append(("the counts are summed over the summaries", counts == (0, 714, 0, 714), counts))
    cases.append(("the command restores in locked mode and names the solution", "-p:RestoreLockedMode=true" in COMMAND and SOLUTION in COMMAND, COMMAND))
    try:
        run_tests(cwd=os.path.join(kit.ROOT, "no-such-directory-for-the-self-test"))
        cases.append(("a command that cannot start is refused", False, "no exception"))
    except kit.Refused as e:
        cases.append(("a command that cannot start is refused", True, str(e)))

    def out_of_time(*args, **kwargs):
        raise subprocess.TimeoutExpired(COMMAND, kwargs.get("timeout"))

    try:
        run_tests(timeout=1, run=out_of_time)
        cases.append(("a run out of time is refused", False, "no exception"))
    except kit.Unanswered as e:
        cases.append(("a run out of time is refused", "within 1 s" in str(e), str(e)))
    with kit.scratch("compiler-gate-") as root:
        cases.append(("a missing package config is red", any("missing" in e for e in config_errors(root)), config_errors(root)))
        os.makedirs(os.path.join(root, "compiler"))
        path = os.path.join(root, CONFIG)
        with open(path, "w", encoding="utf-8") as f:
            f.write(CONFIG_TEXT)
        cases.append(("a config that clears the sources and maps the packages passes", not config_errors(root), config_errors(root)))
        with open(path, "w", encoding="utf-8") as f:
            f.write(CONFIG_TEXT.replace("    <clear />\n", ""))
        cases.append(("a config without clear is red", any("clear" in e for e in config_errors(root)), config_errors(root)))
        with open(path, "w", encoding="utf-8") as f:
            f.write(CONFIG_TEXT.replace("packageSourceMapping", "nothing"))
        cases.append(("a config without a source mapping is red", any("maps no package" in e for e in config_errors(root)), config_errors(root)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit(main())
