using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// X3's change of case by Unicode's full mapping, as Rust's <c>to_lowercase</c> and <c>to_uppercase</c>: <c>ToLower</c> and <c>ToUpper</c>
/// of every code point of the first two planes, the only planes the database maps, give what <see cref="CaseTables"/> holds for it, read
/// here by walking every run and every mapping to several code points, without the standard library's binary search; and fixed lines
/// whose results are Rust's give the final sigma in its contexts and the mappings to several code points.
/// </summary>
public sealed class CaseTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private const int Planes = 0x20000;

    private const string EveryCodePoint = """
        string Of(int c) {
            mut long count = 4
            if c < 128 {
                count = 1
            } else if c < 2048 {
                count = 2
            } else if c < 65536 {
                count = 3
            }

            mut string text = ""
            raw {
                byte* p = string.Allocate(count)
                if count == 1 {
                    p[0] = byte(c)
                } else if count == 2 {
                    p[0] = byte(192 | (c >> 6))
                    p[1] = byte(128 | (c & 63))
                } else if count == 3 {
                    p[0] = byte(224 | (c >> 12))
                    p[1] = byte(128 | ((c >> 6) & 63))
                    p[2] = byte(128 | (c & 63))
                } else {
                    p[0] = byte(240 | (c >> 18))
                    p[1] = byte(128 | ((c >> 12) & 63))
                    p[2] = byte(128 | ((c >> 6) & 63))
                    p[3] = byte(128 | (c & 63))
                }

                text = string.FromRaw(p, count)
            }

            return text
        }

        mut point = 0
        while point < 131072 {
            if point < 55296 || point > 57343 {
                one = Of(point)
                lowered = one.ToLower()
                raised = one.ToUpper()
                if lowered != one {
                    print("{point} l {lowered.Bytes}")
                }

                if raised != one {
                    print("{point} u {raised.Bytes}")
                }
            }

            point = point + 1
        }

        """;

    /// <summary>Each line's input, written as a Kurz literal, and what Rust's <c>to_lowercase</c> or <c>to_uppercase</c> gives it.</summary>
    private static readonly (string Input, bool Upper, string Expected)[] Fixed =
    [
        ("ΣΑΣ", false, "σας"), // a capital sigma after a cased letter and before none ends a word
        ("ὈΔΥΣΣΕΎΣ", false, "ὀδυσσεύς"), // Rust's example: only the last of three
        ("Σ", false, "σ"), // no cased letter before it
        ("AΣ", false, "aς"),
        ("AΣB", false, "aσb"), // a cased letter after it
        ("A.Σ", false, "a.ς"), // a case-ignorable character between the letter and the sigma
        ("AΣ.", false, "aς."), // and one after it, with no letter beyond
        ("AΣ.B", false, "aσ.b"), // a letter past the case-ignorable character
        ("1Σ", false, "1σ"), // a character before it that is neither case-ignorable nor cased
        ("ΑΣ́", false, "ας́"), // a combining mark after it is case-ignorable
        ("ß", true, "SS"),
        ("tschüß", true, "TSCHÜSS"), // Rust's example
        ("İ", false, "i̇"), // two code points, a mapping of SpecialCasing.txt without a condition
        ("K", false, "k"), // the Kelvin sign, into ASCII
        ("ŉ", true, "ʼN"),
        ("ǰ", true, "J̌"),
        ("ﬃ", true, "FFI"), // three code points
        ("ΐ", true, "\u0399\u0308\u0301"), // three code points, two bytes into six
        ("𐐀", false, "𐐨"), // four bytes, in the second plane
        ("ǅ", true, "Ǆ"), // a title-case letter
        ("ǅ", false, "ǆ"),
        ("", false, ""),
        ("abc", false, "abc"), // nothing to change
        ("Straße ΑΒΓ", false, "straße αβγ"),
    ];

    /// <summary>The mapping of one direction as the tables hold it: every code point of every run that its step reaches, then every mapping to several code points.</summary>
    private static Dictionary<int, int[]> Mapping(int[] runs, int[] multis)
    {
        var mapping = new Dictionary<int, int[]>();
        for (var at = 0; at < runs.Length; at += 4)
        {
            for (var c = runs[at]; c <= runs[at + 1]; c += runs[at + 2])
            {
                mapping[c] = [c + runs[at + 3]];
            }
        }

        for (var at = 0; at < multis.Length; at += 4)
        {
            mapping[multis[at]] = multis[(at + 1)..(at + 4)].Where(c => c != 0).ToArray();
        }

        return mapping;
    }

    /// <summary>The text of a list of bytes, the UTF-8 bytes of the code points, as <c>print</c> writes it (A4).</summary>
    private static string Bytes(IEnumerable<int> points) =>
        "[" + string.Join(", ", Encoding.UTF8.GetBytes(string.Concat(points.Select(char.ConvertFromUtf32)))) + "]";

    [Fact]
    public void Every_code_point_of_the_first_two_planes_changes_case_as_the_tables_hold_it()
    {
        var lower = Mapping(CaseTables.LowerRuns, CaseTables.LowerMultis);
        var upper = Mapping(CaseTables.UpperRuns, CaseTables.UpperMultis);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            lower[c] = [c + 32];
            upper[c + 32] = [c];
        }

        var expected = new List<string>();
        for (var c = 0; c < Planes; c++)
        {
            if (lower.TryGetValue(c, out var down))
            {
                expected.Add($"{c} l {Bytes(down)}");
            }

            if (upper.TryGetValue(c, out var up))
            {
                expected.Add($"{c} u {Bytes(up)}");
            }
        }

        // the floors: the tables are read, and a mapping to several code points and one in the second plane are among them
        Assert.True(lower.Count >= 1000 && upper.Count >= 1000, $"{lower.Count} lowercase and {upper.Count} uppercase mappings");
        Assert.True(upper.Values.Count(v => v.Length > 1) >= 100, "fewer than 100 uppercase mappings to several code points");
        Assert.Contains(expected, line => line.StartsWith("66560 l ", StringComparison.Ordinal)); // U+10400
        Assert.True(lower.Keys.All(c => c < Planes) && upper.Keys.All(c => c < Planes), "a mapping past the first two planes");

        var compilation = Builder.Compile(SourceText.FromString("program.kz", EveryCodePoint), ["raw"], toolchain.Runtime, testBuild: true);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, "every-code-point"));
        Assert.Equal((0, ""), (code, error));
        Assert.Equal(expected, output);
    }

    [Fact]
    public void The_final_sigma_and_the_mappings_to_several_code_points_are_rusts()
    {
        var program = string.Concat(Fixed.Select((f, n) => $"r{n} = \"{f.Input}\".{(f.Upper ? "ToUpper" : "ToLower")}()\nprint(\"[{{r{n}}}]\")\n"));
        var expected = Fixed.Select(f => $"[{f.Expected}]").ToList();
        foreach (var (runtime, name) in new[] { (toolchain.Runtime, "case-lines"), (ListTests.Fenced, "case-lines-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }
}
