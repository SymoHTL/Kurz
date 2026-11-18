using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// U12: the copy of a value that crosses into an actor, at each argument of a message and of <c>spawn</c>, and back to the caller as
/// a message's result, and of a value after <c>copy</c> at any call (the owner, 2026-11-15, #519, comment 6896192351, Q6, "Rust: deep
/// copy anywhere"). A crossing copies every counted block the value reaches into the heap of the actor it crosses into, or of the caller
/// its result goes to, the sender making the copy (section 6; the owner, 2026-11-15, #519, comment 6902210646, Q5, "Copy all, share big
/// unbuilt"), so that the heap of an actor holds every block its code reaches and is freed whole at the actor's end (section 3): a value
/// arrives as a copy of its own, with a count of one, made before the message is queued, a string's block, a list's, a map's, a
/// <c>data</c> block, an instance and the environment of a lambda among them; a value after <c>copy</c> at a call that is no crossing
/// has every block on a path to a class instance that passes through no function value and no actor's reference copied, the instance
/// among them, and shares by its count every other block. The copy is made by the type's helpers: a new block for each block on the way, an instance's strong and weak counts at one; a class with derived classes copies through the copy
/// slot of the instance's table (K6); whether a block that reaches no instance is copied is the session's, which the runtime's
/// <c>CopyWhole</c> reads. The copies one call makes, of its
/// arguments, share one memo, the call's session in the caller's frame, so that two paths that reach one block, in one argument or
/// in two, reach one copy (Q2, "Sharing kept"), and a result has a session of its own; each argument is copied where it is evaluated,
/// left to right, and the memo holds a count of each block it maps until the call's last copy (section 6). A <c>weak</c> reference
/// points at the copy of its target where the call's copies hold the target, else null, set at the call's last copy (Q3, "Points at
/// the copy"), so that each copy of a call of more than one is read after its last, or made in the place its callee reads (CrossInto),
/// one that holds a <c>weak</c> reference in its own bytes among them; and the blocks whose items or fields wait to be copied go on the runtime's work list, so that a chain of any
/// length crosses with no deeper stack than the nesting of its types (<c>runtime/common/runtime.kz</c>, "the copy of a value that
/// crosses"). The copy of a map whose keys reach a class instance places each key again under its copy's hash once the run's work list
/// is empty (<see cref="FillMap"/>; the owner, 2026-11-16, #519, comment 6923513534, "Rehash each key (Recommended)"). A copy runs no
/// program code.
/// </summary>
public sealed partial class Emitter
{
    // K6 with U12: the classes whose values a copy reads through their tables, whose tables then hold each class's copy
    private readonly HashSet<ClassSymbol> copiedThroughTable = new(ReferenceEqualityComparer.Instance);

    // U12: the session of the call each argument after `copy` stands in, where the call makes more than one copy (CallSession)
    private readonly Dictionary<Expression, string> copySessions = new(ReferenceEqualityComparer.Instance);

    // U12: the sessions of more than one copy begun in each region, which its end, or a jump out of it, ends (LetGo)
    private readonly Dictionary<List<(string Value, KurzType Type)>, List<string>> regionSessions = new(ReferenceEqualityComparer.Instance);

    // U12: the slots of the copies each session of more than one copy made so far, which its call reads after its last copy (Late), keyed
    // by the function that runs the session and its name, which is unique only in that function: a guarded block's function, begun in the
    // middle of an argument with a count of its own, can name a session as the function around it named the call's open one (LateKey)
    private readonly Dictionary<(IrFunction Function, string Session), List<(string Slot, KurzType Type, bool Held)>> lateCopies = new();

    /// <summary>
    /// U12: whether a value of the type reaches a class instance through a block a copy makes anew: an instance, an interface's value and a
    /// <c>weak</c> reference, and a list, a map, a union and a <c>data</c> value that hold one, but no function value, whose environment
    /// crosses by its count (Q5, "By written type"), and no actor's reference.
    /// </summary>
    private bool ReachesInstance(KurzType type) => ReachesInstance(type, new HashSet<string>(StringComparer.Ordinal));

    private bool ReachesInstance(KurzType type, HashSet<string> seen) => type switch
    {
        ClassType or InterfaceType or WeakRefType => true,
        ListType l => ReachesInstance(l.Element, seen),
        MapType m => ReachesInstance(m.Key, seen) || ReachesInstance(m.Value, seen),
        UnionType u => u.Cases.Any(c => ReachesInstance(c, seen)),
        // a block family holds no generic type, which is `not supported` where it is laid out (Layout.Refusal), so its types take no arguments
        DataType d when layout.IsBlock(d) => layout.Derived(d.Symbol).Any(x => seen.Add(x.Name) && Layout.Fields(new DataType(x, [])).Any(f => ReachesInstance(f, seen))),
        DataType d => seen.Add(d.Text) && Layout.Fields(d).Any(f => ReachesInstance(f, seen)),
        _ => false,
    };

