using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 7: an instance of a class is the address of its block, as Rust's <c>Rc&lt;T&gt;</c> is the address of its <c>RcBox</c>
/// (section 3): the count of the references that hold it, the count of the weak ones, one for all the strong ones together, then its
/// fields in their order (<see cref="Layout.ClassBlock"/>); a nullable instance is the address alone, null its <c>null</c>. The
/// instance is counted as a list is (K2): every place that holds one holds a count, a variable lets go of it at the end of its block,
/// and the last count lets go of the fields in their order, as Rust drops them, and then of the weak count the strong ones held; the
/// last weak count frees the block. An instance whose last count a release lets go of waits on a work list until that release ends
/// (<see cref="ReleaseInstance"/>). No count is elided. A method is a function of the module whose first parameter is the instance,
/// <c>this</c>, which it borrows from the caller as a function borrows its arguments (K7). A read of a counted field through an
/// instance takes a count of its own, a temporary of the region, since a call can assign the field through another reference while the
/// value is still in use (K2, K9).
/// </summary>
public sealed partial class Emitter
{
    private readonly Dictionary<ClassSymbol, Unit> classOwners = new(ReferenceEqualityComparer.Instance);

    // each class whose instances are released: the helper that ends one, and its work list (the owner, 2026-11-01, "Iterative, never crashes")
    private readonly List<(string Drop, string List)> released = [];

    /// <summary>K1: an instance with both counts at one and each field holding the argument bound to it or its default.</summary>
    private string EmitClassNew(CallExpression c, CallInfo info)
    {
        var symbol = ((ClassType)info.Target!).Symbol;
        var fields = symbol.Fields.Select(f => f.Type).ToList();
        var arguments = Arguments(c, info, j => fields[j], _ => false, j => ClassFieldDefault(symbol, j), cConvention: false, owned: true);
        var block = layout.ClassBlock(symbol);
        var p = Allocate(block);
        fn.Emit($"store i64 1, ptr {p}");
        fn.Emit($"store i64 1, ptr {fn.Value($"getelementptr {block}, ptr {p}, i32 0, i32 1")}");
        for (var i = 0; i < fields.Count; i++)
        {
            fn.Emit($"store {arguments[i]}, ptr {InstanceFieldPointer(symbol, p, i)}");
        }

        return p;
    }

    /// <summary>The default written for the field of the primary constructor in the slot, and the file of the class.</summary>
    private (Expression Value, Unit File) ClassFieldDefault(ClassSymbol symbol, int slot) =>
        ((symbol.Declaration.PrimaryConstructor ?? [])[slot].Default ?? throw new InvalidOperationException($"the field {slot} of `{symbol.Name}` has no argument and no default"), classOwners[symbol]);

    /// <summary>The address of field <paramref name="index"/> in the block of an instance: the two counts come first.</summary>
    private string InstanceFieldPointer(ClassSymbol symbol, string pointer, int index) =>
        fn.Value($"getelementptr {layout.ClassBlock(symbol)}, ptr {pointer}, i32 0, i32 {Number(index + 2)}");

    /// <summary>K1, K2: a field of an instance, of a counted type with a count of its own that the region lets go of.</summary>
    private string InstanceField(string pointer, ClassSymbol symbol, int index)
    {
        var type = symbol.Fields[index].Type;
        var value = fn.Value($"load {Ir(type)}, ptr {InstanceFieldPointer(symbol, pointer, index)}");
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

        return fn.Value($"load ptr, ptr {Place(self, line)}");
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
        Store(owned, symbol.Fields[index].Type, InstanceFieldPointer(symbol, pointer, index));
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
                    return NarrowedPlace(InstanceFieldPointer(symbol, This(n, n.Line), index), field.Type, TypeOf(receiver), n.Line).Place;
                }

            case MemberAccess m when TypeOf(m.Target) is ClassType c:
                {
                    var index = c.Symbol.FieldIndex(m.Member);
                    return NarrowedPlace(InstanceFieldPointer(c.Symbol, EmitExpression(m.Target), index), c.Symbol.Fields[index].Type, TypeOf(receiver), m.Line).Place;
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
        var receiver = c.Callee switch
        {
            MemberAccess m => EmitExpression(m.Target),
            NameExpression n => This(n, n.Line),
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
        var result = Sub(f.ReturnType);
        var call = $"call {Ir(result)} {Instance(f, NoMap)}({string.Join(", ", arguments.Prepend($"ptr {receiver}"))})";
        return Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>A5: the text of an instance, which its <c>Text()</c> gives; the caller holds a count of it.</summary>
    private string InstanceText(string value, ClassType type) =>
        fn.Value($"call %kz.str {Instance(type.Symbol.TextMethod ?? throw new InvalidOperationException($"`{type}` has no `Text()`: `no-text`, which the checker reports (A4)"), NoMap)}(ptr {value})");

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
        if (released.Count == 0)
        {
            module.Global("@\"kz.releasing\" = internal thread_local global i1 false");
        }

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
