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
        data EAnswered
        data EClause
        data EEscalate
        data ERestarted
        data ETypeEscalate
        data EBlocked
        data EInBlock
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
        data ERefused
        data EReadAgain
        data EDefault
        data EWithDerived
        data EDerivedText
        data EDerivedDataText
        data EInterfaceText
        data EMapKeyText
        data EMapValueText
        data EUnionText
        data EWeakText
        data EKept
        data EComparedText
        data EKeyedText
        data ECheckText

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

            pub int Get() {
                throw EAnswered
            }
        }

        void ViaActorCall() {
            a = spawn Answering()
            send a.Fail()
            x = a.Get()
            print(x)
        }

        duration ClauseBound() {
            throw EClause
        }

        void ViaClause() {
            a = spawn Answering()
            print(a.Get() within ClauseBound())
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

        actor Blocked {
            pub void Fail() {
                throw EBlocked
            }
        }

        actor Deciding {
            pub void Make() {
                b = spawn Blocked() on crash (c) {
                    if c.Place.Line > 1 {
                        throw EInBlock
                    }

                    stop
                }
                send b.Fail()
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

        data Pair(int A)

        pub void Refuse<T>(T value) {
            for f in T.Fields {
                print("{f.Name} = {f.Get(value)}")
                throw ERefused
            }
        }

        void ReadAgain<T>(T value) {
            for f in T.Fields {
                print("{f.Name} = {f.Get(value)}")
                throw EReadAgain
            }
        }

        void ViaCopy() {
            ReadAgain(Pair(1))
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

        int DefaultArea(int n) {
            throw EDefault
        }

        data Sizing(int N) {
            pub int Area = DefaultArea(N)
        }

        int ViaDefault(Sizing s = Sizing(1)) => s.N

        class Fitted(int N) {
            pub Fitted(Sizing s = Sizing(2)) : this(s.N) { }
        }

        Fitted ViaConstructorDefault() => Fitted()

        class Framed(Sizing S = Sizing(3))

        Framed ViaClassDefault() => Framed()

        data Padded(Sizing S = Sizing(4))

        Padded ViaDataDefault() => Padded()

        int RaisedArea(int h) {
            throw EWithDerived
        }

        data Plane(int W)

        data Raised(int H) : Plane {
            pub int Area = RaisedArea(H)
        }

        Plane ViaWithDerived(Plane p) => p with { W = 3 }

        class Creature(int N) {
            pub virtual string Text() => "creature"
        }

        class Hound(int Legs) : Creature {
            pub override string Text() {
                throw EDerivedText
            }
        }

        void ViaDerivedText(Creature c) {
            print(c)
        }

        data Note(int N)

        data Memo(int M) : Note {
            pub string Text() {
                throw EDerivedDataText
            }
        }

        void ViaDerivedDataText(Note n) {
            print(n)
        }

        interface Labelled {
            string Text()
        }

        class Label(int N) : Labelled {
            pub string Text() {
                throw EInterfaceText
            }
        }

        void ViaInterfaceText(Labelled l) {
            print(l)
        }

        data MapKey(int N) {
            pub string Text() {
                throw EMapKeyText
            }
        }

        class MapValue(int N) {
            pub string Text() {
                throw EMapValueText
            }
        }

        void ViaMapKeyText(Map<MapKey, int> m) {
            print(m)
        }

        void ViaMapValueText(Map<string, MapValue> m) {
            print(m)
        }

        class Either(int N) {
            pub string Text() {
                throw EUnionText
            }
        }

        void ViaUnionText(int | Either u) {
            print(u)
        }

        class Kept(int N) {
            pub string Text() {
                throw EWeakText
            }
        }

        data Keeping(weak Kept? K)

        void ViaWeakText(Keeping k) {
            print(k)
        }

        data CheckedSide(int N) {
            pub string Text() {
                throw ECheckText
            }
        }

        void ViaCheck(CheckedSide a) {
            check a == CheckedSide(1)
        }

        data Picked(int N)

        bool KeepPicked(Picked p) {
            throw EKept
        }

        int ViaWhereValue(List<Picked> xs, (Picked) => bool keep) => xs.Where(keep).Count

        int CountPicked(List<Picked> xs) => ViaWhereValue(xs, p => KeepPicked(p))

        data Compared(int N) {
            pub string Text() {
                throw EComparedText
            }
        }

        class Keyed(int Id) equal by Id {
            pub string Text() {
                throw EKeyedText
            }
        }

        bool ViaEquality(Compared a, Compared b) => a == b

        bool ViaEqualBy(Keyed a, Keyed b) => a == b

        int ViaHash(Keyed k) {
            mut counts = Map<Keyed, int>()
            counts[k] = 1
            return counts[k] ?? 0
        }

        void ViaCompared(Compared c, Keyed k) {
            print(c)
            print(k)
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

    /// <summary>A program of its own, which the checker reads without a diagnostic, with its sets.</summary>
    private static (ProgramSyntax Tree, Checker Checker, ThrowSets Sets) Own(string text, params string[] grants)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", text), diagnostics);
        var checker = Checker.Check(tree, diagnostics, grants, CorpusTests.StdChecker.Value);
        Assert.Empty(diagnostics.Items);
        return (tree, checker, new ThrowSets(checker, CorpusTests.StdChecker.Value));
    }

    private static List<string> RaisedIn((ProgramSyntax Tree, Checker Checker, ThrowSets Sets) own, string function) =>
        Names(own.Sets.RaisedBy(own.Checker.Model.FunctionOf(own.Tree.Statements.OfType<FunctionDeclaration>().Single(f => f.Name == function))));

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
    [InlineData("ViaWithDerived", "EWithDerived")] // a field of the body of a type derived from the one `with` copies, computed again (D6, D16)
    [InlineData("ViaDefault", "EDefault")] // the default of a function's parameter, walked with the function that declares it (F12)
    [InlineData("ViaConstructorDefault", "EDefault")] // the default of a parameter of a constructor of a class's body (F12, K14)
    [InlineData("ViaClassDefault", "EDefault")] // the default of a parameter of a class's primary constructor (F12, D11)
    [InlineData("ViaDataDefault", "EDefault")] // the default of a field of a data type's primary constructor (F12, D11)
    [InlineData("ViaWhere", "EWhere")] // a lambda a built-in call takes (M9)
    [InlineData("ViaInterpolation", "EInterpolation")] // the Text() of a hole (L6, A5)
    [InlineData("ViaPrint", "EPrint")] // the Text() of what `print` writes (A1, A5)
    [InlineData("ViaJoin", "EJoin")] // the Text() of each item `string.Join` joins (X6)
    [InlineData("ViaNested", "ENested")] // the Text() of a field of a data type in a list of nullable values (A3, A5)
    [InlineData("ViaDerivedText", "EDerivedText")] // the Text() of a class derived from the type printed (A9)
    [InlineData("ViaDerivedDataText", "EDerivedDataText")] // the Text() of a data type derived from the type printed (A5, D6)
    [InlineData("ViaInterfaceText", "EInterfaceText")] // the Text() of a class that implements the interface printed (A10)
    [InlineData("ViaMapKeyText", "EMapKeyText")] // the Text() of the keys of a map (A6)
    [InlineData("ViaMapValueText", "EMapValueText")] // the Text() of the values of a map (A6)
    [InlineData("ViaUnionText", "EUnionText")] // the Text() of a case of a union (A4)
    [InlineData("ViaWeakText", "EWeakText")] // the Text() of the target of a `weak` field of a data type without its own Text() (A3, R3)
    [InlineData("ViaCheck", "ECheckText")] // the Text() of each side of a comparison whose failure a `check` shows (Y6, F10)
    [InlineData("ViaSpawn", "ESpawn")] // the construction of an actor a spawn makes, whose throw is the spawner's crash (U3)
    [InlineData("ViaClause", "EClause")] // the bound of a call's clause, which runs in the caller (U39)
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
    public void A_built_in_call_given_a_function_value_reaches_the_lambdas_whose_type_fits()
    {
        // M9, X8: `xs.Where(keep)` is given no lambda, so it reaches every lambda whose type fits the value's, the one `CountPicked` passes
        var raised = Raised("ViaWhereValue");
        Assert.Equal("EKept", raised[0]);
        Assert.Equal("RuntimeError", raised[^1]);
        Assert.DoesNotContain("EWhere", raised); // a lambda of another parameter type does not fit
    }

    [Theory]
    [InlineData("ViaEquality")] // `==` on two data values compares their fields (D3)
    [InlineData("ViaEqualBy")] // `==` on two instances of a class with `equal by` compares the fields it names (K5, K8)
    [InlineData("ViaHash")] // a key of a map is hashed from the fields `==` reads (K5)
    public void Equality_and_a_hash_run_no_code_of_the_program(string function)
    {
        // K5, D16: a type declares no `Equals` and no `GetHashCode` (`equals-method`); the positive control: a `print` of the same
        // values reaches their Text(), which the comparison and the hash do not run
        Assert.Equal(["EComparedText", "EKeyedText", "RuntimeError"], Raised("ViaCompared"));
        Assert.Equal(["RuntimeError"], Raised(function));
    }

    [Fact]
    public void A_call_through_an_actor_s_reference_and_the_text_of_a_thrown_value_add_nothing()
    {
        // U20: the callee's crash reaches its caller as `callee-crashed`, a runtime error, and none of its code runs in the caller,
        // through a call that waits for its reply, `a.Get()`, as through a `send`
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
    public void A_function_that_reads_its_type_runs_as_its_copies_alone()
    {
        // T33: the copy for `Pair` runs in the place of `ReadAgain`, its loop as one pass for each field
        Assert.Equal(["EReadAgain", "RuntimeError"], Raised("ViaCopy"));

        // a function that reads its type and that no call copies runs nowhere, and its text adds nothing, so the list of such a `pub`
        // function holds the runtime's type alone, as Rust makes no code of a generic function that nothing instantiates (section 5,
        // the owner, 2026-11-17, #560, comment 6945856976, Q2)
        var listed = Program.Value.Sets.PubFunctions();
        Assert.Equal(["RuntimeError"], Names(listed[Assert.Single(listed.Keys, f => f.Name == "Refuse")]));
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
    public void The_text_of_a_generic_type_derived_from_the_printed_one_is_the_text_of_each_instantiation_the_program_builds()
    {
        // section 5, the owner, 2026-11-16, "Each instantiation": the top-level code builds `Sub<int>` and `Sub<Loud>`, the `pub`
        // function `Listed` builds `Sub<Wrapped>` through the type argument its call gives `Wrap`, and `Sub<Handed>` through the lambda
        // that `Maker` makes for its type argument, which `Listed` calls as a function value, and the generic `pub` function `Generic`
        // builds `Sub<Quiet>`, with every type argument named whatever type argument a call gives it, whose `EQuiet` a call of it raises
        // (section 5, "Compiler lists them"); `Sub<Silent>` stands in a function no root reaches, so the program does not build it, as
        // Rust's monomorphization builds nothing for a function nothing calls
        const string text = """
            data ELoud
            data EWrapped
            data EHanded
            data EQuiet
            data ESilent
            data EMade

            data Plain(int N)

            data Sub<U>(U X) : Plain

            class Loud(int N) {
                pub string Text() {
                    throw ELoud
                }
            }

            class Wrapped(int N) {
                pub string Text() {
                    throw EWrapped
                }
            }

            class Handed(int N) {
                pub string Text() {
                    throw EHanded
                }
            }

            class Quiet(int N) {
                pub string Text() {
                    throw EQuiet
                }
            }

            class Silent(int N) {
                pub string Text() {
                    throw ESilent
                }
            }

            void Show(Plain p) {
                print(p)
            }

            Plain Wrap<T>(T x) => Sub<T>(1, x)

            () => Plain Maker<T>(T x) => () => Sub<T>(1, x)

            void Unreached() {
                Show(Sub<Silent>(1, Silent(2)))
            }

            pub void Generic<T>(T x) {
                Show(Sub<Quiet>(1, Quiet(3)))
            }

            int Fail() {
                throw EMade
            }

            () => int Deferred() => () => Fail()

            int Called() {
                f = Deferred()
                return f()
            }

            pub void Listed() {
                Show(Wrap(Wrapped(3)))
                made = Maker(Handed(4))
                Show(made())
            }

            Show(Sub<int>(1, 2))
            Show(Sub<Loud>(1, Loud(2)))

            """;
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", text), diagnostics);
        var checker = Checker.Check(tree, diagnostics, [], CorpusTests.StdChecker.Value);
        Assert.Empty(diagnostics.Items);
        var sets = new ThrowSets(checker, CorpusTests.StdChecker.Value);
        FunctionSymbol Named(string name) => checker.Model.FunctionOf(tree.Statements.OfType<FunctionDeclaration>().Single(f => f.Name == name));
        Assert.Equal(["ELoud", "EWrapped", "EHanded", "EQuiet", "RuntimeError"], Names(sets.RaisedBy(Named("Show"))));
        var listed = sets.PubFunctions();
        Assert.Equal(["ELoud", "EWrapped", "EHanded", "EQuiet", "RuntimeError"], Names(listed[Assert.Single(listed.Keys, f => f.Name == "Generic")]));

        // F11: the unit that makes a lambda takes what it builds and nothing it throws, which a call of the function value brings
        Assert.Equal(["RuntimeError"], Names(sets.RaisedBy(Named("Deferred"))));
        Assert.Equal(["EMade", "RuntimeError"], Names(sets.RaisedBy(Named("Called"))));
    }

    [Fact]
    public void An_instantiation_built_only_in_the_text_of_another_is_built()
    {
        // section 5: the text of `Sub<Loud>` runs `Loud`'s Text(), which is no root and builds `Sub<Wrapped>`, so the text of each
        // instantiation found is read and the roots solved again until the list grows no more
        const string text = """
            data EWrapped

            data Plain(int N)

            data Sub<U>(U X) : Plain

            class Wrapped(int N) {
                pub string Text() {
                    throw EWrapped
                }
            }

            class Loud(int N) {
                string Text() {
                    Show(Sub<Wrapped>(1, Wrapped(2)))
                    return "loud"
                }
            }

            pub void Show(Plain p) {
                print(p)
            }

            Show(Sub<Loud>(1, Loud(2)))

            """;
        Assert.Equal(["EWrapped", "RuntimeError"], RaisedIn(Own(text), "Show"));
    }

    [Fact]
    public void A_text_reads_each_instantiation_of_one_generic_type_it_holds()
    {
        // section 5: `Two` holds a `Box<int>` and a `Box<Quiet>`, and its text reads both, `Quiet`'s Text() through the second
        const string text = """
            data EQuiet

            data Box<T>(T V)

            class Quiet(int N) {
                pub string Text() {
                    throw EQuiet
                }
            }

            data Two(Box<int> A, Box<Quiet> B)

            pub void Show(Two t) {
                print(t)
            }

            """;
        Assert.Equal(["EQuiet", "RuntimeError"], RaisedIn(Own(text), "Show"));
    }

    [Fact]
    public void An_instantiation_built_only_in_a_pub_method_of_an_actor_is_built()
    {
        // U9, section 5: a message reaches a `pub` method alone, and each is a root of what the program builds, so the text of
        // `Sub<Shouty>`, built in `Go` alone, is in the actor's `Reason`; `Sub<Hushed>`, built in a method nothing calls, is built nowhere
        const string text = """
            data EShouty
            data EHushed

            data Plain(int N)

            data Sub<U>(U X) : Plain

            class Shouty(int N) {
                pub string Text() {
                    throw EShouty
                }
            }

            class Hushed(int N) {
                pub string Text() {
                    throw EHushed
                }
            }

            void Show(Plain p) {
                print(p)
            }

            actor Printer {
                pub void Go() {
                    Show(Sub<Shouty>(1, Shouty(1)))
                }

                void Muted() {
                    Show(Sub<Hushed>(1, Hushed(1)))
                }
            }

            printer = spawn Printer()
            send printer.Go()

            """;
        var own = Own(text);
        var printer = own.Checker.Model.ClassOf(own.Tree.Statements.OfType<TypeDeclaration>().Single(t => t.Name == "Printer"));
        Assert.Equal(["EShouty", "RuntimeError"], Names(own.Sets.ReasonOf(printer)));
    }

    [Fact]
    public void A_call_of_a_C_function_value_reaches_the_functions_named_as_values_of_its_type()
    {
        // R17: a function of the C calling convention named as a value is a value of a C function type, and a call through it runs such
        // a function whose type is the value's; `Other`, named as a value of another type, does not fit `Call`'s call
        const string text = """
            data EHook
            data EOther

            extern "C" long Twice(long x) {
                throw EHook
            }

            extern "C" int Other(int x) {
                throw EOther
            }

            pub void Call() {
                extern "C" (long) => long f = Twice
                raw {
                    print(f(4))
                }
            }

            pub void Keep() {
                extern "C" (int) => int g = Other
                raw {
                    print(g(1))
                }
            }

            """;
        var own = Own(text, "raw", "native");
        Assert.Equal(["EHook", "RuntimeError"], RaisedIn(own, "Call"));
        Assert.Equal(["EOther", "RuntimeError"], RaisedIn(own, "Keep"));
    }

    [Fact]
    public void A_built_in_call_that_keeps_a_lambda_runs_none_of_it()
    {
        // M9, X8: `Where` calls its test and `SortBy` its key, while `Add` (M9) and `Insert` (X11) keep a lambda, whose throw a call of the
        // value it became brings where it is called
        const string text = """
            data EStored
            data EKey

            int Fail() {
                throw EStored
            }

            int Key(int x) {
                throw EKey
            }

            pub void Keep() {
                mut handlers = List<() => int>()
                handlers.Add(() => Fail())
                handlers.Insert(0, () => Fail())
                print(handlers.Count)
            }

            pub void Run() {
                mut handlers = List<() => int>()
                handlers.Add(() => Fail())
                print(handlers[0]())
            }

            pub void Sorted() {
                mut xs = [3, 1]
                xs.SortBy(x => Key(x))
                print(xs)
            }

            """;
        var own = Own(text);
        Assert.Equal(["RuntimeError"], RaisedIn(own, "Keep"));
        Assert.Equal(["EStored", "RuntimeError"], RaisedIn(own, "Run"));
        Assert.Equal(["EKey", "RuntimeError"], RaisedIn(own, "Sorted"));
    }

    [Fact]
    public void A_built_in_call_no_arm_names_counts_as_calling_the_function_value_it_takes()
    {
        // a kind of built-in call that takes a function value and that the walk names neither as one that calls it nor as one that keeps
        // it, `Hint.BlackBox`, which gives the value back (F18), counts as calling it, so that a kind nobody classified counts what the
        // function throws, too much rather than too little
        const string text = """
            data EBoxed

            int Fail() {
                throw EBoxed
            }

            () => int Deferred() => () => Fail()

            pub () => int Boxed() => Hint.BlackBox(Deferred())

            """;
        Assert.Equal(["EBoxed", "RuntimeError"], RaisedIn(Own(text), "Boxed"));
    }

    [Fact]
    public void The_case_of_a_throw_in_the_text_of_an_instantiation_raises_what_that_text_throws()
    {
        // O7, A3, A5, D6: the case's `Show` prints a `Plain` that the top-level code builds as a `Sub<Loud>`, whose text throws
        // `TextFailed`; the corpus runner does not run the case (`NotEmitted` in `RunTests`)
        var (header, tree, frontEnd) = CorpusTests.ReadCase("outcomes/throw-in-text-of-instantiation.kz");
        Assert.Empty(frontEnd.Items);
        var diagnostics = new DiagnosticBag();
        var checker = Checker.Check(tree, diagnostics, header.Grants, CorpusTests.StdChecker.Value);
        Assert.Empty(diagnostics.Items);
        var show = checker.Model.FunctionOf(tree.Statements.OfType<FunctionDeclaration>().Single(f => f.Name == "Show"));
        Assert.Equal(["TextFailed", "RuntimeError"], Names(new ThrowSets(checker, CorpusTests.StdChecker.Value).RaisedBy(show)));
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

        // section 5, the owner, 2026-11-17, #560, comment 6945856976, Q1: the list of a `pub` function holds what the children it spawns
        // under `escalate` or a block that decides can pass up, and what such a block throws, since such a crash can come while a call of
        // it still waits; this test holds the half of `escalate`
        var listed = Program.Value.Sets.PubFunctions();
        Assert.Equal(["EEscalate", "ETypeEscalate", "RuntimeError"], Names(listed[Assert.Single(listed.Keys, f => f.Name == "Make" && f.Owner?.Name == "Parent")]));
    }

    [Fact]
    public void The_reason_of_an_actor_holds_what_the_block_of_its_spawn_throws_and_what_it_can_escalate()
    {
        // U28, U32: a block can pass the child's crash up whole with `escalate`, and its throw is the crash of the actor whose code spawns;
        // U30 counts "the code of each actor they spawn under `escalate` or a block, whose crash can pass up to the child", every block,
        // since any block can `escalate`
        Assert.Equal(["EBlocked", "EInBlock", "RuntimeError"], Names(Program.Value.Sets.ReasonOf(Actor("Deciding"))));
        Assert.Equal(["EBlocked", "RuntimeError"], Names(Program.Value.Sets.ReasonOf(Actor("Blocked"))));
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
        // in the order of the table, each id in Pascal case, and after them `invalid-argument` (S13), which the chapter names outside its
        // table and an actor's read of the command line raises (U30; assumed: its place after the table's)
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
        Assert.DoesNotContain("invalid-argument", ids); // the chapter names it outside its table
        ids = [.. ids.Where(id => id != "thrown"), "invalid-argument"];
        var kinds = ids.Select(id => string.Concat(id.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]))).ToList();

        var error = CorpusTests.StdChecker.Value.Model.DataTypes.Single(d => d.Name == "RuntimeError");
        Assert.Equal(["Kind", "Detail"], error.Fields.Select(f => f.Name));
        var kind = Assert.IsType<EnumType>(error.Fields[0].Type);
        Assert.True(Types.Same(Types.String, error.Fields[1].Type), $"the detail is {error.Fields[1].Type}");
        Assert.Equal(kinds, kind.Symbol.Declaration.Values.Select(v => v.Name));

        // U30: std's `RuntimeErrorOf`, which the emitted block of `on crash` makes the runtime's error with, names each id of the table,
        // and `invalid-argument`, with its kind, in that order
        var std = File.ReadAllText(Path.Combine(CorpusTests.Root, "std", "std.kz"));
        var of = std[std.IndexOf("pub RuntimeError RuntimeErrorOf(", StringComparison.Ordinal)..];
        of = of[..of.IndexOf("\n}\n", StringComparison.Ordinal)];
        var arms = Regex.Matches(of, "\"([a-z-]+)\" => RuntimeKind\\.([A-Za-z]+)").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.Equal(ids.Zip(kinds).ToList(), arms);
    }
}
