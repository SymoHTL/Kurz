namespace Kurz.Compiler;

/// <summary>
/// What the compiler says about a program. An <see cref="DiagnosticKind.Error"/> is a compile error
/// of the reference's table (chapter 12): its id and its line are what a corpus case pins (E1, E2),
/// and its message is free. <see cref="DiagnosticKind.NotSupported"/> names a construct the reference
/// states and this compiler does not implement: it carries no id, so it can never pass for an error
/// a case expects, and a program that gets one is not judged.
/// </summary>
public enum DiagnosticKind
{
    Error,
    NotSupported,
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

    public bool IsError => Kind == DiagnosticKind.Error;

    /// <summary>The line as the command line prints it, after the file name: <c>4: error syntax: ...</c>.</summary>
    public override string ToString() =>
        IsError ? $"{Line}: error {Id}: {Message}" : $"{Line}: not supported: {Message}";
}

/// <summary>The diagnostics of one compilation, in the order they were found; <see cref="Sorted"/> orders them by line.</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> items = [];

    public IReadOnlyList<Diagnostic> Items => items;

    public bool HasErrors => items.Any(d => d.IsError);

    public bool HasNotSupported => items.Any(d => !d.IsError);

    public void Add(Diagnostic diagnostic) => items.Add(diagnostic);

    public void Error(string id, int line, string message) => items.Add(Diagnostic.Error(id, line, message));

    public void NotSupported(int line, string construct) => items.Add(Diagnostic.NotSupported(line, construct));

    public IReadOnlyList<Diagnostic> Sorted => items.OrderBy(d => d.Line).ThenBy(d => d.Kind).ToList();
}

/// <summary>
/// The ids of the error table in reference/12-errors.md, spelled as the table spells them. A compile
/// error the compiler reports carries one of these, and a run-time error of a produced program one
/// of the run-time ids.
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
    public const string UnlistedCase = "unlisted-case";
    public const string MatchNotExhaustive = "match-not-exhaustive";
    public const string BracesRequired = "braces-required";
    public const string ReferenceCycle = "reference-cycle";
    public const string WeakNotNullable = "weak-not-nullable";
    public const string WeakValue = "weak-value";
    public const string RawNotAllowed = "raw-not-allowed";
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

    /// <summary>L1: named in the rule and not in the table, because no corpus file can be a file that is not UTF-8.</summary>
    public const string InvalidSource = "invalid-source";

    // run-time errors
    public const string Thrown = "thrown";
    public const string Overflow = "overflow";
    public const string DivideByZero = "divide-by-zero";
    public const string IndexOutOfRange = "index-out-of-range";
    public const string ReversedRangeAtRunTime = "reversed-range-at-run-time";
    public const string StackOverflow = "stack-overflow";
}
