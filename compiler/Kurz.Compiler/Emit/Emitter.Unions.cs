using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // the case value that reached the arm of `else` being emitted, which a bare `throw` there throws (O7)
    private (string Value, KurzType Type)? elseCase;

    // ---- unions (D9) -----------------------------------------------------------------------------

    /// <summary>The tag of a union value: the number of its case's type in the program.</summary>
    private string Tag(string union, UnionType type) => fn.Value($"extractvalue {Ir(type)} {union}, 0");

    private string IsCase(string tag, UnionType type, KurzType c) =>
        fn.Value($"icmp eq i{Number(layout.TagBits(type))} {tag}, {Number(layout.Number(c))}");

    /// <summary>The value of a union's case, read from its room as the case's type.</summary>
    private string Payload(string union, UnionType type, KurzType c)
    {
        var slot = fn.Alloca(Ir(type), "union");
        fn.Emit($"store {Ir(type)} {union}, ptr {slot}");
        var room = fn.Value($"getelementptr {Ir(type)}, ptr {slot}, i32 0, i32 1");
        return fn.Value($"load {Ir(c)}, ptr {room}");
    }

    /// <summary>A union value of the case <paramref name="c"/>: the number of the case's type as its tag, and the value in its room.</summary>
    private string MakeUnion(UnionType type, KurzType c, string? value)
    {
        var slot = fn.Alloca(Ir(type), "union");
        fn.Emit($"store i{Number(layout.TagBits(type))} {Number(layout.Number(c))}, ptr {slot}");
        if (c is not VoidType)
        {
            var room = fn.Value($"getelementptr {Ir(type)}, ptr {slot}, i32 0, i32 1");
            fn.Emit($"store {Ir(c)} {value ?? throw new InvalidOperationException($"the case `{c}` without a value")}, ptr {room}");
        }

        return fn.Value($"load {Ir(type)}, ptr {slot}");
    }

    /// <summary>A branch on the tag to the code for each of <paramref name="cases"/>, which ends its block; another tag is none of them.</summary>
    private void ByCase(string tag, UnionType type, IEnumerable<KurzType> cases, Action<KurzType> each)
    {
        var list = cases.ToList();
        var labels = list.Select(_ => fn.Label("case")).ToList();
        var other = fn.Label("other");
        var bits = Number(layout.TagBits(type));
        fn.Terminate($"switch i{bits} {tag}, label %{other} [ {string.Join(" ", list.Select((c, i) => $"i{bits} {Number(layout.Number(c))}, label %{labels[i]}"))} ]");
        for (var i = 0; i < list.Count; i++)
        {
            fn.Block(labels[i]);
            each(list[i]);
        }

        fn.Block(other);
        fn.Unreachable();
    }

    /// <summary>
    /// D9 with O1: a union where a union is required, case by case: the first case to the first case, and each other case to the one
    /// case after the first it is, widens into or derives from, with its value converted and that case's number as its tag.
    /// </summary>
    private string ConvertUnion(string value, UnionType from, UnionType to, int line)
    {
        var result = fn.Alloca(Ir(to), "union");
        var done = fn.Label("converted");
        var rest = to.Cases.Skip(1).ToList();
        ByCase(Tag(value, from), from, from.Cases, c =>
        {
            var target = Types.Same(c, from.Cases[0]) ? to.Cases[0] : Types.CasesFor(c, rest).Single();
            var converted = c is VoidType ? null : Convert(Payload(value, from, c), c, target, line);
            fn.Emit($"store {Ir(to)} {MakeUnion(to, target, converted)}, ptr {result}");
            fn.Branch(done);
        });
        fn.Block(done);
        return fn.Value($"load {Ir(to)}, ptr {result}");
    }

    /// <summary>C4: whether an arm of <paramref name="armType"/> takes every value of the case: the case is the type or derived from it (D6).</summary>
    private static bool TakesCase(KurzType c, KurzType armType) =>
        Types.Same(c, armType) || (c is DataType && armType is DataType && Types.Assignable(c, armType));

    // ---- `match` (C4, C9) ------------------------------------------------------------------------

    /// <summary>
    /// C4, C9: the arms tested in order, the first that takes the value runs; as an expression each arm's value is the match's,
    /// converted to its type. The arms cover every case (O8), which the checker holds.
    /// </summary>
    private string? EmitMatch(Expression subject, IReadOnlyList<MatchArm> arms, KurzType? result)
    {
        var type = TypeOf(subject);
        var value = EmitExpression(subject);
        var union = type as UnionType;
        var tag = union is null ? null : Tag(value, union);
        var slot = result is null ? null : fn.Alloca(Ir(result), "match");
        var done = fn.Label("matched");
        foreach (var arm in arms)
        {
            var body = fn.Label("arm");
            var next = fn.Label("next");
            var bound = Test(arm.Pattern, value, type, tag, union?.Cases ?? [type], body, next);
            fn.Block(body);
            InScope(() =>
            {
                if (arm.Pattern is TypePattern { Binding: not null } tp)
                {
                    // the name holds the value as a variable does, so that the arm can change the subject's variable (M3)
                    var bindingType = Sub(Model.PatternTypeOf(tp));
                    if (Counted(bindingType))
                    {
                        Retain(fn.Value($"load {Ir(bindingType)}, ptr {bound}"), bindingType);
                        Declare(bound!, bindingType);
                    }
                }

                if (slot is null)
                {
                    EmitStatement(arm.Body);
                }
                else
                {
                    InRegion(() => ArmValue(arm.Body, result!, slot));
                }
            });

            if (!fn.Terminated)
            {
                fn.Branch(done);
            }

            fn.Block(next);
        }

        fn.Unreachable();
        fn.Block(done);
        return slot is null ? null : fn.Value($"load {Ir(result!)}, ptr {slot}");
    }

    /// <summary>C9: an arm of a <c>match</c> used as a value: its value converted to the match's type, which the match's value holds, or its <c>throw</c>.</summary>
    private void ArmValue(Statement body, KurzType result, string slot)
    {
        switch (body)
        {
            case ExpressionStatement es:
                fn.Emit($"store {Ir(result)} {Convert(Owned(es.Expression), TypeOf(es.Expression), result, es.Line)}, ptr {slot}");
                break;
            default:
                EmitStatement(body);
                break;
        }
    }

    /// <summary>
    /// Branches to <paramref name="body"/> when the arm's pattern takes the value and to <paramref name="next"/> when it does not.
    /// <c>else</c> takes every value; a literal (C9) a value equal to it, of its case in a union; a type every value of a case
    /// that is the type or derived from it, and of a case it derives from the values whose block holds it or a type derived from
    /// it (C4, D6). A type's arm binds the value, as the arm's type, to its name; the slot it is in is returned, null for the others.
    /// </summary>
    private string? Test(Pattern pattern, string value, KurzType type, string? tag, IReadOnlyList<KurzType> cases, string body, string next)
    {
        switch (pattern)
        {
            case ElsePattern:
                fn.Branch(body);
                return null;
            case ValuePattern vp:
                {
                    var literalType = TypeOf(vp.Value);
                    var literal = EmitExpression(vp.Value);
                    if (type is not UnionType union)
                    {
                        fn.CondBranch(Equal(value, Convert(literal, literalType, type, vp.Line), type, vp.Line), body, next);
                        return null;
                    }

                    var c = cases.First(k => Types.Same(k, literalType));
                    var check = fn.Label("literal");
                    fn.CondBranch(IsCase(tag!, union, c), check, next);
                    fn.Block(check);
                    fn.CondBranch(Equal(Payload(value, union, c), literal, c, vp.Line), body, next);
                    return null;
                }

            case TypePattern tp:
                {
                    var armType = Sub(Model.PatternTypeOf(tp));
                    var slot = fn.Alloca(Ir(armType), tp.Binding ?? "case");
                    if (tp.Binding is not null)
                    {
                        slots[Model.DeclarationOf(tp)] = slot;
                    }

                    if (type is not UnionType union)
                    {
                        if (Types.Assignable(type, armType))
                        {
                            fn.Emit($"store {Ir(armType)} {Convert(value, type, armType, tp.Line)}, ptr {slot}");
                            fn.Branch(body);
                            return slot;
                        }

                        var take = fn.Label("derived");
                        fn.CondBranch(HoldsDerived(value, ((DataType)armType).Symbol), take, next); // the arm's type derives from the value's (D6)
                        fn.Block(take);
                        fn.Emit($"store ptr {value}, ptr {slot}");
                        fn.Branch(body);
                        return slot;
                    }

                    foreach (var c in cases)
                    {
                        var takes = TakesCase(c, armType);
                        if (!takes && !(armType is DataType && c is DataType && Types.Assignable(armType, c)))
                        {
                            continue;
                        }

                        var candidate = fn.Label("candidate");
                        var other = fn.Label("try");
                        fn.CondBranch(IsCase(tag!, union, c), candidate, other);
                        fn.Block(candidate);
                        var payload = Payload(value, union, c);
                        if (takes)
                        {
                            fn.Emit($"store {Ir(armType)} {Convert(payload, c, armType, tp.Line)}, ptr {slot}");
                        }
                        else
                        {
                            var take = fn.Label("derived");
                            fn.CondBranch(HoldsDerived(payload, ((DataType)armType).Symbol), take, other);
                            fn.Block(take);
                            fn.Emit($"store ptr {payload}, ptr {slot}");
                        }

                        fn.Branch(body);
                        fn.Block(other);
                    }

                    fn.Branch(next);
                    return slot;
                }

            default:
                throw new InvalidOperationException(pattern.GetType().Name);
        }
    }

    // ---- outcomes (O2 to O10) --------------------------------------------------------------------

    /// <summary>O2, O3: the success of a call's result, its value; every other case leaves the function being emitted.</summary>
    private string? Propagate(string value, UnionType union, int line)
    {
        var success = fn.Label("success");
        var leave = fn.Label("leave");
        fn.CondBranch(IsCase(Tag(value, union), union, union.Cases[0]), success, leave);
        fn.Block(leave);
        Leave(value, union, union.Cases.Skip(1).ToList(), line);
        fn.Block(success);
        return union.Cases[0] is VoidType ? null : Payload(value, union, union.Cases[0]);
    }

    /// <summary>O3: returns from the function being emitted with the union's value, whose case is one of <paramref name="cases"/>, as the one case after the first of the function's result it is, widens into or derives from.</summary>
    private void Leave(string value, UnionType union, IReadOnlyList<KurzType> cases, int line)
    {
        var result = returnType as UnionType ?? throw new InvalidOperationException($"a case leaves a function whose result is no union at line {line}: the checker reports it (O3)");
        var rest = result.Cases.Skip(1).ToList();
        Leaving(0, 0); // the case's value goes to the result, which holds it
        ByCase(Tag(value, union), union, cases, c =>
        {
            var target = Types.CasesFor(c, rest).Single();
            fn.Return($"{Ir(result)} {MakeUnion(result, target, Convert(Payload(value, union, c), c, target, line))}");
        });
    }

    /// <summary>
    /// O5: <c>call else { Case => ... }</c>. The success is the value; each arm takes the cases its pattern takes and recovers with a
    /// value of the success type, returns or throws; a case no arm takes leaves the function (O2, O3).
    /// </summary>
    private string? EmitElse(ElseExpression e, bool asValue)
    {
        var call = (CallExpression)Unwrap(e.Call);
        var union = Sub(Model.CallOf(call).Result) as UnionType ?? throw new InvalidOperationException($"`else` after a call whose result is no union at line {e.Line}: the checker reports it (O5)");
        var value = EmitCall(call) ?? throw new InvalidOperationException($"the call before `else` at line {e.Line} has no value");
        var success = union.Cases[0];
        var rest = union.Cases.Skip(1).ToList();
        var tag = Tag(value, union);
        var slot = asValue && success is not VoidType ? fn.Alloca(Ir(success), "recovered") : null;
        var done = fn.Label("recovered");
        var ok = fn.Label("success");
        var cases = fn.Label("cases");
        fn.CondBranch(IsCase(tag, union, success), ok, cases);
        fn.Block(ok);
        if (slot is not null)
        {
            fn.Emit($"store {Ir(success)} {Payload(value, union, success)}, ptr {slot}");
        }
        else if (success is not VoidType && Counted(success))
        {
            Release(Payload(value, union, success), success); // a success nothing holds
        }

        fn.Branch(done);
        fn.Block(cases);
        var taken = new List<KurzType>();
        var hasElse = false;
        foreach (var arm in e.Arms)
        {
            var body = fn.Label("arm");
            var next = fn.Label("next");
            var caseSlot = Test(arm.Pattern, value, union, tag, rest, body, next);
            fn.Block(body);
            var outer = elseCase;
            if (arm.Pattern is TypePattern tp)
            {
                var armType = Sub(Model.PatternTypeOf(tp));
                taken.Add(armType);
                elseCase = (fn.Value($"load {Ir(armType)}, ptr {caseSlot}"), armType);
            }
            else if (arm.Pattern is ElsePattern)
            {
                hasElse = true;
                elseCase = (value, union);
            }
            else
            {
                throw new InvalidOperationException($"an arm of `else` that names no case type at line {arm.Line}: the checker reports it (O5)");
            }

            InScope(() => InRegion(() => ElseArm(arm.Body, success, slot)));
            elseCase = outer;
            if (!fn.Terminated)
            {
                fn.Branch(done);
            }

            fn.Block(next);
        }

        var left = hasElse ? [] : rest.Where(c => !taken.Any(t => TakesCase(c, t))).ToList();
        if (left.Count == 0)
        {
            fn.Unreachable();
        }
        else
        {
            Leave(value, union, left, e.Line);
        }

        fn.Block(done);
        return slot is null ? null : fn.Value($"load {Ir(success)}, ptr {slot}");
    }

    /// <summary>O5: what an arm of <c>else</c> does; in a block, its last statement: the value it recovers with, or what it does.</summary>
    private void ElseArm(Statement body, KurzType success, string? slot)
    {
        switch (body)
        {
            case ExpressionStatement es when success is not VoidType:
                {
                    var value = Convert(Owned(es.Expression), TypeOf(es.Expression), success, es.Line);
                    if (slot is not null)
                    {
                        fn.Emit($"store {Ir(success)} {value}, ptr {slot}");
                    }
                    else
                    {
                        Temp(value, success); // a recovered value nothing holds: the arm's end lets go of it
                    }

                    break;
                }

            case Block b when b.Statements.Count > 0:
                for (var i = 0; i < b.Statements.Count - 1 && !fn.Terminated; i++)
                {
                    EmitStatement(b.Statements[i]);
                }

                if (!fn.Terminated)
                {
                    ElseArm(b.Statements[^1], success, slot);
                }

                break;
            default:
                EmitStatement(body);
                break;
        }
    }
}
