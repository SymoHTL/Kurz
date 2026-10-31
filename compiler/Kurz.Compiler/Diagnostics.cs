namespace Kurz.Compiler;

/// <summary>
/// What the compiler says about a program. An <see cref="DiagnosticKind.Error"/> is a compile error
/// with an id of the reference's table (chapter 12) or L1's <c>invalid-source</c>, which the rule
/// names and the table keeps out: the id and the line are what a corpus case pins (E1, E2), and the
/// message is free. <see cref="DiagnosticKind.NotSupported"/> names a construct the reference
/// states and this compiler does not implement: it carries no id, so it can never pass for an error
/// a case expects. The checker gives it to a construct it does not read, and the emitter to one it does not lay out, a build for
/// Windows to the calls on files and sockets and the reads of the command line, the environment and the clock among them (S11); a
/// corpus case that gets one fails its theory unless the theory names the case or its rules lie outside the checker. A <see cref="DiagnosticKind.Warning"/>
/// is a lint with its id, <c>md5-insecure</c> (H5), which builds the program (design record, section 12, "Warning, build goes on").
/// </summary>
public enum DiagnosticKind
{
    Error,
    NotSupported,
    Warning,
}

public sealed record Diagnostic(DiagnosticKind Kind, string Id, int Line, string Message)
{
    public static Diagnostic Error(string id, int line, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return new Diagnostic(DiagnosticKind.Error, id, line, message);
    }

    public static Diagnostic NotSupported(int line, string construct) =>
        new(DiagnosticKind.NotSupported, "", line, construct);

    public static Diagnostic Warning(string id, int line, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return new Diagnostic(DiagnosticKind.Warning, id, line, message);
    }

    public bool IsError => Kind == DiagnosticKind.Error;

    public bool IsNotSupported => Kind == DiagnosticKind.NotSupported;

    /// <summary>
    /// What to do about the error, the line below it, or null: <c>nullable-unchecked</c> says how to check (section 4, the owner,
    /// 2026-10-30, "Text + help line"), indented by two spaces as the command's own failures indent their <c>help:</c> (section 2, an
    /// assumption in section 4).
    /// </summary>
    public string? Help => IsError && Id == ErrorIds.NullableUnchecked ? "check it with `!= null`, or give a value for null with `??`" : null;

    /// <summary>
    /// The diagnostic as the command writes it, the file's name and the line in front: <c>hello.kz:3: &lt;id&gt;: &lt;text&gt;</c> (design
    /// record, section 2), <c>hello.kz:3: warning: &lt;id&gt;: &lt;text&gt;</c> (H5), or <c>hello.kz:3: not supported: &lt;text&gt;</c>, with
    /// its <see cref="Help"/> on the line below.
    /// </summary>
    public string At(string file) => Kind switch
    {
        DiagnosticKind.Error => $"{file}:{Line}: {Id}: {Message}",
        DiagnosticKind.Warning => $"{file}:{Line}: warning: {Id}: {Message}",
        _ => $"{file}:{Line}: not supported: {Message}",
    } + (Help is { } help ? $"\n  help: {help}" : string.Empty);

    /// <summary>The diagnostic as one line: <c>4: error syntax: ...</c>, <c>4: warning md5-insecure: ...</c>, or <c>4: not supported: ...</c>.</summary>
    public override string ToString() => Kind switch
    {
        DiagnosticKind.Error => $"{Line}: error {Id}: {Message}",
        DiagnosticKind.Warning => $"{Line}: warning {Id}: {Message}",
        _ => $"{Line}: not supported: {Message}",
    };
}

/// <summary>The diagnostics of one compilation, in the order they were found; <see cref="Sorted"/> orders them by line.</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> items = [];

    public IReadOnlyList<Diagnostic> Items => items;

    public bool HasErrors => items.Any(d => d.IsError);

    public bool HasNotSupported => items.Any(d => d.IsNotSupported);

    public void Add(Diagnostic diagnostic) => items.Add(diagnostic);

    public void Error(string id, int line, string message) => items.Add(Diagnostic.Error(id, line, message));

    public void NotSupported(int line, string construct) => items.Add(Diagnostic.NotSupported(line, construct));

    public IReadOnlyList<Diagnostic> Sorted => items.OrderBy(d => d.Line).ThenBy(d => d.Kind).ToList();
}

