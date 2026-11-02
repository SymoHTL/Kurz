using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 7: an instance of a class is the address of its block, as Rust's <c>Rc&lt;T&gt;</c> is the address of its <c>RcBox</c>
/// (section 3): the count of the references that hold it, the count of the weak ones, one for all the strong ones together, then its
/// fields in their order, a base's first (<see cref="Layout.ClassBlock"/>); a nullable instance is the address alone, null its
/// <c>null</c>. A value of a class that another class derives from is the pair of the address and the table of the instance's class,
/// as Rust's <c>Rc&lt;dyn Trait&gt;</c> is the address and its vtable (K6, <see cref="Layout.Fat"/>): the table holds the helpers that
/// end the instance and free its block, its equality and its hash where the program uses them, and its <c>virtual</c> methods, the
/// base's first (<see cref="EmitTables"/>). The instance is counted as a list is (K2): every place that holds one holds a count, a
/// variable lets go of it at the end of its block, and the last count lets go of the fields in their order, as Rust drops them, and
/// then of the weak count the strong ones held; the last weak count frees the block. An instance whose last count a release lets go of
/// waits on a work list until that release ends (<see cref="ReleaseInstance"/>). No count is elided. A method is a function of the
/// module whose first parameter is the instance, <c>this</c>, which it borrows from the caller as a function borrows its arguments
/// (K7), with its table where its class has a base or derived classes. A read of a counted field through an instance takes a count of
/// its own, a temporary of the region, since a call can assign the field through another reference while the value is still in use
/// (K2, K9).
/// </summary>
public sealed partial class Emitter
{
    private readonly Dictionary<ClassSymbol, Unit> classOwners = new(ReferenceEqualityComparer.Instance);

    // each class whose instances are released: the helper that ends one, and its work list (the owner, 2026-11-01, "Iterative, never crashes")
    private readonly List<(string Drop, string List)> released = [];

    // K6: the classes whose tables the program names, in the order first named, and the classes whose values `==` and a map's hash
    // read through the table (K13)
    private readonly List<ClassSymbol> tabled = [];
    private readonly HashSet<ClassSymbol> comparedThroughTable = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ClassSymbol> hashedThroughTable = new(ReferenceEqualityComparer.Instance);

    // K6: the slots of a table before its `virtual` methods: the release, the weak release, the equality and the hash
    private const int MethodSlots = 4;

    // each class whose body's defaults are queued for their function, with its name (K7)
    private readonly Queue<(ClassSymbol Class, string Name)> defaultsPending = new();

