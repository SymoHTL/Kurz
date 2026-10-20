using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>
/// The command <c>kurz</c> (design record, section 2). <c>kurz build file.kz</c> writes the executable
/// beside the source, named after it, or where <c>-o</c> says; <c>kurz run file.kz</c> builds the file
/// into a temporary place and runs it; <c>kurz exec "&lt;program&gt;"</c> does the same with the program
/// given as the argument. The default is the test build; <c>--release</c> builds the release one.
/// A refused program gets its diagnostics, one line each, <c>hello.kz:3: &lt;id&gt;: &lt;text&gt;</c>, with
/// <c>exec</c> in place of the file's name under <c>exec</c>, and the command exits with 1; <c>run</c>
/// and <c>exec</c> end with the exit code of the program they ran. A command line the command cannot
/// read, and a toolchain that is missing or refuses the build, are reported on standard error and
/// exit with 1 as a refused program does (<c>compiler/README.md</c>).
/// </summary>
public static class Command
{
    /// <summary>What the command prints for a command line it cannot read.</summary>
    public const string Usage =
        "usage: kurz build <file.kz> [-o <executable>] [--release]\n" +
        "       kurz run <file.kz> [--release]\n" +
        "       kurz exec \"<program>\" [--release]\n";

    /// <summary>The name under which a program given as the argument of <c>exec</c> is reported.</summary>
    public const string ExecName = "exec";

    /// <summary>Runs the command line; the exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(error);
        if (!Read(arguments, out var verb, out var subject, out var outputPath, out var release))
        {
            error.Write(Usage);
            return 1;
        }

        var source = verb == ExecName ? SourceText.FromString(ExecName, subject) : ReadFile(subject, error);
        if (source is null)
        {
            return 1;
        }

        var compilation = Builder.Compile(source, testBuild: !release);
        if (!compilation.Accepted)
        {
            foreach (var d in compilation.Diagnostics.Sorted)
            {
                error.WriteLine(Line(source.Name, d));
            }

            return 1;
        }

        var clang = Clang.Find(out var looked);
        if (clang is null)
        {
            error.WriteLine($"kurz: clang is not there; looked for {string.Join(", ", looked)}");
            return 1;
        }

        var work = Path.Combine(Path.GetTempPath(), "kurz-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (verb == "build")
            {
                Builder.Build(compilation.Ir!, clang, work, null, outputPath ?? Builder.ExecutableBeside(subject));
                return 0;
            }

            var program = Path.Combine(work, OperatingSystem.IsWindows() ? "program.exe" : "program");
            Builder.Build(compilation.Ir!, clang, work, null, program);
            return Processes.Inherit(program, [], null);
        }
        catch (ToolchainException e)
        {
            error.WriteLine($"kurz: {e.Message}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // a working directory the system holds on to is left to it
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The one line of a diagnostic: <c>hello.kz:3: &lt;id&gt;: &lt;text&gt;</c>, or <c>hello.kz:3: not supported: &lt;construct&gt;</c>.</summary>
    public static string Line(string name, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return diagnostic.IsError
            ? $"{name}:{diagnostic.Line}: {diagnostic.Id}: {diagnostic.Message}"
            : $"{name}:{diagnostic.Line}: not supported: {diagnostic.Message}";
    }

    private static bool Read(IReadOnlyList<string> arguments, out string verb, out string subject, out string? outputPath, out bool release)
    {
        verb = arguments.Count > 0 ? arguments[0] : string.Empty;
        subject = string.Empty;
        outputPath = null;
        release = false;
        if (verb is not ("build" or "run" or ExecName))
        {
            return false;
        }

        for (var i = 1; i < arguments.Count; i++)
        {
            var a = arguments[i];
            if (a == "--release" && !release)
            {
                release = true;
            }
            else if (a == "-o" && verb == "build" && outputPath is null && i + 1 < arguments.Count)
            {
                outputPath = arguments[++i];
            }
            else if (subject.Length == 0 && (verb == ExecName || !a.StartsWith('-')))
            {
                subject = a;
            }
            else
            {
                return false;
            }
        }

        return subject.Length > 0;
    }

    private static SourceText? ReadFile(string path, TextWriter error)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"kurz: {path}: {e.Message}");
            return null;
        }

        var source = SourceText.FromBytes(path, bytes, out var invalid);
        if (source is null)
        {
            error.WriteLine(Line(path, invalid!));
        }

        return source;
    }
}
