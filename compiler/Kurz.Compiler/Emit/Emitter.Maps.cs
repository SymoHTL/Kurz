using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;
using VoidType = Kurz.Compiler.Semantics.VoidType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// M9: a map is the address of its block, null for a map that never took one (<c>Clear</c> and the <c>Remove</c> of the last entry
/// leave an empty map that holds its block); the block's functions are the standard library's (<c>std/std.kz</c>), a control byte of
/// it says whether a slot holds an entry, 255 where it holds none and a probe ends at its group, never filled or emptied in a group
/// that holds a 255 still, and 128 where its entry was removed in a group with no 255, which a probe passes, and a slot holds an
/// entry as <c>{ i64, K, V }</c>, the hash of its key, the key and the value. A
/// key's hash is SipHash-1-3 under the keys of the map's block: of a string's UTF-8 bytes, of a time of 128 bits its 16 bytes, of a
/// <c>double</c> or a <c>float</c> the bits of its <c>double</c>, every NaN as one NaN and -0.0 as 0.0, of any other key of one value
/// widened to 64 bits (<see cref="Word"/>), and of the hashes of a <c>data</c> value's fields, of a list's items or of a union's tag and value, as words.
/// </summary>
public sealed partial class Emitter
{
    // the IR types of the entries defined in the module
    private readonly HashSet<string> entries = new(StringComparer.Ordinal);

    /// <summary>The IR type of an entry in a slot of the map's block.</summary>
    private string EntryType(MapType type)
    {
        var name = $"%\"entry.{Lower.Mangle(type)}\"";
        if (entries.Add(name))
        {
            module.Global($"{name} = type {{ i64, {Ir(type.Key)}, {Ir(type.Value)} }}");
        }

        return name;
    }

    /// <summary>The bytes a slot of the map's block takes, as LLVM lays out its entry.</summary>
    private string EntrySize(MapType type)
    {
        var end = fn.Value($"getelementptr {EntryType(type)}, ptr null, i32 1");
        return fn.Value($"ptrtoint ptr {end} to i64");
    }

    /// <summary>
    /// The hash of a key under the keys of the map's block: of a string's bytes, of a time of 128 bits' 16 bytes, of any other value of
    /// one word's 8 bytes, and of a <c>data</c> value, a list or a union made of words by <see cref="HashBody"/>, so that two keys equal
    /// by <see cref="Equal"/> have one hash (D3).
    /// </summary>
    private string KeyHash(string block, string key, KurzType type)
    {
        switch (type)
        {
            case PrimitiveType { Kind: Primitive.String }:
                {
                    var bytes = fn.Value($"extractvalue %kz.str {key}, 0");
                    var count = fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {key}, 1")}, 9223372036854775807"); // without the mark of a block
                    return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {bytes}, i64 {count})");
                }

            case DataType or ListType or UnionType:
                {
                    var name = Helper($"hash.{Lower.Mangle(type)}", "i64", ["ptr", Ir(type)], () => HashBody(type));
                    return fn.Value($"call i64 {name}(ptr {block}, {Ir(type)} {key})");
                }

