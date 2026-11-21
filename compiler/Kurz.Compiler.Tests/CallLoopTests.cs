using System.Text;
using System.Text.RegularExpressions;
using Kurz.Compiler.Build;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using Kurz.Compiler.Text;
using Xunit;

namespace Kurz.Compiler.Tests;

/// <summary>
/// Layer 2 of section 6's deadlocks (U56 to U59; the owner, 2026-11-20, issue 519, comment 7051071630: T40 to T62 and T74): the line of
/// each <c>call-loop</c> and how many a program gets, the loop its text names, read by the lines it names, the callbacks U57 takes out,
/// the word <c>vouch</c> as the parser reads it, the lint <c>vouch-unneeded</c>, and the walk that reads each unit of code once. Every
/// error is pinned by its id and its line; of a text, only the lines of a loop's hops are read, and for T56 the order of the words
/// <c>send</c> and <c>vouch</c>.
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
        // T40, T52: `A.Go` waits on `B.Go`, which waits on `C.Go`, which waits on `A.Value`, a path of methods that comes back to `A`;
        // the types are declared in the reverse order of the loop, so that the call that stands first in the file, at line 2, is its
        // last hop, and the text goes round the loop from `A`, the type it comes back to
        const string program = """
            actor C {
                pub int Go(A a) => a.Value() + 3

                pub int Value() => 3
            }

            actor B {
                pub int Go(C c, A a) => c.Go(a) + 2

                pub int Value() => 2
            }

            actor A {
                pub int Go(B b, C c, A a) => b.Go(c, a) + 1

                pub int Value() => 1
            }

            theA = spawn A()
            theB = spawn B()
            theC = spawn C()
            print(theA.Go(theB, theC, theA))

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([14, 8, 2], Named(errors[0]));
    }

    [Fact]
    public void The_text_names_the_shortest_loop_through_the_first_call()
    {
        // T52: after `A.Go`'s call at line 2, `B.Both` comes back to `A` by its own call at line 18, and by a path of three hops through
        // `C.Far` and `D.Far`, whose call of `A.Value` at line 8 stands first in source order: the text names the loop of two hops
        const string program = """
            actor A {
                pub int Go(B b, C c, D d, A a) => b.Both(c, d, a) + 1

                pub int Value() => 1
            }

            actor D {
                pub int Far(A a) => a.Value()
            }

            actor C {
                pub int Far(D d, A a) => d.Far(a)
            }

            actor B {
                pub int Both(C c, D d, A a) {
                    far = c.Far(d, a)
                    return far + a.Value()
                }
            }

            theA = spawn A()
            print(theA.Go(spawn B(), spawn C(), spawn D(), theA))

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([2, 18], Named(errors[0]));
    }

    [Fact]
    public void Of_two_loops_of_the_fewest_hops_the_text_names_the_one_the_code_of_each_type_reaches_first()
    {
        // U56: `A`'s construction, at line 10, and `A.Go`, at line 6, each wait on `B.Mid`, whose call at line 2 comes back to `A`: two
        // loops of two hops through that call, the first in source order; the walk takes a type's construction before its methods, so
        // that the text names the loop from the construction, where the order of the lines would name the one from `A.Go`
        const string program = """
            actor B(A Back) {
                pub int Mid() => Back.Value()
            }

            actor A(B Keep) {
                pub int Go() => Keep.Mid() + 1

                pub int Value() => 1

                int seen = Keep.Mid()
            }

            print(1)

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([10, 2], Named(errors[0]));
    }

    [Fact]
    public void The_text_starts_at_the_method_that_reaches_the_call_in_the_fewest_edges_of_the_first_type_declared()
    {
        // U56: `A.Far`, declared first, reaches the call at line 2 through `C.Relay` at line 6, two edges, and `A.Go` reaches it at line
        // 12, one: the text starts at `A.Go`; and where the call at line 2 is on a loop back to `A` and on one back to `B` of three hops
        // each, through `D.Y`, the text comes back to `A`, the type declared first
        const string fewest = """
            actor B(A Back) {
                pub int Mid() => Back.Value()
            }

            actor C(B Keep) {
                pub int Relay() => Keep.Mid()
            }

            actor A(B Keep, C Via) {
                pub int Far() => Via.Relay() + 1

                pub int Go() => Keep.Mid() + 1

                pub int Value() => 1
            }

            print(1)

            """;
        var errors = Check(fewest).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([12, 2], Named(errors[0]));
        const string two = """
            actor C(D Next) {
                pub int X() => Next.Y()
            }

            actor D(A First, B Second) {
                pub int Y() => First.Value() + Second.Value()
            }

            actor A(C Hub) {
                pub int Go() => Hub.X() + 1

                pub int Value() => 1
            }

            actor B(C Hub) {
                pub int Go() => Hub.X() + 2

                pub int Value() => 2
            }

            print(1)

            """;
        errors = Check(two).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([10, 2, 6], Named(errors[0]));
    }

    [Fact]
    public void A_group_of_types_that_reach_one_another_is_one_error_and_two_groups_are_two()
    {
        // T52: `A.Go` and `B.Go` each wait on `C.X`, which waits on both: the loops back to `A` and back to `B` reach each other's types,
        // one group, one error at line 2; with a method of `C` for each, `C.X` back to `A` and `C.Y` back to `B`, neither loop reaches
        // the other's type, though both pass through `C`: two groups, an error each, at the first call of each
        const string one = """
            actor A {
                pub int Go(C hub, A me, B other) => hub.X(me, other)

                pub int Value() => 1
            }

            actor B {
                pub int Go(C hub, A other, B me) => hub.X(other, me) + 1

                pub int Value() => 2
            }

            actor C {
                pub int X(A first, B second) => first.Value() + second.Value()
            }

            print(1)

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(one));
        const string two = """
            actor A {
                pub int Go(C hub, A me) => hub.X(me)

                pub int Value() => 1
            }

            actor B {
                pub int Go(C hub, B me) => hub.Y(me) + 1

                pub int Value() => 2
            }

            actor C {
                pub int X(A first) => first.Value()

                pub int Y(B second) => second.Value()
            }

            print(1)

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2), (ErrorIds.CallLoop, 8)], Diagnostics(two));
    }

    [Fact]
    public void A_call_whose_edge_leaves_its_group_is_none_of_the_group_s_calls()
    {
        // U56: `A` waits on `E` at line 2, a type on no loop, before it waits on `B.ToA` at line 4, which waits on `A`: the error stands
        // at the first call whose edge stays in the loop's group, and names the loop through it
        const string program = """
            actor A {
                pub int ToE(E e) => e.Value()

                pub int ToB(B b, A a) => b.ToA(a)

                pub int Value() => 1
            }

            actor B {
                pub int ToA(A a) => a.Value()

                pub int Value() => 2
            }

            actor E {
                pub int Value() => 3
            }

            print(1)

            """;
        var errors = Check(program).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 4)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([4, 10], Named(errors[0]));
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
                pub int Ask(Right other, Left back) => other.Ask(back) + 1

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
        // call through `Peek`'s parameter reaches the caller that waits, read so in `Peek`'s own entry too, whose call of `A.Load`, which
        // waits on `B.Count`, would come back to `B`, and the program has no error; a top-level call of `Check` with another `A` makes the
        // call in `Peek` an edge of `Check`, and the loop of `A.Save` and `B.Check` is one error, at line 2
        const string program = """
            actor A(B Other) {
                pub int Save() => Other.Check(this) + 1

                pub int Load() => Other.Count() + 7
            }

            actor B {
                pub int Peek(A x) => x.Load()

                pub int Check(A x) => Peek(x) * 10

                pub int Count() => 1
            }

            b = spawn B()
            a = spawn A(b)
            print(a.Save())

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(program + "print(b.Check(spawn A(b)))\n"));
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
        // U57, T60: `Checker.Valid` hands the `Store` it was given on to `Verifier.Verify` by a `send` and by a call that waits; the
        // `send` gives no callback (T49), so that `Verify`'s call of `Load` is an edge, and the loop of three types, through the call that
        // waits, is one error, at line 2; the `send` made a call that waits hands the callback on, and the program has no error
        const string program = """
            actor Store(Checker Check) {
                pub int Save() => Check.Valid(this) + 1

                pub int Load() => 7
            }

            actor Checker(Verifier V) {
                pub int Valid(Store store) {
                    send V.Verify(store)
                    V.Verify(store)
                    return 1
                }
            }

            actor Verifier {
                pub void Verify(Store store) {
                    print(store.Load())
                }
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

    // the origins the first argument of the call of `method` can have in the terms of the entry named `entry`, as the walk of the loops
    // reads them (ThrowSets.ActorsCode)
    private static IReadOnlyCollection<Origin> FirstArgument(string program, string entry, string method)
    {
        var (_, checker) = Check(program);
        var code = new ThrowSets(checker, null, reach: true).ActorsCode();
        return code.SelectMany(c => c.Entries).Single(e => e.Name == entry).Sites.Single(r => r.Site.Method?.Name == method).Arguments[0];
    }

    [Fact]
    public void The_arguments_of_a_call_reach_its_overrides_its_implementers_and_a_data_method_after_the_value()
    {
        // U57, D16: `A.Save` passes `this` to a function that calls a method through a base class, one through an interface and a data
        // type's method, and each passes it on to `B.Check`: the edge of the picked method's override, of the method that implements
        // the interface's and of the data method, whose function takes the value first, each reads `this` at the call of `B.Check`; an
        // actor's message is the only caller of each of those methods, so that each call hands on a callback and the program has no error
        // (T74); a top-level call of the function makes code outside the actors' messages reach them (ThrowSets.Outside), and the loop of
        // `A.Save` and `B.Check` is one error, at the call of `B.Check`
        const string actors = """

            actor A(B Other) {
                pub int Save() => Ask(Other, this) + 1

                pub int Load() => 7
            }

            actor B {
                pub int Check(A x) => x.Load() * 10
            }

            """;
        const string overridden = """
            class Base {
                pub virtual int Run(B peer, A x) => 0
            }

            class Via : Base {
                pub override int Run(B peer, A x) => peer.Check(x)
            }

            int Ask(B peer, A s) => Via().Run(peer, s)
            """;
        const string implemented = """
            interface Checker {
                int Run(B peer, A x)
            }

            class Via : Checker {
                pub int Run(B peer, A x) => peer.Check(x)
            }

            int Through(Checker k, B peer, A s) => k.Run(peer, s)

            int Ask(B peer, A s) => Through(Via(), peer, s)
            """;
        const string data = """
            data Relay(int K) {
                pub int Pass(B peer, A x) => peer.Check(x) + K
            }

            int Ask(B peer, A s) => Relay(1).Pass(peer, s)
            """;
        foreach (var (program, line) in new[] { (overridden, 6), (implemented, 6), (data, 2) })
        {
            Assert.Equal([Origin.Self], FirstArgument(program + actors, "`A.Save`", "Check"));
            Assert.Empty(Diagnostics(program + actors));
            Assert.Equal([(ErrorIds.CallLoop, line)], Diagnostics(program + actors + "theB = spawn B()\nprint(Ask(theB, spawn A(theB)))\n"));
        }
    }

    [Fact]
    public void The_walk_of_the_sets_refuses_a_node_it_does_not_read_and_the_walk_of_the_loops_passes_an_attempt()
    {
        // U56: the block `attempt`, which the checker says `not supported` to (section 6, U28), runs no code; the walk of the loops,
        // which reads a program beside its errors, passes it, and the walk of the sets, which reads a program with none, refuses it as
        // a node it has no case for
        const string program = """
            pub int Go() {
                x = attempt {
                    y = 1
                }

                return 1
            }

            actor A {
                pub int Get() => Go()
            }

            print(1)

            """;
        var (_, checker) = Check(program);
        Assert.NotEmpty(new ThrowSets(checker, CorpusTests.StdChecker.Value, reach: true).ActorsCode());
        var refused = Assert.Throws<InvalidOperationException>(() => new ThrowSets(checker, CorpusTests.StdChecker.Value).PubFunctions());
        Assert.Contains(nameof(AttemptExpression), refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_text_names_a_send_before_vouch_for_a_loop_of_two_types_and_of_one()
    {
        // T56: the text of `call-loop` names a `send` where no result is needed, or a change of the code, before `vouch`, for a loop
        // through two types and for one through one type, each text read by where the two words stand in it and not by its wording
        const string two = """
            actor Left {
                pub int Ask(Right other, Left back) => other.Ask(back) + 1

                pub int Value() => 1
            }

            actor Right {
                pub int Ask(Left other) => other.Value() + 2
            }

            l = spawn Left()
            r = spawn Right()
            print(l.Ask(r, l))

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
        // U56, T43: the `spawn` at line 4 is an edge of `Parent.Make` into the construction of `Child`, which runs in `Child` (U3) and
        // waits on `Parent.Count` at line 8: a path that comes back to `Parent`, the error at its call that waits and the text from
        // `Parent.Make`, two hops, where a construction read as the spawner's code would make the call at line 8 a hop of `Parent` to
        // itself, a loop of one hop
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
        Assert.Equal([4, 8], Named(errors[0]));
    }

    [Fact]
    public void Two_chains_that_cross_through_two_different_methods_are_no_loop_and_a_callee_that_waits_back_on_its_caller_s_type_is_one()
    {
        // T40: `Left.Ask` waits on `Right.Value` and `Right.Ask` on `Left.Value`, the crossing of `deadlock.kz`: neither callee's code
        // waits on a method of its caller's type, no loop, and the program has no error; with `Left.Ask` waiting on `Right.Ask`, whose code
        // waits on `Left.Value`, the path comes back to `Left`, a loop at line 2
        const string program = """
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
        Assert.Empty(Diagnostics(program));
        var back = program.Replace("pub int Ask(Right other) => other.Value() + 1", "pub int Ask(Right other) => other.Ask(spawn Left()) + 1", StringComparison.Ordinal);
        var errors = Check(back).Diagnostics.Sorted;
        Assert.Equal([(ErrorIds.CallLoop, 2)], errors.Select(d => (d.Id, d.Line)));
        Assert.Equal([2, 8], Named(errors[0]));
    }

    [Fact]
    public void A_path_that_comes_back_to_a_type_only_by_a_spawn_is_no_loop()
    {
        // T40, T43: `A.Go` waits on `B.Make`, which spawns an `A`: the path comes back to the construction of `A`, which waits on nothing,
        // and a `spawn` is no call that waits on a method of `A`, so that it is no loop; with `B.Make` waiting on the `A` it spawned, the
        // path comes back to `A.Value`, a loop at line 3
        const string program = """
            actor A {
                pub int Go(B b) {
                    b.Make()
                    return 1
                }

                pub int Value() => 1
            }

            actor B {
                mut List<A> made = []

                pub void Make() {
                    made.Add(spawn A())
                }
            }

            print(1)

            """;
        Assert.Empty(Diagnostics(program));
        var back = program.Replace("made.Add(spawn A())", "made.Add(spawn A())\n        print(made[0].Value())", StringComparison.Ordinal);
        Assert.Equal([(ErrorIds.CallLoop, 3)], Diagnostics(back));
    }

    [Fact]
    public void A_field_whose_every_write_is_a_callback_is_one_and_any_other_write_makes_it_none()
    {
        // U57, T49: `B.Keep` sets `peer` from its parameter, which `A.Join` fills with `this` by a call that waits, the field's one write
        // beside its `null`: `B.Read`'s call through it reaches the caller that waits, and the program has no error, read and written
        // through `this` as well; a second write from a parameter that a top-level call fills with another `A`, by the field's name or
        // through `this`, a default that spawns another `A`, or a write in a lambda that no code runs, makes the field none, and the loop
        // of `A.Total` and `B.Read` is one error at line 6; and a field of the primary constructor that a `spawn` of the top-level code
        // fills is none (T75 1)
        const string program = """
            actor A(B Other) {
                pub void Join() {
                    Other.Keep(this)
                }

                pub int Total() => Other.Read() + 1

                pub int Load() => 7
            }

            actor B {
                mut A? peer = null

                pub void Keep(A x) {
                    peer = x
                }

                pub int Read() {
                    p = peer
                    if p != null {
                        return p.Load()
                    }

                    return 0
                }
            }

            b = spawn B()
            a = spawn A(b)
            a.Join()
            print(a.Total())

            """;
        Assert.Empty(Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace("        peer = x\n", "        this.peer = x\n", StringComparison.Ordinal).Replace("p = peer", "p = this.peer", StringComparison.Ordinal)));
        var put = program.Replace("    pub int Read() {", "    pub void Put(A y) {\n        peer = y\n    }\n\n    pub int Read() {", StringComparison.Ordinal);
        Assert.Equal([(ErrorIds.CallLoop, 6)], Diagnostics(put + "b.Put(spawn A(b))\n"));
        Assert.Equal([(ErrorIds.CallLoop, 6)], Diagnostics(put.Replace("        peer = y\n", "        this.peer = y\n", StringComparison.Ordinal) + "b.Put(spawn A(b))\n"));
        Assert.Equal([(ErrorIds.CallLoop, 6)], Diagnostics(program.Replace("mut A? peer = null", "mut A? peer = spawn A(spawn B())", StringComparison.Ordinal)));
        var unrun = program.Replace("        peer = x\n", "        peer = x\n        f = () => {\n            peer = x\n        }\n", StringComparison.Ordinal);
        Assert.Equal([(ErrorIds.CallLoop, 6), (ErrorIds.UnusedVariable, 16)], Diagnostics(unrun));
        const string primary = """
            actor A {
                pub int Go(B b) => b.Ask()

                pub int Value() => 1
            }

            actor B(A Back) {
                pub int Ask() => Back.Value()
            }

            a = spawn A()
            print(a.Go(spawn B(a)))

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(primary));
    }

    [Fact]
    public void A_field_of_the_primary_constructor_that_every_spawn_fills_with_a_callback_is_one()
    {
        // U57, T75 1: `Boss.Run` spawns `Worker` with `this` and waits on `Worker.Work`, whose call through the field `Back` reaches the
        // caller that waits, and the program has no error, with `this` held in a local without `mut`, the field given by the second
        // parameter, and a parameter that is a callback handed on to the `spawn` (T60 (b)), as well; a second `spawn` that fills it with
        // another `Boss`, in the top-level code or in a method whose caller passes another, `this` held in a local with `mut`, and a write
        // of the field from a parameter that is none make it none, and the loop of `Boss.Run` and `Worker.Work` is one error at line 4,
        // at line 6 after the local; and a field of an actor that no `spawn` makes is none, as a method that nothing calls has no caller
        const string program = """
            actor Boss {
                pub int Run() {
                    w = spawn Worker(this)
                    return w.Work()
                }

                pub int Report() => 7
            }

            actor Worker(Boss Back) {
                pub int Work() => Back.Report() + 1
            }

            b = spawn Boss()
            print(b.Run())

            """;
        const string spawn = "        w = spawn Worker(this)\n";
        Assert.Empty(Diagnostics(program));
        Assert.Empty(Diagnostics(program.Replace(spawn, "        me = this\n        w = spawn Worker(me)\n", StringComparison.Ordinal)));
        Assert.Empty(Diagnostics(program.Replace("spawn Worker(this)", "spawn Worker(1, this)", StringComparison.Ordinal)
            .Replace("actor Worker(Boss Back)", "actor Worker(int N, Boss Back)", StringComparison.Ordinal)));
        const string handed = """
            actor Top {
                pub int Go(Boss b) => b.Run(this)

                pub int Ping() => 3
            }

            actor Boss {
                pub int Run(Top t) {
                    w = spawn Worker(t)
                    return w.Work()
                }
            }

            actor Worker(Top Back) {
                pub int Work() => Back.Ping() + 1
            }

            top = spawn Top()
            print(top.Go(spawn Boss()))

            """;
        Assert.Empty(Diagnostics(handed));
        foreach (var other in new[]
        {
            program + "v = spawn Worker(b)\nprint(v.Work())\n",
            program.Replace("    pub int Report() => 7\n", "    pub int Other(Boss x) {\n        w = spawn Worker(x)\n        return w.Work()\n    }\n\n    pub int Report() => 7\n", StringComparison.Ordinal)
                + "print(b.Other(spawn Boss()))\n",
            program.Replace("actor Worker(Boss Back) {\n", "actor Worker(mut Boss Back) {\n    pub void Set(Boss x) {\n        Back = x\n    }\n\n", StringComparison.Ordinal),
        })
        {
            Assert.Equal([(ErrorIds.CallLoop, 4)], Diagnostics(other));
        }

        Assert.Equal([(ErrorIds.CallLoop, 6)], Diagnostics(program.Replace(spawn, "        mut me = this\n        me = this\n        w = spawn Worker(me)\n", StringComparison.Ordinal)));
        const string unspawned = """
            actor Boss {
                pub int Run(Worker x) => x.Work()

                pub int Report() => 7
            }

            actor Worker(Boss Back) {
                pub int Work() => Back.Report() + 1
            }

            b = spawn Boss()
            print(b.Report())

            """;
        Assert.Equal([(ErrorIds.CallLoop, 2)], Diagnostics(unspawned));
    }

    [Fact]
    public void A_list_field_whose_every_item_written_is_a_callback_holds_callbacks_read_by_a_for_or_an_index()
    {
        // U57, T49: `B.Keep` adds its parameter, which `A.Join` fills with `this`, to `kept`, which starts empty: each item is a callback,
        // and `B.Sum`'s call through the variable of a `for` over the list reaches the caller that waits, and the program has no error,
        // read through an index, put in by `Insert`, an item assigned or a list literal, after an empty list its constructor made, and
        // through `this`, as well; an item from another `A`, one added in a lambda, which captures it or a local that holds it, an item
        // assigned from another `A`, one that `AddRange` puts in, a list literal with another item, a list assigned from a local, a list
        // passed as `mut`, and an item from another `A` added in an own method that `Keep` calls directly, make it none, and so does a call
        // through an item read in a lambda: the loop of `A.Total` and `B.Sum` is one error at line 6, and at line 9 after the function
        const string program = """
            actor A(B Other) {
                pub void Join() {
                    Other.Keep(this)
                }

                pub int Total() => Other.Sum() + 1

                pub int Load() => 7
            }

            actor B {
                mut List<A> kept = []

                pub void Keep(A x) {
                    kept.Add(x)
                }

                pub int Sum() {
                    mut total = 0
                    for k in kept {
                        total = total + k.Load()
                    }

                    return total
                }
            }

            b = spawn B()
            a = spawn A(b)
            a.Join()
            print(a.Total())

            """;
        const string add = "        kept.Add(x)\n";
        Assert.Empty(Diagnostics(program));
        foreach (var (from, to) in new[]
        {
            ("        for k in kept {\n            total = total + k.Load()\n        }\n", "        if kept.Count > 0 {\n            total = kept[0].Load()\n        }\n"),
            (add, "        kept.Insert(0, x)\n"),
            (add, add + "        kept[0] = x\n"),
            (add, "        kept = [x]\n"),
            (add, "        kept = List<A>()\n" + add),
            (add, "        this.kept.Add(x)\n"),
        })
        {
            Assert.Empty(Diagnostics(program.Replace(from, to, StringComparison.Ordinal)));
        }

        foreach (var other in new[]
        {
            program + "b.Keep(spawn A(b))\n",
            program.Replace(add, "        more = () => kept.Add(x)\n        more()\n", StringComparison.Ordinal),
            program.Replace(add, "        held = x\n        more = () => kept.Add(held)\n        more()\n", StringComparison.Ordinal),
            program.Replace(add, add + "        kept[0] = spawn A(spawn B())\n", StringComparison.Ordinal),
            program.Replace(add, "        mut more = List<A>()\n        more.Add(x)\n        kept = more\n", StringComparison.Ordinal),
            program.Replace("        for k in kept {\n            total = total + k.Load()\n        }\n", "        if kept.Count > 0 {\n            first = () => kept[0].Load()\n            total = first()\n        }\n", StringComparison.Ordinal),
            program.Replace(add, "        Put(x)\n", StringComparison.Ordinal).Replace("    pub int Sum() {", "    void Put(A y) {\n        kept.Add(y)\n    }\n\n    pub int Sum() {", StringComparison.Ordinal) + "b.Keep(spawn A(b))\n",
            program.Replace(add, "        kept.AddRange([x])\n", StringComparison.Ordinal),
            program.Replace(add, "        kept = [x, spawn A(spawn B())]\n", StringComparison.Ordinal),
        })
        {
            Assert.Equal([(ErrorIds.CallLoop, 6)], Diagnostics(other));
        }

        Assert.Equal([(ErrorIds.CallLoop, 9)], Diagnostics("void Grow(mut List<A> items) {\n}\n\n" + program.Replace(add, add + "        Grow(mut kept)\n", StringComparison.Ordinal)));
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
    [InlineData("x = vouch c.Child().Size()", "(assign-or-declare x (vouch 1 (call (. (call (. c Child)) Size))))")]
    public void A_vouch_is_a_word_before_a_name_or_this_on_its_line_and_binds_to_the_call_after_it(string program, string tree)
    {
        // U58: the word before a name, `this` or `spawn` on its line, the call after it its node, with the call's clause and its `else`,
        // and as a statement, the record's own `vouch c.Size()`, which the statement parser reads as the call, where `vouch c = 2`, a
        // name after a name and then `=`, declares `c` of a type `vouch`, a statement read as a declaration first; of a chain of calls
        // the word takes the last, `.Size()` of `c.Child().Size()`, the postfix expression's own node; anywhere else `vouch` is a name, a variable, a call or an
        // operand (section 6, issue 519, comment 6847932993, Q10)
        var diagnostics = new DiagnosticBag();
        var parsed = Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(tree, SyntaxPrinter.Print(parsed));
    }

    [Theory]
    [InlineData("x = vouch spawn C()", 1)]
    [InlineData("x = vouch c.Count", 1)]
    [InlineData("x = 1\ny = vouch x", 2)]
    public void A_vouch_before_what_is_no_call_is_syntax_at_the_word(string program, int line)
    {
        // U58: after the word, what is no call, a `spawn` and a property among them, is `syntax` at the word's line (E4)
        var diagnostics = new DiagnosticBag();
        Parser.Parse(SourceText.FromString("test.kz", program), diagnostics);
        Assert.Equal([(ErrorIds.Syntax, line)], diagnostics.Items.Select(d => (d.Id, d.Line)));
    }

    [Fact]
    public void A_vouch_before_a_line_break_inside_brackets_is_a_name_and_syntax_at_the_name_after_it()
    {
        // U58: inside brackets, where a line break is no end, a name on the line after `vouch` leaves the word a name, and the name
        // after it is text no rule gives a meaning, `syntax` at that name's line (E4), the third, which nothing else stands on
        var diagnostics = new DiagnosticBag();
        Parser.Parse(SourceText.FromString("test.kz", "x = f(1,\n    vouch\n    c.Size())"), diagnostics);
        Assert.Equal([(ErrorIds.Syntax, 3)], diagnostics.Items.Select(d => (d.Id, d.Line)));
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
        // U59: the graph is read with every `vouch` left out, so that each of the two covers the loop of `Left.Ask`, `Right.Ask` and
        // `Left.Value`; with one left out, the other is still needed (the control); and an actor the build does not read, `per node`,
        // takes the warning away beside its `not supported`, where another error, an `unknown-name` at line 11, leaves it
        const string program = """
            actor Left {
                pub int Ask(Right other, Left back) => vouch other.Ask(back) + 1

                pub int Value() => 1
            }

            actor Right {
                pub int Ask(Left other) => vouch other.Value() + 2
            }

            l = spawn Left()
            r = spawn Right()
            print(l.Ask(r, l))

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
        var read = unread.Replace("actor Keyed per node {", "actor Keyed {", StringComparison.Ordinal);
        Assert.Equal([(LintIds.VouchUnneeded, 10)], Diagnostics(read));
        Assert.Equal([(LintIds.VouchUnneeded, 10), (ErrorIds.UnknownName, 11)], Diagnostics(read + "print(nope)\n"));
    }

    [Fact]
    public void The_walk_reads_each_unit_of_two_thousand_actor_types_once_and_their_loop_is_one_error()
    {
        // T59: a chain of actor types, the method of each waiting on the same method of the one after it and the last on the first's, one
        // path of methods that comes back to every type on it: each type adds its root, its construction and its two methods to the units
        // the walk read, once each, and the loop is one error at the first call; the groups are found without a frame of the process per
        // type
        static string Chain(int count)
        {
            var text = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"actor A{i}(A{(i + 1) % count} Next) {{\n    pub int Get() => Next.Get() + 1\n\n    pub int Value() => {i}\n}}\n\n");
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
