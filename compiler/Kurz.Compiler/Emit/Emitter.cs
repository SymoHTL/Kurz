using System.Globalization;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The code generator: LLVM IR, as text, for a program the checker read without an error, from
/// the syntax tree and the checker's <see cref="SemanticModel"/>. It emits what the checker reads
/// of chapters 1 to 3, 5, 6 and 8 to 10 over the integers, <c>bool</c>, <c>char</c> and <c>string</c>
/// (<see cref="Lower.Supported"/>); a program that uses another type, or a construct the emitter
/// does not lay out, gets one <c>not supported</c> diagnostic and no IR. Generic functions and data
/// types are emitted once per set of type arguments. In a test build the arithmetic of T4 raises
/// <c>overflow</c> (T5); in a release build it wraps. Every call stores its line before it runs
/// (design record, section 6), and no call of the program is a tail call, so that a recursion that
/// does not end exhausts the stack as O11 says instead of running as a loop.
/// </summary>
public sealed partial class Emitter
{
    private static readonly IReadOnlyDictionary<TypeParameterSymbol, KurzType> NoMap = new Dictionary<TypeParameterSymbol, KurzType>();

    private readonly SemanticModel model;
    private readonly bool test;
    private readonly IrModule module = new();
    private readonly Dictionary<DataSymbol, int> kinds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> instances = new(StringComparer.Ordinal);
    private readonly Queue<(FunctionSymbol Function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> Map, string Name)> pending = new();
    private readonly HashSet<string> helpers = new(StringComparer.Ordinal);
    private readonly List<IrFunction> emitted = [];
    private readonly Dictionary<string, string> cStrings = new(StringComparer.Ordinal);

    // the function being emitted
    private IrFunction fn = null!;
    private Dictionary<VariableSymbol, string> slots = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyDictionary<TypeParameterSymbol, KurzType> map = NoMap;
    private KurzType returnType = Types.Void;
    private readonly Stack<(string Break, string Continue)> loops = new();

    private Emitter(SemanticModel model, bool test)
    {
        this.model = model;
        this.test = test;
    }

    /// <summary>
    /// The IR of the program, or null with one <c>not supported</c> diagnostic added when the program
    /// uses what the emitter does not lay out. <paramref name="checker"/> is the checker that read
    /// the program, with no error among its diagnostics.
    /// </summary>
    public static string? Emit(ProgramSyntax program, Checker checker, bool testBuild, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.HasErrors)
        {
            throw new InvalidOperationException("the emitter takes a program the checker read without an error");
        }