    /// <summary>
    /// U12: the copy of a value, a value of its own with one count of each block it holds: the type's copy run from a slot that holds the
    /// value into the slot <paramref name="into"/>, in the call's session <paramref name="session"/>, else in a session of its own
    /// (<see cref="CopyRun"/>), whose copies go into the heap <paramref name="heap"/> of a crossing, every counted block among them, or,
    /// for null, after <c>copy</c> at a call that is no crossing, into the heap the allocation entry serves, every block on a path to a
    /// class instance that passes through no function value and no actor's reference among them (Q5). A function value crosses by
    /// <paramref name="copyInto"/>, the copy of the environment of its lambda
    /// (<see cref="CopyEnvironmentInto"/>). A session of more than one copy points the <c>weak</c> references of its copies at its last
    /// copy, the ones the slot holds itself among them, a <c>weak T?</c> and a plain <c>data</c> value or a union that holds one, so that
    /// the slot is read after the call's last copy (<see cref="Late"/>), or is the place the callee reads.
    /// </summary>
    private void CrossInto(string into, string value, KurzType type, string? session, string? heap, string? copyInto = null)
    {
        var source = fn.Alloca(Ir(type), "crossing");
        fn.Emit($"store {Ir(type)} {value}, ptr {source}");
        fn.Emit($"call void {CopyRun()}(ptr {into}, ptr {source}, ptr {copyInto ?? CopyInto(type)}, ptr {session ?? CopySession(1, heap)})");
    }

    /// <summary>U12: the copy of a value in a session of its own, read at once, since its one copy ends the session (<see cref="CrossInto"/>).</summary>
    private string Crossed(string value, KurzType type, string? heap, string? copyInto = null)
    {
        var copy = fn.Alloca(Ir(type), "copy");
        CrossInto(copy, value, type, null, heap, copyInto);
        return fn.Value($"load {Ir(type)}, ptr {copy}");
    }

    /// <summary>
    /// U12: the copy of a value in the session <paramref name="session"/> of a call of more than one copy, made where it is evaluated, into
    /// a slot of its own that the call reads after its last copy, which ends the session and points the <c>weak</c> references the slot
    /// holds itself (<see cref="CrossInto"/>); the slot is listed for the session, so that an exit before that read lets go of it where
    /// <paramref name="held"/>, as of a temporary of the region (<see cref="EndSessions"/>).
    /// </summary>
    private string Late(string value, KurzType type, string session, string? heap, bool held, string? copyInto = null)
    {
        var slot = fn.Alloca(Ir(type), "copy");
        CrossInto(slot, value, type, session, heap, copyInto);
        if (!lateCopies.TryGetValue(LateKey(session), out var listed))
        {
            lateCopies[LateKey(session)] = listed = [];
        }

        listed.Add((slot, type, held));
        return slot;
    }

    /// <summary>
    /// U12: the value of the argument <paramref name="e"/> after <c>copy</c> at a call that is no message, the copy of a value that
    /// reaches a class instance, made in a session of its own, a temporary of the region, and any other value as it is, which no copy
    /// could tell apart from its source (Q6, "Rust: deep copy anywhere"). An argument of a call of more than one copy is copied by its
    /// call, which reads it after its last copy (<see cref="CopiedLate"/>).
    /// </summary>
    private string Copied(Expression e, string value, KurzType type) => !ReachesInstance(type)
        ? value
        : copySessions.ContainsKey(e)
            ? throw new InvalidOperationException($"the argument after `copy` at line {e.Line} of a call of more than one copy, which its call reads before its last copy (U12)")
            : Temp(Crossed(value, type, null), type);

    /// <summary>
    /// U12: the argument <paramref name="e"/> after <c>copy</c> in a call of more than one copy, evaluated where it is written and copied
    /// there in the call's session (<see cref="Late"/>): the slot of its copy, which the call reads after its last copy.
    /// </summary>
    private string CopiedLate(Expression e, bool held)
    {
        var value = EmitUncopied(e);
        copySessions.Remove(e, out var session);
        return Late(value, Sub(Model.TypeOf(e).Type), session!, null, held);
    }

    /// <summary>
    /// U12: the session the copies of a call share where it makes <paramref name="copies"/> of them, more than one, into the heap
    /// <paramref name="heap"/> of a crossing, else null, the one copy then working in a session of its own (<see cref="Crossed"/>): the
    /// memo of a call is the call's, not the argument's (Q2, "Sharing kept"). <paramref name="noExit"/> where no code that can leave runs
    /// between its copies, as in the helpers of a restart and of a crash's messages, which open no region (<see cref="CopySession"/>).
    /// </summary>
    private string? CallSession(int copies, string? heap, bool noExit = false) => copies > 1 ? CopySession(copies, heap, noExit) : null;

