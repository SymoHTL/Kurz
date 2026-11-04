using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;
using Xunit.Abstractions;

namespace Kurz.Compiler.Tests;

/// <summary>
/// What the checker records for a code generator (<see cref="SemanticModel"/>): on every corpus case
/// the front end and the checker read without a diagnostic, an entry for every node a code generator
/// reads, found by a walk of the whole tree; and, program by program, the facts each kind of entry
/// holds: the function a call picks and how its arguments bind, the type a literal takes from the
/// parameter it lands on, what a lambda captures, and what a name means where it is no variable.
/// </summary>
public sealed class SemanticModelTests(ITestOutputHelper output)
{
    /// <summary>At least this many corpus cases are read without a diagnostic; fewer means the walk looked at too few.</summary>
    private const int CaseFloor = 160;

    /// <summary>At least this many entries are looked up over those cases; fewer means the walk stopped reading the trees.</summary>
    private const int EntryFloor = 2900;

    private static (DiagnosticBag Diagnostics, ProgramSyntax Tree, SemanticModel Model) Read(string program, string grants = "")
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        var checker = Checker.Check(tree, diagnostics, grants.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (diagnostics, tree, checker.Model);
    }

    /// <summary>The model of a program the checker reads without a diagnostic.</summary>
    private static (ProgramSyntax Tree, SemanticModel Model) Clean(string program, string grants = "")
    {
        var (diagnostics, tree, model) = Read(program, grants);
        Assert.Empty(diagnostics.Items);
        return (tree, model);
    }

    /// <summary>The corpus cases the front end and the checker read without a diagnostic, with their trees and models.</summary>
    private static IEnumerable<(string File, ProgramSyntax Tree, SemanticModel Model)> CleanCases()
    {
        foreach (var file in CorpusTests.CaseFiles())
        {
            var (header, tree, frontEnd) = CorpusTests.ReadCase(file);
            if (frontEnd.HasErrors)
            {
                continue;
            }

            var diagnostics = new DiagnosticBag();
            var checker = Checker.Check(tree, diagnostics, header.Grants, CorpusTests.StdChecker.Value); // W1: as a build reads it
            if (diagnostics.Items.Count == 0)
            {
                yield return (file, tree, checker.Model);
            }
        }
    }

    public static TheoryData<string> CleanCaseFiles()
    {
        var data = new TheoryData<string>();
        foreach (var (file, _, _) in CleanCases())
        {
            data.Add(file);
        }

        return data;
    }

    [Fact]
    public void The_walk_reads_more_cases_and_entries_than_its_floors()
    {
        var cases = 0;
        var entries = 0;
        foreach (var (_, tree, model) in CleanCases())
        {
            var walk = new Walk(model);
            walk.Program(tree);
            cases++;
            entries += walk.Seen;
        }

        output.WriteLine($"{cases} corpus cases walked, {entries} entries looked up");
        Assert.True(cases >= CaseFloor, $"only {cases} corpus cases are read without a diagnostic");
        Assert.True(entries >= EntryFloor, $"only {entries} entries are looked up");
    }

    [Theory]
    [MemberData(nameof(CleanCaseFiles))]
    public void Every_node_a_code_generator_reads_has_its_entry(string file)
    {
        var (_, tree, model) = CleanCases().Single(c => c.File == file);
        var walk = new Walk(model);
        walk.Program(tree);
        Assert.True(walk.Missing.Count == 0, $"{file}: no entry for\n{string.Join("\n", walk.Missing.Take(20))}");
    }

    [Fact]
    public void The_walk_reads_each_copy_of_a_function_that_reads_its_type_and_the_one_arm_of_its_kind()
    {
        // T33: a code generator reads the copies of such a function, never its own body, and of a match on `T.Kind` the arm the copy's
        // kind takes; a function that passes its type parameter to one is read the same way
        var (tree, model) = Clean("void ShowKind<T>() {\n    match T.Kind {\n        TypeKind.Int => print(\"int\")\n        else => print(\"other\")\n    }\n}\n\nvoid Outer<U>() {\n    ShowKind<U>()\n}\n\nShowKind<int>()\nOuter<bool>()");
        var walk = new Walk(model);
        walk.Program(tree);
        Assert.Empty(walk.Missing);
        Assert.Equal(["ShowKind<int>", "ShowKind<bool>", "Outer<bool>"], walk.Copies);
        Assert.Equal(2, walk.KindArms);
    }

