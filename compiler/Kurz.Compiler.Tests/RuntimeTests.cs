using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The runtime's Kurz under <c>runtime/</c> (design record, section 2): what the compiler carries is the files of the repository, every
/// target's check is clean, and every target's directory gives the functions <c>common/</c> calls with the same parameters and result; and
/// the checker's reading of a folder that a `use` brings in, <c>sys</c> (F9, F10, and for the qualified call the design record's section 2:
/// the owner, 2026-10-23, "Rust's sys").
/// </summary>
public sealed class RuntimeTests
{
    private static string RuntimeDirectory => Path.Combine(CorpusTests.Root, "runtime");

    private static RuntimeFile File(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    [Fact]
    public void The_compiler_carries_every_file_of_the_runtime_directory_as_it_is()
    {
        var onDisk = Directory.EnumerateFiles(RuntimeDirectory, "*", SearchOption.AllDirectories)
            .Select(f => new RuntimeFile("runtime/" + Path.GetRelativePath(RuntimeDirectory, f).Replace('\\', '/'), System.IO.File.ReadAllBytes(f)))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["runtime/common/runtime.kz", "runtime/linux/linux.kz", "runtime/windows/windows.kz"], onDisk.Select(f => f.Path));
        Assert.Equal(onDisk.Select(f => f.Path), Runtime.Files.Select(f => f.Path));
        foreach (var (disk, carried) in onDisk.Zip(Runtime.Files))
        {
            Assert.True(disk.Bytes.SequenceEqual(carried.Bytes), $"{disk.Path}: the compiler carries other bytes than the file holds");
        }
    }

    [Fact]
    public void The_runtime_checks_clean_for_every_target_and_common_calls_its_target()
    {
        Assert.Empty(Runtime.CheckEveryTarget());
        foreach (var target in Enum.GetValues<Target>())
        {
            var errors = new List<string>();
            var checkedRuntime = Runtime.Check(target, errors);
            Assert.Empty(errors);
            Assert.NotEmpty(checkedRuntime.CommonChecker.FolderCalls); // the rule of the same functions compares something
            Assert.NotEmpty(checkedRuntime.Common.Statements);
            Assert.NotEmpty(checkedRuntime.Sys.Statements);
        }
    }

    private static List<RuntimeFile> Runtime3(string common, string linux, string windows) =>
    [
        File("runtime/common/a.kz", common),
        File("runtime/linux/a.kz", linux),
        File("runtime/windows/a.kz", windows),
    ];

    private const string Common = "use sys\n\npub long Out() => sys.Write(1)\n";

