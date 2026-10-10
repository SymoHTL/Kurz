using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The parser against chapters 1, 2, 5, 7, 8 and 10 of the reference. A tree is pinned as the one
/// line <see cref="SyntaxPrinter"/> writes for it; an error by its id and its line (E2).
/// </summary>
public sealed class ParserTests
{
    private static (ProgramSyntax Program, DiagnosticBag Diagnostics) Parse(string program)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        return (tree, diagnostics);
    }

    /// <summary>The printed tree of a program that parses without a diagnostic.</summary>
    private static string Tree(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        return SyntaxPrinter.Print(tree);
    }

    private static List<(string Id, int Line)> Errors(string program) =>
        Parse(program).Diagnostics.Items.Select(d => (d.Id, d.Line)).ToList();

    [Theory]
    [InlineData("x = 4\nprint(x + 5)", "(assign-or-declare x (int 4)) (call print (+ x (int 5)))")]
    [InlineData("mut x = 4", "(mut-declare x (int 4))")]
    [InlineData("int x = 4", "(declare int x (int 4))")]
    [InlineData("mut int x = 4", "(mut-declare int x (int 4))")]
    [InlineData("mut Node? node = Node(1, null)", "(mut-declare Node? node (call Node (int 1), null))")]
    [InlineData("string? other = null", "(declare string? other null)")]
    [InlineData("int | NotFound result = Find(1)", "(declare int | NotFound result (call Find (int 1)))")]
    [InlineData("mut xs = List<int>()", "(mut-declare xs (call List<int>))")]
    [InlineData("c = Club(List<weak Person?>())", "(assign-or-declare c (call Club (call List<weak Person?>)))")]
    public void A_declaration_or_an_assignment_takes_its_form(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("int Add(int a, int b) =>\n    a + b", "(function int Add(int a, int b) => (+ a b))")]
    [InlineData("total = 1 +\n    2 +\n    3", "(assign-or-declare total (+ (+ (int 1) (int 2)) (int 3)))")]
    [InlineData("count = xs\n    .Where(x => x > 1)\n    .Count", "(assign-or-declare count (. (call (. xs Where) (lambda (x) (> x (int 1)))) Count))")]
    [InlineData("print(Add(\n    total,\n    total\n))", "(call print (call Add total, total))")]
    [InlineData("x =\n    5", "(assign-or-declare x (int 5))")]
    [InlineData("big = xs.Where(x => {\n    total = total + x\n    return x > 0\n})", "(assign-or-declare big (call (. xs Where) (lambda (x) {(assign-or-declare total (+ total x)) (return (> x (int 0)))})))")]
    public void A_statement_continues_on_the_next_line_only_as_l4_says(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("print(1 + 2 * 3)", "(call print (+ (int 1) (* (int 2) (int 3))))")]
    [InlineData("print(-7 / 2)", "(call print (/ (- (int 7)) (int 2)))")]
    [InlineData("print(!(1 == 1) || 3 >= 3)", "(call print (|| (! (paren (== (int 1) (int 1)))) (>= (int 3) (int 3))))")]
    [InlineData("print(1 < 2 && 2 < 3)", "(call print (&& (< (int 1) (int 2)) (< (int 2) (int 3))))")]
    [InlineData("print(6 & 3 | 1 ^ 2)", "(call print (| (& (int 6) (int 3)) (^ (int 1) (int 2))))")]
    [InlineData("print(1 << 4 + 1)", "(call print (<< (int 1) (+ (int 4) (int 1))))")]
    [InlineData("print(~6)", "(call print (~ (int 6)))")]
    [InlineData("print(a ?? b ?? c)", "(call print (?? a (?? b c)))")]
    [InlineData("print(n +% 1 *% 2)", "(call print (+% n (*% (int 1) (int 2))))")]
    [InlineData("print(a == b != c)", "(call print (!= (== a b) c))")]
    [InlineData("print(1 < 2 == true)", "(call print (== (< (int 1) (int 2)) true))")]
    public void Operators_have_the_precedence_of_c_sharp(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("for i in 1..3 {\n    print(i)\n}", "(for i in (.. (int 1) (int 3)) {(call print i)})")]
    [InlineData("for i in 0..<xs.Count {\n    print(xs[i])\n}", "(for i in (..< (int 0) (. xs Count)) {(call print (index xs i))})")]
    [InlineData("for i in a..<n {\n}", "(for i in (..< a n) {})")]
    public void A_range_has_two_ends(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void If_else_if_and_else_follow_the_closing_brace()
    {
        var program = "int Sign(int n) {\n    if n > 0 {\n        return 1\n    } else if n < 0 {\n        return -1\n    } else {\n        return 0\n    }\n}";
        Assert.Equal(
            "(function int Sign(int n) {(if (> n (int 0)) {(return (int 1))} else (if (< n (int 0)) {(return (- (int 1)))} else {(return (int 0))}))})",
            Tree(program));
    }

    [Fact]
    public void A_condition_in_parentheses_is_an_expression_like_any_other()
    {
        Assert.Equal("(if (paren (> x (int 5))) {(call print (string \"big\"))})", Tree("if (x > 5) {\n    print(\"big\")\n}"));
    }

    [Theory]
    [InlineData("x = 7\nif x > 5 print(x)", 2, "(assign-or-declare x (int 7)) (if (> x (int 5)) {(call print x)})")]
    [InlineData("x = 7\nif x > 5\n{\n    print(\"big\")\n}", 2, "(assign-or-declare x (int 7)) (if (> x (int 5)) {(call print (string \"big\"))})")]
    [InlineData("while i < 3\n{\n    i = i + 1\n}", 1, "(while (< i (int 3)) {(assign-or-declare i (+ i (int 1)))})")]
    [InlineData("if x {\n    print(1)\n} else print(2)", 3, "(if x {(call print (int 1))} else {(call print (int 2))})")]
    [InlineData("if a &&\n    b\n{\n    print(1)\n}", 2, "(if (&& a b) {(call print (int 1))})")]
    public void A_block_that_does_not_open_on_the_line_of_the_condition_is_braces_required_at_that_line(string program, int line, string tree)
    {
        var (parsed, diagnostics) = Parse(program);
        Assert.Equal([(ErrorIds.BracesRequired, line)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(tree, SyntaxPrinter.Print(parsed));
    }

    [Fact]
    public void An_else_on_a_line_of_its_own_is_syntax_at_its_line_and_the_branch_still_parses()
    {
        var (tree, diagnostics) = Parse("if a {\n    print(1)\n}\nelse {\n    print(2)\n}");
        Assert.Equal([(ErrorIds.Syntax, 4)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(if a {(call print (int 1))} else {(call print (int 2))})", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void The_three_part_for_is_one_syntax_error_and_the_lines_after_it_parse()
    {
        var (tree, diagnostics) = Parse("for (i = 0; i < 3; i++) {\n    print(i)\n}\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_semicolon_between_two_statements_is_semicolon_and_both_statements_parse()
    {
        var (tree, diagnostics) = Parse("a = 1\nprint(a); print(a + 1)");
        Assert.Equal([(ErrorIds.Semicolon, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(assign-or-declare a (int 1)) (call print a) (call print (+ a (int 1)))", SyntaxPrinter.Print(tree));
    }

    [Theory]
    [InlineData(";\nprint(1)", "(call print (int 1))")]
    [InlineData("a = 1;;", "(assign-or-declare a (int 1))")]
    [InlineData("a = 1; ; print(a)", "(assign-or-declare a (int 1)) (call print a)")]
    public void A_semicolon_where_a_statement_starts_or_a_run_of_them_is_one_semicolon_error(string program, string tree)
    {
        var (parsed, diagnostics) = Parse(program);
        Assert.Equal([(ErrorIds.Semicolon, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(tree, SyntaxPrinter.Print(parsed));
    }

    [Theory]
    [InlineData("equal = 1\nprint(equal)", 1)]
    [InlineData("int = 1\nprint(1)", 1)]
    [InlineData("data Job(int match)\n\nprint(Job(1) == Job(2))", 1)]
    [InlineData("x = 1\nmut while = 2", 2)]
    [InlineData("if = 1", 1)]
    [InlineData("match = 1", 1)]
    [InlineData("mut = 1", 1)]
    [InlineData("data = 1", 1)]
    [InlineData("class = 1", 1)]
    [InlineData("x = 2\nwhile = x", 2)]
    public void A_core_word_as_a_name_is_reserved_word_at_its_line(string program, int line)
    {
        Assert.Equal([(ErrorIds.ReservedWord, line)], Errors(program));
    }

    [Fact]
    public void A_core_word_that_begins_no_statement_is_read_as_a_name_where_an_expression_stands()
    {
        // the front end reports nothing: whether the name is declared is the checker's to say (L12 against E1, a round's question)
        Assert.Equal("(call print equal)", Tree("print(equal)"));
    }

    [Theory]
    [InlineData("print(\"{a $ b}\")")]
    [InlineData("print(\"{zähler}\")")]
    [InlineData("print((\"{a $ b}\"))")]
    public void A_bad_token_inside_an_interpolation_is_one_syntax_error(string program)
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Fact]
    public void A_control_word_where_an_expression_stands_is_syntax_even_after_a_variable_of_that_name()
    {
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.Syntax, 2)], Errors("if = 1\nprint(if)"));
    }

    /// <summary>A declaration whose type is a user type is parsed speculatively; the errors inside it are errors all the same.</summary>
    [Theory]
    [InlineData("Job match = Job(1)", "reserved-word", 1)]
    [InlineData("Job Make(int match) => Job(match)", "reserved-word", 1)]
    [InlineData("User Find(int id) {\n    if id == 1 print(id)\n}", "braces-required", 2)]
    [InlineData("Job Make() {\n    a = 1; b = 2\n}", "semicolon", 2)]
    [InlineData("Job j = Job(\"{a $ b}\")", "syntax", 1)]
    [InlineData("Job j = Job(\"{a $ b}\")\nprint(j)", "syntax", 1)]
    public void An_error_inside_a_declaration_parsed_speculatively_is_reported_once(string program, string id, int line)
    {
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void An_error_in_the_value_of_a_declaration_is_reported_on_its_own_line_once_the_declaration_is_committed()
    {
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("Job j = Job(\n    1 +,\n    2)"));
    }

    [Fact]
    public void A_speculation_that_is_no_declaration_drops_what_it_reported()
    {
        // `Job match` reports `reserved-word` on the name, then finds neither `=` nor `(`: the statement is an
        // expression that ends too late, one `syntax`, and the dropped report does not come back
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("Job match x"));
    }

    [Fact]
    public void A_variable_named_match_stands_as_a_name_where_no_value_follows_it()
    {
        var (tree, diagnostics) = Parse("match = 1\nprint(match)\nprint(match + 1)");
        Assert.Equal([(ErrorIds.ReservedWord, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(assign-or-declare match (int 1)) (call print match) (call print (+ match (int 1)))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_variable_named_match_at_the_start_of_a_statement_is_a_name_where_no_value_follows_it()
    {
        var (tree, diagnostics) = Parse("mut match = 1\nmatch.Add(1)");
        Assert.Equal([(ErrorIds.ReservedWord, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(2, tree.Statements.Count);
        Assert.Contains("(call (. match Add) (int 1))", SyntaxPrinter.Print(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_after_a_variable_named_match_reads_as_a_match_expression()
    {
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.Syntax, 2)], Errors("match = 1\nprint(match - 1)"));
    }

    [Theory]
    [InlineData("print(1;)")]
    [InlineData("x = xs[1;]")]
    [InlineData("data ;")]
    [InlineData("print(\"{a;}\")")]
    [InlineData("x = ;")]
    [InlineData("x = 1 +;")]
    [InlineData("enum E { A; }")]
    [InlineData("void F(int a;) {\n}")]
    public void A_semicolon_inside_a_construct_is_syntax(string program)
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Theory]
    [InlineData("class A {\n    ;\n}")]
    [InlineData("match x {\n    ;\n}")]
    public void A_semicolon_where_a_member_or_an_arm_starts_is_syntax(string program)
    {
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors(program));
    }

    [Theory]
    [InlineData("if")]
    [InlineData("else")]
    [InlineData("while")]
    [InlineData("for")]
    [InlineData("in")]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("return")]
    [InlineData("throw")]
    [InlineData("data")]
    [InlineData("class")]
    [InlineData("interface")]
    [InlineData("enum")]
    [InlineData("flags")]
    [InlineData("raw")]
    [InlineData("use")]
    [InlineData("with")]
    public void A_word_that_starts_a_statement_or_a_declaration_is_syntax_where_an_expression_stands(string word)
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors($"print({word})"));
    }

    [Theory]
    [InlineData("void F()\n{\n    print(1)\n}", 1)]
    [InlineData("raw\n{\n}", 1)]
    [InlineData("if a {\n} else\n{\n}", 2)]
    [InlineData("class A {\n    A()\n    {\n    }\n}", 2)]
    [InlineData("match x\n{\n    1 => print(1)\n}", 1)]
    [InlineData("while a &&\n    b\n{\n}", 2)]
    [InlineData("for x in a +\n    b\n{\n}", 2)]
    [InlineData("int F(\n    int a)\n{\n}", 1)]
    [InlineData("class A {\n    A(\n        int x)\n    {\n    }\n}", 2)]
    [InlineData("class A\n{\n}", 1)]
    [InlineData("data P(int X)\n{\n}", 1)]
    [InlineData("interface I\n{\n}", 1)]
    [InlineData("enum E\n{\n    A\n}", 1)]
    [InlineData("flags F\n{\n    A\n}", 1)]
    [InlineData("b = a with\n{\n    X = 1\n}", 1)]
    [InlineData("void F() {\n    int G()\n    {\n    }\n}", 2)]
    [InlineData("Job F()\n{\n}", 1)]
    [InlineData("value = Find(2) else\n{\n    else => 0\n}", 1)]
    public void Braces_required_is_raised_wherever_a_block_opens_on_the_next_line(string program, int line)
    {
        Assert.Equal([(ErrorIds.BracesRequired, line)], Errors(program));
    }

    [Fact]
    public void Recovery_passes_a_block_inside_an_open_parenthesis_and_an_else_clause()
    {
        var (lambda, first) = Parse("Run(1 +, x => {\n    a = 1\n})\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 1)], first.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(lambda));
        var (branches, second) = Parse("if x + {\n    a = 1\n} else {\n    b = 2\n}\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 1)], second.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(branches));
    }

    [Fact]
    public void A_double_greater_split_by_a_speculative_type_argument_list_is_whole_again_after_the_rewind()
    {
        Assert.Equal("(assign-or-declare x (< i (>> n (int 1))))", Tree("x = i < n >> 1"));
    }

    [Fact]
    public void A_line_that_starts_with_a_conditional_member_access_continues_the_statement()
    {
        Assert.Equal("(assign-or-declare x (?. a b))", Tree("x = a\n    ?.b"));
    }

    [Fact]
    public void A_comma_continues_the_equal_by_list_on_the_next_line()
    {
        var (tree, diagnostics) = Parse("class K(int Id, int Kind) equal by Id,\n    Kind {\n}");
        Assert.Empty(diagnostics.Items);
        Assert.NotEmpty(tree.Statements);
    }

    [Theory]
    [InlineData("enum Color {\n    Red,\n    Green\n}")]
    [InlineData("flags Perm {\n    Read,\n    Write\n}")]
    [InlineData("b = a with {\n    Name = \"x\"\n}")]
    [InlineData("x = List<\n    int>()")]
    [InlineData("data Pair<\n    A, B>(A First, B Second)")]
    public void A_line_break_inside_the_braces_of_a_type_body_or_inside_angle_brackets_continues(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        Assert.NotEmpty(tree.Statements);
    }

    [Theory]
    [InlineData("class A :\n    B {\n}")]
    [InlineData("(int) =>\n    int f = null")]
    [InlineData("enum E { A, B, }")]
    [InlineData("flags F { A, B, }")]
    public void A_base_list_colon_and_a_function_type_arrow_continue_and_an_enum_body_takes_a_trailing_comma(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        Assert.NotEmpty(tree.Statements);
    }

    [Theory]
    [InlineData("b = a with { X = 1, }")]
    [InlineData("Run(by => by.Id)")]
    [InlineData("f(by: 1)")]
    public void A_trailing_comma_in_with_and_a_core_word_as_an_untyped_parameter_or_an_argument_name_are_syntax(string program)
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Fact]
    public void A_typed_lambda_parameter_that_is_a_core_word_is_reserved_word_once()
    {
        Assert.Equal([(ErrorIds.ReservedWord, 1)], Errors("f = (int by) => by"));
    }

    [Fact]
    public void A_declaration_commits_at_its_type_parameter_list_too()
    {
        // without the commit at `<` the parse would be rewound and the error reported at `Make` on line 1
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("Job Make<\n    T,,>(int a) => a"));
    }

    [Fact]
    public void A_line_that_ends_in_a_carriage_return_alone_runs_on()
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("a = 1\rb = 2"));
    }

    [Theory]
    [InlineData("f(1\n\n\n", 1)]
    [InlineData("f(\n    1\n\n", 2)]
    [InlineData("xs[1\n\n", 1)]
    [InlineData("if x {\n    print(1)\n\n", 2)]
    public void A_closer_missing_at_the_end_of_the_file_is_reported_at_the_last_line_with_code(string program, int line)
    {
        Assert.Equal([(ErrorIds.Syntax, line)], Errors(program));
    }

    [Fact]
    public void A_reserved_word_reported_before_the_commit_is_kept_when_the_committed_parse_fails()
    {
        // without the flush of the pending reports in FailStatement the reserved-word would be lost
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.Syntax, 1)], Errors("Job match = 1 +"));
    }

    [Fact]
    public void A_nested_literal_in_an_interpolation_that_ends_in_a_backslash_does_not_take_the_next_line()
    {
        Assert.Equal([(ErrorIds.Syntax, 1), (ErrorIds.Syntax, 2)], Errors("x = \"{f(\"a\\\nprint(1 +)"));
    }

    [Fact]
    public void An_else_arm_after_an_if_statement_as_an_arm_body_is_the_default_arm_not_an_else_clause()
    {
        var (tree, diagnostics) = Parse("match x {\n    1 => if y {\n        print(1)\n    }\n    else => print(2)\n}");
        Assert.Empty(diagnostics.Items);
        Assert.Single(tree.Statements);
    }

    [Theory]
    [InlineData("xs\n\n    .Count")]
    [InlineData("xs\n    // the count\n    .Count")]
    [InlineData("xs\n\n    ?.Count")]
    public void Empty_and_comment_lines_before_the_dot_of_a_chain_do_not_end_the_statement(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        Assert.Single(tree.Statements);
    }

    [Fact]
    public void Every_core_word_and_type_name_as_a_variable_name_is_reserved_word()
    {
        var words = CoreWords.Syntax.Concat(CoreWords.TypeNames).ToList();
        Assert.True(words.Count >= 50, $"{words.Count} words");
        Assert.All(words, word => Assert.Equal([(ErrorIds.ReservedWord, 1)], Errors($"{word} = 1")));
    }

    [Fact]
    public void A_protected_member_parses()
    {
        var (tree, diagnostics) = Parse("class A {\n    prot int x = 1\n}");
        Assert.Empty(diagnostics.Items);
        var type = Assert.IsType<TypeDeclaration>(Assert.Single(tree.Statements));
        var field = Assert.IsType<FieldDeclaration>(Assert.Single(type.Members));
        Assert.Equal(Modifiers.Prot, field.Modifiers);
    }

    [Fact]
    public void The_core_words_are_the_lists_of_l18_and_l19()
    {
        string[] words = ["if", "else", "match", "for", "in", "while", "break", "continue", "return", "throw", "mut", "data",
            "class", "interface", "enum", "flags", "with", "equal", "by", "pub", "prot", "static", "this", "virtual", "override",
            "weak", "raw", "use", "void", "true", "false", "null"];
        string[] types = ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal",
            "bool", "string", "char", "duration", "timestamp", "longduration", "longtimestamp"];
        Assert.Equal(words.Order(), CoreWords.Syntax.Order());
        Assert.Equal(types.Order(), CoreWords.TypeNames.Order());
    }

    [Fact]
    public void The_body_after_a_brace_on_the_next_line_still_parses()
    {
        var (tree, diagnostics) = Parse("class A\n{\n    int x = 1\n}");
        Assert.Equal([(ErrorIds.BracesRequired, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)).ToList());
        var type = Assert.IsType<TypeDeclaration>(Assert.Single(tree.Statements));
        Assert.IsType<FieldDeclaration>(Assert.Single(type.Members));
    }

    [Theory]
    [InlineData("for i in 0 ..<\n    n {\n}")]
    [InlineData("for i in 0 ..\n    n {\n}")]
    public void A_range_operator_at_the_end_of_a_line_continues_the_statement(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        Assert.Single(tree.Statements);
    }

    [Theory]
    [InlineData("while x print(1)", 1)]
    [InlineData("for x in xs print(x)", 1)]
    [InlineData("raw print(1)", 1)]
    [InlineData("class A {\n    A() print(1)\n}", 2)]
    [InlineData("while a &&\n    b print(1)", 2)]
    [InlineData("while x\nprint(1)", 1)]
    [InlineData("class A {\n    A(int x)\n}", 2)]
    public void A_statement_in_place_of_a_block_is_braces_required_at_the_line_of_the_construct(string program, int line)
    {
        Assert.Equal([(ErrorIds.BracesRequired, line)], Errors(program));
    }

    [Theory]
    [InlineData("match x print(1)")]
    [InlineData("b = a with print(1)")]
    [InlineData("enum E")]
    public void A_construct_that_takes_no_statement_in_place_of_its_braces_is_syntax(string program)
    {
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Fact]
    public void The_shapes_the_readme_lists_as_low_give_the_errors_it_says()
    {
        // pinned as they are, so that a fix to one of them shows
        Assert.Equal([(ErrorIds.BracesRequired, 1), (ErrorIds.Syntax, 1)], Errors("if x print(1) else print(2)"));
        Assert.Equal([(ErrorIds.ReservedWord, 1)], Errors("x.match(1)"));
        Assert.Empty(Errors("x.match"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("s = \"\"\"\n    {f(\"}\")}\n    \"\"\""));
        Assert.Equal([(ErrorIds.UnknownEscape, 1), (ErrorIds.Syntax, 1)], Errors("x = \"a\\"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = \"\"\"\n    a\n"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = \"\"\"\n    a\nprint(1 +)\n")); // the rest of the file goes with the block
        Assert.Equal([(ErrorIds.Syntax, 3), (ErrorIds.Syntax, 4), (ErrorIds.Syntax, 4)], Errors("class A {\n    int x = 1\nprint(1)\nprint(2)\n"));
    }

    [Fact]
    public void A_variable_named_match_before_a_bracket_is_a_name()
    {
        var (tree, diagnostics) = Parse("print(match[0])");
        Assert.Empty(diagnostics.Items);
        Assert.Single(tree.Statements);
    }

    [Fact]
    public void A_method_signature_that_ends_its_line_is_the_bodiless_form_so_a_brace_on_the_next_line_is_syntax()
    {
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("class A {\n    pub void F()\n    {\n    }\n}"));
    }

    [Fact]
    public void A_union_type_continues_after_a_bar_at_the_end_of_its_line()
    {
        Assert.Equal("(declare int | NotFound result (call Find (int 1)))", Tree("int |\n    NotFound result = Find(1)"));
    }

    [Theory]
    [InlineData("match (x) {\n    1 => print(1)\n}")]
    [InlineData("match !x {\n    true => print(1)\n}")]
    [InlineData("match ~x {\n    1 => print(1)\n}")]
    [InlineData("match 1 {\n    1 => print(1)\n}")]
    [InlineData("match \"s\" {\n    \"s\" => print(1)\n}")]
    [InlineData("match true {\n    true => print(1)\n}")]
    public void A_match_followed_by_a_token_that_can_start_a_value_is_a_match_statement(string program)
    {
        var (tree, diagnostics) = Parse(program);
        Assert.Empty(diagnostics.Items);
        Assert.IsType<MatchStatement>(Assert.Single(tree.Statements));
    }

    [Theory]
    [InlineData("by")]
    [InlineData("pub")]
    [InlineData("static")]
    [InlineData("mut")]
    [InlineData("void")]
    [InlineData("weak")]
    [InlineData("int")]
    public void A_core_word_that_starts_no_statement_is_a_name_where_an_expression_stands(string word)
    {
        Assert.Equal($"(assign-or-declare x {word})", Tree($"x = {word}"));
    }

    [Fact]
    public void An_error_inside_a_with_block_is_one_error_and_the_statement_after_it_parses()
    {
        var (tree, diagnostics) = Parse("b = a with { Name = }\nprint(1)");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 1))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void An_error_inside_a_with_block_does_not_end_the_block_around_the_statement()
    {
        var (tree, diagnostics) = Parse("void F() {\n    b = a with { Name = }\n    print(1)\n}");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Single(tree.Statements);
        Assert.Contains("(call print (int 1))", SyntaxPrinter.Print(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void A_match_statement_names_case_types_with_a_binding()
    {
        var program = "match shape {\n    Circle c => print(\"circle {c.Radius}\")\n    Square s => print(\"square {s.Side}\")\n    NotFound => print(\"none\")\n}";
        Assert.Equal(
            "(match shape [(Circle c => (call print (string \"circle \" (. c Radius)))) (Square s => (call print (string \"square \" (. s Side)))) (NotFound => (call print (string \"none\")))])",
            Tree(program));
    }

    [Fact]
    public void A_match_expression_over_literals_ends_in_else()
    {
        var program = "string Name(int n) => match n {\n    0 => \"zero\"\n    1 => \"one\"\n    else => \"many\"\n}";
        Assert.Equal(
            "(function string Name(int n) => (match-expression n [((int 0) => (string \"zero\")) ((int 1) => (string \"one\")) (else => (string \"many\"))]))",
            Tree(program));
    }

    [Fact]
    public void A_match_arm_can_name_an_enum_value_or_break()
    {
        Assert.Equal(
            "(match p [((. Plan Free) => (call print (string \"free\"))) ((. Plan Pro) => (call print (string \"pro\")))])",
            Tree("match p {\n    Plan.Free => print(\"free\")\n    Plan.Pro => print(\"pro\")\n}"));
        Assert.Equal(
            "(for i in (.. (int 1) (int 5)) {(match i [((int 3) => (break)) (else => (call print i))])})",
            Tree("for i in 1..5 {\n    match i {\n        3 => break\n        else => print(i)\n    }\n}"));
    }

    [Fact]
    public void An_arm_after_else_is_syntax_at_its_line()
    {
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("match n {\n    else => print(1)\n    0 => print(0)\n}"));
    }

    [Theory]
    [InlineData("print(xs.Where(x => x > 1).Count)", "(call print (. (call (. xs Where) (lambda (x) (> x (int 1)))) Count))")]
    [InlineData("first = () => n", "(assign-or-declare first (lambda () n))")]
    [InlineData("Run(x => print(x))", "(call Run (lambda (x) (call print x)))")]
    [InlineData("f = (int x, int y) => x + y", "(assign-or-declare f (lambda (int x, int y) (+ x y)))")]
    [InlineData("f = (x, y) => x + y", "(assign-or-declare f (lambda (x, y) (+ x y)))")]
    [InlineData("b.OnClick = () => print(b.Name)", "(assign (. b OnClick) (lambda () (call print (. b Name))))")]
    public void A_lambda_takes_its_parameters_and_its_body(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("print(First<int>(xs))", "(call print (call First<int> xs))")]
    [InlineData("p = Pair<int, string>(1, \"one\")", "(assign-or-declare p (call Pair<int, string> (int 1), (string \"one\")))")]
    [InlineData("m = Map<int, List<int>>()", "(assign-or-declare m (call Map<int, List<int>>))")]
    [InlineData("print(a < b)", "(call print (< a b))")]
    [InlineData("print(a < b, c > d)", "(call print (< a b), (> c d))")]
    public void Type_arguments_are_told_from_comparisons(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("int | NotFound Find(int id) {\n    return 10\n}", "(function int | NotFound Find(int id) {(return (int 10))})")]
    [InlineData("void | NotFound Remove(int id) {\n}", "(function void | NotFound Remove(int id) {})")]
    [InlineData("int Apply((int) => int f, int n) => f(n)", "(function int Apply((int) => int f, int n) => (call f n))")]
    [InlineData("void Run((int) => void f) {\n    f(1)\n}", "(function void Run((int) => void f) {(call f (int 1))})")]
    [InlineData("((int) => int)? g = null", "(declare ((int) => int)? g null)")]
    [InlineData("(int) => int? h = null", "(declare (int) => int? h null)")]
    [InlineData("T First<T>(List<T> items) => items[0]", "(function T First<T>(List<T> items) => (index items (int 0)))")]
    [InlineData("int AreaOf<T: Shape>(T item) => item.Area()", "(function int AreaOf<T: Shape>(T item) => (call (. item Area)))")]
    [InlineData("void Fill(mut List<int> xs) {\n    xs.Add(1)\n}", "(function void Fill(mut List<int> xs) {(call (. xs Add) (int 1))})")]
    [InlineData("void Greet(string name, string word = \"hello\") {\n}", "(function void Greet(string name, string word = (string \"hello\")) {})")]
    public void A_function_declares_its_types_first(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("data User(int Id, string Name)", "(data User(int Id, string Name))")]
    [InlineData("data Admin(int Level) : User", "(data Admin(int Level) : User)")]
    [InlineData("data Empty", "(data Empty)")]
    [InlineData("data Item(int ProductId, int Count = 1)", "(data Item(int ProductId, int Count = (int 1)))")]
    [InlineData("data Pair<A, B>(A First, B Second)", "(data Pair<A, B>(A First, B Second))")]
    [InlineData("data Tree(int Key, Tree? Left, Tree? Right)", "(data Tree(int Key, Tree? Left, Tree? Right))")]
    [InlineData("class Guest(int Number) : User(\"guest {Number}\")", "(class Guest(int Number) : User((string \"guest \" Number)))")]
    [InlineData("class User(int Id, mut string Name) equal by Id", "(class User(int Id, mut string Name) equal by Id)")]
    [InlineData("class Key(int Id, string Kind, mut int Hits) equal by Id, Kind", "(class Key(int Id, string Kind, mut int Hits) equal by Id, Kind)")]
    [InlineData("class Pet(weak Owner? Keeper)", "(class Pet(weak Owner? Keeper))")]
    [InlineData("class Button(string Name, mut () => void OnClick)", "(class Button(string Name, mut () => void OnClick))")]
    [InlineData("class Dog : Animal, Named {\n    pub string Name() => \"Rex\"\n}", "(class Dog : Animal : Named {(function pub string Name() => (string \"Rex\"))})")]
    [InlineData("enum Plan { Free, Pro }", "(enum Plan {Free, Pro})")]
    [InlineData("enum Plan { Free = 1, Pro = 2 }", "(enum Plan {Free = (int 1), Pro = (int 2)})")]
    [InlineData("flags Access { Read, Write, Run }", "(flags Access {Read, Write, Run})")]
    [InlineData("interface Shape {\n    int Area()\n    string Text()\n}", "(interface Shape {(function int Area()) (function string Text())})")]
    public void A_type_declaration_takes_its_form(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void A_class_body_holds_fields_methods_and_constructors()
    {
        var program = "class Counter {\n    mut int count = 0\n    pub static int Step = 2\n\n    pub void Add() {\n        count = count + Step\n    }\n\n    pub int Value() => count\n}";
        Assert.Equal(
            "(class Counter {(field mut int count (int 0)) (field pub static int Step (int 2)) (function pub void Add() {(assign-or-declare count (+ count Step))}) (function pub int Value() => count)})",
            Tree(program));
        Assert.Equal(
            "(class Counter(int Start) {(constructor pub Counter() this((int 0)) {}) (function pub int Value() => Start)})",
            Tree("class Counter(int Start) {\n    pub Counter() : this(0) { }\n\n    pub int Value() => Start\n}"));
        Assert.Equal(
            "(class Point {(field int x) (constructor pub Point(int value) {(assign-or-declare x value)})})",
            Tree("class Point {\n    int x\n\n    pub Point(int value) {\n        x = value\n    }\n}"));
        Assert.Equal(
            "(class Admin(int Level) : User {(function pub override string Text() => (string \"admin \" Name \" \" Level))})",
            Tree("class Admin(int Level) : User {\n    pub override string Text() => \"admin {Name} {Level}\"\n}"));
        Assert.Equal(
            "(data Counter(int Count) {(function pub mut void Increment() {(assign-or-declare Count (+ Count (int 1)))})})",
            Tree("data Counter(int Count) {\n    pub mut void Increment() {\n        Count = Count + 1\n    }\n}"));
    }

    [Theory]
    [InlineData("b = a with { Name = \"Bea\" }", "(assign-or-declare b (with a Name = (string \"Bea\")))")]
    [InlineData("value = Find(id) else {\n    NotFound => 0\n}", "(assign-or-declare value (else (call Find id) [(NotFound => (int 0))]))")]
    [InlineData("value = Find(id) else {\n    NotFound => return Missing(id)\n}", "(assign-or-declare value (else (call Find id) [(NotFound => (return (call Missing id)))]))")]
    [InlineData("value = Find(2) else {\n    NotFound => throw\n}", "(assign-or-declare value (else (call Find (int 2)) [(NotFound => (throw))]))")]
    [InlineData("Remove(1) else {\n    NotFound => print(\"none\")\n}", "(else (call Remove (int 1)) [(NotFound => (call print (string \"none\")))])")]
    [InlineData("throw \"negative\"", "(throw (string \"negative\"))")]
    [InlineData("throw ConfigMissing(\"app.json\")", "(throw (call ConfigMissing (string \"app.json\")))")]
    [InlineData("c.Home.City = \"Wien\"", "(assign (. (. c Home) City) (string \"Wien\"))")]
    [InlineData("ages[\"Ann\"] = 31", "(assign (index ages (string \"Ann\")) (int 31))")]
    [InlineData("print(Email(1)?.Bytes.Count ?? 0)", "(call print (?? (. (?. (call Email (int 1)) Bytes) Count) (int 0)))")]
    [InlineData("Fill(mut numbers)", "(call Fill mut numbers)")]
    [InlineData("Greet(\"Ann\", word: \"bye\")", "(call Greet (string \"Ann\"), word: (string \"bye\"))")]
    [InlineData("print(\"next {count + 1}\")", "(call print (string \"next \" (+ count (int 1))))")]
    [InlineData("print(\"{Counter(2)}\")", "(call print (string (call Counter (int 2))))")]
    [InlineData("Show(90min)", "(call Show (unit 90min))")]
    [InlineData("print(c == 'a')", "(call print (== c (char 97)))")]
    [InlineData("raw {\n    print(1)\n}", "(raw {(call print (int 1))})")]
    [InlineData("use net.http", "(use net.http)")]
    [InlineData("print(int(2.5))", "(call print (call int (real 2.5)))")]
    [InlineData("print(Plan.Pro.Number)", "(call print (. (. Plan Pro) Number))")]
    [InlineData("print(Access.None.Has(Access.Read))", "(call print (call (. (. Access None) Has) (. Access Read)))")]
    [InlineData("print(this.Count)", "(call print (. this Count))")]
    public void An_expression_or_a_statement_takes_its_form(string program, string tree)
    {
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("x = (1 +", 1)]
    [InlineData("print(1", 1)]
    [InlineData("a = 1\nprint(1 +)", 2)]
    [InlineData("x = 1 2", 1)]
    [InlineData("int Add(int a, int b)", 1)]
    [InlineData("class Vec(int X) {\n    pub static Vec operator +(Vec a, Vec b) => Vec(a.X + b.X)\n}", 2)]
    [InlineData("x = 1\n}", 2)]
    [InlineData("print(if)", 1)]
    public void Text_no_rule_gives_a_meaning_is_syntax_at_the_line_where_it_stops_making_sense(string program, int line)
    {
        Assert.Equal([(ErrorIds.Syntax, line)], Errors(program));
    }

    [Fact]
    public void After_an_error_the_next_line_parses_on_its_own()
    {
        var (tree, diagnostics) = Parse("print(1 +)\nprint(2)\nprint(3");
        Assert.Equal([(ErrorIds.Syntax, 1), (ErrorIds.Syntax, 3)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 2))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_statement_over_several_lines_with_an_error_is_one_error_and_the_next_statement_parses()
    {
        var (tree, diagnostics) = Parse("print(\n    1 +,\n    2\n)\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_binary_operator_reports_the_line_of_the_operator()
    {
        var (tree, _) = Parse("total = 1 +\n    2");
        var declaration = Assert.IsType<AssignOrDeclare>(tree.Statements[0]);
        var sum = Assert.IsType<BinaryExpression>(declaration.Value);
        Assert.Equal(1, sum.Line);
        Assert.Equal(2, sum.Right.Line);
    }

    [Fact]
    public void An_argument_carries_its_own_line()
    {
        var (tree, _) = Parse("print(Add(\n    total,\n    other\n))");
        var call = Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(tree.Statements[0]).Expression);
        var inner = Assert.IsType<CallExpression>(call.Arguments[0].Value);
        Assert.Equal([2, 3], inner.Arguments.Select(a => a.Line));
        Assert.Equal(1, call.Line);
    }
}
