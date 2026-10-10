using Kurz.Compiler.Text;

namespace Kurz.Compiler.Syntax;

/// <summary>
/// Builds the syntax tree of one file from its tokens. A line break ends a statement (L2) except
/// inside an open <c>(</c> or <c>[</c>, after a binary operator, a comma, <c>=</c> or <c>=></c>,
/// and before a line that starts with <c>.</c> (L4); a block between braces holds statements of its
/// own, each on its line, also inside an open bracket. Text no rule gives a meaning is the one
/// error <c>syntax</c> (E4), reported on the line where the text stops making sense; after it the
/// rest of the statement is skipped, so one statement yields one error.
/// </summary>
public sealed class Parser
{
    private readonly SourceText source;
    private readonly List<Token> tokens;
    private readonly DiagnosticBag diagnostics;
    private int index;
    private int suppressNewlines;
    private int speculating;

    private readonly HashSet<int> badLines;
    private int openBraces; // braces a construct of the statement opened (`with { }`, an enum body): recovery closes them
    private List<Diagnostic> pending = new(); // reported while speculating: kept when that parse succeeds, dropped when it is rewound
    private bool committed; // the speculative parse has seen the shape of a declaration: an error after that is reported, not rewound
    private readonly List<(int Index, Token Original)> tokenEdits = new(); // tokens a speculative parse rewrote (`>>`): undone with the rewind

    private Parser(SourceText source, IEnumerable<Token> tokens, DiagnosticBag diagnostics)
    {
        this.source = source;
        this.tokens = tokens.ToList();
        this.diagnostics = diagnostics;
        // the lexer has reported `syntax` for every Bad token; a second report on that line would be a second error for one text
        badLines = this.tokens.Where(t => t.Kind == TokenKind.Bad).Select(t => t.Line).ToHashSet();
    }

    /// <summary>Lexes and parses a whole file.</summary>
    public static ProgramSyntax Parse(SourceText source, DiagnosticBag diagnostics)
    {
        var tokens = new Lexer(source, diagnostics).Lex();
        var parser = new Parser(source, tokens, diagnostics);
        var statements = parser.ParseStatementsUntil(TokenKind.EndOfFile, topLevel: true);
        return new ProgramSyntax(statements);
    }

    private sealed class ParseError(int line, string message) : Exception(message)
    {
        public int Line { get; } = line;
    }

    // ---- tokens --------------------------------------------------------------------------------

    private Token Current
    {
        get
        {
            if (suppressNewlines > 0)
            {
                while (tokens[index].Kind == TokenKind.Newline)
                {
                    index++;
                }
            }

            return tokens[index];
        }
    }

    private Token Peek(int ahead = 1)
    {
        _ = Current;
        var i = index;
        var seen = 0;
        while (true)
        {
            i++;
            if (i >= tokens.Count)
            {
                return tokens[^1];
            }

            if (suppressNewlines > 0 && tokens[i].Kind == TokenKind.Newline)
            {
                continue;
            }

            seen++;
            if (seen == ahead)
            {
                return tokens[i];
            }
        }
    }

    /// <summary>The first token after the line breaks in front of the cursor, when line breaks count.</summary>
    private Token PeekPastNewlines()
    {
        var i = index;
        while (tokens[i].Kind == TokenKind.Newline)
        {
            i++;
        }

        return tokens[i];
    }

    /// <summary><c>else =></c> after the line breaks is the default arm of an enclosing <c>match</c> (C9), not an <c>else</c> clause.</summary>
    private bool ElseStartsAnArm()
    {
        var i = index;
        while (tokens[i].Kind == TokenKind.Newline)
        {
            i++;
        }

        return i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Arrow;
    }

    private bool At(TokenKind kind) => Current.Kind == kind;

    private bool AtKeyword(string word) => Current.IsKeyword(word);

    private Token Advance()
    {
        var token = Current;
        if (token.Kind != TokenKind.EndOfFile)
        {
            index++;
        }

        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (At(kind))
        {
            Advance();
            return true;
        }

        return false;
    }

    private bool MatchKeyword(string word)
    {
        if (AtKeyword(word))
        {
            Advance();
            return true;
        }

        return false;
    }

    private void SkipNewlines()
    {
        while (tokens[index].Kind == TokenKind.Newline)
        {
            index++;
        }
    }

    /// <summary>The line an error at the cursor names: the cursor's token, or the token before a line break or the end.</summary>
    private int ErrorLine()
    {
        _ = Current;
        var i = index;
        while (i > 0 && tokens[i].Kind is TokenKind.Newline or TokenKind.EndOfFile)
        {
            i--; // past the line breaks and the end: an empty line is never where the error is
        }

        return tokens[i].Line;
    }

    private ParseError Fail(string expected) => new(ErrorLine(), $"expected {expected}, found {Current}");

    private Token Expect(TokenKind kind, string expected)
    {
        if (!At(kind))
        {
            throw Fail(expected);
        }

        return Advance();
    }

    /// <summary>A closing <c>&gt;</c> of type arguments; a <c>&gt;&gt;</c> closes two lists, so its first half is taken and the second left.</summary>
    private void ExpectGreater()
    {
        if (At(TokenKind.Greater))
        {
            Advance();
            return;
        }

        if (At(TokenKind.GreaterGreater))
        {
            var token = Current;
            if (speculating > 0)
            {
                tokenEdits.Add((index, token)); // a rewound speculation puts the `>>` back
            }

            tokens[index] = new Token(TokenKind.Greater, ">", token.Line, token.Offset + 1);
            return;
        }

        throw Fail("`>`");
    }

