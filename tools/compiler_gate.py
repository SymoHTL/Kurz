#!/usr/bin/env python3
"""Gate `compiler-tests`: builds the compiler under compiler/ and runs its unit tests, from inside
compiler/, where the dotnet host reads the SDK pin of compiler/global.json (the host reads the
working directory and its parents, not the directory of the solution it is given), with the
packages that the lock files pin (a restore in locked mode refuses a package the lock files do not
name), from the one package source compiler/NuGet.config names, and with the build servers off, so
that no MSBuild node or compiler server outlives the command and holds its output open past the
timeout. After the tests it runs the command it built, `kurz --version`, and prints what the
command wrote: the compiler's version, the major release of LLVM the compiler pins and the clang
it found with that clang's release (design record, section 2, round 20).

  compiler_gate.py               build, test and ask the command; exit 0 when every check passed
  compiler_gate.py --self-test

Red when: compiler/NuGet.config is missing, is not well-formed XML, does not open its package
sources with `<clear />` (the first child of `<packageSources>`, so that a clear inside a comment
or inside another element does not count) or maps no package pattern to a source under
`<packageSourceMapping>` (knowledge/one-package-source-per-restore.md); the dotnet command is
missing or the build fails (the output names the first errors); a test fails; a test was skipped (a
skipped test is work nobody checked that reads as green); fewer tests passed than FLOOR, or no
summary line was printed at all, so that a run that tested nothing cannot pass; the test command
exited non-zero although no test failed (a project that did not build beside one that did); the
test command ran out of time; `kurz --version` exited with another code than 0, which the command
does where it refused the clang it found or found none, did not start or ran out of time, or exited
with 0 and named no clang release. The summary line is the one the test runner prints,
`Passed! - Failed: 0, Passed: N, Skipped: 0, Total: N`, one per test project, and the counts are
summed over them."""
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit  # noqa: E402

CWD = "compiler"  # the working directory of the command: the dotnet host reads global.json from here and its parents, never from beside the solution
SOLUTION = "Kurz.sln"  # relative to CWD
CONFIG = "compiler/NuGet.config"
FLOOR = 1000  # below the tests the suites hold, above what is left without either corpus theory (over 300 cases each): a run that lost a project or a corpus theory cannot pass
TIMEOUT = 900  # seconds for restore, build and tests together
COMMAND = ["dotnet", "test", SOLUTION, "--nologo", "-c", "Release", "-p:RestoreLockedMode=true", "--disable-build-servers"]
# the command the test run built, run without a build of its own; the dotnet host reads global.json from CWD as for the tests
VERSION_COMMAND = ["dotnet", "run", "--project", "Kurz", "-c", "Release", "--no-build", "--", "--version"]
VERSION_TIMEOUT = 120  # seconds: the command starts and asks one clang for its version
CLANG_RELEASE = re.compile(r"^clang version: \d+\.\d+\.\d+$", re.M)
SUMMARY = re.compile(r"^(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", re.M)
ERROR_LINE = re.compile(r"^.*(?:error [A-Z]+\d+|error MSB\d+|error NU\d+).*$", re.M)


def config_errors(root):
    """What is wrong with the package-source config under root: missing, not well-formed XML, without
    the `<clear />` that opens `<packageSources>` and drops the machine's own sources, or without a
    package pattern mapped to a source. The file is read as XML, so that a clear inside a comment
    or inside another element, and a mapping with nothing in it, do not pass. An allowlist of paths
    cannot see a deleted file, so the gate reads it."""
    path = os.path.join(root, CONFIG)
    if not os.path.isfile(path):
        return [f"{CONFIG} is missing: a restore would read the machine's own package sources"]
    try:
        config = ET.fromstring(kit.read(path).encode("utf-8"))  # bytes: a text with an XML declaration is refused as a str
    except ET.ParseError as e:
        return [f"{CONFIG} is not well-formed XML: {e}"]
    errors = []
    sources = config.find("packageSources")
    first = None if sources is None else next(iter(sources), None)
    if first is None or first.tag != "clear":
        errors.append(f"{CONFIG} does not open its package sources with <clear />: the inherited sources would stay")
    mapping = config.find("packageSourceMapping")
    patterns = [] if mapping is None else mapping.findall("packageSource/package[@pattern]")
    if not patterns:
        errors.append(f"{CONFIG} maps no package to its source")
    return errors


