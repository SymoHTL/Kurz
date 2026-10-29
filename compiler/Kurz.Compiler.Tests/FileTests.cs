using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on files that no corpus case pins (chapter 13; design record, section 12): a file closes when the last value that
/// holds it lets go of it, which a process allowed few descriptors shows over many opens, and so do the calls that open a file or a
/// directory for themselves (S5, S6, S8); a copy of a file holds the one file (S5); a read into a list another value holds copies the
/// list first, and a count and a <c>max</c> past the largest <c>int</c> raise <c>allocation-failed</c> before the read (S3); a directory
/// that exists is created again without an error, and one under a file is not (S8); the text of an error without a path (S1); and a
/// build for Windows says <c>not supported</c> at a call on a file (S11). Each program runs under the runtime of this machine and under
/// the fenced allocator of <see cref="ListTests.Fenced"/>; on Windows each is held to the build's refusal instead.
/// </summary>
public sealed class FileTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and run with each runtime, under at most <paramref name="descriptors"/> open descriptors where
    /// that is given, prints <paramref name="lines"/> and exits with <paramref name="code"/> and <paramref name="error"/> on its error
    /// stream; on Windows the build says <c>not supported</c> at a call on a file.
    /// </summary>
    private void Ran(string text, string name, string[] lines, int? descriptors = null, int code = 0, string error = "")
    {
        if (Builder.HostTarget == Target.Windows)
        {
            RefusedOnWindows(Compile(text, toolchain.Runtime));
            return;
        }

        foreach (var (runtime, suffix) in new[] { (toolchain.Runtime, ""), (ListTests.Fenced, "-fenced") })
        {
            var compilation = Compile(text, runtime);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var executable = toolchain.Build(compilation.Program, name + suffix);
            var run = descriptors is null ? Toolchain.Run(executable) : RunLimited(executable, descriptors.Value);
            Assert.Equal(lines, run.Output);
            Assert.Equal((code, error), (run.Code, run.Error));
        }
    }

    /// <summary>A run of the executable as <see cref="Toolchain.Run(string)"/> runs it, with the shell's limit on the descriptors the process may hold open.</summary>
    private static (int Code, IReadOnlyList<string> Output, string Error) RunLimited(string executable, int descriptors)
    {
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"ulimit -n {descriptors} && exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" });
        return (code, Toolchain.Lines(output), error);
    }

    private static void RefusedOnWindows(Compilation compilation)
    {
        Assert.Null(compilation.Program);
        Assert.Equal(Emitter.NotOnWindows, Assert.Single(compilation.Diagnostics.Items).Message);
    }

    private const string Rounds = """
        void | IoError Rounds() {
            File.WriteAll("probe.txt", "x".Bytes)
            Directory.Create("probe")
            mut last = File.Open("probe.txt")
            mut round = 0
            while round < 200 {
                round = round + 1
                f = File.Open("probe.txt")
                last = f
                File.WriteAll("probe.txt", File.ReadAll("probe.txt"))
                Directory.List("probe")
            }

            last.Close()
            print(round)
            File.Delete("probe.txt")
            Directory.Delete("probe")
        }

        Rounds() else {
            IoError e => print(e)
        }

        """;

    [Fact]
    public void A_file_closes_when_the_last_value_that_holds_it_lets_go_of_it()
    {
        // 200 opens of a file, each copied into a variable that lets go of the one before, and of the files and the directory the
        // whole-file calls and the list open for themselves, under 32 descriptors
        Ran(Rounds, "file-rounds", ["200"], descriptors: 32);

        // the positive control: the same files, all of them held, use up the descriptors, `EMFILE` (24), a number Rust gives no kind
        var held = Rounds.Replace("    mut round = 0\n", "    mut all = List<File>()\n    mut round = 0\n", StringComparison.Ordinal)
            .Replace("        last = f\n", "        all.Add(f)\n", StringComparison.Ordinal);
        Ran(held, "file-rounds-held", ["other: probe.txt"], descriptors: 32);
    }

    private const string Shared = """
        void | IoError Shared() {
            f = File.Create("shared.txt")
            g = f
            f.Close()
            File.Delete("shared.txt")
            g.Write("x".Bytes) else {
                IoError e => print(e.Code)
            }

            print("end")
        }

        Shared() else {
            IoError e => print(e)
        }

        """;

    [Fact]
    public void A_copy_of_a_file_holds_the_one_file_so_a_close_through_one_closes_it_for_both()
    {
        Ran(Shared, "file-shared", ["9", "end"]); // `EBADF`, the error of a descriptor that is not open

        // the positive control: without the close the copy writes
        Ran(Shared.Replace("    f.Close()\n", "", StringComparison.Ordinal), "file-shared-open", ["end"]);
    }

    [Fact]
    public void A_read_into_a_list_another_value_holds_copies_the_list_first()
    {
        const string copied = """
            void | IoError Copied() {
                File.WriteAll("copied.txt", "abc".Bytes)
                f = File.Open("copied.txt")
                File.Delete("copied.txt")
                mut xs = "zz".Bytes
                ys = xs
                f.Read(mut xs, 3)
                print(xs.Count)
                print(ys.Count)
                print(string.FromUtf8(xs) else {
                    InvalidUtf8 => "not UTF-8"
                })
            }

            Copied() else {
                IoError e => print(e)
            }

            """;
        Ran(copied, "file-read-shared", ["5", "2", "zzabc"]);
    }

    [Fact]
    public void A_count_and_a_max_past_the_largest_int_raise_allocation_failed_before_the_read()
    {
        const string past = """
            void | IoError Past() {
                File.WriteAll("past.txt", "abc".Bytes)
                f = File.Open("past.txt")
                File.Delete("past.txt")
                mut xs = "a".Bytes
                print("before")
                f.Read(mut xs, 2147483647)
                print("after")
            }

            Past() else {
                IoError e => print(e)
            }

            """;
        Ran(past, "file-read-past", ["before"], code: 1, error: "error: allocation-failed (2147483648 bytes)\n");

        // the positive control: one byte less is room the list can hold, which the read asks of the system, and three bytes are read
        Ran(past.Replace("mut xs = \"a\".Bytes", "mut xs = Bytes()", StringComparison.Ordinal).Replace("f.Read(mut xs, 2147483647)", "print(f.Read(mut xs, 3))", StringComparison.Ordinal), "file-read-fits", ["before", "3", "after"]);
    }

    [Fact]
    public void A_directory_that_exists_is_created_again_and_one_under_a_file_is_not()
    {
        const string again = """
            void | IoError Again() {
                Directory.Create("again/inner")
                Directory.Create("again/inner")
                Directory.Create("again//inner/")
                print(File.Exists("again/inner"))
                File.WriteAll("blocked", "x".Bytes)
                Directory.Create("blocked/inner") else {
                    IoError e => print(e.Kind)
                }

                File.Delete("blocked")
                Directory.Delete("again/inner")
                Directory.Delete("again")
            }

            Again() else {
                IoError e => print(e)
            }

            """;
        Ran(again, "file-directory-again", ["true", "NotADirectory"]);
    }

    [Fact]
    public void The_text_of_an_error_without_a_path_is_the_words_of_its_kind()
    {
        Ran("match File.Open(\"\") {\n    File => print(\"opened\")\n    IoError e => print(e)\n}\n", "file-empty-path", ["not found"]);
    }

    [Fact]
    public void A_build_for_windows_says_not_supported_at_a_call_on_a_file_and_one_for_linux_builds_it()
    {
        const string text = "File.Delete(\"gone.txt\") else {\n    IoError e => print(e)\n}\n";
        RefusedOnWindows(Compile(text, Builder.CheckRuntime(Target.Windows)));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }
}
