using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// X3's change of case by Unicode's full mapping, as Rust's <c>to_lowercase</c> and <c>to_uppercase</c>: <c>ToLower</c> and <c>ToUpper</c>
/// of every code point of the first two planes, the only planes the database maps, alone and in a text, give what <see cref="CaseTables"/>
/// holds for it, read here from every block of the levels and every mapping to several code points, without the standard library's
/// lookup; fixed lines whose results are Rust's give the final sigma in its contexts and the mappings to several code points; and lines
/// around the walks' own paths give Rust's results too, with no string's count past its room.
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

                // in a text, where the walk reaches the code point after a byte that changes, with room for any mapping left: the
                // path of a code point of two bytes; a space before it, which is neither cased nor case-ignorable, keeps a capital
                // sigma's mapping its mapping alone
                if "A {one}012345678901".ToLower() != "a {lowered}012345678901" {
                    print("{point} l in a text")
                }

                if "a {one}012345678901".ToUpper() != "A {raised}012345678901" {
                    print("{point} u in a text")
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
        ("1ŉ2", true, "1ʼN2"), // a byte kept as it is, then two bytes into three: the last code point outgrows the block
        ("1ΐabc", true, "1\u0399\u0308\u0301ABC"), // two bytes into six after a byte kept as it is: four code points written after the growth
        ("AİB", false, "ai̇b"), // the block outgrown by the last code point
        ("AİΣ", false, "ai̇ς"), // and by a final sigma, which reads the code points before it
    ];

    /// <summary>
    /// Lines around the walks' own paths, each input written as a Kurz literal, with what Rust's <c>to_lowercase</c> or
    /// <c>to_uppercase</c> gives it: runs of ASCII after the first byte that changes, which the walk takes 16 bytes at a time, with the
    /// bytes beside the letters of both directions among them; a byte above ASCII among 16; bytes copied before the first that changes;
    /// and code points of two bytes, which the walk takes on a path of their own while 12 bytes of room are left.
    /// </summary>
    private static readonly (string Input, bool Upper, string Expected)[] Walks =
    [
        ("ABCDEFGHIJKLMNO", false, "abcdefghijklmno"), // 15 bytes: no run of 16
        ("ABCDEFGHIJKLMNOP", false, "abcdefghijklmnop"), // one run, nothing after it
        ("ABCDEFGHIJKLMNOPQ", false, "abcdefghijklmnopq"), // one run and one byte
        ("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFG", false, "abcdefghijklmnopqrstuvwxyzabcdefg"), // two runs and one byte
        ("abcdefghijklmnopqrstuvwxyzabcdefg", true, "ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFG"),
        ("A@AZ[`az\\{@AZ[`az\\{", false, "a@az[`az{@az[`az{"), // the bytes beside both ranges of letters, in a run
        ("a@AZ[`az\\{@AZ[`az\\{", true, "A@AZ[`AZ{@AZ[`AZ{"),
        ("A@AZ[`az\\{", false, "a@az[`az{"), // and one byte at a time
        ("a@AZ[`az\\{", true, "A@AZ[`AZ{"),
        ("ABCDEFGHIJKLMNOÄ", false, "abcdefghijklmnoä"), // a byte above ASCII in the 16th place: no run
        ("ABCDEFGHIJKLMNOPÄBCDEFGHIJKLMNOPQ", false, "abcdefghijklmnopäbcdefghijklmnopq"), // a run, two bytes, then one byte at a time
        ("abcdefghijklmnopqrsT", false, "abcdefghijklmnopqrst"), // 19 bytes copied before the first that changes
        ("äbcdefghijklmnopqrsT", false, "äbcdefghijklmnopqrst"), // a code point above ASCII among them
        ("abcdefghijklmnopqrstu", false, "abcdefghijklmnopqrstu"), // nothing changes: the string itself
        ("ΣΑΣ ΑΣ. ΣΑ Σ ΑΣΑ", false, "σας ας. σα σ ασα"), // the sigma on the path of two bytes, then near the end
        ("ΐ0123456789", true, "\u0399\u0308\u03010123456789"), // two bytes into six, with 12 bytes of room left, then the block outgrown
        ("ΐ012345678", true, "\u0399\u0308\u0301012345678"), // with 11 left
        ("ſſſſſſſſſſſſſſſ", true, "SSSSSSSSSSSSSSS"), // two bytes into one
        ("Привет, Мир! Добрый день", false, "привет, мир! добрый день"),
        ("привет, мир! добрый день", true, "ПРИВЕТ, МИР! ДОБРЫЙ ДЕНЬ"),
    ];

    /// <summary>
    /// The edit that makes the standard library end the process, exit code 3, where a string's count passes the room of its block: a
    /// walk that wrote past the room it read.
    /// </summary>
    private static readonly (string Path, string Old, string New)[] RoomCheck =
    [
        ("std/std.kz", "        *Ptr<int>(bytes - 8) = int(count)\n",
         "        if count > long(*Ptr<int>(bytes - 4)) {\n            sys.Exit(3)\n        }\n\n        *Ptr<int>(bytes - 8) = int(count)\n"),
    ];

    private static readonly Lazy<CheckedRuntime> Roomed = new(() => ListTests.Edited(RoomCheck));

    /// <summary>The program that prints each line's result between brackets, and those results as Rust gives them.</summary>
    private static (string Program, List<string> Expected) Lines(IEnumerable<(string Input, bool Upper, string Expected)> lines) =>
        (string.Concat(lines.Select((f, n) => $"r{n} = \"{f.Input}\".{(f.Upper ? "ToUpper" : "ToLower")}()\nprint(\"[{{r{n}}}]\")\n")),
         lines.Select(f => $"[{f.Expected}]").ToList());

    /// <summary>
    /// The mapping of one direction as the levels hold it: every code point of the first two planes whose delta is not 0, the delta
    /// added to its lowest 16 bits, and for a delta of 32768 the mapping to several code points; every one of those is marked so.
    /// </summary>
    private static Dictionary<int, int[]> Mapping(int[] index, int[] deltas, int[] multis)
    {
        var several = new Dictionary<int, int[]>();
        for (var at = 0; at < multis.Length; at += 4)
        {
            several[multis[at]] = multis[(at + 1)..(at + 4)].Where(c => c != 0).ToArray();
        }

        Assert.Equal(Planes / 32, index.Length);
        var mapping = new Dictionary<int, int[]>();
        for (var c = 0; c < Planes; c++)
        {
            var delta = deltas[(index[c / 32] * 32) + (c % 32)];
            if (delta == 32768)
            {
                mapping[c] = several[c];
            }
            else if (delta != 0)
            {
                mapping[c] = [(c & ~0xFFFF) | ((c + delta) & 0xFFFF)];
            }
        }

        Assert.True(several.Keys.All(mapping.ContainsKey), "a mapping to several code points whose delta is not the mark");
        return mapping;
    }

    /// <summary>The text of a list of bytes, the UTF-8 bytes of the code points, as <c>print</c> writes it (A4).</summary>
    private static string Bytes(IEnumerable<int> points) =>
        "[" + string.Join(", ", Encoding.UTF8.GetBytes(string.Concat(points.Select(char.ConvertFromUtf32)))) + "]";

    [Fact]
    public void Every_code_point_of_the_first_two_planes_changes_case_as_the_tables_hold_it()
    {
        var lower = Mapping(CaseTables.LowerIndex, CaseTables.LowerDeltas, CaseTables.LowerMultis);
        var upper = Mapping(CaseTables.UpperIndex, CaseTables.UpperDeltas, CaseTables.UpperMultis);
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
        var (program, expected) = Lines(Fixed);
        foreach (var (runtime, name) in new[] { (toolchain.Runtime, "case-lines"), (ListTests.Fenced, "case-lines-fenced") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void The_walks_give_rusts_results_around_their_paths_and_write_no_string_past_its_room()
    {
        var (program, expected) = Lines([.. Walks, .. Fixed]);
        foreach (var (runtime, name) in new[] { (toolchain.Runtime, "case-walks"), (Roomed.Value, "case-walks-roomed") })
        {
            var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], runtime, testBuild: true);
            Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
            var (code, output, error) = Toolchain.Run(toolchain.Build(compilation.Program, name));
            Assert.Equal((0, ""), (code, error));
            Assert.Equal(expected, output);
        }

        // the positive control: the same lines, with the walk's room read only where no room is left, end at a string past its room
        var unroomed = ListTests.Edited([.. RoomCheck, ("std/std.kz", "if room - length < 12 && ", "if room - length < 0 && ")]);
        var control = Builder.Compile(SourceText.FromString("program.kz", program), [], unroomed, testBuild: true);
        Assert.True(control.Program is not null, string.Join("; ", control.Diagnostics.Sorted));
        Assert.Equal(3, Toolchain.Run(toolchain.Build(control.Program, "case-walks-unroomed")).Code);
    }
}