    /// <summary>A name where one is required. A core word there is <c>reserved-word</c> (L12), and the word stands as the name.</summary>
    private Token ExpectName(string what)
    {
        if (At(TokenKind.Name))
        {
            return Advance();
        }

        if (At(TokenKind.Keyword))
        {
            var word = Advance();
            Report(ErrorIds.ReservedWord, word.Line, $"`{word.Text}` is a core word and cannot be {what}");
            return word with { Kind = TokenKind.Name };
        }

        throw Fail(what);
    }

    private void Report(string id, int line, string message) => Keep(Diagnostic.Error(id, line, message));

    /// <summary>
    /// A diagnostic reaches the bag at once, or, while a speculative parse runs, when that parse
    /// succeeds (<see cref="Try"/>): a declaration whose type is a user type is parsed speculatively,
    /// and an error inside it is an error all the same.
    /// </summary>
    private void Keep(Diagnostic diagnostic)
    {
        if (speculating == 0)
        {
            diagnostics.Add(diagnostic);
        }
        else
        {
            pending.Add(diagnostic);
        }
    }

    private void ReportSyntax(int line, string message)
    {
        if (!badLines.Contains(line))
        {
            Report(ErrorIds.Syntax, line, message);
        }
    }

    /// <summary>
    /// Runs a parse that may fail: false rewinds to where it started and drops what the parse
    /// reported; true keeps the reports, which reach the bag once no speculation is left. A parse
    /// that fails after it committed (a declaration whose shape was seen) is not rewound: its
    /// error is the statement's error, reported where it stands, and its reports are kept.
    /// </summary>
    private bool Try(Action parse)
    {
        var saved = index;
        var savedSuppress = suppressNewlines;
        var savedBraces = openBraces;
        var savedPending = pending.Count;
        var savedBad = new HashSet<int>(badLines);
        var savedEdits = tokenEdits.Count;
        var savedCommitted = committed;
        committed = false;
        var ok = false;
        speculating++;
        try
        {
            parse();
            ok = true;
        }
        catch (ParseError) when (!committed)
        {
            index = saved;
            suppressNewlines = savedSuppress;
            openBraces = savedBraces;
            pending.RemoveRange(savedPending, pending.Count - savedPending);
            badLines.Clear();
            badLines.UnionWith(savedBad);
            for (var i = tokenEdits.Count - 1; i >= savedEdits; i--)
            {
                tokens[tokenEdits[i].Index] = tokenEdits[i].Original;
            }

            tokenEdits.RemoveRange(savedEdits, tokenEdits.Count - savedEdits);
        }
        finally
        {
            speculating--;
            committed = savedCommitted;
        }

        if (ok)
        {
            FlushPending();
        }

        return ok;
    }

    /// <summary>What was reported during speculation reaches the bag once no speculation is left.</summary>
    private void FlushPending()
    {
        if (speculating != 0)
        {
            return;
        }

        foreach (var diagnostic in pending)
        {
            diagnostics.Add(diagnostic);
        }

        pending.Clear();
        tokenEdits.Clear(); // what the kept parse rewrote stays rewritten
    }

    /// <summary>
    /// After a statement's parse failed: report it, skip the rest of its line (and a block the line
    /// opened), and make sure the cursor moved, so that a token no statement can start with is
    /// passed over and the loop around it ends. The line-break suppression of the construct that
    /// failed is dropped with it.
    /// </summary>
    private void FailStatement(ParseError error, int startIndex, int suppression)
    {
        var open = suppressNewlines - suppression; // the parentheses, brackets and braces the failing construct left open
        var braces = openBraces;
        suppressNewlines = suppression;
        openBraces = 0;
        FlushPending(); // what a committed declaration reported before it failed
        ReportSyntax(error.Line, error.Message);
        Recover(open, braces);
        if (index == startIndex && !At(TokenKind.EndOfFile))
        {
            index++;
        }
    }

    // ---- statements ----------------------------------------------------------------------------

    private List<Statement> ParseStatementsUntil(TokenKind closer, bool topLevel)
    {
        var saved = suppressNewlines;
        var savedBraces = openBraces;
        suppressNewlines = 0;
        openBraces = 0;
        var statements = new List<Statement>();
        while (true)
        {
            SkipNewlines();
            if (At(closer) || At(TokenKind.EndOfFile))
            {
                break;
            }

            if (At(TokenKind.Semicolon))
            {
                // L5: a `;` where a statement starts; a run of them is one error
                SkipSemicolons();
                continue;
            }

            var startIndex = index;
            try
            {
                var statement = ParseStatement(topLevel);
                statements.Add(statement);
                EndStatement();
            }
            catch (ParseError e)
            {
                FailStatement(e, startIndex, 0);
            }
        }

        suppressNewlines = saved;
        openBraces = savedBraces;
        return statements;
    }

    /// <summary>After a statement: a line break, the end, the closing brace of its block, or a <c>;</c> (L5).</summary>
    private void EndStatement()
    {
        if (At(TokenKind.Semicolon))
        {
            SkipSemicolons();
            return;
        }

        if (At(TokenKind.Newline) || At(TokenKind.EndOfFile) || At(TokenKind.RightBrace))
        {
            return;
        }

        throw new ParseError(Current.Line, $"a statement ends where its line ends; found {Current}");
    }

    /// <summary>One <c>semicolon</c> error for a <c>;</c> and every <c>;</c> right after it (L5).</summary>
    private void SkipSemicolons()
    {
        Report(ErrorIds.Semicolon, Current.Line, "there is no `;`: a statement ends where its line ends");
        while (At(TokenKind.Semicolon))
        {
            Advance();
        }
    }

