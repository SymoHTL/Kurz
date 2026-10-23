using System.Globalization;
using Kurz.Compiler.Semantics;
using PointerType = Kurz.Compiler.Semantics.PointerType;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// The layout of the types of one program, which the whole program decides. Every type has a number in the program, which a
/// union's tag holds for the case of that type (design record, section 5, round 19, "Program-wide type number"); the numbers are
/// given out as the emitter meets the types, those of the data types in a relation of inheritance first, and are seen by no form
/// of the language outside <c>raw</c>. A data type in no relation of inheritance is a plain value, the structure of its fields
/// (section 4: small data values are plain values without counts); a data type with a base or with a type derived from it is a
/// pointer to a block that starts with the number of the value's own type, then its fields, the base's first (D7), so that the
/// block of a derived value reads as a block of its base (D6). A union is its tag, as wide as the largest number among its cases
/// needs, a byte while those numbers are below 256 (section 5), then room for its largest case, aligned as its most aligned case.
/// </summary>
internal sealed class Layout(IrModule module)
{
    private readonly List<(KurzType Type, int Number)> numbers = [];
    private readonly List<DataSymbol> related = [];
    private readonly HashSet<string> defined = new(StringComparer.Ordinal);

    /// <summary>The data types of the module, read before any is laid out: those in a relation of inheritance are blocks, and get their numbers first.</summary>
    public void Register(IEnumerable<DataSymbol> symbols)
    {
        var all = symbols.ToList();
        foreach (var d in all)
        {
            if (d.Base is not null || all.Any(other => ReferenceEquals(other.Base, d)))
            {
                related.Add(d);
            }
        }

        foreach (var d in related)
        {
            if (!d.IsGeneric)
            {
                Number(new DataType(d, []));
            }
        }
    }

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

    /// <summary>Whether a value of the data type is a pointer to a block: the type has a base or a type derived from it.</summary>
    public bool IsBlock(DataType type) => related.Contains(type.Symbol);

    /// <summary>The data type and every data type derived from it, directly or through others: the types a block of it may hold.</summary>
    public IReadOnlyList<DataSymbol> Derived(DataSymbol symbol) => related.Where(d => Types.HasBaseOrIs(d, symbol)).ToList();

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

    /// <summary>Whether the emitter lays the type out: <see cref="Lower.Supported"/>, data whose fields it lays out, unless the type is generic and in a relation of inheritance, and unions of such cases.</summary>
    public bool Supported(KurzType type) => type switch
    {
        DataType d => !(d.Symbol.IsGeneric && related.Contains(d.Symbol)) && Fields(d).All(Supported),
        UnionType u => u.Cases.All(c => c is VoidType || Supported(c)),
        _ => Lower.Supported(type),
    };

    /// <summary>The <c>not supported</c> of a type <see cref="Supported"/> refuses: the part of it the emitter does not lay out.</summary>
    public string Refusal(KurzType type) => type switch
    {
        DataType d when d.Symbol.IsGeneric && related.Contains(d.Symbol) => $"a generic data type in a relation of inheritance, `{d}` (D6, T19)",
        DataType d => Refusal(Fields(d).First(f => !Supported(f))),
        UnionType u => Refusal(u.Cases.First(c => c is not VoidType && !Supported(c))),
        _ => Lower.Refusal(type),
    };

    /// <summary>The IR type of a value: <see cref="Lower.IrType"/>, a data type's structure or <c>ptr</c> for a block, and a union's structure of its tag and its room.</summary>
    public string Ir(KurzType type)
    {
        switch (type)
        {
            case DataType d when IsBlock(d):
                return "ptr";
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
            case DataType d:
                return Structure(Fields(d).Select(Measure));
            case UnionType u:
                {
                    var tag = TagBits(u) / 8;
                    var room = Room(u);
                    return Structure([(tag, tag), room]);
                }

            default:
                {
                    var size = Lower.Size(type);
                    return (size, Math.Min(size, 8));
                }
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
