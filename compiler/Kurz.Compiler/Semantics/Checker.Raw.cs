using Kurz.Compiler.Syntax;

namespace Kurz.Compiler.Semantics;

/// <summary>
/// The raw forms of chapter 11: <c>raw</c> blocks and the grants (R7), raw pointers (R10), inline assembly (R11), <c>naked</c>
/// functions (R12), state outside a function (R13), the conversions of a pointer and the size of a type (R14), the memory of a
/// string and a list (R15), the functions of C (R16) and the type of a C function (R17). A package's grants come from its
/// project file, and a case of the corpus takes them from its header's <c>// allow:</c> line (R7); a register class is read
/// against x86-64's table, the target a case is read for where its header names none (R11).
/// </summary>
public sealed partial class Checker
{
    /// <summary>R11: the register classes Rust gives x86-64 with the features every x86-64 target has, each with the modifiers a template may give its register and the types a value in it takes.</summary>
    private static readonly Dictionary<string, (string Modifiers, Func<KurzType, bool> Takes)> RegisterClasses = new(StringComparer.Ordinal)
    {
        ["reg"] = ("lxer", TakesWord),
        ["reg_abcd"] = ("lhxer", TakesWord),
        ["reg_byte"] = ("", t => t is PrimitiveType { Kind: Primitive.SByte or Primitive.Byte }),
        ["xmm_reg"] = ("xyz", TakesVector),
    };

    /// <summary>R11: the classes of x86-64 that Rust gives a target with a feature beyond those every x86-64 target has, with the feature.</summary>
    private static readonly Dictionary<string, string> FeatureClasses = new(StringComparer.Ordinal) { ["ymm_reg"] = "avx", ["zmm_reg"] = "avx512f", ["kreg"] = "avx512f" };

    /// <summary>R11: the classes of x86-64 that Rust takes only as a clobber, through an explicit register.</summary>
    private static readonly HashSet<string> ClobberOnlyClasses = new(StringComparer.Ordinal) { "kreg0", "x87_reg", "mmx_reg", "tmm_reg" };

    /// <summary>
    /// R11: the registers of x86-64 an operand names in quotes, each with the register it is a view of and its class, as Rust's tables of
    /// register classes and of register names give them: a smaller view and an alias are the register itself.
    /// </summary>
    private static readonly Dictionary<string, (string Base, string Class)> ExplicitRegisters = ExplicitRegistersOfX86_64();

    /// <summary>R11: the registers Rust takes as no operand on x86-64, by the register they are a view of, with Rust's reason.</summary>
    private static readonly Dictionary<string, string> ReservedRegisters = new(StringComparer.Ordinal)
    {
        ["sp"] = "the stack pointer, which the `asm` gives back as it found it",
        ["bp"] = "the frame pointer",
        ["bx"] = "the base pointer LLVM keeps for a function with a complex frame",
        ["ip"] = "the program counter, no register a value goes into",
    };

    private static Dictionary<string, (string Base, string Class)> ExplicitRegistersOfX86_64()
    {
        var registers = new Dictionary<string, (string Base, string Class)>(StringComparer.Ordinal);
        void Add(string name, string @base, string registerClass) => registers.Add(name, (@base, registerClass));
        foreach (var (@base, byteView) in new[] { ("ax", "al"), ("bx", "bl"), ("cx", "cl"), ("dx", "dl"), ("si", "sil"), ("di", "dil"), ("bp", "bpl"), ("sp", "spl") })
        {
            foreach (var name in new[] { @base, "e" + @base, "r" + @base })
            {
                Add(name, @base, "reg");
            }

            Add(byteView, @base, "reg_byte");
        }

        foreach (var name in new[] { "ip", "eip", "rip" })
        {
            Add(name, "ip", "reg");
        }

        for (var i = 8; i <= 15; i++)
        {
            foreach (var suffix in new[] { "", "w", "d" })
            {
                Add($"r{i}{suffix}", $"r{i}", "reg");
            }

            Add($"r{i}b", $"r{i}", "reg_byte");
        }

        for (var i = 0; i <= 31; i++)
        {
            Add($"xmm{i}", $"xmm{i}", i < 16 ? "xmm_reg" : "zmm_reg"); // xmm16 to xmm31 stand in the class that needs avx512f
            Add($"ymm{i}", $"xmm{i}", i < 16 ? "ymm_reg" : "zmm_reg");
            Add($"zmm{i}", $"xmm{i}", "zmm_reg");
        }

        for (var i = 0; i <= 7; i++)
        {
            Add($"k{i}", $"k{i}", i == 0 ? "kreg0" : "kreg");
            Add($"st({i})", $"st({i})", "x87_reg");
            Add($"mm{i}", $"mm{i}", "mmx_reg");
            Add($"tmm{i}", $"tmm{i}", "tmm_reg");
        }

        Add("st", "st(0)", "x87_reg");
        return registers;
    }

    private static readonly HashSet<string> ClobberAbis = new(StringComparer.Ordinal) { "C", "system", "efiapi", "win64", "sysv64" };

    private static readonly HashSet<string> AsmOptions = new(StringComparer.Ordinal) { "pure", "nomem", "readonly", "preserves_flags", "noreturn", "nostack", "att_syntax", "raw" };

    private enum Holding
    {
        /// <summary>The type holds no counted reference: a pointer points to it (R10).</summary>
        Nothing,

        /// <summary>The type is or holds a string, a list, a function value or a data value that holds one (R10).</summary>
        Counted,

        /// <summary>The type holds a type parameter, which a pointer's rule does not judge.</summary>
        Unknown,
    }

    private enum Crossing
    {
        /// <summary>A number but <c>decimal</c>, a <c>bool</c>, a raw pointer or a C function type (section 2, "Plus C-layout data").</summary>
        Yes,

        /// <summary>A string, a <c>char</c>, a time type, a list, a data type, a union or a function type.</summary>
        No,

        /// <summary>A <c>decimal</c>, which is a number of Kurz and no type of C, or a type parameter.</summary>
        Unknown,
    }

    /// <summary>x86-64's <c>reg</c>: a 16-, 32- or 64-bit integer, a <c>float</c>, a <c>double</c>, a pointer or a C function.</summary>
    private static bool TakesWord(KurzType t) =>
        t is PrimitiveType { Kind: Primitive.Short or Primitive.UShort or Primitive.Int or Primitive.UInt or Primitive.Long or Primitive.ULong or Primitive.Float or Primitive.Double }
        or PointerType or CFunctionType;

    /// <summary>x86-64's <c>xmm_reg</c>: a 32- or 64-bit integer, a <c>float</c> or a <c>double</c>, the scalars among Rust's types for it, and a pointer or a C function, which Rust's table counts as the 64-bit integer.</summary>
    private static bool TakesVector(KurzType t) =>
        t is PrimitiveType { Kind: Primitive.Int or Primitive.UInt or Primitive.Long or Primitive.ULong or Primitive.Float or Primitive.Double }
        or PointerType or CFunctionType;

    // ---- raw blocks (R7) -------------------------------------------------------------------------

    /// <summary>R7: a <c>raw</c> block compiles in a package granted <c>allow raw</c>; its body is checked as any block, and what reads or writes raw memory is allowed in it.</summary>
    private Flow CheckRawBlock(RawBlock r)
    {
        if (!allowRaw)
        {
            Report(ErrorIds.RawNotAllowed, r.Line, "a `raw` block needs the grant `allow raw`, which this package does not have (R7)");
        }

        rawDepth++;
        var flow = CheckBlock(r.Body);
        rawDepth--;
        return flow;
    }

    /// <summary>
    /// R10, R11, R13, R15 to R17: what reads or writes raw memory stands inside a <c>raw</c> block or the body of a <c>naked</c> function;
    /// elsewhere it is <c>raw-required</c> at its line. In a default value or the initial value of state such a read is no constant, and the
    /// error that says so is the value's one (F12, R13, E1).
    /// </summary>
    private void RequireRaw(int line, string what, string rule)
    {
        if (rawDepth == 0 && !readingConstant)
        {
            Report(ErrorIds.RawRequired, line, $"{what} stands inside a `raw` block ({rule})");
        }
    }

    // ---- raw pointers (R10, R14) -----------------------------------------------------------------

    /// <summary>R10: whether a value of the type holds a counted reference. A data type that holds itself does, through the reference its recursion needs (R6).</summary>
    private static Holding Holds(KurzType type, List<DataSymbol> seen) => type switch
    {
        PrimitiveType { Kind: Primitive.String } => Holding.Counted,
        PrimitiveType => Holding.Nothing,
        ListType or MapType or FunctionType => Holding.Counted, // a lambda holds what it captures (F11)
        ClassType => Holding.Counted, // K2: an instance is a counted reference (section 3)
        UnionType u => Worst(u.Cases.Select(c => Holds(c, seen))),
        DataType d => seen.Contains(d.Symbol) ? Holding.Counted : HoldsFields(d, seen),
        TypeParameterType => Holding.Unknown,
        _ => Holding.Nothing, // a pointer, a C function type, the error type
    };

    private static Holding HoldsFields(DataType d, List<DataSymbol> seen)
    {
        seen.Add(d.Symbol);
        var result = Worst(d.Symbol.Fields.Select(f => Holds(SubstituteData(d, f.Type), seen)));
        seen.RemoveAt(seen.Count - 1);
        return result;
    }

    private static Holding Worst(IEnumerable<Holding> holdings)
    {
        var list = holdings.ToList();
        return list.Contains(Holding.Counted) ? Holding.Counted : list.Contains(Holding.Unknown) ? Holding.Unknown : Holding.Nothing;
    }

