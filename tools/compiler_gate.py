#!/usr/bin/env python3
"""Gate `compiler-tests`: builds the compiler under compiler/ and runs its unit tests, from inside
compiler/, where the dotnet host reads the SDK pin of compiler/global.json (the host reads the
working directory and its parents, not the directory of the solution it is given), with the
packages that the lock files pin (a restore in locked mode refuses a package the lock files do not
name), from the one package source compiler/NuGet.config names, and with the build servers off, so
that no MSBuild node or compiler server outlives the command and holds its output open past the
timeout.

  compiler_gate.py               build and test; exit 0 when every check passed
  compiler_gate.py --self-test

Red when: compiler/NuGet.config is missing, is not well-formed XML, does not open its package
sources with `<clear />` (the first child of `<packageSources>`, so that a clear inside a comment
or inside another element does not count) or maps no package pattern to a source under
`<packageSourceMapping>` (knowledge/one-package-source-per-restore.md); the dotnet command is
missing or the build fails (the output names the first errors); a test fails; a test was skipped (a
skipped test is work nobody checked that reads as green); fewer tests passed than FLOOR, or no
summary line was printed at all, so that a run that tested nothing cannot pass; the test command
exited non-zero although no test failed (a project that did not build beside one that did); the
test command ran out of time; no clang answers `--version` under the names the compiler tries, or
the one that answers is a major release outside ACCEPTED, the set this gate names (design record,
section 2): the suite builds and runs the corpus with it, and its version line is printed so that
the log says which clang did. The summary line is the one the test runner prints,
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
FLOOR = 1450  # below the tests the suites hold, above what is left without one corpus theory (over 300 cases each) or the corpus runner (over 180): a run that lost a project or a theory cannot pass
TIMEOUT = 900  # seconds for restore, build and tests together
COMMAND = ["dotnet", "test", SOLUTION, "--nologo", "-c", "Release", "-p:RestoreLockedMode=true", "--disable-build-servers"]
SUMMARY = re.compile(r"^(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)", re.M)
ERROR_LINE = re.compile(r"^.*(?:error [A-Z]+\d+|error MSB\d+|error NU\d+).*$", re.M)
ACCEPTED = range(18, 24)  # the major releases of clang the emitted IR is written for: 18 on the Linux image, 20 on the Windows image, and up to the newest a build was seen on; a release outside is red
VERSIONED = re.compile(r"^clang-(\d+)(?:\.exe)?$", re.I)
VERSION = re.compile(r"clang version (\d+)\.(\d+)\.(\d+)")


def clang_candidates(path=None, windows=None, program_files=None):
    """The names the compiler tries, in its order (compiler/Kurz.Compiler/Build/Clang.cs): every
    `clang-<major>` on the path, newest major first, then `clang`, then on Windows the LLVM
    installer's directory under the program files, which the installer does not put on the path."""
    windows = os.name == "nt" if windows is None else windows
    path = os.environ.get("PATH", "") if path is None else path
    versioned = []
    for d in path.split(os.pathsep):
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            m = VERSIONED.match(f)
            if m:
                versioned.append((int(m.group(1)), os.path.join(d, f)))
    names = [p for _, p in sorted(versioned, key=lambda v: (-v[0], v[1]))]
    names.append("clang.exe" if windows else "clang")
    if windows:
        program_files = os.environ.get("ProgramFiles", "") if program_files is None else program_files
        if program_files:
            names.append(os.path.join(program_files, "LLVM", "bin", "clang.exe"))
    return names


def clang_version(output):
    """(major, minor, patch, first line) from what `clang --version` printed, or None when it is not clang's answer."""
    first = output.split("\n")[0].rstrip("\r")
    m = VERSION.search(first)
    return (int(m.group(1)), int(m.group(2)), int(m.group(3)), first) if m else None


def find_clang(candidates, run=subprocess.run):
    """(name, version) of the first candidate that answers `--version` as clang does, else (None, None)."""
    for candidate in candidates:
        try:
            p = run([candidate, "--version"], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=30)
        except (OSError, subprocess.TimeoutExpired):
            continue
        if p.returncode != 0:
            continue
        version = clang_version(p.stdout)
        if version:
            return candidate, version
    return None, None


def toolchain_errors(candidates=None, run=subprocess.run, accepted=ACCEPTED, out=print):
    """What is wrong with the clang the compiler would use: none of the names answers as clang (the
    names tried are listed), or the one that answers is a major release outside the accepted set.
    The version line and the name are printed, so that the log says which clang built the corpus."""
    candidates = clang_candidates() if candidates is None else candidates
    name, version = find_clang(candidates, run)
    if name is None:
        return [f"no clang answers `--version`; tried {', '.join(candidates)}"]
    out(f"clang: {version[3]} ({name})")
    if version[0] not in accepted:
        return [f"clang {version[0]}.{version[1]}.{version[2]} at {name} is outside the accepted releases {accepted.start} to {accepted.stop - 1}"]
    return []


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
    errors = config_errors(kit.ROOT) + toolchain_errors()
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


