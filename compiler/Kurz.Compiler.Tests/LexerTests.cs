using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The lexer against chapter 1 of the reference. Every expectation is written out; an error is
/// pinned by its id and its line, never by its message (E2).
/// </summary>
public sealed class LexerTests
{
    private static (IReadOnlyList<Token> Tokens, DiagnosticBag Diagnostics) Lex(string program)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(SourceText.FromString("test.kz", program), diagnostics).Lex();
        return (tokens, diagnostics);
    }

    private static IReadOnlyList<Token> Meaningful(string program) =>
        Lex(program).Tokens.Where(t => t.Kind is not TokenKind.Newline and not TokenKind.EndOfFile).ToList();

    private static Token Single(string program)
    {
        var (tokens, diagnostics) = Lex(program);
        Assert.Empty(diagnostics.Items);
        var meaningful = tokens.Where(t => t.Kind is not TokenKind.Newline and not TokenKind.EndOfFile).ToList();
        return Assert.Single(meaningful);
    }

    private static (string Id, int Line) OnlyError(string program)
    {
        var (_, diagnostics) = Lex(program);
        var error = Assert.Single(diagnostics.Items);
        return (error.Id, error.Line);
    }

    [Fact]
    public void Names_and_core_words_are_told_apart()
    {
        var tokens = Meaningful("total Total _count2 if match string equal");
        Assert.Equal(
            [TokenKind.Name, TokenKind.Name, TokenKind.Name, TokenKind.Keyword, TokenKind.Keyword, TokenKind.Keyword, TokenKind.Keyword],
            tokens.Select(t => t.Kind));
        Assert.Equal(["total", "Total", "_count2", "if", "match", "string", "equal"], tokens.Select(t => t.Text));
    }

    [Theory]
    [InlineData("zähler = 1")]
    [InlineData("@total = 1")]
    [InlineData("print(zähler)")]
    public void A_name_with_a_character_outside_ascii_or_an_at_is_syntax(string program)
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError(program));
    }

    [Fact]
    public void A_name_in_the_second_line_reports_the_second_line()
    {
        Assert.Equal((ErrorIds.Syntax, 2), OnlyError("a = 1\nzähler = 2\n"));
    }

    [Theory]
    [InlineData("0xFF", "FF", 16, "")]
    [InlineData("0b101", "101", 2, "")]
    [InlineData("0x_FF", "FF", 16, "")]
    [InlineData("0b_101", "101", 2, "")]
    [InlineData("1_000_000", "1000000", 10, "")]
    [InlineData("4_000_000_000L", "4000000000", 10, "L")]
    [InlineData("7u", "7", 10, "U")]
    [InlineData("7UL", "7", 10, "UL")]
    [InlineData("7Lu", "7", 10, "UL")]
    [InlineData("99999999999999999999", "99999999999999999999", 10, "")]
    public void Integer_literals_keep_their_digits_radix_and_suffix(string program, string digits, int radix, string suffix)
    {
        var token = Single(program);
        Assert.Equal(TokenKind.IntegerLiteral, token.Kind);
        Assert.Equal(new IntegerLiteralValue(digits, radix, suffix), token.Value);
    }

    [Theory]
    [InlineData("1e3", "1e3", "")]
    [InlineData("2.5E-3", "2.5e-3", "")]
    [InlineData("1.5e2", "1.5e2", "")]
    [InlineData("0.5", "0.5", "")]
    [InlineData("0.5f", "0.5", "f")]
    [InlineData("1.50m", "1.50", "m")]
    [InlineData("3d", "3", "d")]
    [InlineData("100000000000000000000.0", "100000000000000000000.0", "")]
    public void Real_literals_keep_their_text_and_suffix(string program, string text, string suffix)
    {
        var token = Single(program);
        Assert.Equal(TokenKind.RealLiteral, token.Kind);
        Assert.Equal(new RealLiteralValue(text, suffix), token.Value);
    }

    [Theory]
    [InlineData("2kb", "2", "kb")]
    [InlineData("1mb", "1", "mb")]
    [InlineData("1.5s", "1.5", "s")]
    [InlineData("0.5h", "0.5", "h")]
    [InlineData("90min", "90", "min")]
    [InlineData("100000days", "100000", "days")]
    [InlineData("1500ms", "1500", "ms")]
    [InlineData("0.0000000001s", "0.0000000001", "s")]
    public void Unit_literals_keep_their_number_and_unit(string program, string number, string unit)
    {
        var token = Single(program);
        Assert.Equal(TokenKind.UnitLiteral, token.Kind);
        Assert.Equal(new UnitLiteralValue(number, unit), token.Value);
    }

    [Theory]
    [InlineData("x = 1l")]
    [InlineData("x = 1ul")]
    [InlineData("print(0x10ms)")]
    [InlineData("print(0b1s)")]
    [InlineData("print(1Lms)")]
    [InlineData("print(1e3s)")]
    [InlineData("print(1x)")]
    [InlineData("print(1_)")]
    [InlineData("print(0x)")]
    public void A_number_form_no_rule_gives_a_meaning_is_syntax(string program)
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError(program));
    }

    [Fact]
    public void A_range_after_an_integer_is_not_a_fraction()
    {
        var tokens = Meaningful("1..5 0..<n");
        Assert.Equal(
            [TokenKind.IntegerLiteral, TokenKind.DotDot, TokenKind.IntegerLiteral, TokenKind.IntegerLiteral, TokenKind.DotDotLess, TokenKind.Name],
            tokens.Select(t => t.Kind));
    }

    [Fact]
    public void A_plain_string_is_one_text_part()
    {
        var token = Single("\"hello\"");
        Assert.Equal(TokenKind.StringLiteral, token.Kind);
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal("hello", value.PlainText);
    }

    [Fact]
    public void A_hole_is_kept_as_its_source_with_its_line()
    {
        var token = Single("\n\"hello {name}, {count + 1} items\"");
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal(
            [new TextPart("hello "), new HolePart("name", 2, 9), new TextPart(", "), new HolePart("count + 1", 2, 17), new TextPart(" items")],
            value.Parts);
    }

    [Theory]
    [InlineData("\"a \\{b}\"", "a {b}")]
    [InlineData("\"say \\\"hi\\\"\"", "say \"hi\"")]
    [InlineData("\"\\u0041\\x42|\\U00000043\"", "AB|C")]
    [InlineData("\"tab\\there\\nnew\"", "tab\there\nnew")]
    [InlineData("\"\\0\\a\\b\\f\\r\\v\\'\\\\\"", "\0\a\b\f\r\v'\\")]
    [InlineData("\"a } b\"", "a } b")]
    [InlineData("\"aä😀\"", "aä😀")]
    public void Escapes_of_c_sharp_mean_what_they_mean_there(string program, string text)
    {
        var token = Single(program);
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal(text, value.PlainText);
    }

    [Theory]
    [InlineData("print(\"\\u12\")")]
    [InlineData("print(\"a\\q\")")]
    [InlineData("print(\"\\}\")")]
    [InlineData("print(\"\\xg\")")]
    [InlineData("print(\"\\U00110000\")")]
    [InlineData("print(\"\\e\")")]
    [InlineData("print(\"\\uD800\")")]
    [InlineData("print(\"\\U0000DFFF\")")]
    [InlineData("print(\"\\xD800\")")]
    [InlineData("print(\"\\U80000000\")")]
    [InlineData("print(\"\\UFFFFFFFF\")")]
    [InlineData("print(\"\\😀\")")]
    [InlineData("c = '\\😀'")]
    public void An_escape_no_rule_gives_a_meaning_is_unknown_escape(string program)
    {
        Assert.Equal((ErrorIds.UnknownEscape, 1), OnlyError(program));
    }

    [Theory]
    [InlineData("text = \"\"\"abc\"\"\"")]
    [InlineData("text = \"\"\"abc\n    \"\"\"")]
    [InlineData("text = \"abc")]
    [InlineData("text = \"abc\nprint(text)")]
    [InlineData("text = \"{}\"")]
    [InlineData("text = \"{a\"")]
    public void A_string_shape_no_rule_gives_a_meaning_is_syntax(string program)
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError(program));
    }

    [Fact]
    public void Text_after_the_closing_quotes_of_a_block_is_syntax_at_the_closing_line()
    {
        Assert.Equal((ErrorIds.Syntax, 3), OnlyError("text = \"\"\"\n    abc\n    \"\"\" tail"));
    }

    [Fact]
    public void A_block_that_is_never_closed_is_syntax_at_its_opening_line()
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError("text = \"\"\"\n    abc\n"));
    }

    [Fact]
    public void Text_before_a_closing_triple_quote_on_a_content_line_is_syntax_at_that_line()
    {
        Assert.Equal((ErrorIds.Syntax, 2), OnlyError("text = \"\"\"\n    abc\"\"\"\nprint(text)"));
    }

    [Fact]
    public void An_interpolation_a_block_line_does_not_close_is_syntax_at_the_line_of_its_brace()
    {
        Assert.Equal((ErrorIds.Syntax, 3), OnlyError("text = \"\"\"\n    first\n    a {b\n    \"\"\""));
    }

    [Fact]
    public void A_lone_carriage_return_is_an_ordinary_character_in_a_literal_and_dropped_at_the_end_of_a_block_line()
    {
        Assert.Equal("a\rb", Assert.IsType<StringLiteralValue>(Single("\"a\rb\"").Value).PlainText);
        Assert.Equal("a\rb", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\rb\n    \"\"\"").Value).PlainText);
        Assert.Equal("a", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\r\r\n    \"\"\"").Value).PlainText);
        // the two-byte break at the end folds first (L1), so two lone carriage returns reach the lexer here; a trim of one would leave one
        Assert.Equal("a", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\r\r\r\n    \"\"\"").Value).PlainText);
    }

    [Fact]
    public void A_lone_carriage_return_outside_a_literal_is_whitespace()
    {
        var (tokens, diagnostics) = Lex("a = 1\rb = 2");
        Assert.Empty(diagnostics.Items);
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Newline);
        Assert.All(tokens, t => Assert.Equal(1, t.Line));
        string[] texts = ["a", "=", "1", "b", "=", "2"];
        Assert.Equal(texts, tokens.Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text));
    }

    [Fact]
    public void Spaces_and_tabs_after_the_opening_or_the_closing_quotes_of_a_block_are_not_text()
    {
        Assert.Equal("a", Assert.IsType<StringLiteralValue>(Single("\"\"\"  \t\n    a\n    \"\"\" \t").Value).PlainText);
    }

    [Fact]
    public void A_literal_is_not_normalized()
    {
        // L1: the decomposed form stays two code points
        Assert.Equal("a\u0308", Assert.IsType<StringLiteralValue>(Single("\"a\u0308\"").Value).PlainText);
    }

    [Fact]
    public void An_under_indented_block_line_with_an_interpolation_is_block_indentation_alone()
    {
        Assert.Equal((ErrorIds.BlockIndentation, 3), OnlyError("text = \"\"\"\n    a\n  Hello {name}\n    \"\"\""));
    }

    [Fact]
    public void The_rest_of_a_block_with_a_stray_triple_quote_is_passed_over_up_to_its_closing_line()
    {
        var (tokens, diagnostics) = Lex("text = \"\"\"\n    abc\"\"\"\n    more\n    \"\"\"\nprint(text)");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(5, tokens.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
    }

    [Fact]
    public void A_block_string_drops_the_indentation_of_its_closing_line()
    {
        var token = Single("\"\"\"\n    SELECT name\n      FROM users\n    \"\"\"");
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal("SELECT name\n  FROM users", value.PlainText);
    }

    [Fact]
    public void A_block_string_line_of_only_whitespace_is_an_empty_line()
    {
        var token = Single("\"\"\"\n    a\n  \n    b\n    \"\"\"");
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal("a\n\nb", value.PlainText);
    }

    [Fact]
    public void A_block_string_line_with_less_indentation_is_block_indentation_at_its_line()
    {
        Assert.Equal((ErrorIds.BlockIndentation, 3), OnlyError("text = \"\"\"\n        SELECT name\n    FROM users\n        \"\"\"\nprint(text)\n"));
    }

    [Fact]
    public void A_block_string_has_escapes_and_holes_and_an_ordinary_quote()
    {
        var token = Single("\"\"\"\n    \\{ \"table\": \"{table}\" }\n    a\\\\b\n    \"\"\"");
        var value = Assert.IsType<StringLiteralValue>(token.Value);
        Assert.Equal(
            [new TextPart("{ \"table\": \""), new HolePart("table", 2, 22), new TextPart("\" }\na\\b")],
            value.Parts);
    }

    [Theory]
    [InlineData("'a'", 97)]
    [InlineData("'\\n'", 10)]
    [InlineData("'ä'", 228)]
    [InlineData("'😀'", 0x1F600)]
    [InlineData("'\\u0041'", 65)]
    public void A_char_literal_is_one_code_point(string program, int codePoint)
    {
        var token = Single(program);
        Assert.Equal(TokenKind.CharLiteral, token.Kind);
        Assert.Equal(codePoint, token.Value);
    }

    [Theory]
    [InlineData("c = 'ab'")]
    [InlineData("c = ''")]
    [InlineData("c = 'a")]
    public void A_char_literal_with_another_count_of_code_points_is_syntax(string program)
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError(program));
    }

    [Fact]
    public void A_comment_runs_to_the_end_of_its_line_and_a_string_holds_no_comment()
    {
        var tokens = Meaningful("a = 1 // a comment\nprint(\"http://x\")");
        Assert.Equal(
            [TokenKind.Name, TokenKind.Assign, TokenKind.IntegerLiteral, TokenKind.Name, TokenKind.LeftParen, TokenKind.StringLiteral, TokenKind.RightParen],
            tokens.Select(t => t.Kind));
    }

    [Fact]
    public void Line_breaks_are_tokens_with_the_line_of_the_token_after_them()
    {
        var (tokens, diagnostics) = Lex("a = 1\n\nprint(a)\n");
        Assert.Empty(diagnostics.Items);
        Assert.Equal(
            [TokenKind.Name, TokenKind.Assign, TokenKind.IntegerLiteral, TokenKind.Newline, TokenKind.Newline, TokenKind.Name, TokenKind.LeftParen, TokenKind.Name, TokenKind.RightParen, TokenKind.Newline, TokenKind.EndOfFile],
            tokens.Select(t => t.Kind));
        Assert.Equal(3, tokens[5].Line);
    }

    [Fact]
    public void Every_operator_is_one_token()
    {
        var program = "+ - * / % +% -% *% == != < <= > >= && || ! & | ^ ~ << >> = => . .. ..< ?. ?? ? : , ( ) [ ] { } ;";
        var kinds = Meaningful(program).Select(t => t.Kind).ToList();
        Assert.Equal(
            [
                TokenKind.Plus, TokenKind.Minus, TokenKind.Star, TokenKind.Slash, TokenKind.Percent,
                TokenKind.PlusPercent, TokenKind.MinusPercent, TokenKind.StarPercent,
                TokenKind.EqualEqual, TokenKind.NotEqual, TokenKind.Less, TokenKind.LessEqual, TokenKind.Greater, TokenKind.GreaterEqual,
                TokenKind.AmpersandAmpersand, TokenKind.BarBar, TokenKind.Bang, TokenKind.Ampersand, TokenKind.Bar, TokenKind.Caret, TokenKind.Tilde,
                TokenKind.LessLess, TokenKind.GreaterGreater, TokenKind.Assign, TokenKind.Arrow, TokenKind.Dot, TokenKind.DotDot, TokenKind.DotDotLess,
                TokenKind.QuestionDot, TokenKind.QuestionQuestion, TokenKind.Question, TokenKind.Colon, TokenKind.Comma,
                TokenKind.LeftParen, TokenKind.RightParen, TokenKind.LeftBracket, TokenKind.RightBracket, TokenKind.LeftBrace, TokenKind.RightBrace,
                TokenKind.Semicolon,
            ],
            kinds);
    }

    [Fact]
    public void A_character_with_no_meaning_is_syntax()
    {
        Assert.Equal((ErrorIds.Syntax, 2), OnlyError("a = 1\nb = a $ 2"));
    }

    [Fact]
    public void A_windows_line_break_reads_as_a_line_break()
    {
        var (tokens, _) = Lex("a = 1\r\nprint(a)\r\n");
        Assert.Equal(2, tokens.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
        Assert.DoesNotContain(tokens, t => t.Text.Contains('\r', StringComparison.Ordinal));
    }

    [Fact]
    public void A_windows_line_break_reads_as_one_inside_a_block_string_too()
    {
        var bytes = "text = \"\"\"\r\n    SELECT name\r\n      FROM users\r\n    \"\"\"\r\nprint(text)\r\n"u8.ToArray();
        var source = SourceText.FromBytes("crlf.kz", bytes, out var error);
        Assert.Null(error);
        Assert.DoesNotContain('\r', source!.Text);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source!, diagnostics).Lex();
        Assert.Empty(diagnostics.Items);
        var value = Assert.IsType<StringLiteralValue>(tokens.Single(t => t.Kind == TokenKind.StringLiteral).Value);
        Assert.Equal("SELECT name\n  FROM users", value.PlainText);
        Assert.Equal(5, tokens.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
    }

    [Fact]
    public void The_first_bad_byte_decides_the_line_in_a_file_with_windows_line_breaks()
    {
        var bytes = "a = 1\r\nb = 2\r\n"u8.ToArray().Concat(new byte[] { 0xFF }).Concat("\r\nc = 3\r\n"u8.ToArray()).Concat(new byte[] { 0xFE, (byte)'\n' }).ToArray();
        var source = SourceText.FromBytes("bad.kz", bytes, out var error);
        Assert.Null(source);
        Assert.Equal((ErrorIds.InvalidSource, 3), (error!.Id, error.Line));
    }

    [Fact]
    public void An_encoded_surrogate_is_invalid_source_at_its_line()
    {
        var bytes = "x = 1\nprint(\""u8.ToArray().Concat(new byte[] { 0xED, 0xA0, 0x80, (byte)'"', (byte)')', (byte)'\n' }).ToArray();
        var source = SourceText.FromBytes("surrogate.kz", bytes, out var error);
        Assert.Null(source);
        Assert.Equal((ErrorIds.InvalidSource, 2), (error!.Id, error.Line));
    }

    [Fact]
    public void A_file_that_is_not_utf8_is_invalid_source_at_the_line_of_the_first_bad_byte()
    {
        var bytes = "a = 1\nprint(\"x"u8.ToArray().Concat(new byte[] { 0xFF, 0xFE, (byte)'"', (byte)')', (byte)'\n' }).ToArray();
        var source = SourceText.FromBytes("bad.kz", bytes, out var error);
        Assert.Null(source);
        Assert.Equal((ErrorIds.InvalidSource, 2), (error!.Id, error.Line));
    }

    [Fact]
    public void A_byte_order_mark_is_skipped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("x = 1\n"u8.ToArray()).ToArray();
        var source = SourceText.FromBytes("bom.kz", bytes, out var error);
        Assert.Null(error);
        Assert.Equal("x = 1\n", source!.Text);
    }
}
