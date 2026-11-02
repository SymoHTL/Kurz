using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The clang search over a path and a Program Files directory the test makes, with the answers to
/// <c>--version</c> given, and the lines of the command's failure for each outcome (design record,
/// section 2: the places, the pinned major, its refusal and the refusal of Apple's clang, and the
/// words of the failures, shown to the owner on 2026-10-23); and the search over this machine's own
/// environment, which finds the pinned major or fails, on every machine.
/// </summary>
public sealed class ClangTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("kurz-clang-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>A directory under the test's own, holding an empty file of each name given.</summary>
    private string Dir(string name, params string[] files)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }

        return directory;
    }

    private static string PathOf(params string[] directories) => string.Join(Path.PathSeparator, directories);

    private static Func<string, string?> Answers(params (string File, string? Answer)[] answers) =>
        file => answers.FirstOrDefault(a => a.File == file).Answer;

    [Fact]
    public void The_versioned_name_anywhere_on_the_path_comes_before_the_bare_name()
    {
        var a = Dir("a", "clang");
        var b = Dir("b", "clang-23");
        var search = Clang.Find(PathOf(a, b), null, windows: false, Answers(
            (Path.Combine(a, "clang"), "clang version 20.1.2\n"),
            (Path.Combine(b, "clang-23"), "Ubuntu clang version 23.1.4 (++20261020091207+0123456789ab-1~exp1~20261020091224.17)\nTarget: x86_64-pc-linux-gnu\n")));
        Assert.Equal((Path.Combine(b, "clang-23"), "23.1.4"), (search.Found?.Path, search.Found?.Version));
        Assert.Equal(["clang-23 on the path"], search.Looked);
    }

    [Fact]
    public void The_bare_name_is_taken_where_no_directory_of_the_path_holds_the_versioned_one()
    {
        var a = Dir("a", "clang.exe");
        var search = Clang.Find(PathOf(Dir("empty"), a), null, windows: true, Answers((Path.Combine(a, "clang.exe"), "clang version 23.1.4\nTarget: x86_64-pc-windows-msvc\n")));
        Assert.Equal((Path.Combine(a, "clang.exe"), "23.1.4"), (search.Found?.Path, search.Found?.Version));
        Assert.Equal(["clang-23.exe on the path", "clang.exe on the path"], search.Looked);
    }

    [Fact]
    public void The_first_place_that_holds_a_clang_decides_and_its_refusal_ends_the_search()
    {
        var a = Dir("a", "clang-23");
        var b = Dir("b", "clang-23");
        var refused = Path.Combine(a, "clang-23");
        var search = Clang.Find(PathOf(a, b), null, windows: false, Answers((refused, "clang version 20.1.2\n"), (Path.Combine(b, "clang-23"), "clang version 23.1.4\n")));
        Assert.Equal((null, refused, "20.1.2"), (search.Found, search.Place, search.Version));
        Assert.Equal(
            $"error: clang 20.1.2 is not LLVM 23\n  note: found at {refused}\n  help: install LLVM 23, ahead of this clang on the path\n",
            Command.Failure(search));
    }

    [Theory]
    [InlineData(null)] // it did not start, or did not exit with 0
    [InlineData("gcc (GCC) 13.2.0\n")]
    public void A_file_whose_answer_names_no_clang_release_is_refused(string? answer)
    {
        var a = Dir("a", "clang");
        var search = Clang.Find(PathOf(a), null, windows: false, Answers((Path.Combine(a, "clang"), answer)));
        Assert.Equal((null, Path.Combine(a, "clang"), null), (search.Found, search.Place, search.Version));
        Assert.Equal(
            $"error: the clang found names no release in its answer to `--version`\n  note: found at {Path.Combine(a, "clang")}\n  help: install LLVM 23, ahead of this clang on the path\n",
            Command.Failure(search));
    }

    [Fact]
    public void Apple_clang_is_refused_whatever_number_its_answer_names()
    {
        var a = Dir("a", "clang");
        var file = Path.Combine(a, "clang");
        var search = Clang.Find(PathOf(a), null, windows: false, Answers((file, "Apple clang version 23.0.0 (clang-2300.0.13.3)\nTarget: arm64-apple-darwin25.0.0\n")));
        Assert.Equal((null, file, null, true), (search.Found, search.Place, search.Version, search.Apple));
        Assert.Equal(
            $"error: the clang found is Apple clang, whose version names no LLVM release\n  note: found at {file}\n  help: install LLVM 23, ahead of this clang on the path\n",
            Command.Failure(search));
    }

    [Fact]
    public void A_search_that_finds_nothing_names_every_place_it_looked_at()
    {
        var empty = Dir("empty");
        var programFiles = Dir("program-files");
        var elsewhere = Clang.Find(PathOf(empty), programFiles, windows: false, Answers());
        Assert.Equal((null, null), (elsewhere.Found, elsewhere.Place));
        Assert.Equal("error: no clang found\n  note: looked for clang-23 and clang on the path\n  help: install LLVM 23\n", Command.Failure(elsewhere));
        var windows = Clang.Find(PathOf(empty), programFiles, windows: true, Answers());
        Assert.Equal(["clang-23.exe on the path", "clang.exe on the path", Path.Combine(programFiles, "LLVM", "bin", "clang.exe")], windows.Looked);
        Assert.Equal(
            $"error: no clang found\n  note: looked for clang-23.exe and clang.exe on the path and in the LLVM installer directory, {Path.Combine(programFiles, "LLVM", "bin")}\n  help: install LLVM 23\n",
            Command.Failure(windows));
    }

    [Fact]
    public void On_Windows_the_installer_directory_is_tried_after_the_path_and_elsewhere_never()
    {
        var programFiles = Dir("program-files", Path.Combine("LLVM", "bin", "clang.exe"));
        var installed = Path.Combine(programFiles, "LLVM", "bin", "clang.exe");
        var windows = Clang.Find(PathOf(Dir("empty")), programFiles, windows: true, Answers((installed, "clang version 23.1.4\n")));
        Assert.Equal(installed, windows.Found?.Path);
        var elsewhere = Clang.Find(PathOf(Dir("empty")), programFiles, windows: false, Answers((installed, "clang version 23.1.4\n")));
        Assert.Equal((null, null), (elsewhere.Found, elsewhere.Place));
        var refused = Clang.Find(PathOf(Dir("empty")), programFiles, windows: true, Answers((installed, "clang version 22.1.0\n")));
        Assert.Equal(
            $"error: clang 22.1.0 is not LLVM 23\n  note: found at {installed}\n  help: install LLVM 23\n",
            Command.Failure(refused));
    }

    [Theory]
    [InlineData("clang version 23.1.4\nTarget: x86_64-pc-windows-msvc\n", "23.1.4")]
    [InlineData("Ubuntu clang version 23.1.4 (++20261020091207+0123456789ab-1~exp1~20261020091224.17)\n", "23.1.4")]
    [InlineData("Apple clang version 17.0.0 (clang-1700.0.13.3)\n", null)] // Apple's own number, built from LLVM 19
    [InlineData("gcc (GCC) 13.2.0\n", null)]
    [InlineData("", null)]
    public void The_release_is_read_from_the_answer_as_each_distribution_words_it(string answer, string? release) =>
        Assert.Equal(release, Clang.ReadVersion(answer));

    [Fact]
    public void A_cpu_is_described_by_the_classes_whose_macros_clang_defines_for_it_and_the_features_its_driver_passes()
    {
        // section 2 and R18: x86-64 has the baseline's classes, x86-64-v2 adds SSE3 to SSE4.2 and no SHA, goldmont SHA and AES and no
        // AVX, native this machine's, with the features the driver passes for it; a CPU clang does not know is null, with the target
        // clang builds for
        var clang = Clang.Find().Found ?? throw new InvalidOperationException("no clang of the pinned major is found");
        var baseline = clang.DescribeCpu("x86-64", x86: true, out var target)!;
        Assert.Equal(Cpu.Baseline.Classes, baseline.Classes);
        Assert.Equal("x86-64", baseline.Name);
        Assert.StartsWith("x86_64-", target, StringComparison.Ordinal);
        var v2 = clang.DescribeCpu("x86-64-v2", x86: true, out _)!;
        Assert.Equal([.. Cpu.Baseline.Classes, "X86.Sse3", "X86.Ssse3", "X86.Sse41", "X86.Sse42", "X86.Popcnt"], v2.Classes);
        Assert.Empty(v2.Features); // the driver passes the CPU's name alone, which carries its features to LLVM
        var goldmont = clang.DescribeCpu("goldmont", x86: true, out _)!;
        Assert.Contains("X86.Sha", goldmont.Classes);
        Assert.Contains("X86.Aes", goldmont.Classes);
        Assert.DoesNotContain("X86.Avx", goldmont.Classes);
        var native = clang.DescribeCpu("native", x86: true, out _)!;
        var sha = (System.Runtime.Intrinsics.X86.X86Base.CpuId(7, 0).Ebx & (1 << 29)) != 0;
        Assert.Equal(
            (System.Runtime.Intrinsics.X86.Sse41.IsSupported, System.Runtime.Intrinsics.X86.Aes.IsSupported, sha),
            (native.Classes.Contains("X86.Sse41"), native.Classes.Contains("X86.Aes"), native.Classes.Contains("X86.Sha")));
        Assert.Equal(sha, native.Features.Contains("+sha")); // for `native` the driver passes the machine's features, each by its name
        Assert.Null(clang.DescribeCpu("pentium9", x86: true, out var unknown));
        Assert.Equal(target, unknown);
        Assert.Null(clang.DescribeCpu("cortex-a72", x86: true, out var other)); // a CPU of another architecture, unknown for x86-64 (section 2)
        Assert.Equal(target, other);
    }

    [Fact]
    public void This_machine_has_the_clang_of_the_pinned_major()
    {
        // the record's requirement on every machine that builds: a machine without it fails here, with the command's own lines
        var search = Clang.Find();
        Assert.True(search.Found is not null, Command.Failure(search));
        Assert.StartsWith("23.", search.Found!.Version, StringComparison.Ordinal);
    }
}