            case PrimitiveType p when Types.IsFloating(p):
                {
                    // section 4, the owner, 2026-10-30, "As C#": a key compares as C#'s `double.Equals`, NaN equal to NaN and -0.0 to 0.0
                    // (Equal), so every NaN hashes as the one NaN and -0.0 as 0.0, as .NET's `double.GetHashCode` does
                    var wide = p.Kind == Primitive.Double ? key : fn.Value($"fpext float {key} to double");
                    var bits = fn.Value($"bitcast double {wide} to i64");
                    var zero = fn.Value($"select i1 {fn.Value($"fcmp oeq double {wide}, 0.0")}, i64 0, i64 {bits}");
                    var one = fn.Value($"select i1 {fn.Value($"fcmp uno double {wide}, {wide}")}, i64 9221120237041090560, i64 {zero}");
                    var slot = fn.Alloca("i64", "key");
                    fn.Emit($"store i64 {one}, ptr {slot}");
                    return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {slot}, i64 8)");
                }

            case PrimitiveType p when Ir(p) == "i128":
                {
                    var wide = fn.Alloca("i128", "key");
                    fn.Emit($"store i128 {key}, ptr {wide}");
                    return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {wide}, i64 16)");
                }

            default:
                {
                    var slot = fn.Alloca("i64", "key");
                    fn.Emit($"store i64 {Word(key, type)}, ptr {slot}");
                    return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {slot}, i64 8)");
                }
        }
    }

    /// <summary>A key of one word as an <c>i64</c>: a signed integer widened by its sign, an unsigned one, a <c>bool</c>, a <c>char</c> and an enum's value by zeros.</summary>
    private string Word(string value, KurzType type) => type switch
    {
        _ when Ir(type) == "i64" => value,
        PrimitiveType { Kind: Primitive.Bool or Primitive.Char } or EnumType => fn.Value($"zext {Ir(type)} {value} to i64"),
        _ => Resize(value, type, Types.IsSigned(type) ? Types.Long : Types.ULong),
    };

    /// <summary>
    /// The helper of the hash of a <c>data</c> value, a list or a union, the map's block in <c>%a0</c> and the value in <c>%a1</c>: the
    /// SipHash-1-3 of words, one for each field's hash, each item's hash, or the union's tag and its value's hash, and first the number of
    /// the type a block holds, as <see cref="EqualBody"/> compares them (D3, D15).
    /// </summary>
    private void HashBody(KurzType type)
    {
        var state = fn.Alloca("[5 x i64]", "hash");
        fn.Emit($"call void {StdFunction("HashStart")}(ptr %a0, ptr {state})");
        void Add(string word) => fn.Emit($"call void {StdFunction("HashWord")}(ptr {state}, i64 {word})");
        string End() => fn.Value($"call i64 {StdFunction("HashEnd")}(ptr {state})");
        switch (type)
        {
            case ListType list:
                EachItem("%a1", list.Element, item => Add(KeyHash("%a0", item, list.Element)));
                break;
            case UnionType union:
                {
                    var tag = Tag("%a1", union);
                    var bits = layout.TagBits(union);
                    Add(bits == 64 ? tag : fn.Value($"zext i{Number(bits)} {tag} to i64"));
                    ByCase(tag, union, union.Cases, c =>
                    {
                        if (c is not (NullType or VoidType))
                        {
                            Add(KeyHash("%a0", Payload("%a1", union, c), c));
                        }

                        fn.Return($"i64 {End()}");
                    });
                    return;
                }

            case DataType d when layout.IsBlock(d):
                {
                    Add(fn.Value($"zext i32 {fn.Value("load i32, ptr %a1")} to i64"));
                    ByNumber("%a1", d.Symbol, held =>
                    {
                        var block = layout.Block(held);
                        var types = Layout.Fields(new DataType(held, []));
                        for (var i = 0; i < types.Count; i++)
                        {
                            Add(KeyHash("%a0", fn.Value($"load {Ir(types[i])}, ptr {FieldPointer(block, "%a1", i)}"), types[i]));
                        }

                        fn.Return($"i64 {End()}");
                    });
                    return;
                }

            case DataType d:
                {
                    var types = Layout.Fields(d);
                    for (var i = 0; i < types.Count; i++)
                    {
                        Add(KeyHash("%a0", fn.Value($"extractvalue {Ir(d)} %a1, {Number(i)}"), types[i]));
                    }

                    break;
                }
        }

        fn.Return($"i64 {End()}");
    }

    /// <summary>
    /// The helper that finds the slot of a key's entry in a map's block, <c>%a0</c>, by the key, <c>%a1</c>, and its hash, <c>%a2</c>:
    /// the slot's address, or null where the map has no entry of the key. It reads a group's 8 control bytes as one word and marks
    /// the bytes equal to the hash's control byte as hashbrown's generic group does (a byte after a match can be marked too, which
    /// the comparison of the hash and the key settles); a group with a slot of 255, which holds no entry, ends the probe, and one of
    /// 128, whose entry was removed, does not.
    /// </summary>
    private string FindEntry(MapType type) => Helper($"find.{Lower.Mangle(type)}", "ptr", ["ptr", Ir(type.Key), "i64"], () =>
    {
        const string ones = "72340172838076673"; // 0x0101010101010101
        const string highs = "-9187201950435737472"; // 0x8080808080808080
        module.Declare("declare i64 @llvm.cttz.i64(i64, i1)");
        var entry = EntryType(type);
        var mask = fn.Value($"sub i64 {fn.Value($"sext i32 {fn.Value($"call i32 {StdFunction("MapRoom")}(ptr %a0)")} to i64")}, 1");
        var control = fn.Value($"call ptr {StdFunction("MapControl")}(ptr %a0)");
        var slots = fn.Value($"call ptr {StdFunction("MapSlots")}(ptr %a0)");
        var pattern = fn.Value($"mul i64 {fn.Value($"and i64 {fn.Value("lshr i64 %a2, 56")}, 127")}, {ones}");
        var at = fn.Alloca("i64", "group");
        var step = fn.Alloca("i64", "step");
        var marks = fn.Alloca("i64", "marks");
        fn.Emit($"store i64 {fn.Value($"and i64 {fn.Value($"and i64 %a2, {mask}")}, -8")}, ptr {at}");
        fn.Emit($"store i64 0, ptr {step}");
        var group = fn.Label("group");
        var check = fn.Label("marks");
        var mark = fn.Label("mark");
        var same = fn.Label("same.hash");
        var after = fn.Label("marks.done");
        var next = fn.Label("next");
        var none = fn.Label("none");
        fn.Branch(group);
        fn.Block(group);
        var position = fn.Value($"load i64, ptr {at}");
        var word = fn.Value($"load i64, ptr {fn.Value($"getelementptr i8, ptr {control}, i64 {position}")}");
        var differ = fn.Value($"xor i64 {word}, {pattern}");
        var zeros = fn.Value($"and i64 {fn.Value($"and i64 {fn.Value($"sub i64 {differ}, {ones}")}, {fn.Value($"xor i64 {differ}, -1")}")}, {highs}");
        fn.Emit($"store i64 {zeros}, ptr {marks}");
        fn.Branch(check);
        fn.Block(check);
        var left = fn.Value($"load i64, ptr {marks}");
        fn.CondBranch(fn.Value($"icmp eq i64 {left}, 0"), after, mark);
        fn.Block(mark);
        var i = fn.Value($"add i64 {position}, {fn.Value($"lshr i64 {fn.Value($"call i64 @llvm.cttz.i64(i64 {left}, i1 true)")}, 3")}");
        fn.Emit($"store i64 {fn.Value($"and i64 {left}, {fn.Value($"sub i64 {left}, 1")}")}, ptr {marks}");
        var slot = fn.Value($"getelementptr {entry}, ptr {slots}, i64 {i}");
        fn.CondBranch(fn.Value($"icmp eq i64 {fn.Value($"load i64, ptr {slot}")}, %a2"), same, check);
        fn.Block(same);
        var key = fn.Value($"load {Ir(type.Key)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 1")}");
        var equal = Equal(key, "%a1", type.Key, 0);
        var hit = fn.Label("hit");
        fn.CondBranch(equal, hit, check);
        fn.Block(hit);
        fn.Return($"ptr {slot}");
        fn.Block(after);
        var empty = fn.Value($"and i64 {fn.Value($"and i64 {word}, {fn.Value($"shl i64 {word}, 1")}")}, {highs}"); // the bytes of 255, as hashbrown's `match_empty`: 128 has bit 6 clear
        fn.CondBranch(fn.Value($"icmp ne i64 {empty}, 0"), none, next); // a slot of 255 ends the probe
        fn.Block(next);
        var stepped = fn.Value($"add i64 {fn.Value($"load i64, ptr {step}")}, 8");
        fn.Emit($"store i64 {stepped}, ptr {step}");
        fn.Emit($"store i64 {fn.Value($"and i64 {fn.Value($"add i64 {position}, {stepped}")}, {mask}")}, ptr {at}"); // the last group followed by the first
        fn.Branch(group);
        fn.Block(none);
        fn.Return("ptr null");
    });

    /// <summary><paramref name="each"/> for the key and the value of every entry of the map's block, in the order of its slots (M10).</summary>
    private void EachEntry(string block, MapType type, Action<string, string> each)
    {
        var entry = EntryType(type);
        var room = fn.Value($"sext i32 {fn.Value($"call i32 {StdFunction("MapRoom")}(ptr {block})")} to i64");
        var control = fn.Value($"call ptr {StdFunction("MapControl")}(ptr {block})");
        var slots = fn.Value($"call ptr {StdFunction("MapSlots")}(ptr {block})");
        var counter = fn.Alloca("i64", "slot");
        fn.Emit($"store i64 0, ptr {counter}");
        var check = fn.Label("slots");
        var body = fn.Label("slot");
        var held = fn.Label("entry");
        var next = fn.Label("slot.next");
        var done = fn.Label("slots.done");
        fn.Block(check);
        var i = fn.Value($"load i64, ptr {counter}");
        fn.CondBranch(fn.Value($"icmp slt i64 {i}, {room}"), body, done);
        fn.Block(body);
        var mark = fn.Value($"load i8, ptr {fn.Value($"getelementptr i8, ptr {control}, i64 {i}")}");
        fn.CondBranch(fn.Value($"icmp sge i8 {mark}, 0"), held, next); // 255 and 128 hold none
        fn.Block(held);
        var slot = fn.Value($"getelementptr {entry}, ptr {slots}, i64 {i}");
        var key = fn.Value($"load {Ir(type.Key)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 1")}");
        var value = fn.Value($"load {Ir(type.Value)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 2")}");
        each(key, value);
        fn.Branch(next);
        fn.Block(next);
        fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {counter}");
        fn.Branch(check);
        fn.Block(done);
    }

    /// <summary>
    /// M9: <c>map[key]</c>, the value of the key's entry or <c>null</c>, a value of its own that the caller makes a temporary of: the
    /// entry's value counted once more, since the map keeps its count, so that nothing that changes the map afterwards reaches it. The
    /// map is read before its key and held without a count of its own while the key is made: a key that changes the map,
    /// <c>m[F(mut m)]</c> or <c>m[m.Remove(k) ?? d]</c>, is the checker's <c>changed-while-held</c> (M7; <c>CheckerTests</c>).
    /// </summary>
    private string EmitMapRead(IndexExpression index, MapType type)
    {
        var map = EmitExpression(index.Target);
        var key = Convert(EmitExpression(index.Index), TypeOf(index.Index), type.Key, index.Index.Line);
        var result = Types.Nullable(type.Value);
        var get = Helper($"get.{Lower.Mangle(type)}", Ir(result), ["ptr", Ir(type.Key)], () =>
        {
            var entry = EntryType(type);
            var look = fn.Label("look");
            var absent = fn.Label("absent");
            var present = fn.Label("present");
            fn.CondBranch(fn.Value("icmp eq ptr %a0, null"), absent, look);
            fn.Block(look);
            var slot = fn.Value($"call ptr {FindEntry(type)}(ptr %a0, {Ir(type.Key)} %a1, i64 {KeyHash("%a0", "%a1", type.Key)})");
            fn.CondBranch(fn.Value($"icmp eq ptr {slot}, null"), absent, present);
            fn.Block(present);
            var value = fn.Value($"load {Ir(type.Value)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 2")}");
            fn.Return($"{Ir(result)} {AsResult(result, type.Value, Retained(value, type.Value), index.Line)}");
            fn.Block(absent);
            fn.Return($"{Ir(result)} {MakeUnion(result, Types.Null, "zeroinitializer")}");
        });
        return Temp(fn.Value($"call {Ir(result)} {get}(ptr {map}, {Ir(type.Key)} {key})"), result);
    }

    /// <summary>
    /// An entry's value as the result of a read or a <c>Remove</c>: a value of the nullable type (<see cref="WithNull"/>), a union's cases
    /// among its cases (section 4, the owner, 2026-10-30, "Nullable form, as Option"), or as it is where the values are nullable (section
    /// 4, the owner, 2026-10-30, "Flatten, as C#"). <paramref name="line"/> is the line of the read or the <c>Remove</c> that builds the
    /// helper of its map's type, the first of them, which a <c>not supported</c> of the conversion names.
    /// </summary>
    private string AsResult(UnionType result, KurzType value, string v, int line) => Types.Same(result, value) ? v : WithNull(result, value, v, line);

    /// <summary>
    /// M9, M4: <c>map[key] = value</c> on a <c>mut</c> variable or parameter, or on a path of fields from one: the value first, then
    /// the key, then the map the variable holds as they left it, as a path assignment reads its value first (M4). The map takes both
    /// over: a block other values hold is copied before the write (M3), the entry of the key takes the value and lets go of its old
    /// one and of the key given, and a new entry takes both, in the first slot of its probe without an entry, in a block made anew
    /// where that slot is of 255 and the block has no room left.
    /// </summary>
    private void EmitMapWrite(Assignment a, IndexExpression index, MapType type)
    {
        string value = null!;
        string key = null!;
        InRegion(() =>
        {
            value = Convert(Owned(a.Value), TypeOf(a.Value), type.Value, a.Value.Line);
            key = Convert(Owned(index.Index), TypeOf(index.Index), type.Key, index.Index.Line);
        });
        var place = CollectionPlace(index.Target);
        var set = Helper($"set.{Lower.Mangle(type)}", "void", ["ptr", Ir(type.Key), Ir(type.Value)], () => SetBody(type));
        fn.Emit($"call void {set}(ptr {place}, {Ir(type.Key)} {key}, {Ir(type.Value)} {value})");
    }

    /// <summary>The helper of <c>map[key] = value</c>: the place of the map in <c>%a0</c>, the key in <c>%a1</c> and the value in <c>%a2</c>, both of which the map takes over.</summary>
    private void SetBody(MapType type)
    {
        var entry = EntryType(type);
        var size = EntrySize(type);
        var asked = fn.Alloca("i64", "asked");
        fn.Emit($"store i64 0, ptr {asked}");
        var block = fn.Value("load ptr, ptr %a0");
        var first = fn.Label("first");
        var held = fn.Label("held");
        var copy = fn.Label("copy");
        var ready = fn.Label("ready");
        var failed = fn.Label("failed");
        fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), first, held);
        fn.Block(first);
        var keyed = fn.Label("keyed");
        var random = fn.Label("random");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("MapKeys")}()"), keyed, random); // false where the system gave no randomness (M9)
        fn.Block(random);
        fn.Emit($"call void {RuntimeFunction("RandomFailed")}()");
        fn.Unreachable();
        fn.Block(keyed);
        var made = fn.Value($"call ptr {StdFunction("MapNew")}(i64 {size}, ptr {asked})");
        var keepMade = fn.Label("made");
        fn.CondBranch(fn.Value($"icmp eq ptr {made}, null"), failed, keepMade);
        fn.Block(keepMade);
        fn.Emit($"store ptr {made}, ptr %a0");
        fn.Branch(ready);
        fn.Block(held);
        fn.CondBranch(fn.Value($"call i1 {StdFunction("MapShared")}(ptr {block})"), copy, ready);
        fn.Block(copy);
        var copied = fn.Value($"call ptr {StdFunction("MapCopy")}(ptr {block}, i64 {size}, ptr {asked})"); // which lowers the old block's count
        var keepCopy = fn.Label("copied");
        fn.CondBranch(fn.Value($"icmp eq ptr {copied}, null"), failed, keepCopy);
        fn.Block(keepCopy);
        if (Counted(type.Key) || Counted(type.Value))
        {
            EachEntry(copied, type, (k, v) =>
            {
                // the copy holds the entries' blocks too
                if (Counted(type.Key))
                {
                    Retain(k, type.Key);
                }

                if (Counted(type.Value))
                {
                    Retain(v, type.Value);
                }
            });
        }

        fn.Emit($"store ptr {copied}, ptr %a0");
        fn.Branch(ready);
        fn.Block(ready);
        var current = fn.Value("load ptr, ptr %a0");
        var hash = KeyHash(current, "%a1", type.Key);
        var slot = fn.Value($"call ptr {FindEntry(type)}(ptr {current}, {Ir(type.Key)} %a1, i64 {hash})");
        var found = fn.Label("found");
        var absent = fn.Label("absent");
        fn.CondBranch(fn.Value($"icmp eq ptr {slot}, null"), absent, found);
        fn.Block(found);
        Store("%a2", type.Value, fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 2"));
        if (Counted(type.Key))
        {
            Release("%a1", type.Key); // the entry keeps its own key
        }

        fn.Return(null);
        fn.Block(absent);
        var grow = fn.Label("grow");
        var add = fn.Label("add");
        var at = fn.Alloca("ptr", "slot");
        var taken = fn.Value($"call ptr {StdFunction("MapEmpty")}(ptr {current}, i64 {hash}, i64 {size})"); // null where the block has no room left
        fn.Emit($"store ptr {taken}, ptr {at}");
        fn.CondBranch(fn.Value($"icmp eq ptr {taken}, null"), grow, add);
        fn.Block(grow);
        var grown = fn.Value($"call ptr {StdFunction("MapGrow")}(ptr {current}, i64 {size}, ptr {asked})");
        var keepGrown = fn.Label("grown");
        fn.CondBranch(fn.Value($"icmp eq ptr {grown}, null"), failed, keepGrown);
        fn.Block(keepGrown);
        fn.Emit($"store ptr {grown}, ptr %a0");
        fn.Emit($"store ptr {fn.Value($"call ptr {StdFunction("MapEmpty")}(ptr {grown}, i64 {hash}, i64 {size})")}, ptr {at}");
        fn.Branch(add);
        fn.Block(add);
        var target = fn.Value("load ptr, ptr %a0");
        var empty = fn.Value($"load ptr, ptr {at}");
        fn.Emit($"store i64 {hash}, ptr {empty}");
        fn.Emit($"store {Ir(type.Key)} %a1, ptr {fn.Value($"getelementptr {entry}, ptr {empty}, i32 0, i32 1")}");
        fn.Emit($"store {Ir(type.Value)} %a2, ptr {fn.Value($"getelementptr {entry}, ptr {empty}, i32 0, i32 2")}");
        fn.Emit($"call void {StdFunction("MapAdded")}(ptr {target})");
        fn.Return(null);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
    }

    /// <summary>
    /// The helper that makes the block in the place <c>%a0</c> one no other value holds, before a write that takes an entry out (M3): a
    /// block other values hold is copied, with a count more of each entry's own blocks, the old block held by one value less.
    /// </summary>
    private void OwnBlock(MapType type, string asked)
    {
        var block = fn.Value("load ptr, ptr %a0");
        var copy = fn.Label("copy");
        var own = fn.Label("own");
        var failed = fn.Label("failed");
        fn.CondBranch(fn.Value($"call i1 {StdFunction("MapShared")}(ptr {block})"), copy, own);
        fn.Block(copy);
        var copied = fn.Value($"call ptr {StdFunction("MapCopy")}(ptr {block}, i64 {EntrySize(type)}, ptr {asked})"); // which lowers the old block's count
        var keep = fn.Label("copied");
        fn.CondBranch(fn.Value($"icmp eq ptr {copied}, null"), failed, keep);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
        fn.Block(keep);
        if (Counted(type.Key) || Counted(type.Value))
        {
            EachEntry(copied, type, (k, v) =>
            {
                if (Counted(type.Key))
                {
                    Retain(k, type.Key);
                }

                if (Counted(type.Value))
                {
                    Retain(v, type.Value);
                }
            });
        }

        fn.Emit($"store ptr {copied}, ptr %a0");
        fn.Branch(own);
        fn.Block(own);
    }

    /// <summary>The map a <c>mut</c> method changes, and its receiver, a variable or a path of fields from one, whose place it changes (M5).</summary>
    private (MapType Type, Expression Receiver) MutMap(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        return ((MapType)TypeOf(member.Target), member.Target);
    }

    /// <summary>
    /// M9, M5: <c>map.Remove(key)</c> on a variable or a path of fields from one, the value of the key's entry, moved out of the map
    /// with the count the map held of it, or <c>null</c>
    /// where the map has no entry of the key, as Rust's <c>HashMap::remove</c> gives <c>Option&lt;V&gt;</c> (section 4, the owner,
    /// 2026-10-30, "C# names, Rust's Remove"). Only a map with the key's entry is written: a block other values hold is copied first
    /// (M3), the entry lets go of its own key, and its slot's control byte becomes 255 or 128 (<c>MapErase</c>, as hashbrown's
    /// <c>erase</c>). The key is made before the place and takes no count where it is read from a variable, a field or an item: what it
    /// was read from keeps it while the helper runs, since <c>CollectionPlace</c> copies a block on the path only where other values
    /// hold it (M3), and those keep the block the key was read from; <c>ListTests</c> holds <c>n.Counts.Remove(n.Name)</c>, the copy of a
    /// shared map block with a key read from a field, whose literal holds no block of its own.
    /// </summary>
    private string EmitMapRemove(CallExpression c)
    {
        var (type, receiver) = MutMap(c);
        var argument = c.Arguments[0].Value;
        var key = Convert(EmitExpression(argument), TypeOf(argument), type.Key, argument.Line);
        var result = Types.Nullable(type.Value);
        var remove = Helper($"remove.{Lower.Mangle(type)}", Ir(result), ["ptr", Ir(type.Key)], () =>
        {
            var entry = EntryType(type);
            var asked = fn.Alloca("i64", "asked");
            fn.Emit($"store i64 0, ptr {asked}");
            var block = fn.Value("load ptr, ptr %a0");
            var look = fn.Label("look");
            var found = fn.Label("found");
            var absent = fn.Label("absent");
            fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), absent, look);
            fn.Block(look);
            var hash = KeyHash(block, "%a1", type.Key);
            var slot = fn.Value($"call ptr {FindEntry(type)}(ptr {block}, {Ir(type.Key)} %a1, i64 {hash})");
            fn.CondBranch(fn.Value($"icmp eq ptr {slot}, null"), absent, found);
            fn.Block(found);
            OwnBlock(type, asked);
            var current = fn.Value("load ptr, ptr %a0");
            var own = fn.Value($"call ptr {FindEntry(type)}(ptr {current}, {Ir(type.Key)} %a1, i64 {hash})"); // the same slot of a copy
            var value = fn.Value($"load {Ir(type.Value)}, ptr {fn.Value($"getelementptr {entry}, ptr {own}, i32 0, i32 2")}");
            if (Counted(type.Key))
            {
                Release(fn.Value($"load {Ir(type.Key)}, ptr {fn.Value($"getelementptr {entry}, ptr {own}, i32 0, i32 1")}"), type.Key);
            }

            fn.Emit($"call void {StdFunction("MapErase")}(ptr {current}, ptr {own}, i64 {EntrySize(type)})");
            fn.Return($"{Ir(result)} {AsResult(result, type.Value, value, c.Line)}");
            fn.Block(absent);
            fn.Return($"{Ir(result)} {MakeUnion(result, Types.Null, "zeroinitializer")}");
        });
        return fn.Value($"call {Ir(result)} {remove}(ptr {CollectionPlace(receiver)}, {Ir(type.Key)} {key})");
    }

    /// <summary>
    /// M9: <c>map.ContainsKey(key)</c>, whether the map has an entry of the key, as C#'s <c>Dictionary.ContainsKey</c>. The map is read
    /// before its key and held without a count of its own while the key is made: a key that changes the map,
    /// <c>m.ContainsKey(F(mut m))</c>, is the checker's <c>changed-while-held</c> (M7; <c>CheckerTests</c>).
    /// </summary>
    private string EmitMapContainsKey(CallExpression c)
    {
        var member = (MemberAccess)Unwrap(c.Callee);
        var type = (MapType)TypeOf(member.Target);
        var map = EmitExpression(member.Target);
        var argument = c.Arguments[0].Value;
        var key = Convert(EmitExpression(argument), TypeOf(argument), type.Key, argument.Line);
        var contains = Helper($"contains.{Lower.Mangle(type)}", "i1", ["ptr", Ir(type.Key)], () =>
        {
            var look = fn.Label("look");
            var absent = fn.Label("absent");
            fn.CondBranch(fn.Value("icmp eq ptr %a0, null"), absent, look);
            fn.Block(look);
            var slot = fn.Value($"call ptr {FindEntry(type)}(ptr %a0, {Ir(type.Key)} %a1, i64 {KeyHash("%a0", "%a1", type.Key)})");
            fn.Return($"i1 {fn.Value($"icmp ne ptr {slot}, null")}");
            fn.Block(absent);
            fn.Return("i1 false");
        });
        return fn.Value($"call i1 {contains}(ptr {map}, {Ir(type.Key)} {key})");
    }

    /// <summary>
    /// M9, M5: <c>map.Clear()</c>, through the place of the map's variable or of the field a path from one ends at: a block other
    /// values hold is let go of, and the map holds none (M3), as a list's <c>Clear</c> does; one it alone holds lets go of its entries'
    /// own blocks and keeps its slots (<c>MapClear</c>).
    /// </summary>
    private string? EmitMapClear(CallExpression c)
    {
        var (type, receiver) = MutMap(c);
        var clear = Helper($"clear.{Lower.Mangle(type)}", "void", ["ptr"], () =>
        {
            var block = fn.Value("load ptr, ptr %a0");
            var held = fn.Label("held");
            var shared = fn.Label("shared");
            var own = fn.Label("own");
            var none = fn.Label("none");
            fn.CondBranch(fn.Value($"icmp eq ptr {block}, null"), none, held);
            fn.Block(held);
            fn.CondBranch(fn.Value($"call i1 {StdFunction("MapShared")}(ptr {block})"), shared, own);
            fn.Block(shared);
            Release(block, type);
            fn.Emit("store ptr null, ptr %a0");
            fn.Return(null);
            fn.Block(own);
            if (Counted(type.Key) || Counted(type.Value))
            {
                EachEntry(block, type, (k, v) =>
                {
                    if (Counted(type.Key))
                    {
                        Release(k, type.Key);
                    }

                    if (Counted(type.Value))
                    {
                        Release(v, type.Value);
                    }
                });
            }

            fn.Emit($"call void {StdFunction("MapClear")}(ptr {block})");
            fn.Return(null);
            fn.Block(none);
            fn.Return(null);
        });
        fn.Emit($"call void {clear}(ptr {CollectionPlace(receiver)})");
        return null;
    }

    /// <summary>
    /// C6, M10: <c>for e in map</c> and <c>for key, value in map</c> run the block for each entry, in the order of the map's slots,
    /// over the map as it was when the loop started: the loop holds the map's block, so that a write to the variable inside the block,
    /// <c>Remove</c> and <c>Clear</c> among them, copies it first or lets go of it (M3), as a loop over a list holds its block. The
    /// names are the entry's key and value, borrowed from that block, or the entry, a value of <c>Entry&lt;K, V&gt;</c> of the two
    /// (section 4, the owner, 2026-10-30, "A value, as C#"), borrowed as an item of a list is.
    /// </summary>
    private void EmitForInMap(ForInStatement f, MapType type)
    {
        var map = Owned(f.Collection);
        regions[^1].Add((map, type)); // the statement's end, a `return` or a `break` lets go of it
        var whole = f.Second is null ? (DataType)Model.DeclarationOf(f).Type : null;
        string key = null!;
        string value = null!;
        if (whole is null)
        {
            key = fn.Alloca(Ir(type.Key), f.Variable);
            value = fn.Alloca(Ir(type.Value), f.Second!.Name);
            slots[Model.DeclarationOf(f)] = key;
            slots[Model.DeclarationOf(f.Second)] = value;
        }
        else if (layout.IsBlock(whole))
        {
            // the built-in `Entry` is declared by no unit, so the layout never makes it a block, whatever its type arguments hold
            throw new InvalidOperationException($"a `{whole}` laid out as a block (C6)");
        }
        else
        {
            slots[Model.DeclarationOf(f)] = fn.Alloca(Ir(whole), f.Variable);
        }

        var entry = EntryType(type);
        var walk = fn.Label("walk");
        var check = fn.Label("check");
        var mark = fn.Label("slot");
        var body = fn.Label("for");
        var onward = fn.Label("onward");
        var exit = fn.Label("endfor");
        fn.CondBranch(fn.Value($"icmp eq ptr {map}, null"), exit, walk);
        fn.Block(walk);
        var room = fn.Value($"sext i32 {fn.Value($"call i32 {StdFunction("MapRoom")}(ptr {map})")} to i64");
        var control = fn.Value($"call ptr {StdFunction("MapControl")}(ptr {map})");
        var entries = fn.Value($"call ptr {StdFunction("MapSlots")}(ptr {map})");
        var counter = fn.Alloca("i64", "slot");
        fn.Emit($"store i64 0, ptr {counter}");
        fn.Block(check);
        var i = fn.Value($"load i64, ptr {counter}");
        fn.CondBranch(fn.Value($"icmp slt i64 {i}, {room}"), mark, exit);
        fn.Block(mark);
        var held = fn.Value($"icmp sge i8 {fn.Value($"load i8, ptr {fn.Value($"getelementptr i8, ptr {control}, i64 {i}")}")}, 0"); // 255 and 128 hold none
        var full = fn.Label("entry");
        fn.CondBranch(held, full, onward);
        fn.Block(full);
        var slot = fn.Value($"getelementptr {entry}, ptr {entries}, i64 {i}");
        var k = fn.Value($"load {Ir(type.Key)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 1")}");
        var v = fn.Value($"load {Ir(type.Value)}, ptr {fn.Value($"getelementptr {entry}, ptr {slot}, i32 0, i32 2")}");
        if (whole is null)
        {
            fn.Emit($"store {Ir(type.Key)} {k}, ptr {key}");
            fn.Emit($"store {Ir(type.Value)} {v}, ptr {value}");
        }
        else
        {
            var pair = fn.Value($"insertvalue {Ir(whole)} {fn.Value($"insertvalue {Ir(whole)} poison, {Ir(type.Key)} {k}, 0")}, {Ir(type.Value)} {v}, 1");
            fn.Emit($"store {Ir(whole)} {pair}, ptr {slots[Model.DeclarationOf(f)]}");
        }
        fn.Branch(body);
        fn.Block(body);
        loops.Push((exit, onward, regions.Count, scopes.Count));
        EmitBlock(f.Body);
        loops.Pop();
        fn.Block(onward);
        fn.Emit($"store i64 {fn.Value($"add i64 {fn.Value($"load i64, ptr {counter}")}, 1")}, ptr {counter}");
        fn.Branch(check);
        fn.Block(exit);
    }

    /// <summary>The helper that lets go of one holder of a map's block: the last lets go of its entries' own blocks and frees it.</summary>
    private void MapReleaseBody(MapType type)
    {
        var last = fn.Value($"call i1 {StdFunction("MapRelease")}(ptr %a0)");
        var free = fn.Label("free");
        var kept = fn.Label("kept");
        fn.CondBranch(last, free, kept);
        fn.Block(free);
        if (Counted(type.Key) || Counted(type.Value))
        {
            EachEntry("%a0", type, (k, v) =>
            {
                if (Counted(type.Key))
                {
                    Release(k, type.Key);
                }

                if (Counted(type.Value))
                {
                    Release(v, type.Value);
                }
            });
        }

        fn.Emit($"call void {StdFunction("MapFree")}(ptr %a0, i64 {EntrySize(type)})");
        fn.Return(null);
        fn.Block(kept);
        fn.Return(null);
    }

    /// <summary>
    /// A6: the text of a map, each entry as <c>key: value</c> between <c>[</c> and <c>]</c> with <c>, </c> between them, in the order
    /// of its slots (M10), joined by the runtime's <c>ListText</c> from a buffer of the entries' texts; <c>[]</c> without entries.
    /// </summary>
    private string MapText(string value, MapType type, int line)
    {
        var name = Helper($"text.{Lower.Mangle(type)}", "%kz.str", ["ptr"], () =>
        {
            var count = fn.Value($"call i32 {StdFunction("MapCount")}(ptr %a0)");
            var empty = fn.Label("empty");
            var texts = fn.Label("texts");
            fn.CondBranch(fn.Value($"icmp eq i32 {count}, 0"), empty, texts);
            fn.Block(empty);
            fn.Return($"%kz.str {StringValue("[]")}");
            fn.Block(texts);
            var wide = fn.Value($"sext i32 {count} to i64");
            var bytes = fn.Value($"mul i64 {wide}, 16");
            var buffer = fn.Value($"call ptr {RuntimeFunction("Allocate")}(i64 {bytes})");
            var at = fn.Alloca("i64", "at");
            fn.Emit($"store i64 0, ptr {at}");
            EachEntry("%a0", type, (k, v) =>
            {
                var keyText = TextOf(k, type.Key, line);
                var text = fn.Value($"call %kz.str {RuntimeFunction("Concat")}(%kz.str {keyText}, %kz.str {StringValue(": ")})");
                if (MakesText(type.Key))
                {
                    Release(keyText, Types.String);
                }

                var valueText = TextOf(v, type.Value, line);
                text = Joined(text, valueText);
                if (MakesText(type.Value))
                {
                    Release(valueText, Types.String);
                }

                var i = fn.Value($"load i64, ptr {at}");
                fn.Emit($"store %kz.str {text}, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {i}")}");
                fn.Emit($"store i64 {fn.Value($"add i64 {i}, 1")}, ptr {at}");
            });
            var joined = fn.Value($"call %kz.str {RuntimeFunction("ListText")}(ptr {buffer}, i64 {wide})");

            // the entries' texts, which the joined text holds no count of
            fn.Emit($"store i64 0, ptr {at}");
            var check = fn.Label("made");
            var each = fn.Label("made.text");
            var done = fn.Label("made.done");
            fn.Branch(check);
            fn.Block(check);
            var j = fn.Value($"load i64, ptr {at}");
            fn.CondBranch(fn.Value($"icmp slt i64 {j}, {wide}"), each, done);
            fn.Block(each);
            Release(fn.Value($"load %kz.str, ptr {fn.Value($"getelementptr %kz.str, ptr {buffer}, i64 {j}")}"), Types.String);
            fn.Emit($"store i64 {fn.Value($"add i64 {j}, 1")}, ptr {at}");
            fn.Branch(check);
            fn.Block(done);
            fn.Emit($"call void {StdFunction("Free")}(ptr {buffer}, i64 {bytes})");
            fn.Return($"%kz.str {joined}");
        });
        return fn.Value($"call %kz.str {name}(ptr {value})");
    }
}
