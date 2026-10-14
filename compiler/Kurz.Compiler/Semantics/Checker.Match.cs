using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    private readonly record struct MatchResult(KurzType Type, bool Exhaustive, bool AllArmsEnd);

    /// <summary>
    /// C4, C9, O8: a <c>match</c> over a union or a single type: each arm names a case type, a
    /// literal or <c>else</c>; an arm an earlier one covers is <c>unreachable-arm</c>, a type that is
    /// no case is <c>type-mismatch</c>, and a <c>match</c> that lists neither every case nor <c>else</c>
    /// is <c>match-not-exhaustive</c>. As an expression its arms share one type.
    /// </summary>
    private MatchResult CheckMatch(Expression subject, IReadOnlyList<MatchArm> arms, bool isExpression, KurzType? expected, int line)
    {
        var typed = CheckExpression(subject, null, keepCases: true);
        var subjectType = typed.Type;
        var known = !typed.IsError;
        var cases = subjectType is UnionType u ? u.Cases.ToList() : known ? [subjectType] : [];
        var covered = new List<KurzType>();
        var literals = new List<(KurzType Type, Constant Value)>();
        var armTypes = new List<(KurzType Type, int Line)>();
        var hasElse = false;
        var allEnd = true;
        foreach (var arm in arms)
        {
            PushScope();
            switch (arm.Pattern)
            {
                case ElsePattern:
                    if (known && Covers(cases, covered))
                    {
                        Report(ErrorIds.UnreachableArm, arm.Line, "`else` after arms that cover every case (C4, C9)");
                    }

                    hasElse = true;
                    break;
                case TypePattern tp:
                    {
                        // a subject the checker could not type: the arms name cases of a type it does not know, so nothing is resolved
                        var type = known ? Resolve(tp.Type) : Types.Error;
                        if (known && !Types.IsError(type))
                        {
                            if (!cases.Any(c => Types.Assignable(type, c)))
                            {
                                Report(ErrorIds.TypeMismatch, arm.Line, $"`{type}` is no case of `{subjectType}` and derived from none (C4, T1)");
                            }
                            else if (covered.Any(c => Types.Assignable(type, c)))
                            {
                                Report(ErrorIds.UnreachableArm, arm.Line, $"an earlier arm covers `{type}` (C4)");
                            }
                            else
                            {
                                covered.Add(type);
                            }
                        }

                        if (tp.Binding is not null)
                        {
                            DeclareChecked(tp.Binding, type, false, VariableKind.Binding, arm.Line);
                        }

                        break;
                    }

                case ValuePattern vp:
                    {
                        var value = CheckExpression(vp.Value, Reach(subjectType));
                        if (!known || value.IsError)
                        {
                            break;
                        }

                        if (value.Constant is null)
                        {
                            Report(ErrorIds.Syntax, arm.Line, "an arm names a case type, a literal or `else` (C4, C9)");
                        }
                        else if (!cases.Any(c => Types.Assignable(value.Type, c)))
                        {
                            Report(ErrorIds.TypeMismatch, arm.Line, $"a `{value.Type}` is no value of `{subjectType}` (C9, T1)");
                        }
                        else if (covered.Any(c => Types.Assignable(value.Type, c)))
                        {
                            Report(ErrorIds.UnreachableArm, arm.Line, $"the arm of `{value.Type}` above takes every value (C4, C9)");
                        }
                        else if (literals.Any(l => Types.Same(l.Type, value.Type) && l.Value == value.Constant))
                        {
                            Report(ErrorIds.UnreachableArm, arm.Line, "an earlier arm names the same literal (C4)");
                        }
                        else
                        {
                            literals.Add((value.Type, value.Constant));
                        }

                        break;
                    }

                default:
                    throw new InvalidOperationException(arm.Pattern.GetType().Name);
            }

            if (isExpression)
            {
                switch (arm.Body)
                {
                    case ExpressionStatement es:
                        {
                            var t = CheckExpression(es.Expression, expected, keepCases: expected is UnionType);
                            if (Types.IsVoid(t.Type))
                            {
                                Report(ErrorIds.Syntax, es.Line, "a call with a `void` success used as a value (O10)");
                            }
                            else
                            {
                                armTypes.Add((t.Type, es.Line));
                            }

                            break;
                        }

                    case ThrowStatement { Value: not null } t:
                        CheckStatement(t);
                        break;
                    default:
                        Report(ErrorIds.Syntax, arm.Line, "an arm of a `match` used as a value is an expression (C9)");
                        break;
                }
            }
            else if (CheckStatement(arm.Body) == Flow.Completes)
            {
                allEnd = false;
            }

            PopScope();
        }

        var exhaustive = !known || hasElse || Covers(cases, covered) || (subjectType is PrimitiveType { Kind: Primitive.Bool } && literals.Count(l => l.Value is BoolConstant) == 2);
        if (!exhaustive)
        {
            Report(ErrorIds.MatchNotExhaustive, line, $"the `match` lists neither every case of `{subjectType}` nor `else` (O8, C9)");
        }

        if (!isExpression)
        {
            return new MatchResult(Types.Void, exhaustive, allEnd);
        }

        return new MatchResult(ArmsType(armTypes, expected, line), exhaustive, allEnd);
    }

    private static bool Covers(List<KurzType> cases, List<KurzType> covered) => cases.Count > 0 && cases.All(c => covered.Any(t => Types.Assignable(c, t)));

    /// <summary>C9: the type the arms of a <c>match</c> expression share, or the one written or expected around it.</summary>
    private KurzType ArmsType(List<(KurzType Type, int Line)> armTypes, KurzType? expected, int line)
    {
        if (armTypes.Count == 0 || armTypes.Any(a => Types.IsError(a.Type)))
        {
            return Types.Error;
        }

        if (expected is not null && !Types.IsError(expected))
        {
            foreach (var (type, armLine) in armTypes)
            {
                if (!Types.Assignable(type, expected))
                {
                    Report(Types.MismatchId(type, expected), armLine, $"the arm is a `{type}` where a `{expected}` is required (C9, T1)");
                }
            }

            return expected;
        }

        var first = armTypes[0].Type;
        if (armTypes.Any(a => !Types.Same(a.Type, first)))
        {
            Report(ErrorIds.TypeMismatch, line, "the arms of a `match` used as a value have one type (C9)");
            return Types.Error;
        }

        return first;
    }

    /// <summary>
    /// O5: <c>call else { Case => ... }</c> handles the listed cases at the call. An arm recovers with a
    /// value of the success type, returns another case from the calling function, or throws; with a
    /// <c>void</c> success (O10) an arm is a statement. A case not listed propagates (O2, O3, O9).
    /// </summary>
    private Typed CheckElse(ElseExpression e, bool isStatement, KurzType? expected)
    {
        if (e.Call is not CallExpression call)
        {
            Report(ErrorIds.Syntax, e.Line, "`else` follows a call (O5)");
            CheckExpression(e.Call, null);
            CheckArmsLoosely(e.Arms);
            return Typed.Error;
        }

        var result = CallResult(call, expected);
        if (result.IsError)
        {
            CheckArmsLoosely(e.Arms);
            return Typed.Error;
        }

        var cases = result.Type is UnionType u ? u.Cases.ToList() : [result.Type];
        var success = cases[0];
        var rest = cases.Skip(1).ToList();
        var handled = new List<KurzType>();
        var hasElse = false;
        var armWrong = false;
        foreach (var arm in e.Arms)
        {
            PushScope();
            switch (arm.Pattern)
            {
                case ElsePattern:
                    if (rest.Count > 0 && rest.All(c => handled.Any(h => Types.Assignable(c, h))))
                    {
                        Report(ErrorIds.UnreachableArm, arm.Line, "`else` after arms that handle every case (O5, C4)");
                    }

                    hasElse = true;
                    break;
                case TypePattern tp:
                    {
                        var type = Resolve(tp.Type);
                        if (!Types.IsError(type))
                        {
                            if (!rest.Any(c => Types.Assignable(type, c)))
                            {
                                Report(ErrorIds.TypeMismatch, arm.Line, $"`{type}` is no case the call can return beside its success (O5, T1)");
                                armWrong = true;
                            }
                            else if (handled.Any(h => Types.Assignable(type, h)))
                            {
                                Report(ErrorIds.UnreachableArm, arm.Line, $"an earlier arm handles `{type}` (O5, C4)");
                            }
                            else
                            {
                                handled.Add(type);
                            }
                        }

                        if (tp.Binding is not null)
                        {
                            DeclareChecked(tp.Binding, type, false, VariableKind.Binding, arm.Line);
                        }

                        break;
                    }

                case ValuePattern vp:
                    Report(ErrorIds.Syntax, arm.Line, "an arm of `else` names a case type (O5)");
                    CheckExpression(vp.Value, null);
                    break;
                default:
                    throw new InvalidOperationException(arm.Pattern.GetType().Name);
            }

            CheckElseArmBody(arm.Body, success);
            PopScope();
        }

        if (!hasElse && !armWrong)
        {
            Propagate(rest.Where(c => !handled.Any(h => Types.Assignable(c, h))), e.Line); // an arm that names no case was reported; what it meant to handle is not reported again (E1)
        }

        if (!isStatement && Types.IsVoid(success))
        {
            Report(ErrorIds.Syntax, e.Line, "a call with a `void` success used as a value (O10)");
            return Typed.Error;
        }

        return new Typed(success);
    }

    private void CheckArmsLoosely(IReadOnlyList<MatchArm> arms)
    {
        foreach (var arm in arms)
        {
            PushScope();
            if (arm.Pattern is TypePattern { Binding: not null } tp)
            {
                Declare(tp.Binding, Types.Error, false, VariableKind.Binding, arm.Line);
            }

            var wasInElseArm = inElseArm;
            inElseArm = true;
            CheckStatement(arm.Body);
            inElseArm = wasInElseArm;
            PopScope();
        }
    }

    /// <summary>O5: what an arm does; in a block, its last statement.</summary>
    private void CheckElseArmBody(Statement body, KurzType success)
    {
        var wasInElseArm = inElseArm;
        inElseArm = true;
        switch (body)
        {
            case ExpressionStatement es when !Types.IsVoid(success):
                {
                    var t = CheckExpression(es.Expression, success, keepCases: success is UnionType);
                    if (Types.IsVoid(t.Type))
                    {
                        Report(ErrorIds.Syntax, es.Line, "a call with a `void` success used as a value (O10)");
                    }
                    else if (!t.IsError && !Types.IsError(success) && !Types.Assignable(t.Type, success))
                    {
                        Report(Types.MismatchId(t.Type, success), es.Line, $"the arm recovers with a `{t.Type}` where the success is a `{success}` (O5, T1)");
                    }

                    break;
                }

            case Block b:
                PushScope();
                for (var i = 0; i < b.Statements.Count; i++)
                {
                    if (i == b.Statements.Count - 1)
                    {
                        CheckElseArmBody(b.Statements[i], success);
                    }
                    else
                    {
                        inElseArm = false;
                        CheckStatement(b.Statements[i]);
                        inElseArm = true;
                    }
                }

                PopScope();
                break;
            default:
                CheckStatement(body);
                break;
        }

        inElseArm = wasInElseArm;
    }
}
