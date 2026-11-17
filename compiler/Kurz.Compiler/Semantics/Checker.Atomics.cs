using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// R19: the atomic types behind <c>raw</c>, Rust's <c>core::sync::atomic</c> in Kurz's names (design record, section 6, the owner,
/// 2026-11-17, #519, comment 6946657505, answer 1, "Rust's set"): <c>AtomicBool</c>, <c>AtomicInt</c>, <c>AtomicLong</c> and
/// <c>AtomicPtr&lt;T&gt;</c>, each a view of a word through a pointer that <c>FromPtr</c> makes, as Rust's <c>from_ptr</c> does, with
/// Rust's members, each order an <c>MemoryOrder</c>, and <c>Fence</c>. A member and <c>Fence</c> stand in a <c>raw</c> block, as a read
/// through a pointer does (R10); <c>FromPtr</c> reads nothing, as <c>Ptr&lt;T&gt;(x)</c> (R14). An order written as a value of
/// <c>MemoryOrder</c> at the call that Rust's lint <c>invalid_atomic_ordering</c> refuses is <c>invalid-ordering</c>; any other order is
/// read when the call runs (the emitter's).
/// </summary>
public sealed partial class Checker
{
    /// <summary>The names of the atomic types, which no declaration of a file takes (<c>redeclared</c>, <see cref="IsBuiltInName"/>).</summary>
    private static bool IsAtomicBuiltIn(string name) => name is "AtomicBool" or "AtomicInt" or "AtomicLong" or "AtomicPtr" or "Fence";

    /// <summary>R19: a name of an atomic type where no variable refused so gives it, whose reads report nothing more (E1).</summary>
    private bool IsAtomicName(string name) =>
        name is "AtomicBool" or "AtomicInt" or "AtomicLong" or "AtomicPtr" && Lookup(name).Variable is null && !(frame.Reserved?.Contains(name) ?? false);

    /// <summary>R19: <c>Fence</c>, where no variable refused so gives the name.</summary>
    private bool IsFenceName(string name) => name == "Fence" && Lookup(name).Variable is null && !(frame.Reserved?.Contains(name) ?? false);

    private static Atomic AtomicOf(string name) => name switch
    {
        "AtomicBool" => Atomic.Bool,
        "AtomicInt" => Atomic.Int,
        "AtomicLong" => Atomic.Long,
        _ => Atomic.Ptr,
    };

    /// <summary>R19: an atomic type as a type, <c>AtomicPtr&lt;T&gt;</c> with the <c>T</c> a pointer points to (R10), or null where the name is no atomic type's.</summary>
    private KurzType? ResolveAtomic(NamedType n)
    {
        if (n.Name is not ("AtomicBool" or "AtomicInt" or "AtomicLong" or "AtomicPtr"))
        {
            return null;
        }

        var kind = AtomicOf(n.Name);
        if (kind != Atomic.Ptr)
        {
            if (n.TypeArguments.Count > 0)
            {
                Report(ErrorIds.Syntax, n.Line, $"`{n.Name}` takes no type arguments (R19, T17)");
                return Types.Error;
            }

            return new AtomicType(kind);
        }

        if (n.TypeArguments.Count != 1)
        {
            Report(n.TypeArguments.Count == 0 ? ErrorIds.CannotInfer : ErrorIds.Syntax, n.Line, "`AtomicPtr` takes one type argument, the type its word points to (R19, T17)");
            return Types.Error;
        }

        var pointee = Resolve(n.TypeArguments[0]);
        var pointer = PointerTo(pointee, n.Line, "`AtomicPtr<T>`");
        return pointer is PointerType p ? new AtomicType(Atomic.Ptr, p.Pointee) : Types.Error;
    }

    /// <summary>R19: a member of an atomic type read through its name as a value, <c>AtomicLong.FromPtr</c>, which is called; false for any other read.</summary>
    private bool AtomicMemberAsValue(MemberAccess m)
    {
        if (m.Target is not NameExpression n || !IsAtomicName(n.Name))
        {
            return false;
        }

        Report(ErrorIds.UnknownName, m.Line, m.Member == "FromPtr"
            ? $"`{n.Name}.FromPtr` is a function, which is called (R19, V10)"
            : $"`{n.Name}` has no member `{m.Member}`: its view is made by `FromPtr` (R19, V10)");
        return true;
    }

