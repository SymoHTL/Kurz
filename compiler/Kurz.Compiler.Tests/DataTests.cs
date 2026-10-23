using Kurz.Compiler.Build;
using Kurz.Compiler.Emit;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// The emitter's decisions on data values, unions and outcomes that no corpus case pins: the number of a type as a union's tag and
/// the tag's width (design record, section 5, round 19), a block for a data type in a relation of inheritance, copied before a write
/// (section 4, D5, D6, M4), the arm of a derived type over a value of its base (C4), the text of a thrown value (O7), and what the
/// emitter says `not supported` to.
/// </summary>
public sealed class DataTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private Compilation Compile(string text) => Builder.Compile(SourceText.FromString("program.kz", text), [], toolchain.Runtime, testBuild: true);

    private (int Code, IReadOnlyList<string> Output, string Error) BuildAndRun(string text, string name)
    {
        var compilation = Compile(text);
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
            print(Box("in a field"))
            print(Box(long(8)).V)
            """;
        Ran(BuildAndRun(text, "union-sizes"), 0, ["byte 7", "string seven", "long -7000000000", "Box { V = in a field }", "8"], "");
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
    public void A_data_type_that_holds_a_value_of_its_own_type_is_a_block_read_and_written_as_any_value()
    {
        const string text = """
            data Leaf
            data Node(int V, Node | Leaf Next)
            data A(int X, B | Leaf B)
            data B(int Y, A | Leaf A)

            int Sum(Node | Leaf n) => match n {
                Node x => x.V + Sum(x.Next)
                Leaf => 0
            }

            chain = Node(1, Node(2, Node(3, Leaf)))
            print(Sum(chain))
            print(chain)
            mut changed = chain
            changed.V = 9
            print("{changed.V} {chain.V} {Sum(changed)}")
            print(A(1, B(2, A(3, Leaf))))
            """;
        Ran(BuildAndRun(text, "self-holding"), 0, ["6", "Node { V = 1, Next = Node { V = 2, Next = Node { V = 3, Next = Leaf } } }", "9 1 14", "A { X = 1, B = B { Y = 2, A = A { X = 3, B = Leaf } } }"], "");
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

    [Theory]
    [InlineData("data Pair<A, B>(A First, B Second)\n\nprint(Pair<int, string>(1, \"one\"))\n", 3, "the text of a value of the generic data type `Pair<int, string>` (A3)")]
    [InlineData("data Box(int | string V)\n\nprint(Box(1) == Box(1))\n", 3, "`==` on a data value with a field of the union `int | string` (D3, T18)")]
    [InlineData("data User(int Id)\n\nprint(SizeOf<User>())\n", 3, "the size of a `User` (R14)")]
    [InlineData("data Thing(int A)\ndata Box<T>(T V) : Thing\n\nprint(Box<int>(1, 2).V)\n", 4, "a generic data type in a relation of inheritance, `Box<int>` (D6, T19)")]
    [InlineData("data Leaf\ndata Tree<T>(T V, Tree<T> | Leaf Next)\n\nprint(Tree<int>(1, Leaf).V)\n", 4, "a generic data type that holds a value of its own type, `Tree<int>` (D1, T19)")]
    [InlineData("data Bag(List<int> Items)\n\nBag Empty() => Empty()\n\nEmpty()\n", 5, "a value of type `List<int>` (M9)")] // the field of the type the call returns
    public void What_the_emitter_does_not_lay_out_is_not_supported_at_its_line(string text, int line, string message)
    {
        var compilation = Compile(text);
        Assert.Null(compilation.Program);
        var diagnostic = Assert.Single(compilation.Diagnostics.Items);
        Assert.Equal((line, message), (diagnostic.Line, diagnostic.Message));
        Assert.False(diagnostic.IsError);
    }
}