    [Fact]
    public void The_walk_reads_a_copy_for_each_constant_a_call_gives()
    {
        // T35: each constant makes a copy of its own, a default and a constant a copy passes on among them, through two copies too, and a
        // second call with the same constant reads the copy the first made; the name, in a lambda and in a hole, is the constant
        var (tree, model) = Clean("enum Mode { Plain, Loud }\n\nvoid Say<const Mode M>() {\n    print(M)\n}\n\nvoid Twice<T, const Mode M = Mode.Loud>(T value) {\n    Say<M>()\n}\n\n" +
            "void Report<const Mode M>() {\n    f = () => M\n    print(\"{M} {f()}\")\n    Twice<string, M>(\"r\")\n}\n\nSay<Mode.Plain>()\nSay<Mode.Plain>()\nTwice(1)\nTwice<int, Mode.Loud>(2)\nReport<Mode.Plain>()");
        var walk = new Walk(model);
        walk.Program(tree);
        Assert.Empty(walk.Missing);
        Assert.Equal(["Say<Mode.Plain>", "Say<Mode.Loud>", "Twice<int, Mode.Loud>", "Twice<string, Mode.Plain>", "Report<Mode.Plain>"], walk.Copies);

        // in each copy the parameter's name is the constant its call gave, the value's place among the enum's values
        var say = model.CopiesOf(model.FunctionOf(Find<FunctionDeclaration>(tree, f => f.Name == "Say")));
        Assert.Equal([0, 1], say.Select(copy =>
        {
            var names = new List<NameExpression>();
            Nodes.Collect(copy.Declaration!, names);
            return Assert.IsType<EnumConstant>(model.TypeOf(names.Single(n => n.Name == "M")).Constant).Index;
        }));

        // in the lambda and in the hole as well
        var report = Assert.Single(model.CopiesOf(model.FunctionOf(Find<FunctionDeclaration>(tree, f => f.Name == "Report"))));
        var inReport = new List<NameExpression>();
        Nodes.Collect(report.Declaration!, inReport);
        Assert.Equal([0, 0], inReport.Where(n => n.Name == "M").Select(n => Assert.IsType<EnumConstant>(model.TypeOf(n).Constant).Index));
    }

