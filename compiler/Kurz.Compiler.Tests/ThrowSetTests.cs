using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;
using Xunit.Abstractions;

namespace Kurz.Compiler.Tests;

/// <summary>
/// What the code of a program can throw (<see cref="ThrowSets"/>, section 5 of the design record): over a fenced program, one thrown
/// type for each form that runs code of the program, each reached by that form alone, and the forms that reach nothing; the order of a
/// set; the <c>Reason</c> of an actor, the code of the actors it spawns under <c>escalate</c> among it; every kind of node of the tree
/// classified; every corpus case and the whole of <c>std/</c> walked; the runtime, which throws nothing; and the kinds of the runtime's
/// error type, one for each run-time row of the error table.
/// </summary>
public sealed class ThrowSetTests(ITestOutputHelper output)
{
    /// <summary>At least this many corpus cases are read without a diagnostic and walked; fewer means the walk read too few.</summary>
    private const int CaseFloor = 650;

    /// <summary>At least this many units of code are walked over those cases; fewer means the walk stopped following the calls.</summary>
    private const int UnitFloor = 3900;

    /// <summary>A program with one thrown type for each form that runs code, each in a function of its own that reaches it by that form alone.</summary>
    private const string Fenced = """
        data EDirect
        data EMethod
        data EOverride
        data EInterface
        data EDataMethod
        data EDataOverride
        data EProperty
        data EBareProperty
        data EConstructor
        data EFieldDefault
        data EBase
        data EInitializer
        data EWith
        data ELambda
        data EWhere
        data EInterpolation
        data EPrint
        data EJoin
        data ENested
        data ESpawn
        data ECallee
        data EEscalate
        data ERestarted
        data ETypeEscalate
        data EGeneric
        data EBare
        data EOther
        data EFirst
        data ESecond
        data EMixed
        data ELoud
        data EListed
        data EWorkerSeed
        data EWorkerHelper
        data EWorkerPrivate
        data EUnrelated

        void Direct() {
            throw EDirect
        }

        void ViaCall() {
            Direct()
        }

        class Callee(int N) {
            pub void Run() {
                throw EMethod
            }
        }

        void ViaMethod() {
            Callee(1).Run()
        }

        class Base(int N) {
            pub virtual void Go() {
                print(N)
            }
        }

        class Derived(int M) : Base {
            pub override void Go() {
                throw EOverride
            }
        }

        void ViaOverride(Base b) {
            b.Go()
        }

        interface Runner {
            void Step()
        }

        class Impl(int N) : Runner {
            pub void Step() {
                throw EInterface
            }
        }

        void ViaInterface(Runner r) {
            r.Step()
        }

        data Point(int X) {
            pub void Check() {
                throw EDataMethod
            }
        }

        void ViaDataMethod(Point p) {
            p.Check()
        }

        data Shape(int W) {
            pub virtual int Area() => W
        }

        data Box(int D) : Shape {
            pub override int Area() {
                throw EDataOverride
            }
        }

        int ViaDataOverride(Shape s) => s.Area()

        int CountOf(int n) {
            throw EProperty
        }

        class Bag(int N) {
            pub int Count => CountOf(N)
        }

        int ViaProperty(Bag b) => b.Count

        int BareCountOf(int n) {
            throw EBareProperty
        }

        class Sack(int N) {
            pub int Count => BareCountOf(N)

            pub int Twice() => Count * 2
        }

        int ViaBareProperty(Sack s) => s.Twice()

        class Made(int N) {
            pub Made() : this(0) {
                throw EConstructor
            }
        }

        Made ViaConstructor() => Made()

        int StartOf(int n) {
            throw EFieldDefault
        }

        class Defaulted(int N) {
            int start = StartOf(N)

            pub int Start() => start
        }

        Defaulted ViaFieldDefault() => Defaulted(1)

        int SeedOf(int m) {
            throw EBase
        }

        class Upper(int N)

        class Lower(int M) : Upper(SeedOf(M))

        Lower ViaBase() => Lower(5)

        int AreaOf(int w) {
            throw EInitializer
        }

        data Sized(int W) {
            pub int Area = AreaOf(W)
        }

        Sized ViaInitializer() => Sized(2)

        int ResizedArea(int w) {
            throw EWith
        }

        data Resized(int W) {
            pub int Area = ResizedArea(W)
        }

        Resized ViaWith(Resized r) => r with { W = 3 }

        int Lam(string s) {
            throw ELambda
        }

        class Holder((string) => int F) {
            pub int Run() => F("a")
        }

        int ViaLambdaValue() => Holder(s => Lam(s)).Run()

        bool Pick(int x) {
            throw EWhere
        }

        int ViaWhere(List<int> xs) => xs.Where(x => Pick(x)).Count

        class Shown(int N) {
            pub string Text() {
                throw EInterpolation
            }
        }

        string ViaInterpolation(Shown s) => "{s}"

        class Printed(int N) {
            pub string Text() {
                throw EPrint
            }
        }

        void ViaPrint(Printed p) {
            print(p)
        }

        class Joined(int N) {
            pub string Text() {
                throw EJoin
            }
        }

        string ViaJoin(List<Joined> xs) => string.Join(", ", xs)

        data Leaf(int N) {
            pub string Text() {
                throw ENested
            }
        }

        data Outer(Leaf L)

        void ViaNested(List<Outer?> xs) {
            print(xs)
        }

        int SpawnStart(int n) {
            throw ESpawn
        }

        actor Starter(int N) {
            int first = SpawnStart(N)

            pub int Ping() => first
        }

        void ViaSpawn() {
            s = spawn Starter(1)
            print(s.Ping())
        }

        actor Answering {
            pub void Fail() {
                throw ECallee
            }
        }

        void ViaActorCall() {
            a = spawn Answering()
            send a.Fail()
        }

        actor Escalated {
            pub void Fail() {
                throw EEscalate
            }
        }

        actor Restarted {
            pub void Fail() {
                throw ERestarted
            }
        }

        actor ByType on crash escalate {
            pub void Fail() {
                throw ETypeEscalate
            }
        }

        actor Parent {
            pub void Make() {
                e = spawn Escalated() on crash escalate
                r = spawn Restarted()
                t = spawn ByType()
                send e.Fail()
                send r.Fail()
                send t.Fail()
            }
        }

        void Raise<T>(T value) {
            throw value
        }

        void ViaGeneric() {
            Raise(EGeneric)
        }

        int | EBare Lookup(int id) {
            if id == 1 {
                return 1
            }

            return EBare
        }

        int ViaBare(int id) {
            v = Lookup(id) else {
                EBare => throw
            }

            return v
        }

        int | EOther Find(int id) {
            if id == 1 {
                return 1
            }

            return EOther
        }

        int ViaElsePattern(int id) {
            v = Find(id) else {
                else => throw
            }

            return v
        }

        void First() {
            throw EFirst
        }

        void Second() {
            throw ESecond
        }

        void Ordered() {
            Second()
            First()
        }

        HttpResponse Mixed(int status) {
            if status == 0 {
                throw EMixed
            }

            return HttpResponse.Empty(status)
        }

        class Loud(int N) {
            pub string Text() {
                throw ELoud
            }
        }

        void ViaThrowText() {
            throw Loud(1)
        }

        void ViaString() {
            throw "no config"
        }

        pub void Listed() {
            throw EListed
        }

        int WorkerSeed(int n) {
            throw EWorkerSeed
        }

        void Helper() {
            throw EWorkerHelper
        }

        void Unrelated() {
            throw EUnrelated
        }

        actor Worker(int N) {
            int seed = WorkerSeed(N)

            pub int Run() {
                Helper()
                return seed
            }

            void Quiet() {
                throw EWorkerPrivate
            }
        }

        """;