    /// <summary>R19: <c>AtomicLong.FromPtr(p)</c>, a call on the name of an atomic type; null where the target names none.</summary>
    private Typed? CheckAtomicFunction(MemberAccess m, CallExpression c)
    {
        if (m.Target is not NameExpression n || !IsAtomicName(n.Name))
        {
            return null;
        }

        if (n.Name == "AtomicPtr")
        {
            Report(ErrorIds.CannotInfer, n.Line, "`AtomicPtr` takes one type argument, the type its word points to: `AtomicPtr<T>.FromPtr(p)` (R19, T27)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        return CheckFromPtr(new AtomicType(AtomicOf(n.Name)), m.Member, m.Line, c);
    }

    /// <summary>
    /// R19: <c>FromPtr(ptr)</c> of an atomic type, the view of the word <c>ptr</c> points to, a <c>long*</c> for <c>AtomicLong</c> and a
    /// <c>T**</c> for <c>AtomicPtr&lt;T&gt;</c>, as Rust's <c>from_ptr</c> takes a <c>*mut i64</c>; it reads nothing, so no <c>raw</c>
    /// block holds it, as none holds <c>Ptr&lt;T&gt;(x)</c> (R14).
    /// </summary>
    private Typed CheckFromPtr(AtomicType atomic, string member, int line, CallExpression c)
    {
        if (member != "FromPtr")
        {
            Report(ErrorIds.UnknownName, line, $"`{atomic}` has no function `{member}`: its view is made by `FromPtr` (R19, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{atomic}.FromPtr`"))
        {
            return Typed.Error;
        }

        var candidate = new Candidate { Slots = [new Slot("ptr", new PointerType(atomic.Word), false, false, false)], Result = atomic };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.AtomicFromPtr, null, null, atomic, NoMap, picked.Binding, 1, atomic));
        }