    /// <summary>
    /// K1, K7, K10: an instance with both counts at one, each field of the primary constructor holding the argument bound to it or its
    /// default; where a class of the chain writes the explicit form, the fields of each class from its constructor's arguments
    /// (<see cref="FillFields"/>); then each field of the body its default, in their order, read in the class's file through the
    /// instance being built, as C# runs a field's initializer before the constructor's body; a class in a chain declares none (K6, K7,
    /// K18). A class with derived classes gives the pair of the address and its table.
    /// </summary>
    private string EmitClassNew(CallExpression c, CallInfo info)
    {
        var symbol = ((ClassType)info.Target!).Symbol;
        var block = layout.ClassBlock(symbol);
        string p;
        if (symbol.Chain.All(x => x.BaseCall is null))
        {
            // each parameter of the constructor is a field, which owns the argument bound to it
            var arguments = Arguments(c, info, j => symbol.Constructor[j].Field.Type, _ => false, j => ClassFieldDefault(symbol, j), cConvention: false, owned: true);
            p = Allocate(block);
            fn.Emit($"store i64 1, ptr {p}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {p}, i32 0, i32 1")}");
            for (var i = 0; i < arguments.Count; i++)
            {
                fn.Emit($"store {arguments[i]}, ptr {InstanceFieldPointer(symbol, p, symbol.Fields.IndexOf(symbol.Constructor[i].Field))}");
            }

            // K7: the fields of the body follow the primary constructor's, each given its default by the class's function of
            // them (ClassDefaults); a class in a chain declares none
            if (symbol.Fields.Any(f => f.Body is not null))
            {
                fn.Emit($"call void {ClassDefaults(symbol)}(ptr {p})");
            }
        }
        else
        {
            var arguments = Arguments(c, info, j => symbol.Constructor[j].Field.Type, _ => false, j => ClassFieldDefault(symbol, j), cConvention: false, owned: false);
            p = Allocate(block);
            fn.Emit($"store i64 1, ptr {p}");
            fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {p}, i32 0, i32 1")}");
            FillFields(symbol, arguments, p);
        }

        return Layout.Fat(symbol) ? Pair(p, Table(symbol)) : p;
    }

    /// <summary>
    /// K10, K11: the fields of class <paramref name="x"/> in the block <paramref name="p"/> of an instance being made, from the arguments
    /// bound to its constructor, which it borrows: the base's first, from the arguments the explicit form computes over the parameters
    /// or from those the short form passes on, then the class's own, each with a count of its own.
    /// </summary>
    private void FillFields(ClassSymbol x, List<string> arguments, string p)
    {
        var own = x.Base?.Fields.Count ?? 0;
        if (x.Base is { } b && x.BaseCall is { } call)
        {
            for (var i = 0; i < x.Constructor.Count; i++)
            {
                // the parameters, as variables the arguments of the base read
                var slot = fn.Alloca(Ir(x.Constructor[i].Field.Type), x.Constructor[i].Declaration.Name);
                fn.Emit($"store {arguments[i]}, ptr {slot}");
                slots[Model.DeclarationOf(x.Constructor[i].Declaration)] = slot;
            }

            FillFields(b, Arguments(call, Model.CallOf(call), j => b.Constructor[j].Field.Type, _ => false, j => ClassFieldDefault(b, j), cConvention: false, owned: false), p);
        }
        else if (x.Base is { } shortBase)
        {
            FillFields(shortBase, arguments.Take(shortBase.Constructor.Count).ToList(), p);
        }

        for (var i = 0; i < x.Constructor.Count; i++)
        {
            var field = x.Constructor[i].Field;
            var index = x.Fields.IndexOf(field);
            if (index < own)
            {
                continue; // the base's field, which the base's arguments gave (K11)
            }

            if (Counted(field.Type))
            {
                Retain(arguments[i][(Ir(field.Type).Length + 1)..], field.Type);
            }

            fn.Emit($"store {arguments[i]}, ptr {InstanceFieldPointer(x, p, index)}");
        }
    }

    /// <summary>
    /// K7: the function that gives each field of a class's body its default, in their order, through the instance it takes, queued
    /// on first use and emitted once per class, which every construction calls, as C# runs the initializers in the constructor; a
    /// default that builds an instance of its own class, or of a class whose default builds this one, calls it again, so the build
    /// ends and the run recurses into <c>stack-overflow</c> (the owner, 2026-11-02, against a refusal of such a default).
    /// </summary>
    private string ClassDefaults(ClassSymbol symbol)
    {
        var key = $"{classOwners[symbol].Prefix}.{symbol.Name}.{Number(symbol.Declaration.Line)}";
        if (!instances.TryGetValue(key, out var name))
        {
            name = $"@\"{key}\"";
            instances[key] = name;
            defaultsPending.Enqueue((symbol, name));
        }

        return name;
    }

    /// <summary>The function <see cref="ClassDefaults"/> names: a trace names its frame by the class, as the construction is written.</summary>
    private void EmitClassDefaults(ClassSymbol symbol, string name)
    {
        var caller = unit;
        unit = classOwners[symbol];
        try
        {
            features = NoFeatures; // one function serves every construction, so what it calls is compiled for the CPU alone (R18)
            Begin(new IrFunction($"internal void {name}(ptr %this)", FeatureAttributes(features)), NoMap, Types.Void);
            Trace(symbol.Name, Symbol(name), symbol.Declaration.Line);
            var self = fn.Alloca("ptr", "this");
            fn.Emit($"store ptr %this, ptr {self}");
            slots[Model.FieldsThisOf(symbol)] = self;
            for (var i = symbol.Fields.Count(f => f.Body is null); i < symbol.Fields.Count; i++)
            {
                var field = symbol.Fields[i];
                var d = field.Body!.Value!;
                var index = i;
                At(d.Line);
                InRegion(() => fn.Emit($"store {Ir(field.Type)} {EmitDefault(unit, d, field.Type, owned: true)}, ptr {InstanceFieldPointer(symbol, "%this", index)}"));
            }

            fn.Return(null);
            End();
        }
        finally
        {
            unit = caller;
        }
    }

    /// <summary>The default written for the parameter of the primary constructor in the slot, and the file of the class that writes it.</summary>
    private (Expression Value, Unit File) ClassFieldDefault(ClassSymbol symbol, int slot) =>
        (symbol.Constructor[slot].Declaration.Default ?? throw new InvalidOperationException($"the parameter {slot} of `{symbol.Name}` has no argument and no default"), classOwners[symbol.Constructor[slot].Owner]);

    /// <summary>K6: the address of the block a value of the class holds, the first of the pair where the class has derived classes.</summary>
    private string Address(string value, ClassSymbol symbol) => Layout.Fat(symbol) ? fn.Value($"extractvalue %kz.obj {value}, 0") : value;

    /// <summary>K6: the pair of an instance's address and its class's table.</summary>
    private string Pair(string address, string table) =>
        fn.Value($"insertvalue %kz.obj {fn.Value($"insertvalue %kz.obj undef, ptr {address}, 0")}, ptr {table}, 1");

    /// <summary>K6: the table of the class, emitted after the last function (<see cref="EmitTables"/>).</summary>
    private string Table(ClassSymbol symbol)
    {
        if (!tabled.Contains(symbol))
        {
            tabled.Add(symbol);
        }

        layout.Pair();
        return $"@\"table.{symbol.Name}\"";
    }

    /// <summary>K6, K7: whether the methods of the class take the instance with its table: a class with a base or with derived classes.</summary>
    private static bool Related(ClassSymbol symbol) => symbol.Base is not null || symbol.Derived;

    /// <summary>K7: the method a chain of overrides starts from, the one marked <c>virtual</c>, whose slot each override fills.</summary>
    private static FunctionSymbol Root(FunctionSymbol method) => method.Overridden is { } overridden ? Root(overridden) : method;

    /// <summary>K7: the <c>virtual</c> methods of a class in the order of their slots: its base's first, then those it marks <c>virtual</c>, in the order written.</summary>
    private static List<FunctionSymbol> VirtualMethods(ClassSymbol symbol) =>
        [.. symbol.Base is { } b ? VirtualMethods(b) : [], .. symbol.Methods.Values.SelectMany(list => list).Where(m => (m.Declaration!.Modifiers & Modifiers.Virtual) != 0).OrderBy(m => m.Line)];

    /// <summary>K7: what a call of the <c>virtual</c> method runs on an instance of the class: the override nearest the class, or the method itself.</summary>
    private static FunctionSymbol Implementation(ClassSymbol symbol, FunctionSymbol root) =>
        symbol.Chain.SelectMany(c => c.Methods.TryGetValue(root.Name, out var list) ? list : []).First(m => ReferenceEquals(Root(m), root));

    /// <summary>The address of field <paramref name="index"/> in the block of an instance: the two counts come first.</summary>
    private string InstanceFieldPointer(ClassSymbol symbol, string pointer, int index) =>
        fn.Value($"getelementptr {layout.ClassBlock(symbol)}, ptr {pointer}, i32 0, i32 {Number(index + 2)}");

    /// <summary>K1, K2: a field of an instance, of a counted type with a count of its own that the region lets go of.</summary>
    private string InstanceField(string instance, ClassSymbol symbol, int index)
    {
        var type = symbol.Fields[index].Type;
        var value = fn.Value($"load {Ir(type)}, ptr {InstanceFieldPointer(symbol, Address(instance, symbol), index)}");
        if (type is WeakRefType weak)
        {
            return Upgrade(value, weak); // R3: a strong `T?`, held to the end of the statement
        }

        if (Counted(type))
        {
            Retain(value, type);
            Temp(value, type);
        }

        return value;
    }

    /// <summary>K7: <c>this</c> of the method being emitted, or of the method a lambda in it was made in, which the lambda holds.</summary>
    private string This(object node, int line)
    {
        if (!Model.IsImplicitField(node, out var self, out _))
        {
            throw new InvalidOperationException($"`this` that the checker did not read at line {line}");
        }

        return fn.Value($"load {Ir(self.Type)}, ptr {Place(self, line)}");
    }

    /// <summary>K7: a field of the instance read by its name inside a method.</summary>
    private string ImplicitField(NameExpression n, FieldSymbol field, KurzType type)
    {
        var symbol = ClassOfThis(n, n.Line).Symbol;
        var index = symbol.FieldIndex(field.Name);
        return Narrowed(InstanceField(This(n, n.Line), symbol, index), field.Type, type);
    }

    /// <summary>The class whose method holds the implicit field or method <paramref name="node"/>, as the type of its <c>this</c>.</summary>
    private ClassType ClassOfThis(object node, int line) =>
        Model.IsImplicitField(node, out var self, out _) && self.Type is ClassType c ? c : throw new InvalidOperationException($"no `this` at line {line}");

    /// <summary>
    /// K2, K9, M4: an assignment into a <c>mut</c> field of an instance, the value read first (the owner, 2026-10-24, "Value first"),
    /// then the instance the field is in, then the field, which lets go of its old value after it holds the new one.
    /// </summary>
    private void EmitFieldAssignment(Expression value, KurzType targetType, Func<(string Pointer, ClassSymbol Symbol)> instance, string field)
    {
        var owned = Convert(Owned(value), TypeOf(value), targetType, value.Line);
        var (pointer, symbol) = instance();
        var index = symbol.FieldIndex(field);
        Store(owned, symbol.Fields[index].Type, InstanceFieldPointer(symbol, Address(pointer, symbol), index));
    }

    /// <summary>M5, M9: the place of a list, a map or a hasher held by a field of an instance, for a <c>mut</c> member; null for any other receiver.</summary>
    private string? InstanceFieldPlace(Expression receiver)
    {
        switch (Unwrap(receiver))
        {
            case NameExpression n when Model.IsImplicitField(n, out _, out var field) && field is not null:
                {
                    var symbol = ClassOfThis(n, n.Line).Symbol;
                    var index = symbol.FieldIndex(field.Name);
                    return NarrowedPlace(InstanceFieldPointer(symbol, Address(This(n, n.Line), symbol), index), field.Type, TypeOf(receiver), n.Line).Place;
                }

            case MemberAccess m when TypeOf(m.Target) is ClassType c:
                {
                    var index = c.Symbol.FieldIndex(m.Member);
                    return NarrowedPlace(InstanceFieldPointer(c.Symbol, Address(EmitExpression(m.Target), c.Symbol), index), c.Symbol.Fields[index].Type, TypeOf(receiver), m.Line).Place;
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// K7: a call of a method, the instance before the dot, or <c>this</c> where the call names the method alone, passed first and
    /// borrowed for the call, then the arguments as a function's (F13).
    /// </summary>
    private string EmitMethodCall(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var (receiver, receiverType) = c.Callee switch
        {
            MemberAccess m => (EmitExpression(m.Target), (ClassType)TypeOf(m.Target)),
            NameExpression n => (This(n, n.Line), ClassOfThis(n, n.Line)),
            _ => throw new InvalidOperationException($"a method called through `{c.Callee.GetType().Name}` at line {c.Line}"),
        };
        var arguments = Arguments(
            c,
            info,
            j => Sub(f.Parameters[j].Type),
            j => f.Parameters[j].Mut,
            j => Default(f.Declaration!.Parameters[j].Default, owners[f]),
            cConvention: false,
            owned: false);
        return CallOn(f, receiver, receiverType, arguments);
    }

    /// <summary>
    /// K7: a call of the method on an instance held by a value of <paramref name="receiverType"/>, the instance first, with its table
    /// where the method's class has a base or derived classes. A <c>virtual</c> method called through a value that can be an instance of
    /// a derived class runs what the table of the instance's class holds (K6, A9); every other call is direct.
    /// </summary>
    private string CallOn(FunctionSymbol f, string receiver, ClassType receiverType, IEnumerable<string> arguments)
    {
        var self = $"ptr {receiver}";
        var callee = Instance(f, NoMap);
        if (Related(f.Owner!))
        {
            var pair = Layout.Fat(receiverType.Symbol) ? receiver : Pair(receiver, Table(receiverType.Symbol));
            self = $"%kz.obj {pair}";
            if (f.IsVirtual && Layout.Fat(receiverType.Symbol))
            {
                var root = Root(f);
                var slot = MethodSlots + VirtualMethods(root.Owner!).IndexOf(root);
                var table = fn.Value($"extractvalue %kz.obj {pair}, 1");
                callee = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(slot)}")}");
            }
        }

        var result = Sub(f.ReturnType);
        var call = $"call {Ir(result)} {callee}({string.Join(", ", arguments.Prepend(self))})";
        return Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>A5, A9: the text of an instance, which its <c>Text()</c> gives, the override of the instance's class where it is <c>virtual</c>; the caller holds a count of it.</summary>
    private string InstanceText(string value, ClassType type) =>
        CallOn(type.Symbol.TextMethod ?? throw new InvalidOperationException($"`{type}` has no `Text()`: `no-text`, which the checker reports (A4)"), value, type, []);

    /// <summary>One more strong count of an instance.</summary>
    private string RetainInstance() => Helper("retain.instance", "void", ["ptr"], () =>
    {
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value("load i64, ptr %a0")}, 1")}, ptr %a0");
        fn.Return(null);
    });

    /// <summary>
    /// One strong count less of an instance of the class. The last puts the instance on its class's work list, linked through its
    /// strong count, which is no count from there on, and, unless a release is already working the lists off, works them off
    /// (<see cref="EmitDrain"/>): so an instance freed while another one's fields are let go of waits on its list instead of being
    /// freed inside that release, and a chain of any length is freed with no deeper stack than the nesting of the types in it (the
    /// owner, 2026-11-01, "Iterative, never crashes").
    /// </summary>
    private string ReleaseInstance(ClassSymbol symbol) => Helper($"release.class.{symbol.Name}", "void", ["ptr"], () =>
    {
        var list = $"@\"pending.class.{symbol.Name}\"";
        module.Global($"{list} = internal thread_local global ptr null");
        released.Add((DropInstance(symbol), list));
        var strong = fn.Value($"sub i64 {fn.Value("load i64, ptr %a0")}, 1");
        fn.Emit($"store i64 {strong}, ptr %a0");
        var last = fn.Label("last");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {strong}, 0"), last, held);
        fn.Block(last);
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {list}")}, ptr %a0");
        fn.Emit($"store ptr %a0, ptr {list}");
        var drain = fn.Label("drain");
        fn.CondBranch(fn.Value("load i1, ptr @\"kz.releasing\""), held, drain);
        fn.Block(drain);
        fn.Emit("store i1 true, ptr @\"kz.releasing\"");
        fn.Emit("call void @\"kz.drain\"()");
        fn.Emit("store i1 false, ptr @\"kz.releasing\"");
        fn.Return(null);
        fn.Block(held);
        fn.Return(null);
    });

    /// <summary>
    /// R3: one more weak count of the block a <c>weak</c> reference points at, none for <c>null</c>, as Rust's <c>Weak::clone</c>
    /// counts: the block stays while a weak reference to it is held, for the reference's read to find its strong count.
    /// </summary>
    private string RetainWeak() => Helper("retain.weak", "void", ["ptr"], () =>
    {
        var count = fn.Label("count");
        var none = fn.Label("none");
        fn.CondBranch(fn.Value("icmp eq ptr %a0, null"), none, count);
        fn.Block(count);
        var weak = fn.Value("getelementptr i64, ptr %a0, i64 1");
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value($"load i64, ptr {weak}")}, 1")}, ptr {weak}");
        fn.Return(null);
        fn.Block(none);
        fn.Return(null);
    });

    /// <summary>
    /// R3: one weak count less of the block of an instance of the class, none for <c>null</c>; the last frees the block, whose
    /// instance was ended when its last strong count went, as Rust's <c>Weak</c> drops (section 3).
    /// </summary>
    private string ReleaseWeak(ClassSymbol symbol) => Helper($"release.weak.{symbol.Name}", "void", ["ptr"], () =>
    {
        var count = fn.Label("count");
        var none = fn.Label("none");
        fn.CondBranch(fn.Value("icmp eq ptr %a0, null"), none, count);
        fn.Block(count);
        var block = layout.ClassBlock(symbol);
        var weakCount = fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1");
        var weak = fn.Value($"sub i64 {fn.Value($"load i64, ptr {weakCount}")}, 1");
        fn.Emit($"store i64 {weak}, ptr {weakCount}");
        var free = fn.Label("free");
        fn.CondBranch(fn.Value($"icmp eq i64 {weak}, 0"), free, none);
        fn.Block(free);
        var end = fn.Value($"getelementptr {block}, ptr null, i32 1");
        fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {fn.Value($"ptrtoint ptr {end} to i64")})");
        fn.Return(null);
        fn.Block(none);
        fn.Return(null);
    });

    /// <summary>
    /// R3: the read of a <c>weak</c> place, a strong <c>T?</c> that holds its target to the end of the statement (the owner,
    /// 2026-11-01, "A strong T?, held"): one more strong count where the target lives, and <c>null</c> where the reference is
    /// <c>null</c> or its target's strong count is 0, as Rust's <c>Weak::upgrade</c>. A target on its class's work list is never read
    /// here: no code of the program runs while the lists are worked off (<see cref="EmitDrain"/>).
    /// </summary>
    private string Upgrade(string value, WeakRefType type) => Temp(Upgraded(value, type), Types.Read(type));

    /// <summary>R3: the strong <c>T?</c> a <c>weak</c> reference reads as (<see cref="Upgrade"/>), whose count the caller holds; with K6 the table goes with the address.</summary>
    private string Upgraded(string value, WeakRefType type)
    {
        if (Layout.Fat(type.Target.Symbol))
        {
            return fn.Value($"insertvalue %kz.obj {value}, ptr {Upgraded(fn.Value($"extractvalue %kz.obj {value}, 0"))}, 0");
        }

        return Upgraded(value);
    }

    private string Upgraded(string value)
    {
        var name = Helper("upgrade.instance", "ptr", ["ptr"], () =>
        {
            var load = fn.Label("load");
            var none = fn.Label("none");
            fn.CondBranch(fn.Value("icmp eq ptr %a0, null"), none, load);
            fn.Block(load);
            var strong = fn.Value("load i64, ptr %a0");
            var live = fn.Label("live");
            fn.CondBranch(fn.Value($"icmp eq i64 {strong}, 0"), none, live);
            fn.Block(live);
            fn.Emit($"store i64 {fn.Value($"add i64 {strong}, 1")}, ptr %a0");
            fn.Return("ptr %a0");
            fn.Block(none);
            fn.Return("ptr null");
        });
        return fn.Value($"call ptr {name}(ptr {value})");
    }

    /// <summary>
    /// R3: a <c>T</c>, a <c>T?</c> or <c>null</c> where a <c>weak T?</c> stands, with a weak count of its own. With
    /// <paramref name="owned"/> the strong count the value came with goes back to the region, which lets go of it at the end of the
    /// statement, so that a temporary target lives as long as R3 says, and the place holds the weak count; else the value is borrowed,
    /// and the region lets go of the weak count, as a callee borrows its arguments for the call.
    /// </summary>
    private string Weaken(string value, KurzType from, WeakRefType to, bool owned)
    {
        if (from is NullType)
        {
            return Layout.Fat(to.Target.Symbol) ? "zeroinitializer" : "null";
        }

        // a `T` or a `T?`, whose class is the target's or derived from it (K6): its address, and the pair with its table where the
        // target's class has derived classes
        var symbol = ((ClassType)(Types.NonNull(from) ?? from)).Symbol;
        var address = Address(value, symbol);
        fn.Emit($"call void {RetainWeak()}(ptr {address})");
        var weak = Layout.Fat(to.Target.Symbol) && !Layout.Fat(symbol) ? Pair(address, Table(symbol)) : value;
        Temp(owned ? value : weak, owned ? from : to);
        return weak;
    }

    /// <summary>
    /// K6: one strong count less of an instance held through a value of a class with derived classes: the release of the instance's
    /// class, which its table holds.
    /// </summary>
    private void ReleaseThroughTable(string value)
    {
        var table = fn.Value($"extractvalue %kz.obj {value}, 1");
        fn.Emit($"call void {fn.Value($"load ptr, ptr {table}")}(ptr {fn.Value($"extractvalue %kz.obj {value}, 0")})");
    }

    /// <summary>R3 with K6: one weak count less of the block a <c>weak</c> reference of a class with derived classes points at, none for <c>null</c>, by the weak release of the instance's class, which its table holds.</summary>
    private string ReleaseWeakThroughTable() => Helper("release.weak.instance", "void", ["%kz.obj"], () =>
    {
        var count = fn.Label("count");
        var none = fn.Label("none");
        var address = fn.Value("extractvalue %kz.obj %a0, 0");
        fn.CondBranch(fn.Value($"icmp eq ptr {address}, null"), none, count);
        fn.Block(count);
        var slot = fn.Value($"getelementptr ptr, ptr {fn.Value("extractvalue %kz.obj %a0, 1")}, i64 1");
        fn.Emit($"call void {fn.Value($"load ptr, ptr {slot}")}(ptr {address})");
        fn.Return(null);
        fn.Block(none);
        fn.Return(null);
    });

    /// <summary>
    /// K13: <c>==</c> on two values of a class with derived classes: false where the instances' classes differ, which their tables tell,
    /// else the equality of that class, which its table holds (<see cref="ClassEqual"/>).
    /// </summary>
    private string EqualThroughTable(string left, string right, ClassSymbol symbol)
    {
        comparedThroughTable.Add(symbol);
        var name = Helper("equal.instance", "i1", ["%kz.obj", "%kz.obj"], () =>
        {
            var table = fn.Value("extractvalue %kz.obj %a0, 1");
            var same = fn.Label("same");
            var differ = fn.Label("differ");
            fn.CondBranch(fn.Value($"icmp eq ptr {table}, {fn.Value("extractvalue %kz.obj %a1, 1")}"), same, differ);
            fn.Block(differ);
            fn.Return("i1 false");
            fn.Block(same);
            var slot = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 2")}");
            fn.Return($"i1 {fn.Value($"call i1 {slot}(ptr {fn.Value("extractvalue %kz.obj %a0, 0")}, ptr {fn.Value("extractvalue %kz.obj %a1, 0")})")}");
        });
        return fn.Value($"call i1 {name}(%kz.obj {left}, %kz.obj {right})");
    }

    /// <summary>K13 with M9: the hash of a key of a class with derived classes, by the hash of the instance's class, which its table holds (<see cref="ClassHash"/>).</summary>
    private string HashThroughTable(string block, string key, ClassSymbol symbol)
    {
        hashedThroughTable.Add(symbol);
        var name = Helper("hash.instance", "i64", ["ptr", "%kz.obj"], () =>
        {
            var slot = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {fn.Value("extractvalue %kz.obj %a1, 1")}, i64 3")}");
            fn.Return($"i64 {fn.Value($"call i64 {slot}(ptr %a0, ptr {fn.Value("extractvalue %kz.obj %a1, 0")})")}");
        });
        return fn.Value($"call i64 {name}(ptr {block}, %kz.obj {key})");
    }

    /// <summary>
    /// K6: the helpers and methods a table of each class the program names holds, made while functions are still emitted: the release,
    /// the weak release and the method each <c>virtual</c> slot runs (<see cref="Implementation"/>).
    /// </summary>
    private void PrepareTable(ClassSymbol symbol)
    {
        ReleaseInstance(symbol);
        ReleaseWeak(symbol);
        foreach (var root in VirtualMethods(symbol))
        {
            Instance(Implementation(symbol, root), NoMap);
        }
    }

    /// <summary>
    /// K13: the equality and the hash in the table of each class the program names, where <c>==</c> or a map's key reads a base of the
    /// class through the table; returns whether it made one not made before, which can name more functions.
    /// </summary>
    private bool TableFacets(HashSet<string> made)
    {
        var more = false;
        foreach (var symbol in tabled.ToList())
        {
            if (symbol.Chain.Any(comparedThroughTable.Contains) && made.Add($"equal {symbol.Name}"))
            {
                ClassEqual(symbol);
                more = true;
            }

            if (symbol.Chain.Any(hashedThroughTable.Contains) && made.Add($"hash {symbol.Name}"))
            {
                ClassHash(symbol);
                more = true;
            }
        }

        return more;
    }

    /// <summary>
    /// K6, K13: the table of each class the program names, a constant array of addresses: the release of an instance, its weak
    /// release, its equality where `==` reads a base of the class through the table and its hash where a map's key does, else null,
    /// and the method each <c>virtual</c> slot runs, the base's slots first, so that a base's slot has one place in every derived
    /// class's table. Emitted after the last function, when every use is known.
    /// </summary>
    private void EmitTables()
    {
        foreach (var symbol in tabled)
        {
            var compared = symbol.Chain.Any(comparedThroughTable.Contains);
            var hashed = symbol.Chain.Any(hashedThroughTable.Contains);
            var entries = new List<string>
            {
                ReleaseInstance(symbol),
                ReleaseWeak(symbol),
                compared ? ClassEqual(symbol) : "null",
                hashed ? ClassHash(symbol) : "null",
            };
            entries.AddRange(VirtualMethods(symbol).Select(root => Instance(Implementation(symbol, root), NoMap)));
            module.Global($"{Table(symbol)} = internal constant [{Number(entries.Count)} x ptr] [{string.Join(", ", entries.Select(e => "ptr " + e))}]");
        }
    }

    /// <summary>
    /// The end of an instance whose last strong count was let go of: its fields let go of in their order, as Rust drops them, and then
    /// the weak count all the strong ones held, whose last frees the block, as Rust's <c>Rc</c> and <c>Weak</c> do (section 3).
    /// </summary>
    private string DropInstance(ClassSymbol symbol) => Helper($"drop.class.{symbol.Name}", "void", ["ptr"], () =>
    {
        for (var i = 0; i < symbol.Fields.Count; i++)
        {
            var type = symbol.Fields[i].Type;
            if (Counted(type))
            {
                Release(fn.Value($"load {Ir(type)}, ptr {InstanceFieldPointer(symbol, "%a0", i)}"), type);
            }
        }

        var block = layout.ClassBlock(symbol);
        var weakCount = fn.Value($"getelementptr {block}, ptr %a0, i32 0, i32 1");
        var weak = fn.Value($"sub i64 {fn.Value($"load i64, ptr {weakCount}")}, 1");
        fn.Emit($"store i64 {weak}, ptr {weakCount}");
        var free = fn.Label("free");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {weak}, 0"), free, held);
        fn.Block(free);
        var end = fn.Value($"getelementptr {block}, ptr null, i32 1");
        fn.Emit($"call void {StdFunction("Free")}(ptr %a0, i64 {fn.Value($"ptrtoint ptr {end} to i64")})");
        fn.Return(null);
        fn.Block(held);
        fn.Return(null);
    });

    /// <summary>
    /// The function that works the lists off: each instance on a class's list is taken off, its strong count set back to 0, and
    /// ended (<see cref="DropInstance"/>), which may put more instances on the lists, until every list is empty. Emitted after the
    /// last function, when every class whose instances are released is known; it calls only the helpers made with them.
    /// </summary>
    private void EmitDrain()
    {
        // declared here, once: the first class's release builds the releases of its fields' classes before it is in `released`
        module.Global("@\"kz.releasing\" = internal thread_local global i1 false");
        fn = new IrFunction("internal void @\"kz.drain\"()");
        (regions, scopes) = ([], []);
        var progress = fn.Alloca("i1", "progress");
        var round = fn.Label("round");
        fn.Branch(round);
        fn.Block(round);
        fn.Emit($"store i1 false, ptr {progress}");
        foreach (var (drop, list) in released)
        {
            var check = fn.Label("check");
            var one = fn.Label("one");
            var next = fn.Label("next");
            fn.Branch(check);
            fn.Block(check);
            var instance = fn.Value($"load ptr, ptr {list}");
            fn.CondBranch(fn.Value($"icmp eq ptr {instance}, null"), next, one);
            fn.Block(one);
            fn.Emit($"store ptr {fn.Value($"load ptr, ptr {instance}")}, ptr {list}");
            fn.Emit($"store i64 0, ptr {instance}");
            fn.Emit($"call void {drop}(ptr {instance})");
            fn.Emit($"store i1 true, ptr {progress}");
            fn.Branch(check);
            fn.Block(next);
        }

        var done = fn.Label("done");
        fn.CondBranch(fn.Value($"load i1, ptr {progress}"), round, done);
        fn.Block(done);
        fn.Return(null);
        emitted.Add(fn);
    }
}