    private static readonly Lazy<(ProgramSyntax Tree, Checker Checker, ThrowSets Sets)> Program = new(() =>
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", Fenced), diagnostics);
        Assert.Empty(diagnostics.Items);
        var checker = Checker.Check(tree, diagnostics, [], CorpusTests.StdChecker.Value);
        Assert.Empty(diagnostics.Items);
        return (tree, checker, new ThrowSets(checker, CorpusTests.StdChecker.Value));
    });

    private static FunctionSymbol Function(string name)
    {
        var (tree, checker, _) = Program.Value;
        return checker.Model.FunctionOf(tree.Statements.OfType<FunctionDeclaration>().Single(f => f.Name == name));
    }

    private static ClassSymbol Actor(string name)
    {
        var (tree, checker, _) = Program.Value;
        return checker.Model.ClassOf(tree.Statements.OfType<TypeDeclaration>().Single(t => t.Name == name));
    }

    private static List<string> Names(IReadOnlyList<KurzType> types) => types.Select(Name).ToList();

    private static string Name(KurzType type) => type switch
    {
        DataType d => d.Symbol.Name,
        ClassType c => c.Symbol.Name,
        TypeParameterType p => p.Symbol.Name,
        PrimitiveType { Kind: Primitive.String } => "string",
        _ => type.ToString(),
    };

    private static List<string> Raised(string function) => Names(Program.Value.Sets.RaisedBy(Function(function)));

    [Theory]
    [InlineData("ViaCall", "EDirect")] // a call of a function
    [InlineData("ViaMethod", "EMethod")] // a call of a method
    [InlineData("ViaOverride", "EOverride")] // an override of the method a call names (K7)
    [InlineData("ViaInterface", "EInterface")] // the method of a class that implements the interface a call names (K7)
    [InlineData("ViaDataMethod", "EDataMethod")] // a method of a data type's body (D16)
    [InlineData("ViaDataOverride", "EDataOverride")] // an override in a data type derived from the one a call names (D16)
    [InlineData("ViaProperty", "EProperty")] // a read of a property through a value (K7)
    [InlineData("ViaBareProperty", "EBareProperty")] // a read of a property by its name inside its class (K7)
    [InlineData("ViaConstructor", "EConstructor")] // a constructor of a class's body (K14)
    [InlineData("ViaFieldDefault", "EFieldDefault")] // the default of a field of a class's body (K7)
    [InlineData("ViaBase", "EBase")] // the arguments a class passes to its base (K10)
    [InlineData("ViaInitializer", "EInitializer")] // a field of a data type's body, computed at its construction (D16)
    [InlineData("ViaWith", "EWith")] // a field of a data type's body, computed again by `with` (D16)
    [InlineData("ViaWhere", "EWhere")] // a lambda a built-in call takes (M9)
    [InlineData("ViaInterpolation", "EInterpolation")] // the Text() of a hole (L6, A5)
    [InlineData("ViaPrint", "EPrint")] // the Text() of what `print` writes (A1, A5)
    [InlineData("ViaJoin", "EJoin")] // the Text() of each item `string.Join` joins (X6)
    [InlineData("ViaNested", "ENested")] // the Text() of a field of a data type in a list of nullable values (A3, A5)
    [InlineData("ViaSpawn", "ESpawn")] // the construction of an actor a spawn makes, whose throw is the spawner's crash (U3)
    [InlineData("ViaGeneric", "EGeneric")] // a throw of a type parameter, with the type argument the call gives it (T19)
    [InlineData("ViaBare", "EBare")] // a bare `throw` in an arm of `else` throws the case its arm took (O7)
    [InlineData("ViaElsePattern", "EOther")] // a bare `throw` in the arm `else` throws the cases it takes (O7)
    [InlineData("ViaString", "string")] // the short form of `throw` (section 5)
    public void Each_form_that_runs_code_brings_what_that_code_throws(string function, string thrown) =>
        Assert.Equal([thrown, "RuntimeError"], Raised(function));

    [Fact]
    public void A_call_of_a_function_value_reaches_the_lambdas_whose_type_fits()
    {
        // F11: `F("a")` reaches the lambda given to `Holder`, whose body the function itself does not hold
        Assert.Equal("ELambda", Raised("ViaLambdaValue").First());
        Assert.Equal("RuntimeError", Raised("ViaLambdaValue").Last());
        Assert.DoesNotContain("ELambda", Raised("ViaCall")); // the positive control's negative: a lambda no call reaches adds nothing
    }

    [Fact]
    public void A_call_through_an_actor_s_reference_and_the_text_of_a_thrown_value_add_nothing()
    {
        // U20: the callee's crash reaches its caller as `callee-crashed`, a runtime error, and none of its code runs in the caller
        Assert.Equal(["RuntimeError"], Raised("ViaActorCall"));

        // section 6: a raise while the text of the thrown value is made ends the process, so `Loud`'s Text() adds nothing
        Assert.Equal(["Loud", "RuntimeError"], Raised("ViaThrowText"));
    }

    [Fact]
    public void A_set_is_in_the_order_of_the_first_throw_of_each_type_the_program_s_file_first_and_the_runtime_s_type_last()
    {
        // section 5, assumed there: `Second` is called first and `First` throws first in the text
        Assert.Equal(["EFirst", "ESecond", "RuntimeError"], Raised("Ordered"));

        // the program's own throw before the string `std/` throws for a status outside 200 to 599, though `std/`'s line is the lower
        var mixed = Raised("Mixed");
        Assert.Equal("EMixed", mixed[0]);
        Assert.Contains("string", mixed);
        Assert.Equal("RuntimeError", mixed[^1]);
        Assert.Single(mixed, "RuntimeError");
    }

    [Fact]
    public void A_sym_operand_reaches_the_function_it_names()
    {
        // R11: the instructions may call the function a `sym` operand names
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", "data ESym\n\nlong Nine() {\n    throw ESym\n}\n\nnaked long Forward() {\n    asm(\"jmp {f}\", f = sym Nine)\n}\n"), diagnostics);
        var checker = Checker.Check(tree, diagnostics, ["raw"], CorpusTests.StdChecker.Value);
        Assert.Empty(diagnostics.Items);
        var forward = checker.Model.FunctionOf(tree.Statements.OfType<FunctionDeclaration>().Single(f => f.Name == "Forward"));
        Assert.Equal(["ESym", "RuntimeError"], Names(new ThrowSets(checker, CorpusTests.StdChecker.Value).RaisedBy(forward)));
    }

    [Fact]
    public void A_function_of_type_parameters_throws_its_type_parameter_until_a_call_gives_it_a_type()
    {
        Assert.Equal(["T", "RuntimeError"], Raised("Raise"));
    }

    [Fact]
    public void The_reason_of_an_actor_is_what_its_construction_and_every_method_throw()
    {
        // section 5: the default of its field, a method that calls a function, and a private method, by line; not a function it does not reach
        Assert.Equal(["EWorkerSeed", "EWorkerHelper", "EWorkerPrivate", "RuntimeError"], Names(Program.Value.Sets.ReasonOf(Actor("Worker"))));
    }

    [Fact]
    public void The_reason_of_an_actor_holds_the_code_of_the_children_it_spawns_under_escalate()
    {
        // U25: under `escalate` written at the spawn, or the type's default (U27), the child's crash passes up whole; under the
        // default `restart` it does not
        Assert.Equal(["EEscalate", "ETypeEscalate", "RuntimeError"], Names(Program.Value.Sets.ReasonOf(Actor("Parent"))));
    }

    [Fact]
    public void The_pub_functions_are_listed_with_what_each_can_raise()
    {
        // section 5, "Compiler lists them": a `pub` function, and not one without `pub`
        var listed = Program.Value.Sets.PubFunctions();
        var function = Assert.Single(listed.Keys, f => f.Name == "Listed");
        Assert.Equal(["EListed", "RuntimeError"], Names(listed[function]));
        Assert.DoesNotContain(listed.Keys, f => f.Name == "Direct");
        Assert.Contains(listed.Keys, f => f.Name == "Run" && f.Owner?.Name == "Callee"); // a `pub` method too
    }

    [Fact]
    public void Every_kind_of_node_of_the_tree_is_classified_by_the_walk()
    {
        // a kind added to the tree without a line in the table, which says what it runs, would let a throw it reaches pass unseen
        var kinds = typeof(Node).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(Expression)) || t.IsSubclassOf(typeof(Statement)) || t.IsSubclassOf(typeof(Pattern))))
            .ToHashSet();
        Assert.True(kinds.Count >= 45, $"only {kinds.Count} kinds of node found");
        Assert.Empty(kinds.Except(ThrowSets.Kinds.Keys).Select(t => t.Name));
        Assert.Empty(ThrowSets.Kinds.Keys.Except(kinds).Select(t => t.Name)); // and no line for a kind the tree no longer has
    }

    [Fact]
    public void Every_corpus_case_read_without_a_diagnostic_is_walked_whole()
    {
        var cases = 0;
        var units = 0;
        foreach (var file in CorpusTests.CaseFiles())
        {
            var (header, tree, frontEnd) = CorpusTests.ReadCase(file);
            if (frontEnd.HasErrors)
            {
                continue;
            }

            var diagnostics = new DiagnosticBag();
            var checker = Checker.Check(tree, diagnostics, header.Grants, CorpusTests.StdChecker.Value);
            if (diagnostics.Items.Count > 0)
            {
                continue;
            }

            var sets = new ThrowSets(checker, CorpusTests.StdChecker.Value);
            var reached = new List<IReadOnlyList<KurzType>>();
            reached.AddRange(checker.Model.Functions.Select(sets.RaisedBy));
            reached.AddRange(checker.Model.Classes.Where(c => c.IsActor).Select(sets.ReasonOf));
            reached.AddRange(sets.PubFunctions().Values);
            Assert.All(reached, set => Assert.Equal("RuntimeError", Name(set[^1])));
            cases++;
            units += sets.Walked;
        }

        output.WriteLine($"{cases} corpus cases walked, {units} units of code read");
        Assert.True(cases >= CaseFloor, $"only {cases} corpus cases are read without a diagnostic");
        Assert.True(units >= UnitFloor, $"only {units} units of code are read");
    }

    [Fact]
    public void Every_pub_function_of_std_is_walked_and_its_throws_are_listed()
    {
        var std = CorpusTests.StdChecker.Value;
        var listed = new ThrowSets(std, null).PubFunctions();
        output.WriteLine($"{listed.Count} pub functions of std/");
        Assert.True(listed.Count >= 160, $"only {listed.Count} pub functions of std/ are listed");

        // `HttpResponse.Stream` throws its text for a status with no body, and `HttpError`'s Text() gives its detail and throws nothing
        Assert.Contains("string", Names(listed.Single(p => p.Key.Name == "Stream" && p.Key.Owner?.Name == "HttpResponse").Value));
        Assert.Equal(["RuntimeError"], Names(listed.Single(p => p.Key.Name == "Text" && p.Key.DataOwner?.Name == "HttpError").Value));
    }

    [Fact]
    public void The_runtime_throws_nothing()
    {
        // the sets read a function of a folder of the runtime as code that throws nothing: every function of the runtime, of each
        // target and architecture, raises the runtime's own errors alone, with what it reaches in std/
        var functions = 0;
        foreach (var (target, arch) in Enum.GetValues<Target>().SelectMany(t => Enum.GetValues<Arch>().Select(a => (t, a))))
        {
            var runtime = Builder.CheckRuntime(target, arch);
            foreach (var checker in new[] { runtime.CommonChecker, runtime.SysChecker, runtime.ArchChecker })
            {
                var sets = new ThrowSets(checker, runtime.StdChecker);
                foreach (var function in checker.Model.Functions)
                {
                    Assert.Equal(["RuntimeError"], Names(sets.RaisedBy(function)));
                    functions++;
                }
            }
        }

        Assert.True(functions >= 1000, $"only {functions} functions of the runtime are read");
    }

    [Fact]
    public void The_runtime_s_error_type_has_a_kind_for_each_run_time_row_of_the_error_table()
    {
        // section 5, assumed there: one kind for each run-time id of the reference's chapter 12 but `thrown`, a `throw` of the program,
        // in the order of the table, each id in Pascal case
        var lines = File.ReadAllLines(Path.Combine(CorpusTests.Root, "reference", "12-errors.md"));
        var start = Array.IndexOf(lines, "## Run-time errors");
        Assert.True(start >= 0, "the table of run-time errors has no heading");
        var ids = lines.Skip(start + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => Regex.Match(line, "^\\| `([a-z-]+)` \\|"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Contains("thrown", ids); // the positive control of the one id left out
        Assert.True(ids.Count >= 15, $"only {ids.Count} run-time rows read");
        var kinds = ids.Where(id => id != "thrown")
            .Select(id => string.Concat(id.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..])))
            .ToList();

        var error = CorpusTests.StdChecker.Value.Model.DataTypes.Single(d => d.Name == "RuntimeError");
        Assert.Equal(["Kind", "Detail"], error.Fields.Select(f => f.Name));
        var kind = Assert.IsType<EnumType>(error.Fields[0].Type);
        Assert.True(Types.Same(Types.String, error.Fields[1].Type), $"the detail is {error.Fields[1].Type}");
        Assert.Equal(kinds, kind.Symbol.Declaration.Values.Select(v => v.Name));
    }
}