    /// <summary>
    /// U12: the session of <paramref name="copies"/> copies begun, the runtime's eleven words in the caller's frame (<c>runtime/common/
    /// runtime.kz</c>, "the copy of a value that crosses"), zero from the function's start, so that the end of a region whose code did
    /// not reach the session's start finds none under way: an empty memo, no <c>weak</c> reference, the copies left to make, whether
    /// every block goes into the memo, as it does in a session of more than one copy, where a later argument may reach a block by a
    /// path that holds no count of it, the heap <paramref name="heap"/> of a crossing, null for a <c>copy</c> at a call that is no
    /// crossing, and the two words of the list of the open sessions of the activation that runs it. Such a session ends at its last
    /// copy, or, where the code leaves the region before it, at the region's end (<see cref="LetGo"/>), and a session of more than one
    /// copy is listed among the running activation's open ones, which a crash between two of its copies ends (the runtime's
    /// <c>CopyOpen</c>; U19). A session of more than one copy is begun in a region, whose end ends it, but where <paramref name="noExit"/>
    /// says that nothing leaves between its copies: a call that every statement, expression body, field default and constructor's call
    /// opens a region for, and a helper, which opens none, whose copies follow one another.
    /// </summary>
    private string CopySession(int copies, string? heap, bool noExit = false)
    {
        var session = fn.Alloca("[11 x i64]", "session", "zeroinitializer");
        fn.Emit($"store [11 x i64] [i64 0, i64 0, i64 0, i64 0, i64 0, i64 0, i64 {Number(copies)}, i64 {(copies > 1 ? 1 : 0)}, i64 0, i64 0, i64 0], ptr {session}");
        if (heap is not null)
        {
            fn.Emit($"store ptr {heap}, ptr {fn.Value($"getelementptr [11 x i64], ptr {session}, i32 0, i32 8")}");
        }

        if (copies > 1)
        {
            fn.Emit($"call void {RuntimeFunction("CopyOpen")}(ptr {session})");
        }

        if (copies > 1 && !noExit)
        {
            if (regions.Count == 0)
            {
                throw new InvalidOperationException("a call of more than one copy outside a region, which no exit before its last copy would end (U12)");
            }

            if (!regionSessions.TryGetValue(regions[^1], out var begun))
            {
                regionSessions[regions[^1]] = begun = [];
            }

            begun.Add(session);
        }

        return session;
    }

    /// <summary>
    /// U12: at a region's end, or a jump out of it, the sessions begun in it ended where their calls' code did not reach their last
    /// copy, a case propagated out of a later argument among them, before the region lets go of the copies, so that their <c>weak</c>
    /// references are pointed as a whole call's are and the memo's counts let go of (the runtime's <c>CopyAbort</c>); then the copies
    /// the call had not read yet and held (<see cref="Late"/>) let go of, as the temporaries of the region they would have been. A copy
    /// whose slot takes it, a field of a construction, is not held and stays where it was made: no code lets go of it at such an exit,
    /// as of no value such a slot took from an argument before the one that left, so a construction left so keeps one block for each
    /// such slot, a copy, a count of a shared block and a new value alike (issue 569).
    /// </summary>
    private void EndSessions(List<(string Value, KurzType Type)> temporaries)
    {
        if (regionSessions.TryGetValue(temporaries, out var begun))
        {
            foreach (var session in begun)
            {
                fn.Emit($"call void {RuntimeFunction("CopyAbort")}(ptr {session})");
                foreach (var (slot, type, _) in lateCopies.GetValueOrDefault(LateKey(session), []).Where(l => l.Held))
                {
                    Release(fn.Value($"load {Ir(type)}, ptr {slot}"), type);
                }
            }
        }
    }

    /// <summary>
    /// U12: the copies of the session <paramref name="session"/> read once the call's last copy ended it (<see cref="Late"/>), each by
    /// <paramref name="read"/> with its place among the copies the session listed, and the list let go of.
    /// </summary>
    private void ReadLate(string? session, Action<int, string> read)
    {
        if (session is not null && lateCopies.Remove(LateKey(session), out var listed))
        {
            for (var i = 0; i < listed.Count; i++)
            {
                read(i, fn.Value($"load {Ir(listed[i].Type)}, ptr {listed[i].Slot}"));
            }
        }
    }

    /// <summary>U12: the key of the session <paramref name="session"/> of the function being emitted among the listed copies (<see cref="Late"/>).</summary>
    private (IrFunction Function, string Session) LateKey(string session) => (fn, session);