    [Fact]
    public void A_function_common_calls_with_other_parameters_on_one_target_is_named()
    {
        var same = Runtime.CheckEveryTarget(Runtime3(Common, "pub long Write(int fd) => 0\n", "pub long Write(int fd) => 0\n"));
        Assert.Empty(same); // the positive control: the same signature on both targets

        var errors = Runtime.CheckEveryTarget(Runtime3(Common, "pub long Write(int fd) => 0\n", "pub long Write(long fd) => 0\n"));
        Assert.Contains(errors, e => e.Contains("runtime/windows/", StringComparison.Ordinal) && e.Contains("long Write(int)", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("runtime/linux/", StringComparison.Ordinal) && e.Contains("long Write(long)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_function_common_calls_with_another_result_or_a_mut_parameter_on_one_target_is_named()
    {
        // the result: both targets' checks accept the call, and only the comparison tells them apart
        var result = Runtime.CheckEveryTarget(Runtime3(Common, "pub long Write(int fd) => 0\n", "pub int Write(int fd) => 0\n"));
        Assert.Equal(
            [
                "runtime/windows/: `common/` calls `long Write(int)` of runtime/linux/, which this directory does not give with the same parameters and result",
                "runtime/linux/: `common/` calls `int Write(int)` of runtime/windows/, which this directory does not give with the same parameters and result",
            ],
            result);

        // a `mut` parameter: the target without it refuses the call in its own check, and the comparison names the signature it lacks
        var mutated = Runtime.CheckEveryTarget(Runtime3(
            "use sys\n\npub long Out() {\n    mut n = 1\n    return sys.Write(mut n)\n}\n",
            "pub long Write(mut int fd) => 0\n",
            "pub long Write(int fd) => 0\n"));
        Assert.Contains("runtime/windows/: `common/` calls `long Write(mut int)` of runtime/linux/, which this directory does not give with the same parameters and result", mutated);
        Assert.Contains(mutated, e => e.StartsWith("runtime/common/a.kz:5: ", StringComparison.Ordinal) && e.EndsWith(" (in the check for windows)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_helper_only_its_own_directory_calls_differs_between_targets_freely()
    {
        Assert.Empty(Runtime.CheckEveryTarget(Runtime3(
            Common,
            "pub long Write(int fd) => Twice(fd)\n\nlong Twice(int n) => long(n) * 2\n",
            "pub long Write(int fd) => 0\n")));
    }

    [Fact]
    public void A_function_a_target_lacks_is_named_at_the_call_of_common()
    {
        var errors = Runtime.CheckEveryTarget(Runtime3(Common, "pub long Write(int fd) => 0\n", "pub long Put(int fd) => 0\n"));
        Assert.Contains(errors, e => e.StartsWith("runtime/common/a.kz:3: unknown-name:", StringComparison.Ordinal) && e.EndsWith(" (in the check for windows)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_defect_of_common_that_every_target_reports_stands_once()
    {
        var errors = Runtime.CheckEveryTarget(Runtime3(Common + "print(q)\n", "pub long Write(int fd) => 0\n", "pub long Write(int fd) => 0\n"));
        Assert.Equal(errors.Distinct(), errors);
        Assert.Single(errors, e => e.StartsWith("runtime/common/a.kz:4: unknown-name:", StringComparison.Ordinal) && !e.Contains("(in the check for", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("runtime/common/b.kz", "runtime/common/: not supported: a directory of 2 files")] // two files in one directory, which section 8 makes one folder
    [InlineData("runtime/linux/x86-64/a.kz", "runtime/linux/x86-64/a.kz: not supported: a directory inside a target's")] // the directory of an architecture, which round 20 places there
    [InlineData("runtime/common/x86-64/a.kz", "runtime/common/x86-64/a.kz: the runtime is")] // a directory inside common/, which no round places
    [InlineData("runtime/macos/a.kz", "runtime/macos/a.kz: the runtime is")] // a target the compiler does not know
    [InlineData("runtime/a.kz", "runtime/a.kz: the runtime is")] // a file beside the directories
    public void A_file_the_compiler_does_not_read_in_its_place_is_refused_or_not_supported(string path, string error)
    {
        var files = Runtime3(Common, "pub long Write(int fd) => 0\n", "pub long Write(int fd) => 0\n");
        Assert.Empty(Runtime.Layout(files)); // the positive control
        files.Add(File(path, "pub long Write(int fd) => 0\n"));
        Assert.Contains(Runtime.Layout(files), e => e.StartsWith(error, StringComparison.Ordinal));
        Assert.Equal(Runtime.Layout(files), Runtime.CheckEveryTarget(files));
    }

    [Fact]
    public void A_directory_without_a_file_is_refused()
    {
        var files = Runtime3(Common, "pub long Write(int fd) => 0\n", "pub long Write(int fd) => 0\n");
        files.RemoveAt(2);
        Assert.Contains("runtime/windows/ holds 0 files: the compiler reads one file in each directory of the runtime", Runtime.Layout(files));
    }

    // ---- the checker's reading of `use sys` ------------------------------------------------------

    private static Checker Folder(string text, DiagnosticBag diagnostics)
    {
        var tree = Parser.Parse(SourceText.FromString("sys.kz", text), diagnostics);
        Assert.Empty(diagnostics.Items);
        return Checker.Check(tree, diagnostics, ["raw", "native"]);
    }

    private static (ProgramSyntax Tree, Checker Checker, List<Diagnostic> Diagnostics) WithSys(string sys, string text)
    {
        var sysDiagnostics = new DiagnosticBag();
        var folder = Folder(sys, sysDiagnostics);
        Assert.Empty(sysDiagnostics.Items);
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("common.kz", text), diagnostics);
        Assert.Empty(diagnostics.Items);
        var checker = Checker.Check(tree, diagnostics, ["raw", "native"], new Dictionary<string, Checker> { ["sys"] = folder });
        return (tree, checker, diagnostics.Items.ToList());
    }

    [Fact]
    public void A_call_qualified_by_a_folder_a_use_brought_in_picks_its_pub_function()
    {
        var (tree, checker, diagnostics) = WithSys(
            "pub long Write(int fd) => 0\npub long Write(string s) => 1\n",
            "use sys\n\nprint(sys.Write(\"a\"))\n");
        Assert.Empty(diagnostics);
        var call = Assert.IsType<CallExpression>(((CallExpression)((ExpressionStatement)tree.Statements[1]).Expression).Arguments[0].Value);
        var info = checker.Model.CallOf(call);
        Assert.Equal(CallKind.Function, info.Kind);
        Assert.Equal("long Write(string)", Runtime.Signature(info.Function!));
        Assert.Equal(["long Write(string)"], checker.FolderCalls.Select(Runtime.Signature));
    }

    [Theory]
    [InlineData("pub long Write(int fd) => 0\nlong Hidden() => 0\n", "use sys\n\nprint(sys.Hidden())\n", "not-visible", 3)] // F9: not `pub`
    [InlineData("pub long Write(int fd) => 0\n", "use sys\n\nprint(sys.Read())\n", "unknown-name", 3)] // no such function
    [InlineData("pub long Write(int fd) => 0\n", "print(sys.Write(1))\n", "unknown-name", 1)] // no `use`: `sys` is no name here
    public void A_call_of_a_folder_names_what_it_cannot_reach(string sys, string text, string id, int line)
    {
        var (_, _, diagnostics) = WithSys(sys, text);
        var d = Assert.Single(diagnostics);
        Assert.Equal((id, line), (d.Id, d.Line));
    }

    [Fact]
    public void A_use_of_a_folder_the_checker_is_not_given_is_not_supported()
    {
        var (_, _, diagnostics) = WithSys("pub long Write(int fd) => 0\n", "use other\n\nprint(1)\n");
        var d = Assert.Single(diagnostics);
        Assert.Equal((DiagnosticKind.NotSupported, 1), (d.Kind, d.Line));
    }

    [Fact]
    public void A_variable_of_the_folders_name_is_the_variable()
    {
        var (_, _, diagnostics) = WithSys("pub long Write(int fd) => 0\n", "use sys\n\nmut sys = List<int>()\nsys.Add(1)\nprint(sys.Count)\n");
        Assert.Empty(diagnostics);
    }
}
