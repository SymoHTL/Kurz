using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    // ---- running a program (chapter 13) --------------------------------------------------------------

    /// <summary>
    /// S17: <c>Command.New(program)</c>, the one function of <c>Command</c>, which names the program to run (section 12, the owner,
    /// 2026-11-01, "Chain, each step a new value").
    /// </summary>
    private Typed CheckCommandFunction(MemberAccess m, CallExpression c)
    {
        if (m.Member != "New")
        {
            Report(ErrorIds.UnknownName, m.Line, $"`Command` has no function `{m.Member}` (S17, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, "`Command.New`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = [new Slot("program", Types.String, false, false, false)], Result = Types.Command };
        return Record(c, CallKind.CommandNew, Call([candidate], c, null, out var picked), picked);
    }

    /// <summary>
    /// S17: a command's members, each of which gives a new command: <c>Arg(x)</c>, <c>Args(list)</c>, <c>Dir(path)</c>,
    /// <c>Env(name, value)</c>, <c>EnvRemove(name)</c>, <c>EnvClear()</c>, <c>Stdin(s)</c>, <c>Stdout(s)</c> and <c>Stderr(s)</c>; S18:
    /// <c>Output()</c>, whose success is a <c>ProcessOutput</c>, and <c>Status()</c>, whose success is <c>void</c>, each with the cases
    /// <c>IoError</c> and <c>ExitError</c> (section 12, the owner, 2026-11-01, "Bytes, as Rust" and "A case of the call"); S19:
    /// <c>Input(bytes)</c> and <c>Input(text)</c>, the child's input (the owner, 2026-11-01, "Both forms", and 2026-11-11, "Bytes and string"); S20: <c>Timeout(d)</c>, a bound on the child's
    /// time (the owner, 2026-11-01, "Optional .Timeout(d)"); S19: <c>Spawn()</c>, whose success is a <c>ChildProcess</c>, with the case
    /// <c>IoError</c>.
    /// </summary>
    private Typed CheckCommandMethod(MemberAccess m, CallExpression c)
    {
        Slot Text(string name) => new(name, Types.String, false, false, false);
        var stdio = new EnumType(enums["Stdio"]);
        var exitError = new DataType(dataTypes["ExitError"], []);
        (CallKind Kind, List<Slot> Slots, KurzType Result)? member = m.Member switch
        {
            "Arg" => (CallKind.CommandAdd, [Text("x")], Types.Command),
            "Args" => (CallKind.CommandArgs, [new Slot("list", new ListType(Types.String), false, false, false)], Types.Command),
            "Dir" => (CallKind.CommandAdd, [Text("path")], Types.Command),
            "Env" => (CallKind.CommandAdd, [Text("name"), Text("value")], Types.Command),
            "EnvRemove" => (CallKind.CommandAdd, [Text("name")], Types.Command),
            "EnvClear" => (CallKind.CommandAdd, [], Types.Command),
            "Stdin" or "Stdout" or "Stderr" => (CallKind.CommandStream, [new Slot("s", stdio, false, false, false)], Types.Command),
            "Input" => (CallKind.CommandInput, [new Slot("bytes", new ListType(Types.Byte), false, false, false)], Types.Command),
            "Timeout" => (CallKind.CommandTimeout, [new Slot("d", Types.Duration, false, false, false)], Types.Command),
            "Output" => (CallKind.CommandOutput, [], Types.Union([new DataType(dataTypes["ProcessOutput"], []), IoError, exitError])),
            "Status" => (CallKind.CommandStatus, [], Types.Union([Types.Void, IoError, exitError])),
            "Spawn" => (CallKind.CommandSpawn, [], Types.Union([Types.ChildProcess, IoError])),
            _ => null,
        };
        if (member is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"a `Command` has no member `{m.Member}` (S17 to S21, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        List<Candidate> candidates = [new Candidate { Slots = f.Slots, Result = f.Result }];
        if (m.Member == "Input")
        {
            // S19: `Bytes` or a `string`, whose UTF-8 bytes are the input (the owner, 2026-11-11, "Bytes and string"), two forms, so that a
            // call that fits neither is `argument-mismatch` with a line for each (F13)
            candidates = [new Candidate { Slots = f.Slots, Result = f.Result, Form = "Command Input(Bytes bytes)" }, new Candidate { Slots = [Text("text")], Result = f.Result, Form = "Command Input(string text)" }];
        }

        return Record(c, f.Kind, Call(candidates, c, null, out var picked), picked);
    }

    /// <summary>
    /// S19: a child's members that are calls: <c>Wait()</c>, whose success is <c>void</c>, and <c>WaitWithOutput()</c>, whose success is
    /// a <c>ProcessOutput</c>, each with the cases <c>IoError</c> and <c>ExitError</c>, as <c>Status()</c> and <c>Output()</c> give them,
    /// and <c>Kill()</c>, whose success is <c>void</c>, with the case <c>IoError</c>. <c>Stdin</c>, <c>Stdout</c>, <c>Stderr</c> and
    /// <c>Id</c> are read, not called.
    /// </summary>
    private Typed CheckChildMethod(MemberAccess m, CallExpression c)
    {
        var exitError = new DataType(dataTypes["ExitError"], []);
        (CallKind Kind, KurzType Result)? member = m.Member switch
        {
            "Wait" => (CallKind.ChildWait, Types.Union([Types.Void, IoError, exitError])),
            "WaitWithOutput" => (CallKind.ChildWaitWithOutput, Types.Union([new DataType(dataTypes["ProcessOutput"], []), IoError, exitError])),
            "Kill" => (CallKind.ChildKill, Types.Union([Types.Void, IoError])),
            _ => null,
        };
        if (member is not { } f)
        {
            Report(ErrorIds.UnknownName, m.Line, $"a `ChildProcess` has no member `{m.Member}` to call (S19, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = [], Result = f.Result };
        return Record(c, f.Kind, Call([candidate], c, null, out var picked), picked);
    }
}
