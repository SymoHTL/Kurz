using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- running a program (S17, S18, S21) -------------------------------------------------------------

    /// <summary>What a build for Windows says <c>not supported</c> with at a call on a command (S11, S17 to S21).</summary>
    internal const string CommandNotOnWindows = "running a program, `Command`, which the runtime of Windows does not give (S11, S17 to S21)";

    /// <summary>Whether the data type is <c>ExitError</c>, which no file can declare beside the one every file sees (S18, V9).</summary>
    private static bool IsExitError(KurzType type) => type is DataType { Symbol.Name: "ExitError" };

    /// <summary>
    /// S17: a command is the block of a list of strings: the program, then three strings for each member in the order of the calls, its
    /// tag and its two values, which <c>runtime/common/</c> appends to a copy of the list, so that the command a member is called on
    /// stays as it was; S18, S21: <c>Output()</c> and <c>Status()</c> are the runtime's <c>CommandOutput</c> and <c>CommandStatus</c> of
    /// the target, which start the child, wait for its end and give the call's union. Each call is <c>not supported</c> in a build for
    /// Windows (S11).
    /// </summary>
    private string EmitCommandCall(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, CommandNotOnWindows);
        }

        var m = (MemberAccess)c.Callee;
        KurzType[] slots = m.Member switch
        {
            "New" or "Arg" or "Dir" or "EnvRemove" => [Types.String],
            "Env" => [Types.String, Types.String],
            "Args" => [new ListType(Types.String)],
            "Stdin" or "Stdout" or "Stderr" => [TypeOf(c.Arguments[0].Value)],
            _ => [],
        };
        var command = info.Kind == CallKind.CommandNew ? null : $"ptr {EmitExpression(m.Target)}"; // the command before its arguments, as written
        var arguments = Arguments(c, info, j => slots[j], _ => false, _ => null, cConvention: false, owned: false);
        if (command is null)
        {
            return fn.Value($"call ptr {RuntimeFunction("CommandNew")}({arguments[0]})");
        }

        var empty = $"%kz.str {StringValue(string.Empty)}";
        return info.Kind switch
        {
            CallKind.CommandAdd => fn.Value(m.Member switch
            {
                "Arg" => $"call ptr {RuntimeFunction("CommandAdd")}({command}, %kz.str {StringValue("a")}, {arguments[0]}, {empty})",
                "Dir" => $"call ptr {RuntimeFunction("CommandAdd")}({command}, %kz.str {StringValue("d")}, {arguments[0]}, {empty})",
                "Env" => $"call ptr {RuntimeFunction("CommandAdd")}({command}, %kz.str {StringValue("e")}, {arguments[0]}, {arguments[1]})",
                "EnvRemove" => $"call ptr {RuntimeFunction("CommandAdd")}({command}, %kz.str {StringValue("r")}, {arguments[0]}, {empty})",
                _ => $"call ptr {RuntimeFunction("CommandAdd")}({command}, %kz.str {StringValue("c")}, {empty}, {empty})",
            }),
            CallKind.CommandArgs => fn.Value($"call ptr {RuntimeFunction("CommandArgs")}({command}, {arguments[0]})"),
            CallKind.CommandStream => fn.Value($"call ptr {RuntimeFunction("CommandStream")}({command}, i32 {(m.Member == "Stdin" ? "0" : m.Member == "Stdout" ? "1" : "2")}, {arguments[0]})"),
            CallKind.CommandOutput => fn.Value($"call {Ir(Sub(info.Result))} {SysFunction("CommandOutput")}({command})"),
            _ => fn.Value($"call {Ir(Sub(info.Result))} {SysFunction("CommandStatus")}({command})"),
        };
    }
}
