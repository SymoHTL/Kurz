using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// Chapter 4: a <c>T?</c> is the union <c>T | null</c>, laid out as every union is, its <c>null</c> a case of no size, or of a list or a
/// block the address alone, its <c>null</c> an address no value has (<see cref="Layout.Niche"/>) (N1); a read the
/// checker found known not null is the value of the case <c>T</c> (N3); <c>x == null</c> tests the case (N3), <c>a ?? b</c> runs
/// <c>b</c> only where <c>a</c> is null (N4), and a chain of <c>?.</c> reads each member only where the value before it is not null (N5).
/// </summary>
public sealed partial class Emitter
{
    /// <summary>N3: a value read from a place of the type <paramref name="stored"/> as <paramref name="type"/>: where the place is nullable and the read is not, the value of its case.</summary>
    private string Narrowed(string value, KurzType stored, KurzType type) =>
        !Types.Same(stored, type) && stored is UnionType union && Types.NonNull(union) is { } inner ? Payload(value, union, inner) : value;

    /// <summary>N3: the place of a variable as the type the checker read at <paramref name="n"/>: where the variable is nullable and the read is not, the room of its case, which is the place itself where the union has a niche.</summary>
    private string NarrowedPlace(NameExpression n)
    {
        var place = Place(Model.VariableOf(n), n.Line);
        var stored = Sub(Model.VariableOf(n).Type);
        return !Types.Same(stored, TypeOf(n)) && stored is UnionType union && Types.NonNull(union) is not null && layout.Niche(union) is null
            ? fn.Value($"getelementptr {Ir(union)}, ptr {place}, i32 0, i32 1")
            : place;
    }

    /// <summary>A value made where it is read, for a place that holds it: counted once more, since what it was read from keeps its own count.</summary>
    private string Retained(string value, KurzType type)
    {
        if (Counted(type))
        {
            Retain(value, type);
        }

        return value;
    }

    /// <summary>N3: <c>x == null</c>, and <c>x != null</c> turned around: whether the value is of the case <c>null</c>.</summary>
    private string EmitNullTest(Expression other, bool isNull)
    {
        var union = (UnionType)TypeOf(other);
        var test = IsCase(Tag(EmitExpression(other), union), union, Types.Null);
        return isNull ? test : fn.Value($"xor i1 {test}, true");
    }

    /// <summary>
    /// N4: <c>a ?? b</c>, a value of its own that the caller makes a temporary of: the value of <c>a</c>'s case where it is not null,
    /// counted once more, since <c>a</c> keeps its count to the end of its statement, and <c>b</c> otherwise, which runs only there and
    /// lets go of what it makes there.
    /// </summary>
    private string EmitCoalesce(BinaryExpression b, KurzType type)
    {
        var leftType = (UnionType)TypeOf(b.Left);
        var left = EmitExpression(b.Left);
        var slot = fn.Alloca(Ir(type), "coalesce");
        var value = fn.Label("value");
        var other = fn.Label("other");
        var done = fn.Label("coalesced");
        fn.CondBranch(IsCase(Tag(left, leftType), leftType, Types.Null), other, value);
        fn.Block(value);
        var inner = Types.NonNull(leftType)!;
        var kept = Types.Same(type, leftType) ? Retained(left, leftType) : Convert(Retained(Payload(left, leftType, inner), inner), inner, type, b.Line);
        fn.Emit($"store {Ir(type)} {kept}, ptr {slot}");
        fn.Branch(done);
        fn.Block(other);
        InRegion(() => fn.Emit($"store {Ir(type)} {Convert(Owned(b.Right), TypeOf(b.Right), type, b.Right.Line)}, ptr {slot}"));
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(type)}, ptr {slot}");
    }

    /// <summary>
    /// N5: a member of a chain of <c>?.</c>, a value of its own that the caller makes a temporary of: <c>null</c> where the value it is
    /// read from is null, which stops the rest of the chain, and otherwise the member of that value's case, of its own type
    /// <paramref name="member"/>, made a value of the chain's nullable type.
    /// </summary>
    private string EmitChainMember(MemberAccess m, KurzType member, UnionType result)
    {
        var targetType = (UnionType)TypeOf(m.Target);
        var inner = Types.NonNull(targetType)!;
        var target = EmitExpression(m.Target);
        var slot = fn.Alloca(Ir(result), "chain");
        var read = fn.Label("read");
        var stop = fn.Label("stop");
        var done = fn.Label("chained");
        fn.CondBranch(IsCase(Tag(target, targetType), targetType, Types.Null), stop, read);
        fn.Block(stop);
        fn.Emit($"store {Ir(result)} {MakeUnion(result, Types.Null, "zeroinitializer")}, ptr {slot}");
        fn.Branch(done);
        fn.Block(read);
        InRegion(() =>
        {
            var payload = Payload(target, targetType, inner);
            var value = MemberOf(m, () => payload, inner, member);
            var owned = Counted(member) && !Take(value) ? Retained(value, member) : value;
            fn.Emit($"store {Ir(result)} {Convert(owned, member, result, m.Line)}, ptr {slot}");
        });
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(result)}, ptr {slot}");
    }

    /// <summary>
    /// N5: <c>xs?[i]</c>, a value of its own that the caller makes a temporary of: <c>null</c> where the list is null, which stops the
    /// rest of the chain, and otherwise its item at the index, counted once more, made a value of the chain's nullable type; the index
    /// runs only where the list is not null, as C#'s <c>?[]</c> runs it. The list is read without a count of its own past the index:
    /// an index that changes it, <c>names?[Pop(mut names)]</c>, is the checker's <c>changed-while-held</c> (M7).
    /// </summary>
    private string EmitConditionalIndex(IndexExpression i, UnionType result)
    {
        var targetType = (UnionType)TypeOf(i.Target);
        var list = (ListType)Types.NonNull(targetType)!;
        var target = EmitExpression(i.Target);
        var slot = fn.Alloca(Ir(result), "chain");
        var read = fn.Label("read");
        var stop = fn.Label("stop");
        var done = fn.Label("chained");
        fn.CondBranch(IsCase(Tag(target, targetType), targetType, Types.Null), stop, read);
        fn.Block(stop);
        fn.Emit($"store {Ir(result)} {MakeUnion(result, Types.Null, "zeroinitializer")}, ptr {slot}");
        fn.Branch(done);
        fn.Block(read);
        InRegion(() =>
        {
            var item = Retained(ListItem(Payload(target, targetType, list), i, list), list.Element);
            fn.Emit($"store {Ir(result)} {Convert(item, list.Element, result, i.Line)}, ptr {slot}");
        });
        fn.Branch(done);
        fn.Block(done);
        return fn.Value($"load {Ir(result)}, ptr {slot}");
    }
}