def run_tests(cwd=None, timeout=TIMEOUT, run=subprocess.run):
    """(exit code, combined output) of the test command, run inside compiler/ unless cwd says
    otherwise. A command that cannot start or ran out of time is a refusal (kit.Unanswered is a
    kit.Refused): nothing was proven."""
    try:
        p = run(COMMAND, cwd=cwd or os.path.join(kit.ROOT, CWD), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired:
        raise kit.Unanswered(f"dotnet test: no answer within {timeout} s") from None
    except OSError as e:
        raise kit.Refused(f"dotnet: {type(e).__name__}: {e}") from None
    return p.returncode, p.stdout + p.stderr


def run_version(cwd=None, timeout=VERSION_TIMEOUT, run=subprocess.run):
    """(exit code, standard output, standard error) of `kurz --version`, run inside compiler/ unless
    cwd says otherwise. A command that cannot start or ran out of time is a refusal."""
    try:
        p = run(VERSION_COMMAND, cwd=cwd or os.path.join(kit.ROOT, CWD), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired:
        raise kit.Unanswered(f"kurz --version: no answer within {timeout} s") from None
    except OSError as e:
        raise kit.Refused(f"kurz --version: {type(e).__name__}: {e}") from None
    return p.returncode, p.stdout, p.stderr


def judge_version(code, output, error):
    """The errors of a `kurz --version` run: an exit code other than 0, with what the command wrote on
    standard error, or a 0 with no line that names the release of the clang it found, which would
    pass a command that asked no clang."""
    if code != 0:
        return [f"kurz --version exited {code}: " + " | ".join(line.strip() for line in error.splitlines() if line.strip())[:600]]
    if not CLANG_RELEASE.search(output):
        return ["kurz --version exited 0 and named no clang release: nothing was proven"]
    return []


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
    try:
        version_code, version_output, version_error = run_version()
        print("kurz --version:")
        print((version_output + version_error).rstrip())
        errors += judge_version(version_code, version_output, version_error)
    except kit.Refused as e:
        errors.append(str(e))
    for e in errors:
        print("ERROR:", e)
    print(f"compiler tests: {passed} passed, {failed} failed, {skipped} skipped, {total} total, {len(errors)} errors")
    return 1 if errors else 0


GREEN = ("Passed!  - Failed:     0, Passed:  1263, Skipped:     0, Total:  1263, Duration: 2 s - Kurz.Compiler.Tests.dll (net10.0)\n")
RED = ("Failed!  - Failed:     5, Passed:    77, Skipped:     0, Total:    82, Duration: 107 ms - Kurz.Compiler.Tests.dll (net10.0)\n")
BUILD_ERROR = ("  Determining projects to restore...\n"
               "compiler/Kurz.Compiler/Syntax/Lexer.cs(710,46): error CS0019: Operator '+' cannot be applied [compiler/Kurz.Compiler/Kurz.Compiler.csproj]\n"
               "Build FAILED.\n")
CONFIG_TEXT = ("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- one source -->\n<configuration>\n  <packageSources>\n    <clear />\n"
               "    <add key=\"one\" value=\"https://example.invalid/v3/index.json\" />\n"
               "  </packageSources>\n  <packageSourceMapping>\n    <packageSource key=\"one\">\n      <package pattern=\"*\" />\n    </packageSource>\n"
               "  </packageSourceMapping>\n</configuration>\n")


VERSION_OUTPUT = "kurz 0.1.0\nLLVM version: 23\nclang: /usr/bin/clang-23\nclang version: 23.1.4\n"
NO_CLANG = "error: no clang found\n  note: looked for clang-23 and clang on the path\n  help: install LLVM 23\n"


def self_test():
    cases = []

    def case(name, code, output, needle, floor=FLOOR):
        errors, _ = judge(code, output, floor)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    case("a green run with enough tests passes", 0, GREEN, None)
    case("a failed test is red", 1, RED, "5 tests failed")
    case("a skipped test is red", 0, GREEN.replace("Skipped:     0", "Skipped:     1"), "skipped")
    case("fewer passed tests than the floor is red", 0, GREEN.replace("Passed:  1263", "Passed:   999"), "floor is")
    cases.append(("the floor is at least 1000 tests", FLOOR >= 1000, FLOOR))
    case("no summary line is red: nothing was proven", 0, "Build succeeded.\n", "nothing was proven")
    case("a build failure names its first error", 1, BUILD_ERROR, "CS0019")
    case("a non-zero exit with no failed test is red", 3, GREEN, "exited 3")
    case("two projects' summaries are summed", 0, GREEN.replace("Passed:  1263", "Passed:   520") * 2, None)  # each alone is below the floor
    errors, counts = judge(0, GREEN + GREEN.replace("Passed:  1263", "Passed:    10").replace("Total:  1263", "Total:    10"))
    cases.append(("the counts are summed over the summaries", counts == (0, 1273, 0, 1273), counts))
    cases.append(("the command restores in locked mode and names the solution", "-p:RestoreLockedMode=true" in COMMAND and SOLUTION in COMMAND, COMMAND))
    cases.append(("the command runs inside compiler/, where the SDK pin is read", CWD == "compiler" and "/" not in SOLUTION, (CWD, SOLUTION)))
    cases.append(("the command disables the build servers, so that the timeout can end it", "--disable-build-servers" in COMMAND, COMMAND))
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
    errors = judge_version(0, VERSION_OUTPUT, "")
    cases.append(("a version run that names the clang it found passes", not errors, errors))
    errors = judge_version(1, VERSION_OUTPUT.split("clang:")[0], NO_CLANG)
    cases.append(("a version run that exits 1 is red and names the command's error", any("exited 1" in e and "no clang found" in e for e in errors), errors))
    errors = judge_version(0, VERSION_OUTPUT.split("clang:")[0], "")
    cases.append(("a version run that exits 0 and names no clang release is red", any("named no clang release" in e for e in errors), errors))
    cases.append(("the version run asks the built command for its version, with no build of its own",
                  VERSION_COMMAND[-2:] == ["--", "--version"] and "--no-build" in VERSION_COMMAND and "Release" in VERSION_COMMAND, VERSION_COMMAND))

    def version_out_of_time(*args, **kwargs):
        raise subprocess.TimeoutExpired(VERSION_COMMAND, kwargs.get("timeout"))

    try:
        run_version(timeout=1, run=version_out_of_time)
        cases.append(("a version run out of time is refused", False, "no exception"))
    except kit.Unanswered as e:
        cases.append(("a version run out of time is refused", "within 1 s" in str(e), str(e)))
    with kit.scratch("compiler-gate-") as root:
        cases.append(("a missing package config is red", any("missing" in e for e in config_errors(root)), config_errors(root)))
        os.makedirs(os.path.join(root, "compiler"))
        path = os.path.join(root, CONFIG)

        def config(text):
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
            return config_errors(root)

        errors = config(CONFIG_TEXT)
        cases.append(("a config that clears the sources and maps the packages passes", not errors, errors))
        errors = config(CONFIG_TEXT.replace("    <clear />\n", ""))
        cases.append(("a config without clear is red", any("clear" in e for e in errors), errors))
        errors = config(CONFIG_TEXT.replace("    <clear />\n", "").replace("</configuration>", "  <disabledPackageSources>\n    <clear />\n  </disabledPackageSources>\n</configuration>"))
        cases.append(("a clear outside packageSources is red", any("clear" in e for e in errors), errors))
        errors = config(CONFIG_TEXT.replace("<clear />", "<!-- <clear /> -->"))
        cases.append(("a clear inside a comment is red", any("clear" in e for e in errors), errors))
        errors = config(CONFIG_TEXT.replace("packageSourceMapping", "nothing"))
        cases.append(("a config without a source mapping is red", any("maps no package" in e for e in errors), errors))
        errors = config(re.sub(r"<packageSourceMapping>.*</packageSourceMapping>", "<packageSourceMapping></packageSourceMapping>", CONFIG_TEXT, flags=re.S))
        cases.append(("an empty source mapping is red", any("maps no package" in e for e in errors), errors))
        errors = config("<configuration>\n")
        cases.append(("a config that is not XML is red", any("well-formed" in e for e in errors), errors))
    return kit.report(cases)


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        sys.exit(self_test())
    sys.exit(main())
