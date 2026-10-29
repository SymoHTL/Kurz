using System.Runtime.Versioning;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on files that no corpus case pins (chapter 13; design record, section 12): a file closes when the last value that
/// holds it lets go of it, which a process allowed few descriptors shows over many opens, and so do the calls that open a file or a
/// directory for themselves (S5, S6, S8); a copy of a file holds the one file (S5); a read into a list another value holds copies the
/// list first, and a count and a <c>max</c> past the largest <c>int</c> raise <c>allocation-failed</c> before the read (S3); a whole
/// file a list holds is read whole, and one byte more raises <c>allocation-failed</c> (S6); a read of a file opened to write and a write
/// of one opened to read are the system's error 9, a file and a directory take 0666 and 0777 less the <c>umask</c>, and a descriptor is
/// opened close-on-exec (S2, S8); a directory that exists is created again without an error, and one under a file is not (S8); the text
/// of an error without a path (S1); and a build for Windows says <c>not supported</c> at a call on a file (S11). Each program runs under
/// the runtime of this machine and under the fenced allocator of <see cref="ListTests.Fenced"/>, where no other runtime is named; on
/// Windows each is held to the build's refusal instead.
/// </summary>
public sealed class FileTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime) => Builder.Compile(SourceText.FromString("program.kz", text), [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and run with each runtime of <paramref name="runtimes"/>, or with this machine's and the fenced
    /// one, after the shell command <paramref name="setup"/> where that is given, prints <paramref name="lines"/> and exits with
    /// <paramref name="code"/> and <paramref name="error"/> on its error stream: the executables. On Windows the build says
    /// <c>not supported</c> at a call on a file, and there are none.
    /// </summary>
    private List<string> Ran(string text, string name, string[] lines, string? setup = null, int code = 0, string error = "", CheckedRuntime[]? runtimes = null)
    {
        var executables = new List<string>();
        if (Builder.HostTarget == Target.Windows)
        {
            RefusedOnWindows(Compile(text, toolchain.Runtime));
            return executables;
        }

        var each = runtimes is null ? [(toolchain.Runtime, ""), (ListTests.Fenced, "-fenced")] : runtimes.Select((r, i) => (r, i == 0 ? "" : $"-{i}")).ToArray();
        foreach (var (runtime, suffix) in each)
        {
            var compilation = Compile(text, runtime);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var executable = toolchain.Build(compilation.Program, name + suffix);
            var run = setup is null ? Toolchain.Run(executable) : RunAfter(executable, setup);
            Assert.Equal(lines, run.Output);
            Assert.Equal((code, error), (run.Code, run.Error));
            executables.Add(executable);
        }

        return executables;
    }

    /// <summary>A run of the executable as <see cref="Toolchain.Run(string)"/> runs it, after the shell command <paramref name="setup"/>, a limit or a <c>umask</c>.</summary>
    private static (int Code, IReadOnlyList<string> Output, string Error) RunAfter(string executable, string setup)
    {
        var (code, output, error) = Processes.Capture("/bin/sh", ["-c", $"{setup} && exec \"$0\"", executable], Path.GetDirectoryName(executable), TimeSpan.FromSeconds(30), new Dictionary<string, string?> { ["KURZ_TRACE"] = "0" });
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
        Ran(Rounds, "file-rounds", ["200"], setup: "ulimit -n 32");

        // the positive control: the same files, all of them held, use up the descriptors, `EMFILE` (24), a number Rust gives no kind
        var held = Rounds.Replace("    mut round = 0\n", "    mut all = List<File>()\n    mut round = 0\n", StringComparison.Ordinal)
            .Replace("        last = f\n", "        all.Add(f)\n", StringComparison.Ordinal);
        Ran(held, "file-rounds-held", ["other (os error 24): probe.txt"], setup: "ulimit -n 32");
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
    public void A_whole_file_a_list_holds_is_read_whole_and_one_byte_more_raises_allocation_failed()
    {
        // the largest `int` of items lowered to 20 in the library's text, so that a file of 20 bytes is a file the list holds to the byte
        const string whole = """
            void | IoError Whole() {
                f = File.Create("limit.txt")
                f.Write("0123456789".Bytes)
                f.Write("0123456789".Bytes)
                f.Close()
                bytes = File.ReadAll("limit.txt")
                print(bytes.Count)
                File.Delete("limit.txt")
            }

            Whole() else {
                IoError e => print(e)
            }

            """;
        var limited = ListTests.Edited(("std/std.kz", "2147483647", "20"));
        Ran(whole, "file-limit", ["20"], runtimes: [limited]);

        // the positive control: a byte more than the list holds
        Ran(whole.Replace("    f.Close()\n", "    f.Write(\"x\".Bytes)\n    f.Close()\n", StringComparison.Ordinal), "file-limit-past", [], code: 1, error: "error: allocation-failed (21 bytes)\n", runtimes: [limited]);
    }

    [Fact]
    public void A_read_of_a_file_created_to_write_and_a_write_of_one_opened_to_read_are_the_system_s_error_9()
    {
        // each with its positive control: a write of the file created and a read of the file opened
        const string wrong = """
            void | IoError Wrong() {
                w = File.Create("wrong.txt")
                mut buffer = Bytes()
                match w.Read(mut buffer, 1) {
                    int n => print(n)
                    IoError e => print(e.Code)
                }

                w.Write("ab".Bytes)
                r = File.Open("wrong.txt")
                File.Delete("wrong.txt")
                r.Write("x".Bytes) else {
                    IoError e => print(e.Code)
                }

                print(r.Read(mut buffer, 2))
            }

            Wrong() else {
                IoError e => print(e)
            }

            """;
        Ran(wrong, "file-wrong-direction", ["9", "9", "2"]);
    }

    [Fact]
    public void A_file_and_a_directory_take_0666_and_0777_less_the_umask()
    {
        const string made = "File.WriteAll(\"mode.txt\", \"x\".Bytes) else {\n    IoError e => print(e)\n}\n\nDirectory.Create(\"mode-dir\") else {\n    IoError e => print(e)\n}\n";
        var masked = Ran(made, "file-mode-027", [], setup: "umask 027", runtimes: [toolchain.Runtime]);
        var unmasked = Ran(made, "file-mode-0", [], setup: "umask 0", runtimes: [toolchain.Runtime]);
        if (OperatingSystem.IsWindows())
        {
            return; // each build was held to the refusal
        }

        Assert.Equal((Mode("640"), Mode("750")), Modes(Assert.Single(masked)));

        // the positive control: a mask of 0 takes nothing away
        Assert.Equal((Mode("666"), Mode("777")), Modes(Assert.Single(unmasked)));

        static UnixFileMode Mode(string octal) => (UnixFileMode)Convert.ToInt32(octal, 8);

        [UnsupportedOSPlatform("windows")]
        static (UnixFileMode File, UnixFileMode Directory) Modes(string executable)
        {
            var file = Path.Combine(Path.GetDirectoryName(executable)!, "mode.txt");
            var directory = Path.Combine(Path.GetDirectoryName(executable)!, "mode-dir");
            var modes = (File.GetUnixFileMode(file), File.GetUnixFileMode(directory));
            File.Delete(file);
            Directory.Delete(directory);
            return modes;
        }
    }

    [Fact]
    public void A_file_is_opened_close_on_exec()
    {
        // the flags of the first descriptor the program opens, which the system shows in octal, `O_CLOEXEC` 02000000 among them
        const string flags = """
            void | IoError Flags() {
                f = File.Create("cloexec.txt")
                File.Delete("cloexec.txt")
                info = File.ReadAll("/proc/self/fdinfo/3")
                print(string.FromUtf8(info) else {
                    InvalidUtf8 => "not UTF-8"
                })
                f.Close()
            }

            Flags() else {
                IoError e => print(e)
            }

            """;
        Assert.True(CloseOnExec(toolchain.Runtime, "file-cloexec") ?? true);

        // the positive control: the runtime's options without `O_CLOEXEC`
        var without = ListTests.Edited(("runtime/linux/linux.kz", "mut int options = 524288\n", "mut int options = 0\n"), ("runtime/linux/linux.kz", "options = 524288 + 577", "options = 577"));
        Assert.False(CloseOnExec(without, "file-cloexec-without") ?? false);

        bool? CloseOnExec(CheckedRuntime runtime, string name)
        {
            if (Builder.HostTarget == Target.Windows)
            {
                RefusedOnWindows(Compile(flags, toolchain.Runtime));
                return null;
            }

            var compilation = Compile(flags, runtime);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var run = Toolchain.Run(toolchain.Build(compilation.Program, name));
            Assert.Equal((0, ""), (run.Code, run.Error));
            var line = Assert.Single(run.Output, l => l.StartsWith("flags:", StringComparison.Ordinal));
            return (Convert.ToInt64(line["flags:".Length..].Trim(), 8) & 0x80000) != 0;
        }
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
