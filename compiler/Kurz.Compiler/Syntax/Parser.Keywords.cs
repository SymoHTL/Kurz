using System.Text.RegularExpressions;

namespace Kurz.Compiler.Syntax;

/// <summary>
/// Keywords that transform code (record section 9, chapter 21). A declaration, <c>keyword retry &lt;int times&gt; &lt;block body&gt; { ... }</c>,
/// stands at the top level of a file (J1) and is read before the file's statements, its pattern (J2) and then its template, so that a use
/// can stand before it and in another keyword's template. A use stands where a statement does and matches the pattern (J3); the parser
/// puts the block its template fills in its place (J4). The template is read again from its tokens at each use: every token stands on
/// the use's line, so that an error inside the expansion points at the use (record section 9, rule 4), and every name the template
/// declares is renamed to one no identifier of the file holds, so that the use's names and the template's never meet (J7, rule 6). An
/// expression slot is bound once, in front, to a variable of the slot's type (J5); a block slot is the use's block, and its jumps act on
/// the function and the loop around the use (J6).
/// </summary>
public sealed partial class Parser
{
    /// <summary>J8: the depth of keywords used inside expansions (record section 9, the owner, 2026-11-27, #667, comment 7215318330, Q7).</summary>
    public const int KeywordDepthLimit = 64;

    /// <summary>
    /// J9: the words the parser reads as forms where a statement starts, which no keyword can be declared as, beside the core words of L18
    /// and the types of L19, and <c>retry</c>, the keyword of <c>std/</c> (record section 9, the owner, 2026-11-28, #667, comment 7236862510,
    /// Q19 (a)), which no keyword can be declared as either, as another keyword (rule 8).
    /// </summary>
    private static readonly HashSet<string> StatementWords =new HashSet<string>(StringComparer.Ordinal)
    {
        "keyword", "within", "test", "check", "advance", "mock", "const", "attempt", "retry", "restart", "stop", "escalate",
    };

    private KeywordTable table = KeywordTable.None;
    private Func<IReadOnlyList<Token>, IReadOnlyList<Token>>? expansion; // J4: how the tokens of a hole of the template being read stand at the use
    private int expansionDepth; // J8: the templates being read around the cursor
    private bool file; // the parser of the file's own tokens, whose indexes the table's declarations name

    [GeneratedRegex(@"[\p{L}_][\p{L}\p{N}_]*")]
    private static partial Regex Identifier();

    /// <summary>The keywords of one file, by their word and by the index of their declaration's first token, and the names an expansion takes.</summary>
    private sealed class KeywordTable(string text)
    {
        public static readonly KeywordTable None = new(string.Empty);

        private HashSet<string>? names;
        private int count;

        public Dictionary<string, Keyword> ByWord { get; } = new(StringComparer.Ordinal);

        public Dictionary<int, Keyword> ByStart { get; } = new();

        /// <summary>Each name an expansion took, to the name its template wrote: <c>attempt_3</c> to <c>attempt</c>.</summary>
        public Dictionary<string, string> Taken { get; } = new(StringComparer.Ordinal);

        /// <summary>J7: a name that no identifier of the file holds and no expansion took before, <c>attempt_3</c>.</summary>
        public string Fresh(string name)
        {
            names ??= Identifier().Matches(text).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            while (true)
            {
                count++;
                var fresh = $"{name}_{count}";
                if (names.Add(fresh))
                {
                    Taken[fresh] = name;
                    return fresh;
                }
            }
        }
    }

    /// <summary>
    /// Gives the bag each name the file's expansions took, so that a diagnostic names it as the template wrote it (J7; record section 9,
    /// rule 4): an error inside an expansion points at what the developer wrote, at the use's line.
    /// </summary>
    private void HandTaken()
    {
        foreach (var (fresh, written) in table.Taken)
        {
            diagnostics.Took(fresh, written);
        }
    }

    /// <summary>A declared keyword: its tokens as the file has them, its pattern and its template once read, and what its template does with its slots.</summary>
    private sealed class Keyword
    {
        public required Token Word { get; init; }

        public required int Start { get; init; } // the index of `keyword` among the file's tokens

        public required int Close { get; init; } // the index of the template's `}`

        public required Token Open { get; init; }

        public required Token CloseToken { get; init; }

        public required IReadOnlyList<Token> PatternTokens { get; init; } // with an end of file

