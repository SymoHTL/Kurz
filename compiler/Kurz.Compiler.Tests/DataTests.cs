using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions on data values, unions and outcomes that no corpus case pins: the number of a type as a union's tag and
/// the tag's width (design record, section 5, round 19), a block for a data type in a relation of inheritance, copied before a write
/// (section 4, D5, D6, M4), the order of a path assignment (M4), a static of a block type (R13), the arm of a derived type over a
/// value of its base (C4), a literal arm on a union (C9), an <c>else</c> used as a statement (O5), the text of a thrown value (O7),
/// and what the emitter says `not supported` to.
/// </summary>
public sealed class DataTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private Compilation Compile(string text, params string[] grants) => Builder.Compile(SourceText.FromString("program.kz", text), grants, toolchain.Runtime, testBuild: true);

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name, params string[] grants)
    {
        var compilation = Compile(text, grants);
        Assert.True(compilation.Program is not null, string.Join("; ", compilation.Diagnostics.Sorted));
        return Toolchain.Run(toolchain.Build(compilation.Program, name));
    }

    private static void Ran((int Code, IReadOnlyList<string> Output, string Error) run, int code, string[] lines, string error)
    {
        Assert.Equal(lines, run.Output);
        Assert.Equal((code, error), (run.Code, run.Error));
    }

    [Fact]
    public void A_tag_is_as_wide_as_the_largest_number_among_its_cases_needs()
    {
        var layout = new Layout(new IrModule());
        KurzType type = Types.Int;
        var types = new List<KurzType>();
        for (var i = 0; i < 300; i++)
        {
            types.Add(type);
            type = new Semantics.PointerType(type); // 300 types, numbered in the order met
        }

        Assert.Equal(Enumerable.Range(0, 300), types.Select(layout.Number));
        Assert.Equal(0, layout.Number(Types.Int)); // a number, once given, stays
        Assert.Equal(8, layout.TagBits(new Semantics.UnionType([types[0], types[255]])));
        Assert.Equal(16, layout.TagBits(new Semantics.UnionType([types[0], types[256]])));
        Assert.Equal(16, layout.TagBits(new Semantics.UnionType([types[256], types[0]])));
    }

    [Fact]
    public void Two_unions_that_are_one_type_have_one_layout_whatever_the_order_of_the_cases_after_the_first()
    {
        var layout = new Layout(new IrModule());
        var a = new Semantics.UnionType([Types.Int, Types.String, Types.Byte]);
        var b = new Semantics.UnionType([Types.Int, Types.Byte, Types.String]);
        Assert.True(Types.Same(a, b));
        Assert.Equal(layout.Ir(a), layout.Ir(b));
        Assert.Equal("{ i8, [2 x i64] }", layout.Ir(a)); // the tag, then room for the string's 16 bytes at its alignment of 8
        Assert.Equal("{ i8, [1 x i16] }", layout.Ir(new Semantics.UnionType([Types.Byte, Types.Short])));
    }

    [Fact]
    public void A_nullable_function_value_is_its_pair_and_a_nullable_union_is_laid_out_as_its_union()
    {
        // N1 (section 4, the owner, 2026-10-30, "Nullable form, as Option", whose cost names a niche in a function value): the `null` of a
        // function value is the pair whose code address is 0 (section 4, assumed: that address as the niche, as Rust's), so a
        // `((int) => int)?` takes the function value's 16 bytes; a union's nullable form keeps each case's number in its tag, so where the
        // tags have one width the two are laid out alike, the tag, then room for the string's 16 bytes
        var layout = new Layout(new IrModule());
        var function = Types.Nullable(new Semantics.FunctionType([Types.Int], Types.Int));
        Assert.Equal("zeroinitializer", layout.Niche(function));
        Assert.Equal("%kz.fn", layout.Ir(function));
        Assert.Equal((16, 8), layout.Measure(function));
        var union = new Semantics.UnionType([Types.Int, Types.String]);
        Assert.Null(layout.Niche(Types.Nullable(union)));
        Assert.Equal("{ i8, [2 x i64] }", layout.Ir(union));
        Assert.Equal("{ i8, [2 x i64] }", layout.Ir(Types.Nullable(union)));
    }

    [Fact]
    public void Values_of_every_size_go_through_a_union_and_come_back()
    {
        const string text = """
            data Box(byte | string | long V)

            string Show(byte | string | long v) => match v {
                byte b => "byte {b}"
                string s => "string {s}"
                long n => "long {n}"
            }

            print(Show(byte(7)))
            print(Show("seven"))
            print(Show(-7000000000))
            print(Show(Box("in a field").V))
            print(Show(Box(long(8)).V))
            """;
        Ran(BuildAndRun(text, "union-sizes"), 0, ["byte 7", "string seven", "long -7000000000", "string in a field", "long 8"], "");
    }

    [Fact]
    public void A_copy_by_with_of_a_derived_value_through_its_base_keeps_its_type_and_leaves_the_original()
    {
        const string text = """
            data User(int Id)
            data Admin(int Level) : User

            User u = Admin(1, 2)
            v = u with { Id = 3 }
            print(v)
            print(u)
            """;
        Ran(BuildAndRun(text, "with-derived"), 0, ["Admin { Id = 3, Level = 2 }", "Admin { Id = 1, Level = 2 }"], "");
    }

    [Fact]
    public void An_assignment_into_a_path_through_a_block_copies_the_block_so_that_another_variable_keeps_its_value()
    {
        const string text = """
            data Base(int X)
            data Derived(int Y) : Base
            data Holder(Base B)

            mut h = Holder(Derived(1, 2))
            before = h
            h.B.X = 5
            print(h.B)
            print(before.B)
            """;
        Ran(BuildAndRun(text, "path-block"), 0, ["Derived { X = 5, Y = 2 }", "Derived { X = 1, Y = 2 }"], "");
    }

    [Fact]
    public void An_assignment_into_a_path_follows_the_path_in_the_variable_as_its_value_left_it()
    {
        // M4, the owner, 2026-10-24, "Value first": the block on the path is the one the `mut` argument stored
        const string text = """
            data Base(int X)
            data Derived(int Y) : Base
            data Holder(Base B)

            int Swap(mut Holder held) {
                held = Holder(Derived(7, 8))
                return 5
            }

            mut h = Holder(Derived(1, 2))
            h.B.X = Swap(mut h)
            print(h.B)
            """;
        Ran(BuildAndRun(text, "path-order-block"), 0, ["Derived { X = 5, Y = 8 }"], "");
    }

    [Fact]
    public void A_static_of_a_type_in_a_relation_of_inheritance_is_a_block_of_the_type_of_its_value()
    {
        const string text = """
            data Base
            data Leaf : Base

            static mut Base b = Leaf

            raw {
                print(b)
                print(b == Leaf)
            }
            """;
        Ran(BuildAndRun(text, "static-block", "raw"), 0, ["Leaf", "true"], "");
    }

    [Fact]
    public void The_one_value_of_a_type_without_fields_is_a_constant_whose_count_no_holder_changes()
    {
        // D8 as compiler/README.md assumes it: the one value is a constant of the module, which nothing writes, and its count carries
        // the highest bit, held for ever, as a literal's count of bytes carries its mark (section 4); a family with a type without
        // fields tests the bit at every change of a count, so that the value is held and let go of in every place a block is
        const string text = """
            data Shape
            data Dot : Shape
            data Circle(double R) : Shape

            data Holder(Shape S, int N)

            Shape Pick(int i) {
                if i % 2 == 0 {
                    return Dot
                }

                return Circle(1.5)
            }

            mut all = List<Shape>()
            for i in 0..<1000 {
                all.Add(Dot)
                all.Add(Pick(i))
            }

            d = Dot
            mut Shape e = d
            e = Pick(3)
            e = Dot
            h = Holder(Dot, 1)
            k = h with { N = 2 }
            print(all.Count)
            print(e)
            print(k)
            """;
        Ran(BuildAndRun(text, "unit-held"), 0, ["2000", "Dot", "Holder { S = Dot, N = 2 }"], "");
        var held = Compile(text).Program!.Ir;
        Assert.Contains("{ i64 -9223372036854775808, i32 ", held, StringComparison.Ordinal);
        Assert.Contains("icmp slt i64", Release(held), StringComparison.Ordinal);

        // a family without such a type tests nothing at a change of its counts
        Assert.DoesNotContain("icmp slt i64", Release(Compile("data Link(int V, Link? Next)\n\nprint(Link(1, Link(2, null)).V)\n").Program!.Ir), StringComparison.Ordinal);
    }

    /// <summary>The body of the one helper of the program that lets go of a count of a block.</summary>
    private static string Release(string ir)
    {
        var start = ir.IndexOf("define internal void @\"release.block.", StringComparison.Ordinal);
        Assert.True(start >= 0, "no helper lets go of a count of a block");
        Assert.Equal(-1, ir.IndexOf("define internal void @\"release.block.", start + 1, StringComparison.Ordinal));
        return ir[start..ir.IndexOf("\n}", start, StringComparison.Ordinal)];
    }

    [Fact]
    public void A_data_type_that_holds_a_value_of_its_own_type_is_a_block_read_and_written_as_any_value()
    {
        const string text = """
            data Leaf
            data Node(int V, Node | Leaf Next)
            data A(int X, B | Leaf B)
            data B(int Y, A | Leaf A)

            string Show(Node | Leaf n) => match n {
                Node x => "{x.V} {Show(x.Next)}"
                Leaf => "."
            }

            chain = Node(1, Node(2, Node(3, Leaf)))
            print(Show(chain))
            mut changed = chain
            changed.V = 9
            print(Show(changed))
            print(Show(chain))
            match A(1, B(2, A(3, Leaf))).B {
                B b => print(b.Y)
                Leaf => print("leaf")
            }
            """;
        Ran(BuildAndRun(text, "self-holding"), 0, ["1 2 3 .", "9 2 3 .", "1 2 3 .", "2"], "");
    }

    [Fact]
    public void An_arm_of_a_derived_type_takes_a_value_of_its_base_that_holds_it()
    {
        const string text = """
            data User(string Name)
            data Admin(int Level) : User

            void Show(User u) {
                match u {
                    Admin a => print("admin {a.Level}")
                    User x => print("user {x.Name}")
                }
            }

            Show(Admin("Ann", 2))
            Show(User("Bea"))
            print(User("Bea") == Admin("Bea", 0))
            """;
        Ran(BuildAndRun(text, "derived-arm"), 0, ["admin 2", "user Bea", "false"], "");
    }

    [Fact]
    public void A_literal_arm_on_a_union_takes_the_case_the_literal_is_read_as()
    {
        // C9: the checker reads the literal as the union's case of its kind (T8 with D9), here `long` and `byte`
        const string text = """
            long | string v = 5
            match v {
                5 => print("five")
                else => print("other")
            }
            byte | string w = 7
            match w {
                6 => print("six")
                7 => print("seven")
                else => print("other")
            }
            """;
        Ran(BuildAndRun(text, "literal-union"), 0, ["five", "seven"], "");
    }

    [Fact]
    public void An_else_used_as_a_statement_recovers_with_a_value_it_drops()
    {
        // O5: an arm recovers with a value of the success, which a statement does not keep
        const string text = """
            data NotFound

            int | NotFound Find(int id) => NotFound

            Find(1) else {
                NotFound => 0
            }
            print("done")
            """;
        Ran(BuildAndRun(text, "else-statement"), 0, ["done"], "");
    }

    [Fact]
    public void A_thrown_data_value_and_the_case_a_bare_throw_throws_write_their_texts_below_the_error_line()
    {
        Ran(BuildAndRun("data ConfigMissing(string Path)\n\nthrow ConfigMissing(\"app.json\")\n", "throw-data"), 1, [], "error: thrown at program.kz:3:\nConfigMissing { Path = app.json }\n");
        const string bare = """
            data NotFound(int Id)

            int | NotFound Find(int id) => NotFound(id)

            value = Find(4) else {
                NotFound => throw
            }
            print(value)
            """;
        Ran(BuildAndRun(bare, "throw-bare"), 1, [], "error: thrown at program.kz:6:\nNotFound { Id = 4 }\n");
    }

    [Fact]
    public void A_thrown_instance_of_a_class_without_a_text_writes_its_class_s_name_below_the_error_line()
    {
        // O7: an instance of a class that neither declares nor inherits a `Text()`, thrown by `throw` or by a bare `throw`, has its class's
        // name below the error line (the owner, 2026-11-20, issue 598, comment 7051500939): the bare name of the instance's own class, a
        // derived one thrown as its base (2026-11-21, comment 7060156252, Q2 and Q3 of comment 7055011193), and that name where the instance
        // stands in a `data` value or a list that is thrown (the same answer, Q8 of comment 7055578588)
        const string Note = "data Missing\n\nclass Note(string Body)\n\nint | Note | Missing Find(int id) {\n    if id == 1 {\n        return Note(\"a\")\n    }\n\n    return Missing\n}\n\nv = Find(1) else {\n";
        Ran(BuildAndRun(Note + "    Note n => throw n\n    Missing => 0\n}\nprint(v)\n", "throw-class"), 1, [], "error: thrown at program.kz:14:\nNote\n");
        Ran(BuildAndRun(Note + "    Note => throw\n    Missing => 0\n}\nprint(v)\n", "throw-class-bare"), 1, [], "error: thrown at program.kz:14:\nNote\n");
        Ran(BuildAndRun(Note + "    else => throw\n}\nprint(v)\n", "throw-class-union"), 1, [], "error: thrown at program.kz:14:\nNote\n");
        const string Animals = "class Animal(string Name)\n\nclass Dog(int Legs) : Animal\n\nvoid Fail(Animal a) {\n    throw a\n}\n\n";
        Ran(BuildAndRun(Animals + "Fail(Dog(\"rex\", 4))\n", "throw-class-derived"), 1, [], "error: thrown at program.kz:6:\nDog\n");
        Ran(BuildAndRun(Animals + "Fail(Animal(\"tom\"))\n", "throw-class-base"), 1, [], "error: thrown at program.kz:6:\nAnimal\n");

        // an instance thrown as a base that has no `Text()` shows what the `Text()` of its own class gives, found when the program runs, its
        // class's own or that of a base between the two, a `virtual` one's override among them, as C#'s virtual `ToString()` (the owner,
        // 2026-11-21, issue 598, comment 7060156252, Q9 of comment 7055578588); an instance whose class's chain declares none keeps its name,
        // a sibling class's `Text()` aside; an instance a thrown `data` value holds as such a base takes the text by the same rule (F1),
        // and a `Text()` without `pub` is called as one with it (F2) (the owner, 2026-11-21, issue 598, comment 7063693020)
        const string Texted = "class Animal(string Name)\n\nclass Dog(int Legs) : Animal {\n    pub string Text() => \"dog {Name}\"\n}\n\nvoid Fail(Animal a) {\n    throw a\n}\n\n";
        Ran(BuildAndRun(Texted + "print(Dog(\"rex\", 4))\nFail(Dog(\"rex\", 4))\n", "throw-class-derived-text"), 1, ["dog rex"], "error: thrown at program.kz:8:\ndog rex\n");
        const string Kinds = Texted + "class Puppy(int Age) : Dog\n\nclass Cat(int Lives) : Animal\n\n";
        Ran(BuildAndRun(Kinds + "Fail(Puppy(\"pip\", 4, 1))\n", "throw-class-inherited-text"), 1, [], "error: thrown at program.kz:8:\ndog pip\n");
        Ran(BuildAndRun(Kinds + "Fail(Cat(\"tom\", 9))\n", "throw-class-sibling-text"), 1, [], "error: thrown at program.kz:8:\nCat\n");
        Ran(BuildAndRun(Kinds + "Fail(Animal(\"any\"))\n", "throw-class-base-of-text"), 1, [], "error: thrown at program.kz:8:\nAnimal\n");
        Ran(BuildAndRun(Texted.Replace("pub string", "pub virtual string") + "class Puppy(int Age) : Dog {\n    pub override string Text() => \"puppy {Name}\"\n}\n\nFail(Puppy(\"pip\", 4, 1))\n", "throw-class-override-text"), 1, [], "error: thrown at program.kz:8:\npuppy pip\n");
        Ran(BuildAndRun(Texted + "data Pen(Animal Item)\n\nthrow Pen(Dog(\"rex\", 4))\n", "throw-class-held-text"), 1, [], "error: thrown at program.kz:13:\nPen { Item = dog rex }\n");
        Ran(BuildAndRun(Texted.Replace("pub string", "string") + "Fail(Dog(\"rex\", 4))\n", "throw-class-private-text"), 1, [], "error: thrown at program.kz:8:\ndog rex\n");
        Ran(BuildAndRun("class Counter(mut int Count)\ndata Box(Counter Item)\n\nthrow Box(Counter(1))\n", "throw-class-held"), 1, [], "error: thrown at program.kz:4:\nBox { Item = Counter }\n");
        Ran(BuildAndRun("class Counter(mut int Count)\n\nthrow [Counter(1), Counter(2)]\n", "throw-class-list"), 1, [], "error: thrown at program.kz:3:\n[Counter, Counter]\n");
    }

    [Fact]
    public void A_thrown_value_of_an_interface_without_a_text_writes_the_text_of_its_instance_s_class()
    {
        // O7: a value thrown as an interface that declares no `Text()` takes the text of a thrown class instance: what the `Text()` of the
        // instance's own class gives, found when the program runs, its own or a base's, else that class's name (the owner, 2026-11-21, issue
        // 621, comment 7067091007); one that a thrown `data` value holds takes it by the same rule, and a `Text()` without `pub` is called
        // as one with it (assumed: F1 and F2 of issue 598, comment 7063693020, read for such a value)
        const string Sized = "interface Sized {\n    int Size()\n}\n\nclass Dot : Sized {\n    pub int Size() => 1\n}\n\nclass Ring(int Width) : Sized {\n    pub int Size() => Width\n    pub string Text() => \"ring {Width}\"\n}\n\nvoid Fail(Sized s) {\n    throw s\n}\n\n";
        Ran(BuildAndRun(Sized + "Fail(Ring(2))\n", "throw-face-text"), 1, [], "error: thrown at program.kz:15:\nring 2\n");
        Ran(BuildAndRun(Sized + "Fail(Dot())\n", "throw-face-name"), 1, [], "error: thrown at program.kz:15:\nDot\n");
        Ran(BuildAndRun(Sized + "class Band(int More) : Ring\n\nclass Spot : Dot\n\nFail(Band(3, 4))\n", "throw-face-inherited-text"), 1, [], "error: thrown at program.kz:15:\nring 3\n");
        Ran(BuildAndRun(Sized + "class Band(int More) : Ring\n\nclass Spot : Dot\n\nFail(Spot())\n", "throw-face-derived-name"), 1, [], "error: thrown at program.kz:15:\nSpot\n");
        Ran(BuildAndRun(Sized + "data Pen(Sized Item)\n\nthrow Pen(Ring(2))\n", "throw-face-held"), 1, [], "error: thrown at program.kz:20:\nPen { Item = ring 2 }\n");
        Ran(BuildAndRun(Sized.Replace("pub string", "string") + "Fail(Ring(2))\n", "throw-face-private-text"), 1, [], "error: thrown at program.kz:15:\nring 2\n");
    }

    [Fact]
    public void A_data_type_the_emitter_does_not_lay_out_builds_where_no_code_needs_it()
    {
        // the owner, 2026-10-23, "At first use"; the theory below has the same type where code needs it
        Ran(BuildAndRun("data Thing(int A)\ndata Box<T>(T V) : Thing\n\nBox<int> Make() => Box<int>(1, 2)\n\nprint(1)\n", "unreached-declaration"), 0, ["1"], "");
    }

    [Theory]
    [InlineData("data User(int Id)\n\nprint(SizeOf<User>())\n", 3, "the size of a `User` (R14)")]
    [InlineData("data Thing(int A)\ndata Box<T>(T V) : Thing\n\nprint(Box<int>(1, 2).V)\n", 4, "a generic data type in a relation of inheritance, `Box<int>` (D6, T19)")]
    [InlineData("data Leaf\ndata Tree<T>(T V, Tree<T> | Leaf Next)\n\nprint(Tree<int>(1, Leaf).V)\n", 4, "a generic data type that holds a value of its own type, `Tree<int>` (D1, T19)")]
    [InlineData("data Node<T>(int V, Node<List<T>>? Next)\n\nm = Map<Node<int>, int>()\nprint(m)\n", 3, "a generic data type that holds a value of its own type, `Node<int>` (D1, T19)")] // the checker's walk of a key ends inside it (M9)
    [InlineData("data Node<T>(int V, Node<List<T>>? Next)\n\nn = Node<int>(1, null)\nprint(n == n)\n", 3, "a generic data type that holds a value of its own type, `Node<int>` (D1, T19)")] // and its walk of `==` (D3)
    [InlineData("data Outer<T>(Outer<Map<int, int>>? O, T V)\n\nm = Map<Outer<int>, int>()\nprint(m)\n", 3, "a generic data type that holds a value of its own type, `Outer<int>` (D1, T19)")] // whatever its type arguments: the checker's walk of a key reads no `Map` behind it (M9)
    [InlineData("data Outer<T>(Outer<Map<int, int>>? O, T V)\n\nn = Outer<int>(null, 1)\nprint(n == n)\n", 3, "a generic data type that holds a value of its own type, `Outer<int>` (D1, T19)")] // nor its walk of `==` (D3)
    [InlineData("data Thing(int A)\ndata Box<T>(T V) : Thing\ndata Bag(Box<int> Weight)\n\nBag Empty() => Empty()\n\nEmpty()\n", 5, "a generic data type in a relation of inheritance, `Box<int>` (D6, T19)")] // the field of the type a called function returns, at that function
    public void What_the_emitter_does_not_lay_out_is_not_supported_at_its_line(string text, int line, string message)
    {
        var compilation = Compile(text);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((line, message), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
    }
}
