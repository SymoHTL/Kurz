using System.Globalization;
using System.Text;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Inline assembly (R11) and the functions with no frame (R12), lowered as Rust's compiler lowers <c>asm!</c> and <c>naked_asm!</c> to
/// LLVM (its <c>asm.rs</c> and <c>naked_asm.rs</c>): an <c>asm</c> statement is a call of an inline-assembly value whose constraints list
/// the outputs, then the inputs, an <c>inout</c> as an output and an input tied to it, then the clobbers; a <c>naked</c> function is
/// assembly at the module's level under its symbol, which the code calls through a declaration, so that the compiler gives it no
/// prologue and no epilogue.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>The functions and the state a <c>naked</c> body names by <c>sym</c>, which only the module's assembly reaches, kept in <c>llvm.compiler.used</c>.</summary>
    private readonly SortedSet<string> asmUsed = new(StringComparer.Ordinal);

    /// <summary>
    /// R11, R12: each <c>asm</c> of the program's file, as IR without the rest of the program, in the order emitted, with the function that
    /// holds an <c>asm</c> statement (<see cref="EmittedProgram.Asms"/>).
    /// </summary>
    private readonly List<(AsmAlone Asm, IrFunction? Holder)> asmsAlone = [];

    /// <summary>
    /// R11, R12: the program's <c>asm</c>s in the order LLVM meets them (the owner, 2026-11-13, pull request 530, comment 6855725580, "LLVM's
    /// order everywhere"): the <c>naked</c> functions' module-level assembly, which LLVM writes before every function, in the order emitted,
    /// the order of their first calls, then each <c>asm</c> statement, in the order of the functions that hold them in the module, a guarded
    /// block's before the function it stands in (R18), and of the statements in each.
    /// </summary>
    private List<AsmAlone> AsmsInLlvmOrder() =>
    [
        .. asmsAlone.Where(a => a.Holder is null).Select(a => a.Asm),
        .. asmsAlone.Where(a => a.Holder is not null).OrderBy(a => emitted.IndexOf(a.Holder!)).Select(a => a.Asm),
    ];

    /// <summary>
    /// One register operand of an <c>asm</c>, as the first pass read it: its class, its input and where its output goes. The input is
    /// read where the first pass reads the operand, but an <c>inout</c>'s, which <c>Input</c> loads when it is called, just before
    /// the call.
    /// </summary>
    private sealed record AsmRegister(AsmOperand Operand, string RegisterClass, string Constraint, string OperandType, Func<string>? Input, KurzType? InputType, AsmOutput? Output);

    /// <summary>
    /// R11: where an output goes: what a pointer points to, <c>*p</c> or <c>p[i]</c>, found where its operand stands; or a variable, or a
    /// path of fields of a <c>data</c> value in one (M4), whose place is found when the output is stored, after the call, since the place
    /// of a path into a block is a copy of the block that a later operand, or the place of another output, can let go of.
    /// </summary>
    private sealed record AsmOutput(KurzType Type, string? Pointer, NameExpression? Variable, List<MemberAccess> Members);

    /// <summary>R11: the classes of x86-64 a value goes into, each with LLVM's constraint letter.</summary>
    private static readonly Dictionary<string, string> AsmClassConstraints = new(StringComparer.Ordinal)
    {
        ["reg"] = "r",
        ["reg_abcd"] = "Q",
        ["reg_byte"] = "q",
        ["xmm_reg"] = "x",
    };

    /// <summary>
    /// R11: an <c>asm</c> statement. Every operand's value is read, but an <c>inout</c>'s, which is loaded just before the call, and every
    /// place an output writes through a pointer found, in the order the operands are written, as Rust evaluates them;
    /// the call then writes each output into its place, the place of a variable or of a
    /// path of fields found as the output is stored, so that no copy of a block a path goes through is held across another operand (<see cref="AsmOutput"/>). <c>options</c> map as Rust maps them: no
    /// <c>sideeffect</c> under <c>pure</c>, no <c>alignstack</c> under <c>nostack</c>, AT&amp;T's dialect under <c>att_syntax</c> and Intel's
    /// otherwise, no clobber of the flags under <c>preserves_flags</c>, no clobber of memory under <c>nomem</c>, the call's attributes
    /// <c>willreturn</c> and <c>memory(none)</c> under <c>pure</c> and <c>nomem</c>, or <c>memory(read)</c> under <c>pure</c> and <c>readonly</c>,
    /// <c>memory(inaccessiblemem: readwrite)</c> under <c>nomem</c> alone, and the end of the block after the call under <c>noreturn</c>.
    /// <c>clobber_abi</c> adds, as Rust does, an output that is let go of for each register the ABI does not keep across a call and no
    /// output names, and a clobber for each one no operand of Kurz can name (<see cref="OnlyClobbered"/>). A <c>label</c> operand is not
    /// laid out.
    /// </summary>
    private void EmitAsm(AsmStatement a)
    {
        if (!cpu.X86)
        {
            throw Unsupported(a.Line, "an `asm`, which the compiler builds for x86-64 alone (R11)"); // the checker reads its operands for x86-64
        }

        var options = new HashSet<string>(StringComparer.Ordinal);
        var abis = new List<string>();
        foreach (var o in a.Operands.Where(o => o.Word is "options" or "clobber_abi"))
        {
            if (o.Word == "options")
            {
                options.UnionWith(o.Arguments!.Select(x => x.Text));
            }
            else
            {
                abis.AddRange(o.Arguments!.Select(x => x.Text));
            }
        }

        var registers = new Dictionary<AsmOperand, AsmRegister>(ReferenceEqualityComparer.Instance);
        var symbols = new Dictionary<AsmOperand, string>(ReferenceEqualityComparer.Instance);
        foreach (var o in a.Operands)
        {
            switch (o.Word)
            {
                case "options" or "clobber_abi" or "const":
                    break;
                case "sym":
                    symbols[o] = SymValue(o);
                    break;
                case "label":
                    throw Unsupported(o.Line, "a `label` operand of an `asm` (R11)");
                default:
                    registers[o] = ReadAsmRegister(o);
                    break;
            }
        }

        var outputs = new List<(string Constraint, string Type, AsmRegister? Register)>();
        var outputIndex = new Dictionary<AsmOperand, int>(ReferenceEqualityComparer.Instance);
        var written = new HashSet<string>(StringComparer.Ordinal); // the registers an operand's output names, which no clobber names again
        foreach (var o in a.Operands)
        {
            if (!registers.TryGetValue(o, out var r) || o.Word == "in")
            {
                continue; // the outputs in the order the operands are written
            }

            if (ExplicitBase(r.Operand) is { } b)
            {
                written.Add(b);
            }

            outputIndex[r.Operand] = outputs.Count;
            outputs.Add(((r.Operand.Word is "lateout" or "inlateout" ? "=" : "=&") + r.Constraint, r.OperandType, r));
        }

        var clobbers = new List<string>();
        foreach (var register in abis.SelectMany(AbiClobbers).Distinct(StringComparer.Ordinal))
        {
            if (OnlyClobbered(register))
            {
                clobbers.Add($"~{{{register}}}");
            }
            else if (written.Add(register))
            {
                outputs.Add(($"={{{register}}}", register.StartsWith("xmm", StringComparison.Ordinal) ? "<4 x float>" : "i64", null)); // Rust's `lateout(reg) _`
            }
        }

        var inputs = new List<(string Constraint, string Typed, string Alone)>(); // Alone: the input in the module that holds the `asm` alone
        var inputIndex = new Dictionary<AsmOperand, int>(ReferenceEqualityComparer.Instance);
        foreach (var o in a.Operands)
        {
            if (registers.TryGetValue(o, out var r) && r.Input is not null)
            {
                inputIndex[o] = outputs.Count + inputs.Count;
                inputs.Add((o.Word == "in" ? r.Constraint : outputIndex[o].ToString(CultureInfo.InvariantCulture), $"{r.OperandType} {r.Input()}", $"{r.OperandType} poison"));
            }
            else if (symbols.TryGetValue(o, out var symbol))
            {
                inputIndex[o] = outputs.Count + inputs.Count;
                inputs.Add(("s", $"ptr {symbol}", $"ptr {symbol}"));
            }
        }

        if (!options.Contains("preserves_flags"))
        {
            clobbers.AddRange(["~{dirflag}", "~{fpsr}", "~{flags}"]);
        }

        if (!options.Contains("nomem"))
        {
            clobbers.Add("~{memory}");
        }

        var template = AsmTemplate(a, options.Contains("raw"), (o, modifier) => o.Word switch
        {
            "const" => ConstText(o),
            "sym" => $"${{{inputIndex[o].ToString(CultureInfo.InvariantCulture)}:c}}",
            _ => Placeholder(outputIndex.TryGetValue(o, out var i) ? i : inputIndex[o], registers[o].RegisterClass, modifier),
        });
        var constraints = string.Join(",", outputs.Select(x => x.Constraint).Concat(inputs.Select(x => x.Constraint)).Concat(clobbers));
        var resultType = outputs.Count switch
        {
            0 => "void",
            1 => outputs[0].Type,
            _ => "{ " + string.Join(", ", outputs.Select(x => x.Type)) + " }",
        };
        var words = new StringBuilder(); // the words after `asm` and its `sideeffect`
        if (!options.Contains("nostack"))
        {
            words.Append(" alignstack");
        }

        if (!options.Contains("att_syntax"))
        {
            words.Append(" inteldialect");
        }

        // as Rust's compiler sets them: a `pure` asm is of no memory under `nomem`, reads memory under `readonly` and returns; one with an
        // effect and `nomem` touches no memory the program can reach
        var attributes = options.Contains("pure")
            ? options.Contains("nomem") ? " nounwind willreturn memory(none)" : options.Contains("readonly") ? " nounwind willreturn memory(read)" : " nounwind willreturn"
            : options.Contains("nomem") ? " nounwind memory(inaccessiblemem: readwrite)" : " nounwind";
        var call = $"call {resultType} asm{(options.Contains("pure") ? "" : " sideeffect")}{words} {IrModule.Quote(template)}, {IrModule.Quote(constraints)}({string.Join(", ", inputs.Select(x => x.Typed))}){attributes}";
        if (!unit.Runtime)
        {
            // the program's `asm` without the rest of the program, which the build assembles where clang refuses inline assembly
            // (EmittedProgram.Asms): the same template and constraints, a call the build writes in the copies' `main`, with a poison
            // value of its type for each register's input and each symbol a `sym` names declared; the call has an effect and no attribute
            // but `nounwind`, whatever the options, which changes nothing clang assembles: LLVM drops a call of no effect whose value
            // nothing uses, at -O0 too, a `pure` one of `memory(none)` or `memory(read)` with `sideeffect` or without
            // (knowledge/clang-names-no-line-of-the-program-for-a-refused-asm.md)
            var alone = $"call {resultType} asm sideeffect{words} {IrModule.Quote(template)}, {IrModule.Quote(constraints)}({string.Join(", ", inputs.Select(x => x.Alone))}) nounwind";
            asmsAlone.Add((new AsmAlone(a.Line, "R11", [.. symbols.Values.Distinct(StringComparer.Ordinal).Select(s => $"declare void {s}()")], $"  {alone}\n"), fn));
        }

        if (outputs.Count == 0)
        {
            fn.Emit(call);
        }
        else
        {
            var result = fn.Value(call);
            for (var i = 0; i < outputs.Count; i++)
            {
                if (outputs[i].Register is { Output: { } target } r)
                {
                    var value = outputs.Count == 1 ? result : fn.Value($"extractvalue {resultType} {result}, {i.ToString(CultureInfo.InvariantCulture)}");
                    var place = target.Pointer
                        ?? (target.Members.Count == 0 ? Place(Model.VariableOf(target.Variable!), target.Variable!.Line) : WritablePath(target.Variable!, target.Members).Place);
                    fn.Emit($"store {Ir(target.Type)} {FromAsmOperand(value, r.OperandType, target.Type)}, ptr {place}");
                }
            }
        }

        if (options.Contains("noreturn"))
        {
            fn.Unreachable();
        }
    }

    /// <summary>R11: a register operand's input value and output place, in the order of its words, with LLVM's constraint and the type the register holds.</summary>
    private AsmRegister ReadAsmRegister(AsmOperand o)
    {
        var argument = o.Arguments![0];
        var registerClass = argument.Quoted ? Checker.ExplicitRegisterOf(argument.Text).Class : argument.Text;
        var constraint = argument.Quoted ? $"{{{Checker.ExplicitRegisterOf(argument.Text).Base}}}" : AsmClassConstraints[registerClass];
        var reads = o.Word is "in" or "inout" or "inlateout";
        var written = o.Word == "in" ? null : o.Output ?? o.Value;
        string? input = null;
        KurzType? inputType = null;
        AsmOutput? output = null;
        Func<string>? load = null;
        if (o.Word is "inout" or "inlateout" && o.Output is null)
        {
            // an `inout` without `=>` reads and writes one place: a place through a pointer is found once, here, and the input is loaded
            // when the `asm` runs, after every operand ran, as Rust's compiler gives an `inout` the copy of its place as its input
            // (`in_value: Operand::Copy(place)`, rustc_mir_build's builder/expr/into.rs): through the pointer found, or from the place a
            // variable, or a path of fields from one, has then (R11)
            output = AsmOutputOf(written!);
            inputType = output.Type;
            var pointer = output.Pointer;
            var type = inputType;
            load = () => pointer is not null ? fn.Value($"load {Ir(type)}, ptr {pointer}") : EmitExpression(o.Value!);
        }
        else
        {
            if (reads)
            {
                inputType = TypeOf(o.Value!);
                input = EmitExpression(o.Value!);
            }

            if (written is not null && !(Unwrap(written) is NameExpression { Name: "_" }))
            {
                output = AsmOutputOf(written);
            }
        }

        var operandType = AsmOperandType(output?.Type ?? inputType, registerClass);
        if (load is not null)
        {
            return new AsmRegister(o, registerClass, constraint, operandType, () => ToAsmOperand(load(), inputType!, operandType), inputType, output);
        }

        var read = input is null ? null : ToAsmOperand(input, inputType!, operandType);
        return new AsmRegister(o, registerClass, constraint, operandType, read is null ? null : () => read, inputType, output);
    }

    /// <summary>
    /// R11: where an output writes (<see cref="AsmOutput"/>): what a pointer points to, whose place this finds; a variable, or a path of
    /// fields of a <c>data</c> value in one (M4), whose place the store finds after the call.
    /// </summary>
    private AsmOutput AsmOutputOf(Expression output)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(output);
        while (root is MemberAccess m)
        {
            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        var type = TypeOf(output);
        return root switch
        {
            NameExpression n => new AsmOutput(type, null, n, members),
            UnaryExpression { Operator: TokenKind.Star } u when members.Count == 0 => new AsmOutput(type, EmitExpression(u.Operand), null, members),
            IndexExpression i when members.Count == 0 && TypeOf(i.Target) is PointerType p => new AsmOutput(type, ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index), null, members),
            _ => throw Unsupported(output.Line, $"an `asm` output into {ExpressionName(output)} (R11)"),
        };
    }

    /// <summary>The register an operand in an explicit register names, by the register it is a view of, or null.</summary>
    private static string? ExplicitBase(AsmOperand o) =>
        o.Arguments is [{ Quoted: true } r] ? Checker.ExplicitRegisterOf(r.Text).Base : null;

    /// <summary>
    /// R11: the type LLVM gives the register of an operand of a Kurz type: a general register holds an integer of its width or a pointer, and
    /// a <c>float</c> or a <c>double</c> as the integer of its bits; an <c>xmm</c> register a <c>float</c> or a <c>double</c>, and an integer or a
    /// pointer as the <c>float</c> or the <c>double</c> of its bits. An output that is let go of takes the class's widest scalar.
    /// </summary>
    private string AsmOperandType(KurzType? type, string registerClass)
    {
        var vector = registerClass == "xmm_reg";
        if (type is null)
        {
            return vector ? "<4 x float>" : registerClass == "reg_byte" ? "i8" : "i64";
        }

        var ir = Ir(type);
        return (vector, ir) switch
        {
            (false, "float") => "i32",
            (false, "double") => "i64",
            (true, "i32") => "float",
            (true, "i64" or "ptr") => "double",
            _ => ir,
        };
    }

    /// <summary>A value of a Kurz type as the operand type of its register (<see cref="AsmOperandType"/>), its bits unchanged.</summary>
    private string ToAsmOperand(string value, KurzType type, string operandType)
    {
        var ir = Ir(type);
        if (ir == operandType)
        {
            return value;
        }

        if (ir == "ptr")
        {
            value = fn.Value($"ptrtoint ptr {value} to i64");
            return operandType == "i64" ? value : fn.Value($"bitcast i64 {value} to {operandType}");
        }

        return operandType == "ptr" ? fn.Value($"inttoptr {ir} {value} to ptr") : fn.Value($"bitcast {ir} {value} to {operandType}");
    }

    /// <summary>A register's value as the Kurz type of the place it is written to, its bits unchanged.</summary>
    private string FromAsmOperand(string value, string operandType, KurzType type)
    {
        var ir = Ir(type);
        if (ir == operandType)
        {
            return value;
        }

        if (ir == "ptr")
        {
            var bits = operandType == "i64" ? value : fn.Value($"bitcast {operandType} {value} to i64");
            return fn.Value($"inttoptr i64 {bits} to ptr");
        }

        return operandType == "ptr" ? fn.Value($"ptrtoint ptr {value} to {ir}") : fn.Value($"bitcast {operandType} {value} to {ir}");
    }

    /// <summary>
    /// R11: a placeholder of a register operand in LLVM's template, with Rust's modifier read as LLVM's: on x86-64 a general register without a
    /// modifier is named as its 64 bits, <c>l</c> its low byte, <c>h</c> its high byte, <c>x</c> its 16 bits, <c>e</c> its 32 bits and <c>r</c>
    /// its 64 bits; an <c>xmm</c> register as its 128 bits, <c>y</c> and <c>z</c> as its 256 and 512 bits.
    /// </summary>
    private static string Placeholder(int index, string registerClass, string? modifier)
    {
        var llvm = registerClass switch
        {
            "reg" or "reg_abcd" => modifier switch
            {
                null or "r" => "q",
                "l" => "b",
                "h" => "h",
                "x" => "w",
                "e" => "k",
                _ => throw new InvalidOperationException($"the modifier `{modifier}` of a general register, which the checker refuses"),
            },
            "xmm_reg" => modifier switch
            {
                null or "x" => "x",
                "y" => "t",
                "z" => "g",
                _ => throw new InvalidOperationException($"the modifier `{modifier}` of an xmm register, which the checker refuses"),
            },
            _ => null,
        };
        var number = index.ToString(CultureInfo.InvariantCulture);
        return llvm is null ? $"${{{number}}}" : $"${{{number}:{llvm}}}";
    }

    /// <summary>R11: the general registers the System V ABI does not keep across a call, as Rust lists them for x86-64 (<see cref="AbiClobbers"/>).</summary>
    private static readonly string[] SysVGeneralClobbers = ["ax", "cx", "dx", "si", "di", "r8", "r9", "r10", "r11"];

    /// <summary>R11: the general registers the Windows x64 ABI does not keep across a call, as Rust lists them for x86-64 (<see cref="AbiClobbers"/>).</summary>
    private static readonly string[] Win64GeneralClobbers = ["ax", "cx", "dx", "r8", "r9", "r10", "r11"];

    /// <summary>
    /// R11: the registers an ABI of <c>clobber_abi</c> does not keep across a call, as Rust 1.91 lists them for x86-64
    /// (<c>rustc_target/src/asm/mod.rs</c>, <c>X86_64SysV</c> and <c>X86_64Win</c>): the general registers of the convention, <c>xmm0</c> to
    /// <c>xmm15</c> on Windows x64 too, whose upper halves in <c>ymm6</c> to <c>ymm15</c> no callee keeps, <c>xmm16</c> to <c>xmm31</c> and
    /// <c>k0</c> to <c>k7</c> of AVX-512, the x87 and MMX registers, and <c>tmm0</c> to <c>tmm7</c> of AMX.
    /// </summary>
    private IEnumerable<string> AbiClobbers(string abi)
    {
        var win64 = abi is "win64" or "efiapi" || (abi is "C" or "system" && target == Target.Windows);
        var general = win64 ? Win64GeneralClobbers : SysVGeneralClobbers;
        static IEnumerable<string> Numbered(string prefix, int count) => Enumerable.Range(0, count).Select(i => prefix + i.ToString(CultureInfo.InvariantCulture));
        var x87 = Enumerable.Range(0, 8).Select(i => i == 0 ? "st" : $"st({i.ToString(CultureInfo.InvariantCulture)})");
        return general.Concat(Numbered("xmm", 32)).Concat(Numbered("k", 8)).Concat(x87).Concat(Numbered("mm", 8)).Concat(Numbered("tmm", 8));
    }

    /// <summary>
    /// R11: a register of <c>clobber_abi</c> that no operand of Kurz can name, written as a clobber: the x87, MMX and AMX tile registers,
    /// which Rust takes only as a clobber, and <c>xmm16</c> to <c>xmm31</c> and <c>k0</c> to <c>k7</c>, whose classes the checker reads for no
    /// operand. Rust writes these two as a clobber where the CPU lacks AVX-512 and as an output let go of where it has it, which tells LLVM
    /// the same where no operand can stand in the register (<c>rustc_codegen_llvm/src/asm.rs</c>).
    /// </summary>
    private static bool OnlyClobbered(string register) =>
        register.StartsWith("st", StringComparison.Ordinal) || register.StartsWith("mm", StringComparison.Ordinal) || register.StartsWith("tmm", StringComparison.Ordinal)
        || register.StartsWith('k') || (register.StartsWith("xmm", StringComparison.Ordinal) && int.Parse(register.AsSpan(3), CultureInfo.InvariantCulture) >= 16);

    /// <summary>R11: the value of a <c>const</c> operand, written into the template as its digits.</summary>
    private string ConstText(AsmOperand o) =>
        Model.TypeOf(o.Value!).Constant is IntegerConstant c
            ? c.Value.ToString(CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"a `const` operand without its constant at line {o.Line}");

    /// <summary>R11: the address a <c>sym</c> operand names, as an IR value: a function of the program, a C function, or state outside a function (R13).</summary>
    private string SymValue(AsmOperand o)
    {
        var n = (NameExpression)Unwrap(o.Value!);
        if (Model.IsVariable(n))
        {
            return Place(Model.VariableOf(n), n.Line);
        }

        var f = Model.FunctionNameOf(n);
        if (f.CDeclaration is { } declaration)
        {
            var parameters = f.Parameters.Select(p => Lower.CType(p.Type)).ToList();
            module.Declare($"declare {Lower.CType(f.ReturnType)} @{declaration.Name}({string.Join(", ", declaration.Variadic ? parameters.Append("...") : parameters)})");
            if (cLibraries.TryGetValue(f, out var library) && library is not null)
            {
                libraries.Add(library);
            }

            return $"@{declaration.Name}";
        }

        return Instance(f, NoMap, NoFeatures);
    }

    /// <summary>
    /// R11: the template of an <c>asm</c> as one text, its strings joined by line ends as Rust joins them: <c>{{</c> and <c>}}</c> write a brace,
    /// <c>{}</c> takes the operands without a name in their order, <c>{0}</c> one by its position, <c>{name}</c> one by its name, each written by
    /// <paramref name="placeholder"/> with the modifier after its colon; under <c>options(raw)</c> the text stays as it is. Where the text goes
    /// to LLVM's inline assembly, a <c>$</c> is written <c>$$</c> (<paramref name="inline"/>).
    /// </summary>
    private static string AsmTemplate(AsmStatement a, bool raw, Func<AsmOperand, string?, string> placeholder, bool inline = true)
    {
        var slots = a.Operands.Where(o => o.Word is not ("options" or "clobber_abi") && !(o.Arguments is [{ Quoted: true }])).ToList();
        var positional = slots.Where(o => o.Name is null).ToList();
        var named = slots.Where(o => o.Name is not null).ToDictionary(o => o.Name!, StringComparer.Ordinal);
        var sb = new StringBuilder();
        var implicitIndex = 0;
        foreach (var t in a.Templates)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            var text = t.Text;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '$' && inline)
                {
                    sb.Append("$$");
                }
                else if (raw || (ch != '{' && ch != '}'))
                {
                    sb.Append(ch);
                }
                else if (i + 1 < text.Length && text[i + 1] == ch)
                {
                    sb.Append(ch); // `{{` and `}}`
                    i++;
                }
                else
                {
                    var end = text.IndexOf('}', i + 1);
                    var inside = text[(i + 1)..end];
                    var colon = inside.IndexOf(':', StringComparison.Ordinal);
                    var reference = colon < 0 ? inside : inside[..colon];
                    var modifier = colon < 0 ? null : inside[(colon + 1)..];
                    var operand = reference.Length == 0 ? positional[implicitIndex++]
                        : char.IsAsciiDigit(reference[0]) ? positional[int.Parse(reference, NumberStyles.None, CultureInfo.InvariantCulture)]
                        : named[reference];
                    sb.Append(placeholder(operand, modifier));
                    i = end;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// R12: a <c>naked</c> function, as Rust's compiler emits one: its <c>asm</c> as assembly at the module's level, under the function's
    /// symbol, in Intel's dialect, a <c>const</c> written as its digits and a <c>sym</c> as the symbol it names, which
    /// <c>llvm.compiler.used</c> keeps; the code calls it through a declaration in the C calling convention of the target, so that it gets
    /// no prologue, no epilogue and no line table. The symbol is the function's own in its object, an ELF function with its size on Linux
    /// and a static COFF function on Windows.
    /// </summary>
    private void EmitNaked(FunctionSymbol f, FunctionDeclaration d, string name)
    {
        if (!cpu.X86)
        {
            throw Unsupported(d.Line, "a `naked` function, whose assembly the compiler builds for x86-64 alone (R12)");
        }

        var body = d.BlockBody?.Statements is [AsmStatement asm] ? asm : throw new InvalidOperationException($"a `naked` function whose body is no one `asm` at line {d.Line}");
        var symbol = Symbol(name);
        var text = AsmTemplate(body, raw: false, inline: false, placeholder: (o, _) =>
        {
            if (o.Word == "const")
            {
                return ConstText(o);
            }

            var value = SymValue(o);
            asmUsed.Add(value);
            return "\"" + Symbol(value) + "\"";
        });
        var lines = new List<string>();
        if (target == Target.Windows)
        {
            lines.AddRange([".pushsection .text,\"xr\"", $".def \"{symbol}\"", ".scl 3", ".type 32", ".endef", ".p2align 4", $"\"{symbol}\":", ".intel_syntax noprefix"]);
            lines.AddRange(text.Split('\n'));
            lines.AddRange([".att_syntax prefix", ".popsection"]);
        }
        else
        {
            lines.AddRange([".pushsection .text", ".p2align 4", $".type \"{symbol}\",@function", $"\"{symbol}\":", ".intel_syntax noprefix"]);
            lines.AddRange(text.Split('\n'));
            lines.AddRange([".att_syntax prefix", $".size \"{symbol}\", .-\"{symbol}\"", ".popsection"]);
        }

        foreach (var line in lines)
        {
            module.Global($"module asm {IrModule.Quote(line)}");
        }

        if (!unit.Runtime)
        {
            asmsAlone.Add((new AsmAlone(body.Line, "R12", [], string.Concat(lines.Select(l => $"module asm {IrModule.Quote(l)}\n"))), null)); // the body without the program (EmittedProgram.Asms)
        }

        var parameters = f.Parameters.Select(p => ParameterType(f, p.Type));
        module.Declare($"declare dso_local {ParameterType(f, f.ReturnType)} {name}({string.Join(", ", parameters)})");
    }

    /// <summary>R12: the symbols only a <c>naked</c> body names, kept from LLVM's removal of what no code reaches.</summary>
    private void EmitAsmUsed()
    {
        if (asmUsed.Count > 0)
        {
            module.Global($"@llvm.compiler.used = appending global [{asmUsed.Count.ToString(CultureInfo.InvariantCulture)} x ptr] [{string.Join(", ", asmUsed.Select(s => $"ptr {s}"))}], section \"llvm.metadata\"");
        }
    }
}