    /// <summary>
    /// R10, R14: the pointer to <paramref name="pointee"/>, which holds no counted reference; <c>void</c> makes the untyped pointer. A pointee
    /// that holds one is <c>counted-pointee</c> at <paramref name="line"/>, named after <paramref name="what"/>; one that holds a type
    /// parameter is <c>not supported</c>, since R10 judges a pointee by its type and a type parameter leaves it open.
    /// </summary>
    private KurzType PointerTo(KurzType pointee, int line, string what)
    {
        if (Types.IsError(pointee))
        {
            return Types.Error;
        }

        if (Types.IsVoid(pointee))
        {
            return new PointerType(Types.Void);
        }

        if (pendingPointees is not null)
        {
            var pointer = new PointerType(pointee);
            pendingPointees.Add((pointer, line, what)); // a field's pointee can be a type whose fields stand unresolved here (F6)
            return pointer;
        }

        switch (Holds(pointee, []))
        {
            case Holding.Counted:
                Report(ErrorIds.CountedPointee, line, $"{what} takes a type that holds no counted reference, and {Types.A(pointee)} holds one (R10)");
                return Types.Error;
            case Holding.Unknown:
                NotSupported(line, "a pointer to a type that holds a type parameter (R10, T19)");
                return Types.Error;
            default:
                return new PointerType(pointee);
        }
    }

    /// <summary>
    /// R10 with F6: the pointers the fields of data types name, judged after every field is resolved, so that a pointee's fields count
    /// wherever its type is declared; a field whose type holds a pointer that is an error takes the error type, so that its uses carry none (E1).
    /// </summary>
    private void JudgePendingPointees()
    {
        var pending = pendingPointees!;
        pendingPointees = null;
        var failed = new HashSet<PointerType>(ReferenceEqualityComparer.Instance);
        foreach (var (pointer, line, what) in pending)
        {
            if (Types.IsError(PointerTo(pointer.Pointee, line, what)))
            {
                failed.Add(pointer);
            }
        }

        foreach (var symbol in failed.Count == 0 ? [] : dataDeclared.Concat(refusedTypes))
        {
            for (var i = 0; i < symbol.Fields.Count; i++)
            {
                if (HoldsPointer(symbol.Fields[i].Type, failed))
                {
                    symbol.Fields[i] = symbol.Fields[i] with { Type = Types.Error };
                }
            }
        }
    }

    private static bool HoldsPointer(KurzType type, HashSet<PointerType> pointers) => type switch
    {
        PointerType p => pointers.Contains(p) || HoldsPointer(p.Pointee, pointers),
        ListType l => HoldsPointer(l.Element, pointers),
        MapType m => HoldsPointer(m.Value, pointers),
        UnionType u => u.Cases.Any(c => HoldsPointer(c, pointers)),
        FunctionType f => f.Parameters.Append(f.Result).Any(t => HoldsPointer(t, pointers)),
        CFunctionType f => f.Parameters.Append(f.Result).Any(t => HoldsPointer(t, pointers)),
        DataType d => d.TypeArguments.Any(t => HoldsPointer(t, pointers)),
        _ => false,
    };

    /// <summary>A4 with R10 and R17: whether a value of the type holds a raw pointer or a C function value, whose text no rule gives.</summary>
    private static bool HoldsRaw(KurzType type, List<DataSymbol> seen)
    {
        switch (type)
        {
            case PointerType or CFunctionType:
                return true;
            case ListType l:
                return HoldsRaw(l.Element, seen);
            case MapType m:
                return HoldsRaw(m.Value, seen);
            case UnionType u:
                return u.Cases.Any(c => HoldsRaw(c, seen));
            case DataType d when !seen.Contains(d.Symbol):
                {
                    seen.Add(d.Symbol);
                    var result = d.Symbol.Fields.Any(f => HoldsRaw(SubstituteData(d, f.Type), seen));
                    seen.RemoveAt(seen.Count - 1);
                    return result;
                }

            default:
                return false;
        }
    }

    /// <summary>R10: an offset, and an index through a pointer, is an <c>int</c> or a <c>long</c>, as every index is: an integer that widens into a <c>long</c>.</summary>
    private static bool IsOffset(KurzType type) => Types.IsInteger(type) && Types.Assignable(type, Types.Long);

    /// <summary>R10, R14: the type read or written through a value: <c>type-mismatch</c> for a value that is no pointer and for a <c>void*</c>, through which nothing is read or written.</summary>
    private KurzType ThroughPointer(Typed pointer, int line, string form)
    {
        if (pointer.IsError)
        {
            return Types.Error;
        }

        if (pointer.Type is not PointerType p)
        {
            Report(ErrorIds.TypeMismatch, line, $"{form} reads or writes through a pointer, and {Types.A(pointer.Type)} is none (R10)");
            return Types.Error;
        }

        if (Types.IsVoid(p.Pointee))
        {
            Report(ErrorIds.TypeMismatch, line, "nothing is read or written through a `void*` (R14)");
            return Types.Error;
        }

        return p.Pointee;
    }

    /// <summary>R10: <c>*p</c> reads through a pointer, inside a <c>raw</c> block.</summary>
    private Typed CheckDeref(UnaryExpression u)
    {
        var pointee = ThroughPointer(CheckExpression(u.Operand, null), u.Line, "`*`");
        if (Types.IsError(pointee))
        {
            return Typed.Error;
        }

        RequireRaw(u.Line, "a read through a raw pointer", "R10");
        return new Typed(pointee);
    }

    /// <summary>R10: <c>p[i]</c> reads the element <c>i</c> elements after <c>p</c>, inside a <c>raw</c> block; the index is an offset.</summary>
    private Typed CheckPointerIndex(IndexExpression i, Typed target, Typed index)
    {
        var pointee = ThroughPointer(target, i.Line, "an index");
        if (!index.IsError && !IsOffset(index.Type))
        {
            Report(ErrorIds.TypeMismatch, i.Line, $"an index through a pointer is an `int` or a `long`, not {Types.A(index.Type)} (R10)");
            return Typed.Error;
        }

        if (Types.IsError(pointee))
        {
            return Typed.Error;
        }

        RequireRaw(i.Line, "a read through a raw pointer", "R10");
        return new Typed(pointee);
    }

    /// <summary>
    /// R10: <c>&amp;x</c> is the pointer to a variable, a field or an element, which needs no <c>raw</c> block; its operand is a place, and
    /// another one is text no rule gives a meaning (E4). The address of state outside a function is not read: R10 names a variable,
    /// and R13 says such state is no variable. Neither is the address of a place through a pointer, <c>&amp;*p</c>, <c>&amp;p[i]</c> or
    /// <c>&amp;(*b).Next</c>: R10 says <c>&amp;x</c> needs no <c>raw</c> block, and Rust takes such a place in <c>unsafe</c> code alone.
    /// </summary>
    private Typed CheckAddress(UnaryExpression u)
    {
        var place = Unwrap(u.Operand);
        if (place is NameExpression state && Lookup(state.Name).Variable is { Kind: VariableKind.Static })
        {
            NotSupported(u.Line, "the address of state outside a function, `&` of a `static mut` (R10, R13)");
            return Typed.Error;
        }

        var type = PlaceType(place, u.Line);
        return type is null ? Typed.Error : new Typed(PointerTo(type, u.Line, "the address `&x`"));
    }

    /// <summary>The type of a place <c>&amp;</c> takes: a variable, a field of a data value that is a place, an element of a list or through a pointer, or what a pointer points to; null where an error was reported or the type is not known.</summary>
    private KurzType? PlaceType(Expression place, int line)
    {
        var type = PlaceTypeCore(place, line);
        if (type is not null)
        {
            RecordThrough(place, new Typed(type)); // a place read without CheckExpression: a name, a field, and the parentheses around them
        }

        return type;
    }

    private KurzType? PlaceTypeCore(Expression place, int line)
    {
        switch (Unwrap(place))
        {
            case NameExpression n when Lookup(n.Name).Variable is null && (functions.ContainsKey(n.Name) || cFunctions.ContainsKey(n.Name) || dataTypes.ContainsKey(n.Name)):
                Report(ErrorIds.Syntax, line, $"`&` takes a variable, a field or an element, and `{n.Name}` is none (R10, E4)");
                return null;
            case NameExpression n:
                {
                    var t = CheckName(n);
                    return t.IsError ? null : t.Type;
                }

            case MemberAccess { Conditional: false } m:
                {
                    var target = PlaceType(m.Target, line);
                    if (target is null)
                    {
                        return null;
                    }

                    if (target is DataType d && d.Symbol.Field(m.Member) is { } field)
                    {
                        return SubstituteData(d, field.Type);
                    }

                    if (!MemberOf(target, m.Member, m.Line).IsError)
                    {
                        Report(ErrorIds.Syntax, m.Line, $"`&` takes a variable, a field or an element, and `{m.Member}` is no field of a data value (R10, E4)");
                    }

                    return null;
                }

            case UnaryExpression { Operator: TokenKind.Star } star:
                CheckExpression(star.Operand, null); // its own error stands beside the line, as an operand of `*` does (E1)
                NotSupported(line, "the address of a place through a pointer, `&*p`, `&p[i]` or `&(*b).Next` (R10)");
                return null;
            case IndexExpression index:
                {
                    Push();
                    var target = CheckExpression(index.Target, null);
                    Pop(commit: false);
                    if (target.Type is PointerType)
                    {
                        NotSupported(line, "the address of a place through a pointer, `&*p`, `&p[i]` or `&(*b).Next` (R10)");
                        CheckLoosely(index.Index);
                        return null;
                    }

                    var t = CheckExpression(place, null);
                    return t.IsError ? null : t.Type;
                }

            default:
                Report(ErrorIds.Syntax, line, "`&` takes a variable, a field or an element (R10, E4)");
                CheckExpression(place, null);
                return null;
        }
    }

