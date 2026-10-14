using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The checker, rule by rule: the error id and the line each shape gets (E1, E2), the type each
/// declaration takes, and what it says <c>not supported</c> to. Every program stands inline; the
/// parser reads each one clean, since the checker reads a clean tree only.
/// </summary>
public sealed class CheckerTests
{
    private static (DiagnosticBag Diagnostics, Checker Checker) Check(string program)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        var checker = Checker.Check(tree, diagnostics);
        return (diagnostics, checker);
    }

    /// <summary>The compile errors of a program the checker reads in full, by line.</summary>
    private static List<(string Id, int Line)> Errors(string program)
    {
        var (diagnostics, _) = Check(program);
        Assert.DoesNotContain(diagnostics.Items, d => !d.IsError);
        return diagnostics.Sorted.Select(d => (d.Id, d.Line)).ToList();
    }

    /// <summary>What the checker does not read, as <c>line: construct</c>, with no error beside it.</summary>
    private static List<string> NotSupported(string program)
    {
        var (diagnostics, _) = Check(program);
        Assert.DoesNotContain(diagnostics.Items, d => d.IsError);
        Assert.NotEmpty(diagnostics.Items);
        return diagnostics.Sorted.Select(d => $"{d.Line}: {d.Message}").ToList();
    }

    /// <summary>The type of a top-level variable of a program the checker reads clean.</summary>
    private static string TypeOf(string program, string variable)
    {
        var (diagnostics, checker) = Check(program);
        Assert.Empty(diagnostics.Items);
        return checker.TopLevelVariables[variable].Text;
    }

    private static void Clean(string program)
    {
        var (diagnostics, _) = Check(program);
        Assert.Empty(diagnostics.Items);
    }

    // ---- variables (chapter 2) ------------------------------------------------------------------

    [Theory]
    [InlineData("x = 4\nprint(x)", "x", "int")]
    [InlineData("mut x = 4\nx = 5\nprint(x)", "x", "int")]
    [InlineData("int x = 4\nprint(x)", "x", "int")]
    [InlineData("s = \"ok\"\nprint(s)", "s", "string")]
    [InlineData("yes = true\nprint(yes)", "yes", "bool")]
    [InlineData("c = 'a'\nprint(c)", "c", "char")]
    [InlineData("half = 0.5\nprint(half)", "half", "double")]
    [InlineData("f = 0.5f\nprint(f)", "f", "float")]
    [InlineData("m = 1.5m\nprint(m)", "m", "decimal")]
    [InlineData("d = 90min\nprint(d)", "d", "duration")]
    [InlineData("big = 4000000000\nprint(big)", "big", "long")]
    [InlineData("most = 18446744073709551615\nprint(most)", "most", "ulong")]
    [InlineData("xs = List<int>()\nprint(xs.Count)", "xs", "List<int>")]
    public void A_variable_takes_the_type_of_its_first_value(string program, string variable, string type)
    {
        // V1 to V4, T1: one type, fixed at the declaration; L10, L11, L14 for the literals
        Assert.Equal(type, TypeOf(program, variable));
    }

    [Fact]
    public void An_unused_variable_is_reported_at_its_declaration()
    {
        // V5: never read; V6: being assigned again does not count
        Assert.Equal([(ErrorIds.UnusedVariable, 2)], Errors("x = 1\ny = 2\nprint(x)"));
        Assert.Equal([(ErrorIds.UnusedVariable, 1)], Errors("mut x = 1\nx = 2"));
    }

    [Fact]
    public void A_variable_a_mut_method_a_path_assignment_or_a_mut_argument_reads_counts_as_used()
    {
        // V6's reading: the add, the path write and the mut argument read the value they change
        Clean("mut xs = List<int>()\nxs.Add(1)");
        Clean("data Point(int X, int Y)\n\nmut p = Point(1, 2)\np.X = 3");
        Clean("void Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nmut numbers = List<int>()\nFill(mut numbers)");
    }

    [Fact]
    public void A_parameter_and_a_loop_variable_that_are_never_read_are_no_error()
    {
        // V13
        Clean("int Three(int ignored) => 3\n\nfor i in 0..<2 {\n    print(Three(1))\n}");
    }

    [Fact]
    public void A_second_assignment_without_mut_is_assign_immutable_at_the_assignment()
    {
        // V7; F8 for a parameter; C6's variable is immutable too (the reading)
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("x = 1\nprint(x)\nx = 2"));
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("int Next(int n) {\n    n = n + 1\n    return n\n}\n\nprint(Next(1))"));
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("for i in 0..<2 {\n    i = 5\n}"));
    }

    [Fact]
    public void An_assignment_inside_a_block_is_to_the_outer_variable_and_a_written_type_always_declares()
    {
        // V8, V9
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("x = 1\nif x > 0 {\n    x = 2\n}\nprint(x)"));
        Clean("mut x = 1\nif x > 0 {\n    x = 2\n}\nprint(x)");
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("x = 1\nint x = 2\nprint(x)"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("x = 1\nmut x = 2\nprint(x)"));
        Assert.Equal([(ErrorIds.Redeclared, 3)], Errors("x = 1\nif x > 0 {\n    mut int x = 2\n    print(x)\n}"));
    }

    [Fact]
    public void A_variable_is_visible_to_the_end_of_its_block()
    {
        // V10: not C#'s whole-block scope: the second `y` is a new variable
        Assert.Equal([(ErrorIds.UnknownName, 6)], Errors("x = 1\nif x > 0 {\n    y = x + 1\n    print(y + 1)\n}\nprint(y)"));
        Clean("x = 1\nif x > 0 {\n    y = x + 1\n    print(y)\n}\ny = 2\nprint(y)");
    }

    [Fact]
    public void A_declaration_named_as_a_function_a_type_or_print_is_redeclared()
    {
        // V9's reading: a function's and a type's name is visible in the whole file (F6), and so is `print` (A1)
        Assert.Equal([(ErrorIds.Redeclared, 3)], Errors("int Add(int a, int b) => a + b\n\nAdd = 1\nprint(Add(1, 2))"));
        Assert.Equal([(ErrorIds.Redeclared, 3)], Errors("data User(int Id)\n\nUser = 1\nprint(User(1).Id)"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("print = 1\nprint(1)"));
    }

    // ---- literals and their reach (L10, L11, L14, L17) -------------------------------------------

    [Theory]
    [InlineData("byte b = 200\nprint(b)", "b", "byte")]
    [InlineData("long big = 2147483647 + 1\nprint(big)", "big", "long")]
    [InlineData("byte c = 200 + 50\nprint(c)", "c", "byte")]
    [InlineData("sbyte low = -100\nprint(low)", "low", "sbyte")]
    [InlineData("sbyte x = -128\nprint(x)", "x", "sbyte")]
    [InlineData("int y = -2147483648\nprint(y)", "y", "int")]
    [InlineData("double d = 1 / 2\nprint(d)", "d", "double")]
    [InlineData("float f = 1 + 2.5\nprint(f)", "f", "float")]
    [InlineData("decimal m = 1 + 2\nprint(m)", "m", "decimal")]
    [InlineData("byte b = 255 +% 1\nprint(b)", "b", "byte")]
    [InlineData("x = 1 + 2.5\nprint(x)", "x", "double")]
    [InlineData("x = 10 / 4.0\nprint(x)", "x", "double")]
    [InlineData("uint u = 5\nx = u + 1\nprint(x)", "x", "uint")]
    [InlineData("byte b = 1\nx = b + 1.5\nprint(x)", "x", "double")]
    [InlineData("byte b = 1\nx = b + 300.0\nprint(x)", "x", "double")]
    [InlineData("x = long(2147483647 + 1)\nprint(x)", "x", "long")]
    [InlineData("x = int(2.5)\nprint(x)", "x", "int")]
    [InlineData("f = float(1e300)\nprint(f)", "f", "float")]
    [InlineData("longduration l = 100000days\nprint(l)", "l", "longduration")]
    [InlineData("x = 2kb\nprint(x)", "x", "int")]
    public void A_written_or_expected_type_reaches_the_literals_of_a_constant_expression(string program, string variable, string type)
    {
        // L17: the whole expression when it is made of literals, one operand when the other has a type; a conversion reaches the integer literals inside it
        Assert.Equal(type, TypeOf(program, variable));
    }

    [Theory]
    [InlineData("byte c = 300\nprint(c)", 1)]
    [InlineData("print(99999999999999999999)", 1)]
    [InlineData("byte c = 200 + 100\nprint(c)", 1)]
    [InlineData("byte c = 200 + 100 - 100\nprint(c)", 1)]
    [InlineData("byte b = 1\nprint(b + 300)", 2)]
    [InlineData("byte sum = 1\nprint(sum +% 300)", 2)]
    [InlineData("x = 2147483647 + 1\nprint(x)", 1)]
    [InlineData("x = 2147483647 + 1 +% 0\nprint(x)", 1)]
    [InlineData("x = 1e400\nprint(x)", 1)]
    [InlineData("x = 3.5e38f\nprint(x)", 1)]
    [InlineData("float f = 1e300\nprint(f)", 1)]
    [InlineData("print(byte(300))", 1)]
    [InlineData("print(int(1e10))", 1)]
    [InlineData("print(int(1.0 / 0.0))", 1)]
    [InlineData("print(decimal(1e30))", 1)]
    [InlineData("uint u = -1\nprint(u)", 1)]
    [InlineData("duration d = 200000days\nprint(d)", 1)]
    public void A_constant_that_does_not_fit_the_type_it_takes_is_constant_overflow(string program, int line)
    {
        // T6, L10, L11, L17, T29: wherever the literal stands, and one operation at a time
        Assert.Equal([(ErrorIds.ConstantOverflow, line)], Errors(program));
    }

    [Fact]
    public void A_fraction_literal_under_a_written_integer_type_is_type_mismatch_as_a_literal_under_a_string_is()
    {
        // L10, L17
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("int i = 1 + 2.5\nprint(i)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("string s = 300\nprint(s)"));
    }

    [Fact]
    public void A_wrapping_operator_folds_a_constant_by_wrapping_and_takes_integers_only()
    {
        // T6, T12: `2147483647 +% 1` is `-2147483648`; a floating-point operand is type-mismatch
        Clean("print(2147483647 +% 1)");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("d = 1.0\nprint(d +% 1.0)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("byte sum = 1\nprint(sum +% 1.5)"));
    }

    [Fact]
    public void A_unit_literal_whose_fraction_falls_on_no_whole_nanosecond_or_byte_is_inexact_literal()
    {
        // L14
        Assert.Equal([(ErrorIds.InexactLiteral, 1)], Errors("print(0.0000000001s)"));
        Assert.Equal([(ErrorIds.InexactLiteral, 1)], Errors("print(1.3kb)"));
        Clean("print(1.5s)\nprint(0.5h)\nprint(1.5kb)\nprint(1.00025s)");
    }

    // ---- types and operators (chapter 3) ----------------------------------------------------------

    [Fact]
    public void A_value_of_another_type_is_type_mismatch()
    {
        // T1
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut x = 1\nx = \"one\"\nprint(x)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("int seconds = 30s\nprint(seconds)"));
    }

    [Fact]
    public void A_wider_integer_into_a_narrower_one_is_narrowing_conversion_and_the_other_way_widens()
    {
        // T7, T8, T23, T28
        Assert.Equal([(ErrorIds.NarrowingConversion, 2)], Errors("long big = 5\nint small = big\nprint(small)"));
        Assert.Equal([(ErrorIds.NarrowingConversion, 3)], Errors("byte a = 200\nbyte b = 100\nbyte c = a + b\nprint(c)"));
        Clean("int small = 5\nlong big = small\nprint(big)");
        Clean("byte b = 200\nint i = 5\nprint(b + i)\ndouble d = i\nprint(d / 2)");
        Clean("long big = 5\nint small = int(big)\nprint(small)");
    }

    [Theory]
    [InlineData("uint u = 200\nint i = 5\nprint(u + i)", 3)]
    [InlineData("int i = 5\nuint u = i\nprint(u)", 2)]
    [InlineData("ulong u = 1\nlong l = 1\nprint(u + l)", 3)]
    [InlineData("uint u = 1\nint i = 1\nprint(u < i)", 3)]
    [InlineData("byte a = 1\nsbyte b = 1\nprint(a +% b)", 3)]
    [InlineData("int a = 0\nuint n = 3\nfor i in a..<n {\n    print(i)\n}", 3)]
    [InlineData("uint u = 1\nprint(-u)", 2)]
    public void Signed_and_unsigned_do_not_meet(string program, int line)
    {
        // T9, T23, T28, C8: judged on the types as written; the wrapping operators whatever the widths
        Assert.Equal([(ErrorIds.SignMix, line)], Errors(program));
    }

    [Theory]
    [InlineData("byte a = 200\nbyte b = 100\nx = a + b\nprint(x)", "x", "int")]
    [InlineData("sbyte a = 1\nbyte b = 1\nx = a + b\nprint(x)", "x", "int")]
    [InlineData("uint u = 1\nbyte b = 1\nx = u + b\nprint(x)", "x", "uint")]
    [InlineData("long l = 1\nbyte b = 1\nx = l + b\nprint(x)", "x", "long")]
    [InlineData("ulong u = 1\nuint v = 1\nx = u + v\nprint(x)", "x", "ulong")]
    [InlineData("uint u = 1\nlong l = 1\nx = u + l\nprint(x)", "x", "long")]
    [InlineData("byte a = 1\nx = a << 1\nprint(x)", "x", "int")]
    [InlineData("byte a = 200\nbyte b = 100\nx = a +% b\nprint(x)", "x", "byte")]
    [InlineData("ushort c = 1\nbyte a = 1\nx = c +% a\nprint(x)", "x", "ushort")]
    [InlineData("byte a = 1\nx = -a\nprint(x)", "x", "int")]
    [InlineData("byte a = 1\nx = ~a\nprint(x)", "x", "int")]
    [InlineData("float f = 1\nint i = 1\nx = f * i\nprint(x)", "x", "float")]
    [InlineData("float f = 1\ndouble d = 1\nx = f * d\nprint(x)", "x", "double")]
    [InlineData("decimal m = 1\nint i = 1\nx = m * i\nprint(x)", "x", "decimal")]
    [InlineData("duration a = 1s\nduration b = 2s\nx = a + b\nprint(x)", "x", "duration")]
    [InlineData("longduration a = 1s\nduration b = 2s\nx = a - b\nprint(x)", "x", "longduration")]
    public void An_operation_has_the_common_type_of_its_operands_with_narrow_ones_promoted(string program, string variable, string type)
    {
        // T18, T28, T9, T23, T12 (the wrapping operators do not promote), T29
        Assert.Equal(type, TypeOf(program, variable));
    }

    [Fact]
    public void A_decimal_beside_a_floating_point_operand_and_a_duration_under_a_product_are_type_mismatch()
    {
        // T18, T23 (C# refuses decimal with double); T13 (a duration is no integer)
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("decimal m = 1\ndouble d = 1\nprint(m + d)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(d * 2)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(d / 2)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(d % 2)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(d + 1)"));
        Clean("duration a = 1s\nduration b = 2s\nprint(a + b)\nprint(b - a)\nprint(a < b)");
    }

    [Fact]
    public void Operators_take_numbers_and_the_logical_ones_bools()
    {
        // T18: `+` on strings has no rule (an interpolation joins text); `&&` on numbers; `!` on a number
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(\"a\" + \"b\")"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(1 && 2)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(!1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(1 == \"a\")"));
        Clean("print(1 + 2 * 3)\nprint(7 / 2)\nprint(7 % 3)\nprint(1 < 2 && 2 < 3)\nprint(!(1 == 1) || 3 >= 3)\nprint(\"a\" == \"b\")\nprint(true != false)");
    }

    [Fact]
    public void Bit_operators_take_integers_and_a_shift_count_is_masked_in_a_constant()
    {
        // T22, T28
        Clean("print(6 & 3)\nprint(6 | 3)\nprint(6 ^ 3)\nprint(~6)\nprint(1 << 4)\nprint(-16 >> 2)\nprint(1 << 31)\nprint(1 << 33)\nprint(1 << -1)");
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(1.5 & 1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(true | false)"));
    }

    [Theory]
    [InlineData("print(10 / 0)", 1)]
    [InlineData("print(10 % 0)", 1)]
    [InlineData("int x = 5\nprint(x / 0)", 2)]
    [InlineData("print(1 / (1 - 1))", 1)]
    [InlineData("print(1m / 0m)", 1)]
    public void A_division_by_a_constant_zero_is_constant_divide_by_zero(string program, int line)
    {
        // T25: for the integer types and decimal, whatever the dividend
        Assert.Equal([(ErrorIds.ConstantDivideByZero, line)], Errors(program));
    }

    [Fact]
    public void A_floating_point_division_by_a_constant_zero_is_no_error()
    {
        // T25: it yields an infinity or NaN
        Clean("print(1.0 / 0)\nprint(0.0 / 0.0)\nprint(1.0 % 0.0)");
    }

    [Fact]
    public void A_conversion_is_a_call_of_a_number_type_with_one_argument()
    {
        // T10, T13, T20, T26, T29
        Clean("long big = 5\nprint(int(big))\nprint(int(-2.5))\nprint(byte(255))\nduration d = 1s\nlongduration l = d\nprint(duration(l))");
        Assert.Equal([(ErrorIds.ArgumentMismatch, 1)], Errors("print(int(1, 2))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(int(\"5\"))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(int(true))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(int(d))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("duration d = 1s\nprint(timestamp(d))"));
    }

    [Fact]
    public void A_string_is_indexed_through_bytes_or_chars()
    {
        // T15, T16, T21
        Assert.Equal([(ErrorIds.StringIndex, 2)], Errors("s = \"abc\"\nprint(s[0])"));
        Assert.Equal("List<byte>", TypeOf("s = \"abc\"\nb = s.Bytes\nprint(b.Count)", "b"));
        Assert.Equal("List<char>", TypeOf("s = \"abc\"\nc = s.Chars\nprint(c.Count)", "c"));
        Assert.Equal("char", TypeOf("c = \"abc\".Chars[0]\nprint(c)", "c"));
        Assert.Equal("byte", TypeOf("b = \"abc\".Bytes[0]\nprint(b)", "b"));
        Clean("c = 'a'\nprint(c == 'a')\nd = 'b'\nprint(d == c)");
    }

    [Fact]
    public void A_member_a_type_does_not_have_is_unknown_name_at_the_dot()
    {
        // the reading: V10's id for a name no rule gives the type; a union is one of its cases only after a match (D9)
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("s = \"abc\"\nprint(s.Count)"));
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("xs = List<int>()\nprint(xs.Size)"));
        Assert.Equal([(ErrorIds.UnknownName, 3)], Errors("data User(int Id)\n\nprint(User(1).Name)"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("print(int.Parse(\"1\"))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("data NotFound\n\nint | NotFound Find(int id) => 10\n\nint | NotFound v = Find(1)\nprint(v.Bytes)"));
    }

    [Fact]
    public void A_list_has_add_count_where_and_an_index()
    {
        // M9, M5, T2 (an index is an int or a long)
        Clean("mut xs = List<int>()\nxs.Add(4)\nxs.Add(5)\nprint(xs.Count)\nprint(xs[1])\nprint(xs.Where(x => x > 1).Count)\nlong i = 0\nprint(xs[i])");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut xs = List<int>()\nxs.Add(\"a\")"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nprint(xs[1.5])"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 2)], Errors("mut xs = List<int>()\nxs.Add(1, 2)"));
        Assert.Equal([(ErrorIds.CannotInfer, 1)], Errors("xs = List()\nprint(xs.Count)"));
    }

    [Fact]
    public void A_mut_method_on_a_value_not_reachable_through_a_mut_variable_is_mut_required()
    {
        // M2, M5; through a path of data fields from a mut variable it compiles (the reading of M4 for paths)
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("xs = List<int>()\nxs.Add(1)\nprint(xs.Count)"));
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("void Fill(List<int> xs) {\n    xs.Add(1)\n}\n\nFill(List<int>())"));
        Assert.Equal([(ErrorIds.MutRequired, 1)], Errors("List<int>().Add(1)"));
        Clean("data Bag(List<int> Items)\n\nmut b = Bag(List<int>())\nb.Items.Add(1)\nprint(b.Items.Count)");
        Assert.Equal([(ErrorIds.MutRequired, 4)], Errors("data Bag(List<int> Items)\n\nb = Bag(List<int>())\nb.Items.Add(1)\nprint(b.Items.Count)"));
    }

    // ---- generics (T17, T19, T27) -------------------------------------------------------------------

    [Fact]
    public void A_type_argument_is_inferred_from_the_arguments_or_is_cannot_infer()
    {
        // T27: no widening between arguments; literals take what the others fix; literals alone take their constant's type
        Clean("T First<T>(List<T> items) => items[0]\n\nmut xs = List<int>()\nxs.Add(4)\nprint(First(xs))");
        Clean("T Pick<T>(T a, T b) => a\n\nbyte small = 7\nbyte r = Pick(small, 1)\nprint(r)");
        Assert.Equal("int", TypeOf("T Pick<T>(T a, T b) => a\n\nx = Pick(1, 2)\nprint(x)", "x"));
        Assert.Equal("double", TypeOf("T Pick<T>(T a, T b) => a\n\nx = Pick(1, 2.5)\nprint(x)", "x"));
        Assert.Equal([(ErrorIds.CannotInfer, 5)], Errors("T Pick<T>(T a, T b) => a\n\nbyte small = 1\nint i = 2\nprint(Pick(small, i))"));
        Assert.Equal([(ErrorIds.ConstantOverflow, 4)], Errors("T Pick<T>(T a, T b) => a\n\nbyte small = 1\nprint(Pick(small, 300))"));
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("T Make<T>() => Make<T>()\n\nprint(Make())"));
        Clean("T Pick<T>(T a, T b) => a\n\nprint(Pick<long>(1, 2))");
    }

    [Fact]
    public void A_generic_data_type_names_its_type_arguments_or_infers_them()
    {
        // T17, T19, T27, D1
        Clean("data Pair<A, B>(A First, B Second)\n\np = Pair<int, string>(1, \"one\")\nprint(p.First)\nprint(p.Second)");
        Assert.Equal("Pair<int, string>", TypeOf("data Pair<A, B>(A First, B Second)\n\np = Pair(1, \"one\")\nprint(p.First)", "p"));
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("data Pair<A, B>(A First, B Second)\n\nPair p = Pair<int, string>(1, \"one\")\nprint(p.First)"));
    }

    [Fact]
    public void Where_one_data_argument_derives_from_the_other_the_parameter_takes_the_base()
    {
        // T27's last sentence, read for data types (D6)
        Assert.Equal("User", TypeOf("data User(string Name)\ndata Admin(int Level) : User\n\nT Pick<T>(T a, T b) => a\n\nx = Pick(Admin(\"a\", 1), User(\"b\"))\nprint(x.Name)", "x"));
        Assert.Equal([(ErrorIds.CannotInfer, 6)], Errors("data User(string Name)\ndata Admin(int Level) : User\ndata Guest(int N) : User\n\nT Pick<T>(T a, T b) => a\nprint(Pick(Admin(\"a\", 1), Guest(\"b\", 2)).Name)"));
    }

    // ---- functions (chapter 8) ----------------------------------------------------------------------

    [Fact]
    public void A_function_with_a_result_whose_end_can_be_reached_is_missing_return_at_its_first_line()
    {
        // F3; a bare return in such a function too, at the return; a void function returns no value
        Assert.Equal([(ErrorIds.MissingReturn, 1)], Errors("int Sign(int n) {\n    if n < 0 {\n        return -1\n    }\n}\n\nprint(Sign(3))"));
        Assert.Equal([(ErrorIds.MissingReturn, 2)], Errors("int Sign(int n) {\n    return\n}\n\nprint(Sign(3))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("void Show(int n) {\n    return n\n}\n\nShow(3)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int Sign(int n) {\n    return \"x\"\n}\n\nprint(Sign(3))"));
        Clean("int Max(int a, int b) {\n    if a > b {\n        return a\n    }\n    return b\n}\n\nprint(Max(2, 3))");
        Clean("int Sign(int n) {\n    if n > 0 {\n        return 1\n    } else if n < 0 {\n        return -1\n    } else {\n        return 0\n    }\n}\n\nprint(Sign(5))");
        Clean("string Name(int n) {\n    match n {\n        0 => return \"zero\"\n        else => return \"many\"\n    }\n}\n\nprint(Name(0))");
    }

    [Fact]
    public void A_function_is_visible_in_the_whole_file_and_a_function_in_a_block_is_one_at_the_top_level()
    {
        // F6; F1's reading
        Clean("print(Double(4))\n\nint Double(int n) => n * 2");
        Clean("void F() {\n    int G() => 1\n    print(G())\n}\n\nF()\nprint(G())");
    }

    [Fact]
    public void A_lambda_takes_the_function_type_expected_of_it_or_needs_typed_parameters()
    {
        // F7, F11, T27
        Clean("int Apply((int) => int f, int n) => f(n)\n\nprint(Apply(x => x + 1, 4))");
        Assert.Equal([(ErrorIds.CannotInfer, 1)], Errors("f = x => x * 2\nprint(f(2))"));
        Assert.Equal("(int) => int", TypeOf("f = (int x) => x * 2\nprint(f(2))", "f"));
        Assert.Equal("() => int", TypeOf("mut n = 1\nfirst = () => n\nn = 2\nprint(first())", "first"));
        Assert.Equal("(int) => void", TypeOf("f = (int x) => print(x)\nf(1)", "f"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("int Apply((int) => int f, int n) => f(n)\n\nvoid Show(int x) {\n}\nprint(Apply(x => Show(x), 4))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("int Apply((int) => int f, int n) => f(n)\n\nprint(Apply((x, y) => x, 4))"));
        Clean("void Run((int) => void f) {\n    f(1)\n}\n\nRun(x => print(x))");
    }

    [Fact]
    public void A_lambda_cannot_change_a_variable_around_it()
    {
        // F11: an assignment, a path assignment, a mut method and a mut argument on a captured variable are capture-assign
        Assert.Equal([(ErrorIds.CaptureAssign, 5)], Errors("mut total = 0\nmut xs = List<int>()\nxs.Add(1)\nbig = xs.Where(x => {\n    total = total + x\n    return x > 0\n})\nprint(big.Count)\nprint(total)"));
        Assert.Equal([(ErrorIds.CaptureAssign, 2)], Errors("mut ys = List<int>()\nf = (int x) => ys.Add(x)\nf(1)\nprint(ys.Count)"));
        Assert.Equal([(ErrorIds.CaptureAssign, 4)], Errors("data Point(int X)\n\nmut p = Point(1)\nf = (int x) => { p.X = x }\nf(2)\nprint(p.X)"));
        Assert.Equal([(ErrorIds.CaptureAssign, 6)], Errors("void Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nmut numbers = List<int>()\nf = (int x) => Fill(mut numbers)\nf(1)\nprint(numbers.Count)"));
        Clean("limit = 1\nmut xs = List<int>()\nxs.Add(1)\nprint(xs.Where(x => x > limit).Count)");
    }

    [Fact]
    public void Two_functions_whose_parameters_do_not_differ_are_duplicate_function_at_the_second()
    {
        // F12: names, defaults and the mut marker make no difference; the call carries no second error
        Assert.Equal([(ErrorIds.DuplicateFunction, 5)], Errors("void Fill(List<int> xs) {\n    print(xs.Count)\n}\n\nvoid Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nFill(List<int>())"));
        Assert.Equal([(ErrorIds.DuplicateFunction, 2)], Errors("int F(int a) => a\nint F(int b = 1) => b\nprint(F(1))"));
        Clean("void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(string s) {\n    print(\"text {s}\")\n}\n\nvoid Show(int a, int b) {\n    print(\"pair {a} {b}\")\n}\n\nShow(4)\nShow(\"four\")\nShow(4, 5)");
    }

    [Fact]
    public void A_call_that_fits_more_than_one_function_none_exactly_is_ambiguous_call()
    {
        // F13, F14: an exact fit wins; a literal has its own type for the pick; the function without type parameters wins over a generic one
        var shows = "void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(long n) {\n    print(\"long {n}\")\n}\n\n";
        Assert.Equal([(ErrorIds.AmbiguousCall, 10)], Errors(shows + "short s = 4\nShow(s)"));
        Clean(shows + "int i = 4\nlong big = 4\nShow(i)\nShow(big)\nShow(4)\nShow(4000000000)\nShow(4L)");
        Clean("void Show(short n) {\n    print(\"short {n}\")\n}\n\nvoid Show(long n) {\n    print(\"long {n}\")\n}\n\nShow(4)");
        Clean("void Show<T>(T x) {\n    print(1)\n}\n\nvoid Show(int n) {\n    print(n)\n}\n\nShow(4)");
        Clean("void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(int a, int b = 0) {\n    print(\"two {a} {b}\")\n}\n\nint i = 4\nShow(i)");
    }

    [Theory]
    [InlineData("int Add(int a, int b) => a + b\n\nprint(Add(1))", 3)]
    [InlineData("int Add(int a, int b) => a + b\n\nprint(Add(1, 2, 3))", 3)]
    [InlineData("int Add(int a, int b) => a + b\n\nprint(Add(1, c: 2))", 3)]
    [InlineData("int Add(int a, int b) => a + b\n\nprint(Add(1, a: 2))", 3)]
    [InlineData("int Add(int a, int b) => a + b\n\nprint(Add(a: 1, 2))", 3)]
    [InlineData("data Item(int ProductId, int Count = 1)\n\nprint(Item(7, ProductId: 8).Count)", 3)]
    [InlineData("print(1, 2)", 1)]
    [InlineData("print()", 1)]
    public void An_argument_no_parameter_takes_is_argument_mismatch_at_the_call(string program, int line)
    {
        // D11, A1
        Assert.Equal([(ErrorIds.ArgumentMismatch, line)], Errors(program));
    }

    [Fact]
    public void Defaults_and_named_arguments_work_as_in_c_sharp_before_7_2()
    {
        // D11, F12
        Clean("void Greet(string name, string word = \"hello\") {\n    print(\"{word} {name}\")\n}\n\nGreet(\"Ann\")\nGreet(\"Ann\", \"bye\")\nGreet(\"Ann\", word: \"bye\")");
        Clean("data Item(int ProductId, int Count = 1)\n\na = Item(7)\nb = Item(Count: 5, ProductId: 7)\nprint(a.Count)\nprint(b.ProductId)");
    }

    [Fact]
    public void A_default_that_is_no_constant_is_default_not_constant_at_the_parameter()
    {
        // F12: a literal, a data value without fields, or literals that T6 folds
        Assert.Equal([(ErrorIds.DefaultNotConstant, 3)], Errors("string Pick() => \"hi\"\n\nvoid Greet(string name, string word = Pick()) {\n    print(\"{word} {name}\")\n}\n\nGreet(\"Ann\")"));
        Clean("data Empty\n\nvoid F(int n = 1 + 2, Empty e = Empty, double d = 1) {\n    print(n)\n    print(e)\n    print(d)\n}\n\nF()");
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("void F(int n = \"a\") {\n    print(n)\n}\n\nF()"));
    }

    [Theory]
    [InlineData("mut count = 0\n\nint Next() => count + 1\n\ncount = Next()\nprint(count)", ErrorIds.UnknownName, 3)]
    [InlineData("mut count = 0\n\nvoid Reset() {\n    count = 0\n}\n\nReset()\nprint(count)", ErrorIds.Redeclared, 4)]
    [InlineData("mut count = 0\n\nint Next(int count) => count + 1\n\ncount = Next(count)\nprint(count)", ErrorIds.Redeclared, 3)]
    [InlineData("mut count = 0\n\nvoid Bump() {\n    count = count + 1\n}\n\nBump()\nprint(count)", ErrorIds.Redeclared, 4)]
    [InlineData("void Bump() {\n    for count in 0..<2 {\n        print(count)\n    }\n}\n\nmut count = 0\nBump()\nprint(count)", ErrorIds.Redeclared, 2)]
    [InlineData("void Bump() {\n    f = (int count) => count\n    print(f(1))\n}\n\nif true {\n    count = 0\n    print(count)\n}\nBump()", ErrorIds.Redeclared, 2)]
    [InlineData("string Email(int id) => \"a\"\n\nfor id in 0..2 {\n    print(Email(id))\n}", ErrorIds.Redeclared, 1)]
    public void A_top_level_function_neither_reads_nor_declares_a_top_level_variable(string program, string id, int line)
    {
        // F16: the read is unknown-name, every declaration of the name is redeclared, the one error for `count = count + 1`; the name is reserved at any
        // depth, above or below, and a loop variable of the top-level code reserves it too (the reading in the README)
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void A_lambda_at_the_top_level_reads_the_top_level_variables()
    {
        // F15: a lambda captures; F16 is about functions
        Clean("mut n = 1\nfirst = () => n\nn = 2\nprint(first())\nprint(n)");
    }

    [Fact]
    public void A_function_name_where_a_value_stands_and_a_value_on_its_own_as_a_statement_are_syntax()
    {
        // E4's readings: F11 gives a function value through a lambda only; F5 makes a statement of a call
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("int Double(int n) => n * 2\n\nf = Double\nprint(f(1))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("x = 1\nx + 1"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("1..3"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(int)"));
    }

    // ---- mut parameters (M7) --------------------------------------------------------------------------

    [Fact]
    public void A_mut_parameter_takes_a_mut_variable_marked_mut_at_the_call()
    {
        // M7: leaving mut out, writing it for a parameter that is not mut, or passing one variable twice is mut-at-call; an immutable variable is mut-required
        var fill = "void Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\n";
        Clean(fill + "mut numbers = List<int>()\nFill(mut numbers)\nprint(numbers.Count)");
        Assert.Equal([(ErrorIds.MutAtCall, 6)], Errors(fill + "mut numbers = List<int>()\nFill(numbers)\nprint(numbers.Count)"));
        Assert.Equal([(ErrorIds.MutRequired, 6)], Errors(fill + "numbers = List<int>()\nFill(mut numbers)\nprint(numbers.Count)"));
        Assert.Equal([(ErrorIds.MutAtCall, 5)], Errors(fill + "Fill(mut List<int>())"));
        Assert.Equal([(ErrorIds.MutAtCall, 6)], Errors("void Fill(List<int> xs) {\n    print(xs.Count)\n}\n\nmut numbers = List<int>()\nFill(mut numbers)"));
        Assert.Equal([(ErrorIds.MutAtCall, 6)], Errors("void Swap(mut List<int> a, mut List<int> b) {\n    a.Add(b.Count)\n}\n\nmut xs = List<int>()\nSwap(mut xs, mut xs)"));
        Clean("data Bag(List<int> Items)\n\n" + fill + "mut b = Bag(List<int>())\nFill(mut b.Items)\nprint(b.Items.Count)");
    }

    // ---- control flow (chapter 10) --------------------------------------------------------------------

    [Fact]
    public void A_condition_is_a_bool()
    {
        // C3, C5
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 1\nif x {\n    print(\"yes\")\n}"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 1\nwhile x {\n    print(\"yes\")\n}"));
        Clean("x = 7\nif (x > 5) {\n    print(\"big\")\n}");
    }

    [Fact]
    public void Break_and_continue_need_a_loop_around_them_and_a_lambda_has_none()
    {
        // C7: inside a match arm they mean the loop around the match
        Assert.Equal([(ErrorIds.BreakOutsideLoop, 2)], Errors("print(1)\nbreak"));
        Assert.Equal([(ErrorIds.BreakOutsideLoop, 2)], Errors("for i in 1..3 {\n    f = () => { continue }\n    f()\n}"));
        Clean("for i in 1..5 {\n    match i {\n        3 => break\n        else => print(i)\n    }\n}");
        Clean("mut xs = List<int>()\nxs.Add(1)\nfor x in xs {\n    if x == 2 {\n        continue\n    }\n    if x == 4 {\n        break\n    }\n    print(x)\n}");
    }

    [Theory]
    [InlineData("for i in 3..2 {\n    print(i)\n}", ErrorIds.ReversedRange, 1)]
    [InlineData("for i in 3..<2 {\n    print(i)\n}", ErrorIds.ReversedRange, 1)]
    [InlineData("for i in 0..<1.5 {\n    print(i)\n}", ErrorIds.TypeMismatch, 1)]
    [InlineData("for i in 0..2.0 {\n    print(i)\n}", ErrorIds.TypeMismatch, 1)]
    [InlineData("long n = 3\nfor i in 0..<n {\n    int x = i\n    print(x)\n}", ErrorIds.NarrowingConversion, 3)]
    [InlineData("s = \"ab\"\nfor c in s {\n    print(c)\n}", ErrorIds.TypeMismatch, 2)]
    public void A_range_counts_integers_up_and_a_loop_walks_a_list_or_a_range(string program, string id, int line)
    {
        // C8, C6
        Assert.Equal([(id, line)], Errors(program));
    }

    [Theory]
    [InlineData("for i in 1..3 {\n    print(i)\n}", "int")]
    [InlineData("long n = 3\nfor i in 0..<n {\n    print(i)\n}", "long")]
    [InlineData("byte n = 3\nfor i in 0..<n {\n    print(i)\n}", "int")]
    [InlineData("for i in \"ab\".Chars {\n    print(i)\n}", "char")]
    public void The_loop_variable_has_the_type_of_the_range_or_the_element(string program, string type)
    {
        // C8: the common type of the ends, found as `+` finds it; C6
        var (diagnostics, checker) = Check(program);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(type, Assert.Single(checker.Variables, v => v.Name == "i").Type.Text);
    }

    [Fact]
    public void Match_covers_every_case_or_ends_in_else()
    {
        // O8, C9, C4
        var find = "data NotFound\n\nint | NotFound Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\n";
        Assert.Equal([(ErrorIds.MatchNotExhaustive, 10)], Errors(find + "match Find(2) {\n    int n => print(n)\n}"));
        Assert.Equal([(ErrorIds.MatchNotExhaustive, 2)], Errors("n = 3\nmatch n {\n    0 => print(\"zero\")\n}"));
        Clean(find + "match Find(2) {\n    int n => print(n)\n    NotFound => print(\"none\")\n}");
        Clean("n = 3\nmatch n {\n    0 => print(\"zero\")\n    else => print(\"many\")\n}");
        Clean("b = true\nmatch b {\n    true => print(1)\n    false => print(0)\n}");
    }

    [Theory]
    [InlineData("data NotFound(int Id)\n\nvoid Show(int | NotFound v) {\n    match v {\n        int x => print(x)\n        0 => print(\"zero\")\n        NotFound n => print(n.Id)\n    }\n}\n\nShow(0)", ErrorIds.UnreachableArm, 6)]
    [InlineData("data Yes(int Count)\ndata No(string Why)\n\nvoid Show(Yes | No v) {\n    match v {\n        Yes y => print(y.Count)\n        No n => print(n.Why)\n        else => print(\"other\")\n    }\n}\n\nShow(Yes(1))", ErrorIds.UnreachableArm, 8)]
    [InlineData("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nvoid Show(User | NotFound u) {\n    match u {\n        User x => print(\"user {x.Name}\")\n        Admin a => print(\"admin {a.Level}\")\n        NotFound => print(\"none\")\n    }\n}\n\nShow(Admin(\"Ann\", 2))", ErrorIds.UnreachableArm, 8)]
    [InlineData("n = 1\nmatch n {\n    1 => print(\"one\")\n    1 => print(\"again\")\n    else => print(\"many\")\n}", ErrorIds.UnreachableArm, 4)]
    [InlineData("data Yes\ndata No\n\nvoid Show(Yes | No v) {\n    match v {\n        Yes => print(1)\n        string s => print(s)\n        No => print(0)\n    }\n}\n\nShow(Yes)", ErrorIds.TypeMismatch, 7)]
    [InlineData("n = 1\nmatch n {\n    \"one\" => print(1)\n    else => print(0)\n}", ErrorIds.TypeMismatch, 3)]
    public void An_arm_an_earlier_one_covers_is_unreachable_and_a_type_that_is_no_case_is_type_mismatch(string program, string id, int line)
    {
        // C4, C9: a derived data type before its base is fine, after it unreachable; a literal after the arm of its type; else after every case
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void A_match_used_as_a_value_has_the_type_its_arms_share()
    {
        // C9: where a type is written or expected each arm fits it; where none is, one type
        Clean("string Name(int n) => match n {\n    0 => \"zero\"\n    1 => \"one\"\n    else => \"many\"\n}\n\nprint(Name(0))");
        Assert.Equal("string", TypeOf("n = 1\ns = match n {\n    0 => \"zero\"\n    else => \"many\"\n}\nprint(s)", "s"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("n = 1\ns = match n {\n    0 => \"zero\"\n    else => 1\n}\nprint(s)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("n = 1\nstring s = match n {\n    0 => \"zero\"\n    else => 1\n}\nprint(s)"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("n = 1\ns = match n {\n    0 => break\n    else => \"many\"\n}\nprint(s)"));
        Clean("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nvoid Show(User | NotFound u) {\n    match u {\n        Admin a => print(\"admin {a.Level}\")\n        User x => print(\"user {x.Name}\")\n        NotFound => print(\"none\")\n    }\n}\n\nShow(Admin(\"Ann\", 2))");
    }

    // ---- outcomes (chapter 9) ---------------------------------------------------------------------------

    [Fact]
    public void A_call_yields_its_success_value_and_its_other_cases_have_to_be_listed()
    {
        // O1, O2, O3, O9
        var find = "data NotFound\n\nint | NotFound Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\n";
        Clean(find + "int | NotFound Twice(int id) {\n    value = Find(id)\n    return value * 2\n}\n\nmatch Twice(1) {\n    int n => print(n)\n    NotFound => print(\"none\")\n}");
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors(find + "int Twice(int id) {\n    value = Find(id)\n    return value * 2\n}\n\nprint(Twice(1))"));
        Assert.Equal([(ErrorIds.UnlistedCase, 10)], Errors(find + "value = Find(2)\nprint(value)"));
        Assert.Equal("int", TypeOf(find + "int | NotFound Twice(int id) => Find(id) * 2\n\nx = Twice(1) else {\n    NotFound => 0\n}\nprint(x)", "x"));
    }

    [Fact]
    public void A_written_union_type_keeps_every_case_and_names_them_all()
    {
        // O4: the success case alone unwraps; a type the success converts to counts as the success
        var find = "data NotFound\ndata Invalid\n\nint | NotFound | Invalid Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    if id == 2 {\n        return Invalid\n    }\n    return NotFound\n}\n\n";
        Assert.Equal([(ErrorIds.TypeMismatch, 14)], Errors(find + "int | NotFound result = Find(1)\nmatch result {\n    int n => print(n)\n    NotFound => print(\"none\")\n}"));
        Clean(find + "int | NotFound | Invalid result = Find(1)\nmatch result {\n    int n => print(n)\n    NotFound => print(\"none\")\n    Invalid => print(\"invalid\")\n}");
        Assert.Equal([(ErrorIds.UnlistedCase, 14)], Errors(find + "long result = Find(1)\nprint(result)"));
    }

    [Fact]
    public void Postfix_else_handles_the_listed_cases_at_the_call()
    {
        // O5, O7: an arm recovers with a value of the success type, returns another case, or throws; a case not listed propagates
        var find = "data NotFound\n\nint | NotFound Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\n";
        Clean(find + "int OrZero(int id) {\n    value = Find(id) else {\n        NotFound => 0\n    }\n    return value\n}\n\nprint(OrZero(1))");
        Clean(find + "data Missing(int Id)\n\nint | Missing Need(int id) {\n    value = Find(id) else {\n        NotFound => return Missing(id)\n    }\n    return value\n}\n\nmatch Need(2) {\n    int n => print(n)\n    Missing m => print(\"missing {m.Id}\")\n}");
        Clean(find + "print(\"before\")\nvalue = Find(2) else {\n    NotFound => throw\n}\nprint(value)");
        Clean(find + "value = Find(2) else {\n    NotFound => {\n        print(\"none\")\n        0\n    }\n}\nprint(value)");
        Assert.Equal([(ErrorIds.TypeMismatch, 12)], Errors(find + "int OrZero(int id) {\n    value = Find(id) else {\n        NotFound => \"none\"\n    }\n    return value\n}\n\nprint(OrZero(1))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 12)], Errors(find + "int OrZero(int id) {\n    value = Find(id) else {\n        string s => 0\n    }\n    return value\n}\n\nprint(OrZero(1))"));
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors("data NotFound\ndata Invalid\n\nint | NotFound | Invalid Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\nvalue = Find(2) else {\n    NotFound => 0\n}\nprint(value)"));
    }

    [Fact]
    public void A_bare_throw_stands_only_in_an_arm_of_else()
    {
        // O7
        Assert.Equal([(ErrorIds.ThrowNeedsValue, 2)], Errors("void Fail() {\n    throw\n}\n\nFail()"));
        Clean("void Check(int n) {\n    if n < 0 {\n        throw \"negative\"\n    }\n}\n\nCheck(-1)");
        Clean("data ConfigMissing(string Path)\n\nthrow ConfigMissing(\"app.json\")");
    }

    [Fact]
    public void A_void_success_makes_the_call_a_statement_and_its_use_as_a_value_syntax()
    {
        // O10
        var remove = "data NotFound\n\nvoid | NotFound Remove(int id) {\n    if id != 1 {\n        return NotFound\n    }\n    print(\"removed\")\n}\n\n";
        Clean(remove + "Remove(1) else {\n    NotFound => print(\"none\")\n}");
        Assert.Equal([(ErrorIds.Syntax, 10)], Errors(remove + "x = Remove(1) else {\n    NotFound => print(\"none\")\n}"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Show() {\n}\nx = Show()"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Show() {\n}\nprint(Show())"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Show() {\n}\nprint(\"{Show()}\")"));
    }

    // ---- data (chapter 5) ---------------------------------------------------------------------------------

    [Fact]
    public void A_data_value_is_made_by_its_name_and_read_with_a_dot()
    {
        // D1, D2, D6, D7, D8, D9
        Clean("data User(int Id, string Name)\n\nu = User(1, \"Ann\")\nprint(u.Id)\nprint(u.Name)\nprint(u)\nprint(\"got {u}\")");
        Clean("data User(int Id, string Name)\ndata Admin(int Level) : User\n\nstring NameOf(User u) => u.Name\n\na = Admin(1, \"Ann\", 3)\nprint(a.Level)\nprint(NameOf(a))");
        Clean("data Empty\n\ne = Empty\nprint(e == Empty)");
        Clean("data Circle(int Radius)\ndata Square(int Side)\n\nvoid Describe(Circle | Square shape) {\n    match shape {\n        Circle c => print(\"circle {c.Radius}\")\n        Square s => print(\"square {s.Side}\")\n    }\n}\n\nDescribe(Circle(2))");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("data User(int Id)\n\nu = User(\"one\")\nprint(u.Id)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data User(int Id)\ndata Admin(int Level) : User\n\nAdmin a = User(1)\nprint(a.Level)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 3)], Errors("data Empty\n\nprint(Empty(1))"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("data User(int Id)\n\nprint(User)"));
    }

    [Fact]
    public void A_field_is_assigned_through_a_mut_variable_only()
    {
        // D4, M4: the variable decides, not the field; through a path too
        Assert.Equal([(ErrorIds.AssignImmutable, 4)], Errors("data Point(int X, int Y)\n\np = Point(1, 2)\np.X = 3\nprint(p.X)"));
        Clean("data Point(int X, int Y)\n\nmut p = Point(1, 2)\np.X = 3\nprint(p.X)");
        Clean("data Address(string City)\ndata Customer(string Name, Address Home)\n\nmut c = Customer(\"Ann\", Address(\"Graz\"))\nc.Home.City = \"Wien\"\nprint(c.Home.City)");
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data Point(int X, int Y)\n\nmut p = Point(1, 2)\np.X = \"a\"\nprint(p.X)"));
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("mut xs = List<int>()\nxs.Count = 3"));
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("data Point(int X)\n\nPoint(1).X = 2"));
    }

    [Fact]
    public void With_copies_a_data_value_with_fields_replaced()
    {
        // D5
        Assert.Equal("User", TypeOf("data User(int Id, string Name)\n\na = User(1, \"Ann\")\nb = a with { Name = \"Bea\" }\nprint(b.Name)", "b"));
        Assert.Equal([(ErrorIds.UnknownName, 4)], Errors("data User(int Id, string Name)\n\na = User(1, \"Ann\")\nb = a with { Age = 3 }\nprint(b.Name)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data User(int Id, string Name)\n\na = User(1, \"Ann\")\nb = a with { Id = \"x\" }\nprint(b.Name)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 1\ny = x with { A = 2 }\nprint(y)"));
    }

    [Fact]
    public void Data_values_compare_by_content_within_one_type()
    {
        // D3, D15: `==` between a derived value and its base compiles; between unrelated data types it is type-mismatch
        Clean("data Point(int X, int Y)\n\nprint(Point(1, 2) == Point(1, 2))");
        Clean("data User(int Id)\ndata Admin(int Level) : User\n\nprint(Admin(1, 3) == User(1))");
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data A(int X)\ndata B(int X)\n\nprint(A(1) == B(1))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("data A(int X)\nprint(A(1) < A(2))"));
    }

    [Fact]
    public void A_data_type_declared_twice_and_a_field_declared_twice_are_redeclared()
    {
        // V9 for declarations in every form
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("data A(int X)\ndata A(int Y)\n\nprint(A(1).X)"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data A(int X, int X)\n\nprint(A(1, 2).X)"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("data User(int Id)\ndata Admin(int Id) : User\n\nprint(Admin(1, 2).Id)"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("data A(int X)\nint A(int x) => x\n\nprint(A(1).X)"));
    }

    // ---- print and texts (A1 to A14) -------------------------------------------------------------------------

    [Fact]
    public void Print_takes_a_value_with_a_text_and_a_type_parameter_without_a_limit_has_none()
    {
        // A1, A2 to A4, A8, A10, A11, A12, A14
        Clean("print(1)\nprint(\"a\")\nprint(true)\nprint('c')\nprint(0.5)\nprint(90min)\nprint(1.5kb)\nxs = List<int>()\nprint(xs)");
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("void Show<T>(T item) {\n    print(item)\n}\n\nShow(1)"));
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("void Show<T>(T item) {\n    print(\"got {item}\")\n}\n\nShow(1)"));
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("f = (int x) => x\nprint(f)"));
    }

    // ---- what the checker does not read ---------------------------------------------------------------------

    [Theory]
    [InlineData("class Counter(mut int Count)\n\nc = Counter(0)\nprint(c)", "1: a class (chapter 7)")]
    [InlineData("interface Shape {\n    int Area()\n}", "1: an interface (K6, K10)")]
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan.Free)", "1: an enum (D12)")]
    [InlineData("flags Access { Read, Write }\n\nprint(Access.Read)", "1: a flags type (D13)")]
    [InlineData("string? Email(int id) => null\n\nprint(1)", "1: a nullable type (N1)")]
    [InlineData("x = null\nprint(1)", "1: null (N1)")]
    [InlineData("data User(string Name)\n\nu = User(\"a\")\nprint(u?.Name)", "4: `?.` (N5)")]
    [InlineData("x = 1\nprint(x ?? 2)", "2: `??` (N4)")]
    [InlineData("mut ages = Map<string, int>()\nages[\"Ann\"] = 31", "1: Map (M9)")]
    [InlineData("raw {\n    print(1)\n}", "1: a raw block (R7)")]
    [InlineData("use tools\nprint(1)", "1: use (F10)")]
    [InlineData("data Counter(int Count) {\n    pub mut void Increment() {\n        Count = Count + 1\n    }\n}\n\nc = Counter(0)\nprint(c.Count)", "2: the body of a data type (D16)")]
    [InlineData("print(string(1))", "1: a conversion to `string`")]
    [InlineData("xs = List<int>()\nxs[0] = 1", "2: an assignment into an index (M9)")]
    [InlineData("data Shape\n\nint AreaOf<T: Shape>(T item) => 1\n\nprint(AreaOf(5))", "3: the limit of a type parameter (T19)")]
    public void A_construct_of_a_chapter_the_checker_does_not_read_is_not_supported_and_carries_no_error(string program, string first)
    {
        Assert.Equal(first, NotSupported(program)[0]);
    }

    [Fact]
    public void A_value_the_checker_could_not_type_carries_no_further_error()
    {
        // E1: one defect, one diagnostic; a class instance's fields and members are its chapter's business
        var (diagnostics, _) = Check("class Counter(mut int Count) {\n    pub void Add() {\n        Count = Count + 1\n    }\n}\n\nc = Counter(0)\nc.Count = 2\nc.Add()\nprint(c.Count)");
        Assert.DoesNotContain(diagnostics.Items, d => d.IsError);
        Assert.Single(diagnostics.Items, d => !d.IsError);
    }
}
