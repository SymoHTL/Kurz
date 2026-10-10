using System.Globalization;
using System.Text;
using Kurz.Compiler.Text;

namespace Kurz.Compiler.Syntax;

/// <summary>
/// Turns source text into tokens, following chapter 1 of the reference: names (L9), number literals
/// (L10, L11) and literals with a unit (L14), strings with their escapes and holes (L6, L7, L13,
/// L15, L16), <c>char</c> literals (T21), comments (L3) and the <c>;</c> that is an error (L5). A
/// line break is a token of its own; whether it ends a statement is the parser's (L2, L4).
/// </summary>
public sealed class Lexer
{
    private readonly SourceText source;
    private readonly string text;
    private readonly int end;
    private readonly DiagnosticBag diagnostics;
    private readonly List<Token> tokens = [];
    private int position;

    public Lexer(SourceText source, DiagnosticBag diagnostics)
        : this(source, diagnostics, 0, source.Text.Length)
    {
    }

    /// <summary>A lexer over the part of the text between two offsets: an interpolation hole (L6).</summary>
    public Lexer(SourceText source, DiagnosticBag diagnostics, int start, int end)
    {
        this.source = source;
        text = source.Text;
        this.diagnostics = diagnostics;
        position = start;
        this.end = end;
    }

    private char Current => position < end ? text[position] : '\0';

    private char Peek(int ahead = 1) => position + ahead < end ? text[position + ahead] : '\0';

    private bool AtEnd => position >= end;