    /// <summary>
    /// R10: <c>p + n</c>, <c>n + p</c> and <c>p - n</c> count elements of the pointee and give a pointer, <c>p - q</c> of one pointee type
    /// gives a <c>long</c>, and the comparisons compare two pointers of one pointee type; none reads through a pointer, and none takes a
    /// <c>void*</c> but the comparisons (R14). Null when the operator is no such one, for the operators' own rules to judge.
    /// </summary>
    private Typed? PointerOperation(BinaryExpression b, Typed left, Typed right)
    {
        var lp = left.Type as PointerType;
        var rp = right.Type as PointerType;
        var op = Operator(b.Operator);
        if (ClassOf(b.Operator) == OperatorClass.Comparison)
        {
            if (lp is not null && rp is not null && Types.Same(lp, rp))
            {
                return new Typed(Types.Bool);
            }

            Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` compares two pointers of one pointee type, not {Types.A(left.Type)} and {Types.A(right.Type)} (R10, T1)");
            return new Typed(Types.Bool);
        }

        if (b.Operator is not (TokenKind.Plus or TokenKind.Minus))
        {
            return null;
        }

        var pointer = lp ?? rp!;
        var offset = lp is not null ? right.Type : left.Type;
        var shape = (lp, rp, b.Operator) switch
        {
            (not null, not null, TokenKind.Minus) => "difference",
            (not null, null, _) when IsOffset(offset) => "offset",
            (null, not null, TokenKind.Plus) when IsOffset(offset) => "offset",
            _ => null,
        };
        if (shape is null || (shape == "difference" && !Types.Same(lp!, rp!)))
        {
            Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes a pointer and an `int` or a `long`, or two pointers of one pointee type, not {Types.A(left.Type)} and {Types.A(right.Type)} (R10, T1)");
            return Typed.Error;
        }

        if (Types.IsVoid(pointer.Pointee))
        {
            Report(ErrorIds.TypeMismatch, b.Line, $"`{op}` takes no `void*`, whose elements have no size (R14)");
            return Typed.Error;
        }

        return new Typed(shape == "difference" ? Types.Long : pointer);
    }

    /// <summary>R10: a comparison with <c>null</c>, which a pointer compares with outside a <c>raw</c> block, and N3: a nullable value; null where neither side is the literal, for the operators' own rules to judge.</summary>
    private Typed? CompareWithNull(BinaryExpression b)
    {
        var leftNull = Unwrap(b.Left) is NullLiteral;
        var rightNull = Unwrap(b.Right) is NullLiteral;
        if (leftNull == rightNull)
        {
            return null;
        }

        return CompareNullable(b, leftNull ? b.Right : b.Left, leftNull ? b.Left : b.Right);
    }

    /// <summary>R10: <c>*p = v</c>, <c>p[i] = v</c> and <c>(*p).Next = v</c> write through a pointer, inside a <c>raw</c> block; no variable is assigned, so none needs <c>mut</c>. An assignment into an element of a list is not read (M9).</summary>
    private void CheckWriteThrough(Assignment a, Expression root, List<MemberAccess> members)
    {
        KurzType current;
        if (root is IndexExpression index)
        {
            Push();
            var target = CheckExpression(index.Target, null);
            var captured = Pop(commit: false);
            if (target.IsError)
            {
                foreach (var d in captured)
                {
                    Add(d); // the target's own error, an unknown name among them, or one reported where its type was, and nothing more (E1)
                }

                CheckLoosely(index.Index, a.Value);
                return;
            }

            if (target.Type is MapType map)
            {
                foreach (var d in captured)
                {
                    Add(d);
                }

                CheckMapWrite(a, index, map, members);
                return;
            }

            if (Types.NonNull(target.Type) is MapType)
            {
                foreach (var d in captured)
                {
                    Add(d);
                }

                Report(ErrorIds.NullableUnchecked, a.Line, $"an entry is written into {Types.A(target.Type)} without a check (N2)");
                CheckLoosely(index.Index, a.Value);
                return;
            }

            if (target.Type is ListType list)
            {
                foreach (var d in captured)
                {
                    Add(d);
                }

                CheckListWrite(a, index, list, members);
                return;
            }

            if (Types.NonNull(target.Type) is ListType)
            {
                foreach (var d in captured)
                {
                    Add(d);
                }

                Report(ErrorIds.NullableUnchecked, a.Line, $"an item is written into {Types.A(target.Type)} without a check (N2)");
                CheckLoosely(index.Index, a.Value);
                return;
            }

            if (target.Type is not PointerType)
            {
                NotSupported(a.Line, "an assignment into an index (M9)");
                CheckLoosely(a.Target, a.Value);
                return;
            }

            foreach (var d in captured)
            {
                Add(d);
            }

            var offset = CheckExpression(index.Index, null); // an `int` or a `long`, so a literal takes the type L10 gives it
            current = ThroughPointer(target, index.Line, "an index");
            model.RecordType(index, new Typed(current));
            if (!offset.IsError && !IsOffset(offset.Type))
            {
                Report(ErrorIds.TypeMismatch, index.Line, $"an index through a pointer is an `int` or a `long`, not {Types.A(offset.Type)} (R10)");
                current = Types.Error;
            }
        }
        else
        {
            var star = (UnaryExpression)root;
            current = ThroughPointer(CheckExpression(star.Operand, null), star.Line, "`*`");
            model.RecordType(star, new Typed(current));
        }

        if (!Types.IsError(current))
        {
            RequireRaw(a.Line, "a write through a raw pointer", "R10");
        }

        foreach (var m in members)
        {
            if (Types.IsError(current))
            {
                break;
            }

            if (current is DataType d && d.Symbol.Field(m.Member) is { } field)
            {
                current = SubstituteData(d, field.Type);
                model.RecordType(m, new Typed(current));
                continue;
            }

            if (!MemberOf(current, m.Member, m.Line).IsError)
            {
                Report(ErrorIds.AssignImmutable, m.Line, $"`{m.Member}` is no field of a data value and cannot be assigned (M4)");
            }

            current = Types.Error;
        }

        var value = CheckExpression(a.Value, Types.IsError(current) ? null : current, keepCases: Types.KeepsCases(current));
        if (Types.IsError(current) || value.IsError)
        {
            return;
        }

        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, current))
        {
            ReportMismatch(value.Type, current, a.Value.Line, $"{Types.A(value.Type)} where {Types.A(current)} is required");
        }
    }

    /// <summary>
    /// M9, M4: <c>map[key] = value</c> sets the key's entry, an assignment into a path that starts at a <c>mut</c> variable, the map
    /// the variable or a path of data fields from it (no other member is a map); one through a nullable field is not changed through,
    /// as an assignment into such a path (N3), and a map reached through an item or a call, and a path that goes on after the key,
    /// are not read.
    /// </summary>
    private void CheckMapWrite(Assignment a, IndexExpression index, MapType map, List<MemberAccess> members)
    {
        if (!WrittenVariable(a, index, members, "an assignment into a map reached through an item or a call, or into a path that goes on after the key (M4, M9)"))
        {
            return;
        }

        CheckKey(index.Index, map);
        CheckWrittenValue(a, index, map.Value);
    }

    /// <summary>
    /// M9, M4: <c>xs[i] = value</c> writes the item at the position <c>i</c> of a list that a <c>mut</c> variable or parameter holds,
    /// or a path of data fields from one, as C# and Rust write one (section 4, the owner, 2026-10-30, "As C# and Rust"); one through a
    /// nullable field is not changed through, as an assignment into such a path (N3), and a list reached through an item or a call,
    /// and a path that goes on after the item, are not read.
    /// </summary>
    private void CheckListWrite(Assignment a, IndexExpression index, ListType list, List<MemberAccess> members)
    {
        if (!WrittenVariable(a, index, members, "an assignment into a list reached through an item or a call, or into a path that goes on after the item (M4, M9)"))
        {
            return;
        }

        var position = CheckExpression(index.Index, Types.Int);
        if (!position.IsError && !Types.IsInteger(position.Type))
        {
            Report(ErrorIds.TypeMismatch, index.Line, $"an index is an integer, not {Types.A(position.Type)} (M9, T2)");
        }

        CheckWrittenValue(a, index, list.Element);
    }

    /// <summary>
    /// M4: whether the collection an index assigns into is a variable, a <c>mut</c> one or a <c>mut</c> parameter, or a path of data
    /// fields from one, where the checker takes the write, which an arm's statement makes a change of the path for M7; a path through
    /// a nullable field is not changed through (N3), a member that is no field cannot be assigned, as in a path assignment, and
    /// anything else is <c>not supported</c> with <paramref name="unread"/>.
    /// </summary>
    private bool WrittenVariable(Assignment a, IndexExpression index, List<MemberAccess> members, string unread)
    {
        var fields = new List<MemberAccess>();
        var root = Unwrap(index.Target);
        while (root is MemberAccess { Conditional: false } m)
        {
            fields.Insert(0, m);
            root = Unwrap(m.Target);
        }

        if (members.Count == 0 && (RunsThroughInstance(fields) || root is ThisExpression || (root is NameExpression implicitRoot && Lookup(implicitRoot.Name).Variable is null && ImplicitField(implicitRoot.Name) is not null)))
        {
            return WrittenThroughInstance(a, index, fields); // K2: a collection a field of an instance holds
        }

        if (members.Count > 0 || root is not NameExpression name || !model.IsVariable(name))
        {
            NotSupported(a.Line, unread);
            CheckLoosely(index.Index, a.Value);
            return false;
        }

        var (variable, captured) = Lookup(name.Name);
        if (ThroughNullable(variable!, fields, "an assignment into a path through a nullable variable or field (N3, M4)"))
        {
            CheckLoosely(index.Index, a.Value);
            return false;
        }

        if (!IsFieldPath(variable!.Type, fields, out var notField))
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{notField}` is no field of a data value and cannot be assigned (M4)");
            CheckLoosely(index.Index, a.Value);
            return false;
        }

        var path = string.Join(".", fields.Select(m => m.Member).Prepend(name.Name));
        if (captured)
        {
            Report(ErrorIds.CaptureAssign, a.Line, $"a lambda reads `{name.Name}` and cannot change it (F11)");
        }
        else if (!variable!.Mut)
        {
            Report(ErrorIds.AssignImmutable, a.Line, $"`{name.Name}` was declared without `mut`: nothing reachable through it changes (D4, M4)");
        }
        else
        {
            changes[a] = new Change(variable, path, a.Line, $"an assignment changes `{path}`"); // M7, in an arm inside an expression
        }

        variable!.Changed = true;
        return true;
    }