    /// <summary>
    /// After an error: skips to the end of the statement, which is the end of its line once every
    /// parenthesis, bracket and brace the statement opened is closed again, and past a block the
    /// statement opened, with an `else` clause that follows it, so that the next statement parses on
    /// its own and the error stays one.
    /// </summary>
    private void Recover(int open, int braces)
    {
        var depth = 0;
        while (!At(TokenKind.EndOfFile))
        {
            var token = tokens[index];
            if (token.Kind is TokenKind.LeftParen or TokenKind.LeftBracket)
            {
                open++;
            }
            else if (token.Kind is TokenKind.RightParen or TokenKind.RightBracket)
            {
                open = Math.Max(0, open - 1);
            }
            else if (token.Kind == TokenKind.LeftBrace)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.RightBrace)
            {
                if (depth == 0)
                {
                    if (braces == 0)
                    {
                        return; // the closing brace of the block around the statement
                    }

                    // the closing brace of a construct the statement opened, `with { }` or an enum body
                    braces--;
                    open = Math.Max(0, open - 1);
                    index++;
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    index++;
                    if (open > 0 || AtKeyword("else"))
                    {
                        continue; // the block belongs to a construct that is still open, or an `else` clause follows it
                    }

                    return;
                }
            }
            else if (token.Kind == TokenKind.Newline && depth == 0 && open == 0)
            {
                return;
            }

            index++;
        }
    }

    private Statement ParseStatement(bool topLevel)
    {
        var token = Current;
        if (token.Kind == TokenKind.Keyword && Peek().Kind == TokenKind.Assign)
        {
            // L12, L19: a core word as the name of a variable, `if = 1` as much as `equal = 1`
            var word = ExpectName("a name");
            Advance(); // the `=`
            SkipNewlines();
            return new AssignOrDeclare(word.Line, word.Text, ParseExpression());
        }

        if (token.Kind == TokenKind.Keyword)
        {
            switch (token.Text)
            {
                case "if": return ParseIf();
                case "while": return ParseWhile();
                case "for": return ParseFor();
                case "match" when CanStartExpression(Peek().Kind): return ParseMatchStatement();
                // `match` with no value after it is a variable of that name (L12), an expression statement below
                case "break": Advance(); return new BreakStatement(token.Line);
                case "continue": Advance(); return new ContinueStatement(token.Line);
                case "return": Advance(); return new ReturnStatement(token.Line, EndsStatement() ? null : ParseExpression());
                case "throw": Advance(); return new ThrowStatement(token.Line, EndsStatement() ? null : ParseExpression());
                case "raw": Advance(); return new RawBlock(token.Line, ParseBlockAfter(token.Line, "the `raw` block"));
                case "use": return ParseUse();
                case "data": case "class": case "interface": case "enum": case "flags":
                    return ParseTypeDeclaration(Modifiers.None);
                case "else":
                    // C2: `else` follows the closing brace on its line; here it stands alone
                    ReportSyntax(token.Line, "`else` follows the closing brace of the `if` block on the same line");
                    Advance();
                    return ParseElseClause();
                case "pub": case "prot": case "static": case "mut": case "virtual": case "override": case "void": case "weak":
                    return ParseDeclaration(topLevel, typeName: null);
                case "true": case "false": case "null": case "this":
                    break; // an expression
                default:
                    if (CoreWords.TypeNames.Contains(token.Text))
                    {
                        return ParseDeclaration(topLevel, typeName: null);
                    }

                    break;
            }
        }

        if (token.Kind == TokenKind.Name)
        {
            if (Peek().Kind == TokenKind.Assign)
            {
                Advance();
                Advance();
                SkipNewlines();
                return new AssignOrDeclare(token.Line, token.Text, ParseExpression());
            }

            Statement? declaration = null;
            if (Try(() => declaration = ParseDeclaration(topLevel, typeName: null)))
            {
                return declaration!;
            }
        }

        if (token.Kind == TokenKind.LeftParen)
        {
            // a function whose return type is a function type: `(int) => int Make()`
            Statement? declaration = null;
            if (Try(() => declaration = ParseDeclaration(topLevel, typeName: null)))
            {
                return declaration!;
            }
        }

        var expression = ParseExpression();
        if (At(TokenKind.Assign))
        {
            var assign = Advance();
            if (expression is not (MemberAccess or IndexExpression))
            {
                throw new ParseError(assign.Line, "only a variable, a path or an index can be assigned");
            }

            SkipNewlines();
            return new Assignment(token.Line, expression, ParseExpression());
        }

        return new ExpressionStatement(token.Line, expression);
    }

    private bool EndsStatement() => At(TokenKind.Newline) || At(TokenKind.EndOfFile) || At(TokenKind.RightBrace) || At(TokenKind.Semicolon);

    private UseStatement ParseUse()
    {
        var use = Advance();
        var parts = new List<string> { ExpectName("a folder or package name").Text };
        while (Match(TokenKind.Dot))
        {
            parts.Add(ExpectName("a name").Text);
        }

        return new UseStatement(use.Line, string.Join('.', parts));
    }

    // ---- blocks and control flow ---------------------------------------------------------------

    /// <summary>A block that must open on <paramref name="line"/> (C1): a line break before the brace, or no brace, is <c>braces-required</c>, and the body is read all the same.</summary>
    private Block ParseBlockAfter(int line, string what)
    {
        if (At(TokenKind.LeftBrace))
        {
            return ParseBlock();
        }

        if (At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace)
        {
            Report(ErrorIds.BracesRequired, line, $"the block of {what} opens on the line of its condition");
            SkipNewlines();
            return ParseBlock();
        }

        Report(ErrorIds.BracesRequired, line, $"{what} takes a block between braces");
        if (EndsStatement())
        {
            return new Block(line, [], line);
        }

        var statement = ParseStatement(topLevel: false);
        return new Block(line, [statement], statement.Line);
    }

    /// <summary>
    /// The <c>{</c> of a body that opens on the line of its construct: one on the next line is <c>braces-required</c> at the
    /// construct's line, and the body still parses (the reading of C1 the README names).
    /// </summary>
    private void ExpectOpeningBrace(int line, string what)
    {
        if (BraceOnNextLine())
        {
            Report(ErrorIds.BracesRequired, line, $"{what} opens on its own line");
            SkipNewlines();
        }

        Expect(TokenKind.LeftBrace, "`{`");
    }

    private bool BraceOnNextLine() => At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace;

    private Block ParseBlock()
    {
        var open = Expect(TokenKind.LeftBrace, "`{`");
        var statements = ParseStatementsUntil(TokenKind.RightBrace, topLevel: false);
        var saved = suppressNewlines;
        suppressNewlines = 0;
        var close = Expect(TokenKind.RightBrace, "`}`");
        suppressNewlines = saved;
        return new Block(open.Line, statements, close.Line);
    }

    private IfStatement ParseIf()
    {
        var keyword = Advance();
        var condition = ParseExpression();
        // C1, E1: a missing block is reported on the line where the condition ends, where the block should have opened
        var then = ParseBlockAfter(tokens[index - 1].Line, "`if`");
        Statement? elseClause = null;
        if (AtKeyword("else"))
        {
            Advance();
            elseClause = ParseElseClause();
        }
        else if (At(TokenKind.Newline) && PeekPastNewlines().IsKeyword("else") && !ElseStartsAnArm())
        {
            SkipNewlines();
            var token = Advance();
            ReportSyntax(token.Line, "`else` follows the closing brace of the `if` block on the same line");
            elseClause = ParseElseClause();
        }

        return new IfStatement(keyword.Line, condition, then, elseClause);
    }

    private Statement ParseElseClause()
    {
        if (AtKeyword("if"))
        {
            return ParseIf();
        }

        return ParseBlockAfter(tokens[index - 1].Line, "`else`");
    }

    private WhileStatement ParseWhile()
    {
        var keyword = Advance();
        var condition = ParseExpression();
        return new WhileStatement(keyword.Line, condition, ParseBlockAfter(tokens[index - 1].Line, "`while`"));
    }

    private ForInStatement ParseFor()
    {
        var keyword = Advance();
        if (At(TokenKind.LeftParen))
        {
            throw new ParseError(keyword.Line, "a loop is `for name in collection { }`; the three-part `for` does not exist (C8)");
        }

        var variable = ExpectName("the loop variable");
        if (!MatchKeyword("in"))
        {
            throw Fail("`in`");
        }

        var collection = ParseExpression();
        return new ForInStatement(keyword.Line, variable.Text, variable.Line, collection, ParseBlockAfter(tokens[index - 1].Line, "`for`"));
    }

    private MatchStatement ParseMatchStatement()
    {
        var keyword = Advance();
        var subject = ParseExpression();
        return new MatchStatement(keyword.Line, subject, ParseArms(keyword.Line, "`match`"));
    }

    /// <summary>The arms of a <c>match</c> or of a postfix <c>else</c>, one per line between braces.</summary>
    private List<MatchArm> ParseArms(int line, string what)
    {
        if (!At(TokenKind.LeftBrace))
        {
            if (At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace)
            {
                Report(ErrorIds.BracesRequired, line, $"the arms of {what} open on its line");
                SkipNewlines();
            }
            else
            {
                throw Fail($"the arms of {what} between braces");
            }
        }

        var saved = suppressNewlines;
        suppressNewlines = 0;
        Advance(); // `{`
        var arms = new List<MatchArm>();
        var sawElse = false;
        while (true)
        {
            SkipNewlines();
            if (At(TokenKind.RightBrace) || At(TokenKind.EndOfFile))
            {
                break;
            }

            var armLine = Current.Line;
            var armIndex = index;
            try
            {
                var pattern = ParsePattern();
                Expect(TokenKind.Arrow, "`=>`");
                SkipNewlines();
                var body = At(TokenKind.LeftBrace) ? ParseBlock() : ParseStatement(topLevel: false);
                if (sawElse)
                {
                    ReportSyntax(armLine, "no arm follows `else`, which matches whatever the arms before it did not (C9)");
                }

                sawElse |= pattern is ElsePattern;
                arms.Add(new MatchArm(armLine, pattern, body));
                EndStatement();
            }
            catch (ParseError e)
            {
                FailStatement(e, armIndex, 0);
            }
        }

        Expect(TokenKind.RightBrace, "`}`");
        suppressNewlines = saved;
        return arms;
    }

    private Pattern ParsePattern()
    {
        var token = Current;
        if (token.IsKeyword("else"))
        {
            Advance();
            return new ElsePattern(token.Line);
        }

        if (token.Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral
            or TokenKind.UnitLiteral or TokenKind.Minus || token.IsKeyword("true") || token.IsKeyword("false") || token.IsKeyword("null"))
        {
            return new ValuePattern(token.Line, ParseUnary());
        }

        if ((token.Kind == TokenKind.Name || token.Kind == TokenKind.Keyword) && Peek().Kind == TokenKind.Dot)
        {
            return new ValuePattern(token.Line, ParsePostfix());
        }

        var type = ParseType();
        string? binding = null;
        if (At(TokenKind.Name))
        {
            binding = Advance().Text;
        }
        else if (At(TokenKind.Keyword) && !AtKeyword("else") && Peek().Kind == TokenKind.Arrow)
        {
            binding = ExpectName("the name of the value").Text;
        }

        return new TypePattern(token.Line, type, binding);
    }

    // ---- declarations --------------------------------------------------------------------------

    private Modifiers ParseModifiers()
    {
        var modifiers = Modifiers.None;
        while (At(TokenKind.Keyword))
        {
            var flag = Current.Text switch
            {
                "pub" => Modifiers.Pub,
                "prot" => Modifiers.Prot,
                "static" => Modifiers.Static,
                "mut" => Modifiers.Mut,
                "virtual" => Modifiers.Virtual,
                "override" => Modifiers.Override,
                _ => Modifiers.None,
            };
            if (flag == Modifiers.None)
            {
                break;
            }

            if ((modifiers & flag) != 0)
            {
                throw new ParseError(Current.Line, $"`{Current.Text}` is written twice");
            }

            modifiers |= flag;
            Advance();
        }

        return modifiers;
    }

    /// <summary>
    /// A declaration that starts with modifiers or a type: a variable (V2 to V4), a function (F1), or
    /// in a type body a field, a method or a constructor (K7, K14). <paramref name="typeName"/> is the
    /// name of the type whose body is parsed, or null outside one.
    /// </summary>
    private Statement ParseDeclaration(bool topLevel, string? typeName)
    {
        var start = Current;
        var modifiers = ParseModifiers();
        if (At(TokenKind.Keyword) && Current.Text is "data" or "class" or "interface" or "enum" or "flags")
        {
            return ParseTypeDeclaration(modifiers);
        }

        // `mut x = 1`: a declaration without a type
        if (modifiers == Modifiers.Mut && (At(TokenKind.Name) || At(TokenKind.Keyword)) && Peek().Kind == TokenKind.Assign)
        {
            var name = ExpectName("the name of the variable");
            Advance(); // `=`
            SkipNewlines();
            return new VariableDeclaration(start.Line, true, null, name.Text, ParseExpression());
        }

        // a constructor: the type's own name before `(`
        if (typeName is not null && At(TokenKind.Name) && Current.Text == typeName && Peek().Kind == TokenKind.LeftParen)
        {
            var name = Advance();
            var parameters = ParseParameters(typesOptional: false);
            IReadOnlyList<Argument>? thisArguments = null;
            if (Match(TokenKind.Colon))
            {
                if (!MatchKeyword("this"))
                {
                    throw Fail("`this`");
                }

                thisArguments = ParseArguments();
            }

            var body = ParseBlockAfter(name.Line, "the constructor");
            return new ConstructorDeclaration(start.Line, modifiers, name.Text, parameters, thisArguments, body);
        }

        var type = ParseType();
        var declared = ExpectName("a name after the type");
        if (At(TokenKind.Assign))
        {
            committed = true; // `Type name =`: a declaration, whatever follows
            Advance();
            SkipNewlines();
            var value = ParseExpression();
            return typeName is null
                ? new VariableDeclaration(start.Line, (modifiers & Modifiers.Mut) != 0, type, declared.Text, value)
                : new FieldDeclaration(start.Line, modifiers, type, declared.Text, value);
        }

        if (At(TokenKind.LeftParen) || At(TokenKind.Less))
        {
            committed = true; // `Type name(`: a function, whatever follows
            return ParseFunctionRest(start.Line, modifiers, type, declared, typeName);
        }

        if (typeName is not null && EndsStatement())
        {
            // K7: `int x` in a class body, a field a constructor assigns
            return new FieldDeclaration(start.Line, modifiers, type, declared.Text, null);
        }

        throw Fail(topLevel ? "`=` with the variable's value, or the parameters of a function" : "`=` with the variable's value");
    }

    private FunctionDeclaration ParseFunctionRest(int line, Modifiers modifiers, TypeSyntax returnType, Token name, string? typeName)
    {
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(typesOptional: false);
        if (Match(TokenKind.Arrow))
        {
            SkipNewlines();
            return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, ParseExpression(), null);
        }

        if (At(TokenKind.LeftBrace))
        {
            return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, null, ParseBlock());
        }

        if (typeName is not null && EndsStatement())
        {
            // the signature of an interface method (K7)
            return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, null, null);
        }

        if (At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace)
        {
            Report(ErrorIds.BracesRequired, line, "the body of a function opens on the line of its signature");
            SkipNewlines();
            return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, null, ParseBlock());
        }

        throw Fail("`=>` with the function's result or its body between braces");
    }

    private List<TypeParameter> ParseTypeParameters()
    {
        var result = new List<TypeParameter>();
        if (!At(TokenKind.Less))
        {
            return result;
        }

        Advance();
        suppressNewlines++;
        do
        {
            var name = ExpectName("a type parameter");
            TypeSyntax? limit = null;
            if (Match(TokenKind.Colon))
            {
                limit = ParseType();
            }

            result.Add(new TypeParameter(name.Line, name.Text, limit));
        }
        while (Match(TokenKind.Comma));
        suppressNewlines--;
        ExpectGreater();
        return result;
    }

    private List<Parameter> ParseParameters(bool typesOptional)
    {
        Expect(TokenKind.LeftParen, "`(`");
        suppressNewlines++;
        var parameters = new List<Parameter>();
        if (!At(TokenKind.RightParen))
        {
            do
            {
                parameters.Add(ParseParameter(typesOptional));
            }
            while (Match(TokenKind.Comma));
        }

        suppressNewlines--;
        Expect(TokenKind.RightParen, "`)`");
        return parameters;
    }

    private Parameter ParseParameter(bool typesOptional)
    {
        var start = Current;
        var mut = MatchKeyword("mut");
        TypeSyntax? type = null;
        if (typesOptional && At(TokenKind.Name) && Peek().Kind is TokenKind.Comma or TokenKind.RightParen)
        {
            var only = Advance();
            return new Parameter(start.Line, mut, null, only.Text, null);
        }

        type = ParseType();
        var name = ExpectName("the name of the parameter");
        Expression? defaultValue = null;
        if (Match(TokenKind.Assign))
        {
            defaultValue = ParseExpression();
        }

        return new Parameter(start.Line, mut, type, name.Text, defaultValue);
    }

    private TypeDeclaration ParseTypeDeclaration(Modifiers modifiers)
    {
        _ = modifiers;
        var keyword = Advance();
        var kind = keyword.Text switch
        {
            "data" => TypeDeclarationKind.Data,
            "class" => TypeDeclarationKind.Class,
            "interface" => TypeDeclarationKind.Interface,
            "enum" => TypeDeclarationKind.Enum,
            _ => TypeDeclarationKind.Flags,
        };
        var name = ExpectName("the name of the type");
        var typeParameters = ParseTypeParameters();
        IReadOnlyList<Parameter>? primary = null;
        var bases = new List<BaseSpecifier>();
        var equalBy = new List<string>();
        var members = new List<Statement>();
        var values = new List<EnumMember>();
        if (kind is TypeDeclarationKind.Data or TypeDeclarationKind.Class)
        {
            if (At(TokenKind.LeftParen))
            {
                primary = ParseParameters(typesOptional: false);
            }

            if (Match(TokenKind.Colon))
            {
                do
                {
                    SkipNewlines();
                    var baseType = ParseType();
                    IReadOnlyList<Argument>? arguments = At(TokenKind.LeftParen) ? ParseArguments() : null;
                    bases.Add(new BaseSpecifier(baseType.Line, baseType, arguments));
                }
                while (Match(TokenKind.Comma));
            }

            if (MatchKeyword("equal"))
            {
                if (!MatchKeyword("by"))
                {
                    throw Fail("`by`");
                }

                while (true)
                {
                    equalBy.Add(ExpectName("a field").Text);
                    if (!Match(TokenKind.Comma))
                    {
                        break;
                    }

                    SkipNewlines(); // L4: a line that ends in a comma continues
                }
            }

            if (At(TokenKind.LeftBrace) || BraceOnNextLine())
            {
                members = ParseTypeBody(name.Text, name.Line);
            }
        }
        else if (kind == TypeDeclarationKind.Interface)
        {
            members = ParseTypeBody(name.Text, name.Line);
        }
        else
        {
            ExpectOpeningBrace(name.Line, $"the body of `{name.Text}`");
            suppressNewlines++;
            openBraces++;
            if (!At(TokenKind.RightBrace))
            {
                do
                {
                    var member = ExpectName("a value of the type");
                    Expression? number = null;
                    if (Match(TokenKind.Assign))
                    {
                        number = ParseExpression();
                    }

                    values.Add(new EnumMember(member.Line, member.Text, number));
                }
                while (Match(TokenKind.Comma) && !At(TokenKind.RightBrace));
            }

            suppressNewlines--;
            SkipNewlines();
            Expect(TokenKind.RightBrace, "`}`");
            openBraces--; // after the `}`: an error at the `}` leaves the brace for recovery to close
        }

        return new TypeDeclaration(keyword.Line, kind, name.Text, typeParameters, primary, bases, equalBy, members, values);
    }

    private List<Statement> ParseTypeBody(string typeName, int line)
    {
        ExpectOpeningBrace(line, $"the body of `{typeName}`");
        var saved = suppressNewlines;
        suppressNewlines = 0;
        var members = new List<Statement>();
        while (true)
        {
            SkipNewlines();
            if (At(TokenKind.RightBrace) || At(TokenKind.EndOfFile))
            {
                break;
            }

            var startIndex = index;
            try
            {
                members.Add(ParseDeclaration(topLevel: false, typeName));
                EndStatement();
            }
            catch (ParseError e)
            {
                FailStatement(e, startIndex, 0);
            }
        }

        Expect(TokenKind.RightBrace, "`}`");
        suppressNewlines = saved;
        return members;
    }

    // ---- types ---------------------------------------------------------------------------------

    private TypeSyntax ParseType()
    {
        var first = ParseNonUnionType();
        if (!At(TokenKind.Bar))
        {
            return first;
        }

        var cases = new List<TypeSyntax> { first };
        while (Match(TokenKind.Bar))
        {
            SkipNewlines();
            cases.Add(ParseNonUnionType());
        }

        return new UnionType(first.Line, cases);
    }

    private TypeSyntax ParseNonUnionType()
    {
        if (AtKeyword("weak"))
        {
            var weak = Advance();
            return new WeakType(weak.Line, ParseNonUnionType());
        }

        var type = ParseCoreType();
        while (At(TokenKind.Question))
        {
            var question = Advance();
            type = new NullableType(question.Line, type);
        }

        return type;
    }

    private TypeSyntax ParseCoreType()
    {
        var token = Current;
        if (token.Kind == TokenKind.LeftParen)
        {
            Advance();
            suppressNewlines++;
            var parameters = new List<TypeSyntax>();
            if (!At(TokenKind.RightParen))
            {
                do
                {
                    parameters.Add(ParseType());
                }
                while (Match(TokenKind.Comma));
            }

            Expect(TokenKind.RightParen, "`)`");
            suppressNewlines--;
            if (Match(TokenKind.Arrow))
            {
                SkipNewlines(); // L4: a trailing `=>` continues, a function type's as much as a lambda's
                return new FunctionType(token.Line, parameters, ParseNonUnionType());
            }

            if (parameters.Count == 1)
            {
                return parameters[0];
            }

            throw new ParseError(token.Line, "a type in parentheses is one type, or the parameters of a function type before `=>`");
        }

        if (token.IsKeyword("void"))
        {
            Advance();
            return new VoidType(token.Line);
        }

        if (token.Kind == TokenKind.Name || (token.Kind == TokenKind.Keyword && CoreWords.TypeNames.Contains(token.Text)))
        {
            Advance();
            var arguments = new List<TypeSyntax>();
            if (At(TokenKind.Less))
            {
                Advance();
                suppressNewlines++;
                do
                {
                    arguments.Add(ParseType());
                }
                while (Match(TokenKind.Comma));
                suppressNewlines--;
                ExpectGreater();
            }

            return new NamedType(token.Line, token.Text, arguments);
        }

        throw Fail("a type");
    }

    // ---- expressions ---------------------------------------------------------------------------

    private Expression ParseExpression() => ParseCoalesce();

    private Expression ParseCoalesce()
    {
        var left = ParseBinary(0);
        if (At(TokenKind.QuestionQuestion))
        {
            var op = Advance();
            SkipNewlines();
            var right = ParseCoalesce();
            return new BinaryExpression(op.Line, op.Kind, left, right);
        }

        return left;
    }

    private static readonly TokenKind[][] BinaryLevels =
    [
        [TokenKind.BarBar],
        [TokenKind.AmpersandAmpersand],
        [TokenKind.Bar],
        [TokenKind.Caret],
        [TokenKind.Ampersand],
        [TokenKind.EqualEqual, TokenKind.NotEqual],
        [TokenKind.Less, TokenKind.LessEqual, TokenKind.Greater, TokenKind.GreaterEqual],
        [TokenKind.DotDot, TokenKind.DotDotLess],
        [TokenKind.LessLess, TokenKind.GreaterGreater],
        [TokenKind.Plus, TokenKind.Minus, TokenKind.PlusPercent, TokenKind.MinusPercent],
        [TokenKind.Star, TokenKind.Slash, TokenKind.Percent, TokenKind.StarPercent],
    ];

    private Expression ParseBinary(int level)
    {
        if (level == BinaryLevels.Length)
        {
            return ParseUnary();
        }

        var left = ParseBinary(level + 1);
        while (Array.IndexOf(BinaryLevels[level], Current.Kind) >= 0)
        {
            var op = Advance();
            SkipNewlines();
            var right = ParseBinary(level + 1);
            left = op.Kind is TokenKind.DotDot or TokenKind.DotDotLess
                ? new RangeExpression(op.Line, left, right, op.Kind == TokenKind.DotDot)
                : new BinaryExpression(op.Line, op.Kind, left, right);
            if (op.Kind is TokenKind.DotDot or TokenKind.DotDotLess)
            {
                break; // a range has two ends
            }
        }

        return left;
    }

    private static bool CanStartExpression(TokenKind kind) => kind is TokenKind.Name or TokenKind.Keyword
        or TokenKind.IntegerLiteral or TokenKind.RealLiteral or TokenKind.UnitLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral
        or TokenKind.LeftParen or TokenKind.Minus or TokenKind.Bang or TokenKind.Tilde; // no expression starts with `[`

    private Expression ParseUnary()
    {
        if (At(TokenKind.Minus) || At(TokenKind.Bang) || At(TokenKind.Tilde))
        {
            var op = Advance();
            return new UnaryExpression(op.Line, op.Kind, ParseUnary());
        }

        return ParsePostfix();
    }

    private Expression ParsePostfix()
    {
        var expression = ParsePrimary();
        while (true)
        {
            if (At(TokenKind.Newline) && PeekPastNewlines().Kind is TokenKind.Dot or TokenKind.QuestionDot)
            {
                SkipNewlines(); // L4: a chain broken before the dot
            }

            if (At(TokenKind.Dot) || At(TokenKind.QuestionDot))
            {
                var dot = Advance();
                var member = At(TokenKind.Keyword) && Peek().Kind != TokenKind.LeftParen && !CoreWords.TypeNames.Contains(Current.Text)
                    ? Advance()
                    : ExpectName("a member name");
                expression = new MemberAccess(dot.Line, expression, member.Text, dot.Kind == TokenKind.QuestionDot);
                continue;
            }

            if (At(TokenKind.LeftParen))
            {
                expression = new CallExpression(expression.Line, expression, [], ParseArguments());
                continue;
            }

            if (At(TokenKind.Less) && expression is NameExpression)
            {
                List<TypeSyntax>? typeArguments = null;
                if (Try(() =>
                    {
                        Advance();
                        suppressNewlines++;
                        var list = new List<TypeSyntax>();
                        do
                        {
                            list.Add(ParseType());
                        }
                        while (Match(TokenKind.Comma));
                        suppressNewlines--;
                        ExpectGreater();
                        if (!At(TokenKind.LeftParen))
                        {
                            throw Fail("`(`");
                        }

                        typeArguments = list;
                    }))
                {
                    expression = new CallExpression(expression.Line, expression, typeArguments!, ParseArguments());
                    continue;
                }
            }

            if (At(TokenKind.LeftBracket))
            {
                var open = Advance();
                suppressNewlines++;
                var indexValue = ParseExpression();
                suppressNewlines--;
                Expect(TokenKind.RightBracket, "`]`");
                expression = new IndexExpression(open.Line, expression, indexValue);
                continue;
            }

            if (AtKeyword("with"))
            {
                var with = Advance();
                expression = new WithExpression(with.Line, expression, ParseFieldAssignments(with.Line));
                continue;
            }

            if (AtKeyword("else") && expression is CallExpression)
            {
                var elseKeyword = Advance();
                expression = new ElseExpression(elseKeyword.Line, expression, ParseArms(elseKeyword.Line, "`else`"));
                continue;
            }

            return expression;
        }
    }

    private List<FieldAssignment> ParseFieldAssignments(int line)
    {
        ExpectOpeningBrace(line, "the `{` of `with`");
        suppressNewlines++;
        openBraces++;
        var assignments = new List<FieldAssignment>();
        if (!At(TokenKind.RightBrace))
        {
            do
            {
                var field = ExpectName("a field");
                Expect(TokenKind.Assign, "`=`");
                assignments.Add(new FieldAssignment(field.Line, field.Text, ParseExpression()));
            }
            while (Match(TokenKind.Comma));
        }

        suppressNewlines--;
        Expect(TokenKind.RightBrace, "`}`");
        openBraces--; // after the `}`, as in an enum body
        return assignments;
    }

    private List<Argument> ParseArguments()
    {
        Expect(TokenKind.LeftParen, "`(`");
        suppressNewlines++;
        var arguments = new List<Argument>();
        if (!At(TokenKind.RightParen))
        {
            do
            {
                var start = Current;
                string? name = null;
                if (At(TokenKind.Name) && Peek().Kind == TokenKind.Colon)
                {
                    name = Advance().Text;
                    Advance();
                }

                var mut = MatchKeyword("mut");
                arguments.Add(new Argument(start.Line, name, mut, ParseExpression()));
            }
            while (Match(TokenKind.Comma));
        }

        suppressNewlines--;
        Expect(TokenKind.RightParen, "`)`");
        return arguments;
    }

    private Expression ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.IntegerLiteral:
                Advance();
                return new IntegerLiteral(token.Line, (IntegerLiteralValue)token.Value!);
            case TokenKind.RealLiteral:
                Advance();
                return new RealLiteral(token.Line, (RealLiteralValue)token.Value!);
            case TokenKind.UnitLiteral:
                Advance();
                return new UnitLiteral(token.Line, (UnitLiteralValue)token.Value!);
            case TokenKind.CharLiteral:
                Advance();
                return new CharLiteral(token.Line, (int)token.Value!);
            case TokenKind.StringLiteral:
                Advance();
                return ParseStringLiteral(token);
            case TokenKind.Name:
                Advance();
                if (At(TokenKind.Arrow))
                {
                    Advance();
                    return ParseLambdaBody(token.Line, [new Parameter(token.Line, false, null, token.Text, null)]);
                }

                return new NameExpression(token.Line, token.Text);
            case TokenKind.Keyword:
                switch (token.Text)
                {
                    case "true": Advance(); return new BoolLiteral(token.Line, true);
                    case "false": Advance(); return new BoolLiteral(token.Line, false);
                    case "null": Advance(); return new NullLiteral(token.Line);
                    case "this": Advance(); return new ThisExpression(token.Line);
                    case "match" when !CanStartExpression(Peek().Kind):
                        // no value follows: a variable or parameter named `match`, declared with `reserved-word` (L12)
                        Advance();
                        return new NameExpression(token.Line, token.Text);
                    case "match":
                        Advance();
                        var subject = ParseExpression();
                        return new MatchExpression(token.Line, subject, ParseArms(token.Line, "`match`"));
                    default:
                        if (token.Text is "if" or "while" or "for" or "else" or "return" or "throw" or "break" or "continue"
                            or "data" or "class" or "interface" or "enum" or "flags" or "raw" or "use" or "with" or "in")
                        {
                            throw new ParseError(token.Line, $"`{token.Text}` is a core word and cannot stand here");
                        }

                        // a type's name used as a conversion (T10), or a core word where a name was declared with
                        // `reserved-word` (L12): the name stands, so that the one error stays the declaration's
                        Advance();
                        return new NameExpression(token.Line, token.Text);
                }

            case TokenKind.LeftParen:
            {
                List<Parameter>? parameters = null;
                if (Try(() =>
                    {
                        parameters = ParseParameters(typesOptional: true);
                        Expect(TokenKind.Arrow, "`=>`");
                    }))
                {
                    return ParseLambdaBody(token.Line, parameters!);
                }

                Advance();
                suppressNewlines++;
                var inner = ParseExpression();
                suppressNewlines--;
                Expect(TokenKind.RightParen, "`)`");
                return new ParenthesizedExpression(token.Line, inner);
            }

            case TokenKind.Bad:
                Advance();
                throw new ParseError(token.Line, "text no rule gives a meaning");
            default:
                throw Fail("an expression");
        }
    }

    private LambdaExpression ParseLambdaBody(int line, List<Parameter> parameters)
    {
        SkipNewlines();
        if (At(TokenKind.LeftBrace))
        {
            return new LambdaExpression(line, parameters, null, ParseBlock());
        }

        return new LambdaExpression(line, parameters, ParseExpression(), null);
    }

    /// <summary>The segments of a string literal; each hole is parsed as an expression of its own (L6).</summary>
    private StringLiteral ParseStringLiteral(Token token)
    {
        var value = (StringLiteralValue)token.Value!;
        var segments = new List<StringSegment>();
        foreach (var part in value.Parts)
        {
            if (part is TextPart text)
            {
                segments.Add(new TextSegment(text.Text));
                continue;
            }

            var hole = (HolePart)part;
            // the hole's lexer reports into a bag of its own, and its errors go the way of every report: a
            // speculative parse that is rewound lexes the hole again, so they wait in `pending` with the rest;
            // a line it flagged is a bad line of this parser too, so that the inner parse failing on that
            // token adds no second `syntax`
            var holeBag = new DiagnosticBag();
            var holeTokens = new Lexer(source, holeBag, hole.Offset, hole.Offset + hole.Source.Length).Lex();
            foreach (var diagnostic in holeBag.Items)
            {
                Keep(diagnostic);
            }

            var inner = new Parser(source, holeTokens, diagnostics) { speculating = speculating, pending = pending };
            badLines.UnionWith(inner.badLines);
            inner.suppressNewlines = 1;
            var expression = inner.ParseExpression();
            if (!inner.At(TokenKind.EndOfFile))
            {
                throw new ParseError(hole.Line, $"an interpolation holds one expression; found {inner.Current}");
            }

            segments.Add(new HoleSegment(expression));
        }

        return new StringLiteral(token.Line, segments);
    }
}
