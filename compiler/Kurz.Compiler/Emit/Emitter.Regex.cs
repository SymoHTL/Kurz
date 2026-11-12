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
        return fn.Value($"call {Ir(result)} {name}({arguments[0]}, ptr {RegexTable()}, {flag})");
    }

    /// <summary>
    /// The global that holds G4's tables, written once into a program that makes a <c>Regex</c>, as the standard library's
    /// <c>RegexTablePart</c> reads it: six counts of 32 bits, of the numbers of the ranges of <c>\d</c>, <c>\w</c> and <c>\s</c>, of
    /// the pairs of the folding and of the ranges of the characters of a group's name, Alphabetic and the categories Nd, Nl and No,
    /// then those numbers, 32 bits each, from <see cref="CaseTables"/>.
    /// </summary>
    private string RegexTable()
    {
        const string name = "@\"regex.tables\"";
        if (helpers.Add(name))
        {
            int[][] parts = [CaseTables.RegexDigit, CaseTables.RegexWord, CaseTables.RegexSpace, CaseTables.RegexFold, CaseTables.RegexAlpha,
                CaseTables.RegexNumber];
            int[] all = [.. parts.Select(p => p.Length), .. parts.SelectMany(p => p)];
            module.Global($"{name} = private unnamed_addr constant [{all.Length} x i32] [{string.Join(", ", all.Select(w => $"i32 {w}"))}], align 4");
        }

        return name;
    }
}