    /// <summary>The value an assignment into an index writes, of the type of the entry's value or of the list's items.</summary>
    private void CheckWrittenValue(Assignment a, IndexExpression index, KurzType type)
    {
        model.RecordType(index, new Typed(type));
        var value = CheckExpression(a.Value, type, keepCases: Types.KeepsCases(type));
        if (value.IsError)
        {
            return;
        }

        if (Types.IsVoid(value.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (!Types.Assignable(value.Type, type))
        {
            ReportMismatch(value.Type, type, a.Value.Line, $"{Types.A(value.Type)} where {Types.A(type)} is required");
        }
    }

    /// <summary>R14: <c>Ptr&lt;T&gt;(x)</c> is the pointer to a <c>T</c> at the address a pointer or a <c>long</c> holds, as T10 writes a conversion; it reads nothing, so it needs no <c>raw</c> block.</summary>
    private Typed CheckPtr(CallExpression c)
    {
        var target = Resolve(c.TypeArguments[0]);
        var pointer = PointerTo(target, c.Line, "`Ptr<T>`");
        if (OneValue(c, "`Ptr<T>(x)`") is not { } argument)
        {
            return Typed.Error;
        }

        var source = CheckExpression(argument, IsLiteralOnly(argument) ? Types.Long : null);
        if (source.IsError || Types.IsError(pointer))
        {
            return Types.IsError(pointer) ? Typed.Error : new Typed(pointer);
        }

        if (source.Type is PointerType || IsOffset(source.Type))
        {
            model.RecordCall(c, new CallInfo(CallKind.PointerConversion, null, null, pointer, NoMap, [0], 1, pointer));
            return new Typed(pointer);
        }

        Report(ErrorIds.TypeMismatch, c.Line, $"`Ptr<T>(x)` takes a pointer or a `long`, not {Types.A(source.Type)} (R14, T1)");
        return Typed.Error;
    }

    /// <summary>T10 with R14: the one value a conversion takes, with no name and no <c>mut</c>; null, with the error reported, for another shape.</summary>
    private Expression? OneValue(CallExpression c, string form)
    {
        if (c.Arguments[0].Name is not null)
        {
            Report(ErrorIds.ArgumentMismatch, c.Line, $"{form} takes one value (T10, R14)");
            CheckArgumentsLoosely(c);
            return null;
        }

        if (c.Arguments[0].Mut)
        {
            Report(ErrorIds.MutAtCall, c.Arguments[0].Line, "a conversion changes nothing: no `mut` before its argument (M7)");
            CheckArgumentsLoosely(c);
            return null;
        }

        return c.Arguments[0].Value;
    }

    /// <summary>R14: <c>SizeOf&lt;T&gt;()</c> is the size of a <c>T</c> in bytes, a <c>long</c>, for a type a pointer points to.</summary>
    private Typed CheckSizeOf(CallExpression c)
    {
        var type = Resolve(c.TypeArguments[0]);
        if (Types.IsError(type))
        {
            return Typed.Error;
        }

        if (Types.IsVoid(type))
        {
            Report(ErrorIds.TypeMismatch, c.Line, "`void` has no size: `SizeOf<void>()` names none (R14)");
            return Typed.Error;
        }

        if (Types.IsError(PointerTo(type, c.Line, "`SizeOf<T>()`")))
        {
            return Typed.Error;
        }

        model.RecordCall(c, new CallInfo(CallKind.SizeOf, null, null, type, NoMap, [], 0, Types.Long));
        return new Typed(Types.Long);
    }

    /// <summary>R15: <c>string.FromRaw(p, n)</c> and <c>List&lt;T&gt;.FromRaw(p, n)</c> make a string of the <c>n</c> bytes and a list of the <c>n</c> elements at <c>p</c>, inside a <c>raw</c> block.</summary>
    private Typed CheckFromRaw(CallExpression c, KurzType result, KurzType element, string form)
    {
        if (RefusesTypeArguments(c, $"`{form}`"))
        {
            return Typed.Error;
        }

        if (Types.IsVoid(element))
        {
            Report(ErrorIds.TypeMismatch, c.Line, "`void` has no size: `List<void>.FromRaw` counts no elements (R14, R15)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var pointer = PointerTo(element, c.Line, $"`{form}`");
        if (Types.IsError(pointer))
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        RequireRaw(c.Line, $"`{form}`", "R15");
        var candidate = new Candidate { Slots = [new Slot("p", pointer, false, false, false), new Slot("n", Types.Long, false, false, false)], Result = result };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.FromRaw, null, null, result, NoMap, picked.Binding, 2, result));
        }

        return typed;
    }

    /// <summary>R15: <c>string.Allocate(n)</c>, the address of room for <c>n</c> bytes, in a block that <c>string.FromRaw</c> takes over, inside a <c>raw</c> block.</summary>
    private Typed CheckStringAllocate(CallExpression c)
    {
        if (RefusesTypeArguments(c, "`string.Allocate`"))
        {
            return Typed.Error;
        }

        RequireRaw(c.Line, "`string.Allocate`", "R15");
        var result = new PointerType(Types.Byte);
        var candidate = new Candidate { Slots = [new Slot("n", Types.Long, false, false, false)], Result = result };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.StringAllocate, null, null, result, NoMap, picked.Binding, 1, result));
        }

        return typed;
    }

    /// <summary>
    /// R15: <c>List&lt;T&gt;.Allocate(n)</c>, the address of room for <c>n</c> items in a list's block that <c>List&lt;T&gt;.FromRaw</c> takes
    /// over, a <c>T*</c> of a <c>T</c> a pointer can point to (section 3, the owner, 2026-10-29, "List.Allocate, take over").
    /// </summary>
    private Typed CheckListAllocate(CallExpression c, ListType list, string form)
    {
        if (RefusesTypeArguments(c, $"`{form}`"))
        {
            return Typed.Error;
        }

        if (Types.IsVoid(list.Element))
        {
            Report(ErrorIds.TypeMismatch, c.Line, "`void` has no size: `List<void>.Allocate` makes room for no items (R14, R15)");
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        var pointer = PointerTo(list.Element, c.Line, $"`{form}`");
        if (Types.IsError(pointer))
        {
            CheckArgumentsLoosely(c);
            return Typed.Error;
        }

        RequireRaw(c.Line, $"`{form}`", "R15");
        var candidate = new Candidate { Slots = [new Slot("n", Types.Long, false, false, false)], Result = pointer };
        var typed = Call([candidate], c, null, out var picked);
        if (picked is not null && !typed.IsError)
        {
            model.RecordCall(c, new CallInfo(CallKind.ListAllocate, null, null, list, NoMap, picked.Binding, 1, pointer));
        }

        return typed;
    }

    /// <summary>R15: <c>List&lt;T&gt;.FromRaw(p, n)</c> and <c>List&lt;T&gt;.Allocate(n)</c>; a member of another type with type arguments is one no rule gives it (V10).</summary>
    private Typed CheckTypeMemberCall(TypeMemberAccess t, CallExpression c)
    {
        // `List<void>` is read here, where no type argument is `void` elsewhere (T17), so that `void`'s lack of a size is the error (R14)
        var type = t.Type is NamedType { Name: "List", TypeArguments: [Syntax.VoidType] } ? new ListType(Types.Void) : Resolve(t.Type);
        if (type is ListType list && t.Member == "FromRaw")
        {
            return CheckFromRaw(c, list, list.Element, "List<T>.FromRaw");
        }

        if (type is ListType allocated && t.Member == "Allocate")
        {
            return CheckListAllocate(c, allocated, "List<T>.Allocate");
        }

        if (!Types.IsError(type))
        {
            Report(ErrorIds.UnknownName, t.Line, $"`{type}` has no member `{t.Member}` (V10)");
        }

        CheckArgumentsLoosely(c);
        return Typed.Error;
    }

    // ---- inline assembly (R11, R12) --------------------------------------------------------------

    /// <summary>An operand of an <c>asm</c> the template can name: a register operand with its class, a <c>const</c>, a <c>sym</c> or a <c>label</c>.</summary>
    private sealed class AsmSlot(AsmOperand operand, string? registerClass)
    {
        public AsmOperand Operand { get; } = operand;

        public string? RegisterClass { get; } = registerClass;

        public bool Used { get; set; }
    }

    private static bool IsRegisterWord(string word) => word is "in" or "out" or "lateout" or "inout" or "inlateout";

    private static bool IsDiscard(Expression? e) => e is NameExpression { Name: "_" };

    /// <summary>
    /// R11 with R12: an <c>asm</c> stands inside a <c>raw</c> block or the body of a <c>naked</c> function, and its template and operands follow
    /// Rust's rules for x86-64: Rust's words, the register classes of x86-64 and their types and modifiers, the ABIs of <c>clobber_abi</c> and
    /// the words of <c>options</c>, positional operands before named ones and before those in an explicit register, each name given to one
    /// operand, every operand but one in an explicit register used by the template and every name the template uses an operand's; an operand in
    /// an explicit register names a register of x86-64 Rust takes as an operand, and is no view of a register another input, or another
    /// output, names; a breach is <c>asm-operand</c>. An operand that writes a variable without <c>mut</c> is
    /// <c>assign-immutable</c>. In a <c>naked</c> function an operand other than <c>sym</c> and <c>const</c>, <c>clobber_abi</c> and
    /// <c>options</c> among them, is <c>naked-body</c>. An operand of a class Rust takes only as a clobber, or of one Rust gives a target with
    /// a feature, in a class or an explicit register, is not read: the first needs <c>out</c> with <c>_</c> alone, and no rule names the
    /// features of a build; nor is a name on an operand in an explicit register, which Rust's Reference allows and Rust's compiler refuses.
    /// </summary>
    private void CheckAsm(AsmStatement a, bool naked)
    {
        if (!naked)
        {
            RequireRaw(a.Line, "an `asm`", "R11");
        }

        if (!naked && a.Operands.Select(UnreadOperand).FirstOrDefault(u => u is not null) is { } unread) // in a `naked` function each such operand is `naked-body`
        {
            if (rawDepth > 0)
            {
                NotSupported(unread.Line, unread.Construct); // outside a `raw` block the statement's `raw-required` is its one diagnostic (E1)
            }

            ReadAsmLoosely(a);
            return;
        }

        var positional = new List<AsmSlot>();
        var named = new Dictionary<string, AsmSlot>(StringComparer.Ordinal);
        var options = new Dictionary<string, int>(StringComparer.Ordinal);
        int? clobberLine = null;
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var outputs = new HashSet<string>(StringComparer.Ordinal);
        var explicitSeen = false;
        foreach (var o in a.Operands)
        {
            if (o.Word is "clobber_abi" or "options")
            {
                if (naked)
                {
                    Report(ErrorIds.NakedBody, o.Line, $"the `asm` of a `naked` function takes `sym` and `const` operands alone, not `{o.Word}` (R11, R12)");
                    continue;
                }

                clobberLine ??= o.Word == "clobber_abi" ? o.Line : null;
                CheckAsmDirective(o, options);
                continue;
            }

            if (IsRegisterWord(o.Word) && o.Arguments is [{ Quoted: true } register])
            {
                explicitSeen = true;
                if (naked)
                {
                    Report(ErrorIds.NakedBody, o.Line, $"the `asm` of a `naked` function takes `sym` and `const` operands alone, not `{o.Word}` (R12)");
                    ReadOperandLoosely(o);
                }
                else if (ExplicitRegisterError(o, register.Text, inputs, outputs) is { } error)
                {
                    Report(ErrorIds.AsmOperand, o.Line, error);
                    ReadOperandLoosely(o);
                }
                else
                {
                    CheckAsmOperand(o);
                }

                continue; // the template names it nowhere, so it holds no slot
            }

            var slot = new AsmSlot(o, IsRegisterWord(o.Word) && o.Arguments is [{ Quoted: false } c] && RegisterClasses.ContainsKey(c.Text) ? c.Text : null); // a class x86-64 does not have is the operand's error, and its modifiers are not judged (E1)
            if (o.Name is null)
            {
                if (named.Count > 0 || explicitSeen)
                {
                    Report(ErrorIds.AsmOperand, o.Line, "an operand without a name stands before the named ones and those in an explicit register (R11)");
                }

                positional.Add(slot);
            }
            else if (!named.TryAdd(o.Name, slot))
            {
                Report(ErrorIds.AsmOperand, o.Line, $"the operand `{o.Name}` is named twice (R11)");
            }

            if (naked && o.Word is not ("sym" or "const"))
            {
                Report(ErrorIds.NakedBody, o.Line, $"the `asm` of a `naked` function takes `sym` and `const` operands alone, not `{o.Word}` (R12)");
                ReadOperandLoosely(o);
                continue;
            }

            CheckAsmOperand(o);
        }

        if (options.TryGetValue("pure", out var pure) && !options.ContainsKey("nomem") && !options.ContainsKey("readonly"))
        {
            Report(ErrorIds.AsmOperand, pure, "`options(pure)` takes `nomem` or `readonly` beside it (R11)");
        }
        else if (options.TryGetValue("nomem", out var nomem) && options.TryGetValue("readonly", out var readOnly))
        {
            Report(ErrorIds.AsmOperand, Math.Max(nomem, readOnly), "`options` takes `nomem` or `readonly`, not both (R11)");
        }

        if (!naked)
        {
            CheckAsmOutputs(a, options, clobberLine); // in a `naked` function every output and `label` is `naked-body`
        }

        var implicitIndex = 0;
        foreach (var template in a.Templates)
        {
            CheckTemplate(template, positional, named, ref implicitIndex);
        }

        foreach (var slot in positional.Concat(named.Values).Where(s => !s.Used))
        {
            Report(ErrorIds.AsmOperand, slot.Operand.Line, $"the template does not use the operand {(slot.Operand.Name is null ? $"at position {positional.IndexOf(slot)}" : $"`{slot.Operand.Name}`")} (R11)");
        }
    }

    /// <summary>R11: what of a register operand the checker does not read, with its line, or null.</summary>
    private static (int Line, string Construct)? UnreadOperand(AsmOperand o) => !IsRegisterWord(o.Word) ? null : o.Arguments switch
    {
        [{ Quoted: true }] when o.Name is not null => (o.Line, "a name on an operand in an explicit register, which Rust's Reference allows and Rust's compiler refuses (R11)"),
        [{ Quoted: true } r] when ExplicitRegisters.TryGetValue(r.Text, out var register) => UnreadClass(o.Line, register.Class, $"the register `{r.Text}`, of the class `{register.Class}`"),
        [{ Quoted: false } c] => UnreadClass(o.Line, c.Text, $"the class `{c.Text}`"),
        _ => null,
    };

    private static (int Line, string Construct)? UnreadClass(int line, string registerClass, string what) =>
        ClobberOnlyClasses.Contains(registerClass) ? (line, $"{what}, which Rust takes only as a clobber (R11)")
        : FeatureClasses.TryGetValue(registerClass, out var feature) ? (line, $"{what}, which Rust gives a target with the feature `{feature}`, a feature no rule of a build names (R11)")
        : null;

    /// <summary>
    /// R11: what an operand in an explicit register breaks of Rust's rules for x86-64, or null: a register x86-64 does not have; one Rust
    /// takes as no operand; a view of a register another input, or another output, names. An <c>in</c> is an input, a <c>lateout</c> an
    /// output, an <c>out</c>, an <c>inout</c> and an <c>inlateout</c> both, as Rust's compiler counts them: an <c>out</c> may write its
    /// register before every input is read, and a <c>lateout</c> is the <c>out</c> whose register the allocator can share with an <c>in</c>.
    /// </summary>
    private static string? ExplicitRegisterError(AsmOperand o, string name, Dictionary<string, string> inputs, HashSet<string> outputs)
    {
        if (!ExplicitRegisters.TryGetValue(name, out var register))
        {
            return $"`{name}` is no register of x86-64, the target the build compiles for: name one of its registers, `rax` or `xmm0`, or a register class (R11)";
        }

        if (ReservedRegisters.TryGetValue(register.Base, out var reason))
        {
            return $"`{name}` is {reason}, and Rust takes it as no operand: name another register (R11)";
        }

        var input = o.Word != "lateout" && !inputs.TryAdd(register.Base, o.Word);
        var output = o.Word != "in" && !outputs.Add(register.Base);
        if (output)
        {
            return $"`{name}` is `{register.Base}`, which another output of the `asm` names: give one of them another register (R11)";
        }

        if (!input)
        {
            return null;
        }

        return o.Word == "out" || inputs[register.Base] == "out"
            ? $"`{name}` is `{register.Base}`, which an input of the `asm` names, and an `out` may write its register before every input is read: write `lateout`, or give one of them another register (R11)"
            : $"`{name}` is `{register.Base}`, which another input of the `asm` names: give one of them another register (R11)";
    }

    /// <summary>
    /// R11: Rust's rules of the outputs, for x86-64: <c>pure</c> takes an output that is not <c>_</c>, <c>noreturn</c> no output where no
    /// <c>label</c> stands, a <c>label</c> no output, and <c>clobber_abi</c> outputs in explicit registers alone, so none of a register
    /// class; each breach is <c>asm-operand</c> at the word or the operand the rule names.
    /// </summary>
    private void CheckAsmOutputs(AsmStatement a, Dictionary<string, int> options, int? clobberLine)
    {
        var outputs = a.Operands.Where(o => IsRegisterWord(o.Word) && o.Word != "in").ToList();
        var label = a.Operands.FirstOrDefault(o => o.Word == "label");
        var written = outputs.Any(o => o.Word is "inout" or "inlateout" ? o.Output is null || !IsDiscard(o.Output) : !IsDiscard(o.Value));
        if (options.TryGetValue("pure", out var pure) && !written)
        {
            Report(ErrorIds.AsmOperand, pure, "`options(pure)` takes an output that is not `_`, the result of the `asm` (R11)");
        }

        if (options.TryGetValue("noreturn", out var noreturn) && outputs.Count > 0 && label is null)
        {
            Report(ErrorIds.AsmOperand, noreturn, "`options(noreturn)` takes no output: the `asm` does not return to write one (R11)");
        }

        if (label is not null && outputs.Count > 0)
        {
            Report(ErrorIds.AsmOperand, label.Line, "a `label` operand stands in an `asm` without outputs (R11)");
        }

        if (clobberLine is { } line && outputs.Any(o => o.Arguments is not [{ Quoted: true }]))
        {
            Report(ErrorIds.AsmOperand, line, "`clobber_abi` takes outputs in explicit registers alone, and an output of a register class is none (R11)");
        }
    }

    /// <summary>R11: <c>clobber_abi("C")</c> names ABIs Rust names for x86-64, <c>options(nostack)</c> Rust's options; neither takes a name or a value.</summary>
    private void CheckAsmDirective(AsmOperand o, Dictionary<string, int> options)
    {
        if (o.Name is not null || o.Arguments is not { Count: > 0 } arguments || o.Value is not null)
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes no name and no value, and one or more words in its parentheses (R11)");
            ReadOperandLoosely(o);
            return;
        }

        foreach (var argument in arguments)
        {
            if (o.Word == "clobber_abi" ? !argument.Quoted || !ClobberAbis.Contains(argument.Text) : argument.Quoted || !AsmOptions.Contains(argument.Text))
            {
                Report(ErrorIds.AsmOperand, argument.Line, o.Word == "clobber_abi"
                    ? $"`clobber_abi` takes the ABIs of x86-64 as strings, \"C\", \"system\", \"efiapi\", \"win64\" or \"sysv64\", not `{argument.Text}` (R11)"
                    : $"`{argument.Text}` is no option of an `asm` (R11)");
                continue;
            }

            if (o.Word == "options")
            {
                options.TryAdd(argument.Text, argument.Line);
            }
        }
    }

    /// <summary>R11: one operand of an <c>asm</c> by its word.</summary>
    private void CheckAsmOperand(AsmOperand o)
    {
        switch (o.Word)
        {
            case "const" or "sym" when o.Value is null || o.Output is not null: // a block after the word, or `=>`
                Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes one value after it, `{(o.Word == "const" ? "c = const 4" : "f = sym F")}` (R11)");
                ReadOperandLoosely(o);
                return;
            case "const":
                {
                    var t = CheckExpression(o.Value!, null);
                    if (!t.IsError && t.Constant is not IntegerConstant)
                    {
                        Report(ErrorIds.AsmOperand, o.Line, "`const` takes an integer constant (R11)");
                    }

                    return;
                }

            case "sym":
                CheckSym(o);
                return;
            case "label":
                {
                    // Rust: the block of a `label` operand is a new safety context, so that what reads or writes raw memory in it stands in a
                    // `raw` block of its own
                    var saved = rawDepth;
                    rawDepth = 0;
                    if (o.Body is null || o.Value is not null || o.Arguments is not null)
                    {
                        Report(ErrorIds.AsmOperand, o.Line, "`label` takes a block, `label { ... }` (R11)");
                        ReadOperandLoosely(o);
                    }
                    else
                    {
                        CheckBlock(o.Body);
                    }

                    rawDepth = saved;
                    return;
                }

            default:
                break;
        }

        if (!IsRegisterWord(o.Word))
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` is no word of an `asm` operand (R11)");
            ReadOperandLoosely(o);
            return;
        }

        string? registerClass = null;
        if (o.Arguments is [{ Quoted: true } register] && ExplicitRegisters.TryGetValue(register.Text, out var explicitRegister))
        {
            registerClass = explicitRegister.Class; // CheckAsm judged the register, and an unread class does not come here
        }
        else if (o.Arguments is not [{ Quoted: false } argument])
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes one register class in its parentheses, `{o.Word}(reg)` (R11)");
        }
        else if (!RegisterClasses.ContainsKey(argument.Text))
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{argument.Text}` is no register class of x86-64, the target the build compiles for (R11)");
        }
        else
        {
            registerClass = argument.Text;
        }

        if (o.Value is null)
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes a value after its parentheses (R11)");
            return;
        }

        var reads = o.Word is "in" or "inout" or "inlateout";
        var writes = o.Word != "in";
        KurzType? inType = null;
        KurzType? outType = null;
        if (o.Output is not null && o.Word is "in" or "out" or "lateout")
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes one value, and `=>` splits an `inout` or an `inlateout` alone (R11)");
            ReadOperandLoosely(o);
            return;
        }

        if (reads && IsDiscard(o.Value))
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`_` puts no value into a register, and `{o.Word}` reads one (R11)");
            ReadOperandLoosely(o);
            return;
        }

        if (o.Output is not null)
        {
            inType = CheckAsmValue(o.Value);
            outType = IsDiscard(o.Output) ? null : AsmPlace(o.Output, read: false);
        }
        else if (!writes)
        {
            inType = CheckAsmValue(o.Value);
        }
        else if (!IsDiscard(o.Value))
        {
            outType = AsmPlace(o.Value, read: reads);
            inType = reads ? outType : null;
        }

        if (registerClass is null)
        {
            return;
        }

        var takes = RegisterClasses[registerClass].Takes;
        foreach (var type in new[] { inType, outType }.Distinct())
        {
            if (type is not null && !Types.IsError(type) && !takes(type))
            {
                Report(ErrorIds.AsmOperand, o.Line, $"{Types.A(type)} does not go into a register of the class `{registerClass}` (R11)");
                return;
            }
        }

        if (inType is not null && outType is not null && !Types.IsError(inType) && !Types.IsError(outType) && !Types.Same(inType, outType) && !(SameWidth(inType, outType)))
        {
            Report(ErrorIds.AsmOperand, o.Line, $"`{o.Word}` takes {Types.A(inType)} in and {Types.A(outType)} out, and two types of one register are one type, or integers or pointers of one width (R11)");
        }
    }

    /// <summary>Rust's rule for the two values of an <c>inout</c>: integers or pointers of one width, a pointer being 64 bits on x86-64.</summary>
    private static bool SameWidth(KurzType a, KurzType b)
    {
        static int Width(KurzType t) => t is PointerType or CFunctionType ? 64 : Types.IsInteger(t) ? Types.Width(t) : -1;
        return Width(a) > 0 && Width(a) == Width(b);
    }

    private KurzType? CheckAsmValue(Expression value)
    {
        var t = CheckExpression(value, null);
        if (Types.IsVoid(t.Type))
        {
            Report(ErrorIds.Syntax, value.Line, "a call with a `void` success used as a value (O10)");
            return Types.Error;
        }

        return t.Type;
    }

    /// <summary>R11: a place an operand writes, a variable that is <c>mut</c> as an assignment's (V7), a field of one (M4), or what a pointer points to; another value is <c>asm-operand</c>.</summary>
    private KurzType? AsmPlace(Expression place, bool read)
    {
        var members = new List<MemberAccess>();
        var root = Unwrap(place);
        while (root is MemberAccess { Conditional: false } m)
        {
            members.Insert(0, m);
            root = Unwrap(m.Target);
        }

        switch (root)
        {
            case NameExpression n when Lookup(n.Name).Variable is { } variable:
                {
                    var captured = Lookup(n.Name).Captured;
                    if (Types.IsError(variable.Type))
                    {
                        variable.Read |= read;
                        return Types.Error;
                    }

                    if (captured)
                    {
                        Report(ErrorIds.CaptureAssign, place.Line, $"a lambda reads `{n.Name}` and cannot assign it (F11)");
                    }
                    else if (!variable.Mut)
                    {
                        Report(ErrorIds.AssignImmutable, place.Line, $"`{n.Name}` was declared without `mut`, and the `asm` writes it (R11, V7)");
                    }

                    if (members.Count > 0 && !IsFieldPath(variable.Type, members, out var notField))
                    {
                        Report(ErrorIds.AsmOperand, place.Line, $"`{notField}` is no field of a data value: an operand writes a variable, a field or what a pointer points to (R11)");
                        return Types.Error;
                    }

                    variable.Read |= read || members.Count > 0; // V6: a write into a path reads the variable, as an assignment's does
                    variable.Changed = true;
                    model.RecordName(n, variable);
                    var type = variable.Type;
                    RecordThrough(members.Count == 0 ? place : members[0].Target, new Typed(type));
                    foreach (var m in members.TakeWhile(_ => !Types.IsError(type)))
                    {
                        var data = (DataType)type;
                        type = SubstituteData(data, data.Symbol.Field(m.Member)!.Type);
                        model.RecordType(m, new Typed(type));
                    }

                    RecordThrough(place, new Typed(type));
                    return type;
                }

            case NameExpression n:
                CheckName(n); // reports what the name is
                return Types.Error;
            case UnaryExpression { Operator: TokenKind.Star }:
            case IndexExpression:
                {
                    var t = CheckExpression(place, null);
                    return t.IsError ? Types.Error : t.Type;
                }

            default:
                Report(ErrorIds.AsmOperand, place.Line, "an operand writes a variable, a field or what a pointer points to, or `_` (R11)");
                CheckExpression(place, null);
                return Types.Error;
        }
    }

    /// <summary>R11: <c>sym</c> takes a function or a static, as Rust's does; a function with more than one overload or with type parameters is not read, since a symbol is one function.</summary>
    private void CheckSym(AsmOperand o)
    {
        if (Unwrap(o.Value!) is not NameExpression n)
        {
            Report(ErrorIds.AsmOperand, o.Line, "`sym` takes the name of a function or of state outside a function (R11)");
            CheckExpression(o.Value!, null);
            return;
        }

        var (variable, _) = Lookup(n.Name);
        if (variable is { Kind: VariableKind.Static })
        {
            model.RecordName(n, variable);
            return;
        }

        if (variable is not null)
        {
            variable.Read = true;
            Report(ErrorIds.AsmOperand, o.Line, $"`sym` takes a function or state outside a function, and `{n.Name}` is a variable (R11)");
            return;
        }

        if (cFunctions.TryGetValue(n.Name, out var cFunction))
        {
            model.RecordFunctionName(n, cFunction);
            return;
        }

        if (IsUnreadName(n.Name))
        {
            return;
        }

        if (functions.TryGetValue(n.Name, out var list))
        {
            if (list.Count > 1 || list[0].IsGeneric)
            {
                NotSupported(o.Line, "`sym` of a function with more than one overload or with type parameters (R11)");
            }
            else
            {
                model.RecordFunctionName(n, list[0]);
            }

            return;
        }

        CheckName(n); // reports what the name is: a type, a top-level variable or no name at all
    }

    /// <summary>
    /// R11: the placeholders of a template, as Rust reads them: <c>{{</c> and <c>}}</c> write a brace, <c>{}</c> takes the operands without
    /// a name in their order, <c>{0}</c> one by its position, <c>{name}</c> one by its name, and a modifier after a colon, <c>{r:e}</c>, gives the form of a
    /// register operand's register, from the modifiers of its class.
    /// </summary>
    private void CheckTemplate(AsmTemplate template, List<AsmSlot> positional, Dictionary<string, AsmSlot> named, ref int implicitIndex)
    {
        var text = template.Text;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    i++;
                    continue;
                }

                Unreadable(template, "a `}` in the template closes a `{`, and a brace is written `{{` or `}}` (R11)", positional, named);
                return;
            }

            if (text[i] != '{')
            {
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '{')
            {
                i++;
                continue;
            }

            var end = text.IndexOf('}', i + 1);
            if (end < 0)
            {
                Unreadable(template, "a `{` in the template is closed by a `}`, and a brace is written `{{` or `}}` (R11)", positional, named);
                return;
            }

            var inside = text[(i + 1)..end];
            i = end;
            var colon = inside.IndexOf(':', StringComparison.Ordinal);
            var reference = colon < 0 ? inside : inside[..colon];
            var modifier = colon < 0 ? null : inside[(colon + 1)..];
            AsmSlot? slot;
            if (reference.Length == 0)
            {
                slot = implicitIndex < positional.Count ? positional[implicitIndex] : null;
                implicitIndex++;
            }
            else if (reference.All(char.IsAsciiDigit))
            {
                slot = int.TryParse(reference, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var position) && position < positional.Count ? positional[position] : null;
            }
            else if (IsAsmName(reference))
            {
                slot = named.GetValueOrDefault(reference);
            }
            else
            {
                Unreadable(template, $"`{{{inside}}}` names an operand by its position or its name (R11)", positional, named);
                return;
            }

            if (slot is null)
            {
                Report(ErrorIds.AsmOperand, template.Line, reference.Length > 0 && !reference.All(char.IsAsciiDigit)
                    ? $"no operand is named `{reference}` (R11)"
                    : $"the template names an operand at a position the `asm` has none at, `{{{inside}}}` (R11)");
                continue;
            }

            slot.Used = true;
            if (modifier is null)
            {
                continue;
            }

            if (slot.RegisterClass is { } registerClass)
            {
                if (modifier.Length != 1 || !RegisterClasses[registerClass].Modifiers.Contains(modifier[0], StringComparison.Ordinal))
                {
                    Report(ErrorIds.AsmOperand, template.Line, $"`{modifier}` is no modifier of the class `{registerClass}` (R11)");
                }
            }
            else if (slot.Operand.Word is "const" or "sym" or "label")
            {
                Report(ErrorIds.AsmOperand, template.Line, $"a modifier stands after a register operand, not after `{slot.Operand.Word}` (R11)");
            }
        }
    }

    /// <summary>R11: a template whose braces or placeholders cannot be read is one <c>asm-operand</c>, and what it would name of the operands is not known, so each counts as used (E1).</summary>
    private void Unreadable(AsmTemplate template, string message, List<AsmSlot> positional, Dictionary<string, AsmSlot> named)
    {
        Report(ErrorIds.AsmOperand, template.Line, message);
        foreach (var slot in positional.Concat(named.Values))
        {
            slot.Used = true;
        }
    }

    private static bool IsAsmName(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    /// <summary>The values, places and block of one operand, read with their diagnostics dropped, so that the variables they use count as used (E1).</summary>
    private void ReadOperandLoosely(AsmOperand operand)
    {
        Push();
        foreach (var e in new[] { operand.Value, operand.Output })
        {
            if (e is not null && !IsDiscard(e))
            {
                CheckExpression(e, null);
            }
        }

        if (operand.Body is not null)
        {
            CheckBlock(operand.Body);
        }

        Pop(commit: false);
    }

    /// <summary>
    /// R12: the body of a <c>naked</c> function is one <c>asm</c> statement, which counts as a <c>raw</c> block; another body is
    /// <c>naked-body</c>, at the first statement that is not that <c>asm</c>, or at the declaration where the body holds none. The
    /// instructions return or never end, so no <c>missing-return</c> is judged.
    /// </summary>
    private void CheckNakedBody(FunctionSymbol f)
    {
        var d = f.Declaration!;
        frame = new Frame { ReturnType = f.ReturnType, Line = d.Line, TypeParameters = f.TypeParameters, Reserved = reservedTopLevel };
        foreach (var (p, node) in f.Parameters.Zip(d.Parameters))
        {
            DeclareParameter(p.Name, p.Type, p.Mut, p.Line, node);
        }

        rawDepth++;
        if (d.BlockBody is { Statements: [AsmStatement asm] })
        {
            CheckAsm(asm, naked: true);
        }
        else
        {
            var statements = d.BlockBody?.Statements ?? [];
            var line = statements.Count == 0 ? d.Line : statements.Skip(statements[0] is AsmStatement ? 1 : 0).First().Line;
            Report(ErrorIds.NakedBody, line, "the body of a `naked` function is one `asm` statement (R12)");
            Push();
            if (d.ExpressionBody is not null)
            {
                CheckExpression(d.ExpressionBody, null);
            }
            else if (d.BlockBody is not null)
            {
                CheckBlock(d.BlockBody);
            }

            Pop(commit: false);
        }

        rawDepth--;
        ReportUnused(frame.Scope);
    }

    // ---- state outside a function (R13) --------------------------------------------------------

    private static bool IsState(VariableDeclaration v) => (v.Modifiers & (Modifiers.Static | Modifiers.Mut)) == (Modifiers.Static | Modifiers.Mut);

    /// <summary>R13 with V9 and F6: the name of state outside a function, declared before any function is checked, which every function reaches; a name declared already is <c>redeclared</c>.</summary>
    private void DeclareState(VariableDeclaration v)
    {
        var clash = IsDeclaredName(v.Name) || stateNames.Contains(v.Name);
        if (clash)
        {
            Report(ErrorIds.Redeclared, v.Line, $"the name `{v.Name}` is declared already (V9)");
        }
        else
        {
            stateNames.Add(v.Name);
        }

        stateDeclarations.Add((v, !clash));
    }

    /// <summary>
    /// R13 with F6: each declaration of state outside a function, after every type is declared, before any function's body is checked, with
    /// the name of all of it visible to each initial value. A declaration whose name is <c>redeclared</c> is read with its diagnostics
    /// dropped, and its name stands for state of the error type where no other state has it, so that its uses carry no error (E1).
    /// </summary>
    private void CheckStates()
    {
        foreach (var (v, named) in stateDeclarations)
        {
            if (named || !stateNames.Contains(v.Name))
            {
                statics[v.Name] = new VariableSymbol(v.Name, Types.Error, true, VariableKind.Static, v.Line);
            }
        }

        foreach (var (v, named) in stateDeclarations)
        {
            frame = new Frame { IsTopLevel = true, ReturnType = Types.Void, Line = v.Line };
            if (!named)
            {
                Push();
                CheckState(v);
                Pop(commit: false);
                continue;
            }

            var state = new VariableSymbol(v.Name, CheckState(v), true, VariableKind.Static, v.Line);
            statics[v.Name] = state;
            model.RecordDeclaration(v, state);
        }
    }

    /// <summary>
    /// R13: a declaration of state is <c>static-state</c> in a package without <c>allow raw</c>, of a type that holds a counted reference,
    /// without an initial value or with one that is no constant as F12 counts one, and with <c>pub</c> before it, judged in the order R13
    /// lists them, one error a declaration; an initial value that reads state is no constant, and that error is the value's one (E1). Its
    /// type is the error type after such an error, so that its uses carry none (E1).
    /// </summary>
    private KurzType CheckState(VariableDeclaration v)
    {
        var what = (v.Modifiers & Modifiers.Thread) != 0 ? "`thread static mut`" : "`static mut`";
        Expression[] values = v.Value is null ? [] : [v.Value]; // read with its diagnostics dropped after an error, so that the names it uses count as used (E1)
        var other = v.Modifiers & ~(Modifiers.Static | Modifiers.Mut | Modifiers.Thread | Modifiers.Pub);
        if (other != Modifiers.None)
        {
            Report(ErrorIds.Syntax, v.Line, $"`{other.ToString().ToLowerInvariant()}` has no meaning on state outside a function (R13, E4)");
            CheckLoosely(values);
            return Types.Error;
        }

        if (!allowRaw)
        {
            Report(ErrorIds.StaticState, v.Line, $"{what} at the top level of a file needs the grant `allow raw`, which this package does not have (R13)");
            CheckLoosely(values);
            return Types.Error;
        }

        var declared = v.Type is null ? null : Resolve(v.Type);
        if (declared is not null && Types.IsVoid(declared))
        {
            Report(ErrorIds.Syntax, v.Line, "`void` is no type of a variable (F4)");
            CheckLoosely(values);
            return Types.Error;
        }

        if (declared is not null && !Types.IsError(declared) && Holds(declared, []) != Holding.Nothing)
        {
            Report(ErrorIds.StaticState, v.Line, $"{what} holds what a pointer points to, and {Types.A(declared)} holds a counted reference (R13, R10)");
            CheckLoosely(values);
            return Types.Error;
        }

        if (v.Value is null)
        {
            if (declared is not null && !Types.IsError(declared))
            {
                Report(ErrorIds.StaticState, v.Line, $"{what} takes an initial value (R13)");
            }

            return Types.Error;
        }

        var reading = readingConstant;
        readingConstant = true;
        stateRead = false;
        Push();
        var value = CheckExpression(v.Value, declared, keepCases: Types.KeepsCases(declared));
        var captured = Pop(commit: false);
        readingConstant = reading;
        if (stateRead)
        {
            Report(ErrorIds.StaticState, v.Line, $"the initial value of {what} is a constant expression, as F12 counts one for a default value, and a read of state is none (R13)");
            return Types.Error;
        }

        foreach (var d in captured)
        {
            Add(d);
        }

        var type = declared ?? value.Type;
        if (Types.IsError(type))
        {
            return Types.Error;
        }

        if (Types.IsVoid(type))
        {
            Report(ErrorIds.Syntax, v.Line, "a call with a `void` success used as a value (O10)");
            return Types.Error;
        }

        if (Holds(type, []) != Holding.Nothing)
        {
            Report(ErrorIds.StaticState, v.Line, $"{what} holds what a pointer points to, and {Types.A(type)} holds a counted reference (R13, R10)");
            return Types.Error;
        }

        if (value.IsError)
        {
            return type;
        }

        if (!Types.Assignable(value.Type, type))
        {
            ReportMismatch(value.Type, type, v.Value.Line, $"{Types.A(value.Type)} where {Types.A(type)} is required");
            return type;
        }

        if (value.Constant is null)
        {
            Report(ErrorIds.StaticState, v.Line, $"the initial value of {what} is a constant expression, as F12 counts one for a default value (R13)");
            return Types.Error;
        }

        if ((v.Modifiers & Modifiers.Pub) != 0)
        {
            Report(ErrorIds.StaticState, v.Line, $"no `pub` stands before {what}: it is its package's alone, and another package calls a function of the package (R13)");
            return Types.Error;
        }

        return type;
    }

    /// <summary>R13: a read or a write of state outside a function stands inside a <c>raw</c> block; state whose declaration was an error carries none (E1).</summary>
    private void RequireRawForState(VariableSymbol variable, int line)
    {
        if (variable.Kind == VariableKind.Static && !Types.IsError(variable.Type))
        {
            RequireRaw(line, $"a read or a write of the state `{variable.Name}`", "R13");
        }
    }

    // ---- the functions of C (R12, R16, R17) ------------------------------------------------------

    /// <summary>Section 2 with R16 and R17: whether a type crosses to C.</summary>
    private static Crossing Crosses(KurzType type) => type switch
    {
        PrimitiveType { Kind: Primitive.Decimal } => Crossing.Unknown,
        PrimitiveType p when Types.IsNumber(p) || p.Kind == Primitive.Bool => Crossing.Yes,
        PointerType or CFunctionType or ErrorType => Crossing.Yes,
        UnionType u when Types.NonNull(u) is CFunctionType => Crossing.Yes, // R17: its `null` is the address 0, as Rust's `Option<extern "C" fn()>`
        TypeParameterType => Crossing.Unknown,
        _ => Crossing.No,
    };

    /// <summary>
    /// R16 with F6 and V9: the names of a block of C functions, declared in source order with the functions and types of the file; a C
    /// declaration has one name, so a name another function, a type, another C declaration or state has is <c>redeclared</c> at the second.
    /// </summary>
    private void DeclareCFunctions(ExternBlock e)
    {
        foreach (var f in e.Functions)
        {
            var symbol = new FunctionSymbol(f.Name, null) { CDeclaration = f };
            model.RecordCFunction(f, symbol);
            cDeclared.Add(symbol);
            if (IsDeclaredName(f.Name) || stateNames.Contains(f.Name))
            {
                Report(ErrorIds.Redeclared, f.Line, $"the name `{f.Name}` is declared already, and a C declaration has one name (V9, R16)");
                continue;
            }

            cFunctions[f.Name] = symbol;
        }
    }

    /// <summary>
    /// R16: the types of each C declaration, after every type is declared. A modifier but <c>pub</c> has no meaning on one (E4); a <c>mut</c>
    /// parameter and a type that cannot cross to C are <c>native-type</c>, at the parameter, or at the declaration for its result; a
    /// <c>decimal</c> is not read, being a number of Kurz that no rule says crosses to C, and neither is a call of such a declaration.
    /// </summary>
    private void ResolveCFunctions()
    {
        foreach (var symbol in cDeclared)
        {
            var f = symbol.CDeclaration!;
            var other = f.Modifiers & ~Modifiers.Pub;
            if (other != Modifiers.None)
            {
                Report(ErrorIds.Syntax, f.Line, $"`{other.ToString().ToLowerInvariant()}` has no meaning on a C declaration (R16, E4)");
            }

            var unread = false;
            symbol.ReturnType = Resolve(f.ReturnType);
            if (Crosses(symbol.ReturnType) == Crossing.No && !Types.IsVoid(symbol.ReturnType))
            {
                Report(ErrorIds.NativeType, f.Line, $"{Types.A(symbol.ReturnType)} cannot cross to C (R16)");
            }

            unread |= Crosses(symbol.ReturnType) == Crossing.Unknown;
            foreach (var p in f.Parameters)
            {
                var type = p.Type is null ? Types.Error : Resolve(p.Type);
                if (Types.IsVoid(type))
                {
                    Report(ErrorIds.Syntax, p.Line, "`void` is no type of a parameter (F4)");
                    type = Types.Error;
                }

                if (symbol.Parameters.Any(q => q.Name == p.Name))
                {
                    Report(ErrorIds.Redeclared, p.Line, $"the parameter `{p.Name}` is declared already (V9)");
                }

                if (p.Mut)
                {
                    Report(ErrorIds.NativeType, p.Line, $"a C declaration takes no `mut` parameter, and `{p.Name}` is one (R16)");
                }
                else if (Crosses(type) == Crossing.No)
                {
                    Report(ErrorIds.NativeType, p.Line, $"{Types.A(type)} cannot cross to C (R16)");
                }

                unread |= Crosses(type) == Crossing.Unknown;
                symbol.Parameters.Add(new ParameterSymbol(p.Name, type, false, p.Default is not null, p.Line)); // a `mut` is the declaration's error, and a call need not write `mut` for it (E1)
            }

            if (unread)
            {
                NotSupported(f.Line, "a `decimal` in a C declaration, which no rule says crosses to C (R16)");
                modifiedFunctions.Add(symbol); // its defaults are read with their diagnostics dropped (E1)
                if (cFunctions.Remove(f.Name))
                {
                    unreadFunctions.Add(f.Name);
                }
            }
        }
    }

    /// <summary>
    /// R12, R16: a function of the package's own in the C calling convention needs the grant <c>allow native</c>, and a <c>naked</c> one
    /// <c>allow raw</c>, each error at the declaration. What R12 and R16 leave open is not read, the function and its calls with it: type
    /// parameters, another function of its name, a <c>mut</c> parameter and a type that cannot cross to C, for which no rule names an id.
    /// </summary>
    private void CheckCSignature(FunctionSymbol symbol, FunctionDeclaration d)
    {
        var refused = false;
        if ((d.Modifiers & Modifiers.Naked) != 0 && !allowRaw)
        {
            Report(ErrorIds.NakedNotAllowed, d.Line, "a `naked` function needs the grant `allow raw`, which this package does not have (R12)");
            refused = true;
        }

        if ((d.Modifiers & Modifiers.Extern) != 0 && !allowNative)
        {
            Report(ErrorIds.NativeNotAllowed, d.Line, "a function of the C calling convention needs the grant `allow native`, which this package does not have (R16)");
            refused = true;
        }

        var open = symbol.IsGeneric ? "a function of the C calling convention with type parameters (R12, R16, T19)"
            : functions.TryGetValue(d.Name, out var list) && list.Count > 1 ? "a function of the C calling convention beside another function of its name (R12, R16, F12)"
            : symbol.Parameters.Any(p => p.Mut) ? "a `mut` parameter of a function of the C calling convention (R12, R16)"
            : symbol.Parameters.Select(p => p.Type).Append(symbol.ReturnType).Any(t => Crosses(t) != Crossing.Yes && !Types.IsVoid(t)) ? "a type that cannot cross to C in a function of the C calling convention (R12, R16)"
            : null;
        if (open is not null)
        {
            if (!refused)
            {
                NotSupported(d.Line, open); // after a grant's error, the declaration's one, nothing more (E1)
            }

            modifiedFunctions.Add(symbol); // not read: its defaults and body are read with their diagnostics dropped, and its calls are not read (E1)
        }
    }

    /// <summary>R17: the name of a function of the C calling convention is a value of its C function type, which has no <c>...</c>.</summary>
    private Typed CFunctionValue(FunctionSymbol f, int line)
    {
        if (f.CDeclaration is { Variadic: true })
        {
            NotSupported(line, "the value of a C function that takes `...`, which the type of a C function does not write (R16, R17)");
            return Typed.Error;
        }

        var parameters = f.Parameters.Select(p => p.Type).ToList();
        return parameters.Any(Types.IsError) || Types.IsError(f.ReturnType) ? Typed.Error : new Typed(new CFunctionType(parameters, f.ReturnType));
    }

    /// <summary>
    /// R16: a call of a C declaration stands inside a <c>raw</c> block, and fills in its defaults and named arguments as any call does
    /// (F12, D11); the arguments without a name after its parameters stand in the place of its <c>...</c>, where a <c>bool</c>, a
    /// <c>float</c>, a number smaller than an <c>int</c> and a type that cannot cross to C are <c>native-type</c>.
    /// </summary>
    private Typed CallC(FunctionSymbol f, CallExpression c, KurzType? expected)
    {
        RequireRaw(c.Line, $"a call of the C function `{f.Name}`", "R16");
        if (RefusesTypeArguments(c, $"the C function `{f.Name}`"))
        {
            return Typed.Error;
        }

        var arguments = c.Arguments.ToList();
        var variadic = new List<Argument>();
        var positional = arguments.TakeWhile(a => a.Name is null).Count();
        if (f.CDeclaration!.Variadic && positional > f.Parameters.Count)
        {
            variadic = arguments.Skip(f.Parameters.Count).Take(positional - f.Parameters.Count).ToList();
            arguments = arguments.Take(f.Parameters.Count).Concat(arguments.Skip(positional)).ToList();
        }

        // a `mut` parameter is the declaration's `native-type`, and a call that writes `mut` for it, as the declaration reads, carries no error
        // of its own (E1)
        var declared = f.CDeclaration.Parameters;
        arguments = arguments.Select((a, i) => a.Mut && (a.Name is null ? i < declared.Count && declared[i].Mut : declared.Any(p => p.Name == a.Name && p.Mut)) ? a with { Mut = false } : a).ToList();
        var slots = f.Parameters.Select(p => new Slot(p.Name, p.Type, false, p.HasDefault, false)).ToList();
        var result = Call([new Candidate { Function = f, Slots = slots, Result = f.ReturnType }], c with { Arguments = arguments }, expected, out var picked);
        foreach (var a in variadic)
        {
            CheckVariadicArgument(a);
        }

        if (picked is not null)
        {
            // the binding of the call as written: the arguments in the place of `...` stand between the positional ones and the named ones
            var first = f.Parameters.Count;
            var binding = c.Arguments.Select((_, i) => i < first || i >= first + variadic.Count ? picked.Binding[i < first ? i : i - variadic.Count] : -1).ToList();
            Record(c, CallKind.CFunction, result, picked, binding);
        }

        return result;
    }

    private void CheckVariadicArgument(Argument a)
    {
        if (a.Mut)
        {
            Report(ErrorIds.MutAtCall, a.Line, "an argument in the place of `...` takes no `mut` (M7, R16)");
        }

        var t = CheckExpression(a.Value, null);
        if (t.IsError)
        {
            return;
        }

        if (Types.IsVoid(t.Type))
        {
            Report(ErrorIds.Syntax, a.Line, "a call with a `void` success used as a value (O10)");
        }
        else if (t.Type is PrimitiveType { Kind: Primitive.Bool or Primitive.Float } || Types.IsNarrow(t.Type))
        {
            Report(ErrorIds.NativeType, a.Line, $"an argument in the place of `...` is no `bool`, `float` or number smaller than an `int`, which C would widen: convert the `{t.Type}` (R16)");
        }
        else if (Crosses(t.Type) == Crossing.No)
        {
            Report(ErrorIds.NativeType, a.Line, $"{Types.A(t.Type)} cannot cross to C (R16)");
        }
        else if (Crosses(t.Type) == Crossing.Unknown)
        {
            NotSupported(a.Line, "a `decimal` in the place of `...`, which no rule says crosses to C (R16)");
        }
    }

    /// <summary>R17: the type of a C function, whose parameters and result cross to C; R17 names no id for one that cannot, so such a type is not read.</summary>
    private KurzType ResolveCFunctionType(ExternFunctionType f)
    {
        var parameters = f.Parameters.Select(Resolve).ToList();
        var result = Resolve(f.Result);
        if (parameters.Any(Types.IsError) || Types.IsError(result))
        {
            return Types.Error;
        }

        if (parameters.Any(Types.IsVoid))
        {
            Report(ErrorIds.Syntax, f.Line, "`void` is no type of a parameter (F4)");
            return Types.Error;
        }

        if (parameters.Append(result).Any(t => Crosses(t) != Crossing.Yes && !Types.IsVoid(t)))
        {
            NotSupported(f.Line, "a type that cannot cross to C in the type of a C function (R17)");
            return Types.Error;
        }

        return new CFunctionType(parameters, result);
    }
}
