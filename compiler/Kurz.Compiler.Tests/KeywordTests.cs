using Kurz.Compiler.Build;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Keywords that transform code (chapter 21, record section 9): a declaration's pattern and template, each use filled in its place and
/// checked as code written by hand (J4), an expression slot evaluated once at the use (J5), a block whose <c>return</c> leaves the
/// function around the use and whose jumps a template's loop or lambda would take refused (J6), the names of a template kept apart from
/// the use's, but the name a use gives (J7), the depth of keywords inside templates (J8), the words no keyword can be (J9), and the grant
/// (J10). Each program is built with the grant <c>keywords</c> a case's header line gives, but where a test is about the grant.
/// </summary>
public sealed class KeywordTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static readonly string[] Granted = ["keywords"];

    private static DiagnosticBag Read(string program)
    {
        var diagnostics = new DiagnosticBag();
        Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        return diagnostics;
    }

    private Compilation Compile(string program, string[]? grants = null) =>
        Builder.Compile(SourceText.FromString("program.kz", program), grants ?? Granted, toolchain.Runtime, testBuild: true);

    private IReadOnlyList<string> Printed(string program, string name)
    {
        var compilation = Compile(program);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        var run = Toolchain.Run(toolchain.Build(compilation.Program, name), "0", "1");
        Assert.True(run.Code == 0, $"exit code {run.Code}, error: {run.Error}");
        return run.Output;
    }

    /// <summary>The one error of a program, its id and its line, as a case pins it (E1).</summary>
    private (string Id, int Line) Error(string program, string[]? grants = null)
    {
        var diagnostics = Compile(program, grants).Diagnostics;
        var error = Assert.Single(diagnostics.Items, d => d.IsError || d.IsNotSupported);
        return (error.IsNotSupported ? "not supported" : error.Id, error.Line);
    }

    private const string Repeat = """
        keyword repeat <int times> <block body> {
            mut i = 0
            while i < times {
                body
                i = i + 1
            }
        }

        """;

    [Fact]
    public void A_use_runs_its_template_with_the_use_s_block_in_the_place_of_the_slot()
    {
        // J4: the template filled at the use, its block slot standing where the template names it, twice as a copy
        Assert.Equal(["2"], Printed("keyword twice <block body> {\n    body\n    body\n}\n\nmut n = 0\ntwice {\n    n = n + 1\n}\nprint(n)\n", "keyword-twice"));
        Assert.Equal(["0", "1", "2"], Printed(Repeat + "mut k = 0\nrepeat 3 {\n    print(k)\n    k = k + 1\n}\n", "keyword-repeat"));
    }

    [Fact]
    public void An_expression_slot_is_evaluated_once_where_the_keyword_stands()
    {
        // J5: the slot is bound once, in front, as an argument is, however often the template reads it (record section 9, Q2)
        var program = Repeat + "int Count() {\n    print(\"counted\")\n    return 3\n}\n\nmut total = 0\nrepeat Count() {\n    total = total + 1\n}\nprint(total)\n";
        Assert.Equal(["counted", "3"], Printed(program, "keyword-once"));
    }

    [Fact]
    public void The_names_a_template_declares_are_its_own_and_the_use_s_names_are_the_use_s()
    {
        // J7: the template's `i` is renamed at the use, so the use's own `i` is the variable its block changes; a hole of a string in
        // the template and a member of the same name are read as the template wrote them
        var program = Repeat + "mut i = 100\nrepeat 2 {\n    i = i + 1\n}\nprint(i)\n";
        Assert.Equal(["102"], Printed(program, "keyword-hygiene"));
        var holes = "data P(int size)\n\nkeyword show <P p> {\n    size = p.size + 1\n    q = p with { size = size }\n    print(\"size {size} {q.size}\")\n}\n\nsize = 7\nshow P(1)\nprint(size)\n";
        Assert.Equal(["size 2 2", "7"], Printed(holes, "keyword-holes"));
    }

    [Fact]
    public void A_name_slot_names_the_template_s_variable_for_the_use_s_block()
    {
        // J7: `as n` names the template's variable (record section 9, Q6); where the optional part is not written, the name is the template's own
        const string Each = "keyword each <int count> [as <name index>] <block body> {\n    mut index = 0\n    while index < count {\n        body\n        index = index + 1\n    }\n}\n\n";
        Assert.Equal(["0", "1", "x", "x"], Printed(Each + "each 2 as n {\n    print(n)\n}\neach 2 {\n    print(\"x\")\n}\n", "keyword-name"));
    }

    [Fact]
    public void A_return_in_a_use_s_block_leaves_the_function_around_the_use()
    {
        // J6: `return` acts on the function around the use, as in C#'s `lock` (record section 9, Q3)
        var program = "keyword unless <bool ok> <block body> {\n    if !ok {\n        body\n    }\n}\n\nint Positive(int x) {\n    unless x > 0 {\n        return -1\n    }\n\n    return x\n}\n\nprint(Positive(5))\nprint(Positive(-2))\n";
        Assert.Equal(["5", "-1"], Printed(program, "keyword-return"));
    }

    [Fact]
    public void A_use_may_stand_before_its_declaration_and_inside_another_keyword_s_template()
    {
        // J1, J8: the declarations are read before the statements; a template's use of a keyword is filled in its turn (record section 9, Q7)
        var program = "twice {\n    print(\"a\")\n}\n\nkeyword twice <block body> {\n    once {\n        body\n    }\n\n    body\n}\n\nkeyword once <block body> {\n    body\n}\n";
        Assert.Equal(["a", "a"], Printed(program, "keyword-nested"));
    }

    [Theory]
    [InlineData("for x in [1, 2] {\n    repeat 2 {\n        break\n    }\n}\n", 10)]
    [InlineData("for x in [1, 2] {\n    repeat 2 {\n        continue\n    }\n}\n", 10)]
    public void A_jump_out_of_a_block_its_template_puts_in_a_loop_is_refused(string use, int line)
    {
        // J6: the `break` would act on the template's loop and not on the loop around the use, which Q3 names: refused at the jump
        Assert.Equal(("keyword-jump", line), Error(Repeat + use));
    }

    [Fact]
    public void A_jump_inside_a_loop_of_the_use_s_own_block_is_the_block_s()
    {
        Assert.Equal(["1"], Printed(Repeat + "repeat 1 {\n    for x in [1, 2] {\n        print(x)\n        break\n    }\n}\n", "keyword-own-loop"));
    }

    [Theory]
    [InlineData("int F() {\n    apart {\n        return 1\n    }\n\n    return 0\n}\n\nprint(F())\n", 13)]
    [InlineData("for x in [1] {\n    apart {\n        break\n    }\n}\n", 13)]
    public void No_jump_leaves_a_block_its_template_runs_apart(string use, int line)
    {
        // J6: a `<detached block>` runs inside the template's lambda, so a jump out of it is a compile error (record section 9, Q3, Q10 (a))
        var program = "void Run(() => void f) {\n    f()\n}\n\nkeyword apart <detached block job> {\n    Run(() => {\n        job\n    })\n}\n\n" + use;
        Assert.Equal(("keyword-jump", line), Error(program));
    }

    [Fact]
    public void A_plain_block_slot_in_a_lambda_of_its_template_is_refused_at_its_declaration()
    {
        var program = "void Run(() => void f) {\n    f()\n}\n\nkeyword apart <block job> {\n    Run(() => {\n        job\n    })\n}\n";
        Assert.Equal(("keyword-jump", 5), Error(program));
    }

    [Theory]
    [InlineData("repeat {\n}\n", "keyword-no-match", 8)]
    [InlineData("repeat 2\n", "keyword-no-match", 8)]
    [InlineData("repeat 2 {\n} 3\n", "keyword-no-match", 9)]
    [InlineData("repeat \"two\" {\n}\n", "type-mismatch", 8)]
    [InlineData("repeat = 1\n", "reserved-word", 8)]
    [InlineData("x = repeat 2 {\n}\n", "not supported", 8)]
    public void A_use_is_held_to_its_pattern_and_its_errors_point_at_the_use(string use, string id, int line)
    {
        // J3, J4: a use that matches no pattern, and a type error inside the expansion, at the use's line (record section 9, rule 4);
        // J9: the word is no name; J11: a use as an expression is not supported by this build
        Assert.Equal((id, line), Error(Repeat + use));
    }

    [Theory]
    [InlineData("keyword if <block body> {\n    body\n}\n", "keyword-redefined", 1)]
    [InlineData("keyword within <block body> {\n    body\n}\n", "keyword-redefined", 1)]
    [InlineData("keyword twice <block body> {\n    body\n}\n\nkeyword twice <block body> {\n    body\n}\n", "keyword-redefined", 5)]
    [InlineData("keyword twice <block body> <int n> {\n    body\n}\n", "syntax", 1)]
    [InlineData("keyword twice <block body> {\n    f(body)\n}\n", "syntax", 1)]
    [InlineData("keyword twice <int a> <int b> {\n    print(a + b)\n}\n", "syntax", 1)]
    [InlineData("keyword twice [<block body>] {\n    body\n}\n", "syntax", 1)]
    [InlineData("keyword twice <block body> {\n    twice {\n        body\n    }\n}\n", "keyword-depth", 2)]
    [InlineData("int F() {\n    keyword twice <block body> {\n        body\n    }\n\n    return 1\n}\n", "syntax", 2)]
    [InlineData("keyword codes <int... xs> {\n    print(1)\n}\n", "not supported", 1)]
    [InlineData("keyword twice [then [also <block b>]] {\n    b\n}\n", "not supported", 1)]
    [InlineData("keyword twice [times <int n>] {\n    print(n)\n}\n", "not supported", 1)]
    [InlineData("keyword twice <int n> <block body> {\n    n = 2\n    print(n)\n    body\n}\n\ntwice 1 {\n}\n", "assign-immutable", 7)]
    public void A_declaration_is_held_to_its_rules(string program, string id, int line)
    {
        // J9: no core word, no word of a form, no keyword twice; J2: every slot named in the template, a block slot alone on its line,
        // an end for each expression slot, an optional part that starts with a word; J8: a keyword in its own template; J1: the top level;
        // J2: a repeated part, an optional part inside another and an expression slot in an optional part are not supported by this build;
        // J5: an expression slot's variable is declared without `mut`, so the template cannot assign it (V7), at the use's line
        Assert.Equal((id, line), Error(program));
    }

    [Fact]
    public void An_error_inside_an_expansion_names_the_template_s_name_as_the_template_wrote_it()
    {
        // J7, record section 9, rule 4: the error points at what the developer wrote, `n`, and not at the name the expansion took for it
        const string program = "keyword twice <int n> <block body> {\n    n = 2\n    print(n)\n    body\n}\n\ntwice 1 {\n}\n";
        var error = Assert.Single(Compile(program).Diagnostics.Items, d => d.IsError);
        Assert.Equal((ErrorIds.AssignImmutable, 7), (error.Id, error.Line));
        Assert.Contains("`n` was declared without `mut`", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("n_", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_needs_the_grant_keywords()
    {
        // J10: the grant of a case's header line, as `raw`'s (record section 9, the owner, 2026-10-30, "Definer, `allow keywords`")
        Assert.Equal(("keyword-grant", 1), Error("keyword twice <block body> {\n    body\n}\n", []));
    }

    [Fact]
    public void Keywords_inside_templates_reach_their_depth_past_64_with_an_error()
    {
        // J8: a chain of 66 keywords, each template using the one after it, is refused (record section 9, Q7, as rustc's `recursion_limit`)
        var program = string.Concat(Enumerable.Range(0, 66).Select(i => i < 65 ? $"keyword k{i} {{\n    k{i + 1}\n}}\n\n" : $"keyword k{i} {{\n    print(1)\n}}\n\n")) + "k0\n";
        Assert.Contains(Read(program).Items, d => d.Id == "keyword-depth");
        var short64 = string.Concat(Enumerable.Range(0, 10).Select(i => i < 9 ? $"keyword k{i} {{\n    k{i + 1}\n}}\n\n" : $"keyword k{i} {{\n    print(1)\n}}\n\n")) + "k0\n";
        Assert.Empty(Read(short64).Items); // the control, a chain of ten
    }
}
