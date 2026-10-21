using Kurz.Compiler.Text;

namespace Kurz.Compiler.Syntax;

/// <summary>
/// Builds the syntax tree of one file from its tokens. A line break ends a statement (L2) except
/// inside an open <c>(</c>, <c>[</c>, the <c>&lt; &gt;</c> of type arguments or the braces of an
/// <c>enum</c>, <c>flags</c> or <c>with</c> body, after a binary operator, a comma, <c>=</c>,
/// <c>=&gt;</c> or the <c>:</c> of a base list, and before a line that starts with the <c>.</c> or
/// <c>?.</c> of a member access, where a line that starts with a fraction literal such as <c>.5</c>
/// starts a statement (L4); a block between braces holds statements of its own, each on its line,
/// also inside an open bracket. Text no rule gives a meaning is the one error <c>syntax</c> (E4),
/// reported on the line where the text stops making sense, and a case holds one error (E1): after
/// it the rest of the statement is skipped.
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
    private bool noLambda; // the value of an operand of `asm`, where `=>` splits the operand (R11) and starts no lambda
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
        var statements = parser.ParseStatementsUntil(TokenKind.EndOfFile);
        return new ProgramSyntax(statements);
    }

    private sealed class ParseError(int line, string message) : Exception(message)
    {
        public int Line { get; } = line;

        /// <summary>The error reached the bag under its own id already (C1's <c>braces-required</c>); the statement is given up without a <c>syntax</c>.</summary>
        public bool Reported { get; init; }
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

    /// <summary>A closing <c>&gt;</c> of type arguments; a <c>&gt;&gt;</c> closes two lists, so its first half is taken and the second left, as C# reads <c>List&lt;List&lt;int&gt;&gt;</c> (T17: generic types are written as in C#).</summary>
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
        if (!error.Reported)
        {
            ReportSyntax(error.Line, error.Message);
        }

        Recover(open, braces);
        if (index == startIndex && !At(TokenKind.EndOfFile))
        {
            index++;
        }
    }

    // ---- statements ----------------------------------------------------------------------------

    private List<Statement> ParseStatementsUntil(TokenKind closer)
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
                var statement = ParseStatement();
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

    private Statement ParseStatement()
    {
        var token = Current;
        if (token.Kind == TokenKind.Keyword && Peek().Kind == TokenKind.Assign)
        {
            // L12, L19: a core word as the name of a variable, `if = 1` as much as `equal = 1`. The parser does not know whether the line
            // declares or assigns (V8), so a second `equal = 2` gets the error again: an uneven shape the README lists
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
                case "break": Advance(); return new BreakStatement(token.Line); // C7
                case "continue": Advance(); return new ContinueStatement(token.Line); // C7
                case "return": Advance(); return new ReturnStatement(token.Line, EndsStatement() ? null : ParseExpression());
                case "throw":
                    // O7: `throw` takes a value, and stands bare in an arm of `else`; a bare one anywhere else is the checker's `throw-needs-value`
                    Advance();
                    return new ThrowStatement(token.Line, EndsStatement() ? null : ParseExpression());
                case "raw": Advance(); return new RawBlock(token.Line, ParseBlockAfter(token.Line, "the `raw` block"));
                case "extern" when Peek().Kind == TokenKind.StringLiteral
                    && (Peek(2).Kind is TokenKind.LeftBrace or TokenKind.Newline or TokenKind.EndOfFile || Peek(2).IsKeyword("in")):
                    return ParseExternBlock(); // R16: `extern "C" {` and `extern "C" in "lib" {`; a declaration does not end its line after `extern "C"`
                case "asm" when Peek().Kind == TokenKind.LeftParen:
                    return ParseAsm(); // R11: a statement of its own
                case "use": return ParseUse();
                case "data": case "class": case "interface": case "enum": case "flags":
                    return ParseTypeDeclaration(Modifiers.None);
                case "else":
                    // C2: `else` follows the closing brace on its line; one on a line of its own is `syntax` (E4 lists it; decided)
                    ReportSyntax(token.Line, "`else` follows the closing brace of the `if` block on the same line");
                    Advance();
                    return ParseStrayElseClause(token.Line);
                case "pub": case "prot": case "static": case "mut": case "virtual": case "override": case "void": case "weak": case "naked": case "extern":
                    if (TryDeclaration(out var modified))
                    {
                        return modified!;
                    }

                    break; // the reading on L12 (README): a core word read as a name where no declaration follows it, `mut.Add(1)` on a variable named `mut`
                case "true": case "false": case "null": case "this":
                    break; // an expression
                default:
                    if (CoreWords.TypeNames.Contains(token.Text) && TryDeclaration(out var typed))
                    {
                        return typed!;
                    }

                    break; // `int.Parse(s)`: the type's name as the target of a member access, read as a name (the reading on L12; T10 writes a conversion as a call of the type)
            }
        }

        if (token.Kind == TokenKind.Name)
        {
            if (Peek().Kind == TokenKind.Assign)
            {
                Advance();
                Advance();
                SkipNewlines();
                return new AssignOrDeclare(token.Line, token.Text, ParseExpression()); // V1, V8: a declaration or an assignment
            }

            if (TryDeclaration(out var named))
            {
                return named!; // a declaration whose type is a user type
            }
        }

        if (token.Kind == TokenKind.LeftParen && TryDeclaration(out var functionTyped))
        {
            // F1 with F11: a declaration whose type is a function type, `(int) => int Make()` or `(int) => int f = ...`, starts
            // with `(` as an expression does, so the declaration is tried first, speculatively like one with a user type
            return functionTyped!;
        }

        var expression = ParseExpression();
        if (At(TokenKind.Assign))
        {
            var assign = Advance();
            // V8: a name on the left declares or assigns (above); M4: a path; M9: an index, `map[key] = value`; R10: a write through a
            // pointer, `*p = v`; any other left side is text no rule gives a meaning (E4)
            if (expression is not (MemberAccess or IndexExpression or UnaryExpression { Operator: TokenKind.Star }))
            {
                throw new ParseError(assign.Line, "only a variable, a path, an index or what a pointer points to can be assigned");
            }

            SkipNewlines();
            return new Assignment(token.Line, expression, ParseExpression());
        }

        return new ExpressionStatement(token.Line, expression);
    }

    private bool EndsStatement() => At(TokenKind.Newline) || At(TokenKind.EndOfFile) || At(TokenKind.RightBrace) || At(TokenKind.Semicolon);

    /// <summary>
    /// A declaration tried speculatively (<see cref="Try"/>), because its first token is also how an expression statement can
    /// start: a plain name (a user type), a modifier or a built-in type's name, which the reading on L12 lets stand as a name,
    /// or the `(` of a function type (F11). False, with the cursor back where it was, when it is no declaration.
    /// </summary>
    private bool TryDeclaration(out Statement? declaration)
    {
        Statement? parsed = null;
        var ok = Try(() => parsed = ParseDeclaration(typeName: null));
        declaration = parsed;
        return ok;
    }

    /// <summary><c>use</c> brings in another folder or a package (F10), named by a dotted path as the record's <c>use net.http</c> is; a core word in the path is <c>reserved-word</c> (L12).</summary>
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

    /// <summary>The string after <c>extern</c>: the C calling convention, <c>"C"</c>, the one R16 and R17 write; another is text no rule gives a meaning (E4).</summary>
    private void ExpectConvention()
    {
        committed = true; // `extern` and a string: a declaration, a block of them or a type, whatever follows
        var convention = Current;
        if (convention.Kind != TokenKind.StringLiteral || convention.Value is not StringLiteralValue { PlainText: "C" })
        {
            throw new ParseError(ErrorLine(), $"`extern` takes the convention \"C\" (R16, R17), found {convention}");
        }

        Advance();
    }

    /// <summary>
    /// R16: a block of the functions of a C library, <c>extern "C" { long write(int fd, byte* buf, long n) }</c>, with the library
    /// after <c>in</c> where one is named, <c>extern "C" in "dbghelp" { ... }</c>. Its body opens on the line of <c>extern</c> (C1)
    /// and holds a declaration with no body on each line; which modifiers, types and defaults a declaration may take is the checker's.
    /// </summary>
    private ExternBlock ParseExternBlock()
    {
        var keyword = Advance();
        ExpectConvention();
        string? library = null;
        if (MatchKeyword("in"))
        {
            if (Current.Kind != TokenKind.StringLiteral || Current.Value is not StringLiteralValue { PlainText: { } name })
            {
                throw Fail("the name of the library as a string, `in \"dbghelp\"`");
            }

            library = name;
            Advance();
        }

        ExpectOpeningBrace(keyword.Line, "the block of `extern`");
        var saved = suppressNewlines;
        suppressNewlines = 0;
        var functions = new List<ExternFunction>();
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
                functions.Add(ParseExternFunction());
                EndStatement();
            }
            catch (ParseError e)
            {
                FailStatement(e, startIndex, 0);
            }
        }

        Expect(TokenKind.RightBrace, "`}`");
        suppressNewlines = saved;
        return new ExternBlock(keyword.Line, library, functions);
    }

    /// <summary>A function of a block of R16: its signature, which ends its line, with no body, its last parameter <c>...</c> where it takes a variable count of arguments.</summary>
    private ExternFunction ParseExternFunction()
    {
        var start = Current;
        var modifiers = ParseModifiers();
        var returnType = ParseType();
        var name = ExpectName("the name of the C function");
        Expect(TokenKind.LeftParen, "`(`");
        suppressNewlines++;
        var parameters = new List<Parameter>();
        var variadic = false;
        if (!At(TokenKind.RightParen))
        {
            do
            {
                if (Match(TokenKind.Ellipsis))
                {
                    variadic = true; // R16: `...` is the last parameter, so what follows it is text no rule gives a meaning (E4)
                    break;
                }

                parameters.Add(ParseParameter(typesOptional: false));
            }
            while (Match(TokenKind.Comma));
        }

        suppressNewlines--;
        Expect(TokenKind.RightParen, "`)`");
        return new ExternFunction(start.Line, modifiers, returnType, name.Text, parameters, variadic);
    }

    /// <summary>
    /// R11: <c>asm("add {r}, 1", r = inout(reg) count)</c>, a statement of its own: its templates, string literals the lexer read
    /// with no hole, then its operands as Rust writes them (<see cref="ParseAsmOperand"/>).
    /// </summary>
    private AsmStatement ParseAsm()
    {
        var keyword = Advance();
        Expect(TokenKind.LeftParen, "`(`");
        suppressNewlines++;
        var templates = new List<AsmTemplate>();
        var operands = new List<AsmOperand>();
        do
        {
            if (Current.Kind == TokenKind.StringLiteral && Current.Value is StringLiteralValue { IsTemplate: true } template)
            {
                templates.Add(new AsmTemplate(Advance().Line, template.PlainText!));
            }
            else if (templates.Count == 0)
            {
                throw Fail("the template of the `asm`, a string literal");
            }
            else
            {
                operands.Add(ParseAsmOperand());
            }
        }
        while (Match(TokenKind.Comma));

        suppressNewlines--;
        Expect(TokenKind.RightParen, "`)`");
        return new AsmStatement(keyword.Line, templates, operands);
    }

    /// <summary>
    /// An operand of an <c>asm</c> in Rust's shape (R11): <c>name =</c> where it has a name, a word, then a list in parentheses
    /// (<c>in(reg)</c>, <c>out("eax")</c>, <c>clobber_abi("C")</c>, <c>options(nostack)</c>), then the value and a place after
    /// <c>=&gt;</c>, or a block (<c>label { ... }</c>). Which words, register classes and shapes are Rust's is the checker's
    /// (<c>asm-operand</c>), so a word the parser does not know parses as any other.
    /// </summary>
    private AsmOperand ParseAsmOperand()
    {
        var start = Current;
        string? name = null;
        if ((At(TokenKind.Name) || At(TokenKind.Keyword)) && Peek().Kind == TokenKind.Assign)
        {
            name = Advance().Text;
            Advance(); // `=`
        }

        if (!At(TokenKind.Name) && !At(TokenKind.Keyword))
        {
            throw Fail("an operand of the `asm`, `in(reg) x`, `const 1` or `options(nostack)`");
        }

        var word = Advance();
        if (At(TokenKind.LeftBrace))
        {
            if (Current.Line != word.Line)
            {
                Report(ErrorIds.BracesRequired, word.Line, $"the block of `{word.Text}` opens on its line, not on the next (C1)");
            }

            return new AsmOperand(start.Line, name, word.Text, null, null, null, ParseBlock());
        }

        List<AsmArgument>? arguments = null;
        if (Match(TokenKind.LeftParen))
        {
            arguments = [];
            if (!At(TokenKind.RightParen))
            {
                do
                {
                    var argument = Current;
                    if (argument.Kind == TokenKind.StringLiteral && argument.Value is StringLiteralValue { PlainText: { } text })
                    {
                        arguments.Add(new AsmArgument(Advance().Line, text, Quoted: true));
                    }
                    else if (argument.Kind is TokenKind.Name or TokenKind.Keyword)
                    {
                        arguments.Add(new AsmArgument(Advance().Line, argument.Text, Quoted: false));
                    }
                    else
                    {
                        throw Fail("a register class, a register, an ABI or an option");
                    }
                }
                while (Match(TokenKind.Comma));
            }

            Expect(TokenKind.RightParen, "`)`");
            if (At(TokenKind.Comma) || At(TokenKind.RightParen))
            {
                return new AsmOperand(start.Line, name, word.Text, arguments, null, null, null); // `clobber_abi(...)`, `options(...)`
            }
        }

        var value = ParseAsmValue();
        var output = Match(TokenKind.Arrow) ? ParseAsmValue() : null;
        return new AsmOperand(start.Line, name, word.Text, arguments, value, output, null);
    }

    /// <summary>The value of an operand of an <c>asm</c>, where <c>x =&gt; y</c> is the operand split into what goes in and where the result goes (R11), no lambda.</summary>
    private Expression ParseAsmValue()
    {
        var saved = noLambda;
        noLambda = true;
        try
        {
            return ParseExpression();
        }
        finally
        {
            noLambda = saved;
        }
    }

    // ---- blocks and control flow ---------------------------------------------------------------

    /// <summary>
    /// A block that must open on <paramref name="line"/> (C1): a line break before the brace, or no brace, is <c>braces-required</c>
    /// there, and the body is read all the same. <paramref name="reported"/> says whether the error was reported, so that the
    /// construct adds none for what follows it (E1). A statement in place of the block is read as the block's one statement
    /// (recovery, README); what else stands on the line, a <c>;</c> or text after that statement, goes with the one error.
    /// </summary>
    private Block ParseBlockAfter(int line, string what, out bool reported)
    {
        reported = false;
        if (At(TokenKind.LeftBrace))
        {
            return ParseBlock();
        }

        reported = true;
        if (At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace)
        {
            Report(ErrorIds.BracesRequired, line, $"the block of {what} opens on its line, not on the next (C1)");
            SkipNewlines();
            return ParseBlock();
        }

        Report(ErrorIds.BracesRequired, line, $"{what} takes a block between braces");
        while (At(TokenKind.Semicolon))
        {
            Advance(); // `if x;`: the `;` goes with the missing block's error, not a `semicolon` of its own (E1)
        }

        if (EndsStatement())
        {
            return new Block(line, [], line);
        }

        try
        {
            var statement = ParseStatement();
            if (!EndsStatement() && !AtKeyword("else"))
            {
                throw new ParseError(ErrorLine(), $"the rest of the line after {what} is given up");
            }

            return new Block(line, [statement], statement.Line);
        }
        catch (ParseError e) when (!e.Reported)
        {
            // `if x y {` or `if x = 1 {`: the text after the missing block is given up under the one `braces-required`, as
            // ExpectOpeningBrace gives up a construct whose brace is missing
            throw new ParseError(e.Line, e.Message) { Reported = true };
        }
    }

    private Block ParseBlockAfter(int line, string what) => ParseBlockAfter(line, what, out _);

    /// <summary>
    /// The <c>{</c> of a body that opens on the line of its construct (C1): one on the next line is <c>braces-required</c> at the
    /// construct's line, and the body still parses; one that is missing altogether is the same error there, and the statement is given up.
    /// </summary>
    private void ExpectOpeningBrace(int line, string what)
    {
        if (At(TokenKind.LeftBrace) && index > 0 && tokens[index - 1].Kind == TokenKind.Newline)
        {
            Report(ErrorIds.BracesRequired, line, $"{what} opens on its own line"); // the line break was passed over inside parentheses or brackets (L4)
        }

        if (BraceOnNextLine())
        {
            Report(ErrorIds.BracesRequired, line, $"{what} opens on its own line");
            SkipNewlines();
        }

        if (!At(TokenKind.LeftBrace))
        {
            Report(ErrorIds.BracesRequired, line, $"{what} takes a block between braces");
            throw new ParseError(line, $"{what} takes a block between braces") { Reported = true };
        }

        Advance();
    }

    private bool BraceOnNextLine() => At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace;

    private Block ParseBlock()
    {
        var open = Expect(TokenKind.LeftBrace, "`{`");
        var statements = ParseStatementsUntil(TokenKind.RightBrace);
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
        // C1: the block opens where the condition ends, and E1: a block that is missing or opens on the next line is an error of the
        // whole `if`, reported on the line of its condition, its last line when the condition runs over several lines (the two rules
        // record the owner's choices of 2026-10-10 and 2026-10-11); a statement in its place is read as the block's one statement
        var then = ParseBlockAfter(tokens[index - 1].Line, "`if`", out var reported);
        Statement? elseClause = null;
        if (AtKeyword("else"))
        {
            Advance();
            elseClause = ParseElseClause(reported);
        }
        else if (At(TokenKind.Newline) && PeekPastNewlines().IsKeyword("else") && !ElseStartsAnArm())
        {
            // C2, E4: `else` on a line of its own is `syntax` at its line; the branch still parses, so that the one error stands. An
            // `if` whose block already drew `braces-required` adds none for it: the C# brace style, every brace on a line of its
            // own, is one error per `if` (E1)
            SkipNewlines();
            var token = Advance();
            if (!reported)
            {
                ReportSyntax(token.Line, "`else` follows the closing brace of the `if` block on the same line");
            }

            elseClause = ParseStrayElseClause(token.Line);
        }

        return new IfStatement(keyword.Line, condition, then, elseClause);
    }

    /// <summary>The clause after <c>else</c> (C2): an <c>if</c>, or a block, whose <c>{</c> on the next line is <c>braces-required</c> at the line of the word <c>else</c> (C1) unless <paramref name="reported"/> says the construct has its one error already (E1).</summary>
    private Statement ParseElseClause(bool reported)
    {
        if (AtKeyword("if"))
        {
            return ParseIf();
        }

        if (reported && BraceOnNextLine())
        {
            SkipNewlines();
            return ParseBlock();
        }

        return ParseBlockAfter(tokens[index - 1].Line, "`else`");
    }

    /// <summary>The clause of a stray <c>else</c>, whose error is reported: a block, on this line or the next, or an <c>if</c> parses; anything else is given up under that one error (E1).</summary>
    private Statement ParseStrayElseClause(int line)
    {
        if (At(TokenKind.LeftBrace) || AtKeyword("if") || BraceOnNextLine())
        {
            return ParseElseClause(reported: true);
        }

        throw new ParseError(line, "`else` takes a block") { Reported = true };
    }

    private WhileStatement ParseWhile()
    {
        var keyword = Advance();
        var condition = ParseExpression();
        return new WhileStatement(keyword.Line, condition, ParseBlockAfter(tokens[index - 1].Line, "`while`")); // C1: the last line of the header
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
        if (At(TokenKind.LeftBrace) && index > 0 && tokens[index - 1].Kind == TokenKind.Newline)
        {
            // C1 inside parentheses or brackets: the line break before the brace was passed over (L4), and the arms still open on the next line
            Report(ErrorIds.BracesRequired, line, $"the arms of {what} open on its line");
        }

        if (!At(TokenKind.LeftBrace))
        {
            if (At(TokenKind.Newline) && PeekPastNewlines().Kind == TokenKind.LeftBrace)
            {
                Report(ErrorIds.BracesRequired, line, $"the arms of {what} open on its line");
                SkipNewlines();
            }
            else
            {
                // C1: arms that are missing, or a statement in their place, are an error of the whole construct at its line (the record, 2026-10-10)
                Report(ErrorIds.BracesRequired, line, $"the arms of {what} stand between braces");
                throw new ParseError(line, $"the arms of {what} stand between braces") { Reported = true };
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
                var body = At(TokenKind.LeftBrace) ? ParseBlock() : ParseStatement();
                if (sawElse)
                {
                    ReportSyntax(armLine, "no arm follows `else`, which matches whatever the arms before it did not (C9); an arm after it is `syntax` (E4)");
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

        // C9: an arm names a literal, `true`, `false` or `null`; D12: a value by its path, `Plan.Free`. The front end takes a `-`
        // before a number literal as part of it, as L17 reads the `-100` of `sbyte low = -100` as one literal with its sign. Nothing else is a value
        // pattern: `-y`, `"a".Length` or `Plan.Free(1)` there is text no rule gives a meaning (E4)
        if (token.Kind == TokenKind.Minus)
        {
            Advance();
            if (Current.Kind is not (TokenKind.IntegerLiteral or TokenKind.RealLiteral or TokenKind.UnitLiteral))
            {
                throw new ParseError(token.Line, "an arm names a literal; `-` stands before a number literal only (C9)");
            }

            return new ValuePattern(token.Line, new UnaryExpression(token.Line, TokenKind.Minus, ParsePrimary()));
        }

        if (token.Kind is TokenKind.IntegerLiteral or TokenKind.RealLiteral or TokenKind.StringLiteral or TokenKind.CharLiteral
            or TokenKind.UnitLiteral || token.IsKeyword("true") || token.IsKeyword("false") || token.IsKeyword("null"))
        {
            return new ValuePattern(token.Line, ParsePrimary());
        }

        if ((token.Kind == TokenKind.Name || token.Kind == TokenKind.Keyword) && Peek().Kind == TokenKind.Dot)
        {
            var path = ParsePrimary();
            while (At(TokenKind.Dot))
            {
                var dot = Advance();
                var member = At(TokenKind.Keyword) ? Advance() : ExpectName("a member name");
                path = new MemberAccess(dot.Line, path, member.Text, false);
            }

            return new ValuePattern(token.Line, path);
        }

        // C4: an arm names a case type, optionally followed by a name for the value as that type; a bare name is a type, never a constant
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
        while (true)
        {
            var flag = Current switch
            {
                // R13: `thread` is a word only before `static`, and a name everywhere else (L18)
                { Kind: TokenKind.Name, Text: "thread" } when Peek().IsKeyword("static") => Modifiers.Thread,
                // `extern "C"` before a function, its C calling convention (R16), or before a data type, C's layout (R17); before `(`
                // it starts the type of a C function (R17), which the type is read as
                { Kind: TokenKind.Keyword, Text: "extern" } when Peek().Kind == TokenKind.StringLiteral && Peek(2).Kind != TokenKind.LeftParen => Modifiers.Extern,
                { Kind: TokenKind.Keyword } => Current.Text switch
                {
                    "pub" => Modifiers.Pub,
                    "prot" => Modifiers.Prot,
                    "static" => Modifiers.Static,
                    "mut" => Modifiers.Mut,
                    "virtual" => Modifiers.Virtual,
                    "override" => Modifiers.Override,
                    "naked" => Modifiers.Naked, // R12
                    _ => Modifiers.None,
                },
                _ => Modifiers.None,
            };
            if (flag == Modifiers.None)
            {
                break;
            }

            if ((modifiers & flag) != 0)
            {
                // a reading under E4's general clause (README; a question for a design round): no rule gives a modifier written twice a
                // meaning, in a type body (K7: class bodies follow C#, which refuses it, CS1004) or outside one, and the id could be the
                // checker's; the front end reports `syntax`
                throw new ParseError(Current.Line, $"`{Current.Text}` is written twice");
            }

            modifiers |= flag;
            Advance();
            if (flag == Modifiers.Extern)
            {
                ExpectConvention();
            }
        }

        return modifiers;
    }

    /// <summary>
    /// A declaration that starts with modifiers or a type: a variable (V2 to V4), a function (F1), or
    /// in a type body a field, a method or a constructor (K7, K14). <paramref name="typeName"/> is the
    /// name of the type whose body is parsed, or null outside one.
    /// </summary>
    private Statement ParseDeclaration(string? typeName)
    {
        var start = Current;
        var modifiers = ParseModifiers();
        if (At(TokenKind.Keyword) && Current.Text is "data" or "class" or "interface" or "enum" or "flags")
        {
            committed = true; // `pub class`: a type declaration, whatever follows; an error in its body is reported where it stands
            return ParseTypeDeclaration(modifiers);
        }

        // `mut x = 1`: a declaration without a written type (V2); in a type body a field without one, the reading on K7 with V2 the
        // README lists (K7 shows a field as `Type name`, V2 speaks of variables, and the record's actor bodies write
        // `mut users = Map<int, User>()`). The modifiers beside `mut` stay on the node: which of them apply is the checker's.
        if ((modifiers & Modifiers.Mut) != 0 && (At(TokenKind.Name) || At(TokenKind.Keyword)) && Peek().Kind == TokenKind.Assign)
        {
            var name = ExpectName("the name of the variable");
            committed = true; // `mut name =`: a declaration, whatever follows
            Advance(); // `=`
            SkipNewlines();
            var untypedValue = ParseExpression();
            return typeName is null
                ? new VariableDeclaration(start.Line, modifiers, null, name.Text, untypedValue)
                : new FieldDeclaration(start.Line, modifiers, null, name.Text, untypedValue);
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
                ? new VariableDeclaration(start.Line, modifiers, type, declared.Text, value) // every modifier written stays on the node (the checker judges them)
                : new FieldDeclaration(start.Line, modifiers, type, declared.Text, value);
        }

        if (At(TokenKind.LeftParen) || At(TokenKind.Less))
        {
            committed = true; // `Type name(`: a function, whatever follows
            return ParseFunctionRest(start.Line, modifiers, type, declared, typeName);
        }

        if (typeName is not null && EndsStatement())
        {
            // K7, K14: `int x` in a class body, a field a constructor assigns; whether another kind of body may hold one is the checker's
            return new FieldDeclaration(start.Line, modifiers, type, declared.Text, null);
        }

        throw Fail("`=` with the variable's value, or the parameters of a function");
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
            // C1, last sentence, which records the owner's choice of 2026-10-10: "A method signature inside a type body that ends its
            // line is the bodiless form of an interface member (K7), so its `{` on the next line is `syntax` (E4)"; whether this kind
            // of body may hold the form is the checker's
            return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, null, null);
        }

        // C1: the body of a function outside a type body opens on the first line of its signature; one on the next line, one that
        // is missing (E1: an error of the whole construct) or a statement in its place is `braces-required` there
        return new FunctionDeclaration(line, modifiers, returnType, name.Text, typeParameters, parameters, null, ParseBlockAfter(line, "the body of a function"));
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
                // T19: a limit names an interface, so it is one type by name; a union, a function type or a type in parentheses there
                // is text no rule gives a meaning (E4), whatever the parentheses
                var limitToken = Current;
                limit = ParseCoreType();
                if (limit is not NamedType)
                {
                    throw new ParseError(limitToken.Line, "a type parameter's limit names an interface (T19)");
                }
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
            defaultValue = ParseExpression(); // D11, F12: a default value after the name, `int Count = 1`
        }

        return new Parameter(start.Line, mut, type, name.Text, defaultValue);
    }

    private TypeDeclaration ParseTypeDeclaration(Modifiers modifiers)
    {
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
        if (kind is TypeDeclarationKind.Enum or TypeDeclarationKind.Flags && At(TokenKind.Less))
        {
            // D12, D13 give an enum and a flags type a name and a body; type parameters there are text no rule gives a meaning (E4)
            throw new ParseError(name.Line, "an `enum` or `flags` type has no type parameters");
        }

        var typeParameters = ParseTypeParameters(); // T19: a generic type lists its type parameters after its name
        IReadOnlyList<Parameter>? primary = null;
        var bases = new List<BaseSpecifier>();
        var equalBy = new List<string>();
        List<Statement>? members = null; // null where no body is written; an empty list for `{ }`
        var values = new List<EnumMember>();
        if (kind is TypeDeclarationKind.Data or TypeDeclarationKind.Class)
        {
            if (At(TokenKind.LeftParen))
            {
                primary = ParseParameters(typesOptional: false); // D1: a data type's primary constructor and its fields; K1: a class's
            }

            if (Match(TokenKind.Colon))
            {
                // K6, K10: the base class and the interfaces after `:`, the base's arguments in the explicit form; D7: a data type's base
                do
                {
                    SkipNewlines(); // L4: a line break after the `:` of a base list, or after its comma, continues the declaration
                    var baseType = ParseType();
                    IReadOnlyList<Argument>? arguments = At(TokenKind.LeftParen) ? ParseArguments() : null;
                    bases.Add(new BaseSpecifier(baseType.Line, baseType, arguments));
                }
                while (Match(TokenKind.Comma));
            }

            if (MatchKeyword("equal"))
            {
                // K8: the fields that count, named after the head, `equal by Id`; K15: one or more of them with a comma between
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
                while (Match(TokenKind.Comma) && !At(TokenKind.RightBrace)); // L4 (decided): "a trailing comma is accepted in an `enum` or `flags` body and refused in the braces of `with`" (the owner, 2026-10-10)
            }

            // an empty body parses; whether a type without a value is allowed is the checker's (D12)
            suppressNewlines--;
            SkipNewlines();
            Expect(TokenKind.RightBrace, "`}`");
            openBraces--; // after the `}`: an error at the `}` leaves the brace for recovery to close
        }

        return new TypeDeclaration(keyword.Line, modifiers, kind, name.Text, typeParameters, primary, bases, equalBy, members, values);
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
                members.Add(ParseDeclaration(typeName));
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
            if (AtKeyword("weak"))
            {
                // a reading under E4's general clause (README; a question for a design round): R3 and R5 write one `weak` before a
                // type, so a second is text no rule gives a meaning, as a second `?` is under N1; the id could be the checker's
                throw new ParseError(Current.Line, "`weak` is written twice");
            }

            return new WeakType(weak.Line, ParseNonUnionType()); // R3, R5: `weak T?`
        }

        var type = ParseCoreType();
        // R10: `int*`, a raw pointer, and `int**`, a pointer to one, `void*` the untyped one (R14), as C# writes them; a pointer holds
        // `null` itself, so a `?` after one is text no rule gives a meaning (E4), while `int?*` points to an `int?`, a pointee the
        // checker judges
        while (At(TokenKind.Star) || (At(TokenKind.Question) && type is not NullableType))
        {
            var suffix = Advance();
            if (suffix.Kind == TokenKind.Star)
            {
                type = new PointerType(suffix.Line, type);
                continue;
            }

            if (type is PointerType)
            {
                throw new ParseError(suffix.Line, "a pointer holds `null` itself and takes no `?` (R10)");
            }

            // N1: `T?`; a second `?` is text no rule gives a meaning, which the caller's next expectation reports (E4)
            type = new NullableType(suffix.Line, type);
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

            suppressNewlines--;
            Expect(TokenKind.RightParen, "`)`");
            if (Match(TokenKind.Arrow))
            {
                SkipNewlines(); // L4: a trailing `=>` continues, a function type's as much as a lambda's
                // F11: the `=>` of a function type reaches to the end of the type, so `(int) => int | NotFound` returns the union, and a
                // union of a function type and another type is written `((int) => int) | NotFound` (the rule records the owner's choice of
                // 2026-10-11)
                return new FunctionType(token.Line, parameters, ParseType());
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

        if (token.IsKeyword("extern"))
        {
            // R17: `extern "C" (int) => void`, the type of a function that takes the C calling convention, F11's function type after
            // `extern "C"`; its nullable form is written in parentheses, `(extern "C" (int) => void)?`
            Advance();
            ExpectConvention();
            if (!At(TokenKind.LeftParen))
            {
                throw Fail("the parameter types of the C function, `(int) => void`");
            }

            if (ParseCoreType() is not FunctionType function)
            {
                throw new ParseError(token.Line, "`extern \"C\"` before a type takes a function type, `extern \"C\" (int) => void` (R17)");
            }

            return new ExternFunctionType(token.Line, function.Parameters, function.Result);
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

    // The levels, lowest first, as C# binds them: T18 (`+ - * / %`, the comparisons, `&& ||`), T22 (`& | ^ << >>`) and T12 (`+% -%`
    // with `+ -`, `*%` with `*`) give the operators "the precedence they have in C#", and N4's `??` sits below `||`, grouping to the
    // right (ParseCoalesce), as there; comparisons and equality chain to the left as there. Where `..` and `..<` bind is a reading
    // C8 leaves open (README): below `+ -` and `<< >>` and above the comparisons, so `1..n + 1` runs to `n + 1` and `1..n << 1` to
    // `n << 1`, and a range chains with nothing.
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
        or TokenKind.LeftParen or TokenKind.Minus or TokenKind.Bang or TokenKind.Tilde or TokenKind.Star or TokenKind.Ampersand; // no expression starts with `[`

    private Expression ParseUnary()
    {
        // T18: `-` and `!`, T22: `~`, prefix operators that bind tighter than every binary operator, as in C#
        if (At(TokenKind.Minus) || At(TokenKind.Bang) || At(TokenKind.Tilde))
        {
            var op = Advance();
            return new UnaryExpression(op.Line, op.Kind, ParseUnary());
        }

        // R10: `*p` reads through a pointer and `&x` takes the address of a variable, a field or an element, prefix operators as C#
        // writes them in `unsafe` code, binding as `-` does, so `*p.Next` reads through `p.Next`; `&` of anything else is text no
        // rule gives a meaning (E4), as the left side of an assignment is
        if (At(TokenKind.Star) || At(TokenKind.Ampersand))
        {
            var op = Advance();
            var operand = ParseUnary();
            if (op.Kind == TokenKind.Ampersand && !IsPlace(operand))
            {
                throw new ParseError(op.Line, "`&` takes a variable, a field or an element (R10)");
            }

            return new UnaryExpression(op.Line, op.Kind, operand);
        }

        return ParsePostfix();
    }

    private static bool IsPlace(Expression expression) => expression switch
    {
        NameExpression or IndexExpression or MemberAccess { Conditional: false } or UnaryExpression { Operator: TokenKind.Star } => true,
        ParenthesizedExpression p => IsPlace(p.Inner),
        _ => false,
    };

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
                // the reading on L12 (README; a question for a design round): a core word after the dot is a member name, `match.Add(1)`,
                // `x.match(1)` and `x.int` alike, with no error at the use, so that the one error stays the declaration's (E1)
                var member = At(TokenKind.Keyword) ? Advance() : ExpectName("a member name");
                expression = new MemberAccess(dot.Line, expression, member.Text, dot.Kind == TokenKind.QuestionDot);
                continue;
            }

            if (At(TokenKind.LeftParen))
            {
                expression = new CallExpression(expression.Line, expression, [], ParseArguments());
                continue;
            }

            // T19: a call names its type arguments, `First<int>(xs)` and `xs.Map<int>(f)` alike. How they are told from comparisons is
            // a reading (README; a question for a design round): the list is kept when `(` follows the `>`, so `f(a < b, c > (d))` is
            // the call `a<b, c>(d)` and `a < b, c > d` is two comparisons; C# (T17: generics as in C#) also keeps it before `)`, `]`,
            // `,`, `.`, `?` and a few more, where this front end reads a comparison, since the reference has no form for a generic
            // name that is not called
            if (At(TokenKind.Less) && expression is NameExpression or MemberAccess)
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
                var open = Advance(); // M9: `list[i]`, `map[key]`
                suppressNewlines++;
                var indexValue = ParseExpression();
                suppressNewlines--;
                Expect(TokenKind.RightBracket, "`]`");
                expression = new IndexExpression(open.Line, expression, indexValue);
                continue;
            }

            if (AtKeyword("with"))
            {
                var with = Advance(); // D5: `value with { Field = expression }`; its braces open on the line of `with` (C1) and take no trailing comma (L4)
                expression = new WithExpression(with.Line, expression, ParseFieldAssignments(with.Line));
                continue;
            }

            if (AtKeyword("else") && expression is CallExpression)
            {
                var elseKeyword = Advance(); // O5: `call else { Case => ... }` handles the listed cases at the call, so only a call takes the postfix `else`
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
        var savedNoLambda = noLambda;
        noLambda = false; // an argument of a call inside an operand of `asm` takes a lambda as any argument does
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

                // M7: the caller writes `mut` in front of an argument for a `mut` parameter. A `mut` with nothing that can start an
                // expression after it, `f(mut)` or `f(mut, 1)`, is a name (the reading on L12), so a variable of that name is used
                // without a second error (E1)
                var mut = AtKeyword("mut") && CanStartExpression(Peek().Kind) && MatchKeyword("mut");
                arguments.Add(new Argument(start.Line, name, mut, ParseExpression()));
            }
            while (Match(TokenKind.Comma));
        }

        suppressNewlines--;
        noLambda = savedNoLambda;
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
                if (At(TokenKind.Arrow) && !noLambda)
                {
                    Advance(); // F7: `x => ...`, a lambda written as in C#
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
                    case "asm" when Peek().Kind == TokenKind.LeftParen:
                        throw new ParseError(token.Line, "`asm` is a statement of its own (R11)");
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
                // F7: `(x, y) => ...` and `(int x, int y) => ...`, the parameters tried speculatively, since `(` also opens a
                // parenthesized expression (C1)
                List<Parameter>? parameters = null;
                if (!noLambda && Try(() =>
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

    /// <summary>
    /// The body of a lambda (F7): a block, or an expression. The reading on F7 with L4 and C1 (README; a question for a design
    /// round): a <c>=&gt;</c> that ends its line continues the statement (L4), so the block or the expression may start on the
    /// next line without C1's error, which names no line for a lambda's block.
    /// </summary>
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
                throw new ParseError(inner.Current.Line, $"an interpolation holds one expression; found {inner.Current}"); // E1: the line of that token
            }

            segments.Add(new HoleSegment(expression));
        }

        return new StringLiteral(token.Line, segments);
    }
}
