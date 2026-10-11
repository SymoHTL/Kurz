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
    [InlineData(".5", ".5", "")]
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
    [InlineData(".5h", ".5", "h")]
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
    [InlineData("print(1_.5)")]
    [InlineData("print(1_e5)")]
    [InlineData("print(1.5_e3)")]
    [InlineData("print(3kilo)")]
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
    public void Escapes_of_c_sharp_12_mean_what_they_mean_there(string program, string text)
    {
        // L7 and L16: the escapes of C# 12 (the owner, 2026-10-03); the `\e` of C# 13 is `unknown-escape` under L16's assumed version
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
        // L16: a backslash before any other character, or before `u`, `U` or `x` without their digits; digits that name a surrogate,
        // U+D800 to U+DFFF, or a value beyond U+10FFFF (the owner, 2026-10-10); and `\e`, which C# 13 added after L16's C# 12 list (assumed)
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
    public void Code_may_follow_the_closing_quotes_of_a_block_on_their_line()
    {
        // L13: "on a line of its own" is about what stands before the closing quotes (the record, 2026-10-10)
        var (tokens, diagnostics) = Lex("print(\"\"\"\n    abc\n    \"\"\")\nnext = 1");
        Assert.Empty(diagnostics.Items);
        var meaningful = tokens.Where(t => t.Kind is not TokenKind.Newline and not TokenKind.EndOfFile).ToList();
        Assert.Equal(
            [TokenKind.Name, TokenKind.LeftParen, TokenKind.StringLiteral, TokenKind.RightParen, TokenKind.Name, TokenKind.Assign, TokenKind.IntegerLiteral],
            meaningful.Select(t => t.Kind));
        Assert.Equal("abc", Assert.IsType<StringLiteralValue>(meaningful[2].Value).PlainText);
        Assert.Equal(3, meaningful[3].Line);
        Assert.Equal(4, meaningful[4].Line);
    }

    [Fact]
    public void A_closing_line_of_four_quotes_is_syntax_at_that_line()
    {
        // a reading L13 leaves open (README; a question for a design round): L13 names four quotes at the end of a line only; here the
        // block closes at its three quotes and the fourth opens a one-line literal that nothing closes
        Assert.Equal((ErrorIds.Syntax, 3), OnlyError("text = \"\"\"\n    abc\n    \"\"\"\""));
    }

    [Fact]
    public void Bad_tokens_with_no_readable_token_between_them_are_one_syntax()
    {
        // a reading E1 leaves open (README; a question for a design round): E1 asks one error per text and does not say where one run
        // of bad text ends; here a readable token between two bad ones makes two errors
        Assert.Equal([(ErrorIds.Syntax, 1)], Lex("c = 'ab' 'cd'").Diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal([(ErrorIds.Syntax, 1), (ErrorIds.Syntax, 1)], Lex("c = 'ab' + 'cd'").Diagnostics.Items.Select(d => (d.Id, d.Line)));
    }

    [Fact]
    public void A_block_that_opens_with_four_quotes_is_syntax_at_its_opening_line_alone()
    {
        // L13: there is no block of four or more quotes (the record, 2026-10-10); the closing line of four goes with the one error
        var (tokens, diagnostics) = Lex("text = \"\"\"\"\n    abc\n    \"\"\"\"\nprint(text)");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(4, tokens.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
    }

    [Fact]
    public void A_block_that_is_never_closed_is_syntax_at_its_opening_line_and_takes_the_rest_of_the_file()
    {
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError("text = \"\"\"\n    abc\n"));
        // the shape the README lists as low, pinned as it is: the code after the block goes with it
        var (tokens, diagnostics) = Lex("text = \"\"\"\n    abc\nprint(text)\n");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Name && t.Text == "print");
    }

    [Fact]
    public void Text_before_a_closing_triple_quote_on_a_content_line_is_syntax_at_that_line()
    {
        // the README's low shape, pinned as it is: with no line that starts with three quotes after it, the rest of the file goes with the block
        var (tokens, diagnostics) = Lex("text = \"\"\"\n    abc\"\"\"\nprint(text)");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Name && t.Text == "print");
    }

    [Fact]
    public void Text_beside_the_opening_quotes_is_syntax_at_the_opening_line_and_the_code_after_the_literal_is_read()
    {
        // L13: `\"\"\"abc\"\"\"` on one line ends at its closing quotes, and a block whose opening line holds text is passed over up
        // to its closing line, as a block with an error inside it is; the code after either is read
        var (oneLine, first) = Lex("s = \"\"\"hi\"\"\"\nprint(1)");
        Assert.Equal([(ErrorIds.Syntax, 1)], first.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(2, oneLine.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
        var (block, second) = Lex("s = \"\"\"hi\nprint(1)\nt = \"\"\"\n    a\n    \"\"\"\nnext = 1");
        Assert.Equal([(ErrorIds.Syntax, 1)], second.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(6, block.Single(t => t.Kind == TokenKind.Name && t.Text == "next").Line);
        Assert.DoesNotContain(block, t => t.Kind == TokenKind.Name && t.Text == "print");
    }

    [Fact]
    public void Text_beside_the_opening_quotes_with_no_closing_line_takes_the_rest_of_the_file()
    {
        // the README's low shape, pinned as it is: text or a fourth quote beside the opening quotes, and no line that starts with three
        // quotes after it, so the rest of the file goes with the one error
        var (text, first) = Lex("s = \"\"\"hi\nprint(1)\n");
        Assert.Equal([(ErrorIds.Syntax, 1)], first.Items.Select(d => (d.Id, d.Line)));
        Assert.DoesNotContain(text, t => t.Kind == TokenKind.Name && t.Text == "print");
        var (quote, second) = Lex("x = \"\"\"\"\nprint(1)\n");
        Assert.Equal([(ErrorIds.Syntax, 1)], second.Items.Select(d => (d.Id, d.Line)));
        Assert.DoesNotContain(quote, t => t.Kind == TokenKind.Name && t.Text == "print");
    }

    [Fact]
    public void A_form_feed_or_a_vertical_tab_is_text_no_rule_gives_a_meaning()
    {
        // a shape E4 covers by its general clause (README): L13 names the lone carriage return as whitespace outside a literal, and a
        // form feed and a vertical tab no rule names
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError("a = 1\fb"));
        Assert.Equal((ErrorIds.Syntax, 2), OnlyError("a = 1\n\vb = 2"));
    }

    [Fact]
    public void A_pair_of_surrogate_escapes_is_one_unknown_escape_at_the_first()
    {
        // L16: a pair of such escapes that C# would join is the error at the first (the owner, 2026-10-11); two apart are two errors
        Assert.Equal([(ErrorIds.UnknownEscape, 1)], Lex("print(\"\\uD83D\\uDE00\")").Diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal([(ErrorIds.UnknownEscape, 1), (ErrorIds.UnknownEscape, 1)], Lex("print(\"\\uD800 \\uD800\")").Diagnostics.Items.Select(d => (d.Id, d.Line)));
    }

    [Fact]
    public void A_failed_escape_in_a_char_literal_is_its_one_error()
    {
        // E1: the escape's error stands alone; the literal is read to its closing quote, and the parser passes it over
        Assert.Equal((ErrorIds.UnknownEscape, 1), OnlyError("c = '\\uZZZZ'"));
        Assert.Equal((ErrorIds.UnknownEscape, 1), OnlyError("c = '\\q'"));
    }

    [Fact]
    public void A_lone_surrogate_in_a_literal_is_syntax()
    {
        // L1: a lone surrogate is no code point; a file cannot hold one (its bytes would not be UTF-8), a program text from no file can
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError("s = \"a\uD800b\""));
        Assert.Equal((ErrorIds.Syntax, 1), OnlyError("c = '\uDC00'"));
    }

    [Fact]
    public void An_emoji_is_no_part_of_a_word_where_a_letter_outside_ascii_is()
    {
        // L9: a letter outside ASCII makes the whole word one `syntax`; a symbol is text of its own (E4), and the name after it is a name
        var emoji = Meaningful("x = \U0001F600b");
        Assert.Equal([TokenKind.Name, TokenKind.Assign, TokenKind.Bad, TokenKind.Name], emoji.Select(t => t.Kind));
        var letter = Meaningful("x = \U0001D400b");
        Assert.Equal([TokenKind.Name, TokenKind.Assign, TokenKind.Bad], letter.Select(t => t.Kind));
    }

    [Fact]
    public void An_escape_left_unfinished_at_the_end_of_a_block_line_is_one_error_and_the_next_line_is_text()
    {
        // L15, L16: the escape reads no further than its line, and the block goes on after it
        var (tokens, diagnostics) = Lex("text = \"\"\"\n    a\\x\n    b\n    \"\"\"\nprint(text)");
        Assert.Equal([(ErrorIds.UnknownEscape, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(5, tokens.Single(t => t.Kind == TokenKind.Name && t.Text == "print").Line);
        Assert.Equal("a\nb", Assert.IsType<StringLiteralValue>(tokens.Single(t => t.Kind == TokenKind.StringLiteral).Value).PlainText);
    }

    [Fact]
    public void An_interpolation_a_block_line_does_not_close_is_syntax_at_the_line_of_its_brace()
    {
        Assert.Equal((ErrorIds.Syntax, 3), OnlyError("text = \"\"\"\n    first\n    a {b\n    \"\"\""));
    }

    [Fact]
    public void A_lone_carriage_return_is_an_ordinary_character_in_a_literal_and_dropped_at_the_end_of_a_block_line()
    {
        // L13: an ordinary character inside a literal, except at the end of a block line, where every trailing one is dropped (the owner, 2026-10-10)
        Assert.Equal("a\rb", Assert.IsType<StringLiteralValue>(Single("\"a\rb\"").Value).PlainText);
        Assert.Equal("a\rb", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\rb\n    \"\"\"").Value).PlainText);
        Assert.Equal("a", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\r\r\n    \"\"\"").Value).PlainText);
        // the two-byte break at the end folds first (L1), so two lone carriage returns reach the lexer here; a trim of one would leave one
        Assert.Equal("a", Assert.IsType<StringLiteralValue>(Single("\"\"\"\n    a\r\r\r\n    \"\"\"").Value).PlainText);
    }

    [Fact]
    public void A_lone_carriage_return_outside_a_literal_is_whitespace()
    {
        // L13: whitespace outside a literal, so a file whose lines end in a carriage return alone is one line (the owner, 2026-10-10)
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
        // the code after the closing quotes is read after the error too
        var (tokens2, diagnostics2) = Lex("print(\"\"\"\n    abc\"\"\"\n    \"\"\")\nnext = 1");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics2.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(3, tokens2.Single(t => t.Kind == TokenKind.RightParen).Line);
        Assert.Equal(4, tokens2.Single(t => t.Kind == TokenKind.Name && t.Text == "next").Line);
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
    [InlineData("'{'", 123)]
    [InlineData("'\\''", 39)]
    [InlineData("'\t'", 9)]
    [InlineData("'\"'", 34)]
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
    [InlineData("c = '''")]
    [InlineData("c = '\n")]
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
        Assert.DoesNotContain('\r', SourceText.FromString("crlf.kz", "a = 1\r\nprint(a)\r\n").Text); // L1: the two bytes are one line break; unfolded, the carriage return would stay in the text
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
