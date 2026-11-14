using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Build;

/// <summary>What the compiler made of a program: its IR and the libraries it links when it accepted it, else the diagnostics that refused it.</summary>
public sealed record Compilation(DiagnosticBag Diagnostics, EmittedProgram? Program)
{
    /// <summary>Whether the program was accepted: the front end and the checker found no error, and nothing in it is <c>not supported</c>.</summary>
    public bool Accepted => Program is not null;
}

/// <summary>
/// A build of one program, from its source to an executable: the parser, the checker and the emitter in turn, the runtime of the
/// target emitted into the same module, then the pinned clang on the IR, which links the program with the C library as the platform
/// links it (design record, section 2). The target is the machine that builds (section 2).
/// </summary>
public static partial class Builder
{
    /// <summary>The name of the IR file in a build's working directory.</summary>
    public const string IrFile = "program.ll";

    /// <summary>
    /// The target of the machine that builds, or null on a system the runtime has no directory for, and on a machine that is not
    /// 64-bit: the emitter lays a pointer out in 8 bytes, and the runtime declares C's <c>size_t</c> as a <c>long</c>.
    /// </summary>
    public static Target? HostTarget => TargetFor(RuntimeInformation.OSArchitecture, OperatingSystem.IsWindows(), OperatingSystem.IsLinux());

    /// <summary>The baseline CPU of the machine that builds, which a build that names no CPU is for (section 2): x86-64's, or 64-bit Arm's.</summary>
    public static Cpu HostCpu => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? Cpu.ArmBaseline : Cpu.Baseline;

    /// <summary>The target of a machine of this architecture and system, as <see cref="HostTarget"/> reads it.</summary>
    internal static Target? TargetFor(Architecture architecture, bool windows, bool linux) =>
        architecture is not (Architecture.X64 or Architecture.Arm64) ? null
        : windows ? Target.Windows
        : linux ? Target.Linux
        : null;

    /// <summary>
    /// The runtime checked for <paramref name="target"/>. A runtime the checker refuses is a defect of the compiler, which the suite's
    /// check of every target holds, so it is thrown and not reported against the program.
    /// </summary>
    public static CheckedRuntime CheckRuntime(Target target)
    {
        var errors = new List<string>();
        var runtime = Runtime.Check(target, errors);
        return errors.Count == 0
            ? runtime
            : throw new InvalidOperationException($"the runtime for {Runtime.Directory(target)} does not check:\n{string.Join('\n', errors)}");
    }

    /// <summary>
    /// Reads, checks and emits the program, with the grants of <paramref name="grants"/> (R7, R16) and the runtime given, and with the
    /// line tables in a release build where <paramref name="lines"/> asks for them (<c>--lines</c>), for the CPU <paramref name="cpu"/>
    /// gives once the checker accepted the program, the machine's baseline where it is null (section 2). A program the front end or the
    /// checker refuses, and one that uses what the checker or the emitter does not read, gets no IR, and the diagnostics say why; where
    /// <paramref name="cpu"/> gives null, it wrote why itself, and the program gets no IR either.
    /// </summary>
    public static Compilation Compile(SourceText source, IReadOnlyCollection<string> grants, CheckedRuntime runtime, bool testBuild, bool lines = false, Func<Cpu?>? cpu = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(source, diagnostics);
        if (diagnostics.HasErrors)
        {
            return new Compilation(diagnostics, null);
        }

        var checker = Checker.Check(tree, diagnostics, grants, runtime.StdChecker); // W1: the file sees the types of std/ the prelude names
        if (diagnostics.HasErrors || diagnostics.HasNotSupported)
        {
            return new Compilation(diagnostics, null);
        }

        return (cpu ?? (() => HostCpu))() is { } named
            ? new Compilation(diagnostics, Emitter.Emit(tree, checker, runtime, source.Name, testBuild, diagnostics, lines, named))
            : new Compilation(diagnostics, null);
    }

