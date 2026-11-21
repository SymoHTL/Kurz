using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// R19: the atomic types, lowered as Rust lowers <c>core::sync::atomic</c>: a view is the pointer to its word; <c>Load</c> and
/// <c>Store</c> are LLVM's <c>load atomic</c> and <c>store atomic</c>, <c>Swap</c> and the <c>Fetch</c> members its
/// <c>atomicrmw</c>, <c>CompareExchange</c> and <c>CompareExchangeWeak</c> its <c>cmpxchg</c> and <c>cmpxchg weak</c>, and
/// <c>Fence</c> its <c>fence</c>, each aligned to its word's size, a <c>bool</c>'s word a byte that holds 0 or 1, as Rust's
/// <c>AtomicBool</c> holds a <c>u8</c>. An order known when the program is built, a value of <c>MemoryOrder</c> written at the call or
/// held by a copy of a function as its constant (T35), is the instruction's own, a copy's constant that Rust refuses raising
/// <c>invalid-ordering-at-run-time</c> at the call; any other is read when the call runs, by a choice among the instruction's forms for each order, as Rust's
/// <c>match</c> on the order chooses among its intrinsics, and an order Rust refuses there ends as Rust's panic does, with the
/// exception <c>invalid-ordering-at-run-time</c> at the call's line.
/// </summary>
public sealed partial class Emitter
{
    /// <summary>LLVM's names of the values of <c>MemoryOrder</c>, by their places: <c>Relaxed</c> is LLVM's <c>monotonic</c>.</summary>
    private static readonly string[] AtomicOrderings = ["monotonic", "release", "acquire", "acq_rel", "seq_cst"];

    /// <summary>R19: <c>AtomicLong.FromPtr(p)</c>, the pointer itself, which the view holds.</summary>
    private string EmitAtomicFromPtr(CallExpression c, CallInfo info) =>
        Arguments(c, info, _ => new PointerType(((AtomicType)Sub(info.Target!)).Word), _ => false, _ => null, cConvention: false, owned: false).Single()["ptr ".Length..];

    /// <summary>The type of the word in memory: a <c>bool</c>'s is a byte.</summary>
    private static string WordIr(AtomicType atomic) => atomic.Kind switch
    {
        Atomic.Bool => "i8",
        Atomic.Int => "i32",
        Atomic.Long => "i64",
        _ => "ptr",
    };

    /// <summary>A value of the word's Kurz type as the word holds it: a <c>bool</c> widened to its byte.</summary>
    private string ToWord(AtomicType atomic, string value) => atomic.Kind == Atomic.Bool ? fn.Value($"zext i1 {value} to i8") : value;

    /// <summary>The word as a value of its Kurz type: a <c>bool</c>'s byte is true where it is not 0, as Rust reads it.</summary>
    private string FromWord(AtomicType atomic, string value) => atomic.Kind == Atomic.Bool ? fn.Value($"icmp ne i8 {value}, 0") : value;

    /// <summary>R19: a member of a view (<see cref="CallKind.Atomic"/>), the view first, then its arguments in the order they are written.</summary>
    private string? EmitAtomic(CallExpression c, CallInfo info)
    {
        var atomic = (AtomicType)Sub(info.Target!);
        var member = ((MemberAccess)c.Callee).Member;
        var word = EmitExpression(((MemberAccess)c.Callee).Target);
        var names = member switch
        {
            "Load" => ["order"],
            "CompareExchange" or "CompareExchangeWeak" => ["current", "new", "success", "failure"],
            _ => (string[])["val", "order"],
        };
        var values = new string[names.Length];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = info.Binding[i];
            var a = c.Arguments[i].Value;
            var type = names[slot] switch
            {
                "order" or "success" or "failure" => TypeOf(a),
                "val" when atomic.Kind == Atomic.Ptr && member is "FetchAnd" or "FetchOr" or "FetchXor" => Types.Long,
                _ => atomic.Word,
            };
            values[slot] = Convert(EmitExpression(a), TypeOf(a), type, a.Line);
        }

