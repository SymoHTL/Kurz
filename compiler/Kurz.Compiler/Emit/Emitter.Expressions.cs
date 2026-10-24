using System.Globalization;
using System.Numerics;
using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

public sealed partial class Emitter
{
    /// <summary>The IR value of an expression, of the layout of its own type (<see cref="TypeOf"/>); the site converts it to where it goes.</summary>
    private string EmitExpression(Expression e)
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
                return fn.Value($"load {Ir(type)}, ptr {Place(Model.VariableOf(n), n.Line)}");
            case NameExpression n:
                return Instance(Model.FunctionNameOf(n), NoMap); // R17: a function of the C calling convention as a value, its address
            case MemberAccess m:
                return EmitMember(m);
            case CallExpression c:
                return Temp(EmitCall(c) ?? throw new InvalidOperationException($"a `void` call stands as a value at line {c.Line}: the checker reports it"), type);
            case IndexExpression i when TypeOf(i.Target) is PointerType p:
                return fn.Value($"load {Ir(type)}, ptr {ElementPointer(EmitExpression(i.Target), p.Pointee, i.Index)}");
            case IndexExpression i when TypeOf(i.Target) is ListType list:
                return EmitListIndex(i, list);
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
            default:
                throw Unsupported(e.Line, ExpressionName(e));
        }
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
            case NullConstant when type is PointerType or CFunctionType:
                return "null";
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

    /// <summary>A <c>%kz.str</c> constant, written as an operand is, without its type: the pointer to the bytes of the text and their count; an empty text points at a constant of no bytes, never at <c>null</c> (R15).</summary>
    private string StringValue(string text)
    {
        var (pointer, length) = module.String(text);
        return $"{{ ptr {pointer}, i64 {Number(length)} }}";
    }

    // ---- strings and members ---------------------------------------------------------------------

    /// <summary>L6: the texts of the segments, joined by the runtime's <c>Concat</c>.</summary>
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
            result = result is null ? piece : fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {result}, %kz.str {piece})");
        }

        return result ?? StringValue(string.Empty);
    }

    /// <summary>A field of a data value (D1), a string's <c>Pointer</c> and the count of its <c>Bytes</c> (R15, T15), read from the string itself, and a list's <c>Count</c> (M9); every other member is not emitted.</summary>
    private string EmitMember(MemberAccess m)
    {
        var targetType = TypeOf(m.Target);
        switch (targetType)
        {
            case DataType d:
                return Field(EmitExpression(m.Target), d, Layout.FieldIndex(d, m.Member));
            case PrimitiveType { Kind: Primitive.String } when m.Member == "Pointer":
                return fn.Value($"extractvalue %kz.str {EmitExpression(m.Target)}, 0");
            case ListType when m.Member == "Count" && Unwrap(m.Target) is MemberAccess { Member: "Bytes" } bytes && TypeOf(bytes.Target) is PrimitiveType { Kind: Primitive.String }:
                {
                    // the count of a string's bytes, which the string holds, without the list of them
                    var count = fn.Value($"extractvalue %kz.str {EmitExpression(bytes.Target)}, 1");
                    return fn.Value($"trunc i64 {count} to i32");
                }

            case PrimitiveType { Kind: Primitive.String } when m.Member is "Bytes" or "Chars":
                throw Unsupported(m.Line, $"`s.{m.Member}` as a list (T15)");
            case ListType when m.Member == "Count":
                return ListCount(EmitExpression(m.Target));
            case ListType when m.Member == "Pointer":
                throw Unsupported(m.Line, "`l.Pointer` (R15)");

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
                    return Arithmetic("sub", IntegerText(BigInteger.Zero, type), x, type, u.Line, checkedBuild: test);
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
                return Place(Model.VariableOf(n), n.Line);
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
            case TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent:
                {
                    if (!Types.IsInteger(type))
                    {
                        throw Unsupported(b.Line, $"`{op}` on a `{type}`");
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
        var op = b.Operator;
        var left = EmitExpression(b.Left);
        var right = EmitExpression(b.Right);
        if (Types.IsInteger(leftType) && Types.IsInteger(rightType))
        {
            var common = Types.CommonInteger((PrimitiveType)leftType, (PrimitiveType)rightType) ?? throw Unsupported(b.Line, $"a comparison of a `{leftType}` and a `{rightType}`");
            var l = Convert(left, leftType, common, b.Line);
            var r = Convert(right, rightType, common, b.Line);
            return fn.Value($"icmp {Condition(op, Types.IsSigned(common))} {Ir(common)} {l}, {r}");
        }

        if (leftType is PrimitiveType { Kind: Primitive.Char } && rightType is PrimitiveType { Kind: Primitive.Char })
        {
            return fn.Value($"icmp {Condition(op, signed: false)} i32 {left}, {right}");
        }

        if (leftType is PointerType or CFunctionType && rightType is PointerType or CFunctionType)
        {
            return fn.Value($"icmp {Condition(op, signed: false)} ptr {left}, {right}"); // R10: the addresses, compared as numbers that are never negative
        }

        if (op is not (TokenKind.EqualEqual or TokenKind.NotEqual))
        {
            throw Unsupported(b.Line, $"`{op}` on a `{leftType}`");
        }

        // the two sides as the type the other stands in: a derived value beside its base compares as the base, and is never equal to one (D15)
        var shared = Types.Assignable(leftType, rightType) ? rightType : leftType;
        var equal = Equal(Convert(left, leftType, shared, b.Line), Convert(right, rightType, shared, b.Line), shared, b.Line);
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

    // ---- conversions -----------------------------------------------------------------------------

    /// <summary>
    /// A value of <paramref name="from"/> where a <paramref name="to"/> is required (T1): the same type; an integer widened (T23); a
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

    // ---- the text of a value (A2, A3, A8) --------------------------------------------------------

    /// <summary>The text of a value as `print` and an interpolation show it (A1, L6), as a <c>%kz.str</c>, made by the runtime.</summary>
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
            case DataType or UnionType:
                return DerivedText(value, type, line);
            case ListType list:
                return ListText(value, list, line);
            default:
                throw Unsupported(line, $"the text of a `{type}`");
        }
    }
}