        public required IReadOnlyList<Token> TemplateTokens { get; init; } // between the braces

        public IReadOnlyList<KeywordElement> Pattern { get; set; } = [];

        public Block? Template { get; set; }

        public bool Broken { get; set; } // an error of the declaration was reported: a use reads its slots and stands as an empty block (E1)

        public int State { get; set; } // 0: the template is unread, 1: being read, 2: read

        public HashSet<string> Binders { get; } = new(StringComparer.Ordinal); // the names the template declares, renamed at each use (J7)

        public HashSet<string> InLoop { get; } = new(StringComparer.Ordinal); // the block slots the template puts inside a loop of its own (J6)

        public KeywordDeclaration Declaration => new(Word.Line, Word.Text, Pattern, Template ?? new Block(Open.Line, [], CloseToken.Line));
    }

    private static IEnumerable<KeywordSlot> Slots(IReadOnlyList<KeywordElement> pattern) =>
        pattern.SelectMany(e => e switch
        {
            KeywordSlot slot => [slot],
            KeywordOptional optional => Slots(optional.Elements),
            _ => Enumerable.Empty<KeywordSlot>(),
        });

    /// <summary>
    /// J1: every declaration at the top level of the file, <c>keyword</c> first on its line, then the keyword's word on that line and a
    /// <c>{</c> after the pattern there; each word registered (J9), each pattern read (J2), then each template (J4).
    /// </summary>
    private List<KeywordDeclaration> ReadKeywords()
    {
        file = true;
        table = new KeywordTable(source.Text);
        var found = new List<Keyword>();
        var depth = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind is TokenKind.LeftParen or TokenKind.LeftBracket or TokenKind.LeftBrace)
            {
                depth++;
                continue;
            }

