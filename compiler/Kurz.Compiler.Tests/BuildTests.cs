using Kurz.Compiler.Build;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The pieces of <c>Build/</c> on their own: the clang search over the path and the Program Files
/// directory it is given, and the environment a captured process gets (<c>compiler/README.md</c>,
/// the toolchain).
/// </summary>
public sealed class BuildTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    [Fact]
    public void The_search_finds_nothing_on_a_path_without_a_clang_and_names_every_place_it_looked_at()
    {
        var empty = Path.Combine(toolchain.Directory, "build-no-clang");
        Directory.CreateDirectory(empty);
        var found = Clang.Find(empty, empty, out var looked);
        Assert.True(found is null, $"a clang was found with the path and the Program Files directory emptied: {found?.Path}; looked at {string.Join(", ", looked)}");
        var bare = OperatingSystem.IsWindows() ? "clang.exe" : "clang";
        string[] expected = OperatingSystem.IsWindows()
            ? [$"{bare} on the path", Path.Combine(empty, "LLVM", "bin", "clang.exe")]
            : [$"{bare} on the path"];
        Assert.Equal(expected, looked);
        // the positive control: the search over the environment finds the clang the suite builds with
        Assert.NotNull(Clang.Find(out _));
    }

    [Fact]
    public void A_captured_process_gets_the_variables_it_is_given_in_place_of_its_own()
    {
        var value = Path.Combine(toolchain.Directory, "build-path");
        Directory.CreateDirectory(value);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = value };
        var (code, output, error) = OperatingSystem.IsWindows()
            ? Processes.Capture(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", "echo %PATH%"], null, TimeSpan.FromMinutes(1), environment)
            : Processes.Capture("/bin/sh", ["-c", "printf %s \"$PATH\""], null, TimeSpan.FromMinutes(1), environment);
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(value, output.Trim());
    }
}