    [Fact]
    public void The_walk_reads_the_constructor_s_call_T_Make_became()
    {
        // T33: a code generator reads the call of the primary constructor in place of `T.Make`, with an argument of its own for each
        // field, typed as that field, never the lambda; a type with no fields gives its value, with no call
        var (tree, model) = Clean("data Mixed(int A, long B, string C)\ndata Empty\n\nT Value<T>() => match T.Kind {\n    TypeKind.String => \"s\"\n    else => 0\n}\n\nT Zero<T>() => T.Make(f => Value<f.Type>())\n\nprint(Zero<Mixed>())\nprint(Zero<Empty>())");
        var walk = new Walk(model);
        walk.Program(tree);
        Assert.Empty(walk.Missing);
        Assert.Equal(["Value<int>", "Value<long>", "Value<string>", "Zero<Mixed>", "Zero<Empty>"], walk.Copies);
        Assert.Equal(2, walk.Made);
        var made = model.CopiesOf(model.FunctionOf(Find<FunctionDeclaration>(tree, f => f.Name == "Zero"))).Select(copy =>
        {
            var calls = new List<CallExpression>();
            Nodes.Collect(copy.Declaration!, calls);
            Assert.True(model.IsMade(Assert.Single(calls, c => c.Callee is MemberAccess { Member: "Make" }), out var construction));
            return construction;
        }).ToList();
        var arguments = made[0]!.Arguments.Select(a => a.Value).ToList();
        Assert.Equal(["int", "long", "string"], arguments.Select(a => model.TypeOf(a).Type.Text));
        Assert.Equal(3, arguments.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Null(made[1]);
    }

    [Fact]
    public void The_walk_reads_each_pass_of_a_loop_over_the_fields_of_a_copy_s_type()
    {
        // T33: a code generator reads a pass for each field in place of the loop's own collection and body, `f.Name` as a constant, and
        // a lambda in a pass captures the value it reads and no `f`, which holds no value
        var (tree, model) = Clean("data Order(string Id, int Count)\n\nvoid Show<T>(T value) {\n    for f in T.Fields {\n        show = () => print(\"{f.Name} {f.Get(value)}\")\n        show()\n    }\n}\n\nShow(Order(\"a\", 1))");
        var walk = new Walk(model);
        walk.Program(tree);
        Assert.Empty(walk.Missing);
        Assert.Equal(["Show<Order>"], walk.Copies);
        Assert.Equal(2, walk.Passes);
        var copy = Assert.Single(model.CopiesOf(model.FunctionOf(Find<FunctionDeclaration>(tree))));
        var loops = new List<ForInStatement>();
        Nodes.Collect(copy.Declaration!, loops);
        var passes = model.PassesOf(Assert.Single(loops))!;
        foreach (var pass in passes)
        {
            var lambdas = new List<LambdaExpression>();
            Nodes.Collect(pass, lambdas);
            Assert.Equal(["value"], model.LambdaOf(Assert.Single(lambdas)).Captures.Select(v => v.Name));
        }
    }

    [Fact]
    public void The_walk_finds_a_node_the_checker_did_not_record()
    {
        // the positive control of the theory: a checked tree with a statement the checker never read, built by hand and put after the others
        var (tree, model) = Clean("x = 4\nprint(x)");
        var stray = new ExpressionStatement(3, new CallExpression(3, new NameExpression(3, "print"), [], [new Argument(3, null, false, new IntegerLiteral(3, new IntegerLiteralValue("1", 10, "")))]));
        var walk = new Walk(model);
        walk.Program(new ProgramSyntax([.. tree.Statements, stray]));
        Assert.Contains(walk.Missing, m => m.StartsWith("3: call", StringComparison.Ordinal));
        Assert.Contains(walk.Missing, m => m.StartsWith("3: IntegerLiteral", StringComparison.Ordinal));

        // an expression of a kind the walk does not list is named as such, and never passes for a literal
        var unknown = new Walk(model);
        unknown.Program(new ProgramSyntax([.. tree.Statements, new ExpressionStatement(4, new TypeMemberAccess(4, new NamedType(4, "List", [new NamedType(4, "int", [])]), "FromRaw"))]));
        Assert.Contains("4: an expression the walk does not know, TypeMemberAccess", unknown.Missing);
    }

    // ---- facts --------------------------------------------------------------------------------

    private static T Find<T>(ProgramSyntax tree, Func<T, bool>? where = null)
        where T : class
    {
        var found = new List<T>();
        Nodes.Collect(tree, found);
        return Assert.Single(found, n => where?.Invoke(n) ?? true);
    }

    [Fact]
    public void A_call_records_the_function_picked_and_the_slot_each_argument_fills()
    {
        var (tree, model) = Clean("""
            void Show(int n) { print(n) }
            void Show(string s, int count = 1, bool loud = false) { print(s) }
            Show("a", loud: true)
            """);
        var call = Find<CallExpression>(tree, c => c.Arguments.Count == 2);
        var info = model.CallOf(call);
        Assert.Equal(CallKind.Function, info.Kind);
        Assert.Equal(2, info.Function!.Declaration!.Line);
        Assert.Equal([0, 2], info.Binding);
        Assert.Equal(3, info.SlotCount);
        Assert.Equal("void", info.Result.Text);
    }

    [Fact]
    public void A_literal_argument_has_the_type_of_the_parameter_of_the_function_picked()
    {
        // the literal 4 is read first on its own, as an `int`, to pick among the functions of the name, then against the parameter of the one picked, whose reading stands
        var (tree, model) = Clean("""
            void Take(byte b, string s) { print(b) }
            void Take(long n, int k) { print(n) }
            Take(4, 2)
            """);
        var literal = Find<IntegerLiteral>(tree, l => l.Line == 3 && l.Value.Digits == "4");
        Assert.Equal("long", model.TypeOf(literal).Type.Text);
        Assert.Equal(2, model.CallOf(Find<CallExpression>(tree, c => c.Line == 3)).Function!.Declaration!.Line);
    }

    [Fact]
    public void A_minus_before_a_literal_is_one_constant_and_the_literal_under_it_has_no_entry()
    {
        var (tree, model) = Clean("sbyte x = -128\nprint(x)");
        var minus = Find<UnaryExpression>(tree);
        Assert.Equal("sbyte", model.TypeOf(minus).Type.Text);
        Assert.Equal(-128, (int)Assert.IsType<IntegerConstant>(model.TypeOf(minus).Constant).Value);
        Assert.False(model.HasType(minus.Operand));
    }

    [Fact]
    public void A_call_that_stands_as_a_value_has_its_success_as_its_type_and_its_whole_result_as_the_calls()
    {
        var (tree, model) = Clean("""
            data NotFound
            int | NotFound Find(int id) {
                if id == 1 {
                    return 10
                }

                return NotFound
            }

            int | NotFound Twice(int id) {
                value = Find(id)
                return value * 2
            }

            print(Twice(1) else { NotFound => 0 })
            """);
        var call = Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "Find" });
        Assert.Equal("int", model.TypeOf(call).Type.Text);
        Assert.Equal("int | NotFound", model.CallOf(call).Result.Text);
    }

    [Fact]
    public void A_sym_operand_records_the_function_or_the_state_it_names()
    {
        var (tree, model) = Clean("""
            static mut long count = 0
            void G() {
            }

            raw {
                asm("call {f}", f = sym G)
                asm("inc qword ptr [rip + {c}]", c = sym count)
            }
            """, "raw");
        Assert.Equal("G", model.FunctionNameOf(Find<NameExpression>(tree, n => n.Name == "G")).Name);
        Assert.Same(model.DeclarationOf(Find<VariableDeclaration>(tree)), model.VariableOf(Find<NameExpression>(tree, n => n.Name == "count")));
    }

    [Fact]
    public void A_generic_call_records_its_type_arguments_and_its_result_with_them_put_in()
    {
        var (tree, model) = Clean("""
            T First<T>(List<T> items) => items[0]
            mut xs = List<string>()
            xs.Add("a")
            print(First(xs))
            """);
        var call = Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "First" });
        var info = model.CallOf(call);
        Assert.Equal("string", Assert.Single(info.Map.Values).Text);
        Assert.Equal("string", info.Result.Text);
        Assert.Equal(CallKind.ListAdd, model.CallOf(Find<CallExpression>(tree, c => c.Callee is MemberAccess { Member: "Add" })).Kind);
        Assert.Equal(CallKind.ListNew, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "List" })).Kind);
    }

    [Fact]
    public void A_lambda_records_what_it_captures_through_a_lambda_inside_it()
    {
        var (tree, model) = Clean("""
            int Twice((int) => int f, int x) => f(f(x))
            delta = 3
            print(Twice(x => Twice(y => y + delta, x), 1))
            """);
        var outer = Find<LambdaExpression>(tree, l => l.Parameters[0].Name == "x");
        var inner = Find<LambdaExpression>(tree, l => l.Parameters[0].Name == "y");
        Assert.Equal(["delta"], model.LambdaOf(outer).Captures.Select(v => v.Name));
        Assert.Equal(["delta"], model.LambdaOf(inner).Captures.Select(v => v.Name));
        Assert.Equal("y", Assert.Single(model.LambdaOf(inner).Parameters).Name);
        Assert.Equal(CallKind.Value, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "f" } && c.Arguments[0].Value is NameExpression)).Kind);
    }

    [Fact]
    public void A_name_records_the_variable_it_reads_and_a_declaration_the_variable_it_makes()
    {
        var (tree, model) = Clean("""
            mut total = 0
            for i in 1..3 {
                total = total + i
            }
            print(total)
            """);
        var declaration = Find<VariableDeclaration>(tree);
        var variable = model.DeclarationOf(declaration);
        var assignment = Find<AssignOrDeclare>(tree);
        Assert.False(model.Declares(assignment));
        Assert.Same(variable, model.AssignedVariableOf(assignment));
        Assert.Same(variable, model.VariableOf(Find<NameExpression>(tree, n => n.Name == "total" && n.Line == 5)));
        Assert.Equal(VariableKind.Loop, model.DeclarationOf(Find<ForInStatement>(tree)).Kind);
        Assert.Equal("int", model.TypeOf(Find<RangeExpression>(tree)).Type.Text);
    }

    [Fact]
    public void A_value_of_a_type_without_fields_and_a_match_arm_record_their_types()
    {
        var (tree, model) = Clean("""
            data Empty
            data Box(int Value)
            Box | Empty Pick(bool full) {
                if full {
                    return Box(1)
                }

                return Empty
            }

            match Pick(true) {
                Box b => print(b.Value)
                Empty => print(0)
            }
            """);
        var empty = Find<NameExpression>(tree, n => n.Name == "Empty");
        Assert.IsType<UnitConstant>(model.TypeOf(empty).Constant);
        var arm = Find<TypePattern>(tree, p => p.Binding == "b");
        Assert.Equal("Box", model.PatternTypeOf(arm).Text);
        Assert.Equal(VariableKind.Binding, model.DeclarationOf(arm).Kind);
        Assert.Equal("Box | Empty", model.TypeOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "Pick" })).Type.Text);
        Assert.Equal(CallKind.DataNew, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "Box" })).Kind);
        Assert.Equal("Box", model.DataTypeOf(Find<TypeDeclaration>(tree, d => d.Name == "Box")).Name);
    }

    [Fact]
    public void An_assignment_into_a_path_records_its_variable_and_the_type_of_each_field()
    {
        var (tree, model) = Clean("""
            data Point(int X, int Y)
            data Line(Point From, Point To)
            mut l = Line(Point(0, 0), Point(1, 1))
            l.From.X = 5
            print(l.From.X)
            """);
        var assignment = Find<Assignment>(tree);
        var x = (MemberAccess)assignment.Target;
        Assert.Equal("int", model.TypeOf(x).Type.Text);
        Assert.Equal("Point", model.TypeOf(x.Target).Type.Text);
        Assert.Equal("l", model.VariableOf((NameExpression)((MemberAccess)x.Target).Target).Name);
    }

    [Fact]
    public void A_call_of_a_C_declaration_binds_its_variadic_arguments_to_no_slot()
    {
        var (tree, model) = Clean("""
            extern "C" {
                int printf(byte* format, ...)
            }

            s = "%d %d"
            raw {
                printf(s.Pointer, 1, 2)
            }
            """, "raw native");
        var call = Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "printf" });
        var info = model.CallOf(call);
        Assert.Equal(CallKind.CFunction, info.Kind);
        Assert.Equal([0, -1, -1], info.Binding);
        Assert.Equal("printf", model.CFunctionOf(Find<ExternFunction>(tree)).Name);
    }

    [Fact]
    public void State_outside_a_function_a_C_function_value_and_the_raw_forms_record_what_they_are()
    {
        var (tree, model) = Clean("""
            extern "C" {
                long write(int fd, long* buf, long n)
            }

            static mut long count = 0
            void Bump() {
                raw {
                    count = count + 1
                }
            }

            Bump()
            p = Ptr<long>(64)
            raw {
                f = write
                print(SizeOf<long>() + long(p) + count)
                print(p != null)
                print(f(1, p, 0))
            }
            """, "raw native");
        var state = Find<VariableDeclaration>(tree, v => v.Name == "count");
        Assert.Equal(VariableKind.Static, model.DeclarationOf(state).Kind);
        Assert.Same(model.DeclarationOf(state), model.VariableOf(Find<NameExpression>(tree, n => n.Name == "count" && n.Line == 16)));
        Assert.Same(model.DeclarationOf(state), model.AssignedVariableOf(Find<AssignOrDeclare>(tree, a => a.Name == "count")));
        Assert.Equal("write", model.FunctionNameOf(Find<NameExpression>(tree, n => n.Name == "write")).Name);
        Assert.Equal(CallKind.Value, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "f" })).Kind);
        Assert.Equal(CallKind.PointerConversion, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "Ptr" })).Kind);
        Assert.Equal("long", model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "SizeOf" })).Target!.Text);
        Assert.Equal(CallKind.Conversion, model.CallOf(Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "long" })).Kind);
        Assert.IsType<NullConstant>(model.TypeOf(Find<NullLiteral>(tree)).Constant);
    }

    [Fact]
    public void Inside_its_class_a_name_a_member_and_a_state_have_records_the_member()
    {
        // the reading on R13 with K7 (README): the code generator reads the field and the `static` field, and outside the class the state
        var (tree, model) = Clean("""
            thread static mut long left = 7

            class C(mut long left) {
                pub void Set() {
                    raw {
                        left = left + 1
                    }
                }
            }

            class D(long V) {
                pub static long left = 3

                pub static long Get() => left
            }

            long F() {
                raw {
                    return left
                }
            }

            c = C(1)
            c.Set()
            print(D.Get() + F())
            """, "raw");
        Assert.True(model.IsImplicitField(Find<AssignOrDeclare>(tree, a => a.Name == "left"), out _, out var assigned));
        Assert.True(model.IsImplicitField(Find<NameExpression>(tree, n => n.Name == "left" && n.Line == 6), out _, out var read));
        Assert.Equal("left", assigned!.Name);
        Assert.Same(assigned, read);
        Assert.True(model.IsStaticField(Find<NameExpression>(tree, n => n.Name == "left" && n.Line == 14), out var staticField));
        Assert.Equal("left", staticField.Name);
        var state = model.DeclarationOf(Find<VariableDeclaration>(tree, v => v.Name == "left"));
        Assert.Same(state, model.VariableOf(Find<NameExpression>(tree, n => n.Name == "left" && n.Line == 19)));
    }

    [Fact]
    public void A_call_with_else_records_its_whole_result_and_stands_as_its_success()
    {
        var (tree, model) = Clean("""
            data NotFound
            int | NotFound Find(int key) {
                if key == 1 {
                    return 10
                }

                return NotFound
            }

            print(Find(2) else { NotFound => 0 })
            """);
        var call = Find<CallExpression>(tree, c => c.Callee is NameExpression { Name: "Find" });
        Assert.Equal("int | NotFound", model.TypeOf(call).Type.Text);
        Assert.Equal("int", model.TypeOf(Find<ElseExpression>(tree)).Type.Text);
        Assert.Equal("int | NotFound", model.CallOf(call).Result.Text);
    }

    [Fact]
    public void A_reader_throws_for_a_node_the_checker_did_not_read()
    {
        var (_, model) = Clean("x = 4\nprint(x)");
        var stray = new NameExpression(9, "x");
        var thrown = Assert.Throws<InvalidOperationException>(() => model.TypeOf(stray));
        Assert.Contains("line 9", thrown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => model.VariableOf(stray));
        Assert.False(model.HasType(stray));
    }

    /// <summary>Every node of a tree, found through the properties of its records.</summary>
    private static class Nodes
    {
        public static void Collect<T>(object? node, List<T> found)
            where T : class
        {
            switch (node)
            {
                case null or string:
                    return;
                case T t:
                    found.Add(t);
                    break;
                default:
                    break;
            }

            if (node is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    Collect(item, found);
                }

                return;
            }

            if (node.GetType().Namespace != typeof(Node).Namespace)
            {
                return;
            }

            foreach (var property in node.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
                {
                    Collect(property.GetValue(node), found);
                }
            }
        }
    }

    /// <summary>
    /// The walk of a tree against its model: what a code generator reads of each node, and the entry
    /// it reads it from. A name in a value's place means a variable, a function where it is no variable,
    /// or the one value of a data type without fields (D8); the callee of a call is walked where the call
    /// calls a value (F11) or the receiver of a list's method, and nowhere else, since a function, a type
    /// and a built-in are no values.
    /// </summary>
    private sealed class Walk(SemanticModel model)
    {
        public List<string> Missing { get; } = [];

        public int Seen { get; private set; }

        /// <summary>T33: the copies the walk read in place of the body of a function that reads its type, by name.</summary>
        public List<string> Copies { get; } = [];

        public int Made { get; private set; }

        /// <summary>T33: the arms the walk read of the matches on <c>T.Kind</c>, one of each in each copy.</summary>
        public int KindArms { get; private set; }

        /// <summary>T33: the passes the walk read of the loops over <c>T.Fields</c>, in place of each loop's own body.</summary>
        public int Passes { get; private set; }

        private void Need(bool present, int line, string what)
        {
            Seen++;
            if (!present)
            {
                Missing.Add($"{line}: {what}");
            }
        }

        public void Program(ProgramSyntax tree)
        {
            foreach (var s in tree.Statements)
            {
                Statement(s);
            }
        }

        private void Block(Block b)
        {
            foreach (var s in b.Statements)
            {
                Statement(s);
            }
        }

        private void Statement(Statement s)
        {
            switch (s)
            {
                case Block b:
                    Block(b);
                    break;
                case VariableDeclaration v:
                    Need(model.Declares(v), v.Line, $"declaration of `{v.Name}`");
                    Expression(v.Value);
                    break;
                case AssignOrDeclare a:
                    Need(model.Declares(a) || Try(() => model.AssignedVariableOf(a)) || model.IsImplicitField(a, out _, out _), a.Line, $"`{a.Name} =`");
                    Expression(a.Value);
                    break;
                case Assignment a:
                    Place(a.Target);
                    Expression(a.Value);
                    break;
                case ExpressionStatement e:
                    Expression(e.Expression);
                    break;
                case IfStatement i:
                    Expression(i.Condition);
                    Block(i.Then);
                    if (i.Else is not null)
                    {
                        Statement(i.Else);
                    }

                    break;
                case WhileStatement w:
                    Expression(w.Condition);
                    Block(w.Body);
                    break;
                case ForInStatement f when model.PassesOf(f) is { } passes:
                    foreach (var pass in passes)
                    {
                        Passes++; // T33: a code generator reads each pass, never the loop's own collection and body
                        Block(pass);
                    }

                    break;
                case ForInStatement f:
                    Need(model.Declares(f), f.Line, $"loop variable `{f.Variable}`");
                    Expression(f.Collection);
                    Block(f.Body);
                    break;
                case MatchStatement m:
                    Expression(m.Subject);
                    Arms(KindArm(m.Subject) ?? m.Arms);
                    break;
                case ReturnStatement r:
                    Expression(r.Value);
                    break;
                case ThrowStatement t:
                    Expression(t.Value);
                    break;
                case RawBlock r:
                    Block(r.Body);
                    break;
                case AsmStatement a:
                    foreach (var o in a.Operands)
                    {
                        if (o.Word == "sym" && Unwrap(o.Value) is NameExpression symbol)
                        {
                            Need(model.IsVariable(symbol) || model.IsFunctionName(symbol), symbol.Line, $"`sym {symbol.Name}`");
                            continue;
                        }

                        if (o.Value is not NameExpression { Name: "_" })
                        {
                            Expression(o.Value);
                        }

                        if (o.Output is not NameExpression { Name: "_" })
                        {
                            Expression(o.Output);
                        }

                        if (o.Body is not null)
                        {
                            Block(o.Body);
                        }
                    }

                    break;
                case ExternBlock e:
                    foreach (var f in e.Functions)
                    {
                        Need(Try(() => model.CFunctionOf(f)), f.Line, $"C declaration `{f.Name}`");
                        Defaults(f.Parameters);
                    }

                    break;
                case FunctionDeclaration f when Try(() => model.FunctionOf(f)) && model.ReadsItsType(model.FunctionOf(f)):
                    foreach (var copy in model.CopiesOf(model.FunctionOf(f)))
                    {
                        Copies.Add(copy.Name); // T33: a code generator reads each copy, never the function's own body
                        Statement(copy.Declaration!);
                    }

                    break;
                case FunctionDeclaration f:
                    Need(Try(() => model.FunctionOf(f)), f.Line, $"function `{f.Name}`");
                    foreach (var p in f.Parameters)
                    {
                        Need(model.Declares(p), p.Line, $"parameter `{p.Name}`");
                    }

                    Defaults(f.Parameters);
                    Expression(f.ExpressionBody);
                    if (f.BlockBody is not null)
                    {
                        Block(f.BlockBody);
                    }

                    break;
                case TypeDeclaration { Kind: TypeDeclarationKind.Enum }:
                    break; // D12: its values are constants, which the expressions that name them carry
                case TypeDeclaration { Kind: TypeDeclarationKind.Class } t:
                    Need(Try(() => model.ClassOf(t)), t.Line, $"class `{t.Name}`");
                    Defaults(t.PrimaryConstructor ?? []);
                    foreach (var field in (t.Members ?? []).OfType<FieldDeclaration>().Where(f => (f.Modifiers & Modifiers.Static) == 0 && f.Value is not null))
                    {
                        Need(Try(() => model.FieldsThisOf(model.ClassOf(t))), field.Line, $"the instance the default of `{field.Name}` reads"); // K7
                        Expression(field.Value);
                    }

                    foreach (var method in (t.Members ?? []).OfType<FunctionDeclaration>())
                    {
                        if ((method.Modifiers & Modifiers.Static) == 0)
                        {
                            Need(Try(() => model.ThisOf(model.FunctionOf(method))), method.Line, $"`this` of `{method.Name}`"); // K7: a `static` one has none
                        }

                        Statement(method);
                    }

                    break;
                case TypeDeclaration t:
                    Need(Try(() => model.DataTypeOf(t)), t.Line, $"type `{t.Name}`");
                    Defaults(t.PrimaryConstructor ?? []);
                    foreach (var member in t.Members ?? [])
                    {
                        Statement(member);
                    }

                    break;
                case BreakStatement or ContinueStatement:
                    break;
                default:
                    Missing.Add($"{s.Line}: a statement the walk does not know, {s.GetType().Name}");
                    break;
            }
        }

        private void Defaults(IEnumerable<Parameter> parameters)
        {
            foreach (var p in parameters)
            {
                Expression(p.Default);
            }
        }

        /// <summary>T33: the one arm a code generator reads of a match on <c>T.Kind</c> in a copy; null for any other match.</summary>
        private IReadOnlyList<MatchArm>? KindArm(Expression subject)
        {
            if (model.KindArmOf(subject) is not { } taken)
            {
                return null;
            }

            KindArms++;
            return [taken];
        }

        private void Arms(IReadOnlyList<MatchArm> arms)
        {
            foreach (var arm in arms)
            {
                switch (arm.Pattern)
                {
                    case TypePattern tp:
                        Need(Try(() => model.PatternTypeOf(tp)), tp.Line, "the type of an arm");
                        if (tp.Binding is not null)
                        {
                            Need(model.Declares(tp), tp.Line, $"binding `{tp.Binding}`");
                        }

                        break;
                    case ValuePattern vp:
                        Expression(vp.Value);
                        break;
                    default:
                        break;
                }

                Statement(arm.Body);
            }
        }

        /// <summary>The left side of an assignment into a path or through a pointer (M4, R10): each part has its type, and the variable at its root is known.</summary>
        private void Place(Expression target)
        {
            Need(model.HasType(target), target.Line, $"{target.GetType().Name} assigned into");
            switch (target)
            {
                case ParenthesizedExpression p:
                    Place(p.Inner);
                    break;
                case MemberAccess m:
                    Place(m.Target);
                    break;
                case NameExpression n:
                    Need(model.IsVariable(n) || model.IsImplicitField(n, out _, out _), n.Line, $"the variable `{n.Name}`");
                    break;
                case ThisExpression t:
                    Need(model.IsImplicitField(t, out _, out _), t.Line, "`this`");
                    break;
                case UnaryExpression u:
                    Expression(u.Operand);
                    break;
                case IndexExpression i:
                    Expression(i.Target);
                    Expression(i.Index);
                    break;
                default:
                    Missing.Add($"{target.Line}: a place the walk does not know, {target.GetType().Name}");
                    break;
            }
        }

        private void Expression(Expression? e)
        {
            if (e is null)
            {
                return;
            }

            Need(model.HasType(e), e.Line, e is CallExpression ? "call's type" : e.GetType().Name);
            switch (e)
            {
                case NameExpression n:
                    // a variable, a function, a data value without fields, an implicit field (K7), a static field, or the name of a type parameter that
                    // holds a constant, a value of its enum, an integer, a `bool` or a `char` a code generator reads whole (T35)
                    Need(model.IsVariable(n) || model.IsFunctionName(n) || (model.HasType(n) && model.TypeOf(n).Constant is UnitConstant or EnumConstant or IntegerConstant or BoolConstant or CharConstant) || model.IsImplicitField(n, out _, out _) || model.IsStaticField(n, out _), n.Line, $"what `{n.Name}` means");
                    break;
                case ThisExpression t:
                    Need(model.IsImplicitField(t, out _, out _), t.Line, "`this`"); // K7
                    break;
                case MemberAccess when model.HasType(e) && model.TypeOf(e).Constant is EnumConstant:
                    break; // D12: a value of an enum, a constant a code generator reads whole
                case MemberAccess when model.HasType(e) && model.TypeOf(e).Constant is StringConstant:
                    break; // T33: `f.Name` in a pass of a loop over `T.Fields`, a constant a code generator reads whole
                case MemberAccess m when model.IsClockRead(m):
                    break; // S15: `timestamp.Now`, a read of the clock a code generator reads whole
                case MemberAccess m when model.IsStaticField(m, out _):
                    break; // K18: `T.F`, a `static` field whose constant a code generator reads, with no instance
                case MemberAccess m when model.SupportTestOf(m) is not null:
                    break; // R18: `X86.Sse41.IsSupported`, a test of the CPU a code generator reads whole
                case MemberAccess m:
                    Expression(m.Target);
                    break;
                case CallExpression c when model.IsMade(c, out var construction):
                    Made++; // T33: a code generator reads the call of the constructor `T.Make` became, never its lambda
                    if (construction is not null)
                    {
                        Expression(construction);
                    }

                    break;
                case CallExpression c:
                    Call(c);
                    break;
                case IndexExpression i:
                    Expression(i.Target);
                    Expression(i.Index);
                    break;
                case UnaryExpression { Operator: TokenKind.Minus, Operand: IntegerLiteral or RealLiteral or UnitLiteral }:
                    break; // L17: the `-` belongs to the literal, and the whole is one constant
                case UnaryExpression u:
                    Expression(u.Operand);
                    break;
                case BinaryExpression b:
                    Expression(b.Left);
                    Expression(b.Right);
                    break;
                case RangeExpression r:
                    Expression(r.Start);
                    Expression(r.End);
                    break;
                case LambdaExpression l:
                    Need(Try(() => model.LambdaOf(l)), l.Line, "lambda");
                    foreach (var p in l.Parameters)
                    {
                        Need(model.Declares(p), p.Line, $"lambda parameter `{p.Name}`");
                    }

                    Expression(l.ExpressionBody);
                    if (l.BlockBody is not null)
                    {
                        Block(l.BlockBody);
                    }

                    break;
                case MatchExpression m:
                    Expression(m.Subject);
                    Arms(KindArm(m.Subject) ?? m.Arms);
                    break;
                case WithExpression w:
                    Expression(w.Target);
                    foreach (var a in w.Assignments)
                    {
                        Expression(a.Value);
                    }

                    break;
                case ElseExpression el:
                    Expression(el.Call);
                    Arms(el.Arms);
                    break;
                case ListLiteral list:
                    foreach (var item in list.Items)
                    {
                        Expression(item);
                    }

                    break;
                case ParenthesizedExpression p:
                    Expression(p.Inner);
                    break;
                case StringLiteral s:
                    foreach (var hole in s.Segments.OfType<HoleSegment>())
                    {
                        Expression(hole.Expression);
                    }

                    break;
                case IntegerLiteral or RealLiteral or UnitLiteral or CharLiteral or BoolLiteral or NullLiteral:
                    break; // its type and constant are its entry
                default:
                    Missing.Add($"{e.Line}: an expression the walk does not know, {e.GetType().Name}");
                    break;
            }
        }

        private void Call(CallExpression c)
        {
            Need(model.HasCall(c), c.Line, "call");
            switch (model.HasCall(c) ? model.CallOf(c).Kind : (CallKind?)null)
            {
                case CallKind.Value:
                    Expression(c.Callee);
                    break;
                case CallKind.ListAdd or CallKind.ListWhere:
                    Expression(((MemberAccess)c.Callee).Target);
                    break;
                case CallKind.Method when c.Callee is MemberAccess m:
                    Expression(m.Target); // K7: the instance
                    break;
                case CallKind.Method:
                    Need(model.IsImplicitField(c.Callee, out _, out _), c.Line, "the `this` of a method called by its name"); // K7
                    break;
                default:
                    break;
            }

            foreach (var a in c.Arguments)
            {
                Expression(a.Value);
            }
        }

        private static Expression? Unwrap(Expression? e)
        {
            while (e is ParenthesizedExpression p)
            {
                e = p.Inner;
            }

            return e;
        }

        private static bool Try(Action read)
        {
            try
            {
                read();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
