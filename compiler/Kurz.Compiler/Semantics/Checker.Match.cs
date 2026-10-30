using System.Collections.Immutable;
using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

public sealed partial class Checker
{
    private readonly record struct MatchResult(KurzType Type, bool Exhaustive, bool AllArmsEnd);

    /// <summary>
    /// C4, C9, O8: a <c>match</c> over a union or a single type: each arm names a case type, a
    /// literal or <c>else</c>; an arm an earlier one covers is <c>unreachable-arm</c>, a type that is
    /// no case is <c>type-mismatch</c>, and a <c>match</c> that lists neither every case nor <c>else</c>
    /// is <c>match-not-exhaustive</c>. As an expression its arms share one type. An arm after
    /// <c>else</c> never reaches the checker: C9 puts <c>else</c> last, and the parser reports one
    /// as <c>syntax</c> (the test <c>An_arm_after_else_is_syntax_at_its_line</c> of the parser's suite).
    /// </summary>
    private MatchResult CheckMatch(Expression subject, IReadOnlyList<MatchArm> arms, bool isExpression, KurzType? expected, int line)
    {
        // N3: a `null` arm tests the subject's case, as `x == null` does, so a variable known not null is read as its nullable type
        var typed = arms.Any(a => a.Pattern is ValuePattern { Value: var v } && Unwrap(v) is NullLiteral) ? CheckNullable(subject, keepCases: true) : CheckExpression(subject, null, keepCases: true);
        var subjectType = typed.Type;
        var known = !typed.IsError;
        if (Types.IsVoid(subjectType))
        {
            Report(ErrorIds.Syntax, subject.Line, "a call with a `void` success used as a value (O10)");
            known = false; // the arms name cases of nothing: as for a subject the checker could not type (E1)
        }

        var cases = subjectType is UnionType u ? u.Cases.ToList() : known ? [subjectType] : [];
        var covered = new List<KurzType>();
        var literals = new List<(KurzType Type, Constant Value)>();
        var armTypes = new List<(KurzType Type, int Line)>();
        var hasElse = false;
        var armWrong = false;
        var armReported = false;
        var allEnd = true;
        var start = frame.Known; // N3: each arm starts with what held after the subject, and after the match holds what every arm that completes keeps
        var reaching = new List<ImmutableHashSet<NullPath>>();
        var subjectPath = Types.NonNull(subjectType) is not null ? PathOf(subject) : null; // N3: a nullable variable or path of fields the arms test
        var nullNamed = false;
        foreach (var arm in arms)
        {
            PushScope();
            frame.Known = start;
            KurzType? patternType = null;
            switch (arm.Pattern)
            {
                case ElsePattern:
                    if (known && Covers(cases, covered, literals))
                    {
                        Report(ErrorIds.UnreachableArm, arm.Line, covered.Count == 0 && literals.Count == 0 ? $"`else` where `{subjectType}` has no values to reach it: a `match` with no arms covers it (C4, D12)" : "`else` after arms that cover every case (C4, C9)");
                    }

                    hasElse = true;
                    break;
                case TypePattern tp:
                    {
                        // a subject the checker could not type: the arms name cases of a type it does not know, so nothing is resolved
                        var type = known ? Resolve(tp.Type) : Types.Error;
                        patternType = type;
                        model.RecordPattern(tp, type);
                        if (known && !Types.IsError(type))
                        {
                            if (!cases.Any(c => IsCaseOf(type, c)))
                            {
                                Report(ErrorIds.TypeMismatch, arm.Line, $"`{type}` is no case of `{subjectType}` and derived from none (C4, T1)");
                                armWrong = true;
                            }
                            else if (covered.Any(c => IsCaseOf(type, c)))
                            {
                                Report(ErrorIds.UnreachableArm, arm.Line, $"an earlier arm covers `{type}` (C4)");
                            }
                            else if (type is EnumType e && EveryValueNamed(e, literals))
                            {
                                // an enum without values: no value reaches the arm, as Rust 1.82 warns that such an arm matches no values
                                Report(ErrorIds.UnreachableArm, arm.Line, e.Symbol.Values.Count == 0 ? $"`{type}` has no values, so no arm of it is reached{(Types.Same(subjectType, e) ? ": a `match` with no arms covers it" : "")} (C4, D12)" : $"the arms above name every value of `{type}` (C4, D12)");
                            }
                            else
                            {
                                covered.Add(type);
                            }
                        }

                        if (tp.Binding is not null)
                        {
                            DeclareChecked(tp.Binding, type, false, VariableKind.Binding, arm.Line, tp);
                        }

                        break;
                    }

                case ValuePattern vp:
                    {
                        if (!known)
                        {
                            CheckLoosely(vp.Value); // the arms name values of a type the checker does not know: nothing is judged
                            break;
                        }

                        // C9: an arm names a literal, D12 a value of an enum; a folded expression or a name is neither, text no rule gives a meaning (E4)
                        if (!IsLiteralPattern(vp.Value) && !IsEnumValuePattern(vp.Value))
                        {
                            Report(ErrorIds.Syntax, arm.Line, "an arm names a case type, a literal, a value of an enum or `else` (C4, C9, D12)");
                            CheckLoosely(vp.Value);
                            break;
                        }

                        // N1 (the owner, 2026-10-30, "Allow, as C#"): a `null` arm over a subject whose type has no `null` is an arm no value
                        // reaches, with no diagnostic, as C# compiles `case null` on a `string`; a second one is `unreachable-arm`, as on a `T?`
                        if (Unwrap(vp.Value) is NullLiteral && !cases.Any(c => c is NullType or PointerType or TypeParameterType))
                        {
                            if (nullNamed)
                            {
                                Report(ErrorIds.UnreachableArm, arm.Line, "an earlier arm names the same literal (C4)");
                            }

                            break;
                        }

                        var value = CheckExpression(vp.Value, subjectType); // each literal reads its reach from the type, a union's by its kind (T8 with D9)
                        if (value.IsError || value.Constant is null)
                        {
                            break;
                        }

                        if (!cases.Any(c => IsCaseOf(value.Type, c)))
                        {
                            Report(ErrorIds.TypeMismatch, arm.Line, $"a `{value.Type}` is no value of `{subjectType}` (C9, T1)");
                            armWrong = true;
                        }
                        else if (covered.Any(c => IsCaseOf(value.Type, c)))
                        {
                            Report(ErrorIds.UnreachableArm, arm.Line, $"the arm of `{value.Type}` above takes every value (C4, C9)");
                        }
                        else if (literals.Any(l => Types.Same(l.Type, value.Type) && l.Value == value.Constant))
                        {
                            Report(ErrorIds.UnreachableArm, arm.Line, value.Type is EnumType ? "an earlier arm names the same value (C4, D12)" : "an earlier arm names the same literal (C4)");
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

            // N3, as C# reads a pattern: in an arm no `null` reaches, a type's, a literal's or an `else` after the `null` arm, the subject's
            // path is known not null
            var namesNull = arm.Pattern is ValuePattern { Value: var named } && Unwrap(named) is NullLiteral;
            var notNull = arm.Pattern switch
            {
                ElsePattern => nullNamed,
                TypePattern => patternType is { } t && !Types.IsError(t) && t is not NullType && Types.NonNull(t) is null,
                _ => !namesNull,
            };
            if (subjectPath is { } path && notNull)
            {
                frame.Known = frame.Known.Add(path);
            }

            nullNamed |= namesNull;
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
                                armReported = true;
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
                        // C9 gives the arms of a `match` used as a value a value each: a statement or a block there is text no rule gives a
                        // meaning, E4's `syntax` (the reading in compiler/README.md); what it holds is still checked
                        Report(ErrorIds.Syntax, arm.Line, "an arm of a `match` used as a value is an expression (C9)");
                        armReported = true;
                        CheckStatement(arm.Body);
                        break;
                }

                if (arm.Body is not ThrowStatement)
                {
                    reaching.Add(frame.Known);
                }
            }
            else if (CheckStatement(arm.Body) == Flow.Completes)
            {
                allEnd = false;
                reaching.Add(frame.Known);
            }

            PopScope();
        }

        frame.Known = Join(reaching, start);

        var exhaustive = !known || hasElse || armWrong || Covers(cases, covered, literals); // an arm that named no case was reported; the match is not reported again for it (E1)
        if (!exhaustive)
        {
            Report(ErrorIds.MatchNotExhaustive, line, $"the `match` lists neither every case of `{subjectType}` nor `else` (O8, C9)");
        }

        if (!isExpression)
        {
            return new MatchResult(Types.Void, exhaustive, allEnd);
        }

        if (armTypes.Count == 0 && !armWrong && !armReported && known)
        {
            Report(ErrorIds.Syntax, line, "a `match` used as a value has an arm that is a value (C9)"); // every arm throws: text no rule gives a meaning (E4); an arm reported above is the one error (E1)
            return new MatchResult(Types.Error, exhaustive, allEnd);
        }

        return new MatchResult(ArmsType(armTypes, expected), exhaustive, allEnd);
    }

    /// <summary>C4: whether an arm's type names a case: the case itself, or a data type derived from it (D6); no widening (T8) makes a case.</summary>
    private static bool IsCaseOf(KurzType type, KurzType c) => Types.Same(type, c) || (type is DataType && c is DataType && Types.Assignable(type, c));

    /// <summary>C9: what an arm may name beside a type and <c>else</c>: a literal, with its sign where it is a number (L17), and <c>null</c>, the case of a nullable value (N1, C9; section 4, the owner, 2026-10-30, "Allow, as C#").</summary>
    private static bool IsLiteralPattern(Expression e) => Unwrap(e) switch
    {
        IntegerLiteral or RealLiteral or UnitLiteral or CharLiteral or BoolLiteral or NullLiteral => true,
        StringLiteral s => s.IsPlain,
        UnaryExpression { Operator: TokenKind.Minus, Operand: IntegerLiteral or RealLiteral or UnitLiteral } => true,
        _ => false,
    };

    /// <summary>D12: an arm's value of an enum, <c>Plan.Free</c>, the type's name and one of its members.</summary>
    private bool IsEnumValuePattern(Expression e) =>
        Unwrap(e) is MemberAccess { Conditional: false, Target: NameExpression n } && enums.ContainsKey(n.Name) && Lookup(n.Name).Variable is null;

    /// <summary>D12: whether the arms name every value of the enum, inside a union too (section 4, the owner, 2026-10-29).</summary>
    private static bool EveryValueNamed(EnumType e, List<(KurzType Type, Constant Value)> literals) =>
        Enumerable.Range(0, e.Symbol.Values.Count).All(i => literals.Any(l => Types.Same(l.Type, e) && l.Value is EnumConstant { Index: var named } && named == i));

    /// <summary>O8, C9: every case is covered by an arm of its type or a base's, for <c>bool</c> by the arms of both its literals, for an enum by the arms of all its values (D12), and for <c>null</c> by its arm (N1).</summary>
    private static bool Covers(List<KurzType> cases, List<KurzType> covered, List<(KurzType Type, Constant Value)> literals) =>
        cases.Count > 0 && cases.All(c => covered.Any(t => IsCaseOf(c, t)) || (c is PrimitiveType { Kind: Primitive.Bool } && literals.Count(l => l.Value is BoolConstant) == 2) || (c is EnumType e && EveryValueNamed(e, literals))
            || (c is NullType && literals.Any(l => l.Value is NullConstant)));

    /// <summary>C9: the type the arms of a <c>match</c> expression share, or the one written or expected around it; an arm of another type is reported at that arm (E1).</summary>
    private KurzType ArmsType(List<(KurzType Type, int Line)> armTypes, KurzType? expected)
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
        foreach (var (type, armLine) in armTypes)
        {
            if (!Types.Same(type, first))
            {
                Report(ErrorIds.TypeMismatch, armLine, $"the arm is a `{type}` where the arms above are `{first}`: the arms of a `match` used as a value have one type (C9)");
                return Types.Error;
            }
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
            // O5 gives the postfix `else` to a call, and the parser reads it after a call only, so that after anything else it is the parser's
            // `syntax` (E4; the reading in compiler/README.md); a tree built another way gets the same id here
            Report(ErrorIds.Syntax, e.Line, "`else` follows a call (O5)");
            CheckExpression(e.Call, null);
            CheckArmsLoosely(e.Arms);
            return Typed.Error;
        }

        var result = CallResult(call, expected);
        model.RecordType(call, result); // the call's whole result: the arms take its cases after the success (O5)
        if (result.IsError)
        {
            CheckArmsLoosely(e.Arms);
            return Typed.Error;
        }

        var cases = result.Type is UnionType u && Types.NonNull(u) is null ? u.Cases.ToList() : [result.Type]; // N8: a `T?` is no outcome list
        var success = cases[0];
        var rest = cases.Skip(1).ToList();
        var handled = new List<KurzType>();
        var hasElse = false;
        var armWrong = false;
        var start = frame.Known; // N3: the success goes on with what held after the call, an arm with that too, and after both holds what each keeps
        var reaching = new List<ImmutableHashSet<NullPath>> { start };
        foreach (var arm in e.Arms)
        {
            PushScope();
            frame.Known = start;
            switch (arm.Pattern)
            {
                case ElsePattern:
                    if (rest.All(c => handled.Any(h => IsCaseOf(c, h))))
                    {
                        Report(ErrorIds.UnreachableArm, arm.Line, "`else` after arms that handle every case (O5, C4)"); // a call with no case beside its success among them
                    }

                    hasElse = true;
                    break;
                case TypePattern tp:
                    {
                        var type = Resolve(tp.Type);
                        model.RecordPattern(tp, type);
                        if (Types.IsError(type))
                        {
                            armWrong = true; // the type carries its own diagnostic, and which case the arm meant is unknown (E1)
                        }
                        else
                        {
                            if (!rest.Any(c => IsCaseOf(type, c)))
                            {
                                Report(ErrorIds.TypeMismatch, arm.Line, $"`{type}` is no case the call can return beside its success (O5, T1)");
                                armWrong = true;
                            }
                            else if (handled.Any(h => IsCaseOf(type, h)))
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
                            DeclareChecked(tp.Binding, type, false, VariableKind.Binding, arm.Line, tp);
                        }

                        break;
                    }

                case ValuePattern vp:
                    // O5 gives an arm of `else` a case type: a value there is text no rule gives a meaning, E4's `syntax` (the reading in compiler/README.md)
                    Report(ErrorIds.Syntax, arm.Line, "an arm of `else` names a case type (O5)");
                    CheckExpression(vp.Value, null);
                    armWrong = true;
                    break;
                default:
                    throw new InvalidOperationException(arm.Pattern.GetType().Name);
            }

            CheckElseArmBody(arm.Body, success);
            reaching.Add(frame.Known);
            PopScope();
        }

        frame.Known = Join(reaching, start);
        if (!hasElse && !armWrong)
        {
            // E1: an arm that names no case was reported at the arm; which case it meant is unknown, so no case is reported as unlisted
            // beside it, the one defect carrying the one error
            Propagate(rest.Where(c => !handled.Any(h => IsCaseOf(c, h))), e.Line);
        }

        if (!isStatement && Types.IsVoid(success))
        {
            Report(ErrorIds.Syntax, e.Line, "a call with a `void` success used as a value (O10)");
            return Typed.Error;
        }

        return new Typed(success);
    }

