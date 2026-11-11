using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- running a program (S17 to S21) ----------------------------------------------------------------

    /// <summary>What a build for Windows says <c>not supported</c> with at a call on a command (S11, S17 to S21).</summary>
    internal const string CommandNotOnWindows = "running a program, `Command`, which the runtime of Windows does not give (S11, S17 to S21)";

    /// <summary>Whether the data type is <c>ExitError</c>, which no file can declare beside the one every file sees (S18, V9).</summary>
    private static bool IsExitError(KurzType type) => type is DataType { Symbol.Name: "ExitError" };

    /// <summary>
    /// S17: a command is the block of a list of strings: the program, then three strings for each member in the order of the calls, its
    /// tag and its two values, which <c>runtime/common/</c> appends through a <c>mut</c> list. A member is given its command as a value
    /// of its own: the result of the member before it in a chain, which no other value holds and which it takes over, so that the chain
    /// builds into one block, or a command a variable holds counted once more, so that the first <c>Add</c> copies it and the variable
    /// keeps its command (M3; section 12, the owner, 2026-11-01, P1, "Chain, each step a new value"). S18, S21: <c>Output()</c> and
    /// <c>Status()</c> are the runtime's <c>CommandOutput</c> and <c>CommandStatus</c> of the target, which start the child, wait for its
    /// end and give the call's union. Each call is <c>not supported</c> in a build for Windows (S11).
    /// </summary>
    private string EmitCommandCall(CallExpression c, CallInfo info)
    {
        if (target != Target.Linux)
        {
            throw Unsupported(c.Line, CommandNotOnWindows);
        }

        var m = (MemberAccess)c.Callee;
        if (info.Kind == CallKind.CommandNew)
        {
            var program = Arguments(c, info, _ => Types.String, _ => false, _ => null, cConvention: false, owned: false);
            return fn.Value($"call ptr {RuntimeFunction("CommandNew")}({program[0]})");
        }

        if (info.Kind is CallKind.CommandOutput or CallKind.CommandStatus)
        {
            var run = EmitExpression(m.Target);
            return fn.Value($"call {Ir(Sub(info.Result))} {SysFunction(info.Kind == CallKind.CommandOutput ? "CommandOutput" : "CommandStatus")}(ptr {run})");
        }

        var slot = fn.Alloca("ptr", "command");
        fn.Emit($"store ptr {Owned(m.Target)}, ptr {slot}"); // the command before its arguments, as written
        Append(c, info, slot, MemberArguments(c, info));
        return fn.Value($"load ptr, ptr {slot}");
    }

    /// <summary>
    /// The arguments of a member of S17, read in the order they are written; the callee borrows them. With <paramref name="held"/> each
    /// one that is no temporary of a region is counted once more, a temporary of the region being emitted, whose end, a <c>return</c> or a
    /// <c>break</c> lets go of it, so that it outlives a change of the place it was read from (<see cref="EmitCommandInPlace"/>).
    /// </summary>
    private List<string> MemberArguments(CallExpression c, CallInfo info, bool held = false)
    {
        KurzType[] slots = ((MemberAccess)c.Callee).Member switch
        {
            "Arg" or "Dir" or "EnvRemove" => [Types.String],
            "Env" => [Types.String, Types.String],
            "Args" => [new ListType(Types.String)],
            "Stdin" or "Stdout" or "Stderr" => [TypeOf(c.Arguments[0].Value)],
            "Input" => [TypeOf(c.Arguments[0].Value) is PrimitiveType { Kind: Primitive.String } ? Types.String : new ListType(Types.Byte)], // S19's two forms
            "Timeout" => [Types.Duration],
            _ => [],
        };
        var arguments = Arguments(c, info, j => slots[j], _ => false, _ => null, cConvention: false, owned: false);
        for (var j = 0; held && j < arguments.Count; j++)
        {
            var type = Sub(slots[j]);
            var value = arguments[j][(Ir(type).Length + 1)..]; // `Arguments` writes each as its type and its value
            if (Counted(type) && !regions.Any(r => r.Exists(t => t.Value == value)))
            {
                Retain(value, type);
                Temp(value, type);
            }
        }

        return arguments;
    }

    /// <summary>
    /// S17: the member of the call <paramref name="c"/>, with its <paramref name="arguments"/>, appended to the command at
    /// <paramref name="slot"/>, which holds a count of it: in place where no other value holds its block, else into a copy that the slot
    /// then holds, as a list's <c>Add</c> writes (M3).
    /// </summary>
    private void Append(CallExpression c, CallInfo info, string slot, List<string> arguments)
    {
        var member = ((MemberAccess)c.Callee).Member;
        var empty = $"%kz.str {StringValue(string.Empty)}";
        fn.Emit(info.Kind switch
        {
            CallKind.CommandAdd => member switch
            {
                "Arg" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("a")}, {arguments[0]}, {empty})",
                "Dir" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("d")}, {arguments[0]}, {empty})",
                "Env" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("e")}, {arguments[0]}, {arguments[1]})",
                "EnvRemove" => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("r")}, {arguments[0]}, {empty})",
                _ => $"call void {RuntimeFunction("CommandAdd")}(ptr {slot}, %kz.str {StringValue("c")}, {empty}, {empty})",
            },
            CallKind.CommandArgs => $"call void {RuntimeFunction("CommandArgs")}(ptr {slot}, {arguments[0]})",
            CallKind.CommandInput => $"call void {RuntimeFunction(TypeOf(c.Arguments[0].Value) is PrimitiveType { Kind: Primitive.String } ? "CommandInputText" : "CommandInput")}(ptr {slot}, {arguments[0]})",
            CallKind.CommandTimeout => $"call void {RuntimeFunction("CommandTimeout")}(ptr {slot}, {arguments[0]})", // a `duration` is its nanoseconds, the `long` the runtime takes
            _ => $"call void {RuntimeFunction("CommandStream")}(ptr {slot}, i32 {(member == "Stdin" ? "0" : member == "Stdout" ? "1" : "2")}, {arguments[0]})",
        });
    }

    /// <summary>
    /// S17: <c>cmd = cmd.Arg(x)</c>, an assignment to a variable of a chain of members on that variable, written as the members appended
    /// to the variable itself, so that a command no other value holds changes in place, since the assignment keeps no earlier command,
    /// and one another value holds is copied once, by the first <c>Add</c>. The arguments of every member are read first, in the order
    /// they are written, so that an argument that reads the variable reads the command before any member is appended, and a case an
    /// argument passes on (O2) leaves the variable as it was. The checker holds a borrowed argument unchanged only across its own call
    /// (M7), while here the arguments of a member stay read until the members after it have read theirs, which can change the place it
    /// was read from, <c>cmd = cmd.Arg(s).Arg(Take(mut s))</c>: so each argument of a member before the last is counted for the
    /// statement (<see cref="MemberArguments"/>), as the line <c>command-chain-fenced</c> of <c>ListTests</c> holds. An argument that
    /// changes the variable itself, <c>cmd = cmd.Arg(Reset(mut cmd))</c>, is refused by the checker, <c>changed-while-held</c>, since a
    /// <c>Command</c> is not copied (M7), so the command the members are appended to is the one read before the arguments; a field takes
    /// the ordinary path, since this one takes a variable alone. False, with nothing emitted, where the value is no such chain.
    /// </summary>
    private bool EmitCommandInPlace(Expression assigned, VariableSymbol variable, string place)
    {
        var chain = new List<CallExpression>();
        var value = Unwrap(assigned);
        while (value is CallExpression c && Model.CallOf(c).Kind is CallKind.CommandAdd or CallKind.CommandArgs or CallKind.CommandStream or CallKind.CommandInput or CallKind.CommandTimeout)
        {
            chain.Insert(0, c);
            value = Unwrap(((MemberAccess)c.Callee).Target);
        }

        if (target != Target.Linux || chain.Count == 0 || value is not NameExpression n || !ReferenceEquals(Model.VariableOf(n), variable))
        {
            return false;
        }

        var arguments = chain.Select((c, i) => MemberArguments(c, Model.CallOf(c), held: i < chain.Count - 1)).ToList();
        for (var i = 0; i < chain.Count; i++)
        {
            Append(chain[i], Model.CallOf(chain[i]), place, arguments[i]);
        }

        return true;
    }
}
