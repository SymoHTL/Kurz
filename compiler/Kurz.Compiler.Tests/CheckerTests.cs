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

    /// <summary>What the checker does not read, as <c>line: construct</c>; the helper asserts that no diagnostic of the program is an error.</summary>
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
        Assert.Equal("List<byte>", TypeOf("s = \"abc\"\nb = s.Bytes\nprint(b.Count)", "b"));
        Assert.Equal("List<char>", TypeOf("s = \"abc\"\nc = s.Chars\nprint(c.Count)", "c"));
        Assert.Equal("char", TypeOf("c = \"abc\".Chars[0]\nprint(c)", "c"));
        Assert.Equal("byte", TypeOf("b = \"abc\".Bytes[0]\nprint(b)", "b"));
        Clean("c = 'a'\nprint(c == 'a')\nd = 'b'\nprint(d == c)");
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
    public void A_static_declaration_at_the_top_level_is_not_read_and_one_inside_a_block_has_no_meaning()
    {
        // R13: a `static mut` at the outermost level of a file is chapter 11's state outside a function, which the checker does not read;
        // the uses of its name carry no further error (E1). Inside a block or a function a `static` has no meaning on a variable (V2, V4)
        Assert.Equal(["1: a `static mut` declaration at the top level of a file (R13)"], NotSupported("static mut long total = 0\nprint(total)"));
        Assert.Equal(["2: a `static mut` declaration at the top level of a file (R13)"], NotSupported("print(1)\nstatic mut long total = 0\ntotal = 2"));
        Assert.Equal(["1: a `static mut` declaration at the top level of a file (R13)"], NotSupported("static mut long total = 0\n\nvoid Add() {\n    total = total + 1\n}\n\nAdd()")); // a use inside a function, outside `raw`
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("if true {\n    static mut long total = 0\n    print(total)\n}"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("void F() {\n    static mut long total = 0\n    print(total)\n}\n\nF()"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("static long total = 0\nprint(total)")); // R13 states `static mut` alone
        // its initial value is read, so that a variable it uses counts as used (E1)
        Assert.Equal(["2: a `static mut` declaration at the top level of a file (R13)"], NotSupported("long n = 5\nstatic mut long total = n"));
        // the state is visible in every function, so a parameter of its name is `redeclared` (V9), and a write in that function goes to the
        // parameter, as a read does (V8)
        var (diagnostics, _) = Check("static mut long total = 0\n\nvoid F(long total) {\n    total = 2\n}\n\nF(1)");
        Assert.Equal([(ErrorIds.Redeclared, 3), (ErrorIds.AssignImmutable, 4)], diagnostics.Sorted.Where(d => d.IsError).Select(d => (d.Id, d.Line)).ToList());
    }

    [Fact]
    public void A_conversion_of_a_pointer_and_the_size_of_a_type_are_not_read()
    {
        // R14: `Ptr<T>(x)` and `SizeOf<T>()` take the call form of T10's conversions, which the checker does not read for a pointer;
        // the pointer a conversion gives carries no further error (E1), through an offset and a conversion back among them
        Assert.Equal(["2: `Ptr` (R14)"], NotSupported("long m = 64\np = Ptr<long>(m)\nprint(long(p + 3) - long(p))"));
        Assert.Equal(["1: `SizeOf` (R14)"], NotSupported("print(SizeOf<string>())"));
        // another count of type arguments or arguments is no form of R14, `syntax` (E4)
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(SizeOf())"));
        Assert.Equal([(ErrorIds.Syntax, 1)], Errors("print(SizeOf<long>(1))"));
        Assert.Equal([(ErrorIds.Syntax, 2)], Errors("long m = 64\nprint(long(Ptr(m)))"));
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
        Assert.Equal([(ErrorIds.TypeMismatch, 13)], Errors("data NotFound\ndata Invalid\ndata Other\n\nint | NotFound | Invalid Find(int id) {\n    if id == 1 {\n        return 10\n    }\n    return NotFound\n}\n\nvalue = Find(2) else {\n    Other => 0\n    NotFound => 0\n}\nprint(value)")); // E1: which case the wrong arm meant is unknown, so `Invalid` is not reported beside it
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
    [InlineData("print(string(1))", "1: a conversion to `string` (A5 to A7)")]
    [InlineData("mut xs = List<int>()\nxs[0] = 1", "2: an assignment into an index (M9)")]
    [InlineData("data P(int X)\n\nmut ps = List<P>()\nps[0].X = 1", "4: an assignment into an index (M9)")]
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
    [InlineData("x = 1\nraw {\n    y = x + \"a\"\n}", "2: a raw block (R7)")]
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