GREEN = ("Passed!  - Failed:     0, Passed:  1500, Skipped:     0, Total:  1500, Duration: 2 s - Kurz.Compiler.Tests.dll (net10.0)\n")
RED = ("Failed!  - Failed:     5, Passed:    77, Skipped:     0, Total:    82, Duration: 107 ms - Kurz.Compiler.Tests.dll (net10.0)\n")
BUILD_ERROR = ("  Determining projects to restore...\n"
               "compiler/Kurz.Compiler/Syntax/Lexer.cs(710,46): error CS0019: Operator '+' cannot be applied [compiler/Kurz.Compiler/Kurz.Compiler.csproj]\n"
               "Build FAILED.\n")
CONFIG_TEXT = ("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!-- one source -->\n<configuration>\n  <packageSources>\n    <clear />\n"
               "    <add key=\"one\" value=\"https://example.invalid/v3/index.json\" />\n"
               "  </packageSources>\n  <packageSourceMapping>\n    <packageSource key=\"one\">\n      <package pattern=\"*\" />\n    </packageSource>\n"
               "  </packageSourceMapping>\n</configuration>\n")


def self_test():
    cases = []

    def case(name, code, output, needle, floor=FLOOR):
        errors, _ = judge(code, output, floor)
        cases.append((name, any(needle in e for e in errors) if needle else not errors, errors))

    case("a green run with enough tests passes", 0, GREEN, None)
    case("a failed test is red", 1, RED, "5 tests failed")
    case("a skipped test is red", 0, GREEN.replace("Skipped:     0", "Skipped:     1"), "skipped")
    case("fewer passed tests than the floor is red", 0, GREEN.replace("Passed:  1500", "Passed:   999"), "floor is")
    cases.append(("the floor is at least 1450 tests", FLOOR >= 1450, FLOOR))
    case("no summary line is red: nothing was proven", 0, "Build succeeded.\n", "nothing was proven")
    case("a build failure names its first error", 1, BUILD_ERROR, "CS0019")
    case("a non-zero exit with no failed test is red", 3, GREEN, "exited 3")
    case("two projects' summaries are summed", 0, GREEN.replace("Passed:  1500", "Passed:   750") * 2, None)  # each alone is below the floor
    errors, counts = judge(0, GREEN + GREEN.replace("Passed:  1500", "Passed:    10").replace("Total:  1500", "Total:    10"))
    cases.append(("the counts are summed over the summaries", counts == (0, 1510, 0, 1510), counts))
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
    def answer(code, text):
        def run(args, **kwargs):
            return subprocess.CompletedProcess(args, code, stdout=text, stderr="")
        return run

    def missing(args, **kwargs):
        raise OSError(f"{args[0]} is not there")

    said = []
    errors = toolchain_errors(["clang-18"], answer(0, "Ubuntu clang version 18.1.3 (1ubuntu1)\nTarget: x86_64-pc-linux-gnu\n"), out=said.append)
    cases.append(("a clang inside the accepted releases passes and its version is printed", not errors and said == ["clang: Ubuntu clang version 18.1.3 (1ubuntu1) (clang-18)"], (errors, said)))
    errors = toolchain_errors(["clang"], answer(0, "clang version 17.0.6\n"), out=said.append)
    cases.append(("a clang outside the accepted releases is red", any("outside the accepted" in e for e in errors), errors))
    errors = toolchain_errors(["clang-18", "clang"], missing, out=said.append)
    cases.append(("a missing clang is red and names what was tried", any("no clang" in e and "clang-18, clang" in e for e in errors), errors))
    errors = toolchain_errors(["clang"], answer(0, "gcc (Ubuntu 13.3.0-6ubuntu2~24.04) 13.3.0\n"), out=said.append)
    cases.append(("a compiler that does not answer as clang is red", any("no clang" in e for e in errors), errors))
    errors = toolchain_errors(["clang"], answer(1, "clang version 18.1.3\n"), out=said.append)
    cases.append(("a clang that fails on --version is red", any("no clang" in e for e in errors), errors))
    cases.append(("the accepted releases hold the Linux image's 18 and the Windows image's 20", 18 in ACCEPTED and 20 in ACCEPTED, ACCEPTED))
    with kit.scratch("compiler-gate-clang-") as root:
        for name in ("clang-18", "clang-20", "clang", "clang-format-18"):
            open(os.path.join(root, name), "w").close()
        names = [os.path.basename(n) for n in clang_candidates(path=root, windows=False)]
        cases.append(("the versioned names come newest first, then the bare name", names == ["clang-20", "clang-18", "clang"], names))
        names = clang_candidates(path=root, windows=True, program_files=os.path.join(root, "pf"))
        cases.append(("on Windows the installer's directory is tried last", names[-2:] == ["clang.exe", os.path.join(root, "pf", "LLVM", "bin", "clang.exe")], names))
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