        var emitter = new Emitter(checker.Model, testBuild);
        try
        {
            return emitter.Run(program, checker);
        }
        catch (UnsupportedException e)
        {
            diagnostics.NotSupported(e.Line, e.Construct);
            return null;
        }
    }

    /// <summary>A construct the emitter does not lay out, at its line; the one diagnostic of the run.</summary>
    private sealed class UnsupportedException(int line, string construct) : Exception(construct)
    {
        public int Line { get; } = line;

        public string Construct { get; } = construct;
    }

    private static UnsupportedException Unsupported(int line, string construct) => new(line, construct);

    private string Run(ProgramSyntax program, Checker checker)
    {
        foreach (var d in program.Statements.OfType<TypeDeclaration>().Where(t => t.Kind == TypeDeclarationKind.Data))
        {
            var symbol = model.DataTypeOf(d);
            kinds[symbol] = kinds.Count + 1;
            foreach (var field in symbol.Fields.Where(f => !Lower.Supported(f.Type) && f.Type is not TypeParameterType))
            {
                throw Unsupported(field.Line, $"a field of type `{field.Type}`");
            }
        }

        foreach (var declaration in Runtime.All)
        {
            module.Declare(declaration);
        }

        // the top-level code: its variables live in this function (F16: no function reads them)
        Begin(new IrFunction("void @kz.main()"), NoMap, Types.Void);
        foreach (var s in program.Statements)
        {
            EmitStatement(s);
        }

        if (!fn.Terminated)
        {
            fn.Return(null);
        }

        End();
        while (pending.Count > 0)
        {
            var (function, instanceMap, name) = pending.Dequeue();
            EmitFunction(function, instanceMap, name);
        }

        var main = new IrFunction("i32 @main()");
        main.Emit("call void @kz_init()");
        main.Emit("call void @kz.main()");
        main.Return("i32 0");
        module.Add(main);
        foreach (var f in emitted)
        {
            module.Add(f);
        }

        return module.Render();
    }

    private void Begin(IrFunction function, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, KurzType result)
    {
        fn = function;
        slots = new Dictionary<VariableSymbol, string>(ReferenceEqualityComparer.Instance);
        map = instanceMap;
        returnType = result;
        loops.Clear();
    }

    private void End() => emitted.Add(fn);

    // ---- types -----------------------------------------------------------------------------------

    /// <summary>The type with the type arguments of the function being emitted put in.</summary>
    private KurzType Sub(KurzType type) => map.Count == 0 ? type : Types.Substitute(type, map);

    private KurzType TypeOf(Expression e) => Sub(model.TypeOf(e).Type);

    private string Ir(KurzType type)
    {
        if (type is DataType d)
        {
            Record(d);
        }

        return Lower.IrType(type);
    }

    /// <summary>The record type of a data type instance, defined in the module on first use.</summary>
    private string Record(DataType data)
    {
        var name = Lower.RecordName(data);
        var fields = Lower.FieldTypes(data).Select(Ir);
        module.DefineType(name, "{ i32" + string.Concat(fields.Select(f => ", " + f)) + " }");
        return name;
    }

    private int Kind(DataSymbol symbol) => kinds.TryGetValue(symbol, out var k) ? k : throw new InvalidOperationException($"`{symbol.Name}` has no type number");

    // ---- functions -------------------------------------------------------------------------------

    /// <summary>The IR name of a function for these type arguments, queued for emission on first use.</summary>
    private string Instance(FunctionSymbol f, Dictionary<TypeParameterSymbol, KurzType> instanceMap)
    {
        var arguments = f.TypeParameters.Count == 0 ? string.Empty : "<" + string.Join(", ", f.TypeParameters.Select(p => Lower.Mangle(instanceMap[p]))) + ">";
        var overload = f.Declaration!.Line;
        var key = $"f.{f.Name}.{overload.ToString(CultureInfo.InvariantCulture)}{arguments}";
        if (!instances.TryGetValue(key, out var name))
        {
            name = $"@\"{key}\"";
            instances[key] = name;
            pending.Enqueue((f, instanceMap, name));
        }

        return name;
    }

    private void EmitFunction(FunctionSymbol f, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, string name)
    {
        var d = f.Declaration!;
        try
        {
            EmitFunctionCore(f, d, instanceMap, name);
        }
        catch (NotSupportedException e)
        {
            throw Unsupported(d.Line, e.Message); // a parameter or result of a type the emitter does not lay out
        }
    }

    private void EmitFunctionCore(FunctionSymbol f, FunctionDeclaration d, IReadOnlyDictionary<TypeParameterSymbol, KurzType> instanceMap, string name)
    {
        var result = Types.Substitute(f.ReturnType, instanceMap);
        var parameters = new List<string>();
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var type = Types.Substitute(p.Type, instanceMap);
            parameters.Add(p.Mut ? $"ptr %p{i}" : $"{LowerIr(type)} %p{i}");
        }

        Begin(new IrFunction($"{LowerIr(result)} {name}({string.Join(", ", parameters)})"), instanceMap, result);
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var variable = model.DeclarationOf(p);
            if (p.Mut)
            {
                slots[variable] = $"%p{i}"; // the caller's storage: what the function writes, the caller sees (M7)
            }
            else
            {
                var type = Sub(p.Type);
                var slot = fn.Alloca(Ir(type), p.Name);
                fn.Emit($"store {Ir(type)} %p{i}, ptr {slot}");
                slots[variable] = slot;
            }
        }

        if (d.ExpressionBody is not null)
        {
            EmitBody(d.ExpressionBody, result);
        }
        else
        {
            EmitBlock(d.BlockBody!);
            if (!fn.Terminated)
            {
                EmitFallThroughReturn();
            }
        }

        End();
    }

    private string LowerIr(KurzType type)
    {
        if (type is DataType d)
        {
            Record(d);
        }

        return Lower.IrType(type);
    }

    /// <summary>The end of a body without a <c>return</c>: a <c>void</c> result, or a union whose success is <c>void</c> (O10).</summary>
    private void EmitFallThroughReturn()
    {
        if (Types.IsVoid(returnType))
        {
            fn.Return(null);
        }
        else if (returnType is UnionType)
        {
            fn.Return($"%kz.union {{ i32 0, ptr null }}");
        }
        else
        {
            fn.Unreachable(); // the checker reported missing-return for every other result (F3)
        }
    }

    /// <summary>Returns a value of <paramref name="type"/> from the function being emitted, converted to its result type (O2, T1).</summary>
    private void EmitReturn(string? value, KurzType type, int line)
    {
        if (Types.IsVoid(returnType))
        {
            fn.Return(null);
            return;
        }

        if (value is null || Types.IsVoid(type))
        {
            // a `void` success of a union result (O10)
            fn.Return($"%kz.union {{ i32 0, ptr null }}");
            return;
        }

        var converted = Convert(value, type, returnType, line);
        fn.Return($"{Ir(returnType)} {converted}");
    }

    // ---- statements ------------------------------------------------------------------------------

    private void EmitBlock(Block block)
    {
        foreach (var s in block.Statements)
        {
            if (fn.Terminated)
            {
                return; // what follows a return, a break or a throw never runs
            }

            EmitStatement(s);
        }
    }

    /// <summary>A type the emitter does not lay out, met anywhere inside the statement, is the one diagnostic, at the statement's line.</summary>
    private void EmitStatement(Statement statement)
    {
        try
        {
            EmitStatementCore(statement);
        }
        catch (NotSupportedException e)
        {
            throw Unsupported(statement.Line, e.Message);
        }
    }

    private void EmitStatementCore(Statement statement)
    {
        switch (statement)
        {
            case Block b:
                EmitBlock(b);
                break;
            case VariableDeclaration v:
                {
                    var variable = model.DeclarationOf(v);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(v.Value);
                    var slot = fn.Alloca(Ir(type), v.Name);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(v.Value), type, v.Value.Line)}, ptr {slot}");
                    slots[variable] = slot;
                    break;
                }

            case AssignOrDeclare a:
                if (model.Declares(a))
                {
                    var variable = model.DeclarationOf(a);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(a.Value);
                    var slot = fn.Alloca(Ir(type), a.Name);
                    fn.Emit($"store {Ir(type)} {value}, ptr {slot}");
                    slots[variable] = slot;
                }
                else
                {
                    var variable = model.AssignedVariableOf(a);
                    var type = Sub(variable.Type);
                    var value = EmitExpression(a.Value);
                    fn.Emit($"store {Ir(type)} {Convert(value, TypeOf(a.Value), type, a.Value.Line)}, ptr {slots[variable]}");
                }

                break;
            case Assignment a:
                EmitPathAssignment(a);
                break;
            case ExpressionStatement e:
                EmitExpressionStatement(e);
                break;
            case IfStatement i:
                EmitIf(i);
                break;
            case WhileStatement w:
                {
                    var test = fn.Label("while");
                    var body = fn.Label("do");
                    var exit = fn.Label("done");
                    fn.Block(test);
                    var condition = EmitExpression(w.Condition);
                    fn.CondBranch(condition, body, exit);
                    fn.Block(body);
                    loops.Push((exit, test));
                    EmitBlock(w.Body);
                    loops.Pop();
                    if (!fn.Terminated)
                    {
                        fn.Branch(test);
                    }

                    fn.Block(exit);
                    break;
                }

            case ForInStatement f:
                EmitForIn(f);
                break;
            case MatchStatement m:
                EmitMatch(m.Subject, m.Arms, null, m.Line);
                break;
            case BreakStatement b:
                fn.Branch(loops.Peek().Break);
                break;
            case ContinueStatement c:
                fn.Branch(loops.Peek().Continue);
                break;
            case ReturnStatement r:
                if (r.Value is null)
                {
                    EmitReturn(null, Types.Void, r.Line);
                }
                else
                {
                    var value = EmitExpression(r.Value);
                    EmitReturn(value, TypeOf(r.Value), r.Value.Line);
                }

                break;
            case ThrowStatement t:
                EmitThrow(t);
                break;
            case FunctionDeclaration or TypeDeclaration:
                break; // declared at the top level; emitted when used
            default:
                throw Unsupported(statement.Line, $"the statement `{statement.GetType().Name}`");
        }
    }

    private void EmitExpressionStatement(ExpressionStatement e)
    {
        var inner = Unwrap(e.Expression);
        switch (inner)
        {
            case CallExpression c:
                EmitCall(c, keepCases: false);
                break;
            case ElseExpression el:
                EmitElse(el, asValue: false);
                break;
            case MatchExpression m:
                EmitMatch(m.Subject, m.Arms, null, m.Line);
                break;
            default:
                throw new InvalidOperationException($"a value stands as a statement at line {e.Line}: the checker reports it");
        }
    }

    private static Expression Unwrap(Expression e) => e is ParenthesizedExpression p ? Unwrap(p.Inner) : e;

    private void EmitIf(IfStatement i)
    {
        var then = fn.Label("then");
        var otherwise = fn.Label("else");
        var exit = fn.Label("endif");
        var condition = EmitExpression(i.Condition);
        fn.CondBranch(condition, then, i.Else is null ? exit : otherwise);
        fn.Block(then);
        EmitBlock(i.Then);
        if (!fn.Terminated)
        {
            fn.Branch(exit);
        }

        if (i.Else is not null)
        {
            fn.Block(otherwise);
            EmitStatement(i.Else);
            if (!fn.Terminated)
            {
                fn.Branch(exit);
            }
        }

        fn.Block(exit);
    }

    private void EmitThrow(ThrowStatement t)
    {
        if (t.Value is null)
        {
            // a bare throw in an arm of `else` (O7): the case value the arm matched is what is thrown
            var current = elseCase ?? throw new InvalidOperationException($"a bare throw outside an arm of else at line {t.Line}");
            var text = TextOf(current.Value, current.Type, t.Line);
            Fail(text, t.Line);
            return;
        }

        var type = TypeOf(t.Value);
        var value = EmitExpression(t.Value);
        Fail(TextOf(value, type, t.Line), t.Line);
    }

    /// <summary>`error: thrown at line n: text` and the exit code 1 (O6, section 6 of the record).</summary>
    private void Fail(string text, int line)
    {
        var ptr = fn.Value($"extractvalue %kz.str {text}, 0");
        var len = fn.Value($"extractvalue %kz.str {text}, 1");
        StoreLine(line);
        fn.Emit($"call void @kz_throw(ptr {ptr}, i64 {len}, i32 {line.ToString(CultureInfo.InvariantCulture)})");
        fn.Unreachable();
    }

    /// <summary>An exception of the run-time table (chapter 12) at its line.</summary>
    private void FailWith(string id, int line)
    {
        StoreLine(line);
        fn.Emit($"call void @kz_fail(ptr {CString(id)}, i32 {line.ToString(CultureInfo.InvariantCulture)})");
        fn.Unreachable();
    }

    /// <summary>The store of the line before a call (design record, section 6).</summary>
    private void StoreLine(int line) => fn.Emit($"store i32 {line.ToString(CultureInfo.InvariantCulture)}, ptr @kz_line");

    /// <summary>A null-terminated constant for the runtime's C strings.</summary>
    private string CString(string text)
    {
        if (!cStrings.TryGetValue(text, out var name))
        {
            var (pointer, _) = module.String(text + "\0");
            name = pointer;
            cStrings[text] = name;
        }

        return name;
    }

    private void EmitForIn(ForInStatement f)
    {
        var variable = model.DeclarationOf(f);
        var elementType = Sub(variable.Type);
        var slot = fn.Alloca(Ir(elementType), f.Variable);
        slots[variable] = slot;
        var body = fn.Label("for");
        var next = fn.Label("next");
        var exit = fn.Label("endfor");
        if (Unwrap(f.Collection) is RangeExpression range)
        {
            // C8: both ends once, the end below the start is the exception before the first round, then each number in order
            var start = Convert(EmitExpression(range.Start), TypeOf(range.Start), elementType, range.Start.Line);
            var end = Convert(EmitExpression(range.End), TypeOf(range.End), elementType, range.End.Line);
            var ir = Ir(elementType);
            var signed = Types.IsSigned(elementType);
            var reversed = fn.Value($"icmp {(signed ? "slt" : "ult")} {ir} {end}, {start}");
            var fail = fn.Label("reversed");
            var go = fn.Label("range");
            fn.CondBranch(reversed, fail, go);
            fn.Block(fail);
            FailWith(ErrorIds.ReversedRangeAtRunTime, range.Line);
            fn.Block(go);
            var counter = fn.Alloca(ir, "i");
            fn.Emit($"store {ir} {start}, ptr {counter}");
            if (range.Inclusive)
            {
                fn.Block(body);
                var i = fn.Value($"load {ir}, ptr {counter}");
                fn.Emit($"store {ir} {i}, ptr {slot}");
                loops.Push((exit, next));
                EmitBlock(f.Body);
                loops.Pop();
                fn.Block(next);
                var last = fn.Value($"load {ir}, ptr {counter}");
                var done = fn.Value($"icmp eq {ir} {last}, {end}");
                var step = fn.Label("step");
                fn.CondBranch(done, exit, step);
                fn.Block(step);
                var plus = fn.Value($"add {ir} {last}, 1");
                fn.Emit($"store {ir} {plus}, ptr {counter}");
                fn.Branch(body);
            }
            else
            {
                var check = fn.Label("check");
                fn.Block(check);
                var i = fn.Value($"load {ir}, ptr {counter}");
                var more = fn.Value($"icmp {(signed ? "slt" : "ult")} {ir} {i}, {end}");
                fn.CondBranch(more, body, exit);
                fn.Block(body);
                fn.Emit($"store {ir} {i}, ptr {slot}");
                loops.Push((exit, next));
                EmitBlock(f.Body);
                loops.Pop();
                fn.Block(next);
                var again = fn.Value($"load {ir}, ptr {counter}");
                var plus = fn.Value($"add {ir} {again}, 1");
                fn.Emit($"store {ir} {plus}, ptr {counter}");
                fn.Branch(check);
            }

            fn.Block(exit);
            return;
        }

        // C6: the list as it is when the loop starts; the loop variable takes each item in order
        var listType = TypeOf(f.Collection);
        if (listType is not ListType)
        {
            throw Unsupported(f.Collection.Line, $"a `for` over a `{listType}`");
        }

        var list = EmitExpression(f.Collection);
        var buffer = fn.Value($"extractvalue %kz.list {list}, 0");
        var count = fn.Value($"extractvalue %kz.list {list}, 1");
        var index = fn.Alloca("i64", "n");
        fn.Emit($"store i64 0, ptr {index}");
        var head = fn.Label("each");
        fn.Block(head);
        var n = fn.Value($"load i64, ptr {index}");
        var left = fn.Value($"icmp slt i64 {n}, {count}");
        fn.CondBranch(left, body, exit);
        fn.Block(body);
        var item = ItemPointer(buffer, n, elementType);
        var value = fn.Value($"load {Ir(elementType)}, ptr {item}");
        fn.Emit($"store {Ir(elementType)} {value}, ptr {slot}");
        loops.Push((exit, next));
        EmitBlock(f.Body);
        loops.Pop();
        fn.Block(next);
        var m = fn.Value($"load i64, ptr {index}");
        var advanced = fn.Value($"add i64 {m}, 1");
        fn.Emit($"store i64 {advanced}, ptr {index}");
        fn.Branch(head);
        fn.Block(exit);
    }

    /// <summary>The address of the item at <paramref name="index"/> of a list buffer whose header is 16 bytes; the index is known to be in range.</summary>
    private string ItemPointer(string buffer, string index, KurzType elementType)
    {
        var items = fn.Value($"getelementptr inbounds i8, ptr {buffer}, i64 16");
        return fn.Value($"getelementptr inbounds {Ir(elementType)}, ptr {items}, i64 {index}");
    }

    // ---- paths (M4) ------------------------------------------------------------------------------

    /// <summary>The members of a path from a variable, outermost first, and the variable it starts at.</summary>
    private (VariableSymbol Root, List<MemberAccess> Members) Path(Expression target, int line)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(target);
        while (root is MemberAccess m)
        {
            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        if (root is not NameExpression n)
        {
            throw Unsupported(line, "a path that does not start at a variable");
        }

        return (model.VariableOf(n), members);
    }

    /// <summary>`root.a.b = value`: the variable takes a copy of its value with the field at the end of the path replaced, as nested `with` (M4, D5).</summary>
    private void EmitPathAssignment(Assignment a)
    {
        var (root, members) = Path(a.Target, a.Line);
        var fieldType = TypeOf(a.Target);
        var value = Convert(EmitExpression(a.Value), TypeOf(a.Value), fieldType, a.Value.Line);
        StorePath(root, members, value);
    }

    private void StorePath(VariableSymbol root, List<MemberAccess> members, string value)
    {
        var rootType = Sub(root.Type);
        var current = fn.Value($"load {Ir(rootType)}, ptr {slots[root]}");
        var updated = Replace(current, rootType, members, 0, value);
        fn.Emit($"store {Ir(rootType)} {updated}, ptr {slots[root]}");
    }

    private string Replace(string record, KurzType type, List<MemberAccess> members, int at, string value)
    {
        if (type is not DataType data)
        {
            throw Unsupported(members[at].Line, $"a path through a `{type}`");
        }

        var copy = CopyRecord(record, data);
        var index = FieldIndex(data, members[at].Member);
        var fieldType = Lower.FieldTypes(data)[index];
        var place = fn.Value($"getelementptr inbounds {Record(data)}, ptr {copy}, i32 0, i32 {index + 1}");
        if (at == members.Count - 1)
        {
            fn.Emit($"store {Ir(fieldType)} {value}, ptr {place}");
        }
        else
        {
            var inner = fn.Value($"load {Ir(fieldType)}, ptr {place}");
            var replaced = Replace(inner, fieldType, members, at + 1, value);
            fn.Emit($"store {Ir(fieldType)} {replaced}, ptr {place}");
        }

        return copy;
    }

    private static int FieldIndex(DataType data, string field)
    {
        var fields = data.Symbol.Fields;
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Name == field)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"`{data}` has no field `{field}`");
    }

    /// <summary>A record with the same type number and fields on the heap (D5: a copy, the original unchanged).</summary>
    private string CopyRecord(string record, DataType data)
    {
        var size = RecordBytes(data);
        var copy = fn.Value($"call ptr @kz_alloc(i64 {size})");
        fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {copy}, ptr {record}, i64 {size}, i1 false)");
        return copy;
    }

    /// <summary>The bytes of a record as LLVM lays it out: the type number, then each field at its own alignment, the whole padded to 8.</summary>
    private static int RecordBytes(DataType data) => StructBytes(4, Lower.FieldTypes(data));

    /// <summary>The bytes of a struct of <paramref name="leading"/> bytes and these fields, each at its own alignment, the whole padded to 8.</summary>
    private static int StructBytes(int leading, IEnumerable<KurzType> fields)
    {
        var offset = leading;
        foreach (var f in fields)
        {
            var size = Lower.Size(f);
            var align = size >= 8 ? 8 : size;
            offset = (offset + align - 1) / align * align;
            offset += size;
        }

        return Math.Max((offset + 7) / 8 * 8, 8);
    }
}