        return typed;
    }

    /// <summary>The enum <c>MemoryOrder</c> every file sees (R19).</summary>
    private EnumType OrderingType => new(enums["MemoryOrder"]);

    /// <summary>
    /// R19: a member called on a view, with Rust's parameters, its orders an <c>MemoryOrder</c>: <c>Load</c>, <c>Store</c>, <c>Swap</c>,
    /// <c>CompareExchange</c> and <c>CompareExchangeWeak</c>, which give the value found, or the case <c>NotExchanged</c> with it where
    /// nothing was stored (O1, as Rust's <c>Result&lt;T, T&gt;</c>), on every atomic type, <c>FetchAdd</c> and <c>FetchSub</c> on
    /// <c>AtomicInt</c> and <c>AtomicLong</c>, and <c>FetchAnd</c>, <c>FetchOr</c> and <c>FetchXor</c> on all four, an
    /// <c>AtomicPtr&lt;T&gt;</c>'s taking a <c>long</c> of bits, as Rust's take a <c>usize</c>, where Rust has each for the type.
    /// </summary>
    private Typed CheckAtomicMethod(AtomicType atomic, MemberAccess m, CallExpression c)
    {
        var word = atomic.Word;
        var order = OrderingType;
        Slot Value(string name, KurzType type) => new(name, type, false, false, false);
        List<Slot>? slots = m.Member switch
        {
            "Load" => [Value("order", order)],
            "Store" or "Swap" => [Value("val", word), Value("order", order)],
            "CompareExchange" or "CompareExchangeWeak" => [Value("current", word), Value("new", word), Value("success", order), Value("failure", order)],
            "FetchAdd" or "FetchSub" when atomic.Kind is Atomic.Int or Atomic.Long => [Value("val", word), Value("order", order)],
            "FetchAnd" or "FetchOr" or "FetchXor" => [Value("val", atomic.Kind == Atomic.Ptr ? Types.Long : word), Value("order", order)],
            _ => null,
        };
        if (slots is null)
        {
            Report(ErrorIds.UnknownName, m.Line, $"`{atomic}` has no member `{m.Member}` (R19, V10)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        if (RefusesTypeArguments(c, $"`{m.Member}`"))
        {
            return Typed.Error;
        }

        RequireRaw(c.Line, $"`{m.Member}` of {Types.A(atomic)}", "R19");
        KurzType result = m.Member switch
        {
            "Store" => Types.Void,
            "CompareExchange" or "CompareExchangeWeak" => Types.Union([word, new DataType(dataTypes["NotExchanged"], [word])]),
            _ => word,
        };
        var candidate = new Candidate { Slots = slots, Result = result };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is null || typed.IsError)
        {
            return typed;
        }

        RefuseOrders(c, picked, m.Member);
        model.RecordCall(c, new CallInfo(CallKind.Atomic, null, null, atomic, NoMap, picked.Binding, slots.Count, result));
        return typed;
    }

    /// <summary>R19: <c>Fence(order)</c>, Rust's <c>fence</c>, in a <c>raw</c> block.</summary>
    private Typed CheckFence(CallExpression c)
    {
        if (RefusesTypeArguments(c, "`Fence`"))
        {
            return Typed.Error;
        }

        RequireRaw(c.Line, "`Fence`", "R19");
        var candidate = new Candidate { Slots = [new Slot("order", OrderingType, false, false, false)], Result = Types.Void };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is null || typed.IsError)
        {
            return typed;
        }

        RefuseOrders(c, picked, "Fence");
        model.RecordCall(c, new CallInfo(CallKind.Fence, null, null, null, NoMap, picked.Binding, 1, Types.Void));
        return typed;
    }

    /// <summary>
    /// R19: an order written as a value of <c>MemoryOrder</c> at the call, <c>MemoryOrder.Release</c>, that Rust's lint
    /// <c>invalid_atomic_ordering</c> refuses there is <c>invalid-ordering</c> at its argument: <c>Release</c> or <c>AcqRel</c> for a
    /// <c>Load</c>, <c>Acquire</c> or <c>AcqRel</c> for a <c>Store</c>, <c>Release</c> or <c>AcqRel</c> for the failure of a
    /// compare-exchange, and <c>Relaxed</c> for a <c>Fence</c>.
    /// </summary>
    private void RefuseOrders(CallExpression c, Candidate picked, string member)
    {
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var slot = picked.Slots[picked.Binding[i]].Name;
            var value = c.Arguments[i].Value;
            if (Unwrap(value) is not MemberAccess { Target: NameExpression { Name: "MemoryOrder" } } || model.TypeOf(value) is not { Type: EnumType e, Constant: EnumConstant written }
                || !ReferenceEquals(e.Symbol, enums["MemoryOrder"]))
            {
                continue;
            }

            var name = e.Symbol.Values[written.Index];
            if (AtomicOrders.Refused(member, slot, name) is { } why)
            {
                Report(ErrorIds.InvalidOrdering, c.Arguments[i].Line, $"{why}, as Rust's lint `invalid_atomic_ordering` refuses it, and `MemoryOrder.{name}` stands here (R19)");
            }
        }
    }
}

/// <summary>R19: the orders Rust refuses for an atomic operation, by the operation and the parameter, the one table of the checker and the emitter.</summary>
public static class AtomicOrders
{
    /// <summary>The text of the refusal of <paramref name="order"/> for the parameter <paramref name="slot"/> of <paramref name="member"/>, or null where Rust takes it.</summary>
    public static string? Refused(string member, string slot, string order) => (member, slot, order) switch
    {
        ("Load", "order", "Release" or "AcqRel") => "an atomic load cannot have `Release` or `AcqRel` order",
        ("Store", "order", "Acquire" or "AcqRel") => "an atomic store cannot have `Acquire` or `AcqRel` order",
        ("CompareExchange" or "CompareExchangeWeak", "failure", "Release" or "AcqRel") => "the failure order of a compare-exchange cannot be `Release` or `AcqRel`",
        ("Fence", "order", "Relaxed") => "a fence cannot have `Relaxed` order",
        _ => null,
    };
}
