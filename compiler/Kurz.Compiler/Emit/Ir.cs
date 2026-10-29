using System.Globalization;
using System.Text;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The text of one LLVM IR module as it is built: the types of a string, <c>%kz.str</c>, of a
/// function value, <c>%kz.fn</c>, and of a <c>decimal</c>, <c>%kz.dec</c>, the string constants, the state outside a function (R13), the
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
        globals.AppendLine("%kz.dec = type { i64, i64 }");
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
            globals.Append(name).Append(" = private unnamed_addr constant [").Append(bytes.Length.ToString(CultureInfo.InvariantCulture))
                .Append(" x i8] c").Append(Quote(text)).AppendLine(", align 1");
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

    /// <summary>The line tables of the module, null where the build carries none.</summary>
    public DebugInfo? Debug { get; init; }

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

        Debug?.Render(sb);
        return sb.ToString();
    }

    /// <summary>A string of IR metadata or of a name in quotes, its quote, its backslash and every byte outside printable ASCII escaped.</summary>
    public static string Quote(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (b is >= 0x20 and < 0x7F && b != (byte)'"' && b != (byte)'\\')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('\\').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.Append('"').ToString();
    }
}

/// <summary>
/// The line tables of a module as LLVM's debug metadata (design record, section 2): one compile unit, a file per source the module
/// holds code of, named as a compile error names it in the directory the build ran in (the owner, 2026-10-29, "As Rust: the
/// build's directory"), a subprogram per function with the name a trace shows, and a location per line of each function, which
/// LLVM writes as DWARF into the executable on Linux and as CodeView into the PDB on Windows. Only the line tables: no variables
/// and no types.
/// </summary>
internal sealed class DebugInfo
{
    private readonly List<string> nodes = [];
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Line, string Scope), string> locations = [];
    private readonly string unit;
    private readonly string subroutine;
    private readonly string directory;
    private readonly bool codeView;
    private readonly bool optimized;

    /// <param name="directory">The directory the build ran in, which names where a file named by a relative path lies.</param>
    /// <param name="first">The program's file, which names the compile unit.</param>
    /// <param name="producer">The compiler and its version.</param>
    /// <param name="optimized">Whether the build optimises, as a release build does.</param>
    /// <param name="codeView">Whether the tables are CodeView, Windows', or DWARF, Linux'.</param>
    public DebugInfo(string directory, string first, string producer, bool optimized, bool codeView)
    {
        this.directory = directory;
        this.codeView = codeView;
        this.optimized = optimized;
        unit = $"!{nodes.Count}";
        nodes.Add(string.Empty); // the compile unit, written once its file has a number
        nodes[0] = $"{unit} = distinct !DICompileUnit(language: DW_LANG_C, file: {File(first)}, producer: {IrModule.Quote(producer)}, "
            + $"isOptimized: {(optimized ? "true" : "false")}, runtimeVersion: 0, emissionKind: LineTablesOnly)";
        subroutine = Node("!DISubroutineType(types: !{})");
    }

    private string Node(string text)
    {
        var id = $"!{nodes.Count}";
        nodes.Add($"{id} = {text}");
        return id;
    }

    /// <summary>The file of a source, as a compile error names it, once per name.</summary>
    public string File(string name)
    {
        if (!files.TryGetValue(name, out var id))
        {
            id = Node($"!DIFile(filename: {IrModule.Quote(name)}, directory: {IrModule.Quote(directory)})");
            files[name] = id;
        }

        return id;
    }

    /// <summary>The subprogram of a function: <paramref name="name"/> as a trace shows it, <paramref name="symbol"/> its name in the module.</summary>
    public string Subprogram(string name, string symbol, string file, int line)
    {
        var f = File(file);
        var flags = optimized ? "DISPFlagDefinition | DISPFlagOptimized" : "DISPFlagDefinition";
        return Node($"distinct !DISubprogram(name: {IrModule.Quote(name)}, linkageName: {IrModule.Quote(symbol)}, scope: {f}, file: {f}, "
            + $"line: {line.ToString(CultureInfo.InvariantCulture)}, type: {subroutine}, scopeLine: {line.ToString(CultureInfo.InvariantCulture)}, "
            + $"spFlags: {flags}, unit: {unit})");
    }

    /// <summary>The location of a line in a subprogram, once per pair.</summary>
    public string Location(int line, string scope)
    {
        if (!locations.TryGetValue((line, scope), out var id))
        {
            id = Node($"!DILocation(line: {line.ToString(CultureInfo.InvariantCulture)}, column: 0, scope: {scope})");
            locations[(line, scope)] = id;
        }

        return id;
    }

    public void Render(StringBuilder sb)
    {
        var version = Node("!{i32 2, !\"Debug Info Version\", i32 3}");
        var format = Node(codeView ? "!{i32 2, !\"CodeView\", i32 1}" : "!{i32 7, !\"Dwarf Version\", i32 5}");
        sb.Append("!llvm.dbg.cu = !{").Append(unit).AppendLine("}");
        sb.Append("!llvm.module.flags = !{").Append(version).Append(", ").Append(format).AppendLine("}");
        foreach (var n in nodes)
        {
            sb.AppendLine(n);
        }
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

    /// <summary>The function's subprogram in the line tables, null where the build carries none or the function has no source.</summary>
    public string? Subprogram { get; set; }

    /// <summary>The location every instruction emitted from here on carries, null for none.</summary>
    public string? Location { get; set; }

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

        body.Append("  ").Append(instruction);
        if (Location is not null)
        {
            body.Append(", !dbg ").Append(Location);
        }

        body.AppendLine();
    }

    public string Value(string instruction)
    {
        var t = Temp();
        Emit($"{t} = {instruction}");
        return t;
    }

    /// <summary>Starts the block; the block before it falls through with an unconditional branch where it did not end, the entry block among them.</summary>
    public void Block(string label)
    {
        if (!Terminated)
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

        if (Subprogram is not null)
        {
            sb.Append(" !dbg ").Append(Subprogram);
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