            if (token.Kind is TokenKind.RightParen or TokenKind.RightBracket or TokenKind.RightBrace)
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth != 0 || token is not { Kind: TokenKind.Name, Text: "keyword" } || (i > 0 && tokens[i - 1].Kind != TokenKind.Newline)
                || tokens[i + 1].Kind is not (TokenKind.Name or TokenKind.Keyword) || tokens[i + 1].Line != token.Line)
            {
                continue;
            }

            var open = i + 2;
            while (tokens[open].Kind is not (TokenKind.LeftBrace or TokenKind.Newline or TokenKind.EndOfFile))
            {
                open++;
            }

            var close = open + 1;
            for (var braces = 1; tokens[open].Kind == TokenKind.LeftBrace && tokens[close].Kind != TokenKind.EndOfFile; close++)
            {
                braces += tokens[close].Kind switch { TokenKind.LeftBrace => 1, TokenKind.RightBrace => -1, _ => 0 };
                if (braces == 0)
                {
                    break;
                }
            }

            if (tokens[open].Kind != TokenKind.LeftBrace || tokens[close].Kind != TokenKind.RightBrace)
            {
                continue; // the statement's parse says what is missing (TryKeyword)
            }

            found.Add(new Keyword
            {
                Word = tokens[i + 1],
                Start = i,
                Close = close,
                Open = tokens[open],
                CloseToken = tokens[close],
                PatternTokens = [.. tokens.Skip(i + 2).Take(open - i - 2), new Token(TokenKind.EndOfFile, string.Empty, tokens[open].Line, tokens[open].Offset)],
                TemplateTokens = tokens.Skip(open + 1).Take(close - open - 1).ToList(),
            });
            i = close;
        }

        foreach (var keyword in found)
        {
            table.ByStart[keyword.Start] = keyword;
            var word = keyword.Word.Text;
            if (keyword.Word.Kind == TokenKind.Keyword || CoreWords.IsReserved(word) || StatementWords.Contains(word))
            {
                Report(ErrorIds.KeywordRedefined, keyword.Word.Line, $"`{word}` is a word of the language, and no keyword can be declared as it (J9)");
                keyword.Broken = true;
            }
            else if (!table.ByWord.TryAdd(word, keyword))
            {
                Report(ErrorIds.KeywordRedefined, keyword.Word.Line, $"`{word}` is a keyword already: a keyword cannot be declared again (J9)");
                keyword.Broken = true;
            }
        }

        foreach (var keyword in found.Where(k => !k.Broken))
        {
            keyword.Pattern = ReadPattern(keyword);
        }

        foreach (var keyword in found.Where(k => !k.Broken))
        {
            ReadTemplate(keyword);
        }

        return found.Select(k => k.Declaration).ToList();
    }

    /// <summary>J2: the parts of a pattern, from after the keyword's word to the template's <c>{</c>.</summary>
    private List<KeywordElement> ReadPattern(Keyword keyword)
    {
        var reader = new Parser(source, keyword.PatternTokens, diagnostics) { table = table };
        try
        {
            var pattern = reader.PatternElements(keyword, new HashSet<string>(StringComparer.Ordinal), optional: false);
            Adjacent(keyword, pattern);
            return pattern;
        }
        catch (ParseError e)
        {
            if (!e.Reported)
            {
                Report(ErrorIds.Syntax, e.Line, e.Message);
            }

            keyword.Broken = true;
            return [];
        }
    }

    private List<KeywordElement> PatternElements(Keyword keyword, HashSet<string> names, bool optional)
    {
        var elements = new List<KeywordElement>();
        while (!At(TokenKind.EndOfFile) && !(optional && At(TokenKind.RightBracket)))
        {
            var token = Current;
            if (Match(TokenKind.Less))
            {
                var kind = KeywordSlotKind.Expression;
                TypeSyntax? type = null;
                if (Current is { Kind: TokenKind.Name, Text: "block" } && Peek().Kind == TokenKind.Name)
                {
                    Advance();
                    kind = KeywordSlotKind.Block;
                }
                else if (Current is { Kind: TokenKind.Name, Text: "detached" } && Peek() is { Kind: TokenKind.Name, Text: "block" })
                {
                    Advance();
                    Advance();
                    kind = KeywordSlotKind.DetachedBlock; // record section 9, Q10 (a) (assumed, #667, comment 7229158580)
                }
                else if (Current is { Kind: TokenKind.Name, Text: "name" } && Peek().Kind == TokenKind.Name)
                {
                    Advance();
                    kind = KeywordSlotKind.Name;
                }
                else
                {
                    type = ParseType();
                }

                if (At(TokenKind.Ellipsis))
                {
                    Unsupported(token.Line, "a repeated part of a keyword's pattern, `<int... codes>` (J2)");
                }

                var name = ExpectName("the name of the slot");
                ExpectGreater();
                if (!names.Add(name.Text))
                {
                    throw new ParseError(name.Line, $"the slot `{name.Text}` is named twice in the pattern of `{keyword.Word.Text}` (J2)");
                }

                if (optional && kind == KeywordSlotKind.Expression)
                {
                    Unsupported(token.Line, "an expression slot in an optional part of a keyword's pattern (J2)");
                }

                elements.Add(new KeywordSlot(token.Line, kind, type, name.Text));
            }
            else if (Match(TokenKind.LeftBracket))
            {
                if (optional)
                {
                    Unsupported(token.Line, "an optional part inside an optional part of a keyword's pattern (J2)");
                }

                var inner = PatternElements(keyword, names, optional: true);
                Expect(TokenKind.RightBracket, "`]`");
                if (inner is not [KeywordWord, ..])
                {
                    throw new ParseError(token.Line, "an optional part of a pattern starts with a fixed word, which tells a use that the part is written (J2)");
                }

                elements.Add(new KeywordOptional(token.Line, inner));
            }
            else if (token.Kind is TokenKind.Name or TokenKind.Keyword)
            {
                Advance();
                elements.Add(new KeywordWord(token.Line, token.Text));
            }
            else
            {
                throw new ParseError(token.Line, $"a pattern holds fixed words, slots between `<` and `>` and optional parts between `[` and `]`; found {token} (J2)");
            }
        }

        return elements;
    }

    /// <summary>A form of a pattern the record names and this build does not read: <c>not supported</c>, and the declaration is given up.</summary>
    private void Unsupported(int line, string construct)
    {
        Keep(Diagnostic.NotSupported(line, construct));
        throw new ParseError(line, construct) { Reported = true };
    }

    /// <summary>
    /// J2: where an expression slot ends, a use must see it end: after it stands no other expression slot, also past an optional part,
    /// and no fixed word that goes on from an expression, <c>else</c> after a call or <c>with</c>.
    /// </summary>
    private static void Adjacent(Keyword keyword, List<KeywordElement> pattern)
    {
        for (var i = 0; i < pattern.Count; i++)
        {
            if (pattern[i] is not KeywordSlot { Kind: KeywordSlotKind.Expression } slot)
            {
                continue;
            }

            for (var j = i + 1; j < pattern.Count; j++)
            {
                var next = pattern[j] is KeywordOptional optional ? optional.Elements[0] : pattern[j];
                if (next is KeywordSlot { Kind: KeywordSlotKind.Expression } other)
                {
                    throw new ParseError(other.Line, $"the expression slots `{slot.Name}` and `{other.Name}` of `{keyword.Word.Text}` follow each other, with no fixed word to end the first (J2)");
                }

                if (next is KeywordWord { Word: "else" or "with" } word)
                {
                    throw new ParseError(word.Line, $"the fixed word `{word.Word}` after the expression slot `{slot.Name}` goes on from an expression, so the slot would not end there (J2)");
                }

                if (pattern[j] is not KeywordOptional)
                {
                    break; // past an optional part, the part after it may follow the slot too
                }
            }
        }
    }

    /// <summary>
    /// J4: the template read once at its declaration, its lines its own, to report its errors there and to learn what it declares and
    /// where it runs its blocks. A use inside it is read too, so that a keyword used in its own template is found here (J8).
    /// </summary>
    private void ReadTemplate(Keyword keyword)
    {
        if (keyword.State != 0)
        {
            return;
        }

        keyword.State = 1;
        var before = diagnostics.Items.Count;
        var reader = new Parser(source, [.. keyword.TemplateTokens, new Token(TokenKind.EndOfFile, string.Empty, keyword.CloseToken.Line, keyword.CloseToken.Offset)], diagnostics)
        {
            table = table, expansionDepth = 1, statementDepth = 1,
        };
        keyword.Template = new Block(keyword.Open.Line, reader.ParseStatementsUntil(TokenKind.EndOfFile), keyword.CloseToken.Line);
        if (diagnostics.Items.Skip(before).Any(d => d.IsError))
        {
            keyword.Broken = true;
        }
        else
        {
            Learn(keyword);
        }

        keyword.State = 2;
    }

    /// <summary>
    /// J6, J7: the names the template declares, each slot's use, and where a block slot runs: a block slot stands alone as a statement,
    /// a plain one in no lambda of the template (its <c>return</c> would leave the lambda), and every slot is named in the template.
    /// </summary>
    private void Learn(Keyword keyword)
    {
        var slots = Slots(keyword.Pattern).ToDictionary(s => s.Name, StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);
        var alone = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var inLambda = new HashSet<string>(StringComparer.Ordinal);

        void Declare(string name)
        {
            named.Add(name);
            if (!slots.ContainsKey(name))
            {
                keyword.Binders.Add(name);
            }
        }

        bool IsBlock(string name) => slots.TryGetValue(name, out var slot) && slot.Kind is KeywordSlotKind.Block or KeywordSlotKind.DetachedBlock;

        new KeywordWalk(
            (s, loops, lambda) =>
            {
                switch (s)
                {
                    case AssignOrDeclare a:
                        Declare(a.Name);
                        break;
                    case VariableDeclaration v:
                        Declare(v.Name);
                        break;
                    case ForInStatement f:
                        Declare(f.Variable);
                        if (f.Second is { } second)
                        {
                            Declare(second.Name);
                        }

                        break;
                    case ExpressionStatement { Expression: NameExpression n } when IsBlock(n.Name):
                        alone[n.Name] = alone.GetValueOrDefault(n.Name) + 1;
                        if (lambda && slots[n.Name].Kind == KeywordSlotKind.Block)
                        {
                            inLambda.Add(n.Name);
                        }

                        if (loops > 0)
                        {
                            keyword.InLoop.Add(n.Name);
                        }

                        break;
                }
            },
            e =>
            {
                switch (e)
                {
                    case NameExpression n:
                        named.Add(n.Name);
                        if (IsBlock(n.Name))
                        {
                            seen[n.Name] = seen.GetValueOrDefault(n.Name) + 1;
                        }

                        break;
                    case LambdaExpression l:
                        foreach (var parameter in l.Parameters)
                        {
                            Declare(parameter.Name);
                        }

                        break;
                }
            },
            p =>
            {
                if (p is TypePattern { Binding: { } binding })
                {
                    Declare(binding);
                }
            }).Statements(keyword.Template!.Statements);

        foreach (var slot in slots.Values)
        {
            if (!named.Contains(slot.Name))
            {
                Declared(ErrorIds.Syntax, slot.Line, $"the slot `{slot.Name}` is named nowhere in the template of `{keyword.Word.Text}` (J2)");
                keyword.Broken = true;
            }
            else if (seen.GetValueOrDefault(slot.Name) != alone.GetValueOrDefault(slot.Name))
            {
                Declared(ErrorIds.Syntax, slot.Line, $"the block `{slot.Name}` stands alone as a statement of the template of `{keyword.Word.Text}`, where it runs (J6)");
                keyword.Broken = true;
            }
            else if (inLambda.Contains(slot.Name))
            {
                Declared(ErrorIds.KeywordJump, slot.Line, $"the block `{slot.Name}` stands in a lambda of the template of `{keyword.Word.Text}`, where a `return` in it would leave the lambda: a block the template runs apart is `<detached block {slot.Name}>` (J6)");
                keyword.Broken = true;
            }
        }
    }

    /// <summary>An error of a declaration, which reaches the bag whatever becomes of a speculative parse around the use that read its template.</summary>
    private void Declared(string id, int line, string message) => diagnostics.Add(Diagnostic.Error(id, line, message));

    /// <summary>
    /// J1, J3, J9 where a statement starts with a name: the declaration the file's first read took, which the statements skip; a use of a
    /// keyword the file declares; or <c>keyword</c> and a word that start no declaration there. False where the word is none of these.
    /// </summary>
    private bool TryKeyword(out Statement? statement)
    {
        statement = null;
        var token = Current;
        if (table.ByWord.TryGetValue(token.Text, out var used))
        {
            if (Peek().Kind == TokenKind.Assign)
            {
                Report(ErrorIds.ReservedWord, token.Line, $"`{token.Text}` is a keyword this file declares, and no name (J9)");
                return false; // the word stands as the name, so that the rest of the line is read (E1)
            }

            statement = ParseKeywordUse(used);
            return true;
        }

        if (token.Text != "keyword" || Peek().Kind is not (TokenKind.Name or TokenKind.Keyword) || Peek().Line != token.Line)
        {
            return false;
        }

        if (file && statementDepth == 1 && table.ByStart.TryGetValue(index, out var declared))
        {
            index = declared.Close + 1;
            statement = declared.Declaration;
            return true;
        }

        throw new ParseError(token.Line, file && statementDepth == 1
            ? "a keyword's template opens with `{` on the line of its pattern, and closes with `}` (J1)"
            : "a keyword is declared at the top level of a file alone (J1)");
    }

    /// <summary>J9, J11 where an expression starts with a keyword's word: a use there is <c>not supported</c>, and the word alone is no name.</summary>
    private void KeywordInExpression()
    {
        var token = Current;
        if (!table.ByWord.ContainsKey(token.Text))
        {
            return;
        }

        var next = Peek();
        if (next.Kind == TokenKind.LeftBrace || (next.Line == token.Line && CanStartExpression(next.Kind)))
        {
            Keep(Diagnostic.NotSupported(token.Line, $"a keyword used as an expression, `{token.Text}` (J11)"));
            throw new ParseError(token.Line, string.Empty) { Reported = true };
        }

        Report(ErrorIds.ReservedWord, token.Line, $"`{token.Text}` is a keyword this file declares, and no name (J9)");
    }

    /// <summary>J3, J4: a use, matched against its pattern, and the block its template fills in its place.</summary>
    private Block ParseKeywordUse(Keyword keyword)
    {
        var word = Advance();
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        MatchPattern(keyword, keyword.Pattern, values);
        if (!EndsStatement())
        {
            throw NoMatch(keyword, $"the line goes on after its pattern, with {Current}");
        }

        var last = tokens[index - 1].Line;
        if (keyword.State == 1)
        {
            // J8: a use inside the template being read, through that template or another's, would have no end; the error is the
            // declaration's, so it reaches the bag whatever becomes of a speculative parse around it
            diagnostics.Add(Diagnostic.Error(ErrorIds.KeywordDepth, word.Line, $"the template of `{keyword.Word.Text}` uses `{keyword.Word.Text}` again, so its expansions reach a depth past {KeywordDepthLimit} (J8)"));
            keyword.Broken = true;
        }

        ReadTemplate(keyword); // a keyword whose declaration's turn has not come, used in another's template
        return keyword.Broken ? new Block(word.Line, [], last) : Expand(keyword, word.Line, values, last);
    }

    private void MatchPattern(Keyword keyword, IReadOnlyList<KeywordElement> elements, Dictionary<string, object> values)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case KeywordWord fixedWord:
                    if (!AtWord(fixedWord.Word))
                    {
                        throw NoMatch(keyword, $"the word `{fixedWord.Word}` was expected, and {Current} stands there");
                    }

                    Advance();
                    break;
                case KeywordOptional optional:
                    if (AtWord(((KeywordWord)optional.Elements[0]).Word))
                    {
                        MatchPattern(keyword, optional.Elements, values);
                    }

                    break;
                case KeywordSlot { Kind: KeywordSlotKind.Expression } slot:
                    if (!CanStartExpression(Current.Kind))
                    {
                        throw NoMatch(keyword, $"an expression for `{slot.Name}` was expected, and {Current} stands there");
                    }

                    values[slot.Name] = Header(); // a `{` after it opens the block of the slot that follows in the pattern
                    break;
                case KeywordSlot { Kind: KeywordSlotKind.Name } slot:
                    if (!At(TokenKind.Name))
                    {
                        throw NoMatch(keyword, $"a name for `{slot.Name}` was expected, and {Current} stands there");
                    }

                    values[slot.Name] = Advance();
                    break;
                case KeywordSlot slot:
                    if (!At(TokenKind.LeftBrace) && !BraceOnNextLine())
                    {
                        throw NoMatch(keyword, $"the block `{slot.Name}` was expected, and {Current} stands there");
                    }

                    values[slot.Name] = ParseBlockAfter(tokens[index - 1].Line, $"the block `{slot.Name}` of `{keyword.Word.Text}`");
                    break;
            }
        }
    }

    private bool AtWord(string word) => Current.Kind is TokenKind.Name or TokenKind.Keyword && Current.Text == word;

    private ParseError NoMatch(Keyword keyword, string what)
    {
        var line = ErrorLine();
        Report(ErrorIds.KeywordNoMatch, line, $"this use of `{keyword.Word.Text}` matches no pattern of it: {what} (J3)");
        return new ParseError(line, what) { Reported = true };
    }

    /// <summary>
    /// J4 to J7: the block of one use. The expression slots are bound in the pattern's order to variables of their types in front, the
    /// template is read again from its tokens, each on the use's line, with the names it declares and its slots renamed, and each block
    /// slot, alone on its line of the template, is replaced by the use's block, a copy of it where it stands a second time.
    /// </summary>
    private Block Expand(Keyword keyword, int line, Dictionary<string, object> values, int last)
    {
        if (expansionDepth >= KeywordDepthLimit)
        {
            Report(ErrorIds.KeywordDepth, line, $"keywords used inside expansions reach a depth past {KeywordDepthLimit} at this use of `{keyword.Word.Text}` (J8)");
            return new Block(line, [], last);
        }

        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binder in keyword.Binders)
        {
            renames[binder] = table.Fresh(binder);
        }

        var front = new List<Statement>();
        var blocks = new Dictionary<string, Block>(StringComparer.Ordinal);
        foreach (var slot in Slots(keyword.Pattern))
        {
            values.TryGetValue(slot.Name, out var value);
            switch (slot.Kind)
            {
                case KeywordSlotKind.Expression:
                    // J5: evaluated once, where the keyword stands, as an argument is (record section 9, Q2); a slot of an optional part is not supported
                    var bound = table.Fresh(slot.Name);
                    renames[slot.Name] = bound;
                    front.Add(new VariableDeclaration(line, Modifiers.None, slot.Type, bound, (Expression)value!));
                    break;
                case KeywordSlotKind.Block or KeywordSlotKind.DetachedBlock:
                    var marker = table.Fresh(slot.Name);
                    renames[slot.Name] = marker;
                    var block = value as Block ?? new Block(line, [], line); // an optional part the use does not write runs nothing (assumed)
                    Jumps(keyword, slot, block);
                    blocks[marker] = block;
                    break;
                default:
                    renames[slot.Name] = value is Token name ? name.Text : table.Fresh(slot.Name); // J7: a name the use does not give is the template's own
                    break;
            }
        }

        IReadOnlyList<Token> Stand(IReadOnlyList<Token> written) => Renamed(written, renames, line);
        var reader = new Parser(source, [.. Stand(keyword.TemplateTokens), new Token(TokenKind.EndOfFile, string.Empty, line, keyword.CloseToken.Offset)], diagnostics)
        {
            table = table, expansion = Stand, expansionDepth = expansionDepth + 1, statementDepth = statementDepth, deciding = deciding,
            speculating = speculating, pending = pending,
        };
        var statements = reader.ParseStatementsUntil(TokenKind.EndOfFile);
        var placed = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        var filled = SyntaxCopy.Copy(new Block(line, statements, line), (_, _) => null, (s, _) =>
            s is ExpressionStatement { Expression: NameExpression n } && blocks.TryGetValue(n.Name, out var b)
                ? placed.Add(b) ? b : SyntaxCopy.Copy(b, (_, _) => null)
                : null);
        return new Block(line, [.. front, .. filled.Statements], last);
    }

    /// <summary>
    /// J6: a jump of the use's block that would land elsewhere than where it is written: in a block the template runs apart, any
    /// <c>return</c>, <c>break</c> or <c>continue</c> that leaves it; in a block the template puts inside a loop of its own, a
    /// <c>break</c> or <c>continue</c> that leaves it, which would act on the template's loop and not on the loop around the use.
    /// </summary>
    private void Jumps(Keyword keyword, KeywordSlot slot, Block block)
    {
        var detached = slot.Kind == KeywordSlotKind.DetachedBlock;
        if (!detached && !keyword.InLoop.Contains(slot.Name))
        {
            return;
        }

        new KeywordWalk((s, loops, lambda) =>
        {
            var jump = lambda ? null : s switch
            {
                ReturnStatement when detached => "return",
                BreakStatement when loops == 0 => "break",
                ContinueStatement when loops == 0 => "continue",
                _ => null,
            };
            if (jump is not null)
            {
                Report(ErrorIds.KeywordJump, s.Line, detached
                    ? $"`{jump}` cannot leave the block of `{keyword.Word.Text}`, which its template runs apart (J6)"
                    : $"`{jump}` in the block of `{keyword.Word.Text}` would act on a loop of its template, and not on the loop around the use (J6)");
            }
        }).Statements(block.Statements);
    }

    /// <summary>
    /// J4, J7: the tokens of a template as they stand at a use: each on the use's line, and each name the template declares or a slot
    /// names renamed, but a member's after <c>.</c> or <c>?.</c>, a parameter's before <c>:</c> in a call, and a field's before
    /// <c>=</c> in the braces of <c>with</c>.
    /// </summary>
    private static List<Token> Renamed(IReadOnlyList<Token> written, Dictionary<string, string> renames, int line)
    {
        var stood = new List<Token>(written.Count);
        var withs = new Stack<bool>(); // for each open brace, whether it is the brace of a `with`
        for (var i = 0; i < written.Count; i++)
        {
            var token = written[i];
            if (token.Kind == TokenKind.LeftBrace)
            {
                withs.Push(i > 0 && written[i - 1].IsKeyword("with"));
            }
            else if (token.Kind == TokenKind.RightBrace && withs.Count > 0)
            {
                withs.Pop();
            }
            else if (token.Kind == TokenKind.Name && renames.TryGetValue(token.Text, out var renamed) && !NamedByItsPlace(written, i, withs.Count > 0 && withs.Peek()))
            {
                token = token with { Text = renamed };
            }

            stood.Add(token with { Line = line });
        }

        return stood;
    }

    private static bool NamedByItsPlace(IReadOnlyList<Token> written, int i, bool inWith)
    {
        var before = i - 1;
        while (before >= 0 && written[before].Kind == TokenKind.Newline)
        {
            before--;
        }

        var after = i + 1;
        while (after < written.Count && written[after].Kind == TokenKind.Newline)
        {
            after++;
        }

        var previous = before >= 0 ? written[before].Kind : TokenKind.Newline;
        var next = after < written.Count ? written[after].Kind : TokenKind.EndOfFile;
        return previous is TokenKind.Dot or TokenKind.QuestionDot
            || (next == TokenKind.Colon && previous is TokenKind.LeftParen or TokenKind.Comma)
            || (inWith && next == TokenKind.Assign && previous is TokenKind.LeftBrace or TokenKind.Comma);
    }
}