    /// <summary>
    /// The run of a copy in the session <c>%a3</c>, which it names to the runtime first (<c>CopyUse</c>), since another call's session
    /// may have run since its last: the function of the value's type copies the value at <c>%a1</c> into <c>%a0</c>, then each block the
    /// work list holds is filled by its function, the last put on it first, until the list is empty, then each copy of a map whose keys
    /// reach a class instance is placed again by its function (<c>CopyAfter</c>, <see cref="FillMap"/>), and the runtime's
    /// <c>CopyLeft</c> counts the copy made, the session's last pointing each <c>weak</c> reference of its copies and emptying the memo.
    /// </summary>
    private string CopyRun() => Helper("copy.run", "void", ["ptr", "ptr", "ptr", "ptr"], () =>
    {
        fn.Emit($"call void {RuntimeFunction("CopyUse")}(ptr %a3)");
        fn.Emit("call void %a2(ptr %a0, ptr %a1)");
        var next = fn.Label("next");
        var fill = fn.Label("fill");
        var done = fn.Label("done");
        fn.Block(next);
        var entry = fn.Value($"call ptr {RuntimeFunction("CopyNext")}()");
        fn.CondBranch(fn.Value($"icmp eq ptr {entry}, null"), done, fill);
        fn.Block(fill);
        var source = fn.Value($"load ptr, ptr {entry}");
        var copy = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {entry}, i64 8")}");
        var filler = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {entry}, i64 16")}");
        fn.Emit($"call void {filler}(ptr {copy}, ptr {source})");
        fn.Branch(next);
        fn.Block(done);
        var place = fn.Label("place");
        var placed = fn.Label("placed");
        var waiting = fn.Value($"call ptr {RuntimeFunction("CopyAfterNext")}()");
        fn.CondBranch(fn.Value($"icmp eq ptr {waiting}, null"), placed, place);
        fn.Block(place);
        var map = fn.Value($"load ptr, ptr {waiting}");
        var placer = fn.Value($"load ptr, ptr {fn.Value($"getelementptr i8, ptr {waiting}, i64 8")}");
        fn.Emit($"call void {placer}(ptr {map})");
        fn.Branch(done);
        fn.Block(placed);
        fn.Emit($"call void {RuntimeFunction("CopyLeft")}(ptr %a3)");
        fn.Return(null);
    });

    /// <summary>
    /// U12: the function the runtime calls at a session's end with a block's address and, for a <c>weak</c> reference of a class with
    /// derived classes, its table: the count the memo held of a block of the type let go of, an instance's by its own class's release,
    /// or the weak count the session held of a <c>weak</c> reference's target.
    /// </summary>
    private string CopyLetGo(KurzType type) => Helper($"copy.let.go.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        switch (type)
        {
            case ClassType c:
                fn.Emit($"call void {ReleaseInstance(c.Symbol)}(ptr %a0)");
                break;
            case WeakRefType w when Layout.Fat(w.Target.Symbol):
                fn.Emit($"call void {ReleaseWeakThroughTable()}(%kz.obj {Pair("%a0", "%a1")})");
                break;
            case WeakRefType w:
                fn.Emit($"call void {ReleaseWeak(w.Target.Symbol)}(ptr %a0)");
                break;
            default:
                Release("%a0", type);
                break;
        }

        fn.Return(null);
    });

    /// <summary>The function that copies a value of the type from the slot <c>%a1</c> into the slot <c>%a0</c> (<see cref="CopyAt"/>).</summary>
    private string CopyInto(KurzType type) => Helper($"copy.into.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        CopyAt("%a0", "%a1", type);
        fn.Return(null);
    });

    /// <summary>
    /// The value of the type in the slot <paramref name="from"/> copied into the slot <paramref name="into"/>, which may be the same
    /// slot: a value that holds no block as it is; one that holds a block but reaches no instance copied in a crossing's session (Q5),
    /// else with one more count; an instance as its copy, a <c>weak</c> reference kept for the copy's end, a string, a list, a map or a
    /// block as its copy, and a plain <c>data</c> value and a union part by part.
    /// </summary>
    private void CopyAt(string into, string from, KurzType type)
    {
        if (!Counted(type))
        {
            fn.Emit($"store {Ir(type)} {fn.Value($"load {Ir(type)}, ptr {from}")}, ptr {into}");
            return;
        }

        if (ReachesInstance(type))
        {
            CopyParts(into, from, type);
            return;
        }

        var whole = fn.Label("whole");
        var shared = fn.Label("shared");
        var done = fn.Label("copied");
        fn.CondBranch(fn.Value($"call i1 {RuntimeFunction("CopyWhole")}()"), whole, shared);
        fn.Block(whole);
        CopyParts(into, from, type);
        fn.Branch(done);
        fn.Block(shared);
        var value = fn.Value($"load {Ir(type)}, ptr {from}");
        Retain(value, type);
        fn.Emit($"store {Ir(type)} {value}, ptr {into}");
        fn.Branch(done);
        fn.Block(done);
    }

    /// <summary>The value of a counted type in the slot <paramref name="from"/> copied into the slot <paramref name="into"/> as <see cref="CopyAt"/> copies it, block by block.</summary>
    private void CopyParts(string into, string from, KurzType type)
    {
        switch (type)
        {
            case ClassType c when !Layout.Fat(c.Symbol):
                fn.Emit($"store ptr {fn.Value($"call ptr {CopyClass(c.Symbol)}(ptr {fn.Value($"load ptr, ptr {from}")})")}, ptr {into}");
                return;
            case ClassType c:
                copiedThroughTable.Add(c.Symbol);
                CopyThroughTable(into, from);
                return;
            case WeakRefType weak:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    var address = Address(value, weak.Target.Symbol);
                    var kept = fn.Label("weak");
                    var done = fn.Label("weak.done");
                    fn.CondBranch(fn.Value($"icmp eq ptr {address}, null"), done, kept);
                    fn.Block(kept);
                    fn.Emit($"call void {RuntimeFunction("CopyWeak")}(ptr {into}, ptr {address}, i1 {(Layout.Fat(weak.Target.Symbol) ? "true" : "false")}, ptr {CopyLetGo(weak)})");
                    fn.Block(done);
                    return;
                }

            case ListType list:
                CopyBlockAt(into, from, CopyList(list));
                return;
            case MapType map:
                CopyBlockAt(into, from, CopyMap(map));
                return;
            case DataType d when layout.IsBlock(d):
                fn.Emit($"store ptr {fn.Value($"call ptr {CopyDataBlock(Family(d.Symbol))}(ptr {fn.Value($"load ptr, ptr {from}")})")}, ptr {into}");
                return;
            case DataType d:
                {
                    var fields = Layout.Fields(d);
                    for (var i = 0; i < fields.Count; i++)
                    {
                        CopyAt(PartAt(d, into, i), PartAt(d, from, i), fields[i]);
                    }

                    return;
                }

            case UnionType u when layout.Niche(u) is not null:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    var done = fn.Label("case.done");
                    ByCase(Tag(value, u), u, u.Cases, c =>
                    {
                        if (c is NullType)
                        {
                            fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                        }
                        else
                        {
                            CopyAt(into, from, c); // a union with a niche is laid out as its one case that holds a value
                        }

                        fn.Branch(done);
                    });
                    fn.Block(done);
                    return;
                }

            case UnionType u:
                {
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    var done = fn.Label("case.done");
                    ByCase(Tag(value, u), u, u.Cases, c =>
                    {
                        if (c is not (VoidType or NullType) && Counted(c))
                        {
                            CopyAt(PartAt(u, into, 1), PartAt(u, from, 1), c);
                        }

                        fn.Branch(done);
                    });
                    fn.Block(done);
                    return;
                }

            case PrimitiveType { Kind: Primitive.String }:
                fn.Emit($"store %kz.str {fn.Value($"call %kz.str {CopyString()}(%kz.str {fn.Value($"load %kz.str, ptr {from}")})")}, ptr {into}");
                return;
            case FunctionType or HandleType or HasherType or CommandType or ChildProcessType:
                {
                    // a function value reaches no copy here: the checker refuses one inside what crosses or what a `copy` reaches (U12,
                    // `CrossingOf`), and one given as the argument crosses with its lambda's environment (`CrossingEnvironment`); a handle,
                    // a hasher, a command or a child process reaches it in no program the checker reads clean: the checker refuses each
                    // where it crosses, after `move` as with no word, and a value of a type parameter on its function's text, which a copy
                    // of the function can make one of them (`not supported`, `SharedByCount` in `CrossingErrors`), since this
                    // arm would give the receiver a count of a block of the sender's heap, which the sender's restart or end frees
                    var value = fn.Value($"load {Ir(type)}, ptr {from}");
                    Retain(value, type);
                    fn.Emit($"store {Ir(type)} {value}, ptr {into}");
                    return;
                }

            default:
                throw new InvalidOperationException($"a copy of {Types.A(type)}, which no word makes cross and the checker refuses (U12)");
        }
    }

    /// <summary>
    /// Q5: the copy of a string in a block of its own of the heap the allocation entry serves, by the runtime's <c>CopyBytes</c>; a literal,
    /// which holds no block, as it is.
    /// </summary>
    private string CopyString() => Helper("copy.string", "%kz.str", ["%kz.str"], () =>
    {
        var count = fn.Value("extractvalue %kz.str %a0, 1");
        var held = fn.Label("held");
        var literal = fn.Label("literal");
        fn.CondBranch(fn.Value($"icmp slt i64 {count}, 0"), held, literal); // the mark: a block
        fn.Block(literal);
        fn.Return("%kz.str %a0");
        fn.Block(held);
        var bytes = fn.Value($"call ptr {RuntimeFunction("CopyBytes")}(ptr {fn.Value("extractvalue %kz.str %a0, 0")}, i64 {fn.Value($"and i64 {count}, 9223372036854775807")})");
        fn.Return($"%kz.str {fn.Value($"insertvalue %kz.str %a0, ptr {bytes}, 0")}");
    });

    /// <summary>
    /// U12 with Q5: the function that copies a function value whose lambda captures values of the types <paramref name="captures"/> from the
    /// slot <c>%a1</c> into the slot <c>%a0</c>, a value of its own: a new environment, held by one value, with the function that lets go
    /// of it, each value it captured copied as <see cref="CopyAt"/> copies it; a lambda that captures nothing has no environment to copy.
    /// </summary>
    private string CopyEnvironmentInto(List<KurzType> captures) => Helper($"copy.into.env{string.Concat(captures.Select(t => "." + Lower.Mangle(t)))}", "void", ["ptr", "ptr"], () =>
    {
        var value = fn.Value("load %kz.fn, ptr %a1");
        var environment = fn.Value($"extractvalue %kz.fn {value}, 1");
        var none = fn.Label("none");
        var some = fn.Label("environment");
        fn.CondBranch(fn.Value($"icmp eq ptr {environment}, null"), none, some);
        fn.Block(none);
        fn.Emit($"store %kz.fn {value}, ptr %a0");
        fn.Return(null);
        fn.Block(some);
        var shape = $"{{ i64, ptr{string.Concat(captures.Select(t => ", " + Ir(t)))} }}";
        var copy = Allocate(shape);
        fn.Emit($"store i64 1, ptr {copy}");
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {fn.Value($"getelementptr {shape}, ptr {environment}, i32 0, i32 1")}")}, ptr {fn.Value($"getelementptr {shape}, ptr {copy}, i32 0, i32 1")}");
        for (var i = 0; i < captures.Count; i++)
        {
            CopyAt(Capture(shape, copy, i), Capture(shape, environment, i), captures[i]);
        }

        fn.Emit($"store %kz.fn {fn.Value($"insertvalue %kz.fn {value}, ptr {copy}, 1")}, ptr %a0");
        fn.Return(null);
    });

    /// <summary>The address of part <paramref name="index"/> of a value of the type in the slot: a plain <c>data</c> value's field, a union's room at 1.</summary>
    private string PartAt(KurzType type, string slot, int index) =>
        fn.Value($"getelementptr {Ir(type)}, ptr {slot}, i32 0, i32 {Number(index)}");

    /// <summary>A list or a map in the slot <paramref name="from"/> copied by <paramref name="copy"/> into <paramref name="into"/>; the null of an empty one stays null.</summary>
    private void CopyBlockAt(string into, string from, string copy)
    {
        var block = fn.Value($"load ptr, ptr {from}");
        var made = fn.Alloca("ptr", "copied");
        fn.Emit($"store ptr null, ptr {made}");
        var some = fn.Label("block");
        var done = fn.Label("block.done");
        fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), done, some);
        fn.Block(some);
        fn.Emit($"store ptr {fn.Value($"call ptr {copy}(ptr {block})")}, ptr {made}");
        fn.Block(done);
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {made}")}, ptr {into}");
    }

    /// <summary>
    /// K6 with U12: the instance of a pair of an address and a table, of a class with derived classes, copied by the copy slot of its
    /// table, the pair of the copy and the same table stored into the slot <paramref name="into"/>.
    /// </summary>
    private void CopyThroughTable(string into, string from)
    {
        layout.Pair();
        var pair = fn.Value($"load %kz.obj, ptr {from}");
        var table = fn.Value($"extractvalue %kz.obj {pair}, 1");
        var copy = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(CopySlot)}")}");
        var made = fn.Value($"call ptr {copy}(ptr {fn.Value($"extractvalue %kz.obj {pair}, 0")})");
        fn.Emit($"store %kz.obj {Pair(made, table)}, ptr {into}");
    }

    /// <summary>
    /// In a copy helper over the block <c>%a0</c> of the type, held more than once where <paramref name="shared"/> holds, and in a session
    /// where every block goes into the memo: such a block that the session's copies already copied gives that copy, one count more by
    /// <paramref name="retain"/>; else <paramref name="make"/> makes the new block, which such a block leaves in the memo, with the
    /// function that lets go of the memo's count of the block (<see cref="CopyLetGo"/>), and which goes on the work list with
    /// <paramref name="fill"/>.
    /// </summary>
    private void Memo(KurzType type, string shared, Action<string> retain, Func<string> make, string fill)
    {
        shared = fn.Value($"or i1 {shared}, {fn.Value($"call i1 {RuntimeFunction("CopyEvery")}()")}");
        var look = fn.Label("memo");
        var fresh = fn.Label("fresh");
        fn.CondBranch(shared, look, fresh);
        fn.Block(look);
        var hit = fn.Value($"call ptr {RuntimeFunction("CopyFind")}(ptr %a0)");
        var found = fn.Label("found");
        fn.CondBranch(fn.Value($"icmp eq ptr {hit}, null"), fresh, found);
        fn.Block(found);
        retain(hit);
        fn.Return($"ptr {hit}");
        fn.Block(fresh);
        var copy = make();
        var keep = fn.Label("keep");
        var push = fn.Label("push");
        fn.CondBranch(shared, keep, push);
        fn.Block(keep);
        fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy}, ptr {CopyLetGo(type)})");
        fn.Block(push);
        fn.Emit($"call void {RuntimeFunction("CopyPush")}(ptr %a0, ptr {copy}, ptr {fill})");
        fn.Return($"ptr {copy}");
    }

    /// <summary>
    /// K1 with U12: the copy of an instance of the class, both counts at one, its fields filled from the work list; an instance held more
    /// than once, by its strong or its weak count, through the memo (<see cref="Memo"/>). A class with derived classes reaches it through
    /// the copy slot of its table (<see cref="CopyThroughTable"/>).
    /// </summary>
    private string CopyClass(ClassSymbol symbol) => Helper($"copy.class.{symbol.Name}", "ptr", ["ptr"], () =>
    {
        var block = layout.ClassBlock(symbol);
        var strong = fn.Value("load i64, ptr %a0");
        var weak = fn.Value($"load i64, ptr {fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1")}");
        var shared = fn.Value($"or i1 {fn.Value($"icmp sgt i64 {strong}, 1")}, {fn.Value($"icmp sgt i64 {weak}, 1")}");
        Memo(new ClassType(symbol), shared, hit => fn.Emit($"call void {RetainInstance()}(ptr {hit})"), () =>
        {
            var copy = Allocate(block);
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {copy}, i32 0, i32 1")}");
            return copy;
        }, FillClass(symbol));
    });

    /// <summary>The fields of the copy <c>%a0</c> of an instance of the class filled from the instance <c>%a1</c>, in their order.</summary>
    private string FillClass(ClassSymbol symbol) => Helper($"fill.class.{symbol.Name}", "void", ["ptr", "ptr"], () =>
    {
        for (var i = 0; i < symbol.Fields.Count; i++)
        {
            CopyAt(InstanceFieldPointer(symbol, "%a0", i), InstanceFieldPointer(symbol, "%a1", i), symbol.Fields[i].Type);
        }

        fn.Return(null);
    });

    /// <summary>M3 with U12: the copy of a list's block, held by one list, with room for its items alone, which the work list fills.</summary>
    private string CopyList(ListType type) => Helper($"copy.list.{Lower.Mangle(type)}", "ptr", ["ptr"], () =>
    {
        var shared = fn.Value($"icmp sgt i64 {fn.Value("load i64, ptr %a0")}, 1");
        Memo(type, shared, hit => fn.Emit($"call void {StdFunction("ListRetain")}(ptr {hit})"), () =>
        {
            var count = fn.Value($"load i32, ptr {fn.Value("getelementptr i8, ptr %a0, i64 8")}");
            var size = fn.Value($"add i64 16, {fn.Value($"mul i64 {fn.Value($"sext i32 {count} to i64")}, {ItemSize(type.Element)}")}");
            var copy = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {size})");
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i32 {count}, ptr {fn.Value($"getelementptr i8, ptr {copy}, i64 8")}");
            fn.Emit($"store i32 {count}, ptr {fn.Value($"getelementptr i8, ptr {copy}, i64 12")}");
            return copy;
        }, FillList(type));
    });

    /// <summary>The items of the copy <c>%a0</c> of a list's block filled from the block <c>%a1</c>, in their order.</summary>
    private string FillList(ListType type) => Helper($"fill.list.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        var count = fn.Value($"sext i32 {fn.Value($"load i32, ptr {fn.Value("getelementptr i8, ptr %a1, i64 8")}")} to i64");
        var into = fn.Value("getelementptr i8, ptr %a0, i64 16");
        var from = fn.Value("getelementptr i8, ptr %a1, i64 16");
        var index = fn.Alloca("i64", "item");
        fn.Emit($"store i64 0, ptr {index}");
        var check = fn.Label("item.check");
        var body = fn.Label("item");
        var done = fn.Label("items.done");
        fn.Block(check);
        var i = fn.Value($"load i64, ptr {index}");
        fn.CondBranch(fn.Value($"icmp slt i64 {i}, {count}"), body, done);
        fn.Block(body);
        var element = Ir(type.Element);
        CopyAt(fn.Value($"getelementptr {element}, ptr {into}, i64 {i}"), fn.Value($"getelementptr {element}, ptr {from}, i64 {i}"), type.Element);
        fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {index}");
        fn.Branch(check);
        fn.Block(done);
        fn.Return(null);
    });

    /// <summary>
    /// M9 with U12: the copy of a map's block, its slots' bytes as they stand, with its source's hash and keys (<c>MapCopy</c>), which the
    /// work list fills, each key and each value copied in its place, and which the run places again where its keys reach a class instance
    /// (<see cref="FillMap"/>).
    /// </summary>
    private string CopyMap(MapType type) => Helper($"copy.map.{Lower.Mangle(type)}", "ptr", ["ptr"], () =>
    {
        var shared = fn.Value($"icmp sgt i64 {fn.Value("load i64, ptr %a0")}, 1");
        Memo(type, shared, hit => fn.Emit($"call void {StdFunction("MapRetain")}(ptr {hit})"), () =>
        {
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            fn.Emit($"call void {StdFunction("MapRetain")}(ptr %a0)");
            var copy = fn.Value($"call ptr {StdFunction("MapCopy")}(ptr %a0, i64 {EntrySize(type)}, ptr {asked})"); // which lowers the block's count again
            var failed = fn.Label("failed");
            var made = fn.Label("made");
            fn.CondBranch(fn.Value($"icmp eq ptr {copy}, null"), failed, made);
            fn.Block(failed);
            fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
            fn.Unreachable();
            fn.Block(made);
            return copy;
        }, FillMap(type));
    });

    /// <summary>
    /// The entries of the copy <c>%a0</c> of a map's block, which holds the block's bytes: each key and each value copied in its slot. A
    /// key that reaches no class instance keeps the hash of its bytes, which its copy has too. The copy of a map whose keys reach one is
    /// placed again, each key under its copy's hash, by identity or by content as its class compares (K4, K5), by the helper a lookup past the
    /// bound rehashes with (<see cref="Rehash"/>), once the run's work list is empty, since a key's hash reads the fields of the instances
    /// it reaches, and a copy the memo gave can still wait there to be filled (the runtime's <c>CopyAfter</c>; section 6, the owner,
    /// 2026-11-16, #519, comment 6923513534, "Rehash each key (Recommended)"). The maps are placed before the session's last copy points
    /// the <c>weak</c> references of its copies (the runtime's <c>CopyLeft</c>), and no key the checker admits hashes a <c>weak</c> slot:
    /// a <c>weak T?</c> as a map's key or value is <c>not supported</c> (the checker's <c>WeakUnread</c>), a key type that reaches one
    /// through what <c>==</c> compares by content, a <c>data</c> value's field, an item or a case, is no key the checker reads (its
    /// <c>KeyOf</c>), and a class with a <c>weak</c> field compares, and
    /// hashes, by identity (K4, <c>ComparesByIdentity</c>), by its copy's address, which the pointing does not change, and an
    /// <c>equal by</c> that names such a field is <c>not supported</c> (K8). No two keys are compared: two keys of the source are two of
    /// the copy, since a copy gives each block of its source one copy (Q2), and a key the checker admits reaches no <c>weak</c> reference
    /// through what it compares, the one thing a copy can set to null (those checks).
    /// </summary>
    private string FillMap(MapType type) => Helper($"fill.map.{Lower.Mangle(type)}", "void", ["ptr", "ptr"], () =>
    {
        var next = fn.Label("slot.next");
        var done = fn.Label("slots.done");
        EachSlot("%a0", EntryType(type), next, done, slot =>
        {
            var key = KeyAt(type, slot);
            CopyAt(key, key, type.Key);
            var value = ValueAt(type, slot);
            CopyAt(value, value, type.Value);
        });
        fn.Block(done);
        if (ReachesInstance(type.Key))
        {
            fn.Emit($"call void {RuntimeFunction("CopyAfter")}(ptr %a0, ptr {Rehash(type)})");
        }

        fn.Return(null);
    });

    /// <summary>
    /// D7 with U12: the copy of a block of the family, by the number of the type it holds, with one count, which the work list fills;
    /// the constant of a type without fields stays as it is (<see cref="HeldForEver"/>). The family and its types take no type arguments,
    /// since a generic data type in a relation of inheritance or that holds its own type is <c>not supported</c> where it is laid out
    /// (<c>Layout.Refusal</c>) and <c>Layout.Derived</c> leaves a generic one out.
    /// </summary>
    private string CopyDataBlock(DataSymbol family) => Helper($"copy.block.{Lower.Mangle(new DataType(family, []))}", "ptr", ["ptr"], () =>
    {
        var count = fn.Value("load i64, ptr %a0");
        if (layout.Derived(family).Any(d => Layout.Fields(new DataType(d, [])).Count == 0))
        {
            var constant = fn.Label("constant");
            var counted = fn.Label("counted");
            fn.CondBranch(fn.Value($"icmp slt i64 {count}, 0"), constant, counted);
            fn.Block(constant);
            fn.Return("ptr %a0");
            fn.Block(counted);
        }

        var shared = fn.Value($"or i1 {fn.Value($"icmp sgt i64 {count}, 1")}, {fn.Value($"call i1 {RuntimeFunction("CopyEvery")}()")}");
        var look = fn.Label("memo");
        var fresh = fn.Label("fresh");
        fn.CondBranch(shared, look, fresh);
        fn.Block(look);
        var hit = fn.Value($"call ptr {RuntimeFunction("CopyFind")}(ptr %a0)");
        var found = fn.Label("found");
        fn.CondBranch(fn.Value($"icmp eq ptr {hit}, null"), fresh, found);
        fn.Block(found);
        fn.Emit($"call void {RetainBlock(family)}(ptr {hit})");
        fn.Return($"ptr {hit}");
        fn.Block(fresh);
        ByNumber("%a0", family, held =>
        {
            var block = layout.Block(held);
            var copy = Allocate(block);
            fn.Emit($"store i64 1, ptr {copy}");
            fn.Emit($"store i32 {Number(layout.Number(new DataType(held, [])))}, ptr {fn.Value($"getelementptr {block}, ptr {copy}, i32 0, i32 1")}");
            var keep = fn.Label("keep");
            var push = fn.Label("push");
            fn.CondBranch(shared, keep, push);
            fn.Block(keep);
            fn.Emit($"call void {RuntimeFunction("CopyKeep")}(ptr %a0, ptr {copy}, ptr {CopyLetGo(new DataType(family, []))})");
            fn.Block(push);
            fn.Emit($"call void {RuntimeFunction("CopyPush")}(ptr %a0, ptr {copy}, ptr {FillDataBlock(held)})");
            fn.Return($"ptr {copy}");
        });
    });

    /// <summary>The fields of the copy <c>%a0</c> of a block of the type filled from the block <c>%a1</c>, in their order.</summary>
    private string FillDataBlock(DataSymbol held) => Helper($"fill.block.{Lower.Mangle(new DataType(held, []))}", "void", ["ptr", "ptr"], () =>
    {
        var block = layout.Block(held);
        var types = Layout.Fields(new DataType(held, []));
        for (var i = 0; i < types.Count; i++)
        {
            CopyAt(FieldPointer(block, "%a0", i), FieldPointer(block, "%a1", i), types[i]);
        }

        fn.Return(null);
    });
}
