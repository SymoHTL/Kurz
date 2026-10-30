using Kurz.Compiler.Build;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Tests;

/// <summary>
/// A session's probe of a Kurz program in a file: <c>dotnet Kurz.Compiler.Tests.dll &lt;file&gt; [--fenced] [--grant &lt;name&gt;]...</c>
/// builds it with the runtime of this machine's target, or with <c>--fenced</c> under the allocator that makes every freed block
/// inaccessible (<see cref="ListTests.Fenced"/>, the suite's own), runs it as the suite runs a program, and prints what it wrote,
/// what it wrote on its error stream and its exit code. Its own exit code is 0 where the program was built and ran, whatever the
/// program's, 1 where the program has errors, which it prints, and 2 for a command line it does not read. The test host runs the
/// suite through an entry of its own, so this entry is the assembly's for <c>dotnet</c> alone.
/// </summary>
public static class Probe
{
    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        string? path = null;
        var fenced = false;
        var grants = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--fenced")
            {
                fenced = true;
            }
            else if (args[i] == "--grant" && i + 1 < args.Count)
            {
                grants.Add(args[++i]);
            }
            else if (path is null && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                path = args[i];
            }
            else
            {
                output.WriteLine($"probe: `{args[i]}` is no argument of the probe");
                return 2;
            }
        }

        if (path is null)
        {
            output.WriteLine("usage: Kurz.Compiler.Tests <file> [--fenced] [--grant <name>]...");
            return 2;
        }

        using var toolchain = new Toolchain();
        var compilation = Builder.Compile(SourceText.FromString(path, File.ReadAllText(path)), grants, fenced ? ListTests.Fenced : toolchain.Runtime, testBuild: true);
        if (compilation.Program is null)
        {
            foreach (var d in compilation.Diagnostics.Sorted)
            {
                output.WriteLine(d);
            }

            return 1;
        }

        var (code, lines, error) = Toolchain.Run(toolchain.Build(compilation.Program, "probe"));
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        output.Write(error);
        output.WriteLine($"exit {code}");
        return 0;
    }
}
