using System.Text.RegularExpressions;

namespace Kurz.Compiler.Corpus;

public enum Expectation
{
    /// <summary>The program compiles, runs and prints exactly the output lines.</summary>
    Output,

    /// <summary>The program does not compile: this error, reported on this line of the file.</summary>
    Error,

    /// <summary>The program compiles, prints the output lines, then this exception ends it on this line.</summary>
    Throws,
}

public enum BuildKind
{
    Test,
    Release,
}

/// <summary>
/// The header of a corpus case, in the shape reference/00-about.md states: the first line
/// <c>// expect: output</c>, <c>// expect: error &lt;id&gt; at &lt;line&gt;</c> or
/// <c>// expect: throws &lt;id&gt; at &lt;line&gt;</c>; then <c>// | </c> lines with what the program
/// prints (not for an error); then an optional <c>// build: test</c> or <c>// build: release</c>;
/// then exactly one <c>// rules: A1, B2</c> line; then an empty line; then the program. A line of a
/// diagnostic counts from the first line of the file, the header included.
/// </summary>
public sealed partial record CaseHeader(
    Expectation Expectation,
    string? ErrorId,
    int ErrorLine,
    IReadOnlyList<string> OutputLines,
    BuildKind? Build,
    IReadOnlyList<string> Rules,
    string Body,
    int BodyLine)
{
    [GeneratedRegex(@"^// expect: (?:(output)|(throws) ([a-z][a-z-]*) at (\d+)|error ([a-z][a-z-]*) at (\d+))$")]
    private static partial Regex ExpectLine();

    [GeneratedRegex(@"^// build: (test|release)$")]
    private static partial Regex BuildLine();

    [GeneratedRegex(@"^// rules: [A-Z]\d+(?:, [A-Z]\d+)*$")]
    private static partial Regex RulesLine();

    /// <summary>Reads a case file's text. Null, with <paramref name="problem"/> set, when the header is not in its shape.</summary>
    public static CaseHeader? Parse(string text, out string? problem)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var expect = ExpectLine().Match(lines[0]);
        if (!expect.Success)
        {
            problem = "the first line is not `// expect: output`, `// expect: throws <id> at <line>` or `// expect: error <id> at <line>`";
            return null;
        }

        Expectation expectation;
        string? id = null;
        var errorLine = 0;
        if (expect.Groups[1].Success)
        {
            expectation = Expectation.Output;
        }
        else if (expect.Groups[2].Success)
        {
            expectation = Expectation.Throws;
            id = expect.Groups[3].Value;
            errorLine = int.Parse(expect.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            expectation = Expectation.Error;
            id = expect.Groups[5].Value;
            errorLine = int.Parse(expect.Groups[6].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var i = 1;
        var output = new List<string>();
        while (expectation != Expectation.Error && i < lines.Length && lines[i].StartsWith("// | ", StringComparison.Ordinal))
        {
            output.Add(lines[i]["// | ".Length..]);
            i++;
        }

        BuildKind? build = null;
        if (i < lines.Length && BuildLine().Match(lines[i]) is { Success: true } buildMatch)
        {
            build = buildMatch.Groups[1].Value == "test" ? BuildKind.Test : BuildKind.Release;
            i++;
        }

        if (i >= lines.Length || !RulesLine().IsMatch(lines[i]))
        {
            problem = "the header does not end in one `// rules: A1, B2` line";
            return null;
        }

        var rules = lines[i]["// rules: ".Length..].Split(", ");
        i++;
        if (i >= lines.Length || lines[i].Length != 0)
        {
            problem = "an empty line separates the header from the program";
            return null;
        }

        i++;
        var body = string.Join('\n', lines[i..]);
        if (body.Trim().Length == 0)
        {
            problem = "the case has no body";
            return null;
        }

        problem = null;
        return new CaseHeader(expectation, id, errorLine, output, build, rules, body, i + 1);
    }
}