    /// <summary>
    /// Writes the IR into the working directory and has clang build the executable at <paramref name="outputPath"/>. Where clang refuses
    /// inline assembly, its text names no line of the program, not even the <c>!srcloc</c> Rust's compiler puts on the call (clang 23 on
    /// IR), so the program's <c>asm</c>s are assembled together without the rest of the program, beside a label for each symbol its IR
    /// defines (<see cref="Definitions"/>), in the order LLVM meets them, the order of the code its code generator writes, in one file as
    /// across files: in a test build as the compiler writes them (<see cref="EmittedProgram.Asms"/>), in a release build an <c>asm</c>
    /// statement's copy once for each call the code generator holds once it placed the blocks, in its order (<see cref="Held"/>); each with
    /// those before it that clang took, and each one whose addition clang refuses is the compile error <c>asm-refused</c> at its line, a
    /// line named once however many of its copies clang refuses, an <see cref="AsmRefusedException"/> that names them in that order (R11, R12; the
    /// owner, 2026-11-13, "Rust's srcloc", for the error at the line of the <c>asm</c>, pull request 530, comment 6855725580, "LLVM's
    /// order everywhere", for the order, and pull request 532, comment 6860019854, "Copies from optimised IR", for a release build): one
    /// clang refuses alone, a <c>naked</c> function's whose label is a symbol of the program among them, of two <c>naked</c> functions that
    /// define one label the second LLVM meets, whose label clang refuses beside the first's, where Rust's compiler, which gives a naked
    /// function's assembly no <c>!srcloc</c>, names neither, and in a release build every copy after the first of a call the optimiser or
    /// the code generator copied, whose text clang takes once and refuses again. Where none is refused so, the failure stays clang's.
    /// </summary>
    public static void Build(EmittedProgram program, Clang clang, string workingDirectory, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(clang);
        Directory.CreateDirectory(workingDirectory);
        var irPath = Path.Combine(workingDirectory, IrFile);
        File.WriteAllText(irPath, program.Ir);
        try
        {
            clang.Build(irPath, outputPath, program.Libraries, program.Release, program.Lines, program.CpuFlag);
        }
        catch (ToolchainException e) when (program.Asms.Count > 0 && (e.Message.Contains(InlineAsm, StringComparison.Ordinal) || e.Message.Contains(InlineAsmFailed, StringComparison.Ordinal)))
        {
            var asms = program.Release ? Held(program, clang, irPath, workingDirectory) : program.Asms;
            if (asms is null)
            {
                throw;
            }

            var defined = Definitions(program.Ir);
            var taken = new List<AsmAlone>();
            var refused = new List<Diagnostic>();
            foreach (var (asm, i) in asms.Select((asm, i) => (asm, i))) // in the order LLVM meets them (EmittedProgram.Asms, Held)
            {
                var path = Path.Combine(workingDirectory, $"asm{i.ToString(CultureInfo.InvariantCulture)}.ll");
                File.WriteAllText(path, Together(defined, [.. taken, asm]));
                if (clang.Assemble(path, Path.ChangeExtension(path, ".o"), program.CpuFlag) is { } refusal && refusal.Contains(InlineAsm, StringComparison.Ordinal))
                {
                    var said = AssemblerError().Match(refusal) is { Success: true } m ? m.Groups[1].Value : "the instruction is not one it reads";
                    refused.Add(Diagnostic.Error(ErrorIds.AsmRefused, asm.Line, $"the assembler refuses an instruction of the `asm`: {said} ({asm.Rule})"));
                }
                else
                {
                    taken.Add(asm);
                }
            }

            if (refused.Count == 0)
            {
                throw;
            }

            throw new AsmRefusedException([.. refused.DistinctBy(d => d.Line)]); // an `asm` of a function emitted or copied twice, once (E1)
        }
    }

