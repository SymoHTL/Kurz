using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 7: an instance of a class is the address of its block, as Rust's <c>Rc&lt;T&gt;</c> is the address of its <c>RcBox</c>
/// (section 3): the count of the references that hold it, the count of the weak ones, one for all the strong ones together, then its
/// fields in their order, a base's first (<see cref="Layout.ClassBlock"/>); a nullable instance of a class that nothing derives from
/// is the address alone, null its <c>null</c>. A value of a class that another class derives from is the pair of the address and the
/// table of the instance's class, as Rust's <c>Rc&lt;dyn Trait&gt;</c> is the address and its vtable (K6, <see cref="Layout.Fat"/>),
/// and a nullable one is the pair too, its <c>null</c> the pair of zeros (N1): the table holds the helpers that end the instance and
/// free its block, its equality and its hash where the program uses them, and its <c>virtual</c> methods, the base's first
/// (<see cref="EmitTables"/>). The instance is counted as a list is (K2): every place that holds one holds a count, a
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

    // each class whose instances are released, and each family of data types whose blocks are: the helper that ends one, and its work
    // list (the owner, 2026-11-01, "Iterative, never crashes")
    private readonly List<(string Drop, string List)> released = [];

    // K6: the classes whose tables the program names, in the order first named, and the classes whose values `==` and a map's hash
    // read through the table (K13)
    private readonly List<ClassSymbol> tabled = [];
    private readonly HashSet<ClassSymbol> comparedThroughTable = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ClassSymbol> hashedThroughTable = new(ReferenceEqualityComparer.Instance);

    // K6: the pairs of a class and an interface it implements whose tables the program names, in the order first named, and the
    // interfaces whose values `==` and a map's hash read through those tables (K13)
    private readonly List<(ClassSymbol Class, InterfaceSymbol Face)> interfaceTabled = [];
    private readonly HashSet<InterfaceSymbol> comparedThroughInterface = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<InterfaceSymbol> hashedThroughInterface = new(ReferenceEqualityComparer.Instance);

    // K6: the slots of a table before its methods: the release, the weak release, the equality, the hash and the link, which in a
    // class's table is its base's table and in a class's table for an interface the class's own table, where it has one
    private const int MethodSlots = 5;
    private const int LinkSlot = 4;

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

    /// <summary>
    /// K6: the table of the class for an interface it implements, emitted after the last function (<see cref="EmitTables"/>): the
    /// release, the weak release, the equality and the hash of the class, as its own table holds them, then the method of the class
    /// that implements each method of the interface, in the order the interface writes them, as Rust's vtable of a type for a trait.
    /// </summary>
    private string InterfaceTable(ClassSymbol symbol, InterfaceSymbol face)
    {
        if (!interfaceTabled.Contains((symbol, face)))
        {
            interfaceTabled.Add((symbol, face));
        }

        layout.Pair();
        return $"@\"table.{symbol.Name}.{face.Name}\"";
    }

    /// <summary>
    /// K6: the slots of a class's table after the release, the weak release, the equality and the hash: its base's first, then the
    /// methods it marks <c>virtual</c>, in the order written, then the table for each interface it names that its bases do not, so
    /// that a base's slot has one place in every derived class's table.
    /// </summary>
    private static List<object> Slots(ClassSymbol symbol) =>
        [
            .. symbol.Base is { } b ? Slots(b) : [],
            .. symbol.Methods.Values.SelectMany(list => list).Where(m => (m.Declaration!.Modifiers & Modifiers.Virtual) != 0).OrderBy(m => m.Line),
            .. symbol.Interfaces.Where(face => symbol.Base is not { } b || !b.Implements(face)),
        ];

    /// <summary>
    /// K6: an instance of the class, held by a value of its type, as a value of an interface it implements: the pair of its address and
    /// its class's table for the interface, which a class with derived classes reads from the table of the instance's class.
    /// </summary>
    private string AsInterface(string value, ClassSymbol symbol, InterfaceSymbol face)
    {
        if (!Layout.Fat(symbol))
        {
            return Pair(value, InterfaceTable(symbol, face));
        }

        var owner = symbol.Chain.Last(c => c.Interfaces.Contains(face));
        var slot = MethodSlots + Slots(owner).IndexOf(face);
        var table = fn.Value($"extractvalue %kz.obj {value}, 1");
        var faceTable = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(slot)}")}");
        return Pair(fn.Value($"extractvalue %kz.obj {value}, 0"), faceTable);
    }

    /// <summary>K6, K7: the method of the class that implements the interface's method: the nearest of its chain with the method's name, parameter types and <c>mut</c> marks, which a call on an instance of the class runs.</summary>
    private static FunctionSymbol Implementing(ClassSymbol symbol, FunctionSymbol method) =>
        symbol.Chain.SelectMany(c => c.Methods.TryGetValue(method.Name, out var list) ? list : [])
            .First(m => m.Parameters.Count == method.Parameters.Count && m.Parameters.Zip(method.Parameters).All(p => Types.Same(p.First.Type, p.Second.Type)) && Checker.SameMut(m, method));

    /// <summary>
    /// K6, K7: what the slot of the interface's method in the class's table for it holds: the method itself, whose instance is its
    /// address, or, for a class whose methods take the instance with its table, a function that takes the address and calls the
    /// method with the class's own table, which is the instance's, since the table for the interface is the class's own.
    /// </summary>
    private string InterfaceSlot(ClassSymbol symbol, InterfaceSymbol face, FunctionSymbol method)
    {
        var implementation = Implementing(symbol, method);
        var name = Instance(implementation, NoMap);
        if (!Related(implementation.Owner!))
        {
            return name;
        }

        var result = Ir(method.ReturnType);
        var parameters = method.Parameters.Select(p => p.Mut ? "ptr" : Ir(p.Type)).ToList();
        return Helper($"face.{symbol.Name}.{face.Name}.{Number(face.Methods.IndexOf(method))}", result, ["ptr", .. parameters], () =>
        {
            var self = Pair("%a0", Table(symbol));
            var arguments = parameters.Select((type, i) => $"{type} %a{Number(i + 1)}").Prepend($"%kz.obj {self}");
            var call = $"call {result} {name}({string.Join(", ", arguments)})";
            if (Types.IsVoid(method.ReturnType))
            {
                fn.Emit(call);
                fn.Return(null);
            }
            else
            {
                fn.Return($"{result} {fn.Value(call)}");
            }
        });
    }

    /// <summary>
    /// C4 with K6: whether the instance a value of <paramref name="from"/> holds, a class some class derives from or an interface, is
    /// of the arm's class or of a class derived from it, and that value as the arm's type (the owner, 2026-11-02, #110, as C#'s
    /// <c>is</c>). A class's table links its base's, as the CLR's method table its parent, and the test walks from the table of the
    /// instance's class to the arm's (<see cref="Descends"/>); through an interface it reads that table from the class's table for the
    /// interface, and a class with no base and no derived class is that table for the interface itself, one comparison.
    /// </summary>
    private (string Holds, string Value) ClassArm(string value, KurzType from, ClassSymbol arm)
    {
        var address = fn.Value($"extractvalue %kz.obj {value}, 0");
        var table = fn.Value($"extractvalue %kz.obj {value}, 1");
        if (from is InterfaceType face)
        {
            if (!Related(arm))
            {
                return (fn.Value($"icmp eq ptr {table}, {InterfaceTable(arm, face.Symbol)}"), address);
            }

            table = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(LinkSlot)}")}");
        }

        return (Descends(table, arm), Layout.Fat(arm) ? Pair(address, table) : address);
    }

    /// <summary>
    /// C4 with K6: whether the class whose table <paramref name="table"/> is, null for none, is <paramref name="arm"/> or derives from
    /// it: one comparison where nothing derives from the arm's class, else a walk through the tables' links.
    /// </summary>
    private string Descends(string table, ClassSymbol arm)
    {
        if (!arm.Derived)
        {
            return fn.Value($"icmp eq ptr {table}, {Table(arm)}");
        }

        var walk = Helper("class.descends", "i1", ["ptr", "ptr"], () =>
        {
            var at = fn.Alloca("ptr", "table");
            fn.Emit($"store ptr %a0, ptr {at}");
            var loop = fn.Label("loop");
            var check = fn.Label("check");
            var up = fn.Label("up");
            var yes = fn.Label("yes");
            var no = fn.Label("no");
            fn.Branch(loop);
            fn.Block(loop);
            var current = fn.Value($"load ptr, ptr {at}");
            fn.CondBranch(fn.Value($"icmp eq ptr {current}, %a1"), yes, check);
            fn.Block(check);
            fn.CondBranch(fn.Value($"icmp eq ptr {current}, null"), no, up);
            fn.Block(up);
            fn.Emit($"store ptr {fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {current}, i64 {Number(LinkSlot)}")}")}, ptr {at}");
            fn.Branch(loop);
            fn.Block(yes);
            fn.Return("i1 true");
            fn.Block(no);
            fn.Return("i1 false");
        });
        return fn.Value($"call i1 {walk}(ptr {table}, ptr {Table(arm)})");
    }

    /// <summary>K6, K7: whether the methods of the class take the instance with its table: a class with a base or with derived classes.</summary>
    private static bool Related(ClassSymbol symbol) => symbol.Base is not null || symbol.Derived;

    /// <summary>K7: the method a chain of overrides starts from, the one marked <c>virtual</c>, whose slot each override fills.</summary>
    private static FunctionSymbol Root(FunctionSymbol method) => method.Overridden is { } overridden ? Root(overridden) : method;

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
        if (c.Callee is MemberAccess { Target: var target } && TypeOf(target) is InterfaceType face)
        {
            // the receiver is borrowed past the arguments, as a class's is below: one read from a field holds a count of its own to the end
            // of the statement (InstanceField), and one in a variable cannot change while they run (M7, F15, F16)
            var value = EmitExpression(target);
            var passed = Arguments(
                c,
                info,
                j => Sub(f.Parameters[j].Type),
                j => f.Parameters[j].Mut,
                j => throw new InvalidOperationException($"a default of the method `{f.Name}` of an interface at line {c.Line}"),
                cConvention: false,
                owned: false);
            return CallThrough(f, value, face.Symbol, passed);
        }

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
                var slot = MethodSlots + Slots(root.Owner!).IndexOf(root);
                var table = fn.Value($"extractvalue %kz.obj {pair}, 1");
                callee = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(slot)}")}");
            }
        }

        var result = Sub(f.ReturnType);
        var call = $"call {Ir(result)} {callee}({string.Join(", ", arguments.Prepend(self))})";
        return Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>
    /// K6, K7: a call of the interface's method on a value of it: the instance's address first, then the arguments, to what the slot of
    /// the method holds in the instance's class's table for the interface (<see cref="InterfaceSlot"/>), as Rust calls through a
    /// <c>dyn Trait</c>'s vtable.
    /// </summary>
    private string CallThrough(FunctionSymbol method, string value, InterfaceSymbol face, IEnumerable<string> arguments)
    {
        var slot = MethodSlots + face.Methods.IndexOf(method);
        var table = fn.Value($"extractvalue %kz.obj {value}, 1");
        var callee = fn.Value($"load ptr, ptr {fn.Value($"getelementptr ptr, ptr {table}, i64 {Number(slot)}")}");
        var self = $"ptr {fn.Value($"extractvalue %kz.obj {value}, 0")}";
        var result = Sub(method.ReturnType);
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
        var list = WorkList($"class.{symbol.Name}", () => DropInstance(symbol));
        var strong = fn.Value($"sub i64 {fn.Value("load i64, ptr %a0")}, 1");
        fn.Emit($"store i64 {strong}, ptr %a0");
        var last = fn.Label("last");
        var held = fn.Label("held");
        fn.CondBranch(fn.Value($"icmp eq i64 {strong}, 0"), last, held);
        fn.Block(last);
        Enlist(list);
        fn.Block(held);
        fn.Return(null);
    });

    /// <summary>
    /// The work list of the values the helper <paramref name="drop"/> makes ends, a global of the thread's own, which <see cref="EmitDrain"/>
    /// works off; the flag of a release under way, which every list shares, is declared once, by <see cref="EmitDrain"/>.
    /// </summary>
    private string WorkList(string name, Func<string> drop)
    {
        var list = $"@\"pending.{name}\"";
        module.Global($"{list} = internal thread_local global ptr null");
        released.Add((drop(), list));
        return list;
    }

    /// <summary>
    /// In a release helper, the value <c>%a0</c> whose last count was let go of put on its work list, linked through that count at its
    /// start, which is no count from there on, and, unless a release is already working the lists off, the lists worked off
    /// (<see cref="EmitDrain"/>); then the return.
    /// </summary>
    private void Enlist(string list)
    {
        fn.Emit($"store ptr {fn.Value($"load ptr, ptr {list}")}, ptr %a0");
        fn.Emit($"store ptr %a0, ptr {list}");
        var drain = fn.Label("drain");
        var waits = fn.Label("waits");
        fn.CondBranch(fn.Value("load i1, ptr @\"kz.releasing\""), waits, drain);
        fn.Block(drain);
        fn.Emit("store i1 true, ptr @\"kz.releasing\"");
        fn.Emit("call void @\"kz.drain\"()");
        fn.Emit("store i1 false, ptr @\"kz.releasing\"");
        fn.Return(null);
        fn.Block(waits);
        fn.Return(null);
    }

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
        return EqualPairs(left, right);
    }

    /// <summary>K13: <c>==</c> on two pairs of an instance and its class's table, for the class or for an interface: false where the tables differ, else the equality in the table.</summary>
    private string EqualPairs(string left, string right)
    {
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
        return HashPair(block, key);
    }

    /// <summary>K13 with M9: the hash of a key that is a pair of an instance and its class's table, for the class or for an interface, by the hash in the table.</summary>
    private string HashPair(string block, string key)
    {
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
        if (symbol.Base is { } b)
        {
            Table(b); // the link: a class test walks from the instance's class through its bases (C4, K6)
        }

        foreach (var slot in Slots(symbol))
        {
            _ = slot is FunctionSymbol root ? Instance(Implementation(symbol, root), NoMap) : InterfaceTable(symbol, (InterfaceSymbol)slot);
        }
    }

    /// <summary>K6: the helpers and methods the class's table for the interface holds, made while functions are still emitted.</summary>
    private void PrepareInterfaceTable(ClassSymbol symbol, InterfaceSymbol face)
    {
        ReleaseInstance(symbol);
        ReleaseWeak(symbol);
        if (Related(symbol))
        {
            Table(symbol); // the link: a class test reads the instance's class from the table for the interface (C4, K6)
        }

        foreach (var method in face.Methods)
        {
            InterfaceSlot(symbol, face, method);
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

        foreach (var (symbol, face) in interfaceTabled.ToList())
        {
            if (comparedThroughInterface.Contains(face) && made.Add($"equal {symbol.Name}"))
            {
                ClassEqual(symbol);
                more = true;
            }

            if (hashedThroughInterface.Contains(face) && made.Add($"hash {symbol.Name}"))
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
    /// the table of its base, null for a class with none, and the method each <c>virtual</c> slot runs, the base's slots first, so
    /// that a base's slot has one place in every derived class's table; and of each class's table for an interface, the same four,
    /// the class's own table where the class has a base or derived classes, else null, then its methods. Emitted after the last
    /// function, when every use is known.
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
                symbol.Base is { } b ? Table(b) : "null",
            };
            entries.AddRange(Slots(symbol).Select(slot => slot is FunctionSymbol root ? Instance(Implementation(symbol, root), NoMap) : InterfaceTable(symbol, (InterfaceSymbol)slot)));
            module.Global($"{Table(symbol)} = internal constant [{Number(entries.Count)} x ptr] [{string.Join(", ", entries.Select(e => "ptr " + e))}]");
        }

        foreach (var (symbol, face) in interfaceTabled)
        {
            var entries = new List<string>
            {
                ReleaseInstance(symbol),
                ReleaseWeak(symbol),
                comparedThroughInterface.Contains(face) ? ClassEqual(symbol) : "null",
                hashedThroughInterface.Contains(face) ? ClassHash(symbol) : "null",
                Related(symbol) ? Table(symbol) : "null",
            };
            entries.AddRange(face.Methods.Select(method => InterfaceSlot(symbol, face, method)));
            module.Global($"{InterfaceTable(symbol, face)} = internal constant [{Number(entries.Count)} x ptr] [{string.Join(", ", entries.Select(e => "ptr " + e))}]");
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
    /// The function that works the lists off: each instance on a class's list, and each block on its family's, is taken off, its
    /// count set back to 0, and ended (<see cref="DropInstance"/>, <see cref="DropBlock"/>), which may put more on the lists, until
    /// every list is empty. Emitted after the last function, when every class whose instances are released and every family whose
    /// blocks are is known; it calls only the helpers made with them.
    /// </summary>
    private void EmitDrain()
    {
        // declared here, once: the first release built, a class's or a family's of blocks, builds the releases of its fields' types
        // before it is in `released`
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