        var orderSlots = Enumerable.Range(0, names.Length).Where(i => names[i] is "order" or "success" or "failure").ToList();
        var known = orderSlots.Select(i => Model.TypeOf(c.Arguments[Array.IndexOf(info.Binding.ToArray(), i)].Value).Constant is EnumConstant e ? e.Index : -1).ToList();
        var result = Sub(info.Result);
        var ty = WordIr(atomic);
        var align = atomic.Kind switch { Atomic.Bool => 1, Atomic.Int => 4, _ => 8 };
        return Ordered(member, orderSlots.Select(i => names[i]).ToList(), orderSlots.Select(i => values[i]).ToList(), known, Types.IsVoid(result) ? null : Ir(result), c.Line, orders => member switch
        {
            "Load" => FromWord(atomic, fn.Value($"load atomic {ty}, ptr {word} {orders[0]}, align {align}")),
            "Store" => StoreAtomic(ty, ToWord(atomic, values[0]), word, orders[0], align),
            "Swap" => FromWord(atomic, fn.Value($"atomicrmw xchg ptr {word}, {ty} {ToWord(atomic, values[0])} {orders[0]}, align {align}")),
            "CompareExchange" or "CompareExchangeWeak" => CompareExchange(atomic, (UnionType)result, word, values[0], values[1], member == "CompareExchangeWeak", orders, align),
            _ => FetchOperation(atomic, member, word, values[0], orders[0], align),
        });
    }

    private string? StoreAtomic(string ty, string value, string word, string order, int align)
    {
        fn.Emit($"store atomic {ty} {value}, ptr {word} {order}, align {align}");
        return null;
    }

    /// <summary>R19: <c>FetchAdd</c>, <c>FetchSub</c>, <c>FetchAnd</c>, <c>FetchOr</c> and <c>FetchXor</c>, the value the word held before; an <c>AtomicPtr&lt;T&gt;</c>'s bits taken on the address as a <c>long</c>, as Rust's on a <c>usize</c>.</summary>
    private string FetchOperation(AtomicType atomic, string member, string word, string value, string order, int align)
    {
        var operation = member switch
        {
            "FetchAdd" => "add",
            "FetchSub" => "sub",
            "FetchAnd" => "and",
            "FetchOr" => "or",
            _ => "xor",
        };
        if (atomic.Kind == Atomic.Ptr)
        {
            var bits = fn.Value($"atomicrmw {operation} ptr {word}, i64 {value} {order}, align {align}");
            return fn.Value($"inttoptr i64 {bits} to ptr");
        }

        return FromWord(atomic, fn.Value($"atomicrmw {operation} ptr {word}, {WordIr(atomic)} {ToWord(atomic, value)} {order}, align {align}"));
    }

    /// <summary>
    /// R19: <c>CompareExchange</c> and <c>CompareExchangeWeak</c>, LLVM's <c>cmpxchg</c>: the value found where it stored, the union's
    /// first case, and the case <c>NotExchanged</c> with the value found where it did not, as Rust's <c>Ok</c> and <c>Err</c>.
    /// </summary>
    private string CompareExchange(AtomicType atomic, UnionType union, string word, string current, string next, bool weak, IReadOnlyList<string> orders, int align)
    {
        var ty = WordIr(atomic);
        var pair = fn.Value($"cmpxchg {(weak ? "weak " : "")}ptr {word}, {ty} {ToWord(atomic, current)}, {ty} {ToWord(atomic, next)} {orders[0]} {orders[1]}, align {align}");
        var found = FromWord(atomic, fn.Value($"extractvalue {{ {ty}, i1 }} {pair}, 0"));
        var stored = fn.Value($"extractvalue {{ {ty}, i1 }} {pair}, 1");
        var failure = (DataType)union.Cases[1];
        var carried = fn.Value($"insertvalue {Ir(failure)} undef, {Ir(atomic.Word)} {found}, 0");
        var success = MakeUnion(union, union.Cases[0], found);
        var other = MakeUnion(union, failure, carried);
        return fn.Value($"select i1 {stored}, {Ir(union)} {success}, {Ir(union)} {other}");
    }

    /// <summary>R19: <c>Fence(order)</c>, LLVM's <c>fence</c>.</summary>
    private string? EmitFence(CallExpression c, CallInfo info)
    {
        var a = c.Arguments[0].Value;
        var order = EmitExpression(a);
        var known = Model.TypeOf(a).Constant is EnumConstant e ? e.Index : -1;
        return Ordered("Fence", ["order"], [order], [known], null, c.Line, orders =>
        {
            fn.Emit($"fence {orders[0]}");
            return null;
        });
    }

    /// <summary>
    /// The operation for its orders: each known one taken as it is, each other read from its value by a <c>switch</c> over the five
    /// values of <c>MemoryOrder</c>, so that the operation is emitted once for each choice; a choice Rust refuses for the operation
    /// (<see cref="AtomicOrders"/>) raises <c>invalid-ordering-at-run-time</c> at <paramref name="line"/>. The result, of the IR type
    /// <paramref name="result"/>, null where the operation gives none.
    /// </summary>
    private string? Ordered(string member, List<string> slots, List<string> values, List<int> known, string? result, int line, Func<IReadOnlyList<string>, string?> body)
    {
        if (known.All(k => k >= 0))
        {
            var chosen = known.Select((k, i) => (Index: k, Slot: slots[i])).ToList();
            if (chosen.Any(o => AtomicOrders.Refused(member, o.Slot, OrderName(o.Index)) is not null))
            {
                Raise(ErrorIds.InvalidOrderingAtRunTime, line); // an order known only in a copy (T35), which no written value refused
                fn.Block(fn.Label("after.order"));
                return result is null ? null : "poison";
            }

            return body(chosen.Select(o => AtomicOrderings[o.Index]).ToList());
        }

        var slot = result is null ? null : fn.Alloca(result, "atomic");
        var done = fn.Label("atomic.done");
        Choose(0, []);
        fn.Block(done);
        return slot is null ? null : fn.Value($"load {result}, ptr {slot}");

        void Choose(int i, List<int> picked)
        {
            if (i == known.Count)
            {
                if (picked.Select((k, j) => AtomicOrders.Refused(member, slots[j], OrderName(k))).Any(r => r is not null))
                {
                    Raise(ErrorIds.InvalidOrderingAtRunTime, line);
                    return;
                }

                var value = body(picked.Select(k => AtomicOrderings[k]).ToList());
                if (slot is not null)
                {
                    fn.Emit($"store {result} {value}, ptr {slot}");
                }

                fn.Branch(done);
                return;
            }

            if (known[i] >= 0)
            {
                Choose(i + 1, [.. picked, known[i]]);
                return;
            }

            var labels = AtomicOrderings.Select(_ => fn.Label("order")).ToList();
            var none = fn.Label("order.none");
            fn.Terminate($"switch i8 {values[i]}, label %{none} [ {string.Join(" ", labels.Select((l, k) => $"i8 {k}, label %{l}"))} ]");
            for (var k = 0; k < labels.Count; k++)
            {
                fn.Block(labels[k]);
                Choose(i + 1, [.. picked, k]);
            }

            fn.Block(none);
            fn.Unreachable(); // a value of `MemoryOrder` is one of its five, stored as its place (D18)
        }
    }

    /// <summary>The names of the values of <c>MemoryOrder</c>, by their places, as the built-in declaration writes them (R19).</summary>
    private static readonly string[] OrderNames = ["Relaxed", "Release", "Acquire", "AcqRel", "SeqCst"];

    private static string OrderName(int index) => OrderNames[index];
}
