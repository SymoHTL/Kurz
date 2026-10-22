using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The parser against chapters 1 to 12 of the reference: the forms of chapters 1 to 11, the ids and
/// lines of chapter 12. A tree is pinned as the one
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
        // V1: `x = 4` declares, and V8: the same line assigns where `x` exists, so the tree keeps both readings; V2: `mut`; V3 and V4:
        // a written type, with `mut` before it; N1: `Node?`; D9: a union type; T17: type arguments as in C#; R5: `weak Person?`
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
        // L4: a statement continues after `=>`, a binary operator or `=`, inside an open `(`, and before a line that starts with `.`;
        // a lambda's block (F7) holds statements of its own
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
    [InlineData("print(1..n + 1)", "(call print (.. (int 1) (+ n (int 1))))")]
    [InlineData("print(1..n << 1)", "(call print (.. (int 1) (<< n (int 1))))")]
    [InlineData("print(a < 1..n)", "(call print (< a (.. (int 1) n)))")]
    [InlineData("print(1..n == r)", "(call print (== (.. (int 1) n) r))")]
    public void Operators_bind_as_t18_t22_t12_and_n4_say(string program, string tree)
    {
        // T18, T22 and T12: "the precedence they have in C#", `+%` and `-%` with `+` and `-`, `*%` with `*`; N4: `??` as in C#;
        // where a range binds is a reading (README), pinned by the last four cases: below `+ - << >>`, above the comparisons
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("for i in 1..3 {\n    print(i)\n}", "(for i in (.. (int 1) (int 3)) {(call print i)})")]
    [InlineData("for i in 0..<xs.Count {\n    print(xs[i])\n}", "(for i in (..< (int 0) (. xs Count)) {(call print (index xs i))})")]
    [InlineData("for i in a..<n {\n}", "(for i in (..< a n) {})")]
    public void A_range_has_two_ends(string program, string tree)
    {
        // C8: `a..b` and `a..<b` in a `for ... in`, which C6 writes; M9: `xs.Count` and `xs[i]`
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void If_else_if_and_else_follow_the_closing_brace()
    {
        // C1: `if` with its block; C2: `else` and `else if` after the closing brace on its line; F3: a block body with `return`
        var program = "int Sign(int n) {\n    if n > 0 {\n        return 1\n    } else if n < 0 {\n        return -1\n    } else {\n        return 0\n    }\n}";
        Assert.Equal(
            "(function int Sign(int n) {(if (> n (int 0)) {(return (int 1))} else (if (< n (int 0)) {(return (- (int 1)))} else {(return (int 0))}))})",
            Tree(program));
    }

    [Fact]
    public void A_condition_in_parentheses_is_an_expression_like_any_other()
    {
        // C1: the condition stands without parentheses, and a parenthesized one is a parenthesized expression, kept in the tree
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
        // C1: the block opens on the line where the condition ends, so a `{` on the next line or a statement in its place is
        // `braces-required` there, at the condition's last line when it runs over lines; C2: the same for the block of `else`; C5: `while`
        var (parsed, diagnostics) = Parse(program);
        Assert.Equal([(ErrorIds.BracesRequired, line)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal(tree, SyntaxPrinter.Print(parsed));
    }

    [Fact]
    public void An_else_on_a_line_of_its_own_is_syntax_at_its_line_and_the_branch_still_parses()
    {
        // C2: `else` follows the closing brace on its line; one on a line of its own is `syntax`, which E4 lists (decided), and the
        // branch parses so that the one error stands
        var (tree, diagnostics) = Parse("if a {\n    print(1)\n}\nelse {\n    print(2)\n}");
        Assert.Equal([(ErrorIds.Syntax, 4)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(if a {(call print (int 1))} else {(call print (int 2))})", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void The_three_part_for_is_one_syntax_error_and_the_lines_after_it_parse()
    {
        // C8: there is no three-part `for`; E4 lists it as `syntax`; E1: one error, and the next statement parses
        var (tree, diagnostics) = Parse("for (i = 0; i < 3; i++) {\n    print(i)\n}\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_semicolon_between_two_statements_is_semicolon_and_both_statements_parse()
    {
        // L5 (decided): a `;` that ends a statement is `semicolon` at its line, and both statements parse
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
        // L5 (decided): a `;` where a statement starts, or a run of them, is one `semicolon`
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
        // L12: a core word (L18) or a built-in type's name (L19) as a declared name is `reserved-word` at the declaration's line; D1: a
        // field's name too. V8: `while = x` on line 2 is the error again, since the parser cannot know that it assigns
        Assert.Equal([(ErrorIds.ReservedWord, line)], Errors(program));
    }

    [Fact]
    public void An_undeclared_use_of_a_core_word_outside_the_seventeen_gets_no_front_end_error()
    {
        // the reading on L12 (README; a question for a design round): the front end reports nothing for a use, so that a declared
        // core word is used without a second error (E1); whether this `equal` is declared is the checker's to say
        Assert.Equal("(call print equal)", Tree("print(equal)"));
    }

    [Theory]
    [InlineData("print(\"{a $ b}\")")]
    [InlineData("print(\"{zähler}\")")]
    [InlineData("print((\"{a $ b}\"))")]
    public void A_bad_token_inside_an_interpolation_is_one_syntax_error(string program)
    {
        // L6: a hole holds an expression, read by the parser from the lexer's text; E1: a bad token in it is the literal's one `syntax`
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Fact]
    public void A_control_word_where_an_expression_stands_is_syntax_even_after_a_variable_of_that_name()
    {
        // L12: the declaration is `reserved-word`; `if` where an expression stands begins no statement form, so it is `syntax` (E4) at
        // its line, whatever was declared
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.Syntax, 2)], Errors("if = 1\nprint(if)"));
    }

    /// <summary>A declaration whose type is a user type is parsed speculatively (recovery, README); the errors inside it are errors all the same, reported once (E1).</summary>
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
        // E1 with E2: the error carries the line of the token that stops making sense, inside the open `(` on line 2
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("Job j = Job(\n    1 +,\n    2)"));
    }

    [Fact]
    public void A_modifier_other_than_mut_on_a_variable_is_syntax()
    {
        // E4's general clause, as the front end reads it (the readings in compiler/README.md): V2 and V4 give a variable `mut` and nothing
        // else, so `pub` begins no declaration and the statement ends at it; the reference does not settle which id this takes, and the
        // README lists it among the questions for a design round, with the doubled modifier
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("pub x = 1\nprint(x)"));
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
        // L12: `-` can start a value, so this is a match expression (L12 records the owner's choice of 2026-10-10), and its missing
        // arms are C1's error
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.BracesRequired, 2)], Errors("match = 1\nprint(match - 1)"));
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
        // L5 (decided): a `;` inside parentheses, brackets or an interpolation, or where an operand, a name, a member, a parameter or
        // an arm is expected, is `syntax` (E4), not `semicolon`
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Theory]
    [InlineData("class A {\n    ;\n}")]
    [InlineData("match x {\n    ;\n}")]
    public void A_semicolon_where_a_member_or_an_arm_starts_is_syntax(string program)
    {
        // L5 (decided): where a member or an arm is expected the `;` is `syntax` (E4) at its line
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
    public void Each_of_the_seventeen_statement_words_is_syntax_where_an_expression_stands(string word)
    {
        // L12 (decided): a core word that begins a statement form is `syntax` (E4) where an expression stands, `print(if)` the record's
        // example; the seventeen are the words of L18 that begin or continue a statement form, `with` among them
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors($"print({word})"));
    }

    [Fact]
    public void An_else_after_what_is_no_call_is_syntax()
    {
        // O5 gives the postfix `else` to a call: after anything else it continues nothing, and the statement ends before it, text no rule
        // gives a meaning (E4; the reading in compiler/README.md)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = 5 else {\n    else => 0\n}\nprint(x)"));
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
    [InlineData("print(match x\n{\n    1 => 2\n})", 1)]
    [InlineData("b = f(a with\n{ X = 1 })", 1)]
    [InlineData("b = a with\n{\n    X = 1\n}", 1)]
    [InlineData("void F() {\n    int G()\n    {\n    }\n}", 2)]
    [InlineData("Job F()\n{\n}", 1)]
    [InlineData("value = Find(2) else\n{\n    else => 0\n}", 1)]
    public void Braces_required_is_raised_wherever_a_block_opens_on_the_next_line(string program, int line)
    {
        // C1 names the line for each construct: the last line of the header of a `while` or a `for`, and of an `if`'s condition; the
        // line of the `match` or of the call an `else` follows; the line of the word `else` or `raw`; the line of the name or the word
        // for a type body or the braces of `with`; and the first line of the signature for a function outside a type body or a
        // constructor (C1 records the owner's choices of 2026-10-10 and 2026-10-11)
        Assert.Equal([(ErrorIds.BracesRequired, line)], Errors(program));
    }

    [Fact]
    public void Recovery_passes_a_block_inside_an_open_parenthesis_and_an_else_clause()
    {
        // recovery under no rule (README): after an error the rest of the statement is given up, the blocks inside it with their lines,
        // so that the next statement parses (E1 per text)
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
        // T17 with T22: `>>` is one operator; the speculative type-argument list (the reading on T19, README) splits it and the rewind
        // joins it again
        Assert.Equal("(assign-or-declare x (< i (>> n (int 1))))", Tree("x = i < n >> 1"));
    }

    [Fact]
    public void A_line_that_starts_with_a_conditional_member_access_continues_the_statement()
    {
        // L4: a line that starts with `?.` continues the statement as one that starts with `.` does (L4 records the owner's choice
        // of 2026-10-10)
        Assert.Equal("(assign-or-declare x (?. a b))", Tree("x = a\n    ?.b"));
    }

    [Fact]
    public void A_comma_continues_the_equal_by_list_on_the_next_line()
    {
        // L4: a line that ends in a comma continues; K8 and K15: `equal by Id, Kind` names the fields that count
        Assert.Equal("(class K(int Id, int Kind) equal by Id, Kind {})", Tree("class K(int Id, int Kind) equal by Id,\n    Kind {\n}"));
    }

    [Theory]
    [InlineData("enum Color {\n    Red,\n    Green\n}")]
    [InlineData("flags Perm {\n    Read,\n    Write\n}")]
    [InlineData("b = a with {\n    Name = \"x\"\n}")]
    [InlineData("x = List<\n    int>()")]
    [InlineData("data Pair<\n    A, B>(A First, B Second)")]
    public void A_line_break_inside_the_braces_of_a_type_body_or_inside_angle_brackets_continues(string program)
    {
        // L4: a line break inside the braces of an enum, flags or with body and inside the `< >` of type arguments or type
        // parameters does not end the statement (L4 records the owner's choice of 2026-10-10)
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
        // L4 (decided): a line break after the `:` of a base list continues the declaration, a line that ends in `=>` continues, and
        // "a trailing comma is accepted in an `enum` or `flags` body" (L4 records the owner's choice of 2026-10-10)
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
        // L4 (decided): a trailing comma is "refused in the braces of `with`, where it is `syntax` (E4)" (L4 records the owner's
        // choice of 2026-10-10); L12 (decided): a core word as an untyped lambda parameter or as the name of a named argument is `syntax`
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors(program));
    }

    [Fact]
    public void A_typed_lambda_parameter_that_is_a_core_word_is_reserved_word_once()
    {
        // L12: a typed lambda parameter (F7) is a declaration, so its core word is `reserved-word` once, and the use adds none (E1)
        Assert.Equal([(ErrorIds.ReservedWord, 1)], Errors("f = (int by) => by"));
    }

    [Fact]
    public void A_declaration_commits_at_its_type_parameter_list_too()
    {
        // T19: `Make<T>` declares a generic function, so `<` after a name commits the speculative declaration (recovery, README);
        // without the commit at `<` the parse would be rewound and the error reported at `Make` on line 1
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("Job Make<\n    T,,>(int a) => a"));
    }

    [Fact]
    public void A_line_that_ends_in_a_carriage_return_alone_runs_on()
    {
        // L13: a lone carriage return is whitespace outside a literal (L13 records the owner's choice of 2026-10-10), so both statements
        // stand on line 1, and the second is text the first cannot take (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("a = 1\rb = 2"));
    }

    [Theory]
    [InlineData("f(1\n\n\n", 1)]
    [InlineData("f(\n    1\n\n", 2)]
    [InlineData("xs[1\n\n", 1)]
    [InlineData("if x {\n    print(1)\n\n", 2)]
    public void A_closer_missing_at_the_end_of_the_file_is_reported_at_the_last_line_with_code(string program, int line)
    {
        // E4: a `)`, `]` or `}` missing at the end of the file is reported at the last line that holds code (E4 records the owner's
        // choice of 2026-10-10)
        Assert.Equal([(ErrorIds.Syntax, line)], Errors(program));
    }

    [Fact]
    public void A_reserved_word_reported_before_the_commit_is_kept_when_the_committed_parse_fails()
    {
        // L12 and E4, one error each (E1 per text): without the flush of the pending reports in FailStatement the `reserved-word`
        // would be lost
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.Syntax, 1)], Errors("Job match = 1 +"));
    }

    [Fact]
    public void A_nested_literal_in_an_interpolation_that_ends_in_a_backslash_does_not_take_the_next_line()
    {
        // L6: a literal inside a hole ends on its line as the outer one does; L16: its unfinished escape is the first line's error, and
        // the second line is read on its own (E1 per text)
        Assert.Equal([(ErrorIds.Syntax, 1), (ErrorIds.Syntax, 2)], Errors("x = \"{f(\"a\\\nprint(1 +)"));
    }

    [Fact]
    public void An_else_arm_after_an_if_statement_as_an_arm_body_is_the_default_arm_not_an_else_clause()
    {
        // C9: the last arm can be `else`; C2: an `else` clause follows the closing brace on its line, so the `else` on the next line is
        // the arm, not a clause of the `if` that is the arm's body (C4)
        Assert.Equal(
            "(match x [((int 1) => (if y {(call print (int 1))})) (else => (call print (int 2)))])",
            Tree("match x {\n    1 => if y {\n        print(1)\n    }\n    else => print(2)\n}"));
    }

    [Theory]
    [InlineData("xs\n\n    .Count", "(. xs Count)")]
    [InlineData("xs\n    // the count\n    .Count", "(. xs Count)")]
    [InlineData("xs\n\n    ?.Count", "(?. xs Count)")]
    public void Empty_and_comment_lines_before_the_dot_of_a_chain_do_not_end_the_statement(string program, string tree)
    {
        // L4: a line that starts with `.` or `?.` continues the statement, "with empty lines and comment lines allowed between"
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void Every_core_word_and_type_name_as_a_variable_name_is_reserved_word()
    {
        // L12 over the whole of L18 and L19: every word of both lists as a declared name is `reserved-word`; the floor keeps the lists from
        // being read as empty
        var words = CoreWords.Syntax.Concat(CoreWords.TypeNames).ToList();
        Assert.True(words.Count >= 50, $"{words.Count} words");
        Assert.All(words, word => Assert.Equal([(ErrorIds.ReservedWord, 1)], Errors($"{word} = 1")));
    }

    [Fact]
    public void A_protected_member_parses()
    {
        // F9: `prot` exposes a member to inheriting types; K7: a field in a class body, with its modifier kept for the checker
        var (tree, diagnostics) = Parse("class A {\n    prot int x = 1\n}");
        Assert.Empty(diagnostics.Items);
        var type = Assert.IsType<TypeDeclaration>(Assert.Single(tree.Statements));
        var field = Assert.IsType<FieldDeclaration>(Assert.Single(type.Members!));
        Assert.Equal(Modifiers.Prot, field.Modifiers);
    }

    [Fact]
    public void The_core_words_are_the_lists_of_l18_and_l19()
    {
        // L18 and L19, word for word: the lists are closed, so the sets are pinned whole
        string[] words = ["if", "else", "match", "for", "in", "while", "break", "continue", "return", "throw", "mut", "data",
            "class", "interface", "enum", "flags", "with", "equal", "by", "pub", "prot", "static", "this", "virtual", "override",
            "weak", "raw", "use", "void", "true", "false", "null", "asm", "naked", "extern", "Ptr", "SizeOf"];
        string[] types = ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal",
            "bool", "string", "char", "duration", "timestamp", "longduration", "longtimestamp"];
        Assert.Equal(words.Order(), CoreWords.Syntax.Order());
        Assert.Equal(types.Order(), CoreWords.TypeNames.Order());
    }

    [Fact]
    public void The_body_after_a_brace_on_the_next_line_still_parses()
    {
        // C1: a type body's `{` on the next line is `braces-required` at the line of the name; the body is read all the same (E1, one error)
        var (tree, diagnostics) = Parse("class A\n{\n    int x = 1\n}");
        Assert.Equal([(ErrorIds.BracesRequired, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)).ToList());
        var type = Assert.IsType<TypeDeclaration>(Assert.Single(tree.Statements));
        Assert.IsType<FieldDeclaration>(Assert.Single(type.Members!));
    }

    [Theory]
    [InlineData("for i in 0 ..<\n    n {\n}")]
    [InlineData("for i in 0 ..\n    n {\n}")]
    public void A_range_operator_at_the_end_of_a_line_continues_the_statement(string program)
    {
        // L4 (decided): a line that ends in `..` or `..<` continues, as one that ends in a binary operator does
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
    public void A_statement_in_place_of_a_block_is_braces_required_at_the_line_of_the_construct(string program, int line)
    {
        // C1: the block is missing where it should open, `braces-required` at the construct's line (the last line of a header that
        // runs over lines); recovery (README): the statement in its place is read as the block's one statement, and `while x` with
        // its statement on the next line is the same error with an empty block
        Assert.Equal([(ErrorIds.BracesRequired, line)], Errors(program));
    }

    [Theory]
    [InlineData("match x print(1)", 1)]
    [InlineData("b = a with print(1)", 1)]
    [InlineData("enum E", 1)]
    [InlineData("match x\nprint(1)", 1)]
    [InlineData("flags F\nprint(1)", 1)]
    [InlineData("class A {\n    A(int x)\n}", 2)]
    [InlineData("int Add(int a, int b)", 1)]
    [InlineData("int F()\nprint(1)", 1)]
    public void Braces_that_are_missing_altogether_are_braces_required_at_the_line_of_the_construct(string program, int line)
    {
        // C1: a block that is missing is an error of the whole construct, at the line where it should have opened (C1 records the
        // owner's choice of 2026-10-10); a constructor's body and the body of a function outside a type body are C1's too, and a
        // function signature at the top level that ends its line is no bodiless form (K7: that form is an interface member's)
        Assert.Equal([(ErrorIds.BracesRequired, line)], Errors(program));
    }

    [Fact]
    public void The_shapes_the_readme_lists_as_low_give_the_errors_it_says()
    {
        // pinned as they are, so that a fix to one of them shows
        Assert.Equal([(ErrorIds.BracesRequired, 1), (ErrorIds.BracesRequired, 1)], Errors("if x print(1) else print(2)"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("s = \"\"\"\n    {f(\"}\")}\n    \"\"\""));
        Assert.Equal([(ErrorIds.ReservedWord, 1), (ErrorIds.ReservedWord, 2)], Errors("equal = 1\nequal = 2"));
        Assert.Equal([(ErrorIds.BracesRequired, 1), (ErrorIds.BracesRequired, 4)], Errors("if x\n{\n}\nelse if y\n{\n}"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = \"\"\"\n    a\n"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = \"\"\"\n    a\nprint(1 +)\n")); // the rest of the file goes with the block
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("x = \"\"\"\n    a\"\"\"\nprint(1 +)\n")); // so does the rest after a closing quote inside a content line with no closing line after it
        Assert.Equal([(ErrorIds.Syntax, 3), (ErrorIds.Syntax, 4), (ErrorIds.Syntax, 4)], Errors("class A {\n    int x = 1\nprint(1)\nprint(2)\n"));
    }

    [Fact]
    public void A_line_that_starts_with_a_fraction_literal_starts_a_statement_and_continues_nothing()
    {
        // L4, L11: `.` directly followed by a digit is a literal, not the `.` that continues a statement (L4 records the owner's
        // choice of 2026-10-11)
        var (tree, diagnostics) = Parse("x = 1\n.5");
        Assert.Empty(diagnostics.Items);
        Assert.Equal(2, tree.Statements.Count);
        var (tree2, diagnostics2) = Parse("x = 1\n.ToString()");
        Assert.Empty(diagnostics2.Items);
        Assert.Single(tree2.Statements);
    }

    [Fact]
    public void A_variable_named_match_before_a_bracket_is_a_name()
    {
        // L12: `match` starts a match when a token that can start a value follows it; no value of this reference starts with `[`
        // (M9 builds a list with `List<int>()`), so this is the name, indexed
        var (tree, diagnostics) = Parse("print(match[0])");
        Assert.Empty(diagnostics.Items);
        Assert.Single(tree.Statements);
    }

    [Fact]
    public void A_method_signature_that_ends_its_line_is_the_bodiless_form_so_a_brace_on_the_next_line_is_syntax()
    {
        // C1, last sentence, which records the owner's choice of 2026-10-10: "A method signature inside a type body that ends its line
        // is the bodiless form of an interface member (K7), so its `{` on the next line is `syntax` (E4)"
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("class A {\n    pub void F()\n    {\n    }\n}"));
    }

    [Fact]
    public void A_union_type_continues_after_a_bar_at_the_end_of_its_line()
    {
        // L4 (decided): a line that ends in `|` continues the type; D9: the union
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
        // L12 (decided): `match` starts a match when a token that can start a value follows it, `(`, `!`, `~` and the literals among them
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
    public void A_core_word_outside_the_seventeen_statement_words_is_a_name_where_an_expression_stands(string word)
    {
        // a reading of L12 against E1 (README; a question for a design round): the seventeen words that begin a statement form,
        // pinned in the theory above, are `syntax` where an expression stands; every other core word, the modifiers, `void`, `weak`
        // and the type names among them, stands as a name there, so that a declared one is used without a second error
        Assert.Equal($"(assign-or-declare x {word})", Tree($"x = {word}"));
    }

    [Fact]
    public void An_error_inside_a_with_block_is_one_error_and_the_statement_after_it_parses()
    {
        // D5: `with` and its braces; E1: one error for the statement, and the next one parses
        var (tree, diagnostics) = Parse("b = a with { Name = }\nprint(1)");
        Assert.Equal([(ErrorIds.Syntax, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 1))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void An_error_inside_a_with_block_does_not_end_the_block_around_the_statement()
    {
        // recovery (README): the failed statement gives up its own braces, not those of the block around it, so the function's body goes on
        var (tree, diagnostics) = Parse("void F() {\n    b = a with { Name = }\n    print(1)\n}");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Single(tree.Statements);
        Assert.Contains("(call print (int 1))", SyntaxPrinter.Print(tree), StringComparison.Ordinal);
    }

    [Fact]
    public void A_match_statement_names_case_types_with_a_binding()
    {
        // C4: an arm names a case type, with a binding where the case carries a value and without one where it does not
        var program = "match shape {\n    Circle c => print(\"circle {c.Radius}\")\n    Square s => print(\"square {s.Side}\")\n    NotFound => print(\"none\")\n}";
        Assert.Equal(
            "(match shape [(Circle c => (call print (string \"circle \" (. c Radius)))) (Square s => (call print (string \"square \" (. s Side)))) ((type NotFound) => (call print (string \"none\")))])",
            Tree(program));
    }

    [Fact]
    public void A_match_expression_over_literals_ends_in_else()
    {
        // C9: an arm names a literal, the last arm can be `else`, and `match` is an expression, here the expression body of F2
        var program = "string Name(int n) => match n {\n    0 => \"zero\"\n    1 => \"one\"\n    else => \"many\"\n}";
        Assert.Equal(
            "(function string Name(int n) => (match-expression n [((int 0) => (string \"zero\")) ((int 1) => (string \"one\")) (else => (string \"many\"))]))",
            Tree(program));
    }

    [Fact]
    public void A_match_arm_can_name_an_enum_value_or_break()
    {
        // D12 with C9: an arm names an enum value by its path; C7: `break` as an arm's body inside a loop
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
        // C9: `else` is the last arm, so an arm after it is text no rule gives a meaning (E4) at its line
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
        // F7: lambdas as in C#, `x => ...`, `() => ...`, `(x, y) => ...` and with typed parameters; F11: a lambda assigned to a field of
        // function type
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
        // T19: a call names its type arguments, `First<int>(xs)`, T17: nested as in C#; the reading on T19 (README): the list is kept
        // only when `(` follows the `>`, so `a < b, c > d` is two comparisons
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
        // F1: the result type, the name and the typed parameters; F2 and F3: the two bodies; F4: `void`; F11: function types as parameter
        // types and the arrow's reach; O1: a union result; T19: type parameters with a limit; M7: a `mut` parameter; D11: a default value
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
    [InlineData("class Dog : Animal, Named {\n    pub string Name() => \"Rex\"\n}", "(class Dog : Animal, Named {(function pub string Name() => (string \"Rex\"))})")]
    [InlineData("enum Plan { Free, Pro }", "(enum Plan {Free, Pro})")]
    [InlineData("enum Plan { Free = 1, Pro = 2 }", "(enum Plan {Free = (int 1), Pro = (int 2)})")]
    [InlineData("flags Access { Read, Write, Run }", "(flags Access {Read, Write, Run})")]
    [InlineData("interface Shape {\n    int Area()\n    string Text()\n}", "(interface Shape {(function int Area()) (function string Text())})")]
    public void A_type_declaration_takes_its_form(string program, string tree)
    {
        // D1: `data` with its primary constructor; D7: its base; D8: `data` without fields; D11: a default value; T19: type parameters; R6: a
        // recursive `data` type; K1: `class`; K10: a base with arguments; K6: a base and interfaces; K8 and K15: `equal by`; R5: `weak T?`;
        // F11: a field of function type; D12: `enum`, D18: its numbers; D13: `flags`; K7: an interface with bodiless members
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void A_class_body_holds_fields_methods_and_constructors()
    {
        // K7: fields, methods and constructors in a class body; K14: a further constructor with `: this(...)` and a field without
        // `mut`; D16: the body of a `data` type; M6: the `mut` marker of a method after its visibility
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
    [InlineData("value = Find(id) else {\n    NotFound => 0\n}", "(assign-or-declare value (else (call Find id) [((type NotFound) => (int 0))]))")]
    [InlineData("value = Find(id) else {\n    NotFound => return Missing(id)\n}", "(assign-or-declare value (else (call Find id) [((type NotFound) => (return (call Missing id)))]))")]
    [InlineData("value = Find(2) else {\n    NotFound => throw\n}", "(assign-or-declare value (else (call Find (int 2)) [((type NotFound) => (throw))]))")]
    [InlineData("Remove(1) else {\n    NotFound => print(\"none\")\n}", "(else (call Remove (int 1)) [((type NotFound) => (call print (string \"none\")))])")]
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
        // D5: `with`; O5: the postfix `else` on a call, its arms with a value, a `return` or a bare `throw`; O7: `throw` with a string or a
        // value; M4: assignment into a path and into an index (M9); N5 and N4: `?.` and `??`; M7: `mut` before an argument; F12: an
        // argument by name; L6: holes; L14: a unit literal; T21: a `char` against a `char`; R7: `raw`; F10: `use`; T10: a conversion as a
        // call of the type; D18: `.Number`; D17: `.Has`; K7: `this`
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("x = (1 +", 1)]
    [InlineData("print(1", 1)]
    [InlineData("a = 1\nprint(1 +)", 2)]
    [InlineData("x = 1 2", 1)]
    [InlineData("class Vec(int X) {\n    pub static Vec operator +(Vec a, Vec b) => Vec(a.X + b.X)\n}", 2)]
    [InlineData("x = 1\n}", 2)]
    [InlineData("print(if)", 1)]
    public void Text_no_rule_gives_a_meaning_is_syntax_at_the_line_where_it_stops_making_sense(string program, int line)
    {
        // E4: an unclosed `(` at the end of the file, a token after a complete statement, a `}` that closes nothing, `print(if)`
        // (L12); K5: "There are no operator declarations ... a declaration of one is `syntax` (E4)"; E2: at the line where the text
        // stops making sense
        Assert.Equal([(ErrorIds.Syntax, line)], Errors(program));
    }

    [Fact]
    public void After_an_error_the_next_line_parses_on_its_own()
    {
        // E1 per text, E2: each statement with an error is one error at its line, and the statement between them parses
        var (tree, diagnostics) = Parse("print(1 +)\nprint(2)\nprint(3");
        Assert.Equal([(ErrorIds.Syntax, 1), (ErrorIds.Syntax, 3)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 2))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_statement_over_several_lines_with_an_error_is_one_error_and_the_next_statement_parses()
    {
        // E1 and E2: one error, at the line of the token that stops making sense, and the statement after the closing `)` parses
        var (tree, diagnostics) = Parse("print(\n    1 +,\n    2\n)\nprint(9)");
        Assert.Equal([(ErrorIds.Syntax, 2)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(tree));
    }

    [Fact]
    public void A_binary_operator_reports_the_line_of_the_operator()
    {
        // E2: a binary operator's node carries its operator's line, so that an error there names it; the left operand stands on the
        // line before it, and a node that took the operand's line would fail. L4: the operator at the end of the first line continues
        var (tree, _) = Parse("total = (1\n    + 2)");
        var declaration = Assert.IsType<AssignOrDeclare>(tree.Statements[0]);
        var sum = Assert.IsType<BinaryExpression>(Assert.IsType<ParenthesizedExpression>(declaration.Value).Inner);
        Assert.Equal(2, sum.Line);
        Assert.Equal(1, sum.Left.Line);
        Assert.Equal(2, sum.Right.Line);
        var (ending, _) = Parse("total = (1 +\n    2)");
        var onFirst = Assert.IsType<BinaryExpression>(Assert.IsType<ParenthesizedExpression>(Assert.IsType<AssignOrDeclare>(ending.Statements[0]).Value).Inner);
        Assert.Equal(1, onFirst.Line);
        Assert.Equal(2, onFirst.Right.Line);
    }

    [Fact]
    public void A_stray_else_without_a_block_is_one_syntax_and_the_next_statement_parses()
    {
        // C2, E4: the stray `else` is `syntax`; with no block and no `if` after it, the statement is given up under that one error (E1)
        var (after, first) = Parse("if a {\n}\nelse print(1)\nprint(2)");
        Assert.Equal([(ErrorIds.Syntax, 3)], first.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 2))", SyntaxPrinter.Print(after));
        var (alone, second) = Parse("else print(1)\nprint(2)");
        Assert.Equal([(ErrorIds.Syntax, 1)], second.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 2))", SyntaxPrinter.Print(alone));
    }

    [Fact]
    public void A_range_chains_with_nothing()
    {
        // the reading on C8 (README): a range has two ends, so a third is text no rule gives a meaning (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = 1..2..3"));
    }

    [Fact]
    public void A_function_declared_inside_a_block_parses_as_one_at_the_top_level_does()
    {
        // the reading on F1 (README): the reference places functions in a file (F5) and in a type body (K7) and says nothing of a block
        Assert.Equal("(function void F() {(function int G() => (int 1)) (call print (call G))})", Tree("void F() {\n    int G() => 1\n    print(G())\n}"));
    }

    [Fact]
    public void A_hole_with_more_than_one_expression_is_syntax_at_its_line()
    {
        // L6: a hole holds one expression; the error names the line of the token that is not part of it (E1), which is the hole's line, since a hole does not span lines
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("x = 1\nprint(\"{a b}\")"));
    }

    [Fact]
    public void A_modifier_written_twice_is_syntax()
    {
        // a reading under E4's general clause (README; a question for a design round): no rule gives a modifier written twice a
        // meaning, in a type body (K7: class bodies follow C#, which refuses it) or outside one; the id could be the checker's, and the
        // front end reports `syntax`
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("class A {\n    pub pub int x = 1\n}"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("mut mut x = 1"));
    }

    [Fact]
    public void A_field_without_a_written_type_is_a_field_with_its_modifiers()
    {
        // the reading on K7 with V2 (README; a question for a design round): V2's form, which speaks of variables, read in a type body
        // as a field without a written type; K7 shows a field as `Type name`, and the record's actor bodies write
        // `mut users = Map<int, User>()`
        Assert.Equal("(class A {(field mut count (int 0)) (field pub mut total (int 1))})", Tree("class A {\n    mut count = 0\n    pub mut total = 1\n}"));
    }

    [Theory]
    [InlineData("pub int x = 1", "(declare pub int x (int 1))")]
    [InlineData("static mut y = 2", "(mut-declare static y (int 2))")]
    [InlineData("void F() {\n    override int x = 1\n}", "(function void F() {(declare override int x (int 1))})")]
    [InlineData("pub class X {\n}", "(class pub X {})")]
    [InlineData("static data D(int X)", "(data static D(int X))")]
    public void Every_modifier_written_stays_on_the_declaration_for_the_checker(string program, string tree)
    {
        // F9, K7, D1: the modifiers are kept where they are written; which of them apply at the top level, in a block or on a type is the
        // checker's, so the front end refuses none
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("long* p = null", "(declare long* p null)")]
    [InlineData("byte** pp = null", "(declare byte** pp null)")]
    [InlineData("void* v = Ptr<void>(m)", "(declare void* v (call Ptr<void> m))")]
    [InlineData("int?* q = null", "(declare int?* q null)")]
    [InlineData("List<int*> xs = List<int*>()", "(declare List<int*> xs (call List<int*>))")]
    [InlineData("void Fill(byte* buf, long n) {\n}", "(function void Fill(byte* buf, long n) {})")]
    [InlineData("p = &n", "(assign-or-declare p (& n))")]
    [InlineData("q = &a[i]", "(assign-or-declare q (& (index a i)))")]
    [InlineData("q = &(*b).Next", "(assign-or-declare q (& (. (paren (* b)) Next)))")]
    [InlineData("x = *p + 1", "(assign-or-declare x (+ (* p) (int 1)))")]
    [InlineData("print(*b.Next)", "(call print (* (. b Next)))")]
    [InlineData("y = **pp", "(assign-or-declare y (* (* pp)))")]
    [InlineData("x = a & b", "(assign-or-declare x (& a b))")]
    [InlineData("*p = 7", "(assign (* p) (int 7))")]
    [InlineData("(*b).Next = q", "(assign (. (paren (* b)) Next) q)")]
    [InlineData("x = a\n*p = 3", "(assign-or-declare x a) (assign (* p) (int 3))")]
    [InlineData("x = a *\n    b", "(assign-or-declare x (* a b))")]
    [InlineData("Block* b = Ptr<Block>(m)", "(declare Block* b (call Ptr<Block> m))")]
    [InlineData("match *p {\n    else => print(0)\n}", "(match (* p) [(else => (call print (int 0)))])")]
    [InlineData("Fill(mut *p)", "(call Fill mut (* p))")]
    [InlineData("q = &*p", "(assign-or-declare q (& (* p)))")]
    [InlineData("a * b = c", "(declare a* b c)")]
    public void A_raw_pointer_is_written_as_c_sharp_writes_it(string program, string tree)
    {
        // R10: the type `int*`, `int**` a pointer to one, `void*` the untyped one (R14), in a declaration and a parameter, and in a type
        // argument, since R10 lets a pointer type stand anywhere a type stands, where C# refuses one (CS0306); every type the parser
        // reads takes the suffix, its trial parses among them, so a statement `a * b = c` declares `b` a pointer to an `a`, as C# reads
        // `a * b = c;`, while `x = a * b` stays a product;
        // the read `*p` and the address `&x` are prefix operators that bind as `-` does, so `*b.Next` reads through `b.Next`; `*p = v` and
        // `(*b).Next = q` write through a pointer; a line that starts with `*` starts a statement, and one that ends in `*` continues (L4);
        // `*` and `&` start a value as `-` does, after `match` and after the `mut` of an argument (M7).
        // `int?*` points to an `int?`, a pointee the checker judges
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("naked void Switch() {\n    asm(\"ret\")\n}", "(function naked void Switch() {(asm \"ret\")})")]
    [InlineData("naked pub void S() {\n}", "(function pub naked void S() {})")]
    [InlineData("thread static mut long depth = 0", "(mut-declare static thread long depth (int 0))")]
    [InlineData("thread = 1", "(assign-or-declare thread (int 1))")]
    [InlineData("print(thread.Count)", "(call print (. thread Count))")]
    [InlineData("extern \"C\" {\n    long write(int fd, byte* buf, long n)\n}", "(extern {(c-function long write(int fd, byte* buf, long n))})")]
    [InlineData("extern \"C\" { long write(int fd, byte* buf, long n) }", "(extern {(c-function long write(int fd, byte* buf, long n))})")]
    [InlineData("extern \"C\" in \"dbghelp\" {\n    int printf(byte* format, ...)\n    void Tick()\n}", "(extern in \"dbghelp\" {(c-function int printf(byte* format, ...)) (c-function void Tick())})")]
    [InlineData("extern \"C\" int OnSample(int frames) {\n    return frames\n}", "(function extern int OnSample(int frames) {(return frames)})")]
    [InlineData("extern \"C\" (int) => void f = Tick", "(declare extern \"C\" (int) => void f Tick)")]
    [InlineData("(extern \"C\" (int) => void)? g = null", "(declare (extern \"C\" (int) => void)? g null)")]
    [InlineData("extern \"C\" extern \"C\" (int) => int Pick() => Twice", "(function extern extern \"C\" (int) => int Pick() => Twice)")]
    [InlineData("extern \"C\" data Point(int X, int Y)", "(data extern Point(int X, int Y))")]
    public void A_function_with_no_frame_thread_state_and_the_forms_of_c_parse(string program, string tree)
    {
        // R12: `naked` before a signature, among the other modifiers in any order, as C# orders modifiers; R13: `thread` is a word only
        // before `static`, and a name everywhere else (L18); R16: a block of C functions with no bodies, on one line or on several, the
        // library after `in`, `...` the last parameter; R16: a function of the package's own in the C calling convention; R17: the type
        // of a C function, its nullable form in parentheses, and `extern "C"` before `(` starts that type; R17: data in C's layout
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("asm(\"nop\")", "(asm \"nop\")")]
    [InlineData("asm(\"nop\", \"nop\")", "(asm \"nop\" \"nop\")")]
    [InlineData("asm(\"add {r}, 1\", r = inout(reg) count)", "(asm \"add {r}, 1\" [r = inout(reg) count])")]
    [InlineData("asm(\"mov {}, {}\", out(reg) x, in(\"eax\") y)", "(asm \"mov {}, {}\" [out(reg) x] [in(\"eax\") y])")]
    [InlineData("asm(\"xchg {a}, {b}\", a = inout(reg) x => y, b = inout(reg) z => _)", "(asm \"xchg {a}, {b}\" [a = inout(reg) x => y] [b = inout(reg) z => _])")]
    [InlineData("asm(\"{a}\", a = inout(reg) (x) => y)", "(asm \"{a}\" [a = inout(reg) (paren x) => y])")]
    [InlineData("asm(\"call {f}\", f = sym Tick, const 4, clobber_abi(\"C\"), options(nostack, raw))", "(asm \"call {f}\" [f = sym Tick] [const (int 4)] [clobber_abi(\"C\")] [options(nostack, raw)])")]
    [InlineData("asm(\"jmp {l}\", l = label {\n    print(1)\n})", "(asm \"jmp {l}\" [l = label {(call print (int 1))}])")]
    [InlineData("asm(\"\"\"\n    nop\n    nop {x}\n    \"\"\", x = in(reg) 1)", "(asm \"nop\\nnop {x}\" [x = in(reg) (int 1)])")]
    [InlineData("asm(\"{x}\", x = in(reg) Pick(y => y))", "(asm \"{x}\" [x = in(reg) (call Pick (lambda (y) y))])")]
    [InlineData("asm(\"{x}\", x = frobnicate(reg) y)", "(asm \"{x}\" [x = frobnicate(reg) y])")]
    [InlineData("asm(\"{n}\", n = const (N * 2))", "(asm \"{n}\" [n = const (paren (* N (int 2)))])")]
    public void An_asm_statement_takes_its_operands_as_rust_writes_them(string program, string tree)
    {
        // R11: the templates first, then the operands in Rust's shape, a name and `=` where they have one, `in`, `out`, `inout` with a
        // register class or a register's string, `=>` that splits an operand (a lambda nowhere but in an argument of a call there), `_`,
        // `const` with a value and `sym` with a path, read as a value with no list, `clobber_abi`, `options` and `label` with its block; a word that is not Rust's parses, and the checker judges it
        // (`asm-operand`)
        Assert.Equal(tree, Tree(program));
    }

    [Theory]
    [InlineData("int*? p = null", 1)]
    [InlineData("q = &F()", 1)]
    [InlineData("q = &(a + b)", 1)]
    [InlineData("q = &a?.b.c", 1)]
    [InlineData("q = &a?.b[0]", 1)]
    [InlineData("extern \"C++\" {\n}", 1)]
    [InlineData("extern \"C\" {\n    long write(int fd, ...) {\n    }\n}", 2)]
    [InlineData("extern \"C\" {\n    int printf(..., byte* format)\n}", 2)]
    [InlineData("extern \"C\" {\n    long errno\n}", 2)]
    [InlineData("int F(int x, ...) {\n}", 1)]
    [InlineData("x = asm(\"nop\")", 1)]
    [InlineData("asm(1)", 1)]
    [InlineData("asm()", 1)]
    [InlineData("asm(\"nop\", x = in(reg) 1, \"nop\")", 1)]
    [InlineData("extern \"C\" (int) F() {\n}", 1)]
    [InlineData("static thread mut long x = 0", 1)]
    [InlineData("extern \"C\" in dbghelp {\n}", 1)]
    public void A_raw_form_outside_its_shape_is_syntax(string program, int line)
    {
        // E4: a pointer holds `null` itself, so `int*?` has no meaning (R10, as C#); `&` takes a place as the front end reads one (IsPlace);
        // `extern` names the convention "C" (R16, R17); a function of a block of C functions has no body, `...` is its last parameter, and
        // the block declares functions (R16), whether it declares a C library's variables standing open; `asm` is a statement whose
        // templates, string literals, come before its operands (R11); `extern "C" (` starts the type of a C function (R17); `thread`
        // is a word only before `static` (R13)
        Assert.Equal([(ErrorIds.Syntax, line)], Errors(program));
    }

    [Theory]
    [InlineData("extern \"C\"\n{\n    int abs(int n)\n}")]
    [InlineData("asm(\"jmp {l}\", l = label\n{\n})")]
    public void The_block_of_c_functions_and_of_label_opens_on_the_line_of_its_word(string program)
    {
        // C1: a block that opens on the next line is `braces-required` at the line of its construct, inside the parentheses of `asm` too,
        // where L4 passes the line break over; the block is read all the same
        Assert.Equal([(ErrorIds.BracesRequired, 1)], Errors(program));
    }

    [Theory]
    [InlineData("xs.Map<int>(f)", "(call (. xs Map)<int> f)")]
    [InlineData("f(a < b, c > (d))", "(call f (call a<b, c> d))")]
    [InlineData("print(a < b, c > d)", "(call print (< a b), (> c d))")]
    [InlineData("print(a < b, c > .5)", "(call print (< a b), (> c (real .5)))")]
    [InlineData("print(a < b, c > d, e)", "(call print (< a b), (> c d), e)")]
    public void Type_arguments_follow_a_member_access_too_and_are_told_from_comparisons_as_in_c_sharp(string program, string tree)
    {
        // T19: a call names its type arguments, after a member access too. How they are told from comparisons is a reading (README; a
        // question for a design round): the list is kept only when `(` follows the `>`; C# (T17: generics as in C#) also keeps it before
        // `)`, `]`, `,`, `.`, `?` and a few more, which the front end reads as comparisons (the `.5` and `, e` rows), since
        // the reference has no form for a generic name that is not called; the fact below pins the `)`
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void A_generic_name_that_is_not_called_is_a_comparison_without_its_right_operand()
    {
        // the reading on T17 with T19 (README): C# reads `a<b, c>` before `)` or `]` as a generic name; the front end reads a comparison,
        // which lacks its right operand there, so the shape is `syntax` (E4) at its line
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("f(a < b, c >)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("x = xs[a < b, c >]"));
    }

    [Fact]
    public void An_enum_or_flags_type_has_no_type_parameters()
    {
        // a reading under E4's general clause (README; a question for a design round): D12 and D13 give an enum and a flags type a name
        // and a body and no type parameters, so type parameters there are text no rule gives a meaning; the id could be the checker's
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("enum E<T> { A }"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("flags F<T> { A }"));
    }

    [Fact]
    public void A_second_question_mark_on_a_type_is_syntax()
    {
        // a reading under E4's general clause (README; a question for a design round): N1 gives a type one `?`, so a second is text no
        // rule gives a meaning; the id could be the checker's
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("int? ? x = 1"));
    }

    [Theory]
    [InlineData("void Run((int) => int | NotFound f) {\n}", "(function void Run((int) => (int | NotFound) f) {})")]
    [InlineData("((int) => int) | NotFound g = null", "(declare ((int) => int) | NotFound g null)")]
    public void The_arrow_of_a_function_type_reaches_to_the_end_of_the_type(string program, string tree)
    {
        // F11: `(int) => int | NotFound` returns the union, and a union of a function type and another type is written
        // `((int) => int) | NotFound` (F11 records the owner's choice of 2026-10-11)
        Assert.Equal(tree, Tree(program));
    }

    [Fact]
    public void The_printer_keeps_a_type_parameter_limit_and_parenthesizes_a_union_inside_a_type()
    {
        // T19: a limit; D9 with N1 and R3: a union inside a nullable, a weak or another union type, each tree printed as itself
        Assert.Equal("(data Box<T: Shape>(T Item))", Tree("data Box<T: Shape>(T Item)"));
        Assert.Equal("(declare (A | B)? x null)", Tree("(A | B)? x = null"));
        Assert.Equal("(declare (A | B) | C x null)", Tree("(A | B) | C x = null"));
        Assert.Equal("(declare A | (B | C) x null)", Tree("A | (B | C) x = null"));
        Assert.Equal("(declare weak (A | B)? x null)", Tree("weak (A | B)? x = null"));
        Assert.Equal("(declare weak A | B x null)", Tree("weak A | B x = null"));
    }

    [Fact]
    public void A_comment_inside_an_interpolation_is_syntax()
    {
        // L3: a comment runs to the end of its line, which an interpolation (L6) does not reach: text no rule gives a meaning (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(\"{x // note}\")"));
    }

    [Fact]
    public void A_core_word_after_a_dot_is_a_member_name_without_a_second_error()
    {
        // the reading on L12 (README; a question for a design round): a core word after the dot is a member name, with `(` after it or
        // not, a type name too, and the use adds no error, so that the one error stays the declaration's (E1)
        Assert.Empty(Errors("x.match(1)"));
        Assert.Empty(Errors("x.match"));
        Assert.Empty(Errors("x.int"));
        Assert.Equal("(call (. x if))", Tree("x.if()"));
    }

    [Fact]
    public void An_argument_carries_its_own_line()
    {
        // E2: an argument's node carries the line it stands on, inside an open `(` that L4 lets run over lines, and the call keeps its own
        var (tree, _) = Parse("print(Add(\n    total,\n    other\n))");
        var call = Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(tree.Statements[0]).Expression);
        var inner = Assert.IsType<CallExpression>(call.Arguments[0].Value);
        Assert.Equal([2, 3], inner.Arguments.Select(a => a.Line));
        Assert.Equal(1, call.Line);
    }

    [Fact]
    public void A_modifier_or_a_type_name_at_the_start_of_a_statement_is_a_name_where_no_declaration_follows()
    {
        // the reading on L12 (README; a question for a design round): a modifier or a built-in type's name at the start of a statement
        // is tried as a declaration and otherwise stands as a name, so `int.Parse(s)` is a member access on the type's name (T10 writes
        // a conversion as a call of the type) and a variable named `mut` is used without a second error (E1)
        Assert.Equal("(call (. int Parse) s)", Tree("int.Parse(s)"));
        var (tree, diagnostics) = Parse("mut = 1\nmut.Add(1)");
        Assert.Equal([(ErrorIds.ReservedWord, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(assign-or-declare mut (int 1)) (call (. mut Add) (int 1))", SyntaxPrinter.Print(tree));
        // a declaration that fails after its commit is its own error, not read again as an expression
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("print(1)\nint x = 1 +"));
    }

    [Fact]
    public void A_missing_block_takes_the_rest_of_its_line_under_the_one_braces_required()
    {
        // C1 with E1: the missing block is the construct's one error; a `;`, a statement that a `{` follows, or text that is no
        // statement in its place adds none, and the statement after the construct parses
        Assert.Equal([(ErrorIds.BracesRequired, 1)], Errors("if x;"));
        Assert.Equal([(ErrorIds.BracesRequired, 1)], Errors("while x;\nprint(1)"));
        var (tree, diagnostics) = Parse("if x y {\n    print(1)\n}\nprint(9)");
        Assert.Equal([(ErrorIds.BracesRequired, 1)], diagnostics.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(call print (int 9))", SyntaxPrinter.Print(tree));
        Assert.Equal([(ErrorIds.BracesRequired, 1)], Errors("if x = 1 {\n}"));
    }

    [Fact]
    public void An_if_with_every_brace_on_its_own_line_is_one_braces_required_and_its_else_parses()
    {
        // C1 and C2 with E1: the C# brace style is one error per `if`, at the line of its condition; the `else` on its own line adds
        // none, and both branches parse. A stray `else` whose brace stands on the next line is its one `syntax` (E4), and its block parses
        var (braces, first) = Parse("if a\n{\n    print(1)\n}\nelse\n{\n    print(2)\n}");
        Assert.Equal([(ErrorIds.BracesRequired, 1)], first.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(if a {(call print (int 1))} else {(call print (int 2))})", SyntaxPrinter.Print(braces));
        var (stray, second) = Parse("if a {\n}\nelse\n{\n    print(2)\n}");
        Assert.Equal([(ErrorIds.Syntax, 3)], second.Items.Select(d => (d.Id, d.Line)));
        Assert.Equal("(if a {} else {(call print (int 2))})", SyntaxPrinter.Print(stray));
    }

    [Fact]
    public void A_lambda_whose_arrow_ends_its_line_takes_its_body_on_the_next_line()
    {
        // the reading on F7 with L4 and C1 (README; a question for a design round): a `=>` that ends its line continues the statement
        // (L4), and C1 names no line for a lambda's block, so the block or the expression on the next line is no error
        Assert.Equal("(call Run (lambda (x) {(call print x)}))", Tree("Run(x =>\n{\n    print(x)\n})"));
        Assert.Equal("(assign-or-declare f (lambda (x) (* x (int 2))))", Tree("f = x =>\n    x * 2"));
    }

    [Fact]
    public void Mut_before_an_argument_is_a_modifier_only_where_an_expression_follows_it()
    {
        // M7: `mut` in front of an argument; the reading on L12 (README): `mut` with no expression after it is a name, so a variable of
        // that name is passed without a second error (E1)
        Assert.Equal("(call f mut)", Tree("f(mut)"));
        Assert.Equal("(call f mut, (int 1))", Tree("f(mut, 1)"));
    }

    [Fact]
    public void A_value_pattern_is_a_literal_a_negated_number_literal_or_a_path()
    {
        // C9: an arm names a literal, with `-` before a number literal read as part of it (L17 reads `-100` as one literal); D12: a
        // value by its path. Anything else there is text no rule gives a meaning (E4)
        Assert.Equal("(match x [((- (int 1)) => (call print (int 1)))])", Tree("match x {\n    -1 => print(1)\n}"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("match x {\n    -y => print(1)\n}"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("match x {\n    \"a\".Length => print(1)\n}"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("match x {\n    Plan.Free(1) => print(1)\n}"));
    }

    [Fact]
    public void A_second_weak_on_a_type_is_syntax()
    {
        // a reading under E4's general clause (README; a question for a design round): R3 and R5 write one `weak` before a type, so a
        // second is text no rule gives a meaning; the id could be the checker's
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("weak weak Node? x = null"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("class Pet(weak weak Owner? Keeper)"));
    }

    [Fact]
    public void A_type_parameter_limit_is_one_type_by_name()
    {
        // T19: a limit names an interface, so a union, a function type or a parenthesized type there is `syntax` (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("data Box<T: A | B>(T Item)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("data Box<T: (A | B)>(T Item)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("data Box<T: (int) => int>(T Item)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("data Box<T: (A?)>(T Item)"));
    }
}
