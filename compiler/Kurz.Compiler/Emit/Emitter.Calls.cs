using System.Globalization;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    // ---- calls -----------------------------------------------------------------------------------

    /// <summary>
    /// The value of a call, null for a <c>void</c> one. With <paramref name="keepCases"/> the whole
    /// union stays, for a <c>match</c>, an <c>else</c> or a variable with a written union type (O4);
    /// otherwise every case beside the success leaves the function here (O2).
    /// </summary>
    private string? EmitCall(CallExpression c, bool keepCases)
    {
        var info = model.CallOf(c);
        var result = Sub(info.Result);
        var value = info.Kind switch
        {
            CallKind.Print => EmitPrint(c),
            CallKind.Conversion => EmitConversion(c, info.Target!),
            CallKind.ListNew => EmptyList(),
            CallKind.DataNew => EmitConstruction(c, info, (DataType)result),
            CallKind.ListAdd => EmitListAdd(c),
            CallKind.ListWhere => EmitListWhere(c),
            CallKind.Function => EmitFunctionCall(c, info),
            CallKind.Value => EmitValueCall(c),
            _ => throw new InvalidOperationException(info.Kind.ToString()),
        };
        if (result is UnionType union && !keepCases)
        {
            return Propagate(value!, union, c.Line);
        }

        return Types.IsVoid(result) ? null : value;
    }

    /// <summary>O2: the success value of a call, or the leaving of the function with the case the call returned (O3).</summary>
    private string? Propagate(string value, UnionType union, int line)
    {
        var tag = fn.Value($"extractvalue %kz.union {value}, 0");
        var payload = fn.Value($"extractvalue %kz.union {value}, 1");
        var success = fn.Value($"icmp eq i32 {tag}, 0");
        var ok = fn.Label("success");
        var leave = fn.Label("leave");
        fn.CondBranch(success, ok, leave);
        fn.Block(leave);
        Leave(tag, payload, union, Enumerable.Range(1, union.Cases.Count - 1).ToList(), line);
        fn.Block(ok);
        var successType = union.Cases[0];
        return Types.IsVoid(successType) ? null : Unbox(payload, successType);
    }

    /// <summary>
    /// O3: leaves the function with the case a call returned, under the index the function's own
    /// result gives it and with the same box; only the listed cases of the call's union can be the
    /// one at hand, so a tag outside them is never reached, and no case at all ends the block as
    /// unreachable.
    /// </summary>
    private void Leave(string tag, string payload, UnionType union, List<int> cases, int line)
    {
        if (cases.Count == 0)
        {
            fn.Unreachable();
            return;
        }

        var target = returnType as UnionType ?? throw Unsupported(line, $"a `{union.Cases[cases[0]]}` that leaves code whose result is `{returnType}`");
        var dead = fn.Label("notag");
        var labels = cases.Select(i => fn.Label($"case{i}")).ToList();
        fn.Emit($"switch i32 {tag}, label %{dead} [ {string.Join(" ", cases.Select((i, k) => $"i32 {i.ToString(CultureInfo.InvariantCulture)}, label %{labels[k]}"))} ]");
        for (var k = 0; k < cases.Count; k++)
        {
            fn.Block(labels[k]);
            fn.Return($"%kz.union {MakeUnion(CaseIndex(target, union.Cases[cases[k]], line), payload)}");
        }

        fn.Block(dead);
        fn.Unreachable();
    }

    private string? EmitPrint(CallExpression c)
    {
        var argument = c.Arguments[0].Value;
        var text = TextOf(EmitExpression(argument), TypeOf(argument), argument.Line);
        var ptr = fn.Value($"extractvalue %kz.str {text}, 0");
        var len = fn.Value($"extractvalue %kz.str {text}, 1");
        StoreLine(c.Line);
        fn.Emit($"call void @kz_print(ptr {ptr}, i64 {len})");
        return null;
    }

    private string EmptyList()
    {
        var withBuffer = fn.Value("insertvalue %kz.list undef, ptr null, 0");
        return fn.Value($"insertvalue %kz.list {withBuffer}, i64 0, 1");
    }

    /// <summary>The type arguments of a call, in the terms of the function being emitted (T19, T27).</summary>
    private Dictionary<TypeParameterSymbol, KurzType> InstanceMap(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> callMap) =>
        f.TypeParameters.ToDictionary(p => p, p => Sub(callMap.TryGetValue(p, out var t) ? t : throw new InvalidOperationException($"`{p.Name}` of `{f.Name}` is unbound")));

    private string EmitFunctionCall(CallExpression c, CallInfo info)
    {
        var f = info.Function!;
        var instanceMap = InstanceMap(f, info.Map);
        var name = Instance(f, instanceMap);
        var (arguments, copyBacks) = Arguments(
            c,
            info,
            j => Types.Substitute(f.Parameters[j].Type, instanceMap),
            j => f.Parameters[j].Mut,
            j => f.Declaration!.Parameters[j].Default);
        var result = Types.Substitute(f.ReturnType, instanceMap);
        StoreLine(c.Line);
        var call = $"notail call {Ir(result)} {name}({string.Join(", ", arguments)})";
        var value = Types.IsVoid(result) ? Emitted(call) : fn.Value(call);
        foreach (var copyBack in copyBacks)
        {
            copyBack();
        }

        return value;
    }

    private string Emitted(string instruction)
    {
        fn.Emit(instruction);
        return "void";
    }

    /// <summary>F11: a call of a function value: its code with its environment first, then the arguments by position.</summary>
    private string EmitValueCall(CallExpression c)
    {
        var type = TypeOf(c.Callee) as FunctionType ?? throw Unsupported(c.Line, $"a call of a `{TypeOf(c.Callee)}`");
        var callee = EmitExpression(c.Callee);
        var code = fn.Value($"extractvalue %kz.fn {callee}, 0");
        var env = fn.Value($"extractvalue %kz.fn {callee}, 1");
        var arguments = new List<string> { $"ptr {env}" };
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            var a = c.Arguments[i].Value;
            arguments.Add($"{Ir(type.Parameters[i])} {Convert(EmitExpression(a), TypeOf(a), type.Parameters[i], a.Line)}");
        }

        StoreLine(c.Line);
        var call = $"notail call {Ir(type.Result)} {code}({string.Join(", ", arguments)})";
        return Types.IsVoid(type.Result) ? Emitted(call) : fn.Value(call);
    }

    /// <summary>
    /// The arguments of a call in the order of the parameter slots (D11): the argument bound to the
    /// slot, converted to its type, or the slot's default; a <c>mut</c> slot gets the address of the
    /// caller's variable, or of a copy of the value at the end of a path, written back after the
    /// call (M7, M4). The arguments are evaluated in the order they are written.
    /// </summary>
    private (List<string> Arguments, List<Action> CopyBacks) Arguments(CallExpression c, CallInfo info, Func<int, KurzType> slotType, Func<int, bool> slotMut, Func<int, Expression?> slotDefault)
    {
        var values = new string?[c.Arguments.Count];
        for (var i = 0; i < c.Arguments.Count; i++)
        {
            if (!slotMut(info.Binding[i]))
            {
                var a = c.Arguments[i].Value;
                values[i] = Convert(EmitExpression(a), TypeOf(a), slotType(info.Binding[i]), a.Line);
            }
        }

        var arguments = new List<string>();
        var copyBacks = new List<Action>();
        for (var j = 0; j < info.SlotCount; j++)
        {
            var argument = -1;
            for (var i = 0; i < info.Binding.Count; i++)
            {
                if (info.Binding[i] == j)
                {
                    argument = i;
                }
            }

            var type = slotType(j);
            if (slotMut(j))
            {
                if (argument < 0)
                {
                    throw Unsupported(c.Line, "a `mut` parameter left to its default");
                }

                var a = c.Arguments[argument];
                var (root, members) = Path(a.Value, a.Line);
                if (members.Count == 0)
                {
                    arguments.Add($"ptr {Slot(root, a.Line)}");
                }
                else
                {
                    var temp = fn.Alloca(Ir(type), "mut");
                    fn.Emit($"store {Ir(type)} {LoadPath(root, members)}, ptr {temp}");
                    arguments.Add($"ptr {temp}");
                    copyBacks.Add(() => StorePath(root, members, fn.Value($"load {Ir(type)}, ptr {temp}")));
                }

                continue;
            }

            string value;
            if (argument >= 0)
            {
                value = values[argument]!;
            }
            else
            {
                var d = slotDefault(j) ?? throw new InvalidOperationException($"the slot {j} of the call at line {c.Line} has no argument and no default");
                value = Convert(EmitExpression(d), TypeOf(d), type, d.Line);
            }

            arguments.Add($"{Ir(type)} {value}");
        }

        return (arguments, copyBacks);
    }

    private string LoadPath(VariableSymbol root, List<MemberAccess> members)
    {
        var type = Sub(root.Type);
        var current = fn.Value($"load {Ir(type)}, ptr {Slot(root, members[0].Line)}");
        foreach (var m in members)
        {
            var data = type as DataType ?? throw Unsupported(m.Line, $"a path through a `{type}`");
            var index = FieldIndex(data, m.Member);
            current = LoadField(current, data, m.Member);
            type = Lower.FieldTypes(data)[index];
        }

        return current;
    }

    /// <summary>D2, D7, D11: a record on the heap with the type's number and its fields, the base's first.</summary>
    private string EmitConstruction(CallExpression c, CallInfo info, DataType data)
    {
        var fieldTypes = Lower.FieldTypes(data);
        var (arguments, _) = Arguments(c, info, j => fieldTypes[j], _ => false, j => FieldDefault(data.Symbol, j));
        var record = fn.Value($"call ptr @kz_alloc(i64 {RecordBytes(data).ToString(CultureInfo.InvariantCulture)})");
        fn.Emit($"store i32 {Kind(data.Symbol).ToString(CultureInfo.InvariantCulture)}, ptr {record}");
        for (var j = 0; j < fieldTypes.Count; j++)
        {
            var place = fn.Value($"getelementptr inbounds {Record(data)}, ptr {record}, i32 0, i32 {(j + 1).ToString(CultureInfo.InvariantCulture)}");
            fn.Emit($"store {arguments[j]}, ptr {place}");
        }

        return record;
    }

    /// <summary>The default of a field, from the declaration that holds it: the base's for a base field (D7).</summary>
    private static Expression? FieldDefault(DataSymbol symbol, int index)
    {
        var baseCount = symbol.Base?.Fields.Count ?? 0;
        if (symbol.Base is not null && index < baseCount)
        {
            return FieldDefault(symbol.Base, index);
        }

        return symbol.Declaration.PrimaryConstructor![index - baseCount].Default;
    }

    /// <summary>M9, M5: `Add` on a `mut` variable or a path from one: the list value in that storage grows.</summary>
    private string EmitListAdd(CallExpression c)
    {
        var m = (MemberAccess)Unwrap(c.Callee);
        var list = TypeOf(m.Target) as ListType ?? throw Unsupported(c.Line, $"`Add` on a `{TypeOf(m.Target)}`");
        var argument = c.Arguments[0].Value;
        var item = Convert(EmitExpression(argument), TypeOf(argument), list.Element, argument.Line);
        var itemSlot = fn.Alloca(Ir(list.Element), "item");
        fn.Emit($"store {Ir(list.Element)} {item}, ptr {itemSlot}");
        var (root, members) = Path(m.Target, m.Line);
        var size = Lower.Size(list.Element).ToString(CultureInfo.InvariantCulture);
        StoreLine(c.Line);
        if (members.Count == 0)
        {
            fn.Emit($"call void @kz_list_push(ptr {Slot(root, m.Line)}, ptr {itemSlot}, i64 {size})");
        }
        else
        {
            var temp = fn.Alloca("%kz.list", "list");
            fn.Emit($"store %kz.list {LoadPath(root, members)}, ptr {temp}");
            fn.Emit($"call void @kz_list_push(ptr {temp}, ptr {itemSlot}, i64 {size})");
            StorePath(root, members, fn.Value($"load %kz.list, ptr {temp}"));
        }

        return "void";
    }

    /// <summary>M9: the items that pass the test, in their order, as a new list.</summary>
    private string EmitListWhere(CallExpression c)
    {
        var m = (MemberAccess)Unwrap(c.Callee);
        var listType = TypeOf(m.Target) as ListType ?? throw Unsupported(c.Line, $"`Where` on a `{TypeOf(m.Target)}`");
        var list = EmitExpression(m.Target);
        var testExpression = c.Arguments[0].Value;
        var test = EmitExpression(testExpression);
        var code = fn.Value($"extractvalue %kz.fn {test}, 0");
        var env = fn.Value($"extractvalue %kz.fn {test}, 1");
        var element = Ir(listType.Element);
        var result = fn.Alloca("%kz.list", "where");
        fn.Emit($"store %kz.list {EmptyList()}, ptr {result}");
        var buffer = fn.Value($"extractvalue %kz.list {list}, 0");
        var count = fn.Value($"extractvalue %kz.list {list}, 1");
        var index = fn.Alloca("i64", "n");
        fn.Emit($"store i64 0, ptr {index}");
        var head = fn.Label("each");
        var body = fn.Label("test");
        var keep = fn.Label("keep");
        var next = fn.Label("next");
        var done = fn.Label("filtered");
        fn.Block(head);
        var n = fn.Value($"load i64, ptr {index}");
        var more = fn.Value($"icmp slt i64 {n}, {count}");
        fn.CondBranch(more, body, done);
        fn.Block(body);
        var place = ItemPointer(buffer, n, listType.Element);
        var item = fn.Value($"load {element}, ptr {place}");
        StoreLine(c.Line);
        var passes = fn.Value($"notail call i1 {code}(ptr {env}, {element} {item})");
        fn.CondBranch(passes, keep, next);
        fn.Block(keep);
        fn.Emit($"call void @kz_list_push(ptr {result}, ptr {place}, i64 {Lower.Size(listType.Element).ToString(CultureInfo.InvariantCulture)})");
        fn.Branch(next);
        fn.Block(next);
        var advanced = fn.Value($"add i64 {n}, 1");
        fn.Emit($"store i64 {advanced}, ptr {index}");
        fn.Branch(head);
        fn.Block(done);
        return fn.Value($"load %kz.list, ptr {result}");
    }

    // ---- lambdas (F7, F11, F15) ------------------------------------------------------------------

    /// <summary>A function value: the lambda's code, and an environment that holds the value each captured variable has now (F15).</summary>
    private string EmitLambda(LambdaExpression l)
    {
        var info = model.LambdaOf(l);
        var type = (FunctionType)Sub(info.Type);
        var captureTypes = info.Captures.Select(v => Sub(v.Type)).ToList();
        var number = module.Next().ToString(CultureInfo.InvariantCulture);
        var envType = $"%\"env.{number}\"";
        module.DefineType(envType, "{ " + string.Join(", ", captureTypes.Select(Ir)) + " }");
        var name = $"@\"lambda.{number}\"";
        var parameters = type.Parameters.Select((p, i) => $"{Ir(p)} %p{i}");
        var outerSlots = slots;
        Nested(new IrFunction($"{Ir(type.Result)} {name}({string.Join(", ", parameters.Prepend("ptr %env"))})"), type.Result, () =>
        {
            for (var k = 0; k < info.Captures.Count; k++)
            {
                var place = fn.Value($"getelementptr inbounds {envType}, ptr %env, i32 0, i32 {k.ToString(CultureInfo.InvariantCulture)}");
                var value = fn.Value($"load {Ir(captureTypes[k])}, ptr {place}");
                var slot = fn.Alloca(Ir(captureTypes[k]), info.Captures[k].Name);
                fn.Emit($"store {Ir(captureTypes[k])} {value}, ptr {slot}");
                slots[info.Captures[k]] = slot;
            }

            for (var i = 0; i < l.Parameters.Count; i++)
            {
                var variable = model.DeclarationOf(l.Parameters[i]);
                var slot = fn.Alloca(Ir(type.Parameters[i]), l.Parameters[i].Name);
                fn.Emit($"store {Ir(type.Parameters[i])} %p{i}, ptr {slot}");
                slots[variable] = slot;
            }

            if (l.ExpressionBody is not null)
            {
                EmitBody(l.ExpressionBody, type.Result);
            }
            else
            {
                EmitBlock(l.BlockBody!);
                if (!fn.Terminated)
                {
                    EmitFallThroughReturn();
                }
            }
        });

        var env = fn.Value($"call ptr @kz_alloc(i64 {StructBytes(0, captureTypes).ToString(CultureInfo.InvariantCulture)})");
        for (var k = 0; k < info.Captures.Count; k++)
        {
            var current = fn.Value($"load {Ir(captureTypes[k])}, ptr {outerSlots[info.Captures[k]]}");
            var place = fn.Value($"getelementptr inbounds {envType}, ptr {env}, i32 0, i32 {k.ToString(CultureInfo.InvariantCulture)}");
            fn.Emit($"store {Ir(captureTypes[k])} {current}, ptr {place}");
        }

        var withCode = fn.Value($"insertvalue %kz.fn undef, ptr {name}, 0");
        return fn.Value($"insertvalue %kz.fn {withCode}, ptr {env}, 1");
    }

    /// <summary>The `=> expression` of a function or a lambda: a value returned, or a call run where the result is `void` (F2, F11).</summary>
    private void EmitBody(Expression body, KurzType result)
    {
        if (Types.IsVoid(result) || (result is UnionType u && Types.IsVoid(u.Cases[0])))
        {
            EmitExpressionStatement(new ExpressionStatement(body.Line, body));
            if (!fn.Terminated)
            {
                EmitFallThroughReturn();
            }

            return;
        }

        var value = EmitExpression(body);
        EmitReturn(value, TypeOf(body), body.Line);
    }

    // ---- match (C4, C9) and else (O5) ------------------------------------------------------------

    /// <summary>A <c>match</c>: the first arm whose case the value is runs; with <paramref name="resultType"/> the arm's value is the result (C9).</summary>
    private string? EmitMatch(Expression subject, IReadOnlyList<MatchArm> arms, KurzType? resultType, int line)
    {
        var subjectType = TypeOf(subject);
        var value = EmitExpression(subject);
        var slot = resultType is null || Types.IsVoid(resultType) ? null : fn.Alloca(Ir(resultType), "match");
        var end = fn.Label("endmatch");
        string? tag = null;
        string? payload = null;
        if (subjectType is UnionType)
        {
            tag = fn.Value($"extractvalue %kz.union {value}, 0");
            payload = fn.Value($"extractvalue %kz.union {value}, 1");
        }

        foreach (var arm in arms)
        {
            var body = fn.Label("arm");
            var next = fn.Label("next");
            string? bound = null;
            switch (arm.Pattern)
            {
                case ElsePattern:
                    fn.Branch(body);
                    break;
                case TypePattern tp:
                    {
                        var type = Sub(model.PatternTypeOf(tp));
                        if (subjectType is UnionType union)
                        {
                            TestCases(union, type, tag!, payload!, body, next, line);
                        }
                        else if (subjectType is DataType d && !Types.Same(type, d))
                        {
                            fn.CondBranch(InFamily(KindOf(value), type, arm.Line), body, next);
                        }
                        else
                        {
                            fn.Branch(body);
                        }

                        break;
                    }

                case ValuePattern vp:
                    {
                        var literal = model.TypeOf(vp.Value);
                        var literalType = Sub(literal.Type);
                        var constant = Constant(literal.Constant ?? throw new InvalidOperationException($"the arm at line {arm.Line} names no constant"), literalType, arm.Line);
                        if (subjectType is UnionType union)
                        {
                            var index = CaseIndex(union, literalType, arm.Line);
                            var isCase = fn.Value($"icmp eq i32 {tag}, {index.ToString(CultureInfo.InvariantCulture)}");
                            var compare = fn.Label("literal");
                            fn.CondBranch(isCase, compare, next);
                            fn.Block(compare);
                            fn.CondBranch(Equal(Unbox(payload!, literalType), literalType, constant, literalType, arm.Line), body, next);
                        }
                        else
                        {
                            fn.CondBranch(Equal(value, subjectType, constant, literalType, arm.Line), body, next);
                        }

                        break;
                    }

                default:
                    throw new InvalidOperationException(arm.Pattern.GetType().Name);
            }

            fn.Block(body);
            if (arm.Pattern is TypePattern { Binding: not null } binding)
            {
                var type = Sub(model.PatternTypeOf(binding));
                bound = subjectType is UnionType ? Unbox(payload!, type) : value;
                var variable = model.DeclarationOf(binding);
                var variableSlot = fn.Alloca(Ir(type), binding.Binding!);
                fn.Emit($"store {Ir(type)} {bound}, ptr {variableSlot}");
                slots[variable] = variableSlot;
            }

            EmitArmBody(arm.Body, resultType, slot);
            if (!fn.Terminated)
            {
                fn.Branch(end);
            }

            fn.Block(next);
        }

        fn.Unreachable(); // every value is some arm's (O8), or the else arm ran
        fn.Block(end);
        return slot is null ? null : fn.Value($"load {Ir(resultType!)}, ptr {slot}");
    }

    /// <summary>Branches to <paramref name="body"/> when the union holds a value the arm's type takes: its own case, or a case of its base type holding a record of its family (C4, D6).</summary>
    private void TestCases(UnionType union, KurzType type, string tag, string payload, string body, string next, int line)
    {
        var any = false;
        for (var i = 0; i < union.Cases.Count; i++)
        {
            var c = union.Cases[i];
            var exact = Types.Same(c, type);
            var derived = !exact && type is DataType d && c is DataType cd && Types.Assignable(d, cd);
            if (!exact && !derived)
            {
                continue;
            }

            any = true;
            var tryNext = fn.Label("try");
            var isCase = fn.Value($"icmp eq i32 {tag}, {i.ToString(CultureInfo.InvariantCulture)}");
            if (exact)
            {
                fn.CondBranch(isCase, body, tryNext);
            }
            else
            {
                var kind = fn.Label("kind");
                fn.CondBranch(isCase, kind, tryNext);
                fn.Block(kind);
                fn.CondBranch(InFamily(KindOf(Unbox(payload, c)), type, line), body, tryNext);
            }

            fn.Block(tryNext);
        }

        if (!any)
        {
            throw new InvalidOperationException($"`{type}` is no case of `{union}` at line {line}: the checker reports it");
        }

        fn.Branch(next);
    }

    /// <summary>Whether a record's type number is the type's or one derived from it (D6).</summary>
    private string InFamily(string kind, KurzType type, int line)
    {
        var data = type as DataType ?? throw Unsupported(line, $"a type test on a `{type}`");
        string? any = null;
        foreach (var (_, k) in Family(data, line))
        {
            var test = fn.Value($"icmp eq i32 {kind}, {k.ToString(CultureInfo.InvariantCulture)}");
            any = any is null ? test : fn.Value($"or i1 {any}, {test}");
        }

        return any!;
    }

    /// <summary>What an arm does: a value stored as the result where the match is one (C9), or a statement.</summary>
    private void EmitArmBody(Statement body, KurzType? resultType, string? slot)
    {
        if (slot is null || resultType is null)
        {
            EmitStatement(body);
            return;
        }

        switch (body)
        {
            case ExpressionStatement es when !Types.IsVoid(model.TypeOf(es.Expression).Type):
                {
                    var value = EmitExpression(es.Expression);
                    fn.Emit($"store {Ir(resultType)} {Convert(value, TypeOf(es.Expression), resultType, es.Line)}, ptr {slot}");
                    break;
                }

            case Block b when b.Statements.Count > 0:
                for (var i = 0; i < b.Statements.Count - 1; i++)
                {
                    EmitStatement(b.Statements[i]);
                }

                EmitArmBody(b.Statements[^1], resultType, slot);
                break;
            default:
                EmitStatement(body);
                break;
        }
    }

    /// <summary>`call else { Case => ... }`: the listed cases handled at the call, the others leaving the function (O5).</summary>
    private string? EmitElse(ElseExpression el, bool asValue)
    {
        var call = (CallExpression)Unwrap(el.Call);
        var info = model.CallOf(call);
        var result = Sub(info.Result);
        var value = EmitCall(call, keepCases: true);
        if (result is not UnionType union)
        {
            return asValue ? value : null; // no case beside the success: the arms never run
        }

        var success = union.Cases[0];
        var slot = asValue && !Types.IsVoid(success) ? fn.Alloca(Ir(success), "else") : null;
        var tag = fn.Value($"extractvalue %kz.union {value}, 0");
        var payload = fn.Value($"extractvalue %kz.union {value}, 1");
        var end = fn.Label("endelse");
        var ok = fn.Label("success");
        var handle = fn.Label("handle");
        var isSuccess = fn.Value($"icmp eq i32 {tag}, 0");
        fn.CondBranch(isSuccess, ok, handle);
        fn.Block(ok);
        if (slot is not null)
        {
            fn.Emit($"store {Ir(success)} {Unbox(payload, success)}, ptr {slot}");
        }

        fn.Branch(end);
        fn.Block(handle);
        var savedCase = elseCase;
        var covered = new HashSet<int>(); // the cases an arm takes whole: a case no arm lists leaves the function (O5, O3)
        foreach (var arm in el.Arms)
        {
            var body = fn.Label("arm");
            var next = fn.Label("next");
            KurzType? armType = null;
            switch (arm.Pattern)
            {
                case ElsePattern:
                    covered.UnionWith(Enumerable.Range(1, union.Cases.Count - 1));
                    fn.Branch(body);
                    break;
                case TypePattern tp:
                    armType = Sub(model.PatternTypeOf(tp));
                    covered.UnionWith(Enumerable.Range(1, union.Cases.Count - 1).Where(i => Types.Same(union.Cases[i], armType)));
                    TestCases(union, armType, tag, payload, body, next, arm.Line);
                    break;
                default:
                    throw new InvalidOperationException($"an arm of else names a case type (O5) at line {arm.Line}");
            }

            fn.Block(body);
            if (armType is not null)
            {
                var caseValue = Unbox(payload, armType);
                elseCase = (caseValue, armType);
                if (arm.Pattern is TypePattern { Binding: not null } binding)
                {
                    var variable = model.DeclarationOf(binding);
                    var variableSlot = fn.Alloca(Ir(armType), binding.Binding!);
                    fn.Emit($"store {Ir(armType)} {caseValue}, ptr {variableSlot}");
                    slots[variable] = variableSlot;
                }
            }
            else
            {
                elseCase = (value!, union); // the else arm: whichever case the call returned
            }

            EmitArmBody(arm.Body, Types.IsVoid(success) ? null : success, slot);
            if (!fn.Terminated)
            {
                fn.Branch(end);
            }

            fn.Block(next);
        }

        elseCase = savedCase;
        if (!fn.Terminated)
        {
            Leave(tag, payload, union, Enumerable.Range(1, union.Cases.Count - 1).Where(i => !covered.Contains(i)).ToList(), el.Line);
        }

        fn.Block(end);
        return slot is null ? null : fn.Value($"load {Ir(success)}, ptr {slot}");
    }
}
