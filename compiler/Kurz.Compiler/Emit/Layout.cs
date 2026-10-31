using System.Globalization;
using Kurz.Compiler.Semantics;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The layout of the types of one program, which the whole program decides. Every type has a number in the program, which a
/// union's tag holds for the case of that type (design record, section 5, round 19, "Program-wide type number"); the numbers are
/// given out as the emitter meets the types, those of the data types in a relation of inheritance first, and are seen by no form
/// of the language outside <c>raw</c>. A data type in no relation of inheritance that holds no value of its own type is a plain
/// value, the structure of its fields (section 4: small data values are plain values without counts); a data type with a base or
/// with a type derived from it, and one that holds a value of its own type through its fields, which no structure of a fixed size
/// holds, is a pointer to a block that starts with the number of the value's own type, then its fields, the base's first (D7), so
/// that the block of a derived value reads as a block of its base (D6). A union is its tag, as wide as the largest number among its
/// cases needs, a byte while those numbers are below 256 (section 5), then room for its largest case, aligned as its most aligned
/// case; a <c>T?</c> of a list or of a block is the address alone, with an address no value of <c>T</c> has as its <c>null</c>
/// (<see cref="Niche"/>).
/// </summary>
internal sealed class Layout(IrModule module)
{
    private readonly List<(KurzType Type, int Number)> numbers = [];
    private readonly List<DataSymbol> related = [];
    private readonly List<DataSymbol> recursive = [];
    private readonly List<DataSymbol> blocks = [];
    private readonly HashSet<string> defined = new(StringComparer.Ordinal);
    private readonly List<KurzType> walking = [];

    /// <summary>
    /// The data types of the module, read before any is laid out: those in a relation of inheritance and those that reach
    /// themselves through the types of their fields are blocks, and get their numbers first.
    /// </summary>
    public void Register(IEnumerable<DataSymbol> symbols)
    {
        var all = symbols.ToList();
        foreach (var d in all)
        {
            if (d.Base is not null || all.Any(other => ReferenceEquals(other.Base, d)))
            {
                related.Add(d);
            }

            if (Reaches(d, d, []))
            {
                recursive.Add(d);
            }
        }

        blocks.AddRange(all.Where(d => related.Contains(d) || recursive.Contains(d)));
        foreach (var d in blocks)
        {
            if (!d.IsGeneric)
            {
                Number(new DataType(d, []));
            }
        }
    }

