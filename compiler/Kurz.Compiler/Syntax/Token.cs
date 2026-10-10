namespace Kurz.Compiler.Syntax;

public enum TokenKind
{
    EndOfFile,

    /// <summary>A line break. Whether it ends a statement is L2 and L4, decided by the parser.</summary>
    Newline,

    /// <summary>A name (L9).</summary>
    Name,

    /// <summary>A core word (L18) or the name of a built-in type (L19); <see cref="Token.Text"/> holds it.</summary>
    Keyword,

    /// <summary>An integer literal (L11); <see cref="Token.Value"/> is an <see cref="IntegerLiteralValue"/>.</summary>
    IntegerLiteral,

    /// <summary>A literal with a fraction, an exponent or a floating-point or decimal suffix; <see cref="Token.Value"/> is a <see cref="RealLiteralValue"/>.</summary>
    RealLiteral,

    /// <summary>A number with a unit (L14); <see cref="Token.Value"/> is a <see cref="UnitLiteralValue"/>.</summary>
    UnitLiteral,

    /// <summary>A string literal, one line or a <c>"""</c> block; <see cref="Token.Value"/> is a <see cref="StringLiteralValue"/>.</summary>
    StringLiteral,

    /// <summary>A <c>char</c> literal (T21); <see cref="Token.Value"/> is the code point as an <see cref="int"/>.</summary>
    CharLiteral,

    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,
    Comma,
    Dot,
    DotDot,
    DotDotLess,
    Colon,
    Question,
    QuestionDot,
    QuestionQuestion,
    Arrow,
    Assign,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    PlusPercent,
    MinusPercent,
    StarPercent,
    EqualEqual,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    AmpersandAmpersand,
    BarBar,
    Bang,
    Ampersand,
    Bar,
    Caret,
    Tilde,
    LessLess,
    GreaterGreater,

    /// <summary>A <c>;</c>, which L5 rules out; the parser reports it.</summary>
    Semicolon,

    /// <summary>Text no rule gives a meaning (E4). The lexer has reported <c>syntax</c> for it.</summary>
    Bad,
}

/// <summary>
/// One token. <see cref="Line"/> is the 1-based line of its first character, which is the line a
/// diagnostic about it names (E1). <see cref="Offset"/> is the index of that character in the
/// source text.
/// </summary>
public sealed record Token(TokenKind Kind, string Text, int Line, int Offset, object? Value = null)
{
    public bool Is(TokenKind kind) => Kind == kind;

    public bool IsKeyword(string word) => Kind == TokenKind.Keyword && Text == word;

    public override string ToString() => Kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.Newline => "line break",
        _ => $"`{Text}`",
    };
}

/// <summary>The digits of an integer literal without <c>_</c>, its radix (10, 16 or 2) and its suffix in upper case (<c>""</c>, <c>"U"</c>, <c>"L"</c> or <c>"UL"</c>).</summary>
public sealed record IntegerLiteralValue(string Digits, int Radix, string Suffix);

/// <summary>The text of a real literal without <c>_</c> and without its suffix, and the suffix in lower case (<c>""</c>, <c>"f"</c>, <c>"d"</c> or <c>"m"</c>).</summary>
public sealed record RealLiteralValue(string Text, string Suffix);

/// <summary>A decimal number, an integer or a fraction, without <c>_</c>, and one of L14's units.</summary>
public sealed record UnitLiteralValue(string Number, string Unit);

/// <summary>A piece of a string literal: text as written, after escapes, or a hole with an expression.</summary>
public abstract record StringPart;

public sealed record TextPart(string Text) : StringPart;

/// <summary>The source of an interpolated expression (L6), with the line and the offset of its first character.</summary>
public sealed record HolePart(string Source, int Line, int Offset) : StringPart;

public sealed record StringLiteralValue(IReadOnlyList<StringPart> Parts)
{
    /// <summary>The text of a literal without holes, or null when it has one.</summary>
    public string? PlainText => Parts.All(p => p is TextPart) ? string.Concat(Parts.Cast<TextPart>().Select(p => p.Text)) : null;
}
