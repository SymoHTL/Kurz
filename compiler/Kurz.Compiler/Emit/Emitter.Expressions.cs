using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    /// <summary>The case value the arm of an <c>else</c> being emitted matched, for a bare <c>throw</c> (O7).</summary>
    private (string Value, KurzType Type)? elseCase;

    /// <summary>The IR value of an expression, of the layout of its own type (<see cref="TypeOf"/>); the site converts it to where it goes.</summary>
    private string EmitExpression(Expression e)
    {
        var typed = model.TypeOf(e);
        var type = Sub(typed.Type);
        if (typed.Constant is not null)
        {
            return Constant(typed.Constant, type, e.Line);
        }

        switch (e)
        {
            case StringLiteral s:
                return EmitInterpolation(s);
            case NameExpression n:
                {
                    var variable = model.VariableOf(n);
                    return fn.Value($"load {Ir(type)}, ptr {Slot(variable, n.Line)}");
                }

            case MemberAccess m:
                return EmitMember(m, type);
            case CallExpression c:
                // the checker left the whole union on the call where a match or a written union type keeps its cases (O4); elsewhere the success stands (O2)
                return EmitCall(c, keepCases: model.TypeOf(c).Type is UnionType) ?? throw new InvalidOperationException($"a `void` call stands as a value at line {c.Line}: the checker reports it");
            case IndexExpression i:
                return EmitIndex(i, type);
            case UnaryExpression u:
                return EmitUnary(u, type);
            case BinaryExpression b:
                return EmitBinary(b, type);
            case LambdaExpression l:
                return EmitLambda(l);
            case MatchExpression m:
                return EmitMatch(m.Subject, m.Arms, type, m.Line) ?? throw new InvalidOperationException($"a match without a value at line {m.Line}");
            case WithExpression w:
                return EmitWith(w, type);
            case ElseExpression el:
                return EmitElse(el, asValue: true) ?? throw new InvalidOperationException($"an else without a value at line {el.Line}");
            case ParenthesizedExpression p:
                return EmitExpression(p.Inner);
            case RangeExpression r:
                throw new InvalidOperationException($"a range stands outside a `for` at line {r.Line}: the checker reports it");
            default:
                throw Unsupported(e.Line, $"the expression `{e.GetType().Name}`");
        }
    }

    private string Slot(VariableSymbol variable, int line) =>
        slots.TryGetValue(variable, out var slot) ? slot : throw new InvalidOperationException($"`{variable.Name}` has no storage at line {line}");

    // ---- constants -------------------------------------------------------------------------------

    private string Constant(Constant constant, KurzType type, int line)
    {
        switch (constant)
        {
            case IntegerConstant i when Types.IsInteger(type):
                return IntegerText(i.Value, type);
            case IntegerConstant i when type is PrimitiveType { Kind: Primitive.Char }:
                return IntegerText(i.Value, Types.Int);
            case BoolConstant b:
                return b.Value ? "true" : "false";
            case CharConstant c:
                return c.Value.ToString(CultureInfo.InvariantCulture);
            case StringConstant s:
                return StringValue(s.Value);
            case UnitConstant when type is DataType d:
                return UnitValue(d);
            default:
                throw Unsupported(line, $"a constant of type `{type}`");
        }
    }

    /// <summary>The LLVM literal of an integer of the type: the two's complement bits, written as the signed number LLVM reads.</summary>
    private static string IntegerText(BigInteger value, KurzType type)
    {
        var width = Lower.Size(type) * 8;
        var modulus = BigInteger.One << width;
        var bits = ((value % modulus) + modulus) % modulus;
        if (bits >= modulus >> 1)
        {
            bits -= modulus;
        }

        return bits.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A <c>%kz.str</c> constant, written as an operand is, without its type: the pointer to the literal's bytes and their count.</summary>
    private string StringValue(string text)
    {
        if (text.Length == 0)
        {
            return "{ ptr null, i64 0 }";
        }

        var (pointer, length) = module.String(text);
        return $"{{ ptr {pointer}, i64 {length.ToString(CultureInfo.InvariantCulture)} }}";
    }

    /// <summary>D8: the one value of a data type without fields is a record that holds its type number alone, defined once in the module.</summary>
    private string UnitValue(DataType data)
    {
        var name = $"@\"u.{Lower.Mangle(data)}\"";
        if (helpers.Add(name))
        {
            module.Global($"{name} = private constant {Record(data)} {{ i32 {Kind(data.Symbol).ToString(CultureInfo.InvariantCulture)} }}");
        }

        return name;
    }

    // ---- strings and members ---------------------------------------------------------------------

    /// <summary>L6: the texts of the segments, joined.</summary>
    private string EmitInterpolation(StringLiteral s)
    {
        string? result = null;
        foreach (var segment in s.Segments)
        {
            var piece = segment switch
            {
                TextSegment t => StringValue(t.Text),
                HoleSegment h => TextOf(EmitExpression(h.Expression), TypeOf(h.Expression), h.Expression.Line),
                _ => throw new InvalidOperationException(segment.GetType().Name),
            };
            result = result is null ? piece : Concat(result, piece);
        }

        return result ?? StringValue(string.Empty);
    }

    private string Concat(string a, string b)
    {
        var ap = fn.Value($"extractvalue %kz.str {a}, 0");
        var al = fn.Value($"extractvalue %kz.str {a}, 1");
        var bp = fn.Value($"extractvalue %kz.str {b}, 0");
        var bl = fn.Value($"extractvalue %kz.str {b}, 1");
        var out_ = fn.Alloca("%kz.str", "cat");
        fn.Emit($"call void @kz_concat(ptr {ap}, i64 {al}, ptr {bp}, i64 {bl}, ptr {out_})");
        return fn.Value($"load %kz.str, ptr {out_}");
    }

    private string EmitMember(MemberAccess m, KurzType type)
    {
        var targetType = TypeOf(m.Target);
        var target = EmitExpression(m.Target);
        switch (targetType)
        {
            case DataType d:
                return LoadField(target, d, m.Member);
            case ListType when m.Member == "Count":
                {
                    var count = fn.Value($"extractvalue %kz.list {target}, 1");
                    return fn.Value($"trunc i64 {count} to i32");
                }

            case PrimitiveType { Kind: Primitive.String } when m.Member is "Bytes" or "Chars":
                {
                    var ptr = fn.Value($"extractvalue %kz.str {target}, 0");
                    var len = fn.Value($"extractvalue %kz.str {target}, 1");
                    var out_ = fn.Alloca("%kz.list", m.Member.ToLowerInvariant());
                    fn.Emit($"call void @{(m.Member == "Bytes" ? "kz_str_bytes" : "kz_str_chars")}(ptr {ptr}, i64 {len}, ptr {out_})");
                    return fn.Value($"load %kz.list, ptr {out_}");
                }

            default:
                throw Unsupported(m.Line, $"the member `{m.Member}` of a `{targetType}` (a `{type}`)");
        }
    }

    private string LoadField(string record, DataType data, string field)
    {
        var index = FieldIndex(data, field);
        var fieldType = Lower.FieldTypes(data)[index];
        var place = fn.Value($"getelementptr inbounds {Record(data)}, ptr {record}, i32 0, i32 {(index + 1).ToString(CultureInfo.InvariantCulture)}");
        return fn.Value($"load {Ir(fieldType)}, ptr {place}");
    }

    /// <summary>The number of the type the record was made as, read from its first field.</summary>
    private string KindOf(string record)
    {
        return fn.Value($"load i32, ptr {record}");
    }

    private string EmitIndex(IndexExpression i, KurzType type)
    {
        var listType = TypeOf(i.Target) as ListType ?? throw Unsupported(i.Line, $"an index into a `{TypeOf(i.Target)}`");
        var list = EmitExpression(i.Target);
        var indexType = TypeOf(i.Index);
        var index = Convert(EmitExpression(i.Index), indexType, Types.Long, i.Index.Line);
        var buffer = fn.Value($"extractvalue %kz.list {list}, 0");
        var count = fn.Value($"extractvalue %kz.list {list}, 1");
        StoreLine(i.Line);
        var place = fn.Value($"call ptr @kz_list_at(ptr {buffer}, i64 {count}, i64 {index}, i64 {Lower.Size(listType.Element).ToString(CultureInfo.InvariantCulture)}, i32 {i.Line.ToString(CultureInfo.InvariantCulture)})");
        return fn.Value($"load {Ir(type)}, ptr {place}");
    }

    private string EmitWith(WithExpression w, KurzType type)
    {
        var data = type as DataType ?? throw Unsupported(w.Line, $"`with` on a `{type}`");
        var copy = CopyRecord(EmitExpression(w.Target), data);
        var fieldTypes = Lower.FieldTypes(data);
        foreach (var a in w.Assignments)
        {
            var index = FieldIndex(data, a.Field);
            var value = Convert(EmitExpression(a.Value), TypeOf(a.Value), fieldTypes[index], a.Line);
            var place = fn.Value($"getelementptr inbounds {Record(data)}, ptr {copy}, i32 0, i32 {(index + 1).ToString(CultureInfo.InvariantCulture)}");
            fn.Emit($"store {Ir(fieldTypes[index])} {value}, ptr {place}");
        }

        return copy;
    }

    // ---- operators -------------------------------------------------------------------------------

    private string EmitUnary(UnaryExpression u, KurzType type)
    {
        var operandType = TypeOf(u.Operand);
        var operand = EmitExpression(u.Operand);
        switch (u.Operator)
        {
            case TokenKind.Minus:
                {
                    var x = Convert(operand, operandType, type, u.Line);
                    return Arithmetic("sub", IntegerText(BigInteger.Zero, type), x, type, u.Line, checkedBuild: test);
                }

            case TokenKind.Bang:
                return fn.Value($"xor i1 {operand}, true");
            case TokenKind.Tilde:
                {
                    var x = Convert(operand, operandType, type, u.Line);
                    return fn.Value($"xor {Ir(type)} {x}, -1");
                }

            default:
                throw Unsupported(u.Line, $"the operator `{u.Operator}`");
        }
    }

    private string EmitBinary(BinaryExpression b, KurzType type)
    {
        var op = b.Operator;
        var leftType = TypeOf(b.Left);
        var rightType = TypeOf(b.Right);
        switch (op)
        {
            case TokenKind.AmpersandAmpersand or TokenKind.BarBar:
                {
                    // T18: the right side runs only when the left leaves the answer open
                    var isAnd = op == TokenKind.AmpersandAmpersand;
                    var slot = fn.Alloca("i1", isAnd ? "and" : "or");
                    var left = EmitExpression(b.Left);
                    fn.Emit($"store i1 {left}, ptr {slot}");
                    var right = fn.Label("rhs");
                    var done = fn.Label("logic");
                    fn.CondBranch(left, isAnd ? right : done, isAnd ? done : right);
                    fn.Block(right);
                    var r = EmitExpression(b.Right);
                    fn.Emit($"store i1 {r}, ptr {slot}");
                    fn.Branch(done);
                    fn.Block(done);
                    return fn.Value($"load i1, ptr {slot}");
                }

            case TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent:
                {
                    if (!Types.IsInteger(type))
                    {
                        throw Unsupported(b.Line, $"`{Spell(op)}` on a `{type}`");
                    }

                    var l = Convert(EmitExpression(b.Left), leftType, type, b.Line);
                    var r = Convert(EmitExpression(b.Right), rightType, type, b.Line);
                    return op switch
                    {
                        TokenKind.Plus => Arithmetic("add", l, r, type, b.Line, checkedBuild: test),
                        TokenKind.Minus => Arithmetic("sub", l, r, type, b.Line, checkedBuild: test),
                        TokenKind.Star => Arithmetic("mul", l, r, type, b.Line, checkedBuild: test),
                        _ => Divide(op == TokenKind.Slash, l, r, type, b.Line),
                    };
                }

            case TokenKind.PlusPercent or TokenKind.MinusPercent or TokenKind.StarPercent:
                {
                    // T12: wraps in every build
                    var l = Convert(EmitExpression(b.Left), leftType, type, b.Line);
                    var r = Convert(EmitExpression(b.Right), rightType, type, b.Line);
                    var instruction = op == TokenKind.PlusPercent ? "add" : op == TokenKind.MinusPercent ? "sub" : "mul";
                    return Arithmetic(instruction, l, r, type, b.Line, checkedBuild: false);
                }

            case TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual:
                return EmitComparison(b, leftType, rightType);
            case TokenKind.Ampersand or TokenKind.Bar or TokenKind.Caret:
                {
                    var l = Convert(EmitExpression(b.Left), leftType, type, b.Line);
                    var r = Convert(EmitExpression(b.Right), rightType, type, b.Line);
                    var instruction = op == TokenKind.Ampersand ? "and" : op == TokenKind.Bar ? "or" : "xor";
                    return fn.Value($"{instruction} {Ir(type)} {l}, {r}");
                }

            case TokenKind.LessLess or TokenKind.GreaterGreater:
                {
                    // T22, T28: the count is taken modulo the width of the type; `>>` keeps the sign of a signed integer
                    var l = Convert(EmitExpression(b.Left), leftType, type, b.Line);
                    var count = Resize(EmitExpression(b.Right), rightType, type);
                    var width = Lower.Size(type) * 8;
                    var masked = fn.Value($"and {Ir(type)} {count}, {(width - 1).ToString(CultureInfo.InvariantCulture)}");
                    var instruction = op == TokenKind.LessLess ? "shl" : Types.IsSigned(type) ? "ashr" : "lshr";
                    return fn.Value($"{instruction} {Ir(type)} {l}, {masked}");
                }

            default:
                throw Unsupported(b.Line, $"the operator `{Spell(op)}`");
        }
    }

    private static string Spell(TokenKind kind) => kind.ToString();

    /// <summary>`add`, `sub` or `mul` on an integer type: in a test build the overflow is the exception `overflow` (T5); otherwise it wraps (T4).</summary>
    private string Arithmetic(string instruction, string l, string r, KurzType type, int line, bool checkedBuild)
    {
        var ir = Ir(type);
        if (!checkedBuild)
        {
            return fn.Value($"{instruction} {ir} {l}, {r}");
        }

        var intrinsic = $"llvm.{(Types.IsSigned(type) ? "s" : "u")}{instruction}.with.overflow.{ir}";
        module.Declare($"declare {{ {ir}, i1 }} @{intrinsic}({ir}, {ir})");
        var pair = fn.Value($"call {{ {ir}, i1 }} @{intrinsic}({ir} {l}, {ir} {r})");
        var overflow = fn.Value($"extractvalue {{ {ir}, i1 }} {pair}, 1");
        var fail = fn.Label("overflow");
        var ok = fn.Label("ok");
        fn.CondBranch(overflow, fail, ok);
        fn.Block(fail);
        FailWith(ErrorIds.Overflow, line);
        fn.Block(ok);
        return fn.Value($"extractvalue {{ {ir}, i1 }} {pair}, 0");
    }

    /// <summary>T14, T25, T18: a division or a remainder, toward zero; by zero the exception `divide-by-zero`; the smallest value by -1 overflows (T4, T5).</summary>
    private string Divide(bool quotient, string l, string r, KurzType type, int line)
    {
        var ir = Ir(type);
        var zero = fn.Value($"icmp eq {ir} {r}, 0");
        var fail = fn.Label("divzero");
        var ok = fn.Label("divide");
        fn.CondBranch(zero, fail, ok);
        fn.Block(fail);
        FailWith(ErrorIds.DivideByZero, line);
        fn.Block(ok);
        if (!Types.IsSigned(type))
        {
            return fn.Value($"{(quotient ? "udiv" : "urem")} {ir} {l}, {r}");
        }

        var min = IntegerText(Types.Range(type).Min, type);
        var isMin = fn.Value($"icmp eq {ir} {l}, {min}");
        var isMinusOne = fn.Value($"icmp eq {ir} {r}, -1");
        var edge = fn.Value($"and i1 {isMin}, {isMinusOne}");
        if (quotient && test)
        {
            var overflow = fn.Label("divoverflow");
            var fine = fn.Label("divok");
            fn.CondBranch(edge, overflow, fine);
            fn.Block(overflow);
            FailWith(ErrorIds.Overflow, line);
            fn.Block(fine);
        }

        // the machine traps on the smallest value by -1: the divisor 1 gives what wrapping gives, the smallest value for `/` and 0 for `%`
        var divisor = fn.Value($"select i1 {edge}, {ir} 1, {ir} {r}");
        return fn.Value($"{(quotient ? "sdiv" : "srem")} {ir} {l}, {divisor}");
    }

    private string EmitComparison(BinaryExpression b, KurzType leftType, KurzType rightType)
    {
        var op = b.Operator;
        var left = EmitExpression(b.Left);
        var right = EmitExpression(b.Right);
        if (Types.IsInteger(leftType) && Types.IsInteger(rightType))
        {
            var common = Types.CommonInteger((PrimitiveType)leftType, (PrimitiveType)rightType) ?? throw Unsupported(b.Line, $"a comparison of a `{leftType}` and a `{rightType}`");
            var l = Convert(left, leftType, common, b.Line);
            var r = Convert(right, rightType, common, b.Line);
            var signed = Types.IsSigned(common);
            var condition = op switch
            {
                TokenKind.EqualEqual => "eq",
                TokenKind.NotEqual => "ne",
                TokenKind.Less => signed ? "slt" : "ult",
                TokenKind.LessEqual => signed ? "sle" : "ule",
                TokenKind.Greater => signed ? "sgt" : "ugt",
                _ => signed ? "sge" : "uge",
            };
            return fn.Value($"icmp {condition} {Ir(common)} {l}, {r}");
        }

        if (leftType is PrimitiveType { Kind: Primitive.Char } && rightType is PrimitiveType { Kind: Primitive.Char })
        {
            var condition = op switch
            {
                TokenKind.EqualEqual => "eq",
                TokenKind.NotEqual => "ne",
                TokenKind.Less => "ult",
                TokenKind.LessEqual => "ule",
                TokenKind.Greater => "ugt",
                _ => "uge",
            };
            return fn.Value($"icmp {condition} i32 {left}, {right}");
        }

        if (op is not (TokenKind.EqualEqual or TokenKind.NotEqual))
        {
            throw Unsupported(b.Line, $"`{Spell(op)}` on a `{leftType}`");
        }

        var equal = Equal(left, leftType, right, rightType, b.Line);
        return op == TokenKind.EqualEqual ? equal : fn.Value($"xor i1 {equal}, true");
    }

    /// <summary>Whether two values are equal by the equality Kurz gives their type (T18, D3).</summary>
    private string Equal(string left, KurzType leftType, string right, KurzType rightType, int line)
    {
        switch (leftType)
        {
            case PrimitiveType p when Types.IsInteger(p) || p.Kind is Primitive.Bool or Primitive.Char:
                return fn.Value($"icmp eq {Ir(leftType)} {left}, {right}");
            case PrimitiveType { Kind: Primitive.String }:
                {
                    var lp = fn.Value($"extractvalue %kz.str {left}, 0");
                    var ll = fn.Value($"extractvalue %kz.str {left}, 1");
                    var rp = fn.Value($"extractvalue %kz.str {right}, 0");
                    var rl = fn.Value($"extractvalue %kz.str {right}, 1");
                    var byteResult = fn.Value($"call i8 @kz_str_eq(ptr {lp}, i64 {ll}, ptr {rp}, i64 {rl})");
                    return fn.Value($"icmp ne i8 {byteResult}, 0");
                }

            case DataType d:
                {
                    // D3: records of the same type with equal fields; a derived value and a base value are compared as what they are
                    var wider = rightType is DataType r && Types.Assignable(d, r) ? r : d;
                    return fn.Value($"call i1 {EqualityHelper(wider)}(ptr {left}, ptr {right})");
                }

            case ListType or UnionType:
                return fn.Value($"call i1 {EqualityHelper(leftType)}({Ir(leftType)} {left}, {Ir(leftType)} {right})");
            default:
                throw Unsupported(line, $"`==` on a `{leftType}`");
        }
    }

    // ---- conversions -----------------------------------------------------------------------------

    /// <summary>A value of <paramref name="from"/> where a <paramref name="to"/> is required (T1): widening (T23), a derived data value as its base (D6), a case into its union (D9), a union into a wider one.</summary>
    private string Convert(string value, KurzType from, KurzType to, int line)
    {
        if (Types.Same(from, to))
        {
            return value;
        }

        if (to is UnionType target)
        {
            if (from is UnionType source)
            {
                return Retag(value, source, target, line);
            }

            return MakeUnion(CaseIndex(target, from, line), Box(value, from));
        }

        if (from is DataType && to is DataType)
        {
            return value;
        }

        if (Types.IsInteger(from) && Types.IsInteger(to))
        {
            return Resize(value, from, to);
        }

        throw Unsupported(line, $"a `{from}` where a `{to}` is required");
    }

    /// <summary>The bits of an integer in another width: extended by its own sign, or cut.</summary>
    private string Resize(string value, KurzType from, KurzType to)
    {
        var fromWidth = Lower.Size(from);
        var toWidth = Lower.Size(to);
        if (fromWidth == toWidth)
        {
            return value;
        }

        if (fromWidth < toWidth)
        {
            return fn.Value($"{(Types.IsSigned(from) ? "sext" : "zext")} {Ir(from)} {value} to {Ir(to)}");
        }

        return fn.Value($"trunc {Ir(from)} {value} to {Ir(to)}");
    }

    /// <summary>The index of the case of a union that a value of <paramref name="type"/> is: the same type, or the base of a derived data type (D6).</summary>
    private static int CaseIndex(UnionType union, KurzType type, int line)
    {
        for (var i = 0; i < union.Cases.Count; i++)
        {
            if (Types.Same(union.Cases[i], type))
            {
                return i;
            }
        }

        for (var i = 0; i < union.Cases.Count; i++)
        {
            if (type is DataType d && union.Cases[i] is DataType c && Types.Assignable(d, c))
            {
                return i;
            }
        }

        throw Unsupported(line, $"a `{type}` is no case of `{union}`");
    }

    /// <summary>A value on the heap, for the payload of a union (D9); a `void` success has none (O10).</summary>
    private string Box(string value, KurzType type)
    {
        if (Types.IsVoid(type))
        {
            return "null";
        }

        var box = fn.Value($"call ptr @kz_alloc(i64 {Lower.Size(type).ToString(CultureInfo.InvariantCulture)})");
        fn.Emit($"store {Ir(type)} {value}, ptr {box}");
        return box;
    }

    private string Unbox(string box, KurzType type) => fn.Value($"load {Ir(type)}, ptr {box}");

    private string MakeUnion(int tag, string payload)
    {
        var withTag = fn.Value($"insertvalue %kz.union undef, i32 {tag.ToString(CultureInfo.InvariantCulture)}, 0");
        return fn.Value($"insertvalue %kz.union {withTag}, ptr {payload}, 1");
    }

    /// <summary>A union value as a union that lists its cases among others: the same payload under the index the wider union gives the case.</summary>
    private string Retag(string value, UnionType from, UnionType to, int line)
    {
        var tag = fn.Value($"extractvalue %kz.union {value}, 0");
        var payload = fn.Value($"extractvalue %kz.union {value}, 1");
        var slot = fn.Alloca("%kz.union", "retag");
        var done = fn.Label("retagged");
        var labels = from.Cases.Select((_, i) => fn.Label($"case{i}")).ToList();
        var dead = fn.Label("notag");
        fn.Emit($"switch i32 {tag}, label %{dead} [ {string.Join(" ", labels.Select((l, i) => $"i32 {i.ToString(CultureInfo.InvariantCulture)}, label %{l}"))} ]");
        for (var i = 0; i < from.Cases.Count; i++)
        {
            fn.Block(labels[i]);
            var retagged = MakeUnion(CaseIndex(to, from.Cases[i], line), payload);
            fn.Emit($"store %kz.union {retagged}, ptr {slot}");
            fn.Branch(done);
        }

        fn.Block(dead);
        fn.Unreachable();
        fn.Block(done);
        return fn.Value($"load %kz.union, ptr {slot}");
    }

    /// <summary>T10, T20: a conversion written as a call of an integer type; a value that does not fit raises `overflow` in a test build and wraps in a release build.</summary>
    private string EmitConversion(CallExpression c, PrimitiveType target)
    {
        var source = c.Arguments[0].Value;
        var sourceType = TypeOf(source);
        if (!Types.IsInteger(sourceType) || !Types.IsInteger(target))
        {
            throw Unsupported(c.Line, $"a conversion from `{sourceType}` to `{target}`");
        }

        var value = EmitExpression(source);
        var fromWidth = Lower.Size(sourceType);
        var toWidth = Lower.Size(target);
        var fromSigned = Types.IsSigned(sourceType);
        var toSigned = Types.IsSigned(target);
        if (test)
        {
            string? misfit = null;
            if (toWidth > fromWidth || toWidth == fromWidth)
            {
                if (fromSigned && !toSigned)
                {
                    misfit = fn.Value($"icmp slt {Ir(sourceType)} {value}, 0"); // a negative value fits no unsigned type
                }
                else if (!fromSigned && toSigned && toWidth == fromWidth)
                {
                    misfit = fn.Value($"icmp slt {Ir(sourceType)} {value}, 0"); // the high bit set: above the signed maximum
                }
            }
            else
            {
                var cut = fn.Value($"trunc {Ir(sourceType)} {value} to {Ir(target)}");
                var back = fn.Value($"{(toSigned ? "sext" : "zext")} {Ir(target)} {cut} to {Ir(sourceType)}");
                var same = fn.Value($"icmp eq {Ir(sourceType)} {back}, {value}");
                var negativeIntoUnsigned = fromSigned && !toSigned ? fn.Value($"icmp slt {Ir(sourceType)} {value}, 0") : null;
                misfit = fn.Value($"xor i1 {same}, true");
                if (negativeIntoUnsigned is not null)
                {
                    misfit = fn.Value($"or i1 {misfit}, {negativeIntoUnsigned}");
                }
            }

            if (misfit is not null)
            {
                var fail = fn.Label("overflow");
                var ok = fn.Label("fits");
                fn.CondBranch(misfit, fail, ok);
                fn.Block(fail);
                FailWith(ErrorIds.Overflow, c.Line);
                fn.Block(ok);
            }
        }

        return Resize(value, sourceType, target);
    }

    // ---- the text of a value (A2 to A4, A8) ------------------------------------------------------

    /// <summary>The text of a value as `print` and an interpolation show it (A1, L6), as a <c>%kz.str</c>.</summary>
    private string TextOf(string value, KurzType type, int line)
    {
        switch (type)
        {
            case PrimitiveType { Kind: Primitive.String }:
                return value;
            case PrimitiveType p when Types.IsInteger(p):
                {
                    var wide = Resize(value, type, Types.IsSigned(type) ? Types.Long : Types.ULong);
                    var out_ = fn.Alloca("%kz.str", "text");
                    fn.Emit($"call void @{(Types.IsSigned(type) ? "kz_text_i64" : "kz_text_u64")}(i64 {wide}, ptr {out_})");
                    return fn.Value($"load %kz.str, ptr {out_}");
                }

            case PrimitiveType { Kind: Primitive.Bool }:
                {
                    var byteValue = fn.Value($"zext i1 {value} to i8");
                    var out_ = fn.Alloca("%kz.str", "text");
                    fn.Emit($"call void @kz_text_bool(i8 {byteValue}, ptr {out_})");
                    return fn.Value($"load %kz.str, ptr {out_}");
                }

            case PrimitiveType { Kind: Primitive.Char }:
                {
                    var out_ = fn.Alloca("%kz.str", "text");
                    fn.Emit($"call void @kz_text_char(i32 {value}, ptr {out_})");
                    return fn.Value($"load %kz.str, ptr {out_}");
                }

            case DataType or ListType or UnionType:
                return fn.Value($"call %kz.str {TextHelper(type, line)}({Ir(type)} {value})");
            default:
                throw Unsupported(line, $"the text of a `{type}`");
        }
    }

    /// <summary>Runs <paramref name="body"/> as the body of another function, then comes back to the one being emitted.</summary>
    private void Nested(IrFunction function, KurzType result, Action body)
    {
        var saved = (fn, slots, map, returnType, loops: loops.ToArray(), elseCase);
        Begin(function, map, result);
        body();
        End();
        (fn, slots, map, returnType, _, elseCase) = saved;
        loops.Clear();
        foreach (var l in saved.loops.Reverse())
        {
            loops.Push(l);
        }
    }

    /// <summary>The data types whose records a value of <paramref name="data"/> can hold: itself and every type derived from it (D6), each with its type number.</summary>
    private List<(DataType Type, int Kind)> Family(DataType data, int line)
    {
        var family = new List<(DataType, int)> { (data, Kind(data.Symbol)) };
        foreach (var symbol in kinds.Keys.Where(s => !ReferenceEquals(s, data.Symbol) && Types.HasBaseOrIs(s, data.Symbol)))
        {
            if (symbol.IsGeneric || data.TypeArguments.Count > 0)
            {
                throw Unsupported(line, $"a generic data type with a derived type (`{symbol.Name}` from `{data}`)");
            }

            family.Add((new DataType(symbol, []), Kind(symbol)));
        }

        return family;
    }

    /// <summary>The function that builds the text of a data type, a list or a union, emitted once per type.</summary>
    private string TextHelper(KurzType type, int line)
    {
        var name = $"@\"text.{Lower.Mangle(type)}\"";
        if (!helpers.Add(name))
        {
            return name;
        }

        var ir = Ir(type);
        Nested(new IrFunction($"%kz.str {name}({ir} %value)"), Types.String, () =>
        {
            switch (type)
            {
                case DataType d:
                    EmitDataText(d, line);
                    break;
                case ListType l:
                    EmitListText(l, line);
                    break;
                case UnionType u:
                    EmitUnionText(u, line);
                    break;
                default:
                    throw new InvalidOperationException(type.Text);
            }
        });
        return name;
    }

    /// <summary>A3, A4: `Name { Field = text, Field = text }`, or the name alone for a type without fields; the fields of the type the record was made as.</summary>
    private void EmitDataText(DataType data, int line)
    {
        var kind = KindOf("%value");
        var family = Family(data, line);
        var labels = family.Select(f => fn.Label("kind")).ToList();
        var dead = fn.Label("nokind");
        fn.Emit($"switch i32 {kind}, label %{dead} [ {string.Join(" ", family.Select((f, i) => $"i32 {f.Kind.ToString(CultureInfo.InvariantCulture)}, label %{labels[i]}"))} ]");
        for (var i = 0; i < family.Count; i++)
        {
            fn.Block(labels[i]);
            var (member, _) = family[i];
            var text = StringValue(member.Symbol.Name);
            if (member.Symbol.HasFields)
            {
                text = Concat(text, StringValue(" { "));
                var fieldTypes = Lower.FieldTypes(member);
                for (var f = 0; f < member.Symbol.Fields.Count; f++)
                {
                    text = Concat(text, StringValue((f == 0 ? string.Empty : ", ") + member.Symbol.Fields[f].Name + " = "));
                    var fieldValue = LoadField("%value", member, member.Symbol.Fields[f].Name);
                    text = Concat(text, TextOf(fieldValue, fieldTypes[f], line));
                }

                text = Concat(text, StringValue(" }"));
            }

            fn.Return($"%kz.str {text}");
        }

        fn.Block(dead);
        fn.Unreachable();
    }

    /// <summary>A4: `[a, b]`, and `[]` for no items.</summary>
    private void EmitListText(ListType list, int line)
    {
        var buffer = fn.Value("extractvalue %kz.list %value, 0");
        var count = fn.Value("extractvalue %kz.list %value, 1");
        var acc = fn.Alloca("%kz.str", "acc");
        fn.Emit($"store %kz.str {StringValue("[")}, ptr {acc}");
        var index = fn.Alloca("i64", "n");
        fn.Emit($"store i64 0, ptr {index}");
        var head = fn.Label("each");
        var body = fn.Label("item");
        var done = fn.Label("close");
        fn.Block(head);
        var n = fn.Value($"load i64, ptr {index}");
        var more = fn.Value($"icmp slt i64 {n}, {count}");
        fn.CondBranch(more, body, done);
        fn.Block(body);
        var first = fn.Value($"icmp eq i64 {n}, 0");
        var separator = fn.Value($"select i1 {first}, %kz.str {StringValue(string.Empty)}, %kz.str {StringValue(", ")}");
        var current = fn.Value($"load %kz.str, ptr {acc}");
        var withSeparator = Concat(current, separator);
        var item = fn.Value($"load {Ir(list.Element)}, ptr {ItemPointer(buffer, n, list.Element)}");
        var withItem = Concat(withSeparator, TextOf(item, list.Element, line));
        fn.Emit($"store %kz.str {withItem}, ptr {acc}");
        var next = fn.Value($"add i64 {n}, 1");
        fn.Emit($"store i64 {next}, ptr {index}");
        fn.Branch(head);
        fn.Block(done);
        var closing = Concat(fn.Value($"load %kz.str, ptr {acc}"), StringValue("]"));
        fn.Return($"%kz.str {closing}");
    }

    /// <summary>The text of the case the union holds.</summary>
    private void EmitUnionText(UnionType union, int line)
    {
        var tag = fn.Value("extractvalue %kz.union %value, 0");
        var payload = fn.Value("extractvalue %kz.union %value, 1");
        var labels = union.Cases.Select((_, i) => fn.Label($"case{i}")).ToList();
        var dead = fn.Label("notag");
        fn.Emit($"switch i32 {tag}, label %{dead} [ {string.Join(" ", labels.Select((l, i) => $"i32 {i.ToString(CultureInfo.InvariantCulture)}, label %{l}"))} ]");
        for (var i = 0; i < union.Cases.Count; i++)
        {
            fn.Block(labels[i]);
            var c = union.Cases[i];
            if (Types.IsVoid(c))
            {
                throw Unsupported(line, "the text of a `void` success (O10)");
            }

            fn.Return($"%kz.str {TextOf(Unbox(payload, c), c, line)}");
        }

        fn.Block(dead);
        fn.Unreachable();
    }

    /// <summary>The function that compares two values of a data type, a list or a union (D3), emitted once per type.</summary>
    private string EqualityHelper(KurzType type)
    {
        var name = $"@\"eq.{Lower.Mangle(type)}\"";
        if (!helpers.Add(name))
        {
            return name;
        }

        var ir = Ir(type);
        Nested(new IrFunction($"i1 {name}({ir} %a, {ir} %b)"), Types.Bool, () =>
        {
            switch (type)
            {
                case DataType d:
                    {
                        var ka = KindOf("%a");
                        var kb = KindOf("%b");
                        var sameKind = fn.Value($"icmp eq i32 {ka}, {kb}");
                        var compare = fn.Label("fields");
                        var differ = fn.Label("differ");
                        fn.CondBranch(sameKind, compare, differ);
                        fn.Block(differ);
                        fn.Return("i1 false");
                        fn.Block(compare);
                        var family = Family(d, d.Symbol.Line);
                        var labels = family.Select(_ => fn.Label("kind")).ToList();
                        var dead = fn.Label("nokind");
                        fn.Emit($"switch i32 {ka}, label %{dead} [ {string.Join(" ", family.Select((f, i) => $"i32 {f.Kind.ToString(CultureInfo.InvariantCulture)}, label %{labels[i]}"))} ]");
                        for (var i = 0; i < family.Count; i++)
                        {
                            fn.Block(labels[i]);
                            var (member, _) = family[i];
                            var fieldTypes = Lower.FieldTypes(member);
                            var unequal = fn.Label("unequal");
                            for (var f = 0; f < member.Symbol.Fields.Count; f++)
                            {
                                var fa = LoadField("%a", member, member.Symbol.Fields[f].Name);
                                var fb = LoadField("%b", member, member.Symbol.Fields[f].Name);
                                var equal = Equal(fa, fieldTypes[f], fb, fieldTypes[f], member.Symbol.Fields[f].Line);
                                var next = fn.Label("field");
                                fn.CondBranch(equal, next, unequal);
                                fn.Block(next);
                            }

                            fn.Return("i1 true");
                            fn.Block(unequal);
                            fn.Return("i1 false");
                        }

                        fn.Block(dead);
                        fn.Unreachable();
                        break;
                    }

                case ListType l:
                    {
                        var ca = fn.Value("extractvalue %kz.list %a, 1");
                        var cb = fn.Value("extractvalue %kz.list %b, 1");
                        var sameCount = fn.Value($"icmp eq i64 {ca}, {cb}");
                        var walk = fn.Label("walk");
                        var no = fn.Label("unequal");
                        fn.CondBranch(sameCount, walk, no);
                        fn.Block(no);
                        fn.Return("i1 false");
                        fn.Block(walk);
                        var ba = fn.Value("extractvalue %kz.list %a, 0");
                        var bb = fn.Value("extractvalue %kz.list %b, 0");
                        var index = fn.Alloca("i64", "n");
                        fn.Emit($"store i64 0, ptr {index}");
                        var head = fn.Label("each");
                        var body = fn.Label("item");
                        var yes = fn.Label("equal");
                        fn.Block(head);
                        var n = fn.Value($"load i64, ptr {index}");
                        var more = fn.Value($"icmp slt i64 {n}, {ca}");
                        fn.CondBranch(more, body, yes);
                        fn.Block(body);
                        var ia = fn.Value($"load {Ir(l.Element)}, ptr {ItemPointer(ba, n, l.Element)}");
                        var ib = fn.Value($"load {Ir(l.Element)}, ptr {ItemPointer(bb, n, l.Element)}");
                        var equalItem = Equal(ia, l.Element, ib, l.Element, 0);
                        var step = fn.Label("step");
                        fn.CondBranch(equalItem, step, no);
                        fn.Block(step);
                        var next = fn.Value($"add i64 {n}, 1");
                        fn.Emit($"store i64 {next}, ptr {index}");
                        fn.Branch(head);
                        fn.Block(yes);
                        fn.Return("i1 true");
                        break;
                    }

                case UnionType u:
                    {
                        var ta = fn.Value("extractvalue %kz.union %a, 0");
                        var tb = fn.Value("extractvalue %kz.union %b, 0");
                        var pa = fn.Value("extractvalue %kz.union %a, 1");
                        var pb = fn.Value("extractvalue %kz.union %b, 1");
                        var sameTag = fn.Value($"icmp eq i32 {ta}, {tb}");
                        var cases = fn.Label("cases");
                        var no = fn.Label("unequal");
                        fn.CondBranch(sameTag, cases, no);
                        fn.Block(no);
                        fn.Return("i1 false");
                        fn.Block(cases);
                        var labels = u.Cases.Select((_, i) => fn.Label($"case{i}")).ToList();
                        var dead = fn.Label("notag");
                        fn.Emit($"switch i32 {ta}, label %{dead} [ {string.Join(" ", labels.Select((l, i) => $"i32 {i.ToString(CultureInfo.InvariantCulture)}, label %{l}"))} ]");
                        for (var i = 0; i < u.Cases.Count; i++)
                        {
                            fn.Block(labels[i]);
                            var c = u.Cases[i];
                            if (Types.IsVoid(c))
                            {
                                fn.Return("i1 true");
                                continue;
                            }

                            var equal = Equal(Unbox(pa, c), c, Unbox(pb, c), c, 0);
                            fn.Return($"i1 {equal}");
                        }

                        fn.Block(dead);
                        fn.Unreachable();
                        break;
                    }

                default:
                    throw new InvalidOperationException(type.Text);
            }
        });
        return name;
    }
}
