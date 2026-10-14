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
    private int end; // the end of the lexer's text: the file's, a hole's (L6), or a block line's while that line is read (L13)
    private readonly bool inHole;
    private readonly DiagnosticBag diagnostics;
    private readonly List<Token> tokens = [];
    private int position;
    private int lastReportedBad = -1; // the index of the last Bad token that reported its error; a token PassOver added is none

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
        inHole = start != 0 || end != source.Text.Length;
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
            else if (c is ' ' or '\t' or '\r')
            {
                // a space, a tab, and a lone carriage return, which L13 reads as whitespace outside a literal (the owner, 2026-10-10);
                // a form feed or a vertical tab is text no rule gives a meaning (E4), read as such below
                position++;
            }
            else if (c == '/' && Peek() == '/')
            {
                if (inHole)
                {
                    // L3: a comment runs to the end of its line, which an interpolation (L6) does not reach; text no rule gives a meaning (E4)
                    Bad(position, end - position, "a `//` comment cannot stand inside an interpolation");
                    position = end;
                    continue;
                }

                while (!AtEnd && Current != '\n')
                {
                    position++;
                }
            }
            else if (IsAsciiDigit(c) || (c == '.' && IsAsciiDigit(Peek())))
            {
                LexNumber(); // L11: `.5` is a fraction literal; `..` is read as an operator before this, so `1..5` is a range
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

    /// <summary>
    /// The text at <paramref name="at"/> would be part of a word in a wider alphabet: a letter, a digit, a
    /// combining mark or a connector punctuation outside ASCII, a surrogate pair that encodes a letter or a
    /// digit, or C#'s `@`. A symbol, an emoji among them, is none of these: it is text of its own, which no
    /// rule gives a meaning (E4), and the name after it is read as a name (a reading on L9, README).
    /// </summary>
    private bool IsForeignWordPartAt(int at)
    {
        var c = text[at];
        if (c == '@')
        {
            return true;
        }

        if (c <= 127)
        {
            return false;
        }

        if (char.IsSurrogate(c))
        {
            return char.IsHighSurrogate(c) && at + 1 < end && char.IsLowSurrogate(text[at + 1]) && char.IsLetterOrDigit(text, at);
        }

        return char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;
    }

    private bool AtForeignWordPart => !AtEnd && IsForeignWordPartAt(position);

    /// <summary>Past the rest of a word that holds a character outside ASCII (L9), a surrogate pair as one character.</summary>
    private void SkipForeignWord()
    {
        while (!AtEnd && (IsNamePart(Current) || AtForeignWordPart))
        {
            position += char.IsHighSurrogate(Current) ? 2 : 1;
        }
    }

    private int LineAt(int offset) => source.LineOf(offset);

    private void Add(TokenKind kind, int start, int length, object? value = null) =>
        tokens.Add(new Token(kind, text.Substring(start, length), LineAt(start), start, value));

    private void Bad(int start, int length, string why) => Bad(start, length, why, LineAt(start));

    /// <summary>Text no rule gives a meaning: one <c>syntax</c> at <paramref name="line"/>, and a token the parser passes over.</summary>
    private void Bad(int start, int length, string why, int line)
    {
        // E1: a run of text no rule gives a meaning is one error, so a bad token right after another on its line adds none (`'''`);
        // a token PassOver added reported nothing, so the bad token after it is the first of its run
        var continuesRun = tokens.Count > 0 && lastReportedBad == tokens.Count - 1 && tokens[^1].Line == line;
        if (!continuesRun)
        {
            diagnostics.Error(ErrorIds.Syntax, line, why);
        }

        lastReportedBad = tokens.Count;
        tokens.Add(new Token(TokenKind.Bad, text.Substring(start, Math.Min(length, end - start)), line, start));
    }

    /// <summary>A token the parser passes over, for text whose error another id reported already (an escape in a <c>char</c> literal): no second error (E1).</summary>
    private void PassOver(int start, int length) => tokens.Add(new Token(TokenKind.Bad, text.Substring(start, Math.Min(length, end - start)), LineAt(start), start));

    /// <summary>
    /// After an error inside a <c>"""</c> block: passes over the rest of the block, up to and including the
    /// closing <c>"""</c>, so that the block's text is not read as code; what follows the quotes on their line is (L13).
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

            var line = text[lineStart..position];
            var stripped = line.TrimStart(' ', '\t');
            if (stripped.StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                position = lineStart + (line.Length - stripped.Length) + 3;
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

        if (AtForeignWordPart)
        {
            // L9: a letter is one of ASCII's. The whole word is one `syntax`, not a name and a stray character.
            SkipForeignWord();
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
            // L11: "`_` may also stand directly after the prefix (`0x_FF`)", where every other `_` stands between digits
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
                if (position > start && text[position - 1] == '_')
                {
                    FailNumber(start, "a `_` stands between digits (L11), not before the `.` of a fraction");
                    return;
                }

                isReal = true;
                digits.Append('.');
                position++;
                ReadDigits(digits, 10, allowLeadingUnderscore: false);
            }

            if (Current is 'e' or 'E' && (IsAsciiDigit(Peek()) || (Peek() is '+' or '-' && IsAsciiDigit(Peek(2)))))
            {
                if (position > start && text[position - 1] == '_')
                {
                    FailNumber(start, "a `_` stands between digits (L11), not before the exponent");
                    return;
                }

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
            Bad(start, position - start, "a `_` stands between digits (L11), not at the end of a number");
            return;
        }

        // what follows the digits directly: a suffix, a unit, or text no rule gives a meaning
        var tailStart = position;
        SkipForeignWord();

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

        var upper = tail.ToUpperInvariant();
        if (upper is "U" or "L" or "UL" or "LU")
        {
            if (isReal)
            {
                // L11: an integer suffix after a fraction or an exponent is no form C# gives, so it is `syntax` (E4)
                Bad(start, position - start, $"the integer suffix `{tail}` does not apply to a fraction or an exponent");
                return;
            }

            if (tail.Contains('l', StringComparison.Ordinal))
            {
                // L11: a suffix holding a lower-case `l` is `syntax`, because `1l` reads as `11`
                Bad(start, position - start, "the suffix `L` is written in upper case: `1l` reads as `11`");
                return;
            }

            Add(TokenKind.IntegerLiteral, start, position - start, new IntegerLiteralValue(digits.ToString(), radix, upper == "LU" ? "UL" : upper));
            return;
        }

        if (upper is "F" or "D" or "M")
        {
            if (radix != 10)
            {
                Bad(start, position - start, $"the suffix `{tail}` does not apply to a hexadecimal or binary literal");
                return;
            }

            Add(TokenKind.RealLiteral, start, position - start, new RealLiteralValue(digits.ToString(), upper.ToLowerInvariant()));
            return;
        }

        Bad(start, position - start, $"`{tail}` is neither a suffix nor a unit of a number");
    }

    /// <summary>A number form no rule gives a meaning (L11, E4): the rest of the literal, a fraction, an exponent and a tail included, goes with the one error.</summary>
    private void FailNumber(int start, string why)
    {
        while (!AtEnd && (IsNamePart(Current) || (Current == '.' && IsAsciiDigit(Peek())) || (Current is '+' or '-' && text[position - 1] is 'e' or 'E')))
        {
            position++;
        }

        Bad(start, position - start, why);
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
            if (AtForeignWordPart)
            {
                SkipForeignWord();
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
        var failed = false;
        while (!AtEnd && Current != '\'' && Current != '\n')
        {
            if (failed)
            {
                // E1: after the first failed escape the literal is read to its end and nothing else is said about it, so a second
                // bad escape in `'\q\q'` adds no error
                position += Current == '\\' && Peek() != '\n' && Peek() != '\0' ? 2 : 1;
                continue;
            }

            var point = ReadCharacter(out var ok);
            if (!ok)
            {
                failed = true; // the error is reported; the literal is read to its end, and nothing else is said about it (E1)
                continue;
            }

            points.Add(point);
        }

        if (AtEnd || Current != '\'')
        {
            if (failed)
            {
                PassOver(start, position - start); // the escape's error stands for the literal: an unterminated `'\q` adds no `syntax` (E1)
                return;
            }

            Bad(start, position - start, "a `char` literal ends with `'` on its line (T21)");
            return;
        }

        position++; // the closing quote
        if (failed)
        {
            PassOver(start, position - start);
            return;
        }

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
    /// <c>unknown-escape</c> at its line, and a lone surrogate <c>syntax</c>; <paramref name="ok"/> is
    /// false then, and the position stands after what the error covers: the backslash and the
    /// character after it (both chars of an escaped surrogate pair), the hex digits a <c>\u</c>,
    /// <c>\U</c> or <c>\x</c> read and a second surrogate escape that goes with the first, or, for a
    /// backslash at the end of a line, on the line break.
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

            if (char.IsSurrogate(c))
            {
                // a string holds code points (L1, T16), and a lone surrogate is none: text no rule gives a meaning (E4's general clause, a
                // reading listed in the README); a file cannot hold one, since the bytes would not be UTF-8, so this is text from no file
                diagnostics.Error(ErrorIds.Syntax, LineAt(position), "a lone surrogate is no character of a literal");
                position++;
                ok = false;
                return '?';
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
            // L16: digits that name a surrogate or a value beyond U+10FFFF are `unknown-escape` (the owner, 2026-10-10), and a pair of
            // surrogate escapes that C# would join, a high surrogate followed by a low one, is the error at the first (the owner,
            // 2026-10-11): the low one directly after goes with it; two escapes in another order are two errors
            diagnostics.Error(ErrorIds.UnknownEscape, LineAt(escapeStart), $"`{text[escapeStart..position]}` names no code point");
            if (value is >= 0xD800 and <= 0xDBFF)
            {
                SkipLowSurrogateEscape();
            }

            ok = false;
            return '?';
        }

        ok = true;
        return (int)value;
    }

    /// <summary>Past a <c>\u</c>, <c>\U</c> or <c>\x</c> escape at the cursor whose digits name a low surrogate, U+DC00 to U+DFFF, which C# would join with the high one before it, so the error of that one covers it (L16); otherwise nothing moves.</summary>
    private void SkipLowSurrogateEscape()
    {
        if (Current != '\\' || Peek() is not ('u' or 'U' or 'x'))
        {
            return;
        }

        var (min, max) = Peek() switch
        {
            'u' => (4, 4),
            'U' => (8, 8),
            _ => (1, 4),
        };
        var digitsStart = position + 2;
        var p = digitsStart;
        while (p < end && p - digitsStart < max && IsDigitOf(text[p], 16))
        {
            p++;
        }

        if (p - digitsStart >= min && long.Parse(text.AsSpan(digitsStart, p - digitsStart), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) is >= 0xDC00 and <= 0xDFFF)
        {
            position = p;
        }
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
        var failed = false;
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
            else
            {
                failed = true;
            }
        }

        if (AtEnd || Current != '"')
        {
            if (failed)
            {
                PassOver(start, position - start); // E1: the escape's error stands for the literal, `"a\` at the end of a line included
                return;
            }

            // L13: an ordinary literal ends on the line it starts on, so one that does not is text no rule gives a meaning (E4)
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
    /// Reads a hole from its `{` to the `}` that closes it, the brackets inside it matched by kind and the
    /// nested literals passed over, and adds it as a <see cref="HolePart"/>. False after a `syntax` error,
    /// when the literal has been given up: a `}` missing on the line, a hole with nothing in it, or a `)`
    /// or `]` that closes no bracket of its kind.
    /// </summary>
    private bool ReadHole(List<StringPart> parts, int literalStart)
    {
        var open = position;
        position++; // the `{`
        var closers = new Stack<char>();
        closers.Push('}');
        while (!AtEnd && Current != '\n')
        {
            var c = Current;
            if (c is '"' or '\'')
            {
                SkipNestedLiteral(c);
                continue;
            }

            if (c is '{' or '(' or '[')
            {
                closers.Push(c == '{' ? '}' : c == '(' ? ')' : ']');
            }
            else if (c is '}' or ')' or ']')
            {
                if (closers.Peek() != c)
                {
                    Bad(literalStart, position + 1 - literalStart, $"`{c}` closes no open bracket inside the interpolation");
                    return false;
                }

                closers.Pop();
                if (closers.Count == 0)
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

        if (AtEnd)
        {
            // E4: a `"""` block that is never closed is `syntax` at its opening line; here the file ends on the opening quotes
            Bad(start, position - start, "the `\"\"\"` block is never closed");
            return;
        }

        if (Current != '\n')
        {
            // L13: text beside the opening quotes is one `syntax`. `"""abc"""` on one line ends at its closing quotes; a block
            // whose opening line holds text is passed over up to its closing line, as a block with an error inside it is
            // (recovery under no rule, README)
            var lineEnd = text.IndexOf('\n', afterOpen, end - afterOpen);
            lineEnd = lineEnd < 0 ? end : lineEnd;
            var close = text.IndexOf("\"\"\"", afterOpen, lineEnd - afterOpen, StringComparison.Ordinal);
            if (close >= 0)
            {
                position = close + 3;
            }
            else
            {
                position = lineEnd;
                SkipToClosingLine();
            }

            while (!AtEnd && Current == '"')
            {
                position++; // `""""` at the end of the line, and a closing line of four or more quotes, go with the one error (L13)
            }

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
                // L13: "on a line of its own" is about what stands before the closing quotes; code may follow them, `""")` closing a call
                position = lineStart + indentation.Length + 3;
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
                diagnostics.Error(ErrorIds.BlockIndentation, LineAt(offset), "a line of a `\"\"\"` block must start with the indentation of its closing line (L13)");
                var trimmed = content.TrimStart(' ', '\t');
                offset += content.Length - trimmed.Length; // the content is read from where it starts, so a hole on the line stays whole
                content = trimmed;
            }

            // the content of one line, with its escapes and holes, read through the same code as a one-line literal
            var saved = (position, end: this.end);
            position = offset;
            var lineEnd = offset + content.Length;
            end = lineEnd; // an escape of this line cannot read into the next line or the closing quotes
            var ok = true;
            string? why = null;
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

                    var unclosed = p >= lineEnd;
                    if (unclosed || text[(holeOpen + 1)..p].Trim().Length == 0)
                    {
                        why = unclosed
                            ? "a `{` inside a `\"\"\"` block opens an interpolation that `}` closes on the same line; a brace is written `\\{`"
                            : "an interpolation holds an expression";
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

            (position, end) = saved;
            if (!ok)
            {
                // E1, E4: the error is on the line of the brace, though the token covers the whole block
                Bad(start, position - start, why!, LineAt(offset));
                return;
            }

            if (i < lines.Count - 1)
            {
                buffer.Append('\n');
            }
        }

        // the position is right after the closing quotes; the rest of their line is read as code
        FlushText(parts, buffer);
        Add(TokenKind.StringLiteral, start, position - start, new StringLiteralValue(parts));
    }

    /// <summary>
    /// After an error inside a one-line literal: past its closing quote on this line, the holes after the error and the
    /// literals nested in them passed over as <see cref="ReadHole"/> would read them, so that nothing inside it is read as code.
    /// </summary>
    private void SkipRestOfLiteral()
    {
        while (!AtEnd && Current != '"' && Current != '\n')
        {
            if (Current == '{')
            {
                SkipHole();
                continue;
            }

            position += Current == '\\' && Peek() != '\n' && Peek() != '\0' ? 2 : 1;
        }

        if (!AtEnd && Current == '"')
        {
            position++;
        }
    }

    /// <summary>Past a hole at the cursor without a report: brackets counted, nested literals passed over; stops at the line's end.</summary>
    private void SkipHole()
    {
        var depth = 0;
        while (!AtEnd && Current != '\n')
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
                if (depth <= 0)
                {
                    position++;
                    return;
                }
            }

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
