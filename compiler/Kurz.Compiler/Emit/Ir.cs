using System.Globalization;
using System.Text;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The text of one LLVM IR module as it is built: the types of a string, <c>%kz.str</c>, and of a
/// function value, <c>%kz.fn</c>, the string constants, the state outside a function (R13), the
/// declarations of the C functions the code calls (R16), and the functions emitted, each rendered by
/// its <see cref="IrFunction"/>.
/// </summary>
internal sealed class IrModule
{
    private readonly StringBuilder globals = new();
    private readonly List<string> functions = [];
    private readonly Dictionary<string, string> strings = new(StringComparer.Ordinal);
    private readonly HashSet<string> declared = new(StringComparer.Ordinal);

    public IrModule()
    {
        globals.AppendLine("%kz.str = type { ptr, i64 }");
        globals.AppendLine("%kz.fn = type { ptr, ptr }");
    }

    /// <summary>The global that holds the UTF-8 bytes of a string literal, as <c>ptr</c>, with their count.</summary>
    public (string Pointer, int Length) String(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var key = Convert.ToBase64String(bytes);
        if (!strings.TryGetValue(key, out var name))
        {
            name = $"@.str.{strings.Count}";
            strings[key] = name;
            var escaped = new StringBuilder();
            foreach (var b in bytes)
            {
                if (b is >= 0x20 and < 0x7F && b != (byte)'"' && b != (byte)'\\')
                {
                    escaped.Append((char)b);
                }
                else
                {
                    escaped.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            globals.Append(name).Append(" = private unnamed_addr constant [").Append(bytes.Length.ToString(CultureInfo.InvariantCulture))
                .Append(" x i8] c\"").Append(escaped).AppendLine("\", align 1");
        }

        return (name, bytes.Length);
    }

    /// <summary>Declares a function once: <c>declare i64 @write(i32, ptr, i64)</c>.</summary>
    public void Declare(string declaration)
    {
        if (declared.Add(declaration))
        {
            globals.AppendLine(declaration);
        }
    }

    /// <summary>A global constant or variable, written as it is.</summary>
    public void Global(string text) => globals.AppendLine(text);

    public void Add(IrFunction function) => functions.Add(function.Render());

    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append(globals);
        sb.AppendLine();
        foreach (var f in functions)
        {
            sb.Append(f);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// One function while its body is emitted: temporaries, blocks and the allocas of its locals, which
/// all stand in the entry block so that the optimiser turns them into registers.
/// </summary>
internal sealed class IrFunction
{
    private readonly string signature;
    private readonly StringBuilder allocas = new();
    private readonly StringBuilder body = new();
    private readonly List<string> attributes;
    private int next;
    private int labels;

    /// <param name="signature">The text between <c>define</c> and the body: <c>i32 @main()</c>.</param>
    /// <param name="attributes">Function attributes written after the signature.</param>
    public IrFunction(string signature, params string[] attributes)
    {
        this.signature = signature;
        this.attributes = attributes.ToList();
    }

    /// <summary>Whether the current block ended in a terminator, so that the next instruction needs a block.</summary>
    public bool Terminated { get; private set; }

    private string Temp() => $"%t{++next}";

    /// <summary>A local's storage in the entry block; <paramref name="hint"/> names it for a reader.</summary>
    public string Alloca(string type, string hint)
    {
        var name = $"%v.{hint}.{++next}";
        allocas.Append("  ").Append(name).Append(" = alloca ").AppendLine(type);
        return name;
    }

    public string Label(string hint) => $"{hint}.{++labels}";

    public void Emit(string instruction)
    {
        if (Terminated)
        {
            // an instruction after a terminator needs a block of its own, which nothing reaches: the optimiser removes it
            Block(Label("dead"));
        }

        body.Append("  ").AppendLine(instruction);
    }

    public string Value(string instruction)
    {
        var t = Temp();
        Emit($"{t} = {instruction}");
        return t;
    }

    /// <summary>Starts the block; the block before it falls through with an unconditional branch where it did not end.</summary>
    public void Block(string label)
    {
        if (!Terminated && body.Length > 0)
        {
            body.Append("  br label %").AppendLine(label);
        }

        body.Append(label).AppendLine(":");
        Terminated = false;
    }

    public void Branch(string label)
    {
        Emit($"br label %{label}");
        Terminated = true;
    }

    public void CondBranch(string condition, string then, string otherwise)
    {
        Emit($"br i1 {condition}, label %{then}, label %{otherwise}");
        Terminated = true;
    }

    /// <summary>Ends the block with a terminator of its own: a <c>switch</c>.</summary>
    public void Terminate(string instruction)
    {
        Emit(instruction);
        Terminated = true;
    }

    public void Return(string? typedValue)
    {
        Emit(typedValue is null ? "ret void" : $"ret {typedValue}");
        Terminated = true;
    }

    public void Unreachable()
    {
        Emit("unreachable");
        Terminated = true;
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append("define ").Append(signature);
        foreach (var a in attributes)
        {
            sb.Append(' ').Append(a);
        }

        sb.AppendLine(" {");
        sb.AppendLine("entry:");
        sb.Append(allocas);
        sb.Append(body);
        if (!Terminated)
        {
            sb.AppendLine("  unreachable");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
