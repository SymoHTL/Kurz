using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    /// <summary>
    /// G3: <c>Regex.New(pattern)</c>, the standard library's <c>RegexNew</c>, whose result is <c>Regex | RegexError</c>, or for a
    /// literal the checker read <c>RegexChecked</c>, whose result is the <c>Regex</c>, each given the pattern, borrowed for the call,
    /// the table of G4's classes and folding (<see cref="RegexTable"/>), and the flag <c>backtracking</c>, named or the second
    /// argument, false where the call has none (G2).
    /// </summary>
    private string EmitRegexNew(CallExpression c, CallInfo info)
    {
        var arguments = Arguments(c, info, j => j == 0 ? Types.String : Types.Bool, _ => false, _ => null, cConvention: false, owned: false);
        var result = Sub(info.Result);
        var name = StdFunction(result is UnionType ? "RegexNew" : "RegexChecked");
        var flag = arguments.Count > 1 ? arguments[1] : "i1 false";
        var pattern = c.Arguments.FirstOrDefault(a => a.Name is null or "pattern")?.Value;
        var classes = result is UnionType || pattern is null || Unwrap(pattern) is not StringLiteral { IsPlain: true } s
            || s.Segments.Cast<TextSegment>().Any(t => t.Text.Contains("\\p", StringComparison.Ordinal) || t.Text.Contains("\\P", StringComparison.Ordinal));
        return fn.Value($"call {Ir(result)} {name}({arguments[0]}, ptr {RegexTable(classes)}, {flag})");
    }

    /// <summary>
    /// The global that holds G4's tables, written once into a program that makes a <c>Regex</c>, as the standard library's
    /// <c>RegexTablePart</c> reads it: seven counts of 32 bits, of the numbers of the ranges of <c>\d</c>, <c>\w</c> and <c>\s</c>,
    /// of the pairs of the folding, of the ranges of the characters of a group's name, Alphabetic and the categories Nd, Nl and No,
    /// and of the 32 bits of the classes of <c>\p</c>, then those numbers, 32 bits each, from <see cref="CaseTables"/>; the
    /// classes of <c>\p</c> are the bytes of <see cref="CaseTables.RegexProperties"/>, four to each number, the first the lowest.
    /// A program carries one such global, which every call reads: with the classes where one of its patterns can need them, a
    /// pattern made when the program runs or a literal that holds <c>\p</c> or <c>\P</c> (<paramref name="classes"/>), and with
    /// a count of 0 for them otherwise, so that a program whose patterns are all literals without them carries none (issue 176,
    /// comment 6812529825, Q1, the default taken). It is written once the whole program is emitted (<see cref="EmitRegexTable"/>).
    /// </summary>
    private string RegexTable(bool classes)
    {
        regexTable = true;
        regexClasses |= classes;
        return "@\"regex.tables\"";
    }

    /// <summary>Whether the program makes a <c>Regex</c>, and whether one of its patterns can need the classes of <c>\p</c>.</summary>
    private bool regexTable, regexClasses;

    /// <summary>The global of <see cref="RegexTable"/>, once, where the program makes a <c>Regex</c>.</summary>
    private void EmitRegexTable()
    {
        if (regexTable)
        {
            const string name = "@\"regex.tables\"";
            byte[] bytes = regexClasses ? RegexPattern.Properties : [];
            var packed = new int[(bytes.Length + 3) / 4];
            for (var i = 0; i < bytes.Length; i++)
            {
                packed[i / 4] |= bytes[i] << (8 * (i % 4));
            }

            int[][] parts = [CaseTables.RegexDigit, CaseTables.RegexWord, CaseTables.RegexSpace, CaseTables.RegexFold, CaseTables.RegexAlpha,
                CaseTables.RegexNumber, packed];
            int[] all = [.. parts.Select(p => p.Length), .. parts.SelectMany(p => p)];
            module.Global($"{name} = private unnamed_addr constant [{all.Length} x i32] [{string.Join(", ", all.Select(w => $"i32 {w}"))}], align 4");
        }
    }
}
