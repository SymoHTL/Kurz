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
    /// <summary>The checker's reading of a program, with the grants a case's header line would give it, <c>raw</c>, <c>native</c> or both (R7).</summary>
    private static (DiagnosticBag Diagnostics, Checker Checker) Check(string program, string grants = "")
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        var checker = Checker.Check(tree, diagnostics, grants.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (diagnostics, checker);
    }

    /// <summary>The compile errors of a program the checker reads in full, by line.</summary>
    private static List<(string Id, int Line)> Errors(string program, string grants = "")
    {
        var (diagnostics, _) = Check(program, grants);
        Assert.DoesNotContain(diagnostics.Items, d => !d.IsError);
        return diagnostics.Sorted.Select(d => (d.Id, d.Line)).ToList();
    }

    /// <summary>What the checker does not read, as <c>line: construct</c>; the helper asserts that no diagnostic of the program is an error.</summary>
    private static List<string> NotSupported(string program, string grants = "")
    {
        var (diagnostics, _) = Check(program, grants);
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

    private static void Clean(string program, string grants = "")
    {
        var (diagnostics, _) = Check(program, grants);
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
        // M4: `p.X = 3` is short for `p = p with { X = 3 }`, which reads `p`; the add and the mut argument hand the value to code that reads
        // it; V6's second assignment of a whole value (above) reads nothing
        Clean("mut xs = List<int>()\nxs.Add(1)");
        Clean("data Point(int X, int Y)\n\nmut p = Point(1, 2)\np.X = 3");
        Clean("void Fill(mut List<int> xs) {\n    xs.Add(1)\n}\n\nmut numbers = List<int>()\nFill(mut numbers)");
    }

    [Fact]
    public void A_parameter_and_a_loop_variable_that_are_never_read_are_no_error()
    {
        // V13; a binding of a match arm too (the reading in the README: the arm declares it)
        Clean("int Three(int ignored) => 3\n\nfor i in 0..<2 {\n    print(Three(1))\n}");
        Clean("data Found(int Id)\ndata NotFound\n\nvoid Show(Found | NotFound v) {\n    match v {\n        Found f => print(1)\n        NotFound => print(0)\n    }\n}\n\nShow(NotFound)");
    }

    [Fact]
    public void An_error_in_a_construct_over_several_lines_is_reported_at_the_line_of_the_part_that_is_wrong()
    {
        // E1: an argument reports its own line, a binary operator its operator's line
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("int Add(int a, int b) => a + b\n\nprint(Add(1,\n    \"two\"))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(1 +\n    \"two\")"));
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
        Assert.Equal([(ErrorIds.Redeclared, 4)], Errors("x = \"a\"\nmut xs = List<int>()\nxs.Add(1)\nfor x in xs {\n    print(x + 1)\n}\nprint(x)")); // E1: the inner `x` is bound to no type, so `x + 1` is no second error and the outer `x` is untouched
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
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("int F(int print) => print\n\nprint(F(1))")); // a parameter, as a local
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data List(int X)\n\nprint(1)")); // a type named as a built-in could never be named (M9, T17)
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
    [InlineData("short s = -(5)\nprint(s)", "s", "short")]
    [InlineData("short s = -(1 + 2)\nprint(s)", "s", "short")]
    [InlineData("byte m = ~0x0F\nprint(m)", "m", "byte")]
    [InlineData("x = byte(200) + byte(100)\nprint(x)", "x", "int")]
    public void A_written_or_expected_type_reaches_the_literals_of_a_constant_expression(string program, string variable, string type)
    {
        // L17: the whole expression when it is made of literals; an integer literal beside an operand that has a type takes that type, while
        // a fraction literal keeps its own (`b + 1.5` on a byte is a double); a conversion reaches the integer literals inside it and leaves a
        // fraction or exponent literal its own type, so `float(1e300)` converts a double to a float, which T26 makes an infinity and no error;
        // `-` and `~` before an expression of literals fold in the type the context reached it with, as the binary operators do, since T28's
        // promotion is for the operands of a run-time operation (L17), so `~0x0F` as a byte is 240; a conversion is no literal, so the sum of
        // two converted constants is promoted (T28) and `byte(200) + byte(100)` is the `int` 300
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
    [InlineData("print(-5U)", 1)]
    [InlineData("print(decimal(79228162514264337593543950336.0))", 1)]
    [InlineData("duration d = 200000days\nprint(d)", 1)]
    [InlineData("byte b = -(5)\nprint(b)", 1)]
    public void A_constant_that_does_not_fit_the_type_it_takes_is_constant_overflow(string program, int line)
    {
        // T6, L10, L11, L17, T29: wherever the literal stands, and one operation at a time; `b + 300` on a byte is 300 as a byte, since the
        // literal takes the other operand's type (L17) before the operation's type is found (T28); a `-` written directly before a literal
        // belongs to the literal (L17), so `uint u = -1` is the literal -1 under `uint` and `-5U` the literal -5, which fits none of the types
        // the suffix names (L10): neither is the `-` on a `uint` value that no rule gives a meaning (the not-supported theory below); `-(5)`
        // under a byte folds in the byte its literal takes (L17); the double that equals 2^96 lies one above the range of decimal
        Assert.Equal([(ErrorIds.ConstantOverflow, line)], Errors(program));
    }

    [Fact]
    public void An_integer_literal_beyond_the_range_of_double_that_a_floating_type_reaches_is_constant_overflow()
    {
        // T6, L11: the same bound as for a literal with an exponent
        Assert.Equal([(ErrorIds.ConstantOverflow, 1)], Errors("double d = 1" + new string('0', 310) + "\nprint(d)"));
        Assert.Equal([(ErrorIds.ConstantOverflow, 1)], Errors("x = double(1" + new string('0', 310) + ")\nprint(x)"));
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
        // T6, T12: `2147483647 +% 1` is `-2147483648`; two literals of one type wrap in that type (L17), so `200 +% 100` as bytes is 44;
        // a floating-point operand is type-mismatch
        Clean("print(2147483647 +% 1)");
        Assert.Equal("byte", TypeOf("byte b = 200 +% 100\nprint(b)", "b"));
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
    [InlineData("sbyte s = 1\nuint u = 1\nprint(s + u)", 3)]
    [InlineData("short s = 1\nulong u = 1\nprint(s + u)", 3)]
    public void Signed_and_unsigned_do_not_meet(string program, int line)
    {
        // T9: judged on the two operand types as written, before the promotion of T28. Two narrow operands are an `int` whatever their
        // signedness, an unsigned narrow operand beside a `uint` or a `ulong` takes that type (T28), and T23 widens `uint` into `long`; what is
        // left is a `uint` or a `ulong` beside a signed type it does not widen into, a narrow signed one included: a signed narrow operand is
        // never promoted into an unsigned type, so `sbyte + uint` (a `long` in C#) and `short + ulong` are this error. The wrapping operators
        // mix no signedness whatever the widths (T28); `-` on a `uint` or a `ulong` value is not read (the not-supported theory below)
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
        // T18, T28 (two narrow operands are an `int`, `sbyte + byte` among them; a narrow operand beside a `uint`, a `long` or a `ulong` takes
        // that type, so `uint + byte` is a `uint`; a shift on a narrow operand is an `int`; `-` and `~` promote too), T23 (`uint + long` is a
        // `long`, as `uint` widens into `long`), T12 (the wrapping operators do not promote), T29
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
        // T22, T28: the count is taken modulo the width, 32 for a narrow left operand; two literals fold in the type they take (L17), and a
        // shift is not arithmetic in the sense of T6: the bits that leave the type are dropped at every width, so `1 << 31` is -2147483648,
        // `0x40000000 << 2` is 0 and `byte b = 1 << 9` is 0, none of them an error; a conversion is no literal, so `byte(1) << 9` is promoted to
        // `int` (T28); a narrow left operand that is no constant is promoted to `int`, so `1 << n` as a byte is narrowing
        Clean("print(6 & 3)\nprint(6 | 3)\nprint(6 ^ 3)\nprint(~6)\nprint(1 << 4)\nprint(-16 >> 2)\nprint(1 << 31)\nprint(1 << 33)\nprint(1 << -1)\nprint(0x40000000 << 2)");
        Clean("byte b = 1 << 7\nprint(b)");
        Assert.Equal("byte", TypeOf("byte b = 1 << 9\nprint(b)", "b"));
        Assert.Equal("int", TypeOf("x = byte(1) << 9\nprint(x)", "x"));
        Assert.Equal([(ErrorIds.NarrowingConversion, 2)], Errors("n = 3\nbyte b = 1 << n\nprint(b)"));
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
        // T10, T13, T20, T26, T29; L17: the target reaches the integer literals inside, so `double(1 / 2)` is 0.5; M7: `mut` before the argument
        Clean("long big = 5\nprint(int(big))\nprint(int(-2.5))\nprint(byte(255))\nduration d = 1s\nlongduration l = d\nprint(duration(l))");
        Assert.Equal("double", TypeOf("x = double(1 / 2)\nprint(x)", "x"));
        Assert.Equal([(ErrorIds.MutAtCall, 2)], Errors("mut x = 1\nprint(int(mut x))"));
        Assert.Equal([(ErrorIds.MutAtCall, 1)], Errors("print(byte(mut 300))")); // E1: after the call's own error nothing more
        Assert.Equal([(ErrorIds.MutAtCall, 2)], Errors("f = (int x) => x\nprint(mut f)"));
        Clean("print(1m / (decimal(123456789.123456789) - 123456789.123457m))"); // T6: the double's value rounded to decimal's precision, not to 15 digits
        Clean("print(1m / (decimal(0.1) - 0.1m))");
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
        Assert.Equal("Bytes", TypeOf("s = \"abc\"\nb = s.Bytes\nprint(b.Count)", "b")); // M9: the second name of a list of bytes
        Assert.Equal("List<char>", TypeOf("s = \"abc\"\nc = s.Chars\nprint(c.Count)", "c"));
        Assert.Equal("char", TypeOf("c = \"abc\".Chars[0]\nprint(c)", "c"));
        Assert.Equal("byte", TypeOf("b = \"abc\".Bytes[0]\nprint(b)", "b"));
        Clean("c = 'a'\nprint(c == 'a')\nd = 'b'\nprint(d == c)");
    }

    [Fact]
    public void Bytes_is_the_second_name_of_a_list_of_bytes()
    {
        // M9 (section 4, the owner, 2026-10-29, "Bytes = List<byte>"): one type under two names, which a message names `Bytes`
        Assert.Equal("Bytes", TypeOf("List<byte> b = Bytes()\nprint(b)", "b"));
        Assert.Equal("Bytes", TypeOf("Bytes b = List<byte>()\nprint(b)", "b"));
        Assert.Equal("List<int>", TypeOf("xs = List<int>()\nprint(xs)", "xs"));
        Clean("void Show(List<byte> b) => print(b)\n\nShow(\"ab\".Bytes)\nShow(Bytes())");
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("Bytes<byte> b = Bytes()\nprint(b)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 1)], Errors("b = Bytes(1)\nprint(b)"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data Bytes(int Count)"));
        var (diagnostics, _) = Check("int x = Bytes()\nprint(x)");
        Assert.Contains("`Bytes`", Assert.Single(diagnostics.Items).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_become_a_string_through_FromUtf8_whose_other_case_is_InvalidUtf8()
    {
        // T15 (section 4, the owner, 2026-10-29, "Union, as Rust"): a union whose case leaves as every case does (O1, O3, O9)
        Clean("b = Bytes()\nmatch string.FromUtf8(b) {\n    string s => print(s)\n    InvalidUtf8 e => print(e.At)\n}");
        Clean("b = Bytes()\nprint(string.FromUtf8Lossy(b))");
        Clean("string | InvalidUtf8 Text(Bytes b) {\n    s = string.FromUtf8(b)\n    return \"<{s}>\"\n}\n\nmatch Text(\"ab\".Bytes) {\n    string s => print(s)\n    InvalidUtf8 e => print(e.At)\n}");
        Assert.Equal([(ErrorIds.UnlistedCase, 2)], Errors("string Text(Bytes b) {\n    return string.FromUtf8(b)\n}\n\nprint(Text(Bytes()))"));
        Assert.Equal([(ErrorIds.UnlistedCase, 1)], Errors("print(string.FromUtf8(Bytes()))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nprint(string.FromUtf8Lossy(xs))"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data InvalidUtf8(int At)"));
    }

    [Fact]
    public void A_part_of_a_list_is_a_list_and_its_ends_are_a_range_s()
    {
        // M9 (section 4, the owner, 2026-10-29, "A List<T>, a value"), with C8's ends: integers of one sign, in order where both are literals
        Assert.Equal("List<int>", TypeOf("xs = List<int>()\nys = xs[0..<0]\nprint(ys)", "ys"));
        Assert.Equal("Bytes", TypeOf("ys = \"abc\".Bytes[1..2]\nprint(ys)", "ys"));
        Clean("xs = List<int>()\nlong a = 0\nprint(xs[a..<a])");
        Assert.Equal([(ErrorIds.ReversedRange, 2)], Errors("xs = List<int>()\nprint(xs[2..<1])"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nprint(xs[0..<1.5])"));
        Assert.Equal([(ErrorIds.SignMix, 4)], Errors("xs = List<int>()\nint a = 0\nuint u = 1\nprint(xs[a..<u])"));
        Assert.Equal([(ErrorIds.StringIndex, 1)], Errors("print(\"abc\"[0..<1])"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 5\nprint(x[0..<1])"));
    }

    [Fact]
    public void A_member_a_type_does_not_have_is_unknown_name_at_the_dot()
    {
        // V10 gives `unknown-name` to a name that is not visible at the place it stands; a member no rule gives the type is such a name (the
        // reading in the README), and so is a member after the name of a type or a function (`int.Parse`, `print.Count`): the name stands before
        // a dot, where the parser reads a member access (L12's reading), not where a value stands, so E4's reading for a function's or a type's
        // name as a value does not apply; a member of a union is `type-mismatch`, since a union is one of its cases only after a match (D9), and
        // the last case names a field one case has, so the id comes from D9 and not from a missing member
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("s = \"abc\"\nprint(s.Count)"));
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("xs = List<int>()\nprint(xs.Size)"));
        Assert.Equal([(ErrorIds.UnknownName, 3)], Errors("data User(int Id)\n\nprint(User(1).Name)"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("print(int.Parse(\"1\"))"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("print(print.Count)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("data NotFound(int Id)\n\nint | NotFound Find(int id) => 10\n\nint | NotFound v = Find(1)\nprint(v.Id)"));
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
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nxs.Add(\"a\")")); // E1: after the call's own error the receiver is not judged
    }

    // ---- generics (T17, T19, T27) -------------------------------------------------------------------

    [Fact]
    public void A_type_argument_is_inferred_from_the_arguments_or_is_cannot_infer()
    {
        // T27: no widening between arguments; literals take what the others fix; literals alone take the type a constant of them has, so
        // `Pick(1, 2.5)` is a `double` as `1 + 2.5` is, `Pick(1, 2.5m)` a `decimal` (T23) and `Pick(1.5, 2.5m)` nothing (T18); a count of type
        // arguments that is neither none nor the count declared, `Pair<int>` for two, is text no rule gives a meaning (T19, E4), where none
        // written leaves them to be inferred (T19) and is T27's `cannot-infer` when nothing infers them (the test below); a type argument that
        // does not resolve is one error, whatever the overloads
        Clean("T First<T>(List<T> items) => items[0]\n\nmut xs = List<int>()\nxs.Add(4)\nprint(First(xs))");
        Clean("T Pick<T>(T a, T b) => a\n\nbyte small = 7\nbyte r = Pick(small, 1)\nprint(r)");
        Assert.Equal("int", TypeOf("T Pick<T>(T a, T b) => a\n\nx = Pick(1, 2)\nprint(x)", "x"));
        Assert.Equal("double", TypeOf("T Pick<T>(T a, T b) => a\n\nx = Pick(1, 2.5)\nprint(x)", "x"));
        Assert.Equal("decimal", TypeOf("T Pick<T>(T a, T b) => a\n\nx = Pick(1, 2.5m)\nprint(x)", "x"));
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("T Pick<T>(T a, T b) => a\n\nprint(Pick(1.5, 2.5m))"));
        Assert.Equal([(ErrorIds.CannotInfer, 5)], Errors("T Pick<T>(T a, T b) => a\n\nbyte small = 1\nint i = 2\nprint(Pick(small, i))"));
        Assert.Equal([(ErrorIds.ConstantOverflow, 4)], Errors("T Pick<T>(T a, T b) => a\n\nbyte small = 1\nprint(Pick(small, 300))"));
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("T Make<T>() => Make<T>()\n\nprint(Make())"));
        Clean("T Pick<T>(T a, T b) => a\n\nprint(Pick<long>(1, 2))");
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("T Pick<T>(T a, T b) => a\n\nprint(Pick<int, int>(1, 2))"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("data Pair<A, B>(A First, B Second)\n\nPair<int> p = Pair(1, \"one\")\nprint(p.First)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("int<int> x = 1\nprint(x)"));
        Assert.Equal([(ErrorIds.UnknownName, 5)], Errors("T Pick<T>(T a, T b) => a\nT Pick<T>(T a, T b, T c) => a\n\nx = 1\nprint(Pick<Nope>(x, x))"));
        Assert.Equal([(ErrorIds.UnknownName, 3)], Errors("T Pick<T>(T a, T b) => a\n\nprint(Pick(nope, 1))")); // E1: an argument that has no type fixes nothing and is no second error
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("void Show<T>(T item) {\n    print(item.Name)\n}\n\nShow(1)")); // only a limit gives a type parameter members (T19, A10)
    }

    [Fact]
    public void A_callee_without_type_parameters_takes_no_type_argument()
    {
        // T19 with E4: `print`, a conversion, a function value and a list's `Add` and `Where` declare no type parameter, so a type argument
        // written for one is a count the callee does not have, text no rule gives a meaning, as `int<int>` is in a type
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print<int>(1)"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("x = 5\nprint(long<bool>(x))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("f = (int x) => x\nprint(f<int>(1))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("mut xs = List<int>()\nxs.Add<string>(1)\nprint(xs.Count)"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("xs = List<int>()\nprint(xs.Where<int>(x => x > 1).Count)"));
    }

    [Fact]
    public void A_lambda_argument_fixes_no_type_parameter()
    {
        // T27 with F11 (the reading in the README): a lambda is typed by the parameter it lands on, after the other arguments fixed the type
        // arguments, so a type parameter that only the lambda's result could fix is cannot-infer
        Clean("U Each<T, U>(T x, (T) => U f) => f(x)\n\nprint(Each<int, int>(1, x => x + 1))");
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("U Each<T, U>(T x, (T) => U f) => f(x)\n\nprint(Each(1, x => x + 1))"));
    }

    [Fact]
    public void A_generic_data_type_names_its_type_arguments_or_infers_them()
    {
        // T17, T19, T27, D1: T19 says a use names the type arguments or leaves them to be inferred (T27), the part of T19 the checker reads
        // beside the limit of a type parameter, which it does not; a written type that leaves them has no argument to infer them from, so
        // `Pair p` is T27's `cannot-infer`, as `List()` is, where `Pair<int> p`, a count that is neither, is `syntax` (the test above)
        Clean("data Pair<A, B>(A First, B Second)\n\np = Pair<int, string>(1, \"one\")\nprint(p.First)\nprint(p.Second)");
        Assert.Equal("Pair<int, string>", TypeOf("data Pair<A, B>(A First, B Second)\n\np = Pair(1, \"one\")\nprint(p.First)", "p"));
        Assert.Equal([(ErrorIds.CannotInfer, 3)], Errors("data Pair<A, B>(A First, B Second)\n\nPair p = Pair<int, string>(1, \"one\")\nprint(p.First)"));
    }

    [Fact]
    public void Where_one_data_argument_derives_from_the_other_the_parameter_takes_the_base()
    {
        // T27: where one argument's data type is the base of the others' (D6) the parameter takes the base, in whatever order the arguments
        // stand; two derived types with a common base that no argument has are cannot-infer
        Assert.Equal("User", TypeOf("data User(string Name)\ndata Admin(int Level) : User\n\nT Pick<T>(T a, T b) => a\n\nx = Pick(Admin(\"a\", 1), User(\"b\"))\nprint(x.Name)", "x"));
        Assert.Equal("User", TypeOf("data User(string Name)\ndata Admin(int Level) : User\n\nT Pick<T>(T a, T b) => a\n\nx = Pick(User(\"b\"), Admin(\"a\", 1))\nprint(x.Name)", "x"));
        var shapes = "data Shape(int Id)\ndata Circle(int R) : Shape\ndata Square(int S) : Shape\n\nT Pick<T>(T a, T b, T c) => a\n\n";
        Assert.Equal("Shape", TypeOf(shapes + "x = Pick(Circle(1, 2), Shape(1), Square(1, 3))\nprint(x.Id)", "x"));
        Assert.Equal("Shape", TypeOf(shapes + "x = Pick(Circle(1, 2), Square(1, 3), Shape(1))\nprint(x.Id)", "x"));
        Assert.Equal([(ErrorIds.CannotInfer, 6)], Errors("data User(string Name)\ndata Admin(int Level) : User\ndata Guest(int N) : User\n\nT Pick<T>(T a, T b) => a\nprint(Pick(Admin(\"a\", 1), Guest(\"b\", 2)).Name)"));
        var put = "data User(string Name)\ndata Admin(int Level) : User\n\nT Put<T>(List<T> items, T item) => item\n\nmut users = List<User>()\nusers.Add(User(\"b\"))\n";
        Assert.Equal("User", TypeOf(put + "got = Put(users, Admin(\"a\", 1))\nprint(got.Name)", "got")); // the list fixes `User`, and the derived argument meets it there
        Assert.Equal([(ErrorIds.CannotInfer, 6)], Errors("data User(string Name)\ndata Admin(int Level) : User\n\nT Put<T>(List<T> items, T item) => item\n\nprint(Put(List<Admin>(), User(\"b\")).Name)")); // a list of the derived type fixes it, and the base is no `Admin`
    }

    // ---- functions (chapter 8) ----------------------------------------------------------------------

    [Fact]
    public void A_function_with_a_result_whose_end_can_be_reached_is_missing_return_at_its_first_line()
    {
        // F3 with E1: the end that can be reached is a defect of the function, at its first line; a bare `return` in such a function is a
        // defect of that statement, at its line; F3 makes `return` with a value in a `void` function (F4) `type-mismatch`, reported where the
        // value itself is clean (E1), while a value as the expression body of one is E4's `syntax` (the reading in the README: F2 and F4 name
        // no id for that shape, where F3 names this one)
        Assert.Equal([(ErrorIds.MissingReturn, 1)], Errors("int Sign(int n) {\n    if n < 0 {\n        return -1\n    }\n}\n\nprint(Sign(3))"));
        Assert.Equal([(ErrorIds.MissingReturn, 2)], Errors("int Sign(int n) {\n    return\n}\n\nprint(Sign(3))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("void Show(int n) {\n    return n\n}\n\nShow(3)"));
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("void Show(int n) {\n    return m\n}\n\nShow(3)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int Sign(int n) {\n    return \"x\"\n}\n\nprint(Sign(3))"));
        Clean("int Max(int a, int b) {\n    if a > b {\n        return a\n    }\n    return b\n}\n\nprint(Max(2, 3))");
        Clean("int Sign(int n) {\n    if n > 0 {\n        return 1\n    } else if n < 0 {\n        return -1\n    } else {\n        return 0\n    }\n}\n\nprint(Sign(5))");
        Clean("string Name(int n) {\n    match n {\n        0 => return \"zero\"\n        else => return \"many\"\n    }\n}\n\nprint(Name(0))");
    }

    [Fact]
    public void A_function_is_visible_in_the_whole_file_and_one_declared_inside_a_block_is_not_read()
    {
        // F6 makes a declaration of the file visible in the whole file and says nothing of one inside a block (the reading in the README):
        // inside its block the name of such a declaration is one the checker does not read, so a use of it there is no error of its own (E1),
        // and outside its block the name is not visible, `unknown-name` (V10), as a type's name and a function's name are where no type or
        // function of the file has it; its body is read with its diagnostics dropped, so that a variable around it that it reads counts as used
        Clean("print(Double(4))\n\nint Double(int n) => n * 2");
        Assert.Equal(["2: a function declared inside a block (F1, F6)"], NotSupported("void F() {\n    int G() => 1\n    print(G())\n}\n\nF()"));
        Assert.Equal(["2: a type declared inside a block (D1, F6)"], NotSupported("if true {\n    data P(int X)\n    print(P(1).X)\n}"));
        Assert.Equal(["3: a function declared inside a block (F1, F6)"], NotSupported("void F() {\n    n = 1\n    int G() => n\n    print(G())\n}\n\nF()"));
        var (outside, _) = Check("void F() {\n    int G() => 1\n    print(G())\n}\n\nF()\nprint(G())");
        Assert.Equal(["2: not supported: a function declared inside a block (F1, F6)", "7: error unknown-name: no `G` is visible here (V10)"], outside.Sorted.Select(d => d.ToString()));
        var (elsewhere, _) = Check("void F() {\n    if true {\n        data P(int X)\n        print(P(1).X)\n    }\n}\n\nint G(P p) => 1\n\nF()\nprint(G(1))");
        Assert.Equal([(ErrorIds.UnknownName, 8)], elsewhere.Sorted.Where(d => d.IsError).Select(d => (d.Id, d.Line)));
        var (asType, _) = Check("void F() {\n    int G() => 1\n    G x = G()\n    print(x)\n}\n\nF()");
        Assert.Equal([(ErrorIds.UnknownName, 3)], asType.Sorted.Where(d => d.IsError).Select(d => (d.Id, d.Line))); // a function's name is no type
    }

    [Fact]
    public void A_lambda_takes_the_function_type_expected_of_it_or_needs_typed_parameters()
    {
        // F7, F11, T27; F11 makes a lambda whose body is a call with a `void` success, under a function type whose result is not `void`,
        // `type-mismatch` at the lambda, where O10 makes a `void` call elsewhere where a value stands `syntax`; a parameter of a lambda of the
        // top-level code reserves no name in a top-level function, `x` beside `Show(int x)` (F16, the owner, 2026-10-21); a written
        // parameter type that does not fit is the one error of the lambda, which is then read with it error-typed (E1)
        Clean("int Apply((int) => int f, int n) => f(n)\n\nprint(Apply(x => x + 1, 4))");
        Assert.Equal([(ErrorIds.CannotInfer, 1)], Errors("f = x => x * 2\nprint(f(2))"));
        Assert.Equal("(int) => int", TypeOf("f = (int x) => x * 2\nprint(f(2))", "f"));
        Assert.Equal("() => int", TypeOf("mut n = 1\nfirst = () => n\nn = 2\nprint(first())", "first"));
        Assert.Equal("(int) => void", TypeOf("f = (int x) => print(x)\nf(1)", "f"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("int Apply((int) => int f, int n) => f(n)\n\nvoid Show(int x) {\n}\nprint(Apply(x => Show(x), 4))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("int Apply((int) => int f, int n) => f(n)\n\nprint(Apply((x, y) => x, 4))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("y = 5\nint Apply((int) => int f, int n) => f(n)\n\nprint(Apply((a, b) => y, 4))")); // the body is read after the error: `y` counts as read
        Clean("void Run((int) => void f) {\n    f(1)\n}\n\nRun(x => print(x))");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("int Apply((int) => int f, int n) => f(n)\n\nprint(Apply((string s) => 1, 4))"));
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
        Assert.Equal([(ErrorIds.DuplicateFunction, 2)], Errors("int F(int a) => a\nint F<T>(int b) => b\nprint(F(1))")); // F12 names the parameters, not the type parameters
        Assert.Equal([(ErrorIds.DuplicateFunction, 2), (ErrorIds.TypeMismatch, 3)], Errors("int F(int a) => a\nint F(int b) {\n    return \"s\"\n}\nprint(F(1))")); // the duplicate's body is checked all the same
        Clean("void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(string s) {\n    print(\"text {s}\")\n}\n\nvoid Show(int a, int b) {\n    print(\"pair {a} {b}\")\n}\n\nShow(4)\nShow(\"four\")\nShow(4, 5)");
    }

    [Fact]
    public void A_call_that_fits_more_than_one_function_none_exactly_is_ambiguous_call()
    {
        // F13: the function whose parameters have exactly the argument's types, with no default value used, wins, so `Show(int n)` beats
        // `Show(int a, int b = 0)`; the function without type parameters wins over a generic one. F14: a literal has its own type for the pick
        // of an overloaded function, `Show(4)` fits `Show(long)` and not `Show(short)`; where the function is not overloaded the parameter's
        // type reaches the literal (L17), so `H(1)` with one `H(byte b)` compiles and the same call with a second `H` does not
        var shows = "void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(long n) {\n    print(\"long {n}\")\n}\n\n";
        Assert.Equal([(ErrorIds.AmbiguousCall, 10)], Errors(shows + "short s = 4\nShow(s)"));
        Clean(shows + "int i = 4\nlong big = 4\nShow(i)\nShow(big)\nShow(4)\nShow(4000000000)\nShow(4L)");
        Clean("void Show(short n) {\n    print(\"short {n}\")\n}\n\nvoid Show(long n) {\n    print(\"long {n}\")\n}\n\nShow(4)");
        Clean("void Show<T>(T x) {\n    print(1)\n}\n\nvoid Show(int n) {\n    print(n)\n}\n\nShow(4)");
        Clean("void Show(int n) {\n    print(\"int {n}\")\n}\n\nvoid Show(int a, int b = 0) {\n    print(\"two {a} {b}\")\n}\n\nint i = 4\nShow(i)");
        var h = "void H(byte b) {\n    print(b)\n}\n\n";
        Clean(h + "H(1)");
        Assert.Equal([(ErrorIds.ArgumentMismatch, 9)], Errors(h + "void H(string s) {\n    print(s)\n}\n\nH(1)"));
        // F13 with F14 (the owner, 2026-10-21): a call that fits none of several functions is argument-mismatch, whichever order the
        // declarations stand in (the test below)
        Assert.Equal([(ErrorIds.ArgumentMismatch, 9)], Errors("void H(string s) {\n    print(s)\n}\n\n" + h + "H(1)"));
    }

    [Fact]
    public void A_call_that_fits_none_of_several_functions_is_argument_mismatch_at_the_call_naming_each_function()
    {
        // F13 with F14 (the owner, 2026-10-21): one id, whatever kept each function from fitting, at the first line of the call, as E1 reports an
        // error of the whole construct (the record's reading); the message names each function and why it does not fit, and its words are not
        // pinned, since a test pins the id and the line of an error, never its message (E2), only its shape (the test below); a name with one
        // function keeps the error of its argument at the argument's line, and so does a name whose second function is a duplicate, which takes
        // no part in the pick (F12)
        var two = "void Show(int n) {\n    print(n)\n}\n\nvoid Show(string s) {\n    print(s)\n}\n\n";
        Assert.Equal([(ErrorIds.ArgumentMismatch, 10)], Errors(two + "long big = 5\nShow(big)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 9)], Errors(two + "Show(\n    true)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 9)], Errors(two + "Show(1, 2)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 9)], Errors(two + "Show<int>(1)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 4)], Errors("T Pick<T>(T a, T b) => a\nint Pick(int a) => a\n\nprint(Pick(1, \"a\"))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("void Take(int n) {\n    print(n)\n}\n\nTake(\n    true)"));
        Assert.Equal([(ErrorIds.DuplicateFunction, 2), (ErrorIds.TypeMismatch, 3)], Errors("int F(int a) => a\nint F(int b = 1) => b\nprint(F(true))"));
    }

    [Fact]
    public void The_message_of_a_call_that_fits_none_of_several_functions_has_a_line_per_function()
    {
        // F13: a head line and one line per function, each as its declaration writes it; a char or string default that the source writes with
        // an escape (L7, L16) stands as `...`, so that no line break or quote of its value splits the message
        var (diagnostics, _) = Check("void Show(char c = '\\n') {\n    print(c)\n}\n\nvoid Show(int n, string s = \"a\\\"b\") {\n    print(n)\n}\n\nShow(true)");
        var message = Assert.Single(diagnostics.Items).Message;
        Assert.Equal(3, message.Split('\n').Length);
        Assert.DoesNotContain("a\"b", message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_argument_with_an_error_of_its_own_leaves_the_pick_of_an_overload_open()
    {
        // E1: an argument the checker could not type, or a `void` call standing as one, which O10 makes `syntax` wherever the parameter is (the
        // reading in the README), carries its own error; a call that fits no function or more than one of them with it reports nothing more,
        // since the argument may have been meant for any of them
        var two = "void Show(int n) {\n    print(n)\n}\n\nvoid Show(string s) {\n    print(s)\n}\n\n";
        Assert.Equal([(ErrorIds.UnknownName, 9)], Errors(two + "Show(missing)"));
        Assert.Equal([(ErrorIds.Syntax, 11)], Errors(two + "void Log() {\n}\nShow(Log())"));
        Assert.Equal([(ErrorIds.Syntax, 7)], Errors("void Log() {\n}\nvoid Take(int n) {\n    print(n)\n}\n\nTake(Log())"));
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
    [InlineData("count = 0\n\nvoid Bump() {\n    f = (int count) => count\n    print(f(1))\n}\n\nBump()\nprint(count)", ErrorIds.Redeclared, 4)]
    public void A_top_level_function_neither_reads_nor_declares_a_top_level_variable(string program, string id, int line)
    {
        // F16: the read is unknown-name, every declaration of the name is redeclared, the one error for `count = count + 1`, whether the variable
        // stands above or below the function; inside the function the parameter of a lambda is such a declaration too (the record's reading)
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void Only_a_variable_at_the_outermost_level_of_the_top_level_code_reserves_its_name()
    {
        // F16 (the owner, 2026-10-21): a loop variable, a variable inside a block of the top-level code and a parameter of a lambda there (the
        // test of lambdas above) reserve no name in a top-level function; such a variable is not visible in the function either, so a read
        // of it there is unknown-name (V10). The name an arm of a top-level match binds, which lives in its arm, reserves none either (the
        // record's reading)
        Clean("string Email(int id) => \"a\"\n\nfor id in 0..2 {\n    print(Email(id))\n}");
        Clean("int Twice(int n) {\n    count = n * 2\n    return count\n}\n\nif true {\n    count = 1\n    print(Twice(count))\n}");
        Clean("data Found(int Id)\ndata NotFound\n\nint Show(int f) => f\n\nFound | NotFound v = Found(1)\nmatch v {\n    Found f => print(Show(f.Id))\n    NotFound => print(0)\n}");
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("int Get() => count\n\nif true {\n    count = 1\n    print(count)\n}\nprint(Get())"));
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
        // E4 gives one id, `syntax`, to text no rule gives a meaning, and names shapes of the checker's among them; these are the shapes the
        // checker reads under it (the reading in the README): F11 gives a function value through a lambda only, and T10 a type's name a
        // meaning as a conversion only; F5 lists what a statement is; C8 shows a range in `for` only; F2 and F4 give a `void` function's
        // expression body no value, and F11 a `void` lambda's; D8 writes a fieldless data type's value as the bare name, and declares the type
        // as the bare name too, `data Name`, so the declaration `data E()` with an empty field list is no shape of D8 or D1; D6 gives a data
        // type one base; C9 has an arm name a literal, and a `match` used as a value an arm with a value; O5 has an arm of `else` recover,
        // return or throw
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("int Double(int n) => n * 2\n\nf = Double\nprint(f(1))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("x = 1\nx + 1"));
        Clean("void F() {\n}\n\n((F()))");
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("1..3"));
        Clean("for i in (1..3) {\n    print(i)\n}");
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(int)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("void F(int x) => x\n\nF(1)"));
        Clean("int One() => 1\nvoid F() => (One())\n\nF()");
        Assert.Equal([(ErrorIds.Syntax, 4)], Errors("void Run((int) => void h) {\n    h(1)\n}\nRun(x => x)"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("data Empty\n\nprint(Empty())"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("data A(int X)\ndata B(int Y)\ndata C(int Z) : A, B\n\nprint(C(1, 2).Z)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("data E()\n\nprint(1)"));
        Assert.Equal([(ErrorIds.Syntax, 6)], Errors("data P(int X)\n\np = P(1)\nn = 1\nmatch n {\n    p.X => print(1)\n    else => print(0)\n}")); // the parser reads a path as a pattern, for a value of an enum (D12); C9 names a literal
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("n = 1\nx = match n {\n    else => throw \"no\"\n}\nprint(x)"));
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
        var two = "data P(List<int> A, List<int> B)\n\nvoid Two(mut List<int> a, mut List<int> b) {\n    a.Add(b.Count)\n}\n\nmut p = P(List<int>(), List<int>())\n";
        Clean(two + "Two(mut p.A, mut p.B)\nprint(p.A.Count)"); // two fields of one variable are two values
        Assert.Equal([(ErrorIds.MutAtCall, 8)], Errors(two + "Two(mut p.A, mut p.A)\nprint(p.A.Count)"));
        Assert.Equal([(ErrorIds.MutAtCall, 9)], Errors("data P(List<int> A)\n\nvoid Both(mut P q, mut List<int> a) {\n    a.Add(q.A.Count)\n}\n\nmut p = P(List<int>())\n\nBoth(mut p, mut p.A)")); // a path and one it is a prefix of; the parameter is not `p`, which F16 keeps for the top-level variable
        Assert.Equal([(ErrorIds.MutAtCall, 6)], Errors("void FillB(mut List<byte> xs) {\n    xs.Add(1)\n}\n\nmut s = \"abc\"\nFillB(mut s.Bytes)")); // M7: a path of data fields, which `Bytes` is not
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("mut s = \"abc\"\ns.Bytes.Add(1)")); // M5 with M4: the same for a receiver
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("void Bump(mut int n) {\n    n = n + 1\n}\n\nmut short s = 1\nBump(mut s)\nprint(s)")); // M7: the callee writes an `int` back
    }

    [Fact]
    public void A_read_the_expression_still_holds_while_a_call_or_an_arm_changes_it_is_changed_while_held()
    {
        // M7 (the owner, 2026-10-24 and 2026-10-25), beside the corpus's cases: a call deeper inside, an arm, paths, one error for
        // each change (E1), what an arm's statement changes, a `mut` method's receiver, and the copied types
        var grow = "int Grow(mut List<int> xs) {\n    xs.Add(5)\n    return xs.Count\n}\n\nint Sum(List<int> xs, int n) => xs.Count + n\n\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors(grow + "mut numbers = List<int>()\nprint(Sum(numbers, 1 + Grow(mut numbers)))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11)], Errors(grow + "mut numbers = List<int>()\nk = 1\nprint(Sum(numbers, match k {\n    1 => Grow(mut numbers)\n    else => 0\n}))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11)], Errors(grow + "mut numbers = List<int>()\nk = 1\nmatch k {\n    1 => print(Sum(numbers, Grow(mut numbers)))\n    else => print(0)\n}")); // an arm's statement, read once
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11)], Errors(grow + "int Three(List<int> a, List<int> b, int n) => n\n\nmut numbers = List<int>()\nprint(Three(numbers, numbers, Grow(mut numbers)))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11), (ErrorIds.ChangedWhileHeld, 11)], Errors(grow + "int Both(List<int> a, int m, int n) => m + n\n\nmut numbers = List<int>()\nprint(Both(numbers, Grow(mut numbers), Grow(mut numbers)))"));
        Clean(grow + "mut numbers = List<int>()\nprint(Sum(numbers, 0) + Grow(mut numbers))"); // the call around the read has ended

        var cart = "data Cart(List<int> Items, List<int> Seen)\n\nint Clear(mut Cart c) {\n    c = Cart(List<int>(), List<int>())\n    return 0\n}\n\nint Drop(mut List<int> xs) {\n    xs = List<int>()\n    return 0\n}\n\nint Count(List<int> xs, int n) => xs.Count + n\n\nint Size(Cart c, int n) => c.Items.Count + n\n\nmut cart = Cart(List<int>(), List<int>())\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 18)], Errors(cart + "print(Count(cart.Items, Clear(mut cart)))")); // a path into the variable
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 18)], Errors(cart + "print(Size(cart, Drop(mut cart.Items)))")); // the value that holds the path
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 18)], Errors(cart + "print(Count(cart.Items, Drop(mut cart.Items)))"));
        Clean(cart + "print(Count(cart.Seen, Drop(mut cart.Items)))"); // two fields of one variable are two values

        var name = "string Name(mut string t) {\n    t = \"b\"\n    return \"c\"\n}\n\nmut s = \"a\"\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors(name + "print(s == Name(mut s))")); // a `string` is not copied
        // a hole of a string holds it until the interpolation is made, as Rust's `format!` borrows its arguments (the owner,
        // 2026-10-29, "Refuse, as Rust"); a hole closes on its line, so what changes a place inside one is a `mut` argument
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors(name + "print(\"{s}{Name(mut s)}\")"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors(name + "print(\"{s}, {Name(mut s)}\")")); // text between them
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors(name + "print(\"{s}{\"{Name(mut s)}!\"}\")")); // a hole inside a hole
        Clean(name + "print(\"{Name(mut s)}{s}\")"); // the read after the change
        var card = "data Card(string Title, int Size)\n\nint Retitle(mut Card c) {\n    c = Card(\"b\", 1)\n    return 1\n}\n\nmut card = Card(\"a\", 2)\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors(card + "print(\"{card.Title}{Retitle(mut card)}\")")); // a path into the variable
        Clean(card + "n = 1\nprint(\"{card.Size}{Retitle(mut card)}{n}\")"); // a hole of another type is made text where it stands
        var lone = "string Name(mut string t) {\n    t = \"b\"\n    return \"c\"\n}\n\nstring First(string a, string b) => a\n\nstring Three(string a, string b, string c) => a\n\nmut s = \"a\"\n";
        Clean(lone + "print(First(\"{s}\", Name(mut s)))"); // one hole of a string is a new value (the owner, 2026-10-29, "New value, as format!")
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11)], Errors(lone + "print(Three(\"{s}\", s, Name(mut s)))")); // beside the read of the variable itself
        Clean(grow + "mut numbers = List<int>()\nprint(\"{numbers}{Grow(mut numbers)}\")"); // the text of a list is made where it stands
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 12)], Errors("data End\ndata Node(int V, Node | End Next)\n\nint Cut(mut Node node) {\n    node = Node(0, End)\n    return 1\n}\n\nint Value(Node node, int n) => node.V + n\n\nmut first = Node(1, End)\nprint(Value(first, Cut(mut first)))")); // a data type that holds itself is not copied, as a Rust `Box`
        Clean("data Inner(int A)\ndata Outer(Inner X, Inner Y)\n\nint Cut(mut Outer o) {\n    o = Outer(Inner(0), Inner(0))\n    return 1\n}\n\nint Value(Outer o, int n) => o.Y.A + n\n\nmut first = Outer(Inner(1), Inner(2))\nprint(Value(first, Cut(mut first)))"); // a type met twice is no type that holds itself
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 13)], Errors("data Person(int Id)\ndata Member(List<int> Items) : Person\n\nint Reset(mut Person q) {\n    q = Person(2)\n    return 0\n}\n\nint Size(Person q, int n) => q.Id + n\n\nmut Person p = Member(1, List<int>())\nprint(p.Id)\nprint(Size(p, Reset(mut p)))")); // a type derived from it holds a list
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors("T Same<T>(mut T a) => a\n\nT Pick<T>(T a, T b) => a\n\nvoid Run<T>(mut T v) {\n    v = Pick(v, Same(mut v))\n}")); // a type parameter is not copied
        Clean("int Later(mut duration d) {\n    d = d + 1ms\n    return 1\n}\n\nint Keep(duration d, int n) => n\n\nmut duration t = 1ms\nprint(Keep(t, Later(mut t)))"); // a time is copied
        Clean("int Swap(mut (int) => int g) {\n    g = x => x\n    return 1\n}\n\nint Keep((int) => int g, int n) => n\n\nmut (int) => int f = x => x + 1\nprint(Keep(f, Swap(mut f)))"); // a function value is copied (the owner, 2026-10-25, "Copied")
        Clean("data Rule((int) => int Step, int Limit)\n\nint Swap(mut Rule r) {\n    r = Rule(x => x, 0)\n    return 1\n}\n\nint Keep(Rule r, int n) => n\n\nmut rule = Rule(x => x + 1, 5)\nprint(Keep(rule, Swap(mut rule)))"); // and so is a data value of one and a copied type (the owner, 2026-10-25, "Copied too")
        Clean("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nint Swap(mut extern \"C\" (long) => long g) {\n    g = Twice\n    return 1\n}\n\nint Keep(extern \"C\" (long) => long g, int n) => n\n\nmut extern \"C\" (long) => long f = Twice\nprint(Keep(f, Swap(mut f)))", "native"); // the type of a C function is copied

        // the text names what is read and what changes it, and how to keep the value, as R11's messages name the change (the rest of
        // the text stays free to improve, E2)
        void Names(string program, string read, string change)
        {
            var error = Assert.Single(Check(program).Diagnostics.Items);
            Assert.Equal(ErrorIds.ChangedWhileHeld, error.Id);
            Assert.StartsWith(read + " is still in use while " + change + ",", error.Message, StringComparison.Ordinal);
            Assert.Contains("copy it into a variable of its own on a line before", error.Message, StringComparison.Ordinal);
        }

        Names(grow + "mut numbers = List<int>()\nprint(Sum(numbers, Grow(mut numbers)))", "`numbers`", "the call that takes `mut numbers` runs");
        Names(cart + "print(Count(cart.Items, Clear(mut cart)))", "`cart.Items`", "the call that takes `mut cart` runs");
        Names("int Clear(mut List<List<int>> all) {\n    all = List<List<int>>()\n    return 0\n}\n\nint Sum(List<int> row, int n) => row.Count + n\n\nmut rows = List<List<int>>()\nprint(Sum(rows[0], Clear(mut rows)))", "an item of `rows`", "the call that takes `mut rows` runs");
        Names(grow + "int Apply(() => int f, int n) => f() + n\n\nmut numbers = List<int>()\nprint(Apply(() => numbers.Count, Grow(mut numbers)))", "a lambda that reads `numbers`", "the call that takes `mut numbers` runs");

        // an assignment and a `mut` method in a statement of an `else` arm's block change what the expression holds, at their line
        var find = "data NotFound\n\nint | NotFound Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\n";
        var arm = find + "int Sum(List<int> xs, int n) => xs.Count + n\n\nmut numbers = List<int>()\nprint(Sum(numbers, Find(2) else {\n    NotFound => {\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 15)], Errors(arm + "        numbers = List<int>()\n        5\n    }\n}))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 15)], Errors(arm + "        numbers.Add(1)\n        5\n    }\n}))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 12)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\ndata Cart(List<int> Items)\n\nint Count(List<int> xs, int n) => xs.Count + n\n\nmut cart = Cart(List<int>())\nprint(Count(cart.Items, Find(2) else {\n    NotFound => {\n        cart.Items.Add(1)\n        5\n    }\n}))")); // a `mut` method on a path
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 17)], Errors(arm + "        k = 1\n        if k == 1 {\n            numbers = List<int>()\n        }\n        5\n    }\n}))"));
        Clean(arm + "        mut others = List<int>()\n        others.Add(1)\n        others.Count\n    }\n}))"); // a variable of the arm's own
        Clean(find + "mut numbers = List<int>()\nprint(Find(2) else {\n    NotFound => {\n        numbers = List<int>()\n        5\n    }\n})\nprint(numbers.Count)"); // an arm's change that nothing holds
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 12)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\ndata Cart(List<int> Items)\n\nint Count(List<int> xs, int n) => xs.Count + n\n\nmut cart = Cart(List<int>())\nprint(Count(cart.Items, Find(2) else {\n    NotFound => {\n        cart.Items = List<int>()\n        5\n    }\n}))")); // a path assignment
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 22)], Errors(grow + "int Foo(List<int> xs, int n) => n\n\n" + find + "mut numbers = List<int>()\nprint(Sum(numbers, Find(2) else {\n    NotFound => {\n        print(Foo(numbers, Grow(mut numbers)))\n        5\n    }\n}))")); // one change, two holders: one error

        // a read through `?.` takes a count of its own, as a call's result does, so the walk meets no held read of it (N5); a read of a
        // field of a variable known not null is the field (N3); a value of a type with a generic base is not read (T19)
        const string Cart = "data Cart(List<int> Items)\n\nint Count(List<int>? xs, int n) => n\n\nint Clear(mut Cart? c) {\n    c = null\n    return 0\n}\n\nmut Cart? cart = Cart(List<int>())\n";
        Clean(Cart + "print(Count(cart?.Items, Clear(mut cart)))");
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 11)], Errors(Cart + "print(Count(cart.Items, Clear(mut cart)))"));
        Assert.Equal(["2: a generic data type as a base (T19)"], NotSupported("data Base<T>(T X)\ndata Derived(List<int> Items) : Base<int>\n\nint Reset(mut Base<int> b) {\n    b = Base(2)\n    return 0\n}\n\nint Size(Base<int> b, int n) => n\n\nmut Base<int> p = Base(1)\nprint(Size(p, Reset(mut p)))"));

        // `Add` changes its receiver after its argument ran, so a read of it there is held by nothing that the change meets (the
        // owner, 2026-10-25, "Allow, as Rust's clone"), but by a lambda the argument's value can keep (below); the receiver is
        // held while a call in its argument changes it
        Clean("mut rows = List<List<int>>()\nrows.Add(List<int>())\nrows.Add(rows[0])\nprint(rows.Count)");
        Clean("mut xs = List<int>()\nxs.Add(1)\nxs.Add(xs[0])\nxs.Add(xs.Count)\nprint(xs)");
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 8)], Errors("List<int> Take(mut List<List<int>> all) {\n    all = List<List<int>>()\n    return List<int>()\n}\n\nmut rows = List<List<int>>()\nrows.Add(List<int>())\nrows.Add(Take(mut rows))\nprint(rows.Count)"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors("data Store(List<int> Names)\n\nint Reset(mut Store t) {\n    t = Store(List<int>())\n    return 0\n}\n\nmut s = Store(List<int>())\ns.Names.Add(Reset(mut s))\nprint(s)")); // a receiver that is a path of fields

        // a lambda in that argument that reads the receiver holds it while `Add` changes it, as its value or an arm's (the owner,
        // 2026-10-25, "Refuse, as Rust's borrow"); a copy on a line before passes
        var fs = "mut fs = List<() => int>()\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 2)], Errors(fs + "fs.Add(() => fs.Count)\nprint(fs.Count)"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 2)], Errors(fs + "fs.Add((() => fs.Count))\nprint(fs.Count)"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 3)], Errors(fs + "k = 1\nfs.Add(match k {\n    1 => () => fs.Count\n    else => () => 0\n})\nprint(fs.Count)"));
        Clean(fs + "n = fs.Count\nfs.Add(() => n)\nprint(fs.Count)");

        // and one that a value made around it can keep, a value of a type that can hold a function value: a call's result, a field
        // of one, a `with`; one given to a call whose result is an `int` holds nothing past it (the owner, 2026-10-25, "Refuse if
        // it can keep it")
        var wrap = "data Rule(() => int Step, int Limit)\n\n() => int Wrap(() => int g) => g\n\nint Count(() => int g) => g()\n\n" + fs;
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 8)], Errors(wrap + "fs.Add(Wrap(() => fs.Count))\nprint(fs.Count)"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 8)], Errors(wrap + "fs.Add(Rule(() => fs.Count, 1).Step)\nprint(fs.Count)"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors(wrap + "r = Rule(() => 0, 1)\nfs.Add((r with { Step = () => fs.Count }).Step)\nprint(fs.Count)"));
        Clean(wrap + "fs.Add(Wrap(() => 0))\nmut xs = List<int>()\nxs.Add(Count(() => xs.Count))\nprint(fs.Count + xs.Count)");

        // a value inside the argument that can hold no function value ends what the argument keeps (the owner, 2026-10-28, "Pass,
        // as Rust"); a generic type holds one where a type argument can, at any depth, and one that holds itself with other
        // arguments ends the walk
        Clean(wrap + "mut xs = List<int>()\nxs.Add(Count(Wrap(() => xs.Count)))\nprint(fs.Count + xs.Count)");
        var box = "data Box<T>(T V)\n\nint Count(() => int g) => g()\n\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(box + "mut bs = List<Box<Box<() => int>>>()\nbs.Add(Box<Box<() => int>>(Box<() => int>(() => bs.Count)))\nprint(bs.Count)"));
        Clean(box + "mut cs = List<Box<Box<int>>>()\ncs.Add(Box<Box<int>>(Box<int>(Count(() => cs.Count))))\nprint(cs.Count)");
        var chain = "data P<T>(T V, List<P<List<T>>> Next)\n\nint Count(() => int g) => g()\n\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(chain + "mut qs = List<P<() => int>>()\nqs.Add(P<() => int>(() => qs.Count, List<P<List<() => int>>>()))\nprint(qs.Count)"));
        Clean(chain + "mut ps = List<P<int>>()\nps.Add(P<int>(Count(() => ps.Count), List<P<List<int>>>()))\nprint(ps.Count)");

        // a data type holds what its type arguments hold, whether a field stores them or not (the owner, 2026-10-28, "Counts, as
        // Rust"): an argument no field uses, one that only the type itself uses, and a field whose type, met again, has such an
        // argument; a type whose arguments and fields hold none passes
        static string Kept(string data, string type) => $"{data}\n\nList<{type}> Keep(() => int g) => List<{type}>()\n\nmut qs = List<List<{type}>>()\nqs.Add(Keep(() => qs.Count))\nprint(qs.Count)";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(Kept("data Q<T>(int X)", "Q<() => int>")));
        Clean(Kept("data Q<T>(int X)", "Q<int>"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(Kept("data Q<T>(int X, List<Q<T>> N)", "Q<() => int>")));
        Clean(Kept("data Q<T>(int X, List<Q<T>> N)", "Q<int>"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(Kept("data Q<T>(int X, List<Q<() => int>> N)", "Q<int>")));
        Clean(Kept("data Q<T>(int X, List<Q<int>> N)", "Q<int>"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 6)], Errors(Kept("data R<T>(T X, List<R<() => int>> N)", "R<int>")));
        Clean(Kept("data R<T>(T X, List<R<int>> N)", "R<int>"));
        Clean(fs + "mut others = List<int>()\nfs.Add(() => others.Count)\nprint(fs.Count)");
        Names(fs + "fs.Add(() => fs.Count)\nprint(fs.Count)", "a lambda that reads `fs`", "`Add` changes `fs`");
        Names(arm + "        numbers.Add(1)\n        5\n    }\n}))", "`numbers`", "`Add` changes `numbers`");
        Names(arm + "        numbers = List<int>()\n        5\n    }\n}))", "`numbers`", "an assignment changes `numbers`");
    }

    // ---- control flow (chapter 10) --------------------------------------------------------------------

    [Fact]
    public void A_condition_is_a_bool()
    {
        // C3, C5
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 1\nif x {\n    print(\"yes\")\n}"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("x = 1\nwhile x {\n    print(\"yes\")\n}"));
        Clean("x = 7\nif (x > 5) {\n    print(\"big\")\n}");
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Log() {\n}\nif Log() {\n    print(1)\n}")); // O10: a void call where a value stands, as everywhere but the body of a lambda with a result, which F11 makes type-mismatch
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Log() {\n}\nfor x in Log() {\n    print(1)\n}"));
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

    [Fact]
    public void A_reversed_range_is_judged_on_ends_made_only_of_literals()
    {
        // C8: both ends expressions made only of literals; a named constant is the run time's `reversed-range-at-run-time`
        Assert.Equal([(ErrorIds.ReversedRange, 1)], Errors("for i in (2 + 1)..(1 - 1) {\n    print(i)\n}"));
        Clean("n = 3\nfor i in n..0 {\n    print(i)\n}");
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
        Clean("data Other\n\nvoid Show(bool | Other v) {\n    match v {\n        true => print(1)\n        false => print(0)\n        Other => print(2)\n    }\n}\n\nShow(true)"); // both literals cover a bool case of a union too
        Assert.Equal([(ErrorIds.UnreachableArm, 5)], Errors("b = true\nmatch b {\n    true => print(1)\n    false => print(0)\n    else => print(2)\n}"));

        // C9 with L17: the subject's type reaches the literal of an arm as an expected type does, a union by the literal's kind (T8 with D9, the
        // owner, 2026-10-21), so the one time case beside a number case reaches a duration literal, and one that does not fit it is
        // constant-overflow, not the type-mismatch of its own type, a `longduration`
        var span = "int | duration d = 5days\nmatch d {\n";
        Clean(span + "    5days => print(0)\n    int n => print(n)\n    duration t => print(t)\n}");
        Assert.Equal([(ErrorIds.ConstantOverflow, 3)], Errors(span + "    106752days => print(0)\n    int n => print(n)\n    duration t => print(t)\n}"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("void Log() {\n}\nmatch Log() {\n    else => print(1)\n}")); // O10
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("n = 1\nmatch n {\n    \"one\" => print(1)\n}")); // E1: the arm that names no case is the one error, not the match
    }

    [Theory]
    [InlineData("data NotFound(int Id)\n\nvoid Show(int | NotFound v) {\n    match v {\n        int x => print(x)\n        0 => print(\"zero\")\n        NotFound n => print(n.Id)\n    }\n}\n\nShow(0)", ErrorIds.UnreachableArm, 6)]
    [InlineData("data Yes(int Count)\ndata No(string Why)\n\nvoid Show(Yes | No v) {\n    match v {\n        Yes y => print(y.Count)\n        No n => print(n.Why)\n        else => print(\"other\")\n    }\n}\n\nShow(Yes(1))", ErrorIds.UnreachableArm, 8)]
    [InlineData("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nvoid Show(User | NotFound u) {\n    match u {\n        User x => print(\"user {x.Name}\")\n        Admin a => print(\"admin {a.Level}\")\n        NotFound => print(\"none\")\n    }\n}\n\nShow(Admin(\"Ann\", 2))", ErrorIds.UnreachableArm, 8)]
    [InlineData("n = 1\nmatch n {\n    1 => print(\"one\")\n    1 => print(\"again\")\n    else => print(\"many\")\n}", ErrorIds.UnreachableArm, 4)]
    [InlineData("data Yes\ndata No\n\nvoid Show(Yes | No v) {\n    match v {\n        Yes => print(1)\n        string s => print(s)\n        No => print(0)\n    }\n}\n\nShow(Yes)", ErrorIds.TypeMismatch, 7)]
    [InlineData("n = 1\nmatch n {\n    \"one\" => print(1)\n    else => print(0)\n}", ErrorIds.TypeMismatch, 3)]
    [InlineData("void Show(int | string v) {\n    match v {\n        byte b => print(b)\n        int n => print(n)\n        string s => print(s)\n    }\n}\n\nShow(1)", ErrorIds.TypeMismatch, 3)]
    public void An_arm_an_earlier_one_covers_is_unreachable_and_a_type_that_is_no_case_is_type_mismatch(string program, string id, int line)
    {
        // C4, C9: a derived data type before its base is fine, after it unreachable; a literal after the arm of its type; else after every
        // case; a type that only widens into a case (`byte` for `int`, T8) is no case
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void A_match_used_as_a_value_has_the_type_its_arms_share()
    {
        // C9: where a type is written or expected each arm fits it; where none is, one type
        Clean("string Name(int n) => match n {\n    0 => \"zero\"\n    1 => \"one\"\n    else => \"many\"\n}\n\nprint(Name(0))");
        Assert.Equal("string", TypeOf("n = 1\ns = match n {\n    0 => \"zero\"\n    else => \"many\"\n}\nprint(s)", "s"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("n = 1\ns = match n {\n    0 => \"zero\"\n    else => 1\n}\nprint(s)")); // E1: at the first arm whose type differs
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("n = 1\nstring s = match n {\n    0 => \"zero\"\n    else => 1\n}\nprint(s)"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("for i in 1..3 {\n    s = match i {\n        0 => break\n        else => \"many\"\n    }\n    print(s)\n}")); // C9: an arm of a value is an expression
        Assert.Equal([(ErrorIds.Syntax, 4)], Errors("n = 1\nm = 2\nx = match n {\n    0 => {\n        print(m)\n    }\n    else => 1\n}\nprint(x)")); // the arm's body is read all the same: `m` counts as read
        Assert.Equal([(ErrorIds.Syntax, 5)], Errors("void Log() {\n}\nn = 1\nx = match n {\n    else => Log()\n}\nprint(x)")); // E1: the arm carries the error, the match no second one
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

        // O4 keeps the cases only for the subject of a match and a written union type, so a call that a lambda returns without a written
        // result unwraps (O2), and the result F7 infers from the value lists no case (O3), the reading in the README
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors(find + "f = (int id) => {\n    return Find(id)\n}\nprint(f(1))"));

        // O3 with O1 (the owner, 2026-10-21): a propagated case is listed as a case after the first one, so a return type that is the case itself,
        // or that names it first, lists it nowhere; a case after the first lists a case that widens into it (the owner, 2026-10-21) or derives
        // from it (the record's reading), and a case that would go to two cases after the first is unlisted-case, as a value that would go to two cases is
        // type-mismatch (the owner, 2026-10-21); a case that is a case after the first is that case, and no other fit is looked for (the owner,
        // 2026-10-21)
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors(find + "NotFound Lost(int id) {\n    value = Find(id)\n    print(value)\n    return NotFound\n}\n\nprint(Lost(1))"));
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors(find + "NotFound | int Lost(int id) {\n    value = Find(id)\n    print(value)\n    return NotFound\n}\n\nmatch Lost(1) {\n    NotFound => print(0)\n    int n => print(n)\n}"));
        Clean("data User(string Name)\ndata Admin(int Level) : User\n\nint | Admin Get(int id) {\n    if id == 1 {\n        return 1\n    }\n    return Admin(\"a\", 2)\n}\n\nint | User Load(int id) {\n    return Get(id) + 1\n}\n\nmatch Load(1) {\n    int n => print(n)\n    User u => print(u.Name)\n}");
        Clean("string | int Get(int id) {\n    if id == 1 {\n        return \"one\"\n    }\n    return id\n}\n\nstring | long Load(int id) {\n    got = Get(id)\n    return got\n}\n\nmatch Load(1) {\n    string s => print(s)\n    long n => print(n)\n}");
        Assert.Equal([(ErrorIds.UnlistedCase, 9)], Errors("string | byte Get(int id) {\n    if id == 1 {\n        return \"one\"\n    }\n    return byte(id)\n}\n\nstring | short | int Load(int id) {\n    got = Get(id)\n    return got\n}\n\nmatch Load(1) {\n    string s => print(s)\n    short n => print(n)\n    int i => print(i)\n}"));
        Assert.Equal([(ErrorIds.UnlistedCase, 13)], Errors("data Person(string Name)\ndata User(int Id) : Person\ndata Admin(int Level) : User\n\nint | Admin Get(int id) {\n    if id == 1 {\n        return 1\n    }\n    return Admin(\"a\", 1, 2)\n}\n\nint | User | Person Load(int id) {\n    return Get(id) + 1\n}\n\nmatch Load(1) {\n    int n => print(n)\n    User u => print(u.Id)\n    Person p => print(p.Name)\n}"));
        Clean("string | short Get(int id) {\n    if id == 1 {\n        return \"one\"\n    }\n    return short(id)\n}\n\nstring | short | int Load(int id) {\n    got = Get(id)\n    return got\n}\n\nmatch Load(1) {\n    string s => print(s)\n    short n => print(n)\n    int i => print(i)\n}");

        // O3 with O1 (the owner, 2026-10-21): a propagated case is looked for among the cases after the first only, so a case that is the success
        // type of the calling function leaves as the case after the first it widens into or derives from
        Clean("bool | short Check(int id) {\n    if id == 1 {\n        return true\n    }\n    return short(id)\n}\n\nshort | int Load(int id) {\n    ok = Check(id)\n    print(ok)\n    return int(0)\n}\n\nmatch Load(2) {\n    short s => print(s)\n    int i => print(i)\n}");
        Clean("data User(string Name)\ndata Admin(int Level) : User\n\nbool | Admin Check(int id) {\n    if id == 1 {\n        return true\n    }\n    return Admin(\"a\", id)\n}\n\nAdmin | User Load(int id) {\n    ok = Check(id)\n    print(ok)\n    return User(\"b\")\n}\n\nmatch Load(2) {\n    Admin a => print(a.Level)\n    User u => print(u.Name)\n}");
    }

    [Fact]
    public void A_written_union_type_keeps_every_case_and_names_them_all()
    {
        // O4: the success case alone unwraps; a type the success converts to counts as the success
        var find = "data NotFound\ndata Rejected\n\nint | NotFound | Rejected Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    if id == 2 {\n        return Rejected\n    }\n    return NotFound\n}\n\n";
        Assert.Equal([(ErrorIds.TypeMismatch, 14)], Errors(find + "int | NotFound result = Find(1)\nmatch result {\n    int n => print(n)\n    NotFound => print(\"none\")\n}"));
        Clean(find + "int | NotFound | Rejected result = Find(1)\nmatch result {\n    int n => print(n)\n    NotFound => print(\"none\")\n    Rejected => print(\"invalid\")\n}");
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
        Assert.Equal([(ErrorIds.UnlistedCase, 11)], Errors("data NotFound\ndata Rejected\n\nint | NotFound | Rejected Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\nvalue = Find(2) else {\n    NotFound => 0\n}\nprint(value)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 13)], Errors("data NotFound\ndata Rejected\ndata Other\n\nint | NotFound | Rejected Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\nvalue = Find(2) else {\n    Other => 0\n    NotFound => 0\n}\nprint(value)")); // E1: which case the wrong arm meant is unknown, so `Rejected` is not reported beside it
        Assert.Equal([(ErrorIds.UnreachableArm, 4)], Errors("int One() => 1\n\nx = One() else {\n    else => 0\n}\nprint(x)")); // a call with no case beside its success
        Assert.Equal([(ErrorIds.Syntax, 13)], Errors(find + "int OrZero(int id) {\n    value = Find(id) else {\n        NotFound => {\n            if id > 0 {\n                print(1)\n            }\n        }\n    }\n    return value\n}\n\nprint(OrZero(1))")); // O5: the arm neither recovers, returns nor throws
        Assert.Equal([(ErrorIds.Redeclared, 12)], Errors(find + "int OrZero(int id) {\n    value = Nope(id) else {\n        NotFound id => 0\n    }\n    return value\n}\n\nprint(OrZero(1))").Where(e => e.Item1 == ErrorIds.Redeclared).ToList()); // after a call that did not type, a binding is still checked
        Assert.Equal([(ErrorIds.UnknownName, 11)], Errors(find + "value = Find(2) else {\n    Nope => 0\n}\nprint(value)")); // E1: which case the arm meant is unknown, so no case is unlisted beside it
        Assert.Equal([(ErrorIds.UnknownName, 5)], Errors("data NotFound\nvoid Log() {\n}\n\nx = Nope() else {\n    NotFound => Log()\n}\nprint(x)")); // the success is unknown, and with a `void` one the arm is a statement (O10)
        Assert.Equal([(ErrorIds.Syntax, 11)], Errors(find + "value = Find(2) else {\n    0 => 0\n}\nprint(value)")); // O5: an arm of `else` names a case type, not a literal (E4)
    }

    [Fact]
    public void A_bare_throw_stands_only_in_an_arm_of_else()
    {
        // O7; O6: a throw is an exception, which ends the actor and is no case of the result, so a function may throw a type its result does
        // not list, and the top level may throw
        Assert.Equal([(ErrorIds.ThrowNeedsValue, 2)], Errors("void Fail() {\n    throw\n}\n\nFail()"));
        Clean("void Check(int n) {\n    if n < 0 {\n        throw \"negative\"\n    }\n}\n\nCheck(-1)");
        Clean("data ConfigMissing(string Path)\n\nthrow ConfigMissing(\"app.json\")");
        Clean("data NotFound\n\nint Find(int id) {\n    throw NotFound\n}\n\nprint(Find(1))");
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
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("mut xs = List<int>()\nxs.Count = 3")); // M9 gives a list `Count` to read, no field to assign (V7's reading in the README)
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("data Point(int X)\n\nPoint(1).X = 2"));
        Clean("data Address(string City)\ndata Customer(string Name, Address Home)\n\nmut c = Customer(\"Ann\", Address(\"Graz\"))\n(c.Home).City = \"Wien\"\nprint(c.Home.City)");
        Clean("data NotFound\ndata Box(int | NotFound Value)\n\nint | NotFound Find(int id) => id\n\nmut b = Box(1)\nb.Value = Find(2)\nprint(1)"); // O4: the field's union keeps every case
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
        // D3, D15: `==` between a derived value and its base compiles; between unrelated data types, and two types derived from one
        // base, it is type-mismatch
        Clean("data Point(int X, int Y)\n\nprint(Point(1, 2) == Point(1, 2))");
        Clean("data User(int Id)\ndata Admin(int Level) : User\n\nprint(Admin(1, 3) == User(1))");
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data A(int X)\ndata B(int X)\n\nprint(A(1) == B(1))"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data User(int Id)\ndata Admin(int Level) : User\ndata Guest(int Visits) : User\n\nprint(Admin(1, 3) == Guest(1, 3))"));
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
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("int A(int x) => x\ndata A(int X)\n\nprint(A(1))")); // the later declaration, whichever kind it is
        Assert.Equal([(ErrorIds.Redeclared, 2), (ErrorIds.TypeMismatch, 3)], Errors("data A(int X)\nint A(int x) {\n    return \"s\"\n}\n\nprint(A(1).X)")); // the refused function's body is checked all the same
    }

    [Fact]
    public void A_cycle_of_bases_is_base_cycle_at_the_base_that_closes_it()
    {
        // D6 (the owner, 2026-10-21): the id base-cycle, at the base that closes the cycle, and the walk up the bases ends; that base is the base
        // of the declaration that completes the cycle where the files of the program are read in the order of their paths, of which the
        // checker reads one. Each file is read from its top, a type that is its own base is a cycle of one, a cycle is one error, and a type
        // that only derives from a cycle gets none (the record's reading)
        Assert.Equal([(ErrorIds.BaseCycle, 1)], Errors("data A(int X) : A\n\nprint(1)"));
        Assert.Equal([(ErrorIds.BaseCycle, 2)], Errors("data A(int X) : B\ndata B(int Y) : A\n\nprint(1)"));
        Assert.Equal([(ErrorIds.BaseCycle, 3)], Errors("data A(int X) : B\ndata B(int Y) : C\ndata C(int Z) : A\n\nprint(1)"));
        Assert.Equal([(ErrorIds.BaseCycle, 3)], Errors("data A(int X) : C\ndata B(int Y) : A\ndata C(int Z) : B\n\nprint(1)"));
        Assert.Equal([(ErrorIds.BaseCycle, 3)], Errors("data D(int W) : A\ndata A(int X) : B\ndata B(int Y) : A\n\nprint(D(1, 2, 3).W)"));
        Assert.Equal([(ErrorIds.BaseCycle, 2), (ErrorIds.BaseCycle, 4)], Errors("data A(int X) : B\ndata B(int Y) : A\ndata C(int Z) : D\ndata D(int W) : C\n\nprint(1)"));
    }

    [Fact]
    public void A_data_type_takes_the_data_type_among_its_bases_and_one_it_does_not_read_leaves_it_unread()
    {
        // D6 gives a data type one base, the data type among its bases wherever it stands, and a second one is `syntax` (E4); a type argument
        // on a base that is not generic is a count it does not have (T19, E4); a name that is no type is `unknown-name` (V10), and a type that
        // is no data type D6's `type-mismatch`, wherever it stands. An interface among the bases (K10), a generic base (T19) and the explicit
        // form (K17) are `not supported`, and the data type, with every one derived from it, is not read: its uses carry nothing more (E1)
        var shape = "interface Shape {\n    int Area()\n}\n\n";
        Assert.Contains("5: an interface a data type names (K10)", NotSupported(shape + "data Circle(int R) : Shape\n\nc = Circle(1)\nprint(c.R)"));
        Assert.Contains("6: an interface a data type names (K10)", NotSupported(shape + "data B(int X)\ndata A(int Y) : Shape, B\n\nprint(A(1, 2).Y)"));
        Assert.Equal(["2: a generic data type as a base (T19)"], NotSupported("data Box<T>(T Item)\ndata IntBox(int Extra) : Box<int>\ndata Big(int More) : IntBox\n\nb = Big(1, 2, 3)\nprint(b.More)"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("data A(int X)\ndata B(int Y) : A<int>\n\nprint(B(1, 2).Y)"));
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("data B(int X)\ndata A(int Y) : B, Nmae\n\nprint(A(1, 2).Y)"));
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("data B(int X)\ndata A(int Y) : Nmae, B\n\nprint(A(1, 2).Y)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("data B(int X)\ndata A(int Y) : B, int\n\nprint(A(1, 2).Y)"));
    }

    [Fact]
    public void A_field_or_a_constructor_read_as_a_statement_is_not_supported()
    {
        // K7, K14: the parser reads a field and a constructor in a type body only, whose members the checker does not read (D16, chapter 7);
        // a tree that holds one among its statements, built here since the parser makes none, gets `not supported` and no error
        var field = new FieldDeclaration(1, Modifiers.None, null, "x", new BoolLiteral(1, true));
        var constructor = new ConstructorDeclaration(2, Modifiers.None, "P", [], null, new Block(2, [], 2));
        var diagnostics = new DiagnosticBag();
        Checker.Check(new ProgramSyntax([field, constructor]), diagnostics);
        Assert.Equal(["1: not supported: a field of a type body (K7)", "2: not supported: a constructor of a type body (K14)"], diagnostics.Sorted.Select(d => d.ToString()));
    }

    // ---- print and texts (A1 to A4, A8, A10 for a type parameter, A11, A12, A14) -------------------------------

    [Fact]
    public void Print_takes_a_value_with_a_text_and_a_type_parameter_without_a_limit_has_none()
    {
        // A1, A2 to A4, A8, A10, A11, A12, A14; a data value's text is its fields' (A3), so a function value two generic types deep has none
        Clean("print(1)\nprint(\"a\")\nprint(true)\nprint('c')\nprint(0.5)\nprint(90min)\nprint(1.5kb)\nxs = List<int>()\nprint(xs)");
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("void Show<T>(T item) {\n    print(item)\n}\n\nShow(1)"));
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("void Show<T>(T item) {\n    print(\"got {item}\")\n}\n\nShow(1)"));
        Assert.Equal([(ErrorIds.NoText, 2)], Errors("f = (int x) => x\nprint(f)"));
        Assert.Equal([(ErrorIds.NoText, 5)], Errors("data Box<T>(T Value)\n\nf = (int x) => x\nb = Box(Box(f))\nprint(b)"));
        Clean("data Node(int Value, List<Node> Children)\n\nprint(Node(1, List<Node>()))");
    }

    // ---- raw code (chapter 11; the grants of R7 and R16, given as a case's header line gives them) -------------------

    [Fact]
    public void A_grant_is_raw_or_native()
    {
        // R7: the header line takes `raw`, `native` or both; the checker takes no other grant
        Assert.Throws<ArgumentException>(() => Check("print(1)", grants: "network"));
    }

    [Fact]
    public void A_raw_block_needs_the_grant_and_its_body_is_checked()
    {
        // R7: a `raw` block compiles in a package granted `allow raw`, and a program without a project file has no grant; the body is
        // checked as any block's, with the grant or without it
        Clean("raw {\n    print(1)\n}", grants: "raw");
        Assert.Equal([(ErrorIds.RawNotAllowed, 1)], Errors("raw {\n    print(1)\n}"));
        Assert.Equal([(ErrorIds.RawNotAllowed, 1)], Errors("raw {\n    print(1)\n}", grants: "native"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("x = 1\nraw {\n    y = x + \"a\"\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.RawNotAllowed, 2), (ErrorIds.TypeMismatch, 3)], Errors("x = 1\nraw {\n    print(x + \"a\")\n}"));
        // the reading on R7 with E1 (README): each block without the grant is its own error, and a read through a pointer in it is no other
        Assert.Equal([(ErrorIds.RawNotAllowed, 1), (ErrorIds.RawNotAllowed, 2)], Errors("raw {\n    raw {\n        print(1)\n    }\n}"));
        Assert.Equal([(ErrorIds.RawNotAllowed, 2)], Errors("long* p = null\nraw {\n    print(*p)\n}"));
    }

    [Fact]
    public void A_pointer_stands_anywhere_and_a_read_or_a_write_through_it_stands_in_raw()
    {
        // R10: the type, `&x`, `p + n`, `p - q`, the comparisons and `null` need no `raw` block and no grant
        Clean("void Fill(byte* buf) {\n}\n\nprint(1)");
        Clean("long n = 5\np = &n\nq = p + 2\nd = q - p\nprint(d)\nprint(p < q)\nprint(p == null)");
        Assert.Equal("long*", TypeOf("long n = 5\np = &n\nprint(p == null)", "p"));
        Assert.Equal("long", TypeOf("long n = 5\np = &n\nd = (p + 2) - p\nprint(d)", "d"));
        // a read or a write through a pointer, `*p`, `p[i]` and `(*b).Next` among them, stands inside a `raw` block
        Clean("long n = 5\np = &n\nraw {\n    print(*p + 1)\n}", grants: "raw");
        Clean("mut long n = 5\np = &n\nraw {\n    *p = 7\n}\nprint(n)", grants: "raw");
        Clean("long* p = null\nraw {\n    print(p[2])\n}", grants: "raw");
        Clean("data B(long Next)\n\nmut b = B(1)\np = &b\nraw {\n    (*p).Next = 2\n}\nprint(b.Next)", grants: "raw");
        Assert.Equal([(ErrorIds.RawRequired, 3)], Errors("long n = 5\np = &n\nprint(*p + 1)"));
        Assert.Equal([(ErrorIds.RawRequired, 3)], Errors("mut long n = 5\np = &n\n*p = 7\nprint(n)"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("long* p = null\nprint(p[2])"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("long* p = null\np[1] = 3"));
        Assert.Equal([(ErrorIds.RawRequired, 4)], Errors("data B(long Next)\n\nB* b = null\nprint((*b).Next)"));
        // what is written through a pointer takes its pointee type (T1)
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("mut long n = 5\np = &n\nraw {\n    *p = \"x\"\n}\nprint(n)", grants: "raw"));
    }

    [Fact]
    public void A_pointer_points_to_a_type_that_holds_no_counted_reference()
    {
        // R10 (round 21): a number, a `bool`, a `char`, a time type, a `decimal`, another pointer and a `data` value without a counted
        // reference are pointees; a list, a string and a `data` value that holds one are `counted-pointee`, `&x` among them, and the
        // `null` of such a pointer carries nothing more (E1)
        Clean("data P(int X)\n\nchar* c = null\nduration* d = null\ndecimal* m = null\nbool* b = null\nlong** pp = null\nP* q = null\nprint(c == null)\nprint(d == null)\nprint(m == null)\nprint(b == null)\nprint(pp == null)\nprint(q == null)");
        Assert.Equal([(ErrorIds.CountedPointee, 2)], Errors("xs = List<int>()\np = &xs\nprint(1)"));
        Assert.Equal([(ErrorIds.CountedPointee, 1)], Errors("string* p = null\nprint(1)"));
        Assert.Equal([(ErrorIds.CountedPointee, 5)], Errors("data P(int X)\ndata Q(string S)\n\nP* a = null\nQ* b = null\nprint(a == null)\nprint(b == null)"));
        // a field's pointee is judged after every field is resolved, wherever its type stands (F6), and the field then carries no error
        // of its own (E1); a parameter whose pointer is the error takes `null` with none either
        Clean("data Node(Node* Next, long V)\nprint(1)");
        Assert.Equal([(ErrorIds.CountedPointee, 1)], Errors("data Node(Node* Next, string S)\nprint(1)"));
        Assert.Equal([(ErrorIds.CountedPointee, 1)], Errors("data A(B* Link)\ndata B(string S)\n\na = A(null)\nlong n = a.Link\nprint(n)"));
        Assert.Equal([(ErrorIds.CountedPointee, 1)], Errors("void F(string* p) {\n}\n\nF(null)"));
        // the reading on R10 with F11 (README): a function value holds what it captures, a counted reference
        Assert.Equal([(ErrorIds.CountedPointee, 3)], Errors("data H((long) => long F)\n\nH* p = null\nprint(p == null)"));
    }

    [Fact]
    public void An_offset_a_difference_and_an_address_take_what_R10_names()
    {
        // R10: an offset is an `int` or a `long`, `p - q` takes two pointers of one pointee type (T1); R14: `void*` takes no offset and
        // nothing is read through it; `&x` takes a variable, a field or an element, a function's name being text no rule gives a
        // meaning (E4); a pointer is no `long` (T1), and an unknown name under `*` or `&` is that name's error alone (V10)
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("long n = 5\np = &n\nprint(p + 1.5)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("long n = 5\nint m = 1\np = &n\nq = &m\nprint(p - q)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("void* v = null\nprint(v + 1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("void* v = null\nraw {\n    print(*v)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.Syntax, 3)], Errors("int F() => 1\n\nprint(&F)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("long* p = null\nlong n = p\nprint(n)"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("print(*q)"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("p = &q"));
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("*q = 5"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("long n = 1\nint m = 2\np = &n\nq = &m\nprint(p < q)"));
        // the reading on R10 (README): `n + p` is the offset `p + n`, as C# writes both; `p - n` counts back
        Clean("long m = 64\np = Ptr<long>(m)\nq = 2 + p\nr = p - 1\nprint(q - r)");
        // an integer that widens into a `long` is an offset (T8, T23), a `ulong` is none, and a literal takes the type L10 gives it
        Clean("long* p = null\nuint u = 1\nq = p + u\nraw {\n    print(p[u])\n}\nprint(q == null)", grants: "raw");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("long* p = null\nulong u = 1\nprint(p + u == null)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("long* p = null\nulong u = 1\nraw {\n    print(p[u])\n}", grants: "raw"));
        Clean("mut long* p = null\nraw {\n    print(p[3000000000])\n    p[3000000000] = 1\n}", grants: "raw");
        // every comparison takes `null` beside a pointer; a write through a pointer whose type was the error carries nothing more (E1)
        Clean("long* p = null\nprint(p < null)\nprint(null >= p)");
        Assert.Equal([(ErrorIds.CountedPointee, 2)], Errors("s = \"hi\"\np = &s\nraw {\n    p[0] = 1\n}", grants: "raw"));
        // the reading on R10 with E1 (README): a read and a write through a pointer outside `raw` are two, as Rust reports each
        Assert.Equal([(ErrorIds.RawRequired, 5), (ErrorIds.RawRequired, 5)], Errors("mut long n = 1\nlong m = 2\np = &n\nq = &m\n*p = *q\nprint(n)"));
    }

    [Fact]
    public void Inline_assembly_stands_in_raw_and_follows_the_rules_of_its_template()
    {
        // R11: an `asm` outside a `raw` block and outside a `naked` body is `raw-required`; an operand that writes a variable without
        // `mut` is `assign-immutable`
        Clean("mut long count = 1\nraw {\n    asm(\"add {r}, 1\", r = inout(reg) count)\n}\nprint(count)", grants: "raw");
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("mut long count = 1\nasm(\"add {r}, 1\", r = inout(reg) count)\nprint(count)"));
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("long count = 1\nraw {\n    asm(\"add {r}, 1\", r = inout(reg) count)\n}\nprint(count)", grants: "raw"));
        // Rust's template and words: `{{`, a position, a modifier, `options`, `clobber_abi`, `_` as an output, `=>`, `label`, `sym`
        // and `const`; the variables an operand and a `label` block use count as used
        Clean("raw {\n    asm(\"{{}} {0}\", in(reg) 1)\n    asm(\"{0:e}\", in(reg) 1)\n    asm(\"nop\", options(nomem, nostack), clobber_abi(\"C\"))\n    asm(\"{x}\", x = out(reg) _)\n}", grants: "raw");
        Clean("long a = 1\nmut long b = 2\nlong c = 3\nraw {\n    asm(\"{x}\", x = inout(reg) a => b, options(pure, nomem))\n    asm(\"jmp {l}\", l = label {\n        print(c)\n    })\n}\nprint(b)", grants: "raw");
        Clean("void G() {\n}\n\nraw {\n    asm(\"call {f}\", f = sym G)\n    asm(\"mov rax, {c}\", c = const 4)\n}", grants: "raw");
        // a breach of the template's rules is `asm-operand`, one for each defect, and a class or a modifier is read against x86-64
        Assert.Equal([(ErrorIds.AsmOperand, 3), (ErrorIds.AsmOperand, 3)], Errors("mut long count = 1\nraw {\n    asm(\"add {x}, 1\", r = inout(reg) count)\n}\nprint(count)", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 3)], Errors("mut long count = 1\nraw {\n    asm(\"nop\", r = inout(reg) count)\n}\nprint(count)", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 3)], Errors("mut long count = 1\nraw {\n    asm(\"add {r}, 1\", r = inout(freg) count)\n}\nprint(count)", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"{0:h}\", in(reg) 1)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"nop\", options(fast))\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"nop\", clobber_abi(\"D\"))\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"{x}\", x = in(reg) _)\n}", grants: "raw"));
        // an operand of a class Rust takes only as a clobber is not read; outside a `raw` block the statement's `raw-required` is its one
        // diagnostic (E1)
        Assert.Equal(["3: the class `kreg0`, which Rust takes only as a clobber (R11)"], NotSupported("mut long n = 1\nraw {\n    asm(\"{x}\", x = out(kreg0) n)\n}\nprint(n)", grants: "raw"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("mut long n = 0\nasm(\"mov {x}, 1\", x = out(kreg0) n)\nprint(n)"));
        // Rust's block of a `label` is a context of its own, so a read through a pointer there stands in a `raw` block of its own
        Assert.Equal([(ErrorIds.RawRequired, 4)], Errors("long* p = null\nraw {\n    asm(\"jmp {l}\", l = label {\n        print(*p)\n    })\n}", grants: "raw"));
        Clean("long* p = null\nraw {\n    asm(\"jmp {l}\", l = label {\n        raw {\n            print(*p)\n        }\n    })\n}", grants: "raw");
    }

    [Theory]
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x}\", x = inout(reg) a, x = in(reg) 2)\n}\nprint(a)", 3)] // each name on one operand
    [InlineData("raw {\n    asm(\"{x} {}\", x = in(reg) 1, in(reg) 2)\n}", 2)] // the operands without a name first
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x}\", x = out(reg) a, options(pure))\n}\nprint(a)", 3)] // `pure` with `nomem` or `readonly`
    [InlineData("raw {\n    asm(\"nop\", options(nomem, readonly))\n}", 2)] // not both
    [InlineData("raw {\n    asm(\"{c}\", c = const \"x\")\n}", 2)] // `const` an integer constant
    [InlineData("raw {\n    asm(\"{l}\", l = label 1)\n}", 2)] // `label` a block
    [InlineData("raw {\n    asm(\"{x}\", x = frobnicate(reg) 1)\n}", 2)] // Rust's words
    [InlineData("raw {\n    asm(\"{x}\", x = in(reg, reg) 1)\n}", 2)] // one class
    [InlineData("raw {\n    asm(\"{x}\", x = in(reg))\n}", 2)] // a value after the class
    [InlineData("mut long b = 1\nraw {\n    asm(\"{x}\", x = in(reg) 1 => b)\n}\nprint(b)", 3)] // `=>` after `inout` and `inlateout` alone
    [InlineData("mut string s = \"a\"\nraw {\n    asm(\"{x}\", x = inout(reg) s)\n}\nprint(s)", 3)] // a type the class takes
    [InlineData("int a = 1\nmut long b = 2\nraw {\n    asm(\"{x}\", x = inout(reg) a => b)\n}\nprint(b)", 4)] // two values of one width
    [InlineData("raw {\n    asm(\"{x}\", x = out(reg) 1 + 2)\n}", 2)] // a place to write
    [InlineData("raw {\n    asm(\"{s}\", s = sym 4)\n}", 2)] // `sym` a name
    [InlineData("long n = 1\nraw {\n    asm(\"{s}\", s = sym n)\n}\nprint(n)", 3)] // `sym` no variable
    [InlineData("raw {\n    asm(\"}\")\n}", 2)] // a brace written twice
    [InlineData("raw {\n    asm(\"{\")\n}", 2)] // a brace closed
    [InlineData("raw {\n    asm(\"{c:e}\", c = const 4)\n}", 2)] // a modifier after a register operand
    [InlineData("raw {\n    asm(\"nop\", o = options(nostack))\n}", 2)] // `options` with no name
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x}\", x = out(reg) a.Count)\n}\nprint(a)", 3)] // a field of a data value
    [InlineData("raw {\n    asm(\"nop\", options(pure, nomem))\n}", 2)] // `pure` an output
    [InlineData("raw {\n    asm(\"{x}\", x = out(reg) _, options(pure, nomem))\n}", 2)] // `pure` an output that is not `_`
    [InlineData("raw {\n    asm(\n        \"nop\",\n        options(pure, nomem)\n    )\n}", 4)] // at the line of the word the rule names
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x}\", x = out(reg) a, options(noreturn))\n}\nprint(a)", 3)] // `noreturn` no output
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x} {l}\", x = out(reg) a, l = label {\n    })\n}\nprint(a)", 3)] // a `label` no output
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x} {l}\", x = out(reg) a, l = label {\n    }, options(noreturn))\n}\nprint(a)", 3)] // `noreturn` beside a `label`: the label's rule alone
    [InlineData("mut long a = 1\nraw {\n    asm(\"{x}\", x = out(reg) a, clobber_abi(\"C\"))\n}\nprint(a)", 3)] // `clobber_abi` no output of a class
    [InlineData("mut long n = 0\nraw {\n    asm(\"mov {x:e}, 1\", x = out(vreg) n)\n}\nprint(n)", 3)] // a class x86-64 has, its modifier then not judged
    [InlineData("raw {\n    asm(\"{x y}\", in(reg) 1)\n}", 2)] // a placeholder that names no operand's way, and nothing more
    [InlineData("raw {\n    asm(\"} {x}\", x = in(reg) 1)\n}", 2)] // a brace written twice, and nothing more
    [InlineData("mut byte b = 0\nraw {\n    asm(\"{r}\", r = out(reg) b)\n}\nprint(b)", 3)] // `reg` no `byte`
    [InlineData("mut long b = 0\nraw {\n    asm(\"{r}\", r = out(reg_byte) b)\n}\nprint(b)", 3)] // `reg_byte` a byte
    [InlineData("raw {\n    asm(\"nop\", in(\"rzx\") 1)\n}", 2)] // a register of x86-64
    [InlineData("raw {\n    asm(\"nop\", in(\"rsp\") 1)\n}", 2)] // not the stack pointer
    [InlineData("raw {\n    asm(\"nop\", out(\"rbx\") _)\n}", 2)] // not LLVM's base pointer
    [InlineData("raw {\n    asm(\"nop\", in(\"ebp\") 1)\n}", 2)] // not the frame pointer
    [InlineData("raw {\n    asm(\"nop\", in(\"rip\") 1)\n}", 2)] // not the program counter
    [InlineData("mut long a = 1\nraw {\n    asm(\"nop\", inout(\"rax\") a,\n        in(\"eax\") 2)\n}\nprint(a)", 4)] // an `inout` an input
    [InlineData("mut long a = 1\nraw {\n    asm(\"nop\", inlateout(\"rcx\") a,\n        out(\"ecx\") _)\n}\nprint(a)", 4)] // an `inlateout` an output
    [InlineData("raw {\n    asm(\"nop\", in(\"rax\") 1,\n        out(\"eax\") _)\n}", 3)] // an `out` an input too, as it may write before the inputs are read
    [InlineData("string s = \"a\"\nraw {\n    asm(\"nop\", in(\"xmm0\") s)\n}", 3)] // the class of a vector register takes the type
    [InlineData("raw {\n    asm(\"nop\", in(\"rax\") 1,\n        in(\"eax\") 2)\n}", 3)] // a view of an input's register no second input
    [InlineData("mut byte b = 0\nraw {\n    asm(\"nop\", out(\"rax\") _,\n        out(\"al\") b)\n}\nprint(b)", 4)] // nor of an output's a second output
    [InlineData("raw {\n    asm(\"{}\", in(\"rdx\") 1,\n        in(reg) 2)\n}", 3)] // the operands without a name before the explicit registers
    [InlineData("raw {\n    asm(\"{0}\", in(\"rax\") 1)\n}", 2)] // the template names an explicit register nowhere
    [InlineData("long a = 1\nraw {\n    asm(\"nop\", in(\"al\") a)\n}", 3)] // the class of the register takes the type
    [InlineData("raw {\n    asm(\"{c}\", c = const {\n    })\n}", 2)] // `const` a value, not a block
    [InlineData("mut long x = 0\nraw {\n    asm(\"{c}\", c = const 4 => x)\n}\nprint(x)", 3)] // and nothing it writes
    [InlineData("raw {\n    asm(\"{f}\", f = sym {\n    })\n}", 2)] // `sym` a name, not a block
    [InlineData("void G() {\n}\n\nmut long x = 0\nraw {\n    asm(\"{f}\", f = sym G => x)\n}\nprint(x)", 6)] // and nothing it writes
    [InlineData("long* p = null\nmut int m = 0\nraw {\n    asm(\"{r}\", r = inout(reg) p => m)\n}\nprint(m)", 4)] // a pointer 64 bits wide
    public void A_breach_of_the_rules_of_an_asm_is_asm_operand(string program, int line)
    {
        // R11: the rest of Rust's rules of the template and of its words, for x86-64, each one `asm-operand` at the operand's or the
        // template's line
        Assert.Equal([(ErrorIds.AsmOperand, line)], Errors(program, grants: "raw"));
    }

    [Theory]
    [InlineData("raw {\n    asm(\"nop\", in(\"rzx\") 1)\n}", "name one of its registers, `rax` or `xmm0`, or a register class")]
    [InlineData("raw {\n    asm(\"nop\", in(\"rsp\") 1)\n}", "name another register")]
    [InlineData("raw {\n    asm(\"nop\", in(\"rax\") 1, in(\"eax\") 2)\n}", "give one of them another register")]
    [InlineData("raw {\n    asm(\"nop\", out(\"rax\") _, out(\"al\") _)\n}", "give one of them another register")]
    [InlineData("raw {\n    asm(\"nop\", out(\"rax\") _, in(\"eax\") 1)\n}", "write `lateout`, or give one of them another register")]
    [InlineData("raw {\n    asm(\"{c}\", c = const {\n    })\n}", "takes one value after it, `c = const 4`")]
    [InlineData("raw {\n    asm(\"{}\", in(\"rdx\") 1, in(reg) 2)\n}", "stands before the named ones and those in an explicit register")]
    public void An_asm_operand_message_names_what_to_change(string program, string remedy)
    {
        // R11: the message of each rule an operand in an explicit register, a `const` or a `sym` breaks names the change that keeps it
        var (diagnostics, _) = Check(program, "raw");
        var error = Assert.Single(diagnostics.Items);
        Assert.Equal(ErrorIds.AsmOperand, error.Id);
        Assert.Contains(remedy, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_asm_that_follows_the_rules_is_clean_and_a_template_names_what_is_there()
    {
        // R11: `lateout`, `inlateout`, `reg_abcd` with `h`, `{}` in order, `sym` of state, the classes of a byte and a vector, and a
        // pointer in and a `long` out of one register; a name no operand has, or a position the `asm` has none at, leaves the operand
        // unused too, two breaches
        Clean("mut byte b = 0\nmut double d = 0.5\nraw {\n    asm(\"{x} {y}\", x = out(reg_byte) b, y = out(xmm_reg) d)\n}\nprint(b)\nprint(d)", grants: "raw");
        Clean("long* p = null\nmut long n = 0\nraw {\n    asm(\"{r}\", r = inout(reg) p => n)\n}\nprint(n)", grants: "raw");
        Clean("mut long a = 1\nraw {\n    asm(\"{x}\", x = lateout(reg) a)\n}\nprint(a)", grants: "raw");
        Clean("mut long a = 1\nraw {\n    asm(\"{x}\", x = inlateout(reg) a)\n}\nprint(a)", grants: "raw");
        Clean("raw {\n    asm(\"{0:h}\", in(reg_abcd) 1)\n}", grants: "raw");
        Clean("raw {\n    asm(\"{} {}\", in(reg) 1, in(reg) 2)\n}", grants: "raw");
        Clean("static mut long t = 0\n\nraw {\n    asm(\"lea rax, {s}\", s = sym t)\n}", grants: "raw");
        // R11: an operand in an explicit register, which the template names nowhere: a view of a register as the register, an input and
        // a `lateout` of one register, a clobber, a byte register and a vector register, after the operands without a name, and with
        // `clobber_abi`, whose outputs are explicit registers; a pointer goes into a vector register as Rust's 64-bit integer
        Clean("mut long n = 0\nraw {\n    asm(\"syscall\", in(\"rax\") 60, in(\"edi\") 0, lateout(\"rax\") n, out(\"rcx\") _, out(\"r11\") _)\n}\nprint(n)", grants: "raw");
        Clean("mut byte b = 0\nraw {\n    asm(\"{} {}\", in(reg) 1, in(reg) 2, out(\"al\") b, in(\"xmm0\") 1.5, in(\"r8d\") 3)\n}\nprint(b)", grants: "raw");
        Clean("void G() {\n}\n\nraw {\n    asm(\"call {f}\", f = sym G, clobber_abi(\"C\"), out(\"rax\") _)\n}", grants: "raw");
        Clean("long* p = null\nraw {\n    asm(\"{r}\", r = in(xmm_reg) p)\n}", grants: "raw");
        Clean("extern \"C\" {\n    int abs(int n)\n}\n\nraw {\n    asm(\"{r}\", r = in(xmm_reg) abs)\n}", grants: "raw native");
        Assert.Equal([(ErrorIds.AsmOperand, 2), (ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"{x}\", in(reg) 1)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.AsmOperand, 2), (ErrorIds.AsmOperand, 2)], Errors("raw {\n    asm(\"{1}\", in(reg) 1)\n}", grants: "raw"));
        // R10: `&` of a field and of an element; an index that is no offset, and `*` of a value that is no pointer, are `type-mismatch`
        Clean("data P(long X)\n\nmut p = P(1)\nq = &p.X\nprint(q == null)");
        Clean("xs = List<long>()\nq = &xs[0]\nprint(q == null)");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("long* p = null\nraw {\n    print(p[1.5])\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("long n = 1\nraw {\n    print(*n)\n}", grants: "raw"));
        // R16: `pub` on a C declaration, which another package may call; another modifier has no meaning there (E4); a result that
        // cannot cross to C and an argument in the place of `...` that C would widen or that cannot cross are `native-type`
        Clean("extern \"C\" {\n    pub int abs(int n)\n}\n\nprint(1)", grants: "native");
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("extern \"C\" {\n    static int abs(int n)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.NativeType, 2)], Errors("extern \"C\" {\n    string f()\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.NativeType, 7)], Errors("extern \"C\" {\n    int printf(byte* f, ...)\n}\n\nbyte b = 1\nraw {\n    print(printf(null, b))\n}", grants: "raw native"));
        Assert.Equal([(ErrorIds.NativeType, 6)], Errors("extern \"C\" {\n    int printf(byte* f, ...)\n}\n\nraw {\n    print(printf(null, \"x\"))\n}", grants: "raw native"));
        // R13: `void` is no type of state (F4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("static mut void t = 0\nprint(1)", grants: "raw"));
    }

    [Theory]
    [InlineData("raw {\n    asm(\"fldz\", out(\"st(0)\") _)\n}", "raw", "2: the register `st(0)`, of the class `x87_reg`, which Rust takes only as a clobber (R11)")]
    [InlineData("raw {\n    asm(\"nop\", in(\"ymm1\") 1.5)\n}", "raw", "2: the register `ymm1`, of the class `ymm_reg`, which Rust gives a target with the feature `avx`, a feature no rule of a build names (R11)")]
    [InlineData("raw {\n    asm(\"nop\", in(\"xmm16\") 1.5)\n}", "raw", "2: the register `xmm16`, of the class `zmm_reg`, which Rust gives a target with the feature `avx512f`, a feature no rule of a build names (R11)")]
    [InlineData("raw {\n    asm(\"nop\", in(\"k1\") 1)\n}", "raw", "2: the register `k1`, of the class `kreg`, which Rust gives a target with the feature `avx512f`, a feature no rule of a build names (R11)")]
    [InlineData("raw {\n    asm(\"nop\", out(\"k0\") _)\n}", "raw", "2: the register `k0`, of the class `kreg0`, which Rust takes only as a clobber (R11)")]
    [InlineData("raw {\n    asm(\"emms\", out(\"mm0\") _)\n}", "raw", "2: the register `mm0`, of the class `mmx_reg`, which Rust takes only as a clobber (R11)")]
    [InlineData("raw {\n    asm(\"nop\", out(\"tmm0\") _)\n}", "raw", "2: the register `tmm0`, of the class `tmm_reg`, which Rust takes only as a clobber (R11)")]
    [InlineData("raw {\n    asm(\"nop\", x = in(\"rax\") 1)\n}", "raw", "2: a name on an operand in an explicit register, which Rust's Reference allows and Rust's compiler refuses (R11)")]
    [InlineData("mut long k = 0\nraw {\n    asm(\"{z}\", z = out(kreg) k)\n}\nprint(k)", "raw", "3: the class `kreg`, which Rust gives a target with the feature `avx512f`, a feature no rule of a build names (R11)")]
    [InlineData("raw {\n    asm(\"{y}\", y = in(ymm_reg) 1.5)\n}", "raw", "2: the class `ymm_reg`, which Rust gives a target with the feature `avx`, a feature no rule of a build names (R11)")]
    [InlineData("long m = 64\np = Ptr<long>(m)\nq = &*p\nprint(q == null)", "", "3: the address of a place through a pointer, `&*p`, `&p[i]` or `&(*b).Next` (R10)")]
    [InlineData("long m = 64\np = Ptr<long>(m)\nq = &p[1]\nprint(q == null)", "", "3: the address of a place through a pointer, `&*p`, `&p[i]` or `&(*b).Next` (R10)")]
    [InlineData("data B(long Next)\n\nB* b = null\nq = &(*b).Next\nprint(q == null)", "", "4: the address of a place through a pointer, `&*p`, `&p[i]` or `&(*b).Next` (R10)")]
    [InlineData("void G() {\n}\n\nvoid G(int n) {\n}\n\nraw {\n    asm(\"call {f}\", f = sym G)\n}", "raw", "8: `sym` of a function with more than one overload or with type parameters (R11)")]
    [InlineData("static mut long t = 0\nq = &t\nprint(q == null)", "raw", "2: the address of state outside a function, `&` of a `static mut` (R10, R13)")]
    [InlineData("void F<T>(T* p) {\n}\n\nprint(1)", "", "1: a pointer to a type that holds a type parameter (R10, T19)")]
    [InlineData("void Run(extern \"C\" (string) => long f) {\n}\n\nprint(1)", "", "1: a type that cannot cross to C in the type of a C function (R17)")]
    [InlineData("extern \"C\" {\n    int printf(byte* f, ...)\n}\n\nraw {\n    print(printf(null, 1.5m))\n}", "raw native", "6: a `decimal` in the place of `...`, which no rule says crosses to C (R16)")]
    [InlineData("extern \"C\" long Id<T>(long x) {\n    return x\n}\n\nprint(1)", "native", "1: a function of the C calling convention with type parameters (R12, R16, T19)")]
    [InlineData("extern \"C\" long Inc(mut long x) {\n    return x\n}\n\nprint(1)", "native", "1: a `mut` parameter of a function of the C calling convention (R12, R16)")]
    [InlineData("extern \"C\" long F(long x) {\n    return x\n}\n\nlong F(int x) => 1\n\nprint(1)", "native", "1: a function of the C calling convention beside another function of its name (R12, R16, F12)")]
    [InlineData("extern \"C\" {\n    int printf(byte* f, ...)\n}\n\nf = printf\nprint(1)", "native", "5: the value of a C function that takes `...`, which the type of a C function does not write (R16, R17)")]
    [InlineData("long* p = null\nprint(p)", "", "2: the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)")]
    [InlineData("long* p = null\nprint(\"at {p}\")", "", "2: the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)")]
    [InlineData("data Block(long* Next)\n\nprint(Block(null))", "", "3: the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)")]
    [InlineData("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nextern \"C\" (long) => long f = Twice\nprint(f)", "native", "6: the text of a raw pointer or of a C function value, which no rule gives (A4, R10, R17)")]
    public void A_raw_form_no_rule_gives_an_id_is_not_supported(string program, string grants, string first)
    {
        // the README's list: what R10 to R17 leave open, each `not supported` at its line and nothing more (E1)
        Assert.Equal([first], NotSupported(program, grants));
    }

    [Fact]
    public void A_naked_function_needs_the_grant_and_its_body_is_one_asm()
    {
        // R12: `allow raw`, else `naked-not-allowed` alone; the body is one `asm` with `sym` and `const` operands alone, else
        // `naked-body`; a `naked extern "C"` function needs `allow native` too, both errors at its line in a package with neither grant
        Clean("naked void Switch() {\n    asm(\"ret\")\n}\n\nSwitch()", grants: "raw");
        Clean("naked void F() {\n    asm(\"call {f}\", f = sym G)\n}\n\nvoid G() {\n}\n\nprint(1)", grants: "raw");
        Assert.Equal([(ErrorIds.NakedNotAllowed, 1)], Errors("naked void Switch() {\n    asm(\"ret\")\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.NakedBody, 2)], Errors("naked void F() {\n    print(1)\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 3)], Errors("naked void F() {\n    asm(\"ret\")\n    asm(\"ret\")\n}\n\nprint(1)", grants: "raw"));
        // R12: an operand in an explicit register, or of a class the checker does not read, is `naked-body` there as any operand but `sym`
        // and `const`; the template names the first nowhere, and its rules hold the second as any operand of a class (below)
        Assert.Equal([(ErrorIds.NakedBody, 2)], Errors("naked void F() {\n    asm(\"ret\", in(\"rax\") 1)\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 2), (ErrorIds.AsmOperand, 2)], Errors("naked void F() {\n    asm(\"ret\", out(kreg0) _)\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 1)], Errors("naked void F() {\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 2), (ErrorIds.AsmOperand, 2)], Errors("naked void F() {\n    asm(\"ret\", in(reg) 1)\n}\n\nprint(1)", grants: "raw"));
        // R11 counts `clobber_abi` and `options` among the words of the operands, so there they are operands other than `sym` and
        // `const`; and an output or a `label` there is `naked-body` alone, with no rule of the outputs beside it
        Assert.Equal([(ErrorIds.NakedBody, 2)], Errors("naked void F() {\n    asm(\"ret\", clobber_abi(\"C\"))\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 2)], Errors("naked void F() {\n    asm(\"ret\", options(att_syntax))\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedBody, 2), (ErrorIds.NakedBody, 2)], Errors("naked void F() {\n    asm(\"ret {x} {l}\", x = out(reg) _, l = label {\n    })\n}\n\nprint(1)", grants: "raw"));
        Clean("naked extern \"C\" void F() {\n    asm(\"ret\")\n}\n\nprint(1)", grants: "raw native");
        Assert.Equal([(ErrorIds.NativeNotAllowed, 1)], Errors("naked extern \"C\" void F() {\n    asm(\"ret\")\n}\n\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.NakedNotAllowed, 1), (ErrorIds.NativeNotAllowed, 1)], Errors("naked extern \"C\" void F() {\n    asm(\"ret\")\n}\n\nprint(1)"));
        // the `asm` of a `naked` body takes no `raw` block, and one beside it in the top-level code does
        Assert.Equal([(ErrorIds.RawRequired, 6)], Errors("naked void F() {\n    asm(\"ret\")\n}\n\nmut long n = 1\nasm(\"{x}\", x = inout(reg) n)\nprint(n)", grants: "raw"));
    }

    [Fact]
    public void State_outside_a_function_is_read_and_written_in_raw()
    {
        // R13: `static mut` and `thread static mut` at the top level, in a package granted `allow raw`, reached by every function inside
        // a `raw` block; never read, it is no `unused-variable` (V5)
        Clean("static mut long total = 0\n\nvoid Add() {\n    raw {\n        total = total + 1\n    }\n}\n\nAdd()", grants: "raw");
        Clean("static mut long total = 0\nthread static mut long depth = 0\nstatic mut long* head = null\nprint(1)", grants: "raw");
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("static mut long total = 0\nprint(total)", grants: "raw"));
        // `static-state`: without the grant, with `pub`, of a type that holds a counted reference, without an initial value or with one
        // that is no constant (F12)
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("static mut long total = 0\nprint(1)"));
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("pub static mut long t = 0\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("static mut string s = \"\"\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("static mut long t\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("thread static mut long t\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.StaticState, 3)], Errors("long Seed() => 1\n\nstatic mut long t = Seed()\nprint(1)", grants: "raw"));
        // the reading on R13 with F12 (README): the initial value sees no variable of the top-level code, as a default does not
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("long n = 1\nstatic mut long t = n\nprint(n)", grants: "raw"));
        // a `static` without `mut`, and one inside a block or a function, has no meaning on a variable (V2, V4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("static long total = 0\nprint(total)", grants: "raw"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("if true {\n    static mut long total = 0\n    print(total)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("void F() {\n    static mut long total = 0\n    print(total)\n}\n\nF()", grants: "raw"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("void F() {\n    static mut long t\n    print(t)\n}\n\nF()", grants: "raw"));
        // the state is visible in every function, so a parameter of its name is `redeclared` (V9), and a write there goes to the parameter
        Assert.Equal([(ErrorIds.Redeclared, 3), (ErrorIds.AssignImmutable, 4)], Errors("static mut long total = 0\n\nvoid F(long total) {\n    total = 2\n}\n\nF(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("static mut long t = 0\nstatic mut long t = 1\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("static mut long t = 0\nstatic mut string t = \"\"\nraw {\n    t = 1\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.Redeclared, 4)], Errors("void total() {\n}\n\nstatic mut long total = 0\nraw {\n    total = 1\n    print(total)\n}", grants: "raw"));
        // the state of the file is visible to each initial value and to each default, in either order (F6), and a read of it is no
        // constant: the value's one error (F12, E1)
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("static mut long a = b\nstatic mut long b = 0\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.StaticState, 2)], Errors("static mut long a = 0\nstatic mut long b = a + 1\nprint(1)", grants: "raw"));
        Assert.Equal([(ErrorIds.DefaultNotConstant, 3)], Errors("static mut long total = 0\n\nvoid F(long n = total) {\n    print(n)\n}\n\nF()", grants: "raw"));
        Assert.Equal(["1: `thread static` before a function, which R13 states before state alone (R13)"], NotSupported("thread static void F() {\n}\n\nF()", grants: "raw"));
    }

    [Theory]
    [InlineData("static mut long t = 0\nprint(1)", "", "grant")]
    [InlineData("pub static mut string s = \"\"\nprint(1)", "raw", "counted reference")]
    [InlineData("static mut string s\nprint(1)", "raw", "counted reference")]
    [InlineData("pub static mut long t\nprint(1)", "raw", "takes an initial value")]
    [InlineData("long Seed() => 1\n\npub static mut long t = Seed()\nprint(1)", "raw", "constant expression")]
    [InlineData("pub static mut long t = 0\nprint(1)", "raw", "no `pub`")]
    public void State_is_judged_in_the_order_R13_lists_its_causes(string program, string grants, string cause)
    {
        // the reading on R13 (README): the grant, the type, the initial value, `pub`; one `static-state` a declaration, naming the first
        var (diagnostics, _) = Check(program, grants);
        var error = Assert.Single(diagnostics.Items);
        Assert.Equal(ErrorIds.StaticState, error.Id);
        Assert.Contains(cause, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conversion_of_a_pointer_and_the_size_of_a_type_need_no_raw()
    {
        // R14: `Ptr<T>(x)` of a pointer or a `long`, `long(p)` and `SizeOf<T>()`, a `long`, without a `raw` block or a grant
        Clean("long m = 64\np = Ptr<long>(m)\nprint(long(p + 3) - long(p))");
        Clean("int* p = null\nq = Ptr<long>(p)\nv = Ptr<void>(long(q))\nprint(v == null)");
        // T10 with R14: `Ptr<T>(x)` takes one value, as a conversion does; the parser reads no other count as a conversion
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("p = Ptr<long>()\nprint(p == null)"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("p = Ptr<long>(1, 2)\nprint(p == null)"));
        Assert.Equal("long*", TypeOf("long m = 64\np = Ptr<long>(m)\nprint(p == null)", "p"));
        Assert.Equal("long", TypeOf("s = SizeOf<long>()\nprint(s)", "s"));
        Assert.Equal([(ErrorIds.CountedPointee, 1)], Errors("print(SizeOf<string>())"));
        Assert.Equal([(ErrorIds.CountedPointee, 2)], Errors("long m = 64\nprint(Ptr<string>(m) == null)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(SizeOf<void>())"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("print(Ptr<long>(\"x\"))"));
        // the reading on R14 (README): an integer that widens into a `long` is one there (T8, T23), a `ulong` none; `void*` takes no
        // `p - n` and no `p - q`, as no `p + n`
        Clean("int m = 64\np = Ptr<long>(m)\nprint(p == null)");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("ulong m = 64\nprint(Ptr<long>(m) == null)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("void* v = null\nprint(v - 1 == null)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("void* v = null\nvoid* w = null\nprint(v - w)"));
        // another count of type arguments or arguments is no form of R14, `syntax` (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(SizeOf())"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(SizeOf<long>(1))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("long m = 64\nprint(long(Ptr(m)))"));
    }

    [Fact]
    public void The_memory_of_a_string_and_a_list_shows_in_raw()
    {
        // R15: `s.Pointer`, `l.Pointer`, `string.Allocate`, `string.FromRaw` and `List<T>.FromRaw` inside a `raw` block, `raw-required`
        // outside it, and a list of a type that holds a counted reference is `counted-pointee`
        Clean("s = \"ab\"\nraw {\n    p = s.Pointer\n    print(string.FromRaw(p, 2))\n}", grants: "raw");
        Clean("raw {\n    byte* p = string.Allocate(2)\n    p[0] = 104\n    p[1] = 105\n    print(string.FromRaw(p, 2))\n}", grants: "raw");
        Clean("xs = List<long>()\nraw {\n    p = xs.Pointer\n    ys = List<long>.FromRaw(p, 0)\n    print(ys.Count)\n}", grants: "raw");
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("s = \"ab\"\nprint(s.Pointer == null)"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("byte* p = null\nprint(string.FromRaw(p, 0))"));
        Assert.Equal([(ErrorIds.RawRequired, 1)], Errors("print(string.Allocate(2) == null)"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("xs = List<long>()\nprint(xs.Pointer == null)"));
        Assert.Equal([(ErrorIds.RawRequired, 2)], Errors("long* p = null\nys = List<long>.FromRaw(p, 0)\nprint(ys.Count)"));
        Assert.Equal([(ErrorIds.CountedPointee, 3)], Errors("xs = List<string>()\nraw {\n    print(xs.Pointer == null)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.CountedPointee, 2)], Errors("raw {\n    xs = List<string>.FromRaw(Ptr<long>(64), 1)\n    print(xs.Count)\n}", grants: "raw"));
        // the reading on R14 with R15 (README): `void` has no size, so a list of its elements counts none, `type-mismatch` as `SizeOf<void>()`
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("raw {\n    xs = List<void>.FromRaw(Ptr<void>(64), 2)\n    print(xs.Count)\n}", grants: "raw"));

        // `List<T>.Allocate`, the room `List<T>.FromRaw` takes over, and both through `Bytes` (section 3, the owner, 2026-10-29)
        Clean("raw {\n    long* p = List<long>.Allocate(4)\n    ys = List<long>.FromRaw(p, 0)\n    print(ys.Count)\n}", grants: "raw");
        Clean("raw {\n    byte* p = Bytes.Allocate(2)\n    p[0] = 104\n    b = Bytes.FromRaw(p, 1)\n    print(b)\n}", grants: "raw");
        Assert.Equal([(ErrorIds.RawRequired, 1)], Errors("print(List<long>.Allocate(4) == null)"));
        Assert.Equal([(ErrorIds.RawRequired, 1)], Errors("print(Bytes.Allocate(4) == null)"));
        Assert.Equal([(ErrorIds.CountedPointee, 2)], Errors("raw {\n    print(List<string>.Allocate(1) == null)\n}", grants: "raw"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("raw {\n    print(List<void>.Allocate(1) == null)\n}", grants: "raw"));
    }

    [Fact]
    public void A_C_function_is_declared_with_the_native_grant_and_called_in_raw()
    {
        // R16: a block needs `allow native` and a call of its functions a `raw` block, which needs `allow raw`; a declaration takes
        // defaults, `null` among them (F12), and its call named arguments; a block names its library after `in`
        Clean("extern \"C\" {\n    int abs(int n)\n}\n\nraw {\n    print(abs(-3))\n}", grants: "raw native");
        Clean("extern \"C\" {\n    long write(int fd, byte* buf = null, long n = 0)\n}\n\nraw {\n    print(write(1, n: 2))\n}", grants: "raw native");
        Clean("extern \"C\" in \"m\" {\n    double cos(double x)\n}\n\nprint(1)", grants: "native");
        Assert.Equal([(ErrorIds.NativeNotAllowed, 1)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.RawRequired, 5)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nprint(abs(-3))", grants: "raw native"));
        Assert.Equal([(ErrorIds.RawNotAllowed, 5)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nraw {\n    print(abs(-3))\n}", grants: "native"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nraw {\n    print(abs(\"x\"))\n}", grants: "raw native"));
        // a type that cannot cross to C, a `mut` parameter, and an argument in the place of `...` that C would widen are `native-type`
        Assert.Equal([(ErrorIds.NativeType, 2)], Errors("extern \"C\" {\n    int f(string s)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.NativeType, 2)], Errors("extern \"C\" {\n    int f(mut int s)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.NativeType, 2)], Errors("extern \"C\" {\n    int f(mut long* p)\n}\n\nmut long* q = null\nraw {\n    print(f(mut q))\n    print(f(q))\n}", grants: "raw native"));
        Assert.Equal([(ErrorIds.NativeType, 6), (ErrorIds.NativeType, 6)], Errors("extern \"C\" {\n    int printf(byte* f, ...)\n}\n\nraw {\n    print(printf(null, 1, true, 2.5f))\n}", grants: "raw native"));
        // a function of the package's own in the C calling convention is called with no `raw` block and needs `allow native`
        Clean("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nprint(Twice(4))", grants: "native");
        Assert.Equal([(ErrorIds.NativeNotAllowed, 1)], Errors("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nprint(Twice(4))"));
        // after a grant's error, what R12 and R16 leave open in the declaration is not read and says nothing more (E1)
        Assert.Equal([(ErrorIds.NativeNotAllowed, 1)], Errors("extern \"C\" long Len(string s) {\n    return 1\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.NakedNotAllowed, 1)], Errors("naked void F<T>() {\n    asm(\"ret\")\n}\n\nprint(1)"));
        // what no rule says crosses to C is not read
        Assert.Equal(["2: a `decimal` in a C declaration, which no rule says crosses to C (R16)"], NotSupported("extern \"C\" {\n    int f(decimal d)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal(["1: a type that cannot cross to C in a function of the C calling convention (R12, R16)"], NotSupported("extern \"C\" long Len(string s) {\n    return 1\n}\n\nprint(Len(\"a\"))", grants: "native"));
    }

    [Fact]
    public void A_C_declaration_whose_name_is_taken_is_redeclared_at_the_second()
    {
        // R16 with V9: in either order, in one block and across two, against a type and a built-in; the body of the other function is
        // checked all the same
        Assert.Equal([(ErrorIds.Redeclared, 5)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nint abs(int n) => n", grants: "native"));
        Assert.Equal([(ErrorIds.Redeclared, 6)], Errors("long abs(long n) {\n    return n\n}\n\nextern \"C\" {\n    int abs(int n)\n}\n\nprint(abs(1))", grants: "native"));
        Assert.Equal([(ErrorIds.Redeclared, 3)], Errors("extern \"C\" {\n    int abs(int n)\n    long abs(long n)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.Redeclared, 6)], Errors("extern \"C\" {\n    int abs(int n)\n}\n\nextern \"C\" {\n    long abs(long n)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("extern \"C\" {\n    int print(int n)\n}\n\nprint(1)", grants: "native"));
        Assert.Equal([(ErrorIds.Redeclared, 4)], Errors("data Point(int X)\n\nextern \"C\" {\n    int Point(int n)\n}\n\nprint(Point(1).X)", grants: "native"));
        Assert.Equal([(ErrorIds.UnknownName, 2), (ErrorIds.Redeclared, 6)], Errors("long abs(long n) {\n    return m\n}\n\nextern \"C\" {\n    int abs(int n)\n}", grants: "native"));
    }

    [Fact]
    public void A_value_of_a_C_function_type_is_the_name_of_a_C_function()
    {
        // R17: the type stands where a pointer stands, without a grant; its value is the name of a function of the C calling convention,
        // the package's own or a declared one, and a call through it stands in a `raw` block; a lambda or a Kurz function is `type-mismatch`
        Clean("void Run(extern \"C\" (long) => long f) {\n}\n\nprint(1)");
        Clean("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nextern \"C\" (long) => long f = Twice\nraw {\n    print(f(4))\n}", grants: "raw native");
        Clean("extern \"C\" {\n    int abs(int n)\n}\n\nextern \"C\" (int) => int f = abs\nraw {\n    print(f(-3))\n}", grants: "raw native");
        Clean("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nvoid Run(extern \"C\" (long) => long f) {\n}\n\nRun(Twice)", grants: "native");
        Assert.Equal([(ErrorIds.RawRequired, 6)], Errors("extern \"C\" long Twice(long x) {\n    return x * 2\n}\n\nextern \"C\" (long) => long f = Twice\nprint(f(4))", grants: "native"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("void Run(extern \"C\" (long) => long f) {\n}\n\nRun((long x) => x * 2)", grants: "native"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("long Twice(long x) => x * 2\n\nvoid Run(extern \"C\" (long) => long f) {\n}\n\nRun(Twice)", grants: "native"));
        Assert.Equal(["1: a data type in C's layout, `extern \"C\" data` (R17)"], NotSupported("extern \"C\" data Point(int X, int Y)\nprint(Point(1, 2).X)", grants: "native"));
    }

    // ---- enums (D12, D18, D20) ------------------------------------------------------------------

    [Theory]
    [InlineData("enum Plan { Free = 1.5, Pro = 2 }\n\nprint(Plan.Pro)", "type-mismatch", 1)] // a constant of another type: T1's id (section 4, an assumption there)
    [InlineData("enum Plan { Free = 1u, Pro = 2 }\n\nprint(Plan.Pro)", "sign-mix", 1)]
    [InlineData("int One() {\n    return 1\n}\n\nenum Plan { Free = One(), Pro = 2 }\n\nprint(Plan.Pro)", "enum-number", 5)] // no constant
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan.From(1))", "enum-number", 3)]
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan.Free >= Plan.Pro)", "enum-number", 3)]
    [InlineData("enum Plan { Free = 1, Pro = 2 }\n\nprint(Plan(2))", "syntax", 3)]
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan)", "syntax", 3)]
    [InlineData("enum Plan { Free = 1, Pro = 2 }\n\nf = Plan.From\nprint(1)", "syntax", 3)]
    [InlineData("extern \"C\" enum Plan { Free, Pro }\n\nprint(Plan.Free)", "syntax", 1)]
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan.Gold)", "unknown-name", 3)]
    [InlineData("enum Plan { Free, Pro }\n\nprint(Plan.Free == 0)", "type-mismatch", 3)] // a value is not an integer (D12)
    [InlineData("enum Plan { Free, Pro }\nenum Tier { Free, Pro }\n\nprint(Plan.Free == Tier.Free)", "type-mismatch", 4)]
    [InlineData("enum Plan { Free, Pro }\n\nmatch Plan.Pro {\n    Plan.Free => print(1)\n    Plan.Free => print(2)\n    Plan.Pro => print(3)\n}", "unreachable-arm", 5)]
    [InlineData("enum Plan { Free, Pro }\n\nmatch Plan.Pro {\n    Plan p => print(p)\n    Plan.Free => print(2)\n}", "unreachable-arm", 5)]
    [InlineData("enum Plan { Free, Pro }\n\nmatch Plan.Pro {\n    Plan.Free => print(1)\n    Plan.Pro => print(2)\n    else => print(3)\n}", "unreachable-arm", 6)]
    [InlineData("enum Plan { Free = 1, Pro = 2 }\n\nvoid Show(int n) {\n    match Plan.From(n) {\n        Plan.Free => print(1)\n        Invalid => print(0)\n    }\n}\n\nShow(1)", "match-not-exhaustive", 4)]
    [InlineData("enum Plan { Free, Pro }\n\nextern \"C\" {\n    int abs(Plan p)\n}\n\nprint(1)", "native-type", 4)] // its layout is the compiler's (R16)
    public void An_enum_shape_the_rules_do_not_give_is_its_one_error(string program, string id, int line)
    {
        Assert.Equal([(id, line)], Errors(program, grants: "native"));
    }

    [Fact]
    public void An_enum_whose_declaration_was_reported_carries_no_second_error()
    {
        // E1 with D20: the numbers, `From` and an order of a declaration that broke D20 carry nothing more
        Assert.Equal([(ErrorIds.EnumNumber, 1)], Errors("enum Plan { Free = 1, Pro = 1 }\nprint(Plan.Pro.Number)\nprint(Plan.From(1))\nprint(Plan.Free < Plan.Pro)"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("enum Plan { Free, Pro, Free }\nprint(Plan.Free)\nprint(Plan.Pro)"));
    }

    [Fact]
    public void A_match_on_an_enum_without_values_takes_no_arm_and_the_error_of_one_says_why()
    {
        // D12: no value reaches the arm of the type or `else`, as Rust 1.82 warns of either that it matches no values; with no arm above
        // it, the text names the `match` without arms and not "the arms above"
        Clean("enum Never {}\n\nvoid Show(Never n) {\n    match n {\n    }\n}\n\nprint(1)");
        foreach (var arm in new[] { "Never x => print(x)", "else => print(1)" })
        {
            var error = Assert.Single(Check($"enum Never {{}}\n\nvoid Show(Never n) {{\n    match n {{\n        {arm}\n    }}\n}}\n\nprint(1)").Diagnostics.Items);
            Assert.Equal((ErrorIds.UnreachableArm, 5), (error.Id, error.Line));
            Assert.Contains("a `match` with no arms covers it", error.Message);
        }

        // in a union the enum without values is one case of several, which a `match` with no arms does not cover
        var inUnion = Assert.Single(Check("enum Never {}\n\nvoid Show(Never | int n) {\n    match n {\n        Never x => print(x)\n        int i => print(i)\n    }\n}\n\nprint(1)").Diagnostics.Items);
        Assert.Equal((ErrorIds.UnreachableArm, 5), (inUnion.Id, inUnion.Line));
        Assert.DoesNotContain("no arms", inUnion.Message);

        // an enum with values keeps the texts of the arms above
        Assert.Contains("the arms above", Assert.Single(Check("enum Plan { Free }\n\nmatch Plan.Free {\n    Plan.Free => print(1)\n    Plan p => print(p)\n}").Diagnostics.Items).Message);
        Assert.Contains("after arms that cover every case", Assert.Single(Check("enum Plan { Free }\n\nmatch Plan.Free {\n    Plan.Free => print(1)\n    else => print(2)\n}").Diagnostics.Items).Message);
    }

    [Fact]
    public void A_declaration_of_a_built_in_data_type_names_the_rule_that_declares_it()
    {
        // V9 with D18, T15 and S1: no line of the file declares the type, so the error names the rule that does; the corpus pins the id and
        // the line (E2), and this the rule the text names
        foreach (var (text, rule) in new[] { ("data Invalid", "(D18, V9)"), ("data InvalidUtf8(int At)", "(T15, V9)"), ("data IoError(int Code)", "(S1, V9)"), ("enum IoKind { Lost }", "(S1, V9)") })
        {
            var error = Assert.Single(Check(text).Diagnostics.Items);
            Assert.Equal((ErrorIds.Redeclared, 1), (error.Id, error.Line));
            Assert.EndsWith(rule, error.Message, StringComparison.Ordinal);
        }

        // a name the file declares twice names V9 alone
        Assert.EndsWith("(V9)", Assert.Single(Check("data Rejected\ndata Rejected").Diagnostics.Items).Message, StringComparison.Ordinal);
    }

    // ---- files (chapter 13) --------------------------------------------------------------------------------

    [Fact]
    public void The_names_of_files_and_sockets_are_names_every_file_sees_with_the_functions_and_members_their_rules_give()
    {
        // S2, S8, S9 with V9: a file's own declaration of one is `redeclared`
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data File"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data Directory"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data TcpStream"));

        // V10: a function of `File` and a member of a file that no rule gives
        const string Program = "void | IoError Use() {\n    f = File.Open(\"a\")\n    f.Close()\n    File.Delete(\"a\")\n}\n\nUse() else {\n    IoError e => print(e)\n}";
        Clean(Program); // the positive control
        Assert.Equal([(ErrorIds.UnknownName, 4)], Errors(Program.Replace("File.Delete", "File.Remove", StringComparison.Ordinal)));
        Assert.Equal([(ErrorIds.UnknownName, 3)], Errors(Program.Replace("f.Close()", "f.Flush()", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_listener_accepts_and_has_a_port_and_a_stream_reads_writes_and_closes_as_a_file()
    {
        // S9 with V10: the functions and the members of a listener and a stream, every other one `unknown-name`
        const string Program = "void | IoError Serve() {\n    l = TcpListener.Bind(\"127.0.0.1:0\")\n    int port = l.Port\n    s = l.Accept()\n    mut buffer = Bytes()\n    s.Read(mut buffer, port)\n    s.Write(buffer)\n    s.Close()\n    l.Close()\n    c = TcpStream.Connect(\"localhost:{port}\")\n    c.Close()\n}\n\nServe() else {\n    IoError e => print(e)\n}";
        Clean(Program); // the positive control
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors(Program.Replace("TcpListener.Bind", "TcpListener.Listen", StringComparison.Ordinal)));
        Assert.Equal([(ErrorIds.UnknownName, 4)], Errors(Program.Replace("l.Accept()", "l.Read()", StringComparison.Ordinal)));
        Assert.Equal([(ErrorIds.UnknownName, 8)], Errors(Program.Replace("s.Close()", "s.Accept()", StringComparison.Ordinal)));
        Assert.Equal([(ErrorIds.UnknownName, 11)], Errors(Program.Replace("c.Close()", "print(c.Port)", StringComparison.Ordinal)));
        Assert.Equal([(ErrorIds.TypeMismatch, 10)], Errors(Program.Replace("TcpStream.Connect(\"localhost:{port}\")", "TcpStream.Connect(port)", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_read_takes_its_list_as_mut_and_clear_changes_a_mut_list()
    {
        // S3 with M7: the list a read adds to is a `mut` argument, written so at the call
        const string Program = "void | IoError Use() {\n    f = File.Open(\"a\")\n    mut buffer = Bytes()\n    print(f.Read(mut buffer, 4))\n}\n\nUse() else {\n    IoError e => print(e)\n}";
        Clean(Program); // the positive control
        Assert.Equal([(ErrorIds.MutAtCall, 4)], Errors(Program.Replace("mut buffer, 4", "buffer, 4", StringComparison.Ordinal)));

        // M9 with M5: `Clear` changes the list it is called on
        Clean("mut xs = List<int>()\nxs.Clear()\nprint(xs.Count)");
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("xs = List<int>()\nxs.Clear()\nprint(xs.Count)"));
    }

    [Fact]
    public void An_enum_value_is_a_constant_and_a_copied_value()
    {
        // F12 with D12: a default can be a value of an enum (section 4, an assumption there)
        Clean("enum Plan { Free, Pro }\n\nvoid Show(Plan p = Plan.Pro) {\n    print(p)\n}\n\nShow()");

        // M7: a read of an enum value that a `mut` argument changes in the same expression is a copy, as a number's is; a string's is the error
        const string Program = "data Two(T A, int B)\n\nint Change(mut T p) {\n    p = V\n    return 1\n}\n\nmut q = W\nprint(Two(q, Change(mut q)))";
        Clean("enum Plan { Free, Pro }\n" + Program.Replace("T ", "Plan ", StringComparison.Ordinal).Replace("V", "Plan.Pro", StringComparison.Ordinal).Replace("W", "Plan.Free", StringComparison.Ordinal));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors(Program.Replace("T ", "string ", StringComparison.Ordinal).Replace("V", "\"b\"", StringComparison.Ordinal).Replace("W", "\"a\"", StringComparison.Ordinal)));
    }

    // ---- null (chapter 4) --------------------------------------------------------------------------------------

    /// <summary>A function of a nullable result, on lines 1 to 7: a program after it starts on line 8.</summary>
    private const string Email = "string? Email(int id) {\n    if id == 1 {\n        return \"a\"\n    }\n    return null\n}\n\n";

    [Fact]
    public void A_nullable_type_is_its_type_or_null_and_a_call_of_one_is_a_value_of_it()
    {
        // N1, N8: a call's nullable result is the value, not the success of an outcome list; N4: `??` gives the type it makes nullable
        // where its right side fits that, else the nullable type
        Assert.Equal("string?", TypeOf(Email + "e = Email(1)\nprint(e)", "e"));
        Assert.Equal("string", TypeOf(Email + "e = Email(1) ?? \"none\"\nprint(e)", "e"));
        Assert.Equal("string?", TypeOf(Email + "e = Email(1) ?? Email(2)\nprint(e)", "e"));
    }

    [Theory]
    [InlineData("x = null\nprint(x)", 1)] // no type to take
    [InlineData("int x = null\nprint(x)", 1)] // a type without `null`
    [InlineData("x = 1\nprint(x == null)", 2)] // a comparison with a value that is never null
    [InlineData("int? x = 1\nprint(x < null)", 2)] // an order, which `null` has none of
    [InlineData("T Id<T>(T x) => x\nprint(Id(null))", 2)] // a type parameter, which a nullable type could stand for
    public void Null_where_no_nullable_type_or_pointer_takes_it_is_a_type_mismatch(string program, int line)
    {
        // N6 (section 4, an assumption there): `null` has no type of its own
        Assert.Equal([(ErrorIds.TypeMismatch, line)], Errors(program));
    }

    [Theory]
    [InlineData("print(n + 1)")]
    [InlineData("print(-n)")]
    [InlineData("int m = n\nprint(m)")]
    [InlineData("if b.Flag {\n    print(1)\n}")]
    [InlineData("for i in b.Items {\n    print(i)\n}")]
    [InlineData("print(b.Items[0])")]
    [InlineData("print(b.Items.Count)")]
    [InlineData("print(b.Name.Bytes.Count)")]
    [InlineData("print(Maybe() with { Name = null })")]
    public void A_nullable_value_where_its_type_is_needed_without_a_check_is_nullable_unchecked(string statement)
    {
        // N2: an operator's operand, a value of the type it makes nullable, a condition, a loop's list, an index's list, a member's
        // value and the value `with` copies
        const string Program = "data Box(List<int>? Items, bool? Flag, string? Name)\n\nint? Find(int id) {\n    return null\n}\n\nBox? Maybe() => null\n\nn = Find(1)\nb = Box(null, null, null)\nprint(n)\nprint(b)\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 13)], Errors(Program + statement));
    }

    [Fact]
    public void A_test_narrows_where_it_holds_and_where_ways_meet_only_what_each_keeps_is_known()
    {
        // N3 as C# reads it (section 4, an assumption there): a test narrows the branch it guards, `!`, `&&` and `||` as they come out,
        // `else` and an early leave the code after them, and a declaration and an assignment of a value that is not null their target
        Clean(Email + "e = Email(1)\nif e != null {\n    print(e.Bytes.Count)\n}");
        Clean(Email + "e = Email(1)\nif e == null {\n    print(0)\n} else {\n    print(e.Bytes.Count)\n}");
        Clean(Email + "e = Email(1)\nif !(e == null) && e.Bytes.Count > 0 {\n    print(e.Bytes.Count)\n}");
        Clean(Email + "e = Email(1)\nif e == null || e.Bytes.Count == 0 {\n    print(0)\n} else {\n    print(e.Bytes.Count)\n}");
        Clean(Email + "void Show(string? e) {\n    if e == null {\n        return\n    }\n    print(e.Bytes.Count)\n}\n\nShow(Email(1))");
        Clean("string? d = \"x\"\nprint(d.Bytes.Count)");
        Clean(Email + "mut f = Email(1)\nif f == null {\n    f = \"x\"\n}\nprint(f.Bytes.Count)");
        Clean("data User(string? Email)\n\nmut u = User(null)\nu.Email = \"b\"\nprint(u.Email.Bytes.Count)");

        // where one way in leaves it nullable, it is: after an `if` whose one branch did not test it, and on `||`'s right side
        Assert.Equal([(ErrorIds.NullableUnchecked, 12)], Errors(Email + "e = Email(1)\nif e == null {\n    print(0)\n}\nprint(e.Bytes.Count)"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 9)], Errors(Email + "e = Email(1)\nif e != null || e.Bytes.Count == 0 {\n    print(0)\n}"));
    }

    [Fact]
    public void A_loop_knows_at_its_head_what_every_round_keeps_and_after_it_what_each_way_out_keeps()
    {
        // N3, N7: a round that leaves it nullable at its end or at a `continue` reads the loop again from there; after the loop holds
        // what a false condition shows, joined with what holds at each `break`
        Clean(Email + "mut e = Email(1)\nwhile e != null {\n    print(e.Bytes.Count)\n    e = Email(2)\n}");
        Assert.Equal([(ErrorIds.NullableUnchecked, 12)], Errors(Email + "mut e = Email(1)\nif e != null {\n    mut i = 0\n    while i < 2 {\n        print(e.Bytes.Count)\n        e = Email(2)\n        i = i + 1\n    }\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 12)], Errors(Email + "mut e = Email(1)\nif e != null {\n    for i in 0..<2 {\n        if i == 1 {\n            print(e.Bytes.Count)\n        }\n        e = Email(2)\n    }\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 15)], Errors(Email + "mut e = Email(1)\nif e != null {\n    for i in 0..<2 {\n        if i == 1 {\n            e = Email(2)\n            continue\n        }\n        print(e.Bytes.Count)\n    }\n}"));
        Clean(Email + "mut e = Email(1)\nwhile true {\n    if e != null {\n        break\n    }\n    e = Email(1)\n}\nprint(e.Bytes.Count)");
        Clean(Email + "mut e = Email(1)\nwhile e == null {\n    e = Email(1)\n}\nprint(e.Bytes.Count)");
        Assert.Equal([(ErrorIds.NullableUnchecked, 15)], Errors(Email + "mut e = Email(1)\nwhile e == null {\n    if Email(2) == null {\n        break\n    }\n    e = Email(1)\n}\nprint(e.Bytes.Count)"));

        // the condition is read again with what its own `mut` argument ended, which every round after the first starts with
        const string Refill = "bool Refill(mut string? t) {\n    t = null\n    return true\n}\n\nmut string? s = \"abc\"\nmut n = 0\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 8)], Errors(Refill + "while s.Bytes.Count > 0 && Refill(mut s) && n < 3 {\n    n = n + 1\n}\nprint(n)"));
        Clean(Refill + "while s != null && s.Bytes.Count > 0 && Refill(mut s) && n < 3 {\n    n = n + 1\n}\nprint(n)");

        // a `for` can run no round, so after it holds what its head holds, joined with each `break`
        Assert.Equal([(ErrorIds.NullableUnchecked, 7)], Errors("mut xs = List<int>()\nmut string? y = null\nfor x in xs {\n    y = \"a\"\n    break\n}\nprint(y.Bytes.Count)"));
        Clean("mut xs = List<int>()\nmut string? y = \"b\"\nfor x in xs {\n    y = \"a\"\n    break\n}\nprint(y.Bytes.Count)");
    }

    [Fact]
    public void An_assignment_or_a_mut_argument_ends_what_was_known_of_its_target_and_of_every_path_through_it()
    {
        // N7: an assignment of a nullable value and a `mut` argument end it for the path and every path through it
        const string Reset = "void Reset(mut string? s) {\n    s = null\n}\n\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 11)], Errors(Email + "mut e = Email(1)\nif e != null {\n    e = Email(2)\n    print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 15)], Errors(Email + Reset + "mut e = Email(1)\nif e != null {\n    Reset(mut e)\n    print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 6)], Errors("data User(string? Email)\n\nmut u = User(\"a\")\nif u.Email != null {\n    u = User(null)\n    print(u.Email.Bytes.Count)\n}"));

        // the right side of `&&` and `||` runs after the left side's test, so what it changes the test no longer shows on any way out
        const string Cleared = "bool Cleared(mut string? s) {\n    s = null\n    return true\n}\n\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 15)], Errors(Email + Cleared + "mut e = Email(1)\nif e != null && Cleared(mut e) {\n    print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 17)], Errors(Email + Cleared + "mut e = Email(1)\nif e == null || Cleared(mut e) {\n    print(0)\n} else {\n    print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 15)], Errors(Email + Cleared + "mut e = Email(1)\nwhile e != null && Cleared(mut e) {\n    print(e.Bytes.Count)\n}"));
        Clean(Email + Cleared + "mut e = Email(1)\nif Cleared(mut e) && e != null {\n    print(e.Bytes.Count)\n}");
        Clean(Email + Cleared + "mut e = Email(1)\nif e != null && Cleared(mut e) && e != null {\n    print(e.Bytes.Count)\n}");

        // a `mut` argument inside an operand of `??` or of a test ends it too: only a plain read there runs nothing that could change it
        const string Take = "string? Take(mut string? s) {\n    s = null\n    return null\n}\n\nmut string? x = \"abc\"\nif x != null {\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 9)], Errors(Take + "    print(Take(mut x) ?? \"d\")\n    print(x.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 9)], Errors(Take + "    print(Take(mut x) == null)\n    print(x.Bytes.Count)\n}"));
        Clean(Take + "    print(Take(mut x) ?? \"d\")\n    if x != null {\n        print(x.Bytes.Count)\n    }\n}");

        // a `mut` method or a `mut` argument on a path through a nullable variable or field not known not null (M5, M7)
        const string Box = "data Box(int N, List<int> Items)\n\nBox? Find() => null\n\nvoid Bump(mut int n) {\n    n = n + 1\n}\n\nmut b = Find()\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Box + "b.Items.Add(1)"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Box + "Bump(mut b.N)"));

        // a variable passed as `mut` is read as its own type, which a `mut` parameter of the type it makes nullable does not take (M7)
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("void Set(mut string s) {\n    s = \"b\"\n}\n\nmut string? e = \"a\"\nSet(mut e)"));
    }

    [Fact]
    public void Coalescing_runs_its_right_side_only_where_the_left_is_null_and_a_chain_stops_at_the_first_null()
    {
        // N4, N5 as C# reads them: a right side of neither type is the mismatch; `?.` makes the member's type nullable, keeps a nullable
        // member's type, and goes on through the members after it, which read the member before as its own type, so that a nullable
        // one there is N2's; a parenthesis ends the chain; after a test, `?.` and `??` read the nullable type, as a second test does
        const string Find = "data User(string Name, string? Email)\n\nUser? Find(int id) => null\n\n";
        Assert.Equal("string?", TypeOf(Find + "n = Find(1)?.Name\nprint(n)", "n"));
        Assert.Equal("string?", TypeOf(Find + "n = Find(1)?.Email\nprint(n)", "n"));
        Assert.Equal("int?", TypeOf(Find + "n = Find(1)?.Name.Bytes.Count\nprint(n)", "n"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 5)], Errors(Find + "print(Find(1)?.Email.Bytes.Count)"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 5)], Errors(Find + "print((Find(1)?.Name).Bytes.Count)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors(Find + "print(Find(1)?.Name ?? 1)"));
        Clean(Find + "u = Find(1)\nif u != null {\n    print(u?.Name ?? u.Name)\n}");

        // N3: a member after `?.` reads the member before as a test showed that member's path, as C#'s analysis narrows it
        // (compiler/README.md), and as the path itself reads; where the test shows nothing of the path, where an assignment ended it
        // (N7), and where the test is of another variable's path, the nullable member is N2's
        Clean(Find + "u = Find(1)\nif u?.Email != null {\n    print(u?.Email.Bytes.Count)\n}");
        Clean(Find + "u = Find(1)\nif u?.Email != null {\n    print(u.Email.Bytes.Count)\n}");
        Assert.Equal([(ErrorIds.NullableUnchecked, 7)], Errors(Find + "u = Find(1)\nif u?.Email == null {\n    print(u?.Email.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 8)], Errors(Find + "mut u = Find(1)\nif u?.Email != null {\n    u = Find(2)\n    print(u?.Email.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 8)], Errors(Find + "u = Find(1)\nv = Find(2)\nif v?.Email != null {\n    print(u?.Email.Bytes.Count)\n}"));
    }

    [Theory]
    [InlineData("x = 1\nprint(x ?? 2)", 2)]
    [InlineData("data User(string Name)\n\nu = User(\"a\")\nprint(u?.Name)", 4)]
    [InlineData("xs = List<int>()\nprint(xs?[0])", 2)]
    public void Coalescing_or_a_chain_on_a_value_that_is_not_nullable_is_a_type_mismatch(string program, int line)
    {
        // section 4, the owner, 2026-10-30, "Error, as Rust": `??` and `?.` on a value whose type has no `null`, and `?[` with them (an
        // assumption there)
        Assert.Equal([(ErrorIds.TypeMismatch, line)], Errors(program));
    }

    [Fact]
    public void A_conditional_index_reads_the_item_of_a_nullable_list_and_goes_on_in_the_chain()
    {
        // N5 with section 4, the owner, 2026-10-30, "Add, as C#": `xs?[i]` is the item's type made nullable, or the item's own where it is
        // nullable; a member after it reads the item as its own type; the index is an integer; a string is indexed through its bytes
        const string Lists = "data User(string Name, string? Email)\n\nList<int>? Numbers() => null\nList<User>? Users() => null\nList<string?>? Names() => null\n\n";
        Assert.Equal("int?", TypeOf(Lists + "n = Numbers()?[0]\nprint(n)", "n"));
        Assert.Equal("string?", TypeOf(Lists + "n = Names()?[0]\nprint(n)", "n"));
        Assert.Equal("string?", TypeOf(Lists + "n = Users()?[0].Name\nprint(n)", "n"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 7)], Errors(Lists + "print(Users()?[0].Email.Bytes.Count)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 7)], Errors(Lists + "print(Numbers()?[\"a\"])"));
        Assert.Equal([(ErrorIds.StringIndex, 2)], Errors("string? s = \"a\"\nprint(s?[0])"));
        Clean(Lists + "xs = Numbers()\nif xs != null {\n    print(xs?[0] ?? xs[0])\n}");

        // the list is read before its index runs and held without a count, so an index that changes it is refused (M7)
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 8)], Errors("int Pop(mut List<string>? xs) {\n    xs = null\n    return 0\n}\n\nmut List<string>? names = List<string>()\nnames.Add(\"a\")\nprint(names?[Pop(mut names)])"));
    }

    [Fact]
    public void A_part_of_a_nullable_list_and_a_nullable_index_are_nullable_unchecked()
    {
        // N2: a part of a nullable list, a nullable index of a list and the nullable index of `?[`, as an index of a nullable list is
        const string Lists = "int? Find(int id) => null\nList<int>? Numbers() => null\n\nxs = List<int>()\nn = Find(1)\nys = Numbers()\nprint(xs.Count)\nprint(n)\nprint(ys)\n";
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Lists + "print(ys[0..<1])"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Lists + "print(xs[n])"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Lists + "print(ys?[n])"));
        Clean(Lists + "if n != null && ys != null {\n    print(ys[0..<1])\n    print(xs[n])\n    print(ys?[n])\n}");
    }

    [Fact]
    public void Equality_with_a_nullable_value_is_lifted_and_an_order_is_not()
    {
        // section 4, the owner, 2026-10-30, "Lift == and !=": a nullable side compares with a value or another nullable value of a type
        // its own compares with; `<` stays N2's; a side equal to a value that is not nullable is known not null where the test holds, as
        // C#'s analysis reads it, and one equal to a nullable value is not
        const string Find = "int? Find(int n) => null\n\n";
        Clean(Find + "print(Find(1) == 0)\nprint(Find(1) != Find(2))\nprint(1L == Find(1))");
        Clean(Email + "print(Email(1) == \"a\")\nprint(Email(1) != Email(2))");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors(Find + "print(Find(1) == \"a\")"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 3)], Errors(Find + "print(Find(1) < 1)"));
        Clean("byte? Small() => null\nuint? Big() => null\n\nprint(Small() == 200)\nprint(Big() != 5)\nprint(5 == Big())"); // a literal takes the type of the nullable side's value
        Clean(Email + "e = Email(1)\nif e == \"a\" {\n    print(e.Bytes.Count)\n}");
        Clean(Email + "e = Email(1)\nif e != \"a\" {\n    print(0)\n} else {\n    print(e.Bytes.Count)\n}");
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Email + "e = Email(1)\nif e == Email(2) {\n    print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Email + "e = Email(1)\nif e != \"a\" {\n    print(e.Bytes.Count)\n}"));
    }

    [Fact]
    public void A_nullable_type_parameter_filled_with_a_nullable_type_is_that_type()
    {
        // section 4, the owner, 2026-10-30, "Flatten, as C#": `T?` of a `T` that is `int?` is `int?`
        const string First = "T? First<T>(List<T> xs) {\n    if xs.Count == 0 {\n        return null\n    }\n    return xs[0]\n}\n\n";
        Assert.Equal("int?", TypeOf(First + "items = List<int?>()\nn = First(items)\nprint(n)", "n"));
        Assert.Equal("int?", TypeOf(First + "items = List<int>()\nn = First(items)\nprint(n)", "n"));
        Clean("T Or<T>(T? a, T b) => a ?? b\n\nprint(Or(1, 2))");
    }

    [Fact]
    public void A_match_on_a_nullable_value_takes_a_null_arm_for_its_null()
    {
        // N1, C9, O8 (section 4, the owner, 2026-10-30, "Allow, as C#"): an arm may name `null`, which covers the case `null`
        Clean(Email + "match Email(1) {\n    null => print(0)\n    string s => print(s.Bytes.Count)\n}");
        Assert.Equal([(ErrorIds.MatchNotExhaustive, 8)], Errors(Email + "match Email(1) {\n    string s => print(s.Bytes.Count)\n}"));

        // the `null` arm tests the case, as `e == null` does, so a variable known not null is read as its nullable type there (N3)
        Clean(Email + "e = Email(1)\nif e != null {\n    match e {\n        null => print(0)\n        string s => print(s.Bytes.Count)\n    }\n}");
        Clean("mut string? d = \"x\"\nprint(match d {\n    null => 0\n    string s => s.Bytes.Count\n})");
    }

    [Fact]
    public void A_null_arm_over_a_subject_with_no_null_is_an_arm_no_value_reaches()
    {
        // N1 (section 4, the owner, 2026-10-30, "Allow, as C#"): a `null` arm over a `string` or over a union without `null` builds with no
        // diagnostic, as C# compiles `case null` on a `string`; it covers nothing (O8), and a second one is `unreachable-arm`, as on a `T?` (C4)
        Clean("void Show(string s) {\n    match s {\n        null => print(0)\n        string t => print(t.Bytes.Count)\n    }\n}\n\nShow(\"ab\")");
        Clean("int | string x = 1\nprint(match x {\n    null => 0\n    int i => i\n    string s => s.Bytes.Count\n})");
        Assert.Equal([(ErrorIds.MatchNotExhaustive, 2)], Errors("void Show(string s) {\n    match s {\n        null => print(0)\n    }\n}\n\nShow(\"ab\")"));
        Assert.Equal([(ErrorIds.UnreachableArm, 4)], Errors("void Show(string s) {\n    match s {\n        null => print(0)\n        null => print(1)\n        string t => print(t.Bytes.Count)\n    }\n}\n\nShow(\"ab\")"));

        // a subject of a type parameter, which a nullable type can fill (N11), is no subject the answer named: its `null` arm stays
        // `type-mismatch`, as `null` where a `T` is required is (N6), and the case stands open (section 14)
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("void Show<T>(T x) {\n    match x {\n        null => print(0)\n        else => print(1)\n    }\n}\n\nShow(1)"));
    }

    [Fact]
    public void An_arm_no_null_reaches_knows_the_subject_not_null()
    {
        // N3 as C# reads a pattern: a type's arm, a literal's and an `else` after the `null` arm know the subject's variable not null,
        // and after the `match` holds what every arm that completes keeps
        Clean(Email + "e = Email(1)\nmatch e {\n    null => print(0)\n    string s => print(e.Bytes.Count)\n}");
        Clean(Email + "e = Email(1)\nmatch e {\n    null => print(0)\n    else => print(e.Bytes.Count)\n}");
        Clean(Email + "void Show(string? e) {\n    match e {\n        null => return\n        string s => print(s)\n    }\n    print(e.Bytes.Count)\n}\n\nShow(Email(1))");

        // the `null` arm, an `else` no `null` arm comes before, the code after a `null` arm that completes, and an arm that changes it
        Assert.Equal([(ErrorIds.NullableUnchecked, 10)], Errors(Email + "e = Email(1)\nmatch e {\n    null => print(e.Bytes.Count)\n    string s => print(s)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 11)], Errors(Email + "e = Email(1)\nmatch e {\n    string s => print(s)\n    else => print(e.Bytes.Count)\n}"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 13)], Errors(Email + "e = Email(1)\nmatch e {\n    null => print(0)\n    string s => print(s)\n}\nprint(e.Bytes.Count)"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 13)], Errors(Email + "mut e = Email(1)\nmatch e {\n    null => print(0)\n    string s => {\n        e = Email(2)\n        print(e.Bytes.Count)\n    }\n}"));
    }

    // ---- maps (M9) ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_map_gives_a_missing_key_as_null_and_takes_an_entry_through_a_mut_variable()
    {
        // M9: `Map<K, V>()` makes a map without entries, `m[k]` is a `V?` that is null for a missing key (N2), and `m[k] = v` changes the
        // variable (M4); a key takes what widens into the key type (T8), as an argument does; a map stands in a parameter, a `mut` one
        // among them, and in a field
        Assert.Equal("Map<string, int>", TypeOf("m = Map<string, int>()\nprint(m)", "m"));
        Assert.Equal("int?", TypeOf("m = Map<string, int>()\nn = m[\"a\"]\nprint(n)", "n"));
        Clean("mut m = Map<long, string>()\nint k = 1\nm[k] = \"a\"\nprint(m[k] ?? \"none\")");
        Clean("void Fill(mut Map<char, bool> m) {\n    m['a'] = true\n}\n\nmut seen = Map<char, bool>()\nFill(mut seen)\nprint(seen)");
        Clean("data Index(Map<string, int> Ids)\n\ni = Index(Map<string, int>())\nprint(i.Ids[\"a\"] ?? 0)");
        Clean("int Bump(mut Map<string, int> t) {\n    t[\"b\"] = 2\n    return 1\n}\n\nmut m = Map<string, int>()\nm[\"a\"] = Bump(mut m)\nprint(m)"); // M4: the value is made before the write

        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("m = Map<string, int>()\nm[\"a\"] = 1"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut m = Map<string, int>()\nm[1] = 1")); // T1: a key
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut m = Map<string, int>()\nm[\"a\"] = \"b\"")); // a value
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("m = Map<string, int>()\nprint(m[1] ?? 0)")); // a key read
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut m = Map<string, int>()\nm[\"a\"] = null")); // N6
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("m = Map<string, int>()\nprint(m[\"a\"] + 1)"));
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("Map<string, int>? m = null\nprint(m[\"a\"] ?? 0)")); // a nullable map, read
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("mut Map<string, int>? m = null\nm[\"a\"] = 1\nprint(m)")); // and written
        Clean("mut Map<string, int>? m = Map<string, int>()\nif m != null {\n    m[\"a\"] = 1\n}\nprint(m)"); // N3: known not null
        Assert.Equal([(ErrorIds.CaptureAssign, 2)], Errors("mut m = Map<string, int>()\nf = (int x) => { m[\"a\"] = x }\nf(1)\nprint(m)")); // F11
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("m = Map<string>()\nprint(m)")); // T17
        Assert.Equal([(ErrorIds.CannotInfer, 1)], Errors("m = Map()\nprint(m)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 1)], Errors("m = Map<string, int>(1)\nprint(m)"));
        Assert.Equal([(ErrorIds.NoText, 4)], Errors("data Rule(() => int Step)\n\nm = Map<string, Rule>()\nprint(m)")); // A7: the text of its values
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("a = Map<string, int>()\nb = Map<string, int>()\nprint(a == b)")); // T18, M9: a map takes no `==`
        Assert.Equal([(ErrorIds.StaticState, 1)], Errors("thread static mut Map<string, int> m = Map<string, int>()\nprint(1)", grants: "raw")); // F12
    }

    [Fact]
    public void A_write_into_a_map_a_call_still_holds_is_changed_while_held()
    {
        // M7: an arm's assignment and a `mut` argument change the map the call's first argument holds, or the map a read reads
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 10)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, Find(2) else {\n    NotFound => {\n        ages[\"a\"] = 1\n        5\n    }\n}))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 9)], Errors("int Fill(mut Map<string, int> m) {\n    m[\"a\"] = 1\n    return 1\n}\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, Fill(mut ages)))"));
        Clean("int Fill(mut Map<string, int> m) {\n    m[\"a\"] = 1\n    return 1\n}\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, 0) + Fill(mut ages))"); // the call around the read has ended
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors("int Fill(mut Map<int, int> t) {\n    t[1] = 1\n    return 1\n}\n\nmut ages = Map<int, int>()\nprint(ages[Fill(mut ages)] ?? 0)")); // a read whose key changes the map
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors("int Fill(mut Map<int, int> t) {\n    t[1] = 1\n    return 1\n}\n\nmut ages = Map<int, int>()\nprint(ages.ContainsKey(Fill(mut ages)))")); // a lookup whose key changes the map
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 2)], Errors("mut ages = Map<int, int>()\nprint(ages[ages.Remove(1) ?? 0] ?? 0)")); // a read whose key removes an entry
        var people = "data NotFound\n\nint | NotFound Find(int id) => NotFound\n\ndata People(Map<string, int> Ages, Map<string, int> Ids)\n\nint Size(Map<string, int> m, int n) => n\n\nmut p = People(Map<string, int>(), Map<string, int>())\n";
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 12)], Errors(people + "print(Size(p.Ages, Find(2) else {\n    NotFound => {\n        p.Ages[\"a\"] = 1\n        5\n    }\n}))")); // a write through a path into the map the call holds
        Clean(people + "print(Size(p.Ids, Find(2) else {\n    NotFound => {\n        p.Ages[\"a\"] = 1\n        5\n    }\n}))"); // two fields of one variable are two values
    }

    [Fact]
    public void A_map_in_a_field_takes_an_entry_through_a_path_from_a_mut_variable()
    {
        // M4, M9: a path of data fields from a `mut` variable reaches the map it writes into, as a `mut` method reaches a list (M5), and
        // changes the variable; no member but a field is a map, and nothing reachable through a variable declared without `mut` changes
        const string box = "data Box(Map<string, int> M, int N)\ndata Outer(Box B)\n\n";
        Clean(box + "mut b = Box(Map<string, int>(), 0)\nb.M[\"a\"] = 1\nprint(b)");
        Clean(box + "mut o = Outer(Box(Map<string, int>(), 0))\no.B.M[\"a\"] = 1\nprint(o)");
        Clean(box + "void Put(mut Box x) {\n    x.M[\"a\"] = 1\n}\n\nmut b = Box(Map<string, int>(), 0)\nPut(mut b)\nprint(b)");
        Clean(box + "int Reset(mut Box x) {\n    x = Box(Map<string, int>(), 1)\n    return 2\n}\n\nmut b = Box(Map<string, int>(), 0)\nb.M[\"a\"] = Reset(mut b)\nprint(b)"); // M4: the value is made before the write
        Assert.Equal([(ErrorIds.AssignImmutable, 5)], Errors(box + "b = Box(Map<string, int>(), 0)\nb.M[\"a\"] = 1"));
        Assert.Equal([(ErrorIds.CaptureAssign, 5)], Errors(box + "mut b = Box(Map<string, int>(), 0)\nf = (int x) => { b.M[\"a\"] = x }\nf(1)\nprint(b)")); // F11
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors(box + "mut b = Box(Map<string, int>(), 0)\nb.M[1] = 1")); // T1: a key
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors(box + "mut b = Box(Map<string, int>(), 0)\nb.M[\"a\"] = \"b\"")); // a value
        Assert.Equal([(ErrorIds.NullableUnchecked, 7)], Errors(box + "Box? Find() => null\n\nmut b = Find()\nb.M[\"a\"] = 1")); // N2, where the path is read, and nothing more
        Clean("data Pocket(Map<string, int>? M)\n\nmut p = Pocket(Map<string, int>())\nif p.M != null {\n    p.M[\"a\"] = 1\n}\nprint(p)"); // N3: the last field known not null
        Assert.Equal([(ErrorIds.NullableUnchecked, 4)], Errors("data Pocket(Map<string, int>? M)\n\nmut p = Pocket(Map<string, int>())\np.M[\"a\"] = 1")); // and not known
    }

    [Fact]
    public void A_map_s_key_is_any_type_that_compares_by_content()
    {
        // M9: a key is a value of a type `==` compares by content (section 4, the owner, 2026-10-30, "Any type with =="): an enum, a time,
        // a `data` value of such fields, a field of a union of them among them (D3), a list of such items, a type that holds itself
        // through a list; a map, a function value and a file take no `==` (T18), so they are no key
        Clean("enum Tier { Free, Pro }\n\nmut m = Map<Tier, int>()\nm[Tier.Pro] = 1\nprint(m[Tier.Pro] ?? 0)");
        Clean("data P(int X, string Name)\n\nmut m = Map<P, int>()\nm[P(1, \"a\")] = 1\nprint(m[P(1, \"a\")] ?? 0)");
        Clean("mut m = Map<List<string>, int>()\nmut k = List<string>()\nk.Add(\"a\")\nm[k] = 1\nprint(m[k] ?? 0)");
        Clean("data Tag(string? Note, List<int> Ids)\n\nm = Map<Tag, int>()\nprint(m)"); // a nullable field and a list field
        Clean("data Tree(int V, List<Tree> Kids)\n\nm = Map<Tree, int>()\nprint(m)");
        Clean("m = Map<duration, int>()\nprint(m)");
        Clean("data Pair<T>(T A, T B)\n\nm = Map<Pair<int>, int>()\nprint(m)");
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("m = Map<Map<int, int>, int>()\nprint(m)")); // `==` between two maps
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("m = Map<List<Map<int, int>>, int>()\nprint(m)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("m = Map<() => int, int>()\nprint(m)"));
    }

    [Fact]
    public void A_map_of_nullable_values_reads_a_missing_entry_and_a_null_one_alike()
    {
        // M9 with N1: `T?` of a nullable `T` is `T` (section 4, the owner, 2026-10-30, "Flatten, as C#"), so `m[k]` and `Remove` give `int?`
        Assert.Equal("int?", TypeOf("m = Map<string, int?>()\nn = m[\"a\"]\nprint(n)", "n"));
        Assert.Equal("string?", TypeOf("mut m = Map<int, string?>()\nn = m.Remove(1)\nprint(n)", "n"));
        Clean("mut m = Map<string, int?>()\nm[\"a\"] = null\nm[\"b\"] = 2\nprint(m[\"a\"] ?? 0)");
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("m = Map<string, int?>()\nprint(m[\"a\"] + 1)"));
        // N11: values of a type parameter, whose `T?` is the type that fills it where that type is nullable already
        Clean("T? ValueAt<T>(Map<string, T> m, string k) => m[k]\n\nmut ages = Map<string, int?>()\nprint(ValueAt(ages, \"a\") ?? -1)");
        Assert.Equal([(ErrorIds.NoText, 3)], Errors("void F<T>(T x) {\n    m = Map<string, T>()\n    print(m)\n}\n\nF(1)")); // A7: `T` declares no text
    }

    [Fact]
    public void A_map_has_Count_ContainsKey_Remove_and_Clear()
    {
        // M9: C#'s names and Rust's `Remove` (section 4, the owner, 2026-10-30, "C# names, Rust's Remove"); `Remove` and `Clear` change
        // the map (M5), `ContainsKey` and `Count` read it
        Assert.Equal("int", TypeOf("m = Map<string, int>()\nn = m.Count\nprint(n)", "n"));
        Assert.Equal("bool", TypeOf("m = Map<string, int>()\nb = m.ContainsKey(\"a\")\nprint(b)", "b"));
        Assert.Equal("int?", TypeOf("mut m = Map<string, int>()\nn = m.Remove(\"a\")\nprint(n)", "n"));
        Clean("mut m = Map<string, int>()\nm.Remove(\"a\")\nm.Clear()\nprint(m)");
        Clean("void Empty(mut Map<int, int> t) {\n    t.Clear()\n}\n\nmut m = Map<int, int>()\nEmpty(mut m)\nprint(m)");
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("m = Map<string, int>()\nm.Remove(\"a\")\nprint(m)"));
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("m = Map<string, int>()\nm.Clear()\nprint(m)"));
        Assert.Equal([(ErrorIds.MutRequired, 2)], Errors("void F(Map<int, int> m) {\n    m.Clear()\n}\n\nF(Map<int, int>())"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("m = Map<string, int>()\nprint(m.ContainsKey(1))")); // T1: a key
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut m = Map<string, int>()\nm.Remove(1)\nprint(m)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 2)], Errors("mut m = Map<string, int>()\nm.Remove()\nprint(m)"));
        Assert.Equal([(ErrorIds.ArgumentMismatch, 2)], Errors("mut m = Map<string, int>()\nm.Clear(1)\nprint(m)"));
        Assert.Equal([(ErrorIds.CaptureAssign, 2)], Errors("mut m = Map<string, int>()\nf = () => m.Clear()\nf()\nprint(m)")); // F11
        Assert.Equal([(ErrorIds.UnknownName, 2)], Errors("m = Map<string, int>()\nprint(m.Length)")); // V10
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("Map<string, int>? m = null\nprint(m.Count)")); // N2
    }

    [Fact]
    public void A_mut_method_of_a_map_changes_what_a_call_still_holds()
    {
        // M7: `Remove` and `Clear` change the map they are called on, in an arm's statement and against a `mut` argument of the same call;
        // a read of the map in `Remove`'s own key is a value of its own (M9), which holds nothing of the map, so whether `Add`'s exception
        // holds for another method, which section 14 leaves open, does not arise for it
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 10)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, Find(2) else {\n    NotFound => {\n        ages.Remove(\"a\")\n        5\n    }\n}))"));
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 10)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, Find(2) else {\n    NotFound => {\n        ages.Clear()\n        5\n    }\n}))"));
        Clean("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\nint Size(Map<string, int> m, int n) => n\n\nmut ages = Map<string, int>()\nprint(Size(ages, 0) + (Find(2) else {\n    NotFound => {\n        ages.Clear()\n        5\n    }\n}))"); // the call around the read has ended
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors("string Key(mut Map<string, int> m) {\n    m[\"b\"] = 1\n    return \"a\"\n}\n\nmut ages = Map<string, int>()\nprint(ages.Remove(Key(mut ages)) ?? 0)"));
        Clean("mut names = Map<string, string>()\nnames[\"a\"] = \"b\"\nprint(names.Remove(names[\"a\"] ?? \"\") ?? \"\")"); // a read of the receiver in its own argument
    }

    [Fact]
    public void A_loop_over_a_map_names_its_entry_or_its_key_and_value()
    {
        // C6: `for e in map` gives `e.Key` and `e.Value`, `for k, v in map` names both under any two names (section 4, the owner,
        // 2026-10-30, "Key and Value, as C#"); two names walk nothing but a map
        Clean("m = Map<string, int>()\nfor e in m {\n    string k = e.Key\n    int v = e.Value\n    print(k)\n    print(v)\n}");
        Clean("m = Map<string, List<int>>()\nfor name, items in m {\n    string k = name\n    List<int> v = items\n    print(k)\n    print(v)\n}");
        Clean("mut m = Map<string, int>()\nfor k, v in m {\n    m.Remove(k)\n    m[k] = v + 1\n}\nprint(m)"); // the loop walks the map as it was (C6)
        Clean("m = Map<string, int>()\nfor k, v in m {\n    f = () => v + 1\n    print(f())\n}"); // a name of its own, which a lambda reads (F15)
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nfor a, b in xs {\n    print(a)\n}"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("for a, b in 1..3 {\n    print(a)\n}"));
        Assert.Equal([(ErrorIds.UnknownName, 3)], Errors("m = Map<string, int>()\nfor e in m {\n    print(e.Name)\n}")); // V10
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("m = Map<string, int>()\nfor k, v in m {\n    v = 1\n}"));
        Assert.Equal([(ErrorIds.Redeclared, 2)], Errors("m = Map<string, int>()\nfor k, k in m {\n    print(k)\n}")); // V9
    }

    [Fact]
    public void A_double_or_a_float_key_compares_as_C_s_Equals()
    {
        // M9: a `double` or a `float` key, alone or in a field of a data key, NaN finding NaN and -0.0 finding 0.0 (section 4, the owner,
        // 2026-10-30, "As C#")
        Clean("mut m = Map<double, int>()\nm[1.5] = 1\nprint(m[1.5] ?? 0)");
        Clean("m = Map<float, string>()\nprint(m)");
        Clean("data P(int X, double Y)\n\nmut m = Map<P, int>()\nm[P(1, 0.5)] = 1\nprint(m)");
        Clean("data P(float? Y)\n\nm = Map<List<P>, int>()\nprint(m)"); // a field of a union of a float and null, in a list's items
    }

    [Fact]
    public void Two_lists_compare_by_their_items()
    {
        // M9: `==` and `!=` on two lists of one type, equal counts and equal items in order, each pair by `==` (section 4, the owner,
        // 2026-10-30, "By items, as Rust"); items `==` does not take are `type-mismatch`, and a call that changes a list the other side
        // still holds is `changed-while-held` (M7)
        Clean("xs = List<int>()\nys = List<int>()\nprint(xs == ys)\nprint(xs != ys)");
        Clean("xs = List<List<string>>()\nprint(xs == List<List<string>>())");
        Clean("data P(int X, double Y)\n\nxs = List<P>()\nprint(xs == xs)");
        Clean("xs = List<double>()\nprint(xs == xs)"); // IEEE's `==` on each pair of items, as Rust
        Clean("enum Tier { Free, Pro }\n\nxs = List<Tier>()\nprint(xs == xs)");
        Clean("List<int> Grown(mut List<int> t) {\n    t.Add(9)\n    return t\n}\n\nmut xs = List<int>()\nprint(Grown(mut xs) == xs)"); // the call runs before the read
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("xs = List<int>()\nys = List<long>()\nprint(xs == ys)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nprint(xs == 1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int>()\nprint(xs < xs)")); // no order
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<Map<int, int>>()\nprint(xs == xs)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<() => int>()\nprint(xs == xs)"));
        Clean("xs = List<int | string>()\nprint(xs == xs)"); // items of a union by their case and their value (section 4, "Case and value, as Rust")
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("xs = List<int | () => int>()\nprint(xs == xs)")); // a case `==` does not take
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 7)], Errors("List<int> Grown(mut List<int> t) {\n    t.Add(9)\n    return t\n}\n\nmut xs = List<int>()\nprint(xs == Grown(mut xs))")); // the record's `xs == F(mut xs)`
    }

    [Fact]
    public void A_data_value_that_holds_a_map_a_function_a_file_or_a_socket_takes_no_equality()
    {
        // D3, M9: `==` on a data value that holds a map or a function value, at any depth: a field, a field of a field, an item, a case of
        // a union and a derived type's field, is `type-mismatch` at the `==`, and such a value is no key (section 4, the owner, 2026-10-30,
        // "type-mismatch"); so is one that holds a file or a socket (section 4, the owner, 2026-10-30, "Handle ==": "type-mismatch")
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data Box(Map<string, int> M)\n\na = Box(Map<string, int>())\nb = Box(Map<string, int>())\nprint(a == b)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data Rule((int) => int Apply)\n\nr = Rule(x => x)\nprint(r != r)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data In(List<Map<int, int>> Ms)\ndata Out(int A, In I)\n\no = Out(1, In(List<Map<int, int>>()))\nprint(o == o)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data U(int | () => int V)\n\nu = U(1)\nprint(u == u)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data Shape(int Id)\ndata Tagged(Map<int, int> Tags) : Shape\n\nvoid Same(Shape a, Shape b) {\n    print(a == b)\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data Box(Map<string, int> M)\n\nxs = List<Box>()\nprint(xs == xs)")); // a list of them
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("data Box(Map<string, int> M)\n\nm = Map<Box, int>()\nprint(m)")); // no key
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("data Rule((int) => int Apply)\n\nm = Map<List<Rule>, int>()\nprint(m)"));
        Clean("data Box(List<int> Items)\n\na = Box(List<int>())\nprint(a == a)"); // a list compares by its items
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data H(File F)\n\nvoid Same(H a, H b) {\n    print(a == b)\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data H(List<TcpStream> Ss)\n\nvoid Same(H a, H b) {\n    print(a != b)\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data H(TcpListener? L)\n\nvoid Same(H a, H b) {\n    print(a == b)\n}\n\nprint(1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("data H(File F)\n\nm = Map<H, int>()\nprint(m)")); // no key
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("data H(TcpListener? L)\n\nm = Map<List<H>, int>()\nprint(m)"));

        // each instance of a generic data type is read with its own type arguments: `Box<int>`, read first, does not stand for
        // `Box<Map<int, int>>` or `Box<() => int>`
        Assert.Equal([(ErrorIds.TypeMismatch, 4)], Errors("data Box<T>(T V)\ndata K(Box<int> A, Box<Map<int, int>> B)\n\nm = Map<K, int>()\nprint(m)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data Box<T>(T V)\ndata P(Box<int> A, Box<() => int> B)\n\nvoid Same(P a, P b) {\n    print(a == b)\n}\n\nprint(1)"));
        Clean("data Box<T>(T V)\ndata K(Box<int> A, Box<string> B)\n\nm = Map<K, int>()\nprint(m)"); // both instances are keys
    }

    [Fact]
    public void A_union_compares_by_its_case_and_its_value()
    {
        // section 4, the owner, 2026-10-30, "Case and value, as Rust": `==` on two values of a union compares the case, then the value,
        // alone, as an item and in a field; a union of cases `==` takes is a key, and one with a case `==` does not take is `type-mismatch`,
        // as a value and as a key; a union beside a value of one of its cases is `type-mismatch` on either side, as Rust refuses
        // `Some(5) == 5` (section 4, the owner, 2026-10-30, "Refuse, as Rust")
        Clean("int | string a = 1\nint | string b = \"1\"\nprint(a == b)\nprint(a != b)");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int | string a = 1\nprint(a == 1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int | string a = 1\nprint(\"1\" != a)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int | long a = 1\nprint(a == 1)")); // no widening of the literal into a case (T8)
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("List<int> l = List<int>()\nList<int> | Map<int, int> u = l\nprint(l == u)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("List<int> l = List<int>()\nList<int> | Map<int, int> u = l\nprint(u == l)"));
        Clean("m = Map<int | string, int>()\nprint(m)");
        Clean("m = Map<List<int | string>, int>()\nprint(m)");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("int | () => int a = 1\nprint(a == a)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("m = Map<int | Map<int, int>, int>()\nprint(m)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("int | string a = 1\nbool b = true\nprint(a == b)")); // a `bool` is no case of the union
    }

    [Fact]
    public void A_list_of_double_items_in_a_data_value_or_a_key_compares_as_C_s_Equals()
    {
        // D3, M9: a list of `double` or `float` items in a data value under `==`, or in a key, compares its items as C#'s `Equals`, so that
        // the value equals itself and the key is found again (section 4, the owner, 2026-10-30, "Equals inside data/keys")
        Clean("data P(List<double> Y)\n\na = P(List<double>())\nprint(a == a)");
        Clean("data P(List<List<float>> Y)\n\nm = Map<P, int>()\nprint(m)");
        Clean("m = Map<List<double>, int>()\nprint(m)");
        Clean("data P(List<double> Y)\n\nxs = List<P>()\nprint(xs == xs)");
    }

    [Fact]
    public void A_key_of_a_nullable_type_is_read_and_null_is_one_of_its_keys()
    {
        // M9 with N10: a key of a nullable type of a key's type, `null` one of its keys, compares as `==` compares two nullable values,
        // `null` equal to `null` alone, and a `double?` or a `float?` one's value as a `double` key's (section 4, the owner, 2026-10-30,
        // "As plain double"; for the other types an assumption there); so does a list of such items in a key, and a `data` key that holds one
        Clean("mut m = Map<int?, string>()\nm[null] = \"none\"\nm[1] = \"one\"\nint? k = null\nprint(m[k] ?? \"\")\nprint(m.ContainsKey(null))\nprint(m.Remove(1) ?? \"\")");
        Clean("m = Map<string?, int>()\nprint(m)");
        Clean("m = Map<char?, int>()\nprint(m)");
        Clean("mut m = Map<double?, int>()\nm[null] = 1\nm[0.5] = 2\nprint(m[0.5] ?? 0)");
        Clean("m = Map<float?, int>()\nprint(m)");
        Clean("enum Tier { Free, Pro }\n\nm = Map<Tier?, int>()\nprint(m)");
        Clean("data P(int X)\n\nm = Map<P?, int>()\nprint(m)");
        Clean("m = Map<List<int?>, int>()\nprint(m)");
        Clean("m = Map<List<int>?, int>()\nprint(m)");
        Clean("data P(List<double?> Xs)\n\nm = Map<P, int>()\nprint(m)");
        Clean("mut m = Map<int?, string>()\nm[2] = \"a\"\nfor k, v in m {\n    print(k ?? 0)\n    print(v)\n}");
    }

    [Fact]
    public void A_list_of_nullable_items_compares_each_pair_as_two_nullable_values()
    {
        // M9 with N10: `==` on two lists of nullable items compares each pair as `==` compares two nullable values, `null` equal to `null`
        // alone, a `double?` or a `float?` one's value by IEEE's `==` where the lists stand alone and as C#'s `Equals` in a `data` value
        // (section 4, the owner, 2026-10-30, "As plain double"; for the other types an assumption there)
        Clean("xs = List<int?>()\nprint(xs == xs)\nprint(xs != List<int?>())");
        Clean("xs = List<double?>()\nprint(xs == xs)");
        Clean("xs = List<List<string?>>()\nprint(xs == xs)");
        Clean("data P(int X)\n\nxs = List<P?>()\nprint(xs == xs)");
        Clean("xs = List<List<float>?>()\nprint(xs == xs)");
        Clean("data P(List<double?> Xs)\n\na = P(List<double?>())\nprint(a == a)");
    }

    [Theory]
    [InlineData("m = Map<Map<int, int>?, int>()\nprint(m)", 1)]
    [InlineData("m = Map<File?, int>()\nprint(m)", 1)]
    [InlineData("m = Map<List<Map<int, int>?>, int>()\nprint(m)", 1)]
    [InlineData("xs = List<Map<int, int>?>()\nprint(xs == xs)", 2)]
    [InlineData("xs = List<TcpStream?>()\nprint(xs == xs)", 2)]
    public void A_nullable_key_or_item_of_a_type_eq_does_not_take_is_type_mismatch(string program, int line)
    {
        // M9, T18: the nullable type of a map, a file or a socket compares by nothing, as its type does (section 4, the owner, 2026-10-30,
        // "type-mismatch")
        Assert.Equal([(ErrorIds.TypeMismatch, line)], Errors(program));
    }

    [Fact]
    public void The_entry_of_a_loop_over_a_map_is_a_value_of_Entry()
    {
        // C6: `for e in map` names a value of the built-in data type `Entry<K, V>` with `Key` and `Value`, as C#'s `KeyValuePair` (section 4,
        // the owner, 2026-10-30, "A value, as C#"): it is printed, passed, kept, compared and read in a lambda, and its type is written;
        // a program's own `Entry` is `redeclared`, a reading section 4 marks assumed
        Clean("m = Map<string, int>()\nfor e in m {\n    print(e)\n}");
        Clean("void Show(Entry<string, int> e) {\n    print(e.Key)\n}\n\nm = Map<string, int>()\nfor e in m {\n    Show(e)\n}");
        Clean("m = Map<string, List<int>>()\nmut kept = List<Entry<string, List<int>>>()\nfor e in m {\n    kept.Add(e)\n}\nprint(kept)");
        Clean("m = Map<string, int>()\nfor e in m {\n    f = () => e.Value + 1\n    print(f())\n    print(e == e)\n}");
        Assert.Equal("List<Entry<string, int>>", TypeOf("m = Map<string, int>()\nmut kept = List<Entry<string, int>>()\nfor e in m {\n    kept.Add(e)\n}\nprint(kept)", "kept"));
        Assert.Equal([(ErrorIds.Redeclared, 1)], Errors("data Entry(int X)\n\nprint(1)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("m = Map<string, int>()\nfor e in m {\n    int v = e\n    print(v)\n}"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("m = Map<string, Map<int, int>>()\nfor e in m {\n    print(e == e)\n}")); // D3: it holds a map
    }

    [Fact]
    public void A_program_makes_an_Entry_by_its_name_and_copies_one_by_with()
    {
        // C6, D5: `Entry(k, v)` makes an `Entry<K, V>` and `e with { Value = v }` copies one, as every data type (section 4, the owner,
        // 2026-10-30, "Both, as a data type")
        Clean("x = Entry(\"a\", 1)\nprint(x)\nprint(x.Key)");
        Assert.Equal("Entry<string, int>", TypeOf("x = Entry(\"a\", 1)\nprint(x)", "x"));
        Clean("m = Map<string, int>()\nfor e in m {\n    print(Entry(e.Key, 2))\n    print(e with { Value = 2 })\n}");
        Clean("Entry<string, List<int>> y = Entry(Key: \"b\", Value: List<int>())\nprint(y with { Key = \"c\" })");
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("m = Map<string, int>()\nfor e in m {\n    print(e with { Value = \"two\" })\n}"));
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("Entry<string, int> x = Entry(1, \"a\")\nprint(x)"));
    }

    [Fact]
    public void An_item_of_a_mut_list_is_written_through_its_index()
    {
        // M9, M4: `xs[i] = v` on a `mut` variable or parameter (section 4, the owner, 2026-10-30, "As C# and Rust"), the value first, so a
        // `mut` argument in the value changes the list before the write (M4); an arm's write changes what a call holds (M7)
        Clean("mut xs = List<int>()\nxs.Add(1)\nxs[0] = 2\nprint(xs)");
        Clean("void Set(mut List<string> ys) {\n    ys[0] = \"b\"\n}\n\nmut xs = List<string>()\nxs.Add(\"a\")\nSet(mut xs)\nprint(xs)");
        Clean("mut xs = List<long>()\nxs.Add(1)\nbyte i = 0\nxs[i] = 2\nprint(xs)"); // an index of any integer type, a value that widens (T8)
        Clean("int Grow(mut List<int> ys) {\n    ys.Add(5)\n    return 7\n}\n\nmut xs = List<int>()\nxs.Add(1)\nxs[0] = Grow(mut xs)\nprint(xs)");
        Clean("mut xs = List<List<int>>()\nxs.Add(List<int>())\nxs.Add(List<int>())\nxs[0] = xs[1]\nprint(xs)");
        Assert.Equal([(ErrorIds.AssignImmutable, 3)], Errors("xs = List<int>()\nprint(xs)\nxs[0] = 1"));
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("void Set(List<int> xs) {\n    xs[0] = 1\n}\n\nSet(List<int>())"));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut xs = List<int>()\nxs[0] = \"a\""));
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("mut xs = List<int>()\nxs[\"a\"] = 1")); // T2: an index
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("mut List<int>? xs = null\nxs[0] = 1\nprint(xs)")); // N2
        Assert.Equal([(ErrorIds.NullableUnchecked, 2)], Errors("mut m = Map<string, List<int>>()\nm[\"a\"][0] = 1"));
        Clean("mut List<int>? xs = List<int>()\nif xs != null {\n    xs[0] = 1\n}\nprint(xs)"); // N3: known not null
        Assert.Equal([(ErrorIds.CaptureAssign, 2)], Errors("mut xs = List<int>()\nf = (int x) => { xs[0] = x }\nf(1)\nprint(xs)")); // F11
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 10)], Errors("data NotFound\n\nint | NotFound Find(int id) => NotFound\n\nint Size(List<int> ys, int n) => n\n\nmut xs = List<int>()\nprint(Size(xs, Find(2) else {\n    NotFound => {\n        xs[0] = 1\n        5\n    }\n}))"));
    }

    [Fact]
    public void An_item_of_a_list_in_a_field_is_written_through_a_path_from_a_mut_variable()
    {
        // M4, M9: a path of data fields from a `mut` variable reaches the list whose item it writes, as it reaches a map's entry, and
        // changes the variable; a member that is no field is not assigned through, as in a path assignment
        const string box = "data Box(List<int> Items, int N)\ndata Outer(Box B)\n\n";
        Clean(box + "mut b = Box(List<int>(), 0)\nb.Items.Add(1)\nb.Items[0] = 2\nprint(b)");
        Clean(box + "mut o = Outer(Box(List<int>(), 0))\no.B.Items.Add(1)\no.B.Items[0] = 2\nprint(o)");
        Clean(box + "void Set(mut Box x) {\n    x.Items[0] = 1\n}\n\nmut b = Box(List<int>(), 0)\nb.Items.Add(0)\nSet(mut b)\nprint(b)");
        Assert.Equal([(ErrorIds.AssignImmutable, 5)], Errors(box + "b = Box(List<int>(), 0)\nb.Items[0] = 1"));
        Assert.Equal([(ErrorIds.CaptureAssign, 5)], Errors(box + "mut b = Box(List<int>(), 0)\nf = (int x) => { b.Items[0] = x }\nf(1)\nprint(b)")); // F11
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors(box + "mut b = Box(List<int>(), 0)\nb.Items[0] = \"a\""));
        Assert.Equal([(ErrorIds.AssignImmutable, 2)], Errors("mut s = \"ab\"\ns.Bytes[0] = 65\nprint(s)")); // T15: `Bytes` is no field
        Clean("data Pocket(List<int>? Items)\n\nmut p = Pocket(List<int>())\nif p.Items != null {\n    p.Items[0] = 1\n}\nprint(p)"); // N3: the last field known not null
        Assert.Equal([(ErrorIds.NullableUnchecked, 4)], Errors("data Pocket(List<int>? Items)\n\nmut p = Pocket(List<int>())\np.Items[0] = 1")); // and not known
    }

    // ---- list literals (M11) -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("xs = [1, 2]\nprint(xs)", "List<int>")]
    [InlineData("xs = [1, 2L]\nprint(xs)", "List<long>")]
    [InlineData("xs = [1.5, 2]\nprint(xs)", "List<double>")]
    [InlineData("xs = [\"a\", \"b{1}\"]\nprint(xs)", "List<string>")]
    [InlineData("xs = [[1], [2, 3]]\nprint(xs)", "List<List<int>>")]
    [InlineData("xs = [[1], []]\nprint(xs)", "List<List<int>>")]
    [InlineData("n = 5L\nxs = [n, 1]\nprint(xs)", "List<long>")]
    [InlineData("Bytes xs = [1, 255]\nprint(xs)", "Bytes")]
    [InlineData("List<int>? xs = [1]\nprint(xs)", "List<int>?")]
    [InlineData("List<int?> xs = [1, null]\nprint(xs)", "List<int?>")]
    [InlineData("List<int | string> xs = [1, \"a\"]\nprint(xs)", "List<int | string>")]
    public void A_list_literal_takes_the_list_type_where_one_stands_and_else_its_items_type(string program, string type)
    {
        // M11: the type where one stands (section 4, the owner, 2026-10-30, "[1, 2, 3], as C#"), and where none stands the type its
        // items give, as T27 infers a type parameter ("From its items"): a literal takes what the others fix, literals alone the type a
        // constant of them has, and an inner `[]` the type its sibling gives
        Assert.Equal(type, TypeOf(program, "xs"));
    }

    [Theory]
    [InlineData("x = []\nprint(x)", ErrorIds.CannotInfer, 1)]
    [InlineData("x = [1, \"a\"]\nprint(x)", ErrorIds.CannotInfer, 1)]
    [InlineData("x = [1, null]\nprint(x)", ErrorIds.TypeMismatch, 1)]
    [InlineData("Bytes b = [1, 300]\nprint(b)", ErrorIds.ConstantOverflow, 1)]
    [InlineData("List<int> xs = [1,\n    \"a\"]\nprint(xs)", ErrorIds.TypeMismatch, 2)]
    [InlineData("n = 5L\nList<int> xs = [n]\nprint(xs)", ErrorIds.NarrowingConversion, 2)]
    [InlineData("int x = [1]\nprint(x)", ErrorIds.TypeMismatch, 1)]
    [InlineData("xs = [1]\n[2]\nprint(xs)", ErrorIds.Syntax, 2)]
    [InlineData("void Fill(mut List<int> v) {\n    v.Add(1)\n}\n\nFill(mut [1])", ErrorIds.MutAtCall, 5)]
    public void A_list_literal_that_no_type_reaches_or_whose_items_do_not_fit_is_an_error(string program, string id, int line)
    {
        // M11 with T27 (`cannot-infer`), N6 (`null` with no nullable type to take), L17 (an item literal that does not fit), T1 and T7
        // (an item of another type), F5 (a value on its own is no statement) and M7 (a literal is no variable after `mut`)
        Assert.Equal([(id, line)], Errors(program));
    }

    [Fact]
    public void A_list_literal_argument_takes_its_parameter_s_type()
    {
        // M11: `Take([1, 2])` (the owner, 2026-10-30, "[1, 2, 3], as C#"): the literal is read under its parameter's type, as a number
        // literal is (L17), takes what the other arguments fix and fixes what they leave open (T27), and picks among functions of one
        // name by its items (F13, F14)
        Clean("int Count(Bytes b) => b.Count\nprint(Count([1, 2, 255]))");
        Clean("T First<T>(List<T> items) => items[0]\nprint(First([1, 2]))");
        Clean("T Pick<T>(T a, T c) => a\nBytes bytes = [1]\nprint(Pick(bytes, [2, 255]))\nprint(Pick([4], bytes))");
        Clean("int Apply(List<(int) => int> fs) => fs[0](2)\nprint(Apply([x => x + 1]))");
        Clean("void Show(List<int> v) {\n    print(1)\n}\n\nvoid Show(List<string> v) {\n    print(2)\n}\n\nShow([\"a\"])\nShow([1])");
        Assert.Equal([(ErrorIds.CannotInfer, 2)], Errors("T Pick<T>(T a, T c) => a\nprint(Pick([1], [2L]))"));
        Assert.Equal([(ErrorIds.CannotInfer, 1), (ErrorIds.CannotInfer, 1)], Errors("x = [[], []]\nprint(x)")); // each `[]` its own, and none for the list around them (E1)
        Assert.Equal([(ErrorIds.ConstantOverflow, 2)], Errors("int Count(Bytes b) => b.Count\nprint(Count([1, 256]))"));
    }

    [Fact]
    public void A_list_literal_holds_nothing_of_an_item_while_an_item_after_it_changes_its_variable()
    {
        // M11 with M7: the list counts each item as it is made, as `Add` counts its item, so the `mut` call of an item after it meets nothing
        // the literal holds; a call around the literal holds its own arguments while the literal's items run
        const string grow = "List<int> Grow(mut List<int> list) {\n    list.Add(2)\n    return list\n}\n\n";
        Clean(grow + "mut xs = [1]\nprint([xs, Grow(mut xs), xs])");
        Assert.Equal([(ErrorIds.ChangedWhileHeld, 8)], Errors(grow + "int G(List<int> a, List<List<int>> b) => a.Count + b.Count\nmut xs = [1]\nprint(G(xs, [Grow(mut xs)]))"));
    }

    // ---- what the checker does not read ---------------------------------------------------------------------

    [Theory]
    [InlineData("class Counter(mut int Count)\n\nc = Counter(0)\nprint(c)", "1: a class (chapter 7)")]
    [InlineData("class Account(mut string? Email)\n\nAccount? Load(int id) => null\n\nvoid Touch() {\n}\n\na = Load(1)\nif a?.Email != null {\n    Touch()\n    print(a?.Email.Bytes.Count)\n}", "1: a class (chapter 7)")] // N9: the checker narrows no path through a class instance, and a `weak` path runs through one (R5)
    [InlineData("interface Shape {\n    int Area()\n}", "1: an interface (K6, K10)")]
    [InlineData("flags Access { Read, Write }\n\nprint(Access.Read)", "1: a flags type (D13)")]
    [InlineData("data R(int | string V)\n\nR? G() => null\n\nprint(G()?.V)", "5: `?.` to a `int | string`, which no `?` makes nullable (N1, N5)")]
    [InlineData("data User(List<int> Items)\n\nUser? Find() => null\n\nprint(Find()?.Items[0])", "5: an index after `?.` or `?[` (N5)")]
    [InlineData("List<int>? Find() => null\n\nprint(Find()?[0][1])", "3: an index after `?.` or `?[` (N5)")]
    [InlineData("List<int>? Find() => null\n\nprint(Find()?[0..<1])", "3: a part after `?[` (N5, M9)")]
    [InlineData("List<int | string>? Find() => null\n\nprint(Find()?[0])", "3: `?[` to a `int | string`, which no `?` makes nullable (N1, N5)")]
    [InlineData("mut List<int>? xs = null\nxs?[0] = 1", "2: an assignment through `?[` (N5)")]
    [InlineData("data User(List<int> Items)\n\nmut User? u = null\nu?.Items.Clear()", "4: `?.` (N5)")]
    [InlineData("int? | string x = 1\nprint(x)", "1: a nullable case of a union, `int?` (N1, D9)")]
    [InlineData("void? F() {\n}\n\nprint(1)", "1: a nullable `void` (N1)")]
    [InlineData("data In(int X)\ndata Out(In? I)\n\nmut o = Out(In(1))\nif o.I != null {\n    o.I.X = 2\n}", "6: an assignment of a field through a nullable variable or field (N3, M4)")]
    [InlineData("Map<string, int>? m = null\nprint(m?[\"a\"] ?? -1)", "2: `?[` on a map (N5, M9)")]
    [InlineData("m = Map<decimal?, int>()\nprint(m)", "1: a map with keys of type `decimal?`: the hash of a `decimal`, which reads 1.5 and 1.50 as one value, is not built (M9)")]
    [InlineData("void F<T>(T x) {\n    xs = List<T?>()\n    print(xs == xs)\n}\n\nF(1)", "3: `==` on a `List<T?>`: its items: `==` on a `T` is not read (D3, M9)")]
    [InlineData("m = Map<decimal, int>()\nprint(m)", "1: a map with keys of type `decimal`: the hash of a `decimal`, which reads 1.5 and 1.50 as one value, is not built (M9)")]
    [InlineData("void F<T>(T x) {\n    m = Map<T, int>()\n    print(m)\n}\n\nF(1)", "2: a map with keys of type `T`: a `T` is no key the checker reads (M9)")]
    [InlineData("data Base\ndata Derived<T>(T V) : Base\n\nvoid Same(Base a, Base b) {\n    print(a == b)\n}\n\nprint(1)", "5: `==` on a `Base`: its derived type `Derived`: its field `V`: `==` on a `T` is not read (D3, M9)")]
    [InlineData("data Base\ndata Derived<T>(T V) : Base\n\nmut m = Map<Base, int>()\nprint(m.Count)", "4: a map with keys of type `Base`: its derived type `Derived`: its field `V`: a `T` is no key the checker reads (M9)")]
    [InlineData("m = Map<string, () => int>()\nprint(1)", "1: a map with values of type `() => int` (M9, N1)")]
    [InlineData("m = Map<string, int | string>()\nprint(1)", "1: a map with values of type `int | string` (M9, N1)")]
    [InlineData("data Box(Map<string, int> M)\n\nmut bs = List<Box>()\nbs[0].M[\"a\"] = 1", "4: an assignment into a map reached through an item or a call, or into a path that goes on after the key (M4, M9)")]
    [InlineData("Map<string, int> Make() => Map<string, int>()\n\nMake()[\"a\"] = 1", "3: an assignment into a map reached through an item or a call, or into a path that goes on after the key (M4, M9)")]
    [InlineData("data P(int X)\n\nmut m = Map<string, P>()\nm[\"a\"].X = 1", "4: an assignment into a map reached through an item or a call, or into a path that goes on after the key (M4, M9)")]
    [InlineData("data Box(Map<string, int> M)\n\nmut Box? b = Box(Map<string, int>())\nb.M[\"a\"] = 1", "4: an assignment into a path through a nullable field (N3, M4)")]
    [InlineData("data In(Map<string, int> M)\ndata Out(In? I)\n\nmut o = Out(In(Map<string, int>()))\nif o.I != null {\n    o.I.M[\"a\"] = 1\n}", "6: an assignment into a path through a nullable field (N3, M4)")]
    [InlineData("data Box(List<int> Items)\n\nmut Box? b = Box(List<int>())\nb.Items.Add(1)", "4: a `mut` method on a path through a nullable variable or field (N3, M5)")]
    [InlineData("data In(List<int> Items)\ndata Out(In? I)\n\nmut o = Out(In(List<int>()))\nif o.I != null {\n    o.I.Items.Clear()\n}", "6: a `mut` method on a path through a nullable variable or field (N3, M5)")]
    [InlineData("data In(int X)\ndata Out(In? I)\n\nvoid Bump(mut int n) {\n    n = n + 1\n}\n\nmut o = Out(In(1))\nif o.I != null {\n    Bump(mut o.I.X)\n}", "10: a path through a nullable variable or field passed as `mut` (N3, M7)")]
    [InlineData("use tools\nprint(1)", "1: use (F10)")]
    [InlineData("data Counter(int Count) {\n    pub mut void Increment() {\n        Count = Count + 1\n    }\n}\n\nc = Counter(0)\nprint(c.Count)", "2: the body of a data type (D16)")]
    [InlineData("print(string(1))", "1: a conversion to `string` (A5 to A7)")]
    [InlineData("data Box(List<int> Items)\n\nmut bs = List<Box>()\nbs[0].Items[0] = 1", "4: an assignment into a list reached through an item or a call, or into a path that goes on after the item (M4, M9)")]
    [InlineData("data In(List<int> Items)\ndata Out(In? I)\n\nmut o = Out(In(List<int>()))\nif o.I != null {\n    o.I.Items[0] = 1\n}", "6: an assignment into a path through a nullable field (N3, M4)")]
    [InlineData("data P(int X)\n\nmut ps = List<P>()\nps[0].X = 1", "4: an assignment into a list reached through an item or a call, or into a path that goes on after the item (M4, M9)")]
    [InlineData("mut grid = List<List<int>>()\ngrid[0][0] = 1", "2: an assignment into a list reached through an item or a call, or into a path that goes on after the item (M4, M9)")]
    [InlineData("mut grid = List<List<int>>()\ngrid[0].Add(1)", "2: a `mut` method on an element of a list (M5, M9)")]
    [InlineData("data Shape\n\nint AreaOf<T: Shape>(T item) => 1\n\nprint(AreaOf(5))", "3: the limit of a type parameter (T19)")]
    [InlineData("data Shape\ndata Box<T: Shape>(T Item)\n\nprint(1)", "2: the limit of a type parameter (T19)")]
    [InlineData("data Box<T>(T Item)\ndata IntBox(int Extra) : Box<int>\n\nprint(1)", "2: a generic data type as a base (T19)")]
    [InlineData("data Box<T>(T Item)\ndata IntBox(int Extra) : Box<int>\n\nb = IntBox(1, 2)\nprint(b.Item)", "2: a generic data type as a base (T19)")]
    [InlineData("uint u = 1\nprint(-u)", "2: `-` on a `uint`, which no rule gives a meaning")]
    [InlineData("ulong u = 1\nprint(-u)", "2: `-` on a `ulong`, which no rule gives a meaning")]
    [InlineData("void Bump(mut int n) {\n    n = n + 1\n}\n\nmut xs = List<short>()\nBump(mut xs[0])", "6: an element of a list passed as `mut` (M7, M9)")]
    [InlineData("data Counter(int Count) {\n    pub mut void Increment() {\n        Count = Count + 1\n    }\n}\n\nprint(Counter.Increment)", "2: the body of a data type (D16)")]
    [InlineData("data Counter(int Count) {\n    pub static Counter Zero() => Counter(0)\n}\n\nprint(Counter.Zero().Count)", "2: the body of a data type (D16)")]
    [InlineData("data Counter(int Count) {\n    pub mut void Increment() {\n        Count = Count + 1\n    }\n}\ndata Sub(int X) : Counter\n\nmut s = Sub(1, 2)\ns.Increment()", "2: the body of a data type (D16)")]
    [InlineData("static int Twice(int n) {\n    return \"x\"\n}\n\nprint(Twice(\"a\"))", "1: the modifier `static` of a method (K7, M6)")]
    [InlineData("data User(string Name)\n\nmut u = User(\"a\")\nu?.Name = \"b\"", "4: `?.` (N5)")]
    public void A_construct_of_a_chapter_the_checker_does_not_read_is_not_supported_and_carries_no_error(string program, string first)
    {
        // the helper asserts that no diagnostic is an error: what stands inside the construct is read with its diagnostics dropped, so that
        // the variables it uses count as used and nothing of it is reported (E1)
        Assert.Equal(first, NotSupported(program)[0]);
    }

    [Fact]
    public void A_value_the_checker_could_not_type_carries_no_further_error()
    {
        // E1: one defect, one diagnostic; a class instance's fields and members are its chapter's business; a list, a function or a data type
        // with an error inside it is the error type as a whole, so one unknown element type is one error
        var (diagnostics, _) = Check("class Counter(mut int Count) {\n    pub void Add() {\n        Count = Count + 1\n    }\n}\n\nc = Counter(0)\nc.Count = 2\nc.Add()\nprint(c.Count)");
        Assert.DoesNotContain(diagnostics.Items, d => d.IsError);
        Assert.Single(diagnostics.Items, d => !d.IsError);
        Assert.Equal([(ErrorIds.UnknownName, 1)], Errors("xs = List<Nope>()\nList<int> ys = xs\nprint(ys.Count)"));
    }

    [Fact]
    public void A_union_stands_where_another_is_required_when_its_first_case_goes_to_the_first_and_each_other_case_to_one_after_the_first()
    {
        // D9 with O1 (the owner, 2026-10-21): `A | B` stands where `A | B | C` is required and not where `B | A` is, as an argument too; the
        // first case goes to the first case and each other case to one after the first, by being that type, widening into it (T8) or deriving from
        // it (D6), so a `long | NotFound` takes an `int | NotFound` and a `User | NotFound` no `User | Admin`, whose `Admin` would become a
        // success; a case after the first that is one of the cases after the first goes there, and no other fit is looked for (the owner,
        // 2026-10-21); one that would otherwise go to two cases after the first is type-mismatch, as a value that would go to two cases is
        // (T8 with D9; the record's reading)
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data A\ndata B\n\nA | B x = B\nB | A y = x\nprint(y)"));
        Clean("data A\ndata B\ndata C\n\nA | B x = B\nA | B | C y = x\nprint(y)");
        Assert.Equal([(ErrorIds.TypeMismatch, 9)], Errors("data A\ndata B\n\nvoid F(B | A v) {\n    print(v)\n}\n\nA | B x = A\nF(x)"));
        Clean("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nAdmin | NotFound a = NotFound\nUser | NotFound u = a\nprint(u)");
        Clean("data NotFound\n\nint | NotFound Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\nlong | NotFound r = Find(1)\nprint(r)");
        Clean("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nNotFound | Admin a = NotFound\nNotFound | User u = a\nprint(u)");
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("data User(string Name)\ndata Admin(int Level) : User\ndata NotFound\n\nUser | Admin a = Admin(\"a\", 1)\nUser | NotFound u = a\nprint(u)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 3)], Errors("byte b = 1\nint | byte x = b\nint | short | long y = x\nprint(y)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 7)], Errors("data Person(string Name)\ndata User(int Id) : Person\ndata Admin(int Level) : User\ndata NotFound\n\nNotFound | Admin a = NotFound\nNotFound | User | Person u = a\nprint(u)"));
        Clean("short s = 1\nint | short x = s\nint | short | long y = x\nprint(y)"); // a case after the first that is one there is that case (the owner, 2026-10-21)

        // D9 (the owner, 2026-10-21): the order of the cases after the first makes no other type, so `List<A | B | C>` and `List<A | C | B>` are one
        // type, and two functions of one name that take them do not differ (F12); another first case is another type, so `List<A | B>` and
        // `List<B | A>` are two
        Clean("data A\ndata B\ndata C\n\nA | B | C x = B\nA | C | B y = x\nprint(y)");
        Clean("data A\ndata B\ndata C\n\nvoid F(List<A | B | C> xs) {\n    print(xs.Count)\n}\n\nF(List<A | C | B>())");
        Assert.Equal([(ErrorIds.DuplicateFunction, 8)], Errors("data A\ndata B\ndata C\n\nvoid F(A | B | C v) {\n    print(v)\n}\nvoid F(A | C | B v) {\n    print(v)\n}\n\nF(A)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 8)], Errors("data A\ndata B\n\nvoid F(List<A | B> xs) {\n    print(xs.Count)\n}\n\nF(List<B | A>())"));
    }

    [Fact]
    public void A_union_stands_only_where_a_union_is_required()
    {
        // D9: a union is one of its cases only after a match (C4), so a value of one stands where no union is required in no other way, even
        // where every case would: a `byte | short` is no `int`, and a union of two derived types no value of their base (D6)
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("void Show(byte | short v) {\n    int i = v\n    print(i)\n}\n\nbyte b = 1\nShow(b)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 8)], Errors("data User(string Name)\ndata Admin(int Level) : User\ndata Guest(int N) : User\n\nstring NameOf(User u) => u.Name\n\nvoid Show(Admin | Guest v) {\n    print(NameOf(v))\n}\n\nShow(Guest(\"a\", 1))"));
    }

    [Fact]
    public void A_value_goes_into_one_case_of_a_union_and_one_that_fits_two_is_type_mismatch()
    {
        // T8 with D9 (the owner, 2026-10-21): a value that is no case stands where a union is required when it widens into exactly one of its
        // cases, one that would widen into two is type-mismatch, as an argument too, and a conversion picks the case; a data value that
        // derives from two cases is type-mismatch as well (D6); a value that is a case, a data value among them, is that case, and no other
        // fit by widening or derivation is looked for (the owner, 2026-10-21)
        Clean("byte b = 1\nint | string v = b\nprint(v)");
        Assert.Equal([(ErrorIds.TypeMismatch, 2)], Errors("byte b = 1\nshort | int v = b\nprint(v)"));
        Assert.Equal([(ErrorIds.TypeMismatch, 6)], Errors("void Show(short | int v) {\n    print(v)\n}\n\nbyte b = 1\nShow(b)"));
        Clean("byte b = 1\nshort | int v = short(b)\nprint(v)");
        Clean("int i = 1\nshort | int | long v = i\nprint(v)");
        Assert.Equal([(ErrorIds.TypeMismatch, 5)], Errors("data Person(string Name)\ndata User(int Id) : Person\ndata Admin(int Level) : User\n\nUser | Person v = Admin(\"a\", 1, 2)\nprint(v)"));
        Clean("data Person(string Name)\ndata User(int Id) : Person\ndata Admin(int Level) : User\n\nUser | Person v = User(\"a\", 1)\nprint(v)");
        Clean("data Person(string Name)\ndata User(int Id) : Person\ndata Admin(int Level) : User\n\nUser u = Admin(\"a\", 1, 2)\nUser | Person v = u\nprint(v)");

        // L17 with T8 and D9 (the owner, 2026-10-21): a union with exactly one number case reaches a number literal, a time case being no number
        // case; with two or more the literal has its own type, so one whose cases it widens into twice is type-mismatch, and so is one that
        // widens into neither; a suffix, a fraction or a conversion picks the case
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("long | double x = 5\nprint(x)"));
        Clean("long | double x = 5L\nprint(x)");
        Clean("long | double x = 5.0\nprint(x)");
        Clean("byte | string x = 5\nprint(x)");
        Clean("byte | duration x = 5\nprint(x)");
        Clean("float | duration x = 1.5\nprint(x)");
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("byte | short x = 5\nprint(x)"));
        Clean("byte | short x = byte(5)\nprint(x)");

        // L17 with T8, D9 and T29 (the owner, 2026-10-21): a union with exactly one time case reaches a duration literal with that case, whatever
        // number cases it has, as a written `duration` does, so one that does not fit it is constant-overflow, not the type-mismatch of the
        // literal's own type, a `longduration`; with two or more time cases the literal keeps its own type
        Assert.Equal([(ErrorIds.ConstantOverflow, 1)], Errors("duration | string d = 106752days\nprint(d)"));
        Assert.Equal([(ErrorIds.ConstantOverflow, 1)], Errors("int | duration d = 106752days\nprint(d)"));
        Clean("int | duration d = 5days\nprint(d)");
        Clean("duration | longduration d = 106752days\nprint(d)");
        Assert.Equal([(ErrorIds.TypeMismatch, 1)], Errors("duration | timestamp d = 106752days\nprint(d)"));
        Assert.Equal([(ErrorIds.ConstantOverflow, 1)], Errors("duration d = 106752days\nprint(d)"));
    }

    [Fact]
    public void A_type_built_around_the_error_type_is_the_error_type()
    {
        // E1: a type parameter that only an argument without a type fixed is the error type, and so is a list of it, so the call's result
        // carries nothing more where a list is required
        Assert.Equal([(ErrorIds.UnknownName, 7)], Errors("List<T> Wrap<T>(T item) {\n    mut xs = List<T>()\n    xs.Add(item)\n    return xs\n}\n\nList<int> ys = Wrap(nope)\nprint(ys.Count)"));
    }
}
