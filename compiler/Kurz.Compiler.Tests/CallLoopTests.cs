using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Layer 2 of section 6's deadlocks (U56 to U59; assumed: T40 to T59 of issue 519, comment 7007991607, and T60 and T61 of issue 519,
/// comment 7009683539, built at the lean while it waits for the owner): the line of each <c>call-loop</c> and how many a program gets,
/// the loop its text names, read by the lines it names, the word <c>vouch</c> as the parser reads it, the lint <c>vouch-unneeded</c>,
/// and the walk that reads each unit of code once. Every error is pinned by its id and its line, never by its wording.
/// </summary>
public sealed partial class CallLoopTests(Toolchain toolchain) : IClassFixture<Toolchain>
{
    private static (DiagnosticBag Diagnostics, Checker Checker) Check(string program)
    {
        var diagnostics = new DiagnosticBag();
        var tree = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        return (diagnostics, Checker.Check(tree, diagnostics));
    }

    private static List<(string Id, int Line)> Diagnostics(string program) =>
        Check(program).Diagnostics.Sorted.Select(d => (d.Id, d.Line)).ToList();

    // the lines a `call-loop`'s text names, one for each hop of its loop, in the order of the loop
    private static List<int> Named(Diagnostic error) => HopLine().Matches(error.Message).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

    [GeneratedRegex(@"\(line (\d+)")]
    private static partial Regex HopLine();

    [Fact]
    public void A_loop_of_three_types_is_one_error_at_its_call_that_stands_first_in_source_order()
    {
        // T52: the types are declared in the reverse order of the loop, so that a walk from the first type declared or from the first
        // call the top-level code makes would start elsewhere; the error stands at line 2, the first call of the group in the file, and
        // its text goes round the loop from there
        const string program = """
            actor C {
                pub int Go(A a) => a.Value() + 3

                pub int Value() => 3
            }

            actor B {
                pub int Go(C c) => c.Value() + 2

                pub int Value() => 2
            }

            actor A {
                pub int Go(B b) => b.Value() + 1

                pub int Value() => 1
            }

            theA = spawn A()
            theB = spawn B()
            theC = spawn C()
            print(theA.Go(theB) + theB.Go(theC) + theC.Go(theA))

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([2, 14, 8], Named(errors[0]));
    }

    [Fact]
    public void The_text_names_the_shortest_loop_through_the_first_call()
    {
        // T52: `A.Go` at line 2 is on a loop of two types, back through `B.Near` (line 12), and on one of four, through `B.Far`, `C.Far`
        // and `D.Far` (lines 10, 18 and 24), whose first hop out of `B` stands first in source order: the text names the loop of two
        const string program = """
            actor A {
                pub int Go(B b) => b.Value() + 1

                pub int Value() => 1
            }

            actor B {
                pub int Value() => 2

                pub int Far(C c) => c.Value()

                pub int Near(A a) => a.Value()
            }

            actor C {
                pub int Value() => 3

                pub int Far(D d) => d.Value()
            }

            actor D {
                pub int Value() => 4

                pub int Far(A a) => a.Value()
            }

            theA = spawn A()
            theB = spawn B()
            theC = spawn C()
            theD = spawn D()
            print(theA.Go(theB) + theB.Far(theC) + theB.Near(theA) + theC.Far(theD) + theD.Far(theA))

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([2, 12], Named(errors[0]));
    }