/// <summary>
/// The ids of the error table in reference/12-errors.md, spelled as the table spells them, and the one
/// id a rule names and keeps out of the table, L1's <c>invalid-source</c>: the table lists the ids a
/// corpus case can expect, and no case can be a file that is not UTF-8. A compile error the compiler
/// reports carries one of these, and a run-time error of a produced program one of the run-time ids.
/// </summary>
public static class ErrorIds
{
    // compile errors
    public const string UnusedVariable = "unused-variable";
    public const string AssignImmutable = "assign-immutable";
    public const string Redeclared = "redeclared";
    public const string ArgumentMismatch = "argument-mismatch";
    public const string EnumNumber = "enum-number";
    public const string FlagsNumber = "flags-number";
    public const string UnknownName = "unknown-name";
    public const string TypeMismatch = "type-mismatch";
    public const string MissingReturn = "missing-return";
    public const string NotVisible = "not-visible";
    public const string DuplicateFunction = "duplicate-function";
    public const string ConstantOverflow = "constant-overflow";
    public const string ConstantDivideByZero = "constant-divide-by-zero";
    public const string NarrowingConversion = "narrowing-conversion";
    public const string SignMix = "sign-mix";
    public const string StringIndex = "string-index";
    public const string NullableUnchecked = "nullable-unchecked";
    public const string MutRequired = "mut-required";
    public const string MutAtCall = "mut-at-call";
    public const string ChangedWhileHeld = "changed-while-held";
    public const string UnlistedCase = "unlisted-case";
    public const string MatchNotExhaustive = "match-not-exhaustive";
    public const string BracesRequired = "braces-required";
    public const string ReferenceCycle = "reference-cycle";
    public const string WeakNotNullable = "weak-not-nullable";
    public const string WeakValue = "weak-value";
    public const string RawNotAllowed = "raw-not-allowed";
    public const string RawRequired = "raw-required";
    public const string CountedPointee = "counted-pointee";
    public const string NakedNotAllowed = "naked-not-allowed";
    public const string NakedBody = "naked-body";
    public const string NativeNotAllowed = "native-not-allowed";
    public const string NativeType = "native-type";
    public const string AsmOperand = "asm-operand";
    public const string Semicolon = "semicolon";
    public const string ReservedWord = "reserved-word";
    public const string CaptureAssign = "capture-assign";
    public const string ReversedRange = "reversed-range";
    public const string AmbiguousCall = "ambiguous-call";
    public const string CannotInfer = "cannot-infer";
    public const string EqualByUnknown = "equal-by-unknown";
    public const string BaseFieldClash = "base-field-clash";
    public const string NoText = "no-text";
    public const string BlockIndentation = "block-indentation";
    public const string ThrowNeedsValue = "throw-needs-value";
    public const string BreakOutsideLoop = "break-outside-loop";
    public const string UnknownEscape = "unknown-escape";
    public const string StaticState = "static-state";
    public const string OverrideWithoutVirtual = "override-without-virtual";
    public const string HidesMember = "hides-member";
    public const string NoPrimaryConstructor = "no-primary-constructor";
    public const string ConstructorMustChain = "constructor-must-chain";
    public const string FieldUnassigned = "field-unassigned";
    public const string Syntax = "syntax";
    public const string InexactLiteral = "inexact-literal";
    public const string MissingMember = "missing-member";
    public const string InterfaceMethodPrivate = "interface-method-private";
    public const string UnreachableArm = "unreachable-arm";
    public const string OverrideReturnType = "override-return-type";
    public const string DefaultNotConstant = "default-not-constant";
    public const string BaseCycle = "base-cycle";

    /// <summary>L1 names this id in its rule and keeps it out of the table, which lists the ids cases expect: no corpus file can be a file that is not UTF-8.</summary>
    public const string InvalidSource = "invalid-source";

    // run-time errors
    public const string Thrown = "thrown";
    public const string Overflow = "overflow";
    public const string DivideByZero = "divide-by-zero";
    public const string IndexOutOfRange = "index-out-of-range";
    public const string ReversedRangeAtRunTime = "reversed-range-at-run-time";
    public const string StackOverflow = "stack-overflow";
}

/// <summary>The ids of the lints, which warn and build the program (H5): no case expects one, and the error table keeps them out.</summary>
public static class LintIds
{
    public const string Md5Insecure = "md5-insecure";
}