    /// <summary>The arms of an <c>else</c> after a call that did not type: what does not depend on the call's cases is still checked, with the success unknown (E1).</summary>
    private void CheckArmsLoosely(IReadOnlyList<MatchArm> arms)
    {
        foreach (var arm in arms)
        {
            PushScope();
            switch (arm.Pattern)
            {
                case TypePattern tp:
                    {
                        var type = Resolve(tp.Type);
                        if (tp.Binding is not null)
                        {
                            DeclareChecked(tp.Binding, type, false, VariableKind.Binding, arm.Line, tp);
                        }

                        break;
                    }

                case ValuePattern vp:
                    Report(ErrorIds.Syntax, arm.Line, "an arm of `else` names a case type (O5)");
                    CheckExpression(vp.Value, null);
                    break;
                default:
                    break;
            }

            CheckElseArmBody(arm.Body, Types.Error);
            PopScope();
        }
    }

    /// <summary>
    /// O5: what an arm does; in a block, its last statement. With a success that is not <c>void</c> the
    /// arm recovers with a value, returns or throws; a last statement that does none of these and can
    /// complete leaves the call without a value, text no rule gives a meaning (E4). The bare <c>throw</c>
    /// of O7 stands in the arm's last statement, not in the statements before it.
    /// </summary>
    private void CheckElseArmBody(Statement body, KurzType success)
    {
        var wasInElseArm = inElseArm;
        inElseArm = true;
        switch (body)
        {
            case ExpressionStatement es when !Types.IsVoid(success):
                {
                    var t = CheckExpression(es.Expression, success, keepCases: success is UnionType);
                    if (Types.IsVoid(t.Type) && !Types.IsError(success)) // after a call that did not type, the success may be `void`, and the arm a statement (O10, E1)
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
                PushNested(b.Statements);
                blockDepth++;
                if (b.Statements.Count == 0 && !Types.IsVoid(success) && !Types.IsError(success))
                {
                    Report(ErrorIds.Syntax, b.Line, "an arm of `else` recovers with a value of the success type, returns or throws; an empty block does none (O5)");
                }

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

                blockDepth--;
                PopNested();
                PopScope();
                break;
            default:
                if (CheckStatement(body) == Flow.Completes && !Types.IsVoid(success) && !Types.IsError(success))
                {
                    Report(ErrorIds.Syntax, body.Line, "an arm of `else` recovers with a value of the success type, returns or throws; a statement that completes does none (O5)");
                }

                break;
        }

        inElseArm = wasInElseArm;
    }
}