    /// <summary>
    /// R11 (the owner, 2026-11-13, pull request 532, comment 6860019854, "Copies from optimised IR"): the copies a release build assembles,
    /// whose optimiser drops a call of no effect whose value nothing reads and copies a call where it inlines a function into two callers,
    /// and whose code generator copies a call where it duplicates the block that holds it into the blocks before it: the <c>naked</c>
    /// functions' module-level assembly, which neither changes, then a copy for each call of an <c>asm</c> statement the code generator holds
    /// once it placed the blocks (<see cref="Clang.Generate"/>), in the order it holds them, the order LLVM meets them, each the
    /// <see cref="AsmAlone"/> of the line the call's mark names, the <c>!srcloc</c> the emitter puts on it, which every copy LLVM makes keeps,
    /// whose template the call has, since two <c>asm</c>s can stand at one line; the machine code keeps no constraints, so of two at one line
    /// with one template, which name one line, the first. A call without a mark is the runtime's. Null where clang does not write the code.
    /// </summary>
    private static List<AsmAlone>? Held(EmittedProgram program, Clang clang, string irPath, string workingDirectory)
    {
        var path = Path.Combine(workingDirectory, GeneratedFile);
        if (clang.Generate(irPath, path, program.CpuFlag) is not null)
        {
            return null;
        }

        var code = File.ReadAllText(path);
        var lines = Mark().Matches(code).ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var statements = program.Asms.Where(a => a.Rule == "R11").ToList();
        return
        [
            .. program.Asms.Where(a => a.Rule != "R11"),
            .. MarkedCopy().Matches(code)
                .Select(m => (Template: m.Groups[1].Value.StartsWith('"') ? m.Groups[1].Value : $"\"{m.Groups[1].Value}\"", Mark: m.Groups[2].Value))
                .Select(c => lines.TryGetValue(c.Mark, out var line)
                    ? statements.FirstOrDefault(a => a.Line == line && a.Ir.Contains($"{c.Template}, \"", StringComparison.Ordinal))
                    : null)
                .OfType<AsmAlone>(),
        ];
    }

    /// <summary>The name of the code generator's machine IR in a build's working directory (<see cref="Held"/>).</summary>
    private const string GeneratedFile = "generated.mir";

    /// <summary>
    /// R11, R12: one module of IR that holds the <c>asm</c>s given after the labels of <paramref name="defined"/>, the symbols the
    /// program's IR defines, each symbol their <c>sym</c>s name declared once: a <c>naked</c> function's module-level assembly, and the
    /// calls of the <c>asm</c> statements, in the order given, in the function <see cref="Main"/>, in place of its label, so that the copies
    /// define no symbol the program does not, and none twice.
    /// </summary>
    private static string Together(IReadOnlyList<(string Name, string Label)> defined, IReadOnlyList<AsmAlone> asms)
    {
        var statements = asms.Where(a => a.Rule == "R11").ToList();
        return string.Concat(asms.SelectMany(a => a.Declarations).Distinct(StringComparer.Ordinal).Select(d => d + "\n"))
            + string.Concat(defined.Where(d => statements.Count == 0 || d.Name != Main).Select(d => d.Label))
            + string.Concat(asms.Where(a => a.Rule != "R11").Select(a => a.Ir))
            + (statements.Count == 0 ? "" : $"define void @{Main}() {{\n{string.Concat(statements.Select(a => a.Ir))}  ret void\n}}\n");
    }

    /// <summary>R11: the function the C library's start calls, which every program's IR defines and no <c>sym</c> names.</summary>
    private const string Main = "main";