    [Fact]
    public void A_group_of_types_that_reach_one_another_is_one_error_and_two_groups_are_two()
    {
        // T52: `A` waits on `B` and on `C`, which both wait on `A`: two loops in one group, one error at line 2; two groups apart, `A`
        // and `B`, `C` and `D`, are an error each, at the first call of each
        const string one = """
            actor A {
                pub int ToB(B b) => b.Value()

                pub int ToC(C c) => c.Value()

                pub int Value() => 1
            }

            actor B {
                pub int ToA(A a) => a.Value()

                pub int Value() => 2
            }

            actor C {
                pub int ToA(A a) => a.Value()

                pub int Value() => 3
            }

            theA = spawn A()
            theB = spawn B()
            theC = spawn C()
            print(theA.ToB(theB) + theA.ToC(theC) + theB.ToA(theA) + theC.ToA(theA))

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(one));
        const string two = """
            actor A {
                pub int ToB(B b) => b.Value()

                pub int Value() => 1
            }

            actor B {
                pub int ToA(A a) => a.Value()

                pub int Value() => 2
            }

            actor C {
                pub int ToD(D d) => d.Value()

                pub int Value() => 3
            }

            actor D {
                pub int ToC(C c) => c.Value()

                pub int Value() => 4
            }

            theA = spawn A()
            theB = spawn B()
            theC = spawn C()
            theD = spawn D()
            print(theA.ToB(theB) + theB.ToA(theA) + theC.ToD(theD) + theD.ToC(theC))

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2), (ErrorIds.CallLoop, 14)], Diagnostics(two));
    }

    [Fact]
    public void A_loop_is_reported_beside_the_other_errors_and_a_call_with_its_own_error_makes_no_edge()
    {
        // T54: the loop of `Left` and `Right` and a name nothing declares, each at its line; a call that reported its own error makes no
        // edge and runs none of the callee's code as the caller's, which would make the wait in `Hidden` one of `Left` on itself: a call
        // of a method that is no `pub` one (U9), which the checker records no call for, and a call with a `mut` argument, which it records
        // as the method it picked; the same calls without their errors close the loop (the controls)
        const string beside = """
            actor Left {
                pub int Ask(Right other) => other.Value() + 1

                pub int Value() => 1
            }

            actor Right {
                pub int Ask(Left other) => other.Value() + 2

                pub int Value() => 2
            }

            print(missing)

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2), (ErrorIds.UnknownName, 13)], Diagnostics(beside));
        const string own = """
            actor Left {
                pub int Ask(Right other) => other.Hidden() + 1

                pub int Value() => 1
            }

            actor Right(Left Back) {
                int Hidden() => Back.Value()
            }

            l = spawn Left()
            r = spawn Right(l)
            print(l.Ask(r))

            """;
        Assert.Equal([(ErrorIds.NotVisible, 2)], Diagnostics(own));
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(own.Replace("    int Hidden()", "    pub int Hidden()", StringComparison.Ordinal)));
        const string picked = """
            actor Left {
                pub int Ask(Right other) {
                    mut k = 1
                    return other.Hidden(mut k) + k
                }

                pub int Value() => 1
            }

            actor Right(Left Back) {
                pub int Hidden(mut int n) => Back.Value() + n
            }

            l = spawn Left()
            r = spawn Right(l)
            print(l.Ask(r))

            """;
        Assert.Equal([(ErrorIds.MutInMessage, 4)], Diagnostics(picked));
        var fine = picked.Replace("mut k = 1", "k = 1", StringComparison.Ordinal).Replace("Hidden(mut k)", "Hidden(k)", StringComparison.Ordinal)
            .Replace("Hidden(mut int n)", "Hidden(int n)", StringComparison.Ordinal);
        Assert.Equal([(ErrorIds.CallLoop, 4)], Diagnostics(fine));
    }

    [Fact]
    public void A_local_with_mut_that_holds_this_is_no_call_back_to_the_caller()
    {
        // U57: a local without `mut` that holds `this` is the actor itself (`call-self-through-this.kz`, the control); one with `mut` can
        // hold another `Counter` by the time of the call, so that its call is an edge, a loop through one type, at line 8
        const string program = """
            actor Counter {
                mut int count = 0

                pub int Next() {
                    mut me = this
                    me = this
                    count = count + 1
                    return me.Get() + 100
                }

                pub int Get() => count
            }

            c = spawn Counter()
            print(c.Next())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 8)], Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace("        mut me = this\n        me = this\n", "        me = this\n", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_method_its_own_actor_calls_directly_reads_its_calls_with_what_its_callers_pass()
    {
        // U57: `B.Peek` is called by no call through a reference, only by `B.Check` directly (U10), which `A.Save` calls with `this`: the
        // call through `Peek`'s parameter reaches the caller that waits, and the program has no error; a top-level call of `Peek` through
        // a reference with another `A` makes it an edge, at line 2, the first call of the loop
        const string program = """
            actor A(B Other) {
                pub int Save() => Other.Check(this) + 1

                pub int Load() => 7
            }

            actor B {
                pub int Peek(A x) => x.Load()

                pub int Check(A x) => Peek(x) * 10
            }

            b = spawn B()
            a = spawn A(b)
            print(a.Save())

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program + "print(b.Peek(spawn A(b)))\n"));
    }

    [Fact]
    public void A_reference_handed_on_through_calls_that_wait_is_a_callback_read_to_the_least_fixed_point()
    {
        // U57, T60: `Store.Save` hands `this` to `Checker.Valid`, which hands it on to `Verifier.Verify`, and that to `Reader.Read`, each
        // by a call that waits: each parameter is a callback, read again round by round, the types declared from the last callee to the
        // first caller, so that a round of the fixed point takes the chain one step; a top-level call of `Valid` with another `Store`
        // makes every parameter of the chain none, and the loop of four types is one error, at line 2
        const string program = """
            actor Reader {
                pub int Read(Store store) => store.Load() + 1
            }

            actor Verifier(Reader R) {
                pub int Verify(Store store) => R.Read(store) * 10
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store) => V.Verify(store) * 10
            }

            actor Store(Checker Check) {
                pub int Save() => Check.Valid(this) + 1

                pub int Load() => 7
            }

            r = spawn Reader()
            v = spawn Verifier(r)
            c = spawn Checker(v)
            s = spawn Store(c)
            print(s.Save())

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program + "print(c.Valid(spawn Store(c)))\n"));
    }

    [Fact]
    public void A_reference_a_send_hands_on_is_no_callback()
    {
        // U57, T60: `Checker.Valid` hands the `Store` it was given on to `Verifier.Verify` by a `send`, which gives no callback (T49), so
        // that `Verify`'s call of `Load` is an edge, and the loop of three types, through `Valid`'s wait on `Verifier.Count`, is one
        // error, at line 2; the same call made as one that waits hands the callback on, and the program has no error
        const string program = """
            actor Store(Checker Check) {
                pub int Save() => Check.Valid(this) + 1

                pub int Load() => 7
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store) {
                    send V.Verify(store)
                    return V.Count()
                }
            }

            actor Verifier {
                pub void Verify(Store store) {
                    print(store.Load())
                }

                pub int Count() => 3
            }

            v = spawn Verifier()
            c = spawn Checker(v)
            s = spawn Store(c)
            print(s.Save())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace("send V.Verify(store)", "V.Verify(store)", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_cycle_of_parameters_that_no_this_fills_is_no_callback()
    {
        // U57, T60: `Checker.Valid` and `Verifier.Verify` call each other back through the reference each passes as `this`, and hand the
        // `Store` on to each other, which no caller fills with `this`: the least fixed point leaves both parameters none, so that
        // `Valid`'s call of `Load` is an edge, and the loop with `Store`'s call of `Checker.Size` is one error, at line 2; without that
        // call of `Load` the program has no error
        const string program = """
            actor Store(Checker Check) {
                pub int Load() => Check.Size() + 1
            }

            actor Checker {
                pub int Valid(Verifier v, Store store) => v.Verify(this, store) + store.Load()

                pub int Size() => 3
            }

            actor Verifier {
                pub int Verify(Checker c, Store store) => c.Valid(this, store)
            }

            k = spawn Checker()
            st = spawn Store(k)
            print(st.Load())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace(" + store.Load()", "", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_caller_in_a_function_the_top_level_code_runs_too_hands_on_no_callback()
    {
        // U57, T60: `Relay` passes `Checker.Valid` its parameter, which `Store.Save` fills with `this`, so that the reference handed on is
        // a callback and the program has no error; a top-level call of `Relay`, code outside the actors' messages, makes it none, and so
        // does a `Save` that passes `Relay` another `Store`: the loop of three types is one error, at line 1, in `Relay`
        const string program = """
            int Relay(Checker c, Store s) => c.Valid(s)

            actor Store(Checker Check) {
                pub int Save() => Relay(Check, this) + 1

                pub int Load() => 7
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store) => V.Verify(store) * 10
            }

            actor Verifier {
                pub int Verify(Store store) => store.Load() * 10
            }

            ver = spawn Verifier()
            chk = spawn Checker(ver)
            sto = spawn Store(chk)
            print(sto.Save())

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Equal([(ErrorIds.CallLoop, 1)], Diagnostics(program + "print(Relay(chk, sto))\n"));
        var other = program.Replace("pub int Save() => Relay(Check, this) + 1", "pub int Save(Store other) => Relay(Check, other) + 1", StringComparison.Ordinal)
            .Replace("print(sto.Save())", "print(sto.Save(sto))", StringComparison.Ordinal);
        Assert.Equal([(ErrorIds.CallLoop, 1)], Diagnostics(other));
    }

    [Fact]
    public void A_call_in_a_lambda_that_no_code_runs_hands_on_no_callback()
    {
        // U57, T61: `Keep` adds to a list a lambda that calls `Checker.Valid` with `this`, and no code calls it: no entry's code reaches
        // that caller, which hands on no callback, as `this` inside a lambda gives none, so that `Valid`'s call of `Load` is an edge, and
        // the loop of two types is one error, at line 2; without `Keep` the program has no error
        const string program = """
            actor Store(Checker Check) {
                pub int Save() => Check.Valid(this) + 1

                pub int Keep() {
                    mut kept = List<() => int>()
                    kept.Add(() => Check.Valid(this))
                    return kept.Count
                }

                pub int Load() => 7
            }

            actor Checker {
                pub int Valid(Store store) => store.Load() * 10
            }

            c = spawn Checker()
            s = spawn Store(c)
            print(s.Save())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program));
        const string keep = "    pub int Keep() {\n        mut kept = List<() => int>()\n        kept.Add(() => Check.Valid(this))\n        return kept.Count\n    }\n\n";
        Assert.Empty(Diagnostics(program.Replace(keep, "", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_cycle_of_parameters_that_a_this_enters_is_no_callback()
    {
        // U57, T62: `Store.Save` hands `this` to `Checker.Valid`, which hands it to `Verifier.Verify`, and that back to `Valid`, a
        // recursion that layer 1 runs as nested calls: the least fixed point reads the cycle of the two parameters as none, though a
        // `this` enters it, so that `Valid`'s call of `Load` is an edge, and the loop with `Store`'s call of `Valid` is one error, at
        // line 2; with `vouch` on that call of `Load` the program has no error
        const string program = """
            actor Store(Checker Check) {
                pub int Save() => Check.Valid(this, 2) + 1

                pub int Load() => 7
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store, int n) {
                    if n == 0 {
                        return store.Load()
                    }
                    return V.Verify(this, store, n - 1)
                }
            }

            actor Verifier {
                pub int Verify(Checker c, Store store, int n) => c.Valid(store, n)
            }

            ver = spawn Verifier()
            chk = spawn Checker(ver)
            sto = spawn Store(chk)
            print(sto.Save())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace("return store.Load()", "return vouch store.Load()", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_text_names_a_send_before_vouch_for_a_loop_of_two_types_and_of_one()
    {
        // T56: the text of `call-loop` names a `send` where no result is needed, or a change of the code, before `vouch`, for a loop
        // through two types and for one through one type, each text read by where the two words stand in it and not by its wording
        const string two = """
            actor Left {
                pub int Ask(Right other) => other.Value() + 1

                pub int Value() => 1
            }

            actor Right {
                pub int Ask(Left other) => other.Value() + 2

                pub int Value() => 2
            }

            l = spawn Left()
            r = spawn Right()
            print(l.Ask(r))
            print(r.Ask(l))

            """;
        const string one = """
            actor Node {
                pub int Size(Node other) => other.Count() + 1

                pub int Count() => 1
            }

            n = spawn Node()
            print(n.Size(spawn Node()))

            """;
        foreach (var (program, hops) in new[] { (two, 2), (one, 1) })
        {
            var error = Assert.Single(Check(program).Diagnostics.Sorted);
            Assert.Equal((ErrorIds.CallLoop, 2), (error.Id, error.Line));
            Assert.Equal(hops, Named(error).Count);
            var send = error.Message.IndexOf("`send`", StringComparison.Ordinal);
            var vouch = error.Message.IndexOf("`vouch`", StringComparison.Ordinal);
            Assert.True(send >= 0 && vouch > send, error.Message);
        }
    }

    [Fact]
    public void The_text_of_a_loop_through_a_spawn_names_the_construction_of_the_spawned_type()
    {
        // U56: the construction of `Child` runs in `Child` (U3), so that its call at line 8 is `Child`'s hop and the `spawn` at line 4
        // `Parent`'s: two hops, where a construction read as the spawner's code would make the call at line 8 a hop of `Parent` to
        // itself too, which stands first, since `Parent` is declared first, and a loop of one hop
        const string program = """
            actor Parent {
                pub int Count() => 3

                pub Child Make(Parent other) => spawn Child(other)
            }

            actor Child(Parent Up) {
                int seen = Up.Count()

                pub int Seen() => seen
            }

            p = spawn Parent()
            q = spawn Parent()
            print(p.Make(q).Seen())

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 8)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([8, 4], Named(errors[0]));
    }

    [Theory]
    [InlineData("x = vouch c.Size()", "(assign-or-declare x (vouch 1 (call (. c Size))))")]
    [InlineData("x = n + vouch c.Size()", "(assign-or-declare x (+ n (vouch 1 (call (. c Size)))))")]
    [InlineData("x = vouch c.Size() within 1s", "(assign-or-declare x (vouch 1 (call (. c Size) (within (unit 1s)))))")]
    [InlineData("x = vouch this.Size()", "(assign-or-declare x (vouch 1 (call (. this Size))))")]
    [InlineData("x = vouch s.Find(1) else {\n    Gone => 0\n}", "(assign-or-declare x (else (vouch 1 (call (. s Find) (int 1))) [((type Gone) => (int 0))]))")]
    [InlineData("vouch = 2", "(assign-or-declare vouch (int 2))")]
    [InlineData("x = vouch(3)", "(assign-or-declare x (call vouch (int 3)))")]
    [InlineData("x = vouch + 1", "(assign-or-declare x (+ vouch (int 1)))")]
    [InlineData("x = vouch\nc.Size()", "(assign-or-declare x vouch) (call (. c Size))")]
    [InlineData("vouch c.Size()", "(vouch 1 (call (. c Size)))")]
    [InlineData("vouch c = 2", "(declare vouch c (int 2))")]
    public void A_vouch_is_a_word_before_a_name_or_this_on_its_line_and_binds_to_the_call_after_it(string program, string tree)
    {
        // U58: the word before a name, `this` or `spawn` on its line, the call after it its node, with the call's clause and its `else`,
        // and as a statement, the record's own `vouch c.Size()`, which the statement parser reads as the call, where `vouch c = 2`, a
        // name after a name and then `=`, declares `c` of a type `vouch`; anywhere else `vouch` is a name, a variable, a call or an
        // operand (section 6, comment 6847932993, Q10)
        var diagnostics = new DiagnosticBag();
        var parsed = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(tree, SyntaxPrinter.Print(parsed));
    }

    [Theory]
    [InlineData("x = vouch spawn C()", 1)]
    [InlineData("x = vouch c.Count", 1)]
    [InlineData("x = 1\ny = vouch x", 2)]
    [InlineData("x = f(vouch\n    c.Size())", 2)]
    public void A_vouch_before_what_is_no_call_is_syntax_at_the_word(string program, int line)
    {
        // U58: after the word, what is no call, a `spawn` and a property among them, is `syntax` at the word's line (E4); inside brackets,
        // where a line break is no end, a name on the next line leaves `vouch` a name, which a name after it makes `syntax` there
        var diagnostics = new DiagnosticBag();
        Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Equal([(ErrorIds.Syntax, line)], diagnostics.Items.Select(d => (d.Id, d.Line)));
    }

    [Fact]
    public void A_vouch_that_covers_no_loop_is_a_warning_at_the_word_and_the_program_is_built()
    {
        // U59: `User` waits on `Counter`, which calls nothing: the `vouch` at line 12 covers no loop, a warning there, and the program is
        // built; a `vouch` before a call through `this` held in a local, which U57 takes out, is one too, and so is one in the top-level
        // code, which is no point of the graph
        const string program = """
            actor Counter {
                mut int count = 0

                pub void Add(int n) {
                    count = count + n
                }

                pub int Get() => count
            }

            actor User(Counter C) {
                pub int Read() => vouch C.Get()
            }

            c = spawn Counter()
            u = spawn User(c)
            c.Add(2)
            print(u.Read())

            """;
        var compilation = Builder.Compile(SourceText.FromString("program.kz", program), [], toolchain.Runtime, testBuild: true);
        Assert.NotNull(compilation.Program);
        Assert.Equal([(DiagnosticKind.Warning, LintIds.VouchUnneeded, 12)], compilation.Diagnostics.Sorted.Select(d => (d.Kind, d.Id, d.Line)));
        const string self = """
            actor Counter {
                mut int count = 0

                pub int Next() {
                    me = this
                    count = count + 1
                    return vouch me.Get() + 100
                }

                pub int Get() => count
            }

            c = spawn Counter()
            print(c.Next())
            print(vouch c.Get())

            """;
        Assert.Equal([(LintIds.VouchUnneeded, 7), (LintIds.VouchUnneeded, 15)], Diagnostics(self));
    }

    [Fact]
    public void Two_vouches_on_one_loop_keep_each_other_and_a_program_with_a_not_supported_gets_no_warning()
    {
        // U59: the graph is read with every `vouch` left out, so that each of the two covers the loop of `Left` and `Right`; with one
        // left out, the other is still needed (the control); and an actor the build does not read, `per node`, takes the warning away
        // beside its `not supported`
        const string program = """
            actor Left {
                pub int Ask(Right other) => vouch other.Value() + 1

                pub int Value() => 1
            }

            actor Right {
                pub int Ask(Left other) => vouch other.Value() + 2

                pub int Value() => 2
            }

            l = spawn Left()
            r = spawn Right()
            print(l.Ask(r))
            print(r.Ask(l))

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace("=> vouch other.Value() + 2", "=> other.Value() + 2", StringComparison.Ordinal)));
        const string unread = """
            actor Counter {
                pub int Get() => 3
            }

            actor Keyed per node {
                pub int Get() => 4
            }

            c = spawn Counter()
            print(vouch c.Get())

            """;
        var diagnostics = Check(unread).Diagnostics.Sorted;
        Assert.Equal([(DiagnosticKind.NotSupported, 5)], diagnostics.Select(d => (d.Kind, d.Line)));
        Assert.Equal([(LintIds.VouchUnneeded, 10)], Diagnostics(unread.Replace("actor Keyed per node {", "actor Keyed {", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_walk_reads_each_unit_of_two_thousand_actor_types_once_and_their_loop_is_one_error()
    {
        // T59: a chain of actor types, each waiting on the one after it and the last on the first, one loop through all of them: each type
        // adds its root, its construction and its two methods to the units the walk read, once each, and the loop is one error at the
        // first call; the groups are found without a frame of the process per type
        static string Chain(int count)
        {
            var text = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"actor A{i} {{\n    pub int Get(A{(i + 1) % count} n) => n.Value()\n\n    pub int Value() => {i}\n}}\n\n");
            }

            return text.Append("print(1)\n").ToString();
        }

        var (oneDiagnostics, one) = Check(Chain(1));
        Assert.Equal([(ErrorIds.CallLoop, 2)], oneDiagnostics.Sorted.Select(d => (d.Id, d.Line)));
        var (diagnostics, checker) = Check(Chain(2000));
        Assert.Equal([(ErrorIds.CallLoop, 2)], diagnostics.Sorted.Select(d => (d.Id, d.Line)));
        Assert.Equal(2000, Named(diagnostics.Sorted[0]).Count);
        Assert.Equal(new CallLoops(one, null).Walked + (4 * 1999), new CallLoops(checker, null).Walked);
    }
}
