using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using FunctionType = Kurz.Compiler.Semantics.FunctionType;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    /// <summary>
    /// The IR value of an expression, of the layout of its own type (<see cref="TypeOf"/>); the site converts it to where it goes. Its
    /// instructions carry its line in the line tables, and what the expression around it emits after it carries that one's again.
    /// </summary>
    private string EmitExpression(Expression e)
    {
        var f = fn;
        var around = f.Location;
        At(e.Line);
        var value = EmitExpressionAt(e);
        f.Location = around;
        return value;
    }

    private string EmitExpressionAt(Expression e)
    {
        var typed = Model.TypeOf(e);
        var type = Sub(typed.Type);
        RequireSupported(type, e.Line);
        if (typed.Constant is not null)
        {
            return Constant(typed.Constant, type, e.Line);
        }

        switch (e)
        {
            case StringLiteral s:
                return EmitInterpolation(s);
            case NameExpression n when Model.IsVariable(n):
                {
                    var stored = Sub(Model.VariableOf(n).Type);
                    return HeldCopy(n, Narrowed(fn.Value($"load {Ir(stored)}, ptr {Place(Model.VariableOf(n), n.Line)}"), stored, type), type);
                }

            case NameExpression n:
                return Instance(Model.FunctionNameOf(n), NoMap); // R17: a function of the C calling convention as a value, its address
            case MemberAccess m when Model.IsChainMember(m, out var member):
                return Temp(EmitChainMember(m, Sub(member), (UnionType)type), type);
            case MemberAccess m when Model.IsClockRead(m):
                return EmitNow(m.Line);
            case MemberAccess m:
                return HeldCopy(m, EmitMember(m, type), type);
            case CallExpression c:
                return Temp(EmitCall(c) ?? throw new InvalidOperationException($"a `void` call stands as a value at line {c.Line}: the checker reports it"), type);
            case IndexExpression { Conditional: true } i:
                return Temp(EmitConditionalIndex(i, (UnionType)type), type);
            case IndexExpression i when TypeOf(i.Target) is PointerType p:
                return fn.Value($"load {Ir(type)}, ptr {ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index)}");
            case IndexExpression i when TypeOf(i.Target) is ListType list:
                return HeldCopy(i, EmitListIndex(i, list), type);
            case IndexExpression i when TypeOf(i.Target) is MapType map:
                return EmitMapRead(i, map);
            case ListLiteral l:
                return Temp(EmitListLiteral(l, (ListType)type), type);
            case UnaryExpression u:
                return EmitUnary(u, type);
            case BinaryExpression b:
                return EmitBinary(b, type);
            case ParenthesizedExpression p:
                return EmitExpression(p.Inner);
            case WithExpression w:
                return Temp(EmitWith(w), type);
            case MatchExpression m:
                return Temp(EmitMatch(m.Subject, m.Arms, type)!, type);
            case ElseExpression el:
                return Temp(EmitElse(el, asValue: true) ?? throw new InvalidOperationException($"`else` with a `void` success stands as a value at line {el.Line}: the checker reports it"), type);
            case LambdaExpression l:
                return EmitLambda(l, (FunctionType)type);
            default:
                throw Unsupported(e.Line, ExpressionName(e));
        }
    }

    /// <summary>
    /// A read the checker found its expression holds while a call in it changes the place it was read from, of a type it copies
    /// (M7, the owner, 2026-10-25, "Copied"): a value of its own, a temporary of the region, so that the function values in it keep
    /// their environments past the change.
    /// </summary>
    private string HeldCopy(Expression e, string value, KurzType type)
    {
        if (!Model.IsHeldCopy(e) || !Counted(type))
        {
            return value;
        }

        Retain(value, type);
        return Temp(value, type);
    }

    /// <summary>What an expression the emitter does not lay out is, for its <c>not supported</c>.</summary>
    private static string ExpressionName(Expression e) => e switch
    {
        LambdaExpression => "a lambda (F7)",
        IndexExpression => "an index into a list (M9)",
        MemberAccess m => $"the member `{m.Member}`",
        ParenthesizedExpression p => ExpressionName(p.Inner),
        _ => $"the expression `{e.GetType().Name}`",
    };

    // ---- constants -------------------------------------------------------------------------------

    private string Constant(Constant constant, KurzType type, int line)
    {
        switch (constant)
        {
            case IntegerConstant i when Types.IsInteger(type) || Types.IsTime(type):
                return IntegerText(i.Value, type);
            case IntegerConstant i when type is PrimitiveType { Kind: Primitive.Char }:
                return IntegerText(i.Value, Types.Int);
            case RealConstant r when type is PrimitiveType { Kind: Primitive.Double }:
                return RealText(r.Value);
            case RealConstant r when type is PrimitiveType { Kind: Primitive.Float }:
                return RealText((float)r.Value);
            case DecimalConstant m when Types.IsDecimal(type):
                return DecimalText(m.Value);
            case BoolConstant b:
                return b.Value ? "true" : "false";
            case CharConstant c:
                return c.Value.ToString(CultureInfo.InvariantCulture);
            case StringConstant s:
                return StringValue(s.Value);
            case NullConstant when type is PointerType or CFunctionType:
                return "null";
            case NullConstant when type is NullType:
                return "zeroinitializer"; // N1: the value of a nullable type's case `null`, of no size
            case UnitConstant when type is DataType d && layout.IsBlock(d):
                {
                    // D8: the one value of a type without fields, a block that is never written, since a write copies a block first
                    var name = $"@\"unit.{Lower.Mangle(d)}\"";
                    if (helpers.Add(name))
                    {
                        module.Global($"{name} = private unnamed_addr constant {layout.Block(d.Symbol)} {{ i32 {Number(layout.Number(d))} }}");
                    }

                    return name;
                }

            case UnitConstant when type is DataType:
                return "zeroinitializer"; // D8: a structure of no fields
            case EnumConstant e when type is EnumType enumType:
                return IntegerText(Lower.EnumStored(enumType.Symbol, e.Index), type); // D12, D18: its number, or its place
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

    /// <summary>The LLVM literal of a <c>double</c>, and of a <c>float</c>, which a <c>double</c> holds exactly: its 64 bits as a <c>double</c>, in hexadecimal.</summary>
    private static string RealText(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>A <c>%kz.str</c> constant, written as an operand is, without its type: the pointer to the bytes of the text and their count; an empty text points at a constant of no bytes, never at <c>null</c> (R15).</summary>
    private string StringValue(string text)
    {
        var (pointer, length) = module.String(text);
        return $"{{ ptr {pointer}, i64 {Number(length)} }}";
    }

    // ---- strings and members ---------------------------------------------------------------------

    /// <summary>
    /// L6: the texts of the segments, copied into one block that holds their bytes together (<see cref="InOneBlock"/>); each text a hole
    /// made is a temporary of the region, and so is the block.
    /// </summary>
    private string EmitInterpolation(StringLiteral s)
    {
        var pieces = new List<string>();
        foreach (var segment in s.Segments)
        {
            pieces.Add(segment switch
            {
                TextSegment t => StringValue(t.Text),
                HoleSegment h => Text(EmitExpression(h.Expression), TypeOf(h.Expression), h.Expression.Line),
                _ => throw new InvalidOperationException(segment.GetType().Name),
            });
        }

        // one hole of a string is the string itself, and the interpolation a new value with a count of its own, as Rust's `format!`
        // makes a new `String`, so that a call around it holds nothing of the place the hole read (M7, the owner, 2026-10-29, "New
        // value, as format!")
        if (s.Segments is [HoleSegment { Expression: var only }] && TypeOf(only) is PrimitiveType { Kind: Primitive.String })
        {
            Retain(pieces[0], Types.String);
            return Temp(pieces[0], Types.String);
        }

        return pieces.Count switch
        {
            0 => StringValue(string.Empty),
            1 => pieces[0],
            _ => Temp(InOneBlock(pieces), Types.String),
        };
    }

    /// <summary>
    /// The texts in one block of <c>string.Allocate</c> that holds their bytes together, each copied in after the one before, as Rust's
    /// <c>format!</c> writes its pieces into one <c>String</c> and C#'s interpolation into one buffer, with no text made of the texts
    /// before the last; more bytes than the largest <c>int</c> end the process with the count asked for (T15).
    /// </summary>
    private string InOneBlock(List<string> texts)
    {
        // the count of a text's bytes, without the highest bit, which marks a text that holds a block (T15)
        var counts = texts.Select(t => fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {t}, 1")}, 9223372036854775807")).ToList();
        var total = counts.Aggregate((sum, count) => fn.Value($"add i64 {sum}, {count}"));
        var bytes = fn.Value($"call ptr {RuntimeFunction("AllocateString")}(i64 {total})");
        module.Declare("declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1)");
        var at = "0";
        for (var i = 0; i < texts.Count; i++)
        {
            var to = fn.Value($"getelementptr i8, ptr {bytes}, i64 {at}");
            fn.Emit($"call void @llvm.memcpy.p0.p0.i64(ptr {to}, ptr {fn.Value($"extractvalue %kz.str {texts[i]}, 0")}, i64 {counts[i]}, i1 false)");
            at = fn.Value($"add i64 {at}, {counts[i]}");
        }

        return TakenOver(bytes, total);
    }

    /// <summary>
    /// A field of a data value (D1), a string's <c>Pointer</c> and the count of its <c>Bytes</c> (R15, T15), read from the string itself,
    /// a string's <c>Bytes</c> as a list (T15), a list's <c>Count</c> (M9) and <c>Pointer</c> (R15), and a listener's <c>Port</c> (S9);
    /// every other member is not emitted.
    /// </summary>
    private string EmitMember(MemberAccess m, KurzType type) => MemberOf(m, () => EmitExpression(m.Target), TypeOf(m.Target), type);

    /// <summary>The member of the value <paramref name="target"/> gives, of the type <paramref name="targetType"/>, as <paramref name="type"/>: a field of a nullable type read where its path is known not null is its value (N3).</summary>
    private string MemberOf(MemberAccess m, Func<string> target, KurzType targetType, KurzType type)
    {
        switch (targetType)
        {
            case DataType d:
                {
                    var index = Layout.FieldIndex(d, m.Member);
                    return Narrowed(Field(target(), d, index), Layout.Fields(d)[index], type);
                }

            case PrimitiveType { Kind: Primitive.String } when m.Member == "Pointer":
                return fn.Value($"extractvalue %kz.str {target()}, 0");
            case ListType when m.Member == "Count" && Unwrap(m.Target) is MemberAccess { Member: "Bytes" } bytes && TypeOf(bytes.Target) is PrimitiveType { Kind: Primitive.String }:
                {
                    // the count of a string's bytes, which the string holds, without the list of them
                    var count = fn.Value($"extractvalue %kz.str {EmitExpression(bytes.Target)}, 1");
                    return fn.Value($"trunc i64 {count} to i32");
                }

            case PrimitiveType { Kind: Primitive.String } when m.Member == "Bytes":
                return Temp(StringBytes(target()), new ListType(Types.Byte));
            case PrimitiveType { Kind: Primitive.String } when m.Member == "Chars":
                throw Unsupported(m.Line, "`s.Chars` as a list (T15)");
            case ListType when m.Member == "Count":
                return ListCount(target());
            case MapType when m.Member == "Count":
                return fn.Value($"call i32 {StdFunction("MapCount")}(ptr {target()})"); // M9
            case ListType list when m.Member == "Pointer":
                return ListPointer(target(), list.Element);
            case EnumType e when m.Member == "Number":
                return EnumNumber(target(), e); // D18
            case HandleType when m.Member == "Port":
                return fn.Value($"load i32, ptr {HandleField(target(), 12)}"); // S9

            default:
                throw Unsupported(m.Line, $"the member `{m.Member}` of {Lower.Refusal(targetType)}");
        }
    }

    /// <summary>R10: the address of the element at <paramref name="index"/> of the memory a pointer points to, counted in elements of the pointee.</summary>
    private string ElementPointer(string pointer, KurzType pointee, Expression index)
    {
        var offset = Convert(EmitExpression(index), TypeOf(index), Types.Long, index.Line);
        return fn.Value($"getelementptr {Ir(pointee)}, ptr {pointer}, i64 {offset}");
    }

    // ---- operators -------------------------------------------------------------------------------

    private string EmitUnary(UnaryExpression u, KurzType type)
    {
        switch (u.Operator)
        {
            case TokenKind.Minus:
                {
                    var x = Convert(EmitExpression(u.Operand), TypeOf(u.Operand), type, u.Line);
                    if (Types.IsFloating(type))
                    {
                        return fn.Value($"fneg {Ir(type)} {x}"); // the sign turned, of a zero, an infinity and NaN as well
                    }

                    if (Types.IsDecimal(type))
                    {
                        return DecimalNegate(x);
                    }

                    return Arithmetic("sub", IntegerText(BigInteger.Zero, type), x, type, u.Line, checkedBuild: test || Types.IsTime(type)); // T31: a duration in every build
                }

            case TokenKind.Bang:
                return fn.Value($"xor i1 {EmitExpression(u.Operand)}, true");
            case TokenKind.Tilde:
                {
                    var x = Convert(EmitExpression(u.Operand), TypeOf(u.Operand), type, u.Line);
                    return fn.Value($"xor {Ir(type)} {x}, -1");
                }

            case TokenKind.Star:
                return fn.Value($"load {Ir(type)}, ptr {EmitExpression(u.Operand)}"); // R10: the read through a pointer
            case TokenKind.Ampersand:
                return AddressOf(u.Operand); // R10: the address of a variable or of an element
            default:
                throw Unsupported(u.Line, $"the operator `{u.Operator}`");
        }
    }

    /// <summary>R10: `&amp;x` takes a variable, `&amp;p[i]` an element; the address of a field is not emitted.</summary>
    private string AddressOf(Expression operand)
    {
        switch (Unwrap(operand))
        {
            case NameExpression n:
                return NarrowedPlace(n);
            case IndexExpression i when TypeOf(i.Target) is PointerType p:
                return ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index);
            default:
                throw Unsupported(operand.Line, $"the address of {ExpressionName(operand)}");
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
                    InRegion(() => fn.Emit($"store i1 {EmitExpression(b.Right)}, ptr {slot}")); // what the right side makes is let go of where it ran

                    fn.Branch(done);
                    fn.Block(done);
                    return fn.Value($"load i1, ptr {slot}");
                }

            case TokenKind.Plus or TokenKind.Minus when leftType is PointerType pointer:
                return PointerArithmetic(b, pointer, rightType);
            case TokenKind.Plus or TokenKind.Minus when Types.IsTime(type):
                return TimeArithmetic(op, EmitExpression(b.Left), leftType, EmitExpression(b.Right), rightType, type, b.Line);
            case TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent:
                {
                    if (!Types.IsNumber(type))
                    {
                        throw Unsupported(b.Line, $"`{op}` on {Types.A(type)}");
                    }

                    var l = Convert(EmitExpression(b.Left), leftType, type, b.Line);
                    var r = Convert(EmitExpression(b.Right), rightType, type, b.Line);
                    if (Types.IsDecimal(type))
                    {
                        return DecimalArithmetic(op, l, r, b.Line);
                    }

                    if (Types.IsFloating(type))
                    {
                        // T18, T25: IEEE 754's operations, as C# has them: by zero `/` gives an infinity or NaN and `%` NaN, and `%` is the
                        // remainder of the division toward zero
                        var instruction = op switch
                        {
                            TokenKind.Plus => "fadd",
                            TokenKind.Minus => "fsub",
                            TokenKind.Star => "fmul",
                            TokenKind.Slash => "fdiv",
                            _ => "frem",
                        };
                        return fn.Value($"{instruction} {Ir(type)} {l}, {r}");
                    }

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

            case TokenKind.QuestionQuestion:
                return Temp(EmitCoalesce(b, type), type);
            case TokenKind.EqualEqual or TokenKind.NotEqual when leftType is NullType || rightType is NullType:
                return EmitNullTest(leftType is NullType ? b.Right : b.Left, op == TokenKind.EqualEqual);
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
                    var masked = fn.Value($"and {Ir(type)} {count}, {Number(width - 1)}");
                    var instruction = op == TokenKind.LessLess ? "shl" : Types.IsSigned(type) ? "ashr" : "lshr";
                    return fn.Value($"{instruction} {Ir(type)} {l}, {masked}");
                }

            default:
                throw Unsupported(b.Line, $"the operator `{op}`");
        }
    }

    /// <summary>R10: `p + n` and `p - n` count elements of the pointee and wrap, as Rust's <c>wrapping_add</c>; `p - q` is the count of elements from `q` to `p`.</summary>
    private string PointerArithmetic(BinaryExpression b, PointerType pointer, KurzType rightType)
    {
        var left = EmitExpression(b.Left);
        var size = Number(Lower.Size(pointer.Pointee));
        if (rightType is PointerType)
        {
            var from = fn.Value($"ptrtoint ptr {EmitExpression(b.Right)} to i64");
            var to = fn.Value($"ptrtoint ptr {left} to i64");
            var bytes = fn.Value($"sub i64 {to}, {from}");
            return fn.Value($"sdiv exact i64 {bytes}, {size}");
        }

        var count = Convert(EmitExpression(b.Right), rightType, Types.Long, b.Right.Line);
        if (b.Operator == TokenKind.Minus)
        {
            count = fn.Value($"sub i64 0, {count}");
        }

        return fn.Value($"getelementptr {Ir(pointer.Pointee)}, ptr {left}, i64 {count}");
    }

    /// <summary>`add`, `sub` or `mul` on an integer type: in a test build the overflow is the exception `overflow` (T5); otherwise it wraps (T4). A time type is signed, and checked in every build (T31).</summary>
    private string Arithmetic(string instruction, string l, string r, KurzType type, int line, bool checkedBuild)
    {
        var ir = Ir(type);
        if (!checkedBuild)
        {
            return fn.Value($"{instruction} {ir} {l}, {r}");
        }

        var intrinsic = $"llvm.{(Types.IsUnsigned(type) ? "u" : "s")}{instruction}.with.overflow.{ir}";
        module.Declare($"declare {{ {ir}, i1 }} @{intrinsic}({ir}, {ir})");
        var pair = fn.Value($"call {{ {ir}, i1 }} @{intrinsic}({ir} {l}, {ir} {r})");
        var overflow = fn.Value($"extractvalue {{ {ir}, i1 }} {pair}, 1");
        var fail = fn.Label("overflow");
        var ok = fn.Label("ok");
        fn.CondBranch(overflow, fail, ok);
        fn.Block(fail);
        Raise(ErrorIds.Overflow, line);
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
        Raise(ErrorIds.DivideByZero, line);
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
            Raise(ErrorIds.Overflow, line);
            fn.Block(fine);
        }

        // the machine traps on the smallest value by -1: the divisor 1 gives what wrapping gives, the smallest value for `/` and 0 for `%`
        var divisor = fn.Value($"select i1 {edge}, {ir} 1, {ir} {r}");
        return fn.Value($"{(quotient ? "sdiv" : "srem")} {ir} {l}, {divisor}");
    }

    private string EmitComparison(BinaryExpression b, KurzType leftType, KurzType rightType)
    {
        var left = EmitExpression(b.Left);
        var right = EmitExpression(b.Right);
        return Types.NonNull(leftType) is not null || Types.NonNull(rightType) is not null
            ? LiftedEquality(b, left, leftType, right, rightType)
            : Compare(b.Operator, left, leftType, right, rightType, b.Line);
    }

    /// <summary>
    /// <c>==</c> and <c>!=</c> with a nullable value on a side, lifted as C#'s (section 4, the owner, 2026-10-30, "Lift == and !="):
    /// <c>null</c> equals <c>null</c> alone, and two values compare as their types do.
    /// </summary>
    private string LiftedEquality(BinaryExpression b, string left, KurzType leftType, string right, KurzType rightType)
    {
        string IsNull(string value, KurzType type) => type is UnionType union && Types.NonNull(union) is not null ? IsCase(Tag(value, union), union, Types.Null) : "false";
        var leftNull = IsNull(left, leftType);
        var rightNull = IsNull(right, rightType);
        var slot = fn.Alloca("i1", "equal");
        var nulls = fn.Label("null");
        var values = fn.Label("values");
        var done = fn.Label("compared");
        fn.CondBranch(fn.Value($"or i1 {leftNull}, {rightNull}"), nulls, values);
        fn.Block(nulls);
        fn.Emit($"store i1 {fn.Value($"and i1 {leftNull}, {rightNull}")}, ptr {slot}");
        fn.Branch(done);
        fn.Block(values);
        var l = Types.NonNull(leftType) is { } li ? (WithoutNull(left, (UnionType)leftType), li) : (left, leftType);
        var r = Types.NonNull(rightType) is { } ri ? (WithoutNull(right, (UnionType)rightType), ri) : (right, rightType);
        fn.Emit($"store i1 {Compare(TokenKind.EqualEqual, l.Item1, l.Item2, r.Item1, r.Item2, b.Line)}, ptr {slot}");
        fn.Branch(done);
        fn.Block(done);
        var equal = fn.Value($"load i1, ptr {slot}");
        return b.Operator == TokenKind.EqualEqual ? equal : fn.Value($"xor i1 {equal}, true");
    }

    /// <summary>T18, D3: two values that are not nullable, compared as their types are.</summary>
    private string Compare(TokenKind op, string left, KurzType leftType, string right, KurzType rightType, int line)
    {
        if (Types.IsFloating(leftType) || Types.IsFloating(rightType))
        {
            // T18, T23: both sides as the wider floating-point type, an integer side widened into it; NaN is equal to nothing and
            // unequal to everything, itself among them, and 0 equals -0, as in C#
            var common = leftType is PrimitiveType { Kind: Primitive.Double } || rightType is PrimitiveType { Kind: Primitive.Double } ? Types.Double : Types.Float;
            var l = Convert(left, leftType, common, line);
            var r = Convert(right, rightType, common, line);
            return fn.Value($"fcmp {FloatingCondition(op)} {Ir(common)} {l}, {r}");
        }

        if (Types.IsDecimal(leftType) || Types.IsDecimal(rightType))
        {
            // T18, T23: by their values, an integer side widened into a `decimal`
            var order = DecimalOrder(Convert(left, leftType, Types.Decimal, line), Convert(right, rightType, Types.Decimal, line));
            return fn.Value($"icmp {Condition(op, signed: true)} i32 {order}, 0");
        }

        if (Types.IsInteger(leftType) && Types.IsInteger(rightType))
        {
            var common = Types.CommonInteger((PrimitiveType)leftType, (PrimitiveType)rightType) ?? throw Unsupported(line, $"a comparison of {Types.A(leftType)} and {Types.A(rightType)}");
            var l = Convert(left, leftType, common, line);
            var r = Convert(right, rightType, common, line);
            return fn.Value($"icmp {Condition(op, Types.IsSigned(common))} {Ir(common)} {l}, {r}");
        }

        if (leftType is PrimitiveType { Kind: Primitive.Char } && rightType is PrimitiveType { Kind: Primitive.Char })
        {
            return fn.Value($"icmp {Condition(op, signed: false)} i32 {left}, {right}");
        }

        if (leftType is PrimitiveType { Kind: Primitive.String } && op is not (TokenKind.EqualEqual or TokenKind.NotEqual))
        {
            // T32: in the order of their bytes, which in UTF-8 is the order of their code points
            var order = fn.Value($"call i32 {RuntimeFunction("Order")}(%kz.str {left}, %kz.str {right})");
            return fn.Value($"icmp {Condition(op, signed: true)} i32 {order}, 0");
        }

        if (Types.IsTime(leftType) && Types.IsTime(rightType))
        {
            return TimeComparison(op, left, leftType, right, rightType);
        }

        if (leftType is EnumType e)
        {
            return fn.Value($"icmp {Condition(op, Lower.EnumSigned(e.Symbol))} {Ir(leftType)} {left}, {right}"); // D12, D18: by their numbers
        }

        if (leftType is PointerType or CFunctionType && rightType is PointerType or CFunctionType)
        {
            return fn.Value($"icmp {Condition(op, signed: false)} ptr {left}, {right}"); // R10: the addresses, compared as numbers that are never negative
        }

        if (op is not (TokenKind.EqualEqual or TokenKind.NotEqual))
        {
            throw Unsupported(line, $"`{op}` on {Types.A(leftType)}");
        }

        // the two sides as the type the other stands in: a derived value beside its base compares as the base, and is never equal to one (D15)
        var shared = Types.Assignable(leftType, rightType) ? rightType : leftType;
        // M9: two lists by IEEE's `==`, and two values of a union, whose `double` or `float` case compares as such values alone do (section
        // 4, the owner, 2026-10-30, "Case and value, as Rust")
        var equal = Equal(Convert(left, leftType, shared, line), Convert(right, rightType, shared, line), shared, line, ieee: shared is ListType or UnionType);
        return op == TokenKind.EqualEqual ? equal : fn.Value($"xor i1 {equal}, true");
    }

    private static string Condition(TokenKind op, bool signed) => op switch
    {
        TokenKind.EqualEqual => "eq",
        TokenKind.NotEqual => "ne",
        TokenKind.Less => signed ? "slt" : "ult",
        TokenKind.LessEqual => signed ? "sle" : "ule",
        TokenKind.Greater => signed ? "sgt" : "ugt",
        _ => signed ? "sge" : "uge",
    };

    private static string FloatingCondition(TokenKind op) => op switch
    {
        TokenKind.EqualEqual => "oeq",
        TokenKind.NotEqual => "une",
        TokenKind.Less => "olt",
        TokenKind.LessEqual => "ole",
        TokenKind.Greater => "ogt",
        _ => "oge",
    };

    // ---- conversions -----------------------------------------------------------------------------

    /// <summary>
    /// A value of <paramref name="from"/> where a <paramref name="to"/> is required (T1): the same type; an integer widened (T23); an
    /// integer or a <c>float</c> into a floating-point type (T23); an integer into a <c>decimal</c> (T23); a narrow time value into
    /// the wide pair (T29); a
    /// derived data value as its base (D6), the same block, and no data value as a type it does not derive from; a value into the one
    /// case of a union it goes to (D9, T8 with D9); a union into a union (D9 with O1).
    /// </summary>
    private string Convert(string value, KurzType from, KurzType to, int line)
    {
        if (Types.Same(from, to) || (from is PointerType or CFunctionType && to is PointerType or CFunctionType))
        {
            return value;
        }

        if (Types.IsInteger(from) && Types.IsInteger(to))
        {
            return Resize(value, from, to);
        }

        if ((Types.IsInteger(from) || Types.IsFloating(from)) && Types.IsFloating(to))
        {
            return ToFloating(value, from, to);
        }

        if (Types.IsInteger(from) && Types.IsDecimal(to))
        {
            return DecimalFromInteger(value, from);
        }

        if (Types.IsTime(from) && Types.IsTime(to))
        {
            return TimeBits(value, from, Types.IsWideTime(to)); // T29: the narrow pair widens
        }

        if (from is DataType && to is DataType && Types.Assignable(from, to))
        {
            return value; // D6: a block of the derived type reads as a block of its base
        }

        if (to is UnionType union)
        {
            if (from is UnionType source)
            {
                return ConvertUnion(value, source, union, line);
            }

            var c = Types.CasesFor(from, union.Cases).Single();
            return MakeUnion(union, c, Convert(value, from, c, line));
        }

        throw Unsupported(line, $"{Types.A(from)} where {Types.A(to)} is required");
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

    /// <summary>
    /// T23, T26: an integer as the nearest value a floating-point type holds, a <c>float</c> as the <c>double</c> that holds it, and a
    /// <c>double</c> as the nearest <c>float</c>, an infinity beyond its range, NaN and the infinities kept.
    /// </summary>
    private string ToFloating(string value, KurzType from, KurzType to)
    {
        if (Types.IsInteger(from))
        {
            return fn.Value($"{(Types.IsSigned(from) ? "sitofp" : "uitofp")} {Ir(from)} {value} to {Ir(to)}");
        }

        return fn.Value($"{(Lower.Size(from) < Lower.Size(to) ? "fpext" : "fptrunc")} {Ir(from)} {value} to {Ir(to)}");
    }

    /// <summary>
    /// T26: a floating-point value into an integer type, its fraction dropped toward zero. A value beyond the type's range, an
    /// infinity and NaN raise <c>overflow</c> in a test build; in a release build they give the type's largest or smallest value,
    /// and NaN 0, as LLVM's saturating conversion does.
    /// </summary>
    private string FromFloating(string value, KurzType from, KurzType to, int line)
    {
        var ir = Ir(from);
        var instruction = Types.IsSigned(to) ? "fptosi" : "fptoui";
        if (!test)
        {
            var intrinsic = $"llvm.{instruction}.sat.{Ir(to)}.{(ir == "float" ? "f32" : "f64")}";
            module.Declare($"declare {Ir(to)} @{intrinsic}({ir})");
            return fn.Value($"call {Ir(to)} @{intrinsic}({ir} {value})");
        }

        // it fits above the smallest value minus one and below the largest plus one, which no NaN is; where the floating-point
        // type cannot hold the smallest minus one, it holds nothing between that and the smallest
        var (min, max) = Types.Range(to);
        var low = Holds(from, min - 1)
            ? fn.Value($"fcmp ogt {ir} {value}, {RealText((double)(min - 1))}")
            : fn.Value($"fcmp oge {ir} {value}, {RealText((double)min)}");
        var high = fn.Value($"fcmp olt {ir} {value}, {RealText((double)(max + 1))}");
        var fail = fn.Label("overflow");
        var ok = fn.Label("fits");
        fn.CondBranch(fn.Value($"and i1 {low}, {high}"), ok, fail);
        fn.Block(fail);
        Raise(ErrorIds.Overflow, line);
        fn.Block(ok);
        return fn.Value($"{instruction} {ir} {value} to {Ir(to)}");
    }

    /// <summary>Whether the floating-point type holds the integer exactly.</summary>
    private static bool Holds(KurzType type, BigInteger value) =>
        type is PrimitiveType { Kind: Primitive.Float } ? new BigInteger((float)value) == value : new BigInteger((double)value) == value;

    // ---- the text of a value (A2, A3, A8) --------------------------------------------------------

    /// <summary>The text of a value as <see cref="TextOf"/> makes it, a temporary of the region where it made one.</summary>
    private string Text(string value, KurzType type, int line) => MakesText(type) ? Temp(TextOf(value, type, line), Types.String) : TextOf(value, type, line);

    /// <summary>Whether <see cref="TextOf"/> makes a text for a value of the type, which holds a count its caller lets go of: for every type but a string, which is its own text, and a <c>bool</c>, an enum, <c>null</c> and a hasher, whose texts are literals.</summary>
    private static bool MakesText(KurzType type) => type is not (PrimitiveType { Kind: Primitive.String or Primitive.Bool } or EnumType or NullType or HasherType);

    /// <summary>
    /// The text of a value as `print` and an interpolation show it (A1, L6), as a <c>%kz.str</c>, made by the runtime: a string as it
    /// is, a <c>bool</c> as a literal, and for every other type a text whose count the caller holds (<see cref="MakesText"/>).
    /// </summary>
    private string TextOf(string value, KurzType type, int line)
    {
        switch (type)
        {
            case PrimitiveType { Kind: Primitive.String }:
                return value;
            case PrimitiveType p when Types.IsInteger(p):
                {
                    var signed = Types.IsSigned(type);
                    var wide = Resize(value, type, signed ? Types.Long : Types.ULong);
                    return fn.Value($"call %kz.str {RuntimeFunction(signed ? "TextOfLong" : "TextOfULong")}(i64 {wide})");
                }

            case PrimitiveType { Kind: Primitive.Bool }:
                return fn.Value($"select i1 {value}, %kz.str {StringValue("true")}, %kz.str {StringValue("false")}");
            case PrimitiveType { Kind: Primitive.Char }:
                return fn.Value($"call %kz.str {RuntimeFunction("TextOfChar")}(i32 {value})");
            case PrimitiveType { Kind: Primitive.Double }:
                return fn.Value($"call %kz.str {RuntimeFunction("TextOfDouble")}(i64 {fn.Value($"bitcast double {value} to i64")})"); // A4, A14: from its bits
            case PrimitiveType { Kind: Primitive.Float }:
                return fn.Value($"call %kz.str {RuntimeFunction("TextOfFloat")}(i32 {fn.Value($"bitcast float {value} to i32")})"); // A8, A11
            case PrimitiveType { Kind: Primitive.Decimal }:
                {
                    var (low, high) = DecimalWords(value);
                    return fn.Value($"call %kz.str {RuntimeFunction("TextOfDecimal")}(i64 {low}, i64 {high})"); // A8
                }

            case PrimitiveType p when Types.IsTime(p):
                return TimeText(value, type); // A12, A16

            case DataType or UnionType:
                return DerivedText(value, type, line);
            case EnumType e:
                return EnumText(value, e); // A4
            case NullType:
                return StringValue("null"); // A4
            case HasherType:
                return StringValue(type.Text); // H6: the name of its type and nothing of its state
            case ListType list:
                return ListText(value, list, line);
            case MapType map:
                return MapText(value, map, line);
            default:
                throw Unsupported(line, $"the text of {Types.A(type)}");
        }
    }
}