    public IReadOnlyList<Token> Lex()
    {
        while (!AtEnd)
        {
            var c = Current;
            if (c == '\n')
            {
                Add(TokenKind.Newline, position, 1);
                position++;
            }
            else if (c is ' ' or '\t' or '\r' or '\f' or '\v')
            {
                position++;
            }
            else if (c == '/' && Peek() == '/')
            {
                while (!AtEnd && Current != '\n')
                {
                    position++;
                }
            }
            else if (IsAsciiDigit(c))
            {
                LexNumber();
            }
            else if (IsNameStart(c))
            {
                LexName();
            }
            else if (c == '"')
            {
                LexString();
            }
            else if (c == '\'')
            {
                LexChar();
            }
            else if (c == ';')
            {
                // L5: there is no `;`. The parser reports it where it stands between two statements, and
                // reads it as part of the construct where it stands inside one (C8's three-part `for`).
                Add(TokenKind.Semicolon, position, 1);
                position++;
            }
            else
            {
                LexPunctuation();
            }
        }

        tokens.Add(new Token(TokenKind.EndOfFile, "", LineAt(end), end));
        return tokens;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsNameStart(char c) => IsAsciiLetter(c) || c == '_';

    private static bool IsNamePart(char c) => IsNameStart(c) || IsAsciiDigit(c);

    /// <summary>A character that would be part of a word in a wider alphabet: a letter or digit outside ASCII, or C#'s `@`.</summary>
    private static bool IsForeignWordPart(char c) => c == '@' || (c > 127 && (char.IsLetterOrDigit(c) || char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation));

    private int LineAt(int offset) => source.LineOf(offset);

    private void Add(TokenKind kind, int start, int length, object? value = null) =>
        tokens.Add(new Token(kind, text.Substring(start, length), LineAt(start), start, value));

    private void Bad(int start, int length, string why) => Bad(start, length, why, LineAt(start));

    /// <summary>Text no rule gives a meaning: one <c>syntax</c> at <paramref name="line"/>, and a token the parser passes over.</summary>
    private void Bad(int start, int length, string why, int line)
    {
        diagnostics.Error(ErrorIds.Syntax, line, why);
        tokens.Add(new Token(TokenKind.Bad, text.Substring(start, Math.Min(length, end - start)), line, start));
    }

    /// <summary>
    /// After an error inside a <c>"""</c> block: passes over the rest of the block, up to and including the
    /// line that starts with <c>"""</c>, so that the block's text is not read as code.
    /// </summary>
    private void SkipToClosingLine()
    {
        while (!AtEnd)
        {
            position++; // the line break
            var lineStart = position;
            while (!AtEnd && Current != '\n')
            {
                position++;
            }

            if (text[lineStart..position].TrimStart(' ', '\t').StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private void LexName()
    {
        var start = position;
        while (!AtEnd && IsNamePart(Current))
        {
            position++;
        }

        if (!AtEnd && IsForeignWordPart(Current))
        {
            // L9: a letter is one of ASCII's. The whole word is one `syntax`, not a name and a stray character.
            while (!AtEnd && (IsNamePart(Current) || IsForeignWordPart(Current)))
            {
                position++;
            }

            Bad(start, position - start, "a name holds ASCII letters, digits and `_` only");
            return;
        }

        var word = text[start..position];
        Add(CoreWords.IsReserved(word) ? TokenKind.Keyword : TokenKind.Name, start, position - start);
    }

    private void LexNumber()
    {
        var start = position;
        var digits = new StringBuilder();
        var radix = 10;
        var isReal = false;
        if (Current == '0' && Peek() is 'x' or 'X' or 'b' or 'B')
        {
            radix = Peek() is 'x' or 'X' ? 16 : 2;
            position += 2;
            if (!ReadDigits(digits, radix, allowLeadingUnderscore: true))
            {
                Bad(start, position - start, "a number needs digits after its prefix");
                return;
            }
        }
        else
        {
            ReadDigits(digits, 10, allowLeadingUnderscore: false);
            if (Current == '.' && IsAsciiDigit(Peek()))
            {
                isReal = true;
                digits.Append('.');
                position++;
                ReadDigits(digits, 10, allowLeadingUnderscore: false);
            }

            if (Current is 'e' or 'E' && (IsAsciiDigit(Peek()) || (Peek() is '+' or '-' && IsAsciiDigit(Peek(2)))))
            {
                isReal = true;
                digits.Append('e');
                position++;
                if (Current is '+' or '-')
                {
                    digits.Append(Current);
                    position++;
                }

                ReadDigits(digits, 10, allowLeadingUnderscore: false);
            }
        }

        if (position > start && text[position - 1] == '_')
        {
            Bad(start, position - start, "a `_` stands between digits, not at the end of a number");
            return;
        }

        // what follows the digits directly: a suffix, a unit, or text no rule gives a meaning
        var tailStart = position;
        while (!AtEnd && (IsNamePart(Current) || IsForeignWordPart(Current)))
        {
            position++;
        }

        var tail = text[tailStart..position];
        if (tail.Length == 0)
        {
            Add(isReal ? TokenKind.RealLiteral : TokenKind.IntegerLiteral, start, position - start,
                isReal ? new RealLiteralValue(digits.ToString(), "") : new IntegerLiteralValue(digits.ToString(), radix, ""));
            return;
        }

        if (CoreWords.Units.ContainsKey(tail))
        {
            if (radix != 10)
            {
                Bad(start, position - start, "a number with a unit is written in decimal digits");
                return;
            }

            if (digits.ToString().Contains('e', StringComparison.Ordinal))
            {
                Bad(start, position - start, "a number with a unit is a decimal integer or a decimal fraction, without an exponent");
                return;
            }

            Add(TokenKind.UnitLiteral, start, position - start, new UnitLiteralValue(digits.ToString(), tail));
            return;
        }

        if (tail.Contains('l', StringComparison.Ordinal))
        {
            Bad(start, position - start, "the suffix `L` is written in upper case: `1l` reads as `11`");
            return;
        }

        var upper = tail.ToUpperInvariant();
        if (!isReal && upper is "U" or "L" or "UL" or "LU")
        {
            Add(TokenKind.IntegerLiteral, start, position - start, new IntegerLiteralValue(digits.ToString(), radix, upper == "LU" ? "UL" : upper));
            return;
        }

        if (radix == 10 && upper is "F" or "D" or "M")
        {
            Add(TokenKind.RealLiteral, start, position - start, new RealLiteralValue(digits.ToString(), upper.ToLowerInvariant()));
            return;
        }

        Bad(start, position - start, $"`{tail}` is neither a suffix nor a unit of a number");
    }

    /// <summary>Reads digits of the radix with `_` between them into the builder. False when there was none.</summary>
    private bool ReadDigits(StringBuilder digits, int radix, bool allowLeadingUnderscore)
    {
        var any = false;
        if (allowLeadingUnderscore)
        {
            while (Current == '_')
            {
                position++;
            }
        }

        while (!AtEnd)
        {
            var c = Current;
            if (c == '_')
            {
                position++;
                continue;
            }

            if (!IsDigitOf(c, radix))
            {
                break;
            }

            digits.Append(c);
            any = true;
            position++;
        }

        return any;
    }

    private static bool IsDigitOf(char c, int radix) => radix switch
    {
        2 => c is '0' or '1',
        16 => IsAsciiDigit(c) || c is (>= 'a' and <= 'f') or (>= 'A' and <= 'F'),
        _ => IsAsciiDigit(c),
    };

    private void LexPunctuation()
    {
        var start = position;
        var c = Current;
        var n = Peek();
        (TokenKind kind, int length) = (c, n) switch
        {
            ('(', _) => (TokenKind.LeftParen, 1),
            (')', _) => (TokenKind.RightParen, 1),
            ('[', _) => (TokenKind.LeftBracket, 1),
            (']', _) => (TokenKind.RightBracket, 1),
            ('{', _) => (TokenKind.LeftBrace, 1),
            ('}', _) => (TokenKind.RightBrace, 1),
            (',', _) => (TokenKind.Comma, 1),
            ('.', '.') when Peek(2) == '<' => (TokenKind.DotDotLess, 3),
            ('.', '.') => (TokenKind.DotDot, 2),
            ('.', _) => (TokenKind.Dot, 1),
            (':', _) => (TokenKind.Colon, 1),
            ('?', '.') => (TokenKind.QuestionDot, 2),
            ('?', '?') => (TokenKind.QuestionQuestion, 2),
            ('?', _) => (TokenKind.Question, 1),
            ('=', '>') => (TokenKind.Arrow, 2),
            ('=', '=') => (TokenKind.EqualEqual, 2),
            ('=', _) => (TokenKind.Assign, 1),
            ('+', '%') => (TokenKind.PlusPercent, 2),
            ('+', _) => (TokenKind.Plus, 1),
            ('-', '%') => (TokenKind.MinusPercent, 2),
            ('-', _) => (TokenKind.Minus, 1),
            ('*', '%') => (TokenKind.StarPercent, 2),
            ('*', _) => (TokenKind.Star, 1),
            ('/', _) => (TokenKind.Slash, 1),
            ('%', _) => (TokenKind.Percent, 1),
            ('!', '=') => (TokenKind.NotEqual, 2),
            ('!', _) => (TokenKind.Bang, 1),
            ('<', '=') => (TokenKind.LessEqual, 2),
            ('<', '<') => (TokenKind.LessLess, 2),
            ('<', _) => (TokenKind.Less, 1),
            ('>', '=') => (TokenKind.GreaterEqual, 2),
            ('>', '>') => (TokenKind.GreaterGreater, 2),
            ('>', _) => (TokenKind.Greater, 1),
            ('&', '&') => (TokenKind.AmpersandAmpersand, 2),
            ('&', _) => (TokenKind.Ampersand, 1),
            ('|', '|') => (TokenKind.BarBar, 2),
            ('|', _) => (TokenKind.Bar, 1),
            ('^', _) => (TokenKind.Caret, 1),
            ('~', _) => (TokenKind.Tilde, 1),
            _ => (TokenKind.Bad, 0),
        };
        if (kind == TokenKind.Bad)
        {
            if (IsForeignWordPart(c))
            {
                while (!AtEnd && (IsNamePart(Current) || IsForeignWordPart(Current)))
                {
                    position++;
                }

                Bad(start, position - start, "a name holds ASCII letters, digits and `_` only");
                return;
            }

            position += char.IsSurrogatePair(c, n) ? 2 : 1;
            Bad(start, position - start, $"`{text[start..position]}` has no meaning here");
            return;
        }

        Add(kind, start, length);
        position += length;
    }

    private void LexChar()
    {
        var start = position;
        position++; // the opening quote
        var points = new List<int>();
        while (!AtEnd && Current != '\'' && Current != '\n')
        {
            var point = ReadCharacter(out var ok);
            if (!ok)
            {
                // unknown-escape is reported; the literal is read on so that one error stands
                point = '?';
            }

            points.Add(point);
        }

        if (AtEnd || Current != '\'')
        {
            Bad(start, position - start, "a `char` literal ends with `'` on its line");
            return;
        }

        position++; // the closing quote
        if (points.Count != 1)
        {
            Bad(start, position - start, points.Count == 0 ? "a `char` literal holds one code point" : "a `char` literal holds one code point, not several");
            return;
        }

        Add(TokenKind.CharLiteral, start, position - start, points[0]);
    }

    /// <summary>
    /// Reads one code point of a string or char literal at the current position: a plain character,
    /// a surrogate pair, or an escape (L7, L16). An escape no rule gives a meaning is
    /// <c>unknown-escape</c> at its line; <paramref name="ok"/> is false then and the position has
    /// moved past the backslash and the character after it.
    /// </summary>
    private int ReadCharacter(out bool ok)
    {
        ok = true;
        var c = Current;
        if (c != '\\')
        {
            if (char.IsHighSurrogate(c) && char.IsLowSurrogate(Peek()))
            {
                var pair = char.ConvertToUtf32(c, Peek());
                position += 2;
                return pair;
            }

            position++;
            return c;
        }

        var escapeStart = position;
        position++; // the backslash
        var e = Current;
        position++;
        switch (e)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case '"': return '"';
            case '\\': return '\\';
            case '{': return '{';
            case '\'': return '\'';
            case '0': return '\0';
            case 'a': return '\a';
            case 'b': return '\b';
            case 'f': return '\f';
            case 'r': return '\r';
            case 'v': return '\v';
            case 'u': return ReadHexEscape(escapeStart, 4, 4, out ok);
            case 'U': return ReadHexEscape(escapeStart, 8, 8, out ok);
            case 'x': return ReadHexEscape(escapeStart, 1, 4, out ok);
            default:
                if (char.IsHighSurrogate(e) && char.IsLowSurrogate(Current))
                {
                    position++; // the escaped character is one code point of two chars: both go, or the second is read as a character of its own
                }

                position = Math.Min(position, end);
                diagnostics.Error(ErrorIds.UnknownEscape, LineAt(escapeStart), $"`\\{(e == '\0' || e == '\n' ? "" : text[(escapeStart + 1)..position])}` is not an escape");
                if (e == '\n')
                {
                    position--; // the line break is not part of the escape
                }

                ok = false;
                return '?';
        }
    }

    private int ReadHexEscape(int escapeStart, int min, int max, out bool ok)
    {
        var start = position;
        while (!AtEnd && position - start < max && IsDigitOf(Current, 16))
        {
            position++;
        }

        var count = position - start;
        if (count < min)
        {
            diagnostics.Error(ErrorIds.UnknownEscape, LineAt(escapeStart), $"`{text[escapeStart..position]}` needs {(min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min} to {max}")} hex digits");
            ok = false;
            return '?';
        }

        // long, not int: eight digits from 80000000 up would overflow int into a negative number that passes both checks
        var value = long.Parse(text.AsSpan(start, count), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        if (value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF)
        {
            // beyond U+10FFFF is what C# refuses too; a surrogate alone is no code point a UTF-8 string can hold (L1)
            diagnostics.Error(ErrorIds.UnknownEscape, LineAt(escapeStart), $"`{text[escapeStart..position]}` names no code point");
            ok = false;
            return '?';
        }

        ok = true;
        return (int)value;
    }

    private void LexString()
    {
        var start = position;
        if (Peek() == '"' && Peek(2) == '"')
        {
            LexBlockString(start);
            return;
        }

        position++; // the opening quote
        var parts = new List<StringPart>();
        var buffer = new StringBuilder();
        while (!AtEnd && Current != '"' && Current != '\n')
        {
            if (Current == '{')
            {
                FlushText(parts, buffer);
                if (!ReadHole(parts, start))
                {
                    SkipRestOfLiteral();
                    return;
                }

                continue;
            }

            var point = ReadCharacter(out var ok);
            if (ok)
            {
                buffer.Append(char.ConvertFromUtf32(point));
            }
        }

        if (AtEnd || Current != '"')
        {
            Bad(start, position - start, "a string literal ends with `\"` on its line; a string over several lines opens with `\"\"\"`");
            return;
        }

        position++; // the closing quote
        FlushText(parts, buffer);
        Add(TokenKind.StringLiteral, start, position - start, new StringLiteralValue(parts));
    }

    private static void FlushText(List<StringPart> parts, StringBuilder buffer)
    {
        if (buffer.Length > 0)
        {
            parts.Add(new TextPart(buffer.ToString()));
            buffer.Clear();
        }
    }

    /// <summary>
    /// Reads a hole from its `{` to the `}` that closes it, skipping nested brackets and nested
    /// literals, and adds it as a <see cref="HolePart"/>. False after a `syntax` error, when the
    /// literal has been given up.
    /// </summary>
    private bool ReadHole(List<StringPart> parts, int literalStart)
    {
        var open = position;
        position++; // the `{`
        var depth = 1;
        while (!AtEnd && depth > 0 && Current != '\n')
        {
            var c = Current;
            if (c is '"' or '\'')
            {
                SkipNestedLiteral(c);
                continue;
            }

            if (c is '{' or '(' or '[')
            {
                depth++;
            }
            else if (c is '}' or ')' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    break;
                }
            }

            position++;
        }

        if (AtEnd || Current != '}')
        {
            Bad(literalStart, position - literalStart, "a `{` inside a string opens an interpolation that `}` closes on the same line; a brace is written `\\{`");
            return false;
        }

        var inner = text[(open + 1)..position];
        if (inner.Trim().Length == 0)
        {
            Bad(literalStart, position + 1 - literalStart, "an interpolation holds an expression");
            return false;
        }

        parts.Add(new HolePart(inner, LineAt(open + 1), open + 1));
        position++; // the `}`
        return true;
    }

    private void SkipNestedLiteral(char quote)
    {
        position++;
        while (!AtEnd && Current != quote && Current != '\n')
        {
            position += Current == '\\' && Peek() != '\n' && Peek() != '\0' ? 2 : 1; // a `\` before the line's end escapes nothing
        }

        if (!AtEnd && Current == quote)
        {
            position++;
        }
    }

    /// <summary>L13 and L15: a <c>"""</c> block.</summary>
    private void LexBlockString(int start)
    {
        position += 3;
        var afterOpen = position;
        while (!AtEnd && Current is ' ' or '\t' or '\r')
        {
            position++;
        }

        if (AtEnd || Current != '\n')
        {
            // L13: text beside the opening quotes. The whole literal, up to the next `"""`, is one error.
            var close = text.IndexOf("\"\"\"", afterOpen, StringComparison.Ordinal);
            position = close >= 0 && close < end ? close + 3 : end;
            Bad(start, position - start, "`\"\"\"` opens a block only at the end of a line; a one-line string is written with `\"`");
            return;
        }

        position++; // the line break after the opening
        var lines = new List<(int Offset, string Text)>();
        string? indentation = null;
        var closed = false;
        while (!AtEnd)
        {
            var lineStart = position;
            while (!AtEnd && Current != '\n')
            {
                position++;
            }

            var line = text[lineStart..position];
            var stripped = line.TrimStart(' ', '\t');
            if (!stripped.StartsWith("\"\"\"", StringComparison.Ordinal) && line.Contains("\"\"\"", StringComparison.Ordinal))
            {
                // L13, E1: text before a closing `"""`; the error is on that line, and the block before it gets no token
                Bad(lineStart, position - lineStart, "the closing `\"\"\"` stands on a line of its own");
                SkipToClosingLine();
                return;
            }

            if (stripped.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                indentation = line[..(line.Length - stripped.Length)];
                var rest = stripped[3..].TrimEnd(' ', '\t', '\r');
                if (rest.Length > 0)
                {
                    // L13, E1: the text stops making sense on the closing line; the block before it gets no token
                    Bad(lineStart, position - lineStart, "the closing `\"\"\"` stands on a line of its own");
                    return;
                }

                closed = true;
                break;
            }

            lines.Add((lineStart, line));
            if (!AtEnd)
            {
                position++; // the line break, which belongs to the text between two lines
            }
        }

        if (!closed)
        {
            Bad(start, position - start, "the `\"\"\"` block is never closed");
            return;
        }

        var parts = new List<StringPart>();
        var buffer = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var (offset, line) = lines[i];
            var content = line.TrimEnd('\r');
            if (content.Trim(' ', '\t').Length == 0)
            {
                content = "";
            }
            else if (content.StartsWith(indentation!, StringComparison.Ordinal))
            {
                content = content[indentation!.Length..];
                offset += indentation.Length;
            }
            else
            {
                diagnostics.Error(ErrorIds.BlockIndentation, LineAt(offset), "a line of a `\"\"\"` block starts with the indentation of its closing line");
                var trimmed = content.TrimStart(' ', '\t');
                offset += content.Length - trimmed.Length; // the content is read from where it starts, so a hole on the line stays whole
                content = trimmed;
            }

            // the content of one line, with its escapes and holes, read through the same code as a one-line literal
            var saved = (position, end: this.end);
            position = offset;
            var lineEnd = offset + content.Length;
            var ok = true;
            while (position < lineEnd)
            {
                if (Current == '{')
                {
                    FlushText(parts, buffer);
                    var holeOpen = position;
                    var depth = 0;
                    var p = position;
                    while (p < lineEnd)
                    {
                        if (text[p] == '{')
                        {
                            depth++;
                        }
                        else if (text[p] == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                break;
                            }
                        }

                        p++;
                    }

                    if (p >= lineEnd || text[(holeOpen + 1)..p].Trim().Length == 0)
                    {
                        ok = false;
                        break;
                    }

                    parts.Add(new HolePart(text[(holeOpen + 1)..p], LineAt(holeOpen + 1), holeOpen + 1));
                    position = p + 1;
                    continue;
                }

                var point = ReadCharacterBounded(lineEnd, out var okPoint);
                if (okPoint)
                {
                    buffer.Append(char.ConvertFromUtf32(point));
                }
            }

            position = saved.position;
            if (!ok)
            {
                // E1: the error is on the line of the brace, though the token covers the whole block
                Bad(start, position - start, "a `{` inside a `\"\"\"` block opens an interpolation that `}` closes on the same line; a brace is written `\\{`", LineAt(offset));
                return;
            }

            if (i < lines.Count - 1)
            {
                buffer.Append('\n');
            }
        }

        // the position is past the closing line already
        FlushText(parts, buffer);
        Add(TokenKind.StringLiteral, start, position - start, new StringLiteralValue(parts));
    }

    /// <summary>After an error inside a one-line literal: past its closing quote on this line, so that nothing inside it is read as code.</summary>
    private void SkipRestOfLiteral()
    {
        while (!AtEnd && Current != '"' && Current != '\n')
        {
            position += Current == '\\' && Peek() != '\n' && Peek() != '\0' ? 2 : 1;
        }

        if (!AtEnd && Current == '"')
        {
            position++;
        }
    }

    /// <summary>ReadCharacter for the content of one block line, which ends before <paramref name="limit"/>.</summary>
    private int ReadCharacterBounded(int limit, out bool ok)
    {
        if (Current != '\\')
        {
            return ReadCharacter(out ok);
        }

        if (position + 1 >= limit)
        {
            diagnostics.Error(ErrorIds.UnknownEscape, LineAt(position), "a `\\` at the end of a line is not an escape");
            position++;
            ok = false;
            return '?';
        }

        return ReadCharacter(out ok);
    }
}
