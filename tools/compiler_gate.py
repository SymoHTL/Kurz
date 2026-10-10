#!/usr/bin/env python3
"""Gate `compiler-tests`: builds the compiler under compiler/ and runs its unit tests, with the SDK
that compiler/global.json pins and the packages that the lock files pin (a restore in locked mode
refuses a package the lock files do not name).

  compiler_gate.py               build and test; exit 0 when every check passed
  compiler_gate.py --self-test

Red when: the dotnet command is missing or the build fails (the output names the first errors); a
test fails; a test was skipped (a skipped test is work nobody checked that reads as green); fewer
tests passed than FLOOR, or no summary line was printed at all, so that a run that tested nothing
cannot pass; the test command ran out of time. The summary line is the one the test runner prints,
`Passed! - Failed: 0, Passed: N, Skipped: 0, Total: N`, one per test project, and the counts are
summed over them."""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

SOLUTION = "compiler/Kurz.sln"
FLOOR = 300  # tests the suites hold; a scan that finds fewer ran in the wrong place or lost a project
TIMEOUT = 900  # seconds for restore, build and tests together
COMMAND = ["dotnet", "test", SOLUTION, "--nologo", "-c", "Release", "-p:RestoreLockedMode=true"]
SUMMARY = re.compile(r"^(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", re.M)
ERROR_LINE = re.compile(r"^.*(?:error [A-Z]+\d+|error MSB\d+|error NU\d+).*$", re.M)


def run_tests(cwd=None, timeout=TIMEOUT):
    """(exit code, combined output) of the test command. A command that cannot start or ran out of
    time is a refusal: nothing was proven."""
    try:
        p = subprocess.run(COMMAND, cwd=cwd or kit.ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
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
    try:
        code, output = run_tests()
    except kit.Refused as e:
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


GREEN = ("Passed!  - Failed:     0, Passed:   465, Skipped:     0, Total:   465, Duration: 2 s - Kurz.Compiler.Tests.dll (net10.0)\n")
RED = ("Failed!  - Failed:     5, Passed:    77, Skipped:     0, Total:    82, Duration: 107 ms - Kurz.Compiler.Tests.dll (net10.0)\n")
BUILD_ERROR = ("  Determining projects to restore...\n"
               "compiler/Kurz.Compiler/Syntax/Lexer.cs(710,46): error CS0019: Operator '+' cannot be applied [compiler/Kurz.Compiler/Kurz.Compiler.csproj]\n"
               "Build FAILED.\n")


def self_test():
    cases = []

    def case(name, code, output, needle, floor=FLOOR):
        errors, _ = judge(code, output, floor)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    case("a green run with enough tests passes", 0, GREEN, None)
    case("a failed test is red", 1, RED, "5 tests failed")
    case("a skipped test is red", 0, GREEN.replace("Skipped:     0", "Skipped:     1"), "skipped")
    case("fewer passed tests than the floor is red", 0, GREEN.replace("Passed:   465", "Passed:   299"), "floor is")
    case("the floor is at least FLOOR tests", 0, GREEN.replace("Passed:   465", f"Passed:   {FLOOR - 1}"), "floor is")
    case("no summary line is red: nothing was proven", 0, "Build succeeded.\n", "nothing was proven")
    case("a build failure names its first error", 1, BUILD_ERROR, "CS0019")
    case("a non-zero exit with no failed test is red", 3, GREEN, "exited 3")
    case("two projects' summaries are summed", 0, GREEN + GREEN.replace("Passed:   465", "Passed:    10"), None)
    errors, counts = judge(0, GREEN + GREEN.replace("Passed:   465", "Passed:    10").replace("Total:   465", "Total:    10"))
    cases.append(("the counts are summed over the summaries", counts == (0, 475, 0, 475), counts))
    cases.append(("the command restores in locked mode and names the solution", "-p:RestoreLockedMode=true" in COMMAND and SOLUTION in COMMAND, COMMAND))
    try:
        run_tests(cwd=os.path.join(kit.ROOT, "no-such-directory-for-the-self-test"))
        cases.append(("a command that cannot start is refused", False, "no exception"))
    except kit.Refused as e:
        cases.append(("a command that cannot start is refused", True, str(e)))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit(main())