    /// <summary>
    /// Whether a value of <paramref name="from"/> holds one of <paramref name="target"/> through the types of its fields, unions and
    /// the type arguments of a generic type among them, read as written, so that a type argument counts whether or not a field
    /// holds it.
    /// </summary>
    private static bool Reaches(DataSymbol from, DataSymbol target, HashSet<DataSymbol> seen)
    {
        if (!seen.Add(from))
        {
            return false;
        }

        foreach (var symbol in from.Fields.SelectMany(f => Held(f.Type)))
        {
            if (ReferenceEquals(symbol, target) || Reaches(symbol, target, seen))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<DataSymbol> Held(KurzType type) => type switch
    {
        DataType d => d.TypeArguments.SelectMany(Held).Prepend(d.Symbol),
        UnionType u => u.Cases.SelectMany(Held),
        _ => [],
    };

    /// <summary>The number of a type in this program, given out on first use.</summary>
    public int Number(KurzType type)
    {
        foreach (var (t, n) in numbers)
        {
            if (Types.Same(t, type))
            {
                return n;
            }
        }

        numbers.Add((type, numbers.Count));
        return numbers.Count - 1;
    }

    /// <summary>Whether a value of the data type is a pointer to a block: the type has a base or a type derived from it, or holds a value of its own type.</summary>
    public bool IsBlock(DataType type) => blocks.Contains(type.Symbol);

    /// <summary>The data type and every data type derived from it, directly or through others: the types a block of it may hold.</summary>
    public IReadOnlyList<DataSymbol> Derived(DataSymbol symbol) => blocks.Where(d => Types.HasBaseOrIs(d, symbol)).ToList();

    /// <summary>The types of a data type's fields, the base's first (D7), with its type arguments put in (T19).</summary>
    public static IReadOnlyList<KurzType> Fields(DataType type)
    {
        if (type.TypeArguments.Count == 0)
        {
            return type.Symbol.Fields.Select(f => f.Type).ToList();
        }

        var map = type.Symbol.TypeParameters.Zip(type.TypeArguments).ToDictionary(p => p.First, p => p.Second);
        return type.Symbol.Fields.Select(f => Types.Substitute(f.Type, map)).ToList();
    }

    public static int FieldIndex(DataType type, string name)
    {
        var index = type.Symbol.Fields.FindIndex(f => f.Name == name);
        return index >= 0 ? index : throw new InvalidOperationException($"`{type}` has no field `{name}`");
    }

    /// <summary>
    /// The <c>not supported</c> of the part of the type the emitter does not lay out, or null where it lays out all of it: what
    /// <see cref="Lower.Supported"/> takes, data whose fields it lays out, unless the type is generic and a block, unions of such
    /// cases, lists of such items and function types over such types. A type met again inside itself is read once.
    /// </summary>
    public string? Refusal(KurzType type)
    {
        if (walking.Any(t => Types.Same(t, type)))
        {
            return null;
        }

        walking.Add(type);
        try
        {
            return type switch
            {
                DataType d when d.Symbol.IsGeneric && related.Contains(d.Symbol) => $"a generic data type in a relation of inheritance, `{d}` (D6, T19)",
                DataType d when d.Symbol.IsGeneric && recursive.Contains(d.Symbol) => $"a generic data type that holds a value of its own type, `{d}` (D1, T19)",
                DataType d => Fields(d).Select(Refusal).FirstOrDefault(r => r is not null),
                UnionType u => u.Cases.Where(c => c is not VoidType).Select(Refusal).FirstOrDefault(r => r is not null),
                ListType l => Refusal(l.Element),
                MapType m => Refusal(m.Key) ?? Refusal(m.Value),
                FunctionType f => f.Parameters.Append(f.Result).Where(t => t is not VoidType).Select(Refusal).FirstOrDefault(r => r is not null),
                _ => Lower.Supported(type) ? null : Lower.Refusal(type),
            };
        }
        finally
        {
            walking.RemoveAt(walking.Count - 1);
        }
    }

    /// <summary>
    /// The <c>null</c> of a <c>T?</c> whose <c>T</c> is held through an address, an address no value of <c>T</c> has, as Rust lays out
    /// <c>Option&lt;Box&lt;T&gt;&gt;</c> and <c>Option&lt;Vec&lt;T&gt;&gt;</c> (section 4, the owner, 2026-10-30, "Rust's niche"): a block's is the
    /// address 0, and a list's the address 1, since a list without items is the address 0; a C function's is the address 0, which no
    /// function has, as Rust lays out <c>Option&lt;extern "C" fn()&gt;</c> (R17); a function value's is the pair of no code and no
    /// environment, the address 0 in its code, which no function value has, as Rust lays out <c>Option&lt;fn()&gt;</c> (section 4, the
    /// owner, 2026-10-30, "Nullable form, as Option", whose cost names "a <c>null</c> niche in a function value"; that the niche is the
    /// code address 0 is the record's assumption there, as Rust's). Null for every other union, which keeps its tag.
    /// </summary>
    public string? Niche(UnionType union) => Types.NonNull(union) switch
    {
        ListType => "inttoptr (i64 1 to ptr)",
        DataType d when IsBlock(d) => "null",
        CFunctionType => "null",
        FunctionType => "zeroinitializer",
        _ => null,
    };

    /// <summary>The IR type of a union with a <see cref="Niche"/>: the function value's pair for a nullable function value, else an address.</summary>
    public static string NicheIr(UnionType union) => Types.NonNull(union) is FunctionType ? "%kz.fn" : "ptr";

    /// <summary>The IR type of a value: <see cref="Lower.IrType"/>, a data type's structure or <c>ptr</c> for a block, and a union's structure of its tag and its room, or <c>ptr</c> where it has a <see cref="Niche"/>.</summary>
    public string Ir(KurzType type)
    {
        switch (type)
        {
            case DataType d when IsBlock(d):
                return "ptr";
            case UnionType u when Niche(u) is not null:
                return NicheIr(u);
            case DataType d:
                {
                    var name = $"%\"d.{Lower.Mangle(d)}\"";
                    if (defined.Add(name))
                    {
                        module.Global($"{name} = type {{ {string.Join(", ", Fields(d).Select(Ir))} }}");
                    }

                    return name;
                }

            case UnionType u:
                {
                    var (size, align) = Room(u);
                    return $"{{ i{TagBits(u).ToString(CultureInfo.InvariantCulture)}, [{(size / align).ToString(CultureInfo.InvariantCulture)} x i{(align * 8).ToString(CultureInfo.InvariantCulture)}] }}";
                }

            default:
                return Lower.IrType(type);
        }
    }

    /// <summary>The IR type of the block of a data type in a relation of inheritance: the number of the value's type, then the fields.</summary>
    public string Block(DataSymbol symbol)
    {
        var type = new DataType(symbol, []);
        var name = $"%\"b.{Lower.Mangle(type)}\"";
        if (defined.Add(name))
        {
            module.Global($"{name} = type {{ i32{string.Concat(Fields(type).Select(f => ", " + Ir(f)))} }}");
        }

        return name;
    }

    /// <summary>The width of a union's tag in bits: what the largest number among its cases needs.</summary>
    public int TagBits(UnionType union)
    {
        var largest = union.Cases.Max(Number);
        return largest < 1 << 8 ? 8 : largest < 1 << 16 ? 16 : 32;
    }

    /// <summary>The bytes a value of the type takes and its alignment, as LLVM lays out the IR type.</summary>
    public (int Size, int Align) Measure(KurzType type)
    {
        switch (type)
        {
            case VoidType:
                return (0, 1);
            case DataType d when IsBlock(d):
                return (8, 8);
            case UnionType u when Niche(u) is not null:
                return Types.NonNull(u) is FunctionType ? (16, 8) : (8, 8);
            case DataType d:
                return Structure(Fields(d).Select(Measure));
            case UnionType u:
                {
                    var tag = TagBits(u) / 8;
                    var room = Room(u);
                    return Structure([(tag, tag), room]);
                }

            default:
                return (Lower.Size(type), Lower.Align(type));
        }
    }

    /// <summary>The room of a union: the size of its largest case rounded up to the alignment of its most aligned case, and that alignment.</summary>
    private (int Size, int Align) Room(UnionType union)
    {
        var cases = union.Cases.Select(Measure).ToList();
        var align = cases.Max(c => c.Align);
        var size = cases.Max(c => c.Size);
        return (RoundUp(size, align), align);
    }

    /// <summary>A structure of these members in order, each at the next offset its alignment allows, its size rounded up to its largest alignment.</summary>
    private static (int Size, int Align) Structure(IEnumerable<(int Size, int Align)> members)
    {
        var offset = 0;
        var align = 1;
        foreach (var (size, a) in members)
        {
            offset = RoundUp(offset, a) + size;
            align = Math.Max(align, a);
        }

        return (RoundUp(offset, align), align);
    }

    private static int RoundUp(int value, int align) => (value + align - 1) / align * align;
}