    /// <summary>
    /// R12: each symbol the program's IR defines, a function or a global, with module-level assembly that defines a label for it under the
    /// name LLVM gives it in the assembly, a private one's after <c>.L</c> on ELF and on COFF, so that a <c>naked</c> function's label that
    /// is one of them is refused in that function's text. LLVM writes a <c>naked</c> function's module-level assembly before the program's own
    /// definitions and refuses such a label where it writes the definition, with no line of the template (clang 23); a <c>naked</c>
    /// function's own symbol, which the IR declares, is none of them, nor is a C function the program declares (R16), a <c>declare</c>
    /// line, nor a global the IR declares, <c>external</c> or <c>extern_weak</c>, beside each of which clang takes the label (clang 23).
    /// </summary>
    private static List<(string Name, string Label)> Definitions(string ir) =>
    [
        .. DefinedSymbol().Matches(ir)
            .Select(m => (Name: (m.Groups[2].Success ? m.Groups[2] : m.Groups[3]).Value.Trim('"'), Words: (m.Groups[1].Success ? m.Groups[1] : m.Groups[4]).Value))
            .Where(s => !s.Name.StartsWith("llvm.", StringComparison.Ordinal) && !s.Words.Split(' ').Any(w => w is "external" or "extern_weak"))
            .Select(s => (s.Name, "module asm " + IrModule.Quote("\"" + (s.Words.Split(' ').Contains("private") ? ".L" : "") + s.Name + "\":") + "\n")),
    ];

    /// <summary>The file clang names an error of inline assembly in, LLVM's buffer of the <c>asm</c>'s text.</summary>
    private const string InlineAsm = "<inline asm>";

    /// <summary>
    /// The line clang ends a refusal of inline assembly with, which a label of a <c>naked</c> function that is a symbol the program
    /// defines draws with no <see cref="InlineAsm"/> line (clang 23).
    /// </summary>
    private const string InlineAsmFailed = "cannot compile inline asm";

    /// <summary>A definition of the program's IR: a function's words before its name and its name, or a global's name and the words after <c>=</c>, which set a declaration apart (<see cref="Definitions"/>).</summary>
    [GeneratedRegex("""^(?:define ([^@\r\n]*)@("[^"\r\n]*"|[-a-zA-Z$._0-9]+)|@("[^"\r\n]*"|[-a-zA-Z$._0-9]+) = ([a-z_ ]*))""", RegexOptions.Multiline)]
    private static partial Regex DefinedSymbol();

    [GeneratedRegex(@"<inline asm>:\d+:\d+: error: ([^\r\n]*)")]
    private static partial Regex AssemblerError();

    /// <summary>A node of the module the machine IR holds that is a call's mark (<see cref="Held"/>): its number and the line it holds.</summary>
    [GeneratedRegex(@"^ *!(\d+) = !\{i64 (\d+)\}\r?$", RegexOptions.Multiline)]
    private static partial Regex Mark();

    /// <summary>
    /// A call of inline assembly in the machine IR with a mark: its template, quoted as the IR quotes it or bare where it is a name LLVM
    /// writes without quotes, <c>&amp;nop</c>, and the number of its mark, the one metadata operand of the call.
    /// </summary>
    [GeneratedRegex("""^ *INLINEASM &("[^"\r\n]*"|[-a-zA-Z0-9._]+), [^\r\n]*?, !(\d+)\b""", RegexOptions.Multiline)]
    private static partial Regex MarkedCopy();

    /// <summary>The name of the executable a build of this source writes beside it: the source's name without its extension, with <c>.exe</c> on Windows.</summary>
    public static string ExecutableBeside(string sourcePath)
    {
        var stem = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, Path.GetFileNameWithoutExtension(sourcePath));
        return OperatingSystem.IsWindows() ? stem + ".exe" : stem;
    }
}

/// <summary>R11, R12: the <c>asm</c>s of a program the assembler refuses, each the compile error <c>asm-refused</c> at its line, in the order LLVM meets them (<see cref="Builder.Build"/>).</summary>
public sealed class AsmRefusedException(IReadOnlyList<Diagnostic> refused) : Exception(string.Join("\n", refused))
{
    public IReadOnlyList<Diagnostic> Refused { get; } = refused;
}
