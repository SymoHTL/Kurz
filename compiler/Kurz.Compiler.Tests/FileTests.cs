using System.Runtime.Versioning;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The decisions on files and sockets that no corpus case pins (chapter 13; design record, section 12): a file closes when the last value that
/// holds it lets go of it, which a process allowed few descriptors shows over many opens, and so do the calls that open a file or a
/// directory for themselves (S5, S6, S8); a copy of a file holds the one file (S5); a read into a list another value holds copies the
/// list first, and a count and a <c>max</c> past the largest <c>int</c> raise <c>allocation-failed</c> before the read (S3); a whole
/// file a list holds is read whole, and one byte more raises <c>allocation-failed</c>, and a whole file is read into room for the size
/// the system names for it without its list grown, and whole where that size is short, long or a failure, the room it asks for at
/// once is what a refusal names, and the list has room for the file's size (S6); a read of a file opened to write and a write
/// of one opened to read are the system's error 9, a file and a directory take 0666 and 0777 less the <c>umask</c>, and a descriptor is
/// opened close-on-exec (S2, S8); a directory that exists is created again without an error, and one under a file is not (S8); the text
/// of an error without a path (S1); a socket is opened close-on-exec, a listener reuses its address, an address is read at its last
/// colon, a literal of IPv6 binds where the machine has its loopback, and a connection <c>Accept</c> gave names its listener's address
/// (S9), and a lookup that fails with <c>EAI_AGAIN</c> or <c>EAI_FAIL</c>, or with <c>EAI_SYSTEM</c> while <c>errno</c> holds 0, is the kind
/// <c>Other</c>, and one that fails with <c>EAI_SYSTEM</c> while <c>errno</c> holds 13 the error of that number, each through a runtime
/// whose lookup gives that result; a stream reads and writes within 5 seconds by default and a listener waits without a bound, a setter
/// sets the time as Rust's <c>set_timeout</c> sets it, read back by <c>getsockopt</c>, the first form of <c>Connect</c> waits the default
/// bound, one bound holds over every address of a name, and <c>EAGAIN</c> is <c>TimedOut</c> on a stream's read and write alone (S9);
/// and a build for Windows says
/// <c>not supported</c> at a call on a file or a socket (S11). Each program runs under
/// the runtime of this machine and under the fenced allocator of <see cref="ListTests.Fenced"/>, where no other runtime is named; on
/// Windows each is held to the build's refusal instead.
/// </summary>
public sealed class FileTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static Compilation Compile(string text, CheckedRuntime runtime, IReadOnlyCollection<string>? grants = null) => Builder.Compile(SourceText.FromString("program.kz", text), grants ?? [], runtime, testBuild: true);

    /// <summary>
    /// The program built for this machine and run with each runtime of <paramref name="runtimes"/>, or with this machine's and the fenced
    /// one, after the shell command <paramref name="setup"/> where that is given, prints <paramref name="lines"/> and exits with
    /// <paramref name="code"/> and <paramref name="error"/> on its error stream: the executables. On Windows the build says
    /// <c>not supported</c> at a call on a file, and there are none. A program built with <paramref name="grants"/> is given them.
    /// </summary>
    private List<string> Ran(string text, string name, string[] lines, string? setup = null, int code = 0, string error = "", CheckedRuntime[]? runtimes = null, IReadOnlyCollection<string>? grants = null)
    {
        var executables = new List<string>();
        if (Builder.HostTarget == Target.Windows)
        {
            RefusedOnWindows(Compile(text, toolchain.Runtime, grants));
            return executables;
        }

        var each = runtimes is null ? [(toolchain.Runtime, ""), (ListTests.Fenced, "-fenced")] : runtimes.Select((r, i) => (r, i == 0 ? "" : $"-{i}")).ToArray();
        foreach (var (runtime, suffix) in each)
        {
            var compilation = Compile(text, runtime, grants);
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

        // five bytes more: the room made for the size the system names is no more than the list holds, so the read takes the 20 bytes
        // and the byte after them, and the error names the 21 bytes
        Ran(whole.Replace("    f.Close()\n", "    f.Write(\"vwxyz\".Bytes)\n    f.Close()\n", StringComparison.Ordinal), "file-limit-five-past", [], code: 1, error: "error: allocation-failed (21 bytes)\n", runtimes: [limited]);

        // a file the system names no size for, `/proc/self/stat`, of a few hundred bytes: its pieces stop at the 20 bytes the list holds,
        // and the byte after them raises the same error
        const string unsized = """
            void | IoError Unsized() {
                bytes = File.ReadAll("/proc/self/stat")
                print(bytes.Count)
            }

            Unsized() else {
                IoError e => print(e)
            }

            """;
        Ran(unsized, "file-limit-unsized", [], code: 1, error: "error: allocation-failed (21 bytes)\n", runtimes: [limited]);
    }

    [Fact]
    public void A_whole_file_whose_room_the_system_refuses_raises_allocation_failed_with_the_bytes_of_that_one_request()
    {
        // M9: the system of this runtime refuses every mapping of more than a mebibyte; a file of 1572864 bytes, which the shell writes
        // before the run, asks the allocator for its size and the 16 bytes of the list's counts at once, 1572880 bytes, which it refuses
        const string whole = """
            void | IoError Whole() {
                bytes = File.ReadAll("refused.bin")
                print(bytes.Count)
            }

            Whole() else {
                IoError e => print(e)
            }

            """;
        var small = ListTests.Edited(("runtime/linux/linux.kz", "pub byte* Memory(long bytes) {\n", "pub byte* Memory(long bytes) {\n    if bytes > 1048576 {\n        return null\n    }\n\n"));
        Ran(whole, "file-refused", [], setup: "head -c 1572864 /dev/zero > refused.bin", code: 1, error: "error: allocation-failed (1572880 bytes)\n", runtimes: [small]);

        // a file of more bytes than a list holds, with the largest `int` lowered to 1200000 in the library's text: the room the read asks
        // for is the 1200000 bytes the list holds and the 16 bytes of its counts, 1200016 bytes, which the same system refuses
        var smallLimited = ListTests.Edited(("std/std.kz", "2147483647", "1200000"), ("runtime/linux/linux.kz", "pub byte* Memory(long bytes) {\n", "pub byte* Memory(long bytes) {\n    if bytes > 1048576 {\n        return null\n    }\n\n"));
        Ran(whole, "file-refused-past", [], setup: "head -c 2097152 /dev/zero > refused.bin", code: 1, error: "error: allocation-failed (1200016 bytes)\n", runtimes: [smallLimited]);

        // the positive control: a file of 1048000 bytes, whose room the system gives
        Ran(whole, "file-granted", ["1048000"], setup: "head -c 1048000 /dev/zero > refused.bin", runtimes: [small]);
    }

    [Fact]
    public void The_list_a_whole_file_is_read_into_has_room_for_its_size_and_8_bytes_at_least()
    {
        // the room made for the size: an `Add` to the list of a file of 100003 bytes grows it, as one to Rust's `Vec` after
        // `try_reserve_exact` does, and five to the list of a file of 3 bytes do not, since a list of bytes takes 8 bytes of room at least,
        // as `Grown` gives it, where Rust's `try_reserve_exact` gives the 3 alone; under the library that ends the process with the exit
        // code 70 where an `Add` would grow a block a list has; the files are written by the shell, since a list made by `Add`s grows
        const string room = """
            void | IoError Room() {
                mut small = File.ReadAll("small.bin")
                print(small.Count)
                for i in 0..<5 {
                    small.Add(byte(i))
                }

                print(small.Count)
                mut large = File.ReadAll("large.bin")
                print(large.Count)
                large.Add(byte(1))
                print(large.Count)
            }

            Room() else {
                IoError e => print(e)
            }

            """;
        const string files = "head -c 3 /dev/zero > small.bin && head -c 100003 /dev/zero > large.bin";
        var noAddGrows = ListTests.Edited(("std/std.kz", "    long grown = Grown(room, item)\n    long bytes = 16 + grown * item\n    byte* p = Reallocate(", "    sys.Exit(70)\n    long grown = Grown(room, item)\n    long bytes = 16 + grown * item\n    byte* p = Reallocate("));
        Ran(room, "file-room-ended", ["3", "8", "100003"], setup: files, code: 70, runtimes: [noAddGrows]);

        // the positive control: the same program under the runtime
        Ran(room, "file-room", ["3", "8", "100003", "100004"], setup: files, runtimes: [toolchain.Runtime]);
    }

    /// <summary>The library whose read into a list of bytes ends the process with the exit code 70 where it would grow a block the list has (S3, S6).</summary>
    private static CheckedRuntime NoReadGrows => ListTests.Edited(("std/std.kz", "    long size = 16 + grown\n", "    if block != null {\n        sys.Exit(70)\n    }\n\n    long size = 16 + grown\n"));

    private const string Sizes = """
        void | IoError Sizes() {
            for n in [1, 7, 8, 9, 11, 8191, 8192, 8193, 100003, 1048579] {
                mut b = Bytes()
                for i in 0..<n {
                    b.Add(byte(i % 251))
                }

                File.WriteAll("sized.bin", b)
                back = File.ReadAll("sized.bin")
                print("{back.Count} {back == b}")
            }

            File.Delete("sized.bin")
            stat = File.ReadAll("/proc/self/stat")
            print(stat.Count > 0)
        }

        Sizes() else {
            IoError e => print(e)
        }

        """;

    private static readonly string[] SizesRead = ["1 true", "7 true", "8 true", "9 true", "11 true", "8191 true", "8192 true", "8193 true", "100003 true", "1048579 true", "true"];

    [Fact]
    public void A_whole_file_is_read_into_room_for_the_size_the_system_names_for_it_and_its_list_never_grows()
    {
        // S6, as Rust's `fs::read`: the room for the file's size is made before the first read, and a list full at that size reads one
        // byte, which tells the end of the file, so that no list grows, under the library that ends the process where one would; a file
        // the system names no size for, `/proc/self/stat`, takes 8192 bytes of room first, which its few hundred bytes do not fill
        Ran(Sizes, "file-sizes", SizesRead, runtimes: [toolchain.Runtime, ListTests.Fenced, NoReadGrows]);

        // the positive control: a read that grows the list it reads into ends there under that library
        const string grows = """
            void | IoError Grows() {
                File.WriteAll("grows.bin", "abcdefghij".Bytes)
                f = File.Open("grows.bin")
                File.Delete("grows.bin")
                mut buffer = Bytes()
                print(f.Read(mut buffer, 8))
                print(f.Read(mut buffer, 2))
                f.Close()
            }

            Grows() else {
                IoError e => print(e)
            }

            """;
        Ran(grows, "file-read-grows", ["8", "2"], runtimes: [toolchain.Runtime]);
        Ran(grows, "file-read-grows-ended", ["8"], code: 70, runtimes: [NoReadGrows]);
    }

    [Fact]
    public void A_whole_file_is_read_whole_where_the_size_the_system_names_is_short_long_or_a_failure()
    {
        // the sizes the system names 3 bytes short and 3 bytes long: a list full at the short size reads one byte, which the file has, adds
        // it and grows on, as Rust's `read_to_end` appends its probe and grows its `Vec`; a list made for the long size reads what the file
        // has; and a size the system fails to give, the error 9, is no size, as Rust's `fs::read` takes `metadata().ok()`: the files of the
        // sizes above, which every one of them reads byte for byte
        static CheckedRuntime Named(string by) => ListTests.Edited(("runtime/linux/linux.kz", "                result = *Ptr<long>(status + 48)\n", $"                result = *Ptr<long>(status + 48) {by}\n"));
        var failing = ListTests.Edited(("runtime/linux/linux.kz", "            result = Checked(long(fstat(fd, Ptr<void>(status))))\n", "            result = -9\n"));
        Ran(Sizes, "file-sizes-named", SizesRead, runtimes: [Named("- 3"), Named("+ 3"), failing]);
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

    // ---- sockets (S9) ---------------------------------------------------------------------------------

    [Fact]
    public void A_socket_is_opened_close_on_exec()
    {
        // the flags of the listener, the connected stream and the accepted one, the first three descriptors the program opens
        const string flags = """
            void | IoError Flags() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                mut n = 3
                while n <= 5 {
                    info = File.ReadAll("/proc/self/fdinfo/{n}")
                    print(string.FromUtf8(info) else {
                        InvalidUtf8 => "not UTF-8"
                    })
                    n = n + 1
                }

                client.Close()
                server.Close()
            }

            Flags() else {
                IoError e => print(e)
            }

            """;
        if (CloseOnExec(toolchain.Runtime, "socket-cloexec") is { } with) // null on Windows, where the build refuses the program
        {
            Assert.Equal([true, true, true], with);
        }

        // the positive control: the runtime's socket and accept without `SOCK_CLOEXEC`
        var without = ListTests.Edited(("runtime/linux/linux.kz", "int fd = socket(family, 524289 + blocking, 0)", "int fd = socket(family, 1 + blocking, 0)"), ("runtime/linux/linux.kz", "accept4(fd, null, null, 524288)", "accept4(fd, null, null, 0)"));
        if (CloseOnExec(without, "socket-cloexec-without") is { } withoutFlag)
        {
            Assert.Equal([false, false, false], withoutFlag);
        }

        List<bool>? CloseOnExec(CheckedRuntime runtime, string name)
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
            return run.Output.Where(l => l.StartsWith("flags:", StringComparison.Ordinal)).Select(l => (Convert.ToInt64(l["flags:".Length..].Trim(), 8) & 0x80000) != 0).ToList();
        }
    }

    private const string Rebind = """
        void | IoError Rebind() {
            listener = TcpListener.Bind("127.0.0.1:0")
            port = listener.Port
            client = TcpStream.Connect("127.0.0.1:{port}")
            server = listener.Accept()
            server.Close()
            client.Close()
            listener.Close()
            again = TcpListener.Bind("127.0.0.1:{port}")
            print(again.Port == port)
        }

        Rebind() else {
            IoError e => print(e.Kind)
        }

        """;

    [Fact]
    public void A_listener_binds_a_port_whose_closed_connection_waits_since_it_reuses_its_address()
    {
        // the side that closes first keeps the connection's port waiting (`TIME_WAIT`), which `SO_REUSEADDR` binds again (S9)
        Ran(Rebind, "socket-rebind", ["true"]);

        // the positive control: the option set to 0
        var without = ListTests.Edited(("runtime/linux/linux.kz", "*Ptr<int>(held + 36) = 1", "*Ptr<int>(held + 36) = 0"));
        Ran(Rebind, "socket-rebind-without", ["AddressInUse"], runtimes: [without]);
    }

    [Fact]
    public void An_address_is_a_host_and_a_port_after_its_last_colon_and_a_lookup_that_finds_no_host_is_not_found()
    {
        // a port as Rust reads a `u16`, `+` and zeros in front taken; a host that is no literal looked up, `127.1` by the C library's
        // reading of a number, the empty one failing with `EAI_NONAME`, which names no error of the system; a NUL byte `InvalidInput`, as in a path (S9)
        const string text = """
            void Try(string address) {
                match TcpListener.Bind(address) {
                    TcpListener => print("bound")
                    IoError e => print("{e.Kind} {e.Code}")
                }
            }

            Try("127.0.0.1:+0")
            Try("127.0.0.1:00000")
            Try("127.1:0")
            Try("127.0.0.1:")
            Try("127.0.0.1:-1")
            Try("127.0.0.1:0x10")
            Try("[::1]:x")
            Try(":0")
            Try("127.0.0.1:0\0")

            """;
        Ran(text, "socket-addresses", ["bound", "bound", "bound", "InvalidInput 0", "InvalidInput 0", "InvalidInput 0", "InvalidInput 0", "NotFound 0", "InvalidInput 0"]);
    }

    [Fact]
    public void A_lookup_that_fails_otherwise_than_finding_no_host_is_the_kind_Other_or_the_error_of_the_number_in_errno()
    {
        // a lookup that fails with `EAI_AGAIN` (-3), a failure the resolver calls temporary, or `EAI_FAIL` (-4), one of the name server,
        // which no name server gives on demand, through a runtime whose lookup gives that result: `Other`, with the number 0 (S9)
        const string lookup = "int looked = getaddrinfo(text, null, Ptr<void>(held + 64), Ptr<void>(held + 112))";
        const string failed = "match TcpListener.Bind(\"name.invalid:0\") {\n    TcpListener => print(\"bound\")\n    IoError e => print(\"{e.Kind} {e.Code}\")\n}\n";
        foreach (var (result, name) in new[] { ("-3", "again"), ("-4", "fail") })
        {
            var failing = ListTests.Edited(("runtime/linux/linux.kz", lookup, $"int looked = {result}"));
            Ran(failed, $"socket-lookup-{name}", ["Other 0"], runtimes: [failing]);
        }

        // a lookup that fails with `EAI_SYSTEM` (-11) while `errno` holds 0: the error of the number 0, `Other`, as S9 gives such a
        // lookup the number in `errno` and S1 gives `Other` to a number of no kind, where the 0 was read as a descriptor, the bind's
        // check of it closing standard input and the connection made on it
        var system = ListTests.Edited(("runtime/linux/linux.kz", lookup, "*__errno_location() = 0\n            int looked = -11"));
        const string both = failed + "match TcpStream.Connect(\"name.invalid:80\") {\n    TcpStream => print(\"connected\")\n    IoError e => print(\"{e.Kind} {e.Code}\")\n}\n";
        Ran(both, "socket-lookup-system", ["Other 0", "Other 0"], runtimes: [system]);

        // with `errno` holding 13, `EACCES`: the error of that number, `PermissionDenied`, as S1 gives it after Rust's `decode_error_kind`
        var denied = ListTests.Edited(("runtime/linux/linux.kz", lookup, "*__errno_location() = 13\n            int looked = -11"));
        Ran(both, "socket-lookup-errno", ["PermissionDenied 13", "PermissionDenied 13"], runtimes: [denied]);
    }

    /// <summary>
    /// What a bind on IPv6's loopback gives on this machine: the line of a program that bound it, the system's error where the machine has
    /// IPv6 and not that address, `EADDRNOTAVAIL` (99), or where the kernel has no IPv6 at all and no <c>/proc/net/if_inet6</c>, the
    /// error of the socket, `EAFNOSUPPORT` (97), to which Rust gives no kind.
    /// </summary>
    private static string Loopback6(string bound)
    {
        var inet6 = File.Exists("/proc/net/if_inet6");
        return inet6 && File.ReadAllText("/proc/net/if_inet6").Contains("00000000000000000000000000000001", StringComparison.Ordinal) ? bound
            : inet6 ? "AddressNotAvailable 99" : "Other 97";
    }

    [Fact]
    public void A_literal_of_ipv6_binds_where_the_machine_has_its_loopback()
    {
        // `[::1]` is read by `inet_pton` into an address of IPv6 (S9); a machine without that address, or without IPv6, gives the system's error
        const string text = "match TcpListener.Bind(\"[::1]:0\") {\n    TcpListener l => print(l.Port > 0)\n    IoError e => print(\"{e.Kind} {e.Code}\")\n}\n";
        Ran(text, "socket-ipv6", [Loopback6("true")]);
    }

    [Fact]
    public void A_listener_on_the_wildcard_of_ipv6_takes_a_connection_of_ipv4_where_the_dual_stack_is_on()
    {
        // `[::]` binds every interface of IPv6, and Linux's dual stack, on where `net.ipv6.bindv6only` is 0, its default, gives such a socket the
        // connections of IPv4 too, as Rust's listener gets them, since neither sets `IPV6_V6ONLY` (S9); with the stack off the connection is
        // refused, and without IPv6 the socket is not made
        const string text = """
            match TcpListener.Bind("[::]:0") {
                TcpListener l => {
                    match TcpStream.Connect("127.0.0.1:{l.Port}") {
                        TcpStream => print("connected")
                        IoError e => print("{e.Kind} {e.Code}")
                    }
                }
                IoError e => print("{e.Kind} {e.Code}")
            }

            """;
        var expected = !File.Exists("/proc/net/if_inet6") ? "Other 97"
            : File.ReadAllText("/proc/sys/net/ipv6/bindv6only").Trim() == "0" ? "connected" : "ConnectionRefused 111";
        Ran(text, "socket-dual-stack", [expected]);
    }

    [Fact]
    public void A_literal_of_ipv6_takes_a_scope_id_and_one_whose_port_has_a_sign_is_looked_up_as_rust_looks_it_up()
    {
        // Rust's parse of a socket address reads `[host%scope]:port`, a `u32` after the `%` and digits alone in the port; where it fails,
        // Rust looks the host up as it is written, and `[::1]` is no name, so each lookup fails before a socket is made (S9)
        const string text = """
            void Try(string address) {
                match TcpListener.Bind(address) {
                    TcpListener => print("bound")
                    IoError e => print("{e.Kind} {e.Code}")
                }
            }

            Try("[::1]:+0")
            Try("[::1%x]:0")
            Try("[::1%]:0")
            Try("[::1%4294967296]:0")
            Try("[::1%1]:0")
            Try("[::1%4294967295]:00000")

            """;
        Ran(text, "socket-ipv6-scope", ["NotFound 0", "NotFound 0", "NotFound 0", "NotFound 0", Loopback6("bound"), Loopback6("bound")]);
    }

    [Fact]
    public void A_write_to_a_connection_whose_peer_closed_it_is_a_broken_pipe_and_raises_no_signal()
    {
        // the runtime ignores `SIGPIPE` at its start (section 2), so a write fails with `EPIPE` (32), `BrokenPipe`, as in Rust, and does not end the
        // process by the signal: the first write after the peer's close is taken by the system, the peer answers it with a reset, and a later one fails
        const string text = """
            void | IoError Broken() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                server.Close()
                mut writes = 0
                mut failed = false
                while !failed && writes < 1000 {
                    client.Write("x".Bytes) else {
                        IoError e => {
                            print("{e.Kind} {e.Code}")
                            failed = true
                        }
                    }

                    writes = writes + 1
                }

                print(writes > 1)
            }

            Broken() else {
                IoError e => print(e)
            }

            """;
        Ran(text, "socket-broken-pipe", ["BrokenPipe 32", "true"]);
    }

    [Fact]
    public void A_connection_that_accept_gave_names_its_listener_s_address_in_its_errors()
    {
        const string text = """
            void | IoError Named() {
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                server.Close()
                mut buffer = Bytes()
                match server.Read(mut buffer, 1) {
                    int => print("read")
                    IoError e => print(e)
                }

                client.Close()
            }

            Named() else {
                IoError e => print(e)
            }

            """;
        Ran(text, "socket-accepted-path", ["other (os error 9): 127.0.0.1:0"]);
    }

    // ---- the time bounds of sockets (S9) -------------------------------------------------------------------

    /// <summary>
    /// A program that reads the time of the reads (<c>SO_RCVTIMEO</c>, 20) and of the writes (<c>SO_SNDTIMEO</c>, 21) of a descriptor back
    /// by <c>getsockopt</c>, as its seconds and microseconds: the descriptors 3, 4 and 5 are the listener, the stream <c>Connect</c> made
    /// and the one <c>Accept</c> gave, the first three the program opens.
    /// </summary>
    private const string Bounds = """
        extern "C" {
            int getsockopt(int fd, int level, int name, void* value, int* length)
            void* malloc(long size)
            void free(void* p)
        }

        string Bound(int fd, int option) {
            mut long seconds = -1
            mut long micros = -1
            raw {
                void* time = malloc(16)
                mut int length = 16
                if getsockopt(fd, 1, option, time, &length) == 0 {
                    seconds = *Ptr<long>(time)
                    micros = *Ptr<long>(Ptr<byte>(time) + 8)
                }

                free(time)
            }

            return "{seconds} {micros}"
        }

        void | IoError Bounds() {
            listener = TcpListener.Bind("127.0.0.1:0")
            client = TcpStream.Connect("127.0.0.1:{listener.Port}")
            server = listener.Accept()
            mut fd = 3
            while fd <= 5 {
                print("{Bound(fd, 20)} {Bound(fd, 21)}")
                fd = fd + 1
            }

            client.SetReadTimeout(1500000001ns)
            client.SetWriteTimeout(1ns)
            print(Bound(4, 20))
            print(Bound(4, 21) != "0 0")
            client.SetReadTimeout(null)
            print(Bound(4, 20))
            client.Close()
            server.Close()
        }

        Bounds() else {
            IoError e => print(e)
        }

        """;

    [Fact]
    public void A_stream_waits_5_seconds_by_default_a_listener_without_a_bound_and_a_setter_sets_the_time_as_rust_does()
    {
        // S9: the stream `Connect` made and the one `Accept` gave read and write with a bound of 5 seconds, and the listener has none, so
        // that `Accept` waits; a time cut down to whole microseconds, as Rust's `set_timeout` cuts it, which Linux keeps in its own ticks,
        // rounding a part of one up, so that 1.5 s and 1 ns read back as 1.5 s and a 1 ns that rounded up would not; a positive time that
        // comes to no microsecond raised to 1, a bound and not none; and `null` no bound
        string[] lines(string seconds) => ["0 0 0 0", $"{seconds} 0 {seconds} 0", $"{seconds} 0 {seconds} 0", "1 500000", "true", "0 0"];
        Ran(Bounds, "socket-bounds", lines("5"), grants: ["raw", "native"]);

        // the positive control: the default the runtime sets, 7 seconds, read back as 7
        var seven = ListTests.Edited(("runtime/linux/linux.kz", "long DefaultBound() => 5000000000", "long DefaultBound() => 7000000000"));
        Ran(Bounds, "socket-bounds-seven", lines("7"), runtimes: [seven], grants: ["raw", "native"]);
    }

    /// <summary>A listener whose queue of 128 connections that wait is full, and connections to it until one is not made: what it was, and whether all of it took less than 2 seconds.</summary>
    private const string Full = """
        void | IoError Full() {
            listener = TcpListener.Bind("127.0.0.1:0")
            mut held = List<TcpStream>()
            mut timedOut = false
            start = timestamp.Now
            while !timedOut && held.Count < 1000 {
                match TcpStream.Connect("127.0.0.1:{listener.Port}") {
                    TcpStream s => held.Add(s)
                    IoError e => {
                        print("{e.Kind} {e.Code}")
                        timedOut = true
                    }
                }
            }

            print(held.Count > 1)
            print(timestamp.Now - start < 2s)
        }

        Full() else {
            IoError e => print(e)
        }

        """;

    [Fact]
    public void The_first_form_of_connect_is_bounded_by_the_default_and_runs_out_with_timed_out_and_the_number_0()
    {
        // S9: Linux drops the connection that finds its listener's queue full, so that it waits; the first form of `Connect` waits the
        // default bound, here 100 ms, and gives `TimedOut` with the number 0, as Rust's `connect_timeout` gives no error of the system
        var short_ = ListTests.Edited(("runtime/linux/linux.kz", "long DefaultBound() => 5000000000", "long DefaultBound() => 100000000"));
        Ran(Full, "socket-connect-default", ["TimedOut 0", "true", "true"], runtimes: [short_]);
    }

    [Fact]
    public void One_bound_holds_over_every_address_of_a_name_and_once_it_ran_out_no_other_is_tried()
    {
        // S9 (assumed: one bound shared by every address a name gives; its cost is that a first address that never answers spends the
        // whole bound, and the later addresses are not tried): a lookup that asks for every type of socket gives `127.1` three times
        // (a stream, a datagram and a raw socket), each a connection to the full queue; the call ends with the one bound, 500 ms, not
        // three times it
        const string text = """
            void | IoError Shared() {
                listener = TcpListener.Bind("127.0.0.1:0")
                mut held = List<TcpStream>()
                mut full = false
                while !full && held.Count < 1000 {
                    match TcpStream.Connect("127.0.0.1:{listener.Port}", 100ms) {
                        TcpStream s => held.Add(s)
                        IoError => full = true
                    }
                }

                start = timestamp.Now
                match TcpStream.Connect("127.1:{listener.Port}", 500ms) {
                    TcpStream => print("connected")
                    IoError e => print("{e.Kind} {e.Code}")
                }

                print(timestamp.Now - start < 1s)
            }

            Shared() else {
                IoError e => print(e)
            }

            """;
        var every = ListTests.Edited(("runtime/linux/linux.kz", "*Ptr<int>(held + 72) = 1", "*Ptr<int>(held + 72) = 0"));
        Ran(text, "socket-connect-shared", ["TimedOut 0", "true"], runtimes: [every]);
    }

    [Fact]
    public void Eagain_is_timed_out_on_a_stream_s_read_and_write_alone()
    {
        // S9 (assumed): a call on a blocking socket whose time ran out returns `EAGAIN` (11), which a stream's read and write give as
        // `TimedOut` with the system's number; every other `EAGAIN` keeps Rust's `WouldBlock`: through a runtime whose every read gives it
        const string text = """
            void | IoError Again() {
                File.WriteAll("again.txt", "x".Bytes)
                f = File.Open("again.txt")
                File.Delete("again.txt")
                listener = TcpListener.Bind("127.0.0.1:0")
                client = TcpStream.Connect("127.0.0.1:{listener.Port}")
                server = listener.Accept()
                mut buffer = Bytes()
                match f.Read(mut buffer, 1) {
                    int => print("read")
                    IoError e => print("{e.Kind} {e.Code}")
                }

                match client.Read(mut buffer, 1) {
                    int => print("read")
                    IoError e => print("{e.Kind} {e.Code}")
                }

                server.Close()
            }

            Again() else {
                IoError e => print(e)
            }

            """;
        var again = ListTests.Edited(("runtime/linux/linux.kz", "pub long ReadFile(int fd, byte* bytes, long count) {\n", "pub long ReadFile(int fd, byte* bytes, long count) {\n    if count >= 0 {\n        return -11\n    }\n\n"));
        Ran(text, "socket-eagain", ["WouldBlock 11", "TimedOut 11"], runtimes: [again]);
    }

    [Fact]
    public void A_build_for_windows_says_not_supported_at_a_call_on_a_file_and_one_for_linux_builds_it()
    {
        const string text = "File.Delete(\"gone.txt\") else {\n    IoError e => print(e)\n}\n";
        RefusedOnWindows(Compile(text, Builder.CheckRuntime(Target.Windows)));
        Assert.NotNull(Compile(text, Builder.CheckRuntime(Target.Linux)).Program); // the positive control
    }
}
