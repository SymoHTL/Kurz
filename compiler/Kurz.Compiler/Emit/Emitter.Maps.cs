using Kurz.Compiler.Semantics;
using Kurz.Compiler.Syntax;
using UnionType = Kurz.Compiler.Semantics.UnionType;

namespace Kurz.Compiler.Emit;

/// <summary>
/// M9: a map is the address of its block, null for a map without entries, which takes no block; the block's functions are the
/// standard library's (<c>std/std.kz</c>), a control byte of it says whether a slot holds an entry, 255 where it holds none, and a
/// slot holds an entry as <c>{ i64, K, V }</c>, the hash of its key, the key and the value. A key's hash is SipHash-1-3 under the
/// keys of the map's block, of a string's UTF-8 bytes and of any other key's value widened to 64 bits.
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

    /// <summary>The hash of a key under the keys of the map's block.</summary>
    private string KeyHash(string block, string key, KurzType type)
    {
        if (type is PrimitiveType { Kind: Primitive.String })
        {
            var bytes = fn.Value($"extractvalue %kz.str {key}, 0");
            var count = fn.Value($"and i64 {fn.Value($"extractvalue %kz.str {key}, 1")}, 9223372036854775807"); // without the mark of a block
            return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {bytes}, i64 {count})");
        }

        var wide = type is PrimitiveType { Kind: Primitive.Bool or Primitive.Char }
            ? fn.Value($"zext {Ir(type)} {key} to i64")
            : Resize(key, type, Types.IsSigned(type) ? Types.Long : Types.ULong);
        var slot = fn.Alloca("i64", "key");
        fn.Emit($"store i64 {wide}, ptr {slot}");
        return fn.Value($"call i64 {StdFunction("MapHash")}(ptr {block}, ptr {slot}, i64 8)");
    }

    /// <summary>
    /// The helper that finds the slot of a key's entry in a map's block, <c>%a0</c>, by the key, <c>%a1</c>, and its hash, <c>%a2</c>:
    /// the slot's address, or null where the map has no entry of the key. It reads a group's 8 control bytes as one word and marks
    /// the bytes equal to the hash's control byte as hashbrown's generic group does (a byte after a match can be marked too, which
    /// the comparison of the hash and the key settles); a group with a slot without an entry ends the probe.
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
        fn.CondBranch(fn.Value($"icmp ne i64 {fn.Value($"and i64 {word}, {highs}")}, 0"), none, next); // a slot without an entry ends the probe
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
        fn.CondBranch(fn.Value($"icmp ne i8 {mark}, -1"), held, next);
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
    /// entry's value counted once more, since the map keeps its count, so that nothing that changes the map afterwards reaches it.
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
            fn.Return($"{Ir(result)} {MakeUnion(result, type.Value, Retained(value, type.Value))}");
            fn.Block(absent);
            fn.Return($"{Ir(result)} {MakeUnion(result, Types.Null, "zeroinitializer")}");
        });
        return Temp(fn.Value($"call {Ir(result)} {get}(ptr {map}, {Ir(type.Key)} {key})"), result);
    }

    /// <summary>
    /// M9, M4: <c>map[key] = value</c> on a <c>mut</c> variable or parameter: the value first, then the key, then the map the variable
    /// holds as they left it, as a path assignment reads its value first (M4). The map takes both over: a block other values hold is
    /// copied before the write (M3), the entry of the key takes the value and lets go of its old one and of the key given, and a new
    /// entry takes both, in a block that grows where it is full.
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
        var place = NarrowedPlace((NameExpression)Unwrap(index.Target));
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
        fn.CondBranch(fn.Value($"call i1 {StdFunction("MapFull")}(ptr {current})"), grow, add);
        fn.Block(grow);
        var grown = fn.Value($"call ptr {StdFunction("MapGrow")}(ptr {current}, i64 {size}, ptr {asked})");
        var keepGrown = fn.Label("grown");
        fn.CondBranch(fn.Value($"icmp eq ptr {grown}, null"), failed, keepGrown);
        fn.Block(keepGrown);
        fn.Emit($"store ptr {grown}, ptr %a0");
        fn.Branch(add);
        fn.Block(add);
        var target = fn.Value("load ptr, ptr %a0");
        var empty = fn.Value($"call ptr {StdFunction("MapEmpty")}(ptr {target}, i64 {hash}, i64 {size})");
        fn.Emit($"store i64 {hash}, ptr {empty}");
        fn.Emit($"store {Ir(type.Key)} %a1, ptr {fn.Value($"getelementptr {entry}, ptr {empty}, i32 0, i32 1")}");
        fn.Emit($"store {Ir(type.Value)} %a2, ptr {fn.Value($"getelementptr {entry}, ptr {empty}, i32 0, i32 2")}");
        fn.Emit($"call void {StdFunction("MapAdded")}(ptr {target})");
        fn.Return(null);
        fn.Block(failed);
        fn.Emit($"call void {RuntimeFunction("AllocationFailed")}(i64 {fn.Value($"load i64, ptr {asked}")})");
        fn.Unreachable();
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
